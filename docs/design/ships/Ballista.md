# Ballista (railgun drone) with probe foundry, Peeper probe, Spotter

Key `ship_ballista`. Modules `tools/designs/ballista.py` (`MODELS = {"Ballista": fn(lite)}`), `tools/designs/peeper.py` (the probe), printer `Probe Foundry` in `tools/designs/printers.py`.
Scripts: `ShrikeAI.cs` (Helm, +1 hook), `SpotterAI.cs` (new), `PeeperAI.cs` (new), `HiveMotherAI.cs` (the foundry, unchanged). Tests: `tools/tests/test_ballista.py`.
Evidence labels as in the other design notes: [data] repo game data, [calc] computed here, [unverified] never run in the game.

## 1. The brief and the answer

Owner's brief: a drone with loads of large railguns, highly manoeuvrable, no armour, stays out of range and hits hard; at most 10,000 PCU. Then: a small drone-printer bay that sends a tiny probe
to fly close enough for the AI targeting system to find the critical systems and report them back, sent automatically as required; the probe cycles power / weapons / propulsion, the drone
remembers where every targetable block is, power first because it is railguns.

| | Ballista (Full) |
|---|---|
| railguns | 27 `LargeRailgun`, fixed forward, 9 wide x 3 high, 6 slugs each pre-loaded, capacitors full; salvo = 27 x 50,000 slug pool = 1.35 M [data] |
| frame | 52.5 x 45 x 37.5 m, 168 blocks, only the breech wall (54 light-armour cubes) and a mast are armour |
| thrust | 22 `LargeBlockLargeHydrogenThrust` on six tanks (each thruster touches its tank: no conveyor net). Forward / back 36 MN, other four axes 21.6 MN (section 9) |
| mass | 1,081 t blocks, 1,095 t loaded; with foundry and one probe 1,113.5 t |
| acceleration (loaded) | 32.9 m/s2 forward and back, 19.7 up / down / left / right [calc] |
| turning | 13 `LargeBlockGyro` = 437 MN m; 3.8 / 3.0 / 3.4 rad/s2 pitch / yaw / roll [calc, point-mass inertia] (was 12 gyros, 5.0 / 3.1 / 4.6 before section 9) |
| power | 3 large reactors = 900 MW, 13 large batteries = 156 MW / 39 MWh: **1,056 MW against the 1,026 MW the 27 rails draw to recharge together** (27 x 38 MW). A full re-arm is gun-bound (59 s per gun), no longer supply-bound (57 s) [calc]. Section 9 |
| PCU | ship 5,358; foundry about 2,860; one live probe about 1,080 with its projection. **About 8,200 idle, 9,300 with a probe alive** [calc, sum of block PCU]; the test (`test_ballista.py`) adds 400 for the Full foundry and passes at 9,990 of 10,000 |

Before section 9, PCU was not what limited the design (mass, power and the volume of the gun block were); with the extra reactors it is close (10 PCU of the 10,000 left, which is why the gyros stopped at 13 and the belly tank has no brake). 10,000 PCU could hold about 60 railguns, but they would weigh 870 t on their own and re-arm for ten minutes.
`COLS` / `ROWS` in `ballista.py` scale the battery (the tests pin 27).

## 2. Shape

Cells of 2.5 m, x right, y up, z back, bow at -z. Rails x -4..4, y 0..5, z 0..7, muzzles at z = 0, breech wall z = 8. Four hydrogen tanks (3x3x3) on the flanks (x +-5..7, y 0..2 and 3..5,
z 5..7) each carry a large thruster on every free face: out, forward (brake), aft (main), up or down. Core behind the wall (z 9..11): reactor, 13 batteries on the y = 3 deck, 13 gyros (the 2 added reactors, 2 tanks and 6 thrusters sit on the top and bottom decks outside the rails: section 9),
the Helm and Spotter PBs and the Remote Control (y = 4 deck). Mast on the wall: 5 cameras, antenna, sensor (Full). Rear face z = 12: two large `Connector` blocks (`[SH] Foundry Connector A/B`).

