## Install

1. Unzip this package anywhere. You get `Blueprints/<Name>/{bp.sbc,thumb.png}`, `Scripts/*.cs` and this README. The scripts in
   `Scripts/` are compacted (no comments) so they fit the Programmable Block's 100,000-character limit: paste those. The commented
   originals in `Scripts/Source/` are for reading only and are too long to paste.
2. **Delete the old blueprint folders first.** Open `%AppData%\SpaceEngineers\Blueprints\local\`, and remove every folder of an earlier
   version of these ships (Hornet, Gnat, Longbow ..., also their `bp.sbcB5` files). The game caches a binary `bp.sbcB5` next to `bp.sbc`
   and keeps loading the stale copy if it is still there. This package never ships a `.sbcB5`.
3. Copy the `Blueprints/<Name>` folders you want into `%AppData%\SpaceEngineers\Blueprints\local\`.
4. In game: press **F10** (blueprint screen), pick the ship, and paste it into the world (creative tools, or load it into a projector in a
   survival world).
5. Every ship is pasted as a **dynamic** small-grid ship (a large-grid ship for the Basilisk). Do not convert printers to a station: a
   static printer would make every printed drone static.
6. Check that "In-game scripts" is enabled in the world settings, otherwise the Programmable Blocks do not run.
7. If something does not paste, send the game log (`%AppData%\SpaceEngineers\SpaceEngineers_*.log`) and say which model and variant.

**Which variant?** `<Name>` (Full) uses the whole design, including block types whose XML has never been seen in a game-written
blueprint (Sensor, AI blocks, Decoy, cargo containers, turret). `<Name> Lite` uses only block types whose XML shape is copied from a
game-written ship (the Starburst reference), with the one exception noted under "Known unknowns" (the small merge block). If a Full ship
does not paste, try its Lite twin: it must still fly and fight. No ship uses DLC blocks.
