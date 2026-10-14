# Plan: THRUST VECTORING (fly on with a lost thrust axis)

Goal: when a drone loses a thrust axis (e.g. every thruster that pushes it forward is shot off) it notices and turns the ship so a
surviving thruster group points along the acceleration it needs, as the Gnat does on purpose (`scripts/GnatSwarmAI.cs` VectorFly/Axial,
~1765-1810), while keeping the guns as close to the target as the need allows. Intact drones must fly exactly as today.

Rules that bind this work (CLAUDE.md): commit to `main` only; no release zips; small sim runs (seed counts stated below);
hot path (tracking, lead, aim, control, firing) stays in ONE PB; budget about 80k minified per PB, so a PB that would pass it gets a
module moved to another PB rather than the feature cut.

## 1. Findings (current code)

* 11 scripts share the same thrust code: `_thrDir[6]` (Forward, Backward, Left, Right, Up, Down of `_rc`; a thruster joins the group
  whose axis its push (`WorldMatrix.Backward`) is within dot 0.9 of, else it is never used), `MinAccel()` and `ApplyThrust()`.
  Hashes: Hornet, Wasp, WaspFlight, Lancer, Harrier, Aegis, Basilisk identical; Longbow + Mantis differ in layout only (floor 0.3);
  Shrike has `Comp()`/`AccelDir()`; Peeper has no `_powerScale`.
* `ApplyThrust` projects the world force on the 6 axes and clamps each group to its working thrust. For a fixed attitude that per-axis
  clamp is already the least-squares optimum (the groups are orthogonal), so the allocation is not the problem: attitude is. A dead
  group's component is just dropped and `Aim()` keeps the nose on the target, so a drone with no forward thrust never closes in.
* Detection already half works: dead / damaged / unpowered blocks give `IsWorking == false`, so they drop out of the sums every tick.
  Gaps: (a) nothing records that capacity was LOST (a 600-tick rescan rebuilds `_thrDir` without the destroyed blocks, so a "rated"
  value computed at the rescan forgets the loss); (b) a thruster on a piece that broke off is still `IsWorking` and still counted
  (only `_rc` is checked with `IsSameConstructAs`); (c) `MinAccel` skips empty groups, so braking distances assume the ship can still
  brake along the dead axis.
* `Control()` calls `Aim()` before it computes the force, so attitude must use the previous tick's force (1 tick old at 60 Hz: fine).
* In the game thrust acts at the centre of mass (no torque); the sim matches that by default (`SimSettings.ThrusterTorque` off). No torque
  term is needed.
* Minified sizes now (tools/minify_cs.py): WaspFlightAI 76,465 (budget 80,000, check in tools/minify_check.sh), WaspStaffAI 32,844,
  Hornet 95,504, Lancer 97,037, Harrier 96,620, Basilisk 94,063, Longbow 99,081, Wasp 99,358, Aegis 99,843, Mantis 99,882,
  Shrike 81,389 (limit 81,600), Peeper 11,404, Gnat 87,047 (already point-and-burn: excluded).
* Sim: `ShipSpec.Commands` are PB argument runs only; no way to destroy blocks on a schedule. `Body.RemoveBlock(b, attacker)`
  (sim/src/Core/Body.cs:304) is the destroy path (removes from `Thrusters`, marks connectivity dirty; `Closed` = `Destroyed`).
  `mono` is not installed in this container (pc/ensure_mono.sh shows the apt packages).

## 2. Design (one approach)

New shared block `tools/thrust_block.cs`, markers `// >>> THRUST VECTORING v1.0 ...` / `// <<< THRUST VECTORING <<<`,
own Custom Data section `[Thrust Vectoring]`. It owns `ApplyThrust` and the capacity bookkeeping; scripts keep `Aim()` and get one-line hooks.

