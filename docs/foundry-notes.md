# Foundry notes (HiveMotherAI v2.2)

## v2.2: crash proofing and breadcrumbs (after "ERROR Object reference not set to an instance of an object" in the game)
The real game showed that text on the Hive Mother PB of the v2.0 'Mobile Hive' release. It is `Main()`'s catch block (`Echo("ERROR " + ex.Message)`): a
NullReferenceException inside `Tick()`, no line number, and the script then does that every tick (so no bay ever advances). The cause was NOT found
(no real-game log); v2.2 removes every candidate and makes the next occurrence self-explaining.
* Candidates guarded (v2.0 had all of them unguarded): `IMyProjector.RemainingBlocks / TotalBlocks / BuildableBlocksCount / IsProjecting` (a projector
  with no or a half-loaded projection; v2.0/v2.1 lines `int rem = b.Proj.IsProjecting ? b.Proj.RemainingBlocks : -1;` in BUILD and
  `b.Proj.IsProjecting && b.Proj.RemainingBlocks > 0` in COOLDOWN), `Enabled` on removed/destroyed welders/projector/merge blocks (`SetOn`; a block that
  threw is skipped until the next GetBlocks), `GetCubeBlock` null / `FatBlock` null (armour) / cells that throw, `Merge.CubeGrid`, drone blocks
  (`DPb`, `DMerge`, `DBats`) that left the grid terminal system after the release, `Nearest()` over a removed merge block, `GetInventory` null,
  `Me.GetSurface(0)` null (`SurfaceCount` checked), `Me.CustomData`, IGC dictionaries, per-thruster / per-turret / per-battery accesses.
* Every bay steps inside its own try/catch (`BayFail`): the bay goes to FAULT with `EXCEPTION <type>: <msg> (at <where>)`, the other bays keep
  printing, and the FAULT bay retries after `Fault Retry Seconds`. The other Tick sections (IGC read, expiry, home broadcast, relay, joins, display,
  GetBlocks) are each guarded too. The constructor sets `UpdateFrequency` first and survives a failing LoadConfig/GetBlocks. `GetBlocks` builds the new
  bay list aside, so a failure half way keeps the old one. Bays whose projector or merge block is gone are FAULTed and the list is rebuilt.
* Breadcrumbs: `_where` is set before each risky section ("bay 2 BUILD projector read", "bay 1 ARM write drone Custom Data", ...). `Err()` keeps the last 3
  distinct errors with the time of the first occurrence and a repeat count: `<Type>: <msg> | at: <_where> | bay N state S | in <script method from the
  stack trace>`. The terminal info (Echo, now every run so it never goes blank) and LCD 0 show `!! n ERRORS` in the header and the error block at the
  bottom; `reload` clears the log.
* Real bug found by the new sim test (v2.1 logic): with `[Stock]` cargo empty the stall detector sets `Starved`, which held back `t` for ever; the
  no-box fallback finish timer (`Finish Fallback Seconds`) used the same `t`, so FINISH never ended. It now uses the real time since FINISH began.
* `sim/printer_test.sh` (`SimHost.exe printer-test`): the real script on the real 'Mobile Hive' blueprint for 320 s, plain and with stand-in projectors /
  welders that throw NullReferenceException (no projection, stats not ready, projection lost, projector stuck off, IsProjecting throwing, welder removed).
  With `--script-file` it runs old scripts from git: the v2.0 and v2.1 scripts print exactly the in-game text "ERROR Object reference not set to an instance of an object" every tick (throw site `Program.StepBay`, `b.Proj.RemainingBlocks`); v2.2 keeps all four bays ticking and shows the error with its breadcrumb. The stand-ins are HYPOTHESES (nobody has seen the
  real API throw there); the sim also never places projected blocks, so the drone box never fills (the test uses `Skip Finish Check=true`) and no
  physical drone exists for ARM/RELEASE (the DRONE side of the release is covered by `sim/dock_launch_test.sh`).
* Reading the in-game error: the first line is `Type: message`, then `at:` the section, then bay/state, then the script method. Send that line.

