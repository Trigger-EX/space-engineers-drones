# Handoff: Ballista tuning, Helm split

## Goal
Keep the drone/warship project consistent (scripts, generators, sim, zips). Nothing has run in the real game except Wasp Split and the owner's Ballista tests below.

## Decisions and constraints
- Work and push on `main` only; no PRs; zips only when asked; few sim seeds; PB size: add a PB (Opus plan first), never raise the cap (CLAUDE.md).
- Ballista (owner game tests drove these): `Standoff Range=1600` with `Standoff By Weapons=false` (sim: 1,600 m never hit by a 100 m/s Tech Hunter, 1,000 m always hit); `Auto Camera Scan=false` (idle sweep spun the ship); wing spacing keys (300 m / 12 m/s / 45 m fire clearance); 4 gatling PD turrets; `Kamikaze Rail Hold Range=250` + `BatKz` (tracking stays free for the launcher); `1st contact` line on the Helm screen. Details `docs/design/ships/Ballista.md` 3b-3e.
- Helm split (`docs/design/helm_split.md`): FLEET channel + damage sensing now in `StaffAI.cs` 1.5 (HELMLINK 1.2, H2S bit 8); ShrikeAI 81,100 min, gate 86,000; Ballista `[Staff][SH]` (Mothership=false), Shrike `[Staff] Shrike`.
- Charge test: `pc/plans/charge_standoff.plan` (+ `tools/gen_charge_plan.py`, `tools/charge_standoff_report.py`): owner runs it, then I adjust each design's standoff.

## Current state
All on `main`, zips NOT rebuilt. `selftest hsplit m2 m3 m6` green; `m1 m4 m5` and 5 scenario checks fail as before the split. Five blueprints (Wasp, Aegis, Mantis, Longbow S/L) failed `build_all` at 100k until the thrust-vector split fixed them (check `build_all`).

Aegis Carrier (Starscape-style disc mega carrier, `tools/designs/aegis_carrier.py`, `docs/design/ships/Aegis_Carrier.md`) is built and tested (28 tests), 24 drone slots in 6 pods; sim smoke and compile check not run (no dotnet / mcs in the cloud box); zips not rebuilt.

## Open questions
Why the 4-Ballista cluster did not lock on while the pilot was in the line (friendly in the way? unverified); kamikaze defence unverified (sim Stingers crawl); HUD/menu (cut 2) not moved.

## Next step
Owner rebuilds and tests the Ballista wing; or sends the charge-test report, then set each design's standoff from it. Older items (Sprue Test, rotor turrets in-game tests) are in git history of this file and `docs/design/sprue_test.md`, `turrets.md`.
