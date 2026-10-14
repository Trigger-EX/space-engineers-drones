"""Scripted drone printers (projector + welders + merge block bays, controlled by scripts/HiveMotherAI.cs).

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Every printer exists as "<Name>" (full) and
"<Name> Lite".  `python3 -I tools/designs/printers.py [name ...]` builds + validates every printer and variant and prints one PASS/FAIL
line each (blocks, mass, accel per axis, bays, welders); `python3 -I tools/designs/reach.py` runs the full reach/contact report.

WHAT A PRINTER IS
  * ONE dynamic small-grid ship (the XML never carries <IsStatic>; do not convert it to a station, a static printer would turn every
    drone static).  A 5x5x5 equipment pod (large battery, remote control, PB with HiveMotherAI.cs, 3 gyros, antenna, thrusters in all six
    directions) with docking bays on stalks.
  * A bay = projector [Bay N] (the drone blueprint is PRE-LOADED, Keep Projection ON) + SmallShipWelder(s) [Bay N] + merge block [Bay N]
    + the armour scaffold that carries them.  The drone is projected in the printer's own axes (no rotation); the printer merge block
    sits in the cell the drone's merge block clamps toward, clamp faces opposite (MERGE_CLAMP_FACE_SMALL, unverified in game).
  * FULL printers project the FULL drone (sensor / AI blocks) and carry two component-stock cargo containers; LITE printers project the
    LITE drone and carry no cargo container (cargo containers are in Starburst but not in shapes.json, so their XML is a guess;
    welders keep their own inventory).  Lite and Full both use projector / welder / merge shapes: projector + welder are SEEN in Starburst,
    the small merge block is from Keen source (not seen), the cargo container is a guess.

GEOMETRY GUARANTEES (tools/designs/reach.py checks them again independently, for every bay of every printer)
  * every drone block is within reach of a welder under a FAMILY of sphere-centre hypotheses (centre at the tip face, 1, 2, 3 cells inside the
    welder; radius 1.3 m and 1.0 m), see docs/welder-reach.md: tier A (tip and tip-1) is mandatory except where no legal welder position can
    reach, the deeper hypotheses and a second welder per block are best effort; every drone block is also SUPPORTED (mount-point chain from the
    merge pair, so the welders can actually build it);
  * drone and printer never overlap; the drone touches the printer only through the merge pair (armour, projector, stalk keep out of
    the drone's 26-neighbourhood; a welder TIP may touch the drone only where it has no mount point, flagged gap=0 in the notes);
  * exhaust, muzzle and camera clear zones of the drone are free of printer blocks (camera zones are the first to be relaxed, reported);
  * no welder of one bay reaches the drone of another bay; printer thruster flames do not hit the docked drones.

UNVERIFIED IN GAME (see the final report): small merge block clamp face, whether welded blocks separate from the projector grid when the
merge pair is switched off (they are built INTO the projector's grid), projected PB behaviour, the exact welder sensor centre, the
projection offset sign (checked against the Starburst ship, it fits exactly).  The welder sphere centre is NOT known (the dummy position in
Welder.mwm is unpublished), hence the hypothesis family.

CUSTOM DATA for HiveMotherAI.cs, per [Bay N]: Exit Direction, Drone Box Min / Drone Box Max (printer grid cells, translation T included), Drone Cell
Count (unique drone blocks), Drone Occupied Cells, Ignore Cells; and a [Launch] section (Auto=true, Burst Seconds=4).
"""
import sys, os, time, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import printer_lib as PL
import reach

