"""Space Engineers blueprint builder with real-definition validation and game-shaped XML.

Public API (backward compatible with tools/drones.py): Blueprint(name, grid, seed), .add(), .armor(),
.keep_clear(), .validate(), .write(outdir, display), .stats(), .thumb(path, title, subtitle, palette).
New: Blueprint.from_sbc(path) / load_sbc(path) (read any game-written blueprint), .attachment(),
.dlc_used(), .build_xml(), per-block `settings={}` and `links`, see docs/xml-notes.md.

MOUNT-POINT CONVENTION (verified against the game source and against the user's Starburst ship)
  Source: KeenSoftwareHouse/SpaceEngineers, Sandbox.Game/Definitions/MyCubeBlockDefinition.cs
  (m_mountPointTransforms / m_mountPointWallOffsets / SetMountPoints) and
  Sandbox.Game/Game/Entities/Cube/MyCubeGrid.Static.cs (CheckMountPointsForSide).
  Block-local box: x in [0,X] = block Right, y in [0,Y] = block Up, z in [0,Z] = block Backward
  (Forward = -Z, so the FRONT face is z=0). A <MountPoint Side StartX StartY EndX EndY> is a rectangle
  on that face with side-local coordinates (u, v):
    Front  z=0 : (X-u, v, 0)     Back   z=Z: (u, v, Z)
    Left   x=0 : (0, v, u)       Right  x=X: (X, v, Z-u)
    Top    y=Y : (u, Y, Z-v)     Bottom y=0: (u, 0, v)
  The rectangle is shrunk by 0.001 on each side. Two blocks are attached when a mount rectangle of A and
  one of B lie on the same plane with opposite normals and overlap, unless
  (A.excl & B.props or A.props & B.excl) AND the two blocks are different definitions.
  Blocks without a <MountPoints> element get every face fully covered; an empty element means none.
"""
import json, os, random, hashlib, struct, zlib, re
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
DEFS = json.load(open(os.path.join(HERE, '..', 'defs.json')))
try:
    SHAPES = json.load(open(os.path.join(HERE, 'shapes.json')))   # sanitized game-written block tails (tools/extract_shapes.py)
except (OSError, ValueError):
    SHAPES = {}
# the reference ship's antenna label ("CAMERA DRONE") must not leak into every generated antenna
for _v in SHAPES.values():
    if isinstance(_v, dict) and isinstance(_v.get('tail'), str) and '<HudText>' in _v['tail']:
        _v['tail'] = _v['tail'].replace('<HudText>CAMERA DRONE</HudText>', '<HudText />')
DIR = {"Forward": (0,0,-1), "Backward": (0,0,1), "Up": (0,1,0), "Down": (0,-1,0), "Left": (-1,0,0), "Right": (1,0,0)}
SHRINK = 0.001
XSI_NS = 'http://www.w3.org/2001/XMLSchema-instance'
XSI = '{%s}' % XSI_NS
CUSTOM_DATA_GUID = '74de02b3-27f9-4960-b1c4-27351f2b06d1'    # MyTerminalBlock.m_storageGuid: CustomData lives in ModStorage

def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def neg(a): return tuple(-v for v in a)

def defkey(xtype, sub):
    t = xtype.replace('MyObjectBuilder_', '')
    for k in (f'{t}/{sub}', f'MyObjectBuilder_{t}/{sub}'):
        if k in DEFS: return k
    return None

# =============================================================================================
# XML helpers (ElementTree based, serialized the way the game's XmlSerializer lays things out)
# =============================================================================================
def _qn(k):
    return 'xsi:' + k[len(XSI):] if k.startswith(XSI) else k

def ser(e, ind=0):
    pad = '  ' * ind
    attrs = ''.join(f' {_qn(k)}="{escape(v, {chr(34): "&quot;"})}"' for k, v in e.attrib.items())
    kids = list(e)
    if not kids:
        if not e.text: return f'{pad}<{e.tag}{attrs} />\n'
        return f'{pad}<{e.tag}{attrs}>{escape(e.text)}</{e.tag}>\n'
    return f'{pad}<{e.tag}{attrs}>\n' + ''.join(ser(k, ind + 1) for k in kids) + f'{pad}</{e.tag}>\n'

def frag(xml):
    """parse an XML fragment (zero or more sibling elements) -> list of Elements"""
    root = ET.fromstring(f'<r xmlns:xsi="{XSI_NS}">{xml}</r>')
    for e in root.iter():
        if len(e) and (e.text or '').strip() == '': e.text = None
        if e.tail is not None: e.tail = None
    return list(root)

