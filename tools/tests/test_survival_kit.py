#!/usr/bin/env python3
"""Survival kit and item 14 on the combat drones (docs/design/survivability.md items 11-15, package C1).
python3 -I tools/tests/test_survival_kit.py   (unittest; no third-party packages)

Checked per drone, on the blueprint the build writes (support PBs added, as build_all / survivability_audit do):
  * the kit is there with the exact names: '<Model> Decoy n' (Small/LargeDecoy), '<Model> Flare [Flare]' (a *FlareLauncher subtype, FlareClips loaded),
    '<Model> Projector [Self]' (Small/LargeProjector, switched off); the parts a model cannot seat are listed in bp.kit['skipped'] (KIT_EXPECT);
  * outline rule (CLAUDE.md): the kit and the item 14 blocks add no cell outside the drone's set outline: the bounding box of the drone without the kit
    equals the bounding box with it, and for the Gnat every block except its two side cameras stays in its 2 x 2 section;
  * no DLC block;
  * the self-projection: the ghost holds every block of the drone at its own cell (ProjectionOffset = projector cell - first ghost block), and no projector
    inside the ghost carries a projection of its own;
  * item 14: no gyro single point of failure except on the Gnat (KIT_EXPECT); the Gnat has two batteries; turn rates not below the figures before the change
    for the drones that got Gyroscope 2."""
import os, sys, unittest, importlib
import xml.etree.ElementTree as ET
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
import _common as C                                                # noqa: E402

MODELS = {'Hornet': 'core', 'Watcher': 'support', 'Harrier': 'extra', 'Lancer': 'wasp', 'Aegis': 'extra', 'Gnat': 'core', 'Longbow S': 'core',
          'Longbow L': 'core', 'Longbow L Seeker': 'core', 'Wasp': 'wasp', 'Mantis': 'extra', 'Basilisk': 'extra', 'Basilisk Seeker': 'extra', 'Midge': 'support'}
# (decoys added by the kit, flare, projector, gyros >= 2)
KIT_EXPECT = {m: (2, True, True, True) for m in MODELS}
KIT_EXPECT.update({'Aegis': (0, True, True, True),          # keeps its two roof decoys
                   'Gnat': (1, False, False, False),        # 2 x 2 section: one free cell that mounts (docstring of core.gnat_survival)
                   'Midge': (2, False, False, True)})       # scriptless: decoys only (it already has two gyros)
# gyro turn rate before the change (pitch, yaw, roll rad/s2, C.turn_rates, 2026-10-17) for the drones that got Gyroscope 2
TURN_BEFORE = {'Hornet': (130.12, 112.87, 142.26), 'Watcher': (132.4, 117.26, 153.0), 'Harrier': (115.74, 100.99, 138.65), 'Lancer': (120.81, 104.29, 139.32),
               'Aegis': (98.03, 86.02, 134.48), 'Wasp': (125.47, 105.49, 135.31), 'Basilisk': (14.82, 10.43, 16.33), 'Basilisk Seeker': (14.82, 10.43, 16.33)}
_CACHE = {}

def model(name):
    if name not in _CACHE:
        bp = importlib.import_module(MODELS[name]).MODELS[name](False)
        C.add_support_pbs(bp)
        C.finalize(bp)
        _CACHE[name] = bp
    return _CACHE[name]

def box(bp, blocks):
    cs = [c for b in blocks for c in bp.cells(b)]
    return [(min(c[i] for c in cs), max(c[i] for c in cs)) for i in range(3)]

def kit_blocks(bp):
    k = bp.kit
    return [b for b in k['decoys'] + [k['flare'], k['projector']] + k['panels'] if b is not None]

