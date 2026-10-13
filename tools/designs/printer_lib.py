"""Printer building blocks shared by the printer modules (owned by the scripted-printer agent).

CONCEPTS
  Frame      Everything is built in ONE integer cell frame.  The drone Blueprint is placed in the printer frame with a pure
             TRANSLATION T (no rotation): the projector block has the identity orientation and ProjectionRotation is 0,0,0, so the
             drone's grid axes equal the printer's grid axes.  (Rotation would need Keen's yaw/pitch/roll mapping, see below.)
  Bay        projector [Bay N] + SmallShipWelder(s) [Bay N] + merge block [Bay N], plus the armour scaffold that carries them.
             The printer merge block sits in the cell the drone merge block clamps toward; the clamp faces oppose.
  Solver     `solve_bay_auto(drone)` finds welder positions (weighted set cover over (block, sphere hypothesis) pairs, hypothesis family in
             reach.py, numpy candidate generation, randomised restarts + repair), keeps every
             printer cell out of the drone's 26-neighbourhood and clear zones, routes armour from each welder's mounting face to
             the merge stalk and places the projector.  Results are cached per drone model.

PROJECTION OFFSET / ROTATION (verified against the user's Starburst ship, which holds a projector with a loaded projection AND the
drone welded from it: all 7 drone blocks and the translation fit EXACTLY):
  Keen's MyProjectorClipboard.UpdateGridTransformations: worldMatrix = R * projector.WorldMatrix (R = ProjectionRotationQuaternion
  = CreateFromYawPitchRoll(rot.X*90deg, rot.Y*90deg, rot.Z*90deg), i.e. the X component turns about the block's UP axis);
  the FIRST block of the projected grid (CubeBlocks.First(), its ob `Min`) is moved onto  projector_cell - O(ProjectionOffset)  where
  O is the projector block's orientation (block axes -> grid axes) and the offset is in cells of the projected grid.
  With identity orientation and zero rotation:   first_block_cell_in_printer = projector_cell - ProjectionOffset.
  => ProjectionOffset = projector_cell - (first_block_min + T).   The first block of the embedded grid is therefore always a
  1x1x1 armour cube (its Min is its position).  Starburst: offset (-5,12,-16), rotation (2,0,0), projector Forward=Down/Up=Forward,
  first block min (0,2,0)  ->  real welded copy at L(p)+(5,17,14) which equals projector_cell - O(offset) - L(first); checked.

WELDING INTO THE PROJECTOR GRID: RemainingBlocks counts projected blocks that have no real block at that position of the PROJECTOR's
  grid, so welded drone blocks join the PRINTER's grid.  Separation by switching the merge pair off therefore only works if the game
  splits the grid at the merge pair; this is UNVERIFIED (test P1 in docs/printers.md).  HiveMotherAI.cs checks it at run time.
"""
import sys, os, copy, itertools, collections, zlib
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import reach
from reach import neighbours26, eff_radius_cells, cell_dist, tip_point_from_cells, CELL_M, FAMILY as FAM
NH = len(FAM)
RED_IDX = FAM.index(reach.RED_HYP)
import sebp
from sebp import Blueprint, DIR, E, ser, frag, cross, neg
import xml.etree.ElementTree as ET

VEC2NAME = {v: k for k, v in DIR.items()}
ARMOR_DARK = (0.0, -0.81, -0.55)
PRINTER_COLOR = (0.58, -0.78, -0.25)
BAY_COLOR = (0.07, 0.55, 0.10)
WELD_COLOR = (0.12, 0.75, 0.05)

