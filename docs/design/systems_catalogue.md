# Systems catalogue for the four warships (fighter, corvette, frigate, dreadnought)

Key `systems_catalogue`. Read-only study; generated from `defs.json`, `defs_stats.json`, the owner's blueprints (`ref/user-samples/*`, `pc/enemies/Tech Hunter`) and `tools/sebp.py`. Scratch scripts (not committed): `/tmp/claude-0/-home-user-space-engineers-drones/6a04a508-9b69-51e1-8b05-9278fa2139b7/scratchpad/` (`designs.py`, `part*.py`, `hull_rules.py`, `fillet_test.py`, `inv_patch.py`). No simulation was run (CLAUDE.md: none was needed).

## 0. Decisions in one page

1. **Rules.** Non-DLC (`dlc == []`) AND not `*Prototech*` (14 blocks carry no DLC flag but need Prototech components). `sebp.add()` already refuses DLC blocks. Every subtype named in this document passed an `assert not dlc`.
2. **Armour.** Every light and heavy armour block has exactly 5 HP/kg in the data. Heavy blocks are chosen for PCU (6.6x / 5.6x more HP per PCU) and because one block takes many hits; use heavy outside, light inside. Shape set: Block, Slope, Corner, CornerInv, Slope2Base/Tip, Corner2Base/Tip are enough for every hull in this document (all with identical heavy/light mounts).
3. **Propulsion.** Hydrogen main thrusters on all four ships (4-10x lighter than ion per newton, no reactor, 642.7 L/s per MW of listed power), tanks sized 1.25x (fighter, corvette), 7x (frigate) and 3.5x (dreadnought) the 30-minute need. Large: `LargeBlockLargeHydrogenThrust` 7.2 MN / 6.9 t; small: `SmallBlockLargeHydrogenThrust` 480 kN / 1.2 t. Gyros: `LargeBlockGyro` (33.6 MN*m) / `SmallBlockGyro` (448 kN*m).
4. **Weapons (section 4).** Fighter 2 autocannon + 1 assault gun + 2 gatling (5.3 k DPS, 66 t, 16 m/s2). Corvette 2 small railguns + 3 assault + 2 autocannon + 4 gatling turrets (8.1 k DPS, 257 t, 8 m/s2, 2 Hornet docks). Frigate 2 large railguns + 2 artillery + 4 assault + 8 gatling turrets (18.4 k, 2.1 kt, 3.4 m/s2, 4 docks). Dreadnought 6 railguns + 8 artillery + 12 assault + 20 gatling + 4 rocket turrets (58.9 k, 13.2 kt, 2.0 m/s2, 8 docks). Point defence = gatling turrets (+ decoys); large heavy armour shrugs off small warheads.
5. **Power.** Railgun recharge is the only big electric load of a hydrogen-thrust ship: small 3.6 MW (20 s), large 38 MW (59 s). Frigate 6 x `LargeBlockSmallGenerator` (90 MW), dreadnought 1 large reactor + 8 small (420 MW), uranium 1 kg = 1 MWh.
6. **PCU.** Fighter 3.3 k, corvette 12.1 k, frigate 9.7 k, dreadnought 34.6 k (69 % of a 50 k world, 35 % of 100 k). Armour blocks (1 PCU each) dominate small-grid ships; gyros (50 PCU) are the third-largest PCU line in the dreadnought after weapons and armour.
7. **Ammunition/fuel in the blueprint.** The XML shape is known and quoted (section 3.2): `Items` of `MyObjectBuilder_InventoryItem` inside the `MyInventoryBase` component, plus railgun `MyEntityCapacitorComponent` `StoredPower` (0.016 small / 0.5 large), reactor `Capacity`, tank `FilledRatio`, battery `CurrentStoredPower`. The generator needs `items=` support and shapes for the large turrets, reactors, cargo, hydrogen engines/tanks, cockpits (none exist in `shapes.json`). The inventories are only 3-6 rounds deep for assault/artillery: a conveyor feed from cargo is mandatory for sustained fire, and the sim ignores inventory and conveyors entirely (infinite ammo, or a full inventory).
8. **Style.** The block-by-block rule set of section 6.3 (exposed faces -> Block / Slope / Corner / CornerInv / Slope2) produced a validating 127-block wedge-nosed hull in both light and heavy armour. **`validate()` cannot detect a wrongly oriented slope** (negative test passed); add the 10-line mount-faces-hull check.
9. **Sim limits.** No cockpit/manual-flight model, no fuel, no connectors/rotors/subgrids/printing, no timers/ECs, no conveyors (section 5). Give each ship a RemoteControl for the scripts.

## 1. Rules applied

