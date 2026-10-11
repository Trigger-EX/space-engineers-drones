# Script audit (compile health + in-game rules)

Run `tools/compile_check.sh` (wraps each `scripts/*.cs` in `public partial class Program : MyGridProgram { }` with the MDK
Programmable Block usings, compiles with `mcs -langversion:6` against `stubs.cs`). Status: all 8 scripts PASS, 0 warnings.
C# 6 is used because MDK documents the PB compiler as C# 6; the scripts use nothing newer.

## Stubs
`stubs.cs` is a hand-written SUBSET of the in-game API. Namespaces were corrected to the real ones this pass:
`SpaceEngineers.Game.ModAPI.Ingame` (IMyShipMergeBlock, IMyTurretControlBlock, IMySmallGatlingGun, IMySmallMissileLauncherReload),
`Sandbox.ModAPI.Interfaces` (ITerminalProperty, SetValueFloat/SetValueBool), `Sandbox.ModAPI.Ingame` (IMyWarhead, IMyProjector,
IMyShipWelder, ApplyAction). Fixed `UpdateType` to the real flag values; added `Runtime.Current/MaxInstructionCount`.
- Verified online (MDK-SE wiki, malforge pbapi, Keen source): IMyWarhead (IsArmed get/set, IsCountingDown, DetonationTime, Detonate, Start/StopCountdown),
  IMyShipMergeBlock (namespace, IsConnected), IMyProjector (namespace, IsProjecting, RemainingBlocks, TotalBlocks), IMyTurretControlBlock (namespace, HasTarget),
  TerminalPropertyExtensions.SetValueFloat/Bool namespace, TerminalBlockExtentions.ApplyAction, Vector3D/MatrixD/MathHelper members used
  (Normalize, Dot, Cross, Distance, Transform*, CreateFromAxisAngle, Clamp(double)), MyTuple 5-arity exists.
- NOT verified (from memory of the API, no source page fetched): IMyCameraBlock.CanScan(double) exact overloads, IMyShipController/IMyThrust/IMyGyro/IMyBatteryBlock
  members, IMySensorBlock.DetectedEntities, MyIni/MyIniParseResult (LineNo), IMySlimBlock members, MatrixD.Transpose, IMyShipWelder base type,
  IMySmallMissileLauncherReload being the railgun interface, IMyTextSurface/IMyTextSurfaceProvider. Wrong stubs here would hide an API mistake.
- Stub limitation: no behaviour. Compile success proves syntax/types only, not logic and not the instruction limit.

## Changes made to scripts
1. HiveMotherAI: `LaunchTag` (HANDOFF 6.4): rack mode sends `launch` on the Stinger tag; new ini key "Rack Launch Command Tag".
   Bay projector/welder/merge lookup now also requires `IsSameConstructAs(Me)` (was: any block in the world grid system with "[Bay" in its name).
2. Hornet/Longbow/Gnat: `ScanSlims` was a triple loop over the whole grid bounding box in ONE tick, run in the constructor, on every
   grid change and every 600 ticks. Now incremental (200 cells/tick, `StepSlimScan`), and skipped above 30,000 cells.
3. All status screens show `Ins: current / max  peak N` (Runtime.CurrentInstructionCount / MaxInstructionCount; peak is sampled at the end of each tick).

## Why scripts may be "missing" from the PB (hypotheses, unproven)
- H1 (strong, code-based): a drone merged to a printer is one grid; bounding-box scan of a printer-sized grid costs far more than the 50,000-instruction
  limit. "Script too complex" cannot be caught by try/catch and halts the PB (code still shows in Edit, red error). Fixed in item 2. Does not explain plain F10 pastes of a small drone.
- H2: the generated PB XML differs from what the game writes (Starburst bp.sbc): no `<Owner>`/`<BuiltBy>`, no `ComponentContainer` (text-panel component),
  `<DefaultRunArgument xsi:nil>` emitted after `<Storage />`, no `NumberInGrid`. An unowned PB (`OwnerId == 0`) is flagged on screen by Hornet already. Compare with Starburst before blaming code.
- H3: projector-welded PBs start with no program; the Program text is not part of a projection (unverified; the printer plan needs the user's projector test).
- H4: constructor exceptions (e.g. `Me.GetSurface(0)` etc.) show red text in the PB; the editor still shows the code. Ask the user which of: empty editor / code + red error.
- Size is not the cause (HANDOFF 4.2). No non-ASCII, tabs or CRs in any script.

## Rule audit (in-game restrictions)
| Check | Result |
|---|---|
| Namespaces/types outside whitelist (System.IO, Reflection, Threading, Diagnostics, Console, DateTime, Stopwatch, MyResourceSinkComponent) | none found by grep |
| `static` members | none |
| Linq / lambdas | only List.Sort and GetBlocksOfType lambdas; allowed |
| C# > 6 features (`out var`, `?.`, `$""`, local functions, tuples) | none (compiled with -langversion:6) |
| `Storage` | used only in ctor (read) and `Save()` (write), as intended |
| Random | `System.Random`, allowed |
| Update frequency | Update1 for combat drones/Stinger, Update10 for Hive Mother/motherships; `Main` ignores non-matching update types and runs commands from Terminal/Trigger/Script/Mod |
| Garbage | `_sb` reused; `ApplyThrust` allocates a 6-element double[] per tick (Hornet, Longbow) and Display builds strings every 30 ticks: low, left as is |
| Per-tick cost | not measurable without the game; see the new Ins line. Candidates if Ins peak nears 50,000: hull-point sort (`_aimIdx.Sort`), OmniScan raycast loop, GetBlocks (every 600 ticks) |
| try/catch in Main | present in all; cannot catch ScriptTooComplex |
| IGC | listeners registered once in ctor; payloads are MyTuple of long/double/Vector3D/int, all IGC-legal |

Not done: no functional run of the scripts (no game, stubs have no behaviour); instruction cost is unmeasured.
