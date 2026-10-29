"""Aegis Carrier: large-grid mega carrier modelled on the player mothership of the game Starscape (a rounded-square disc, 157.5 m across).

Model: 'Aegis Carrier' = the ship plus SIX docked, empty 4-slot 'Cradle Pod's (designs/pods.py), each its own CubeGrid: four stand nose-up in the central launch well, one in each side bay.
The Aegis ESCORT DRONE (extra.py) is a different ship: this one is `Aegis Carrier`, ship tag `AEGISC`.
Design record, plan and the in-game checks: docs/design/ships/Aegis_Carrier.md.   Run `python3 -I tools/designs/aegis_carrier.py [--render DIR] [--plans] [--pcu] [--hull]`.

Shape language: a flat DISC (superellipse, exponent 2.6: the rounded square of the reference) with a bevelled rim, a raised HUB RING around a circular LAUNCH WELL in the middle, a glazed
bridge cabin and a small aft pod on the hub, two flat BAY DOORS on the east and west rim, and FOUR boxy ENGINE / WEAPON PODS standing at the diagonal corners (six large hydrogen thrusters,
a tank, an ammo locker, an artillery turret on top and an assault turret underneath each).  Grey plating, dark seams and belly, cyan accent strips (paint only).

Naming (the scripts rely on it, docs/design/control_arch.md 3.2 and grids_bays 6.3), as the Leviathan:
  * every block of the ship carries `[SH]` in its CustomName; the pods' blocks carry `[POD1]` .. `[POD6]`, never `[SH]`;
  * the PBs: `[Helm][SH] Aegis Helm`, `[Staff][SH] Aegis Staff` (ship grid); `[Pod] Pod PB 1` .. `[Pod] Pod PB 6` (pod grids);
  * weapon battery tag `[B1]` = the two bow railguns; turrets are never batteries; point-defence gatlings carry `[PD]`;
  * terminal groups: B1, Artillery, Assault, PD, Main Turrets, Turrets, Weapons, Thrusters, Decoys, [Hangar 1] (port doors), [Hangar 2] (starboard doors), Gyros, Reactors, Batteries, Tanks;
  * the LargeBlockConveyor junctions are `[SH] Junction n`: four independent fuel / ammunition networks, one per engine pod.
Only the Full design is built (the Lite variants are retired).
"""
import sys, os, math, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import hulls as H
from hulls import Volume, Zone, Zones
import sebp
import sebp_toolbar as TB
import sebp_multi as MU
from sebp_inv import Item
import pods
from pods import orient_attached
import leviathan as L
from leviathan import nm, GUNC, GUND
from ballista import PD_SETTINGS

ROOT = C.ROOT
B = Volume.box

HELM_SCRIPT, STAFF_SCRIPT, POD_SCRIPT = L.HELM_SCRIPT, L.STAFF_SCRIPT, L.POD_SCRIPT
SCRIPTS = [HELM_SCRIPT, STAFF_SCRIPT, POD_SCRIPT]
TAG = 'AEGISC'

# ------------------------------------------------------------------------------------------------------------
# layout constants (cells; x Right, y Up, z Backward, bow at -z, the disc axis is the centre of cell (0, *, 0), mirror x -> -x, cell = 2.5 m)
# ------------------------------------------------------------------------------------------------------------
R = 28.5                               # planform radius (cells) of the disc: an octagon of apothem 28.5 (57 cells = 142.5 m), then 45 degree bevels
WELL_R, HUB_TOP_R, HUB_R = 9.0, 14.5, 15.5
X_CONN = 26                            # side-bay connector wall (|x|), pod interior |x| 27..30, doors at |x| = 31
X_DOOR = 31
BAY_Y = (-1, 1)                        # bay interior y
BAY_Z = (-4, 3)                        # bay interior z (8 cells = 20 m)
CONN_Z = (-3, 2)                       # connector cells (5 cells = 12.5 m apart, the pod's ConnectorMedium spacing)
WELL_FLOOR_Y = 3                       # the well floor skin; the pods stand on it
WELL_X = (-5, -2, 2, 5)                # the well pods' connector columns
POD0 = 18                              # the engine pods' inner corner: |x|, |z| = 18 .. 27 (they stand in notched corners of the octagon)
OCT_C = 12                             # the octagon's diagonal sides are |x| + |z| = r + OCT_C
PLANE_K = 29                           # on the diagonal bevels the top cell of a column sits at y = PLANE_K - r (r = 25..28): one plane x + y + z = PLANE_K + OCT_C
RAIL_X = (-3, 3)
RAIL_Z = (-31, -24)
CAB_X, CAB_Z = 4, (-14, -10)           # bridge cabin: x -4..4, z -14..-10, y 7..9 (interior x -3..3, y 7..8, z -13..-11)
MAX_POD_PCU = 0


def _oct(x, z, c):
    """octagon norm with flat sides on the axes and on the diagonals |x| + |z| = m + c.  Along every grid row it grows by exactly one per cell (also where the diagonal side
    dominates), so a bevel of one cell of height per cell of radius is a run of plain slopes and corners; a circular contour, or the regular-octagon norm (0.71 per diagonal
    row), gives treads of one and two cells that alternate and leave a tooth at every tread edge"""
    return max(abs(x), abs(z), abs(x) + abs(z) - c)


def rho(x, z):
    """planform norm of the disc: a regular octagon (flat sides on the axes and on the diagonals |x| + |z| = r + 12). Along every grid row it grows by one per cell, so a bevel of
    one cell of height per cell of radius is exactly the plane x + y + z = const on the diagonal sides, which Corner and CornerInv pieces tile smoothly (see _plane_tiles)"""
    return max(abs(x), abs(z), abs(x) + abs(z) - OCT_C)


def rc(x, z):
    """norm of the hub ring and the well: a square (no diagonal walls, which would be stairs of cubes)"""
    return max(abs(x), abs(z))


def half_t(r):
    """half thickness (cells) of the disc at planform radius r: a 45 degree bevel, one cell of height per cell of radius, so the hull builder meets it with plain slopes"""
    return 4 if r <= 25.5 else 3 if r <= 26.5 else 2 if r <= 27.5 else 1


# ------------------------------------------------------------------------------------------------------------
# the envelope
# ------------------------------------------------------------------------------------------------------------
def _disc():
    def f(c):
        x, y, z = c
        r = rho(x, z)
        if r > R: return False
        d = rc(x, z)
        ht = half_t(r)
        top = 6 if d <= HUB_TOP_R else 5 if d <= HUB_R else ht
        if d <= WELL_R and y >= WELL_FLOOR_Y + 1: return False
        return -ht <= y <= max(ht, top)
    return Volume.from_function((-32, -4, -32), (33, 7, 33), f)


