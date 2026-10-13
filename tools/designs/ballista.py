"""Ballista: large-grid railgun drone, a 9 x 3 battery of 27 Large Railguns on a thin frame of hydrogen thrusters, no armour worth the name.

Models: 'Ballista' / 'Ballista Lite' (Lite = no Sensor, no AI Offensive / AI Flight: block shapes never seen in a game-written file).
    python3 -I tools/designs/ballista.py            build + validate both variants, one report line each
    python3 -I tools/designs/ballista.py --numbers  the design figures (PCU, mass, accelerations, power, salvo)
Design record: docs/design/ships/Ballista.md.

Idea: stay out of range (railgun 2,000 m, slug 2,000 m/s) and land a whole battery in one salvo.  The Helm (ShrikeAI.cs, [Battery] Standoff By Weapons) keeps
the standoff of the RAIL battery (~1,700 m) and turns the whole ship onto the target; all 27 guns are fixed forward.

Frame (cells of 2.5 m; x Right, y Up, z Backward, the bow points to -z; mirror plane x = 0):
  * rails   : x -4..4, y 0..5 (three rows of 2), z 0..7, muzzles at z = 0, breech wall (armour) at z = 8
  * pods    : four 3x3x3 hydrogen tanks on the flanks (x +-5..7, y 0..2 and 3..5, z 5..7); every free tank face carries a large hydrogen thruster
              (exhaust out / up / down / forward / aft), so each tank feeds its thrusters by contact and no conveyor network is needed
  * core    : behind the wall (x -3..3, y 0..5, z 9..11): large reactor, batteries, small generators, gyros, the Helm PB and the Remote Control
  * mast    : armour pylon on top of the wall carrying the cameras
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import sebp_toolbar as TB
from sebp_inv import Item
from pods import orient_attached
import pods
import sebp_multi as MU
import printers as PR
import printer_lib as PL
from types import SimpleNamespace

NAME = 'Ballista'
TAG = 'BALLISTA'
COLS = tuple(range(-4, 5))        # 9 columns of railguns
ROWS = 3                          # 3 rows (2 cells each) -> 27 guns
N_GYRO = 12

HELM_DATA = """
[Link]
Block Prefix=[SH]

[Shrike]
IGC Tag=BALLISTA
Aim Point (AUTO/BREACH/ENGINES/FACING/SPREAD/CENTER)=BREACH
AI Block Search Assist=false
Start Mode (GUARD/HUNT/ASSIST/IDLE)=HUNT
Standoff Range=1600
Min Range=900
Evasive Jinking=true
Orbit Speed=30
Combat Speed=100

