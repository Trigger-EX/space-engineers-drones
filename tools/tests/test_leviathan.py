"""Tests for the Leviathan dreadnought (tools/designs/leviathan.py), with its two docked cradle pods (tools/designs/pods.py).

    python3 -I tools/tests/test_leviathan.py          (or through tools/tests/run_all.py)

Pure python, about 60 s (two ship builds, a third with eight drones).  What is pinned: the rules of the brief (no DLC, no Prototech, Lite = sample-derived shapes
only), the naming the scripts rely on, the pre-loads, the catalogue targets (docs/design/systems_catalogue.md section 4.5, within about 20 %), the thrust balance about
the centre of mass, the conveyor network, and the hangar geometry (both pods docked upright on their two connector pairs to 2 mm, inside the hangar, the launch lane
empty, eight Hornet-class drones fit).
"""
import os, sys, unittest, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (os.path.join(TOOLS, 'designs'), TOOLS):
    if p not in sys.path: sys.path.insert(0, p)
import sebp                                    # noqa: E402
import sebp_multi as MU                        # noqa: E402
import _common as C                            # noqa: E402
import pods                                    # noqa: E402
import leviathan as L                          # noqa: E402

_CACHE = {}
def ship(lite=False, **kw):
    k = (lite, tuple(sorted(kw.items())))
    if k not in _CACHE: _CACHE[k] = L.leviathan(lite, **kw)
    return _CACHE[k]

def blocks_all(bp):
    return [b for g, _ in bp.grids() for b in g.blocks]

class RuleTests(unittest.TestCase):
    def test_names_and_models(self):
        self.assertEqual(ship(False).name, 'Leviathan')
        self.assertEqual(ship(True).name, 'Leviathan Lite')
        self.assertEqual(list(L.MODELS), ['Leviathan', 'Leviathan Wing'])
        self.assertIn('Leviathan', L.DOCS)
        self.assertGreater(len(L.DOCS['Leviathan']), 200)
        self.assertEqual(set(L.SCRIPTS), {'ShrikeAI.cs', 'StaffAI.cs', 'CradleBayAI.cs'})
        self.assertIn('Leviathan', L.MODEL_NOTES)

    def test_no_dlc_no_prototech(self):
        for lite in (False, True):
            bp = ship(lite)
            for b in blocks_all(bp):
                d = sebp.DEFS[b['key']]
                self.assertEqual(d['dlc'], [], b['key'])
                self.assertNotIn('Prototech', b['key'])
            self.assertEqual(bp.dlc_used(), [])
            for g, _ in bp.grids(): self.assertEqual(g.dlc_used(), [])
            pods.assert_clean(bp)

    def test_lite_uses_no_unseen_shape(self):
        lite = ship(True)
        for k, (n, conf) in lite.shape_summary().items():
            self.assertNotIn(conf, ('guess', 'from-source'), k)
            self.assertFalse(k.startswith(('SensorBlock', 'OffensiveCombat', 'FlightMovement', 'DefensiveCombat', 'BasicMission')), k)
        # the rocket turret (LargeMissileTurret with the empty subtype) was never saved by the game in any sample: its shape is the assault turret's, so Lite has none
        self.assertEqual(sum(1 for b in lite.blocks if b['xtype'].endswith('LargeMissileTurret') and not b['sub']), 0)
        full = ship(False).shape_summary()
        unseen = {k for k, (n, c) in full.items() if c in ('guess', 'from-source')}
        self.assertEqual(unseen, {'SensorBlock/LargeBlockSensor', 'OffensiveCombatBlock/LargeOffensiveCombat', 'FlightMovementBlock/LargeFlightMovement', 'GravityGenerator/'})

    def test_validates_every_grid(self):
        for lite in (False, True):
            ship(lite).validate_all()

    def test_hull_mount_faces(self):
        for lite in (False, True):
            ship(lite).hull.check()                     # every slope / corner / window still meets the hull with its mount faces

    def test_symmetric(self):
        occ = ship(False).occupancy()
        # everything but the conveyor junctions (the router is free to choose a side)
        self.assertEqual([c for c in occ if occ[c]['kind'] != 'conv' and (-c[0], c[1], c[2]) not in occ], [])

    def test_size_in_blocks(self):
        bp = ship(False)
        self.assertTrue(6_000 <= len(bp.blocks) <= 12_000, len(bp.blocks))

