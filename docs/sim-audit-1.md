# Sim audit 1: why the baseline (v2.0) looks the way it does

Scope: `out/sim/results/baseline-v2.0/runs.jsonl` (47,300 runs) plus new targeted runs with a private build. No script and no sim file was
changed. Analysis scripts: `tools/sim_audit_timeouts.py`, `tools/sim_audit_ships.py` (both `python3 -I`, read `runs.jsonl`).
Extra runs (20-30 seeds each, `--jobs 2`) are listed in section 8 with their commands.

## 0. Short version

1. **The scripts almost never find a small grid by themselves.** Self-acquisition is limited by one thing: the confirm raycast pattern
   (`AiStep`, bug 3.2) never fires down the boresight, so the hybrid AI+camera search only works when the AI block holds the enemy closer than
   about 390 m. In the default scenarios 85-89 % of ship-runs never get a script target. Everything else follows from this.
2. **Once a script holds a target it is far better than the AI stand-in**: 70-75 % hits and 4-8x damage for Aegis/Harrier/Lancer/Mantis,
   fights end in 12-34 s instead of 130-170 s. Aegis/Harrier/Lancer are on top because their `ProjSpeed` is 800; Hornet (400) is not. The
   AI-only hit rates of all gatling models are the same (27 %).
3. **59 % of all timeouts are a scenario/AI-stand-in artefact**: the ships start beyond the modelled AI search range (2,500 m, hard-coded in
   `VanillaAi.SearchRange`) and nobody ever sees anybody. 34 % are real "fought but no kill in 300 s". 7 % are teams without any finder.
4. **Swarm and Watcher results are about acquisition only.** With a pre-set lock the 6/12-Gnat swarm kills a Hornet/Aegis/Midge/Longbow in
   6-10 s in 85-100 % of runs; without it the Gnats have no sensor that can see 700 m and do nothing. The Watcher has no AI block, shares on
   `HORNET_TGT` while the Gnats listen on `GNAT_TGT`, and Stinger only listens to `GNAT,HORNET,LONGBOW`.
5. **Collisions are wingmates flying into each other** (10,206 of 10,207 collision events are between teammates). No combat script except
   Gnat has a separation term. **Friendly fire is rare (0.6 % of ship-runs) but each railgun/rocket event is huge.**
6. The three script bugs of `docs/sim-validation.md` section 3 are all present in the other scripts as indicated in section 5.

## 1. Why do 49 % of runs time out?

`tools/sim_audit_timeouts.py` output, all 47,300 runs: 22,982 timeouts (48.6 %).