def _despike(vol, passes=4):
    """remove single-column teeth from a disc-like volume: a column top (or bottom) with three or more of its four neighbours lower (higher) is cut by one cell, and a pit
    with three or more higher neighbours is filled by one; a few passes leave only slopes, corners and flat treads on the bevels (no isolated cubes)"""
    cells = set(vol.cells)
    for _ in range(passes):
        top, bot = {}, {}
        for x, y, z in cells:
            if (x, z) not in top or y > top[(x, z)]: top[(x, z)] = y
            if (x, z) not in bot or y < bot[(x, z)]: bot[(x, z)] = y
        changed = False
        for (x, z), t in top.items():
            nb = [(x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1)]
            lower = sum(1 for n in nb if n not in top or top[n] < t)
            higher = sum(1 for n in nb if n in top and top[n] > t)
            if lower >= 3 and t > bot[(x, z)] + 1 and sum(1 for n in nb if n in top) >= 3:
                cells.discard((x, t, z)); changed = True
            elif higher >= 3 and all(n in top for n in nb):
                cells.add((x, t + 1, z)); changed = True
        for (x, z), t in bot.items():
            nb = [(x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1)]
            higher = sum(1 for n in nb if n not in bot or bot[n] > t)
            if higher >= 3 and top[(x, z)] > t + 1 and sum(1 for n in nb if n in bot) >= 3:
                cells.discard((x, t, z)); changed = True
        if not changed: break
    return Volume(cells)


def _bay_modules():
    return B((X_CONN - 3, -2, -5), (X_DOOR + 1, 3, 5)).mirror('x', 0.5)


def _pod_boxes():
    """the four corner pods: 10 x 7 x 10 boxes with chamfered vertical edges"""
    pod = B((POD0, -3, POD0), (POD0 + 10, 4, POD0 + 10)).chamfer("y", 2).chamfer("x", 2).chamfer("z", 2, corners=[(-1, -1), (-1, 1)])
    return pod.mirror('x', 0.5).mirror('z', 0.5)


def _cabin():
    return B((-CAB_X, 7, CAB_Z[0]), (CAB_X + 1, 10, CAB_Z[1] + 1))


def _aft_pod():
    return B((-2, 7, 10), (3, 9, 14))


# ---- engine pod sites: (u, y, w) local coordinates, u = |x| - POD0, w = |z| - POD0 (both grow outward) ----
POD_NAMES = {(-1, -1): 'NW', (1, -1): 'NE', (-1, 1): 'SW', (1, 1): 'SE'}


def _pod_cell(sx, sz, box):
    """box = (u, y, w, su, sy, sw) -> min cell (x, y, z) and size in ship cells"""
    u, y, w, su, sy, sw = box
    x0 = POD0 + u if sx > 0 else -(POD0 + u + su - 1)
    z0 = POD0 + w if sz > 0 else -(POD0 + w + sw - 1)
    return (x0, y, z0), (su, sy, sw)


LOCAL = dict(axial1=(2, -1, 7, 3, 3, 3), axial2=(5, -1, 7, 3, 3, 3), lat_lo=(7, -3, 2, 3, 3, 3), lat_hi=(7, 1, 2, 3, 3, 3), up=(1, 1, 1, 3, 3, 3), down=(1, -3, 1, 3, 3, 3),
             tank=(4, -1, 2, 3, 3, 3), locker=(0, -2, 4, 3, 3, 3), art=(4, 4, 5, 3, 3, 3), assault=(4, -5, 5, 3, 2, 3))


def thruster_sites():
    """[dict(group, mn, exhaust, name, pod)] the 24 large hydrogen thrusters.  Thrust on the ship points opposite to the exhaust."""
    out = []
    for (sx, sz), pn in POD_NAMES.items():
        aft = sz > 0
        for k in (1, 2):
            mn, _ = _pod_cell(sx, sz, LOCAL[f'axial{k}'])
            out.append(dict(group='main' if aft else 'brake', mn=mn, exhaust='Backward' if aft else 'Forward', name=f'{"Main" if aft else "Brake"} {pn} {k}', pod=pn))
        for tag, key in (('lo', 'lat_lo'), ('hi', 'lat_hi')):
            mn, _ = _pod_cell(sx, sz, LOCAL[key])
            out.append(dict(group='side', mn=mn, exhaust='Right' if sx > 0 else 'Left', name=f'Strafe {pn} {tag}', pod=pn))
        mn, _ = _pod_cell(sx, sz, LOCAL['down'])
        out.append(dict(group='lift', mn=mn, exhaust='Down', name=f'Lift {pn}', pod=pn))
        mn, _ = _pod_cell(sx, sz, LOCAL['up'])
        out.append(dict(group='dive', mn=mn, exhaust='Up', name=f'Dive {pn}', pod=pn))
    return out


# PD gatling turrets (3x3x3): dorsal on the top deck (min cell y = 5), ventral under the belly (min cell y = -7)
PD_XZ = {'N': (-1, -22), 'S': (-1, 20), 'E': (20, -1), 'W': (-22, -1), 'NE': (16, -17), 'NW': (-18, -17), 'SE': (16, 15), 'SW': (-18, 15)}


def turret_sites():
    """[dict(role, mn, up, size, where)] every turret of the ship"""
    out = []
    for (sx, sz), pn in POD_NAMES.items():
        mn, size = _pod_cell(sx, sz, LOCAL['art'])
        out.append(dict(role='art', mn=mn, up='Up', size=size, where=pn))
        mn, size = _pod_cell(sx, sz, LOCAL['assault'])
        out.append(dict(role='assault', mn=mn, up='Down', size=size, where=pn))
    for where, (x, z) in PD_XZ.items():
        out.append(dict(role='pd', mn=(x, 5, z), up='Up', size=(3, 3, 3), where=f'Top {where}'))
    for where, (x, z) in PD_XZ.items():
        out.append(dict(role='pd', mn=(x, -7, z), up='Down', size=(3, 3, 3), where=f'Belly {where}'))
    return out


def _vol(mn, size):
    return B(mn, tuple(mn[i] + size[i] for i in range(3)))