class NamingTests(unittest.TestCase):
    def test_host_blocks_carry_sh(self):
        bp = ship(False)
        for b in bp.blocks:
            if b['xtype'].endswith('_CubeBlock') and not b['name']: continue
            self.assertIn('[SH]', b['name'] or '', b['sub'])
        for pod in bp.pods_:
            for b in pod.blocks:
                self.assertNotIn('[SH]', b['name'] or '')

    def test_pbs(self):
        bp = ship(False)
        pbs = [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]
        self.assertEqual(sorted(b['name'] for b in pbs), ['[Helm][SH] Leviathan Helm', '[Staff][SH] Leviathan Staff'])
        names = []
        for pod in bp.pods_:
            pp = [b for b in pod.blocks if b['xtype'].endswith('MyProgrammableBlock')]
            self.assertEqual(len(pp), 1)
            names.append(pp[0]['name'])
            pbs.append(pp[0])
        self.assertEqual(names, ['[Pod] Pod PB 1', '[Pod] Pod PB 2'])
        for b in pbs:
            self.assertIn('Tag=LEVIATHAN', b['custom_data'])
        staff = [b for b in pbs if b['name'].startswith('[Staff]')][0]
        for n in names: self.assertIn(f'Pod={n}', staff['custom_data'])

    def test_unique_names_per_grid_and_per_pod_tag(self):
        bp = ship(False)
        for g, _ in bp.grids():
            names = [b['name'] for b in g.blocks if b['name']]
            self.assertEqual(len(names), len(set(names)), g.name)
        tags = []
        for pod in bp.pods_:
            t = {b['name'].split(']')[0] + ']' for b in pod.blocks if b['name'] and not b['name'].startswith('[Pod]')}
            self.assertEqual(len(t), 1)
            tags.append(t.pop())
        self.assertEqual(tags, ['[POD1]', '[POD2]'])

    def test_battery_and_pd_tags(self):
        bp = ship(False)
        b1 = [b for b in bp.blocks if '[B1]' in (b['name'] or '')]
        self.assertEqual(sorted(b['sub'] for b in b1), ['LargeRailgun'] * 6)
        pd = [b for b in bp.blocks if '[PD]' in (b['name'] or '')]
        self.assertEqual(len(pd), 20)
        self.assertTrue(all(b['xtype'].endswith('LargeGatlingTurret') for b in pd))
        for b in bp.blocks:                               # turrets other than the gatlings never carry a battery tag
            if b['xtype'].endswith('LargeMissileTurret'): self.assertNotIn('[B', b['name'])

    def test_groups(self):
        bp = ship(False)
        sizes = {k: len(v) for k, v in bp.groups.items()}
        self.assertEqual(sizes, {'B1': 6, 'Artillery': 8, 'Assault': 12, 'Rockets': 4, 'PD': 20, 'Main Turrets': 20, 'Turrets': 44, 'Thrusters': 14, 'Weapons': 50, 'Decoys': 12,
                                 '[Hangar 1]': 8, '[Hangar 2]': 8, 'Gyros': 144, 'Reactors': 9, 'Batteries': 20, 'Tanks': 4})
        lite = {k: len(v) for k, v in ship(True).groups.items()}
        self.assertNotIn('Rockets', lite)
        self.assertEqual(lite['PD'], 24)

    def test_hangar_groups_are_the_doors_of_their_side(self):
        bp = ship(False)
        self.assertTrue(all(b['min'][0] == -L.HW for b in bp.groups['[Hangar 1]']))      # port
        self.assertTrue(all(b['min'][0] == L.HW for b in bp.groups['[Hangar 2]']))       # starboard

