# Audit: repository consistency and build health (key `repo_consistency`)

Scope: everything cheap to run, version/release consistency, doc-vs-code contradictions, forked logic across the Hornet-derived
scripts, repo size, `pc/` reproducibility, Python and C# hygiene, and a CI proposal. The working tree already had uncommitted edits by
other auditors (`sim/src/*`, `sim/shim/IlCounter.cs`, `tools/ilcount/`, `docs/audit/*.md`); none were touched or committed here. All
outputs went to the scratchpad (`rc_sim`, `stage`, `ba_full`, `rel_repo_consistency`).

Headline: the build is healthy. Every checker passes, the release zips are byte-for-byte what the current sources produce, and the
sim is deterministic against the committed reference. The problems are documentation drift (HANDOFF.md, script-audit.md), a set of
small unsynced fixes between the forked scripts, about 135 MB of release zips in git history, and thin character headroom on three scripts.

## 1. What was run

| Check | Command | Result |
|---|---|---|
| Script compile (PB wrapper, C# 6, vs `stubs.cs`) | `TMPDIR=$S bash tools/compile_check.sh` | PASS 15/15, 0 warnings (4.5 s) |
| 7 shared-block syncs | `python3 -I tools/{ai_gate,aim,fleet,follow,kz,launch,lead}_sync.py --check` | all `ok`; launch reports `StingerAI` as `absent` (documented, not failing) |
| Minify proof (IL identical) | `SIM_OUT=$S/rc_sim bash tools/minify_check.sh` | "same IL" for all 15; max length Basilisk 97,721 chars |
| Sim build | `SIM_OUT=$S/rc_sim bash sim/build.sh` | OK, 0 warnings at the script's own flags; 15 scripts wrapped |
| Selftest, bare build dir | `mono rc_sim/SimHost.exe selftest all` | OK but sections silently skipped (RC-09) |
| Selftest, full (blueprints, `defs_stats.json`, `scripts/`, `out/dist/MODELS.md` staged) | `cd stage && mono out/sim/SimHost.exe selftest all` | SELFTEST OK, 335 `ok` lines, incl. "all 30 Full models of MODELS.md: block count, mass and six-direction acceleration match the python tools" |
| Blueprint build, all | `python3 -I tools/build_all.py --out $S/ba_full --no-zip` | 60 PASS, 0 FAIL, 0 cross-check failures; 656 s wall (40 CPU-min, box was loaded) |
| Blueprint build, small | `build_all.py --only Hornet --out ...` | 2 PASS in 1 s; second run byte-identical (deterministic) |
| Sim reproducibility | `run --filter '^1v1/(Aegis-vs-Hornet\|Longbow S-vs-Wasp\|Hornet-vs-Hornet\|Gnat-vs-Gnat)$' --seeds 5` with my build and with the shipped `SE_SimPack_e533424` exe | 20/20 and 20/20 identical to `pc/reference/verify-linux.runs.jsonl` (outcome, duration, shots). 5 seeds because it is a bit-identity check, not a statistic |
| Lead bench | compile `tools/lead_bench/Bench.cs`; `bench.exe weapon=rocket scen=strafe size=huge truth=1 seeds=3` | runs in 0.3 s (smoke test only) |
| Shell syntax | `bash -n pc/*.sh` | all 7 OK |
| Python | `python3 -m pyflakes` on tools/, designs/, pc/; `py_compile` | no syntax or undefined-name errors; unused imports only (RC-12) |
| C# strict warnings | `mcs -warn:4`, no `-nowarn`, over `sim/src` | 19x CS0219, 2x CS0414, 1x CS0675 (RC-13) |
| Release contents | unzipped all 7 zips; `Scripts/*.cs` vs `minify(scripts/*.cs)`, `Scripts/Source/*.cs` vs `scripts/*.cs`, every `Blueprints/*/bp.sbc` vs the fresh `ba_full` build | 122 script files identical, 60/60 blueprints byte-identical, `MODELS.md` identical; SimPack `scripts/` and `pc/*` identical to the repo |

## 2. Found CORRECT (coverage)

| Item | Evidence |
|---|---|
| Release zips v2.6 equal current sources | scripts (min + Source), all 60 blueprints, MODELS.md identical |
| SimPack e533424 equals HEAD sim | `git diff e533424 HEAD -- sim scripts pc` touches only `pc/reference/*.jsonl`; its exe reproduces the reference |
| All 15 scripts under 100,000 chars once minified | max 97,721 (Basilisk); `build_all.py:129` refuses more |
| Minifier preserves semantics | IL identical for all 15 scripts |
| Shared blocks in sync | 7 checkers pass; marker grep shows every block sits in exactly the scripts its sync list names |
| No tabs, CRs or non-ASCII in scripts | scanned `scripts/*.cs` |
| `VER` constants vs `changelog.md` | 2.6 for the seven Hornet-derived drones, Gnat 1.13, Longbow 2.4: matches changelog v2.4 line |
| `build_all.py VERSION='2.6'` vs zip names and `MODELS.md` title | match |
| File references in docs/code | scan of `docs|tools|scripts|sim|pc|packages|releases/...` paths in tracked md/py/sh/cs: one genuinely dead (RC-07) |
| `.gitignore` | `ref/`, `out/`, `*.dll`, `__pycache__`, `*.pyc` ignored; no exe/dll/pyc tracked |
| Sim determinism | bit-identical to reference; selftest includes same-seed identical result lines |
| Plan filters | default.plan verify and sweep filters match 4 and 6 scenarios as intended; `^1v1/` = 91 pairs |
| `pc/` Windows files | `run_endless.bat` / `run_search.bat` have CRLF; the one-line `run.bat` / `pack_results.bat` are LF but label-free |

## 3. Findings

### RC-01 (medium) Unsynced small fixes across the Hornet-derived scripts
The seven sync tools cover about 40 % of each script (36,977 of 88-98k minified chars). The rest is forked by hand. Function-level
diff (top-level members extracted, synced blocks removed, token-compared so minifier line breaks do not count):

| Pair | Common members | Token-identical | Differing methods | Differing fields | Only in A / only in B |
|---|--:|--:|--:|--:|--:|
| Hornet -> Wasp | 227 | 203 | 12 | 12 | 2 / 39 |
| Hornet -> Lancer | 229 | 208 | 11 | 10 | 0 / 15 |
| Hornet -> Harrier | 228 | 212 | 8 | 8 | 1 / 6 |
| Hornet -> Aegis | 228 | 209 | 12 | 7 | 1 / 27 |
| Longbow S -> Mantis | 237 | 220 | 10 | 7 | 0 / 8 |
| Wasp -> Basilisk | 262 | 219 | 15 | 28 | 4 / 20 |
| Hornet -> Longbow S (other lineage) | 183 | 134 | 29 | 20 | 46 / 54 |
| Hornet -> Gnat (other lineage) | 160 | 112 | 31 | 17 | 69 / 68 |

Most differences are intended (fire gate, Display, LoadConfig keys, rocket code). Ones that look like fixes present in only some scripts:

1. `Disabled()`: Hornet, Harrier, Aegis, Basilisk wrap the wreck rules `RawMax/Raw` in `t.Type!=MyDetectedEntityType.Missile && (...)`.
   Wasp and Lancer do not, so they can write a missile off as "disabled" via `t.Raw<0.75`, the same class of bug the v2.4 kamikaze work fixed elsewhere.
2. `PatrolStep()`: Wasp, Lancer, Aegis, Basilisk have randomised phase/direction (`PatrolRandom`, `_patrolDir`, `_patrolRadF`); Hornet and Harrier still have the fixed circle.
3. Longbow S and Mantis lack safeguards Hornet has in shared helpers: `ReadTgt` (Hornet drops shared targets whose id is the own grid or a known mate: `d.Item1==Me.CubeGrid.EntityId||_mates.ContainsKey(d.Item1)`), `CamFor` (Hornet requires `IsFunctional && IsWorking`, Longbow only `IsWorking`), `AddContact` (no `t.Name`), `MinAccel` floor 0.3 vs 0.5. `Separation` (x2.5 range on large grids) is intentional.
4. `PruneAndSelect()` exists in five variants; Basilisk lacks Wasp's stale-track penalty `score+=Math.Max(0,(_now-t.Seen)-1.0)*150`.
5. `Valid()` / `PruneAndSelect()` `TargetMissiles`, `SmallBonus`: Harrier and Aegis only (intended).

Recommendation: keep a `tools/fork_diff.py` (the scratch script) with an allow-list in the check suite, or move `Disabled`, `PatrolStep`,
`CamFor`, `ReadTgt`, `AddContact`, `Home`, `MinAccel` into sync blocks. Review 1 and 3 first; 3-10 seeds A/B before porting, since
results for Wasp/Lancer/Mantis were tuned with current behaviour. Porting costs characters (RC-05).

### RC-02 (medium) HANDOFF.md is stale but CLAUDE.md names it the project brief
`CLAUDE.md` says to read `HANDOFF.md` for "the full project brief, status". Its section 2 still says printers "Not started", "Simulation engine +
fights + audit: Not started", drone scripts "v1.8", `StingerAI`/`HiveMotherAI` "never compiled", lists `tools/drones.py` as the redesigned drones
(now an 11-line shim) and `sim_v5.py` in the root (now `sim_legacy/`). Only one "Later work" pointer. Real state: `packages/changelog.md`
v2.6, 15 scripts, 30 models / 60 blueprints, sim with 504 scenarios. `docs/handoff.md` (lowercase, PC-crash topic) is easy to confuse with it.
Fix: rewrite section 2 and the file list from `releases/MODELS.md` + changelog, or point CLAUDE.md at a current brief.

### RC-03 (medium) Git history carries about 135 MB of release zips
`.git` is 109 MB (pack 100.9 MB) for 36.7 MB of tracked working-tree content. Release blobs in history total 134.8 MB uncompressed over 67 commits:
a new `SE_SimPack_<rev>.zip` (5-8.5 MB) per checkpoint and a `SpaceEngineers_Drones_vX_All.zip` (5-8.5 MB) per version are kept forever.
Biggest tracked files now: `SpaceEngineers_Drones_v2.6_All.zip` 6.1 MB, `SE_SimPack_e533424.zip` 5.9 MB, `pc/enemies/Tech Hunter/bp.sbc` 5.9 MB,
`defs_stats.json` 2.7 MB, `Printers_v2.6_*.zip` 2.7 MB each. All zip content is reproducible (verified), so GitHub Releases would do.
No exe/dll is tracked directly; they only exist inside zips. `ref/` (32 MB) is ignored and not in git.

### RC-04 (medium) docs/script-audit.md out of date; API fidelity of stubs never closed
It says "all 8 scripts PASS" (now 15; still all pass). Its "NOT verified" list (camera `CanScan` overloads, thruster/gyro/battery members,
`IMySensorBlock.DetectedEntities`, `MyIni`, `IMySlimBlock`, `IMyShipWelder` base type, `IMySmallMissileLauncherReload` as the railgun interface,
text surface interfaces) has no closure in `packages/unknowns.md`, and `docs/research.md:231` still says "not compiled against real API yet".
`stubs.cs` is hand written and doubles as the sim's API (`SeApi.dll`), so a green compile and selftest prove scripts match the stubs, not
the game. Suggest: compile once against the game's real `Sandbox.ModAPI.Ingame.dll` / VRage DLLs from the user's install (or MDK) and add that to CI.

### RC-05 (medium) Thin character headroom under the 100,000-char limit
Minified size / headroom: Basilisk 97,721 / 2,279; Wasp 95,313 / 4,687; Mantis 94,623 / 5,377; Aegis 93,509 / 6,491; Longbow S 91,672 / 8,328;
Lancer 90,660; Harrier 89,955; Hornet 88,840 / 11,160. One more shared block (about 3-6k) breaks Basilisk and Wasp at build time. Shared
blocks are 36,977 chars per script. Whether 100,000 characters is the real in-game limit is for the `script_logistics` auditor; this only records the margin.

### RC-06 (low) Version labels disagree inside scripts and docs
Banner vs `VER`: Hornet/Wasp/Lancer/Harrier/Aegis/Basilisk/Mantis banners say `v2.5-...`, `VER` is `2.6-...`; Gnat banner 1.12 vs `VER` `1.13+launch`;
Longbow banner v2.3 vs `VER` `2.4`. `WaspAI.cs` and `LancerAI.cs` lines 2-3 hold the banner twice (`v2.5` and `v2.3`). `HiveMotherAI` is 2.2
(commit 58d4a59: guarded reads, per-bay try/catch) but the changelog stops at 2.1. `build_all.py` docstring line 12 still says `v2.0` zip names.
`packages/tests.md` numbers two items "19" (list runs to 23 in 24 lines), so a quoted number such as "test 23" is right only by luck of position.
PB screens show `VER`, so only readers of the source are confused.

### RC-07 (low) Dead and optional references
- `tools/designs/printer_lib.py:27` cites `docs/printers.md`; it does not exist.
- `build_all.py:292` / `designs/printers.py` look for optional modules `stinger` and `printers_timer`; startup prints `not present (skipped): stinger, printers_timer`.
  `designs/stinger.py` is absent (fallback Stinger in `printers.py`). `printers_timer` is a hook for a timer-block printer variant: there is no such module, script or blueprint yet.
- `tools/drones.py` (shim) is imported by nothing. `sim_legacy/sim_v5.py` is referenced only from HANDOFF.md. `scripts/MidgeNotes.md` ships in the Drones zips under `Scripts/`.

### RC-08 (low) Lead bench is a re-implementation
`tools/lead_bench/Bench.cs` has its own solver; "SHIPPED v2.4 (LEAD v1.0)" is a config of that copy, not `tools/lead_block.cs`. It cannot detect
drift of the real block. Compile warnings: unused `set` (line 353), never-assigned `Cfg.ForceCenter`. Better: drive the bench through the sim's script host.

### RC-09 (low) Selftest can say OK while skipping sections
A fresh clone has no `out/Blueprints` (gitignored). `selftest all` then skips blueprint tests (SelfTestA), part of SelfTestC, the MODELS.md cross-check,
and SelfTestD's scenario tests, yet ends "SELFTEST OK", exit 0. With `defs_stats.json` but no `scripts/` beside it, `SelfTestD.TestScriptKeys`
throws an unhandled `DirectoryNotFoundException` (seen in the staged tree). Fix: print a skipped-section count and add `--strict` (non-zero if any section skipped).

### RC-10 (info) pc/ reproducibility
`bash -n` passes for all 7 shell runners; SimPack copies of runners and `README_PC.md` equal `pc/`. `run_plan.sh` re-implements `run_plan.ps1`
(resume, restarts, `{OUT}`) separately, so the two can drift. No `.gitattributes`: a Windows `autocrlf` checkout can give `.sh` CRLF (breaks on Mint)
and LF `.bat` files break the day they gain a `goto`. Add `*.sh text eol=lf`, `*.bat text eol=crlf`, `*.ps1 text eol=crlf`. `docs/handoff.md` names
SimPack 9627626 as the reference engine, older than the committed jsonl (regenerated, matches e533424).

### RC-11 (info) Sync coverage is list-based
`HiveMotherAI` and `StingerAI` are outside most syncs by design (Stinger has no LAUNCH block); `SeekerProbe` and the three `*Mothership.cs` relays only in FLEET. Nothing
checks that the SCRIPTS lists are complete, so a new script would silently skip a block (my marker grep catches it).

### RC-12 (low) Python hygiene
No undefined names. Unused imports: `build_all.py` (`json`; unused local `mod` at 291), `drones.py` (13), `extract_shapes.py` (`re`), `make_simpack.py` (`sys`),
`th_report.py` (`re`), `designs/_common.py` (`Blueprint`; f-string without placeholders at 200). Unused locals: `index_stats.py:418,446`, `sim_audit_timeouts.py:9,11`, `sim_report.py:207`.

### RC-13 (low) C# warnings hidden by -nowarn
`sim/build.sh` passes `-nowarn:0169,0414,0649,0219 -warn:2`. At `-warn:4` without it: 19 unused locals (`Metrics.cs:497 cosTol`, `TurretAi.cs:72 dist`,
`Weapons.cs:259 body`, `DefDb.cs:107 mags`, `GridFactory.cs:70 s`, tests), 2 unused fields (`Metrics._scratch`, `TurretUnit._subTick`) and CS0675 at
`PirateAi.cs:189` (the `(long)(x+512)<<40 | ...` key is correct while coordinates stay within +-512). `Metrics.OnTarget` computes `cosTol` and never uses it:
the on-target metric may ignore `OnTargetDeg`; the sim_methodology auditor should look. No warnings in the 15 wrapped scripts.

### RC-14 (info) build_all cost and out/ coupling
Full build 40 CPU-minutes (656 s here; the sim's message says "about 3 minutes" unloaded). `--only` skips zips and `MODELS.md`, so the sim's MODELS cross-check
needs a full run. The sim finds `defs_stats.json` by walking up from the exe, so a custom `SIM_OUT` outside the repo needs `defs_stats.json`, `scripts/`, `out/Blueprints` staged beside it.

## 4. Proposal: minimal `tools/check_all.sh` (not added)

```bash
#!/bin/bash
# check_all.sh [--full]  fast gate (~1 min with blueprints cached); --full rebuilds all blueprints (3-11 min)
set -uo pipefail
cd "$(dirname "$0")/.."; ROOT=$PWD; S=$(mktemp -d); rc=0
fail() { echo "FAIL: $*"; rc=1; }
bash tools/compile_check.sh || fail compile_check
for t in ai_gate aim fleet follow kz launch lead; do python3 -I tools/${t}_sync.py --check >/dev/null || fail "${t}_sync"; done
bash sim/build.sh >"$S/b.log" 2>&1 || { cat "$S/b.log"; fail sim_build; }
bash tools/minify_check.sh || fail minify_check
python3 -m pyflakes tools/*.py 2>&1 | grep -E "undefined name|syntax" && fail pyflakes
for f in pc/*.sh; do bash -n "$f" || fail "bash -n $f"; done
if [ "${1:-}" = --full ] || [ ! -d out/Blueprints ]; then python3 -I tools/build_all.py --no-zip || fail build_all; fi
mono out/sim/SimHost.exe selftest all >"$S/st.log" 2>&1; grep -q "SELFTEST OK" "$S/st.log" || fail selftest
grep -E "skip|SKIP|missing" "$S/st.log" && fail "selftest skipped sections"
mono out/sim/SimHost.exe run --filter '^1v1/(Aegis-vs-Hornet|Longbow S-vs-Wasp|Hornet-vs-Hornet|Gnat-vs-Gnat)$' --seeds 5 --out "$S/v" >/dev/null &&
python3 -I - "$S/v/runs.jsonl" <<'PY' || fail determinism
import json,sys
L=lambda p:{(r['scenario'],r['seed_index']):r for r in map(json.loads,open(p))}
a,b=L(sys.argv[1]),L('pc/reference/verify-linux.runs.jsonl')
sys.exit(1 if any((a[k]['outcome'],a[k]['duration_s'])!=(b[k]['outcome'],b[k]['duration_s']) for k in a if k in b) else 0)
PY
python3 -I tools/check_release.py || fail release     # to write: scripts in releases/*.zip == minify(scripts/*.cs); VERSION/changelog/zip names agree
python3 -I tools/fork_diff.py --check || fail fork_diff # to write: scratch div.py + allow-list (RC-01)
exit $rc
```

Every line except the last two is a command that ran in this audit. `check_release.py` and `fork_diff.py` are thin wrappers of the scratchpad
`cmp.py` and `div.py`. Keep the determinism step at 5 seeds (CLAUDE.md simulation rules).
