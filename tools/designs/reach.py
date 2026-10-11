"""Welder reach and bay-contact verification for printer ships (owned by the scripted-printer agent).

WELDER MODEL (SmallShipWelder, 3x3x6 cells = 1.5 x 1.5 x 3.0 m, definition SensorRadius 1.3 m):
  * the welder works toward its block Forward; the sensor sphere is centred on the middle of the TIP face
    (the outer face of the first layer of cells along Forward) and has radius 1.3 m = 2.6 cells.
  * a block is "reachable" when its cell box (cell i spans [i, i+1) in cell units) intersects that sphere.
    Multi-cell blocks count as reachable as soon as ONE of their cells is, which is the conservative reading of the game's
    slim-block bound test (the block's whole box is at least as close as any one cell).
  * a SAFETY MARGIN (default 0.2 m) is subtracted from the radius so that rounding, a tip dummy that sits a bit
    inside the model, or a slightly smaller real radius do not decide the build.
  UNVERIFIED IN GAME: the exact sphere centre (the definition has no SensorOffset for this welder, drills have one), see
  TIP_BACKOFF_CELLS.  A positive value moves the sphere centre back into the welder; run `reach.py --sweep` to see how many
  bays would lose blocks for a given back-off.

BAY CONTACT MODEL (merge-block printing, docs/research.md 1.2): the drone may touch the printer ONLY through the merge pair.
  * no printer cell inside or next to any drone cell (26-neighbourhood) except the printer merge block next to the drone merge block;
  * no printer cell inside the drone's exhaust / muzzle / camera clear zones (bp.reserve of the drone blueprint);
  * the two merge blocks have opposite clamp normals and sit in neighbouring cells;
  * welders of one bay must not reach another bay's drone (they would weld a foreign projection).

A printer Blueprint carries `bp.bays`, a list of dicts written by printer_lib (n, drone, T, merge, projector, welders ...).
`verify_printer(bp)` checks everything and returns a report; `python3 -I tools/designs/reach.py` runs it on every printer.
"""
import sys, os, math
HERE_ = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE_)
sys.path.insert(0, os.path.normpath(os.path.join(HERE_, '..')))
from sebp import DIR

CELL_M = 0.5                      # small grid
WELDER_SUB = "SmallShipWelder"
WELD_RADIUS_M = 1.3
SAFETY_MARGIN_M = 0.2
TIP_BACKOFF_CELLS = float(os.environ.get('SE_WELDER_BACKOFF', '0.0'))   # sphere centre moved this far back from the tip plane (into the welder); rebuild with SE_WELDER_BACKOFF=1 to test a different guess

def eff_radius_cells(margin_m=None):
    return (WELD_RADIUS_M - (SAFETY_MARGIN_M if margin_m is None else margin_m)) / CELL_M

def dvec(name): return DIR[name]

def neighbours26(c):
    x, y, z = c
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                if dx or dy or dz: yield (x + dx, y + dy, z + dz)

# ---------------------------------------------------------------------------------------------
def tip_point_from_cells(cells, f, backoff=None):
    """centre of the tip face of a welder occupying `cells`, working along unit vector f (cell units)"""
    ax = [abs(v) for v in f].index(1); sg = f[ax]
    plane = (max(c[ax] for c in cells) + 1) if sg > 0 else min(c[ax] for c in cells)
    n = len(cells)
    p = [sum(c[k] for c in cells) / n + 0.5 for k in range(3)]
    p[ax] = plane - (TIP_BACKOFF_CELLS if backoff is None else backoff) * sg
    return tuple(p)

def welder_tip(bp, b, backoff=None):
    """(tip point, forward vector) of welder block b of Blueprint bp"""
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

def coverage(bp, bay, margin_m=None, backoff=None):
    """per drone block: list of welder names (of this bay) that reach it"""
    r = eff_radius_cells(margin_m)
    welders = [(w, welder_tip(bp, w, backoff)[0]) for w in bay_welders(bp, bay['n'])]
    res = []
    for b, cells in drone_world(bay['drone'], bay['T']):
        res.append((b, [w['name'] for w, p in welders if reaches(p, cells, r)]))
    return res

def foreign_reach(bp, bays, margin_m=None):
    """welders reaching a drone of another bay: [(welder name, bay n, other bay n, block name)]"""
    r = eff_radius_cells(margin_m); bad = []
    for bay in bays:
        ws = [(w, welder_tip(bp, w)[0]) for w in bay_welders(bp, bay['n'])]
        for other in bays:
            if other is bay: continue
            for b, cells in drone_world(other['drone'], other['T']):
                for w, p in ws:
                    if reaches(p, cells, r): bad.append((w['name'], bay['n'], other['n'], b['name'] or b['sub']))
    return bad

