#!/usr/bin/env python3
"""Basilisk over two PBs (docs/design/basilisk_split.md): BasiliskAI.cs is the Flight half, BasiliskStaffAI.cs the Staff half.   python3 -I tools/tests/test_basilisk_split.py"""
import os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.dirname(TOOLS)
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import designs                                                       # noqa: E402
import _common as C                                                  # noqa: E402

def src(n):      # code only: the hook comments of the shared blocks name FleetLoad( / FollowStep( too
    return re.sub(r'//[^\n]*', '', open(os.path.join(ROOT, 'scripts', n), encoding='utf-8').read())
def pbs(bp): return [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]

class BasiliskSplit(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mods = designs.load()[0]

    def test_models_carry_flight_and_staff(self):
        for k in ('Basilisk', 'Basilisk Seeker'):
            bp = self.mods['extra'].MODELS[k](False)
            bp.validate()
            ps = pbs(bp)
            self.assertEqual(sorted(b['name'].split(']')[0] + ']' for b in ps), ['[Flight]', '[Staff]', '[Thrust]'], [b['name'] for b in ps])
            fl = [b for b in ps if b['name'].startswith('[Flight]')][0]
            st = [b for b in ps if b['name'].startswith('[Staff]')][0]
            self.assertIn('LkInit(', fl['extra']); self.assertIn('BASILISK STAFF', st['extra'])
            self.assertEqual(tuple(st['min']), (-2, 0, 1)); self.assertEqual(bp.stats()['dims'], (17.5, 17.5, 20.0))
            n = [b['name'] for b in C.add_support_pbs(bp).blocks if b['xtype'].endswith('MyProgrammableBlock')]
            self.assertEqual(len(n), 3, n); self.assertTrue(any(x.startswith('[Thrust]') for x in n))

    def test_shrike_basilisk_test_is_unchanged(self):
        bp = self.mods['shrike_test'].MODELS['Shrike Basilisk Test'](False)
        n = [b['name'] for b in pbs(bp)]
        self.assertFalse(any(x.startswith('[Staff]') for x in n), n)
        self.assertTrue(any(x.startswith('[Helm]') for x in n))

    def test_what_moved(self):
        f, s = src('BasiliskAI.cs'), src('BasiliskStaffAI.cs')
        for gone in ('FleetLoad(', 'FollowStep(', 'HiveStatus(', 'MatesInLine('): self.assertNotIn(gone, f, gone)
        self.assertIn('LkInit(', f); self.assertIn('AimInfo()', re.search(r'void Display\(\)\s*\{.*?\n\}', f, re.S).group(0))
        self.assertRegex(f, r'int LkAmmo\(\) \{ return _rockets; \}'); self.assertIn('_gateWhy', re.search(r'string LkGate\(\)[^\n]*', f).group(0))
        for gone in ('LaunchStep(', '_lnS', 'LaunchCmd(', 'LaunchLoad('): self.assertNotIn(gone, s, gone)
        self.assertIn('double MaxSpeed = 60, SepRange = 160;', s); self.assertIn('Rockets', s)

    def test_sizes(self):
        from minify_cs import minify
        self.assertLess(len(minify(src('BasiliskAI.cs'))), 85000)
        self.assertLess(len(minify(src('BasiliskStaffAI.cs'))), 40000)

if __name__ == '__main__':
    unittest.main(verbosity=2)
