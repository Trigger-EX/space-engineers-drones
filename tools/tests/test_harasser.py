#!/usr/bin/env python3
"""Tests for the Ballista harasser defence: the Lookout module of SpotterAI.cs and the HARASSER LINK block of ShrikeAI.cs (docs/design/ballista_harasser_defence.md).
python3 -I tools/tests/test_harasser.py   (unittest; no third-party packages)

The two scripts only meet over IGC, so the tests pin the channel names, the tuple shapes and the link version, that the Spotter never writes a thruster / gyro / gun
(the Helm is the only writer), the PB name rules the link relies on, and the corner arithmetic of the plan (docs section 3.1)."""
import math, os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.dirname(TOOLS)
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path:
        sys.path.insert(0, p)


def src(name):
    with open(os.path.join(ROOT, 'scripts', name), encoding='utf-8') as f:
        return f.read()


HELM, SPOT = src('ShrikeAI.cs'), src('SpotterAI.cs')
H2A = 'MyTuple<int, Vector3D, double, double, long, Vector3D>'
HTK = 'MyTuple<long, Vector3D, Vector3D, float, float, int>'
A2H = 'MyTuple<Vector3D, Vector3D, int, float, long, long>'


class LinkTests(unittest.TestCase):
    def test_channels_and_tuples_agree(self):
        for tag in ('HDEF', '.H2A', '.HTK', '.A2H'):
            self.assertIn(tag, HELM, tag)
            self.assertIn(tag, SPOT, tag)
        for shape in (H2A, HTK, A2H):
            self.assertIn(shape, HELM, shape)
            self.assertIn(shape, SPOT, shape)
        # key = HDEF + the Helm PB's EntityId on both sides
        self.assertIn('"HDEF" + Me.EntityId', HELM)
        self.assertIn('"HDEF" + id + ".H2A"', SPOT)
        self.assertIn('"HDEF" + id + ".HTK"', SPOT)
        self.assertIn('"HDEF" + _lkHid + ".A2H"', SPOT)

    def test_link_version_is_checked_both_ways(self):
        self.assertRegex(HELM, r'\|\s*256\s*\|')                      # the Helm writes version 1 in bits 8-11
        self.assertIn('((d.Item1 >> 8) & 15) != 1', SPOT)             # the Spotter refuses another version
        self.assertIn('int fl = _lkFl | 256;', SPOT)                  # ... and answers with version 1

    def test_the_spotter_never_writes_the_ship(self):
        with open(os.path.join(TOOLS, 'lookout_block.cs'), encoding='utf-8') as f: lk = f.read()   # the shared LOOKOUT block (SpotterAI + StaffAI)
        for bad in ('ThrustOverride', 'GyroOverride', 'ApplyAction', 'DampenersOverride', '.Pitch =', '.Yaw =', '.Roll =', 'EnableRaycast', '.Enabled =', 'Shoot'):
            self.assertNotIn(bad, lk, 'one writer per override: only the Helm drives the ship')
        self.assertNotIn('TvAim(', SPOT)
        self.assertNotIn('LkInit(', SPOT)

    def test_pb_name_rules(self):
        self.assertIn('"[Helm]"', SPOT)                               # the Spotter finds the Helm by its [Helm] prefix
        self.assertIn('"[Spotter]"', HELM)                            # the Helm sends only when a [Spotter] PB is on the construct
        import ballista as B
        import _common as C
        bp = B.ballista(False, with_foundry=False)
        names = [b['name'] for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]
        self.assertEqual(sum(n.startswith('[Helm]') for n in names), 1)
        self.assertEqual(sum(n.startswith('[Spotter]') for n in names), 1)
        self.assertFalse([n for n in names if 'Hive Mother PB' in n])

    def test_hooks_are_in_the_helm(self):
        for hook in ('HdTick();', '+ HdScore(t)', 'HdHar(_tgt.Id)', '!HdJ()', 'want = HdWant(want, cap);', 'HdUp()', 'HdStatus()'):
            self.assertIn(hook, HELM, hook)
        self.assertLess(HELM.index('want = HdWant(want, cap);'), HELM.index('want = KzWant(want, me, cap);'), 'the bias goes in before the kamikaze back-away / break')
        self.assertNotIn('// >>> HARASSER LINK', SPOT)                       # the block is a Helm block

    def test_sizes(self):
        import _common as C
        self.assertLessEqual(len(C.script_text('ShrikeAI.cs')), C.SCRIPT_LIMITS['ShrikeAI.cs'])
        self.assertLessEqual(len(C.script_text('SpotterAI.cs')), 80000)