[Battery]
Primary (AUTO/B1..B4)=B1
Standoff By Weapons=true
"""

GUNC = (0.0, -0.81, -0.45)
GUND = (0.0, -0.81, -0.55)
FRAME = (0.58, -0.78, -0.25)

def nm(s):
    return f'[SH] {s}'

def _script(name):
    return script_text(name) if os.path.exists(os.path.join(ROOT, 'scripts', name)) else ''

class Ship:
    """collects the placed blocks by role (same contract as warden.Ship)"""
    def __init__(self, bp):
        self.bp, self.occ, self.r = bp, bp.occupancy(), {}

    def put(self, role, b):
        for c in self.bp.cells(b): self.occ[c] = b
        self.r.setdefault(role, []).append(b)
        return b

    def add(self, role, xtype, sub, mn, fwd='Forward', up='Up', name=None, color=GUNC, toward=None, **kw):
        b = orient_attached(self.bp, xtype, sub, mn, toward=toward, prefer=((fwd, up),), occ=self.occ, name=name, color=color, **kw)
        return self.put(role, b)

    def fixed(self, role, xtype, sub, mn, fwd, up, name, color=GUNC, **kw):
        b = self.bp.add(xtype, sub, mn, fwd, up, name, color, **kw)
        for c in self.bp.cells(b):
            assert c not in self.occ, f'{self.bp.name}: {name} overlaps {self.occ[c]["name"] or self.occ[c]["sub"]} at {c}'
        return self.put(role, b)

    def armor(self, x, y, z, color=FRAME, role='armor'):
        b = self.bp.armor(x, y, z, color)
        assert (x, y, z) not in self.occ, f'{self.bp.name}: armour overlaps at {(x, y, z)}'
        return self.put(role, b)

# ---------------------------------------------------------------------------------------------
def _rails(S):
    bp = S.bp
    n = 0
    for row in range(ROWS):
        for x in COLS:
            n += 1
            b = S.fixed('rail', 'MyObjectBuilder_SmallMissileLauncherReload', 'LargeRailgun', (x, 2 * row, 0), 'Forward', 'Up',
                        nm(f'[B1] Railgun {n:02d}'), GUNC, kind='rail')
            bp.preload(b, items=[Item.ammo('LargeRailgunAmmo', 6)], state={'capacitor': 'full'})
            bp.keep_clear(b, 3, 'muzzle')
    # breech wall: every Back mount seats on it
    for x in COLS:
        for y in range(2 * ROWS):
            S.armor(x, y, 8)

def _pods(S):
    """four hydrogen tanks on the flanks, each with a large hydrogen thruster on every free face"""
    bp = S.bp
    for sx, sn, ex_out in ((1, 'S', 'Right'), (-1, 'P', 'Left')):
        x0 = 5 if sx > 0 else -7
        for lvl, y0, ex_vert in (('Up', 3, 'Up'), ('Lo', 0, 'Down')):
            tag = f'{sn}{lvl}'
            S.add('tank', 'MyObjectBuilder_OxygenTank', 'LargeHydrogenTank', (x0, y0, 5), 'Forward', 'Up', nm(f'Hydrogen Tank {tag}'),
                  toward='Left' if sx > 0 else 'Right', kind='tank', state={'filled': 'full'})
            specs = [
                ('out', (x0 + 3 if sx > 0 else x0 - 3, y0, 5), ex_out, 'Up', f'Lateral {tag}'),
                ('brake', (x0, y0, 2), 'Forward', 'Up', f'Brake {tag}'),
                ('main', (x0, y0, 8), 'Backward', 'Up', f'Main {tag}'),
                ('vert', (x0, y0 + 3 if ex_vert == 'Up' else y0 - 3, 5), ex_vert, 'Forward', f'Vertical {tag}'),
            ]
            for role, mn, ex, up, label in specs:
                b = S.fixed('thr_' + role, 'MyObjectBuilder_Thrust', 'LargeBlockLargeHydrogenThrust', mn, ex, up, nm(f'Thruster {label}'), GUND, kind='thr')
                bp.keep_clear(b, 3, 'exhaust')

def _core(S, full):
    """behind the breech wall: reactor, batteries, generators, gyros, controllers"""
    bp = S.bp
    S.add('reactor', 'MyObjectBuilder_Reactor', 'LargeBlockLargeGenerator', (-1, 0, 9), 'Forward', 'Up', nm('Reactor Large'), toward='Forward', kind='reactor',
          items=[Item.ingot('Uranium', 300)])
    n = 0
    for z in (9, 10, 11):
        for x in range(-3, 4):
            n += 1
            S.add('bat', 'MyObjectBuilder_BatteryBlock', 'LargeBlockBatteryBlock', (x, 3, z), 'Forward', 'Up', nm(f'Battery {n:02d}'), extra=bat_extra(3), kind='bat')
    for i, (x, z) in enumerate(((-1, 9), (1, 9), (-1, 10), (1, 10))):
        S.add('reactor', 'MyObjectBuilder_Reactor', 'LargeBlockSmallGenerator', (x, 4, z), 'Forward', 'Up', nm(f'Generator {i + 1}'), toward='Down', kind='reactor',
              items=[Item.ingot('Uranium', 100)])
    gy = [(x, y, 9) for x in (-3, -2, 2, 3) for y in (0, 1, 2)]
    gy += [(x, y, 10) for x in (-3, -2, 2, 3) for y in (0, 1, 2)]
    for i, c in enumerate(gy[:N_GYRO]):
        S.add('gyro', 'MyObjectBuilder_Gyro', 'LargeBlockGyro', c, 'Forward', 'Up', nm(f'Gyroscope {i + 1}'), kind='gyro')
    # controllers on the battery deck
    S.rc = S.add('rc', 'MyObjectBuilder_RemoteControl', 'LargeBlockRemoteControl', (0, 4, 10), 'Forward', 'Up', nm('Remote Control'), toward='Down', kind='ctrl')
    S.helm = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (0, 4, 9), 'Forward', 'Up', '[Helm][SH] Ballista Helm', toward='Down', kind='ctrl',
                   custom_data=f'[Ship]\nTag={TAG}\n' + HELM_DATA, extra=pb_extra(_script('ShrikeAI.cs')) if _script('ShrikeAI.cs') else '')
    S.spotter = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (0, 4, 11), 'Forward', 'Up', '[Spotter][SH] Ballista Spotter', toward='Down', kind='ctrl',
                      custom_data=f'[Ship]\nTag={TAG}\n', extra=pb_extra(_script('SpotterAI.cs')) if _script('SpotterAI.cs') else '')
    # the two large connectors on the rear face of the battery deck: the probe foundry docks here
    S.conns = [S.add('conn', 'MyObjectBuilder_ShipConnector', 'Connector', (x, 3, 12), 'Backward', 'Up', nm(f'Foundry Connector {"AB"[i]}'), toward='Forward', kind='conn')
               for i, x in enumerate((0, 1))]
    if full:
        S.add('ai', 'MyObjectBuilder_OffensiveCombatBlock', 'LargeOffensiveCombat', (-2, 4, 10), 'Forward', 'Up', nm('AI Offensive [AI]'), toward='Down', kind='ai')
        S.add('ai', 'MyObjectBuilder_FlightMovementBlock', 'LargeFlightMovement', (2, 4, 10), 'Forward', 'Up', nm('AI Flight [AI]'), toward='Down', kind='ai')

def _sensing(S, full):
    bp = S.bp
    S.armor(0, 6, 8, role='mast'); S.armor(0, 7, 8, role='mast')
    specs = [('Front', (0, 6, 7), 'Forward', 'Up'), ('Rear', (0, 6, 9), 'Backward', 'Up'), ('Port', (-1, 6, 8), 'Left', 'Up'),
             ('Starboard', (1, 6, 8), 'Right', 'Up'), ('Top', (0, 8, 8), 'Up', 'Backward')]
    for tag, mn, look, up in specs:
        S.put('cam', C.cam(bp, mn, look, up, nm(f'Camera {tag}'), 3))
    S.armor(0, -1, 8, role='mast')
    S.put('cam', C.cam(bp, (0, -2, 8), 'Down', 'Forward', nm('Camera Belly'), 3))
    S.add('ant', 'MyObjectBuilder_RadioAntenna', 'LargeBlockCompactRadioAntenna', (1, 7, 8), 'Forward', 'Up', nm('Antenna'), extra=ant_extra(10000), kind='ant')
    if full:
        S.add('sensor', 'MyObjectBuilder_SensorBlock', 'LargeBlockSensor', (-1, 7, 8), 'Up', 'Forward', nm('Sensor'), color=GUND, kind='sensor')

def _groups(S):
    bp, R = S.bp, S.r
    bp.group('B1', R['rail'])
    bp.group('Weapons', R['rail'])
    bp.group('Thrusters', [b for k in R if k.startswith('thr_') for b in R[k]])
    bp.group('Gyros', R['gyro']); bp.group('Reactors', R['reactor']); bp.group('Batteries', R['bat'])

# ---------------------------------------------------------------------------------------------
# probe foundry: a small-grid printer (printers.py 'Probe Foundry', one bay printing Peepers) docked on the two rear connectors
# ---------------------------------------------------------------------------------------------
EXIT_WANT = 'Up'                  # host direction the printed probe leaves along (the foundry bay's exit is its local Right)

def foundry(lite):
    fb = PR.MODELS['Probe Foundry'](lite)
    occ = fb.occupancy()
    # a flange under the equipment pod carries two ConnectorMedium blocks 5 cells (2.5 m = one large cell) apart, contact face Down
    # (the pair is displaced along the foundry's z, so the bay exit - the foundry's exit direction - ends up perpendicular to the connector row)
    for z in (-4, -3, 3):
        for x in (-1, 0, 1):
            assert (x, -2, z) not in occ, f'foundry flange cell {(x, -2, z)} is taken'
            fb.armor(x, -2, z, PL.PRINTER_COLOR)
    conns = [fb.add('MyObjectBuilder_ShipConnector', 'ConnectorMedium', (-1, -4, z), 'Down', 'Forward', f'Foundry Connector {"AB"[i]} ({fb.name})', GUND, kind='conn')
             for i, z in enumerate((-4, 1))]
    fb.pod = SimpleNamespace(connectors=conns)
    # the Spotter starts and stops the foundry (the Hive Mother PB would print a probe the moment it is pasted otherwise)
    for b in fb.blocks:
        if b.get('custom_data') and 'Auto Start=true' in b['custom_data']:
            b['custom_data'] = b['custom_data'].replace('Auto Start=true', 'Auto Start=false')
    fb.display = 'Ballista Probe Foundry'
    return fb

def exit_of(fb):
    """the bay's exit direction in the foundry's own frame: the Hive Mother PB's `Exit Direction=` (the last one in the file wins, as in the script)"""
    d = None
    for b in fb.blocks:
        for line in (b.get('custom_data') or '').split('\n'):
            if line.startswith('Exit Direction=') and line.split('=', 1)[1].strip() in MU.DIRS: d = line.split('=', 1)[1].strip()
    assert d, f'{fb.name}: no Exit Direction in the Hive Mother PB'
    return d

