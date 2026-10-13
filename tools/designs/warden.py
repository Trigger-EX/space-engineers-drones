"""Warden: large-grid frigate (about 85 m), heavy gun deck, bridge tower, belly hangar with a docked 4-slot cradle pod.

Models: 'Warden' / 'Warden Lite' = the ship plus ONE docked, empty 'Cradle Pod' (designs/pods.py) as a second CubeGrid.  The pod alone is 'Cradle Pod'.
Design record, layout map, numbers and the in-game checks: docs/design/ships/Warden.md.   Run `python3 -I tools/designs/warden.py [--render DIR]`.

Shape language: arrowhead bow with a flat foredeck (twin railguns in channels either side of a central ridge, breech into the forecastle wall), stepped decks,
slab sides with a lower sponson belt carrying recessed assault turrets, a raised bridge tower (glazed bridge level, antenna, a gatling turret on top), a ventral
hangar blister whose belly doors open onto a free lane, and a four-engine stern (two large and two small hydrogen thrusters).  Charcoal gunmetal hull, orange
accents (bow, forecastle, flank stripe, engine surround), cyan glass.

Naming (the scripts rely on it, docs/design/control_arch.md 3.2, grids_bays 6.3):
  * every block of the ship carries `[SH]` in its CustomName; the pod's blocks carry `[POD]` (never `[SH]`);
  * the three PBs: `[Helm][SH] Warden Helm`, `[Staff][SH] Warden Staff` (ship grid) and `[Pod] Pod PB` (pod grid);
  * weapon batteries by tag: `[B1]` = the two railguns (RAIL); turrets are never batteries; point-defence gatlings carry `[PD]`;
  * terminal groups: `B1`, `PD`, `Main Turrets`, `Thrusters`, `Weapons`, `Decoys`, `[Hangar 1]` (the 8 belly doors), `Gyros`, `Reactors`, `Batteries`;
  * the 101 LargeBlockConveyor junctions are `[SH] Junction n` (fuel and ammunition network, see _conveyors).
Lite = the same ship without the unseen-shape blocks (Sensor, AI Offensive, AI Flight); everything else is built from sample-derived shapes.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import hulls as H
from hulls import Volume, Zone, Zones
import sebp_toolbar as TB
import sebp_multi as MU
from sebp_inv import Item
import pods
from pods import orient_attached

ROOT = C.ROOT
B = Volume.box

def _script(name):
    return script_text(name) if os.path.exists(os.path.join(ROOT, 'scripts', name)) else ''

def _pb(name):
    """the PB's <Program> element, or '' (a PB with no program) while the script file does not exist yet"""
    t = _script(name)
    return pb_extra(t) if t else ''

HELM_SCRIPT, STAFF_SCRIPT = 'ShrikeAI.cs', 'StaffAI.cs'
SCRIPTS = [HELM_SCRIPT, STAFF_SCRIPT, 'CradleBayAI.cs']

TAG = 'WARDEN'

# Helm Custom Data of the large ships (ShrikeAI.cs is the Helm of every ship class; these keys are the class tuning, measured in the sim: docs/design/ships)
HELM_DATA = """
[Link]
Block Prefix=[SH]

[Shrike]
AI Block Search Assist=false
Standoff Range=700
Min Range=150
Evasive Jinking=false
Orbit Speed=10
Combat Speed=40

[Manual]
HUD Surface=0

[Battery]
Standoff By Weapons=false
"""
# spare magazines in each of the two ammo lockers (no conveyor line: the crew or a conveyor added by hand moves them into the guns)
LOCKER_RESERVE = [Item.ammo('LargeRailgunAmmo', 10), Item.ammo('LargeCalibreAmmo', 20), Item.ammo('MediumCalibreAmmo', 30), Item.ammo('NATO_25x184mm', 100)]
GUN = H.hsv_mask(215, .13, .36)
GUNL = H.hsv_mask(215, .10, .52)
GUND = H.hsv_mask(220, .16, .24)
ORANGE = H.hsv_mask(28, .85, .95)
ORANGE_D = H.hsv_mask(24, .80, .70)
GLASSC = H.hsv_mask(190, .55, .85)
BLACK_ = H.BLACK
GUNC = (0.0, -0.81, -0.45)

def nm(s):
    return f'[SH] {s}'

# ------------------------------------------------------------------------------------------------------------
# layout constants (cells; x Right, y Up, z Backward, bow at -z, centre column x = 0)
# ------------------------------------------------------------------------------------------------------------
HW = 4                                   # main hull cells x -4..4 (9 wide)
Z_BOW, Z_MAIN0, Z_MAIN1 = 0, 12, 30      # bow tip, forecastle wall / first main-hull layer, last hull layer (stern wall)
Y_BELLY, Y_DECK, Y_FORE = 2, 6, 4        # lowest hull layer, deck layer, foredeck layer
HANGAR_Z = (17, 24)                      # hangar interior z (8 cells = 20 m), x -1..1, y 1..4
CONN_Z = (18, 23)                        # large Connector cells (y = 5): 5 cells = 12.5 m apart, the pod's ConnectorMedium spacing
RAIL_X, RAIL_Z0 = (-2, 2), 4             # rails: 1 wide x 2 high x 8 long, y 5..6, z 4..11 (breech on the forecastle wall)

def _bow_cell(c):
    x, y, z = c
    return abs(x) <= 1.0 + z * 3.0 / 11.0 + 1e-9 and not (y == 4 and z < 3)

