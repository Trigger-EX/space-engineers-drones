"""Leviathan: large-grid dreadnought (about 220 m), armoured dorsal citadel, forward main-battery spine, two flank hangars with docked cradle pods, wide engine block.

Models: 'Leviathan' / 'Leviathan Lite' = the ship plus TWO docked, empty 'Cradle Pod's (designs/pods.py), each a second / third CubeGrid.
Design record, layout map, numbers and the in-game checks: docs/design/ships/Leviathan.md.   Run `python3 -I tools/designs/leviathan.py [--render DIR] [--plans]`.

Shape language: a massive STEPPED WEDGE.  A chisel arrowhead bow (3 cells wide at the tip, 23 at the shoulders) carries the forward MAIN-BATTERY SPINE (six large railguns
in six channels between seven armour ribs, breeches into the gunhouse wall); behind it the hull steps up: forecastle with the artillery turrets recessed on both flank ledges,
the 17-wide upper hull with a sloped front, the ARMOURED DORSAL CITADEL (three stepped heavy layers) and the glazed bridge tower; the flanks carry the assault and gatling
turrets on ledges, two FLANK HANGARS (eight doors each, orange frames) with the docked pods; the stern is a WIDE ENGINE BLOCK (27 wide, raked top, four large hydrogen
nozzles in a row, flank nacelles with the manoeuvring thrusters) and a pair of swept fins.  Charcoal gunmetal hull, orange accent zones, cyan bridge glass.

Naming (the scripts rely on it, docs/design/control_arch.md 3.2 and grids_bays 6.3):
  * every block of the ship carries `[SH]` in its CustomName; the pods' blocks carry `[POD1]` (port) / `[POD2]` (starboard), never `[SH]`;
  * the PBs: `[Helm][SH] Leviathan Helm`, `[Staff][SH] Leviathan Staff` (ship grid; M6: no Gunnery PB, its one useful job is a StaffAI Custom Data key, docs/design/ships/Leviathan.md); `[Pod] Pod PB 1` / `[Pod] Pod PB 2` (pod grids);
  * weapon battery tag `[B1]` = the six railguns (RAIL); turrets are never batteries; point-defence gatlings carry `[PD]`;
  * terminal groups: B1, Artillery, Assault, Rockets, PD, Main Turrets, Turrets, Weapons, Thrusters, Decoys, [Hangar 1] (port doors), [Hangar 2] (starboard doors), Gyros, Reactors,
    Batteries, Tanks;
  * the LargeBlockConveyor junctions are `[SH] Junction n` (fuel and ammunition network, see _conveyors).
Lite = the same ship without the unseen-shape blocks (Sensor, AI Offensive, AI Flight); everything else is built from sample-derived shapes.
"""
import sys, os, math, time, heapq
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


HELM_SCRIPT, STAFF_SCRIPT, POD_SCRIPT = 'ShrikeAI.cs', 'StaffAI.cs', 'CradleBayAI.cs'
SCRIPTS = [HELM_SCRIPT, STAFF_SCRIPT, POD_SCRIPT]

TAG = 'LEVIATHAN'

# Helm Custom Data of the large ships (ShrikeAI.cs is the Helm of every ship class; these keys are the class tuning, measured in the sim: docs/design/ships)
HELM_DATA = """
[Link]
Block Prefix=[SH]

[Shrike]
AI Block Search Assist=false
Standoff Range=800
Min Range=150
Evasive Jinking=false
Orbit Speed=10
Combat Speed=40

[Manual]
HUD Surface=0

[Battery]
Standoff By Weapons=false
"""
# spare magazines in each of the four ammunition lockers (the conveyor network feeds the artillery / assault / rocket turrets from them; the railguns are not fed)
LOCKER_RESERVE = [Item.ammo('LargeRailgunAmmo', 20), Item.ammo('LargeCalibreAmmo', 100), Item.ammo('MediumCalibreAmmo', 150),
                  Item.ammo('NATO_25x184mm', 300), Item.ammo('Missile200mm', 60)]

GUN = H.hsv_mask(215, .13, .36)
GUNL = H.hsv_mask(215, .10, .52)
GUND = H.hsv_mask(220, .16, .24)
ORANGE = H.hsv_mask(28, .85, .95)
ORANGE_D = H.hsv_mask(24, .80, .70)
GUNC = (0.0, -0.81, -0.45)


def nm(s):
    return f'[SH] {s}'


# ------------------------------------------------------------------------------------------------------------
# layout constants (cells; x Right, y Up, z Backward, bow at z = 0, centre column x = 0, mirror x -> -x, cell = 2.5 m)
# ------------------------------------------------------------------------------------------------------------
LEN = 88                       # z 0..87  (220 m)
HW = 11                        # main hull half width (23 wide)
HW_POD = 14                    # flank nacelles reach x = +-14 (29 cells = 72.5 m over the nacelles)
Z_RAIL0, Z_RAIL1 = 12, 19      # railgun channels
Z_FORE0 = 20                   # gunhouse wall / start of the upper hull and of the ledges
Z_POD0 = 56                    # flank nacelles start
Z_RAKE = 66                    # the stern block starts to rise
Z_TIER2_END = 75
Y_T1 = 4                       # top cell of tier 1 (full width), y 0..4
Y_T2 = 7                       # top cell of the upper hull (17 wide), y 5..7
Y_CIT = 10                     # citadel y 8..10
Y_STEM = 11                    # tower stem
Y_BRIDGE = (12, 14)            # bridge level y 12..14, interior y 13
Z_COM = 51                     # z of the thruster groups that must act through the centre of mass (checked in numbers())
HANGAR_Z = (41, 48)            # hangar interior z (8 cells = 20 m)
HANGAR_Y = (1, 3)              # hangar interior y
CONN_Z = (42, 47)              # large Connector cells (5 cells = 12.5 m apart, the pod's ConnectorMedium spacing)
CONN_X = 6                     # connector wall column (|x|); the pod occupies |x| 7..9, free cell 10, doors at |x| 11


def halfw(z):
    return 1.0 + (z + 0.5) * 10.0 / 20.0 if z < Z_FORE0 else float(HW)


