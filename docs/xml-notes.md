# Blueprint XML fidelity and mount-point validation (tools/sebp.py)

Status of this document: sections 1-6 were written when only `ref/Starburst` existed; **section 7 (Generator extension) is current** and
supersedes them where they disagree (shapes are now taken from all of the owner's samples, containers/inventories/toolbars/groups/multi-grid are
written; the open items of section 6 are closed there). Written from primary sources (Keen's public `KeenSoftwareHouse/SpaceEngineers` source, snapshot
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

* (closed in section 7) ammo preload for guns.
* Pre-loaded projector grids not generated (shape known from Starburst, builder not written).
* AI block shapes (section 3) are the only blocks where the whole object is a guess.
* `drones.py` seeds `Blueprint` with `hash(name)`, which Python randomizes per process, so entity ids differ between runs of the same build;
  harmless, but use `zlib.crc32` if reproducible bytes are wanted (not my file).
* Mount rectangles are checked inside one grid; rotor/hinge/piston/connector links between grids are not modelled.


## 7. Generator extension: inventories, sample-derived shapes, toolbars, block groups, multi-grid files

Everything below is checked by `python3 -I tools/tests/run_all.py` against the owner's game-written blueprints (`ref/user-samples/*/bp.sbc`
and `pc/enemies/Tech Hunter/bp.sbc`; `ref/` is not tracked: in a git worktree the tests look in the main checkout, or set `SEBP_SAMPLES`).
Tags: **seen** = reproduced from / verified against a game-written file, **derived** = copied from a sibling class, **source** = Keen's source,
**unseen** = no ground truth.

### 7.1 What was added to `tools/sebp.py` (new modules `sebp_xml.py`, `sebp_inv.py`, `sebp_toolbar.py`, `sebp_multi.py`)