# ---------------------------------------------------------------------------------------------
# drone catalogue: key -> (module, model key, IGC tag, needs latch)
# ---------------------------------------------------------------------------------------------
DRONES = {
    'Gnat':       ('core',    'Gnat',      'GNAT',     False),
    'Hornet':     ('core',    'Hornet',    'HORNET',   False),
    'Longbow S':  ('core',    'Longbow S', 'LONGBOW',  True),
    'Wasp':       ('wasp',    'Wasp',      'WASP',     False),
    'Lancer':     ('wasp',    'Lancer',    'LANCER',   False),
    'Watcher':    ('support', 'Watcher',   'HORNET',   False),
    'Harrier':    ('extra',   'Harrier',   'HARRIER',  False),
    'Aegis':      ('extra',   'Aegis',     'AEGIS',    False),
    'Mantis':     ('extra',   'Mantis',    'MANTIS',   True),
    'Stinger':    ('stinger', 'Stinger',   'STINGER',  False),
    'Peeper':     ('peeper',  'Peeper',    'PEEPER',   False),
    'Midge':      ('support', 'Midge',     'MIDGE',    False),       # scriptless (AI blocks): Hive Mother switches its AI behaviour on at release
    # tube rams (TubeRamAI v1.1: held while welded to the printer, _SWARM heartbeat): no merge block of their own -> latch stalk
    **{f'Tube {s}{w}': ('tube', f'Tube {s}{w}', 'TUBE', True) for s in ('2x2', '2x4', '3x3', '4x4') for w in ('', ' W')},
}
_DRONE_CACHE = {}
# drones that can only leave their bay one way: the Tubes have no reverse thrust and weak side thrusters, so they fly out nose first (drone frame)
EXIT_DIR = {k: (0, 0, -1) for k in DRONES if k.startswith('Tube ')}

def _stinger_fallback(lite):
    """minimal kamikaze Stinger used ONLY when tools/designs/stinger.py does not exist: the Gnat frame (2 axial ion thrusters, gyro steering,
    belly merge latch) with the gatling replaced by two small warheads.  Lite = no sensor."""
    from core import gnat_frame
    name = 'Stinger Lite' if lite else 'Stinger'
    bp = gnat_frame(script_text('StingerAI.cs'), merge=True, sensor=not lite, name=name)
    g = [b for b in bp.blocks if b['kind'] == 'gun'][0]
    bp.blocks.remove(g)
    for c in [c for c, why in bp.reserve.items() if why.endswith(g['name'])]: del bp.reserve[c]
    for i, z in enumerate((-3, -2)):
        bp.add('MyObjectBuilder_Warhead', 'SmallWarhead', (0, 0, z), 'Forward', 'Up', f'{name} Warhead {i + 1}', (0.98, 0.65, 0.05), kind='warhead')
    # removing the gatling left blocks that hung on it (the left camera): drop everything not attached to the merge block by mount points
    adj = bp.attachment(); m = [b for b in bp.blocks if b['kind'] == 'merge'][0]
    seen = {id(m)}; todo = [id(m)]
    while todo:
        for n in adj[todo.pop()]:
            if n not in seen: seen.add(n); todo.append(n)
    for b in [b for b in bp.blocks if id(b) not in seen]: bp.blocks.remove(b)
    return finalize(bp)

def get_drone(key, lite):
    k = (key, lite)
    if k not in _DRONE_CACHE:
        import importlib
        mod, model, tag, latch = DRONES[key]
        if mod == 'stinger' and not HAVE_STINGER: bp = _stinger_fallback(lite)
        else: bp = importlib.import_module(mod).MODELS[model](lite)
        if latch: bp = PL.with_flush_latch(bp)            # outline rule (CLAUDE.md): merge block set into the hull, never on a stalk
        _DRONE_CACHE[k] = bp
    return _DRONE_CACHE[k]

