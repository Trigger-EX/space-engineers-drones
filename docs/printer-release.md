# Printer release: timer chain and the nose lane

Owner: `tools/designs/printer_lib.py` (`add_bay_timers`, `front_prism`, `exit_prism`), `scripts/HiveMotherAI.cs` v2.3, `tools/designs/reach.py` (lane checks).
Nothing here has run in the game. The simulator does not model welded blocks, so none of it was simulated either; the generator's geometry checks are the only test.

## 1. Timer chain: release without the Programmable Block

Every bay of every printer now carries three timers (named `Timer Build [Bay N] (...)`, `Timer Release [Bay N] (...)`, `Timer Reclamp [Bay N] (...)`, sitting
on the pod shell). They run the bay by themselves, so a printer whose script is missing, crashed ("Script too complex" stops a script for good) or simply
not wanted still releases its drones:

| timer | delay (default) | toolbar | what it does |
|---|---|---|---|
| Timer Build | about 8 x the ideal weld time, 120 s minimum (Gnat ~2-3 min, Hornet ~5 min) | bay welders OFF (block group `Bay N Welders`), Start Timer Release | the build window ends |
| Timer Release | 20 s | the bay's printer merge block OFF, Start Timer Reclamp | the drone charges from the printer for 20 s, then the merge pair opens |
| Timer Reclamp | 30 s | merge block ON, bay welders ON, Start Timer Build | the old drone has left the projection; the next print starts |

**To use it:** press `Start` on `Timer Build [Bay N]` once (terminal, a button panel, or the toolbar). The bay then loops forever. `Stop` on any of the
three stops it. The blueprint does not start the timers.

**What the drone does when the merge pair opens:** its own LAUNCH block (all armed drones, `docs/launch-protocol.md` trigger 1) sees that its merge block is
no longer connected and flies away from the printer on its own. A drone without a working script stays where it is (separated, but not flying); the timers
cannot fix that.

**Script and timers together:** `HiveMotherAI.cs` leaves a bay alone for as long as one of its timers is counting down (PB screen: "timer cycle running -
script paused"). When the timers are stopped, the script takes the bay back from scratch (a finished drone still standing in the bay is recognised by the
finish scan). Use one or the other per bay.

**Limits, stated plainly:**
* The delays are guesses. A timer cannot know when the print is finished (the projector's remaining-block count is not an Event Controller event), so a delay that is too
  short releases a half-built drone; too long only wastes time. Tune `Timer Build` (1 s to 1 h).
* The timers open the **printer-side** merge block only (the drone-side block belongs to the projection and has no entity id in the blueprint). That is enough
  when the drone is a separate grid. If welded blocks become part of the printer's own grid (`docs/audit/printers.md` printers-01, the biggest open risk), no
  merge switching can split them, with timers or with the script. In that case the drone must be cut off by grinding.
* Timer `Start` and `OnOff` actions are seen in the Tech Hunter sample; the `Delay` field is in milliseconds as the game writes it. The group action on welders is a
  group item as seen in other samples.

## 2. Nose lane: welders no longer stand in front of the drone

Report: "sometimes welders cover the front of the drone so it can't properly take off". Measured on the old layouts: 5 of 11 Hornet welders (Lite) stood in
the swept path of the nose (16 cells = 8 m). The old solver only kept the **exit lane** free (the direction the drone leaves along, 14 cells), and for a
Hornet the cheapest exit was sideways, so the welders were free to gather in front.

Changes in `printer_lib.py`:
* **Nose lane (`front_prism`)**: the drone's footprint swept 16 cells along its own Forward is forbidden to welders, armour, projector and to every other bay
  (it is part of the bay's `forbid` set). A drone can therefore always fly straight ahead out of its bay. The solver first tries every exit with the lane;
  only when the lane costs more than 2 tier-A (block, sphere hypothesis) pairs compared with the best layout without it (`FRONT_MISS_TOL`) does it fall back to
  the old behaviour (`design.front` False, `stats['front']`, the verifier then reports `nose_lane_blocked` as a note instead of a problem).
  Result for all 10 drones in Full and Lite: every bay keeps the lane, 0 welders in front of the nose (before: 5 of 11-12 welders for Hornet, Wasp, Lancer and
  Longbow S; the other drones were not measured). The Full Hornet-class drones miss 1 pessimistic pair (a 1.0 m sphere at a camera or the Remote Control). Skipped when the printer's own merge block sits in front of
  the nose.
  Cost for the Hornet (Lite): 11 -> 12 welders, best-effort (deeper than the welder tip) coverage 233 -> 356 unmet weight, all tier-A pairs still reached.
* **Exit lane 14 -> 18 cells (9 m)**: the launch block burns until the drone is 8 m away (`Clear Distance`); with a 7 m lane the drone ran into the next
  bay's scaffold (Hornet Forge, bay 1 leaving towards bay 2: first obstruction at 7.5 m).
* **Array bays leave outwards**: the mirrored exit of bays in a row is now forced (`solve_bay_auto(prefer, force=True)`); before, the preference was only a
  tie-break and both Hornet Forge bays left to the right, bay 1 towards bay 2.
* `reach.py` checks both lanes (`exit lane ... blocked`, `nose lane ... blocked`) for every bay of every printer.

What this does **not** solve: a drone whose own thrusters or script do not take it out (see launch-protocol.md), or a real welder sphere that is larger than the
model (docs/welder-reach.md says its centre is unknown).
