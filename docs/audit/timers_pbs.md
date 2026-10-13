# Audit: Timer Blocks, Event Controllers and extra Programmable Blocks (key `timers_pbs`)

Auditor scope: how Timer Blocks, Event Controllers (EC) and additional Programmable Blocks (PB) would work in the drones and printers, whether to use them, what the sim lacks, and a reference architecture for large multi-PB warships. Read-only audit; nothing outside this file was changed. Date 2026-10-13.

## 0. Evidence used and its limits

| Tag | Source | Weight |
|---|---|---|
| [GT] ground truth | `pc/enemies/Tech Hunter/bp.sbc` is a **game-written** blueprint with 23 Timer Blocks and 6 Event Controllers (lines 97004-97600, 100xxx, 101xxx). No other file in the repo has populated Timer/EC toolbars. `defs.json` / `defs_stats.json` for PCU, mass, DLC. | highest |
| [KS] Keen source snapshot | `raw.githubusercontent.com/KeenSoftwareHouse/SpaceEngineers/master/Sources/Sandbox.Game/Game/Entities/Blocks/MyProgrammableBlock.cs` (39 KB) and `.../Game/Screens/MyGuiScreenEditor.cs`. **This is the old public snapshot (about 2018-19)**. It shows the design (limits, TryRun, termination), but constants and newer features (UpdateFrequency, IGC, EC) may have moved. Everything taken from it is labelled [KS]. | high for design, medium for current numbers |
| [wiki] | spaceengineers.wiki.gg pages: Timer_Block, Event_Controller, Programmable_Block, Scripting/The_Runtime, Scripting/The_Anatomy_of_a_Script, Scripting/Antenna_Communication_(IGC). | medium |
| [rec] | my recollection of the game; confidence given | low |

I could not reach github.com/malware-dev wiki (403 per the brief) and found no page that documents `TryRun` budget sharing, IGC message size or listener limits. Those are listed as UNVERIFIED below.

## 1. What the real game does (research)

### 1.1 Timer Block
| Fact | Value | Source |
|---|---|---|
| Delay range | 1 s to 1 h, entered h:m:s; stored in XML as milliseconds (`<Delay>1000</Delay>` is 1 s, `10000` default) | [wiki], [GT] (Tech Hunter has Delay 1000 and 10000, 15000, 17000) |
| Actions | up to 81 per timer (9 toolbar pages x 9 slots); actions in one timer fire together, none waits for another | [wiki] |
| Triggers | Trigger Now / Start / Stop on the block; Start from another timer's toolbar, Event Controller, Sensor, Button Panel, AI recorder | [wiki], [GT] (`Start`, `TriggerNow` items) |
| Chaining | a timer's toolbar may hold `Start` of another timer (and of itself for loops); `OnOff_On/Off` of a timer arms/disarms the chain | [GT] Tech Hunter "Printer S1..S5 / Retrack" chain |
| PCU / mass / power | 25 PCU, 30.2 kg (small), 0.1 W | `defs_stats.json` (25 PCU, 30.2 kg), [wiki] (0.1 W) |
| Accuracy | whole-second granularity, slow when sim speed < 1, can round down | [wiki], docs/research.md |
| Restart | Starting a counting timer restarts the countdown (the known "EC keeps re-starting timer" report) | docs/research.md 2.2, [rec] medium |
| Scripting API | `IMyTimerBlock` (StartCountdown, StopCountdown, Trigger, TriggerDelay, IsCountingDown, Silent) | [rec] high |

### 1.2 Event Controller
22 events, only one per EC, own grid or connected/merged grids only [wiki]: Altitude, Angle Changed, **Block Added/Removed**, **Block Integrity %**, **Block On/Off Switched**, Cargo Filled %, **Cockpit Occupied**, Connector Connected, Connector Ready to Lock, Distance to Locked Target (cockpit, AI Offensive, AI Defensive; 1-2500 m), Door Opened, Gas Tank %, Grid Speed (0-100 m/s), Landing Gear Locked, Magnetic Lock Ready, **Merge Block Merged**, Natural Gravity, Piston Position, Power Output %, Rotor/Hinge Attached, **Stored Power %** (battery, jump drive), **Thrust %**. This matches docs/research.md section 2.1 (22 events).

