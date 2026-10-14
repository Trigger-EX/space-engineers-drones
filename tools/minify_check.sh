#!/bin/bash
# Proves tools/minify_cs.py changes nothing but whitespace/comments: every scripts/*.cs is compiled as written and minified
# (same PB wrapper as sim/build.sh, against stubs.cs) and the two assemblies' IL is compared (ikdasm, MVID lines dropped).
# Usage: tools/minify_check.sh   (needs out/sim/SeApi.dll + SimShim.dll from sim/build.sh)
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; OUT="${SIM_OUT:-$ROOT/out/sim}"; W="$(mktemp -d)"; mkdir -p "$W/src" "$W/min"
USINGS=$(sed -n '/^USINGS=/,/^using Random = SimShim.DetRandom;.$/p' "$ROOT/sim/build.sh" | sed "1s/^USINGS='//; \$s/'$//")
rc=0
for f in "$ROOT"/scripts/*.cs; do
  n=$(basename "$f" .cs)
  for v in src min; do
    { echo "$USINGS"; echo "public partial class Program : MyGridProgram {";
      if [ $v = src ]; then cat "$f"; else python3 -I -c 'import sys; sys.path.insert(0, sys.argv[2]); from minify_cs import minify; sys.stdout.write(minify(open(sys.argv[1], encoding="utf-8").read()))' "$f" "$ROOT/tools"; fi
      echo; echo "}"; } > "$W/$n.$v.cs"
    mcs -langversion:6 -target:library -optimize+ -nowarn:0169,0414,0649,0219 -r:"$OUT/SeApi.dll" -r:"$OUT/SimShim.dll" -out:"$W/$v/$n.dll" "$W/$n.$v.cs" > "$W/$n.$v.log" 2>&1 || { echo "COMPILE FAIL $n ($v)"; cat "$W/$n.$v.log" | head -5; rc=1; continue 2; }
    ikdasm "$W/$v/$n.dll" 2>/dev/null | grep -v -E '^\s*// MVID|\.module |\.ver |^// ' > "$W/$n.$v.il"
  done
  len=$(python3 -I -c 'import sys; sys.path.insert(0, sys.argv[2]); from minify_cs import minify; print(len(minify(open(sys.argv[1], encoding="utf-8").read())))' "$f" "$ROOT/tools")
  if cmp -s "$W/$n.src.il" "$W/$n.min.il"; then echo "same IL  $n  ($len chars)"; else echo "IL DIFFERS $n"; diff "$W/$n.src.il" "$W/$n.min.il" | head -8; rc=1; fi
  lim=100000; [ "$n" = ShrikeAI ] && lim=86000     # ship Helm scripts: <= 80,000 minified characters (control_arch s1.2)
  case "$n" in WaspFlightAI|WaspStaffAI) lim=80000;; esac     # the two Wasp Split PBs: <= 80,000 minified characters each (docs/audit/multi_pb_feasibility.md)
  [ "$len" -le "$lim" ] || { echo "TOO LONG $n: $len > $lim"; rc=1; }
done
rm -rf "$W"; exit $rc
