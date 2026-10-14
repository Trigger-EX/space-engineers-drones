"""Survival printers (owner, 2026-10-14): every bay welder is joined by small conveyor junctions to a [Stock] cargo container (printer_lib.route_conveyors):
the junction behind the centre of the welder's back face (its port), a junction chain to a junction touching a stock container, and a ConveyorLine for
each face-adjacent pair.  Runs on the Hive Mother (about 10 s)."""
import os, sys, unittest, collections
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import printers as P
import printer_lib as PL
from sebp import DIR

class Conveyors(unittest.TestCase):
    @classmethod
    def setUpClass(cls): cls.bp = P.MODELS['Hive Mother'](False)

    def test_every_welder_is_piped_to_the_stock(self):
        bp = self.bp; occ = bp.occupancy()
        conv = {c for c, b in occ.items() if b['kind'] == 'conv'}
        stock = {c for c, b in occ.items() if b['kind'] == 'cargo'}
        start = {c for c in conv if any(tuple(c[k] + v[k] for k in range(3)) in stock for v in DIR.values())}
        self.assertTrue(start)
        seen = set(start); q = collections.deque(start)
        while q:
            c = q.popleft()
            for v in DIR.values():
                n = tuple(c[k] + v[k] for k in range(3))
                if n in conv and n not in seen: seen.add(n); q.append(n)
        welders = [b for b in bp.blocks if b['kind'] == 'welder']
        self.assertGreater(len(welders), 0)
        for w in welders:
            d = DIR[w['fwd']]
            cs = bp.cells(w)
            back = min(cs, key=lambda c: sum(c[k] * d[k] for k in range(3)))           # a cell of the back layer
            layer = [c for c in cs if sum(c[k] * d[k] for k in range(3)) == sum(back[k] * d[k] for k in range(3))]
            centre = tuple(sorted(c[k] for c in layer)[len(layer) // 2] for k in range(3))
            pipe = tuple(centre[k] - d[k] for k in range(3))
            self.assertIn(pipe, seen, f'{w["name"]}: no conveyor junction at its port cell {pipe} joined to the stock')

    def test_conveyor_lines_written(self):
        self.assertGreater(len(self.bp.conveyor_pairs), 0)
        self.assertIn('<MyObjectBuilder_ConveyorLine>', self.bp.build_xml())

if __name__ == '__main__':
    unittest.main()
