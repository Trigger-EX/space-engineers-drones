#!/bin/bash
# Zips everything under results/ into SE_results.zip (works with or without the 'zip' program).
cd "$(dirname "$0")"; rm -f SE_results.zip
if command -v zip >/dev/null 2>&1; then
  zip -q -r SE_results.zip results -x 'results/*/runs.jsonl.tmp'
else
  python3 - <<'PY'
import os, zipfile
with zipfile.ZipFile("SE_results.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for d, _, fs in os.walk("results"):
        for f in fs:
            if not f.endswith(".tmp"): z.write(os.path.join(d, f))
PY
fi
ls -la SE_results.zip