def dock_foundry(host, fb, conns, want=EXIT_WANT):
    """dock with the roll that sends the bay's exit (the foundry's exit direction) along the host direction `want`"""
    errs = []
    for order in (list(conns), list(conns)[::-1]):
        for roll in range(4):
            try:
                pose = pods.dock_pod(host, fb, order, host_face='Forward', roll=roll)
            except AssertionError as e:
                errs.append(f'roll {roll}: {e}'); continue
            d = pose.rot.apply(MU.DIRS[exit_of(fb)])
            if MU._dot(d, MU.DIRS[want]) > 0.99 or MU._dot(d, MU.DIRS['Backward']) > 0.99: return pose
            host.subgrids.pop()
    raise AssertionError('dock_foundry: no roll puts the bay exit along ' + want + '; ' + '; '.join(errs))

def _boxes(g, cs):
    root = getattr(g, '_world', g.pose)
    out = []
    for b in g.blocks:
        for c in g.cells(b):
            ctr = root.apply(tuple(v * cs for v in c))
            out.append((tuple(ctr[i] - cs / 2 for i in range(3)), tuple(ctr[i] + cs / 2 for i in range(3)), b))
    return out

def check_foundry(bp, min_gap=0.3, lane_m=8.0):
    """the docked foundry keeps min_gap metres from every ship block (the connector pairs excepted), is outside the ship's exhaust and muzzle zones, and the
    printed probe can leave: the bay's exit lane (its drone box swept lane_m metres along the exit direction) holds no ship block"""
    fb = bp.foundry
    bp.grids()
    ship = _boxes(bp, 2.5)
    fbx = _boxes(fb, 0.5)
    bad = []
    for lo, hi, b in fbx:
        for slo, shi, sb in ship:
            gap = max(max(slo[i] - hi[i], lo[i] - shi[i], 0.0) for i in range(3))
            if gap < (pods.PAIR_GAP - 0.005 if b['kind'] == 'conn' and sb['kind'] == 'conn' else min_gap) and not (b['kind'] == 'conn' and sb['kind'] == 'conn'):
                bad.append((b['name'] or b['sub'], sb['name'] or sb['sub'], round(gap, 3)))
    assert not bad, f'{bp.name}: foundry too close to the ship: {bad[:4]}'
    zones = []
    for c, why in bp.reserve.items():
        if why.startswith(('exhaust', 'muzzle')): zones.append((tuple((c[i] - 0.5) * 2.5 for i in range(3)), tuple((c[i] + 0.5) * 2.5 for i in range(3)), why))
    hit = [(why, b['name'] or b['sub']) for lo, hi, b in fbx for zlo, zhi, why in zones if all(zlo[i] < hi[i] - 1e-6 and zhi[i] > lo[i] + 1e-6 for i in range(3))]
    assert not hit, f'{bp.name}: foundry inside an exhaust/muzzle zone: {hit[:3]}'
    bay = fb.bays[0]
    d = fb.pose.rot.apply(MU.DIRS[exit_of(fb)])
    dcells = [c for b in bay['drone'].blocks for c in bay['drone'].cells(b)]
    root = fb._world
    lane = []
    for c in dcells:
        ctr = root.apply(tuple((c[k] + bay['T'][k]) * 0.5 for k in range(3)))
        lane.append((ctr, d))
    blocked = []
    for ctr, dd in lane:
        lo = tuple(min(ctr[i], ctr[i] + dd[i] * lane_m) - 0.25 for i in range(3)); hi = tuple(max(ctr[i], ctr[i] + dd[i] * lane_m) + 0.25 for i in range(3))
        for slo, shi, sb in ship:
            if all(slo[i] < hi[i] - 1e-6 and shi[i] > lo[i] + 1e-6 for i in range(3)): blocked.append(sb['name'] or sb['sub'])
    assert not blocked, f'{bp.name}: the probe exit lane is blocked by {sorted(set(blocked))[:3]}'
    return True