class CornerMathTests(unittest.TestCase):
    """the plan's thrust box: best acceleration across a line of sight (docs section 3.1), mirrored in Python"""
    BOX = dict(R=15.8, L=15.8, U=15.8, D=15.8, F=31.5, B=31.5)

    def corners(self):
        for sr in (1, -1):
            for su in (1, -1):
                for sf in (1, -1):
                    yield (sr * (self.BOX['R'] if sr > 0 else self.BOX['L']), su * (self.BOX['U'] if su > 0 else self.BOX['D']), sf * (self.BOX['F'] if sf > 0 else self.BOX['B']))

    def best_across(self, u):
        n = math.sqrt(sum(x * x for x in u)); u = [x / n for x in u]
        best = 0
        for c in self.corners():
            d = sum(a * b for a, b in zip(c, u))
            best = max(best, math.sqrt(max(sum(a * a for a in c) - d * d, 0)))
        return best

    def test_figures_of_the_plan(self):
        self.assertAlmostEqual(self.best_across((0, 0, 1)), 22.3, delta=0.2)          # threat on the nose or tail axis
        self.assertAlmostEqual(self.best_across((0, 1, 0)), 35.2, delta=0.3)          # abeam in any plane
        self.assertAlmostEqual(self.best_across((1, 1, 1)), 38.6, delta=0.3)          # on a body diagonal
        for u in ((0, 0, 1), (0, 1, 0), (1, 0, 0), (1, 1, 1), (1, 2, 3)):
            self.assertGreaterEqual(self.best_across(u), 22.0)

    def test_period_clamp_matches_the_script(self):
        self.assertIn('MathHelper.Clamp((0.7 + 0.8 * _lkRng.NextDouble()) * reff / LkVp, LkPerMin, LkPerMax)', SPOT)
        for reff in (100, 500, 1100, 1600):
            for r in (0.0, 1.0):
                t = min(max((0.7 + 0.8 * r) * reff / 800, 0.8), 2.5)
                self.assertTrue(0.8 <= t <= 2.5)

    def test_band_exclusion_rules_are_in_the_script(self):
        self.assertIn('cu > 0.2 && dT < _lkStand - LkMargin', SPOT)        # not closing in on the large target below its stand-off
        self.assertIn('cu < -0.2 && dT > LkMaxFire', SPOT)                 # not opening out of the rails' range



class BigShipTests(unittest.TestCase):
    """2026-10-14: the LOOKOUT block also runs in the Staff PB of the Warden, Leviathan and Corsair (docs/design/ballista_harasser_defence.md section 12)."""
    def test_staff_carries_the_block_off_by_default(self):
        staff = src('StaffAI.cs')
        self.assertIn('// >>> LOOKOUT v', staff)
        self.assertIn('bool LkDefOn = false;', staff)                 # a Shrike's Staff must not dodge small craft: they are its targets
        self.assertIn('bool LkDefOn = true;', SPOT)
        self.assertIn('LkStep()', staff); self.assertIn('LkLoad();', staff); self.assertIn('LkStatus()', staff)

    def test_one_lookout_per_ship(self):
        with open(os.path.join(TOOLS, 'lookout_block.cs'), encoding='utf-8') as f: lk = f.read()
        self.assertIn('_lkYield = sp.Count > 0 && !Me.CustomName.StartsWith("[Spotter]"', lk)   # a Staff copy stands down while a Spotter PB is on the ship
        self.assertIn('"[Staff]"', HELM)                               # the Helm sends to a Staff PB too

    def test_big_ships_enable_it_in_the_staff_custom_data(self):
        import importlib, json
        for mod, model in (('warden', 'Warden'), ('leviathan', 'Leviathan'), ('corsair', 'Corsair')):
            bp = importlib.import_module(mod).MODELS[model](False)
            staff = [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock') and b['name'].startswith('[Staff]')]
            self.assertEqual(len(staff), 1, model)
            cd = staff[0].get('custom_data') or ''
            self.assertIn('[Harasser Defence]', cd, model); self.assertIn('Enabled=true', cd, model)
        import shrike
        bp = shrike.MODELS[list(shrike.MODELS)[0]](False)
        for b in bp.blocks:
            if b['xtype'].endswith('MyProgrammableBlock'): self.assertNotIn('Harasser Defence', b.get('custom_data') or '')

if __name__ == '__main__':
    unittest.main(verbosity=2)
