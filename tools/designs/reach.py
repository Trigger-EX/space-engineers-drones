"""Welder reach, build-order support and bay-contact verification for printer ships.

WELDER GEOMETRY: WHAT IS KNOWN AND WHAT IS ASSUMED  (details and sources in docs/welder-reach.md)
  KNOWN (ref/CubeBlocks/CubeBlocks_Tools.sbc, game definitions):
    * SmallShipWelder is 3x3x6 cells (1.5 x 1.5 x 3.0 m), SensorRadius 1.3 m, NO SensorOffset element (the drills have one, 0.8 / 2.8).
    * Mount points exist on Left/Right/Top/Bottom (partial, mostly the rear half) and Back; none on Front.  So the tool end (tip) is Forward.
  KNOWN (wiki https://spaceengineers.wiki.gg/wiki/Modding/Reference/Blocks/ShipWelder): the model needs a dummy whose name contains
    "detector_shiptool" (first match wins) and that dummy "defines the center of the work sphere".
  KNOWN (Keen forum, "ship tool SensorOffset is not in meters"): SensorOffset is scaled by the dummy size (a welder with offset 1 gave about 2.5 m).
    Irrelevant for the small welder (no offset) except that the dummy matrix may be scaled too.
  NOT KNOWN: where the detector_shiptool dummy sits inside Welder.mwm (no source states it; the .mwm is not in the repo), the exact radius
    behaviour (the game tests the sphere against each projected block's cell box, which is what this module does).
  The user's in-game tests of the foundries show blocks left unwelded that the old model (centre = tip face, r = 1.3 - 0.2 m) called reachable,
  so the old single guess was wrong or too tight.

ROBUST MODEL: instead of one guess every drone block must be reached by at least one welder under EVERY hypothesis of a family
  (FAMILY): sphere centre on the welder axis at the tip face, 1, 2 and 3 cells (0.5 m each) inside the welder (3 cells = middle of the body),
  radius 1.3 m with no margin and with a 0.3 m margin (r = 1.0 m).  A block is reached when its cell box intersects the sphere (multi-cell blocks:
  any cell).  RED_HYP (tip centre, 0.3 m margin) must additionally be reached by TWO different welders (redundancy against a welder that is
  switched off early).  Blocks no legal welder position can reach under a hypothesis are recorded as `design.infeasible` and listed.

BUILD ORDER: a projected block can only be welded when it is supported, i.e. attached through mount points (sebp.attachment, the game's
  CheckMountPointsForSide rule) to a block that already exists.  `support_report` starts from the printer (merge pair) and propagates through
  the drone; a drone block that is never supported fails the design.

BAY CONTACT MODEL (merge-block printing, docs/research.md 1.2): the drone may touch the printer ONLY through the merge pair.
  * no printer cell inside or next to any drone cell (26-neighbourhood) except the printer merge block next to the drone merge block;
  * no printer cell inside the drone's exhaust / muzzle / camera clear zones (bp.reserve of the drone blueprint);
  * the two merge blocks have opposite clamp normals and sit in neighbouring cells;
  * welders of one bay must not reach another bay's drone under ANY hypothesis (they would weld a foreign projection).

A printer Blueprint carries `bp.bays`, a list of dicts written by printer_lib (n, drone, T, merge, projector, welders ...).
`verify_printer(bp)` checks everything and returns a report; `python3 -I tools/designs/reach.py [--table]` runs it on every printer.
"""
import sys, os, math
from collections import namedtuple
HERE_ = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE_)
sys.path.insert(0, os.path.normpath(os.path.join(HERE_, '..')))
from sebp import DIR

CELL_M = 0.5                      # small grid
WELDER_SUB = "SmallShipWelder"
WELD_RADIUS_M = 1.3
SAFETY_MARGIN_M = 0.2             # legacy single-hypothesis margin (still the default of eff_radius_cells / verify_printer(margin_m=...))

