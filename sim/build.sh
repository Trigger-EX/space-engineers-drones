#!/bin/bash
# SLOT D. Builds the simulator with plain mcs (Mono 6.8, no dotnet SDK). Owner: slot D (keep the script wrapper identical to
# sim/src/Run/ScriptCompiler.cs Usings/Wrap). Usage: sim/build.sh [--no-scripts]
#   out/sim/SeApi.dll      stubs.cs (the in-game API, with real VRageMath/MyIni behaviour)
#   out/sim/SimShim.dll    sim/shim/*.cs (deterministic Random for scripts)
#   out/sim/SimHost.exe    sim/src/**/*.cs
#   out/sim/scripts/<Name>.dll + manifest.tsv   every scripts/*.cs, wrapped like the PB does
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/out/sim"; SC="$OUT/scripts"; WRAP="$OUT/wrapped"
mkdir -p "$OUT" "$SC" "$WRAP"
NOWARN="-nowarn:0169,0414,0649,0219"

echo "[1/4] SeApi.dll"
mcs -target:library -optimize+ -debug $NOWARN -out:"$OUT/SeApi.dll" "$ROOT/stubs.cs"
echo "[2/4] SimShim.dll"
mcs -target:library -optimize+ -debug $NOWARN -out:"$OUT/SimShim.dll" "$ROOT"/sim/shim/*.cs
echo "[3/4] SimHost.exe"
mapfile -t SRC < <(find "$ROOT/sim/src" -name '*.cs' | LC_ALL=C sort)
mcs -target:exe -langversion:7.2 -optimize+ -debug $NOWARN -warn:2 \
    -r:"$OUT/SeApi.dll" -r:"$OUT/SimShim.dll" -r:System.Xml.dll -r:System.Core.dll \
    -out:"$OUT/SimHost.exe" "${SRC[@]}"

if [ "${1:-}" = "--no-scripts" ]; then echo "skipped scripts"; exit 0; fi
echo "[4/4] scripts"
USINGS='using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Text;
using System.Text.RegularExpressions; using Sandbox.ModAPI.Ingame; using Sandbox.ModAPI.Interfaces; using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.ModAPI.Ingame; using VRage; using VRage.Collections; using VRage.Game;
using VRage.Game.Components; using VRage.Game.GUI.TextPanel; using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities; using VRage.Game.ObjectBuilders.Definitions; using VRage.ObjectBuilders; using VRageMath;
using Random = SimShim.DetRandom;'
: > "$SC/manifest.tsv"
rc=0
for f in "$ROOT"/scripts/*.cs; do
  n=$(basename "$f" .cs); w="$WRAP/$n.cs"
  { echo "$USINGS"; echo "public partial class Program : MyGridProgram {"; cat "$f"; echo; echo "}"; } > "$w"
  if mcs -langversion:6 -target:library -optimize+ $NOWARN -r:"$OUT/SeApi.dll" -r:"$OUT/SimShim.dll" -out:"$SC/$n.dll" "$w" > "$WRAP/$n.log" 2>&1; then
    # key = sha1 of the script text with CR removed and trailing whitespace trimmed (ScriptCompiler.ScriptKey)
    key=$(python3 -I -c 'import sys,hashlib; t=open(sys.argv[1],encoding="utf-8").read().replace("\r","").rstrip(); print(hashlib.sha1(t.encode("utf-8")).hexdigest())' "$f")
    printf '%s\t%s.dll\n' "$key" "$n" >> "$SC/manifest.tsv"
    echo "  ok $n"
  else
    echo "  FAIL $n"; grep -E 'error' "$WRAP/$n.log" | head -20; rc=1
  fi
done
exit $rc
