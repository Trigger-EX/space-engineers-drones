"""Tests for the Aegis Carrier mega carrier (tools/designs/aegis_carrier.py) with its six docked cradle pods (tools/designs/pods.py).

    python3 -I tools/tests/test_aegis_carrier.py          (or through tools/tests/run_all.py)

Pure python, about 2 minutes (one ship build, one with 24 drones).  What is pinned: the rules of the brief (no DLC, no Prototech, the model is NOT the Aegis escort drone), the
naming the scripts rely on, the pre-loads, the figures of docs/design/ships/Aegis_Carrier.md, thrust equal on all six axes and balanced about the centre of mass, the four
per-pod conveyor networks, and the pod geometry (all six pods docked on both connector pairs to 2 mm, the two bay pods upright down their lane, the four well pods nose up with
the launch lane empty, 24 Hornet-class drones fit).
"""
import os, re, sys, unittest, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.normpath(os.path.join(TOOLS, '..'))
for p in (os.path.join(TOOLS, 'designs'), TOOLS):
    if p not in sys.path: sys.path.insert(0, p)
import sebp                                    # noqa: E402
import sebp_multi as MU                        # noqa: E402
import pods                                    # noqa: E402
import leviathan as L                          # noqa: E402
import aegis_carrier as A                      # noqa: E402

_CACHE = {}
def ship(**kw):
    k = tuple(sorted(kw.items()))
    if k not in _CACHE: _CACHE[k] = A.aegis_carrier(**kw)
    return _CACHE[k]

def blocks_all(bp):
    return [b for g, _ in bp.grids() for b in g.blocks]

class RuleTests(unittest.TestCase):
    def test_names_and_models(self):
        bp = ship()
        self.assertEqual(bp.name, 'Aegis Carrier')
        self.assertEqual(list(A.MODELS), ['Aegis Carrier'])
        self.assertNotEqual(bp.name, 'Aegis')                     # the Aegis escort drone (extra.py) keeps its own name, tag and script
        self.assertEqual(A.TAG, 'AEGISC')
        self.assertNotEqual(A.TAG, 'AEGIS')
        self.assertGreater(len(A.DOCS['Aegis Carrier']), 200)
        self.assertIn('Aegis Carrier', A.MODEL_NOTES)
        self.assertEqual(set(A.SCRIPTS), {'ShrikeAI.cs', 'StaffAI.cs', 'CradleBayAI.cs'})
        with self.assertRaises(AssertionError): A.aegis_carrier(lite=True)          # the Lite variants are retired

    def test_no_dlc_no_prototech(self):
        bp = ship()
        for b in blocks_all(bp):
            d = sebp.DEFS[b['key']]
            self.assertEqual(d['dlc'], [], b['key'])
            self.assertNotIn('Prototech', b['key'])
        self.assertEqual(bp.dlc_used(), [])
        for g, _ in bp.grids(): self.assertEqual(g.dlc_used(), [])
        pods.assert_clean(bp)

    def test_validates_every_grid(self):
        ship().validate_all()

    def test_hull_mount_faces(self):
        ship().hull.check()

    def test_symmetric(self):
        occ = ship().occupancy()
        self.assertEqual([c for c in occ if occ[c]['kind'] != 'conv' and (-c[0], c[1], c[2]) not in occ], [])

    def test_size_in_blocks(self):
        bp = ship()
        self.assertTrue(9_000 <= len(bp.blocks) <= 14_000, len(bp.blocks))

    def test_registered_in_the_design_package(self):
        import designs
        self.assertIn('aegis_carrier', designs.DRONE_MODULES)
        self.assertIn('aegis_carrier', designs.SHIP_MODULES)
        self.assertIn('aegis_carrier_test', designs.TEST_MODULES)

