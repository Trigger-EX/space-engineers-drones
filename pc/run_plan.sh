#!/bin/bash
# Linux / WSL / macOS-with-mono runner.  Usage: ./run_plan.sh [plan file] [--only a,b] [--jobs N]
cd "$(dirname "$0")"
PLAN=plans/default.plan; ONLY=""; JOBS=$(( $(nproc 2>/dev/null || echo 4) - 1 ))
while [ $# -gt 0 ]; do case "$1" in --only) ONLY="$2"; shift 2;; --jobs) JOBS="$2"; shift 2;; *) PLAN="$1"; shift;; esac; done
[ "$JOBS" -lt 1 ] && JOBS=1
export MONO_GC_PARAMS="${MONO_GC_PARAMS:-major=marksweep}"   # see ensure_mono.sh
export MONO_CRASH_NOFILE=1
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
  # a crash (signal: rc >= 128) of a resumable run: restart it, finished fights are kept (up to 50 times)
  tries=0; mark=0   # mark = log lines before the current attempt, so a crash reason is taken from that attempt only
  while [ $rc -ge 128 ] && [ $tries -lt 50 ] && echo "$args" | grep -q -- "--resume"; do
    tries=$((tries + 1)); echo "  crashed (rc=$rc), restart $tries (finished fights are kept)"
    why=$(tail -n +$((mark + 1)) "$out/log.txt" | grep -a -m 1 -E "^Got a SIG|Assertion at|Out of memory|could not allocate")
    [ -n "$why" ] && echo "    reason: $why (details: $out/log.txt)"
    echo "=== restart $tries after rc=$rc ===" >> "$out/log.txt"; mark=$(wc -l < "$out/log.txt")
    eval "$RUN $EXE $args" >> "$out/log.txt" 2>&1; rc=$?
  done
  echo "rc=$rc seconds=$(( $(date +%s) - start ))" | tee "$out/status.txt"; tail -n 2 "$out/log.txt"
done < "$PLAN"
echo "all done. Now run: ./pack_results.sh"
