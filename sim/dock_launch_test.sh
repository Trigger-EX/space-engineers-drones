#!/bin/bash
# Dock-launch regression: a printed drone merged to a dummy printer, merge off at 3 s, real script in its blueprint default state.
# Usage: SIM_OUT=<dir> bash sim/build.sh && SIM_OUT=<dir> bash sim/dock_launch_test.sh [Model ...]   (default: Hornet Harrier)
# Rebuild the model blueprint first (python3 -I tools/build_all.py --only "<Model>"): the PB runs the script text stored in out/Blueprints.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; OUT="${SIM_OUT:-$ROOT/out/sim}"; H="mono $OUT/SimHost.exe dock-launch --repo $ROOT"
models=("$@"); [ ${#models[@]} -eq 0 ] && models=(Hornet Harrier)
fail=0
run() { # name, expect (0 = must launch, 1 = must stay), args...
  local name="$1" exp="$2"; shift 2
  local line; line=$($H "$@" 2>&1 | grep -E "^RESULT|^LAUNCH")
  local rc=0; echo "$line" | grep -q "^LAUNCH OK" || rc=1
  if [ "$rc" = "$exp" ]; then echo "PASS  $name"; else echo "FAIL  $name"; fail=1; fi
  echo "$line" | grep RESULT | sed 's/^/        /'
}
for m in "${models[@]}"; do
  run "$m default (printer side merge off, no JOIN, no arming)"      0 --model $m --seconds 20
  run "$m both merge blocks off"                                      0 --model $m --seconds 20 --both-merges
  run "$m printer arms the PB at 1 s (Custom Data + argument)"        0 --model $m --seconds 20 --arm-at 1 --custom-data "[Launch]\nArmed=true\nAway Z=-1"
  run "$m stored Away points into the printer (ignored)"              0 --model $m --seconds 20 --arm-at 1 --custom-data "[Launch]\nArmed=true\nAway Z=1"
  run "$m PB recompiled while docked"                                 0 --model $m --seconds 20 --recompile-at 2
  run "$m PB recompiled during the burn"                              0 --model $m --seconds 20 --recompile-at 3.4
  run "$m PB first runs after the release, Armed in Custom Data"      0 --model $m --seconds 20 --release-at 0 --custom-data "[Launch]\nArmed=true"
  run "$m PB first runs after the release, only a JOIN at 2 s"        0 --model $m --seconds 20 --release-at 0 --join-at 2
  run "$m Auto=false stays until told (expect: stays)"                1 --model $m --seconds 20 --custom-data "[Launch]\nAuto=false"
  run "$m never docked, no trigger (expect: stays)"                   1 --model $m --seconds 20 --release-at 0
done
[ $fail = 0 ] && echo "DOCK-LAUNCH ALL OK" || echo "DOCK-LAUNCH FAILURES"
exit $fail
