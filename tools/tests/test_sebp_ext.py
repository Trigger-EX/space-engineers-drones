"""Tests for the sebp extensions (inventory pre-loading, sample-derived shapes, toolbars / block groups, multi-grid files).

Ground truth is the owner's game-written blueprints (see ground.py).  Run:  python3 -I tools/tests/run_all.py
Tests that need the samples are skipped (and reported as skipped) when ref/user-samples cannot be found.
"""
import os, sys, re, json, math, hashlib, tempfile, unittest, itertools, collections
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ground
from ground import XSI, blocks, tag_tree, xml_equal, files, name_of, path_of, root_of, grids
import sebp, sebp_inv as INV, sebp_toolbar as TB, sebp_multi as MU
import extract_shapes as X
from sebp import Blueprint, Item, InventoryError, ET, ser, frag, DEFS

needs_samples = unittest.skipUnless(ground.have_samples(), 'ref/user-samples not found (set SEBP_SAMPLES)')
T = lambda t: 'MyObjectBuilder_' + t

def single(xtype, sub, grid=None, **kw):
    """one block in an otherwise empty blueprint -> (Blueprint, block)"""
    d = DEFS[sebp.defkey(xtype, sub)]
    bp = Blueprint('t', grid or d['grid'], seed=1, allow_dlc=False)
    return bp, bp.add(xtype, sub, (0, 0, 0), **kw)

def block_element(bp, b):
    if any(not x['id'] for x in bp.blocks if not (x['xtype'].endswith('_CubeBlock') and not x['name'])): bp._assign_ids(set())
    return bp.build_block(b, {})

def component(el, type_id):
    cc = el.find('ComponentContainer')
    return INV.find_component(cc, type_id)

def sample_container_inventory(b):
    return INV.find_component(b.find('ComponentContainer'), 'MyInventoryBase')

# =====================================================================================================================
# 1. inventories
# =====================================================================================================================
class TestItems(unittest.TestCase):
    def test_item_validation(self):
        with self.assertRaises(InventoryError): Item.ammo('NATO_25x184mm', 1.5)            # magazines are whole
        with self.assertRaises(InventoryError): Item.ammo('NATO_25x184mm', 0)
        with self.assertRaises(InventoryError): Item('Weapon', 'x', 1)
        with self.assertRaises(InventoryError): INV.as_items([('Uranium', 5)])               # ingot or ore? must be told
        self.assertEqual(INV.as_items([('NATO_25x184mm', 3)])[0].kind, 'AmmoMagazine')
        self.assertEqual(INV.as_items([('SteelPlate', 3)])[0].kind, 'Component')
        merged = INV.as_items([Item.ammo('NATO_25x184mm', 3), Item.ammo('NATO_25x184mm', 4)])
        self.assertEqual(merged, [Item.ammo('NATO_25x184mm', 7)])

    @needs_samples
    def test_item_xml_is_what_the_game_writes(self):
        """every supported stack found in the samples is reproduced element for element (ItemId = its index)"""
        seen = collections.Counter(); bad = []
        for name, g, b in blocks():
            inv = sample_container_inventory(b)
            if inv is None or inv.find('Items') is None: continue
            for i, el in enumerate(inv.find('Items')):
                kind = el.find('PhysicalContent').get(XSI + 'type').replace('MyObjectBuilder_', '')
                if kind not in INV.KINDS: continue
                if kind == 'Ore' and el.find('PhysicalContent').find('MaterialNameString') is not None: continue
                it = Item.from_element(el)
                mine = it.element(int(el.findtext('ItemId')))
                d = xml_equal(mine, el)
                seen[kind] += 1
                if d: bad.append((name, kind, d))
        self.assertFalse(bad, bad[:3])
        for k in ('AmmoMagazine', 'Ingot', 'Component', 'Ore'): self.assertGreater(seen[k], 3, f'no {k} stacks in the samples?')

    @needs_samples
    def test_sample_inventories_fit_with_our_volume_tables(self):
        """volume tables vs 274 game-written inventories: none is larger than its own <Volume> (to float rounding) and the ones the game
        filled to the brim (68: ore, ingots, components, scrap) come out at exactly 100 %, which pins every table entry that matters"""
        ratios = []; bad = []
        for name, g, b in blocks():
            inv = sample_container_inventory(b)
            if inv is None or inv.find('Items') is None or not len(inv.find('Items')): continue
            items = [Item.from_element(e) for e in inv.find('Items')]
            if any(i.raw is not None for i in items): continue
            r = sum(i.litres() for i in items) / (float(inv.findtext('Volume')) * 1000)
            ratios.append(r)
            if r > 1 + 1e-6: bad.append((name, b.get(XSI + 'type'), b.findtext('SubtypeName'), round(r, 5)))
        self.assertGreater(len(ratios), 200)
        self.assertFalse(bad, bad[:5])
        self.assertGreater(sum(1 for r in ratios if r > 1 - 1e-5), 40)

