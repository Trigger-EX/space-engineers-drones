# Sprue Test: one-session test of printer option A

Owner: `tools/designs/sprue_test.py` (models `Sprue Test`, `Sprue Test No Grinder`, kind `test`: built by `build_all.py --only ...`, never packaged), `scripts/SprueProbe.cs` (runs on both PBs), `tools/tests/test_sprue_test.py` (10 tests). Owner copy for the game: `test_designs/Sprue Test/`.
Nothing here has run in the game. The simulator models neither fused grids nor grinders, so no simulation was run. Release zips are not rebuilt.

## 1. Why

`docs/audit/printers.md` printers-01: blocks welded from a projection are built into the PROJECTOR's grid, so a printed drone is fused to the printer and switching merge blocks off cannot release it (a grid only splits when a block is removed). Option A (printers.md, "Fix options"): put a sprue, a neck of armour, into the projected blueprint between printer and drone and grind it with a small ship grinder; the next print re-welds the neck.
This rig answers the open questions in ONE creative-world session with as few confounders as possible:

| Q | Question | Audit id | How the rig answers it |
|---|---|---|---|
| Q1 | Is the welded payload on the PRINTER's grid (fused) or its own grid? | P1, printers-01 | The printer PB checks each payload cell with `Me.CubeGrid.GetCubeBlock(cell)`; all present means `Q1 FUSED`. There is no automatic NOT FUSED verdict (the projector cannot see welded blocks on another grid): that is decided from the Info-tab grid list and the payload PB screen (`printer PB not visible`), helped by the diagnosed `WELD STALL`. |
| Q2a | Does removing neck cells split the payload into its own grid? | P2, P3 | `SPLIT` log line on both PBs. |
| Q2b | Does a ship grinder grind blocks of its OWN grid? | P3 | `cut` while fused. Valid only if the grinder was working at least 90 % of the window and `pos OK`: `UNTOUCHED (valid, D3)` means NO, a split means YES. Positive control: `hand` (grind neck cell N2 by hand), then `cut` again (CUT2): the grinder then faces a neck on ANOTHER grid. |
| Q2c | Does the grinder leave printer and payload untouched apart from the neck? | P3 | 31 guard cells (printer cells near the grinder sphere) are watched every tick during CUT/CUT2; any loss stops the grinder (`GUARD HIT`). The payload PB shows its own cluster map. LIMIT: in the expected world (grinder ignores its own grid) this shows collateral on the PAYLOAD grid only. Collateral from an other-grid grinder next to printer blocks (option A') is not tested. |
| Q3 | Does a welded PB keep its program and run without Recompile? Owner? Welded battery charge? Custom Data? | P6, printers-04, printers-09, unknowns 4 and 22 | Payload log `COMPILED boot 1 ...`, RESULTS `Q3 PB RUNS hb n` (heartbeat rising), the raw owner id against the printer's and the blueprint's, the battery value at BUILT against the 0.04 MWh written in the blueprint (0.04 kept, 0.05 full, 0.015 the 30 % default, 0 empty), `cd ok` or `cd LOST` for Custom Data. |
| Q4 | Does the cut payload get free, and does the next cycle re-weld the neck? | none | One ion thruster pushes the payload away (2 % for 1 s, 15 s after the split, or on `push`): `PAYLOAD LEFT: moved > 6 m`. Then `weld` (cycle 2): `Q4 c2 re-weld BUILT`, a new `COMPILED boot 1`. |

## 2. What the source says (read this before the result)

* **A ship grinder very probably does NOT grind its own grid.** [Keen's old public source, an unverified snapshot] `MyShipToolBase.CanInteractWithSelf` is `false` and `CanInteractWith` returns false for `entity == CubeGrid && !CanInteractWithSelf`; `MyShipWelder` overrides it to `true`, `MyShipGrinder` has no override. In the fused state the neck is printer-grid material, so option A as written (grinder on the printer) is expected to give `UNTOUCHED (valid, D3)`. The rig proves or refutes this quickly and still answers everything else through the hand-cut control.
* Which side keeps the original grid: `MyDisconnectHelper` keeps the grid for the largest group by block count. The printer (26-27 blocks) out-counts the payload (5-9) in every case.
* Welded battery: `MyObjectBuilder_BatteryBlock.SetupForProjector` zeroes the charge, then 30 % when functional. The blueprint writes 0.04 MWh so the four outcomes can be told apart.
* The tool sphere is tested against cell boxes; its centre and radius are unknown, so the rig uses the repo's welder-reach hypothesis family (`reach.FAMILY`: centre at the tip, tip-1, tip-2, mid; r 1.3 m / 1.0 m).
* Ship welders need components in Survival, and the printer carries none: the world must be in Creative game mode.

## 3. Design decisions

1. Hand-built with `sebp`; `printer_lib` is not patched (`BayDesign` asserts exactly one merge block). Only `printer_lib.projector_settings` and the `reach.py` helpers are reused; `verify()` in the module re-does the checks that matter.
2. Both grids use `legacy_shapes=True`, the forms the owner's game pasted for the v2.0 Mobile Hive.
3. **The ship grinder XML is the only guessed shape**: the legacy small-welder form with the grinder `xsi:type`, registered at build time in `sebp.HAND['ShipGrinder']`. The registration is process-global once it has run; a production grinder model must call `register_shapes()` itself or move it into `printer_lib.register_shapes()`. The small welders use the LARGE welder's sample (pasted in v2.0), a secondary risk.
4. **Fallback `Sprue Test No Grinder`**: the same rig without the grinder block. If `Sprue Test` fails to paste and this one pastes, the grinder XML is the culprit. The owner places a Small Ship Grinder by hand; the printer screen says `grinder pos OK` or `pos WRONG move x.. y.. z..`. `cut` is refused while the position is wrong (`cut force` overrides).
5. **No sentinel block for the own-grid control.** The neck itself is printer-grid material inside the sphere. The control is `hand` + grind **N2**, then `cut` (CUT2). N2, not N1: hand-cutting N1 and then CUT2 leaves N2 as a one-block orphan grid wedged in projected cell (0,0,2) that blocks the cycle-2 re-weld forever (`verify()` reports `3 pieces [1, 6, 25] (orphan neck cell?)`, also a unit test).
6. Neck of 7 cells, grinder flush beside the middle: every hypothesis cuts N2..N6 (r 1.3) or N3..N5 (r 1.0); the neck ends (stub, hub) sit 3.81 cells from the nearest sphere centre against r 2.6 cells.
7. Three welders on +x with their tips toward the neck. Welder 2 keeps one cell of air (with its tip on the neck its tip cells would sit 2.55 cells from a grinder centre).
8. Payload = neck 7 + hub + small battery + PB + ONE small ion thruster (11 blocks). The push is 2 % for 1 s (about 0.6 m/s at about 470 kg); the auto-push waits 15 s after the split so the owner can screenshot the payload screen; at push end the PB logs the summed `CurrentThrust` (0 N means `override ignored`, told apart from a jam).
9. Control is PB Run arguments only; one script on both PBs. The role comes from Custom Data `Role=`; if the payload PB's Custom Data is lost on weld but its name contains `Payload`, it runs on a built-in copy of its config (`PDEF`, kept equal to the generator by a unit test) and reports `cd LOST`.
10. Batteries: `weld`, `cut`, `hand` and `stop` set both back to Auto (a payload battery left in Recharge supplies nothing and the payload would look dead after the split). The payload reads its own battery by name, never `b[0]` of the grid (while fused that list also holds the printer battery).
11. Cut safety: guard cells are checked every tick in CUT/CUT2; on NO SPLIT the payload is set to HOLD so a hand cut cannot trigger an auto-push; any split outside CUT forces HOLD.
12. After a split `weld` is refused while the cached payload PB has moved less than `Clear Metres` (6 m) and still exists (`weld force` overrides); FREE logs a hint after 45 s without leaving.
13. Weld diagnosis: at 5 s `PROJECTION MISSING` or `projection has T blocks, expected 11`; progress is the summed cell integrity; a stall (45 s) names its cause (nothing buildable: ghost not attached, Offset must read 0,1,2; a welder ON but not working: power/owner; nothing on my grid: Q1 NOT FUSED? check the Info tab, do NOT hand-weld, or Survival / safe zone; some cells missing: reach stall, wait 2 more minutes, report the map, then hand-weld).
14. Small, dynamic grid, never `IsStatic`; no thrusters, gyros or controller on the printer. Paste it in space, at least 1 km from any safe zone and asteroid.
15. Not taken: a third model `Sprue Test Pod` (grinder on a second CubeGrid of the same blueprint, testing A' directly). It adds unseen multi-grid XML to a rig meant to minimise confounders; it is the natural follow-up after outcome E1a below.

## 4. Geometry (printer frame == payload frame, T = (0,0,0))

Axes: grid `Forward` = -z, `Right` = +x, `Up` = +y. The neck lies on the z axis at x = y = 0; printer at +z, payload at -z; grinder on -x, welders on +x. Coordinates are cell Min values.

Payload (projected, 11 blocks): neck N1..N7 at (0,0,3) .. (0,0,-3) (red armour; N1 is the first projected block and the projection anchor), hub (0,0,-4), `Sprue Payload Battery` (0,0,-5), `Sprue Payload PB` (0,-1,-4), `Sprue Payload Thruster` (0,1,-5) (exhaust +z).
Printer (25 blocks): stub (0,0,4) (yellow; the ONLY printer cell mount-linked to the payload, to N1), core (0,0,5), beam (1..6,0,5), `Sprue Projector` (0,1,5) (Offset 0,1,2, rotation 0, Keep Projection), `Sprue Battery` (0,-1,5), `Sprue PB` (0,0,6), `Sprue Welder 1..3` at (1,-1,2), (2,-1,-1), (1,-1,-4) (Forward=Left, Enabled false), grinder seat (-1,-5,1) and stalk to the core, `Sprue Grinder` at (-3,-4,-1) (Forward=Up, tip +y, Enabled false; absent in `No Grinder`).
Mass 2,609 kg / 733 PCU (2,120 kg / 633 PCU without the grinder); no DLC blocks.

Grinder sphere: centres (cell corner coordinates) tip (-1.5,2,0.5), tip-1 (-1.5,1,0.5), tip-2 (-1.5,0,0.5), mid (-1.5,-1,0.5). r 1.3 m (2.6 cells) cuts N2..N6, r 1.0 m (2.0 cells) cuts N3..N5; N1 is never reached. Pieces after a cut (payload, printer): fused cut [5, 26] / [6, 27]; hand cut N2 [9, 26]; hand N2 + CUT2 [5, 26] / [6, 26]. The closest protected printer cell is the tip of Welder 1, 0.16 m beyond r 1.3 m; the guard stop switches the grinder off on the first loss.
Welder coverage: tier A (`FAMILY[:4]`) reaches all 11 payload blocks (N4 under tip-1 r1.0 only tangentially); support chain 10 rounds; the exit prism and the thruster lane are free of printer cells.

## 5. SprueProbe.cs

Update10, C# 6, ASCII (26.5k characters minified), stubs API only (no `IMyShipGrinder` in the stubs: the grinder is found as `IMyFunctionalBlock` by name "Grinder" on its own grid). Every screen line is cut to 46 characters (Monospace 0.42); the same text is Echoed every run (K menu), and the full log is in Custom Data `[Log]` (printer: last 60 lines, with the payload's lines copied as `P:`; read back after a Recompile or reload, the RESULTS block is not, the log lines keep the answers).

**Armour is not readable (game finding, section 9).** In the first game test `GetCubeBlock` returned null for every plain armour cube (the 4 armour guard cells of the printer and the 8 armour cells of the payload) while battery, PB, thruster and welders were found. The script therefore reads a cell with `GetCubeBlock` and, when that gives null, with `CubeExists`: map characters are `O` full, `o` partial, `x` exists but its integrity cannot be read (counted as full), `.` missing or unreadable. BUILT then needs all cells present and, when some are `x`, `Settle Seconds` (default 25) of waiting; when the neck cannot be read at all, a BLIND fallback (`Blind Seconds`, default 40, after the projector shows 0 left and battery, PB and thruster are at 100 %) ends the weld with the log line `BUILT (armour BLIND)` and Q1 is taken from the payload PB sharing the printer's grid. A cut that does not split reports `UNTOUCHED` only when the neck cells could be compared; otherwise it says `NO SPLIT, and this API cannot read the neck: LOOK at it`. The first SNAP and the RESULTS block carry `API neck cells (armour): GetCubeBlock a/7, CubeExists b/7`, which tells us which call sees armour.

Printer commands (terminal, select `Sprue PB`, type into Argument, Run):

| argument | action |
|---|---|
| `weld` / `weld force` | batteries Auto, projector ON, welders ON, grinder OFF; cycle +1 after a split; refused while the cut payload is still in the bay (nearer than Clear Metres, or its distance cannot be read) and refused after BUILT when nothing was cut (it would overwrite the Q1 and welded-battery answers); `force` overrides both |
| `charge` | printer battery Discharge, payload battery Recharge for Charge Seconds (30), then both Auto |
| `cut` / `cut force` | refused with no grinder, with `pos WRONG`, or while the neck is not fully welded (unless `force`; a refusal also ends `auto`); welders OFF, grinder ON; state CUT (CUT2 if already split). The `UNTOUCHED` verdict needs both the lowest and the summed neck integrity unchanged |
| `hand` | tools OFF, payload HOLD: grind ONLY neck cell N2 with the hand grinder |
| `push` | `TryRun("push")` on the cached payload PB, logs true/false (false: run `push` on the payload PB itself) |
| `auto` | WELD -> CHARGE -> CUT -> FREE -> WELD ... for `Cycles` (2); completes only in the D1 world; refused while a cut payload is in the bay; after BUILT it continues with charge |
| `stop`, `snap`, `reset` | tools OFF and IDLE / SNAP line / clear log and results |

States: IDLE, WELD (`PLACED`, then `BUILT in Ns ... -> Q1 FUSED`), CHARGE, CUT / CUT2, HAND, FREE (`PAYLOAD LEFT`). Verdicts of a CUT with no split: `UNTOUCHED (valid, D3)`, `INVALID (D4)`, `ground, not through (D5)`; `GUARD HIT (D2)` stops the grinder. The payload role logs `COMPILED boot N`, `SPLIT seen by the payload`, `PUSH`, `push end` and writes a `[Status]` line to its Custom Data every 10 runs. The printer screen ends with a RESULTS block (Q1 .. Q4 lines).

## 6. In-game procedure

0. New world, Creative, space, away from safe zones, scripts ON. Paste `Sprue Test` (fallback: `No Grinder` plus a hand-placed grinder until `pos OK`). Screenshot the Sprue PB screen: `proj on rem 11 bld 1 tot 11`, `grind off`, `grinder pos OK`, `guards 31/31 full, min 100%`.
1. `weld`; wait for `BUILT` (1-3 min). Screenshot the printer screen and the Sprue Payload PB terminal (K menu, still fused). RESULTS holds Q1 and Q3.
2. `charge` (30 s).
3. `cut` (up to 60 s). 3a `SPLIT ... CUT OK`: screenshot the payload within 15 s, it pushes itself away. 3b `NO SPLIT ... UNTOUCHED (valid, D3)` (expected): go to 4. 3c `INVALID (D4)`: fix power, owner or placement and repeat. 3d `ground, not through (D5)`: `cut` again. 3e `GUARD HIT (D2)`: screenshot the guards.
4. (control) `hand`: grind ONLY N2 with the hand grinder; expect `SPLIT ... in state HAND`. Then `cut` (CUT2, 60 s), then `push`.
5. Delete any loose 1-block grids. `weld` (cycle 2): expect `Q4 c2 re-weld BUILT` and a new `COMPILED boot 1`.
6. Report: screenshots, the Sprue PB `[Log]` text, world settings (game mode, welder/grinder multipliers, safe zone nearby), and the Info-tab grid list (names and block counts) before and after the split.

## 7. Decision tree (outcome -> next engineering step)

**A. Paste.** A1 OK: continue. A2 `Sprue Test` fails and `No Grinder` pastes: the grinder shape is wrong; ask for a Ctrl+B export with the hand-placed grinder, `extract_shapes.py`, rebuild. A3 both fail: PB, projection or welder legacy shape; bisect with `tools/probe_blueprints.py` P13/P14/P17 and the game log; printers stay blocked.
**B. Weld (Q1).** B0 `PROJECTION MISSING` or wrong block count: fix before any Q1 conclusion. B1 `Q1 FUSED` (expected): printers-01 confirmed, the merge release is dead, unknowns 13 becomes confirmed. B2 the stall says `NO payload block on my grid` AND welded blocks are visible AND the Info tab lists them as another grid (payload PB: `printer PB not visible`): the premise is wrong, STOP option A, run P1/P2 on a merge-bay printer and test HiveMotherAI RELEASE as shipped (decide B2 only from the Info tab plus the payload screen). B2' same stall with nothing welded: world setting (Survival, safe zone, power), repeat. B3 `reach stall` with some cells missing: the welder sphere is smaller or deeper than tier A; shrink tier A in `docs/welder-reach.md`, use more welders and tip contact (accept this conclusion only in this branch).
**C. Q3.** C1 `COMPILED boot 1` + heartbeat: auto-compiles and runs; delete `ApplyAction("Recompile")` in HiveMotherAI (printers-04 dead code). C2 code but no heartbeat: no auto-compile, record in unknowns 4. C3 code missing: stripped, projected scripted drones are impossible. C4 `cd LOST`: Custom Data stripped; drones cannot rely on `[Launch]` Custom Data from a projection, config must be compiled in or sent by IGC. Owner `=me` (re-owned to the welder/printer owner), `(=BP)` (ghost XML owner kept), `0 (Nobody)` (owner fix needed). Battery 0.015: 30 % (keep CHARGE); 0.04: blueprint value kept (write 0.05 in projections); 0: empty (CHARGE mandatory); `CHARGED` with no rise: Discharge-to-Recharge does not work.
**D. Cut (Q2).** D1 SPLIT in CUT, guards 31/31: contradicts the source, **option A works as written**: build the Gnat sprue bay with opt-in sprue support in `printer_lib`, move the ShipGrinder registration there; HiveMotherAI RELEASE = welders OFF, grinder ON until the drone PB is off the printer grid, then OFF; COOLDOWN waits for distance, not `RemNow > 0`. D2 `GUARD HIT` or payload damage: the sphere is larger or centred differently; the damaged cells give its size; neck 9 and more keep-out first (use only the SPLIT-line / GUARD HIT guards, taken before the push). D3 `UNTOUCHED (valid, D3)` (expected): **option A as written is dead**, go to E. D4 `INVALID`: fix and repeat. D5 `ground, not through`: grinding is slow, measure and size Cut Seconds.
**E. Control after D3 (hand N2 + CUT2).** E1 the hand cut gives SPLIT: removal splits. E1a CUT2 valid and the middle cells vanish: the grinder works on OTHER grids and the geometry is right; next design **A' = grinder on a rotor/hinge head of the printer** (small rotor XML unseen, paste probe first; collateral next to an other-grid grinder is untested, so a `Sprue Test Pod` variant comes first) or **option B = projector on a rotor head**. E1b CUT2 valid but nothing vanishes: reach geometry wrong, build a reach ladder before any A'. E1c CUT2 INVALID: fix and repeat. E2 no split after the hand cut: another link exists (Info tab, welder tips, a merge face).
**F. Leave and repeat (Q4).** `TryRun(push) true`: a printer can command a cut drone through a cached reference; `false`: arm drones BEFORE the cut. F1 `PAYLOAD LEFT`: free. F2 `PAYLOAD STUCK`, thrust > 0, moved about 0: jammed against the welder tips, real bays need one cell of clearance; `thrust was 0 N (override ignored)`: thruster override needs a controller (drones have one). F3 cycle 2 `BUILT` + new `COMPILED boot 1`: repeatable with Keep Projection. F4 projection gone: HiveMotherAI must re-assert Keep Projection. F5 the neck does not regrow: payload still near or a loose cube in a missing cell; COOLDOWN must wait for distance and release must leave no orphan cells. `printer kept its grid NO`: the largest-group rule differs; production printers must out-count drones.

## 8. Results table (first game test 2026-10-14 filled in; the rest is open)

| item | result |
|---|---|
| Paste: Sprue Test / No Grinder | `Sprue Test` pasted and ran (the guessed grinder XML was accepted; the grinder block is in the terminal list) |
| Q1 fused | payload PB on the printer grid (both PBs report the same grid id; `onprinter=1`); neck armour stays with the projector off (real blocks, not the hologram) |
| Q2b grinder on own grid (cut) | |
| Q2 hand N2 + CUT2 | |
| Q2c guards / payload damage | |
| Q3 program / heartbeat / Custom Data | YES: compiled by itself about 10 s after the weld started (`COMPILED boot 1`), heartbeat rising, `cd=ok`, no Recompile |
| Q3 owner (raw ids) | blueprint owner ...915, welded PB owner ...919 = the printer owner (the welder's owner replaces the blueprint's) |
| Q3 welded battery MWh | 0.05/0.05 at 97 s (blueprint wrote 0.04): full, or charged by the printer while fused; not separable, the BUILT-time value was never recorded (armour blind) |
| Q4 payload leaves / cycle 2 | |
| World: game mode, multipliers, safe zone, new world | |

## 9. First game test (2026-10-14): what it showed

1. **Nothing is automatic**: the welders are saved off and start only on `weld`.
2. **The welded PB is on the printer's grid and runs by itself** (Q1 for the PB, Q3 yes). A scripted drone can be printed: the program, its Custom Data and an owner survive the weld.
3. **Plain armour cannot be read by `GetCubeBlock`** (12 of 12 armour cells read null, 0 of 7 non-armour cells did). The first script version needed the armour cells, so it never reached BUILT, kept the welders running and its `cut` checks were blind; the current script reads armour with `CubeExists` and falls back as described in section 5. The jump from "neck not found" to "neck is real" came from switching the projector off (blocks stayed) and the guards line (27/31 = exactly the 4 armour guards).
4. **Consequences outside this rig (not fixed here):** the `HiveMotherAI.cs` FINISH scan (counts unique blocks by `GetCubeBlock` over the drone box) and the lane check of `CradleBayAI.cs` (`GetCubeBlock != null`, so armour in the lane is invisible) assume armour is readable; both need `CubeExists` as a second opinion before they can be trusted in the game. The flown drones' damage scan skips cells with `GetCubeBlock == null`, so it never counted armour.
5. Still open: the cut itself (Q2), the control, the push and the second cycle (Q4).

Release zips are not rebuilt.
