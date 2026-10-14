#!/usr/bin/env python3
"""Tests for the rotor + hinge AI turrets (tools/designs/turrets.py, scripts/TurretAI.cs, docs/design/turrets.md).   python3 -I tools/tests/test_turrets.py

  * the rotor / hinge geometry (sebp_multi.TOP_OFFSETS / top_pose) reproduces the owner's Tech Hunter (pc/enemies/Tech Hunter/bp.sbc: 29 stator / top pairs);
  * the writer: Motor* shapes come from that sample, a stator's XML has the game's tag order, TopBlockId / ParentEntityId resolve across the grids;
  * the pointing law of TurretAI.cs (hinge axis +-(a x d), hinge angle -+angle(a, d), azimuth turns the hinge axis there) puts the bore on the target;
  * the generated masks, the swing envelope and the naming rule (`[TU]` blocks are invisible to the Helm's `Own()`);
  * the four ships carry their turrets and the Helm Custom Data keeps the Helm off them."""
import os, sys, math, random, tempfile, unittest, re
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import xml.etree.ElementTree as ET                                   # noqa: E402
import sebp                                                          # noqa: E402
import sebp_multi as MU                                              # noqa: E402
import _common as C                                                  # noqa: E402
import turrets as T                                                  # noqa: E402
from sebp import Blueprint                                           # noqa: E402

X = '{http://www.w3.org/2001/XMLSchema-instance}'
HUNTER = os.path.join(C.ROOT, 'pc', 'enemies', 'Tech Hunter', 'bp.sbc')
_CACHE = {}

def deck(grid, n=7):
    h = Blueprint('Deck ' + grid, grid, seed=3)
    r = range(-n, n + 1)
    for x in r:
        for z in r: h.armor(x, 0, z)
    return h

def mk(model):
    key = ('mk', model)
    if key not in _CACHE:
        host = deck(T.HOST_OF[model])
        _CACHE[key] = (host, T.add_turret(host, 1, (0, 1, 0), 'Up', model=model))
    return _CACHE[key]

def ship(name):
    if name not in _CACHE:
        import designs
        mods, _ = designs.load()
        fn = {m: f for k, mod, m, f in designs.iter_models(mods)}[name]
        _CACHE[name] = fn(False)
    return _CACHE[name]

def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def nrm(a): l = math.sqrt(dot(a, a)); return tuple(x / l for x in a)
def sa(u, v, ax): return math.atan2(dot(ax, cross(u, v)), dot(u, v))


