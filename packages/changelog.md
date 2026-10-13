### v2.3 (this package): lead aiming against manoeuvring targets
In-game report (2026-10-13): the Basilisk's rockets kept missing an enormous target that was doing basic evasive manoeuvres. A rocket
needs about 4 s to fly 800 m; an AI ship on "evasive" flips its strafe every 2-5 s. Full write-up and measurements: `docs/lead-v1.md`.
- **LEAD v1.0** (shared block in every armed drone script, `tools/lead_block.cs`, scripts v2.5 / Gnat 1.12 / Longbow 2.3):
  - the target's freshest acceleration, held only for as long as its manoeuvres usually last, then constant velocity (the old
    solver extrapolated a lagging, smoothed acceleration over the whole flight);
  - the hull aim point counts only across the line of fire, turns with the target's spin for up to 1 s, and keeps
    `Aim Margin (predicted misses)` x the predicted miss of known hull round it, so long, uncertain shots go to the middle of the
    known hull instead of its edge.
- Basilisk / Wasp: rockets modelled as leaving with the ship's velocity + launch speed and curving onto the barrel line
  (`Rocket Velocity Model` CAPPED / NONE / FULL; NONE is the v2.2 model). Closed-form rocket path, Newton solve on the flight time.
- Offline bench (`tools/lead_bench`), hits on manoeuvring targets: rockets 50 % -> 62 % (strafing 120 m ship 43 -> 66 %, strafing
  30 m ship 14 -> 39 %), gatling 70 % -> 90 %, railgun 77 % -> 90 %.
- Simulator: rockets inherit the shooter's velocity with the speed cap (`--rocket-inherit`, default 1), evasive Pirate
  (`--pirate-evasive true`).

### v2.2: FOLLOW mode
Question from the user: does `guard` follow the owner? Only as a side effect: a Mothership link's position replaces the guard spot, but
the drones fly to the ship's centre (into its hull for most drone types), lag behind a moving ship, and fall back to the old spot when
the link stops. Nothing followed a player on foot.
- **FOLLOW** (shared block in every scripted combat drone, `tools/follow_block.cs`, synced by `tools/follow_sync.py`): `follow` / `escort`
  command (also Start Mode and launch Mode `FOLLOW`). The owner is the ship running a Mothership link (any type, FLEET channel), or the
  owner's own character seen by the drone's sensor (on foot, ~50 m; wins while seen). Each drone flies its own slot on a ring behind the
  owner (upper half in gravity) outside its hull, with the owner's velocity fed forward (estimated from the link's position fixes, so old
  link scripts work), detours round the hull instead of through it, engages only targets within 800 m of the owner, breaks off past
  1.2 km (2.2 km for the snipers), and after a link loss coasts 3 s on the owner's track (10 s on foot) and holds there. On a
  collision course with the owner it sidesteps up to 8 s ahead, and the ring widens (up to 2x) while the owner manoeuvres hard.
  New `[Follow]` section in Custom Data. No AI-block search in FOLLOW (the AI Flight block would fly the drone away from you).
- Gnat 1.11: `follow` / `escort` = `form`; the formation now matches the link ship's velocity (keeps up with it), and also reads the
  ship radius from the FLEET channel.
- Mothership links (Hornet / Longbow v1.1, Gnat v1.2): send the ship's radius on `<Tag>_HOMER` and `<fleet>_HOMER`; `follow` relays.
- FLEET block: `follow` and `escort` added to the default `Allowed Commands`.
- Simulator: `follow-test` (moving owner on the fleet channel, or an owner character reported to the sensors; cruise, weave, link loss,
  on foot, fight).

### v2.1: first in-game report fixes
In-game report (2026-10-13): Full drones set to `guard` stood still; `hunt` drones waited about 20 s, then all flew off the same way
and never saw a pirate ship 700 m away. Cause: the AI blocks paste with **AI Behavior off**, so they do nothing, but the scripts handed
them the ship and switched their own camera search off while waiting for them. Reproduced in the simulator (AI blocks inert:
3 Hornets vs pirate at 700 m won 0 of 40, found the pirate in 1 of 40 runs).
- **AI GATE** (shared block in every scripted drone, `tools/ai_gate_block.cs`): the script tries to switch AI Behavior on itself
  (the PB screen says whether it could). Until the AI blocks have flown the ship once, they only get it in 6 s windows between 8 s
  of the script's own camera search (under fire the script searches); once they have, they are trusted as before (status `AI ok`).
  New `[AI Gate]` section in Custom Data.
- Side cameras scan out to 900 m whenever there is no target (was 120 m); short close-scan rays no longer drain the nose cameras
  below the long scan's range while under fire.
- Hit direction: the blocks that lose integrity tell which side was hit; the nose sweep and the side cameras search that side.
- HUNT search patrol: drones fan out (each starts its square at a different corner), keep sweeping their cameras on a leg instead of
  looking along the flight path, hold position at listening stops, and Lite drones (no AI blocks) patrol too.
- Simulator results (pirate ship 700 m away, 36 pirate scenarios x 20 seeds, every model): AI blocks doing nothing, `hunt`: drone
  side wins 23 -> 44 of 720, `guard`: 24 -> 47 of 720 (3 Hornets alone: found the pirate in 1 -> 15 of 40 fights); AI blocks that
  need the AI Behavior switch: 23 -> 211 of 720 (the script switches it on). With working AI blocks drone-vs-drone results are
  unchanged (1v1 without the Watcher, 1,560 fights: 77.6 % -> 77.4 % decisive). The unarmed Watcher (no AI blocks) now patrols in
  `hunt` like every drone without AI blocks, so armed drones catch it less often (Watcher fights 60 % -> 24 % decisive).
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
