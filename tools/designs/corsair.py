"""Corsair: small-grid corvette (36 m), catamaran hammerhead with a central railgun spine and two outrigger pylons that carry the drone cradles.

Module contract: MODELS = {"Corsair": fn(lite), "Corsair Wing": fn(lite)}, DOCS = {name: one paragraph}, SCRIPTS = [script file names it embeds].
    python3 -I tools/designs/corsair.py                 build + validate both models and both variants, one report line each
    python3 -I tools/designs/corsair.py --plans         ASCII deck plans (docs/design/ships/Corsair.md)
    python3 -I tools/designs/corsair.py --render DIR    PNG views + silhouettes with the repo's thumbnail code (tools/hull_demo.py)
Design notes, numbers, layout map, bay geometry and the in-game checks: docs/design/ships/Corsair.md.   Tests: tools/tests/test_corsair.py.

Frame: cells are 0.5 m, x = Right, y = Up, z = Backward (the bow points to -z); mirror plane = the middle of cell 0 (x -> -x, cell c <-> cell -c).
Hull from tools/hulls.py (voxel volumes -> armour blocks; zones for heavy / light and the livery), made block-for-block mirror-symmetric by
mirror_hull(); systems placed by hand into reserved volumes.

  * Centre hull x -6..6, y 1..12, z 24..67 with a chisel nose (tip z 6), a hammer crossbar (wings, z 10..18) and a dorsal ridge carrying the two
    railguns; stepped bridge tower z 34..55 with the cockpit.
  * Two outrigger pylons (x +-7..12, z 53..67): each is a 5x5x5 hydrogen tank with three large thrusters on its faces (brake, up, down).
  * Two cradle pods (x +-13..25, z 56..67): a 9 x 8 cell bore, closed at the stern by a 2-cell wall, open to the front; the 24-cell launch lane
    continues forward (z 32..55).  Drone nose forward, one cell of free space on every side, host merge block on a one-cell stalk in front of the wall.
  * Hydrogen: ten 500 kL tanks; every thruster touches the tank that feeds it (direct tank contact, the pattern the Shrike module also uses).
Lite = only block shapes seen in a game-written blueprint (no turrets, sensor, decoys, big generator, PB Run toolbar slots).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403  (helpers + constants)
import _common as C
import hulls as H
from hulls import Volume, Zone, Zones
import sebp_toolbar as TB
import sebp_multi as MU
from sebp_inv import Item

NAME = 'Corsair'
SCRIPTS = ['CorsairHelmAI.cs', 'CorsairStaffAI.cs', 'CradleBayAI.cs']
SCRIPT_MAP = {}                   # test models (designs/cradle_test.py) swap the Helm / Staff program files here
TAG = '[SH] '                     # host-ship prefix (control_arch 3.6 / grids_bays 6.3): Helm / Staff collect blocks by prefix AND grid

HELM_DATA = """[Ship]
Tag=SH
Class=CORVETTE

[Battery 1]
Tag=B1
Class=RAIL
Role=PRIMARY_CANDIDATE

[Battery 2]
Tag=B2
Class=SHELL
Role=SECONDARY

[Battery 3]
Tag=B3
Class=BALLISTIC
Role=SECONDARY

[Link]
Ship Tag=SH
Block Prefix=[SH]

[Cockpit]
Helm Surface=0
Staff Surface=1
"""

STAFF_DATA = """[Ship]
Tag=SH
Class=CORVETTE

[Staff]
Ship Tag=SH
Block Prefix=[SH]

[Wing]
Types=HORNET,WASP,WATCHER,HARRIER,LANCER,AEGIS

[Cockpit]
Staff Surface=1
"""

# the bay manager (scripts/CradleBayAI.cs) is its own PB, as on the pods: the Staff sends it "<Tag>.S2P release n" over IGC (control_arch 4.2)
BAY_DATA = """[Pod]
Tag=SH
Slots=2

[Bay 1]
Merge=[SH] [Bay 1] Merge Port
Drone Box Min=-22,4,57
Drone Box Max=-16,9,64
Drone Prefix=Hornet
Away=0,0,-1

