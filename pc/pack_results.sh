#!/bin/bash
cd "$(dirname "$0")"; rm -f SE_results.zip
zip -q -r SE_results.zip results -x 'results/*/runs.jsonl.tmp'
ls -la SE_results.zip
