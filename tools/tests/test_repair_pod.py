#!/usr/bin/env python3
"""Repair Pod (docs/design/survivability.md item 17, package C3): tools/designs/pods.py cradle_pod(2, repair=True).   python3 -I tools/tests/test_repair_pod.py

Blueprint: registered as a 'ship' model, validates, no DLC / Prototech, every block named [POD]/[Pod]; 12 [Bay n] welders per slot placed by the
printers' bay solver; reach.verify_printer (every Hornet block reached under the calibrated hypotheses, no welder reaches the other slot's drone, no
mount-point contact with a drone, exit lanes free, build-order support) on the docked Hornets; a real Hornet clamped by attach_drone sits exactly
where the reach check put it; every welder port is piped to the pre-loaded [Stock] cargo (ConveyorLines written); the Custom Data the script reads
(Merge, Drone Box, Away = the side lane, Dock Forward / Dock Up, Repair=true) matches the blocks; CradleBayAI's own lane test (5 lines x Lane Cells
along Away) finds no pod block; no unnamed terminal block inside a drone box (CradleBayAI Mine() would count it as a drone block).
The first build runs the bay solver twice (about two minutes); the result is shared by every test."""
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

def add(a, b): return tuple(a[k] + b[k] for k in range(3))

_BP = None
def bp():
    global _BP
    if _BP is None:
        mods = designs.load(('pods',))[0]
        _BP = mods['pods'].MODELS['Repair Pod'](False)
        C.add_support_pbs(_BP); C.finalize(_BP)
    return _BP

def pods():
    return designs.load(('pods',))[0]['pods']

def ini(text):
    """section -> {key: value}, the way MyIni reads the PB Custom Data (first '=' splits, no comments in this text)"""
    out = {}; sec = None
    for line in text.splitlines():
        line = line.strip()
        if not line: continue
        m = re.match(r'^\[(.+)\]$', line)
        if m: sec = m.group(1); out[sec] = {}; continue
        k, v = line.split('=', 1); out[sec][k.strip()] = v.strip()
    return out

def v3(s): return tuple(int(x) for x in s.split(','))

def pod_pb(b): return [x for x in b.blocks if x['xtype'].endswith('MyProgrammableBlock') and x['name'] == '[Pod] Pod PB'][0]

class Blueprint(unittest.TestCase):
    def test_registered_and_valid(self):
        mods, _ = designs.load(('pods',))
        kinds = {m: k for k, mod, m, fn in designs.iter_models(mods)}
        self.assertEqual(kinds.get('Repair Pod'), 'ship')
        b = bp(); b.validate()
        self.assertEqual(b.name, 'Repair Pod')
        self.assertFalse(sorted({d for x in b.blocks for d in (C._stat(x).get('dlc') or [])}) or b.dlc_used(), 'DLC block')
        pods().assert_clean(b)
        self.assertTrue(b.pod.repair)
        P = pods()
        for bay in b.bays:                                          # the solver kept the script's lane lines clear (they are hard zones of the bay drone)
            self.assertTrue(any(w.startswith('bay lane') for w in bay['drone'].reserve.values()))

    def test_names_and_counts(self):
        b = bp()
        k = C.block_counts(b)
        for kind, n in (('welder', 24), ('cargo', 8), ('cradle', 2), ('conn', 2), ('bat', 2), ('reactor', 2), ('timer', 2), ('ant', 1)):
            self.assertEqual(k.get(kind), n, kind)
        self.assertFalse([x for x in b.blocks if x['kind'] == 'merge'], 'the pod halves of the cradles are kind cradle, never a drone latch')
        for n in (1, 2):
            ws = reach.bay_welders(b, n)
            self.assertEqual(len(ws), 12, f'bay {n} welders')
            self.assertTrue(all(w['name'].startswith('[POD] ') for w in ws))
        named = [x for x in b.blocks if x['name']]
        self.assertEqual(len({x['name'] for x in named}), len(named), 'unique names')
        bad = [x['name'] for x in named if not (x['name'].startswith('[POD] ') or x['name'] == '[Pod] Pod PB')]
        self.assertFalse(bad, 'every pod block starts with [POD] (the host ship never adopts one)')
        self.assertIn('CRADLE', pod_pb(b)['extra'].upper(), 'the Pod PB carries CradleBayAI')

    def test_power(self):
        """welder power is not in the definitions (a few kW each in the game); 2 batteries (4 MW each) and 2 small reactors (0.5 MW each, uranium)"""
        b = bp()
        out = sum((C._stat(x).get('stats') or {}).get('max_output_mw') or 0 for x in b.blocks if x['kind'] in ('bat', 'reactor'))
        self.assertGreaterEqual(out, 9.0)
        for r in [x for x in b.blocks if x['kind'] == 'reactor']: self.assertTrue(b.items_of(r), 'reactor fuelled')

    def test_no_unnamed_terminal_block_in_a_drone_box(self):
        """CradleBayAI Mine(): a block on the pod grid inside a bay's Drone Box counts as a drone block unless its name starts with '['"""
        b = bp(); cd = ini(pod_pb(b)['custom_data'])
        for n in (1, 2):
            lo, hi = v3(cd[f'Bay {n}']['Drone Box Min']), v3(cd[f'Bay {n}']['Drone Box Max'])
            for x in b.blocks:
                if x['xtype'] == 'MyObjectBuilder_CubeBlock': continue          # armour is not a terminal block
                if all(lo[k] <= x['min'][k] <= hi[k] for k in range(3)):
                    self.assertTrue((x['name'] or '').startswith('['), f'bay {n}: {x["sub"]} at {x["min"]} inside the drone box')

