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
  * core    : behind the wall (x -3..3, y 0..5, z 9..11): large reactor, batteries, gyros, the Helm PB and the Remote Control
  * aux     : two more large reactors on the top deck and two extra hydrogen tanks (top, belly) with thrusters, for the 27-rail recharge (docs/design/ships/Ballista.md section 9)
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
import turrets as TU
import printer_lib as PL
from types import SimpleNamespace

NAME = 'Ballista'
TAG = 'BALLISTA'
# optional point-defence rails: [B2] rails the Helm fires one at a time at small / kamikaze tracks (needs [Battery] Point Defence=B2)
PD_RAILS = set()      # {(x, r) for x in (-1, 0, 1) for r in (0, 2)} would tag six rails [B2]; with Point Defence=B2 they shoot small tracks alone.  Off: sim A/B (docs 3e) showed no gain over the turrets + full salvo
# vanilla gatling turrets (3x3x3, base on the breech wall's armour cubes beside the mast): min corner, up, place
PD_TURRETS = (((-4, 6, 7), 'Up', 'Top P'), ((2, 6, 7), 'Up', 'Top S'), ((-4, -3, 7), 'Down', 'Belly P'), ((2, -3, 7), 'Down', 'Belly S'))
# two of the four sites are rotor + hinge AI turrets (TurretAI.cs, docs/design/turrets.md PD4L: 4 small gatlings, own lead solver, fast slew) on opposite corners (top port, belly starboard): they see both hemispheres,
# (PD4L 568 PCU, 4 x 1,050 dps; a vanilla LargeGatlingTurret 225 PCU, 792 dps).  The 10,000 PCU brief pays for the two and for the turret PB only if one vanilla site is dropped (Belly P);
# the one vanilla turret left (Top S) stays a designator for the Helm and a fallback if the turret script dies
CUSTOM_PD = ('Top P', 'Belly S')
DROP_PD = ('Belly P',)
COLS = tuple(range(-4, 5))        # 9 columns of railguns
ROWS = 3                          # 3 rows (2 cells each) -> 27 guns
N_GYRO = 13
# battery cells (x, z) of the y = 3 deck left empty to pay for the added reactors' mass: none of them carries a block above it
BAT_DROP = {(-2, 9), (-1, 9), (1, 9), (-1, 10), (1, 10), (-1, 11), (-2, 11), (2, 11)}       # 13 of 21 kept (156 MW, 39 MWh)