class TestInventoryBlocks(unittest.TestCase):
    def test_gatling_turret_24_boxes(self):
        bp, b = single(T('LargeGatlingTurret'), '', items=[Item.ammo('NATO_25x184mm', 24)])
        inv = component(block_element(bp, b), 'MyInventoryBase')
        self.assertEqual(inv.findtext('Volume'), '0.384')
        self.assertEqual(inv.findtext('nextItemId'), '1')
        self.assertEqual(inv.find('Items')[0].findtext('Amount'), '24')

    def test_capacity_errors(self):
        with self.assertRaises(InventoryError) as c: single(T('LargeGatlingTurret'), '', items=[Item.ammo('NATO_25x184mm', 25)])
        self.assertIn('do not fit', str(c.exception))
        with self.assertRaises(InventoryError): single(T('SmallMissileLauncherReload'), 'LargeRailgun', items=[Item.ammo('LargeRailgunAmmo', 7)])
        with self.assertRaises(InventoryError): single(T('SmallMissileLauncherReload'), 'LargeRailgun', items=[Item.ammo('NATO_25x184mm', 1)])   # wrong ammo
        with self.assertRaises(InventoryError): single(T('SmallGatlingGun'), '', items=[Item.component('SteelPlate', 1)])                       # not a magazine
        with self.assertRaises(InventoryError): single(T('Reactor'), 'LargeBlockLargeGenerator', items=[Item.ingot('Iron', 5)])                 # not fuel
        with self.assertRaises(InventoryError): single(T('Thrust'), 'SmallBlockSmallThrust', items=[Item.ammo('NATO_25x184mm', 1)])             # no inventory
        with self.assertRaises(InventoryError): single(T('CubeBlock'), 'SmallBlockArmorBlock', items=[Item.component('SteelPlate', 1)])
        with self.assertRaises(InventoryError): single(T('CargoContainer'), 'SmallBlockSmallContainer', items=[Item.component('SteelPlate', 300)])   # 900 L into 125 L

    def test_cargo_volume_limit(self):
        # 0.125 m3 = 125 L; SteelPlate 3 L each
        single(T('CargoContainer'), 'SmallBlockSmallContainer', items=[Item.component('SteelPlate', 41)])
        with self.assertRaises(InventoryError): single(T('CargoContainer'), 'SmallBlockSmallContainer', items=[Item.component('SteelPlate', 42)])

    def test_fill_ammo_matches_the_catalogue(self):
        """docs/design/systems_catalogue.md 2.6 / 3.3: magazines each weapon holds"""
        want = {('LargeGatlingTurret', ''): 24, ('LargeGatlingTurret', 'SmallGatlingTurret'): 5, ('LargeGatlingTurret', 'AutoCannonTurret'): 3,
                ('LargeMissileTurret', ''): 6, ('LargeMissileTurret', 'LargeBlockMediumCalibreTurret'): 6, ('LargeMissileTurret', 'LargeCalibreTurret'): 6,
                ('LargeMissileTurret', 'SmallMissileTurret'): 2, ('LargeMissileTurret', 'SmallBlockMediumCalibreTurret'): 3,
                ('InteriorTurret', 'LargeInteriorTurret'): 768,
                ('SmallGatlingGun', ''): 4, ('SmallGatlingGun', 'SmallBlockAutocannon'): 3,
                ('SmallMissileLauncher', ''): 4, ('SmallMissileLauncher', 'LargeBlockLargeCalibreGun'): 3,
                ('SmallMissileLauncherReload', 'LargeRailgun'): 6, ('SmallMissileLauncherReload', 'SmallRailgun'): 6,
                ('SmallMissileLauncherReload', 'SmallBlockMediumCalibreGun'): 3, ('SmallMissileLauncherReload', 'SmallRocketLauncherReload'): 4}
        for (t, sub), n in want.items():
            with self.subTest(block=f'{t}/{sub}'):
                bp, b = single(T(t), sub)
                bp.fill_ammo(b)
                its = bp.items_of(b)
                self.assertEqual(sum(i.amount for i in its), n)
                self.assertEqual(bp.max_items(b, 'AmmoMagazine', its[0].sub), 0)
                bp.build_xml()                                                    # writes

    def test_railgun_capacitor_and_ammo_like_starburst(self):
        bp, b = single(T('SmallMissileLauncherReload'), 'SmallRailgun', items=[Item.ammo('SmallRailgunAmmo', 3)], state={'capacitor': 'full'})
        el = block_element(bp, b)
        cap = component(el, 'MyEntityCapacitorComponent')
        self.assertEqual(cap.findtext('StoredPower'), '0.016')
        with self.assertRaises(InventoryError): single(T('SmallMissileLauncherReload'), 'SmallRailgun', state={'capacitor': 0.02})
        with self.assertRaises(InventoryError): single(T('LargeGatlingTurret'), '', state={'capacitor': 'full'})
        bp, b = single(T('SmallMissileLauncherReload'), 'LargeRailgun', state={'capacitor': 'full'})
        self.assertEqual(component(block_element(bp, b), 'MyEntityCapacitorComponent').findtext('StoredPower'), '0.5')
        bp, b = single(T('SmallMissileLauncherReload'), 'LargeRailgun', state={'capacitor': 0.25})
        self.assertEqual(component(block_element(bp, b), 'MyEntityCapacitorComponent').findtext('StoredPower'), '0.25')

    def test_reactor_uranium_and_capacity(self):
        bp, b = single(T('Reactor'), 'LargeBlockLargeGenerator', items=[Item.ingot('Uranium', 104.682732)])
        el = block_element(bp, b)
        self.assertEqual(el.findtext('Capacity'), '104.682732')
        self.assertEqual(component(el, 'MyInventoryBase').find('Items')[0].find('PhysicalContent').get(XSI + 'type'), T('Ingot'))
        # tags after Enabled keep the game's order
        tags = [c.tag for c in el]
        self.assertEqual(tags[tags.index('Enabled'):], ['Enabled', 'Capacity'] + ([] if 'UseConveyorSystem' not in tags else ['UseConveyorSystem']))
        # no items: capacity 0
        bp, b = single(T('Reactor'), 'SmallBlockSmallGenerator')
        self.assertEqual(block_element(bp, b).findtext('Capacity'), '0')

    def test_hydrogen_engine_tank_battery(self):
        bp, b = single(T('HydrogenEngine'), 'LargeHydrogenEngine', state={'fuel': 'full'})
        self.assertEqual(block_element(bp, b).findtext('Capacity'), '100000')
        bp, b = single(T('HydrogenEngine'), 'SmallHydrogenEngine', state={'fuel': 2500})
        self.assertEqual(block_element(bp, b).findtext('Capacity'), '2500')
        with self.assertRaises(InventoryError): single(T('HydrogenEngine'), 'SmallHydrogenEngine', state={'fuel': 5001})
        bp, b = single(T('OxygenTank'), 'LargeHydrogenTank', state={'filled': 'full'})
        self.assertEqual(block_element(bp, b).findtext('FilledRatio'), '1')
        with self.assertRaises(InventoryError): single(T('OxygenTank'), 'LargeHydrogenTank', state={'filled': 1.5})
        bp, b = single(T('BatteryBlock'), 'LargeBlockBatteryBlock', state={'stored_power': 'full'})
        el = block_element(bp, b)
        self.assertEqual((el.findtext('CurrentStoredPower'), el.findtext('MaxStoredPower')), ('3', '3'))
        with self.assertRaises(InventoryError): single(T('BatteryBlock'), 'LargeBlockBatteryBlock', state={'stored_power': 3.5})
        with self.assertRaises(InventoryError): single(T('Gyro'), 'LargeBlockGyro', state={'filled': 1})            # wrong block type
        with self.assertRaises(InventoryError): single(T('BatteryBlock'), 'LargeBlockBatteryBlock', state={'nonsense': 1})

    def test_welder_and_cargo_components(self):
        bp, b = single(T('CargoContainer'), 'LargeBlockLargeContainer',
                       items=[Item.ammo('LargeCalibreAmmo', 51), Item.ammo('AutocannonClip', 1), Item.component('PowerCell', 33), Item.ingot('Uranium', 100)])
        inv = component(block_element(bp, b), 'MyInventoryBase')
        self.assertEqual([e.findtext('ItemId') for e in inv.find('Items')], ['0', '1', '2', '3'])
        self.assertEqual(inv.findtext('nextItemId'), '4')
        bp, b = single(T('ShipWelder'), 'LargeShipWelder', items=[Item.component('SteelPlate', 100)])
        self.assertEqual(component(block_element(bp, b), 'MyInventoryBase').findtext('Volume'), '15.625')

    def test_containers_false_drops_items_with_a_note(self):
        bp = Blueprint('t', 'Large', containers=False)
        b = bp.add(T('LargeGatlingTurret'), '', (0, 0, 0), items=[Item.ammo('NATO_25x184mm', 2)])
        el = block_element(bp, b)
        self.assertIsNone(el.find('ComponentContainer'))
        self.assertTrue(any('not written' in n for n in bp.notes))

    def test_legacy_shapes_refuse_items_without_inventory(self):
        bp = Blueprint('t', 'Large', legacy_shapes=True)
        with self.assertRaises(InventoryError): bp.add(T('LargeGatlingTurret'), '', (0, 0, 0), items=[Item.ammo('NATO_25x184mm', 2)])

    @needs_samples
    def test_regenerates_the_inventory_of_every_loaded_sample_weapon_and_reactor(self):
        """items read from a game-written block are written back into a fresh block of the same definition: the inventory component
        (items, nextItemId, Volume, Mass, MaxItemCount, Size) and the stored state (capacitor, reactor Capacity, tank ratio) match"""
        kinds = {'SmallGatlingGun', 'SmallMissileLauncher', 'SmallMissileLauncherReload', 'LargeGatlingTurret', 'LargeMissileTurret',
                 'Reactor', 'CargoContainer', 'ShipWelder', 'OxygenTank', 'HydrogenEngine'}
        checked = collections.Counter(); problems = []
        for name, g, el in blocks(types=kinds):
            xt, sub = el.get(XSI + 'type'), el.findtext('SubtypeName') or ''
            k = sebp.defkey(xt, sub)
            if not k or DEFS[k]['dlc']: continue
            inv = sample_container_inventory(el)
            items = [Item.from_element(e) for e in inv.find('Items')] if inv is not None and inv.find('Items') is not None else []
            if any(i.raw is not None for i in items): continue
            state = INV.read_state(el)
            state = {s: v for s, v in state.items() if s in ('capacitor', 'filled')}
            # a tank holds hydrogen in the gas, not in the inventory; sample values are runtime states, take the exact ones
            bp = Blueprint('t', g.findtext('GridSizeEnum'), seed=2)
            try:
                b = bp.add(xt, sub, (0, 0, 0), items=items or None, state=state or None)
            except (InventoryError, AssertionError) as e:
                if 'capacitor' in str(e) and state.get('capacitor', 0) > 0: continue           # the sample was mid-charge above the recorded full charge? no: report
                problems.append((name, k, str(e)[:120])); continue
            mine = block_element(bp, b)
            mi, si = component(mine, 'MyInventoryBase'), inv
            if si is not None:
                if mi is None: problems.append((name, k, 'no inventory in our block')); continue
                for tag in ('Volume', 'Mass', 'MaxItemCount'):
                    if not ground.num_equal(mi.findtext(tag), si.findtext(tag)): problems.append((name, k, f'{tag} {mi.findtext(tag)} != {si.findtext(tag)}'))
                got = [(i.kind, i.sub, i.amount) for i in INV.read_items(mine.find('ComponentContainer'))]
                want = [(i.kind, i.sub, i.amount) for i in INV.as_items(items)]          # equal stacks merge (the sample may hold two)
                if len(got) != len(want) or any(a[:2] != b_[:2] or not ground.num_equal(a[2], b_[2]) for a, b_ in zip(got, want)):
                    problems.append((name, k, f'items {got} != {want}'))
                if mi.findtext('nextItemId') != str(len(want)): problems.append((name, k, 'nextItemId'))
            cap = component(el, 'MyEntityCapacitorComponent')
            if cap is not None and state.get('capacitor'):
                mc = component(mine, 'MyEntityCapacitorComponent')
                if mc is None or not ground.num_equal(mc.findtext('StoredPower'), cap.findtext('StoredPower')): problems.append((name, k, 'capacitor'))
            if xt == T('Reactor') and not ground.num_equal(mine.findtext('Capacity'), sum(i.amount for i in items if i.sub == 'Uranium'), 1e-5):
                problems.append((name, k, 'reactor Capacity'))      # game: Capacity equals the uranium kg
            checked[k] += 1
        self.assertFalse(problems, problems[:6])
        self.assertGreater(sum(checked.values()), 60)
        for k in ('SmallGatlingGun/', 'LargeMissileTurret/LargeCalibreTurret', 'Reactor/LargeBlockLargeGenerator', 'CargoContainer/LargeBlockLargeContainer'):
            self.assertGreater(checked[k], 0, f'no loaded {k} block was regenerated')

    def test_round_trip_through_load_sbc(self):
        bp = Blueprint('rt', 'Large', seed=7)
        for x in range(4): bp.armor(x, 0, 0, sub='LargeBlockArmorBlock')
        g = bp.add(T('LargeGatlingTurret'), '', (0, 1, 0), name='G', items=[Item.ammo('NATO_25x184mm', 12)])
        r = bp.add(T('SmallMissileLauncherReload'), 'LargeRailgun', (1, 1, 0), name='R', state={'capacitor': 'full'})
        bp.fill_ammo(r, count=4)
        u = bp.add(T('Reactor'), 'LargeBlockSmallGenerator', (3, 1, 0), name='U', items=[Item.ingot('Uranium', 25.5)])
        t = bp.add(T('OxygenTank'), 'LargeHydrogenTank', (3, 1, 3), name='H', state={'filled': 0.5})
        with tempfile.TemporaryDirectory() as d:
            bp.write(d)
            loaded = sebp.load_sbc(os.path.join(d, 'bp.sbc'))[0]
        by = {b['name']: b for b in loaded.blocks if b['name']}
        self.assertEqual(by['G']['items'], [Item.ammo('NATO_25x184mm', 12)])
        self.assertEqual(by['R']['items'], [Item.ammo('LargeRailgunAmmo', 4)])
        self.assertAlmostEqual(by['R']['state']['capacitor'], 0.5)
        self.assertEqual(by['U']['items'], [Item.ingot('Uranium', 25.5)])
        self.assertAlmostEqual(by['U']['state']['fuel'], 25.5)
        self.assertAlmostEqual(by['H']['state']['filled'], 0.5)

