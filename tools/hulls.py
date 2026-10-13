"""Procedural hull builder for stylish ships on top of tools/sebp.py.

    Volume   integer cell sets: primitives, boolean ops, mirror, hollow, queries        (section 1)
    build_hull()  solid -> armour blocks (Block / Slope / Corner / CornerInv / Slope2Base+Tip / windows)
                  with light/heavy zones, colours, reserved system cells, skins and the mount check   (section 2)
    check_mount_faces()  "mount faces hull" check that sebp.validate() cannot do                       (section 3)
    nose_cone, swept_wing, nacelle, spine, stepped_deck, hardpoints, greeble                           (section 4)
    report()  block counts, mass, HP, PCU, bounding box in metres                                      (section 5)

Coordinates are sebp grid cells: x = Right, y = Up, z = Backward (the bow points to -z).  A cell (x, y, z) is the
armour block whose `Min` is (x, y, z).  Volumes use half-open boxes [lo, hi) and cell-EDGE coordinates for planes
and float shapes (cell i spans [i, i+1)).  Pure python (sets of tuples), no third-party packages.
Rules and the measured basis are in docs/design/systems_catalogue.md section 6 and docs/design/hull_builder.md.
"""
import os, sys, json, math, random
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import sebp                                                       # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, '..'))
EPS = 1e-6


class HullError(ValueError):
    """A hull could not be turned into blocks that are guaranteed to meet the hull."""


# =============================================================================================
# directions
# =============================================================================================
AXI = {'x': 0, 'y': 1, 'z': 2}
FACES = ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))
DNAME = {v: k for k, v in sebp.DIR.items()}                       # (1,0,0) -> 'Right'


def dvec(d):
    """'Up' / '+y' / (0, 1, 0) -> grid direction vector"""
    if isinstance(d, (tuple, list)):
        return tuple(d)
    if d in sebp.DIR:
        return sebp.DIR[d]
    s = str(d).lower()
    if len(s) == 2 and s[0] in '+-' and s[1] in AXI:
        v = [0, 0, 0]
        v[AXI[s[1]]] = 1 if s[0] == '+' else -1
        return tuple(v)
    raise ValueError(f'unknown direction {d!r}')


def axis_of(v):
    return [abs(t) for t in v].index(1)


def _add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _neg(a):
    return (-a[0], -a[1], -a[2])


def _others(axis):
    return [i for i in range(3) if i != axis]


def _axis(a):
    return AXI[a] if isinstance(a, str) else int(a)


def _pred(where):
    """None | Volume | set | callable(cell) -> bool   ->   callable"""
    if where is None:
        return lambda c: True
    if isinstance(where, Volume):
        return where.cells.__contains__
    if isinstance(where, (set, frozenset)):
        return where.__contains__
    return where


def _inpoly(poly, x, y):
    return sum(1 for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1])
               if (y0 <= y < y1 or y1 <= y < y0) and x0 + (y - y0) * (x1 - x0) / (y1 - y0) > x) % 2 == 1


