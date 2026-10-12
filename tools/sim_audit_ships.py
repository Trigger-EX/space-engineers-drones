#!/usr/bin/env python3
"""Per-model ship-run audit of baseline runs.jsonl: hit rates, damage, collisions, friendly fire, script-target share.
python3 -I tools/sim_audit_ships.py [runs.jsonl]"""
import json, sys, collections
path = sys.argv[1] if len(sys.argv) > 1 else 'out/sim/results/baseline-v2.0/runs.jsonl'
rows = [json.loads(l) for l in open(path)]
A = collections.defaultdict(lambda: collections.defaultdict(float))
for r in rows:
    for s in r['ships']:
        a = A[s['model']]
        a['n'] += 1; a['shots'] += s['shots']; a['hits'] += s['hits']; a['dealt'] += s['damage_dealt']
        a['taken'] += s['damage_taken']; a['coll'] += s['collisions']; a['ffdmg'] += s['friendly_damage']
        a['ffhits'] += s['friendly_hits']; a['ffruns'] += s['friendly_hits'] > 0
        a['det'] += s['detonations']; a['tot'] += s['time_on_target_s']; a['lost'] += s['blocks_lost']
        if s['shots'] > 0:
            a['nfire'] += 1; a['tot_fire'] += s['time_on_target_s']
        a['collruns'] += s['collisions'] > 0
print(f"{'model':10s} {'n':>6s} {'shots/run':>9s} {'hit%':>6s} {'dealt':>7s} {'taken':>7s} {'coll':>6s} {'collRuns%':>9s} {'ffRuns':>6s} {'ffDmg':>8s} {'ToT(firing)':>11s}")
for m, a in sorted(A.items()):
    n = a['n']
    print(f"{m:10s} {int(n):6d} {a['shots']/n:9.0f} {100*a['hits']/max(a['shots'],1):6.1f} {a['dealt']/n:7.0f} {a['taken']/n:7.0f} {int(a['coll']):6d} {100*a['collruns']/n:9.1f} {int(a['ffruns']):6d} {a['ffdmg']:8.0f} {a['tot_fire']/max(a['nfire'],1):11.1f}")
# friendly fire by scenario kind and by model pair
ff = collections.Counter(); ffn = collections.Counter()
for r in rows:
    for s in r['ships']:
        if s['friendly_hits']:
            ff[(r['kind'], s['model'])] += s['friendly_damage']; ffn[(r['kind'], s['model'])] += 1
print('\nfriendly fire (kind, shooter model): ship-runs / damage')
for k, v in ff.most_common(25): print(k, ffn[k], int(v))
# collisions by kind
co = collections.Counter()
for r in rows:
    for s in r['ships']:
        co[(r['kind'], s['model'])] += s['collisions']
print('\ncollisions (kind, model)')
for k, v in co.most_common(15): print(k, v)