def _volumes():
    """the envelope pieces (Volume) and the reserved system volumes of the hull"""
    v = {}
    bow = Volume.from_function((-5, 2, 0), (6, 5, 12), _bow_cell)
    main = B((-HW, Y_BELLY, Z_MAIN0), (HW + 1, Y_DECK + 1, Z_MAIN1 + 1))
    blister = B((-3, 0, 15), (4, 2, 26)) | Volume.wedge((-3, 0, 12), (4, 2, 15), '-y', '-z', 1) | Volume.wedge((-3, 0, 26), (4, 2, 28), '-y', '+z', 1)
    ridge = B((-1, 5, 7), (2, 7, 12)) | Volume.wedge((-1, 5, 5), (2, 7, 7), '+y', '-z', 1)
    parapet = Volume((x, 5, z) for z in range(6, 12) for x in (-3, 3) if _bow_cell((x, 4, z)))
    base = B((-2, 7, 18), (3, 9, 27))                     # the stem of the tower (solid)
    bridge = B((-3, 9, 19), (4, 11, 26))                   # the bridge level overhangs the stem by one cell each side
    conn = B((-1, 11, 21), (2, 12, 25))
    bulge = [B((5, 3, 17), (6, 5, 26)), B((-5, 3, 17), (-4, 5, 26))]
    v['solid'] = bow | main | blister | ridge | parapet | base | bridge | conn | bulge[0] | bulge[1]
    v['base'] = base
    # --- system volumes (cells the hull builder must leave empty) ---
    v['rails'] = [B((x, 5, RAIL_Z0), (x + 1, 7, RAIL_Z0 + 8)) for x in RAIL_X]
    v['art'] = [B((s * 3 - 1, 7, 13), (s * 3 + 2, 10, 16)) for s in (-1, 1)]
    v['pd_front'] = B((-1, 7, 13), (2, 10, 16))
    v['pd_stern'] = [B((s * 3 - 1, 7, 28), (s * 3 + 2, 10, 31)) for s in (-1, 0, 1)]
    v['pd_belly'] = [B((s * 3 - 1, 0, 9), (s * 3 + 2, 3, 12)) for s in (-1, 1)] + [B((s * 3 - 1, -1, 28), (s * 3 + 2, 2, 31)) for s in (-1, 1)]
    v['assault'] = [B((s * 4 - 1, 5, z), (s * 4 + 2, 7, z + 3)) for s in (-1, 1) for z in (17, 22)]
    v['hangar'] = B((-1, 1, HANGAR_Z[0]), (2, 5, HANGAR_Z[1] + 1))
    v['doors'] = B((-1, 0, HANGAR_Z[0]), (2, 1, HANGAR_Z[1] + 1))
    v['conn'] = [B((0, 5, z), (1, 6, z + 1)) for z in CONN_Z]
    v['eng_l'] = [B((-1, 3, 31), (2, 6, 34))]
    v['eng_s'] = [B((x, y, 31), (x + 1, y + 1, 32)) for x, y in ((-3, 4), (3, 4), (0, 6))]
    return v

def thruster_plan():
    """[(cell, exhaust direction, group, name)] of the 20 small hydrogen thrusters (large grid LargeBlockSmallHydrogenThrust, 1.08 MN each).
    Thrust on the ship points opposite to the exhaust.  Only the two forward Strafe pairs sit flush in the skin; the others stand 1 cell proud."""
    out = []
    for s, sn in ((-1, 'P'), (1, 'S')):
        for x in (1, 3):
            out.append(((s * x, 7, 16), 'Up', 'Down', f'Dive {sn}{x}'))          # exhaust up -> pushes the ship down (2 cells ahead of the COM: 22 MN m of pitch, 8 % of the gyros)
        for z in (9, 28):
            out.append(((s, 1, z), 'Down', 'Up', f'Lift {sn}{z}'))               # exhaust down -> pushes the ship up
        for y in (3, 4):
            out.append(((s * 5, y, 16), 'Forward', 'Back', f'Brake {sn}{y}'))   # exhaust forward -> pushes the ship back
        for z in (10, 11):
            out.append(((s * 4, 3, z), 'Right' if s > 0 else 'Left', 'Left' if s > 0 else 'Right', f'Strafe {sn}{z}'))
        for z in (26, 27):
            out.append(((s * 5, 3, z), 'Right' if s > 0 else 'Left', 'Left' if s > 0 else 'Right', f'Strafe {sn}{z}'))
    return out

# ------------------------------------------------------------------------------------------------------------
def _zones():
    flank = lambda c: c[1] == 3 and abs(c[0]) >= HW and c[2] >= Z_MAIN0
    return Zones(
        Zone('conn', H.region(y=(11, 12)), color=GUNL, kind='plate', flat=('Up',)),
        Zone('bulge', lambda c: abs(c[0]) == HW + 1, color=ORANGE, kind='accent', flat=('Up', 'Down', 'Right', 'Left', 'Backward')),
        Zone('stripe', flank, color=ORANGE, kind='accent'),
        Zone('mouth', lambda c: c[1] <= 1 and 15 <= c[2] <= 26 and abs(c[0]) >= 2, color=ORANGE, kind='accent'),                  # hazard frame round the hangar mouth
        Zone('engine_plate', lambda c: c[2] == Z_MAIN1 and 3 <= c[1] <= 6, color=ORANGE_D, kind='accent'),                         # stern plate behind the nozzles
        Zone('deck_bands', lambda c: c[1] == Y_DECK and c[2] in (16, 27), color=ORANGE, kind='stripe'),
        Zone('bridge_trim', lambda c: c[1] == 9 and abs(c[0]) == 3, color=ORANGE_D, kind='accent'),
        Zone('belt', lambda c: abs(c[0]) >= HW and 3 <= c[1] <= 4 and c[2] >= Z_MAIN0, heavy=True),     # armour belt along both flanks
        Zone('forecastle', lambda c: c[2] == Z_MAIN0 and c[1] >= 5, heavy=True, color=ORANGE, kind='accent'),
        Zone('bowdeck', lambda c: c[2] < Z_MAIN0 and c[1] == Y_FORE, heavy=True, color=ORANGE, kind='accent'),
        Zone('bow', H.region(z=(None, Z_MAIN0)), heavy=True, color=GUN, kind='armor'),
        Zone('floor', lambda c: c[1] == Y_BELLY and abs(c[0]) <= 3 and Z_MAIN0 <= c[2] <= Z_MAIN1 and 15 <= c[2] <= 27, color=GUND, kind='dark'),
        Zone('belly', H.region(y=(None, Y_BELLY + 1)), heavy=True, color=GUND, kind='dark', flat=('Down',)),
        Zone('stern', H.region(z=(Z_MAIN1 - 1, None)), color=GUND, kind='dark'),
        Zone('bridge', H.region(y=(9, None)), color=GUNL, kind='plate'),
        Zone('citadel', lambda c: -2 <= c[0] <= 2 and 7 <= c[1] <= 8 and 17 <= c[2] <= 27, heavy=True, color=GUNL, kind='plate'),
        Zone('tower', H.region(y=(7, None)), color=GUNL, kind='plate'),
        default_color=GUN)