def ballista(lite=False, with_foundry=True):
    full = not lite
    name = 'Ballista Lite' if lite else 'Ballista'
    bp = Blueprint(name, 'Large', seed=seed_for(name))
    S = Ship(bp)
    _rails(S)
    _pods(S)
    _core(S, full)
    _sensing(S, full)
    _groups(S)
    bp.ship = S
    if with_foundry:
        fb = foundry(lite)
        dock_foundry(bp, fb, S.conns)
        bp.foundry = fb
        check_foundry(bp)
    pods.assert_clean(bp)
    return C.finalize(bp)

MODELS = {'Ballista': ballista}
DOCS = {'Ballista': 'Large-grid railgun drone: 27 Large Railguns in a fixed 9 x 3 battery behind a thin frame of 16 large hydrogen thrusters (32 m/s2 fore and aft, 16 m/s2 on the other axes, 12 gyros), 1.35 M of slug damage per salvo at 2,000 m. '
        'The Helm keeps the stand-off and turns the ship onto the target. A small probe foundry docked on its rear connectors prints a Peeper probe when a big target is engaged; the probe circles the target and its turret finds the '
        'reactors and batteries (then weapons and propulsion), the Spotter remembers them and the railguns drill the best one. About 7,100 PCU with the foundry, 8,000 with a live probe. Nothing has run in the game.'}