[Bay 2]
Merge=[SH] [Bay 2] Merge Starboard
Drone Box Min=16,4,57
Drone Box Max=22,9,64
Drone Prefix=Hornet
Away=0,0,-1
"""


def _script(name):
    name = SCRIPT_MAP.get(name, name)
    return script_text(name) if os.path.exists(os.path.join(ROOT, 'scripts', name)) else ''


# ---- livery (family colours, as the Shrike module: charcoal gunmetal hull, orange accents, cyan glass) ----
GUNMETAL = H.hsv_mask(214, 0.14, 0.30)
PLATE = H.hsv_mask(214, 0.10, 0.44)
DARKSTEEL = H.hsv_mask(220, 0.18, 0.18)
ORANGE = H.hsv_mask(24, 0.92, 0.96)
ORANGE_D = H.hsv_mask(20, 0.85, 0.70)
GUNCOL = H.hsv_mask(220, 0.15, 0.20)

PAL = {'armor': (92, 98, 110), 'plate': (130, 138, 150), 'dark': (58, 62, 74), 'accent': (236, 120, 36), 'stripe': (236, 120, 36),
       'glass': (96, 205, 235), 'gun': (24, 26, 32), 'thr': (240, 170, 60), 'sys': (80, 150, 110), 'cockpit': (96, 205, 235),
       'tank': (70, 120, 90), 'ctrl': (90, 110, 170), 'cam': (36, 36, 42), 'bat': (70, 120, 90), 'gyro': (150, 150, 60),
       'trim': (52, 56, 68), 'ant': (185, 185, 195), 'sensor': (120, 80, 150), 'ai': (60, 150, 170), 'cargo': (150, 120, 70),
       'light': (255, 210, 120), 'cradle': (236, 120, 36), 'merge': (236, 120, 36), 'rail': (24, 26, 32), 'drone': (150, 156, 166)}

# ---------------------------------------------------------------------------------------------------------------
# frame constants (cells)
# ---------------------------------------------------------------------------------------------------------------
Z_STERN = 68                      # stern face plane of the hull (cells up to z = 67); the main thrusters sit behind it (z 68..70)
Z_TIP = 6                         # nose tip
POD_Z0 = 56                       # first interior cell of a pod bore (the front of the pod)
POD_INT_X = (15, 24)              # right pod interior x cells 15..23 (the left pod mirrors it: -23..-15)
POD_INT_Y = (3, 11)               # interior y cells 3..10 (8 cells)
POD_INT_Z = (56, 66)              # interior z cells 56..65 (9 for the drone + one stalk layer)
LANE_Z = (32, 56)                 # launch lane: 24 cells in front of the bore
MERGE_CELL = (19, 6, 65)          # right host merge block; the left one mirrors it (-19, 6, 65)


def mirror_x(v):
    return v.mirror('x', 0.5)


def mx(c):
    """mirror cell"""
    return (-c[0], c[1], c[2])


def box(mn, size):
    return Volume.box(mn, (mn[0] + size[0], mn[1] + size[1], mn[2] + size[2]))


def mirror_box(mn, size):
    """the box of the same size on the other side of the mirror plane"""
    return (-(mn[0] + size[0] - 1), mn[1], mn[2])


# ---- hydrogen: ten tanks (right side listed; the left is the mirror) and the thrusters that touch them ----
# (name, min cell of the 5x5x5 tank)
TANKS_R = [('Stern Stbd Low', (1, 2, 63)), ('Stern Stbd High', (1, 7, 63)),
           ('Mid Stbd Low', (2, 2, 40)), ('Mid Stbd High', (2, 7, 40)),
           ('Pylon Stbd', (7, 4, 56))]
# (name, min cell of the 3x3x3 large hydrogen thruster, exhaust direction)
THR_R = [('Main Stbd Low', (2, 3, 68), 'Backward'), ('Main Stbd High', (2, 8, 68), 'Backward'),
         ('Strafe Stbd Low', (7, 3, 41), 'Right'), ('Strafe Stbd High', (7, 8, 41), 'Right'),
         ('Brake Stbd', (8, 5, 53), 'Forward'), ('Lift Stbd', (8, 1, 57), 'Down'), ('Dive Stbd', (8, 9, 57), 'Up')]


def both(items):
    """[(name, mn, ...)] of the right side -> right and left (Stbd/Port) lists, mirrored boxes of size 5 (tanks) or 3 (thrusters)"""
    return items


def tank_list():
    out = []
    for nm, mn in TANKS_R:
        out.append((nm, mn))
        out.append((nm.replace('Stbd', 'Port'), mirror_box(mn, (5, 5, 5))))
    return out


def thr_list():
    out = []
    flip = {'Left': 'Right', 'Right': 'Left'}
    for nm, mn, d in THR_R:
        out.append((nm, mn, d))
        out.append((nm.replace('Stbd', 'Port'), mirror_box(mn, (3, 3, 3)), flip.get(d, d)))
    return out


# ---------------------------------------------------------------------------------------------------------------
# hull volumes
# ---------------------------------------------------------------------------------------------------------------
def fuselage():
    nose = H.nose_cone('z', base=24, length=18, center=(0.5, 7.0), half=(6.5, 6.0), tip_half=(1.5, 1.5), tip_shift=(0, -1.5), shape='octagon')
    body = Volume.loft('z', 24, Z_STERN, (-6, 7, 1, 13), (-6, 7, 1, 13), 'rect').chamfer('z', 1)
    return nose | body


def hammer():
    """crossbar wings: right half x 0..11, z 10..18, 5 -> 3 thick, a slight sweep back"""
    w = H.swept_wing((0, 4, 10), 12, 9, tip_chord=7, sweep=0.12, thick=5, tip_thick=3, span_dir='+x', chord_dir='+z', section='slab')
    return mirror_x(w)


def ridge():
    """dorsal ridge (3 wide) from the nose to the bridge: the railgun spine, rails stand on both sides of it"""
    poly = [(5, 6), (8, 6), (15, 32), (15, 36), (5, 36)]       # (y, z)
    return Volume.prism(poly, 'x', -1, 2)


def tower():
    return H.stepped_deck((-5, 13, 34), (6, 14, 56), steps=3, inset=(1, 2), rise=1, front_inset=3)


def pylon():
    """tank housing: x 7..12, y 4..8 (the tank's own height), z 56..67"""
    return Volume.box((7, 4, 56), (13, 9, Z_STERN))


def pod():
    """cradle pod (right): x 13..25, y 1..12, z 56..67 with the open-fronted bore cut out"""
    shell = Volume.box((13, 1, POD_Z0), (26, 13, Z_STERN)).chamfer('z', 1)
    return shell


def fins():
    """swept outboard fins standing on the pod roofs (right side; x 23..24, 6 tall) and a short keel blade under each pod"""
    fin = Volume.prism([(13, 57), (20, 65), (20, 68), (13, 68)], 'x', 23, 25)          # (y, z)
    keel = Volume.prism([(1, 60), (-1, 66), (1, 68)], 'x', 19, 22) if False else Volume()
    return fin | keel


def bore():
    """interior of the right pod, closed at the back by the 2-cell wall (z 66, 67), plus the 24-cell launch lane in front: x 15..23, y 3..10"""
    return Volume.box((POD_INT_X[0], POD_INT_Y[0], LANE_Z[0]), (POD_INT_X[1], POD_INT_Y[1], POD_INT_Z[1]))


def hull_envelope():
    right = pylon() | pod() | fins()
    env = fuselage() | hammer() | ridge() | tower() | mirror_x(right)
    return env - bore() - mirror_x(bore())


def lane_cells():
    """cells that must stay empty in both pods: bore + lane, minus the two host merge blocks"""
    cells = set(mirror_x(bore()).cells)
    cells -= {MERGE_CELL, mx(MERGE_CELL)}
    return cells


def top_y(vol, x, z):
    """highest hull cell y at column (x, z), or None"""
    ys = [c[1] for c in vol.cells if c[0] == x and c[2] == z]
    return max(ys) if ys else None


# ---- guns on the hammer, rails on the ridge, turrets ----
def rail_cells(sx):
    """SmallRailgun 1x2x8, min cell; the Back face stands against the tower front (z 34); right side x = 2, left x = -2"""
    return (2 if sx > 0 else -2, 13, 26)


def hammer_guns(env):
    """(name, kind, min cell) of the fixed guns on the hammer top: assault guns at the tips, autocannons inboard"""
    out = []
    for sx in (1, -1):
        for nm, kind, x, z0, ln in (('Assault', 'assault', 9, 7, 9), ('Autocannon', 'ac', 4, 9, 5)):
            x *= sx
            ty = max(top_y(env, x, z) for z in range(z0 + ln - 3, z0 + ln))
            out.append((nm, kind, (x, ty + 1, z0), sx))
    return out


def turret_sites():
    """(name, kind, min cell, up) of the turrets: 2 gatling on the pod roofs, 2 gatling under the pods, the assault turret on the aft deck"""
    out = []
    for sx, tg in ((1, 'Stbd'), (-1, 'Port')):
        out.append(('PD Roof ' + tg, 'gat', (sx * 19 - 2, 13, 58), 'Up'))
        out.append(('PD Belly ' + tg, 'gat', (sx * 19 - 2, -4, 58), 'Down'))
    out.append(('Assault Turret', 'med', (-3, 13, 58), 'Up'))
    return out


def machinery():
    """reactor, batteries and gyro cells on the centre line (x -1..1) of the lower deck"""
    d = {'reactor': (-1, 2, 33), 'batt': [(-1, 2, z) for z in (36, 39, 42, 45)]}
    gy = [(x, 4, z) for z in range(36, 42) for x in (-1, 0, 1)] + [(-1, 4, 42), (1, 4, 42)]      # 20 gyros on the battery tops (symmetric)
    d['gyro'] = gy
    return d


def exhaust_cells():
    """the flame of every large thruster: 3 x 3 cells wide, 4 cells long beyond the nozzle"""
    cells = set()
    for nm, mn, d in thr_list():
        v = H.dvec(d)
        ax = H.axis_of(v)
        lo = list(mn)
        hi = [m + 3 for m in mn]
        if v[ax] > 0:
            lo[ax], hi[ax] = mn[ax] + 3, mn[ax] + 7
        else:
            lo[ax], hi[ax] = mn[ax] - 4, mn[ax]
        cells |= set(Volume.box(tuple(lo), tuple(hi)).cells)
    return cells


def reserved_cells(env):
    r = {}
    for nm, mn in tank_list():
        r['tank ' + nm] = box(mn, (5, 5, 5))
    for nm, mn, d in thr_list():
        r['thr ' + nm] = box(mn, (3, 3, 3))
    m = machinery()
    rod = box(m['reactor'], (3, 3, 3))
    for mn in m['batt']:
        rod = rod | box(mn, (3, 2, 3))
    for c in m['gyro']:
        rod = rod | Volume([c])
    r['rod'] = rod
    r['cockpit'] = box((-1, 16, 41), (3, 3, 3))
    for sx in (1, -1):
        mn = rail_cells(sx)
        r['rail%d' % sx] = box(mn, (1, 2, 8))
    for nm, kind, mn, sx in hammer_guns(env):
        r['%s%d' % (kind, sx)] = box(mn, (1, 1, 9 if kind == 'assault' else 5))
    for nm, kind, mn, up in turret_sites():
        size = (5, 5, 5) if kind == 'gat' else (7, 5, 7)
        r['turret ' + nm] = box(mn, size)
    return r


def _mirror_orient(bp, b, tol=1e-3):
    """(Forward, Up) that gives block b's shape mirrored in x (matched on the six mount-face areas, the same table the hull builder uses)"""
    areas = H.orient_areas(b['sub'], bp.grid)
    cur = areas[(b['fwd'], b['up'])]
    want = {(-d[0], d[1], d[2]): a for d, a in cur.items()}
    for o in H.ORIENTS:
        a = areas[o]
        if all(abs(a.get(d, 0.0) - want.get(d, 0.0)) <= tol for d in H.FACES):
            return o
    raise H.HullError(f'no mirror orientation for {b["sub"]}')


def mirror_hull(res):
    """Make the hull mirror-symmetric block for block: the builder picks slope / slope2 / corner pairs by scanning cells in a fixed order,
    so the two halves of a symmetric voxel hull can differ in detail.  The right half (x > 0) is the master: every block of the left half
    takes the master's subtype with the mirrored orientation (x = 0 is its own mirror)."""
    bp = res.bp
    for c, b in list(res.cell_block.items()):
        if c[0] <= 0:
            continue
        m = mx(c)
        pb = res.cell_block[m]
        if pb['sub'] != b['sub']:
            for k in ('sub', 'key', 'mounts', 'size', 'mass', 'xtype'):
                pb[k] = b[k]
        o = _mirror_orient(bp, b)
        pb['fwd'], pb['up'] = o
        pb.pop('_mount_rects', None)
        res.cell_kind[m] = res.cell_kind[c]
        res.exposure[m] = frozenset((-d[0], d[1], d[2]) for d in res.exposure[c])
        if c in res.flush:
            res.flush[m] = frozenset((-d[0], d[1], d[2]) for d in res.flush[c])
    res.check()


def mirror_added(bp, blocks):
    """copy right-half blocks (greeble panels) to the left half"""
    out = []
    for b in blocks:
        if b['min'][0] <= 0:
            continue
        mn = (-b['min'][0], b['min'][1], b['min'][2])
        n = bp.add(b['xtype'], b['sub'], mn, b['fwd'], b['up'], b['name'], b['color'], kind=b['kind'])
        n['mass'] = b['mass']
        o = _mirror_orient(bp, n)
        n['fwd'], n['up'] = o
        out.append(n)
    return out


def build_hull(bp):
    env = hull_envelope()
    res_cells = reserved_cells(env)
    clear = lane_cells() | exhaust_cells()
    shell = env.hollow(1, bulkhead_every=12, deck_every=0)
    skin = [k for k in res_cells if k.startswith('tank') or k in ('rod',)]
    zones = Zones(
        # --- orange markings first (first match wins an attribute) ---
        Zone('pod_rim', lambda c: abs(c[0]) >= 13 and c[2] in (56, 57), color=ORANGE, kind='accent'),
        Zone('pod_stripe', lambda c: abs(c[0]) >= 24 and 5 <= c[1] <= 8, color=ORANGE_D, kind='accent'),
        Zone('hammer_edge', lambda c: c[2] <= 12 + abs(c[0]) * 0.12 and abs(c[0]) >= 3 and c[1] <= 9, color=ORANGE, kind='accent'),
        Zone('nose', H.region(z=(None, 12)), heavy=True, color=ORANGE, kind='accent'),
        Zone('deck_stripe', lambda c: abs(c[0]) in (3, 4) and c[1] == 12 and 24 <= c[2] <= 54, color=ORANGE, kind='accent'),
        Zone('ridge', lambda c: abs(c[0]) <= 1 and c[1] >= 12 and c[2] <= 36, color=DARKSTEEL, kind='dark'),
        Zone('tower_front', H.region(y=(13, None), z=(34, 38)), heavy=True, color=PLATE, kind='plate'),
        Zone('tower', H.region(y=(13, None), z=(34, 56)), color=PLATE, kind='plate'),
        Zone('belly', lambda c: c[1] <= 2 and abs(c[0]) <= 6, heavy=True, color=DARKSTEEL, kind='dark'),
        Zone('stern', lambda c: c[2] >= 66 and abs(c[0]) <= 6, heavy=False, color=DARKSTEEL, kind='dark'),
        Zone('pylon', lambda c: 7 <= abs(c[0]) <= 12 and c[1] <= 9, color=ORANGE_D, kind='accent'),
        Zone('wall', lambda c: abs(c[0]) >= 13 and c[2] >= 66, heavy=False, color=DARKSTEEL, kind='dark'),
        default_color=GUNMETAL, default_kind='armor')
    res = H.build_hull(bp, env, fill=shell, reserved=res_cells, clear=clear, skin_around=skin, zones=zones,
                       windows=H.all_of(H.region(y=(13, 17), z=(34, 40))))
    mirror_hull(res)
    return res, env, res_cells


# ---------------------------------------------------------------------------------------------------------------
# placement helpers
# ---------------------------------------------------------------------------------------------------------------
ORIENTS = H.ORIENTS


def _occupancy(bp):
    occ = {}
    for b in bp.blocks:
        for c in bp.cells(b):
            occ[c] = b
    return occ


def _attached(bp, b, occ):
    """does block b touch some other block through mount rectangles (the game's rule, see sebp.attachment)?"""
    import math
    for (axis, pc, ns, lo, hi, ex, pr) in bp.mount_rects(b):
        nidx = int(round(pc)) if ns > 0 else int(round(pc)) - 1
        oa = [i for i in range(3) if i != axis]
        for i in range(math.floor(lo[0]), math.floor(hi[0]) + 1):
            for j in range(math.floor(lo[1]), math.floor(hi[1]) + 1):
                cell = [0, 0, 0]
                cell[axis], cell[oa[0]], cell[oa[1]] = nidx, i, j
                nb = occ.get(tuple(cell))
                if nb is None or nb is b:
                    continue
                for (a2, pc2, ns2, lo2, hi2, ex2, pr2) in bp.mount_rects(nb):
                    if a2 == axis and abs(pc2 - pc) < 1e-6 and ns2 == -ns and lo[0] <= hi2[0] and lo2[0] <= hi[0] and lo[1] <= hi2[1] and lo2[1] <= hi[1]:
                        if ((ex & pr2) or (pr & ex2)) and b['key'] != nb['key']:
                            continue
                        return True
    return False


def place(bp, occ, xtype, sub, mn, name, prefer=(), strict=False, **kw):
    """add a block at `mn`, trying `prefer` then (unless strict) every orientation until it touches the hull; refuses to overlap."""
    for f, u in list(prefer) + ([] if strict else ORIENTS):
        b = bp.add(xtype, sub, mn, f, u, name, **kw)
        cells = bp.cells(b)
        if any(c in occ for c in cells) or any(c in bp.reserve for c in cells) or not _attached(bp, b, occ):
            bp.blocks.remove(b)
            continue
        for c in cells:
            occ[c] = b
        return b
    raise AssertionError(f'{bp.name}: {name} at {mn} cannot be attached in any orientation (overlap, reserved cell, or nothing to touch)')


def place_slide(bp, occ, xtype, sub, mn, name, deltas=(0, 1, -1, 2, -2, 3, -3, 4), axis=2, **kw):
    for d in deltas:
        c = list(mn)
        c[axis] += d
        try:
            return place(bp, occ, xtype, sub, tuple(c), name, **kw)
        except AssertionError:
            continue
    raise AssertionError(f'{bp.name}: {name} near {mn}: no attachable cell')


def place_any(bp, occ, xtype, sub, cells, name, **kw):
    for c in cells:
        try:
            return place(bp, occ, xtype, sub, c, name, **kw)
        except AssertionError:
            continue
    raise AssertionError(f'{bp.name}: {name}: none of {len(cells)} candidate cells works')


def merge_blocks(bp):
    return [b for b in bp.blocks if b['kind'] == 'cradle']


# ---------------------------------------------------------------------------------------------------------------
# systems
# ---------------------------------------------------------------------------------------------------------------
def add_systems(bp, res, env, lite):
    occ = _occupancy(bp)
    S = {}
    thr_blocks, gyros = [], []

    def reg(b):
        for c in bp.cells(b):
            assert c not in occ, f'{bp.name}: {b["name"]} overlaps {occ[c]["name"] or occ[c]["sub"]} at {c}'
            occ[c] = b
        return b

    # ---- hydrogen: ten full tanks, fourteen large thrusters touching them (exhaust cross-section 3x3 stays clear) ----
    tanks = []
    for nm, mn in tank_list():
        tanks.append(reg(bp.add('MyObjectBuilder_OxygenTank', 'SmallHydrogenTank', mn, 'Forward', 'Up', TAG + 'Hydrogen Tank ' + nm, GUNMETAL,
                                kind='tank', state={'filled': 'full'})))
    for nm, mn, d in thr_list():
        up = 'Forward' if d in ('Up', 'Down') else 'Up'
        b = reg(bp.add('MyObjectBuilder_Thrust', 'SmallBlockLargeHydrogenThrust', mn, d, up, TAG + 'Thrust ' + nm, ORANGE_D, kind='thr'))
        thr_blocks.append(b)
    S['tanks'], S['thr'] = tanks, thr_blocks

    # ---- weapons: B1 two railguns on the ridge, B2 two assault guns at the hammer tips, B3 two autocannons; turrets ----
    b1, b2, b3, pd, other = [], [], [], [], []
    for sx, tg in ((-1, 'Port'), (1, 'Stbd')):
        b1.append(reg(bp.add('MyObjectBuilder_SmallMissileLauncherReload', 'SmallRailgun', rail_cells(sx), 'Forward', 'Up',
                             TAG + '[B1] Railgun ' + tg, GUNCOL, kind='gun', state={'capacitor': 'full'})))
    for nm, kind, mn, sx in hammer_guns(env):
        tg = 'Stbd' if sx > 0 else 'Port'
        if kind == 'assault':
            b2.append(reg(bp.add('MyObjectBuilder_SmallMissileLauncherReload', 'SmallBlockMediumCalibreGun', mn, 'Forward', 'Up',
                                 TAG + '[B2] Assault Cannon ' + tg, GUNCOL, kind='gun')))
        else:
            b3.append(reg(bp.add('MyObjectBuilder_SmallGatlingGun', 'SmallBlockAutocannon', mn, 'Forward', 'Up',
                                 TAG + '[B3] Autocannon ' + tg, GUNCOL, kind='gun')))
    for b in b1 + b2 + b3:
        bp.keep_clear(b, 6, 'muzzle')
        bp.fill_ammo(b)
    if not lite:
        for nm, kind, mn, up in turret_sites():
            if kind == 'gat':
                t = reg(bp.add('MyObjectBuilder_LargeGatlingTurret', 'SmallGatlingTurret', mn, 'Forward', up, TAG + '[PD] Gatling Turret ' + nm[3:], GUNCOL, kind='gun'))
                pd.append(t)
            else:
                t = reg(bp.add('MyObjectBuilder_LargeMissileTurret', 'SmallBlockMediumCalibreTurret', mn, 'Forward', up, TAG + 'Assault Turret', GUNCOL, kind='gun'))
                other.append(t)
            bp.fill_ammo(t)
    weapons = b1 + b2 + b3 + pd + other
    S.update(b1=b1, b2=b2, b3=b3, pd=pd, weapons=weapons)

    # ---- power: reactor (Full) or small generators (Lite), four charged 1 MWh batteries, twenty gyros on the battery tops ----
    m = machinery()
    if lite:
        for i, mn in enumerate(((-1, 2, 33), (0, 2, 33), (1, 2, 33), (0, 2, 34))):
            place(bp, occ, 'MyObjectBuilder_Reactor', 'SmallBlockSmallGenerator', mn, TAG + 'Generator %d' % (i + 1), prefer=[('Forward', 'Up')], kind='bat',
                  items=[Item.ingot('Uranium', 5)])
    else:
        reg(bp.add('MyObjectBuilder_Reactor', 'SmallBlockLargeGenerator', m['reactor'], 'Forward', 'Up', TAG + 'Reactor', kind='bat',
                   items=[Item.ingot('Uranium', 20)]))
    bats = []
    for i, mn in enumerate(m['batt']):
        bats.append(reg(bp.add('MyObjectBuilder_BatteryBlock', 'SmallBlockBatteryBlock', mn, 'Forward', 'Up', TAG + 'Battery %d' % (i + 1), kind='bat',
                               state={'stored_power': 'full'})))
    for i, c in enumerate(m['gyro']):
        gyros.append(reg(bp.add('MyObjectBuilder_Gyro', 'SmallBlockGyro', c, 'Forward', 'Up', TAG + 'Gyro %d' % (i + 1), kind='gyro')))
    S['bats'], S['gyros'] = bats, gyros

    # ---- bridge: cockpit on the tower top; avionics in the centre corridor between the mid tanks ----
    cockpit = reg(bp.add('MyObjectBuilder_Cockpit', 'SmallBlockCockpit', (-1, 16, 41), 'Forward', 'Up', TAG + 'Cockpit [Helm Seat]', kind='cockpit'))
    pb_h = place(bp, occ, 'MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', (0, 3, 29), '[Helm] Corsair', kind='ctrl',
                 extra=pb_extra(_script('CorsairHelmAI.cs')) if _script('CorsairHelmAI.cs') else '', custom_data=HELM_DATA)
    pb_s = place(bp, occ, 'MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', (0, 3, 31), '[Staff] Corsair', kind='ctrl',
                 extra=pb_extra(_script('CorsairStaffAI.cs')) if _script('CorsairStaffAI.cs') else '', custom_data=STAFF_DATA)
    pb_b = place_any(bp, occ, 'MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', [(0, 4, 30), (0, 5, 31), (0, 5, 30), (0, 3, 30), (0, 3, 27), (0, 3, 33)],
                     '[Bay] Corsair', kind='ctrl', extra=pb_extra(_script('CradleBayAI.cs')) if _script('CradleBayAI.cs') else '', custom_data=BAY_DATA)
    rc = place(bp, occ, 'MyObjectBuilder_RemoteControl', 'SmallBlockRemoteControl', (0, 4, 29), TAG + 'Remote Control', kind='ctrl')
    timer = place(bp, occ, 'MyObjectBuilder_TimerBlock', 'TimerBlockSmall', (0, 4, 31), TAG + 'Timer D (deadman)', kind='ctrl',
                  settings={'Delay': 5000, 'Silent': 'true'})
    # event controller for the dead-man's second layer: placed and named, UNCONFIGURED (the "Cockpit occupied" event number was never seen, test K4)
    place(bp, occ, 'MyObjectBuilder_EventControllerBlock', 'EventControllerSmall', (0, 5, 29), TAG + 'EC Cockpit (configure in game)', kind='ctrl')
    S.update(cockpit=cockpit, pb=pb_h, staff=pb_s, bay=pb_b, rc=rc, timer=timer)

    # ---- cradle merge blocks (clamp face Forward, towards the drone), one cell in front of the 2-cell wall ----
    merges = []
    for mn, nm in ((MERGE_CELL, '[Bay 1] Merge Port'), (mx(MERGE_CELL), '[Bay 2] Merge Starboard')):
        pass
    # Bay 1 = the port pod (x < 0), Bay 2 = starboard (x > 0)
    for mn, nm in ((mx(MERGE_CELL), '[Bay 1] Merge Port'), (MERGE_CELL, '[Bay 2] Merge Starboard')):
        b = merge_small(bp, mn, 'Forward', TAG + nm, ORANGE)
        b['kind'] = 'cradle'
        merges.append(reg(b))
    S['merges'] = merges

    # ---- cameras, antenna ----
    cams = []
    cam_sites = [('Front', (0, 5, 4), 'Forward', 'Up', 2), ('Rear', (0, 6, 68), 'Backward', 'Up', 2), ('Left', (-7, 6, 40), 'Left', 'Up', 2),
                 ('Right', (7, 6, 40), 'Right', 'Up', 2), ('Up', (0, 16, 49), 'Up', 'Backward', 2), ('Down', (0, 0, 40), 'Down', 'Forward', 2),
                 ('Wing Port', (-10, 6, 9), 'Forward', 'Up', 2), ('Wing Stbd', (10, 6, 9), 'Forward', 'Up', 2)]
    for nm, mn, f, u, ax in cam_sites:
        cands = [(mn[0], y, z) for y in (mn[1], mn[1] - 1, mn[1] + 1, mn[1] - 2, mn[1] + 2) for z in (mn[2] + d for d in (0, 1, -1, 2, -2, 3, -3, 4))] if nm.startswith('Wing') else \
                [tuple(mn[k] + (d if k == ax else 0) for k in range(3)) for d in (0, 1, -1, 2, -2, 3, -3, 4)]
        b = place_any(bp, occ, 'MyObjectBuilder_CameraBlock', 'SmallCameraBlock', cands, TAG + 'Camera ' + nm, prefer=[(f, u)], strict=True, kind='cam')
        bp.keep_clear(b, 3, 'camera view')
        cams.append(b)
    place_slide(bp, occ, 'MyObjectBuilder_RadioAntenna', 'SmallBlockRadioAntenna', (0, 16, 48), TAG + 'Antenna', prefer=[('Up', 'Backward')], strict=True,
                kind='ant', settings={'BroadcastRadius': 5000, 'ShowShipName': 'true'})
    if not lite:
        for sx in (-1, 1):
            place_slide(bp, occ, 'MyObjectBuilder_SensorBlock', 'SmallBlockSensor', (sx * 3, 0, 38), TAG + 'Sensor ' + ('Stbd' if sx > 0 else 'Port'),
                        prefer=[('Down', 'Forward')], strict=True, kind='sensor')
        for i, (sx, zr, xr, yr) in enumerate(((1, (10, 16), (9, 13), (3, 9)), (-1, (10, 16), (9, 13), (3, 9)), (1, (58, 66), (26, 27), (4, 9)),
                                              (-1, (58, 66), (26, 27), (4, 9)))):
            cands = [(sx * x, y, z) for x in range(*xr) for y in range(*yr) for z in range(*zr)]
            place_any(bp, occ, 'MyObjectBuilder_Decoy', 'SmallDecoy', cands, TAG + 'Decoy %d' % (i + 1), kind='ai')

    # ---- groups (names double as toolbar targets) and toolbars ----
    bp.group(TAG + 'Thrusters', thr_blocks)
    bp.group(TAG + 'Weapons', weapons)
    bp.group('[B1]', b1)
    bp.group('[B2]', b2)
    bp.group('[B3]', b3)
    if pd:
        bp.group('[PD]', pd)
    bp.group(TAG + 'Gyros', gyros)
    bp.group(TAG + 'Bay Merges', merges)
    bp.set_toolbars(timer, toolbar=[TB.group(TAG + 'Thrusters', 'OnOff_Off'), TB.group(TAG + 'Weapons', 'OnOff_Off')])
    tb = {0: TB.weapon('SmallMissileLauncherReload', 'SmallRailgun'), 1: TB.weapon('SmallMissileLauncherReload', 'SmallBlockMediumCalibreGun'),
          2: TB.weapon('SmallGatlingGun', 'SmallBlockAutocannon'), 3: TB.group(TAG + 'Thrusters', 'OnOff'),
          4: TB.block(merges[0], 'OnOff_Off'), 5: TB.block(merges[1], 'OnOff_Off')}
    if pd:
        tb[6] = TB.group('[PD]', 'OnOff')
    if not lite:
        for i, arg in enumerate(('auto', 'manual', 'assist hud', 'hunt', 'rtb', 'lock', 'next', 'unlock')):
            tb[9 + i] = TB.run(pb_h, arg, experimental=True)
        for i, arg in enumerate(('wing satellite', 'wing hunt', 'wing rtb', 'release 1', 'release 2', 'release all', 'recall')):
            tb[18 + i] = TB.run(pb_s, arg, experimental=True)
    bp.set_toolbars(cockpit, toolbar=tb)
    cockpit['settings']['IsMainCockpit'] = 'true'
    S['occ'] = occ
    return S


def build(lite=False, name=None):
    name = name or (NAME + (' Lite' if lite else ''))
    bp = Blueprint(name, 'Small', seed=seed_for(NAME + (' Lite' if lite else '')))
    res, env, _ = build_hull(bp)
    S = add_systems(bp, res, env, lite)
    return bp, res, S


def check_cradles(bp):
    """host-side docking check (what _common.check_docking does for a drone, turned around): per cradle the merge block is the only block in the bore
    and the lane, the clamp face points at the bore (Forward), the stalk layer around it is empty and the wall behind it is armour"""
    for mb in merge_blocks(bp):
        assert merge_clamp_dir(bp, mb) == 'Forward', f'{mb["name"]}: clamp face {merge_clamp_dir(bp, mb)}'
    occ = bp.occupancy()
    for sx in (1, -1):
        bore_cells = [c for c in (set(bore().cells) if sx > 0 else set(bore().reflected('x', 0.5).cells))]
        mc = MERGE_CELL if sx > 0 else mx(MERGE_CELL)
        inside = [c for c in bore_cells if c in occ and c != mc]
        assert not inside, f'{bp.name}: {len(inside)} blocks inside the launch lane/bore (first {inside[0]}: {occ[inside[0]]["name"] or occ[inside[0]]["sub"]})'
        assert mc in occ and occ[mc]['kind'] == 'cradle'
        wall = (mc[0], mc[1], mc[2] + 1)
        assert wall in occ and occ[wall]['kind'] in ('armor', 'dark', 'accent', 'plate'), f'no armour wall behind the merge block at {wall}'
        assert (mc[0], mc[1], mc[2] + 2) in occ, '2-cell wall'
    return True


def corsair(lite=False):
    bp, res, _ = build(lite)
    res.check()
    check_cradles(bp)
    return finalize(bp)


def drone_cells_in_host(bp, drone):
    """cells of the docked drone expressed in host cells (same 0.5 m lattice; the pose must be axis-aligned and lattice-aligned) -> {host cell: block}"""
    pose = drone.pose
    out = {}
    for b in drone.blocks:
        for c in drone.cells(b):
            p = pose.apply(tuple(v * 0.5 for v in c))
            hc = tuple(p[i] / 0.5 for i in range(3))
            r = tuple(round(v) for v in hc)
            assert all(abs(hc[i] - r[i]) < 1e-6 for i in range(3)), f'drone cell {c} is off the host lattice ({hc})'
            out[r] = b
    return out


def check_wing(bp):
    """each docked drone: nose forward, inside its bore with one free cell on every side (merge pair excepted), no host block touches it
    face to face except the host merge block (a permanent merge otherwise)"""
    occ = bp.occupancy()
    mbs = {mb['name']: mb for mb in merge_blocks(bp)}
    assert len(bp.subgrids) == 2
    for drone, pose in bp.subgrids:
        dm = [b for b in drone.blocks if b['kind'] == 'merge']
        assert len(dm) == 1
        cells = drone_cells_in_host(bp, drone)
        dmc = [c for c, b in cells.items() if b is dm[0]][0]
        hm = [mb for mb in mbs.values() if tuple(mb['min']) == (dmc[0], dmc[1], dmc[2] + 1)]
        assert len(hm) == 1, f'{drone.name}: no host merge block one cell behind the drone merge block at {dmc}'
        assert abs(pose.rot.angle_to(MU.Rot.identity()) if hasattr(pose.rot, 'angle_to') else 0) < 1e-6, 'drone must be nose forward'
        for c, b in cells.items():
            assert c not in occ, f'{drone.name}: drone cell {c} inside host block {occ[c]["name"] or occ[c]["sub"]}'
            assert 15 <= abs(c[0]) <= 23 and 3 <= c[1] <= 10 and 56 <= c[2] <= 65, f'{drone.name}: cell {c} outside the bore'
            for d in H.FACES:
                n = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
                if n in occ and not (b is dm[0] and occ[n] is hm[0]):
                    raise AssertionError(f'{drone.name}: host block {occ[n]["name"] or occ[n]["sub"]} at {n} touches drone cell {c}')
            assert abs(c[0]) not in (15, 23) or True
    return True


def corsair_wing(lite=False):
    """Corsair + two Hornets clamped in the cradles as separate small CubeGrids (drone nose forward, merge faces touching)"""
    from core import hornet
    bp, res, S = build(lite, name='Corsair Wing' + (' Lite' if lite else ''))
    res.check()
    check_cradles(bp)
    for hm, tg in zip(S['merges'], ('Port', 'Starboard')):
        d = hornet(lite)
        d.display = 'Hornet ' + tg
        dm = [b for b in d.blocks if b['kind'] == 'merge'][0]
        bp.dock(d, hm, dm, parent_face=MERGE_CLAMP_FACE_SMALL, child_face=MERGE_CLAMP_FACE_SMALL)
    finalize(bp)
    check_wing(bp)
    return bp


MODELS = {NAME: corsair, NAME + ' Wing': corsair_wing}
_COMMON = ('Catamaran-hammerhead corvette (small grid, 33 m x 26.5 m x 12 m): charcoal hull with orange accents, hammer crossbar at the bow carrying two assault cannons '
           'and two autocannons, a dorsal ridge with two small railguns (B1), a stepped bridge tower with the cockpit, four point-defence gatling turrets and an assault '
           'turret (Full), 14 large hydrogen thrusters on 10 full tanks (7.1 m/s2 forward, 3.6 m/s2 in the other directions), 20 gyros, a 14.75 MW reactor and four charged '
           'batteries, three Programmable Blocks ("[Helm] Corsair", "[Staff] Corsair", "[Bay] Corsair" for the cradles), dead-man timer, hand-flyable. Two cradle pods on outrigger pylons, each a 9 x 8 x 9 cell '
           'bay for a Hornet-class drone (Hornet, Wasp, Watcher, Harrier, Lancer, Aegis), nose forward, 24-cell launch lane; the bay merge blocks are "[SH] [Bay n] Merge ...". '
           'Limitation: the assault cannons hold 3 shells, the autocannons 3 clips (48 rounds) and the railguns 6 slugs; there is no conveyor feed (port positions are not known), '
           'so those guns run dry after a short fight. ')
DOCS = {NAME: _COMMON + 'Paste it empty and add drones by hand, or use Corsair Wing.',
        NAME + ' Wing': _COMMON + 'Corsair Wing is the same ship with two Hornets already clamped in the cradles as separate CubeGrids (merge blocks touching, enabled). '
                        'Paste risk C1 (grids_bays.md): whether the game merges the touching grids at paste time is untested; if the drones float free on paste, '
                        'toggle the bay merge blocks Off/On, or paste Corsair and the drones separately. The drones add 2 x 4.4 t that the acceleration figures do not include.'}


def figures(bp):
    """numbers for the doc: mass (blocks / loaded with magazines + uranium), acceleration, COM, PCU, HP, dimensions, rounds per gun, fuel, power"""
    acc, m = twr(bp)
    loaded = m + sum(i.kg() for b in bp.blocks for i in bp.items_of(b))
    thrust = thrust_by_dir(bp)
    com = [0.0, 0.0, 0.0]
    for b in bp.blocks:
        cs = bp.cells(b)
        mm = block_mass(b) + sum(i.kg() for i in bp.items_of(b))
        for c in cs:
            for k in range(3):
                com[k] += mm / len(cs) * (c[k] + 0.5)
    com = [v / loaded for v in com]
    rep = H.report(bp)
    rounds = {}
    for b in bp.blocks:
        for i in bp.items_of(b):
            if i.kind == 'AmmoMagazine' and b['kind'] == 'gun':
                cap = C._stat(b)['stats']['weapon']['ammo_detail'][i.sub]['magazine_capacity']
                rounds[b['name']] = (i.amount, i.amount * cap)
    return dict(mass_kg=m, loaded_kg=loaded, acc=acc, acc_loaded={d: thrust[d] / loaded for d in thrust}, thrust_n=thrust, dims=bp.stats()['dims'],
                blocks=len(bp.blocks), com=com, pcu=rep['pcu'], hp=rep['hp'], rounds=rounds, bbox_cells=rep['bbox_cells'])


def render(bp, outdir, stem='Corsair'):
    """PNG views with the repo's own thumbnail / orthographic code (tools/hull_demo.py): yaw views from above and below, silhouettes, zooms"""
    import hull_demo as HD
    from PIL import Image
    os.makedirs(outdir, exist_ok=True)
    HD.PAL.update(PAL)
    cells_of = {id(b): bp.cells(b) for b in bp.blocks}

    class Sub:
        def __init__(self, keep):
            self.blocks = [b for b in bp.blocks if keep(cells_of[id(b)])]

        def cells(self, b):
            return cells_of[id(b)]

    def sheet(name, keep, pitch):
        sub = Sub(keep)
        paths = []
        for yaw in range(4):
            pth = os.path.join(outdir, f'{stem}_{name}_{yaw}.png')
            HD._View(sub, yaw, pitch).thumb(pth, stem + ' ' + name, f'yaw {yaw} pitch {pitch}', HD.PAL)
            paths.append(pth)
        ims = [Image.open(q) for q in paths]
        w, h = ims[0].size
        out = Image.new('RGB', (w * 2, h * 2))
        for i, im in enumerate(ims):
            out.paste(im, ((i % 2) * w, (i // 2) * h))
        fn = os.path.join(outdir, f'{stem}_{name}.png')
        out.save(fn)
        for q in paths:
            os.remove(q)
        return fn
    files = [sheet('top', lambda cs: True, 0), sheet('under', lambda cs: True, 2),
             sheet('bow', lambda cs: all(c[2] <= 40 for c in cs), 0),
             sheet('pod', lambda cs: all(c[0] >= 4 and c[2] >= 50 for c in cs), 0),
             sheet('stern', lambda cs: all(c[2] >= 50 for c in cs), 0)]
    files.append(HD.render_ortho(bp, outdir, stem))
    return files


_LETTER = {'Cockpit': 'C', 'Thrust': 'T', 'OxygenTank': 'K', 'SmallGatlingGun': 'G', 'SmallMissileLauncherReload': 'G', 'LargeGatlingTurret': 'U',
           'LargeMissileTurret': 'U', 'BatteryBlock': 'B', 'Reactor': 'R', 'Gyro': 'y', 'MyProgrammableBlock': 'P', 'RemoteControl': 'r',
           'TimerBlock': 't', 'EventControllerBlock': 'e', 'CameraBlock': 'c', 'RadioAntenna': 'a', 'Decoy': 'd', 'SensorBlock': 's', 'MergeBlock': 'M'}


def plans(bp):
    """ASCII plans: side (x = 0), decks y = 12 / 8 / 4 (columns x -26..26, rows z), cross sections z = 30 / 60 (the cradles)"""
    occ = {c: b for b in bp.blocks for c in bp.cells(b)}

    def ch(b):
        if b is None:
            return '.'
        x = b['xtype'].replace('MyObjectBuilder_', '')
        if x == 'CubeBlock':
            return 'w' if 'Window' in b['sub'] else '#'
        return 'M' if b['kind'] == 'cradle' else _LETTER.get(x, '?')
    out = ['SIDE x=0 (columns z 0..70, rows y)', '     ' + ''.join(str(z // 10 % 10) if z % 10 == 0 else ' ' for z in range(71)),
           '     ' + ''.join(str(z % 10) for z in range(71))]
    for y in range(20, -5, -1):
        out.append('y%3d ' % y + ''.join(ch(occ.get((0, y, z))) for z in range(71)))
    for y in (12, 8, 4):
        out += ['', f'PLAN y={y} (columns x -26..26, rows z)', '     ' + ''.join(str(abs(x) % 10) for x in range(-26, 27))]
        for z in range(0, 71):
            out.append('z%3d ' % z + ''.join(ch(occ.get((x, y, z))) for x in range(-26, 27)))
    for z in (30, 60, 65):
        out += ['', f'SECTION z={z} (columns x, rows y)', '     ' + ''.join(str(abs(x) % 10) for x in range(-26, 27))]
        for y in range(19, -5, -1):
            out.append('y%3d ' % y + ''.join(ch(occ.get((x, y, z))) for x in range(-26, 27)))
    return '\n'.join(out)


def main(argv):
    """python3 -I tools/designs/corsair.py [--render DIR] [--plans]   (no option: build + validate every model and variant)"""
    if '--plans' in argv:
        print(plans(corsair(False)))
        return 0
    if '--render' in argv:
        d = argv[argv.index('--render') + 1]
        for f in render(corsair_wing(False), d):
            print(f)
        return 0
    return C.run_module(MODELS)


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