def _volumes():
    v = {}
    disc = Volume(c for c in _disc().cells if not (abs(c[0]) >= POD0 and abs(c[2]) >= POD0))       # the engine pods stand in notched corners
    v['solid'] = disc | _bay_modules() | _pod_boxes() | _cabin() | _aft_pod()
    v['rails'] = [B((x, 0, RAIL_Z[0]), (x + 1, 2, RAIL_Z[1] + 1)) for x in RAIL_X]
    v['hangar'] = []
    v['doors'] = []
    v['conn'] = []
    for s in (-1, 1):
        lo, hi = (X_CONN + 1, X_DOOR) if s > 0 else (-X_DOOR + 1, -X_CONN)
        v['hangar'].append(B((lo, BAY_Y[0], BAY_Z[0]), (hi, BAY_Y[1] + 1, BAY_Z[1] + 1)))
        v['doors'].append(B((s * X_DOOR, BAY_Y[0], BAY_Z[0]), (s * X_DOOR + 1, BAY_Y[1] + 1, BAY_Z[1] + 1)))
        v['conn'].append([B((s * X_CONN, 0, z), (s * X_CONN + 1, 1, z + 1)) for z in CONN_Z])
    v['wconn'] = [B((x, WELL_FLOOR_Y, z), (x + 1, WELL_FLOOR_Y + 1, z + 1)) for x in WELL_X for z in CONN_Z]
    v['turrets'] = [(t, _vol(t['mn'], t['size'])) for t in turret_sites()]
    v['thr'] = [(t, _vol(t['mn'], (3, 3, 3))) for t in thruster_sites()]
    v['tanks'] = [_vol(*_pod_cell(sx, sz, LOCAL['tank'])) for (sx, sz) in POD_NAMES]
    v['lockers'] = [_vol(*_pod_cell(sx, sz, LOCAL['locker'])) for (sx, sz) in POD_NAMES]
    return v


# ------------------------------------------------------------------------------------------------------------
# colours and zones
# ------------------------------------------------------------------------------------------------------------
PLATE = H.hsv_mask(210, .06, .62)
PLATE_M = H.hsv_mask(215, .10, .45)
SEAM = H.hsv_mask(220, .16, .24)
CYAN = H.hsv_mask(186, .80, .95)
CYAN_D = H.hsv_mask(190, .75, .70)

_RING = None


def _nozzle_ring():
    """cells touching a thruster nozzle: the glowing blue frame of each engine"""
    global _RING
    if _RING is None:
        ring = set()
        for t in thruster_sites():
            ring |= _vol(t['mn'], (3, 3, 3)).dilate(1, 'edge').cells
        _RING = ring
    return _RING


def _compass(c):
    """angle (deg) of the cell from the bow, 0 = N (-z), 90 = E"""
    return math.degrees(math.atan2(c[0], -c[2])) % 360.0


def _zones():
    ring = _nozzle_ring()
    r_ = lambda c: rho(c[0], c[2])
    d_ = lambda c: rc(c[0], c[2])

    def near_cardinal(c, tol=14.0):
        a = _compass(c)
        return min(abs(((a - k + 180) % 360) - 180) for k in (0, 90, 180, 270)) <= tol

    hub_strip = lambda c: WELL_R < d_(c) <= 10.6 and c[1] == 6 and abs(c[2]) <= 4
    rim_strip = lambda c: 25.5 < r_(c) <= 27.5 and abs(c[1]) >= 1 and near_cardinal(c) and not (abs(c[0]) >= 23 and abs(c[2]) <= 6)
    nozzle = lambda c: c in ring and abs(c[0]) >= POD0 and abs(c[2]) >= POD0
    door_frame = lambda c: abs(c[0]) == X_DOOR and BAY_Z[0] - 1 <= c[2] <= BAY_Z[1] + 1 and (c[1] in (BAY_Y[0] - 1, BAY_Y[1] + 1) or c[2] in (BAY_Z[0] - 1, BAY_Z[1] + 1))
    well = lambda c: d_(c) <= 10.6 and c[1] >= WELL_FLOOR_Y
    seams = lambda c: c[1] >= 4 and (c[0] == 0 or c[2] == 0) and d_(c) > HUB_R and r_(c) < 28
    ring_seam = lambda c: c[1] >= 4 and 22 <= r_(c) < 23
    pods = lambda c: abs(c[0]) >= POD0 and abs(c[2]) >= POD0
    return Zones(
        Zone('hub_strip', hub_strip, color=CYAN, heavy=True, kind='accent'),
        Zone('rim_strip', rim_strip, color=CYAN, kind='accent'),
        Zone('nozzle_ring', nozzle, heavy=True, color=CYAN_D, kind='accent'),
        Zone('door_frame', door_frame, heavy=True, color=CYAN_D, kind='accent'),
        Zone('well', well, heavy=True, color=SEAM, kind='dark'),
        Zone('seams', lambda c: seams(c) or ring_seam(c), color=SEAM, kind='dark'),
        Zone('pods', pods, heavy=True, color=PLATE_M, kind='plate'),
        Zone('rim_belt', lambda c: r_(c) >= 27.5, heavy=True, color=PLATE_M, kind='plate'),
        Zone('bow_guard', lambda c: c[2] <= -28 and abs(c[0]) <= 6, heavy=True, kind='armor'),
        Zone('belly', lambda c: c[1] <= -1, color=SEAM, kind='dark'),
        default_color=PLATE)


def _orient_for(sub, kind, toward):
    """(fwd, up) of the Corner / CornerInv block whose three faces `toward` are the ones that matter: Corner = the three half faces (mount area > 0) are `toward`,
    CornerInv = the three partly cut faces (area < 1) are `toward`"""
    want = frozenset(toward)
    for (f, u), area in H.orient_areas(sub, 'Large').items():
        faces = frozenset(d for d, a in area.items() if (a > 0.01 if kind == 'corner' else a < 0.99))
        if faces == want:
            return f, u
    raise AssertionError(f'no orientation of {sub} with faces {sorted(want)}')


