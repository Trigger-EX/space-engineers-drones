#!/usr/bin/env python3
"""Design check: how few hits does it take to disable each drone?   python3 -I tools/survivability_audit.py [--check] [--only "Hornet" ...] [--ships] [--summary]

Builds the in-memory blueprints (the same generator calls as tools/build_all.py: model function, add_support_pbs, finalize; Full variants only) and reports, per drone:
  1. critical systems and their blocks: controllers (RemoteControl / cockpit), programmable blocks (flight / staff / thrust PB by name prefix), gyros,
     thrusters grouped by the direction they push (6 axes), power (batteries, reactors, hydrogen engines), guns;
  2. kill sets: a block dies when any one of its cells is hit, so the smallest cell set that disables a system has one cell per block of it.  Systems that
     disable the drone: all controllers, all flight PBs, all gyros, all thrusters of any one axis the drone thrusts on, all power.  Guns are listed (a mission
     kill) but do not count.  Reported: the smallest kill set and its system, and every system with a kill set of one cell (single point of failure, SPOF);
  3. exposure: per critical block and for each of the 6 axis directions, how many occupied cells (every cell of multi-cell blocks, the block's own cells
     excluded) lie between it and the outside of the bounding box; the block's value is the minimum over its cells.  0 = open face, one hit lands on it;
  4. clustering: per system with several blocks, the max pairwise distance, and a flag when one blast (BLAST_RADIUS_M) can reach all of them;
  5. a score 0..100 and a summary table.
Not modelled: structural cut points (a hit that splits the grid), armour as hit points, block health, the actual weapon of the enemy.  Details: docs/design/survivability_audit.md.

--check exits 1 if a drone has a single-cell kill set in a system that is not in WAIVED_SPOF (those are printed as "waived").  --strict waives nothing.
"""
import sys, os, re, argparse
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import designs                                   # noqa: E402  (puts tools/designs on sys.path)
import sebp                                      # noqa: E402
import _common as C                              # noqa: E402

# the drones of the audit (build_all.py model names); --only picks from these, --ships adds the warships / pod models
DRONES = ('Hornet', 'Watcher', 'Harrier', 'Lancer', 'Aegis', 'Gnat', 'Longbow S', 'Longbow L', 'Wasp', 'Mantis', 'Basilisk')

# one blast reaches blocks within this radius of its centre, metres.  A GUESS (a small-grid rocket ~2.5 m, large ~5 m), not measured in game.
BLAST_RADIUS_M = {'Small': 2.5, 'Large': 5.0}
CELL_M = {'Small': 0.5, 'Large': 2.5}

# systems whose single-cell kill set is accepted by --check (still reported).  The controller: a drone carries one RemoteControl by design (a second one is
# fine but not required; with two, the system is no longer a SPOF anyway).  The flight PB: exactly one PB writes the thruster/gyro overrides (CLAUDE.md,
# Wasp Split), so a second flight PB is no remedy.  Everything else (gyros, power, thrusters of an axis) can be doubled and is not waived.
WAIVED_SPOF = ('controller', 'flight PB')

# score = 100 - 15 per unwaived SPOF - 3 per waived SPOF - 5 per clustered system (thruster axes not counted: the drone is small, see diag_m) - 30 * (share of non-thruster critical (block, face) pairs with 0 cover)
SCORE_SPOF, SCORE_WAIVED, SCORE_CLUSTER, SCORE_OPEN = 15, 3, 5, 30

DIRS = C.AXIS_DIRS
SHORT = {'Forward': 'F', 'Backward': 'B', 'Up': 'U', 'Down': 'D', 'Left': 'L', 'Right': 'R'}
CTRL_X = ('RemoteControl', 'Cockpit', 'CryoChamber')
POWER_X = ('BatteryBlock', 'Reactor', 'HydrogenEngine')
GUN_X = re.compile(r'Gun|Missile|Railgun|Turret|Launcher|Warhead')

# ---------------------------------------------------------------------------------------------
# classification
# ---------------------------------------------------------------------------------------------
def xt(b): return b['xtype'].replace('MyObjectBuilder_', '')