# ---------------------------------------------------------------------------------------------
# hypothesis family
# ---------------------------------------------------------------------------------------------
Hyp = namedtuple('Hyp', 'name backoff margin')           # backoff: cells the sphere centre sits inside the welder behind the tip face
def _mk(pos, backoff, margin): return Hyp(f'{pos} r{WELD_RADIUS_M - margin:.1f}', backoff, margin)
FAMILY = tuple(_mk(pos, bo, mg) for pos, bo in (('tip', 0), ('tip-1', 1), ('tip-2', 2), ('mid', 3)) for mg in (0.0, 0.3))
RED_HYP = FAMILY[1]                                      # tip centre, r = 1.0 m: needs TWO welders per block
LEGACY_HYP = Hyp('legacy', 0, SAFETY_MARGIN_M)
HYP_NAMES = [h.name for h in FAMILY]

def radius_cells(h): return (WELD_RADIUS_M - h.margin) / CELL_M

def eff_radius_cells(margin_m=None):
    return (WELD_RADIUS_M - (SAFETY_MARGIN_M if margin_m is None else margin_m)) / CELL_M

def dvec(name): return DIR[name]

def block_label(b):
    """unique, human readable id of a block: its name or subtype plus its Min cell in the drone frame"""
    return (b['name'] or b['sub']) + ('' if b['name'] else '@' + ','.join(str(v) for v in b['min']))

TIER_A = 4                     # FAMILY[:TIER_A] (tip and tip-1 centres, both radii) must hold wherever a legal welder position can do it
EXIT_CHECK = 18                 # cells of exit lane / nose lane the verifier walks (printer_lib.EXIT_LEN and FRONT_LEN)
FRONT_CHECK = 16

def neighbours26(c):
    x, y, z = c
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                if dx or dy or dz: yield (x + dx, y + dy, z + dz)

# ---------------------------------------------------------------------------------------------
def tip_point_from_cells(cells, f, backoff=0.0):
    """sphere centre for a welder occupying `cells`, working along unit vector f (cell units): the middle of the tip face, moved `backoff`
    cells back into the welder"""
    ax = [abs(v) for v in f].index(1); sg = f[ax]
    plane = (max(c[ax] for c in cells) + 1) if sg > 0 else min(c[ax] for c in cells)
    n = len(cells)
    p = [sum(c[k] for c in cells) / n + 0.5 for k in range(3)]
    p[ax] = plane - backoff * sg
    return tuple(p)

def welder_tip(bp, b, backoff=0.0):
    """(sphere centre, forward vector) of welder block b of Blueprint bp"""
    f = DIR[b['fwd']]
    return tip_point_from_cells(bp.cells(b), f, backoff), f

def cell_dist(p, c):
    """distance (cell units) from point p to the box of cell c"""
    d2 = 0.0
    for k in range(3):
        lo, hi = c[k], c[k] + 1
        v = lo - p[k] if p[k] < lo else (p[k] - hi if p[k] > hi else 0.0)
        d2 += v * v
    return math.sqrt(d2)

def reaches(p, cells, r):
    return any(cell_dist(p, c) <= r for c in cells)

# ---------------------------------------------------------------------------------------------
def drone_world(drone, T):
    """cells of every drone block translated into printer coordinates: [(block, [cells])]"""
    out = []
    for b in drone.blocks:
        out.append((b, [(c[0] + T[0], c[1] + T[1], c[2] + T[2]) for c in drone.cells(b)]))
    return out

def bay_welders(bp, n):
    tag = f'[Bay {n}]'
    return [b for b in bp.blocks if b['sub'] == WELDER_SUB and tag in (b['name'] or '')]

def coverage(bp, bay, margin_m=None, backoff=None, hyp=None):
    """per drone block: list of welder names (of this bay) that reach it.  hyp (a Hyp) wins over margin_m / backoff."""
    if hyp is None: hyp = Hyp('x', backoff or 0.0, SAFETY_MARGIN_M if margin_m is None else margin_m)
    r = radius_cells(hyp)
    welders = [(w, welder_tip(bp, w, hyp.backoff)[0]) for w in bay_welders(bp, bay['n'])]
    res = []
    for b, cells in drone_world(bay['drone'], bay['T']):
        res.append((b, [w['name'] for w, p in welders if reaches(p, cells, r)]))
    return res

