# Basilisk Split: BasiliskAI over a Flight PB and a Staff PB

Status: **implemented** (2026-10-14): `BasiliskAI.cs` is the Flight half (82,091 minified), `BasiliskStaffAI.cs` the Staff half (23,038, no LAUNCH). Done as planned except that the other fleets' target feeds and the sensor setup were NOT moved to the Staff (the Flight stays at 82.1k instead of ~80.3k, under the 85k hard cap; they are the next cut if the Flight grows). `FollowTest.cs` now reads `Wingmate Separation Range` from every PB of the drone (the Staff is `Pb`, the key is in the Flight's Custom Data). Sim, 1v1/Basilisk-vs-Hornet 10 seeds: bit-identical to the single-PB ship (3 wins / 5 losses / 2 timeouts, same shots and hits per seed); Basilisk-vs-Basilisk 5 seeds no errors; `killpb Staff` leaves the Flight fighting unchanged, `killpb Flight` gives `FLIGHT PB DOWN` and the ship coasts; follow-test 3 drones, 4 seeds, cruise 0 fails (as before), foot fails 1 0 1 2 (as before). Not run in the game. Release zips not rebuilt. Pattern: `docs/design/wasp_split.md` and the drone splits in a27fd45 / 51f75cd (Aegis, Mantis, Longbow, Hornet,
Harrier, Lancer). Rules: CLAUDE.md "Script size". Blueprints: Basilisk and Basilisk Seeker (`tools/designs/extra.py: basilisk()`). Lite is retired, so it is not built or tested.

## 0. Numbers (measured on a scratch copy of the current tree with `tools/minify_cs.py`; the scratch scripts compile with mcs against `stubs.cs`)
| step | BasiliskAI (Flight) | BasiliskStaffAI |
|---|---|---|
| today, single PB | 95,484 | - |
| `split_flight.py` as for the other drones (FLEET LINK, FOLLOW, status screen leave) | 83,053 | 32,784 (WaspStaffAI copy incl. LAUNCH 9,582) |
| + delete dead code: `HiveStatus`, `AiStatus` (only the old screen called them), `MatesInLine` + `MateClear` (never called) | 82,091 (-962) | |
| + other fleets' target feeds `ListenTags`/`_lFeed`/`RegisterFeeds` to the Staff | 81,630 (-461) | +~0.5 k |
| + sensor setup `ConfigureSensors`/`SetF`/`SetB`/`_sensorSetupMissing` to the Staff | **80,347** (-1,283) | +~1.6 k |
| - LAUNCH block and its hooks stripped from the Staff (below) | | **~25 k** |

80,347 is where the other Flight halves sit (WaspFlightAI 80,451; Aegis/Mantis/Longbow 78.1-78.6 k). Expect +-300 when done properly. If the final number is above
81,000, the next cut is damage sensing (`ScanSlims/StepSlimScan/CheckDamage`, 1,552 min., as Helm split did with a `.DMG` tuple); it is not taken now because it
feeds `UnderFire`/`_hitDir`/`Evade()` (combat reaction) and would need a new SPLIT LINK tuple in all seven Flight scripts.

## 1. What moves, and why each piece is off the hot path
| module | goes to | why off the hot path |
|---|---|---|
| FLEET LINK (fleet channel in/out) | Staff | target relay from other ships; shares already go at 2 Hz. `ReadTgt(_lkTgt)` keeps the Flight's input; `FleetSend` stub sends `.FLT` |
| FOLLOW / SATELLITE geometry | Staff | owner tracking and slot math, sent as a velocity on `.FOL` every 2 ticks in FOLLOW; 2-4 ticks of lag are accepted in every split |
| status screen | Staff (generic Staff screen) + compact Flight line | text only |
| other fleets' target feeds (`<TAG>_TGT`, key `Listen To IGC Tags (target feeds)`) | Staff, relayed on `.TGT` | the same kind of input as the fleet channel, +2 ticks |
| sensor setup (`ConfigureSensors`) | Staff | one-shot terminal settings at start, on a grid change and every 600 ticks; the settings stay in the sensor block, so a dead Staff changes nothing |
| dead code (`HiveStatus`, `AiStatus`, `MatesInLine`) | deleted | no caller after the split (MatesInLine had none before) |

**Stays in Flight (hot path, one PB):** tracking, cameras/raycasts, `PollSensors` (reads detections), designators, LEAD, rocket solver, aim, gyros, thrust, firing gate,
salvo control, KZ, FRIENDLY AVOIDANCE, BREACH + SEEKER (`AimInfo`, `SeekerTick`), AI GATE + AI hybrid, damage sensing + `Evade`, hive `_SWARM`, own `Tag_TGT/_CMD/_HOME`,
THRUST VECTOR LINK, `LoadConfig` (the Flight must fight alone), `Save()`/Storage.