class Reach(unittest.TestCase):
    def test_reach_verify(self):
        ok, lines = reach.verify_printer(bp())
        self.assertTrue(ok, '\n'.join(lines))

    def test_calibrated_hypotheses_cover_every_block(self):
        b = bp()
        for bay in b.bays:
            fr = reach.family_report(b, bay)
            for h in ('tip r1.0', 'tip-1 r1.3'):
                self.assertFalse(fr[h], f'bay {bay["n"]}: {h} misses {fr[h]}')

    def test_attached_hornet_is_where_reach_checked(self):
        """a real Hornet clamped into each slot (pods.attach_drone: merge pair, clearance with welder tips allowed) lands on the bay's T"""
        P = pods()
        from core import hornet
        b = P.repair_pod()
        for k, bay in enumerate(b.bays):
            pose = P.attach_drone(b, k, hornet(False))
            self.assertEqual(pose.rot.axis_names(), ('Forward', 'Up'), 'upright, nose -z')
            t = pose.apply((0.0, 0.0, 0.0))
            self.assertEqual(tuple(int(round(v / 0.5)) for v in t), tuple(bay['T']))

class Conveyors(unittest.TestCase):
    def test_every_welder_port_reaches_the_stock(self):
        b = bp(); occ = b.occupancy()
        pairs = {tuple(sorted(p)) for p in b.conveyor_pairs}
        conv = {c for c, x in occ.items() if x['kind'] == 'conv'}
        stock = [x for x in b.blocks if x['kind'] == 'cargo']
        self.assertTrue(all('[Stock]' in x['name'] for x in stock))
        stock_cells = {b.cells(x)[0]: x for x in stock}
        fed = {c for c in conv if any(tuple(sorted((c, add(c, v)))) in pairs and add(c, v) in stock_cells for v in DIR.values())}
        self.assertTrue(fed, 'junctions linked to the stock')
        for w in [x for x in b.blocks if x['kind'] == 'welder']:
            cs = set(b.cells(w)); d = DIR[w['fwd']]
            back = min(sum(c[k] * d[k] for k in range(3)) for c in cs)
            face = [c for c in cs if sum(c[k] * d[k] for k in range(3)) == back]
            centre = tuple(sorted(c[k] for c in face)[len(face) // 2] for k in range(3))
            port = tuple(centre[k] - d[k] for k in range(3))
            self.assertIn(port, conv, f'{w["name"]}: junction at its port')
            self.assertIn(tuple(sorted((port, centre))), pairs, f'{w["name"]}: ConveyorLine junction - welder')
            seen = {port}; q = collections.deque([port])
            while q:
                c = q.popleft()
                for v in DIR.values():
                    n = add(c, v)
                    if n in conv and n not in seen and tuple(sorted((c, n))) in pairs: seen.add(n); q.append(n)
            self.assertTrue(seen & fed, f'{w["name"]}: no conveyor path to the [Stock] cargo')
        for p in b.conveyor_pairs:
            self.assertEqual(sum(abs(p[0][k] - p[1][k]) for k in range(3)), 1)
        self.assertIn('<ConveyorLines>', b._conveyor_xml())
        self.assertTrue(all((x['name'] or '').startswith('[POD] Conveyor ') for x in b.blocks if x['kind'] == 'conv'))

    def test_kit_preloaded(self):
        b = bp(); stock = [x for x in b.blocks if x['kind'] == 'cargo']
        vol = [sum(i.litres() for i in b.items_of(k)) for k in stock]
        self.assertTrue(all(0 < v <= designs.load(('pods',))[0]['pods'].STOCK_L for v in vol), vol)
        self.assertGreater(sum(vol) / (designs.load(('pods',))[0]['pods'].STOCK_L * len(stock)), 0.5)
        subs = {i.sub for k in stock for i in b.items_of(k)}
        self.assertTrue({'SteelPlate', 'Construction', 'Computer', 'InteriorPlate'} <= subs, subs)

class Keys(unittest.TestCase):
    def test_custom_data(self):
        b = bp(); cd = ini(pod_pb(b)['custom_data'])
        self.assertEqual(cd['Pod']['Slots'], '2')
        names = {x['name']: x for x in b.blocks if x['name']}
        for bay in b.bays:
            s = cd[f'Bay {bay["n"]}']
            self.assertEqual(s['Repair'], 'true')
            self.assertIs(names[s['Merge']], bay['merge'])
            self.assertEqual(names[s['Timer']]['kind'], 'timer')
            self.assertEqual(v3(s['Dock Forward']), (0, 0, -1)); self.assertEqual(v3(s['Dock Up']), (0, 1, 0))
            self.assertEqual(v3(s['Away']), DIR[bay['exit']], 'Away = the solved side lane')
            lo, hi = v3(s['Drone Box Min']), v3(s['Drone Box Max'])
            for c in bay['design'].D:
                w = add(c, bay['T'])
                self.assertTrue(all(lo[k] <= w[k] <= hi[k] for k in range(3)), f'bay {bay["n"]}: drone cell {w} outside the Drone Box')
            self.assertEqual(len([x for x in b.blocks if x['kind'] == 'welder' and f'[Bay {bay["n"]}]' in x['name']]), 12)
        self.assertEqual({tuple(DIR[bay['exit']]) for bay in b.bays}, {(-1, 0, 0), (1, 0, 0)}, 'outward side lanes')

    def test_plain_pod_unchanged(self):
        cd = ini(pod_pb(pods().cradle_pod(4))['custom_data'])
        for n in range(1, 5):
            self.assertEqual(cd[f'Bay {n}']['Repair'], 'false'); self.assertEqual(cd[f'Bay {n}']['Away'], '0,0,-1')

    def test_script_lane_check_is_clear(self):
        """CradleBayAI Lane(): 5 lines (centre and the 4 corners of the box face) of Lane Cells (24) cells along Away from the box face, any pod block"""
        b = bp(); occ = b.occupancy(); cd = ini(pod_pb(b)['custom_data'])
        for n in (1, 2):
            s = cd[f'Bay {n}']; mn, mx = v3(s['Drone Box Min']), v3(s['Drone Box Max']); a = v3(s['Away'])
            ax = max(range(3), key=lambda i: abs(a[i])); sg = -1 if a[ax] < 0 else 1
            u, v = (ax + 1) % 3, (ax + 2) % 3; f = mx[ax] if sg > 0 else mn[ax]
            cu, cv, du, dv = (mn[u] + mx[u]) // 2, (mn[v] + mx[v]) // 2, (mx[u] - mn[u]) // 2, (mx[v] - mn[v]) // 2
            for ou, ov in ((0, 0), (du, dv), (-du, dv), (du, -dv), (-du, -dv)):
                for k in range(1, 25):
                    c = [0, 0, 0]; c[ax] = f + sg * k; c[u] = cu + ou; c[v] = cv + ov
                    c = tuple(c)
                    self.assertFalse(c in occ, f'bay {n}: lane cell {c} holds {(occ.get(c) or {}).get("name")}')

if __name__ == '__main__':
    unittest.main()
