# Printer Test: Hive Mother printing real Gnats (merge-pair release)

Sprue Test Merge proved in the game (2026-10-14) that a drone welded from a projection splits off the printer the moment its merge block is switched off (the 10X Missle Platform method). This is the real printer: Hive Mother, 2 bays, each prints a Gnat (guard/orbit drone) and releases it.

Copy `Blueprints/Hive Mother` into your Space Engineers Blueprints folder (delete any old "Hive Mother" folder first: stale `.sbcB5` cache).
World: CREATIVE, space, in-game scripts ON, no safe zone near. Paste Hive Mother (it is a ship: keep it clear of asteroids; dampeners on).
1. Terminal, the Hive Mother PB, Run with argument `start` (it may already run by itself). The PB screen shows each bay: BUILD -> FINISH -> CHARGE -> ARM -> RELEASE -> COOLDOWN.
2. FINISH may say "all blocks present (N armour cells unreadable): welding on for .. s more": expected (the game hides armour from scripts; v2.6 waits 2 s). Paste a FRESH copy: the script writes its settings into the PB Custom Data on the first run, so an old paste keeps the old 30 s.
3. RELEASE: the Gnat should come free, burn away for 4 s and start guarding/orbiting the printer. The bay then prints the next one (Max Drones 8).
Report: screenshots of the Hive Mother PB screen at FINISH, at RELEASE and after a Gnat flew off; any FAULT text; whether the Gnat orbits. If a bay shows FAULT "FUSED", send the PB's terminal text.
Zips not rebuilt.
