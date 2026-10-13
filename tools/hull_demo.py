#!/usr/bin/env python3
"""Demo for tools/hulls.py: a 14 m small-grid fighter hull and an 80 m large-grid frigate hull.

    python3 -I tools/hull_demo.py [--out DIR] [--bench N]

Builds both hulls (wedge nose, swept wings/fins, dorsal spine, engine nacelles, canopy windows, reserved system cells),
runs sebp validate() + the hulls mount-face check, prints the figure report, writes thumbnails with sebp's own thumbnail
code (four yaw views and a montage) and, with --bench, times a ~10,000-cell hull.  No real ship is defined here.
"""
import os, sys, time, argparse
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import sebp                                                        # noqa: E402
import hulls as H                                                  # noqa: E402
from hulls import Volume, Zone, Zones                              # noqa: E402

PAL = {'armor': (112, 120, 132), 'plate': (150, 156, 166), 'dark': (86, 92, 106), 'accent': (225, 130, 40), 'stripe': (214, 60, 52),
       'glass': (90, 190, 230), 'trim': (70, 76, 88), 'gun': (30, 32, 38), 'thr': (210, 150, 60), 'sys': (90, 150, 110), 'cockpit': (90, 190, 230)}

NAVY = H.hsv_mask(215, 0.25, 0.42)
STEEL = H.hsv_mask(210, 0.08, 0.62)
ACCENT = H.hsv_mask(28, 0.85, 0.95)
STRIPE = H.hsv_mask(2, 0.80, 0.80)
DARK = H.hsv_mask(220, 0.15, 0.22)


# ---------------------------------------------------------------------------------------------------------
# fighter: small grid, 0.5 m cells, 28 cells = 14 m long.   bow = -z, up = +y, plane of symmetry x = 0
# ---------------------------------------------------------------------------------------------------------
def fighter(name='Hull Fighter'):
    bp = sebp.Blueprint(name, 'Small', seed=7)
    cy = 3.0                                               # fuselage centre height (edge coords)
    # fuselage: a chisel nose that grows into the body, then a short taper to the engine bay
    nose = H.nose_cone('z', base=10, length=10, center=(0.0, cy), half=(3.0, 3.0), tip_half=(1.0, 0.8), tip_shift=(0, -0.8), shape='octagon')
    body = Volume.loft('z', 10, 21, (-3, 3, 0, 6), (-3, 3, 0, 6), 'rect').chamfer('z', 1)
    stern = Volume.loft('z', 21, 24, (-3, 3, 0, 6), (-2, 2, 1, 5), 'rect')
    hull = nose | body | stern
    canopy = Volume.wedge((-2, 6, 10), (2, 9, 19), 'Up', 'Forward', run=3)          # long 1-in-3 ramp: glass on its slope
    sp = H.spine(15, 25, 6, 4, width=2, x_at=0, profile='blade')                    # dorsal spine to the tail
    keel = H.spine(12, 24, 0, 2, width=2, x_at=0, profile='ramp', ventral=True)
    # engines on the wing roots, swept wings with flat tops (gun shelves), canards and canted tail fins
    nacs = [H.nacelle(center=(sx * 5.0, 2.5), start=14, length=11, radius=2.0, axis='z', sign=1, rear_taper=0.7, intake_taper=0.6)
            for sx in (-1, 1)]
    wing = H.swept_wing((3, 1, 12), 11, 11, tip_chord=4, sweep=0.8, thick=2, tip_thick=2, span_dir='+x', chord_dir='+z', section='slab')
    canard = H.swept_wing((3, 2, 8), 4, 5, tip_chord=2, sweep=0.5, thick=1, tip_thick=1, span_dir='+x', chord_dir='+z', section='slab')
    fin = H.swept_wing((5, 4, 17), 6, 7, tip_chord=3, sweep=0.8, thick=1, tip_thick=1, span_dir='+y', chord_dir='+z', section='slab')
    solid = hull | canopy | sp | keel | (wing | canard | fin).mirror('x', 0) | nacs[0].volume | nacs[1].volume
    solid = solid.largest_component()
    shell = solid.hollow(1, bulkhead_every=6, deck_every=3)
    # systems: cockpit (3x3x3 small block) in the canopy, thrusters at the nozzles, one gun pair on the wing tops
    cockpit_cells = Volume.box((-1, 6, 12), (2, 9, 15))
    guns = H.hardpoints(solid, ('Up',), footprint=(1, 4), height=1, margin=1, spacing=100, mirror=('x', 0),
                        where=lambda c: 6 <= abs(c[0] + 0.5) <= 9)
    reserved = {'cockpit': cockpit_cells, 'guns': [c for hp in guns for c in hp.column(1)]}
    thr_cells = []
    for n in nacs:
        c = n.nozzle
        thr_cells += [c, (c[0], c[1], c[2] + 1)]
    reserved['thrusters'] = thr_cells
    solid = solid | cockpit_cells
    wing_zone = lambda c: abs(c[0] + 0.5) >= 3.5
    zones = Zones(
        Zone('canopy', H.region(y=(6, None), z=(9, 20)), color=STEEL, kind='plate'),
        Zone('stripe', H.all_of(H.stripe('z', 8, 1, 3, lo=10), lambda c: abs(c[0] + 0.5) < 3.5), color=STRIPE, kind='stripe'),
        Zone('belly', H.region(y=(None, 2)), heavy=True, color=DARK, kind='dark'),
        Zone('bow', H.region(z=(None, 12)), heavy=True, color=ACCENT, kind='accent'),
        Zone('wing', wing_zone, color=DARK, kind='dark'),
        default_color=NAVY)
    res = H.build_hull(bp, solid, fill=shell, reserved=reserved, skin_around=['cockpit'], zones=zones,
                       windows=H.region(y=(7, None), z=(9, 15)))
    bp.add('MyObjectBuilder_Cockpit', 'SmallBlockCockpit', (-1, 6, 12), 'Forward', 'Up', 'Cockpit', kind='cockpit')
    for c in thr_cells[::2]:
        b = bp.add('MyObjectBuilder_Thrust', 'SmallBlockSmallThrust', c, 'Backward', 'Up', None, kind='thr')
        bp.keep_clear(b, 4, 'exhaust')
    for hp in guns:
        g = bp.add('MyObjectBuilder_SmallGatlingGun', '', hp.cells[0], 'Forward', 'Up', None, kind='gun')
        bp.keep_clear(g, 4, 'muzzle')
    H.greeble(res, 'panel', density=0.12, normals=('Up',), where=H.region(y=(None, 7), z=(10, 24)), seed=3, color=DARK)
    return bp, res


