#!/usr/bin/env python3
"""Markdown report from simulator results (sim/ SimHost.exe `run` output).

usage:
  python3 -I tools/sim_report.py out/sim/results/<batch> [--out report.md] [--title TEXT]
  python3 -I tools/sim_report.py out/sim/results/<after> --compare out/sim/results/<before> [--out diff.md]
  python3 -I tools/sim_report.py --trace trace.csv [--out trace.svg]     (SimHost.exe `one --trace` CSV: SVG plot + summary)

The first argument is a results directory (runs.jsonl [+ meta.json]) or a runs.jsonl file. Without --out the report goes to
stdout. Only the standard library is used. Nothing here knows about game rules: it only aggregates what runs.jsonl records
(outcome, winner_team, per-ship stats), so the numbers are exactly those of the simulator.

Terms: "side A" = team 0 = the first-named side of the scenario (1v1/X-vs-Y: X). win% = share of runs the side won;
draw = mutual kill or no contest; timeout = 5 minute cap without a decision. A model's 1v1 score counts a win as 1, a draw
or timeout as 0.5 and mirrors count twice (once per side) so they contribute 0.5 per run by construction.
"""
import argparse
import json
import os
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))   # python -I drops the script directory
import sim_survival


def load_runs(path):
    p = path
    meta = None
    if os.path.isdir(p):
        mp = os.path.join(p, "meta.json")
        if os.path.exists(mp):
            try:
                with open(mp, encoding="utf-8") as f:
                    meta = json.load(f)
            except Exception:
                meta = None
        p = os.path.join(p, "runs.jsonl")
    runs = []
    with open(p, encoding="utf-8") as f:
        for ln, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                runs.append(json.loads(line))
            except Exception as e:  # keep going, report at the end
                sys.stderr.write("skipping unreadable line %d: %s\n" % (ln, e))
    return runs, meta


def pct(n, d):
    return "%.0f%%" % (100.0 * n / d) if d else "-"


def median(v):
    v = sorted(v)
    if not v:
        return None
    m = len(v) // 2
    return v[m] if len(v) % 2 else 0.5 * (v[m - 1] + v[m])


def fnum(x, nd=1):
    return "-" if x is None else ("%." + str(nd) + "f") % x


def table(headers, rows, align=None):
    out = ["| " + " | ".join(headers) + " |"]
    al = []
    for i, h in enumerate(headers):
        a = (align[i] if align else ("l" if i == 0 else "r"))
        al.append(":---" if a == "l" else "---:")
    out.append("| " + " | ".join(al) + " |")
    for r in rows:
        out.append("| " + " | ".join(str(c) for c in r) + " |")
    return "\n".join(out)


def by_scenario(runs):
    d = defaultdict(list)
    for r in runs:
        d[r["scenario"]].append(r)
    return d


def side_models(run, team):
    return [s["model"] for s in run["ships"] if s["team"] == team]


def side_label(run, team):
    c = Counter(side_models(run, team))
    return "+".join(("%dx%s" % (n, m)) if n > 1 else m for m, n in sorted(c.items()))


def scenario_row(name, rs):
    n = len(rs)
    wa = sum(1 for r in rs if r["outcome"] == "win" and r["winner_team"] == 0)
    wb = sum(1 for r in rs if r["outcome"] == "win" and r["winner_team"] == 1)
    dr = sum(1 for r in rs if r["outcome"] in ("mutual-kill", "no-contest"))
    to = sum(1 for r in rs if r["outcome"] == "timeout")
    er = sum(1 for r in rs if r["outcome"] == "error")
    ttk = [r["ttk_s"] for r in rs if r["outcome"] == "win" and r.get("ttk_s", -1) is not None and r.get("ttk_s", -1) >= 0]
    return n, wa, wb, dr, to, er, median(ttk)


def hits_per_shot(rs, team):
    sh = sum(s["shots"] for r in rs for s in r["ships"] if s["team"] == team)
    hi = sum(s["hits"] for r in rs for s in r["ships"] if s["team"] == team)
    return (hi / sh) if sh else None


