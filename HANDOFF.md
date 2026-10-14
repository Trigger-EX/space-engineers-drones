# Space Engineers drone project: handoff

You're picking up a long project from a previous session. That session ended because an auto-mode
safety check started blocking every command, triggered by earlier conversation content (not by the work).
Everything below is what you need to continue. Read sections 1–4 before touching anything.

---

## 1. The user and what they want

- Plays **Space Engineers (PC)**, creative world, experimental mode on, tests by pasting blueprints with
  **F10**. Blueprint folder: `%AppData%\SpaceEngineers\Blueprints\local\<Name>\bp.sbc` (+ `thumb.png`).
- Wants combat drones with their **script already inside the programmable block (PB)**, blueprints that
  paste cleanly, a README per package, and versioned zip packages. Likes ambitious scope and autonomy.
- **Open request (their latest, do this):**
  1. Finish all drone models, make **as many printer-ship variants as you can think of**, and make
     **more drone models**.
  2. Also: *"investigate using timer blocks to automate things, weigh if it's better."* (analysis in §8)
  3. Then **run simulated fights between all models, 100 simulations per variation**, including
     **unfair fights (1v2, 1v3, 2v3, etc.)**. The game can't be run, so **build your own simulation
     engine**. (Since replaced: in a Claude session, run only as many sims as the task needs. 100 per
     variation is only for the local PC plans. See CLAUDE.md "Simulations".)
  4. **Audit the results for improvements, implement the combat improvements, re-simulate.**
  5. **Finally, audit all aspects for improvement.** They said to keep going for hours if needed.
- Deliver: zip(s) with `Blueprints/`, `Scripts/`, `README.md`; plus a short summary of sim results.

---

## 2. Status

| Item | State |
|---|---|
| Block definition index (`defs.json`, 1,503 vanilla blocks: id, size, grid, mount points, DLC) | Done |
| Blueprint builder + validator (`tools/sebp.py`): checks IDs, overlaps, real mount-point attachment, one connected piece, exhaust/muzzle/camera clearance | Done (see caveat in §4.3) |
| Redesigned **Hornet, Gnat, Longbow S, Longbow L** (`tools/drones.py`), all pass validation | Done in code, **not yet written out as blueprints** |
| Drone scripts **v1.8** (Hornet, Longbow, Gnat) | Written, **not compiled** (v1.7 compiled clean against the stand-in API) |
| `StingerAI.cs` (kamikaze), `HiveMotherAI.cs` (printer controller) | Written, **never compiled** |
| One pending fix in `HiveMotherAI.cs` | See §6.4 |
| New drones Wasp, Watcher, Midge; Stinger blueprint | Not started (designs in §7) |
| Printer ships | Not started (designs in §7) |
| Simulation engine + fights + audit | Not started (plan in §9) |

**Later work (after this handoff was written):** see `packages/changelog.md` (v2.0 to v2.4) and `docs/RESULTS.md`; v2.4 is the
**kamikaze defence** against incoming swarms of small warhead drones (`docs/kamikaze-defence.md`, shared block `tools/kz_block.cs`
synced by `tools/kz_sync.py`, sim scenario kind `kamikaze`).

**What the user currently has installed:** `Drones_v1.7` (Hornet, Longbow S, Longbow L, Gnat). It has
**floating, unattached blocks**, which the user spotted (e.g. cameras floating on the Hornet).

---

## 3. Ground truth from the user's in-game testing

> **MULTI-PB WORKS (owner-tested in the real game, 2026-10).** Two programmable blocks on one grid linked by IGC (`TransmissionDistance.CurrentConstruct`) fly and fight: `Wasp Split` = `scripts/WaspFlightAI.cs` (hot path) + `scripts/WaspStaffAI.cs` (launch/follow/fleet/display/watchdog). So the 100,000-character cap is no longer a design limit: add PBs instead of cutting features. Rules and numbers: `CLAUDE.md` ("Script size"), `docs/design/wasp_split.md`, `docs/audit/multi_pb_feasibility.md` (IGC hop = 2 ticks, queue 25, keep aim/lead/fire in one PB, one writer per override). Rollout to the other drones is in progress (`docs/design/split_rollout.md` when it lands).

Trust these over anything inferred.