* **There is no "Grid Connected" event, no projector event, no PB-state event, no weapon/ammo/target-acquired event, no damage event other than Block Integrity %.**
* Slot 1 = condition true, slot 2 = condition false/reset; conditions `>=` / `<=`; AND gate = all selected blocks vs any. 10 PCU, 500 W [wiki], `defs_stats.json` (10 PCU, 38.4 kg).
* [GT] real XML (Tech Hunter "Event Controller TMR1/2/3"): `SelectedEvent` 2 (Block integrity), **`Threshold` 0.99 (a fraction, not 0-100)**, `ANDGate false`, `ConditionInvert true`, `SelectedBlocks` = list of EntityIds (duplicates of the same subtype are fine), toolbar slot 0 = `TriggerNow` of a "Weld On" timer, slot 1 = `TriggerNow` of "Weld Off". Result: a self-repair welder that switches on whenever any selected block drops below 99 % integrity. This is a working EC to Timer to block chain made in the real game.

### 1.3 Programmable Block
| Fact | Finding | Source / status |
|---|---|---|
| Instruction limit per run | 50,000 "significant instructions" (code blocks entered, not IL ops); `MAX_NUM_EXECUTED_INSTRUCTIONS = 50000`, `MAX_NUM_METHOD_CALLS = 10000` in the snapshot; call chain depth 1000 (rec). | [KS] lines 146-147, [wiki] |
| Per PB or global | The counter is created per run by `IlInjector.BeginRunBlock(...)` inside `RunSandboxedProgramAction` of each PB: **every PB has its own 50,000 per run**. | [KS] line 448 (design); UNVERIFIED for nested `TryRun` (the snapshot has a "nested run" branch: `NestedTooComplex`, program not reset, but I could not see whether the nested run gets a fresh counter) |
| `TryRun(arg)` | Runs the target **synchronously, inside the caller's run**, returns false if target has no instance, is running, not working or not functional, or has terminated; throws `ScriptOutOfRangeException` into the caller if the target overflowed. Whose budget it spends: UNVERIFIED (assume the safe case, that the callee's instructions may also count toward the caller). Re-entrancy: a PB that is running cannot be TryRun again. | [KS] lines 178-197 |
| Toolbar/timer `Run` | `Run(argument)` is synchronous too (`ExecuteCode`), arrives as `UpdateType.Trigger` (terminal button = `Terminal`), only if the PB is working and functional. | [KS] lines 501-515 |
| Sticky termination | After "Script too complex" (and after runtime exception, ownership change in survival, missing Main) `m_instance` and `m_assembly` are nulled and `m_terminationReason` stays set: **every later run returns immediately** (`return m_terminationReason`). Only `Recompile()` clears it (it resets the reason and recompiles with the saved Storage). | [KS] lines 427-431, 492-499, 329-336, 607+ |
| Recompile | `Recompile` is a **terminal button** (and editor OK). The only toolbar actions registered on the PB are **`Run` and `RunWithDefaultArgument`**. The wiki says a script can use only actions that can be placed on a toolbar. Therefore `block.ApplyAction("Recompile")` almost certainly does nothing, and neither a timer, an EC nor another PB can recompile a halted PB. A Steam-forum answer says the same ("only a player can recompile"). | [KS] lines 231-243, [wiki Terminal_Properties_And_Actions], forum; confidence 75 % for today's game |
| Compile at load | `UpdateOnceBeforeFrame` after `Init` calls `Recompile()` if `m_programData != null` and in-game scripts are enabled. So a PB created from a blueprint (paste, or welded from a projection if the `<Program>` text travels with the projected block) compiles **by itself**, no Recompile needed. The constructor then runs while the block may still be an unwelded skeleton (not functional); `Main` only runs once the block is working. | [KS] lines 556-585 |
| Ownership | In **survival** `OnOwnershipChanged` terminates the program (reason `OwnershipChange`); someone must recompile. Creative: not applied. | [KS] lines 770-783 |
| Script length | 100,000 characters, counted as `Text.Length` of the editor window; **enforced in the editor UI only** (`TextTooLong()` in `SaveCode`, red counter). The block load path (`Init` -> `Recompile`) has no length check, so an over-long `<Program>` in a blueprint or save would very probably still compile. Not enforced by the server for already-stored text as far as the snapshot shows. UNVERIFIED for the current game; do not rely on it. | [KS] MyGuiScreenEditor.cs line 35, 375; MyProgrammableBlock.cs 296-316 |
| Echo | at most 8,000 characters kept in DetailedInfo | [KS] line 148 |
| Storage | a string saved with the world; kept across Recompile; no size limit seen in the snapshot (the repo's scripts use about 60 chars) | [KS] 338-360 |
| Power / PCU | 100 PCU, 138.4 kg small; the sink asks 0.0005 MW when working; no work without power ("IsWorking") | defs_stats.json, [KS] line 542 |
| Server cost | Every PB runs on the main thread inside the 16.67 ms tick; "if one script takes 1 ms, 10 PBs running it use 10 ms". There is no documented "Script took too long" message I could find; sim-speed protection is by admin policy / Torch, not a built-in per-script abort (UNVERIFIED). | [wiki The_Runtime] |
| IGC | Tags + immutable payloads; unicast address = the receiving PB's EntityId (`IMyProgrammableBlock.EntityId`) [rec high]; `TransmissionDistance.CurrentConstruct` keeps traffic on the grid (no antenna needed) [wiki + rec]; `SetMessageCallback(arg)` runs the PB once with `UpdateType.IGC` [rec high]. Message size and listener/queue limits: **not documented anywhere I could reach** (UNVERIFIED; sim uses 25 queue cap `[I]`). Latency: sent messages are readable on a later tick (sim: next tick); real game 1-2 ticks [rec medium]. | mixed |

## 2. Analysis per drone / printer question

### 2.1 Splitting a >95k script over two PBs (Basilisk 97,721 / 100,000; Wasp 95,313; Mantis 94,623)

Measured with `tools/minify_cs.py` (per method, minified chars; script total 97,721, my per-method sum 97,814 because of chunk boundaries):

| Module | Minified chars | Couples to flight/fire loop? |
|---|---|---|
| LAUNCH block (`Ln*`, `LaunchStep/Load/Status`) | 8,754 | writes thruster/gyro overrides only during a take-off; **inert on the Basilisk (no merge block)** |
| FOLLOW (`Follow*`, `Fo*`) | 6,697 | outputs a goal point and mode to Control |
| KAMIKAZE DEFENCE (`Kz*`) | 6,811 | needs live tracks; keep with targeting |
| FLEET LINK (`Fleet*`) | 3,210 | IGC only |
| Display | 2,732 | read-only |
| AI GATE | 2,477 | AI-block power control |
| LoadConfig | 6,039 | needs splitting by key prefix |
| RegisterFeeds | 2,441 | IGC |
| LEAD (`Lead*`) | 3,978 | hot path |
| Aim/Breach/Seeker | 6,411 | hot path |
| Control (fire/thrust) | 4,402 | hot path |

**Option 0 (do first, zero risk): delete the LAUNCH block from the Basilisk (and Longbow L, which also has no merge block).** `launch_sync.py --add` already installs it everywhere; the Basilisk has no merge block, so 8.8 k chars (9 %) are dead code. Headroom goes from 2.3 k to 11 k. For the Wasp/Mantis (which can be printed) it cannot be removed.

**Option 1 (two PBs, same grid): "Flight/Fire" (PB-A) + "Staff" (PB-B).**
* PB-A keeps Tick, targeting, tracks, AI hand-over, Lead, Aim, Control, FireRockets, Kz*, Breach/Seeker, GetBlocks. About 97.7 - 8.8 (Launch) - 6.7 (Follow) - 3.2 (Fleet) - 2.7 (Display) - 2.4 (Feeds) + about 3 (IGC glue, half of config) = **about 79 k** (21 % headroom).
* PB-B gets LAUNCH, FOLLOW, FLEET LINK, Display (both screens), RegisterFeeds, hive status. About 24 k + 4 k glue = **about 28 k**. B runs on `Update10` plus an IGC callback, so its cost is a few percent of A's.
* Crossing over IGC (unicast PB-A <-> PB-B, tag `<Tag>_A2B` / `_B2A`, `TransmissionDistance.CurrentConstruct`): A to B at 10 Hz: `MyTuple<int mode, long targetId, Vector3D pos, Vector3D vel, float hp, float batt, string status>`; B to A: `MyTuple<string cmd, Vector3D rally, double followRadius>` on change plus 1 Hz refresh. Only immutable types (the sim already enforces this).
* Latency: 1 tick A to B (sim) to 2 ticks (real, rec medium) = 17-33 ms for follow goals and display, irrelevant. Hot path (aim, fire) stays inside A, so no latency is added where it matters.
* Instruction budget: each PB has its own 50,000 per run (see 1.3). A is bounded by the same logic as today minus the moved modules; B is far under. Do not use `TryRun` between them (unknown shared budget, synchronous nesting); use IGC callbacks so runs never nest.
* Costs: +1 PB = 100 PCU, +138 kg (small) / 534 kg (large PB), duplicated `Me.CustomData` sections (each PB has its own Custom Data: A reads `[Hornet]`-style keys, B reads `[Fleet] [Follow] [Launch]`; the config loader has to be split by prefix), GetBlocks duplicated in B (cheap), a second `Storage`. **Benefit that is not about size: fault isolation.** A runtime exception or "Script too complex" in A kills only A; B keeps running and can put the ship in a safe state (clear overrides, section 2.2).
* The shared blocks in `tools/*_block.cs` were designed as copy-paste modules; they are exactly the cut lines. `launch_sync.py` / `fleet_sync.py` would need a `--target PB-B` flag so the sync check still passes.

### 2.2 Watchdog / restart of a halted PB

* **There is no unattended way to restart a halted PB.** The termination is sticky, `Recompile` is a button, not an action (1.3). Timers and ECs only offer toolbar actions, so they cannot recompile either. HiveMotherAI.cs:482 `b.DPb.ApplyAction("Recompile")` is therefore probably a no-op (it is in a `try`, so harmless); sim `SimProgrammableBlock` registers a `Recompile` action that the game does not have (ProgrammableBlockApi.cs:50), and `dock_launch_test` cases "recompile while docked / during burn" test a path that cannot happen.
* What does work, in order of value:
  1. **Prevention**: the scripts only *display* `Runtime.CurrentInstructionCount` (BasiliskAI.cs:1299). Add a cheap guard in `Tick`: `if (Runtime.CurrentInstructionCount > 0.6 * Runtime.MaxInstructionCount) skip optional work` before each optional stage (OmniScan, AutoScanStep, PatrolStep, AiRays, Kz). The instruction counter is a property read, not heavy.
  2. **Software dead-man in PB-B** (section 2.1): A sends a heartbeat; if none arrives for 1 s, B sets `ThrustOverridePercentage = 0` on all thrusters, `GyroOverride = false`, dampeners on. This fixes a real hazard: **overrides live in the thruster/gyro blocks and survive the death of the PB** (a halted drone keeps its last burn).
  3. **Timer dead-man** (single-PB ships): one small Timer (delay 5 s, Silent) whose toolbar is: thruster group `OnOff_Off`, weapons group `OnOff_Off`. The PB restarts it every second (`StopCountdown(); StartCountdown();`). If the PB dies, the timer expires and cuts thrust and fire. Cost: 1 block, 25 PCU, 30 kg, 2 API calls/s. Caveat: the thruster action list is not guaranteed to contain a "reset override" action (rec: Increase/Decrease Override only), so the timer cuts power instead of resetting; for a manually piloted ship the dead-man must be disarmed by the Staff PB while a pilot is in the seat, or it must only be armed in AUTO mode.
  4. An EC cannot see PB state, so it cannot trigger a watchdog by itself.

### 2.3 Merge-block release / launch sequencing by timers when the PB is unavailable
Possible, open loop only. The Tech Hunter chain proves multi-step timer sequencing works in the real game. A printer-side backup for HiveMotherAI:
1. EC#1 "Merge Block Merged" on the bay merge block: slot 1 = `Start` Timer R (delay = max build time, e.g. 120 s); slot 2 = `Stop` Timer R.
2. Timer R: bay merge block `OnOff_Off` (release), drone-side merge is also off by the LAUNCH block.
3. HiveMotherAI calls `IMyTimerBlock.StopCountdown()` on Timer R when it releases itself (so timer is only a fallback), or `StartCountdown()` to push the schedule.
Risks: releases a half-printed or uncharged drone (no `RemainingBlocks`, no battery check) - EC "Stored power %" can gate it only via the *printer's* own batteries because EC sees merged grids (drone battery visible while merged, so EC#2 "Stored Power % >= 90" on the drone battery is possible, but SelectedBlocks must hold the drone block's EntityId, which does not exist at blueprint time; research T7). Verdict: worthwhile only as a **last-resort backup for the Hive Mother**, not as the primary.

