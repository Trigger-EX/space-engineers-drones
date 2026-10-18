# Survivability upgrade for the combat drones (design)

`docs/design/survivability.md`. Planned from origin/main at bd7817a (2026-10-17).

**Read-only plan.** Nothing is implemented. Release zips are not rebuilt.

**Binding rules (CLAUDE.md):**
- No DLC blocks.
- Outline rule: no block outside a drone's set outline.
- PB budget about 80k minified. Move off-hot-path modules to another PB. Never cut a feature. Never raise a cap.
- Every general change goes to retrofit Pending.
- Run few sim seeds.
- No zips.

**Off-limits files.** `scripts/TubeRamAI.cs` and `tools/designs/tube.py` belong to another session (Tube flare pods, v1.4).

**Owned by other agents (only referenced here):**
- `tools/survivability_audit.py` (item 16).
- Sim survival metrics, `sim/src` plus `tools/sim_report.py` (item 21).

---

## 0. Scope and decisions

### 0.1 Models in scope

The 9 split gun drones. Each has a Flight PB and a Staff PB:

| Model | Flight PB | Staff PB |
|---|---|---|
| Hornet and Watcher (same script; the Watcher has no guns) | `HornetDroneAI` | `HornetStaffAI` |
| Harrier | `HarrierAI` | `HarrierStaffAI` |
| Lancer | `LancerAI` | `LancerStaffAI` |
| Aegis | `AegisAI` | `AegisStaffAI` |
| Gnat | `GnatSwarmAI` | `GnatStaffAI` |
| Longbow S, Longbow L, L Seeker | `LongbowSniperAI` | `LongbowStaffAI` |
| Wasp | `WaspFlightAI` | `WaspStaffAI` |
| Mantis | `MantisAI` | `MantisStaffAI` |
| Basilisk and Basilisk Seeker | `BasiliskAI` | `BasiliskStaffAI` |

Other scripts in scope:
- `HiveMotherAI` and `CradleBayAI`: repair bay.
- New `MedicAI` plus a new Medic design: item 18.
- Midge (scriptless): decoys only, generator change.

### 0.2 Out of scope (rows go to `docs/retrofit.md` Pending, section 10)

- **ShrikeAI and StaffAI (warships Ballista, Warden, Leviathan, Corsair, Shrike).**
  - The Helm is at 84.7k of an 86k gate.
  - The ships have their own BATTERY standoff, decoys and Helm/Staff split.
  - They are the best candidates for item 9, because their [PD] turrets can target missiles.
  - Every Staff-side module below fits into `StaffAI` later.
- **TubeRamAI:** off-limits now.
- **StingerAI:** an expendable munition. Only items 1 and 2 apply (on approach).
- **WaspAI:** legacy, no blueprint.
- **Mothership relays, PathfinderAI, PeeperAI, SpotterAI:** see section 10.

### 0.3 Where each item lives

