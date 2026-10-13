#!/bin/bash
# One-step runner for Linux Mint / Ubuntu / Debian.
#   ./run_mint.sh                      install mono if missing, run the whole plan, pack the results
#   ./run_mint.sh --only verify,full-fresh        run only some experiments
#   ./run_mint.sh --jobs 12            use 12 threads (default: all logical CPUs minus 1)
# Safe to stop with Ctrl+C and start again: it resumes where it stopped.
set -u
cd "$(dirname "$0")"
chmod +x ./*.sh 2>/dev/null

need=0
command -v mono >/dev/null 2>&1 || need=1
mono -V >/dev/null 2>&1 && ls /usr/lib/mono/4.5/System.Xml.dll /usr/lib/mono/4.5/System.Core.dll >/dev/null 2>&1 || need=1
if [ "$need" = 1 ]; then
  echo "Mono runtime not found (or incomplete). Installing it with apt (needs your sudo password)..."
  sudo apt-get update && sudo apt-get install -y mono-runtime libmono-system-core4.0-cil libmono-system-xml4.0-cil libmono-system-numerics4.0-cil zip
  if ! ls /usr/lib/mono/4.5/System.Xml.dll >/dev/null 2>&1; then
    echo "Minimal install did not give a complete runtime; installing mono-complete instead (bigger download)..."
    sudo apt-get install -y mono-complete zip || { echo "Mono install failed. Install 'mono-complete' manually and re-run."; exit 1; }
  fi
fi

echo "Mono: $(mono --version | head -n 1)"
echo "CPU : $(lscpu 2>/dev/null | grep -m1 'Model name' | sed 's/.*: *//')  (logical CPUs: $(nproc))"
echo "--- quick self-check (5 s) ---"
if ! mono out/sim/SimHost.exe selftest all > /tmp/se_selfcheck.txt 2>&1 || ! grep -q "SELFTEST OK" /tmp/se_selfcheck.txt; then
  echo "Self-check FAILED. Last lines:"; tail -n 15 /tmp/se_selfcheck.txt
  echo "Send me that output. (If it mentions a missing System.* assembly, run: sudo apt install mono-complete)"; exit 1
fi
echo "self-check OK"

./run_plan.sh "$@"
./pack_results.sh
echo
echo "Done. Send me:  $(pwd)/SE_results.zip"