### 2.4 Stinger warhead arming / detonation by a timer fuse
* Timer granularity 1 s; the Stinger closes at about 100 m/s (`MaxSpeed = 100`), so a 1 s resolution is 100 m of error. Useless as a proximity fuse. StingerAI uses ray-based hull detonation (StingerAI.cs:311-328), correctly.
* The warhead has its own countdown (`CountdownMs`, `StartCountdown`) that is more precise than a timer. A useful, cheap backup is the **PB-side**: when the target is lost/Abort, call `StartCountdown()` on the heads so the missile does not coast away armed (the code now calls `Disarm()`, fine).
* Unverified in the repo and in my sources: whether `IMyWarhead.Detonate()` requires `IsArmed` (the sim assumes yes, WarheadApi.cs:41) and whether `StartCountdown` works on an unarmed warhead.
Verdict: do not use timers as fuses. Optional: a dead-man timer (2.2) that starts the warhead countdown if the PB dies near a target (self-destruct), only for Stinger.

### 2.5 Timer/EC-only "Midge Nest" printers
* Confirmed: no projector event in the EC list (docs/research.md claim 5 is right), so completion cannot be read.
* New finding: **EC "Block Added/Removed" (event 10)** on the printer grid fires for blocks welded in from a projection (the welded blocks join the printer grid). Idle-detector: EC slot 1 = `Start` Timer I (30 s) each time a block is added (a restart on every add, see 1.1); Timer I expiring means "nothing new for 30 s" -> weld off -> release. That is a real closed-loop signal. UNTESTED; it also fires early if a component shortage pauses welding (but then cargo % can pause it).
* The user's own Tech Hunter shows a full timer-driven printer exists (`Printer Print` timer: projector on, piston, welders on; chain S1-S5 and Retrack). So a timer printer is buildable; sebp cannot write its toolbars yet (4.1).
* Verdict: build **one** experimental timer-only printer only after the scripted one passes tests T5/T6/P1; it is the only option when scripts are disabled on a server.