# =====================================================================================================================
# 2. shapes
# =====================================================================================================================
SHAPE_TYPES = ['LargeGatlingTurret', 'LargeMissileTurret', 'InteriorTurret', 'Cockpit', 'Reactor', 'HydrogenEngine', 'OxygenTank', 'Thrust',
               'Decoy', 'SensorBlock', 'CargoContainer', 'Conveyor', 'ConveyorConnector', 'ConveyorSorter', 'ShipConnector', 'LandingGear',
               'Door', 'AirtightHangarDoor', 'AirtightSlideDoor', 'ButtonPanel', 'TimerBlock', 'EventControllerBlock', 'Projector', 'ShipWelder',
               'Gyro', 'SmallGatlingGun', 'SmallMissileLauncher', 'SmallMissileLauncherReload', 'MergeBlock', 'Warhead', 'BatteryBlock']

class TestShapes(unittest.TestCase):
    def test_no_dlc_and_no_prototech_in_the_shape_tables(self):
        for tbl in (sebp.SHAPES2, sebp.SHAPES):
            for k in tbl:
                t, sub = k.split('/', 1)
                self.assertNotIn('Prototech', k)
                if sub != '*':
                    dk = sebp.defkey(t, sub)
                    self.assertIsNotNone(dk, k)
                    self.assertEqual(DEFS[dk]['dlc'], [], k)

    def test_templates_carry_no_entity_data(self):
        """EntityId, Min, orientation, colour, names, owner ids, ids in toolbars/lists, items, programs: all must be parameters, not copies"""
        forbidden = ['<EntityId', '<Min ', '<BlockOrientation', '<ColorMaskHSV', '<CustomName', '<Owner>', '<BuiltBy>', '<ShareMode', '<NumberInGrid',
                     '<Program>', '<Storage>', '<ConnectedEntityId', '<SkinSubtypeId', '<IntegrityPercent', '<BuildPercent', '<ConstructionStockpile']
        for tbl_name, tbl in (('shapes', sebp.SHAPES2),):
            for k, sp in tbl.items():
                text = (sp.get('container') or '') + (sp.get('tail') or '')
                for f in forbidden: self.assertNotIn(f, text, f'{k}: {f}')
                self.assertIsNone(re.search(r'\d{15,}', re.sub(r'<Mass>9223372036854[^<]*</Mass>|<MaxItemCount>\d+', '', text)), f'{k}: a long number (an id?) in the template')
                if sp.get('container'):
                    for items in frag(sp['container'])[0].iter('Items'): self.assertEqual(len(items), 0, f'{k}: pre-loaded items in a template')
                for tb in re.findall(r'<(Toolbar|BuildToolbar|OnLockedToolbar)>(.*?)</\1>', text, re.S):
                    self.assertNotIn('<Slot>', tb[1], f'{k}: populated toolbar')
                self.assertNotRegex(text, r'<(Rotation|Elevation)>(?!0<)', f'{k}: aim state')
                self.assertNotRegex(text, r'<LastShootTime>(?!0<)', f'{k}: shot time')
                self.assertNotRegex(text, r'<(Target|CurrOpening|Opening|CurrentTime)>(?!0<)', f'{k}: run state')

    def test_generic_fallback_only_for_unseen_types(self):
        """every type that has a sample (or a derivation) resolves for every non-DLC subtype to something other than GENERIC"""
        unseen_allowed = {'OffensiveCombatBlock', 'FlightMovementBlock', 'DefensiveCombatBlock', 'BasicMissionBlock'}
        for t in sebp.KNOWN_TYPES:
            if t in ('CubeBlock', 'SensorBlock', 'Warhead') or t in unseen_allowed: continue
            for sub in sebp.type_subtypes(t):
                sp, conf, src = sebp.shape_lookup(T(t), sub)
                self.assertIsNot(sp, sebp.GENERIC, f'{t}/{sub}')
                self.assertNotEqual(conf, 'guess', f'{t}/{sub}')
        # the hand-written ones are labelled as such
        self.assertEqual(sebp.shape_for(T('SensorBlock'), 'LargeBlockSensor')[1], 'from-source')
        self.assertEqual(sebp.shape_for(T('Warhead'), 'SmallWarhead')[1], 'from-source')
        self.assertEqual(sebp.shape_for(T('OffensiveCombatBlock'), 'SmallOffensiveCombat')[1], 'guess')

    def test_confidence_table_names_sources_and_unseen(self):
        ct = sebp.confidence_table()
        for t in SHAPE_TYPES:
            if t in ('SensorBlock', 'Warhead'): continue
            self.assertIn(t, ct)
        self.assertEqual(ct['SensorBlock'], 'from-source')
        self.assertIn('derived:LargeGatlingTurret', ct['InteriorTurret'])
        self.assertTrue(ct['LargeGatlingTurret'].startswith('seen'), ct['LargeGatlingTurret'])
        feat = {k: v for k, v in ct.items() if k.startswith('feature:')}
        run = [v for k, v in feat.items() if 'Run with argument' in k]
        self.assertTrue(run and 'UNSEEN' in run[0])
        # every sample named in a confidence string exists
        names = {name_of(f) for f in files()} if ground.have_samples() else None
        if names:
            for k, v in ct.items():
                if k.startswith('feature:'): continue
                for m in re.finditer(r'(?:seen|seen-type|derived)[: ]\(?([^,()]+?)(?=[,)]| \(|$)', v):
                    pass
            for k, v in sebp.shape_report().items():
                src = v[1]
                if src and not src.startswith(('Starburst', 'derived', 'LargeGatlingTurret/*')):
                    self.assertIn(src, names, f'{k}: source sample {src!r} unknown')

    @needs_samples
    def test_every_exact_shape_has_the_tag_structure_of_a_sample_block(self):
        """a block generated from each sample-derived exact shape has the same nested tag structure as at least one eligible sample
        block of that definition (the optional base tags are ignored)"""
        optional = {'EntityId', 'Min', 'BlockOrientation', 'ColorMaskHSV', 'SkinSubtypeId', 'Owner', 'BuiltBy', 'CustomName', 'IntegrityPercent',
                    'BuildPercent', 'ConstructionStockpile', 'ConstructionInventory', 'DeformationRatio', 'ShareMode',
                    'Program', 'Storage', 'StoredPower', 'RemainingAmmo', 'RemainingMagazines', 'UseConveyorSystem', 'Range', 'State', 'ConnectedEntityId',
                    'Strength', 'ThrowOut', 'CollectAll', 'ThrustOverride', 'GyroPower', 'IsMainCockpit'}          # instance data / written only when not default
        lists = {'Slots', 'SlotsGamepad', 'Items', 'Blocks', 'Waypoints', 'TextPanelsContents', 'ToolIds', 'SelectedBlocks', 'dictionary',
                 'ProjectedGrids', 'RemainingAmmosList', 'DefinitionIds', 'Sprites', 'MasterToSlaveTransform', 'MasterToSlaveGrid'}      # (filled when locked)
        def user_only(c):          # a container holding only CustomData / script storage is user data, not part of the template
            return c.tag == 'ComponentContainer' and all(cd.findtext('TypeId') == 'MyModStorageComponentBase' for cd in c.find('Components'))
        def tree(el):
            return (el.tag, () if el.tag in lists else tuple(tree(c) for c in el if c.tag not in optional and not user_only(c) and
                                                              not (c.tag == 'ComponentData' and c.findtext('TypeId') == 'MyModStorageComponentBase')))
        by_key = collections.defaultdict(list)
        for name, g, el in blocks():
            t = el.get(XSI + 'type').replace('MyObjectBuilder_', ''); sub = el.findtext('SubtypeName') or ''
            by_key[f'{t}/{sub}'].append(tree(el))
        n = 0; bad = []
        for k, sp in sebp.SHAPES2.items():
            t, sub = k.split('/', 1)
            if sub == '*' or sp.get('derived_from') or t == 'CubeBlock': continue
            dk = sebp.defkey(t, sub)
            d = DEFS[dk]
            bp = Blueprint('t', d['grid'], seed=3)
            b = bp.add(T(t), sub, (0, 0, 0), name='x')
            mine = tree(block_element(bp, b))
            n += 1
            if mine not in by_key[k]:
                bad.append(k)
        self.assertGreater(n, 80)
        self.assertFalse(bad, bad[:8])

    @needs_samples
    def test_derived_and_unseen_subtypes_follow_the_type_template(self):
        # the cockpit family: the container (text panels) differs per subtype, so unseen subtypes carry none
        sp, conf, src = sebp.shape_lookup(T('Cockpit'), 'PassengerSeatLarge')
        self.assertNotIn('container', sp)
        self.assertIn('container omitted', conf)
        sp, conf, src = sebp.shape_lookup(T('Cockpit'), 'DBSmallBlockFighterCockpit')
        self.assertIn('MyMultiTextPanelComponent', sp['container'])
        # an unseen small turret reuses the large gatling turret container with ITS volume and ITS magazine
        sp, conf, src = sebp.shape_lookup(T('LargeGatlingTurret'), 'AutoCannonTurret')
        self.assertTrue(conf.startswith('seen-type'))
        self.assertIn('<Volume>0.072</Volume>', sp['container'])
        self.assertIn('<CurrentAmmoMagazineName>AutocannonClip<', sp['tail'])
        # InteriorTurret is derived from its sibling class, and says so
        sp, conf, src = sebp.shape_lookup(T('InteriorTurret'), 'LargeInteriorTurret')
        self.assertTrue(conf.startswith('derived:'))

    @needs_samples
    def test_exact_weapon_shapes_use_the_definitions_default_magazine(self):
        """the rule used for unseen subtypes (first magazine of the weapon definition) reproduces every sample weapon"""
        n = 0
        for k, sp in sebp.SHAPES2.items():
            t, sub = k.split('/', 1)
            if sub == '*' or '<GunBase>' not in (sp.get('tail') or '') or sp.get('derived_from'): continue
            dk = sebp.defkey(t, sub)
            want = INV.default_ammo(dk)
            got = re.search(r'<CurrentAmmoMagazineName>([^<]*)<', sp['tail']).group(1)
            self.assertEqual(got, want, k); n += 1
        self.assertGreater(n, 8)

    @needs_samples
    def test_exact_inventory_volumes_follow_the_derivation_rules(self):
        """the rules for unseen subtypes (InventoryMaxVolume / InventorySize / cargo cells) hold for every sample shape that has them"""
        n = 0
        for k, sp in sebp.SHAPES2.items():
            t, sub = k.split('/', 1)
            if sub == '*' or not sp.get('container') or sp.get('derived_from'): continue
            inv = INV.find_component(frag(sp['container'])[0], 'MyInventoryBase')
            if inv is None: continue
            want = sebp.derived_inventory_volume(sebp.defkey(t, sub))
            if want is None: continue               # cockpit / connector volumes are not derivable (and say so)
            self.assertAlmostEqual(float(inv.findtext('Volume')), want, 4, k); n += 1
        self.assertGreater(n, 15)

    def test_every_non_dlc_subtype_of_the_wanted_types_can_be_placed_and_written(self):
        count = 0
        for t in SHAPE_TYPES:
            for sub in sebp.type_subtypes(t):
                d = DEFS[sebp.defkey(t, sub)]
                bp = Blueprint('t', d['grid'], seed=4)
                bp.add(T(t), sub, (0, 0, 0), name='n')
                xml = bp.build_xml()
                root = ET.fromstring(xml)
                self.assertEqual(len(root.findall('.//CubeBlocks/*')), 1)
                self.assertEqual(root.findall('.//DLC'), [])
                count += 1
        self.assertGreater(count, 100)

    @needs_samples
    def test_extractor_reproduces_the_committed_file(self):
        """tools/shapes.json is what `extract_shapes.py` makes of the samples (legacy section pinned)"""
        files_ = X.sample_files(X.find_repo_path('ref', 'user-samples'), [X.find_repo_path('pc', 'enemies', 'Tech Hunter', 'bp.sbc')])
        skipped = []
        fresh = X.extract(files_, skipped=skipped); X.derive_shapes(fresh)
        self.assertEqual(sorted(skipped), ['Gnat', 'Hornet Drone', 'Longbow L', 'Longbow S'])
        self.assertEqual(json.loads(json.dumps(fresh, sort_keys=True)), json.loads(json.dumps(sebp.SHAPES2, sort_keys=True)))
        self.assertEqual(X.centers_from(files_), sebp.CENTERS)

    @needs_samples
    def test_optional_fields_land_where_the_game_puts_them(self):
        """Strength / ThrowOut / CollectAll (connector), Range (turret), IsMainCockpit, door State: the position in the field order is the samples'"""
        def tail_after_enabled(el): 
            t = [c.tag for c in el]; return t[t.index('Enabled') + 1:]
        want = collections.defaultdict(set)
        for name, g, el in blocks(types={'ShipConnector', 'LargeMissileTurret', 'Door', 'Cockpit'}):
            tags = [c.tag for c in el]
            want[el.get(XSI + 'type')].add(tuple(tags))
        bp = Blueprint('t', 'Small', seed=1)
        c = bp.add(T('ShipConnector'), 'ConnectorSmall', (0, 0, 0), name='c', settings={'Strength': 0.1})
        tags = [x.tag for x in block_element(bp, c)]
        self.assertEqual(tags[tags.index('MasterToSlaveTransform'):tags.index('MasterToSlaveTransform') + 2], ['MasterToSlaveTransform', 'Strength'])
        c2 = bp.add(T('ShipConnector'), 'ConnectorMedium', (4, 0, 0), name='c2', settings={'ThrowOut': True, 'CollectAll': True})
        tags = [x.tag for x in block_element(bp, c2)]
        self.assertEqual(tags[tags.index('Enabled') + 1:tags.index('Enabled') + 4], ['ThrowOut', 'CollectAll', 'MasterToSlaveTransform'])
        bl = Blueprint('t', 'Large', seed=1)
        t = bl.add(T('LargeMissileTurret'), 'LargeCalibreTurret', (0, 0, 0), name='t', settings={'Range': 1200})
        tags = [x.tag for x in block_element(bl, t)]
        self.assertEqual(tags[tags.index('MaxFov') + 1], 'Range')
        bs = Blueprint('t', 'Small', seed=1)
        k = bs.add(T('Cockpit'), 'SmallBlockCockpit', (0, 0, 0), name='k', settings={'IsMainCockpit': True})
        tags = [x.tag for x in block_element(bs, k)]
        self.assertEqual(tags[tags.index('SelectedGunId') + 1], 'IsMainCockpit')
        self.assertTrue(any(('SelectedGunId', 'IsMainCockpit', 'BuildToolbar') in zip(s_, s_[1:], s_[2:]) for s_ in want[T('Cockpit')]))
        bd = Blueprint('t', 'Small', seed=1)
        dr = bd.add(T('Door'), 'SmallDoor', (0, 0, 0), name='d', settings={'State': True})
        tags = [x.tag for x in block_element(bd, dr)]
        self.assertEqual(tags[tags.index('AnyoneCanUse') + 1], 'State')
        self.assertTrue(any(('AnyoneCanUse', 'State', 'Opening') in zip(s_, s_[1:], s_[2:]) for s_ in want[T('Door')]))
        # every ordered pair of tags that the samples show adjacent for these optional fields is reproduced
        seqs = want[T('ShipConnector')]
        self.assertTrue(any(('MasterToSlaveTransform', 'Strength', 'MasterToSlaveGrid') in zip(s_, s_[1:], s_[2:]) for s_ in seqs))
        self.assertTrue(any(('ThrowOut', 'CollectAll', 'MasterToSlaveTransform') in zip(s_, s_[1:], s_[2:]) for s_ in seqs))
        self.assertTrue(any(('MaxFov', 'Range', 'RemainingAmmo') in zip(s_, s_[1:], s_[2:]) for s_ in want[T('LargeMissileTurret')]))

    def test_shape_summary_reports_what_was_not_seen(self):
        bp = Blueprint('t', 'Large', seed=1)
        bp.add(T('LargeGatlingTurret'), '', (0, 0, 0))
        bp.add(T('SensorBlock'), 'LargeBlockSensor', (5, 0, 0))
        bp.add(T('InteriorTurret'), 'LargeInteriorTurret', (7, 0, 0))
        bp.armor(9, 0, 0, sub='LargeBlockArmorBlock')
        sm = bp.shape_summary()
        self.assertEqual(set(sm), {'LargeGatlingTurret/', 'SensorBlock/LargeBlockSensor', 'InteriorTurret/LargeInteriorTurret'})
        self.assertEqual(sm['SensorBlock/LargeBlockSensor'][1], 'from-source')
        self.assertTrue(sm['InteriorTurret/LargeInteriorTurret'][1].startswith('derived:'))
        self.assertTrue(sm['LargeGatlingTurret/'][1].startswith('seen:'))

