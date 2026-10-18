#!/usr/bin/env python3
"""Survival metrics from simulator results (runs.jsonl written by SimHost.exe `run`), and A/B comparison on survival.

usage:
  python3 -I tools/sim_survival.py RESULTS                       per scenario, per side: losses, survival, exchange, damage, hits by class
  python3 -I tools/sim_survival.py AFTER --compare BEFORE        A/B: the same table for both runs and the change (same scenarios only)
  python3 -I tools/sim_survival.py RESULTS --side B              the side to show (default A; A = team 0 = first-named side, B = all others)
  python3 -I tools/sim_survival.py RESULTS --ships               add one line per ship model of that side (per drone design)
  python3 -I tools/sim_survival.py RESULTS --hitmap [--model M] [--top 15]   hit map by cell for a design (aggregated over seeds)
  --md                                                           markdown tables instead of aligned text
RESULTS is a results directory (runs.jsonl) or a runs.jsonl file; --compare takes the same.

Definitions (all from the per-ship fields of runs.jsonl):
  lost       a ship that was disabled (combat capable once, not any more) or destroyed; lost_at_s is the first of the two times
  surv%      1 - lost / ships, over all runs of the scenario
  lost/kill  ships the side lost per ship the OTHER side lost (the exchange ratio; lower is better; '-' when the enemy lost none)
  dmg ratio  integrity the side dealt to enemies / integrity it took (collisions and own blasts count as taken)
  first loss mean time of the first loss, over the runs in which the side lost a ship;  life of lost = mean time at which lost ships were lost
  hits/ship  impacts taken per ship, split into bullet / rail / explosion / collision (a rocket blast is one impact)
Old runs.jsonl without the survival fields still work for lost, dmg and first loss; hits by class and the hit map need new runs.
"""
import argparse
import json
import os
import sys
from collections import defaultdict


