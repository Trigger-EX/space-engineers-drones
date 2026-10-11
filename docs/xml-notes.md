# Blueprint XML fidelity and mount-point validation (tools/sebp.py)

Status of this document: written from primary sources (Keen's public `KeenSoftwareHouse/SpaceEngineers` source, snapshot
Jan 2022; the user's `ref/Starburst/bp.sbc`, written by the current game; the game definitions in `ref/`).
Everything below is tagged **verified** (reproduced by a command listed here), **source** (read in Keen's code, not
run in-game) or **inferred / guess**. Nothing here has been pasted into the real game yet; section 5 is the test plan.

## 1. Headline findings

1. **Probable cause of the "Sensor / Turret Controller won't paste" failures (source, high confidence).**
   `MyObjectBuilder_SensorBlock.Remap()` and `MyObjectBuilder_TimerBlock.Remap()` call `Toolbar.Remap(...)` with no null
   check, and the paste path runs `MyEntities.RemapObjectBuilderCollection(...)` (`MyGridClipboard.cs`) on every pasted
   grid. The old generator wrote no `<Toolbar>`, so `Toolbar` was null, so the remap throws and the paste fails.
   `MyObjectBuilder_ShipController`/`RemoteControl` guard the same call (`if (Toolbar != null)`), which is why Remote
   Control and the v1.2 drones pasted fine. The Turret Controller block is newer than the 2022 source, but Starburst shows
   it carries a `<Toolbar>` too, so it very likely has the same unguarded remap. sebp now always writes an empty
   `<Toolbar>` (exactly the form the game writes: `ToolbarType Character`, `SelectedSlot nil`, `Slots`, `SlotsGamepad`)
   for Sensor, Timer, Turret Controller, Event Controller.
2. **`<CustomData>` was silently ignored by the game (source + Starburst, high confidence).** `MyTerminalBlock.CustomData`
   is stored in a `MyModStorageComponent` under GUID `74de02b3-27f9-4960-b1c4-27351f2b06d1` (`MyTerminalBlock.cs`);
   Starburst's cargo containers/PBs store their CustomData exactly that way, inside `<ComponentContainer>`. There is no
   `<CustomData>` element anywhere in Starburst. sebp's old `custom_data=` wrote one, which the game ignores. sebp now writes
   the ModStorage entry. Any drone config that relied on CustomData never reached the PB.
3. **`<ComponentContainer>` semantics (source).** `MyComponentContainerExtension.InitComponents`: if the element is absent,
   every default component from `EntityContainers.sbc` is created; if present, only the *serialized* default components are
   added. So omitting a container is always safe, and writing a partial one is the risky thing. Hence the policy below.
4. Starburst contains more of our block types than the task assumed (see the confidence table), including Projector (with an
   embedded `ProjectedGrids` small-grid ship, a ready model for pre-loaded printers), Event Controller, Welder, Programmable
   Block, Gatling and Rocket Launcher.
5. **The mount-point convention that was already in sebp.py was correct** (section 2). The one real bug in the old validator:
   it ignored the game's rule that exclusion masks are skipped when both blocks are the same definition.

## 2. Mount-point / face convention: verification

Source of truth: `Sandbox.Game/Definitions/MyCubeBlockDefinition.cs` (`m_mountPointTransforms`,
`m_mountPointWallOffsets`, `SetMountPoints`) and `MyCubeGrid.Static.cs` (`CheckMountPointsForSide`,
`TransformMountPoints`). I transcribed the six matrices by hand; they give, in block-local box space (x=Right,
y=Up, z=Backward, Forward = -Z, size X,Y,Z), for side-local (u,v):

| side | point | side | point |
|---|---|---|---|
| Front | (X-u, v, 0) | Back | (u, v, Z) |
| Left | (0, v, u) | Right | (X, v, Z-u) |
| Top | (u, Y, Z-v) | Bottom | (u, 0, v) |

This is identical to what `sebp.py` already had (and what its docstring claimed it had inferred). Rectangles are shrunk by
0.001, blocks attach when rectangles on the same plane with opposite normals overlap, and the exclusion/property mask test is
bypassed for two blocks of the same definition. No `MountPoints` element means all six faces fully covered; an empty
element means none. All of that is now in `Blueprint.mount_rects` / `Blueprint.attachment`.

**Empirical check on Starburst** (`python3 -I tools/audit.py --types ref/Starburst/bp.sbc`, 13 grids incl. 5 sub-grids,
slopes, panels, 2x2 conveyors, hinges, pistons, turrets, doors, connectors):

* 3343 of 3343 blocks validate as attached (100.00%), 0 skipped for unknown/unsupported definitions. The 1-block grid is
  counted as trivially attached, the rest are 3342/3342.
* Every one of the 13 grids is a single connected piece under the validator (the 3162-block main grid included), as the
  game itself kept them.
* Per-type result for the types our drones use (attached/total): Thrust small ion 2/2, large-grid ion/hydrogen thrusters 232/232, Camera
  small 2/2 + large 2/2, Gyro small 1/1 + large 82/82, Battery small 1/1 + large 12/12, Antenna small 1/1, Remote Control
  small 1/1, Turret Controller (large) 2/2, Railgun small 5/5, Medium-calibre gun 4/4, Gatling 4/4, Rocket Reload 1/1,
  Event Controller small 1/1, Projector small 1/1, Welder large 1/1, Programmable Block large 1/1, Connector 13/13,
  armour cubes/slopes/panels all 100%. Full table: run the command above.
* **Negative controls** (`python3 -I tools/audit.py --controls ref/Starburst/bp.sbc`): the same test with a deliberately
  wrong face mapping splits the ship: true convention 12 pieces for 12 multi-block grids; a one-side mirror error gives
  13 to 18 pieces; swapping Top/Bottom, Left/Right or Front/Back gives 185 / 166 / 132; random orientations 237.
  So the check is not vacuous.

Limits (honest): Starburst is mostly full-face armour, so per-block "attached" alone barely discriminates (single-side
mirror errors still leave 99.9% attached); the discriminating evidence is the piece count plus the source transcription. A flip of
the side-local `v` axis on Front/Back/Left/Right cannot be distinguished on Starburst (its tall 1x2 blocks are symmetric); the
source says there is no flip. Types we support that are **not present in Starburst** and so are validated by the
source transcription only: small Programmable Block, Sensor, Merge Block, Warhead, Decoy, Timer, the four AI blocks, small
Turret Controller, small Welder, small Gatling/Launcher variants. Blocks in Starburst's sub-grids that attach to a parent grid by
rotor/hinge/piston are separate grids (not tested across grids). Residual failures: none.

## 3. XML shape: what the game writes vs what sebp writes now

Field order below follows the game: `SubtypeName, EntityId, Min, [IntegrityPercent, BuildPercent], BlockOrientation, ColorMaskHSV,
[SkinSubtypeId], Owner, BuiltBy, ShareMode, DeformationRatio, ComponentContainer, CustomName, ShowOnHUD, ShowInTerminal,
ShowInToolbarConfig, ShowInInventory, NumberInGrid, Enabled, <type specific fields>`. The game omits `EntityId` for plain
armour, `BlockOrientation` for identity, `Owner`/`ShareMode` for blocks without ownership (Thruster, armour) and `Owner=0`.

| block type | confidence | notes |
|---|---|---|
| CubeBlock (armour, slopes) | seen-in-Starburst | SubtypeName, Min, orientation, colour only |
| Thrust | seen-in-Starburst | no Owner/ShareMode; `MyTimerComponent` (Frame100) container |
| CameraBlock | seen-in-Starburst | `IsActive`, `Fov` |
| BatteryBlock | seen-in-Starburst | stored-power fields (caller-supplied via `extra`) |
| RadioAntenna | seen-in-Starburst | `AttachedPB`, `IgnoreAllied`, `IgnoreOther`, `HudText` kept at game defaults |
| RemoteControl | seen-in-Starburst | autopilot container, empty Toolbar/BuildToolbar/OnLockedToolbar |
| TurretControlBlock | seen-in-Starburst (large); small = same class | Toolbar, `ToolIds/CameraId/DirectionBlockId` via `links=`; container has SunTracking + text panels |
| SmallMissileLauncherReload (railgun, rocket reload) | seen-in-Starburst | Inventory + capacitor container, empty `GunBase` as the game writes an empty railgun |
| SmallGatlingGun, SmallMissileLauncher | seen-in-Starburst | Inventory container, empty `GunBase`; no ammo is preloaded |
| Gyro | seen-in-Starburst | nothing beyond base |
| ShipConnector | seen-in-Starburst | DeformationRatio 0.5, Inventory+Timer container, connector state fields |
| MyProgrammableBlock | seen-in-Starburst | text-panel container (+ ModStorage when CustomData given), `Program`, `Storage`; `DefaultRunArgument nil` is dropped because the game omits null |
| EventControllerBlock | seen-in-Starburst | 5-event container as the game writes it, empty Toolbar, `SelectedEvent` default is Starburst's value (14): set it via `settings=` |
| Projector | seen-in-Starburst | empty `ProjectedGrids`, zero offset/rotation. Pre-loaded projections: put a `MyObjectBuilder_CubeGrid` inside `ProjectedGrids` (Starburst does it); not generated yet |
| ShipWelder | seen-in-Starburst | `Enabled=false` default, DeformationRatio 0.5, Inventory container |
| SensorBlock | from-source | `FieldMin/FieldMax`, empty `Toolbar`, `Play...`/`Detect*` flags in class order. New game may have extra fields; XmlSerializer defaults them |
| TimerBlock | from-source | empty `Toolbar`, `Delay` 10000, `CurrentTime`, `IsCountingDown`, `Silent` |
| MergeBlock, Decoy | from-source | no fields beyond FunctionalBlock |
| Warhead | from-source | TerminalBlock level (no `Enabled`): `CountdownMs`, `IsArmed`, `IsCountingDown` |
| OffensiveCombat / FlightMovement / DefensiveCombat / BasicMission | **guessed** | not in the 2022 source nor Starburst; written as plain functional blocks, no container (the game then builds the default `AiBlock*` components from `EntityContainers.sbc`). Unknown extra reference-type fields would stay null |

Container policy: seen types get the exact container the game writes (stripped of instance data); unseen types get none, which the
source guarantees yields the default components. `Blueprint(..., containers=False)` strips every container for an A/B paste test.
Ownership: no `Owner`/`BuiltBy` unless `Blueprint(owner=<identity id>)` is given (the paste sets `BuiltBy` itself; Owner left 0 matches what
pasted fine before). `NumberInGrid` is numbered per subtype. Every non-armour block gets a unique `EntityId` (the game remaps them at
paste); the Turret Controller links use them.

Blueprint document: `ShipBlueprint/Id/@Subtype` is now `"<display> [<10 hex of sha1(name, version, blocks)>]"` so each build with
different content or `version=` has a distinct id; `<DLC>` elements are emitted only if a used block's definition lists a DLC
(`Blueprint.add` refuses DLC blocks unless `allow_dlc=True`; defs.json DLC data checked against Starburst: its only DLC block,
Rotating Light Large, maps to the file's `<DLC>DecorativeBlocks2</DLC>`); `WorkshopId/OwnerSteamId/Points` written as the game does;
`bp.sbcB5` is never written and `write()` deletes a leftover one in the output folder; `thumb.png` is drawn with PIL when
installed and with a built-in dependency-free PNG writer otherwise (force with `SEBP_NO_PIL=1`; verified 512x512 output).

## 4. Tools

* `python3 -I tools/audit.py [--types|--list] bp.sbc...`: per-type attachment, pieces per grid (works on game-written and generated files).
* `python3 -I tools/audit.py --controls ref/Starburst/bp.sbc`: negative controls.
* `python3 -I tools/audit.py --shape generated.sbc [ref.sbc]`: tag presence/order diff per block type against a game-written file
  (current drones: all seen types OK, unseen types reported as source/guess).
* `python3 -I tools/extract_shapes.py ref/Starburst/bp.sbc tools/shapes.json`: regenerate the block templates (committed output).
* `python3 -I tools/probe_blueprints.py out/probes`: one tiny blueprint per risky block type (section 5).
* API additions to `sebp.Blueprint` (all optional, drones.py unchanged and re-run): `settings={tag: value}`, `links={...}`,
  `custom_data=` (now real), `owner=`, `allow_dlc=`, `containers=`, `write(outdir, display, version)`, `from_sbc`, `attachment`,
  `components`, `dlc_used`, `build_xml`.

## 5. In-game test plan (ordered by risk, highest first)

Delete old blueprint folders before copying (stale `bp.sbcB5`). Generate with
`python3 -I tools/probe_blueprints.py out/probes`, copy each folder to `%AppData%\SpaceEngineers\Blueprints\local\`, F10-paste, and send
the game log (`%AppData%\SpaceEngineers\SpaceEngineers_*.log`) after any failure. Each probe is two armour cubes plus one block, so a
failure names the block.

1. `P05`-`P08` AI Offensive / Flight / Defensive / BasicMission (shape guessed; these already carry no container). If one fails, send a Ctrl+B
   export of a small grid with that block so the shape can be copied (the one thing still missing).
2. `P02` Sensor and `P04` Timer (shape from source; the empty-Toolbar fix is the paste-failure hypothesis). Pass means hypothesis 1 confirmed.
3. `P03` Turret Controller (large shape seen in Starburst, small one built the same). Then in the terminal: do the weapon list, camera and
   direction fields look unset (expected, they are linked only when `links=` is used).
4. `P17` Programmable Block with CustomData: open the block's Custom Data and Edit window. Expect `PROBE-CUSTOMDATA-OK` and the probe comment
   text. Pass confirms finding 2 (previously CustomData was dropped).
5. `P09` Event Controller (container copied from Starburst; check the selected event is something harmless) and `P13` Projector
   (should be empty, projection offset zero).
6. `P10` Merge, `P11` Warhead, `P12` Decoy, `P14` Welder (welder must start OFF), `P15` Gatling, `P16` Rocket launcher.
7. `P01` Thrust/Gyro/Camera (regression: the seen types with the new containers/NumberInGrid fields) and `P00` control, then `P99_Everything`.
8. Full drones: Gnat (sensor + merge), Hornet (sensor + 2 AI blocks), Longbow S/L. Expected: no floating blocks (cut a block with the
   grinder; the thrusters, cameras, gyro, PB, antenna must all stay on the grid after paste, and the grid must not split into pieces).
   Paste a Gnat twice from two package versions: both must appear (unique Id) and no DLC warning should show.
9. Merge face: weld/merge test of the printer bay (the clamp face of the small merge block is still unverified, see HANDOFF 4.4).
10. Thumbnails: folder shows `thumb.png` in the blueprint screen (generated with and without PIL).

If something in 1-6 fails and the log does not name it, the fastest bisect is `containers=False` on that probe, then dropping individual
tail fields; the shape table in section 3 says which parts were copied from the game and which were guessed.

## 6. Not done / open

* No ammo preload for guns (shape needs a Starburst-like inventory item plus `GunBase` counts; empty guns are what the game writes for a fresh block).
* Pre-loaded projector grids not generated (shape known from Starburst, builder not written).
* AI block shapes (section 3) are the only blocks where the whole object is a guess.
* `drones.py` seeds `Blueprint` with `hash(name)`, which Python randomizes per process, so entity ids differ between runs of the same build;
  harmless, but use `zlib.crc32` if reproducible bytes are wanted (not my file).
* Mount rectangles are checked inside one grid; rotor/hinge/piston/connector links between grids are not modelled.