### 2.1 Detection
* `TvCaps()` once per tick in `Control()`, right after the `LaunchStep` early return: for each group `_tvCap[i]` = sum of `MaxEffectiveThrust * _powerScale` over
  thrusters with `IsWorking && !Closed` (this replaces the two loops `MinAccel` and `ApplyThrust` run today), and
  `_tvLive[i]` = sum of `MaxThrust` over the same set.
* `_tvRated[i]` = max ever seen of the group's `MaxThrust` sum (set in `TvSetup()` at the end of `GetBlocks()`, never lowered by a
  rescan). `TvReset()` clears it on a grid change (`CheckGrid`), on "reload" and after a printer join/launch, so a merged printer's
  thrusters never count. Using `MaxThrust` for the lost test keeps atmosphere (ion effectiveness) from looking like damage; the
  allocation itself uses `MaxEffectiveThrust`.
* Group LOST when `_tvLive[i] < Weak * _tvRated[i]` (Weak = 0.5; a Hornet group of 2 is lost at 1 thruster down: still usable, but the
  vectoring is allowed to consider it). `_tvLost` = 6-bit mask.
* 1 Hz (`_tick % 60 == 7`): any group thruster `Closed` or `!IsSameConstructAs(Me)` (check `Closed` first) -> `GetBlocks()` next tick.
  That drops severed thrusters and keeps the lists clean.

### 2.2 Allocation (unchanged maths, now block-owned)
`ApplyThrust(F)`: store `_tvNeed = F`; per-axis clamp with `_tvCap` exactly as today (same 0.002 minimum). Numerically identical for an
intact ship, so intact sim runs stay bit-identical (the regression test, section 5).

### 2.3 Attitude: `TvAim(ref desiredFwd, ref desiredUp)`, first line of `Aim()`
Return at once (no change) when `!TvOn`, `_tvLost == 0`, or `|_tvNeed| / mass < 0.3 m/s2`. Otherwise score 7 options and pick the lowest:
* `none` (plain aim): shortfall of the per-axis clamp evaluated in the attitude the plain aim would reach (rotate the need by the minimal
  rotation gun-forward -> desiredFwd, then clamp). Aim cost 0.
