#!/bin/bash
# il_profiler. Builds the instruction-count rewriter: $SIM_OUT/ilcount/IlRewrite.exe (default out/sim/ilcount). Needs Roslyn 1.3.2
# (Microsoft.CodeAnalysis.CSharp, the compiler generation the game's C# 6 scripts use) which is fetched once from NuGet:
#   bash tools/ilcount/build.sh            then run the sim with  --count-instructions
# The DLLs are not committed (*.dll is git-ignored); this script re-downloads them if out/ilcount-lib is missing.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${SIM_OUT:-$ROOT/out/sim}"; D="$OUT/ilcount"; LIB="${ILCOUNT_LIB:-$ROOT/out/ilcount-lib}"
mkdir -p "$D" "$LIB"
fetch() { # id version subdir
  local id="$1" v="$2" sub="$3" t; t=$(mktemp -d)
  curl -fsSL -m 180 -o "$t/p.zip" "https://api.nuget.org/v3-flatcontainer/$id/$v/$id.$v.nupkg"
  unzip -qo "$t/p.zip" -d "$t/x"
  cp "$t"/x/lib/$sub*/*.dll "$LIB/"
  rm -rf "$t"
}
if [ ! -f "$LIB/System.Reflection.Metadata.dll" ]; then
  fetch microsoft.codeanalysis.common 1.3.2 net45
  fetch microsoft.codeanalysis.csharp 1.3.2 net45
  fetch system.collections.immutable 1.1.37 portable-net45
  fetch system.reflection.metadata 1.2.0 portable-net45
fi
cp "$LIB"/*.dll "$D/"
# the net45 Roslyn assemblies reference the System.Runtime-style facades (apt: mono-devel provides /usr/lib/mono/4.5/Facades)
FACADES=$(for f in /usr/lib/mono/4.5/Facades/System.*.dll; do printf -- "-r:%s " "$f"; done)
mcs -langversion:6 -optimize+ -nowarn:0168 -target:exe -out:"$D/IlRewrite.exe" \
  -r:"$D/Microsoft.CodeAnalysis.dll" -r:"$D/Microsoft.CodeAnalysis.CSharp.dll" -r:"$D/System.Collections.Immutable.dll" -r:System.Core.dll $FACADES \
  "$ROOT/tools/ilcount/IlRewrite.cs"
echo "built $D/IlRewrite.exe"
# optional IL-level counter (InstructionCountMode il): needs Mono.Cecil (apt: libmono-cecil-cil)
if [ -f /usr/lib/mono-cecil/Mono.Cecil.dll ]; then
  cp /usr/lib/mono-cecil/Mono.Cecil.dll /usr/lib/mono-cecil/Mono.Cecil.Rocks.dll "$D/"
  mcs -langversion:6 -optimize+ -target:exe -out:"$D/IlCecil.exe" -r:"$D/Mono.Cecil.dll" -r:"$D/Mono.Cecil.Rocks.dll" -r:System.Core.dll "$ROOT/tools/ilcount/IlCecil.cs"
  echo "built $D/IlCecil.exe"
else echo "Mono.Cecil not found: InstructionCountMode il unavailable (apt-get install libmono-cecil-cil)"; fi