def top1(z):
    """highest cell of tier 1 (full width) at z"""
    if z < Z_FORE0: return 2
    if z < Z_RAKE: return Y_T1
    return Y_T1 + min(6, (z - Z_RAKE) // 3)        # raked stern block: one step of 3 cells up every 3 cells


def bottom1(z):
    return 1 if z < 6 else 0


def _tier1():
    def f(c):
        x, y, z = c
        if abs(x) > halfw(z): return False
        return bottom1(z) <= y <= top1(z)
    return Volume.from_function((-HW, 0, 0), (HW + 1, 12, LEN), f)


def _tier2():
    box = B((-8, Y_T1 + 1, Z_FORE0), (9, Y_T2 + 1, Z_TIER2_END + 1))
    ramp = Volume.wedge((-8, Y_T1 + 1, Z_FORE0 - 6), (9, Y_T2 + 1, Z_FORE0), '+y', '-z', run=2)
    return box | ramp


def _ribs():
    return Volume((x, y, z) for x in range(-6, 7, 2) for y in (3, 4) for z in range(Z_RAIL0, Z_RAIL1 + 1))


def _citadel():
    return Volume.loft('y', Y_T2 + 1, Y_CIT + 1, (0.5 - 7, 0.5 + 7, 36, 66), (0.5 - 4, 0.5 + 4, 41, 61))


def _tower():
    stem = B((-2, Y_STEM, 54), (3, Y_STEM + 1, 61))
    bridge = B((-4, Y_BRIDGE[0], 52), (5, Y_BRIDGE[1] + 1, 63))
    return stem | bridge


def _fins():
    poly = [(8, 71), (12, 73), (14, 86), (14, 88), (8, 88)]            # (y, z) side profile
    return Volume.prism(poly, 'x', 4, 5).mirror('x', 0.5)


def _pods():
    """flank nacelles x 12..14: a box with a raked upper front (the brake thrusters sit in the vertical lower front, y 2..4)"""
    p = B((12, 0, Z_POD0), (HW_POD + 1, 8, LEN))
    cut = Volume((x, y, z) for x in range(12, HW_POD + 1) for y in (5, 6, 7) for z in range(Z_POD0, Z_POD0 + 1 + 2 * (y - 4)))
    return (p - cut).mirror('x', 0.5)


def _ram():
    """the ram keel under the bow: 3 cells wide, 2 deep at the tip, fading into the belly at z = 22"""
    poly = [(0, 2), (-2, 5), (-2, 12), (-1, 17), (0, 22)]               # (y, z) side profile
    return Volume.prism(poly, 'x', -1, 2)


def _stern_floor():
    return B((-10, 3, 81), (11, 4, 84))


# ------------------------------------------------------------------------------------------------------------
# hardpoints: ledge slots, deck sites, thrusters (all in one table so the reserved volumes and the blocks agree)
# ------------------------------------------------------------------------------------------------------------
def _ledge_x0(s):
    return 9 if s > 0 else -11


def _ledge_z0(k):
    return Z_FORE0 + 3 * k


ART_K = (0, 1, 2, 3)            # z 20..31 (forecastle)
GAT_K = (4, 7, 13, 14)
ASL_K = (5, 6, 8, 9, 11, 12)
DIVE_K = 10                     # the Dive thrusters sit at the centre of mass
ROCKET_K = 15


def turret_sites():
    """[dict(role, mn, up, size, where, s)] every turret of the ship"""
    out = []
    side = {1: 'S', -1: 'P'}
    for s in (-1, 1):
        for k in ART_K:
            out.append(dict(role='art', mn=(_ledge_x0(s), Y_T1 + 1, _ledge_z0(k)), up='Up', size=(3, 3, 3), where=f'{side[s]}{ART_K.index(k) + 1}', s=s))
        for k in ASL_K:
            out.append(dict(role='assault', mn=(_ledge_x0(s), Y_T1 + 1, _ledge_z0(k)), up='Up', size=(3, 2, 3), where=f'{side[s]}{ASL_K.index(k) + 1}', s=s))
        for k in GAT_K:
            out.append(dict(role='pd', mn=(_ledge_x0(s), Y_T1 + 1, _ledge_z0(k)), up='Up', size=(3, 3, 3), where=f'Ledge {side[s]}{GAT_K.index(k) + 1}', s=s))
        out.append(dict(role='rocket', mn=(_ledge_x0(s), Y_T1 + 1, _ledge_z0(ROCKET_K)), up='Up', size=(3, 3, 3), where=f'Ledge {side[s]}', s=s))
        # upper hull deck: one gatling and one rocket turret each side (x 5..7 / -7..-5)
        x0 = 5 if s > 0 else -7
        out.append(dict(role='pd', mn=(x0, Y_T2 + 1, 22), up='Up', size=(3, 3, 3), where=f'Deck {side[s]}', s=s))
        out.append(dict(role='rocket', mn=(x0, Y_T2 + 1, 28), up='Up', size=(3, 3, 3), where=f'Deck {side[s]}', s=s))
        # stern block steps: gatlings (base = the step top)
        for z0 in (72, 78):
            out.append(dict(role='pd', mn=(_ledge_x0(s), top1(z0) + 1, z0), up='Up', size=(3, 3, 3), where=f'Stern {side[s]}{(z0 - 72) // 6 + 1}', s=s))
        # belly: gatlings recessed into the keel (flush with the belly plane)
        for z0 in (24, 76):
            out.append(dict(role='pd', mn=(_ledge_x0(s), 0, z0), up='Down', size=(3, 3, 3), where=f'Belly {side[s]}{(z0 - 24) // 52 + 1}', s=s))
    for z0 in (28, 60):
        out.append(dict(role='pd', mn=(-1, 0, z0), up='Down', size=(3, 3, 3), where=f'Keel {(z0 - 28) // 32 + 1}', s=0))
    return out


def thruster_sites():
    """[dict(group, mn, exhaust, name)] the 14 large hydrogen thrusters.  Thrust on the ship points opposite to the exhaust."""
    out = []
    for i, x0 in enumerate((-10, -5, 3, 8)):
        out.append(dict(group='main', mn=(x0, 4, 85), exhaust='Backward', name=f'Main Engine {i + 1}'))
    for s, sn in ((-1, 'P'), (1, 'S')):
        x0 = 12 if s > 0 else -14
        out.append(dict(group='brake', mn=(x0, 2, Z_POD0), exhaust='Forward', name=f'Brake {sn}'))
        out.append(dict(group='side', mn=(x0, 2, Z_COM + 16), exhaust='Right' if s > 0 else 'Left', name=f'Strafe {sn} aft'))
        out.append(dict(group='side', mn=(9 if s > 0 else -11, 1, Z_COM - 19), exhaust='Right' if s > 0 else 'Left', name=f'Strafe {sn} fore'))
        out.append(dict(group='lift', mn=(2 if s > 0 else -4, 0, _ledge_z0(DIVE_K)), exhaust='Down', name=f'Lift {sn}'))
        out.append(dict(group='dive', mn=(_ledge_x0(s), Y_T1 + 1, _ledge_z0(DIVE_K)), exhaust='Up', name=f'Dive {sn}'))
    return out


def _vol(mn, size):
    return B(mn, tuple(mn[i] + size[i] for i in range(3)))


def _volumes():
    v = {}
    v['solid'] = _tier1() | _tier2() | _ribs() | _citadel() | _tower() | _fins() | _pods() | _ram()
    v['rails'] = [B((x, 3, Z_RAIL0), (x + 1, 5, Z_RAIL1 + 1)) for x in (-5, -3, -1, 1, 3, 5)]
    # hangars: interior |x| 7..10, connector wall |x| = 6, doors |x| = 11
    v['hangar'] = []
    v['doors'] = []
    v['conn'] = []
    for s in (-1, 1):
        lo, hi = (7, 11) if s > 0 else (-10, -6)
        v['hangar'].append(B((lo, HANGAR_Y[0], HANGAR_Z[0]), (hi, HANGAR_Y[1] + 1, HANGAR_Z[1] + 1)))
        v['doors'].append(B((HW if s > 0 else -HW, HANGAR_Y[0], HANGAR_Z[0]), ((HW + 1) if s > 0 else (-HW + 1), HANGAR_Y[1] + 1, HANGAR_Z[1] + 1)))
        v['conn'].append([B((s * CONN_X, 2, z), (s * CONN_X + 1, 3, z + 1)) for z in CONN_Z])
    v['turrets'] = [(t, _vol(t['mn'], t['size'])) for t in turret_sites()]
    v['thr'] = [(t, _vol(t['mn'], (3, 3, 3))) for t in thruster_sites()]
    return v


# ------------------------------------------------------------------------------------------------------------
_RING = None


def _nozzle_ring():
    """cells touching a main-engine nozzle: the glowing frame of the stern"""
    global _RING
    if _RING is None:
        ring = set()
        for t in thruster_sites():
            if t['group'] == 'main':
                ring |= _vol(t['mn'], (3, 3, 3)).dilate(1, 'edge').cells
        _RING = ring
    return _RING


def _zones():
    ax = lambda c: abs(c[0])
    ring = _nozzle_ring()
    hang_frame = lambda c: ax(c) == HW and HANGAR_Z[0] - 1 <= c[2] <= HANGAR_Z[1] + 1 and (c[1] in (0, 4) or c[2] in (HANGAR_Z[0] - 1, HANGAR_Z[1] + 1))
    return Zones(
        Zone('hangar_frame', hang_frame, color=ORANGE, kind='accent'),
        Zone('nozzle_ring', lambda c: c in ring and c[2] >= LEN - 4, heavy=True, color=ORANGE_D, kind='accent'),
        Zone('bow_tip', H.region(z=(None, 7)), heavy=True, color=ORANGE, kind='accent'),
        Zone('gunhouse', lambda c: 12 <= c[2] <= 19 and c[1] >= 5, heavy=True, color=ORANGE, kind='accent'),
        Zone('gunhouse_wall', lambda c: 20 <= c[2] <= 22 and c[1] >= 5, heavy=True, color=GUNL, kind='plate'),
        Zone('rib_tops', lambda c: Z_RAIL0 <= c[2] <= Z_RAIL1 and c[1] == 4, heavy=True, color=ORANGE_D, kind='accent'),
        Zone('rib_sides', lambda c: Z_RAIL0 <= c[2] <= Z_RAIL1, heavy=True, color=GUNL, kind='plate'),
        Zone('bow', H.region(z=(None, Z_FORE0)), heavy=True, color=GUN, kind='armor'),
        Zone('citadel_band', lambda c: Y_T2 < c[1] <= Y_CIT and c[2] in (36, 37, 64, 65), heavy=True, color=ORANGE, kind='accent'),
        Zone('citadel', lambda c: Y_T2 < c[1] <= Y_CIT, heavy=True, color=GUNL, kind='plate'),
        Zone('bridge_trim', lambda c: c[1] == Y_BRIDGE[0] and ax(c) >= 3, color=ORANGE_D, kind='accent'),
        Zone('tower', lambda c: c[1] >= Y_STEM, color=GUNL, kind='plate'),
        Zone('deck_edge', lambda c: ax(c) == HW and c[1] == Y_T1 and Z_FORE0 <= c[2] < Z_RAKE + 3, color=ORANGE, kind='accent'),
        Zone('stern_plate', H.region(z=(LEN - 3, None)), heavy=True, color=GUND, kind='dark'),
        Zone('fins', lambda c: ax(c) == 4 and c[1] >= 8, color=ORANGE, kind='accent'),
        Zone('pod_cap', lambda c: ax(c) >= 12 and (c[2] <= Z_POD0 + 1 or c[2] >= LEN - 2), heavy=True, color=ORANGE_D, kind='accent'),
        Zone('pod_stripe', lambda c: ax(c) >= 12 and c[1] in (4, 5) and (c[2] // 4) % 2 == 0, color=ORANGE_D, kind='accent'),
        Zone('pods', lambda c: ax(c) >= 12, color=GUND, kind='dark'),
        Zone('belt', lambda c: ax(c) == HW and 1 <= c[1] <= 3 and Z_FORE0 <= c[2], heavy=True, color=GUN, kind='armor'),
        Zone('keel', lambda c: c[1] <= 0 and ax(c) <= 5, heavy=True, color=GUND, kind='dark', flat=('Down',)),
        Zone('belly', H.region(y=(None, 1)), color=GUND, kind='dark', flat=('Down',)),
        Zone('upper', lambda c: c[1] > Y_T1, color=GUNL, kind='plate'),
        Zone('engine_block', H.region(z=(Z_RAKE, None)), color=GUND, kind='dark'),
        default_color=GUN)


def hull(bp):
    v = _volumes()
    solid = v['solid']
    reserved = {}
    reserved['rails'] = [c for r in v['rails'] for c in r]
    reserved['hangar'] = [c for r in v['hangar'] for c in r]
    reserved['doors'] = [c for r in v['doors'] for c in r]
    reserved['conn'] = [c for rr in v['conn'] for r in rr for c in r]
    reserved['thr'] = [c for t, r in v['thr'] for c in r]
    reserved['belly_pd'] = [c for t, r in v['turrets'] if t['up'] == 'Down' for c in r]
    reserved['turrets'] = [c for t, r in v['turrets'] if t['up'] != 'Down' for c in r]
    shell = solid.hollow(1, bulkhead_every=10)
    shell = shell | _stern_floor() | B((-4, Y_BRIDGE[0], 52), (5, Y_BRIDGE[0] + 1, 63))           # + the stern floor and the bridge floor
    shell = Volume(shell.cells & solid.cells)
    res = H.build_hull(bp, solid, fill=shell, reserved=reserved, skin_around=['hangar', 'doors', 'conn', 'thr', 'belly_pd'], zones=_zones(),
                       windows=lambda c: (c[1] == 13 and (c[2] == 52 or abs(c[0]) == 4)) or (c[1] == 12 and c[2] == 52) or (c[1] == 14 and c[2] == 52))
    return res, v


# ------------------------------------------------------------------------------------------------------------
# systems
# ------------------------------------------------------------------------------------------------------------
class Ship:
    """collects the placed blocks by role so the toolbars, groups, conveyors and docs can refer to them"""
    def __init__(self, bp, res, vol):
        self.bp, self.res, self.vol = bp, res, vol
        self.occ = bp.occupancy()
        self.r = {}
        env = set(vol['solid'].cells)
        resv = set()
        for vv in res.reserved.values(): resv |= set(vv.cells)
        self.env, self.resv = env, resv

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

    def free_cavity(self, cells):
        """True when every cell is an empty interior cell (inside the envelope, not occupied, not reserved)"""
        return all(c in self.env and c not in self.occ and c not in self.resv and c not in self.bp.reserve for c in cells)


def _weapons(S, full):
    bp = S.bp
    # --- main battery B1: six large railguns in the spine channels, muzzle forward, breech on the gunhouse wall ---
    for i, x in enumerate((-5, -3, -1, 1, 3, 5)):
        b = S.fixed('rail', 'MyObjectBuilder_SmallMissileLauncherReload', 'LargeRailgun', (x, 3, Z_RAIL0), 'Forward', 'Up', nm(f'[B1] Railgun {i + 1}'), GUNC, kind='rail')
        bp.preload(b, items=[Item.ammo('LargeRailgunAmmo', 6)], state={'capacitor': 'full'})
        bp.keep_clear(b, 3, 'muzzle')
    counters = {}
    spec = {'art': ('MyObjectBuilder_LargeMissileTurret', 'LargeCalibreTurret', 'Artillery Turret'),
            'assault': ('MyObjectBuilder_LargeMissileTurret', 'LargeBlockMediumCalibreTurret', 'Assault Turret'),
            'pd': ('MyObjectBuilder_LargeGatlingTurret', '', '[PD] Gatling'),
            'rocket': ('MyObjectBuilder_LargeMissileTurret', '', 'Rocket Turret')}
    for t in turret_sites():
        role = 'pd' if (t['role'] == 'rocket' and not full) else t['role']          # Lite: no rocket turret (its XML shape is derived, never seen): a gatling turret stands there
        xt, sub, label = spec[role]
        counters[role] = counters.get(role, 0) + 1
        b = S.fixed(role, xt, sub, t['mn'], 'Forward', t['up'], nm(f'{label} {counters[role]} {t["where"]}'), GUNC, kind='turret')
        bp.fill_ammo(b)


def _propulsion(S):
    bp = S.bp
    for t in thruster_sites():
        up = 'Up' if t['exhaust'] in ('Forward', 'Backward', 'Left', 'Right') else 'Forward'
        b = S.fixed('thr_' + t['group'], 'MyObjectBuilder_Thrust', 'LargeBlockLargeHydrogenThrust', t['mn'], t['exhaust'], up, nm(f'Thruster {t["name"]}'), GUND, kind='thr')
        bp.keep_clear(b, 3, 'exhaust')


GYRO_BANKS = [((-9, 1, 62), (6, 1, 3)), ((4, 1, 62), (6, 1, 3)), ((-9, 1, 72), (6, 1, 3)), ((4, 1, 72), (6, 1, 3))]   # (min cell, (nx, ny, nz)): 18 gyros each, 72 in the machinery banks (72 more in the nose, see below); a gyro has one mount face, so one layer on the floor


def _interior(S, full):
    """power, fuel, gyros, stores: everything sits in the hollow hull on the belly skin or the stern floor (docs: compartment table)"""
    bp = S.bp
    # hydrogen tanks (3x3x3) in front of the four main engines, on the stern floor (y = 3); the junction at the centre of each engine's back face touches the tank
    for i, x0 in enumerate((-10, -5, 3, 8)):
        S.add('tank', 'MyObjectBuilder_OxygenTank', 'LargeHydrogenTank', (x0, 4, 81), 'Forward', 'Up', nm(f'Hydrogen Tank {i + 1}'), toward='Down', kind='tank', state={'filled': 'full'})
    # ammunition lockers (3x3x3 cargo containers) in the two forward compartments
    n = 0
    for z0 in (22, 33):
        for x0 in (-7, 5):
            n += 1
            S.add('cargo', 'MyObjectBuilder_CargoContainer', 'LargeBlockLargeContainer', (x0, 1, z0), 'Forward', 'Up', nm(f'Ammo Locker {n}'), toward='Down', kind='cargo', items=LOCKER_RESERVE if full else LOCKER_RESERVE[:-1])
    # core: the large reactor and eight small ones under the citadel, uranium pre-loaded
    S.add('reactor', 'MyObjectBuilder_Reactor', 'LargeBlockLargeGenerator', (-1, 1, 54), 'Forward', 'Up', nm('Reactor Large'), toward='Down', kind='reactor', items=[Item.ingot('Uranium', 500)])
    n = 0
    for x in (-3, 3):
        for z in (54, 55, 56, 57):
            n += 1
            S.add('reactor', 'MyObjectBuilder_Reactor', 'LargeBlockSmallGenerator', (x, 1, z), 'Forward', 'Up', nm(f'Reactor {n}'), toward='Down', kind='reactor',
                  items=[Item.ingot('Uranium', 100)])
    # battery bank (5 x 2 x 2 = 20) between the hangars
    n = 0
    for x in range(-2, 3):
        for y in (1, 2):
            for z in (43, 44):
                n += 1
                S.add('bat', 'MyObjectBuilder_BatteryBlock', 'LargeBlockBatteryBlock', (x, y, z), 'Forward', 'Up', nm(f'Battery {n}'), extra=bat_extra(3), kind='bat', toward='Down',
                      state={'stored_power': 'full'})
    # gyros: four banks of 6 x 3 on the belly skin (a gyro mounts through its bottom face only, so they cannot be stacked)
    n = 0
    for (x0, y0, z0), (nx, ny, nz) in GYRO_BANKS:
        for x in range(x0, x0 + nx):
            for y in range(y0, y0 + ny):
                for z in range(z0, z0 + nz):
                    n += 1
                    S.add('gyro', 'MyObjectBuilder_Gyro', 'LargeBlockGyro', (x, y, z), 'Forward', 'Up', nm(f'Gyroscope {n}'), toward='Down', kind='gyro')
    # 72 more (144 in all, 2x the first design) on the belly floor of the nose cavity: z 16..19 full rows (x +-1..7/8/9, none on the centre line), z 15 x +-1..4
    for z, xm in ((19, 9), (18, 8), (17, 8), (16, 7), (15, 4)):
        for x in [-v for v in range(xm, 0, -1)] + list(range(1, xm + 1)):
            n += 1
            S.add('gyro', 'MyObjectBuilder_Gyro', 'LargeBlockGyro', (x, 1, z), 'Forward', 'Up', nm(f'Gyroscope {n}'), toward='Down', kind='gyro')


def _bridge(S, full):
    """bridge level (interior y 13, x -3..3, z 53..61): two seats, Remote Control, Helm + Staff PB, timer, event controllers"""
    bp = S.bp
    cd = f'[Ship]\nTag={TAG}\n'
    helm_cd = cd + HELM_DATA
    staff_cd = (cd + '\n[Staff]\nBlock Prefix=[SH]\nCockpit Surface=1\nHelm HUD Surface=0\nWing Surface=2\nTurret Target Group=Weapons\nTurret Target Names=Artillery,Assault\n\n[Wing]\nTypes=HORNET,WASP,HARRIER,LANCER,AEGIS,WATCHER\n'
                '\n[Hangar 1]\nGroup=[Hangar 1]\nPod=[Pod] Pod PB 1\n\n[Hangar 2]\nGroup=[Hangar 2]\nPod=[Pod] Pod PB 2\n')
    S.cockpit = S.add('cockpit', 'MyObjectBuilder_Cockpit', 'LargeBlockCockpitSeat', (0, 13, 53), 'Forward', 'Up', nm('Bridge Cockpit'), toward='Down', kind='cockpit', settings={'IsMainCockpit': True})
    S.gunner = S.add('cockpit', 'MyObjectBuilder_Cockpit', 'LargeBlockCockpitSeat', (2, 13, 54), 'Forward', 'Up', nm('Gunnery Seat'), toward='Down', kind='cockpit')
    S.add('rc', 'MyObjectBuilder_RemoteControl', 'LargeBlockRemoteControl', (-2, 13, 54), 'Forward', 'Up', nm('Remote Control'), toward='Down', kind='ctrl')
    S.helm = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (-2, 13, 59), 'Backward', 'Up', '[Helm][SH] Leviathan Helm', toward='Down', kind='ctrl',
                   custom_data=helm_cd, extra=_pb(HELM_SCRIPT))
    S.staff = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (2, 13, 59), 'Backward', 'Up', '[Staff][SH] Leviathan Staff', toward='Down', kind='ctrl',
                    custom_data=staff_cd, extra=_pb(STAFF_SCRIPT))
    S.timer = S.add('timer', 'MyObjectBuilder_TimerBlock', 'TimerBlockLarge', (0, 13, 57), 'Forward', 'Up', nm('Timer D (deadman)'), toward='Down', kind='timer', settings={'Delay': 5000, 'Silent': True})
    S.ec_cockpit = S.add('ec', 'MyObjectBuilder_EventControllerBlock', 'EventControllerLarge', (-2, 13, 61), 'Forward', 'Up', nm('EC Cockpit'), toward='Down', kind='timer')
    S.ec_power = S.add('ec', 'MyObjectBuilder_EventControllerBlock', 'EventControllerLarge', (0, 13, 61), 'Forward', 'Up', nm('EC Power'), toward='Down', kind='timer')
    S.ec_core = S.add('ec', 'MyObjectBuilder_EventControllerBlock', 'EventControllerLarge', (2, 13, 61), 'Forward', 'Up', nm('EC Core'), toward='Down', kind='timer')
    if full:
        S.add('ai', 'MyObjectBuilder_OffensiveCombatBlock', 'LargeOffensiveCombat', (-3, 13, 55), 'Forward', 'Up', nm('AI Offensive [AI]'), toward='Down', kind='ai')
        S.add('ai', 'MyObjectBuilder_FlightMovementBlock', 'LargeFlightMovement', (3, 13, 55), 'Forward', 'Up', nm('AI Flight [AI]'), toward='Down', kind='ai')


def _hangars(S, full):
    """per side: two large Connectors in the wall |x| = 6 (contact face outward), eight AirtightHangarDoors at |x| = 11"""
    bp = S.bp
    S.conns = {}
    S.doors = {}
    for s, sn, gname in ((-1, 'P', '[Hangar 1]'), (1, 'S', '[Hangar 2]')):
        face = 'Right' if s > 0 else 'Left'
        cs = []
        for i, z in enumerate(CONN_Z):
            b = S.fixed('conn', 'MyObjectBuilder_ShipConnector', 'Connector', (s * CONN_X, 2, z), face, 'Forward', nm(f'Hangar {sn} Connector {"AB"[i]}'), GUND, kind='conn')
            cs.append(b)
        S.conns[s] = cs
        ds = []
        for i, z in enumerate(range(HANGAR_Z[0], HANGAR_Z[1] + 1)):
            b = S.fixed('door', 'MyObjectBuilder_AirtightHangarDoor', '', (s * HW, HANGAR_Y[0], z), 'Backward', 'Up', nm(f'Hangar {sn} Door {i + 1}'), GUND, kind='door')
            ds.append(b)
        S.doors[s] = ds


def _sensing(S, full):
    bp = S.bp
    # a camera mounts through its back face only: each one stands against a hull face and looks away from it
    specs = [('Front', (0, 3, 11), 'Forward', 'Up'), ('Rear', (0, 9, 88), 'Backward', 'Up'), ('Port', (-12, 3, 24), 'Left', 'Up'), ('Starboard', (12, 3, 24), 'Right', 'Up'),
             ('Belly', (0, -1, 40), 'Down', 'Forward')]
    for tag, mn, look, up in specs:
        b = C.cam(bp, mn, look, up, nm(f'Camera {tag}'), 3)
        S.put('cam', b)
    S.add('ant', 'MyObjectBuilder_RadioAntenna', 'LargeBlockCompactRadioAntenna', (0, 13, 63), 'Up', 'Backward', nm('Antenna'), extra=ant_extra(20000), kind='ant', toward='Forward')
    if full:
        for tag, c in (('Lane P', (-12, 0, 44)), ('Lane S', (12, 0, 44))):
            b = orient_attached(bp, 'MyObjectBuilder_SensorBlock', 'LargeBlockSensor', c, toward='Left' if c[0] > 0 else 'Right', prefer=(('Forward', 'Up'),), occ=S.occ, name=nm(f'Sensor {tag}'),
                                color=GUND, kind='sensor')
            S.put('sensor', b)


def _decoys(S, res):
    """six pairs of decoys flush in the skin (flat plates with exactly one exposed face), symmetric pairs"""
    bp = S.bp
    def ok(c):
        b = res.cell_block.get(c)
        return b is not None and res.cell_kind.get(c) == 'block' and len(res.exposure.get(c, ())) == 1 and b['sub'].endswith('ArmorBlock')
    seeds = [(8, 2, 14), (11, 2, 36), (11, 2, 55), (8, 6, 60), (13, 4, 80), (8, 0, 56)]
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
            S.occ[c] = b
            del res.cell_block[c]


def _greebles(res):
    """thin armour panels (ArmorPanel, 1 PCU each) as deck seams: lines along the upper-hull deck, a regular grid (every third cell, so the ship stays mirror symmetric) on the bow deck, the ledges and the stern wall"""
    H.greeble(res, 'panel', pattern='lines', every=4, normals=('Up',), where=lambda c: c[1] == Y_T2 and Z_FORE0 + 3 <= c[2] <= Z_TIER2_END and abs(c[0]) <= 7, color=GUND, seed=3)
    H.greeble(res, 'panel', pattern='grid', every=3, normals=('Up',), where=lambda c: c[1] <= Y_T1 and not (c[2] < 12 and c[1] >= 2 and abs(c[0]) <= 6) and (c[2] < Z_FORE0 - 1 or (abs(c[0]) >= 9 and Z_FORE0 <= c[2] < Z_RAKE)), color=GUND)
    H.greeble(res, 'panel', pattern='grid', every=3, normals=('Backward',), where=lambda c: c[2] >= LEN - 1 and c[1] >= 7, color=ORANGE_D)


def _terminals(S):
    """[(cell, role, label)] cells that must hold a conveyor junction: behind every hydrogen thruster, under every artillery / assault / rocket
    turret, against the tanks' tops and the lockers' tops (layout seen in the owner's ships, see docs/design/ships/Warden.md section 7: thrusters always have a junction behind
    them, turrets stand on one, tanks and containers are touched at a face centre).  Gatling turrets keep their own 3,360 rounds and are not on the network."""
    bp = S.bp
    out = []
    for role, blocks in S.r.items():
        for b in blocks:
            cs = bp.cells(b)
            lo = [min(c[i] for c in cs) for i in range(3)]
            hi = [max(c[i] for c in cs) for i in range(3)]
            mid = [(lo[i] + hi[i]) // 2 for i in range(3)]
            if role.startswith('thr_'):
                d = DIR[b['fwd']]
                cell = list(mid)
                for i in range(3):
                    if d[i] > 0: cell[i] = lo[i] - 1
                    elif d[i] < 0: cell[i] = hi[i] + 1
                out.append((tuple(cell), 'thruster', b['name']))
            elif role in ('art', 'assault', 'rocket'):
                u = DIR[b['up']]
                cell = list(mid)
                for i in range(3):
                    if u[i] > 0: cell[i] = lo[i] - 1
                    elif u[i] < 0: cell[i] = hi[i] + 1
                out.append((tuple(cell), 'turret', b['name']))
            elif role == 'tank':
                out.append(((mid[0], hi[1] + 1, mid[2]), 'tank', b['name']))
            elif role == 'cargo':
                out.append(((mid[0], hi[1] + 1, mid[2]), 'locker', b['name']))
    return out


def _conveyors(S):
    """one junction network (LargeBlockConveyor cubes; adjacent junctions connect) from the tanks to every manoeuvring thruster, every artillery / assault / rocket turret base and
    the four ammo lockers.  Routed by Dijkstra through free hull cavity (cost 1) and hidden interior armour cubes (cost 4, replaced by a junction), never next to a hangar connector."""
    bp, res = S.bp, S.res
    env, resv = S.env, S.resv
    terms = _terminals(S)
    tcells = {c for c, r, n in terms}
    conn_nb = set()
    for s in S.conns:
        for b in S.conns[s]:
            for c in bp.cells(b):
                for d in DIR.values(): conn_nb.add(tuple(c[i] + d[i] for i in range(3)))
    keep = set(bp.reserve)

    def cost(c, terminal=False):
        b = S.occ.get(c)
        if b is None:
            if c in env and c not in resv and c not in keep and c not in conn_nb: return 1
            return None
        hb = res.cell_block.get(c)
        if hb is b and b['sub'].endswith('ArmorBlock') and c not in conn_nb and (terminal or not (res.exposure.get(c) or res.flush.get(c))): return 4
        return None
    for c, r, n in terms:
        assert cost(c, True) is not None, f'{bp.name}: no junction cell for {n} at {c} ({S.occ.get(c) and (S.occ[c]["name"] or S.occ[c]["sub"])})'
    net = set()
    start = [c for c, r, n in terms if r == 'tank'][0]
    net.add(start)
    todo = [c for c, r, n in terms if c != start]
    todo.sort(key=lambda c: (abs(c[0]) + abs(c[1] - 2) + abs(c[2] - 82), c))
    while todo:
        dist = {c: 0 for c in net}
        prev = {}
        h = [(0, c) for c in sorted(net)]
        heapq.heapify(h)
        goal = None
        tset = set(todo)
        while h:
            d0, c = heapq.heappop(h)
            if d0 > dist.get(c, 1e9): continue
            if c in tset: goal = c; break
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
    rockets = R.get('rocket', [])                     # Lite has none
    bp.group('B1', R['rail'])
    bp.group('Artillery', R['art'])
    bp.group('Assault', R['assault'])
    if rockets: bp.group('Rockets', rockets)
    bp.group('PD', R['pd'])
    bp.group('Main Turrets', R['art'] + R['assault'])
    bp.group('Turrets', R['art'] + R['assault'] + rockets + R['pd'])
    thr = [b for k in R if k.startswith('thr_') for b in R[k]]
    bp.group('Thrusters', thr)
    bp.group('Weapons', R['rail'] + R['art'] + R['assault'] + rockets + R['pd'])
    bp.group('Decoys', R['decoy'])
    bp.group('[Hangar 1]', S.doors[-1])
    bp.group('[Hangar 2]', S.doors[1])
    bp.group('Gyros', R['gyro']); bp.group('Reactors', R['reactor']); bp.group('Batteries', R['bat']); bp.group('Tanks', R['tank'])
    # deadman timer (control_arch 2.6): cuts thrusters and weapons; the EC / Helm re-arm it
    bp.set_toolbars(S.timer, toolbar=[TB.group('Thrusters', 'OnOff_Off'), TB.group('Weapons', 'OnOff_Off')])
    # hand flight: page 1 = weapons, then Helm / Staff and the doors
    cock = [TB.group('B1', 'Shoot'), TB.group('Main Turrets', 'OnOff'), TB.group('PD', 'OnOff')]
    if rockets: cock.append(TB.group('Rockets', 'OnOff'))
    cock += [TB.group('Decoys', 'OnOff'), TB.group('[Hangar 1]', 'Open_On'), TB.group('[Hangar 1]', 'Open_Off'), TB.run(S.helm), TB.run(S.staff),
             TB.run(S.staff, 'deploy', experimental=True), TB.run(S.staff, 'dock', experimental=True)]
    bp.set_toolbars(S.cockpit, toolbar=cock)
    gun = [TB.group('B1', 'Shoot'), TB.group('Artillery', 'OnOff'), TB.group('Assault', 'OnOff'), TB.group('PD', 'OnOff')]
    if rockets: gun.append(TB.group('Rockets', 'OnOff'))
    gun += [TB.group('[Hangar 2]', 'Open_On'), TB.group('[Hangar 2]', 'Open_Off'), TB.run(S.helm), TB.run(S.staff),
            TB.run(S.staff, 'deploy', experimental=True), TB.run(S.staff, 'dock', experimental=True)]
    bp.set_toolbars(S.gunner, toolbar=gun)
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


def check_hangar(bp, pod, s, min_gap=0.3):
    """pod `pod` sits inside the hangar of side s: no cell of the pod within `min_gap` metres of a ship block (connector planes excepted, they meet at the lock gap), the
    launch lane outboard of the doors (12 m, the hangar's z and y extent) is empty, and the pod nose points down the lane"""
    bp.grids()
    ship = _world_boxes(bp)
    pod_boxes = []
    root = pod._world
    for b in pod.blocks:
        for c in pod.cells(b):
            ctr = root.apply(tuple(v * 0.5 for v in c))
            pod_boxes.append((tuple(ctr[i] - 0.25 for i in range(3)), tuple(ctr[i] + 0.25 for i in range(3)), b, c[2]))
    bad = []
    for lo, hi, pb, pz in pod_boxes:
        need = pods.PAIR_GAP - 0.005 if pz >= pods.CONN_Z + 1 else min_gap
        for slo, shi, sb in ship:
            gap = max(max(slo[i] - hi[i], lo[i] - shi[i], 0.0) for i in range(3))
            if gap < need and not (pb['kind'] == 'conn' and sb['kind'] == 'conn'): bad.append((pb['name'] or pb['sub'], sb['name'] or sb['sub'], round(gap, 3)))
    assert not bad, f'{bp.name}: pod too close to the hull: {bad[:4]}'
    x_out = (HW + 1) * 2.5
    lane_lo = (x_out if s > 0 else -x_out - 12.0, HANGAR_Y[0] * 2.5, HANGAR_Z[0] * 2.5)
    lane_hi = (x_out + 12.0 if s > 0 else -x_out, (HANGAR_Y[1] + 1) * 2.5, (HANGAR_Z[1] + 1) * 2.5)
    inside = [(sb['name'] or sb['sub']) for slo, shi, sb in ship
              if all(slo[i] < lane_hi[i] - 1e-6 and shi[i] > lane_lo[i] + 1e-6 for i in range(3))]
    assert not inside, f'{bp.name}: blocks in the launch lane of side {s}: {inside[:4]}'
    nose = pod.pose.rot.apply(MU.DIRS['Forward'])
    assert MU._dot(nose, (s, 0, 0)) > 0.99, f'the pod nose does not point down the lane (nose {nose})'
    return True


def _dock_upright(bp, pod, conns, nose, lock=False):
    """dock `pod` on the two hangar connectors, nose down the lane, and with the pod's up = the ship's up (a drone then sits upright in its cradle on both sides):
    tries both pairings of the connectors (pod pair A on host A or on host B) and the four rolls about the connector axis"""
    errs = []
    for hc in (list(conns), list(conns)[::-1]):
        for roll in range(4):
            try:
                pose = pods.dock_pod(bp, pod, hc, want_nose=nose, roll=roll, lock=False)
            except AssertionError as e:
                errs.append(str(e)[-80:])
                continue
            if MU._dot(pose.rot.apply(MU.DIRS['Up']), (0, 1, 0)) > 0.99:
                if lock:
                    bp.lock_connectors(hc[0], pod, pod.pod.connectors[0])
                    bp.lock_connectors(hc[1], pod, pod.pod.connectors[1])
                return pose
            bp.subgrids.pop()
            errs.append('upside down')
    raise AssertionError('no upright docking pose: ' + '; '.join(errs))


def _dress(S, res, vol, full=True):
    """artificial gravity for the crew spaces (bridge, stem shaft, central compartment, hangar walls) and round slopes / corners in the concave edges of the big rooms"""
    env = vol['solid'].cells
    if full: C.place_gravity(S, res, env, (0, 5, 55), (60.0, 60.0, 70.0), nm('Gravity Generator'))
    n = 0
    for box in (((-3, 13, 53), (3, 13, 61)), ((-7, 5, 51), (7, 9, 59)), ((-5, 1, 41), (5, 3, 48)), ((-3, 5, 38), (3, 9, 49))):
        n += C.chamfer_room(S, res, env, box)
    S.chamfered = n
    res.check()                                   # the new slopes meet the walls with their mount faces

def _walkways(S, res, vol):
    """entry + walkways: a slide door in each wall of the bridge stem (x = +-2, y 11, reached from the citadel top at (+-3, 11, 57)) opens on the stem shaft
    (x -1..1, z 55..59), which climbs through a hole in the bridge floor to the bridge and drops through the citadel cavity and the bulkhead gap at x = 0 to the
    central compartment; a door in the wall |x| = 6 leads into each hangar"""
    env = set(vol['solid'].cells)
    legs = []
    entry = (2, 11, 57)
    path = [(x, 11, 57) for x in range(-2, 3)] + [(-1, 12, 57), (1, 12, 57)]
    for s in (-1, 1):
        legs.append(C.route_walkway(S, res, env, [(0, 9, 57), (0, 2, 45), (s * 6, 1, 45)]))
    path += [(0, 10, 57), (0, 11, 57)]
    cells = list(dict.fromkeys(path + legs[0] + legs[1]))
    doors = {entry: 'Entry S', (-2, 11, 57): 'Entry P', (-6, 1, 45): 'Hangar P', (6, 1, 45): 'Hangar S'}
    C.carve_walkway(S, res, cells, doors=doors, name='Leviathan walkway', namer=nm)
    rooms = [(-1, 13, 57), (1, 13, 57), (-1, 13, 56), (-1, 13, 58), (-7, 1, 45), (7, 1, 45)]
    C.walk_check(S, (3, 11, 57), [(-1, 13, 57), (-7, 1, 45), (7, 1, 45)], cells + rooms, 'Leviathan walkway')


def leviathan(lite=False, with_pods=True, lock=True, name=None, wing=0):
    full = not lite
    name = name or ('Leviathan Lite' if lite else 'Leviathan')
    bp = Blueprint(name, 'Large', seed=seed_for(name))
    res, vol = hull(bp)
    S = Ship(bp, res, vol)
    _hangars(S, full)
    _weapons(S, full)
    _propulsion(S)
    _interior(S, full)
    _bridge(S, full)
    _sensing(S, full)
    _decoys(S, res)
    _walkways(S, res, vol)
    _conveyors(S)
    _greebles(res)
    res.check()
    _dress(S, res, vol, full)
    C.link_conveyors(bp, [(c, n) for c, r, n in _terminals(S)])
    _groups_and_toolbars(S)
    bp.ship = S
    bp.hull = res
    bp.pods_ = []
    if with_pods:
        for s, tag, pname, nose in ((-1, 'POD1', 'Port', 'Left'), (1, 'POD2', 'Starboard', 'Right')):
            pod = pods.cradle_pod(4, lite=lite, name=f'{name} {pname} Cradle Pod', tag=tag, ship_tag=TAG, first_bay=1 if s < 0 else 5)
            pod.display = f'Leviathan {pname} Cradle Pod'
            pod.pod.pb['name'] = f'[Pod] Pod PB {1 if s < 0 else 2}'
            if wing: pods.load_hornets(pod, wing, lite)         # 'Leviathan Wing': four Hornets in each pod
            _dock_upright(bp, pod, S.conns[s], nose, lock)
            check_hangar(bp, pod, s)
            bp.pods_.append(pod)
    pods.assert_clean(bp)
    return C.finalize(bp)


def build(name='Leviathan'):
    """hull only (development helper)"""
    bp = Blueprint(name, 'Large', seed=seed_for(name))
    res, v = hull(bp)
    return bp, res, v


# ------------------------------------------------------------------------------------------------------------
# figures for the design record (docs/design/ships/Leviathan.md)
# ------------------------------------------------------------------------------------------------------------
CS = 2.5
THRUST_DIR = {'Forward': (0, 0, -1), 'Backward': (0, 0, 1), 'Up': (0, 1, 0), 'Down': (0, -1, 0), 'Left': (-1, 0, 0), 'Right': (1, 0, 0)}


def _centre(bp, b):
    cs = bp.cells(b)
    return tuple((sum(c[i] for c in cs) / len(cs) + 0.5) * CS for i in range(3))


def _items_kg(bp, b):
    return sum(it.kg() for it in bp.items_of(b))


def mass_properties(bp):
    """(total kg, COM metres, (Iyaw, Ipitch, Iroll) kg m2) of the ship grid with its pre-loaded items (point masses at the block centres)"""
    pts = []
    for b in bp.blocks:
        m = C.block_mass(b) + _items_kg(bp, b)
        pts.append((m, _centre(bp, b)))
    M = sum(m for m, c in pts)
    com = tuple(sum(m * c[i] for m, c in pts) / M for i in range(3))
    iy = sum(m * ((c[0] - com[0]) ** 2 + (c[2] - com[2]) ** 2) for m, c in pts)
    ip = sum(m * ((c[1] - com[1]) ** 2 + (c[2] - com[2]) ** 2) for m, c in pts)
    ir = sum(m * ((c[0] - com[0]) ** 2 + (c[1] - com[1]) ** 2) for m, c in pts)
    return M, com, (iy, ip, ir)


def thrust_torques(bp, com):
    """per thrust direction: (total force N, net torque vector N m about the COM when every thruster of that direction fires at 100 %)"""
    out = {}
    for b in bp.blocks:
        if b['xtype'] != 'MyObjectBuilder_Thrust': continue
        f = C._stat(b)['stats']['force_n']
        d = THRUST_DIR[OPP[b['fwd']]]
        r = tuple(_centre(bp, b)[i] - com[i] for i in range(3))
        t = (r[1] * d[2] - r[2] * d[1], r[2] * d[0] - r[0] * d[2], r[0] * d[1] - r[1] * d[0])
        key = OPP[b['fwd']]
        F, T = out.get(key, (0.0, (0.0, 0.0, 0.0)))
        out[key] = (F + f, tuple(T[i] + f * t[i] for i in range(3)))
    return out


def pcu_table(bp):
    """[(category, count, pcu)] for the ship grid, in the order of the design record, and the same for each pod grid"""
    role = {}
    S = getattr(bp, 'ship', None)
    if S is not None:
        for r, blocks in S.r.items():
            for b in blocks: role[id(b)] = r
    names = {'art': 'Artillery turrets (LargeCalibreTurret)', 'assault': 'Assault turrets (LargeBlockMediumCalibreTurret)', 'pd': 'Gatling turrets (point defence)',
             'rocket': 'Rocket turrets', 'rail': 'Railguns', 'gyro': 'Gyroscopes', 'bat': 'Batteries', 'reactor': 'Reactors (1 large + 8 small)', 'tank': 'Hydrogen tanks',
             'cargo': 'Ammunition lockers', 'conv': 'Conveyor junctions', 'door': 'Hangar doors', 'conn': 'Hangar connectors', 'pb': 'Programmable blocks', 'cockpit': 'Seats',
             'rc': 'Remote Control', 'cam': 'Cameras', 'ant': 'Antenna', 'decoy': 'Decoys', 'sensor': 'Sensors (Full)', 'ai': 'AI blocks (Full)', 'timer': 'Timer', 'ec': 'Event controllers'}
    rows = {}

    def add(cat, pcu):
        n, p = rows.get(cat, (0, 0))
        rows[cat] = (n + 1, p + pcu)
    for b in bp.blocks:
        pcu = C._stat(b).get('pcu') or 0
        r = role.get(id(b))
        if r is None: cat = 'Armour, windows, panels' if b['xtype'].endswith('_CubeBlock') else b['sub'] or b['xtype']
        elif r.startswith('thr_'): cat = 'Thrusters (14 large hydrogen)'
        else: cat = names.get(r, r)
        add(cat, pcu)
    order = ['Armour, windows, panels', 'Gatling turrets (point defence)', 'Assault turrets (LargeBlockMediumCalibreTurret)', 'Artillery turrets (LargeCalibreTurret)', 'Rocket turrets',
             'Railguns', 'Gyroscopes', 'Conveyor junctions', 'Hangar doors', 'Hangar connectors', 'Thrusters (14 large hydrogen)', 'Programmable blocks', 'Seats',
             'Reactors (1 large + 8 small)', 'Batteries', 'Hydrogen tanks', 'Ammunition lockers', 'Cameras', 'Antenna', 'Remote Control', 'Decoys', 'Sensors (Full)', 'AI blocks (Full)',
             'Timer', 'Event controllers']
    out = [(k, *rows[k]) for k in order if k in rows] + [(k, *v) for k, v in rows.items() if k not in order]
    return out


def numbers(bp):
    """the figures of the design record: dict, SI-ish units as named"""
    blocks = bp.blocks
    stat = C._stat
    n = {}
    n['blocks'] = len(blocks)
    n['dims_m'] = bp.stats()['dims']
    hull_m = sum(C.block_mass(b) for b in blocks)
    items_kg = sum(_items_kg(bp, b) for b in blocks)
    n['mass_t_blocks'] = round(hull_m / 1000.0, 1)
    n['mass_t_preload'] = round(items_kg / 1000.0, 1)
    n['mass_t_total'] = round((hull_m + items_kg) / 1000.0, 1)
    thr = C.thrust_by_dir(bp)
    n['thrust_MN'] = {d: round(f / 1e6, 2) for d, f in thr.items()}
    n['accel_loaded_m_s2'] = {d: round(f / (hull_m + items_kg), 2) for d, f in thr.items()}
    n['accel_empty_m_s2'] = {d: round(f / hull_m, 2) for d, f in thr.items()}
    M, com, I = mass_properties(bp)
    n['com_m'] = tuple(round(v, 1) for v in com)
    n['inertia_kg_m2'] = tuple(float(f'{v:.3g}') for v in I)
    gyros = [b for b in blocks if b['sub'] == 'LargeBlockGyro']
    tq = len(gyros) * stat(gyros[0])['stats']['force_n']
    n['gyro_torque_MNm'] = round(tq / 1e6, 1)
    n['angular_accel_rad_s2'] = tuple(round(tq / i, 3) for i in I)       # yaw, pitch, roll
    n['turn_90deg_s'] = tuple(round(2 * math.sqrt(math.pi / 2 / (tq / i)), 1) for i in I)
    tt = thrust_torques(bp, com)
    n['thrust_torque_pct_of_gyros'] = {d: round(100 * max(abs(v) for v in T) / tq, 1) for d, (F, T) in tt.items()}
    n['thrust_torque_MNm'] = {d: tuple(round(v / 1e6, 1) for v in T) for d, (F, T) in tt.items()}
    n['pcu_ship'] = sum(stat(b).get('pcu') or 0 for b in blocks)
    n['hp_M'] = round(sum((stat(b).get('integrity') or {}).get('sum_of_components') or 0 for b in blocks) / 1e6, 1)
    heavy = [b for b in blocks if b['xtype'].endswith('_CubeBlock') and 'Heavy' in b['sub']]
    light = [b for b in blocks if b['xtype'].endswith('_CubeBlock') and 'Heavy' not in b['sub'] and 'Window' not in b['sub'] and 'Panel' not in b['sub']]
    n['armour_blocks'] = dict(heavy=len(heavy), light=len(light), windows=sum(1 for b in blocks if 'Window' in b['sub']))
    pods_ = getattr(bp, 'pods_', [])
    if pods_:
        n['pcu_pods'] = [sum(stat(b).get('pcu') or 0 for b in p.blocks) for p in pods_]
        n['mass_t_pods'] = [round(sum(C.block_mass(b) for b in p.blocks) / 1000.0, 1) for p in pods_]
        n['pcu_total'] = n['pcu_ship'] + sum(n['pcu_pods'])
    gens = [b for b in blocks if b['xtype'] == 'MyObjectBuilder_Reactor']
    bats = [b for b in blocks if b['xtype'].endswith('_BatteryBlock')]
    gen_mw = sum(stat(b)['stats']['max_output_mw'] for b in gens)
    bat_mwh = sum(stat(b)['stats']['max_stored_mwh'] for b in bats)
    bat_mw = sum(stat(b)['stats']['max_output_mw'] for b in bats)
    rails = [b for b in blocks if b['sub'] == 'LargeRailgun']
    rail_mw = 38.0 * len(rails)                          # catalogue 2.6: 0.5 MWh per slug at 0.8 efficiency over 59.2 s
    other_mw = 0.01 * len(gyros) + 0.002 * sum(1 for b in blocks if b['xtype'].endswith('Turret')) + 0.1
    uran = sum(it.amount for b in gens for it in bp.items_of(b))
    n['power'] = dict(generation_MW=gen_mw, battery_MWh=bat_mwh, battery_MW=bat_mw, railgun_recharge_MW=rail_mw, other_MW=round(other_mw, 2),
                      surplus_MW=round(gen_mw - rail_mw - other_mw, 1), uranium_kg=uran, uranium_hours_full_load=round(uran / gen_mw, 1),
                      battery_minutes_all_rails_no_reactor=round(bat_mwh / rail_mw * 60, 1))
    tanks = [b for b in blocks if b['sub'] == 'LargeHydrogenTank']
    litres = 15_000_000 * len(tanks)
    per_group = {}
    for b in blocks:
        if b['xtype'] == 'MyObjectBuilder_Thrust':
            g = OPP[b['fwd']]
            per_group[g] = per_group.get(g, 0) + 642.7 * stat(b)['stats']['max_power_mw']
    n['fuel'] = dict(tank_ML=litres / 1e6, forward_burn_L_s=round(per_group['Forward']), all_thrusters_L_s=round(sum(per_group.values())),
                     full_forward_minutes=round(litres / per_group['Forward'] / 60, 1), all_full_minutes=round(litres / sum(per_group.values()) / 60, 1),
                     minutes_at_25pct_forward_10pct_others=round(litres / (0.25 * per_group['Forward'] + 0.10 * (sum(per_group.values()) - per_group['Forward'])) / 60, 0))
    sub = lambda s_: sum(1 for b in blocks if (b['sub'] or b['xtype']) == s_)
    n['weapons'] = dict(railguns=len(rails), artillery=sub('LargeCalibreTurret'), assault=sub('LargeBlockMediumCalibreTurret'),
                        gatling=sum(1 for b in blocks if b['xtype'].endswith('LargeGatlingTurret')), rocket=sum(1 for b in blocks if b['xtype'].endswith('LargeMissileTurret') and not b['sub']),
                        decoys=sum(1 for b in blocks if b['xtype'].endswith('_Decoy')))
    n['ammo_rounds_per_gun'] = {'LargeRailgun': 6, 'LargeCalibreTurret': 6, 'LargeBlockMediumCalibreTurret': 6, 'LargeGatlingTurret': 24 * 140, 'LargeMissileTurret': 6}
    mags = {}
    for b in blocks:
        for it in bp.items_of(b):
            if it.kind == 'AmmoMagazine': mags[it.sub] = mags.get(it.sub, 0) + it.amount
    n['magazines_total'] = mags
    dps = {'LargeRailgun': 817.0, 'LargeCalibreTurret': 2666.7, 'LargeBlockMediumCalibreTurret': 1263.2, 'LargeGatlingTurret': 791.7, 'LargeMissileTurret': 409.1}
    n['dps_sustained'] = round(sum(dps[b['sub'] or 'LargeGatlingTurret' if b['xtype'].endswith('LargeGatlingTurret') else (b['sub'] or 'LargeMissileTurret')]
                                   for b in blocks if b['kind'] in ('rail', 'turret')))
    n['thrusters'] = len([b for b in blocks if b['xtype'] == 'MyObjectBuilder_Thrust'])
    return n


# ------------------------------------------------------------------------------------------------------------
# deck plans (text) for the design record: python3 -I tools/designs/leviathan.py --plans
# ------------------------------------------------------------------------------------------------------------
_SYM = {'rail': 'R', 'turret': 'T', 'thr': 't', 'gyro': 'G', 'bat': 'B', 'reactor': 'F', 'tank': 'K', 'cargo': 'C', 'conv': 'j', 'door': 'D', 'conn': 'N', 'ctrl': 'P',
        'cockpit': 'S', 'decoy': 'X', 'cam': 'c', 'ant': 'a', 'sensor': 'n', 'ai': 'i', 'timer': 'e'}


def deck_plan(bp, y, xr=range(-14, 15), zr=None):
    """one horizontal layer as text: rows = z (bow at the top), columns = x (port left); '.' empty, '#' armour, 'w' window, letters = systems (_SYM)"""
    occ = bp.occupancy()
    cells = [c for c in occ if c[1] == y]
    if not cells: return ''
    zs = [c[2] for c in cells]
    rows = []
    for z in (zr or range(min(zs), max(zs) + 1)):
        row = ''
        for x in xr:
            b = occ.get((x, y, z))
            if b is None: row += '.'
            elif b['xtype'].endswith('_CubeBlock'): row += 'w' if 'Window' in b['sub'] else '#'
            else: row += _SYM.get(b['kind'], '?')
        rows.append(f'z{z:>2} ' + row)
    return f'y = {y}\n    ' + ''.join(str(abs(x) % 10) for x in xr) + f'   (columns x {xr[0]}..{xr[-1]})\n' + '\n'.join(rows)


DOCS = {'Leviathan': 'LARGE-grid dreadnought (about 222 x 72 x 42 m, 14 kt with ammunition): a stepped wedge with an armoured dorsal citadel and a glazed bridge tower, a forward main-battery '
                     'spine of six large railguns (battery [B1]), eight artillery and twelve assault turrets on the flank ledges, twenty gatling turrets for point defence, four rocket turrets (Full only) '
                     'and twelve decoys; four large hydrogen engines (2 m/s2 forward) and ten more large thrusters for the other axes, 144 gyros, one large and eight small reactors, 20 batteries, '
                     'four hydrogen tanks, Helm and Staff programmable blocks, bridge cockpit, gunnery seat and Remote Control, and two flank hangars (eight doors each) with a docked, empty '
                     '4-slot Cradle Pod each (own CubeGrids and Pod PBs, room for 8 Hornet-class drones). Ammunition, uranium, hydrogen and batteries are pre-loaded and about 270 junction blocks feed the '
                     'thrusters and the artillery / assault / rocket turrets from the tanks and four ammo lockers. Limits: the six railguns have no feed (6 slugs each, reload by hand from the lockers), '
                     'the pods carry no drones, about 33,000 PCU with the pods (worlds of 50 k or more; the doc lists what to drop for 20 k), and nothing of this has been pasted in the game yet.'}
MODEL_NOTES_LEVIATHAN = ('Large grid, paste only (delete the old blueprint folder first). About 33,000 PCU with the two pods: needs a world limit of 50 k or more (docs/design/ships/Leviathan.md lists the trimming '
                         'steps for 20 k). The Helm, Staff and Pod scripts are embedded in the PBs; flyable by hand from the bridge cockpit. '
                         'Each pod is a second / third grid docked by two connector pairs inside its hangar: press Lock on the hangar connectors if a pod did not lock. Drones are added by pasting them '
                         "against the pods' merge blocks.")

MODEL_NOTES = {'Leviathan': MODEL_NOTES_LEVIATHAN}          # shown in the README by build_all.model_note
def leviathan_wing(lite=False):
    return leviathan(lite, name='Leviathan Wing' + (' Lite' if lite else ''), wing=4)

DOCS['Leviathan Wing'] = ('The Leviathan with eight Hornets already clamped, four in each hangar pod: launch them with `launch all` on the Staff PB (the second pod follows the first by 20 s). '
                          'Adds 432 blocks and about 8,000 PCU to the empty Leviathan (about 41,000 PCU in all: a world limit of 50 k or more).')
MODEL_NOTES['Leviathan Wing'] = MODEL_NOTES_LEVIATHAN
MODELS = {'Leviathan': leviathan, 'Leviathan Wing': leviathan_wing}


if __name__ == '__main__':
    import argparse, json
    ap = argparse.ArgumentParser()
    ap.add_argument('--render', default=None, help='write the renders of the Full ship into this folder')
    ap.add_argument('--hull', action='store_true', help='hull only (development)')
    ap.add_argument('--plans', action='store_true', help='print the deck plans of the design record and exit')
    ap.add_argument('--pcu', action='store_true', help='print the PCU table and exit')
    a = ap.parse_args()
    if a.hull:
        t0 = time.time()
        bp, res, v = build()
        print('blocks', len(bp.blocks), round(time.time() - t0, 1), 's')
        if a.render:
            import _shiprender as R
            print(R.render_sheet(bp, a.render, 'hull'))
        sys.exit(0)
    if a.plans:
        b_ = leviathan(False, with_pods=False)
        for y_ in (0, 1, 2, 3, 4, 5, 6, 7, 9, 11, 13): print(deck_plan(b_, y_), '\n')
        sys.exit(0)
    if a.pcu:
        b_ = leviathan(False)
        tot = 0
        for cat, k, p_ in pcu_table(b_):
            print(f'{cat:55s} {k:6d} {p_:8d}'); tot += p_
        print(f'{"ship":55s} {len(b_.blocks):6d} {tot:8d}')
        for p in b_.pods_:
            print(p.name, sum(C._stat(b).get('pcu') or 0 for b in p.blocks))
        sys.exit(0)
    t0 = time.time()
    rc = C.run_module(MODELS, min_acc=0.5)
    print('build+validate', round(time.time() - t0, 1), 's')
    bp = leviathan(False)
    print(json.dumps(numbers(bp), indent=1, default=str))
    if a.render:
        import _shiprender as R
        print(R.render_sheet(bp, a.render, 'leviathan'))
    sys.exit(rc)
