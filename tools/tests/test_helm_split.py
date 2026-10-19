#!/usr/bin/env python3
"""Helm split (docs/design/helm_split.md): the fleet channel relay and the damage sensing moved from ShrikeAI.cs to StaffAI.cs.
python3 -I tools/tests/test_helm_split.py   (unittest, no third-party packages)

Checks that the moved code is gone from the Helm and present in the Staff, that both sides agree on every IGC suffix and tuple, that every ship that runs the Helm carries a
[Staff] PB with StaffAI.cs and the same Ship Tag (Ballista and Shrike with Mothership=false), and that the Helm stays under its size gate."""
import os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.dirname(TOOLS)
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path:
        sys.path.insert(0, p)
import _common as C                                                # noqa: E402
import ballista as B                                               # noqa: E402
import shrike as SH                                                # noqa: E402


def src(name):
    with open(os.path.join(ROOT, 'scripts', name), encoding='utf-8') as f:
        return f.read()


class Sources(unittest.TestCase):
    def test_the_helm_lost_the_moved_code(self):
        h = re.sub(r'//[^\n]*', '', src('ShrikeAI.cs'))      # (comments may still name them)
        for w in ('FleetLoad(', 'FleetPump(', 'RegisterFeeds', 'ScanSlims(', 'StepSlimScan', 'CheckDamage(', 'ListenTags'):
            self.assertNotIn(w, h, w)
        self.assertNotIn('>>> FLEET LINK', src('ShrikeAI.cs'))

    def test_the_staff_has_it(self):
        s = src('StaffAI.cs')
        for w in ('void SplitStart()', 'void SplitStep()', 'void ScanSlims()', 'void StepSlimScan()', 'void CheckDamage()', 'Mothership', '".DMG"', '".TGT"', '".FLT"', '(_fl & 256)'):
            self.assertIn(w, s, w)

    def test_both_sides_agree_on_suffixes_and_tuples(self):
        h, s = src('ShrikeAI.cs'), src('StaffAI.cs')
        for suffix in ('.TGT', '.DMG', '.FLT', '.S2H', '.H2S'):
            self.assertIn('"' + suffix + '"', h, 'Helm ' + suffix)
            self.assertIn('"' + suffix + '"', s, 'Staff ' + suffix)
        self.assertEqual(h.count('MyTuple<int, Vector3D, float>'), 2)      # .DMG: Helm side is() + cast
        self.assertIn('MyTuple<int, Vector3D, float>(_dmgHits, _dmgDir', s)
        tgt = 'MyTuple<long, Vector3D, Vector3D, double, long>'
        self.assertIn(tgt, h); self.assertIn(tgt, s)
        self.assertIn('| 256 |', h)                                         # H2S flags bit 8 = split Helm

    def test_helm_size_gate(self):
        n = len(C.minify(src('ShrikeAI.cs'))) if hasattr(C, 'minify') else 0
        lim = getattr(C, 'PB_BUDGET', 80000)      # flight_split.md 9.4: ShrikeAI <= 80,000 (target 76,500, test_flight_split.Sizes)
        if n:
            self.assertLessEqual(n, lim, 'ShrikeAI over the PB budget: move an off-hot-path module into StaffAI (flight_split.md section 7.1)')
        self.assertLessEqual(C.SCRIPT_LIMITS.get('ShrikeAI.cs', lim), lim, 'the ShrikeAI gate must not exceed the PB budget (CLAUDE.md: add a PB, do not raise the cap)')


class Designs(unittest.TestCase):
    def pbs(self, bp):
        return [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]

    def test_ballista_has_a_staff_with_mothership_off(self):
        bp = B.ballista(False)
        st = [b for b in self.pbs(bp) if b['name'].lower().startswith('[staff]')]
        self.assertEqual(len(st), 1)
        self.assertIn('<Program>', st[0]['extra']); self.assertIn('SplitStart', st[0]['extra'])
        cd = st[0]['custom_data']
        self.assertIn('Tag=BALLISTA', cd); self.assertIn('Mothership=false', cd); self.assertIn('Block Prefix=[SH]', cd)
        self.assertTrue(st[0]['name'].startswith('[Staff][SH]'))

    def test_shrike_has_a_staff_with_mothership_off(self):
        bp = SH.MODELS['Shrike'](False)
        st = [b for b in self.pbs(bp) if b['name'].lower().startswith('[staff]')]
        self.assertEqual(len(st), 1)
        self.assertIn('SplitStart', st[0]['extra'])
        self.assertIn('Tag=SH', st[0]['custom_data']); self.assertIn('Mothership=false', st[0]['custom_data'])

    def test_the_large_warships_keep_their_staff(self):
        for f in ('corsair.py', 'warden.py', 'leviathan.py'):
            with open(os.path.join(TOOLS, 'designs', f), encoding='utf-8') as fh:
                self.assertIn("StaffAI.cs", fh.read(), f)


if __name__ == '__main__':
    unittest.main()
