"""Voxel ship renderer with real slope / corner wedges, several views, sub-grids (docked pods) in world position.

    import _shiprender as R
    R.render_sheet(bp, outdir, stem)            # iso views + top / side / front / belly, one PNG per view and a contact sheet
    R.render_view(bp, path, az=35, el=28)       # one view

Why not sebp.Blueprint.thumb: it draws every block as a full cube, so a chisel nose looks like a staircase.  Here every hull block
that hulls.build_hull made (Slope, Corner, Slope2Base/Tip, window slopes) is cut by the half space that its open (zero-mount) faces define,
so what you see is close to what the game draws.  Large systems (turrets, tanks, rails) are boxes in their kind colour.  Needs PIL.

Conventions: cell x = Right, y = Up, z = Backward (bow at -z).  Camera azimuth 0 = looking at the bow from the front, 90 = from the starboard side.
"""
import os, math, sys
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
if TOOLS not in sys.path: sys.path.insert(0, TOOLS)
import sebp
import hulls as H

# colours of the non-armour kinds (armour keeps the ColorMaskHSV the designer gave it)
KIND_RGB = {
    'thr': (255, 170, 60), 'gun': (40, 42, 48), 'turret': (74, 80, 94), 'rail': (34, 36, 42), 'tank': (70, 130, 160), 'reactor': (90, 190, 110),
    'bat': (80, 150, 100), 'gyro': (170, 160, 70), 'ctrl': (120, 140, 220), 'cockpit': (120, 215, 245), 'cam': (30, 30, 36),
    'ant': (200, 200, 210), 'merge': (230, 150, 40), 'cradle': (240, 120, 30), 'door': (110, 118, 128), 'conn': (230, 200, 60), 'cargo': (130, 108, 76),
    'decoy': (200, 70, 60), 'sensor': (150, 100, 180), 'ai': (80, 170, 190), 'glass': (110, 205, 240), 'timer': (190, 120, 200),
    'warhead': (190, 50, 40), 'turret_part': (66, 72, 84), 'sys': (150, 150, 150), 'light': (255, 250, 200),
}

def _hsv_rgb(mask):
    h, s, v = mask
    s = max(0.0, min(1.0, s + 0.8)); v = max(0.0, min(1.0, v + 0.55))
    import colorsys
    r, g, b = colorsys.hsv_to_rgb(h % 1.0, s, v)
    return (int(r * 255), int(g * 255), int(b * 255))

def _dot(a, b): return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]
def _cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def _norm(a):
    n = math.sqrt(_dot(a, a)) or 1.0
    return (a[0]/n, a[1]/n, a[2]/n)
def _sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def _add(a, b): return (a[0]+b[0], a[1]+b[1], a[2]+b[2])
def _mul(a, s): return (a[0]*s, a[1]*s, a[2]*s)

_CUBE_FACES = [((1, 0, 0), ((1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1))),
               ((-1, 0, 0), ((-1, -1, 1), (-1, 1, 1), (-1, 1, -1), (-1, -1, -1))),
               ((0, 1, 0), ((-1, 1, -1), (-1, 1, 1), (1, 1, 1), (1, 1, -1))),
               ((0, -1, 0), ((-1, -1, 1), (-1, -1, -1), (1, -1, -1), (1, -1, 1))),
               ((0, 0, 1), ((-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1))),
               ((0, 0, -1), ((1, -1, -1), (-1, -1, -1), (-1, 1, -1), (1, 1, -1)))]

def _clip_poly(poly, n, d):
    """keep n.p <= d (Sutherland-Hodgman); returns (polygon, points on the plane)"""
    out, on = [], []
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        da, db = _dot(n, a) - d, _dot(n, b) - d
        if da <= 1e-9: out.append(a)
        if (da < -1e-9 and db > 1e-9) or (da > 1e-9 and db < -1e-9):
            t = da / (da - db)
            p = _add(a, _mul(_sub(b, a), t)); out.append(p); on.append(p)
        if abs(da) <= 1e-9: on.append(a)
    return out, on

