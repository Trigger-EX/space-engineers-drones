"""Cradle pods: a small-grid frame with merge-block cradles for Hornet-class drones, docked to a large ship by two connector pairs.

Models: 'Cradle Pod' / 'Cradle Pod Lite' (the 4-slot pod alone).  The Warden docks one (designs/warden.py), the Leviathan docks two.
Design record: docs/design/ships/Pod.md (geometry, numbers, paste risks).  Concept: docs/design/grids_bays.md sections 5.1, 5.4.

PUBLIC API (stable; append-only)
--------------------------------
cradle_pod(n_slots=4, lite=False, name=None, tag='POD', ship_tag='WARDEN', script=None, uranium_kg=4.0, conn_spacing=None, first_bay=1) -> sebp.Blueprint
    (n_slots 2..8)  A SMALL-grid Blueprint named `name` (default 'Cradle Pod' / 'Cradle Pod Lite').  Pod frame: x = the row of slots (Right), y = Up, z = Backward,
    the drones' noses point to -z (Forward), which is the LAUNCH direction.  Cell size 0.5 m.  `bp.pod` is a PodInfo (below).
    Blocks: n merge blocks (host halves of the cradles), 2 ConnectorMedium (rear face, Forward = +z), the Pod PB (script CradleBayAI.cs when the
    file exists, else an empty program), antenna, 2 batteries (full), 2 small reactors (uranium), n backup timers (toolbar: merge block Off),
    frame armour (nose ring, mid ribs, corner rails, 2-cell rear wall with pads).  Every block name starts with `[{tag}]`
    (the pod's PB is `[Pod] Pod PB`), never `[SH]`, so the host ship's Helm/Staff never adopt a pod block.
PodInfo  (bp.pod): .n, .tag, .pitch, .width, .depth, .height, .slots[k] -> SlotInfo(merge, merge_cell, drone_origin, box_min, box_max, away),
    .connectors (the two ConnectorMedium blocks), .conn_spacing (small cells between the two connector centres), .pb, .timers, .dims_m,
    .lane (box_min, box_max of the open lane in front of the nose, pod cells), .rear (z of the connectors' contact face edge)
drone_pose(pod, k, drone_bp) -> sebp_multi.Pose            pose of a drone grid (merged to slot k) in the pod frame
attach_drone(pod, k, drone_bp, check=True) -> Pose         pod.attach(drone) + clearance check: no pod block within one cell of a drone cell except the merge pair
check_clearance(pod)                                       raises AssertionError listing the pod blocks that touch a docked drone
dock_pod(host, pod, host_connectors, host_face='Forward', roll=None, lock=False, gap=PAIR_GAP) -> Pose
    host.dock(pod, ...) with BOTH connector pairs checked: the second pair's contact faces must meet to 2 mm, normals opposed.  `host_connectors` =
    two Large-grid `Connector` blocks with the same spacing as the pod's pair; `roll` chooses the turn about the connector axis (None: the first
    that is free of the host hull and has the lane closest to the host's Down).
hangar_footprint(pod) -> dict                               what a host hangar must provide (large cells): width along the row, depth, clearance, connector spacing
orient_attached(bp, xtype, sub, mn, toward=None, prefer=(...), **add_kwargs) -> block      place any block with the orientation whose mount faces meet a neighbour
slot_report(pod) -> str                                    one line per slot (cells, boxes) for docs
assert_clean(bp) -> bp                                     asserts no DLC and no Prototech block in any grid (also run by cradle_pod() and the ships)

Geometry (small cells; drones 7 x 6 x 9 cells = Hornet, Wasp, Watcher, Harrier (8 long) and Lancer, Aegis (9 long)): slot pitch 9 along x (2 cells
between neighbouring drones), free box 9 x 8 x 9 around each drone, drone merge cell (xc, 3, 9), host merge (xc, 3, 10), rear wall z = 11..12, connectors
z = 13..14.  Midge Full (10 long) and the Gnat (belly merge) do not fit (documented).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import sebp_multi as MU
import sebp_toolbar as TB
from sebp_inv import Item
import hulls as H

ROOT = C.ROOT
PITCH = 9                      # cells between slot centres (drone 7 wide + 2 clearance)
Y0 = 3                         # merge height (drone y -2..3 -> pod y 1..6)
ZD, ZH, ZW = 9, 10, 11         # drone merge cell, host merge cell, first rear wall layer (wall = ZW, ZW + 1)
CONN_Z = 13                    # connectors z 13..14 (ConnectorMedium 3 x 3 x 2)
DEPTH = 15                     # cells z 0..14
BOX_X, BOX_Y, BOX_Z = 7, 6, 9  # drone envelope
PAIR_GAP = 0.07                # metres between the two connector faces (the owner's Venture lock: 0.0707 m)

CHARCOAL = (0.60, -0.70, -0.42)
STEEL = (0.60, -0.74, -0.30)
ORANGE_ = (0.08, 0.15, 0.0)
DARKM = (0.0, -0.81, -0.50)

def _script(name):
    return script_text(name) if os.path.exists(os.path.join(ROOT, 'scripts', name)) else ''

class SlotInfo(dict):
    __getattr__ = dict.__getitem__
class PodInfo(dict):
    __getattr__ = dict.__getitem__

def assert_clean(bp):
    """the brief's block rules for every grid of `bp`: defs.json says no DLC, and none of the 14 Prototech blocks (they carry no DLC flag)"""
    for g, _ in bp.grids():
        for b in g.blocks:
            d = sebp.DEFS[b['key']]
            assert not d['dlc'], f'{g.name}: {b["key"]} needs DLC {d["dlc"]}'
            assert 'Prototech' not in b['key'], f'{g.name}: {b["key"]} is a Prototech block'
    return bp