class TestOwner(unittest.TestCase):
    def test_default_owner_is_unchanged_and_overridable(self):
        bp, b = single(T('Gyro'), 'SmallBlockGyro')
        el = block_element(bp, b)
        self.assertEqual((el.findtext('Owner'), el.findtext('BuiltBy')), (str(sebp.DEFAULT_OWNER),) * 2)
        bp, b = single(T('Gyro'), 'SmallBlockGyro', owner=12345)
        el = block_element(bp, b)
        self.assertEqual((el.findtext('Owner'), el.findtext('BuiltBy')), ('12345', '12345'))
        bp, b = single(T('Gyro'), 'SmallBlockGyro', owner=0)
        el = block_element(bp, b)
        self.assertEqual((el.find('Owner'), el.find('BuiltBy')), (None, None))
        bp, b = single(T('Gyro'), 'SmallBlockGyro', owner=7, built_by=9)
        el = block_element(bp, b)
        self.assertEqual((el.findtext('Owner'), el.findtext('BuiltBy')), ('7', '9'))
        # per blueprint
        bp = Blueprint('t', 'Small', owner=555)
        b = bp.add(T('Gyro'), 'SmallBlockGyro', (0, 0, 0))
        self.assertEqual(block_element(bp, b).findtext('Owner'), '555')
        b2 = bp.add(T('Gyro'), 'SmallBlockGyro', (1, 0, 0), owner=1)
        self.assertEqual(block_element(bp, b2).findtext('Owner'), '1')
        # armour carries BuiltBy only
        bp = Blueprint('t', 'Small', owner=555)
        a = bp.armor(0, 0, 0)
        el = block_element(bp, a)
        self.assertEqual((el.find('Owner'), el.findtext('BuiltBy')), (None, '555'))

