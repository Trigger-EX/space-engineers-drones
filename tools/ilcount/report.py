#!/usr/bin/env python3
"""il_profiler report: aggregate the "il" records of SimHost runs (runs.jsonl from `run --count-instructions`, or the "IL {...}" lines /
--il-out files of printer-test / dock-launch / follow-test) into a per-script table against the 50,000 limit.

    python3 -I tools/ilcount/report.py runs.jsonl [more.jsonl ...] [--limit 50000]

Per script: PB-runs, peak (max over all PBs/runs), the tick/UpdateType/seed-run where it happened, mean (run-weighted), worst per-PB p99,
constructor count, max call-chain depth, per sim-time window (0-10 s, 10-30, 30-60, 60-120, 120+) the mean / worst p99 / peak, and the top
sites (code blocks) at the global peak and the top sites of the mean run."""
import sys, json, collections

def records(path):
    for line in open(path, encoding='utf-8'):
        line = line.strip()
        if line.startswith('IL '): line = line[3:]
        if not line.startswith('{'): continue
        try: d = json.loads(line)
        except ValueError: continue
        if 'ships' in d:
            for s in d['ships']:
                for il in s.get('il', []):
                    yield (d.get('scenario', '?') + '#' + str(d.get('seed_index', 0)), il)
        elif 'il' in d and isinstance(d['il'], dict):
            yield (d.get('label', '?'), d['il'])

def main(a):
    limit = 50000
    if '--limit' in a: i = a.index('--limit'); limit = int(a[i + 1]); del a[i:i + 2]
    agg = collections.OrderedDict()
    for p in a:
        for label, il in records(p):
            g = agg.setdefault(il['script'], dict(pbs=0, runs=0, sum=0.0, peak=-1, peak_at=None, p99=0, ctor=0, depth=0, over=0, win={}, peak_sites=None, mean_sites=collections.Counter(), scen=set()))
            g['pbs'] += 1; g['runs'] += il['runs']; g['sum'] += il['mean'] * il['runs']; g['p99'] = max(g['p99'], il['p99'])
            g['ctor'] = max(g['ctor'], il['ctor']); g['depth'] = max(g['depth'], il['max_depth']); g['over'] += il['overruns']; g['scen'].add(label.split('#')[0])
            if il['max'] > g['peak']:
                g['peak'] = il['max']; g['peak_at'] = '%s tick %d %s' % (label, il['max_tick'], il['max_type']); g['peak_sites'] = il['peak_sites']
            for w in il['windows']:
                k = (w['from_s'], w['to_s']); x = g['win'].setdefault(k, dict(n=0, sum=0.0, p99=0, max=0))
                x['n'] += w['n']; x['sum'] += w['mean'] * w['n']; x['p99'] = max(x['p99'], w['p99']); x['max'] = max(x['max'], w['max'])
            for s in il['mean_sites']:
                g['mean_sites'][(s['kind'], s['method'], s['line'])] += s['n'] * il['runs']
    print('(site line numbers refer to the minified Program text, not scripts/*.cs)')
    print('| script | PBs | runs | peak | mean | worst p99 | ctor | depth | peak/limit | where the peak happened |')
    print('|---|---|---|---|---|---|---|---|---|---|')
    for name, g in sorted(agg.items(), key=lambda kv: -kv[1]['peak']):
        print('| %s | %d | %d | %d | %.0f | %d | %d | %d | %.1f%% | %s |' % (name, g['pbs'], g['runs'], g['peak'], g['sum'] / max(1, g['runs']), g['p99'], g['ctor'], g['depth'], 100.0 * g['peak'] / limit, g['peak_at']))
    for name, g in sorted(agg.items(), key=lambda kv: -kv[1]['peak']):
        print('\n### %s  (%d scenarios/cases: %s)' % (name, len(g['scen']), ', '.join(sorted(g['scen']))[:300]))
        print('windows (sim seconds): ' + '; '.join('%s-%s s: n=%d mean=%.0f p99<=%d max=%d' % (k[0], 'end' if k[1] < 0 else k[1], x['n'], x['sum'] / max(1, x['n']), x['p99'], x['max']) for k, x in sorted(g['win'].items()) if x['n']))
        print('peak sites: ' + '; '.join('%s %s:%s x%s' % (s['kind'], s['method'], s['line'], s['n']) for s in g['peak_sites'][:6]))
        tot = float(g['runs']) or 1
        print('mean sites (per run): ' + '; '.join('%s %s:%s %.1f' % (k[0], k[1], k[2], v / tot) for k, v in g['mean_sites'].most_common(6)))

if __name__ == '__main__':
    main(sys.argv[1:])