def family_report(bp, bay):
    """{hyp name: [uncovered block names]} for FAMILY plus 'redundancy': blocks that fewer than two welders reach under RED_HYP"""
    out = {}
    for h in FAMILY:
        out[h.name] = [block_label(b) for b, ws in coverage(bp, bay, hyp=h) if not ws]
    out['redundancy'] = [block_label(b) for b, ws in coverage(bp, bay, hyp=RED_HYP) if len(ws) < 2]
    return out

def foreign_reach(bp, bays, margin_m=None):
    """welders reaching a drone of another bay under ANY hypothesis without margin: [(welder name, bay n, other bay n, block name)]"""
    bad = []
    hyps = [h for h in FAMILY if h.margin == 0.0]
    for bay in bays:
        ws = [(w, [welder_tip(bp, w, h.backoff)[0] for h in hyps]) for w in bay_welders(bp, bay['n'])]
        for other in bays:
            if other is bay: continue
            for b, cells in drone_world(other['drone'], other['T']):
                for w, pts in ws:
                    if any(reaches(p, cells, radius_cells(h)) for p, h in zip(pts, hyps)): bad.append((w['name'], bay['n'], other['n'], b['name'] or b['sub']))
    return bad

# ---------------------------------------------------------------------------------------------
def printer_cells(bp):
    """cell -> block for the printer blueprint"""
    occ = {}
    for b in bp.blocks:
        for c in bp.cells(b): occ[c] = b
    return occ

def combined_graph(bp):
    """printer + all drones (translated) in one Blueprint; returns (chk, owner, adj) with owner[id(copy)] = ('P'|'D', bay n or None, original block)"""
    import copy
    from sebp import Blueprint
    chk = Blueprint('chk', 'Small', legacy_shapes=True)
    owner = {}
    for b in bp.blocks:
        nb = copy.copy(b); chk.blocks.append(nb); owner[id(nb)] = ('P', None, b)
    for bay in bp.bays:
        T = bay['T']
        for b in bay['drone'].blocks:
            nb = copy.copy(b); nb['min'] = tuple(b['min'][k] + T[k] for k in range(3)); chk.blocks.append(nb); owner[id(nb)] = ('D', bay['n'], b)
    return chk, owner, chk.attachment()

def support_report(bp, graph=None):
    """BUILD ORDER: simulate support propagation.  Every printer block exists; a projected drone block can be welded once it is attached by mount
    points to an existing block.  Returns {bay n: (unsupported block names, rounds)}; a drone block that is never supported cannot be welded."""
    chk, owner, adj = graph or combined_graph(bp)
    have = {i for i, o in owner.items() if o[0] == 'P'}
    layer = {i: 0 for i in have}
    frontier = set(have); rnd = 0
    while frontier:
        rnd += 1; nxt = set()
        for i in frontier:
            for j in adj[i]:
                if j not in layer and owner[j][0] == 'D': layer[j] = rnd; nxt.add(j)
        frontier = nxt
    res = {}
    for bay in bp.bays:
        ids = [i for i, o in owner.items() if o[0] == 'D' and o[1] == bay['n']]
        res[bay['n']] = ([owner[i][2]['name'] or owner[i][2]['sub'] for i in ids if i not in layer], max([layer[i] for i in ids if i in layer] or [0]))
    return res