# =============================================================================================
# 1. Volume
# =============================================================================================
class Volume:
    """A set of integer cells.  Every operation returns a new Volume (inputs are never modified)."""
    __slots__ = ('cells',)

    def __init__(self, cells=()):
        self.cells = set(cells.cells if isinstance(cells, Volume) else cells)

    # ---- container protocol / boolean ops ----
    def __len__(self): return len(self.cells)
    def __iter__(self): return iter(self.cells)
    def __contains__(self, c): return tuple(c) in self.cells
    def __bool__(self): return bool(self.cells)
    def __eq__(self, o): return isinstance(o, Volume) and self.cells == o.cells
    __hash__ = None
    def __or__(self, o): return Volume(self.cells | _cs(o))
    def __and__(self, o): return Volume(self.cells & _cs(o))
    def __sub__(self, o): return Volume(self.cells - _cs(o))
    def __xor__(self, o): return Volume(self.cells ^ _cs(o))
    __ror__ = __or__
    def copy(self): return Volume(self.cells)
    def union(self, *o): return Volume(self.cells.union(*[_cs(v) for v in o]))
    def subtract(self, *o): return Volume(self.cells.difference(*[_cs(v) for v in o]))
    def intersect(self, *o): return Volume(self.cells.intersection(*[_cs(v) for v in o]))
    def __repr__(self):
        return f'Volume({len(self)} cells, bbox={self.bbox() if self else None})'

    # ---- primitives ----
    @classmethod
    def box(cls, lo, hi):
        """all cells with lo <= cell < hi"""
        return cls((x, y, z) for x in range(lo[0], hi[0]) for y in range(lo[1], hi[1]) for z in range(lo[2], hi[2]))

    @classmethod
    def wedge(cls, lo, hi, face='+y', toward='-z', run=1):
        """Box [lo,hi) cut by a slanted plane.  `face` = the side that carries the slanted surface (any of the 6
        directions), `toward` = the direction in which the solid gets lower (perpendicular to face).  The column at
        step t (0 = tall end) is H - t//run cells high (H = box extent along face); run=1 is a 45 degree ramp,
        run=2 a 2-run/1-rise ramp (Slope2Base+Tip), run=None scales the slope so it ends exactly at the far end."""
        f, w = dvec(face), dvec(toward)
        fa, wa = axis_of(f), axis_of(w)
        if fa == wa:
            raise ValueError('face and toward must be perpendicular')
        la = 3 - fa - wa
        H, L = hi[fa] - lo[fa], hi[wa] - lo[wa]
        out = set()
        for t in range(L):
            h = H - t // run if run else int(round(H * (1 - (t + 0.5) / L)))
            if h <= 0:
                break
            cw = lo[wa] + t if w[wa] > 0 else hi[wa] - 1 - t
            for l in range(lo[la], hi[la]):
                for j in range(min(h, H)):
                    c = [0, 0, 0]
                    c[la], c[wa] = l, cw
                    c[fa] = lo[fa] + j if f[fa] > 0 else hi[fa] - 1 - j
                    out.add(tuple(c))
        return cls(out)

    @classmethod
    def loft(cls, axis, start, stop, rect0, rect1, shape='rect'):
        """Tapered frustum / loft between two rectangles along `axis` ('x','y','z'), cells start..stop-1.
        rect = (u0, u1, v0, v1) in cell-edge coordinates, (u, v) = the two other axes in x<y<z order; rect0 applies at the
        `start` boundary, rect1 at the `stop` boundary, linear in between (sampled at cell centres).
        shape: 'rect' | 'ellipse' (inscribed in the rect) | 'diamond' | 'octagon'."""
        ai = _axis(axis)
        ua, va = _others(ai)
        n = stop - start
        out = set()
        for i in range(start, stop):
            f = (i + 0.5 - start) / n
            u0, u1, v0, v1 = (a + (b - a) * f for a, b in zip(rect0, rect1))
            if u1 < u0 - EPS or v1 < v0 - EPS:
                continue
            cu, cv, ru, rv = (u0 + u1) / 2, (v0 + v1) / 2, (u1 - u0) / 2, (v1 - v0) / 2
            for a in range(math.ceil(u0 - 0.5 - EPS), math.floor(u1 - 0.5 + EPS) + 1):
                for b in range(math.ceil(v0 - 0.5 - EPS), math.floor(v1 - 0.5 + EPS) + 1):
                    if shape != 'rect':
                        du = (a + 0.5 - cu) / ru if ru > EPS else 0.0
                        dv = (b + 0.5 - cv) / rv if rv > EPS else 0.0
                        if shape == 'ellipse':
                            ok = du * du + dv * dv <= 1 + EPS
                        elif shape == 'diamond':
                            ok = abs(du) + abs(dv) <= 1 + EPS
                        elif shape == 'octagon':
                            ok = abs(du) + abs(dv) <= 1.5 + EPS
                        else:
                            raise ValueError(shape)
                        if not ok:
                            continue
                    c = [0, 0, 0]
                    c[ai], c[ua], c[va] = i, a, b
                    out.add(tuple(c))
        return cls(out)

    frustum = loft

    @classmethod
    def ellipsoid(cls, center, radii):
        """cells whose centre is inside the ellipsoid; center in cell-edge coordinates (floats), radii in cells"""
        out = set()
        rng = [range(math.ceil(c - r - 0.5 - EPS), math.floor(c + r - 0.5 + EPS) + 1) for c, r in zip(center, radii)]
        for x in rng[0]:
            for y in rng[1]:
                for z in rng[2]:
                    if sum(((p + 0.5 - c) / r) ** 2 for p, c, r in zip((x, y, z), center, radii)) <= 1 + EPS:
                        out.add((x, y, z))
        return cls(out)

    @classmethod
    def cylinder(cls, axis, start, stop, center, radii, radii_end=None):
        """elliptic cylinder (cone/frustum when radii_end is given) along `axis`; center=(cu, cv) edge coords of the
        two other axes (x<y<z order), radii=(ru, rv)"""
        (cu, cv), (ru, rv) = center, radii
        re = radii_end or radii
        return cls.loft(axis, start, stop, (cu - ru, cu + ru, cv - rv, cv + rv),
                        (cu - re[0], cu + re[0], cv - re[1], cv + re[1]), 'ellipse')

    @classmethod
    def prism(cls, poly, axis, start, stop):
        """Extrude a 2D polygon [(u, v), ...] (cell-edge coordinates of the two other axes, x<y<z order) along `axis`
        for cells start..stop-1 (a hull side profile, a fin, a keel ...)."""
        ai = _axis(axis)
        ua, va = _others(ai)
        poly = [tuple(map(float, p)) for p in poly]
        vs = [p[1] for p in poly]
        out = set()
        for b in range(math.floor(min(vs)), math.ceil(max(vs))):
            y = b + 0.5
            xs = []
            for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]):
                if y0 != y1 and (y0 <= y < y1 or y1 <= y < y0):
                    xs.append(x0 + (y - y0) * (x1 - x0) / (y1 - y0))
            xs.sort()
            for k in range(0, len(xs) - 1, 2):
                for a in range(math.ceil(xs[k] - 0.5 - EPS), math.floor(xs[k + 1] - 0.5 + EPS) + 1):
                    for i in range(start, stop):
                        c = [0, 0, 0]
                        c[ai], c[ua], c[va] = i, a, b
                        out.add(tuple(c))
        return cls(out)

    extrude = prism

    @classmethod
    def from_function(cls, lo, hi, f):
        return cls(c for c in ((x, y, z) for x in range(lo[0], hi[0]) for y in range(lo[1], hi[1]) for z in range(lo[2], hi[2])) if f(c))

    # ---- transforms ----
    def translated(self, dx=0, dy=0, dz=0):
        return Volume((x + dx, y + dy, z + dz) for x, y, z in self.cells)

    def rotated(self, axis, k=1, about=(0, 0, 0)):
        """rotate by k * 90 degrees about the line through the cell-edge point `about` along `axis` (right-handed)"""
        ai = _axis(axis)
        ua, va = _others(ai)
        out = set()
        for c in self.cells:
            u, v = c[ua] + 0.5 - about[ua], c[va] + 0.5 - about[va]
            for _ in range(k % 4):
                u, v = -v, u
            n = list(c)
            n[ua], n[va] = math.floor(u + about[ua]), math.floor(v + about[va])
            out.add(tuple(n))
        return Volume(out)

    def reflected(self, axis=0, at=0):
        """mirror image about the plane `axis` = `at` (cell-edge coordinate: at=0 is the boundary between cells -1 and 0,
        at=0.5 the middle of cell 0).  2*at must be an integer."""
        ai, k = _axis(axis), round(2 * at)
        if abs(k - 2 * at) > EPS:
            raise ValueError('2*at must be an integer')
        out = set()
        for c in self.cells:
            n = list(c)
            n[ai] = k - 1 - c[ai]
            out.add(tuple(n))
        return Volume(out)

    def mirror(self, axis=0, at=0):
        """self united with its mirror image: build half a ship, then `.mirror('x', at)`"""
        return self | self.reflected(axis, at)

    def half(self, axis=0, at=0, side='+'):
        """cells on one side of the plane (the centre column of an odd-width hull belongs to both sides)"""
        ai, k = _axis(axis), round(2 * at)
        return Volume(c for c in self.cells if (2 * c[ai] - (k - 1) >= 0 if side == '+' else 2 * c[ai] - (k - 1) <= 0))

    def symmetry_error(self, axis=0, at=0):
        """number of cells without a mirror partner (0 = bilaterally symmetric)"""
        return len(self.cells ^ self.reflected(axis, at).cells)

    def clip(self, x=None, y=None, z=None):
        """keep cells inside per-axis ranges [lo, hi) (None = unbounded)"""
        rs = [x, y, z]
        return Volume(c for c in self.cells if all(r is None or ((r[0] is None or c[i] >= r[0]) and (r[1] is None or c[i] < r[1]))
                                                   for i, r in enumerate(rs)))

    def cut(self, point, normal):
        """remove the half space beyond a plane through `point` (cell-edge coords) with outward `normal` (any vector)"""
        return Volume(c for c in self.cells if sum((c[i] + 0.5 - point[i]) * normal[i] for i in range(3)) <= EPS)

    def chamfer(self, axis, n=1, corners=None):
        """Cut the edges of the bounding box that run parallel to `axis`: remove cells with du + dv < n, where du/dv are
        the distances (in cells) to the nearest bounding box side of that corner.  n=1 removes only the corner column,
        n=2 a 3-cell diagonal, ...  corners: iterable of (su, sv) in {-1,1}^2 (default all four)."""
        ai = _axis(axis)
        ua, va = _others(ai)
        lo, hi = self.bbox()
        corners = corners or [(-1, -1), (-1, 1), (1, -1), (1, 1)]
        out = set()
        for c in self.cells:
            for su, sv in corners:
                du = c[ua] - lo[ua] if su < 0 else hi[ua] - 1 - c[ua]
                dv = c[va] - lo[va] if sv < 0 else hi[va] - 1 - c[va]
                if du + dv < n:
                    break
            else:
                out.add(c)
        return Volume(out)

    # ---- queries ----
    def bbox(self):
        """(lo, hi) with hi exclusive"""
        if not self.cells:
            raise HullError('empty volume has no bounding box')
        xs, ys, zs = zip(*self.cells)
        return (min(xs), min(ys), min(zs)), (max(xs) + 1, max(ys) + 1, max(zs) + 1)

    def size(self):
        lo, hi = self.bbox()
        return tuple(h - l for l, h in zip(lo, hi))

    def exposed(self):
        """{cell: tuple of exposed face directions} for every cell with at least one empty face neighbour"""
        cs = self.cells
        out = {}
        for c in cs:
            e = tuple(d for d in FACES if (c[0] + d[0], c[1] + d[1], c[2] + d[2]) not in cs)
            if e:
                out[c] = e
        return out

    def surface(self):
        return Volume(self.exposed())

    def erode(self, n=1, mode='face'):
        """remove n layers: a cell survives when all its neighbours survive the previous layer; mode 'face' = the six face
        neighbours, 'box' = all 26 (also removes cells that only touch the outside diagonally, which keeps thin necks attached)"""
        offs = FACES if mode == 'face' else [d for d in ((x, y, z) for x in (-1, 0, 1) for y in (-1, 0, 1) for z in (-1, 0, 1)) if d != (0, 0, 0)]
        cs = self.cells
        for _ in range(n):
            cs = {c for c in cs if all((c[0] + d[0], c[1] + d[1], c[2] + d[2]) in cs for d in offs)}
        return Volume(cs)

    def dilate(self, n=1, mode='face'):
        """grow by n layers.  mode: 'face' (6 neighbours), 'edge' (18) or 'box' (26)"""
        offs = [d for d in ((x, y, z) for x in (-1, 0, 1) for y in (-1, 0, 1) for z in (-1, 0, 1))
                if d != (0, 0, 0) and (sum(map(abs, d)) <= {'face': 1, 'edge': 2, 'box': 3}[mode])]
        cs = set(self.cells)
        for _ in range(n):
            cs |= {(c[0] + d[0], c[1] + d[1], c[2] + d[2]) for c in cs for d in offs}
        return Volume(cs)

    def depth_layers(self, max_depth=None):
        """{cell: depth}: 1 = outer skin, 2 = next layer in, ... (cells deeper than max_depth are omitted)"""
        rest, depth, out = set(self.cells), 1, {}
        while rest and (max_depth is None or depth <= max_depth):
            skin = {c for c in rest if any((c[0] + d[0], c[1] + d[1], c[2] + d[2]) not in rest for d in FACES)}
            if not skin:
                skin = set(rest)
            for c in skin:
                out[c] = depth
            rest -= skin
            depth += 1
        return out

    def components(self):
        """6-connected components, largest first"""
        todo, comps = set(self.cells), []
        while todo:
            seed = todo.pop()
            comp, stack = {seed}, [seed]
            while stack:
                c = stack.pop()
                for d in FACES:
                    n = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
                    if n in todo:
                        todo.discard(n)
                        comp.add(n)
                        stack.append(n)
            comps.append(Volume(comp))
        comps.sort(key=len, reverse=True)
        return comps

    def is_connected(self):
        return len(self.components()) <= 1

    def largest_component(self):
        comps = self.components()
        return comps[0] if comps else Volume()

    def hollow(self, wall=1, bulkhead_every=None, deck_every=None, keel_every=None, thickness=1, check=True):
        """Shell with a `wall`-cell skin (face erosion) and optional interior partitions so the hull stays one connected
        piece and keeps mass: bulkheads = planes normal to z every `bulkhead_every` cells (counted from the bbox
        start), decks = planes normal to y, keel_every = planes normal to x.  `thickness` = partition thickness.
        Raises HullError if hollowing splits a connected solid (a first pass erodes by face neighbours, a second keeps necks that
        only touch the outside diagonally)."""
        connected = not check or self.is_connected()
        for mode in ('face', 'box'):
            inner = self.erode(wall, mode)
            out = self.cells - inner.cells
            if inner:
                lo, _ = self.bbox()
                for ai, every in ((2, bulkhead_every), (1, deck_every), (0, keel_every)):
                    if every:
                        out |= {c for c in inner.cells if (c[ai] - lo[ai]) % every < thickness and (c[ai] - lo[ai]) >= every}
            res = Volume(out)
            if not check or not connected or res.is_connected():
                return res                       # (a thin neck that the face erosion detaches is kept by the 26-neighbour pass)
        raise HullError(f'hollow(wall={wall}) split the hull into {len(res.components())} pieces (a part is thinner than the wall)')