# ---------------------------------------------------------------------------------------------------------
# frigate: large grid, 2.5 m cells, 32 cells = 80 m long
# ---------------------------------------------------------------------------------------------------------
def frigate(name='Hull Frigate'):
    bp = sebp.Blueprint(name, 'Large', seed=11)
    cy = 3.0
    nose = H.nose_cone('z', base=9, length=9, center=(0.0, cy), half=(4.0, 3.0), tip_half=(0.5, 0.5), tip_shift=(0, -1.0), shape='octagon')
    body = Volume.loft('z', 9, 26, (-4, 4, 0, 6), (-4, 4, 0, 6), 'rect').chamfer('z', 1)
    stern = Volume.loft('z', 26, 29, (-4, 4, 0, 6), (-3, 3, 0, 5), 'rect')
    hull = nose | body | stern
    # armoured sponsons: flat roofs for the turrets, sloped fronts
    sponson = Volume.box((4, 1, 13), (8, 4, 25)) | Volume.wedge((4, 1, 9), (8, 4, 13), 'Up', 'Forward', run=1)
    tower = H.stepped_deck((-2, 6, 14), (2, 7, 24), steps=3, inset=(1, 1), rise=1)    # bridge: three decks
    spine = H.spine(20, 28, 6, 3, width=2, x_at=0, profile='blade')
    keel = H.spine(10, 27, 0, 1, width=2, x_at=0, profile='ramp', ventral=True)
    wing = H.swept_wing((4, 0, 19), 9, 9, tip_chord=3, sweep=1.0, thick=2, tip_thick=1, span_dir='+x', chord_dir='+z', section='wedge')
    fin = H.swept_wing((6, 3, 20), 6, 7, tip_chord=3, sweep=0.9, thick=1, tip_thick=1, span_dir='+y', chord_dir='+z', section='slab')
    solid = hull | tower | spine | keel | (sponson | wing | fin).mirror('x', 0)
    shell = solid.hollow(1, bulkhead_every=5, deck_every=3)
    turrets = H.hardpoints(solid, ('Up',), footprint=(3, 3), height=3, margin=0, spacing=7, mirror=('x', 0),
                           where=lambda c: 4 <= c[0] <= 5 or -6 <= c[0] <= -5)
    engines = [Volume.box((x, 1, 29), (x + 3, 4, 32)) for x in (-3, 0)]            # two 3x3x3 hydrogen thrusters aft
    reserved = {'hangar': Volume.box((-2, 1, 14), (2, 3, 22)), 'turrets': [c for hp in turrets for c in hp.column(3)],
                'engines': [c for e in engines for c in e]}
    zones = Zones(
        Zone('bridge', H.region(y=(6, None), z=(13, 25)), color=STEEL, kind='plate'),
        Zone('stripe', H.all_of(H.stripe('z', 8, 1, 3, lo=9), lambda c: abs(c[0] + 0.5) < 3.5), color=STRIPE, kind='stripe'),
        Zone('bow', H.region(z=(None, 12)), heavy=True, color=ACCENT, kind='accent'),
        Zone('belly', H.region(y=(None, 2)), heavy=True, color=DARK, kind='dark'),
        Zone('sponson', lambda c: abs(c[0] + 0.5) > 3.5 and c[1] < 5, heavy=True, color=STEEL, kind='plate'),
        default_color=NAVY)
    res = H.build_hull(bp, solid, fill=shell, reserved=reserved, skin_around=['hangar'], zones=zones,
                       windows=H.region(y=(7, None), z=(14, 24)))
    for hp in turrets:
        bp.add('MyObjectBuilder_LargeMissileTurret', 'LargeCalibreTurret', hp.cells[0], 'Forward', 'Up', None, kind='gun')
    for e in engines:
        lo, _ = e.bbox()
        b = bp.add('MyObjectBuilder_Thrust', 'LargeBlockLargeHydrogenThrust', lo, 'Backward', 'Up', None, kind='thr')
        bp.keep_clear(b, 3, 'exhaust')
    H.greeble(res, 'panel', density=0.15, normals=('Up',), where=H.region(y=(None, 7), z=(9, 28)), seed=5, color=DARK)
    return bp, res


