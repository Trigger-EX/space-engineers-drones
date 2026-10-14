#!/usr/bin/env python3
"""Tests for tools/designs/shrike.py (the fighter).   python3 -I tools/tests/test_shrike.py   (unittest; no third-party packages)

Checks the design contract (MODELS / DOCS / SCRIPTS, blueprint names), the rules of the brief (no DLC, no Prototech, seen shapes only in the
Lite variant, one PB, names and tags the scripts rely on, symmetry), the numbers (acceleration against the catalogue, pre-loaded ammunition,
fuel, charge) and that every thruster touches a hydrogen tank."""
import os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path:
        sys.path.insert(0, p)
import sebp                                                        # noqa: E402
import hulls as H                                                  # noqa: E402
import _common as C                                                # noqa: E402
import shrike as S                                                 # noqa: E402

_CACHE = {}


def ship(lite):
    if lite not in _CACHE:
        _CACHE[lite] = S.shrike(lite)
    return _CACHE[lite]


def named(bp, text):
    return [b for b in bp.blocks if b['name'] and text in b['name']]


class ContractTests(unittest.TestCase):
    def test_module_contract(self):
        self.assertEqual(list(S.MODELS), ['Shrike'])
        self.assertEqual(S.SCRIPTS, ['ShrikeAI.cs', 'StaffAI.cs', 'TurretAI.cs'])
        self.assertIn('Shrike', S.DOCS)
        self.assertGreater(len(S.DOCS['Shrike']), 100)
        self.assertIn('3 magazines', S.DOCS['Shrike'])          # the ammunition limitation is stated in the README sentence
        self.assertEqual(S._script('NoSuchScript.cs'), '')      # the helper falls back to an empty program

    def test_names_and_registration(self):
        import designs
        self.assertIn('shrike', designs.DRONE_MODULES)
        for lite, name in ((False, 'Shrike'), (True, 'Shrike Lite')):
            self.assertEqual(ship(lite).name, name)

    def test_both_variants_validate_and_reload(self):
        import tempfile
        for lite in (False, True):
            bp = ship(lite)
            bp.validate()
            with tempfile.TemporaryDirectory() as d:
                bp.write(d, bp.name)
                grids = sebp.load_sbc(os.path.join(d, 'bp.sbc'), allow_dlc=False)
                self.assertEqual(len(grids), 1 if lite else 3)          # Full: + the top and head grids of the rotor turret
                self.assertEqual(len(grids[0].blocks), len(bp.blocks))
                for g in grids: g.validate()


