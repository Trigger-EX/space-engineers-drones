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
for _b in list(SECTION): SECTION[_b + " W"] = SECTION[_b]; CAP[_b + " W"] = CAP[_b]     # the warhead variants

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
            self.assertEqual([b['name'] for b in bp.blocks if b['kind'] == 'merge'], [], n)   # no latch: nothing may stand outside the section
            allthr = [b for b in bp.blocks if b['kind'] == 'thr']
            thr = [b for b in allthr if b['fwd'] == 'Backward']          # the main bank: one per section cell, exhaust aft
            self.assertEqual(len(thr), W * H, n)
            for b in allthr: self.assertEqual(b['sub'], 'SmallBlockSmallThrust')
            side = {d: [b for b in allthr if b['fwd'] == d] for d in ('Up', 'Down', 'Left', 'Right')}
            for d, l in side.items(): self.assertGreaterEqual(len(l), 1, f'{n}: no side thruster exhausting {d}')
            self.assertEqual(len(allthr), len(thr) + sum(len(l) for l in side.values()), f'{n}: a thruster that is neither main nor side (no reverse thrust)')
            occ = bp.occupancy()
            for (x, y, z), b in occ.items():
                self.assertTrue(0 <= x < W and 0 <= y < H, f'{n}: {b["name"]} at {(x, y, z)} outside the section')
            for z in range(CAP[n]):
                for y in range(H):
                    for x in range(W):
                        b = occ[(x, y, z)]
                        if n.endswith(' W') and z == 1: self.assertEqual(b['sub'], 'SmallWarhead', f'{n}: {(x, y, z)}'); continue
                        self.assertTrue(b['sub'] == tube.HEAVY or b['kind'] == 'cam', f'{n}: nose cell {(x, y, z)} is {b["name"]}')
            self.assertTrue(any(b['kind'] == 'cam' and b['fwd'] == 'Forward' for b in bp.blocks), n)
            acc, m = C.twr(bp)
            self.assertGreaterEqual(acc['Forward'], 13, n)            # the 2x2 is the slowest (13.8 m/s2 with its side thrusters and the extra batteries they need)
            self.assertEqual(acc['Backward'], 0, n)                        # no reverse thrust
            for d in ('Up', 'Down', 'Left', 'Right'): self.assertGreater(acc[d], 1.5, n)   # recessed side thrusters
            st = C.stats_db()
            need = sum(st[b['key']]['stats']['max_power_mw'] for b in thr)
            grp = sorted((sum(st[b['key']]['stats']['max_power_mw'] for b in l) for l in side.values()), reverse=True)
            need += grp[0] + grp[1]          # full forward burn while pushing sideways on both cross axes (never two opposite sides)
            bats = sum(st[b['key']]['stats']['max_output_mw'] for b in bp.blocks if b['kind'] == 'bat')
            self.assertGreaterEqual(bats, 1.05 * need, n)

    def test_warhead_variants(self):
        for n, bp in self.bps.items():
            heads = [b for b in bp.blocks if b['sub'] == 'SmallWarhead']
            pb = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_MyProgrammableBlock'][0]
            if not n.endswith(' W'):
                self.assertEqual(heads, [], n); self.assertNotIn('Max Rams=1', pb.get('custom_data', ''), n); continue
            W, H = SECTION[n]
            self.assertEqual(len(heads), W * H, n)                                   # one full layer...
            self.assertTrue(all(bp.cells(b)[0][2] == 1 for b in heads), n)       # ...right under the front heavy-armour layer
            self.assertIn('Max Rams=1', pb.get('custom_data', ''), n)                # one-shot
            base = self.bps[n[:-2]]
            self.assertEqual(len(bp.blocks), len(base.blocks), n)                     # same size, a layer swapped
            self.assertAlmostEqual(C.total_mass(bp), C.total_mass(base), delta=0.05 * C.total_mass(base))

    def test_script_budget(self):
        self.assertLessEqual(len(C.script_text('TubeRamAI.cs')), 80000)

class TubeLargeTests(unittest.TestCase):
    """Tube L 1x1 / 2x2 (+ W): large grid, heavy nose, side thrusters without reverse, heavy armour panels round the body."""
    NAMES = ("Tube L 1x1", "Tube L 2x2", "Tube L 1x1 W", "Tube L 2x2 W")

    def setUp(self):
        self.bps = {n: tube.MODELS[n](False) for n in self.NAMES}

    def test_build_validate_dlc(self):
        stats = C.stats_db()
        for n, bp in self.bps.items():
            bp.validate()
            self.assertEqual(bp.grid, 'Large', n)
            for b in bp.blocks: self.assertFalse(stats.get(b['key'], {}).get('dlc'), f"{n}: {b['name']}")
            pbs = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_MyProgrammableBlock']
            self.assertEqual(len(pbs), 1, n); self.assertEqual(pbs[0]['name'], f'{n} PB')
            cd = pbs[0].get('custom_data', '')
            self.assertIn(f'Block Tag={n}', cd); self.assertIn('Start Mode (WAIT/HUNT)=HUNT', cd)
            self.assertEqual(n.endswith(' W'), 'Max Rams=1' in cd, n)
            self.assertEqual('1x1' in n, 'Nose Camera Setback (m)=2.5' in cd, n)
            names = [b['name'] for b in bp.blocks if b['name']]
            self.assertEqual(len(names), len(set(names)), n)
            self.assertEqual({b['sub'] for b in bp.blocks if b['kind'] == 'ai'}, {'LargeOffensiveCombat', 'LargeFlightMovement'}, n)

    def test_thrust_power_panels(self):
        st = C.stats_db()
        for n, bp in self.bps.items():
            acc, m = C.twr(bp)
            self.assertGreaterEqual(acc['Forward'], 12, n)
            self.assertEqual(acc['Backward'], 0, n)                        # no reverse thrust
            for d in ('Up', 'Down', 'Left', 'Right'): self.assertGreater(acc[d], 2.5, n)
            thr = [b for b in bp.blocks if b['kind'] == 'thr']
            main = [b for b in thr if b['fwd'] == 'Backward']
            side = {d: [b for b in thr if b['fwd'] == d] for d in ('Up', 'Down', 'Left', 'Right')}
            need = sum(st[b['key']]['stats']['max_power_mw'] for b in main)
            grp = sorted((sum(st[b['key']]['stats']['max_power_mw'] for b in l) for l in side.values()), reverse=True)
            need += grp[0] + grp[1]
            bats = sum(st[b['key']]['stats']['max_output_mw'] for b in bp.blocks if b['kind'] == 'bat')
            self.assertGreaterEqual(bats, 1.05 * need, n)
            self.assertGreaterEqual(sum(b['sub'] == tube.LPANEL for b in bp.blocks), 40, n)      # armour panels round the body
            heads = [b for b in bp.blocks if b['sub'] == 'LargeWarhead']
            self.assertEqual(len(heads), (1 if '1x1' in n else 4) if n.endswith(' W') else 0, n)
            for b in heads: self.assertEqual(bp.cells(b)[0][2], 1, n)                          # under the front heavy layer

if __name__ == '__main__':
    unittest.main()