# ---------------------------------------------------------------------------------------------------------
# rendering
# ---------------------------------------------------------------------------------------------------------
class _View:
    """re-oriented copy of a blueprint's cells so sebp.Blueprint.thumb can draw other viewpoints"""
    thumb = sebp.Blueprint.thumb

    def __init__(self, bp, yaw=0, pitch=0):
        self.blocks = []
        vols = [(b, bp.cells(b)) for b in bp.blocks]
        for b, cells in vols:
            self.blocks.append({'kind': b['kind'], '_c': [self._rot(c, yaw, pitch) for c in cells]})

    @staticmethod
    def _rot(c, yaw, pitch):
        x, y, z = c
        for _ in range(yaw % 4):
            x, z = z, -x - 1
        for _ in range(pitch % 4):
            y, z = z, -y - 1
        return (x, y, z)

    def cells(self, b):
        return b['_c']


def render_ortho(bp, outdir, stem, px=14):
    """top / side / front orthographic silhouettes (nearest cell per pixel block, shaded by depth) next to each other"""
    from PIL import Image, ImageDraw
    at = {}
    for b in bp.blocks:
        for c in bp.cells(b):
            at[c] = b['kind']
    xs, ys, zs = zip(*at)
    lo = (min(xs), min(ys), min(zs))
    hi = (max(xs) + 1, max(ys) + 1, max(zs) + 1)
    sz = tuple(h - l for l, h in zip(lo, hi))
    # (depth axis, depth sign toward the viewer, horizontal axis, vertical axis, flip horizontal, flip vertical)
    views = [('top', 1, +1, 0, 2, False, False), ('side', 0, +1, 2, 1, False, True), ('front', 2, -1, 0, 1, False, True)]
    tiles = []
    for name, da, ds, ha, va, fh, fv in views:
        W, Hh = sz[ha] * px, sz[va] * px
        im = Image.new('RGB', (W, Hh), (16, 19, 26))
        d = ImageDraw.Draw(im)
        best = {}
        for c, k in at.items():
            key = (c[ha], c[va])
            dep = c[da] * ds
            if key not in best or dep > best[key][0]:
                best[key] = (dep, k)
        for (u, v), (dep, k) in best.items():
            col = PAL.get(k, (120, 120, 120))
            f = 0.6 + 0.4 * (dep - ds * lo[da] * (1 if ds > 0 else 1) if False else 0)
            span = max(1, sz[da])
            rel = ((dep - (lo[da] if ds > 0 else -(hi[da] - 1))) / span)
            col = tuple(int(max(0, min(255, v_ * (0.55 + 0.45 * rel)))) for v_ in col)
            x0 = (u - lo[ha]) * px
            y0 = (v - lo[va]) * px
            if fh: x0 = W - x0 - px
            if fv: y0 = Hh - y0 - px
            d.rectangle([x0, y0, x0 + px - 1, y0 + px - 1], fill=col, outline=(20, 22, 28))
        tiles.append((name, im))
    Wt = sum(im.size[0] for _, im in tiles) + 20 * (len(tiles) + 1)
    Ht = max(im.size[1] for _, im in tiles) + 40
    sheet = Image.new('RGB', (Wt, Ht), (8, 10, 14))
    x = 20
    d = ImageDraw.Draw(sheet)
    for name, im in tiles:
        sheet.paste(im, (x, 30))
        d.text((x, 10), name, fill=(235, 170, 60))
        x += im.size[0] + 20
    out = os.path.join(outdir, f'{stem}_ortho.png')
    sheet.save(out)
    return out