class NamingTests(unittest.TestCase):
    def test_host_blocks_carry_sh(self):
        bp = ship()
        for b in bp.blocks:
            if b['xtype'].endswith('_CubeBlock') and not b['name']: continue
            self.assertIn('[SH]', b['name'] or '', b['sub'])
        for pod in bp.pods_:
            for b in pod.blocks: self.assertNotIn('[SH]', b['name'] or '')

    def test_pbs(self):
        bp = ship()
        pbs = [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]
        self.assertEqual(sorted(b['name'] for b in pbs), ['[Helm][SH] Aegis Helm', '[Staff][SH] Aegis Staff'])
        names = []
        for pod in bp.pods_:
            pp = [b for b in pod.blocks if b['xtype'].endswith('MyProgrammableBlock')]
            self.assertEqual(len(pp), 1)
            names.append(pp[0]['name']); pbs.append(pp[0])
        self.assertEqual(names, [f'[Pod] Pod PB {k}' for k in range(1, 7)])
        for b in pbs: self.assertIn('Tag=AEGISC', b['custom_data'])
        staff = [b for b in pbs if b['name'].startswith('[Staff]')][0]['custom_data']
        for k in range(1, 7):
            self.assertIn(f'[Hangar {k}]', staff)
            self.assertIn(f'Pod=[Pod] Pod PB {k}', staff)
        self.assertIn('Group=[Hangar 1]', staff); self.assertIn('Group=[Hangar 2]', staff)
        self.assertNotIn('Group=[Hangar 3]', staff)                  # the well has no doors
        self.assertIn('Pod Gap (s)=6', staff)

    def test_staff_script_reads_all_six_hangars(self):
        src = open(os.path.join(ROOT, 'scripts', 'StaffAI.cs'), encoding='utf-8').read()
        m = re.search(r'for \(int n = 1; n <= (\d+); n\+\+\) if \(_ini\.ContainsSection\("Hangar "', src)
        self.assertIsNotNone(m)
        self.assertGreaterEqual(int(m.group(1)), 6)

    def test_pod_tags_and_unique_names(self):
        bp = ship()
        for g, _ in bp.grids():
            names = [b['name'] for b in g.blocks if b['name']]
            self.assertEqual(len(names), len(set(names)), g.name)
        tags = []
        for pod in bp.pods_:
            t = {b['name'].split(']')[0] + ']' for b in pod.blocks if b['name'] and not b['name'].startswith('[Pod]')}
            self.assertEqual(len(t), 1)
            tags.append(t.pop())
        self.assertEqual(tags, [f'[POD{k}]' for k in range(1, 7)])

    def test_first_bays_are_global(self):
        bp = ship()
        for k, pod in enumerate(bp.pods_):
            self.assertIn(f'First Bay={1 + 4 * k}', [b for b in pod.blocks if b['xtype'].endswith('MyProgrammableBlock')][0]['custom_data'])

    def test_battery_and_pd_tags(self):
        bp = ship()
        self.assertEqual(sorted(b['sub'] for b in bp.blocks if '[B1]' in (b['name'] or '')), ['LargeRailgun'] * 2)
        pd = [b for b in bp.blocks if '[PD]' in (b['name'] or '')]
        self.assertEqual(len(pd), 16)
        self.assertTrue(all(b['xtype'].endswith('LargeGatlingTurret') for b in pd))
        for b in pd: self.assertIn('TargetLargeGrids', b['settings']['Flags'])
        for b in bp.blocks:
            if b['xtype'].endswith('LargeMissileTurret'): self.assertNotIn('[B', b['name'])

    def test_groups(self):
        sizes = {k: len(v) for k, v in ship().groups.items()}
        self.assertEqual(sizes, {'B1': 2, 'Artillery': 4, 'Assault': 4, 'PD': 16, 'Main Turrets': 8, 'Turrets': 24, 'Thrusters': 24, 'Weapons': 26, 'Decoys': 12,
                                 '[Hangar 1]': 8, '[Hangar 2]': 8, 'Gyros': 144, 'Reactors': 5, 'Batteries': 12, 'Tanks': 4})

    def test_hangar_groups_are_the_doors_of_their_side(self):
        bp = ship()
        self.assertTrue(all(b['min'][0] == -A.X_DOOR for b in bp.groups['[Hangar 1]']))      # port
        self.assertTrue(all(b['min'][0] == A.X_DOOR for b in bp.groups['[Hangar 2]']))       # starboard