def _cs(o):
    return o.cells if isinstance(o, Volume) else (o if isinstance(o, (set, frozenset)) else set(o))


# =============================================================================================
# 2. solid -> block mapper
# =============================================================================================
ORIENTS = [(f, u) for f in sebp.DIR for u in sebp.DIR
           if sum(a * b for a, b in zip(sebp.DIR[f], sebp.DIR[u])) == 0]          # the 24 (Forward, Up) pairs
_SHAPES = ('Block', 'Slope', 'Corner', 'CornerInv', 'Slope2Base', 'Slope2Tip', 'Corner2Base', 'Corner2Tip',
           'RoundSlope', 'RoundCorner', 'RoundCornerInv')
KINDS = ('block', 'slope', 'corner', 'corner_inv', 'slope2base', 'slope2tip', 'corner2base', 'corner2tip',
         'round_slope', 'round_corner', 'round_corner_inv', 'half', 'half_slope', 'panel', 'slab',
         'window_slope', 'window_flat')


_SHAPE_OF = dict(zip(KINDS[:11], _SHAPES))


def subtype(kind, grid, heavy=False):
    """real block subtype of a hull shape for a grid size ('Small' | 'Large') in the light or heavy variant"""
    L = grid == 'Large'
    if kind in _SHAPE_OF:
        return ('Large' if L else 'Small') + ('Heavy' if heavy else '') + 'BlockArmor' + _SHAPE_OF[kind]
    if kind in ('half', 'slab'):
        return ('Large' if L else '') + ('Heavy' if heavy else '') + 'HalfArmorBlock'
    if kind == 'half_slope':
        return ('Large' if L else '') + ('Heavy' if heavy else '') + 'HalfSlopeArmorBlock'
    if kind == 'panel':
        return ('Large' if L else 'Small') + 'ArmorPanel' + ('Heavy' if heavy else 'Light')
    if kind in ('window_slope', 'window_flat'):
        return ('' if L else 'Small') + 'Window1x1' + ('Slope' if kind == 'window_slope' else 'Flat')
    raise ValueError(kind)


def _check_names():
    """every name this module can emit must exist in defs.json for its grid size and be non-DLC"""
    for grid in ('Small', 'Large'):
        for kind in KINDS:
            for heavy in (False, True):
                sub = subtype(kind, grid, heavy)
                k = sebp.defkey('MyObjectBuilder_CubeBlock', sub)
                assert k, f'hulls: {sub} is not in defs.json'
                d = sebp.DEFS[k]
                assert not d['dlc'], f'hulls: {sub} needs DLC {d["dlc"]}'
                assert d['grid'] == grid, f'hulls: {sub} is a {d["grid"]} grid block'
                assert tuple(d['size']) == (1, 1, 1), f'hulls: {sub} is not 1x1x1'
_check_names()

_STATS = None


def stats_db():
    global _STATS
    if _STATS is None:
        with open(os.path.join(ROOT, 'defs_stats.json')) as f:
            _STATS = json.load(f)['blocks']
    return _STATS


def block_stat(b):
    """(mass_kg, hp, pcu) of a sebp block dict from defs_stats.json"""
    s = stats_db().get(b['key']) or {}
    return (s.get('mass_kg') or b.get('mass') or 0, (s.get('integrity') or {}).get('sum_of_components') or 0, s.get('pcu') or 0)


def cell_m(grid):
    return 0.5 if grid == 'Small' else 2.5


# ---- mount-area tables (the one source of truth for orientation: sebp.mount_rects) ----
def _faces_of(bp, b):
    """{(cell, outward direction): mount area} of block b in grid space, exactly as sebp.attachment sees the rectangles"""
    out = {}
    for (axis, pc, ns, lo, hi, ex, pr) in bp.mount_rects(b):
        oa = _others(axis)
        plane = int(round(pc))
        ca = plane - 1 if ns > 0 else plane
        d = [0, 0, 0]
        d[axis] = ns
        d = tuple(d)
        a0, a1 = lo[0] - sebp.SHRINK, hi[0] + sebp.SHRINK
        b0, b1 = lo[1] - sebp.SHRINK, hi[1] + sebp.SHRINK
        for i in range(math.floor(a0 + EPS), math.ceil(a1 - EPS)):
            wa = min(a1, i + 1) - max(a0, i)
            if wa <= EPS:
                continue
            for j in range(math.floor(b0 + EPS), math.ceil(b1 - EPS)):
                wb = min(b1, j + 1) - max(b0, j)
                if wb <= EPS:
                    continue
                c = [0, 0, 0]
                c[axis], c[oa[0]], c[oa[1]] = ca, i, j
                out[(tuple(c), d)] = out.get((tuple(c), d), 0.0) + wa * wb
    return out


_AREAS = {}


