# Handoff: v2.7 + rotor AI turrets

## Goal
Keep the Space Engineers drone/warship project consistent: scripts, generator, simulator, release zips. Nothing here has run in the real game.

## Decisions and constraints
- **Multi-PB is proven in the real game** (Wasp Split, owner-tested): no practical script-size limit; split modules into extra PBs over IGC instead of dropping features (CLAUDE.md "Script size").
- Work and push on `main` only (CLAUDE.md). Sim runs: few seeds (CLAUDE.md).
- **Rotor + hinge AI turrets** (this session): `scripts/TurretAI.cs` 1.0 (17,316 chars), `tools/designs/turrets.py` (PD4L large, PD2S small), `sebp.Blueprint.mount()`, `sebp_multi.top_pose/TOP_OFFSETS` (measured on the Tech Hunter), docs `docs/design/turrets.md`, tests `tools/tests/test_turrets.py`. On Shrike 1, Corsair 2 (Full only), Warden 4, Leviathan 8 (Lite 10). Names `[TU] ...`, PB `[TU] Turret PB`; every Helm Custom Data has `[Link] Block Prefix=[SH]`.
- Audit: `docs/audit/SUMMARY.md`; SATELLITE formation `docs/follow-mode.md`; warships `docs/design/ships/*.md`, `control_arch.md`. Helm `ShrikeAI.cs` is 79,977 of 80,000 chars: no headroom.
- Mono works here (`apt install mono-mcs mono-runtime mono-devel`; `sim/build.sh`). Baseline: 18 `test_sebp_ext` tests need the owner's `ref/user-samples` (not in the repo) and fail without it.

## Current state
Release zips are NOT rebuilt for the turrets (CLAUDE.md: only when asked; `python3 -I tools/build_all.py` takes about 12 minutes). `selftest m6` and `ship/Warden-vs-Pirate` pass with the turrets; the sim has no rotor model, so the turret heads are inert there and no combat result exists.

- Ballista (post-v2.7, zips not rebuilt): large-grid railgun drone (27 rails, ~8,200 PCU with its docked Probe Foundry, 9,990 of the 10,000 in the test; 3 large reactors + 13 batteries = 1,056 MW for the 1,026 MW recharge of all rails, 22 thrusters, 13 gyros: `Ballista.md` section 9, zips not rebuilt, not run in sim), Peeper probe, `SpotterAI.cs` (vitals memory, feed to the Helm, foundry control), ShrikeAI `<Tag>_AIM` hook (Helm budget now 81,600 chars). Game-test fixes (salvo, AI assist, AI-behavior gate): `Ballista.md` 3a; run-off at paste NOT reproduced in the sim. `docs/design/ships/Ballista.md`, `tools/tests/test_ballista.py`. Not run in game or sim; the printed-probe separation risk (audit printers-01) applies.
- **Sprue Test** (option A of the printer fix, zips not rebuilt, nothing run in game or sim): `tools/designs/sprue_test.py` (`Sprue Test`, `Sprue Test No Grinder`, kind test), `scripts/SprueProbe.cs` (both PBs), `docs/design/sprue_test.md`, owner copy `test_designs/Sprue Test/`. Keen's old source says a ship grinder skips its own grid, so the main cut is expected to be `UNTOUCHED`; the hand-cut control (grind N2, then `cut`) shows whether a grinder works on another grid. The new script and blueprints change the sim provenance key: finish or restart interrupted `pc/` plan runs before `--resume`.

## Open questions
In-game: `packages/tests.md` 38-42 (paste/attach of the rotor turrets, motion, Helm ignores them, masks/friendly fire, ammo), `packages/unknowns.md` K-T1..K-T12, plus the earlier list (merge clamp face T1, cockpit toolbar Run argument K3, IGC across docked pods, ammo/hydrogen feed). Printer option A: Q1 fused, Q2 grinder on own grid / other grid, Q3 welded PB, owner and battery, Q4 payload leaves and the neck re-welds (`docs/design/sprue_test.md` section 1).

## Next step
The owner runs `test_designs/Sprue Test` in a creative world and sends the Sprue PB screen and `[Log]`; follow the decision tree in `docs/design/sprue_test.md` section 7 (expected: D3 then E1a, so the next design is a grinder on a rotor/hinge head, A', or the projector on a rotor head, B).
Add a `SimMotorStator` (velocity mode, limits, the `top_pose` chain) and head-grid weapons to the simulator, then run 3-10 seeds of turret scenarios (Hornet pair, Pirate, kamikaze swarm). Or run the in-game tests first and fix what they show.
Run the in-game tests (Ballista list: `docs/design/ships/Ballista.md` section 8, starting with `SeekerProbe.cs`), then fix what they show. Known weak spots: lone Shrike (2/5 vs a Hornet), Lite ships have no AI blocks and find nothing alone, drone recovery into cradles (phase 2), `pc/reference` and `docs/RESULTS.md` need regenerating after the sim fidelity changes.

## Chat
Base name: ultracode drone audit ships
Run: 2