def _bulkheads():
    """interior partitions (light armour): forward of the hangar column, aft of it, and the engine room wall"""
    cells = Volume()
    for z in (16, 25):
        cells = cells | B((-3, 3, z), (4, 6, z + 1))
    return cells

def hull(bp):
    v = _volumes()
    solid = v['solid']
    reserved = {'rails': [c for r in v['rails'] for c in r], 'art': [c for r in v['art'] for c in r], 'pd_front': list(v['pd_front']),
                'pd_stern': [c for r in v['pd_stern'] for c in r], 'pd_belly': [c for r in v['pd_belly'] for c in r],
                'assault': [c for r in v['assault'] for c in r], 'hangar': list(v['hangar']), 'doors': list(v['doors']),
                'conn': [c for r in v['conn'] for c in r], 'eng': [c for r in v['eng_l'] + v['eng_s'] for c in r]}
    thr_cells = {c for c, d, g, n in thruster_plan()}
    reserved['thr'] = sorted(thr_cells)
    shell = solid.hollow(1) | v['base'] | _bulkheads() | B((-3, Y_BELLY, Z_MAIN0), (4, Y_BELLY + 1, Z_MAIN1 + 1))     # + the floor over the hangar blister
    shell = Volume(shell.cells & solid.cells)
    res = H.build_hull(bp, solid, fill=shell, reserved=reserved, skin_around=['hangar', 'doors', 'conn', 'assault', 'pd_belly', 'thr'], zones=_zones(),
                       windows=lambda c: (c[1] == 9 and (c[2] == 19 or abs(c[0]) == 3)) or (c[1] == 10 and c[2] <= 19))
    return res, v

# ------------------------------------------------------------------------------------------------------------
# systems
# ------------------------------------------------------------------------------------------------------------
def _occ(bp):
    return bp.occupancy()

class Ship:
    """collects the placed blocks by role so the toolbars, groups and docs can refer to them"""
    def __init__(self, bp):
        self.bp, self.occ = bp, bp.occupancy()
        self.r = {}

    def put(self, role, b):
        for c in self.bp.cells(b): self.occ[c] = b
        self.r.setdefault(role, []).append(b)
        return b

    def add(self, role, xtype, sub, mn, fwd='Forward', up='Up', name=None, color=GUNC, toward=None, **kw):
        """place with the given orientation when it attaches, else search; toward = the direction of the neighbour it must be attached through"""
        b = orient_attached(self.bp, xtype, sub, mn, toward=toward, prefer=((fwd, up),), occ=self.occ, name=name, color=color, **kw)
        return self.put(role, b)

    def fixed(self, role, xtype, sub, mn, fwd, up, name, color=GUNC, **kw):
        """no search: the orientation is part of the design"""
        b = self.bp.add(xtype, sub, mn, fwd, up, name, color, **kw)
        for c in self.bp.cells(b):
            assert c not in self.occ, f'{self.bp.name}: {name} overlaps {self.occ[c]["name"] or self.occ[c]["sub"]} at {c}'
        return self.put(role, b)

def _weapons(S, full):
    bp = S.bp
    # --- main battery B1: two large railguns in the foredeck channels, muzzle forward, breech on the forecastle wall ---
    for x, tag in zip(RAIL_X, ('P', 'S')):
        b = S.fixed('rail', 'MyObjectBuilder_SmallMissileLauncherReload', 'LargeRailgun', (x, 5, RAIL_Z0), 'Forward', 'Up', nm(f'[B1] Railgun {tag}'), GUNC, kind='rail')
        bp.preload(b, items=[Item.ammo('LargeRailgunAmmo', 6)], state={'capacitor': 'full'})
        bp.keep_clear(b, 3, 'muzzle')
    # --- artillery turrets on the foredeck shoulders (recessed one layer), centre point-defence gatling between them ---
    for s, tag in ((-1, 'P'), (1, 'S')):
        b = S.fixed('art', 'MyObjectBuilder_LargeMissileTurret', 'LargeCalibreTurret', (s * 3 - 1, 7, 13), 'Forward', 'Up', nm(f'Artillery Turret {tag}'), GUNC, kind='turret')
        bp.fill_ammo(b)
    # --- assault turrets on the recessed sponsons ---
    n = 0
    for s, tag in ((-1, 'P'), (1, 'S')):
        for z in (17, 22):
            n += 1
            b = S.fixed('assault', 'MyObjectBuilder_LargeMissileTurret', 'LargeBlockMediumCalibreTurret', (s * 4 - 1, 5, z), 'Forward', 'Up',
                        nm(f'Assault Turret {n} {tag}'), GUNC, kind='turret')
            bp.fill_ammo(b)
    # --- point defence: 8 gatling turrets, hemispherical cover (4 dorsal, 4 ventral) ---
    pd = [('Front', (-1, 7, 13), 'Up'), ('Stern P', (-4, 7, 28), 'Up'), ('Stern C', (-1, 7, 28), 'Up'), ('Stern S', (2, 7, 28), 'Up'),
          ('Belly Bow P', (-4, 0, 9), 'Down'), ('Belly Bow S', (2, 0, 9), 'Down'), ('Belly Aft P', (-4, -1, 28), 'Down'), ('Belly Aft S', (2, -1, 28), 'Down')]
    for i, (tag, mn, up) in enumerate(pd):
        fwd, u = ('Forward', 'Up') if up == 'Up' else ('Forward', 'Down')
        b = S.fixed('pd', 'MyObjectBuilder_LargeGatlingTurret', '', mn, fwd, u, nm(f'[PD] Gatling {i + 1} {tag}'), GUNC, kind='turret')
        bp.fill_ammo(b)