| cause | timeouts | share |
|---|---:|---:|
| a finder exists, closest approach of the teams > 2,500 m (nobody within the AI model's search range, nobody shoots) | 12,849 | 55.9 % |
| a finder exists, shots fired, no kill within 300 s | 7,712 | 33.6 % |
| only Gnat/Stinger/Watcher on the field, enemy closed, no shot (no finder) | 1,697 | 7.4 % |
| only Gnat/Stinger/Watcher on the field, start > 2.5 km | 703 | 3.1 % |
| a finder exists, closed in, no shot | 21 | 0.1 % |

- **Armed-vs-armed 1v1** (no Gnat/Stinger/Watcher; 5,500 runs, 2,024 timeouts): 91.2 % of the timeouts (1,846) have a closest approach above
  2,500 m and 0 shots fired. The start distance is uniform 1.5-3.0 km (`Scenario.StartDistanceMin/Max`) and `VanillaAi.SearchRange = 2500`
  is hard-coded, so 33 % of default runs cannot start whatever the scripts do. The scripts' own search cannot help (point 2).
- **Why the script search cannot help at that range (analytic, matches the runs):** a ray hits a small grid only if it lands inside its angular
  radius `atan(r/R)`. For r = 4 m: 0.76 deg at 300 m, 0.31 deg at 730 m, 0.15 deg at 1,500 m. The 35 deg auto-scan cone has 3,700 deg^2; the
  target covers 0.45 deg^2 at 600 m, i.e. 1.2e-4 per ray; a camera casts 1.7 rays/s at 1,200 m (charge 2 km/s) so the expected time to a hit is
  more than 1,000 s. Measured: scripts-only (`ai off; hunt`), 1.5-3 km, 284 mirror runs of 10 models: **284 timeouts, 0 shots.** Scripts-only at
  400-900 m start: 152 timeouts of 171; the only model that finds anything is Basilisk (large grid, 17 of 20 mirror wins), 2 of 20 for Longbow L.
- **"Slow" flag (speed < 1 m/s for 30 s): 31-33 % of every AI-block model, 92 % of Gnat/Stinger.** It is not a stuck ship. It is a ship that
  waits (HUNT with no target holds the home point and sweeps the nose) or that the metric catches after the kill. Treat it as "no target".
- **No script target ever acquired:** share of 1v1 ship-runs that ever had a script target: Aegis 11 %, Harrier 11 %, Hornet 12 %, Lancer 13 %,
  Mantis 9 %, Longbow S 12 %, Wasp 15 %, Basilisk 60 % (large target), Midge 0 % (scriptless), Gnat/Stinger/Watcher 0 %. When it does happen the
  median first script target is at 58-94 s (p10 7-26 s, p90 136-189 s) after the AI has already walked the ships to short range.
- **Lock lost:** with the target handed over at t = 0 (`ai off; lock; hunt`, facing, 800-950 m, 30 seeds) the timeouts that remain
  (Aegis 33 %, Harrier 36 %, Lancer 63 %, Hornet 30 %, Wasp 16 %) are ships that lost the lock after a mean of 8 s of script target and 5 s of time
  on target (Aegis/Lancer/Harrier/Wasp timeouts), and then never recovered (scripts-only has no AI to hand back to). In 8 traced
  Lancer-vs-Lancer fights the lock was lost after 5-18 s in every one. `MaintainLock` casts one ray at the predicted centre; there is no search
  around it (bug 3.3).
- **Standoff too long / hold logic:** not a cause in the script-flown phase (standoffs 220-420 m, 600 for Basilisk, are inside weapon range).
  In the AI-flown phase the ships stop at about 730 m (trace Hornet duel seed 0: both hold 730 m for 117 s) which is the stand-in's behaviour,
  not the scripts'. The stand-in speed limit (10 m/s) makes a 2 km approach take 150-200 s.
- **AI model dependence:** the 7,712 "fought, no kill" timeouts contain the AI stand-in's low hit rate (27 % at 730 m) and its 10 m/s speed;
  they are the least trustworthy part of the baseline.

## 2. Gnat, Stinger, Watcher: 0 % in 1v1; swarm and "92 % slow"

**Root cause: no acquisition path, not formation, not IGC range, not the engine.**

- Gnat blueprint: sensor (50 m), 6 cameras, no AI block, no turret. `AiStep()` returns false (`_ai.Count == 0`), so the script runs
  `AutoScanStep` (35 deg cone, 1,200 m, same statistics as above) and `OmniScan` (120 m). Swarm scenarios (`ScenarioGen.Swarm`) contain **no feeder**.
- Baseline 6/9/12 Gnat vs single models: Gnat `shots` 0-2 per ship-run, `script_target_s` 0.0-0.2, `first_script_target_s >= 0` in 0-1 % of
  ship-runs, `avg_speed` 2.4-3.4 m/s, min enemy distance 1,030-1,090 m. They sit at the home point and are hunted by the enemy's AI. Wins that
  exist: swarm vs Pirate (the Pirate brain drives into the 120 m omni-scan).
- **Swarm logic works when it has a target.** `--face --start-distance 500:700 --commands "0.5:lock;0.5:hunt"` (default AI on for the enemy),
  20 seeds:

  | scenario | Gnat team wins | median kill time | Gnat hit rate | Gnat alive at end |
  |---|---:|---:|---:|---:|
  | 6xGnat vs Hornet | 20/20 | 8.3 s | 12 % | 88 % |
  | 6xGnat vs Aegis | 19/20 | 10 s | 13 % | 82 % |
  | 6xGnat vs Midge | 20/20 | 7.5 s | 8 % | 91 % |
  | 6xGnat vs Longbow S | 19/20 | 9.7 s | 14 % | 99 % |
  | 12xGnat vs Hornet | 20/20 | 7.3 s | 9 % | 96 % |
  | 12xGnat vs Aegis | 19/20 | 6.8 s | 9 % | 92 % |
  | 12xGnat vs Midge | 17/20 | 6 s | 4 % | 88 % |
  | 12xGnat vs Longbow S | 20/20 | 8.3 s | 10 % | 100 % |

  Slots, attack cone, separation (2 uses), hold-fire and yo-yo all work. The 8-14 % hit rate is the `ProjSpeed = 400` bug (3.1); validation
  showed Gnat 6.4 % -> 12.9 % with 800.
- **Watcher feeder is broken twice.** (a) `Watcher` blueprint CustomData has `IGC Tag=HORNET` (tools/designs/support.py `watcher_frame`) while the
  Gnat default tag is `GNAT` and `ReadIGC` only listens to `GNAT_TGT`; no mothership relay is in the `Watcher+3xGnat` scenario. (b) The Watcher
  has no AI block (only Hornet script + 11 cameras), so it finds nothing either: `Watcher+3xGnat` team 0 never won in 1,700 runs (340 team-1 wins,
  1,360 timeouts), Watcher script target 0 %, Gnat shots 0.
- **Stinger:** `ListenTags = "GNAT,HORNET,LONGBOW"`; the Aegis/Harrier/Lancer/Mantis/Wasp/Basilisk tags are not listened to, no AI block, no
  search motion, nobody feeds it in the 1v1/1v2 scenarios (0 shots by design: kamikaze). Its 0 % is structural, as expected.
- **92 % Gnat "slow"** = parked with no target (see section 1), not a flight problem. 107 Gnat collisions are negligible.
- Engine: IGC delivery, antenna range (4 kW/km model) and the camera model were not limiting. No engine defect found.

## 3. Ranking anomalies and hit rates

Armed-vs-armed 1v1 only (Gnat/Stinger/Watcher excluded), per ship-run: win % / damage dealt / damage taken.
Aegis 42.0 / 8,332 / 2,937; Harrier 38.5 / 6,608 / 2,799; Lancer 38.5 / 6,791 / 2,634; Hornet 35.4 / 5,755 / 3,151; Midge 30.9 / 4,708 / 2,274;
Basilisk 30.6; Wasp 22.9 / 2,625 / 3,846; Mantis 22.6; Longbow S 21.2; Longbow L 18.5.

Hit rate and damage by who is flying (1v1, ship-runs that fired, Aegis shown; other columns in `sim_audit_ships.py` and the table below):

| model | AI-only phase: hit% / dealt | script has target >= 20 s: hit% / dealt |
|---|---|---|
| Aegis | 26.8 / 6,115 | **71.6 / 46,248** |
| Harrier | 27.4 / 5,785 | **75.5 / 31,617** |
| Lancer | 23.4 / 4,761 | **71.9 / 39,836** |
| Hornet | 27.8 / 6,058 | 20.7 / 21,441 (`ProjSpeed` 400) |
| Mantis | 37.6 / 2,187 | 69.7 / 36,983 |
| Longbow S | 38.0 / 1,982 | 91.1 / 47,981 |
| Wasp | 10.9 / 5,146 | 32.8 / 5,343 |
| Basilisk | 2.5 / 2,339 | 31.0 / 11,619 |

- **Aegis/Harrier/Lancer on top:** the AI-only hit rate is the same for every gatling model; the difference is entirely the script phase
  where they hit 72-75 % (800 m/s correct) and Hornet 21 % (400 m/s assumed). The ranking is ordered by "script share x script quality".
  Aegis's two `SmallDecoy` blocks do nothing in the sim (`SimGenericFunctional`), so they are not the reason.
- **Wasp 13 % hit rate:** 90 % of Wasp ship-runs are AI-flown, where the stand-in fires rockets (100 -> 200 m/s, 4 m blast, 4 s of flight at 730 m)
  at a target that strafes 10 m/s: 10.9 % hits. With the script flying (standoff 300 m, 1.5 s) 32.8 %. Wasp also fires only 25 rockets per run.
  Part artefact (stand-in fires at 730 m), part design (the script itself should refuse long shots; `MaxFireRange` 650 is 3.2 s of flight).
  Against large targets it hits 57-83 % (Longbow L, Pirate).
- **Analytic check, gatling 800 m/s, range 350-730 m:** flight time 0.44-0.91 s; the script leads on current velocity, so only a velocity
  change of the target inside the flight window causes a miss. With the stand-in's reversals every 1.5-3.5 s and 10 m/s, expected hit rate
  is 55-75 % at 350 m and 40-65 % at 730 m before spread (0.15 deg, 1.9 m at 730 m). The script phase (72-75 %) matches; the AI-only 27 % at 730 m
  is below it and therefore an AI-stand-in effect ([I] aim/lead model), not script behaviour. Hornet's 21 % corresponds to a lead twice too long
  (0.88 s instead of 0.44 s at 350 m): about 3.5 m of error on an 8 m/s target against a 4 m silhouette.
- **Metric artefact:** `hit%` above 100 % (Longbow L 124-128 %, Wasp vs Watcher 110 %) because piercing slugs and rocket splash count several hits per
  shot. Rank by damage, not by hits/shot, for railguns and rockets.

## 4. Friendly fire and collisions

**Collisions** (`tools/sim_audit_ships.py`): 10,206 of 10,207 collision events are between ships of the same team; ships on single-ship teams
have 1. Per ship-run in multi-ship teams: Basilisk 9.4 %, Longbow L 6.4 %, Watcher 4.6 %, Mantis 1.8 %, others below 1.3 %. Longbow L's 3,260
and Basilisk's 3,200 are events from a few hundred runs where two wingmates sit in permanent contact (cooldown 30 ticks per event).
- Trace `pirate/2xLongbow S-vs-Pirate` seed 65: after takeover (t = 46 s) both ships fly the same orbit at 1,327 m, 40.4 m/s, and converge from
  52 m to 2.5 m apart within 25 s and stay there for 230 s (460 events each, 2 shots total). Same orbit, same speed, no separation term.
- `Separation()` exists only in `GnatSwarmAI.cs` (2 hits). Aegis/Basilisk/Harrier/Hornet/Lancer/Longbow/Mantis/Wasp have none.
- With `CollisionDamage = False` this costs nothing in the sim; in the game two 2.5 m-apart ships would take damage. Fix is cheap and worth
  doing (per-mate repulsion from `_mates` positions).

**Friendly fire:** 566 ship-runs of 94,600 (0.6 %). Damage is dominated by single big events: Longbow L 23 events, 17,260 damage per hit;
Wasp 41 runs, 4,975 per hit event; gatling models 85-90 per hit (Hornet 120 runs, Midge 129, Basilisk 138).
- Longbow L trace `2v3/2xLongbow L-vs-3xGnat` seed 21: wingman 77 m away takes a 50,000-damage slug at t = 173 s. `LineClear()` (Longbow
  L801) is one camera ray at `dist - radius` and **returns true when no camera has the charge/cone** (`cam == null`), which is exactly the case
  right after a reload cycle. It does not use the IGC `_mates` positions that the script already receives.
- Hornet `CheckLineOfFire` (L734): same fail-open `cam == null`, checked only every 20th tick; hold fire 1 s.
- Wasp `MatesInTheWay` (L795) uses IGC but compares the mate's position extrapolated to *now* with a 4.5 m flight-line and 9 m blast
  clearance; a rocket takes up to 3.2 s so the mate moves 20-60 m before impact.
- Midge (scriptless stand-in) has no friendly check in the sim (`VanillaAi`); 129 ship-runs. Unknown for the real AI block.

## 5. The three script bugs from docs/sim-validation.md section 3, per script

| script | 3.1 `ProjSpeed` 400 (gatling) | 3.2 confirm-ray pattern misses boresight | 3.3 fragile `MaintainLock`, no re-acquire |
|---|---|---|---|
| HornetDroneAI (also Watcher) | **present** L38 (400) | **present** L1233 (`3*sqrt((i%13+.5)/13)`, min 0.59 deg) | present L478 |
| GnatSwarmAI | **present** L38 (400) | same code L1138 but inert (no AI blocks, `_ai.Count==0`) | present L438 (slightly different code) |
| LongbowSniperAI | fallback `ProjSpeed = 400` L381 only when the weapon is unknown (railgun path 1000/2000 is right): harmless in the fleet | **present** L1295 | present (`MaintainLock(double)` L486) |
| AegisAI | ok (800, L53) | **present** L1319 | present L522 (byte-identical to Hornet/Harrier/Lancer/Wasp/Basilisk) |
| HarrierAI | ok (800) | **present** L1268 | present L508 |
| LancerAI | ok (800) | **present** L1273 | present L484 |
| MantisAI | ok (`GatSpeed` 800, railgun 1000/2000) | **present** L1367 | present (`MaintainLock(double)` L507) |
| WaspAI | n/a rockets: `RktV0 100, RktAcc 600, RktVmax 200` equals the data (stats.md); no bug | **present** L1328 | present L504 |
| BasiliskAI | n/a rockets, `ProjSpeed` 200 informational | **present** L1336 | present L512 |
| StingerAI | n/a | n/a (no AI hybrid, no ray pattern); listens only to GNAT/HORNET/LONGBOW | n/a |
| HiveMotherAI / motherships | n/a | n/a | n/a |

Only 3.2 and 3.3 matter for acquisition; 3.1 explains the Hornet/Gnat hit rates.

## 6. Prioritised improvement list (evidence, file/function, expected effect)

Priority 1: all of these move default and scripts-only numbers the most.

1. **Confirm-ray pattern (AiStep).** Files: all nine `*AI.cs` at the `_aiScan % 13` line (Hornet L1233, Aegis L1319, Basilisk L1336,
   Harrier L1268, Lancer L1273, Longbow L1295, Mantis L1367, Wasp L1328; Gnat inert). Make every second ray a centre ray (r = 0), add a small
   cross/ring at 0.1-0.3 deg, then widen to 3 deg; cap the ray length at `c.AvailableScanRange` instead of requiring a full 2,600 m charge.
   Evidence: the AI points the guns with median 0.1 deg error (trace) while the first ring ray is 0.59 deg; takeover happens only when the AI
   holds the enemy closer than r/tan(0.59 deg) = 390 m. Expected: script target in most default runs within seconds of the AI aiming;
   locked-start fights end in 12-34 s instead of 130-170 s (experiment 8.4), approach at 40-98 m/s instead of 10 m/s.
2. **Hornet and Gnat `ProjSpeed` 400 -> 800** (`HornetDroneAI.cs:38`, `GnatSwarmAI.cs:38`; Longbow fallback `LongbowSniperAI.cs:381`).
   Evidence: hit rate with script target 20.7 % (Hornet) vs 72-75 % (Aegis/Harrier/Lancer); validation 1.8 % -> 26.6 %. Expected: Hornet ~3x
   the damage once it flies.
3. **Lock maintenance and re-acquisition** (`MaintainLock`, `ProbePoint`, `AimMissed`; Longbow/Mantis `MaintainLock(double)`). Cast around the
   predicted point (spiral 0.5-2x radius, widening with age), use the filtered acceleration for the extrapolation, cast with `dist*1.05`
   rather than `dist+20`, and when the lock is gone for more than 2 s go back to `AiStep` (hybrid) or to a narrowing search. Evidence: after a
   locked start the survivors drop the lock after 5-18 s (8 Lancer traces) and timeouts of 26-63 % remain for the gatling models; Aegis
   timeouts held the script target for 8 s on average.
4. **Gnat/Watcher/Stinger feed chain** (acquisition for swarms):
   - Gnat listens to extra `_TGT` tags (`HORNET`, `LONGBOW`, `WATCHER`, configurable list like Stinger's `ListenTags`) or the Watcher blueprint
     uses tag `GNAT` (`tools/designs/support.py` `watcher_frame(tag=...)`; this is a blueprint/design change).
   - The Watcher needs a detector that works: an AI block pair (like Hornet), or a Custom Turret Controller `GetTargetedEntity` source. Raycast
     search cannot do it (section 1).
   - `StingerAI.ListenTags` default: add `WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER`.
   Expected: `Watcher+3xGnat` from 0 % to the pre-lock numbers of section 2 once a target exists. Needs the sim fix in section 7 (sim-side
   AI model for the Watcher) to be measured.
5. **Wingmate separation** in the eight non-Gnat combat scripts (`Control` translation block): add a repulsion velocity from `_mates` positions
   (the data already arrives over `_SWARM`) when a mate is within ~60 m (small) / ~150 m (large). Evidence: 99.99 % of collisions are between
   teammates; Basilisk 9.4 % and Longbow L 6.4 % of multi-ship ship-runs, steady contact at 2.5 m. Cost: a few vector operations per tick.

Priority 2

6. **Friendly-fire gating from IGC positions** (Longbow `LineClear` L801, Mantis `LineClear` L831, Hornet/Aegis/Harrier/Lancer
   `CheckLineOfFire`, Wasp `MatesInTheWay` L795): test the point-to-line distance of every mate (`Pos + Vel*age`, `age <= 1 s`) to the fire line
   with a margin of mate radius + 5 m, and do not fail open when the camera has no charge. For rockets extrapolate the mate to the rocket's
   arrival time and use `LineClear >= 8 m`, `BlastClear >= 15 m`. Evidence: 23 Longbow L slug events of 17,260 damage each, Wasp 41 ship-runs.
7. **Wasp: shorten the engagement range** (`MaxFireRange` 650 -> 450 and `Standoff` 300 -> 250, `WaspAI.cs:47-49`, and refuse launches at
   intercepts beyond 2.5 s of flight against small grids). Evidence: 6-10 % hit rate against small AI-flown targets (730 m, 4 s of flight), 33 %
   with the script flying at 300 m.
8. **Longbow fire rate against large targets** (`EngageRange` L564 clamps to exactly `WeaponRange*RangeMargin` and `inRange` L683 tests the lead
   travel against the same number, so the ship parks at 1,327 m on the limit). Single Longbow S vs Pirate seed 3 fires 12 of a possible 13
   shots in 300 s (all hit), so the edge is not blocking by itself; but the 2xLongbow S run (seed 65) fired 2 shots in 250 s while the
   wingmate sat 2.5 m away, most likely held by `LineClear` (1.5 s hold each time the neighbour is in the ray; hypothesis, not traced).
   Clamp the engage range to 0.9 of the limit anyway and fix item 5/6 first. The Pirate (260 blocks) also needs far more than 12 slugs:
   257/260 blocks left after 87,000 damage, so most "timeouts" there are damage-limited, not a script failure.
9. **HUNT with nothing in sight**: instead of holding the home point with a nose sweep, in HUNT advance to the last team target or a fan of
   search points when `ai on` is unavailable. Only helps ships without AI blocks; the camera statistics make it a low-yield change.

## 7. Things in the sim that look like artefacts, not game behaviour

1. `VanillaAi.SearchRange = 2500` with start distance up to 3,000 m: 33 % of default 1v1 runs are dead on arrival. The real AI block range
   is not known (research.md). Report default-mode results both with and without the runs that start beyond 2.5 km, or run 1.5-2.4 km.
2. `VanillaSpeedLimit = 10 m/s`: approach phases of 100-200 s dominate time-to-kill in default mode. [I].
3. The stand-in's AI-only aim/lead and 730 m hold (hit rate 27 %, ToT 25 s) set every AI-flown number; Wasp's 10.9 % hit rate there is the
   stand-in firing rockets at 4 s of flight.
4. Decoy blocks (Aegis has 2) are inert; in the game they attract turret/AI fire.
5. `CameraMaxCharge = infinity` (settings): a camera can bank unlimited range; the game may cap it, which would hurt long probes.
6. Metrics: `stuck`/`slow` flags parked ships; `hit%` over 100 % for piercing/splash ammo; the collision counter counts one event per
   30 ticks of contact, so contact time is reported as "thousands of collisions".
7. Collision damage is off, so wingmate collisions cost nothing; Longbow L results (mutual kills) are optimistic about wingmates.
8. IGC is blocked between enemy teams and relationship is Enemies for every team (neutral factions are skipped by the scripts in game).
9. `FakeInstructionCount = 0`: the 50,000-instruction in-game limit is not modelled (`script_max_tick_ms` p99 about 10 ms is wall time, JIT included).
10. Railgun reload/charge (20 s small, large 60 s per script, capacitor model in the sim) is an inference ([I]), the same number the script assumes;
    the data sheet says 4 s reload.
11. Mutual-kill / "disabled" definition: a ship is out when it can no longer fight; small damage to gun blocks kills combat capability while
    the grid stays alive. A 12-Gnat swarm "wins" in 3-10 s mostly by disabling the two gatlings.

## 8. Reproduction (private build, nothing in the repo was changed)

```
export SIM_OUT=<scratch>/auditor-sim; bash sim/build.sh
python3 -I tools/sim_audit_timeouts.py      # section 1 tables
python3 -I tools/sim_audit_ships.py         # sections 3-4 tables
```
1. Scripts-only mirror, default distance: `run --filter '^1v1/(Aegis|Basilisk|Harrier|Hornet|Lancer|Longbow L|Longbow S|Mantis|Wasp|Gnat)-vs-\1$'
   --seeds 30 --commands "0.5:ai off;0.5:hunt"` -> 284 runs, 284 timeouts, 0 shots (same filter written as an explicit list in practice).
2. Scripts-only, near: add `--start-distance 400:900`, 20 seeds -> 152 timeouts of 171; Basilisk 17/20 wins, Longbow L 2/20, rest 0.
3. Swarm pre-lock: `--filter '^swarm/(6x|12x)Gnat-vs-(Hornet|Aegis|Midge|Longbow S)$' --seeds 20 --face --start-distance 500:700 --commands "0.5:lock;0.5:hunt"`.
4. Script-flown fights (acquisition solved): `--face --start-distance 800:950 --commands "0.5:ai off;0.5:lock;0.5:hunt"`, 30 seeds, same seeds
   as baseline seed_index 0-29:

   | scenario | baseline timeout / win / median ttk | pre-lock timeout / win / median ttk | hit% (base -> pre-lock) |
   |---|---|---|---|
   | Aegis-Aegis | 30 / 60 / 172 s | 33 / 66 / 14 s | 24.8 -> 23.3 |
   | Aegis-Hornet | 33 / 46 / 156 s | 40 / 60 / 12 s | 19.2 -> 6.1 |
   | Harrier-Harrier | 40 / 50 / 132 s | 36 / 63 / 16 s | 27.4 -> 24.3 |
   | Lancer-Lancer | 40 / 56 / 165 s | 63 / 36 / 22 s | 24.2 -> 21.4 |
   | Hornet-Hornet | 20 / 73 / 158 s | 30 / 66 / 106 s | 22.8 -> 1.8 (400 m/s) |
   | Hornet-Wasp | 43 / 56 / 139 s | 26 / 73 / 27 s | 24.3 -> 2.5 |
   | Hornet-Longbow S | 33 / 66 / 168 s | 6 / 93 / 34 s | 34.1 -> 4.6 |
   | Wasp-Wasp | 33 / 56 / 157 s | 16 / 76 / 20 s | 6.4 -> 31.0 |
   | Basilisk-Basilisk | 33 / 63 / 88 s | 3 / 90 / 19 s | 92.8 -> 94.5 |
   | Mantis-Mantis | 40 / 53 / 163 s | 13 / 73 / 5 s | 18.2 -> 30.3 |
   | Longbow S-Longbow S | 36 / 60 / 156 s | 3 / 83 / 4 s | 32.1 -> 100 |
   | Longbow L-Longbow L | 36 / 40 / 54 s | 16 / 53 / 3 s | 128.6 -> 124.2 |

   Note this is an upper bound for fix 1 (lock at t = 0.5 s at 800-950 m); a lock at 1.5-3 km fails because the lock ray at 0.5 s has only
   1,000 m of charge and later the nose has swept away (`--commands "3:lock"` gave 0 shots).
5. Traces quoted: `1v1/Hornet-vs-Hornet` seed 0 (default), `pirate/2xLongbow S-vs-Pirate` seed 65, `pirate/Longbow S-vs-Pirate` seed 3,
   `2v3/2xLongbow L-vs-3xGnat` seed 21, `1v1/Lancer-vs-Lancer` seeds 0-7 (pre-lock), all with `one --trace`.

## 9. Unverified assumptions about the real game

- That an AI Offensive/Flight block aims the guns precisely enough (0.1 deg) for a centre ray to hit; the stand-in's aim is `[I]`.
- That a script can switch AI blocks off and on at runtime and take control at once (HANDOFF section 3 has no in-game confirmation of v1.8's hybrid).
- That cameras can bank 2,600 m of charge and cast rays at 1.5-3.3 rays/s as modelled.
- Railgun reload/capacitor timing, rocket speed curve, gatling spread, and collision damage in the game.
