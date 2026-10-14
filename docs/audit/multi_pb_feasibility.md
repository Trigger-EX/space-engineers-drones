# Multi-PB feasibility (bypassing the 100,000-char script limit)

Date 2026-10-13. Sim probe: `mono out/sim/SimHost.exe multipb-probe [--seeds N]` (`sim/src/Run/MultiPbProbe.cs`, 2-3 seeds used). Everything marked [sim] is simulator behaviour, [game?] is unverified in the real game.

## Verdict: feasible, with caveats
The limit is per script ("No scripts can have any more than 100 000 characters", wiki, Anatomy of a Script), so N PBs give N x 100k. The warships already do it (Helm 79,995 + Staff 23,086 + Bay 17,339 chars).

## Measured [sim]
- IGC same grid: one-way 1 tick, round trip 2 ticks (4 with 2-tick latency). Update10 receiver: RTT 3-11. Update100: up to 89.
- Control loop split over IGC at Update1: the answer is 2 ticks old (3 with a 3-PB ring). Never 0 or 1.
- Receive queue 25, oldest dropped. A receiver on Update1 sustains 25 msgs/tick; on Update10 only 2.5/tick.
- `TryRun` + reading `W.CustomData`: age 0 (synchronous), nested calls work, re-entry/self returns false. Budget sharing [game?] unknown.
- Timers: `Trigger`/`TriggerNow` chains add 0 ticks (<=1 if the target PB ran earlier that tick). `Start` countdowns are 1 s (60 ticks) each. A timer carries only a fixed argument, never data. Sim delivers it as `UpdateType.Terminal` (audit expects `Trigger` in-game).
- Corsair and Warden (3 PBs each) won 5/5 seeds vs 2 Hornets; all PBs ran, no exceptions; selftest M6 passes for them.

## Static split estimate (minified chars)
Movable off the hot path ~29k (LAUNCH 9.6k, FOLLOW 9.0k, FLEET send 3.2k, Display 2.7k, half of config 2.5k); duplicated overhead ~6k per extra PB. Wasp 98,971 -> flight PB ~71.5k + staff PB ~35k. The unsplittable hot path (tracking, lead, aim, control, core, state) is ~55-60k, so the flight PB stays the limit, not the PB count.

## Rules that follow
1. Split by function, not by tick: staff/launch/follow/display/fleet in extra PBs over IGC (`CurrentConstruct`); keep aim/lead/fire in one PB.
2. Never put a 1-s timer in a control path; timers only for waits >= 1 s and watchdogs.
3. `TryRun` is the only zero-lag route; unverified, so not used.
4. Each extra PB: +100 PCU, +138 kg small / 534 kg large; fault isolation is a bonus.

## To test in game [game?]
IGC latency/drops (Keen forum reports slow/lossy IGC), `TryRun` return and budget, timer `Trigger` UpdateType, minimum timer delay, PB count limits on servers.

Zips not rebuilt.