* `k` = each group 0..5 with `_tvCap[k] > 0`: body push direction `e_k`, fixed angle `th_k = acos(gunFwd . e_k)`. The gun forward must lie
  on the cone of half-angle `th_k` round the need direction `nd`; the closest point to `desiredFwd` (`a`) is in the `nd, a` plane:
  `p = norm(a - (a.nd) nd)` (if `a` is parallel to `nd`, use the current gun forward's part across `nd`), `fw = nd cos th_k + p sin th_k`.
  Aim error `|acos(nd.a) - th_k|`, shortfall `max(0, |F| - cap_k) / |F|`.
* Score `J = shortfall + W * aimErr / pi + 0.1 * turnFromNow / pi`. `W = Aim Weight` (1.0) while the script says it is fighting
  (`_tvFight`, set by the script: target held and inside its fire range), `W * 0.2` otherwise, and 0 when natural gravity > 1 m/s2 and the
  shortfall of `none` has an upward part (do not fall to keep the guns on).
* Hysteresis: the current option gets `-Hysteresis` (0.1) and holds at least `Hold Time` (1 s), unless its shortfall is > 0.9.
* Output for `k`: `desiredFwd = fw`; `desiredUp` from a triad so `e_k` lands on `nd`: body basis `(gunFwd, e_k across gunFwd)`, world basis
  `(fw, nd across fw)`, map the gun's current up through it. If `e_k` is parallel to gun forward (th 0 or 180) keep the script's up.
* The guns keep firing on their own: the script's fire gate compares the real gun direction with the lead, so shots happen whenever the
  vectored attitude passes the target. `ApplyThrust` still delivers every component the new attitude allows (never one against the need).

Behaviour this gives: forward group lost, target far -> W small, the drone turns a side or rear group toward the target and burns, then
turns back to fight; at standoff the need is mostly orbit / velocity matching, which side groups give with the nose on the target.
Up group lost in gravity -> hangs tilted on the strongest group, guns secondary.

### 2.4 Braking estimate
`TvMinAccel(mass, grav, floor)`: as today, but a LOST group counts as `0.5 * strongest cap` (turn then brake) instead of being skipped.
Script `MinAccel` bodies become `return TvMinAccel(mass, grav, 0.5);` (0.3 for Longbow/Mantis). Identical when nothing is lost.

### 2.5 Config `[Thrust Vectoring]` (defaults written on first run, "reload" re-reads)
`Enabled=true`, `Lost Fraction=0.5`, `Aim Weight=1.0`, `Hold Time (s)=1.0`, `Hysteresis=0.1`.

### 2.6 Status
`TvStatus()`: empty when nothing is lost, else `Thrust : lost F,R  vectoring R` (groups F,B,L,R,U,D by push direction; `vectoring -`
when `none` is chosen). Single-PB scripts append it in `Display()`. Wasp Split: Flight sends it in the free bits of the `.F2S` flags
(bits 16-21 lost mask, bits 22-24 chosen option + 1, 0 = none); WaspStaffAI decodes it into the same line (update docs/design/wasp_split.md table).

### 2.7 Hooks per script (one line each)
`TvLoad()` next to `KzLoad()` (both call sites); `TvCaps()` right after the `LaunchStep` line of `Control()`; `TvAim(ref desiredFwd, ref desiredUp);` first line of
`Aim()`; `TvSetup()` last line of the thruster grouping in `GetBlocks()`; `TvReset();` in `CheckGrid()` before `GetBlocks()` and in "reload";
`_tvFight = haveAim && dist < MaxFireRange;` (Longbow/Mantis: their own range) in the aiming section; delete the script's `ApplyThrust`;
`MinAccel` body -> `TvMinAccel`; status line. Uses from the script: `_rc, _aimRef, _thrDir, _powerScale, _tick, _now, Me`.

## 3. Rollout (staged)

* Stage 1: WaspFlightAI (+ WaspStaffAI status). The only combat PB with a Flight/Staff cut today. Block target <= 3.5k minified; if
  Flight then passes 80,000, move an off-hot-path module to Staff (candidates: printer join, `_CMD` text parsing, settings echo) in the
  same change, over IGC as in docs/design/wasp_split.md.
* Stage 2: Hornet. At 95.5k it is already past the 80k budget, so do "Hornet Split" first (copy of the Wasp Split pattern: FLEET LINK,
  FOLLOW, LAUNCH, full display to a `[Staff]` PB; tracking/lead/aim/control/firing + this block in `[Flight]`), then add the block. Do not
  push the single-PB Hornet to ~99k.
* Stage 3: Lancer, Harrier, Basilisk, Aegis, Longbow S/L, Mantis (each via its own split), Shrike (its Staff PB exists: move a module
  to free room), Peeper (11k, has room; needs a `_powerScale = 1` and its own `Aim(fwd)` hook). WaspAI single-PB stays as is.
* `tools/thrust_sync.py` lists only the scripts already hooked (its insert-before-`public Program()` would otherwise clash with the
  script's own `ApplyThrust`), so SCRIPTS grows per stage.

## 4. Implementation steps (ordered, each small)

1. `sim/src/Run/Scenario.cs`: a command whose argument starts with `!` is a harness event, not a PB run. Add `!kill-thrust <F|B|L|R|U|D> [N]`:
   remove N (default all) thrusters of the ship's main body whose push is within dot 0.9 of that controller axis, via
   `body.RemoveBlock(b, -1)`. Small helper class in `sim/src/Run/SimEvents.cs`.
2. `sim/src/Run/Program.cs`: `--events "20:!kill-thrust F;20:!kill-thrust R 1"` appends (does not replace, unlike `--commands`) to every
   scripted drone of team `--event-team` (default 0); add to `Known` and to meta provenance. `FollowTest.cs`: same `--events` option.
   Selftest in `SelfTestD.cs`: event at 1 s removes exactly the right thrusters and `IsWorking`/`Closed` read false on old references.
3. Baseline (before any script change), recorded in the plan's results section: follow-test park and a 1v1, with and without the kill
   (section 5) on Wasp Split and Hornet, to show the current failure.
4. `tools/thrust_block.cs`: fields, `TvLoad/TvReset/TvSetup/TvCaps/ApplyThrust/TvMinAccel/TvAim/TvStatus`, header comment listing the hooks.
5. `tools/thrust_sync.py` (copy of tools/kz_sync.py, SCRIPTS = ["WaspFlightAI"]), then `--apply`.
6. WaspFlightAI hooks (2.7) and F2S bits; WaspStaffAI status decode; docs/design/wasp_split.md F2S table.
7. Size and compile: `tools/compile_check.sh`, `tools/minify_check.sh` (Flight <= 80,000). Over -> move a module to Staff (stage 1 note).
8. Blueprint for the sim: `python3 -I tools/build_all.py --no-zip --only "Wasp Split"`. Runs of section 5.
9. Docs: `docs/thrust-vectoring.md` (behaviour, config, hooks, results); line in HANDOFF.md. Say zips are not rebuilt. Commit to `main`.
10. Stage 2 and 3 as separate tasks (Hornet Split first).

## 5. Verification (seed counts and why)

* Regression, intact ship, 5 seeds: `run --filter "1v1/Wasp Split-vs-Hornet" --seeds 5` before and after: per-seed results must be
  identical (allocation numerically unchanged, `TvAim` returns early). Any difference = bug. 5 is enough because the check is exact.
* Holding position, deterministic, 3 seeds each: `follow-test --model "Wasp Split" --count 1 --path park --cmd satellite --events "30:!kill-thrust F"`,
  then `B`, `R`, and `F` + `U` together. Pass: back in the slot (within 1.6x slot distance) by 60 s, no hull contact, status line shows the lost
  groups. Same with `[Thrust Vectoring] Enabled=false` (via `--custom-data`) to show the old failure. Also `--path cruise` with `F` killed at
  30 s: keeps up with the owner.
* Combat direction, 20 seeds per arm (rough A/B): `run --filter "1v1/Wasp Split-vs-Hornet" --seeds 20 --events "15:!kill-thrust F"` with
  vectoring on vs off; read wins / survival time / shots. Only this one matchup; stop if the direction is clear at 20.
* Unit-level: the sim selftest of step 2; `thrust_sync.py --check`; a trace (`--trace`) of one kill run to check the chosen option does not flip
  more than once per Hold Time and that `ThrustOverridePercentage` on the dead axis is never needed.

## 6. Risks

* `Aim()` only applies roll when the forward error is < 0.6 rad, with gain 0.5: side-group vectoring may settle slowly. If the trace shows it,
  let `TvAim` raise a flag that lifts the roll gate in that one PB (shared Aim change, all scripts later).
* Fire time lost while turning to burn: the Aim Weight trade-off is a guess; the 20-seed A/B tells the direction, tune only `Aim Weight`.
* Gravity tests: the follow-test has no planet; gravity behaviour is reasoned, not simulated, until a gravity scenario exists.
* Thrusters mounted off-axis (dot < 0.9) stay unused, as today; widening the grouping is out of scope.
* Two writers: `TvAim` only edits Aim's inputs and `ApplyThrust` stays the single thruster writer; LAUNCH's take-off burn (`LaunchStep`,
  which returns before `TvCaps`) is untouched.
* Split work (stage 2/3) is the large part of the rollout; this plan only makes the block ready for it.
* This session's checkout is on branch `code/keen-mccarthy-06pl20`; CLAUDE.md requires committing to `main`.