- **Flight and aim work.** Drones fly, turn onto targets and shoot. So the gyro sign convention in the
  scripts (`g.Pitch = -l.X; g.Yaw = -l.Y; g.Roll = -l.Z` after transforming the world angular velocity
  into gyro-local) is **correct in-game**. Don't "fix" it.
- Hornet **over-led** shots at a tumbling, smoking wreck → v1.3 gates target acceleration by steadiness and
  adds `Lead Scale`. Not yet re-verified in-game.
- Longbows (many) engaged a pirate ship but **all aimed at center mass** → v1.6 hull-point aiming. Not
  verified in-game.
- Drones **couldn't see enemies beside/above/below** them until facing them → v1.4 added 5 side cameras
  and an all-around short-ray scan. Then v1.5 added damage sensing. Not verified in-game.
- Drones **kept shooting disabled wrecks** and ignored new enemies → v1.5 disabled-target logic. Not verified.
- **Paste failures:**
  - v1.0 Hornet **would not paste**. A test copy without the Custom Turret Controller (and without
    battery/antenna settings) pasted. The Gnat, which *had* the battery/antenna settings, pasted, so those
    settings are fine.
  - v1.1 added a **Sensor** block to all four drones → **none of them pasted**. v1.2 removed it → pasted.
  - Earlier conclusion was "bad block IDs". **That was wrong**: `SmallTurretControlBlock` and
    `SmallBlockSensor` are valid IDs (confirmed in `defs.json`). Real cause unknown (see §4.1).
- **Scripts sometimes missing from PBs after spawning**: Longbows "from the get-go", and later Hornet
  (around v1.5) and Gnat (around v1.6). Root cause **unknown**. Not script size (a smaller script also
  failed). Questions still unanswered by the user: (a) spawned with F10 paste or projector + welding?
  (b) does the PB's Edit window show code, or is it empty / red error text?
- User has **not** yet done the projector test (project + weld a Gnat; does its PB keep the code?). This
  decides whether printers can produce scripted drones.

---

## 4. Lessons learned (don't repeat these)

### 4.1 Paste failures: leading hypotheses
1. **Incomplete object-builder XML** for "complex" blocks (Sensor, Turret Controller, probably the AI
   blocks, maybe Projector/Timer/Event Controller). The generator writes only name/color/enabled plus a
   few fields. Blocks with toolbars or settings objects may need fields the game fails to deserialize
   without. **The redesigned drones in `drones.py` include a Sensor + AI Offensive + AI Flight block, so
   they may fail to paste for the same reason.**
   Fix: ask the user to place one of each (Sensor, Custom Turret Controller, AI Offensive, AI Flight,
   AI Defensive, Merge Block, Projector, Welder, Timer, Event Controller, Warhead) on a small grid, save it
   with Ctrl+B, and send the `bp.sbc`. Copy the exact XML shape from it. Until then, ship each drone in two
   versions: core only (known-safe block types) and core + sensor/AI.
2. **Stale binary cache.** The game may keep a `bp.sbcB5` next to `bp.sbc` and load it instead. Always tell
   the user to delete old blueprint folders entirely before copying new ones. Unverified, but cheap to
   avoid.
3. **Game log tells you.** `%AppData%\SpaceEngineers\SpaceEngineers_*.log` after a failed paste. Ask for it.

### 4.2 Missing scripts: hypotheses
- Script failed to compile in-game. v1.0–v1.1 Longbow used `MyResourceSinkComponent` (railgun power
  reading), probably not allowed in in-game scripts; removed in v1.2. **Compile against the real game
  DLLs** to rule this out for every version (§6.3).
- Projector-built PBs may come without code (unverified).
- In-game instruction limit ("Script Too Complex" stops the script; the code would still show in Edit). The
  scripts run every tick and keep growing. Add `Runtime.CurrentInstructionCount` / `MaxInstructionCount` to
  each status screen and keep per-tick work bounded.

### 4.3 Mount points (cause of the floating blocks)
Many blocks attach only on specific faces. From the real definitions:
- small **Gyro**: Bottom only · large Gyro: Bottom only
- small **Programmable Block**: Front only (exclusion mask 3) · **large PB: Bottom only**
- small/large **Camera**: Back only, small patch, exclusion mask 3 (won't attach to a PB's front)
- small/large **Sensor**: Back only
- small **Ion thruster** (1×1×2): Back, plus side mounts only on the back-end cell
- small **Gatling** (1×1×4): Back, plus side mounts only on the back-end cell
- small **Antenna** (1×1×2): Back, Top/Bottom/Left/Right on the back-end cell
- large Antenna (1×6×2): Bottom only (exclusion 1, property 2)
- **Railgun** (1×2×8, both grids): Back, part of Top, Bottom near the middle, Left/Right near the rear
- Remote Control: all six faces · Small Battery: all · armour cube: all

