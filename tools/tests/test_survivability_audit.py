#!/usr/bin/env python3
"""tools/survivability_audit.py: kill sets, single points of failure, cover counts and clustering on a hand-made 5x3x3 small-grid block, plus the shipped drones run.
   python3 -I tools/tests/test_survivability_audit.py"""
import os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import sebp                                                          # noqa: E402
import survivability_audit as SA                                     # noqa: E402

X = 'MyObjectBuilder_'
def toy(two_gyros=False):
    """armour cube x 0..4, y 0..2, z 0..2: RC and flight PB in the middle row, one battery, thrusters on the +x end (exhaust Right, pushes Left)"""
    bp = sebp.Blueprint('Toy', 'Small')
    spec = {(2, 1, 1): (X + 'RemoteControl', 'SmallBlockRemoteControl', 'RC'), (1, 1, 1): (X + 'MyProgrammableBlock', 'SmallProgrammableBlock', '[Flight] PB'),
            (3, 1, 1): (X + 'Gyro', 'SmallBlockGyro', 'G1'), (0, 1, 1): (X + 'BatteryBlock', 'SmallBlockSmallBatteryBlock', 'Bat'),
            (4, 0, 0): (X + 'Thrust', 'SmallBlockSmallThrust', 'T1'), (4, 2, 2): (X + 'Thrust', 'SmallBlockSmallThrust', 'T2')}
    if two_gyros: spec[(3, 1, 2)] = (X + 'Gyro', 'SmallBlockGyro', 'G2')
    for x in range(5):
        for y in range(3):
            for z in range(3):
                if (x, y, z) in spec:
                    xt, sub, nm = spec[(x, y, z)]
                    b = bp.add(xt, sub, (x, y, z), 'Right' if xt.endswith('Thrust') else 'Forward', 'Up', nm, kind='ctrl')
                else: bp.armor(x, y, z)
    # (the thrusters are 2 cells long and stick out of the +x end; the audit reads cells, no validate() needed)
    return bp

class Survivability(unittest.TestCase):
    def test_kill_sets_and_spofs(self):
        a = SA.audit(toy())
        self.assertEqual(a['kills']['controller'], 1); self.assertEqual(a['kills']['gyro'], 1); self.assertEqual(a['kills']['power'], 1)
        self.assertEqual(a['kills']['thrust Left'], 2)
        self.assertEqual(a['min_kill'], 1)
        self.assertEqual({s[0] for s in a['spofs']}, {'controller', 'flight PB', 'gyro', 'power'})
        self.assertEqual({s[0] for s in a['unwaived']}, {'gyro', 'power'})       # controller and flight PB are in WAIVED_SPOF
        a2 = SA.audit(toy(two_gyros=True))
        self.assertEqual(a2['kills']['gyro'], 2)
        self.assertNotIn('gyro', {s[0] for s in a2['spofs']})

    def test_cover_counts_cells_between_block_and_outside(self):
        bp = toy(); occ = bp.occupancy(allow_overlap=True)
        cells = list(occ); box = [(min(c[i] for c in cells), max(c[i] for c in cells)) for i in range(3)]
        rc = [b for b in bp.blocks if b['name'] == 'RC'][0]          # (2,1,1): centre column of a 5 x 3 x 3 box
        cv = SA.cover(bp, occ, box, rc)
        self.assertEqual((cv['Right'], cv['Left']), (2, 2))          # x = 3, 4 and x = 1, 0
        self.assertEqual((cv['Up'], cv['Down'], cv['Forward'], cv['Backward']), (1, 1, 1, 1))
        bat = [b for b in bp.blocks if b['name'] == 'Bat'][0]        # (0,1,1): on the -x face, Left is open
        self.assertEqual(SA.cover(bp, occ, box, bat)['Left'], 0)

    def test_blast_cover(self):
        self.assertAlmostEqual(SA.blast_cover([(0, 0, 0), (2, 0, 0)], 2.5), 1.0)       # two points 2 m apart: a ball of radius 1 holds both
        self.assertGreater(SA.blast_cover([(0, 0, 0), (10, 0, 0)], 2.5), 2.5)

    def test_shipped_drones(self):
        import designs, _common as C
        mods, _ = designs.load(designs.DRONE_MODULES)
        got = {}
        for kind, mod, model, fn in SA.models(mods, ['Hornet', 'Gnat', 'Longbow S'], False):
            bp = SA.build(mods, kind, mod, model, fn); got[model] = SA.audit(bp)
        self.assertEqual(sorted(got), ['Gnat', 'Hornet', 'Longbow S'])
        for m, a in got.items():
            self.assertFalse(a['missing'], m)
            self.assertIn('flight PB', a['systems']); self.assertEqual(len([n for n in a['systems'] if n.startswith('thrust ') and n[7:] in C.AXIS_DIRS]) > 0, True)
            self.assertTrue(0 <= a['score'] <= 100)

if __name__ == '__main__':
    unittest.main()