## Diagnosis (checked in the code)
* `RemainingBlocks` counts projected blocks not yet placed. v2.0 left BUILD at 0 and switched the welders off in CHARGE, so blocks that were
  placed as skeletons (little integrity) stayed unfinished. Confirmed by reading the old code; it is the most likely cause of "welders turn
  off early". The old stall "kick" also turned ALL welders off for 2 s, and `Power(b, false)` was reachable from any state.
* "Out of reach" is a geometry problem (see reach.py, owned elsewhere); the script can only report it: it lists the unfinished block cells with
  the distance (centre to centre) to the nearest welder.

## New cycle
`IDLE -> BUILD -> FINISH -> CHARGE -> ARM -> RELEASE -> COOLDOWN` (`DOCKED` after CHARGE in rack mode, `FAULT` from anywhere).

### FINISH
* Welders stay on. `Power(b,false)` is ignored in BUILD and FINISH; only `Fault()` (after the state changed) and later states switch them off.
* Done when a complete pass over the bay's cell box found: no non-null slim block (excluding `Ignore Cells`, the printer merge block) that is
  not `IsFullIntegrity` or whose FatBlock is not `IsFunctional`; unique blocks found >= `Drone Cell Count` - `Drone Cell Tolerance` (default 2);
  and the pass started while `RemainingBlocks == 0`. Also `RemainingBlocks <= 0` at the end.
* Bay keys (Custom Data, `[Bay N]`, written by the blueprint tooling, coordinates are the printer grid's cell coordinates):
  `Drone Box Min=x,y,z`, `Drone Box Max=x,y,z`, `Drone Cell Count=n` (unique drone blocks, as printer_lib writes it; compared with the unique blocks found, which also satisfies a block count
  because cells >= blocks), `Drone Cell Tolerance`, `Ignore Cells=x,y,z;x,y,z`, `Skip Finish Check=false`, `Launch Burst Seconds`.
  Box > 200000 cells is rejected with a warning. Scan budget 150 cells per Update10 tick per bay.
* Without box keys: terminal blocks starting with the bay prefix must all be functional + full integrity, and `Finish Fallback Seconds` (90) must
  have elapsed; the LCD warns that armour is not verified.
* Stall (no progress for `Stall Warning Seconds`): per-welder report (off / damaged / unpowered), `[Stock]` empty (timeouts paused, message to refill),
  or "projection done but blocks not finishing" with the unfinished list. ONE welder at a time is switched off 2 s and back on, at most once per
  StallSeconds. Progress = fewer unfinished blocks, more integrity, or more blocks in the box.
* `Finish Timeout Seconds` (360) -> FAULT with the counts and the unfinished list. Faulted bays retry; the next BUILD continues welding the
  half-built drone (rem==0 for 15 s is accepted as "already placed").
* Keep Projection is forced on (property `KeepProjection`, in a try/catch because the property id is unverified) every 10 s.

### Take-off
See hive-protocol.md section 9. FUSED false positives: the check now waits `Fused Grace Seconds` (8) re-asserting merge off, needs both located
drone blocks to be on the printer grid, and only then FAULTs (and holds the bay).

## Manual tests (in game)
1. Foundry, creative or survival with [Stock]: watch the LCD. After BUILD shows 0 left it must say FINISH with a scan %, and the welders keep
   working until `0 unfinished`. Then CHARGE, ARM, RELEASE.
2. Break it on purpose: switch one welder off by hand in FINISH; it must be switched on again within one tick and the LCD stall text must name it.
3. Empty [Stock] during FINISH: the LCD says refill; refill; it continues, no timeout fault.
4. Remove the Drone Box keys: LCD shows the "no Drone Box keys" warning and finish waits 90 s.
5. After release the drone's PB Custom Data contains `[Launch]` with `Armed=true` (check on the drone before separation with `release 1`), and the
   drone leaves without any other input. Disable the drone PB compile (Recompile Drone PB = false) and check that JOIN/FLEET join still start it.

## Not verified / not testable in the sim
* sim projector does not model placed-vs-welded blocks (`SimProjector` only counts down), so the FINISH logic has no sim scenario.
* `IMyProjector.BuildableBlocksCount` semantics, `KeepProjection` property id, that GetCubeBlock on the merged printer grid returns the drone's
  skeleton blocks as non-full integrity, and that a merged drone really is on the printer grid (assumed, as in v2.0).