def orient_areas(sub, grid):
    """{(Forward, Up): {direction: mount area}} for a 1x1x1 block placed at the origin (24 orientations)"""
    key = (sub, grid)
    if key not in _AREAS:
        scratch = sebp.Blueprint('areas', grid)
        table = {}
        for f, u in ORIENTS:
            b = scratch.add('MyObjectBuilder_CubeBlock', sub, (0, 0, 0), fwd=f, up=u)
            table[(f, u)] = {d: a for (c, d), a in _faces_of(scratch, b).items()}
            scratch.blocks.pop()
        _AREAS[key] = table
    return _AREAS[key]


_PICK = {}


def pick_orientation(sub, grid, open_dirs, need=()):
    """The (Forward, Up) whose zero-mount faces are EXACTLY `open_dirs` (the faces that must look at empty space) and, among
    those, the one whose mount areas are closest to `need`: a dict {direction: wanted area} or an iterable of directions
    (wanted area 1.0 = a full face against the hull).  None when no orientation fits."""
    tgt = dict(need) if isinstance(need, dict) else {d: 1.0 for d in need}
    key = (sub, grid, frozenset(open_dirs), frozenset(tgt.items()))
    if key not in _PICK:
        best = None
        for o in ORIENTS:
            area = orient_areas(sub, grid)[o]
            if {d for d in FACES if area.get(d, 0) <= 0.01} != set(open_dirs):
                continue
            score = -sum((area.get(d, 0) - t) ** 2 for d, t in tgt.items())
            if best is None or score > best[0] + 1e-9:
                best = (score, o)
        _PICK[key] = best[1] if best else None
    return _PICK[key]


def _axes_distinct(dirs):
    return len({axis_of(d) for d in dirs}) == len(dirs)


def classify(exposed):
    """catalogue section 6.3: exposed-face pattern -> shape kind"""
    n = len(exposed)
    if n <= 1:
        return 'block'
    if n <= 3 and _axes_distinct(exposed):
        return 'slope' if n == 2 else 'corner'
    return 'block'


# ---- colours ----
def hsv_mask(h_deg, s=1.0, v=1.0):
    """normal HSV (hue in degrees, s and v in 0..1) -> the ColorMaskHSV triple sebp writes.  The mask stores hue 0..1 and
    saturation/value as offsets from the game's palette base (0.8 and 0.55); matches the repo palette: DARK (0,-0.81,-0.55)
    is black and the default (0,-0.8,-0.3) a dark grey."""
    return ((h_deg % 360) / 360.0, round(max(-1.0, min(1.0, s - 0.8)), 3), round(max(-1.0, min(1.0, v - 0.55)), 3))


GREY = (0.0, -0.8, -0.3)
DARK_GREY = (0.0, -0.8, -0.45)
LIGHT_GREY = (0.0, -0.8, -0.1)
WHITE = (0.0, -0.8, 0.3)
BLACK = (0.0, -0.81, -0.55)
RED = (0.0, 0.2, 0.0)
ORANGE = (0.08, 0.35, 0.05)
YELLOW = (0.15, 0.4, 0.1)
BLUE = (0.58, 0.1, -0.05)
GLASS = (0.55, -0.4, -0.2)


# ---- zones: heavy/light, colour, flat belly, shape variant per region ----
def region(x=None, y=None, z=None):
    """predicate: cell inside per-axis ranges [lo, hi) (None = unbounded; lo or hi may be None)"""
    rs = (x, y, z)
    def f(c):
        for i, r in enumerate(rs):
            if r is not None and ((r[0] is not None and c[i] < r[0]) or (r[1] is not None and c[i] >= r[1])):
                return False
        return True
    return f


def stripe(axis, period, width=1, offset=0, lo=None, hi=None):
    """predicate: cells where (coordinate - offset) mod period < width, optionally only inside [lo, hi)"""
    ai = _axis(axis)
    return lambda c: (lo is None or c[ai] >= lo) and (hi is None or c[ai] < hi) and (c[ai] - offset) % period < width


def any_of(*preds):
    ps = [_pred(p) for p in preds]
    return lambda c: any(p(c) for p in ps)


def all_of(*preds):
    ps = [_pred(p) for p in preds]
    return lambda c: all(p(c) for p in ps)


class Zone:
    """A rule layer.  For every cell the FIRST matching zone that sets an attribute wins that attribute (CSS-like), so
    stripes/markings go first and the base livery last.
      where  None (everywhere) | Volume | set | callable(cell) -> bool
      heavy  True/False (None = decided by Zones.heavy_depth / default)
      color  ColorMaskHSV triple or callable(cell) -> triple
      flat   directions whose exposure is ignored when choosing the shape (hard edge: 'Down' keeps a flat belly)
      round  True: RoundSlope/RoundCorner instead of Slope/Corner
      kind   sebp block kind tag (selects the thumbnail palette colour)"""
    def __init__(self, name='zone', where=None, heavy=None, color=None, flat=None, round=None, kind=None):
        self.name, self.match, self.heavy, self.color, self.round, self.kind = name, _pred(where), heavy, color, round, kind
        self.flat = None if flat is None else frozenset(dvec(d) for d in flat)


class Zones:
    def __init__(self, *zones, heavy_depth=None, default_heavy=False, default_color=GREY, default_kind='armor'):
        self.zones, self.heavy_depth, self.default_heavy = list(zones), heavy_depth, default_heavy
        self.default_color, self.default_kind = default_color, default_kind

    def resolve(self, cell, depth=None):
        heavy = color = flat = rnd = kind = None
        names = []
        for z in self.zones:
            if not z.match(cell):
                continue
            names.append(z.name)
            if heavy is None and z.heavy is not None: heavy = z.heavy
            if color is None and z.color is not None: color = z.color(cell) if callable(z.color) else z.color
            if flat is None and z.flat is not None: flat = z.flat
            if rnd is None and z.round is not None: rnd = z.round
            if kind is None and z.kind is not None: kind = z.kind
        if heavy is None:
            heavy = self.default_heavy if self.heavy_depth is None or depth is None else depth <= self.heavy_depth
        return dict(heavy=heavy, color=color or self.default_color, flat=flat or frozenset(), round=bool(rnd),
                    kind=kind or self.default_kind, zones=names)


class HullResult:
    """What build_hull() produced."""
    def __init__(self, bp, env, fill, support):
        self.bp, self.env, self.fill, self.support = bp, env, fill, support
        self.blocks = []                  # sebp blocks added by the hull builder, in order
        self.cell_block = {}              # cell -> block
        self.cell_kind = {}               # cell -> kind ('block', 'slope', ...)
        self.exposure = {}                # cell -> frozenset of exposed directions used for the shape
        self.flush = {}                   # cell -> directions that were exposed but deliberately left flat
        self.fillets = []                 # cells outside the envelope filled by concave fillets
        self.greebles = []                # panel/slab blocks added by greeble()
        self.reserved = {}                # name -> Volume
        self.clear = set()
        self.counts = Counter()

    def check(self):
        """raises HullError unless every block's mount faces meet the hull (see check_mount_faces)"""
        return check_mount_faces(self.bp, self.support, self.flush, blocks=self.blocks + self.greebles)

    def validate(self, fast=True):
        """mount-face check + sebp validation (overlap, attachment, one connected piece, DLC); fast=True uses fast_validate(),
        which is equivalent to sebp.Blueprint.validate() but linear (sebp's is quadratic: 8 s for 9,000 blocks)"""
        self.check()
        return fast_validate(self.bp) if fast else self.bp.validate()

    def report(self):
        return report(self.bp)


def _emit(res, c, kind, E, st, flush=(), orient=None, need=()):
    bp = res.bp
    heavy = st['heavy']
    if kind in ('slope', 'corner') and st['round']:
        kind = 'round_' + kind
    sub = subtype(kind, bp.grid, heavy)
    if orient is None:
        orient = ('Forward', 'Up') if kind == 'block' else pick_orientation(sub, bp.grid, [dvec(d) if isinstance(d, str) else d for d in E], need)
    if orient is None:
        raise HullError(f'no orientation of {sub} has open faces {sorted(DNAME[d] for d in E)} (cell {c}); refusing to emit it')
    b = bp.add('MyObjectBuilder_CubeBlock', sub, c, fwd=orient[0], up=orient[1], color=st['color'], kind=st['kind'])
    b['mass'] = block_stat(b)[0]
    res.blocks.append(b)
    res.cell_block[c] = b
    res.cell_kind[c] = kind
    res.exposure[c] = frozenset(E)
    if flush:
        res.flush[c] = frozenset(flush)
    res.counts[kind] += 1
    return b