class LoadoutTests(unittest.TestCase):
    def test_counts(self):
        bp = ship()
        n = A.numbers(bp)
        self.assertEqual(n['weapons'], dict(railguns=2, artillery=4, assault=4, gatling=16, rocket=0, decoys=12))
        cnt = lambda sub: sum(1 for b in bp.blocks if b['sub'] == sub)
        self.assertEqual(cnt('LargeBlockLargeGenerator'), 1)
        self.assertEqual(cnt('LargeBlockSmallGenerator'), 4)
        self.assertEqual(cnt('LargeBlockBatteryBlock'), 12)
        self.assertEqual(cnt('LargeBlockGyro'), 144)
        self.assertEqual(cnt('LargeHydrogenTank'), 4)
        self.assertEqual(cnt('LargeBlockLargeHydrogenThrust'), 24)
        self.assertEqual(cnt('LargeBlockLargeContainer'), 5)                       # one per engine pod + the bow locker
        self.assertEqual(sum(1 for b in bp.blocks if b['xtype'].endswith('AirtightHangarDoor')), 16)
        self.assertEqual(cnt('LargeBlockCockpitSeat'), 2)
        self.assertEqual(cnt('LargeCameraBlock'), 8)                              # Front / Rear P and S, Port, Starboard, Top, Belly
        self.assertEqual(cnt('LargeBlockRemoteControl'), 1)
        self.assertEqual(len(bp.pods_), 6)

    def test_everything_is_preloaded(self):
        bp = ship()
        for b in bp.blocks:
            if b['xtype'].endswith(('LargeGatlingTurret', 'LargeMissileTurret')) or b['sub'] == 'LargeRailgun':
                mags = [it for it in bp.items_of(b) if it.kind == 'AmmoMagazine']
                self.assertEqual(len(mags), 1, b['name'])
                self.assertEqual(bp.max_items(b, 'AmmoMagazine', mags[0].sub), 0, b['name'])
            if b['sub'] == 'LargeRailgun': self.assertEqual(b['state'].get('capacitor'), 'full')
            if b['sub'] == 'LargeBlockSmallGenerator': self.assertEqual([(i.sub, i.amount) for i in bp.items_of(b)], [('Uranium', 100.0)])
            if b['sub'] == 'LargeBlockLargeGenerator': self.assertEqual([(i.sub, i.amount) for i in bp.items_of(b)], [('Uranium', 500.0)])
            if b['sub'] == 'LargeHydrogenTank': self.assertEqual(b['state'].get('filled'), 'full')
            if b['sub'] == 'LargeBlockBatteryBlock': self.assertIn('<CurrentStoredPower>3</CurrentStoredPower>', b['extra'])
        lockers = [b for b in bp.blocks if b['sub'] == 'LargeBlockLargeContainer']
        subs = [{i.sub for i in bp.items_of(b)} for b in lockers]
        self.assertEqual(sum(1 for s in subs if s == {'LargeCalibreAmmo', 'MediumCalibreAmmo'}), 4)
        self.assertEqual(sum(1 for s in subs if s == {'LargeRailgunAmmo', 'NATO_25x184mm'}), 1)

    def test_figures(self):
        """the figures of docs/design/ships/Aegis_Carrier.md"""
        n = A.numbers(ship())
        self.assertTrue(12_000 <= n['mass_t_total'] <= 19_000, n['mass_t_total'])
        for d, a in n['accel_loaded_m_s2'].items(): self.assertTrue(1.6 <= a <= 2.2, (d, a))
        self.assertTrue(35_000 <= n['pcu_total'] <= 45_000, n['pcu_total'])
        self.assertTrue(26_000 <= n['pcu_ship'] <= 34_000, n['pcu_ship'])
        self.assertGreaterEqual(n['power']['generation_MW'], 1.5 * n['power']['railgun_recharge_MW'])
        self.assertTrue(140 <= n['dims_m'][0] <= 175 and 140 <= n['dims_m'][2] <= 175, n['dims_m'])
        self.assertTrue(n['hp_M'] >= 50, n['hp_M'])
        self.assertGreater(n['angular_accel_rad_s2'][0], 0.04)

    def test_thrust_is_equal_on_the_six_axes_and_balanced(self):
        n = A.numbers(ship())
        self.assertEqual(set(n['thrust_MN'].values()), {28.8})
        self.assertEqual(set(n['thrusters_per_direction'].values()), {4})
        for d, pct in n['thrust_torque_pct_of_gyros'].items(): self.assertLess(pct, 10.0, d)

    def test_firing_lanes_and_exhaust_are_clear(self):
        bp = ship()
        occ = bp.occupancy()
        for b in bp.blocks:
            if b['sub'] == 'LargeRailgun':
                cs = bp.cells(b)
                z0 = min(c[2] for c in cs)
                for x in {c[0] for c in cs}:
                    for y in {c[1] for c in cs}:
                        for z in range(z0 - 3, z0): self.assertNotIn((x, y, z), occ, b['name'])
            elif b['xtype'] == 'MyObjectBuilder_Thrust':
                d = L.THRUST_DIR[b['fwd']]
                for c in bp.cells(b):
                    for k in range(1, sebp.LARGE_H2_LANE + 1):
                        o = occ.get((c[0] + d[0] * k, c[1] + d[1] * k, c[2] + d[2] * k))
                        self.assertTrue(o is None or o is b, (b['name'], k))

    def test_pcu_table_adds_up(self):
        bp = ship()
        rows = A.pcu_table(bp)
        self.assertEqual(sum(p for c, k, p in rows), A.numbers(bp)['pcu_ship'])
        self.assertEqual(sum(k for c, k, p in rows), len(bp.blocks))

