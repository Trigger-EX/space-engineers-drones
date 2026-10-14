# Wasp Split (test copy, two programmable blocks)

Copy `Blueprints/Wasp Split` into your Space Engineers Blueprints folder (delete any old "Wasp Split" folder first: stale `.sbcB5` cache). It has two PBs with their scripts already inside: `[Flight] Wasp Split PB` (WaspFlightAI, ~76k) and `[Staff] Wasp Split PB` (WaspStaffAI, ~33k). Scripts are also in `Scripts/` for manual paste. Design notes: docs/design/wasp_split.md, findings: docs/audit/multi_pb_feasibility.md.
Test in game: both PBs run without errors, Staff screen shows link OK, `hunt` on either PB, `launch`/`follow`/`satellite` work, drone keeps fighting if the Staff PB is switched off.
Printers: set the bay Prefix to `[Flight]` (the Staff reads the printer's [Launch] section from the Flight PB too).
Sim (not the game): 10 seeds each, Wasp vs Wasp Split, HUNT start, 700-900 m: vs Watcher 10/10 vs 10/10 (median kill 7.9 s vs 14.2 s); vs Gnat 10/10 vs 10/10 (23.0 s vs 19.1 s); no exceptions. Zips not rebuilt.