**LAUNCH: stripped from the Staff, not accepted.** The Basilisk has no merge block and is not printable, but the inherited block is not inert: `LnReady()` returns true
when `_lnMg.Count < 2`, so a fleet `launch` (in `_flAllowed`, meant for carriers) sets `_lnForce` and starts a 4 s burn along the ship's back with dampeners off, and an
IDLE Basilisk on a fleet channel shared with printers takes off on their repeated `join`. Today's single-PB Basilisk ignores both words. So `BasiliskStaffAI.cs` carries no
LAUNCH block, and the Flight never sees `LkBurn` (FOL bit 2 is never set).

## 2. Where the PBs sit: the reserved spare becomes the Staff
`basilisk()` already places `[Spare] <name> PB 2` with `spares.add_spare_pbs` at **(-2,0,1)**, a LargeProgrammableBlock inside the existing box, seated on the core,
next to Camera Left (-2,1,1), dims unchanged (17.5 x 17.5 x 20.0 m). **Decision: re-use it** (same call, `name='[Staff] {model} PB'`, `reserved_ok=True`,
`extra=pb_extra(script_text("BasiliskStaffAI.cs"))`; the placement does not depend on the name, so it lands on the same cell). Rename the main PB at (-1,1,0) to
`[Flight] <name> PB`. Why not drop it and let `add_support_pbs` add the Staff: `free_pb_spot` searches one cell outside the hull and puts a support PB on the surface
(the `[Thrust]` PB lands at (0,0,3), the stern between the main thrusters); the spare cell was chosen for this half (most enclosed); the model function stays the
whole ship (golden hash, `test_pb_count`); the PB count is unchanged: Flight + Staff + `[Thrust] <name> PB` from `build_all` = 3. **`add_support_pbs` is not called in
`basilisk()`**: `build_all.build_one` adds the `[Thrust]` PB and skips the Staff because a `[Staff]` PB is already there.

`shrike_basilisk_test` takes the first block whose name ends in `' PB'` from `X.basilisk()` and puts ShrikeAI in it. A `[Staff]` PB on its construct would make the Helm
switch HELMLINK on against a BasiliskStaffAI. So `basilisk(lite=False, seeker=False, staff=True)` gets a `staff` flag; `shrike_basilisk_test` calls `X.basilisk(lite=False,
staff=False)`, which builds today's ship exactly (main PB `<name> PB`, blank `[Spare] Basilisk PB 2`): Shrike Basilisk Test stays byte-identical (checked by hash, s7).

## 3. IGC link: the SPLIT LINK block v1.0 unchanged (`tools/lk_block.cs`, `split_sync.py`)
All `TransmissionDistance.CurrentConstruct`, key `WSPL<Flight PB EntityId>`, both PBs Update1. No new tuple: feeds ride on `.TGT`, sensors need none.
| tag | from -> to | payload | rate |
|---|---|---|---|
| `.F2S` | Flight -> Staff | `MyTuple<int flags, long tgtId, Vector3D tgtPos, Vector3D(aimErr, fireWin, aBrake), float batt, string gateWhy>`; flags bits 0-2 mode, 3 firing, 4 under fire, 5 lock stale, 6 AI on, 8-15 rockets (`LkAmmo() = _rockets`, 255 = unknown) | every 12 ticks + on mode change |
| `.FOL` | Staff -> Flight | `MyTuple<Vector3D want, ownerP, ownerV, dynHome, (engage, leash, 0), int flags>` (bit 0 owner, 1 on foot, 3 dynHome fresh; bit 2 burn never set) | 2 ticks in FOLLOW, else 30 + on change |
| `.S2F` | Staff -> Flight | `string` verb into `Command()` (a mode verb equal to the current mode is skipped) | on event |
| `.CMD` | Flight -> Staff | `string` PB argument given to Flight | on event |
| `.TGT` | Staff -> Flight | `MyTuple<long, Vector3D, Vector3D, double, long>` from the fleet channel **and the other fleets' `<TAG>_TGT` feeds**, coalesced per id, <= 10 a tick, only while F2S < 90 ticks old | on arrival |
| `.FLT` | Flight -> Staff | the same tuple: own shares and "finished" (-1) for the fleet channel | on event |
`LkGate() = _gateWhy` (the Staff shows the hold reason, as the Wasp). The Flight's own `BASILISK_TGT/_CMD/_HOME/_SWARM` listeners stay in the Flight.

