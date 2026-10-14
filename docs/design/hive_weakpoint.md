# Hive Weakpoint: the wing shares one breach point over the FLEET channel

Status: **implemented** (2026-10-14; minified Flight halves +3.9 k, Staff halves +10 k; sim and game not yet run). Owner request: the Longbow L Seeker stands off at 2 km (railgun range) but its
`[Seeker]` artillery turret only targets within 800 m (large-grid turret Range cap without an AI combat block), so it never finds a
weakpoint from a safe range. Give the hive the ability to RELAY WEAKPOINTS that any drone locates, with a DEFAULT relay of the shared
breach point, so the whole wing (snipers at standoff included) drills one hole. Background: `docs/tech-hunter-study.md` sections 2, 5
(path item 3, "one shared breach point for the whole fleet over IGC"), 6, 7.

## 0. What exists today (read before changing anything)

| piece | where | relevant facts |
|---|---|---|
| BREACH + SEEKER | `tools/aim_block.cs` v1.0, synced by `tools/aim_sync.py` into HornetDroneAI, GnatSwarmAI, LongbowSniperAI, WaspFlightAI, LancerAI, HarrierAI, AegisAI, MantisAI, BasiliskAI (+ legacy `WaspAI`) | hole state `_bId,_bS,_bDir,_bDepth,_bProgT,_bSeek,_bBad`; points in the target frame (offset from `e.Position`, axes of `e.Orientation`), turned to world every tick by `BreachOffset` with `t.O`; depth measured by `BreachHit` from every 4th lock ray (`ProbePoint`); `BreachMiss` x2 or `Breach Stall (s)` -> `BreachDrop` -> `_bBad` (6 m). `SeekerTick` makes `_skS/_skDir` from turret Azimuth/Elevation (or `HitPosition` if the game gives one) + one camera ray. |
| SPLIT LINK | `tools/lk_block.cs`, `tools/split_sync.py`, every Flight half | Flight<->Staff over `WSPL<FlightPB id>` + `.F2S/.FOL/.S2F/.CMD/.TGT/.FLT`, `TransmissionDistance.CurrentConstruct` |
| FLEET LINK | `tools/fleet_block.cs`, `tools/fleet_sync.py`, in the **Staff** halves (LongbowStaffAI etc.), carrier relays, Stinger, TubeRam, StaffAI | `<ch>_TGT/_CMD/_HOME`; IGC-sourced contacts are never re-shared |
| Spotter feed (Ballista only) | `SpotterAI.cs` <- Peeper `<Host>_CRIT`; ShrikeAI reads `<Tag>_AIM (id, local point)` into `tools/breach_block.cs` | prior art: same target-frame convention, `BreachSet` on arrival unless `BreachBad` |

The aim block lives in the Flight PB (hot path); the fleet channel lives in the Staff PB. The relay therefore needs both halves.

## 1. Who finds a weakpoint (realistic sources)

1. **Any drone's own BREACH point (the DEFAULT relay, kind 0).** Camera-derived, works today on every armed drone with a lock: the
   hull point nearest the target centre plus the measured hole depth. No turret needed, works at 2 km. This is what makes "one hole"
   happen even with no seeker in the wing.
