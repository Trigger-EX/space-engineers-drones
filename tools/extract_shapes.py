"""Extract game-written block shapes from the owner's real blueprints into tools/shapes.json.

  python3 -I tools/extract_shapes.py                         # all of ref/user-samples + pc/enemies/Tech Hunter -> tools/shapes.json
  python3 -I tools/extract_shapes.py --fresh                 # same, but do not keep the pinned legacy entries (see below)
  python3 -I tools/extract_shapes.py --samples DIR --extra FILE.sbc ... -o OUT.json
  python3 -I tools/extract_shapes.py ref/Starburst/bp.sbc tools/shapes.json     # old two-argument form (one file)

For every block type we want to write, the game's own serialisation of a block is taken from the smallest *complete* exemplar
(non-DLC, no Prototech, fully built, not inside a ProjectedGrids hologram) and turned into a TEMPLATE: everything that
belongs to one entity is removed or reset (EntityId, Min, orientation, colour, owner, names are written by sebp.Blueprint.build_block;
toolbars and ids are emptied; turret aim, ammo state, door/timer/connector run state, cockpit pilot state, inventories are reset).
What remains is "what the game writes after the instance fields": the ComponentContainer, DeformationRatio and the derived-class
fields after <Enabled>.  Every spec records its source sample ("src").

Keys are "Type/Subtype" (exact subtype) and "Type/*" (smallest exemplar of the type; `container_generic` says whether the container
is identical for all seen subtypes, i.e. whether it may be used for an unseen subtype).  Keys starting with "_" are metadata
(`_meta`, `_centers`: the block "Center" cell the game writes in BlockGroups, derived from the samples).

Legacy entries: the first 40 shapes of tools/shapes.json came from the reference ship Starburst and are used, byte for byte, by the
shipped drones.  Unless --fresh is given they are PINNED: kept from the existing output file exactly as they are.  The file
has two sections, "legacy" (pinned) and "shapes" (fresh); sebp.Blueprint(..., legacy_shapes=True) reads only "legacy" (that is
how the shipped drones keep their XML byte for byte).
"""
import sys, os, json, re, argparse, glob
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..'))
sys.path.insert(0, HERE)
import xml.etree.ElementTree as ET
import sebp
from sebp import ser, frag, E, XSI

# types the generator may write (legacy list first, then the warship needs)
WANT = ['Thrust', 'CameraBlock', 'BatteryBlock', 'RadioAntenna', 'RemoteControl', 'TurretControlBlock', 'SmallMissileLauncherReload',
        'SmallMissileLauncher', 'SmallGatlingGun', 'Gyro', 'ShipConnector', 'MyProgrammableBlock', 'EventControllerBlock',
        'Projector', 'ShipWelder',
        'LargeGatlingTurret', 'LargeMissileTurret', 'InteriorTurret', 'Cockpit', 'Reactor', 'HydrogenEngine', 'OxygenTank', 'Decoy',
        'SensorBlock', 'CargoContainer', 'Conveyor', 'ConveyorConnector', 'ConveyorSorter', 'LandingGear', 'Door',
        'AirtightHangarDoor', 'AirtightSlideDoor', 'ButtonPanel', 'TimerBlock', 'MergeBlock', 'Warhead']
BASE = {'SubtypeName', 'EntityId', 'Min', 'IntegrityPercent', 'BuildPercent', 'BlockOrientation', 'ColorMaskHSV', 'SkinSubtypeId',
        'ConstructionInventory', 'ConstructionStockpile', 'Owner', 'BuiltBy', 'ShareMode', 'DeformationRatio', 'ComponentContainer',
        'CustomName', 'ShowOnHUD', 'ShowInTerminal', 'ShowInToolbarConfig', 'ShowInInventory', 'NumberInGrid', 'Enabled'}
TOOLBARS = {'Toolbar', 'BuildToolbar', 'OnLockedToolbar'}
ZERO = {'AzimuthId', 'ElevationId', 'CameraId', 'DirectionBlockId', 'BindedCamera', 'AttachedPB'}
# run state / user settings that must not leak from the exemplar into a fresh block: tag -> text ('' = empty element)
RESET = {'Rotation': '0', 'Elevation': '0', 'RemainingAmmo': '0', 'IsShooting': 'false', 'IsShootingFromTerminal': 'false',
         'TargetingGroup': '', 'CurrentTime': '0', 'IsCountingDown': 'false', 'Open': 'false', 'CurrOpening': '0', 'Opening': '0',
         'FilledRatio': '0', 'OxygenLevel': '0', 'SitAnimation': '0', 'IsInFirstPersonView': 'false', 'TimeOfConnection': '0',
         'IsApproaching': 'false', 'IsConnecting': 'false', 'FirstLockAttempt': 'false', 'IsLocked': 'false', 'HudText': ''}
