#!/usr/bin/env python3
"""Tests for tools/designs/ballista.py (railgun drone), peeper.py (vitals probe) and the Spotter / Helm / Peeper wiring.
python3 -I tools/tests/test_ballista.py   (unittest; no third-party packages)

Checks the owner's brief (27 large railguns, PCU under 10,000 with the foundry and one printed probe, no DLC), the agility figures, the
docked foundry (two connector pairs, bay exit clear), and that the three scripts agree on every IGC tag and message shape."""
import os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.dirname(TOOLS)
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path:
        sys.path.insert(0, p)
import _common as C                                                # noqa: E402
import ballista as B                                               # noqa: E402
import peeper as P                                                 # noqa: E402
import pods                                                        # noqa: E402

_CACHE = {}
PCU_LIMIT = 10000


def full_no_foundry():
    if 'f' not in _CACHE:
        _CACHE['f'] = B.ballista(False, with_foundry=False)
    return _CACHE['f']


def lite_docked():
    if 'l' not in _CACHE:
        _CACHE['l'] = B.ballista(True)
    return _CACHE['l']


def pcu(bp):
    return sum(C._stat(b).get('pcu') or 0 for b in bp.blocks)


def src(name):
    return open(os.path.join(ROOT, 'scripts', name), encoding='utf-8').read()


class ShipTests(unittest.TestCase):
    def test_module_contract(self):
        self.assertEqual(list(B.MODELS), ['Ballista'])
        self.assertEqual(B.SCRIPTS, ['ShrikeAI.cs', 'SpotterAI.cs', 'HiveMotherAI.cs', 'PeeperAI.cs'])

    def test_27_large_railguns_pre_loaded(self):
        bp = full_no_foundry()
        rails = [b for b in bp.blocks if b['sub'] == 'LargeRailgun']
        self.assertEqual(len(rails), 27)
        for b in rails:
            self.assertEqual(sum(it.amount for it in bp.items_of(b) if it.kind == 'AmmoMagazine'), 6)
            self.assertIn('[B1]', b['name'])

    def test_no_dlc_no_prototech(self):
        pods.assert_clean(full_no_foundry())
        pods.assert_clean(lite_docked())

    def test_pcu_under_the_limit_with_foundry_and_one_probe(self):
        n = B.numbers(lite_docked())
        ship = pcu(full_no_foundry())
        foundry_full = n['pcu_foundry'] + 400          # the Full foundry is ~270 PCU above the Lite one (sensor-free shell, same welders)
        probe = pcu(P.peeper(False)) + len(P.peeper(False).blocks)      # a live probe + its projection (1 PCU per projected block)
        self.assertLess(ship + foundry_full + probe, PCU_LIMIT)
        self.assertLess(ship, 5000)

    def test_agility(self):
        acc = B.numbers(lite_docked())['accel_loaded_m_s2']
        for d in ('Up', 'Down', 'Left', 'Right'):
            self.assertGreaterEqual(acc[d], 15.0, d)
        self.assertGreaterEqual(acc['Forward'], 30.0)
        self.assertGreaterEqual(acc['Backward'], 30.0)
        n = B.numbers(lite_docked())
        self.assertGreater(min(n['turn_rad_s2_pitch_yaw_roll']), 3.0)

    def test_every_thruster_touches_a_tank(self):
        bp = full_no_foundry()
        occ = bp.occupancy()
        tanks = {id(b) for b in bp.blocks if b['sub'] == 'LargeHydrogenTank'}
        for t in [b for b in bp.blocks if b['xtype'].endswith('_Thrust')]:
            cells = set(bp.cells(t))
            nb = {id(occ[(c[0] + d[0], c[1] + d[1], c[2] + d[2])]) for c in cells for d in sebp_dirs() if (c[0] + d[0], c[1] + d[1], c[2] + d[2]) in occ}
            self.assertTrue(nb & tanks, t['name'])

    def test_names_the_scripts_rely_on(self):
        bp = full_no_foundry()
        names = [b['name'] for b in bp.blocks if b['name']]
        self.assertTrue(all(n.startswith('[SH]') or n.startswith('[Helm][SH]') or n.startswith('[Spotter][SH]') for n in names), 'every block carries the [SH] prefix')
        self.assertEqual(sum(1 for n in names if n.startswith('[Helm]')), 1)
        self.assertEqual(sum(1 for n in names if n.startswith('[Spotter]')), 1)
        self.assertEqual(sum(1 for n in names if 'Foundry Connector' in n), 2)

    def test_three_pbs_and_the_spare_is_empty_and_inert(self):
        for lite in (False, True):
            bp = B.ballista(lite, with_foundry=False)
            pbs = [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]
            self.assertEqual(len(pbs), 3)
            spare = [b for b in pbs if 'Spare' in b['name']]
            self.assertEqual(len(spare), 1)
            self.assertFalse(spare[0]['extra'], 'the spare PB carries no program')
            self.assertFalse(spare[0]['name'].lower().startswith('[staff]'), 'the Helm would switch its Staff link on')
            self.assertNotIn('Hive Mother', spare[0]['name'])


