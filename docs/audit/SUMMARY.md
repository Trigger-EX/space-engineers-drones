# Audit summary (2026-10)

SIM VS GAME. The core matches. Mass, COM, inertia, per-direction thrust, the 100 m/s clamp, gyro override, data-driven weapon values, camera charge/cone, and determinism all check out, and swept projectiles do not tunnel. These are wrong or unverified:
- (Confirmed) The functional threshold ignores the critical component's own integrity. Large thrusters and batteries die with 1.6-5.9x less damage in the game, so large-grid ships last too long in the sim.
- (Confirmed) Idle gyro damping is about 4.7x too weak.
- (Confirmed) Grid splits use face contact where the game uses mount points.
- (Source/wiki only, unverified) Gatling is 600 rpm in the game and 700 in the sim. Burst/reload, bullet range randomisation of 80-100%, shell ricochet, armed warheads exploding on a hit or contact, explosion occlusion (the auditor measured -74% rocket damage), spread distribution and collision deformation also differ.
- The biggest methodology problem is that the default sim assumes the AI blocks fly pasted drones. Your 2026-10-13 report says they paste inert, so default win rates mostly measure a stand-in AI. The Pirate and warship models are omniscient [I] stand-ins. There are no confidence intervals, tuning is in-sample, and RESULTS.md is partial and predates v2.3-v2.6. Use the sim for same-assumption A/B only.

WOULD THE DRONES RUN. The combat scripts very likely run.
- All 36 blueprints equal minify(scripts), stub members exist in the real PB API, and every program is ASCII and under 100k chars.
- A ported Keen instruction counter puts every script at 2-6% of 50,000 (Gnat worst at 3,076). That rests on Keen's old block-counting source plus a wiki example. If the current game counts IL-like, Gnat (145%) and the Quad Hive constructor (120%) overflow. One in-game read of the 'Ins peak' line settles it.
- Basilisk has only 2.3k chars of headroom. About 9k chars of inert LAUNCH code can be removed.
- Survival worlds need pre-loaded magazines, because the drones have no ammo and no conveyors.
- GnatSwarmAI's RaidScan passes a world vector to a camera-local API, so it fails in the game while the sim hides it.
- No combat constructor is guarded. The Main catch never releases overrides and re-throws every tick, and a dead or split-off RC is handled badly.
- The printers are NO-GO as designed:
  - Blocks welded from a projection most likely join the projector's grid, so the merge release cannot separate the drone (plausible, supported by your report).
  - The [Stock] cargo is unconnected and 9x too small outside creative.

TOP RISKS.
1. Printer design.
2. Optimistic sim for rockets and large grids, plus the AI-block assumption.
3. A halted PB leaves its thrust and gyro overrides active, and nothing in-game restarts it.
4. Bare or guessed blueprint shapes: large turrets, the AI-block IsActivated flag, the LargeRailgun capacitor, and a hard-coded Owner id.

TIMERS AND EXTRA PBs.
- Timers (1 s minimum) cannot replace Update1 and cannot be a Stinger fuse.
- They fit as a dead-man (the PB re-arms a 5 s timer each second; if the PB dies, the timer cuts thrust and weapons) and as a backup bay release.
- An Event Controller or Timer can already call the PB with guard/assist/hunt/rtb/idle/home, because argument runs reach Command() before the Update1 gate. The costs are EC 10 PCU, Timer 25 PCU and PB 100 PCU, all non-DLC in the base variants.
- Recompile is a terminal button, not a toolbar action (about 75% confidence, from an old source snapshot plus the wiki). HiveMotherAI's ApplyAction("Recompile") is therefore almost certainly a no-op. A PB compiles itself on weld.
- A second PB is the right fix for scripts near the limit: a Helm PB at Update1 and a Staff PB at Update10 over IGC, with CurrentConstruct. Avoid TryRun between PBs.
- The sim does not model Timer, EC or toolbars, and sebp cannot yet write populated toolbars.

SKEPTIC OUTCOME. Nothing was refuted. Corrections to the auditors:
- LargeGatlingTurret IS in the samples (Venture - Expedition End).
- The index_stats '+/-1 unit' note is wrong.
- The EC event numbering is unverified.
- Setting UpdateFrequency first does not save a script from a constructor exception.

UNVERIFIABLE, so do not rework the sim on these alone: explosion occlusion, spread distribution, collision deformation, sensor relation/penetration/power, re-ownership on paste, GetTargetedEntity velocity, merge-block mount veto.

IN-GAME TESTS (packages/tests.md ids):
- T12 plus the 'Ins peak' read: counting model.
- T1: merge face.
- T3, P1, P2, P3: release, fused grid, neck and grinder.
- P4: components.
- P5/P6 (T5, T6): weld time, PB program, owner, battery charge.
- P7: HiveMother compile whitelist.
- T7: Midge AI.
- T8, T9: ammo and railgun.
- T10: gyro sign.
- T2/T11/T13/T14: Ctrl+B export and cache.
New tests to add:
- Print the PB action names and the AI property ids.
- Read IsAutoPilotEnabled and SpeedLimit from a script.
- Time a gatling box (14 s vs 12 s), and measure rocket burst and bullet impact distance.
- Hit an armed warhead with a gatling burst.
- Do a rocket-behind-armour damage test.
- Cast a camera ray with world vs local direction.
- Print GetTargetedEntity().Velocity.
- Measure IGC latency, size and queue limits.
- Paste a Hornet in a second world and read the PB owner and an enemy's Relationship.
- Run the timer dead-man test (does Start restart a counting timer).

FIX PLAN: 14 items with disjoint file sets. No refuted findings are included. The sim/src files listed (SimSettings, ScriptHost, Metrics, JsonOut, SelfTestD, DockLaunch, PrinterTest, RuntimeInfo) already carry uncommitted edits in the working tree (git status), so workers must build on those edits. The untracked sim/shim/IlCounter.cs, sim/src/Run/IlProfile.cs and tools/ilcount/ must be committed (item 6). Dependency order is noted per item.

Per-domain reports: the other files in this folder. The api_fidelity auditor failed (API safeguard error) and is not covered; see the follow-up note in the project log.