### 2.6 Manual-override / failsafe uses
* **Cockpit Occupied EC (event 6)**: slot 1 (occupied) = PB `Run` with argument `assist` (Basilisk `Tick` handles `Trigger` arguments before the Update1 test, BasiliskAI.cs:1272-1273), slot 2 (empty) = `Run` with `guard`. Zero polling, works with the existing `Command()` verbs (`guard|follow|hunt|assist|rtb|idle`). The script can also poll `IsUnderControl` for free, so this is a convenience, but it still works when the script's own detection is disabled.
* **Stored Power % EC** (drone battery `<=` 15 %): `Run` `rtb`. The scripts already do `RTB Battery Fraction`; EC adds a second path that survives a script bug.
* **Sensor** (`DetectFriendly` true, field around the hangar mouth): toolbar `OnOff_Off` on a hangar-facing turret group while a friendly is inside the box. Cheap bay safety. Check sensor DLC in `defs.json` (SensorBlock/SmallBlockSensor is non-DLC; the `...Reskin` is Automatons DLC).
* **Thrust cut timer**: see dead-man 2.2.
* **Block integrity EC** (reactor/battery group `<=` 40 %): `Run` `rtb`/`retreat` and weld-repair like the Tech Hunter pods.

### 2.7 Update1 cost versus timer-triggered "update once" designs
* All armed drones set `Update1` (grep: Aegis, Basilisk, Gnat, Harrier, Hornet, Lancer, Longbow, Mantis, Wasp, Stinger). The real cost per run is **unmeasured** (`Runtime.LastRunTimeMs` is faked constant 0.05 ms in the sim; no in-game number is in docs/user-reports). Wiki budget: 16.67 ms per tick for the whole game; 1 ms per script x 10 PBs = 10 ms.
* A timer cannot replace Update1: minimum 1 s, inaccurate. `UpdateFrequency.Once` (re-armed in `Main`) gives a one-tick wait with no extra block; `Update10` gives 6 runs/s. Timers are right only for waits of 1 s and more that must survive a PB halt.
* Real saving from more PBs: the staff PB can run at `Update10`/IGC-callback only, so the cost of display, fleet, follow is cut by about 10 times relative to running it every tick inside the Update1 script (those modules already throttle with `_tick % N`, so the saving is small, mostly on the status text).