HELM_DATA = """
[Link]
Block Prefix=[SH]

[Shrike]
IGC Tag=BALLISTA
AI Block Search Assist=true
Start Mode (GUARD/HUNT/ASSIST/IDLE)=HUNT
Standoff Range=1600
Min Range=900
Evasive Jinking=true
Orbit Speed=30
Combat Speed=100
Auto Camera Scan=false
Wingmate Separation Range (m)=300
Wingmate Separation Speed (m/s)=12
Wingmate Fire Clearance (m)=45

[AI Gate]
AI Window (s)=20
Script Search Window (s)=5

[Battery]
Primary (AUTO/B1..B4)=B1
Standoff By Weapons=false
Salvo (AUTO/ON/OFF)=ON
Kamikaze Rail Hold Range (m)=250
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
        self.pd_rails = []

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
            pd = (x, row) in PD_RAILS
            b = S.fixed('rail', 'MyObjectBuilder_SmallMissileLauncherReload', 'LargeRailgun', (x, 2 * row, 0), 'Forward', 'Up',
                        nm(f'[B2] PD Railgun {n:02d}' if pd else f'[B1] Railgun {n:02d}'), GUNC, kind='rail')
            if pd: S.pd_rails.append(b)
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

def _aux(S):
    """power and thrust added for the 27-gun recharge (docs/design/ships/Ballista.md section 9): two more large reactors on the top deck (300 MW each: the railguns
    ask 27 x 38 = 1,026 MW to recharge together and the first reactor set gave 360 MW), and two extra hydrogen tanks with thrusters (6 in all), which give back the acceleration the
    reactors' 148 t take away.  Top deck (y 6..8, over the rails; the rails' top patch is the seat): reactors at x -4..-2 and 2..4, z 4..6; a tank in front of the
    port reactor (z 1..3) carries an upward-exhaust thruster (pushes down) and a brake.  Belly (y -3..-1, under the rails): one tank at x -1..1, z 4..6 carries
    four of its free faces (down, left, right, main; the brake lane is left out: the back push already has 5 thrusters, and the 10,000 PCU brief is full), which add a push in every direction together with the top tank (up, down, left, right, forward, back)."""
    bp = S.bp
    for n, x in enumerate((-4, 2), 2):
        S.add('reactor', 'MyObjectBuilder_Reactor', 'LargeBlockLargeGenerator', (x, 6, 4), 'Forward', 'Up', nm(f'Reactor Large {n}'), toward='Down', kind='reactor',
              items=[Item.ingot('Uranium', 300)])
    S.add('tank', 'MyObjectBuilder_OxygenTank', 'LargeHydrogenTank', (-4, 6, 1), 'Forward', 'Up', nm('Hydrogen Tank Aux Top'), toward='Backward', kind='tank', state={'filled': 'full'})
    S.add('tank', 'MyObjectBuilder_OxygenTank', 'LargeHydrogenTank', (-1, -3, 4), 'Forward', 'Up', nm('Hydrogen Tank Aux Belly'), toward='Up', kind='tank', state={'filled': 'full'})
    for role, mn, ex, up, label in (
            ('vert', (-4, 9, 1), 'Up', 'Forward', 'Aux Top Vertical'), ('brake', (-4, 6, -2), 'Forward', 'Up', 'Aux Top Brake'),
            ('vert', (-1, -6, 4), 'Down', 'Forward', 'Aux Belly Vertical'), ('out', (-4, -3, 4), 'Left', 'Up', 'Aux Belly Lateral P'),
            ('out', (2, -3, 4), 'Right', 'Up', 'Aux Belly Lateral S'),
            ('main', (-1, -3, 7), 'Backward', 'Up', 'Aux Belly Main')):
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
            if (x, z) in BAT_DROP: continue
            S.add('bat', 'MyObjectBuilder_BatteryBlock', 'LargeBlockBatteryBlock', (x, 3, z), 'Forward', 'Up', nm(f'Battery {n:02d}'), extra=bat_extra(3), kind='bat')
    gy = [(x, y, 9) for x in (-3, -2, 2, 3) for y in (0, 1, 2)]
    gy += [(x, y, 9) for y in (0, 1, 2, 3) for x in (-4, 4)]           # a gyro seats only by its bottom face: the extra ones sit on the breech wall's back face beside the core
    for i, c in enumerate(gy[:N_GYRO]):
        S.add('gyro', 'MyObjectBuilder_Gyro', 'LargeBlockGyro', c, 'Forward', 'Up', nm(f'Gyroscope {i + 1}'), kind='gyro')
    # controllers on the battery deck
    S.rc = S.add('rc', 'MyObjectBuilder_RemoteControl', 'LargeBlockRemoteControl', (0, 4, 10), 'Forward', 'Up', nm('Remote Control'), toward='Down', kind='ctrl')
    S.helm = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (0, 4, 9), 'Forward', 'Up', '[Helm][SH] Ballista Helm', toward='Down', kind='ctrl',
                   custom_data=f'[Ship]\nTag={TAG}\n' + HELM_DATA, extra=pb_extra(_script('ShrikeAI.cs')) if _script('ShrikeAI.cs') else '')
    S.spotter = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (0, 4, 11), 'Forward', 'Up', '[Spotter][SH] Ballista Spotter', toward='Down', kind='ctrl',
                      custom_data=f'[Ship]\nTag={TAG}\n', extra=pb_extra(_script('SpotterAI.cs')) if _script('SpotterAI.cs') else '')
    # the Staff half of the Helm (docs/design/helm_split.md): fleet channel relay and damage sensing run here, so ShrikeAI stays under its size gate.  Its name must start with [Staff] (the Helm
    # switches its link on for that) and carry the [SH] prefix (the Staff only adopts blocks with the Block Prefix).  Mothership=false: the Ballista carries no drones, so no <Type>_HOME / FLEET_HOME.
    S.spare = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (1, 4, 11), 'Forward', 'Up', '[Staff][SH] Ballista Staff', toward='Down', kind='ctrl',
                    custom_data=f'[Ship]\nTag={TAG}\n\n[Staff]\nBlock Prefix=[SH]\nMothership=false\n', extra=pb_extra(_script('StaffAI.cs')) if _script('StaffAI.cs') else '')
    # the turret PB (TurretAI.cs; Custom Data with the masks is filled in by ballista() once the hull is final): first free battery-deck cell where a large PB seats (Bottom mount only, on a battery)
    S.tpb = None
    for c in ((-1, 4, 11), (-1, 4, 10), (-1, 4, 9), (1, 4, 9), (1, 4, 10), (-2, 4, 9), (2, 4, 9), (-3, 4, 10), (3, 4, 10), (-2, 4, 11), (2, 4, 11), (-3, 4, 9), (3, 4, 9), (-3, 4, 11), (3, 4, 11)):
        if c in S.occ or c in bp.reserve: continue
        try:
            S.tpb = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', c, 'Forward', 'Up', f'{TU.PREFIX} Turret PB', toward='Down', kind='ctrl',
                          custom_data=f'[Ship]\nTag={TAG}\n', extra=pb_extra(_script('TurretAI.cs')) if _script('TurretAI.cs') else '')
            break
        except AssertionError:
            continue
    assert S.tpb is not None, 'no cell for the turret PB'
    # the two large connectors on the rear face of the battery deck: the probe foundry docks here
    S.conns = [S.add('conn', 'MyObjectBuilder_ShipConnector', 'Connector', (x, 3, 12), 'Backward', 'Up', nm(f'Foundry Connector {"AB"[i]}'), toward='Forward', kind='conn')
               for i, x in enumerate((0, 1))]
    if full:
        S.add('ai', 'MyObjectBuilder_OffensiveCombatBlock', 'LargeOffensiveCombat', (-2, 4, 10), 'Forward', 'Up', nm('AI Offensive [AI]'), toward='Down', kind='ai')
        S.add('ai', 'MyObjectBuilder_FlightMovementBlock', 'LargeFlightMovement', (2, 4, 10), 'Forward', 'Up', nm('AI Flight [AI]'), toward='Down', kind='ai')

def _sensing(S, full):
    bp = S.bp
    for y in (6, 7, 8, 9): S.armor(0, y, 8, role='mast')          # (the top turrets stand beside the mast: the side cameras sit above their roofs)
    specs = [('Front', (0, 6, 7), 'Forward', 'Up'), ('Rear', (0, 6, 9), 'Backward', 'Up'), ('Port', (-1, 9, 8), 'Left', 'Up'),
             ('Starboard', (1, 9, 8), 'Right', 'Up'), ('Top', (0, 10, 8), 'Up', 'Backward')]
    for tag, mn, look, up in specs:
        S.put('cam', C.cam(bp, mn, look, up, nm(f'Camera {tag}'), 3))
    # belly: the aux tank's main thruster took (0, -1..-2, 8), the old camera / antenna lane; they hang from the outer batteries at z = 11 on armour columns, clear of its exhaust (x -1..1)
    for y in (2, 1, 0): S.armor(3, y, 11, role='mast')
    S.put('cam', C.cam(bp, (3, -1, 11), 'Down', 'Forward', nm('Camera Belly'), 3))
    for y in (2, 1, 0): S.armor(-3, y, 11, role='mast')
    S.add('ant', 'MyObjectBuilder_RadioAntenna', 'LargeBlockCompactRadioAntenna', (-3, -3, 11), 'Forward', 'Down', nm('Antenna'), extra=ant_extra(10000), kind='ant')
    if full:
        S.add('sensor', 'MyObjectBuilder_SensorBlock', 'LargeBlockSensor', (-1, 7, 8), 'Up', 'Forward', nm('Sensor'), color=GUND, kind='sensor')

def _defence(S, full):
    """four vanilla gatling turrets (autonomous point defence against whatever gets inside 800 m; they are also designators for the Helm)"""
    S.tmounts = []
    for n, (mn, up, where) in enumerate(PD_TURRETS, 1):
        if where in DROP_PD: continue
        if where in CUSTOM_PD:
            # the vanilla turret's 3x3x3 footprint holds one PD4L: stator on the base-centre cell (the lower cell of its 2-cell box), swing sphere r 1.9 m
            base_y = mn[1] if up == 'Up' else mn[1] + 2
            m = TU.add_turret(S.bp, len(S.tmounts) + 1, (mn[0] + 1, base_y, mn[2] + 1), up, fwd='Forward', model='PD4L', lite=not full, color=GUNC)
            S.put('tu', m.stator)
            for x in range(mn[0], mn[0] + 3):
                for y in range(mn[1], mn[1] + 3):
                    for z in range(mn[2], mn[2] + 3): S.occ.setdefault((x, y, z), m.stator)
            S.tmounts.append(m)
            continue
        b = S.fixed('pdturret', 'MyObjectBuilder_LargeGatlingTurret', '', mn, 'Forward', up, nm(f'[PD] Gatling Turret {n} {where}'), GUNC, kind='turret')
        S.bp.fill_ammo(b)

def _groups(S):
    bp, R = S.bp, S.r
    bp.group('B1', [b for b in R['rail'] if b not in S.pd_rails])
    bp.group('B2', S.pd_rails)
    bp.group('Weapons', R['rail'] + R.get('pdturret', []))
    bp.group('Thrusters', [b for k in R if k.startswith('thr_') for b in R[k]])
    bp.group('Gyros', R['gyro']); bp.group('Reactors', R['reactor']); bp.group('Batteries', R['bat'])

# ---------------------------------------------------------------------------------------------
# probe foundry: a small-grid printer (printers.py 'Probe Foundry', one bay printing Peepers) docked on the two rear connectors
# ---------------------------------------------------------------------------------------------
EXIT_WANT = 'Up'                  # host direction the printed probe leaves along (the foundry bay's exit is its local Right)

def foundry(lite, row='x'):
    """the printer with its two ConnectorMedium blocks on a flange under the equipment pod, in a row along the foundry's x or z axis (ballista() tries both
    and keeps the one whose bay exit ends up pointing away from the ship)"""
    fb = PR.MODELS['Probe Foundry'](lite)
    occ = fb.occupancy()
    # a flange under the equipment pod carries two ConnectorMedium blocks 5 cells (2.5 m = one large cell) apart, contact face Down
    pos = (-4, 1)                      # block corner along the row; the pod spans -2..2, so the flange fills -4..-3 and 3
    for u in (-4, -3, 3):
        for v in (-1, 0, 1):
            c = (u, -2, v) if row == 'x' else (v, -2, u)
            assert c not in occ, f'foundry flange cell {c} is taken'
            fb.armor(*c, PL.PRINTER_COLOR)
    conns = [fb.add('MyObjectBuilder_ShipConnector', 'ConnectorMedium', (u, -4, -1) if row == 'x' else (-1, -4, u), 'Down', 'Forward',
                    f'Foundry Connector {"AB"[i]} ({fb.name})', GUND, kind='conn') for i, u in enumerate(pos)]
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

def dock_foundry(host, fb, conns, want=EXIT_WANT, lock=True):
    """dock with the roll that sends the bay's exit (the foundry's exit direction) along the host direction `want`"""
    errs = []
    for order in (list(conns), list(conns)[::-1]):
        for roll in range(4):
            try:
                pose = pods.dock_pod(host, fb, order, host_face='Forward', roll=roll)
            except AssertionError as e:
                errs.append(f'roll {roll}: {e}'); continue
            d = pose.rot.apply(MU.DIRS[exit_of(fb)])
            if MU._dot(d, MU.DIRS[want]) > 0.99 or MU._dot(d, MU.DIRS['Backward']) > 0.99:
                if lock:      # written locked, as the Warden / Leviathan pods are (an unlocked pod broke off the hull in the game)
                    host.lock_connectors(order[0], fb, fb.pod.connectors[0])
                    host.lock_connectors(order[1], fb, fb.pod.connectors[1])
                return pose
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
    _aux(S)
    _core(S, full)
    _sensing(S, full)
    _defence(S, full)
    _groups(S)
    S.tu_check = TU.check_mounts(bp, S.tmounts, min_cover=0.4)       # swing envelopes clear the hull, parked pose free, each turret sees 40 % of its bins
    S.tpb['custom_data'] = TU.turret_custom_data(bp, S.tmounts, TAG)
    bp.ship = S
    if with_foundry:
        errs = []
        for row in ('x', 'z'):
            fb = foundry(lite, row)
            try:
                dock_foundry(bp, fb, S.conns)
                break
            except AssertionError as e:
                errs.append(f'row {row}: {e}')
        else:
            raise AssertionError('no foundry docking: ' + ' | '.join(errs))
        bp.foundry = fb
        check_foundry(bp)
    pods.assert_clean(bp)
    return C.finalize(bp)

MODELS = {'Ballista': ballista}
DOCS = {'Ballista': 'Large-grid railgun drone: 27 Large Railguns in a fixed 9 x 3 battery behind a thin frame of 22 large hydrogen thrusters (33 m/s2 forward and back, 20 on the other axes, 13 gyros), 1.35 M of slug damage per salvo at 2,000 m. '
        'The Helm keeps the stand-off and turns the ship onto the target. A small probe foundry docked on its rear connectors prints a Peeper probe when a big target is engaged; the probe circles the target and its turret finds the '
        'reactors and batteries (then weapons and propulsion), the Spotter remembers them and the railguns drill the best one. About 8,200 PCU with the foundry, 9,300 with a live probe. Three large reactors (900 MW) and 13 batteries cover the 1,026 MW the 27 rails draw to recharge together. Nothing has run in the game.'}
MODEL_NOTES = {'Ballista': 'Needs a world PCU limit of 10,000 or more. The foundry connectors are written locked (the Spotter presses Connect if they ever come up unlocked). Lite has no sensor / AI blocks and prints Lite probes (no turret, so they find nothing): use Full.'}
SCRIPTS = ['ShrikeAI.cs', 'SpotterAI.cs', 'StaffAI.cs', 'TurretAI.cs', 'HiveMotherAI.cs', 'PeeperAI.cs']

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
    tm = getattr(getattr(bp, 'ship', None), 'tmounts', [])
    n['pcu_ship'] = sum(stat(b).get('pcu') or 0 for b in blocks) + sum(stat(b).get('pcu') or 0 for b in TU.all_blocks(tm))     # the AI turrets' tops and heads are their own grids (their stators are host blocks)
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
