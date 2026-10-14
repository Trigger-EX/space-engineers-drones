# Ballista harasser-drone defence: Lookout PB + Helm hook (plan)

Status: **phase 1 implemented (2026-10-14), in a reduced form; nothing has run in the game.** Differences from this plan, after the owner's answers (the drone was AI-flown, small-grid autocannons, it chased the ship down and got close, the turrets never fired, no rails are to be redirected; custom turrets welcome):
* **No separate Lookout PB and no `LookoutAI.cs`.** The module (`Lk*` functions, section "LOOKOUT" of `scripts/SpotterAI.cs`, `[Harasser Defence]` Custom Data in the Spotter PB) lives in the Spotter PB. Reason: PCU. The ship with the foundry and a live probe is about 9,860 of 10,000 after the two AI turrets, a new PB (+100) would not fit, and the Spotter has 60 k of free room. The Helm hook is the shared block `tools/hd_block.cs` (`HARASSER LINK`). The spare PB of section 5 is the Staff PB since the Helm split.
* Phase 2 (PD rail pokes, nose request, `Harasser Window`) is **dropped** (owner: no rails redirected). The H2A tuple has no rail field and A2H no nose field; `HAR` is replaced by two ids in A2H.
* **Added: kiting** (`Away Speed (m/s)=40`, `Away Within (m)=900`): while a harasser is inside that range the bias also opens the range, unless the stand-off band of the large target forbids it. The sim showed the jink alone loses to the old behaviour (section 10).
* No camera refresh rays (section 2 source 4) and no hit-bearing "press" option: tracks come from the Helm's contacts (`.HTK`) and the vanilla turret's target; the dodge needs only a bearing.
* The roll is not frozen before the salvo (the Helm's fire window and its `Salvo` timing are not visible to the Spotter).
* **Custom turrets** (not in this plan): two `PD4L` rotor + hinge AI turrets, see `docs/design/ships/Ballista.md` 3f.
Original text of the plan follows.

Original owner report (game): a Tech Hunter ~1.6 km away launched a small drone that harassed the Ballista and
eventually beat it. The Helm knows two small-threat behaviours: KAMIKAZE DEFENCE (a small contact on a collision course) and `Evade()`, which returns
zero on large grids. A drone that stands off, circles and shoots matches neither.
Labels: [data] repo game data (`defs_stats.json`, design figures), [code] read from the current source, [calc] worked out here, [sim] stand-in
simulator, [unverified] never seen in the game.

## 0. What the current Helm does against such a drone (diagnosis)

| # | Mechanism | Evidence |
|---|---|---|
| D1 | **Beyond 800 m the drone is invisible while the Tech Hunter (TH) is held.** The AI blocks are off while a target is held (`AiStep`). `Auto Camera Scan=false` on the Ballista, and `AutoScanStep` only runs with no target. `MaintainLock` rays only go to the target. Only damage (`_hitDir`, `UnderFire`) reports the drone. | [code] |
| D2 | A blind camera search cannot help. A 3 m drone at 1 km covers ~2.8e-5 sr, and a 25 deg cone round `_hitDir` covers 0.6 sr. That gives 5e-5 per ray and ~3 rays/s, so about one find per hour. | [calc] |
| D3 | **Inside 800 m the turrets designate it and it takes the target.** Score: TH (current) 1600 - 400 sticky - 300 hive = 900; drone at 500 m: 500 - 150 small - 250 hive + 6 x angle. So the drone wins unless it is more than 133 deg off the nose (more than 83 deg at 800 m). `BatHope` does not save the TH: the rail P(hit) against a jinking 3 m drone is 0.4-0.8 at 500-1000 m. | [code, calc] |
| D4 | **Once the drone is `_tgt`, `Control()` flies relative to it.** `want = tv + ...` copies the drone's velocity, so the relative motion is zero and the drone's shooting is easy. Standoff 1,600 m and Min Range 900 m (flee at 60 m/s) are applied to the drone, the TH standoff is forgotten, and the 27-rail salvo goes at a 3 m target. The 27 slugs fly one lead solution, so they are correlated: about one real try per 66 s. | [code] |
| D5 | **No evasion.** `Evade()` is 0 on `Big`. The jink (12 m/s, random across the TARGET's line, new every 1-2 s) saturates the 15.8 m/s2 axes for ~0.6 s and then coasts: on average about 5-6 m/s2 across the drone's line of sight. | [code, calc] |
| D6 | The 4 `LargeGatlingTurret` (800 m, elevation -43..+90 deg on their own base, 0.3 deg spread, 800 m/s) are the only weapon that can follow a small drone. Turrets top P/S on the breech wall, belly P/S below it. Hull masking of the horizontal flank belt is not known. | [data] / [unverified] |

Likely in-game sequence (hypothesis, [unverified]): drone inside 800 m, turret designation, the Helm switches target to the drone (D3), mirrors its
velocity and fires the salvo at it (D4), the TH is ignored, and the unarmoured hull loses turrets and thrusters. Section 9 asks the owner to confirm.

## 1. Approach in one table

| Part | Where | What |
|---|---|---|
| Detection, classification, threat tracking | **Lookout PB** (the spare PB, renamed) | small hostile contacts from the 4 turrets (polled read-only), the Helm's small contacts (IGC), `BALLISTA_TGT` shares and its own refresh rays; HARASSER / SUSPECT / PHANTOM states |
| Counter-manoeuvre planning | Lookout | "corner jink": bang-bang acceleration along the strongest corner of the thrust box that lies across the threat's line of sight, re-picked every 0.8-2.5 s; a roll request that puts the drone above or below the hull (turret and camera coverage) |
| Applying it | **Helm**, a ~1.8k-char hook block | adds the velocity bias before `KzWant`, takes the roll, turns its own jink off, keeps a harasser from becoming the steering/salvo target, stops copying a harasser's velocity |
| Offence | existing turrets (phase 1); PD-rail pokes in the rail recharge window (phase 2, switch, off) | section 4 |

The Helm stays the only writer of thrusters, gyros, guns and AI blocks. The Lookout writes nothing on the grid except its own screen.

## 2. Detection and classification (Lookout)

**Sources** (in order of value):
1. **Turrets**: `HasTarget` / `GetTargetedEntity()` on `[SH] [PD] Gatling Turret 1-4`, every run (6 Hz). These give position, velocity, orientation and radius out to 800 m. [code: the Helm does the same in `PollDesignators`]
2. **Helm small contacts** `.HTK`: every contact of radius 15 m or less that the Helm saw in the last 1 s, from LOCK rays that hit "something else in the way", AI confirm rays, SCAN, IGC shares. Note that IGC-created tracks are typed `LargeGrid` in `ReadTgt`, so the filter is the radius, not the type. 3 Hz, at most 4 per send.
3. **`BALLISTA_TGT`** shares (wingmates; the Helm's own target and kamikaze shares): same tuple the Spotter reads. Radius < 0 means finished.
4. **Own refresh rays** on SUSPECT/HARASSER tracks that are older than 0.5 s and within 1,800 m: at most 2 per run, aimed at the predicted point + 2 m. Rules:
   * use a camera only if the point is inside its 44 deg cone (`dot >= 0.72`, as `CamFor`) and `AvailableScanRange >= len + Reserve` (600 m);
   * never use a camera whose cone holds the Helm's target bearing (that camera feeds `MaintainLock`);
   * call `CanScan(len)` before each cast (the Helm's `KzRayStep` shares the charge).

   The 6 cameras' 44 deg cones leave gaps round the 8 body diagonals (54.7 deg from each axis) [calc]. The roll request (section 3) moves the drone onto the Top or Belly camera axis.
5. **Damage**: `_hitDir`, its age and a hit counter, from the Helm (`.H2A`). `CheckDamage` stays in the Helm because it already runs there.
6. **Launch event** (opportunistic): a small contact first seen within 400 m of a tracked large enemy skips the dwell time.

**States** (per small track; constants are Custom Data, section 6):

| Test | Rule | Default |
|---|---|---|
| small | `SmallGrid` (or IGC with radius only) and bounding radius <= Max Harasser Radius | 15 m |
| not a kamikaze | NOT (closing > 15 m/s and CPA miss < 40 + 60/km and tgo < 12 s). This is the KZ block's `KzCpa`/`KzHit` test, copied; kamikazes stay the KZ block's job | KZ defaults |
| SUSPECT | small, hostile, not a kamikaze, range within the band for >= Dwell (0 after a launch event) | band 150-1,600 m, dwell 3 s |
| HARASSER | SUSPECT plus any one piece of evidence: **nose-on** (angle(fwd, me - pos) <= 12 deg for >= 1 s of the last 4 s; a fixed-gun drone must point at its lead point); **damage match** (`UnderFire`, hitDir age <= 3 s, angle(hitDir, bearing) <= 60 deg); **persistent** (range <= Turret Range for >= 5 s) | latch 15 s after the last evidence, while seen in the last 8 s |
| PHANTOM | `UnderFire`, hitDir age <= 3 s, and no tracked contact of any size within 50 deg of hitDir. Bearing = hitDir, range assumed `Phantom Range`. A hitDir that lines up with the TH is kept as a threat bearing too: evading the TH's long guns is also right | 1,000 m, latch 10 s |

Evasion needs only a **bearing**, not a range, so PHANTOM (the stand-off drone the Helm cannot see, D1/D2) gets the full manoeuvre.
`_hitDir` is the mean direction from the grid centre to the damaged blocks. As a bearing to the shooter it may be off by tens of degrees
[unverified]. If it is off, part of the dodge acceleration falls along the line of sight: less useful, never harmful.

## 3. Counter-manoeuvre

### 3.1 Physics [calc]
* **Thrust box** (body frame, loaded 913 t): +-31.5 m/s2 fore/aft, +-15.8 m/s2 right/left and up/down [data]. `ApplyThrust` clamps each axis group on its own [code], so a large velocity error toward a corner gets the whole box corner.

  | acceleration | magnitude |
  |---|---|
  | box corner `(+-15.8, +-15.8, +-31.5)` | 38.6 m/s2, 35.3 deg off the nose axis |
  | fore + lateral corner | 35.2 m/s2 |
  | lateral + vertical corner | 22.3 m/s2 |
* **Only acceleration across the shooter's line of sight (LOS) makes a shot miss.** Motion along the LOS only changes the time of flight. With `P = I - u uT`, u = unit bearing to the threat, the best dodge is `max_k |P c_k|` over the 8 corners. This is **>= 35 m/s2 whenever the threat is more than ~35 deg off the nose/tail axis** (abeam in any plane: 35.2; on a body diagonal: 38.6), and **22.3 m/s2 when it sits on the nose or tail axis**. Rolling does not change the dodge; it changes which turrets and cameras see the drone.
* **Miss distance** for a shooter that leads with velocity only: `e = 1/2 a_perp T^2`, T = R / v_p. A shooter that also leads with acceleration (our sim drones' LEAD v1.0) only loses on a switch during the flight: about `0.24 a T^2` when one happens (~80 % of shots with switch period ~T).

  | drone weapon (v_p [data]) | R | T | e now (~5.6 m/s2, D5) | e with corner jink (~30 m/s2 effective) |
  |---|---|---|---|---|
  | gatling 800 m/s | 500 m | 0.63 s | 1 m | 6 m |
  | gatling 800 m/s | 800 m | 1.0 s | 3 m | 15 m |
  | small railgun ~1,000 m/s | 1,200 m | 1.2 s | 4 m | 22 m |
  | assault cannon 500 m/s | 1,200 m | 2.4 s | 16 m | 86 m |
  | rockets ~200 m/s | 700 m | 3.5 s | 34 m | 184 m |

  A round misses once `e` exceeds the hull half-extent along the shift (15-26 m: hull 52.5 x 30 x 32.5 m). So the defence is decisive against slow and long-range weapons (rockets, assault cannon, railguns beyond ~1 km). It is marginal against gatlings inside 500 m, which is where the turrets are already shooting.
* **Cross-section is not a lever.** Every view of the hull is 30 m or more, and a gatling's spread at 800 m is about 4 m. The smallest view is broadside (32.5 x 30 m), not end-on (52.5 x 30 m). Do not spend turning on it.
* **The drone cannot be outrun**: both are capped at 100 m/s, and a small drone accelerates harder. Opening the range only matters against weapons of 800 m or less, and those drones are inside turret range anyway.
* **Turning** (BatTurn calibration, `MaxTurn = 0.85 sqrt(2 a 0.6)`, yaw 3.1 rad/s2, giving ~1.6 rad/s): 90 deg in ~1.5 s, 180 deg in ~2.4 s; roll 4.7 rad/s2, so 90 deg in ~1.3 s [calc, point-mass inertia].
* **Hydrogen**: a corner fires 8 of the 16 large H2 thrusters at full (4 main + 2 lateral + 2 vertical), 7.5 MW each, ~4,820 L/s each. The 4 x 15 ML of tanks last ~26 min of continuous corner thrust [calc; the H2 energy density constant is unverified].

### 3.2 The planner (Lookout, every run, ~6 Hz)

Function names are suggestions for `LookoutAI.cs`.

1. **Threat set** `{(u_i, w_i, R_i)}`:
   * HARASSER: w = 1, or 1.5 when damage-matched;
   * PHANTOM: w = 0.7;
   * the TH bearing u_T: w = 0.5 when hitDir is within 30 deg of it.

   At most 3 bearings, the nearest first.
2. **Box** `LoBox()`: per direction, the sum of `MaxEffectiveThrust` of working `[SH] Thruster` blocks grouped by the Remote Control's frame, divided by `CalculateShipMass().PhysicalMass`. Lost thrusters shrink the box.
3. **Corners** `c_k = s_R a_R R + s_U a_U U + s_F a_F F`, for s in {+-1}^3 (world frame).
4. **Score** `S_k = sum_i w_i |c_k - (c_k . u_i) u_i|`. Excluded corners:
   * (TH band) `c_k . u_T > 0` while `d_T < Standoff - 150`, and `c_k . u_T < 0` while `d_T > 1,700` (rail MaxFireRange). Standoff is read from the Helm PB's `[Shrike] Standoff Range`.
   * (reflect) `c_k . v_dev > 0` while `|v_dev| > 0.8 V_j`, with `v_dev` = ship velocity minus its 4 s moving average.
5. **Switch** `LoPick()` when `now >= t_next`, on a new hit (hit counter changed, at most once per 0.5 s), or when the current corner is excluded. Pick uniformly among the corners with `S_k >= 0.8 max S`, never the current one. Then `t_next = now + clamp(U(0.7, 1.5) x R_eff / v_p, 0.8, 2.5) s`, with R_eff the range of the heaviest threat and v_p = `Harasser Projectile Speed` (800).
6. **Bias** `B = V_j c_hat_k`, V_j = `Jink Speed` (30 m/s). A step between corners is a velocity error of up to ~42 m/s. `acc = 2(want - v)` saturates the box for most of a 0.8-2.5 s leg, so the motion is near bang-bang with a random switch time and axis.
   * **Press** (switch, default off): `+ u_H x 20 m/s` while a tracked HARASSER holds 800-1,100 m and the TH band allows. It brings the drone into turret range or pushes it back.
   * `|B| <= 45 m/s`.
7. **Roll** `LoRoll()`, sent as the desired Up `U* = +-normalize(u_H - (u_H . n) n)`. n is the nose; the sign is the one nearer the current Up, so the roll is at most 90 deg. Rules:
   * only when `|u_H . n| < 0.94` (the threat is more than 20 deg off the nose axis);
   * deadband 20 deg;
   * **frozen while the rail salvo is due within 3 s** (`railReadyIn <= 3`), so the roll cannot hold the 0.3 deg fire window open.

   With the drone overhead or underfoot, both top or both belly turrets see it with the hull below them, and the Top or Belly camera can ray it.

### 3.3 Composition in the Helm's `Control()` (per tick; the only thruster writer)

| Order | Term | Limit / rule |
|---|---|---|
| 1 | base: `tv + dir*vr + tan*orbit + jink + Evade() + Separation()`, where **tv = 0 when `_tgt` is a flagged harasser** and **the old `_jink` is off while the Lookout's bias is active** (A2H flag bit2) | MaxSpeed; standoff vs the TH unchanged |
| 2 | `want = HdWant(want, cap)`: `+ B` when A2H is fresh (<= ttl 0.6 s); over the cap, the rest is shrunk to `cap - |B|` and B is kept whole (same rule as Kz's break) | new, **before** KzWant |
| 3 | `KzWant` | back-away and break unchanged; the break stays whole over the bias |
| 4 | `FaWant` | barrier/ramp up to 50 m/s, unchanged; it wins over the bias (30-45 m/s) |
| 5 | cap clamp, `acc = 2(want - v)`, `|acc| <= 50`, `ApplyThrust` per-axis clamp | unchanged |

**Orientation.**
* The nose stays on the TH: the rails are the reason the ship exists, and the dodge is good anyway once the threat is more than 35 deg off the nose axis.
* Roll is free (`desiredUp` is zero in space today): `up = grav ? -grav : HdUp()` in both `Aim()` call sites of `Control()`.
* `TvAim` (the Thrust PB, lost thrust groups) overrides both, as now.
* The nose may be **sacrificed only in the rail recharge window** (phase 2, section 4): after a salvo, `railReadyIn > 10 s`. It returns when `railReadyIn <= 5 s`, when the harasser is gone, or when the TH track is older than 1 s. Yaw away from the TH only in steps of 90 deg, so the TH sits on a side camera's axis and `MaintainLock` keeps it.

**Targets.**
* `PruneAndSelect`: `score += HdScore(t)` = +3000 for a flagged harasser (the `BatKz` pattern). The TH stays the target; with nothing else about, the harasser is still the target and gets the guns.
* `MANUAL`, `ASSIST`, `IDLE`/`RTB` and the AI-flown search ignore the bias (Control does not run, or returns before translation). The Lookout screen says so.

## 4. Offence options

| Option | Scope | Notes |
|---|---|---|
| O1 Turrets do the killing; roll the drone above/below; the salvo is not wasted on it while a large target is held | **phase 1** | D3/D4 fixed by `HdScore` + tv = 0; the roll is 3.2 step 7 |
| O2 **PD-rail pokes in the recharge window**: `PD_RAILS` = 3 centre bottom-row rails tagged `[B2]` (the salvo becomes 24 rails, -11 %), `Point Defence=B2`, `Exclusive=true`, `Max Radius=15`. Lookout sets A2H bit4 "window" when `railReadyIn > 10 s`, the PD battery is ready (H2A bit6) and a harasser is within 1,400 m. The Helm then gives it `-3000` instead of `+3000`, the nose swings onto it, and `BatAim` fires the single rails | **phase 2, switch `Harasser Window=false`, blueprint variant first** | Slug 2,000 m/s: at 600 m, T = 0.3 s and 20-40 m/s2 of drone evasion leave 0.9-1.8 m of unpredictable miss against a 2-4 m radius, so P ~ 0.4-0.7 per poke; at 1,100 m, P ~ 0-0.3 [calc]. Independent tries, unlike the correlated salvo. The earlier PD sim (charger kamikazes) showed no gain, so test against harassers before adopting |
| O3 Decoys (`LargeDecoy`, vanilla, 5 PCU) on hull extremities away from turrets, thrusters and power | **owner decision, blueprint only, not in this checklist** | turrets/AI targeting prefer decoys [data: wiki via `sim/src/Combat/Decoys.cs`]; useless against a hand-aimed drone |
| O4 B1 single shots at a lone harasser instead of the salvo | out (phase 2 alternative) | needs a BATTERY-block change; O2 reuses the tested PD path |
| O5 AI Offensive block as an extra detector while a target is held (`SearchEnemyComponent.FoundEnemyId`, TargetPriority Closest), or an AI-search burst in the recharge window | **out (phase 3)** | the API member is listed in `docs/audit/sensors_igc_ai.md` but gives an id, not a position; whether the block searches while its Flight partner is off is [unverified]. Probe in game first |
| O6 Peeper as a picket designator (its no-ammo turret near the Ballista); armed probes from the foundry | **out** | needs Peeper code + PCU (the probe alive is ~9,300 of 10,000) |
| O7 Share harassers with wingmates on `BALLISTA_TGT` | out (phase 3) | wingmate Helms would add them as contacts; needs their own Lookout to flag them |

## 5. Where the code lives

**Decision: the module goes in the spare PB; the Helm gets a small hook block.**
* The Helm is at **86,228** minified characters of its 90,000 build cap [measured]. The module (classifier, ray tracker, planner, screen, Custom Data) is ~15-25 k, so it does not fit the Helm. The hook is ~1.8 k, bringing the Helm to ~88 k.
* Classification and planning are off the hot path. They pick a zero-mean bias that changes every 0.8-2.5 s, so the 2-tick IGC hop plus the Lookout's Update10 cadence (<= 0.2 s) is far below a jink leg.
* Tracking of the TH, lead, aim, the control law, firing and every override stay in the Helm.
* If the hook pushes ShrikeAI over 90,000: trim the hook's status text; **do not raise the cap**. If it is still over, stop and get an Opus plan for moving another Helm module into the Lookout PB.

**PB**:
* `[SH] Ballista Spare PB` at (1, 4, 11) is renamed **`[Lookout][SH] Ballista Lookout`** and runs `scripts/LookoutAI.cs`. Its Custom Data is `[Ship] Tag=BALLISTA` plus `[Harasser Defence]` defaults.
* Name rules:
  * it does not start with `[Staff]` (the Helm would switch HELMLINK on);
  * it does not contain `Hive Mother PB` (the Spotter would take it for the foundry);
  * it does not start with `[SH]` (the Helm's `Own()` lists ignore it).
* The source must not contain `LkInit(` or `TvAim(`: `add_support_pbs` would add a Staff or Thrust PB.
* No new spare is added: +100 PCU would put the ship at 5,551 against the test's 5,600, and the Lookout PB has about 60 k of room for later modules.
* `SCRIPT_LIMITS`: add `"LookoutAI.cs": 80000`; ShrikeAI stays 90000 (update its comment: "+~1,800 HARASSER LINK hook").

**IGC** (all `TransmissionDistance.CurrentConstruct`; key `HDEF<Helm PB EntityId>`; link version 1 in flag bits 8-11 both ways):

| Tag | From -> to | Payload | Rate |
|---|---|---|---|
| `.H2A` | Helm -> Lookout | `MyTuple<int flags, Vector3D hitDir, double hitAge, double railReadyIn, long tgtId, Vector3D tgtPos>` | every 10 ticks, always (inert with no Lookout) |
| `.HTK` | Helm -> Lookout | `MyTuple<long id, Vector3D pos, Vector3D vel, Vector3D fwd, double radius, double age>`, contacts of radius <= 15 m seen <= 1 s ago | every 20 ticks, <= 4 |
| `.A2H` | Lookout -> Helm | `MyTuple<Vector3D bias, Vector3D up, Vector3D nose, int flags, double ttl, long harasserId>` (nose = zero until phase 2) | every Lookout run (6 Hz) |
| `.HAR` | Lookout -> Helm | `MyTuple<long id, Vector3D pos, Vector3D vel, double radius, int state>` (1 suspect, 2 harasser) | every run, <= 3 |

* H2A flags:
  * bit0 Control runs (not MANUAL / AI-flown / IDLE / RTB / ASSIST)
  * bit1 UnderFire
  * bit2 `_aiOn`
  * bit3 bias applied
  * bit4 MANUAL
  * bit5 target radius >= 25
  * bit6 PD battery ready (phase 2)
  * bits 8-11 version
  * bits 16-30 `_hits & 0x7FFF`
* A2H flags:
  * bit0 active
  * bit1 phantom only
  * bit2 suppress the Helm's jink
  * bit3 nose request
  * bit4 harasser window
  * bits 8-11 version
* **Clocks**: each PB integrates its own `_now`, so messages carry **ages and durations, never times**. The receiver stamps the arrival.

**Sync**:
* **Start-up handshake.** The Lookout finds the nearest `[Helm]` PB on its construct, registers `HDEF<id>.H2A` and `.HTK`, and waits for an H2A with version 1. Then it is LINKED and sends A2H every run (zero bias when nothing threatens). The Helm registers `HDEF<Me.EntityId>.A2H` and `.HAR` on its first `HdTick()`.
* **Re-bind on rebuild.** The Lookout re-scans for the Helm every 10 s and after 2 s of H2A silence. A different EntityId means `DisableBroadcastListener` on the old key and register on the new one. A rebuilt Helm has a new EntityId, so its key changes by itself. A rebuilt Lookout just re-links.
* **Run gap / state resend.** A2H, HAR and H2A are full snapshots, no deltas. The Helm drops bias, up and harasser flags `ttl` (0.6 s) after the last A2H, and `HdHar` drops after 1.5 s. The Lookout stops sending after 2 s without H2A and resends everything on the first H2A after that. Lookout `Storage` keeps only the on/off command.
* **One writer.** The Lookout calls no `ThrustOverride*`, `GyroOverride`, `Pitch/Yaw/Roll`, `ApplyAction`, `Shoot*`, `.Enabled =`, `DampenersOverride` or `EnableRaycast`. It only reads turrets, thrusters and the Remote Control, and casts rays (shared charge, bounded by the rules in section 2).

**Helm hook** = new shared block `// >>> HARASSER LINK v1.0` in `tools/hd_block.cs`, `tools/blocks.json` entry `{"file": "tools/hd_block.cs", "scripts": ["ShrikeAI"]}`.
* Fields: `_hdL`, `_hdH`, `_hdKey`, `_hdB`, `_hdU`, `_hdF`, `_hdT`, `_hdTtl`, `Dictionary<long,double> _hdHar`, `_hdWas`.
* Functions:
  * `HdTick()`: read A2H/HAR, send H2A/HTK
  * `bool HdOn`
  * `Vector3D HdWant(Vector3D want, double cap)`
  * `Vector3D HdUp()`
  * `bool HdHar(long id)`
  * `double HdScore(Track t)`
  * `string HdStatus()`
* Hooks in `ShrikeAI.cs`:
  * `HdTick();` after `HlkTick();`
  * `+ HdScore(t)` after `BatKz(t, dist)`
  * `tv = HdHar(_tgt.Id) ? Vector3D.Zero : _tgt.V(_now)`
  * the jink term `(Jink && !_flee && !HdJ() ? _jink : 0)`
  * `want = HdWant(want, cap);` right before the KzWant line
  * `: HdUp()` replacing `: Vector3D.Zero` in the two `up` lines of `Control()`
  * `.Append(HdStatus())` after `KzStatus()`
* The Helm has no new Custom Data keys. Corsair, Warden, Leviathan and Shrike run the same script, have no Lookout, never receive A2H, and so are unchanged in behaviour.

## 6. Custom Data `[Harasser Defence]` (Lookout PB; written on first run; `reload` re-reads, and a changed Custom Data string is re-read every 10 s)

| Key | Default | | Key | Default |
|---|---|---|---|---|
| Enabled | true | | Jink Speed (m/s) | 30 |
| Max Harasser Radius (m) | 15 | | Jink Period Min / Max (s) | 0.8 / 2.5 |
| Band Min / Max (m) | 150 / 1600 | | Harasser Projectile Speed (m/s) | 800 |
| Dwell (s) | 3 | | Roll To Threat | true |
| Latch (s) | 15 | | Press Beyond Turret Range | false |
| Nose-On Angle (deg) | 12 | | Press Speed (m/s) | 20 |
| Damage Bearing Match (deg) | 60 | | Turret Range (m) | 800 |
| Phantom From Damage | true | | Track Rays / Camera Charge Reserve (m) | true / 600 |
| Phantom Range (m) | 1000 | | Harasser Window (phase 2) / Window Margin (s) | false / 5 |

The standoff and rail range are read from the Helm PB's Custom Data.

**Commands:** `on | off | reload | reset | dump`.

**Lookout screen:**
```
LOOKOUT v0.1  link OK 6 Hz  (applied | held: MANUAL/AI/IDLE | no Helm | version?)
Threat: HARASSER 640 m brg 072/+35 r 3 m nose-on 2.1 s dmg  | PHANTOM brg 110/-20 | none
Evade : corner +F+R-U 37 m/s2  leg 1.2 s  roll 15 deg (frozen: salvo)  press off
Tracks: 3 small (1 harasser, 1 suspect)  rays 2.7/s  turrets on it 2/4
```
**Helm line** (`HdStatus`, only once linked): `Harasser: none`, `!! HARASSER 640 m (evading)`, `!! FIRE FROM 072 deg (unseen, evading)` or `Lookout: silent`.

## 7. Test plan

**Build** (fast, no zips): `python3 -I tools/build_all.py --no-zip --only "Ballista" "Hornet" "Longbow S"`, then `bash sim/build.sh`.

**Scenarios.** Driver `tools/ballista_harasser_ab.py`, on the pattern of `tools/ballista_charge_sweep.py`. Each run is `SimHost.exe one --scenario ... --enemies pc/enemies --enemy-brain hold --start-distance 1600:1600 --cap-seconds 180 --seed-index K`. Arm A adds `--pb-custom-data "Lookout=[Harasser Defence]\nEnabled=false"`; arm B is the defaults.

| Id | Scenario | Stand-in harasser | Tests |
|---|---|---|---|
| S1 | `ship/Ballista-vs-Tech Hunter+Hornet` | Hornet, real script, 2 gatlings, standoff 350 m (inside turret range); starts beside the held TH, so it is "launched" | target stays the TH; dodge; turrets kill |
| S2 | `ship/Ballista-vs-Tech Hunter+Longbow S` + `--commands "1:Flight:range 1100"` (check that the PB is named `[Flight] Longbow S PB` and that the default start command is hunt) | small railgun, 1,400 m, forced to 1,100 m, beyond the turrets | PHANTOM path; dodge against a long gun |
| S3 | `ship/Ballista-vs-Tech Hunter` held 2.2 km; the 3e set "TH + 3 Hornet chargers"; one Corsair scenario | none | regression: identical or near (no small loiterers; chargers stay KZ; Corsair has no Lookout) |

**Metrics** (run JSON):
* the harasser's `shots` / `hits` on the Ballista (hit rate per shot is the primary metric);
* Ballista `damage_taken`, `blocks_lost`, `alive`;
* the harasser's `alive` and time of death;
* the TH's `damage_taken` (salvo cadence kept);
* Ballista `min_enemy_dist_m` to the TH (>= 1,400 m).

**Seeds** (CLAUDE.md):
* Smoke: 3 seeds per arm per scenario (12 runs), to check that it runs and to read the echo screens (`--echo-every 5 --echo-ship 0`).
* A/B: **10 seeds per arm on S1 and S2 (40 runs)**. The predicted effect is large (2-5x the miss distance, section 3.1), and S2 gives ~9 slugs per run, so 90 per arm and about 5 % standard error on the hit rate. Go to 30 seeds only on a scenario whose result is ambiguous.
* S3: 3 seeds each; expect the same outcomes as main.

**Pass:**
* S2: B's hit rate <= 0.6 x A's, and the TH is never closer than 1,400 m.
* S1: B is no worse than A in damage taken; the TH stays the target (Helm echo `TGT`); the Hornet's time to death is not more than 20 % longer.
* Both: the TH's damage taken drops by no more than 15 %.

**What the stand-in cannot show:**
* the real vanilla turret's lead (the sim uses first-order lead) and hull masking of the turret arcs (the sim checks line of sight with one ray);
* how accurate `_hitDir` is;
* the real drone (unknown weapons and AI; the stand-ins lead with acceleration, LEAD v1.0, which is harder to fool than velocity lead);
* hydrogen use, thruster spool, and the real IGC latency (2 ticks is a sim figure);
* whether the AI Offensive block searches while the ship is engaged.

**Unit / sync tests** (`tools/tests/test_ballista.py` and a new `test_harasser.py`):
1. The Lookout PB is at (1, 4, 11) and named `[Lookout][SH] Ballista Lookout`: not `[Staff]`, no `Hive Mother`, not `[SH]`-first. There are 3 programmed PBs (Helm, Spotter, Lookout; update `test_three_pbs...`, `test_names_the_scripts_rely_on`, `test_scripts_are_embedded`).
2. `"HDEF"`, `.H2A`, `.HTK`, `.A2H` and `.HAR` appear in both `ShrikeAI.cs` and `LookoutAI.cs`, with the four tuple type strings identical in both.
3. One writer: `LookoutAI.cs` contains none of `ThrustOverride`, `GyroOverride`, `ApplyAction`, `Shoot`, `.Enabled =`, `DampenersOverride`, `EnableRaycast`, `.Pitch =`.
4. `LookoutAI.cs` contains neither `LkInit(` nor `TvAim(`. Its size is within `SCRIPT_LIMITS` (80,000), and ShrikeAI is <= 90,000.
5. `python3 -I tools/blocks_sync.py --check` passes (HARASSER LINK only in ShrikeAI).
6. A Python mirror of the corner maths: threat on the body up axis gives 35.2, on the nose 22.3, on a body diagonal 38.6. The TH-band exclusion and the reflect rule drop the right corners. The period clamp holds.
7. `tools/compile_check.sh` and `tools/minify_check.sh` on both scripts; `mono out/sim/SimHost.exe selftest m3` still passes.

**In-game checks for the owner** (in order):
1. Paste. Within 2 s the Lookout screen shows `link OK`, and the Helm screen has no new line until a threat.
2. Bring a hostile small drone inside 800 m (or let the Tech Hunter launch one):
   * the Lookout goes SUSPECT, then HARASSER;
   * the ship rolls so the drone is above or below it, and the turrets fire;
   * the Helm keeps `TGT` on the Tech Hunter and does not fire the salvo at the drone.
3. A drone firing from beyond 800 m: `FIRE FROM ... (unseen)` appears within a second of the first hits, and visible direction changes follow every 1-2 s. The ship stays 1,400 m or more from the Tech Hunter.
4. Compare damage over 60 s with `off` sent to the Lookout and with it `on`.
5. Grind and reweld the Helm PB: the Lookout re-links within 10 s. Recompile the Lookout: it resumes.
6. Report the drone's weapons, the range it kept, whether the turrets hit it, and which blocks went first.

## 8. Implementation checklist (Sonnet-sized steps, in order)

1. `tools/hd_block.cs`: the HARASSER LINK block (section 5: fields, `HdTick`, `HdWant`, `HdUp`, `HdHar`, `HdScore`, `HdStatus`), with a header that lists the hooks. Add it to `tools/blocks.json`, then `python3 -I tools/blocks_sync.py --apply`.
2. `scripts/ShrikeAI.cs`: the 7 hooks of section 5. Measure the minified size (`script_text`): it must be <= 90,000. If not, see section 5 (no cap raise).
3. `scripts/LookoutAI.cs` (new, Update10):
   * `LoLoad` (Custom Data, reading the Helm CD)
   * `LoBlocks` (Helm PB, RC, turrets, cameras, thrusters by `[SH]` prefix and construct)
   * `LoLink` (handshake, rebind)
   * `LoRead` (H2A, HTK, `BALLISTA_TGT`)
   * `LoTurrets`, `LoRays`, `LoClassify` (incl. `KzCpa` copy), `LoPhantom`
   * `LoBox`, `LoPlan` / `LoPick` / `LoRoll`, `LoSend`, `LoShow`, `Command`, `Save` (on/off)
4. `tools/compile_check.sh` and `tools/minify_check.sh` on both scripts.
5. `tools/designs/ballista.py`: rename the spare and embed `LookoutAI.cs`, add `LOOKOUT_DATA` defaults to its Custom Data, `SCRIPTS += ['LookoutAI.cs']`, and update the DOCS text. `tools/designs/_common.py`: the `SCRIPT_LIMITS` entry and comment.
6. Tests of section 7 (1-7). Run `python3 -I tools/tests/test_ballista.py`, the new test and `tools/tests/run_all.py`. Then `run_all.py --update-golden`: the ShrikeAI change alters the Ballista, Corsair, Warden, Leviathan and Shrike blueprints.
7. `tools/ballista_harasser_ab.py`. Build (section 7), smoke 3 seeds, A/B 10 seeds S1/S2, S3 regression 3 seeds. Write the results into a "Results" section of this file with the seeds stated.
8. Docs: `docs/design/ships/Ballista.md` (section 3c spare -> Lookout; a new 3f summary with the in-game checks), and one line in `HANDOFF.md`. Tell the owner the **release zips are not rebuilt**.
9. Commit and push to `main`.

Phase 2 (only after phase 1 is measured): the A2H nose request and window flag in the Helm hook, `PD_RAILS` 3 rails as a sim variant, a 10-seed A/B on S1/S2, adopted only on a gain. Phase 3: O5 probe script in game, O7.

## 9. Risks and open questions

**Risks:**
* `_hitDir` as a bearing may be poor, which weakens PHANTOM.
* The roll could disturb the 0.3 deg fire window; it is frozen 3 s before the salvo, and the S1 TH damage check watches this.
* Continuous corner thrust uses hydrogen (~26 min) [calc] and loads the H2 thrusters that a harasser may be shooting.
* The AI-flown search phase gets no evasion.
* A turreted drone gives no nose-on cue (the damage or persistence rule must classify it).
* The stand-ins lead with acceleration; a velocity-lead player drone is easier to fool than the sim shows.

**Open questions for the owner:**
1. What did the drone carry (gatling / autocannon / rockets / assault cannon / small railgun), and what range did it keep: inside or outside 800 m?
2. Did the 4 turrets fire at it? What died first: turrets, thrusters, power?
3. What did the Helm screen show: `TGT` on the drone or on the Tech Hunter, `UNDER FIRE`? Did the Ballista fire its salvo at the drone or run from it?
4. Was the drone AI-flown (AI blocks) or hand-flown? Decoys (O3) only fool AI targeting.
5. One drone or several? (The planner handles up to 3 bearings.)
6. OK to use the spare PB for the Lookout (no empty spare left)? Phase 2: accept 3 rails out of the salvo for PD pokes?


## 10. Results (phase 1, 2026-10-14; [sim] stand-in, nothing run in the game)

Driver `tools/ballista_harasser_ab.py` (Ballista vs a held Tech Hunter at 1,600 m plus a Hornet, 180 s, **10 seeds per arm**, 40 runs; chosen because the arms differ by tens of percent and the stand-in is rough). The Spotter's Custom Data switches the arm.
The Hornet is a stand-in: gatlings, leads with acceleration, keeps about 600-700 m. The vanilla turret model is first-order and the rotor turrets are inert in the sim (no rotor model), so nobody kills the Hornet here (0 of 40).

| arm | what the Ballista does | Ballista damage taken | Hornet shots | Hornet hit rate |
|---|---|---|---|---|
| off | module disabled: the harasser becomes the Helm's target, it copies its velocity, flees to 900 m (the behaviour before this change) | 36,700 | 1,800 | 56 % |
| jink | corner jink only (`Away Speed=0`) | 54,700 | 3,522 | 77 % |
| kite | opens the range only (`Jink Speed=0`) | 38,600 | 4,696 | 20 % |
| **on (default)** | corner jink + kite, the Tech Hunter stays the target | **31,400** | 1,143 | **35 %** |

* The jink alone is worse than doing nothing against this stand-in: the Hornet leads with acceleration and the hull is 30-50 m across, so a 30 m/s velocity step changes little at 600 m, while keeping the Tech Hunter as the target stops the old flight away from the drone. Kiting does the work (the drone needs time to catch up and shoots less); the jink on top of it lowers the hit rate again.
* The gain over the old behaviour is modest (-15 % damage, -37 % hit rate), and it is a gain only because the Tech Hunter stays the target. A first 6-seed, 90 s run (arms on / off / kite / jink: 14.5 k / 27.0 k / 31.2 k / 29.6 k damage) looked better; the 180 s, 10-seed run is the one to trust.
* Regression: Ballista vs the Tech Hunter alone, 3 seeds, module on and off: identical (shots, hits, damage).
* [unverified] everything that matters in the game: the real drone's weapon and speed, whether `_hitDir` is a good bearing, whether the rotor turrets and the one vanilla turret see and hit a drone that stays at 500-700 m (the turrets are what can kill it).

## 11. In-game checks for the owner

1. Paste. The Spotter PB screen shows `LOOKOUT link OK` within a few seconds (it needs the `[Helm]` PB on the same construct). If it says `waiting for the Helm` the Helm did not send `.H2A` (no `[Spotter]` PB seen), `link version?` means the scripts differ.
2. Bring a hostile small drone inside 800 m: `Threat HARASSER <range> m` after the 3 s dwell and 5 s of staying inside 800 m (or at once on a hit from its direction); the Helm screen shows `!! HARASSER (evading)` and `TGT` stays on the large ship; the Ballista moves away from the drone along its thrust box corners and rolls so the drone is above or below.
3. Damage from a direction with nothing tracked there: `FIRE FROM <bearing> deg (unseen)` and the same dodge.
4. `lookout off` on the Spotter PB: the old behaviour (the drone becomes the Helm's target).
5. Report: the drone's weapons, its range, whether `[TU]` turrets and the vanilla turret fired, which blocks went first, the Spotter `Ins` line.

## 12. Warden, Leviathan and Corsair (2026-10-14, owner: "implement harasser defence on the big ships")

* **Where it runs.** The Lookout is now the shared block `LOOKOUT` (`tools/lookout_block.cs`, `tools/blocks.json`), in `SpotterAI.cs` (Ballista, on by default) and in `StaffAI.cs` (off by default: the Shrike's Staff PB must not dodge small craft, they are its targets). The Warden, Leviathan and Corsair blueprints switch it on in their `[Staff]` PB's Custom Data (`[Harasser Defence] Enabled=true`). No new PB, no PCU. A Staff copy stands down while a `[Spotter]` PB is on the ship, so the Ballista still has exactly one Lookout. `HARASSER LINK` v1.1 in the Helm sends to a `[Spotter]` or a `[Staff]` PB. `Max Fire Range (m)` is now a key (was the constant 1700): Warden and Leviathan 1700 (large railguns, 2,000 m), Corsair 1300 (small railguns, 1,400 m).
* **Tuning.** Warden and Leviathan: `Jink Speed (m/s)=10`, `Away Speed (m/s)=15` (slow ships: 1.6 m/s2 on most axes, a 30 m/s jink is never reached and the kite would drag them off their fight). Corsair: the defaults (30 / 40).
* **Sim A/B** [stand-in; Tech Hunter held at 1.6 km + a Hornet harasser, 90 s cap, 6 seeds per row; the vanilla turret model never killed the Hornet in any row]:

  | Ship | Lookout | damage taken avg | harasser hit rate |
  |---|---|---|---|
  | Warden | off | 200 k | 80 % |
  | Warden | on, default dodge | 175 k | 79 % |
  | Warden | on, no dodge (targeting rule only) | 175 k | 80 % |
  | Warden | on, 10 / 15 (shipped) | 180 k | 79 % |
  | Corsair | off | 23.4 k | 91 % |
  | Corsair | on, default (shipped) | 25.8 k | 74 % |
  | Leviathan | off | 1.17 M | 95 % |
  | Leviathan | on, default dodge | 1.18 M | 97 % |
  | Leviathan | on, 10 / 15 (shipped) | 1.14 M | 92 % |
  | Ballista (check after the move) | off / on | 28.2 k / 14.6 k | 46 % / 29 % |

  Reading: on the Warden the gain (about -10 to -13 % damage) comes from keeping the large target, not from the dodge. The Corsair is hit less often but takes about the same damage. The Leviathan (222 m, slow) cannot be missed and gains nothing measurable; it relies on its turrets, whose stand-in model is weak. Nothing verified in the game; in-game checks as section 11, on the Staff PB screen (`LOOKOUT link OK`).
