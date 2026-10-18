#!/usr/bin/env python3
"""Medic tender drone (docs/design/survivability.md item 18, package E1): tools/designs/medic.py + scripts/MedicAI.cs.   python3 -I tools/tests/test_medic.py

Blueprint: validates, no DLC, one large [Stock] container (SmallBlockLargeContainer) carrying two full Hornet rebuilds, 6 thrust axes >= 4 m/s2 loaded,
welder on the nose fed from the container through a junction at its port (back-face centre), ConveyorLines written, brake flames clear of the work pose,
self-projector ghost on the real blocks, no merge block, every block inside the 5x5 section or standing on it.
Script: within budget, Custom Data keys = the design's, the section 4.3 channels and exact tuple types, welder reach at the default Tip Offset."""
import os, re, sys, unittest, collections
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.dirname(TOOLS)
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import designs                                                       # noqa: E402
import _common as C                                                  # noqa: E402
import reach                                                         # noqa: E402
from sebp import DIR                                                 # noqa: E402
from minify_cs import minify                                         # noqa: E402

def raw():
    with open(os.path.join(ROOT, 'scripts', 'MedicAI.cs'), encoding='utf-8') as f: return f.read()
def add(a, b): return tuple(a[k] + b[k] for k in range(3))

_BP = None
def bp():
    global _BP
    if _BP is None:
        _BP = designs.load(('medic',))[0]['medic'].MODELS['Medic'](False)
        C.add_support_pbs(_BP); C.finalize(_BP)
    return _BP