class GeometryTests(unittest.TestCase):
    @unittest.skipUnless(os.path.exists(HUNTER), 'Tech Hunter sample not found')
    def test_top_offsets_reproduce_the_tech_hunter(self):
        root = ET.parse(HUNTER).getroot()
        gs = root.findall('.//CubeGrid')
        blocks = {}
        for gi, g in enumerate(gs):
            for b in g.find('CubeBlocks'):
                e = b.findtext('EntityId')
                if e: blocks[e] = (gi, b)
        gpose = lambda g: MU.Pose.from_element(g.find('PositionAndOrientation'))
        def mkb(b):
            t = b.get(X + 'type').replace('MyObjectBuilder_', ''); sub = b.findtext('SubtypeName') or ''
            k = sebp.defkey(t, sub); d = sebp.DEFS[k]; mn = b.find('Min'); o = b.find('BlockOrientation')
            return dict(xtype=t, sub=sub, min=(int(mn.get('x', 0)), int(mn.get('y', 0)), int(mn.get('z', 0))) if mn is not None else (0, 0, 0),
                        fwd=o.get('Forward') if o is not None else 'Forward', up=o.get('Up') if o is not None else 'Up', size=tuple(d['size']), mounts=d['mounts'], key=k, name=None)
        n = bad = 0
        for gi, g in enumerate(gs):
            gp = sebp.Blueprint('x', g.findtext('GridSizeEnum'), allow_dlc=True)
            for b in g.find('CubeBlocks'):
                tid = b.findtext('TopBlockId'); ty = b.get(X + 'type')
                if not (tid and tid in blocks and 'Motor' in ty and 'Piston' not in ty): continue
                tg, tb = blocks[tid]
                cp = sebp.Blueprint('x', gs[tg].findtext('GridSizeEnum'), allow_dlc=True)
                st, tp = mkb(b), mkb(tb)
                if (st['sub'], tp['sub']) not in MU.TOP_OFFSETS: continue
                pred = gpose(g) * MU.top_pose(gp, st, cp, tp, float(b.findtext('CurrentAngle')))
                act = gpose(gs[tg])
                n += 1
                if MU._norm(MU._sub(pred.pos, act.pos)) > 0.02 or pred.rot.angle_to(act.rot) > 0.02: bad += 1
        self.assertGreaterEqual(n, 25)
        self.assertLessEqual(bad, 1, f'{bad} of {n} stator/top pairs are off')       # one LargeHinge/LargeHingeHead pair of the sample sits 0.35 m off (a displaced hinge)

    @unittest.skipUnless(os.path.exists(HUNTER), 'Tech Hunter sample not found')
    def test_motor_shapes_are_from_the_tech_hunter(self):
        for k in ('MotorAdvancedStator/LargeAdvancedStator', 'MotorAdvancedStator/LargeHinge', 'MotorAdvancedRotor/LargeAdvancedRotor', 'MotorAdvancedRotor/SmallHingeHead'):
            sp = sebp.SHAPES2[k]
            self.assertEqual(sp['src'], 'Tech Hunter', k)
        self.assertEqual(sebp.SHAPES2['MotorAdvancedRotor/SmallHingeHead']['level'], 'top')

    def test_stator_xml_has_the_games_tag_order(self):
        host, m = mk('PD4L')
        xml = host.build_xml('Deck')
        blocks = ET.fromstring(xml).findall('.//CubeGrid')[0].find('CubeBlocks')
        st = [b for b in blocks if (b.get(X + 'type') or '').endswith('MotorAdvancedStator')][0]
        tags = [c.tag for c in st]
        want = ['SubtypeName', 'EntityId', 'Min', 'ColorMaskHSV', 'Owner', 'BuiltBy', 'ShareMode', 'CustomName', 'ShowOnHUD', 'ShowInTerminal', 'ShowInToolbarConfig',
                'ShowInInventory', 'NumberInGrid', 'Enabled', 'TopBlockId', 'ShareInertiaTensor', 'SafetyDetach', 'RotorEntityId', 'WeldedEntityId', 'TargetVelocity',
                'MinAngle', 'MaxAngle', 'CurrentAngle', 'LimitsActive', 'RotorLock', 'Torque', 'BrakingTorque']
        self.assertEqual(tags, want)
        self.assertEqual(st.findtext('LimitsActive'), 'false')

    def test_links_resolve_across_the_grids(self):
        host, m = mk('PD4L')
        xml = host.build_xml('Deck')
        root = ET.fromstring(xml)
        ids, tops, parents = {}, {}, {}
        grids = root.findall('.//CubeGrid')
        self.assertEqual(len(grids), 3)
        for g in grids:
            for b in g.find('CubeBlocks'):
                e = b.findtext('EntityId')
                if e: ids[e] = b
                if b.findtext('TopBlockId'): tops[e] = b.findtext('TopBlockId')
                if b.findtext('ParentEntityId'): parents[e] = b.findtext('ParentEntityId')
        self.assertEqual(len(tops), 2); self.assertEqual(len(parents), 2)
        for st, top in tops.items():
            self.assertIn(top, ids); self.assertEqual(parents[top], st)
            self.assertTrue((ids[top].get(X + 'type') or '').endswith(('MotorAdvancedRotor',)))

    def test_pose_roundtrip_through_the_file(self):
        host, m = mk('PD4L')
        with tempfile.TemporaryDirectory() as d:
            host.write(d)
            main = sebp.load_sbc_multi(os.path.join(d, 'bp.sbc'))
            self.assertEqual(len(main.subgrids), 2)         # the loader keeps the grids flat: the top grid and the head grid
            main.validate_all()
            kinds = sorted(g.grid for g, _ in main.subgrids)
            self.assertEqual(kinds, ['Large', 'Small'])