def _plane_tiles(res, vol):
    """the diagonal sides of the octagon: every column of the 45 degree bevel (r = 25..28, |x|, |z| > OCT_C) holds, from the top, a Corner (the 1/6 tetrahedron at the cell's inner
    vertex) and under it a CornerInv (the cell minus its outer tetrahedron): together with the neighbouring columns they tile ONE flat plane x + y + z = const (the owner's
    `slopedcorner` sample); a cube under the corner piece pokes through that plane and was the jagged tooth.  The belly is the mirror image.  Where the column's lower cell
    would be the middle cell of the rim (y = 0) it is left to the hull builder (a sideways slope)."""
    bp = res.bp
    n = 0
    for x in range(-33, 34):
        for z in range(-33, 34):
            if abs(x) <= OCT_C or abs(z) <= OCT_C or (abs(x) >= POD0 - 1 and abs(z) >= POD0 - 1): continue
            r = rho(x, z)
            if not 25 <= r <= 28 or abs(x) + abs(z) - OCT_C != r: continue
            yt = PLANE_K - r
            sx, sz = (1 if x > 0 else -1), (1 if z > 0 else -1)
            for sy in (1, -1):
                for k, kind in ((0, 'corner'), (1, 'corner_inv')):
                    y = sy * (yt - k)
                    c = (x, y, z)
                    if kind == 'corner_inv' and abs(y) < 1: continue
                    old = res.cell_block.get(c)
                    if old is None: continue
                    heavy = 'Heavy' in old['sub']
                    sub = H.subtype(kind, 'Large', heavy)
                    inward = [(-sx, 0, 0), (0, -sy, 0), (0, 0, -sz)]
                    outward = [(sx, 0, 0), (0, sy, 0), (0, 0, sz)]
                    f, u = _orient_for(sub, kind, inward if kind == 'corner' else outward)
                    bp.blocks.remove(old)
                    res.blocks.remove(old)
                    b = bp.add('MyObjectBuilder_CubeBlock', sub, c, fwd=f, up=u, color=old['color'], kind=old['kind'])
                    b['mass'] = H.block_stat(b)[0]
                    res.blocks.append(b)
                    res.cell_block[c] = b
                    res.cell_kind[c] = kind
                    res.exposure[c] = frozenset(outward)
                    n += 1
    res.plane_tiles = n


def hull(bp):
    v = _volumes()
    solid = v['solid']
    reserved = {}
    reserved['rails'] = [c for r in v['rails'] for c in r]
    reserved['hangar'] = [c for r in v['hangar'] for c in r]
    reserved['doors'] = [c for r in v['doors'] for c in r]
    reserved['hconn'] = [c for rr in v['conn'] for r in rr for c in r]
    reserved['wconn'] = [c for r in v['wconn'] for c in r]
    reserved['thr'] = [c for t, r in v['thr'] for c in r]
    reserved['turrets_dn'] = [c for t, r in v['turrets'] if t['up'] == 'Down' and t['role'] == 'assault' for c in r]
    reserved['tanks'] = [c for r in v['tanks'] for c in r]
    reserved['lockers'] = [c for r in v['lockers'] for c in r]
    shell = Volume(solid.cells - solid.erode(1, 'box').cells)           # 26-neighbour skin: stays one piece along the 45 degree walls (the 6-neighbour skin splits there)
    pb = _pod_boxes()
    shell = shell | Volume(pb.cells - pb.erode(1, 'box').cells)               # each engine pod keeps its own sealed wall inside the disc (its fuel island) and joins the disc skin
    shell = shell | B((-CAB_X, 6, CAB_Z[0]), (CAB_X + 1, 7, CAB_Z[1] + 1))                 # + the cabin floor
    shell = Volume(shell.cells & solid.cells)
    win = lambda c: (c[1] in (7, 8) and ((c[2] == CAB_Z[0] and abs(c[0]) <= CAB_X - 1) or (abs(c[0]) == CAB_X and CAB_Z[0] + 1 <= c[2] <= CAB_Z[1] - 1 and (c[2] != -12 or c[1] == 8))))
    res = H.build_hull(bp, solid, fill=shell, reserved=reserved, skin_around=["hangar", "doors", "hconn", "wconn", "thr", "rails"], zones=_zones(), windows=win, round_edges=True, slope2=False, fillets=True)
    _plane_tiles(res, v)
    return res, v


def build(name='Aegis Carrier'):
    """hull only (development helper)"""
    bp = Blueprint(name, 'Large', seed=seed_for(name))
    res, v = hull(bp)
    return bp, res, v



# ------------------------------------------------------------------------------------------------------------
# systems
# ------------------------------------------------------------------------------------------------------------
HELM_DATA = L.HELM_DATA
POD_RESERVE = [Item.ammo('LargeCalibreAmmo', 100), Item.ammo('MediumCalibreAmmo', 150)]
BOW_RESERVE = [Item.ammo('LargeRailgunAmmo', 20), Item.ammo('NATO_25x184mm', 100)]


def _hangars(S):
    """side bays: per side two large Connectors in the wall |x| = 26 (contact face outward) and eight AirtightHangarDoors at |x| = 31;
    the well: two floor Connectors (contact face Up) under each of the four well pods"""
    S.conns, S.doors = {}, {}
    for s, sn in ((-1, 'P'), (1, 'S')):
        face = 'Right' if s > 0 else 'Left'
        S.conns[s] = [S.fixed('conn', 'MyObjectBuilder_ShipConnector', 'Connector', (s * X_CONN, 0, z), face, 'Forward', nm(f'Bay {sn} Connector {"AB"[i]}'), GUND, kind='conn')
                      for i, z in enumerate(CONN_Z)]
        S.doors[s] = [S.fixed('door', 'MyObjectBuilder_AirtightHangarDoor', '', (s * X_DOOR, BAY_Y[0], z), face, 'Up', nm(f'Bay {sn} Door {i + 1}'), GUND, kind='door')
                      for i, z in enumerate(range(BAY_Z[0], BAY_Z[1] + 1))]
    for k, x in enumerate(WELL_X):
        S.conns[f'W{k + 1}'] = [S.fixed('conn', 'MyObjectBuilder_ShipConnector', 'Connector', (x, WELL_FLOOR_Y, z), 'Up', 'Forward', nm(f'Well Connector {k + 1}{"AB"[i]}'), SEAM, kind='conn')
                                for i, z in enumerate(CONN_Z)]


def _weapons(S):
    bp = S.bp
    for i, x in enumerate(RAIL_X):
        b = S.fixed('rail', 'MyObjectBuilder_SmallMissileLauncherReload', 'LargeRailgun', (x, 0, RAIL_Z[0]), 'Forward', 'Up', nm(f'[B1] Railgun {i + 1}'), GUNC, kind='rail')
        bp.preload(b, items=[Item.ammo('LargeRailgunAmmo', 6)], state={'capacitor': 'full'})
        bp.keep_clear(b, 3, 'muzzle')
    spec = {'art': ('MyObjectBuilder_LargeMissileTurret', 'LargeCalibreTurret', 'Artillery Turret'),
            'assault': ('MyObjectBuilder_LargeMissileTurret', 'LargeBlockMediumCalibreTurret', 'Assault Turret'),
            'pd': ('MyObjectBuilder_LargeGatlingTurret', '', '[PD] Gatling')}
    counters = {}
    S.tmounts = []
    for t in turret_sites():
        xt, sub, label = spec[t['role']]
        counters[t['role']] = counters.get(t['role'], 0) + 1
        kw = dict(settings=dict(PD_SETTINGS)) if t['role'] == 'pd' else {}
        b = S.fixed(t['role'], xt, sub, t['mn'], 'Forward', t['up'], nm(f'{label} {counters[t["role"]]} {t["where"]}'), GUNC, kind='turret', **kw)
        bp.fill_ammo(b)