def section_overview(runs, meta, title):
    out = ["# " + title, ""]
    oc = Counter(r["outcome"] for r in runs)
    kinds = Counter(r["kind"] for r in runs)
    scen = len(set(r["scenario"] for r in runs))
    wall = sum(r.get("wall_ms", 0) for r in runs) / 1000.0
    hashes = sorted(set(r.get("settings_hash", "") for r in runs))
    out.append("%d runs in %d scenarios (%s). Outcomes: %s." % (
        len(runs), scen, ", ".join("%s %d" % (k, v) for k, v in sorted(kinds.items())),
        ", ".join("%s %d" % (k, v) for k, v in sorted(oc.items()))))
    out.append("")
    out.append("Total wall time of all runs %.0f s (%.2f s per run on average); settings hash %s." % (
        wall, wall / max(1, len(runs)), ", ".join(hashes)))
    if meta:
        out.append("")
        out.append("Command line: `%s`" % meta.get("command_line", ""))
        a = meta.get("assumptions") or []
        if a:
            out.append("")
            out.append("Assumptions recorded by the loader:")
            for x in a:
                out.append("- " + x)
    out.append("")
    return out


def section_1v1(runs):
    rs1 = [r for r in runs if r["kind"] == "1v1"]
    if not rs1:
        return []
    out = ["## 1v1 matrix", ""]
    models = sorted(set(s["model"] for r in rs1 for s in r["ships"]))
    # per ordered model pair: wins of row model over column model, runs
    wins = defaultdict(int)
    runsn = defaultdict(int)
    mirror_runs = defaultdict(int)
    mirror_dec = defaultdict(int)
    score = defaultdict(float)
    cnt = defaultdict(int)
    for r in rs1:
        m = [side_models(r, 0)[0], side_models(r, 1)[0]]
        if m[0] == m[1]:
            mirror_runs[m[0]] += 1
            if r["outcome"] == "win":
                mirror_dec[m[0]] += 1
        else:
            runsn[(m[0], m[1])] += 1
            runsn[(m[1], m[0])] += 1
        for t in (0, 1):
            cnt[m[t]] += 1
            if r["outcome"] == "win":
                score[m[t]] += 1.0 if r["winner_team"] == t else 0.0
                if r["winner_team"] == t and m[0] != m[1]:
                    wins[(m[t], m[1 - t])] += 1
            else:
                score[m[t]] += 0.5
    out.append("Row model wins against column model (share of runs; the rest are draws, timeouts and losses). "
               "The diagonal (mirror fights) shows the share of runs with a decisive result.")
    out.append("")
    rows = []
    for a in models:
        row = [a]
        for b in models:
            if a == b:
                row.append(pct(mirror_dec[a], mirror_runs[a]) if mirror_runs[a] else "-")
            else:
                n = runsn.get((a, b), 0)
                row.append(pct(wins.get((a, b), 0), n) if n else "-")
        rows.append(row)
    out.append(table([""] + models, rows))
    out.append("")
    rank = sorted(models, key=lambda m: -(score[m] / cnt[m] if cnt[m] else 0))
    out.append("### Ranking by 1v1 score (win 1, draw/timeout 0.5)")
    out.append("")
    rows = []
    for m in rank:
        dmg = sum(s["damage_taken"] for r in rs1 for s in r["ships"] if s["model"] == m)
        shots = sum(s["shots"] for r in rs1 for s in r["ships"] if s["model"] == m)
        hits = sum(s["hits"] for r in rs1 for s in r["ships"] if s["model"] == m)
        rows.append([m, "%.1f%%" % (100 * score[m] / cnt[m]) if cnt[m] else "-", cnt[m],
                     fnum(hits / shots * 100 if shots else None) + ("%" if shots else ""), "%.0f" % (dmg / max(1, cnt[m]))])
    out.append(table(["model", "score", "sides", "hits per shot", "avg damage taken"], rows))
    out.append("")
    return out