class LoadoutTests(unittest.TestCase):
    def test_counts(self):
        bp = ship(False)
        n = L.numbers(bp)
        self.assertEqual(n['weapons'], dict(railguns=6, artillery=8, assault=12, gatling=20, rocket=4, decoys=12))
        cnt = lambda sub: sum(1 for b in bp.blocks if b['sub'] == sub)
        self.assertEqual(cnt('LargeBlockLargeGenerator'), 1)
        self.assertEqual(cnt('LargeBlockSmallGenerator'), 8)
        self.assertEqual(cnt('LargeBlockBatteryBlock'), 20)
        self.assertEqual(cnt('LargeBlockGyro'), 144)
        self.assertEqual(cnt('LargeHydrogenTank'), 4)
        self.assertEqual(cnt('LargeBlockLargeHydrogenThrust'), 14)
        self.assertEqual(cnt('LargeBlockLargeContainer'), 4)
        self.assertEqual(sum(1 for b in bp.blocks if b['xtype'].endswith('AirtightHangarDoor')), 16)
        self.assertEqual(cnt('LargeBlockCockpitSeat'), 2)
        self.assertEqual(cnt('LargeBlockRemoteControl'), 1)
        self.assertEqual(sum(1 for b in bp.blocks if b['xtype'].endswith('_Decoy')), 12)

    def test_everything_is_preloaded(self):
        bp = ship(False)
        for b in bp.blocks:
            if b['xtype'].endswith(('LargeGatlingTurret', 'LargeMissileTurret')) or b['sub'] == 'LargeRailgun':
                mags = [it for it in bp.items_of(b) if it.kind == 'AmmoMagazine']
                self.assertEqual(len(mags), 1, b['name'])
                self.assertEqual(bp.max_items(b, 'AmmoMagazine', mags[0].sub), 0, b['name'])          # the inventory is full
            if b['sub'] == 'LargeRailgun':
                self.assertEqual(b['state'].get('capacitor'), 'full')
            if b['sub'] == 'LargeBlockSmallGenerator':
                self.assertEqual([(i.sub, i.amount) for i in bp.items_of(b)], [('Uranium', 100.0)])
            if b['sub'] == 'LargeBlockLargeGenerator':
                self.assertEqual([(i.sub, i.amount) for i in bp.items_of(b)], [('Uranium', 500.0)])
            if b['sub'] == 'LargeHydrogenTank':
                self.assertEqual(b['state'].get('filled'), 'full')
            if b['sub'] == 'LargeBlockBatteryBlock':
                self.assertIn('<CurrentStoredPower>3</CurrentStoredPower>', b['extra'])
        lockers = [b for b in bp.blocks if b['sub'] == 'LargeBlockLargeContainer']
        for b in lockers:
            self.assertEqual({i.sub for i in bp.items_of(b)}, {'LargeRailgunAmmo', 'LargeCalibreAmmo', 'MediumCalibreAmmo', 'NATO_25x184mm', 'Missile200mm'})
        for lite_locker in [b for b in ship(True).blocks if b['sub'] == 'LargeBlockLargeContainer']:
            self.assertNotIn('Missile200mm', {i.sub for i in ship(True).items_of(lite_locker)})

    def test_catalogue_targets(self):
        """docs/design/systems_catalogue.md 4.5 within about 20 % (the deviations are explained in docs/design/ships/Leviathan.md)"""
        n = L.numbers(ship(False))
        self.assertTrue(10.6 <= n['mass_t_total'] / 1000 <= 15.9, n['mass_t_total'])
        self.assertTrue(1.6 <= n['accel_loaded_m_s2']['Forward'] <= 2.4, n['accel_loaded_m_s2'])
        for d in ('Backward', 'Up', 'Down', 'Left', 'Right'):
            self.assertTrue(0.88 <= n['accel_loaded_m_s2'][d] <= 1.32, (d, n['accel_loaded_m_s2'][d]))
        self.assertTrue(27_700 <= n['pcu_total'] <= 41_500, n['pcu_total'])
        self.assertTrue(55_000 <= n['dps_sustained'] <= 62_000, n['dps_sustained'])
        self.assertGreaterEqual(n['power']['generation_MW'], 1.5 * n['power']['railgun_recharge_MW'])
        self.assertGreater(n['fuel']['full_forward_minutes'], 30)
        x, y, z = n['dims_m']
        self.assertTrue(180 <= z <= 250, n['dims_m'])
        self.assertTrue(n['hp_M'] >= 55, n['hp_M'])

    def test_thrust_balance_about_the_centre_of_mass(self):
        """every thrust direction fired at 100 % turns the ship by less than 10 % of what the gyros can counter"""
        n = L.numbers(ship(False))
        for d, pct in n['thrust_torque_pct_of_gyros'].items():
            self.assertLess(pct, 10.0, d)
        for a in n['angular_accel_rad_s2'][:2]:
            self.assertGreater(a, 0.03)

    def test_firing_lanes_and_exhaust_are_clear(self):
        """no block ahead of a railgun muzzle, none in the first 8 cells of any thruster exhaust"""
        bp = ship(False)
        occ = bp.occupancy()
        for b in bp.blocks:
            if b['sub'] == 'LargeRailgun':
                cs = bp.cells(b)
                z0 = min(c[2] for c in cs)
                for x in {c[0] for c in cs}:
                    for y in {c[1] for c in cs}:
                        for z in range(-4, z0):
                            self.assertNotIn((x, y, z), occ, b['name'])
            elif b['xtype'] == 'MyObjectBuilder_Thrust':
                d = L.THRUST_DIR[b['fwd']]
                for c in bp.cells(b):
                    for k in range(1, 9):
                        o = occ.get((c[0] + d[0] * k, c[1] + d[1] * k, c[2] + d[2] * k))
                        self.assertTrue(o is None or o is b, b['name'])

    def test_lite_differs_only_by_unseen_blocks(self):
        full, lite = ship(False), ship(True)
        a = sorted((b['sub'] or b['xtype']) for b in full.blocks if b['kind'] not in ('conv', 'turret', 'cradle', 'armor', 'plate', 'accent', 'dark', 'glass'))
        b = sorted((b['sub'] or b['xtype']) for b in lite.blocks if b['kind'] not in ('conv', 'turret', 'cradle', 'armor', 'plate', 'accent', 'dark', 'glass'))
        extra = [x for x in a if x not in b or a.count(x) != b.count(x)]
        self.assertEqual(sorted(set(extra)), ['LargeBlockSensor', 'LargeFlightMovement', 'LargeOffensiveCombat', 'MyObjectBuilder_GravityGenerator'])

    def test_pcu_table_adds_up(self):
        bp = ship(False)
        rows = L.pcu_table(bp)
        self.assertEqual(sum(p for c, k, p in rows), L.numbers(bp)['pcu_ship'])
        self.assertEqual(sum(k for c, k, p in rows), len(bp.blocks))