class ConveyorTests(unittest.TestCase):
    def _components(self, bp):
        cells = {b['min'] for b in bp.ship.conveyors}
        seen, comps = set(), []
        for start in sorted(cells):
            if start in seen: continue
            comp = {start}; todo = [start]
            while todo:
                c = todo.pop()
                for d in sebp.DIR.values():
                    n = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
                    if n in cells and n not in comp: comp.add(n); todo.append(n)
            seen |= comp; comps.append(comp)
        return comps

    def test_four_networks_one_per_pod(self):
        bp = ship()
        comps = self._components(bp)
        self.assertEqual(len(comps), 4)
        terms = L._terminals(bp.ship)
        for comp in comps:
            mine = [(c, r, n) for c, r, n in terms if c in comp]
            roles = {}
            for c, r, n in mine: roles[r] = roles.get(r, 0) + 1
            self.assertEqual(roles, {'thruster': 6, 'turret': 2, 'tank': 1, 'locker': 1})
            self.assertEqual(len({A._pod_of(c) for c, r, n in mine}), 1)           # a network never leaves its pod
        self.assertEqual(sum(len(c) for c in comps), len(bp.ship.conveyors))
        for c, r, n in terms: self.assertIn(c, {x for comp in comps for x in comp}, (r, n))
        self.assertEqual(len(terms), 40)
        for b in bp.ship.conveyors:
            self.assertEqual((b['xtype'], b['sub']), ('MyObjectBuilder_Conveyor', 'LargeBlockConveyor'))
            self.assertIn('[SH]', b['name'])
        names = [b['name'] for b in bp.ship.conveyors]
        self.assertEqual(len(names), len(set(names)))

    def test_junctions_keep_off_the_connectors(self):
        bp = ship()
        cells = {b['min'] for b in bp.ship.conveyors}
        for s in bp.ship.conns:
            for k in bp.ship.conns[s]:
                c = k['min']
                for d in sebp.DIR.values(): self.assertNotIn((c[0] + d[0], c[1] + d[1], c[2] + d[2]), cells)

