"""Shrike: small-grid delta-wing fighter, one pilot, one Programmable Block ("[Helm] Shrike", script ShrikeAI.cs), hand-flyable and scripted.

Module contract: MODELS = {"Shrike": fn(lite)}, DOCS = {"Shrike": one paragraph}, SCRIPTS = [script file names it embeds].
    python3 -I tools/designs/shrike.py                 build + validate Shrike and Shrike Lite, one report line each
    python3 -I tools/designs/shrike.py --plans         ASCII deck plans (docs/design/ships/Shrike.md)
    python3 -I tools/designs/shrike.py --render DIR    PNG views + silhouettes with the repo's thumbnail code (tools/hull_demo.py)
Design notes, numbers, layout map and the in-game checks: docs/design/ships/Shrike.md.   Tests: tools/tests/test_shrike.py.

Frame: cells are 0.5 m, x = Right, y = Up, z = Backward (the bow points to -z); mirror plane = the middle of cell 0.  Hull from tools/hulls.py
(voxel volumes -> armour blocks; zones for heavy / light and the livery), made block-for-block mirror-symmetric by mirror_hull(); systems placed
by hand into reserved volumes.  Hydrogen: three 500 kL tanks with eight large thrusters on their faces (Back face on the face centre: the pattern of
Red Ship / Starburst / Venture); the lateral and vertical axes get one thruster each (8 m/s2), forward and back a symmetric pair (16 m/s2).
Lite = only block shapes seen in a game-written blueprint (no sensor, AI blocks, decoys, PB Run toolbar slots).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403  (helpers + constants)
import _common as C
import spares
import hulls as H
from hulls import Volume, Zone, Zones
import turrets as TU
import sebp_toolbar as TB
from sebp_inv import Item

NAME = 'Shrike'
SCRIPTS = ['ShrikeAI.cs', 'TurretAI.cs']
TURRET_CELL = (0, 9, 22)           # the PD2S stator's lower cell: on the spine (top y = 8) behind the canopy (y 9, z 17..19)
TAG = '[SH] '                     # host-ship prefix (control_arch 3.6 / grids_bays 6.3): Helm collects blocks by prefix AND grid

CUSTOM_DATA = """[Link]
Block Prefix=[SH]

[Manual]
HUD Surface=0

[Battery]
Standoff By Weapons=true
"""
# Full: the Helm also sends its target (.H2S) to the turret PB, which a single-PB ship would not do by itself (the link is on when a [Staff] PB exists, or with Enable=ON)
CUSTOM_DATA_FULL = """[Ship]
Tag=SH

[Link]
Enable (AUTO/ON/OFF)=ON
Ship Tag=SH
Block Prefix=[SH]

[Manual]
HUD Surface=0

