#!/usr/bin/env python3
"""Tests for tools/designs/corsair.py (corvette + Corsair Wing).   python3 -I tools/tests/test_corsair.py"""
import os, sys, unittest, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path:
        sys.path.insert(0, p)
import sebp                                                        # noqa: E402
import _common as C                                                # noqa: E402
import corsair as K                                                # noqa: E402

_CACHE = {}


def ship(lite, wing=False):
    k = (lite, wing)
    if k not in _CACHE:
        _CACHE[k] = (K.corsair_wing if wing else K.corsair)(lite)
    return _CACHE[k]


class Contract(unittest.TestCase):
    def test_contract(self):
        self.assertEqual(list(K.MODELS), ['Corsair', 'Corsair Wing'])
        self.assertEqual(set(K.DOCS), set(K.MODELS))
        self.assertEqual(K._script('NoSuchScript.cs'), '')
        self.assertIn('C1', K.DOCS['Corsair Wing'])
        self.assertIn('3 shells', K.DOCS['Corsair'])
        for lite, wing, n in ((False, False, 'Corsair'), (True, False, 'Corsair Lite'), (False, True, 'Corsair Wing'), (True, True, 'Corsair Wing Lite')):
            self.assertEqual(ship(lite, wing).name, n)

    def test_validate_and_reload(self):
        for lite in (False, True):
            for wing in (False, True):
                bp = ship(lite, wing)
                bp.validate()
                with tempfile.TemporaryDirectory() as d:
                    bp.write(d, bp.name)
                    grids = sebp.load_sbc(os.path.join(d, 'bp.sbc'), allow_dlc=False)
                    self.assertEqual(len(grids), (3 if wing else 1) + (0 if lite else 4))          # Full: a top and a head grid for each of the two rotor turrets
                    self.assertEqual(len(grids[0].blocks), len(bp.blocks))
                    for g in grids:
                        g.validate()


class Rules(unittest.TestCase):
    def test_no_dlc(self):
        for lite in (False, True):
            bp = ship(lite)
            self.assertEqual(bp.dlc_used(), [])
            for b in bp.blocks:
                self.assertEqual(sebp.DEFS[b['key']]['dlc'], [])
                self.assertNotIn('Prototech', b['sub'])

    def test_lite_only_seen(self):
        for k, (n, conf) in ship(True).shape_summary().items():
            self.assertTrue(conf.startswith('seen:'), f'{k}: {conf}')
        self.assertFalse([b for b in ship(True).blocks if b['kind'] in ('sensor', 'ai')])

    def test_names(self):
        bp = ship(False)
        pbs = [b for b in bp.blocks if b['sub'] == 'SmallProgrammableBlock']
        self.assertEqual(sorted(b['name'] for b in pbs), ['[Bay] Corsair', '[Helm] Corsair', '[Staff] Corsair', '[TU] Turret PB'])
        for b in bp.blocks:
            if b['xtype'].endswith('_CubeBlock') or b in pbs:
                continue
            self.assertTrue(b['name'].startswith('[SH] ') or b['name'].startswith('[TU] '), b['name'])      # `[TU]` = the rotor turrets (the Helm adopts only `[SH]` blocks)
        self.assertEqual(sorted(b['name'] for b in bp.blocks if b['kind'] == 'cradle'), ['[SH] [Bay 1] Merge Port', '[SH] [Bay 2] Merge Starboard'])

    def test_cradles_and_lane(self):
        self.assertTrue(K.check_cradles(ship(False)))

    def test_every_thruster_touches_a_tank(self):
        bp = ship(False)
        occ = {c: b for b in bp.blocks for c in bp.cells(b)}
        for t in [b for b in bp.blocks if b['kind'] == 'thr']:
            self.assertTrue(any(occ.get(tuple(c[i] + d[i] for i in range(3)), {}).get('kind') == 'tank'
                                for c in bp.cells(t) for d in sebp.DIR.values()), t['name'])

    def test_ammo_and_state(self):
        bp = ship(False)
        for b in bp.blocks:
            if b['kind'] == 'gun':
                self.assertTrue(bp.items_of(b), b['name'])
        self.assertTrue(all(b['state'].get('filled') == 'full' for b in bp.blocks if b['kind'] == 'tank'))

    def test_numbers(self):
        f = K.figures(ship(False))
        self.assertTrue(205e3 < f['mass_kg'] < 310e3)
        self.assertTrue(6.5 < f['acc']['Forward'] < 9.9 and 3.3 < f['acc']['Up'] < 4.9)


class Wing(unittest.TestCase):
    def test_docked_drones(self):
        self.assertTrue(K.check_wing(ship(False, True)))
        self.assertTrue(K.check_wing(ship(True, True)))


if __name__ == '__main__':
    unittest.main()
