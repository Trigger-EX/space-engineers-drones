#!/bin/bash
# Overnight run for Linux Mint / Ubuntu: useful results AND a stability test of the PC, unattended.
#   ./overnight.sh                 8 hours on all logical CPUs
#   ./overnight.sh --hours 6 --jobs 16
# What it does (everything lands in results/, then SE_results.zip):
#   1. plans/enemy.plan from the start: every drone model and mixed team vs your ships in enemies/ (main, hold, long, turret800,
#      traces). Results older than the first overnight run are moved to old_results/<date>/ (made before the BIOS reset).
#   2. the time left: endless rounds of the same matchups with new seeds (100, 101, ...) into results/enemy-main-more.
#   3. a stability check about every 30 minutes: the 336 fights of results/recheck run again and compared fight by fight
#      (the sim is deterministic, so any difference = the PC computed something wrong), plus the kernel's hardware-error count.
#   results/overnight/summary.txt says STABLE or UNSTABLE and why. The PC is kept awake while it runs.
# If the PC crashes or reboots: start it again, it continues where it stopped (finished fights are kept).
# Ctrl+C stops it early (fights in progress are dropped or finished), then it still writes the summary and SE_results.zip.
set -u
cd "$(dirname "$0")"
chmod +x ./*.sh 2>/dev/null
HOURS=8; JOBS=$(nproc 2>/dev/null || echo 4); SLICE_MIN=30
while [ $# -gt 0 ]; do
  case "$1" in
    --hours) HOURS="$2"; shift 2;;
    --jobs) JOBS="$2"; shift 2;;
    --slice-min) SLICE_MIN="$2"; shift 2;;
    *) echo "unknown option $1 (use --hours H, --jobs N)"; exit 1;;
  esac
done
# keep the PC from sleeping or suspending while this runs
if [ -z "${SE_INHIBITED:-}" ] && command -v systemd-inhibit >/dev/null 2>&1 && systemd-inhibit --what=idle:sleep true >/dev/null 2>&1; then
  export SE_INHIBITED=1
  exec systemd-inhibit --what=idle:sleep --who="SE overnight" --why="simulation stress test" "$0" --hours "$HOURS" --jobs "$JOBS" --slice-min "$SLICE_MIN"
fi
. ./ensure_mono.sh

PLAN="${SE_PLAN:-plans/enemy.plan}"                                     # SE_* overrides are for testing the script
SEL=(--enemies enemies --kind enemy --enemy-counts "${SE_COUNTS:-1,2,3,4,6,8,12,16,24}")
CHECK_SEEDS="${SE_CHECK_SEEDS:-3}"
EXE=out/sim/SimHost.exe
OV=results/overnight; LOG=$OV/status.log; mkdir -p "$OV"
START=$(date +%s); END=$(( START + $(awk -v h="$HOURS" 'BEGIN { printf "%d", h * 3600 }') ))
say() { echo "[$(date '+%m-%d %H:%M:%S')] $*" | tee -a "$LOG"; sync "$LOG" 2>/dev/null; }
left() { echo $(( END - $(date +%s) )); }
cnt() { local n; n=$(grep -a -c "$@" 2>/dev/null); echo "${n:-0}"; }     # count matching lines, 0 if the file is missing

# ---- first start or a continuation after a crash ----
if [ ! -f "$OV/state" ]; then
  old="old_results/$(date +%Y%m%d-%H%M)"
  for d in results/selfcheck results/verify-enemy results/enemy-* results/trace-*; do
    [ -d "$d" ] || continue; mkdir -p "$old"; mv "$d" "$old/"; echo "moved $d -> $old/ (made before this overnight run)"
  done
  echo "first start $(date)" > "$OV/state"
else
  if grep -q "^running" "$OV/state"; then
    say "NOTE: the previous overnight run did not finish (PC crash/reset, or the terminal was closed)."
    journalctl -k -b 0 --no-pager 2>/dev/null | grep -a -i -E "BERT|Hardware Error" | head -n 5 | while IFS= read -r l; do say "  this boot: $l"; done
    echo "pc-reset" >> "$OV/events"
  fi
  say "continuing: finished fights are kept"
fi
echo "running since $(date)" >> "$OV/state"
say "overnight: $HOURS h on $JOBS threads, CPU $(lscpu 2>/dev/null | grep -m1 'Model name' | sed 's/.*: *//'), microcode $(grep -m1 microcode /proc/cpuinfo | awk '{print $3}')"

