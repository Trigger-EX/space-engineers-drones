#!/usr/bin/env python3
"""Tube ram drones (docs/design/tube_drones.md section 5.4).   python3 -I tools/tests/test_tube.py"""
import os, sys, json, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import _common as C                                                  # noqa: E402
import tube                                                          # noqa: E402

SECTION = {"Tube 2x2": (2, 2), "Tube 2x4": (4, 2), "Tube 3x3": (3, 3), "Tube 4x4": (4, 4)}
CAP = {"Tube 2x2": 2, "Tube 2x4": 3, "Tube 3x3": 3, "Tube 4x4": 4}

class TubeTests(unittest.TestCase):
    def setUp(self):
        self.bps = {n: tube.MODELS[n](False) for n in SECTION}

    def test_validate_and_dlc(self):
        stats = C.stats_db()
        for n, bp in self.bps.items():
            bp.validate()
            self.assertEqual(bp.dlc_used(), [] if isinstance(bp.dlc_used(), list) else set(), n)
            for b in bp.blocks:
                self.assertFalse(stats.get(b['key'], {}).get('dlc'), f"{n}: {b['name']}")

    def test_names_and_pb(self):
        for n, bp in self.bps.items():
            names = [b['name'] for b in bp.blocks if b['name']]       # armour blocks are unnamed
            self.assertEqual(len(names), len(set(names)), n)
            self.assertTrue(all(x.startswith(n) for x in names), n)
            pbs = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_MyProgrammableBlock']
            self.assertEqual(len(pbs), 1, n)
            self.assertEqual(pbs[0]['name'], f'{n} PB')
            self.assertIn(f'Block Tag={n}', pbs[0].get('custom_data', ''))
            self.assertEqual(sum(b['xtype'] == 'MyObjectBuilder_MyProgrammableBlock' for b in bp.blocks), 1, n)

    def test_ai_blocks(self):
        for n, bp in self.bps.items():
            ai = {b['sub']: b for b in bp.blocks if b['kind'] == 'ai'}
            self.assertEqual(set(ai), {'SmallOffensiveCombat', 'SmallFlightMovement'}, n)
            for b in ai.values(): self.assertTrue(b['name'].startswith(n) and b['name'].endswith('[AI]'), b['name'])

    def test_envelope_nose_thrust_power(self):
        for n, bp in self.bps.items():
            W, H = SECTION[n]
            latch = [b for b in bp.blocks if b['kind'] == 'merge']
            self.assertEqual(len(latch), 1)
            thr = [b for b in bp.blocks if b['kind'] == 'thr']
            self.assertEqual(len(thr), W * H, n)
            for b in thr:
                self.assertEqual(b['sub'], 'SmallBlockSmallThrust'); self.assertEqual(b['fwd'], 'Backward')
            occ = bp.occupancy()
            for (x, y, z), b in occ.items():
                if y < 0: continue                      # the docking latch below the tail
                self.assertTrue(0 <= x < W and 0 <= y < H, f'{n}: {b["name"]} at {(x, y, z)} outside the section')
            for z in range(CAP[n]):
                for y in range(H):
                    for x in range(W):
                        b = occ[(x, y, z)]
                        self.assertTrue(b['sub'] == tube.HEAVY or b['kind'] == 'cam', f'{n}: nose cell {(x, y, z)} is {b["name"]}')
            self.assertTrue(any(b['kind'] == 'cam' and b['fwd'] == 'Forward' for b in bp.blocks), n)
            acc, m = C.twr(bp)
            self.assertGreaterEqual(acc['Forward'], 15, n)
            for d in ('Backward', 'Up', 'Down', 'Left', 'Right'): self.assertEqual(acc[d], 0, n)
            st = C.stats_db()
            need = sum(st[b['key']]['stats']['max_power_mw'] for b in thr)
            bats = sum(st[b['key']]['stats']['max_output_mw'] for b in bp.blocks if b['kind'] == 'bat')
            self.assertGreaterEqual(bats, 1.05 * need, n)

    def test_docking_and_script_budget(self):
        for n, bp in self.bps.items():
            self.assertEqual(C.check_docking(bp, [b for b in bp.blocks if b['kind'] == 'merge'][0]), 'Down', n)
        self.assertLessEqual(len(C.script_text('TubeRamAI.cs')), 80000)

if __name__ == '__main__':
    unittest.main()