## 3. How the sim handles timers, ECs, extra PBs

| Area | Today | Needed to support the new designs |
|---|---|---|
| Multiple PBs per ship | **Already works structurally**: `ScriptHost.Attach` is called for every PB with `<Program>` text (BlockApiFactory.cs:115); `Runners` is a list; `Scenario.Pbs` lists PBs per ship; each has its own RNG stream, phases, Storage, IGC endpoint; same-`Body` IGC needs no antenna (Igc.cs:188). Reports (Metrics) iterate all PBs. | Test a two-PB blueprint end to end; `ScriptRegistry.GetProgramType(pb.ProgramText)` must compile two different scripts (cache key by text). |
| Instruction limit | `FakeInstructionCount = 0`, `MaxInstructionCount = 50000` never enforced (SimSettings.cs:159-161; docs/sim-audit-1.md item 9). An untracked work-in-progress (`sim/shim/IlCounter.cs`, `tools/ilcount/`, "il_profiler") implements Keen's rewriter model; budgets in section 2.1 should be checked with it. | Per-PB budget, sticky termination (`InstructionOverflow`) instead of `Kill` with the exception text; nested `TryRun` semantics once known. |
| `TryRun` | queued to next tick, always returns true (ProgrammableBlockApi.cs:34-39) | Game: synchronous, returns false when target not working/functional/dead/running, throws into caller on overflow. Make `TryRun` run immediately (re-entrancy guard on the callee only) and return the real result. |
| `Recompile` action | exists in sim (ProgrammableBlockApi.cs:50) | Game: not a toolbar action. Make it a setting (`AllowRecompileAction=false` by default) so tests show the real behaviour; keep a separate `Terminal button` path for scenarios. |
| Ownership termination | none | optional: PB dies on ownership change in survival. |
| Timer Block | `SimGenericTerminal` with no actions (BlockApiFactory.cs:73-77); no `IMyTimerBlock` in `stubs.cs` | `IMyTimerBlock` stub (StartCountdown/StopCountdown/Trigger/TriggerDelay/IsCountingDown/Silent), actions `Start/Stop/TriggerNow/OnOff*`, countdown in ticks with delay = ms, restart on Start, toolbar execution (all slots same tick, in index order), 0.1 W draw. |
| Toolbar parse | loader keeps only scalar children in `Props` (BlueprintLoader.cs:195-212); `<Toolbar>` is lost | read `Toolbar/Slots/Slot`: `Data xsi:type` (`ToolbarItemTerminalBlock` / `ToolbarItemTerminalGroup`), `Action`, `BlockEntityId`, `GroupName`, `Parameters` (`TypeCode`, `Value`); plus `BlockGroups` of the grid (groups are keyed by block `Min` cells, GT line 95713, 100163); resolve ids through the loader's EntityId map. |
| Event Controller | none; no `IMyEventControllerBlock` | evaluators at 10 Hz or per tick for events 2 (integrity, fraction threshold, invert), 4 (stored power), 6 (cockpit), 10 (block added/removed), 14 (on/off), 16 (merge), 17 (thrust %), 18 (grid speed), 22 (distance to locked target); edge-triggered slot 1/slot 2; `ANDGate`; `ConditionInvert`. About 250 lines. |
| PB actions from toolbar | `Run`, `RunWithDefaultArgument` exist (sync? queued) | argument `Parameters` string. |
| IGC | next-tick delivery, 25 queue cap `[I]`, immutable-type check, antenna graph for different bodies | real message-size/queue limits are unknown; keep the 25 and add a "delivery latency 1-2 ticks" setting for A/B sensitivity. |