| # | Item | Lives in | Package |
|---|---|---|---|
| 1 | Perpendicular evade | Flight block SURVIVAL LINK (replaces each script's `Evade()`) | A1 |
| 2 | Lead-breaking jinks | Flight block (`SvJink`, replaces the random jink code) | A1 |
| 3 | Damage RTB, retreat off the threat axis | Staff block SURVIVAL STAFF decides. Flight block flies the via point and can fight back. | B1, A1 |
| 4 | Limp mode | Staff computes the caps. Flight applies them. The existing `[Thrust]` PB re-orients thrust (unchanged). | B1, A1 |
| 5 | PB failover | Staff block `SvWatch` (limp-home) plus a Flight yield handshake | B1, A1 |
| 6 | Power protection (railgun recharge) | Flight block `SvPower`, called by Longbow and Mantis only | A1 |
| 7 | Blind-side orbit | Staff picks the bearing. Flight steers the orbit. | B1, A1 |
| 8 | Standoff by enemy weapons | Staff (threat memory gives a standoff floor). Flight applies `SvStand`. | B1, A1 |
| 9 | Missile warning: dodge and flares | Flight reports sightings. Staff tracks them, picks the dodge and fires the flares. | A1, B1 |
| 10 | Wing hand-off, damaged drones to the back | Staff (standoff add, FOLLOW distance), SWARM bits | B1 |
| 11 | Decoys | Generator helper `add_survival_kit` | C1 |
| 12 | Flare launchers | Generator plus Staff firing | C1, B1 |
| 13 | Spaced armour or skin panels | Generator, limited (decision in section 2) | C1 |
| 14 | Bury and spread critical blocks | Generator, guided by the audit | C1 |
| 15 | Self-projector | Generator (`printer_lib` projection) plus the Staff REPAIR CLIENT | C1, C2, B1 |
| 16 | Single-hit-kill audit | Other agent (`tools/survivability_audit.py`), used by C1 | n/a |
| 17 | Repair bay | Printer bays (HiveMother) first; Cradle bays when they have welders; Staff dock controller | D1, B1, C2, C3 |
| 18 | Medic / tender drone | New `MedicAI.cs`, new `tools/designs/medic.py`, Staff patient side | E1, B1 |
| 19 | Last-gasp report and threat memory | Staff (wing channel `<Tag>_THREAT`), Flight reports hits | B1, A1 |
| 20 | Attack together (bounded gather) | Staff picks the slots. Flight flies the gather point. Never blocks a fight. | B1, A1 |
| 21 | Sim survival metrics | Other agent | n/a |

---

## 1. Architecture

### 1.1 What runs where

**The Flight PB keeps only the hot path:**
- tracking, lead, aim, control, firing;
- plus small survival pieces that must react within one tick or that only Flight can see:
  - the evade vector on a hit;
  - jinks;
  - the missile sighting inside `Valid()`;
  - per-block hit stats from `CheckDamage`;
  - the railgun power guard;
  - applying advice from the Staff.

**Everything slow or decision-like moves to the Staff PB:**
- damage state and RTB decision;
- limp caps;
- failover limp-home;
- threat memory and wing broadcasts;
- blind-side bearing, gather slots, hand-off;
- missile tracking, dodge and flares;
- docking and medic calls.

**The 7 split drones over budget get a fourth PB, `[Aux] <Model> PB` (docs/design/staff_split.md).** Hornet/Watcher, Harrier, Lancer, Aegis, Wasp, Longbow and Mantis run REPAIR CLIENT (bay dock controller, medic patient side, self-projector) and the SEEKER turret reader in one shared script, `scripts/AuxAI.cs`; their Staff carries the thin AUX LINK block in their place. The Aux PB writes no override: during a dock approach it sends setpoints on `.DKS` and the Staff flies them (SURVIVAL STAFF dock actuator, FOL bit 2). Gnat and Basilisk keep both blocks in their Staff PB (under 75k; the Gnat has no free cell for a fourth PB).

**Phase 1 also moves four existing off-hot-path modules from Flight to Staff to make room.** Section 5 has the sizes.
- **SEARCH PATROL:** HUNT with no target. Not combat.
- **SENSOR SETUP:** writes the sensor terminal properties once.
- **TARGET FEEDS:** other fleets' `<TAG>_TGT`. The Staff relays them over the existing `.TGT` path.
- **SEEKER:** reads the `[Seeker]` turret twice a second. Its finding reaches the Flight through the existing WEAKPOINT RELAY `.WPT` slot. (Since staff_split: the SEEKER block, local in Gnat/Basilisk, in the `[Aux]` PB for the other 7, which sends its finding to the Staff on `.SKW`.)

**How the Flight/Staff split is maintained.**
- `tools/split_flight.py` and `tools/make_staff.py` are one-off generators: they assert "already split". Never re-run them; they would drop the new code.
- Staff and Flight scripts are now edited by hand, plus shared blocks:
  - `tools/split_sync.py` (SPLIT LINK);
  - the legacy `*_sync.py` scripts;
  - `tools/blocks_sync.py` (`tools/blocks.json`).
- All new code goes into new shared blocks, plus small per-script hooks.

### 1.2 New shared blocks

All are registered in `tools/blocks.json` with the default anchor `^public Program\(\)`.

| Marker name | File | Scripts | Owner package |
|---|---|---|---|
| `PATROL LINK v1.0` | `tools/pl_block.cs` | HornetDroneAI, HarrierAI, LancerAI, AegisAI, WaspFlightAI, BasiliskAI | A1 |
| `SURVIVAL LINK v1.0` | `tools/sv_block.cs` | the 9 Flight scripts | A1 |
| `STAFF SEARCH v1.1` | `tools/ss_block.cs` | the 9 Staff scripts | B1; v1.1 seeker cut out into SEEKER (staff_split) |
| `SURVIVAL STAFF v1.1` | `tools/svs_block.cs` | the 9 Staff scripts | B1; v1.1 dock actuator `DkGo/DkSteer/DkRel/DkHold/SvRepaired` (staff_split) |
| `REPAIR CLIENT v1.1` | `tools/rc_block.cs` | GnatStaffAI, BasiliskStaffAI, AuxAI | B1; v1.1 actuator writes become host hooks (staff_split) |
| `SEEKER v1.0` | `tools/sk_block.cs` | GnatStaffAI, BasiliskStaffAI, AuxAI | staff_split |
| `AUX LINK v1.0` | `tools/al_block.cs` | the 7 split Staff scripts (Hornet, Harrier, Lancer, Aegis, Wasp, Longbow, Mantis) | staff_split: IGC proxies for REPAIR CLIENT and SEEKER |

Existing shared blocks that get edited:

| Block | File | Change | Owner |
|---|---|---|---|
| BREACH + SEEKER (legacy sync `tools/aim_sync.py`, 9 Flight scripts) | `tools/aim_block.cs` | becomes BREACH + SEEKER v1.3: seeker removed, own-seeker hole taken from `.WPT` bit 4096 | A1 |
| WEAKPOINT RELAY v1.1 (9 Staff scripts) | `tools/wp_block.cs` | own seeker entries rank first | B1 |

**Rules for shared blocks:**
- C# 6 only: `compile_check.sh` uses `-langversion:6`. No tuple syntax, no `out var`.
- A shared block may name only `Mode.IDLE` and `Mode.RTB`. Gnat's enum is {IDLE, FORM, ATTACK, RTB}. Everything else goes through per-script hook functions, listed in each package.
- Host fields a block uses are listed in its header comment, as the existing blocks do.

### 1.3 One writer per override

| Output | Normal | Take-off burn (LAUNCH) | Dock approach (item 17) | Flight PB down (item 5) | Docked or welded |
|---|---|---|---|---|---|
| Gyro and thruster overrides, `DampenersOverride` | Flight | Staff (FOL bit 2, `LkBurn`) | Staff (same FOL bit 2 `LkBurn`; `DkSteer` flies the setpoints from the `[Aux]` PB's `.DKS`; Gnat/Basilisk: local REPAIR CLIENT, same `DkSteer`). **The Aux PB never writes.** | Staff limp-home, then nobody (released, dampeners on) | nobody (PRINTER GUARD, Staff `SvDocked()`) |
| Gun `Shoot_On`/`Shoot_Off` | Flight | Flight | Flight (holds fire, no target) | Staff (off, once) | Flight (off) |
| AI blocks `Enabled` | Flight | Flight | Flight | Staff (off, once) | Flight |
| Flare launchers (`ShootOnce`) | Staff | Staff | Staff | Staff | nobody |
| Railgun `Enabled` (power guard) | Flight | Flight | Flight | nobody (Flight re-enables on start and in `Release()`) | Flight |
| Sensor properties | Staff (moved) | Staff | Staff | Staff | Staff |
| `[Seeker]` turret targeting group, seeker cameras `EnableRaycast` | Aux PB (SEEKER); Gnat/Basilisk Staff | same | same | same | same |
| Own projector `Enabled` | Aux PB (REPAIR CLIENT); Gnat/Basilisk Staff | same | same | same | bay script in its REPAIR state, else Aux / Staff |
| Own merge `Enabled` | LAUNCH (Staff) switches it off after a take-off | LAUNCH | REPAIR CLIENT (Aux PB; Gnat/Basilisk Staff) switches it on at ALIGN -> INSERT | n/a | n/a |
| Bay welders and projector | bay script | | | | bay script |
| Medic welders | MedicAI | | | | |

**Flight `Release()` guard (hook).** First line: `if (LkBurn || SvYield) return;`.
- Today an exception in Flight calls `Release()` once.
- That zeroes overrides under a Staff that is flying.

### 1.4 What happens when one PB is missing

- **Staff dead (Flight alive).** SVS goes stale. Every survival advice field is ignored. Flight still has:
  - perpendicular evade;
  - lead-breaking jinks;
  - the railgun power guard;
  - its old battery and ammo RTB.

  Lost until the Staff returns:
  - damage RTB, limp caps, gather, blind side, threat memory, missile dodge, flares;
  - patrol, sensor setup, feeds, seeker;
  - docking, medic calls.

  Today a Flight without its Staff already loses FOLLOW, LAUNCH and FLEET. This is documented, not hidden.
- **Flight dead (Staff alive).** Item 5. The Aux PB sees SDK bit 2 and aborts a dock (every write is the Staff's).
- **Aux PB missing or dead (the 7 split drones).** Lost: bay docking, medic calls, self-projector control and the seeker. Everything else works. The Staff screen says `! Aux PB silent` or, when none ever spoke, `! no [Aux] PB (repair, medic, seeker off)` after 5 s. A dock flight in progress: the Staff flies the last setpoint at most 15 ticks, releases, and the Flight resumes 2 ticks later. An in-game drone whose Staff script is updated without adding the PB loses exactly these, so paste `AuxAI.cs` into a PB named `[Aux] ...`.
- **Staff dead, Aux alive.** The Aux shows `STAFF PB SILENT`, sends no requests and aborts a bay session in states 1-4 (`staff silent`); a medic session keeps broadcasting WORK. The seeker stops (no target).

---

## 2. Items

Key names below are `[Survival]` keys unless a section is named. Section 3 has the full table.

### Item 1. Evade perpendicular to the shooter (Flight, `SURVIVAL LINK`)

**Today.** `Evade()` is identical in all 9 Flight scripts (Shrike has an extra `|| Big`). Under fire it picks a new random 3D velocity of `Evade Speed When Hit` every 1 to 2.5 s and on every new hit.

**New.** `Evade()` is defined in the block. A1 deletes each script's own copy and its `_evade/_evadeNext/_evadeHits/_evRng` fields, which move into the block.

```
Vector3D SvAxis(Vector3D me):  t = _tgt != null ? normalize(_tgt.P(_now) - me) : 0
                                h = (_now - _hitDirT < 3) ? _hitDir : 0      // _hitDir: hit blocks seen from the grid centre (CheckDamage), points at the shooter's side
                                if (h != 0 && (t == 0 || dot(h, t) < 0.7)) return h  // shot by something other than my target
                                return t != 0 ? t : h
Evade(): if (!UnderFire) return 0
         on _now > _evadeNext or a new hit:  _evadeNext = _now + 1 + rnd*1.5
           u = SvAxis(me); r = random unit vector; if (u != 0) r -= dot(r,u)*u     // dodge in the plane perpendicular to the threat axis
           vLat = own velocity minus its u component; with probability 0.7, if dot(r, vLat) > 0 then r = -r   // reverse the current drift
           _evade = normalize(r) * EvadeSpeed
         return _evade
```

**Keys.** Unchanged: `Evade Speed When Hit`.

**Failure mode.** With no target and no fresh hit direction, `u = 0` and the old random behaviour remains.

**Test.** `pirate/Hornet-vs-Pirate`, A/B, 20 seeds.

### Item 2. Lead-breaking jinks (Flight, `SURVIVAL LINK`)

**Why it works.** Vanilla turrets lead with constant velocity. The sim's `TurretAi` does the same; it is unverified in game. Over the time of flight T, a lateral acceleration a moves us 0.5·a·T² off the turret's prediction.

**Today.** A random velocity of `Jink Speed` (12 m/s) every `Jink Interval (s)` + rand × `Jink Interval Random (s)` (1 to 2 s).

**New function.** `Vector3D SvJink(Vector3D los, double dist, double aLat, double ampCap, bool on)`.

```
if (!on || !SvLeadJink) return 0
vEnemy = (SVS fresh && N.Z > 0) ? N.Z : SvEnemyV          // threat class speed from the Staff (rocket 200, bullet 800, long 1000), else the key
T = dist / max(100, vEnemy)
P = clamp(T * (1 + SvJinkRnd*(2*rnd-1)), SvJinkMin, SvJinkMax)
if (_now >= _svJinkNext):
    _svJinkNext = _now + P
    d = random unit vector perpendicular to los; if (dot(d, _svJinkDir) > 0) d = -d   // at least 90 deg turn each period: thrusters stay saturated
    _svJinkDir = d
    amp = min(ampCap, aLat * SvJinkA * P * 0.5)                  // a velocity swing of aLat*P per period = full lateral accel for most of P
    _svJink = d * amp
    if (rnd < 0.1) _svOs = -_svOs                                // orbit sign flip (replaces the old 25 % flip per jink)
return _svJink
```

**Hooks.**
- **Hornet family** (Hornet, Harrier, Lancer, Aegis, Wasp, Basilisk): replace the `if (_now > _nextJink) {...}` block with `_jink = SvJink(dir, dist, aBrake / 0.55, JinkSpeed, Jink);`.
  - `aBrake` is 0.55 × the weakest axis acceleration, so `aBrake / 0.55` is the lateral estimate.
  - The orbit term uses `SvOrbit` (item 7), which owns the orbit sign.
- **Longbow and Mantis:** the reload-jink block becomes `SvJink(dir, dist, aLat, JinkSpeed, true)`. Their key is `Jink Speed While Reloading`. Keep the existing "only while reloading" gate.
- **Gnat:** no lateral thrust. `SvJink` is not called; the Gnat gets item 1 only.

**Keys.**
- Kept:
  - `[<Model>] Evasive Jinking`: master switch.
  - `Jink Speed`: now the amplitude cap.
- Removed from `LoadConfig`: `Jink Interval (s)` and `Jink Interval Random (s)`. Existing Custom Data keeps them; they are ignored.
- New Flight-read keys: `Lead-Breaking Jinks`, `Jink Accel Fraction`, `Enemy Projectile Speed (m/s)`, `Jink Period Min (s)`, `Jink Period Max (s)`, `Jink Period Random`.

**Failure modes.**
- Close range: T < 0.4 s, so the minimum period holds and the gain is small.
- Rockets: T is long, so the amplitude may hit the cap.
- AB2 tunes the `Jink Speed` default (20 to 25 suggested).

**Test.** `pirate/{Hornet,Harrier,Aegis}-vs-Pirate`, 20 seeds A/B with `Lead-Breaking Jinks=false`. Also the charge line `charge-Hornet-default` with 10 seeds.

### Item 3. Damage RTB with retreat off the threat axis (Staff decides, Flight flies)

**Staff, `SURVIVAL STAFF`, every 10 ticks.**
- **Integrity fraction:** from SVF bits 8-15.
- **Thrust per axis:** relative to `_rc`, own grid only. Keeps `_svRated[i]` = the best working `MaxEffectiveThrust` sum seen. An axis is lost when `live < 0.1 × rated` (rated > 0).
- **Gyros:** working count vs the most seen.
- **Forward cameras:** dot(cam fwd, RC fwd) > 0.9. Working vs the most seen.

**Triggers.** Only when `Damage RTB` is on, mode is not IDLE or RTB, and the drone is not docked. Any of:
- integrity < `RTB Integrity Below`;
- an axis lost (`RTB On Lost Thrust Axis`);
- working gyros ≤ `RTB Gyro Fraction` × max, with max ≥ 2;
- forward cameras 0, with max ≥ 1 (`RTB On Lost Forward Cameras`).

**AB2 defaults:** only the integrity trigger is on. The axis, gyro and camera triggers are off by default (keys kept): in the sim they fired on the first lost block and cost about 40 % of the wins (section 11).

**On a new cause** (rising edge; latched per cause):
- Store the current mode verb in `_svPrevVerb` (per-script hook `SvVerb()`).
- `Fwd("rtb")`.
- `_svRtb = true`. Screen: `DAMAGED: <why> - RTB`.

**Owner override.** Any later mode command, or the new Staff word `fight`, clears the latch. It re-triggers only on a new cause. No odds-based refusal: this item only reacts to damage.

**Retreat via point** (Staff, at the trigger and every 1 s while RTB with `_svRtb`):

```
T = attacker position (SVF, fresh < 10 s) or the current target position (F2S), else none -> no via
R = threat range for that enemy (item 8 memory) or 900 m;  M = me;  H = SVF home
u = normalize(M - T);  h = normalize(H - M)
if (dot(h, -u) < cos(60 deg)) no via                       // home is not behind the threat
side = normalize(h - dot(h,u)*u)  (if ~0: RC.Right made perpendicular to u)
V = T + normalize(u*cos(70 deg) + side*sin(70 deg)) * max(R + Retreat Margin, |M - T|)
clear the via point when |M - V| < 150 or the segment M-H keeps >= R from T
```

**Flight hooks** (all 9; adapt names per script):
- **RTB / no-target branch:**
  - `Vector3D d = SvHome(home) - me;`
  - speed `Math.Min(SvViaOn ? MaxSpeed : CruiseSpeed, ...)`
  - the arrival-to-IDLE check gets `&& !SvViaOn`.
- **Aim condition:** `if (_tgt != null && (_mode != Mode.RTB || SvFightBack))`. The Staff sets bit 10 only when `Fight Back While Retreating` is on, a gun works and no axis is lost.

**Failure modes.**
- Staff dead: no damage RTB.
- A small threat range estimate: the arc may still pass inside the real range.

**Test.**
- `one --scenario "pirate/Hornet-vs-Pirate" --seed-index 0..2 --events "20:!kill-thrust B"`.
- F1 events `!kill-block Gyroscope 1` and `!damage 0.5`, with `--trace`: RTB starts and the path bends.

### Item 4. Limp mode (Staff computes, Flight applies, `[Thrust]` PB re-orients)

**What exists.**
- `MinAccel()` already uses only working thrusters, but skips an axis whose thrust is 0. That overestimates braking along a lost axis.
- `scripts/ThrustVectorAI.cs` (`[Thrust]` PB) detects a lost group, reported in `TVEC.O` state bits 0-5. It turns the ship so a surviving group lines up with the needed force.
- With `_tvD = -1` (no target distance) it gives force priority. With a target inside 1,200 m it keeps the guns first.

**Staff.**
- `f = min over axes with rated > 0 of live/rated`; a lost axis counts as 0.3 (time to rotate a surviving group).
- `limp = lostMask != 0 || f < 0.5`.
- `limpCap = MaxSpeed × clamp(sqrt(f), Limp Min Speed Fraction, 1)`, sent in SVS bits 16-23 (m/s; 0 = none) with SVS bit 8.
- No fight-back while an axis is lost (item 3), so in RTB `_tvD` stays -1 and the `[Thrust]` PB lines thrust up for braking.

**Flight.**
- `double aBrake = SvBrake(MinAccel(mass, grav) * 0.55);` gives × 0.6 while limp.
- `cap = SvCap(cap);` before the final clamp.

**Gnat.** VectorFly already picks the main or reverse thruster. Only the caps apply.

**Test.** `--events "20:!kill-thrust F"` and `"20:!kill-thrust R"`, 3 seeds each. Check speed under the cap and no overshoot at home.

### Item 5. PB failover (Staff `SvWatch` replaces each Staff's `Watch()`)

**Takeover.**

```
down = _f2sN > 0 && (_tick - _f2sTick > 90 || !_fpb.IsWorking)        // unchanged detection
if (!down): if (_svHold) { _svHold = false; stop writing at once (no zeroing); send SVS with bit 9 = 0 immediately }  return
if (busy || SvDocked()) return     // busy = take-off burn (host passes burn || _lnS == 2; Basilisk: burn); SvDocked = an own merge IsConnected or a "[Bay" / Hive Mother PB on own grid
first tick: release overrides, dampeners on, AI blocks off, guns Shoot_Off (as today)
if (Limp Home On Flight Loss && home known (SVF home < 120 s old, or dynHome < 5 s) && _rc works) _svHold = true
while _svHold, every tick: limp-home controller -> nose to home; velocity command toward home at min(Limp Home Speed, sqrt(2*a*d));
    thrust split over all 6 axes like Flight ApplyThrust; d < 30 m and v < 2 m/s: release all, dampeners on, stay put (still _svHold, no writes)
SVS every 12 ticks with bit 9 = 1 while _svHold
```

Do not fly when docked or welded: the Staff's `_thr` would then be the host's thrusters.

**Return of the Flight PB** (one writer at every tick; at most about 4 ticks with nobody writing, overrides stay as left):
1. Flight runs again. `_lkMd = -9` after a restart, so the first `LkSend` sends F2S at once.
   - Flight yields: `SvYield` is true while `_tick < 6`, or while SVS is fresh with bit 9.
   - While yielding, Tick skips `AiStep()` and `Control()`. It still reads IGC and sends F2S and SVF (SVF bit 1 = yielding).
2. Staff sees F2S, stops writing, and sends SVS with bit 9 = 0 immediately.
3. Flight sees bit 9 cleared (or SVS stale for 0.6 s), calls per-script `SvResync()` (`_aiOn = !_aiOn; _firing = false;` plus `_aiKick = false` where it exists) and flies again.

**Rebuilt Flight PB (new EntityId).**
- While down, the Staff's `GetBlocks()` runs at 1 Hz (existing). It finds the new `[Flight]` PB and changes `_fk`.
- `Listen()` re-registers all `.SVF/.SVO/.MSL` listeners, keyed by `_lsKey`.
- The new Flight's start-up yield (`_tick < 6`) covers the gap until the Staff sees its first F2S.

**Keys.** `Limp Home On Flight Loss`, `Limp Home Speed`.

**Test.** F1 events `30:!disable-pb Flight; 45:!enable-pb Flight`, 3 seeds. F1's writer audit must report 0 ticks with two override writers.

### Item 6. Power protection (Flight, `SvPower(double load, bool recharging)`)

**Why.** Power priority serves weapons (5) before gyros (70) and thrusters (80). A charging small railgun draws about 3.6 MW (`_common.RAIL_RECHARGE_MW`).

**Where it applies.** Gatlings and rocket launchers draw almost nothing, so only the railgun drones call it (Longbow S/L, Mantis). The block code is generic over `_guns` whose `SubtypeId` contains "Railgun".

```
called every 10 ticks at the end of PowerAndHealth (Longbow, Mantis) with load = cur/max and SvRecharging() (per-script: time since the last confirmed shot < reload - 1 s)
brown = load > 0.97 && _powerScale < 0.6;  need = UnderFire || _mode == Mode.RTB || (SVS fresh && dodge bit)
if (Power Guard && brown && need && recharging && _now - _svRailOnT > Hold): rails Enabled = false, _svRailOffT = _now
if (rails off && (!brown || _now - _svRailOffT > Power Guard Hold (s))): rails Enabled = true, _svRailOnT = _now    // duty cycle >= 50 %
Release() and GetBlocks(): rails Enabled = true
```

**Not verified:**
- **In game:** does `Enabled = false` stop the capacitor draw and keep the stored charge?
- **In the sim:** A1 checks that `Weapons.cs` drops the `PowerSink` request of a disabled railgun.

**Test.** `run --filter "^1v1/(Longbow S|Mantis)-vs-Hornet$" --seeds 10`, A/B `Power Guard=false`. Trace `_powerScale`.

### Item 7. Blind-side orbit (Staff picks, Flight steers)

**Staff exposure model per enemy** (threat memory, item 19).
- **Samples S_i:** the bearing from the enemy centre to the place where a friendly was hit, whether own (SVF) or wing (`<Tag>_THREAT`).
  - Stored in the target frame when orientation is known: from `.SVO` for own, flag bit 3 for wing.
  - Otherwise stored in the world frame.
  - Weight = the reporter's integrity fraction lost in that event × exp(−age / 30 s).
- **Extra samples:** WEAKPOINT RELAY `_wpMap` entries of kind 2 (Weapons) for that target, weight 0.05 each. Their `s` (hull entry point, target frame) is a turret position.
- **Exposure:** E(b) = Σ w_i · max(0, b·S_i)². A turret on the far side is masked by the hull.

**Choice, 1 Hz.**
- Candidates: the current bearing c (target to me), c rotated ±30°, ±60° and ±90° about two axes perpendicular to c, and the gather slot bearing when one is assigned (item 20).
- Cost(b) = E(b) + 0.02 × angle(c,b)/30° + 0.05 × max(0, angle(b, slot) − 60°)/30°.
- Send the bearing only if minCost < 0.7 × E(c) and there is at least one sample.
- SVS B = unit bearing × desired tangential speed, `Blind Side Speed Factor` × the script's orbit speed (Staff reads it from the Flight's Custom Data, key per script), capped at MaxSpeed × 0.5. SVS bit 2 set.

**Flight, `Vector3D SvOrbit(Vector3D dir, Vector3D tan, double orbit)`.**
- Replaces `tan * orbit * _orbitSign`.
- If bit 2 is fresh and the SVS target id equals `_tgt.Id`: `c = -dir`, `p = normalize(B)`, `e = p - c·dot(c,p)`. If |e| > 0.17 rad, return `normalize(e) × |B|`.
- Otherwise return `tan × orbit × _svOs`.
- Gnat: not called (yo-yo runs).

**Failure modes.** No samples means no change. An unknown target rotation lets world-frame samples go stale (30 s decay).

**Test.** `pirate/3xHornet-vs-Pirate`, 20 seeds A/B `Blind Side Orbit=false`. Metric: damage taken per drone.

### Item 8. Standoff by enemy weapons (Staff memory, Flight `SvStand`)

**Staff, for the Flight's current target.**
- `theirR = max(classRange(cls), rObs × 1.05)`. Class ranges are the keys `Threat Range Bullet (m)`, `Threat Range Rocket (m)`, `Threat Range Long (m)`. `rObs` is the largest range at which this enemy hit someone.
- Unknown class and no observation: no advice.
- `floor = theirR + Threat Standoff Margin (m)` only if `floor < 0.95 × ownRange` (SVF aux.Y), else 0. With no range advantage there is no floor; the blind side does the work.
- SVS N.X = floor, bit 11.

**Flight, `double SvStand(double s, double lo, double hi)`.**
- Fresh, matching target id: `clamp(max(s, N.X) + N.Y, lo, hi)`. Otherwise `s`.
- Hooks:
  - Hornet family: `double e = dist - SvStand(StandoffFor(_tgt), MinRange, MaxFireRange - 30);`
  - Longbow and Mantis: `_engage = SvStand(EngageRange(_tgt), MinEngage, <rail range> - 30);`
  - Gnat: add `SvStand(BackOff, ...) - BackOff` to both `Back-off Range` and `Re-close Range`. A1 checks the Gnat variable names.
- It can only push out (floor) or move by the hand-off add (item 10). It never stops a fight.

**Test.** Covered by AB2 team runs. Unit check: a Longbow vs the Pirate gets a floor of 950 after one bullet-class hit report.

### Item 9. Missile or rocket warning: dodge and flares

**Flight, `SvSee(MyDetectedEntityInfo e)`.** First line of `Valid()`, next to `FaSee`.
- Accept: `e.Type == MyDetectedEntityType.Missile`, and Relationship not Owner, FactionShare or Friends (our own Wasp and Basilisk rockets are excluded).
- Send `.MSL (id, e.Position, e.Velocity)`, at most once per 6 ticks per id.
- This catches every detection path: turrets and CTCs (`PollDesignators`), cameras, sensors.

**Reality check.**
- Only Basilisk (+Seeker) and Longbow L Seeker carry a turret. The other drones see missiles mainly through wing reports (`<Tag>_MSL`) and lucky camera rays.
- Adding a turret controller to small drones is not proposed: a CTC broke pasting in v1.0 (HANDOFF section 3).

**Staff tracks.** Own `.MSL` plus wing `<Tag>_MSL`. Own sightings are relayed to the wing at ≤ 5 Hz per id, only if the missile closes on some friendly within 1.5 km.

```
per track (age < 1 s, extrapolated): r = p - me, vr = v - myV, tca = -dot(r,vr)/|vr|^2
threat if 0 < tca < 4 s and miss = |r + vr*tca| < Missile Dodge Radius (m)
dodge dir = normalize(me - (p + v*tca) projected perpendicular to vr) (miss ~0: RC.Up made perpendicular to vr); dodge = dir * MaxSpeed
SVS D = dodge, bit 3, until tca + 0.5 s has passed
flares: on a threat with tca < Flare Lead (s), at most once per Flare Interval (s): every own block whose SubtypeId contains "FlareLauncher": ApplyAction("ShootOnce")
```

**Flight, `Vector3D SvWant(Vector3D want, Vector3D me, double cap)`.** After `KzWant`: if the dodge is fresh, `want = D + want × 0.3`.

**Flight guns list (hook in every `GetBlocks`).** `_guns.RemoveAll(SvNotGun);` where `SvNotGun` = SubtypeId contains "Flare". A flare launcher is a `SmallMissileLauncher` and must never be fired by `SetFire`, be `_aimRef`, or count as ammo.

**Not verified in game:**
- what flares distract (assumed: homing missiles locked by players or AI; turrets probably not);
- whether turrets or CTCs report rockets as `Missile` in `GetTargetedEntity`.

The sim turrets never target rockets. F1's `!msl` event injects a track for testing.

### Item 10. Wing hand-off; damaged drones to the back (Staff)

**SWARM bits** (Flight ORs `SvSwarm()` into `BroadcastStatus`):
- 8 = DAMAGED: integrity < `Hand-off Integrity`, or limp.
- 16 = RETREATING: damage RTB.

**Staff standoff add (SVS N.Y, bit 11), re-evaluated at 2 Hz, held 5 s after the condition ends:**
- **Own drone under fire** (F2S bit 4) **and DAMAGED, with a fresh mate on the same target** (SWARM < 1.5 s, same Tgt, neither bit 8 nor 16, within 1.5 km): `+Hand-off Back (m)`. `SvStand` clamps it to ≤ own range − 30, so it keeps shooting.
- **Own drone fresh** (integrity ≥ 0.8, not under fire) **and a mate on the same target is DAMAGED:** `−Hand-off In (m)`, floor MinRange + 50.

**FOLLOW / SATELLITE.** The Staff's `SendFol` hook temporarily adds `Damaged Formation Extra (m)` to `FoDist` while own DAMAGED. The FOLLOW block is untouched. Not in Gnat (no FOLLOW block).

**Test.** `pirate/3xHornet-vs-Pirate`, 20 seeds, A/B `Wing Hand-off=false`. Metrics: losses, time alive.

### Item 11. Decoys (generator C1)

**Helper.** `_common.add_survival_kit(bp, outline=None, decoys=2, flares=1, projector=True, model=None)`. Call it in each model function before `add_support_pbs`, so printed copies carry the kit.

**Cells.**
- Candidates: inside the outline (default: the strict bounding box of the occupied cells, never ±1), not occupied, not in `bp.reserve` (exhaust, muzzle, camera lanes).
- The Gnat passes its 2 x 2 section explicitly. Any model whose spec gives a section does the same.

**Decoys.**
- Score: distance from the critical centroid (RC, PB, gyros, batteries); pairs at the maximum distance from each other.
- If no free cell: swap a 1-cell light armour block that covers no critical face (`swap_armour`).
- `bp.validate()` must pass: connected, mounts.
- Name: `<Model> Decoy n`. Aegis keeps its two roof decoys (skip). Midge gets decoys (scriptless, passive).

**Effect.** Turret aim goes to the nearest working decoy (sim `Decoys.cs`, wiki). A corner far from the core takes the hits.

**Tube reference.** The Tube flare-pod idea (detachable merge-latched decoy pods, `docs/design/tube_decoys.md` WIP) is NOT used on drones: a detachable pod outside the outline breaks the outline rule. A pod inside the outline would need a flush latch per pod (Pending row).

### Item 12. Flare launchers (generator C1, fired by the Staff)

- One `SmallMissileLauncher/SmallFlareLauncher` (1 cell, non-DLC, back mount) where a cell is free.
- Muzzle faces outward with `keep_clear(b, 3, 'muzzle')`.
- Preload: `Item.ammo('FlareClip', 2)` (magazine of 4).
- Name: `<Model> Flare [Flare]`.
- Gnat: probably no cell; recorded as skipped.
- Its definition has `gui_visible: false`: the owner checks that it pastes.
- The audit's `GUN_X` regex counts it as a gun (cosmetic). Tell the audit owner; do not edit their file.

### Item 13. Spaced armour (decision)

**Decision.** No shipped drone gets a skin-gap-core layer by removing structure. Every hull is 2-4 cells thick at the nose and flanks, so a gap would remove the only armour layer or a system cell.

**What C1 does.** Last step, only where the audit reports cover 0 on a critical block face (RC, PB, gyro, battery) and the next cell outward is free, inside the outline and not reserved:
- Add `SmallArmorPanelLight` or `LargeArmorPanelLight` (non-DLC).
- Plate on the far face of that cell (about 0.4 m air gap on small grid) if `validate()` accepts the mount; else on the near face (an extra layer, no gap).

**Testing.** Explosion occlusion is off in the default sim (`SimSettings.ExplosionOcclusion` exists: AB2 runs 10 seeds with it on). The real effect is in-game only.

### Item 14. Bury and spread critical blocks (generator C1, audit-guided)

Per model, run `python3 -I tools/survivability_audit.py --only "<Model>"` before and after. Goals, in order:
1. A second gyro where a cell inside the outline can seat one. The small gyro mounts on its bottom only. This removes the unwaived `gyro` single point of failure.
2. A second battery for drones with one (Gnat; record "no cell" if none).
3. Critical blocks with cover 0 get cover ≥ 1 by swapping with armour, where `validate()` and welder reach allow.

**Limits.**
- Never grow the outline.
- Never break printer welder reach. Run `python3 -I tools/designs/reach.py "<Printer>"` for every printer of the model (C3 re-solves). This is the hard build check that already fails Longbow and Mantis Foundry for buried blocks.
- Record a before/after audit score table in this doc (section 2, item 14 results).
- Update `tools/tests/golden_hashes.json` with `run_all.py --update-golden`.

### Item 15. Self-projector (generator C1 + C2, Staff REPAIR CLIENT)

**Block.** One `SmallProjector` (or `LargeProjector` for large grid). Enclosed cell, but welder-reachable for printable models. Named `<Model> Projector [Self]`. `Enabled=false` at paste.

**Settings.**
- Built by `printer_lib.projected_grid_xml()` from a deep copy of the drone.
- The copy has its own projector's `ProjectedGrids` stripped: no recursion.
- Offset: the ghost's first block lands on the real first block. Projector identity orientation: `ProjectionOffset = (A - P)` in projector-local cells, where A is the anchor cube min and P the projector cell. Rotation (0,0,0). `KeepProjection` true.
- C1 adds a unit test (`tools/tests/test_survival_kit.py`): every ghost block cell equals its real block cell.

**Printers (C2).**
- `projected_grid_xml` must strip the `ProjectedGrids` of every projector inside the drone ghost.
- So a printed drone's self-projector is empty. It still works with the printer's own projection at a bay. A medic can then only fix damaged blocks, not rebuild destroyed ones.

**Staff.**
- Enables the projector while waiting for a medic, or docked on a cradle bay that has no projector.
- Disables it at a printer bay (whose projector covers the drone) and when returning to duty.
- Reports `RemainingBlocks` on its screen.

**Blueprint size.** It roughly doubles (the ghost carries the PB programs). Accepted.

**Not verified in game:**
- the XML pastes;
- only missing blocks show;
- a rebuilt PB keeps its program. HiveMotherAI v2.5 notes "a welded PB compiles by itself", which supports it.

### Item 17. Repair bay (D1 bay scripts, B1 dock controller, C2 dock keys, C3 Repair Pod)

**Smallest design that delivers.** The existing printer bays become repair bays:
- Projector, welders, merge and `[Stock]` are already there.
- Welder reach is solved and verified against the printed pose.
- The merge pair puts a returning drone back exactly on its print pose, so the bay's projection overlays it and the welders rebuild missing blocks.

Printers: Hornet Forge, Wasp/Lancer Foundry, Harrier Forge, Aegis Shipyard, Watcher Pad, Fleet Forge, Escort Carrier, plus Longbow and Mantis Foundry when they build.

**Who can dock.**
- Drones with an own merge block. A pasted Longbow S has none; the printed one has the latch.
- Not the Gnat: belly merge plus axial thrust cannot descend belly-first.
- Not Basilisk or Longbow L: no merge.

**Cradle bays (`CradleBayAI`).**
- Get the same protocol.
- A bay with welders named `[Bay n]` repairs through the drone's own self-projection; a bay without welders is dock-only (recovery and recharge).
- C3 adds a "Repair Pod" model (cradle pod variant with welders, `[Stock]` and conveyors, reach-checked).

**C2 printer and pod Custom Data keys.** Per bay: `Dock Forward=x,y,z` and `Dock Up=x,y,z`. These are the docked drone RC's Forward and Up in host grid axes, from `printer_lib` / `pods.drone_pose`. Bays without the keys advertise no docking.

**D1, HiveMotherAI.**
- Advertise `<DTag>_RBAY` every 2 s per bay that is idle (not printing, merge on, box empty) and has dock keys.
  - `clampPos` = merge position + clamp normal × half a cell.
  - `axis` = the host merge clamp normal, `Merge.WorldMatrix.Up`: the `MERGE_CLAMP_FACE_SMALL` convention, unverified in game, as the printers assume.
  - `dockFwd`/`dockUp` in world.
- `_RREQ`: reserve the bay for 90 s (no new print starts) and answer `_RACK`.
- In IDLE or reserved, a connected merge plus a drone found in the Drone Box enters the BUILD → FINISH → CHARGE → ARM → RELEASE cycle. The `Max Drones` gate is skipped. LCD shows `REPAIR`. FINISH already accepts an already-placed drone ("rem==0 for 15 s").
- **CradleBayAI:** a new state `REPAIR` between DOCKED and CHARGING:
  - welders on and the drone's `Projector [Self]` on;
  - leave when the projector reports `RemainingBlocks == 0` and a FINISH-like pass finds every drone FatBlock functional and full integrity in the Drone Box;
  - then the normal CHARGING → READY → ARMING → RELEASED.

**B1, Staff REPAIR CLIENT dock controller.**

```
preconditions: own merge block (not "Flare"), damage latch set, Flight mode RTB or IDLE, not under fire, fresh RBAY of own Tag within 4 km with free bit,
               host clampPos moved < 2 m between two ads (stationary host only)
REQUEST: send _RREQ, wait _RACK ok (5 s; else try another bay after 10 s)
goal pose: R* = (Forward dockFwd, Up dockUp); merge goal Pm* = clampPos + axis * half cell;
           o = merge position - RC position in drone-local axes (taken once); RC goal G = Pm* - R* * o
TRANSIT: to L = G + axis * (20 + drone radius) at cruise; attitude toward L, R* inside 100 m      (FOL bit 2 set: Flight lets go)
ALIGN:   hold at L until attitude error < 2 deg and |v| < 0.3 m/s (max 15 s)
INSERT:  along -axis at clamp(0.5*along, Dock Final Speed, Dock Speed); lateral PD v_lat = -1.0*latErr;
         |latErr| > 0.8 m with along < 3 m -> back out to L, retry (max 3, then give up and call a medic)
CONTACT: merge IsConnected -> release overrides, dampeners on, clear FOL bit 2; wait. PRINTER GUARD holds the Flight;
         the bay repairs and releases; the LAUNCH block re-arms on "a merge pair locks" and flies the take-off
```

**Not verified in game:** the clamp face, snap tolerance, approach accuracy, and the full repair cycle. The sim has `MergeApi`, but projectors and welders do not build there.

### Item 18. Medic / tender drone (E1)

**Design: new `tools/designs/medic.py`, model "Medic", kind drone.** Added to `DRONE_MODULES` in `tools/designs/__init__.py`. Not added to the sim scenario lists (unarmed).
- One `SmallShipWelder` (3x3x6) on the nose axis, tip forward.
- Core behind it: RC, `[Flight]`-less single PB running MedicAI, 2 gyros, 4 small batteries, 12 ion thrusters (2 per axis), antenna, forward, side and rear cameras.
- 2 x `SmallBlockSmallContainer` conveyor-linked to the welder's back port. Reuse the printers' conveyor routing; `validate()` plus a port check.
- Item 11 decoys and item 15 projector.
- Components preloaded for one Hornet rebuild, computed from `defs_stats.json` in `medic.py`.

**Script: new `scripts/MedicAI.cs`, single PB, about 26k minified.**
- Flight core copied from Hornet: `GetBlocks`, `_thrDir`, `Aim` PD, `ApplyThrust`, `MinAccel`, `Release`.
- Modes:
  - IDLE: hold home; `home` command.
  - GO: cruise to the patient.
  - APPROACH: within 60 m, ≤ 5 m/s relative.
  - WORK: tip target = work point + outward normal × 0.6 m. The tip is the welder position + forward × 1.25 m. Nose points at the work point. Hold relative to the patient's velocity. Welders on. Stay until the patient's next WORK message drops that point, or 20 s.
  - NEXT, then RETURN.
- Safety:
  - Keep the medic centre outside `patient radius + medic radius + 1 m`, except along the work point's normal.
  - ≤ 0.5 m/s inside 3 m.
  - Abort to standoff if the patient's relative speed > 1 m/s, the patient is under fire (packed bit), or own integrity drops.
  - Refuse jobs with cargo below 10 %: `STOCK LOW`.
- Custom Data `[Medic]`: `Medic Channel=MEDIC`, `Work Hold (s)=20`, `Approach Speed=5`, `Work Speed=0.5`, `Tip Offset (m)=0.6`, `Cruise Speed=40`, `Max Job Range (m)=3000`.

**Patient side, Staff REPAIR CLIENT.**
- When no repair bay is available within 4 km, the Flight is IDLE (damage RTB ended at home) and a free `MEDIC_AD` is within `Max Job Range`: send `REQ`.
- On `ACK`: own projector on. Broadcast `WORK` at 1 Hz with up to 2 work points (world, nearest to the medic first), patient centre, velocity, radius, remaining count.
- **Work points:**
  - At start, an incremental own-grid cell scan (200 cells per tick) records occupied cells.
  - While waiting, a re-scan every 2 s gives missing cells (`CubeExists` false where a block was) and damaged blocks (integrity < max).
- **Done:** none left and projector `RemainingBlocks == 0`. Send `WORK` with the done bit, projector off, `Fwd(_svPrevVerb)` to resume duty.

**Test.** E2 uses F2's `survival-test --mode medic` for approach and hold geometry only. Welding is in-game only.

### Item 19. Last-gasp report and threat memory (Staff; Flight reports hits)

**Flight `CheckDamage` hook.**
- In the `if (cmp && _slimHp[i] - v > 0.5)` branch: `_svNb++; _svMax = Math.Max(_svMax, _slimHp[i] - v);`.
- At the end: `SvDamage();`. It keeps `_svI0`, the most integrity seen; reset only when PRINTER GUARD lets go of a printer, i.e. a fresh or repaired drone.
- On a hit it sends SVF at once.
- **Attacker attribution:** the live contact whose direction is within 35° of `_hitDir`, ties to `_tgt`. It goes in SVF Item2 and Item4, and aux.Z gives its range.

**Staff.**
- **Weapon class:**
  - explosive (2): a missile closed on me within 3 s, or `nb ≥ Explosive Hit Blocks` with `maxDrop ≥ Explosive Drop Min`;
  - long (3): attacker range > 1,000 m;
  - otherwise bullet (1).
  - A heuristic: AB2 calibrates both thresholds in the sim.
- **dps fraction:** integrity % change per second over 2 s windows.
- **Last gasp:** integrity fell ≥ `Last Gasp Drop` within 2 s, or is < 0.35. Broadcast `<Tag>_THREAT` immediately with flag bit 2. Also on the fleet channel `<ch>_THREAT` when FLEET LINK has a channel.
- **Normal reports:** ≤ 1 per enemy per 2 s, when an attributed hit happens.
- **Memory per enemy:** up to 8 enemies, each with up to 16 samples.
  - Fields: class (highest confidence), `rObs` (max, decays 1 %/s after 60 s), dps EWMA, hit-bearing samples (item 7), `Threat Memory (s)` expiry.
  - Fed by own SVF and wing THREAT reports.
- **Uses:** standoff floor (item 8), blind side (item 7), the jink enemy speed (SVS N.Z), screen line `Threat: <cls> r<m> dps<x>`.
- **Mothership / ship memory:** Pending (section 10).

### Item 20. Attack together: a bounded gather window (Staff; Flight flies the point)

**Owner rule.** A drone never refuses or aborts a fight. With no wingmate available it engages at once, as today. No odds-based retreat.

**Staff.**
- **Starts:** when F2S shows a new target id, range > own range + 200, not under fire, mode a fight mode (per-script hook `SvFightMode()`), `Attack Together` on, and no window for this target in the last 60 s.
- **Participants:** me plus fresh mates (SWARM < 1.5 s) with Tgt == T or Tgt == 0, not RETREATING, within `Gather Mate Radius (m)`.
- **None:** no window.

```
P sorted by grid id, n = |P|, my slot k; axis a = normalize(centroid(P) - targetPos)
e1 = normalize(cross(a, gravity up or world +Y)), e2 = cross(a, e1); phi = min(Gather Spread, 30 deg * (n - 1))
b_k = a*cos(phi) + (e1*cos(2*pi*k/n) + e2*sin(2*pi*k/n))*sin(phi)
G_k = targetPos + b_k * (standoff + Gather Extra Range (m))       -> SVS A, bit 0 (GATHERING, SWARM 64)
READY (SWARM 32) when |me - G_k| < 150
end the window when: all participants READY, or Gather Max (s) elapsed, or under fire, or target within own range, or target changed, or mode changed
after the window: b_k stays the blind-side sector anchor for 60 s (item 7)
```

**Flight.** Target branch: `if (SvGather(me, dist, out gv)) want = gv + Evade() + Separation(me); else want = <existing>`.
- `SvGather` is true while bit 0 is fresh, the target id matches, dist > `SvRange()` and not `UnderFire`.
- `gv` = toward A at MaxSpeed.
- Aim and fire are untouched: the guns still shoot anything in range.

**Note.** Mixed-type wings (Hornet + Harrier) have different SWARM tags and do not gather. Accepted.

---

## 3. Custom Data

All new keys live in one `[Survival]` section in the **Flight PB's** Custom Data. That is one place for the owner.

**Who writes and reads.**
- The Staff writes missing defaults into `_fpb.CustomData`. It does so only after the first F2S of the current Flight key, after re-parsing right before writing. That avoids the start-up race with Flight's `LoadConfig` write-back.
- The Flight reads its keys with `Get` only (no `Set`). Its compiled defaults must equal the Staff's.
- Both re-read on `reload`.

| Key | Default | Read by | Item |
|---|---|---|---|
| Lead-Breaking Jinks | true | Flight | 2 |
| Jink Accel Fraction | 0.8 | Flight | 2 |
| Enemy Projectile Speed (m/s) | 800 | Flight | 2 |
| Jink Period Min (s) | 0.4 | Flight | 2 |
| Jink Period Max (s) | 3 | Flight | 2 |
| Jink Period Random | 0.3 | Flight | 2 |
| Power Guard | true | Flight (Longbow, Mantis) | 6 |
| Power Guard Hold (s) | 2 | Flight | 6 |
| Damage RTB | true | Staff | 3 |
| RTB Integrity Below | 0.55 | Staff | 3 |
| RTB On Lost Thrust Axis | false (AB2; was true) | Staff | 3 |
| RTB Gyro Fraction | 0 (AB2; was 0.5: all gyros gone) | Staff | 3 |
| RTB On Lost Forward Cameras | false (AB2; was true) | Staff | 3 |
| Retreat Margin (m) | 300 | Staff | 3 |
| Fight Back While Retreating | true | Staff | 3 |
| Limp Min Speed Fraction | 0.4 | Staff | 4 |
| Limp Home On Flight Loss | true | Staff | 5 |
| Limp Home Speed | 30 | Staff | 5 |
| Blind Side Orbit | true | Staff | 7 |
| Blind Side Speed Factor | 2 | Staff | 7 |
| Standoff By Enemy Weapons | true | Staff | 8 |
| Threat Standoff Margin (m) | 150 | Staff | 8 |
| Threat Range Bullet (m) | 800 | Staff | 8, 19 |
| Threat Range Rocket (m) | 800 | Staff | 8, 19 |
| Threat Range Long (m) | 2000 | Staff | 8, 19 |
| Missile Dodge | true | Staff | 9 |
| Missile Dodge Radius (m) | 25 | Staff | 9 |
| Flare On Missile | true | Staff | 9, 12 |
| Flare Interval (s) | 1.5 | Staff | 9, 12 |
| Flare Lead (s) | 2 | Staff | 9 |
| Wing Hand-off | true | Staff | 10 |
| Hand-off Integrity | 0.75 | Staff | 10 |
| Hand-off Back (m) | 200 | Staff | 10 |
| Hand-off In (m) | 100 | Staff | 10 |
| Damaged Formation Extra (m) | 40 | Staff | 10 |
| Share Threats | true | Staff | 19 |
| Threat Memory (s) | 120 | Staff | 19 |
| Last Gasp Drop | 0.15 | Staff | 19 |
| Explosive Hit Blocks | 3 | Staff | 19 |
| Explosive Drop Min | 400 | Staff | 19 |
| Attack Together | true | Staff | 20 |
| Gather Max (s) | 6 | Staff | 20 |
| Gather Spread (deg) | 50 | Staff | 20 |
| Gather Extra Range (m) | 150 | Staff | 20 |
| Gather Mate Radius (m) | 2000 | Staff | 20 |
| Repair At Bay | true | Staff | 17 |
| Dock Speed (m/s) | 2 | Staff | 17 |
| Dock Final Speed (m/s) | 0.3 | Staff | 17 |
| Call Medic | true | Staff | 18 |
| Medic Channel | MEDIC | Staff | 18 |

**Moved keys (same names, same `[<Model>]` section, so existing configs keep working).** The Staff now reads them and writes their defaults; the Flight stops reading them:
- the seven `Search Patrol ...` keys;
- `Listen To IGC Tags (target feeds)`;
- `Seeker Group (PowerSystems/Weapons/Propulsion)`;
- `Seeker Turret Guides Aim`.

The `Target Small/Large Grids` and `Target Neutral/Unowned` keys stay Flight keys. The Staff also reads them for the sensor setup.

**Changed meaning.** `Jink Speed` is now the jink amplitude cap. `Jink Interval (s)` and `Jink Interval Random (s)` are ignored.

**Bays and pods** (written by C2 generators):
- `[Bay N] Dock Forward=x,y,z`
- `[Bay N] Dock Up=x,y,z`
- `[Bay N] Repair=true` (HiveMother and CradleBay)

**Staff words added to `Local()`:** `fight` (clear the damage latch), `repair` (start the bay or medic search now), `dock`, `seeker on|off` (moved).

---

## 4. IGC specification

### 4.1 In-construct link

- All channels use `TransmissionDistance.CurrentConstruct`.
- Key `K = "WSPL" + <Flight PB EntityId>`. Both PBs run Update1.
- Existing channels (`.F2S .FOL .S2F .CMD .TGT .FLT .WPF .WPT`) are unchanged, except that `.WPT` gets flag bit 4096.
- The `[Aux]` PB link (`.SDK .DKS .SKW .D2S`, plus `.S2F` from the Aux after a medic repair) is specified in docs/design/staff_split.md section 4.

| Tag | Direction | Tuple | Rate |
|---|---|---|---|
| `.SVF` | Flight → Staff | `MyTuple<int flags, long attackerId, Vector3D hitDir, Vector3D attackerPos, Vector3D aux, Vector3D home>`. Flags: bit0 hit in this report, bit1 Flight yielding, bits 8-15 integrity % of max (255 unknown), bits 16-23 blocks hit in the last check. aux = (largest single-block drop, own max fire range `SvRange()`, attacker range or 0). | 2 Hz; at once after a `CheckDamage` with a hit (≤ 3 Hz); at once after a run gap or a yield change |
| `.SVO` | Flight → Staff | `MyTuple<long tgtId, Vector3D tgtFwd, Vector3D tgtUp, Vector3D tgtVel, double tgtRadius, int flags>`. flags bit0 = orientation valid (`HasOri`). | 2 Hz while `_tgt != null` |
| `.MSL` | Flight → Staff | `MyTuple<long missileId, Vector3D pos, Vector3D vel>` | per sighting, ≤ 1 per 6 ticks per id |
| `.SVS` | Staff → Flight | `MyTuple<int flags, long tgtId, Vector3D A, Vector3D B, Vector3D D, Vector3D N>`. Flags in the next table. A = gather point or via point (via wins). B = orbit bearing × tangential speed. D = dodge velocity. N = (standoff floor, standoff add, enemy projectile speed or 0). | every 12 ticks (5 Hz); at once on a change of bits 0-11; full resend on a new Flight key, on the first F2S after `_down`, and on bit 9 release. Fresh at Flight: 0.6 s |
| `.PAT` | Staff → Flight | `MyTuple<Vector3D waypoint, double speed, int flags>`. bit0 leg active (Flight flies to the waypoint, AI blocks off), bit1 listening stop. | every 30 ticks + on change. Stale 1.5 s = no patrol |
| `.WPT` | Staff → Flight (existing) | new flag value 4096 = this hole is the drone's own seeker finding. Flight calls `BreachSet(s, d, true)` (the old direct seeker path) instead of the adopt cone. | 2 Hz while an own seeker finding is fresh, else 1 Hz (as now) |

`.SVS` flag bits:

| Bit | Meaning |
|---|---|
| 0 | gather active (A) |
| 1 | retreat via active (A) |
| 2 | orbit bearing valid (B) |
| 3 | missile dodge active (D) |
| 4 | DAMAGED |
| 5 | RETREATING |
| 6 | GATHER READY |
| 7 | GATHERING |
| 8 | LIMP |
| 9 | STAFF HOLDS OVERRIDES: Flight yields |
| 10 | FIGHT BACK in RTB |
| 11 | standoff advice valid |
| 16-23 | limp speed cap in m/s (0 = none) |
| 24-27 | threat class of `tgtId` |

### 4.2 Wing and fleet channels

These use the default broadcast distance (antenna), like `_SWARM`.

| Channel | Tuple | Sender, rate |
|---|---|---|
| `<Tag>_THREAT`, also `<ch>_THREAT` if a FLEET LINK channel is set | `MyTuple<long enemyId, Vector3D enemyPos, Vector3D bearing, Vector3D stats, long reporterGrid, int flags>`. stats = (hit range m, dps fraction/s, reporter integrity 0-1). flags: bits 0-1 class, bit 2 LAST GASP, bit 3 bearing in the target frame. | Staff; event-driven, ≤ 1 per enemy per 2 s; last gasp immediately |
| `<Tag>_MSL` | `MyTuple<long missileId, Vector3D pos, Vector3D vel, long senderGrid>` | Staff; ≤ 5 Hz per missile, only missiles closing on a friendly within 1.5 km |
| `<Tag>_SWARM` (existing) | new flag values 8 DAMAGED, 16 RETREATING, 32 GATHER READY, 64 GATHERING (from `SvSwarm()`) | Flight, unchanged rate. Checked: no script reads values ≥ 8 today; ShrikeAI and kz use 1, 2, 4. |

### 4.3 Repair and medic channels

| Channel | Tuple | Sender, rate |
|---|---|---|
| `<DTag>_RBAY` | `MyTuple<long bayPbId, int bayAndFlags, Vector3D clampPos, Vector3D axis, Vector3D dockFwd, Vector3D dockUp>`. bayAndFlags = bay \| flags<<8; flags bit0 free, bit1 can repair, bit2 stock ok. | HiveMother / CradleBay, every 2 s per eligible bay |
| `<DTag>_RREQ` | `MyTuple<long bayPbId, int bay, long droneGrid>` | drone Staff, once, retry after 5 s |
| `<DTag>_RACK` | `MyTuple<long droneGrid, long bayPbId, int bay, int ok>` | bay, on request |
| `<M>_AD` (M = `Medic Channel`) | `MyTuple<long medicGrid, Vector3D pos, int flags>` (bit0 free, bit1 stock ok) | MedicAI, every 2 s |
| `<M>_REQ` | `MyTuple<long medicGrid, long patientGrid, Vector3D pos, int missing>` | patient Staff |
| `<M>_ACK` | `MyTuple<long patientGrid, long medicGrid, int ok>` | MedicAI |
| `<M>_WORK` | `MyTuple<long patientGrid, Vector3D centre, Vector3D vel, Vector3D p0, Vector3D p1, int packed>`. packed = remaining (0-4095) \| radius in dm << 12 \| done << 24 \| under fire << 25. | patient Staff, 1 Hz while serviced |

### 4.4 Sync summary

- **Start-up.**
  - Flight yields for 6 ticks, then flies unless SVS bit 9 is fresh.
  - The Staff writes `[Survival]` defaults only after the first F2S.
  - The Staff sends a full SVS on the first F2S from a new key.
- **Re-bind.** The Staff finds the nearest `[Flight]` PB every 600 ticks, on a grid change, and at 1 Hz while down (existing). A new key re-registers `.SVF/.SVO/.MSL` and resets the survival mirror state; the threat memory is kept.
- **Run gap.**
  - Flight with dt > 0.5: `_svSentT = -99` (SVF and SVO resent next tick) plus the existing AI/gun resync.
  - Staff after `_down`: full SVS at once.
- **Failover handshake:** item 5.

---

## 5. PB budget table

All values are minified characters (`python3 -I tools/minify_cs.py`), measured at bd7817a. The new block estimates are deliberately generous:
- SURVIVAL LINK about 4.5k plus 0.5k of hooks;
- STAFF SEARCH about 7.5k (patrol 2.6, sensors 1.3, feeds 0.6, seeker 3.0);
- SURVIVAL STAFF about 12k;
- REPAIR CLIENT about 4k.

### 5.1 Flight PBs

Moved out to that drone's Staff PB:
- patrol −1.85k net: −2.2k code and keys, +0.35k PATROL LINK receiver;
- sensor setup −1.2k (`ConfigureSensors`, `SetF`, `SetB`);
- feeds −0.4k;
- seeker −2.7k (`SeekerSetup`, `SeekerTick`, `SeekerShotSpeed`, fields, 2 keys, command).

| Script | Now | + SURVIVAL LINK (net of removed `Evade`/jink) | Moves out | After (estimate) |
|---|---|---|---|---|
| HornetDroneAI | 80.8k | +4.3k | patrol, sensors, feeds, seeker (−6.15k) | ≈ 79.0k (AB2 measured 79,645) |
| HarrierAI | 81.9k | +4.3k | same (−6.15k) | ≈ 80.1k (80,703) |
| LancerAI | 82.3k | +4.3k | same (−6.15k) | ≈ 80.5k (81,085) |
| AegisAI | 84.8k | +4.3k | same (−6.15k) | ≈ 83.0k (83,633) |
| WaspFlightAI | 86.5k | +4.3k | same (−6.15k) | ≈ 84.7k (85,587) |
| BasiliskAI | 87.7k | +4.3k | same (−6.15k) | ≈ 85.9k (86,831) |
| GnatSwarmAI | 84.0k | +4.65k (no jink) | sensors, seeker (−3.9k) | ≈ 84.8k (85,733) |
| LongbowSniperAI | 84.1k | +4.45k (incl. power guard hook) | sensors, seeker (−3.9k) | ≈ 84.7k (85,628) |
| MantisAI | 87.1k | +4.45k | sensors, seeker (−3.9k) | ≈ 87.7k (88,646) |

**Why the moved modules are off the hot path.**
- Patrol: only HUNT with no target.
- Sensor setup: one-time property writes.
- Feeds: IGC already delayed by other ships. They take the existing coalesced `.TGT` relay.
- Seeker: samples at 2 Hz, and its result is a target-frame hole that is valid for seconds.

**Within budget?**
- No Flight PB grows by more than about 0.7k. Every one stays under the 100k cap; no `SCRIPT_LIMITS` change.
- The six over 80k were above the budget before this work.
- If a later feature needs room, the next documented cut is the search-phase camera auto-scan (`AutoScanStep`, about 1k) to the Staff, with a `.SCN` contact channel.
- AB2 measured the real numbers (in brackets, `tools/minify_cs.py`, 2026-10-17): every Flight PB is within its estimate + 1k (Basilisk 86.8k of 86.9k, Mantis 88.6k of 88.7k are the tightest).

### 5.2 Staff PBs and other scripts

| Script | Now | Added | After (estimate) |
|---|---|---|---|
| Hornet, Harrier, Lancer, Aegis, Longbow, Mantis, Wasp Staff | 37.9k each | +7.5 STAFF SEARCH +12 SURVIVAL STAFF +4 REPAIR CLIENT, −0.6 old `Watch()` | ≈ 60.8k |
| GnatStaffAI | 27.5k | same +0.8 (HIVE MATES lite and `_sensors` back) | ≈ 51.2k |
| BasiliskStaffAI | 28.2k | same (no dock: no merge; medic only) | ≈ 51.1k |
| HiveMotherAI | 36.4k | +3k (RBAY/RREQ/RACK, repair entry) | ≈ 39.4k |
| CradleBayAI | 17.6k | +4.5k (protocol, REPAIR state) | ≈ 22.1k |
| MedicAI (new) | n/a | n/a | ≈ 26k |
| ThrustVectorAI | 7.3k | none | 7.3k |

Measured after staff_split (2026-10-17, `tools/minify_cs.py`):

| Script | Minified |
|---|---|
| Hornet / Harrier / Lancer / Aegis / Wasp Staff | 72,652 / 72,660 / 72,652 / 72,644 / 72,639 |
| Longbow / Mantis Staff | 72,705 / 72,697 |
| GnatStaffAI / BasiliskStaffAI | 73,805 / 73,548 |
| AuxAI (new, the `[Aux]` PB of the 7) | 19,449 |

Before the split the 7 were at 82.9-83.0k. The 7 split drones carry a fourth PB, `[Aux]` (docs/design/staff_split.md); no other drone gets one.

---

## 6. Work packages

Every package:
- edits only the files it owns in that phase;
- states the interface contract it relies on (sections 3 and 4 are the contract);
- ends with its tests green;
- commits to `main` with a retrofit note when it applies;
- says the zips are not rebuilt.

Seed counts follow CLAUDE.md. Say in one line what was run and why.

### Phase 0: registry (one agent, short, before anything else)

**P0 owns:**
- `tools/blocks.json`, `tools/blocks_sync.py`;
- skeletons `tools/pl_block.cs`, `tools/sv_block.cs`, `tools/ss_block.cs`, `tools/svs_block.cs`, `tools/rc_block.cs`;
- `docs/design/survivability.md` (this text), `docs/retrofit.md`;
- for the single apply run only: the 9 Flight and 9 Staff scripts.

**Steps:**
1. Add `--only NAME[,NAME]` to `blocks_sync.py` so parallel packages apply only their own blocks.
2. Register the 5 blocks (section 1.2).
3. Write skeleton files: marker lines plus one comment line.
4. `python3 -I tools/blocks_sync.py --apply`.
5. `tools/compile_check.sh`.
6. `python3 -I tools/blocks_sync.py --check`.
7. Add the section 10 rows to retrofit.

**Done when:** all scripts compile, check is green, and the doc is saved.

### Phase 1: parallel

#### A1, Flight

**Owns:**
- the 9 Flight scripts;
- `tools/pl_block.cs`, `tools/sv_block.cs`;
- `tools/aim_block.cs`, `tools/aim_sync.py` (remove the seeker HOOKS and the seeker parts of `OLD` hooks).

**Steps:**
1. Make room:
   - PATROL LINK replaces `PatrolStep()`. `bool PatrolStep()` reads `.PAT` and sets `_patrolLeg`/`_patrolWp`. Remove the patrol keys, fields, and the AI-move detection in `AiStep`. `PlInit()` goes next to `LkInit()`.
   - Remove `ConfigureSensors`/`SetF`/`SetB` and their calls.
   - Remove `RegisterFeeds`, `_lFeed` and the `ListenTags` key.
   - Aim block v1.3: delete the SEEKER parts. In `WpTick`/`WpAdopt`, take `_wpF & 4096` as `BreachSet(_wpS, _wpD, true)`. Seek status moves to the Staff screen.
2. Write SURVIVAL LINK (section 2 functions) and add the hooks (next list).
3. Delete each script's `Evade()` and jink code, and the `Jink Interval` keys.
4. Add the missing host fields per script (e.g. `bool Jink` in Longbow, Mantis, Gnat) and the per-script hooks `SvRange()`, `SvResync()`, `SvRecharging()`.
5. Apply: `blocks_sync.py --apply --only "PATROL LINK,SURVIVAL LINK"` and `aim_sync.py --apply`.

**Hook list (Hornet names; adapt per script):**
- ctor: `SvInit();` after `LkInit();`
- Tick: `ReadIGC(); LkRead(); SvRead();`
- Tick: `if (!LkBurn && !SvYield && !AiStep()) Control(dt);`
- Tick: `LkSend(); SvSend();`
- run-gap line: `_svSentT = -99;`
- `LoadConfig`: `SvLoad(_ini);`
- `GetBlocks`: `_guns.RemoveAll(SvNotGun);`
- `Valid()`: `SvSee(e);`
- `CheckDamage`: counters plus `SvDamage();`
- Control: `SvStand`, `SvJink`, `SvOrbit`, `SvGather`, `SvHome`/`SvViaOn`/`SvFightBack`, `SvBrake`, `SvCap`, `SvWant`
- `BroadcastStatus`: `| SvSwarm()`
- `Display`: `+ SvStatus()`
- `Release()`: guard first line
- Longbow and Mantis `PowerAndHealth`: `SvPower(load, SvRecharging())`

**Contract.** Sections 3 and 4.1 exactly: tuple types, bits, key names, defaults.

**Tests:**
- `tools/compile_check.sh` (9 scripts).
- `blocks_sync.py --check` (includes the legacy aim, kz and lead checks).
- Minified sizes: report the table.
- `sim/build.sh`, then `run --filter "^1v1/(Hornet-vs-Hornet|Gnat-vs-Gnat|Longbow S-vs-Wasp|Mantis-vs-Hornet)$" --seeds 3` as a smoke test: runs, no exceptions. Patrol, seeker, damage RTB etc. stay inert until B1 lands.

**Done when:** compiles, checks green, every Flight ≤ its section 5.1 estimate + 1k, smoke clean.

#### B1, Staff

**Owns:**
- the 9 Staff scripts;
- `tools/ss_block.cs`, `tools/svs_block.cs`, `tools/rc_block.cs`, `tools/wp_block.cs`.

**Steps:**
1. STAFF SEARCH:
   - patrol: logic moved from Flight. Inputs: F2S mode, tgtId and bits; own RC motion; SVF home; `_mates`. Sends `.PAT`.
   - sensor setup in `GetBlocks`;
   - feeds into `Relay()`;
   - seeker: turret plus camera ray. Own-seeker entries enter `_wpMap` with `Snd` = own grid. Ranked first in WEAKPOINT RELAY v1.1 and sent with bit 4096.
   - per-script consts `const string FS = "<Model>"; const bool SsPatrol = ...;`
2. SURVIVAL STAFF: items 3, 4, 5 (`SvWatch` replaces `Watch()`), 7, 8, 9, 10, 19, 20; the `[Survival]` default writer (`SvCfg`).
3. REPAIR CLIENT: items 15, 17, 18 patient side; shares the flight primitives with limp-home.
4. Per-script hooks:
   - `SvsRead()` after `ReadF2S()`;
   - `SvWatch(burn || _lnS == 2)` (Basilisk: `burn`), also in the no-RC path;
   - `SendFol(burn || SvFly)` and the FoDist extra;
   - `SvListen()` in `Listen()`, `SvBlocks()` + `SsBlocks()` in `GetBlocks`, `SvCfg(ini)` + `SsCfg(ini)` in `LoadFlightCfg`;
   - `SvStatus()` in `Display`;
   - the `Local()` words;
   - hooks `SvFightMode()`, `SvVerb()`.
5. GnatStaffAI: add the HIVE MATES lite section (as in HornetStaffAI) and `_sensors`.
6. Apply: `blocks_sync.py --apply --only "STAFF SEARCH,SURVIVAL STAFF,REPAIR CLIENT,WEAKPOINT RELAY"`.

**Contract.** Sections 3 and 4. Never write gyro or thruster overrides except under `_svHold` (item 5) or with FOL bit 2 set (dock), and never while `SvDocked()`.

**Tests:**
- compile; check;
- `follow-test --count 3` (FOLLOW unchanged);
- `dock-launch` (LAUNCH unchanged);
- the A1 smoke filter, 3 seeds.

**Done when:** green, Staff sizes reported.

#### C1, drone designs

**Owns:**
- `tools/designs/_common.py`, `core.py`, `extra.py`, `wasp.py`, `support.py` (Watcher, Midge decoys);
- `tools/tests/golden_hashes.json`;
- new `tools/tests/test_survival_kit.py`.

**Steps:**
1. `add_survival_kit` (items 11, 12, 15) with the outline parameter. Call it in hornet, harrier, lancer, aegis, gnat, longbow_s/l/l_seeker, wasp, mantis, basilisk (+seeker), watcher; decoys only for midge. Names exactly `<Model> Decoy n`, `<Model> Flare [Flare]`, `<Model> Projector [Self]`.
2. Item 14 per the audit.
3. Item 13 panels.
4. Before/after audit table into this doc's item 14 results. Leave the file to G3 in Phase 3: hand the table over.
5. `python3 -I tools/tests/run_all.py --fast`.
6. `--update-golden` after the deliberate change.

**Contract:**
- B1 finds flares by SubtypeId "FlareLauncher" and the projector by name "Projector [Self]".
- A1 drops "Flare" from `_guns`.

**Tests:**
- `python3 -I tools/build_all.py --no-zip --only "Hornet" "Aegis" "Gnat" "Longbow S" "Mantis" "Basilisk" "Midge" "Watcher"`
- `python3 -I tools/designs/reach.py` for the printers of changed models (report failures to C3).
- `survivability_audit.py --summary`.
- Sim: `run --filter "^pirate/Hornet-vs-Pirate$" --seeds 20`, old vs new blueprint (decoys are modelled).

**Done when:** builds and validates, no block outside any outline, golden updated, audit table.

#### C2, printer and pod data

**Owns:** `tools/designs/printer_lib.py`, `printers.py`, `pods.py`, their tests (`tools/tests/test_printer_lanes.py`, `test_printer_conveyors.py`).

**Steps:**
1. `projected_grid_xml` strips nested `ProjectedGrids`.
2. Write `Dock Forward`/`Dock Up`/`Repair` per bay into the printer `[Bay N]` and pod `[Bay n]` Custom Data.

**Tests:** `run_all.py --fast`; build "Hornet Forge", "Aegis Shipyard", "Cradle Pod"; `sim/printer_test.sh`.

**Done when:** keys present, printers build.

#### D1, bay scripts

**Owns:** `scripts/HiveMotherAI.cs`, `scripts/CradleBayAI.cs`, `docs/foundry-notes.md`, `tools/tests/test_cradle_bay.py`.

**Steps:** item 17 bay side (section 4.3 channels, repair entry and REPAIR state, reservation). Guard every API read as in v2.2.

**Tests:** compile; `sim/printer_test.sh`; `test_cradle_bay.py`; `dock-launch` cradle variant.

**Done when:** advertisement and reservation are visible in the sim log and the print cycle is unchanged.

#### E1, Medic

**Owns:** new `scripts/MedicAI.cs`, new `tools/designs/medic.py`, `tools/designs/__init__.py`, new `tools/tests/test_medic.py`.

**Steps:** item 18 design and script.

**Tests:** compile; `build_all.py --no-zip --only "Medic"` (validate, conveyor port check, no DLC); minified size.

**Done when:** builds, compiles, test passes.

#### F1, sim harness

**Owns:**
- `sim/src/Run/SimEvents.cs`;
- `sim/src/Api/ThrustApi.cs` and `GyroApi.cs`, for the writer audit only, after confirming with the item 21 metrics agent that they do not touch these files.

**Steps.** Add harness events and a per-tick writer audit:
- `!kill-block <name part> [N]`
- `!damage <fraction>` (random hull blocks)
- `!disable-pb <tag>`, `!enable-pb <tag>`
- `!msl <distance>`: spawns a rocket aimed at the ship and broadcasts a `<Tag>_MSL` track for it
- writer audit: count ticks where two PBs set gyro or thruster overrides; print it in the run summary line.

**Tests:** `selftest all` (pre-existing failures: M4, M5, M6 launch-all); one smoke run per event.

### Phase 2: integration

#### AB2, integration

Owns the Flight and Staff scripts plus all five new blocks and the aim and wp blocks.

**End-to-end runs (seed counts and reasons):**

| Purpose | Command | Seeds |
|---|---|---|
| Regression | `run --filter "^1v1/(Hornet-vs-Hornet\|Aegis-vs-Hornet\|Gnat-vs-Gnat\|Longbow S-vs-Wasp\|Mantis-vs-Hornet\|Basilisk-vs-Hornet\|Wasp-vs-Hornet\|Harrier-vs-Hornet\|Lancer-vs-Hornet)$" --seeds 5` | 5 each, to catch breakage |
| Jinks A/B | `pirate/{Hornet,Harrier,Aegis}-vs-Pirate`, `--custom-data "[Survival]\nLead-Breaking Jinks=false"` vs default | 20 each, direction |
| Charge vs Tech Hunter | the `charge-Hornet-default` plan line | 10 |
| Team: gather, blind side, hand-off | `pirate/3xHornet-vs-Pirate`, A/B per key | 20 |
| Damage RTB / limp | F1 events | 3 each |
| Failover | F1 events | 3, writer audit 0 |
| Power guard | item 6 runs | 10 |
| Missile | `!msl` | 3 |
| Spaced panels | with `ExplosionOcclusion` on | 10 |
| Weakpoint regression after the seeker move | a `pc/plans/weakpoint.plan` line with `--seeds 5` | 5 |

Also: `selftest all`, `follow-test --count 3`, `dock-launch`.

Tune the defaults (Jink Speed cap, RTB integrity, explosive thresholds). Fill section 5 with measured sizes.

**Done when:**
- no regression beyond noise;
- the writer audit is 0;
- every Flight PB within its section 5.1 estimate + 1k.

#### C3, printers and Repair Pod (after C1)

**Owns** C2's files plus `tools/designs/reach.py`.

**Steps:**
1. Re-solve every printer whose drone changed until reach passes.
2. Add the "Repair Pod" model (cradle pod variant with `[Bay n]` welders, `[Stock]`, conveyors, `Repair=true`). `test_cradle_bay.py` is D-owned: coordinate any edit with D2.

**Tests:** full `build_all.py --no-zip --only` of every printer and pod; `run_all.py`.

#### F2, sim protocol harness (after coordination with the item 21 agent)

**Owns:** new `sim/src/Run/SurvivalTest.cs`, plus one `case` line in `sim/src/Run/Program.cs`.

**Step:** `survival-test --mode dock|medic|failover`:
- **dock:** a damaged Hornet docks on a Hornet Forge bay. Measures approach accuracy and that the merge locks.
- **medic:** the Medic holds its work points on a parked damaged Hornet; no collisions.
- **failover:** the item 5 handshake timing.

#### D2 and E2, protocol fixes

D1 and E1 continue on their own files, driven by F2's results, 3 seeds per mode.

### Phase 3: documentation and the owner's test list

**G3 owns:** `docs/design/survivability.md` (results sections), `docs/retrofit.md` (move rows), `packages/changelog.md`, `packages/tests.md` (the section 8 list).

---

## 7. Verification commands (reference)

- `tools/compile_check.sh [scripts/X.cs ...]`
- `python3 -I tools/blocks_sync.py --check` (also runs the legacy `*_sync.py --check`)
- `python3 -I tools/minify_cs.py scripts/<X>.cs`
- `python3 -I tools/tests/run_all.py --fast`
- `python3 -I tools/build_all.py --no-zip --only "<Model>" ...` (never without `--no-zip`)
- `sim/build.sh`, then `mono out/sim/SimHost.exe run|one|selftest|follow-test|dock-launch|printer-test ...`
  - `--events` for harness events;
  - `--custom-data` or `--pb-custom-data "<Model>=<ini>"` for key A/B.
- `python3 -I tools/survivability_audit.py --summary`
- `python3 -I tools/designs/reach.py "<Printer>"`

---

## 8. In-game only: the owner's test list

1. Lead-breaking jinks against real turrets: do they lead with constant velocity? Hit rate with jinks on and off against a turret pirate.
2. Flare launcher:
   - does it paste (`gui_visible` false);
   - do FlareClips load;
   - what flares distract (homing missiles? turrets?).
3. Decoys on small drones: does turret aim go to the decoy corner?
4. Turrets and CTCs: does `GetTargetedEntity()` report incoming rockets as `Missile`?
5. Railgun `Enabled=false`: does it stop the recharge draw, and is the charge kept?
6. Self-projector:
   - does the XML paste;
   - are only missing blocks shown;
   - does a destroyed and rebuilt PB keep its program and run.
7. Docking:
   - clamp face (`MERGE_CLAMP_FACE_SMALL`);
   - snap tolerance and approach;
   - a damaged Hornet docks at a Hornet Forge, is repaired, recharged and released;
   - a cradle dock without welders.
8. Medic: welder reach on a held patient, collisions, components drawn from cargo through the conveyor.
9. The Staff writing the Flight PB's Custom Data (no clobbering on paste or reload).
10. Failover: switch the Flight PB off mid-flight. Does the Staff fly home? Switch it on again: does the Flight take over cleanly?
11. Skin panels against rockets; explosion occlusion behaviour.
12. IGC latency of the in-construct link under load.

---

## 9. Risks

- **Two writers.** Failover, dock and burn all hand control over by message. Mitigated by the yield handshake, the `Release()` guard, `SvDocked()`, and F1's writer audit as a regression gate.
- **Staff dependency.** Most decisions are lost if the Staff PB dies (section 1.4). The audit can show whether the Staff PB needs more cover (item 14).
- **Seeker move.** Behaviour may change in the weakpoint pipeline (the own seeker now goes via `.WPT` bit 4096). Weakpoint plan seeds in AB2. Camera charge is now shared by two PBs: the Staff casts only from a camera holding ≥ 2× the ray length.
- **Damage RTB too eager.** Fewer kills, so tune `RTB Integrity Below` with sims. The owner can override with `fight` or any mode verb.
- **Gather window.** It delays the first shot by at most `Gather Max` and never when under fire or in range. A0/B test against `Attack Together=false`.
- **Design changes ripple.** They reach printers (welder reach), cradle pods and wing ships (Corsair/Warden carry Hornets), golden hashes, and the in-game-tested Aegis. C3 re-solves; the owner re-tests one printed drone.
- **Blueprint size.** It doubles with the self-projection. Printed drones carry no self-projection.
- **Heuristics.** Weapon-class thresholds are sim-calibrated, not game-calibrated.
- **Untestable in the sim:** flares, welding, projection, real missiles on turrets, explosion occlusion (option only).
- **Off-limits Tube files.** No package may touch `TubeRamAI.cs` or `tube.py`.

---

## 10. retrofit.md rows

**Pending (added by P0):**

| Improvement (origin) | Could go to | Note |
|---|---|---|
| Perpendicular evade and lead-breaking jinks (SURVIVAL LINK, this plan) | StingerAI (approach), TubeRamAI (run-in; off-limits now), PathfinderAI, PeeperAI | Ships: evade only (a big ship cannot jink) |
| Damage RTB plus retreat off the threat axis, limp caps (SURVIVAL STAFF) | Helm ships (ShrikeAI + StaffAI), Pathfinder, Peeper | Ships' "home" is the fleet anchor |
| Flight-PB failover limp-home with the yield handshake (SURVIVAL STAFF `SvWatch`) | Helm ships (StaffAI watchdog), the Corsair/Warden Helm split | Same one-writer rule |
| Missile warning from own turrets / `<Tag>_MSL`, dodge and flares | Helm ships (vanilla [PD] turrets target missiles: the best fit), Mothership relays (relay carrier turret missile targets on `<Tag>_MSL`) | Flares need hardware on ships |
| Threat memory and `<Tag>_THREAT` / `<ch>_THREAT` consumer (standoff floor, blind side) | StaffAI (BATTERY Standoff By Weapons could take the floor), HiveMotherAI and Mothership relays (fleet memory) | Generalises Ballista's Standoff By Weapons |
| Railgun power guard (`SvPower`) | Ballista and any rail ship (BATTERY block) | Gatling and rocket drones: no draw, not needed |
| Survival kit: decoys, flare, self-projector (`add_survival_kit`) | Ships (Shrike, Corsair already have decoys), Tube (own flare pods WIP), Pathfinder, Peeper | Outline rule: inside the set outline only |
| Repair at printer bays (RBAY/RREQ/RACK) | Tube Foundry (tubes are expendable: probably not), Pathfinder Nest | Needs a merge on the drone |

**Role-specific:**
- Gnat: no jinks (axial thrust only) and no bay docking (belly merge).
- Basilisk and Longbow L: no bay docking (no merge block).
- Power guard only on railgun drones.

---

## 11. Results (AB2)

AB2 integration runs, 2026-10-17, simulator only. New = main at 693e880 plus the AB2 default change below. Base = bd7817a (before the survivability work), with its own blueprints and sim build. Seeds are per scenario; counts follow CLAUDE.md (5 for regression, 20 for an A/B direction, 3 for event checks, 10 for the charge and power-guard lines). "Lost" = disabled or destroyed (`tools/sim_survival.py`); "W" = fights the drones' side won. Release zips are not rebuilt.

### 11.1 Default changed: damage RTB fires on integrity only

| Key | Old default | New default |
|---|---|---|
| `RTB On Lost Thrust Axis` | true | false |
| `RTB Gyro Fraction` | 0.5 | 0 (RTB only when every gyro is gone) |
| `RTB On Lost Forward Cameras` | true | false |

`RTB Integrity Below` stays 0.55. The keys stay, so an owner can switch each trigger back on.

**Why.** The Hornet-class drones have 2 thrusters per axis side and now 2 gyros, so the axis, gyro and camera triggers fired on the first or second lost block. The drone then left a fight it could still fight: the `[Thrust]` PB turns a surviving group onto the lost axis, and limp mode caps the speed. A drone in the sim is disabled at about 70-80 % of its blocks, so the integrity trigger alone fires late enough not to cost kills.

Team runs, `pirate/3xHornet-vs-Pirate` + `pirate/3xHarrier-vs-Pirate`, 20 seeds each (40 fights, 120 drones):

| Variant | W / 40 | Drones lost / 120 | Lost per kill |
|---|---|---|---|
| Base (bd7817a) | 18 | 37 | 2.06 |
| New, old defaults (all four triggers) | 18 | 24 | 1.33 |
| New, `Damage RTB=false` | 31 | 28 | 0.90 |
| **New, AB2 defaults (integrity 0.55 only)** | **30** | **25** (29 with the fixed metric) | **0.83** (0.97) |
| integrity only, 0.65 | 28 | 22 | 0.79 |
| integrity only, 0.75 | 25 | 27 | 1.08 |
| integrity 0.55 + lost axis | 17 | 21 | 1.24 |
| integrity 0.55 + cameras | 25 | 31 | 1.24 |
| integrity 0.55 + gyros (0.5) | 25 | 31 | 1.24 |

`pirate/3xAegis-vs-Pirate`: 20/20 wins and 0 losses in every variant (old and new).

The 1v1 duels (`Aegis-vs-Hornet`, `Harrier-vs-Hornet`, `Hornet-vs-Lancer`, `Hornet-vs-Hornet`, 20 seeds, 80 fights) confirm it. With the old defaults, 29 of 80 ended as 300 s timeouts (both drones retreating), against 12 on the base. With the AB2 defaults: 12.

Solo pirate (`pirate/{Hornet,Harrier,Aegis}-vs-Pirate`, 20 seeds): the old defaults lost 54/60 and dealt 37.5k per fight; the AB2 defaults lost 56/60 and dealt 51.7k (base: 52/60, 68.6k). A lone drone still loses to the Pirate either way.

0.65 is within noise of 0.55, so the default stays 0.55.

**Re-check with the fixed sim metric (0acaaa6).** The two metric bugs in 11.4 (an `[Aux]` / `[Thrust]` PB counted as control, a flare launcher counted as a gun) are fixed in 0acaaa6. The base-vs-new comparisons they could inflate were re-run on a fresh sim build of main (3785530 + 0acaaa6). Base (bd7817a) and new blueprints both ran in that one sim; the base scripts were compiled from their blueprints.

| Run | Seeds | Base | New (AB2 defaults) |
|---|---|---|---|
| Team 3xHornet + 3xHarrier vs Pirate | 20 each (40 fights, 120 drones) | 18 W, 37 lost, 2.06 lost per kill; writer audit 8 ticks (3xHornet) | 30 W, 29 lost, 0.97 lost per kill; writer audit 0 |
| 1v1 regression, 9 duels | 5 (45 fights) | 17 side-A wins, 14 timeouts, side A lost 14 | 18 side-A wins, 16 timeouts, side A lost 11; writer audit 0 |

- The bias was real but small: 4 of the 25 new-build team losses had been hidden (now 29), and the base numbers do not change (no Aux PB, no flare launcher).
- The conclusions stand. The new builds with the AB2 defaults win 30 of 40 team fights against 18 for the base, and lose fewer drones (29 vs 37).
- The Longbow S-vs-Wasp timeouts (3/5) remain with the fixed metric. They come from the Longbow S running out of rail ammo, not from a capable-but-headless Wasp.
- The default tuning (11.1) compared variants of the new build with each other, so both sides carried the same bias. It was not re-run.
- Exceptions 0 in every run.

### 11.2 Per run

| # | Run | Seeds | Base | New (AB2 defaults) | Writer audit / exceptions |
|---|---|---|---|---|---|
| 1 | 1v1 regression, 9 duels (Hornet-vs-Hornet, Aegis-, Gnat-vs-Gnat, Longbow S-vs-Wasp, Hornet-vs-Mantis, Basilisk-, Hornet-vs-Wasp, Harrier-, Hornet-vs-Lancer) | 5 | 17 side-A wins, 14 timeouts; fixed metric: side A lost 14/45 | 18 side-A wins, 16 timeouts (old defaults: 17, 19); fixed metric: side A lost 11/45, same wins and timeouts | new 0 / 0; base (fixed-metric sim) 6 writer-audit ticks in Basilisk-vs-Hornet |
| 2 | Jinks, solo pirate ×3 models, `Lead-Breaking Jinks` on / off / `Jink Accel Fraction` 2 / 4 / Period Min 1 + 2 | 20 | lost 52/60, Pirate hit rate 6.7 % | on: lost 56/60, hit 7.9 %; off 56/60, 7.9 %; ×2 58/60, 8.3 %; ×4 59/60, 8.0 %; P1 55/60, 8.2 % | 0 / 0 |
| 2b | Jinks drone vs drone (only the Hornet changed; vs Aegis, Harrier, Lancer) | 20 | n/a | on 22 W / 29 L, hit on Hornet 26.9 %; off 23 / 29, 28.7 %; ×4 21 / 30, 27.3 % | 0 / 0 |
| 3 | Team 3xHornet + 3xHarrier, AB2 defaults, then one key off | 20 | 18 W, 37 lost (fixed metric: the same) | all on 30 W / 25 lost (fixed metric: 30 W / 29 lost); `Attack Together=false` 28 / 35; `Blind Side Orbit=false` 24 / 41; `Wing Hand-off=false` 21 / 41; `Standoff By Enemy Weapons=false`: identical (no range advantage over the Pirate, never fires) | 0 / 0 |
| 4 | Events, `pirate/Hornet-vs-Pirate` at t=40 s: `!kill-thrust B`, `!kill-block Gyroscope 1`, `!damage 0.5`, `!disable-pb Flight` / `!enable-pb Flight` at 55 s | 3 each | n/a | see 11.3 | 0 / 0 |
| 5 | Charge, `charge-Hornet-default` (Tech Hunter rush at 100 m/s from 3 km) | 10 | Hornet lost 10/10 at 70.8 s mean, dealt 10.6k | lost 10/10 at 74.0 s, dealt 6.9k; `Damage RTB=false` 73.8 s / 7.3k; jinks off 73.2 s / 6.8k | 0 / 0 |
| 6a | Power guard, `1v1/Hornet-vs-{Longbow S,Mantis}`, `Power Guard` on / off (railgun drones only) | 10 | Longbow S 5 W, Mantis 4 W | on and off identical (Longbow S 7 W, Mantis 5 W): the brown-out condition never occurs in these duels | 0 / 0 |
| 6b | Missile, `!msl` at 20, 45 and 70 s (800 / 600 / 1000 m), `Missile Dodge` + `Flare On Missile` on / off | 3 | n/a | dodge bit (SVS bit 3) up for the whole flight of every rocket; 1 of 9 rockets hit with dodge on, 1 of 9 with it off | 0 / 0 |
| 6c | Weakpoint, `wp-small-on` (3 small fleets vs Tech Hunter 90k, 600 s) | 5 | power / weapons / blocks killed: 3xLongbow L 7.0 / 0.2 / 211; 3xL Seeker 3.2 / 0.2 / 212; Basilisk S+2xL 1.8 / 25.6 / 477 | 3.8 / 1.4 / 203; 5.0 / 0.6 / 205; 1.8 / 42.6 / 775 | 0 / 0 |

### 11.3 Damage RTB, limp and failover events (3 seeds each, AB2 defaults)

- **`!kill-thrust B`:** LIMP (SVS bit 8) on, no RTB, and the drone keeps fighting. Seed 1 dealt 62k after losing its rear thrusters.
- **`!kill-block Gyroscope 1`:** no RTB from one gyro of two. Seed 1 retreated later at integrity 51 % (via point); seed 2 when its second gyro was shot.
- **`!damage 0.5`:** too harsh as a test. The random block removal takes PBs and guns, so the drone is disabled at once and RTB cannot be judged. Use 0.3 to 0.4 next time.
- **RTB by integrity** fired at 43-54 % in the missile runs. That is the trigger at 0.55 plus the 10-tick scan.
- **Failover** (`!disable-pb Flight` 40 s, `!enable-pb Flight` 55 s): the Staff shows `LIMP HOME: flying home` within the 1.5 s detection. The Flight takes over again when re-enabled ("link OK" at the next echo). Writer audit 0 in 3/3.

### 11.4 Findings that were not tuned

- **Jinks: no direction.** Lead-breaking jinks on, off, or 2 to 4 times the amplitude make no measurable difference, either against the Pirate's turrets or in drone duels. All are within noise at 20 seeds. The defaults stay (0.8, 800 m/s, 0.4-3 s); `Jink Speed` stays 12 (Harrier 35). The base's lower Pirate hit rate (6.7 % vs 7.9 %) is most likely the denser kit hull inside the same outline: the new drones get hit just as often with jinks off.
- **Kills in duels.** The kit-hardened drones take more hits to disable. In `Longbow S-vs-Wasp`, the Longbow S now runs out of rail ammo (12 shots) before the Wasp is disabled: 3/5 timeouts against 0/5 on the base.
- **Sim metric: the `[Aux]` PB counted as control (fixed in 0acaaa6, see 11.2).** `Metrics.BodyCapable` accepts any functional PB as control, so a drone whose Flight and Staff PBs were shot off keeps "capable" through its `[Aux]` PB. Its guns still exist but nobody steers them (seen in Longbow S-vs-Wasp seed 3: Wasp at 26/61 blocks, Flight and Staff detached, run ends as a timeout). Sim owner: control should need a PB that writes the overrides (the Flight, or the Staff while it holds).
- **Explosive class thresholds** (`Explosive Hit Blocks` 3, `Explosive Drop Min` 400) are not calibrated. Sim turrets never fire rockets at drones; only `!msl` does, and those mostly miss.
- **Power guard** never triggers in the sim duels. Left on: it only acts on a real brown-out.
- **Gnat-vs-Gnat:** both Gnats never meet (5/5 timeouts, the same on the base).
- **PB sizes (section 5.1):** every Flight PB is within its estimate + 1k.
- **Sim metric: the flare launcher counted as a weapon (fixed in 0acaaa6, see 11.2).** The sim builds the kit's `SmallFlareLauncher` as a slug (railgun-mode) weapon with no power sink. `selftest` C fails with "missing 10", one per kit drone. Worse, `BodyCapable` counts it as a gun, so a drone whose guns are all shot off stays "capable" while its flare launcher lives. New-vs-base survival numbers (11.1, 11.2) are therefore biased toward the new builds. The variant-vs-variant comparisons on the new build, which set the defaults, carry the same bias on both sides. Sim owner: no weapon unit for flare launchers (or exclude them from capability), and make control need the Flight PB (above).

### 11.5 Checks

- `tools/compile_check.sh`: all PASS. `blocks_sync --check`: all blocks identical.
- `selftest all` (new sim, a full `build_all --no-zip` blueprint set): 8 failures. Base bd7817a: 8, but a different set.
  - New: SelfTestA Hornet mass/accel is fixed: the check now expects 5842 kg / 4.93 m/s2 (was 4589 / 6.3; it failed on the base too).
  - New: SelfTestC flare-launcher units (sim, above).
  - New: SelfTestD looks for a field `_rng` in HornetDroneAI. SURVIVAL LINK removed that field; the jinks now use `_evRng`. This is a test expectation, sim side.
  - SelfTestM4 (2, wing satellite ring / wing lock timing) and SelfTestM5 (4, Warden hangar doors): fail on the base too.
- `dock-launch` Hornet, Harrier, Aegis, Wasp, Gnat: LAUNCH OK.
  - Writer audit gyro 1 / thrust 1 at the take-off: the Staff's burn hands over to the Flight on the next tick, which the audit counts. It disappears with the Staff PB off, and is not a survivability writer.
  - `--return-at 10`: RETURN OK, writer audit 0 from the return on.
  - Longbow S has no merge block (base as well).
- `follow-test --count 3`: FOLLOW OK (slot about 60 m).
- `Mantis Foundry` fails reach in `build_all` on both base and new (C3's re-solve).