def _propulsion(S):
    bp = S.bp
    for t in thruster_sites():
        up = 'Up' if t['exhaust'] in ('Forward', 'Backward', 'Left', 'Right') else 'Forward'
        b = S.fixed('thr_' + t['group'], 'MyObjectBuilder_Thrust', 'LargeBlockLargeHydrogenThrust', t['mn'], t['exhaust'], up, nm(f'Thruster {t["name"]}'), GUND, kind='thr')
        bp.keep_clear(b, sebp.LARGE_H2_LANE, 'exhaust', face=True)
    # the pod tanks and ammo lockers (each pod is its own fuel island)
    for (sx, sz), pn in POD_NAMES.items():
        mn, _ = _pod_cell(sx, sz, LOCAL['tank'])
        S.add('tank', 'MyObjectBuilder_OxygenTank', 'LargeHydrogenTank', mn, 'Forward', 'Up', nm(f'Hydrogen Tank {pn}'), toward=None, kind='tank', state={'filled': 'full'})
        mn, _ = _pod_cell(sx, sz, LOCAL['locker'])
        S.add('cargo', 'MyObjectBuilder_CargoContainer', 'LargeBlockLargeContainer', mn, 'Forward', 'Up', nm(f'Ammo Locker {pn}'), toward=None, kind='cargo', items=POD_RESERVE)


GYRO_BANKS = [(10, 10), (-15, 10), (10, -15), (-15, -15)]       # 6 x 6 each on the belly skin (y = -3), 144 in all


def _interior(S):
    """power, gyros and the bow locker, in the hollow disc on the belly skin"""
    bp = S.bp
    S.add('reactor', 'MyObjectBuilder_Reactor', 'LargeBlockLargeGenerator', (-1, -3, -1), 'Forward', 'Up', nm('Reactor Large'), toward='Down', kind='reactor', items=[Item.ingot('Uranium', 500)])
    n = 0
    for x in (-4, 4):
        for z in (-2, 1):
            n += 1
            S.add('reactor', 'MyObjectBuilder_Reactor', 'LargeBlockSmallGenerator', (x, -3, z), 'Forward', 'Up', nm(f'Reactor {n}'), toward='Down', kind='reactor', items=[Item.ingot('Uranium', 100)])
    n = 0
    for x in (-6, -5, 5, 6):
        for z in (-1, 0, 1):
            n += 1
            S.add('bat', 'MyObjectBuilder_BatteryBlock', 'LargeBlockBatteryBlock', (x, -3, z), 'Forward', 'Up', nm(f'Battery {n}'), extra=bat_extra(3), kind='bat', toward='Down', state={'stored_power': 'full'})
    n = 0
    for x0, z0 in GYRO_BANKS:
        for x in range(x0, x0 + 6):
            for z in range(z0, z0 + 6):
                n += 1
                S.add('gyro', 'MyObjectBuilder_Gyro', 'LargeBlockGyro', (x, -3, z), 'Forward', 'Up', nm(f'Gyroscope {n}'), toward='Down', kind='gyro')
    S.add('cargo_bow', 'MyObjectBuilder_CargoContainer', 'LargeBlockLargeContainer', (-1, -3, -21), 'Forward', 'Up', nm('Ammo Locker Bow'), toward='Down', kind='cargo', items=BOW_RESERVE)


def _bridge(S):
    """the bridge cabin on the hub (interior x -2..2, y 7..8, z -13..-11): two seats, Remote Control, Helm + Staff PB, timer, event controllers, AI blocks"""
    bp = S.bp
    cd = f'[Ship]\nTag={TAG}\n'
    helm_cd = cd + HELM_DATA
    hangars = ''.join(f'\n[Hangar {k}]\n' + (f'Group=[Hangar {k}]\n' if k <= 2 else '') + f'Pod=[Pod] Pod PB {k}\n' for k in range(1, 7))
    staff_cd = (cd + '\n[Staff]\nBlock Prefix=[SH]\nCockpit Surface=1\nHelm HUD Surface=0\nWing Surface=2\nTurret Target Group=Weapons\nTurret Target Names=Artillery,Assault\nPod Gap (s)=6\n'
                '\n[Wing]\nTypes=HORNET,WASP,HARRIER,LANCER,AEGIS,WATCHER\n' + hangars +
                '\n[Harasser Defence]\nEnabled=true\nMax Fire Range (m)=1700\nJink Speed (m/s)=10\nAway Speed (m/s)=15\n')
    y = 7
    S.cockpit = S.add('cockpit', 'MyObjectBuilder_Cockpit', 'LargeBlockCockpitSeat', (0, y, -13), 'Forward', 'Up', nm('Bridge Cockpit'), toward='Down', kind='cockpit', settings={'IsMainCockpit': True})
    S.gunner = S.add('cockpit', 'MyObjectBuilder_Cockpit', 'LargeBlockCockpitSeat', (2, y, -13), 'Forward', 'Up', nm('Gunnery Seat'), toward='Down', kind='cockpit')
    S.add('rc', 'MyObjectBuilder_RemoteControl', 'LargeBlockRemoteControl', (-2, y, -13), 'Forward', 'Up', nm('Remote Control'), toward='Down', kind='ctrl')
    S.helm = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (-3, y, -11), 'Backward', 'Up', '[Helm][SH] Aegis Helm', toward='Down', kind='ctrl',
                   custom_data=helm_cd, extra=L._pb(HELM_SCRIPT))
    S.staff = S.add('pb', 'MyObjectBuilder_MyProgrammableBlock', 'LargeProgrammableBlock', (3, y, -11), 'Backward', 'Up', '[Staff][SH] Aegis Staff', toward='Down', kind='ctrl',
                    custom_data=staff_cd, extra=L._pb(STAFF_SCRIPT))
    S.timer = S.add('timer', 'MyObjectBuilder_TimerBlock', 'TimerBlockLarge', (0, y, -11), 'Forward', 'Up', nm('Timer D (deadman)'), toward='Down', kind='timer', settings={'Delay': 5000, 'Silent': True})
    S.ec_cockpit = S.add('ec', 'MyObjectBuilder_EventControllerBlock', 'EventControllerLarge', (-2, y, -11), 'Forward', 'Up', nm('EC Cockpit'), toward='Down', kind='timer')
    S.ec_core = S.add('ec', 'MyObjectBuilder_EventControllerBlock', 'EventControllerLarge', (2, y, -11), 'Forward', 'Up', nm('EC Core'), toward='Down', kind='timer')
    S.ec_power = S.add('ec', 'MyObjectBuilder_EventControllerBlock', 'EventControllerLarge', (0, y + 1, -11), 'Forward', 'Up', nm('EC Power'), toward='Down', kind='timer')
    S.add('ai', 'MyObjectBuilder_OffensiveCombatBlock', 'LargeOffensiveCombat', (-3, y, -13), 'Forward', 'Up', nm('AI Offensive [AI]'), toward='Down', kind='ai')
    S.add('ai', 'MyObjectBuilder_FlightMovementBlock', 'LargeFlightMovement', (3, y, -13), 'Forward', 'Up', nm('AI Flight [AI]'), toward='Down', kind='ai')