def pb_role(b):
    """name prefix [Flight] / [Staff] / [Thrust]; a PB without a prefix is the (only) flight PB"""
    m = re.match(r'\[([^\]]+)\]', b['name'] or '')
    return m.group(1).lower() if m and m.group(1).lower() in ('flight', 'staff', 'thrust') else 'flight'

def systems(bp):
    """{system name: [blocks]}: the critical systems (empty ones left out).  Thruster axes are named 'thrust <dir>' (the direction the ship is pushed)."""
    s = {'controller': [], 'flight PB': [], 'staff PB': [], 'thrust PB': [], 'gyro': [], 'power': [], 'guns': []}
    for d in DIRS: s['thrust ' + d] = []
    for b in bp.blocks:
        x = xt(b)
        if x in CTRL_X: s['controller'].append(b)
        elif x == 'MyProgrammableBlock': s[pb_role(b) + ' PB'].append(b)
        elif x == 'Gyro': s['gyro'].append(b)
        elif x == 'Thrust': s['thrust ' + C.OPP[b['fwd']]].append(b)
        elif x in POWER_X: s['power'].append(b)
        elif 'Flare' in b.get('subtype', ''): continue        # a flare launcher is a SmallMissileLauncher but no gun (survivability item 12)
        elif b['kind'] in ('gun', 'rail', 'turret') or GUN_X.search(x): s['guns'].append(b)
    return {k: v for k, v in s.items() if v}

def is_kill_system(name):
    """systems whose loss disables the drone (guns and the staff / thrust PBs do not: it still flies)"""
    return name in ('controller', 'flight PB', 'gyro', 'power') or is_axis(name)

def is_axis(name): return name.startswith('thrust ') and name[7:] in DIRS

# ---------------------------------------------------------------------------------------------
# geometry
# ---------------------------------------------------------------------------------------------
def centre_m(bp, b):
    cs = bp.cells(b); k = CELL_M[bp.grid]
    return tuple((sum(c[i] for c in cs) / len(cs) + 0.5) * k for i in range(3))

def dist(a, b): return sum((x - y) ** 2 for x, y in zip(a, b)) ** 0.5

def cover(bp, occ, box, b):
    """{direction: occupied cells between the block and the outside of the bounding box} (minimum over the block's cells, own cells not counted)"""
    out = {}
    for d in DIRS:
        v = sebp.DIR[d]; best = None
        for c in bp.cells(b):
            n = 0; p = tuple(c[i] + v[i] for i in range(3))
            while all(box[i][0] <= p[i] <= box[i][1] for i in range(3)):
                if p in occ and occ[p] is not b: n += 1
                p = tuple(p[i] + v[i] for i in range(3))
            best = n if best is None else min(best, n)
        out[d] = best
    return out

def blast_cover(points, r):
    """smallest max-distance from a candidate centre (a block, a pair midpoint, the centroid) to all points: <= r means one blast can take every one"""
    if len(points) < 2: return 0.0
    cand = list(points) + [tuple((a[i] + b[i]) / 2 for i in range(3)) for j, a in enumerate(points) for b in points[j + 1:]]
    cand.append(tuple(sum(p[i] for p in points) / len(points) for i in range(3)))
    return min(max(dist(c, p) for p in points) for c in cand)

# ---------------------------------------------------------------------------------------------
# audit of one blueprint
# ---------------------------------------------------------------------------------------------
def label(b): return b['name'] or b['sub'] or xt(b)