2. **A short-range seeker inside 800 m (kinds 1-3).** Realistic carriers now: **Basilisk Seeker** (roof gatling `[Seeker]`, fights at
   rocket range anyway) and the **Longbow L Seeker whenever the target is inside 800 m** (closing target, start of the fight). The
   point stays valid after the finder dies (it is in the target frame), so an expendable finder that lives 45-70 s near a Tech Hunter
   only needs a few seconds of `IsAimed`. Later candidates (owner's call, not in this plan): Peeper/Spotter bridge (section 7), a
   Watcher with a turret (outline rule: no block outside the Watcher's outline; ask first).
3. **In-game unknown that could remove the problem:** the Longbow L Seeker carries an `AI Offensive [AI]` block. The sim lets a
   working AI combat block extend the grid's turrets to ammo range (`TurretAiBlockExtendsRange=true`), so in the sim its seeker
   already reaches 2 km. If that holds in the game (block Enabled, "AI Behavior" possibly off under the AI GATE), the Seeker self-finds
   at standoff. In-game test item T1.
4. **Not relied on:** `GetTargetedEntity().HitPosition` (unverified; SeekerProbe tests it). Without it the seeker gives a good hull
   entry `S` and a good line `D` (towards the subsystem), but **no depth**: the current "m under hull" is the foot of the target centre
   on the aim line, not the block. So depth hints are sent only when `HitPosition` was used (flag bit 4), else `-1`.

## 2. What is relayed: `<ch>_WP`

One new tag on the fleet channel, `<ch>_WP` (`FLEET_WP`, or `FLEET_<secret>_WP`). Not copied to `Extra Broadcast Tags` or per-type tags.

```
<ch>_WP  MyTuple<long tgtId, Vector3D s, Vector3D d, Vector3D info, long sender, int flags>
  s      hull entry point, TARGET FRAME: (hitWorld - e.Position) rotated by Transpose(e.Orientation)   (= _bS convention)
  d      unit drill direction into the hull, target frame                                             (= _bDir convention)
  info   X = hole depth reached by the sender's own probe rays (m, 0..60)
         Y = vital depth hint along d from s (m), -1 = unknown (only set from HitPosition, or triangulation later)
         Z = age (s) since the sender last confirmed the point (probe hit, depth progress or seeker sample)
  sender sending GRID EntityId (Me.CubeGrid.EntityId; Flight and Staff share it)
  flags  bits 0-2 kind: 0 BREACH (camera point), 1 PowerSystems, 2 Weapons, 3 Propulsion, 4 other group
         bit 3 SEEKER (a turret located it)   bit 4 HITPOS (game gave the block position)   bit 5 DONE   bit 6 STALL
         bit 7 ADOPTED (sender took it from the relay and has since confirmed it with its own ray)
```

**Why target frame, not world position + age:** the ships move and turn (the Tech Hunter circles at 700 m); a world point is wrong
within a second, and every drone already stores hull points in the target frame from the same `MyDetectedEntityInfo` (`Position`,
`Orientation` are game state, identical for every observer at a given time). A receiver with its own lock (`t.HasOri`) re-projects it
every tick for free (`BreachOffset` already does). A receiver without its own lock cannot BREACH anyway (`AimActive` needs 3 hull
points), so world coordinates would buy nothing. No sender orientation snapshot is needed. Known weakness: `e.Position` is the
bounding-box centre and shifts by up to a few metres when edge blocks die; probe-ray validation (section 3) absorbs it.

**Rates.** Flight -> Staff `.WPF` on every `BreachSet`/`BreachDrop`/new seeker point, else every 2 s while a point is held.
Staff -> `<ch>_WP` on change at once (cap 4/s), else one heartbeat per 2 s per drone. Load: 10 drones = 5 msg/s.

**What a drone may send (no-echo rule, the analogue of "IGC contacts are never re-shared").** A drone sends only **its own** state:
its seeker sample, or its current hole **after its own probe ray confirmed it** (a hit within 3 m of the entry, or depth progress).
It never forwards a received `_WP` message; a relayed point it adopted but has not confirmed is not sent. Staff drops messages with
`m.Source == IGC.Me` or `sender == Me.CubeGrid.EntityId`. Plausibility as `FleetV` (finite, |s| < 1e4, |d| ~ 1).

**Clustering / priority (receiver Staff).** Per target (max 4 targets, 16 entries each) keep the latest entry per sender. Entries of
one target with |s - s'| < 4 m are one HOLE. Hole score, highest wins, deterministic so every drone picks the same hole:
1. class: seeker kinds by `Weakpoint Priority` (default `1,3,2`: power, propulsion, weapons) above kind 0;
2. supporters (distinct senders that are not STALL);  3. max depth reached;  4. lowest sender id (tie break).
**Expiry:** an entry not refreshed for `Weakpoint Expire (s)` (default 20) is removed; the hole lives as long as anyone drills it
(snipers that adopted and confirmed it keep it alive after the finder dies).
**Finished:** DONE (sender's depth >= hint + 3 m, or >= 55 m, or its seeker group turret moved > 8 m away from the point after a kill)
removes the hole at once and every receiver puts `s` into `_bBad`. STALL (sender's `BreachDrop` by stall or two misses) only removes
that sender's support; a hole whose every supporter is STALL and that nobody deepened for `Breach Stall (s)` is removed.

## 3. How receivers use it (Flight, inside the aim block)

The Staff sends the Flight the **best hole for the Flight's current target** (`_tgtId` from `.F2S`) as `.WPT` (same tuple, info.Z =
age at the Staff, sender = the hole's best sender) on change and every 1 s. Flight keeps one slot `_wp*` + its arrival time.

`WpAdopt(t)`, called in `BreachOffset` right after the stall check, before the own-seeker line:

* skip if `!WpOn`, slot older than 3 s, other target id, `BreachBad(s)`, in the local reject list `_wpRej` (6 m, max 6), or the drone
  holds an **own** seeker point (`_bSeek` and not from relay): own turret beats the relay, the relay beats own camera points;
* **cone test:** `los` = own line of sight to the target in the target frame; `c = Dot(-los, d)`.
  `c >= cos(Weakpoint Cone)` (default 40 deg): **same hole** -> `BreachSet(s, d, seek)`, `_bDepth = max(0, depth - 2)` (aim at the
  bottom of the shared hole at once), mark `_wpAdopt = true`.
  Else, if the hint is known: **converge** -> `V = s + d * hint`; entry `h` = the own hull point (`t.Hull`) nearest the line from me
  to `V` and in front of `V`; `BreachSet(h, Normalize(V - h), seek)`. Else (off-cone, no depth): keep the own point.
* **switch rules (hysteresis):** adopt when not holding a point; replace a held kind-0 point by a seeker hole at once; replace a held
  point by another relayed hole only when its class is higher, or it has >= 2 more supporters, or it is >= 3 m deeper, and the held
  point has made no progress for 6 s (the existing seeker rule). Never more often than every 6 s.
* **validation by own rays (standoff check):** nothing new to cast: the existing probe ray (every 4th lock ray, to the aim point +
  `BreachProbe`) checks it. In `BreachHit`, for an adopted, not yet confirmed hole: `d < -6` (my ray stops on hull well before the
  entry: the hole is not visible from my side) -> `_wpRej.Add(s)`, `BreachDrop` without STALL, back to own point; a hit within 3 m of
  the entry or progress -> confirmed (`_wpOk`), now sendable with ADOPTED. Two empty probe rays -> existing `BreachMiss`.
* stall handling unchanged (`Breach Stall (s)` -> `_bBad` + STALL sent). A DONE/STALL for my current hole that arrives over `.WPT`:
  DONE -> `BreachDrop` + `_bBad`; STALL -> ignored while my own depth progressed in the last 10 s.
* frame transform per tick: none extra; `s, d` are stored as `_bS/_bDir` and `BreachOffset` applies `t.O` every tick.

Seeker side: `SeekerTick` also records `_skKind` (from `SeekerGroup`), `_skHint` (`(q - hit)` only when `hp`, else -1) and
`_skTime`; `WpOut` sends the seeker sample even when the drone itself is not in BREACH (a Basilisk Seeker relays for the Longbows).

**Config** (written with defaults; all default ON):
* Flight, script section (`[Longbow]`, `[Basilisk]`, ...) via the `aim_sync.py` config hook: `Use Fleet Weakpoints=true`,
  `Share Weakpoints=true`, `Weakpoint Cone (deg)=40`.
* Staff, `[Fleet]` section (written by the new Staff block, FleetLoad keeps unknown keys): `Weakpoint Priority=1,3,2`,
  `Weakpoint Expire (s)=20`.
* Commands (PB argument, either PB): `wp on|off` (adoption and sharing). Not in FLEET `Allowed Commands` (like `aim`).
  `breach off` makes the relay moot for that drone (it still shares seeker samples); `seeker off` stops its own turret, relayed seeker
  holes are still used; `aim breach` forces BREACH (then relayed holes are used on any target size).
* Status: aim line `hole N m (fleet k)` (k supporters) / `(seeker)`; Staff screen `WP: <kind> hole N m, k drones, age s`.

## 4. Standoff geometry (sniper at 2 km, finder at 800 m)

The finder's line `d` is set by where the finder was; the sniper's railgun slug goes along the sniper's own line. Aiming at
`s + d * depth` from an angle theta enters the hull `depth * tan(theta)` away from the hole: at theta 40 deg and the Tech Hunter's
~6-10 m of cover that is 5-8 m, two large blocks, the edge of "one hole". Hence the 40 deg cone for "same hole" and the converge rule
(all lines meet at the vital `V`) when the depth is known. Snipers do not trust the relay blindly: their own probe ray (camera charge
is ~2 km/s, the lock already refreshes at 2 km) confirms or rejects the hole from their side (`d < -6`). No flight change: the
standoff bearing stays the drone's own. Follow-up (not in this plan): the Staff could hint the Flight a preferred standoff bearing
(the hole's axis); the Flight would stay the only writer of thrusters and gyros.

## 5. Size / PB budget

Minified now (tools/minify_cs.py): Flight LongbowSniperAI 79.9k, GnatSwarmAI 79.9k, HornetDroneAI 76.7k, LancerAI 78.1k, HarrierAI
77.8k, AegisAI 80.7k, WaspFlightAI 82.4k, MantisAI 83.0k, BasiliskAI 83.6k; Staff halves 22-33k; legacy WaspAI 99.8k (no blueprint).
Estimate: Flight +1.7k (aim block ~1.3k, link ~0.4k), Staff +3-4k (new block).

* **No new PB is needed.** The off-hot-path part (fleet IGC, clustering, scoring, expiry, display, priorities) goes to the Staff PB
  every armed drone already has (room ~45k each). Only the adoption, which changes the aim point, stays in Flight. No thruster/gyro
  override is touched by any of this; Flight stays the only writer.
* Flight halves past ~85k after the change take their **documented next cut into their existing Staff PB** in the same commit:
  BasiliskAI (~85.3k) -> other fleets' target feeds + sensor setup, or damage sensing `.DMG` (1,552) per `basilisk_split.md`;
  MantisAI / WaspFlightAI if they pass 85k -> damage sensing the same way. Do not raise any cap, do not drop the feature.
* **WaspAI.cs (legacy single PB, 99.8k, no blueprint since the Wasp runs WaspFlightAI)** cannot take the block and has no SPLIT LINK.
  Add `FROZEN = ["WaspAI"]` to `aim_sync.py` (skipped by `unlisted()` and the sync, printed as `frozen`), as the SENTRY retrofit did.

## 6. Implementation steps (in this order, one commit per step or one at the end; work on `main`)

1. **aim block v1.1** (`tools/aim_block.cs`): `WpOn, WpShare, WpCone`; slot `_wpS,_wpD,_wpI,_wpF,_wpId,_wpT`; `_wpAdopt,_wpOk,_wpRej`;
   `WpAdopt(Track t)` (section 3), confirm/reject lines in `BreachHit`, a `stall` argument to `BreachDrop` (stall/miss vs reject vs
   done), `_skKind/_skHint/_skTime` in `SeekerTick`, `WpOut()` building the tuple, `AimInfo` text. `tools/aim_sync.py`: config hook
   (3 keys) and `case "wp"` in the command hook, both with `PREVIOUS` entries for the v1.0 text; `FROZEN`. `--apply`.
2. **SPLIT LINK v1.1** (`tools/lk_block.cs`): `_lkWpt` listener on `_lkK + ".WPT"` in `LkInit`, read in `LkRead` into the `_wp*` slot;
   `WpSend(tuple)` -> `_lkK + ".WPF"` when `LkAlive`. `python3 -I tools/split_sync.py --apply`. The tuple type check
   (`m.Data is MyTuple<long,Vector3D,Vector3D,Vector3D,long,int>`) as in `LkRead`.
3. **New shared Staff block `WEAKPOINT RELAY v1.0`** (`tools/wp_block.cs`, registered in `tools/blocks.json` for AegisStaffAI,
   BasiliskStaffAI, GnatStaffAI, HarrierStaffAI, HornetStaffAI, LancerStaffAI, LongbowStaffAI, MantisStaffAI, WaspStaffAI; anchor
   default). Functions: `WpLoad()` (keys in `[Fleet]`, listener on `_flChan + "_WP"` re-registered when `_flChan` changes),
   `WpListen()` (`_fk + ".WPF"`), `WpStep()` (drain fleet + Flight messages, clustering, expiry, send `<ch>_WP` and `.WPT`),
   `WpStatus()`. Hooks per Staff script (same text in all 9): `WpLoad();` at the end of `LoadMods()`, `WpListen();` at the end of
   `Listen()`, `WpStep();` after `Relay();` in `Tick`, `WpStatus()` in `Display()`. Uses `_fk, _tgtId, _tick, _now, _f2sTick, _flChan`.
   `python3 -I tools/blocks_sync.py --apply` (runs the legacy syncs too).
4. **Docs in the same commit:** `docs/hive-protocol.md` (section 2 table row `<ch>_WP`, section 3 keys, section 4 who sends/listens,
   a new section "Weakpoints" with the tuple, flags, no-echo and cluster rules, `.WPF/.WPT` added to the split link table in
   `docs/design/wasp_split.md`); `docs/retrofit.md` Pending row (section 7); `docs/tech-hunter-study.md` section 5 item 3 -> link here;
   this file's status line with measured sizes.
5. **Checks:** `bash tools/compile_check.sh`, `python3 -I tools/blocks_sync.py --check`, minified sizes of all 9 Flight + 9 Staff
   halves, `python3 -I tools/build_all.py --no-zip --only "Longbow L" --only "Longbow L Seeker" --only "Longbow S" --only "Basilisk Seeker"`
   (and one build of every other changed model, or the full `--no-zip` run once at the end). Say the zips are not rebuilt.

**Sync checks to add** (as Wasp Split): Staff sends nothing while the Flight is down (`_tick - _f2sTick > 90`); a rebuilt Flight PB (new
`_fk`) gets the current best hole within 1 s (the 1 s `.WPT` heartbeat); after a run gap (Staff recompiled) the Staff memory is empty
and refills from the 2 s heartbeats of the wing within one `Weakpoint Expire`; Flight ignores a `.WPT` slot older than 3 s.

## 7. Sim plan (small, per CLAUDE.md)

No sim code change is needed: IGC between same-team PBs works, `--enemy-fleets` takes fleet files, and the generic settings override
gives the owner's premise: `--turret-ai-block-extends-range false` (the drones' AI Offensive block no longer lifts the Seeker turret to
2 km; the Tech Hunter has none anyway). New fleet file `sim/fleets/tech_hunter_wp.txt`:
`8xLongbow L`, `2xBasilisk Seeker+8xLongbow L`, `Longbow L Seeker+7xLongbow L`, `2xBasilisk Seeker+4xLongbow L+4xLongbow S`.

1. Smoke, 3 seeds, one fleet, `--echo-ship` on a Longbow: no script exceptions, `hole N m (fleet k)` appears, supporters > 1.
2. A/B, 10 seeds per fleet, `Use Fleet Weakpoints` on vs off (`--custom-data`/`--pb-custom-data` "Flight=..."), Tech Hunter, 10-min
   cap: power blocks / thrusters destroyed (`power_blocks_end`, existing result fields). Extend to 30 seeds only on the fleet whose
   direction is unclear. Expect: kind-0 sharing alone already concentrates fire (one hole instead of up to ten).
3. Regression: `1v1` Longbow L / Basilisk vs Hornet, 5 seeds each (small grids: BREACH and the relay stay off), no exceptions.
4. Split sync: `killpb Staff` at 30 s (3 seeds): Flight keeps drilling its hole; `killpb Flight`: Staff stops sending `_WP`.

### Smoke results (2026-10-14, new Tech Hunter 90k = 17,457 blocks, `--turret-ai-block-extends-range false`)

* `Basilisk Seeker + 8x Longbow L`, 3 seeds, 300 s: 0 script exceptions on any ship, 0 errors.
* `Basilisk Seeker + 2x Longbow L`, seed 0, Longbow screen: the Basilisk's seeker found a **power** hole, the Staff showed `Weakpt : power hole, 2 drone(s)`
  and the Longbow aimed `hole 0 m (fleet 2)` at it from standoff; once the Basilisk died the hole expired (20 s) and the Longbow went back to its own
  camera point (`hole 19 m (fleet 1)` later). So send, cluster, adopt and expiry all work in the sim.
* No A/B (relay on vs off) run yet: needs `Use Fleet Weakpoints=false` / `Share Weakpoints=false` in the `[Longbow]` section via `--pb-custom-data`.
* Full `build_all --no-zip`: 71 PASS, 2 FAIL (Longbow Foundry, Mantis Foundry: bay layout; Longbow Foundry fails the same way on the commit before this change).

## 8. Retrofit (docs/retrofit.md, Pending row in the implementing commit)

| Improvement | Could go to | Note |
|---|---|---|
| Fleet weakpoint relay `<ch>_WP` (aim block v1.1, SPLIT LINK v1.1, WEAKPOINT RELAY Staff block) | Helm ships (ShrikeAI `breach_block.cs` + StaffAI fleet channel: Ballista, Warden, Leviathan, Corsair, Shrike); SpotterAI could publish its best vital as kind 1-3 with HITPOS-like confidence (bridge from the Peeper's `_CRIT`) | the 9 split drones get it in the implementing commit |

## 9. Risks

* Opposite sides: two snipers on opposite sides never share (cone); each drills its own hole, as today. Not worse than now.
* A wrong seeker hole (decoy, aim-line error) attracts the whole wing: mitigated by per-drone ray validation, STALL votes and the
  30 s stall; a decoy gives kind = the group the turret was set to but a hole that never deepens.
* `e.Position` drift after edge-block losses (section 2): points move by metres; the 4 m cluster radius and the probe-ray depth
  measure cope; watch it in game.
* Spoofing: `<ch>_WP` is as open as `<ch>_TGT`; the fleet `Secret` covers it. A fake hole costs at most 30 s of fire per drone.
* IGC queue: 5 msg/s for 10 drones on one extra tag; fine.

## 10. In-game test items (owner)

* T1 Longbow L Seeker at 1.5-2 km from a large target with its AI Offensive block on: does the `[Seeker]` turret get a target
  (status `Seek:` line)? If yes, it self-finds at standoff.
* T2 SeekerProbe on a scout: is `HitPosition` given (`hitpos ... from centre`)? Decides whether depth hints exist.
* T3 Basilisk Seeker + 3 Longbow L vs a large ship: every Longbow shows `hole N m (fleet k)` with the same k; the target hull shows
  one hole (or one per side), not scattered craters.
* T4 Kill the Basilisk Seeker after its first report: the Longbows keep the hole (supporters keep it alive).
* T5 Two fleets with different `Secret`: no weakpoints cross.