def E(tag, text=None, **attrs):
    e = ET.Element(tag, {k: str(v) for k, v in attrs.items()})
    if text is not None: e.text = _txt(text)
    return e

def _txt(v):
    if isinstance(v, bool): return 'true' if v else 'false'
    if isinstance(v, float):
        t = ('%.6f' % v).rstrip('0').rstrip('.')
        return '0' if t in ('', '-0') else t
    return str(v)

EMPTY_TOOLBAR = '<Toolbar><ToolbarType>Character</ToolbarType><SelectedSlot xsi:nil="true" /><Slots /><SlotsGamepad /></Toolbar>'

# =============================================================================================
# Per-type serialized shape.  level: cube | term | func ; owner: block carries Owner/ShareMode.
# 'seen' = taken from Starburst (shapes.json), 'source' = from Keen's MyObjectBuilder_* source,
# 'guess' = no ground truth (see docs/xml-notes.md).
# =============================================================================================
def _sensor_tail():
    return ('<FieldMin x="-5" y="-5" z="-5" /><FieldMax x="5" y="5" z="5" />' + EMPTY_TOOLBAR +
            '<PlaySound>true</PlaySound><DetectPlayers>true</DetectPlayers><DetectFloatingObjects>false</DetectFloatingObjects>'
            '<DetectSmallShips>false</DetectSmallShips><DetectLargeShips>false</DetectLargeShips><DetectStations>false</DetectStations>'
            '<DetectSubgrids>false</DetectSubgrids><IsActive>false</IsActive><DetectAsteroids>false</DetectAsteroids>'
            '<DetectOwner>true</DetectOwner><DetectFriendly>true</DetectFriendly><DetectNeutral>true</DetectNeutral><DetectEnemy>true</DetectEnemy>')

HAND = {
    # Remap() dereferences Toolbar unguarded in the source -> a missing <Toolbar> kills the paste (see docs)
    'SensorBlock':  dict(level='func', owner=True, tail=_sensor_tail(), conf='from-source'),
    'TimerBlock':   dict(level='func', owner=True, conf='from-source',
                         tail=EMPTY_TOOLBAR + '<Delay>10000</Delay><CurrentTime>0</CurrentTime><IsCountingDown>false</IsCountingDown><Silent>false</Silent>'),
    'MergeBlock':   dict(level='func', owner=True, tail='', conf='from-source'),
    'Decoy':        dict(level='func', owner=True, tail='', conf='from-source'),
    'Warhead':      dict(level='term', owner=True, conf='from-source',
                         tail='<CountdownMs>10000</CountdownMs><IsArmed>false</IsArmed><IsCountingDown>false</IsCountingDown>'),
    # AI blocks: no object-builder source or sample is available; settings live in default components
    # (EntityContainers.sbc) which the game creates when <ComponentContainer> is absent.
    'OffensiveCombatBlock': dict(level='func', owner=True, tail='', conf='guess'),
    'FlightMovementBlock':  dict(level='func', owner=True, tail='', conf='guess'),
    'DefensiveCombatBlock': dict(level='func', owner=True, tail='', conf='guess'),
    'BasicMissionBlock':    dict(level='func', owner=True, tail='', conf='guess'),
}
GENERIC = dict(level='func', owner=True, tail='', conf='guess')
CUBE = dict(level='cube', owner=False, tail='', conf='seen-in-Starburst')

def shape_for(xtype, sub):
    t = xtype.replace('MyObjectBuilder_', '')
    if t == 'CubeBlock': return CUBE, 'seen-in-Starburst'
    for k in (f'{t}/{sub}', f'{t}/*'):
        if k in SHAPES: return SHAPES[k], 'seen-in-Starburst'
    if t in HAND: return HAND[t], HAND[t]['conf']
    return GENERIC, 'guess'

def confidence_table():
    """block type -> confidence, for every type used by DEFS-supported designs we care about"""
    names = ['CubeBlock', 'Thrust', 'CameraBlock', 'BatteryBlock', 'RadioAntenna', 'RemoteControl', 'TurretControlBlock',
             'SmallMissileLauncherReload', 'SmallMissileLauncher', 'SmallGatlingGun', 'Gyro', 'ShipConnector', 'MyProgrammableBlock',
             'EventControllerBlock', 'Projector', 'ShipWelder', 'SensorBlock', 'TimerBlock', 'MergeBlock', 'Warhead', 'Decoy',
             'OffensiveCombatBlock', 'FlightMovementBlock', 'DefensiveCombatBlock', 'BasicMissionBlock']
    return {n: shape_for(n, '')[1] for n in names}