**Start-up handshake.** Flight `LkInit()` registers `.FOL/.S2F/.TGT` under its own id in the constructor. Staff `GetBlocks()` finds the nearest `[Flight]` PB on its grid,
keys `WSPL<id>`, registers `.F2S/.FLT/.CMD`, sends `.FOL` within 30 ticks; the Flight counts the Staff alive while FOL < 1 s; the Staff counts the Flight once F2S arrived.
**Re-bind.** Flight rebuilt = new EntityId: Staff sees F2S silent (`_down`), re-runs `GetBlocks()` at 1 Hz, re-keys. Staff rebuilt: the Flight's key does not change, the
new Staff finds it. Both re-scan every 600 ticks and on a grid change. **Resent after a run gap.** FOL and F2S are periodic states; the Flight's `dt > 0.5` branch (from
`split_flight.py`) flips `_aiOn` and `_firing` so the AI-block and gun state is written again after the Staff watchdog may have switched them off; sensor setup is
re-applied by the Staff every 600 ticks; feeds/fleet targets older than the gap are dropped by `ReadTgt` when own data is fresher.

**Failure behaviour (as the Wasp).** Staff silent 1 s: Flight fights on alone (own tags, hive, KZ, AI hybrid), FOLLOW flown round the last owner point, engage radius
800; no fleet channel, no other fleets' feeds; sensors keep their last settings. Flight silent 90 ticks or `!IsWorking`: Staff releases gyros/thrusters, dampeners on,
AI blocks and fixed guns off once, repeats the release at 1 Hz, screen `FLIGHT PB DOWN`.

## 4. One writer per override
Gyro override/rates, thruster overrides, dampeners, launcher fire, AI-block enable: **Flight only** (`Control/Aim/ApplyThrust/SetFire/Release/ReleaseGyros/AiStep`, and
`Release()` in its error path). Staff: only in `Watch()`, only while the Flight is silent or not working (time-exclusive); no LAUNCH, so no take-off burn writer. Thrust
PB: never writes (sends an attitude to the Flight). Sensor terminal settings: **Staff only** (the Flight loses `ConfigureSensors`). `[Seeker]` turret targeting group: Flight.

## 5. Edits per file
**`tools/split_flight.py`** (one-off generator, now used for the 8th script):
* `AMMO["BasiliskAI"] = "_rockets"`; new `GATE = {"BasiliskAI": "_gateWhy"}` used in the `LkGate()` line (default `""`).
* `hasL = "// >>> LAUNCH" in s`: drop LAUNCH only when present; skip the `LaunchLoad(); ` rep, the `case "armed"... LaunchCmd` rep and the `LaunchStep` rep when absent
  (tried on scratch: every other rep hits exactly as asserted).
* `EXTRA = {"BasiliskAI": basilisk_cuts}` run last: delete `string HiveStatus()` (to `class Mate`), `string AiStatus()`, the MatesInLine comment + `MateClear` +
  `MatesInLine`; delete `ListenTags`, `_lFeed`, `RegisterFeeds()` and its constructor call, the `foreach (var fl in _lFeed) ReadTgt(fl);` line and the
  `ListenTags = Cfg(S, "Listen To IGC Tags (target feeds)", ...)` line; delete `_sensorSetupMissing`, `ConfigureSensors`, `SetF`, `SetB` and the `ConfigureSensors();`
  call in `GetBlocks()`. Each by `rep(..., cnt=1)` so a miss asserts. Run `python3 -I tools/split_flight.py BasiliskAI`.
**`scripts/BasiliskAI.cs`**: generated by the above; then by hand: header comment (two PBs, `[Flight]` name, what moved, Staff reads the feeds key), `extra.py`
comment. VER stays as the other splits did.
**`tools/make_staff.py`**: option `nolaunch` (strip `// >>> LAUNCH`..`<<<`, `LaunchLoad(); ` in `LoadMods`, `bool burn = LaunchStep(...)` -> `bool burn = false;`, the
`case "armed": ... LaunchCmd` line, `|| _lnS == 2` in `Watch`, `_sb.Append(LaunchStatus());`, the LAUNCH header lines) and a `PATCH["Basilisk"]`:
* keep the `"%  Rockets "` label (skip the Ammo substitution: LkAmmo is a rocket count); header says large grid, lists feeds and sensor setup;
* `double MaxSpeed = 60, SepRange = 160;` (Basilisk defaults; the Wasp's 98/70 would shape FOLLOW slots until the Flight's Custom Data has the keys);
* `LoadFlightCfg()` also reads `Target Large Grids`, `Target Small Grids`, `Target Characters`, `Target Neutral/Unowned`, `Listen To IGC Tags (target feeds)` from
  `[Basilisk]` of the Flight PB (default list `HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER`); pasted configs keep working;
