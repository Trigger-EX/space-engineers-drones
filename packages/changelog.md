### v2.1 (this package): foundry fixes
- **Finish-welding verification.** `HiveMotherAI.cs` 2.1 no longer leaves BUILD when the projector reports 0 blocks left (that only means "all placed", not "all welded"). A new FINISH state keeps every welder on until a full scan of the bay's drone box finds each block at full integrity and functional. Welders can no longer be switched off in BUILD/FINISH (the old stall "kick" that turned all welders off is gone; it now toggles one welder at a time). The LCD lists unfinished blocks (name, cell, integrity, distance to the nearest welder), reports off/damaged/unpowered welders and an empty `[Stock]`, and FINISH has a timeout that FAULTs with the list.
- **Robust welder reach.** The welder sphere centre is unknown (model dummy), so `reach.py` now checks 8 hypotheses (centre at the tip, 1, 2 or 3 cells inside the welder; radius 1.3 m and 1.0 m), a build-order support check and a foreign-bay check. Hornet-class bays now carry 10-12 welders (was 5-8), Gnat bays 7 (was 5). Exit directions were re-chosen per drone. Costs about +900 PCU and +2.7 t per Hornet-class bay. Per-bay Custom Data gains `Drone Box Min/Max`, `Drone Cell Count`, `Drone Occupied Cells`, `Ignore Cells`, an explicit `Exit Direction`, and a `[Launch]` section.
- **Automatic take-off.** A shared LAUNCH block (`tools/launch_block.cs`, synced by `tools/launch_sync.py`) is in the Hornet, Gnat, Longbow, Mantis, Wasp, Lancer, Harrier, Aegis and Basilisk scripts. A printed drone launches when its own PB sees the merge pair open, or on `[Launch] Armed=true`, `_JOIN`, FLEET `join`, or the arguments `armed` / `launch` / `disarm`. No single message is needed. It burns away from the printer for `Burst Seconds`, then goes home and into its start mode (default GUARD). The printer writes `[Launch]` into the drone PB before release, repeats `_JOIN` for 40 s and recompiles the drone PB.
- Known limit: Gnat only thrusts forward/back, so a lateral Away direction does not move it in the sim (see docs/launch-protocol.md). The Stinger script is not yet integrated with the LAUNCH block.
- Docs: `docs/foundry-notes.md`, `docs/welder-reach.md`, `docs/launch-protocol.md`, `docs/hive-protocol.md` section 9.

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