## 3. Control

* **Helm** `[Helm][SH] Ballista Helm` runs `ShrikeAI.cs` with the Warden's recipe: weapon battery `[B1]` = the 27 rails (RAIL class, 2,000 m), `Standoff By Weapons=false` holds `Standoff Range=1600` (80 % of the 2,000 m range; with it true, `BatStand` weighs hit chance against a threat step at 1,000 m and parks the ship at exactly 1,000 m),
  `Combat Speed=100`, jinking on. **`AI Block Search Assist=true`** (unlike the Warden): the ship has no turrets, and its cameras alone cannot find anything (6 cameras bank ~6 rays/s of 1.8 km; a 5 degree wide ship is hit by
  about one ray in a thousand, so in the sim it never saw a Pirate that shot it from 2 km down to 500 m). The AI Offensive block's 2.5 km search points the nose, the cameras confirm it. **`[Battery] Salvo=ON`**: all 27 rails fire in
  the same tick (see section 3a). Against large grids of radius >= 25 m the Helm's AUTO aim is BREACH (hold one hull point and drill it); that is what the Spotter feed steers. IGC Tag `BALLISTA`.
### 3a. Salvo, AI assist, and what was changed after the first game test (post-v2.7)

Owner's report: the Ballista did not notice enemies until they were well inside turret range, fired one railgun every ~8 s instead of the volley, and ran off the moment it was pasted.
* **Detection** [sim, stand-in AI]: before, `Assist=false`: 0 targets and 0 shots in 100 s in 4 of 4 seeds (Pirate at 2.0-2.7 km, 30-60 k damage taken). After, `Assist=true`: target held at 1.9-2.4 km after 3-18 s, one 27-gun salvo, 14-19 hits, Pirate dead in
  17-31 s, no damage taken (4 seeds). Idle with the AI assist on the Ballista stays within ~30 m of the paste point (sim). The Warden recipe switched the assist off because an idle warship wanders while the AI block searches; that is a
  stand-in observation, not a game one. If the real AI Flight block flies the Ballista off, send `ai off` to the Helm (the ship then finds targets only by camera).