def sebp_dirs():
    import sebp
    return list(sebp.DIR.values())


class FoundryTests(unittest.TestCase):
    def test_foundry_docked_on_two_connector_pairs(self):
        bp = lite_docked()
        self.assertEqual(len(bp.subgrids), 1)
        fb = bp.foundry
        self.assertEqual(len([b for b in fb.blocks if b['sub'] == 'ConnectorMedium']), 2)
        self.assertEqual(len(fb.bays), 1)
        self.assertEqual(fb.bays[0]['drone'].name, 'Peeper Lite')

    def test_foundry_keeps_clear_and_the_probe_can_leave(self):
        self.assertTrue(B.check_foundry(lite_docked()))

    def test_foundry_does_not_start_by_itself(self):
        fb = lite_docked().foundry
        data = [b['custom_data'] for b in fb.blocks if b.get('custom_data') and '[Hive Mother]' in b['custom_data']][0]
        self.assertIn('Auto Start=false', data)
        self.assertIn('Max Drones (all tags)=1', data)
        self.assertIn('Tag=PEEPER', data)

    def test_the_ship_can_not_dock_a_foundry_whose_exit_is_blocked(self):
        # negative control: ask for an exit direction the ship's own hull covers
        bp = lite_docked()
        fb = bp.foundry
        saved = bp.subgrids[:]
        try:
            with self.assertRaises(AssertionError):
                B.dock_foundry(B.ballista(True, with_foundry=False), fb, [b for b in B.ballista(True, with_foundry=False).blocks if b['kind'] == 'conn'], want='Forward')
        finally:
            bp.subgrids[:] = saved


class ProbeTests(unittest.TestCase):
    def test_probe_variants(self):
        full, lite = P.peeper(False), P.peeper(True)
        self.assertEqual(len([b for b in full.blocks if b['xtype'].endswith('_LargeGatlingTurret')]), 1)
        self.assertEqual(len([b for b in lite.blocks if b['xtype'].endswith('_LargeGatlingTurret')]), 0)
        for bp in (full, lite):
            self.assertEqual(len([b for b in bp.blocks if b['kind'] == 'merge']), 1)
            self.assertEqual(len([b for b in bp.blocks if b['xtype'].endswith('_Thrust')]), 12)
            self.assertEqual(len([b for b in bp.blocks if b['kind'] == 'gun' and b['xtype'].endswith('SmallGatlingGun')]), 0, 'no armed guns')
            self.assertEqual(C.check_docking(bp, [b for b in bp.blocks if b['kind'] == 'merge'][0]), 'Down')
        self.assertLess(C.total_mass(full), 6000)
        acc, _ = C.twr(full)
        self.assertGreater(min(acc.values()), 5.0)

    def test_probe_has_no_ammunition(self):
        bp = P.peeper(False)
        t = [b for b in bp.blocks if b['xtype'].endswith('_LargeGatlingTurret')][0]
        self.assertEqual(len(bp.items_of(t)), 0)