# ---------------------------------------------------------------------------------------------
# printer specifications
# ---------------------------------------------------------------------------------------------
#  groups: (drone key, bays, (na, nb) slot array)       per_dir: ion thrusters per direction (0 = none, a stationary nest)
SPECS = [
    dict(name='Hive Mother',     groups=[('Gnat', 2, (2, 1))],  max=8,  per_dir=2,
         doc='2 bays, prints Gnats (swarm drone).'),
    dict(name='Twin Hive',       groups=[('Gnat', 4, (2, 2))],  max=12, per_dir=2,
         doc='4 bays in a 2x2 array, prints Gnats.'),
    dict(name='Quad Hive',       groups=[('Gnat', 8, (4, 2))],  max=24, per_dir=3,
         doc='8 compact bays in a 4x2 array, prints Gnats.'),
    dict(name='Hornet Forge',    groups=[('Hornet', 2, (2, 1))], max=6,  per_dir=2,
         doc='2 bays, prints Hornets (twin gatling fighters).'),
    dict(name='Wasp Foundry',    groups=[('Wasp', 2, (2, 1))],  max=6,  per_dir=2,
         doc='2 bays, prints Wasps (rocket launchers).'),
    dict(name='Lancer Foundry',  groups=[('Lancer', 2, (2, 1))], max=6, per_dir=2,
         doc='2 bays, prints Lancers (autocannons).'),
    dict(name='Longbow Foundry', groups=[('Longbow S', 2, (2, 1))], max=4, per_dir=2,
         doc='2 big bays, prints Longbow S railgun snipers (docking stalk added by printer_lib.with_latch).'),
    dict(name='Harrier Forge',   groups=[('Harrier', 2, (2, 1))], max=6, per_dir=2,
         doc='2 bays, prints Harrier interceptors.'),
    dict(name='Aegis Shipyard',  groups=[('Aegis', 2, (2, 1))], max=4,  per_dir=2,
         doc='2 bays, prints Aegis escorts (4 gatlings, decoys).'),
    dict(name='Mantis Foundry',  groups=[('Mantis', 2, (2, 1))], max=4, per_dir=2,
         doc='2 bays, prints Mantis hybrids (railgun + gatling; docking stalk added).'),
    dict(name='Watcher Pad',     groups=[('Watcher', 2, (2, 1))], max=4, per_dir=2,
         doc='2 bays, prints unarmed Watcher scouts (feed the HORNET hive).'),
    dict(name='Fleet Forge',     groups=[('Gnat', 2, (2, 1)), ('Hornet', 1, (1, 1)), ('Watcher', 1, (1, 1))], max=12, per_dir=3,
         caps={'GNAT': 6, 'HORNET': 3}, doc='mixed bays: 2 Gnats on top, a Hornet and a Watcher in front.'),
    dict(name='Escort Carrier',  groups=[('Hornet', 2, (2, 1))], max=6, per_dir=3, heavy=True, guns=4, relay=True, turret=True,
         doc='Hornet bays + heavy armour, 4 fixed gatlings and the turret-target relay.'),
    dict(name='Mobile Hive',     groups=[('Gnat', 4, (2, 2))], max=12, per_dir=4,
         doc='4 Gnat bays and 4 ion thrusters per direction: keeps up with its brood.'),
    dict(name='Static Nest',     groups=[('Gnat', 6, (3, 2))], max=18, per_dir=0,
         doc='6 Gnat bays, no thrusters: a stationary hive (still a DYNAMIC grid; never convert it to a station).'),
    dict(name='Probe Foundry',   groups=[('Peeper', 1, (1, 1))], max=1, per_dir=0,
         doc='1 bay, prints Peeper vitals probes (a stationary foundry: the Ballista carries one docked on connectors).'),
    dict(name='Midge Nest',      groups=[('Midge', 2, (2, 1))], max=6, per_dir=2,
         doc='2 bays, prints scriptless Midges (AI blocks, EXPERIMENTAL) and switches their AI behaviour on as they leave; 3 prints per bay (it cannot report in), then \'reload\'.'),
    dict(name='Tube Foundry',    groups=[('Tube 2x2', 1, (1, 1)), ('Tube 2x4', 1, (1, 1)), ('Tube 3x3', 1, (1, 1)), ('Tube 4x4', 1, (1, 1))], max=8, per_dir=3,
         caps={'TUBE': 8}, doc='one bay per small Tube ram (2x2, 2x4, 3x3, 4x4): reprints each tube that rams.'),
    dict(name='Tube W Foundry',  groups=[('Tube 2x2 W', 1, (1, 1)), ('Tube 2x4 W', 1, (1, 1)), ('Tube 3x3 W', 1, (1, 1)), ('Tube 4x4 W', 1, (1, 1))], max=8, per_dir=3,
         caps={'TUBE': 8}, doc='the warhead Tubes (2x2 W .. 4x4 W), one bay each.'),
    dict(name='Stinger Rack',    groups=[('Stinger', 4, (2, 2))], max=4, per_dir=2, rack=True,
         doc='RACK MODE: 4 Stingers stay docked; one is released per enemy sighting and told to launch.'),
]