# =====================================================================================================================
# 3. toolbars and block groups
# =====================================================================================================================
def sample_slots():
    """distinct slot forms in the samples: key -> (sample, block type, slot element)"""
    out = {}
    for name, g, el in blocks():
        for tb in el:
            if tb.tag not in TB.TOOLBAR_TAGS: continue
            for slot in tb.iter('Slot'):
                d = slot.find('Data')
                if d is None: continue
                ty = d.get(XSI + 'type').replace('MyObjectBuilder_ToolbarItem', '')
                ps = tuple(p.findtext('TypeCode') for p in (d.find('Parameters') if d.find('Parameters') is not None else []))
                key = (ty, ps, d.find('CustomIconTitle') is not None)
                out.setdefault(key, (name, el.get(XSI + 'type'), slot))
    return out

class TestToolbars(unittest.TestCase):
    @needs_samples
    def test_every_slot_form_in_the_samples_is_reproduced(self):
        forms = sample_slots()
        self.assertGreaterEqual(len(forms), 6)
        for (ty, ps, titled), (name, bt, slot) in forms.items():
            d = slot.find('Data')
            idx = int(slot.findtext('Index'))
            params = [TB.Param(p.findtext('TypeCode'), p.findtext('Value')) for p in (d.find('Parameters') if d.find('Parameters') is not None else [])]
            for p in params:
                if p.code == 'Single': p.value = float(p.value)
                elif p.code in ('Int32', 'Int64'): p.value = int(p.value)
            title = d.findtext('CustomIconTitle')
            if ty == 'TerminalBlock': item = TB.BlockItem(int(d.findtext('BlockEntityId')), d.findtext('Action'), params, title)
            elif ty == 'TerminalGroup': item = TB.GroupItem(d.findtext('GroupName'), d.findtext('Action'), params, title)
            elif ty == 'Weapon': item = TB.WeaponItem(d.find('DefinitionId').get('Type'), d.find('DefinitionId').get('Subtype') or '')
            else: self.fail(f'unknown slot type {ty}')
            owner_id = int(d.findtext('BlockEntityId')) if ty == 'TerminalGroup' else 1
            tb = TB.toolbar_element('Toolbar', {idx: item}, owner_id, lambda t: int(t))
            mine = tb.find('Slots')[0]
            # sample slots of older game versions carry an empty <SubtypeName /> in Data: not written by the current game
            ref = frag(ser(slot))[0]; dd = ref.find('Data')
            sn = dd.find('SubtypeName')
            if sn is not None: dd.remove(sn)
            self.assertIsNone(xml_equal(mine, ref), (name, bt, ty))

    @needs_samples
    def test_all_actions_in_the_samples_are_known(self):
        """SEEN_ACTIONS is exactly the set of Action ids in the game-written samples (embedded projector holograms included)"""
        acts = set()
        for f in ground.game_files():
            for el in root_of(f).iter('MyObjectBuilder_CubeBlock'):
                for tb in el:
                    if tb.tag in TB.TOOLBAR_TAGS:
                        for d in tb.iter('Data'):
                            if d.findtext('Action'): acts.add(d.findtext('Action'))
        self.assertEqual(acts ^ TB.SEEN_ACTIONS, set())
        self.assertEqual(len(acts), 48)

    @needs_samples
    def test_group_slots_hold_the_id_of_the_owning_block(self):
        n = ok = 0
        for name, g, el in blocks():
            for tb in el:
                if tb.tag != 'Toolbar': continue
                for d in tb.iter('Data'):
                    if d.get(XSI + 'type').endswith('TerminalGroup'):
                        n += 1; ok += d.findtext('BlockEntityId') == el.findtext('EntityId')
        self.assertGreater(n, 100); self.assertEqual(n, ok)
        bp = Blueprint('t', 'Small', seed=2)
        a = bp.add(T('Gyro'), 'SmallBlockGyro', (0, 0, 0), name='g')
        tm = bp.add(T('TimerBlock'), 'TimerBlockSmall', (1, 0, 0), name='tm', toolbar=[TB.group('G', 'OnOff_On')])
        bp.group('G', [a])
        el = block_element(bp, tm)
        d = el.find('Toolbar/Slots/Slot/Data')
        self.assertEqual(d.findtext('BlockEntityId'), str(tm['id']))
        self.assertEqual(d.findtext('GroupName'), 'G')
        self.assertEqual(d.findtext('GridEntityId'), '0')

    def test_timer_button_event_cockpit_toolbars(self):
        bp = Blueprint('t', 'Small', seed=5)
        gun = bp.add(T('SmallGatlingGun'), '', (0, 0, 0), name='gun')
        th = bp.add(T('Thrust'), 'SmallBlockSmallThrust', (1, 0, 0), name='thr')
        bp.group('Thrusters', [th])
        tm = bp.add(T('TimerBlock'), 'TimerBlockSmall', (2, 0, 0), name='tm')
        bp.set_toolbars(tm, [TB.block(gun, 'OnOff_Off'), TB.group('Thrusters', 'OnOff_On'), None, TB.block(tm, 'Start')])
        bt = bp.add(T('ButtonPanel'), 'ButtonPanelSmall', (3, 0, 0), name='bt', toolbar={0: TB.block(tm, 'TriggerNow'), 4: TB.group('Thrusters', 'Reverse')})
        ec = bp.add(T('EventControllerBlock'), 'EventControllerSmall', (4, 0, 0), name='ec', settings=TB.event_settings('connector', 0.8, invert=True),
                    links={'selected': [th]}, toolbar=[TB.block(tm, 'TriggerNow')])
        ck = bp.add(T('Cockpit'), 'DBSmallBlockFighterCockpit', (5, 0, 0), name='ck',
                    toolbar=[TB.weapon('SmallGatlingGun', ''), TB.group('Thrusters', 'OnOff')], toolbars={'OnLockedToolbar': [TB.block(tm, 'Start')]})
        self.assertEqual(bp.notes, [])
        bp.check_toolbars()
        els = {b['name']: block_element(bp, b) for b in bp.blocks}
        idx = lambda e: [int(s.findtext('Index')) for s in e.find('Toolbar/Slots')]
        self.assertEqual(idx(els['tm']), [0, 1, 3])
        self.assertEqual(idx(els['bt']), [0, 4])
        self.assertEqual(els['ec'].findtext('SelectedEvent'), '7')
        self.assertEqual(els['ec'].findtext('ConditionInvert'), 'true')
        self.assertEqual([e.text for e in els['ec'].find('SelectedBlocks')], [str(th['id'])])
        self.assertEqual(els['ck'].find('Toolbar/Slots/Slot/Data').get(XSI + 'type'), T('ToolbarItemWeapon'))
        self.assertEqual(els['ck'].find('OnLockedToolbar/Slots/Slot/Data/BlockEntityId').text, str(tm['id']))
        self.assertEqual(len(els['ck'].find('BuildToolbar/Slots')), 0)
        # the toolbar element keeps its position among the derived fields
        tags = [c.tag for c in els['tm']]
        self.assertEqual(tags[tags.index('Enabled') + 1:], ['Toolbar', 'Delay', 'CurrentTime', 'IsCountingDown', 'Silent'])

    def test_errors(self):
        bp = Blueprint('t', 'Small', seed=5)
        gun = bp.add(T('SmallGatlingGun'), '', (0, 0, 0), name='gun')
        th = bp.add(T('Thrust'), 'SmallBlockSmallThrust', (1, 0, 0))
        with self.assertRaises(TB.ToolbarError): bp.set_toolbars(th, [TB.block(gun, 'OnOff')])          # a thruster has no toolbar
        tm = bp.add(T('TimerBlock'), 'TimerBlockSmall', (2, 0, 0))
        bp.set_toolbars(tm, [TB.group('Missing', 'OnOff')])
        with self.assertRaises(AssertionError): bp.check_toolbars()                                       # group not defined
        with self.assertRaises(TB.ToolbarError): TB.normalise(['x'])
        with self.assertRaises(TB.ToolbarError): TB.event_settings(99)
        arm = bp.armor(5, 5, 5)
        bp.set_toolbars(tm, [TB.block(arm, 'OnOff')])                                                       # plain armour has no EntityId
        with self.assertRaises(TB.ToolbarError): block_element(bp, tm)
        with self.assertRaises(AssertionError): bp.group('X', [{'name': 'not mine', 'sub': 's'}])

    def test_unseen_actions_are_reported_not_hidden(self):
        bp = Blueprint('t', 'Small', seed=5)
        tm = bp.add(T('TimerBlock'), 'TimerBlockSmall', (2, 0, 0), name='tm', toolbar=[TB.block(1, 'FancyNewAction')])
        self.assertTrue(any('FancyNewAction' in n for n in bp.notes))

    def test_pb_run_with_argument_is_experimental(self):
        bp = Blueprint('t', 'Small', seed=5)
        pb = bp.add(T('MyProgrammableBlock'), 'SmallProgrammableBlock', (0, 0, 0), name='pb')
        with self.assertRaises(TB.ToolbarError): TB.run(pb, 'go')
        tm = bp.add(T('TimerBlock'), 'TimerBlockSmall', (1, 0, 0), name='tm', toolbar=[TB.run(pb), TB.run(pb, 'launch 3', experimental=True)])
        self.assertEqual(len([n for n in bp.notes if 'UNVERIFIED' in n]), 1)
        el = block_element(bp, tm)
        slots = list(el.find('Toolbar/Slots'))
        self.assertEqual(slots[0].find('Data/Action').text, 'Run')
        self.assertIsNone(slots[0].find('Data/Parameters'))
        p = slots[1].find('Data/Parameters/MyObjectBuilder_ToolbarItemActionParameter')
        self.assertEqual((p.findtext('TypeCode'), p.findtext('Value')), ('String', 'launch 3'))
        # its closest analogue in the samples: the same wrapper with Single / Int32 / Int64 parameters
        if ground.have_samples():
            ana = [d for _, _, e in blocks(types={'TimerBlock'}) for d in e.iter('Data') if d.find('Parameters') is not None]
            self.assertTrue(ana)
            ref = ana[0].find('Parameters/MyObjectBuilder_ToolbarItemActionParameter')
            self.assertEqual(tag_tree(p), tag_tree(ref))
        ct = sebp.confidence_table()
        self.assertTrue(any('UNSEEN' in v and 'Run with argument' in k for k, v in ct.items()))

    @needs_samples
    def test_event_controller_matches_a_venture_event_controller(self):
        """Event Controller 'Pon Docking' of Venture (event 7 on two connectors, TriggerNow x2): same structure"""
        ref = [e for n, g, e in blocks(types={'EventControllerBlock'}, files_=[path_of('Venture - Expedition End')])
               if e.findtext('CustomName') == 'Event Controller Pon Docking'][0]
        bp = Blueprint('t', 'Small', seed=5)
        c1 = bp.add(T('ShipConnector'), 'ConnectorMedium', (0, 0, 0), name='c1')
        c2 = bp.add(T('ShipConnector'), 'SmallBlockInsetConnectorMedium', (4, 0, 0), name='c2')
        tm = bp.add(T('TimerBlock'), 'TimerBlockSmall', (8, 0, 0), name='t')
        ec = bp.add(T('EventControllerBlock'), 'EventControllerSmall', (9, 0, 0), name='Event Controller Pon Docking',
                    settings=TB.event_settings(7, 0, invert=False), links={'selected': [c1, c2]}, toolbar=[TB.block(tm, 'TriggerNow'), TB.block(tm, 'TriggerNow')])
        mine = block_element(bp, ec)
        self.assertEqual(tag_tree(mine)[1][-6:], tag_tree(ref)[1][-6:])
        tt = lambda e: tag_tree(e)
        self.assertEqual(tt(mine.find('Toolbar')), tt(ref.find('Toolbar')))
        for tag in ('SelectedEvent', 'ConditionInvert', 'ANDGate', 'Threshold'):
            self.assertEqual(mine.findtext(tag), ref.findtext(tag), tag)

    @needs_samples
    def test_block_groups_reproduce_the_samples(self):
        """cells in <BlockGroups> are the definition's Center cell: cross-validated (centres learned on half of the files, checked on the rest)"""
        gf = [f for f in ground.game_files() if any(g.find('BlockGroups') is not None for g in grids(f))]
        self.assertGreater(len(gf), 8)
        n = bad = 0
        for half in (0, 1):
            learn = [f for i, f in enumerate(gf) if i % 2 == half]
            test = [f for i, f in enumerate(gf) if i % 2 != half]
            centers = X.centers_from(learn)
            for f in test:
                for g in grids(f):
                    bgs = g.find('BlockGroups')
                    if bgs is None: continue
                    bp = sebp.load_sbc.__globals__['Blueprint'](name_of(f), g.findtext('GridSizeEnum'), allow_dlc=True)
                    cells = {}
                    for e in g.find('CubeBlocks'):
                        xt = e.get(XSI + 'type'); sub = e.findtext('SubtypeName') or ''
                        k = sebp.defkey(xt, sub)
                        if not k: continue
                        mn, o = e.find('Min'), e.find('BlockOrientation')
                        fwd, up = (o.get('Forward'), o.get('Up')) if o is not None else ('Forward', 'Up')
                        m = (int(mn.get('x', 0)), int(mn.get('y', 0)), int(mn.get('z', 0))) if mn is not None else (0, 0, 0)
                        blk = dict(xtype=xt, sub=sub, min=m, fwd=fwd, up=up, size=tuple(DEFS[k]['size']), key=k)
                        for c in bp.cells(blk): cells[c] = blk
                    for grp in bgs:
                        for v in grp.find('Blocks'):
                            p = (int(v.findtext('X') or 0), int(v.findtext('Y') or 0), int(v.findtext('Z') or 0))
                            blk = cells.get(p)
                            if blk is None or blk['key'] not in centers: continue
                            mine = sebp.local_cell_to_grid(bp, blk, tuple(centers[blk['key']]))
                            n += 1; bad += mine != p
        self.assertGreater(n, 200); self.assertEqual(bad, 0)

    @needs_samples
    def test_block_groups_xml_matches_pond_hopper(self):
        g = [g for g in grids(path_of('Pond Hopper Mk2')) if g.find('BlockGroups') is not None][0]
        ref = g.find('BlockGroups')
        bp = Blueprint('t', 'Small', seed=2)
        blks = [bp.add(T('Thrust'), 'SmallBlockSmallThrust', (i, 0, 0)) for i in range(3)]
        bp.group('Thrusters - Pond Hopper', blks)
        mine = TB.block_groups_element(bp.groups, bp.group_cell)
        self.assertEqual(mine.tag, ref.tag)
        self.assertEqual(tag_tree(mine[0]), tag_tree(ref[0]))
        self.assertEqual(mine[0].findtext('Name'), 'Thrusters - Pond Hopper')
        self.assertEqual(len(mine[0].find('Blocks')), 3)
        # XML text: after CubeBlocks, before DisplayName
        xml = bp.build_xml()
        self.assertLess(xml.index('</CubeBlocks>'), xml.index('<BlockGroups>'))
        self.assertLess(xml.index('</BlockGroups>'), xml.index('<DestructibleBlocks>'))

    def test_groups_round_trip(self):
        bp = Blueprint('rt', 'Small', seed=9)
        ths = [bp.add(T('Thrust'), 'SmallBlockSmallThrust', (i * 2, 0, 0), name=f't{i}') for i in range(3)]
        bp.group('All', ths); bp.group('Two', ths[:2])
        tm = bp.add(T('TimerBlock'), 'TimerBlockSmall', (0, 2, 0), name='tm', toolbar=[TB.group('All', 'OnOff_Off'), TB.block(ths[0], 'OnOff_On', [TB.single(1.5)])])
        with tempfile.TemporaryDirectory() as d:
            bp.write(d)
            loaded = sebp.load_sbc(os.path.join(d, 'bp.sbc'))[0]
        self.assertEqual({k: sorted(m['name'] for m in v) for k, v in loaded.groups.items()}, {'All': ['t0', 't1', 't2'], 'Two': ['t0', 't1']})
        lt = [b for b in loaded.blocks if b['name'] == 'tm'][0]
        sl = lt['toolbars']['Toolbar']
        self.assertEqual(sl[0].group, 'All'); self.assertEqual(sl[1].action, 'OnOff_On'); self.assertEqual(sl[1].params[0].value, 1.5)
        lt0 = [b for b in loaded.blocks if b['name'] == 't0'][0]
        self.assertEqual(sl[1].target, lt0['id'])
        # a loaded blueprint writes back with the same references
        with tempfile.TemporaryDirectory() as d:
            loaded.write(d)
            again = sebp.load_sbc(os.path.join(d, 'bp.sbc'))[0]
        lt2 = [b for b in again.blocks if b['name'] == 'tm'][0]
        t02 = [b for b in again.blocks if b['name'] == 't0'][0]
        self.assertEqual(lt2['toolbars']['Toolbar'][1].target, t02['id'])

