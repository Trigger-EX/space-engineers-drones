# Simulator validation (integration pass)

This file records what the simulator (`sim/`, built by `sim/build.sh`) does when it runs the real, unmodified scripts in
`scripts/*.cs`, and every place where sim behaviour differs from what HANDOFF.md section 3 says the user saw in game
(drones fly, turn onto targets and shoot). The design is in `docs/sim-design.md`. Every number here comes from the runs
listed under each section and can be reproduced with the same commands.

Short version:

- **All four self-test groups pass.** The engine runs every script with no exceptions in 1,500 runs.
- **The gyro convention works.** With the default signs (-1,-1,-1), drones that have a target turn onto it and hold their
  aim within 0.2-0.5 deg. Flipping Pitch or Yaw stops them landing hits on target.
- **The default fights are flown mostly by the AI-block model, not by the scripts.** The scripts' own target finding almost
  never finds a small grid, so the v1.8 hybrid search leaves the AI blocks in control. When a drone does have a target (a
  pilot-style `lock` at the start), it flies in, aims and shoots, and hits register.
- **Two script bugs were found but not fixed.** Editing `scripts/*.cs` was denied in this session. Details in section 3.

## 1. How to reproduce

```
bash sim/build.sh                                    # SeApi.dll, SimShim.dll, SimHost.exe, 14 script DLLs
mono out/sim/SimHost.exe selftest all                # physics (A) + api (B) + combat (C) + runner (D) + 3 end-to-end duels
F='^(1v1/(Hornet-vs-Hornet|Gnat-vs-Gnat|Hornet-vs-Longbow S|Hornet-vs-Wasp)|pirate/Hornet-vs-Pirate)$'
# (a) default scenarios: 1.5-3 km apart, random orientation, scripts get "hunt" at 0.5 s
mono out/sim/SimHost.exe run --filter "$F" --seeds 100 --out out/sim/results/validation-default
# (b) "pilot lock": 500-700 m apart, facing each other, scripts get "ai off; lock; hunt" at 0.5 s
mono out/sim/SimHost.exe run --filter "$F" --seeds 100 --face --start-distance 500:700 \
     --commands "0.5:ai off;0.5:lock;0.5:hunt" --out out/sim/results/validation-lock
# (c) as (b), plus the projectile-speed fix applied through CustomData (the script text is unchanged)
mono out/sim/SimHost.exe run --filter "$F" --seeds 100 --face --start-distance 500:700 \
     --commands "0.5:ai off;0.5:lock;0.5:hunt" --custom-data "[Hornet]\nProjectile Speed=800\n[Gnat]\nProjectile Speed=800" \
     --out out/sim/results/validation-lock-ps800
python3 -I tools/sim_report.py out/sim/results/validation-default --out report.md
# trajectory CSV + SVG plot (positions on the engagement plane, distance, closing speed, aim error, shots/hits)
mono out/sim/SimHost.exe one --scenario "1v1/Hornet-vs-Hornet" --seed-index 0 --trace t.csv --trace-every 6
python3 -I tools/sim_report.py --trace t.csv --out t.svg
```

Each trace row holds, per ship: position, velocity, speed, angular speed, blocks, combat capability, shots, hits and damage
taken. It also holds the nearest enemy distance and closing speed, `aim_err_deg` (best fixed gun axis against the enemy's
bound centre) and `ai_on` (AI Offensive block enabled). Finally `script_tgt` records whether the PB status screen shows a
target. Traces of the runs quoted below are in `out/sim/results/validation-traces/`.

## 2. Results of the five requested fights (100 seeds each)

All three modes had **0 script exceptions and 0 engine errors**. Per-ship averages are over 100 runs. ToT = time on target:
seconds with a fixed gun within 2 deg of the enemy. "script tgt" = seconds with the script holding a target. "AI on" =
seconds the AI blocks were flying the ship.

### (a) Default scenario (1.5-3 km, hybrid AI search)

