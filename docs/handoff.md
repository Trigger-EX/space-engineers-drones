# Handoff: v2.7 + rotor AI turrets

## Goal
Keep the Space Engineers drone/warship project consistent: scripts, generator, simulator, release zips. Nothing here has run in the real game.

## Decisions and constraints
- Work and push on `main` only (CLAUDE.md). Sim runs: few seeds (CLAUDE.md).
- **Rotor + hinge AI turrets** (this session): `scripts/TurretAI.cs` 1.0 (17,316 chars), `tools/designs/turrets.py` (PD4L large, PD2S small), `sebp.Blueprint.mount()`, `sebp_multi.top_pose/TOP_OFFSETS` (measured on the Tech Hunter), docs `docs/design/turrets.md`, tests `tools/tests/test_turrets.py`. On Shrike 1, Corsair 2 (Full only), Warden 4, Leviathan 8 (Lite 10). Names `[TU] ...`, PB `[TU] Turret PB`; every Helm Custom Data has `[Link] Block Prefix=[SH]`.
- Audit: `docs/audit/SUMMARY.md`; SATELLITE formation `docs/follow-mode.md`; warships `docs/design/ships/*.md`, `control_arch.md`. Helm `ShrikeAI.cs` is 79,977 of 80,000 chars: no headroom.
- Mono works here (`apt install mono-mcs mono-runtime mono-devel`; `sim/build.sh`). Baseline: 18 `test_sebp_ext` tests need the owner's `ref/user-samples` (not in the repo) and fail without it.

## Current state
Release zips v2.7 rebuilt in `releases/` (Drones, Printers, Warships, All). `selftest m6` and `ship/Warden-vs-Pirate` pass with the turrets; the sim has no rotor model, so the turret heads are inert there and no combat result exists.

## Open questions
In-game: `packages/tests.md` 36-40 (paste/attach of the rotor turrets, motion, Helm ignores them, masks/friendly fire, ammo), `packages/unknowns.md` K-T1..K-T12, plus the earlier list (merge clamp face T1, cockpit toolbar Run argument K3, IGC across docked pods, ammo/hydrogen feed).

## Next step
Add a `SimMotorStator` (velocity mode, limits, the `top_pose` chain) and head-grid weapons to the simulator, then run 3-10 seeds of turret scenarios (Hornet pair, Pirate, kamikaze swarm). Or run the in-game tests first and fix what they show.

## Chat
Base name: ultracode drone audit ships
Run: 2