def render(bp, outdir, stem, title):
    from PIL import Image
    os.makedirs(outdir, exist_ok=True)
    paths = []
    views = [(0, 0), (1, 0), (2, 0), (3, 0)]
    for i, (yaw, pitch) in enumerate(views):
        p = os.path.join(outdir, f'{stem}_{i}.png')
        _View(bp, yaw, pitch).thumb(p, title, f'view {i}', PAL)
        paths.append(p)
    ims = [Image.open(p) for p in paths]
    w, h = ims[0].size
    sheet = Image.new('RGB', (w * 2, h * 2))
    for i, im in enumerate(ims):
        sheet.paste(im, ((i % 2) * w, (i // 2) * h))
    out = os.path.join(outdir, f'{stem}.png')
    sheet.save(out)
    return out


def bench(n_target=10000):
    """timing of a ~n_target-cell solid hull through every stage"""
    t = time.time()
    k = (n_target * 1.0 / (4 / 3 * 3.14159265 * 0.55 * 0.4)) ** (1 / 3)             # ellipsoid radii (0.55k, 0.4k, k) ~ 85 % of the target
    rx, ry, rz = 0.55 * k, 0.4 * k, k
    vol = Volume.ellipsoid((0, 0, 0), (rx, ry, rz))
    vol = vol | H.nose_cone('z', base=-int(rz * 0.6), length=int(rz * 0.5), center=(0.0, 0.0), half=(rx * 0.6, ry * 0.6), shape='ellipse')
    vol = vol | H.spine(int(-rz * 0.3), int(rz * 0.8), int(ry), 4, width=3, x_at=0, profile='blade')
    t_vol = time.time() - t
    out = {}
    bp = sebp.Blueprint('Bench', 'Large', seed=1)
    t = time.time()
    res = H.build_hull(bp, vol, zones=Zones(Zone('bow', H.region(z=(None, 0)), heavy=True), default_color=NAVY))
    out['map'] = time.time() - t
    t = time.time(); res.check(); out['mount check'] = time.time() - t
    t = time.time(); H.fast_validate(bp); out['validate (fast)'] = time.time() - t
    t = time.time(); H.report(bp); out['report'] = time.time() - t
    t = time.time(); bp.build_xml(); out['xml'] = time.time() - t
    out['volume'] = t_vol
    return len(vol), len(bp.blocks), out


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default='hull_demo')
    ap.add_argument('--bench', type=int, default=0, help='also time an N-cell hull')
    ap.add_argument('--only', choices=('fighter', 'frigate'))
    a = ap.parse_args(argv)
    for name, fn in (('fighter', fighter), ('frigate', frigate)):
        if a.only and a.only != name:
            continue
        t = time.time()
        bp, res = fn()
        t_build = time.time() - t
        t = time.time()
        res.check()                                      # hulls: every block's mount faces meet the hull
        bp.validate()                                    # sebp: overlaps, attachment, one connected piece, DLC
        t_val = time.time() - t
        print(H.format_report(H.report(bp)))
        print(f'  hull kinds: {dict(res.counts)}   build {t_build:.2f} s, validate {t_val:.2f} s')
        print('  image:', render(bp, a.out, name, bp.name), render_ortho(bp, a.out, name))
    if a.bench:
        n, nb, t = bench(a.bench)
        print(f'bench: {n} cells -> {nb} blocks:', ', '.join(f'{k} {v:.2f}s' for k, v in t.items()))


if __name__ == '__main__':
    main()