| fight | outcome (100 runs) | A hit% | B hit% | A script tgt s (runs with any) | AI on s |
|---|---|---|---|---|---|
| Hornet vs Hornet | win 27 / 34, mutual 8, timeout 31 | 20.2 | 21.0 | 2.8 (8/100) | 200 |
| Gnat vs Gnat | timeout 100 | - | - | 0 (0/100) | 0 |
| Hornet vs Longbow S | win 52 / 9, timeout 39 | 32.7 | 23.2 | 0.1 (2/100) | 210 |
| Hornet vs Wasp | win 42 / 17, mutual 3, timeout 38 | 27.7 | 5.8 | 0.4 (2/100) | 208 |
| Hornet vs Pirate | Pirate wins 100 | 57.9 | 26.3 | 41.7 (99/100) | 47 |

- 30 of the 31 Hornet-Hornet timeouts started more than 2.5 km apart. That is beyond the AI Offensive search range, and a
  HUNT drone with no target holds its home point, so nothing ever happens.
- The median time to kill of the Hornet-Hornet wins is 152 s. Most of it is the approach at the AI Flight default speed
  limit of 10 m/s.
- One default duel, plotted (`hornet-default-s0`):
  - Approach at 9.9 m/s, closing 10 m/s.
  - Both ships hold about 730 m with a gun within 2 deg of the enemy for 117 of 120 s. Shots 229/159, hits 87/48.
  - Ship 1 is disabled at 120 s.
  - The AI blocks were on all the time, and neither script ever held a target.

### (b) Pilot lock (500-700 m, facing, `ai off; lock; hunt`)

| fight | outcome | A hit% | B hit% | A ToT s | A script tgt s |
|---|---|---|---|---|---|
| Hornet vs Hornet | win 29 / 30, timeout 41 | 1.8 | 1.9 | 40 | 88 |
| Gnat vs Gnat | win 30 / 39, mutual 10, timeout 21 | 6.4 | 6.7 | 22 | 23 |
| Hornet vs Longbow S | win 13 / 85, timeout 2 | 2.8 | 89.8 | 28 | 34 |
| Hornet vs Wasp | win 24 / 63, timeout 13 | 2.2 | 13.1 | 39 | 68 |
| Hornet vs Pirate | Pirate wins 100 | 60.8 | 28.5 | 35 | 37 |

### (c) As (b) with `Projectile Speed=800` for Hornet and Gnat

| fight | outcome | A hit% | B hit% | A shots | median fight length |
|---|---|---|---|---|---|
| Hornet vs Hornet | win 38 / 35, timeout 27 | 26.6 | 26.1 | 235 | 92 s avg |
| Gnat vs Gnat | win 31 / 46, mutual 11, timeout 12 | 12.9 | 14.4 | 142 | 51 s avg |
| Hornet vs Longbow S | win 44 / 56 | 30.8 | 95.0 | 203 | 21 s avg |
| Hornet vs Wasp | win 73 / 19, timeout 8 | 32.8 | 16.7 | 297 | 39 s avg |
| Hornet vs Pirate | win 5 / 92, timeout 3 | 93.2 | 28.9 | 724 | 50 s avg |

One locked duel, plotted (`hornet-lock-ps800-s3`), to sanity-check the numbers:

- Start 556 m apart. Ship 0 accelerates to 40 m/s, closes at up to 28 m/s and settles near the 350 m standoff.
- Ship 0's guns stay within 2 deg of the enemy centre for 18.5 of 20.5 s. It fires 468 rounds, hits 155 times and disables
  the enemy at 20.5 s.
- The enemy lost its lock after 3.5 s (section 3.3), then sat still and was shot.
- The numbers hang together: 23 rounds/s from 2 gatlings × 20 s ≈ 460 shots; 155 hits × 90 damage ≈ 14,000 dealt against
  a 54-block Hornet.

## 3. Script findings (sim evidence; the scripts were NOT changed)