class Blueprint(unittest.TestCase):
    def test_registered_and_valid(self):
        self.assertIn('medic', designs.DRONE_MODULES)
        b = bp(); b.validate()
        self.assertEqual(b.name, 'Medic')
        self.assertFalse(sorted({d for x in b.blocks for d in (C._stat(x).get('dlc') or [])}) or b.dlc_used(), 'DLC block')
        self.assertFalse([x for x in b.blocks if x['kind'] == 'merge'], 'the Medic does not dock: no merge block (outline rule)')

    def test_blocks(self):
        k = C.block_counts(bp())
        for kind, n in (('welder', 1), ('cargo', 1), ('gyro', 4), ('bat', 7), ('thr', 54), ('ant', 1), ('decoy', 1), ('proj', 1)): self.assertEqual(k.get(kind), n, kind)
        cargo = [x for x in bp().blocks if x['kind'] == 'cargo'][0]
        self.assertEqual(cargo['sub'], 'SmallBlockLargeContainer')
        self.assertAlmostEqual(bp().inventory_limits(cargo)[0], 15.625, 3, 'the written inventory volume is the large container\'s')
        pbs = [x for x in bp().blocks if x['xtype'].endswith('MyProgrammableBlock')]
        self.assertEqual(len(pbs), 1, 'one PB (MedicAI), no support PBs')
        self.assertIn('MEDIC v', pbs[0]['extra'], 'the PB carries MedicAI')
        self.assertIn('[Medic]', pbs[0].get('custom_data') or '')

    def test_thrust_every_axis(self):
        acc = C.loaded_accel(bp())
        for d in C.AXIS_DIRS:
            self.assertGreaterEqual(acc[d], 4.0, f'{d}: {acc[d]:.2f} m/s2 with the kit aboard')
        n = collections.Counter(C.OPP[x['fwd']] for x in bp().blocks if x['kind'] == 'thr')
        self.assertTrue(all(n[d] >= 2 for d in C.AXIS_DIRS), n)
        self.assertTrue(all('Thrust' not in x['sub'] or x['sub'] in ('SmallBlockSmallThrust', 'SmallBlockLargeThrust') for x in bp().blocks), 'ion thrusters only')
        pw = C.power_budget(bp())
        self.assertGreater(pw['margin'], 1.2, pw)
        tr = C.turn_rates(bp())['pitch_yaw_roll_rad_s2']
        self.assertTrue(all(r >= 10 for r in tr), tr)

    def test_brake_flames_clear_of_work_pose(self):
        """the thrusters that fire forward (towards the patient in WORK) are small ions whose nozzles stand >= 5 cells behind the welder tip, beside it"""
        b = bp(); w = [x for x in b.blocks if x['kind'] == 'welder'][0]
        tip = min(c[2] for c in b.cells(w))
        brakes = [x for x in b.blocks if x['kind'] == 'thr' and x['fwd'] == 'Forward']
        self.assertTrue(brakes)
        for x in brakes:
            self.assertEqual(x['sub'], 'SmallBlockSmallThrust', 'brake flames near the patient: small ions only (flame about 0.8 m)')
            cs = b.cells(x)
            self.assertGreaterEqual(min(c[2] for c in cs) - tip, 5, x['name'])
            self.assertFalse({(c[0], c[1]) for c in cs} & {(c[0], c[1]) for c in b.cells(w)}, f'{x["name"]}: its flame would hit the welder')

    def test_outline(self):
        """the 5x5 section (x, y -2..2) holds the body; only thrusters, cameras and the antenna stand outside it, at most 2 cells"""
        for x in bp().blocks:
            for c in bp().cells(x):
                out = max(abs(c[0]), abs(c[1])) - 2
                if out > 0:
                    self.assertIn(x['kind'], ('thr', 'cam', 'ant'), x['name'])
                    self.assertLessEqual(out, 2, x['name'])

    def test_welder_on_nose_and_piped_to_stock(self):
        b = bp(); occ = b.occupancy()
        w = [x for x in b.blocks if x['kind'] == 'welder'][0]
        self.assertEqual(w['fwd'], 'Forward', 'tip forward, on the nose axis')
        cs = b.cells(w); d = DIR[w['fwd']]
        front = max(sum(c[k] * d[k] for k in range(3)) for c in cs)
        allf = max(sum(c[k] * d[k] for k in range(3)) for c in occ)
        self.assertEqual(front, allf, 'nothing stands in front of the welder tip')
        self.assertEqual({(c[0], c[1]) for c in cs}, {(x, y) for x in (-1, 0, 1) for y in (-1, 0, 1)}, 'centred on the nose axis')
        back = min(sum(c[k] * d[k] for k in range(3)) for c in cs)
        centre = [c for c in cs if sum(c[k] * d[k] for k in range(3)) == back and c[0] == 0 and c[1] == 0][0]
        port = tuple(centre[k] - d[k] for k in range(3))
        self.assertEqual(occ[port]['kind'], 'conv', 'a conveyor junction behind the centre of the welder back face (its port)')
        conv = {c for c, x in occ.items() if x['kind'] == 'conv'}
        seen = {port}; q = collections.deque([port])
        while q:
            c = q.popleft()
            for v in DIR.values():
                n = add(c, v)
                if n in conv and n not in seen: seen.add(n); q.append(n)
        stock = [x for x in b.blocks if x['kind'] == 'cargo']
        self.assertTrue(all('[Stock]' in x['name'] for x in stock))
        pairs = {tuple(sorted(p)) for p in b.conveyor_pairs}
        self.assertIn(tuple(sorted((port, centre))), pairs, 'ConveyorLine junction - welder port')
        for k in stock:
            kcs = set(b.cells(k))
            j = [(c, add(c, v)) for c in seen for v in DIR.values() if add(c, v) in kcs]
            self.assertTrue(j, f'{k["name"]} touches the welder junction network')
            self.assertIn(tuple(sorted(j[0])), pairs, f'ConveyorLine for {k["name"]}')
            lo = [min(c[i] for c in kcs) for i in range(3)]; hi = [max(c[i] for c in kcs) for i in range(3)]
            self.assertEqual(j[0][1], ((lo[0] + hi[0]) // 2, (lo[1] + hi[1]) // 2, lo[2]), 'the junction meets the centre of the container\'s front face (its port)')
        for p in b.conveyor_pairs:
            self.assertEqual(sum(abs(p[0][k] - p[1][k]) for k in range(3)), 1, 'conveyor pairs are face-adjacent')
        self.assertIn('<ConveyorLines>', b._conveyor_xml())

    def test_kit(self):
        from sebp_inv import Item
        b = bp(); stock = [x for x in b.blocks if x['kind'] == 'cargo']
        vol = sum(i.litres() for k in stock for i in b.items_of(k))
        import medic
        self.assertLessEqual(vol, medic.CARGO_L)
        self.assertGreater(vol, 10 * medic.STOCK_MIN_L, 'well above the STOCK LOW line')
        self.assertEqual(medic.STOCK_MIN_L, float(re.search(r'StockMinL = ([\d.]+)', raw()).group(1)), 'design and script agree on STOCK LOW')
        got = collections.Counter()
        for k in stock:
            for i in b.items_of(k): got[i.sub] += i.amount
        need = collections.Counter()
        for x in medic.hornet_bp().blocks:
            for c in C._stat(x).get('components', []): need[c['subtype']] += c['count']
        self.assertGreaterEqual(medic.KITS, 2)
        for sub, n in need.items():
            self.assertGreaterEqual(got[sub], medic.KITS * n, f'{sub}: {got[sub]} aboard, {medic.KITS} Hornet rebuilds need {medic.KITS * n}')
        self.assertTrue({'PowerCell', 'RadioCommunication', 'MetalGrid'} <= set(got), 'the bulky parts are aboard')

    def test_self_projector(self):
        b = bp()
        pj = [x for x in b.blocks if x['kind'] == 'proj'][0]
        self.assertTrue(pj['name'].endswith('Projector [Self]'))
        self.assertIs(pj.get('enabled'), False, 'off at paste')
        s = pj['settings']
        self.assertIn('<ProjectedGrids>', s['ProjectedGrids'])
        self.assertEqual(s['ProjectedGrids'].count('<ProjectedGrids>'), 1, 'no recursion: the ghost carries no projection of its own')
        m = re.search(r'<X>(-?\d+)</X><Y>(-?\d+)</Y><Z>(-?\d+)</Z>', s['ProjectionOffset'])
        off = tuple(int(v) for v in m.groups())
        m = re.search(r'<Min x="(-?\d+)" y="(-?\d+)" z="(-?\d+)"', s['ProjectedGrids'])
        first = tuple(int(v) for v in m.groups())
        self.assertEqual(tuple(pj['min'][k] - off[k] for k in range(3)), first, 'the ghost first block lands on the real first block')
        self.assertIn(first, b.occupancy())
        self.assertTrue(b.occupancy()[first]['xtype'].endswith('CubeBlock'))

class Script(unittest.TestCase):
    def test_size(self):
        n = len(minify(raw()))
        self.assertLess(n, 40000, f'MedicAI {n} minified characters (design about 26k, PB budget 80k)')

    def test_custom_data_keys(self):
        import medic
        keys = set(re.findall(r'Cfg\("([^"]+)"', raw()))
        want = {l.split('=')[0] for l in medic.CUSTOM.splitlines() if '=' in l}
        self.assertEqual(keys, want)
        self.assertEqual(want, {'Medic Channel', 'Work Hold (s)', 'Approach Speed', 'Work Speed', 'Tip Offset (m)', 'Cruise Speed', 'Max Job Range (m)'})
        self.assertIn('const string VER = "1.1", S = "Medic"', raw())

    def test_channels_and_tuples(self):
        """section 4.3: the exact tuple types per channel"""
        t = re.sub(r'//[^\n]*', '', raw())
        self.assertIn('Ch + "_AD", new MyTuple<long, Vector3D, int>(', t)
        self.assertIn('Ch + "_ACK", new MyTuple<long, long, int>(', t)
        self.assertIn('RegisterBroadcastListener(Ch + "_REQ")', t)
        self.assertIn('RegisterBroadcastListener(Ch + "_WORK")', t)
        self.assertIn('is MyTuple<long, long, Vector3D, int>', t)
        self.assertIn('is MyTuple<long, Vector3D, Vector3D, Vector3D, Vector3D, int>', t)
        self.assertNotIn('TransmissionDistance.CurrentConstruct', t, 'medic channels use the antenna (default distance)')
        for pat in (r'p & 4095', r'\(p >> 12\) & 4095', r'p >> 24 & 1', r'p >> 25 & 1'):
            self.assertRegex(t, pat)

    def test_one_writer_and_shared_block(self):
        t = raw()
        self.assertEqual(t.count('// >>> FRIENDLY AVOIDANCE v1.0'), 1)
        with open(os.path.join(TOOLS, 'fa_block.cs'), encoding='utf-8') as f: fa = f.read().strip()
        self.assertIn(fa, t, 'FRIENDLY AVOIDANCE block verbatim (blocks_sync can take it over once MedicAI is listed in tools/blocks.json)')
        code = re.sub(r'//[^\n]*', '', t)
        self.assertNotIn('IGC.SendUnicastMessage', code)
        self.assertEqual(len(re.findall(r'ThrustOverridePercentage\s*=', code)), 2, 'ApplyThrust + Release only')
        self.assertEqual(len(re.findall(r'\.Enabled\s*=', code)), 2, 'welders: Weld() + Release() only')

    def test_welder_reach_at_default_offset(self):
        """the work point sits Tip Offset in front of the tip (welder centre + 1.25 m); the welder tip face is 1.5 m from its centre.  The block cell
        (0.5 m box round the point) must be inside the work sphere under the tip and tip-1 hypotheses of reach.py (both radii, TIER_A)"""
        t = raw()
        tipf = float(re.search(r'TipFwd = ([\d.]+)', t).group(1)); off = float(re.search(r'TipOff = ([\d.]+)', t).group(1))
        face = 1.5
        ahead = tipf + off - face                                   # work point ahead of the tip face, m
        self.assertGreater(ahead, 0.25 - 1e-9, 'the welder face stays outside the work cell (no ramming the block)')
        for h in reach.FAMILY[:reach.TIER_A]:
            d = ahead + h.backoff * reach.CELL_M - 0.25            # sphere centre to the near face of the cell
            self.assertLessEqual(d, reach.WELD_RADIUS_M - h.margin, h.name)

if __name__ == '__main__':
    unittest.main()
