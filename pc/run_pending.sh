#!/bin/bash
# Runs plans/pending.plan (the open sim tests) in rounds until you stop it: round 1 with N=5 seeds per line, then 10, 20, 40, ...
# Each round only adds seeds (--resume), so stopping at any time leaves every test with about the same amount of data.
#   ./run_pending.sh                  start, or continue where it stopped
#   ./run_pending.sh --jobs 12        fewer threads (default: all logical CPUs minus 1)
#   ./run_pending.sh --start-n 20     first round size (default 5); --max-n 640 stops after the round with N=640
#   ./run_pending.sh --only retro-pirate-base,matrix-1v1     only these lines (names; "charge-" matches every charge line)
# Stop: press Enter (or Ctrl+C once). The fight in progress is dropped, every finished fight is kept, summaries are rewritten and SE_results.zip is packed.
# Run it again later and it continues. Works in the SimPack folder (out/sim next to this script) or in a repo checkout (../out/sim).
cd "$(dirname "$0")"
PLAN=plans/pending.plan; JOBS=$(( $(nproc 2>/dev/null || echo 4) - 1 )); N=5; MAXN=1000000; ONLY=""
while [ $# -gt 0 ]; do case "$1" in
  --jobs) JOBS="$2"; shift 2;; --start-n) N="$2"; shift 2;; --max-n) MAXN="$2"; shift 2;; --only) ONLY="$2"; shift 2;;
  *) PLAN="$1"; shift;; esac; done
[ "$JOBS" -lt 1 ] && JOBS=1
[ -f ensure_mono.sh ] && [ -f out/sim/SimHost.exe ] && . ./ensure_mono.sh     # installs mono if needed + 5 s self-check (SimPack layout)
export MONO_GC_PARAMS="${MONO_GC_PARAMS:-major=marksweep}" MONO_CRASH_NOFILE=1
EXE=out/sim/SimHost.exe; [ -f "$EXE" ] || EXE=../out/sim/SimHost.exe
[ -f "$EXE" ] || { echo "SimHost.exe not found (out/sim): unzip a SimPack built from the current scripts"; exit 1; }
mkdir -p results; echo "machine: $(uname -a); cpus: $(nproc); jobs: $JOBS" > results/machine.txt
STATE=results/pending_state.txt; [ -f "$STATE" ] && N=$(cat "$STATE")      # continue at the round that was running
LOG=results/pending_progress.txt

# expand the plan: "@plan FILE {SEEDS}" includes FILE's lines with --seeds <n> replaced by {SEEDS}
LINES=()
while IFS= read -r line; do
  case "$line" in ''|\#*) continue;; esac
  if [[ "$line" == @plan* ]]; then
    read -r _ f tok <<< "$line"
    while IFS= read -r l2; do case "$l2" in ''|\#*) continue;; esac; LINES+=("$(echo "$l2" | sed -E "s/--seeds [0-9]+/--seeds $tok/")"); done < "$f"
  else LINES+=("$line"); fi
done < "$PLAN"
echo "${#LINES[@]} experiments in $PLAN"

STOP=0
finish() {
  [ -n "${WATCH:-}" ] && kill "$WATCH" 2>/dev/null
  echo; echo "Stopping: writing summaries and SE_results.zip (finished fights are kept; run ./run_pending.sh again to continue)"
  for d in results/*/; do
    [ -s "$d/runs.jsonl" ] && mono "$EXE" summarize --in "$d/runs.jsonl" > /dev/null 2>&1
  done
  [ -f pack_results.sh ] && ./pack_results.sh
  exit 0
}
CHILD=""
trap 'STOP=1; [ -n "$CHILD" ] && kill -TERM "$CHILD" 2>/dev/null' INT TERM
# Enter = Ctrl+C (needs a terminal; with nohup / no tty only Ctrl+C or kill works)
WATCH=""
if [ -t 0 ]; then ( read -r _ < /dev/tty && kill -TERM $$ ) 2>/dev/null & WATCH=$!; fi

while [ "$N" -le "$MAXN" ]; do
  echo "$N" > "$STATE"
  echo "##### round N=$N ($(date '+%F %H:%M:%S'))" | tee -a "$LOG"
  for line in "${LINES[@]}"; do
    [ "$STOP" = 1 ] && finish
    name="$(echo "${line%%|*}" | xargs)"; args="${line#*|}"
    if [ -n "$ONLY" ]; then ok=0; IFS=, read -ra sel <<< "$ONLY"; for s in "${sel[@]}"; do [[ "$name" == "$s" || ( "$s" == *- && "$name" == "$s"* ) ]] && ok=1; done; [ $ok = 0 ] && continue; fi
    out="results/$name"; mkdir -p "$out"
    args="${args//\{OUT\}/$out}"; args="${args//\{N\}/$N}"; args="${args//\{N\*4\}/$((N * 4))}"; args="${args//\{N\*2\}/$((N * 2))}"
    case "$args" in *" run "*|" run"*) args="$args --jobs $JOBS";; esac
    echo "$EXE $args" > "$out/command.txt"
    start=$(date +%s); tries=0
    while :; do
      eval "mono $EXE $args" >> "$out/log.txt" 2>&1 < /dev/null &
      CHILD=$!; wait $CHILD; rc=$?
      while kill -0 $CHILD 2>/dev/null; do wait $CHILD; rc=$?; done     # a stop signal interrupts wait, not the simulator: it is killed by the trap
      CHILD=
      [ "$STOP" = 1 ] && break
      [ $rc -ge 128 ] && [ $tries -lt 20 ] && { tries=$((tries + 1)); echo "  $name crashed (rc=$rc), restart $tries" | tee -a "$LOG"; continue; }
      break
    done
    [ "$STOP" = 1 ] && finish
    fights=$(wc -l < "$out/runs.jsonl" 2>/dev/null || echo 0)
    echo "rc=$rc seconds=$(( $(date +%s) - start )) N=$N fights=$fights" > "$out/status.txt"
    printf '%s  %-34s rc=%s %4ss fights=%s\n' "$(date +%H:%M:%S)" "$name" "$rc" "$(( $(date +%s) - start ))" "$fights" | tee -a "$LOG"
  done
  N=$((N * 2))
done
finish