def section_groups(runs):
    out = []
    names = {"1v2": "1v2", "1v3": "1v3", "2v3": "2v3", "mixed": "Mixed teams", "swarm": "Hive swarms (Gnats)", "pirate": "Pirate ship"}
    for kind in ("1v2", "1v3", "2v3", "mixed", "swarm", "pirate"):
        rs = [r for r in runs if r["kind"] == kind]
        if not rs:
            continue
        out += ["## " + names[kind], ""]
        rows = []
        for name, l in sorted(by_scenario(rs).items()):
            n, wa, wb, dr, to, er, tt = scenario_row(name, l)
            rows.append([name.split("/", 1)[1], n, pct(wa, n), pct(wb, n), pct(dr, n), pct(to, n), er,
                         fnum(tt), fnum(hits_per_shot(l, 0) and hits_per_shot(l, 0) * 100) , fnum(hits_per_shot(l, 1) and hits_per_shot(l, 1) * 100)])
        out.append(table(["scenario (A-vs-B)", "runs", "A wins", "B wins", "draw", "timeout", "err", "median ttk s", "A hit%", "B hit%"], rows))
        out.append("")
    return out


def section_problems(runs):
    out = ["## Problems seen", ""]
    errs = [r for r in runs if r["outcome"] == "error"]
    if errs:
        c = Counter((r.get("error") or "?").strip().split("\n")[0] for r in errs)
        out.append("Engine errors (%d runs):" % len(errs))
        for k, v in c.most_common(10):
            out.append("- %d x `%s`" % (v, k[:200]))
        out.append("")
    exc = Counter()
    exc_scripts = Counter()
    for r in runs:
        for s in r["ships"]:
            if s["exceptions"]:
                exc[(s["script"], (s.get("first_exception") or "?")[:160])] += s["exceptions"]
                exc_scripts[s["script"]] += 1
    if exc:
        out.append("Script exceptions (a PB that throws out of Main stops, like in the game):")
        for (sc, msg), v in exc.most_common(12):
            out.append("- %s: %d x `%s`" % (sc, v, msg))
        out.append("")
    else:
        out.append("No script exceptions.")
        out.append("")
    st = defaultdict(lambda: [0, 0, Counter()])
    for r in runs:
        for s in r["ships"]:
            e = st[s["model"]]
            e[0] += 1
            if s["stuck"]:
                e[1] += 1
                e[2][s.get("stuck_kind", "")] += 1
    rows = []
    for m, (n, k, kinds) in sorted(st.items()):
        if k:
            rows.append([m, n, k, pct(k, n), ", ".join("%s %d" % kv for kv in kinds.most_common())])
    if rows:
        out.append("Stuck or circling flags (speed < 1 m/s for 30 s, or no engagement within 1.5x weapon range for 60 s):")
        out.append("")
        out.append(table(["model", "ship-runs", "flagged", "share", "kinds"], rows))
        out.append("")
    ff = [(r["scenario"], s["friendly_damage"]) for r in runs for s in r["ships"] if s["friendly_damage"] > 0]
    if ff:
        by = defaultdict(lambda: [0, 0.0])
        for n, d in ff:
            by[n][0] += 1
            by[n][1] += d
        out.append("Friendly fire occurred in %d ship-runs; top scenarios:" % len(ff))
        for n, (c, d) in sorted(by.items(), key=lambda kv: -kv[1][1])[:8]:
            out.append("- %s: %d ships, %.0f damage" % (n, c, d))
        out.append("")
    col = Counter()
    for r in runs:
        for s in r["ships"]:
            col[s["model"]] += s["collisions"]
    if sum(col.values()):
        out.append("Collisions: " + ", ".join("%s %d" % kv for kv in col.most_common() if kv[1]))
        out.append("")
    return out


def section_timing(runs):
    out = ["## Cost", ""]
    by = defaultdict(list)
    for r in runs:
        by[r["kind"]].append(r)
    rows = []
    for k, l in sorted(by.items()):
        w = [x.get("wall_ms", 0) for x in l]
        d = [x["duration_s"] for x in l]
        rows.append([k, len(l), "%.0f" % (sum(w) / len(w)), "%.1f" % (sum(d) / len(d)), "%.1f" % (sum(w) / max(1e-9, sum(d)))])
    out.append(table(["kind", "runs", "avg wall ms", "avg sim s", "wall ms per sim s"], rows))
    out.append("")
    sc = defaultdict(lambda: [0.0, 0, 0.0])
    for r in runs:
        for s in r["ships"]:
            e = sc[s["script"]]
            e[0] += s.get("script_cpu_ms", 0)
            e[1] += s.get("script_runs", 0)
            e[2] = max(e[2], s.get("script_max_tick_ms", 0))
    rows = []
    for k, (cpu, n, mx) in sorted(sc.items(), key=lambda kv: -kv[1][0]):
        rows.append([k, "%.0f" % cpu, n, "%.4f" % (cpu / n if n else 0), "%.2f" % mx])
    out.append(table(["script", "total cpu ms", "Main calls", "ms per call", "slowest call ms"], rows))
    out.append("")
    return out


