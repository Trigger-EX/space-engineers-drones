#!/usr/bin/env python3
"""Gnat over two PBs (docs/design/gnat_split.md): GnatSwarmAI.cs is the Flight half, GnatStaffAI.cs the Staff half.   python3 -I tools/tests/test_gnat_split.py"""
import os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.dirname(TOOLS)
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import designs                                                       # noqa: E402
import _common as C                                                  # noqa: E402
from minify_cs import minify                                         # noqa: E402

def raw(n):
    with open(os.path.join(ROOT, 'scripts', n), encoding='utf-8') as f: return f.read()
def src(n):      # code only: the hook comments of the shared blocks name FleetLoad( / LaunchStep( too
    return re.sub(r'//[^\n]*', '', raw(n))

class GnatSplit(unittest.TestCase):
    def test_blueprint(self):
        bp = designs.load()[0]['core'].MODELS['Gnat'](False)
        bp.validate()
        ps = [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]
        self.assertEqual(sorted(b['name'] for b in ps), ['[Flight] Gnat PB', '[Staff] Gnat PB'])
        n = len(bp.blocks); C.add_support_pbs(bp)
        self.assertEqual(len(bp.blocks), n, 'no [Thrust] PB (point-and-burn), no second Staff')

    def test_outline_and_flush_latch(self):
        """owner rule: nothing may stand outside the 2 x 2 section (only the Left / Right side cameras); the merge block is set flush into the belly"""
        bp = designs.load()[0]['core'].MODELS['Gnat'](False)
        for c, b in bp.occupancy().items():
            if b['kind'] == 'cam' and b['fwd'] in ('Left', 'Right'): continue
            self.assertTrue(0 <= c[0] <= 1 and 0 <= c[1] <= 1, f'{b["name"]} at {c} is outside the 2 x 2 section')
        m = [b for b in bp.blocks if b['kind'] == 'merge']
        self.assertEqual(len(m), 1)
        self.assertTrue(m[0].get('flush'))
        self.assertEqual(C.check_docking(bp, m[0]), 'Down')

    def test_what_moved(self):
        f, s = src('GnatSwarmAI.cs'), src('GnatStaffAI.cs')
        for gone in ('FleetLoad(', 'FleetPump(', 'FleetTgtL', '_flChan', 'LaunchStep(', 'LaunchCmd(', '_lnS'): self.assertNotIn(gone, f, gone)
        self.assertIn('LkInit(', f); self.assertIn('LkSend();', f); self.assertIn('if (!LkBurn && !SvYield && !AiStep()) Control(dt);', f)
        for need in ('FleetPump(', 'LaunchStep(', 'FleetLoad('): self.assertIn(need, s, need)
        for gone in ('Follow', 'FoSat', 'Mode.FOLLOW'): self.assertNotIn(gone, s, gone)   # _mates and _sensors came back with the survivability STAFF SEARCH / HIVE MATES lite
        self.assertIn('enum Mode { IDLE, FORM, ATTACK, RTB }', s); self.assertIn('Math.Min(d.Item1 & 7, 3)', s)

    def test_link_tags_pair_up(self):
        f, s = src('GnatSwarmAI.cs'), src('GnatStaffAI.cs')
        for tag in ('.HMR', '.FOL', '.S2F', '.TGT'):      # Staff sends, Flight listens
            self.assertIn('_fk + "' + tag + '"', s, tag); self.assertIn('_lkK + "' + tag + '"', f, tag)
        for tag in ('.F2S', '.FLT', '.CMD'):                # Flight sends, Staff listens
            self.assertIn('_lkK + "' + tag + '"', f, tag); self.assertIn('_fk + "' + tag + '"', s, tag)

    def test_parse_mode_sentinel_does_not_set_satellite(self):
        f = src('GnatSwarmAI.cs')
        self.assertIn('if ((int)d == -1) return SatOn && !_ready && !_sentry ? Mode.FORM : (Mode)(-2); SatOn = true;', f)
        self.assertIn('case "ATTACK": case "HUNT":', f); self.assertIn('case "FORM": case "GUARD":', f)

    def test_staff_screen_keeps_the_lines_the_sim_reads(self):
        s = raw('GnatStaffAI.cs')
        self.assertIn('"Mode   : "', s); self.assertIn('"Target : "', s); self.assertIn('FLIGHT PB DOWN', s)

    def test_sizes(self):
        self.assertLess(len(minify(raw('GnatSwarmAI.cs'))), 80000)
        self.assertLess(len(minify(raw('GnatStaffAI.cs'))), 40000)

if __name__ == '__main__':
    unittest.main(verbosity=2)