`sebp.py` maps each mount rectangle into grid space. The face-coordinate convention is **inferred** from
thruster/gatling/welder definitions (documented in its docstring). Evidence it's right: the audit flagged
exactly the Hornet cameras the user saw floating. **Confirm it** by running the validator on a few of the
user's own known-good ship blueprints. Every block should pass. If not, fix the convention first.

### 4.4 Other
- Ion thrusters: Forward = exhaust direction; thrust pushes along the block's Backward.
- Keep 4–5 cells clear behind every thruster and in front of guns and cameras (the validator's `keep_clear`).
- A grid merged to a printer is **one grid**. Drone scripts must not drive the printer's thrusters (v1.8
  re-scans blocks when `Me.CubeGrid` changes and stays idle while docked).
- For printing: **no drone block may touch a printer block except the merge-block pair**, or the drone
  can't separate. A small **Welder is 3×3×6 with a 1.3 m weld radius**, so welders must sit close to every
  part of the drone.
- **Merge face**: the wiki says only the clamp side merges. `drones.py` assumed the small merge block's
  local Right is the clamp face. **Unverified**: confirm from the user's exported blueprint or a forum/wiki
  source.

---

## 5. Block facts from the definitions (`defs.json`)

| Block | Type / Subtype | Size | Notes |
|---|---|---|---|
| Armor | CubeBlock / SmallBlockArmorBlock, LargeBlockArmorBlock | 1³ | all faces |
| Remote control | RemoteControl / SmallBlockRemoteControl | 1³ | |
| PB | MyProgrammableBlock / SmallProgrammableBlock, LargeProgrammableBlock | 1³ | see mounts |
| Gyro | Gyro / SmallBlockGyro, LargeBlockGyro | 1³ | |
| Battery | BatteryBlock / SmallBlockSmallBatteryBlock (1³), SmallBlockBatteryBlock (3×2×3), LargeBlockBatteryBlock (1³) | | |
| Ion thruster | Thrust / SmallBlockSmallThrust, LargeBlockSmallThrust | 1×1×2 | |
| Camera | CameraBlock / SmallCameraBlock, LargeCameraBlock | 1³ | |
| Antenna | RadioAntenna / SmallBlockRadioAntenna (1×1×2), LargeBlockRadioAntenna (1×6×2) | | |
| Gatling | SmallGatlingGun / *(empty subtype)* | 1×1×4 | autocannon = SmallBlockAutocannon 1×1×5 |
| Rocket launcher | SmallMissileLauncher / *(empty)* (small, 1×1×4), LargeMissileLauncher | | |
| Railgun | SmallMissileLauncherReload / SmallRailgun, LargeRailgun | 1×2×8 | |
| Sensor | SensorBlock / SmallBlockSensor, LargeBlockSensor | 1³ | |
| Turret controller | TurretControlBlock / SmallTurretControlBlock (1×1×2), LargeTurretControlBlock | | |
| AI blocks | OffensiveCombatBlock / SmallOffensiveCombat · FlightMovementBlock / SmallFlightMovement · DefensiveCombatBlock / SmallDefensiveCombat · BasicMissionBlock / SmallBasicMission | 1³ | all faces |
| Merge | MergeBlock / SmallShipSmallMergeBlock (1³, strength 0.005), SmallShipMergeBlock (1×3×3, 0.015), LargeShipMergeBlock | | |
| Welder | ShipWelder / SmallShipWelder (3×3×6, SensorRadius 1.3), LargeShipWelder (1×1×2) | | |
| Projector | Projector / SmallProjector, LargeProjector | 1³ | all faces |
| Timer | TimerBlock / TimerBlockSmall, TimerBlockLarge | 1³ | |
| Event controller | EventControllerBlock / EventControllerSmall, EventControllerLarge | 1³ | |
| Warhead | Warhead / SmallWarhead (ExplosionRadius 4.49), LargeWarhead | 1³ | |
| Decoy | Decoy / SmallDecoy | 1³ | |
| Connector | ShipConnector / ConnectorSmall, Connector | | |