* `Listen()`: key `Tag|_fk|ListenTags`; disable the old feed listeners, register `<T>_TGT` for every tag != Tag into `_lFeed`;
* `Relay()`: drain `FleetTgtL` and `_lFeed` through the same filter/coalesce code (a small `Drain(l)` helper);
* `GetBlocks()`: `ConfigureSensors()` at the end (verbatim from today's BasiliskAI, with `FoFoot` / `TargetChars || FoFoot`, which the Staff has);
* `Display()`: `! Sensor: set ranges to max + Detect Enemy` when `_sensorSetupMissing > 0`.
Run `python3 -I tools/make_staff.py BasiliskAI Basilisk BASILISK Basilisk "" nolaunch` -> `scripts/BasiliskStaffAI.cs` (header `BASILISK STAFF AI v1.0-basilisk-split`).
**Sync lists:** `fleet_sync.py` and `follow_sync.py` SCRIPTS: `BasiliskAI` -> `BasiliskStaffAI`. `launch_sync.py`: drop `BasiliskAI` from SCRIPTS; `EXCLUDE =
["BasiliskStaffAI"]` with the comment "no merge block, not printable: the Staff carries no LAUNCH (fleet 'launch'/'join' would start a burn)". `split_sync.py`
SCRIPTS += `BasiliskAI`. Unchanged (the blocks stay in Flight): `aim_sync`, `kz_sync`, `lead_sync`, `ai_gate_sync`, `thrust_sync`, `blocks.json` FRIENDLY AVOIDANCE.
**`tools/designs/_common.py`**: `SUPPORT_STAFF['BASILISK'] = 'BasiliskStaffAI.cs'` (add_support_pbs asserts on a Flight without a known Staff). No `SCRIPT_LIMITS` entry.
**`tools/designs/extra.py` `basilisk(lite=False, seeker=False, staff=True)`**: main PB `f"[Flight] {name} PB" if staff else f"{name} PB"`; if `staff`:
`spares.add_spare_pbs(bp, total=spares.pb_count(bp) + 1, name='[Staff] {model} PB', model=name, reserved_ok=True, extra=pb_extra(script_text("BasiliskStaffAI.cs")))`,
else today's call. Docstring + comment. `basilisk_seeker` unchanged (`staff` defaults True).
**`tools/designs/shrike_test.py`**: `X.basilisk(lite=False, staff=False)`; nothing else.
**`tools/build_all.py`** text: Basilisk "two PBs (`[Flight]` BasiliskAI, `[Staff]` BasiliskStaffAI) + `[Thrust]`"; Basilisk Seeker "the `Seek:` line is on the `[Flight]`
PB's screen". **`tools/make_seeker_pack.py`**: SCRIPTS += `BasiliskStaffAI.cs` (list edit only, not run).
**Sim harness: no code change.** The sim runs every PB with a program; an untagged command reaches every PB (Scenario.cs:166), the Staff forwards it and the Flight
skips a repeated mode verb; `--custom-data` goes to every PB (the Staff ignores `[Basilisk]` except the keys above); `follow-test` drives the `[Staff]` PB
(FollowTest.cs:145); `Metrics.ScriptHasTarget` reads the Staff's `Target :` line (the compact Flight line has `tgt`); `pbOk` is true while any PB lives.
`sim/build.sh` compiles `BasiliskStaffAI.cs` with all scripts. M3 uses Shrike Basilisk Test (unchanged); M4 has no Basilisk rig.
**Tests:** new `tools/tests/test_basilisk_split.py`: (a) Basilisk and Basilisk Seeker model functions: one `[Flight] <name> PB` whose program has `LkInit(`, one
`[Staff] <name> PB` at (-2,0,1) carrying BasiliskStaffAI, no `[Spare]`, dims (17.5, 17.5, 20.0); after `C.add_support_pbs` exactly Flight, Staff, Thrust. (b) Shrike
Basilisk Test: no `[Staff]`, one blank `[Spare] Basilisk PB 2`, ShrikeAI in `[Helm]`, bp.sbc sha256 = the value recorded in s7 step 0. (c) BasiliskAI has none of
`FleetLoad(`, `FollowStep(`, `RegisterFeeds(`, `ConfigureSensors(`, `HiveStatus(`, `LaunchStep(`; its `Display()` calls `AimInfo()`; `LkAmmo` returns `_rockets`.
(d) BasiliskStaffAI has no `// >>> LAUNCH`, `LaunchStep(`, `_lnS`; has `ConfigureSensors(`; every `ini.Get("Basilisk", "<key>")` key is a literal in BasiliskAI.cs
except `Listen To IGC Tags (target feeds)` (documented). `test_pb_count.py`: drop Basilisk/Seeker from `test_reserved_spares` (Gnat stays), docstring.
**Golden hash:** `python3 -I tools/tests/run_all.py --update-golden` rewrites all eight; `git diff tools/tests/golden_hashes.json` must change only `Basilisk` and
`Basilisk Seeker` (anything else = another session's change: stop and ask).
**Docs:** `docs/design/pb_headroom.md` (Basilisk to Done), HANDOFF.md thrust-vectoring section (Basilisk in the split list), this file's Status line + measured sizes.

## 6. One-writer and finder notes for other PBs
HiveMotherAI/CradleBayAI: the Basilisk is never in a bay (no merge block); `[Staff] Basilisk PB` / `[Flight] Basilisk PB` match no bay Prefix; the main session's
finder fix (accept `[Staff] <Prefix>` / `[Flight] <Prefix>`) does not change that. ShrikeAI/StaffAI look for `[Staff]` on their own construct only.

## 7. Steps and validation
0. Baseline on the current tree: `python3 -I tools/build_all.py --no-zip --only "Basilisk" "Basilisk Seeker" "Shrike Basilisk Test"`; `bash sim/build.sh`; record the
   Shrike Basilisk Test bp.sbc sha256; run the baseline sims of step 6.
1. split_flight.py + run; 2. make_staff.py + run; measure `python3 -I tools/minify_cs.py scripts/BasiliskAI.cs scripts/BasiliskStaffAI.cs` (expect ~80.3 k / ~25 k).
3. Sync lists; `python3 -I tools/blocks_sync.py --check` (also runs the legacy fleet/follow/launch/aim/kz/lead/ai_gate checks) and
   `python3 -I tools/split_sync.py --check`, `python3 -I tools/thrust_sync.py --check` (these two are not in the legacy list).
4. Designs, `_common.py`, tests, golden; `python3 -I tools/tests/run_all.py` green.
5. Rebuild the three blueprints (`--no-zip`) and `bash sim/build.sh` (mono 6.8 / mcs is installed; the scratch Flight and Staff compiled).
6. Sims, regression only, deterministic seeds, before vs after (CLAUDE.md: few seeds, say why):
   * `mono SimHost.exe run --filter "^1v1/Basilisk-vs-Hornet$" --seeds 10`: wins/losses/timeouts and shots within noise of the baseline (catches a broken cut).
   * `run --filter "^1v1/Basilisk-vs-Basilisk$" --seeds 5`: both sides split, no stuck/no-fire seeds.
   * faults, 3 seeds each on Basilisk-vs-Hornet: `--pilot "20:killpb Staff"` (Flight keeps fighting, no override release) and `--pilot "20:killpb Flight"` (Staff
     `FLIGHT PB DOWN`, overrides released within 1.5 s; check with `--echo`).
   * `follow-test --model Basilisk --count 3 --path cruise` and `--path foot`, seed 1 each, numbers within ~1 m of the baseline (FOLLOW now crosses the link).
   * 20+ seeds only if a direction looks off. No full matrix.
7. Docs; say in the reply that the release zips are not rebuilt. Not run in the game.

## 8. Risks / not verifiable here
* Real-game IGC latency on CurrentConstruct (FOLLOW lag 2-4 ticks), as every split; owner-tested only for the Wasp.
* Feeds through the Staff: +2 ticks and <= 10 relayed ids a tick; with many fleets the queue coalesces per id, so only stale duplicates drop.
* A Flight that never had a Staff (hand-built) keeps the sensor block's own settings; the blueprint sensor has game defaults (smaller range).
* Status detail lost from the PB screen (lead, power cap, camera/gyro counts, instruction line, KZ and AI-gate lines, READY shown as GUARD): accepted as in the
  other splits. The Seeker's `Seek:` text stays on the Flight line (only with a target, as today).
* Old pasted Custom Data: `[Fleet]`/`[Follow]` on the Flight PB are ignored, the Staff writes its own (same as the Wasp; the shipped blueprint has neither).
* Concurrent sessions edit the same sync lists, `_common.py` and `golden_hashes.json`: pull before editing, re-run the generators on a fresh BasiliskAI.cs if it changed.
* `split_flight.py` asserts on exact text; a BasiliskAI edit landing first may need the reps adjusted (it fails loudly, never silently).