def _entry(S, res, vol):
    """a slide door in each side wall of the cabin (x = +-4, z = -12), reached from the hub top; the gravity generator stands on the centre line of the walkway row"""
    env = set(vol['solid'].cells)
    cells = [(sx * CAB_X, 7, -12) for sx in (-1, 1)]
    C.carve_walkway(S, res, cells, doors={cells[0]: 'Entry P', cells[1]: 'Entry S'}, name='Aegis walkway', namer=nm)
    C.place_gravity(S, res, env, (0, 7, -12), (20.0, 10.0, 15.0), nm('Gravity Generator'))
    for sx in (-1, 1):
        inside = [(sx * k, 7, -12) for k in range(1, CAB_X)] + [(sx, 7, -13), (sx, 7, -11)]
        C.walk_check(S, (sx * (CAB_X + 1), 7, -12), [(sx, 7, -12), (sx, 7, -13), (sx, 7, -11)], [(sx * CAB_X, 7, -12), (sx * (CAB_X + 1), 7, -12)] + inside, 'Aegis walkway')


def _flat_cam(S, look, near, up='Up'):
    """a camera mounts through its back face only: stand it against a hull cell that is a plain armour cube with exactly one exposed face, `look` (the rim is stepped, most
    rim cells are slopes), the nearest to the cell `near`"""
    d = DIR[look]
    best = None
    for c, b in S.res.cell_block.items():
        if S.res.cell_kind.get(c) != 'block' or S.res.exposure.get(c) != frozenset({d}) or not b['sub'].endswith('ArmorBlock'): continue
        m = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
        if m in S.occ or m in S.bp.reserve: continue
        k = sum(abs(m[i] - near[i]) * (1 if i != 1 else 3) for i in range(3))
        if best is None or (k, m) < best: best = (k, m)
    assert best, f'no flat hull face for a camera looking {look} near {near}'
    return best[1]


def _sensing(S):
    """six hull cameras in mirrored pairs on the flat outer faces of the engine pods (Front P/S, Rear P/S, Port, Starboard), plus Top and Belly on the centre line"""
    bp = S.bp
    for tag, look, near, up in (('Front', 'Forward', (22, 0, -29), 'Up'), ('Rear', 'Backward', (22, 0, 29), 'Up'), ('Starboard', 'Right', (29, 0, -22), 'Up')):
        c = _flat_cam(S, look, near)
        mirror = (-c[0], c[1], c[2])
        assert mirror not in S.occ and mirror not in bp.reserve, f'camera {tag}: no mirrored cell'
        if look == 'Right':
            S.put('cam', C.cam(bp, mirror, 'Left', up, nm('Camera Port'), 3))
            S.put('cam', C.cam(bp, c, look, up, nm('Camera Starboard'), 3))
        else:
            S.put('cam', C.cam(bp, mirror, look, up, nm(f'Camera {tag} P'), 3))
            S.put('cam', C.cam(bp, c, look, up, nm(f'Camera {tag} S'), 3))
    for tag, mn, look, up in (('Top', (0, 9, 12), 'Up', 'Forward'), ('Belly', (0, -5, 18), 'Down', 'Forward')):
        S.put('cam', C.cam(bp, mn, look, up, nm(f'Camera {tag}'), 3))
    S.add('ant', 'MyObjectBuilder_RadioAntenna', 'LargeBlockCompactRadioAntenna', (0, 9, 11), 'Up', 'Backward', nm('Antenna'), extra=ant_extra(20000), kind='ant', toward='Down')
    d = DIR['Down']
    for tag, sgn in (('Lane P', -1), ('Lane S', 1)):
        cands = sorted(((abs(c[0] - sgn * 24) + abs(c[2]) + 3 * abs(c[1] + 3), c) for c, b in S.res.cell_block.items()
                        if S.res.cell_kind.get(c) == 'block' and S.res.exposure.get(c) == frozenset({d}) and b['sub'].endswith('ArmorBlock') and c[0] * sgn > 0))
        for k, c in cands[:60]:
            m = (c[0], c[1] - 1, c[2])
            if m in S.occ or m in bp.reserve: continue
            try:
                blk = orient_attached(bp, 'MyObjectBuilder_SensorBlock', 'LargeBlockSensor', m, toward='Up', prefer=(('Forward', 'Up'),), occ=S.occ, name=nm(f'Sensor {tag}'), color=GUND, kind='sensor')
            except AssertionError:
                continue
            S.put('sensor', blk)
            break
        else:
            raise AssertionError(f'no cell for sensor {tag}')


DECOY_SEEDS = [(8, 4, -24), (24, 3, -12), (12, 4, 24), (30, 0, 14), (26, -3, 26), (10, -4, 4)]


def _greebles(res):
    """no surface studs or panels: the owner wants a clean sloped hull without cubes standing proud of it (seams are paint, see _zones)"""
    return []


def _terminals(S):
    """the junction cells, per engine pod: L._terminals without the bow locker (it is not on any network)"""
    return L._terminals(S)


def _pod_of(c):
    return POD_NAMES[(1 if c[0] > 0 else -1, 1 if c[2] > 0 else -1)]


def _networks(S):
    """four independent junction networks, one per engine pod (each pod is a fuel island: damage isolation, ~25 junctions instead of ~300 over 60 m of disc)"""
    terms = _terminals(S)
    first = 1
    for (sx, sz), pn in POD_NAMES.items():
        mine = [t for t in terms if abs(t[0][0]) >= POD0 and abs(t[0][2]) >= POD0 and _pod_of(t[0]) == pn]
        assert mine, f'no terminals in pod {pn}'
        before = len(S.conveyors) if hasattr(S, 'conveyors') else 0
        L._conveyors(S, terms=mine, first=first, toward=(sx * 26, 0, sz * 26))
        first += len(S.conveyors) - before
    return terms


