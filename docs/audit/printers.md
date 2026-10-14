# Audit: printer ships (projector + welders + merge-block bays, HiveMotherAI v2.2)

Auditor key `printers`, date 2026-10-13. Read-only audit; this file is the only repo change.
Evidence tags: [KS] Keen public source snapshot (old, about 2018-19; raw.githubusercontent.com, files fetched into the session scratchpad: `MyProjectorBase.cs`,
`MyCubeGrid.cs`, `MySlimBlock.cs`, `MyDisconnectHelper.cs`, `MyProgrammableBlock.cs`); [wiki] spaceengineers.wiki.gg; [data] `defs.json` / `defs_stats.json` /
`ref/user-samples`; [repo] the repo's own files; [rec] my recollection, with confidence. `MyShipWelder.cs` and `MyShipMergeBlock.cs` return 404 on raw.githubusercontent.com
(all path guesses tried), so welder and merge-block internals below are wiki + recollection only.

## 0. Verdict in one paragraph

**As designed, no printer in the package can release a finished drone.** Keen's code builds every block welded from a projection into the **projector's own grid**
(`MyProjectorBase.Build` raises `BuildBlockRequest` on `CubeGrid`, the projector's grid). The drone's merge block therefore becomes a block of the printer grid, next to the
printer's merge block, on ONE grid; the two merge blocks have nothing to lock (a merge pair joins two different grids) and switching them off cannot split a grid, because
grids only split when a block is **removed** (`MyDisconnectHelper`, `DetectDisconnects` after block removal). The repo already lists this as "FUSED, biggest risk" and the
user's own screenshot report (printed shells that do not fly off, grinder in hand for cleanup) is what that predicts. The source and community evidence say it is not a risk but
the expected outcome. Second blocker for survival: welders get components only from their own inventory or a conveyor network; the `[Stock]` cargo sits inside the pod,
connected to nothing, and is 0.25 m3 against 1.07 m3 (Gnat) / 2.23 m3 (Hornet) of components. Creative mode needs no components at all (source). Everything else
(projection offsets, RemainingBlocks semantics, Program text surviving the weld) checks out and is better than the repo feared.

## 1. Findings (ids match the structured list)

### printers-01 (critical) Welded blocks join the projector's grid; merge off cannot release them
* `MyProjectorBase.cs` `Build()` (about line 1295-1345): `var projectorGrid = CubeGrid; ... MyMultiplayer.RaiseEvent(projectorGrid, x => x.BuildBlockRequest, ...)`. The block is created in the projector's grid.
* `UpdateProjection()` (line 735-741) decides "already built" by `CubeGrid.GetCubeBlock(realPosition)` on the projector's own grid, and the projector re-evaluates on `CubeGrid.OnBlockAdded/OnBlockRemoved` (lines 532-533), i.e. only for its own grid. A drone block that lives on another grid would never count.
* `CanBuild()` (line 1188) tests connectivity against `this.CubeGrid` only. So the first drone block (the drone's merge block, mount-attached to the printer's merge block) is built into the printer grid.
* Grids split only through `MyCubeGrid.DetectDisconnects` -> `MyDisconnectHelper.Disconnect`, which groups blocks by `MySlimBlock.Neighbours` (mount-point links) after a block was **removed** (`m_disconnectsDirty` is set from block removal, MyCubeGrid.cs 4923/5029/5042). Switching a block off removes nothing.
* A merge pair exists between two different grids (`MergeGrid_MergeBlock` moves the smaller grid's blocks into the larger one and closes it, MyCubeGrid.cs 5749-5800). The wiki says unmerge works "only if the Merge Blocks are the only contact point"; here the drone and printer are one grid from the first weld.
* Community: Steam thread 3775742015032638218 (a ship printed from a projector on a base "stayed part of the base grid even after the connecting bridge was removed ... It's one grid"; advice: put the projector on a rotor head so what is built is a subgrid); the user's own `ref/user-samples/Starburst - v4/bp.sbc` carries "Projector - Bay 1" on a **small drone subgrid hanging on a rotor head** (Small Grid 4752), welded by a large-grid welder on a piston: the owner already built a working printer that way.
* Repo: `tools/designs/printer_lib.py:24-28`, `docs/xml-notes.md`, `packages/unknowns.md` item 13 describe it as UNVERIFIED; `HiveMotherAI.cs:507-513` detects it after the fact and faults.
* Fix options: (A, cheapest) a **sprue**: put a 4-5 cell neck of armour in the drone blueprint between drone and printer so the drone's only link to the printer is projected cells, add a small ship grinder aimed only at the neck (script switches it on in RELEASE; grid splits when the neck is ground; the neck is re-welded next print, and the drone keeps nothing). The grinder radius (SensorRadius, 1.3 m for tools) must not touch drone or printer blocks, so the neck must be at least 3 cells long on each side of the grinder centre. Whether a ship grinder also grinds blocks of its own grid is not verified (see tests). (B) put projector and welders on a rotor/piston head grid and let the drone BE the head grid (projector rides away with the drone, 130 kg, 50 PCU; needs a fresh head per drone, not automatable). (C) abandon projector printing and use pre-spawned drones in merge-block racks (Stinger Rack concept without welders).

### printers-02 (high; critical for survival) Components: no conveyor path, stock is 9x too small
* Wiki Welder_Block: the welder takes components from its own inventory first and pulls from a conveyor network when attached to one. `docs/xml-notes`/unknowns say "stock must sit in a cargo container touching a welder's port": direct port-to-port adjacency does connect inventories in the game [rec, 80 %], but in `printer_lib.py` `pod()` the two `[Stock]` containers are 1x1x1 cells (0.5 m, 0.125 m3 each by the container XML the game wrote, line 654) at (1,1,1) and (1,1,-1) inside a solid 5x5x5 armour pod; the welders are on bay stalks outside the pod with armour scaffold between. No cargo block touches a welder, no conveyor block exists in any printer. So welders never receive stock.
* Volumes from `defs_stats.json` components: Gnat 163 components, 1,074 L, 1.94 t; Hornet 341 components, 2,228 L, 4.45 t; Longbow S 445 components, 3,555 L; Mantis 457, 3,709 L. The two stock cubes hold 250 L total.
* Creative: `MySlimBlock.IncreaseMountLevel` (MySlimBlock.cs ~1456) has `if (MySession.Static.CreativeMode) ClearConstructionStockpile(...)`: no components needed [KS; matches play experience]. Survival, or survival with creative tools: needed.
* Script side: `StockEmpty()` (HiveMotherAI.cs:718-731) only looks at `[Stock]` blocks; it never looks at the welders' own inventories, which is where real stock has to be. With an empty (unconnected) `[Stock]` in creative it prints "STALLED: component stock empty - refill" and sets `Starved`, which freezes `BuildTimeout`/`FinishTimeout` for ever (lines 470, 575), so a bay that is stalled for another reason never faults.
* Fix: add real cargo (3x3x3 or 5x5x5 small container) touching a welder port, or conveyor tubes from a cargo block to every welder; mount Stock next to the welder cluster per bay; make `StockEmpty` count welder inventories; document "creative = nothing to load".

### printers-03 (high) What the fused state does to the scripts (not covered by any sim test)
The sim's DockLaunch test merges two real bodies; the game never does that for a printed drone. Consequences if the drone stays on the printer grid:
* `launch_block.cs` `LnDocked()` looks for a **connected** merge pair (`IsConnected`); there is none, so the printed drone's PB is in state 0 "free". HiveMother ARM writes `[Launch] Armed=true` and runs `armed` (HiveMotherAI.cs:826-845): trigger 2 (`Armed` while not docked) fires. `LnReady()` (launch_block.cs:146-151) waits at most 9 checks (~1.7 s) while 2 merge blocks are on the grid, then returns true. `LnBegin` collects thrusters/gyros with `CubeGrid == Me.CubeGrid`, which in the fused case is the printer grid, so the drone's PB overrides the **printer's** thrusters and gyros for `Burst Seconds`. The printer and drone fly off together, then the drone's mode GUARD runs on the printer's controller.
* A `_JOIN` on a drone that broadcasts `_SWARM` while still on the printer: the drone's `_SWARM` carries the printer grid id; HiveMother counts it as a live drone and extends the join window; `LnComplete()` (every terminal block of the grid functional) can be true while the drone is still a skeleton if the rest of the printer is functional.
* Stinger Rack: `StingerAI.Docked()` = merge `IsConnected`, false when fused, so docked Stingers consider themselves free and `Auto Launch When Released` may start thrusting on the shared grid.
* Fix: gate the drone PB on `Me.CubeGrid` containing a block named `[Bay` / the printer's PB (refuse to burn on a grid that holds a Hive Mother PB), independent of the merge state.

### printers-04 (medium) Projected PB: the Program survives; the "Recompile" call is dead code
* Good news [KS]: `MyProjectorBase.Build()` rebuilds the block from `m_originalGridBuilder` (the full builder of the projection, `blockBuilder.Clone()` + `SetupForProjector()`), so `Program`, `Storage` and the CustomData ModStorage component are copied into the welded block. The preloaded `ProjectedGrids` is copied straight into `m_originalGridBuilder` in `UpdateOnceBeforeFrame` (line 1014-1022) and is **not** script-stripped; stripping (`RemoveScriptsFromProjection`, `programmable.Program = null`) happens only for a blueprint sent over the network by a user who is not a scripter (line 1360-1376), and on paste of a grid for a non-scripter (`MyCubeGrid.cs` 8432-8445, top-level blocks only, the nested `ProjectedGrids` is untouched). In single player and for scripter admins the code survives. (Current game may differ: the 2018 snapshot predates `ProjectedGrids` plural.)
* `MyProgrammableBlock.UpdateOnceBeforeFrame` (lines 565-583) compiles the program on its own when `m_programData != null`, so a welded PB compiles by itself, constructor included, while it may still be a skeleton; `Main` runs once it is working (`TryRun` returns false before that).
* `BuildBlockSuccess` -> `ChangeBlockOwner(..., ownerId)` (MyCubeGrid.cs 3425-3450) sets the owner of every welded block to the id passed by the welder (welder's owner, [rec]); `GetOwnershipFromProjector` (XML `false`) only affects the "spawn projection" feature (OnSpawnProjection, line ~1100), never welding. In survival `OnOwnershipChanged` terminates the program (MyProgrammableBlock.cs 770-783), but that happens before the next-frame compile, which clears the termination; a later owner change (player "transfer ownership" of the printer grid in survival) terminates the drone PBs and only a manual Recompile brings them back.
* `IMyProgrammableBlock` has no Recompile; Recompile is a terminal **button** (`MyTerminalControlButton`, MyProgrammableBlock.cs:231), only Run / RunWithDefaultArgument are terminal actions (236-244). `b.DPb.ApplyAction("Recompile")` (HiveMotherAI.cs:482) therefore very probably does nothing (see also `docs/audit/timers_pbs.md`, `script_logistics.md` SL line 242). Effects: DoRecompile only adds a 3 s delay. Not harmful. Remove or relabel; rely on the automatic compile; check `DPb.TerminalRunArgument`/ `TryRun` result (already done) as the real readiness test.

### printers-05 (medium) `hold` and the FUSED hold do not stick
`GetBlocks()` runs every 60 ticks (10 s) and executes `bool held = C(sec, "Hold", bay.Held); bay.Held = held;` (HiveMotherAI.cs:258). `C()` returns the Custom Data value when the key exists, so the in-memory `Held` set by `hold <n>` (line 910) or by the FUSED fault (line 512) is overwritten with `Hold=false` from Custom Data within 10 s. After a FUSED fault the bay retries after `Fault Retry Seconds` (60 s) and repeats the cycle for ever. Fix: write `Hold` back to the ini when it changes (or keep a separate runtime flag that GetBlocks ORs with the ini value).

### printers-06 (medium) The sim cannot test any of this and is 4-10x optimistic on build speed
* `sim/src/Api/ProjectorWelderApi.cs`: `RemainingBlocks` counts down at `ApiTuning.PrintBlocksPerSecond = 2.0` blocks/s while any welder is working: no support chain, no component need, no reach, no integrity phase. Hornet 54 blocks = 27 s in the sim. `BuildableBlocksCount` = Remaining. Welded blocks are never created, so FINISH runs with `Skip Finish Check`.
* `SimMergeBlock.Release()` splits the body (`ConnectivityDirty`), which is right for two genuinely merged grids and wrong for blocks welded from a projection (printers-01).
* `printer-test` PASS therefore only proves the script survives faulty stand-ins; it says nothing about printing. This is stated in `docs/foundry-notes.md` but `packages/changelog.md` v2.5 reads as if the cycle were tested.

### printers-07 (medium) Build time and the support chain
Per-block `build_time_s` from `defs_stats.json` (1x welder seconds): Gnat 19 blocks, 150 s total; Hornet 54 blocks, 367 s; Longbow S 55 blocks, 386 s; Mantis 55 blocks, 401 s. A block takes `BuildTimeSeconds / speed` of welding (`IncreaseMountLevel(amount)`: `amount *= IntegrityPointsPerSec`, MySlimBlock.cs:1459; the welder's speed multiplier is not in the files, [rec] 1x ship welder, session `WelderSpeedMultiplier` default 1, medium confidence).
* Lower bound with perfect parallelism: Gnat 150/7 welders = 21 s, Hornet 367/12 = 31 s.
* Realistic: each block can only be placed when its neighbour exists; `CheckConnectivity` uses the mount points of the neighbour's **current build stage** (`GetBuildProgressModelMountPoints(BuildLevelRatio)`), and `reach.support_report` assumes full mount points once a block is placed. The 7-9 round dependency chain then dominates: estimate 2-5 minutes per Hornet, 1-2 minutes per Gnat, plus CHARGE (up to 25 s), ARM 0.5-3 s, RELEASE 2.5 s, COOLDOWN 6-24 s. About 10-20 drones per hour per bay for Gnats, 8-15 per hour for Hornets in the best case; `docs` quote no figures.
* Script timeouts (`BuildTimeout` 600 s, `FinishTimeout` 360 s) are consistent with this; the stall detector (45 s without a change of `RemainingBlocks`) can warn falsely during a long thruster/gyro weld in BUILD because `RemainingBlocks` only counts placement.
* Power: 2 kW per running welder regardless of work (wiki) = 24 kW for 12 welders; negligible against a 4 MW pod battery.

### printers-08 (medium, unverified) The merge block's clamp face may not mount-connect
`MySlimBlock.ConnectionAllowed` (MySlimBlock.cs:673-683) lets a FatBlock veto a mount-point link (`CheckConnectionAllowed` / `ConnectionAllowed` overrides) and `DisconnectFaces` removes faces. I recall [rec, 50 %] the merge block uses this so its merge face does not count as a connection (this is why "unmerge" splits grids). If so: (a) the drone's merge block placed against the printer's merge block would not be connected to the printer by the neighbour graph; `CanBuild`/`CheckConnectivity` may still allow the first weld (it uses definition mount points), but the whole drone would be a floating group inside the printer grid and the first block removal anywhere on the grid (any grinder, any destroyed block) would split it off as a new grid (the same "floating blocks" split seen with the old paste bug, docs/audit/blueprints.md 2.3); (b) `reach.support_report` is wrong in its first step. A cheap in-game test settles it (P2 below). Source file `MyShipMergeBlock.cs` is not retrievable here.

### printers-09 (low) Battery charge of welded batteries
`unknowns.md` item 22 assumes a welded battery starts at 30 % definition charge. `MyProjectorBase.Build()` clones the original builder, and the generated `ProjectedGrids` write `CurrentStoredPower=0.05` = full (`tools/designs/_common.py:62-65`, verified in `Hornet Forge/bp.sbc`). Unless `MyObjectBuilder_BatteryBlock.SetupForProjector` zeroes it [rec, unknown], welded drone batteries come out full and the 25 s CHARGE phase is mostly redundant. Test P6.

### printers-10 (low) First in-game compile of v2.2 uses `Exception.StackTrace` and `GetType().Name`
Only HiveMotherAI uses them (11 places, `ex.GetType().Name`, `ex.StackTrace`). Whitelist Overview (wiki): `System.Exception.*` and `object.*` are listed; `System.Type` lists `FullName` but not `Name` (reachable only as `System.Reflection.MemberInfo.Name`). The page calls itself "mostly a hint". If one of these is rejected, the **whole script fails to compile** (and a PB that does not compile on a fresh printer shows only a compile error). Mitigation: use `ex.GetType().FullName` or `ex.ToString()` first line, or check with MDK2's whitelist analyzer before shipping. v2.2 has never run in the game (the user's NRE report was v2.0).

### printers-11 (low) PCU / limits
Per the wiki each projected block costs 1 PCU while projected and the projector 50 PCU, welder 150, small merge block 100 (all match `defs_stats.json`); `MaxNumberOfBlocks` 200 in the XML only limits instant "spawn" projections (the drone has 54 blocks anyway). Server PCU limits and safe zones (a safe zone with building/welding disabled stops welding) are world settings the scripts cannot read; the Quad Hive (8,381 PCU) and Static Nest (6,244 PCU) will not paste under a default per-player PCU cap on many servers [rec, low confidence on the cap value].

### printers-12 (info) Documentation drift
`docs/hive-protocol.md` section 4 ("HiveMotherAI nothing on FLEET") and section 7 ("Printers are not on the fleet channel yet") contradict section 9 and the code (`FleetCh + "_CMD" "join"`, HiveMotherAI.cs:426). `docs/xml-notes.md` section 6 still lists "pre-loaded projector grids not generated" although `printer_lib.projector_settings` does it. `unknowns.md` 16 ("A creative world may not need components at all (unconfirmed)") can be upgraded to confirmed-by-source.

### printers-13 (info) Merge strength is not a holding force
`SmallShipSmallMergeBlock` `Strength 0.005` (large 0.015 per `defs_stats.json`) is the magnetic attraction before snapping; a locked pair is one merged grid (`MergeGrid_MergeBlock`, no constraint), so thrusters cannot pull it apart. Alignment: wiki magnet within about 1 m, snap/align within about 0.1 m. Irrelevant for printed drones (printers-01), relevant to carrier-hangar docking by pasted drones.

## 2. HiveMotherAI state machine review (line by line summary)

| area | result |
|---|---|
| bay lookup (`GetBlocks`, `[Bay n]` names, `IsSameConstructAs`) | correct: `"[Bay 1]"` does not match `[Bay 10]`; drone block prefixes (`Hornet ...`, `Hornet Merge`, `Hornet PB`) match what `projected_grid_xml` writes (checked in `Hornet Forge/bp.sbc`) |
| `Held` handling | BUG printers-05 |
| BUILD (RemainingBlocks==0 -> FINISH, `t>15` guard) | correct for the game's semantics: remaining = projected blocks with no real block of the same definition at that cell (MyProjectorBase.cs:735-741), skeleton counts as built. The `m_remainingBlocks` field starts at 0 before the first projection update, hence the 15 s guard is needed and right |
| FINISH scan (`GetCubeBlock`, `IsFullIntegrity`, `FatBlock.IsFunctional`, multi-cell `Seen`) | API use correct; relies on drone blocks living on the printer grid (true) and on `Drone Box` printer-grid cells (generated with translation T) |
| CHARGE | `ApplyAction("Recompile")` no-op (printers-04); `DroneCharged` 95 % fine; welders off |
| ARM / `TryRun("armed")` | TryRun is synchronous inside the printer's run and returns false for a PB that is not working yet; correct handling. In the fused case it triggers printers-03 |
| RELEASE / `StillAttached` / FUSED | logic is sound, outcome in the real game is FUSED every time (printers-01). Hold lost (printers-05) |
| COOLDOWN | `RemNow>0` after the drone left is the right signal only if the drone left the projector grid |
| Stall kick (one welder off for 2 s) | harmless, useless for the real causes (reach, components, support) |
| counting drones via IGC `_SWARM` | tuple type matches all scripts (`MyTuple<long,Vector3D,Vector3D,long,int>`), drones send ~1 Hz, expiry 6 s fine; the id is the **grid** id, so a fused drone counts as the printer's grid |
| rack mode | `launch` goes to all `STINGER` listeners but docked ones ignore it only when `IsConnected` (printers-03) |
| `Wants`/caps/ParallelBuilds | fine |
| `Nudge` thrust direction | uses `WorldMatrix.Backward` as push direction, same convention as the flying Hornet (validated in game by the owner) |
| instruction budget | `docs/audit/il_profiler.md`: peak 1,897 block-model counts; the Quad Hive constructor (60,092 IL-model) is that auditor's open point, not repeated here |

## 3. Coverage: checked and found correct

| item | verdict | evidence |
|---|---|---|
| Small projector projects small grids only; `ProjectedGrids` embedding shape | correct | wiki Projector; Starburst v4 projector element vs `Hornet Forge/bp.sbc` (same trailing fields, Scale 1) |
| ProjectionOffset in cells of the projected grid, ProjectionRotation in 90 degree steps | correct | `GetProjectionTranslationOffset() = offset * GridSize`, `SetRotation(... rotation.X * PiOver2)` MyProjectorBase.cs; repo's Starburst fit |
| Preloaded projection restores on a dynamic grid, KeepProjection from XML | correct | `UpdateOnceBeforeFrame` 1011-1025 (`CubeGrid.Physics != null`), line 506-510 |
| Projection follows the projector grid when it moves/rotates | correct (clipboard transforms from the projector's WorldMatrix) | MyProjectorBase `UpdateGridTransformations` [rec] |
| RemainingBlocks counts placed, not welded; BuildableBlocksCount = blocks placeable now | correct | MyProjectorBase.cs:735-757 |
| Keep Projection must stay on (else the projection is removed at 0 remaining) | correct | line 777 `if (m_remainingBlocks == 0 && !m_keepProjection) RemoveProjection` |
| Projector stays on, `RemoveProjection(true)` on a power blip keeps the blueprint | correct | lines 213-228, 1043-1060 |
| Welded PB keeps Program/CustomData | correct (single player) | printers-04 |
| Welder 3x3x6, SensorRadius 1.3, power 2 kW, 150 PCU | correct | `defs_stats.json`, wiki |
| Merge block small = 1x1x1, six full mount faces, Strength 0.005 | correct | `defs.json` |
| Creative mode needs no components | correct | MySlimBlock.cs ~1463 |
| Blocks inherit the welder's owner, not the blueprint's | correct (source), welder owner [rec] | BuildBlockSuccess/ChangeBlockOwner |
| Merged (real) grids share power; no conveyor sharing across a merge pair | correct | wiki Merge_Block |
| Welder reach family of hypotheses (tip -3 cells, 1.3/1.0 m) | reasonable, still unmeasured | `docs/welder-reach.md` |
| `Drone Box`/`Ignore Cells`/cell count generation | consistent with FINISH code | `printer_lib.bay_geometry`, HiveMotherAI.cs:314-328 |

Not checked or not possible here: welder internals (MyShipWelder source 404), merge block internals, the welder's real sphere centre, whether a welder divides its speed among blocks in range, game settings (`WelderSpeedMultiplier`), PCU caps.
No simulation was run: the sim does not model any of the disputed mechanics (printers-06), so a run would not be informative.

## 4. Go / no-go per concept

| concept | judgement |
|---|---|
| All 16 projector+merge printers as shipped (Hive Mother, Twin Hive, Quad Hive, Hornet/Wasp/Lancer/Longbow/Harrier/Aegis/Mantis/Watcher, Fleet Forge, Escort Carrier, Mobile Hive, Static Nest) | **NO-GO**: the drone cannot separate (printers-01); survival also has no component path (printers-02) |
| Stinger Rack (rack mode) | **NO-GO**: same, plus docked Stingers believe they are free (printers-03) |
| Creative-mode Gnat bay with a sprue + grinder release | **conditional GO as an experiment** once test P1/P3 pass; Gnat is small enough (19 blocks, 150 s of weld work) |
| Hornet-class bays | NO-GO until the reach/touch-welder question (unknowns 15) and P1 are answered; the 10-12 touching welders also make the "tip touching the drone" arrangement depend on the unverified sphere centre |
| Timer-only "Midge Nest" | out of scope; would hit the same fused-grid wall |

## 5. Minimum in-game tests (add to `packages/tests.md`, in this order, creative world, one small projector and a few welders are enough)

* **P1 fused grid (decides everything).** Project two armour cubes next to a small merge block on your own ship, weld them, put a merge block on the first cube pointing at the printer's, switch both off. Open Info / the grid list: is the welded part a separate grid? Expected from source: no.
* **P2 split-by-removal.** After P1, grind any one block far away from the welded part. If the welded part detaches as a new grid, the merge face is not a mount connection (printers-08) and a sprue is not even needed (any removal splits).
* **P3 sprue.** Weld a 5-cell neck of armour between the printer and a 3x3x3 block cluster, point a small ship grinder at the middle of the neck (only the neck in its 1.3 m sphere), switch it on: does the cluster become its own grid and does the grinder leave the cluster and the printer untouched? Also: does a grinder grind blocks of its own grid?
  Rig: tools/designs/sprue_test.py / test_designs/Sprue Test (docs/design/sprue_test.md). Keen's old public source: MyShipGrinder has no CanInteractWithSelf override (MyShipToolBase default false), so a ship grinder very probably does NOT grind its own grid; the rig measures it and has a hand-cut control.
* **P4 components.** In survival, load one welder's inventory by hand, project a block needing those components, weld: works? Then put a small cargo container touching the welder's back and see whether the welder pulls from it (conveyor port adjacency). Decides whether Stock can sit next to the welders without tubes.
* **P5 timing.** Time a full Gnat weld in creative with the shipped 7 welders (stopwatch from `start` to `0 unfinished`), then a Hornet with 12; record the Info text `Build progress`, `BuildableBlocksCount` over time, and whether blocks wait for their neighbour's progress. Calibrates printers-07 and the sim's 2 blocks/s.
* **P6 projected PB and battery.** After a weld: PB Edit window shows the code; PB runs without a manual Recompile; owner shown; battery charge of the welded battery (full or 30 %?). (T5/T6 plus printers-09.)
* **P7 compile.** Paste HiveMotherAI v2.2 onto a PB: it must compile (printers-10); report the first "ERROR ..." text if any.
* **T1 merge face** (already in the list) stays as the first test for the rack/hangar use of merge blocks.