[Battery]
Standoff By Weapons=true
"""


def _script(name):
    return script_text(name) if os.path.exists(os.path.join(ROOT, 'scripts', name)) else ''

# ---- livery (family colours: charcoal gunmetal hull, orange accents, cyan glass) ----
GUNMETAL = H.hsv_mask(214, 0.14, 0.30)
PLATE = H.hsv_mask(214, 0.10, 0.44)
DARKSTEEL = H.hsv_mask(220, 0.18, 0.18)
ORANGE = H.hsv_mask(24, 0.92, 0.96)
ORANGE_D = H.hsv_mask(20, 0.85, 0.70)
GUNCOL = H.hsv_mask(220, 0.15, 0.20)

PAL = {'armor': (92, 98, 110), 'plate': (130, 138, 150), 'dark': (58, 62, 74), 'accent': (236, 120, 36), 'stripe': (236, 120, 36),
       'glass': (96, 205, 235), 'gun': (24, 26, 32), 'thr': (240, 170, 60), 'sys': (80, 150, 110), 'cockpit': (96, 205, 235),
       'tank': (70, 120, 90), 'ctrl': (90, 110, 170), 'cam': (36, 36, 42), 'bat': (70, 120, 90), 'gyro': (150, 150, 60),
       'trim': (52, 56, 68), 'ant': (185, 185, 195), 'sensor': (120, 80, 150), 'ai': (60, 150, 170), 'cargo': (150, 120, 70), 'light': (255, 210, 120)}

# ---------------------------------------------------------------------------------------------------------------
# frame constants (cells).  Mirror plane: the middle of cell 0 (odd widths): x -> -x.
# ---------------------------------------------------------------------------------------------------------------
LEN = 34                          # z 0 .. 33  = 17 m
PX, PY, PZ = 8, 3, 18             # centre cell of the right pod's hydrogen tank (the left pod mirrors it)
CY, CZ = 4, 18                    # centre cell (y, z) of the core tank on the centre line (x = 0)


def mirror_x(v):
    return v.mirror('x', 0.5)


def box(mn, size):
    return Volume.box(mn, (mn[0] + size[0], mn[1] + size[1], mn[2] + size[2]))


def fuselage():
    """chisel nose, body, tapered tail; x -3..3 (3.5 m), y 1..5"""
    nose = Volume.loft('z', 3, 12, (-2, 3, 2, 5), (-3, 4, 1, 6), 'rect').chamfer('z', 1)
    body = Volume.loft('z', 12, 28, (-3, 4, 1, 6), (-3, 4, 1, 6), 'rect').chamfer('z', 1)
    tail = Volume.loft('z', 28, LEN, (-3, 4, 1, 6), (-2, 3, 2, 5), 'rect')
    return nose | body | tail


def hump():
    """dorsal housing of the core tank (it rises one cell above the deck)"""
    return Volume.loft('z', CZ - 3, CZ + 4, (-3, 4, 5, 7), (-3, 4, 5, 7), 'rect').chamfer('z', 1)


def wing():
    """delta wing, one cell thick, on the deck plane y = 5 (2 thick at the root); right side"""
    poly = [(4, 11), (16, 24), (16, 27), (4, 30)]
    return Volume.prism(poly, 'y', 5, 6) | Volume.prism([(4, 11), (7, 14), (7, 29), (4, 30)], 'y', 4, 5)


def canard():
    """small foreplane on the fuselage flank (right side), level with the wing root"""
    return Volume.prism([(4, 8), (8, 11), (8, 13), (4, 13)], 'y', 4, 5)


def fin():
    """tail fin (right side), standing on the wing root: a swept 1-thick plate at x = 4, 7 cells tall"""
    return Volume.prism([(6, 26), (13, 31), (13, 34), (6, 34)], 'x', 4, 5)      # (y, z) profile


def spine():
    return H.spine(16, 31, 6, 3, width=3, x_at=0.5, profile='blade')


def pod_thrusters(sx):
    """(name, min cell, exhaust direction) of one pod: main (aft), retro (forward), strafe (outboard)"""
    px = sx * PX
    out = [('Main', (px - 1, PY - 1, PZ + 3), 'Backward'),
           ('Retro', (px - 1, PY - 1, PZ - 5), 'Forward')]
    out.append(('Strafe', (px + 3, PY - 1, PZ - 1), 'Right') if sx > 0 else ('Strafe', (px - 5, PY - 1, PZ - 1), 'Left'))
    return out


def core_thrusters():
    return [('Dive', (-1, CY + 3, CZ - 1), 'Up'),               # exhaust up: pushes the ship down
            ('Lift', (-1, CY - 5, CZ - 1), 'Down')]             # exhaust down: pushes the ship up


def tank_min(sx):
    return (sx * PX - 2, PY - 2, PZ - 2)


def core_tank_min():
    return (-2, CY - 2, CZ - 2)


def cowl():
    """skin around the pod tank, stepped toward the nozzles: a 7 x 7 x 5 body (notched corners; it passes through the wing and rises one cell
    above it) and a 5 x 5 frame on each end; the thrusters stick out of the frames and the outboard face"""
    body = Volume.box((PX - 3, PY - 3, PZ - 2), (PX + 4, PY + 4, PZ + 3)).chamfer('z', 1)
    ends = box((PX - 2, PY - 2, PZ - 3), (5, 5, 1)) | box((PX - 2, PY - 2, PZ + 3), (5, 5, 1))
    return body | ends


def hull_envelope():
    right = wing() | fin() | cowl() | canard()
    return fuselage() | spine() | hump() | mirror_x(right)


def reserved_cells():
    r = {}
    r['cockpit'] = box((-1, 5, 10), (3, 4, 6))
    for sx, tag in ((1, 'R'), (-1, 'L')):
        r['tank' + tag] = box(tank_min(sx), (5, 5, 5))
        for nm, mn, d in pod_thrusters(sx):
            r['thr' + nm + tag] = box(mn, (3, 3, 3))
    r['tankC'] = box(core_tank_min(), (5, 5, 5))
    for nm, mn, d in core_thrusters():
        r['thr' + nm] = box(mn, (3, 3, 3))
    r['cargo'] = box((-1, 2, 6), (3, 3, 3))
    r['batt2'] = box((-1, 2, 25), (3, 2, 3))             # carves the bulkhead at z = 27
    r['assault'] = box((0, 5, 0), (1, 1, 9))
    for sx in (1, -1):
        r['acn%d' % sx] = box((sx * 2, 3, 2), (1, 1, 5))
        r['gat%d' % sx] = box((sx * 2, 6, 10), (1, 1, 4))
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
        m = (-c[0], c[1], c[2])
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


def exhaust_clear():
    """the flame of every large thruster: 3 x 3 cells wide, 4 cells long, beyond the nozzle"""
    cells = set()
    ths = [t for sx in (1, -1) for t in pod_thrusters(sx)] + core_thrusters()
    for nm, mn, d in ths:
        v = H.dvec(d)
        ax = H.axis_of(v)
        lo = list(mn); hi = [m + 3 for m in mn]
        if v[ax] > 0:
            lo[ax], hi[ax] = mn[ax] + 3, mn[ax] + 7
        else:
            lo[ax], hi[ax] = mn[ax] - 4, mn[ax]
        cells |= set(Volume.box(tuple(lo), tuple(hi)).cells)
    return cells


def build_hull(bp):
    env = hull_envelope()
    res_cells = reserved_cells()
    shell = env.hollow(1, bulkhead_every=8)
    zones = Zones(
        Zone('tail_glow', H.all_of(H.region(z=(33, None)), lambda c: abs(c[0]) <= 1), color=ORANGE, kind='accent'),
        Zone('wing_le', lambda c: abs(c[0]) >= 5 and c[1] == 5 and c[2] <= 11 + (abs(c[0]) - 4) * 1.08 + 1, color=ORANGE, kind='accent'),
        Zone('wingtip', lambda c: abs(c[0]) >= 14, color=ORANGE, kind='accent'),
        Zone('wing_stripe', lambda c: abs(c[0]) >= 5 and c[1] >= 5 and c[2] in (23, 24) and abs(c[0]) <= 13, color=ORANGE_D, kind='accent'),
        Zone('fin_tip', lambda c: c[1] >= 11 and abs(c[0]) >= 4, color=ORANGE, kind='accent'),
        Zone('spine_stripe', lambda c: abs(c[0]) <= 0 and c[1] >= 7 and 17 <= c[2] <= 29, color=ORANGE, kind='accent'),
        Zone('nose', H.region(z=(None, 8)), heavy=True, color=ORANGE, kind='accent'),
        Zone('belly', H.all_of(H.region(y=(None, 2)), H.region(z=(3, 17)), lambda c: abs(c[0]) <= 3), heavy=True, color=DARKSTEEL, kind='dark'),
        Zone('belly_aft', H.region(y=(None, 2)), color=DARKSTEEL, kind='dark'),
        Zone('cockpit', H.region(y=(5, None), z=(9, 17)), heavy=True, color=PLATE, kind='plate'),
        Zone('podring', lambda c: abs(c[0]) >= 5 and c[1] <= 4 and abs(c[2] - PZ) == 3 and c[0] != 0, color=ORANGE, kind='accent'),
        Zone('wing_root', lambda c: 4 <= abs(c[0]) <= 8 and c[1] >= 4, color=PLATE, kind='plate'),
        default_color=GUNMETAL, default_kind='armor')
    res = H.build_hull(bp, env, fill=shell, reserved=res_cells, clear=exhaust_clear(), skin_around=['cockpit'], zones=zones,
                       windows=H.all_of(H.region(y=(3, 5), z=(12, 17)), lambda c: abs(c[0]) == 3))
    mirror_hull(res)
    # raised plating on the flat upper surfaces of the right half (thin ArmorPanel slabs, deterministic), mirrored to the left
    pan = H.greeble(res, 'panel', density=0.30, normals=('Up',), where=lambda c: c[1] in (5, 6) and c[2] >= 14 and c[0] >= 2, seed=11, color=DARKSTEEL)
    res.greebles += mirror_added(bp, pan)
    res.check()
    return res


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
    """add a block at `mn`, trying `prefer` then (unless strict) every orientation until it touches the hull; refuses to overlap.
    strict=True for blocks that must face a given way (cameras, sensor, antenna)."""
    last = None
    for f, u in list(prefer) + ([] if strict else ORIENTS):
        b = bp.add(xtype, sub, mn, f, u, name, **kw)
        cells = bp.cells(b)
        if any(c in occ for c in cells) or not _attached(bp, b, occ):
            bp.blocks.remove(b)
            last = (f, u)
            continue
        for c in cells:
            occ[c] = b
        return b
    raise AssertionError(f'{bp.name}: {name} at {mn} cannot be attached in any orientation (overlap or nothing to touch)')


# ---------------------------------------------------------------------------------------------------------------
# systems
# ---------------------------------------------------------------------------------------------------------------
def add_systems(bp, res, lite):
    occ = _occupancy(bp)
    thr_blocks, weapons, b1, b2, gyros = [], [], [], [], []

    def put(*a, **k):
        return place(bp, occ, *a, **k)

    # hydrogen: three tanks (full) and the eight large thrusters that touch them (thruster Back face on the tank face centre, as in Red Ship / Venture)
    def tank(mn, name):
        b = bp.add('MyObjectBuilder_OxygenTank', 'SmallHydrogenTank', mn, 'Forward', 'Up', TAG + name, GUNMETAL, kind='tank', state={'filled': 'full'})
        for c in bp.cells(b): occ[c] = b
        return b
    def thruster(mn, d, name):
        up = 'Forward' if d in ('Up', 'Down') else 'Up'
        b = bp.add('MyObjectBuilder_Thrust', 'SmallBlockLargeHydrogenThrust', mn, d, up, TAG + name, ORANGE_D, kind='thr')
        bp.keep_clear(b, 4, 'exhaust')
        for c in bp.cells(b): occ[c] = b
        thr_blocks.append(b)
        return b
    for sx, tag in ((1, 'R'), (-1, 'L')):
        tank(tank_min(sx), 'Hydrogen Tank ' + tag)
        for nm, mn, d in pod_thrusters(sx):
            thruster(mn, d, 'Thrust ' + nm + ' ' + tag)
    tank(core_tank_min(), 'Hydrogen Tank Core')
    for nm, mn, d in core_thrusters():
        thruster(mn, d, 'Thrust ' + nm)

    # weapons (all fixed forward, parallel to the nose): B1 ballistic (2 autocannon + 2 gatling), B2 shell (assault cannon)
    cargo = bp.add('MyObjectBuilder_CargoContainer', 'SmallBlockMediumContainer', (-1, 2, 6), 'Forward', 'Up', TAG + 'Ammo Reserve', kind='cargo')
    for c in bp.cells(cargo): occ[c] = cargo
    ass = bp.add('MyObjectBuilder_SmallMissileLauncherReload', 'SmallBlockMediumCalibreGun', (0, 5, 0), 'Forward', 'Up', TAG + '[B2] Assault Cannon', GUNCOL, kind='gun')
    b2.append(ass)
    for sx, tg in ((-1, 'L'), (1, 'R')):
        b1.append(bp.add('MyObjectBuilder_SmallGatlingGun', 'SmallBlockAutocannon', (sx * 2, 3, 2), 'Forward', 'Up', TAG + '[B1] Autocannon ' + tg, GUNCOL, kind='gun'))
        b1.append(bp.add('MyObjectBuilder_SmallGatlingGun', '', (sx * 2, 6, 10), 'Forward', 'Up', TAG + '[B1] Gatling ' + tg, GUNCOL, kind='gun'))
    for b in b1 + b2:
        bp.keep_clear(b, 6, 'muzzle')
        for c in bp.cells(b): occ[c] = b
        bp.fill_ammo(b)
    weapons = b1 + b2
    # spare magazines in the reserve (conveyor-fed only if the contact pattern of Pod Chaser really connects: see docs)
    bp.preload(cargo, items=[Item.ammo('AutocannonClip', 12), Item.ammo('MediumCalibreAmmo', 10)])

    # cockpit (pilot) + remote control + the single Programmable Block ("Helm")
    cockpit = bp.add('MyObjectBuilder_Cockpit', 'DBSmallBlockFighterCockpit', (-1, 5, 10), 'Forward', 'Up', TAG + 'Cockpit [Helm Seat]', kind='cockpit')
    for c in bp.cells(cockpit): occ[c] = cockpit

    # ---- avionics bay under the cockpit: PB (front face on the bulkhead), remote control, dead-man timer ----
    pb = put('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', (0, 2, 12), '[Helm] Shrike', prefer=[('Forward', 'Up')], kind='ctrl',
             extra=pb_extra(_script('ShrikeAI.cs')) if _script('ShrikeAI.cs') else '', custom_data=CUSTOM_DATA if lite else CUSTOM_DATA_FULL)
    rc = put('MyObjectBuilder_RemoteControl', 'SmallBlockRemoteControl', (0, 2, 13), TAG + 'Remote Control', prefer=[('Forward', 'Up')], kind='ctrl')
    timer = put('MyObjectBuilder_TimerBlock', 'TimerBlockSmall', (-1, 2, 12), TAG + 'Timer D (deadman)', prefer=[('Forward', 'Up')], kind='ctrl',
                settings={'Delay': 5000, 'Silent': 'true'})

    # event controller for the dead-man's second layer (control_arch 2.6): placed and named, left UNCONFIGURED because the "Cockpit occupied"
    # event number has never been seen in a game file (test K4): set it in game (cockpit occupied -> Timer D Stop, empty -> Start)
    put('MyObjectBuilder_EventControllerBlock', 'EventControllerSmall', (1, 2, 12), TAG + 'EC Cockpit (configure in game)', prefer=[('Forward', 'Up')], kind='ctrl')

    # ---- power: two 1 MWh batteries (charged), two small reactors (uranium) ----
    for i, z in enumerate((22, 25)):
        put('MyObjectBuilder_BatteryBlock', 'SmallBlockBatteryBlock', (-1, 2, z), TAG + 'Battery %d' % (i + 1), prefer=[('Forward', 'Up')], kind='bat',
            state={'stored_power': 'full'})
    for tg, x in (('L', -1), ('R', 1)):
        put('MyObjectBuilder_Reactor', 'SmallBlockSmallGenerator', (x, 2, 29), TAG + 'Reactor ' + tg, prefer=[('Forward', 'Up')], kind='bat',
            items=[Item.ingot('Uranium', 5)])

    # ---- six gyros on top of the two batteries (their only mount is the Bottom face) ----
    for i, (x, z) in enumerate(((-1, 22), (0, 22), (1, 22), (-1, 25), (0, 25), (1, 25))):
        gyros.append(put('MyObjectBuilder_Gyro', 'SmallBlockGyro', (x, 4, z), TAG + 'Gyro %d' % (i + 1), prefer=[('Forward', 'Up')], kind='gyro'))

    # ---- antenna on the dorsal spine, six cameras all round ----
    put('MyObjectBuilder_RadioAntenna', 'SmallBlockRadioAntenna', (0, 9, 25), TAG + 'Antenna', prefer=[('Up', 'Backward')], strict=True, kind='ant',
        settings={'BroadcastRadius': 5000, 'ShowShipName': 'true'})
    cams = [('Front', (0, 3, 2), 'Forward', 'Up'), ('Rear', (0, 3, 34), 'Backward', 'Up'), ('Left', (-5, 8, 30), 'Left', 'Up'),
            ('Right', (5, 8, 30), 'Right', 'Up'), ('Up', (0, 9, 27), 'Up', 'Backward'), ('Down', (0, 0, 12), 'Down', 'Forward'),
            ('Front Top', (0, 6, 9), 'Forward', 'Up')]          # (M6) a second forward camera: ShrikeAI confirms the AI block's contact with the forward cameras and the nose one is shot off first
    for nm, mn, f, u in cams:
        for dz in (0, -2, -1, 1, -3, -4):                      # slide along the hull until the camera finds a flat face to stand on
            try:
                b = put('MyObjectBuilder_CameraBlock', 'SmallCameraBlock', (mn[0], mn[1], mn[2] + dz), TAG + 'Camera ' + nm, prefer=[(f, u)], strict=True, kind='cam')
                break
            except AssertionError:
                continue
        else:
            raise AssertionError('camera ' + nm)
        bp.keep_clear(b, 3, 'camera view')

    # ---- Full only: one rotor + hinge AI turret (PD2S, TurretAI.cs) on the dorsal spine behind the canopy; the small-grid stator shapes were never seen in a game file ----
    tmounts = []
    if not lite:
        m = TU.add_turret(bp, 1, TURRET_CELL, 'Up', fwd='Forward', model='PD2S', lite=lite, color=GUNCOL)
        for c in bp.cells(m.stator): assert c not in occ, f'{bp.name}: turret stator overlaps {occ[c]["name"] or occ[c]["sub"]}'
        for c in bp.cells(m.stator): occ[c] = m.stator
        tmounts.append(m)
        tpb = None
        for c in ((-1, 2, 13), (1, 3, 12), (-1, 3, 12), (0, 3, 13), (1, 3, 13), (-1, 3, 13), (0, 3, 12)):
            try:
                tpb = put('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', c, f'{TU.PREFIX} Turret PB', prefer=[('Forward', 'Up')], kind='ctrl',
                          extra=pb_extra(_script('TurretAI.cs')) if _script('TurretAI.cs') else '', custom_data='[Ship]\nTag=SH\n')
                break
            except AssertionError:
                continue
        assert tpb is not None, 'no cell for the turret PB'

    # ---- Full only: blocks whose XML shape is not a seen one (sensor, AI blocks, decoys) ----
    if not lite:
        for nm, mn, f, u in (('Sensor', (0, 0, 9), 'Down', 'Forward'),):
            for dz in (0, 1, -1, 2, -2, 3):
                try:
                    put('MyObjectBuilder_SensorBlock', 'SmallBlockSensor', (mn[0], mn[1], mn[2] + dz), TAG + nm, prefer=[(f, u)], strict=True, kind='sensor')
                    break
                except AssertionError:
                    continue
            else:
                raise AssertionError('sensor')
        put('MyObjectBuilder_OffensiveCombatBlock', 'SmallOffensiveCombat', (-1, 2, 14), TAG + 'AI Offensive [AI]', prefer=[('Forward', 'Up')], kind='ai')
        put('MyObjectBuilder_FlightMovementBlock', 'SmallFlightMovement', (1, 2, 14), TAG + 'AI Flight [AI]', prefer=[('Forward', 'Up')], kind='ai')
        for sx, tg in ((-1, 'L'), (1, 'R')):
            put('MyObjectBuilder_Decoy', 'SmallDecoy', (sx * 13, 6, 24), TAG + 'Decoy ' + tg, prefer=[('Forward', 'Up')], kind='ai')

    # ---- groups (names double as the toolbar targets) and toolbars ----
    bp.group(TAG + 'Thrusters', thr_blocks)
    bp.group(TAG + 'Weapons', weapons)
    bp.group('[B1]', b1)
    bp.group('[B2]', b2)
    bp.group(TAG + 'Gyros', gyros)
    # dead-man timer: cuts thrusters and guns (Helm re-arms it every second in AUTO and stops it in MANUAL)
    timer['toolbars'] = None
    bp.set_toolbars(timer, toolbar=[TB.group(TAG + 'Thrusters', 'OnOff_Off'), TB.group(TAG + 'Weapons', 'OnOff_Off')])
    # cockpit toolbar: hand weapons as in the owner's fighters (slots 1-3), thruster switch; Full adds the Helm commands (PB Run, unverified)
    tb = [TB.weapon('SmallMissileLauncherReload', 'SmallBlockMediumCalibreGun'), TB.weapon('SmallGatlingGun', 'SmallBlockAutocannon'),
          TB.weapon('SmallGatlingGun', ''), TB.group(TAG + 'Thrusters', 'OnOff')]
    items = dict(enumerate(tb))
    if not lite:
        for i, arg in enumerate(('auto', 'manual', 'assist hud', 'hunt', 'rtb', 'lock', 'next', 'unlock')):
            items[9 + i] = TB.run(pb, arg, experimental=True)
    bp.set_toolbars(cockpit, toolbar=items)
    cockpit['settings']['IsMainCockpit'] = 'true'
    if tmounts:
        tu_check = TU.check_mounts(bp, tmounts, min_cover=0.25)
        tpb['custom_data'] = TU.turret_custom_data(bp, tmounts, 'SH')
    else:
        tu_check = {}
    return dict(occ=occ, thr=thr_blocks, weapons=weapons, b1=b1, b2=b2, cockpit=cockpit, pb=pb, rc=rc, timer=timer, gyros=gyros, tmounts=tmounts, tu_check=tu_check)


def build(lite=False):
    """-> (Blueprint, HullResult, systems dict)"""
    name = NAME + (' Lite' if lite else '')
    bp = Blueprint(name, 'Small', seed=seed_for(name))
    res = build_hull(bp)
    sysd = add_systems(bp, res, lite)
    return bp, res, sysd


def shrike(lite=False):
    bp, res, _ = build(lite)
    res.check()
    link_conveyors(bp)
    return finalize(bp)


def shrike_shipped(lite=False):
    """the blueprint that ships: one blank PB reserved for a Staff half (FLEET LINK receive + AI GATE leave ShrikeAI, 86 k minified; docs/design/pb_headroom.md).  shrike() stays as it was for the tests and the warship modules."""
    bp = shrike(lite)
    spares.add_spare_pbs(bp, total=spares.pb_count(bp) + 1, model=NAME + (' Lite' if lite else ''))
    return bp

MODELS = {NAME: shrike_shipped}
DOCS = {NAME: ('Delta-wing fighter (small grid, 17 m): one pilot in a fighter cockpit, one Programmable Block "[Helm] Shrike" (ShrikeAI.cs), Remote Control, '
               'two autocannons + two gatlings (battery [B1]) and an assault cannon ([B2]) fixed forward, eight large hydrogen thrusters on three full tanks '
               '(16 m/s2 forward and back, 8 m/s2 up / down / sideways), six gyros, two charged batteries, two uranium reactors, six cameras, antenna, '
               'dead-man timer. No drone bay. Limitation: the autocannons hold 3 magazines (19 s of fire) and the assault cannon 3 shells; the 12 + 10 spare '
               'magazines in the nose container touch the guns exactly as the owner\'s Pod Chaser layout does, but whether that feeds them is untested in game.')}


def figures(bp):
    """numbers for the doc and the report: mass (empty / loaded with magazines + uranium), acceleration (C.twr(), empty and loaded), centre of mass
    (cells, edge coordinates of the pre-loaded ship), PCU, HP, dimensions, the pre-loaded rounds per gun"""
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


# ---------------------------------------------------------------------------------------------------------------
# renders and deck plans (docs/design/ships/Shrike.md)
# ---------------------------------------------------------------------------------------------------------------
def render(bp, outdir, stem='Shrike'):
    """PNG views with the repo's own thumbnail / orthographic code (tools/hull_demo.py): four yaw views from above and from below, the
    top / side / front silhouettes and zoomed views of the nose, a pod and the tail.  Returns the file names."""
    import hull_demo as HD
    from PIL import Image
    os.makedirs(outdir, exist_ok=True)
    HD.PAL.update(PAL)

    class Sub:
        def __init__(self, keep):
            self.blocks = [b for b in bp.blocks if keep(bp.cells(b))]
        def cells(self, b):
            return bp.cells(b)

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
    everything = lambda cs: True
    files = [sheet('top', everything, 0), sheet('under', everything, 2),
             sheet('nose', lambda cs: all(c[2] <= 17 and abs(c[0]) <= 6 for c in cs), 0),
             sheet('pod', lambda cs: all(c[0] >= 4 and 10 <= c[2] <= 29 for c in cs), 0),
             sheet('podunder', lambda cs: all(c[0] >= 4 and 10 <= c[2] <= 29 for c in cs), 2),
             sheet('tail', lambda cs: all(c[2] >= 22 for c in cs), 0)]
    files.append(HD.render_ortho(bp, outdir, stem))
    return files


_LETTER = {'Cockpit': 'C', 'Thrust': 'T', 'OxygenTank': 'K', 'SmallGatlingGun': 'G', 'SmallMissileLauncherReload': 'G', 'BatteryBlock': 'B',
           'Reactor': 'R', 'Gyro': 'y', 'MyProgrammableBlock': 'P', 'RemoteControl': 'r', 'TimerBlock': 't', 'EventControllerBlock': 'e',
           'CameraBlock': 'c', 'RadioAntenna': 'a', 'CargoContainer': 'M', 'Decoy': 'd', 'SensorBlock': 's', 'OffensiveCombatBlock': 'i',
           'FlightMovementBlock': 'i'}


def plans(bp):
    """ASCII deck plans (side x = 0, plans y = 5 / 3 / 2, sections z = 12 / 18) as one string; # armour, w window, p panel, other letters = systems"""
    occ = {c: b for b in bp.blocks for c in bp.cells(b)}

    def ch(b):
        if b is None:
            return '.'
        x = b['xtype'].replace('MyObjectBuilder_', '')
        if x == 'CubeBlock':
            return 'w' if 'Window' in b['sub'] else 'p' if 'Panel' in b['sub'] else '#'
        return _LETTER.get(x, '?')
    out = []
    out.append('SIDE x=0 (columns z, rows y)')
    out.append('     ' + ''.join(str(z // 10 % 10) if z % 10 == 0 else ' ' for z in range(35)))
    out.append('     ' + ''.join(str(z % 10) for z in range(35)))
    for y in range(10, -2, -1):
        out.append('y%3d ' % y + ''.join(ch(occ.get((0, y, z))) for z in range(35)))
    for y in (5, 3, 2):
        out.append('')
        out.append(f'PLAN y={y} (columns x, rows z)')
        out.append('     ' + ''.join(str(abs(x) % 10) for x in range(-15, 16)))
        for z in range(35):
            out.append('z%3d ' % z + ''.join(ch(occ.get((x, y, z))) for x in range(-15, 16)))
    for z in (12, 18):
        out.append('')
        out.append(f'SECTION z={z} (columns x, rows y)')
        out.append('     ' + ''.join(str(abs(x) % 10) for x in range(-15, 16)))
        for y in range(11, -3, -1):
            out.append('y%3d ' % y + ''.join(ch(occ.get((x, y, z))) for x in range(-15, 16)))
    return '\n'.join(out)


def main(argv):
    """python3 -I tools/designs/shrike.py [--render DIR] [--plans]   (no option: build + validate both variants)"""
    if '--plans' in argv:
        print(plans(shrike(False)))
        return 0
    if '--render' in argv:
        d = argv[argv.index('--render') + 1]
        for f in render(shrike(False), d):
            print(f)
        return 0
    return C.run_module(MODELS)


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
