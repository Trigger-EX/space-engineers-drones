# Printer Test: Hive Mother printing real Gnats (merge-pair release)

Sprue Test Merge proved in the game (2026-10-14) that a drone welded from a projection splits off the printer the moment its merge block is switched off (the 10X Missle Platform method). This is the real printer: Hive Mother, 2 bays, each prints a Gnat (guard/orbit drone) and releases it.

Copy `Blueprints/Hive Mother` into your Space Engineers Blueprints folder (delete any old "Hive Mother" folder first: stale `.sbcB5` cache).
World: CREATIVE, space, in-game scripts ON, no safe zone near. Paste Hive Mother (it is a ship: keep it clear of asteroids; dampeners on).
1. Terminal, the Hive Mother PB, Run with argument `start` (it may already run by itself). The PB screen shows each bay: BUILD -> FINISH -> CHARGE -> ARM -> RELEASE -> COOLDOWN.
2. FINISH may say "all blocks present (N armour cells unreadable): welding on for .. s more": expected (the game hides armour from scripts; v2.6 waits 2 s). Paste a FRESH copy: the script writes its settings into the PB Custom Data on the first run, so an old paste keeps the old 30 s.
3. RELEASE: the Gnat should come free, burn away for 4 s and start guarding/orbiting the printer. The bay then prints the next one (Max Drones 8).
Report: screenshots of the Hive Mother PB screen at FINISH, at RELEASE and after a Gnat flew off; any FAULT text; whether the Gnat orbits. If a bay shows FAULT "FUSED", send the PB's terminal text.
Zips not rebuilt.

## New printers (2026-10-14, Hive Mother v2.7 script)
- `Tube Foundry` / `Tube W Foundry`: one bay each for Tube 2x2, 2x4, 3x3, 4x4 (W = the warhead tubes). Each tube has a small latch stalk with a merge block. A printed tube stays still while welded on, then hunts once released. With no enemy around it searches with its AI blocks. Keep the W foundry away from anything you want to keep.
- `Midge Nest`: 2 bays of scriptless Midges (EXPERIMENTAL). Hive Mother switches the Midge's AI behaviour on as it releases it. Report whether the released Midge flies. A Midge cannot report in, so each bay prints 3 and stops; run `reload` on the Hive Mother PB to print more.
Same checks as before: time from print finish to release, any FAULT, whether the drone leaves cleanly.

## Re-solved shipyards (2026-10-14, Hive Mother v2.8)
Your Aegis test showed the welders reach less far than the printers were designed for: the 3 blocks left in each Aegis bay are exactly the ones the smaller
reach misses. Every bay is now solved so that each drone block is reached with that smaller reach; delete the old folders and paste fresh copies of
Aegis Shipyard, Hornet Forge, Harrier Forge, Wasp Foundry, Lancer Foundry, Watcher Pad, Fleet Forge, Escort Carrier, Probe Foundry.
"Component stock empty" no longer appears in a creative world (it was really "blocks out of reach").
Longbow Foundry and Mantis Foundry are NOT included: no welder layout can reach 2-3 blocks buried inside those drones (see the chat for the options).

## Survival conveyors, nose-first Tubes, printer guard (2026-10-14, Hive Mother v2.9)
Every printer now has conveyor junctions from each welder's back to the two `Component Stock [Stock]` containers in its core, so it can print in
SURVIVAL: fill the stock containers with components (the PB's Custom Data lists what one drone of each type needs). Printers carry more thrusters
to keep their speed with the pipes' mass. If a bay line on the Hive Mother screen shows `PIPES n/m`, only n of its m welders reach the stock through the
conveyors: tell me the printer and bay (the welder's conveyor port position is not confirmed in the game yet).
Tube Foundry / Tube W Foundry: the tubes now face out of their bays and burn clear for 2.5 s when released, then search or hunt.
Printed drones no longer touch the printer's thrusters while they are still welded on (the Watcher Pad problem).
Longbow Foundry: the Longbow S had a reactor, an armour block and its [Thrust] PB where no welder reaches; they moved inside its outline.
Mantis Foundry is not included yet (see the chat).