# ---------------------------------------------------------------------------------------------
def printer_cells(bp):
    """cell -> block for the printer blueprint"""
    occ = {}
    for b in bp.blocks:
        for c in bp.cells(b): occ[c] = b
    return occ

def verify_bay(bp, bay, margin_m=None, backoff=None, strict_camera=False):
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
        pair = {id(pm)}
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
    #     in the swept footprint for 14 cells, or the drone is boxed in by its own welders (found by the geometry review)
    des = bay.get('design')
    mbs = [b for b in drone.blocks if b['kind'] == 'merge']
    if des is not None and len(mbs) == 1:
        e = tuple(getattr(des, 'exit', None) or tuple(-v for v in DIR[merge_clamp_dir(drone, mbs[0])]))
        blockers = {}
        for c in dcells:
            for k in range(1, 15):
                o = occ.get((c[0] + e[0] * k, c[1] + e[1] * k, c[2] + e[2] * k))
                if o is not None: blockers[o['name'] or o['sub']] = k
        if blockers: prob.append(f'bay {bay["n"]}: exit lane {e} blocked by ' + ', '.join(f'{n} (step {k})' for n, k in list(blockers.items())[:4]))
    # 5. weld coverage
    cov = coverage(bp, bay, margin_m, backoff)
    miss = [b['name'] or b['sub'] for b, ws in cov if not ws]
    st['welders'] = len(bay_welders(bp, bay['n'])); st['drone_blocks'] = len(cov)
    st['min_cover'] = min((len(ws) for b, ws in cov), default=0)
    if miss: prob.append(f'bay {bay["n"]}: {len(miss)} drone blocks out of weld reach: ' + ', '.join(miss[:6]) + (' ...' if len(miss) > 6 else ''))
    return prob, st

def verify_connections(bp):
    """mount-point test on printer + all drones in one frame: the only printer<->drone connections allowed are the merge pairs, and no
    two drones may connect.  Returns problems."""
    import copy
    from sebp import Blueprint
    chk = Blueprint('chk', 'Small')
    owner = {}
    for b in bp.blocks:
        nb = copy.copy(b); chk.blocks.append(nb); owner[id(nb)] = ('P', None, b)
    for bay in bp.bays:
        T = bay['T']
        for b in bay['drone'].blocks:
            nb = copy.copy(b); nb['min'] = tuple(b['min'][k] + T[k] for k in range(3)); chk.blocks.append(nb); owner[id(nb)] = ('D', bay['n'], b)
    adj = chk.attachment()
    prob = []
    merges = {bay['n']: bay['merge'] for bay in bp.bays}
    byid = {id(b): b for b in chk.blocks}
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
    xml = None
    for bay in bp.bays:
        prob, st = verify_bay(bp, bay, margin_m, backoff)
        lines.append(f'  bay {bay["n"]} ({bay["drone"].name}): {st.get("welders")} welders, {st.get("drone_blocks")} drone blocks, '
                     f'min welders/block {st.get("min_cover")}, welders touching the drone {st.get("weld_touching")}' + (f', camera-zone cells blocked {st["camera_zone_blocked"]}' if st.get('camera_zone_blocked') else ''))
        if prob: ok = False; lines += ['    ' + p for p in prob]
    cp = verify_connections(bp)
    if cp: ok = False; lines += ['  ' + p for p in cp[:8]]
    fr = foreign_reach(bp, bp.bays, margin_m)
    if fr:
        ok = False; lines.append(f'  welders reach another bay: {fr[:3]}')
    names = [b['name'] for b in bp.blocks if b['name']]
    if len(names) != len(set(names)): ok = False; lines.append('  duplicate block names')
    return ok, lines

def main():
    import importlib, printers
    margin = None; backoff = None; sweep = '--sweep' in sys.argv
    bad = 0
    for name, fn in printers.MODELS.items():
        for lite in (False, True):
            label = name + (' Lite' if lite else '')
            try:
                bp = fn(lite)
            except AssertionError as e:
                print(f'FAIL {label}: {e}'); bad += 1; continue
            ok, lines = verify_printer(bp, margin, backoff)
            print(('PASS ' if ok else 'FAIL ') + label); bad += (not ok)
            for l in lines: print(l)
            if sweep:
                for bo in (0.0, 0.5, 1.0, 1.5):
                    ok2, l2 = verify_printer(bp, margin, bo)
                    print(f'    sweep backoff {bo}: {"ok" if ok2 else "MISS"}')
    return 1 if bad else 0

if __name__ == '__main__':
    sys.exit(main())