# ---------------------------------------------------------------------------------------------
def build(spec, lite):
    pr = PL.Printer(spec['name'], lite)
    pr.pod(heavy=spec.get('heavy', False), cargo=not lite)
    cfg = []
    for key, count, grid in spec['groups']:
        drone = get_drone(key, lite)
        tag = DRONES[key][2]
        nums = pr.add_group(drone, count, grid, exit_dir=EXIT_DIR.get(key))
        # one drone type: its cap is the printer's Max Drones (Hive Mother stopped at 6 of 8 with 3 per bay); mixed printers share it out
        cap = (spec.get('caps') or {}).get(tag, spec['max'] if len(spec['groups']) == 1 else max(4, 3 * count))
        for n in nums:
            scriptless = not any('ProgrammableBlock' in b['xtype'] for b in drone.blocks)      # never reports in: cap the prints instead
            cfg.append(dict(n=n, tag=tag, prefix=drone.name, cap=cap, max_prints=max(1, cap // count) if scriptless else 0, exit=next(r['exit'] for r in pr.records if r['n'] == n)))

    if spec.get('turret') and not lite: pr.add_turret()
    if spec.get('guns'): pr.add_guns(spec['guns'])
    # survival conveyors BEFORE the thrusters (their exhaust zones then route round the pipes), and more thrusters for the pipes' mass:
    # the per-direction count grows with the mass so the printer keeps the acceleration it had without conveyors
    m0 = C.total_mass(pr.bp); pr.route_conveyors(); m1 = C.total_mass(pr.bp)
    if spec.get('per_dir'): pr.add_thrusters(int(math.ceil(spec['per_dir'] * m1 / m0 - 1e-9)))
    else: pr.tcount = {}
    pr.add_antenna(5000)
    for cf in cfg: cf['geom'] = PL.bay_geometry(pr, next(r for r in pr.records if r['n'] == cf['n']))
    comps = {}
    for bay in pr.records:
        comps.setdefault(bay['drone'].name, cost_components_of(bay['drone']))
    comments = ['Components for ONE drone of each type (stock the [Stock] cargo / feed the welders in survival; a creative world needs none):']
    for dn, cc in comps.items(): comments.append(f'{dn}: ' + ', '.join(f'{k} {v}' for k, v in sorted(cc.items())))
    ini = PL.hive_ini(cfg, spec['max'], rack=spec.get('rack', False), relay=spec.get('relay', False), comments=comments)
    bp = pr.finish(script_text('HiveMotherAI.cs'), ini)
    bp.spec = spec; bp.printer = pr
    PL.check_lanes(pr, bp)                   # every bay's exit lane free of printer blocks and other bays' drones (build fails otherwise)
    return bp

DOCS = {s['name']: s['doc'] for s in SPECS}          # one-line description per printer (for the package README)

def _model(spec):
    return lambda lite=False: build(spec, lite)

try:
    import stinger as _stinger       # noqa: F401  (another agent's module; Stinger Rack exists only when it does)
    HAVE_STINGER = True
except ImportError:
    HAVE_STINGER = False
MODELS = {s['name']: _model(s) for s in SPECS}

# ---------------------------------------------------------------------------------------------
def cost_components_of(drone):
    tot = {}
    for b in drone.blocks:
        for comp in (C._stat(b).get('components') or []):
            tot[comp['subtype']] = tot.get(comp['subtype'], 0) + comp['count']
    return tot

def cost_components(bp):
    """total component list needed to build the printer's DRONES (what the [Stock] cargo should hold): from defs_stats components"""
    tot = {}
    for bay in bp.bays:
        for b in bay['drone'].blocks:
            for comp in (C._stat(b).get('components') or []):
                tot[comp['subtype']] = tot.get(comp['subtype'], 0) + comp['count']
    return tot

def report(name, lite):
    label = name + (' Lite' if lite else '')
    t0 = time.time()
    try:
        bp = MODELS[name](lite)
    except AssertionError as e:
        return False, f'FAIL {label}: build: {e}'
    ok, lines = reach.verify_printer(bp)
    # thruster flames must not reach the docked drones
    for bay in bp.bays:
        dc = {tuple(c[k] + bay['T'][k] for k in range(3)) for b, cs in reach.drone_world(bay['drone'], (0, 0, 0)) for c in cs}
        dc = {tuple(c[k] + bay['T'][k] for k in range(3)) for b in bay['drone'].blocks for c in bay['drone'].cells(b)}
        hit = [c for c in dc if c in bp.reserve and bp.reserve[c].startswith('exhaust')]
        if hit: ok = False; lines.append(f'  bay {bay["n"]}: printer thruster exhaust reaches the drone at {hit[:3]}')
    acc, m = C.twr(bp)
    st = bp.stats()
    docked = sum(C.total_mass(bay['drone']) for bay in bp.bays)
    cs = C.cost(bp)
    wel = sum(len(b['welders']) for b in bp.bays)
    gaps = sorted({bay['drone'].name + ':' + str(PL.solve_bay_auto(bay['drone']).stats['gap']) for bay in bp.bays})
    txt = (f'{"PASS" if ok else "FAIL"} {label}: {len(bp.bays)} bays, {wel} welders, {len(bp.blocks)} blocks, mass {m:.0f} kg '
           f'(+{docked:.0f} kg when all drones are docked), dims {st["dims"]} m, accel m/s2 '
           + ' '.join(f'{d[:2]}{a:.1f}' for d, a in acc.items())
           + ' | cost ' + ' '.join(f'{k} {cs[k]:.0f}' for k in C.COST_KEYS if cs.get(k)) + f' | weld-gap {",".join(gaps)} | {time.time() - t0:.0f}s')
    return ok, txt, lines

PAL_PRINTER = dict(PAL, welder=(70, 190, 90), proj=(150, 90, 40), cargo=(120, 100, 70))

def write(name, lite, root):
    """write out/Blueprints-style folder  <root>/<Name>[ Lite]/bp.sbc + thumb.png  (called by tools/build_all.py or --write)"""
    bp = MODELS[name](lite)
    label = bp.name
    folder = os.path.join(root, label)
    bp.write(folder, label)
    bp.thumb(os.path.join(folder, 'thumb.png'), label, f'{len(bp.bays)} bays, {sum(len(b["welders"]) for b in bp.bays)} welders', PAL_PRINTER)
    return folder

def main(argv):
    if argv[:1] == ['--write']:
        root = argv[1]
        for name in (argv[2:] or list(MODELS)):
            for lite in (False, True):
                print('wrote', write(name, lite, root))
        return 0
    names = argv or list(MODELS)
    bad = 0
    for name in names:
        for lite in (False, True):
            r = report(name, lite)
            print(r[1])
            if not r[0]:
                bad += 1
                for l in (r[2] if len(r) > 2 else []): print(l)
    return 1 if bad else 0

if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
