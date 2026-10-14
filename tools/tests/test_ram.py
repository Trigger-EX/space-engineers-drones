#!/usr/bin/env python3
"""Ram / Ram Tandem kinetic impact drones (docs/design/kinetic_ram.md section 3).   python3 -I tools/tests/test_ram.py"""
import os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import _common as C                                                  # noqa: E402
import ram                                                           # noqa: E402

class RamTests(unittest.TestCase):
    def setUp(self):
        self.bps = {n: ram.MODELS[n](False) for n in ram.MODELS}

    def test_validate_and_no_dlc(self):
        stats = C.stats_db()
        for n, bp in self.bps.items():
            bp.validate()
            for b in bp.blocks:
                self.assertFalse(stats.get(b['key'], {}).get('dlc'), f"{n}: {b['name']}")

    def test_prow_is_solid_heavy(self):
        for n, bp in self.bps.items():
            occ = bp.occupancy()
            for z in range(ram.PROW):
                for y in range(3):
                    for x in range(3):
                        self.assertEqual(occ[(x, y, z)]['sub'], ram.HEAVY, f'{n}: {(x, y, z)}')
            self.assertEqual(min(c[2] for c in occ), 0, n)           # nothing in front of the prow

    def test_shadow(self):
        """everything except the two camera stubs, their seats, the antenna and the merge stalk lies inside the prow's 3x3 section"""
        for n, bp in self.bps.items():
            for (x, y, z), b in bp.occupancy().items():
                if b['kind'] in ('cam', 'ant', 'merge'): continue
                if x == 1 and (y == 3 or y == -1): continue          # camera seats
                self.assertTrue(0 <= x <= 2 and 0 <= y <= 2, f'{n}: {b["name"]} at {(x, y, z)}')

    def test_mass_thrust_power(self):
        for n, bp in self.bps.items():
            acc, m = C.twr(bp)
            self.assertTrue(9000 <= m <= 9600, f'{n}: {m}')
            self.assertGreaterEqual(acc['Forward'], 15, n)
            for d in ('Backward', 'Up', 'Down', 'Left', 'Right'): self.assertEqual(acc[d], 0, n)
            st = C.stats_db()
            need = sum(st[b['key']]['stats']['max_power_mw'] for b in bp.blocks if b['kind'] == 'thr')
            have = sum(st[b['key']]['stats']['max_output_mw'] for b in bp.blocks if b['kind'] == 'bat')
            self.assertGreaterEqual(have, need, n)

    def test_names_pb_docking_warheads(self):
        for n, bp in self.bps.items():
            names = [b['name'] for b in bp.blocks if b['name']]
            self.assertEqual(len(names), len(set(names)), n)
            self.assertEqual(sum(b['xtype'] == 'MyObjectBuilder_MyProgrammableBlock' for b in bp.blocks), 1, n)
            self.assertEqual(C.check_docking(bp, [b for b in bp.blocks if b['kind'] == 'merge'][0]), 'Backward', n)
            self.assertEqual(sum(b['kind'] == 'warhead' for b in bp.blocks), 2 if n == 'Ram Tandem' else 0, n)
            self.assertLessEqual(len(C.script_text('StingerAI.cs')), 80000)

if __name__ == '__main__':
    unittest.main()
