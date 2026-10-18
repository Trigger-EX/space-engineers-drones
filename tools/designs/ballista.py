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
  * aux     : two more large reactors on the top deck and two extra hydrogen tanks (belly under the breech, aft behind the core) with thrusters, for the 27-rail recharge
              (docs/design/ships/Ballista.md sections 9 and 10)
  * mast    : armour pylon on top of the wall carrying the cameras
  * turrets : four vanilla gatling turrets set for drones and rockets only (two on the reactor tops, two under the outer rails), nothing in front of any of them
Every large hydrogen thruster keeps its whole 3 x 3 nozzle face clear for sebp.LARGE_H2_LANE (7) cells: the old 1-lane, 3-cell check let two belly thrusters burn the pods.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import sebp_toolbar as TB
from sebp_inv import Item
from pods import orient_attached
import pods
import sebp

NAME = 'Ballista'
TAG = 'BALLISTA'
# optional point-defence rails: [B2] rails the Helm fires one at a time at small / kamikaze tracks (needs [Battery] Point Defence=B2)
PD_RAILS = set()      # {(x, r) for x in (-1, 0, 1) for r in (0, 2)} would tag six rails [B2]; with Point Defence=B2 they shoot small tracks alone.  Off: sim A/B (docs 3e) showed no gain over the turrets + full salvo
# point defence (2026-10-16 redesign, owner: "the turrets are blocked from seeing the front ... use vanilla turrets set specifically for drones and rockets"): four vanilla
# LargeGatlingTurret (3x3x3; the only mount is the 1x1 centre of the base).  min corner, base direction, place.  Top pair: base on the top centre patch of the two top-deck
# reactors (y 9..11, the highest blocks forward of the mast: nothing ahead, beside or behind them at their height).  Belly pair: base under the outer rails (x +-3), whose bottom
# patch is z 4.9..6.5 (so z 4..6), ahead of every belly block.  Each pair sees its own hemisphere and the horizon all round; the gatling's -43 deg elevation lets each see past the bow.
# (The old sites at z 7..9 on the breech wall had the reactors / a belly thruster in front of them at the same height, and two of them were PD4L rotor turrets with their own PB.)
PD_TURRETS = (((-4, 9, 4), 'Up', 'Top P'), ((2, 9, 4), 'Up', 'Top S'), ((-4, -3, 4), 'Down', 'Belly P'), ((2, -3, 4), 'Down', 'Belly S'))
# targeting written into the blueprint: small grids and missiles (rockets) only; large grids, stations, characters and meteors off (the railguns and the Helm take large grids).
# The game writes only the non-default switches in <Flags> (the owner's Tech Hunter: <Flags><TargetMeteors>false</TargetMeteors><TargetCharacters>false</TargetCharacters></Flags>);
# TargetLargeGrids / TargetStations are the legacy object-builder names, kept in that field order [unverified in game: check the terminal after pasting, packages/tests.md]
PD_FLAGS = ('<Flags><TargetMeteors>false</TargetMeteors><TargetLargeGrids>false</TargetLargeGrids><TargetCharacters>false</TargetCharacters>'
            '<TargetStations>false</TargetStations></Flags>')
PD_SETTINGS = {'TargetCharacters': 'false', 'Flags': PD_FLAGS}
H2_LANE = sebp.LARGE_H2_LANE      # exhaust lane depth (cells) of a large hydrogen thruster, over its whole nozzle face
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
                bp.keep_clear(b, H2_LANE, 'exhaust', face=True)

def _aux(S):
    """power and thrust added for the 27-gun recharge (docs/design/ships/Ballista.md sections 9 and 10): two more large reactors on the top deck (300 MW each: the railguns
    ask 27 x 38 = 1,026 MW to recharge together), and two extra hydrogen tanks with six thrusters, one more per direction, which give back the acceleration the reactors' 148 t take away.
    Section 10 (2026-10-16) moved the tanks: the old belly tank (z 4..6) fired its side thrusters straight into the pods' lower vertical thrusters (z 5..7, the very next cells), and the old
    top tank stood in front of the top turrets.  Now:
      belly tank under the breech wall and the core reactor (x -1..1, y -3..-1, z 8..10): brake (front), main (back), left, right, down.  Its side flames run at z 8..10, behind the
      pods' lower verticals (z 5..7) and under the pods' mains (y 0..2); the brake's flame runs forward along the keel between the belly turrets (x +-2..4).
      aft tank on the back of the core reactor (x -1..1, y 0..2, z 12..14, where the foundry connectors were): one upward-exhaust thruster (pushes down) on its top."""
    bp = S.bp
    for n, x in enumerate((-4, 2), 2):
        S.add('reactor', 'MyObjectBuilder_Reactor', 'LargeBlockLargeGenerator', (x, 6, 4), 'Forward', 'Up', nm(f'Reactor Large {n}'), toward='Down', kind='reactor',
              items=[Item.ingot('Uranium', 300)])
    S.add('tank', 'MyObjectBuilder_OxygenTank', 'LargeHydrogenTank', (-1, -3, 8), 'Forward', 'Up', nm('Hydrogen Tank Aux Belly'), toward='Up', kind='tank', state={'filled': 'full'})
    S.add('tank', 'MyObjectBuilder_OxygenTank', 'LargeHydrogenTank', (-1, 0, 12), 'Forward', 'Up', nm('Hydrogen Tank Aux Aft'), toward='Forward', kind='tank', state={'filled': 'full'})
    for role, mn, ex, up, label in (
            ('brake', (-1, -3, 5), 'Forward', 'Up', 'Aux Belly Brake'), ('main', (-1, -3, 11), 'Backward', 'Up', 'Aux Belly Main'),
            ('out', (-4, -3, 8), 'Left', 'Up', 'Aux Belly Lateral P'), ('out', (2, -3, 8), 'Right', 'Up', 'Aux Belly Lateral S'),
            ('vert', (-1, -6, 8), 'Down', 'Forward', 'Aux Belly Vertical'), ('vert', (-1, 3, 12), 'Up', 'Forward', 'Aux Aft Vertical')):
        b = S.fixed('thr_' + role, 'MyObjectBuilder_Thrust', 'LargeBlockLargeHydrogenThrust', mn, ex, up, nm(f'Thruster {label}'), GUND, kind='thr')
        bp.keep_clear(b, H2_LANE, 'exhaust', face=True)

