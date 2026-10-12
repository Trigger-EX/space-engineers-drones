#!/bin/bash
# Linux / WSL / macOS-with-mono runner.  Usage: ./run_plan.sh [plan file] [--only a,b] [--jobs N]
cd "$(dirname "$0")"
PLAN=plans/default.plan; ONLY=""; JOBS=$(( $(nproc 2>/dev/null || echo 4) - 1 ))
while [ $# -gt 0 ]; do case "$1" in --only) ONLY="$2"; shift 2;; --jobs) JOBS="$2"; shift 2;; *) PLAN="$1"; shift;; esac; done
[ "$JOBS" -lt 1 ] && JOBS=1
EXE="out/sim/SimHost.exe"; RUN="mono"; command -v mono >/dev/null || { echo "mono not found (install mono-runtime)"; exit 1; }
mkdir -p results; echo "machine: $(uname -a); cpus: $(nproc); jobs: $JOBS" > results/machine.txt
while IFS= read -r line; do
  case "$line" in ''|\#*) continue;; esac
  name="$(echo "${line%%|*}" | xargs)"; args="${line#*|}"
  if [ -n "$ONLY" ] && ! echo ",$ONLY," | grep -q ",$name,"; then continue; fi
  out="results/$name"; mkdir -p "$out"; args="${args//\{OUT\}/$out}"
  case "$args" in *" run "*|" run"*) args="$args --jobs $JOBS";; esac
  echo "=== $name ($(date +%H:%M:%S)) ==="; echo "$RUN $EXE $args" > "$out/command.txt"
  start=$(date +%s)
  eval "$RUN $EXE $args" > "$out/log.txt" 2>&1; rc=$?
  echo "rc=$rc seconds=$(( $(date +%s) - start ))" | tee "$out/status.txt"; tail -n 2 "$out/log.txt"
done < "$PLAN"
echo "all done. Now run: ./pack_results.sh"
