#!/bin/bash
# Printer regression: the REAL Hive Mother script on the REAL 'Mobile Hive' blueprint for 320 sim seconds, plain and with real-game-like
# projector/welder faults (sim/src/Run/PrinterTest.cs). The current script must never print ERROR and must keep printing around a failing bay.
# Usage: SIM_OUT=<dir> bash sim/build.sh && python3 -I tools/build_all.py --only "Mobile Hive" && SIM_OUT=<dir> bash sim/printer_test.sh [--old]
#   --old  also runs the old v2.0 / v2.1 scripts from git history (read-only `git show`) to show the NullReferenceException they die of (informational)
# The PB runs the script text stored in out/Blueprints/Mobile Hive/bp.sbc: rebuild the blueprint first.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; OUT="${SIM_OUT:-$ROOT/out/sim}"; H="mono $OUT/SimHost.exe printer-test --repo $ROOT"
fail=0
run() { # name, extra args...
  local name="$1"; shift
  local o; o=$($H "$@" 2>&1)
  if echo "$o" | grep -q "^PRINTER OK"; then echo "PASS  $name"; else echo "FAIL  $name"; fail=1; fi
  echo "$o" | grep -E "^RESULT|^FIRST THROW|^  throw site|^LAST ERROR" | sed 's/^/        /'
}
run "plain sim projector, no ERROR allowed"             --seconds 320 --fault none --expect-clean
run "no projection at all (counters throw NRE)"          --seconds 320 --fault noproj
run "projection stats not ready for 3 s at 40 s"         --seconds 320 --fault halfload --fault-at 40
run "projection lost at 60 s (Keep Projection lost)"    --seconds 320 --fault projlost --fault-at 60
run "projector stuck off"                                --seconds 320 --fault projoff
run "IsProjecting throws for 3 s at 40 s"                --seconds 320 --fault isproj --fault-at 40
run "welder 1 of every bay removed at 50 s"              --seconds 320 --fault welderclosed --fault-at 50
run "bay 1 halfload + bay 3 projlost + welders closed"   --seconds 320 --fault all --fault-at 45
if [ "$1" = "--old" ]; then
  tmp="$(mktemp -d)"
  git -C "$ROOT" show c144d8c:scripts/HiveMotherAI.cs > "$tmp/v20.cs"
  git -C "$ROOT" show 77d6d56:scripts/HiveMotherAI.cs > "$tmp/v21.cs"
  echo "--- old scripts (informational, a FAIL here is the bug being reproduced) ---"
  run "OLD v2.0 halfload"  --seconds 120 --fault halfload --fault-at 40 --script-file "$tmp/v20.cs"
  run "OLD v2.1 halfload"  --seconds 120 --fault halfload --fault-at 40 --script-file "$tmp/v21.cs"
  rm -rf "$tmp"
fi
[ $fail = 0 ] && echo "PRINTER ALL OK" || echo "PRINTER FAILURES"
exit $fail