RESET_BY_TYPE = {'TimerBlock': {'Delay': '10000'}}              # a user setting in the exemplar -> the game's default
DROP = {'Strength', 'ConnectedEntityId', 'ThrustOverride', 'GyroPower', 'IsMainCockpit', 'Range', 'State', 'ThrowOut', 'CollectAll',
        'Program', 'Storage'}
NIL = {'PilotJetpackEnabled', 'SelectedGunId', 'PilotGunDefinition', 'GearPivotPosition', 'OtherPivot', 'AttachedEntityId',
       'MasterToSlave', 'MasterToSlaveTransform', 'MasterToSlaveGrid', 'PreviousControlledEntityId'}
FUEL_TYPES = {'Reactor', 'HydrogenEngine'}     # <Capacity> is the stored fuel there -> 0 for a fresh block (set with state=)
IDENT_POSE = ('<PilotRelativeWorld><Position x="0" y="0" z="0" /><Forward x="0" y="0" z="-1" /><Up x="0" y="1" z="0" />'
              '<Orientation><X>0</X><Y>0</Y><Z>0</Z><W>1</W></Orientation></PilotRelativeWorld>')

def clean_container(cc):
    comps = cc.find('Components')
    for cd in list(comps):
        tid = cd.findtext('TypeId')
        if tid == 'MyModStorageComponentBase': comps.remove(cd); continue      # holds script/CustomData; sebp adds it on demand
        c = cd.find('Component')
        if tid == 'MyInventoryBase':
            c.find('Items').clear(); c.find('nextItemId').text = '0'
        elif tid == 'MyTimerComponent':
            c.find('FramesFromLastTrigger').text = '0'
        elif tid == 'MyEntityCapacitorComponent':
            for ch in list(c): c.remove(ch)               # an empty capacitor, as the game writes it (the full charge is recorded as capacitor_full)
    return cc if len(comps) else None

def clean_tail(e, btype=None):
    t = e.tag
    if t in DROP: return None
    if t in TOOLBARS:
        n = frag(sebp.EMPTY_TOOLBAR)[0]; n.tag = t; return n
    if t in ZERO: n = E(t, 0); return n
    if t == 'ToolIds' or t == 'SelectedBlocks': return E(t)
    if t == 'ProjectedGrids': return E(t)
    if t in ('ProjectionOffset', 'ProjectionRotation'):
        n = ET.Element(t)
        for a in 'XYZ': n.append(E(a, 0))
        return n
    if t in ('ProjectionsRemaining',): return E(t, 0)
    if t == 'GunBase':
        g = ET.Element('GunBase')
        g.append(E('CurrentAmmoMagazineName', e.findtext('CurrentAmmoMagazineName') or None))
        g.append(E('RemainingAmmosList')); g.append(E('LastShootTime', 0)); return g
    if t == 'PilotRelativeWorld': return frag(IDENT_POSE)[0]
    if t == 'TargetData':
        n = ET.Element(t); n.append(E('TargetId', 0)); n.append(E('IsTargetLocked', False)); n.append(E('LockingProgress', 0)); return n
    if t == 'CustomButtonNames':
        n = ET.Element(t); n.append(ET.Element('dictionary')); return n
    if t == 'DefinitionIds': return E(t)
    if t in NIL:
        n = ET.Element(t); n.set(XSI + 'nil', 'true'); return n
    if t == 'Capacity' and btype in FUEL_TYPES:
        return E(t, 0)
    if t == 'Capacity':
        return e                                  # not a fuel level on this type
    if t in RESET_BY_TYPE.get(btype, {}):
        n = E(t); n.text = RESET_BY_TYPE[btype][t]; return n
    if t in RESET:
        if t == 'FilledRatio' and btype != 'OxygenTank': return e
        n = E(t); n.text = RESET[t] or None; return n
    return e

