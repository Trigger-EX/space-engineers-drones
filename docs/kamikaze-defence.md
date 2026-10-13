# Kamikaze defence (incoming swarms of small warhead drones)

Shared block `// >>> KAMIKAZE DEFENCE v1.0` in every combat drone script (Hornet, Wasp, Harrier, Lancer, Aegis, Mantis, Basilisk,
Longbow S/L, Gnat). Source: `tools/kz_block.cs`, kept identical in every script by `python3 -I tools/kz_sync.py --apply`
(`--check` exits 1 when a copy differs). Package v2.4; script versions 2.6-* (Longbow 2.4, Gnat 1.13).

## 1. The threat and what went wrong before

A swarm of small warhead drones (the project's Stinger, or anyone's 1-3 m kamikaze) flies straight at a drone at the 100 m/s speed cap
and detonates within a few metres (small warhead: 4.5 m blast). New simulator scenarios (`kamikaze`, section 5) showed what the scripts
before this change did against 4 and 8 Stingers:

- **They wrote the kamikazes off as wrecks.** A Stinger correcting its course at the speed cap spins 1-2 rad/s. The scripts' "tumbling
  for 4 s = disabled" rule fired, and 5 s later the drone dropped its target, ignored it for 120 s and broadcast "finished" to the whole hive.
  That usually happened 1.5 km out, just before the Stinger arrived. The AI confirm rays could not take it back, because ignored ids fail `Valid()`.
- **They held on to harmless wrecks.** A Stinger crippled by gunfire drifting past stayed the target for 20 s. The AI-block search (the only
  all-round long-range detector a drone has against something this small) only runs without a target, so the rest of the pack arrived unseen.
