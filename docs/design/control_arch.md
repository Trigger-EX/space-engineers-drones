# control_arch: script and control architecture for autonomous AND hand-flown warships

Key `control_arch`. Read-only study; this file is the only repo change. Evidence labels: [data] repo game data / owner's blueprints, [meas] measured here with `tools/minify_cs.py` (my per-member grouping, +-1 %), [pbapi] https://malforge.github.io/spaceengineers/pbapi/ (fetched today), [wiki], [rec] recollection with confidence, [sim] read from `sim/`. Builds on `docs/audit/timers_pbs.md` (s2.1, s7), `docs/design/grids_bays.md`, `docs/follow-mode.md`, `docs/hive-protocol.md`.

## 0. Decisions in one table

| # | Decision |
|---|---|
| D1 | Base the ship "Helm" on **HornetDroneAI** (91.4 k minified), not Basilisk (99.8 k) / Wasp (97.7 k) / Longbow (94.2 k) [meas]. Those three have no room for new code; rocket and railgun logic is transplanted out of them into one new shared block `BATTERY`. |
| D2 | PB split: **Fighter 1 PB (Helm); Corvette 2 (Helm, Staff); Frigate 3 (Helm, Staff, Pod); Dreadnought 5 (Helm, Gunnery, Staff, Pod x2)**. Every PB <= 80 k minified, each owns one job, Helm is the only writer of thruster/gyro overrides. |
| D3 | Manual flight = a **mode of Helm, not another script**. Helm keeps running at Update1 while a pilot sits; it releases all overrides on seat entry, keeps tracking, turret management and (optionally) fly-by-wire aim assist; Staff keeps the wing and bays running. |
| D4 | Helm <-> Staff <-> Gunnery <-> Pod traffic is **IGC with `TransmissionDistance.CurrentConstruct`** (never reaches other ships, so no secret needed, unlike FLEET). Tags `<T>.H2S`, `.S2H`, `.H2G`, `.G2H`, `.S2P`, `.P2S`; payloads <= 6-item `MyTuple` (the repo's stubs and the game stop at 6). |
| D5 | Fire control = **weapon batteries** by name tag (`[B1]`..`[B4]`), class auto-detected from block subtype, **one lead solver per class** (BALLISTIC, ROCKET, RAIL, SHELL). The battery with the highest `dps x P(hit)` at the current range ("primary") decides ship orientation; others fire when their own boresight is inside their window. Turrets and point defence are configured, never steered. |
| D6 | Command surface for the pilot: cockpit toolbar -> PB `Run` with argument (Parameters XML **unverified, never in a game file**), fallback = one Button Panel / Timer per command, last fallback = typing the argument in the PB terminal. |
| D7 | Sim needs: a `Cockpit` block with `IsUnderControl` + move/rotation indicators and a **scripted pilot**, pilot input in `Physics` that yields to overrides block by block, Timer/EC blocks, per-PB scenario commands and CustomData, enemy scenarios that run the blueprint's own scripts, the cradle merge pass. About 1,350 lines in total (s6). |
| D8 | Plan: M0 sim/generator groundwork, M1 Shrike (fighter) one PB + manual mode, M2 Helm/Staff split + deadman, M3 multi-weapon fire control, M4 Staff wing + mothership link, M5 cradle bays, M6 corvette/frigate/dreadnought assembly. Each is testable with 3-20 seeds (s7). |

---

## 1. PB split, modules, sizes, IGC

### 1.1 What exists (measured)

Minified size of the whole script and of each module, grouped by member name prefix [meas] (`tools/minify_cs.py` on every top-level member; group sums are +-1 % of the file total because members are chunked):

| Group (chars) | Hornet | Wasp | Basilisk | Longbow |
|---|---|---|---|---|
| **whole script, minified** | **91,125** | 97,428 | 99,834 | 93,957 |
| LAUNCH (`Ln*`, `LaunchStep`) | 8,754 | 8,754 | 8,754 | 8,754 |
| FOLLOW (`Fo*`, `Follow*`) | 8,659 | 8,659 | 8,659 | 8,659 |
| KAMIKAZE (`Kz*`) | 6,811 | 6,811 | 6,811 | 6,811 |
| TRACKING (lock, prune, contacts, assess) | 7,762 | 7,720 | 7,733 | 7,606 |
| SENSORS/SCAN (omni, auto-scan, cams, sensors) | 5,537 | 5,537 | 5,537 | 5,524 |
| LEAD + `Track` | 5,432 | 5,493 | 5,493 | 5,926 |
| AIM/BREACH | 5,082 | 5,082 | 5,082 | 5,166 |
| CONTROL (`Control`, `ApplyThrust`, `SetFire`, `Separation`...) | 6,784 | 6,461 | 7,095 | 7,632 |
| CORE (`Tick`, `Command`, `GetBlocks`, `Program`) | 5,691 | 5,710 | 5,713 | 5,807 |
| CONFIG (`LoadConfig`) | 4,155 | 5,704 | 6,039 | 4,302 |
| AIBLOCK (`AiStep`, `FindAi`, `PatrolStep`) | 3,797 | 4,429 | 4,503 | 2,077 |
| FLEET | 3,210 | 3,210 | 3,210 | 3,210 |
| DISPLAY | 2,783 | 2,880 | 2,886 | 2,696 |
| AIGATE | 2,477 | 2,477 | 2,477 | 2,477 |
| IGC glue (`RegisterFeeds`, `ReadIGC`, `BroadcastStatus`) | 2,329 | 2,329 | 2,329 | 2,024 |
| SEEKER | 2,105 | 2,105 | 2,105 | 2,105 |
| field/state declarations | 7,732 | 8,699 | 8,919 | 7,601 |
| weapon-specific (rocket gate/path/ripple; railgun `UpdateGuns`/`DetectWeapon`/`EngageRange`) | 0 | ~3.0 k | ~3.7 k | ~2.4 k |

Other modules: `HornetMothership.cs` 5,332; `HiveMotherAI.cs` 33,710 (Bay class 1.1 k, `StepBay` 3.0 k, `FindDrone` 1.0 k, `Release` 0.7 k, `Arm` 1.1 k, `GetBlocks` 3.5 k, `Show` 1.7 k, `Command` 0.8 k, `LoadConfig` 1.8 k, printing-only parts ~16 k); `tools/*_block.cs`: aim 4,275, ai_gate 2,739, fleet 3,603, follow 8,999, kz 7,462, launch 9,290, lead 2,729.

Two facts that shape everything:
1. **LAUNCH and FOLLOW are drone-side** (the drone takes off from a cradle / follows an owner). A warship must not carry them: -17.4 k at once. What the warship needs is the **other end**: the HOME/HOMER broadcast (Mothership link) and the bay release, both small.
2. The shared blocks are copy-paste modules with hooks into script-local names (`Track`, `_contacts`, `_mates`, `_rc`, `Command`). They can be reused verbatim only inside a script that has those names, i.e. **Helm is a Hornet-derived script**; Staff/Gunnery/Pod reuse the generic blocks (FLEET, Mothership, MyIni helpers) and share only data over IGC.

### 1.2 Module lists and size budgets (all numbers minified chars, estimates marked ~)

Rule: every PB <= 80,000 (hard cap 85,000, then something moves out). New code estimates are my own sizing from comparable existing members (e.g. `UpdateGuns` 838 + `ReadyGun` + `DetectWeapon` 576 for RAIL; Basilisk `RktPath`+`Lead`+`FireGate`+`FireRockets`+`MatesInTheWay` ~3.7 k for ROCKET).

**Fighter, "Shrike" (small grid, ONE PB "Helm", `Update1`)**

| Module | chars | Origin |
|---|---:|---|
| Hornet minus LAUNCH, FOLLOW, SEEKER | 71,872 | verbatim (91,125 - 8,754 - 8,659 - 2,105 + 265 glue) |
| MANUAL (pilot detect, mode FSM, release/re-acquire, HUD, FBW assist) | ~4,500 | new (s2) |
| BATTERY-lite (2 batteries: gatling + one slow class, shared solver table) | ~4,000 | new + ported (`DetectWeapon`, `UpdateGuns`) |
| DEADMAN heartbeat (timer re-arm) | ~500 | new |
| DISPLAY replaced by the compact cockpit HUD | -1,500 | new text builder shrinks Display |
| **Total** | **~79.4 k** | tight; first thing to drop is AIBLOCK (-3.8 k) |

Keeps FLEET (3.2 k), so the fighter can be a wingman of the dreadnought (`fleet hunt`, target sharing) and KAMIKAZE (6.8 k).

**Corvette (small grid, 2 PBs)**

| PB | Update | Modules | chars |
|---|---|---|---:|
| Helm | 1 | Hornet core - LAUNCH - FOLLOW - FLEET - SEEKER - DISPLAY - AIBLOCK/AIGATE (6.3 k) + MANUAL 4.5 k + BATTERY 10 k + HELMLINK 2.0 k + HUD 1.5 k | ~74 k |
| Staff | 10 + IGC callback | FLEET (3.6) + Mothership link (HOME/HOMER/turret feed, 1.7) + wing orders (1.0) + CradleLite for 2 slots (~6.5, cut from HiveMother) + deadman watch + Staff display + toolbar `Command()` | ~22 k |

**Frigate (large grid, 3 PBs)**: Helm same as corvette (~74 k; battery manager with 3-4 batteries); Staff ~26 k (adds hangar doors, pod commands, power plan); **Pod PB** (in the docked pod, own construct, `Update10`) = CradleBay for 4 slots ~12 k.

**Dreadnought (large grid, 5 PBs)**

| PB | Update | Job | chars |
|---|---|---|---:|
| Helm | 1 | flight, tracking, the **primary battery's** lead + ship orientation, KAMIKAZE, MANUAL | ~72 k |
| Gunnery | 1 | secondary batteries (windows, ripple, rail FSMs), turret/PD configuration, ammo/ready table | ~32 k |
| Staff | 10 | wing + hangars + mothership link + HUD pages + doors + power plan | ~28 k |
| Pod x2 | 10 | CradleBay per hangar | ~12 k each |

Why Gunnery exists only on the dreadnought: Helm's instruction budget (50,000 per run, own per PB [KS], timers_pbs s1.3) and the 100 k limit, not latency. The aim loop must not cross IGC (1-2 ticks, s1.3 below); Gunnery therefore never steers: it reads the real weapon `WorldMatrix` of its own grid (same construct, zero latency) and only needs the target track, which tolerates 50-100 ms.

PCU for the control layer (data: PB 100, Timer 25, EC 10, ButtonPanel 5): fighter 100; corvette 2 PB + 3 timers + 2 EC = 295; frigate 3 PB + 5 timers + 3 EC = 455; dreadnought 5 PB + 8 timers + 5 EC + panels = ~800.

### 1.3 IGC between PBs on one grid

Facts used: `TransmissionDistance.CurrentConstruct` keeps traffic on the construct without antenna [wiki Antenna_Communication_(IGC), rec high]; the sim implements it as same-`Body` and a unicast always as `AntennaRelay` but same-body is reachable [sim `Igc.cs` Reachable/EnqueueUnicast]; delivery in the sim is next tick, in the game 1-2 ticks [rec medium]; the sender does not receive its own broadcast [sim, rec high]; listener queue 25 `[I]` in the sim (`IgcMaxWaitingMessages`), real limit undocumented; payloads must be immutable [sim `ImmutableTypes`, game]. Never `TryRun` between PBs (nested budget unknown, timers_pbs 1.3).

`<T>` = `[Ship] Tag` in each PB's Custom Data (default `SHIP`). Broadcast + `CurrentConstruct` so no addresses to configure; PBs identify each other by role in the payload.

| Tag | Dir | Payload | Rate | Content |
|---|---|---|---|---|
| `<T>.H2S` | Helm -> Staff | `MyTuple<int flags, long tgtId, Vector3D tgtPos, Vector3D tgtVel, float hullRadius, float batt>` | 5 Hz (every 12th tick) and on change | flags: bits0-2 mode (0 IDLE 1 GUARD 2 HUNT 3 FOLLOW 4 RTB 5 MANUAL 6 ASSIST), bit3 pilot seated, bit4 firing, bit5 kamikaze alarm, bit6 deadman armed, bit7 degraded (no thrust/gyro) |
| `<T>.S2H` | Staff -> Helm | `MyTuple<string cmd, Vector3D rally, double radius>` | on change + 1 Hz refresh | cmd = the same verbs as `Command()`; rally = fleet rally/HOME to follow when in FOLLOW |
| `<T>.HB` | Helm -> Staff, Gunnery | `long tick` | 1 Hz | heartbeat, Staff trips if silent 1.5 s (s2.6) |
| `<T>.TRK` | Helm -> Gunnery | `MyTuple<long id, Vector3D pos, Vector3D vel, Vector3D acc, double radius, float ageMs>` | 20 Hz for the selected target + 2 Hz per other contact (max 8) | tracks incl. the LEAD block's fresh acceleration |
| `<T>.G2H` | Gunnery -> Helm | `MyTuple<int ready, int battery, float dpsNow, float rangeEff, float bestWindowDeg>` | 5 Hz | per-battery readiness for the primary-battery choice (s3.3) |
| `<T>.S2P` | Staff -> Pod | `string` | on command | `release <n\|all> [delay]`, `hold n`, `recall`, `status`, `doors open\|close` |
| `<T>.P2S` | Pod -> Staff | `MyTuple<int bay, string state, long droneId, float batt>` | on change + 0.5 Hz | states EMPTY DOCKED CHARGING READY ARMING RELEASED COOLDOWN FAULT (grids_bays 6.1) |
| `<T>.PILOT` | Helm -> all | `MyTuple<int mode, int seated>` | on change | so Staff/Gunnery switch their own behaviour without reading the seat |

A docked pod is a different construct (grids_bays 6.2): Staff <-> Pod uses `ConnectedConstructs` (rec 85 %, in-game test C4); the fallback is a one-time `Connect()` plus an antenna pair on a private tag.

The existing per-type channels stay untouched: Helm still listens/sends `FLEET_TGT/_CMD/_HOME` and the drones' `<TAG>_*` through the FLEET block, which on the corvette/frigate/dreadnought moves into **Staff** (it owns the wing). Staff does not relay wing contacts: Helm's own `ReadIGC` keeps its listener on `FLEET_TGT` (about 0.4 k); only the send/command half of FLEET moves to Staff.

Latency budget: Helm <-> Staff 17-33 ms, harmless; the hot path (track -> lead -> gyro/fire) stays inside Helm (primary battery) and, for secondaries, inside Gunnery against the live weapon matrices.

### 1.4 Reused verbatim vs new

| Item | Verdict |
|---|---|
| `Track`, LEAD block, `AimOffset`, BREACH/AIM block, SENSORS/SCAN, TRACKING, KAMIKAZE, AIGATE, CORE, `Aim`, `ApplyThrust`, `Separation`, `Evade` | verbatim from HornetDroneAI (Helm is a copy-edit, kept in sync by the existing `*_sync.py` with `Shrike*.cs` and `Helm*.cs` added to their SCRIPTS lists) |
| FLEET block | verbatim in Helm (listen) and Staff (send/commands); Fighter keeps both |
| `HornetMothership.cs` `Refresh/Send/Main` | merged verbatim into Staff (1.7 k beyond the FLEET block) |
| `HiveMotherAI` `Bay`, `ParseBox`, `FindDrone`, `Release`, `Arm`, `StepBay` (DOCKED/ARM/RELEASE/COOLDOWN/FAULT only), `Diagnose`, `Command`, `Show`, `Err` | cut down into `CradleBay` (~12 k); projector/welder/stock/FINISH/CHARGE-from-print dropped |
| LAUNCH, FOLLOW | **not** in warships (drone-side) |
| Basilisk `RktPath`, rocket `Lead`, `FireGate`, `FireRockets`, `MatesInTheWay`; Longbow `DetectWeapon`, `UpdateGuns`, `ReadyGun`, `EngageRange` | ported into `BATTERY` (one copy, parametrised by class) |
| **New**: `MANUAL`, `BATTERY`, `HELMLINK` (IGC H2S/S2H/HB/TRK), compact HUD, Staff wing/bay/door glue, Gunnery, Pod glue, DEADMAN | new shared blocks `tools/manual_block.cs`, `tools/battery_block.cs`, `tools/helmlink_block.cs` + sync scripts; generalise the 7 `*_sync.py` into one `tools/blocks_sync.py` driven by `tools/blocks.json` (block -> script list), because the SCRIPTS lists are hard-coded in each file today |

C# 6 / PB constraints (carry over from the existing scripts, `sim/build.sh` compiles with `mcs -langversion:6`): C# 6 features (`?.`, `$""`, `nameof`, expression-bodied members) are fine, but **no C# 7** (no tuples `(a,b)`, no `out var`, no local functions, no pattern `is T t`, no expression-bodied accessors beyond C# 6), no `async`, no reflection, no `System.IO`/`Thread`, only `System.Linq`/Collections/Text/`Regex`; use `MyTuple` for IGC; avoid LINQ in per-tick code (allocations); no `static` mutable state shared across PBs (each PB is a separate instance); `Echo` max 8,000 chars; every optional stage guarded by `Runtime.CurrentInstructionCount < 0.6 * Runtime.MaxInstructionCount` (audit item 2.2.1, 20 lines); `Main` try/catch that **releases overrides on any exception** (today it only prints; audit TOP RISK 3).

---

## 2. Manual piloting

### 2.1 API facts relied on

[pbapi IMyShipController, fetched]: `IsUnderControl` "whether the block is controlled locally or remotely"; `CanControlShip` "whether this specific controller can control the ship"; `ControlThrusters` get/set "whether this controller drives the thrusters"; `DampenersOverride` get/set; `MoveIndicator`, `RotationIndicator`, `RollIndicator` "directional / pitch+yaw / roll input from the user or autopilot; values can be very large at high controller sensitivity" (so **normalise and clamp**); `IsMainCockpit` get/set; `HandBrake`, `ControlWheels`; `GetShipVelocities`, `GetNaturalGravity`. [pbapi IMyCockpit]: `IMyCockpit : IMyShipController, IMyTextSurfaceProvider` (`SurfaceCount`, `GetSurface(i)`), no own methods. [rec high] types: `MoveIndicator` Vector3 (x right+, y up+, z backward+, so W = z -1), `RotationIndicator` Vector2 (x pitch from mouse Y, y yaw from mouse X), `RollIndicator` float (Q/E); **signs and scale are to be measured in game (test K1)**. [rec medium] `CanControlShip` is false for passenger seats (`PassengerSeat*`), true for flight seats, and **true** for Remote Control blocks (a RC is `IsUnderControl` only while someone remote-controls it). [data] cockpit XML carries `MyMultiTextPanelComponent` with N `MySerializedTextPanelData`: 6 surfaces on `DBSmallBlockFighterCockpit` and `LargeBlockCockpitSeat`, 5 on `OpenCockpitLarge`, 4 on `SmallBlockCockpit`, 1 on `CockpitOpen`/`OpenCockpitSmall`; the cockpit toolbar is `<ToolbarType>Character</ToolbarType>` with `ToolbarItemWeapon`, `ToolbarItemTerminalBlock`, `ToolbarItemTerminalGroup` items (Pod Chaser sample).

Thruster/gyro override semantics the design depends on (already relied on by `ApplyThrust`/`Aim`, [sim] `Physics.cs` and `SelfTestA`): an overridden thruster (`ThrustOverridePercentage > 0`) ignores pilot input and is excluded from dampeners; `GyroOverride = true` makes that gyro ignore pilot rotation. Overrides live in the blocks and **survive the death of the PB** (audit).

### 2.2 Seat detection

```csharp
// cockpits = IMyShipController with CanControlShip, EXCLUDING IMyRemoteControl (the RC is the script's own reference)
List<IMyCockpit> _seats;                       // filled in GetBlocks: IsSameConstructAs(Me) && c.CanControlShip
bool PilotSeated() { foreach (var c in _seats) if (c.IsFunctional && c.IsUnderControl) return true; return false; }
```
Helm keeps `_rc` = the Remote Control (as today, for mass, velocity, orientation) **and** `_seats`. Ships with only a cockpit use the main cockpit (`IsMainCockpit`) as reference. Pod/passenger seats are excluded by `CanControlShip` and by an optional name tag `[Helm Seat]`.

### 2.3 Mode machine

```
            seat taken >= 0.4 s                    seat empty >= 1.5 s (Release Delay)
 AUTO modes ---------------------->  MANUAL  --------------------------------------> back to _autoMode
 (GUARD/HUNT/FOLLOW/RTB)           (ASSIST when "assist" or Auto Assist=true)        (default GUARD; Storage-saved)
 command "manual" / "auto" / "pilot toggle" work with no seat (toggle, EC/Timer backup, s2.7)
```
- `_pilot` debounced; `_forced` set by `manual`/`auto` verbs overrides the seat (a human can stand next to the console and fly with a Remote Control seat, or a pilot can sit and say `auto` to let the ship fly itself with him aboard: `auto` while seated = AUTO with a HUD).
- Persisted in `Storage` (`Save()` already writes mode): `_autoMode`, `_userDamp`.

### 2.4 Handover without fighting

Single-writer rule: Helm is the only script that touches `GyroOverride`, `ThrustOverridePercentage`, `DampenersOverride`; Staff/timers only *cut* (deadman).

On entry to MANUAL (same tick, before any other stage):
1. `ReleaseGyros()` (exists: `GyroOverride=false`, `Pitch=Yaw=Roll=0`), all `t.ThrustOverridePercentage = 0` (this is `Release()` minus `SetFire(false)`; the new `ReleaseControl()` must **not** send `Shoot_Off`, otherwise guns the pilot toggled are silenced).
2. `_rc.DampenersOverride = _userDamp` (remembered when AUTO was entered; default true). `Release()` today forces `true`; AUTO sets false in `ApplyThrust`. The pilot's own V-key state is therefore restored, not guessed.
3. Reset estimator state: `_havePrev=false; _prevErr=0` (the PD derivative would spike, there is already a `derr > 20` guard).
4. Leave `ControlThrusters` as it is (true on the flight seats). It is a per-cockpit property, setting it false on the seat would make the pilot's thrust input dead; only the RC (never driven by a player) could be set false. Rule: never write `ControlThrusters` in Helm; document it for the generator (all seats true).
5. Turn the AI blocks (AIGATE) off: they would grab gyros.

During MANUAL Helm's `Control(dt)` is replaced by `ManualStep(dt)`, which only (a) reads `MoveIndicator/RotationIndicator` for the assist, (b) updates HUD, (c) manages turrets/batteries (below).

On exit: `_userDamp = _rc.DampenersOverride` is saved on entry to AUTO, then `Aim()` takes over from the current heading (the PD starts with `_prevErr=0`, `AimKp 6` for the small drones; for large ships the rate cap `MaxTurn` is the gyro-limited rate, s3.6). Re-acquire delay: `Release Delay` 1.5 s so a quick climb out of the seat does not flip modes; the nose is *not* snapped: a 2 s ramp of `MaxTurn` from the measured current angular rate to the cap.

### 2.5 What keeps running in MANUAL

| Automation | In MANUAL | How |
|---|---|---|
| Vanilla turrets (gatling/rocket/autocannon/artillery/assault) | yes | not script-controlled; their targeting flags are set once by Helm (`TurretSetup`, s3.5) |
| Point defence (InteriorTurret, small gatling turrets on rockets/drones) | yes | same; plus KAMIKAZE assessment keeps running to raise the HUD alarm |
| Tracking, camera scan, sensors, FLEET target feed | yes | the `Tick` front half is unchanged; the contact table feeds the HUD and the assist |
| Targeting assistance | yes | `lock`, `next`, `unlock` from the toolbar; auto-scan sweep **off** unless `Scan In Manual=true` (it rotates the cameras, it never rotates the ship) |
| Fixed-gun aim assist | optional, 3 levels | `assist hud` (default): HUD pip + fire window only; `assist fbw`: fly-by-wire blend; `assist off` |
| Auto-fire of fixed batteries | optional | `Auto Fire In Manual=false` default: the pilot fires with the toolbar; `true`: batteries fire only inside their window |
| Wing orders | yes | Staff is unaffected by the seat; it sees `flags` bit3 on `.H2S`/`.PILOT`; the wing keeps `satellite`/`follow` on the ship's HOME/HOMER |
| Bay launches | yes | toolbar -> Staff `Run release 1` etc. |
| Deadman | armed in AUTO only; in MANUAL replaced by the EC path (s2.6) |

**Fly-by-wire aim assist (`assist fbw`)**, only when a lock exists and the lead solution is valid: `wCmd = Clamp(k_p * RotationIndicator/RollIndicator scaled) + wAssist * g`, where `wAssist` is the usual PD output toward the primary battery's lead direction, `g = clamp(1 - |pilotRate|/Pilot Priority, 0, 1)` (pilot input dominates: any deliberate stick motion lowers the assist to 0 within 0.2 s and it ramps back in 0.5 s after the stick is idle) and the assist is limited to `Assist Max Rate` (0.5 rad/s large ships, 1.5 small). Gyro overrides are all-or-nothing per block, so FBW means Helm *writes* the pilot's rate too: set `GyroOverride=true` on all gyros and compute `w = pilotRateWorld + assist` (pilot rate: `RotationIndicator` x `Pilot Rate Gain`, calibrated by test K2, clamped to `MaxTurn`). Without a lock Helm returns to full release (no override): the pilot flies with native feel; dampeners untouched. This level is **opt-in** because it needs the K1/K2 calibrations.

### 2.6 Dead-man and the seat

Design correction to timers_pbs s2.2/s7: the dead-man must not cut a *piloted* ship.
- **Soft dead-man (PB level, all ships with Staff):** Helm sends `.HB` at 1 Hz; if Staff sees nothing for 1.5 s it clears all overrides on all gyros/thrusters it can see (`GyroOverride=false`, `ThrustOverridePercentage=0`, `DampenersOverride=true`), `Shoot_Off` on fixed guns, and shows `HELM LOST` on the cockpit HUD. Works in AUTO and in MANUAL (in MANUAL there are no overrides unless FBW, so it just frees them). Because it only *releases*, it is correct in every mode.
- **Hard dead-man (Timer, single-PB fighter and as second layer):** Timer D (5 s, Silent) re-armed by Helm every second only in AUTO (`StopCountdown(); StartCountdown();`); its toolbar: thruster group `OnOff_Off`, weapon group `OnOff_Off` (the audit notes no "reset override" action is certain, so it cuts power). On entering MANUAL Helm calls `D.StopCountdown()`. Backup if Helm is already dead: an Event Controller "Cockpit Occupied" (slot 1, occupied) -> `Stop` on Timer D; (slot 2, empty) -> `Start` on Timer D. XML below.

### 2.7 Commands from the cockpit

Verbs added to `Command()` (unknown words are already ignored, so old scripts are unaffected): `manual`, `auto`, `pilot` (toggle), `assist hud|fbw|off`, `fire auto|pilot`, `track`/`lock`/`next`/`unlock` (existing), `standoff <m>` (`range` existing), `mode?` (prints), `wing <cmd>` (Staff relays `fleet <cmd>`), `release <n|all>`, `recall`, `doors open|close`.

Cockpit toolbar -> PB: the PB `Run` action takes an argument in a toolbar item. The Keen PB source accepts a `String` parameter [KS, audit]; no game file in the repo contains it (only `Single`/`Int32` parameters for `SetVelocity`, `RotateToAngle`, `SetOverride`, `SetColor` appear in the 303 toolbars checked [data]). So the item shape is **hypothesised by analogy** (confidence ~70 %), one saved sample from the game settles it (test K3):

```xml
<Slot><Index>10</Index><Item />
  <Data xsi:type="MyObjectBuilder_ToolbarItemTerminalBlock">
    <Action>Run</Action>
    <BlockEntityId>PB_ENTITY_ID</BlockEntityId>
    <Parameters><MyObjectBuilder_ToolbarItemActionParameter>
      <TypeCode>String</TypeCode><Value>assist hud</Value>
    </MyObjectBuilder_ToolbarItemActionParameter></Parameters>
  </Data></Slot>
```
Observed real shapes to copy for the rest [data, Pod Chaser cockpit]: weapon `<Data xsi:type="MyObjectBuilder_ToolbarItemWeapon"><DefinitionId Type="MyObjectBuilder_SmallGatlingGun" Subtype="SmallBlockAutocannon" /></Data>`; block `ToolbarItemTerminalBlock` + `Action` + `BlockEntityId`; group `ToolbarItemTerminalGroup` + `Action` + `GridEntityId 0` + `BlockEntityId` (the owning block's id) + `GroupName`. Event Controller toolbar: `Action TriggerNow`, `Threshold`, `SelectedEvent` (2 = integrity in the Tech Hunter ECs), `SelectedBlocks` list of `<long>`, `ConditionInvert`, `ANDGate` [data].

Toolbar layout (9 slots x pages; page 1 = hand weapons, fixed in the game, so hand-fire stays natural):

| Page | Slots (1-9) |
|---|---|
| 1 weapons | B1, B2, B3 battery groups (`Shoot` on groups `B1`..`B3`), turret group on/off, decoy, dampeners... |
| 2 Helm | `Run` Helm: `auto`, `manual`, `assist hud`, `assist fbw`, `lock`, `next`, `unlock`, `hunt`, `rtb` |
| 3 Wing/Bays (Staff) | `Run` Staff: `wing satellite`, `wing follow`, `wing hunt`, `wing rtb`, `release 1`, `release all`, `recall`, `doors open`, `doors close` |

Fallback ladder if `Run` with Parameters does not save in the game: (1) a Button Panel (`ButtonPanelSmall` 5 PCU, non-DLC) per command, same item; (2) Timer per command (`TriggerNow`), each timer's toolbar a `Run` of the PB (same hypothetical item, so this does not help) — so the real fallback is (3) **argument-less `Run`** that cycles: Helm `Run` with empty argument = `Command("next")` where the PB's `DefaultRunArgument` is edited by hand per PB; and (4) terminal typing. K3 decides. The generator (`sebp.py` `links={'toolbar': [...]}`, timers_pbs 4.1) needs this work anyway.

### 2.8 HUD / LCD

Cockpit surfaces (`IMyTextSurfaceProvider`): the PB writes only the surfaces assigned in its own Custom Data so there is no contention:

```
[Cockpit]
Helm Surface=0        ; main status (text, monospace 0.8)
Staff Surface=1       ; wing + bays
Gunnery Surface=2     ; battery table (dreadnought) / otherwise Helm
Scope Surface=3       ; sprite scope (optional, ContentType SCRIPT + DrawFrame)
```
Helm page (<= 14 lines, 30 Hz is too fast, refresh every 10 ticks in MANUAL and 30 in AUTO):
```
MODE  MANUAL/ASSIST-HUD   AUTO-HUNT   seat OK
TGT   Tech Hunter  L  1432 m  v 12 m/s  lock 0.2s
B1 RAIL  READY  win 0.20deg err 0.6   B2 CANN firing  B3 ROCK 12/19
LEAD  T 0.72 s  pip +1.4/-0.3 deg
PWR 82%  AMMO 71%  THR cap 100%  DAMP on
ALERT KZ 3 incoming 5.1 s
HELM 64% ins 21k  STAFF ok  GUN ok  DEADMAN armed
```
Scope (optional): `surface.ContentType = ContentType.SCRIPT; using (var f = surface.DrawFrame()) { f.Add(new MySprite(SpriteType.TEXTURE, "Circle", ...)); }` showing the target's direction in the primary battery's frame, the lead pip, the fire window ring. 1.5-2 k chars, `DrawFrame`/`MySprite` are not in the repo's `stubs.cs` or the sim (s6). The HUD never blocks: every surface write in `try/catch` (a destroyed cockpit).

---

## 3. Autonomous combat with several weapon classes

### 3.1 Weapon classes [data defs_stats.json + audit weapons.md]

| Class | Blocks (non-DLC) | Muzzle speed / max range | Fire pattern | Existing solver |
|---|---|---|---|---|
| BALLISTIC | `SmallGatlingGun` (gatling), `SmallBlockAutocannon`; turrets `LargeGatlingTurret`, `AutoCannonTurret`, `InteriorTurret` | 800 m/s / 800 m (range randomised 80-100 %: plan for ~650 m for sure hits) | continuous, 600 rpm effective gatling (audit W-02) | Hornet `Lead()` |
| ROCKET | `SmallMissileLauncher`, `LargeMissileLauncher` (burst 19, reload 4 s), `SmallRocketLauncherReload`; turret `LargeMissileTurret` | initial 100, accel 600, desired 200 m/s, 800 m, 4 s flight at 800 m, blast radius 4 m, 500 dmg | `ShootOnce` ripple, salvo, pause | Basilisk/Wasp `RktPath`+`Lead`+`FireGate` |
| RAIL | `SmallRailgun`, `LargeRailgun` (capacitor); no railgun turret | 1000/1400 and 2000/2000 m/s/m; shot delay 0.5/2.0 s, reload (capacitor) | `ShootOnce`, one gun at a time, charge FSM | Longbow `DetectWeapon`/`UpdateGuns`/`ReadyGun` |
| SHELL | fixed `LargeBlockLargeCalibreGun` (artillery), `SmallBlockMediumCalibreGun` (assault); turrets `LargeCalibreTurret`, `LargeBlockMediumCalibreTurret`, `SmallBlockMediumCalibreTurret` | 500 m/s / 2000 m (artillery), 500 / 1400 (assault); ricochet at grazing angles (audit W-08) | burst 1-2 then 6-12 s reload | BALLISTIC solver with `ProjSpeed=500` (no drag in space) |

Gravity drop is not modelled in space; `GravityDrop` stays as in Longbow for planets.

### 3.2 Batteries

Name tag `[B1]`..`[B4]` in the block's `CustomName` (and the same text as terminal group name, so the cockpit toolbar can hold the group). Class and constants are auto-detected from `BlockDefinition.SubtypeId` through one table (extending Longbow's `DetectWeapon`), the same table gives `ProjSpeed`, `Range`, `Reload`, `Burst`. Config per battery in Helm's Custom Data:

```
[Battery 1]
Tag=B1
Class=AUTO              ; BALLISTIC | ROCKET | RAIL | SHELL
Role=PRIMARY_CANDIDATE  ; | SECONDARY | ALWAYS_FIRE
Min Range=200
Max Range=0             ; 0 = class range x 0.85
Window Scale=1.0        ; x silhouette window
Salvo=1
```
Blocks without a tag form battery 0 ("unassigned", class by subtype, grouped by subtype) so unmodified blueprints still work. Turrets (`IMyLargeTurretBase`) and `IMyTurretControlBlock` never belong to a battery: they are **designators and automatic weapons** (existing roles: `PollDesignators` reads them for targets).

### 3.3 One solver per battery, who orients the ship

Per battery b with class solver `S_b` and gun reference position `g_b` (centroid of its working blocks, like `Control()` already does with `_guns`) Helm/Gunnery computes lead point `p_b` and direction `d_b`. Helm computes **score** each 10 ticks:

```
score_b(r) = dps_b * Pready_b * Phit_b(r, T = r / v_b) * [r in [MinRange_b, 0.85 * Range_b]]
Phit_b = clamp(1 - miss/Radius_t, 0, 1),  miss = 0.5 * unc_a * T^2 + disp_b * r   (same "unc" used by FireGate)
```
`dps_b` from data (e.g. gatling 600 rpm x 25 dmg, railgun ~ damage/shot_delay+reload). `Pready_b` is the fraction of its guns charged/loaded within `Lookahead` 1.5 s (`G2H.ready`). **The primary battery = argmax score_b at the current range, re-evaluated at most every 3 s with 25 % hysteresis**; the ship is oriented so `d_primary` coincides with the primary's boresight (the `_aimRef` in `Aim()` becomes "battery reference block" = first working gun of the primary; already generalised in `Aim()` by `_aimRef`).

Secondary batteries: each fires when `angle(boresight_b, d_b) < window_b` (silhouette window, same formula as `_fireWinDeg`), range and friendly-line checks pass (`MatesInLine`, `CheckLineOfFire`, `MatesInTheWay` for rockets), independent of what steers. Because each battery has a fixed mount, a secondary's boresight error follows the ship's orientation plus its mount offset; the generator aligns all fixed batteries parallel to the nose where it can ("boresighted" ships, simplest and recommended), so all batteries have an error within a few degrees of the primary's and share windows. Slight offsets are accepted; wide offsets (broadside rails) become their own "broadside mode".

**Broadside mode** (ships with a battery mounted +-90 deg): if the primary is a broadside battery the orientation law adds a 90 deg yaw offset and orbits the target (thrust tangent as `Control()` already does with `OrbitSpeed`).

**Rail and shells** are low-rate: the existing Longbow rule applies, aim only when the capacitor will be full in `ReadyLead`, otherwise orient to the best *ready* battery; `G2H.ready` makes this the same score.

### 3.4 Standoff selection

Today `Standoff`, `MinRange`, `MaxFireRange` are fixed numbers per script; Longbow computes `EngageRange(t)` from dodge capability. For a multi-weapon ship:

```
R(t) = argmax_r  sum_b score_b(r)  -  lambda * Threat(r, t)
bounded by [max(MinRange, hull radius + 60 m),  0.85 * Range_primary]
```
evaluated on a 100 m grid between 200 and 2,000 m; `Threat(r,t)` = enemy weapon range indicator: while the enemy's weapon types are unknown assume gatling/autocannon 800 m and rockets 800 m; if `r` is beyond 900 m `Threat = 0.2`, inside 900 m `1.0`, inside 400 m `1.5`. In plain words: **if the primary outranges 900 m (rail 2000, artillery 2000), kite at 1000-1300 m** (outside randomised bullet range 640-800 m), otherwise close to the best combined-DPS range (typically 500-650 m where gatling + rocket + turrets all reach). Large targets (`Huge()`): use the closer end (BREACH wants accuracy). Parameters `Standoff Min`, `Standoff Max`, `Threat Weight` in config. Orbit speed scales with `MinAccel` of the ship (a heavy ship cannot do `JinkSpeed 12`); defaults for large grids: `Jink=false`, `OrbitSpeed = 0.25 * MinAccel * 4 s`.

### 3.5 When turrets alone engage

- Targets that no fixed battery has a plausible hit on (`Phit_b < 0.15` for all `b`): small, fast, jinking drones; missiles; kamikazes inside `Warning Time`. The ship keeps its orientation law for the biggest threat/primary target and **turrets engage the rest on their own** (existing vanilla behaviour).
- Fixed batteries reloading (> 4 s to ready): ship holds orientation for the *wing's* focus target and keeps the standoff.
- MANUAL: turrets always on; fixed batteries per pilot.
- The script configures turrets once in `TurretSetup()` through terminal properties by name (try/catch each, as `SetF/SetB` do; ids **to be verified by the "print property ids" in-game test**, audit): main turrets large-grid priority (`TargetLargeGrids` true, `TargetSmallGrids` false), PD turrets (tag `[PD]`) `TargetMissiles` true, `TargetSmallGrids` true, `TargetLargeGrids` false, `TargetCharacters` false, `EnableIdleMovement`; `Range` to class range. [rec medium] for the exact property ids (`TargetMeteors`, `TargetMissiles`, `TargetSmallGrids`, `TargetLargeGrids`, `TargetCharacters`, `TargetStations`, `TargetNeutrals`, `Range`).
- Overlay: a Custom Turret Controller (`TurretControlBlock`, in the sim) tied to the ship's cameras can act as a subsystem seeker, as SEEKER does now (not carried in Helm: -2.1 k; stays as an optional Gunnery module on the dreadnought if the in-game test T-seeker succeeds).

### 3.6 Heavy-ship flight

The drone laws (`AimKp 6, MaxTurn 5 rad/s`, `JinkSpeed`) do not fit a 100-1,000 t ship. Needed in `Aim()`/`Control()`:
- `MaxTurn` and `Kp` auto-calibrated: on first AIM command record the highest |omega| reached, set `MaxTurn = 0.85 x peak` (min 0.15 rad/s) and `Kp = MaxTurn / 0.6 rad`; persisted in Storage. (Sim: large-grid gyro torque from `defs_stats` is exact, [sim].)
- Acceleration limit `MinAccel()` already used; `CruiseSpeed` default 60 m/s and `MaxSpeed 98` stay (100 m/s cap, [data]).
- The `GetBlocks` rule: collect weapons/thrusters/gyros by **grid ownership and name prefix** (`[SH]` for the host ship, grids_bays 6.3) so merged drones are not adopted; re-run `GetBlocks` when `Me.CubeGrid` changes (exists: `CheckGrid`) and every 600 ticks.

### 3.7 How the existing blocks plug in

| Block | Role on a ship |
|---|---|
| LEAD v1.0 | unchanged; supplies `LeadAcc/LeadHold/LeadOffset/LeadPick` to all four solvers; `Track` also feeds `.TRK` to Gunnery |
| AIM/BREACH | the primary battery's reference drives `AimOffset`; BREACH on large grids stays (armour holes) with the primary battery |
| KAMIKAZE | verbatim in Helm; its output (alarm, back-away) already modifies `want`; on a heavy ship `KzBackSpeed` is clamped by `MinAccel`; PD turrets provide the real defence |
| FLEET | Staff sends; Helm's listener receives wing targets |
| FOLLOW / SATELLITE | **drone-side**; the ship feeds them by broadcasting `HOME`/`HOMER` (Staff Mothership link). `satellite` ring distance auto-grows with the wing count and the ship's HOMER radius (follow-mode.md: `max(SepRange*1.1,40)/(2 sin(pi/n))`, floor hull radius + 45 m) |
| LAUNCH | **drone-side**; the Pod PB arms it (`armed`, `Away X/Y/Z` Custom Data) and the merge release triggers it (grids_bays 6.1) |
| AIGATE | only on the fighter (and optional); lean Helms drop it |

---

## 4. Wing control from the ship

### 4.1 Staff PB (Update10 + IGC callback)

Staff is "HornetMothership + CradleLite + display". Per 10 ticks:
- Broadcast `<Tag>_HOME` (grid centre; for a big ship the **ship COM** `_rc.CenterOfMass`, not `WorldVolume.Center`, because the drones' `FollowSlot` orients by it), `_HOMER` (hull radius = `Me.CubeGrid.WorldVolume.Radius`, stays; the FOLLOW block widens to 2x under manoeuvre), on `FLEET` and each drone `<TAG>` that the carrier hosts (list in `[Wing] Types=HORNET,WASP,...`). Existing code (`HornetMothership.Main`), only the tag loop is new.
- Forward the ship's designators' enemies (turrets/CTCs) as `_TGT` (exists) **plus Helm's selected target** (from `.H2S`) so the wing shoots what the ship shoots.
- `wing <cmd>` verbs: `wing satellite` -> `FleetSendCmd("satellite")` (`satellite|sat|orbit` is in the drone `Command()`; **it is not in the default `Allowed Commands` list of the FLEET block**, `hunt,attack,guard,follow,escort,form,assist,rtb,idle,stop,launch,abort,split,focus,next,unlock,aialert` — hive-protocol s2 and follow-mode.md: so Staff either sends it on the per-type `<TAG>_CMD` (accepted unconditionally) or the default list gets `satellite,sat,orbit` added; recommend adding it, one-line change in `tools/fleet_block.cs`'s `_flAllowed` and each script's synced copy).
- Wing roll call: listens to `<TAG>_SWARM` (gridId, pos, vel, targetId, flags) of its own drones; counts alive/engaged, shows on HUD page 2; no new protocol.
- `recall`: `wing rtb` + after N seconds `wing satellite`; the drone returns to HOME; docking back is phase 2 (grids_bays 5.6).
- Secret: set `[Fleet] Secret=` identically on ship and drones (hive-protocol s5); IGC between Helm/Staff is `CurrentConstruct`-only, which needs no secret.

### 4.2 Bay state machine (CradleBay; in Staff for the corvette, in the Pod PB for frigate/dreadnought)

States and release sequence exactly as grids_bays s6.1 (EMPTY DOCKED CHARGING READY ARMING RELEASED COOLDOWN FAULT; `FindDrone` by cell box; `Arm` writes `[Launch]` and runs `armed`; release = drone batteries Auto -> host merge off -> drone merge off -> verify `Me.CubeGrid != drone grid` within 3 s else `FAULT FUSED`). My assumptions (grids_bays was available and is followed):
- A1 clamp face and paste-time merge are unverified (T1, C1); CradleBay toggles the host merge once at start and reports `FAULT` if `IsConnected` stays false.
- A2 while merged, drone blocks are host-grid blocks: Helm/Staff `GetBlocks` filter is **name prefix `[SH]` AND grid** (C10). Drone PBs stay IDLE (drones are only woken by `launch`/`join`).
- A3 launch from the toolbar: `Run` Staff `release 1` -> `S2P release 1` -> Pod. The Timer R(n) backup (grids_bays/timers_pbs 2.3) is armed by the EC "Merge Block Merged" on the bay merge; Staff stops it when it releases.
- A4 waves: Staff paces `release all` 2 s per drone, 20 s per pod (grids_bays 5.5) and keeps the ship's own acceleration < 1 m/s2 during release (Helm receives `cmd="hold"`: caps thrust to 20 % for 3 s so the cradle merge is not loaded; the cradle is rigid but the lane must stay clear).
- A5 doors (frigate/dreadnought hangars): `IMyDoor.OpenDoor()/CloseDoor()` on group `[Hangar n]`; Staff opens, waits `Status == Open` on all, sends `release`; closes 10 s after the last drone's `RELEASED`.

---

## 5. Timers, Event Controllers and the toolbar glue

Block list per ship (PCU from data): Timer D "deadman" 25; Timer R(n) per bay 25; EC "Cockpit" 10; EC "Bay n merged" 10; EC "Power" 10; EC "Core" 10; Button panel 5.

EC "Cockpit occupied" (XML shape from the six Tech Hunter ECs [data]; the event number for Cockpit Occupied is **unverified** (the Tech Hunter ones are `SelectedEvent` 2 = integrity, 1 = block-related); needs one saved sample, test K4):

```xml
<MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_EventControllerBlock">
  <SubtypeName>EventControllerSmall</SubtypeName> <EntityId>…</EntityId> <Min x="1" y="1" z="0" />
  <BlockOrientation Forward="Right" Up="Backward" /> <Owner>…</Owner> <ShareMode>Faction</ShareMode>
  <CustomName>EC Cockpit</CustomName> <Enabled>true</Enabled>
  <Toolbar><ToolbarType>Character</ToolbarType><SelectedSlot xsi:nil="true" />
    <Slots>
      <Slot><Index>0</Index><Item /><Data xsi:type="MyObjectBuilder_ToolbarItemTerminalBlock"><Action>Stop</Action><BlockEntityId>TIMER_D_ID</BlockEntityId></Data></Slot>
      <Slot><Index>1</Index><Item /><Data xsi:type="MyObjectBuilder_ToolbarItemTerminalBlock"><Action>Start</Action><BlockEntityId>TIMER_D_ID</BlockEntityId></Data></Slot>
    </Slots><SlotsGamepad /></Toolbar>
  <Threshold>0</Threshold><ANDGate>false</ANDGate><SelectedEvent>COCKPIT_OCCUPIED_NO</SelectedEvent>
  <SelectedBlocks><long>COCKPIT_ID</long></SelectedBlocks><ConditionInvert>false</ConditionInvert>
</MyObjectBuilder_CubeBlock>
```
(slot 0 = event true, slot 1 = false [data]; `TriggerNow`, `Start`, `Stop` action names appear in the Tech Hunter timers/ECs; `Start`/`TriggerNow` are [data], `Stop` [rec high].) `ComponentContainer` with the five `MyEvent*` components is copied verbatim from the Tech Hunter ECs by the generator. Timer D delay `5000` ms, `Silent true`. [pbapi IMyEventControllerBlock]: `IsAndModeEnabled`, `IsLowerOrEqualCondition`, `Threshold` are scriptable; there is no event selection property, so events are generator-set.

Roles: EC Cockpit -> Timer D stop/start (armed only when nobody sits); EC Power (battery `<=15 %`) -> Helm `Run rtb`; EC Core (reactor/battery group integrity `<=40 %`) -> Helm `Run retreat`; EC Bay n merged -> Staff `Run docked n` / `Run released n` (event-driven bay state, saves polling); Timer D toolbar -> `OnOff_Off` on thruster and weapon groups. Generator work first: `sebp.py` toolbar/`SelectedBlocks`/`BlockGroups` (timers_pbs 4.1), samples for cockpit/button panel/text panel shapes (`tools/shapes.json` has none of `Cockpit/*`, `ButtonPanel/*`, `TextPanel/*`, `AirtightHangarDoor`; extend `tools/extract_shapes.py` from the Pod Chaser / Starburst samples).

---

## 6. Sim support (what must change, by file, with size)

Current state read from `sim/` [sim]: `SimShipController.IsUnderControl = false`, no `MoveIndicator/RotationIndicator`; `BlockApiFactory` has no `Cockpit` case (a cockpit becomes `SimGenericFunctional`, so `GetBlocksOfType<IMyShipController>` does not see it and `ShipInstance.Controllers` stays empty); `TimerBlock/EventControllerBlock/ButtonPanel` -> `SimGenericTerminal` with no actions; `Physics` has dampeners and overrides but no player input; `Scenario` sends every command to **all** PBs of a ship (`RunOne`: `for k in c.Ship.Pbs`) and applies `CustomDataOverride` to all PBs; enemy ships (`--enemies`) get a Pirate brain and `EnemyBlueprint` keeps only the largest grid; `IlCounter/IlProfile` exist per runner; `TryRun` is queued; `stubs.cs` has `MyTuple` up to 6, no `IMyCockpit/IMyTextPanel/IMyTimerBlock/IMyDoor/IMyEventControllerBlock/IMyButtonPanel/DrawFrame/MySprite`.

| # | File | Change | Size |
|---|---|---|---|
| S1 | `stubs.cs` | add `MoveIndicator (Vector3)`, `RotationIndicator (Vector2)`, `RollIndicator`, `HandBrake`; `IMyCockpit : IMyShipController, IMyTextSurfaceProvider`; `IMyTimerBlock` (StartCountdown/StopCountdown/Trigger/TriggerDelay/IsCountingDown/Silent), `IMyEventControllerBlock`, `IMyButtonPanel`, `IMyDoor` (OpenDoor/CloseDoor/Status), `IMyShipConnector`, `IMyTextPanel`; `MySprite`, `DrawFrame`, `SpriteType` | ~110 |
| S2 | `sim/src/Api/RemoteControlApi.cs` | `SimShipController` gets `Pilot` input fields (`Move`, `Rot`, `Roll`, `UnderControl` set by the scenario) and the new properties; new `SimCockpit : SimShipController, IMyCockpit` with N `SimTextSurface` (N from the blueprint's `MySerializedTextPanelData` count) | ~60 |
| S3 | `sim/src/Api/BlockApiFactory.cs` | case `Cockpit` -> `SimCockpit`; `TimerBlock` -> `SimTimer`; `EventControllerBlock` -> `SimEventController`; `ButtonPanel` -> `SimButtonPanel`; door types -> `SimDoor`; `TextPanel` -> `SimTextPanel`; `BlueprintLoader` keeps the `Toolbar` | ~30 |
| S4 | `sim/src/Core/Physics.cs` | **pilot input**: for each non-overridden thruster apply `MoveIndicator` demand on its axis (and dampeners on the rest, as now), for each non-overridden gyro apply `RotationIndicator`/`Roll` as a rate target (rate controller with the gyro's torque), only when the cockpit is `UnderControl && ControlThrusters`. This is what makes "overrides win per block" testable | ~90 |
| S5 | new `sim/src/Run/ScriptedPilot.cs` | scenario-driven pilot: timeline `0:sit`, `5:move 0 0 -1`, `8:rot 0 1`, `20:leave`; and simple closed-loop modes `fly-to <p>`, `hold`, `face-target`, `fire-on-trigger` (sets `Shoot_On` on cockpit weapon group); runs as a `Brain` (like `PirateBrain`) | ~250 |
| S6 | `sim/src/Run/Scenario.cs`, `ScenarioGen.cs` | `ShipSpec.Commands` entry syntax `"<pbTag>:<arg>"` (match `CustomName` prefix `[Helm]`, `[Staff]`...) and `PbCustomData` per tag; `--enemy-scripted`: enemy blueprint keeps its PBs running, no Pirate brain (`HasBrain=false`), start command `hunt` to `[Helm]` and `wing satellite` to `[Staff]`; `EnemyBlueprint` keeps **all** grids (fused by S9) instead of the largest; new `Kind "ship"`: our warship vs `--enemies` set, pirates, mixed fleets | ~120 |
| S7 | new `sim/src/Api/TimerApi.cs`, `EventControllerApi.cs`, `ToolbarExec.cs` | Timer countdown in ticks (restart on Start, 0.1 W, whole-second granularity as in the game), EC evaluators for the events used (cockpit occupied, stored power %, block integrity, merge merged, thrust %), edge-triggered slot 0/1, toolbar items `Start/Stop/TriggerNow/OnOff_On/OnOff_Off/Run(arg)/Shoot`, group items via `BlockGroups` (timers_pbs s3 sizing: 150 + 250) | ~420 |
| S8 | `sim/src/Api/ProgrammableBlockApi.cs`, `ScriptHost.cs` | `TryRun` synchronous with real result (timers_pbs s3); `Recompile` action off by default (game has none); `KillPb(name, tick)` scenario event for the dead-man test; sticky "Script too complex" termination via the IlCounter (`EnforceInstructionLimit` exists) | ~60 |
| S9 | `sim/src/Core/GridFactory.cs` (+`BlockApiFactory`) | merge pass: fuse GridData whose merge blocks touch into one Body keeping `ConstructId` per origin grid (grids_bays 7.2), connector/stator-linked grids fused rigidly; `IsSameConstructAs` keeps working per `ConstructId` | ~100 |
| S10 | `sim/src/Api/Igc.cs`, `SimSettings.cs` | `IgcLatencyTicks` (1 default, 2 for the sensitivity run) setting; per-queue limit `IgcMaxWaitingMessages` already exists, add message-size accounting for `Dropped` stats | ~30 |
| S11 | `sim/src/Run/Metrics.cs`, `JsonOut.cs`, `sim_report.py` | per-PB: runs, peak `CurrentInstructionCount`, CpuMs (exists); per-ship: `ModeTimeline` (mode string sampled each second from the Helm echo or an `IMyProgrammableBlock.CustomInfo` line), `OverrideFightTicks` (pilot input nonzero AND any gyro/thruster Override on while `UnderControl`), `DeadmanTrips`, per-battery shots/hits (needs a `Projectile.Source` battery tag from the weapon block name `[B1]`), `WingAlive`, `BaysReleased`, `FusedBays`, `TimeToFirstFire`, `PilotRateError` | ~120 |
| S12 | `sim/src/Run/FollowTest.cs` | `--owner ship`: the owner is a spawned ship blueprint with a Staff PB instead of a virtual point | ~60 |
| S13 | `sim/src/Run/DockLaunch.cs`, `sim/dock_launch_test.sh` | `--cradle` case: real drone GridData in a host blueprint (needs S9) | ~50 |
| S14 | `sim/src/Core/SelfTest*.cs` | tests for S2/S4/S7/S9 (selftest suites exist per slot) | ~150 |
| | **Total** | | **~1,650** |

Unmodelled and kept unverified: real `MoveIndicator` scale and gyro feel (K1/K2), the Event Controller event numbering, door animation, connector locks (pod stays welded in the sim), ownership change in survival, real IGC latency (S10 runs both 1 and 2 ticks).

---

## 7. Staged plan with acceptance tests (CLAUDE.md seed rules)

Seeds: 3-10 to check something runs / to catch a regression; 20-50 for a rough A/B direction; 100+ not planned. Each line states what runs and why.

**M0 Groundwork (no ship yet)** — S1-S3, S4, S5, S6 (command/CustomData per PB), S8 `TryRun`, S14 selftests; generator: `shapes.json` for Cockpit/ButtonPanel/TextPanel/door via `extract_shapes.py`, `sebp.py` second PB with names, `tools/blocks_sync.py`. *Tests:* `selftest` passes; scripted pilot flies a Hornet with an added cockpit 10 s with no script (3 seeds, thrust/rotation within 5 % of the analytic response); override-wins test: one gyro overridden, one not, pilot yaws (1 run).

**M1 "Shrike" fighter: one PB + MANUAL** — `scripts/ShrikeAI.cs` = Hornet - LAUNCH - FOLLOW - SEEKER + MANUAL + BATTERY-lite + DEADMAN; blueprint = Hornet-class hull + `DBSmallBlockFighterCockpit` (150 PCU) or `SmallBlockFlushCockpit` (45 PCU) + RC. *Acceptance (5 seeds each, scenario `hornet` enemy, pilot sits at 10-20 s):*
1. seat taken -> within 3 ticks every gyro `GyroOverride=false` and every thruster override 0; `OverrideFightTicks = 0` for the whole seated period;
2. pilot rate error < 15 % vs the unscripted baseline (M0 test);
3. seat released -> mode returns to `_autoMode` within `Release Delay + 0.3 s` and the lock resumes within 5 s; no oscillation in the first 3 s (|omega| not exceeding the cap);
4. `manual`/`auto` verbs work with no pilot (toggle), the Timer-D arm/stop is observed once S7 exists (until then a flag counter);
5. Minified size <= 80 k (build_all check); regression: Shrike vs Hornet 1v1 AUTO, 20 seeds, win rate within +-2 sigma of the same-assumption Hornet-vs-Hornet baseline (it is a Hornet with one more gun class).
6. `assist hud` shows pip text (screen dump); `assist fbw` with scripted pilot: stick-idle converges to the target within `Assist Max Rate` and a stick motion drops the assist within 0.2 s (5 seeds).

**M2 Helm/Staff split + dead-man** — `tools/helmlink_block.cs`; Staff v1 (FLEET send, Mothership link, display, HB watch); Timer D + EC Cockpit in sim (S7 subset). *Acceptance:* (a) parity: Shrike-split vs Shrike 1-PB, 20 seeds, same win-rate band and median time-to-kill within 5 %; (b) `KillPb Helm` at 15 s with thrust override on: Staff clears all overrides within 1.5 s (5 seeds); (c) Timer D fires 5 s after Helm death in AUTO, does not fire in MANUAL, EC covers a Helm that died while seated (3 seeds); (d) IGC latency 1 vs 2 ticks: win rate unchanged (10 seeds each); (e) peak instructions per PB < 30 k with `--count-instructions`.

**M2 as built** (commit "M2: Helm/Staff split ..."): `tools/helmlink_block.cs` (HELMLINK, carried by ShrikeAI via `blocks.json`), `scripts/StaffAI.cs` (12.3 k minified; FLEET send block, HOME/HOMER/_TGT link, `.HB` watch with the 1.5 s trip, displays, `wing`/`fleet` verbs, `release`/`recall`/`doors` stubs for M5, argument-less Run menu), test ship `Shrike Staff Test` (+ `deadman_kit()` in `tools/designs/_common.py`), sim `IgcLatencyTicks` (S10, opt-in), `selftest m2`. Wire format: HB every 0.5 s (spec said 1 Hz; 2 Hz keeps "within 1.5 s" true), H2S 5 Hz, PILOT on change, S2H on command (no 1 Hz refresh: the Helm has no use for it yet). On the first trip the Staff also switches the `[AI]` blocks off (a vanilla AI block left on keeps flying with its own overrides and wins against the release); the Helm re-enables them if it believes the AI flies (one line in `AiStep`). To make room (+1.9 k) ShrikeAI lost 24 rarely used Custom Data keys (the variables keep the Hornet defaults); it is now 79,878 of 80,000.

**M3 Multi-weapon fire control** — `tools/battery_block.cs`, classes BALLISTIC/ROCKET/RAIL/SHELL, primary choice, standoff; Gunnery PB for the dreadnought only after the single-PB version works. Test ship: temporary "Corvette-test" built from Hornet hull plus Longbow railgun + Basilisk launchers (hull+weapons all non-DLC in `defs.json`) or the Basilisk itself with its launchers tagged. *Acceptance:* (a) solver unit tests in `tools/lead_bench` for the four classes vs strafing/turning/orbiting targets (existing bench), hit rate not worse than the class's current script (-2 points) ; (b) A/B on 3 enemy sets (Pirate, Tech Hunter via `--enemies`, Hornet pair), `battery-manager` vs `single-class (current Basilisk logic)`, 20 seeds each: damage dealt up or equal, no friendly fire events; (c) range behaviour: with rail present and an enemy gatling ship, median fight distance > 900 m (sim trace), with rockets+gatling only 450-700 m; (d) turrets-only mode: 10 s with a small drone target that all fixed batteries refuse (`Phit < 0.15`), the ship keeps orientation on the large target (5 seeds); (e) ship turn rate: `MaxTurn` auto-calibration within 20 % of the analytic gyro/inertia value (3 seeds).

**M4 Staff wing + mothership link** — Staff wing orders, HOME at COM, HOMER, `wing` verbs, FLEET `satellite` allowance, HUD page 2. *Acceptance:* `follow-test --owner ship` (S12) reproduces follow-mode.md rows with 6 Hornets (ring 100 m +- 1, plane deviation <= 3 m, orbit rate 1.99 deg/s, nobody inside hull + 5 m) and 4 Basilisks, 3 seeds each; `wing hunt` -> drones engage the ship's selected target (time-to-first-fire < 3 s after the ship locks, 5 seeds); `wing rtb` / `satellite` flip works.

**M5 Cradle bays** — S9, S13, `scripts/CradleBayAI.cs`, blueprint `Cradle Test` (grids_bays 10). *Acceptance:* `dock_launch_test.sh --cradle` 2 slots, 10 seeds: no FUSED, drones separate within 3 s of `release`, burn vector clear of the host (lane check), release waves 2 s apart; Helm lists contain no drone block while merged (C10, 3 seeds); `release 1` from the toolbar equivalent (scripted `Run Staff "release 1"`) works while the pilot is seated.

**M6 Assembly: Corvette, Frigate, Dreadnought** — one blueprint at a time with its PB split; each run in `ship` scenarios vs pirates, a mixed drone swarm and the Tech Hunter (`--enemy-scripted` off for our ship's opponents; our ship uses its scripts), 5-10 seeds each. *Acceptance:* every PB <= 80 k and < 40 k instructions peak, deadman/HELM LOST tested per class, all three win >= 80 % vs the Pirate in 10 seeds (calibrate later), wing launch + fight + satellite works, a scripted pilot flies each ship with no override fight.

**Not for Claude sessions (user's PC, `pc/plans`)**: the 100-seed matrices.

### In-game tests to add to `packages/tests.md`

K1 `MoveIndicator`/`RotationIndicator`/`RollIndicator` signs, ranges and units while flying a Hornet-class seat and a large cockpit; K2 gyro feel: override rate vs native rate for a given indicator; K3 save a cockpit toolbar slot with `Run` + argument on a PB and read the XML (Ctrl+B export); K4 EC "Cockpit Occupied" `SelectedEvent` number and slot semantics; K5 `IsUnderControl` for a Remote Control that a player uses remotely vs a seated cockpit; `CanControlShip` on passenger seats; K6 turret terminal property ids (print `GetProperties` of `LargeGatlingTurret`, `LargeMissileTurret`, `InteriorTurret`); K7 `DampenersOverride` write while a pilot sits (does the V key state change?); K8 IGC `CurrentConstruct` Helm->Staff latency and 25-queue overflow; K9 cockpit surface indices -> which physical screens on `DBSmallBlockFighterCockpit` / `LargeBlockCockpitSeat`; K10 `DrawFrame` on a cockpit surface; K11 `TryRun` budget (timers_pbs 8.3).

---

## 8. Open risks and what would change the plan

1. **PB `Run` with a typed argument on a toolbar** (K3) is the single point of failure for hand control of Staff/Helm. If it does not exist, pilot commands collapse to: weapons/doors/merge blocks directly on the toolbar (all exist), plus mode switching by the cockpit EC and a few Timers with fixed behaviours (Timer/PB cannot pass an argument either). Mitigation: design every verb so that **argument-less `Run` cycles** a small menu (`Run` repeatedly = next menu item, the HUD shows the highlighted one, a second slot "confirm"). Costs ~0.6 k chars in Helm; add it to M1 as a second fallback.
2. **Fighter at ~79 k** has no headroom: AIBLOCK (3.8 k) and the Display (2.8 k) are the pre-agreed removals; Wasp/Basilisk/Longbow ports must not be added to it.
3. **Instruction budget**: Helm with 4 batteries and BREACH on a 13 k-block target is the worst case; use the IlProfile (`--count-instructions`) in M3, and the `0.6 x Max` guard on optional stages. If Keen counts IL rather than blocks (audit: Gnat 145 %), Helm needs another cut (SEEKER already out, then AIBLOCK/BREACH).
4. **Fly-by-wire** depends on K1/K2; default stays `assist hud`.
5. **Merged-drone contamination** (name-prefix rule `[SH]`) is a generator rule as much as a script rule; a blueprint without prefixes makes Helm adopt drone thrusters (grids_bays F6).
6. Sim fidelity: enemy ships and the new ships share the optimistic large-grid damage model (audit: large thrusters/batteries die 1.6-5.9x too slowly); use same-assumption A/B only.
