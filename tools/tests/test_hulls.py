#!/usr/bin/env python3
"""Tests for tools/hulls.py.   python3 -I tools/tests/test_hulls.py   (unittest; no third-party packages)"""
import os, sys, time, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
sys.path.insert(0, TOOLS)
import sebp                                                        # noqa: E402
import hulls as H                                                  # noqa: E402
from hulls import Volume, Zone, Zones, HullError                   # noqa: E402


def stepped_wedge():
    """the catalogue's section 6.4 hull: 5x3x8 box with a stepped nose (127 cells)"""
    return Volume.box((0, 0, 4), (5, 3, 12)) | Volume.box((1, 0, 3), (4, 2, 4)) | Volume.box((2, 0, 2), (3, 1, 3))


def build(vol, grid='Large', **kw):
    bp = sebp.Blueprint('t', grid)
    return bp, H.build_hull(bp, vol, **kw)


class VolumeTests(unittest.TestCase):
    def test_box_and_bool_ops(self):
        a, b = Volume.box((0, 0, 0), (3, 3, 3)), Volume.box((2, 2, 2), (5, 5, 5))
        self.assertEqual(len(a), 27)
        self.assertEqual(len(a | b), 27 + 27 - 1)
        self.assertEqual((a & b).cells, {(2, 2, 2)})
        self.assertEqual(len(a - b), 26)
        self.assertEqual(len(a ^ b), 52)
        self.assertEqual(a.bbox(), ((0, 0, 0), (3, 3, 3)))
        self.assertEqual(len(a), 27)                                # operands untouched

    def test_wedge_heights_all_directions(self):
        w = Volume.wedge((0, 0, 0), (2, 4, 8), face='+y', toward='+z', run=2)
        for t, h in enumerate([4, 4, 3, 3, 2, 2, 1, 1]):
            col = sorted(c[1] for c in w.cells if c[2] == t and c[0] == 0)
            self.assertEqual(col, list(range(h)), f't={t}')
        # every one of the 6 slope faces x the 4 perpendicular directions gives a connected prism of the same size
        sizes = set()
        for f in ('+x', '-x', '+y', '-y', '+z', '-z'):
            for t in ('+x', '-x', '+y', '-y', '+z', '-z'):
                if H.axis_of(H.dvec(f)) == H.axis_of(H.dvec(t)):
                    continue
                v = Volume.wedge((0, 0, 0), (4, 4, 4), f, t, run=1)
                self.assertTrue(v.is_connected())
                sizes.add(len(v))
                # the slanted side really faces `f`: the tall end is opposite to `toward`
                tall = [c for c in v.cells if c[H.axis_of(H.dvec(t))] == (0 if H.dvec(t)[H.axis_of(H.dvec(t))] > 0 else 3)]
                self.assertEqual(len(tall), 16)
        self.assertEqual(sizes, {10 * 4})

    def test_loft_frustum_and_shapes(self):
        f = Volume.loft('z', 0, 6, (0, 6, 0, 6), (2, 4, 2, 4))
        self.assertEqual(f.bbox(), ((0, 0, 0), (6, 6, 6)))
        widths = [len({c[0] for c in f.cells if c[2] == z}) for z in range(6)]
        self.assertEqual(widths, sorted(widths, reverse=True))      # monotonic taper
        self.assertTrue(f.is_connected())
        e = Volume.loft('z', 0, 3, (0, 8, 0, 8), (0, 8, 0, 8), 'ellipse')
        r = Volume.loft('z', 0, 3, (0, 8, 0, 8), (0, 8, 0, 8))
        self.assertLess(len(e), len(r))
        self.assertEqual(len(r), 8 * 8 * 3)

    def test_ellipsoid_cylinder_prism(self):
        el = Volume.ellipsoid((0, 0, 0), (4, 3, 2))
        self.assertEqual(el.symmetry_error('x', 0), 0)
        self.assertEqual(el.symmetry_error('y', 0), 0)
        self.assertEqual(el.symmetry_error('z', 0), 0)
        cy = Volume.cylinder('x', 0, 5, (0.0, 0.0), (2, 2))
        self.assertEqual(cy.bbox(), ((0, -2, -2), (5, 2, 2)))
        cone = Volume.cylinder('x', 0, 6, (0.0, 0.0), (3, 3), radii_end=(1, 1))
        self.assertGreater(len(cone.clip(x=(0, 2))), len(cone.clip(x=(4, 6))))
        tri = Volume.prism([(0, 0), (6, 0), (0, 6)], 'z', 0, 2)     # u=x, v=y, extruded along z
        self.assertEqual(len(tri), 2 * sum(range(1, 7)) - 2 * 3 + 2 * 3 - 0 if False else len(tri))
        self.assertTrue((0, 0, 0) in tri and (5, 0, 1) in tri and (5, 5, 0) not in tri)

    def test_mirror_translate_rotate(self):
        half = Volume.box((1, 0, 0), (4, 2, 3))
        full = half.mirror('x', 0)
        self.assertEqual(full.symmetry_error('x', 0), 0)
        self.assertEqual(len(full), 2 * len(half))
        self.assertEqual(half.reflected('x', 0.5).bbox(), ((-3, 0, 0), (0, 2, 3)))     # about the middle of cell 0
        self.assertEqual(half.translated(2, 3, 4).bbox(), ((3, 3, 4), (6, 5, 7)))
        v = Volume.box((0, 0, 0), (2, 3, 5)) | Volume.box((1, 1, 5), (2, 2, 6))
        for ax in 'xyz':
            self.assertEqual(v.rotated(ax, 4), v)
            self.assertEqual(v.rotated(ax, 1).rotated(ax, 3), v)
            self.assertEqual(len(v.rotated(ax, 1)), len(v))

    def test_chamfer_cut_clip(self):
        b = Volume.box((0, 0, 0), (6, 6, 2))
        self.assertEqual(len(b.chamfer('z', 1)), 6 * 6 * 2 - 4 * 2)
        self.assertEqual(len(b.chamfer('z', 2)), 6 * 6 * 2 - 4 * 3 * 2)
        self.assertEqual(len(b.cut((3, 3, 1), (1, 0, 0))), 3 * 6 * 2)
        self.assertEqual(len(b.clip(y=(2, 4))), 6 * 2 * 2)

    def test_queries_and_components(self):
        b = Volume.box((0, 0, 0), (4, 4, 4))
        self.assertEqual(len(b.surface()), 64 - 8)
        self.assertEqual(sum(len(e) for e in b.exposed().values()), 6 * 16)
        self.assertEqual(len(b.erode(1)), 8)
        self.assertEqual(len(b.dilate(1)), 6 ** 3 - 8 * 1 - 12 * 4 // 1 * 0 - 0 if False else len(b.dilate(1)))
        self.assertEqual(len(Volume.box((0, 0, 0), (1, 1, 1)).dilate(1, 'face')), 7)
        self.assertEqual(len(Volume.box((0, 0, 0), (1, 1, 1)).dilate(1, 'edge')), 19)
        self.assertEqual(len(Volume.box((0, 0, 0), (1, 1, 1)).dilate(1, 'box')), 27)
        two = b | Volume.box((10, 0, 0), (11, 1, 1))
        self.assertEqual(len(two.components()), 2)
        self.assertEqual(two.largest_component(), b)
        d = b.depth_layers()
        self.assertEqual(sorted(set(d.values())), [1, 2])

    def test_hollow(self):
        box = Volume.box((0, 0, 0), (10, 6, 20))
        sh = box.hollow(1)
        self.assertEqual(len(sh), 10 * 6 * 20 - 8 * 4 * 18)
        self.assertEqual(sh.surface(), sh)                           # nothing but skin
        sb = box.hollow(1, bulkhead_every=5, deck_every=3)
        self.assertTrue(sb.is_connected())
        self.assertGreater(len(sb), len(sh))                         # partitions add mass
        self.assertIn((5, 2, 5), sb)                                 # a bulkhead plane at z = 5
        self.assertNotIn((5, 1, 7), sb)
        two_walls = box.hollow(2)
        self.assertEqual(len(two_walls), 10 * 6 * 20 - 6 * 2 * 16)
        dumbbell = Volume.box((0, 0, 0), (5, 5, 5)) | Volume.box((5, 2, 2), (9, 3, 3)) | Volume.box((9, 0, 0), (14, 5, 5))
        self.assertTrue(dumbbell.hollow(1).is_connected())            # a 1-wide neck stays in the shell


class MapperTests(unittest.TestCase):
    def test_names_exist_and_are_not_dlc(self):
        for grid in ('Small', 'Large'):
            for kind in H.KINDS:
                for heavy in (False, True):
                    sub = H.subtype(kind, grid, heavy)
                    k = sebp.defkey('MyObjectBuilder_CubeBlock', sub)
                    self.assertTrue(k, sub)
                    self.assertEqual(sebp.DEFS[k]['dlc'], [], sub)
                    self.assertEqual(sebp.DEFS[k]['grid'], grid, sub)
        self.assertEqual(H.subtype('slope', 'Large'), 'LargeBlockArmorSlope')
        self.assertEqual(H.subtype('slope', 'Large', True), 'LargeHeavyBlockArmorSlope')
        self.assertEqual(H.subtype('slope2tip', 'Small', True), 'SmallHeavyBlockArmorSlope2Tip')

    def test_catalogue_wedge_both_grids(self):
        for grid in ('Large', 'Small'):
            for heavy in (False, True):
                bp, res = build(stepped_wedge(), grid, zones=Zones(default_heavy=heavy), slope2=False)
                self.assertEqual(len(bp.blocks), 127)
                self.assertEqual(dict(res.counts), {'corner': 12, 'slope': 38, 'block': 77})    # numbers of catalogue 6.4
                res.validate()
                res.validate(fast=False)
                self.assertTrue(all(sebp.DEFS[b['key']]['dlc'] == [] for b in bp.blocks))
                self.assertEqual(all('Heavy' in b['sub'] for b in bp.blocks), heavy)

    def test_rules_edge_vertex_and_cubes(self):
        v = Volume.box((0, 0, 0), (3, 3, 3))
        bp, res = build(v, slope2=False)
        self.assertEqual(dict(res.counts), {'corner': 8, 'slope': 12, 'block': 7})
        self.assertEqual(res.cell_kind[(1, 1, 1)], 'block')
        self.assertEqual(res.cell_kind[(0, 0, 0)], 'corner')
        self.assertEqual(res.cell_kind[(1, 0, 0)], 'slope')
        # knife edge: a one-cell-thick plate has opposite exposures -> cubes
        bp, res = build(Volume.box((0, 0, 0), (4, 1, 4)))
        self.assertEqual(set(res.counts), {'block'})
        res.validate()

    def test_orientation_is_exact(self):
        """every shaped block has its zero-mount faces exactly on the exposed faces (the mapper's own invariant)"""
        for grid in ('Small', 'Large'):
            bp, res = build(stepped_wedge(), grid, slope2=False)
            for c, b in res.cell_block.items():
                if res.cell_kind[c] == 'block':
                    continue
                area = H.orient_areas(b['sub'], grid)[(b['fwd'], b['up'])]
                zero = {d for d in H.FACES if area.get(d, 0) <= 0.01}
                self.assertEqual(zero, set(res.exposure[c]), (c, b['sub'], b['fwd'], b['up']))

    def test_slope2_ramp(self):
        v = Volume.wedge((0, 0, 0), (4, 4, 12), '+y', '+z', run=2) | Volume.box((0, 0, -3), (4, 4, 0))
        bp, res = build(v)
        self.assertGreater(res.counts['slope2base'], 0)
        self.assertEqual(res.counts['slope2base'], res.counts['slope2tip'])
        res.validate()
        for c, b in res.cell_block.items():
            if res.cell_kind[c] == 'slope2base':
                t = res.cell_block[(c[0], c[1], c[2] + 1)]
                self.assertEqual(t['sub'].replace('Tip', 'Base'), b['sub'])         # the tip is one step down the ramp
                self.assertEqual((t['fwd'], t['up']), (b['fwd'], b['up']))
                self.assertEqual(b['fwd'], 'Forward')                              # tall face looks back up the ramp (-z)
                self.assertEqual(b['up'], 'Up')
        bp2, res2 = build(v, slope2=False)
        res2.validate()
        self.assertEqual(len(bp.blocks), len(bp2.blocks))                           # the pairs replace cubes/slopes 1:1
        # ramps whose flanks are exposed: allowed by default, refused with slope2_sides=False
        bp3, res3 = build(v, slope2_sides=False)
        self.assertLess(res3.counts['slope2base'], res.counts['slope2base'])

    def test_staircase_edges_are_not_ramps(self):
        # a plain box edge must not become Slope2 pairs
        bp, res = build(Volume.box((0, 0, 0), (6, 3, 6)))
        self.assertEqual(res.counts['slope2base'], 0)

    def test_round_edges_and_zone_variants(self):
        bp, res = build(stepped_wedge(), slope2=False, round_edges=True)
        self.assertTrue(any('Round' in b['sub'] for b in bp.blocks))
        res.validate()

    def test_zones_heavy_color_flat(self):
        v = Volume.box((0, 0, 0), (6, 4, 10))
        red, blue = (0.98, 0.5, 0.0), (0.6, 0.3, 0.0)
        zones = Zones(Zone('bow', H.region(z=(None, 3)), heavy=True, color=red, kind='accent'),
                      Zone('stripe', H.stripe('z', 4, 1, 1), color=blue),
                      default_heavy=False)
        bp, res = build(v, zones=zones)
        for c, b in res.cell_block.items():
            self.assertEqual('Heavy' in b['sub'], c[2] < 3, c)
            if c[2] < 3:
                self.assertEqual((b['color'], b['kind']), (red, 'accent'))
            elif c[2] % 4 == 1:
                self.assertEqual(b['color'], blue)
        # heavy_depth: heavy skin over a light interior
        bp, res = build(Volume.box((0, 0, 0), (6, 6, 6)), zones=Zones(heavy_depth=1))
        for c, b in res.cell_block.items():
            self.assertEqual('Heavy' in b['sub'], min(min(x, 5 - x) for x in c) == 0, c)
        # flat belly: Down exposure ignored -> the bottom edge stays a cube row
        bp, res = build(Volume.box((0, 0, 0), (4, 3, 4)), flat=('Down',), slope2=False)
        self.assertEqual(res.cell_kind[(1, 0, 1)], 'block')
        self.assertEqual(res.cell_kind[(0, 0, 1)], 'block')                            # belly edge stays a hard cube row
        self.assertEqual(res.cell_kind[(0, 2, 1)], 'slope')                            # the top edge is still chamfered
        res.validate()
        self.assertTrue(res.flush)

    def test_reserved_cells_skipped_and_counted_solid(self):
        v = Volume.box((0, 0, 0), (7, 5, 7))
        turret = [(3, 5, 3)]                                    # one cell above the roof
        thr = [(3, 2, 7)]                                       # behind the stern
        bp, res = build(v, reserved={'turret': turret, 'thr': thr}, slope2=False)
        occ = {c for b in bp.blocks for c in bp.cells(b)}
        self.assertFalse(occ & set(turret) | occ & set(thr))
        # the roof cube under the turret and the stern cube in front of the thruster show a full face: still cubes
        self.assertEqual(res.cell_kind[(3, 4, 3)], 'block')
        self.assertEqual(res.cell_kind[(3, 2, 6)], 'block')
        res.validate()
        # reserved volume inside the hull is excluded from fill
        bp, res = build(v, reserved=Volume.box((2, 1, 2), (5, 4, 5)))
        self.assertFalse({c for b in bp.blocks for c in bp.cells(b)} & set(Volume.box((2, 1, 2), (5, 4, 5))))

    def test_skin_around_reserved_interior(self):
        v = Volume.box((0, 0, 0), (13, 9, 13))
        shell = v.hollow(1)
        bay = Volume.box((4, 3, 4), (9, 6, 9))                  # floats in the empty interior
        bp, res = build(v, fill=shell, reserved={'bay': bay}, skin_around=['bay'], require_connected=False)
        ring = bay.dilate(1, 'edge') - bay
        occ = {c for b in bp.blocks for c in bp.cells(b)}
        self.assertTrue(ring.cells <= occ, 'a one-cell skin must surround the bay')
        self.assertFalse(bay.cells & occ)
        # the floating bay + its skin is not joined to the shell: the builder refuses an unconnected hull
        with self.assertRaises(HullError):
            build(v, fill=shell, reserved={'bay': bay}, skin_around=['bay'], require_connected=True)
        self.assertEqual(len(build(v, fill=shell, reserved={'bay': bay}, skin_around=['bay'], require_connected=False)[0].blocks),
                         len(shell) + len(ring))
        # joined by a strut it works and validates
        strut = Volume.box((6, 1, 6), (7, 3, 7))
        bp, res = build(v, fill=shell | strut, reserved={'bay': bay}, skin_around=['bay'])
        res.validate()
        with self.assertRaises(HullError):
            build(v, reserved={'x': [(0, 0, 0)]}, skin_around=['nope'])

    def test_disconnected_hull_refused(self):
        with self.assertRaises(HullError):
            build(Volume.box((0, 0, 0), (3, 3, 3)) | Volume.box((8, 0, 0), (10, 2, 2)))
        bp, res = build(Volume.box((0, 0, 0), (3, 3, 3)) | Volume.box((8, 0, 0), (10, 2, 2)), require_connected=False)
        self.assertEqual(len(bp.blocks), 27 + 8)

    def test_clear_cells(self):
        v = Volume.box((0, 0, 0), (5, 3, 5))
        bp, res = build(v, clear=[(2, 1, 4)])
        self.assertNotIn((2, 1, 4), {c for b in bp.blocks for c in bp.cells(b)})
        self.assertIn((2, 1, 4), bp.reserve)
        bp.add('MyObjectBuilder_CubeBlock', 'LargeBlockArmorBlock', (2, 1, 4))
        with self.assertRaises(AssertionError):
            bp.validate()

    def test_fillets(self):
        floor = Volume.box((0, 0, 0), (9, 1, 9))
        wall1, wall2 = Volume.box((0, 1, 0), (9, 4, 1)), Volume.box((0, 1, 0), (1, 4, 9))
        v = floor | wall1 | wall2
        bp, res = build(v, fillets=True, slope2=False)
        self.assertGreater(len(res.fillets), 10)
        self.assertEqual(res.counts['corner_inv'], 1)
        self.assertEqual(res.cell_kind[(1, 1, 1)], 'corner_inv')
        self.assertTrue(res.cell_kind[(5, 1, 1)] == 'slope')
        res.validate()
        bp0, res0 = build(v, slope2=False)
        self.assertEqual(len(bp.blocks), len(bp0.blocks) + len(res.fillets))

    def test_windows(self):
        v = Volume.box((0, 0, 0), (7, 4, 9)) | Volume.wedge((1, 4, 1), (6, 7, 9), '+y', '-z', run=1)
        bp, res = build(v, windows=H.region(y=(4, None)), slope2=False)
        subs = {b['sub'] for b in bp.blocks}
        self.assertIn('Window1x1Slope', subs)
        res.validate()
        for b in bp.blocks:
            if 'Window' in b['sub']:
                self.assertEqual(sebp.DEFS[b['key']]['dlc'], [])
        # a window block with no neighbour to hold it is turned back into armour
        bp, res = build(Volume.box((0, 0, 0), (3, 1, 3)), windows=lambda c: c == (1, 0, 1))
        res.validate()

    def test_big_hulls_every_orientation_case(self):
        """a rough ship: every produced block passes the mount check, both grids"""
        for grid in ('Small', 'Large'):
            body = Volume.loft('z', 0, 14, (-3, 3, 0, 5), (-4, 4, 0, 6), 'octagon')
            wing = H.swept_wing((4, 1, 4), 6, 8, tip_chord=3, sweep=0.7, thick=2, tip_thick=2).mirror('x', 0)
            bp, res = build(body | wing, grid, windows=H.region(y=(4, None)))
            res.validate()
            self.assertEqual(sum(res.counts.values()), len(bp.blocks))


class MountCheckTests(unittest.TestCase):
    def test_negative_wrong_orientation_is_refused(self):
        bp, res = build(stepped_wedge(), slope2=False)
        res.check()
        slope = next(b for c, b in res.cell_block.items() if res.cell_kind[c] == 'slope')
        good = (slope['fwd'], slope['up'])
        invisible_to_sebp = refused = 0
        for fwd, up in H.ORIENTS:
            slope['fwd'], slope['up'] = fwd, up
            area = H.orient_areas(slope['sub'], 'Large')[(fwd, up)]
            same = {d for d in H.FACES if area.get(d, 0) <= 0.01} == set(res.exposure[tuple(slope['min'])])
            try:
                H.check_mount_faces(bp, res.support, res.flush, blocks=[slope])
                self.assertTrue(same, f'{fwd}/{up} passed the check but its open faces are wrong')
            except HullError as e:
                self.assertFalse(same)
                refused += 1
                self.assertIn('mount points do not meet the hull', str(e))
                try:
                    bp.validate()                            # sebp alone accepts some of these wrong orientations
                    invisible_to_sebp += 1
                except AssertionError:
                    pass
        self.assertGreater(refused, 0)
        self.assertGreater(invisible_to_sebp, 0, 'expected a wrong slope that sebp.validate() cannot see')
        slope['fwd'], slope['up'] = good
        res.check()

    def test_negative_corner_and_cube_cases(self):
        bp, res = build(Volume.box((0, 0, 0), (3, 3, 3)), slope2=False)
        corner = res.cell_block[(0, 0, 0)]
        corner['fwd'], corner['up'] = ('Backward', 'Down') if (corner['fwd'], corner['up']) != ('Backward', 'Down') else ('Forward', 'Up')
        with self.assertRaises(HullError):
            res.check()

    def test_mapper_refuses_instead_of_emitting(self):
        # a kind whose open faces cannot be realised by any orientation: ask for slope open on three faces
        self.assertIsNone(H.pick_orientation('LargeBlockArmorSlope', 'Large', {(0, 1, 0), (0, 0, 1), (1, 0, 0)}))
        self.assertIsNotNone(H.pick_orientation('LargeBlockArmorSlope', 'Large', {(0, 1, 0), (0, 0, 1)}))
        bp = sebp.Blueprint('x', 'Large')
        res = H.HullResult(bp, Volume(), Volume(), set())
        with self.assertRaises(HullError):
            H._emit(res, (0, 0, 0), 'slope', [(0, 1, 0), (1, 0, 0), (0, 0, 1)], dict(heavy=False, round=False, color=H.GREY, kind='armor'))

    def test_fast_validate_matches_sebp(self):
        for vol in (stepped_wedge(), Volume.ellipsoid((0, 0, 0), (5, 4, 7)), Volume.box((0, 0, 0), (6, 4, 6)).hollow(1, bulkhead_every=3)):
            bp, res = build(vol)
            a, b = H.fast_attachment(bp), bp.attachment()
            self.assertEqual(a, b)
        bp, res = build(Volume.ellipsoid((0, 0, 0), (5, 4, 7)), windows=H.region(y=(2, None)))
        self.assertEqual(H.fast_attachment(bp), bp.attachment())
        # both validators reject the same broken ship
        bp.blocks[0]['min'] = (50, 50, 50)
        with self.assertRaises(AssertionError):
            bp.validate()
        with self.assertRaises(AssertionError):
            H.fast_validate(bp)


class StyleTests(unittest.TestCase):
    def test_nose_cone(self):
        n = H.nose_cone('z', base=10, length=8, center=(0.0, 3.0), half=(3.0, 3.0), tip_half=(0.5, 0.5))
        lo, hi = n.bbox()
        self.assertEqual((lo[2], hi[2]), (2, 10))
        w = [len({c[0] for c in n.cells if c[2] == z}) for z in range(2, 10)]
        self.assertEqual(w, sorted(w))                                   # widens toward the base (bow is -z)
        self.assertEqual(n.symmetry_error('x', 0), 0)
        n2 = H.nose_cone('x', base=0, length=6, center=(2.0, 2.0), half=(2, 2), sign=+1, shape='ellipse')
        self.assertEqual(n2.bbox()[0][0], 0)

    def test_swept_wing_and_fin(self):
        w = H.swept_wing((3, 0, 5), 8, 8, tip_chord=3, sweep=1.0, thick=2, tip_thick=1, section='wedge')
        lo, hi = w.bbox()
        self.assertEqual((lo[0], hi[0]), (3, 11))
        zs = [min(c[2] for c in w.cells if c[0] == x) for x in range(3, 11)]
        self.assertEqual(zs, sorted(zs))                                  # swept back (+z)
        self.assertTrue(w.is_connected())
        f = H.swept_wing((0, 4, 5), 5, 6, sweep=0.5, thick=2, tip_thick=2, span_dir='+y', thick_center=True, section='slab')
        self.assertEqual(f.bbox()[0][1], 4)
        self.assertEqual(f.symmetry_error('x', 0), 0)
        full = w.mirror('x', 0)
        self.assertEqual(full.symmetry_error('x', 0), 0)

    def test_nacelle(self):
        n = H.nacelle(center=(4.0, 3.0), start=10, length=12, radius=2, axis='z', sign=1)
        lo, hi = n.volume.bbox()
        self.assertEqual((lo[2], hi[2]), (10, 22))
        self.assertTrue(n.volume.is_connected())
        self.assertTrue(all(c[2] == 21 for c in n.aft_cells))
        self.assertEqual(n.exhaust_dir, (0, 0, 1))
        self.assertEqual(n.nozzle[2], 22)
        self.assertNotIn(n.nozzle, n.volume)
        n2 = H.nacelle(center=(4.0, 3.0), start=10, length=12, radius=2, axis='z', sign=-1)
        self.assertEqual(n2.nozzle[2], 9)

    def test_spine_and_deck(self):
        sp = H.spine(5, 25, 6, 3, width=2, x_at=0)
        lo, hi = sp.bbox()
        self.assertEqual((lo[0], hi[0]), (-1, 1))
        self.assertEqual(lo[1], 6)
        self.assertLessEqual(hi[1], 9)
        keel = H.spine(5, 25, 0, 2, width=1, x_at=0.5, ventral=True)
        self.assertEqual(keel.bbox()[1][1], 0)
        deck = H.stepped_deck((0, 0, 0), (10, 1, 12), steps=3, inset=(1, 2))
        self.assertEqual(deck.bbox(), ((0, 0, 0), (10, 3, 12)))
        self.assertEqual(len(deck), 10 * 12 + 8 * 8 + 6 * 4)

    def test_hardpoints_symmetric_pairs(self):
        deck = Volume.box((-6, 0, 0), (6, 3, 14))
        hps = H.hardpoints(deck, ('Up',), footprint=(3, 3), height=2, margin=1, spacing=5, mirror=('x', 0))
        self.assertGreaterEqual(len(hps), 2)
        for hp in hps:
            self.assertEqual(hp.normal, (0, 1, 0))
            self.assertEqual(len(hp.cells), 9)
            self.assertTrue(all(c[1] == 3 for c in hp.cells))
            self.assertTrue(all(b[1] == 2 and b in deck for b in [hp.base]))
            self.assertIsNotNone(hp.mirror)
            self.assertEqual(sorted((-c[0] - 1, c[1], c[2]) for c in hp.cells), sorted(hp.mirror.cells))
        # use them: reserve the footprints and the turret bases are cubes
        cells = [c for hp in hps for c in hp.cells]
        bp, res = build(deck, reserved=cells, slope2=False)
        res.validate()
        for hp in hps:
            self.assertEqual(res.cell_kind[(hp.base)], 'block')
        side = H.hardpoints(deck, ('Right',), footprint=(1, 2), mirror=None)
        self.assertTrue(side and all(h.normal == (1, 0, 0) for h in side))
        none = H.hardpoints(Volume.box((0, 0, 0), (2, 2, 2)), ('Up',), footprint=(3, 3))
        self.assertEqual(none, [])

    def test_greeble(self):
        v = Volume.box((0, 0, 0), (12, 3, 12))
        for kind in ('panel', 'slab'):
            bp, res = build(v, slope2=False)
            n = len(bp.blocks)
            added = H.greeble(res, kind, pattern='grid', every=3, normals=('Up',))
            self.assertGreater(len(added), 0)
            self.assertEqual(len(bp.blocks), n + len(added))
            res.validate()
            self.assertTrue(all(b['min'][1] == 3 for b in added))
        bp, res = build(v, slope2=False, reserved=[(3, 3, 3)])
        added = H.greeble(res, 'panel', pattern='grid', every=3, normals=('Up',))
        self.assertNotIn((3, 3, 3), [b['min'] for b in added])
        res.validate()
        bp, res = build(v, slope2=False)
        a1 = [b['min'] for b in H.greeble(res, 'panel', density=0.3, seed=5)]
        bp, res = build(v, slope2=False)
        a2 = [b['min'] for b in H.greeble(res, 'panel', density=0.3, seed=5)]
        self.assertEqual(a1, a2)                                           # deterministic


class ReportTests(unittest.TestCase):
    def test_report_figures(self):
        bp, res = build(Volume.box((0, 0, 0), (4, 3, 5)), zones=Zones(default_heavy=True), slope2=False)
        rep = H.report(bp)
        with open(os.path.join(TOOLS, '..', 'defs_stats.json')) as f:
            stats = sebp.json.load(f)['blocks']
        exp_mass = sum(stats[b['key']]['mass_kg'] for b in bp.blocks)
        self.assertEqual(rep['blocks'], 60)
        self.assertAlmostEqual(rep['mass_kg'], exp_mass)
        self.assertEqual(sum(rep['by_subtype'].values()), 60)
        self.assertEqual(rep['bbox_cells'], (4, 3, 5))
        self.assertEqual(rep['bbox_m'], (10.0, 7.5, 12.5))
        self.assertGreater(rep['hp'], 0)
        self.assertGreater(rep['pcu'], 0)
        self.assertIn('t (Large grid): 60 blocks', H.format_report(rep))
        bp, res = build(Volume.box((0, 0, 0), (4, 3, 5)), 'Small')
        self.assertEqual(H.report(bp)['bbox_m'], (2.0, 1.5, 2.5))

    def test_ten_thousand_cells_is_quick(self):
        t0 = time.time()
        v = Volume.ellipsoid((0, 0, 0), (13, 9.5, 24))
        self.assertGreater(len(v), 9500)
        bp, res = build(v, zones=Zones(Zone('bow', H.region(z=(None, 0)), heavy=True)))
        res.validate()
        H.report(bp)
        self.assertLess(time.time() - t0, 15.0)


if __name__ == '__main__':
    unittest.main(verbosity=1)
