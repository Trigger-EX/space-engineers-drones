# Drones vs a big warship: the Tech Hunter study (v2.6)

Question from the user (2026-10-13): against large battleships like their *Tech Hunter*, the drones "can't figure out where vital
components are, and just shoot the hull uselessly. What can be done?" Then: test it in the simulator with mixed fleets of Hornet,
Longbow L, Longbow S, Gnat and Basilisk up to 10 drones, only as many runs as needed to choose the path, and give an in-game test
version of option 4 (let a turret find the vitals).

## 1. The ship

`pc/enemies/Tech Hunter/bp.sbc`, main grid only (13,666 blocks, 215 x 97 x 72 m; the rotor turret pods are dropped). Bow is grid -X,
top deck +Z (cockpit orientation).

| Part | Count | Where / cover (cheapest side) |
|---|--:|---|
| Large reactor | 1 | mid-ship near the keel; 2 conveyors from the belly |
| Batteries | 34 | mid-ship, low; mostly 1 light armour block from the belly |
| Hydrogen engines | 15 | stern; 9 fully exposed |
| Jump drives | 10 | 6 exposed |
| Thrusters | 114 | stern face nozzles exposed |
| Turrets | 76 | 36 assault cannon (1400 m), 16 artillery (2000 m), 24 gatling (800 m); all exposed, 38 on top, 38 below |
| Decoys | 14 | under one heavy slope on the top deck |
| Armour | 11,843 | 31 % heavy, concentrated on the bow |

From the belly the median armour over a power block is about 6,200 HP (2.5 light blocks). The vitals are shallow; the drones
were spreading their fire so no block ever died. Survey scripts: the analysis above was made with a cell-occupancy ray walk over the
blueprint (each column from each face, HP of the blocks before the first vital block).

## 2. What was built

- **BREACH aim** (`tools/aim_block.cs`, synced into all 9 armed drone scripts by `tools/aim_sync.py`): against a large grid of
  radius >= 25 m, AUTO now holds ONE hull point, the known hull point nearest the target's centre (mid-deck / mid-belly on a long
  ship, where the armour over the core is thinnest), and keeps every round on it. Probe rays show how deep the hole is; the drone
  then aims down the hole toward the centre. No progress for 30 s, or two probe rays in a row that find nothing: the next point.
  `aim breach` forces it, `breach off` restores the old AUTO (ENGINES / SPREAD). Custom Data: `Breach Large Ships (AUTO)`,
  `Breach Min Target Radius (m)`, `Breach Stall (s)`. The status line shows `Aim: BREACH (... hull pts) hole N m`.
- **SEEKER** (option 4, experimental): a turret on the drone with `[Seeker]` in its name is set to the targeting group
  `Seeker Group` (default `PowerSystems`). The game aims such a turret at a block of that group. Twice a second the script reads the
  turret's Azimuth/Elevation, removes the lead (or uses the targeted entity's HitPosition if the game supplies one), raycasts that
  line with its cameras, and makes the hull point over the subsystem the breach point, drilled along the turret's line.
  `seeker on|off`, Custom Data `Seeker Turret Guides Aim`. Status line `Seek: ...` says what the turret reports.
  Test ships: **Longbow L Seeker** (artillery turret on the railgun's back, 2 km like the railgun) and **Basilisk Seeker** (its roof
  gatling turret renamed). Standalone probe for any ship: `scripts/SeekerProbe.cs`.
- **Simulator fixes** needed for a fair fight with a big ship:
  - large turrets are Defense power sinks (`--turrets-need-power`): a ship with no power has silent turrets;
  - only railguns have a capacitor. Artillery and assault-cannon shells are penetrating "slugs" in the sim and had been given a
    railgun capacitor in the Defense power group; recharging 52 of them asked ~870 MW and starved every turret on the ship;
  - turrets honour targeting groups (`--turret-subsystem-targeting`, nearest functional block of the group; a decoy still wins) and
    expose Azimuth / Elevation / Get/SetTargetingGroup to scripts;
  - results carry the enemy's vitals: `power_start/end_mw`, `power_blocks_start/end`, `armed_start/end` (functional AND powered
    weapons), `power_zero_at_s`, `armed_zero_at_s`;
  - `--enemy-fleets FILE|"3xHornet+2xGnat;..."` runs explicit fleets against the `--enemies` ships (`sim/fleets/*.txt`).

## 3. Runs (10 seeds per fleet, 10-minute cap, ship flown like the pirate: closes in, circles at 700 m)

| Set | Fleets | Runs | Scripts |
|---|---|--:|---|
| Matrix, current aim (`breach off`) vs BREACH | 41: each model x3/x6/x10, all pairs 5+5, all triples 4+3+3, all quads 3+3+2+2, all five 2 each (`sim/fleets/tech_hunter.txt`) | 820 | before the v2.3-v2.5 merges |
| Matrix, Basilisk Seeker instead of Basilisk | the 18 fleets with Basilisks | 180 | same |
| Confirmation on the final scripts | 10 fleets (`tech_hunter_confirm.txt`), current vs BREACH | 200 | final |
| Longbow L Seeker, seeker off vs on | 4 fleets (`tech_hunter_ll_seeker.txt`) | 80 | final |