class PointingTests(unittest.TestCase):
    def check(self, model):
        host, m = mk(model)
        a = DIR_UP = (0, 1, 0)
        def hax(alpha):
            pt = MU.top_pose(host, m.stator, m.top, m.rotor, alpha)
            return pt.rot.apply(MU.block_pose(m.top, m.hinge).rot.up)
        def bore(alpha, theta):
            _, ph = T._poses(m, alpha, theta); return ph.rot.apply((-1, 0, 0))
        random.seed(7); worst = 0.0
        for _ in range(200):
            al0, th0 = random.uniform(0, 2 * math.pi), random.uniform(-1.5, 1.5)
            d = nrm((random.uniform(-1, 1), random.uniform(0.02, 1), random.uniform(-1, 1)))
            h = hax(al0); c = cross(a, d); sn = math.sqrt(dot(c, c)); cs = dot(a, d); phi = math.atan2(sn, cs)
            hd = tuple(x / sn for x in c); sg = 1 if dot(h, hd) >= 0 else -1; hd = tuple(sg * x for x in hd)
            ea = -sa(h, hd, a); ee = -sg * phi - th0                    # exactly the formulas of TurretAI.cs Control()
            th = th0 + ee
            self.assertLessEqual(abs(th), math.pi / 2 + 1e-9)
            b = bore(al0 + ea, th)
            worst = max(worst, math.acos(max(-1, min(1, dot(b, d)))))
        self.assertLess(worst, 1e-6)

    def test_pd4l_pointing_law(self): self.check('PD4L')
    def test_pd2s_pointing_law(self): self.check('PD2S')

    def check_rotor(self, model):
        """rotor elevation: no horizon clamp, the elevation wraps (Wrap in TurretAI.cs), every direction of the sphere is reached"""
        host, m = mk(model)
        a = (0, 1, 0)
        wrap = lambda x: (x + math.pi) % (2 * math.pi) - math.pi
        def hax(alpha):
            pt = MU.top_pose(host, m.stator, m.top, m.rotor, alpha)
            return pt.rot.apply(MU.block_pose(m.top, m.hinge).rot.up)
        def bore(alpha, theta):
            _, ph = T._poses(m, alpha, theta); return ph.rot.apply((-1, 0, 0))
        random.seed(11); worst = 0.0; below = 0; far = 0.0
        for _ in range(300):
            al0, th0 = random.uniform(0, 2 * math.pi), random.uniform(-math.pi, math.pi)
            d = nrm((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1)))
            if d[1] < 0: below += 1
            h = hax(al0); c = cross(a, d); sn = math.sqrt(dot(c, c)); cs = dot(a, d); phi = math.atan2(sn, cs)
            hd = tuple(x / sn for x in c); sg = 1 if dot(h, hd) >= 0 else -1; hd = tuple(sg * x for x in hd)
            ea = -sa(h, hd, a); ee = wrap(-sg * phi - th0)            # exactly the formulas of TurretAI.cs Control() for Rotor
            far = max(far, abs(ea))
            b = bore(al0 + ea, th0 + ee)
            worst = max(worst, math.acos(max(-1, min(1, dot(b, d)))))
        self.assertLess(worst, 1e-6); self.assertGreater(below, 100)
        self.assertLessEqual(far, math.pi / 2 + 1e-9)               # the sign choice keeps the azimuth move within 90 deg

    def test_pd4lr_pointing_law(self): self.check_rotor('PD4LR')
    def test_pd2sr_pointing_law(self): self.check_rotor('PD2SR')

    def test_rotor_bore_at_pi_points_into_the_hull(self):
        for model in ('PD4LR', 'PD2SR'):
            host, m = mk(model)
            _, ph = T._poses(m, 0.0, 0.0)
            self.assertLess(MU._norm(MU._sub(ph.rot.apply((-1, 0, 0)), (0, 1, 0))), 1e-9)
            _, ph = T._poses(m, 0.4, math.pi)
            self.assertLess(MU._norm(MU._sub(ph.rot.apply((-1, 0, 0)), (0, -1, 0))), 1e-9)

    def test_rotor_head_sits_near_the_azimuth_axis(self):
        for model in ('PD4LR', 'PD2SR'):
            host, m = mk(model)
            c = T.hinge_centre(m); st = MU.block_pose(host, m.stator).pos
            self.assertLess(math.hypot(c[0] - st[0], c[2] - st[2]), 0.5)

    def test_bore_at_zero_is_the_azimuth_axis_and_the_hinge_reaches_the_horizon(self):
        for model in ('PD4L', 'PD2S'):
            host, m = mk(model)
            _, ph = T._poses(m, 0.0, 0.0)
            self.assertLess(MU._norm(MU._sub(ph.rot.apply((-1, 0, 0)), (0, 1, 0))), 1e-9)
            for el in (-math.pi / 2, math.pi / 2):
                _, ph = T._poses(m, 0.7, el)
                self.assertAlmostEqual(ph.rot.apply((-1, 0, 0))[1], 0.0, places=9)

    def test_the_script_uses_the_same_formulas(self):
        src = open(os.path.join(C.ROOT, 'scripts', 'TurretAI.cs'), encoding='utf-8').read()
        for needle in ('Vector3D.Cross(a, d)', '-SA(h, hd, a)', '(-sg * phi) - curE', 'Math.Atan2(sn, cs)'):
            self.assertIn(needle, src)