- **DLC rule.** A block is usable only if `defs.json[...]["dlc"]` is an empty list. `defs_stats.json` agrees (0 mismatches over 1,503 blocks; 787 non-DLC, 716 DLC). Every subtype recommended below was asserted `dlc == []` by script (`assert not v['dlc']`) and `sebp.Blueprint.add()` refuses DLC blocks anyway (`allow_dlc=False`). Watch the near-twins that ARE DLC: every `*Reskin`, `*SciFi`, `*Industrial`, `*Warfare2`, `*ModularThruster`, `LargeSearchlight/SmallSearchlight`, `*StandingCockpit`, `*SuspendedControlSeat`, `*RoundEdge*` armour (CoreSystems), `BridgeWindow*` (Warfare2), `HalfWindow*/Embrasure/PassageSciFi*` (Warfare1), `LargeBlockNarrowViewport*` (Economy2), `LargeBlockCentredDoor*`, `SlidingHatchDoor*`, `AirtightHangarDoorWarfare2*`, `LargeBlockRadioAntennaDish/LargeBlockGate` (Frostbite). Counts per DLC in the data: ScrapRace 70, Automatons 56, Signal 54, DecorativeBlocks3 53, Contact 50, Prosperity 50, Fieldwork 49, CoreSystems 48, Economy2 44, HeavyIndustry 43, DecorativeBlocks2 42, ApexSurvival 39, SparksOfTheFuture 39, Warfare2 29, DecorativeBlocks 20, Warfare1 19, Frostbite 9, Economy 2.
- **Second rule (mine): exclude the 14 `*Prototech*` blocks even though they carry no DLC flag.** Their component lists need `PrototechFrame`/`PrototechScrap`-class items (`PrototechPanel`, `PrototechCapacitor`, `PrototechPropulsionUnit`, ...) that a normal assembler cannot make from ingots (the repo's `components` table lists them with `PrototechScrap` ingots or `PrototechFrame` as its own ingot). Tempting but excluded: `LargePrototechReactor` 400 MW, `LargeBlockPrototechThruster` 14.0 MN, `LargeBlockPrototechGyro` 201.6 MN*m, `LargeBlockPrototechBattery` 18 MWh / 48 MW, `LargePrototechJumpDrive`. [data] for the numbers, [rec] for "not craftable in survival".
- **The owner's samples contain DLC blocks** (`RotatingLightLarge/Small`, `GratedStairs`, both DecorativeBlocks2): do not copy them into generated ships; `extract_shapes.py` is fine, the census table in docs/audit is not a whitelist.
- **Hidden-variant caveat.** Many armour pieces carry `gui_visible == False` (e.g. `LargeBlockArmorCorner`, `LargeBlockArmorSlope`, most `...Window1x1...`). They are not in the G-menu list; in the game they appear as variants of a visible pair (`BlockPairName`) and can be pasted from a blueprint (the user's Starburst/Venture samples contain `Window1x1Slope`, `Window1x1Face`). So they are legal for the generator. `public == False` is the stricter flag (`LargeRoundArmor_Corner`, `_CornerInv`, `_Slope`, `Monolith`, `Stereolith`, plushies): do not use those.
- Units: mass kg, force N, power MW, energy MWh, torque N*m. Large cell 2.5 m, small cell 0.5 m (`constants.CubeSizeMeters`). Every armour block is 1x1x1 cells; HP below is `sum_of_components` (= block `MaxIntegrity`, verified in `_meta.notes`), not the damage-modified value.
- Evidence labels: [data] = defs.json / defs_stats.json / owner blueprints; [wiki] fetched from spaceengineers.wiki.gg; [src] repo docs that cite Keen source; [rec] recollection with a confidence; [calc] arithmetic done in this session.

### Fact worth knowing before choosing armour [data][calc]
Every light AND heavy armour block in the data has exactly **5 HP per kg** (large light 500 kg / 2,500 HP; large heavy 3,300 kg / 16,500 HP; small light 20 kg / 100 HP; small heavy 112 kg / 560 HP). Heavy armour does not give more HP per tonne. It gives (a) 6.6x (large) or 5.6x (small) more HP per PCU, so PCU-limited worlds should use heavy blocks, (b) one big block that takes many hits to remove (a gatling round does 90 per hit: 2 hits kill a small light block, 7 hits a small heavy one), and (c) a lower `DeformationRatio` (large 0.4, small 0.16 vs small light 0.32), which the explosion and collision rules multiply into the damage (`docs/audit/weapons.md` W-01, W-09; large light armour has no explicit ratio). [data] for the numbers, [src] for the rule.

## 2. Catalogue tables

### 2.1 Armour families, both grid sizes

All blocks are 1x1x1 cells. `L`/`S` = large / small grid; `lt`/`hv` = light / heavy. Mass kg and HP are per block. **Mounts** list the faces that have mount rectangles in the block's default orientation (Fr=Front, Bk=Back, Lf, Rt, Tp, Bt=Bottom; a number is the fraction of that face covered; no entry = the face carries no mount, i.e. it is the open/slanted side; a triangular mount is stored as 2-3 rectangles, so a right triangle reads 0.75 or 0.36 instead of 0.5). Front is the block's Forward face; the validator needs overlapping mount rectangles on both blocks. PCU is 1 for every armour block (a few CornerSquare/HalfSlope pieces list no PCU, the game then charges the default 1).

| family (Large lt / Large hv / Small lt / Small hv subtypes follow the pattern) | L lt kg / HP | L hv kg / HP | S lt kg / HP | S hv kg / HP | mounts (default orientation) | use |
|---|---|---|---|---|---|---|
| `ArmorCenter` | 2800 / 14000 | - | 100 / 500 | - | Lf:0.80 Rt:0.80 Tp:0.80 Bt:0.80 |  |
| `ArmorCorner` | 2400 / 12000 | - | 100 / 500 | - | Lf:0.24 Rt:0.64 Tp:0.24 Bt:0.64 |  |
| `ArmorInvCorner` | 2700 / 13500 | - | 100 / 500 | - | Lf:0.64 Rt:0.80 Tp:0.64 Bt:0.80 |  |
| `ArmorSide` | 2600 / 13000 | - | 100 / 500 | - | Lf:0.64 Rt:0.64 Tp:0.24 Bt:0.80 |  |
| `BlockArmorBlock` | 500 / 2500 | 3300 / 16500 | 20 / 100 | 112 / 560 | all faces full | full cube |
| `BlockArmorCorner` | 80 / 400 | 560 / 2800 | 20 / 100 | 46 / 230 | Fr:0.75 Rt:0.75 Bt:0.75 | tetra corner cap (Front/Right/Bottom) |
| `BlockArmorCorner2Base` | 200 / 1000 | 1190 / 5950 | 20 / 100 | 66 / 330 | Fr:0.75 Lf:0.75 Rt:0.25 Bt:0.75 | 2-cell corner, base |
| `BlockArmorCorner2Tip` | 60 / 300 | 416 / 2080 | 20 / 100 | 46 / 230 | Fr:0.20 Rt:0.36 Bt:0.36 | 2-cell corner, tip |
| `BlockArmorCornerInv` | 420 / 2100 | 2800 / 14000 | 20 / 100 | 86 / 430 | Fr:0.75 Bk Lf Rt:0.75 Tp Bt:0.75 | concave corner fill |
| `BlockArmorCornerSquare` | 140 / 700 | 890 / 4450 | 20 / 100 | 46 / 230 | Fr:0.75 Lf:0.75 Bt | square corner |
| `BlockArmorCornerSquareInverted` | 380 / 1900 | 2480 / 12400 | 20 / 100 | 86 / 430 | Fr Bk:0.75 Lf Rt:0.75 Bt | square concave corner |
| `BlockArmorHalfCorner` | 120 / 600 | 872 / 4360 | 20 / 100 | 46 / 230 | Fr:0.50 Lf:0.50 Bt:0.75 | half corner |
| `BlockArmorHalfSlopeCorner` | 40 / 200 | 264 / 1320 | 20 / 100 | 46 / 230 | Bk:0.20 Rt:0.20 Bt:0.20 | half-slope corner |
| `BlockArmorHalfSlopeCornerInverted` | 460 / 2300 | 3050 / 15250 | 20 / 100 | 106 / 530 | Fr:0.76 Bk Lf:0.76 Rt Tp:0.76 Bt | half-slope concave corner |
| `BlockArmorHalfSlopeInverted` | 440 / 2200 | 2930 / 14650 | 20 / 100 | 106 / 530 | Fr:0.76 Bk:0.76 Lf Rt:0.50 Tp:0.50 Bt | half slope inverted |
| `BlockArmorHalfSlopedCorner` | 220 / 1100 | 930 / 4650 | 20 / 100 | 66 / 330 | Fr:0.75 Lf:0.75 Bt:0.75 | half sloped corner |
| `BlockArmorHalfSlopedCornerBase` | 220 / 1100 | 990 / 4950 | 20 / 100 | 66 / 330 | Fr:0.50 Bk:0.36 Lf:0.50 Rt:0.36 Bt | half sloped corner base |
| `BlockArmorInvCorner2Base` | 440 / 2200 | 2930 / 14650 | 20 / 100 | 106 / 530 | Fr:0.76 Bk Lf Rt:0.75 Tp Bt:0.75 | 2-cell concave, base |
| `BlockArmorInvCorner2Tip` | 320 / 1600 | 2030 / 10150 | 20 / 100 | 106 / 530 | Fr:0.75 Bk:0.76 Lf Rt:0.36 Tp Bt:0.36 | 2-cell concave, tip |
| `BlockArmorRaisedSlopedCorner` | 340 / 1700 | 2180 / 10900 | 20 / 100 | 92 / 460 | Fr:0.78 Bk:0.50 Lf:0.78 Rt:0.50 Bt | raised sloped corner |
| `BlockArmorRoundCorner` | 80 / 400 | 2740 / 13700 | 20 / 100 | 86 / 430 | Fr:0.75 Rt:0.75 Bt:0.75 | curved corner |
| `BlockArmorRoundCornerInv` | 420 / 2100 | 3100 / 15500 | 20 / 100 | 106 / 530 | Fr:0.75 Bk Lf Rt:0.75 Tp Bt:0.75 | curved concave corner |
| `BlockArmorRoundSlope` | 260 / 1300 | 2900 / 14500 | 20 / 100 | 86 / 430 | Fr Lf:0.75 Rt:0.75 Bt | curved ramp |
| `BlockArmorSlope` | 260 / 1300 | 1650 / 8250 | 20 / 100 | 66 / 330 | Fr Lf:0.75 Rt:0.75 Bt | 45 deg ramp, tall face = Front, flat = Bottom |
| `BlockArmorSlope2Base` | 380 / 1900 | 2480 / 12400 | 20 / 100 | 92 / 460 | Fr Bk:0.50 Lf:0.75 Rt:0.75 Bt | 2-cell ramp, tall half |
| `BlockArmorSlope2Tip` | 120 / 600 | 872 / 4360 | 20 / 100 | 46 / 230 | Fr:0.50 Lf:0.36 Rt:0.36 Bt | 2-cell ramp, thin half |
| `BlockArmorSlopeTransition` | 200 / 1000 | 1308 / 6540 | 20 / 100 | 66 / 330 | Fr:0.75 Lf:0.75 Rt:0.36 Bt | slope-to-corner transition (3 + 3 mirror/base/tip pieces) |
| `BlockArmorSlopeTransitionBase` | 320 / 1600 | 2110 / 10550 | 20 / 100 | 92 / 460 | Fr Bk:0.36 Lf:0.75 Rt:0.75 Bt |  |
| `BlockArmorSlopeTransitionBaseMirrored` | 320 / 1600 | 2110 / 10550 | 20 / 100 | 92 / 460 | Fr:0.36 Bk Lf:0.75 Rt:0.75 Bt |  |
| `BlockArmorSlopeTransitionMirrored` | 200 / 1000 | 1308 / 6540 | 20 / 100 | 66 / 330 | Bk:0.75 Lf:0.75 Rt:0.36 Bt |  |
| `BlockArmorSlopeTransitionTip` | 100 / 500 | 654 / 3270 | 20 / 100 | 46 / 230 | Fr:0.50 Lf:0.36 Rt:0.25 Bt:0.75 |  |
| `BlockArmorSlopeTransitionTipMirrored` | 100 / 500 | 654 / 3270 | 20 / 100 | 46 / 230 | Bk:0.50 Lf:0.36 Rt:0.25 Bt:0.75 |  |
| `BlockArmorSlopedCorner` | 260 / 1300 | 1650 / 8250 | 20 / 100 | 66 / 330 | Fr:0.75 Bk:0.36 Lf:0.75 Rt:0.36 Bt | sloped corner (Base/Tip pieces) |
| `BlockArmorSlopedCornerBase` | 400 / 2000 | 2780 / 13900 | 20 / 100 | 106 / 530 | Fr Bk:0.75 Lf:0.75 Rt Tp:0.75 Bt |  |
| `BlockArmorSlopedCornerTip` | 100 / 500 | 496 / 2480 | 20 / 100 | 46 / 230 | Bk:0.36 Lf:0.36 Bt:0.75 |  |
| `BlockArmorSquareSlopedCornerBase` | 360 / 1800 | 2310 / 11550 | 20 / 100 | 92 / 460 | Fr:0.78 Bk:0.50 Lf:0.78 Rt:0.50 Bt | square-base pyramid corner (with Tip) |
| `BlockArmorSquareSlopedCornerTip` | 120 / 600 | 654 / 3270 | 20 / 100 | 46 / 230 | Fr:0.36 Lf:0.36 Bt | pyramid tip |
| `BlockArmorSquareSlopedCornerTipInv` | 180 / 900 | 1208 / 6040 | 20 / 100 | 66 / 330 | Fr:0.50 Bk:0.36 Lf:0.50 Rt:0.36 Bt | inverted pyramid tip |
| `HalfArmorBlock` | 240 / 1200 | 1650 / 8250 | 20 / 100 | 66 / 330 | Fr Lf:0.50 Rt:0.50 Tp:0.50 Bt:0.50 | half-height slab |
| `HalfSlopeArmorBlock` | 80 / 400 | 416 / 2080 | 20 / 100 | 46 / 230 | Fr:0.50 Lf:0.20 Rt:0.20 Bt:0.50 | half slope |
| `RoundArmor_Corner` | 80 / 400 | - | - | - | Fr:0.72 Rt:0.72 Bt:0.72 |  |
| `RoundArmor_CornerInv` | 420 / 2100 | - | - | - | Fr:0.72 Bk Lf Rt:0.72 Tp Bt:0.72 |  |
| `RoundArmor_Slope` | 260 / 1300 | - | - | - | Fr Lf:0.75 Rt:0.75 Bt |  |
| `Armor2x1HalfSlopedPanelLeft` | 60 / 300 | 198 / 990 | 20 / 100 | 46 / 230 | Lf:0.20 Tp:0.05 Bt:0.05 | panel |
| `Armor2x1HalfSlopedPanelRight` | 60 / 300 | 198 / 990 | 20 / 100 | 46 / 230 | Rt:0.20 Tp:0.05 Bt:0.05 | panel |
| `Armor2x1HalfSlopedTipPanelLeft` | 60 / 300 | 198 / 990 | 20 / 100 | 46 / 230 | Bk:0.05 Lf:0.18 Bt:0.05 | panel |
| `Armor2x1HalfSlopedTipPanelRight` | 60 / 300 | 198 / 990 | 20 / 100 | 46 / 230 | Bk:0.05 Rt:0.18 Bt:0.05 | panel |
| `Armor2x1SlopedPanel` | 100 / 500 | 396 / 1980 | 20 / 100 | 66 / 330 | Lf:0.20 Rt:0.20 Tp:0.10 Bt:0.10 | panel |
| `Armor2x1SlopedPanelTip` | 100 / 500 | 396 / 1980 | 20 / 100 | 66 / 330 | Bk:0.10 Lf:0.18 Rt:0.18 Bt:0.10 | panel |
| `Armor2x1SlopedSideBasePanel` | 100 / 500 | 264 / 1320 | 20 / 100 | 66 / 330 | Bk:0.10 Rt:0.74 Tp:0.05 Bt:0.10 | panel |
| `Armor2x1SlopedSideBasePanelInv` | 100 / 500 | 264 / 1320 | 20 / 100 | 66 / 330 | Bk:0.10 Lf:0.74 Tp:0.05 Bt:0.10 | panel |
| `Armor2x1SlopedSideTipPanel` | 60 / 300 | 132 / 660 | 20 / 100 | 46 / 230 | Bk:0.10 Rt:0.26 Bt:0.05 | panel |
| `Armor2x1SlopedSideTipPanelInv` | 60 / 300 | 132 / 660 | 20 / 100 | 46 / 230 | Bk:0.10 Lf:0.26 Bt:0.05 | panel |
| `ArmorCenterPanel` | 100 / 500 | 330 / 1650 | 20 / 100 | 66 / 330 | Fr:0.10 Bk:0.10 Tp:0.10 Bt:0.10 | panel |
| `ArmorHalfCenterPanel` | 60 / 300 | 178 / 890 | 20 / 100 | 46 / 230 | Fr:0.05 Bk:0.05 Bt:0.10 | panel |
| `ArmorHalfPanel` | 60 / 300 | 178 / 890 | 20 / 100 | 46 / 230 | Fr:0.05 Bk:0.05 Rt:0.50 Bt:0.10 | panel |
| `ArmorHalfSlopedPanel` | 80 / 400 | 198 / 990 | 20 / 100 | 46 / 230 | Bk:0.05 Rt:0.20 Bt:0.05 | panel |
| `ArmorPanel` | 100 / 500 | 330 / 1650 | 20 / 100 | 66 / 330 | Fr:0.10 Bk:0.10 Rt Tp:0.10 Bt:0.10 | panel |
| `ArmorQuarterPanel` | 40 / 200 | 112 / 560 | 20 / 100 | 46 / 230 | Bk:0.25 Rt:0.05 Bt:0.05 | panel |
| `ArmorSlopedPanel` | 120 / 600 | 462 / 2310 | 20 / 100 | 66 / 330 | Bk:0.10 Lf:0.20 Rt:0.20 Bt:0.10 | panel |
| `ArmorSlopedSidePanel` | 60 / 300 | 178 / 890 | 20 / 100 | 46 / 230 | Fr:0.10 Rt:0.61 Bt:0.10 | panel |
| `RoundArmorPanel` | 160 / 800 | 528 / 2640 | 20 / 100 | 66 / 330 | Fr:0.16 Bk:0.16 Rt:0.10 Bt:0.10 | panel |
| `RoundArmorPanelCorner` | 100 / 500 | 330 / 1650 | 20 / 100 | 46 / 230 | Fr:0.16 Rt:0.16 Bt:0.16 | panel |
| `RoundArmorPanelFace` | 80 / 400 | 264 / 1320 | 20 / 100 | 66 / 330 | Fr:0.10 Lf:0.80 Bt:0.10 | panel |
| `RoundArmorPanelInvertedCorner` | 100 / 500 | 330 / 1650 | 20 / 100 | 66 / 330 | Fr:<.01 Bk:0.16 Lf:0.16 Rt:<.01 Tp:0.16 Bt:<.01 | panel |

Reading the family names: the real subtypes are `Large` + name for large light (`LargeBlockArmorSlope`), `LargeHeavyBlockArmorSlope` or `LargeBlockHeavyArmorHalfCorner` for large heavy (Keen uses both orders; the table's `BlockArmor*` rows contain both), `Small` + name for small. Panel families end `...PanelLight` / `...PanelHeavy` (plus `Left/Right/Inv` forms). `ArmorCenter/ArmorCorner/ArmorInvCorner/ArmorSide` (large 2,400-2,800 kg, 12-14 k HP, `LargeBlock` pair absent) are old hidden decorative pieces; skip them.

**Mount-point behaviour that matters for stylish hulls** [data, verified by the test in section 6.4]:
1. Cube, Slope, Corner, Slope2 Base/Tip, InvCorner and the "Transition"/"SquareSloped" families all have FULL (or triangle-half) mounts on the faces that touch the hull body. A slope has full Front and Bottom mounts plus triangular Left/Right mounts; its slanted face has NO mount.
2. Panels (`...Panel...`, 5-25 % of a face), `Window*` (10-20 %) and `Half*` pieces attach through a thin edge; they attach to a cube face as long as the rectangles overlap, but they are fragile in the physics sense and never carry other blocks well. Use them as skin trim only.
3. Large heavy and light variants are mount-identical, so `LargeHeavyBlockArmorX` and `LargeBlockArmorX` interconnect freely (tested). Solid armour has no `excl`/`prop` mount filters. Panels and windows carry `prop=2` (some also `excl=1`), but no non-DLC block carries `prop=1`, so the veto rule `(A.excl & B.prop) or (A.prop & B.excl)` (sebp.attachment) never fires among non-DLC blocks [data, checked over all 787].
4. Slope2Base's Back face is a half-height strip (v 0..0.5) that meets Slope2Tip's half-height Front strip. This is the 2-run, 1-rise ramp; three such pairs make a long smooth nose.
5. Large-grid blocks cannot attach to small-grid blocks directly (the generator asserts the grid size); small grids dock to large hulls through connectors, landing gear or a merge block on a subgrid.

### 2.2 Cockpits, seats, remote control

A Cockpit is an `IMyShipController` (manual flight + `MoveIndicator` readable by scripts). Non-DLC only; `G` = hidden from the G-menu (still pasteable, see section 1). Hand flying needs one of these with `CanControlShip`; the fighter cockpit is small-grid only [data].

| block (type) | grid | size cells | mass kg | HP | PCU | note |
|---|---|---|---|---|---|---|
| `PassengerSeatLarge` | L | 1x1x1 | 220 | 900 | 15 | passenger only |
| `CockpitOpen` | L | 1x1x1 | 320 | 1100 | 45 | open 1x1x1 seat (45 PCU), used in user ships |
| `LargeBlockCockpit` | L | 1x1x1 | 368 | 1130 | 45 | compact large cockpit (45 PCU) (G) |
| `OpenCockpitLarge` | L | 1x1x1 | 446 | 1560 | 150 | open large seat (150 PCU) (G) |
| `LargeBlockCockpitSeat` | L | 1x1x1 | 1768 | 7380 | 150 | big 2.5 m bridge seat with displays, 150 PCU (G) |
| `PassengerSeatSmall` | S | 3x3x3 | 220 | 900 | 15 | passenger only (no controls) (G) |
| `PassengerSeatSmallNew` | S | 3x3x2 | 220 | 900 | 15 | passenger only |
| `PassengerSeatSmallOffset` | S | 2x3x2 | 220 | 900 | 15 | passenger only |
| `OpenCockpitSmall` | S | 3x3x3 | 263 | 965 | 45 | open seat (no canopy) (G) |
| `RoverCockpit` | S | 2x3x3 | 374 | 1320 | 100 | rover seat |
| `SmallBlockCockpit` | S | 3x3x3 | 797 | 3180 | 150 | standard small bridge, 3x3x3 (G) |
| `SmallBlockFlushCockpit` | S | 3x3x4 | 1252 | 5125 | 45 | flush cockpit 3x3x4 (G) |
| `DBSmallBlockFighterCockpit` | S | 3x4x6 | 1325 | 5605 | 150 | the only fighter cockpit, 3x4x6 cells = 1.5x2x3 m canopy; user Pod Chaser/Tech Hunter fighter use it. Controller. (G) |
| `LargeBlockRemoteControl` (RemoteControl) | L | 1x1x1 | 137 | 505 | 25 | script/AI flight; 0.01 MW |
| `SmallBlockRemoteControl` (RemoteControl) | S | 1x1x1 | 38.2 | 101 | 25 | script/AI flight; 0.01 MW |

Recommendation: **fighter = `DBSmallBlockFighterCockpit` (small)**; corvette = `SmallBlockCockpit` or `SmallBlockFlushCockpit`; frigate and dreadnought = `LargeBlockCockpitSeat` as the bridge (+ a `CockpitOpen` for a secondary station) and a `LargeBlockRemoteControl` as the script's controller. DLC traps: `LargeBlockStandingCockpit`, `SmallBlockStandingCockpit` (Warfare2), `*SuspendedControlSeat` (CoreSystems). Do not make the PB depend on a cockpit being the main controller: the sim has no cockpit model (section 5), a RemoteControl is what the sim and all current scripts use.

### 2.3 Thrusters (non-DLC, no Prototech)

`F` = force; `kN/t` = block force per tonne of block mass; `N/kg` equal. Ion needs electricity and works in vacuum only at full force; atmospheric works only in atmosphere; hydrogen needs a hydrogen tank/conveyor and burns `642.7 L/s per MW of listed max power` (so a large hydrogen thruster burns 4,820 L/s at full thrust: matches [wiki] Hydrogen Thrusters table 4820.05 / 803.34 / 385.60 / 80.33 L/s; [calc] ratio 642.7 for all four). Thrust is along the block's Backward face (nozzle) pushing the grid Forward-wise.

| block | grid | size cells | mass kg | HP | PCU | F kN | power MW | kN per t | type/fuel | notes |
|---|---|---|---|---|---|---|---|---|---|---|
| `LargeBlockSmallFlatAtmosphericThrust` | L | 1x1x1 | 1273 | 3570 | 12 | 200 | 0.80 | 157.1 | Atmospheric | needs air (min influence 0.3) |
| `LargeBlockSmallFlatAtmosphericThrustDShape` | L | 1x1x1 | 1273 | 3570 | 12 | 200 | 0.80 | 157.1 | Atmospheric | needs air (min influence 0.3) |
| `LargeBlockSmallHydrogenThrust` | L | 1x1x1 | 1420 | 5980 | 12 | 1080 | 1.25 | 760.6 | Hydrogen | 803 L/s at full thrust |
| `LargeBlockSmallAtmosphericThrust` | L | 1x1x3 | 4000 | 10180 | 12 | 648 | 2.40 | 162 | Atmospheric | needs air (min influence 0.3) |
| `LargeBlockSmallThrust` | L | 1x1x2 | 4380 | 7180 | 12 | 345.6 | 3.36 | 78.9 | Ion |  |
| `LargeBlockLargeHydrogenThrust` | L | 3x3x3 | 6940 | 30300 | 12 | 7200 | 7.50 | 1037.5 | Hydrogen | 4820 L/s at full thrust |
| `LargeBlockLargeFlatAtmosphericThrust` | L | 3x3x2 | 12190 | 27400 | 12 | 2600 | 6.70 | 213.3 | Atmospheric | needs air (min influence 0.3) |
| `LargeBlockLargeFlatAtmosphericThrustDShape` | L | 3x3x2 | 12190 | 27400 | 12 | 2600 | 6.70 | 213.3 | Atmospheric | needs air (min influence 0.3) |
| `LargeBlockLargeAtmosphericThrust` | L | 3x3x5 | 32970 | 73000 | 12 | 6480 | 16.8 | 196.5 | Atmospheric | needs air (min influence 0.3) |
| `LargeBlockLargeThrust` | L | 3x2x4 | 43200 | 49200 | 12 | 4320 | 33.6 | 100 | Ion |  |
| `SmallBlockSmallThrust` | S | 1x1x2 | 121 | 350 | 12 | 14.4 | 0.20 | 119.0 | Ion |  |
| `SmallBlockSmallFlatAtmosphericThrust` | S | 1x1x1 | 303 | 860 | 12 | 32 | 0.20 | 105.6 | Atmospheric | needs air (min influence 0.3) |
| `SmallBlockSmallFlatAtmosphericThrustDShape` | S | 1x1x1 | 303 | 860 | 12 | 32 | 0.20 | 105.6 | Atmospheric | needs air (min influence 0.3) |
| `SmallBlockSmallHydrogenThrust` | S | 1x1x1 | 334 | 1390 | 12 | 98.4 | 0.12 | 294.6 | Hydrogen | 80 L/s at full thrust |
| `SmallBlockSmallAtmosphericThrust` | S | 1x1x3 | 699 | 1770 | 12 | 96 | 0.60 | 137.3 | Atmospheric | needs air (min influence 0.3) |
| `SmallBlockLargeThrust` | S | 3x2x4 | 721 | 1220 | 12 | 172.8 | 2.40 | 239.7 | Ion |  |
| `SmallBlockLargeFlatAtmosphericThrust` | S | 3x3x2 | 1060 | 2630 | 12 | 230 | 1 | 217.0 | Atmospheric | needs air (min influence 0.3) |
| `SmallBlockLargeFlatAtmosphericThrustDShape` | S | 3x3x2 | 1060 | 2630 | 12 | 230 | 1 | 217.0 | Atmospheric | needs air (min influence 0.3) |
| `SmallBlockLargeHydrogenThrust` | S | 3x3x3 | 1222 | 5160 | 12 | 480 | 0.60 | 392.8 | Hydrogen | 386 L/s at full thrust |
| `SmallBlockLargeAtmosphericThrust` | S | 3x3x5 | 2948 | 6980 | 12 | 576 | 2.40 | 195.4 | Atmospheric | needs air (min influence 0.3) |

Per 10 MN of thrust [calc]: **large hydrogen** (7.2 MN, 6.94 t) needs 1.39 blocks = 9.6 t, 17 PCU, 0 MW, 6,700 L/s; **large ion** (4.32 MN, 43.2 t, 33.6 MW) needs 2.31 blocks = 100 t, 28 PCU, 77.8 MW; **large atmospheric** (6.48 MN, 33 t, 16.8 MW) = 51 t and useless in vacuum. Small grid per 1 MN: small-grid large hydrogen (480 kN, 1.22 t) = 2.55 t, 25 PCU; small-grid large ion (172.8 kN, 721 kg, 2.4 MW) = 4.2 t, 14 MW. Hydrogen thrust is 4 to 10x lighter than ion per newton and needs no reactor, which is why every design below uses hydrogen main thrusters. Costs: hydrogen needs tanks (`LargeHydrogenTank` 15,000,000 L, 8.16 t, 34.9 k HP; `SmallHydrogenTank` 500,000 L, 1.58 t) and burns them; the sim treats hydrogen thrust as free and fuel as infinite (DefDb.cs:161) so it flatters hydrogen designs. Keep 2-4 ion thrusters (`SmallBlockSmallThrust`, 14.4 kN, 0.2 MW) per axis as a no-fuel vernier if fuel-out must not mean dead stick (they are what the existing drones use).

### 2.4 Gyros

| block | grid | size | mass kg | HP | PCU | torque N*m | power MW | N*m per kg |
|---|---|---|---|---|---|---|---|---|
| `LargeBlockGyro` | L | 1x1x1 | 12817 | 63105 | 50 | 3.36e+07 | 0.01 | 2621.5 |
| `SmallBlockGyro` | S | 1x1x1 | 613.6 | 2793 | 50 | 4.48e+05 | 0.002 | 730.1 |

A large gyro gives 3.6x more torque per kg than a small one (2,621 vs 730 N*m/kg) but costs 50 PCU either way: PCU per N*m favours large gyros 75:1. Torque needed = alpha x I; I of a box ~ M (L^2 + W^2)/12 (see section 4.0).

### 2.5 Power: reactors, engines, batteries, tanks, solar

| block | grid | size | mass kg | HP | PCU | output MW | storage | note |
|---|---|---|---|---|---|---|---|---|
| `LargeBlockBatteryBlock` | L | 1x1x1 | 3845 | 12925 | 15 | 12 (charge 12) | 3 MWh | initial charge 30 %%; XML `CurrentStoredPower` |
| `LargeHydrogenEngine` | L | 1x1x2 | 3253.8 | 13654 | 25 | 5 | hydrogen | burns hydrogen (rate not in data) |
| `LargeBlockSmallGenerator` | L | 1x1x1 | 4793 | 12065 | 25 | 15 | uranium: 1 MWh per kg [wiki] | at full output: 1 kg per - s [wiki] |
| `LargeBlockLargeGenerator` | L | 3x3x3 | 73795 | 147075 | 25 | 300 | uranium: 1 MWh per kg [wiki] | at full output: 1 kg per 12 s [wiki] |
| `LargeBlockSolarPanel` | L | 4x2x1 | 516.8 | 1276 | 55 | 0.16 | - | useless in combat |
| `LargeBlockWindTurbine` | L | 3x3x3 | 616.4 | 1882 | 55 | 0.40 | - | useless in combat |
| `SmallBlockSmallBatteryBlock` | S | 1x1x1 | 146.4 | 562 | 15 | 0.20 (charge 0.20) | 0.05 MWh | initial charge 30 %%; XML `CurrentStoredPower` |
| `SmallBlockBatteryBlock` | S | 3x2x3 | 1040.4 | 3652 | 15 | 4 (charge 4) | 1 MWh | initial charge 30 %%; XML `CurrentStoredPower` |
| `SmallHydrogenEngine` | S | 3x2x2 | 1005.2 | 4141 | 25 | 0.50 | hydrogen | burns hydrogen (rate not in data) |
| `SmallBlockSmallGenerator` | S | 1x1x1 | 278 | 830 | 25 | 0.50 | uranium: 1 MWh per kg [wiki] | at full output: 1 kg per - s [wiki] |
| `SmallBlockLargeGenerator` | S | 3x3x3 | 3901 | 8845 | 25 | 14.8 | uranium: 1 MWh per kg [wiki] | at full output: 1 kg per 244 s [wiki] |
| `SmallBlockSolarPanel` | S | 10x5x1 | 143.2 | 389 | 55 | 0.04 | - | useless in combat |
| `` (hydrogen tank) | L | 1x2x1 | 3161.6 | 12508 | 25 | - | 100,000 L | fills thrusters/engines via conveyors |
| `LargeHydrogenTankSmall` (hydrogen tank) | L | 1x2x1 | 3161.6 | 12508 | 25 | - | 1,000,000 L | fills thrusters/engines via conveyors |
| `LargeHydrogenTank` (hydrogen tank) | L | 3x3x3 | 8161.6 | 34908 | 25 | - | 15,000,000 L | fills thrusters/engines via conveyors |
| `SmallHydrogenTankSmall` (hydrogen tank) | S | 2x2x1 | 109.8 | 454 | 25 | - | 15,000 L | fills thrusters/engines via conveyors |
| `OxygenTankSmall` (hydrogen tank) | S | 3x3x3 | 641.6 | 2538 | 25 | - | 50,000 L | fills thrusters/engines via conveyors |
| `SmallHydrogenTank` (hydrogen tank) | S | 5x5x5 | 1580.8 | 6254 | 25 | - | 500,000 L | fills thrusters/engines via conveyors |

Densities [calc]: large reactor 300 MW / 73.8 t = 4.07 kW/kg and 25 PCU; `LargeBlockSmallGenerator` 15 MW / 4.79 t = 3.13 kW/kg (1x1x1 cell: the easy one to bury in armour and to spread for redundancy); small `SmallBlockLargeGenerator` 14.75 MW / 3.9 t; large battery 3 MWh / 3.85 t = 0.78 kWh/kg, 12 MW. A railgun recharge (below) is the only big steady electric load of a hydrogen-thrust ship, so reactors are sized by railgun count. Uranium inventory: 1 kg = 1 MWh, so 30 minutes at 300 MW is 150 kg; the 'Amount' in the XML is kg of ingots.

### 2.6 Weapons: every non-DLC fixed gun and turret (both grids)

Damage column: `/hit` = block damage per projectile (`ProjectileMassDamage`); `blast` = rocket explosion damage and radius; `pool` = penetrating slug/shell damage budget (`MissileHealthPool`, spent block by block; assault and artillery shells also ricochet at shallow angles, [data] ricochet 40-70 / 50-80 deg, probabilities 0.25-0.85, damage 2,000 / 7,000). **Peak DPS** = damage x rpm/60 straight from the data (pool weapons show `alpha` = pool x shells in the first burst instead, because their rpm only spaces the burst) (gatlings at the file value of 700 rpm; [wiki]/[src] say the game really does 600 rpm, so multiply gatling numbers by 0.857). **Sustained DPS** includes the burst/reload cycle (`ShotsInBurst`, `ReloadTime`) and, for railguns, the capacitor recharge (small 0.016 MWh at 3.6 MW x 0.8 = 20 s; large 0.5 MWh at 38 MW x 0.8 = 59.2 s, [src] docs/sim-design.md section 15.1, wiki-confirmed). Pool DPS is a budget, not an effective rate; one shell can kill several blocks in line. **Range** = ammo `MaxTrajectory`; every turret additionally has a terminal `Range` (MaxRangeMeters) of 800 m (large) / 600 m (small, large interior 600) unless a working AI combat block or a pilot lock raises it to the ammo range ([src] docs/tech-hunter-study.md correction). Bullet range is randomised to 80-100 % of the printed value per shot ([src] W-04). `Cap` = magazines the block's inventory holds (inventory volume / magazine volume); power: guns 0.0002 MW, turrets 0.002 MW idle, railguns recharge draw above ([src] W-10).

| block | grid | size cells | mass kg | HP | PCU | ammo item (cap rounds, kg, L) | damage | speed m/s | range m | rpm | burst / reload s | peak DPS | sustained DPS | inv cap (mags) | turn rad/s |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `LargeInteriorTurret` (InteriorTurret) | L | 1x1x1 | 311 | 1190 | 125 | `RapidFireAutomaticRifleGun_Mag_50rd` (50, 1.50, 0.50) | 150/hit | 600 | 800 | 600 | - / - | 300 | 300 | 768 | 2 |
| `LargeGatlingTurret` (LargeGatlingTurret) | L | 3x3x3 | 1428 | 6070 | 225 | `NATO_25x184mm` (140, 35, 16) | 90/hit | 800 | 800 | 700 | 140 / 4 | 1050 | 791.7 | 24 | 1.50 |
| `LargeMissileTurret` (LargeMissileTurret) | L | 3x3x3 | 1826 | 6960 | 275 | `Missile200mm` (1, 45, 60) | 500 blast r4m | 200 | 800 | 90 | 6 / 4 | 750 | 409.1 | 6 | 1 |
| `LargeBlockMediumCalibreTurret` (LargeMissileTurret) | L | 3x2x3 | 9654 | 41920 | 275 | `MediumCalibreAmmo` (1, 60, 30) | 4000 pool | 500 | 1400 | 180 | 2 / 6 | alpha 8000 | 1263.2 | 6 | 0.40 |
| `LargeCalibreTurret` (LargeMissileTurret) | L | 3x3x3 | 14224 | 62120 | 275 | `LargeCalibreAmmo` (1, 100, 100) | 17000 pool | 500 | 2000 | 80 | 2 / 12 | alpha 34000 | 2666.7 | 6 | 0.20 |
| `LargeMissileLauncher` (SmallMissileLauncher) | L | 1x1x2 | 1713.8 | 6384 | 825 | `Missile200mm` (1, 45, 60) | 500 blast r4m | 200 | 800 | 120 | 19 / 4 | 1000 | 730.8 | 19 | - |
| `LargeBlockLargeCalibreGun` (SmallMissileLauncher) | L | 1x1x4 | 5781 | 27405 | 80 | `LargeCalibreAmmo` (1, 100, 100) | 17000 pool | 500 | 2000 | 80 | 1 / 12 | alpha 17000 | 1416.7 | 3 | - |
| `LargeRailgun` (SmallMissileLauncherReload) | L | 1x2x8 | 14470 | 48950 | 80 | `LargeRailgunAmmo` (1, 60, 40) | 50000 pool | 2000 | 2000 | 20 | 1 / 4 (+2 s delay, +59.2 s charge) | alpha 50000 | 817.0 | 6 | - |
| `SmallGatlingTurret` (LargeGatlingTurret) | S | 5x5x5 | 692 | 2810 | 225 | `NATO_25x184mm` (140, 35, 16) | 90/hit | 800 | 800 | 700 | 140 / 6 | 1050 | 703.3 | 5 | 2 |
| `AutoCannonTurret` (LargeGatlingTurret) | S | 5x4x5 | 870 | 3610 | 225 | `AutocannonClip` (16, 40, 24) | 500/hit | 800 | 800 | 150 | 16 / 4 | 1250 | 800 | 3 | 1 |
| `SmallMissileTurret` (LargeMissileTurret) | S | 5x5x5 | 894 | 3300 | 100 | `Missile200mm` (1, 45, 60) | 500 blast r4m | 200 | 800 | 90 | 2 / 6 | 750 | 150 | 2 | 1.50 |
| `SmallBlockMediumCalibreTurret` (LargeMissileTurret) | S | 7x5x7 | 2254 | 9080 | 275 | `MediumCalibreAmmo` (1, 60, 30) | 4000 pool | 500 | 1400 | 200 | 1 / 6 | alpha 4000 | 666.7 | 3 | 0.80 |
| `SmallGatlingGun` (SmallGatlingGun) | S | 1x1x4 | 148.2 | 621 | 80 | `NATO_25x184mm` (140, 35, 16) | 90/hit | 800 | 800 | 700 | 140 / - | 1050 | 1050 | 4 | - |
| `SmallBlockAutocannon` (SmallGatlingGun) | S | 1x1x5 | 180.2 | 791 | 80 | `AutocannonClip` (16, 40, 24) | 500/hit | 800 | 800 | 150 | 16 / - | 1250 | 1250 | 3 | - |
| `SmallMissileLauncher` (SmallMissileLauncher) | S | 1x1x4 | 226.2 | 771 | 425 | `Missile200mm` (1, 45, 60) | 500 blast r4m | 200 | 800 | 60 | - / - | 500 | 500 | 4 | - |
| `SmallBlockMediumCalibreGun` (SmallMissileLauncherReload) | S | 1x1x9 | 860.2 | 3551 | 80 | `MediumCalibreAmmo` (1, 60, 30) | 4000 pool | 500 | 1400 | 200 | 1 / 6 | alpha 4000 | 666.7 | 3 | - |
| `SmallRocketLauncherReload` (SmallMissileLauncherReload) | S | 1x3x3 | 1058.4 | 3962 | 425 | `Missile200mm` (1, 45, 60) | 500 blast r4m | 200 | 800 | 60 | - / - | 500 | 500 | 4 | - |
| `SmallRailgun` (SmallMissileLauncherReload) | S | 1x2x8 | 1364 | 4080 | 80 | `SmallRailgunAmmo` (1, 10, 8) | 8000 pool | 1000 | 1400 | 20 | 1 / 4 (+0.50 s delay, +20 s charge) | alpha 8000 | 390.2 | 6 | - |

Notes:
- Subtype names: the large gatling turret and large rocket turret and the small fixed gatling have an EMPTY `SubtypeName` in the game (`<SubtypeName />`, seen in the owner's blueprints); in this repo they are keys `LargeGatlingTurret/`, `LargeMissileTurret/`, `SmallGatlingGun/`. The assault-cannon and artillery turrets are `MyObjectBuilder_LargeMissileTurret` blocks with subtype `LargeBlockMediumCalibreTurret` / `LargeCalibreTurret`; the fixed artillery gun is `MyObjectBuilder_SmallMissileLauncher` subtype `LargeBlockLargeCalibreGun`; railguns, the small assault cannon and the reloadable rocket launcher are `MyObjectBuilder_SmallMissileLauncherReload`. `SmallGatlingGun/SmallBlockAutocannon` is the small fixed autocannon.
- There is no large-grid fixed gatling, autocannon or fixed assault cannon in the non-DLC set; large-grid fixed weapons are: `LargeRailgun`, `LargeBlockLargeCalibreGun` (fixed artillery), `LargeMissileLauncher` (19-rocket burst, 825 PCU, awful PCU/DPS) and `LargeFlareLauncher` (cosmetic). The small-grid ones are `SmallGatlingGun`, `SmallBlockAutocannon`, `SmallBlockMediumCalibreGun`, `SmallRailgun`, `SmallMissileLauncher`, `SmallRocketLauncherReload`.
- DLC weapon twins to avoid: `SmallGatlingGunWarfare2`, `SmallMissileLauncherWarfare2`, `LargeGatlingTurretReskin`, `LargeMissileTurretReskin`, `SmallGatlingTurretReskin`, `SmallMissileTurretReskin`, `LargeSearchlight`, `SmallSearchlight`, `Large/SmallBlockWeaponRack`.
- `LargeInteriorTurret` (1x1x1 large, 311 kg, 125 PCU, 600 m): rifle rounds at 0.2 damage multiplier = 30 per hit, 300 DPS peak. It is the cheapest point-defence but weakest per PCU; use only inside hangars/corridors.
- Point defence math [calc] against a 100 m/s kamikaze (`docs/kamikaze-defence.md`): a detected Stinger takes 8 s to cross 800 m. A large gatling turret (peak 900 effective DPS at 600 rpm) kills a ~2,500-HP small drone in ~3 s of hits, so with slew (1.5 rad/s) and misses count on about 1 kill per 8 s per turret. A swarm of 8 needs about 8 gatling turrets with overlapping arcs; that is the frigate/dreadnought gatling count below. A small warhead (15,000 damage, 4.5 m) cannot remove a large heavy block (16,500 HP) in one blast, so large heavy armour makes the capital ships nearly immune to a few kamikazes; small-grid ships (heavy block 560 HP) lose everything inside 4.5 m, so the corvette and fighter rely on decoys + PD + evasion.
- Rockets (`Missile200mm`): 200 m/s, 500 damage, 4 m radius; the audit says occlusion cuts effective rocket damage by ~74 % on small grids ([src] W-01). Prefer gatling/autocannon/railgun/assault cannon for damage per PCU; use rockets only for splash against clustered drones.

### 2.7 Utility, sensing, AI and docking blocks (non-DLC)

| block (type) | grid | size cells | mass kg | HP | PCU | key data |
|---|---|---|---|---|---|---|
| `LargeBasicMission` (BasicMissionBlock) | L | 1x1x1 | 900 | 3460 | 15 | AI block, 0.01 MW |
| `SmallBasicMission` (BasicMissionBlock) | S | 1x1x1 | 156 | 486 | 15 | AI block, 0.01 MW |
| `LargeBlockBeacon` (Beacon) | L | 1x2x1 | 2662 | 10710 | 50 | broadcast up to 200000 m, 0.02 MW |
| `SmallBlockBeacon` (Beacon) | S | 1x1x2 | 84.2 | 306 | 50 | broadcast up to 200000 m, 0.02 MW |
| `LargeBlockBroadcastController` (BroadcastController) | L | 1x1x1 | 344 | 1155 | 25 |  |
| `SmallBlockBroadcastController` (BroadcastController) | S | 1x1x1 | 46.4 | 142 | 25 |  |
| `LargeCameraBlock` (CameraBlock) | L | 1x1x1 | 40.6 | 203 | 25 | raycast cone 45 deg, charge x2.0, 3e-5 MW |
| `SmallCameraBlock` (CameraBlock) | S | 1x1x1 | 40.6 | 203 | 25 | raycast cone 45? deg, charge x2?, 3e-5 MW |
| `LargeBlockSmallContainer` (CargoContainer) | L | 1x1x1 | 648.4 | 2387 | 10 |  |
| `LargeBlockLargeContainer` (CargoContainer) | L | 3x3x3 | 2593.6 | 10233 | 10 |  |
| `SmallBlockSmallContainer` (CargoContainer) | S | 1x1x1 | 49.2 | 121 | 10 |  |
| `SmallBlockMediumContainer` (CargoContainer) | S | 3x3x3 | 274.8 | 919 | 10 |  |
| `SmallBlockLargeContainer` (CargoContainer) | S | 5x5x5 | 626.2 | 2206 | 10 |  |
| `LargeBlockConveyorSorter` (ConveyorSorter) | L | 1x1x1 | 1362 | 5200 | 25 |  |
| `MediumBlockConveyorSorter` (ConveyorSorter) | S | 3x3x1 | 180 | 595 | 25 |  |
| `SmallBlockConveyorSorter` (ConveyorSorter) | S | 1x1x1 | 180 | 595 | 25 |  |
| `LargeDecoy` (Decoy) | L | 1x1x1 | 740 | 3445 | 5 | target priority modifier 10 (turrets shoot decoys first) |
| `SmallDecoy` (Decoy) | S | 1x1x1 | 64.2 | 276 | 5 | target priority modifier 10 (turrets shoot decoys first) |
| `LargeDefensiveCombat` (DefensiveCombatBlock) | L | 1x1x1 | 900 | 3460 | 25 | AI block, 0.01 MW |
| `SmallDefensiveCombat` (DefensiveCombatBlock) | S | 1x1x1 | 156 | 486 | 25 | AI block, 0.01 MW |
| `EventControllerLarge` (EventControllerBlock) | L | 1x1x1 | 304 | 1080 | 10 |  |
| `EventControllerSmall` (EventControllerBlock) | S | 1x1x1 | 38.4 | 127 | 10 |  |
| `LargeFlightMovement` (FlightMovementBlock) | L | 1x1x1 | 900 | 3460 | 25 | AI block, 0.01 MW |
| `SmallFlightMovement` (FlightMovementBlock) | S | 1x1x1 | 156 | 486 | 25 | AI block, 0.01 MW |
| `LargeJumpDrive` (JumpDrive) | L | 3x3x2 | 35980 | 30080 | 80 | 32 MW, jump 2000.0 km, 3 MWh |
| `LargeBlockSmallMagneticPlate` (LandingGear) | L | 1x1x1 | 348 | 1630 | 35 |  |
| `LargeBlockLandingGear` (LandingGear) | L | 1x2x3 | 3304 | 15840 | 35 |  |
| `SmallBlockSmallMagneticPlate` (LandingGear) | S | 1x1x1 | 72 | 270 | 35 |  |
| `SmallBlockLandingGear` (LandingGear) | S | 1x2x3 | 104 | 390 | 35 |  |
| `LargeBlockLaserAntenna` (LaserAntenna) | L | 1x2x1 | 3584 | 8050 | 100 |  |
| `SmallBlockLaserAntenna` (LaserAntenna) | S | 3x3x3 | 666 | 1925 | 100 |  |
| `LargeShipMergeBlock` (MergeBlock) | L | 1x1x1 | 558.4 | 2092 | 100 | strength 0.1, deformation 0.5 |
| `SmallShipSmallMergeBlock` (MergeBlock) | S | 1x1x1 | 92.2 | 346 | 100 | strength 0.005, deformation 0.5 |
| `SmallShipMergeBlock` (MergeBlock) | S | 1x3x3 | 152.2 | 621 | 100 | strength 0.015, deformation 0.5 |
| `LargeProjector` (Projector) | L | 1x1x1 | 526.4 | 2382 | 50 |  |
| `SmallProjector` (Projector) | S | 1x1x1 | 130.4 | 422 | 50 |  |
| `LargeProgrammableBlock` (MyProgrammableBlock) | L | 1x1x1 | 534.4 | 2387 | 100 |  |
| `SmallProgrammableBlock` (MyProgrammableBlock) | S | 1x1x1 | 138.4 | 427 | 100 |  |
| `LargeOffensiveCombat` (OffensiveCombatBlock) | L | 1x1x1 | 900 | 3460 | 25 | AI block, 0.01 MW |
| `SmallOffensiveCombat` (OffensiveCombatBlock) | S | 1x1x1 | 156 | 486 | 25 | AI block, 0.01 MW |
| `LargeOreDetector` (OreDetector) | L | 1x1x2 | 1545 | 6505 | 40 |  |
| `SmallBlockOreDetector` (OreDetector) | S | 1x1x2 | 105.2 | 405 | 40 |  |
| `LargeBlockCompactRadioAntenna` (RadioAntenna) | L | 1x3x1 | 1901.6 | 6858 | 100 |  |
| `LargeBlockRadioAntenna` (RadioAntenna) | L | 1x6x2 | 3401.6 | 12808 | 100 |  |
| `SmallBlockRadioAntenna` (RadioAntenna) | S | 1x1x2 | 84.2 | 306 | 100 |  |
| `LargeBlockSensor` (SensorBlock) | L | 1x1x1 | 134.2 | 470 | 25 | range <= 50 m |
| `SmallBlockSensor` (SensorBlock) | S | 1x1x1 | 134.2 | 470 | 25 | range <= 50 m |
| `Connector` (ShipConnector) | L | 1x1x1 | 3564 | 16720 | 125 |  |
| `LargeBlockInsetConnector` (ShipConnector) | L | 1x1x1 | 3564 | 16720 | 125 |  |
| `LargeBlockInsetConnectorSmall` (ShipConnector) | L | 1x1x1 | 3564 | 16720 | 125 |  |
| `LargeBlockStructural_PlatformConnector` (ShipConnector) | L | 5x1x5 | 10564 | 51720 | 125 |  |
| `ConnectorSmall` (ShipConnector) | S | 1x1x1 | 204.8 | 894 | 125 |  |
| `SmallBlockInsetConnector` (ShipConnector) | S | 1x1x1 | 204.8 | 894 | 125 |  |
| `ConnectorMedium` (ShipConnector) | S | 3x3x2 | 685.2 | 2796 | 125 |  |
| `SmallBlockInsetConnectorMedium` (ShipConnector) | S | 3x3x2 | 685.2 | 2796 | 125 |  |
| `LargeShipGrinder` (ShipGrinder) | L | 1x1x2 | 761.4 | 3122 | 100 | radius 1.4 m |
| `SmallShipGrinder` (ShipGrinder) | S | 3x3x6 | 488.4 | 1932 | 100 | radius 1.3 m |
| `LargeShipWelder` (ShipWelder) | L | 1x1x2 | 713.4 | 3042 | 150 | work radius 2.26 m (tip has no mount) |
| `SmallShipWelder` (ShipWelder) | S | 3x3x6 | 448.4 | 1882 | 150 | work radius 1.3 m (tip has no mount) |
| `TimerBlockLarge` (TimerBlock) | L | 1x1x1 | 259 | 995 | 25 |  |
| `TimerBlockSmall` (TimerBlock) | S | 1x1x1 | 30.2 | 121 | 25 |  |
| `LargeBlockTransponder` (TransponderBlock) | L | 1x1x1 | 802 | 3685 | 25 |  |
| `SmallBlockTransponder` (TransponderBlock) | S | 1x1x1 | 84.4 | 377 | 25 |  |
| `LargeTurretControlBlock` (TurretControlBlock) | L | 1x1x1 | 948 | 3490 | 100 | Range 800 m, idle 0.0005 MW; wires turrets/cameras via ToolIds |
| `SmallTurretControlBlock` (TurretControlBlock) | S | 1x1x2 | 250 | 871 | 100 | Range 600 m, idle 0.0005 MW; wires turrets/cameras via ToolIds |
| `LargeWarhead` (Warhead) | L | 1x1x1 | 700.4 | 2932 | 100 | 15000 dmg r 22.4 m |
| `SmallWarhead` (Warhead) | S | 1x1x1 | 106.2 | 486 | 50 | 15000 dmg r 4.5 m |

Docking notes [data]: connectors are 1x1x1 (large `Connector` family 3,564 kg, 125 PCU) or 3x3x2 cells small `ConnectorMedium`; a SMALL drone docks to a LARGE ship with `LargeBlockInsetConnectorSmall` (large-grid block, small connector, used in the owner's `Small Grid 8000` sample). Merge blocks: `SmallShipSmallMergeBlock` (strength 0.005, the drones' block), `SmallShipMergeBlock`, `LargeShipMergeBlock` (strength 0.1); merge needs same grid size on both sides. Landing gear `LargeBlockLandingGear` 3,304 kg, `SmallBlockSmallMagneticPlate` 72 kg. Projector 50 PCU, welder 150 PCU each: a 4-welder bay costs 650 PCU before the drone's own blocks (each projected block +1 PCU while projected). Jump drives (`LargeJumpDrive` 32 MW, 36 t, 80 PCU) exist but the owner's goal is combat, skip them.

### 2.8 Style, interior and comfort blocks (non-DLC)

| class | blocks (large subtype; small = `Small` prefix where it exists) | note |
|---|---|---|
| windows (glass, 135-12,360 HP) | `Window1x1Flat/FlatInv/Slope/Side/SideInv/Face/Inv`, `Window1x2*` (Flat, Face, Slope, SideLeft/Right, Inv forms), `Window2x3Flat(Inv)`, `Window3x3Flat(Inv)`, `WindowRound*` (Corner, Face, Inv, InwardsCorner), `LargeWindowSquare` (480 HP), `LargeWindowEdge` | small versions `SmallWindow*`. Mount areas are only 10-20 % of a face: they attach to a cube/slope beside them but nothing attaches onto them. `Window1x1Slope` continues a slope line with glass; `Window3x3Flat` = bridge wall; `WindowRoundFace` for cockpit blisters. |
| lights | `SmallLight` (60 HP, 20 PCU), `LargeBlockLight_1corner/_2corner`, `SmallBlockSmallLight`, `SmallBlockLight_1/2corner`, `LargeBlockFrontLight` (reflector, 450 kg, 25 PCU), `SmallBlockFrontLight` (86 kg) | lights are 20-25 PCU each: 40 lights = 1,000 PCU, budget them |
| doors, hangar | `Door` (large 582 kg, 115 PCU), `SmallDoor` (5x4x1 cells), `LargeBlockSlideDoor` (AirtightSlideDoor 115 PCU), `AirtightHangarDoor` (1x3x1 cells, 7.9 t, 37 k HP, 115 PCU) | hangar-door-only; `AirtightHangarDoorWarfare2*` is DLC |
| walkable/interior | `LargeStairs`, `LargeStairsShort`, `LargeRamp`, `LargeSteelCatwalk*`, `LargeBlockInteriorWall`, `LargeInteriorPillar`, `LadderShaft`, `LargeBlockStructural_Frame/Platform/SupportBeam3x3-5x5` | frames/beams are 3-5 cells wide with tiny mount area: good for greebles/hangar trusses |
| displays | `LargeLCDPanel`, `LargeLCDPanelWide`, `LargeBlockCorner_LCD_1`, `SmallLCDPanel`, `SmallLCDPanelWide`, text panels (45 PCU each) | 45 PCU each: 4 per ship is plenty |
| decals/letters | `LargeSymbol0-9,A-Z` (80 kg, 400 HP, 1 PCU) and `SmallSymbol*` | hull numbers, 1 PCU |
| anything with `Statue`, `DeadAstronaut`, `Plushie`, `Monolith`, `Stereolith`, `Warning sign` | skip | some are `public=False` |

## 3. Inventory pre-loading (ammunition, uranium, hydrogen, charge)

### 3.1 What the repo and the samples show [data]
- The audit statement is right for the generator: `tools/sebp.py` has no way to put items in an inventory; `shapes.json` containers are always `<Items />`. 40 shape keys exist, and none for `LargeMissileTurret` (assault/artillery/rocket turrets), `LargeGatlingTurret`, `Reactor`, `HydrogenEngine`, `OxygenTank`, `CargoContainer`, `Cockpit`, `Decoy`, `SensorBlock`. Those fall to `GENERIC` (no `ComponentContainer`, no turret tail), which is an incomplete block for a warship. `tools/extract_shapes.py` `WANT` must be extended with these types and re-run on `ref/user-samples/Starburst - v2/bp.sbc` (has all the large turrets, reactor, containers, cockpits), `Red Ship`, `Venture - Expedition End` (large gatling, hydrogen engine, tanks) and `Longbow L`.
- The owner's game-written files contain real, loaded inventories: 36 samples + Tech Hunter. Counting `MyObjectBuilder_InventoryItem` by `PhysicalContent` type: AmmoMagazine 137, Component 703, Ingot 914, Ore 1,592, PhysicalGunObject 20, ConsumableItem 9, OxygenContainerObject 2. **Tech Hunter itself has every inventory empty** (24 gatling, 36 assault, 16 artillery turrets, 132 welders, reactor all `<Items />`), i.e. the enemy warship has no ammo or fuel in the file; our four ships can be better than that.
- Inventory capacity is per block and is the `InventoryMaxVolume` (m^3) of the definition, written as `<Volume>` in the inventory. Magazine volumes in `defs_stats.json` are litres, so *magazines that fit* = floor(1000 x Volume / magazine litres). It reproduces the samples (`LargeBlockLargeCalibreGun` 0.3 m3 / 100 L = 3 = the sample's `<Amount>3`; `SmallRailgun` 0.048 / 8 L = 6).
- `<Mass>9223372036854.775807</Mass>` (= long.MaxValue / 1e6) and `<MaxItemCount>2147483647</MaxItemCount>` are the game's "unlimited" markers; copy them verbatim. `InventoryFlags` is `CanReceive` for guns/reactors, `CanSend` for welders/connectors, `CanReceive CanSend` for cargo.

### 3.2 Exact XML shapes (copy-paste; ids elided)
`PhysicalContent` types and subtypes: `MyObjectBuilder_AmmoMagazine` (+ `<ProjectilesCount>0</ProjectilesCount>`), `MyObjectBuilder_Ingot` (Uranium), `MyObjectBuilder_Component`. The inventory `Amount` of a magazine is a count of magazines; of an ingot it is kg (fractional allowed).

**Large gatling turret** (`MyObjectBuilder_LargeGatlingTurret`, `<SubtypeName />`; Venture - Expedition End holds 12 boxes; capacity 24):
```xml
<ComponentContainer><Components><ComponentData>
  <TypeId>MyInventoryBase</TypeId>
  <Component xsi:type="MyObjectBuilder_Inventory">
    <CanPutItems>true</CanPutItems>
    <Items>
      <MyObjectBuilder_InventoryItem>
        <Amount>24</Amount>
        <PhysicalContent xsi:type="MyObjectBuilder_AmmoMagazine">
          <SubtypeName>NATO_25x184mm</SubtypeName>
          <ProjectilesCount>0</ProjectilesCount>
        </PhysicalContent>
        <ItemId>0</ItemId>
      </MyObjectBuilder_InventoryItem>
    </Items>
    <nextItemId>1</nextItemId>
    <Volume>0.384</Volume>
    <Mass>9223372036854.775807</Mass>
    <MaxItemCount>2147483647</MaxItemCount>
    <Size xsi:nil="true" />
    <InventoryFlags>CanReceive</InventoryFlags>
    <RemoveEntityOnEmpty>false</RemoveEntityOnEmpty>
  </Component>
</ComponentData>
<ComponentData><TypeId>MyTimerComponent</TypeId>  <!-- the game adds this to large turrets and reactors; keep it -->
  <Component xsi:type="MyObjectBuilder_TimerComponent"><Repeat>true</Repeat><TimeToEvent>0</TimeToEvent><SetTimeMinutes>0</SetTimeMinutes>
  <TimerEnabled>true</TimerEnabled><RemoveEntityOnTimer>false</RemoveEntityOnTimer><TimerType>Frame10</TimerType>
  <FramesFromLastTrigger>0</FramesFromLastTrigger><TimerTickInFrames>10</TimerTickInFrames><IsSessionUpdateEnabled>false</IsSessionUpdateEnabled></Component>
</ComponentData></Components></ComponentContainer>
```
**Large artillery turret** (`MyObjectBuilder_LargeMissileTurret` / `LargeCalibreTurret`, Starburst - v2): same shape with `<SubtypeName>LargeCalibreAmmo</SubtypeName>`, `<Amount>2</Amount>` (capacity 6), `<Volume>0.6</Volume>`, and in the block tail `<GunBase><CurrentAmmoMagazineName>LargeCalibreAmmo</CurrentAmmoMagazineName><RemainingAmmosList /><LastShootTime>0</LastShootTime></GunBase>`, `<Range>800</Range>`, `<RemainingAmmo>0</RemainingAmmo>`. The game itself writes `RemainingAmmo 0` with 2 magazines in the inventory, so the magazines are loaded into the gun on first use. **Assault-cannon turret** (`LargeBlockMediumCalibreTurret`): `MediumCalibreAmmo`, `<Volume>0.18</Volume>`, Red Ship holds 4 (capacity 6). **Rocket turret** (`LargeMissileTurret`, empty subtype): `Missile200mm`, `<Volume>0.384</Volume>`, capacity 6.

**Fixed small gatling** (Pond Hopper Mk2; `<SubtypeName />`, capacity 4; the game writes the gun tail after a few shots, a fresh gun has the `GunBase` of shapes.json):
```xml
<Items><MyObjectBuilder_InventoryItem><Amount>4</Amount>
  <PhysicalContent xsi:type="MyObjectBuilder_AmmoMagazine"><SubtypeName>NATO_25x184mm</SubtypeName><ProjectilesCount>0</ProjectilesCount></PhysicalContent>
  <ItemId>0</ItemId></MyObjectBuilder_InventoryItem></Items>
<nextItemId>1</nextItemId><Volume>0.064</Volume>
...
<GunBase><RemainingAmmo>127</RemainingAmmo><RemainingMagazines>4</RemainingMagazines><CurrentAmmoMagazineName>NATO_25x184mm</CurrentAmmoMagazineName>
  <RemainingAmmosList><RemainingAmmoIns SubtypeName="NATO_25x184mm" Amount="0" /></RemainingAmmosList><LastShootTime>638921342101772910</LastShootTime></GunBase>
```
(`RemainingAmmo` 127 and `Amount="0"` are mid-fight values. For a fresh block write `<RemainingAmmosList />` and no RemainingAmmo.) **Autocannon** `SmallBlockAutocannon`: item `AutocannonClip`, `<Volume>0.072</Volume>`, capacity 3 (the sample autocannons are empty).

**Small railgun** (Starburst - v4; capacity 6): inventory item `SmallRailgunAmmo` Amount 3, `<Volume>0.048</Volume>`, **plus the capacitor component** the game writes full:
```xml
<ComponentData><TypeId>MyEntityCapacitorComponent</TypeId>
  <Component xsi:type="MyObjectBuilder_EntityCapacitorComponent"><StoredPower>0.016</StoredPower></Component></ComponentData>
```
and in the tail `<UseConveyorSystem>true</UseConveyorSystem>` and `<GunBase><RemainingAmmo>1</RemainingAmmo><RemainingMagazines>3</RemainingMagazines><CurrentAmmoMagazineName>SmallRailgunAmmo</CurrentAmmoMagazineName>...`. **Large railgun** (`plagarism` sample, written by the game, empty): `<Volume>0.24</Volume>`, capacitor `<StoredPower>0.5</StoredPower>`, `CurrentAmmoMagazineName LargeRailgunAmmo`; capacity 6 slugs (60 kg each). Item shape for the slugs is by analogy with the small railgun (`<SubtypeName>LargeRailgunAmmo</SubtypeName>`), [rec, high] since no loaded large railgun exists in the samples. The docs/audit finding that the Longbow L's large railgun lacks the capacitor stays valid: the shape above is the fix.

**Reactor** (Red Ship; large `LargeBlockLargeGenerator` `<Volume>8</Volume>`, small `<Volume>0.125</Volume>`; `LargeBlockSmallGenerator` 1, `SmallBlockLargeGenerator` 1 [data InventorySize]):
```xml
<Items><MyObjectBuilder_InventoryItem><Amount>104.682732</Amount>
  <PhysicalContent xsi:type="MyObjectBuilder_Ingot"><SubtypeName>Uranium</SubtypeName></PhysicalContent>
  <ItemId>0</ItemId></MyObjectBuilder_InventoryItem></Items>
...
<Enabled>true</Enabled>
<Capacity>104.682739</Capacity>      <!-- block tail after Enabled: equals the uranium kg (both Red Ship reactors: 104.68/104.68 and 45.95/45.95) -->
```
Uranium: 1 kg = 1 MWh [wiki], so the Amount in kg is also the MWh of fuel; 300 MW full-load 30 min = 150 kg. The definition has `FuelPullAmountFromConveyorInMinutes 10`: a conveyor-fed reactor pulls only ~10 min of fuel at a time, so for a fight without conveyors pre-load the whole budget.

**Hydrogen engine** (Venture, Tech Hunter, Pod Chaser): **no inventory**; tail after `<Enabled>` is `<Capacity>100002.438</Capacity>` (large; 6 of 15 engines in Tech Hunter show exactly 100000, i.e. full) or `<Capacity>5000.56543</Capacity>` (small). It is the engine's internal hydrogen in litres; it also draws from tanks. **Hydrogen tank** (`MyObjectBuilder_OxygenTank`): tail `<IsStockpiling>false</IsStockpiling><FilledRatio>1</FilledRatio><AutoRefill>false</AutoRefill>` (Small Grid 8269 writes FilledRatio 1; Red Ship tanks 0.0462); FilledRatio 1 = full tank (15,000,000 L large). Thrusters have no stored fuel. **Battery**: `<CurrentStoredPower>3</CurrentStoredPower><ProducerEnabled>true</ProducerEnabled><MaxStoredPower>3</MaxStoredPower>...<ChargeMode>0</ChargeMode>` (sebp's shape writes `MaxStoredPower 0`; the sim reads CurrentStoredPower, BlueprintLoader/BlockApiFactory.cs:99).

**Cargo container** (`MyObjectBuilder_CargoContainer`, `LargeBlockLargeContainer`, 3x3x3 cells, `<Volume>421.875008</Volume>`; `LargeBlockSmallContainer` 15.625; small `SmallBlockSmallContainer` 0.125, `SmallBlockMediumContainer` 3.375, `SmallBlockLargeContainer` 15.625 [calc: cells x cell volume]). Red Ship has a real combat reserve:
```xml
<Items>
  <MyObjectBuilder_InventoryItem><Amount>51</Amount><PhysicalContent xsi:type="MyObjectBuilder_AmmoMagazine"><SubtypeName>LargeCalibreAmmo</SubtypeName><ProjectilesCount>0</ProjectilesCount></PhysicalContent><ItemId>0</ItemId></MyObjectBuilder_InventoryItem>
  <MyObjectBuilder_InventoryItem><Amount>1</Amount><PhysicalContent xsi:type="MyObjectBuilder_AmmoMagazine"><SubtypeName>AutocannonClip</SubtypeName><ProjectilesCount>0</ProjectilesCount></PhysicalContent><ItemId>1</ItemId></MyObjectBuilder_InventoryItem>
  <MyObjectBuilder_InventoryItem><Amount>33</Amount><PhysicalContent xsi:type="MyObjectBuilder_Component"><SubtypeName>PowerCell</SubtypeName></PhysicalContent><ItemId>3</ItemId></MyObjectBuilder_InventoryItem>
</Items>
<nextItemId>..</nextItemId><Volume>421.875008</Volume><Mass>9223372036854.775807</Mass><MaxItemCount>2147483647</MaxItemCount>
<InventoryFlags>CanReceive CanSend</InventoryFlags>
```
Ammo item subtypes that exist [data]: `NATO_25x184mm` (gatling, 140 rds, 35 kg, 16 L), `AutocannonClip` (16 rds, 40 kg, 24 L), `MediumCalibreAmmo` (assault cannon, 60 kg, 30 L), `LargeCalibreAmmo` (artillery, 100 kg, 100 L), `Missile200mm` (45 kg, 60 L), `SmallRailgunAmmo` (10 kg, 8 L), `LargeRailgunAmmo` (60 kg, 40 L); `NATO_5p56x45mm` and the `*RifleGun_Mag*` items only feed the interior turret.
**Welder / PB**: welders have an empty `<Items />` with `<Volume>15.625</Volume>` (large) and `CanSend`; the PB has no inventory (its state is `<Program>` and `<Storage>`). Components for welding go into cargo/welders as `MyObjectBuilder_Component` (`SteelPlate`, `Computer`, `Motor`, `Construction`, `InteriorPlate`, `SmallTube`, `PowerCell`, `Thrust`, ... all seen in Venture/Red Ship).

### 3.3 Recommended pre-load per block (full magazines the inventory can hold)
| block | mags held | mass kg | what it buys at the data rate |
|---|---|---|---|
| `LargeGatlingTurret` | 24 x NATO_25x184mm = 3,360 rds | 840 | 336 s of firing at 600 rpm (1 box per 14 s) |
| `SmallGatlingGun` | 4 = 560 rds | 140 | 56 s |
| `SmallGatlingTurret` | 5 = 700 rds | 175 | 70 s |
| `SmallBlockAutocannon` | 3 clips = 48 rds | 120 | 19 s of 2.5 rds/s (!) |
| `SmallBlockMediumCalibreGun` / turret | 3 = 3 shells | 180 | 3 shots (!) |
| `LargeBlockMediumCalibreTurret` | 6 | 360 | 6 shells = 3 salvos, ~19 s |
| `LargeCalibreTurret` | 6 | 600 | 3 salvos of 2, ~38 s |
| `LargeRailgun` | 6 | 360 | 6 shots = 6 min at the 61 s cycle |
| `SmallRailgun` | 6 | 60 | 6 shots = 2 min |
| `LargeMissileTurret` | 6 | 270 | 6 rockets, ~7 s |
| `LargeBlockSmallGenerator` etc. reactor | kg uranium = MWh needed | see section 4 | |

Conclusion [calc]: **inventory alone cannot sustain fire from artillery, assault cannons or autocannons**; the inventories are 3-6 rounds deep. Pre-loading is enough for railguns and gatlings only. The other weapons need a conveyor chain from cargo containers holding the reserve (turrets pull ammo through the conveyor system by default; `UseConveyorSystem` true on the weapons that have it). So the generator must also place `LargeBlockConveyor`/`ConveyorTube*` pieces touching the turret bases and cargo, and ConveyorSorters if wanted; the conveyor port positions are not in defs.json (the `mounts` list covers structure only), so this is unresolved: copy the conveyor trunk layout from the Starburst/Red Ship samples (they have `ConveyorTube`s attached to turrets) [rec, medium].

### 3.4 What the sim does with it (read sim/src)
- `BlueprintLoader.cs` parses `ComponentContainer` only through `ScanForStoredPower` (railgun capacitor `StoredPower`); `Items`, `Amount`, `FilledRatio`, `Capacity` of reactors/engines are ignored (`Props` keeps only leaf elements outside the container).
- `Weapons.cs:105-113` `InitAmmo`: with `SimSettings.InfiniteAmmo = true` (default) ammo is never consumed. With `--infinite-ammo false` the magazine count is `floor(InventoryVolume*1000 / MagazineVolume)`, i.e. the sim assumes a FULL inventory regardless of what the blueprint says, loads one at spawn and swaps with `ReloadTimeMs`. So **pre-loaded ammo would be ignored (honoured only as "full") in both modes.** A blueprint with 3 magazines in a 6-slot inventory still fights with 6 in the sim.
- Fuel: reactors/engines are constant output, "fuel not modelled" (`DefDb.cs:23`, `PowerSystem.cs`); hydrogen thrusters are free (`DefDb.cs:161`); batteries do discharge (`PowerSystem.cs`) and read `CurrentStoredPower`. Conveyor resupply is not modelled at all.
- To honour pre-loads the loader would have to read `Items` per block and `InitAmmo` use `min(that, capacity)`; for uranium/hydrogen a fuel counter with `1 kg = 1 MWh` and `642.7 L/s per MW`. About 40 lines in `BlueprintLoader.cs` + `Weapons.cs`, none needed for the decision below.

### 3.5 Generator patch (tested in the scratchpad, not applied)
`inv_patch.py` (scratchpad) monkey-patches `Blueprint._components_xml` to fill the `Items` of the first `MyInventoryBase` component from a per-block list `[(kind, subtype, amount)]` and sets `nextItemId`; the output for a fixed small gatling with 4 NATO boxes is character-identical in structure to Pond Hopper Mk2's game-written block (shown above): `Amount, PhysicalContent(xsi:type, SubtypeName, ProjectilesCount), ItemId`, then `nextItemId`. The real patch should be a new `add(..., items=[...])` argument (the scratch version leaked the list into the XML as a `<__items>` tag because it rode on `settings`), `ItemId` = list index, `nextItemId` = len(items), and `Capacity` for reactors set to the uranium total. Steps: (1) extend `extract_shapes.py` WANT and regenerate `shapes.json` for the missing types; (2) add `items`; (3) add `FilledRatio`/`CurrentStoredPower` overrides through the existing `settings=` mechanism; (4) add a validator check `sum(volume of items) <= block Volume` using the table in 3.3. In-game test needed: paste a ship with the items and read `RemainingAmmo` (packages/tests T8).

## 4. Recommended loadouts and budgets

### 4.0 Method, assumptions, targets
- Everything below is [calc] from `defs_stats.json` by `designs.py`/`part5.py` (scratchpad); no simulation was run (the sim cannot model most of this, section 6; per CLAUDE.md I ran 0 simulations: nothing here needed one). Rounded to sensible numbers; every block is non-DLC, non-Prototech.
- **Space, no gravity** (the project's rule, `packages/unknowns.md`). In a gravity well, hovering needs F >= M x 9.81 N: the frigate's 4 lift thrusters give 1.93 m/s2, the dreadnought's 1.09: neither can leave a planet. Add 5-6x thrust if planetary operation is wanted.
- **Hull model.** Outer shell = 0.62 x bounding-box surface area (rounded hull factor, guess), `layers` cells thick, mixed heavy/light blocks, average block mass: small light 20 kg / heavy 85 kg (slopes and corners are lighter than cubes), large light 380 / heavy 2,000 kg. HP = 5 HP/kg of armour [data]. Interior fill/decks/conveyors are NOT armour: the `fixed` mass below holds all functional blocks, ammo and 30 min of consumables.
- **Thrust.** Hydrogen thrusters sized per axis for the target acceleration at the final mass (iterated until the mass converges): `big` = small-grid large H2 (480 kN) or large-grid large H2 (7.2 MN); `small` = 98.4 kN / 1.08 MN. **Gyros**: torque = alpha x I, I = M (L^2 + W^2)/12 about the vertical axis (box approximation; pitch is a bit smaller for flat ships).
- **Fuel/energy for 30 min of combat**: hydrogen = 1,800 s x (25 % duty on the forward group + 10 % on the others) x 642.7 L/s per MW [calc from wiki table]; electricity = railgun recharge draw (3.6 MW small, 38 MW large, from the capacitor data) at 60 % duty + 50 % gyro duty + base load; uranium kg = MWh over 30 min.
- **PCU**: sum of block PCU (1 per armour block, 12 per thruster, 50 per gyro, ...). Worlds: [wiki] default max PCU is 100,000; the owner's creative world is "50,000-ish" (the Tech Hunter alone is 77,896 PCU by `defs_stats.json`, so it only exists in a larger world); a survival world with an admin limit can be as low as 20,000 [wiki] example. Fractions of 50 k are given.

### 4.1 Summary table

| | Fighter | Corvette | Frigate | Dreadnought |
|---|---|---|---|---|
| grid / length x beam x height (m) | Small / 14 x 7 x 4.5 | Small / 36 x 14 x 8 | Large / 75 x 25 x 15 | Large / 200 x 60 x 35 |
| cells (L x W x H) | 28 x 14 x 9 | 72 x 28 x 16 | 30 x 10 x 6 | 80 x 24 x 14 |
| total mass (t) | 65.7 | 257 | 2,147 | 13,231 |
| armour mass / share | 40.8 t / 62 % | 192 t / 74 % | 1,765 t / 82 % | 11,033 t / 83 % |
| armour blocks heavy + light (layers) | 168 + 1,327 (1.0) | 789 + 6,230 (1.0) | 750 + 697 (1.5) | 4,689 + 4,355 (1.5) |
| armour HP | 0.20 M | 0.96 M | 8.82 M | 55.16 M |
| weapons: sustained DPS (data rates) | 5,267 | 8,093 | 18,356 | 58,870 |
| weapons: first-volley alpha (pool damage) | 4,000 | 28,000 | 200,000 | 668,000 |
| longest reach (m) | 1,400 | 1,400 | 2,000 | 2,000 |
| thrust fwd / other axes (MN) | 1.06 / 0.58 | 2.12 / 1.06 | 7.20 / 3.24 | 27.00 / 14.40 |
| acceleration fwd / other (m/s2) | 16.1 / 8.8 | 8.2 / 4.1 | 3.4 / 1.5 | 2.0 / 1.1 |
| thrusters (count) | 13 | 21 | 16 | 18 |
| gyros (angular accel target) | 6 (2.00 rad/s2 = 115 deg/s2) | 22 (0.30 rad/s2 = 17 deg/s2) | 5 (0.15 rad/s2 = 9 deg/s2) | 86 (0.06 rad/s2 = 3 deg/s2) |
| generation MW / batteries MWh | 1 / 2 | 14.8 / 4 | 90 / 24 | 420 / 60 |
| PCU total (% of 50 k) | 3,266 (7 %) | 12,051 (24 %) | 9,664 (19 %) | 34,575 (69 %) |
| ingots (Iron t, total all kinds t) | 69.1 / 72.0 | 270 / 277 | 2,241 / 2,278 | 13,869 / 14,042 |

(Ingot cost = sum of each block's `ingot_cost` + 1.07 kg Fe per kg armour; real survival cost also needs Silicon, Cobalt, Nickel, Silver, Gold, Platinum, see per-ship note.) The Fighter at 66 t is bigger than the user's Fighter Ac (34 t, 6.5 x 4.5 x 11 m, 524 blocks); drop `layers` to 0.6 for a 45 t racer. A 100 % skin everywhere is a design target, not a requirement: armour mass is the adjustable slack.

### 4.2 Fighter

**Role**: single-pilot dogfighter and drone killer, also scriptable (Helm PB reusing the Hornet loops). Hull: wedge nose, canopy forward of centre, two nose cannons, dorsal gatling for point defence. No drone bay (allowed); a belly `ConnectorSmall` lets it dock to a frigate/dreadnought `LargeBlockInsetConnectorSmall`.

**Weapons**: 2 x `SmallBlockAutocannon` (500 dmg x 2.5/s = 1,250 DPS each, 800 m, 80 PCU, 180 kg: the best close-range DPS per PCU in the data) + 1 x `SmallBlockMediumCalibreGun` (assault cannon, 1,400 m, 4,000-pool shell for penetration, 80 PCU) + 2 x `SmallGatlingGun` (1,050 data DPS each, 800 m; anti-drone). 400 PCU, 1.5 t, 5,267 sustained data DPS (a Hornet has 2,100). Magazines are the limit: 3 autocannon clips = 19 s of fire and 3 assault shells, so it is a 20-40 s strike craft unless a conveyor feeds it from its `SmallBlockSmallContainer`.

**Thrusters** (hydrogen, small grid): forward 2 x `SmallBlockLargeHydrogenThrust` + 1 x `SmallBlockSmallHydrogenThrust` = 1.06 MN (16.1 m/s2); each other axis 1 large + 1 small = 0.58 MN (8.8 m/s2); 13 thrusters, 10.6 t, 156 PCU. Hydrogen for 30 min at 25 % / 10 % duty is 0.80 ML against 1.0 ML in 2 x `SmallHydrogenTank` (3.2 t); a continuous forward burn (852 L/s) empties them in 19 min. Optional vernier: swap the two small up/down thrusters for `SmallBlockSmallThrust` ion (14.4 kN each) so fuel-out is not dead stick.

**Gyros**: 6 x `SmallBlockGyro` (2.69 MN*m, 2.0 rad/s2 about I = 1.3e6 kg*m2, 3.7 t, 300 PCU). The Hornet's single gyro turns a 4.4 t drone; a 66 t fighter needs six. The sim's idle gyro damping is 4.7x too weak (audit), so the fighter will look twitchier there than in game.

**Power**: 2 x `SmallBlockSmallGenerator` (1 MW total, 556 kg, 0.5 kg uranium/h each) + 2 x `SmallBlockBatteryBlock` (2 MWh, 8 MW); the load is ~0.2 MW, no railgun on this class (its 3.6 MW recharge would dominate). Option: swap the assault gun for a `SmallRailgun` (1,400 m, 8,000 pool, 390 sustained DPS) and add one `SmallBlockLargeGenerator` (14.75 MW, 3.9 t).

**Block list** (`mass` and `PCU` are totals for the line):

| block | n | mass kg | PCU |
|---|---|---|---|
| `DBSmallBlockFighterCockpit` | 1 | 1,325 | 150 |
| `SmallBlockBatteryBlock` | 2 | 2,081 | 30 |
| `SmallBlockSmallGenerator` | 2 | 556 | 50 |
| `SmallHydrogenTank` | 2 | 3,162 | 50 |
| `SmallBlockAutocannon` | 2 | 360 | 160 |
| `SmallGatlingGun` | 2 | 296 | 160 |
| `SmallBlockMediumCalibreGun` | 1 | 860 | 80 |
| `SmallProgrammableBlock` | 1 | 138 | 100 |
| `SmallBlockRadioAntenna` | 1 | 84 | 100 |
| `SmallCameraBlock` | 3 | 122 | 75 |
| `SmallDecoy` | 2 | 128 | 10 |
| `SmallOffensiveCombat` | 1 | 156 | 25 |
| `SmallFlightMovement` | 1 | 156 | 25 |
| `EventControllerSmall` | 1 | 38 | 10 |
| `TimerBlockSmall` | 1 | 30 | 25 |
| `ConnectorSmall` | 1 | 205 | 125 |
| `SmallBlockFrontLight` | 2 | 172 | 50 |
| `SmallBlockSmallContainer` | 1 | 49 | 10 |
| `SmallBlockSmallLight` | 4 | 64 | 80 |
| thrusters fwd: `SmallBlockLargeHydrogenThrust` x2 + `SmallBlockSmallHydrogenThrust` x1 (1.06 MN, 16.1 m/s2) | 3 | 2,778 | 36 |
| thrusters back: `SmallBlockLargeHydrogenThrust` x1 + `SmallBlockSmallHydrogenThrust` x1 (0.58 MN, 8.8 m/s2) | 2 | 1,556 | 24 |
| thrusters up: `SmallBlockLargeHydrogenThrust` x1 + `SmallBlockSmallHydrogenThrust` x1 (0.58 MN, 8.8 m/s2) | 2 | 1,556 | 24 |
| thrusters down: `SmallBlockLargeHydrogenThrust` x1 + `SmallBlockSmallHydrogenThrust` x1 (0.58 MN, 8.8 m/s2) | 2 | 1,556 | 24 |
| thrusters left: `SmallBlockLargeHydrogenThrust` x1 + `SmallBlockSmallHydrogenThrust` x1 (0.58 MN, 8.8 m/s2) | 2 | 1,556 | 24 |
| thrusters right: `SmallBlockLargeHydrogenThrust` x1 + `SmallBlockSmallHydrogenThrust` x1 (0.58 MN, 8.8 m/s2) | 2 | 1,556 | 24 |
| gyros `SmallBlockGyro` | 6 | 3,682 | 300 |
| armour: 168 heavy + 1,327 light blocks, 1.0 layers | 1,495 | 40,818 | 1,495 |
| ammunition pre-load (see 3.3), kg | - | 700 | 0 |
| **total** | | **65,741** | **3,266** |

Budgets: electric peak 0.2 MW (railguns 0.0 + gyros 0.01 + base), generation 1 MW, battery 2 MWh / 8 MW; hydrogen needed 0.80 ML for 30 min vs 1.0 ML carried; PCU 3,266 of 50,000 (7 %) and of 100,000 (3 %); armour HP 0.20 M (3,104 HP per tonne of ship).

### 4.3 Corvette

**Role**: escort/raider with two docked Hornet-class drones; manual from the bridge cockpit, scripted by a Helm PB + Staff PB (the docs/audit/timers_pbs.md section 7 architecture; control layer 400-550 PCU: 2 PBs 200, 3 timers 75, 3 event controllers 30, 2 AI blocks 50, turret control 100).

**Weapons**: 2 x `SmallRailgun` (1,400 m, 8,000-pool slugs at 1,000 m/s; 80 PCU, 1.36 t each) + 2 fixed `SmallBlockMediumCalibreGun` + 1 `SmallBlockMediumCalibreTurret` (1,400 m, 4,000-pool) + 2 `SmallBlockAutocannon` (close DPS) + 4 `SmallGatlingTurret` as 360-degree point defence (703 sustained DPS, 225 PCU, 692 kg each). 8,093 sustained data DPS, 28,000 alpha, weapons 9.8 t and 1,655 PCU. Decoys: 4 x `SmallDecoy` (276 HP, 5 PCU): turrets pick decoys first, the cheapest HP-per-PCU in the game.

**Thrusters**: forward 4 x `SmallBlockLargeHydrogenThrust` + 2 x `SmallBlockSmallHydrogenThrust` = 2.12 MN (8.2 m/s2); each other axis 2 large + 1 small = 1.06 MN (4.1 m/s2); 21 thrusters, 19.4 t, 252 PCU. Hydrogen 1.53 ML for 30 min vs 2.0 ML in 4 x `SmallHydrogenTank` (6.3 t, 6.3 k HP each: bury them deep).

**Gyros**: 22 x `SmallBlockGyro` for 0.30 rad/s2 (13.5 t, 1,100 PCU). Gyro PCU is the hidden cost of a small-grid ship above ~100 t (50 PCU per 448 kN*m); 12 gyros give 0.16 rad/s2 if PCU is tight.

**Power**: 1 x `SmallBlockLargeGenerator` (14.75 MW, 3.9 t, 25 PCU; 1 kg uranium per 244 s at full) covers the two railguns' 2 x 3.6 MW recharge (20 s each) with 7 MW to spare; 4 x `SmallBlockBatteryBlock` (4 MWh, 16 MW) carry the 7.4 MW peak for 32 min if the reactor dies. Uranium for 30 min is ~2 kg; preload 20 kg.

**Bays**: 2 launch/recover cradles at the stern. Each is a `SmallShipSmallMergeBlock` mating the drone's own merge block (same part as Hornet/Gnat) plus a `ConnectorSmall` for ammo/charge transfer; the drones sit in 4 x 3.5 x 3 m recesses (Hornet box 3.5 x 3 x 4 m). Launch = the LAUNCH block already in the drone scripts; recovery = the docking arm logic. 200 PCU for the merge blocks. No projector/welder printing on this class: 4 welders (600 PCU) plus projectors need the audit's NO-GO printer arrangement.

**Block list** (`mass` and `PCU` are totals for the line):

| block | n | mass kg | PCU |
|---|---|---|---|
| `SmallBlockCockpit` | 1 | 797 | 150 |
| `SmallBlockRemoteControl` | 1 | 38 | 25 |
| `SmallBlockLargeGenerator` | 1 | 3,901 | 25 |
| `SmallBlockBatteryBlock` | 4 | 4,162 | 60 |
| `SmallHydrogenTank` | 4 | 6,323 | 100 |
| `SmallRailgun` | 2 | 2,728 | 160 |
| `SmallBlockMediumCalibreGun` | 2 | 1,720 | 160 |
| `SmallBlockMediumCalibreTurret` | 1 | 2,254 | 275 |
| `SmallBlockAutocannon` | 2 | 360 | 160 |
| `SmallGatlingTurret` | 4 | 2,768 | 900 |
| `SmallProgrammableBlock` | 2 | 277 | 200 |
| `SmallBlockRadioAntenna` | 1 | 84 | 100 |
| `SmallCameraBlock` | 8 | 325 | 200 |
| `SmallDecoy` | 4 | 257 | 20 |
| `SmallOffensiveCombat` | 1 | 156 | 25 |
| `SmallFlightMovement` | 1 | 156 | 25 |
| `SmallTurretControlBlock` | 1 | 250 | 100 |
| `EventControllerSmall` | 3 | 115 | 30 |
| `TimerBlockSmall` | 3 | 91 | 75 |
| `SmallShipSmallMergeBlock` | 2 | 184 | 200 |
| `ConnectorSmall` | 2 | 410 | 250 |
| `SmallBlockLargeContainer` | 2 | 1,252 | 20 |
| `SmallBlockSmallContainer` | 2 | 98 | 20 |
| `SmallBlockFrontLight` | 6 | 516 | 150 |
| `SmallBlockSmallLight` | 10 | 160 | 200 |
| `SmallBlockSensor` | 2 | 268 | 50 |
| thrusters fwd: `SmallBlockLargeHydrogenThrust` x4 + `SmallBlockSmallHydrogenThrust` x2 (2.12 MN, 8.2 m/s2) | 6 | 5,556 | 72 |
| thrusters back: `SmallBlockLargeHydrogenThrust` x2 + `SmallBlockSmallHydrogenThrust` x1 (1.06 MN, 4.1 m/s2) | 3 | 2,778 | 36 |
| thrusters up: `SmallBlockLargeHydrogenThrust` x2 + `SmallBlockSmallHydrogenThrust` x1 (1.06 MN, 4.1 m/s2) | 3 | 2,778 | 36 |
| thrusters down: `SmallBlockLargeHydrogenThrust` x2 + `SmallBlockSmallHydrogenThrust` x1 (1.06 MN, 4.1 m/s2) | 3 | 2,778 | 36 |
| thrusters left: `SmallBlockLargeHydrogenThrust` x2 + `SmallBlockSmallHydrogenThrust` x1 (1.06 MN, 4.1 m/s2) | 3 | 2,778 | 36 |
| thrusters right: `SmallBlockLargeHydrogenThrust` x2 + `SmallBlockSmallHydrogenThrust` x1 (1.06 MN, 4.1 m/s2) | 3 | 2,778 | 36 |
| gyros `SmallBlockGyro` | 22 | 13,499 | 1100 |
| armour: 789 heavy + 6,230 light blocks, 1.0 layers | 7,019 | 191,684 | 7,019 |
| ammunition pre-load (see 3.3), kg | - | 3,100 | 0 |
| **total** | | **257,381** | **12,051** |

Budgets: electric peak 7.4 MW (railguns 7.2 + gyros 0.04 + base), generation 14.8 MW, battery 4 MWh / 16 MW; hydrogen needed 1.53 ML for 30 min vs 2.0 ML carried; PCU 12,051 of 50,000 (24 %) and of 100,000 (12 %); armour HP 0.96 M (3,724 HP per tonne of ship).

### 4.4 Frigate

**Role**: line warship (large grid): long-range artillery duelist with a gatling screen and 4 drone docks; hand flight from `LargeBlockCockpitSeat`.

**Weapons**: 2 x `LargeRailgun` (2,000 m, 50,000-pool; 76 MW recharge if both fire together) + 2 x `LargeCalibreTurret` (artillery, 2,000 m, alpha 34,000 per pair) + 4 x `LargeBlockMediumCalibreTurret` (assault, 1,400 m) + 8 x `LargeGatlingTurret` (800 m; point defence and anti-drone). 18.4 k sustained data DPS, alpha 200 k, weapons 107 t (5 % of the ship) and 3,610 PCU. No rockets: 825 PCU for a `LargeMissileLauncher` is not worth it. Eight gatling turrets cover a hemisphere each (section 2.6); 16,500-HP heavy blocks also make small warheads (15 k) survivable.

**Thrusters**: forward 1 x `LargeBlockLargeHydrogenThrust` = 7.2 MN (3.4 m/s2); each other axis 3 x `LargeBlockSmallHydrogenThrust` = 3.24 MN (1.5 m/s2); 16 thrusters, 28.2 t, 192 PCU. Hydrogen need 4.3 ML vs 30 ML (2 x `LargeHydrogenTank`, 16.3 t, one per side). A full forward burn is 4,820 L/s = 52 min of one tank.

**Gyros**: 5 x `LargeBlockGyro` = 168 MN*m: 0.15 rad/s2 about I = 1.1e9 kg*m2 (64 t, 250 PCU).

**Power**: 6 x `LargeBlockSmallGenerator` (90 MW, 28.8 t, 150 PCU) spread through the hull against a 76 MW railgun recharge; 8 x `LargeBlockBatteryBlock` (24 MWh, 96 MW: 19 min of both railguns recharging with every generator dead). Uranium ~25 kg for 30 min; preload 100 kg per generator.

**Bays**: 4 drone docks as `LargeBlockInsetConnectorSmall` (a large-grid block with a small-connector face; in the owner's `Small Grid 8000` sample) in hangar niches. The drone then needs a `ConnectorSmall` (Hornet has only a merge block today). Alternative: a `LargeStator` with a small-grid `SmallRotor` subgrid carrying `SmallShipSmallMergeBlock`s (the stator/rotor pair is in the Starburst v3/v4 and Venture samples), keeping the existing merge launch protocol. Printing: 2 x `LargeProjector` + 8 x `LargeShipWelder` (1,350 PCU) fed from the 4 `LargeBlockLargeContainer`s through `LargeBlockConveyor` (the Tech Hunter pattern: 132 welders and a projected small grid), only after in-game tests P1-P6.

**Block list** (`mass` and `PCU` are totals for the line):

| block | n | mass kg | PCU |
|---|---|---|---|
| `LargeBlockCockpitSeat` | 1 | 1,768 | 150 |
| `LargeBlockRemoteControl` | 1 | 137 | 25 |
| `LargeBlockSmallGenerator` | 6 | 28,758 | 150 |
| `LargeBlockBatteryBlock` | 8 | 30,760 | 120 |
| `LargeHydrogenTank` | 2 | 16,323 | 50 |
| `LargeRailgun` | 2 | 28,940 | 160 |
| `LargeCalibreTurret` | 2 | 28,448 | 550 |
| `LargeBlockMediumCalibreTurret` | 4 | 38,616 | 1,100 |
| `LargeGatlingTurret` | 8 | 11,424 | 1,800 |
| `LargeDecoy` | 6 | 4,440 | 30 |
| `LargeProgrammableBlock` | 2 | 1,069 | 200 |
| `LargeBlockRadioAntenna` | 1 | 3,402 | 100 |
| `LargeCameraBlock` | 4 | 162 | 100 |
| `LargeTurretControlBlock` | 1 | 948 | 100 |
| `LargeOffensiveCombat` | 1 | 900 | 25 |
| `LargeFlightMovement` | 1 | 900 | 25 |
| `EventControllerLarge` | 4 | 1,216 | 40 |
| `TimerBlockLarge` | 4 | 1,036 | 100 |
| `LargeBlockInsetConnectorSmall` | 4 | 14,256 | 500 |
| `LargeProjector` | 2 | 1,053 | 100 |
| `LargeShipWelder` | 8 | 5,707 | 1,200 |
| `LargeBlockLargeContainer` | 4 | 10,374 | 40 |
| `LargeBlockSmallContainer` | 6 | 3,890 | 60 |
| `LargeBlockConveyor` | 40 | 20,960 | 400 |
| `LargeBlockSensor` | 2 | 268 | 50 |
| `LargeBlockFrontLight` | 8 | 3,600 | 200 |
| `SmallLight` | 20 | 320 | 400 |
| thrusters fwd: `LargeBlockLargeHydrogenThrust` x1 + `LargeBlockSmallHydrogenThrust` x0 (7.20 MN, 3.4 m/s2) | 1 | 6,940 | 12 |
| thrusters back: `LargeBlockLargeHydrogenThrust` x0 + `LargeBlockSmallHydrogenThrust` x3 (3.24 MN, 1.5 m/s2) | 3 | 4,260 | 36 |
| thrusters up: `LargeBlockLargeHydrogenThrust` x0 + `LargeBlockSmallHydrogenThrust` x3 (3.24 MN, 1.5 m/s2) | 3 | 4,260 | 36 |
| thrusters down: `LargeBlockLargeHydrogenThrust` x0 + `LargeBlockSmallHydrogenThrust` x3 (3.24 MN, 1.5 m/s2) | 3 | 4,260 | 36 |
| thrusters left: `LargeBlockLargeHydrogenThrust` x0 + `LargeBlockSmallHydrogenThrust` x3 (3.24 MN, 1.5 m/s2) | 3 | 4,260 | 36 |
| thrusters right: `LargeBlockLargeHydrogenThrust` x0 + `LargeBlockSmallHydrogenThrust` x3 (3.24 MN, 1.5 m/s2) | 3 | 4,260 | 36 |
| gyros `LargeBlockGyro` | 5 | 64,085 | 250 |
| armour: 750 heavy + 697 light blocks, 1.5 layers | 1,447 | 1,764,731 | 1,447 |
| ammunition pre-load (see 3.3), kg | - | 30,000 | 0 |
| **total** | | **2,146,732** | **9,664** |

Budgets: electric peak 76.5 MW (railguns 76.0 + gyros 0.05 + base), generation 90 MW, battery 24 MWh / 96 MW; hydrogen needed 4.34 ML for 30 min vs 30.0 ML carried; PCU 9,664 of 50,000 (19 %) and of 100,000 (10 %); armour HP 8.82 M (4,110 HP per tonne of ship).

### 4.5 Dreadnought

**Role**: flagship, 200 x 60 x 35 m, 13.2 kt: 67 % of the Tech Hunter's 19.9 kt with 55 % of its sustained turret DPS (107 k by the same table: 36 assault + 16 artillery + 24 gatling) from 50 weapons instead of 76, but with 6 railguns it outranges it (2,000 m, every Tech Hunter turret is limited to 800 m without an AI block). Two bridge seats (`LargeBlockCockpitSeat` x2: command and gunnery), 8 docks.

**Weapons** (sustained data DPS 58.9 k, alpha 668 k): 6 x `LargeRailgun` (2,000 m; 228 MW recharge if all fire together) + 8 x `LargeCalibreTurret` (artillery, 2,000 m) + 12 x `LargeBlockMediumCalibreTurret` (assault, 1,400 m) + 20 x `LargeGatlingTurret` (800 m, point defence and anti-drone) + 4 x `LargeMissileTurret` (splash on clumped drones). Weapon mass 352 t (2.7 %), 11,580 PCU. Decoys: 12 x `LargeDecoy` (3,445 HP, 5 PCU) under a heavy slope on the dorsal and ventral skin (the Tech Hunter hides 14 that way).

**Thrusters**: forward 3 x `LargeBlockLargeHydrogenThrust` + 5 x `LargeBlockSmallHydrogenThrust` = 27.0 MN (2.0 m/s2); each other axis 2 large = 14.4 MN (1.1 m/s2); 18 thrusters, 97 t, 216 PCU. Hydrogen need 17 ML vs 60 ML (4 x `LargeHydrogenTank`, 32.6 t).

**Gyros**: 86 x `LargeBlockGyro` for 0.06 rad/s2 (3.4 deg/s2) about I = 4.8e10 kg*m2: 1,102 t (8 % of mass) and 4,300 PCU. The Tech Hunter has 136 gyros for 9.2e10 kg*m2 (0.05 rad/s2). Gyros are the biggest PCU item after armour and weapons; 43 gyros give 0.03 rad/s2 (a lumbering flagship, fine for a platform whose weapons are mostly turrets).

**Power**: 1 x `LargeBlockLargeGenerator` (300 MW, 73.8 t) + 8 x `LargeBlockSmallGenerator` (120 MW): 420 MW against 228 MW railgun recharge + 0.9 MW gyros; 20 x `LargeBlockBatteryBlock` (60 MWh, 240 MW). Without the big reactor the 120 MW left cycles the railguns at 53 %. Uranium ~70 kg per 30 min; preload 500 kg in the large reactor and 100 kg in each small one.

**Bays**: 8 docks (`LargeBlockInsetConnectorSmall`) or 4 stator-and-subgrid merge cradles, plus an optional 4-projector / 16-welder foundry with 12 `LargeBlockLargeContainer`s, all inside, behind `AirtightHangarDoor` (7.9 t, 37 k HP, 115 PCU, non-DLC).

**Block list** (`mass` and `PCU` are totals for the line):

| block | n | mass kg | PCU |
|---|---|---|---|
| `LargeBlockCockpitSeat` | 2 | 3,536 | 300 |
| `LargeBlockRemoteControl` | 2 | 274 | 50 |
| `LargeBlockLargeGenerator` | 1 | 73,795 | 25 |
| `LargeBlockSmallGenerator` | 8 | 38,344 | 200 |
| `LargeBlockBatteryBlock` | 20 | 76,900 | 300 |
| `LargeHydrogenTank` | 4 | 32,646 | 100 |
| `LargeRailgun` | 6 | 86,820 | 480 |
| `LargeCalibreTurret` | 8 | 113,792 | 2,200 |
| `LargeBlockMediumCalibreTurret` | 12 | 115,848 | 3,300 |
| `LargeGatlingTurret` | 20 | 28,560 | 4,500 |
| `LargeMissileTurret` | 4 | 7,304 | 1,100 |
| `LargeDecoy` | 12 | 8,880 | 60 |
| `LargeProgrammableBlock` | 3 | 1,603 | 300 |
| `LargeBlockRadioAntenna` | 2 | 6,803 | 200 |
| `LargeCameraBlock` | 8 | 325 | 200 |
| `LargeTurretControlBlock` | 2 | 1,896 | 200 |
| `LargeOffensiveCombat` | 2 | 1,800 | 50 |
| `LargeFlightMovement` | 2 | 1,800 | 50 |
| `EventControllerLarge` | 6 | 1,824 | 60 |
| `TimerBlockLarge` | 8 | 2,072 | 200 |
| `LargeBlockInsetConnectorSmall` | 8 | 28,512 | 1,000 |
| `LargeProjector` | 4 | 2,106 | 200 |
| `LargeShipWelder` | 16 | 11,414 | 2,400 |
| `LargeBlockLargeContainer` | 12 | 31,123 | 120 |
| `LargeBlockSmallContainer` | 12 | 7,781 | 120 |
| `LargeBlockConveyor` | 200 | 104,800 | 2,000 |
| `LargeBlockSensor` | 4 | 537 | 100 |
| `LargeBlockFrontLight` | 16 | 7,200 | 400 |
| `SmallLight` | 40 | 640 | 800 |
| thrusters fwd: `LargeBlockLargeHydrogenThrust` x3 + `LargeBlockSmallHydrogenThrust` x5 (27.00 MN, 2.0 m/s2) | 8 | 27,920 | 96 |
| thrusters back: `LargeBlockLargeHydrogenThrust` x2 + `LargeBlockSmallHydrogenThrust` x0 (14.40 MN, 1.1 m/s2) | 2 | 13,880 | 24 |
| thrusters up: `LargeBlockLargeHydrogenThrust` x2 + `LargeBlockSmallHydrogenThrust` x0 (14.40 MN, 1.1 m/s2) | 2 | 13,880 | 24 |
| thrusters down: `LargeBlockLargeHydrogenThrust` x2 + `LargeBlockSmallHydrogenThrust` x0 (14.40 MN, 1.1 m/s2) | 2 | 13,880 | 24 |
| thrusters left: `LargeBlockLargeHydrogenThrust` x2 + `LargeBlockSmallHydrogenThrust` x0 (14.40 MN, 1.1 m/s2) | 2 | 13,880 | 24 |
| thrusters right: `LargeBlockLargeHydrogenThrust` x2 + `LargeBlockSmallHydrogenThrust` x0 (14.40 MN, 1.1 m/s2) | 2 | 13,880 | 24 |
| gyros `LargeBlockGyro` | 86 | 1,102,262 | 4300 |
| armour: 4,689 heavy + 4,355 light blocks, 1.5 layers | 9,044 | 11,032,836 | 9,044 |
| ammunition pre-load (see 3.3), kg | - | 200,000 | 0 |
| **total** | | **13,231,353** | **34,575** |

Budgets: electric peak 229.3 MW (railguns 228.0 + gyros 0.86 + base), generation 420 MW, battery 60 MWh / 240 MW; hydrogen needed 16.99 ML for 30 min vs 60.0 ML carried; PCU 34,575 of 50,000 (69 %) and of 100,000 (35 %); armour HP 55.16 M (4,169 HP per tonne of ship).

## 5. What the simulator cannot model (check of sim/src/Api, Combat, Core)

| Thing used by these designs | Sim status | Consequence |
|---|---|---|
| `Cockpit` blocks, manual flight, pilot input | `BlockApiFactory.Make` has no `Cockpit` case: a cockpit is a `SimGenericFunctional`; only `RemoteControl` becomes `SimShipController` (RemoteControlApi.cs). `Scenario.cs:234` collects controllers by that class. No player input exists | hand flight cannot be simulated at all; scripts that need `IMyShipController` from a cockpit (`MoveIndicator`, `IsUnderControl`) get nothing. Give every ship a `LargeBlockRemoteControl`/`SmallBlockRemoteControl` as the script's controller |
| Hydrogen thrust, tanks, fuel | `DefDb.cs:161`: hydrogen thrusters draw 0 MW, tanks assumed full, never consumed | hydrogen designs look better than they are; fuel-out is never seen |
| Reactors, hydrogen engines, uranium | constant output, "fuel not modelled" (`PowerSystem.cs`, `DefDb.cs:23`) | uranium budget untestable; loss of all generators is modelled only through `Generators` list (blocks destroyed) |
| Pre-loaded ammo, cargo, conveyors, sorters, resupply | `Items` ignored; `InfiniteAmmo=true`, or a full inventory when false (`Weapons.cs:105-113`); CargoContainer/ConveyorSorter are `SimGenericTerminal` | the 3-6-round inventories of artillery/assault turrets (section 3.3) never run dry; the conveyor-feed requirement is invisible |
| Assault cannon / artillery shells | slugs with a penetrating pool but no capacitor and flat reload per shot, no burst of 2, no ricochet (`weapons.md` W-03, W-08) | assault/artillery DPS in the sim differs from section 2.6 by up to 2x (burst) and flatters shallow hits |
| Large railgun | capacitor modelled (38 MW recharge, 0.5 MWh); needs `StoredPower` in the blueprint (`Weapons.cs:321`) | fine; set `<StoredPower>0.5</StoredPower>` |
| Connectors, landing gear, rotors/stators/hinges/pistons, subgrids | `ShipConnector`, `LandingGear`, `MotorStator` etc. fall into the generic functional block; no docking or constraint physics (only `MergeBlock` is modelled, MergeApi.cs, DockLaunch.cs) | `LargeBlockInsetConnectorSmall` bays and stator-and-subgrid cradles cannot be tested; the merge-block bay (corvette) can, with the small-merge assumption of `packages/unknowns.md` |
| Projector + welder printing | `SimProjector` counts down only; no placed blocks (`foundry-notes.md`) | printing bays untestable in sim |
| Timer, Event Controller, button panel, toolbars | `SimGenericTerminal`, no toolbar execution (`audit/timers_pbs.md` section 3) | dead-man timer / EC cockpit switching untestable |
| Decoys, large turrets, cameras, sensors, TurretControlBlock, AI blocks | modelled (stand-in AI), ranges: large turrets 800 m without an AI block | OK; AI-block behaviour is a stand-in ("assumes drones fly") |
| Armour deformation / ramming, ricochet, explosion occlusion | placeholder collision formula; explosions ignore occlusion (-74 % on small-grid rockets in the game) | rocket/PD results optimistic for the attacker; large-grid thrusters/batteries die 1.6-5.9x too late (functional threshold bug) so capital ships last too long |
| 100 m/s cap | `SmallShipMaxSpeed = LargeShipMaxSpeed = 100` (constants) matches | our accelerations never reach it in a fight |
| Mass/COM/inertia/thrust per direction | audited as matching the game | the thruster/gyro numbers here can be checked in the sim for a pasted ship (Tech Hunter ran: 13.7 k blocks) |
| Collision of a 13 kt grid | not meaningful | do not tune ramming |

Safe uses of the sim for these ships: same-assumption A/B of weapon mixes, thruster layouts and armour distribution against the Pirate/Tech Hunter scenarios with `--filter`, 10-20 seeds per variant. I ran none for this report (CLAUDE.md: only as many runs as the task needs; this task needed none).

## 6. Style and the voxel-hull to block mapping

### 6.1 Cosmetic blocks that work with the validator
- **Shaping set (both grids, same shapes)**: `Block`, `Slope`, `Corner`, `CornerInv`, `Slope2Base/Tip` (2-run ramp), `Corner2Base/Tip`, `InvCorner2Base/Tip`, `RoundSlope`, `RoundCorner(Inv)`, `SquareSlopedCornerBase/Tip(Inv)`, `RaisedSlopedCorner`, `SlopeTransition(+Base/Tip, +Mirrored)`, `SlopedCorner(Base/Tip)`, `HalfCorner`, `HalfSlopeCorner(+Inverted)`, `HalfSlopedCorner(+Base)`, `HalfArmorBlock`, `HalfSlopeArmorBlock` (table 2.1; every row has light and heavy twins).
- **Skin trim**: the `*Panel*` families (`ArmorPanel`, `ArmorSlopedPanel`, `Armor2x1SlopedPanel...`, `RoundArmorPanel...`): mount areas 5-25 % of a face, 100-2,640 HP; they attach to a face of a neighbour but have too little mount to carry anything. Light panels are 100-160 kg large, 20 kg small.
- **Glass**: `Window1x1Slope/Flat/Face/Side/Inv`, `Window1x2*`, `Window2x3Flat`, `Window3x3Flat`, `WindowRound*`, `LargeWindowSquare`; small `SmallWindow*`. 135 HP (small) to 12,360 HP (large 3x3). Mount 10-20 %: place them against full cube/slope faces and never expect another block to hang from them.
- **Lights**: `SmallLight`, `LargeBlockLight_1corner/2corner`, `LargeBlockFrontLight`, `SmallBlockFrontLight`; 20-25 PCU each.
- **Greebles**: `LargeBlockStructural_Frame/SupportBeam3x3-5x5/Platform` (hangar trusses), `LargeSteelCatwalk*`, `LargeStairs`, `LargeRamp`, `LargeCoverWall*`, `Large/SmallSymbol*`.
- Heavy and light twins have identical mounts (checked by script: 115 of 116 light/heavy pairs have identical mount lists; the 116th, `LargeBlockArmorBlock`, has the implicit all-faces-full list; masses differ), so a heavy outer layer over a light inner one, or colour-coded stripes (`ColorMaskHSV` per block), is free.

### 6.2 Orientation conventions of tools/sebp.py (verified by reading and by tests)
- `DIR = Forward (0,0,-1), Backward (0,0,1), Up (0,1,0), Down (0,-1,0), Left (-1,0,0), Right (1,0,0)` in GRID coordinates (x right, y up, z backward). A block's `BlockOrientation` is `(Forward, Up)`, perpendicular; `Right = cross(Forward, Up)`; local axes in grid directions are `[Right, Up, Backward]` (`Blueprint.axes`).
- Block-local box: x in [0,X] = Right, y in [0,Y] = Up, z in [0,Z] = Backward; the FRONT face is z = 0 (the block's Forward side). `Min` = lowest grid cell; `ext()` swaps extents by orientation; `cells()` enumerates.
- A `<MountPoint Side StartX StartY EndX EndY>` is a rectangle on a face with side-local (u,v): `Front (X-u, v, 0)`, `Back (u, v, Z)`, `Left (0, v, u)`, `Right (X, v, Z-u)`, `Top (u, Y, Z-v)`, `Bottom (u, 0, v)`; shrunk by 0.001 each side; two blocks attach when rectangles lie on the same plane with opposite normals and overlap (`attachment`), unless `(A.excl & B.prop) or (A.prop & B.excl)` and different definitions.
- Consequences for shaped blocks, with the default orientation (Forward, Up): **Slope**: full Front and Bottom faces (tall vertical face = Forward side, flat face = Down side), triangular Left/Right, slanted face (normal Up + Backward) has no mount. **Corner**: triangles on Front, Right, Bottom; the three open sides face Backward, Left, Up. **CornerInv**: full Back, Left, Top; 3/4 on Front, Right, Bottom; the cut-off vertex is at Front-Right-Bottom. **Slope2Base**: full Front + Bottom, half-height Back strip; **Slope2Tip**: half-height Front strip + full Bottom.
- `validate()` checks: no overlap, DLC, every block attached, one connected component, reserved clear cells. **It does not check that a shaped block faces the right way**: in a negative test (below) a slope turned so that its slanted face is against the hull still passes `validate()` because its Front face still touches a neighbour. A semantic check is needed (section 6.4).

### 6.3 Voxel-hull to block rule set (tested)
Input: a set `V` of hull cells (grid coordinates). For each cell `c` in `V` let `E` = the directions `d` in the six DIR vectors with `c+d` not in `V` (exposed faces). Interior neighbours are the other directions.

| `E` | block | orientation |
|---|---|---|
| 0 or 1 exposed face | `Block` (light or heavy) | any (Forward, Up) |
| 2 exposed faces `a`, `b`, perpendicular (an edge) | `Slope` (convex chamfer) | Up = `a`, Forward = `-b` (the swap Up=`b`, Forward=`-a` is the identical solid); verified: the closed form reproduces the mount-area vector of the brute-force solver in 24 of 24 cases |
| 3 exposed faces mutually perpendicular (a vertex) | `Corner` | table below |
| 2 exposed faces opposite (a 1-cell fin), or 3+ with an opposite pair, or 4+ | `Block` (or `HalfArmorBlock`/`HalfSlopeArmorBlock` for knife edges) | any |
| EMPTY cell with 2 perpendicular solid neighbours `s1`, `s2` (concave edge) | `Slope` as a fillet | exposed directions are `-s1`, `-s2`, then as for an edge |
| EMPTY cell with 3 mutually perpendicular solid neighbours (concave vertex) | `CornerInv` | the orientation whose three full mounts point at the solids (solver) |
| a run of 2 cells along nose direction `r` where the top surface drops 1 cell | `Slope2Base` then `Slope2Tip` | Forward = `-r`, Up = the exposed normal; both blocks the same orientation |

Corner orientations (exposed direction triple -> Forward, Up), from the solver: (Right, Up, Backward) -> (Forward, Right); (Right, Up, Forward) -> (Backward, Up); (Right, Down, Backward) -> (Forward, Down); (Right, Down, Forward) -> (Backward, Right); (Left, Up, Backward) -> (Forward, Up); (Left, Up, Forward) -> (Backward, Left); (Left, Down, Backward) -> (Forward, Left); (Left, Down, Forward) -> (Backward, Down). The generic solver is simpler to keep than tables: for each shape block precompute, for the 24 (Forward, Up) pairs, the mount area per grid direction (from `mount_rects`), and pick the orientation with zero area on exposed directions and maximal area on interior directions.

Procedure notes: (1) run it on a SOLID filled hull to get the right faces; for a hollow ship fill only a skin of `layers` cells (outer rule uses the full solid, then discard cells deeper than the skin). (2) Edge/vertex rules chamfer ALL hull edges, including the keel; to keep a flat belly, ignore exposures on a chosen set of directions when classifying (e.g. do not count Down). (3) Heavy vs light by depth (heavy on the outside, light inside) or by region (bow heavy). (4) Round shapes (`RoundSlope`, `RoundCorner`) are drop-in replacements for Slope/Corner (identical mounts for slope; corner differs slightly). (5) Pieces from table 2.1 with `Left`/`Right`/`Inv` suffixes are chiral pairs: use the mirrored name for the port side.

### 6.4 Test (throwaway script, scratchpad `hull_rules.py`, `fillet_test.py`; not committed)
Hull: 5 x 3 x 8 cell box (x 0-4, y 0-2, z 4-11) with a stepped wedge nose along -z (layer z=3: x 1-3, y 0-1; tip layer z=2: x 2, y 0), 127 cells, `Blueprint('wedge','Large')`, `LargeBlockArmor*` and again with `LargeHeavyBlockArmor*`:
```
light cells 127 blocks 127 {'corner': 12, 'slope': 38, 'block': 77} validate OK semantic problems []
heavy cells 127 blocks 127 {'corner': 12, 'slope': 38, 'block': 77} validate OK semantic problems []
closed form Up=a Forward=-b reproduces solver face-area vector in 24 of 24
```
- `validate()` (overlap, DLC, all attached, one component) passed for both. The semantic check (every mount rectangle of every non-cube block must face a hull cell) found 0 problems.
- Negative test: one slope turned to Forward/Up: `validate()` still PASSED, the semantic check reported 3 mounts facing empty space. So add this check to the build (it is ~10 lines, in the script).
- 2-cell ramp: 3 columns, `Slope2Base` at the high cell and `Slope2Tip` at the low cell, both `Forward=Backward, Up=Up`, validates; adding a `LargeHeavyBlockArmorBlock` beside light blocks validates (heavy/light interchange).
- Concave fillets (L-shaped floor + two walls, 8 edge fillets with `Slope`, 1 vertex fillet with `CornerInv`): 61 blocks, `validate()` OK, no full-area mount faces an empty cell.
Not tested: SquareSloped/Transition families, Corner2, round shapes, hollow shells, large (200 m) hulls, the actual game rendering of the chosen orientation (the geometry is derived from the mount points, not from the model mesh).

Core code of the test (copy to `tools/` if adopted):
```python
DIRS = list(DIR.values())
NAME = {v: k for k, v in DIR.items()}
def add(a, b): return (a[0]+b[0], a[1]+b[1], a[2]+b[2])
def neg(a): return (-a[0], -a[1], -a[2])
def dot(a, b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]

ORIENTS = [(f, u) for f in DIR for u in DIR if dot(DIR[f], DIR[u]) == 0]   # the 24 (Forward, Up) pairs

def face_areas(bp, sub, grid='Large'):
    """orientation -> {grid direction: mount area (cell-face fractions)} for a 1x1x1 block placed at origin."""
    out = {}
    for f, u in ORIENTS:
        b = bp.add('MyObjectBuilder_CubeBlock', sub, (0, 0, 0), fwd=f, up=u)
        area = {}
        for (axis, pc, ns, lo, hi, ex, pr) in bp.mount_rects(b):
            d = [0, 0, 0]; d[axis] = ns
            area[tuple(d)] = area.get(tuple(d), 0) + max(0, hi[0]-lo[0]+2*sebp.SHRINK) * max(0, hi[1]-lo[1]+2*sebp.SHRINK)
        out[(f, u)] = area
        bp.blocks.pop()
    return out

class Rules:
    def __init__(self, bp, heavy=False):
        self.bp = bp
        p = 'LargeHeavyBlockArmor' if heavy else 'LargeBlockArmor'
        self.sub = dict(block=p+'Block', slope=p+'Slope', corner=p+'Corner', corner_inv=p+'CornerInv',
                        slope2base=p+'Slope2Base', slope2tip=p+'Slope2Tip')
        self.areas = {k: face_areas(bp, s) for k, s in self.sub.items()}

    def pick(self, kind, exposed):
        """generic solver: orientation with zero mount area on exposed faces, max area on the others"""
        best = None
        for o, area in self.areas[kind].items():
            if any(area.get(d, 0) > 1e-3 for d in exposed): continue
            score = sum(a for d, a in area.items() if d not in exposed)
            if best is None or score > best[0] + 1e-9: best = (score, o)
        return best[1] if best else None

def build(V, heavy=False, name='wedge'):
    bp = Blueprint(name, 'Large')
    R = Rules(bp, heavy)
    stats = {}
    for c in sorted(V):
        E = [d for d in DIRS if add(c, d) not in V]
        n = len(E)
        perp = lambda s: all(dot(s[i], s[j]) == 0 for i in range(len(s)) for j in range(i+1, len(s)))
        if n <= 1 or not perp(E) and n == 2 or n > 3:
            kind = 'block'; o = ('Forward', 'Up')
        elif n == 2:
            kind = 'slope'; o = R.pick('slope', E)
            a, b = E   # closed form: Up = a, Forward = -b (or the mirror: both give the same solid)
        elif n == 3 and perp(E):
            kind = 'corner'; o = R.pick('corner', E)
        else:
            kind = 'block'; o = ('Forward', 'Up')
        if o is None: kind, o = 'block', ('Forward', 'Up')
        bp.add('MyObjectBuilder_CubeBlock', R.sub[kind], c, fwd=o[0], up=o[1])
        stats[kind] = stats.get(kind, 0) + 1
    return bp, stats

def semantic_check(bp, V):
    """every mount rectangle of every shaped block must face a hull cell (the slanted/open faces carry no mount)"""
    bad = []
    for b in bp.blocks:
        if b['sub'].endswith('ArmorBlock'): continue
        cell = b['min']
        for (axis, pc, ns, lo, hi, ex, pr) in bp.mount_rects(b):
            d = [0, 0, 0]; d[axis] = ns
            if add(cell, tuple(d)) not in V: bad.append((b['sub'], cell, NAME[tuple(d)]))
    return bad
```

### 6.5 Stylish-ship techniques that follow from the data
1. **Wedge nose by chamfer + 2-run ramps**: use the edge/vertex rules for a blunt wedge; for a long nose give the top and sides a 2-run Slope2Base/Tip pair per step (smoother than 1-run stairs) and finish the tip with `Corner2Tip`/`SquareSlopedCornerTip` pieces.
2. **Stepped decks / superstructure**: stack layers shrinking by 1 cell; the rule set slopes every step edge, which gives a stepped-pyramid bridge. Put `Window1x1Slope` where the bridge Slope would be (mount 10-20 %, so give it a full cube behind).
3. **Fins and wings**: a 1-cell-thick plate is all-exposed; use `HalfArmorBlock` (Front 1.0, halves on sides) for thin fins and `HalfSlopeArmorBlock` for leading edges; attach to a hull cube face (needs a full mount on the hull side). Thruster pods: a thruster block + `Slope`/`Corner` shroud around its flame-free sides.
4. **Heavy bow / light stern, colour stripes**: heavy on the facing side, light elsewhere; `ColorMaskHSV` per block; `LargeSymbol*` hull numbers (1 PCU).
5. **Lights and greebles**: reflectors on fin tips, `LargeBlockLight_1corner` in recesses, `LargeBlockStructural_SupportBeam*` for hangar trusses; budget 20-25 PCU per light.
6. **Reserve clear cells**: `Blueprint.keep_clear(block, n)` for thruster exhaust and gun muzzles, so no armour fills them (the nose cannons and the stern thrusters need 1-3 cells).

## 7. Manual flight, control layer and the enemies

- **Hand-flyable and autonomous with the same blocks.** The cockpit (`DBSmallBlockFighterCockpit`, `SmallBlockCockpit`, `LargeBlockCockpitSeat`) is the manual controller; the scripts' controller is a RemoteControl (the only `IMyShipController` the sim and the existing scripts know). Thrusters and gyros are NOT overridden while a pilot sits: the Helm PB must release overrides (`Command("assist")`/idle) when the cockpit is occupied; docs/audit/timers_pbs.md section 7 gives the tested architecture: EC `Cockpit occupied -> Run assist`, empty -> `Run guard`, dead-man Timer disarmed by the Staff PB during manual flight, Helm PB <= 80 k characters, Staff PB at Update10. Control-layer PCU 400-550 (fighter ~300 with one PB; corvette/frigate ~450; dreadnought ~550 with 3 PBs, 6 ECs, 8 timers). Event-controller and timer toolbar items with a PB `Run` argument have never been seen in a game-written file (audit test T2/T-list).
- **Enemies the loadouts answer** (docs/RESULTS.md, docs/tech-hunter-study.md, docs/kamikaze-defence.md): Pirate ships with gatling/missile turrets (800 m turret range without an AI block), swarms of Gnat/Hornet/Stinger drones, kamikaze warheads at 100 m/s. Numbers that drive the choices: 10 Longbow L (large railgun) do 10 power-block kills in 10 min on the Tech Hunter while 10 Hornets/Gnats/Basilisks die in 50-70 s, i.e. range and railgun penetration beat gatling/rocket DPS against big hulls; gatling turrets are the right answer to small drones, and the 800 m turret cap means a ship with an AI block or a pilot lock (all four ships have one) outranges an unmodified pirate by 1,200 m with artillery/railguns.
- **Bays and drones.** Existing drones carry a small merge block (Hornet/Wasp/Gnat) and, on the Longbow S/L and Basilisk, no docking face; large-grid docking needs either `ConnectorSmall` on the drone or a stator-and-subgrid merge cradle. Printing is the audit's NO-GO design: keep bays launch/recover-only until P1-P6.

## 8. Open risks and next steps

1. In-game unknowns that change numbers: gatling is 600 rpm in game (all gatling DPS above x0.857); `LargeRailgunAmmo` item shape never seen loaded; whether pre-loaded ammo in a pasted blueprint is accepted (test T8); conveyor port positions are not in defs.json; `LargeBlockInsetConnectorSmall` docking with a small `ConnectorSmall` [rec, medium]; the 0.62 hull factor and layer assumptions (section 4.0) set the masses, tune with the first real block counts.
2. The 66 t fighter is heavier than the user's Fighter Ac (34 t) because it assumes a complete 1-block skin; lower `layers` for a lighter craft (acceleration scales as 1/mass).
3. Dreadnought PCU 34.6 k is ~70 % of a 50 k world; dropping to 43 gyros saves 2,150 PCU and 551 t, dropping the 4 rocket turrets saves 1,100 PCU. A survival world with 20 k PCU only fits the fighter, corvette and frigate.
4. Generator work list: (a) shapes for LargeMissileTurret/LargeGatlingTurret/Reactor/CargoContainer/OxygenTank/HydrogenEngine/Cockpit/Decoy/Sensor, (b) `items=` and `FilledRatio`/`Capacity` overrides, (c) the semantic mount-face check, (d) a hull generator from section 6.3, (e) toolbars/groups (audit 4.1), (f) a conveyor-network helper.
5. Sim work list (optional): read `Items` into `InitAmmo`; model uranium/hydrogen counters; a Cockpit ship controller; connector docking. None is needed to choose the designs above.