def audit(bp):
    """-> dict(systems, kill, spofs, exposure, cluster, open_share, score, ...) for a validated blueprint (main grid only)"""
    sysm = systems(bp)
    occ = bp.occupancy(allow_overlap=True)
    cells = list(occ)
    box = [(min(c[i] for c in cells), max(c[i] for c in cells)) for i in range(3)]
    r = dict(systems=sysm, box=box)
    r['diag_m'] = dist((0, 0, 0), tuple((box[i][1] - box[i][0] + 1) * CELL_M[bp.grid] for i in range(3)))   # bounding-box diagonal: a drone shorter than 2 blast radii dies to one hit
    # kill sets: one cell per block, so the size is the block count; SPOF cells are every cell of a lone block
    kills = {n: len(v) for n, v in sysm.items() if is_kill_system(n)}
    for need in ('controller', 'flight PB', 'gyro', 'power'):
        kills.setdefault(need, 0)              # no such block at all: the drone is dead from the start (kill set of size 0), always shown
    r['kills'] = kills
    live = {n: k for n, k in kills.items() if k > 0}
    r['min_kill'] = min(live.values()) if live else 0
    r['min_systems'] = sorted(n for n, k in live.items() if k == r['min_kill'])
    r['missing'] = sorted(n for n, k in kills.items() if k == 0)
    r['spofs'] = [(n, sysm[n][0], bp.cells(sysm[n][0])) for n in sorted(live) if live[n] == 1]
    # exposure
    expo = []
    for n, bl in sysm.items():
        if n == 'guns': continue
        for b in bl:
            cv = cover(bp, occ, box, b); v = list(cv.values())
            expo.append(dict(system=n, block=b, cover=cv, min=min(v), mean=sum(v) / 6, open=[d for d in DIRS if cv[d] == 0]))
    r['exposure'] = expo
    nt = [e for e in expo if not is_axis(e['system'])]      # thrusters are open to space by design (the exhaust must be clear)
    r['open_share'] = sum(len(e['open']) for e in nt) / (6 * len(nt)) if nt else 0.0
    r['uncovered'] = [e for e in nt if e['open']]
    # clustering
    rad = BLAST_RADIUS_M[bp.grid]; cl = []
    for n, bl in sysm.items():
        if len(bl) < 2 or n == 'guns': continue
        pts = [centre_m(bp, b) for b in bl]
        far = max(dist(a, b) for i, a in enumerate(pts) for b in pts[i + 1:])
        reach = blast_cover(pts, rad)
        cl.append(dict(system=n, n=len(bl), max_dist=far, reach=reach, clustered=reach <= rad and is_kill_system(n)))
    r['cluster'] = cl
    clustered = [c for c in cl if c['clustered'] and not is_axis(c['system'])]
    r['thr_clustered'] = [c['system'] for c in cl if c['clustered'] and is_axis(c['system'])]
    waived = [s for s in r['spofs'] if s[0] in WAIVED_SPOF]
    r['unwaived'] = [s for s in r['spofs'] if s[0] not in WAIVED_SPOF]
    r['waived'] = waived
    r['clustered'] = clustered
    score = (100 - SCORE_SPOF * len(r['unwaived']) - SCORE_WAIVED * len(waived) - SCORE_CLUSTER * len(clustered) - SCORE_OPEN * r['open_share']
             - 100 * len(r['missing']))
    r['score'] = max(0, round(score))
    return r

# ---------------------------------------------------------------------------------------------
# building and printing
# ---------------------------------------------------------------------------------------------
def build(mods, kind, mod, model, fn):
    bp = fn(False)                                  # Full variant (Lite is retired)
    C.add_support_pbs(bp)
    C.finalize(bp)
    return bp

def models(mods, only, ships):
    want = set(only) if only else set(DRONES)
    for kind, mod, model, fn in designs.iter_models(mods):
        if kind == 'ship' and (ships and (not only or model in want)): yield kind, mod, model, fn
        elif kind == 'drone' and model in want: yield kind, mod, model, fn

def cell_str(b, cs): return f'{label(b)} @{min(cs)}' + (f' (+{len(cs) - 1} cells)' if len(cs) > 1 else '')

