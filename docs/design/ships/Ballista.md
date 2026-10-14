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
| frame | 52.5 x 30 x 32.5 m, 157 blocks, only the breech wall (54 light-armour cubes) and a mast are armour |
| thrust | 16 `LargeBlockLargeHydrogenThrust` on four 15 ML tanks (each thruster touches its tank: no conveyor net). Forward / back 28.8 MN, other four axes 14.4 MN |
| mass | 902.8 t blocks, 913.2 t loaded; with foundry and one probe 931 t |
| acceleration (loaded) | 31.5 m/s2 fore and aft, 15.8 m/s2 up / down / left / right [calc] |
| turning | 12 `LargeBlockGyro` = 403 MN m; 5.1 / 3.1 / 4.7 rad/s2 pitch / yaw / roll [calc, point-mass inertia], i.e. the 180 deg/s cap in about 1 s |
| power | 1 large reactor + 4 small = 360 MW, 21 large batteries = 252 MW / 63 MWh. A full re-arm of 27 guns (0.5 MWh each at 38 MW) takes about 100 s supply-bound, 59 s is the per-gun floor |
| PCU | ship 4,349; foundry about 2,860; one live probe 916 (+62 projected). **About 7,200 idle, 8,200 with a probe alive** [calc, sum of block PCU] |

PCU is not what limits the design: it is mass, power and the volume of the gun block. 10,000 PCU could hold about 60 railguns, but they would weigh 870 t on their own and re-arm for ten minutes.
`COLS` / `ROWS` in `ballista.py` scale the battery (the tests pin 27).

## 2. Shape

Cells of 2.5 m, x right, y up, z back, bow at -z. Rails x -4..4, y 0..5, z 0..7, muzzles at z = 0, breech wall z = 8. Four hydrogen tanks (3x3x3) on the flanks (x +-5..7, y 0..2 and 3..5,
z 5..7) each carry a large thruster on every free face: out, forward (brake), aft (main), up or down. Core behind the wall (z 9..11): reactor, 21 batteries on the y = 3 deck, 4 generators, 12 gyros,
the Helm and Spotter PBs and the Remote Control (y = 4 deck). Mast on the wall: 5 cameras, antenna, sensor (Full). Rear face z = 12: two large `Connector` blocks (`[SH] Foundry Connector A/B`).

## 3. Control

* **Helm** `[Helm][SH] Ballista Helm` runs `ShrikeAI.cs` with the Warden's recipe: weapon battery `[B1]` = the 27 rails (RAIL class, 2,000 m), `Standoff By Weapons=true` keeps about 85 % of the railgun range,
  `Combat Speed=100`, jinking on. Against large grids of radius >= 25 m the Helm's AUTO aim is BREACH (hold one hull point and drill it); that is what the Spotter feed steers. IGC Tag `BALLISTA`.
* **Spotter** `[Spotter][SH] Ballista Spotter` runs `SpotterAI.cs` (section 5).
* No cockpit: a drone. Hand flying needs a seat added by hand (the Helm switches to MANUAL when somebody sits in a flight seat).

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
5. Helm size: the hook raised ShrikeAI to 80,547 of the Helm's 80,600-character budget (design budget 80,000, hard cap 85,000, the Programmable Block takes 100,000).
6. The foundry's 12 welders cost 150 PCU each (1,800 of its ~2,800); fewer welders would save PCU but the bay's reach guarantees (`reach.py`) assume them.
7. Lite Ballista prints Lite probes: they cannot identify anything. Use Full.

## 8. In-game tests, in order

1. `SeekerProbe.cs` on any ship with a turret: does the turret report a subsystem position?  (risk 2)
2. Paste Ballista; the Spotter screen shows `Conn LL` after a few seconds (locked). Raise the world PCU limit to 10,000 if needed.
3. Hold a target with the Helm (a grid with power blocks): Spotter says `printing`, the foundry builds, the probe leaves (risk 1), `Probes 1/1`.
4. Spotter screen: vitals count by group rising; `Feed power`; Helm status line shows ` hole N m` and the railgun salvo lands on the reported block.
5. Kill the probe: a replacement is printed after 15 s. Remove the target: the foundry stops.

Commands: Spotter `reset | dump | bay on|off | feed on|off | reload`; Peeper `scout | park | reload`; Helm unchanged.
