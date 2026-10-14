"""Sprue Test (test ship, kind 'test'): one-bay in-game test of printer fix option A (docs/audit/printers.md printers-01, tests P1/P3/P6).

A small dynamic printer projects a PAYLOAD (7-cell armour neck + a 4-block dummy drone: hub, battery, Programmable Block, one ion thruster).
The neck root is the payload's ONLY link to the printer (no merge blocks).  Three ship welders print it, a ship grinder lies flush beside the
middle of the neck.  scripts/SprueProbe.cs runs on both PBs (Role=printer / Role=payload) and drives weld -> charge -> cut -> push -> re-weld,
and shows what happened (docs/design/sprue_test.md).

Frame: printer frame == payload frame (T = 0).  Neck on the z axis at x=y=0 (root z=3 ... z=-3), printer at +z, payload at -z, grinder on -x,
welders on +x.  Every coordinate below is a cell of the pasted printer grid.

GUESSED XML: the ShipGrinder (no game-written sample, no legacy shape).  It is written in the form the owner's game accepted for the small ship
welder (v2.0 Mobile Hive: legacy ShipWelder/* = large-welder inventory Volume 15.625, DeformationRatio 0.5, Enabled false) with the grinder's
xsi:type.  Fallback blueprint 'Sprue Test No Grinder' = the same rig without that block (place a Small Ship Grinder by hand on the yellow seat).
NOTE: register_shapes() puts that guess into the process-global sebp.HAND['ShipGrinder']; any later ShipGrinder built in the same process
(build_all, run_all) gets the same form.  A production grinder model must call register_shapes() itself or move it into printer_lib.
SECONDARY: the legacy ShipWelder/* form ('seen-in-Starburst') is the LARGE welder's sample applied to small welders (pasted in v2.0 only).

The payload Custom Data written here must equal the PDEF fallback built into scripts/SprueProbe.cs (tools/tests/test_sprue_test.py checks it).
"""
import sys, os, copy
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import sebp
from sebp import Blueprint, DIR
import printer_lib as PL
import reach as R

NAME = 'Sprue Test'
NAME_NG = 'Sprue Test No Grinder'
NAME_CL = 'Sprue Test Close'
NO_LITE = (NAME, NAME_NG, NAME_CL)
SCRIPT = 'SprueProbe.cs'
DOCS = {
    NAME: 'TEST SHIP (option-A printer test, not packaged): a small printer projects a 7-cell armour neck plus a dummy drone (battery, PB, ion '
          'thruster); 3 welders print it, a Small Ship Grinder flush beside the neck cuts it. SprueProbe on both PBs: weld / charge / cut / hand / '
          'push / auto / stop; answers fused yes/no, grinder cuts its own grid yes/no, welded PB and battery state, payload leaves, neck re-welds. '
          'Ship Grinder XML is a GUESS (fallback: Sprue Test No Grinder).',
    NAME_CL: 'TEST SHIP (option-A printer test, not packaged): Sprue Test with the Small Ship Grinder pointing its TIP straight at the middle of the neck, one block of air in between '
             '(Sprue Test lays the grinder beside the neck, which only reaches if the game\'s working sphere is centred at the tip and about 1 m wide). Same script, same payload.',
    NAME_NG: 'TEST SHIP: Sprue Test without the Ship Grinder block (its XML is a guess): paste this if Sprue Test fails to paste, then place a '
             'Small Ship Grinder by hand on the yellow seat (the PB screen says "pos OK" when it sits right).',
}