# =====================================================================================================================
# 4. multi-grid
# =====================================================================================================================
def close(a, b, tol): return all(abs(x - y) <= tol for x, y in zip(a, b))

def grid_world(g):
    return MU.Pose.from_element(g.find('PositionAndOrientation'))

def loaded_by_entity(path):
    """{grid EntityId: (grid element, loaded Blueprint)} and {block EntityId: (Blueprint, block)}"""
    gs = sebp.load_sbc(path)
    els = grids(path)
    ge = {}; be = {}
    for el, bp in zip(els, gs):
        ge[el.findtext('EntityId')] = (el, bp)
        for b in bp.blocks:
            if b['id']: be[str(b['id'])] = (bp, b)
    return ge, be

class TestMultiGrid(unittest.TestCase):
    @needs_samples
    def test_quaternion_and_frame_conventions_on_every_sample_grid(self):
        n = 0
        for f in ground.game_files():
            for g in grids(f):
                po = g.find('PositionAndOrientation')
                if po is None or po.find('Orientation') is None: continue
                o = po.find('Orientation'); q = tuple(float(o.findtext(t)) for t in 'XYZW')
                rot = grid_world(g).rot
                mine = rot.quat()
                same = max(abs(a - b) for a, b in zip(mine, q)) < 2e-4 or max(abs(a + b) for a, b in zip(mine, q)) < 2e-4
                self.assertTrue(same, (name_of(f), mine, q))
                back = MU.Rot.from_quat(*q)
                self.assertLess(rot.angle_to(back), 1e-3)
                n += 1
        self.assertGreater(n, 100)

    @needs_samples
    def test_venture_connector_pairs_reproduce_the_documented_relative_pose(self):
        path = path_of('Venture - Expedition End')
        ge, be = loaded_by_entity(path)
        pairs = [('131301848321511708', '97090944396047994'), ('140910696978271421', '74522245970540523')]   # (master, slave)
        for mid, sid in pairs:
            mbp, mb = be[mid]; sbp, sb = be[sid]
            mel = [e for _, _, e in blocks(types={'ShipConnector'}, files_=[path]) if e.findtext('EntityId') == mid][0]
            gm = [el for el, bp in ge.values() if bp is mbp][0]; gs = [el for el, bp in ge.values() if bp is sbp][0]
            mworld, sworld = grid_world(gm), grid_world(gs)
            rel = MU.relative_block_transform(mworld, MU.block_pose(mbp, mb), sworld, MU.block_pose(sbp, sb))
            ref = MU.Pose.from_element(mel.find('MasterToSlaveTransform'))
            self.assertLess(MU._norm(MU._sub(rel.pos, ref.pos)), 0.01, (rel, ref))          # the file stores floats around 3.9e6 m: 1 cm is the noise floor
            self.assertLess(rel.rot.angle_to(ref.rot), 0.005)            # 0.2 degrees: the lock transform is a snapshot, the grids have flexed since
            # MasterToSlaveGrid
            pos, q = MU.master_to_slave_grid(mworld, sworld, MU.block_pose(sbp, sb))
            g = mel.find('MasterToSlaveGrid')
            refpos = tuple(float(g.find('PositionOffset').findtext(t)) for t in 'XYZ')
            refq = tuple(float(g.find('OrientationOffset').findtext(t)) for t in 'XYZW')
            self.assertLess(MU._norm(MU._sub(pos, refpos)), 0.01)
            self.assertLess(MU.Rot.from_quat(*q).angle_to(MU.Rot.from_quat(*refq)), 0.005)
            self.assertEqual([float(g.find('OrientationAsVector').findtext(t)) for t in 'XYZW'], list(refq))

    @needs_samples
    def test_dock_pose_matches_venture_within_the_games_lock_slack(self):
        """face-to-face geometry from the block cells alone: the saved pose differs only by the lock gap (0.07 m / 0.13 m) and by the
        <3 degree misalignment the pilot docked with"""
        path = path_of('Venture - Expedition End')
        ge, be = loaded_by_entity(path)
        for pid, cid, face_gap in (('97090944396047994', '131301848321511708', 1.8204208612442017 - 1.75),     # large Connector <- ConnectorMedium
                                   ('74522245970540523', '140910696978271421', 2.6334903240203857 - 2.5)):       # Connector <- Connector
            pbp, pb = be[pid]; cbp, cb = be[cid]
            gp = [el for el, bp in ge.values() if bp is pbp][0]; gc = [el for el, bp in ge.values() if bp is cbp][0]
            real = grid_world(gp).inv() * grid_world(gc)            # child grid in the parent grid frame, as saved
            cands = MU.dock_candidates(pbp, pb, cbp, cb)
            self.assertEqual(len(cands), 4)
            best = min(cands, key=lambda p: p.rot.angle_to(real.rot))
            self.assertLess(best.rot.angle_to(real.rot), 0.05)                                    # 2.5 degrees: the pilot's misalignment
            # where the saved child puts its connector face centre, relative to the parent's: along the contact normal by the lock gap, sideways by < 1 cm
            cp, n_p = MU.contact_point(pbp, pb)
            cl, n_c = MU.contact_point(cbp, cb)
            d = MU._sub(real.apply(cl), cp)
            along = MU._dot(d, n_p); lateral = MU._norm(MU._sub(d, MU._mul(n_p, along)))
            self.assertLess(abs(along - face_gap), 0.005, (along, face_gap)); self.assertLess(lateral, 0.02)
            # the generated pose puts the same face centre exactly on the parent's (gap 0) / at the requested gap
            for gap in (0.0, face_gap):
                pose = MU.dock_pose(pbp, pb, cbp, cb, gap=gap, roll=cands.index(best))
                d2 = MU._sub(pose.apply(cl), cp)
                self.assertAlmostEqual(MU._dot(d2, n_p), gap, 9); self.assertLess(MU._norm(MU._sub(d2, MU._mul(n_p, gap))), 1e-9)
            # centre-to-centre distance of the ideal pose = both half depths (1.75 m / 2.5 m)
            dp = MU.face_depth(pbp, pb, 'Forward'); dc = MU.face_depth(cbp, cb, 'Forward')
            pc = MU.block_pose(pbp, pb).pos; cc_ = best.apply(MU.block_pose(cbp, cb).pos)
            self.assertAlmostEqual(MU._norm(MU._sub(cc_, pc)), dp + dc, 6)

    @needs_samples
    def test_small_grid_8000_pair(self):
        path = path_of('Small Grid 8000')
        gs = sebp.load_sbc(path)
        small = [g for g in gs if g.grid == 'Small'][0]; large = [g for g in gs if g.grid == 'Large'][0]
        cs = [b for b in small.blocks if b['sub'] == 'ConnectorSmall']
        cl = [b for b in large.blocks if b['sub'] == 'LargeBlockInsetConnectorSmall']
        self.assertTrue(cs and cl)
        real = large.pose.inv() * small.pose
        best = min(MU.dock_candidates(large, cl[0], small, cs[0]), key=lambda p: p.rot.angle_to(real.rot))
        self.assertLess(best.rot.angle_to(real.rot), 0.1)
        self.assertLess(MU._norm(MU._sub(best.pos, real.pos)), 0.8)            # inset connector: the saved lock is tilted and slides a little

    def test_pose_algebra(self):
        for r in MU.ALL_24:
            self.assertLess((r * r.inv()).angle_to(MU.Rot.identity()), 1e-9)
            self.assertLess(MU.Rot.from_quat(*r.quat()).angle_to(r), 1e-9)
            f, u = r.axis_names()
            self.assertLess(MU.Rot.from_axes(f, u).angle_to(r), 1e-12)
        self.assertEqual(len(MU.ALL_24), 24)
        p = MU.Pose((1, 2, 3), MU.Rot.from_axes('Right', 'Up'))
        q = MU.Pose((0, 0, -5), MU.Rot.from_axes('Down', 'Forward'))
        pq = p * q
        self.assertLess(MU._norm(MU._sub((pq * pq.inv()).pos, (0, 0, 0))), 1e-9)
        pt = (0.3, -2, 7)
        self.assertLess(MU._norm(MU._sub(pq.apply(pt), p.apply(q.apply(pt)))), 1e-9)

    def _two_grids(self, lock=False, gap=0.0):
        big = Blueprint('Ship', 'Large', seed=5)
        for x in range(3):
            for y in range(3): big.armor(x, y, 0, sub='LargeBlockArmorBlock')
        conn = big.add(T('ShipConnector'), 'Connector', (1, 1, 1), 'Forward', 'Up', name='Dock A')
        pod = Blueprint('Pod', 'Small', seed=6)
        for x in range(2):
            for y in range(2): pod.armor(x, y, 0)
        pc = pod.add(T('ShipConnector'), 'ConnectorSmall', (0, 0, 1), 'Forward', 'Up', name='Pod conn')
        pose = big.dock(pod, conn, pc, gap=gap, lock=lock)
        return big, pod, conn, pc, pose

    def test_dock_puts_the_faces_together(self):
        big, pod, conn, pc, pose = self._two_grids(gap=0.2)
        cp, n_p = MU.contact_point(big, conn)
        cc_, n_c = MU.contact_point(pod, pc)
        world_c = pose.apply(cc_)
        self.assertLess(MU._norm(MU._sub(world_c, MU._add(cp, MU._mul(n_p, 0.2)))), 1e-9)
        self.assertLess(MU._norm(MU._add(pose.rot.apply(n_c), n_p)), 1e-9)

    def test_writer_emits_several_cubegrids(self):
        big, pod, conn, pc, pose = self._two_grids()
        xml = big.build_xml()
        root = ET.fromstring(xml)
        cgs = root.findall('.//CubeGrids/CubeGrid')
        self.assertEqual(len(cgs), 2)
        self.assertEqual([g.findtext('GridSizeEnum') for g in cgs], ['Large', 'Small'])
        w0, w1 = grid_world(cgs[0]), grid_world(cgs[1])
        self.assertLess(w0.rot.angle_to(MU.Rot.identity()), 1e-9)
        self.assertLess(MU._norm(MU._sub(w1.pos, pose.pos)), 1e-6); self.assertLess(w1.rot.angle_to(pose.rot), 1e-6)
        ids = [e.text for e in root.iter('EntityId')]
        self.assertEqual(len(ids), len(set(ids)))                                           # unique across grids
        # the order of the children of a CubeGrid is the game's (Venture grid 0 as the reference when available)
        order = [c.tag for c in cgs[1]]
        self.assertEqual(order[:7], ['SubtypeName', 'EntityId', 'PersistentFlags', 'PositionAndOrientation', 'LocalPositionAndOrientation', 'GridSizeEnum', 'CubeBlocks'])
        if ground.have_samples():
            ref = [c.tag for c in grids(path_of('Venture - Expedition End'))[0]]
            pos = [ref.index(t) for t in order if t in ref]          # (DampenersEnabled is a field of newer files only)
            self.assertEqual(pos, sorted(pos))
        # reading it back
        with tempfile.TemporaryDirectory() as d:
            big.write(d)
            rp = os.path.join(d, 'bp.sbc')
            two = sebp.load_sbc(rp)
            self.assertEqual([g.grid for g in two], ['Large', 'Small'])
            multi = sebp.Blueprint.from_sbc_multi(rp)
            self.assertEqual(len(multi.subgrids), 1)
            child, rel = multi.subgrids[0]
            self.assertLess(MU._norm(MU._sub(rel.pos, pose.pos)), 1e-5); self.assertLess(rel.rot.angle_to(pose.rot), 1e-5)
            self.assertEqual(len(child.blocks), 5)
        self.assertEqual(big.build_xml().count('<DLC>'), 0)

    def test_unlocked_by_default_locked_on_request(self):
        big, pod, conn, pc, pose = self._two_grids()
        root = ET.fromstring(big.build_xml())
        conns = [b for b in root.iter('MyObjectBuilder_CubeBlock') if b.get(XSI + 'type') == T('ShipConnector')]
        self.assertEqual(len(conns), 2)
        for c in conns:
            self.assertIsNone(c.find('ConnectedEntityId'))
            self.assertEqual(c.find('MasterToSlaveTransform').get(XSI + 'nil'), 'true')
            self.assertEqual(c.find('MasterToSlaveGrid').get(XSI + 'nil'), 'true')
            self.assertEqual(c.findtext('IsConnecting'), 'false')
        big, pod, conn, pc, pose = self._two_grids(lock=True)
        big.check_toolbars()
        root = ET.fromstring(big.build_xml())
        a, b = [bb for bb in root.iter('MyObjectBuilder_CubeBlock') if bb.get(XSI + 'type') == T('ShipConnector')]
        self.assertEqual(a.findtext('ConnectedEntityId'), b.findtext('EntityId')); self.assertEqual(b.findtext('ConnectedEntityId'), a.findtext('EntityId'))
        self.assertEqual((a.findtext('IsMaster'), b.findtext('IsMaster')), ('false', 'true'))
        self.assertEqual(ser(a.find('MasterToSlaveTransform')), ser(b.find('MasterToSlaveTransform')))
        self.assertEqual(a.find('MasterToSlaveGrid').get(XSI + 'nil'), 'true')
        self.assertIsNotNone(b.find('MasterToSlaveGrid/PositionOffset'))
        # the field order is Venture's: ConnectedEntityId directly after Enabled, then the transform fields
        tags = [c.tag for c in b]
        self.assertEqual(tags[tags.index('Enabled') + 1: tags.index('Enabled') + 4], ['ConnectedEntityId', 'MasterToSlaveTransform', 'MasterToSlaveGrid'])
        # and the stored transform is the geometry of the two grids as written in the same file
        cgs = root.findall('.//CubeGrids/CubeGrid')
        w_big, w_pod = grid_world(cgs[0]), grid_world(cgs[1])
        rel = MU.relative_block_transform(w_pod, MU.block_pose(pod, pc), w_big, MU.block_pose(big, conn))
        ref = MU.Pose.from_element(b.find('MasterToSlaveTransform'))
        self.assertLess(MU._norm(MU._sub(rel.pos, ref.pos)), 1e-5); self.assertLess(rel.rot.angle_to(ref.rot), 1e-5)
        # two locks on one connector, or a merge block, are refused
        with self.assertRaises(AssertionError): big.lock_connectors(conn, pod, pod.add(T('Gyro'), 'SmallBlockGyro', (5, 5, 5)))

    def test_cross_grid_toolbar_and_ids(self):
        big, pod, conn, pc, pose = self._two_grids()
        tm = pod.add(T('TimerBlock'), 'TimerBlockSmall', (3, 3, 3), name='pod timer', toolbar=[TB.block(conn, 'OnOff_Off'), TB.block(pc, 'OnOff_On')])
        root = ET.fromstring(big.build_xml())
        t = [b for b in root.iter('MyObjectBuilder_CubeBlock') if b.findtext('CustomName') == 'pod timer'][0]
        ids = [s.findtext('Data/BlockEntityId') for s in t.find('Toolbar/Slots')]
        self.assertEqual(ids, [str(conn['id']), str(pc['id'])])
        self.assertTrue(conn['id'] and pc['id'] and conn['id'] != pc['id'])

    def test_blueprint_id_changes_with_the_sub_grids(self):
        big, pod, conn, pc, pose = self._two_grids()
        a = big.blueprint_id('x')
        big2 = Blueprint('Ship', 'Large', seed=5)
        for x in range(3):
            for y in range(3): big2.armor(x, y, 0, sub='LargeBlockArmorBlock')
        big2.add(T('ShipConnector'), 'Connector', (1, 1, 1), 'Forward', 'Up', name='Dock A')
        self.assertNotEqual(a, big2.blueprint_id('x'))