def exemplar(b, btype=None):
    spec = {'level': 'func' if b.find('Enabled') is not None else ('term' if b.find('ShowOnHUD') is not None else 'cube'),
            'owner': b.find('ShareMode') is not None}
    if b.find('DeformationRatio') is not None: spec['deform'] = float(b.findtext('DeformationRatio'))
    if b.find('Enabled') is not None and b.findtext('Enabled') == 'false': spec['enabled'] = False
    cc = b.find('ComponentContainer')
    if cc is not None:
        cc = clean_container(cc)
        if cc is not None: spec['container'] = ser(cc).strip()
    tail = []
    for c in b:
        if c.tag in BASE: continue
        n = clean_tail(c, btype)
        if n is not None: tail.append(n)
    spec['tail'] = ''.join(ser(t, 0) for t in tail)
    return spec

def container_skeleton(spec):
    """what must be equal for a container to be reusable for another subtype: the component list (and surface count of text panels)"""
    if not spec.get('container'): return None
    cc = frag(spec['container'])[0]
    out = []
    for cd in cc.find('Components'):
        c = cd.find('Component')
        n = len(c.findall('.//MySerializedTextPanelData')) if cd.findtext('TypeId') == 'MyMultiTextPanelComponent' else 0
        out.append((cd.findtext('TypeId'), n))
    return out

def repo_roots():
    """this checkout, plus the main checkout when this is a git worktree (ref/ is not tracked, so a worktree has no samples)"""
    roots = [ROOT]
    try:
        import subprocess
        gc = subprocess.run(['git', '-C', ROOT, 'rev-parse', '--git-common-dir'], capture_output=True, text=True, timeout=20).stdout.strip()
        if gc:
            main = os.path.dirname(os.path.normpath(os.path.join(ROOT, gc)))
            if main not in roots: roots.append(main)
    except (OSError, ValueError, subprocess.SubprocessError):
        pass
    return roots

def find_repo_path(*rel):
    """first existing <root>/<rel> over repo_roots(); env SEBP_SAMPLES overrides ref/user-samples"""
    if rel == ('ref', 'user-samples') and os.environ.get('SEBP_SAMPLES'): return os.environ['SEBP_SAMPLES']
    for r in repo_roots():
        p = os.path.join(r, *rel)
        if os.path.exists(p): return p
    return os.path.join(ROOT, *rel)

def sample_files(samples_dir, extra):
    fs = sorted(glob.glob(os.path.join(samples_dir, '*', 'bp.sbc'))) if samples_dir else []
    return fs + [f for f in extra if os.path.exists(f)]

def sample_name(path):
    return os.path.basename(os.path.dirname(os.path.abspath(path)))

def real_grids(root):
    """CubeGrid elements that are real grids (not ProjectedGrids holograms)"""
    nested = set()
    for pg in root.iter('ProjectedGrids'):
        for g in pg.iter('MyObjectBuilder_CubeGrid'): nested.add(id(g))
        for g in pg.iter('CubeGrid'): nested.add(id(g))
    return [g for g in root.iter('CubeGrid') if id(g) not in nested]

def eligible(b, t, sub):
    if b.find('BuildPercent') is not None: return False          # still under construction (a leftover ConstructionStockpile or damage do not matter)
    k = sebp.defkey(t, sub)
    if not k or sebp.DEFS[k]['dlc'] or 'Prototech' in sub: return False
    return True

def centers_from(files):
    """block Center cell (local) written in BlockGroups, per definition: intersect the candidates over all observations"""
    import itertools
    cands, nobs = {}, {}
    for f in files:
        root = ET.parse(f).getroot()
        for g in real_grids(root):
            gb, bg = g.find('CubeBlocks'), g.find('BlockGroups')
            if gb is None or bg is None: continue
            bp = sebp.Blueprint('x', g.findtext('GridSizeEnum') or 'Large', allow_dlc=True)
            bycell = {}
            for e in gb:
                xt = e.get(XSI + 'type'); sub = e.findtext('SubtypeName') or ''
                k = sebp.defkey(xt, sub)
                if not k: continue
                d = sebp.DEFS[k]
                mn, o = e.find('Min'), e.find('BlockOrientation')
                fwd, up = (o.get('Forward'), o.get('Up')) if o is not None else ('Forward', 'Up')
                m = (int(mn.get('x', 0)), int(mn.get('y', 0)), int(mn.get('z', 0))) if mn is not None else (0, 0, 0)
                b = dict(xtype=xt, sub=sub, min=m, fwd=fwd, up=up, size=tuple(d['size']), name=None, mounts=d['mounts'], key=k)
                for c in bp.cells(b): bycell[c] = b
            for grp in bg:
                for v in grp.find('Blocks'):
                    p = (int(v.findtext('X') or 0), int(v.findtext('Y') or 0), int(v.findtext('Z') or 0))
                    b = bycell.get(p)
                    if not b: continue
                    ok = set()
                    for loc in itertools.product(*[range(s) for s in b['size']]):
                        if sebp.local_cell_to_grid(bp, b, loc) == p: ok.add(loc)
                    cands[b['key']] = ok if b['key'] not in cands else cands[b['key']] & ok
                    nobs[b['key']] = nobs.get(b['key'], 0) + 1
    return {k: list(sorted(v)[0]) for k, v in sorted(cands.items()) if len(v) == 1}