NECK = [(0, 0, z) for z in (3, 2, 1, 0, -1, -2, -3)]   # N1 (root, next to the stub) .. N7; N1 is the first payload block = projection anchor
HUB, PAY_BAT, PAY_PB, PAY_THR = (0, 0, -4), (0, 0, -5), (0, -1, -4), (0, 1, -5)
STUB, CORE, PROJ, PB, BAT = (0, 0, 4), (0, 0, 5), (0, 1, 5), (0, 0, 6), (0, -1, 5)
BEAM = [(x, 0, 5) for x in range(1, 7)]
WELDERS = ((1, -1, 2), (2, -1, -1), (1, -1, -4))        # Forward=Left (tip -x), Up=Up; Welder 2 keeps ONE cell of air (its tip would sit in the grinder sphere)
# grinder layouts: 'side' = Sprue Test (long axis y, tip +y, flush BESIDE N3..N5), 'close' = Sprue Test Close (long axis x, tip +x, one block of air in front of N4).
# seat = the armour cube under a corner of the grinder's back ring (its Back mount is a thin frame), stalk = armour from the seat to the core,
# tier = how many of the FIRST entries of reach.FAMILY must cut the neck; FAMILY is [tip, tip-1, tip-2, mid] x [r 1.3 m, r 1.0 m] = 8 entries, so 8 = every hypothesis,
# 3 = tip r1.3, tip r1.0, tip-1 r1.3 (tip-1 r1.0 is left out on purpose for 'close': it reaches N4 only, an exact tie at 2.0 cells, and after a hand cut of N2 it would orphan N3).
# collateral = printer blocks the sphere may touch (guarded at run time). The tool tip direction is 'fwd'.
LAYOUTS = {
    'side':  dict(min=(-3, -4, -1), fwd='Up', up='Forward', tier=8, collateral=(),
                  stalk=[(-1, -5, z) for z in range(1, 6)] + [(-1, y, 5) for y in range(-4, 1)]),
    'close': dict(min=(-7, -1, -1), fwd='Right', up='Up', tier=3, collateral=('Sprue Welder 1', 'Sprue Welder 3'),
                  stalk=[(-8, -1, z) for z in range(1, 6)] + [(-8, 0, 5)] + [(x, 0, 5) for x in range(-7, 0)]),
}
GRINDER = LAYOUTS['side']['min']; G_STALK = LAYOUTS['side']['stalk']
YELLOW, NECK_RED, WELD_GREEN, PROJ_COL = (0.13, 0.9, 0.9), C.RED, (0.33, 0.7, 0.4), (0.6, 0.3, 0.1)
GUARD_CELLS = 4.0                                       # printer cells this close (cells) to a grinder sphere centre are watched by the PB
HAND_CUT = 1                                            # the owner hand-grinds NECK[1] = N2 (second red cell): N1 stays on the printer, no orphan after CUT2
PAYLOAD_HEAD = ('Role=payload\nMe Cell=0,-1,-4\nPrinter PB=Sprue PB\nBattery=Sprue Payload Battery\nNeck=%s\nCluster=%s\n'
                'Push=hold\nPush Percent=2\nPush Seconds=1\nPush Delay=15\n')

def register_shapes():
    """idempotent, called by the builder, never at import: the ShipGrinder has no seen or legacy shape (see module docstring)"""
    if 'ShipGrinder' not in sebp.HAND:
        g = copy.deepcopy(sebp.SHAPES['ShipWelder/*'])
        g['conf'] = 'guess: legacy small-welder form (pasted in v2.0) with the grinder xsi:type'
        sebp.HAND['ShipGrinder'] = g

def cells_txt(cs): return ';'.join(','.join(str(v) for v in c) for c in cs)

def payload_head():
    assert PAY_PB == (0, -1, -4)
    return PAYLOAD_HEAD % (cells_txt(NECK), cells_txt([HUB, PAY_BAT, PAY_PB, PAY_THR]))

def payload_bp():
    pay = Blueprint('Sprue Payload', 'Small', seed=C.seed_for('Sprue Payload'), legacy_shapes=True)
    for c in NECK: pay.armor(*c, color=NECK_RED)
    pay.armor(*HUB)
    pay.add('MyObjectBuilder_BatteryBlock', 'SmallBlockSmallBatteryBlock', PAY_BAT, name='Sprue Payload Battery', extra=C.BAT_S, kind='bat',
            state={'stored_power': 0.04})                 # 0.04 written: welded 0.04 = kept, 0.05 = full, 0.015 = 30 % default, 0 = empty
    pay.add('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', PAY_PB, 'Up', 'Forward', 'Sprue Payload PB',
            extra=pb_extra(script_text(SCRIPT)), kind='ctrl',
            custom_data='[Sprue]\n' + payload_head())
    C.thr(pay, PAY_THR, 'Backward', 'Up', 'Sprue Payload Thruster')      # exhaust +z, pushes -z; reserves (0,1,-3..0)
    pay.validate()
    return pay

def grinder_centres(bp):
    L = LAYOUTS[getattr(bp, 'layout', 'side')]
    gb = dict(min=L['min'], fwd=L['fwd'], up=L['up'], size=(3, 3, 6))
    cs = bp.cells(gb)
    return cs, [(h, R.tip_point_from_cells(cs, DIR[L['fwd']], h.backoff)) for h in R.FAMILY]

