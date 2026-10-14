# Gnat Split: GnatSwarmAI.cs over two Programmable Blocks (Flight + Staff)

Status: **implemented** (2026-10-14): `GnatSwarmAI.cs` 1.14-gnat-split is the Flight half (76,374 minified), new `GnatStaffAI.cs` the Staff half (22,386); `split_flight.py` / `make_staff.py` have a Gnat path; the Staff PB sits on the tail where the reserved spare was (the 2 x 2 hull rework c94b316 moved it to (0,0,3)). Not done from this plan: the C# `selftest gsplit` (the checks were run by hand instead). Sim, same seeds before / after: `1v1/Gnat-vs-Hornet` 10 seeds bit-identical (0 wins / 5 losses / 5 timeouts), `swarm/6xGnat-vs-Hornet` 5 seeds same outcomes (3 wins / 2 timeouts), `dock_launch_test.sh Gnat` all 10 cases pass, `printer-test --fault none` OK (the `ARM: drone PB not visible` line is the stand-in printer, the same as before), `killpb Staff` leaves the Flight fighting unchanged, `killpb Flight` gives `FLIGHT PB DOWN`, Staff `satellite` then `form` / `hunt` then `guard` switch the Flight's ring on and off. Not run in the game. Release zips not rebuilt. Pattern: `docs/design/wasp_split.md` (Wasp Split) and the Hornet/Lancer/Harrier/Aegis/Mantis/Longbow splits
(51f75cd, a27fd45). Rules: CLAUDE.md "Script size". Release zips are not rebuilt by this work.

## 0. Numbers (measured with `tools/minify_cs.py` on HEAD c77eb80)
`scripts/GnatSwarmAI.cs` 87,228 minified (one PB). Per shared block: FLEET LINK 3,598, LAUNCH 9,582, AI GATE 3,216, LEAD 2,722, KZ 7,440, FA 2,443, BREACH+SEEKER 4,263.
`tools/lk_block.cs` (SPLIT LINK) 2,310. A throw-away prototype of the cut below (scratch copies, not in the repo) was compiled with `mcs` against `stubs.cs`
(same wrapper as `sim/build.sh`): **Flight 76,216** (keeps the full Gnat status screen), **Staff 22,349**. Expect +-300 for the final text (ParseMode tweak, thruster guard, headers).

Earlier assessment, checked:
* `tools/split_flight.py` cannot run on the Gnat: confirmed. Besides the FOLLOW hooks (FollowLoad, FoSat, FollowStep, FoEngage, FoSetSat, Mode.FOLLOW, FollowWant/FollowFar,
  FollowSensed, FoFoot, MinAccel) and the missing AMMO entry, the Gnat differs in: no `// >>> FOLLOW` block (the `drop` asserts); the target loop is
  `for (li = -1; li < _lFeed.Count + FleetTgtL.Count; ...)`, not `foreach (var fl in FleetTgtL)`; LAUNCH is called in `Tick` as `if (LaunchStep(...)) { } else if (!AiStep()) Control(dt);`,
  not `... return;` in Control; `ReadIGC` uses `_flChan` for the fleet `_HOMER` listener; the script has no `_fireWinDeg` (SPLIT LINK reads it).
* `tools/make_staff.py` assumes WaspStaffAI with FOLLOW and a `[Wasp]` section: confirmed. The "~33k with FOLLOW" option is moot: the Gnat has no FOLLOW block (its FORM / satellite
  ring is its own code, on the hot path of Control), so the Staff is ~22.4k.
* No `[Thrust]` PB: confirmed (point-and-burn, no `TvAim(`; `add_support_pbs` would not add one).
* `DockLaunch.cs:234` parses `Mode : FORM`, but tries `Mode   : ` first and picks the `[Staff]` PB (`PbRank`), so the Staff screen keeps the Wasp-Staff line `Mode   : FORM`.
  `Metrics.ScriptHasTarget` scans every PB for `Target :` or `Tgt  :`: the Staff prints `Target :`, the Flight keeps `Tgt  :`.
* `HiveMotherAI.FindDrone` and `CradleBayAI.Mine` already accept `[Staff] <Prefix>` / `[Flight] <Prefix>` and arm the Staff (commit 4d8fccd). Plan needs: PB names exactly
  `[Flight] Gnat PB` and `[Staff] Gnat PB` (bay Prefix `Gnat`). `[Spare] ...` stays invisible to both.