def _groups_and_toolbars(S):
    bp = S.bp
    R = S.r
    bp.group('B1', R['rail'])
    bp.group('Artillery', R['art'])
    bp.group('Assault', R['assault'])
    bp.group('PD', R['pd'])
    bp.group('Main Turrets', R['art'] + R['assault'])
    bp.group('Turrets', R['art'] + R['assault'] + R['pd'])
    thr = [b for k in R if k.startswith('thr_') for b in R[k]]
    bp.group('Thrusters', thr)
    bp.group('Weapons', R['rail'] + R['art'] + R['assault'] + R['pd'])
    bp.group('Decoys', R['decoy'])
    bp.group('[Hangar 1]', S.doors[-1])
    bp.group('[Hangar 2]', S.doors[1])
    bp.group('Gyros', R['gyro']); bp.group('Reactors', R['reactor']); bp.group('Batteries', R['bat']); bp.group('Tanks', R['tank'])
    bp.set_toolbars(S.timer, toolbar=[TB.group('Thrusters', 'OnOff_Off'), TB.group('Weapons', 'OnOff_Off')])
    cock = [TB.group('B1', 'Shoot'), TB.group('Main Turrets', 'OnOff'), TB.group('PD', 'OnOff'), TB.group('Decoys', 'OnOff'), TB.group('[Hangar 1]', 'Open_On'), TB.group('[Hangar 1]', 'Open_Off'),
            TB.run(S.helm), TB.run(S.staff), TB.run(S.staff, 'deploy', experimental=True), TB.run(S.staff, 'dock', experimental=True)]
    bp.set_toolbars(S.cockpit, toolbar=cock)
    gun = [TB.group('B1', 'Shoot'), TB.group('Artillery', 'OnOff'), TB.group('Assault', 'OnOff'), TB.group('PD', 'OnOff'), TB.group('[Hangar 2]', 'Open_On'), TB.group('[Hangar 2]', 'Open_Off'),
           TB.run(S.helm), TB.run(S.staff), TB.run(S.staff, 'deploy', experimental=True), TB.run(S.staff, 'dock', experimental=True)]
    bp.set_toolbars(S.gunner, toolbar=gun)
    bp.set_toolbars(S.ec_core, toolbar=[TB.block(S.staff, 'Run')])
    S.ec_core['settings'] = TB.event_settings('integrity', 0.4, invert=True)
    S.ec_core['links'] = {'selected': R['reactor']}


# ------------------------------------------------------------------------------------------------------------
# pods
# ------------------------------------------------------------------------------------------------------------
def _dock_well(bp, pod, conns, out_sign, lock=False):
    """dock `pod` nose up on two floor connectors; of the pairings and rolls that dock, keep the one whose pod Up points to (out_sign, 0, 0) (away from the well axis)"""
    errs = []
    for hc in (list(conns), list(conns)[::-1]):
        for roll in range(4):
            try:
                pose = pods.dock_pod(bp, pod, hc, want_nose='Up', roll=roll, lock=False)
            except AssertionError as e:
                errs.append(str(e)[-80:])
                continue
            if MU._dot(pose.rot.apply(MU.DIRS['Up']), (out_sign, 0, 0)) > 0.99:
                if lock:
                    bp.lock_connectors(hc[0], pod, pod.pod.connectors[0])
                    bp.lock_connectors(hc[1], pod, pod.pod.connectors[1])
                return pose
            bp.subgrids.pop()
            errs.append('wrong way round')
    raise AssertionError('no nose-up docking pose: ' + '; '.join(errs))


def check_well(bp, pod, min_gap=0.3):
    """pod `pod` stands in the well: no ship block within `min_gap` metres of a pod cell (the connector planes meet at the lock gap), the lane above the pod (12 m) is empty, nose up"""
    bp.grids()
    ship = L._world_boxes(bp)
    root = pod._world
    bad = []
    top = -1e9
    foot = [1e9, -1e9, 1e9, -1e9]
    pod_boxes = []
    for b in pod.blocks:
        for c in pod.cells(b):
            ctr = root.apply(tuple(v * 0.5 for v in c))
            pod_boxes.append((tuple(ctr[i] - 0.25 for i in range(3)), tuple(ctr[i] + 0.25 for i in range(3)), b, c[2]))
    for lo, hi, pb, pz in pod_boxes:
        need = pods.PAIR_GAP - 0.005 if pz >= pods.CONN_Z + 1 else min_gap
        top = max(top, hi[1])
        foot = [min(foot[0], lo[0]), max(foot[1], hi[0]), min(foot[2], lo[2]), max(foot[3], hi[2])]
        for slo, shi, sb in ship:
            gap = max(max(slo[i] - hi[i], lo[i] - shi[i], 0.0) for i in range(3))
            if gap < need and not (pb['kind'] == 'conn' and sb['kind'] == 'conn'): bad.append((pb['name'] or pb['sub'], sb['name'] or sb['sub'], round(gap, 3)))
    assert not bad, f'{bp.name}: well pod too close to the hull: {bad[:4]}'
    lane_lo, lane_hi = (foot[0], top, foot[2]), (foot[1], top + 12.0, foot[3])
    inside = [(sb['name'] or sb['sub']) for slo, shi, sb in ship if all(slo[i] < lane_hi[i] - 1e-6 and shi[i] > lane_lo[i] + 1e-6 for i in range(3))]
    assert not inside, f'{bp.name}: blocks in the launch lane above a well pod: {inside[:4]}'
    nose = pod.pose.rot.apply(MU.DIRS['Forward'])
    assert MU._dot(nose, (0, 1, 0)) > 0.99, f'the well pod nose does not point up (nose {nose})'
    return True


