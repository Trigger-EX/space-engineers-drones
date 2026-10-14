"""Outline rule (CLAUDE.md, owner): a printed drone that has no merge block gets one set INTO its hull (printer_lib.with_flush_latch), never a stalk:
every block of the latched drone stays inside the bounding box of the drone as designed."""
import os, sys, unittest, importlib
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import printers as P
import printer_lib as PL
import _common as C

def box(bp):
    cs = [c for b in bp.blocks for c in bp.cells(b)]
    return [(min(c[i] for c in cs), max(c[i] for c in cs)) for i in range(3)]

class FlushLatch(unittest.TestCase):
    def test_latched_drones_keep_their_outline(self):
        keys = [k for k, v in P.DRONES.items() if v[3]]
        self.assertIn('Tube 2x2', keys)
        for k in keys:
            mod, model, _, _ = P.DRONES[k]
            base = importlib.import_module(mod).MODELS[model](False)
            lat = PL.with_flush_latch(base)
            self.assertEqual(box(lat), box(base), k)
            m = [b for b in lat.blocks if b['kind'] == 'merge']
            self.assertEqual(len(m), 1, k); self.assertTrue(m[0].get('flush'), k)
            C.check_docking(lat, m[0])

    def test_calibrated_hypotheses_are_in_the_family(self):
        import reach as R
        self.assertTrue(set(PL.CALIB_HYPS) <= set(R.HYP_NAMES))

if __name__ == '__main__':
    unittest.main()
