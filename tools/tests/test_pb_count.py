#!/usr/bin/env python3
"""Extra Programmable Blocks are added where a ship needs one (docs/design/pb_headroom.md), not as a blanket three.   python3 -I tools/tests/test_pb_count.py

  * the drones that print from a Foundry/Shipyard/Pad carry their [Flight] + [Staff] + [Thrust] PBs in the model function itself (a printer projects the model
    function's grid, build_all only adds support PBs to the top-level blueprint): Wasp, Aegis, Mantis, Longbow S; the Peeper carries its [Thrust] PB;
  * Gnat carries `[Flight]` + `[Staff]` (docs/design/gnat_split.md), Shrike its `[Staff]` (docs/design/helm_split.md);
  * the cradle pod's PB count (warship pod subgrids) and the Midge (scriptless) are untouched."""
import os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import designs                                                       # noqa: E402
import spares                                                        # noqa: E402

_MODS = None
def mods():
    global _MODS
    if _MODS is None: _MODS = designs.load()[0]
    return _MODS

def build(mod, model, lite=False):
    return mods()[mod].MODELS[model](lite)

def names(bp):
    return [b['name'] for b in bp.blocks if b['xtype'] == spares.PB_X]

class PbCount(unittest.TestCase):
    def test_printed_drones_carry_their_support_pbs(self):
        for mod, model in (('wasp', 'Wasp'), ('extra', 'Aegis'), ('extra', 'Mantis'), ('core', 'Longbow S')):
            n = names(build(mod, model))
            for tag in ('[Flight]', '[Staff]', '[Thrust]'):
                self.assertEqual(sum(x.startswith(tag) for x in n), 1, f'{model}: {n}')
        n = names(build('peeper', 'Peeper'))
        self.assertEqual(sum(x.startswith('[Thrust]') for x in n), 1, n)

    def test_gnat_carries_flight_and_staff(self):      # (the blank PB reserved for it became [Staff] Gnat: docs/design/gnat_split.md)
        bp = build('core', 'Gnat', False)
        ps = [b for b in bp.blocks if b['xtype'] == spares.PB_X]
        self.assertEqual(sorted(b['name'] for b in ps), ['[Flight] Gnat PB', '[Staff] Gnat PB'])
        self.assertIn('LkInit(', [b for b in ps if b['name'].startswith('[Flight]')][0]['extra'])
        self.assertIn('GNAT STAFF', [b for b in ps if b['name'].startswith('[Staff]')][0]['extra'])
        bp.validate()

    def test_shrike_carries_the_staff_half_of_the_helm(self):      # (the blank PB reserved for it became [Staff] Shrike: docs/design/helm_split.md)
        bp = build('shrike', 'Shrike', False)
        st = [b for b in bp.blocks if b['xtype'] == spares.PB_X and b['name'].startswith('[Staff]')]
        self.assertEqual(len(st), 1, names(bp))
        self.assertTrue((st[0].get('extra') or '').strip(), 'the Staff carries StaffAI.cs')
        bp.validate()

    def test_untouched(self):
        import pods
        self.assertEqual(len(names(pods.cradle_pod(n_slots=4, lite=False))), 1)
        self.assertEqual(names(build('support', 'Midge')), [])
        self.assertEqual(len(names(build('ballista', 'Ballista', False))), 4)

if __name__ == '__main__':
    unittest.main(verbosity=2)
