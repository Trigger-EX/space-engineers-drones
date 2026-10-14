"""Sprue Test (option-A printer test rig, tools/designs/sprue_test.py): the geometry and config the in-game test depends on."""
import os, sys, re, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import designs
import sprue_test as S
import reach as R
from minify_cs import minify

class SprueTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bp = S.sprue_test()
        cls.ng = S.MODELS[S.NAME_NG](False)
        cls.cl = S.MODELS[S.NAME_CL](False)
        cls.xml = cls.bp.build_xml()

    def test_registered_as_test_kind(self):
        mods, _ = designs.load(('sprue_test',))
        kinds = {m: k for k, _, m, _ in designs.iter_models(mods)}
        self.assertEqual(kinds, {S.NAME: 'test', S.NAME_NG: 'test', S.NAME_CL: 'test'})
        self.assertEqual(set(S.NO_LITE), {S.NAME, S.NAME_NG, S.NAME_CL})

    def test_verify_passes_for_both(self):
        self.assertTrue(S.verify(self.bp)); self.assertTrue(S.verify(self.ng)); self.assertTrue(S.verify(self.cl))

    def test_neck_ends_outside_every_grinder_sphere(self):
        _, cen = S.grinder_centres(self.bp)
        for end in (S.STUB, S.HUB):
            self.assertGreater(min(R.cell_dist(p, end) for _, p in cen), 2.6 + 1.0)       # >= 1 cell of margin beyond r 1.3 m

    def test_tools_and_grinder_written_off_and_dynamic(self):
        for name in ('Sprue Grinder', 'Sprue Welder 1', 'Sprue Welder 2', 'Sprue Welder 3'):
            m = re.search(r'<CustomName>%s</CustomName>.*?<Enabled>(\w+)</Enabled>' % name, self.xml, re.S)
            self.assertEqual(m.group(1), 'false', name)
        self.assertNotIn('<IsStatic>true', self.xml)
        self.assertEqual(self.bp.dlc_used(), [])

    def test_grinder_shape_is_flagged_as_guess_and_absent_in_fallback(self):
        conf = self.bp.shape_summary()['ShipGrinder/SmallShipGrinder'][1]
        self.assertTrue(conf.startswith('guess'))
        self.assertNotIn('ShipGrinder/SmallShipGrinder', self.ng.shape_summary())
        # SmallShipWelder reports 'seen-in-Starburst' but is the LARGE welder's sample (pasted in v2.0 only): secondary risk, not a guess
        self.assertEqual([k for k, (n, c) in self.bp.shape_summary().items() if not c.startswith('seen')], ['ShipGrinder/SmallShipGrinder'])

    def test_projection_holds_payload_with_scripts_and_battery_value(self):
        pg = self.xml[self.xml.index('<ProjectedGrids>'):self.xml.index('</ProjectedGrids>')]
        self.assertEqual(pg.count('<MyObjectBuilder_CubeBlock'), len(self.bp.payload.blocks))
        self.assertIn('Role=payload', pg)
        self.assertIn('<CurrentStoredPower>0.04</CurrentStoredPower>', pg)
        self.assertEqual(self.bp.projection_offset, (0, 1, 2))
        self.assertIn('<KeepProjection>true</KeepProjection>', self.xml)

    def test_both_pbs_carry_the_current_probe_script(self):
        from xml.sax.saxutils import escape
        with open(os.path.join(TOOLS, '..', 'scripts', 'SprueProbe.cs'), encoding='utf-8') as fh: txt = escape(minify(fh.read()))
        self.assertEqual(self.xml.count(txt), 2)

    def test_payload_config_matches_script_fallback_and_printer_keys(self):
        with open(os.path.join(TOOLS, '..', 'scripts', 'SprueProbe.cs'), encoding='utf-8') as fh: src = fh.read()
        pdef = re.search(r'const string PDEF = "([^"]*)";', src).group(1).replace('|', '\n') + '\n'
        self.assertEqual(S.payload_head(), pdef)                      # a payload PB whose Custom Data is lost runs on the same config
        self.assertIn('Battery=Sprue Payload Battery', S.payload_head())
        self.assertIn('Clear Metres=6', self.xml)
        self.assertIn('Blueprint Owner=%d' % S.sebp.DEFAULT_OWNER, self.xml)

    def test_close_layout_points_the_tip_at_the_neck_and_reaches_it(self):
        gcells, cen = S.grinder_centres(self.cl)
        self.assertEqual(self.cl.layout, 'close')
        xs = sorted({c[0] for c in gcells}); self.assertEqual((xs[0], xs[-1]), (-7, -2))        # long axis x, tip face one block short of the neck (x = 0)
        for h, p in cen[:2]:                                                                      # tip and tip-1, r 1.3 m: N2..N6 are cut, N1 / N7 never
            cut = {c for c in S.NECK if R.cell_dist(p, c) <= R.radius_cells(h) and R.radius_cells(h) > 2.5}
            if R.radius_cells(h) > 2.5: self.assertEqual(sorted(c[2] for c in cut), [-2, -1, 0, 1, 2])
        self.assertIn('Grinder Forward=Right', self.cl.build_xml())
        self.assertIn('Grinder Forward=Up', self.xml)

    def test_close_layout_only_welders_1_and_3_may_be_touched(self):
        _, cen = S.grinder_centres(self.cl)
        rmax = max(R.radius_cells(h) for h, _ in cen)
        touched = {b['name'] for b in self.cl.blocks if b['sub'] != 'SmallShipGrinder' and any(min(R.cell_dist(p, c) for _, p in cen) <= rmax for c in self.cl.cells(b))}
        self.assertEqual(touched, {'Sprue Welder 1', 'Sprue Welder 3'})
        _, cen0 = S.grinder_centres(self.bp)
        self.assertFalse([b for b in self.bp.blocks if b['sub'] != 'SmallShipGrinder' and any(min(R.cell_dist(p, c) for _, p in cen0) <= rmax for c in self.bp.cells(b))])

    def test_hand_cut_n1_would_leave_an_orphan(self):              # why the owner grinds N2, not N1
        old = S.HAND_CUT
        try:
            S.HAND_CUT = 0
            with self.assertRaises(AssertionError) as cm: S.verify(self.bp)
            self.assertIn('orphan', str(cm.exception))
        finally:
            S.HAND_CUT = old

    def test_grinder_guess_is_process_global_once_registered(self):  # documents the order dependence for future grinder models
        S.register_shapes()
        self.assertTrue(S.sebp.HAND['ShipGrinder']['conf'].startswith('guess'))

if __name__ == '__main__':
    unittest.main()