def build_hull(bp, envelope, *, fill=None, reserved=None, clear=None, skin_around=(), zones=None,
               slope2=True, slope2_sides=True, fillets=False, windows=None, flat=(), round_edges=False, check=True, require_connected=True):
    """Fill `envelope` (a Volume = the outer solid) with armour blocks inside Blueprint `bp` and return a HullResult.

      envelope     the outer solid; exposure of a face is decided against it (never against a hollowed interior)
      fill         cells that get blocks (default = envelope); pass envelope.hollow(...) for a shell
      reserved     {name: cells} (or one iterable) the caller fills with systems: never armoured, counted as solid for the
                   neighbours' shapes (so a turret / thruster / hangar wall gets a full mount face toward it)
      clear        cells that must stay empty (exhaust, muzzle): removed from the hull and registered in bp.reserve
      skin_around  names in `reserved` that keep a one-cell armour skin (edges included) even where `fill` is hollow
      zones        Zones(...) for light/heavy, colours, flat edges
      slope2       emit Slope2Base+Slope2Tip pairs where the top drops one cell every two (set slope2_sides=False to
                   require pure ramps without exposed flanks)
      fillets      add Slope / CornerInv blocks in concave edges and vertices (cells outside the envelope)
      windows      predicate/Volume (or a list): slope cells inside become Window1x1Slope, single-face block cells Window1x1Flat
      flat         directions ignored for every cell (hard edges), e.g. ('Down',)
      require_connected  raise HullError when fill + reserved is not one 6-connected piece
    The result is checked with check_mount_faces() unless check=False; it raises HullError rather than return blocks that do not meet."""
    zones = zones or Zones()
    env = Volume(envelope)
    if reserved is None:
        reserved = {}
    elif not isinstance(reserved, dict):
        reserved = {'reserved': reserved}
    reserved = {k: Volume(v) for k, v in reserved.items()}
    res_all = set().union(*[v.cells for v in reserved.values()]) if reserved else set()
    clear_set = set(clear or ())
    env = Volume(env.cells - clear_set)
    if clear_set & res_all:
        raise HullError(f'cells both reserved and cleared: {sorted(clear_set & res_all)[:4]}')
    fillv = (Volume(fill) & env) if fill is not None else env.copy()
    for name in skin_around:
        if name not in reserved:
            raise HullError(f'skin_around: no reserved volume named {name!r}')
        fillv = fillv | (reserved[name].dilate(1, 'edge') & env)
    fillv = Volume(fillv.cells - res_all - clear_set)
    support = env.cells | res_all
    if require_connected and fillv:
        pieces = Volume(fillv.cells | res_all).components()
        if len(pieces) > 1:
            raise HullError(f'the hull is {len(pieces)} separate pieces (sizes {[len(p) for p in pieces[:5]]}); a piece near '
                            f'{sorted(pieces[1].cells)[0]} is not joined to the main body')
    res = HullResult(bp, env, fillv, support)
    res.reserved, res.clear = reserved, clear_set
    for c in clear_set:
        bp.reserve[c] = 'hull clear cell'
    win_preds = [_pred(w) for w in (windows if isinstance(windows, list) else [windows] if windows is not None else [])]
    flat = frozenset(dvec(d) for d in flat)
    depth = env.depth_layers(zones.heavy_depth) if zones.heavy_depth is not None else {}

    # ---- exposure of every fill cell against env + reserved ----
    E = {}
    sty = {}
    for c in fillv.cells:
        st = sty[c] = zones.resolve(c, depth.get(c, (zones.heavy_depth or 0) + 1))
        if round_edges:
            st['round'] = True
        ex = [d for d in FACES if (c[0] + d[0], c[1] + d[1], c[2] + d[2]) not in support]
        ign = [d for d in ex if d in flat or d in st['flat']]
        E[c] = (frozenset(ex) - frozenset(ign), frozenset(ign))
    claimed = set()

    # ---- 2-run ramps: Slope2Base + Slope2Tip ----
    if slope2:
        for c in sorted(fillv.cells):
            ec = E[c][0]
            if c in claimed or len(ec) < 2 or len(ec) > 4:
                continue
            for a in ec:
                done = False
                for r in ec:
                    if a == r or axis_of(a) == axis_of(r):
                        continue
                    L = ec - {a, r}
                    lat = {d for d in FACES if axis_of(d) not in (axis_of(a), axis_of(r))}
                    if (L and not slope2_sides) or not L <= lat:
                        continue
                    c0 = (c[0] - r[0], c[1] - r[1], c[2] - r[2])
                    up0 = (c0[0] - r[0], c0[1] - r[1], c0[2] - r[2])
                    if c0 in claimed or c0 not in E or E[c0][0] != L | {a} or up0 not in support:
                        continue
                    landing = (c[0] + r[0] - a[0], c[1] + r[1] - a[1], c[2] + r[2] - a[2])
                    if landing not in support and _neg(a) not in E[c][1]:
                        continue                                # not a staircase step: a plain edge of the hull
                    # base: full front (toward the high end) and full bottom; tip: the half-height strip that meets the base, full bottom
                    for cc, k, ex, want in ((c0, 'slope2base', {a}, {_neg(r): 1.0, _neg(a): 1.0}),
                                            (c, 'slope2tip', {a, r}, {_neg(r): 0.5, _neg(a): 1.0})):
                        sub = subtype(k, bp.grid, sty[cc]['heavy'])
                        o = pick_orientation(sub, bp.grid, ex, want)
                        if o is None:
                            raise HullError(f'no orientation for {sub}')
                        _emit(res, cc, k, E[cc][0], sty[cc], E[cc][1], orient=o)
                        claimed.add(cc)
                    done = True
                    break
                if done:
                    break

    # ---- everything else by the exposed-face rules ----
    wins = {}
    for c in sorted(fillv.cells):
        if c in claimed:
            continue
        ec, ign = E[c]
        kind = classify(ec)
        st = sty[c]
        if win_preds and any(p(c) for p in win_preds) and (kind == 'slope' or (kind == 'block' and len(ec) == 1)):
            wk = 'window_slope' if kind == 'slope' else 'window_flat'
            o = pick_orientation(subtype(wk, bp.grid), bp.grid, ec)
            if o is not None:
                wst = dict(st, kind='glass', color=st['color'])
                _emit(res, c, wk, ec, wst, ign, orient=o)
                wins[c] = (kind, st)
                continue
        if kind == 'block':
            _emit(res, c, 'block', ec, st, ign)
        else:
            _emit(res, c, kind, ec, st, ign)

    # ---- concave fillets ----
    if fillets:
        _fillets(res, zones, sty, support, clear_set)

    # ---- windows must attach: a glass block with no neighbour through its thin mounts goes back to armour ----
    #      (and a glass block's thin mounts must not be the only link that holds a cluster of armour to the rest)
    def _revert(c):
        kind, st = wins.pop(c)
        b = res.cell_block[c]
        bp.blocks.remove(b)
        res.blocks.remove(b)
        res.counts[res.cell_kind[c]] -= 1
        ec, ign = E[c]
        del res.cell_block[c], res.cell_kind[c]
        _emit(res, c, kind, ec, st, ign)

    while wins:
        adj = fast_attachment(bp)
        bad = {c for c in wins if not adj[id(res.cell_block[c])]}
        if not bad and require_connected:
            comps = bp.components(adj)
            if len(comps) > 1:
                comps.sort(key=len, reverse=True)
                loose = {id(b) for comp in comps[1:] for b in comp}
                for c in wins:
                    if id(res.cell_block[c]) in loose or any(
                            id(res.cell_block.get(_add(c, d))) in loose for d in FACES):
                        bad.add(c)
        if not bad:
            break
        for c in bad:
            _revert(c)
    if check:
        res.check()
    return res