def add3(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub3(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul3(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def perp_axes(d):
    ax = [abs(v) for v in d].index(1)
    o = [k for k in range(3) if k != ax]
    u = [0, 0, 0]; v = [0, 0, 0]; u[o[0]] = 1; v[o[1]] = 1
    return tuple(u), tuple(v)

# ---------------------------------------------------------------------------------------------
# welder geometry
# ---------------------------------------------------------------------------------------------
def welder_cells(C0, d):
    """cells of a SmallShipWelder whose TIP layer is centred on cell C0 and which works along unit vector d"""
    u, v = perp_axes(d)
    out = []
    for m in range(6):
        for a in (-1, 0, 1):
            for b in (-1, 0, 1):
                out.append(tuple(C0[k] - m * d[k] + a * u[k] + b * v[k] for k in range(3)))
    return out

def welder_orientation(d):
    fwd = VEC2NAME[d]
    for up in ("Up", "Forward", "Right"):
        if sum(a * b for a, b in zip(DIR[up], d)) == 0: return fwd, up
    raise AssertionError(d)

def welder_block_args(C0, d):
    cells = welder_cells(C0, d)
    mn = tuple(min(c[k] for c in cells) for k in range(3))
    fwd, up = welder_orientation(d)
    return mn, fwd, up

_OFFS = {}
def welder_offsets(d):
    """(cell offsets from the tip-centre cell C0, attach offsets from C0, (fwd, up), min offset) for tip direction d; cached"""
    if d not in _OFFS:
        offs = welder_cells((0, 0, 0), d)
        mn, fwd, up = welder_block_args((0, 0, 0), d)
        att = [add3(mn, o) for o in welder_attach_offsets(fwd, up)]
        _OFFS[d] = (offs, att, (fwd, up), mn)
    return _OFFS[d]

_ATT = {}
def welder_attach_offsets(fwd, up):
    """cell offsets (relative to the welder's Min) where an armour cube attaches to the welder through real mount points"""
    key = (fwd, up)
    if key in _ATT: return _ATT[key]
    bp = Blueprint('t', 'Small')
    w = bp.add("MyObjectBuilder_ShipWelder", reach.WELDER_SUB, (0, 0, 0), fwd, up, 'w')
    cells = set(bp.cells(w)); adj = set()
    for c in cells:
        for d in DIR.values():
            n = add3(c, d)
            if n not in cells: adj.add(n)
    arms = {}
    for n in sorted(adj): arms[n] = bp.armor(*n)
    att = bp.attachment()
    res = {n for n, b in arms.items() if id(b) in att[id(w)]}
    _ATT[key] = res
    return res

# ---------------------------------------------------------------------------------------------
# bay solver
# ---------------------------------------------------------------------------------------------
class BayDesign:
    """printer-side blocks for one drone, in the drone frame (T = 0)."""
    def __init__(self, drone):
        self.drone = drone
        self.dblocks = [(b, [tuple(c) for c in drone.cells(b)]) for b in drone.blocks]
        self.D = {c for b, cs in self.dblocks for c in cs}
        mb = [b for b in drone.blocks if b['kind'] == 'merge']
        assert len(mb) == 1, f'{drone.name}: needs exactly one merge block'
        self.dm = mb[0]
        self.cname = C.merge_clamp_dir(drone, self.dm)
        self.c = DIR[self.cname]
        self.Md = drone.cells(self.dm)[0]
        self.Mp = add3(self.Md, self.c)
        self.welders = []          # list of (C0, d)
        self.scaffold = set()      # armour cells
        self.proj = None
        self.stats = {}

EXIT_LEN = 14          # cells of free runway behind the drone (see exit_prism)

def exit_options(design, avoid_axes=()):
    """exit directions to try, best first: straight away from the merge block (-c, what HiveMotherAI nudges along by default), then sideways;
    sideways directions along `avoid_axes` (the axes the bays of one group are lined up on, the neighbour bay would be in the way) come last"""
    c = design.c
    side = [v for v in (DIR[n] for n in ('Forward', 'Backward', 'Up', 'Down', 'Left', 'Right')) if v != c and v != neg(c)]
    ax = lambda v: [abs(q) for q in v].index(1)
    return [neg(c)] + [v for v in side if ax(v) not in avoid_axes] + [v for v in side if ax(v) in avoid_axes]

def exit_prism(design, length=None, e=None):
    """cells swept by the docked drone when it leaves AWAY from the printer merge block (opposite its clamp direction): the drone footprint
    shifted 1..length cells along -c.  HiveMotherAI nudges the printer along +c after the merge pair is switched off, so the drone
    moves along -c relative to the printer; no printer block (welder body, scaffold) may stand in this prism or the drone is boxed in."""
    L = length or EXIT_LEN
    e = e or getattr(design, 'exit', None) or neg(design.c)
    return {(x + e[0] * k, y + e[1] * k, z + e[2] * k) for (x, y, z) in design.D for k in range(1, L + 1)}

def build_forbidden(design, soft_camera=False):
    F = set(design.D)
    for c in list(design.D): F.update(neighbours26(c))
    F |= exit_prism(design)
    for c, why in design.drone.reserve.items():
        if soft_camera and why.startswith('camera'): continue
        F.add(tuple(c))
    F.discard(design.Mp)
    return F

def _bbox(cells, pad):
    xs, ys, zs = zip(*cells)
    return [(min(a) - pad, max(a) + pad) for a in (xs, ys, zs)]

MODES = ((1, False), (1, True), (0, False), (0, True))        # (gap cells between welders and drone, camera zones soft)

def solve_bay_auto(drone, margin_m=None, avoid_axes=(), prefer=None):
    """pick the exit direction and the weld mode: the (exit, mode) with the FEWEST (block, sphere hypothesis) pairs that no legal welder
    position can reach wins; ties go to the straight-back exit and the strictest mode.  Then run the cover solver on it."""
    des0 = BayDesign(drone)
    opts = exit_options(des0, avoid_axes)
    if prefer is not None and tuple(prefer) in opts: opts.remove(tuple(prefer)); opts.insert(1, tuple(prefer))      # right after the straight-back exit
    key = (drone.name, _drone_sig(drone), tuple(opts))
    if key in _AUTO: return _AUTO[key]
    scored = []; last = None
    for ei, e in enumerate(opts):
        for mi, (gap, soft) in enumerate(MODES):
            try:
                ctx = _prepare(drone, soft, gap, e)
            except AssertionError as ex:
                last = last or ex; continue
            scored.append(((ctx['n_infeasible'], ei, mi), ctx))
        if scored and min(sc[0][0] for sc in scored) == 0: break
    if not scored: raise last
    scored.sort(key=lambda t: t[0])
    # run the real cover on the best few settings: welders blocking each other can make a nominally better setting worse
    best = None
    for sc, ctx in scored[:TRY_SETTINGS]:
        try:
            des = _cover(ctx)
        except AssertionError as ex:
            last = last or ex; continue
        rk = (des.stats['missA'], des.stats['unmet_weight'], len(des.welders), sc)
        if best is None or rk < best[0]: best = (rk, des)
    if best is None: raise last
    _AUTO[key] = best[1]
    return best[1]

TRY_SETTINGS = 3
REPAIR_ITERS = 30

_AUTO = {}

def _drone_sig(drone):
    return hash(tuple(sorted((b['xtype'][-12:], b['sub'], b['min'], b['fwd'], b['up']) for b in drone.blocks)))

def solve_bay(drone, soft_camera=False, gap=1, margin_m=None, max_pad=9, verbose=False, exit_dir=None):
    return _cover(_prepare(drone, soft_camera, gap, tuple(exit_dir) if exit_dir else None))

# ---- numpy helpers ----
_KOFF = 128
def _keys(a):
    a = np.asarray(a, dtype=np.int64)
    return ((a[..., 0] + _KOFF) * 256 + (a[..., 1] + _KOFF)) * 256 + (a[..., 2] + _KOFF)

def _boxdist(pts, cells):
    """(P, N) distance (cell units) from each point to each unit cell box (cells = integer min corners)"""
    out = np.empty((len(pts), len(cells)))
    lo = cells[None, :, :].astype(float); hi = lo + 1.0
    for s in range(0, len(pts), 2500):
        p = pts[s:s + 2500, None, :]
        d = np.maximum(np.maximum(lo - p, p - hi), 0.0)
        out[s:s + 2500] = np.sqrt((d * d).sum(-1))
    return out

def hyp_points(base, dv):
    """sphere centres for every FAMILY hypothesis of welders whose tip-centre CELL is `base` (array (M,3), integer) working along dv: (NH, M, 3)"""
    return np.stack([base + 0.5 + np.asarray(dv, float) * (0.5 - h.backoff) for h in FAM])

def any_reach(points, radius, cells):
    """True when any point is within `radius` (cell units) of any cell box"""
    if len(points) == 0 or len(cells) == 0: return False
    return bool((_boxdist(np.asarray(points, float), np.asarray(cells)) <= radius).any())

def _prepare(drone, soft_camera, gap, exit_dir, max_pad=9):
    """everything the cover solver needs for one (exit direction, gap, camera zones) setting: forbidden cells, welder candidates with their
    coverage bitmasks over (block, hypothesis) pairs; raises AssertionError when even the baseline hypothesis cannot be met"""
    key = (exit_dir, drone.name, soft_camera, gap, _drone_sig(drone))
    if key in _CTX: return _CTX[key]
    des = BayDesign(drone)
    des.gap = gap; des.soft_camera = soft_camera; des.exit = tuple(exit_dir) if exit_dir else neg(des.c)
    F = build_forbidden(des, soft_camera)
    Mp0 = des.Mp; S10 = add3(Mp0, des.c)
    assert S10 not in F, f'{drone.name}: stalk cell {S10} behind the printer merge is not free'
    ray = {add3(Mp0, mul3(des.c, k)) for k in range(2, 41)}      # the straight stalk that later joins the bay to the pod
    F |= ray
    # gap 0: welders (only) may touch the drone: their tip face has no mount points.  Forbidden for them: drone cells, hard clear zones,
    # and any cell where the welder's real mount points would connect to a drone block.
    Fw = F
    if gap == 0:
        Fw = set(des.D) | ray | exit_prism(des)
        for c, why in drone.reserve.items():
            if soft_camera and why.startswith('camera'): continue
            Fw.add(tuple(c))
    Mp = des.Mp; S1 = S10
    nb = len(des.dblocks)
    cellarr = np.array([c for b, cs in des.dblocks for c in cs], dtype=np.int64)
    cblock = np.array([i for i, (b, cs) in enumerate(des.dblocks) for c in cs])
    onehot = np.zeros((len(cellarr), nb)); onehot[np.arange(len(cellarr)), cblock] = 1.0
    Fk_w = np.unique(_keys(np.array(sorted(Fw | {Mp, S1}))))
    Fk = np.unique(_keys(np.array(sorted(F | {Mp, S1}))))
    Dk = np.unique(_keys(cellarr))
    tb = _bbox(des.D, 4)
    xs, ys, zs = (np.arange(tb[k][0], tb[k][1] + 1) for k in range(3))
    P = np.stack(np.meshgrid(xs, ys, zs, indexing='ij'), -1).reshape(-1, 3)
    radii = np.array([radius_cells_of(h) for h in FAM])
    cand = []
    for d in DIR.values():
        offs, att, _, _ = welder_offsets(d)
        offa = np.array(offs, dtype=np.int64); atta = np.array(att, dtype=np.int64)
        dv = np.array(d, float)
        near = (_boxdist(P + 0.5 + dv * 0.5, cellarr).min(axis=1) <= 4.2)
        Q = P[near]
        if len(Q) == 0: continue
        wk = _keys(Q[:, None, :] + offa[None, :, :])                    # (m, 54)
        ok = ~np.isin(wk, Fk_w).any(axis=1)
        if gap == 0: ok &= ~np.isin(_keys(Q[:, None, :] + atta[None, :, :]), Dk).any(axis=1)
        Q = Q[ok]; wk = wk[ok]
        if len(Q) == 0: continue
        strict = ~np.isin(wk, Fk).any(axis=1)
        pts = hyp_points(Q, dv)                                         # (NH, m, 3)
        bits = np.zeros((len(Q), nb * NH), dtype=bool)
        for hi in range(NH):
            cov = (_boxdist(pts[hi], cellarr) <= radii[hi]).astype(float) @ onehot > 0      # (m, nb)
            bits[:, hi::NH] = cov
        packed = np.packbits(bits, axis=1, bitorder='little')
        for r in range(len(Q)):
            m = int.from_bytes(packed[r].tobytes(), 'little')
            if not m: continue
            C0 = tuple(int(v) for v in Q[r])
            cs = frozenset((C0[0] + o[0], C0[1] + o[1], C0[2] + o[2]) for o in offs)
            cand.append((C0, d, cs, m, tuple(float(v) for v in pts[0][r]), bool(strict[r])))
    allmask = (1 << (nb * NH)) - 1
    union = 0
    for cc in cand: union |= cc[3]
    infeasible = allmask & ~union
    base_miss = [i for i in range(nb) if not (union >> (i * NH)) & 1]
    if base_miss:
        names = [des.dblocks[i][0]['name'] or des.dblocks[i][0]['sub'] for i in base_miss]
        raise AssertionError(f'{drone.name}: {len(base_miss)} blocks can never be reached by a welder even under the tip hypothesis ({names[:5]})')
    ctx = dict(drone=drone, des=des, F=F, Fw=Fw, ray=ray, Mp=Mp, S1=S1, cand=cand, nb=nb, allmask=allmask, infeasible=infeasible,
               n_infeasible=bin(infeasible).count('1'), max_pad=max_pad)
    _CTX[key] = ctx
    return ctx

_CTX = {}
WELDER_BUDGET = 12        # soft cap per bay for the best-effort hypotheses (tier A may exceed it when the drone needs more)
RESTARTS = 12             # randomised greedy restarts; the best (fewest tier-A misses, least unmet weight, fewest welders) wins
# importance of each FAMILY hypothesis (tier A = FAM[:4], must be met wherever any legal welder position can do it; the rest is best effort)
HYP_WEIGHT = {'tip r1.3': 8, 'tip r1.0': 8, 'tip-1 r1.3': 6, 'tip-1 r1.0': 6, 'tip-2 r1.3': 3, 'tip-2 r1.0': 2, 'mid r1.3': 2, 'mid r1.0': 1}
TIER_A = reach.TIER_A
RED_WEIGHT = 2

def radius_cells_of(h): return reach.radius_cells(h)

def _bits(mask):
    while mask:
        low = mask & -mask
        yield low.bit_length() - 1
        mask ^= low

def _pop(x): return bin(x).count('1')

def _cover(ctx):
    """weighted greedy cover of the (block, hypothesis) pairs: tier A pairs first (hardest first), then best-effort pairs and a second welder for
    RED_HYP up to WELDER_BUDGET welders; randomised restarts; pruning; scaffold routing"""
    import random
    drone = ctx['drone']; des = ctx['des']; F = ctx['F']; cand = ctx['cand']; nb = ctx['nb']
    if getattr(des, 'welders', None): return des
    Mp = ctx['Mp']; S1 = ctx['S1']; ray = ctx['ray']; max_pad = ctx['max_pad']
    free_pad = _bbox(des.D | {Mp}, max_pad + 3)
    def inbox(c): return all(free_pad[k][0] <= c[k] <= free_pad[k][1] for k in range(3))
    def center_dist(tip): return sum(abs(tip[k] - (S1[k] + 0.5)) for k in range(3))
    hb = [sum(1 << (i * NH + k) for i in range(nb)) for k in range(NH)]
    wts = [HYP_WEIGHT[h.name] for h in FAM]
    feas = ctx['allmask'] & ~ctx['infeasible']
    tierA = feas & sum(hb[:TIER_A])
    ctx2 = dict(ctx, hb=hb, wts=wts, feas=feas, tierA=tierA, inbox=inbox, center_dist=center_dist)
    best = None
    rs = 0
    while rs <= RESTARTS or (best is None or best[0][0] > 0) and rs <= 4 * RESTARTS:
        rng = random.Random(1000 + rs)
        res = _greedy(ctx2, rng, 0.0 if rs == 0 else 0.6)
        rs += 1
        if res is None: continue
        sc = (res['missA'], res['unmet'], len(res['chosen']))
        if best is None or sc < best[0]: best = (sc, res)
    # repair: a tier-A pair that no restart managed (welders blocking each other): force a welder that reaches it, let the cover adapt around it
    forced = []
    rng = random.Random(77)
    by_e = collections.defaultdict(list)
    for ci, cc in enumerate(cand):
        for e in _bits(cc[3] & tierA): by_e[e].append(ci)
    it = 0
    while best is not None and best[0][0] > 0 and it < REPAIR_ITERS:
        it += 1
        miss = tierA & ~best[1]['union']
        e = rng.choice(list(_bits(miss)))
        forced.append(rng.choice(by_e[e]))
        if len(forced) > 6: forced.pop(rng.randrange(len(forced) - 1))
        res = _greedy(ctx2, rng, 0.3, forced)
        if res is None: continue
        sc = (res['missA'], res['unmet'], len(res['chosen']))
        if sc < best[0]: best = (sc, res)
        elif sc[0] <= best[0][0] + 1 and rng.random() < 0.5: best = (sc, res) if sc[0] <= best[0][0] else best
    assert best, f'{drone.name}: no welder layout found'
    res = best[1]
    chosen = res['chosen']
    des.welders = [(C0, d) for C0, d, _ in chosen]
    des.welder_cells = [cs for _, _, cs in chosen]
    des.armour = res['armour']
    des.occupied = res['occupied']
    occupied = res['occupied']; armour = res['armour']
    # ---- projector: a free cell next to the stalk, outside F ----
    pc = None
    for r in range(1, 6):
        for c in sorted(_ring(S1, r)):
            if c in F or c in occupied: continue
            if any(add3(c, n) in armour for n in DIR.values()):
                pc = c; break
        if pc: break
    assert pc, f'{drone.name}: no projector cell'
    des.proj = pc
    des.forbid = F - ray
    des.ray = ray
    names = lambda m: sorted({reach.block_label(des.dblocks[e // NH][0]) for e in _bits(m)})
    inf = {}                              # pairs the final layout does not reach: {hyp name: set of block labels}
    for k, h in enumerate(FAM):
        m = hb[k] & ~res['union']
        if m: inf[h.name] = set(names(m))
    if res['short_red']: inf['redundancy'] = set(names(res['short_red']))
    des.unmet = inf                       # everything the final layout does not reach, per hypothesis
    imp = {}                              # pairs NO legal welder position reaches (physics of the drone shape + forbidden zones)
    for k, h in enumerate(FAM):
        m = hb[k] & ctx['infeasible']
        if m: imp[h.name] = set(names(m))
    des.impossible = imp
    # tier-A pairs that were reachable in principle but not together with the other welders (they block each other): listed, the verifier waives them
    ta = [h.name for h in FAM[:TIER_A]]
    des.conflict = {k: v for k, v in inf.items() if k in ta and v - imp.get(k, set())}
    des.conflict = {k: v - imp.get(k, set()) for k, v in des.conflict.items()}
    des.stats = dict(welders=len(chosen), scaffold=len(armour), candidates=len(cand), gap=des.gap, soft_camera=des.soft_camera,
                     infeasible_pairs=ctx['n_infeasible'], unmet_pairs=_pop(sum(hb) & ~res['union']), missA=res['missA'], unmet_weight=res['unmet'])
    return des

def _greedy(ctx, rng, noise, forced=()):
    des = ctx['des']; F = ctx['F']; cand = ctx['cand']; nb = ctx['nb']; Mp = ctx['Mp']; S1 = ctx['S1']
    hb = ctx['hb']; wts = ctx['wts']; feas = ctx['feas']; tierA = ctx['tierA']; inbox = ctx['inbox']; center_dist = ctx['center_dist']
    red_bits = hb[RED_IDX]
    struct = {Mp, S1}; armour = {S1}; occupied = set(struct)
    chosen = []; chosen_idx = []; banned = set()
    union = 0
    def wgain(m):
        return sum(wts[k] * _pop(m & hb[k]) for k in range(NH))
    by_elem = collections.defaultdict(list)
    for ci, cc in enumerate(cand):
        for e in _bits(cc[3] & tierA): by_elem[e].append(ci)
    def usable_all():
        return [(cc[0], cc[1]) not in banned and not (cc[2] & occupied) for cc in cand]
    def add(ci):
        nonlocal occupied, struct, armour, union
        C0, d, cs, m, tip, strict = cand[ci]
        path = _route_welder(C0, d, cs, F, occupied | set(cs), struct, inbox, des.D)
        if path is None: banned.add((C0, d)); return False
        chosen.append((C0, d, cs)); chosen_idx.append(ci)
        occupied |= set(cs); occupied |= set(path); struct |= set(path); armour |= set(path)
        union |= m
        return True
    for ci in forced:                                   # candidates a repair pass insisted on (see _cover)
        if not (cand[ci][2] & occupied) and (cand[ci][0], cand[ci][1]) not in banned: add(ci)
    need = tierA & ~union
    while need:
        us = usable_all()
        hard = None; hc = None; counts = []
        for e in _bits(need):
            n_ = sum(1 for ci in by_elem[e] if us[ci])
            counts.append((n_, e))
            if n_ == 0: break
        counts.sort()
        hc, hard = counts[0] if noise == 0 or counts[0][0] == 0 else counts[min(len(counts) - 1, int(rng.random() * rng.random() * 4))]
        if hc == 0: need &= ~(1 << hard); continue            # cannot be met any more with non-overlapping welders: counted as missed
        best = None
        for ci in by_elem[hard]:
            if not us[ci]: continue
            C0, d, cs, m, tip, strict = cand[ci]
            sc = (wgain(m & ~union) + (2 if strict else 0)) * (1.0 + noise * rng.random()) - 0.01 * center_dist(tip)
            if best is None or sc > best[0]: best = (sc, ci)
        if add(best[1]): need &= ~union
    missA = _pop(tierA & ~union)
    # ---- best effort: remaining pairs and a second RED welder, up to the budget ----
    def red_cnt():
        cnt = [0] * nb
        for ci in chosen_idx:
            for e in _bits(cand[ci][3] & red_bits): cnt[e // NH] += 1
        return cnt
    while len(chosen) < WELDER_BUDGET:
        cnt = red_cnt()
        lowmask = sum(1 << (i * NH + RED_IDX) for i in range(nb) if cnt[i] < 2)
        rest = feas & ~union
        us = usable_all()
        pick = None
        for ci, cc in enumerate(cand):
            if not us[ci] or ci in chosen_idx: continue
            m = cc[3]
            g = wgain(m & rest) + RED_WEIGHT * _pop(m & lowmask)
            if g < 3: continue
            sc = (g + (1 if cc[5] else 0)) * (1.0 + noise * rng.random()) - 0.01 * center_dist(cc[4])
            if pick is None or sc > pick[0]: pick = (sc, ci)
        if pick is None: break
        add(pick[1])
    # ---- prune welders that add nothing ----
    cnt0 = red_cnt()
    def cover_ok(sel):
        u = 0; cnt = [0] * nb
        for ci in sel:
            u |= cand[ci][3]
            for e in _bits(cand[ci][3] & red_bits): cnt[e // NH] += 1
        return (u & feas) == (union & feas) and all(cnt[i] >= min(2, cnt0[i]) for i in range(nb))
    order = sorted(range(len(chosen)), key=lambda k: (cand[chosen_idx[k]][5], _pop(cand[chosen_idx[k]][3])))      # touching / weak welders first
    keep = list(range(len(chosen)))
    for k in order:
        trial = [j for j in keep if j != k]
        if cover_ok([chosen_idx[j] for j in trial]): keep = trial
    chosen = [chosen[j] for j in keep]; chosen_idx = [chosen_idx[j] for j in keep]
    cnt = red_cnt()
    short_red = sum(1 << (i * NH + RED_IDX) for i in range(nb) if cnt[i] < 2)
    unmet = sum(wts[k] * _pop(hb[k] & ~union) for k in range(NH)) + RED_WEIGHT * _pop(short_red)
    # rebuild scaffold for the pruned set so no useless armour remains
    occupied = {Mp, S1}; struct = {Mp, S1}; armour = {S1}
    srt = sorted(chosen, key=lambda t: center_dist(tip_point_from_cells(t[2], t[1])))
    for C0, d, cs in srt: occupied |= set(cs)
    for C0, d, cs in srt:
        path = _route_welder(C0, d, cs, F, occupied, struct, inbox, des.D)
        if path is None: return None
        occupied |= set(path); struct |= set(path); armour |= set(path)
    return dict(chosen=chosen, armour=armour, occupied=occupied, struct=struct, short_red=short_red, union=union, missA=missA, unmet=unmet)

_BAYS = {}

def _ring(c, r):
    for dx in range(-r, r + 1):
        for dy in range(-r, r + 1):
            for dz in range(-r, r + 1):
                if max(abs(dx), abs(dy), abs(dz)) == r: yield (c[0] + dx, c[1] + dy, c[2] + dz)

def _route_welder(C0, d, cs, F, occupied, struct, inbox, D=()):
    """shortest armour path from a mount-attachable cell of the welder to a cell adjacent to the structure; returns path cells or None"""
    mn, fwd, up = welder_block_args(C0, d)
    starts = [add3(mn, o) for o in welder_attach_offsets(fwd, up)]
    if any(s in struct for s in starts): return []          # already touches the structure through a real mount point
    starts = [s for s in starts if s not in F and s not in occupied and inbox(s)]
    if not starts: return None
    # target: free cell adjacent to a structure cell
    prev = {}
    dq = collections.deque()
    for s in sorted(starts):
        prev[s] = None; dq.append(s)
    # the welder might already touch the structure through an attachable cell that IS a structure cell
    for s in starts:
        pass
    # also: if an attach cell itself is a structure armour cell the welder is already attached
    while dq:
        c = dq.popleft()
        if any(add3(c, n) in struct for n in DIR.values()):
            path = []
            while c is not None: path.append(c); c = prev[c]
            return path
        for n in DIR.values():
            nb = add3(c, n)
            if nb in prev or nb in F or nb in occupied or not inbox(nb): continue
            prev[nb] = c; dq.append(nb)
    return None


# =============================================================================================
# projection embedding
# =============================================================================================
IDENT_POSE = ('<PositionAndOrientation><Position x="0" y="0" z="0" /><Forward x="0" y="0" z="-1" /><Up x="0" y="1" z="0" />'
              '<Orientation><X>0</X><Y>0</Y><Z>0</Z><W>1</W></Orientation></PositionAndOrientation>')
GHOST_PCT = '1.52590219E-05'          # the value the game writes for an unbuilt projected block

def _is_plain_cube(b): return b['xtype'].endswith('_CubeBlock') and tuple(b['size']) == (1, 1, 1)

def projected_grid_xml(drone, seed_text=''):
    """<ProjectedGrids> element text holding `drone` as an unbuilt ghost grid.  The FIRST block is a plain 1x1x1 armour cube (it anchors the
    projection, see module docstring).  Returns (xml, first_block_min)."""
    d = copy.deepcopy(drone)
    for b in d.blocks: b['id'] = 0
    d.rng.seed(zlib.crc32((d.name + '|' + seed_text).encode()))
    d._assign_ids()
    order = sorted(range(len(d.blocks)), key=lambda i: (not _is_plain_cube(d.blocks[i]), i))
    first = d.blocks[order[0]]
    assert _is_plain_cube(first), f'{d.name}: needs a plain armour cube to anchor the projection'
    counters = {}
    parts = []
    for i in order:
        el = d.build_block(d.blocks[i], counters)
        kids = list(el)
        pos = [k.tag for k in kids].index('Min') + 1
        el.insert(pos, E('BuildPercent', GHOST_PCT)); el.insert(pos, E('IntegrityPercent', GHOST_PCT))
        el.find('IntegrityPercent').text = GHOST_PCT; el.find('BuildPercent').text = GHOST_PCT
        parts.append(ser(el, 8))
    gid = d.rng.randint(10**17, 9 * 10**17)
    from xml.sax.saxutils import escape
    xml = ('<ProjectedGrids>\n<MyObjectBuilder_CubeGrid>\n<SubtypeName />\n'
           f'<EntityId>{gid}</EntityId>\n<PersistentFlags>CastShadows InScene</PersistentFlags>\n<Name>{gid}</Name>\n{IDENT_POSE}\n'
           '<LocalPositionAndOrientation xsi:nil="true" />\n<GridSizeEnum>Small</GridSizeEnum>\n<CubeBlocks>\n' + ''.join(parts) +
           f'</CubeBlocks>\n<DisplayName>{escape(d.name)}</DisplayName>\n<DestructibleBlocks>true</DestructibleBlocks>\n<CreatePhysics>false</CreatePhysics>\n'
           '<EnableSmallToLargeConnections>false</EnableSmallToLargeConnections>\n<IsRespawnGrid>false</IsRespawnGrid>\n<LocalCoordSys>0</LocalCoordSys>\n'
           '<TargetingTargets />\n<NPCGridClaimElapsed xsi:nil="true" />\n</MyObjectBuilder_CubeGrid>\n</ProjectedGrids>')
    return xml, tuple(first['min'])

def projection_offset(first_min, T, pcell):
    """ProjectionOffset (projector-local cells) for identity projector orientation and zero rotation: first block lands on pcell - offset"""
    return tuple(pcell[k] - (first_min[k] + T[k]) for k in range(3))

def projector_settings(drone, T, pcell, seed_text=''):
    xml, fm = projected_grid_xml(drone, seed_text)
    off = projection_offset(fm, T, pcell)
    vec = lambda v: '<X>%d</X><Y>%d</Y><Z>%d</Z>' % tuple(v)
    return {'ProjectedGrids': xml, 'ProjectionOffset': f'<ProjectionOffset>{vec(off)}</ProjectionOffset>',
            'ProjectionRotation': f'<ProjectionRotation>{vec((0, 0, 0))}</ProjectionRotation>',
            'KeepProjection': 'true', 'ProjectionsRemaining': '0'}, off

# =============================================================================================
# orientation helpers
# =============================================================================================
def dirname(v): return VEC2NAME[tuple(v)]

def perp_pref(v, prefer=("Up", "Forward", "Right", "Down", "Backward", "Left")):
    for n in prefer:
        if sum(a * b for a, b in zip(DIR[n], v)) == 0: return n
    raise AssertionError(v)

def orient_for_dims(size, want, ext_of=None):
    """(fwd, up) orientation under which a block of `size` covers `want` grid cells along (x, y, z)"""
    for f in DIR:
        for u in DIR:
            if sum(a * b for a, b in zip(DIR[f], DIR[u])): continue
            ax = [cross(DIR[f], DIR[u]), DIR[u], neg(DIR[f])]
            e = [0, 0, 0]
            for i, s in enumerate(size):
                for k in range(3): e[k] += abs(ax[i][k]) * s
            if tuple(e) == tuple(want): return f, u
    raise AssertionError((size, want))

# =============================================================================================
# conflict model between placed bays
# =============================================================================================
class PlacedBay:
    """a BayDesign translated by T"""
    def __init__(self, design, T):
        self.design = design; self.T = T
        tr = lambda cs: {add3(c, T) for c in cs}
        self.drone_cells = tr(design.D)
        self.noprint = tr(design.forbid)                       # printer blocks of OTHER bays stay out of this
        self.noprint_weld = tr(design.D | {tuple(c) for c, w in design.drone.reserve.items() if not (w.startswith('camera') and design.soft_camera)})
        self.armour = tr(design.armour)
        self.weld_cells = [tr(cs) for cs in design.welder_cells]
        self.printer_cells = set(self.armour)
        for s in self.weld_cells: self.printer_cells |= s
        self.Mp = add3(design.Mp, T)
        self.printer_cells.add(self.Mp)
        self.proj = add3(design.proj, T); self.printer_cells.add(self.proj)
        self.tips = [tip_point_from_cells(list(cs), d) for cs, (C0, d) in zip(self.weld_cells, [(add3(C0, T), d) for C0, d in design.welders])]
        self.dblock_cells = [[add3(c, T) for c in bcs] for b, bcs in design.dblocks]
        self.dcell_arr = np.array(sorted(self.drone_cells), dtype=np.int64).reshape(-1, 3)
        # sphere centres of every welder under the margin-free hypotheses (the loosest ones): a foreign drone must stay out of ALL of them
        pts = []
        for cs, (C0, d) in zip(self.weld_cells, [(add3(C0, T), d) for C0, d in design.welders]):
            for h in FAM:
                if h.margin == 0.0: pts.append(tip_point_from_cells(list(cs), d, h.backoff))
        self.reach_pts = np.array(pts, dtype=float).reshape(-1, 3)

def bays_conflict(A, B):
    """True when two placed bays cannot coexist (cell overlap, contact with a foreign drone, or a welder reaching a foreign drone)"""
    if A.printer_cells & B.printer_cells: return True
    if A.printer_cells & B.noprint or B.printer_cells & A.noprint: return True
    if A.drone_cells & B.drone_cells: return True
    R = reach.radius_cells(FAM[0])
    for P, Q in ((A, B), (B, A)):
        if any_reach(P.reach_pts, R, Q.dcell_arr): return True
    return False


# =============================================================================================
# the printer ship
# =============================================================================================
POD_R = 2          # pod half size: 5x5x5 cells

# CargoContainer is in Starburst but not in shapes.json; copy the shape the game wrote there (terminal level: no <Enabled>)
_CARGO_SHAPE = dict(level='term', owner=True, tail='', container=(
    '<ComponentContainer>\n  <Components>\n    <ComponentData>\n      <TypeId>MyInventoryBase</TypeId>\n'
    '      <Component xsi:type="MyObjectBuilder_Inventory">\n        <CanPutItems>true</CanPutItems>\n        <Items />\n'
    '        <nextItemId>0</nextItemId>\n        <Volume>0.125</Volume>\n        <Mass>9223372036854.775807</Mass>\n'
    '        <MaxItemCount>2147483647</MaxItemCount>\n        <Size xsi:nil="true" />\n        <InventoryFlags>CanReceive CanSend</InventoryFlags>\n'
    '        <RemoveEntityOnEmpty>false</RemoveEntityOnEmpty>\n      </Component>\n    </ComponentData>\n  </Components>\n</ComponentContainer>'))

def register_shapes():
    """idempotent: teach sebp the CargoContainer shape seen in Starburst (called when a build needs it, never at import)"""
    sebp.SHAPES.setdefault('CargoContainer/*', _CARGO_SHAPE)

class Printer:
    """Builds one printer ship (a Blueprint) around a 5x5x5 equipment pod at the origin.  Typical use:

        pr = Printer("Hive Mother", lite)
        pr.pod(batteries=1, heavy=False)
        pr.add_group(drone_bp, count=2, grid=(2, 1))
        pr.add_thrusters(per_dir=2)
        pr.finish(script_name, ini_text)
    """
    def __init__(self, name, lite, seed_extra=''):
        self.name = name; self.lite = lite
        self.label = name + (' Lite' if lite else '')
        self.bp = Blueprint(self.label, 'Small', seed=seed_for(self.label + seed_extra))
        self.occ = {}                 # cell -> block dict (printer blocks)
        self.noprint = set()          # cells printer blocks must not use (drones, their ring, clear zones)
        self.bays = []                # PlacedBay objects
        self.records = []             # dicts for reach.py / README
        self.net = set()              # armour/structure cells that are connected to the pod (for routing)
        self.heavy = False
        self.notes = []

    # ---- naming ----
    def nm(self, role, bay=None):
        return f'{role} [Bay {bay}] ({self.label})' if bay else f'{role} ({self.label})'

    # ---- low level placement ----
    def is_free(self, c, extra=()):
        return c not in self.occ and c not in self.noprint and c not in self.bp.reserve and c not in extra

    def _own(self, b):
        for c in self.bp.cells(b):
            assert c not in self.occ, f'{self.label}: cell {c} used twice ({b["name"]} vs {self.occ[c]["name"] or self.occ[c]["sub"]})'
            self.occ[c] = b
        return b

    def armour(self, c, color=None, heavy=None, net=True):
        assert c not in self.occ, f'{self.label}: armour cell {c} occupied by {self.occ[c]["name"] or self.occ[c]["sub"]}'
        h = self.heavy if heavy is None else heavy
        b = self.bp.armor(*c, color=color or PRINTER_COLOR, sub='SmallHeavyBlockArmorBlock' if h else None)
        self._own(b)
        if net: self.net.add(c)
        return b

    def armour_path(self, cells, color=None):
        for c in cells:
            if c not in self.occ: self.armour(c, color)

    # ---- the equipment pod ----
    def pod(self, heavy=False, cargo=True, batteries=1, gyros=3):
        """5x5x5 core: big battery(ies), remote control, PB (script added in finish), gyros, optional component stock"""
        self.heavy = heavy
        if cargo: register_shapes()
        r = POD_R
        cells = {(x, y, z) for x in range(-r, r + 1) for y in range(-r, r + 1) for z in range(-r, r + 1)}
        bat_b = []
        f, u = orient_for_dims((3, 2, 3), (3, 2, 3))
        b = C.bat(self.bp, (-1, -1, -1), self.nm('Battery 1'), f, u, small=False)
        bat_b.append(b); self._own(b)
        used = set(self.bp.cells(b))
        # remote control
        rc = self.bp.add('MyObjectBuilder_RemoteControl', 'SmallBlockRemoteControl', (0, 1, 0), name=self.nm('Remote Control'), kind='ctrl')
        self._own(rc); used.add((0, 1, 0))
        # programmable block: front face (its only mount) points into the pod shell
        self.pb_cell = (1, 1, 0)
        used.add(self.pb_cell)
        # gyros stand on the battery (bottom mount)
        gy = [(-1, 1, 0), (0, 1, 1), (0, 1, -1)][:gyros]
        for i, c in enumerate(gy):
            g = self.bp.add('MyObjectBuilder_Gyro', 'SmallBlockGyro', c, 'Forward', 'Up', self.nm(f'Gyroscope {i + 1}'), kind='gyro')
            self._own(g); used.add(c)
        if cargo:
            for i, c in enumerate([(1, 1, 1), (1, 1, -1)]):
                k = self.bp.add('MyObjectBuilder_CargoContainer', 'SmallBlockSmallContainer', c, 'Forward', 'Up', self.nm(f'Component Stock {i + 1} [Stock]'), kind='cargo')
                self._own(k); used.add(c)
        for c in sorted(cells - used):
            self.armour(c)
        self.pod_cells = cells
        self.net |= cells
        self.battery_blocks = bat_b

    def add_pb(self, script, ini_text):
        """the PB sits at pb_cell; Forward points at the shell cell (2,1,0)"""
        pb = self.bp.add('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', self.pb_cell, 'Right', 'Up', self.nm('Hive Mother PB'),
                         extra=pb_extra(script) if script else '', kind='ctrl', custom_data=ini_text)
        self._own(pb)
        self.pb = pb
        return pb

    # ---- bays ----
    def add_group(self, drone, count=1, grid=None, rails_pad=0, first_n=None, design=None, face_offset=(0, 0)):
        """Place `count` bays for `drone` on the pod face that looks to -c (c = the drone's clamp direction).  grid = (na, nb) slot array
        along the two lateral axes (default: a row along the first lateral axis).  Returns the list of bay numbers."""
        c0 = C.merge_clamp_dir(drone, [b for b in drone.blocks if b['kind'] == 'merge'][0])
        ax = [abs(v) for v in DIR[c0]].index(1)
        lat = [k for k in range(3) if k != ax]
        na, nb = grid or (count, 1)
        design = design or solve_bay_auto(drone, avoid_axes=tuple(lat[i] for i, n_ in enumerate((na, nb)) if n_ > 1))
        c = design.c
        assert na * nb >= count
        ua = tuple(1 if k == lat[0] else 0 for k in range(3)); ub = tuple(1 if k == lat[1] else 0 for k in range(3))
        # face cell outside the pod on the side opposite to c: pod centre + (-c) * (POD_R + 1)
        out = tuple(-v for v in c)
        base0 = mul3(out, POD_R + 1)
        # stalk: Mp -> S1 .. B ; B = Mp + c*(g+1).  Mp(0-slot) = B - c*(g+1)
        def T_for(g, ia, ib, pa, pb_):
            B = add3(add3(base0, mul3(ua, ia * pa + face_offset[0])), mul3(ub, ib * pb_ + face_offset[1]))
            Mp = sub3(B, mul3(c, g + 1))
            return sub3(Mp, design.Mp)
        # standoff g: smallest value for which one bay does not touch the pod or its neighbour cells
        pod_ring = set(self.pod_cells)
        for cc in list(self.pod_cells): pod_ring.update(neighbours26(cc))
        g = 0
        while True:
            pbay = PlacedBay(design, T_for(g, 0, 0, 0, 0))
            stalk = {add3(pbay.Mp, mul3(c, k)) for k in range(1, g + 2)}
            clash = (pbay.printer_cells | stalk) & self.pod_cells
            near = pbay.drone_cells & pod_ring
            if not clash and not near and not (pbay.noprint & self.pod_cells): break      # other bays (their exit lanes included) are handled by the slot search below
            g += 1
            assert g < 25, f'{self.label}: cannot stand the bay off from the pod'
        # lateral pitch: smallest offset along each lateral axis at which two identical bays coexist
        def pitch(u_):
            for p in range(2, 40):
                T2 = tuple(T_for(g, 0, 0, 0, 0)[k] + u_[k] * p for k in range(3))
                if not bays_conflict(PlacedBay(design, T_for(g, 0, 0, 0, 0)), PlacedBay(design, T2)): return p
            raise AssertionError('no pitch')
        pa = pitch(ua) if na > 1 else 0
        pb_ = pitch(ub) if nb > 1 else 0
        slots = []
        for ib in range(nb):
            for ia in range(na): slots.append((ia, ib))
        slots = slots[:count]
        base_design = design
        def design_for(ia):
            """sideways exits along the array axis point OUTWARDS (away from the neighbour bay): mirror the exit per slot"""
            if tuple(base_design.exit) == neg(base_design.c) or na < 2: return base_design
            ea = [abs(v) for v in base_design.exit].index(1)
            if ea != lat[0] or ia == (na - 1) / 2.0: return base_design
            want = mul3(ua, -1 if ia < (na - 1) / 2.0 else 1)
            try: return solve_bay_auto(drone, prefer=want)
            except AssertionError: return base_design
        # centre the array around the pod axis
        ca = (na - 1) / 2.0; cb = (nb - 1) / 2.0
        nums = []
        for k, (ia, ib) in enumerate(slots):
            T = None
            design = design_for(ia)
            for ext in [0] + [sg * k for k in range(1, 41) for sg in (1, -1)]:
                for gg in range(g, g + 12):
                    oa = int(round((ia - ca) * pa)) + ext; ob = int(round((ib - cb) * pb_))
                    B = add3(add3(base0, mul3(ua, oa + face_offset[0])), mul3(ub, ob + face_offset[1]))
                    Mp = sub3(B, mul3(c, gg + 1))
                    T = sub3(Mp, design.Mp)
                    cand = PlacedBay(design, T)
                    if all(not bays_conflict(cand, o) for o in self.bays) and not (cand.printer_cells & self.occ.keys()) and not (cand.printer_cells & self.noprint) \
                       and not (cand.noprint & set(self.occ)) and not ({add3(cand.Mp, mul3(c, j)) for j in range(1, gg + 2)} & (set(self.occ) | self.noprint)) \
                       and not (cand.drone_cells & set(self.occ)):
                        break
                    T = None
                if T is not None: break
            assert T is not None, f'{self.label}: bay slot {ia},{ib} does not fit'
            n = (first_n or (len(self.records) + 1)) if k == 0 else len(self.records) + 1
            self._place_bay(n, drone, design, T, gg)
            nums.append(n)
        return nums

    def _place_bay(self, n, drone, design, T, g):
        pb = PlacedBay(design, T)
        c = design.c
        bp = self.bp
        self.bays.append(pb)
        # merge block (printer side) pointing at the drone
        clamp = OPP[design.cname]
        pm = C.merge_small(bp, pb.Mp, clamp, self.nm('Merge', n))
        pm['bay'] = n
        self._own(pm)
        # stalk from Mp to the base (adjacent to the pod)
        stalk = [add3(pb.Mp, mul3(c, k)) for k in range(1, g + 2)]
        for s in stalk: self.armour(s, BAY_COLOR, net=False)
        for s in pb.armour:
            if s not in self.occ: self.armour(s, BAY_COLOR, net=False)
        welders = []
        for i, (C0, d) in enumerate(design.welders):
            C1 = add3(C0, T)
            mn, fwd, up = welder_block_args(C1, d)
            w = bp.add('MyObjectBuilder_ShipWelder', reach.WELDER_SUB, mn, fwd, up, self.nm(f'Welder {i + 1}', n), WELD_COLOR, kind='welder')
            w['bay'] = n
            self._own(w); welders.append(w)
        # projector
        sett, off = projector_settings(drone, T, pb.proj, seed_text=f'bay{n}')
        pj = bp.add('MyObjectBuilder_Projector', 'SmallProjector', pb.proj, 'Forward', 'Up', self.nm('Projector', n), (0.6, 0.3, 0.1),
                    settings=sett, kind='proj')
        pj['bay'] = n
        self._own(pj)
        self.noprint |= pb.noprint | pb.drone_cells
        self.connect(stalk[-1])
        self.net.update(stalk); self.net.update(pb.armour); self.net.add(pb.Mp)
        rec = dict(n=n, drone=drone, T=T, exit=(VEC2NAME[design.exit] if tuple(design.exit) != neg(design.c) else ''), merge=pm, projector=pj, welders=welders, offset=off, design=design, g=g, clamp=clamp, c=design.cname)
        self.records.append(rec)
        return rec

    def connect(self, start):
        """armour path from the occupied cell `start` to the network (pod and earlier bays); shortest route through free cells"""
        def touches(c): return any(add3(c, d) in self.net for d in DIR.values())
        if touches(start): return []
        prev = {start: None}; dq = collections.deque([start])
        while dq:
            c = dq.popleft()
            for d in DIR.values():
                n = add3(c, d)
                if n in prev or not self.is_free(n): continue
                prev[n] = c
                if touches(n):
                    path = []
                    while n != start: path.append(n); n = prev[n]
                    self.armour_path(path, BAY_COLOR)
                    return path
                dq.append(n)
        raise AssertionError(f'{self.label}: cannot connect the bay at {start} to the pod')

    # ---- free-space search helpers ----
    def _bfs_from_net(self, maxlen, extra_block=()):
        """armour paths from the network into free space: {cell: path-from-net-excl-cell-list}"""
        dist = {}
        dq = collections.deque()
        for c in self.net:
            for d in DIR.values():
                n = add3(c, d)
                if self.is_free(n, extra_block) and n not in dist:
                    dist[n] = (1, [n]); dq.append(n)
        while dq:
            c = dq.popleft(); ln, path = dist[c]
            if ln >= maxlen: continue
            for d in DIR.values():
                n = add3(c, d)
                if n in dist or not self.is_free(n, extra_block): continue
                dist[n] = (ln + 1, path + [n]); dq.append(n)
        return dist

    def _thruster_fits(self, A, e, taken):
        cells = [add3(A, e), add3(A, mul3(e, 2))]
        zone = [add3(A, mul3(e, k)) for k in range(3, 7)]
        bad = taken | set()
        for c in cells:
            if not self.is_free(c) or c in bad: return None
        for c in zone:
            if c in self.occ or c in self.noprint or c in bad: return None
        return cells

    def add_thrusters(self, per_dir=2, maxlen=16):
        """per_dir ion thrusters for each of the six directions (force on the ship toward that direction).  Thrusters sit on the pod faces
        where the 4-cell exhaust is clear, otherwise on armour pylons that reach out of the way of the bays."""
        bp = self.bp
        tcount = {}
        pylon_net_before = None
        for fdir in ('Forward', 'Backward', 'Up', 'Down', 'Left', 'Right'):
            force = DIR[fdir]
            e = neg(force)                        # exhaust points opposite to the force on the ship
            placed = []
            taken = set()
            # candidates: (A is an armour cell of the net, or a new pylon cell)
            cands = []
            # direct: A in the net (existing armour) whose outward cells are free
            for A in sorted(self.net):
                if A not in self.occ or self.occ[A]['sub'] not in ('SmallBlockArmorBlock', 'SmallHeavyBlockArmorBlock'): continue
                if add3(A, e) in self.occ: continue
                cells = self._thruster_fits(A, e, set())
                if cells: cands.append((0, sum(abs(v) for v in A), A, []))
            dist = self._bfs_from_net(maxlen)
            for A, (ln, path) in dist.items():
                if len(path) > maxlen: continue
                cells = self._thruster_fits(A, e, set(path))
                if cells: cands.append((ln, sum(abs(v) for v in A), A, path))
            cands.sort(key=lambda t: (t[0], t[1], t[2]))
            assert cands, f'{self.label}: nowhere for a {fdir} thruster'
            for k in range(per_dir):
                if k == 0:
                    pick = cands[0]
                else:
                    a0 = placed[0]
                    mirror = tuple(-a0[j] if abs(e[j]) == 0 else a0[j] for j in range(3))
                    cs = [t for t in cands if t[2] not in taken and not (set(add3(t[2], mul3(e, m)) for m in (1, 2)) & taken)]
                    cs.sort(key=lambda t: (sum(abs(t[2][j] - mirror[j]) for j in range(3)) + t[0] * 0.3, t[0]))
                    pick = None
                    for t in cs:
                        if self._thruster_fits(t[2], e, taken | set(t[3])) and all(p not in taken for p in t[3] if False): pick = t; break
                    assert pick, f'{self.label}: second {fdir} thruster does not fit'
                ln, _, A, path = pick
                for p in path:
                    if p not in self.occ: self.armour(p)
                cells = self._thruster_fits(A, e, set())
                assert cells, f'{self.label}: thruster slot {A} {e} vanished'
                mn = tuple(min(cc[j] for cc in cells) for j in range(3))
                up = perp_pref(e)
                t = C.thr(bp, mn, dirname(e), up, self.nm(f'Ion {fdir} {len(placed) + 1}'))
                self._own(t)
                placed.append(A)
                taken |= set(cells)
                # refresh candidates: drop those now blocked
                cands = [t_ for t_ in cands if t_[2] != A and not (set(t_[3]) & set(cells))]
                dist = None
            tcount[fdir] = len(placed)
        self.tcount = tcount

    def add_antenna(self, radius):
        """antenna on the first pod-shell cell whose outward column of 2 cells is free"""
        bp = self.bp
        for e in (DIR['Up'], DIR['Forward'], DIR['Backward'], DIR['Left'], DIR['Right'], DIR['Down']):
            for A in sorted(self.pod_cells):
                if self.occ.get(A, {}).get('sub') not in ('SmallBlockArmorBlock', 'SmallHeavyBlockArmorBlock'): continue
                cells = [add3(A, e), add3(A, mul3(e, 2))]
                if not all(self.is_free(c) for c in cells): continue
                if any(max(abs(c[j]) for j in range(3)) <= POD_R for c in cells): continue
                mn = tuple(min(cc[j] for cc in cells) for j in range(3))
                a = C.ant(bp, mn, dirname(e), perp_pref(e), self.nm('Antenna'), radius)
                self._own(a)
                return a
        raise AssertionError(f'{self.label}: no room for the antenna')

    def add_guns(self, n=2, sub='', xtype='MyObjectBuilder_SmallGatlingGun', length=4, muzzle=6):
        """fixed forward guns on pod-shell cells whose line ahead is free"""
        bp = self.bp; placed = 0
        m = DIR['Forward']
        for A in sorted(self.pod_cells, key=lambda c: (c[2], abs(c[0]) + abs(c[1]))):
            if placed >= n: break
            if self.occ.get(A, {}).get('sub') not in ('SmallBlockArmorBlock', 'SmallHeavyBlockArmorBlock'): continue
            if A[2] != -POD_R: continue
            cells = [add3(A, mul3(m, k)) for k in range(1, length + 1)]
            if not all(self.is_free(c) for c in cells): continue
            mz = None                                         # the full muzzle clearance if possible, else down to 3 cells (the bays stand in front now)
            for cand_mz in sorted({muzzle, 5, 4, 3}, reverse=True):
                if cand_mz > muzzle: continue
                zone = [add3(A, mul3(m, k)) for k in range(length + 1, length + 1 + cand_mz)]
                if not any((c in self.occ) or (c in self.noprint) for c in zone): mz = cand_mz; break
            if mz is None: continue
            mn = tuple(min(cc[j] for cc in cells) for j in range(3))
            g = bp.add(xtype, sub, mn, 'Forward', 'Up', self.nm(f'Gatling {placed + 1}'), GUN, kind='gun')
            bp.keep_clear(g, mz, 'muzzle')
            self._own(g); placed += 1
        assert placed == n, f'{self.label}: only {placed} of {n} guns fit'

    def add_turret(self, xtype='MyObjectBuilder_LargeGatlingTurret', sub='SmallGatlingTurret'):
        """5x5x5 small-grid turret (a target designator for the carrier relay) sitting on the +Y face of the pod, or the first free face"""
        bp = self.bp
        for up, fwd in (('Up', 'Forward'), ('Down', 'Forward'), ('Left', 'Up'), ('Right', 'Up'), ('Forward', 'Up'), ('Backward', 'Up')):
            u = DIR[up]
            # turret local Up = outward from the pod; the 5x5 bottom sits on the pod face
            cells = []
            ctr = mul3(u, POD_R + 1 + 2)
            lat = [k for k in range(3) if u[k] == 0]
            for a in range(-2, 3):
                for b in range(-2, 3):
                    for h in range(5):
                        c = [0, 0, 0]
                        c[lat[0]] = a; c[lat[1]] = b
                        for k in range(3):
                            if u[k]: c[k] = u[k] * (POD_R + 1 + h)
                        cells.append(tuple(c))
            if not all(self.is_free(c) for c in cells): continue
            mn = tuple(min(c[k] for c in cells) for k in range(3))
            t = bp.add(xtype, sub, mn, fwd, up, self.nm('Gatling Turret'), GUN, kind='gun')
            if set(bp.cells(t)) != set(cells):
                bp.blocks.remove(t); continue
            self._own(t)
            return t
        raise AssertionError(f'{self.label}: no room for the turret')

    # ---- finish ----
    def finish(self, script, ini_text):
        self.add_pb(script, ini_text)
        bp = self.bp
        bp.bays = [dict(n=r['n'], drone=r['drone'], T=r['T'], merge=r['merge'], projector=r['projector'], welders=r['welders'], offset=r['offset'],
                        g=r['g'], c=r['c'], design=r['design'], exit=r['exit']) for r in self.records]
        bp.printer = self
        finalize(bp)
        return bp


# =============================================================================================
# docking latch for drones that have none (Longbow S, Mantis): a stalk with a small merge block
# =============================================================================================
def with_latch(drone, prefer=("Down", "Up", "Backward", "Left", "Right", "Forward")):
    """copy of `drone` with a docking stalk added (armour + SmallShipSmallMergeBlock, clamp face away from the hull).  The stalk starts at
    a hull cell whose outward neighbour is free, stays out of the clear zones and ends one layer beyond every other block (the merge
    block must be the only block in the outermost layer, _common.check_docking).  Raises AssertionError if no stalk works."""
    if any(b['kind'] == 'merge' for b in drone.blocks): return drone
    best = None
    occ0 = {c for b in drone.blocks for c in drone.cells(b)}
    cx = sum(c[0] for c in occ0) / len(occ0); cy = sum(c[1] for c in occ0) / len(occ0); cz = sum(c[2] for c in occ0) / len(occ0)
    for rank, dn in enumerate(prefer):
        d = DIR[dn]
        L = max(sum(c[k] * d[k] for k in range(3)) for c in occ0)         # outermost layer along d
        cands = []
        for p in occ0:
            q = add3(p, d)
            if q in occ0 or q in drone.reserve: continue
            lay = sum(p[k] * d[k] for k in range(3))
            n = L + 1 - lay                                                  # cells from q up to and including the merge block layer
            cells = [add3(p, mul3(d, i)) for i in range(1, n + 1)]
            if any((c in occ0) or (c in drone.reserve) for c in cells): continue
            lat = abs(p[0] - cx) * (1 - abs(d[0])) + abs(p[1] - cy) * (1 - abs(d[1])) + abs(p[2] - cz) * (1 - abs(d[2]))
            cands.append((n, lat, cells))
        cands.sort(key=lambda t: (t[0] + t[1] * 0.5, t[0]))
        for n, lat, cells in cands[:25]:
            t = copy.deepcopy(drone)
            for c in cells[:-1]: t.armor(*c, color=RED)
            C.merge_small(t, cells[-1], dn, f'{t.name} Merge')
            try:
                t.validate()
                C.check_docking(t, [b for b in t.blocks if b['kind'] == 'merge'][0])
            except AssertionError:
                continue
            score = (rank, n)
            if best is None or score < best[0]: best = (score, t)
            break
        if best is not None and best[0][0] == rank: break
    assert best, f'{drone.name}: no room for a docking stalk'
    return finalize(best[1])

# =============================================================================================
# PB configuration
# =============================================================================================
def bay_geometry(printer, rec):
    """Custom Data facts of one bay for HiveMotherAI.cs: the drone's cell box in PRINTER grid coordinates (translation T included), its unique
    block count, and the printer cells that stand inside that box or next to the drone's merge block (the script must not count them)"""
    T = rec['T']; drone = rec['drone']
    cells = [add3(c, T) for b in drone.blocks for c in drone.cells(b)]
    mn = tuple(min(c[k] for c in cells) for k in range(3)); mx = tuple(max(c[k] for c in cells) for k in range(3))
    mine = set(cells)
    ign = {c for c in printer.occ if all(mn[k] <= c[k] <= mx[k] for k in range(3)) and c not in mine}
    mc = add3(drone.cells([b for b in drone.blocks if b['kind'] == 'merge'][0])[0], T)
    ign |= {n for n in (add3(mc, d) for d in DIR.values()) if n in printer.occ}
    ex = tuple(rec['design'].exit)
    return dict(box_min=mn, box_max=mx, blocks=len(drone.blocks), cells=len(mine), ignore=sorted(ign), exit_dir=VEC2NAME[ex])

def hive_ini(bays, max_drones, rack=False, relay=False, launch_tag='STINGER', target_tags='GNAT,HORNET,LONGBOW', extra=None, comments=(), launch=True):
    """Custom Data for HiveMotherAI.cs.  bays = [dict(n, tag, prefix, cap, max_prints, exit, geom)]  (geom = bay_geometry())"""
    L = [f'; {c}' for c in comments]
    L += ['[Hive Mother]', f'Max Drones (all tags)={max_drones}', f'Rack Mode (keep docked, launch on targets)={"true" if rack else "false"}',
         f'Rack Launch Command Tag={launch_tag}', f'Rack Target Tags (enemy sightings that release a drone)={target_tags}',
         f'Relay Turret Targets To All Tags={"true" if relay else "false"}', 'Auto Start=true']
    for k, v in (extra or {}).items(): L.append(f'{k}={v}')
    if launch:
        L += ['[Launch]', 'Auto=true', 'Burst Seconds=4']
    v3 = lambda v: ','.join(str(int(x)) for x in v)
    for b in bays:
        L += [f'[Bay {b["n"]}]', f'Tag={b["tag"]}', f'Prefix={b["prefix"]}', f'Max Alive={b["cap"]}', f'Max Prints={b.get("max_prints", 0)}']
        if b.get('exit'): L.append(f'Exit Direction (printer axis the drone leaves along, blank means straight away from the merge block)={b["exit"]}')
        g = b.get('geom')
        if g:
            L += [f'Exit Direction={g["exit_dir"]}', f'Drone Box Min={v3(g["box_min"])}', f'Drone Box Max={v3(g["box_max"])}',
                  f'Drone Cell Count={g["blocks"]}', f'Drone Occupied Cells={g["cells"]}', 'Ignore Cells=' + ';'.join(v3(c) for c in g['ignore'])]
    return '\n'.join(L) + '\n'
