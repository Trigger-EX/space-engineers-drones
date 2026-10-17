#!/usr/bin/env python3
"""Compares two runs.jsonl files fight by fight (same scenario + seed index), ignoring timing fields.
The sim is deterministic, so any difference on the same SimHost.exe means the machine computed something wrong.
Usage: python3 compare_runs.py results/enemy-main/runs.jsonl results/recheck/runs.jsonl
       python3 compare_runs.py --survival AFTER BEFORE [--side A|B]   A/B on survival (losses, exchange ratio, damage ratio, hits taken), see tools/sim_survival.py"""
import json, os, sys
if len(sys.argv) > 1 and sys.argv[1] == "--survival":
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
    import sim_survival
    side = sys.argv[sys.argv.index("--side") + 1] if "--side" in sys.argv else "A"
    paths = [x for x in sys.argv[2:] if x not in ("--side", side)]
    print(sim_survival.compare(sim_survival.load_runs(paths[0]), sim_survival.load_runs(paths[1]), side, False))
    sys.exit(0)
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
