#!/bin/bash
# Dock-launch regression: a printed drone merged to a dummy printer, merge off at 3 s, real script in its blueprint default state.
# Usage: SIM_OUT=<dir> bash sim/build.sh && SIM_OUT=<dir> bash sim/dock_launch_test.sh [Model ...]   (default: Hornet Harrier)
#        bash sim/dock_launch_test.sh --cradle [SEEDS]   (M5, S13) a REAL merge-block cradle instead of the dummy printer: "Cradle Pod Test" (the 2-slot Cradle Pod, two Hornets) and
#        "Cradle Test" (Corsair Wing Lite, real Helm / Staff / Bay scripts, two Hornets), SEEDS (default 10) seeds each, 'release all' to the bay PB at 3 s: no FUSED, every drone
#        separates within 3 s of its release, the two merge-offs are 2 s apart, the burn is along the lane axis and no drone block touches an occupied host cell; then the negative
#        controls (a spinning host, a lane aimed into the wall, a half-charged drone: the release must wait) and 'force'. Needs: python3 -I tools/build_all.py --only "Cradle Pod Test" "Cradle Test" Hornet --no-zip
# Rebuild the model blueprint first (python3 -I tools/build_all.py --only "<Model>"): the PB runs the script text stored in out/Blueprints.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; OUT="${SIM_OUT:-$ROOT/out/sim}"; H="mono $OUT/SimHost.exe dock-launch --repo $ROOT"
fail=0
if [ "${1:-}" = "--cradle" ]; then
  seeds="${2:-10}"
  crun() { # name, extra args...
    local name="$1"; shift
    local out; out=$($H --cradle "$@" 2>&1); local rc=$?
    if [ $rc = 0 ]; then echo "PASS  $name"; else echo "FAIL  $name"; fail=1; echo "$out" | grep -E "FAIL:|RESULT" | sed 's/^/        /'; fi
    echo "$out" | grep RESULT | sed 's/^/        /'
  }
  for m in "Cradle Pod Test" "Cradle Test"; do
    for s in $(seq 1 "$seeds"); do crun "$m seed $s: release all" --model "$m" --seed "$s" --seconds 14 --release-at 3; done
  done
  crun "Cradle Test: spinning host (expect: waits)"            --model "Cradle Test" --helm none --spin 0.4 --seconds 12 --expect stay
  crun "Cradle Test: spinning host, release all force"         --model "Cradle Test" --helm none --spin 0.4 --seconds 12 --cmd "release all force" --expect forced
  crun "Cradle Pod Test: lane aimed into the wall (expect: waits)" --model "Cradle Pod Test" --away "0,0,1" --seconds 12 --expect stay
  crun "Cradle Pod Test: drones at 30 % charge (expect: waits at CHARGING)" --model "Cradle Pod Test" --charge 0.3 --seconds 8 --expect stay
  crun "Cradle Pod Test: drone 1 bridged to the pod by an armour block (expect: FAULT FUSED in bay 1, bay 2 released)" --model "Cradle Pod Test" --fuse-bridge --seconds 12 --expect fused
  crun "Cradle Test: 'Run Staff release 1' to the Staff PB"    --model "Cradle Test" --via staff --cmd "release 1" --seconds 14 --expect first
  [ $fail = 0 ] && echo "DOCK-LAUNCH CRADLE ALL OK" || echo "DOCK-LAUNCH CRADLE FAILURES"
  exit $fail
fi
models=("$@"); [ ${#models[@]} -eq 0 ] && models=(Hornet Harrier)
run() { # name, expect (0 = must launch, 1 = must stay), args...
  local name="$1" exp="$2"; shift 2
  local line; line=$($H "$@" 2>&1 | grep -E "^RESULT|^LAUNCH")
  local rc=0; echo "$line" | grep -q "^LAUNCH OK" || rc=1
  if [ "$rc" = "$exp" ]; then echo "PASS  $name"; else echo "FAIL  $name"; fail=1; fi
  echo "$line" | grep RESULT | sed 's/^/        /'
}
for m in "${models[@]}"; do
  run "$m default (printer side merge off, no JOIN, no arming)"      0 --model "$m" --seconds 20
  run "$m both merge blocks off"                                      0 --model "$m" --seconds 20 --both-merges
  run "$m printer arms the PB at 1 s (Custom Data + argument)"        0 --model "$m" --seconds 20 --arm-at 1 --custom-data "[Launch]\nArmed=true\nAway Z=-1"
  run "$m stored Away points into the printer (ignored)"              0 --model "$m" --seconds 20 --arm-at 1 --custom-data "[Launch]\nArmed=true\nAway Z=1"
  run "$m PB recompiled while docked (optimistic)"                           0 --model "$m" --seconds 20 --recompile-at 2
  run "$m PB recompiled during the burn (optimistic)"                            0 --model "$m" --seconds 20 --recompile-at 3.4
  run "$m PB first runs after the release, Armed in Custom Data"      0 --model "$m" --seconds 20 --release-at 0 --custom-data "[Launch]\nArmed=true"
  run "$m PB first runs after the release, only a JOIN at 2 s"        0 --model "$m" --seconds 20 --release-at 0 --join-at 2
  run "$m Auto=false stays until told (expect: stays)"                1 --model "$m" --seconds 20 --custom-data "[Launch]\nAuto=false"
  run "$m never docked, no trigger (expect: stays)"                   1 --model "$m" --seconds 20 --release-at 0
done
[ $fail = 0 ] && echo "DOCK-LAUNCH ALL OK" || echo "DOCK-LAUNCH FAILURES"
exit $fail