def _propulsion(S):
    bp = S.bp
    # main engines: one large hydrogen thruster (centre) and three small ones (port, starboard, top), exhaust aft
    b = S.fixed('thr_main', 'MyObjectBuilder_Thrust', 'LargeBlockLargeHydrogenThrust', (-1, 3, 31), 'Backward', 'Up', nm('Main Engine (large)'), GUND, kind='thr')
    bp.keep_clear(b, 3, 'exhaust')
    for (x, y), tag in zip(((-3, 4), (3, 4), (0, 6)), ('P', 'S', 'T')):
        b = S.fixed('thr_main', 'MyObjectBuilder_Thrust', 'LargeBlockSmallHydrogenThrust', (x, y, 31), 'Backward', 'Up', nm(f'Main Engine {tag} (small)'), GUND, kind='thr')
        bp.keep_clear(b, 3, 'exhaust')
    for cell, ex, grp, name in thruster_plan():
        up = 'Up' if ex in ('Forward', 'Backward', 'Left', 'Right') else 'Forward'
        b = S.fixed('thr_' + grp.lower(), 'MyObjectBuilder_Thrust', 'LargeBlockSmallHydrogenThrust', cell, ex, up, nm(f'Thruster {name}'), GUND, kind='thr')
        bp.keep_clear(b, 3, 'exhaust')

def _interior(S, full):
    """power, fuel, gyros, cargo: everything sits in the hollow hull on floors and bulkheads (interior cells x -3..3, y 3..5)"""
    bp = S.bp
    # hydrogen tanks (3x3x3) in the engine room z 26..28, one each side of a 1-cell service corridor
    for x, tag in ((-3, 'P'), (1, 'S')):
        S.add('tank', 'MyObjectBuilder_OxygenTank', 'LargeHydrogenTank', (x, 3, 26), 'Forward', 'Up', nm(f'Hydrogen Tank {tag}'), toward='Down', kind='tank', state={'filled': 'full'})
    # cargo containers in the forward compartment z 13..15 (reserve ammunition; no conveyor line: see docs)
    for x, tag in ((-3, 'P'), (1, 'S')):
        S.add('cargo', 'MyObjectBuilder_CargoContainer', 'LargeBlockLargeContainer', (x, 3, 13), 'Forward', 'Up', nm(f'Ammo Locker {tag}'), toward='Down', kind='cargo',
              items=LOCKER_RESERVE)
    # six reactors in the forward corridor (x = 0, y 3..4, z 13..15), uranium pre-loaded
    n = 0
    for y in (3, 4):
        for z in (13, 14, 15):
            n += 1
            S.add('reactor', 'MyObjectBuilder_Reactor', 'LargeBlockSmallGenerator', (0, y, z), 'Forward', 'Up', nm(f'Reactor {n}'), toward='Down' if y == 3 else 'Down', kind='reactor',
                  items=[Item.ingot('Uranium', 100)])
    # flank bays beside the hangar (x +-3, y 3): batteries z 17..19 and 23, gyros z 20..22 and 24
    for s, tag in ((-1, 'P'), (1, 'S')):
        for i, z in enumerate((17, 18, 19, 23)):
            S.add('bat', 'MyObjectBuilder_BatteryBlock', 'LargeBlockBatteryBlock', (s * 3, 3, z), 'Forward', 'Up', nm(f'Battery {tag}{i + 1}'), extra=bat_extra(3), kind='bat', toward='Down')
        for i, z in enumerate((20, 21, 22, 24)):
            S.add('gyro', 'MyObjectBuilder_Gyro', 'LargeBlockGyro', (s * 3, 3, z), 'Forward', 'Up', nm(f'Gyroscope {tag}{i + 1}'), toward='Down', kind='gyro')

def _bridge(S, full):
    """bridge level (y 9, x -1..1, z 20..24): cockpit, gunnery seat, Remote Control, Helm + Staff PB, timers, event controllers"""
    bp = S.bp
    cd = f'[Ship]\nTag={TAG}\n'
    helm_cd = cd + HELM_DATA
    staff_cd = (cd + '\n[Staff]\nBlock Prefix=[SH]\nCockpit Surface=1\nHelm HUD Surface=0\nWing Surface=2\nTurret Target Group=Weapons\nTurret Target Names=Artillery,Assault\n\n[Wing]\nTypes=HORNET,WASP,HARRIER,LANCER,AEGIS,WATCHER\n\n[Hangar 1]\nGroup=[Hangar 1]\nPod=[Pod] Pod PB\n')
    S.cockpit = S.add('cockpit', 'MyObjectBuilder_Cockpit', 'LargeBlockCockpitSeat', (0, 9, 20), 'Forward', 'Up', nm('Bridge Cockpit'), toward='Down', kind='cockpit', settings={'IsMainCockpit': True})
    S.add('gunner', 'MyObjectBuilder_Cockpit', 'CockpitOpen', (1, 9, 20), 'Forward', 'Up', nm('Gunnery Seat'), toward='Down', kind='cockpit')
    S.add('rc', 'MyObjectBuilder_RemoteControl', 'LargeBlockRemoteControl', (-1, 9, 20), 'Forward', 'Up', nm('Remote Control'), toward='Down', kind='ctrl')
    S.helm = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (-1, 9, 24), 'Backward', 'Up', '[Helm][SH] Warden Helm', toward='Backward', kind='ctrl',
                   custom_data=helm_cd, extra=_pb(HELM_SCRIPT))
    S.staff = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (1, 9, 24), 'Backward', 'Up', '[Staff][SH] Warden Staff', toward='Backward', kind='ctrl',
                    custom_data=staff_cd, extra=_pb(STAFF_SCRIPT))
    S.timer = S.add('timer', 'MyObjectBuilder_TimerBlock', 'TimerBlockLarge', (0, 9, 24), 'Forward', 'Up', nm('Timer D (deadman)'), toward='Down', kind='timer', settings={'Delay': 5000, 'Silent': True})
    S.ec_cockpit = S.add('ec', 'MyObjectBuilder_EventControllerBlock', 'EventControllerLarge', (-1, 9, 22), 'Forward', 'Up', nm('EC Cockpit'), toward='Down', kind='timer')
    S.ec_power = S.add('ec', 'MyObjectBuilder_EventControllerBlock', 'EventControllerLarge', (1, 9, 22), 'Forward', 'Up', nm('EC Power'), toward='Down', kind='timer')
    S.ec_core = S.add('ec', 'MyObjectBuilder_EventControllerBlock', 'EventControllerLarge', (0, 9, 22), 'Forward', 'Up', nm('EC Core'), toward='Down', kind='timer')
    if full:
        S.add('ai', 'MyObjectBuilder_OffensiveCombatBlock', 'LargeOffensiveCombat', (-1, 9, 23), 'Forward', 'Up', nm('AI Offensive [AI]'), toward='Down', kind='ai')
        S.add('ai', 'MyObjectBuilder_FlightMovementBlock', 'LargeFlightMovement', (1, 9, 23), 'Forward', 'Up', nm('AI Flight [AI]'), toward='Down', kind='ai')