class ScriptWiringTests(unittest.TestCase):
    """the Helm, the Spotter and the Peeper must agree on tags and message shapes (they only meet over IGC)"""

    def test_default_tags_agree(self):
        bd = B.HELM_DATA
        self.assertIn('IGC Tag=BALLISTA', bd)
        self.assertIn('Host Tag=BALLISTA', P.CUSTOM)
        self.assertIn('IGC Tag=PEEPER', P.CUSTOM)
        self.assertIn('string HostTag = "BALLISTA", ProbeTag = "PEEPER"', src('SpotterAI.cs'))
        self.assertIn('string Tag = "PEEPER", HostTag = "BALLISTA"', src('PeeperAI.cs'))

    def test_message_shapes(self):
        pee, spo, hel = src('PeeperAI.cs'), src('SpotterAI.cs'), src('ShrikeAI.cs')
        crit = 'MyTuple<long, int, Vector3D, Vector3D, long>'
        self.assertIn(crit, pee); self.assertIn(crit, spo)
        self.assertIn('HostTag + "_CRIT"', pee); self.assertIn('HostTag + "_CRIT"', spo)
        self.assertIn('HostTag + "_AIM"', spo); self.assertIn('Tag + "_AIM"', hel)
        self.assertIn('MyTuple<long, Vector3D>', spo); self.assertIn('MyTuple<long, Vector3D>', hel)
        swarm = 'MyTuple<long, Vector3D, Vector3D, long, int>'
        self.assertIn(swarm, pee); self.assertIn(swarm, spo)
        self.assertIn('Tag + "_SWARM"', pee)
        tgt = 'MyTuple<long, Vector3D, Vector3D, double, long>'
        for s in (pee, spo): self.assertIn(tgt, s)

    def test_target_groups_power_first(self):
        pee, spo = src('PeeperAI.cs'), src('SpotterAI.cs')
        self.assertIn('{ "PowerSystems", "Weapons", "Propulsion", "Default" }', pee)
        self.assertIn('Cycle = { 0, 1, 0, 2 }', pee)                    # power twice per round
        self.assertIn('string PrioText = "0,1,2"', spo)

    def test_hive_commands_the_spotter_sends_exist(self):
        hive = src('HiveMotherAI.cs')
        for verb in ('start', 'stop', 'max'):
            self.assertIn(f'case "{verb}"', hive)

    def test_helm_custom_data_keys_are_literals_in_the_script(self):
        hel = src('ShrikeAI.cs')
        for m in re.finditer(r'^([A-Za-z][^=\n\[]*?)=', B.HELM_DATA, re.M):
            self.assertIn(m.group(1), hel, m.group(1))

    def test_scripts_fit_the_programmable_block(self):
        for f, lim in (('ShrikeAI.cs', C.SCRIPT_LIMITS['ShrikeAI.cs']), ('SpotterAI.cs', 100000), ('PeeperAI.cs', 100000), ('HiveMotherAI.cs', 100000)):
            self.assertLessEqual(len(C.script_text(f)), lim, f)

    def test_scripts_are_embedded(self):
        bp = full_no_foundry()
        progs = [b['extra'] for b in bp.blocks if '<Program>' in (b.get('extra') or '')]
        self.assertEqual(len(progs), 2)
        fb = lite_docked().foundry
        self.assertTrue(any('<Program>' in (b.get('extra') or '') for b in fb.blocks))
        pee = P.peeper(True)
        self.assertTrue(any('<Program>' in (b.get('extra') or '') for b in pee.blocks))


if __name__ == '__main__':
    unittest.main(verbosity=1)
