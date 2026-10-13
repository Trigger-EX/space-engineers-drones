"""Tests for the M0 generator additions (docs/design/control_arch.md section 7, M0): a cockpit and a second Programmable Block in a blueprint,
the PB switched ON by default, placeholder Event Controller ids, the TextPanel shape, the test ships (tools/designs/testships.py).
Run:  python3 -I tools/tests/run_all.py   (these tests need no sample files, except the TextPanel shape which is in shapes.json)
"""
import os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import sebp, sebp_toolbar as TB
from sebp import Blueprint, ET, DEFS

T = lambda t: 'MyObjectBuilder_' + t

def xml_of(bp):
    return ET.fromstring(bp.build_xml(bp.name))

def blocks_of(root, xtype):
    return [b for b in root.iter('MyObjectBuilder_CubeBlock') if (b.get('{http://www.w3.org/2001/XMLSchema-instance}type') or '') == T(xtype)]

class TestPb(unittest.TestCase):
    def test_a_generated_pb_is_switched_on_even_with_sample_shapes(self):
        """the sample-derived PB shape came from a switched-off PB (Pond Hopper); a generated ship with a script must run it"""
        bp = Blueprint('t', 'Small', seed=1)
        bp.armor(0, 0, 0)
        bp.add(T('MyProgrammableBlock'), 'SmallProgrammableBlock', (0, 0, 1), 'Backward', 'Up', name='[Helm] PB')
        bp.add(T('MyProgrammableBlock'), 'SmallProgrammableBlock', (1, 0, 0), 'Left', 'Up', name='[Staff] PB', enabled=False)
        root = xml_of(bp)
        pbs = blocks_of(root, 'MyProgrammableBlock')
        self.assertEqual([b.findtext('CustomName') for b in pbs], ['[Helm] PB', '[Staff] PB'])
        self.assertEqual([b.findtext('Enabled') for b in pbs], ['true', 'false'])
        self.assertEqual([b.findtext('NumberInGrid') for b in pbs], ['1', '2'])     # two PBs: numbered in order, ids distinct
        self.assertNotEqual(pbs[0].findtext('EntityId'), pbs[1].findtext('EntityId'))

class TestPlaceholderEvents(unittest.TestCase):
    def test_cockpit_event_needs_experimental(self):
        with self.assertRaises(TB.ToolbarError): TB.event_settings('cockpit')
        s = TB.event_settings('cockpit', threshold=0, experimental=True)
        self.assertEqual(s['SelectedEvent'], 101)
        self.assertEqual(TB.event_settings('power', 0.15, invert=True, experimental=True)['SelectedEvent'], 102)
        self.assertEqual(TB.event_settings('merge', experimental=True)['SelectedEvent'], 103)
        self.assertEqual(TB.event_settings('integrity', 0.5)['SelectedEvent'], 2)          # seen ids need no flag

    def test_placeholder_ids_are_reported_in_the_notes(self):
        bp = Blueprint('t', 'Small', seed=1)
        seat = bp.add(T('Cockpit'), 'SmallBlockCockpit', (0, 0, 0), 'Forward', 'Up', name='Seat')
        ec = bp.add(T('EventControllerBlock'), 'EventControllerSmall', (3, 0, 0), 'Forward', 'Up', name='EC',
                    settings=TB.event_settings('cockpit', threshold=0, experimental=True), links={'selected': [seat]})
        bp.build_xml('t')
        self.assertTrue(any('SelectedEvent 101' in n and 'game does not know' in n for n in bp.notes), bp.notes)

class TestShapes(unittest.TestCase):
    def test_the_automation_and_control_blocks_have_sample_shapes(self):
        """cockpit, button panel, timer, event controller, door, PB: shapes the game wrote (an LCD panel is NOT in this list: the only TextPanel exemplar
        carries a third-party inventory-manager script text and mod storage, so extract_shapes leaves TextPanel out until a clean sample exists)"""
        self.assertEqual(sebp.shape_for(T('TextPanel'), 'LargeLCDPanel')[1], 'guess')
        for k, sub in (('Cockpit', 'DBSmallBlockFighterCockpit'), ('ButtonPanel', 'ButtonPanelSmall'), ('TimerBlock', 'TimerBlockSmall'),
                       ('EventControllerBlock', 'EventControllerSmall'), ('Door', 'SmallDoor'), ('MyProgrammableBlock', 'SmallProgrammableBlock')):
            self.assertTrue(sebp.shape_for(T(k), sub)[1].startswith('seen'), (k, sub))