# ---- CPU temperature, sampled every 30 s ----
( while true; do
    t=$(sensors 2>/dev/null | grep -m1 -E "Package id 0|Tctl" | grep -o -E "[+-][0-9]+\.[0-9]" | head -n 1)
    [ -n "$t" ] && echo "$(date +%H:%M:%S) $t" >> "$OV/temps.log"
    sleep 30
  done ) &
TEMP_PID=$!
trap 'kill $TEMP_PID 2>/dev/null' EXIT
STOP=0; CHILD=""
trap 'STOP=1' INT TERM
trap '' PIPE                                # writing Enter to a run that already crashed must not kill this script

mce_count() { journalctl -k --since "@$START" --no-pager 2>/dev/null | grep -a -c "Hardware Error"; }

# run one experiment; a crash of a resumable one restarts it (like run_plan.sh). $1 = name, $2 = SimHost arguments; sets RC
run_exp() {
  local name="$1" args="$2" out="results/$1" rc tries=0 mark=0 why
  mkdir -p "$out"; echo "mono $EXE $args" > "$out/command.txt"
  say "start $name"
  eval "mono $EXE $args" >> "$out/log.txt" 2>&1; rc=$?
  while [ $STOP = 0 ] && [ $rc -ge 128 ] && [ $tries -lt 50 ] && echo "$args" | grep -q -- "--resume"; do
    tries=$((tries + 1))
    why=$(tail -n +$((mark + 1)) "$out/log.txt" | grep -a -m 1 -E "^Got a SIG|Assertion at|Out of memory|could not allocate")
    say "CRASH in $name (rc=$rc): ${why:-no reason printed}; restart $tries"
    echo "crash $name rc=$rc ${why:-}" >> "$OV/events"
    echo "=== restart $tries after rc=$rc ===" >> "$out/log.txt"; mark=$(wc -l < "$out/log.txt")
    eval "mono $EXE $args" >> "$out/log.txt" 2>&1; rc=$?
  done
  if [ $STOP = 1 ]; then
    say "stopped by Ctrl+C during $name"
  elif [ $rc -ge 128 ]; then
    say "CRASH in $name (rc=$rc), not restarted"; echo "crash $name rc=$rc" >> "$OV/events"
  fi
  say "end $name (rc=$rc), $(cnt . "$out/runs.jsonl") fights in file"
  RC=$rc
}

# stability check: the reference fights again, compared fight by fight
REF=$OV/reference.jsonl
if [ ! -f "$REF" ] && [ -f results/recheck/runs.jsonl ]; then cp results/recheck/runs.jsonl "$REF"; fi
check() {
  local c=$OV/check rc res
  [ $STOP = 1 ] && return
  rm -rf "$c"
  mono $EXE run "${SEL[@]}" --seeds "$CHECK_SEEDS" --jobs "$JOBS" --out "$c" > "$OV/check.log" 2>&1; rc=$?
  if [ ! -f "$REF" ]; then
    if [ $rc -eq 0 ]; then cp "$c/runs.jsonl" "$REF"; say "check: no results/recheck found, this pass is the reference from now on"; fi
    return
  fi
  [ -f "$c/runs.jsonl" ] && res=$(python3 compare_runs.py "$REF" "$c/runs.jsonl" | tail -n 1) || res="no results"
  if [ $STOP = 1 ]; then
    return
  elif [ $rc -ge 128 ]; then
    say "CHECK CRASHED (rc=$rc): $(grep -a -m 1 -E '^Got a SIG|Assertion at' "$OV/check.log")"; echo "crash check rc=$rc" >> "$OV/events"
    cp "$OV/check.log" "$OV/check-crash-$(date +%H%M%S).log"
  elif echo "$res" | grep -q " 0 different" && [ "$(cnt . "$c/runs.jsonl")" -eq "$(cnt . "$REF")" ]; then
    say "check OK: $res; hardware errors since start: $(mce_count)"; echo "check-ok" >> "$OV/events"
  else
    say "CHECK FAILED: $res (rc=$rc); hardware errors since start: $(mce_count)"; echo "check-failed $res" >> "$OV/events"
    python3 compare_runs.py "$REF" "$c/runs.jsonl" > "$OV/check-failed-$(date +%H%M%S).txt"
  fi
}

# ---- 1. the plan ----
check
while IFS= read -r line; do
  case "$line" in ''|\#*) continue;; esac
  [ $STOP = 1 ] && break
  [ "$(left)" -le 0 ] && { say "time is up, skipping the rest of the plan"; break; }
  name="$(echo "${line%%|*}" | xargs)"; args="${line#*|}"
  args="${args//\{OUT\}/results/$name}"
  case "$args" in *" run "*|" run"*) args="$args --jobs $JOBS";; esac
  if [ -f "results/$name/done" ]; then say "skip $name (finished earlier)"; continue; fi
  run_exp "$name" "$args"
  [ $RC = 0 ] && [ $STOP = 0 ] && touch "results/$name/done"
  case "$args" in *--resume*) check;; esac                          # after each big experiment