The integration task allowed script fixes for clear bugs, but editing `scripts/*.cs` was refused by the session's
permission check. Each finding is listed with the one-line fix and the evidence. Fixes 3.1 and 3.2 also need
`python3 -I tools/build_all.py`, because the blueprints embed the script text and the simulator runs the blueprint's
`<Program>`.

### 3.1 Wrong gatling projectile speed (clear bug)

- **Where:**
  - `HornetDroneAI.cs:38` `double ProjSpeed = 400;`
  - `GnatSwarmAI.cs:38` `ProjSpeed = 400`
  - `LongbowSniperAI.cs:381`, the gatling fallback `ProjSpeed = 400;`
- **What the data says:** game data (`defs_stats.json`, `docs/stats.md`) gives the gatling and autocannon 800 m/s. The newer
  scripts (Aegis, Harrier, Lancer, Mantis) already use 800.
- **Effect:** the lead is about twice too long. In pilot-lock Hornet duels the hit rate is 1.8 % at 400 and 26-27 % at 800;
  shots per run fall from about 2,000 to 235 and fights end much sooner (table b vs c).
- This matches the in-game report "Hornet over-led shots at a tumbling wreck" (HANDOFF 3). The fix made in v1.3 (gating the
  target's acceleration) does not touch the real cause.
- **Fix:** change those three values to 800. It can be tested now without touching the script:
  `--custom-data "[Hornet]\nProjectile Speed=800"`.

### 3.2 The AI-confirm raycast pattern cannot find small grids (clear bug)

- **Where:** `AiStep()` in all 9 combat scripts, for example `HornetDroneAI.cs:1233`:
  `r = 3 * Math.Sqrt((_aiScan % 13 + 0.5) / 13.0)`.
- **The pattern never casts down the boresight.** The smallest offset is 0.59 deg, but a Hornet seen from 600 m covers only
  about 0.24 deg (bound radius).
- **Each ray is 2,600 m long,** so each forward camera can cast only one every 1.3 s.
- **Result:** in the default scenarios the AI blocks point the ship at the enemy for minutes, but the script took over in
  only 8-10 of 100 Hornet-Hornet runs, and then only for a few seconds.
- Against the large pirate ship it works: 99 of 100 runs.
- **Fix:** include the centre ray, scale the pattern to the expected target size, and cap the ray length at the charge the
  camera has.

### 3.3 Lock maintenance is fragile (design weakness)

- **Long range:** past about 1 km, only the two forward cameras keep the lock. Each casts one ray per 0.5-0.8 s at the
  predicted centre. A target that accelerates (5 m/s² while closing, or the AI's 10 m/s strafe reversals) drifts out of a
  silhouette of about 0.1-0.2 deg in under a second.
  - Trace: rays at 0.06, 0.23, 0.45, 0.57 and 0.67 deg off over 4 s, with only one hit.
  - The lock is then dropped after 4 s.
- **Recovery is poor:** there is no widening search, and re-acquisition depends on the 120 m omni-scan or the 1,200 m / 35 deg
  auto-scan. Hitting a 2.5 m target with those is a lottery:
  - pure-script drones 187 m apart failed for 120 s;
  - so did drones 943 m apart.
- **Consequence:** a drone that loses its lock usually stays blind while being shot (traces `hornet-lock-ps*`).

### 3.4 Other behaviour the sim shows (not bugs)

- **Gnat alone finds nothing.** It has no AI blocks and no long sweep. In the default 1v1 it waits for hive, designator or
  IGC targets, so 100 of 100 runs time out. Gnats only fight in swarms with a feeder (mothership or Watcher), or after
  `lock`.
- **HUNT with nothing within 2.5 km holds position.** With the HANDOFF start distances of 1.5-3 km, about 30-40 % of the
  default 1v1 runs never start.
- **Hull aiming against the pirate:** the Hornet's status screen shows `Aim: CENTER (1 hull pts)`. It lands 93 % of its
  rounds but kills only about 9 of the pirate's 260 blocks per run, so it has little effect.

## 4. Discrepancies between the sim and HANDOFF section 3 (in-game ground truth)

| In game (HANDOFF 3) | In the sim | Assessment |
|---|---|---|
| Drones fly, turn onto targets and shoot; gyro law `Pitch=-l.X, Yaw=-l.Y, Roll=-l.Z` is correct | With a target, they do. Aim error held at 0.2-0.5 deg, 26-33 % hits at the correct projectile speed. Flipping one gyro sign drops hits from 2,745 to about 55 per 20 runs (gyro sign check below). | Consistent. Pitch has no independent in-game confirmation (Keen's 2019 source suggests +1 for Pitch); in-game test T10 would settle it. |
| (implicit) drones acquire targets by themselves | With the default 1.5-3 km starts, the scripts hold a target in under 10 % of small-grid duels. The fight is flown by the modelled AI blocks. | The installed in-game build is v1.7, which has no hybrid AI search, and the user's tests were not at 1.5-3 km with small targets. Not a contradiction, but the sim says self-acquisition of small grids is weak (3.2, 3.3). |
| Hornet over-led a tumbling wreck | Over-lead reproduced on every moving target, caused by ProjSpeed 400 vs 800 m/s (3.1) | Consistent, with a different root cause than the one assumed. |
| Drones could not see enemies beside/above/below them (fixed in v1.4 with side cameras and omni-scan) | Omni-scan (120 m, 40 deg random cone) and sensor (50 m) almost never find a small grid beyond about 100 m | Consistent: v1.4 does not solve it at combat range. |
| Longbows (many) aimed at the pirate's centre mass | Hornet against the code-built pirate: `Aim: CENTER`, 93 % hits, few blocks killed | Same symptom. The pirate is a sim stand-in (no blueprint), not the user's pirate. |
| Drones kept shooting disabled wrecks | Not exercised: no wreck-plus-new-enemy scenario was run | Open |
| Scripts missing from PBs after spawning | Not modelled: the sim always loads the blueprint `<Program>` | Out of scope |

## 5. Simulator changes and decisions made in this pass

1. **The AI blocks fly scripted ships.**
   - ScenarioRunner adds a `VanillaAiBrain { ScriptShip = true }` to every ship that has OffensiveCombat or FlightMovement
     blocks.
   - While the script keeps those blocks off, the brain does nothing at all.
   - When the script switches them off, the brain clears the thrust and gyro overrides it set, stops its guns and restores
     the dampener state (the game's AI handing back control).
   - Before this, scripted drones never moved (they idled in "AI: searching").
2. **New vanilla-AI settings** (`SimSettings`, all CLI-settable; the ones marked [I] are inferred):
   - `VanillaSpeedLimit` 10 m/s: the AI Flight default from research.md 3.3. The blueprints carry no AI component data.
   - `VanillaStayMin/Max` 300/600 m [I].
   - `VanillaAimLead` true [I].
   - `VanillaSearchOnActivate` true [I]: search as soon as the block is enabled, then every 30 s.
   - `VanillaTurnAccel` 2 rad/s² and `VanillaMaxTurnRate` 3 rad/s [I]: a stop-able turn profile.
   - Before this, the AI's P-only aim with Kp=6 oscillated with ±6 rad/s commands under the gyro ramp, and the aim error
     stayed at 30-90 deg.
   - These settings decide the default-mode results (section 2a), so treat those as a model of the AI blocks, not of the
     scripts.
3. **Thrust acts through the centre of mass** (`SimSettings.ThrusterTorque=false`, the default).
   - The game's thrust component applies the summed thrust at `CenterOfMassWorld`, so where a thruster sits never turns the
     ship.
   - With the old per-thruster r×F torque, a Hornet missing one block spun at the 2π rad/s cap: the 120-frame gyro ramp
     could not hold against the thruster torque.
   - `--thruster-torque true` restores the old model. SelfTestA checks both modes.
4. **Metrics changes:**
   - New per-ship fields: `script_target_s`, `first_script_target_s` and `ai_on_s`.
   - Approaching by 50 m or more now counts as engaging, so a slow AI approach is no longer flagged "noengage".
   - Damage a ship's own blast does to itself (ShipA == ShipB) counts as damage taken, not as friendly fire (slot C's
     request).
5. **CLI changes:**
   - `selftest physics|api|combat|runner|scenario|all`. Before, `selftest physics` ran everything, and SelfTestC no longer
     reads the command line.
   - Scenario overrides for `run` and `one`:
     - `--commands "T:cmd;..."`
     - `--start-distance MIN:MAX`
     - `--face`
     - `--custom-data "INI"`
   - Richer `--trace` columns.
   - A `RunOptions.OnTick` debugging hook.
   - `tools/sim_report.py --trace CSV --out SVG`.
6. **Camera debug hook:** `SimCamera.DebugCast` (null in production) reports every raycast.
7. **Cross-slot requests resolved or confirmed:**
   - `SimTerminalBlock` sets `BlockInst.Api` in its constructor.
   - ScriptHost already sets `InMain` and `Runner`, and skips PBs that are not working.
   - The script host exists before spawn.
   - `FlushPending` runs after placement (ScenarioRunner), not inside GridFactory, so PB constructors see the final
     positions.
   - Weapons fire only when functional and enabled.
   - Shot events carry ShipA.
   - A friendly hit is FriendlyFire only, deduplicated in Metrics.
8. **Left as is (documented):**
   - `ApiTuning` stays static: CargoFill 0.5, print 2 blocks/s, MaxAntennaRadius, and `IgcAllowEnemyRelay=false`, so enemy
     teams never share IGC traffic even when they use the same tag. Not verified in game.
   - Printer cycles are not modelled.
   - Stinger is spawned free-flying.

### Gyro sign check (pilot-lock Hornet duels, Projectile Speed 800, 20 seeds)

| signs (pitch, yaw, roll) | outcomes | shots | hits | ToT / script-target s |
|---|---|---|---|---|
| -1, -1, -1 (default) | win 16, timeout 4 | 9,550 | 2,745 | 439 / 424 |
| +1, -1, -1 | timeout 20 | 230 | 58 | 47 / 298 |
| -1, +1, -1 | timeout 20 | 200 | 53 | 44 / 282 |

## 6. Throughput

- **2-drone fight, one core:** `run --filter '^1v1/Hornet-vs-Hornet$' --seeds 16 --jobs 1` simulated 2,888 s in 4.4 s of
  user CPU, about **650x realtime per core**. A 60 s fight costs about 0.09 s of CPU. Scripts take about 34 % of that
  (`one --profile`: 168 of 499 ms for 120 s).
  - **Caveat:** in these default-mode duels the hybrid AI holds the ships, so the scripts run their cheap path. The
    pilot-lock 1v1 runs, where the scripts track and fire, ran at about 380-680x realtime per run with four runs in parallel.
- **The same 16 runs on 4 threads:** 1.7 s wall.
- **Larger fights:** 12 Gnats vs a Hornet run at 75x realtime (scripts 76 % of the time). 3 Hornets vs the Pirate run at
  185x realtime.
- **Full batch:** the 500-run default validation batch took 42.7 s wall on 4 cores.
- The target was 40x realtime per core, so no optimisation pass was needed.

## 7. What the sim cannot tell (be honest in reports)

- **AI blocks.** The AI Offensive and Flight behaviour is a hand-written approximation: patterns, speeds, lead and search
  timing. Default-mode win rates mostly measure that model.
- **Gyros.** The ramp (re-armed on every override write) and the Pitch sign are inferred from Keen's source and from
  HANDOFF 3; they are not measured.
- **Pirate.** The pirate ship is a code-built stand-in, with turret slew 1 rad/s and an elevation floor of -10 deg [I].
- **Not modelled:**
  - no gravity, drag or ammo mass;
  - collision damage is off;
  - deformation is off;
  - IGC between enemies is blocked [I].

## 8. API fidelity audit (adversarial review, no online source authoritative)

Online checks (MDK wiki, spaceengineers.wiki.gg) were thin: they did not state Save() timing, IGC self-receive, queue
overflow or enemy rules. Items below are from code reading plus recollection of the game; none was measured in game.

Verified consistent with the game (by reading the sim): terminal property ids the scripts use (OnOff, Override, Yaw/Pitch/Roll,
Power, Radius, EnableBroadCast, DampenersOverride, sensor "Detect ..." ids with spaces and Left/Right/Top/Bottom/Front/Back,
Shoot, Range); actions Shoot_On/Shoot_Off/ShootOnce/Run/Recompile/StartCountdown/Detonate. The scripts' LeftExtend-style
names do not exist in the game and fall back to the real ids (the fallback is the path that actually runs). Position =
BoundingBox.Center (world AABB, not centre of mass), HitPosition = hit point, TimeStamp never 0. GetBlocksOfType clears the
list, filters `is T` then the predicate. Update1/10/100/Once and Trigger/Terminal/Script/IGC flags are scheduled as in the game.

Differences that could flatter or punish the scripts (not changed):
1. Recompile does not call Save() on the old instance. If the game does (believed, not confirmed), Storage would carry
   mode/home across a HiveMother-triggered recompile; the sim keeps the blueprint Storage.
2. IsSameConstructAs / GetBlocksOfType / ConnectedConstructs are same-Body only. The game also includes connector-linked and
   rotor-linked grids; only matters for connector docking (merge blocks, used by the printers, are one grid, as modelled).
3. Camera Raycast(pitch, yaw): +pitch up, +yaw left is assumed from VRage matrix conventions; the game's sign is unconfirmed.
   The scripts' ray patterns are mostly symmetric, so the effect on results is small.
4. IGC: the sim lets a non-broadcasting antenna receive but not relay (author's choice; the wiki hints reception may also need
   Enable Broadcast, a candidate fix that broke the existing selftest, so it was reverted). Queue overflow drops the OLDEST
   message; the game may drop the newest. Enemy teams never receive each other's broadcasts (unverified). Delivery is
   always on the next tick; the game may deliver same-tick locally.
5. Detected-entity Name is the real grid name only for friendly relations; in the game the rule differs by detector. No script
   parses names, so no effect.
6. Relationship: all teams are Enemies by default; in the game two factions are Neutral until at war and Neutral targets are
   skipped by scripts unless TargetNeutral is set. The sim is therefore harsher than a default two-faction world.


## Scenario/metrics audit (independent review)

Verified: 20 seeded Hornet duels run twice (4 threads vs 1 thread, plus a second 4-thread run) and a 24-run mixed/swarm/pirate set
give identical runs.jsonl except wall/CPU timing fields. Seeds are distinct per (scenario, index). Start centroid distance spans
1.47-3.0 km (actual centroids of the spread-out teams), forward vectors are uniform on the sphere, ship separation >= 25 m, teams are
as named. No mutable static state is shared between worker threads (caches are locked and read-only afterwards; DetRandom and the
slug scratch are [ThreadStatic]; scripts have no statics; `ApiTuning` values are only written before the runs start).
runs.jsonl lines and summary.csv parse with a strict JSON/CSV reader.
Changed: (1) a team that was never armed (lone Watcher) is no longer "beaten" at tick 0: an armed side used to win after 5.2 s;
now the unarmed side only loses when no ship can fly/control any more (power + thruster + PB + controller) or all are destroyed;
when no team was ever armed the result is still no-contest. (2) time_on_target_s only counts targets inside the weapon's ammo range
(earlier numbers in this file include out-of-range time and are inflated). (3) --resume ends a partial last line before appending.
Remaining: summary.csv column avg_cpu_ms_per_run and the script cpu fields are wall-clock and not reproducible.


## Physics audit (independent review, tests in `sim/src/Core/SelfTestAudit.cs`, `selftest audit`)

Method: expected values come from oracles that do not call the engine's own maths (grid axes from `Cross(Forward, Up)` of the
placement, tensor-form inertia from the cell boxes, a Gauss-Jordan solve, Rodrigues rotation, slab tests). 47 checks, all pass.

Verified (no defect): VRageMath stand-ins (quaternion product, `CreateFromQuaternion`, `CreateFromAxisAngle`, `CreateWorld`);
mass, COM and the full inertia tensor of a rotated asymmetric body (products of inertia included); `R I^-1 R^T tau dt`;
right-handed orientation update (omega=+Y turns Forward to Left, omega=+X lifts the nose); thrust direction and magnitude for all
24 block orientations on a rotated ship; gyro Pitch/Yaw/Roll axes (block Right / Up / Backward) through the `IMyGyro` API for all 24
mountings; gyro torque ceiling = `448000 * GyroPower * number of gyros` (checked via the angular momentum change of a 112 t body),
no torque without battery energy; speed and angular clamps keep direction (2 pi small, pi large); projectile sweep against a rotating
and a translating target (both signs of the pose back-map, no tunnelling at 800 m/s, 0.05 m clearance misses); explosion falloff
`damage*(1-d/r)` on a rotated body; camera forward axis for all 24 mountings, pitch/yaw, 45 deg cone, charge 2 km/s, cost of hit
and miss, `TimeUntilScan`, near-face hit point; gatling muzzle point and `ship velocity + dir*speed` for all 24 mountings;
`WorldAabb` and bound sphere; block-split kinematics (read, covered by SelfTestA).

Defect fixed: with `GyroscopicTerm` on, the momentum-form integrator rotated the body with the omega of the START orientation
(first order). A tumbling asymmetric body gained 10 % rotational energy in 20 s (omega 1.0 rad/s class, I ratio 4:1). Now the
orientation uses omega at the half-step orientation (midpoint rule); energy and |L| drift are below 0.01 %. The option is off by
default, so no fight result changes.

Independent justification of the gyro sign (single switch: `--gyro-pitch-sign/--gyro-yaw-sign/--gyro-roll-sign`, default -1,-1,-1):
the scripts compute `l`, the wanted RIGHT-HANDED rate in gyro-local axes, and write `Pitch=-l.X, Yaw=-l.Y, Roll=-l.Z`. The in-game
fact (HANDOFF 3) is that the drones turn onto their targets, i.e. the rate error is driven to zero. A sign error on one axis turns
that loop into positive feedback (the error grows), so the game must map `rate = (-Pitch, -Yaw, -Roll)`. The sim reproduces this:
with pitch sign +1 or yaw sign +1 the 20 duels end in timeouts with 58 and 53 hits instead of 2,745 (integrator's run). Keen's old `MyGyro.cs` (Pitch=+stored.X) would contradict the scripts
on pitch only; the in-game result outranks it. The roll sign is only weakly pinned (the scripts barely use roll), T10 still decides.
The resulting reading: positive Pitch noses DOWN, positive Yaw turns RIGHT, positive Roll rolls RIGHT (clockwise from behind).

Not verifiable without the game (unchanged, flagged by the integrator): the 120-frame ramp and its restart on every override write
(the setters mark the gyro system dirty even when the value is unchanged; if Keen only does so on a change, a script that holds
three constant values would see torque build up instead of restarting; the PD scripts write changing values every run, so the
effect is small); thrust applied through the COM; yaw sign of `Raycast(d, pitch, yaw)` (sim: +yaw aims LEFT, like
`CreateRotationY`; scripts use symmetric patterns, so the effect is small); linear explosion falloff; per-axis (not conic) cone
limit for the pitch/yaw overload; armour/ammo mass not modelled (infinite ammo, no inventory mass).