def clipped_cube(planes, h=0.5):
    """polygons [(normal, [vertices])] of the unit cube (half size h, centred 0) cut by the half spaces n.p <= d"""
    faces = [(n, [(v[0]*h, v[1]*h, v[2]*h) for v in vs]) for n, vs in _CUBE_FACES]
    caps = []
    for (pn, pd) in planes:
        nf, cap_pts = [], []
        for n, vs in faces:
            cv, on = _clip_poly(vs, pn, pd)
            if len(cv) >= 3: nf.append((n, cv))
            cap_pts += on
        faces = nf
        # unique cap points
        uniq = []
        for p in cap_pts:
            if not any(abs(p[0]-q[0]) + abs(p[1]-q[1]) + abs(p[2]-q[2]) < 1e-7 for q in uniq): uniq.append(p)
        if len(uniq) >= 3:
            c = (sum(p[0] for p in uniq)/len(uniq), sum(p[1] for p in uniq)/len(uniq), sum(p[2] for p in uniq)/len(uniq))
            nn = _norm(pn)
            ref = _norm(_sub(uniq[0], c))
            tang = _cross(nn, ref)
            uniq.sort(key=lambda p: math.atan2(_dot(_sub(p, c), tang), _dot(_sub(p, c), ref)))
            faces.append((nn, uniq))
    return faces

_V = {'Right': (1, 0, 0), 'Left': (-1, 0, 0), 'Up': (0, 1, 0), 'Down': (0, -1, 0), 'Backward': (0, 0, 1), 'Forward': (0, 0, -1)}

def _planes_for(bp, b):
    """cutting half spaces of a hull block from its open (zero-mount) faces; [] for a plain cube"""
    sub = b['sub']
    if 'Slope2' in sub: k = 'slope2base' if sub.endswith('Base') else 'slope2tip'
    elif sub.endswith('Slope') or sub.endswith('Window1x1Slope'): k = 'slope'
    elif sub.endswith('ArmorCorner') and 'Inv' not in sub: k = 'corner'
    else: return []
    if k in ('slope2base', 'slope2tip'):
        a = _V[b['up']]; r = _neg(_V[b['fwd']])
        return [((a[0]+0.5*r[0], a[1]+0.5*r[1], a[2]+0.5*r[2]), 0.25 if k == 'slope2base' else -0.25)]
    try:
        area = H.orient_areas(sub, bp.grid)[(b['fwd'], b['up'])]
    except Exception:
        return []
    op = [_V[d] for d in _V if area.get(_V[d], 0) <= 0.01]
    if k == 'slope' and len(op) == 2:
        return [((op[0][0]+op[1][0], op[0][1]+op[1][1], op[0][2]+op[1][2]), 0.0)]
    if k == 'corner' and len(op) == 3:
        return [((op[0][0]+op[1][0]+op[2][0], op[0][1]+op[1][1]+op[2][1], op[0][2]+op[1][2]+op[2][2]), -0.5)]
    return []

def _neg(v): return (-v[0], -v[1], -v[2])

SKIP_SUB = ('ArmorPanel', 'ArmorPanelLight', 'ArmorPanelHeavy')