def verify_bay(bp, bay, margin_m=None, backoff=None, strict_camera=False, family=True, support=None):
    """returns (problems list, stats dict)"""
    from _common import merge_clamp_dir
    prob = []; st = {}
    occ = printer_cells(bp)
    drone = bay['drone']; T = bay['T']
    dcells = {}
    for b, cells in drone_world(drone, T):
        for c in cells: dcells[c] = b
    # 1. overlap
    for c in dcells:
        if c in occ: prob.append(f'bay {bay["n"]}: drone cell {c} ({dcells[c]["name"]}) overlaps printer block {occ[c]["name"] or occ[c]["sub"]}')
    # 2. merge pair
    pm = bay['merge']
    dm = [b for b in drone.blocks if b['kind'] == 'merge']
    if len(dm) != 1: prob.append(f'bay {bay["n"]}: drone has {len(dm)} merge blocks')
    else:
        dm = dm[0]
        dc = drone.cells(dm)[0]; dc = (dc[0] + T[0], dc[1] + T[1], dc[2] + T[2])
        pc = bp.cells(pm)[0]
        d1 = DIR[merge_clamp_dir(drone, dm)]; d2 = DIR[merge_clamp_dir(bp, pm)]
        if tuple(a + b for a, b in zip(d1, d2)) != (0, 0, 0): prob.append(f'bay {bay["n"]}: merge clamp faces do not oppose ({d1} vs {d2})')
        if tuple(pc[k] - dc[k] for k in range(3)) != d1: prob.append(f'bay {bay["n"]}: printer merge {pc} is not the cell the drone merge {dc} clamps toward {d1}')
        # 3. contact: printer blocks other than welders must keep out of the drone's 26-neighbourhood; welder tips may touch
        #    (their tip face has no mount point) but see verify_connections() for the real mount-point test
        touch = []; weld_touch = 0
        for c, b in dcells.items():
            for nb in neighbours26(c):
                o = occ.get(nb)
                if o is None or o is pm: continue
                if o['sub'] == WELDER_SUB: weld_touch += 1; continue
                touch.append((c, nb, b['name'] or b['sub'], o['name'] or o['sub']))
        st['weld_touch_cells'] = weld_touch
        tw = {o['name'] for c, b in dcells.items() for nb in neighbours26(c) for o in [occ.get(nb)] if o is not None and o['sub'] == WELDER_SUB}
        st['weld_touching'] = len(tw)
        for c, nb, dn, pn in touch[:6]:
            prob.append(f'bay {bay["n"]}: drone {dn} {c} touches printer {pn} {nb}')
        if len(touch) > 6: prob.append(f'bay {bay["n"]}: ... {len(touch) - 6} more contacts')
        for c, b in dcells.items():
            for k in range(3):
                for s_ in (-1, 1):
                    nb = list(c); nb[k] += s_; nb = tuple(nb)
                    if nb in occ and occ[nb] is pm and b is not dm: prob.append(f'bay {bay["n"]}: {b["name"]} touches the printer merge block')
    # 4. clear zones
    for c, why in drone.reserve.items():
        w = (c[0] + T[0], c[1] + T[1], c[2] + T[2])
        o = occ.get(w)
        if o is None: continue
        if why.startswith('camera') and not strict_camera:
            st['camera_zone_blocked'] = st.get('camera_zone_blocked', 0) + 1
            continue
        prob.append(f'bay {bay["n"]}: {why} blocked by printer {o["name"] or o["sub"]} at {w}')
    # 4b. exit lane: the drone leaves along bay['design'].exit (default: straight away from the merge block); nothing of the printer may stand
    #     in the swept footprint for EXIT_CHECK cells, or the drone is boxed in by its own welders (found by the geometry review)
    des = bay.get('design')
    mbs = [b for b in drone.blocks if b['kind'] == 'merge']
    if des is not None and len(mbs) == 1:
        e = tuple(getattr(des, 'exit', None) or tuple(-v for v in DIR[merge_clamp_dir(drone, mbs[0])]))
        blockers = {}
        for c in dcells:
            for k in range(1, EXIT_CHECK + 1):
                o = occ.get((c[0] + e[0] * k, c[1] + e[1] * k, c[2] + e[2] * k))
                if o is not None: blockers[o['name'] or o['sub']] = k
        if blockers: prob.append(f'bay {bay["n"]}: exit lane {e} blocked by ' + ', '.join(f'{n} (step {k})' for n, k in list(blockers.items())[:4]))
        # nose lane: the drone's own Forward (printer_lib.front_prism).  A problem when the bay was solved with it, a note when the solver had to give it up
        fv = DIR['Forward']
        if tuple(des.c) != fv:
            fb = {}
            for c in dcells:
                for k in range(1, FRONT_CHECK + 1):
                    o = occ.get((c[0] + fv[0] * k, c[1] + fv[1] * k, c[2] + fv[2] * k))
                    if o is not None: fb[o['name'] or o['sub']] = k
            if fb and getattr(des, 'front', False):
                prob.append(f'bay {bay["n"]}: nose lane blocked by ' + ', '.join(f'{n} (step {k})' for n, k in list(fb.items())[:4]))
            elif fb: st['nose_lane_blocked'] = len([n for n in fb if 'Welder' in n])
    # 5. weld coverage
    st['welders'] = len(bay_welders(bp, bay['n'])); st['drone_blocks'] = len(drone.blocks)
    if family and margin_m is None and backoff is None:
        fr = family_report(bp, bay)
        imp = getattr(des, 'impossible', None) or {}                # {hyp name: set(block labels)} that NO legal welder position can reach
        conf = getattr(des, 'conflict', None) or {}                 # tier-A pairs reachable alone but not together with the other welders (blocking)
        st['conflict'] = {k: sorted(v) for k, v in conf.items() if v}
        st['family'] = {k: len(v) for k, v in fr.items()}
        st['impossible'] = {k: len(set(v) & set(imp.get(k, ()))) for k, v in fr.items()}
        tier_a = [h.name for h in FAMILY[:TIER_A]]
        for k in tier_a:                                               # tier A: fail unless physically impossible (listed in the report)
            real = [n for n in fr[k] if n not in set(imp.get(k, ())) and n not in set(conf.get(k, ()))]
            if real: prob.append(f'bay {bay["n"]}: {len(real)} drone blocks not covered under {k}: ' + ', '.join(real[:5]) + (' ...' if len(real) > 5 else ''))
        st['partial'] = {k: len(v) for k, v in fr.items() if k not in tier_a and v}      # best-effort hypotheses: reported in the table, not a failure
        cov = coverage(bp, bay, hyp=RED_HYP)
        st['min_cover'] = min((len(ws) for b, ws in cov), default=0)
    else:
        cov = coverage(bp, bay, margin_m, backoff)
        miss = [b['name'] or b['sub'] for b, ws in cov if not ws]
        st['min_cover'] = min((len(ws) for b, ws in cov), default=0)
        st['family'] = {'single': len(miss)}
        if miss: prob.append(f'bay {bay["n"]}: {len(miss)} drone blocks out of weld reach: ' + ', '.join(miss[:6]) + (' ...' if len(miss) > 6 else ''))
    # 6. build order
    if support is not None and bay['n'] in support:
        miss, rounds = support[bay['n']]
        st['support_rounds'] = rounds; st['unsupported'] = len(miss)
        if miss: prob.append(f'bay {bay["n"]}: {len(miss)} drone blocks are never supported (no mount-point chain from the merge pair): ' + ', '.join(miss[:6]))
    return prob, st