class RuleTests(unittest.TestCase):
    def test_no_dlc_no_prototech(self):
        for lite in (False, True):
            bp = ship(lite)
            self.assertEqual(bp.dlc_used(), [])
            for b in bp.blocks:
                self.assertEqual(sebp.DEFS[b['key']]['dlc'], [], b['sub'])
                self.assertNotIn('Prototech', b['sub'])
                self.assertEqual(C._stat(b).get('dlc') or [], [], b['sub'])

    def test_lite_has_only_seen_shapes(self):
        lite = ship(True).shape_summary()
        self.assertTrue(lite)
        for k, (n, conf) in lite.items():
            self.assertTrue(conf.startswith('seen:'), f'{k}: {conf}')
        full = ship(False).shape_summary()
        unseen = {k for k, (n, conf) in full.items() if not conf.startswith('seen:')}
        self.assertEqual(unseen, {'SensorBlock/SmallBlockSensor', 'OffensiveCombatBlock/SmallOffensiveCombat',
                                  'FlightMovementBlock/SmallFlightMovement', 'Decoy/SmallDecoy',
                                  'MotorAdvancedStator/SmallAdvancedStatorSmall', 'MotorAdvancedStator/SmallHinge', 'MotorAdvancedRotor/SmallAdvancedRotorSmall'})      # (no sample has a small-grid stator)

    def test_lite_has_no_ai_sensor_decoy_or_unverified_toolbar(self):
        bp = ship(True)
        for b in bp.blocks:
            self.assertNotIn(b['xtype'], ('MyObjectBuilder_SensorBlock', 'MyObjectBuilder_OffensiveCombatBlock',
                                          'MyObjectBuilder_FlightMovementBlock', 'MyObjectBuilder_Decoy'))
        self.assertEqual([n for n in bp.notes if 'UNVERIFIED' in n], [])
        self.assertTrue([n for n in ship(False).notes if 'UNVERIFIED' in n])      # Full: the PB Run slots are flagged

    def test_one_pb_and_the_controllers(self):
        for lite in (False, True):
            bp = ship(lite)
            pbs = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_MyProgrammableBlock']
            self.assertEqual(sorted(b['name'] for b in pbs), ['[Helm] Shrike'] if lite else ['[Helm] Shrike', '[TU] Turret PB'])
            self.assertEqual(len([b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_RemoteControl']), 1)
            cockpits = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Cockpit']
            self.assertEqual([c['sub'] for c in cockpits], ['DBSmallBlockFighterCockpit'])
            self.assertIn('[Helm Seat]', cockpits[0]['name'])
            self.assertGreaterEqual(len([b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Gyro']), 2)
            self.assertEqual(len([b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_CameraBlock']), 7)
            self.assertEqual(len([b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_RadioAntenna']), 1)

    def test_cameras_and_thrusters_face_the_way_their_names_say(self):
        bp = ship(False)
        for nm in ('Front', 'Rear', 'Left', 'Right', 'Up', 'Down'):
            cam = named(bp, 'Camera ' + nm)[0]
            self.assertEqual(cam['fwd'], {'Front': 'Forward', 'Rear': 'Backward'}.get(nm, nm), nm)
        exhaust = {'Main': 'Backward', 'Retro': 'Forward', 'Dive': 'Up', 'Lift': 'Down'}
        for nm, d in exhaust.items():
            for t in named(bp, 'Thrust ' + nm):
                self.assertEqual(t['fwd'], d, t['name'])
        self.assertEqual(named(bp, 'Thrust Strafe R')[0]['fwd'], 'Right')
        self.assertEqual(named(bp, 'Thrust Strafe L')[0]['fwd'], 'Left')
        for b in bp.blocks:
            if b['kind'] == 'gun':
                self.assertEqual(b['fwd'], 'Forward', b['name'])       # every gun is fixed forward, parallel to the nose

    def test_every_functional_block_is_tagged_for_the_helm(self):
        bp = ship(False)
        for b in bp.blocks:
            if b['xtype'].endswith('_CubeBlock'):
                continue
            self.assertTrue(b['name'], b['sub'])
            self.assertTrue(b['name'].startswith(('[SH] ', '[Helm] ', '[TU] ')), b['name'])
            if b['name'].startswith('[TU] '): continue                  # the rotor turret and its PB: not adopted by the Helm (it adopts [SH] blocks only)

    def test_script_is_embedded_when_it_exists(self):
        bp = ship(False)
        pb = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_MyProgrammableBlock'][0]
        if os.path.exists(os.path.join(C.ROOT, 'scripts', 'ShrikeAI.cs')):
            self.assertIn('<Program>', pb['extra'])
        else:
            self.assertEqual(pb['extra'], '')            # empty program until the script exists

    def test_hull_is_mirror_symmetric_block_for_block(self):
        bp = ship(False)
        occ = {c: b for b in bp.blocks for c in bp.cells(b)}
        for (x, y, z), b in occ.items():
            if x <= 0 or not b['xtype'].endswith('_CubeBlock'):
                continue
            m = occ.get((-x, y, z))
            self.assertIsNotNone(m, (x, y, z))
            self.assertEqual(m['sub'], b['sub'], (x, y, z))
            a1 = H.orient_areas(b['sub'], 'Small')[(b['fwd'], b['up'])]
            a2 = H.orient_areas(m['sub'], 'Small')[(m['fwd'], m['up'])]
            for d in H.FACES:
                self.assertAlmostEqual(a1.get(d, 0), a2.get((-d[0], d[1], d[2]), 0), places=3, msg=str((x, y, z)))


class NumberTests(unittest.TestCase):
    def test_size(self):
        w, h, l = ship(False).stats()['dims']
        self.assertTrue(14 <= l <= 18, l)
        self.assertLessEqual(w, 16)

    def test_acceleration_against_the_catalogue(self):
        """catalogue 4.2: forward ~16 m/s2, the other axes ~8-9 (within 20 %); loaded = with magazines and uranium"""
        for lite in (False, True):
            f = S.figures(ship(lite))
            self.assertGreaterEqual(f['acc_loaded']['Forward'], 16 * 0.8)
            self.assertLessEqual(f['acc_loaded']['Forward'], 16 * 1.2)
            for d in ('Up', 'Down', 'Left', 'Right'):
                self.assertGreaterEqual(f['acc_loaded'][d], 8 * 0.8, d)
                self.assertLessEqual(f['acc_loaded'][d], 9 * 1.2, d)
            self.assertGreaterEqual(f['acc_loaded']['Backward'], 8 * 0.8)

    def test_thrust_is_hydrogen_and_covers_six_axes(self):
        bp = ship(False)
        thr = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Thrust']
        self.assertEqual(len(thr), 8)
        self.assertEqual({b['sub'] for b in thr}, {'SmallBlockLargeHydrogenThrust'})
        self.assertTrue(all(v > 0 for v in C.thrust_by_dir(bp).values()))

    def test_thrust_line_passes_close_to_the_centre_of_mass(self):
        """every axis group's parasitic torque is below 15 % of the six gyros' torque (calc: sum of r x F about the loaded COM)"""
        bp = ship(False)
        f = S.figures(bp)
        com = [v * 0.5 for v in f['com']]
        DIRV = C.DIR
        groups = {}
        for b in bp.blocks:
            if b['xtype'] != 'MyObjectBuilder_Thrust':
                continue
            ex = DIRV[b['fwd']]
            ctr = [sum(c[k] + 0.5 for c in bp.cells(b)) / len(bp.cells(b)) * 0.5 for k in range(3)]
            groups.setdefault(b['fwd'], []).append(([-v * 480000.0 for v in ex], ctr))
        for d, lst in groups.items():
            tau = [0.0, 0.0, 0.0]
            for F, c in lst:
                r = [c[k] - com[k] for k in range(3)]
                t = sebp.cross(r, F)
                tau = [tau[k] + t[k] for k in range(3)]
            mag = sum(v * v for v in tau) ** 0.5
            self.assertLess(mag, 0.15 * 6 * 448000, (d, mag))

    def test_weapons_and_their_ammunition(self):
        bp = ship(False)
        w = [b for b in bp.blocks if b['kind'] == 'gun']
        self.assertEqual(sorted(b['sub'] for b in w), ['', '', 'SmallBlockAutocannon', 'SmallBlockAutocannon', 'SmallBlockMediumCalibreGun'])
        for b in w:
            ammo = [i for i in bp.items_of(b) if i.kind == 'AmmoMagazine']
            self.assertEqual(len(ammo), 1, b['name'])
            self.assertEqual(bp.max_items(b, 'AmmoMagazine', ammo[0].sub), 0, b['name'] + ' is not full')    # every weapon holds its full capacity
            self.assertTrue(b['name'].startswith('[SH] [B1]') or b['name'].startswith('[SH] [B2]'))
        self.assertEqual(set(bp.groups), {'[SH] Thrusters', '[SH] Weapons', '[B1]', '[B2]', '[SH] Gyros'})
        self.assertEqual((len(bp.groups['[B1]']), len(bp.groups['[B2]'])), (4, 1))

    def test_fuel_charge_and_uranium(self):
        bp = ship(False)
        tanks = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_OxygenTank']
        self.assertEqual(len(tanks), 3)
        self.assertTrue(all(b['state'].get('filled') == 'full' for b in tanks))
        bats = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_BatteryBlock']
        self.assertEqual(len(bats), 2)
        self.assertTrue(all(b['state'].get('stored_power') == 'full' for b in bats))
        for r in [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Reactor']:
            self.assertGreater(sum(i.amount for i in bp.items_of(r)), 0)

    def test_every_thruster_touches_a_hydrogen_tank(self):
        bp = ship(False)
        adj = H.fast_attachment(bp)
        by = {id(b): b for b in bp.blocks}
        for t in [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Thrust']:
            self.assertTrue(any(by[i]['xtype'] == 'MyObjectBuilder_OxygenTank' for i in adj[id(t)]), t['name'])

    def test_timer_and_cockpit_toolbars(self):
        bp = ship(False)
        timer = named(bp, 'Timer D')[0]
        self.assertEqual(sorted(i.group for i in timer['toolbars']['Toolbar'].values()), ['[SH] Thrusters', '[SH] Weapons'])
        cockpit = named(bp, 'Cockpit [Helm Seat]')[0]
        slots = cockpit['toolbars']['Toolbar']
        self.assertEqual([slots[i].sub for i in (0, 1, 2)], ['SmallBlockMediumCalibreGun', 'SmallBlockAutocannon', ''])
        lite_slots = named(ship(True), 'Cockpit [Helm Seat]')[0]['toolbars']['Toolbar']
        self.assertEqual(sorted(lite_slots), [0, 1, 2, 3])


if __name__ == '__main__':
    unittest.main()