def looks_generated(root):
    """True for a blueprint written by OUR generator, not by the game: the owner's samples folder holds a few old sebp outputs
    (Gnat, Hornet Drone, Longbow S/L: no ComponentContainer and no Owner on any block although the ship has dozens of blocks).
    They must never be used as ground truth for a block shape."""
    blocks = [b for g in real_grids(root) for b in (g.find('CubeBlocks') if g.find('CubeBlocks') is not None else [])]
    func = [b for b in blocks if b.find('ShareMode') is not None or b.find('Enabled') is not None]
    if len(func) < 5: return False
    return root.find('.//ComponentContainer') is None and root.find('.//Owner') is None

def signature(sp):
    """the structure of a template (not its values): blocks of one game version share it"""
    cont = tuple(container_skeleton(sp) or ())
    tags = tuple(c.tag for c in frag(sp['tail'])) if sp['tail'] else ()
    return (sp['level'], sp['owner'], sp.get('deform') is not None, cont, tags)

def capacitor_charge(b):
    cc = b.find('ComponentContainer')
    if cc is None: return None
    for cd in cc.find('Components'):
        if cd.findtext('TypeId') == 'MyEntityCapacitorComponent':
            return float(cd.find('Component').findtext('StoredPower') or 0)
    return None

def extract(files, want=WANT, skipped=None):
    cands = {}             # key -> {signature: [count, size, name, spec]}
    capmax = {}            # key -> largest capacitor charge seen (= the full charge of that railgun)
    per_type = {}          # type -> {sub: spec}
    for f in files:
        name = sample_name(f)
        root = ET.parse(f).getroot()
        if looks_generated(root):
            if skipped is not None: skipped.append(name)
            continue
        for g in real_grids(root):
            gb = g.find('CubeBlocks')
            if gb is None: continue
            for b in gb:
                t = b.get(XSI + 'type').replace('MyObjectBuilder_', '')
                if t not in want: continue
                sub = b.findtext('SubtypeName') or ''
                if not eligible(b, t, sub): continue
                ch = capacitor_charge(b)
                if ch is not None: capmax[f'{t}/{sub}'] = max(capmax.get(f'{t}/{sub}', 0.0), ch)
                sp = exemplar(b, t); sp['src'] = name; size = len(json.dumps(sp)); sig = signature(sp)
                for k in (f'{t}/{sub}', f'{t}/*'):
                    d = cands.setdefault(k, {})
                    c = d.get(sig)
                    if c is None: d[sig] = [1, size, name, sp]
                    else:
                        c[0] += 1
                        if (size, name) < (c[1], c[2]): c[1], c[2], c[3] = size, name, sp
                per_type.setdefault(t, {})
                per_type[t].setdefault(sub, {})[sig] = sp
    best = {}
    for k, d in cands.items():
        # the form the (newest) game writes most often wins; among equal forms the smallest exemplar, then the sample name
        best[k] = min(d.values(), key=lambda c: (-c[0], c[1], c[2]))[3]
    out = {k: best[k] for k in sorted(best)}
    for k, v in capmax.items():
        if k in out and v > 0: out[k]['capacitor_full'] = v
    for t, subs in per_type.items():
        skels = {json.dumps(container_skeleton(out[f'{t}/{sub}'])) for sub in subs if f'{t}/{sub}' in out}
        if f'{t}/*' in out and len(skels) == 1 and container_skeleton(out[f'{t}/*']) is not None:
            out[f'{t}/*']['container_generic'] = True
    return out

# types never seen in a sample whose game class is a sibling of a seen one: MyObjectBuilder_InteriorTurret derives from
# MyObjectBuilder_LargeTurretBase exactly like the large gatling / rocket turrets (Keen source, Jan 2022): same serialized fields.
DERIVED = {'InteriorTurret': 'LargeGatlingTurret'}