def verify_connections(bp, graph=None):
    """mount-point test on printer + all drones in one frame: the only printer<->drone connections allowed are the merge pairs, and no
    two drones may connect.  Returns problems."""
    chk, owner, adj = graph or combined_graph(bp)
    prob = []
    merges = {bay['n']: bay['merge'] for bay in bp.bays}
    seen = set()
    for i, nbrs in adj.items():
        for j in nbrs:
            if (j, i) in seen: continue
            seen.add((i, j))
            ki, nbay, bi = owner[i]; kj, nbay2, bj = owner[j]
            if ki == kj == 'P': continue
            if ki == kj == 'D' and nbay == nbay2: continue
            if {ki, kj} == {'P', 'D'}:
                n = nbay or nbay2
                pb_ = bi if ki == 'P' else bj; db_ = bj if ki == 'P' else bi
                if pb_ is merges[n] and db_['kind'] == 'merge': continue
                prob.append(f'mount-point connection printer {pb_["name"] or pb_["sub"]} <-> drone[{n}] {db_["name"] or db_["sub"]}')
            else:
                prob.append(f'drones of bay {nbay} and {nbay2} connect')
    return prob

def verify_printer(bp, margin_m=None, backoff=None):
    """full printer check: sebp validation + every bay. Returns (ok, [lines])"""
    lines = []; ok = True
    try:
        bp.validate()
    except AssertionError as e:
        return False, [f'validate: {e}']
    for need in ('bays',):
        if not hasattr(bp, need): return False, ['not a printer blueprint (no .bays)']
    if bp.dlc_used(): ok = False; lines.append(f'DLC blocks {bp.dlc_used()}')
    graph = combined_graph(bp)
    sup = support_report(bp, graph)
    for bay in bp.bays:
        prob, st = verify_bay(bp, bay, margin_m, backoff, support=sup)
        fam = st.get('family', {})
        lines.append(f'  bay {bay["n"]} ({bay["drone"].name}): {st.get("welders")} welders, {st.get("drone_blocks")} drone blocks, '
                     f'min welders/block {st.get("min_cover")}, welders touching the drone {st.get("weld_touching")}, support rounds {st.get("support_rounds")}'
                     + (f', camera-zone cells blocked {st["camera_zone_blocked"]}' if st.get('camera_zone_blocked') else '')
                     + (f', tier-A pairs lost to welder conflicts (waived): {st["conflict"]}' if st.get('conflict') else ''))
        if prob: ok = False; lines += ['    ' + p for p in prob]
    cp = verify_connections(bp, graph)
    if cp: ok = False; lines += ['  ' + p for p in cp[:8]]
    fr = foreign_reach(bp, bp.bays, margin_m)
    if fr:
        ok = False; lines.append(f'  welders reach another bay: {fr[:3]}')
    names = [b['name'] for b in bp.blocks if b['name']]
    if len(names) != len(set(names)): ok = False; lines.append('  duplicate block names')
    return ok, lines