def detail(name, bp, a):
    out = [f'== {name} ({bp.grid} grid, {len(bp.blocks)} blocks, box diagonal {a["diag_m"]:.1f} m, score {a["score"]})']
    sysm = a['systems']
    for n, bl in sysm.items():
        k = f'kill set {a["kills"][n]}' if n in a['kills'] else 'mission kill' if n == 'guns' else 'support'
        out.append(f'  {n:<16}{len(bl):>3} block(s)  {k}')
    if a['missing']: out.append(f'  MISSING systems (dead from the start): {", ".join(a["missing"])}')
    out.append(f'  smallest kill set: {a["min_kill"]} cell(s): {", ".join(a["min_systems"])}')
    for n, b, cs in a['spofs']:
        out.append(f'  SPOF ({"waived" if n in WAIVED_SPOF else "FAIL"}): {n}: {cell_str(b, cs)}')
    for e in a['exposure']:
        if is_axis(e['system']): continue
        cv = ' '.join(f'{SHORT[d]}{e["cover"][d]}' for d in DIRS)
        out.append(f'  cover {e["system"]:<10} {label(e["block"])[:28]:<28} {cv}  min {e["min"]} mean {e["mean"]:.1f}' + ('  OPEN ' + ''.join(SHORT[d] for d in e['open']) if e['open'] else ''))
    th = [e for e in a['exposure'] if is_axis(e['system'])]
    if th: out.append(f'  thrusters ({len(th)}): mean cover {sum(e["mean"] for e in th) / len(th):.1f} (open to space by design)')
    for c in a['cluster']:
        out.append(f'  cluster {c["system"]:<14} {c["n"]} blocks, max distance {c["max_dist"]:.1f} m, one blast radius {c["reach"]:.1f} m' + ('  CLUSTERED' if c['clustered'] else ''))
    if a['diag_m'] <= 2 * BLAST_RADIUS_M[bp.grid]: out.append(f'  whole drone fits in one blast ({a["diag_m"]:.1f} m <= {2 * BLAST_RADIUS_M[bp.grid]:.1f} m)')
    return '\n'.join(out)

def summary(rows):
    out = ['| Drone | Score | Min kill | Weakest system | SPOFs (unwaived) | Open critical faces | Clustered | Thruster axes in one blast | Diag m |', '|---|--:|--:|---|---|--:|---|--:|--:|']
    for name, bp, a in sorted(rows, key=lambda r: r[2]['score']):
        sp = ', '.join(n for n, b, cs in a['spofs']) or '-'
        un = ', '.join(n for n, b, cs in a['unwaived']) or 'none'
        out.append(f'| {name} | {a["score"]} | {a["min_kill"]} | {", ".join(a["min_systems"]) or "-"} | {sp} ({un}) | {a["open_share"] * 100:.0f}% | '
                   f'{", ".join(c["system"] for c in a["clustered"]) or "-"} | '
                   f'{len(a["thr_clustered"])} | {a["diag_m"]:.1f} |')
    return '\n'.join(out)

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--only', nargs='+', metavar='NAME', help='model names (default: the drones of DRONES)')
    ap.add_argument('--ships', action='store_true', help='also the warships and the pod')
    ap.add_argument('--summary', action='store_true', help='only the table and the worst findings')
    ap.add_argument('--check', action='store_true', help='exit 1 if a drone has a single-cell kill set outside WAIVED_SPOF')
    ap.add_argument('--strict', action='store_true', help='with --check: waive nothing')
    a = ap.parse_args(argv)
    mods, _ = designs.load()
    rows = []
    for kind, mod, model, fn in models(mods, a.only, a.ships):
        bp = build(mods, kind, mod, model, fn)
        res = audit(bp)
        rows.append((model, bp, res))
        if not a.summary: print(detail(model, bp, res)); print()
    if not rows: print('no model selected'); return 1
    print(summary(rows)); print()
    bad = []
    for name, bp, res in rows:
        for n, b, cs in (res['spofs'] if a.strict else res['unwaived']): bad.append(f'{name}: {n}: {cell_str(b, cs)}')
        for n in res['missing']: bad.append(f'{name}: no {n} at all')
    print('single points of failure:', *(bad or ['none (outside the waived systems)']), sep='\n  ')
    return 1 if a.check and bad else 0

if __name__ == '__main__':
    sys.exit(main())
