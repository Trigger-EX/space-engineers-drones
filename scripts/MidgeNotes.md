# Midge (scriptless drone): configuration notes

There is no Midge script. The Midge flies and fights with the vanilla AI blocks only. Everything here is
**experimental and untested in the game**. Sources: spaceengineers.wiki.gg pages AI Offensive, AI Flight, AI Defensive
(fetched 2026-10-11) and the local game data (`ref/CubeBlocks/CubeBlocks_Automation.sbc`, `ref/Data/EntityContainers.sbc`).

## Variants

| Variant | What it is | Script |
|---|---|---|
| **Midge** (experimental) | Small gatling, AI Offensive + AI Flight + AI Defensive, Remote Control, 2 gyros, 12 ion thrusters (2 per axis), 3 batteries, antenna, merge latch (rear, clamp face Backward). 35 blocks, 4169 kg, about 6.9 m/s2 on every axis. No Programmable Block. | none |
| **Midge Lite** (fallback, not scriptless) | The Gnat Lite frame (gatling, PB, no sensor, no AI blocks, 2 axial thrusters) named "Midge Lite". Used because a scriptless drone without AI blocks cannot fly. | `GnatSwarmAI.cs` (see the Gnat docs) |

Midge Lite is only a stand-in so the package has a drone that works if the AI-block XML fails to paste. If the printer's projected PB
comes out empty (test T6), Midge Lite will be empty too, and only the full Midge can work.

## What is unknown about the XML (why it is "experimental")

The three AI blocks are written as plain functional blocks with **no `<ComponentContainer>`** (`docs/xml-notes.md`, section 3, "guessed").
The game then creates the default components from `EntityContainers.sbc`: `SearchEnemyComponent`, `TargetLockingBlockComponent`,
`OffensiveCombatCircleOrbit / StayAtRange / HitAndRun / Intercept`, `AiBlockComponent`, `AiBlockPowerComponent` (Offensive); `AutopilotComponent`
for Flight. I could not find the serialized field names of these components (they are newer than Keen's public 2022 source, and no
saved blueprint with an AI block exists in `ref/`). So the blueprint cannot pre-set: attack pattern, ranges, speed limit, selected weapon,
facing mode, flee settings, or the "AI Behavior" switch. **All of that is the default value the game picks, and has to be set by hand once.**

Neither the wiki nor the definitions show an explicit link field between the blocks. The wiki describes the AI blocks as cooperating by
being on the same grid: AI Offensive/Defensive/Basic supply waypoints, AI Flight flies them. So there is no "Flight block" to pick in
AI Offensive; only a weapon list.

## Configure by hand (do this once, then save it back as your own blueprint)

Do it on a Midge pasted with F10 (not on a merged one). Open the terminal, find the blocks named `Midge AI ...`.

1. **Midge AI Flight**: set Speed Limit (default 10 m/s, raise to 60 to 100), Collision Avoidance **on** for free flight (off only when you want
   it to ram/dock), Precision Mode **off**, Align to P. Gravity **off** in space. Needs 6-axis thrust and a gyro: both are present.
2. **Midge AI Offensive**:
   * Attack: Enemies. Target Characters off (drones/ships only). Target Priority: Closest.
   * Attack pattern: **Stay at Range**, Min 300 m, Max 500 m (gatling range 800 m, rounds at 800 m/s), Evasive Maneuvers **on**.
   * Weapons: Facing Mode = **Use Static Weapons**, then move `Midge Gatling` into "Selected". Without this it will fly but not turn the gun onto the target.
   * Turn **AI Behavior on**.
3. **Midge AI Defensive** (optional): Defend Against Enemies; Flee trigger **Never** (it is a cheap drone) or **When taking damage** if you want it to run
   once hit; Flee Destination Away from Target. Its two "Set Up Actions" slots can switch AI Offensive on (slot 1) and off (slot 2). Note both are
   "Combat" blocks and only one of a type is active at a time, so either leave AI Behavior **off** on the Defensive block and use Offensive only,
   or let the Defensive block's slot 1 switch Offensive on.
4. Batteries are pre-charged and the antenna is on. Gatling ammo is **not** preloaded (no inventory fill is generated): drop NATO 25x184 mm
   magazines in the gatling.
5. Enemy detection requires a faction at war or very low standing with you, line of sight, and range 2500 m. Pirates qualify; neutral NPC factions do not
   unless "Enemies and Neutrals" is chosen.

## Printing a Midge (Midge Nest) and the switching problem

A Midge printed by projector + welder comes with every setting at default, **AI Behavior off** (assumed; unverified). Someone has to switch it on:

* by hand once after release; or
* by a toolbar action on a timer/button panel that lists "AI Behavior On" for the Midge's Flight and Offensive blocks. The toolbar needs the blocks'
  entity ids, so it only works for blocks that already exist when the toolbar is made, i.e. the printer cannot pre-reference the drone's blocks.
  After release the Midge is a separate grid and the printer cannot reach it anyway; or
* an Event Controller **inside the Midge** (e.g. "battery stored power >= 90 %" -> AI Behavior On, merge block Off). I did not put one in the blueprint:
  its toolbar entries reference block ids that do not exist until paste, and the shape for filled toolbar slots has never been seen. Test T7 in
  `docs/research.md` decides whether this is possible. Until then: switch on by hand.

**2026-10-14: `Midge Nest` (2 bays) does it from the printer side.** HiveMotherAI v2.7 finds the Midge's AI Flight / Offensive / Defensive blocks while
the Midge is still welded to the printer and runs `OnOff_On` + `ActivateBehavior_On` on them in the same tick it switches the merge blocks off
(the 10X Missle Platform showed `ActivateBehavior_On` is a real toolbar action). Untested in the game: the AI may act for one tick while still
attached. A Midge never reports in, so each bay prints at most `Max Prints` (3) Midges; `reload` on the Hive Mother PB restarts the count.

If the projected AI blocks arrive with no settings at all, the Midge printer has no reliable scriptless path and the user should print Midge Lite or
Gnats.

## Expected behaviour limits (even when configured)

* One AI Offensive pattern at a time; no shared targeting, no focus/split logic, no lead-aim tuning, no friendly-fire check. Aim is the AI block's own, with the first
  selected weapon as reference. Mixed weapons aim badly; the Midge has exactly one.
* Search interval 5 to 60 s (default 30 s): a new enemy may not be acquired for up to the interval.
* Does not use the antenna, IGC or hive. It will not fly to a mothership, report contacts, or return to base on low battery.
* Collision avoidance is the block's; it may refuse to approach close targets or stall near large grids.
* Merge latch: it stays merged to the printer until released; the merge block is not controlled by the AI.
* Compared with the scripted Gnat and Hornet it is expected to be weaker at hitting moving targets. The simulation engine can only approximate the
  vanilla AI, so treat any sim result for the Midge as a rough estimate.

## Test list (in order)

1. Paste `Midge`. If it fails, send the game log: the AI blocks are the prime suspect. Midge Lite is the fallback.
2. Check the three AI blocks appear in the terminal and the Offensive block lists `Midge Gatling` as an available weapon.
3. Do the configuration above, add ammo, set AI Behavior on, and put a hostile pirate ship within 2500 m.
4. Send a Ctrl+B export of the configured Midge (`bp.sbc`) so the real component XML can be copied into the generator; that would make the
   configuration ship pre-set.