class TestTestShips(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import designs
        cls.mods, _ = designs.load()

    def build(self, name):
        for kind, modname, model, fn in __import__('designs').iter_models(self.mods):
            if model == name:
                self.assertEqual(kind, 'test')
                bp = fn(False)
                bp.validate()
                return bp
        self.fail(name)

    def test_hornet_pilot_test_has_the_seat_and_the_automation_chain(self):
        bp = self.build('Hornet Pilot Test')
        root = xml_of(bp)
        seats = blocks_of(root, 'Cockpit')
        self.assertEqual(len(seats), 1)
        self.assertEqual(seats[0].findtext('SubtypeName'), 'SmallBlockCockpit')
        seat_slots = seats[0].findall('Toolbar/Slots/Slot')
        self.assertEqual([s.findtext('Index') for s in seat_slots], ['0', '1'])
        self.assertEqual([s.find('Data').get('{http://www.w3.org/2001/XMLSchema-instance}type') for s in seat_slots], [T('ToolbarItemWeapon'), T('ToolbarItemTerminalBlock')])
        ec = blocks_of(root, 'EventControllerBlock')[0]
        self.assertEqual(ec.findtext('SelectedEvent'), '101')
        self.assertEqual(ec.findall('SelectedBlocks/long')[0].text, seats[0].findtext('EntityId'))
        timer = blocks_of(root, 'TimerBlock')[0]
        self.assertEqual(timer.findtext('Delay'), '5000')
        self.assertEqual(timer.find('Toolbar/Slots/Slot/Data/GroupName').text, 'Thrusters')
        names = [g.findtext('Name') for g in root.iter('MyObjectBuilder_BlockGroup')]
        self.assertEqual(names, ['Thrusters'])
        self.assertEqual(len(list(root.iter('MyObjectBuilder_BlockGroup'))[0].findall('Blocks/Vector3I')), len([b for b in bp.blocks if b['xtype'] == T('Thrust')]))
        self.assertEqual(len(blocks_of(root, 'MyProgrammableBlock')), 0)          # no script, no PB
        self.assertFalse(any(DEFS[b['key']]['dlc'] for b in bp.blocks))

    def test_twin_pb_test_has_two_named_running_pbs(self):
        bp = self.build('Twin PB Test')
        root = xml_of(bp)
        pbs = blocks_of(root, 'MyProgrammableBlock')
        self.assertEqual(sorted(b.findtext('CustomName') for b in pbs), ['[Helm] Twin PB Test PB', '[Staff] Twin PB Test PB'])
        self.assertTrue(all(b.findtext('Enabled') == 'true' and len(b.findtext('Program') or '') > 1000 for b in pbs))
        panel = blocks_of(root, 'ButtonPanel')[0]
        staff = [b for b in pbs if b.findtext('CustomName').startswith('[Staff]')][0]
        self.assertEqual(panel.findtext('Toolbar/Slots/Slot/Data/BlockEntityId'), staff.findtext('EntityId'))
        self.assertEqual(panel.findtext('Toolbar/Slots/Slot/Data/Action'), 'Run')

    def test_test_ships_are_never_packaged(self):
        import designs
        self.assertIn('testships', designs.TEST_MODULES)
        kinds = {k for k, m, n, f in designs.iter_models(self.mods) if n in ('Hornet Pilot Test', 'Twin PB Test')}
        self.assertEqual(kinds, {'test'})
        import build_all
        for base, kinds_, variants, tmpl, intro in build_all.PACKAGES:
            self.assertNotIn('test', kinds_)

class TestBlocksSync(unittest.TestCase):
    def tree(self, d, scripts):
        os.makedirs(os.path.join(d, 'tools')); os.makedirs(os.path.join(d, 'scripts'))
        import json
        json.dump({'legacy': [], 'blocks': {'DEMO': {'file': 'tools/demo_block.cs', 'scripts': ['A', 'B']}}}, open(os.path.join(d, 'tools', 'blocks.json'), 'w'))
        open(os.path.join(d, 'tools', 'demo_block.cs'), 'w').write('// >>> DEMO v1\nint DemoX() { return 1; }\n// <<< DEMO v1\n')
        for n, body in scripts.items(): open(os.path.join(d, 'scripts', n + '.cs'), 'w', newline='').write(body)

    def run_sync(self, d, *args):
        import io, contextlib, blocks_sync
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf): rc = blocks_sync.main(['--root', d, '--no-legacy'] + list(args))
        return rc, buf.getvalue()

    def test_check_apply_unlisted_crlf(self):
        import tempfile
        with tempfile.TemporaryDirectory() as d:
            self.tree(d, {'A': 'class P {\npublic Program() { }\n}\n', 'B': 'class Q {\r\n// >>> DEMO v0\r\nold\r\n// <<< DEMO v0\r\npublic Program() { }\r\n}\r\n',
                          'C': '// >>> DEMO v1\nstale copy\n// <<< DEMO v1\npublic Program() { }\n'})
            rc, out = self.run_sync(d, '--check')
            self.assertEqual(rc, 1)
            self.assertIn('DIFFERS   DEMO A', out); self.assertIn('DIFFERS   DEMO B', out); self.assertIn('UNLISTED  DEMO: C', out)
            os.remove(os.path.join(d, 'scripts', 'C.cs'))
            rc, out = self.run_sync(d, '--apply')
            self.assertEqual(rc, 0, out)
            a = open(os.path.join(d, 'scripts', 'A.cs'), newline='').read()
            b = open(os.path.join(d, 'scripts', 'B.cs'), newline='').read()
            self.assertEqual(a, 'class P {\n// >>> DEMO v1\nint DemoX() { return 1; }\n// <<< DEMO v1\n\npublic Program() { }\n}\n')    # inserted before the anchor
            self.assertIn('\r\nint DemoX() { return 1; }\r\n', b); self.assertNotIn('old', b)                              # replaced, CRLF kept
            rc, out = self.run_sync(d, '--check')
            self.assertEqual(rc, 0, out)

    def test_the_repo_blocks_are_in_sync(self):
        import blocks_sync
        root = os.path.normpath(os.path.join(HERE, '..', '..'))
        import io, contextlib
        with contextlib.redirect_stdout(io.StringIO()): rc = blocks_sync.main(['--root', root, '--check'])
        self.assertEqual(rc, 0)

if __name__ == '__main__':
    unittest.main()