# =============================================================================================
# identity id written as Owner/BuiltBy (same value Starburst, saved by the user's game, carries); the game replaces it with the pasting player
DEFAULT_OWNER = 144115188075855915

class Blueprint:
    # side-local (u, v) -> block-local corner-space point; verified against MyCubeBlockDefinition.m_mountPointTransforms
    FACE_MAP = {
        'Front':  lambda u, v, X, Y, Z: (X - u, v, 0),
        'Back':   lambda u, v, X, Y, Z: (u, v, Z),
        'Left':   lambda u, v, X, Y, Z: (0, v, u),
        'Right':  lambda u, v, X, Y, Z: (X, v, Z - u),
        'Top':    lambda u, v, X, Y, Z: (u, Y, Z - v),
        'Bottom': lambda u, v, X, Y, Z: (u, 0, v),
    }
    def __init__(self, name, grid='Small', seed=1, owner=DEFAULT_OWNER, allow_dlc=False, containers=True):
        self.name, self.grid, self.blocks = name, grid, []
        self.rng = random.Random(seed)
        self.reserve = {}       # cell -> reason (must stay empty: exhaust, camera view, muzzle)
        self.groups = {}
        self.owner = owner          # nonzero: blocks carry Owner/BuiltBy and the game re-owns them to the pasting player. 0 leaves them 'Nobody' (seen in game: user's PB showed Owner: Nobody)
        self.allow_dlc = allow_dlc
        self.containers = containers   # False: strip every <ComponentContainer> (A/B test for in-game paste)
        self.skipped = []           # blocks from a loaded file that could not be validated: (xtype, sub, why)
        self.grid_meta = {}

    def add(self, xtype, sub, mn, fwd='Forward', up='Up', name=None, color=(0, -0.8, -0.3), extra='', kind='armor', mass=0,
            custom_data=None, settings=None, links=None):
        k = defkey(xtype, sub)
        assert k, f'{self.name}: unknown block {xtype}/{sub}'
        d = DEFS[k]
        assert d['grid'] == self.grid, f'{self.name}: {sub} is a {d["grid"]} grid block'
        assert sum(a*b for a, b in zip(DIR[fwd], DIR[up])) == 0, f'{name}: forward/up not perpendicular'
        assert self.allow_dlc or not d['dlc'], f'{self.name}: {sub} needs DLC {d["dlc"]} (DLC blocks are forbidden)'
        b = dict(xtype=xtype, sub=sub, min=tuple(mn), fwd=fwd, up=up, name=name, color=color, extra=extra,
                 kind=kind, size=tuple(d['size']), mounts=d['mounts'], mass=mass, custom_data=custom_data, key=k,
                 settings=settings or {}, links=links or {}, id=0)
        self.blocks.append(b)
        return b

    def armor(self, x, y, z, color=None, sub=None):
        sub = sub or ('SmallBlockArmorBlock' if self.grid == 'Small' else 'LargeBlockArmorBlock')
        return self.add('MyObjectBuilder_CubeBlock', sub, (x, y, z), color=color or (0, -0.8, -0.3),
                        mass=25 if self.grid == 'Small' else 500)

    # ---------- geometry ----------
    @staticmethod
    def axes(b):
        f = DIR[b['fwd']]; u = DIR[b['up']]; r = cross(f, u)
        return [r, u, neg(f)]          # local X (Right), Y (Up), Z (Backward) in grid directions

    def ext(self, b):
        ax = self.axes(b); e = [0, 0, 0]
        for i, s in enumerate(b['size']):
            for k in range(3): e[k] += abs(ax[i][k]) * s
        return e

    def cells(self, b):
        e = self.ext(b); x, y, z = b['min']
        return [(x+i, y+j, z+k) for i in range(e[0]) for j in range(e[1]) for k in range(e[2])]

    def to_grid(self, b, p):
        """block-local corner-space point -> grid corner space (cell i spans [i, i+1))."""
        ax = self.axes(b); g = list(b['min'])
        for i in range(3):
            a = ax[i]; k = [abs(v) for v in a].index(1); sgn = a[k]
            g[k] += p[i] if sgn > 0 else (b['size'][i] - p[i])
        return tuple(g)

    def mount_rects(self, b):
        """grid-space mount rectangles: (axis, plane_coord, normal_sign, lo(2), hi(2), excl, prop), shrunk like the game."""
        X, Y, Z = b['size']
        mps = b['mounts']
        if mps is None:   # no MountPoints element = full mount on every face
            mps = [dict(side=s, sx=0, sy=0, ex=w, ey=h, excl=None, prop=None) for s, w, h in
                   (('Front', X, Y), ('Back', X, Y), ('Left', Z, Y), ('Right', Z, Y), ('Top', X, Z), ('Bottom', X, Z))]
        out = []
        ax = self.axes(b)
        for m in mps:
            pts = []
            for (u, v) in ((m['sx'], m['sy']), (m['ex'], m['ey'])):
                p = self.FACE_MAP[m['side']](u, v, X, Y, Z)
                pts.append(self.to_grid(b, p))
            nl = {'Front': (0,0,-1), 'Back': (0,0,1), 'Left': (-1,0,0), 'Right': (1,0,0), 'Top': (0,1,0), 'Bottom': (0,-1,0)}[m['side']]
            n = [0, 0, 0]
            for i in range(3):
                for k in range(3): n[k] += nl[i] * ax[i][k]
            axis = [abs(v) for v in n].index(1)
            others = [i for i in range(3) if i != axis]
            lo = tuple(min(pts[0][i], pts[1][i]) + SHRINK for i in others)
            hi = tuple(max(pts[0][i], pts[1][i]) - SHRINK for i in others)
            out.append((axis, pts[0][axis], n[axis], lo, hi, int(m['excl']) if m.get('excl') else 0, int(m['prop']) if m.get('prop') else 0))
        return out

    # ---------- validation ----------
    def occupancy(self, allow_overlap=False):
        occ = {}
        for b in self.blocks:
            for c in self.cells(b):
                if c in occ and not allow_overlap:
                    raise AssertionError(f'{self.name}: overlap at {c}: {b["name"] or b["sub"]} vs {occ[c]["name"] or occ[c]["sub"]}')
                occ[c] = b
        return occ

    def attachment(self, occ=None):
        """mount-point adjacency exactly as MyCubeGrid.CheckMountPointsForSide: returns {id(block): set(id(neighbour))}"""
        index = {}
        rects = {}
        for b in self.blocks:
            rs = rects[id(b)] = self.mount_rects(b)
            for r in rs:
                index.setdefault((r[0], r[1], r[2]), []).append((b, r))
        adj = {id(b): set() for b in self.blocks}
        for b in self.blocks:
            for (axis, pc, ns, lo, hi, ex, pr) in rects[id(b)]:
                for (nb, (a2, pc2, ns2, lo2, hi2, ex2, pr2)) in index.get((axis, pc, -ns), ()):
                    if nb is b: continue
                    if ((ex & pr2) or (pr & ex2)) and b['key'] != nb['key']: continue
                    if lo[0] <= hi2[0] and lo2[0] <= hi[0] and lo[1] <= hi2[1] and lo2[1] <= hi[1]:
                        adj[id(b)].add(id(nb)); adj[id(nb)].add(id(b))
        return adj

    def components(self, adj):
        byid = {id(b): b for b in self.blocks}; seen = set(); comps = []
        for b in self.blocks:
            if id(b) in seen: continue
            comp = []; todo = [id(b)]; seen.add(id(b))
            while todo:
                x = todo.pop(); comp.append(byid[x])
                for n in adj[x]:
                    if n not in seen: seen.add(n); todo.append(n)
            comps.append(comp)
        return comps

    def validate(self, require_connected=True):
        occ = self.occupancy()
        for c, why in self.reserve.items():
            assert c not in occ, f'{self.name}: {why} blocked by {occ[c]["name"] or occ[c]["sub"]} at {c}'
        assert self.allow_dlc or not self.dlc_used(), f'{self.name}: uses DLC blocks {self.dlc_used()}'
        adj = self.attachment(occ)
        problems = [b for b in self.blocks if not adj[id(b)]]
        assert not problems, f'{self.name}: not attached: ' + ', '.join(f'{p["name"] or p["sub"]}@{p["min"]}' for p in problems)
        if require_connected and self.blocks:
            comps = self.components(adj)
            lost = [b for c in comps if c[0] is not self.blocks[0] and self.blocks[0] not in c for b in c]
            assert not lost, f'{self.name}: separate pieces: ' + ', '.join(f'{p["name"] or p["sub"]}@{p["min"]}' for p in lost)
        return occ

    def dlc_used(self):
        return sorted({d for b in self.blocks for d in DEFS[b['key']]['dlc']})

    def keep_clear(self, b, n, why):
        """reserve n cells in front of block b's forward face (exhaust, muzzle, camera view)."""
        f = DIR[b['fwd']]; cs = self.cells(b)
        tip = min(cs, key=lambda c: -sum(a*d for a, d in zip(c, f)))
        for i in range(1, n + 1):
            self.reserve[tuple(tip[k] + f[k]*i for k in range(3))] = f'{why} of {b["name"] or b["sub"]}'

    # ---------- loading game-written blueprints ----------
    @classmethod
    def from_sbc(cls, path, allow_dlc=True):
        return load_sbc(path, allow_dlc)

    # ---------- XML ----------
    def _assign_ids(self):
        used = set()
        for b in self.blocks:
            if b['id']: used.add(b['id'])
        for b in self.blocks:
            if b['xtype'].endswith('_CubeBlock') and not b['name']: continue     # plain armour: the game writes no EntityId
            while not b['id'] or b['id'] in used:
                b['id'] = self.rng.randint(10**17, 9 * 10**17)
            used.add(b['id'])

    def _components_xml(self, spec, b):
        """ComponentContainer element or None.  Present containers must hold every default component
        (MyComponentContainerExtension.InitComponents adds only serialized ones when a container exists)."""
        if not self.containers: return None
        comps = []
        if spec.get('container'):
            comps = [c for c in frag(spec['container'])[0].find('Components')]
        if b['custom_data']:
            item = ET.Element('item'); item.append(E('Key', CUSTOM_DATA_GUID)); item.append(E('Value', b['custom_data']))
            d = ET.Element('dictionary'); d.append(item)
            st = ET.Element('Storage'); st.append(d)
            comp = ET.Element('Component', {XSI + 'type': 'MyObjectBuilder_ModStorageComponent'}); comp.append(st)
            cd = ET.Element('ComponentData'); cd.append(E('TypeId', 'MyModStorageComponentBase')); cd.append(comp)
            comps.append(cd)
        if not comps: return None
        cc = ET.Element('ComponentContainer'); cs = ET.SubElement(cc, 'Components')
        for c in comps: cs.append(c)
        return cc

    def build_block(self, b, counters):
        spec, _ = shape_for(b['xtype'], b['sub'])
        level = spec['level']
        o = ET.Element('MyObjectBuilder_CubeBlock', {XSI + 'type': b['xtype']})
        o.append(E('SubtypeName', b['sub'] or None))
        if b['id']: o.append(E('EntityId', b['id']))
        x, y, z = b['min']
        o.append(E('Min', x=x, y=y, z=z))
        if (b['fwd'], b['up']) != ('Forward', 'Up'): o.append(E('BlockOrientation', Forward=b['fwd'], Up=b['up']))
        h, s, v = b['color']
        o.append(E('ColorMaskHSV', x=_txt(h), y=_txt(s), z=_txt(v)))
        if self.owner:
            if spec.get('owner'): o.append(E('Owner', self.owner))
            o.append(E('BuiltBy', self.owner))
        if level == 'cube': return o
        if spec.get('owner'): o.append(E('ShareMode', 'Faction'))
        if spec.get('deform') is not None: o.append(E('DeformationRatio', spec['deform']))
        cc = self._components_xml(spec, b)
        if cc is not None: o.append(cc)
        if b['name']: o.append(E('CustomName', b['name']))
        for t, val in (('ShowOnHUD', False), ('ShowInTerminal', True), ('ShowInToolbarConfig', True), ('ShowInInventory', True)):
            o.append(E(t, val))
        key = (b['xtype'], b['sub'])
        counters[key] = counters.get(key, 0) + 1
        o.append(E('NumberInGrid', counters[key]))
        if level == 'func': o.append(E('Enabled', spec.get('enabled', True)))
        tail = frag(spec['tail']) if spec.get('tail') else []
        # overrides: legacy raw `extra` XML first, then explicit settings (tag -> text)
        ov = frag(b['extra']) if b['extra'] else []
        for tag, val in (b['settings'] or {}).items():
            if isinstance(val, ET.Element): ov.append(val)
            elif isinstance(val, str) and val.lstrip().startswith('<'): ov.extend(frag(val))
            else: ov.append(E(tag, val))
        ov.extend(self._link_elements(b))
        ov = [e for e in ov if not (e.tag == 'DefaultRunArgument' and e.get(XSI + 'nil') == 'true')]   # null: the game omits it
        for e in ov:
            for i, t in enumerate(tail):
                if t.tag == e.tag: tail[i] = e; break
            else: tail.append(e)
        for e in tail: o.append(e)
        return o

    def _link_elements(self, b):
        """TurretControlBlock wiring: links={'tools':[blocks], 'camera':block, 'direction':block, 'azimuth':b, 'elevation':b}"""
        L = b['links']
        if not L: return []
        out = []
        idof = lambda blk: blk['id'] if isinstance(blk, dict) else int(blk)
        for tag, key in (('AzimuthId', 'azimuth'), ('ElevationId', 'elevation'), ('CameraId', 'camera'), ('DirectionBlockId', 'direction')):
            if key in L: out.append(E(tag, idof(L[key])))
        if 'tools' in L:
            t = ET.Element('ToolIds')
            for blk in L['tools']: t.append(E('long', idof(blk)))
            out.append(t)
        return out

    def xml_block(self, b, counters=None):
        return ser(self.build_block(b, counters if counters is not None else {}), 6)

    def blocks_xml(self):
        self._assign_ids()
        counters = {}
        return ''.join(ser(self.build_block(b, counters), 6) for b in self.blocks)

    def blueprint_id(self, display, version=''):
        sig = hashlib.sha1((self.name + '|' + version + '|' + self.grid + '|' + '|'.join(
            f'{b["xtype"]}/{b["sub"]}@{b["min"]}{b["fwd"]}{b["up"]}{b["name"]}' for b in self.blocks)).encode()).hexdigest()[:10]
        safe = re.sub(r'[^A-Za-z0-9 _.\-]', '', display).strip() or 'Blueprint'
        return f'{safe} [{sig}]'

    def build_xml(self, display=None, version=''):
        display = display or self.name
        gid = self.rng.randint(10**17, 9 * 10**17)
        dlc = ''.join(f'      <DLC>{d}</DLC>\n' for d in self.dlc_used())
        return f'''<?xml version="1.0"?>
<Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <ShipBlueprints>
    <ShipBlueprint xsi:type="MyObjectBuilder_ShipBlueprintDefinition">
      <Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="{escape(self.blueprint_id(display, version), {chr(34): "&quot;"})}" />
      <DisplayName>{escape(display)}</DisplayName>
{dlc}      <CubeGrids>
        <CubeGrid>
          <SubtypeName />
          <EntityId>{gid}</EntityId>
          <PersistentFlags>CastShadows InScene</PersistentFlags>
          <PositionAndOrientation>
            <Position x="0" y="0" z="0" />
            <Forward x="0" y="0" z="-1" />
            <Up x="0" y="1" z="0" />
            <Orientation>
              <X>0</X>
              <Y>0</Y>
              <Z>0</Z>
              <W>1</W>
            </Orientation>
          </PositionAndOrientation>
          <LocalPositionAndOrientation xsi:nil="true" />
          <GridSizeEnum>{self.grid}</GridSizeEnum>
          <CubeBlocks>
{self.blocks_xml()}          </CubeBlocks>
          <DisplayName>{escape(display)}</DisplayName>
          <DestructibleBlocks>true</DestructibleBlocks>
          <IsRespawnGrid>false</IsRespawnGrid>
          <LocalCoordSys>0</LocalCoordSys>
          <TargetingTargets />
          <NPCGridClaimElapsed xsi:nil="true" />
          <DampenersEnabled>true</DampenersEnabled>
        </CubeGrid>
      </CubeGrids>
      <WorkshopId>0</WorkshopId>
      <OwnerSteamId>0</OwnerSteamId>
      <Points>0</Points>
    </ShipBlueprint>
  </ShipBlueprints>
</Definitions>
'''

    def write(self, outdir, display=None, version=''):
        xml = self.build_xml(display, version)
        os.makedirs(outdir, exist_ok=True)
        open(os.path.join(outdir, 'bp.sbc'), 'w', encoding='utf-8', newline='\n').write(xml)
        stale = os.path.join(outdir, 'bp.sbcB5')       # never ship the game's binary cache; remove any left over
        if os.path.exists(stale): os.remove(stale)

    def stats(self):
        occ = {}
        for b in self.blocks:
            for c in self.cells(b): occ[c] = b
        xs, ys, zs = zip(*occ)
        cell = 0.5 if self.grid == 'Small' else 2.5
        return dict(blocks=len(self.blocks), mass=sum(b['mass'] for b in self.blocks),
                    dims=tuple(round((max(a)-min(a)+1)*cell, 1) for a in (xs, ys, zs)))

    # ---------- thumbnail (PIL if present, otherwise a built-in pure-python PNG writer) ----------
    def thumb(self, path, title, subtitle, palette):
        at = {}
        for b in self.blocks:
            for c in self.cells(b): at[c] = b
        W = H = 512
        cv = _make_canvas(W, H)
        for i in range(H):
            t = i / H; cv.hline(i, (int(14+18*t), int(17+18*t), int(22+22*t)))
        xs, ys, zs = zip(*at)
        span = max(max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs)) + 1
        S = min(60, 300 / span)
        cx, cy, cz = (max(xs)+min(xs))/2, (max(ys)+min(ys))/2, (max(zs)+min(zs))/2
        def proj(x, y, z):
            x -= cx; y -= cy; z -= cz
            return (W/2 + (x+z)*0.866*S, H/2 + 30 + (x-z)*0.5*S - y*S)
        def sh(c, f): return tuple(max(0, min(255, int(v*f))) for v in c)
        for (x, y, z) in sorted(at, key=lambda c: (c[0]+c[1]-c[2], c[1])):
            b = at[(x, y, z)]; col = palette.get(b['kind'], (100, 100, 110))
            p = lambda dx, dy, dz: proj(x+dx, y+dy, z+dz)
            top = [p(-.5,.5,-.5), p(.5,.5,-.5), p(.5,.5,.5), p(-.5,.5,.5)]
            right = [p(.5,.5,-.5), p(.5,.5,.5), p(.5,-.5,.5), p(.5,-.5,-.5)]
            front = [p(-.5,.5,-.5), p(.5,.5,-.5), p(.5,-.5,-.5), p(-.5,-.5,-.5)]
            if (x, y+1, z) not in at: cv.polygon(top, sh(col, 1.15), sh(col, .55))
            if (x+1, y, z) not in at: cv.polygon(right, sh(col, .8), sh(col, .5))
            if (x, y, z-1) not in at: cv.polygon(front, sh(col, .62), sh(col, .4))
        cv.text(16, 16, title, (235, 170, 60))
        cv.text(16, 32, subtitle, (180, 186, 196))
        cv.save(path)