def compare(after, before, title):
    out = ["# " + title, ""]
    a = by_scenario(after)
    b = by_scenario(before)
    rows = []
    for name in sorted(set(a) & set(b)):
        na, wa, wba, dra, toa, era, tta = scenario_row(name, a[name])
        nb, wb, wbb, drb, tob, erb, ttb = scenario_row(name, b[name])
        da = (wa - wba) / na if na else 0
        db = (wb - wbb) / nb if nb else 0
        rows.append((abs(da - db), name, na, nb, wa / na, wb / nb, wba / na, wbb / nb, toa / na, tob / nb, tta, ttb))
    rows.sort(reverse=True)
    out.append("%d scenarios in both. Sorted by the change of (A wins - B wins). Before = second path, after = first path." % len(rows))
    out.append("")
    trs = []
    for d, name, na, nb, wa_, wb_, wba_, wbb_, toa_, tob_, tta, ttb in rows[:60]:
        trs.append([name, "%d/%d" % (nb, na), pct(wb_ * nb, nb), pct(wa_ * na, na), pct(wbb_ * nb, nb), pct(wba_ * na, na),
                    pct(tob_ * nb, nb), pct(toa_ * na, na), fnum(ttb), fnum(tta)])
    out.append(table(["scenario", "runs b/a", "A wins before", "after", "B wins before", "after", "timeout before", "after", "ttk before", "after"], trs))
    out.append("")
    only_a = sorted(set(a) - set(b))
    only_b = sorted(set(b) - set(a))
    if only_a:
        out.append("Only in after: " + ", ".join(only_a[:30]) + (" ..." if len(only_a) > 30 else ""))
    if only_b:
        out.append("Only in before: " + ", ".join(only_b[:30]) + (" ..." if len(only_b) > 30 else ""))
    out.append("")
    return out


# ---------------------------------------------------------------------------------------------------------------------------
# trace plot (SimHost.exe one --trace FILE): positions projected on the engagement plane + time series, as a standalone SVG
# ---------------------------------------------------------------------------------------------------------------------------
TRACE_COLORS = ["#2a6fdb", "#d9480f", "#2b8a3e", "#ae3ec9", "#e67700", "#0b7285"]