* **Salvo** (`[Battery] Salvo (AUTO/ON/OFF)`, default OFF so the other ships are unchanged; the Ballista writes ON): the old single-shot logic fired one round-robin gun per slot (Reload x 1.1 / guns + 0.5 s), never the battery. Now every working gun
  gets `ShootOnce` in one tick; after 1 s the guns whose magazine shrank are counted: 70 % or more fired = next salvo after Reload x 1.1 + 0.5 s (~66 s), fewer = the rest are still charging (retry in 4 s), none = retry in 2 s. Sim: 27 rails in one
  tick. In the game the capacitor recharge is power-bound (about 100 s for 27 rails on the Ballista's 360 MW + batteries), so the second salvo may be partial.
* **Run-off at paste** [unverified, not reproduced in the sim]: the AI gate (`AiGateFind`) switched "AI Behavior" on on every [AI] block whenever the ship had any, even with `AI Block Search Assist=false`. If the game honours that switch on its own
  (independent of the block's On/Off), the AI Flight block flies the ship away from the first tick. The Helm now leaves the AI blocks alone unless the assist is on (`if (AiAssist) AiGateFind(_ai)`; `ai on|off` re-runs it). With the assist on again the
  AI blocks do fly the ship while it searches; if the Ballista still leaves its paste point, the next suspects are an inbound `FLEET_HOME` (set `[Fleet] Accept Home=false` in the Helm's Custom Data) and the sensor/AI blocks' own default settings.

* **Wing collisions** (2026-10-14): three Ballistas pasted together flew into one spot and collided. The Helm now steers round every other grid with the `FRIENDLY AVOIDANCE` block, hull radii included, and the AI Flight blocks yield to it: `docs/friendly-avoidance.md`, sim `wing-test`.

### 3b. Blind at 900 m, stationary Tech Hunter (game screenshot: `TGT none`, `AI: own search`, Ins 356/50000)

* Not the instruction budget (356 of 50,000). The AI block had not yet shown that it flies the ship, so the AI gate time-shared: 6 s AI, 8 s own camera search.
* **Own camera search is nearly blind for a warship** [sim, AI assist off, Tech Hunter held at 900 m, ship pointing at it]: no target for 50 s. `AutoScanStep` casts 1,800 m rays through all six cameras (about 6.5 rays/s, 264 in 40 s), each in a 35 degree cone round its own axis, so only about 1 ray/s goes forward; the Rear camera mostly hits the docked foundry. Covering the sphere at about 6 degrees spacing takes about 3 minutes of ray budget.
* **The AI Offensive block is the detector** (2,500 m, all round) but its default search interval is 30 s [wiki], longer than the 6 s window. Changes (unverified in game): the Helm sets `UpdateTargetInterval` to 5 s on its AI blocks (`[Shrike] AI Search Interval (s)=5`, 0 = leave alone) and the Ballista's Custom Data gives the AI 20 s windows (`[AI Gate] AI Window (s)=20`, `Script Search Window (s)=5`).
* If it is still blind in game: send `ai off` is not the fix (it removes the best detector); instead lock it by hand (cockpit/Remote Control lock + `assist`) or tell me what the AI Offensive block's terminal shows (found enemy, interval).

### 3d. Firing while still closing (post-v2.7, not run in sim or game)

Owner report: the Ballista chases a target that keeps its range and does not fire on the way in. The Helm never gated fire on standoff (the rails shoot out to `MaxFireRange` = 2,000 m; the standoff only steers the thrust), so the suspect is the aim window: with a hull-point aim (BREACH) `BatWin` shrinks to atan(3 m / range) with a floor of 0.3 degrees, which a heavy ship that is still turning and thrusting rarely holds at 1.8 km. New key `[Battery] Loose Fire Beyond Standoff` (default true): beyond `Standoff + 100` m the window is the whole silhouette (radius x 0.6), inside it the tight hull-point window applies again. Beyond 2,000 m nothing can fire (slug range). If it still holds fire in game, send the Helm screen `_err` / aim-error line.

### 3c. Standoff vs a 100 m/s charge (post-v2.7, sim)

Tech Hunter flown straight at the Ballista at 100 m/s (`--pirate-rush true --pirate-cruise-speed 100`) from 3 km, 8 seeds per standoff, `tools/ballista_charge_sweep.py`. The sim Helm only notices the target at about 1.8 km, then flees at the 100 m/s cap (same as the charger), so the gap stops shrinking.

| Standoff Range | closest approach avg / worst | damage taken avg | runs hit |
|---|---|---|---|
| 800-1000 (what `Standoff By Weapons=true` gave) | 312 / 20 m | 1.10 M | 8/8 |
| 1200 | 421 / 14 m | 0.61 M | 8/8 |
| 1400 | 903 / 728 m | 1.5 k | 1/8 |
| 1500 | 996 / 620 m | 47 k | 1/8 |
| **1600** (default) | 1,120 / 1,026 m | 0 | 0/8 |
| 1700 (the cap) | 1,160 / 969 m | 0 | 0/8 |

Caveats [sim, stand-in AI]: one notice distance only (a later notice shrinks the margin); the Tech Hunter's turrets never fired in the 1,600+ runs, so "hit" is damage taken; never run in the game.

### 3d. Idle spin (game: five pasted Ballistas spun round each other and did not see a Tech Hunter parked 2.2 km away)

* In HUNT with no target the Helm yaws the whole ship round as a camera "radar sweep" (`SweepDir`, half the turn rate). On a 900 t ship that is a continuous spin: [sim, 5 Ballistas, Tech Hunter held at 2.2 km] mean 0.7 rad/s in the first 30 s, first target at 37 s.
* `Auto Camera Scan=false` in the Ballista's Helm Custom Data (the AI Offensive block is its detector; the 1.8 km camera rays cover too little of the sky to help): mean 0.2 rad/s, first target at 8 s, all five fire [sim, 1 seed each, stand-in AI].
* A cockpit added by hand is not seen as a seat unless its name starts with `[SH]` (the Helm only uses blocks with the `[Link] Block Prefix`), so it does not switch the Helm to MANUAL. Rename it `[SH] Cockpit` for hand flying.
* [unverified] why the target was seen the moment the cockpit was used in the game; the Helm status now shows `1st contact <m> v <speed> <source>`.

### 3e. Kamikaze defence and wing spacing (post-v2.7, sim + game report)

* **Four vanilla `LargeGatlingTurret`s** (`[SH] [PD] Gatling Turret n`, loaded, 800 m): top and belly, port and starboard, on the breech wall beside the mast (`PD_TURRETS`). +902 PCU (ship 5,451 with the thrust PB; about 8,300 with the foundry, about 9,300 with a probe alive; limit 10,000). The side cameras moved up the mast (y 9) and the antenna to the belly (the turrets took their space). The turrets are also designators for the Helm.
* **Sim A/B** [stand-in AI, Hornet Kamikaze = a Hornet flown straight at the Ballista at 100 m/s from 3 km by the Pirate brain, 4 seeds]: without turrets the Ballista fired once and was rammed (closest 14 m, 5.1 k damage; four chargers: 26 k damage). With turrets the single charger died at 576-595 m (0 damage); four chargers: 5.2 k damage, 11 of 16 fighters still alive but held off. The in-sim Stinger swarm (`--kind kamikaze --kamikaze-extra Ballista`) is not a usable test right now: the Stingers crawl at 6-8 m/s and never dive.
* **Single-railgun "pokes"** (new `[Battery] Point Defence (OFF/B1..B4)`, `Point Defence Max Radius (m)`, `Point Defence Exclusive`; the battery shoots only small tracks or incoming kamikazes, one gun at a time, and the others leave those tracks alone): exclusive was worse than the full salvo (2 of 4 chargers survived to 123 m vs 0 of 4), additive equal. A rail recharges in about 60 s, so six pokes cannot answer a swarm. Default OFF; no rail is tagged [B2] (`PD_RAILS` empty).
* **Rails stay off incoming kamikazes** (`[Battery] Kamikaze Rail Hold Range (m)=250`, 0 = off, script default 0): railgun / shell batteries leave a classified incoming kamikaze alone until it is closer than that (the turrets answer it), and `BatKz` ranks such a track behind every other track (its kamikaze priority bonus is cancelled), so the tracking stays free for the large ship that launched them: a large ship in range becomes the target and gets the salvo; with nothing else about, the kamikaze is still the target and the turrets get it. [sim, Tech Hunter + 3 Hornet chargers with `Max Kamikaze Radius=9` so the stand-in is classified, 4 seeds]: the behaviour changes as intended but the stand-in shows no clear gain (Tech Hunter damage 50-54 k either way); the Helm test `selftest m3` passes. Helm budget raised 86,000 to 90,000 characters (`SCRIPT_LIMITS`): the Ballista has a spare PB for the next module that moves off the Helm.
* **Wing spacing** (game: four Ballistas pasted together did not lock on while the pilot's ship stood between them and the target, locked on within 10 s once he was out of the line, and did not spread): `Separation()` only pushes mates apart inside 70 m centre to centre (a hull is 52 m long) and the idle ship's home point pulls it back to its paste spot. Now `[Shrike] Wingmate Separation Range (m)=300`, `Wingmate Separation Speed (m/s)=12`, `Wingmate Fire Clearance (m)=45` (was a constant 14 m; a 27-gun frontage is 22 m wide), and an idle ship moves its home point away from its mates, so the wing spreads. Sim, 4 Ballistas, Tech Hunter held at 2.2 km, 3 seeds: all four lock on at 8-9 s and fire in 3 of 3; with the old values 2 of 3 never locked on in 120 s and one at 57 s. [unverified in the game] whether the pilot's own ship in the line blocks the AI block and the rays (a friendly in the way returns no enemy), and whether `Holding fire: wingmate in line` was shown.

* **Staff PB** (Helm split, `docs/design/helm_split.md`): the third PB is `[Staff][SH] Ballista Staff` (`StaffAI.cs`, `[Staff] Mothership=false`: the Ballista carries no drones, so it broadcasts no HOME). It carries the fleet channel and the other fleets' target feeds (to the Helm as `.TGT` / `.S2H`) and the damage scan (`.DMG`), and gives the Ballista the Helm-silent watchdog (releases every override 1.5 s after the Helm stops). Without it the Helm still flies and fights but has no fleet channel and no damage sensing (a screen note for that is not implemented).
* **Spotter** `[Spotter][SH] Ballista Spotter` runs `SpotterAI.cs` (section 5).
* No cockpit: a drone. Hand flying needs a seat added by hand (the Helm switches to MANUAL when somebody sits in a flight seat).

### 3c. Spare PB (headroom)
Three Programmable Blocks: Helm (`ShrikeAI.cs`), Spotter (`SpotterAI.cs`) and `[SH] Ballista Spare PB`, empty, at (1, 4, 11) on the battery deck beside the Spotter (mirror cell of nothing: the deck is the same distance from the breech wall as the other two, seated on a battery like them). Use it for a module that has to leave the Helm (it is 82,328 characters minified with the thrust-vector link): link it over IGC `TransmissionDistance.CurrentConstruct`, keep tracking/aim/fire in the Helm. Its name must not start with `[Staff]` and must not contain `Hive Mother PB`.

## 4. Peeper (the probe)

3.5 x 5 x 5.5 m small-grid, 62 blocks, 4.6 t, 12 ion thrusters (6.3 m/s2 on every axis), 4 batteries, gyro, 3 cameras, antenna, **one `SmallGatlingTurret` with no ammunition** (a 5x5x5 block: the
smallest turret a small grid has) as the target designator, merge latch below the tail. `Lite` has no turret (unseen shape in the Lite rules) and finds nothing.

`PeeperAI.cs` flight: waits while a `[Bay` block shares its grid (fused to the printer); clears the printer for 3 s; then orbits the target the Ballista is shooting at (`<Host>_TGT`), on a sphere of
target radius + 35 m (min 60 m), tilt sweeping so every side is covered, nose on the target. No target for 20 s: parks. Heartbeat `PEEPER_SWARM` once a second (the foundry counts it).

Vitals: turret targeting group cycle **PowerSystems 6 s, Weapons 4 s, PowerSystems 6 s, Propulsion 4 s** (power twice per round). After a 1.5 s settle, when the turret `HasTarget` / `IsAimed` on the target,
the block position is `GetTargetedEntity().HitPosition` when the game gives one, else the aim line (azimuth / elevation); a camera ray through that point returns the hull hit, which is converted to the
**target's own frame** (`Orientation` transposed) so it stays valid while the target moves and turns. Report: `BALLISTA_CRIT` = (target id, group, position, ray direction, probe id), at most 2.5 per second.

## 5. Spotter (memory, feed, bay)

* **Memory** per target: reports of one group within 6 m merge into one vital (position averaged, hit count up). A vital nobody re-sees for 90 s while the probe keeps reporting is forgotten. Saved in `Storage`
  (survives recompile, lost probes and a Spotter restart). Four targets kept. `dump` shows it on the screen.
* **Feed** to the Helm as `BALLISTA_AIM` = (target id, position in the target frame) every 4 s or on change. Choice: `Priority` (default power, weapons, propulsion), then most hits, then freshest. A vital
  fed for 45 s without disappearing is marked tried and the next is used; when all are tried the marks reset. The Helm (ShrikeAI hook in `ReadIGC`) makes it the BREACH point and drills it **along the real
  line of fire** (the direction is computed in the Helm from its own position, not taken from the probe). A point the Helm already gave up on (its 30 s stall rule, `_bBad`) is not taken again.
* **Bay**: finds the foundry's `Hive Mother PB`, runs `max 1` + `start` when the Helm has a target of radius >= 25 m and no probe is alive, `stop` when the target is gone, waits 15 s after a lost probe, and
  presses Connect on `Foundry Connector` blocks whenever they are connectable (a safety net: the blueprint is written with the pairs locked, like the Warden pod). The foundry has `Auto Start=false`, so nothing prints before the first target.

## 6. Probe foundry

`Probe Foundry` (also a stand-alone printer in the printer zips): the repo's printer machinery (`printer_lib.py`, `HiveMotherAI.cs`) with one bay printing a Peeper: 5x5x5 equipment pod, 12 small welders,
projector with the probe pre-loaded, merge block; 185 blocks, 12.1 t, 6 x 10.5 x 10.5 m, no thrusters. Added: a flange and two `ConnectorMedium` on its underside, 5 cells (one large cell) apart, so it docks on the
Ballista's two rear connectors the way the Cradle Pod docks in the Warden's hangar (`pods.dock_pod`; the pair meets to 2 mm). The roll is chosen so the bay's exit leaves upward or straight aft. Checks
(`ballista.check_foundry`): 0.3 m clear of every ship block, outside the exhaust and muzzle zones, and the probe's exit lane (its box swept 8 m) is empty.

## 7. Known risks (read before trusting any of it)

1. **Printed drones may not separate from the printer** (`docs/audit/printers.md` printers-01, the repo's biggest open risk). Welded blocks join the projector's grid; switching the merge blocks off may not
   split it. If the probe stays fused, nothing flies off: the foundry reports FUSED and the Peeper stays docked. The audit's fix (a sprue and a grinder) is **not** built. Tests P1-P3 in the audit decide.
2. **A turret without ammunition may not acquire or aim**, and the targeting groups may not pick subsystems of an enemy grid from outside. `SeekerProbe.cs` answers this in 5 minutes (put it on any ship with a turret).
3. Components: creative mode needs none; in survival the foundry's welders need the probe's components (about 1 t of steel plate, 4.6 t of parts) fed by hand.
4. Not run anywhere: the sim has no probe, foundry or Spotter models. Compile-checked only (`tools/compile_check.sh`, stand-in API).
5. Helm size: the hook raised ShrikeAI to 80,547 characters, the salvo to ~81,200, the friendly-avoidance steering to ~83,800, with the thrust-vector link 84,958 (build limit now 86,000; the Programmable Block takes 100,000).
6. The foundry's 12 welders cost 150 PCU each (1,800 of its ~2,800); fewer welders would save PCU but the bay's reach guarantees (`reach.py`) assume them.
7. Lite Ballista prints Lite probes: they cannot identify anything. Use Full.

## 8. In-game tests, in order

1. `SeekerProbe.cs` on any ship with a turret: does the turret report a subsystem position?  (risk 2)
2. Paste Ballista; the Spotter screen shows `Conn LL` after a few seconds (locked). Raise the world PCU limit to 10,000 if needed.
3. Hold a target with the Helm (a grid with power blocks): Spotter says `printing`, the foundry builds, the probe leaves (risk 1), `Probes 1/1`.
4. Spotter screen: vitals count by group rising; `Feed power`; Helm status line shows ` hole N m` and the railgun salvo lands on the reported block.
5. Kill the probe: a replacement is printed after 15 s. Remove the target: the foundry stops.

Commands: Spotter `reset | dump | bay on|off | feed on|off | reload`; Peeper `scout | park | reload`; Helm unchanged.

## 9. Power for a full recharge, and the thrust that pays for it (2026-10-14)

Question: can the Ballista charge all 27 railguns at once? Before: **no**. Each `LargeRailgun` recharges at 38 MW (RechargeDraw, 0.5 MWh capacitor, 0.8 efficiency, so 59 s per gun) [data]: 27 x 38 = **1,026 MW** against 1 large reactor + 4 small (360 MW) + 21 batteries (252 MW) = 612 MW, 60 % of the demand. A re-arm took about 99 s instead of 59 s; with the batteries empty about 175 s. Hydrogen thrusters burn hydrogen, not electricity, and the 12 gyros draw 0.01 MW each, so the rails were the only real load; thrust (power group 80) and gyros (70) are served before the recharge (90), so it is the recharge that starves.

Changes (`ballista.py` `_aux`, `_core`, `_sensing`):
* **Power**: two more large reactors (300 MW each, 300 uranium each) on the top deck, at x -4..-2 and 2..4, z 4..6, seated on the top rails' top patch (z 4.7..7.8). The four small generators go (25 PCU for 15 MW each; a large reactor is 25 PCU for 300 MW and lighter per MW). Reactors 900 MW + 13 batteries 156 MW = 1,056 MW, 103 % of the 1,026 MW peak, which leaves 30 MW for the small loads (under 5 MW). Sustained: the Helm's salvo cycle is about 66 s (Reload x 1.1 + 0.5), 16.9 MWh per cycle = 922 MW average; the reactors alone give 98 % of that and the batteries cover the 22 MW gap (about 1.5 MWh a cycle of 39 MWh).
* **Mass paid for**: +147.6 t of reactors (+0.6 t uranium), +58 t of tanks and thrusters, +1 gyro, less 8 batteries (-31 t) and 4 small generators (-19 t): 924 t to 1,095 t loaded.
* **Thrust** (hydrogen thrusters need a tank in contact, so they come with two new tanks): top tank `Hydrogen Tank Aux Top` at (-4, 6, 1) in front of the port reactor: one upward-exhaust thruster (pushes down) and a brake. Belly tank `Hydrogen Tank Aux Belly` at (-1, -3, 4) under the rails: down (pushes up), left, right and main (no brake: the PCU limit). Net: forward 4 to 5 thrusters, back 4 to 5, each lateral / vertical direction 2 to 3. The old belly mast, camera lane and antenna were in the main thruster's exhaust lane; the belly camera and the antenna now hang from the outer batteries at z = 11 on armour columns (x +3 and -3).
* **Turning**: the reactors raise the moment of inertia by 47 % / 14 % / 49 % (pitch / yaw / roll). Gyros are 50 PCU and 13 t each, and the 10,000 PCU brief leaves 10 PCU, so only one was added (13): yaw 3.1 to 3.0 rad/s2, pitch 5.0 to 3.8, roll 4.6 to 3.4. That is the one mobility figure that went down; acceleration went up everywhere (forward and back 31.2 to 32.9, lateral / vertical 15.6 to 19.7). A gyro seats only by its bottom face, so extra ones can only sit on the breech wall's back face (x +-4, y 0..3, z 9 are the spare cells).
* **Checks** (`test_ballista.py`): peak supply >= 1.03 x 1,026 MW, reactors alone >= 85 % of it, supply-bound re-arm no longer than the per-gun 59 s, acceleration not below the old 31.2 / 15.6, no block inside an exhaust or muzzle lane, PCU under the limit. The full build (`build_all.py --no-zip --only Ballista`) validates the grid in one piece.

[unverified] in the game: the reactors' seat on the rails (the tool checks the mount patches, the game decides), the thrusters' torque (the new thrusters sit 4 to 5 cells above and below the centre of mass, the old ones are symmetric about it; the gyros have 437 MN m against about 160 MN m for all new thrusters at full fire, so they should hold), and that the game really feeds the recharge in the order assumed (script-fix-notes 1151). No sim run: the sim has no Mono in this session and its power model already treats the rails as above.