def derive_shapes(out):
    """add `derived_from` shapes for the types in DERIVED (copies of the sibling's `Type/*` with the definition's inventory
    volume and default magazine); the confidence of these is 'derived', never 'seen'"""
    for t, src_t in DERIVED.items():
        base = out.get(f'{src_t}/*')
        if not base or not base.get('container_generic'): continue
        subs = sorted(k.split('/', 1)[1] for k, d in sebp.DEFS.items() if k.split('/')[0].replace('MyObjectBuilder_', '') == t and not d['dlc'])
        first = None
        for sub in subs:
            key = sebp.defkey(t, sub)
            sp, why = sebp._retarget_container(base, key)
            if why: continue
            sp = sebp.retarget_weapon(sp, key)
            sp = dict(sp); sp.pop('container_generic', None)
            sp['derived_from'] = src_t; sp['src'] = f"{src_t}/* ({base['src']})"
            out[f'{t}/{sub}'] = sp; first = first or sp
        if first: out[f'{t}/*'] = first

def load_legacy(path):
    """the pinned legacy section of an existing output file (or the whole file when it is still in the old flat format)"""
    try: old = json.load(open(path))
    except (OSError, ValueError): return {}
    if 'shapes' in old or 'legacy' in old: return dict(old.get('legacy', {}))
    return {k: v for k, v in old.items() if not k.startswith('_') and isinstance(v, dict)}

def annotate_legacy(legacy, new):
    """add 'src' to every pinned legacy entry that a fresh extraction reproduces; list the keys where it does not"""
    out, notes = {}, []
    for k, v in legacy.items():
        sp = dict(v); n = new.get(k)
        if n is not None and 'src' not in sp:
            cmp_a = {a: b for a, b in n.items() if a not in ('src', 'container_generic')}
            cmp_b = {a: b for a, b in v.items() if a not in ('src', 'container_generic')}
            if cmp_a == cmp_b: sp['src'] = n['src']
            else: notes.append(k)
        out[k] = sp
    return out, notes

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('paths', nargs='*', help='legacy form: SRC.sbc DST.json')
    ap.add_argument('--samples', default=None, help='default: ref/user-samples of this or the main checkout (env SEBP_SAMPLES)')
    ap.add_argument('--extra', action='append', default=None)
    ap.add_argument('-o', '--out', default=os.path.join(HERE, 'shapes.json'))
    ap.add_argument('--fresh', action='store_true', help='do not pin the legacy entries of the existing output file')
    a = ap.parse_args(argv)
    if len(a.paths) == 2:
        files, out_path, pin = [a.paths[0]], a.paths[1], False
    elif a.paths:
        ap.error('give either no positional argument or SRC.sbc DST.json')
    else:
        extra = a.extra if a.extra is not None else [find_repo_path('pc', 'enemies', 'Tech Hunter', 'bp.sbc')]
        files, out_path, pin = sample_files(a.samples or find_repo_path('ref', 'user-samples'), extra), a.out, not a.fresh
    if not files: sys.exit('no sample files found')
    skipped = []
    shapes = extract(files, skipped=skipped)
    derive_shapes(shapes)
    doc = {'_meta': {'format': 3, 'tool': 'tools/extract_shapes.py', 'samples': sorted({sample_name(f) for f in files} - set(skipped)), 'not_game_written': sorted(skipped),
                     'note': 'shapes: sample-derived templates (default); legacy: pinned entries used byte for byte by the shipped drones '
                             '(Blueprint(legacy_shapes=True))'}}
    notes = []
    if pin:
        legacy, notes = annotate_legacy(load_legacy(out_path), shapes)
        doc['legacy'] = legacy
    doc['shapes'] = shapes
    if len(files) > 1: doc['_centers'] = centers_from(files)
    with open(out_path, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(doc, fh, indent=1, sort_keys=True); fh.write('\n')
    if skipped: print('  skipped (written by our own generator, not by the game):', ', '.join(sorted(skipped)))
    print(f'{len(shapes)} shapes + {len(doc.get("legacy", {}))} pinned legacy from {len(files)} files -> {out_path}')
    for n in notes: print('  note: legacy', n, 'differs from a fresh extraction (kept the legacy entry for legacy_shapes=True)')
    for k in sorted(shapes):
        if k.endswith('/*'): print('  ', k, '<-', shapes[k].get('src'))

if __name__ == '__main__':
    main()
