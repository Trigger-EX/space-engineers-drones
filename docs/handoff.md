# Handoff: SimHost crashes on the user's PC = unstable hardware; overnight stability run

## Goal
Find why `enemy-main` (pc/plans/enemy.plan) crashed (rc=134) on the user's Linux Mint PC, and confirm the PC is stable
before trusting its simulation results.

## Decisions and constraints
- Diagnosis: hardware instability, not SimHost/Mono. Evidence: SIGILL in `ShipPilot.Want` JIT code, SIGSEGV in `Raycast.Trace`
  on a plain stack store, a fight (`Tech Hunter-vs-6xAegis` seed 0) with a different result on the same binary, whole-PC reset
  (BERT record), Firefox crash, rasdaemon MCEs (Instruction TLB L0 on cpu 8, internal parity on cpu 4). CPU i5-13600K,
  microcode 0x133 (already past Intel's 0x12B fix). The user had custom CPU settings (undervolt/OC?).
- After BIOS defaults (XMP re-enabled, Secure Boot turned back off for the NVIDIA driver): 2 x 336-fight rechecks at 19 jobs,
  336/336 identical, no crash.
- Reference for checks: `results/recheck/runs.jsonl` on the user's PC, made with the SimHost.exe of `SE_SimPack_9627626`.
  A newer SimPack (3d8f702, sim changes in 8d2fffb) needs a new reference.
- Old results from the unstable period must not be used (overnight.sh moves them to `old_results/`).

## Current state (all on main)
- `pc/ensure_mono.sh`, `pc/run_plan.sh`: MONO_CRASH_NOFILE=1; run_plan prints the crash reason on restart.
- `pc/compare_runs.py`: fight-by-fight determinism comparison of two runs.jsonl.
- `pc/overnight.sh`: 8 h unattended run (enemy.plan, then endless seeds >= 100 into results/enemy-main-more), check every ~30 min,
  MCE count, temps, resumable after a PC reset, summary.txt STABLE/UNSTABLE. Tested here at small scale (crash + reset injection).
- The user is about to start `./overnight.sh` (default 8 h, all 20 threads).

## Open questions
- Overnight verdict. If UNSTABLE at stock settings: XMP off retest, then Intel warranty (degraded P-cores).
- Older work from the previous handoff (LEAD v1.0): sim A/B of old vs new blueprints and the release zips may still be open.

## Next step
Read the user's `results/overnight/summary.txt` / SE_results.zip and give the verdict; if STABLE, analyse the enemy.plan results.

## Chat
Base name: Enemy main crash investigation
Run: 1
