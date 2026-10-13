# Audit: script logistics (key `script_logistics`)

Auditor scope: would the shipped drone / printer scripts load and run in the real game as built? Script length, instruction and wall-clock budget,
startup, update sources, memory, camera budget, block lookup, destroyed-block behaviour, shared-block sync, minifier safety. Read-only audit;
nothing outside this file was changed. Date 2026-10-13. Cross-referenced with `docs/audit/il_profiler.md` and `docs/audit/timers_pbs.md`.

## 0. Evidence and limits

| Tag | Source | Weight |
|---|---|---|
| [art] | the shipped artefacts: `releases/*.zip` unpacked to the scratchpad, every `bp.sbc` parsed with an XML parser, `scripts/*.cs`, `tools/*` | measured |
| [pbapi] | the current official PB API reference https://malforge.github.io/spaceengineers/pbapi/ (fetchable from this container, one page per type, e.g. `Sandbox.ModAPI.Ingame.IMyCameraBlock.html`) | highest for "does this member exist" |
| [wiki] | https://spaceengineers.wiki.gg/wiki/Scripting/The_Runtime (edited 13 Apr 2026), `.../The_Grid_Terminal_System`, `.../Antenna_Communication_(IGC)` | high |
| [KS] | Keen's public source snapshot (old, about 2018-19): `MyProgrammableBlock.cs`, `MyGuiScreenEditor.cs`, `MyCameraBlock.cs` from raw.githubusercontent.com/KeenSoftwareHouse/SpaceEngineers/master/Sources/... Design evidence only: constants and newer features may have moved | medium |
| [rec] | my recollection of the game, confidence stated | low |

Not possible here: running the game, compiling against the real game DLLs, measuring wall-clock cost of the real API. The scripts have still never run in
the game at these versions (packages/unknowns.md).

## 1. Script length (item a)

### 1.1 Measured sizes (the Program text inside every blueprint, parsed from `SpaceEngineers_Drones_v2.6_All.zip`)

| Script (models using it) | chars = bytes in bp `<Program>` | headroom to 100,000 | non-ASCII | lines | longest line | escaped length in XML |
|---|---|---|---|---|---|---|
| BasiliskAI (Basilisk, Basilisk Lite/Seeker/Seeker Lite) | **97,721** | 2,279 (2.3 %) | 0 | 427 | 470 | 101,955 |
| WaspAI | 95,313 | 4,687 | 0 | 416 | 470 | 99,367 |
| MantisAI | 94,623 | 5,377 | 0 | 413 | 412 | n/m |
| AegisAI | 93,509 | 6,491 | 0 | 408 | 463 | n/m |
| LongbowSniperAI (Longbow S, L, Seeker variants, Foundry) | 91,672 | 8,328 | 0 | 401 | 361 | n/m |
| LancerAI | 90,660 | 9,340 | 0 | 395 | 463 | n/m |
| HarrierAI | 89,955 | 10,045 | 0 | 389 | 463 | n/m |
| HornetDroneAI (Hornet, Watcher, Escort Carrier, Forge) | 88,840 | 11,160 | 0 | 385 | 463 | 92,747 |
| GnatSwarmAI (Gnat, Midge Lite, every hive/nest drone) | 80,844 | 19,156 | 0 | 353 | 332 | 84,610 |
| HiveMotherAI (all printers) | 33,710 | 66,290 | 0 | 147 | 308 | n/m |
| StingerAI | 13,836 | 86,164 | 0 | 60 | 442 | n/m |
| Mothership relays (loose in Scripts/, never inside a blueprint) | 5,328-5,334 | | 0 | | | |

* All 15 `scripts/*.cs` contain zero non-ASCII characters, so characters = UTF-8 bytes = UTF-16 code units; the "characters vs bytes" question is moot.
  The wiki warns that an emoji in a pasted script breaks the paste ("If pasting a script does nothing and the text contains at least one emoji...");
  there is none.
* The Program text in **every** `bp.sbc` (112 PB entries, 36 distinct blueprints) equals `minify(scripts/<name>.cs)` byte for byte (md5 of the 12 distinct
  texts compared). The Scripts/ files in all six zips equal the minified text (LF only, no CR) and `Scripts/Source/` equals the repo source modulo CR.
  No stale script anywhere in the release set.
