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
a full-ammo-range turret robustness run (turrets as if a pilot target-locked: artillery 2 km), and three traces. Roughly 2-5 hours on a 14-core PC; resumable like the default plan.
- Windows: open a command prompt in the pack folder and run `run.bat -Plan plans\enemy.plan`, then `pack_results.bat`.
- Linux Mint: `./run_mint.sh plans/enemy.plan` (packs `SE_results.zip` at the end).
- Another ship: copy its blueprint folder (with `bp.sbc`) from `%AppData%\SpaceEngineers\Blueprints\local` into `enemies\`. Every folder there is used.
- Limits: only the largest grid is simulated (rotor/hinge/piston subgrids, e.g. custom turret pods, are dropped); hydrogen and reactor fuel never run out;
  no PB scripts run on the enemy ship; its turrets aim by themselves.

## Overnight run + stability test (Linux)
- `./overnight.sh` (default 8 hours, all logical CPUs; `--hours 6 --jobs 16` to change). Runs `plans/enemy.plan` from the start,
  then adds new seeds (100, 101, ...) of the same matchups to `results/enemy-main-more` until the time is up. About every
  30 minutes it reruns the 336 fights of `results/recheck` and compares them fight by fight, and it counts the kernel's
  hardware errors. `results/overnight/summary.txt` says STABLE or UNSTABLE; `results/overnight/status.log` has the timeline.
- Results from before the first overnight run are moved to `old_results/`. If the PC crashes, start it again: it continues.

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

## Fleet search: what beats your ship? (`run_search.sh` / `run_search.bat`)
- Linux Mint: `./run_search.sh`   Windows: double-click `run_search.bat`. Press **Enter** to stop and save (twice = stop at once).
- Finds the drone fleets (1-10 drones, any mix of models) that do best against the first ship in `enemies/` (Tech Hunter). There are about
  650,000 possible fleets, so it searches: every pure 10-drone fleet and 40 random mixes first, then mostly variations of the 8 best fleets
  (swap / add / remove drones, combine two fleets) plus some random ones, and gives the promising fleets more fights so luck washes out.
- Score per fight (0-100): drones win = 80-100 (more drones surviving = higher); otherwise up to 60 for the share of the ship's armed
  turrets (25), power blocks (25) and thrusters (10) destroyed. Fights last up to 10 minutes.
- Live line: fights, rate, fleets tried, drone wins; a "leader:" line appears whenever a new fleet takes first place.
- `results/search/leaderboard.csv`: every fleet tried, ranked (rewritten every 30 s and when you stop); the top 10 are printed at the end.
- Options: `--max-drones 6`, `--min-drones 4`, `--models "Hornet,Gnat,Longbow L,Basilisk"`, `--target "Other Ship"`, `--enemy-brain hold`,
  `--cap-seconds 300`, `--jobs 12`. Running it again continues the same search (delete `results/search` to start over).

## Charge test: standoff vs a Tech Hunter flying straight at the drone (`plans/charge_standoff.plan`)

Every main design (Hornet, Harrier, Lancer, Wasp, Aegis, Basilisk, Longbow S/L, Mantis, Ballista) alone against the Tech Hunter rushing at it at 100 m/s from 3,000 m, once per standoff from 200 m up to the longest range its weapon is usable, plus the shipped default (101 lines x 20 seeds; regenerate with `python3 -I tools/gen_charge_plan.py --seeds N`).
- Windows: `run.bat -Plan plans\charge_standoff.plan` (a few lines only: `-Only charge-Hornet-300,charge-Hornet-default`). Linux: `./run_plan.sh plans/charge_standoff.plan`.
- Then `python3 -I tools/charge_standoff_report.py results` prints one table per design (survival, hit rate, damage taken, closest approach, damage dealt) and marks the best standoff. Send that output back.
- Needs `enemies\Tech Hunter` (shipped) and the blueprints of all ten designs in `out\Blueprints`.

## Open tests, run until you stop it (`run_pending.sh`, `plans/pending.plan`)

Linux: `./run_pending.sh` (options: `--jobs 12`, `--only charge-,retro-pirate-base`). It runs every line of `plans/pending.plan` in rounds: 5 seeds per line (the charge lines 20), then 10, 20, 40, ... Each round only adds new seeds, so whenever you stop, every test has about the same amount of data.
- Covers: the charge test (all of `plans/charge_standoff.plan`), an on/off check of the 2026-10-14 loose fire window against the Pirate and the Tech Hunter, 1/2/4 Ballistas locking on and fighting the Tech Hunter, and a fresh 1v1 matrix as filler.
- Stop: **Ctrl+C once**. Finished fights are kept, summaries are rewritten and `SE_results.zip` is packed; send that. Run it again later and it continues at the same round.
- Progress: `results/pending_progress.txt` (one line per test per round).