Tables: `docs/tech-hunter/*.md` (per fleet: power blocks / weapons / thrusters / blocks destroyed, % runs the ship was knocked out,
drones left). Raw runs: `docs/tech-hunter/runs/*.runs.jsonl.gz`; `python3 -I tools/th_report.py LABEL=DIR ...` rebuilds the tables.

## 4. Results

**Nothing of 10 drones or fewer knocks the Tech Hunter out in 10 minutes** (0 % in all 1,280 runs). Its 76 turrets decide it:

| Model | median time alive | why |
|---|--:|---|
| Gnat | 44 s | inside the reach of every turret type |
| Hornet | 50 s | 350 m standoff is inside every turret's range |
| Basilisk | 67 s | assault cannons (1400 m) and artillery (2000 m) outrange its rockets |
| Longbow S | ~260 s | 1400 m railgun = assault-cannon range; artillery still reaches it |
| Longbow L | 600 s (all fight) | 2000 m railgun = artillery range; it alone lives through the fight |

Least-squares share per drone (whole matrix, power blocks / turrets / blocks destroyed per drone in 10 min):

| Model | current aim | BREACH |
|---|---|---|
| Longbow L | 0.65 / 1.04 / 57 | **1.20** / 0.29 / 61 |
| Longbow S | 0.04 / 0.35 / 26 | **0.21** / 0.09 / 27 |
| Basilisk | 0.03 / 0.18 / 9 | -0.02 / 0.17 / 11 |
| Hornet | 0.00 / 0.02 / 3 | 0.03 / 0.09 / 7 |
| Gnat | -0.05 / 0.02 / -3 | -0.04 / 0.03 / -2 |

Confirmation on the final scripts: Longbow L 0.75 -> 1.16, Longbow S 0.01 -> 0.19 power blocks per drone; best fleet 10 x Longbow L:
8.7 -> 10.1 power blocks, 11.3 -> 13.7 thrusters, 7.8 -> 2.8 turrets in 10 minutes (of 50 power blocks, 114 thrusters, 76 turrets).

- **BREACH works for the guns that can drill** (railguns): power-block kills roughly double, thruster kills rise by about half.
  The price: the old SPREAD / FACING aim hit exposed turrets on the hull, BREACH hits far fewer of them. Drone survival did not
  change either way (the Longbows' survival is decided by range, not by which turrets die).
- **Gatling and rocket drones achieve almost nothing against this ship whatever they aim at**: they die in under 70 s, before
  aiming matters. Mixed fleets are worth exactly their Longbows; every Hornet / Gnat / Basilisk slot is better spent on a Longbow L.
- **SEEKER (option 4) did not beat BREACH in the sim.** Basilisk Seeker: no change (it dies first). Longbow L Seeker, where the
  simulated turret knows every power block exactly: seeker on 1.19 power blocks per drone vs 1.31 with the seeker off (the extra
  artillery turret doubles its total kills either way). Knowing where the power is did not help, because the limit is the armour
  along the line the sniper has, and BREACH already picks the thinnest spot. The in-game question (does the game even point the
  turret there, and through the decoys?) is untested; see 6.

## 5. The path

1. **Ship BREACH on by default** (done in v2.6): railgun drones put roughly twice as much fire into the power and engines. It
   costs nothing for the short-range drones.
2. **Against a turret-heavy capital ship, send Longbow L**, and Longbow S only as support. Hornets, Gnats and Basilisks are wasted
   on it in the open; keep them for small ships and drones, or send them in only after the Longbows have silenced a side.
3. **Kill speed, not knowledge, is the limit now.** 10 Longbow L take about a fifth of the power in 10 minutes. Next candidates
   (not done): a STERN option (the 15 hydrogen engines and 6 jump drives at the stern are exposed), one shared breach point for
   the whole fleet over IGC (so all slugs go down one hole), and a "silence one side" mode that kills the turrets facing the
   drones first, then breaches from that side.
4. **Option 4 stays an in-game experiment** (Longbow L Seeker, Basilisk Seeker, SeekerProbe). If the probe shows the game hands a
   script the block position (`hitpos ... from centre`), the seeker can aim deeper than BREACH and is worth another sim round.

## 6. What the sim cannot tell you

- The ship's own welders (132) are not simulated; if they repair in game, chip damage is worth even less and BREACH more.
- Assault cannon and artillery turrets reload after every shot in the sim (no 2-shot burst): the real turrets fire somewhat faster,
  so the drones' survival times above are optimistic.
- Hydrogen and uranium never run out; the 6 rotor turret pods (24 small gatlings) are not simulated.
- Subsystem targeting in the game: which block a turret picks, whether it needs line of sight, how decoys interfere, and whether
  `GetTargetedEntity().HitPosition` is set are unknown. The sim picks the nearest functional block of the group.