| need | API |
|---|---|
| ammo / uranium / cargo pre-load | `bp.add(xtype, sub, mn, ..., items=[Item.ammo('NATO_25x184mm', 24), Item.ingot('Uranium', 100), ('Component', 'SteelPlate', 50)])`; `bp.fill_ammo(b, count='full')`; `bp.preload(b, items=, state=)`; `bp.max_items(b, kind, sub)`; `bp.items_of(b)` |
| stored state | `state={'capacitor': 'full'\|MWh, 'fuel': 'full'\|L (hydrogen engine) / kg (reactor, default = the uranium in items), 'filled': 'full'\|0..1 (hydrogen tank), 'stored_power': 'full'\|MWh (battery)}` |
| capacity check | `InventoryError` when volume (litres of the items vs the block's own `<Volume>`), mass, stack count, item type (a weapon takes only its magazines, a reactor only its fuel) or the shape (no inventory) do not allow it |
| toolbars | `toolbar=[...]` / `{index: item}` and `toolbars={'BuildToolbar': .., 'OnLockedToolbar': ..}` with `sebp_toolbar.block(target, action, params)`, `.group(name, action)`, `.weapon(type, sub)`, `.run(pb[, argument, experimental=True])`, params `single/int32/int64/string` |
| block groups | `bp.group('Thrusters', [blocks])` writes `<BlockGroups>` |
| event controller | `settings=sebp_toolbar.event_settings('connector', 0.8, invert=True)`, `links={'selected': [blocks]}`, `toolbar=[...]` |
| owner | `Blueprint(owner=id)`, `add(owner=id, built_by=id)`; 0 writes none; default `DEFAULT_OWNER` unchanged |
| shapes | `Blueprint(legacy_shapes=True)` = the shipped drones' exact XML; default = sample-derived; `sebp.confidence_table()`, `sebp.shape_report()`, `bp.shape_summary()` |
| several grids | `parent.attach(child, MU.Pose.axes(pos, fwd, up))`, `parent.dock(child, parent_block, child_block, gap=0, roll=0, lock=False)`, `MU.dock_candidates/dock_pose`, `sebp.load_sbc_multi(path)` / `Blueprint.from_sbc_multi`; `dock()` raises if the child would sit inside the parent (a connector whose Forward face looks into its own hull), try another `roll=` or move the connector |
| loader | `load_sbc` now also keeps `id`, `items`, `state`, `toolbars`, `groups`, `pose` per grid |

### 7.2 Shapes (`tools/shapes.json` format 3, `tools/extract_shapes.py`)

* `python3 -I tools/extract_shapes.py` rebuilds the file from all samples. Per block definition the exemplar is the **form the game writes most often**
  (structure signature = level, owner, container component list, tail tag list; ties: smallest, then sample name), complete blocks only (no
  `BuildPercent`), non-DLC, no Prototech. Instance data is reset (ids, toolbars, aim, ammo state, door/timer/connector state, pilot state, items, capacitor,
  `HudText`, timer delay -> 10000); `src` names the sample; `container_generic` says whether a `Type/*` container may serve unseen subtypes.
* **Four of the "samples" are not game files**: `Gnat`, `Hornet Drone`, `Longbow L`, `Longbow S` are old output of this very generator (no container,
  no owner on a ship with dozens of functional blocks). They are detected (`looks_generated`) and never used as ground truth.
* Sections: `shapes` (136 entries: 102 exact `Type/Subtype`, the rest `Type/*`) and `legacy` (the 40 Starburst-era entries, pinned verbatim).
  `Blueprint(legacy_shapes=True)` reads only `legacy` + the hand-written table + generic = byte-identical XML for the shipped drones; every call
  site in `tools/designs/*.py` and `probe_blueprints.py` passes it. `tools/tests/golden_hashes.json` pins the sha256 of eight shipped ships.
* Lookup order (default): exact subtype sample -> `Type/*` sample (container kept only if `container_generic`, then with the definition's inventory
  `Volume` and default magazine; otherwise omitted, which the game accepts: it builds the default components) -> legacy -> hand-written (`from-source`)
  -> generic (`guess`). `InteriorTurret` (no sample anywhere) is **derived** from `LargeGatlingTurret`: both are `MyObjectBuilder_LargeTurretBase`
  subclasses in the 2022 source. `SensorBlock`, `Warhead` stay hand-written (from-source), the four AI blocks stay guesses.
* Inventory `Volume` of an unseen subtype: `InventoryMaxVolume`, else `InventorySize` X*Y*Z, else (cargo) cells x cell volume. All three rules were
  verified against every sample shape that has one. Cockpit and connector volumes (1, 8, 1.152, 0.064 m3) are not derivable and only exist for seen subtypes.
* Optional fields the game writes only when non-default are inserted where the samples put them (`sebp.TAIL_AFTER`): connector `ThrowOut`,
  `CollectAll` (first after `Enabled`), `Strength` (between `MasterToSlaveTransform` and `MasterToSlaveGrid`), turret `Range` (after `MaxFov`),
  cockpit `IsMainCockpit`, door `State`, projector `ProjectedGrids`.

### 7.3 Inventories (seen)

* Item = `Amount`, `PhysicalContent xsi:type=MyObjectBuilder_<Kind>` (`SubtypeName`; magazines add `ProjectilesCount 0`), `ItemId`; `nextItemId` = count.
  Reproduced element for element for AmmoMagazine, Ingot, Component, Ore. Equal stacks are merged. PhysicalGunObject / ConsumableItem / OxygenContainerObject
  etc. are read (kept raw) but cannot be written or sized.
* **Volume tables are checked against 274 game-written inventories**: none exceeds its `<Volume>` (max 1.0000002), and 68 of them the game filled to the
  brim (ore, ingots, components) come out at exactly 100 %. That pins the ingot table, the 0.37 L/kg ore and **Scrap ore = 0.254 L/kg** (a fitted value).
* Railgun capacitor: an empty one is `<Component xsi:type="MyObjectBuilder_EntityCapacitorComponent" />` (no `StoredPower`); full is 0.016 (small) and 0.5
  (large) MWh, 0.00667 was seen mid-charge. Reactor `<Capacity>` = uranium kg in the inventory; hydrogen engine `<Capacity>` = litres, no inventory
  (full = definition `FuelCapacity` 100000 / 5000); tank `<FilledRatio>`; battery `CurrentStoredPower` / `MaxStoredPower`.
* `tools/tests` regenerates the inventory, capacitor, `Capacity` of every loaded weapon, turret, reactor, cargo container, welder, tank and engine in the
  samples from `items=` / `state=` read off the same block and compares (Volume, Mass, MaxItemCount, stacks, charge): all agree.
* Catalogue check (docs/design/systems_catalogue.md 2.6 / 3.3): `fill_ammo` holds 24 gatling turret boxes, 4 small gatling, 3 autocannon, 6 railgun /
  artillery / assault turret magazines, 5 small gatling turret, 2 small rocket turret, 768 interior-turret magazines, ... (test `test_fill_ammo_matches_the_catalogue`).

### 7.4 Toolbars and block groups (seen, except the PB Run slot)

* Slot forms (all reproduced from the samples): `ToolbarItemTerminalBlock` (Action, [Parameters], [CustomIconTitle], BlockEntityId),
  `ToolbarItemTerminalGroup` (Action, [Parameters], [CustomIconTitle], GridEntityId 0, BlockEntityId, GroupName), `ToolbarItemWeapon` (DefinitionId Type/Subtype).
  Parameter types seen: Single, Int32, Int64. 48 distinct `Action` ids occur in the samples (`sebp_toolbar.SEEN_ACTIONS`); other ids are written as given and
  listed in `bp.notes`.
* **A group slot's `BlockEntityId` is the EntityId of the block that owns the toolbar** (155 of 155 group slots), not a member of the group.
* **`<BlockGroups>` holds `<Vector3I>` cells, not EntityIds**: `MyObjectBuilder_BlockGroup/Name` + `Blocks/Vector3I{X,Y,Z}`. The cell is the definition's `Center`
  cell of the block (hangar door (0,2,0), 3x3x3 hydrogen thruster (1,1,2), small gatling gun (0,0,3), large welder (0,0,1), ...), derived from the samples
  into `shapes.json` `_centers` (35 definitions; checked by cross-validation: learned on half the files, 100 % correct on the other half). Definitions without a
  sample use the block-local cell (0,0,0): **unverified** that the game resolves any cell of a multi-cell block (it should: cube lookup), so test groups of
  turrets/railguns in game.
* Event Controller `SelectedEvent` ids in the samples: 1 (cargo containers, Threshold 0.5), 2 (guns/cameras/hinges, 0.99, invert), 6 (unresolvable), 7
  (connectors), 14 (welder). The names are inferred from the selected blocks (`sebp_toolbar.EVENTS_SEEN`), not read from the game.
* **PB `Run` slot: never seen in any file.** `run(pb)` writes `<Action>Run</Action>` + `BlockEntityId` (action id from the game source; the item shape is the seen
  terminal-block one). `run(pb, 'arg', experimental=True)` additionally writes `<Parameters>` with one `TypeCode String` parameter by analogy with the seen
  Single/Int32/Int64 parameters: **unverified**, noted in `bp.notes` and in `confidence_table()` as UNSEEN.

### 7.5 Several grids in one file (seen: Venture - Expedition End, Small Grid 8000)

* One `<CubeGrid>` per grid, each with a **world** `PositionAndOrientation` (Position, Forward, Up, Orientation quaternion). Verified on every grid of the samples:
  frame columns = (Forward x Up, Up, -Forward), standard quaternion of that matrix. Cell (i,j,k) has its centre at (i,j,k) x cell size, block frame origin =
  centre of the block's cell box.
* Connector pair geometry, recomputed from the two grids' poses and the block cells: the stored `MasterToSlaveTransform` (slave block frame in master block
  frame) is reproduced to 5 mm / 0.2 deg on both Venture pairs (the file stores floats around 3.9e6 m); `MasterToSlaveGrid.PositionOffset` = master grid origin - slave block
  origin in the slave block frame (1.3 mm); `OrientationOffset` (= `OrientationAsVector`) = quaternion of slaveBlockRot^-1 x masterGridRot (exact).