def _hangar(S, full):
    bp = S.bp
    conns = []
    for i, z in enumerate(CONN_Z):
        b = S.fixed('conn', 'MyObjectBuilder_ShipConnector', 'Connector', (0, 5, z), 'Down', 'Forward', nm(f'Hangar Connector {"AB"[i]}'), GUND, kind='conn')
        conns.append(b)
    S.conns = conns
    doors = []
    for i, z in enumerate(range(HANGAR_Z[0], HANGAR_Z[1] + 1)):
        b = S.fixed('door', 'MyObjectBuilder_AirtightHangarDoor', '', (-1, 0, z), 'Forward', 'Right', nm(f'Hangar Door {i + 1}'), GUND, kind='door')
        doors.append(b)
    S.doors = doors

def _sensing(S, full):
    bp = S.bp
    specs = [('Front', (0, 11, 20), 'Forward', 'Up'), ('Rear', (0, 11, 25), 'Backward', 'Up'), ('Port', (-2, 11, 22), 'Left', 'Up'), ('Starboard', (2, 11, 22), 'Right', 'Up'),
             ('Belly', (0, 1, 5), 'Down', 'Forward')]
    for tag, mn, look, up in specs:
        b = C.cam(bp, mn, look, up, nm(f'Camera {tag}'), 3)
        S.put('cam', b)
    if full:
        for tag, c in (('Lane P', (-4, 1, 21)), ('Lane S', (4, 1, 21))):
            b = orient_attached(bp, 'MyObjectBuilder_SensorBlock', 'LargeBlockSensor', c, toward='Up', prefer=(('Down', 'Forward'),), occ=S.occ, name=nm(f'Sensor {tag}'),
                                color=GUND, kind='sensor')
            S.put('sensor', b)
    # antenna on the conn platform
    b = bp.add('MyObjectBuilder_RadioAntenna', 'LargeBlockCompactRadioAntenna', (0, 12, 24), 'Forward', 'Up', nm('Antenna'), extra=ant_extra(10000), kind='ant')
    S.put('ant', b)

def _decoys(S, res):
    """six decoys flush in the skin (flat plates with exactly one exposed face), symmetric pairs: bow flank, forecastle flank, belly"""
    bp = S.bp
    def ok(c):
        b = res.cell_block.get(c)
        return b is not None and res.cell_kind.get(c) == 'block' and len(res.exposure.get(c, ())) == 1 and b['sub'].endswith('ArmorBlock')
    seeds = [(4, 3, 7), (5, 3, 14), (4, 2, 20)]
    used = []
    for k, (sx, sy, sz) in enumerate(seeds):
        best = None
        for c in res.cell_block:
            m = (-c[0], c[1], c[2])
            if c[0] <= 0 or not ok(c) or not ok(m) or c in used: continue
            d = abs(c[0] - sx) + 2 * abs(c[1] - sy) + abs(c[2] - sz)
            if best is None or d < best[0]: best = (d, c)
        assert best, f'no decoy site near {(sx, sy, sz)}'
        used.append(best[1])
        for i, c in enumerate((best[1], (-best[1][0], best[1][1], best[1][2]))):
            old = res.cell_block[c]
            bp.blocks.remove(old)
            res.blocks.remove(old)
            b = bp.add('MyObjectBuilder_Decoy', 'LargeDecoy', c, 'Forward', 'Up', nm(f'Decoy {2 * k + i + 1}'), GUND, kind='decoy')
            S.put('decoy', b)
            del res.cell_block[c]