MODEL_NOTES = {'Ballista': 'Needs a world PCU limit of 10,000 or more. Paste it, wait until the two foundry connectors are locked (the Spotter presses Connect), then run the Helm. Lite has no sensor / AI blocks and prints Lite probes (no turret, so they find nothing): use Full.'}
SCRIPTS = ['ShrikeAI.cs', 'SpotterAI.cs', 'HiveMotherAI.cs', 'PeeperAI.cs']

# ---------------------------------------------------------------------------------------------
def numbers(bp):
    """design figures: dict"""
    blocks = bp.blocks
    stat = C._stat
    items_kg = sum(it.kg() for b in blocks for it in bp.items_of(b))
    hull_kg = sum(C.block_mass(b) for b in blocks)
    loaded = hull_kg + items_kg
    n = dict(blocks=len(blocks), dims_m=bp.stats()['dims'], mass_t_blocks=round(hull_kg / 1000, 1), mass_t_loaded=round(loaded / 1000, 1))
    n['accel_loaded_m_s2'] = {d: round(f / loaded, 1) for d, f in C.thrust_by_dir(bp).items()}
    n['thrust_MN'] = {d: round(f / 1e6, 1) for d, f in C.thrust_by_dir(bp).items()}
    n['pcu_ship'] = sum(stat(b).get('pcu') or 0 for b in blocks)
    fb = getattr(bp, 'foundry', None)
    n['pcu_foundry'] = sum(stat(b).get('pcu') or 0 for b in fb.blocks) if fb else 0
    n['pcu'] = n['pcu_ship'] + n['pcu_foundry']
    n['mass_t_foundry'] = round(sum(C.block_mass(b) for b in fb.blocks) / 1000, 1) if fb else 0
    n['mass_t_with_foundry_and_probe'] = round(loaded / 1000 + n['mass_t_foundry'] + (C.total_mass(fb.bays[0]['drone']) / 1000 if fb else 0), 1)
    rails = [b for b in blocks if b['sub'] == 'LargeRailgun']
    gens = [b for b in blocks if b['xtype'].endswith('_Reactor')]
    bats = [b for b in blocks if b['xtype'].endswith('_BatteryBlock')]
    gen_mw = sum(stat(b)['stats']['max_output_mw'] for b in gens)
    bat_mw = sum(stat(b)['stats']['max_output_mw'] for b in bats)
    bat_mwh = sum(stat(b)['stats']['max_stored_mwh'] for b in bats)
    n['rails'] = len(rails)
    n['salvo_pool_damage'] = 50000 * len(rails)
    n['power'] = dict(generation_MW=gen_mw, battery_MW=bat_mw, battery_MWh=bat_mwh, railgun_rearm_MW=38 * len(rails),
                      rearm_s_supply_bound=round(len(rails) * 0.5 / 0.8 / (gen_mw + bat_mw) * 3600), rearm_s_per_gun=59)
    gyros = [b for b in blocks if b['sub'] == 'LargeBlockGyro']
    n['gyro_torque_MNm'] = round(33.6 * len(gyros), 1)
    # moment of inertia about the centre of mass (point masses at block centres, 2.5 m cells)
    cs = 2.5
    pts = []
    for b in blocks:
        m = C.block_mass(b) + sum(it.kg() for it in bp.items_of(b))
        cells = bp.cells(b)
        for c in cells: pts.append((m / len(cells), (c[0] + .5) * cs, (c[1] + .5) * cs, (c[2] + .5) * cs))
    M = sum(p[0] for p in pts)
    com = [sum(p[0] * p[i] for p in pts) / M for i in (1, 2, 3)]
    I = [sum(p[0] * ((p[2] - com[1]) ** 2 + (p[3] - com[2]) ** 2) for p in pts),
         sum(p[0] * ((p[1] - com[0]) ** 2 + (p[3] - com[2]) ** 2) for p in pts),
         sum(p[0] * ((p[1] - com[0]) ** 2 + (p[2] - com[1]) ** 2) for p in pts)]
    n['inertia_kgm2'] = [round(i) for i in I]
    n['turn_rad_s2_pitch_yaw_roll'] = [round(33.6e6 * len(gyros) / i, 2) for i in I]
    return n

if __name__ == '__main__':
    if '--numbers' in sys.argv:
        import json
        print(json.dumps(numbers(ballista(False)), indent=1))
        sys.exit(0)
    sys.exit(C.run_module(MODELS))