def load_runs(path):
    p = os.path.join(path, "runs.jsonl") if os.path.isdir(path) else path
    runs = []
    with open(p, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                try:
                    runs.append(json.loads(line))
                except ValueError:
                    pass
    return runs


def ship_lost_at(s):
    """First of disabled/destroyed time, -1 if neither (works on old files too)."""
    ts = [t for t in (s.get("disabled_at_s", -1), s.get("destroyed_at_s", -1)) if t is not None and t >= 0]
    return min(ts) if ts else -1


def side_of(s):
    return "A" if s["team"] == 0 else "B"


def side_stats(rs, side, by_model=False):
    """Aggregate over runs rs for one side. Returns dict, or per-model dicts when by_model."""
    ships = lost = hits = 0
    dealt = taken = 0.0
    life = []
    first = []
    cls = [0, 0, 0, 0]
    allk = 0
    runs = 0
    for r in rs:
        if r.get("outcome") == "error":
            continue
        runs += 1
        mine = [s for s in r["ships"] if side_of(s) == side]
        ls = [ship_lost_at(s) for s in mine]
        ships += len(mine)
        for s, t in zip(mine, ls):
            dealt += s.get("damage_dealt", 0)
            taken += s.get("damage_taken", 0)
            hits += s.get("hits_taken", 0)
            for i, v in enumerate(s.get("hits_by_class", [0, 0, 0, 0])):
                cls[i] += v
            if t >= 0:
                lost += 1
                life.append(t)
        got = [t for t in ls if t >= 0]
        if got:
            first.append(min(got))
        if mine and len(got) == len(mine):
            allk += 1
    return dict(runs=runs, ships=ships, lost=lost, dealt=dealt, taken=taken, hits=hits, cls=cls, life=life, first=first, all_lost=allk)


def mean(v):
    return sum(v) / len(v) if v else None


def div(a, b):
    return a / b if b else None


def row_values(rs, side):
    me = side_stats(rs, side)
    en = side_stats(rs, "B" if side == "A" else "A")
    n = max(1, me["runs"])
    return {
        "ships": me["ships"] / n, "lost": me["lost"] / n,
        "surv": div(me["ships"] - me["lost"], me["ships"]),
        "lpk": div(me["lost"], en["lost"]),
        "dealt": me["dealt"] / n, "taken": me["taken"] / n, "ratio": div(me["dealt"], me["taken"]),
        "first": mean(me["first"]), "life": mean(me["life"]),
        "hits": div(me["hits"], me["ships"]),
        "cls": [div(c, me["ships"]) for c in me["cls"]],
        "allk": me["all_lost"], "runs": me["runs"],
    }


def f(v, nd=1, pct=False):
    if v is None:
        return "-"
    return ("%.0f%%" % (100 * v)) if pct else ("%." + str(nd) + "f") % v


def fmt_row(v):
    c = v["cls"]
    return [str(v["runs"]), f(v["ships"]), f(v["lost"], 2), f(v["surv"], pct=True), f(v["lpk"], 2), f(v["dealt"], 0), f(v["taken"], 0),
            f(v["ratio"], 2), f(v["first"]), f(v["life"]), f(v["hits"]), "%s/%s/%s/%s" % tuple(f(x) for x in c)]


HEAD = ["runs", "ships", "lost/run", "surv%", "lost/kill", "dealt/run", "taken/run", "dmg ratio", "1st loss s", "life lost s", "hits/ship", "bul/rail/expl/coll"]


def render(headers, rows, md):
    if md:
        out = ["| " + " | ".join(headers) + " |", "|" + "|".join([":---"] + ["---:"] * (len(headers) - 1)) + "|"]
        out += ["| " + " | ".join(r) + " |" for r in rows]
        return "\n".join(out)
    w = [max(len(h), *(len(r[i]) for r in rows)) if rows else len(h) for i, h in enumerate(headers)]
    out = ["  ".join(h.ljust(w[i]) if i == 0 else h.rjust(w[i]) for i, h in enumerate(headers))]
    out += ["  ".join(c.ljust(w[i]) if i == 0 else c.rjust(w[i]) for i, c in enumerate(r)) for r in rows]
    return "\n".join(out)


def by_scenario(runs):
    d = defaultdict(list)
    for r in runs:
        d[r["scenario"]].append(r)
    return d


def models_of(rs, side):
    return sorted({s["model"] for r in rs for s in r["ships"] if side_of(s) == side})


def per_model_rows(rs, side):
    """One row per ship model of `side`: restrict the stats to ships of that model."""
    rows = []
    for m in models_of(rs, side):
        sub = []
        for r in rs:
            r2 = dict(r)
            r2["ships"] = [s for s in r["ships"] if side_of(s) != side or s["model"] == m]
            sub.append(r2)
        rows.append(("  " + m, row_values(sub, side)))
    return rows


def report(runs, side, ships, md):
    sc = by_scenario(runs)
    rows = []
    for name in sorted(sc):
        rows.append([name] + fmt_row(row_values(sc[name], side)))
        if ships:
            for label, v in per_model_rows(sc[name], side):
                rows.append([label] + fmt_row(v))
    tot = row_values(runs, side)
    rows.append(["ALL"] + fmt_row(tot))
    return render(["scenario (side %s)" % side] + HEAD, rows, md)


def delta(a, b, nd=2, pct=False):
    if a is None or b is None:
        return "-"
    d = a - b
    return ("%+.0f%%" % (100 * d)) if pct else ("%+." + str(nd) + "f") % d


def compare(after, before, side, md):
    a, b = by_scenario(after), by_scenario(before)
    names = sorted(set(a) & set(b))
    headers = ["scenario (side %s)" % side, "runs b/a", "surv% b", "a", "d", "lost/kill b", "a", "dmg ratio b", "a", "1st loss b", "a", "life lost b", "a", "hits/ship b", "a"]
    rows = []
    for n in names + ["ALL"]:
        ra = a[n] if n != "ALL" else [r for k in names for r in a[k]]
        rb = b[n] if n != "ALL" else [r for k in names for r in b[k]]
        va, vb = row_values(ra, side), row_values(rb, side)
        rows.append([n, "%d/%d" % (vb["runs"], va["runs"]),
                     f(vb["surv"], pct=True), f(va["surv"], pct=True), delta(va["surv"], vb["surv"], pct=True),
                     f(vb["lpk"], 2), f(va["lpk"], 2), f(vb["ratio"], 2), f(va["ratio"], 2),
                     f(vb["first"]), f(va["first"]), f(vb["life"]), f(va["life"]), f(vb["hits"]), f(va["hits"])])
    out = [render(headers, rows, md)]
    only = sorted(set(a) ^ set(b))
    if only:
        out.append("not in both: " + ", ".join(only[:20]) + (" ..." if len(only) > 20 else ""))
    out.append("b = before (--compare), a = after (first path), d = after - before. Higher surv%, dmg ratio, 1st loss, life lost is better; lower lost/kill is better.")
    return "\n".join(out)


def hitmap(runs, model, side, top):
    agg = defaultdict(lambda: [0.0, 0.0, 0.0])
    n = 0
    for r in runs:
        for s in r["ships"]:
            if s["model"] != model or (side and side_of(s) != side):
                continue
            n += 1
            for cell, h, d, k in s.get("hitmap", []):
                v = agg[cell]
                v[0] += h
                v[1] += d
                v[2] += k
    if not n:
        return "no ship of model %r%s in these runs" % (model, " on side " + side if side else "")
    rows = sorted(agg.items(), key=lambda kv: -kv[1][1])[:top]
    tot = sum(v[1] for v in agg.values()) or 1
    out = ["%s: %d ships, %d cells ever hit, top %d by damage (cell = construct:x:y:z:block)" % (model, n, len(agg), len(rows))]
    out.append(render(["cell", "hits/ship", "dmg/ship", "dmg share", "killed/ship"],
                      [[c, f(v[0] / n, 2), f(v[1] / n, 0), f(v[1] / tot, pct=True), f(v[2] / n, 2)] for c, v in rows], False))
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("results")
    ap.add_argument("--compare")
    ap.add_argument("--side", default="A", choices=["A", "B"])
    ap.add_argument("--ships", action="store_true")
    ap.add_argument("--md", action="store_true")
    ap.add_argument("--hitmap", action="store_true")
    ap.add_argument("--model")
    ap.add_argument("--top", type=int, default=15)
    a = ap.parse_args()
    runs = load_runs(a.results)
    if a.hitmap:
        models = [a.model] if a.model else sorted({s["model"] for r in runs for s in r["ships"] if side_of(s) == a.side})
        for m in models:
            print(hitmap(runs, m, a.side, a.top))
            print()
    elif a.compare:
        print(compare(runs, load_runs(a.compare), a.side, a.md))
    else:
        print(report(runs, a.side, a.ships, a.md))


if __name__ == "__main__":
    main()