def _turret_items(items, g, b, world, cen, hw, rgb):
    """a turret as base slab + head + barrel (the block's up axis is the stand axis, Forward the barrel)"""
    u = world.rot.apply(_V[b['up']]); f = world.rot.apply(_V[b['fwd']])
    ua = [k for k in range(3) if abs(u[k]) > 0.5][0]; fa = [k for k in range(3) if abs(f[k]) > 0.5][0]
    su, sf = (1 if u[ua] > 0 else -1), (1 if f[fa] > 0 else -1)
    H_ = 2 * hw[ua]
    def put(c, h, col):
        items.append((tuple(c), tuple(h), col, None, 'turret_part', None))
    bottom = cen[ua] - su * hw[ua]
    c = list(cen); h = list(hw)
    h1, h2 = 0.30 * H_, 0.46 * H_
    c[ua] = bottom + su * h1 / 2; h[ua] = h1 / 2
    put(c, h, tuple(int(v * 0.85) for v in rgb))
    c2 = list(cen); h2h = [x * 0.66 for x in hw]
    c2[ua] = bottom + su * (h1 + h2 / 2); h2h[ua] = h2 / 2
    put(c2, h2h, tuple(min(255, int(v * 1.25)) for v in rgb))
    n_b = 1
    c3 = list(c2); h3 = [x * 0.12 for x in hw]
    L = hw[fa] * 1.05
    c3[fa] = c2[fa] + sf * (h2h[fa] * 0.4 + L / 2); h3[fa] = L / 2
    c3[ua] = c2[ua] + su * h2 * 0.1
    put(c3, h3, tuple(int(v * 0.7) for v in rgb))

def collect(bp):
    """[(centre metres, half size (m) per axis, rgb, polygons-or-None, kind)]  for the blueprint and every sub-grid, world frame"""
    items = []
    for g, world in bp.grids():
        cs = 2.5 if g.grid == 'Large' else 0.5
        for b in g.blocks:
            kind = b['kind']
            if any(b['sub'].endswith(s) for s in SKIP_SUB): continue
            cells = g.cells(b)
            if b['xtype'].endswith('_CubeBlock'):
                if kind in ('glass',) or 'Window' in b['sub']: rgb = KIND_RGB['glass']
                else: rgb = _hsv_rgb(b['color'])
                planes = _planes_for(g, b)
                c = cells[0]
                cen = world.apply((c[0]*cs, c[1]*cs, c[2]*cs))
                poly = clipped_cube(planes, 0.5) if planes else None
                items.append((cen, (cs/2,)*3, rgb, (poly, world.rot, cs), kind, None))
            else:
                lo = [min(c[i] for c in cells) for i in range(3)]
                hi = [max(c[i] for c in cells) for i in range(3)]
                ctr = [(lo[i] + hi[i]) / 2.0 for i in range(3)]
                half = [(hi[i] - lo[i] + 1) / 2.0 * cs * 0.96 for i in range(3)]
                cen = world.apply((ctr[0]*cs, ctr[1]*cs, ctr[2]*cs))
                # half sizes in world axes (axis-aligned poses only)
                hw = [abs(world.rot.c[0][i])*half[0] + abs(world.rot.c[1][i])*half[1] + abs(world.rot.c[2][i])*half[2] for i in range(3)]
                rgb = KIND_RGB.get(kind) or _hsv_rgb(b['color'])
                glow = None
                if kind == 'turret':
                    _turret_items(items, g, b, world, cen, hw, rgb)
                    continue
                if kind == 'thr':
                    rgb = (58, 62, 72)
                    glow = world.rot.apply(_V[b['fwd']])
                items.append((cen, tuple(hw), rgb, None, kind, glow))
    return items

def _basis(az, el):
    a, e = math.radians(az), math.radians(el)
    cam = (math.sin(a)*math.cos(e), math.sin(e), -math.cos(a)*math.cos(e))     # direction object -> camera
    v = _neg(cam)
    up = (0, 1, 0) if abs(cam[1]) < 0.99 else (0, 0, -1 if cam[1] > 0 else 1)
    right = _norm(_cross(v, up))
    u = _cross(right, v)
    return cam, right, _norm(u)

