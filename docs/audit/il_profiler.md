# il_profiler: per-tick instruction counts of the drone scripts

Auditor key `il_profiler`. Goal: replace the fake `Runtime.CurrentInstructionCount` (always 0) by a real count and measure how close
the 9 combat scripts, HiveMotherAI and StingerAI come to the 50,000 limit ("Script too complex", not catchable).

## 1. Headline

* **The game does not count IL instructions.** Keen's published source (KeenSoftwareHouse/SpaceEngineers,
  `Sources/VRage.Scripting/Rewriters/InstructionCountingRewriter.cs` + `Sources/VRage.Library/Compiler/IlInjector.cs`) rewrites the script
  *source*: `IlInjector.CountInstructions()` (`m_numInstructions++`, throw above the maximum) is injected at the start of every method /
  constructor / operator / accessor / lambda body, every `if` and `else` body (not `else if`), every `for` / `foreach` / `while` / `do` body
  (once per iteration), every non-empty `switch` section, every `catch` / `finally` and every `goto`. `EnterMethod`/`ExitMethod` keep the
  call-chain depth (`m_maxCallChainDepth = 1000`). The wiki sentence "50,000 entered code blocks" (and the wiki "code junctions: method calls,
  switches, conditionals, loops; 1+1 does not count") agree. Everything inside the BCL / game API (LINQ, `List.Sort`, `GetBlocks...`) is free;
  only script lambdas called back from it count.
* **Measured with that model, every script is far below the limit**: worst case GnatSwarmAI peak **3,076 (6.2 %)**; the other eight
  combat scripts peak 920-1,070 (1.8-2.1 %); HiveMotherAI peak 1,897 (3.8 %), constructor 3,137; StingerAI peak 84 (0.2 %).
* **Caveat that matters**: the source above is the public (old) code base; I could not verify the current game build still counts this way
  (no running game here). If the current game instead counted IL instructions (what the game did before ~2016), the same runs give
  GnatSwarmAI peak **72,587 (145 %, would die)**, HiveMother constructor on a Quad Hive **60,092**, other combat scripts 18-21k (36-43 %).
  A one-minute in-game check settles it (section 6).

## 2. What was built (all under sim/ and tools/ilcount/, nothing in scripts/)

| piece | file | what it does |
|---|---|---|
| runtime counter | `sim/shim/IlCounter.cs` (in SimShim.dll) | thread-static `Total`, `Depth`, per-site hit vector; `C(site)` = Keen's CountInstructions, `Enter/Exit`, `A(site,n)` for IL mode; optional limit -> `ScriptTooComplexException` (not swallowed by script `catch`, like the game's unblockable exceptions) |
| block-model rewriter | `tools/ilcount/IlRewrite.cs` | port of Keen's `InstructionCountingRewriter` onto Roslyn 1.3.2 (C# 6 generation, as the game): same injection points, same try/finally depth tracking, same catch filter, expression-bodied lambdas converted using the semantic model. Emits a site table (kind, method, line) |
| IL-model injector | `tools/ilcount/IlCecil.cs` | Mono.Cecil: `A(method, n)` at every IL basic block (n = IL instruction count of the block). Conservative "what if the game counted IL" view |
| build | `tools/ilcount/build.sh` | `SIM_OUT=... bash tools/ilcount/build.sh` fetches Roslyn 1.3.2 from NuGet (not committed, `*.dll` is ignored; needs `apt mono-devel` for the facades and `libmono-cecil-cil` for IL mode) |
| host wiring | `sim/src/Run/IlProfile.cs`, `ScriptHost.cs`, `ScriptCompiler.cs`, `RuntimeInfo.cs`, `SimSettings.cs` | `--count-instructions` (SimSettings.CountInstructions), `--instruction-count-mode blocks|il`, `--enforce-instruction-limit`. Instrumented dlls are built on demand per script *text* (so blueprint-embedded minified text and edited scripts work) into `<simDir>/scripts_il/`. `Runtime.CurrentInstructionCount` / `CurrentCallChainDepth` return the live values |
| results | `Metrics.cs`, `JsonOut.cs` | every ship in runs.jsonl gets `"il":[{script, runs, ctor, max, max_tick, max_type, mean, p50, p99, max_depth, overruns, windows[0-10,10-30,30-60,60-120,120+ s], peak_sites[8], mean_sites[8]}]`. `printer-test`, `dock-launch`, `follow-test` accept the same flags and print `IL {...}` lines or append to `--il-out FILE` |
| report | `tools/ilcount/report.py` | aggregates runs.jsonl / il-out files into the table below |
| self-test | `SelfTestD.TestInstructionCount` | hand-computed script (30 blocks entered: method, for x10, if x5, else x5, helper + while x3, lambda x4, catch) must count exactly 30, and the limit must throw past the script's own `catch (Exception)` |