# =============================================================================================
# canvas backends
# =============================================================================================
class _PilCanvas:
    def __init__(self, W, H):
        from PIL import Image, ImageDraw
        self.img = Image.new('RGB', (W, H)); self.d = ImageDraw.Draw(self.img); self.W = W
    def hline(self, y, c): self.d.line([(0, y), (self.W, y)], fill=c)
    def polygon(self, pts, fill, outline): self.d.polygon(pts, fill=fill, outline=outline)
    def text(self, x, y, s, c): self.d.text((x, y), s, fill=c)
    def save(self, path): self.img.save(path)

_FONT = {  # 5x7 bitmap font, rows top->bottom, 5 bits per row (bit4 = left)
 'A':'0E 11 11 1F 11 11 11','B':'1E 11 11 1E 11 11 1E','C':'0E 11 10 10 10 11 0E','D':'1E 11 11 11 11 11 1E','E':'1F 10 10 1E 10 10 1F',
 'F':'1F 10 10 1E 10 10 10','G':'0E 11 10 17 11 11 0F','H':'11 11 11 1F 11 11 11','I':'0E 04 04 04 04 04 0E','J':'07 02 02 02 02 12 0C',
 'K':'11 12 14 18 14 12 11','L':'10 10 10 10 10 10 1F','M':'11 1B 15 15 11 11 11','N':'11 19 15 13 11 11 11','O':'0E 11 11 11 11 11 0E',
 'P':'1E 11 11 1E 10 10 10','Q':'0E 11 11 11 15 12 0D','R':'1E 11 11 1E 14 12 11','S':'0F 10 10 0E 01 01 1E','T':'1F 04 04 04 04 04 04',
 'U':'11 11 11 11 11 11 0E','V':'11 11 11 11 11 0A 04','W':'11 11 11 15 15 15 0A','X':'11 11 0A 04 0A 11 11','Y':'11 11 0A 04 04 04 04',
 'Z':'1F 01 02 04 08 10 1F','0':'0E 11 13 15 19 11 0E','1':'04 0C 04 04 04 04 0E','2':'0E 11 01 02 04 08 1F','3':'1F 02 04 02 01 11 0E',
 '4':'02 06 0A 12 1F 02 02','5':'1F 10 1E 01 01 11 0E','6':'06 08 10 1E 11 11 0E','7':'1F 01 02 04 08 08 08','8':'0E 11 11 0E 11 11 0E',
 '9':'0E 11 11 0F 01 02 0C','-':'00 00 00 1F 00 00 00','.':'00 00 00 00 00 0C 0C',',':'00 00 00 00 0C 04 08',':':'00 0C 0C 00 0C 0C 00',
 '+':'00 04 04 1F 04 04 00','/':'01 01 02 04 08 10 10','(':'02 04 08 08 08 04 02',')':'08 04 02 02 02 04 08','[':'0E 08 08 08 08 08 0E',
 ']':'0E 02 02 02 02 02 0E','_':'00 00 00 00 00 00 1F','x':'00 00 11 0A 04 0A 11','%':'18 19 02 04 08 13 03',' ':'00 00 00 00 00 00 00',
}
class _RawCanvas:
    """dependency-free RGB canvas: scanline polygon fill, Bresenham outline, 5x7 font, zlib PNG encoder"""
    def __init__(self, W, H):
        self.W, self.H = W, H; self.px = bytearray(W * H * 3)
    def _set(self, x, y, c):
        if 0 <= x < self.W and 0 <= y < self.H:
            i = (y * self.W + x) * 3; self.px[i:i+3] = bytes(c)
    def hline(self, y, c):
        self.px[y*self.W*3:(y+1)*self.W*3] = bytes(c) * self.W
    def line(self, a, b, c):
        x0, y0, x1, y1 = int(round(a[0])), int(round(a[1])), int(round(b[0])), int(round(b[1]))
        dx, dy = abs(x1-x0), -abs(y1-y0); sx = 1 if x0 < x1 else -1; sy = 1 if y0 < y1 else -1; err = dx + dy
        while True:
            self._set(x0, y0, c)
            if x0 == x1 and y0 == y1: break
            e2 = 2 * err
            if e2 >= dy: err += dy; x0 += sx
            if e2 <= dx: err += dx; y0 += sy
    def polygon(self, pts, fill, outline):
        ys = [p[1] for p in pts]
        for y in range(max(0, int(min(ys))), min(self.H - 1, int(max(ys))) + 1):
            xs = []
            for i in range(len(pts)):
                (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % len(pts)]
                if (y0 <= y + .5 < y1) or (y1 <= y + .5 < y0):
                    xs.append(x0 + (y + .5 - y0) * (x1 - x0) / (y1 - y0))
            xs.sort()
            for j in range(0, len(xs) - 1, 2):
                for x in range(int(round(xs[j])), int(round(xs[j + 1])) + 1): self._set(x, y, fill)
        for i in range(len(pts)): self.line(pts[i], pts[(i + 1) % len(pts)], outline)
    def text(self, x, y, s, c):
        for ch in s.upper().replace('–', '-'):
            g = _FONT.get(ch if ch in _FONT else ch.upper())
            if g:
                for r, row in enumerate(g.split()):
                    bits = int(row, 16)
                    for k in range(5):
                        if bits & (16 >> k): self._set(x + k, y + r, c)
            x += 6
    def save(self, path):
        raw = b''.join(b'\x00' + bytes(self.px[y*self.W*3:(y+1)*self.W*3]) for y in range(self.H))
        def chunk(t, d): return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
        open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', self.W, self.H, 8, 2, 0, 0, 0)) +
                               chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))