In the blueprint XML the type is written `xsi:type="MyObjectBuilder_<Type>"` with `<SubtypeName>`.
`defs.json` only stores id/size/mounts. **Re-index more fields** (thrust `ForceMagnitude`, gyro force,
power, camera raycast limits, battery capacity, sensor range) from the raw CubeBlocks `.sbc` files for the
simulator. The user has the `CubeBlocks` folder as a 7z.

---

## 6. Files in this bundle

Recreate this layout. `sebp.py` loads `../defs.json` relative to itself.

```
work/
  defs.json            block index
  stubs.cs             compile-only stand-in for the in-game API (written by hand, NOT the real API)
  tools/sebp.py        builder + validator (IDs, overlaps, mounts, connectivity, clearances, XML, thumbnails)
  tools/drones.py      redesigned Hornet / Gnat / Longbow S+L (validated)
  tools/index_defs.py  rebuilds defs.json:  python3 tools/index_defs.py <CubeBlocks folder> defs.json
  tools/audit.py       audits existing bp.sbc files for unattached blocks
  sim_v5.py            earlier 3D sim of the Gnat's point-and-burn "yo-yo" attack runs (python/numpy)
scripts/
  HornetDroneAI.cs     v1.8   LongbowSniperAI.cs v1.8   GnatSwarmAI.cs v1.8
  StingerAI.cs         v1.0 (never compiled)   HiveMotherAI.cs v1.0 (never compiled)
  HornetMothership.cs / LongbowMothership.cs / GnatMothership.cs   carrier relay (same code, different Tag)
```

### 6.1 Compile check used so far
Wrap a script in `public partial class Program : MyGridProgram { ... }` with usings for
`Sandbox.ModAPI.Ingame, VRage, VRage.Game, VRage.Game.ModAPI.Ingame, VRage.Game.ModAPI.Ingame.Utilities,
VRage.Game.GUI.TextPanel, VRageMath, Sandbox.Game.EntityComponents, VRage.Game.Components`, then
`mcs -target:library stubs.cs wrapped.cs`.

### 6.2 `stubs.cs` is missing types the v1.8 / Stinger / printer scripts use
Add these to the `Sandbox.ModAPI.Ingame` namespace, plus `bool CanScan(double d);` on `IMyCameraBlock`:
```csharp
public interface IMyWarhead : IMyTerminalBlock { bool IsArmed {get;set;} void Detonate(); }
public interface IMyProjector : IMyFunctionalBlock { bool IsProjecting {get;} int RemainingBlocks {get;} }
public interface IMyShipWelder : IMyFunctionalBlock {}
public interface IMyShipMergeBlock : IMyFunctionalBlock { bool IsConnected {get;} }
```

### 6.3 Better: compile against the real game assemblies
If you run on the user's PC, reference the DLLs in `...\steamapps\common\SpaceEngineers\Bin64\`
(the in-game API is typically in `Sandbox.Common.dll`, `SpaceEngineers.Game.dll`, `VRage.Game.dll`,
`VRage.Math.dll`, `VRage.Library.dll`, `VRage.dll`; check). This catches real API mistakes the stand-in
can't. The game also restricts which .NET types scripts may use, so prefer plain `System.Collections.Generic`,
`Math`, `StringBuilder` and `Random`, as the scripts already do.

### 6.4 Pending fix in `HiveMotherAI.cs` (was blocked mid-edit)
Rack mode must send `launch` on the **Stinger** tag, not the drone tag:
- after `bool RackMode = false;` add `string LaunchTag = "STINGER";`
- in `LoadConfig()` after the RackMode line add
  `LaunchTag = C(S, "Rack Launch Command Tag", LaunchTag);`
- change `IGC.SendBroadcastMessage(Tag + "_CMD", "launch");` to `IGC.SendBroadcastMessage(LaunchTag + "_CMD", "launch");`

---

## 7. Drone and printer designs

### Existing (scripts in `scripts/`)
- **Hornet**: small grid, 2 gatlings, 12 ion thrusters (2 per axis), gyro, 4 small batteries, antenna,
  nose cameras plus 5 all-around cameras. Holds standoff about 350 m, orbits and jinks. Modes: guard / hunt /
  assist / rtb / idle; `lock`, `next`, `split` / `focus`, `aim <mode>`, `ai on|off`, `aialert`, `range <m>`.
