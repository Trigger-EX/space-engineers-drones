"""printer_lib.check_lanes: every printer build fails when a bay's exit lane holds a printer block or another bay's drone (owner: launch paths
must never be blocked).  Uses a stand-in printer, so it runs without building a ship."""
import os, sys, unittest, types
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import printer_lib as PL

class FakeBP:
    def __init__(self, cells): self.name = 'Fake Printer'; self._occ = {c: {'name': f'Block {c}', 'sub': 'x'} for c in cells}
    def occupancy(self): return self._occ

def bay(n, T, exit=None):
    d = types.SimpleNamespace(D={(0, 0, 0), (0, 0, 1)}, c=(0, 1, 0), exit=exit)      # clamp +y: the drone leaves along -y
    return dict(n=n, T=T, design=d)

class Lanes(unittest.TestCase):
    def check(self, recs, cells):
        pr = types.SimpleNamespace(records=recs, bp=FakeBP(cells))
        return PL.check_lanes(pr)

    def test_clear_lane_passes(self):
        self.assertTrue(self.check([bay(1, (0, 0, 0))], {(0, 1, 0), (5, -3, 0)}))

    def test_printer_block_in_lane_fails(self):
        with self.assertRaises(AssertionError) as cm: self.check([bay(1, (0, 0, 0))], {(0, -PL.EXIT_LEN, 1)})
        self.assertIn('bay 1', str(cm.exception))

    def test_other_bays_drone_in_lane_fails(self):
        with self.assertRaises(AssertionError) as cm: self.check([bay(1, (0, 0, 0)), bay(2, (0, -6, 0))], set())
        self.assertIn("other bays' drones", str(cm.exception))

    def test_sideways_exit_is_the_lane_checked(self):
        recs = [bay(1, (0, 0, 0), exit=(1, 0, 0))]
        self.assertTrue(self.check(recs, {(0, -3, 0)}))                 # below the drone: not its lane any more
        with self.assertRaises(AssertionError): self.check(recs, {(4, 0, 0)})

if __name__ == '__main__':
    unittest.main()