def _core(S, full):
    """behind the breech wall: reactor, batteries, gyros, controllers"""
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
    # the Spotter keeps its Lookout (harasser defence); the probe foundry it used to run is gone (2026-10-16: the AI Offensive block's 2.5 km search is detection enough), so Bay Control=0
    S.spotter = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (0, 4, 11), 'Forward', 'Up', '[Spotter][SH] Ballista Spotter', toward='Down', kind='ctrl',
                      custom_data=f'[Ship]\nTag={TAG}\n\n[Spotter]\nBay Control=0\n', extra=pb_extra(_script('SpotterAI.cs')) if _script('SpotterAI.cs') else '')
    # the Staff half of the Helm (docs/design/helm_split.md): fleet channel relay and damage sensing run here, so ShrikeAI stays under its size gate.  Its name must start with [Staff] (the Helm
    # switches its link on for that) and carry the [SH] prefix (the Staff only adopts blocks with the Block Prefix).  Mothership=false: the Ballista carries no drones, so no <Type>_HOME / FLEET_HOME.
    S.spare = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (1, 4, 11), 'Forward', 'Up', '[Staff][SH] Ballista Staff', toward='Down', kind='ctrl',
                    custom_data=f'[Ship]\nTag={TAG}\n\n[Staff]\nBlock Prefix=[SH]\nMothership=false\n', extra=pb_extra(_script('StaffAI.cs')) if _script('StaffAI.cs') else '')
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
    """four vanilla gatling turrets, loaded, set for small grids and missiles only (PD_TURRETS, PD_SETTINGS): autonomous point defence inside 800 m and designators for the Helm and the Lookout"""
    for n, (mn, up, where) in enumerate(PD_TURRETS, 1):
        b = S.fixed('pdturret', 'MyObjectBuilder_LargeGatlingTurret', '', mn, 'Forward', up, nm(f'[PD] Gatling Turret {n} {where}'), GUNC, kind='turret', settings=dict(PD_SETTINGS))
        S.bp.fill_ammo(b)

def _groups(S):
    bp, R = S.bp, S.r
    bp.group('B1', [b for b in R['rail'] if b not in S.pd_rails])
    bp.group('B2', S.pd_rails)
    bp.group('Weapons', R['rail'] + R.get('pdturret', []))
    bp.group('Thrusters', [b for k in R if k.startswith('thr_') for b in R[k]])
    bp.group('Gyros', R['gyro']); bp.group('Reactors', R['reactor']); bp.group('Batteries', R['bat'])

def ballista(lite=False):
    full = not lite
    name = 'Ballista Lite' if lite else 'Ballista'
    bp = Blueprint(name, 'Large', seed=seed_for(name))
    S = Ship(bp)
    _rails(S)
    _pods(S)
    _core(S, full)
    _aux(S)                       # after the core: the aft tank hangs on the core reactor's back face
    _sensing(S, full)
    _defence(S, full)
    _groups(S)
    bp.ship = S
    pods.assert_clean(bp)
    return C.finalize(bp)

MODELS = {'Ballista': ballista}
DOCS = {'Ballista': 'Large-grid railgun drone: 27 Large Railguns in a fixed 9 x 3 battery behind a thin frame of 22 large hydrogen thrusters (33 m/s2 forward and back, 20 on the other axes, 13 gyros), 1.35 M of slug damage per salvo at 2,000 m. '
        'The Helm keeps the stand-off and turns the ship onto the target; the AI Offensive block (2.5 km) is its detector. Four vanilla gatling turrets set for drones and rockets only (two on top, two under the bow) '
        'guard it, each with a clear view forward. Three large reactors (900 MW) and 13 batteries cover the 1,026 MW the 27 rails draw to recharge together. No thruster flame touches another block. Nothing has run in the game.'}
MODEL_NOTES = {'Ballista': 'About 4,700 PCU. The turrets are written with large grids, stations, characters and meteors off (small grids and missiles on): check the terminal after pasting. Lite has no sensor / AI blocks: use Full.'}
SCRIPTS = ['ShrikeAI.cs', 'SpotterAI.cs', 'StaffAI.cs']

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
    n['pcu'] = sum(stat(b).get('pcu') or 0 for b in blocks)
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
