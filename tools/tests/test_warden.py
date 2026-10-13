"""Tests for the Warden frigate (tools/designs/warden.py) and the cradle pod (tools/designs/pods.py).

    python3 -I tools/tests/test_warden.py          (or through tools/tests/run_all.py)

Pure python, about 15 s.  What is pinned: the rules of the brief (no DLC, no Prototech, Lite = sample-derived shapes only), the naming the scripts rely
on, the pre-loads, the catalogue targets (docs/design/systems_catalogue.md section 4.4), and the pod geometry (merge pairs one cell apart, the two connector
pairs meeting to 2 mm, the pod inside the hangar, the launch lane empty).
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
import warden as W                             # noqa: E402

_CACHE = {}
def ship(lite=False, **kw):
    k = (lite, tuple(sorted(kw.items())))
    if k not in _CACHE: _CACHE[k] = W.warden(lite, **kw)
    return _CACHE[k]

def blocks_all(bp):
    return [b for g, _ in bp.grids() for b in g.blocks]

class RuleTests(unittest.TestCase):
    def test_names_and_models(self):
        self.assertEqual(ship(False).name, 'Warden')
        self.assertEqual(ship(True).name, 'Warden Lite')
        self.assertEqual(pods.MODELS['Cradle Pod'](False).name, 'Cradle Pod')
        self.assertEqual(pods.MODELS['Cradle Pod'](True).name, 'Cradle Pod Lite')
        self.assertIn('Warden', W.DOCS); self.assertIn('Cradle Pod', pods.DOCS)
        self.assertTrue(set(W.SCRIPTS) >= {'HelmAI.cs', 'StaffAI.cs', 'CradleBayAI.cs'})

    def test_no_dlc_no_prototech(self):
        for lite in (False, True):
            bp = ship(lite)
            for b in blocks_all(bp):
                d = sebp.DEFS[b['key']]
                self.assertEqual(d['dlc'], [], b['key'])
                self.assertNotIn('Prototech', b['key'])
            self.assertEqual(bp.dlc_used(), [])
            for g, _ in bp.grids(): self.assertEqual(g.dlc_used(), [])

    def test_lite_uses_no_unseen_shape(self):
        lite = ship(True)
        for k, (n, conf) in lite.shape_summary().items():
            self.assertNotIn(conf, ('guess', 'from-source'), k)
            self.assertFalse(k.startswith(('SensorBlock', 'OffensiveCombat', 'FlightMovement', 'DefensiveCombat', 'BasicMission')), k)
        full = ship(False).shape_summary()
        unseen = {k for k, (n, c) in full.items() if c in ('guess', 'from-source')}
        self.assertEqual(unseen, {'SensorBlock/LargeBlockSensor', 'OffensiveCombatBlock/LargeOffensiveCombat', 'FlightMovementBlock/LargeFlightMovement'})

    def test_validates_every_grid(self):
        for lite in (False, True):
            ship(lite).validate_all()

    def test_symmetric(self):
        occ = ship(False).occupancy()
        self.assertEqual([c for c in occ if (-c[0], c[1], c[2]) not in occ], [])

class NamingTests(unittest.TestCase):
    def test_host_blocks_carry_sh(self):
        bp = ship(False)
        for b in bp.blocks:
            if b['xtype'].endswith('_CubeBlock') and not b['name']: continue
            self.assertIn('[SH]', b['name'] or '', b['sub'])
        for b in bp.pod.blocks:
            self.assertNotIn('[SH]', b['name'] or '')

    def test_pbs(self):
        bp = ship(False)
        pbs = [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]
        self.assertEqual(sorted(b['name'] for b in pbs), ['[Helm][SH] Warden Helm', '[Staff][SH] Warden Staff'])
        pp = [b for b in bp.pod.blocks if b['xtype'].endswith('MyProgrammableBlock')]
        self.assertEqual([b['name'] for b in pp], ['[Pod] Pod PB'])
        for b in pbs + pp:
            self.assertIn('Tag=WARDEN', b['custom_data'])

    def test_unique_names_per_grid(self):
        for g, _ in ship(False).grids():
            names = [b['name'] for b in g.blocks if b['name']]
            self.assertEqual(len(names), len(set(names)), g.name)

    def test_battery_and_pd_tags(self):
        bp = ship(False)
        b1 = [b for b in bp.blocks if '[B1]' in (b['name'] or '')]
        self.assertEqual(sorted(b['sub'] for b in b1), ['LargeRailgun', 'LargeRailgun'])
        pd = [b for b in bp.blocks if '[PD]' in (b['name'] or '')]
        self.assertEqual(len(pd), 8)
        self.assertTrue(all(b['xtype'].endswith('LargeGatlingTurret') for b in pd))
        # turrets other than the gatlings never carry a battery tag
        for b in bp.blocks:
            if b['xtype'].endswith('LargeMissileTurret'): self.assertNotIn('[B', b['name'])

    def test_groups(self):
        bp = ship(False)
        self.assertEqual(len(bp.groups['[Hangar 1]']), 8)
        self.assertEqual(len(bp.groups['B1']), 2)
        self.assertEqual(len(bp.groups['PD']), 8)
        self.assertEqual(len(bp.groups['Thrusters']), 24)

class LoadoutTests(unittest.TestCase):
    def test_counts(self):
        n = W.numbers(ship(False))
        self.assertEqual(n['weapons'], dict(railguns=2, artillery=2, assault=4, gatling=8, decoys=6))
        bp = ship(False)
        self.assertEqual(sum(1 for b in bp.blocks if b['sub'] == 'LargeBlockSmallGenerator'), 6)
        self.assertEqual(sum(1 for b in bp.blocks if b['sub'] == 'LargeBlockBatteryBlock'), 8)
        self.assertEqual(sum(1 for b in bp.blocks if b['sub'] == 'LargeBlockGyro'), 6)
        self.assertEqual(sum(1 for b in bp.blocks if b['sub'] == 'LargeHydrogenTank'), 2)
        self.assertEqual(sum(1 for b in bp.blocks if b['sub'] == 'LargeBlockLargeHydrogenThrust'), 1)
        self.assertEqual(sum(1 for b in bp.blocks if b['sub'] == 'AirtightHangarDoor' or (b['xtype'].endswith('AirtightHangarDoor'))), 8)
        cockpits = [b for b in bp.blocks if b['sub'] == 'LargeBlockCockpitSeat']
        self.assertEqual(len(cockpits), 1)
        self.assertEqual(sum(1 for b in bp.blocks if b['sub'] == 'LargeBlockRemoteControl'), 1)

    def test_everything_is_preloaded(self):
        bp = ship(False)
        for b in bp.blocks:
            if b['xtype'].endswith(('LargeGatlingTurret', 'LargeMissileTurret')) or b['sub'] == 'LargeRailgun':
                mags = [it for it in bp.items_of(b) if it.kind == 'AmmoMagazine']
                self.assertEqual(len(mags), 1, b['name'])
                self.assertEqual(mags[0].amount, bp.max_items(b, 'AmmoMagazine', mags[0].sub) + mags[0].amount, b['name'])      # the inventory is full
            if b['sub'] == 'LargeRailgun':
                self.assertEqual(b['state'].get('capacitor'), 'full')
            if b['sub'] == 'LargeBlockSmallGenerator':
                self.assertEqual([(i.sub, i.amount) for i in bp.items_of(b)], [('Uranium', 100.0)])
            if b['sub'] == 'LargeHydrogenTank':
                self.assertEqual(b['state'].get('filled'), 'full')
            if b['sub'] == 'LargeBlockBatteryBlock':
                self.assertIn('<CurrentStoredPower>3</CurrentStoredPower>', b['extra'])
        lockers = [b for b in bp.blocks if b['sub'] == 'LargeBlockLargeContainer']
        self.assertEqual(len(lockers), 2)
        for b in lockers:
            self.assertEqual({i.sub for i in bp.items_of(b)}, {'LargeRailgunAmmo', 'LargeCalibreAmmo', 'MediumCalibreAmmo', 'NATO_25x184mm'})

    def test_catalogue_targets(self):
        """docs/design/systems_catalogue.md 4.4 within about 20 % (the mass and the forward acceleration are explained in docs/design/ships/Warden.md)"""
        n = W.numbers(ship(False))
        self.assertTrue(1.8 <= n['mass_t_total'] / 1000 <= 2.7, n['mass_t_total'])
        self.assertTrue(2.7 <= n['accel_loaded_m_s2']['Forward'] <= 4.2, n['accel_loaded_m_s2'])
        for d in ('Backward', 'Up', 'Down', 'Left', 'Right'):
            self.assertTrue(1.2 <= n['accel_loaded_m_s2'][d] <= 1.8, (d, n['accel_loaded_m_s2'][d]))
        self.assertTrue(7_700 <= n['pcu_total'] <= 11_700, n['pcu_total'])
        self.assertTrue(14_700 <= n['dps_sustained'] <= 22_100, n['dps_sustained'])
        self.assertGreaterEqual(n['power']['generation_MW'], n['power']['railgun_recharge_MW'])
        self.assertGreater(n['fuel']['full_burn_minutes'], 30)
        x, y, z = n['dims_m']
        self.assertTrue(70 <= z <= 90, n['dims_m'])

    def test_lite_differs_only_by_unseen_blocks(self):
        full, lite = ship(False), ship(True)
        a = sorted((b['sub'] or b['xtype']) for b in full.blocks)
        b = sorted((b['sub'] or b['xtype']) for b in lite.blocks)
        extra = [x for x in a if x not in b or a.count(x) != b.count(x)]
        self.assertEqual(sorted(set(extra)), ['LargeBlockSensor', 'LargeFlightMovement', 'LargeOffensiveCombat'])

class HangarTests(unittest.TestCase):
    def test_pod_docks_on_both_pairs(self):
        bp = ship(False)
        bp.grids()
        pod = bp.pod
        hc = bp.ship.conns
        pc = pod.pod.connectors
        for h, p_ in zip(hc, pc):
            a, na = MU.contact_point(bp, h, 'Forward')
            b, nb = MU.contact_point(pod, p_, 'Forward')
            b = pod._world.apply(b); nb = pod._world.rot.apply(nb)
            d = [b[i] - a[i] for i in range(3)]
            self.assertLess(MU._norm(MU._add(na, nb)), 1e-9)                                  # opposed normals
            self.assertAlmostEqual(abs(MU._dot(d, na)), pods.PAIR_GAP, delta=0.002)           # the lock gap
            lat = MU._sub(d, MU._mul(na, MU._dot(d, na)))
            self.assertLess(MU._norm(lat), 0.002)                                              # lateral error under 2 mm

    def test_hangar_checks(self):
        W.check_hangar(ship(False))
        W.check_hangar(ship(True))

    def test_locked_variant_writes_and_reloads(self):
        bp = W.warden(False, lock=True)
        with tempfile.TemporaryDirectory() as d:
            bp.write(d)
            main = sebp.load_sbc_multi(os.path.join(d, 'bp.sbc'))
            self.assertEqual(len(main.subgrids), 1)
            main.validate_all()
            xml = open(os.path.join(d, 'bp.sbc'), encoding='utf-8').read()
            self.assertEqual(xml.count('<ConnectedEntityId>'), 4)
        free = W.warden(False)
        with tempfile.TemporaryDirectory() as d:
            free.write(d)
            self.assertEqual(open(os.path.join(d, 'bp.sbc'), encoding='utf-8').read().count('<ConnectedEntityId>'), 0)      # unlocked by default (Tech Hunter form)

    def test_doors_and_connectors(self):
        bp = ship(False)
        doors = [b for b in bp.blocks if b['xtype'].endswith('AirtightHangarDoor')]
        self.assertEqual(len(doors), 8)
        self.assertEqual({(b['min'][0], b['min'][1]) for b in doors}, {(-1, 0)})
        self.assertEqual(sorted(b['min'][2] for b in doors), list(range(17, 25)))
        conns = bp.ship.conns
        self.assertEqual(conns[1]['min'][2] - conns[0]['min'][2], 5)          # 5 large cells = 25 small cells = the pod's connector spacing
        self.assertEqual(bp.pod.pod.conn_spacing, 25)

class PodTests(unittest.TestCase):
    def test_slot_counts(self):
        for n in (2, 3, 4, 6, 8):
            p = pods.cradle_pod(n)
            p.validate()
            self.assertEqual(len(p.pod.slots), n)
            self.assertEqual(sum(1 for b in p.blocks if b['kind'] == 'cradle'), n)
            self.assertEqual(sum(1 for b in p.blocks if b['kind'] == 'timer'), n)

    def test_cradle_drones_fit_and_clamp(self):
        import core, extra, wasp, support
        names = {}
        for mod in (core, extra, wasp, support):
            for nm, fn in mod.MODELS.items(): names[nm] = fn
        fits = ['Hornet', 'Wasp', 'Watcher', 'Harrier', 'Lancer', 'Aegis']
        pod = pods.cradle_pod(4)
        for k, nm in enumerate(fits[:4]):
            d = names[nm](False)
            pods.attach_drone(pod, k, d)
            host = pod.pod.slots[k]['merge']
            dm = [b for b in d.blocks if b['kind'] == 'merge'][0]
            # merge blocks one cell apart (0.5 m), clamp faces opposed
            hp = MU.block_pose(pod, host).pos
            dp = pod.subgrids[-1][1].apply(MU.block_pose(d, dm).pos)
            dist = MU._norm(MU._sub(hp, dp))
            self.assertAlmostEqual(dist, 0.5, places=6)
            self.assertEqual(C.merge_clamp_dir(pod, host), 'Forward')
            self.assertEqual(C.merge_clamp_dir(d, dm), 'Backward')
        pod.validate_all()
        for nm in fits[4:]:
            p2 = pods.cradle_pod(4)
            pods.attach_drone(p2, 0, names[nm](False))
        # the clearance check bites
        p3 = pods.cradle_pod(4)
        pods.attach_drone(p3, 0, names['Hornet'](False), check=False)
        p3.armor(4, 3, 8)                                                  # a pod block touching the drone's stern
        with self.assertRaises(AssertionError): pods.check_clearance(p3)

    def test_pod_names(self):
        p = pods.cradle_pod(4)
        for b in p.blocks:
            if b['name']: self.assertTrue(b['name'].startswith('[POD]') or b['name'] == '[Pod] Pod PB', b['name'])
        self.assertEqual([b['name'] for b in p.pod.timers], [f'[POD] Timer R{k}' for k in (1, 2, 3, 4)])
        for k, t in enumerate(p.pod.timers):
            slots = t['toolbars']['Toolbar']
            self.assertEqual(slots[0].action, 'OnOff_Off')

    def test_connector_spacing_lattice(self):
        for n in (2, 3, 4, 5, 6, 8):
            s = pods._conn_spacing(n)
            self.assertEqual(s % 5, 0)                              # a multiple of the host's 2.5 m cell
            self.assertEqual((s // 5) % 2, 1 if n % 2 == 0 else 0)  # both connectors land on cells of the pod

if __name__ == '__main__':
    unittest.main(verbosity=2)
