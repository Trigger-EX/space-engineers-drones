#!/usr/bin/env python3
"""Audit of baseline runs.jsonl: why do runs time out?  python3 -I tools/sim_audit_timeouts.py [runs.jsonl]"""
import json, sys, collections
path = sys.argv[1] if len(sys.argv) > 1 else 'out/sim/results/baseline-v2.0/runs.jsonl'
rows = [json.loads(l) for l in open(path)]

def cls(r):
    """Classify a timeout by what the armed ships did."""
    tot_shots = sum(s['shots'] for s in r['ships'])
    if tot_shots == 0:
        return 'no shot fired (never in range)'
    return 'shots fired but no kill'

def table(title, keyf, filt):
    c = collections.defaultdict(collections.Counter)
    for r in rows:
        if not filt(r): continue
        c[keyf(r)][r['outcome']] += 1
    print('\n##', title)
    for k in sorted(c):
        n = sum(c[k].values()); t = c[k]['timeout']
        print(f'{str(k):40s} n={n:6d} timeout={100*t/n:5.1f}%  win={100*c[k]["win"]/n:5.1f}%')

if __name__ == '__main__':
    to = [r for r in rows if r['outcome'] == 'timeout']
    print('timeouts', len(to), 'of', len(rows))
    print(collections.Counter(cls(r) for r in to))
    print(collections.Counter((r['kind'], cls(r)) for r in to))
    table('timeout rate by kind', lambda r: r['kind'], lambda r: True)
    # start distance proxy: not stored; use min_enemy_dist for timeout w/o shots
    # per-model, 1v1 only: share of ship-runs with zero shots, zero script target, ai_on
    st = collections.defaultdict(lambda: collections.Counter())
    for r in rows:
        if r['kind'] != '1v1': continue
        for s in r['ships']:
            m = s['model']; c = st[m]
            c['n'] += 1
            c['noshot'] += s['shots'] == 0
            c['script_tgt'] += s['first_script_target_s'] >= 0
            c['ai_on'] += s['ai_on_s'] > 5
            c['slow'] += s['stuck_kind'] == 'slow'
            c['closed<1500'] += s['min_enemy_dist_m'] < 1500
    print('\n## 1v1 ship-runs per model')
    for m, c in sorted(st.items()):
        n = c['n']; print(f'{m:10s} n={n:5d} '+' '.join(f'{k}={100*v/n:5.1f}%' for k, v in c.items() if k != 'n'))

def breakdown_armed_1v1():
    """1v1 timeouts where both sides are 'AI-block fighters' (not Gnat/Stinger/Watcher): where did they stop?"""
    weak = ('Gnat', 'Stinger', 'Watcher')
    bins = [(0, 800, 'closed <800 m (in weapon range)'), (800, 1000, '800-1000'), (1000, 1500, '1000-1500'),
            (1500, 2000, '1500-2000'), (2000, 2500, '2000-2500'), (2500, 9e9, '>2500 (beyond AI search range)')]
    c = collections.Counter(); n = 0; fired = collections.Counter()
    tot = 0
    for r in rows:
        if r['kind'] != '1v1' or any(s['model'] in weak for s in r['ships']): continue
        tot += 1
        if r['outcome'] != 'timeout': continue
        n += 1
        md = min(s['min_enemy_dist_m'] for s in r['ships'])
        sh = sum(s['shots'] for s in r['ships'])
        for lo, hi, name in bins:
            if lo <= md < hi:
                c[name] += 1; fired[name] += sh > 0
    print('\n## armed-vs-armed 1v1: %d runs, %d timeouts; closest approach of the two ships' % (tot, n))
    for lo, hi, name in bins:
        print(f'  {name:36s} {c[name]:5d} ({100*c[name]/max(n,1):4.1f}%)  any shot fired: {fired[name]}')

if __name__ == '__main__':
    breakdown_armed_1v1()

def breakdown_all():
    """All kinds: timeouts split by 'is there an AI-block fighter that could have found the enemy' and closest approach."""
    weak = ('Gnat', 'Stinger', 'Watcher')
    c = collections.Counter()
    for r in rows:
        if r['outcome'] != 'timeout': continue
        models = {t: [s['model'] for s in r['ships'] if s['team'] == t] for t in (0, 1)}
        finder = any(m not in weak for m in sum(models.values(), []))     # includes Pirate (own brain)
        md = min(s['min_enemy_dist_m'] for s in r['ships'])
        sh = sum(s['shots'] for s in r['ships'])
        key = ('a finder exists' if finder else 'only Gnat/Stinger/Watcher (no finder)',
               'start beyond 2.5 km' if md > 2500 else ('fought (shots)' if sh > 0 else 'closed, no shots'))
        c[key] += 1
    print('\n## all timeouts by cause')
    for k, v in sorted(c.items()): print(f'  {k[0]:44s} {k[1]:22s} {v:6d}')

if __name__ == '__main__':
    breakdown_all()