class SurvivalKit(unittest.TestCase):
    def test_kit_present_with_exact_names(self):
        for name, (nd, flare, proj, _) in KIT_EXPECT.items():
            bp = model(name)
            self.assertTrue(hasattr(bp, 'kit'), name)
            dec = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Decoy' and (b['name'] or '').startswith(f'{name} Decoy ') and b['name'][len(name) + 7:].isdigit()]
            self.assertEqual(len(dec), nd, name)
            self.assertEqual(sorted(b['name'] for b in dec), [f'{name} Decoy {i + 1}' for i in range(nd)], name)
            fl = [b for b in bp.blocks if 'FlareLauncher' in b['sub']]
            if flare:
                self.assertEqual([b['name'] for b in fl], [f'{name} Flare [Flare]'], name)
                self.assertTrue(any(it.sub == 'FlareClip' and it.amount > 0 for it in bp.items_of(fl[0])), name)
            else:
                self.assertEqual(fl, [], name)
                if name == 'Gnat': self.assertTrue(any('flare' in s for s in bp.kit['skipped']), name)
            pj = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Projector']
            if proj:
                self.assertEqual([b['name'] for b in pj], [f'{name} Projector [Self]'], name)
                self.assertIs(pj[0].get('enabled'), False, name)
            else:
                self.assertEqual(pj, [], name)
        self.assertEqual(len([b for b in model('Aegis').blocks if b['xtype'] == 'MyObjectBuilder_Decoy']), 2)

    def test_outline_rule(self):
        for name in MODELS:
            bp = model(name)
            kit = {id(b) for b in kit_blocks(bp)}
            rest = [b for b in bp.blocks if id(b) not in kit]
            self.assertEqual(box(bp, bp.blocks), box(bp, rest), name)
            for b in kit_blocks(bp):
                for c in bp.cells(b): self.assertTrue(C.in_outline(c, bp.kit['outline']), f'{name}: {b["name"] or b["sub"]} at {c}')
        g = model('Gnat')
        for b in g.blocks:
            if b['name'] in ('Gnat Camera Left', 'Gnat Camera Right'): continue        # predate the outline rule
            for c in g.cells(b): self.assertTrue(c[0] in (0, 1) and c[1] in (0, 1), f'Gnat: {b["name"] or b["sub"]} at {c} outside the 2 x 2 section')

    def test_valid_no_dlc(self):
        for name in MODELS:
            bp = model(name)
            bp.validate()
            self.assertEqual(bp.dlc_used(), [], name)
            self.assertFalse(bp.allow_dlc, name)
            for m in bp.blocks:
                if m['kind'] == 'merge': C.check_docking(bp, m)
            names = [b['name'] for b in bp.blocks if b['name']]
            self.assertEqual(len(names), len(set(names)), name)

    def test_self_projection_lands_on_the_drone(self):
        for name, (_, _, proj, _) in KIT_EXPECT.items():
            if not proj: continue
            bp = model(name)
            pj = bp.kit['projector']
            root = ET.fromstring('<r xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">' + pj['settings']['ProjectedGrids'] + '</r>')
            grids = root.findall('ProjectedGrids/MyObjectBuilder_CubeGrid')
            self.assertEqual(len(grids), 1, name)
            self.assertEqual(grids[0].findtext('GridSizeEnum'), bp.grid, name)
            blocks = list(grids[0].find('CubeBlocks'))
            ghost = []
            for e in blocks:
                m = e.find('Min')
                ghost.append((e.findtext('SubtypeName') or '', (int(m.get('x')), int(m.get('y')), int(m.get('z')))))
                pg = e.find('ProjectedGrids')
                self.assertTrue(pg is None or len(pg) == 0, f'{name}: projection inside the projection')
            real = sorted((b['sub'], tuple(b['min'])) for b in bp.blocks)
            self.assertEqual(sorted(ghost), real, name)
            off = ET.fromstring(pj['settings']['ProjectionOffset'])
            o = tuple(int(off.findtext(k)) for k in 'XYZ')
            p = tuple(pj['min'])
            self.assertEqual(tuple(p[i] - o[i] for i in range(3)), ghost[0][1], f'{name}: first ghost block does not land on its real cell')
            self.assertEqual(blocks[0].tag, 'MyObjectBuilder_CubeBlock', name)
            self.assertEqual(ET.fromstring(pj['settings']['ProjectionRotation'].replace('ProjectionRotation', 'r')).findtext('X'), '0', name)

    def test_item14_spof_and_turn_rates(self):
        for name, (_, _, _, gyro2) in KIT_EXPECT.items():
            bp = model(name)
            gy = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Gyro']
            if gyro2: self.assertGreaterEqual(len(gy), 2, name)
            else: self.assertTrue(any('gyro' in s for s in bp.kit['skipped']), name)       # the Gnat: recorded
        self.assertGreaterEqual(len([b for b in model('Gnat').blocks if b['xtype'] == 'MyObjectBuilder_BatteryBlock']), 2)
        for name, t0 in TURN_BEFORE.items():
            tr = C.turn_rates(model(name))['pitch_yaw_roll_rad_s2']
            for axis, t, b in zip(('pitch', 'yaw', 'roll'), tr, t0): self.assertGreaterEqual(t, b, f'{name} {axis}')

if __name__ == '__main__':
    unittest.main()