done < "$PLAN"

# ---- 2. the time left: endless rounds with new seeds, in slices with a check after each ----
MORE=results/enemy-main-more
while [ $STOP = 0 ] && [ "$(left)" -gt 300 ]; do
  s=$(( SLICE_MIN * 60 )); [ "$s" -gt "$(left)" ] && s=$(left)
  say "endless slice $(( s / 60 )) min -> $MORE"
  mkdir -p "$MORE"; mark=$(cnt "" "$MORE/log.txt")
  # stdin is a pipe: Enter at the end of the slice = SimHost's own "finish the fights in progress, save, stop"
  rm -f "$OV/stdin"; mkfifo "$OV/stdin"
  mono $EXE run --endless "${SEL[@]}" --seed-start 100 --jobs "$JOBS" --out "$MORE" >> "$MORE/log.txt" 2>&1 < "$OV/stdin" &
  CHILD=$!; exec 3> "$OV/stdin"
  t_end=$(( $(date +%s) + s ))
  while [ $STOP = 0 ] && [ "$(date +%s)" -lt $t_end ] && kill -0 $CHILD 2>/dev/null; do sleep 5; done
  kill -0 $CHILD 2>/dev/null && echo >&3 2>/dev/null
  t_kill=$(( $(date +%s) + 900 )); hung=0
  while kill -0 $CHILD 2>/dev/null; do
    [ "$(date +%s)" -ge $t_kill ] && { kill -9 $CHILD 2>/dev/null; hung=1; }
    sleep 2
  done
  wait $CHILD; rc=$?; exec 3>&-; CHILD=""
  if [ $STOP = 1 ]; then
    say "stopped by Ctrl+C"
  elif [ $hung = 1 ]; then
    say "HANG: the endless run did not stop within 15 min after its slice and was killed"; echo "hang endless" >> "$OV/events"
  elif [ $rc -ge 128 ]; then
    why=$(tail -n +$((mark + 1)) "$MORE/log.txt" | grep -a -m 1 -E "^Got a SIG|Assertion at|Out of memory|could not allocate")
    say "CRASH in endless (rc=$rc): ${why:-no reason printed}"; echo "crash endless rc=$rc ${why:-}" >> "$OV/events"
  fi
  say "endless: $(cnt . "$MORE/runs.jsonl") fights in file"
  check
done

# ---- summary ----
ev() { cnt -E "^$1( |$)" "$OV/events"; }
crashes=$(ev crash); hangs=$(ev hang); ok=$(ev check-ok); bad=$(ev check-failed); resets=$(ev pc-reset)
mce=$(mce_count); tmax=$(awk '{ gsub(/\+/, "", $2); if ($2 + 0 > m) m = $2 + 0 } END { print m + 0 }' "$OV/temps.log" 2>/dev/null)
verdict=STABLE
[ $((crashes + hangs + bad + resets)) -gt 0 ] || [ "${mce:-0}" -gt 0 ] && verdict=UNSTABLE
{
  echo "overnight $(date -d @"$START" '+%F %T') -> $(date '+%F %T'), $JOBS threads"
  echo "VERDICT: $verdict"
  echo "  stability checks: $ok passed, $bad failed ($(cnt . "$REF") fights each, compared with the reference)"
  echo "  simulator crashes: $crashes, hangs: $hangs, PC crashes/resets during the run: $resets"
  echo "  kernel hardware errors (Machine Check) since start: ${mce:-?}"
  echo "  hottest CPU package: $([ "${tmax:-0}" != 0 ] && echo "$tmax C" || echo "n/a (no sensors)")"
  echo "fights in results:"
  for f in results/*/runs.jsonl; do
    case "$f" in results/overnight/*) continue;; esac
    echo "  $(cnt . "$f") $(dirname "$f")"
  done
  [ $STOP = 1 ] && echo "Stopped early with Ctrl+C."
  [ "$verdict" = UNSTABLE ] && echo "Results made while unstable can be wrong; see results/overnight/status.log."
} > "$OV/summary.txt"
sed -i 's/^running since/finished, was running since/' "$OV/state"
say "done"; cat "$OV/summary.txt"
./pack_results.sh
echo; echo "Done. Send me:  $(pwd)/SE_results.zip"