class ConveyorTests(unittest.TestCase):
    def test_one_network_reaches_every_consumer(self):
        bp = ship(False)
        S = bp.ship
        cells = {b['min'] for b in S.conveyors}
        self.assertTrue(150 <= len(cells) <= 500, len(cells))
        start = next(iter(cells)); seen = {start}; todo = [start]
        while todo:                                                 # connected through face adjacency
            c = todo.pop()
            for d in sebp.DIR.values():
                n = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
                if n in cells and n not in seen: seen.add(n); todo.append(n)
        self.assertEqual(seen, cells)
        roles = {}
        for c, r, n in L._terminals(S):
            self.assertIn(c, cells, (r, n))
            roles[r] = roles.get(r, 0) + 1
        self.assertEqual(roles, {'thruster': 14, 'turret': 24, 'tank': 4, 'locker': 4})
        for b in S.conveyors:
            self.assertEqual((b['xtype'], b['sub']), ('MyObjectBuilder_Conveyor', 'LargeBlockConveyor'))
            self.assertIn('[SH]', b['name'])

    def test_lite_network_has_no_rocket_terminals(self):
        roles = {}
        for c, r, n in L._terminals(ship(True).ship): roles[r] = roles.get(r, 0) + 1
        self.assertEqual(roles, {'thruster': 14, 'turret': 20, 'tank': 4, 'locker': 4})

    def test_junctions_keep_off_the_connectors(self):
        bp = ship(False)
        cells = {b['min'] for b in bp.ship.conveyors}
        for s in bp.ship.conns:
            for k in bp.ship.conns[s]:
                c = k['min']
                for d in sebp.DIR.values(): self.assertNotIn((c[0] + d[0], c[1] + d[1], c[2] + d[2]), cells)

    def test_thruster_backs_and_turret_bases_are_junctions(self):
        bp = ship(False)
        cells = {b['min'] for b in bp.ship.conveyors}
        for b in bp.blocks:
            if b['xtype'].endswith('_Thrust'):
                d = sebp.DIR[b['fwd']]
                cs = bp.cells(b)
                lo = [min(c[i] for c in cs) for i in range(3)]; hi = [max(c[i] for c in cs) for i in range(3)]
                mid = [(lo[i] + hi[i]) // 2 for i in range(3)]
                back = tuple(mid[i] - d[i] * ((hi[i] - lo[i]) // 2 + 1) for i in range(3))
                self.assertIn(back, cells, b['name'])

class HangarTests(unittest.TestCase):
    def test_pods_dock_on_both_pairs(self):
        bp = ship(False)
        bp.grids()
        for s, pod in zip((-1, 1), bp.pods_):
            used = set()
            for p_ in pod.pod.connectors:
                b, nb = MU.contact_point(pod, p_, 'Forward')
                b = pod._world.apply(b); nb = pod._world.rot.apply(nb)
                best = None
                for h in bp.ship.conns[s]:
                    a, na = MU.contact_point(bp, h, 'Forward')
                    d = [b[i] - a[i] for i in range(3)]
                    if best is None or MU._norm(d) < best[0]: best = (MU._norm(d), h, a, na, d)
                dist, h, a, na, d = best
                used.add(h['name'])
                self.assertLess(MU._norm(MU._add(na, nb)), 1e-9)                                  # opposed normals
                self.assertAlmostEqual(abs(MU._dot(d, na)), pods.PAIR_GAP, delta=0.002)           # the lock gap
                lat = MU._sub(d, MU._mul(na, MU._dot(d, na)))
                self.assertLess(MU._norm(lat), 0.002)                                              # lateral error under 2 mm
            self.assertEqual(len(used), 2)                                                         # one pod connector on each host connector

    def test_pods_are_upright_and_look_down_their_lane(self):
        bp = ship(False)
        bp.grids()
        for s, pod in zip((-1, 1), bp.pods_):
            self.assertGreater(MU._dot(pod.pose.rot.apply(MU.DIRS['Up']), (0, 1, 0)), 0.99)
            self.assertGreater(MU._dot(pod.pose.rot.apply(MU.DIRS['Forward']), (s, 0, 0)), 0.99)

    def test_hangar_checks(self):
        for lite in (False, True):
            bp = ship(lite)
            for s, pod in zip((-1, 1), bp.pods_):
                L.check_hangar(bp, pod, s)

    def test_doors_and_connectors(self):
        bp = ship(False)
        doors = [b for b in bp.blocks if b['xtype'].endswith('AirtightHangarDoor')]
        self.assertEqual(len(doors), 16)
        for s in (-1, 1):
            ds = [b for b in doors if (b['min'][0] > 0) == (s > 0)]
            self.assertEqual({(b['min'][0], b['min'][1]) for b in ds}, {(s * L.HW, 1)})
            self.assertEqual(sorted(b['min'][2] for b in ds), list(range(41, 49)))
            conns = bp.ship.conns[s]
            self.assertEqual(conns[1]['min'][2] - conns[0]['min'][2], 5)          # 5 large cells = 25 small cells = the pod's connector spacing
        for pod in bp.pods_: self.assertEqual(pod.pod.conn_spacing, 25)

    def test_locked_variant_with_eight_drones_writes_reloads_and_fits(self):
        """one Lite build, connectors locked, four Hornet-class drones in each pod: the file reloads with 2 + 16 sub-grids, every drone stays 0.25 m clear of the hull,
        and the unlocked default writes no connector link"""
        import core, extra, wasp, support
        names = {}
        for mod in (core, extra, wasp, support):
            for nm, fn in mod.MODELS.items(): names[nm] = fn
        bp = L.leviathan(True, lock=True)
        for pod in bp.pods_:
            for k, nm in enumerate(['Hornet', 'Wasp', 'Harrier', 'Lancer']):
                pods.attach_drone(pod, k, names[nm](True))
        bp.validate_all()
        bp.grids()
        ship_boxes = L._world_boxes(bp)
        for pod in bp.pods_:
            self.assertEqual(len(pod.subgrids), 4)
            for dr, pose in pod.subgrids:
                world = dr._world
                for b in dr.blocks:
                    for c in dr.cells(b):
                        ctr = world.apply(tuple(v * 0.5 for v in c))
                        lo = tuple(ctr[i] - 0.25 for i in range(3)); hi = tuple(ctr[i] + 0.25 for i in range(3))
                        for slo, shi, sb in ship_boxes:
                            gap = max(max(slo[i] - hi[i], lo[i] - shi[i], 0.0) for i in range(3))
                            self.assertGreaterEqual(gap, 0.25, (dr.name, b['name'] or b['sub'], sb['name'] or sb['sub']))
        with tempfile.TemporaryDirectory() as d:
            bp.write(d)
            xml = open(os.path.join(d, 'bp.sbc'), encoding='utf-8').read()
            self.assertEqual(xml.count('<ConnectedEntityId>'), 8)                    # 2 pods x 2 pairs x both sides
            self.assertEqual(xml.count('<CubeGrid>'), 3 + 8)
        with tempfile.TemporaryDirectory() as d:
            ship(True).write(d)
            self.assertEqual(open(os.path.join(d, 'bp.sbc'), encoding='utf-8').read().count('<ConnectedEntityId>'), 0)      # unlocked by default (the Tech Hunter form)

class FamilyTests(unittest.TestCase):
    def test_registered_in_the_design_package(self):
        import designs
        self.assertIn('leviathan', designs.DRONE_MODULES)

if __name__ == '__main__':
    unittest.main(verbosity=2)