def _fillets(res, zones, sty, support, clear_set):
    bp = res.bp
    cand = res.env.dilate(1, 'face').cells - support - clear_set
    taken = {c for b in bp.blocks for c in bp.cells(b)}
    for o in sorted(cand):
        if o in taken or o in bp.reserve:
            continue
        S = [d for d in FACES if (o[0] + d[0], o[1] + d[1], o[2] + d[2]) in support]
        if len(S) not in (2, 3) or not _axes_distinct(S):
            continue
        nb = [(o[0] + d[0], o[1] + d[1], o[2] + d[2]) for d in S]
        if any(res.cell_kind.get(n) != 'block' for n in nb):
            continue                                    # only seat a fillet on plain cubes: they present a full face to it
        pairs = [(S[i], S[j]) for i in range(len(S)) for j in range(i + 1, len(S))]
        if any((o[0] + a[0] + b[0], o[1] + a[1] + b[1], o[2] + a[2] + b[2]) not in support for a, b in pairs):
            continue                                    # the inner edge/vertex line must be solid, else it is not a concave notch
        st = zones.resolve(o, None)
        if len(S) == 2:
            _emit(res, o, 'slope', [_neg(d) for d in S], st)
        else:
            _emit(res, o, 'corner_inv', [], st, need=S)
        res.fillets.append(o)


# =============================================================================================
# 3. "mount faces hull" check
# =============================================================================================
def check_mount_faces(bp, support, flush=None, blocks=None):
    """The check sebp.validate() cannot do.  For every shaped 1x1x1 CubeBlock (slope, corner, window, panel ...; plain
    cubes are exempt) compare the block's own mount faces (from sebp.mount_rects) with the hull `support` (set of cells
    that are solid or reserved):
      A. every FULL-area mount face must look at a support cell (or at a direction listed in flush[cell], an exposure the
         designer chose to leave flat);
      B. every face with NO mount (the slanted / open faces) must look at empty space, never at a support cell.
    A slope turned the wrong way fails B (its open face is pressed against the hull) and A (its full faces point into the
    air) even though it still touches a neighbour and so passes sebp.validate().  Raises HullError listing violations."""
    flush = flush or {}
    problems = []
    for b in (bp.blocks if blocks is None else blocks):
        if b['xtype'] != 'MyObjectBuilder_CubeBlock' or tuple(b['size']) != (1, 1, 1):
            continue
        c = tuple(b['min'])
        area = orient_areas(b['sub'], bp.grid)[(b['fwd'], b['up'])]
        if all(area.get(d, 0) >= 0.99 for d in FACES):
            continue                                                   # a plain cube fits any neighbourhood
        for d in FACES:
            n = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
            a = area.get(d, 0)
            if a >= 0.99 and n not in support and d not in flush.get(c, ()):
                problems.append(f'{b["sub"]}@{c} {b["fwd"]}/{b["up"]}: full mount on {DNAME[d]} faces empty space')
            elif a <= 0.01 and n in support:
                problems.append(f'{b["sub"]}@{c} {b["fwd"]}/{b["up"]}: open face {DNAME[d]} is pressed against the hull')
    if problems:
        raise HullError(f'{len(problems)} block(s) whose mount points do not meet the hull:\n  ' + '\n  '.join(problems[:12])
                        + (f'\n  ... and {len(problems) - 12} more' if len(problems) > 12 else ''))
    return True


# ---- fast validation for big hulls ----
_RECT_BASE = {}


def _rects_fast(bp, b):
    """sebp.Blueprint.mount_rects(b), computed once per (definition, orientation) at the origin and translated"""
    key = (b['key'], b['fwd'], b['up'])
    base = _RECT_BASE.get(key)
    if base is None:
        base = _RECT_BASE[key] = bp.mount_rects(dict(b, min=(0, 0, 0)))
    mn = b['min']
    out = []
    for (axis, pc, ns, lo, hi, ex, pr) in base:
        oa = _others(axis)
        out.append((axis, pc + mn[axis], ns, (lo[0] + mn[oa[0]], lo[1] + mn[oa[1]]), (hi[0] + mn[oa[0]], hi[1] + mn[oa[1]]), ex, pr))
    return out


def fast_attachment(bp):
    """Same result as sebp.Blueprint.attachment() ({id(block): set of ids of attached blocks}) but linear in the number of blocks:
    rectangles are bucketed by plane and cell so only rectangles on the same cell face are compared (sebp compares every pair on a
    plane, which is quadratic for a big hull)."""
    index = {}
    rects = {}
    for b in bp.blocks:
        rs = rects[id(b)] = _rects_fast(bp, b)
        for r in rs:
            for i in range(math.floor(r[3][0]), math.floor(r[4][0]) + 1):
                for j in range(math.floor(r[3][1]), math.floor(r[4][1]) + 1):
                    index.setdefault((r[0], r[1], r[2], i, j), []).append((b, r))
    adj = {id(b): set() for b in bp.blocks}
    for b in bp.blocks:
        for (axis, pc, ns, lo, hi, ex, pr) in rects[id(b)]:
            seen = set()
            for i in range(math.floor(lo[0]), math.floor(hi[0]) + 1):
                for j in range(math.floor(lo[1]), math.floor(hi[1]) + 1):
                    for (nb, (a2, pc2, ns2, lo2, hi2, ex2, pr2)) in index.get((axis, pc, -ns, i, j), ()):
                        if nb is b or id(nb) in seen:
                            continue
                        if ((ex & pr2) or (pr & ex2)) and b['key'] != nb['key']:
                            continue
                        if lo[0] <= hi2[0] and lo2[0] <= hi[0] and lo[1] <= hi2[1] and lo2[1] <= hi[1]:
                            seen.add(id(nb))
                            adj[id(b)].add(id(nb))
                            adj[id(nb)].add(id(b))
    return adj


def fast_validate(bp, require_connected=True):
    """sebp.Blueprint.validate() with fast_attachment(): overlap, reserved cells, DLC, every block attached, one connected piece"""
    occ = bp.occupancy()
    for c, why in bp.reserve.items():
        assert c not in occ, f'{bp.name}: {why} blocked by {occ[c]["name"] or occ[c]["sub"]} at {c}'
    assert bp.allow_dlc or not bp.dlc_used(), f'{bp.name}: uses DLC blocks {bp.dlc_used()}'
    adj = fast_attachment(bp)
    problems = [b for b in bp.blocks if not adj[id(b)]]
    assert not problems, f'{bp.name}: not attached: ' + ', '.join(f'{p["name"] or p["sub"]}@{p["min"]}' for p in problems[:20])
    if require_connected and bp.blocks:
        comps = bp.components(adj)
        lost = [b for c in comps if c[0] is not bp.blocks[0] and bp.blocks[0] not in c for b in c]
        assert not lost, f'{bp.name}: separate pieces: ' + ', '.join(f'{p["name"] or p["sub"]}@{p["min"]}' for p in lost[:20])
    return occ


# =============================================================================================
# 4. style helpers
# =============================================================================================
def nose_cone(axis, base, length, center, half, sign=-1, tip_half=(0.0, 0.0), tip_shift=(0.0, 0.0), shape='rect'):
    """A tapered nose: `length` cells from the boundary `base` along `axis` ('z' for a bow) toward `sign` (-1 = bow at -z).
    `center`=(cu,cv) and `half`=(hu,hv) give the cross-section at the base (edge coords, u,v = the other two axes in x<y<z
    order); it narrows to `tip_half` (0 = point) around center + tip_shift (droop/raise the tip).  shape: rect = wedge/chisel
    nose, ellipse = cone, diamond, octagon."""
    cu, cv = center
    base_r = (cu - half[0], cu + half[0], cv - half[1], cv + half[1])
    tu, tv = cu + tip_shift[0], cv + tip_shift[1]
    tip_r = (tu - tip_half[0], tu + tip_half[0], tv - tip_half[1], tv + tip_half[1])
    if sign < 0:
        return Volume.loft(axis, base - length, base, tip_r, base_r, shape)
    return Volume.loft(axis, base, base + length, base_r, tip_r, shape)