## 1. What moves and why it is off the hot path
| PB | script | minified | holds |
|---|---|---|---|
| `[Flight] Gnat PB` (the existing PB, renamed) | `scripts/GnatSwarmAI.cs` 1.14-gnat-split | ~76.4k | settings, AI GATE, LEAD, KZ, FA, BREACH+SEEKER, AI hybrid, tracking, sensors, cameras, hit bearing / RAID, hive (`_SWARM/_TGT/_CMD/_HOME/_HOMER/_HIT`, Listen Tags feeds), FORM / satellite ring, Control, Aim, Axial (point-and-burn), firing, its full status screen, SPLIT LINK |
| `[Staff] Gnat PB` (the reserved spare) | `scripts/GnatStaffAI.cs` 1.0-gnat-split (new) | ~22.4k | FLEET LINK, LAUNCH (take-off burn), fleet `_HOMER` relay, mirror status screen, Flight watchdog |

* **FLEET LINK**: the inter-ship fleet channel (`<ch>_TGT/_CMD/_HOME/_HOMER`). Messages arrive at most a few per second from other ships; a 2-tick relay is far below their own age. No override writes.
* **LAUNCH**: runs once per life (docked -> burn -> mode). The burn is open-loop (zero gyro rate, fixed thrust along Away); during it the Flight writes nothing (`LkBurn`). Not combat.
* Stays in Flight on purpose: everything that aims, steers, fires or decides who flies (AI GATE / AI hybrid = override arbitration), the hive channel (`_SWARM` at 10 Hz feeds separation
  and slot geometry), KZ (steers), the FORM / satellite geometry (the Gnat flies it every tick in Control; no FOLLOW block to move).
* Not moved (no need at 76.4k): the Flight status screen (~1.5k). Next candidate if the Flight grows past ~80k: replace it by the short split_flight-style screen (the Staff mirror already exists).

## 2. Where the Staff PB sits: reuse the reserved spare
`core.gnat()` already adds one blank `[Spare] Gnat PB 2` at cell (1,-1,-1), fwd Backward, up Up (most enclosed free cell, inside the existing box). **Turn it into the Staff**: same call,
`name='[Staff] {model} PB'`, `reserved_ok=True`, `extra=pb_extra(script_text("GnatStaffAI.cs"))`, and rename the frame's `Gnat PB` (2,0,-1) to `[Flight] Gnat PB`. Why not drop the spare
and let `add_support_pbs` add it:
* `spares.add_spare_pbs` guarantees the blueprint box never grows (asserted); `free_pb_spot` searches one cell outside the box. The printers (`printers.py` DRONES `'Gnat'` -> `core.gnat()`,
  Hive Mother / Twin / Quad / Mobile Hive / Static Nest / Fleet Forge) size their bays, Drone Box and Drone Cell Count from the projected grid. Reusing the spare keeps the PB count (2),
  cell count, dims and bay geometry exactly as now; only a PB name and program change.
* Same cell, same block count: `test_spares_do_not_grow_the_blueprint` and the printer layouts stay valid.
* Do it in `gnat()`, not `gnat_frame()` (shared with the Stinger fallback in `printers._stinger_fallback` and Midge Lite). Do not call `C.add_support_pbs` in `gnat()`
  (it would be a no-op: a `[Staff]` exists and there is no `TvAim(`).
* Still add `SUPPORT_STAFF['GNAT'] = 'GnatStaffAI.cs'` in `_common.py`: any other blueprint that embeds GnatSwarmAI through `gnat_frame` (Midge Lite, retired) then gets a Staff from
  `add_support_pbs` in build_all instead of tripping its assert.

