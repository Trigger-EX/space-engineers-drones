# Handoff: LEAD v1.0 (manoeuvre-aware lead), drones v2.3

## Goal
Fix the Basilisk's rockets missing an enormous target doing basic evasive manoeuvres (in-game report 2026-10-13), then port the
improved lead prediction to every armed drone script that benefits.

## Decisions and constraints
- Shared block `tools/lead_block.cs` ("LEAD v1.0"), synced by `python3 -I tools/lead_sync.py --apply` into HornetDroneAI, GnatSwarmAI,
  LongbowSniperAI, WaspAI, LancerAI, HarrierAI, AegisAI, MantisAI, BasiliskAI. Hooks: Track fields `LAcc, LSeg, LW, LOR, LSegN, LSegT0,
  LManT`; `LeadSpin(this, o, dt)` first in Track.Ori; `LeadSample(this, v, now, now - Seen)` first in Track.Set.
- Prediction: fresh acceleration (0.8 newest, x0.9), held for `max(ManT - age, 0.3 ManT, 2 (age - ManT))`, then constant velocity;
  aim offset lateral only, spin for <= 1 s, hull point must keep `Aim Margin (predicted misses)` (default 1) x `0.5 AccMag T^2`.
- Rockets (Basilisk, Wasp): `Rocket Velocity Model (CAPPED/NONE/FULL)` default CAPPED (ship velocity + 100 m/s, +600 m/s^2 along
  the barrel, speed capped 200 -> curves); closed-form `RktPath`, Newton solve. Old key "Projectiles Inherit Ship Velocity" no longer read there.
- Versions: scripts 2.5-* (Gnat 1.12, Longbow 2.3); package changelog entry "v2.3" (main already had v2.2 = FOLLOW, scripts 2.4). StingerAI.cs NOT changed.
- Sim: `SimSettings.RocketInherit` (default 1 = CAPPED), `PirateEvasive` / `PirateEvasiveSpeed`; selftest combat passes (104 checks).
- CLAUDE.md: commit and push directly to `main`.

## Current state
- All 14 scripts compile (tools/compile_check.sh); lead/fleet/ai-gate sync checks pass. Drone blueprints rebuilt in out/ (not tracked).
- Bench results in docs/lead-v1.md (rockets 50 -> 62 % hits, gatling 70 -> 90 %, railgun 77 -> 90 %).
- Sim (300 seeds, Basilisk vs Pirate, plain): per-run hit rate 85.5 % (v2.3) -> 89-90 % (v2.4); mean score 42.5 -> 42.9 (margin 1),
  45.4 (margin 0). Evasive Pirate (300 seeds): v2.3 mean score 28.4 -> v2.4 32.3 (margin 1), 33.6 (margin 0); wins 1/11/34 % -> 10/22/41 %. Full A/B matrix (1v1 + pirate, plain + evasive,
  100 seeds) was started into the session scratchpad and is lost with the container.
- NOTHING IS COMMITTED YET: an auto-mode safety check blocked all shell commands mid-session.

## Open questions
- In-game: do rockets curve when fired from a ship moving sideways (CAPPED) or fly straight (NONE)? Test 17 in packages/tests.md.
- Aim Margin 0 vs 1 for rockets in the sim (0 scored 2.5 points higher vs the plain Pirate, within noise).

## Next step
Commit and push the working tree to `main`, then re-run the sim A/B matrix
(`mono out/sim/SimHost.exe run --filter "^(1v1|pirate)/" --seeds 100` with old vs new blueprints, plus `--pirate-evasive true`),
add the results to docs/lead-v1.md, run `python3 -I tools/build_all.py` for the v2.2 zips (VERSION = '2.3' in tools/build_all.py).

## Chat
Base name: Basilisk missile lead fix
Run: 1
