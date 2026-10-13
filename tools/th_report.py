#!/usr/bin/env python3
"""Summarise enemy-ship fleet studies (sim runs.jsonl from `run --enemies ... --enemy-fleets ...`).

  python3 -I tools/th_report.py LABEL=DIR [LABEL=DIR ...] [--md OUT.md]

Per fleet and mode: mean over seeds of what the drones destroyed on the enemy ship (ship 0): power blocks (reactors, hydrogen
engines, batteries), powered weapons, thrusters, all blocks; whether it was knocked out (not combat capable); drones left.
Also a least-squares split of "power blocks destroyed" into a per-drone contribution of each model (all modes pooled per mode).
"""
import json, sys, collections

def load(d):
    rows = []
    for l in open(d.rstrip('/') + '/runs.jsonl'):
        r = json.loads(l)
        if r.get('outcome') == 'error': rows.append(dict(fleet=r['scenario'].split('-vs-', 1)[1], err=True)); continue
        e = r['ships'][0]; dr = r['ships'][1:]
        rows.append(dict(
            fleet=r['scenario'].split('-vs-', 1)[1], err=False,
            pkill=e['power_blocks_start'] - e['power_blocks_end'],
            akill=e['armed_start'] - e['armed_end'],
            tkill=e['thrusters_start'] - e['thrusters_end'],
            bkill=e['blocks_start'] - e['blocks_end'],
            ko=0 if e['capable'] else 1,
            alive=sum(1 for x in dr if x['capable']), n=len(dr),
            dur=r['duration_s'], models=[x['model'] for x in dr],
            hits=sum(x['hits'] for x in dr)))
    return rows

def mean(v): return sum(v) / len(v) if v else float('nan')

def canon(f): return f.replace('Basilisk Seeker', 'Basilisk')

def main(argv):
    md = None
    if '--md' in argv: i = argv.index('--md'); md = argv[i + 1]; argv = argv[:i] + argv[i + 2:]
    modes = collections.OrderedDict()
    for a in argv:
        lab, d = a.split('=', 1); modes[lab] = load(d)
    fleets = []
    for rows in modes.values():
        for r in rows:
            c = canon(r['fleet'])
            if c not in fleets: fleets.append(c)
    out = []
    hdr = '| Fleet | ' + ' | '.join(f'{m} power / weapons / thrust / blocks / KO / left' for m in modes) + ' |'
    out.append(hdr); out.append('|' + '---|' * (1 + len(modes)))
    agg = {m: collections.defaultdict(list) for m in modes}
    for f in fleets:
        cells = []
        for m, rows in modes.items():
            rs = [r for r in rows if canon(r['fleet']) == f and not r['err']]
            if not rs: cells.append('-'); continue
            for k in ('pkill', 'akill', 'tkill', 'bkill', 'ko', 'alive'): agg[m][k].append(mean([r[k] for r in rs]))
            cells.append(f"{mean([r['pkill'] for r in rs]):.1f} / {mean([r['akill'] for r in rs]):.1f} / {mean([r['tkill'] for r in rs]):.1f} / "
                         f"{mean([r['bkill'] for r in rs]):.0f} / {100*mean([r['ko'] for r in rs]):.0f}% / {mean([r['alive'] for r in rs]):.1f}")
        out.append(f'| {f} | ' + ' | '.join(cells) + ' |')
    out.append('')
    out.append('Per-drone contribution (least squares over all fleets of each mode, no intercept): power blocks / weapons / blocks destroyed per drone of each model')
    for m, rows in modes.items():
        ok = [r for r in rows if not r['err']]
        names = sorted({canon(x) for r in ok for x in r['models']})
        X = [[sum(1 for x in r['models'] if canon(x) == nm) for nm in names] for r in ok]
        res = []
        for key in ('pkill', 'akill', 'bkill'):
            y = [r[key] for r in ok]
            res.append(lstsq(X, y))
        out.append(f'- {m} ({len(ok)} runs, {sum(1 for r in rows if r["err"])} errors): ' + ', '.join(
            f'{nm} {res[0][i]:.2f} / {res[1][i]:.2f} / {res[2][i]:.1f}' for i, nm in enumerate(names)))
    txt = '\n'.join(out)
    print(txt)
    if md: open(md, 'w').write(txt + '\n')

def lstsq(X, y):
    n = len(X[0])
    A = [[sum(X[k][i] * X[k][j] for k in range(len(X))) for j in range(n)] for i in range(n)]
    b = [sum(X[k][i] * y[k] for k in range(len(X))) for i in range(n)]
    # Gaussian elimination
    for c in range(n):
        p = max(range(c, n), key=lambda r: abs(A[r][c]))
        A[c], A[p] = A[p], A[c]; b[c], b[p] = b[p], b[c]
        if abs(A[c][c]) < 1e-12: continue
        for r in range(n):
            if r != c:
                f = A[r][c] / A[c][c]
                for k in range(c, n): A[r][k] -= f * A[c][k]
                b[r] -= f * b[c]
    return [b[i] / A[i][i] if abs(A[i][i]) > 1e-12 else float('nan') for i in range(n)]

if __name__ == '__main__':
    main(sys.argv[1:])