## 3. IGC link (SPLIT LINK v1.0 unchanged; key `WSPL<Flight PB EntityId>`; every message `TransmissionDistance.CurrentConstruct`; both PBs Update1)
| tag | from -> to | payload | rate |
|---|---|---|---|
| `.F2S` | Flight -> Staff | `MyTuple<int flags, long tgtId, Vector3D tgtPos, Vector3D(aimErr, fireWin, aBrake), float batt, string gate>`; flags bits 0-2 Gnat mode (IDLE 0, FORM 1, ATTACK 2, RTB 3), 3 firing, 4 under fire, 5 lock stale, 6 AI on, 8-15 = 255 (no ammo count: `LkAmmo()` = -1) | every 12 ticks + on mode change (heartbeat) |
| `.FOL` | Staff -> Flight | `MyTuple<Vector3D want, ownerP, ownerV, fleetHome, radii(800,1200,0), int flags>`; want/owner zero (no FOLLOW, flag bit 0 never set); bit 2 take-off burn; **bit 3 only on the FOL right after a new fleet `_HOME`** | 30 ticks, 2 ticks during the burn, at once on a burn change or a new fleet home |
| `.S2F` | Staff -> Flight | `string` verb into `Command()` (Staff PB argument, fleet `_CMD` after Allowed Commands, LAUNCH's `home` + mode) | on event |
| `.CMD` | Flight -> Staff | `string` PB argument given to the Flight (Staff handles `armed/launch/disarm/reload`) | on event |
| `.TGT` | Staff -> Flight | fleet target tuple `MyTuple<long, Vector3D, Vector3D, double, long>`, coalesced per id, <= 10 per tick; radius -1 "finished" passes | on arrival |
| `.FLT` | Flight -> Staff | the same tuple: own shares and "finished" (`FleetSend` stub of SPLIT LINK); Staff sends them on its fleet tags | on event |
| `.HMR` (**new, Gnat only, outside SPLIT LINK**) | Staff -> Flight | `double` carrier radius from `<fleet ch>_HOMER` | on arrival + every 60 ticks while < 5 s old |

Gnat-specific deviations from the Wasp Staff, both for correctness:
* FOL bit 3 is an **event**, not a 5 s state: the Flight's `ReadIGC` estimates the carrier velocity from successive home fixes (`_homeV` from `_dynHome/_dynHomeT`, used by FORM). The Wasp
  Staff resends the same point every 30 ticks with `_dynHomeT = _now`, which would read as a stopped carrier. The Flight keeps its own `Tag_HOME` listener (so own-tag homes never pass the Staff);
  only fleet-channel homes come through FOL.
* `.HMR`: the old Flight followed the fleet channel's `_HOMER` (`_lHomeR2Key != _flChan`); `_flChan` now lives in the Staff. The Flight registers `_lkK + ".HMR"` once, next to `LkInit()`,
  and drains it where the `_lHomeR2` loop was. `<Tag>_HOMER` stays a Flight listener.

## 4. Sync
* **Start-up handshake**: Flight registers its listeners in the constructor (`LkInit()`, `.HMR`); `LkAlive` is false until the first `.FOL` (Staff sends one at most 30 ticks after start).
  Staff finds the nearest `[Flight]` PB on its own grid (`GetBlocks`: start, every 600 ticks, on grid change), builds `_fk`, registers `.F2S/.FLT/.CMD`; screen
  "Flight : waiting for the first message" until the first `.F2S`; the watchdog arms only after one `.F2S` (`_f2sN > 0`).
* **Re-bind when the other PB is rebuilt**: a rebuilt Flight has a new EntityId -> Staff sees `.F2S` silence, goes `_down`, re-runs `GetBlocks` at 1 Hz and re-keys. A rebuilt Staff needs nothing
  (the key is the Flight's id). A merged printed copy shares a construct; the key holds the Flight id and the Staff takes only a `[Flight]` PB on **its own grid**.
* **State resent after a run gap**: F2S is a 12-tick state, FOL a 30-tick state (burn bit), HMR 1 Hz. Fleet home: carriers resend `_HOME` periodically; a missed event is replaced by the
  next. Flight gap (`dt > 0.5`, from split_flight): `_aiOn = !_aiOn; _firing = false` so the next pass re-sends AI-block and gun state the Staff watchdog may have switched off.
* **Commands**: as Wasp Split. A PB argument on either PB works; the Staff forwards every word, the Flight forwards its argument. `LkRead` drops a forwarded mode verb equal to the current
  mode; the Gnat's FORM sub-states need one change (see 7, ParseMode), otherwise a Staff-forwarded or fleet `form` would be dropped while the Gnat flies its satellite ring.

## 5. Failure behaviour
* **Staff absent / dead** (FOL silent 1 s): Flight fights fully on its own hive tags (`Tag_TGT/_CMD/_HOME/_HOMER/_SWARM/_HIT`, Listen Tags). Lost: fleet channel (targets, home, commands,
  fleet `_HOMER`), automatic take-off from a printer (a docked Gnat stays IDLE; `launch` on the Flight does nothing), the Staff screen. Flight screen line `Staff: none`.
* **Flight dead** (no F2S for 90 ticks after one, or `!IsWorking`): Staff releases gyro/thruster overrides, dampeners on, `[AI]` blocks and guns off once, repeats the release each second,
  never during its own burn; screen `FLIGHT PB DOWN`.

## 6. One writer per override
* Gyro and thruster overrides: **Flight** (`Control` -> `Aim`/`Axial`, `Release`, `AiStep` hand-over, `GetBlocks` zeroing off-axis thrusters) whenever not `LkBurn`; **Staff** only in `LnBurn`
  (the Flight skips `AiStep` and `Control` while `LkBurn`) and in `Watch` (only while the Flight is silent / not working, never during the burn). Time-exclusive.
* Edit for strictness: `GetBlocks` off-axis loop becomes `else if (!LkBurn) t.ThrustOverridePercentage = 0;` (the merge opening triggers `CheckGrid -> GetBlocks` right at burn start;
  the Gnat frame has no off-axis thruster, so this is a guard, not a behaviour change).
* Known 1-2 tick overlap (same as Wasp Split): a forced `launch` while the Flight is not IDLE; the Flight lets go on the next FOL. A docked Gnat is IDLE (`Control` returns at once).
* AI blocks `Enabled`: Flight (`FindAi`, `AiStep`); Staff only in `Watch`. Guns: Flight `SetFire`; Staff only in `Watch`.

## 7. Concrete edits
**`scripts/GnatSwarmAI.cs` (Flight)** - by a new `gnat()` path in `tools/split_flight.py` (asserting edits, like the others), then by hand for the comments:
1. drop `// >>> FLEET LINK` and `// >>> LAUNCH`; insert `tools/lk_block.cs` before `public Program()` plus `int LkAmmo() { return -1; }`, `string LkGate() { return ""; }`,
   `double _fireWinDeg;`, `IMyBroadcastListener _lkHmr;`.
2. constructor and `reload`: `FleetLoad(true); LaunchLoad(); KzLoad(); AiGateLoad();` -> `KzLoad(); AiGateLoad();` (2 hits); drop `case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;`.
3. listeners: `LkInit(); _lkHmr = IGC.RegisterBroadcastListener(_lkK + ".HMR");` before `_lHome = ...`.
4. `ReadIGC`: target loop `li < _lFeed.Count + FleetTgtL.Count` -> `li <= _lFeed.Count`, `FleetTgtL[li - _lFeed.Count]` -> `_lkTgt`; remove `FleetPump(...)`; replace the `_lHomeR2Key/_lHomeR2`
   block by the `_lkHmr` drain; delete the `_lHomeR2` field.
5. `Tick`: `ReadIGC();` -> `ReadIGC(); LkRead();`; the two LaunchStep/Control lines -> `if (!LkBurn && !AiStep()) Control(dt);` + `LkSend();`; the run-gap `dt` line as split_flight;
   PB argument -> `Command(arg)` + `.CMD` to the Staff (split_flight text).
6. `Control`: `_fireWinDeg = 0;` with `_aimErrDeg = 0;` and `_fireWinDeg = win;` before `fire = inRange && ...`.
7. `ParseMode` (LkRead calls it with the sentinel `(Mode)(-1)`): with the sentinel, `FORM`/`GUARD` count as the current mode only when `!SatOn && !_ready`, `SATELLITE/SAT/ORBIT` only when
   `SatOn && !_ready`, and the sentinel call never sets `SatOn`; add `HUNT` -> ATTACK and `GUARD` -> FORM (the sim and the fleet say `hunt`, LAUNCH says `guard`). Normal calls unchanged.
8. `GetBlocks` off-axis guard (section 6). `Display`: `LaunchStatus()` -> `Staff: ok|none`. VER `1.14-gnat-split`; header: COMMANDS `armed/launch/disarm` and `[Launch]/[Fleet]` now on the Staff.

**`scripts/GnatStaffAI.cs` (new)** - by a `gnat` option in `tools/make_staff.py` (from WaspStaffAI.cs, asserting edits), header rewritten by hand:
drop FOLLOW and HIVE MATES (`ReadSwarm`, `_mates`, `_SWARM`, sensors, `PollSensors`); `Tag = "GNAT"`; `enum Mode { IDLE, FORM, ATTACK, RTB }` (`_mode = (Mode)Math.Min(f & 7, 3)`);
`LoadFlightCfg` reads `[Gnat]` `IGC Tag` and `Start Mode (FORM/ATTACK/IDLE)` only; `LoadMods() { FleetLoad(true); LaunchLoad(); }`; `Local`: `armed/launch/disarm`, `reload`;
`FleetPump` home -> `_dynHome` (for LAUNCH) **and** `_fhP/_fhT/_fhNew` (event for FOL bit 3); fleet `_HOMER` listener re-registered when `_flChan` changes + `.HMR` send; `SendFol` as in
section 3; screen `GNAT STAFF v...`, `Flight : ...`, `Mode   : <mode>`, `Target : <id> range` / `none`, `Aim err`, `Battery`, `Alarm  :` (under fire / lock stale / AI searching),
`LaunchStatus()`. Watchdog `Watch` unchanged. Keep `Tag_HOME` and `Tag_CMD` listeners (LAUNCH home, hive `launch/armed/disarm`).

**Tools**
* `tools/fleet_sync.py`, `tools/launch_sync.py` SCRIPTS: `GnatSwarmAI` -> `GnatStaffAI`. `tools/split_sync.py` SCRIPTS: + `GnatSwarmAI`. kz / ai_gate / lead / aim / `blocks.json` FA: unchanged.
  Optional: add `split_sync.py` to `blocks.json` `legacy` so `blocks_sync.py --check` covers SPLIT LINK too (today it is checked only by hand).
* `tools/split_flight.py`: dispatch `if name == "GnatSwarmAI": return gnat(...)` with the edit list above (keep the existing path untouched). `tools/make_staff.py`: `gnat()` function.
* `tools/designs/_common.py`: `SUPPORT_STAFF['GNAT'] = 'GnatStaffAI.cs'`; `SCRIPT_LIMITS["GnatSwarmAI.cs"] = 80000` with the comment "past ~80 k move a module (docs/design/gnat_split.md)".
* `tools/designs/core.py` `gnat()`: rename the frame PB to `[Flight] {name} PB`; spare call -> `name='[Staff] {model} PB', reserved_ok=True, extra=pb_extra(script_text("GnatStaffAI.cs"))`;
  comment -> this doc. `printers.py`: no edit (projects `core.gnat()`; Stinger fallback uses `gnat_frame` with StingerAI, unchanged). `spares.py`: docstring list of reserved spares.
* `tools/build_all.py`: nothing (scripts are found from the embedded programs); the release text "Midge Lite runs GnatSwarmAI.cs" stays (Lite retired).

**Tests**
* `tools/tests/test_pb_count.py`: `test_reserved_spares` drops `('core','Gnat')`; new `test_gnat_carries_flight_and_staff`: one `[Flight] Gnat PB` with GnatSwarmAI, one `[Staff] Gnat PB` with
  GnatStaffAI, no `[Spare]`, no `[Thrust]`, PB cell (1,-1,-1), dims equal to the frame's. Module docstring updated.
* New `tools/tests/test_gnat_split.py`: GnatSwarmAI has SPLIT LINK and no FLEET LINK / LAUNCH / `FleetTgtL` / `_flChan`; GnatStaffAI has FLEET LINK + LAUNCH, no FOLLOW; every `_fk + ".X"`
  sent by the Staff has a `_lkK + ".X"` listener in the Flight (incl. `.HMR`) and vice versa, same `MyTuple<...>` text; minified Flight <= 80,000; Staff screen has `Mode   : ` and `Target :`.
* Golden hash: `tools/tests/golden_hashes.json` "Gnat" changes (script text and PB names). Regenerate with `python3 -I tools/tests/run_all.py --update-golden` and check in `git diff` that only
  the "Gnat" line moved (Midge is scriptless, Hornet/Aegis/Longbow S untouched).
* Sim: no engine change needed (all PBs of a blueprint run; untagged scenario commands go to every PB; `DockLaunch.PbRank` picks the Staff; Metrics reads both screens).
  New `selftest gsplit` (`sim/src/Run/SelfTestGnatSplit.cs`, in `all`, 3 seeds each, pattern `SelfTestHelmSplit.cs`): (a) link up within 60 ticks, Staff `Mode   :` equals Flight mode;
  (b) a FLEET_TGT becomes a Flight contact in <= 4 ticks; FLEET_HOME sets the Flight `_dynHome` once per fix and `_homeV` matches a moving carrier within 10 %; FLEET_HOMER -> `_homeR`;
  (c) Staff argument `satellite` then `form` -> Flight `SatOn` true then false; untagged `hunt` -> one SETUP reset, not two; (d) `killpb Staff` at 15 s: Flight keeps firing, 0 Staff override
  writes; `killpb Flight`: Staff releases within 90 ticks + 60; `disablepb/enablepb` both ways: link back, no stale overrides; (e) no tick where both PBs wrote a gyro/thrust override.

**Docs**: `docs/design/pb_headroom.md` (Gnat -> Done), `HANDOFF.md` status, `ships`/drone doc for the Gnat if present (two PBs, Custom Data per PB), this file's status line.

## 8. Config (pasted configs keep working)
Flight Custom Data: every `[Gnat]` key, `[Kamikaze Defence]`, `[AI Gate]` (unchanged names). Staff: `[Launch]` and `[Fleet]` (written on first run); it reads IGC Tag and Start Mode from
the Flight PB's `[Gnat]`. Hive Mother writes `[Launch] Armed/Away` into the Staff (FindDrone prefers `[Staff]`) and runs `armed` on it. A player's old single-PB config pasted into the
Flight keeps all combat keys; its `[Launch]/[Fleet]` sections are ignored there (header note says to move them; no automatic migration, as in Wasp Split).

## 9. Validation (in order; each step green before the next)
1. Baseline before any edit: `bash sim/build.sh` (mono/mcs 6.8 present; prototype compiles worked), `python3 -I tools/build_all.py --no-zip --only "Gnat" "Mobile Hive"`, then
   `run --filter "^1v1/Gnat-vs-Hornet$" --seeds 10` (regression direction, 10 is enough for "no cut bug") and `run --filter "^swarm/6xGnat-vs-Hornet$" --seeds 5` (hive + 6 split
   drones; costly, 5 seeds catch a broken link). Keep the result files.
2. Edits; `python3 -I tools/minify_cs.py scripts/GnatSwarmAI.cs scripts/GnatStaffAI.cs` (expect ~76.4k / ~22.4k; Flight must be <= 80,000).
3. `python3 -I tools/blocks_sync.py --check`, `python3 -I tools/split_sync.py --check` (both green; split_sync must list GnatSwarmAI).
4. `bash sim/build.sh` (both scripts compile), `python3 -I tools/build_all.py --no-zip --only "Gnat" "Mobile Hive"`, `python3 -I tools/tests/run_all.py` (golden updated as above).
5. Sim: `selftest scenario` (Gnat-vs-Midge smoke), `selftest gsplit`; `bash sim/dock_launch_test.sh Gnat` (6 deterministic cases: plain, both merges, armed at 1 s, bad Away,
   recompile docked, recompile in the burn; each must say LAUNCH OK and end in FORM, left in about the same time as the baseline); `bash sim/printer_test.sh` plain case only
   (`printer-test --seconds 320 --fault none --expect-clean`, one run: real Hive Mother prints split Gnats, arms the Staff, drones leave).
6. Same runs as step 1, same seeds: expect win/loss/timeout within noise of the baseline (Wasp Split moved by 1-2 of 10). Only if a direction looks off: 30 seeds on that one scenario.
   Kill checks on the duel, 3 seeds each: `--pilot "15:killpb Staff"` and `--pilot "45:killpb Flight"` (team 0).
7. Docs; tell the user the zips (and every Gnat printer blueprint) are not rebuilt.

## 10. Risks
* Real-game IGC latency on CurrentConstruct is unmeasured; fleet targets and fleet home gain 1-2 ticks (they are already 0.1-1 s old). Hive (`_SWARM/_TGT/_HIT`) is not relayed.
* `_homeV` (FORM keeps up with a moving carrier) now depends on the event-only FOL bit 3; a bug there shows as a lagging formation; covered by selftest (b).
* Projected PB behaviour on printers is modelled, not verified in game: a printed Gnat needs both PBs welded before release (Drone Cell Count is unchanged, so the bay waits for both as it
  waits for the spare today).
* SPLIT LINK's FOLLOW helpers (`LkWantOk`, `LkFar`, `LkBack`, `LkEngage`, `_lkOwner`) are dead code in the Gnat (~0.4k); accepted to keep one shared block.
* ParseMode sentinel rule is Gnat-only logic next to a shared-block contract; a later SPLIT LINK change to the dedupe must re-check satellite/ready (test (c)).
* Mixed versions (old single-PB GnatSwarmAI pasted next to a GnatStaffAI): the old script never sends F2S, the Staff waits and never trips; harmless, screen says "waiting".
