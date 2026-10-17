#!/usr/bin/env python3
"""Tests for tools/designs/ballista.py (railgun drone), peeper.py (vitals probe) and the Spotter / Helm / Peeper wiring.
python3 -I tools/tests/test_ballista.py   (unittest; no third-party packages)

Checks the owner's brief (27 large railguns, PCU under 10,000, no DLC), the agility figures, that no thruster flame (whole nozzle face, 7 cells) touches a block,
that the four vanilla point-defence turrets see forward and are set for small grids and missiles only, and that the scripts agree on every IGC tag and message shape.
The probe foundry left the Ballista on 2026-10-16 (docs/design/ships/Ballista.md section 10); the Peeper and the Probe Foundry stay as stand-alone models, tested here too."""
import math, os, re, sys, unittest
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


def full():
    if 'f' not in _CACHE:
        _CACHE['f'] = B.ballista(False)
    return _CACHE['f']


def lite():
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
        self.assertEqual(B.SCRIPTS, ['ShrikeAI.cs', 'SpotterAI.cs', 'StaffAI.cs'])

    def test_27_large_railguns_pre_loaded(self):
        bp = full()
        rails = [b for b in bp.blocks if b['sub'] == 'LargeRailgun']
        self.assertEqual(len(rails), 27)
        for b in rails:
            self.assertEqual(sum(it.amount for it in bp.items_of(b) if it.kind == 'AmmoMagazine'), 6)
            self.assertTrue('[B1]' in b['name'] or '[B2] PD' in b['name'], b['name'])

    def test_point_defence_turrets(self):
        bp = full()
        names = [b['name'] for b in bp.blocks if b['sub'] == 'LargeRailgun']
        self.assertEqual(sum('[B1]' in n for n in names), 27 - len(B.PD_RAILS))
        tur = [b for b in bp.blocks if b['xtype'].endswith('LargeGatlingTurret')]
        self.assertEqual(len(tur), 4)                                    # vanilla only (owner, 2026-10-16): two on top, two under the bow
        self.assertEqual(sorted(b['up'] for b in tur), ['Down', 'Down', 'Up', 'Up'])
        self.assertFalse([b for b in bp.blocks if b['name'] and b['name'].startswith('[TU]')], 'no rotor turrets and no turret PB any more')
        self.assertEqual(bp.subgrids, [])
        for b in tur:
            self.assertTrue(b['name'].startswith('[SH] [PD]'), b['name'])           # the Helm's Link prefix; "[PD]" is the repo's point-defence tag
            self.assertGreater(sum(it.amount for it in bp.items_of(b)), 0)           # loaded
        self.assertIn('Point Defence (OFF/B1..B4)', B._script('ShrikeAI.cs'))

    def test_turrets_target_drones_and_rockets_only(self):
        xml = full().build_xml()
        blocks = re.findall(r'<CustomName>\[SH\] \[PD\] Gatling Turret[^<]*</CustomName>(.*?)</MyObjectBuilder_CubeBlock>', xml, re.S)
        self.assertEqual(len(blocks), 4)
        for body in blocks:
            flags = re.search(r'<Flags>(.*?)</Flags>', body, re.S).group(1)
            off = re.findall(r'<(\w+)>false</\1>', flags)
            self.assertEqual(off, ['TargetMeteors', 'TargetLargeGrids', 'TargetCharacters', 'TargetStations'])     # the game's field order; written only when not default
            self.assertNotIn('TargetSmallGrids', flags); self.assertNotIn('TargetMissiles', flags)                # on (default)
            self.assertEqual(body.count('<Flags>'), 1)
            self.assertIn('<TargetCharacters>false</TargetCharacters>', body.split('<Flags>')[0])

    def test_turrets_see_forward(self):
        """owner (2026-10-16): the turrets were blocked from seeing the front.  From each turret's gun head, rays ahead (azimuth 0, +-20, +-45 deg) from 10 deg below to
        30 deg above its own horizon must leave the ship without crossing another block"""
        bp = full()
        occ = bp.occupancy()
        for b in [b for b in bp.blocks if b['kind'] == 'turret']:
            cs = bp.cells(b); s = 1 if b['up'] == 'Up' else -1
            ys = [c[1] for c in cs]
            o = (sum(c[0] for c in cs) / len(cs) + .5, max(ys) + .8 if s > 0 else min(ys) + .2, sum(c[2] for c in cs) / len(cs) + .5)
            for el in (-10, 0, 10, 30):
                for az in (0, 20, -20, 45, -45):
                    e, a = math.radians(el), math.radians(az)
                    d = (math.sin(a) * math.cos(e), s * math.sin(e), -math.cos(a) * math.cos(e))
                    for i in range(1, 400):
                        c = tuple(math.floor(o[k] + d[k] * i * 0.1) for k in range(3))
                        self.assertFalse(c in occ and occ[c] is not b, f'{b["name"]}: elevation {el}, azimuth {az} hits {c in occ and occ[c]["name"]}')

    def test_the_wing_spreads_out_and_clears_its_wingmates(self):
        for k in ('Kamikaze Rail Hold Range (m)', 'Wingmate Separation Range (m)', 'Wingmate Separation Speed (m/s)', 'Wingmate Fire Clearance (m)'):
            self.assertIn(k + '=', B.HELM_DATA)
            self.assertIn(k, B._script('ShrikeAI.cs'))

    def test_no_dlc_no_prototech(self):
        pods.assert_clean(full())
        pods.assert_clean(lite())

    def test_pcu_under_the_limit(self):
        self.assertLess(B.numbers(full())['pcu'], 6000)        # 5,108: no foundry, no probe, no rotor turrets (was about 9,860 of the 10,000 brief with a live probe)

    def test_agility(self):
        acc = B.numbers(lite())['accel_loaded_m_s2']
        for d in ('Up', 'Down', 'Left', 'Right'):
            self.assertGreaterEqual(acc[d], 15.0, d)
        self.assertGreaterEqual(acc['Forward'], 31.2)             # the figure before the extra reactors (mass +21 %) and the extra thrusters
        self.assertGreaterEqual(acc['Backward'], 31.2)
        for d in ('Up', 'Down', 'Left', 'Right'):
            self.assertGreaterEqual(acc[d], 15.6, d)
        n = B.numbers(lite())
        self.assertGreater(min(n['turn_rad_s2_pitch_yaw_roll']), 3.0)

    def test_power_recharges_all_27_rails_without_a_bottleneck(self):
        pw = B.numbers(lite())['power']
        need = 27 * 38                                            # RechargeDraw of a LargeRailgun, MW (defs_stats / sim settings)
        self.assertGreaterEqual(pw['generation_MW'] + pw['battery_MW'], need + 10)        # peak: reactors + batteries cover every gun at its full draw, plus 10 MW for the other loads (gyros, PBs, turrets: under 5 MW)
        self.assertGreaterEqual(pw['generation_MW'], need * 0.85)                         # sustained: the reactors alone carry the Helm's ~66 s salvo cadence (~922 MW average)
        self.assertLessEqual(pw['rearm_s_supply_bound'], pw['rearm_s_per_gun'])           # power is no longer what limits the re-arm: the capacitor's own 59 s is

    def test_no_block_in_an_exhaust_or_muzzle_lane(self):
        for bp in (full(), lite()):
            occ = bp.occupancy()
            bad = [(c, why, occ[c]['name']) for c, why in bp.reserve.items() if c in occ and not why.startswith('camera')]
            self.assertEqual(bad, [])

    def test_exhaust_lanes_cover_the_whole_nozzle_7_deep(self):
        bp = full()
        for t in [b for b in bp.blocks if b['xtype'].endswith('_Thrust')]:
            lane = [c for c, why in bp.reserve.items() if why == f'exhaust of {t["name"]}']
            self.assertGreaterEqual(len(lane), 9 * B.H2_LANE - 9, t['name'])          # 3 x 3 face x 7 cells (a camera view may share a cell or two)
        self.assertGreaterEqual(B.H2_LANE, 7)

    def test_the_lane_check_catches_the_old_belly_burn(self):
        # negative control: the 2026-10-14 belly lateral (x -4..-2, y -3..-1, z 4..6, exhaust Left) beside the port lower pod's vertical thruster (x -7..-5, z 5..7)
        import sebp
        bp = sebp.Blueprint('burn', 'Large', seed=1)
        old = bp.add('MyObjectBuilder_Thrust', 'LargeBlockLargeHydrogenThrust', (-4, -3, 4), 'Left', 'Up', 'old lateral', kind='thr')
        bp.add('MyObjectBuilder_Thrust', 'LargeBlockLargeHydrogenThrust', (-7, -3, 5), 'Down', 'Forward', 'pod vertical', kind='thr')
        bp.keep_clear(old, 3, 'exhaust')
        self.assertFalse([c for c in bp.reserve if c in bp.occupancy()], 'the old 1-lane check saw nothing')
        bp.reserve.clear(); bp.keep_clear(old, B.H2_LANE, 'exhaust', face=True)
        self.assertTrue([c for c in bp.reserve if c in bp.occupancy()], 'the full-face check sees the burn')

    def test_every_thruster_touches_a_tank(self):
        bp = full()
        occ = bp.occupancy()
        tanks = {id(b) for b in bp.blocks if b['sub'] == 'LargeHydrogenTank'}
        for t in [b for b in bp.blocks if b['xtype'].endswith('_Thrust')]:
            cells = set(bp.cells(t))
            nb = {id(occ[(c[0] + d[0], c[1] + d[1], c[2] + d[2])]) for c in cells for d in sebp_dirs() if (c[0] + d[0], c[1] + d[1], c[2] + d[2]) in occ}
            self.assertTrue(nb & tanks, t['name'])

    def test_names_the_scripts_rely_on(self):
        bp = full()
        names = [b['name'] for b in bp.blocks if b['name']]
        self.assertTrue(all(n.startswith('[SH]') or n.startswith('[Helm][SH]') or n.startswith('[Spotter][SH]') or n.startswith('[Staff][SH]') for n in names), 'every block carries the [SH] prefix')
        self.assertEqual(sum(1 for n in names if n.startswith('[Helm]')), 1)
        self.assertEqual(sum(1 for n in names if n.startswith('[Spotter]')), 1)
        self.assertFalse([n for n in names if 'Connector' in n or 'Hive Mother' in n], 'no probe foundry and nothing for one to dock on')
        spot = [b for b in bp.blocks if (b['name'] or '').startswith('[Spotter]')][0]
        self.assertIn('Bay Control=0', spot['custom_data'])                # the Spotter keeps its Lookout and leaves the (absent) foundry alone

    def test_three_pbs_helm_spotter_and_staff(self):
        for lite in (False, True):
            bp = B.ballista(lite)
            pbs = [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]
            self.assertEqual(len(pbs), 3)          # Helm, Spotter, Staff
            staff = [b for b in pbs if b['name'].lower().startswith('[staff]')]
            self.assertEqual(len(staff), 1)
            self.assertIn('Mothership=false', staff[0]['custom_data'])      # the Ballista carries no drones: no <Type>_HOME / FLEET_HOME
            self.assertNotIn('Hive Mother', staff[0]['name'])               # (the Spotter would take it for the foundry)


def sebp_dirs():
    import sebp
    return list(sebp.DIR.values())


class FoundryTests(unittest.TestCase):
    """the Probe Foundry is a stand-alone printer now (printers.py); it still prints Peepers"""
    def test_probe_foundry_still_builds(self):
        import printers as PR
        fb = PR.MODELS['Probe Foundry'](True)
        self.assertEqual(len(fb.bays), 1)
        self.assertEqual(fb.bays[0]['drone'].name, 'Peeper Lite')


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
        bp = full()
        progs = [b['extra'] for b in bp.blocks if '<Program>' in (b.get('extra') or '')]
        self.assertEqual(len(progs), 3)        # Helm, Spotter, Staff
        pee = P.peeper(True)
        self.assertTrue(any('<Program>' in (b.get('extra') or '') for b in pee.blocks))


if __name__ == '__main__':
    unittest.main(verbosity=1)
