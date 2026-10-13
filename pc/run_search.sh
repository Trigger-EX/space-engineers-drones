#!/bin/bash
# Fleet search for Linux Mint / Ubuntu / Debian: which mix of drones (up to 10, any models) beats your ship?
# Tries random fleets, breeds variations of the best ones, shows a live count and prints new leaders as they appear.
# Press Enter to stop: fights in progress finish, the leaderboard is saved (results/search/leaderboard.csv) and SE_results.zip is written.
#   ./run_search.sh                        vs the first ship in enemies/ (Tech Hunter), 1-10 drones, 10-minute fights
#   ./run_search.sh --max-drones 6         smaller fleets      ./run_search.sh --jobs 12      fewer threads
#   ./run_search.sh --models "Hornet,Gnat,Longbow L,Basilisk"   only these models      --target "Other Ship"   another ship in enemies/
#   ./run_search.sh --enemy-brain hold     the ship holds position instead of attacking
# Running it again continues the same search (results/search keeps growing).
set -u
cd "$(dirname "$0")"
chmod +x ./*.sh 2>/dev/null
. ./ensure_mono.sh
JOBS=$(( $(nproc 2>/dev/null || echo 4) - 1 )); [ "$JOBS" -lt 1 ] && JOBS=1
ARGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --jobs) JOBS="$2"; shift 2;;
    *) ARGS+=("$1"); shift;;
  esac
done
mkdir -p results/search; echo "machine: $(uname -a); cpus: $(nproc); jobs: $JOBS" > results/machine.txt
tries=0
while true; do
  mono out/sim/SimHost.exe search --enemies enemies --cap-seconds 600 --jobs "$JOBS" --out results/search "${ARGS[@]}"
  rc=$?
  [ $rc -lt 128 ] && break                     # 0 = you pressed Enter; 1..5 = a usage error (shown above)
  tries=$((tries + 1)); [ $tries -gt 50 ] && { echo "crashed 50 times, giving up"; break; }
  echo "simulator crashed (rc=$rc); restarting, finished fights are kept (press Enter in the new run to stop)"
  sleep 1
done
./pack_results.sh
echo
echo "Done. Send me:  $(pwd)/SE_results.zip"