def table_rows(bp, label):
    """one row per bay: [printer, bay, drone, welders, blocks, uncovered per FAMILY hypothesis..., redundancy shortfall, unsupported]"""
    sup = support_report(bp)
    rows = []
    for bay in bp.bays:
        fr = family_report(bp, bay)
        rows.append([label, bay['n'], bay['drone'].name, len(bay_welders(bp, bay['n'])), len(bay['drone'].blocks)]
                    + [len(fr[h]) for h in HYP_NAMES] + [len(fr['redundancy']), len(sup[bay['n']][0])])
    return rows

def print_table(rows):
    hdr = ['printer', 'bay', 'drone', 'weld', 'blocks'] + HYP_NAMES + ['<2 welders', 'unsupp']
    w = [max(len(str(r[i])) for r in rows + [hdr]) for i in range(len(hdr))]
    print('uncovered drone blocks under each sphere-centre hypothesis (0 everywhere = robust):')
    print(' '.join(str(h).ljust(w[i]) for i, h in enumerate(hdr)))
    for r in rows: print(' '.join(str(v).ljust(w[i]) for i, v in enumerate(r)))

def main():
    import printers
    names = [a for a in sys.argv[1:] if not a.startswith('--')]
    table = '--table' in sys.argv
    bad = 0; rows = []
    for name, fn in printers.MODELS.items():
        if names and name not in names: continue
        for lite in (False, True):
            label = name + (' Lite' if lite else '')
            try:
                bp = fn(lite)
            except AssertionError as e:
                print(f'FAIL {label}: {e}'); bad += 1; continue
            ok, lines = verify_printer(bp)
            print(('PASS ' if ok else 'FAIL ') + label); bad += (not ok)
            for l in lines: print(l)
            if table: rows += table_rows(bp, label)
    if table: print_table(rows)
    return 1 if bad else 0

if __name__ == '__main__':
    sys.exit(main())
