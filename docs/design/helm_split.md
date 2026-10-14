# Helm Split: moving the off-hot-path modules of ShrikeAI.cs into the Staff PB

Status: plan (2026-10-14), nothing implemented. Pattern: `docs/design/wasp_split.md`. Rules: CLAUDE.md "Script size".

## 0. Problem and numbers (measured with `tools/minify_cs.py` on the current tree)
`scripts/ShrikeAI.cs` is the Helm of Shrike, Corsair, Warden, Leviathan and Ballista: **86,228** minified (165,060 raw). The gate `SCRIPT_LIMITS` in
`tools/designs/_common.py:70` was raised to 90,000 in commit 1395231, against the rule. Minified size per section (lines):

| section (lines) | min. | hot path? |
|---|---|---|
| header + settings + Track (1-177) | 4,149 | yes |
| FLEET LINK (178-269) | 3,598 | **no** (IGC in/out with other ships) |
| other fleets' feeds `ListenTags`/`RegisterFeeds` (101-107) | 351 | **no** |
| AI GATE 3,216 / LEAD 2,722 / KZ 7,440 / BATTERY 10,090 / FA 2,443 / BREACH 1,701 / AI hybrid 3,499 | | yes (aim, fire, steer, AI-block hand-over) |
| MANUAL (704-893) 8,025, of it: `ManMode/ManHud/ManShow` (869-892) 1,504, HUD surface setup in `ManBlocks` (745-750) 269, Run menu (796, 799 + keys) ~350 | | seat machine, `ManFree`, `ManFly` (FBW gyros): yes. HUD + menu: **no** |
| damage sensing `ScanSlims/StepSlimScan/CheckDamage` (2357-2419) + `_slims/_slimHp/_integ` | 2,090 | **no** (a pass already takes 4-80 ticks; also the Helm's worst instruction hog) |
| target sources 8,792, Control 9,761, target status 3,800 (rest) | | yes |

**The cut** (all four are off the hot path, none writes an override):
1. FLEET LINK + the other fleets' feeds (3,949): fleet channel `_TGT/_CMD/_HOME` in and out.
2. Cockpit HUD page and Run menu of MANUAL (~2,120). The seat machine, hand-over, FBW, dead-man re-arm and `ManMode()` **stay**.
3. Damage sensing (~2,090). `_hitAt`, `_hits`, `_hitDir`, `_hitDirT`, `UnderFire`, `Evade()` stay; they are fed from the Staff.

Gross 8.16 k, Helm-side link code +1.15 k. A crude prototype of exactly this edit on a scratch copy minifies to **79,214** (expect +-300 when done properly):
6.8 k under the 86,000 gate, 0.8 k under the 80 k budget. Not moved, on purpose: MANUAL hand-over and FBW (one writer, 0-tick hand-over), AI GATE / AI hybrid
(they decide when the AI blocks fly instead of the Helm: that is override arbitration), KZ (`KzWant` steers; shared with 15 scripts via `kz_sync.py`), BATTERY,
config parsing (the Helm must run alone). Next candidate if more room is needed later: the Helm's PB-screen text (`Display`, `BatStatus`, `AiStatus`, ~1.1 k).

## 1. Where the modules go: the existing Staff PB, not a new script
`scripts/StaffAI.cs` (23,029 min, Update10 + IGC callback) is already the Helm's partner over HELMLINK on Corsair, Warden and Leviathan: it watches `.HB`, releases
overrides when the Helm dies, owns the FLEET **send** half, the cockpit status page and its own Run menu. control_arch s1.3 already planned FLEET to move there.
Adding the three modules makes it ~29 k (estimate: relay 0.6, feeds 0.35, damage 2.1, HUD page + Helm menu 1.9, Helm Custom Data read + [Fleet] migration 0.7,
switches 0.1). A sixth PB on the large ships would duplicate the cockpit writer and the IGC plumbing. So: **every ShrikeAI ship carries a `[Staff]` PB.**

| ship | Staff today | change |
|---|---|---|
| Corsair `[Staff] Corsair` (0,3,31), Warden `[Staff][SH] Warden Staff` (1,9,24), Leviathan `[Staff][SH] Leviathan Staff` (2,13,59) | yes | script only |
| Ballista | no; `[SH] Spare PB` empty at (1,4,11) (`ballista.py:168`) | **reuse the spare**: rename `[Staff][SH] Ballista Staff`, load `StaffAI.cs`, Custom Data below. Same cell, no new block, PB count unchanged (Helm (0,4,9), Spotter (0,4,11), Staff (1,4,11), Thrust added by `add_support_pbs`) |
| Shrike (Full) | no (Helm `[Helm] Shrike` (0,2,12), TU PB, Thrust PB) | add a SmallProgrammableBlock `[Staff] Shrike` via `C.free_pb_spot(bp, 'SmallProgrammableBlock', pb['min'])` (the avionics bay; the TU loop's cells (-1,2,13), (1,3,12)... show free space); `Shrike Staff Test` already proves the pair |

Names: the Helm turns HELMLINK on when a PB whose name starts with `[Staff]` is on its construct (`HlkBlocks`, AUTO); Staff collects blocks by `[Staff] Block Prefix=[SH]`.
`spares.RESERVED_START` already lists `[staff]`. Spotter and TurretAI do not listen to anything new.

Staff Custom Data for the Ballista and Shrike: `[Ship] Tag=BALLISTA|SH`, `[Staff] Block Prefix=[SH]`, `Helm HUD Surface=0`, `Cockpit Surface=1`, and the new
`[Staff] Mothership=false` (default true): no `<Type>_HOME/_HOMER`, no fleet HOME, no turret-target relay. Without it a Staff on a ship that carries no drones
would broadcast `HORNET_HOME` and `FLEET_HOME` = itself and pull other wings to it (today's Ballista and Shrike broadcast neither).

## 2. IGC link (HELMLINK 1.2, `<Ship Tag>.<suffix>`, all `TransmissionDistance.CurrentConstruct`)
Kept from 1.1: `.HB` (Helm->Staff, long tick, 2 Hz), `.H2S` (5 Hz), `.PILOT` (on change), `.S2H` (Staff->Helm `MyTuple<string cmd, Vector3D rally, double radius>`).
Ship Tag keying (not Wasp Split's `WSPL<PB id>`): warships are never printed merged, so two Helms never share a construct; Ship Tag is what every existing PB uses.

| tag | from -> to | payload | rate |
|---|---|---|---|
| `.H2S` flags **bit 8** | Helm -> Staff | "split Helm" (HELMLINK 1.2): the Staff takes the modules over only while it sees this bit | every H2S |
| `.TGT` | Staff -> Helm | the `_TGT` tuple `MyTuple<long id, Vector3D pos, Vector3D vel, double radius, long sender>` unchanged, from the fleet channel + Listen Tags + the old `ListenTags` list (minus the Helm's own Tag); coalesced per id per Update10, <= 20 per step (Helm queue 25); radius -1 "finished" passes through | on arrival |
| `.S2H` cmd `""` | Staff -> Helm | fleet `_HOME` point in `rally` (Staff drops points < 50 m from its own ship, as `ReadIGC:1649` does); the Helm sets `_dynHome/_dynHomeT` | on arrival + 1 Hz while < 5 s old |
| `.S2H` cmd verb | Staff -> Helm | fleet `_CMD` after the FLEET block's `Allowed Commands` filter, sent with `Fwd()` straight to the Helm, **never** through the Staff's own `Command()` (fleet "launch" must not start a bay launch) | on arrival |
| `.FLT` | Helm -> Staff | the Helm's own shares and "finished" tuples (`FleetSend` becomes this one-line stub; the KZ block calls `FleetSend` too); the Staff sends them to `_flOut` | on event |
| `.DMG` | Staff -> Helm | `MyTuple<int hits, Vector3D hitDir, float hitDirAge>` (age in s, so the two clocks never mix) | on change + 1 Hz (also the Staff's liveness beat) |
| `.HUD` | Helm -> Staff | `MyTuple<int flags, Vector3D tgt(dist, speed, seenAge), Vector3D lead(leadT, pipR, pipU), Vector3D win(aimErr, fireWin, has), Vector3D pwr(batt, ammo, thrust), string mode|batCls|err>`; flags: Man, hold, damp, deadman, FIRE, asLvl<<6, prim<<8, contacts<<12 | every 10 ticks while Man or a pilot sits, else 30 (as `Display` now) |
| `.MNU` | Helm -> Staff | `int 0`: a Run of the Helm PB without argument | on event |

Receivers: Helm drains `.S2H/.TGT/.DMG` every Update1 tick in `HlkTick`. Staff registers `.HUD` and `.MNU` with `SetMessageCallback` (like `.HB`), so it writes
the cockpit HUD in the run the message arrives (lag = Helm send + 1-2 ticks, as today plus 2), and `.FLT` is drained by the callback too.

**Handshake.** Staff starts as today. On the first `.H2S` with bit 8 it switches the split on: `FleetLoad(true)` (listeners), starts the damage scan, reads the
Helm PB's Custom Data (the PB on its construct whose name starts `[Helm]`), sends `.DMG` at once and the fleet home if fresh. Without bit 8 (an old Helm) it stays
a 1.4 Staff, so mixed versions never double-handle a fleet verb ("next" twice would skip two targets). The Helm counts the link alive while `.DMG` is < 3 s old.
**Re-bind.** No addresses: both ends re-register by Ship Tag in their constructors and on `reload`; the Helm re-finds the Staff in `HlkBlocks` (every 600 ticks
and on grid change). **State resent after a gap.** `.DMG` and the home are states sent at 1 Hz; HUD is periodic; `.TGT` events older than a run gap are harmless
(`ReadTgt` keeps fresher own data). Hit counter rules: the Helm takes a hit only when `hits` grows **and** the link was already alive; otherwise it only copies
the value (a recompiled Staff restarts at 0, a recompiled Helm must not see an old count as a new hit).

## 3. Failure behaviour
* **Staff absent** (hand-built ship, `[Link] Enable=OFF`): the Helm flies and fights fully on its own `Tag_TGT/_CMD/_HOME/_AIM/_SWARM`; it has no fleet channel,
  no damage sensing (REACT still wakes on AI alarm, a held target, mates' alarm), no cockpit HUD page; Run without argument toggles `pilot` instead of the menu.
  All five shipped blueprints carry a Staff, so no shipped ship loses anything. The Helm screen says `STAFF: none (fleet, damage, HUD off)`.
* **Staff dies in flight** (`.DMG` silent 3 s after it was heard): the Helm sets `_hitAt = _now` once (a destroyed PB means hits: REACT wakes, small ships evade),
  fights on as above, shows `STAFF LOST`. Nothing is released.
* **Helm dies** (`.HB` silent 1.5 s): unchanged Staff 1.4 behaviour (`ReleaseAll`, HELM LOST on the HUD surface); relaying stops; damage scan continues.
  New for the Ballista and Shrike: they gain this watchdog.

## 4. One writer per override (every transition)
Gyro and thruster overrides are written only by the Helm: `Control`/`Aim`/`ApplyThrust` in AUTO, `ManFly` FBW in MANUAL, `ManFree`/`ManExit`/`Release` at the
hand-over; the AI blocks fly only after the Helm released (AiStep). MANUAL hand-over stays entirely inside the Helm, same tick as now. The Staff writes an override
only in `ReleaseAll` (zero/off), only while the Helm is silent, which is time-exclusive with the Helm writing. The Thrust PB never writes (it sends an attitude).
The moved modules write no gyro, thruster, dampener or gun state. The Staff writes text surfaces only: the HUD surface while the Helm is alive, HELM LOST when not.

## 5. Config (pasted configs keep working)
* Helm keeps every key. `[Manual] Run Menu`, `Run Menu Wait`, `HUD Surface`, `Seat Exclude` stay in the **Helm's** Custom Data; the Staff reads them from there
  (re-read on `reload` and every 600 ticks). The Helm no longer writes `[Fleet]` defaults; an existing `[Fleet]` section stays and is read by the Staff.
* `[Fleet]` migration: the FLEET block reads `Me.CustomData`, so the Staff copies, once per start, each `[Fleet]` key whose value in the Helm's Custom Data differs
  from the default while its own still has the default (a `Secret` set on the Helm keeps working); a conflict leaves the Staff's value and shows a note.
* New Staff keys: `Mothership` (true), `Damage Blocks Per Run` (600), `Damage Cells Per Run` (1200): a 3,000-block Leviathan pass is 5 Update10 runs (50 ticks,
  the Helm needs 80 now); Staff skips a slice above 60 % of its instruction limit, as the Helm's `Hot()`.

## 6. Steps (each one builds and is checked before the next)
0. Baseline: `python3 -I tools/build_all.py --no-zip --only "Ballista" "Shrike" "Shrike Staff Test" "Corsair"`; record sizes; run the baseline sims of step 6.
1. StaffAI 1.5: `Mothership`, Helm Custom Data reader, `[Fleet]` migration, fleet listen + `.TGT`/home/verb relay, `.FLT`, damage sensing + `.DMG`, HUD page + Helm
   menu (`.HUD`, `.MNU`), all gated on H2S bit 8. With the old Helm nothing changes: `selftest m2 m4 m5 m6` green.
2. Masters: `tools/helmlink_block.cs` 1.2 (bit 8, `.TGT/.DMG` listeners, `FleetSend` stub, home on empty cmd, `.MNU`, `.HUD` send, link-lost hit),
   `tools/manual_block.cs` 1.1 (HUD/menu out, `ManMode` kept); remove FLEET LINK, feeds and damage code from `ShrikeAI.cs`; drop `"ShrikeAI"` from
   `tools/fleet_sync.py` SCRIPTS; `python3 -I tools/blocks_sync.py` then `--check` green. Measure: Helm <= 80,000.
3. Designs: `ballista.py` spare -> Staff (+ `SCRIPTS` gets `StaffAI.cs`); `shrike.py` adds `[Staff] Shrike` (Full); Corsair/Warden/Leviathan unchanged.
4. Python tests (new `tools/tests/test_helm_split.py`, edits): every blueprint with a ShrikeAI PB has exactly one `[Staff]` StaffAI PB on the same grid with the
   same Ship Tag and Block Prefix; every `.<suffix>` the Helm sends has a Staff listener and vice versa, with identical `MyTuple<...>` text on both sides; ShrikeAI
   has no `FleetLoad(`/`ScanSlims(`/`ManHud(`; `test_ballista.py` `test_three_pbs_and_the_spare...` becomes "Helm, Spotter, Staff (Mothership=false)" and
   `test_scripts_are_embedded` expects 3 programs; `test_helm_custom_data_keys_are_literals_in_the_script` also accepts StaffAI for `[Manual]` menu/HUD keys.
5. Sim selftest `selftest hsplit` (`sim/src/Run/SelfTestHelmSplit.cs`, in `all`, on Shrike Staff Test and Ballista): (a) a FLEET_TGT from a Hornet becomes a Helm
   contact within 14 ticks; FLEET_CMD `hunt` -> HUNT once; FLEET_CMD `launch` reaches the Helm only; FLEET_HOME -> `_dynHome`. (b) a Helm share appears on
   FLEET_TGT. (c) under fire in a duel `UnderFire` and `_hitDir` within one pass + 12 ticks of the baseline. (d) `sit` -> HUD surface shows MODE MANUAL and PIP;
   two argument-less Runs of the Helm -> the 2nd menu item reaches the Helm. (e) `15:killpb Staff`: Helm keeps fighting, one hit set, 0 Staff override writes;
   `killpb Helm`: Staff releases within 1.5 s + 10 ticks (as m2 b). (f) `disablepb Staff;enablepb Staff` and the same for Helm: no false hit, link back.
   (g) `OverrideFightTicks` = 0 in m6 manual (two writers would show here). (h) old-Helm + new-Staff: no relay without bit 8.
6. Sim runs (few seeds, regression only): `run --filter "^enemy/Tech Hunter-vs-Ballista$" --seeds 10` before/after; Shrike vs Hornet 10 seeds; Corsair, Warden,
   Leviathan 3 seeds each in their existing ship scenarios; `selftest m2 m4 m5 m6 hsplit`. 20+ seeds only if a direction looks off.
7. Last: `SCRIPT_LIMITS = {"ShrikeAI.cs": 86000}` with the comment "past ~80 k move a module (docs/design/helm_split.md)", `tools/tests/test_m6.py:11` text
   "ShrikeAI <= 86,000", docs (control_arch s1.3/M-notes, Ballista.md 3c, Shrike doc, HANDOFF). Rebuild the five blueprints with `--no-zip`; zips not rebuilt.

## 7. Risks and what the sim cannot show
* Real-game IGC latency/loss on CurrentConstruct (K8), the `SetMessageCallback` run count, and whether Update10 + callback keeps the HUD pip smooth: in-game test
  "HUD pip lag" with a pilot. FBW itself does not change.
* Damage sensing reads blocks from another PB: same API, but the Staff's slice timing under a real 1,300+ block hull is only modelled.
* Mixed versions on a player's ship (old Staff pasted with a new Helm): the Helm loses the fleet channel until the Staff is updated; the screen note says so.
* Fleet verbs now pass two PBs: an Allowed-Commands mistake in the Staff would forward something the Helm used to drop; covered by test (a).
* `Mothership=false` must be set on the Ballista/Shrike Staff or they start acting as carriers; covered by the design test.