* Saved locks are not face-to-face exact: gaps along the normal 0.0707 m (ConnectorMedium on Connector, faces 1.75 m apart) and 0.1322 m (two Connectors), lateral
  < 1 cm. `dock_pose` places the faces `gap` metres apart (default 0) and picks one of the four axis-aligned rolls about the normal.
* Writer: `attach`/`dock` -> extra `<CubeGrid>` elements (ids unique across grids; toolbars/groups may refer across grids by block). **Connectors are written
  unlocked by default** (Tech Hunter form: `MasterToSlaveTransform`/`MasterToSlaveGrid` nil, `IsConnecting false`); `lock=True` writes the Venture form (both blocks:
  `ConnectedEntityId` right after `Enabled`, same `MasterToSlaveTransform`, `IsApproaching`/`IsConnecting true`, `TimeOfConnection` 60000 [arbitrary]; master = the child's
  block with `MasterToSlaveGrid`). Whether a paste restores the lock is **unverified** (test C2 of docs/design/grids_bays.md). Merge blocks store no lock.

### 7.6 In-game test plan for the extension, and what is still open

`python3 -I tools/probe_ext.py out/probes_ext` writes 13 paste probes (E01 small gatling with 4 boxes ... E14 bridge seat, E20/E21 docked pod unlocked / locked); the header of
the file says what to look at after each paste. Nothing of the extension has been pasted in the game yet.