def _f(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def load_trace(path):
    import csv
    with open(path, encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    n = 0
    while rows and ("s%d_x" % n) in rows[0]:
        n += 1
    return rows, n


def trace_summary(rows, n):
    out = []
    t = [_f(r["t"]) for r in rows]
    for i in range(n):
        p = "s%d_" % i
        dist = [_f(r[p + "enemy_dist"]) for r in rows]
        clos = [_f(r[p + "closing"]) for r in rows]
        aim = [_f(r[p + "aim_err_deg"]) for r in rows]
        spd = [_f(r[p + "speed"]) for r in rows]
        dt = (t[1] - t[0]) if len(t) > 1 else 0
        on = sum(dt for a in aim if a is not None and a < 2.0)
        last = rows[-1]
        dd = [d for d in dist if d is not None]
        cc = [c for c in clos if c is not None]
        ss = [v for v in spd if v is not None]
        out.append("ship %d: min dist %s m, max closing %s m/s, max speed %s m/s, gun within 2 deg of enemy centre %.1f s, "
                   "shots %s, hits %s, blocks %s, script target shown %.1f s, AI blocks on %.1f s" % (
                       i, "%.0f" % min(dd) if dd else "-", "%.1f" % max(cc) if cc else "-", "%.1f" % max(ss) if ss else "-", on,
                       last[p + "shots"], last[p + "hits"], last[p + "blocks"],
                       sum(dt for r in rows if r.get(p + "script_tgt") == "1"), sum(dt for r in rows if r.get(p + "ai_on") == "1")))
    return out


def trace_svg(rows, n, title):
    import math
    W, H = 1000, 1060
    t = [_f(r["t"]) for r in rows]
    pts = []
    for r in rows:
        for i in range(n):
            x, y, z = (_f(r["s%d_%s" % (i, k)]) for k in "xyz")
            if x is not None:
                pts.append((x, y, z))
    if not pts:
        return "<svg xmlns='http://www.w3.org/2000/svg'/>"
    # engagement plane: the two principal axes of all positions (power iteration on the 3x3 covariance, no numpy)
    m = [sum(p[k] for p in pts) / len(pts) for k in range(3)]
    C = [[sum((p[a] - m[a]) * (p[b] - m[b]) for p in pts) for b in range(3)] for a in range(3)]

    def power(C, avoid=None):
        v = [1.0, 0.7, 0.3]
        for _ in range(60):
            w = [sum(C[a][b] * v[b] for b in range(3)) for a in range(3)]
            if avoid:
                d = sum(w[k] * avoid[k] for k in range(3))
                w = [w[k] - d * avoid[k] for k in range(3)]
            l = math.sqrt(sum(x * x for x in w)) or 1.0
            v = [x / l for x in w]
        return v
    e1 = power(C)
    e2 = power(C, e1)
    proj = lambda p: (sum((p[k] - m[k]) * e1[k] for k in range(3)), sum((p[k] - m[k]) * e2[k] for k in range(3)))
    P = [proj(p) for p in pts]
    span = max(max(abs(a) for a, _ in P), max(abs(b) for _, b in P), 1.0) * 1.08
    box = (40, 60, 920, 520)          # x, y, w, h of the map panel

    def mx(u):
        return box[0] + box[2] / 2 + u / span * box[3] / 2

    def my(v):
        return box[1] + box[3] / 2 - v / span * box[3] / 2
    o = ["<svg xmlns='http://www.w3.org/2000/svg' width='%d' height='%d' font-family='sans-serif' font-size='12'>" % (W, H),
         "<rect width='100%' height='100%' fill='white'/>",
         "<text x='40' y='28' font-size='16'>%s</text>" % title,
         "<text x='40' y='46' fill='#555'>positions projected on the engagement plane (principal axes); dots every 5 s, ring = end</text>",
         "<rect x='%d' y='%d' width='%d' height='%d' fill='none' stroke='#bbb'/>" % box]
    # scale bar
    bar = 10 ** int(math.log10(span)) if span > 1 else 1
    o.append("<line x1='%.1f' y1='%d' x2='%.1f' y2='%d' stroke='#333'/><text x='%.1f' y='%d'>%g m</text>" % (
        box[0] + 10, box[1] + box[3] - 12, box[0] + 10 + bar / span * box[3] / 2, box[1] + box[3] - 12,
        box[0] + 14 + bar / span * box[3] / 2, box[1] + box[3] - 8, bar))
    every = max(1, int(round(5.0 / ((t[1] - t[0]) if len(t) > 1 and t[1] > t[0] else 1))))
    for i in range(n):
        c = TRACE_COLORS[i % len(TRACE_COLORS)]
        seg, dots = [], []
        for j, r in enumerate(rows):
            x, y, z = (_f(r["s%d_%s" % (i, k)]) for k in "xyz")
            if x is None:
                continue
            u, v = proj((x, y, z))
            seg.append("%.1f,%.1f" % (mx(u), my(v)))
            if j % every == 0:
                dots.append((mx(u), my(v)))
        if seg:
            o.append("<polyline fill='none' stroke='%s' stroke-width='1.5' points='%s'/>" % (c, " ".join(seg)))
            for (a, b) in dots:
                o.append("<circle cx='%.1f' cy='%.1f' r='2.2' fill='%s'/>" % (a, b, c))
            a, b = seg[-1].split(",")
            o.append("<circle cx='%s' cy='%s' r='5' fill='none' stroke='%s' stroke-width='2'/>" % (a, b, c))
            a, b = seg[0].split(",")
            o.append("<text x='%s' y='%s' fill='%s'>ship %d start</text>" % (a, float(b) - 6, c, i))
    # time series panels
    panels = [("enemy_dist", "distance to nearest enemy (m)", None), ("closing", "closing speed (m/s)", None),
              ("aim_err_deg", "best fixed gun vs enemy centre (deg, clipped 20)", 20.0), ("shots", "cumulative shots / hits (dashed)", None)]
    y0 = 600
    ph = 95
    tmax = max(x for x in t if x is not None) or 1.0
    for k, (key, label, clip) in enumerate(panels):
        top = y0 + k * (ph + 22)
        o.append("<rect x='40' y='%d' width='920' height='%d' fill='none' stroke='#ddd'/>" % (top, ph))
        o.append("<text x='44' y='%d' fill='#333'>%s</text>" % (top + 13, label))
        series = []
        for i in range(n):
            ys = [_f(r["s%d_%s" % (i, key)]) for r in rows]
            if clip is not None:
                ys = [min(v, clip) if v is not None else None for v in ys]
            series.append(ys)
            if key == "shots":
                series.append([_f(r["s%d_hits" % i]) for r in rows])
        vals = [v for ser in series for v in ser if v is not None]
        if not vals:
            continue
        lo, hi = min(vals + [0.0]), max(vals + [1e-9])
        if hi - lo < 1e-9:
            hi = lo + 1
        o.append("<text x='958' y='%d' text-anchor='end' fill='#777'>%.0f</text><text x='958' y='%d' text-anchor='end' fill='#777'>%.0f</text>" % (top + 13, hi, top + ph - 3, lo))
        for si, ser in enumerate(series):
            i = si // 2 if key == "shots" else si
            c = TRACE_COLORS[i % len(TRACE_COLORS)]
            pp = ["%.1f,%.1f" % (40 + 920 * (tt / tmax), top + ph - (v - lo) / (hi - lo) * (ph - 18) - 2)
                  for tt, v in zip(t, ser) if v is not None and tt is not None]
            dash = " stroke-dasharray='4,3'" if key == "shots" and si % 2 == 1 else ""
            if pp:
                o.append("<polyline fill='none' stroke='%s' stroke-width='1.2'%s points='%s'/>" % (c, dash, " ".join(pp)))
        if lo < 0 < hi:
            yz = top + ph - (0 - lo) / (hi - lo) * (ph - 18) - 2
            o.append("<line x1='40' x2='960' y1='%.1f' y2='%.1f' stroke='#ccc'/>" % (yz, yz))
    o.append("<text x='40' y='%d' fill='#777'>t = 0 .. %.0f s</text>" % (H - 8, tmax))
    o.append("</svg>")
    return "\n".join(o)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("results", nargs="?")
    ap.add_argument("--trace", help="trace CSV from SimHost.exe one --trace: write an SVG plot (--out) and print a summary")
    ap.add_argument("--compare", help="older results (directory or runs.jsonl) to compare against")
    ap.add_argument("--out")
    ap.add_argument("--title", default="Simulation report")
    a = ap.parse_args()
    if a.trace:
        rows, n = load_trace(a.trace)
        for line in trace_summary(rows, n):
            print(line)
        if a.out:
            with open(a.out, "w", encoding="utf-8") as f:
                f.write(trace_svg(rows, n, a.title if a.title != "Simulation report" else os.path.basename(a.trace)))
            sys.stderr.write("wrote %s\n" % a.out)
        return
    if not a.results:
        ap.error("results directory or runs.jsonl needed (or --trace CSV)")
    runs, meta = load_runs(a.results)
    if a.compare:
        before, _ = load_runs(a.compare)
        lines = compare(runs, before, a.title + " (before/after)")
        lines += ["## Survival (side A)", "", sim_survival.compare(runs, before, "A", True), ""]
    else:
        lines = section_overview(runs, meta, a.title)
        lines += section_1v1(runs)
        lines += section_groups(runs)
        lines += ["## Survival", "", "Side A = team 0, B = the others. See `tools/sim_survival.py` for the definitions, `--ships` and `--hitmap`.", "", "### Side A", "", sim_survival.report(runs, "A", False, True), "", "### Side B", "", sim_survival.report(runs, "B", False, True), ""]
        lines += section_problems(runs)
        lines += section_timing(runs)
    text = "\n".join(lines) + "\n"
    if a.out:
        with open(a.out, "w", encoding="utf-8") as f:
            f.write(text)
        sys.stderr.write("wrote %s\n" % a.out)
    else:
        sys.stdout.write(text)


if __name__ == "__main__":
    main()
