#!/usr/bin/env python3
"""Compares two runs.jsonl files fight by fight (same scenario + seed index), ignoring timing fields.
The sim is deterministic, so any difference on the same SimHost.exe means the machine computed something wrong.
Usage: python3 compare_runs.py results/enemy-main/runs.jsonl results/recheck/runs.jsonl"""
import json, sys
TIMING = {"wall_ms", "cpu_ms", "script_cpu_ms", "script_max_tick_ms"}
def strip(o):
    if isinstance(o, dict): return {k: strip(v) for k, v in o.items() if k not in TIMING}
    if isinstance(o, list): return [strip(v) for v in o]
    return o
def load(p):
    d = {}
    for line in open(p, encoding="utf-8"):
        line = line.strip()
        if not line: continue
        try: r = json.loads(line)
        except ValueError: continue            # a partial last line left by a crash
        d[(r["scenario"], r["seed_index"])] = strip(r)
    return d
a, b = load(sys.argv[1]), load(sys.argv[2])
common = sorted(set(a) & set(b)); bad = [k for k in common if a[k] != b[k]]
for k in bad[:20]:
    diff = [f for f in a[k] if a[k].get(f) != b[k].get(f)]
    print("DIFFERENT: %s seed %d  fields: %s" % (k[0], k[1], ", ".join(diff)))
print("%d fights in both files, %d identical, %d different" % (len(common), len(common) - len(bad), len(bad)))
