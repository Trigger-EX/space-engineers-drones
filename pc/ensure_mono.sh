#!/bin/bash
# Sourced by run_mint.sh and run_endless.sh: installs mono with apt if missing, then a 5 s self-check (exits on failure).
# Mono's concurrent garbage collector crashed (SIGSEGV) with 19 simulation threads on a 20-thread CPU: use the plain one.
export MONO_GC_PARAMS="${MONO_GC_PARAMS:-major=marksweep}"
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
