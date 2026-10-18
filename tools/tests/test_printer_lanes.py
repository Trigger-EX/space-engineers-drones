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

class DockKeys(unittest.TestCase):
    """survivability item 17 / section 3: [Bay N] Dock Forward / Dock Up / Repair in the printer and pod Custom Data"""
    @staticmethod
    def sections(cd):
        out, cur = {}, None
        for ln in cd.splitlines():
            ln = ln.strip()
            if ln.startswith('[') and ln.endswith(']'): cur = out.setdefault(ln[1:-1], {})
            elif '=' in ln and cur is not None and not ln.startswith(';'): k, v = ln.split('=', 1); cur[k] = v
        return out

    def test_dock_axes_follow_rc(self):
        from core import hornet
        d = hornet(False)
        self.assertEqual(PL.dock_axes(d), ((0, 0, -1), (0, 1, 0)))
        import sebp_multi as MU
        self.assertEqual(PL.dock_axes(d, MU.Rot.from_axes('Right', 'Up')), ((1, 0, 0), (0, 1, 0)))

    def test_dock_lines_without_axes_advertise_nothing(self):
        self.assertEqual(PL.dock_lines(None, True), ['Repair=false'])

    def test_printer_bays_carry_keys(self):
        import printers as P
        for name, keys in (('Hornet Forge', True), ('Hive Mother', False)):
            bp = P.MODELS[name](False)
            pb = [b for b in bp.blocks if 'ProgrammableBlock' in b['xtype'] and b.get('custom_data') and '[Bay 1]' in b['custom_data']][0]
            bays = {k: v for k, v in self.sections(pb['custom_data']).items() if k.startswith('Bay ')}
            self.assertEqual(len(bays), 2)
            for k, v in bays.items():
                if keys:
                    self.assertEqual((v['Dock Forward'], v['Dock Up'], v['Repair']), ('0,0,-1', '0,1,0', 'true'), f'{name} {k}')
                else:
                    self.assertNotIn('Dock Forward', v); self.assertNotIn('Dock Up', v); self.assertEqual(v['Repair'], 'false')

    def test_pod_bays_carry_keys_and_match_the_clamp_pose(self):
        import pods
        from core import hornet
        for rep in (False, True):
            pod = pods.cradle_pod(2, repair=rep)
            secs = self.sections(pod.pod.pb['custom_data'])
            d = hornet(False)
            for k in range(2):
                pose = pods.drone_pose(pod, k, d)
                f, u = PL.dock_axes(d, pose.rot)
                v = secs[f'Bay {k + 1}']
                self.assertEqual(v['Dock Forward'], ','.join(map(str, f))); self.assertEqual(v['Dock Up'], ','.join(map(str, u)))
                self.assertEqual(v['Repair'], 'true' if rep else 'false')

if __name__ == '__main__':
    unittest.main()