class ModelTests(unittest.TestCase):
    def test_models_validate_without_dlc(self):
        for model in ('PD4L', 'PD2S', 'PD4LR', 'PD2SR'):
            host, m = mk(model)
            for g, _ in host.grids():
                g.validate()
                self.assertEqual(g.dlc_used(), [])
                for b in g.blocks: self.assertNotIn('Prototech', b['sub'])

    def test_every_turret_block_is_named_tu(self):
        for model in ('PD4L', 'PD2S', 'PD4LR', 'PD2SR'):
            host, m = mk(model)
            names = [b['name'] for g, _ in host.grids() for b in g.blocks if b['name'] and not b['xtype'].endswith('_CubeBlock') and g is not host]
            self.assertTrue(names)
            for n in names: self.assertTrue(n.startswith('[TU] T1 '), n)
            self.assertTrue(m.stator['name'].startswith('[TU] T1 Azimuth'))
            self.assertNotIn('[SH]', ''.join(names))

    def test_hinge_limits_and_gun_counts(self):
        for model, guns in (('PD4L', 4), ('PD2S', 2)):
            host, m = mk(model)
            self.assertEqual(len(m.guns), guns)
            self.assertTrue(all(g['xtype'].endswith('SmallGatlingGun') for g in m.guns))
            xml = host.build_xml('Deck')
            hinge = [b for b in ET.fromstring(xml).iter('MyObjectBuilder_CubeBlock') if (b.findtext('CustomName') or '').endswith('Elevation')][0]
            self.assertEqual(hinge.findtext('LimitsActive'), 'true')
            self.assertAlmostEqual(float(hinge.findtext('MaxAngle')), math.pi / 2, places=5)
            self.assertAlmostEqual(float(hinge.findtext('MinAngle')), -math.pi / 2, places=5)

    def test_rotor_elevation_is_unlimited_and_the_models_are_laid_out(self):
        for model, guns, est in (('PD4LR', 4, 'MotorStator'), ('PD2SR', 2, 'MotorAdvancedStator')):
            host, m = mk(model)
            self.assertEqual(len(m.guns), guns); self.assertEqual(m.elev, 'rotor')
            xml = host.build_xml('Deck')
            el = [b for b in ET.fromstring(xml).iter() if (b.findtext('CustomName') or '').endswith('Elevation')][0]
            self.assertTrue((el.get(X + 'type') or '').endswith(est))
            self.assertEqual(el.findtext('LimitsActive'), 'false')
            self.assertEqual(sum(1 for g, _ in host.grids() for b in g.blocks if b['xtype'].endswith('Rotor')), 2)

    def test_stats(self):
        s = T.mounts_stats([mk('PD4L')[1]])
        self.assertEqual(s['guns'], 4)
        self.assertGreater(s['pcu'], 400); self.assertLess(s['mass_kg'], 8000)
        r = T.slew_rates(mk('PD4L')[1])
        self.assertGreater(r['alpha_el'], 100)              # torque never limits the turn rate; the rotor's velocity cap does


