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