# =====================================================================================================================
# 5. the shipped drones keep their XML
# =====================================================================================================================
GOLDEN = os.path.join(HERE, 'golden_hashes.json')

def build_model(name):
    import designs
    mods, _ = designs.load()
    for kind, modname, model, fn in designs.iter_models(mods):
        if model == name:
            return fn(False)
    raise KeyError(name)

def model_xml(name):
    bp = build_model(name)
    return bp.build_xml(bp.name)

class TestLegacyBuilds(unittest.TestCase):
    def test_legacy_shape_table_is_the_pinned_one(self):
        self.assertEqual(len([k for k in sebp.SHAPES if not k.startswith('_')]) >= 40, True)
        for k in ('Thrust/SmallBlockSmallThrust', 'ShipConnector/Connector', 'SmallMissileLauncherReload/SmallRailgun'):
            self.assertIn(k, sebp.SHAPES)
        self.assertEqual(sebp.shape_for(T('LargeGatlingTurret'), '', legacy=True)[0], sebp.GENERIC)           # what Basilisk has always had
        self.assertEqual(sebp.shape_for(T('LargeGatlingTurret'), '')[1].startswith('seen'), True)

    def test_shipped_models_use_legacy_shapes(self):
        import designs
        mods, _ = designs.load()
        for kind, modname, model, fn in designs.iter_models(mods):
            if model not in ('Hornet', 'Gnat', 'Basilisk'): continue
            self.assertTrue(fn(False).legacy_shapes, model)

    def test_existing_models_are_byte_identical_to_the_golden_files(self):
        """sha256 of the bp.sbc of Hornet, Gnat, Basilisk (and the turret/decoy/railgun test ships) as written before the extension.
        A design change on purpose needs `python3 -I tools/tests/run_all.py --update-golden`."""
        golden = json.load(open(GOLDEN))
        for name, want in golden.items():
            with self.subTest(model=name):
                got = hashlib.sha256(model_xml(name).encode()).hexdigest()
                self.assertEqual(got, want, f'{name}: bp.sbc changed (see tools/tests/test_sebp_ext.py)')

def update_golden(names=('Hornet', 'Gnat', 'Basilisk', 'Basilisk Seeker', 'Longbow L Seeker', 'Aegis', 'Midge', 'Longbow S')):
    out = {}
    for n in names:
        out[n] = hashlib.sha256(model_xml(n).encode()).hexdigest()
    json.dump(out, open(GOLDEN, 'w'), indent=1, sort_keys=True); open(GOLDEN, 'a').write('\n')
    print('golden hashes written:', GOLDEN, sorted(out))

if __name__ == '__main__':
    if '--update-golden' in sys.argv: update_golden()
    else: unittest.main()