def render_view(bp, path, az=35, el=28, scale=None, title=None, items=None, size=900, bg=(14, 17, 24), cut=None):
    from PIL import Image, ImageDraw
    items = items if items is not None else collect(bp)
    if cut is not None: items = [it for it in items if not cut(it[0])]
    cam, right, up = _basis(az, el)
    # extents
    xs, ys = [], []
    for cen, hw, rgb, poly, kind, glow in items:
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    p = (cen[0]+sx*hw[0], cen[1]+sy*hw[1], cen[2]+sz*hw[2])
                    xs.append(_dot(p, right)); ys.append(_dot(p, up))
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    if scale is None:
        scale = (size - 60) / max(x1 - x0, y1 - y0, 1e-6)
    W = int((x1 - x0) * scale) + 60
    Hh = int((y1 - y0) * scale) + 80
    im = Image.new('RGB', (W, Hh), bg)
    d = ImageDraw.Draw(im)
    light = _norm(_add(_add(_mul(cam, 0.55), _mul(up, 0.65)), _mul(right, -0.45)))
    def proj(p):
        return ((_dot(p, right) - x0) * scale + 30, (y1 - _dot(p, up)) * scale + 50)
    order = sorted(range(len(items)), key=lambda i: _dot(items[i][0], cam))
    for i in order:
        cen, hw, rgb, geo, kind, glow = items[i]
        if geo is not None and geo[0] is not None:
            poly, rot, cs = geo
            faces = []
            for n, vs in poly:
                wn = rot.apply(n)
                wv = [_add(cen, rot.apply(_mul(v, cs))) for v in vs]
                faces.append((wn, wv))
        else:
            faces = []
            for n, vs in _CUBE_FACES:
                faces.append((n, [(cen[0]+v[0]*hw[0], cen[1]+v[1]*hw[1], cen[2]+v[2]*hw[2]) for v in vs]))
        for n, vs in faces:
            if _dot(n, cam) <= 1e-6: continue
            sh = 0.42 + 0.58 * max(0.0, _dot(_norm(n), light))
            col = tuple(max(0, min(255, int(c * sh))) for c in rgb)
            edge = tuple(int(c * 0.55) for c in col)
            d.polygon([proj(v) for v in vs], fill=col, outline=edge)
        if glow is not None and _dot(glow, cam) > 1e-6:
            gn = glow
            ax = [k for k in range(3) if abs(gn[k]) > 0.5][0]
            o1, o2 = [k for k in range(3) if k != ax]
            c0 = [cen[k] + gn[k] * hw[k] * 1.01 for k in range(3)]
            quad = []
            for a1, a2 in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                p = list(c0); p[o1] += a1 * hw[o1] * 0.72; p[o2] += a2 * hw[o2] * 0.72
                quad.append(tuple(p))
            d.polygon([proj(v) for v in quad], fill=(255, 150, 40), outline=(255, 210, 120))
    if title: d.text((12, 8), title, fill=(235, 170, 60))
    d.text((12, Hh - 16), f'az {az} el {el}   {scale/ (1.0):.1f} px/m', fill=(110, 120, 135))
    im.save(path)
    return path

VIEWS = [('iso_fl', 38, 26), ('iso_fr', -38, 26), ('iso_rear', 150, 24), ('belly', 30, -38), ('top', 0, 90), ('side', 90, 0), ('front', 0, 0),
         ('section', 90, 14, lambda c: c[0] > 0.3), ('deck', 0, 90, lambda c: c[1] > 16.0), ('lane', 35, -50, None)]

def render_sheet(bp, outdir, stem, title=None, views=VIEWS, size=900):
    """writes <stem>_<view>.png for every view and <stem>_sheet.png (the first four views); returns the list of paths"""
    from PIL import Image
    os.makedirs(outdir, exist_ok=True)
    items = collect(bp)
    paths = []
    for v in views:
        name, az, el = v[:3]
        cut = v[3] if len(v) > 3 else None
        p = os.path.join(outdir, f'{stem}_{name}.png')
        render_view(bp, p, az, el, title=f'{title or bp.name}  {name}', items=items, size=size, cut=cut)
        paths.append(p)
    ims = [Image.open(p) for p in paths[:4]]
    w = max(i.size[0] for i in ims); h = max(i.size[1] for i in ims)
    sheet = Image.new('RGB', (w * 2, h * 2), (14, 17, 24))
    for k, i in enumerate(ims): sheet.paste(i, ((k % 2) * w, (k // 2) * h))
    sp = os.path.join(outdir, f'{stem}_sheet.png')
    sheet.save(sp)
    return paths + [sp]
