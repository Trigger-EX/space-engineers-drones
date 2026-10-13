#!/bin/bash
# Compiles each in-game script wrapped like the Programmable Block does, against stubs.cs.
# Usage: tools/compile_check.sh [script.cs ...]   (default: scripts/*.cs)
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${TMPDIR:-/tmp}/se_compile_check.$$"; mkdir -p "$OUT"
USINGS='using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Text;
using System.Text.RegularExpressions; using Sandbox.ModAPI.Ingame; using Sandbox.ModAPI.Interfaces; using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.ModAPI.Ingame; using VRage; using VRage.Collections; using VRage.Game;
using VRage.Game.Components; using VRage.Game.GUI.TextPanel; using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities; using VRage.Game.ObjectBuilders.Definitions; using VRage.ObjectBuilders; using VRageMath;'
# stub API names vs the official PB API names (tools/pbapi_names.txt)
while read -r iface members; do
  case "$iface" in ""|\#*) continue;; esac
  decl=$(awk -v re="interface $iface[ :{]" '$0 ~ re {p=1} p {print} p && /} *}[ \t]*$/ {exit}' "$ROOT/stubs.cs")
  [ -z "$decl" ] && { echo "FAIL pbapi_names: $iface missing in stubs.cs"; rm -rf "$OUT"; exit 2; }
  for m in $members; do
    echo "$decl" | grep -qE "\b$m\b" || { echo "FAIL pbapi_names: $iface.$m missing in stubs.cs"; rm -rf "$OUT"; exit 2; }
  done
done < "$ROOT/tools/pbapi_names.txt"
files=("$@"); [ ${#files[@]} -eq 0 ] && files=("$ROOT"/scripts/*.cs)
rc=0
mcs -target:library -nowarn:0169,0414,0649,0219 -out:"$OUT/stubs.dll" "$ROOT/stubs.cs" >"$OUT/stubs.log" 2>&1 \
  || { echo "FAIL stubs.cs"; cat "$OUT/stubs.log"; exit 2; }
for f in "${files[@]}"; do
  n=$(basename "$f"); w="$OUT/$n"
  { echo "$USINGS"; echo "public partial class Program : MyGridProgram {"; cat "$f"; echo; echo "}"; } >"$w"
  if mcs -langversion:6 -target:library -warn:2 -nowarn:0169,0414,0649,0219 -r:"$OUT/stubs.dll" -out:"$OUT/$n.dll" "$w" >"$OUT/$n.log" 2>&1; then
    echo "PASS $n ($(grep -c 'warning' "$OUT/$n.log") warnings)"
  else
    echo "FAIL $n"; sed "s#$OUT/##" "$OUT/$n.log" | grep -E 'error' | head -40; rc=1
  fi
done
rm -rf "$OUT"; exit $rc
