#!/usr/bin/env python3
"""Kamikaze-defence report: compares sim runs of the "kamikaze" scenarios (incoming Stinger swarms, docs/kamikaze-defence.md).

  python3 -I tools/kz_report.py RUNS.jsonl [AFTER.jsonl] [--md OUT.md]

Per scenario (team 0 = the defenders, team 1 = the Stinger swarm), averaged over seeds:
  win      defenders won (every Stinger dead or out of action, a defender still combat capable)
  surv     defender ships still combat capable at the end, as a fraction of the defenders
  hull     defender mass lost (fraction of the starting mass, averaged over defenders)
  dmgT     damage the defenders took (sum over defender ships)
  shot     Stingers out of action that never detonated (shot down, or wrecked by another Stinger's blast) per run
  boom     Stingers that detonated (their own trigger, or an armed warhead shot or caught in a blast) per run
With two files, each cell shows before -> after, and the totals line compares the same (scenario, seed) pairs only.
"""
import json, sys
from collections import defaultdict


def load(p):
    out = {}
    with open(p, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line: continue
            r = json.loads(line)
            if r.get("kind") != "kamikaze": continue
            out[(r["scenario"], r["seed_index"])] = r
    return out


def metrics(r):
    d = [s for s in r["ships"] if s["team"] == 0]
    k = [s for s in r["ships"] if s["team"] == 1]
    boom = sum(1 for s in k if s.get("detonations", 0) > 0)
    dead = sum(1 for s in k if not s["capable"])
    return dict(
        win=1.0 if r["outcome"] == "win" and r["winner_team"] == 0 else 0.0,
        surv=sum(1 for s in d if s["capable"]) / max(1, len(d)),
        hull=sum(s["mass_lost_frac"] for s in d) / max(1, len(d)),
        dmgT=sum(s["damage_taken"] for s in d),
        shot=max(0, dead - boom),
        boom=boom,
    )


KEYS = ["win", "surv", "hull", "dmgT", "shot", "boom"]
FMT = {"win": "{:.0%}", "surv": "{:.0%}", "hull": "{:.0%}", "dmgT": "{:.0f}", "shot": "{:.1f}", "boom": "{:.1f}"}


def table(runs, keys=None):
    g = defaultdict(list)
    for (sc, si), r in runs.items():
        if keys is not None and (sc, si) not in keys: continue
        g[sc].append(metrics(r))
    return {sc: {k: sum(m[k] for m in ms) / len(ms) for k in KEYS} | {"n": len(ms)} for sc, ms in g.items()}


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    md = sys.argv[sys.argv.index("--md") + 1] if "--md" in sys.argv else None
    if md in args: args.remove(md)
    if not args: print(__doc__); return 2
    a = load(args[0]); b = load(args[1]) if len(args) > 1 else None
    common = set(a) & set(b) if b else None
    ta = table(a, common); tb = table(b, common) if b else None
    lines = ["| scenario | n | " + " | ".join(KEYS) + " |", "|---|---|" + "---|" * len(KEYS)]
    tot_a = defaultdict(float); tot_b = defaultdict(float); n_tot = 0
    for sc in sorted(ta):
        ra = ta[sc]; n = ra["n"]; n_tot += n
        cells = []
        for k in KEYS:
            tot_a[k] += ra[k] * n
            if tb:
                rb = tb[sc]; tot_b[k] += rb[k] * n
                cells.append(FMT[k].format(ra[k]) + " -> " + FMT[k].format(rb[k]))
            else: cells.append(FMT[k].format(ra[k]))
        lines.append("| " + sc.replace("kamikaze/", "") + " | " + str(n) + " | " + " | ".join(cells) + " |")
    cells = []
    for k in KEYS:
        s = FMT[k].format(tot_a[k] / max(1, n_tot))
        if tb: s += " -> " + FMT[k].format(tot_b[k] / max(1, n_tot))
        cells.append("**" + s + "**")
    lines.append("| **all** | " + str(n_tot) + " | " + " | ".join(cells) + " |")
    text = "\n".join(lines)
    print(text)
    if md:
        with open(md, "w", encoding="utf-8") as f: f.write(text + "\n")
    return 0


sys.exit(main())
