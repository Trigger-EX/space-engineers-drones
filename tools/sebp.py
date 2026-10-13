"""Space Engineers blueprint builder with real-definition validation and game-shaped XML.

Public API (the old tools/drones.py shim was removed): Blueprint(name, grid, seed), .add(), .armor(),
.keep_clear(), .validate(), .write(outdir, display), .stats(), .thumb(path, title, subtitle, palette).
New: Blueprint.from_sbc(path) / load_sbc(path) (read any game-written blueprint), .attachment(),
.dlc_used(), .build_xml(), per-block `settings={}` and `links`, see docs/xml-notes.md.

Extension (docs/xml-notes.md section 7; tests: python3 -I tools/tests/run_all.py):
  Blueprint(name, grid, seed, owner=id, legacy_shapes=False)   legacy_shapes=True = the exact XML of the shipped drones
  bp.add(..., items=[Item.ammo(..)], state={..}, toolbar=[..], toolbars={..}, owner=id, built_by=id)
  bp.fill_ammo(b) / preload(b, items, state) / max_items(b, kind, sub) / items_of(b)      inventories (sebp_inv)
  bp.group(name, blocks), bp.set_toolbars(b, ..) with sebp_toolbar.block/group/weapon/run   toolbars + <BlockGroups> (sebp_toolbar)
  bp.attach(child, pose), bp.dock(child, parent_block, child_block, lock=False)             several CubeGrids in one file (sebp_multi)
  load_sbc_multi(path) / Blueprint.from_sbc_multi(path), confidence_table(), shape_report(), bp.shape_summary()

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
import json, os, sys, random, hashlib, struct, zlib, re
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path: sys.path.insert(0, HERE)        # the sebp_* extension modules live next to this file
from sebp_xml import (XSI_NS, XSI, _qn, ser, frag, E, _txt, EMPTY_TOOLBAR)      # noqa: F401  (re-exported: extract_shapes, designs)

DEFS = json.load(open(os.path.join(HERE, '..', 'defs.json')))
import sebp_inv as INV            # inventory pre-loading, stored state (ammo, uranium, hydrogen, charge)
import sebp_toolbar as TB        # toolbars, block groups, event controller helper
import sebp_multi as MU          # grid poses, docking geometry, connector locks
from sebp_inv import Item, InventoryError
try:
    _DOC = json.load(open(os.path.join(HERE, 'shapes.json')))   # block templates (tools/extract_shapes.py)
except (OSError, ValueError):
    _DOC = {}
if 'shapes' in _DOC or 'legacy' in _DOC:                         # format 3: "legacy" (pinned, Starburst) + "shapes" (all samples)
    SHAPES = dict(_DOC.get('legacy', {}))        # what Blueprint(legacy_shapes=True) sees; printer_lib injects CargoContainer/* here
    SHAPES2 = dict(_DOC.get('shapes', {}))       # sample-derived templates, the default
    CENTERS = dict(_DOC.get('_centers', {}))     # 'Type/Subtype' -> block-local Center cell written in BlockGroups
else:                                                            # old flat file
    SHAPES, SHAPES2, CENTERS = {k: v for k, v in _DOC.items() if not k.startswith('_')}, {}, {}
# the reference ship's antenna label ("CAMERA DRONE") must not leak into every generated antenna
for _v in SHAPES.values():
    if isinstance(_v, dict) and isinstance(_v.get('tail'), str) and '<HudText>' in _v['tail']:
        _v['tail'] = _v['tail'].replace('<HudText>CAMERA DRONE</HudText>', '<HudText />')
DIR = {"Forward": (0,0,-1), "Backward": (0,0,1), "Up": (0,1,0), "Down": (0,-1,0), "Left": (-1,0,0), "Right": (1,0,0)}
SHRINK = 0.001
CUSTOM_DATA_GUID = '74de02b3-27f9-4960-b1c4-27351f2b06d1'    # MyTerminalBlock.m_storageGuid: CustomData lives in ModStorage

def local_cell_to_grid(bp, b, loc):
    """grid cell of the block-local cell `loc` (cell indices along the block's own Right/Up/Backward axes)"""
    ax = bp.axes(b); g = list(b['min'])
    for i in range(3):
        a = ax[i]; k = [abs(v) for v in a].index(1)
        g[k] += loc[i] if a[k] > 0 else (b['size'][i] - 1 - loc[i])
    return tuple(g)

def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def neg(a): return tuple(-v for v in a)

def defkey(xtype, sub):
    t = xtype.replace('MyObjectBuilder_', '')
    for k in (f'{t}/{sub}', f'MyObjectBuilder_{t}/{sub}'):
        if k in DEFS: return k
    return None


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

# Optional fields the game writes only when they are not at their default (so they are not in the templates): where they go when
# settings={...} asks for them.  tag -> the tag it follows (None = first field after <Enabled>).  Orders read off the samples.
TAIL_AFTER = {'Range': 'MaxFov',                    # large turrets (terminal range)
              'IsMainCockpit': 'SelectedGunId',     # cockpits
              'State': 'AnyoneCanUse',              # doors
              'ProjectedGrids': None,               # projector
              'ThrowOut': None, 'CollectAll': 'ThrowOut',            # connector
              'Strength': 'MasterToSlaveTransform'}

def derived_inventory_volume(key):
    """inventory volume (m3) of a definition from the definition data, or None.  Rules (all verified against the owner's samples):
    InventoryMaxVolume (weapons, tanks, turrets) -> InventorySize X*Y*Z (reactors, sorter) -> cargo containers: cells x cell volume."""
    d = INV.stats()['blocks'].get(key)
    if not d: return None
    r = d['raw']
    if 'InventoryMaxVolume' in r: return r['InventoryMaxVolume']
    if 'InventorySize.X' in r: return r['InventorySize.X'] * r['InventorySize.Y'] * r['InventorySize.Z']
    if d['type'] == 'CargoContainer':
        c = d['size_cells']; cell = 2.5 if d['grid'] == 'Large' else 0.5
        return c[0] * c[1] * c[2] * cell ** 3
    return None

def _retarget_container(spec, key):
    """a copy of `spec` whose container is valid for definition `key` (an unseen subtype of a type whose container is subtype
    independent): the Volume of every inventory component comes from the definition; None = cannot tell, drop the container"""
    vol = derived_inventory_volume(key)
    sp = dict(spec)
    cc = frag(spec['container'])[0]
    changed = False
    for cd in cc.find('Components'):
        if cd.findtext('TypeId') == 'MyInventoryBase':
            if vol is None:
                sp.pop('container', None); return sp, 'container omitted (inventory volume unknown)'
            cd.find('Component').find('Volume').text = _txt(float(vol)) if float(vol) != int(vol) else str(int(vol))
            changed = True
    if changed: sp['container'] = ser(cc).strip()
    return sp, None

def retarget_weapon(spec, key):
    """a copy of a weapon template whose GunBase.CurrentAmmoMagazineName is the default magazine of definition `key`
    (the first magazine of its weapon definition; checked against every exact sample shape by the tests)"""
    ammo = INV.default_ammo(key) if key else None
    tail = spec.get('tail') or ''
    if not ammo or '<CurrentAmmoMagazineName>' not in tail: return spec
    sp = dict(spec)
    sp['tail'] = re.sub(r'<CurrentAmmoMagazineName>[^<]*</CurrentAmmoMagazineName>', f'<CurrentAmmoMagazineName>{ammo}</CurrentAmmoMagazineName>', tail)
    return sp

def shape_lookup(xtype, sub, legacy=False):
    """-> (spec, confidence, source).  Default order: sample-derived shape for the exact subtype, sample-derived shape of the type
    (container kept only if it is subtype independent), pinned legacy shape, hand-written shape (from the game source), generic.
    legacy=True skips the sample-derived shapes: the exact behaviour of the shipped drones."""
    t = xtype.replace('MyObjectBuilder_', '')
    if t == 'CubeBlock': return CUBE, 'seen-in-Starburst', 'Starburst'
    if not legacy and SHAPES2:
        sp = SHAPES2.get(f'{t}/{sub}')
        if sp is not None: return sp, ('derived:' if sp.get('derived_from') else 'seen:') + sp.get('src', '?'), sp.get('src')
        sp = SHAPES2.get(f'{t}/*')
        if sp is not None:
            note = ''
            if sp.get('container'):
                if sp.get('container_generic'):
                    key = defkey(xtype, sub)
                    sp, why = _retarget_container(sp, key) if key else (dict(sp), 'unknown definition')
                    if why: note = f' ({why})'
                else:
                    sp = dict(sp); sp.pop('container'); note = ' (container omitted: it differs between subtypes)'
            sp = retarget_weapon(sp, defkey(xtype, sub))
            return sp, ('derived-type:' if sp.get('derived_from') else 'seen-type:') + sp.get('src', '?') + note, sp.get('src')
    for k in (f'{t}/{sub}', f'{t}/*'):
        if k in SHAPES: return SHAPES[k], 'seen-in-Starburst', SHAPES[k].get('src', 'Starburst')
    if t in HAND: return HAND[t], HAND[t]['conf'], None
    return GENERIC, 'guess', None

def shape_for(xtype, sub, legacy=False):
    """-> (spec, confidence) (see shape_lookup for the source sample)"""
    sp, conf, _ = shape_lookup(xtype, sub, legacy)
    return sp, conf

# every block type the generator knows how to write beyond armour; confidence_table() reports each
KNOWN_TYPES = ['CubeBlock', 'Thrust', 'CameraBlock', 'BatteryBlock', 'RadioAntenna', 'RemoteControl', 'TurretControlBlock',
               'SmallMissileLauncherReload', 'SmallMissileLauncher', 'SmallGatlingGun', 'Gyro', 'ShipConnector', 'MyProgrammableBlock',
               'EventControllerBlock', 'Projector', 'ShipWelder', 'SensorBlock', 'TimerBlock', 'MergeBlock', 'Warhead', 'Decoy',
               'OffensiveCombatBlock', 'FlightMovementBlock', 'DefensiveCombatBlock', 'BasicMissionBlock',
               'LargeGatlingTurret', 'LargeMissileTurret', 'InteriorTurret', 'Cockpit', 'Reactor', 'HydrogenEngine', 'OxygenTank',
               'CargoContainer', 'Conveyor', 'ConveyorConnector', 'ConveyorSorter', 'LandingGear', 'Door', 'AirtightHangarDoor',
               'AirtightSlideDoor', 'ButtonPanel']
# writer features that are not block shapes: confidence of each
FEATURES = {
    'inventory items (AmmoMagazine/Ingot/Component/Ore)': 'seen:Pond Hopper Mk2, Red Ship, Starburst - v2, Venture - Expedition End',
    'railgun capacitor StoredPower': 'seen:temp, death_',
    'reactor Capacity = uranium kg': 'seen:Red Ship',
    'hydrogen engine Capacity (litres)': 'seen:Tech Hunter',
    'hydrogen tank FilledRatio': 'seen:Red Ship',
    'toolbar slot ToolbarItemTerminalBlock': 'seen:Tech Hunter, Pertam Pod',
    'toolbar slot ToolbarItemTerminalGroup': 'seen:Tech Hunter, Pond Hopper',
    'toolbar slot ToolbarItemWeapon': 'seen:Tech Hunter, Peekaboo!',
    'toolbar slot parameters (Single/Int32/Int64)': 'seen:Pond Hopper, Tech Hunter',
    'toolbar slot PB Run (no argument)': 'unseen: action id "Run" from the game source; item shape as the seen terminal-block items',
    'toolbar slot PB Run with argument': 'UNSEEN, experimental=True: hypothesised <Parameters> String parameter',
    'BlockGroups (Vector3I of the block Center cell)': 'seen:Pond Hopper Mk2, Small Grid 8000 (cells, not EntityIds)',
    'multi-grid CubeGrids with PositionAndOrientation': 'seen:Venture - Expedition End, Small Grid 8000',
    'connector lock (ConnectedEntityId, MasterToSlaveTransform, MasterToSlaveGrid)': 'seen:Venture - Expedition End (pair geometry reproduced by tests)',
    'merge block lock': 'unseen: the game writes no lock state for merge blocks',
}

def type_subtypes(t):
    """the non-DLC, non-Prototech subtypes of a block type in defs.json"""
    return sorted(k.split('/', 1)[1] for k, d in DEFS.items()
                  if k.split('/')[0].replace('MyObjectBuilder_', '') == t and not d['dlc'] and 'Prototech' not in k)

def confidence_table(extra=True, legacy=False):
    """block type -> confidence for every type the generator can write, plus the writer features ('feature: ...').
    'seen:<sample>' = the template comes from that game-written sample for the exact subtype (every non-DLC subtype of the type);
    'seen for n/m subtypes (...); others <fallback>' = some subtypes have a sample, the rest use the type's template;
    'seen-in-Starburst' = pinned legacy shape; 'from-source' = Keen's source; 'guess' / 'unseen' = no ground truth.
    Per-subtype detail: shape_report()."""
    out = {}
    for n in KNOWN_TYPES:
        subs = type_subtypes(n) if n != 'CubeBlock' else []
        if not subs:
            out[n] = shape_for(n, '', legacy)[1]; continue
        rows = [shape_lookup(n, sub, legacy) for sub in subs]
        exact = [r for r in rows if not r[1].startswith(('seen-type', 'derived-type')) and r[1] != 'guess']
        srcs = sorted({r[1] for r in exact})
        if len(exact) == len(rows):
            out[n] = srcs[0] if len(srcs) == 1 else 'seen:' + ', '.join(sorted({r[2] for r in exact if r[2]})[:3]) + (' ...' if len({r[2] for r in exact}) > 3 else '')
        else:
            rest = sorted({r[1].split(' (')[0].split(':')[0] for r in rows if r not in exact})
            have = sorted({r[2] for r in exact if r[2]})
            out[n] = (f'seen for {len(exact)}/{len(rows)} subtypes ({", ".join(have[:3])}); others: ' if exact else '') + '/'.join(rest) + \
                     (':' + ','.join(sorted({r[2] for r in rows if r[2]})[:2]) if not exact and any(r[2] for r in rows) else '')
    if extra: out.update({'feature: ' + k: v for k, v in FEATURES.items()})
    return out

def shape_report(legacy=False):
    """{'Type/Subtype': (confidence, source sample)} for every key of the shape tables (what exactly can be written, from where)"""
    out = {}
    src = (SHAPES if legacy else dict(SHAPES, **SHAPES2))
    for k in sorted(src):
        t, sub = k.split('/', 1)
        if sub == '*': continue
        sp, conf, s = shape_lookup(t, sub, legacy)
        out[k] = (conf, s)
    return out

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
    def __init__(self, name, grid='Small', seed=1, owner=DEFAULT_OWNER, allow_dlc=False, containers=True, legacy_shapes=False):
        self.name, self.grid, self.blocks = name, grid, []
        self.rng = random.Random(seed)
        self.reserve = {}       # cell -> reason (must stay empty: exhaust, camera view, muzzle)
        self.groups = {}        # block group name -> [block dicts]; written as <BlockGroups> (see group())
        self.owner = owner          # nonzero: blocks carry Owner/BuiltBy and the game re-owns them to the pasting player. 0 leaves them 'Nobody' (seen in game: user's PB showed Owner: Nobody)
                                    # per block: add(..., owner=<id>) overrides it (0 = none); the default is the identity id of the owner's Starburst save (DEFAULT_OWNER)
        self.allow_dlc = allow_dlc
        self.containers = containers   # False: strip every <ComponentContainer> (A/B test for in-game paste)
        self.legacy_shapes = legacy_shapes   # True: ignore the sample-derived shapes (shapes.json "shapes"); this is how the shipped drones keep their tested XML
        self.skipped = []           # blocks from a loaded file that could not be validated: (xtype, sub, why)
        self.grid_meta = {}
        self.subgrids = []          # [(child Blueprint, Pose of the child grid in THIS grid's frame)]   see attach() / dock()
        self.pose = MU.Pose()       # pose of this grid in its parent (the world for a root blueprint)
        self.display = None         # DisplayName of the CubeGrid when this is a sub-grid (default: name)
        self.notes = []             # things worth reporting (unverified actions, dropped pre-loads)

    def add(self, xtype, sub, mn, fwd='Forward', up='Up', name=None, color=(0, -0.8, -0.3), extra='', kind='armor', mass=0,
            custom_data=None, settings=None, links=None, items=None, state=None, toolbar=None, toolbars=None, owner=None, built_by=None):
        """Place a block.  Extensions over the original signature (all keyword, all optional):
          items    inventory pre-load: [Item.ammo('NATO_25x184mm', 24), Item.ingot('Uranium', 100), ('Component','SteelPlate',50), ...]
                   (volume / mass / allowed-item checks raise sebp_inv.InventoryError at once)
          state    stored state: {'capacitor': 'full'|MWh (railgun), 'fuel': 'full'|L (hydrogen engine) | kg (reactor, default = the uranium in items),
                   'filled': 'full'|0..1 (hydrogen tank), 'stored_power': 'full'|MWh (battery)}
          toolbar  slots of the block's main toolbar (Timer, Button Panel, Event Controller, cockpit, ...): list or {index: item} of
                   sebp_toolbar.block()/group()/weapon()/run();  toolbars={'BuildToolbar': [...], 'OnLockedToolbar': [...]} for the others
          owner    Owner/BuiltBy identity id for this block only (default: the blueprint's owner; 0 = write none)
          built_by BuiltBy only (default: the owner)"""
        k = defkey(xtype, sub)
        assert k, f'{self.name}: unknown block {xtype}/{sub}'
        d = DEFS[k]
        assert d['grid'] == self.grid, f'{self.name}: {sub} is a {d["grid"]} grid block'
        assert sum(a*b for a, b in zip(DIR[fwd], DIR[up])) == 0, f'{name}: forward/up not perpendicular'
        assert self.allow_dlc or not d['dlc'], f'{self.name}: {sub} needs DLC {d["dlc"]} (DLC blocks are forbidden)'
        b = dict(xtype=xtype, sub=sub, min=tuple(mn), fwd=fwd, up=up, name=name, color=color, extra=extra,
                 kind=kind, size=tuple(d['size']), mounts=d['mounts'], mass=mass, custom_data=custom_data, key=k,
                 settings=settings or {}, links=links or {}, id=0)
        if owner is not None: b['owner'] = owner
        if built_by is not None: b['built_by'] = built_by
        self.blocks.append(b)
        if items is not None or state:
            self.preload(b, items=items, state=state)
        if toolbar is not None or toolbars:
            self.set_toolbars(b, toolbar, toolbars)
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
        self.check_toolbars()
        return occ

    def check_toolbars(self):
        """every toolbar group item names a group defined on this grid; every group member is a block of this grid; locks pair up"""
        for b in self.blocks:
            for tag, slots in (b.get('toolbars') or {}).items():
                for i, it in slots.items():
                    if isinstance(it, TB.GroupItem):
                        assert it.group in self.groups, f'{self.label(b)}: {tag} slot {i}: block group {it.group!r} is not defined (Blueprint.group)'
        for name, members in self.groups.items():
            for m in members:
                assert any(m is y for y in self.blocks), f'{self.name}: group {name!r} holds a block that is not on this grid'
        for b in self.blocks:
            lk = b.get('lock')
            if lk: assert lk['other'].get('lock', {}).get('other') is b, f'{self.label(b)}: locked connector whose partner does not point back'

    def dlc_used(self):
        return sorted({d for b in self.blocks for d in DEFS[b['key']]['dlc']})

    def validate_all(self, require_connected=True):
        """validate() every grid of this file (this one and all sub-grids); returns {grid name: occupancy}"""
        return {g.name: g.validate(require_connected) for g, _ in self.grids()}

    def keep_clear(self, b, n, why):
        """reserve n cells in front of block b's forward face (exhaust, muzzle, camera view)."""
        f = DIR[b['fwd']]; cs = self.cells(b)
        tip = min(cs, key=lambda c: -sum(a*d for a, d in zip(c, f)))
        for i in range(1, n + 1):
            self.reserve[tuple(tip[k] + f[k]*i for k in range(3))] = f'{why} of {b["name"] or b["sub"]}'

    # ---------- shapes, pre-loads, toolbars, groups ----------
    def shape(self, b):
        """(template spec, confidence) used for block b (honours legacy_shapes)"""
        return shape_for(b['xtype'], b['sub'], self.legacy_shapes)

    def label(self, b):
        return f'{self.name}: {b["name"] or b["sub"] or b["xtype"].replace("MyObjectBuilder_", "")}@{tuple(b["min"])}'

    def _container(self, b):
        """a fresh parse of the ComponentContainer of the block's shape (nothing added), or None"""
        spec, _ = self.shape(b)
        return frag(spec['container'])[0] if spec.get('container') else None

    def inventory_limits(self, b):
        """(volume m3, mass kg|None, max stacks|None) of the block's inventory as the written file will state it, or None"""
        cc = self._container(b)
        return INV.inventory_limits(cc) if cc is not None else None

    def items_of(self, b):
        """the pre-loaded Items of a block ([Item])"""
        return list(b.get('items') or [])

    def max_items(self, b, kind, sub):
        """how many more units of an item fit into the block's inventory (whole units for magazines and components)"""
        lim = self.inventory_limits(b)
        if lim is None: raise InventoryError(f'{self.label(b)}: no inventory in this shape')
        return INV.capacity_of(lim, kind, sub, b.get('items') or [])

    def preload(self, b, items=None, state=None):
        """set the pre-loaded `items` (replaces) and/or the stored `state` (merged) of block b.  Same arguments as add(); validated now."""
        label = self.label(b)
        cc = self._container(b)
        if items is not None:
            its = INV.as_items(items)
            if its:
                if cc is None:
                    raise InventoryError(f'{label}: this block has no inventory in its shape ({self.shape(b)[1]}); items cannot be written')
                INV.check_fit(label, INV.inventory_limits(cc), its, b['key'])
            b['items'] = its
        if state:
            st = dict(b.get('state') or {}); st.update(state)
            INV.apply_state(label, b['xtype'], b['key'], st, b.get('items') or [], cc, self.shape(b)[0].get('capacitor_full'))   # validates (the patched copy is thrown away)
            b['state'] = st
        return b

    def fill_ammo(self, b, sub=None, count='full'):
        """load the block's weapon with its (default) magazines: `count` whole magazines or 'full' (everything that fits)"""
        sub = sub or INV.default_ammo(b['key'])
        if not sub: raise InventoryError(f'{self.label(b)}: not a weapon (no magazine in its definition)')
        free = self.max_items(b, 'AmmoMagazine', sub)
        n = free if count == 'full' else int(count)
        if n > free: raise InventoryError(f'{self.label(b)}: only {free} x {sub} fit, asked for {n}')
        if n > 0: self.preload(b, items=list(b.get('items') or []) + [Item.ammo(sub, n)])
        return b

    def set_toolbars(self, b, toolbar=None, toolbars=None):
        """give block b populated toolbars (see add()); needs the toolbar tag in the block's shape"""
        tbs = {}
        if toolbar is not None: tbs['Toolbar'] = TB.normalise(toolbar)
        for tag, spec in (toolbars or {}).items():
            if tag not in TB.TOOLBAR_TAGS: raise TB.ToolbarError(f'unknown toolbar {tag!r}; known: {TB.TOOLBAR_TAGS}')
            tbs[tag] = TB.normalise(spec)
        spec, conf = self.shape(b)
        for tag in tbs:
            if f'<{tag}>' not in (spec.get('tail') or ''):
                raise TB.ToolbarError(f'{self.label(b)}: the {b["xtype"]} shape ({conf}) has no <{tag}>; toolbars exist on Timer, Button Panel, '
                                      f'Event Controller, Sensor, Turret Controller and the cockpit / remote control family')
        for tag, slots in tbs.items():
            for i, it in slots.items():
                act = getattr(it, 'action', None)
                if it.experimental: self.notes.append(f'{self.label(b)}: slot {i}: UNVERIFIED toolbar item ({act} with a hypothesised parameter shape)')
                elif act is not None and act not in TB.SEEN_ACTIONS and act != TB.RUN_ACTION:
                    self.notes.append(f'{self.label(b)}: slot {i}: action {act!r} does not occur in any sample (written as asked)')
        b['toolbars'] = tbs
        return b

    def shape_summary(self):
        """{'Type/Subtype': (blocks, confidence)} for the non-armour blocks of this grid and its sub-grids: what was written from a
        sample ('seen:...'), from a sibling class ('derived:...'), from the game source or not at all ('from-source', 'guess').
        Anything that is not 'seen:' deserves an in-game paste test."""
        out = {}
        for g, _ in self.grids():
            for b in g.blocks:
                if b['xtype'].endswith('_CubeBlock'): continue
                k = f'{b["xtype"].replace("MyObjectBuilder_", "")}/{b["sub"]}'
                conf = g.shape(b)[1]
                out[k] = (out.get(k, (0, conf))[0] + 1, conf)
        return out

    def group(self, name, blocks):
        """define (or extend) a named block group of this grid; toolbar group items (sebp_toolbar.group) refer to it by name.
        Written as <BlockGroups>; the game stores the Center cell of every member block (see sebp_toolbar)."""
        for x in blocks:
            assert any(x is y for y in self.blocks), f'{self.name}: group {name!r}: block {x.get("name") or x["sub"]} is not on this grid'
        cur = self.groups.setdefault(name, [])
        for x in blocks:
            if not any(x is y for y in cur): cur.append(x)
        return cur

    def group_cell(self, b):
        """the grid cell the game writes for block b inside <BlockGroups>: the definition's Center cell (known from the samples for
        most multi-cell blocks, shapes.json `_centers`), else the block-local cell (0, 0, 0)"""
        loc = CENTERS.get(b['key'], [0, 0, 0])
        return local_cell_to_grid(self, b, tuple(loc))

    # ---------- several grids in one blueprint ----------
    def attach(self, child, pose=None):
        """add `child` (a Blueprint) as another CubeGrid of this file; `pose` = MU.Pose of the child grid in THIS grid's frame, metres
        (e.g. MU.Pose.axes((x, y, z), 'Forward', 'Up') for one of the 24 axis-aligned orientations)"""
        assert child is not self and all(c is not child for c, _ in self.subgrids), 'grid attached twice'
        child.pose = pose if pose is not None else MU.Pose()
        self.subgrids.append((child, child.pose))
        return child

    def dock(self, child, parent_block, child_block, parent_face='Forward', child_face='Forward', gap=0.0, roll=0,
             lock=False, time_of_connection=60000):
        """attach `child` so that the faces of parent_block (this grid) and child_block (the child) are against each other
        (connectors: the Forward faces; merge blocks: pass the clamp faces).  Returns the child's Pose in this grid's frame.
        lock=True also writes the connector pair as locked, in the style of the owner's Venture file (UNVERIFIED in a paste:
        the game may or may not restore the lock; the unlocked default is the tested form)."""
        assert any(parent_block is y for y in self.blocks) and any(child_block is y for y in child.blocks), 'blocks must belong to the two grids'
        pose = MU.dock_pose(self, parent_block, child, child_block, parent_face, child_face, gap, roll)
        self.attach(child, pose)
        if lock: self.lock_connectors(parent_block, child, child_block, time_of_connection)
        return pose

    def lock_connectors(self, parent_block, child, child_block, time_of_connection=60000):
        """mark a connector pair (a block of this grid and one of `child`) as locked.  As in Venture, the block of the later
        grid is the master (carries MasterToSlaveGrid)."""
        for blk in (parent_block, child_block):
            assert blk['xtype'].endswith('_ShipConnector'), 'only ShipConnector pairs can be locked (merge blocks store no lock)'
        parent_block['lock'] = dict(other=child_block, other_bp=child, master=False, time=time_of_connection)
        child_block['lock'] = dict(other=parent_block, other_bp=self, master=True, time=time_of_connection)

    def grids(self, parent_world=None):
        """[(Blueprint, world Pose)] for this grid and all sub-grids (depth first); also stores the world pose in each (_world)"""
        self._world = self.pose if parent_world is None else parent_world * self.pose
        out = [(self, self._world)]
        for c, _ in self.subgrids: out.extend(c.grids(self._world))
        return out

    # ---------- loading game-written blueprints ----------
    @classmethod
    def from_sbc(cls, path, allow_dlc=True):
        return load_sbc(path, allow_dlc)

    @classmethod
    def from_sbc_multi(cls, path, allow_dlc=True, main=0):
        return load_sbc_multi(path, allow_dlc, main)

    # ---------- XML ----------
    def _assign_ids(self, used=None):
        used = set() if used is None else used
        for b in self.blocks:
            if b['id']: used.add(b['id'])
        for b in self.blocks:
            if b['xtype'].endswith('_CubeBlock') and not b['name']: continue     # plain armour: the game writes no EntityId
            if b.get('keep_id'): continue                                          # loaded from a file: references to it stay valid
            while not b['id'] or b['id'] in used:
                b['id'] = self.rng.randint(10**17, 9 * 10**17)
            used.add(b['id'])

    def _container_and_state(self, spec, b):
        """(ComponentContainer element or None, tail override elements for the stored state) of block b.
        Present containers must hold every default component (MyComponentContainerExtension.InitComponents adds only
        serialized ones when a container exists)."""
        items = b.get('items') or []
        state = dict(b.get('state') or {})
        label = self.label(b)
        cc0 = frag(spec['container'])[0] if (self.containers and spec.get('container')) else None
        if items:
            if cc0 is None:
                if self.containers:
                    raise InventoryError(f'{label}: this block has no inventory in its shape ({self.shape(b)[1]}); items cannot be written')
                self.notes.append(f'{label}: containers=False, the pre-loaded items were not written')
            else:
                INV.check_fit(label, INV.inventory_limits(cc0), items, b['key'])
                INV.write_items(cc0, items)
        if cc0 is None: state.pop('capacitor', None)
        tail_state = INV.apply_state(label, b['xtype'], b['key'], state, items, cc0, spec.get('capacitor_full')) if (state or items) else []
        if not self.containers: return None, tail_state
        comps = [c for c in cc0.find('Components')] if cc0 is not None else []
        if b['custom_data']:
            item = ET.Element('item'); item.append(E('Key', CUSTOM_DATA_GUID)); item.append(E('Value', b['custom_data']))
            d = ET.Element('dictionary'); d.append(item)
            st = ET.Element('Storage'); st.append(d)
            comp = ET.Element('Component', {XSI + 'type': 'MyObjectBuilder_ModStorageComponent'}); comp.append(st)
            cd = ET.Element('ComponentData'); cd.append(E('TypeId', 'MyModStorageComponentBase')); cd.append(comp)
            comps.append(cd)
        if not comps: return None, tail_state
        cc = ET.Element('ComponentContainer'); cs = ET.SubElement(cc, 'Components')
        for c in comps: cs.append(c)
        return cc, tail_state

    @staticmethod
    def _resolve_id(t):
        if isinstance(t, dict):
            if not t['id']: raise TB.ToolbarError(f'toolbar target {t["name"] or t["sub"]} has no EntityId (plain armour has none; give it a name)')
            return t['id']
        return int(t)

    def _own_ids(self, b):
        own = b.get('owner'); own = self.owner if own is None else own
        bb = b.get('built_by'); bb = own if bb is None else bb
        return own, bb

    def build_block(self, b, counters):
        spec, _ = shape_for(b['xtype'], b['sub'], self.legacy_shapes)
        level = spec['level']
        o = ET.Element('MyObjectBuilder_CubeBlock', {XSI + 'type': b['xtype']})
        o.append(E('SubtypeName', b['sub'] or None))
        if b['id']: o.append(E('EntityId', b['id']))
        x, y, z = b['min']
        o.append(E('Min', x=x, y=y, z=z))
        if (b['fwd'], b['up']) != ('Forward', 'Up'): o.append(E('BlockOrientation', Forward=b['fwd'], Up=b['up']))
        h, s, v = b['color']
        o.append(E('ColorMaskHSV', x=_txt(h), y=_txt(s), z=_txt(v)))
        own, bb = self._own_ids(b)
        if own or bb:
            if own and spec.get('owner'): o.append(E('Owner', own))
            if bb: o.append(E('BuiltBy', bb))
        if level == 'cube': return o
        if spec.get('owner'): o.append(E('ShareMode', 'Faction'))
        if spec.get('deform') is not None: o.append(E('DeformationRatio', spec['deform']))
        cc, state_tail = self._container_and_state(spec, b)
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
        ov.extend(state_tail)
        ov = [e for e in ov if not (e.tag == 'DefaultRunArgument' and e.get(XSI + 'nil') == 'true')]   # null: the game omits it
        for tag, slots in (b.get('toolbars') or {}).items():
            ov.append(TB.toolbar_element(tag, slots, b['id'], self._resolve_id, set(self.groups)))
        for e in ov:
            for i, t in enumerate(tail):
                if t.tag == e.tag: tail[i] = e; break
            else:
                anchor = 'END' if self.legacy_shapes else TAIL_AFTER.get(e.tag, 'END')
                j = None if anchor in ('END', None) else next((i for i, t in enumerate(tail) if t.tag == anchor), None)
                if anchor is None: tail.insert(0, e)             # first field after <Enabled>
                elif j is not None: tail.insert(j + 1, e)
                else: tail.append(e)
        if b.get('lock'): tail = self._apply_lock(b, tail)
        for e in tail: o.append(e)
        return o

    def _apply_lock(self, b, tail):
        """connector tail of a locked pair (the Venture form): ConnectedEntityId first, then the transform fields"""
        lk = b['lock']; other, obp = lk['other'], lk['other_bp']
        if not other['id']: raise TB.ToolbarError(f'{self.label(b)}: locked partner has no EntityId yet (write through the root Blueprint.build_xml)')
        mw, sw = (self._world, obp._world) if lk['master'] else (obp._world, self._world)
        mbp, sbp = (self, obp) if lk['master'] else (obp, self)
        mblk, sblk = (b, other) if lk['master'] else (other, b)
        trans, grid = MU.lock_elements(mw, MU.block_pose(mbp, mblk), sw, MU.block_pose(sbp, sblk), mbp, sbp)
        if not lk['master']:
            grid = ET.Element('MasterToSlaveGrid'); grid.set(XSI + 'nil', 'true')
        rep = {'MasterToSlaveTransform': trans, 'MasterToSlaveGrid': grid, 'IsMaster': E('IsMaster', lk['master']),
               'TimeOfConnection': E('TimeOfConnection', lk['time']), 'IsApproaching': E('IsApproaching', True), 'IsConnecting': E('IsConnecting', True)}
        out = [E('ConnectedEntityId', other['id'])] + [rep.get(t.tag, t) for t in tail if t.tag != 'ConnectedEntityId']
        missing = [k for k in rep if not any(t.tag == k for t in tail)]
        if missing: raise TB.ToolbarError(f'{self.label(b)}: the connector shape has no {missing}; cannot write a lock')
        return out

    def _link_elements(self, b):
        """TurretControlBlock wiring: links={'tools':[blocks], 'camera':block, 'direction':block, 'azimuth':b, 'elevation':b};
        EventControllerBlock: links={'selected': [blocks]} -> <SelectedBlocks>"""
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
        if 'selected' in L:
            t = ET.Element('SelectedBlocks')
            for blk in L['selected']: t.append(E('long', idof(blk)))
            out.append(t)
        return out

    def xml_block(self, b, counters=None):
        return ser(self.build_block(b, counters if counters is not None else {}), 6)

    def _blocks_body(self):
        counters = {}
        return ''.join(ser(self.build_block(b, counters), 6) for b in self.blocks)

    def blocks_xml(self):
        self._assign_ids()
        return self._blocks_body()

    def blueprint_id(self, display, version=''):
        sig = hashlib.sha1((self.name + '|' + version + '|' + self.grid + '|' + '|'.join(
            f'{b["xtype"]}/{b["sub"]}@{b["min"]}{b["fwd"]}{b["up"]}{b["name"]}' for b in self.blocks)
            + ''.join(f'|+{c.name}@{c.pose!r}|' + '|'.join(f'{b["xtype"]}/{b["sub"]}@{b["min"]}{b["fwd"]}{b["up"]}{b["name"]}' for b in c.blocks)
                      for c, _ in self.subgrids[:])).encode()).hexdigest()[:10]
        safe = re.sub(r'[^A-Za-z0-9 _.\-]', '', display).strip() or 'Blueprint'
        return f'{safe} [{sig}]'

    def _groups_xml(self):
        """<BlockGroups> element text for this grid ('' when there are no groups)"""
        if not self.groups: return ''
        return ser(TB.block_groups_element(self.groups, self.group_cell), 5)

    def _grid_xml(self, gid, display, world):
        """one <CubeGrid> element (8-space indent) at world pose `world`; ids must be assigned already"""
        po = ser(world.element('PositionAndOrientation'), 5)
        return f'''        <CubeGrid>
          <SubtypeName />
          <EntityId>{gid}</EntityId>
          <PersistentFlags>CastShadows InScene</PersistentFlags>
{po}          <LocalPositionAndOrientation xsi:nil="true" />
          <GridSizeEnum>{self.grid}</GridSizeEnum>
          <CubeBlocks>
{self._blocks_body()}          </CubeBlocks>
{self._groups_xml()}          <DisplayName>{escape(display)}</DisplayName>
          <DestructibleBlocks>true</DestructibleBlocks>
          <IsRespawnGrid>false</IsRespawnGrid>
          <LocalCoordSys>0</LocalCoordSys>
          <TargetingTargets />
          <NPCGridClaimElapsed xsi:nil="true" />
          <DampenersEnabled>true</DampenersEnabled>
        </CubeGrid>
'''

    def build_xml(self, display=None, version=''):
        display = display or self.name
        grids = self.grids()
        used = set()
        gids = {}
        for g, _ in grids:                 # per grid: the grid id first, then the block ids (the order a single grid always had)
            gid = g.rng.randint(10**17, 9 * 10**17)
            while gid in used: gid = g.rng.randint(10**17, 9 * 10**17)
            gids[id(g)] = gid; used.add(gid)
            g._assign_ids(used)
        dlc = ''.join(f'      <DLC>{d}</DLC>\n' for d in sorted({d for g, _ in grids for d in g.dlc_used()}))
        body = ''.join(g._grid_xml(gids[id(g)], display if g is self else (g.display or g.name), w) for g, w in grids)
        return f'''<?xml version="1.0"?>
<Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <ShipBlueprints>
    <ShipBlueprint xsi:type="MyObjectBuilder_ShipBlueprintDefinition">
      <Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="{escape(self.blueprint_id(display, version), {chr(34): "&quot;"})}" />
      <DisplayName>{escape(display)}</DisplayName>
{dlc}      <CubeGrids>
{body}      </CubeGrids>
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
        with open(os.path.join(outdir, 'bp.sbc'), 'w', encoding='utf-8', newline='\n') as fh: fh.write(xml)
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
    """Read a game-written bp.sbc -> [Blueprint, ...] one per CubeGrid (sub-grids included, in file order).
    Blocks whose definition is unknown / on another grid size are recorded in .skipped.
    Besides the geometry every block keeps: id (EntityId, preserved when written again), items (inventory), state (capacitor / fuel /
    tank fill / battery charge), toolbars ({tag: {index: item}}), name; every Blueprint keeps its world .pose, .groups (BlockGroups
    resolved to blocks) and grid_meta.  Use load_sbc_multi() for one Blueprint with the other grids attached."""
    root = ET.parse(path).getroot()
    out = []
    for gi, g in enumerate(root.iter('CubeGrid')):
        bp = Blueprint(g.findtext('DisplayName') or f'grid{gi}', g.findtext('GridSizeEnum') or 'Large', allow_dlc=allow_dlc)
        bp.grid_meta = dict(entity=g.findtext('EntityId'), index=gi)
        po = g.find('PositionAndOrientation')
        if po is not None and po.find('Position') is not None and po.find('Forward') is not None: bp.pose = MU.Pose.from_element(po)
        for e in g.find('CubeBlocks'):
            xt = e.get(XSI + 'type'); sub = e.findtext('SubtypeName') or ''
            k = defkey(xt, sub)
            if not k: bp.skipped.append((xt, sub, 'no definition in defs.json')); continue
            d = DEFS[k]
            if d['grid'] != bp.grid: bp.skipped.append((xt, sub, f'definition is {d["grid"]} grid')); continue
            mn = e.find('Min'); o = e.find('BlockOrientation')
            fwd, up = (o.get('Forward'), o.get('Up')) if o is not None else ('Forward', 'Up')
            blk = dict(xtype=xt, sub=sub, min=((int(mn.get('x', 0)), int(mn.get('y', 0)), int(mn.get('z', 0))) if mn is not None else (0, 0, 0)), fwd=fwd, up=up,
                       name=e.findtext('CustomName'), color=(0, 0, 0), extra='', kind='armor', size=tuple(d['size']),
                       mounts=d['mounts'], mass=0, custom_data=None, key=k, settings={}, links={}, id=0)
            eid = e.findtext('EntityId')
            if eid: blk['id'] = int(eid); blk['keep_id'] = True
            cc = e.find('ComponentContainer')
            its = INV.read_items(cc) if cc is not None else []
            if its: blk['items'] = its
            st = INV.read_state(e)
            if st: blk['state'] = st
            tbs = {t.tag: TB.read_toolbar(t) for t in e if t.tag in TB.TOOLBAR_TAGS}
            tbs = {t: sl for t, sl in tbs.items() if sl}
            if tbs: blk['toolbars'] = tbs
            bp.blocks.append(blk)
        bg = g.find('BlockGroups')
        if bg is not None:
            cellmap = {}
            for blk in bp.blocks:
                for c in bp.cells(blk): cellmap[c] = blk
            for grp in bg:
                members = []
                for v in grp.find('Blocks'):
                    blk = cellmap.get((int(v.findtext('X') or 0), int(v.findtext('Y') or 0), int(v.findtext('Z') or 0)))
                    if blk is not None and not any(blk is m for m in members): members.append(blk)
                bp.groups[grp.findtext('Name')] = members
        out.append(bp)
    return out

def load_sbc_multi(path, allow_dlc=True, main=0):
    """Read a (multi-grid) bp.sbc as ONE Blueprint: grid number `main` (file order) is the root, every other CubeGrid is attached with
    its pose relative to the root (so writing the result puts every grid back where the file had it)."""
    grids = load_sbc(path, allow_dlc)
    root_bp = grids[main]
    world = root_bp.pose
    for i, g in enumerate(grids):
        if i == main: continue
        root_bp.attach(g, world.inv() * g.pose)
    return root_bp