- **No evasion.** The drones flew their normal standoff orbit (or sat at the AI blocks' 10 m/s) while the pack closed in.

Baseline (package v2.3 scripts, 620 runs): 57% of the defending drones were still combat capable at the end, and only 1.0 Stinger per fight
was put out of action before it could detonate.

## 2. What the block does

A contact counts as an **incoming kamikaze** when all of these hold:
- it is small (bounding radius at most `Max Kamikaze Radius`, default 3.5 m; any missile also counts);
- it was seen in the last 3 s;
- it is closing faster than `Min Closing Speed`;
- its straight-line closest approach to me, or to a hive mate, is within `Miss Distance` + `Miss Distance Per km Of Range`;
- that approach is less than `Warning Time` away;
- it is alive: closing over 30 m/s, spinning or accelerating, or already classed as a kamikaze a moment ago (drifting debris is not chased).

Once classified, the following apply (each can be switched off in Custom Data):

| # | Countermeasure | Detail |
|---|---|---|
| 1 | **Never a wreck** | `Disabled()` is skipped for it, out to 5 x Warning Time (60 s) of closest approach, so the spin and size rules cannot drop it. |
| 2 | **Spent = dropped** | A former kamikaze that has not been one for 4 s and coasts (over 20 m/s, no thrust, not spinning) is a wreck on its way out. It is dropped at once, so the AI-block search resumes. |
| 3 | **Priority** | Score bonus `Priority Bonus` x (0.4 + 0.6 x urgency), where urgency grows as time to impact drops below Warning Time. A threat to a hive mate gets 60% of that. In GUARD/FORM mode a kamikaze outside the guard radius is still engaged. |
| 4 | **Back away** | Within `Back Away Within` (800 m), the drone's velocity away from the threat is at least `Back Away Speed` x urgency (urgency measured over `Back Away Reaction Time`, 10 s). The guns stay on target: the gyros aim, the thrusters translate (the Gnat uses its reverse thrusters along the nose). Against a 100 m/s kamikaze this turns about 100 m/s of closing into 40, so the guns get roughly 2.5 times the firing time. |
| 5 | **Break sideways** | Inside `Break Time` (2 s) of impact, with a predicted miss under `Break Miss Distance` + its radius, the drone adds `Break Speed` (40 m/s) along the miss vector, which pushes the closest approach wider. A dead-centre approach gets a random perpendicular side, kept until the threat direction changes. When over the speed cap, the rest of the velocity command is shrunk and the break is kept whole. A Stinger then fires its own closest-approach trigger about 30 m away, far outside its 4.5 m blast. |
| 6 | **Track and pack-search rays** | Spare camera charge, leaving a reserve of `Camera Charge Reserve` metres per camera: one ray refreshes the stalest incoming track that is not the current target, and every second pass one ray searches within `Pack Search Radius` of a known kamikaze for the rest of its pack. |
| 7 | **Share** | The two most urgent incoming tracks the drone sees itself go out twice a second on `<Tag>_TGT` and the FLEET channel (same tuple as a target share), so wingmates shoot what dives on me. |

The Gnat also skips its run-in (SETUP) against a kamikaze: the target is coming to it, so it turns its nose straight onto the target and shoots.

PB screen line: `Kamikaze: none incoming`, or `!! KAMIKAZE x3 (2 at me)  next 4.2 s 380 m`.

## 3. Configuration (`[Kamikaze Defence]` in the PB's Custom Data; written on first run, `reload` re-reads)

| Key | Default | Meaning |
|---|---|---|
| Enabled | true | master switch (false = the behaviour before package v2.4) |
| Max Kamikaze Radius (m) | 3.5 | bounding radius gate (Stinger ~2-3.4 m depending on orientation; fighters 2.8-4.8 m, so some fighters pass the gate and are then judged by behaviour) |
| Warning Time (s) | 12 | act on threats whose closest approach is less than this away |
| Miss Distance (m) | 40 | closest approach that counts as "coming for us" ... |
| Miss Distance Per km Of Range (m) | 60 | ... plus this per km of range (a kamikaze far out is still correcting) |
| Min Closing Speed (m/s) | 15 | |
| Priority Bonus | 2500 | target-score bonus (metre equivalents; stickiness is 400) |
| Protect Hive Mates | true | also classify threats to hive mates (priority only, no evasion for them) |
| Back Away / Back Away Speed (m/s) / Back Away Within (m) / Back Away Reaction Time (s) | true / 60 / 800 / 10 | |
| Break Sideways / Break Time (s) / Break Speed (m/s) / Break Miss Distance (m) | true / 2 / 40 / 20 | |
| Track And Pack Search Rays / Pack Search Radius (m) / Camera Charge Reserve (m) | true / 60 / 600 | |
| Share Threats With Hive | true | |

Sim sensitivity (measured during development on an earlier build, section 5 scenarios): Back Away Speed 90, Reaction Time 15, Break 3 s / 60 m/s
and Max Radius 4.5 each changed the totals by at most 1 point. The defaults are not finely tuned, and the result is not fragile to them.

## 4. Limits (read before trusting it)

- **Detection is the hard part.** A 2 m drone at 300 m is 0.4 degrees wide; random camera rays practically never find it, and the sensor
  (50 m) sees it half a second before impact. The drones find kamikazes through their AI blocks (Full variants), designators
  (turrets / Custom Turret Controllers) and hive mates. A lone Gnat (no AI blocks) still loses every fight against Stingers: it sees them at 45 m.
  In the development runs, about 60% of the defenders still lost (Gnats aside) first locked any kamikaze less than 3 s before they were knocked out. **Give a fleet that
  expects kamikazes a turret or Custom Turret Controller (it designates for every drone in the hive), or a Watcher / Aegis spotter.** The
  Basilisk (turret) and Longbow L took almost no damage in any kamikaze scenario.
- Size alone does not separate a Stinger from a fighter (world bounding boxes of rotated grids overlap), so a fighter diving straight at
  a drone can be classed as incoming. It then gets priority and the drone backs away. In the 1v1 regression (section 5) this cost no measurable score.
- Debris on a collision course faster than 30 m/s counts as alive (dodged and shot, which wastes some rounds).
- In the sim, the Stinger's own trigger (closest approach under 30 m) makes most dodged Stingers explode harmlessly about 30 m away. A smarter
  kamikaze that keeps turning back would make the break less decisive (the back-away and the guns still apply).
- Fewer "wins" in some 2-drone fights: a dodged Stinger that loses its target coasts away alive until the time cap (a timeout, not a loss).

## 5. Verification (simulator, `sim/`)

New pieces: scenario kind **`kamikaze`** (`sim/src/Run/ScenarioGen.cs`): each scripted combat model x1 vs 4 and 8 Stingers, x2 vs 8, and 3
Gnats vs 8, 31 scenarios in all. The Stinger team gets a radar cue every 0.5 s (setting `SwarmCueSeconds` / `SwarmCueTeam`, injected on
`DRONE_CUE`), which stands in for a carrier's turrets: on their own the Stingers only see 50 m by sensor and never launch, so every
kamikaze run is a real attack. `tools/kz_report.py` compares two result files. `one --echo-every S [--echo-ship I]` prints a ship's PB
screens during a single run (debugging).

Results on top of package v2.3 (LEAD v1.0), 20 seeds per scenario, same seeds before and after (620 runs each; 20 seeds is enough here
because the effect is large). "surv" = defenders still combat capable at the end, "hull" = defender mass lost, "shot" = Stingers out of
action without detonating, "boom" = Stingers that detonated, both per run.

| scenario | n | win | surv | hull | dmgT | shot | boom |
|---|---|---|---|---|---|---|---|
| 2xAegis-vs-8xStinger | 20 | 90% -> 85% | 62% -> 90% | 36% -> 10% | 13398 -> 3662 | 0.9 -> 1.4 | 7.0 -> 6.5 |
| 2xBasilisk-vs-8xStinger | 20 | 95% -> 90% | 100% -> 100% | 1% -> 0% | 5073 -> 814 | 3.7 -> 3.6 | 4.2 -> 4.2 |
| 2xGnat-vs-8xStinger | 20 | 90% -> 90% | 45% -> 45% | 55% -> 55% | 8628 -> 8628 | 0.0 -> 0.0 | 8.0 -> 8.0 |
| 2xHarrier-vs-8xStinger | 20 | 95% -> 90% | 80% -> 78% | 20% -> 21% | 7059 -> 7246 | 1.2 -> 1.4 | 6.7 -> 6.5 |
| 2xHornet-vs-8xStinger | 20 | 95% -> 95% | 68% -> 88% | 30% -> 10% | 9987 -> 3454 | 1.1 -> 1.5 | 6.8 -> 6.5 |
| 2xLancer-vs-8xStinger | 20 | 100% -> 85% | 80% -> 80% | 21% -> 14% | 7211 -> 4942 | 0.8 -> 0.9 | 7.2 -> 7.0 |
| 2xLongbow L-vs-8xStinger | 20 | 100% -> 95% | 98% -> 100% | 1% -> 0% | 8990 -> 3103 | 0.9 -> 1.0 | 7.0 -> 7.0 |
| 2xLongbow S-vs-8xStinger | 20 | 90% -> 90% | 72% -> 95% | 22% -> 4% | 9723 -> 1862 | 1.6 -> 2.9 | 6.3 -> 4.8 |
| 2xMantis-vs-8xStinger | 20 | 85% -> 90% | 57% -> 80% | 33% -> 15% | 14890 -> 7158 | 1.2 -> 1.8 | 6.5 -> 6.2 |
| 2xWasp-vs-8xStinger | 20 | 95% -> 95% | 60% -> 62% | 37% -> 36% | 12814 -> 12359 | 0.5 -> 0.9 | 7.5 -> 7.0 |
| 3xGnat-vs-8xStinger | 20 | 90% -> 90% | 67% -> 65% | 33% -> 35% | 7854 -> 8216 | 0.1 -> 0.1 | 7.8 -> 7.8 |
| Aegis-vs-4xStinger | 20 | 50% -> 65% | 50% -> 65% | 49% -> 32% | 9070 -> 5918 | 0.7 -> 0.9 | 3.3 -> 3.1 |
| Aegis-vs-8xStinger | 20 | 20% -> 60% | 20% -> 65% | 78% -> 32% | 14422 -> 5964 | 0.8 -> 1.5 | 7.2 -> 6.2 |
| Basilisk-vs-4xStinger | 20 | 95% -> 95% | 100% -> 100% | 1% -> 0% | 6346 -> 398 | 1.6 -> 2.0 | 2.3 -> 1.9 |
| Basilisk-vs-8xStinger | 20 | 95% -> 90% | 100% -> 100% | 2% -> 0% | 7672 -> 1596 | 2.5 -> 3.3 | 5.5 -> 4.5 |
| Gnat-vs-4xStinger | 20 | 0% -> 0% | 0% -> 0% | 100% -> 100% | 7844 -> 7844 | 0.0 -> 0.0 | 4.0 -> 4.0 |
| Gnat-vs-8xStinger | 20 | 0% -> 0% | 0% -> 0% | 100% -> 100% | 7844 -> 7844 | 0.1 -> 0.1 | 7.9 -> 8.0 |
| Harrier-vs-4xStinger | 20 | 65% -> 60% | 65% -> 70% | 34% -> 26% | 6049 -> 4693 | 0.8 -> 1.1 | 3.1 -> 2.9 |
| Harrier-vs-8xStinger | 20 | 40% -> 60% | 40% -> 60% | 57% -> 39% | 10048 -> 6830 | 1.0 -> 0.9 | 6.8 -> 7.0 |
| Hornet-vs-4xStinger | 20 | 50% -> 70% | 50% -> 80% | 47% -> 16% | 8063 -> 2896 | 0.7 -> 0.9 | 3.2 -> 3.0 |
| Hornet-vs-8xStinger | 20 | 45% -> 65% | 50% -> 75% | 47% -> 20% | 8000 -> 3409 | 1.0 -> 1.3 | 6.7 -> 6.5 |
| Lancer-vs-4xStinger | 20 | 40% -> 85% | 45% -> 85% | 50% -> 17% | 8667 -> 2907 | 0.8 -> 1.0 | 3.2 -> 3.0 |
| Lancer-vs-8xStinger | 20 | 50% -> 50% | 50% -> 55% | 49% -> 42% | 8400 -> 7268 | 1.2 -> 1.1 | 6.8 -> 6.8 |
| Longbow L-vs-4xStinger | 20 | 100% -> 95% | 100% -> 100% | 1% -> 1% | 3775 -> 3756 | 0.6 -> 0.7 | 3.5 -> 3.3 |
| Longbow L-vs-8xStinger | 20 | 100% -> 85% | 100% -> 100% | 0% -> 0% | 2285 -> 2027 | 0.7 -> 0.9 | 7.3 -> 6.9 |
| Longbow S-vs-4xStinger | 20 | 55% -> 80% | 55% -> 80% | 33% -> 7% | 7350 -> 1822 | 1.0 -> 1.2 | 3.0 -> 2.6 |
| Longbow S-vs-8xStinger | 20 | 50% -> 65% | 55% -> 75% | 34% -> 14% | 7898 -> 3371 | 1.1 -> 2.5 | 6.6 -> 5.3 |
| Mantis-vs-4xStinger | 20 | 35% -> 75% | 35% -> 75% | 55% -> 22% | 12456 -> 5074 | 0.8 -> 1.2 | 3.1 -> 2.8 |
| Mantis-vs-8xStinger | 20 | 10% -> 50% | 10% -> 55% | 81% -> 31% | 18097 -> 7565 | 0.9 -> 1.4 | 6.9 -> 6.3 |
| Wasp-vs-4xStinger | 20 | 20% -> 20% | 20% -> 20% | 74% -> 75% | 12681 -> 12748 | 0.6 -> 0.6 | 3.4 -> 3.4 |
| Wasp-vs-8xStinger | 20 | 25% -> 25% | 25% -> 25% | 66% -> 65% | 11310 -> 11190 | 0.8 -> 0.8 | 6.5 -> 6.6 |
| **all** | 620 | **64% -> 71%** | **57% -> 70%** | **40% -> 27%** | **9158 -> 5373** | **1.0 -> 1.2** | **5.7 -> 5.3** |

("win" drops in some 2-drone fights because a dodged Stinger that loses its target coasts away alive until the 300 s cap. The Gnat rows do not
move: a Gnat has no AI blocks and sees a Stinger at 45 m. The Wasp barely moves: its rockets are too slow for 100 m/s targets and most of its
losses are late detections.)

Development history (package v2.1 scripts, 40 seeds, 1,240 runs; survival / damage taken per fight), showing what each part contributed:

| version | surv | dmg taken |
|---|---|---|
| package v2.1 (baseline) | 56% | 9,225 |
| + never a wreck (exemption only, no priority / rays / share / evasion; short warning horizon) | 59% | 8,623 |
| + priority, rays, sharing | 61% | 8,126 |
| + back away, break sideways | 63% | 6,974 |
| + spent wrecks dropped | 64% | 6,867 |
| + exemption out to 60 s, no thrust requirement, coasting-only spent rule (final) | **71%** | **5,353** |

Regression on ordinary fights (no kamikazes), package v2.3 vs this change, 1v1 matrix with 10 seeds (910 runs, same seeds):

| model | 1v1 score old -> new (n) |
|---|---|
| Aegis | 53% -> 55% (130) |
| Basilisk | 49% -> 50% (130) |
| Gnat | 41% -> 41% (130) |
| Harrier | 54% -> 52% (130) |
| Hornet | 54% -> 51% (130) |
| Lancer | 45% -> 48% (130) |
| Longbow L | 55% -> 53% (130) |
| Longbow S | 64% -> 63% (130) |
| Mantis | 56% -> 54% (130) |
| Midge | 52% -> 52% (130) |
| Stinger | 37% -> 37% (130) |
| Wasp | 46% -> 47% (130) |
| Watcher | 45% -> 45% (130) |

Score: win 1, draw / timeout 0.5, from the model's side (n = 130 per model, so a few points either way is seed noise). Side A's mean score
0.525 -> 0.523. During development (package v2.1 base) a 20-seed 1v1 matrix (1,820 runs) and the mixed-team set (620
runs) were also neutral (0.528 -> 0.540, 0.602 -> 0.614). Script CPU per tick in the sim is unchanged (about 17-29 us either way, measured
alone; the module adds a few vector operations per contact and hive mate, 6 times a second).

Re-checked after rebasing onto main's AIM / breach work-in-progress (commit bc4de5c): kamikaze 5 seeds (155 runs) survival 52% -> 70%,
damage taken -46%; 1v1 3 seeds (273 runs) side A's mean score 0.586 -> 0.577 (noise); no script exceptions.

Reproduce:
```
bash sim/build.sh && python3 -I tools/build_all.py --only Aegis Basilisk Gnat Harrier Hornet Lancer "Longbow L" "Longbow S" Mantis Wasp Watcher
mono out/sim/SimHost.exe run --kind kamikaze --seeds 20 --out out/sim/results/kz
python3 -I tools/kz_report.py OLD/runs.jsonl out/sim/results/kz/runs.jsonl
```
Nothing here has been run in the real game.

## 6. Adding the block to a script (checklist)

1. `python3 -I tools/kz_sync.py --apply` inserts the block before `public Program()` (the script needs `Track` with Id, Pos, Vel, AccMag,
   Radius, Spin, Seen, Src, Type, P(), V(); `Mate` with Pos, Vel, Seen; `_contacts`, `_mates`, `_tgt`, `_now`, `_rc`, `Tag`, `CamFor()`,
   `Valid()`, `AddContact()`, `FleetSend()`).
2. `KzLoad();` after every `FleetLoad(true);` (constructor and `reload`).
3. `KzAssess();` as the first line of the target-selection pass (`PruneAndSelect()` / Gnat `Housekeeping()`).
4. `score += HiveScore(t) + KzScore(t);` and `&& !KzLive(t.Id)` on the GUARD/FORM radius skip.
5. In `Disabled()`, after the `DeadAt` line: `if (KzLive(t.Id)) { t.SpinSince = -1; return false; }` then `if (KzSpent(t)) return true;`.
6. `want = KzWant(want, me, cap);` right before the speed-cap clamp in `Control()`.
7. `_sb.Append(KzStatus());` after `_sb.Append(HiveStatus());`.
8. Point-and-burn scripts (Gnat): wrap the velocity commands of the attack run and of the other run states with `KzWant(...)`, and skip
   the run-in against a kamikaze (`if (_run == Run.SETUP && KzLive(_tgt.Id)) _run = Run.RUN;`).