class PodTests(unittest.TestCase):
    def _hosts(self, bp):
        return [bp.ship.conns[-1], bp.ship.conns[1]] + [bp.ship.conns[f'W{j}'] for j in range(1, 5)]

    def test_pods_dock_on_both_pairs(self):
        bp = ship()
        bp.grids()
        for pod, hosts in zip(bp.pods_, self._hosts(bp)):
            used = set()
            for p_ in pod.pod.connectors:
                b, nb = MU.contact_point(pod, p_, 'Forward')
                b = pod._world.apply(b); nb = pod._world.rot.apply(nb)
                best = None
                for h in hosts:
                    a, na = MU.contact_point(bp, h, 'Forward')
                    d = [b[i] - a[i] for i in range(3)]
                    if best is None or MU._norm(d) < best[0]: best = (MU._norm(d), h, a, na, d)
                dist, h, a, na, d = best
                used.add(h['name'])
                self.assertLess(MU._norm(MU._add(na, nb)), 1e-9)
                self.assertAlmostEqual(abs(MU._dot(d, na)), pods.PAIR_GAP, delta=0.002)
                lat = MU._sub(d, MU._mul(na, MU._dot(d, na)))
                self.assertLess(MU._norm(lat), 0.002, pod.name)
            self.assertEqual(len(used), 2, pod.name)

    def test_bay_pods_are_upright_down_their_lane_and_well_pods_nose_up(self):
        bp = ship()
        bp.grids()
        for s, pod in zip((-1, 1), bp.pods_[:2]):
            self.assertGreater(MU._dot(pod.pose.rot.apply(MU.DIRS['Up']), (0, 1, 0)), 0.99)
            self.assertGreater(MU._dot(pod.pose.rot.apply(MU.DIRS['Forward']), (s, 0, 0)), 0.99)
        for x, pod in zip(A.WELL_X, bp.pods_[2:]):
            self.assertGreater(MU._dot(pod.pose.rot.apply(MU.DIRS['Forward']), (0, 1, 0)), 0.99)                       # nose up
            self.assertGreater(MU._dot(pod.pose.rot.apply(MU.DIRS['Up']), (1 if x > 0 else -1, 0, 0)), 0.99)          # pod up points away from the well axis

    def test_geometry_checks(self):
        bp = ship()
        for s, pod in zip((-1, 1), bp.pods_[:2]):
            L.check_hangar(bp, pod, s, door_x=(A.X_DOOR + 1) * 2.5, hangar_y=A.BAY_Y, hangar_z=A.BAY_Z)
        for pod in bp.pods_[2:]:
            A.check_well(bp, pod)

    def test_doors_and_connectors(self):
        bp = ship()
        doors = [b for b in bp.blocks if b['xtype'].endswith('AirtightHangarDoor')]
        self.assertEqual(len(doors), 16)
        for s in (-1, 1):
            ds = [b for b in doors if (b['min'][0] > 0) == (s > 0)]
            self.assertEqual({(b['min'][0], b['min'][1]) for b in ds}, {(s * A.X_DOOR, A.BAY_Y[0])})
            self.assertEqual(sorted(b['min'][2] for b in ds), list(range(A.BAY_Z[0], A.BAY_Z[1] + 1)))
        for hosts in self._hosts(bp): self.assertEqual(hosts[1]['min'][2] - hosts[0]['min'][2], 5)
        for pod in bp.pods_: self.assertEqual(pod.pod.conn_spacing, 25)

    def test_wing_with_24_drones_writes_reloads_and_fits(self):
        """four Hornet-class drones in each of the six pods (the 'Aegis Carrier Wing' test ship): the file has 1 + 6 + 24 grids, every drone stays 0.25 m clear of the hull"""
        bp = ship(name='Aegis Carrier Wing', wing=4)
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
            self.assertEqual(xml.count('<ConnectedEntityId>'), 24)                  # 6 pods x 2 pairs x both sides
            self.assertEqual(xml.count('<CubeGrid>'), 1 + 6 + 24)

if __name__ == '__main__':
    unittest.main(verbosity=2)
