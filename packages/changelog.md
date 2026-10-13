### v2.1 (this package): first in-game report fixes
In-game report (2026-10-13): Full drones set to `guard` stood still; `hunt` drones waited about 20 s, then all flew off the same way
and never saw a pirate ship 700 m away. Cause: the AI blocks paste with **AI Behavior off**, so they do nothing, but the scripts handed
them the ship and switched their own camera search off while waiting for them. Reproduced in the simulator (AI blocks inert:
3 Hornets vs pirate at 700 m won 0 of 40, found the pirate in 1 of 40 runs).
- **AI GATE** (shared block in every scripted drone, `tools/ai_gate_block.cs`): the script tries to switch AI Behavior on itself
  (the PB screen says whether it could), and never leaves the AI blocks in charge unless they visibly fly the ship: 6 s AI window,
  then 8 s of the script's own camera search, and so on. Under fire the script searches. New `[AI Gate]` section in Custom Data.
- Side cameras scan out to 900 m whenever there is no target (was 120 m); short close-scan rays no longer drain the nose cameras
  below the long scan's range while under fire.
- Hit direction: the blocks that lose integrity tell which side was hit; the nose sweep and the side cameras search that side.
- HUNT search patrol: drones fan out (each starts its square at a different corner), keep sweeping their cameras on a leg instead of
  looking along the flight path, hold position at listening stops, and Lite drones (no AI blocks) patrol too.
- Simulator: `--script-ai-blocks-inert` (AI blocks never act) and `--ai-behavior-needs-switch` (AI blocks act only once a script
  switches "ActivateBehavior" on); `GetProperties`/`GetActions` on terminal blocks.

### v2.0
- Every model exists as Full and **Lite** (Lite: only block XML shapes seen in a game-written ship, plus the merge block). Both variants build, validate and ship from one generator (`tools/build_all.py`).
- Blueprint generator rewritten around real data: mount-point validation (checked against the Starburst ship), real masses and costs from `defs_stats.json`, `<Toolbar>` written for Sensor/Timer/Turret Controller/Event Controller (probable paste-failure fix), Custom Data stored in ModStorage (the old `<CustomData>` element was ignored by the game), unique blueprint ids, stable (crc32) seeds, no `.sbcB5`.
- Weapon data corrected from the local game files: gatling 700 rpm, 800 m/s (was 400).
- Merge block clamp face corrected: small 1x1x1 block clamps toward Up (was Right); Hornet, Gnat and every printable drone got a clear docking face.
- New drones: **Wasp** (rockets), **Lancer** (autocannon), **Watcher** (unarmed scout), **Harrier** (interceptor), **Aegis** (escort / point defence), **Mantis** (railgun + gatling), **Basilisk** (large-grid rocket gunship), **Midge** (scriptless, experimental).
- New printers (projector + welders + merge block bays, `HiveMotherAI.cs` 2.0): Hive Mother, Twin Hive, Quad Hive, Hornet Forge, Wasp Foundry, Lancer Foundry, Longbow Foundry, Harrier Forge, Aegis Shipyard, Mantis Foundry, Watcher Pad, Fleet Forge, Escort Carrier, Mobile Hive, Static Nest, Stinger Rack.
- Hornet: rear camera moved one cell up to make room for the docking stalk. Gnat: merge block on a 2-cell stalk under the belly.
- Printers: every bay now has an exit lane. Before, Hornet-class bays (Hornet, Wasp, Lancer, Longbow S, Harrier, Aegis, Mantis, Watcher) had a welder on every side of the drone, so the finished drone could not leave. The drone's footprint swept 14 cells away from the merge block is kept free; Longbow S and Mantis leave sideways (outwards, away from the neighbour bay) and HiveMotherAI.cs reads the new per-bay `Exit Direction` Custom Data key to nudge the printer the right way.

### v1.x history
- **v1.0**: Hornet (gatling), Longbow S/L (railgun), Gnat (2-thruster swarm), mothership relays.
- **v1.1**: sensors added; nothing pasted.
- **v1.2**: sensor removed; Longbow stopped reading railgun power.
- **v1.3**: steadiness-gated lead, `Lead Scale`.
- **v1.4**: 5 extra cameras + all-around close scan; compact embedded scripts (size theory later disproven).
- **v1.5**: disabled-target detection, damage sensing and evasion, split focus, on-screen errors.
- **v1.6**: hull-point aim modes.
- **v1.7**: AI-block search hand-off (user places `[AI]` blocks).
- **v1.8** (uncompiled in-game): printer join / re-scan on grid change, Hornet scout mode.