Effort: Timer + toolbar + Run/Start/Stop/OnOff actions about 150 lines; EC about 250; two-PB test ship nothing new. The hard part is the toolbar/group loader.

## 4. Blueprint generator (tools/sebp.py)

### 4.1 Findings
* **Timer shape is correct** against ground truth: elements `SubtypeName, EntityId, Min, BlockOrientation, Owner, ShareMode, CustomName, Show*, NumberInGrid, Enabled, Toolbar, Delay, CurrentTime, IsCountingDown, Silent` in the same order as the game (Tech Hunter). `HAND['TimerBlock'].conf` can be upgraded from `from-source` to seen. The EC shape (`ComponentContainer` with 5 `MyEvent*` components, `Toolbar, Threshold, ANDGate, SelectedEvent, SelectedBlocks, ConditionInvert`) is also confirmed by the six Tech Hunter ECs.
* **Toolbars cannot be populated:** `EMPTY_TOOLBAR` is the only toolbar; a populated one needs real EntityIds, but ids are assigned late in `_assign_ids()` (sebp.py:302-307) so a `settings={'Toolbar': '<Toolbar>..'}` string cannot know them. `_link_elements` (sebp.py:370) already resolves ids lazily for TurretControlBlock; extend it with `links={'toolbar': [(slot, action, block_or_group, params)], 'selected': [blocks]}`.
* **Groups**: sebp writes no `<BlockGroups>` (grep). `ToolbarItemTerminalGroup` needs it (name + member `Min` cells). Per-block items work without groups (up to 81 per timer). Group items in the game file carry `GridEntityId 0` and `BlockEntityId` = the id of the block that owns the toolbar (observed quirk, Tech Hunter slot 0 of the first timer).
* Slot indices and pages: Tech Hunter uses `Index` 0-8 on page 1 (index 8 used); the generator must keep `SlotsGamepad` as in the game (empty or mirror).
* PB `Run` with argument: expected `<Data xsi:type="MyObjectBuilder_ToolbarItemTerminalBlock"><Action>Run</Action><Parameters><MyObjectBuilder_ToolbarItemActionParameter><TypeCode>String</TypeCode><Value>assist</Value>...` by analogy with the observed `TypeCode Single` for `SetVelocity` (rec medium; the PB snippet in the Keen source accepts `TypeCode.String`). **Never seen in a game file**; needs one saved sample.
* EC `Threshold` is a fraction (0.99 for 99 %), and slots are 0 (true) / 1 (false).
* A second PB: `pb_extra(script)` per block already supports it; `build_all.py:129` checks every `<Program>`. Each PB needs its own name (`[A]`, `[B]`) and the scripts must not both claim `Me.CustomData` defaults.
* DLC: `TimerBlockSmall/Large`, `EventControllerSmall/Large`, `SmallProgrammableBlock` are non-DLC; the `Reskin` variants are Automatons DLC (defs_stats.json).