* Unit and enforcement of the limit: `MyGuiScreenEditor.cs` line 35 `MAX_NUMBER_CHARACTERS = 100000`; the counter is `m_editorWindow.Text.Length` (UTF-16 code
  units); `TextTooLong()` is used only by the editor window (red label, Save refused) [KS]. The load path (`Init` -> `UpdateOnceBeforeFrame` -> `Recompile`) and the
  multiplayer sync (`UpdateProgram(byte[])`, `StringCompressor`) have no length check in the snapshot. So the 100,000 limit is a UI rule, not a hard
  loader rule; unverified for today's build. We stay below it anyway. If a user opens the PB in the editor and the editor counts a newline as 2 characters
  (Windows), the Basilisk is at most 97,721 + 427 = 98,148, still under.
* Is a big Program accepted from a pasted blueprint? Evidence: `ref/user-samples/Starburst - v4/bp.sbc` (game-written, user's install) carries an 88,285-character
  Program (literal LF newlines, no `&#xD;`), and `Hornet Drone`/`Longbow L` samples carry 56-61 k. So at least 88 k round-trips through the game's own save/paste.
  97.7 k is 11 % more; nothing known that would break between the two.
* XML: the escaped Program is 101,955 characters for the Basilisk (526 `&amp;`, 309 `&lt;`, 401 `&gt;`, no `&quot;`, no control characters, no CR). The
  .NET `XmlSerializer` the game uses has no per-text-node limit (default `MaxCharactersInDocument` is unlimited) [rec, high confidence]. Largest blueprint
  file is Quad Hive 1,369,467 bytes (eight embedded Gnat programs, 647 k characters of script text); no known paste-size limit, but a multiplayer paste of 1.4 MB
  goes through replication [rec, low confidence it matters].
* Real headroom problem is not the limit but growth: Basilisk 2.3 %, Wasp 4.7 %, Mantis 5.4 %. The LAUNCH block (9,290 minified characters in the Basilisk) is dead code
  on any model without a merge block (Basilisk x4 variants, Longbow L; Longbow S and Mantis unless printed with the docking stalk): freeing it gives the Basilisk 11.6 k.
  (timers_pbs section 2.1 reached the same conclusion.)

### 1.2 Minifier safety (item h)

* `bash tools/minify_check.sh` run from this audit (compiles each script as written and minified with mcs -langversion:6 and diffs the IL): **15 of 15 "same IL"**,
  no "TOO LONG". Output kept in the scratchpad (`minify_check.out`). Identifier names, string literals and token order are untouched, so behaviour is identical.
* `tools/build_all.py:129` refuses a `<Program>` over 100,000 characters.
* Caveats: IL equality proves the same program for mcs, not for the game's Roslyn; the differences between compilers cannot depend on whitespace. A verbatim
  string with an embedded newline would change meaning if whitespace were stripped; the tokeniser keeps verbatim strings whole and the IL diff (which contains
  `ldstr`) would have caught it.

## 2. Instruction budget (item b)

`docs/audit/il_profiler.md` owns the numbers. Summary and my additions:

* Counting model: the wiki states "A significant instruction is whenever any code block or junction or scope is entered" and (Grid Terminal System page) "a grid of
  37x37x37 would exceed [the 50,000 entered code blocks] limit just from the loops", which only fits block counting (37^3 = 50,653 loop-body entries), not IL counting.
  That supports il_profiler's block model. [wiki]
* il_profiler peaks (block model): GnatSwarmAI 3,076 (6.2 %), the other eight combat scripts 920-1,072 (2 %), HiveMotherAI 1,897 (3.8 %), its constructor 3,137 on a Quad
  Hive, StingerAI 84. No script is within 15x of the limit.
* The wiki also says the counter "includes any other programmable blocks invoked directly". HiveMotherAI uses `DPb.TryRun("armed")` on a drone PB: the drone's
  `Command` -> `LaunchCmd` -> `LaunchLoad` (MyIni parse and rewrite of the whole Custom Data, about 300-500 counts) is charged to the hive's run. Bounded (one call per bay per release),
  not in il_profiler's table as a separate item; 8 bays x 500 = 4,000, well under.
* Static per-tick work in the 9 combat scripts (Hornet-family function bodies are identical in Wasp, Lancer, Harrier, Aegis, Basilisk for the functions below):

| Hot path (per Update1 tick) | Cost shape | Verdict |
|---|---|---|
| `ReadIGC` / `ReadSwarm` / `FollowStep` / `ReadTgt` | O(messages pending); each drone broadcasts `<Tag>_SWARM` every 6 ticks, so every drone drains about N/6 messages per tick | O(N^2) deliveries fleet-wide, small per drone (finding SL-09) |
| `StepSlimScan` | constant 200 `CubeExists/GetCubeBlock` per tick while a scan runs; scan restarts every 600 ticks and is skipped above 30,000 bounding-box cells | bounded; a drone is 100-540 cells (computed from the Min extents: Gnat 100, Hornet 240, Mantis 540) so a pass takes 1-3 ticks |
| `CheckDamage` (every 20 ticks) | O(blocks of own grid) = 19-58 blocks | trivial; scales with hull only when fused (il_profiler: 791 counts on a dummy printer) |
| `OmniScan` | up to 3 raycasts per tick over up to 7-11 cameras; each iteration reads `AvailableScanRange` | charge-limited (section 7) |
| `MaintainLock` | up to 2 raycasts per tick | charge-limited |
| `AutoScanStep` | 1 raycast per tick; loop over cameras | charge-limited |
| `Control`: `Aim`, `Lead`, `ApplyThrust` | 6 x thruster loop, 12-14 `ThrustOverridePercentage` sets, 1-4 gyro sets of 4 properties, one `double[6]` allocation | O(thrusters), constant |
| `PruneAndSelect`, `PowerAndHealth` (every 10 ticks) | O(contacts), O(batteries + guns) | tiny |
| `Display` (every 30 ticks) | about 35 `StringBuilder.Append` + `ToString("0")`, one `WriteText`, one `Echo` of a 1.2-1.5 kB string | fine on CPU; network cost in MP, see SL-04 |
| `GetBlocks` (every 600 ticks and on grid change) | 10 `GetBlocksOfType` calls with `IsSameConstructAs` lambdas over the PB's whole terminal system, plus `ScanSlims`, `FindAi`, `ConfigureSensors` | O(terminal blocks); on a drone fused into a Quad Hive about 1 count per block per call; first tick runs it twice (ctor, then `CheckGrid` because `_myGrid` starts null) |
| GC pressure | no LINQ anywhere in the hot paths; `foreach` over `List<T>`/`Dictionary` uses struct enumerators; allocations per tick are one `double[6]` (ApplyThrust), boxed IGC tuples (about 1.5 per tick), occasional `Track` objects | negligible; no GC risk |

* Worst scaling case is the fused drone: `LnComplete`, `LnPrep`, `GetBlocks` all walk the whole construct. Everything is O(blocks) with a small constant, and the profiler
  measured 1,540-1,670 counts in the first 10 s after a split. The documented 37^3 hazard was already removed by the time-sliced `StepSlimScan` (docs/script-audit.md item 2).
* Wall-clock is the real cost, not instruction count (wiki: "1 ms is very slow ... if one script takes 1 ms then 10 PBs using it will use 10 ms of the tick budget"). The sim's
  `LastRunTimeMs` is a constant, so the repo has no estimate of real milliseconds per Main. The dominant real-time items are the physics raycasts and property setters, which
  the sim does not cost. I cannot measure them here; a one-minute in-game check is the `Run : x.xxx ms` line already on every status screen (`_avgMs`).

## 3. Startup (item c)

| Script | Update frequency | Ctor guarded by try/catch | `Runtime.UpdateFrequency` set | Storage | First-tick behaviour |
|---|---|---|---|---|---|
| 9 combat scripts (Hornet, Gnat, Wasp, Lancer, Harrier, Aegis, Mantis, Longbow S/L, Basilisk) | Update1 | **no** | last line of the ctor | `mode;home.x;home.y;home.z;homeSet` (about 60 chars) | `CheckGrid` re-runs `GetBlocks` on tick 1 (double scan); `_home` = RC position when Storage is empty |
| HiveMotherAI | Update10 | **yes**, every section in its own try/catch, frequency set first | first line | none | `GetBlocks` in ctor and every 60 runs (10 s) |
| StingerAI | Update1 | no | in ctor | none | |
| Gnat/Hornet/Longbow Mothership | Update10 | no | in ctor | none | |
| SeekerProbe | Update100 | no | in ctor | none | log of 60 lines in Custom Data |

* The PB compiles by itself after paste (`UpdateOnceBeforeFrame` -> `Recompile`) and the constructor runs under the same 50,000 counter [KS]. An exception escaping the
  constructor terminates the program permanently (`OnProgramTermination(RuntimeException)`, sticky until a manual recompile) [KS lines 427-431, 492-499]. Only HiveMotherAI
  protects its constructor. See SL-01.
* The constructor runs while a welded PB may still be an unwelded skeleton in a printed drone; all lookups there are null-tolerant (`_rc != null` checks), so the missing-block case
  is handled, but `_rc` stays null until the next `GetBlocks` at tick 600 (SL-03).
* Compile cost: each PB compiles its own 80-98 kchar source (no sharing between PBs in the snapshot, synchronous on the main thread in the snapshot, plus the IL injector
  pass). A swarm of N drones pasted or printed is N compiles. [KS + rec, low confidence for today's build]. See SL-10.
* Storage round-trips doubles with `ToString("R")` and `double.TryParse` under the current culture: consistent on one machine; if a save written on a decimal-comma machine is loaded
  on an en-US host, "1,5" parses as 15 and the saved home point is wrong by 10x. Edge case, low (SL-08).

## 4. Update source, re-entrancy, output (item d)

* No script calls `SetMessageCallback` (grep over all 15 files) so no `UpdateType.IGC` run exists; IGC is polled. Nothing uses `Once` or `Update100` except SeekerProbe.
* Combat scripts: `Tick` handles an argument only when the source has Terminal/Trigger/Script/Mod and the argument is not blank, then returns unless `Update1` is set. So a terminal
  "Run" with an empty argument, a timer `Run`, or `TryRun` does nothing harmful and cannot double-step the logic. An argument and an Update1 in the same tick arrive as flags
  OR-ed in one call and are both handled. HiveMotherAI does the same with Update10. Correct.
* `Runtime.TimeSinceLastRun` is **game time** (wiki), the scripts use it for `dt`, fall back to 1/60 when it is 0 or above 0.5 s (combat; Hive: above 2 s). The camera charge in the
  game is also game time (`MySandboxGame.TotalGamePlayTimeInMilliseconds` [KS]); the two stay consistent at low sim speed. A terminal run between ticks restarts the interval measure, so the next
  Update1 sees a short dt once; harmless.
* `Echo` is cleared at the start of every run (`m_echoOutput.Clear()` [KS line 434]). Combat scripts echo only every 30th tick (inside `Display`), so the terminal's Info panel is empty
  on 29 of 30 runs; HiveMotherAI echoes `_screen` every run on purpose ("never goes blank"). The 8,000-character cap (`MAX_ECHO_LENGTH`) is far above a 1.5 kB status. MP network effect: SL-04.
* PB screen: `_lcd.WriteText(s)` every 30 ticks (combat) / every 3 Update10 runs (Hive). Not per tick, with one exception: the no-controller path (SL-03).

## 5. Memory (item e)

| Structure | Bound | Verdict |
|---|---|---|
| `_contacts` (`Track`, hull list max 48 points) | pruned every 10 ticks by `TrackTimeout`/`GhostTime` | bounded |
| `_mates`, `_alive`, `_targets` (Hive) | pruned (3 s / 6 s) | bounded |
| `_slims`, `_slimHp`, `_slimsNew`, `_slimSeen` | rebuilt per pass, one object per block (19-58) | bounded |
| `_ignore` (`Dictionary<long,double>`) | entries are added per ignored enemy entity and only removed in one path (`_heldN`) or never; expired entries are not purged | unbounded in principle, grows by one entry per destroyed/disabled enemy grid and fragment; tens of bytes each, hours of fights add kilobytes; low |
| `_crash` | set once, **never cleared** | a transient exception leaves "Script error: ..." on the status screen for the life of the script (cosmetic) |
| `Storage` | about 60 chars | none |
| Custom Data | main + 6 shared blocks write defaults once; SeekerProbe keeps a 60-line log | bounded |
| IGC listeners | `FleetLoad`/`LaunchLoad`/`FollowLoad` disable the old listener before registering a new one on `reload` | no leak (`DisableBroadcastListener` exists in the real API [pbapi]) |

No static caches and no unbounded string building.

## 6. Block lookup and destroyed-block behaviour (item g)

* All combat scripts select blocks with `IsSameConstructAs(Me)` (the Hornet family) and the printer with `IsSameConstructAs(Me)` plus a name tag (`[Bay`, `[Stock]`); the LAUNCH block uses
  `CubeGrid == g` on purpose (never touch the printer's thrusters). StingerAI uses `CubeGrid == Me.CubeGrid && CustomName.Contains("Stinger")`.
* Wiki facts that matter: the grid terminal system contains the PB's grid plus grids connected by rotors, pistons **and connectors** (not landing gear), and only blocks the PB's owner can access;
  `IsSameConstructAs` is the mechanical group (rotors, pistons) and "you cannot distinguish between grids that are merged with a merge block", because a merge-block join makes one grid. So a
  drone fused to a printer sees all printer blocks as its own (handled by the launch block and by `ScanSlims`' 30,000 cell cap), and a drone docked by **connector** is excluded by
  `IsSameConstructAs` but included by a bare `GetBlocksOfType` (any script using `GetBlocksOfType` without the filter would pick up the carrier's blocks; none found in the combat scripts,
  Hive uses the filter). [wiki]
* Name/type contracts: `[AI]` by name or "OffensiveCombat"/"FlightMovement" in `BlockDefinition.ToString()`; `[Seeker]` by name; all other blocks by interface. Interfaces verified to exist (section 9).
* Ownership: `Valid()` accepts Enemies and, if `TargetNeutral`, Neutral and NoOwnership. `MyDetectedEntityInfo.Relationship` is evaluated against the camera/sensor owner, so a PB
  or camera with owner 0 (the display warns "PB has no owner") would classify everything as NoOwnership. The blueprints carry `Owner 144115188075855915` (the user's player id); the game rewrites
  owner on paste; printed blocks take the welder owner's id [rec, medium].
* Single points of failure in every drone blueprint (counted from the bp files): **1 Remote Control, 1 Programmable Block, 1 gyro** on all of Gnat, Hornet, Wasp, Lancer, Harrier, Aegis, Mantis,
  Longbow S/L, Basilisk, Watcher; 1 battery on Gnat, Mantis, Longbow S. Losing any of RC, PB or gyro ends the fight for that drone. Design note, outside scope of a script fix.

### What happens when a block dies mid-fight

| Case | Behaviour in the code | Real-game outcome |
|---|---|---|
| Thruster, gun, camera, gyro destroyed | Handles stay in `_thr`, `_guns`, `_cams`, `_gyros` until the next `GetBlocks` (max 600 ticks = 10 s). Loops test `IsFunctional` / `IsWorking` before use; `MaxEffectiveThrust` sums only `IsWorking` thrusters | Safe: no exception expected, because a destroyed block has no integrity so `IsFunctional` is false. Thrust budget is overestimated for up to 10 s if the stale thruster still reports working (a block broken off with its own grid fragment has no power). `GridTerminalSystem.CanAccess(block)` would detect a gone block exactly; unused. Low. |
| Remote Control destroyed or disabled | `Tick`: `_err = "No working Remote Control"; Display(); Release(); return;` **every tick**, in all 9 combat scripts | Whole status string is rebuilt and written to the LCD and Echo each tick (text differs every tick because of `Run ms`/`Ins`), a network event per tick in MP (SL-04), `Release()` re-sends overrides. `GetBlocks` (which could adopt a second controller) only runs when `_tick % 600 == 0`. SL-03. |
| RC on a detached fragment (the drone is cut in half) | `CheckGrid` only reacts to `Me.CubeGrid` changing; the RC stays "functional" | `_rc` velocity/orientation/position come from the other piece while thrusters/gyros belong to this one; control is wrong until the next 600-tick refresh. `_rc.IsSameConstructAs(Me)` per tick would catch it. SL-03. |
| PB destroyed | script gone | drone dead (no second PB exists) |
| Exception inside `Tick` | `Main` catch: stores `_crash`, `Echo`, `_lcd.WriteText("SCRIPT ERROR...")`, and **returns; no Release, no re-init, no throttle** | Next tick runs again and usually throws again: LCD text and Echo rewritten every tick (60 Hz event spam in MP), and the last gyro/thruster overrides stay active, so the drone keeps accelerating/turning open-loop with whatever command it had. SL-02. |
| Script too complex / out of memory type errors | not catchable | program terminated, sticky; only a player recompile clears it [KS]. il_profiler IL-05: the sim keeps it dead too. Not reachable with the block model (6 % max) |

## 7. Camera / raycast budget (item f)

* Charge: `AvailableScanRange += elapsed game ms x RaycastTimeMultiplier` (defs value 2.0, i.e. 2 km/s per camera), uncapped (`double.MaxValue`), accrued lazily only while the
  camera is working **and** `EnableRaycast` is true, and credited for the whole time since the last read [KS `MyCameraBlock.cs` lines 54-66]. A ray costs its length; `Raycast(Vector3D)`
  costs the distance to the point; failing for lack of charge costs nothing and returns an empty info. The sim (`CameraApi.cs`, `SimSettings.CameraChargeMetersPerMs = 2.0`, no cap) matches.
* Budget per drone: 7 cameras (Hornet family), 8 (Mantis), 11 (Longbow) give 14-22 km/s of ray length. Typical lengths: close scan 120 m (needs 240 charge), hard/under-fire scan 900 m (needs
  1,800), auto-scan 1,200, AI confirm rays 500-2,600. The code allows 3 (OmniScan) + 2 (MaintainLock) + 1 (AutoScan) + AI/Kz/Breach rays per tick but every site is gated on
  `AvailableScanRange`, so the physical cap is 2,000 m/s / 60 = 33 m of charge per camera per tick, about 233 m per tick for 7 cameras: roughly 0.2 long rays or 2 close rays per tick, sustained.
  So raycasts per drone per tick are about 0.3-2, never the 6-7 the call sites would permit. Instruction cost of the scan loops is small (each iteration 5-10 counts).
* Real CPU cost: Keen's own comment on the raycast path says "try a physics raycast first, very accurate, but very slow" and it is a synchronous `MyPhysics.CastRay`, so a
  fleet of 20 drones at about 1 ray/tick is about 20 physics ray casts per tick on the main thread. Magnitude per cast unknown to me [rec, low confidence]. This is the real scaling limit of
  a big swarm, not the instruction count. SL-05.
* Fidelity: in the snapshot the camera's `Raycast` returns the first hit in the hit list that is not the camera block itself (`if (entity == this) continue;` compares a grid entity against
  a block, so it never skips the drone's own grid). A ray that starts inside the drone's own collision shape or crosses its own hull returns the **own grid** and spends the full ray
  length. The scripts discard own-grid hits (`Valid()`), so the effect is wasted charge. The sim's `Raycast.RayWorld(..., Inst.Body, ...)` excludes the own body, so the sim never wastes
  rays on itself. Impact depends on camera placement (cameras on the outside of the hull face away from it, so mostly nil; guns and antennas inside the 45 degree cone would hit). Not
  verified for the current build. SL-05.
* Free charge at spawn: because charge is credited lazily for the time since block creation while working, a freshly pasted or printed drone's first `AvailableScanRange` read can credit
  minutes of charge. The sim starts cameras at 0 (`SelfTestB`: "no charge while raycast is off"). Small advantage for the real game; info.

## 8. Shared-block sync (item h)

`python3 -I tools/<x>_sync.py --check` for all seven (ai_gate, aim, fleet, follow, kz, launch, lead): all exit code 0, every listed script "ok" (launch reports "absent StingerAI (not integrated yet)").
My own hash comparison of the marked regions across `scripts/*.cs`:

| Shared block | Chars (source) | Copies, all byte-identical | Scripts that carry it |
|---|---|---|---|
| AI GATE | 5,434 | 9 | the 9 combat scripts |
| BREACH + SEEKER | 8,186 | 9 | the 9 combat scripts |
| FLEET LINK | 5,730 | 13 | 9 combat + 3 Motherships + StingerAI |
| FOLLOW | 12,711 | 8 | the 8 combat scripts without the Gnat |
| KAMIKAZE DEFENCE | 14,216 | 9 | 9 combat |
| LAUNCH | 14,960 | 9 | 9 combat (dead code in models with no merge block) |
| LEAD | 6,398 | 9 | 9 combat |

The Basilisk is in every sync tool's `SCRIPTS` list and carries every block unchanged. Beyond the sync blocks the forks are not synchronised: 203 distinct
method names in the 9 scripts, 82 are in all 9 and byte-identical (after removing comments and whitespace), 34 are in all 9 but differ in at least one script, 87 are not in all 9.
The Hornet, Wasp, Lancer, Harrier, Aegis and Basilisk bodies are identical for `StepSlimScan`, `Release`, `SetFire`, `GetBlocks`; the differences are deliberate (Mantis/Longbow railgun
state, Gnat voting arrays; the Gnat has 19 methods of its own). Because the no-RC path, the catch handler and `Display` are identical copies, every finding in sections 3-6 applies to all nine at once.

## 9. API existence against the real reference (extra check)

Method: parse every `interface/class/struct` in `stubs.cs`, fetch its page from https://malforge.github.io/spaceengineers/pbapi/<Namespace>.<Type>.html, compare every stub member name with the
page's member tables (own and inherited). Script in the scratchpad (`apicheck2.py`, cache `apicache/`). Result:

* Every member declared in the stubs for these types exists on the real type: IMyBatteryBlock, IMyBroadcastListener, IMyCameraBlock (including `Raycast(double,float,float)`, `Raycast(Vector3D)`,
  `Raycast(double,Vector3D)`, `CanScan` x3, `TimeUntilScan`, `AvailableScanRange`, `EnableRaycast`), IMyFunctionalBlock, IMyGridProgramRuntimeInfo, IMyGridTerminalSystem
  (GetBlocksOfType, GetBlockGroups, SearchBlocksOfName, CanAccess ...), IMyGyro, IMyIntergridCommunicationSystem (`DisableBroadcastListener`, `RegisterBroadcastListener`), IMyLargeTurretBase
  (`HasTarget`, `GetTargetedEntity`, `IsAimed`, `Azimuth`, `Elevation`, `GetTargetingGroup`, `SetTargetingGroup`), IMyPowerProducer, IMyProgrammableBlock, IMyProjector, IMyRadioAntenna,
  IMyRemoteControl, IMySensorBlock (`DetectedEntities(List)`), IMyShipController, IMyShipMergeBlock (`IsConnected`), IMyShipWelder, IMySmallMissileLauncher, IMyTerminalBlock (`IsSameConstructAs`),
  IMyTextSurface/Provider, IMyThrust (`MaxEffectiveThrust`, `ThrustOverridePercentage` ...), IMyTurretControlBlock (`HasTarget`, `GetTargetedEntity`, `IsAimed`, `Range`, `AIEnabled`),
  IMyUserControllableGun, IMyWarhead, IMyCubeBlock, IMyCubeGrid (`CubeExists`, `GetCubeBlock`, `Min`, `Max`, `GridIntegerToWorld`), IMyEntity, IMyInventory, IMySlimBlock, MyIni, MyIniValue,
  MyIniParseResult, MyIGCMessage, MyShipVelocities, MyShipMass, BoundingBoxD/SphereD, Vector3D/MatrixD/QuaternionD statics (`CreateFromAzimuthAndElevation`, `ProjectOnVector`, `Reject`, ...).
  The only residual "MISSING" entries are parameter and local-variable names inside stub bodies, not API members.
* Namespace slips in the stubs, harmless because the PB wrapper imports both namespaces: `IMySmallGatlingGun` and `IMySmallMissileLauncherReload` live in `Sandbox.ModAPI.Ingame` (stub: `SpaceEngineers.Game.ModAPI.Ingame`),
  `MyDetectedEntityInfo` in `Sandbox.ModAPI.Ingame` (stub: `VRage.Game.ModAPI.Ingame`). `IMySmallMissileLauncherReload : IMySmallMissileLauncher, IMyUserControllableGun` matches the stub.
* Real members the stubs lack (not used today): `IMyUserControllableGun.Shoot` (bool toggle) and **`IMyUserControllableGun.ShootOnce()`** (typed, documented). The Wasp, which per
  unknowns.md "relies on the terminal action ShootOnce (name from memory, unverified)", calls `g.ApplyAction("ShootOnce")` at `WaspAI.cs:2073`; the typed `ShootOnce()` removes that unknown.
* Typed AI-block API exists and the stubs/scripts ignore it: `IMyOffensiveCombatBlock` (`SelectedAttackPattern` long get/set, `TargetPriority`, `UpdateTargetInterval`, `LastScan`,
  `SearchEnemyComponent`), `IMyFlightMovementBlock` (**`SpeedLimit` float get/set**, `FlightMode`, `PrecisionMode`, `CollisionAvoidance`, `LookAtPosition`, `CurrentWaypoint`, `IsAutoPilotEnabled` get-only),
  `IMyBasicMissionBlock` (`SelectedMissionId`). Contrary to packages/unknowns.md ("AI Flight speed limit 10 m/s ... which a blueprint cannot pre-set"), a script can set the flight
  speed limit, and it can read `IsAutoPilotEnabled` to verify that the AI is actually on instead of guessing a terminal property id containing "behavio". There is still no typed setter for the
  Behavior bool, so the `AI GATE` property-id search stays unverified. SL-11.
* `ApplyAction("Recompile")` (HiveMotherAI.cs:482): `IMyProgrammableBlock` has only `TryRun`, `IsRunning`, `TerminalRunArgument` (no Recompile member); consistent with timers_pbs (75 % it does nothing).
* `ApplyAction("Shoot_On"/"Shoot_Off")`, `"KeepProjection"` (`SetValue<bool>`) are standard ids used by the community; not contradicted by the reference.

## 10. Findings

Severity scale per the brief. IDs match the structured result.

| ID | Sev | Title |
|---|---|---|
| SL-01 | medium | Combat-script constructors are unguarded: any exception kills the PB for good |
| SL-02 | medium | `Main`'s catch re-throws every tick, spams the LCD and leaves stale overrides active |
| SL-03 | medium | No-controller path: Display + Release every tick, no re-lookup, no fragment check |
| SL-04 | medium | Echo/Detailed-info and LCD churn creates network events (MP) |
| SL-05 | low | Raycast wall-clock cost and own-grid hits are not modelled |
| SL-06 | info | Instruction budget: block model supported by the wiki, margins |
| SL-07 | low | Script size headroom thin on four models; dead LAUNCH code |
| SL-08 | low | State hygiene: `_ignore` growth, `_crash` stuck, damage-baseline gap, culture-dependent Storage |
| SL-09 | low | IGC fan-out is O(N^2); default AntennaRelay needs working antennas |
| SL-10 | low | Compile hitch per PB (one compile per drone) |
| SL-11 | medium | Typed AI-block API (SpeedLimit, attack pattern, IsAutoPilotEnabled) not used; docs say it cannot be pre-set |
| SL-12 | low | `ApplyAction("ShootOnce")` and `ApplyAction("Recompile")` should be replaced / verified |
| SL-13 | info | Mothership relays and SeekerProbe never run in the sim or the game |

## 11. Checked and found correct

| Item | Result |
|---|---|
| Every blueprint's Program equals `minify(scripts/*.cs)` | yes, 36 blueprints / 112 PB entries, 12 distinct texts |
| Zip Scripts/ and Scripts/Source/ match the repo | yes, all six zips |
| Largest script 97,721 chars, all ASCII, chars = bytes | yes |
| `minify_check.sh` same IL for 15/15 | yes |
| Shared blocks identical in every script; all seven `--check` exit 0 | yes |
| All stub members exist in the real API | yes (section 9) |
| `UpdateType` / `UpdateFrequency` flag values in `stubs.cs` | equal to the game's (Terminal 1, Trigger 2, Mod 4, Script 8, Update1 16, Update10 32, Update100 64, Once 128, IGC 256; frequency 1/2/4/8) |
| Update-source handling, no double stepping from Terminal/Trigger | yes |
| `TimeSinceLastRun` use (game time, 0 on first run) | yes |
| No LINQ / no heavy per-tick allocation | yes |
| Hot loops bounded (slim scan 200/tick, GetBlocks 600 ticks) | yes |
| Track/mate/contact dictionaries pruned | yes |
| Listener register/disable on reload | yes |
| IGC payload types (MyTuple of long/Vector3D/double/int, string, Vector3D, double) | accepted types per wiki list [rec: Vector3D is allowed, medium-high] |
| Instruction limit 50,000, call chain 1000 | [KS] and il_profiler |
| Camera charge model in sim vs [KS] | equal (2 km/s, uncapped, game time) |
| HiveMotherAI protects constructor, sections, bays | yes |
| Whitelist: no Reflection/IO/Threading use; `DateTime.Now` only in SeekerProbe (DateTime is whitelisted) | yes |

## 12. What I could not check

* Real wall-clock per Main and per raycast; whether the current build still counts instructions the way [KS] does (wiki wording says yes); whether compile is asynchronous now.
* Whether the AI block `Enabled` flag is what turns "AI Behavior" on, and the terminal-property id.
* Whether a welded PB keeps its Program and survives an ownership change in survival (timers_pbs section 1.3: `OnOwnershipChanged` terminates the program in survival [KS]).
* Today's IGC queue limits and whether Vector3D tuples are still accepted (wiki type table did not render).

Commands used: `unzip` of the six release zips into the scratchpad; Python `xml.etree` over every `bp.sbc`; `python3 -I tools/minify_cs.py`-based hash comparison; `bash tools/minify_check.sh`;
`python3 -I tools/<x>_sync.py --check`; `curl` of the KS sources and the pbapi pages; no simulation was run (nothing here needed one).
