# Sprue Test (test copy: does option A release a printed drone?)

Copy the folders from `Blueprints/` into your Space Engineers Blueprints folder (delete any old "Sprue Test" folders first: stale `.sbcB5` cache). There are three; paste ONE at a time:
  Sprue Test            the first choice: the grinder lies BESIDE the neck.
  Sprue Test Close      the same rig with the grinder's tip pointing straight at the middle of the neck, one block of air in front of it. Use it when `cut` and then the hand-cut control (hand, grind N2, cut) both leave the neck alone: then the grinder simply did not reach it. The sphere may also touch the tips of Welder 1 and 3 (the guard stop switches the grinder off as soon as a guarded welder cell is damaged or a guarded armour cell disappears: with this layout that is the expected collateral if the grinder can cut its own grid, just run cut again).
  Sprue Test No Grinder fallback if the first one does not paste (place the grinder by hand, see below).
World: a NEW world, game mode CREATIVE (not Survival with creative tools: ship welders then need components), in SPACE, at least 1 km from any safe zone and asteroid, in-game scripts ON (Experimental mode). Note for the report: game mode, welder/grinder speed multipliers, safe zone nearby yes/no.
Paste "Sprue Test" (or "Sprue Test Close"). If the paste fails, paste "Sprue Test No Grinder" (and send the game log of the failed paste) and place a Small Ship Grinder by hand: its back face on the yellow seat cube, its long body lying flat against neck cells 3-5 (counted from the yellow stub at the printer core; the neck is only a projection until you run weld, red once welded) on the side AWAY from the green welders, its tip pointing the way the blue projector sticks out of the core, one cell past the neck. The Sprue PB screen says "grinder pos OK" when it sits right, or "pos WRONG move x.. y.. z.." (x+ = toward the welders, y+ = projector side, z+ = toward the printer core).
Both PBs carry SprueProbe (also in Scripts/). Commands: terminal, select "Sprue PB", type the word into Argument, press Run. If you cannot see a PB screen: K menu, select the PB: the same text is on the right.
  weld   -> 1-3 min, WAIT for "BUILT ... Q1 FUSED" before anything else (nothing starts by itself: the welders only run after weld). BUILT may say "(armour BLIND)": the PB cannot read plain armour blocks in this game (seen in the first game test), so it ends the weld from the battery, PB and thruster and a timer. That is expected, not an error. Screenshot the Sprue Payload PB terminal now (it is still on the printer grid).
  charge -> 30 s (not before BUILT).
  cut    -> up to 60 s. "NO SPLIT ... UNTOUCHED (valid, D3)" is the EXPECTED result, not a broken rig: continue with hand. If "SPLIT ... CUT OK": screenshot the payload PB screen within 15 s, then it pushes itself away ("PAYLOAD LEFT").
  hand   -> grind ONLY the 2nd red neck cell from the yellow stub (N2) with your hand grinder -> "SPLIT". Then cut (60 s), then push.
  weld   -> the neck and a new payload are welded again (cycle 2). Before it, delete any loose 1-block grid (Info tab grid list).
  auto   -> only completes when cut gives SPLIT; otherwise use the steps above.
Stay out of the grinder's reach while it is ON. Screenshot the Sprue PB screen (the RESULTS block holds the answers) after each step, and copy the [Log] part of Sprue PB's Custom Data (it also holds the payload's log as P: lines) into the report.
If something goes wrong:
  blank PB screen -> world In-game scripts + Experimental ON; K > PB > Edit: code there? Recompile; if empty paste Scripts/SprueProbe.cs.
  the t number on line 1 stops counting -> copy the red error text from the PB terminal, then Recompile.
  both pastes fail -> send %AppData%\SpaceEngineers\SpaceEngineers_*.log.
  "cut refused: neck not fully welded" -> wait for BUILT (or type cut force); "weld refused: already BUILT" -> go on with charge / cut (weld force restarts and overwrites Q1).
  WELD STALL "nothing buildable" -> projector terminal Offset must read X 0, Y 1, Z 2.
  WELD STALL "NO payload block on my grid" -> Creative mode? safe zone? If you SEE welded grey blocks, that is a result (not fused): do NOT hand-weld, screenshot the Info tab grid list and the payload PB.
Design and decision tree: docs/design/sprue_test.md. No simulation was run (the simulator models neither fused grids nor grinders). Zips not rebuilt.