## 5. Coverage: checked and found correct

| Item | Result |
|---|---|
| docs/research.md Timer facts (1 s-1 h, 81 actions, no sub-second) | correct [wiki], [GT] |
| docs/research.md EC event list (22), no projector event | correct [wiki]; also no "Grid Connected" |
| EC slot semantics (slot 1 true, slot 2 false) | correct [GT] |
| PCU: Timer 25, EC 10, PB 100, sensor 25, merge 100, small warhead 50 | matches `defs_stats.json` |
| 50,000 instruction limit used in sim | matches [KS]/[wiki] |
| 100,000 character limit and Basilisk 97,721 | metric correct (re-run minify: 97,721; 427 lines, no non-ASCII); counting unit is UTF-16 `Text.Length`; if a pasted text gets CRLF the Basilisk becomes 98,148 (+427) |
| `Main` in Basilisk catches ordinary exceptions and keeps running (BasiliskAI.cs:1265-1266) | correct design; "Script too complex" is uncatchable by design (sim IlCounter comment) |
| Argument runs reach `Command()` before the Update1 gate (BasiliskAI.cs:1272) | EC/timer `Run` integration works without script change |
| `GetBlocks` rescans every 600 ticks and on grid change (HornetDroneAI.cs:1237, 2770) | handles a PB whose constructor ran on a skeleton block |
| Scripts use `IsSameConstructAs(Me)` filters, no `GetBlocksOfType<IMyProgrammableBlock>` outside HiveMotherAI | a second PB on a ship does not confuse the drone scripts |
| Sim runs multiple PBs per ship | yes (see section 3) |
| Timer XML shape in sebp | matches the game |

## 6. Recommendation table