Checks that the instrumentation is harmless: `2v3/2xHornet-vs-3xGnat` seed 0 gives a byte-identical result JSON (minus il/timing/settings hash)
with and without `--count-instructions`; `selftest all` passes from my own build (scenario end-to-end tests are skipped there because
`out/Blueprints` does not exist in the repo checkout).

Fidelity of the block model vs Keen's rewriter (differences): no `#line` barricades (irrelevant); `using` statements keep their shape
(only the body is counted; the game rewrites to try/finally, +1 for finally at most); `else if` handled the same. mcs, not Roslyn, compiles
the instrumented text, but counting is source-level so codegen differences do not matter. Roslyn semantic model resolved all lambdas
(`unresolvedLambdas=0` for all 15 scripts, `semanticErrors=0`).

## 3. Runs (blocks model, the game's own counting)

All with `--count-instructions`, 3 seeds per scenario, via `SimHost.exe run --filter ... --seeds 3 --jobs 3` unless noted
(seeds chosen small: this is a measurement of a deterministic-ish quantity, not an A/B). Blueprints built into a private out dir.

| set | scenarios (names) | runs |
|---|---|---|
| 1v1 | Aegis/Basilisk/Gnat/Harrier vs Hornet; Hornet vs Hornet/Lancer/Longbow L/Longbow S/Mantis/Midge/Wasp/Watcher | 36 |
| swarm / mixed / pirate / 2v3 | 12xGnat vs Hornet and Pirate; Watcher+3xGnat mirror; Hornet+Longbow S vs Wasp+2xGnat; Lancer+Mantis mirror; Hornet, Basilisk, Longbow L vs Pirate; 2xHornet vs 3xGnat | 27 |
| kamikaze (StingerAI) | Aegis/Hornet/Gnat vs 8xStinger, 3xGnat vs 8xStinger, Hornet vs Stinger | 15 |
| docked / merged | `dock-launch` (drone merged to dummy printer, released at 3 s, 30 s) Hornet, Gnat, Wasp, Harrier, Lancer, Aegis | 6 |
| FOLLOW | `follow-test --path fight --count 3` Hornet, Gnat, Wasp | 3 |
| printers (HiveMotherAI on the real blueprint) | `printer-test --seconds 320` fault none + all (the faulty-projector run with 1,680 caught NullReferenceExceptions): Hive Mother, Twin Hive, Quad Hive, Hornet Forge, Wasp Foundry, Lancer Foundry, Aegis Shipyard, Longbow Foundry, Harrier Forge | 18 |
| IL mode repeat | sets 1+2+3 in `--instruction-count-mode il`, Quad Hive + Aegis Shipyard printer | 78 |

