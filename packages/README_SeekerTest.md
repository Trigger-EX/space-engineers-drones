# Seeker test pack v2.6 (option 4: let the game find the vitals)

Experimental. This tests whether a drone script can learn **where an enemy ship's power blocks are** through a turret, instead
of guessing from the hull. The sim says it would not beat the new BREACH aim (`docs/tech-hunter-study.md`), but the real game may
behave differently, and that is what this pack finds out. **No DLC blocks.**

Delete any old `Longbow L Seeker` / `Basilisk Seeker` folders first (stale `bp.sbcB5` cache), then copy the `Blueprints` folders to
`%AppData%\SpaceEngineers\Blueprints\local\`.

## What is inside

| Item | What it is |
|---|---|
| `Longbow L Seeker` (+ Lite) | Longbow L with an **artillery turret** named `[Seeker]` on its back (2 km, same reach as its railgun). Load artillery shells into the turret. |
| `Basilisk Seeker` (+ Lite) | Basilisk with its roof gatling turret renamed `[Seeker]` (800 m). |
| `Scripts/SeekerProbe.cs` | Standalone probe for **any** ship with a turret: shows what the turret tells a script. |
| `Scripts/LongbowSniperAI.cs`, `Scripts/BasiliskAI.cs` | v2.6 scripts (already inside the blueprints). |

How the seeker works: the script calls `SetTargetingGroup("PowerSystems")` on the `[Seeker]` turret (Custom Data
`Seeker Group`: `PowerSystems`, `Weapons` or `Propulsion`), reads where the turret points (Azimuth / Elevation, lead removed),
raycasts that line with the drone's cameras and drills into the hull point it hits. `seeker off` / `seeker on` switch it.

## Test A: the probe (5 minutes, any ship)

1. Build or paste any ship of yours with a turret. Rename the turret so its name contains `[Seeker]`. Put `SeekerProbe.cs` in a
   Programmable Block on the same grid and press Run once.
2. Get an **enemy** ship (pirate, or one of your ships given to an enemy NPC faction) within the turret's range.
3. Read the PB screen (it updates every second):
   - `group PowerSystems` means the script could set the targeting group. `SET FAILED: ...` means it could not: send the text.
   - `target <name> ... aimed True`: the turret has locked on.
   - `hitpos N m from centre` is the **best case**: the game hands the script the targeted block's position. `no hitpos` means
     it doesn't, and the script has to use the barrel direction.
   - `aim line passes N m from centre`: a few metres means the turret aims at the middle of the ship. Tens of metres off-centre,
     changing when you run `group Weapons` / `group Propulsion`, means it really aims at the subsystem.
   - `hull hit N m from centre`: where a camera ray along the barrel met the hull (needs a camera on the ship; one named
     `[Seeker]` is used first).
4. Run the PB with `group Weapons`, then `group Propulsion`, and watch the numbers change.
5. Copy the PB's **Custom Data** (it keeps the last 60 readings) and send it, together with whether the enemy ship has **decoys**
   (decoys pull turrets onto themselves first).

## Test B: the Longbow L Seeker in a fight

1. Paste a `Longbow L Seeker` 1.5 to 2 km from an enemy ship and load artillery shells into its turret. Run `hunt`.
2. Its PB screen shows `Aim: BREACH (N hull pts) hole N m (seeker)` once the seeker drives the aim, and a `Seek:` line:
   - `aim ray, N m under hull` or `block pos given, ...`: working.
   - `turret has no target (group ...)`: the turret has not picked the ship (out of range, no ammo, or not an enemy).
   - `turret turning`, `turret on another target`, `no camera charge/angle`, `ray missed hull`: say which one you see most.
3. Send the PB screen text and what the drone destroyed (power blocks especially). For comparison, run the same fight with
   `seeker off`.

## Commands and settings

- `seeker on|off`: let the `[Seeker]` turret guide the aim (Custom Data `Seeker Turret Guides Aim`, default true).
- `aim breach`, `breach on|off`, `aim auto`: see the main README (BREACH is on by default against large grids in v2.6).
- Custom Data: `Seeker Group (PowerSystems/Weapons/Propulsion)`, `Breach Stall (s)`, `Breach Min Target Radius (m)`.