def _make_canvas(W, H):
    if os.environ.get('SEBP_NO_PIL'): return _RawCanvas(W, H)
    try: return _PilCanvas(W, H)
    except ImportError: return _RawCanvas(W, H)


# =============================================================================================
# loader for game-written blueprints
# =============================================================================================
def load_sbc(path, allow_dlc=True):
    """Read a game-written bp.sbc -> [Blueprint, ...] one per CubeGrid (sub-grids included).
    Blocks whose definition is unknown / on another grid size are recorded in .skipped."""
    root = ET.parse(path).getroot()
    out = []
    for gi, g in enumerate(root.iter('CubeGrid')):
        bp = Blueprint(g.findtext('DisplayName') or f'grid{gi}', g.findtext('GridSizeEnum') or 'Large', allow_dlc=allow_dlc)
        bp.grid_meta = dict(entity=g.findtext('EntityId'), index=gi)
        for e in g.find('CubeBlocks'):
            xt = e.get(XSI + 'type'); sub = e.findtext('SubtypeName') or ''
            k = defkey(xt, sub)
            if not k: bp.skipped.append((xt, sub, 'no definition in defs.json')); continue
            d = DEFS[k]
            if d['grid'] != bp.grid: bp.skipped.append((xt, sub, f'definition is {d["grid"]} grid')); continue
            mn = e.find('Min'); o = e.find('BlockOrientation')
            fwd, up = (o.get('Forward'), o.get('Up')) if o is not None else ('Forward', 'Up')
            bp.blocks.append(dict(xtype=xt, sub=sub, min=((int(mn.get('x', 0)), int(mn.get('y', 0)), int(mn.get('z', 0))) if mn is not None else (0, 0, 0)), fwd=fwd, up=up,
                                  name=e.findtext('CustomName'), color=(0, 0, 0), extra='', kind='armor', size=tuple(d['size']),
                                  mounts=d['mounts'], mass=0, custom_data=None, key=k, settings={}, links={}, id=0))
        out.append(bp)
    return out