| Thing | Verdict | Expected benefit | Cost | Risk |
|---|---|---|---|---|
| Delete LAUNCH block from Basilisk / Longbow L | **Do** | +8.8 k chars headroom, no new block | 0 blocks | none (inert code) |
| Second PB ("Staff") on ships over 90 k | **Use** for new warships and the next growth of Wasp/Mantis/Basilisk | 20 %+ headroom, fault isolation, PB-side dead-man | 100 PCU, 138-534 kg, config split, 1 sim test | IGC limits unverified; nested TryRun budget unknown (avoid TryRun) |
| Instruction guard in `Tick` | **Do** | prevents the one failure nobody can auto-recover | about 20 lines per script | none |
| Timer dead-man (thrust/weapons off) | **Use only on ships** where a stuck override is dangerous (all Update1 ships) | contains halted-PB drift/burn | 1 timer, 25 PCU, 30 kg | cuts manual flight if left armed; must be disarmed in manual mode; `StopCountdown/StartCountdown` heartbeat |
| Timer/EC based PB restart | **Do not** | none: no Recompile action | n/a | false sense of safety; fix HiveMotherAI/doc claims |
| EC Cockpit occupied -> PB `Run assist/guard` | **Use** on piloted ships | mode switch works even if the script's own seat check fails | 1 EC, 10 PCU | `Run` param XML unseen |
| EC Stored power -> `Run rtb` | **Optional** | second RTB path | 1 EC | none |
| EC Block integrity (weld-repair like Tech Hunter) | **Use only for** capital ships with a repair welder | self-repair without script | EC 10 + timers 2x25 + welder | needs EntityIds (generator work) |
| Timer backup release for Hive Mother | **Use only as** last-resort fallback | printer never stuck forever | 1 EC + 1 timer | releases unfinished drone |
| Timer-only fuse for Stinger | **Do not** | none (1 s = 100 m) | | |
| Timer/EC-only "Midge Nest" | **Experimental only**, after P1/T5/T6 | works on script-less servers; Block-added idle-detector | EC 10 + 3-4 timers | open loop, never delivered, needs generator toolbar support |
| Sensor near hangar -> weapon hold | **Optional** | bay safety | 25 PCU | false holds when own drones fly past |
| Timers for sub-second control or Update1 replacement | **Do not** | | | 1 s minimum |

## 7. Reference architecture for a large multi-PB warship (next task)

Goals: autonomous, manual piloting, drone bays, no PB over 80 k chars, survives loss of a PB.

| Block | Role | Update | Notes |
|---|---|---|---|
| PB-A "Helm" (large PB) | flight, targeting, fire control (Control, Aim, Lead, Fire, Kz, Breach/Seeker, AI gate) | Update1, about 75-80 k | owns thrusters/gyros/weapons; `Command()` verbs as today |
| PB-B "Staff" (small or large PB) | fleet link/IGC to wing, FOLLOW, bay manager (merge on/off per bay, launch, recover), displays, power plan | Update10 + IGC callback | also the software dead-man for PB-A: no heartbeat 1 s -> clear overrides, dampeners on, weapons off |
| PB-C "Hangar x N" (optional, one per 4 bays, small, 15-30 k) | drone bay sequencer based on HiveMotherAI | Update10 | keeps bay failure from killing Helm or Staff |
| Timer D "Dead-man" | 5 s, thrusters+weapons `OnOff_Off` | restarted by Helm each second | disarmed (`Stop`) by Staff while a pilot sits in manual mode |
| Timer R(n) per bay | backup release after 120 s, stopped by Staff when it releases | | |
| EC "Cockpit" | occupied -> Helm `Run assist`; empty -> `Run guard` | | |
| EC "Bay n merged" | merged -> Staff `Run docked n`, unmerged -> `Run released n` | | event-driven bay state |
| EC "Power" | battery stored % <= 15 -> Helm `Run rtb` | | |
| EC "Core integrity" | reactor/battery group integrity <= 40 % -> Helm `Run retreat` | | |
| Sensor hangar mouth | friendly in field -> hangar turret group off | | optional |
| PCU for the above control layer | 2-3 PB 200-300 + timers 25 x (1 + bays) + EC 10 x 3-5 = about 400-550 PCU | | |

Rules: no `TryRun` between PBs, IGC unicast with `CurrentConstruct`, drain listeners every run, one authority for thruster/gyro overrides (Helm only), Staff and timers may only cut, never steer. Generator work first: toolbar/group/`BlockGroups` support in sebp (4.1) and one saved game sample of a PB `Run` toolbar item with argument (test T2).

## 8. Unverified items to test in game (add to packages/tests.md)
1. Does `ApplyAction("Recompile")` exist on a PB (print `GetActions` names of a PB).
2. Is the Basilisk (97.7 k) accepted by the editor (counter) and by the blueprint load path; what is the counter with pasted CRLF.
3. Does `TryRun` of PB-B inside PB-A count B's instructions toward A.
4. IGC: same-grid PB-to-PB latency in ticks, max message size, queue length.
5. Timer restart on `StartCountdown()` while counting; EC `Block added` firing for projector welding.
6. Thruster toolbar actions list (any "reset override").
7. Saved sample of PB toolbar `Run` with an argument (Parameters XML).
