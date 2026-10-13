#!/usr/bin/env python3
"""M5: the design data that scripts/CradleBayAI.cs / StaffAI.cs read must agree with the designs (names, boxes, tags).   python3 -I tools/tests/test_cradle_bay.py

The script reads, per [Bay n]: Merge (exact block name), Drone Box Min/Max (host cells), Away (x,y,z) or Away X/Y/Z, Drone Prefix, Timer; per [Pod]: Tag, First Bay.
Here the Custom Data of the Corsair's [Bay] PB and of the Cradle Pod PB is parsed the way the script parses it and checked against the blocks of the built designs."""
import os, sys, re, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path:
        sys.path.insert(0, p)
import _common as C                                                # noqa: E402
import corsair as K                                                # noqa: E402
import pods as P                                                   # noqa: E402


def ini(text):
    d, cur = {}, None
    for ln in text.splitlines():
        ln = ln.strip()
        if not ln: continue
        if ln.startswith('['): cur = ln[1:-1]; d[cur] = {}; continue
        k, v = ln.split('=', 1); d[cur][k.strip()] = v.strip()
    return d


def box(s): return tuple(int(v) for v in s.split(','))


class Corsair(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bp = K.corsair_wing(True)
        cls.cells = K.drone_cells_in_host(cls.bp, cls.bp.subgrids[0][0])

    def test_bay_pb_and_data(self):
        pbs = {b['name']: b for b in self.bp.blocks if b['sub'] == 'SmallProgrammableBlock'}
        self.assertIn('[Bay] Corsair', pbs)
        cd = ini(pbs['[Bay] Corsair']['custom_data'])
        self.assertEqual(cd['Pod']['Tag'], 'SH')
        merges = {b['name']: b for b in self.bp.blocks if b['kind'] == 'cradle'}
        for n in (1, 2):
            s = cd['Bay %d' % n]
            self.assertIn(s['Merge'], merges)
            lo, hi = box(s['Drone Box Min']), box(s['Drone Box Max'])
            self.assertEqual(s['Away'], '0,0,-1')
            self.assertTrue(s['Drone Prefix'])
            # the host merge block sits one cell behind the box (z + 1), centred in x, and is NOT inside the box
            mc = tuple(merges[s['Merge']]['min'])
            self.assertEqual(mc[2], hi[2] + 1)
            self.assertTrue(lo[0] <= mc[0] <= hi[0] and lo[1] <= mc[1] <= hi[1])

    def test_box_contains_exactly_one_drone(self):
        pbs = {b['name']: b for b in self.bp.blocks if b['sub'] == 'SmallProgrammableBlock'}
        cd = ini(pbs['[Bay] Corsair']['custom_data'])
        for drone, pose in self.bp.subgrids:
            cells = list(K.drone_cells_in_host(self.bp, drone))
            inside = []
            for n in (1, 2):
                lo, hi = box(cd['Bay %d' % n]['Drone Box Min']), box(cd['Bay %d' % n]['Drone Box Max'])
                if all(lo[i] <= c[i] <= hi[i] for c in cells for i in range(3)):
                    inside.append(n)
            self.assertEqual(len(inside), 1, f'{drone.name}: its cells must lie inside exactly one [Bay n] box ({inside})')
        # drones of either bay start with the configured prefix
        self.assertTrue(all(b['name'].startswith(cd['Bay 1']['Drone Prefix']) for b in self.bp.subgrids[0][0].blocks if b['name']))

    def test_tags_and_prefix_rule(self):
        pbs = {b['name']: b for b in self.bp.blocks if b['sub'] == 'SmallProgrammableBlock'}
        helm, staff = ini(pbs['[Helm] Corsair']['custom_data']), ini(pbs['[Staff] Corsair']['custom_data'])
        self.assertEqual(helm['Link']['Block Prefix'], '[SH]')
        self.assertEqual(staff['Staff']['Block Prefix'], '[SH]')
        self.assertEqual(helm['Ship']['Tag'], staff['Ship']['Tag'])           # Helm and Staff scripts fall back to [Ship] Tag
        self.assertEqual(ini(pbs['[Bay] Corsair']['custom_data'])['Pod']['Tag'], staff['Ship']['Tag'])
        # every block the Helm may collect (anything but the PBs) carries the prefix; the drones' blocks never do
        for b in self.bp.blocks:
            if b['sub'] != 'SmallProgrammableBlock' and b['name']: self.assertTrue(b['name'].startswith('[SH] '), b['name'])        # (armour has no name)


class Pod(unittest.TestCase):
    def test_pod_data(self):
        pod = P.cradle_pod(4, ship_tag='WARDEN')
        cd = ini(pod.pod.pb['custom_data'])
        self.assertEqual(cd['Pod']['Tag'], 'WARDEN'); self.assertEqual(cd['Pod']['First Bay'], '1')
        names = {b['name'] for b in pod.blocks}
        for k, s in enumerate(pod.pod.slots, 1):
            sec = cd['Bay %d' % k]
            self.assertIn(sec['Merge'], names); self.assertIn(sec['Timer'], names)
            self.assertEqual(box(sec['Drone Box Min']), tuple(s.box_min)); self.assertEqual(box(sec['Drone Box Max']), tuple(s.box_max))
            self.assertEqual(sec['Away'], '0,0,-1')
        pod2 = P.cradle_pod(4, ship_tag='LEVIATHAN', first_bay=5)
        self.assertEqual(ini(pod2.pod.pb['custom_data'])['Pod']['First Bay'], '5')

    def test_every_pod_block_is_not_a_drone_block(self):
        # the script treats a block whose name does not start with '[' as the clamped drone's: every pod block must start with '[' (the PB is '[Pod] ...')
        pod = P.cradle_pod(2, ship_tag='SHIP')
        for b in pod.blocks:
            if b['sub'] in ('CubeBlock',) or b['kind'] == 'armor': continue
            self.assertTrue((b['name'] or '[').startswith('['), b['name'])


class Script(unittest.TestCase):
    def test_script_present_and_in_module_lists(self):
        self.assertTrue(os.path.exists(os.path.join(C.ROOT, 'scripts', 'CradleBayAI.cs')))
        self.assertIn('CradleBayAI.cs', K.SCRIPTS)
        src = open(os.path.join(C.ROOT, 'scripts', 'CradleBayAI.cs'), encoding='utf-8').read()
        for key in ('Drone Box Min', 'Drone Box Max', 'Drone Prefix', 'First Bay', '"Merge"', '"Away"', '".S2P"', '".P2S"'):
            self.assertIn(key.strip('"'), src)


if __name__ == '__main__':
    unittest.main()
