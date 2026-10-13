#!/bin/bash
# One-step runner for Linux Mint / Ubuntu / Debian.
#   ./run_mint.sh                      install mono if missing, run the whole plan, pack the results
#   ./run_mint.sh --only verify,full-fresh        run only some experiments
#   ./run_mint.sh --jobs 12            use 12 threads (default: all logical CPUs minus 1)
# Safe to stop with Ctrl+C and start again: it resumes where it stopped.
set -u
cd "$(dirname "$0")"
chmod +x ./*.sh 2>/dev/null

. ./ensure_mono.sh


./run_plan.sh "$@"
./pack_results.sh
echo
echo "Done. Send me:  $(pwd)/SE_results.zip"