# ----------------------------------------------------------------------------------------------------------
# placing blocks by their mount faces
# ----------------------------------------------------------------------------------------------------------
_ORIENTS = [(f, u) for f in DIR for u in DIR if sum(a * b for a, b in zip(DIR[f], DIR[u])) == 0]

def _attached_to(bp, b, others):
    """True when a mount rectangle of b meets a mount rectangle of any block in `others` (the game's rule, sebp.attachment)"""
    rs = bp.mount_rects(b)
    for o in others:
        for (a2, pc2, ns2, lo2, hi2, ex2, pr2) in bp.mount_rects(o):
            for (axis, pc, ns, lo, hi, ex, pr) in rs:
                if axis == a2 and abs(pc - pc2) < 1e-6 and ns == -ns2 and lo[0] <= hi2[0] and lo2[0] <= hi[0] and lo[1] <= hi2[1] and lo2[1] <= hi[1]:
                    if ((ex & pr2) or (pr & ex2)) and b['key'] != o['key']: continue
                    return True
    return False

def orient_attached(bp, xtype, sub, mn, toward=None, prefer=(('Forward', 'Up'),), occ=None, **kw):
    """Add a block whose (Forward, Up) is the first of `prefer`, then of all 24 orientations, for which it (a) does not overlap an existing block, (b) does not
    sit in a reserved (keep-clear) cell, (c) has a mount rectangle that meets a neighbour's, and (d) when `toward` (a direction name) is given, is attached
    through the face looking that way.  Raises AssertionError when none fits.  `occ` may be passed to skip the occupancy rebuild (dict cell -> block)."""
    cands = list(prefer) + [o for o in _ORIENTS if o not in prefer]
    occ = bp.occupancy() if occ is None else occ
    last = None
    dirs = [toward] if toward else list(DIR)
    for f, u in cands:
        b = bp.add(xtype, sub, mn, f, u, **kw)
        cells = bp.cells(b)
        bad = [c for c in cells if c in occ or c in bp.reserve]
        ok = False
        if not bad:
            cs = set(cells)
            nb = {id(occ[q]): occ[q] for q in (tuple(c[i] + DIR[d][i] for i in range(3)) for c in cells for d in dirs) if q in occ and q not in cs}
            ok = bool(nb) and _attached_to(bp, b, list(nb.values()))
        if ok:
            return b
        bp.blocks.pop()
        last = (f, u, bad)
    raise AssertionError(f'{bp.name}: no orientation attaches {sub} at {mn} (toward={toward}); last try {last}')

# ----------------------------------------------------------------------------------------------------------
def _rect_ring(x0, x1, y0, y1):
    out = set()
    for x in range(x0, x1 + 1):
        out.add((x, y0)); out.add((x, y1))
    for y in range(y0, y1 + 1):
        out.add((x0, y)); out.add((x1, y))
    return out