def sprue_test(lite=False, grinder=True, layout='side'):
    register_shapes()
    L = LAYOUTS[layout]
    name = NAME_CL if layout == 'close' else (NAME if grinder else NAME_NG)
    pay = payload_bp()
    bp = Blueprint(name, 'Small', seed=C.seed_for(name), legacy_shapes=True)
    bp.armor(*STUB, color=YELLOW)
    bp.armor(*CORE)
    for c in BEAM: bp.armor(*c)
    sett, off = PL.projector_settings(pay, (0, 0, 0), PROJ, seed_text='sprue')
    bp.add('MyObjectBuilder_Projector', 'SmallProjector', PROJ, 'Forward', 'Up', 'Sprue Projector', PROJ_COL, settings=sett, kind='proj')
    C.bat(bp, BAT, 'Sprue Battery')
    pb = bp.add('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', PB, 'Forward', 'Up', 'Sprue PB', extra=pb_extra(script_text(SCRIPT)), kind='ctrl')
    for i, mn in enumerate(WELDERS, 1):
        bp.add('MyObjectBuilder_ShipWelder', 'SmallShipWelder', mn, 'Left', 'Up', f'Sprue Welder {i}', WELD_GREEN, kind='welder', enabled=False)
    for i, c in enumerate(L['stalk']): bp.armor(*c, color=YELLOW if i == 0 else None)
    bp.layout = layout
    if grinder:
        bp.add('MyObjectBuilder_ShipGrinder', 'SmallShipGrinder', L['min'], L['fwd'], L['up'], 'Sprue Grinder', WELD_GREEN, kind='grinder', enabled=False)
        bp.notes.append('UNSEEN XML: SmallShipGrinder written as the legacy small-welder form with the grinder xsi:type (fallback: Sprue Test No Grinder)')
    gcells, cen = grinder_centres(bp)
    guard = sorted({c for b in bp.blocks if b['sub'] != 'SmallShipGrinder' for c in bp.cells(b) if min(R.cell_dist(p, c) for _, p in cen) < GUARD_CELLS})
    gmax = tuple(max(c[k] for c in gcells) for k in range(3))
    pb['custom_data'] = ('[Sprue]\nRole=printer\nMe Cell=%s\nNeck=%s\nCluster=%s\nGuard=%s\nGrinder Min=%s\nGrinder Max=%s\nGrinder Forward=%s\n'
                         'Projector=Sprue Projector\nBattery=Sprue Battery\nPayload PB=Sprue Payload PB\nPayload Battery=Sprue Payload Battery\n'
                         'Cycles=2\nCharge Seconds=30\nCut Seconds=60\nClear Metres=6\nBlueprint Owner=%d\n'
                         % (cells_txt([PB]), cells_txt(NECK), cells_txt([HUB, PAY_BAT, PAY_PB, PAY_THR]), cells_txt(guard), cells_txt([L['min']]), cells_txt([gmax]), L['fwd'],
                            sebp.DEFAULT_OWNER))
    bp.validate()
    bp.payload, bp.projection_offset, bp.guard_cells = pay, off, guard
    verify(bp)                                   # raises AssertionError: build_all lists the model as FAILED
    return finalize(bp)

def fused_view(bp):
    """printer + payload blocks on one Blueprint (what the projector builds), payload exhaust lane included"""
    f = copy.deepcopy(bp); f.name = bp.name + ' (fused view)'
    n = len(f.blocks)
    f.blocks += [copy.deepcopy(b) for b in bp.payload.blocks]
    f.reserve.update(bp.payload.reserve)
    return f, n

