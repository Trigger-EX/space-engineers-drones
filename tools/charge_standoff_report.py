#!/usr/bin/env python3
"""Summarises the charge test (pc/plans/charge_standoff.plan) into one table per design, standoff by standoff.

  python3 -I tools/charge_standoff_report.py [pc/results] [--csv charge_standoff.csv]

Reads <results>/charge-<design>-<standoff|default>/runs.jsonl. The drone is the ship that is not the Tech Hunter.
  surv%   fights where the drone was still capable (flying and armed) at the end      hit%   fights where it took any damage
  dmgin   mean damage it took (Tech Hunter turrets)   lost%  mean share of its blocks lost   dist  mean closest approach (m)
  dmgout  mean damage it dealt   t_dis  mean time it was disabled (s, over the fights where it was)
Best = highest surv%, then lowest dmgin, then highest dmgout (a standoff that never fires is not better than one that does).
Paste the output back; the 'best' line is where the standoff should go, 'default' is what the script ships with.
"""
import json, os, sys, glob, argparse, csv

def load(path):
    rows = []
    for l in open(path):
        l = l.strip()
        if l: rows.append(json.loads(l))
    return rows

def drone(run):
    for s in run['ships']:
        if s['model'] != 'Tech Hunter': return s
    return None

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('results', nargs='?', default='pc/results'); ap.add_argument('--csv')
    a = ap.parse_args()
    groups = {}
    for d in sorted(glob.glob(os.path.join(a.results, 'charge-*'))):
        f = os.path.join(d, 'runs.jsonl')
        if not os.path.exists(f): continue
        name = os.path.basename(d)[len('charge-'):]
        design, tag = name.rsplit('-', 1)
        runs = [r for r in load(f) if r.get('outcome') != 'error']
        ds = [drone(r) for r in runs]; ds = [x for x in ds if x]
        if not ds: continue
        n = len(ds)
        dis = [x['disabled_at_s'] for x in ds if x['disabled_at_s'] >= 0]
        groups.setdefault(design, []).append(dict(
            tag=tag, n=n, surv=100.0 * sum(1 for x in ds if x['capable']) / n, hit=100.0 * sum(1 for x in ds if x['damage_taken'] > 0) / n,
            dmgin=sum(x['damage_taken'] for x in ds) / n, lost=100.0 * sum(x['blocks_lost'] / max(1, x['blocks_start']) for x in ds) / n,
            dist=sum(x['min_enemy_dist_m'] for x in ds) / n, dmgout=sum(x['damage_dealt'] for x in ds) / n, tdis=sum(dis) / len(dis) if dis else -1))
    if not groups: print('no charge-* results with runs.jsonl under', a.results); return 1
    out = []
    for design, rows in groups.items():
        rows.sort(key=lambda r: (r['tag'] != 'default', int(r['tag']) if r['tag'].isdigit() else 0))
        num = [r for r in rows if r['tag'].isdigit()]
        best = max(num, key=lambda r: (round(r['surv'], 1), -r['dmgin'], r['dmgout'])) if num else None
        print(f'\n{design}   (n per row: {rows[0]["n"]})')
        print(f'{"standoff":>9} {"surv%":>6} {"hit%":>6} {"dmgin":>9} {"lost%":>6} {"dist":>6} {"dmgout":>9} {"t_dis":>6}')
        for r in rows:
            mark = ' <- best' if r is best else ''
            print(f'{r["tag"]:>9} {r["surv"]:6.0f} {r["hit"]:6.0f} {r["dmgin"]:9.0f} {r["lost"]:6.1f} {r["dist"]:6.0f} {r["dmgout"]:9.0f} {r["tdis"]:6.0f}{mark}')
            out.append(dict(design=design, **r))
        if best: print(f'  best standoff {best["tag"]} m')
    if a.csv:
        with open(a.csv, 'w', newline='') as fh:
            w = csv.DictWriter(fh, fieldnames=list(out[0].keys())); w.writeheader(); w.writerows(out)
    return 0

if __name__ == '__main__':
    sys.exit(main())
