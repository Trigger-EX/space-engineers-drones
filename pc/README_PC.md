# Space Engineers drone simulation pack (run on your PC)

No installs needed on Windows 10/11: SimHost.exe is a normal .NET Framework program and uses every core.

1. Unzip the whole pack somewhere (keep the folder structure: `out\sim`, `out\Blueprints`, `defs_stats.json`, `plans`).
2. Double-click **run.bat**. It runs `plans\default.plan` experiment by experiment (self-test, verify, a fresh 200-seed matrix,
   robustness variants, scripts-only fights, Hornet sweeps, traces). It prints progress, logs each experiment in `results\<name>\log.txt`,
   and is **resumable**: close the window any time, run it again and it continues.
   - Only some experiments: `run.bat -Only selfcheck,verify,full-fresh`
   - Fewer threads (keep the PC usable): `run.bat -Jobs 12`
3. Double-click **pack_results.bat** -> `SE_results.zip` (whatever has finished). Upload that file to the chat.
   If it is too large, send only `results\*\summary.csv`, `meta.json`, `status.txt`, `machine.txt` (see `SE_results_lite` below) and I will ask for specifics.

GPUs are not used: the simulator is a CPU program (thousands of independent fights, one per thread). They don't help without a rewrite.
**Linux Mint / Ubuntu / Debian:** unzip, open a terminal in the folder and run `./run_mint.sh` (it installs mono with apt if needed, runs a 5 s self-check,
runs the plan on all CPUs minus one, and writes `SE_results.zip`). Options: `./run_mint.sh --only verify,full-fresh`, `./run_mint.sh --jobs 12`.
`results\verify` is a small check: I compare it to the same fights run on my machine to see whether your CPU/.NET gives identical numbers.
If a line fails, send `results\machine.txt` and the failing `results\<name>\log.txt`.

## Your own ship vs the drones ("1vX", `plans/enemy.plan`)
`enemies\Tech Hunter\bp.sbc` is shipped. The plan pits it alone against 1, 2, 3, 4, 6, 8, 12, 16 and 24 of every armed drone model and the 4 mixed teams:
main run (ship flown like the pirate: closes in, circles at 700 m), a "holds position" run, 15-minute fights against the Longbow snipers,
an 800 m turret-range robustness run, and three traces. Roughly 2-5 hours on a 14-core PC; resumable like the default plan.
- Windows: open a command prompt in the pack folder and run `run.bat -Plan plans\enemy.plan`, then `pack_results.bat`.
- Linux Mint: `./run_mint.sh plans/enemy.plan` (packs `SE_results.zip` at the end).
- Another ship: copy its blueprint folder (with `bp.sbc`) from `%AppData%\SpaceEngineers\Blueprints\local` into `enemies\`. Every folder there is used.
- Limits: only the largest grid is simulated (rotor/hinge/piston subgrids, e.g. custom turret pods, are dropped); hydrogen and reactor fuel never run out;
  no PB scripts run on the enemy ship; its turrets aim by themselves.

## Endless mode (run until you press Enter)
- Linux Mint: `./run_endless.sh`   Windows: double-click `run_endless.bat`.
- Fights every matchup round after round (by default your ships in `enemies/` vs 1..24 of every drone) and shows a live line:
  fights done, fights per second, elapsed time and "every matchup >= N" (the number of complete rounds).
- Press **Enter** once: no new fights start, the ones in progress finish, everything is saved and `SE_results.zip` is written.
  Press Enter again to drop the fights in progress and stop at once. Ctrl+C does the same as Enter.
- Every finished fight is saved immediately to `results/endless/runs.jsonl`; running it again continues with new seeds.
- Other selections: `./run_endless.sh --kind 1v1`, `./run_endless.sh --filter "Hornet"`, fewer threads: `--jobs 12`.
- Linux: Mono's concurrent garbage collector crashed (SIGSEGV) with 19 threads, so the scripts use `MONO_GC_PARAMS=major=marksweep`;
  if the simulator still crashes, the scripts restart it automatically and no finished fight is lost (plans resume the same way).
- A crash prints its reason to the experiment's `log.txt` ("Got a SIGILL/SIGSEGV ..."). Random SIGILL/SIGSEGV crashes under full load
  usually mean an unstable CPU (Intel 13th/14th gen: update the BIOS microcode). Check that results are still right: run some
  finished fights again (same command, e.g. `--seeds 3`, `--out results/recheck`, no `--resume`) and
  `python3 compare_runs.py results/enemy-main/runs.jsonl results/recheck/runs.jsonl`. The sim is deterministic, so any
  "different" fight means the machine computed something wrong.
