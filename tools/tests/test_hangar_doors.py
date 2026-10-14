"""Hangar doors (AirtightHangarDoor, 1 x 3 x 1 large cells): the panel's face is the block's Forward (owner's Tech Hunter sample: two doors closing a
forward-facing opening, Forward = Forward).  Owner report 2026-10-14: the Warden and Leviathan doors stood across their openings.  Every door must face
OUT of the opening (the cell in front of each door cell is empty) and every door of one hangar must share the orientation."""
import os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import designs
from sebp import DIR

def door_problems(bp):
    occ = bp.occupancy(); out = []; groups = {}
    for b in bp.blocks:
        if b['xtype'] != 'MyObjectBuilder_AirtightHangarDoor': continue
        f = DIR[b['fwd']]
        for c in bp.cells(b):
            n = tuple(c[i] + f[i] for i in range(3))
            if n in occ: out.append(f'{b["name"]}: cell {c} faces {b["fwd"]} into {occ[n]["name"] or occ[n]["sub"]}')
        key = (b['name'] or '').rsplit(' Door', 1)[0]
        groups.setdefault(key, set()).add((b['fwd'], b['up']))
    out += [f'{k}: mixed orientations {sorted(v)}' for k, v in groups.items() if len(v) > 1]
    return out

class HangarDoors(unittest.TestCase):
    def test_warden_and_leviathan_doors_face_out(self):
        mods, _ = designs.load(('warden', 'leviathan'))
        seen = 0
        for _, _, m, fn in designs.iter_models(mods):
            if m.endswith('Wing'): continue
            bp = fn(False); seen += 1
            self.assertTrue(any(b['xtype'] == 'MyObjectBuilder_AirtightHangarDoor' for b in bp.blocks), m)
            self.assertEqual(door_problems(bp), [], m)
        self.assertEqual(seen, 2)

if __name__ == '__main__':
    unittest.main()