def verify(bp):
    """the checks build_all cannot do (it never reloads the nested ProjectedGrids): one piece when fused, ONLY the stub-root mount link,
    support chain, welder coverage under reach.FAMILY[:TIER_A], grinder sphere vs neck / everything else, split per hypothesis, slim counts"""
    f, n = fused_view(bp)
    f.validate()                                                       # overlaps, mounts, ONE piece, exhaust lane free of printer cells
    occ = f.occupancy(); adj = f.attachment(occ)
    P, D = f.blocks[:n], f.blocks[n:]
    pid = {id(b) for b in P}; did = {id(b) for b in D}
    links = [(a, b) for a in P for b in D if id(b) in adj[id(a)]]
    assert [(tuple(a['min']), tuple(b['min'])) for a, b in links] == [(STUB, NECK[0])], f'printer-payload mount links {links}'
    layer, frontier, rnd = {}, set(pid), 0                             # support: welding order from the printer
    while frontier:
        rnd += 1; nxt = set()
        for i in frontier:
            for j in adj[i]:
                if j not in pid and j not in layer: layer[j] = rnd; nxt.add(j)
        frontier = nxt
    assert all(id(b) in layer for b in D), 'payload block never supported'
    welders = [b for b in bp.blocks if b['sub'] == 'SmallShipWelder']
    for h in R.FAMILY[:R.TIER_A]:
        r = R.radius_cells(h)
        for b in bp.payload.blocks:
            assert any(R.reaches(R.welder_tip(bp, w, h.backoff)[0], bp.payload.cells(b), r) for w in welders), f'{h.name}: no welder reaches {b["name"] or b["min"]}'
    _, cen = grinder_centres(bp)
    rmax = max(R.radius_cells(h) for h, _ in cen)
    L = LAYOUTS[getattr(bp, 'layout', 'side')]
    for b in f.blocks:
        if b['sub'] == 'SmallShipGrinder' or (id(b) in did and tuple(b['min']) in NECK): continue
        if (b['name'] or '') in L['collateral']: continue                  # known guarded collateral (see LAYOUTS): checked below
        for c in f.cells(b):
            d = min(R.cell_dist(p, c) for _, p in cen)
            assert d > rmax, f'{b["name"] or b["sub"]} cell {c} is {d:.2f} cells from a grinder sphere centre (r {rmax})'
    def cut_away(cells):                                               # fused view minus the payload neck cells at these cells
        g = copy.deepcopy(f); g.blocks = [b for i, b in enumerate(g.blocks) if not (i >= n and tuple(b['min']) in cells)]
        return g
    def check_split(g, what):                                          # exactly 2 pieces: whole printer (larger) + whole payload cluster
        comps = [{id(b) for b in c} for c in g.components(g.attachment(g.occupancy()))]
        printer = {id(b) for b in g.blocks[:n]}
        clus = {id(b) for b in g.blocks[n:] if tuple(b['min']) not in NECK}
        pr = [c for c in comps if id(g.blocks[0]) in c][0]
        pc = [c for c in comps if c & clus]
        assert printer <= pr, f'{what}: printer split apart'
        assert len(pc) == 1 and clus <= pc[0] and not (pc[0] & printer), f'{what}: payload cluster split apart or still on the printer'
        assert len(comps) == 2, f'{what}: {len(comps)} pieces {sorted(len(c) for c in comps)} (orphan neck cell?)'
        assert len(pr) > len(pc[0]), f'{what}: printer piece {len(pr)} is not larger than the payload {len(pc[0])}: it would lose its grid'
        return len(pr), len(pc[0])
    hit = {b['name'] for b in f.blocks if (b['name'] or '') in L['collateral'] and any(min(R.cell_dist(p, c) for _, p in cen) <= rmax for c in f.cells(b))}
    assert hit <= set(L['collateral']), f'unexpected collateral {hit}'
    for h, p in cen[:L['tier']]:                                       # D1 world: the grinder cuts the FUSED neck (only the hypotheses the layout promises)
        cut = {c for c in NECK if R.cell_dist(p, c) <= R.radius_cells(h)}
        assert cut, f'grinder {h.name}: no neck cell in reach'
        assert NECK[0] not in cut, f'grinder {h.name}: reaches N1'
        check_split(cut_away(cut), f'grinder {h.name}')
    hand = {NECK[HAND_CUT]}                                            # expected world: hand cut of N2, then CUT2 on the payload grid's neck
    check_split(cut_away(hand), 'hand cut N2')
    for h, p in cen[:L['tier']]:
        cut = {c for c in NECK if R.cell_dist(p, c) <= R.radius_cells(h)} | hand
        check_split(cut_away(cut), f'hand cut N2 + CUT2 {h.name}')
    return True

MODELS = {NAME: sprue_test, NAME_NG: lambda lite=False: sprue_test(lite, grinder=False), NAME_CL: lambda lite=False: sprue_test(lite, layout='close')}

if __name__ == '__main__':
    bad = 0
    for m, fn in MODELS.items():
        try:
            bp = fn(False)
            print(f'PASS {m}: {len(bp.blocks)} blocks + {len(bp.payload.blocks)} projected, offset {bp.projection_offset}, guard {len(bp.guard_cells)} cells')
            for k, (cnt, conf) in bp.shape_summary().items():
                if not conf.startswith('seen'): print(f'   GUESSED XML: {k} x{cnt}: {conf}')
                elif k == 'ShipWelder/SmallShipWelder': print(f'   SECONDARY: {k} x{cnt}: {conf} = the LARGE welder sample, pasted in v2.0 only')
        except AssertionError as e:
            print(f'FAIL {m}: {e}'); bad += 1
    sys.exit(1 if bad else 0)
