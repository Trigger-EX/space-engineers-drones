#!/bin/bash
# Endless simulation for Linux Mint / Ubuntu / Debian: fights every matchup round after round, shows a live count,
# and stops when you press Enter (fights in progress finish, everything is saved; Enter twice = stop at once).
#   ./run_endless.sh                     your ships in enemies/ vs 1..24 of every drone ("1vX")
#   ./run_endless.sh --jobs 12           fewer threads (default: all logical CPUs minus 1)
#   ./run_endless.sh --kind 1v1          any other selection instead (same options as SimHost run: --filter, --kind, ...)
# Running it again continues with new seeds (results/endless keeps growing). It writes SE_results.zip when you stop.
set -u
cd "$(dirname "$0")"
chmod +x ./*.sh 2>/dev/null
. ./ensure_mono.sh
JOBS=$(( $(nproc 2>/dev/null || echo 4) - 1 )); [ "$JOBS" -lt 1 ] && JOBS=1
SEL=(--enemies enemies --kind enemy --enemy-counts 1,2,3,4,6,8,12,16,24)
EXTRA=()
while [ $# -gt 0 ]; do
  case "$1" in
    --jobs) JOBS="$2"; shift 2;;
    --kind|--filter) SEL=(--enemies enemies "$1" "$2"); shift 2;;
    *) EXTRA+=("$1"); shift;;
  esac
done
mkdir -p results/endless; echo "machine: $(uname -a); cpus: $(nproc); jobs: $JOBS" > results/machine.txt
tries=0
while true; do
  mono out/sim/SimHost.exe run --endless "${SEL[@]}" "${EXTRA[@]}" --jobs "$JOBS" --out results/endless
  rc=$?
  [ $rc -lt 128 ] && break                     # 0 = you pressed Enter; 1..5 = a usage error (shown above)
  tries=$((tries + 1)); [ $tries -gt 50 ] && { echo "crashed 50 times, giving up"; break; }
  echo "simulator crashed (rc=$rc); restarting, finished fights are kept (press Enter in the new run to stop)"
  sleep 1
done
./pack_results.sh
echo
echo "Done. Send me:  $(pwd)/SE_results.zip"
