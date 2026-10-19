#!/usr/bin/env python3
"""The PB budget (CLAUDE.md "Script size", docs/design/flight_split.md section 9.2): every scripts/*.cs is <= 80,000 characters minified.
The only exception is LEGACY = {'WaspAI.cs'} (the pre-split Wasp, no blueprint carries it).   python3 -I tools/tests/test_pb_budget.py
Above 76,000 the test prints a "plan the next split" note (it does not fail)."""
import glob, os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.dirname(TOOLS)
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
from minify_cs import minify                                         # noqa: E402

BUDGET = 80000
NOTE_AT = 76000
LEGACY = {'WaspAI.cs'}

def budget():
    try:
        import _common as C
        return getattr(C, 'PB_BUDGET', BUDGET)
    except Exception:
        return BUDGET

def sizes():
    out = {}
    for f in sorted(glob.glob(os.path.join(ROOT, 'scripts', '*.cs'))):
        with open(f, encoding='utf-8') as fh: out[os.path.basename(f)] = len(minify(fh.read()))
    return out

class PbBudget(unittest.TestCase):
    def test_every_script_is_within_the_budget(self):
        over = {n: k for n, k in sizes().items() if n not in LEGACY and k > BUDGET}
        self.assertFalse(over, f'over the {BUDGET:,} PB budget: move an off-hot-path module into another PB (CLAUDE.md \'Script size\', '
                               f'docs/design/flight_split.md section 10); do not trim features or raise the budget. Over: ' +
                               ', '.join(f'{n} {k:,}' for n, k in sorted(over.items())))

    def test_note_above_76k(self):
        near = {n: k for n, k in sizes().items() if n not in LEGACY and NOTE_AT < k <= BUDGET}
        for n, k in sorted(near.items()):
            print(f'  NOTE: {n} is {k:,} minified (> {NOTE_AT:,}): plan the next split now (docs/design/flight_split.md section 10)', file=sys.stderr)

    def test_the_common_budget_matches(self):
        """P1-D adds PB_BUDGET to tools/designs/_common.py: it must be the same number as this test"""
        import _common as C
        if not hasattr(C, 'PB_BUDGET'):
            self.skipTest('pending: tools/designs/_common.py has no PB_BUDGET yet (P1-D, flight_split.md 9.2)')
        self.assertEqual(C.PB_BUDGET, BUDGET, 'C.PB_BUDGET and the budget of test_pb_budget.py differ; the rule is 80,000 (CLAUDE.md Script size)')

    def test_legacy_scripts_are_never_built_into_a_blueprint(self):
        pat = re.compile(r'script_text\(')
        for f in glob.glob(os.path.join(ROOT, 'tools', 'designs', '*.py')):
            t = open(f, encoding='utf-8').read()
            for leg in LEGACY:
                for m in pat.finditer(t):
                    arg = t[m.end():m.end() + 80]
                    self.assertNotIn(leg, arg, f'{os.path.basename(f)} passes the LEGACY script {leg} to script_text( : it is over the PB budget and exempt only because no blueprint carries it '
                                               '(CLAUDE.md \'Script size\', flight_split.md section 10)')

    def test_legacy_set_is_only_wasp(self):
        self.assertEqual(LEGACY, {'WaspAI.cs'})

if __name__ == '__main__':
    unittest.main(verbosity=2)