* In-game paste of any new shape (turrets, reactor, cargo with items, cockpit, locked connectors, toolbars with groups) has not been done.
* Unseen subtypes: see `sebp.shape_report()`; notably 6 thrusters, 7 cockpits (container omitted), the small turrets and interior turret (container from the large
  gatling turret), `SmallDecoy`, large/medium conveyor sorters, merge blocks other than the small one, `LargeBlockSensor`/`SmallBlockSensor`, `Large/SmallWarhead`.
* Sensor block detection fields, AI combat blocks, projector with a pre-loaded `ProjectedGrids`, conveyor line routing (`ConveyorLines` is not written; conveyor blocks are placed only).
* `LargeMissileTurret`'s `TargetingGroup` etc. keep the sample's targeting flags (`Flags`), which are the owner's settings (assault/artillery turrets: characters off).

### 7.7 M0 additions (docs/design/control_arch.md M0): second PB, cockpit, test ships, placeholder events

* **A generated Programmable Block is now ON.** The sample-derived `MyProgrammableBlock/SmallProgrammableBlock` shape came from a PB that was switched off in the Pond Hopper sample (`<Enabled>false</Enabled>`), so every non-legacy ship with a script would have pasted with its PB off. `Blueprint.add(..., enabled=None)`: `None` = the shape's value except for a PB, which is on; `enabled=False` still switches it off. Legacy ships are unchanged (golden hashes pass).
* **Two PBs on one grid** work out of the box (`NumberInGrid` counts per type, distinct ids, names `[Helm] ...` / `[Staff] ...`): see `tools/designs/testships.py` `Twin PB Test`, the button panel whose slot Runs the Staff PB (`sebp_toolbar.run(pb)`, no argument).
* **Cockpit**: `Cockpit/SmallBlockCockpit` (3x3x3, 4 text surfaces) sits on the Hornet deck in `Hornet Pilot Test`; its toolbar takes `weapon()` and `block()` slots. Shapes for Cockpit, ButtonPanel, TimerBlock, EventControllerBlock, Door, PB exist (seen); **TextPanel does not** (the only exemplar, Starburst's `LargeLCDPanel`, carries a third-party inventory-manager text and mod storage; `extract_shapes.py` leaves `TextPanel` out until a clean sample exists).
* **Event Controller events without a seen number** (`cockpit` occupied, `power` stored fraction, `merge` connected): `sebp_toolbar.event_settings('cockpit', experimental=True)` writes the simulator's PLACEHOLDER ids 101/102/103 (`EVENTS_PLACEHOLDER`) and `bp.notes` says so. The game does not know these numbers (test K4 must save one real sample); they exist so a generated blueprint and the sim agree until then. Never ship a blueprint that carries them.
* **Test ships** (`tools/designs/testships.py`, kind `test`: built by `build_all.py --only ...`, never packaged, no Lite variant): `Hornet Pilot Test`, `Twin PB Test`.
* `tools/blocks_sync.py` + `tools/blocks.json`: one driver for all shared script blocks (new blocks are listed in the json; `--check` also runs the seven older `*_sync.py`).

* `Sprue Test` / `Sprue Test No Grinder` (`tools/designs/sprue_test.py`, test ships, not packaged): `ShipGrinder/SmallShipGrinder` is registered at build time in `sebp.HAND` as a GUESS (the legacy small-welder form with the grinder `xsi:type`; process-global once registered). The only guessed shape of the rig; fallback model without the grinder.