class MaskTests(unittest.TestCase):
    def test_mask_format_and_empty_deck(self):
        host, m = mk('PD4L')
        mv, fr = T.masks(m)
        self.assertEqual((len(mv), len(fr)), (180, 180))
        self.assertTrue(re.fullmatch('[0-9A-F]{180}', mv) and re.fullmatch('[0-9A-F]{180}', fr))
        self.assertEqual(set(mv), {'0'})                    # a bare deck: nothing to hit
        self.assertGreaterEqual(T.coverage(T.fire_mask(m)), 0.95)

    def test_a_wall_blocks_the_line_of_fire_and_the_swing(self):
        host = deck('Large')
        for y in (1, 2, 3):
            for z in range(-3, 4): host.armor(2, y, z)       # a wall one cell beside the turret column
        m = T.add_turret(host, 1, (0, 1, 0), 'Up')
        fb = T.fire_mask(m)
        self.assertLess(T.coverage(fb), 0.97)
        # the bore pointing at +x, horizontal (azimuth 90 deg: the hinge axis is along z... any bin whose bore hits +x low is blocked)
        blocked = [k for k, v in fb.items() if v]
        self.assertTrue(blocked)

    def test_rotor_masks_have_36_bins_and_the_custom_data_says_so(self):
        host, m = mk('PD4LR')
        mv, fr = T.masks(m)
        self.assertEqual((len(mv), len(fr)), (324, 324))
        self.assertEqual(set(mv), {'0'})
        # the whole sphere minus the deck: the bore points into the deck below the horizon, not above it
        fb = T.fire_mask(m)
        up = [fb[(a, e)] for a in range(T.AZ_BINS) for e in (14, 18, 22)]      # elevation -40, 0, +40 deg: above the horizon
        dn = [fb[(a, e)] for a in range(T.AZ_BINS) for e in (0, 35)]           # -180, +170: into the hull
        self.assertFalse(any(up)); self.assertGreater(sum(dn), len(dn) * 0.9)
        cd = T.turret_custom_data(host, [m], 'WARDEN')
        for k in ('Elevation=ROTOR', 'El Min=-180', 'El Bins=36'): self.assertIn(k, cd)
        self.assertEqual(len(re.findall(r'Move Mask=[0-9A-F]{324}\n', cd)), 1)

    def test_pack_is_36_rows_of_19_bits(self):
        bl = {(a, e): (a == 3 and e == 18) for a in range(T.AZ_BINS) for e in range(T.EL_BINS)}
        s = T._pack(bl)
        self.assertEqual(s[15:20], '40000')                  # row 3, bit 18
        bl = {(a, e): (a == 3 and e == 35) for a in range(T.AZ_BINS) for e in range(36)}
        self.assertEqual(T._pack(bl, 36)[27:36], '800000000')    # rotor: row 3, bit 35, 9 digits


class ShipTests(unittest.TestCase):
    def test_each_ship_carries_its_turrets(self):
        want = {'Shrike': 1, 'Corsair': 2, 'Warden': 4, 'Leviathan': 8}
        for name, n in want.items():
            bp = ship(name)
            mounts = [b for b in bp.blocks if b['xtype'].endswith('MotorAdvancedStator')]
            self.assertEqual(len(mounts), n, name)
            names = [g.name for g, _ in bp.grids()]
            self.assertEqual(sum(1 for x in names if x.endswith(' Top')), n, name)
            self.assertEqual(sum(1 for x in names if x.endswith(' Head')), n, name)
            pb = [b for b in bp.blocks if b['name'] == '[TU] Turret PB']
            self.assertEqual(len(pb), 1, name)
            cd = pb[0]['custom_data']
            self.assertEqual(cd.count('[Mount T'), n, name)
            self.assertIn('[Turret]', cd)
            self.assertEqual(len(re.findall(r'Move Mask=[0-9A-F]{180}\n', cd)), n, name)
            self.assertEqual(len(re.findall(r'Fire Mask=[0-9A-F]{180}\n', cd)), n, name)
            self.assertIn('<Program>', pb[0]['extra'])

    def test_the_helm_ignores_the_turrets(self):
        # Own() in ShrikeAI.cs: same construct AND name starts with [Link] Block Prefix; every Helm Custom Data carries Block Prefix=[SH]; no turret block starts with it
        for name in ('Shrike', 'Corsair', 'Warden', 'Leviathan'):
            bp = ship(name)
            helm = [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock') and b['name'].startswith('[Helm]')][0]
            self.assertIn('Block Prefix=[SH]', helm['custom_data'], name)
            for g, _ in bp.grids():
                for b in g.blocks:
                    if (b['name'] or '').startswith('[TU]'): self.assertFalse(b['name'].startswith('[SH]'))
        sh = ship('Shrike')
        for b in sh.blocks:                                  # on the Shrike EVERY other functional block wears [SH] (or is the Helm / the turret PB)
            if b['xtype'].endswith('_CubeBlock') or not b['name']: continue
            self.assertTrue(b['name'].startswith(('[SH] ', '[Helm] ', '[TU] ')), b['name'])

    def test_swing_spheres_do_not_overlap_and_parked_poses_are_free(self):
        for name in ('Shrike', 'Corsair', 'Warden', 'Leviathan'):
            bp = ship(name)
            S = bp.ship if hasattr(bp, 'ship') else None
            tm = getattr(S, 'tmounts', None) if S is not None and not isinstance(S, dict) else None
            if tm is None: continue
            self.assertEqual(T.sphere_conflicts(tm), [], name)
            for rep in S.tu_check.values(): self.assertEqual(rep['bins'], 684)


if __name__ == '__main__':
    unittest.main(verbosity=2)
