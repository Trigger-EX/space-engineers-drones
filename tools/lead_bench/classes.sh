#!/bin/bash
# M3 acceptance (a): hit rate of the shipped LEAD v1.0 solver per BATTERY class (rocket / gatling = ballistic / rail / shell) against strafing, turning and orbiting
# targets (docs/design/control_arch.md M3). The Helm's BATTERY block calls the SAME solver as the class' own script (tools/tests/test_battery_solvers.py proves the
# code is identical: Lead() = Hornet / Longbow, BatRkt = Basilisk Lead + RktPath), so the "SHIPPED" row IS the row of the class' current script; this prints it.
# Usage: bash tools/lead_bench/classes.sh [seeds]     (needs out/sim/SeApi.dll from sim/build.sh)
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; SEEDS=${1:-8}; W="$(mktemp -d)"
mcs -langversion:7.2 -optimize+ -nowarn:0168,0219,0414 -r:"$ROOT/out/sim/SeApi.dll" -out:"$W/bench.exe" "$ROOT/tools/lead_bench/Bench.cs" 2>&1 | grep -v warning; cp "$ROOT/out/sim/SeApi.dll" "$W/"
printf "%-8s %-11s %-6s %s\n" class pattern target "hit % of the shipped LEAD v1.0 solver"
for w in rocket gatling rail shell; do
  for sc in strafe strafeturn orbit; do
    for sz in small large; do
      mono "$W/bench.exe" weapon=$w scen=$sc size=$sz truth=1 seeds=$SEEDS | awk -v w=$w -v sc=$sc -v sz=$sz '/SHIPPED/ {s=$0; sub(/.*hit +/,"",s); sub(/%.*/,"",s); ship=s} END {printf "%-8s %-11s %-6s %s\n", w, sc, sz, ship}'
    done
  done
done
rm -rf "$W"