def swept_wing(root, span, chord, tip_chord=None, sweep=0.0, thick=2, tip_thick=1, lift=0.0,
               span_dir='+x', chord_dir='+z', section='wedge', thick_center=False):
    """A swept wing or fin: a thin tapered plate.  `root` = cell of the leading-edge root corner; it extends `span` cells
    along span_dir; the chord (length) runs along chord_dir from the leading edge and shrinks to tip_chord; `sweep` shifts the
    leading edge along chord_dir by that many cells per cell of span (0.5 = 26 degrees back); the thickness (third axis, +
    direction) goes thick -> tip_thick; `lift` = dihedral cells per cell of span.
    section: 'slab' (constant), 'wedge' (thick at the leading edge, thin trailing edge) or 'diamond' (thin both edges).
    thick_center=True centres the thickness on the root coordinate (vertical centre-line fins).
    Build one side and mirror(): `swept_wing(...).mirror('x', at)`."""
    sp, ch = dvec(span_dir), dvec(chord_dir)
    sa, ca = axis_of(sp), axis_of(ch)
    ta = 3 - sa - ca
    tip_chord = chord if tip_chord is None else tip_chord
    out = set()
    for i in range(span):
        f = i / max(1, span - 1)
        c_i = max(1, int(round(chord + (tip_chord - chord) * f)))
        th_i = max(1, int(round(thick + (tip_thick - thick) * f)))
        lead = int(round(sweep * i))
        off = int(round(lift * i))
        for k in range(c_i):
            u = (k + 0.5) / c_i
            if section == 'wedge':
                t = max(1, int(round(th_i * (1 - u * 0.999) + 0.25)))
            elif section == 'diamond':
                t = max(1, int(round(th_i * (1 - abs(2 * u - 1) * 0.999) + 0.25)))
            else:
                t = th_i
            t0 = -(t // 2) if thick_center else 0
            for j in range(t0, t0 + t):
                cell = [0, 0, 0]
                cell[sa] = root[sa] + sp[sa] * i
                cell[ca] = root[ca] + ch[ca] * (lead + k)
                cell[ta] = root[ta] + off + j
                out.add(tuple(cell))
    return Volume(out)


class Nacelle:
    """engine pod: .volume, .aft_cells (cells of the rear end face), .exhaust_dir (unit vector out of the nozzle),
    .nozzle (the cell just behind the centre of the rear face, where a thruster goes)"""
    def __init__(self, volume, aft_cells, exhaust_dir, nozzle):
        self.volume, self.aft_cells, self.exhaust_dir, self.nozzle = volume, aft_cells, exhaust_dir, nozzle


def nacelle(center, start, length, radius, axis='z', sign=1, rear_taper=0.6, intake_taper=0.5, taper_len=None, shape='ellipse'):
    """Engine nacelle along `axis`, occupying cells start .. start+length-1.  `center`=(cu,cv) are edge coordinates of the two
    other axes (x<y<z order), radius = (ru, rv) or one number.  The exhaust points toward `sign` (+1: out of the +z end,
    the stern); the intake is the opposite end.  The exhaust end tapers to rear_taper*radius, the intake to intake_taper*radius,
    each over taper_len cells (default length//3)."""
    ai = _axis(axis)
    ru, rv = (radius, radius) if isinstance(radius, (int, float)) else radius
    cu, cv = center
    tl = taper_len or max(1, length // 3)
    i0, i1 = start, start + length
    def rect(s):
        return (cu - ru * s, cu + ru * s, cv - rv * s, cv + rv * s)
    if sign > 0:
        parts = [Volume.loft(ai, i0, i0 + tl, rect(intake_taper), rect(1.0), shape),
                 Volume.loft(ai, i0 + tl, i1 - tl, rect(1.0), rect(1.0), shape),
                 Volume.loft(ai, i1 - tl, i1, rect(1.0), rect(rear_taper), shape)]
        end = i1 - 1
    else:
        parts = [Volume.loft(ai, i0, i0 + tl, rect(rear_taper), rect(1.0), shape),
                 Volume.loft(ai, i0 + tl, i1 - tl, rect(1.0), rect(1.0), shape),
                 Volume.loft(ai, i1 - tl, i1, rect(1.0), rect(intake_taper), shape)]
        end = i0
    vol = Volume().union(*parts)
    aft = Volume(c for c in vol.cells if c[ai] == end)
    ua, va = _others(ai)
    ctr = [0, 0, 0]
    ctr[ai], ctr[ua], ctr[va] = end + sign, int(math.floor(cu)), int(math.floor(cv))
    d = [0, 0, 0]
    d[ai] = sign
    return Nacelle(vol, aft, tuple(d), tuple(ctr))


def spine(z0, z1, y_base, height, width=1, x_at=0, profile='blade', ventral=False):
    """A dorsal (or ventral) spine along z: a side-profile polygon extruded `width` cells across x (centred on the plane
    x=x_at).  profile: 'blade' (rises at the bow, flat, steps down at the stern), 'ramp' (rises toward the stern),
    'dome' (rounded back).  `y_base` = the y edge coordinate of the hull surface it sits on; ventral=True hangs below it."""
    L = z1 - z0
    h = height
    prof = {
        'blade': [(z0, 0), (z0 + 0.25 * L, h), (z1 - 0.2 * L, h), (z1, 0.35 * h), (z1, 0)],
        'ramp': [(z0, 0), (z0, 0.3 * h), (z1, h), (z1, 0)],
        'dome': [(z0, 0)] + [(z0 + L * (0.5 - 0.5 * math.cos(math.pi * t / 8)), h * math.sin(math.pi * t / 8)) for t in range(1, 8)] + [(z1, 0)],
    }[profile]
    sgn = -1 if ventral else 1
    poly = [(y_base + sgn * hh, z) for z, hh in prof]       # prism wants (u, v) = (y, z) for an extrusion along x
    x0 = x_at - width / 2.0
    vol = Volume.prism(poly, 'x', math.floor(x0 + 0.5 + EPS), math.floor(x0 + 0.5 + EPS) + width)
    return vol


def stepped_deck(lo, hi, steps=3, inset=(1, 1), rise=1, front_inset=None):
    """Superstructure of `steps` stacked boxes.  Base box [lo, hi); every next box is `rise` higher and inset by inset=(x, z)
    cells on each side (front_inset: bow-side inset in z, default = inset[1]; the stern side keeps inset[1])."""
    out = Volume()
    fi = inset[1] if front_inset is None else front_inset
    for s in range(steps):
        out = out | Volume.box((lo[0] + s * inset[0], lo[1] + s * rise, lo[2] + s * fi),
                               (hi[0] - s * inset[0], lo[1] + (s + 1) * rise, hi[2] - s * inset[1]))
    return out


class Hardpoint:
    """A place on the hull for a turret/weapon: .cell = first free cell above the surface, .normal = outward unit vector,
    .base = hull cell it stands on, .cells = footprint cells outside the hull, .mirror = partner on the other side or None"""
    def __init__(self, cell, normal, base, cells):
        self.cell, self.normal, self.base, self.cells, self.mirror = cell, normal, base, cells, None

    def column(self, height):
        """the footprint extended `height` layers outward (the cells a turret of that height occupies)"""
        n = self.normal
        return [(c[0] + n[0] * k, c[1] + n[1] * k, c[2] + n[2] * k) for c in self.cells for k in range(height)]

    def __repr__(self):
        return f'Hardpoint({self.cell}, {DNAME[self.normal]})'


def hardpoints(vol, normals=('Up',), footprint=(1, 1), height=1, margin=0, spacing=None, where=None, avoid=(), mirror=None, pairs=True):
    """Surface sites with outward normals, to drop turrets/hardpoints on.  A site is a footprint of cells that all stand on
    FLAT hull (each base cell exposes exactly the normal, so it will be a plain cube) with `height` free cells above, and
    `margin` cells of hull around it in the surface plane.
      normals    names/vectors of the wanted outward directions
      footprint  (n1, n2) cells along the two surface axes (x<y<z order) from the anchor cell
      spacing    minimum Chebyshev distance between chosen sites (greedy, stable order)
      where      predicate on the anchor BASE cell     avoid  cells that may not be used
      mirror     ('x', at): only generate sites on the + side (or on the plane) and attach .mirror partners; with pairs=True the
                 returned list holds both members of every pair
    Returns [Hardpoint]; pass the .cells of the chosen sites to build_hull(reserved=...) before filling."""
    ex = vol.exposed()
    pred = _pred(where)
    avoid = set(avoid)
    out, taken = [], []
    mk = (_axis(mirror[0]), round(2 * mirror[1])) if mirror else None

    def refl(c):
        n = list(c)
        n[mk[0]] = mk[1] - 1 - c[mk[0]]
        return tuple(n)

    def at(c, t1, t2, i, j):
        n = list(c)
        n[t1] += i
        n[t2] += j
        return tuple(n)

    for nm in normals:
        n = dvec(nm)
        t1, t2 = _others(axis_of(n))
        for c in sorted(vol.cells):
            if ex.get(c) != (n,) or not pred(c):
                continue
            bases = [at(c, t1, t2, i, j) for i in range(footprint[0]) for j in range(footprint[1])]
            if any(ex.get(b) != (n,) for b in bases):
                continue
            tops = [_add(b, n) for b in bases]
            if any(q in avoid or any(_add(q, tuple(v * k for v in n)) in vol.cells for k in range(height)) for q in tops):
                continue
            if margin and not {at(b, t1, t2, i, j) for b in bases for i in range(-margin, margin + 1) for j in range(-margin, margin + 1)} <= vol.cells:
                continue
            side = 0
            if mk:
                s = {(2 * q[mk[0]] - (mk[1] - 1) > 0) - (2 * q[mk[0]] - (mk[1] - 1) < 0) for q in tops}
                if {refl(q) for q in tops} == set(tops):
                    side = 0
                elif s == {1}:
                    side = 1
                else:
                    continue
            if spacing and any(max(abs(c[i] - p[i]) for i in range(3)) < spacing for p in taken):
                continue
            hp = Hardpoint(tops[0], n, c, tops)
            if side == 1:
                mtops = sorted(refl(q) for q in tops)
                mbase = refl(c)
                if ex.get(mbase) is None or any(q in vol.cells or q in avoid for q in mtops):
                    continue
                m = Hardpoint(mtops[0], tuple(-v if i == mk[0] else v for i, v in enumerate(n)), mbase, mtops)
                hp.mirror, m.mirror = m, hp
                taken += [c, mbase]
                out.append(hp)
                if pairs:
                    out.append(m)
            else:
                taken.append(c)
                out.append(hp)
    return out


def greeble(res, kind='panel', density=0.2, pattern='scatter', every=3, normals=None, where=None, avoid=(), seed=1,
            color=None, heavy=False, max_count=None):
    """Raised plating on flat hull faces: a thin ArmorPanel ('panel') or a HalfArmorBlock slab ('slab') in the free cell
    outside every cube that shows exactly one face.  pattern: 'scatter' (random `density`), 'grid' (every `every`-th cell in both
    surface axes), 'lines' (every `every`-th row).  Cells in `avoid`, reserved/clear cells, other blocks and bp.reserve are never
    used.  Returns the blocks added (also stored in res.greebles)."""
    bp = res.bp
    rng = random.Random(seed)
    pred = _pred(where)
    nset = {dvec(n) for n in normals} if normals else None
    taken = {c for b in bp.blocks for c in bp.cells(b)} | set(avoid) | res.support | set(bp.reserve)
    added = []
    for c in sorted(res.cell_kind):
        if res.cell_kind[c] != 'block' or len(res.exposure[c]) != 1 or not pred(c):
            continue
        (e,) = tuple(res.exposure[c])
        if nset and e not in nset:
            continue
        o = _add(c, e)
        if o in taken:
            continue
        t1, t2 = _others(axis_of(e))
        if pattern == 'scatter':
            if rng.random() >= density:
                continue
        elif pattern == 'grid':
            if c[t1] % every or c[t2] % every:
                continue
        elif pattern == 'lines':
            if c[t1] % every:
                continue
        else:
            raise ValueError(pattern)
        sub = subtype(kind, bp.grid, heavy)
        orient = pick_orientation(sub, bp.grid, {e})
        if orient is None:
            raise HullError(f'no orientation of {sub} opens toward {DNAME[e]}')
        b = bp.add('MyObjectBuilder_CubeBlock', sub, o, fwd=orient[0], up=orient[1], color=color or DARK_GREY, kind='trim')
        b['mass'] = block_stat(b)[0]
        added.append(b)
        taken.add(o)
        if max_count and len(added) >= max_count:
            break
    res.greebles += added
    check_mount_faces(bp, res.support, None, blocks=added)
    return added


# =============================================================================================
# 5. cost / figure report
# =============================================================================================
def report(bp):
    """Figures for every block of `bp` (hull and systems): counts by subtype, mass (defs_stats.json mass_kg), HP (sum of component
    integrity), PCU, bounding box in cells and metres."""
    by, mass, hp, pcu = Counter(), 0.0, 0.0, 0
    lo = [10 ** 9] * 3
    hi = [-10 ** 9] * 3
    for b in bp.blocks:
        m, h, p = block_stat(b)
        by[b['sub']] += 1
        mass, hp, pcu = mass + m, hp + h, pcu + p
        for c in bp.cells(b):
            for i in range(3):
                lo[i], hi[i] = min(lo[i], c[i]), max(hi[i], c[i] + 1)
    cells = tuple(h - l for l, h in zip(lo, hi)) if bp.blocks else (0, 0, 0)
    cm = cell_m(bp.grid)
    return dict(name=bp.name, grid=bp.grid, blocks=len(bp.blocks), by_subtype=dict(by.most_common()), mass_kg=mass, hp=hp, pcu=pcu,
                bbox_cells=cells, bbox_m=tuple(round(n * cm, 2) for n in cells), cell_m=cm)


def format_report(rep, top=12):
    L = [f'{rep["name"]} ({rep["grid"]} grid): {rep["blocks"]} blocks, mass {rep["mass_kg"] / 1000:.1f} t, HP {rep["hp"] / 1000:.0f} k, '
         f'PCU {rep["pcu"]}, bbox {rep["bbox_cells"][0]}x{rep["bbox_cells"][1]}x{rep["bbox_cells"][2]} cells = '
         f'{rep["bbox_m"][0]:.1f} x {rep["bbox_m"][1]:.1f} x {rep["bbox_m"][2]:.1f} m (W x H x L)']
    for i, (s, n) in enumerate(rep['by_subtype'].items()):
        if i >= top:
            L.append(f'  ... {len(rep["by_subtype"]) - top} more subtypes')
            break
        L.append(f'  {n:6d}  {s}')
    return '\n'.join(L)


__all__ = ['Volume', 'build_hull', 'check_mount_faces', 'HullError', 'HullResult', 'Zone', 'Zones', 'region', 'stripe', 'any_of', 'all_of',
           'nose_cone', 'swept_wing', 'nacelle', 'spine', 'stepped_deck', 'hardpoints', 'greeble', 'report', 'format_report',
           'fast_validate', 'fast_attachment', 'subtype', 'hsv_mask', 'orient_areas', 'pick_orientation', 'classify']