Not covered: `GnatMothership/HornetMothership/LongbowMothership` (carrier relay scripts, never in a scenario), `SeekerProbe`, the Seeker test ships,
Midge (scriptless), `Twin Hive` merged drones in a real printer grid (the sim never fuses a drone into the printer except in dock-launch's dummy).

### 3.1 Per script, block model (limit 50,000)

Per-tick = one Main call (Update1 -> 1 per tick). "worst p99" = largest per-PB 99th percentile. Windows are sim seconds since scenario start.

| script | PB-runs | peak | mean | worst p99 | ctor | max call depth | peak / limit | where the peak is |
|---|---|---|---|---|---|---|---|---|
| GnatSwarmAI | 1,275,637 (+kamikaze/dock/follow) | **3,076** | 185 | 577 (1,726 in 30-120 s windows) | 316 | 7 | 6.2 % | 2v3 Hornet-vs-Gnat seed 0, tick 11,328: `HitVotes` loop x1,536 |
| WaspAI | 52,583 | 1,072 | 160 | 413 | 546 | 7 | 2.1 % | `StepSlimScan` x200 + `GetBlocks` x42 |
| BasiliskAI | 37,621 | 1,029 | 237 | 470 | 564 | 7 | 2.1 % | same |
| LancerAI | 92,200 | 1,002 | 195 | 399 | 504 | 7 | 2.0 % | same |
| MantisAI | 72,651 | 969 | 189 | 424 | 506 | 7 | 1.9 % | same |
| HornetDroneAI (also Watcher) | 601,940 | 965 | 202 | 382 | 494 | 7 | 1.9 % | same |
| AegisAI | 10,389 (+9,998) | 995 | 187-237 | 468 | 531 | 7 | 2.0 % | same; dock-launch tick 26: 1,669 (`CheckDamage` x791 right after the split) |
| LongbowSniperAI | 147,972 | 955 | 243 | 433 | 500 | 7 | 1.9 % | `StepSlimScan` x200, `StoreHull` x46 |
| HarrierAI | 41,073 | 923 | 200 | 346 | 521 | 7 | 1.8 % | same |
| StingerAI | 280,995 | 84 | 27 | 44 | 87 | 6 | 0.2 % | `Tick` loop x12 |
| HiveMotherAI | 34,560 (18 PBs) | 1,897 | 100 | 1,847 | 3,137 (Quad Hive; Hive Mother 881) | 6 | 3.8 % | `GetBlocks` foreach over the whole printer grid (x384 on a 356-block Twin Hive) every 600 ticks; in the fault run the error path adds ~10 % |

Per phase (all scripts behave alike; Gnat shown because it is the only one with a phase dependence): GnatSwarmAI mean/p99/max:
0-10 s 183/272/578, 10-30 s 191/301/2,320, 30-60 s 195/1,726/2,325, 60-120 s 195/1,688/2,665, 120+ s 174/1,723/3,076. The other combat
scripts: 0-10 s mean 130-180 max 730-1,020 (the `StepSlimScan`/`GetBlocks` start-up scan), later windows mean 150-260, p99 300-470, never above 1,072.
Docked/merged: first 10 s after the split the peak is 1,540-1,670 for all scripts (initial hull/slim scan + damage baseline on the fused
grid), then normal. Hive Mother windows 0-10 s mean 121, later 79-102, peak 1,007 on 4-bay printers.

### 3.2 Top offenders (block model, what to bound if the limit is ever the issue)

1. `GnatSwarmAI.HitVotes` (loop over hit directions): up to 1,536 counts in one tick, 3 x the Gnat's next-largest. Rare (p99 of the Gnat 577, but 1,700 across 30-120 s p99 windows).
2. `StepSlimScan` (time-sliced hull scan, capped at 200 per step) + `GetBlocks` every 600 ticks (x42) in all drone scripts: ~850 together = the constant 900-1,000 peak of the eight non-Gnat scripts.
3. `CheckDamage` (loops over every slim block, O(hull)): 791 on the freshly split dummy printer grid; scales with hull size.
4. `ReadIGC`, `Alerted`, `Forwardish`, `UnderFire`: the steady 150-250 per tick.
5. HiveMotherAI `GetBlocks`/`Power`/`SetOn`: O(blocks of the printer grid); about 1.7 counts per block, so a 20,000-block fused grid would be needed to reach 50,000.

### 3.3 IL-instruction view (`--instruction-count-mode il`, conservative worst case)

| script | peak | peak / 50,000 | mean | ctor |
|---|---|---|---|---|
| GnatSwarmAI | **72,587** | **145 %** | 3,775 | 5,420 |
| BasiliskAI | 21,425 | 43 % | 4,336 | 9,165 |
| WaspAI | 21,133 | 42 % | 2,550 | 8,870 |
| LongbowSniperAI | 19,058 | 38 % | 4,360 | 8,028 |
| AegisAI / LancerAI / HornetDroneAI / MantisAI / HarrierAI | 17.9-18.7k | 36-37 % | 3.0-3.4k | 7,980-8,610 |
| StingerAI | 1,880 | 3.8 % | 914 | 1,612 |
| HiveMotherAI (Quad Hive) | 26,160 | 52 % | 3,027 | **60,092** |

Ratio IL / blocks is about 15-19x for the combat scripts, 14x for Hive Mother, 23x for Gnat's HitVotes peak. In the IL view the Gnat peak
(`HitVotes` 58,400 IL instructions) and the Quad Hive constructor would exceed the limit.

## 4. Findings

See the structured list returned with this report (IL-01 .. IL-08). Short form:

* IL-01: real counter now exists; with the game's own counting model nothing is near the limit (max 6.2 %).
* IL-02: the counting model rests on Keen's published rewriter + wiki wording; the current game build is unverified. If IL instructions were counted, Gnat (145 %) and the Quad Hive constructor (120 %) fail. Calibrate in-game (section 6).
* IL-03: GnatSwarmAI.HitVotes is the single outlier (1,536 counts in a tick, 58k IL instructions) - cap it if the calibration says IL.
* IL-04: `tools/designs/stinger.py` is not in the repo, so the Stinger is the printers.py fallback (Gnat frame + 2 warheads); the numbers for StingerAI use that ship.
* IL-05: sim differences vs game that this tool does not change: API calls are free in the counter (true in game), `LastRunTimeMs` is still a constant 0.05; a script killed by the limit stays dead in the sim (game: stops until recompiled/Run).
* IL-06: lines in the `il` site tables refer to the *minified blueprint text* (the PB's Program), method names are reliable.

## 5. Checked and correct

| item | result |
|---|---|
| `MaxInstructionCount = 50000` (SimSettings) | matches wiki; unverified in a running game (print it, `packages/tests.md` T12) |
| `MaxCallChainDepth = 1000` marked `[I]` in SimSettings | **confirmed** by `IlInjector.m_maxCallChainDepth = 1000` in Keen's source |
| scripts' own `Ins` status line (`Runtime.CurrentInstructionCount`, `_peakIns`) in all 12 scripts | now returns real numbers in the sim; same line gives the in-game calibration |
| call-chain depth | max 7 (Hive 6) of 1000 |
| constructors | 316-564 (Hive Mother 881-3,137) blocks, no constructor-time risk |
| hot loops bounded by constants (StepSlimScan cap 200, GetBlocks every 600 ticks, IGC) | yes, peak is flat (920-1,070) across all non-Gnat scripts |
| instrumentation changes sim results | no (byte-identical result JSON) |

## 6. How to calibrate in the real game (one minute)

The status screens already print `Ins    : <now> / <max>  peak <peak>`. Fly a Gnat in a fight (or two Gnats vs a Hornet) and read `peak`:
* about 300-3,500: the block model is right (sim: 316 constructor-time, 3,076 worst); everything has 15x+ headroom.
* above about 10,000 within a minute: the build counts something finer (IL); then bound `GnatSwarmAI.HitVotes` and the Hive `GetBlocks`.
Also print `Runtime.MaxInstructionCount` and `MaxCallChainDepth` once (open unknowns 6 in docs/research.md).

Reproduce: `SIM_OUT=<dir> bash sim/build.sh && SIM_OUT=<dir> bash tools/ilcount/build.sh`, then
`mono <dir>/SimHost.exe run --repo <repo with out/Blueprints> --filter REGEX --seeds 3 --count-instructions --out DIR` and
`python3 -I tools/ilcount/report.py DIR/runs.jsonl` (add `--instruction-count-mode il` for the conservative view).

Sources: https://raw.githubusercontent.com/KeenSoftwareHouse/SpaceEngineers/master/Sources/VRage.Scripting/Rewriters/InstructionCountingRewriter.cs ,
https://raw.githubusercontent.com/KeenSoftwareHouse/SpaceEngineers/master/Sources/VRage.Library/Compiler/IlInjector.cs ,
https://spaceengineers.wiki.gg/wiki/Scripting/The_Anatomy_of_a_Script (code junctions) , https://spaceengineers.wiki.gg/wiki/Scripting/The_Grid_Terminal_System (50,000).