def aegis_carrier(lite=False, with_pods=True, lock=True, name=None, wing=0):
    assert not lite, 'the Lite variants are retired'
    name = name or 'Aegis Carrier'
    bp = Blueprint(name, 'Large', seed=seed_for(name))
    res, vol = hull(bp)
    S = L.Ship(bp, res, vol)
    _hangars(S)
    _weapons(S)
    _propulsion(S)
    _interior(S)
    _bridge(S)
    _sensing(S)
    _entry(S, res, vol)
    L._decoys(S, res, seeds=DECOY_SEEDS)
    terms = _networks(S)
    _greebles(res)
    res.check()
    C.link_conveyors(bp, [(c, n) for c, r, n in terms])
    _groups_and_toolbars(S)
    S.tu_check = {}
    bp.ship = S
    bp.hull = res
    bp.pods_ = []
    if with_pods:
        sides = ((-1, 'POD1', 'Port', 'Left'), (1, 'POD2', 'Starboard', 'Right'))
        for k, (s, tag, where, nose) in enumerate(sides):
            pod = pods.cradle_pod(4, name=f'{name} {where} Cradle Pod', tag=tag, ship_tag=TAG, first_bay=1 + 4 * k)
            pod.display = f'{name} {where} Cradle Pod'
            pod.pod.pb['name'] = f'[Pod] Pod PB {k + 1}'
            if wing: pods.load_hornets(pod, wing)
            L._dock_upright(bp, pod, S.conns[s], nose, lock)
            L.check_hangar(bp, pod, s, door_x=(X_DOOR + 1) * 2.5, hangar_y=BAY_Y, hangar_z=BAY_Z)
            bp.pods_.append(pod)
        for j, x in enumerate(WELL_X):
            k = j + 2
            pod = pods.cradle_pod(4, name=f'{name} Well {j + 1} Cradle Pod', tag=f'POD{k + 1}', ship_tag=TAG, first_bay=1 + 4 * k)
            pod.display = f'{name} Well {j + 1} Cradle Pod'
            pod.pod.pb['name'] = f'[Pod] Pod PB {k + 1}'
            if wing: pods.load_hornets(pod, wing)
            _dock_well(bp, pod, S.conns[f'W{j + 1}'], 1 if x > 0 else -1, lock)
            check_well(bp, pod)
            bp.pods_.append(pod)
    pods.assert_clean(bp)
    return C.finalize(bp)


def numbers(bp):
    """the figures of the design record (leviathan.numbers) plus the per-axis thrust check"""
    n = L.numbers(bp)
    n['thrusters_per_direction'] = {}
    for b in bp.blocks:
        if b['xtype'] == 'MyObjectBuilder_Thrust':
            d = OPP[b['fwd']]
            n['thrusters_per_direction'][d] = n['thrusters_per_direction'].get(d, 0) + 1
    return n


_PCU_LABELS = {'Thrusters (14 large hydrogen)': 'Thrusters (24 large hydrogen)', 'Reactors (1 large + 8 small)': 'Reactors (1 large + 4 small)'}


def pcu_table(bp):
    rows = {}
    for c, k, p in L.pcu_table(bp):
        c = _PCU_LABELS.get(c, c)
        c = 'Ammunition lockers' if c == 'cargo_bow' else c
        n0, p0 = rows.get(c, (0, 0))
        rows[c] = (n0 + k, p0 + p)
    return [(c, k, p) for c, (k, p) in rows.items()]


def deck_plan(bp, y, xr=range(-32, 33)):
    return L.deck_plan(bp, y, xr=xr)


DOCS = {'Aegis Carrier': 'LARGE-grid MEGA CARRIER modelled on the player mothership of the game Starscape (157.5 x 35 x 157.5 m, about 15 kt): a flat rounded-square disc with a raised hub ring around a '
                         'circular launch well, a glazed bridge cabin on the hub, flat bay doors east and west and four boxy engine / weapon pods at the corners (six large hydrogen thrusters, a tank, an ammo '
                         'locker, an artillery turret and an assault turret each: 24 thrusters, 1.9 m/s2 on all six axes). Two bow railguns (battery [B1]), 4 artillery, 4 assault and 16 gatling '
                         'turrets for point defence, 12 decoys, 144 gyros, one large and four small reactors, 12 batteries, Helm and Staff programmable blocks, bridge cockpit, gunnery seat and Remote Control. '
                         'SIX docked, empty 4-slot Cradle Pods (own CubeGrids and Pod PBs, 24 drone slots): four stand nose-up in the open well and launch upward, one sits in each side bay. '
                         'Ammunition, uranium, hydrogen and batteries are pre-loaded; four independent junction networks (one per engine pod) feed the engines and turrets. '
                         'Limits: the rails have no feed (6 slugs each, reload by hand from the bow locker), the pods carry no drones, about 40,000 PCU with the empty pods (64,000 with 24 Hornets: a world '
                         'limit of 50 k or more, the design record lists what to drop), the cyan strips are paint (no glowing blocks), and nothing of this has been pasted in the game yet.'}
MODEL_NOTES = {'Aegis Carrier': ('Large grid, paste only (delete the old blueprint folder first). About 40,000 PCU with the six empty pods: needs a world limit of 50 k or more, and about 64,000 with 24 Hornets '
                                 '(docs/design/ships/Aegis_Carrier.md lists the trimming steps). The Helm, Staff and Pod scripts are embedded in the PBs; flyable by hand from the bridge cockpit. '
                                 'Each pod is a separate grid docked on two connector pairs (side bays and the floor of the central well): press Lock on the connectors if a pod did not lock. '
                                 "Drones are added by pasting them against the pods' merge blocks; `Run Staff launch all` releases them. This is the carrier, not the Aegis escort drone.")}
MODELS = {'Aegis Carrier': aegis_carrier}
NO_LITE = set(MODELS)


if __name__ == '__main__':
    import argparse, json
    ap = argparse.ArgumentParser()
    ap.add_argument('--render', default=None, help='write the renders into this folder')
    ap.add_argument('--hull', action='store_true', help='hull only (development)')
    ap.add_argument('--pcu', action='store_true', help='print the PCU table and exit')
    ap.add_argument('--plans', action='store_true', help='print the deck plans and exit')
    a = ap.parse_args()
    t0 = time.time()
    if a.hull:
        bp, res, v = build()
        print('blocks', len(bp.blocks), round(time.time() - t0, 1), 's', dict(res.counts), 'symmetry error', v['solid'].symmetry_error('x', 0.5))
        if a.render:
            import _shiprender as RR
            print(RR.render_sheet(bp, a.render, 'hull'))
        sys.exit(0)
    bp = aegis_carrier()
    if a.pcu:
        tot = 0
        for cat, k, p_ in pcu_table(bp):
            print(f'{cat:55s} {k:6d} {p_:8d}'); tot += p_
        print(f'{"ship":55s} {len(bp.blocks):6d} {tot:8d}')
        for p in bp.pods_: print(p.name, sum(C._stat(b).get('pcu') or 0 for b in p.blocks))
        sys.exit(0)
    print(json.dumps(numbers(bp), indent=1, default=str))
    print('built', len(bp.blocks), 'blocks,', len(bp.pods_), 'pods', round(time.time() - t0, 1), 's')
    if a.render:
        import _shiprender as RR
        print(RR.render_sheet(bp, a.render, 'aegis'))