- **Longbow S / L**: railgun snipers (small 1,400 m @ 1,000 m/s, ~20 s recharge; large 2,000 m @
  2,000 m/s, ~60 s). Picks the "max accurate" range: the distance where a target dodging after the shot
  still gets hit (assumed dodge acceleration × flight time vs. target radius). Settles before firing, takes a
  fresh pre-shot raycast and checks for friendlies, jinks while reloading. Gravity drop is compensated.
- **Gnat**: minimal swarm drone (v1.7 had 14 blocks; the redesign adds a sensor and merge block, and the tail now carries AI Offensive + AI Flight [AI] blocks: 21 blocks, 7 cells long; Lite stays 18 blocks without AI). **Two
  axial ion thrusters** (forward + reverse). It moves by turning the whole ship ("point and burn"). Attacks
  in "yo-yo" runs: nose on target, close in, brake, back off and close again along the line of sight.
  Swarm ("hive") over IGC: leaderless slot numbers (sorted grid IDs), formation sphere, attack cone,
  separation, line-of-fire check, split focus.
- **Mothership scripts**: broadcast home point, relay the carrier's turret targets, relay commands.
  Known flaw: home = carrier center, so drones may fly into a big carrier. Send the carrier's radius and
  hold station outside it.

### Shared script architecture (v1.5–v1.8)
- `Main` → `Tick` inside try/catch; errors go to the PB screen.
- Tracks hold position, velocity, filtered acceleration (`EA()` trusts acceleration only when it's steady),
  spin, size, **hull points** (from raycast hit positions, stored in the target's own frame) and a
  disabled state.
- **IGC channels** (`<Tag>` per drone type, configurable):
  - `_TGT`: `MyTuple<long id, Vector3D pos, Vector3D vel, double radius, long sender>`; radius `-1` =
    target finished, everyone drops it for 120 s
  - `_SWARM`: `MyTuple<long gridId, Vector3D pos, Vector3D vel, long targetId, int flags>`; bit0 under
    fire, bit1 alarm
  - `_CMD` (string), `_HOME` (Vector3D), `_JOIN` (from printer; IDLE undocked drones start working)
- Detection: nose camera spiral scan and lock, side-camera close scan (wider and longer when hit),
  sensor (configured by terminal property name, falling back gracefully), turrets / turret controllers,
  IGC, AI-block hand-off (blocks tagged `[AI]` fly the search, the cameras confirm, the script takes over).
- Damage sensing: sums block integrity (`IMySlimBlock`); any drop counts as "under fire" for evasion and an
  alert to the hive.

### Planned, not built
- **Wasp**: Hornet frame with 2 small rocket launchers (rockets about 200 m/s, 500 explosive damage).
- **Stinger**: kamikaze (script done). Small warheads, forward thrusters, gyro, camera, sensor, antenna,
  merge block for a printer rack. Arms only near the target and away from its launch point; detonates at
  closest approach.
- **Watcher**: unarmed scout / spotter (Hornet scout mode is in v1.8: no guns, so it tracks targets for
  the hive and keeps standoff). Extra cameras, long-range antenna.
- **Midge**: **scriptless** drone (AI Offensive + AI Flight + AI Defensive, gatling, 6-axis thrusters,
  merge block). For printers in case projected PBs come out empty, and for script-disabled servers.
- **Printers**:
  - *Hive Mother*: 2 bays, scripted, prints Gnats.
  - *Twin Hive*: larger, more bays.
  - *Midge Nest*: **timer / event-controller only**, no script.
  - *Stinger Rack*: keeps Stingers docked, launches one per enemy.
  - A bay = projector (`[Bay N]`) + welders (`[Bay N]`) + merge block (`[Bay N]`).
  - `HiveMotherAI.cs` cycle: BUILD (welders on until `RemainingBlocks == 0`, stall warning) → CHARGE
    (drone shares printer power while merged) → RELEASE (merge off, broadcast `_JOIN`) → COOLDOWN.
    Keeps Max Drones alive by counting `_SWARM` broadcasts. Rack mode for Stingers.
  - Projectors can carry a pre-loaded projection in their object builder (`ProjectedGrid`). Worth
    trying so printers come pre-loaded, but verify the XML shape from a user-saved blueprint first.

---

## 8. Timers vs scripts (the user asked you to weigh this)

Conclusion so far: **a script runs the printer better.** It can wait for the projector's remaining-block
count to hit 0, handle missing components (stall detection), count living drones, charge the drone
before release, and broadcast join. A timer can only fire fixed delays (welders on, wait, merge off, wait,
merge on), and an event controller can't watch blocks that don't exist yet. **For printed drones,
timers / AI blocks win if projected PBs come out without code** (the Midge path). They also win on servers
with scripts disabled. Recommend: scripted printers plus one timer-only printer. Print scripted Gnats if the
projector test passes, AI Midges if it doesn't. Research the Event Controller's conditions (the wiki has a
page) before building the timer-only printer.

---

## 9. Simulation plan (the user's main request)

The game can't be run here. Build an engine:
- **Preferred: run the real C# drone scripts** inside a functional implementation of the in-game API, so
  any fix found in simulation is a fix to the shipped scripts. Compile each script as its own `Program`
  class (separate namespace / assembly) with `mcs`. Implement thrusters (override %, force from defs), gyros
  (override → angular velocity with torque / inertia limits), cameras (raycast with charge accumulating at
  ~2 km/s per camera and a ~45° cone), sensors, IGC (in-process broadcast with range), batteries / power,
  weapons, merge blocks, warheads, `Runtime` (tick time, plus an instruction-count estimate).
- **Bodies from the validated blueprints**: block cells, masses, mount-connected graph. Hits damage
  specific blocks. Destroyed PB / RC / gyro / thrusters / guns degrade or kill the drone. Optionally split
  grids that lose connectivity.
- **Physics**: rigid body, 60 Hz, vanilla speed cap about 100 m/s, mass and inertia from blocks.
- **Weapons** (wiki "Block Weapons"; confirm with `Ammos.sbc` / `Weapons.sbc` from the user):
  - gatling: 400 m/s, 600 RPM, 90 damage vs ship, 0.15° spread, range 800 m randomized 80–100%
  - autocannon: 150 RPM, 500 damage
  - rockets: 500 explosive damage, about 200 m/s
  - railgun: small 1,000 m/s, 1,400 m, 0° spread, ~20 s recharge; large 2,000 m/s, 2,000 m, ~60 s
  - small warhead: radius 4.49 m
  - Block HP needs `Components.sbc` (component integrity); until then, approximate.
- **Scenarios**: every 1v1 pair including mirrors (Hornet, Gnat, Wasp, Longbow S, Longbow L, Midge, Stinger,
  plus a scripted "pirate" target ship with turrets), **100 seeded runs each**; then 1v2, 1v3, 2v3 and mixed
  teams (e.g. Watcher + Gnats). Random start distance 1.5–3 km, random orientations, 5-minute cap.
- **Metrics**: win / loss / draw, time to kill, hits / shots, time on target, damage taken, friendly
  fire, collisions, stuck / circling detection, script exceptions, per-tick cost.
- **Loop**: run → audit → fix scripts → re-run → compare. Report before/after tables.
- **Earlier small sims** (python, `sim_v5.py` and the ad-hoc tests behind them):
  - Gnat yo-yo runs kept the gun on target 60–85% of the time vs a jinking target, never closer than ~150 m.
  - Pure "rotate to cancel drift" managed only 3–8%.
  - Steadiness-gated lead: steady 4 m/s² target misses by 1.6 m at 375 m; tumbling 4 rad/s target 4.0 → 2.9 m.
  - Split focus stayed stable (12 drones vs frigate + 2 fighters → 6/3/3), no flip-flopping.
- Be honest in the report about what the sim can't capture. Its vanilla-AI model (Midge) is an
  approximation. Its gyro convention is assumed, though it's confirmed in-game for these scripts.

---

## 10. Ask the user (when they're back)
1. Spawned with F10 or projector? Does a "scriptless" PB's Edit window show code? Any red error text?
2. Projector test: project + weld a Gnat; does the PB keep its code?
3. Send a small-grid blueprint with one each of: Sensor, Custom Turret Controller, AI Offensive, AI Flight,
   AI Defensive, Merge Block, Projector (ideally with a projection loaded), Welder, Timer, Event Controller,
   Warhead. Also a couple of their own working ships (to verify the mount validator).
4. `Content\Data\Ammos.sbc`, `Weapons.sbc`, `AmmoMagazines.sbc`, `Components.sbc` (for sim accuracy).
5. The game log after any failed paste.

---

## 11. Research links
- Block weapon stats: https://spaceengineers.wiki.gg/wiki/Block_Weapons
- Railgun: https://spaceengineers.wiki.gg/wiki/Railgun
- Block / SBC modding reference: https://spaceengineers.wiki.gg/wiki/Modding/Reference/Blocks
- Scripting, detection (sensors, cameras, turrets): https://spaceengineers.wiki.gg/wiki/Scripting/Detecting_Things
- AI Offensive / Defensive: https://spaceengineers.wiki.gg/wiki/AI_Offensive · https://spaceengineers.wiki.gg/wiki/AI_Defensive
- Projector (3D printers, Keep Projection): https://spaceengineers.wiki.gg/wiki/Projector
- Merge Block (only the clamp side merges): https://spaceengineers.wiki.gg/wiki/Merge_Block
- Ion thruster / Gatling / Battery pages on the same wiki (sizes, thrust, power)
- In-game API index (MDK, deprecated but still useful for interface names): https://github.com/malware-dev/MDK-SE/wiki/Api-Index
- Experienced script author's code (missile guidance, turret AI patterns): https://github.com/Whiplash141/SpaceEngineersScripts
- Keen's old engine source (dated, but shows object-builder field names): https://github.com/KeenSoftwareHouse/SpaceEngineers
- Rocket velocity discussion (rockets don't inherit ship velocity): https://support.keenswh.com/spaceengineers/pc/topic/rockets-should-inherit-their-launchers-velocity

---

## 12. Version history (for the changelog)
- **v1.0**: Hornet (gatling), Longbow S/L (railgun), Gnat (2-thruster swarm), mothership relays.
- **v1.1**: sensors added; nothing pasted.
- **v1.2**: sensor removed; Longbow stopped reading railgun power.
- **v1.3**: steadiness-gated lead, `Lead Scale`.
- **v1.4**: 5 extra cameras + all-around close scan; compact embedded scripts (size theory later disproven).
- **v1.5**: disabled-target detection, damage sensing and evasion, split focus, on-screen errors.
- **v1.6**: hull-point aim modes.
- **v1.7**: AI-block search hand-off (user places `[AI]` blocks).
- **v1.8** (uncompiled): printer join / re-scan on grid change, Hornet scout mode.

## Thrust vectoring and the Flight/Staff drones (this session; docs/thrust-vectoring.md)
- Every drone/ship whose flight script has the THRUST VECTOR LINK block (Hornet, Lancer, Harrier, Basilisk, Longbow, Mantis, Aegis, Wasp, Shrike Helm, Peeper) gets a small `[Thrust] ... PB` (`scripts/ThrustVectorAI.cs`) from `C.add_support_pbs()` in `tools/build_all.py`. When a thrust direction is lost it tells the flight PB which surviving group to line up with the force needed. Not run in the game; the sim shows no win-rate gain (table in the doc), so it is unproven.
- Aegis, Mantis, Longbow S/L and the Wasp are now two PBs: `[Flight]` (AegisAI.cs / MantisAI.cs / LongbowSniperAI.cs / WaspFlightAI.cs, SPLIT LINK block `tools/lk_block.cs`, `tools/split_sync.py`) + `[Staff]` (`<Drone>StaffAI.cs`, generated from WaspStaffAI by `tools/make_staff.py`). They were at the 100k cap; the flight halves are about 78-80k.
- Fixed in the split scripts: a zero dt at start-up flipped `_aiOn` and left the AI blocks off for ~14 s (also in the existing Wasp Split).
- Extra PBs cost ~140 kg each: Aegis-vs-Hornet 300 seeds 191 -> 160 wins. Probe ships `Twin PB Test` / `Hornet Pilot Test` are exempt (pinned PB counts).
- Sim: `--events "T:!kill-thrust F[ N];..."` (run/one/follow-test), `follow-test --flight-custom-data`. Selftests: M4 and M5 fail with or without this work (M4 verified on the old tree; M5 not verified), M6 launch-all failures also on the old tree.
- Release zips are NOT rebuilt.