def _terminals(S):
    """[(cell, role, label)] cells that must hold a conveyor junction: behind every hydrogen thruster, under every turret, against a tank and a locker.
    Layout seen in the owner's ships (Tech Hunter, Red Ship; docs/design/ships/Warden.md section 7): a LargeBlockSmallHydrogenThrust always has a
    LargeBlockConveyor behind it (1117 of 1117), a gatling turret always stands on one (25 of 25), assault / artillery turrets nearly always (72 of 76 /
    18 of 18 on a junction), a large thruster's back face centre is touched by one (25 of 25), tanks and cargo containers are touched at the face centre."""
    bp = S.bp
    out = []
    for role, blocks in S.r.items():
        for b in blocks:
            cs = bp.cells(b)
            lo = [min(c[i] for c in cs) for i in range(3)]
            hi = [max(c[i] for c in cs) for i in range(3)]
            mid = [(lo[i] + hi[i]) // 2 for i in range(3)]
            if role.startswith('thr_'):
                d = DIR[b['fwd']]                       # exhaust direction; the port is on the opposite face (Back), at its centre
                cell = list(mid)
                for i in range(3):
                    if d[i] > 0: cell[i] = lo[i] - 1
                    elif d[i] < 0: cell[i] = hi[i] + 1
                out.append((tuple(cell), 'thruster', b['name']))
            elif role in ('art', 'assault', 'pd'):
                u = DIR[b['up']]                        # the base is the cell outside the block on the side opposite to Up, under the centre column
                cell = list(mid)
                for i in range(3):
                    if u[i] > 0: cell[i] = lo[i] - 1
                    elif u[i] < 0: cell[i] = hi[i] + 1
                out.append((tuple(cell), 'turret', b['name']))
            elif role == 'tank':
                out.append(((mid[0], mid[1], hi[2] + 1), 'tank', b['name']))        # rear face centre
            elif role == 'cargo':
                out.append(((mid[0], hi[1] + 1, mid[2]), 'locker', b['name']))      # top face centre
    return out

def _conveyors(S, res, vol):
    """one junction network (LargeBlockConveyor cubes, adjacent ones connect) from the two tanks to every thruster, every turret base and the two ammo
    lockers.  Routed by Dijkstra through free hull cavity cells (cost 1) and hidden interior armour cubes (cost 4, replaced by a junction)."""
    import heapq
    bp = S.bp
    env = set(vol['solid'].cells)
    resv = set()
    for v in res.reserved.values(): resv |= set(v.cells)
    terms = _terminals(S)
    tcells = {c for c, r, n in terms}
    conn_nb = set()
    for b in S.conns:
        for c in bp.cells(b):
            for d in DIR.values(): conn_nb.add(tuple(c[i] + d[i] for i in range(3)))

    def cost(c, terminal=False):
        b = S.occ.get(c)
        if b is None:
            if c in env and c not in resv and c not in bp.reserve and c not in conn_nb: return 1
            return None
        hb = res.cell_block.get(c)
        if hb is b and b['sub'].endswith('ArmorBlock') and c not in conn_nb and (terminal or not (res.exposure.get(c) or res.flush.get(c))): return 4
        return None
    for c, r, n in terms:
        assert cost(c, True) is not None, f'{bp.name}: no junction cell behind/under {n} at {c} ({S.occ.get(c) and S.occ[c]["sub"]})'
    net = set()
    start = [c for c, r, n in terms if r == 'tank'][0]
    net.add(start)
    todo = [c for c, r, n in terms if c != start]
    todo.sort(key=lambda c: (abs(c[0]) + abs(c[1] - 4) + abs(c[2] - 28), c))
    while todo:
        # multi-source Dijkstra from the whole network to the nearest unconnected terminal
        dist = {c: 0 for c in net}
        prev = {}
        h = [(0, c) for c in sorted(net)]
        heapq.heapify(h)
        goal = None
        while h:
            d0, c = heapq.heappop(h)
            if d0 > dist.get(c, 1e9): continue
            if c in todo: goal = c; break
            for dv in DIR.values():
                n = (c[0] + dv[0], c[1] + dv[1], c[2] + dv[2])
                k = cost(n, n in tcells)
                if k is None: continue
                nd = d0 + k
                if nd < dist.get(n, 1e9):
                    dist[n] = nd; prev[n] = c; heapq.heappush(h, (nd, n))
        assert goal is not None, f'{bp.name}: conveyor router found no way to {todo[:3]}'
        c = goal
        while c not in net:
            net.add(c); c = prev[c]
        todo = [t for t in todo if t not in net]
    # build the junctions: armour cubes in the way are replaced
    S.conveyors = []
    for i, c in enumerate(sorted(net, key=lambda c: (c[2], c[1], c[0]))):
        old = S.occ.get(c)
        if old is not None:
            bp.blocks.remove(old)
            if old in res.blocks: res.blocks.remove(old)
            res.cell_block.pop(c, None)
            del S.occ[c]
        b = bp.add('MyObjectBuilder_Conveyor', 'LargeBlockConveyor', c, 'Forward', 'Up', nm(f'Junction {i + 1}'), GUND, kind='conv')
        S.put('conv', b)
        S.conveyors.append(b)
    return net

def _groups_and_toolbars(S):
    bp = S.bp
    R = S.r
    bp.group('B1', R['rail'])
    bp.group('PD', R['pd'])
    bp.group('Main Turrets', R['art'] + R['assault'])
    thr = R['thr_main'] + [b for k in R if k.startswith('thr_') and k != 'thr_main' for b in R[k]]
    bp.group('Thrusters', thr)
    bp.group('Weapons', R['rail'] + R['art'] + R['assault'] + R['pd'])
    bp.group('Decoys', R['decoy'])
    bp.group('[Hangar 1]', R['door'])
    bp.group('Gyros', R['gyro']); bp.group('Reactors', R['reactor']); bp.group('Batteries', R['bat'])
    # deadman timer (control_arch 2.6): cuts thrusters and weapons; the EC / Helm re-arm it
    bp.set_toolbars(S.timer, toolbar=[TB.group('Thrusters', 'OnOff_Off'), TB.group('Weapons', 'OnOff_Off')])
    # hand flight: page 1 = weapons, then Helm / Staff / doors
    bp.set_toolbars(S.cockpit, toolbar=[TB.group('B1', 'Shoot'), TB.group('Main Turrets', 'OnOff'), TB.group('PD', 'OnOff'), TB.group('Decoys', 'OnOff'),
                                       TB.group('[Hangar 1]', 'Open_On'), TB.group('[Hangar 1]', 'Open_Off'), TB.run(S.helm), TB.run(S.staff)])
    bp.set_toolbars(S.ec_core, toolbar=[TB.block(S.staff, 'Run')])
    S.ec_core['settings'] = TB.event_settings('integrity', 0.4, invert=True)
    S.ec_core['links'] = {'selected': R['reactor']}

# ------------------------------------------------------------------------------------------------------------
def _world_boxes(bp, only=None):
    """[(lo, hi, block)] axis-aligned world boxes (metres) of every cell of the grid `bp` (a root blueprint or a sub-grid), via its world pose"""
    cs = 2.5 if bp.grid == 'Large' else 0.5
    out = []
    root = getattr(bp, '_world', bp.pose)
    for b in bp.blocks:
        if only and not only(b): continue
        for c in bp.cells(b):
            ctr = root.apply(tuple(v * cs for v in c))
            out.append((tuple(ctr[i] - cs / 2 for i in range(3)), tuple(ctr[i] + cs / 2 for i in range(3)), b))
    return out

def check_hangar(bp, min_gap=0.3):
    """the docked pod sits inside the hangar volume: no cell of the pod within `min_gap` metres of a ship block (the two connector pairs excepted, they
    meet at the lock gap), the launch lane below the doors is empty for 12 m, and the pod nose points down the lane"""
    pod = bp.pod
    bp.grids()                                      # sets the world poses
    ship = _world_boxes(bp)
    pod_boxes = []
    root = pod._world
    for b in pod.blocks:
        for c in pod.cells(b):
            ctr = root.apply(tuple(v * 0.5 for v in c))
            pod_boxes.append((tuple(ctr[i] - 0.25 for i in range(3)), tuple(ctr[i] + 0.25 for i in range(3)), b, c[2]))
    bad = []
    for lo, hi, pb, pz in pod_boxes:
        # the rear layer of the pod (connector plane) meets the hangar ceiling at the lock gap; everything else keeps min_gap
        need = pods.PAIR_GAP - 0.005 if pz >= pods.CONN_Z + 1 else min_gap
        for slo, shi, sb in ship:
            gap = max(max(slo[i] - hi[i], lo[i] - shi[i], 0.0) for i in range(3))
            if gap < need and not (pb['kind'] == 'conn' and sb['kind'] == 'conn'): bad.append((pb['name'] or pb['sub'], sb['name'] or sb['sub'], round(gap, 3)))
    assert not bad, f'{bp.name}: pod too close to the hull: {bad[:4]}'
    # lane: x -1..1 cells (7.5 m), from the door plane (y = -1.25 m) 12 m down, whole hangar length
    lane_lo = (-3.75, -13.25, (HANGAR_Z[0] - 0.5) * 2.5)
    lane_hi = (3.75, -1.25, (HANGAR_Z[1] + 0.5) * 2.5)
    inside = [(sb['name'] or sb['sub']) for slo, shi, sb in ship
              if all(slo[i] < lane_hi[i] - 1e-6 and shi[i] > lane_lo[i] + 1e-6 for i in range(3))]
    assert not inside, f'{bp.name}: blocks in the launch lane: {inside[:4]}'
    nose = pod.pose.rot.apply(MU.DIRS['Forward'])
    assert MU._dot(nose, (0, -1, 0)) > 0.99, 'the pod nose does not point down the lane'
    return True

def warden(lite=False, with_pod=True, lock=False, name=None, wing=0):
    full = not lite
    name = name or ('Warden Lite' if lite else 'Warden')
    bp = Blueprint(name, 'Large', seed=seed_for(name))
    res, vol = hull(bp)
    S = Ship(bp)
    _hangar(S, full)
    _weapons(S, full)
    _propulsion(S)
    _interior(S, full)
    _bridge(S, full)
    _sensing(S, full)
    _decoys(S, res)
    _conveyors(S, res, vol)
    res.check()                                   # hulls: every remaining hull block still meets the hull with its mount faces
    _groups_and_toolbars(S)
    bp.ship = S
    bp.hull = res
    if with_pod:
        pod = pods.cradle_pod(4, lite=lite, name=f'{name} Cradle Pod', ship_tag=TAG)
        pod.display = 'Warden Cradle Pod'
        if wing: pods.load_hornets(pod, wing, lite)         # 'Warden Wing': four Hornets clamped in the pod
        pods.dock_pod(bp, pod, S.conns, lock=lock)
        bp.pod = pod
        check_hangar(bp)
    pods.assert_clean(bp)
    return C.finalize(bp)

def numbers(bp):
    """the figures of the design record (docs/design/ships/Warden.md): dict, all in SI-ish units as named"""
    st = C.stats_db()
    blocks = bp.blocks
    stat = lambda b: C._stat(b)
    n = {}
    n['blocks'] = len(blocks)
    n['dims_m'] = bp.stats()['dims']
    hull_m = sum(C.block_mass(b) for b in blocks)
    items_kg = sum(it.kg() for b in blocks for it in bp.items_of(b))
    n['mass_t_blocks'] = hull_m / 1000.0
    n['mass_t_preload'] = items_kg / 1000.0
    n['mass_t_total'] = (hull_m + items_kg) / 1000.0
    n['accel_empty_m_s2'] = {d: round(f / hull_m, 2) for d, f in C.thrust_by_dir(bp).items()}
    n['accel_loaded_m_s2'] = {d: round(f / (hull_m + items_kg), 2) for d, f in C.thrust_by_dir(bp).items()}
    n['thrust_MN'] = {d: round(f / 1e6, 2) for d, f in C.thrust_by_dir(bp).items()}
    n['pcu_ship'] = sum(stat(b).get('pcu') or 0 for b in blocks)
    n['hp_M'] = round(sum((stat(b).get('integrity') or {}).get('sum_of_components') or 0 for b in blocks) / 1e6, 2)
    pod = getattr(bp, 'pod', None)
    if pod is not None:
        n['pcu_pod'] = sum(C._stat(b).get('pcu') or 0 for b in pod.blocks)
        n['mass_t_pod'] = sum(C.block_mass(b) for b in pod.blocks) / 1000.0
        n['pcu_total'] = n['pcu_ship'] + n['pcu_pod']
    gens = [b for b in blocks if b['sub'] == 'LargeBlockSmallGenerator']
    bats = [b for b in blocks if b['xtype'].endswith('_BatteryBlock')]
    gen_mw = sum(stat(b)['stats']['max_output_mw'] for b in gens)
    bat_mwh = sum(stat(b)['stats']['max_stored_mwh'] for b in bats)
    bat_mw = sum(stat(b)['stats']['max_output_mw'] for b in bats)
    rails = [b for b in blocks if b['sub'] == 'LargeRailgun']
    rail_mw = 38.0 * len(rails)                         # catalogue 2.6: 0.5 MWh per slug at 0.8 efficiency over 59.2 s
    gyros = [b for b in blocks if b['sub'] == 'LargeBlockGyro']
    other_mw = 0.05 + 0.01 * len(gyros) + 0.002 * 8 + 0.002 * 6      # idle turrets, gyros, PBs and the rest (rough)
    n['power'] = dict(generation_MW=gen_mw, battery_MWh=bat_mwh, battery_MW=bat_mw, railgun_recharge_MW=rail_mw, other_MW=round(other_mw, 2),
                      surplus_MW=round(gen_mw - rail_mw - other_mw, 1), uranium_kg=sum(it.amount for b in gens for it in bp.items_of(b)),
                      uranium_hours_full_load=round(sum(it.amount for b in gens for it in bp.items_of(b)) / max(gen_mw, 1e-9), 1))
    big = [b for b in blocks if b['sub'] == 'LargeBlockLargeHydrogenThrust']
    small_main = [b for b in blocks if b['sub'] == 'LargeBlockSmallHydrogenThrust' and 'Main Engine' in (b['name'] or '')]
    lps = 642.7 * stat(big[0])['stats']['max_power_mw'] if big else 0
    sps = 642.7 * 1.25
    fwd_burn = len(big) * lps + len(small_main) * sps
    tanks = [b for b in blocks if b['sub'] == 'LargeHydrogenTank']
    litres = 15_000_000 * len(tanks)
    n['fuel'] = dict(tank_ML=litres / 1e6, full_forward_burn_L_s=round(fwd_burn), full_burn_minutes=round(litres / fwd_burn / 60, 1),
                     minutes_at_25pct_forward=round(litres / (0.25 * fwd_burn) / 60, 0))
    mags = {}
    for b in blocks:
        for it in bp.items_of(b):
            if it.kind == 'AmmoMagazine': mags.setdefault(b['sub'] or 'LargeGatlingTurret', []).append((b['name'], it.sub, it.amount))
    n['ammo_per_gun'] = {'LargeRailgun': 6, 'LargeCalibreTurret': 6, 'LargeBlockMediumCalibreTurret': 6, 'LargeGatlingTurret': 24 * 140}
    n['weapons'] = dict(railguns=len(rails), artillery=sum(1 for b in blocks if b['sub'] == 'LargeCalibreTurret'),
                        assault=sum(1 for b in blocks if b['sub'] == 'LargeBlockMediumCalibreTurret'), gatling=sum(1 for b in blocks if b['xtype'].endswith('LargeGatlingTurret')),
                        decoys=sum(1 for b in blocks if b['xtype'].endswith('_Decoy')))
    # catalogue 2.6 sustained data DPS per weapon
    dps = {'LargeRailgun': 817.0, 'LargeCalibreTurret': 2666.7, 'LargeBlockMediumCalibreTurret': 1263.2, 'LargeGatlingTurret': 791.7}
    n['dps_sustained'] = round(sum(dps[b['sub'] or 'LargeGatlingTurret'] for b in blocks if (b['sub'] or 'LargeGatlingTurret') in dps and (b['kind'] in ('rail', 'turret'))))
    return n

# ------------------------------------------------------------------------------------------------------------
# deck plans (text) for the design record: python3 -I tools/designs/warden.py --plans
# ------------------------------------------------------------------------------------------------------------
_SYM = {'rail': 'R', 'turret': 'T', 'thr': 't', 'gyro': 'G', 'bat': 'B', 'reactor': 'F', 'tank': 'K', 'cargo': 'C', 'conv': 'j', 'door': 'D', 'conn': 'N', 'ctrl': 'P',
        'cockpit': 'S', 'decoy': 'X', 'cam': 'c', 'ant': 'a', 'sensor': 'n', 'ai': 'i', 'timer': 'e'}

def deck_plan(bp, y):
    """one horizontal layer as text: rows = z (bow at the top), columns = x (port left); '.' = empty, '#' armour, 'w' window, letters = systems (_SYM)"""
    occ = bp.occupancy()
    cells = [c for c in occ if c[1] == y]
    if not cells: return ''
    zs = [c[2] for c in cells]
    xr = range(-6, 7)
    rows = []
    for z in range(min(zs), max(zs) + 1):
        row = ''
        for x in xr:
            b = occ.get((x, y, z))
            if b is None: row += '.'
            elif b['xtype'].endswith('_CubeBlock'): row += 'w' if 'Window' in b['sub'] else '#'
            else: row += _SYM.get(b['kind'], '?')
        rows.append(f'z{z:>2} ' + row)
    return f'y = {y}\n   ' + ''.join(str(abs(x) % 10) for x in xr) + '   (columns x -6..6)\n' + '\n'.join(rows)

DOCS = {'Warden': 'LARGE-grid frigate (about 85 x 27.5 x 40 m, 2.6 kt with ammunition): two large railguns (battery [B1]), two artillery and four assault turrets, eight gatling turrets '
                  'for point defence, six decoys, a four-engine hydrogen stern (1 large + 3 small, 4 m/s2), 20 small hydrogen thrusters for the other axes, a bridge tower with cockpit, '
                  'Remote Control and the Helm and Staff programmable blocks, and a belly hangar (8 doors) with a docked, empty 4-slot Cradle Pod for Hornet-class drones (own CubeGrid and '
                  'Pod PB). Ammunition, uranium, hydrogen and batteries are pre-loaded; 101 junction blocks feed the thrusters and every turret from the tanks and two ammo lockers. '
                  'Limits: the two railguns have no feed (6 slugs each, reload by hand from the lockers), the pod carries no drones, and nothing of this has been pasted in the game yet.'}
MODEL_NOTES_WARDEN = ('Large grid, paste only (delete the old blueprint folder first). Needs the Helm / Staff / Pod scripts for autonomous play (PBs stay empty until the script files exist); '
                      'flyable by hand from the bridge cockpit. The pod is a second grid docked by two connector pairs: press Lock on both hangar connectors if it did not lock '
                      '(docs/design/ships/Warden.md, C1..C4). Drones are added by pasting them against the pod\'s merge blocks.')

MODEL_NOTES = {'Warden': MODEL_NOTES_WARDEN}          # shown in the README by build_all.model_note
def warden_wing(lite=False):
    return warden(lite, name='Warden Wing' + (' Lite' if lite else ''), wing=4)

DOCS['Warden Wing'] = ('The Warden with four Hornets already clamped in its pod (each stern merge block on a cradle merge block): launch them with `launch all` on the Staff PB. '
                       'Adds 216 blocks and about 4,000 PCU to the empty Warden.')
MODEL_NOTES['Warden Wing'] = MODEL_NOTES_WARDEN
MODELS = {'Warden': warden, 'Warden Wing': warden_wing}

if __name__ == '__main__':
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument('--render', default=None)
    ap.add_argument('--plans', action='store_true', help='print the deck plans of the design record and exit')
    a = ap.parse_args()
    if a.plans:
        b_ = warden(False)
        for y_ in (0, 1, 3, 4, 5, 6, 7, 9, 11): print(deck_plan(b_, y_), '\n')
        sys.exit(0)
    rc = C.run_module(MODELS, min_acc=0.5)
    import json
    print(json.dumps(numbers(warden(False)), indent=1, default=str))
    if a.render:
        import _shiprender as R
        bp = warden(False)
        print(R.render_sheet(bp, a.render, 'warden'))
    sys.exit(rc)
