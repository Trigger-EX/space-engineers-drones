# Handoff: v2.7 (audit fixes, SATELLITE formation, four warships)

## Goal
Keep the Space Engineers drone/warship project consistent: scripts, generator, simulator, release zips. Nothing here has run in the real game.

## Decisions and constraints
- Work and push on `main` only (CLAUDE.md). Sim runs: few seeds (CLAUDE.md).
- Audit: `docs/audit/SUMMARY.md` (+ per-domain reports). Fix plan applied except items 7, 11, 12 (printer redesign, sim printing realism, blueprint shape fidelity beyond what the warships needed) and the Stinger item.
- SATELLITE formation: FOLLOW block v1.1 (`tools/follow_block.cs`), Gnat has its own copy; `docs/follow-mode.md`.
- Warships `Shrike` (fighter), `Corsair` (corvette), `Warden` (frigate), `Leviathan` (dreadnought) + `Cradle Pod`, Wing variants: `tools/designs/{shrike,corsair,warden,leviathan,pods}.py`, docs `docs/design/ships/*.md`, spec `docs/design/control_arch.md`. Scripts: `ShrikeAI.cs` (Helm, 79,990 of 80,000 chars: no headroom), `StaffAI.cs`, `CradleBayAI.cs`. Hull builder `tools/hulls.py`; generator inventories/toolbars/multi-grid `tools/sebp*.py`.
- Headroom: Wasp 98,971 and Mantis 98,468 of the 100,000-char PB limit.
- Release zips v2.7 in `releases/` (Drones, Printers, Warships, All).

## Current state
All checks pass on `main`: compile_check, blocks_sync, minify_check, tools/tests/run_all.py, full build_all, `SimHost selftest all`.

## Open questions
In-game tests first: `packages/README_Warships.md` ranked list, `packages/tests.md` 24-35 (merge clamp face T1, cockpit toolbar Run argument K3, cockpit surface indices, gyro/stick feel, IGC across docked pods, ammo/hydrogen feed). Printers remain no-go as designed (docs/audit/printers.md).

## Next step
Run the in-game tests, then fix what they show. Known weak spots: lone Shrike (2/5 vs a Hornet), Lite ships have no AI blocks and find nothing alone, drone recovery into cradles (phase 2), `pc/reference` and `docs/RESULTS.md` need regenerating after the sim fidelity changes.

## Chat
Base name: ultracode drone audit ships
Run: 1