def _conn_spacing(n):
    """allowed spacing (small cells) between the two connector centres: a multiple of 5 (the host grid's 2.5 m cell) with the right parity so the
    connectors sit on cells: n even -> odd multiples of 5, n odd -> even multiples of 5; as wide as the frame allows (>= 5)"""
    width = PITCH * n
    best = 5 if n % 2 == 0 else 10
    for s in range(5, width, 5):
        if (s // 5) % 2 == (1 if n % 2 == 0 else 0) and s <= width - 11: best = max(best, s)
    return best

def cradle_pod(n_slots=4, lite=False, name=None, tag='POD', ship_tag='WARDEN', script=None, uranium_kg=4.0, conn_spacing=None, first_bay=1):
    n = int(n_slots)
    assert 2 <= n <= 8, 'a pod holds 2..8 slots'
    name = name or ('Cradle Pod Lite' if lite else 'Cradle Pod')
    bp = Blueprint(name, 'Small', seed=seed_for(name))
    W = PITCH * n
    P = f'[{tag}]'
    X0, X1 = -1, W                      # frame x range (inclusive)
    Yb, Yt = -2, 8                      # frame y range (11 cells: centred on the connectors, y = 3)
    S = conn_spacing or _conn_spacing(n)
    assert (n % 2 == 0 and (S // 5) % 2 == 1 and S % 5 == 0) or (n % 2 == 1 and (S // 5) % 2 == 0 and S % 5 == 0), f'connector spacing {S} off the host lattice'
    mid = (W - 1) / 2.0
    cx = [int(mid - S / 2.0), int(mid + S / 2.0)]          # connector centre cells
    xc = [4 + PITCH * k for k in range(n)]                 # slot centre columns
    frame = {}                                             # cell -> colour

    def put(c, col):
        frame.setdefault(c, col)
    # nose ring (the launch mouth) and mid rib
    for z, col in ((0, ORANGE_), (6, CHARCOAL)):
        for (x, y) in _rect_ring(X0, X1, Yb, Yt): put((x, y, z), col)
    # corner rails
    for x in (X0, X1):
        for y in (Yb, Yt):
            for z in range(1, ZW): put((x, y, z), CHARCOAL)
    # rear wall: ring, centre bar y = Y0, posts between slots, pads behind every merge block and connector
    for z in (ZW, ZW + 1):
        col = STEEL if z == ZW else CHARCOAL
        for (x, y) in _rect_ring(X0, X1, Yb, Yt): put((x, y, z), col)
        for x in range(X0, X1 + 1): put((x, Y0, z), col)
        for k in range(n - 1):
            for y in range(Yb, Yt + 1): put((PITCH * k + 8, y, z), col)
        for cxx in xc + cx:
            for x in range(cxx - 1, cxx + 2):
                for y in range(Y0 - 1, Y0 + 2): put((x, y, z), ORANGE_ if cxx in xc else STEEL)
    for c, col in sorted(frame.items(), key=lambda kv: (kv[0][2], kv[0][1], kv[0][0])):
        bp.armor(c[0], c[1], c[2], col)

    # ---- merge blocks (host half of each cradle): clamp face toward the drone (-z) ----
    slots = []
    merges = []
    for k in range(n):
        m = merge_small(bp, (xc[k], Y0, ZH), 'Forward', f'{P} Merge {k + 1}')
        m['kind'] = 'cradle'      # the HOST half of a cradle: not a drone merge latch (no printer docking check, no 'merge' count)
        merges.append(m)
        slots.append(SlotInfo(index=k, merge=m, merge_cell=(xc[k], Y0, ZH), drone_merge_cell=(xc[k], Y0, ZD),
                              drone_origin=(xc[k], Y0, ZD - 3),
                              box_min=(xc[k] - 3, Y0 - 2, ZD - 8), box_max=(xc[k] + 3, Y0 + 3, ZD),
                              free_min=(xc[k] - 4, Y0 - 3, ZD - 9), free_max=(xc[k] + 4, Y0 + 4, ZD), away=(0, 0, -1)))

    # ---- connectors: ConnectorMedium on the rear face, contact face Forward = +z ----
    conns = []
    for i, x in enumerate(cx):
        b = bp.add('MyObjectBuilder_ShipConnector', 'ConnectorMedium', (x - 1, Y0 - 1, CONN_Z), 'Backward', 'Up', f'{P} Connector {"AB"[i]}', color=STEEL, kind='conn')
        conns.append(b)

    # ---- devices on the rear face (z = 13): batteries on the centre bar (y = 3), singles on the bar, then along the top and bottom rails ----
    bar_x = [x for x in range(X0 + 2, X1 - 1) if not any(abs(x - c) <= 2 for c in cx)]
    occ = bp.occupancy()
    xs = list(bar_x)
    rails = [(x, Yt) for x in range(X0 + 1, X1)] + [(x, Yb) for x in range(X0 + 1, X1)]

    def take(width=1):
        """first run of `width` free consecutive x on the bar"""
        for i in range(len(xs) - width + 1):
            run = xs[i:i + width]
            if all(run[j] + 1 == run[j + 1] for j in range(width - 1)):
                del xs[i:i + width]
                return run
        raise AssertionError('pod rear bar is full')

    def single():
        """the next free cell for a 1 x 1 x 1 device: the bar first, then the rails"""
        if xs: return (xs.pop(0), Y0)
        if rails: return rails.pop(0)
        raise AssertionError('pod rear face is full')

    def dev(xtype, sub, nm_, **kw):
        x, y = single()
        b = orient_attached(bp, xtype, sub, (x, y, CONN_Z), toward='Forward', occ=occ, name=f'{P} {nm_}', **kw)
        for c in bp.cells(b): occ[c] = b
        return b
    # batteries are 3 x 2 x 3 (turned to 3 x 3 x 2): the first two 3-wide runs of the bar
    bats = []
    for i in range(2):
        run = take(3)
        b = orient_attached(bp, 'MyObjectBuilder_BatteryBlock', 'SmallBlockBatteryBlock', (run[0], Y0 - 1, CONN_Z), toward='Forward', occ=occ,
                            prefer=(('Up', 'Backward'),), name=f'{P} Battery {i + 1}', extra=bat_extra(1), kind='bat', state={'stored_power': 'full'})
        for c in bp.cells(b): occ[c] = b
        bats.append(b)
    cd = ('[Pod]\n' f'Tag={ship_tag}\nSlots={n}\nFirst Bay={first_bay}\nPulse=Update10\n'
          + ''.join(f'\n[Bay {k + 1}]\nMerge={P} Merge {k + 1}\nDrone Box Min={s.box_min[0]},{s.box_min[1]},{s.box_min[2]}\nDrone Box Max={s.box_max[0]},{s.box_max[1]},{s.box_max[2]}\n'
                    f'Away=0,0,-1\nTimer={P} Timer R{k + 1}\n' for k, s in enumerate(slots)))
    pb = dev('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', 'Pod PB', kind='ctrl', custom_data=cd,
             extra=(lambda t: pb_extra(t) if t else '')(script if script is not None else _script('CradleBayAI.cs')))
    pb['name'] = '[Pod] Pod PB'
    ant_b = dev('MyObjectBuilder_RadioAntenna', 'SmallBlockRadioAntenna', 'Antenna', extra=ant_extra(5000), kind='ant')
    gens = [dev('MyObjectBuilder_Reactor', 'SmallBlockSmallGenerator', f'Reactor {i + 1}', kind='reactor', items=[Item.ingot('Uranium', uranium_kg)]) for i in range(2)]
    # backup timers: one per slot, toolbar = merge block off (OnOff_Off is a seen action)
    timers = []
    for k in range(n):
        b = dev('MyObjectBuilder_TimerBlock', 'TimerBlockSmall', f'Timer R{k + 1}', kind='timer')
        b['settings'] = {'Delay': 3000, 'Silent': True}
        bp.set_toolbars(b, toolbar=[TB.block(merges[k], 'OnOff_Off')])
        timers.append(b)
    # remote control (script reference frame for the pod; the pod can be flown as a ship only with thrusters, which it does not carry)
    rc = dev('MyObjectBuilder_RemoteControl', 'SmallBlockRemoteControl', 'Remote Control', kind='ctrl')

    info = PodInfo(n=n, tag=tag, pitch=PITCH, width=W + 2, depth=DEPTH, height=Yt - Yb + 1, slots=slots, connectors=conns, conn_spacing=S, conn_cells=cx,
                   pb=pb, timers=timers, batteries=bats, reactors=gens, antenna=ant_b, rc=rc, merges=merges,
                   dims_m=((W + 2) * 0.5, (Yt - Yb + 1) * 0.5, DEPTH * 0.5), rear=CONN_Z + 2,
                   lane=((X0, Yb, -24), (X1, Yt, -1)), x_range=(X0, X1), y_range=(Yb, Yt))
    bp.pod = info
    bp.display = name
    assert_clean(bp)
    return C.finalize(bp)

# ----------------------------------------------------------------------------------------------------------
# drones in the cradles
# ----------------------------------------------------------------------------------------------------------
def _merge_of(drone):
    ms = [b for b in drone.blocks if b['kind'] == 'merge']
    assert len(ms) == 1, f'{drone.name}: {len(ms)} merge blocks (a cradle drone has exactly one)'
    return ms[0]

def drone_pose(pod, k, drone_bp):
    """pose of `drone_bp` in the pod frame when its merge block is clamped to the pod's merge block of slot k (drone upright, nose -z)"""
    host = pod.pod.slots[k]['merge']
    dm = _merge_of(drone_bp)
    cands = MU.dock_candidates(pod, host, drone_bp, dm, parent_face=MERGE_CLAMP_FACE_SMALL, child_face=MERGE_CLAMP_FACE_SMALL, gap=0.0)
    assert cands, 'no axis-aligned pose'
    for p in cands:       # upright and nose toward -z
        if p.rot.axis_names() == ('Forward', 'Up'): return p
    return cands[0]

def check_clearance(pod):
    """no pod block may touch a docked drone (26-neighbourhood) except the host merge block meeting the drone's merge block"""
    bad = []
    pod_cells = {}
    for b in pod.blocks:
        for c in pod.cells(b): pod_cells[c] = b
    for drone, pose in pod.subgrids:
        if drone.grid != 'Small': continue
        dm = [b for b in drone.blocks if b['kind'] == 'merge']
        d_cells = {}
        for b in drone.blocks:
            for c in drone.cells(b):
                w = pose.apply(tuple(v * 0.5 for v in c))
                pc = tuple(int(round(v / 0.5)) for v in w)
                assert all(abs(pc[i] * 0.5 - w[i]) < 1e-6 for i in range(3)), f'{drone.name}: drone cells are off the pod lattice'
                d_cells[pc] = b
        for pc, b in d_cells.items():
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    for dz in (-1, 0, 1):
                        q = (pc[0] + dx, pc[1] + dy, pc[2] + dz)
                        o = pod_cells.get(q)
                        if o is None: continue
                        if b['kind'] == 'merge' and o['kind'] == 'cradle': continue
                        bad.append((drone.name, b['name'] or b['sub'], pc, o['name'] or o['sub'], q))
    assert not bad, f'{pod.name}: pod blocks touch a drone: {bad[:4]} (+{max(0, len(bad) - 4)})'

def attach_drone(pod, k, drone_bp, check=True):
    """clamp `drone_bp` into slot k of `pod`: adds it as a sub-grid at the exact merge pose (merge blocks one cell apart, lattices aligned)"""
    pose = drone_pose(pod, k, drone_bp)
    hit = MU.overlapping_cells(pod, drone_bp, pose)
    assert not hit, f'{drone_bp.name} overlaps the pod in {len(hit)} cells (first {hit[0]})'
    pod.attach(drone_bp, pose)
    drone_bp.display = f'{drone_bp.name} (slot {k + 1})'
    if check: check_clearance(pod)
    return pose

# ----------------------------------------------------------------------------------------------------------
# docking to a host
# ----------------------------------------------------------------------------------------------------------
def hangar_footprint(pod):
    """what the host must provide, in LARGE cells (2.5 m): span along the slot row, thickness, depth below the connector face and the connector spacing"""
    p = pod.pod
    import math
    return dict(row_cells=math.ceil(p.width * 0.5 / 2.5), thickness_cells=math.ceil(p.height * 0.5 / 2.5), depth_cells=math.ceil(p.depth * 0.5 / 2.5),
                connector_spacing_cells=p.conn_spacing / 5.0, row_m=p.width * 0.5, thickness_m=p.height * 0.5, depth_m=p.depth * 0.5,
                launch_lane_m=12.0)

def _contact(bp_, b, world):
    pt, n = MU.contact_point(bp_, b, 'Forward', 0.0)
    return world.apply(pt), world.rot.apply(n)

def dock_pod(host, pod, host_connectors, host_face='Forward', roll=None, lock=False, gap=PAIR_GAP, want_nose='Down'):
    """dock `pod` under `host` with its two ConnectorMedium blocks on two large Connector blocks of the host.  Pair A is docked with sebp's dock();
    pair B must then meet to 2 mm.  roll=None picks the first of the four rolls about the connector axis whose pod nose (-z of the pod) points to
    `want_nose` (a host direction name) and whose slot row runs along the host's z axis."""
    hc = list(host_connectors)
    assert len(hc) == 2, 'two host connectors'
    pc = pod.pod.connectors
    cands = MU.dock_candidates(host, hc[0], pod, pc[0], parent_face=host_face, child_face='Forward', gap=gap)
    assert cands, 'no docking pose'
    want = MU.DIRS[want_nose]
    order = list(range(len(cands))) if roll is None else [roll % len(cands)]
    errs = []
    for i in order:
        pose = cands[i]
        nose = pose.rot.apply(MU.DIRS['Forward'])           # pod's -z in the host frame
        if roll is None and MU._dot(nose, want) < 0.99: continue
        host.attach(pod, pose)
        host._world = host.pose
        # check pair B
        w_host, w_pod = host.pose, host.pose * pose
        a, na = _contact(host, hc[1], w_host)
        b, nb = _contact(pod, pc[1], w_pod)
        d = MU._norm(MU._sub(a, b))
        gap_b = abs(MU._dot(MU._sub(b, a), na))
        lat = MU._norm(MU._sub(MU._sub(b, a), MU._mul(na, MU._dot(MU._sub(b, a), na))))
        if lat < 0.002 and abs(gap_b - gap) < 0.002 and MU._norm(MU._add(na, nb)) < 1e-6:
            hit = MU.overlapping_cells(host, pod, pose)
            if not hit:
                if lock:
                    host.lock_connectors(hc[0], pod, pc[0])
                    host.lock_connectors(hc[1], pod, pc[1])
                return pose
            errs.append(f'roll {i}: {len(hit)} pod cells inside the host')
        else:
            errs.append(f'roll {i}: second pair misses by lateral {lat * 1000:.0f} mm, gap {gap_b * 1000:.0f} mm')
        host.subgrids.pop()
    raise AssertionError('dock_pod: no roll docks both connector pairs: ' + '; '.join(errs))

def slot_report(pod):
    p = pod.pod
    lines = [f'{pod.name}: {p.n} slots, frame {p.width} x {p.height} x {p.depth} cells = {p.dims_m[0]:.1f} x {p.dims_m[1]:.1f} x {p.dims_m[2]:.1f} m']
    for s in p.slots:
        lines.append(f'  slot {s.index + 1}: merge cell {s.merge_cell}, drone box {s.box_min}..{s.box_max}, free box {s.free_min}..{s.free_max}')
    lines.append(f'  connectors at x = {p.conn_cells}, spacing {p.conn_spacing} cells = {p.conn_spacing * 0.5} m')
    return '\n'.join(lines)

# ----------------------------------------------------------------------------------------------------------
DOCS = {'Cradle Pod': 'Small-grid 4-slot drone cradle (merge blocks), Pod PB, two ConnectorMedium, batteries and reactors, backup timers; docks into the Warden / Leviathan hangars. Empty: drones are added by pasting them touching the merge blocks (designs/pods.py attach_drone). No propulsion.'}
MODEL_NOTES_POD = 'Paste next to a large hangar and dock the two connectors (or paste the Warden, which carries it docked). Merge-block clamp face and the connector lock at paste are unverified (docs/design/ships/Pod.md, C1..C4).'
SCRIPTS = ['CradleBayAI.cs']

def _model(lite):
    return cradle_pod(4, lite=lite)

MODEL_NOTES = {'Cradle Pod': MODEL_NOTES_POD}
MODELS = {'Cradle Pod': _model}

if __name__ == '__main__':
    sys.exit(C.run_module(MODELS, min_acc=0.0))
