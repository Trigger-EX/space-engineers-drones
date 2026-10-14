# Retrofit list: general improvements to carry to every blueprint

**Rule (CLAUDE.md "Retrofit list"):** when a drone or ship gets an improvement that is not tied to its role (detection, fire control, flight safety, wing behaviour, diagnostics, robustness), add a row to **Pending** in the same commit: what changed, where it came from (script, commit), which other models could use it. When the owner asks for the end-of-day retrofit, apply every Pending row to every blueprint it fits, test, and move it to **Applied**. A change that only makes sense for one model's role goes under **Role-specific** with one line of why, so nobody re-checks it.

Shared script blocks (`tools/blocks.json`, `tools/*_sync.py`) are the cheapest way to retrofit: put the change in the block and run `python3 -I tools/blocks_sync.py --apply`.

## Pending

| Improvement (origin) | Could go to | Note |
|---|---|---|
| The first forward camera always casts the long AI-confirm ray, even when it is the only forward camera (before: `longCam = nFwd > 1`, so a drone with one nose camera cast only 1,300 m rays and never confirmed a target the AI held at 1.3 to 2.6 km). TubeRamAI, tube detection commit | Gnat (same `nFwd > 1` rule) and any other single-nose-camera AI-search script | Sim, Tube vs Basilisk/Hornet at 1.5 to 2.2 km, 5 seeds: first target 1 to 5 s instead of 21 to 80 s (together with the 5 s search interval and 20 s AI windows) |
| Warheads armed at the last moment (gap to the hull under 20 m or 0.3 s at the closing speed), because gunfire sets armed warheads off: a Tube 4x4 W armed at 200 m was blown up 148 m short by the target's rockets (sim). TubeRamAI, warhead variants commit | StingerAI (`Arm Warheads Within=200`), Ram / Ram Tandem (`tools/designs/ram.py`, StingerAI placeholder) | The Stinger's camera hull-distance fuse already fires close in; only the arming point moves |
| Target memory: the current target is kept `Target Memory (s)=20` after its last fix by dead reckoning, other contacts still expire after 5 s; side cameras refresh the lock (HUNT) and sweep (SEARCH) once no forward camera works. TubeRamAI v1.1, rear-systems commit | StingerAI (same 5 s drop), Gnat and the AI-search drones (any script that drops its target 5 s after the last ray) | Detection robustness; the Tube needs it after its nose (camera) is gone |
| Miss detection for a terminal run: enter the terminal phase only while closing faster than max(20, 0.5 x own speed); closing below -2 m/s for 0.5 s = miss (disarm, extend, come round), no fixed overshoot timer; extend keeps its outbound speed. TubeRamAI v1.1 (owner's Corsair report) | StingerAI (kamikaze terminal run), Ram / Ram Tandem | Fire control of a ram or kamikaze run |

## Needs the owner's call

(none)

## Applied 2026-10-14 (printer guard)
- **PRINTER GUARD** shared block (`tools/printer_guard_block.cs`, blocks.json): a flight PB flies nothing while its grid holds a `[Bay` block or the Hive Mother PB (owner: a printed Watcher flew the Watcher Pad) and re-reads its blocks once it has split off. Applied to AegisAI, GnatSwarmAI, HarrierAI, HornetDroneAI, LancerAI, LongbowSniperAI, MantisAI, StingerAI, WaspFlightAI (TubeRamAI has its own). Not needed: BasiliskAI, ShrikeAI (never printed).

## Applied 2026-10-14 (SENTRY mode, owner request "sentry mode for all drones")

| Improvement | Applied to | Not applied to (why) |
|---|---|---|
| `sentry` command / `Start Mode` SENTRY + `Sentry Range (m)=2500`: still on the spot until a target is within range, then hunt it with no leash, then back to the spot (Storage keeps it) | Hornet, Watcher, Lancer, Harrier, Aegis, Mantis, Basilisk, Wasp (Flight), Longbow S/L, Gnat, Shrike + every warship Helm, Tube rams (all), Stinger (wait and launch only); FLEET block default `Allowed Commands` | Midge (scriptless), Peeper / Spotter (unarmed scouts), `WaspAI.cs` (legacy single-PB, no blueprint, would pass 100k) |

## Applied 2026-10-14 (from the Ballista work of the day)

Sim check: 27 drone-vs-Pirate scenarios x 10 seeds, old vs new scripts: 92 vs 95 wins of 270, no script exceptions; Helm selftests hsplit, M2, M3, M6 pass. Nothing verified in the game. Release zips not rebuilt.

| Improvement (origin) | Applied to | Not applied to (why) |
|---|---|---|
| AI Offensive search interval 5 s (`UpdateTargetInterval`; the block's default 30 s is longer than the AI window). Ballista Helm, `02de6ef` | AI GATE block v1.2: Hornet, Watcher, Gnat, Longbow S/L, Wasp, Lancer, Harrier, Aegis, Mantis, Basilisk, Tube 2x2-4x4, every ShrikeAI ship. Key `[AI Gate] AI Search Interval (s)` (the Helm's `[Shrike]` key moved there) | - |
| AI blocks left alone ("AI Behavior" not switched on) while `AI Block Search Assist` is off; `ai on/off` re-runs the search. Ballista Helm, post-v2.7 3a | Hornet, Watcher, Gnat, Longbow S/L, Wasp, Lancer, Harrier, Aegis, Mantis, Basilisk | Tube (no assist switch: its SEARCH mode is the AI) |
| Loose fire window beyond standoff + 100 m (whole silhouette instead of the 3 m hull point). Ballista Helm, `9d2743b` | Hornet, Watcher, Lancer, Harrier, Aegis, Basilisk, Wasp, Mantis (gatling). Key `Loose Fire Beyond Standoff` | Longbow (settled single shots by design), Gnat (yo-yo runs, no standoff) |
| Idle home point drifts away from close mates (the wing spreads instead of being pulled back to the paste spot). Ballista Helm, `785a7c6` | Hornet, Watcher, Lancer, Harrier, Aegis, Basilisk, Wasp, Longbow S/L, Mantis | Gnat (its FORM sphere is its own spacing) |
| `Wingmate Fire Clearance (m)` as a key (was a constant 14 m). Ballista Helm, `785a7c6` | Hornet, Watcher, Lancer, Harrier, Aegis (default 14, unchanged) | Basilisk, Wasp (own `Friendly Flight Line Clearance`), Mantis, Longbow (clearance computed from grid size and slug time) |
| `1st contact <m> v <speed> <source> <s> ago` status line. Ballista Helm, `8345431` | Flight PB screen of Hornet, Watcher, Lancer, Harrier, Aegis, Basilisk, Wasp, Longbow S/L, Mantis; Gnat screen | - |
| `Kamikaze Rail Hold Range (m)=250`. Ballista, `1395231` | Warden, Leviathan, Corsair Helm Custom Data (they have rails or shell guns and point-defence turrets; Corsair 5-seed wing test: same as before) | Shrike (fighter, no rails) |
| Full FRIENDLY AVOIDANCE steering (older improvement the Mantis never got: no room before its split, and the split left its beacon block unhooked, so it neither dodged nor beaconed) | Mantis Flight (81.7 k minified) | - |

## Applied 2026-10-14 (second round, owner's requests)

| Improvement | Applied to | Notes |
|---|---|---|
| Harasser defence (Ballista Lookout) | Warden, Leviathan, Corsair: the LOOKOUT block now also lives in StaffAI.cs, switched on in their Staff PB Custom Data | sim: Warden -10 to -13 % damage, Corsair hit rate 91 -> 74 % (damage unchanged), Leviathan no measurable gain; docs/design/ballista_harasser_defence.md section 12 |
| Railgun power (Ballista: power for a full rail recharge, thrust to pay for it) | Longbow S, Longbow L (+ Seeker), Mantis: reactors >= rail draw; reactors + batteries >= rail + larger thruster group per axis; acceleration and turn rates >= before | docs/design/railgun_power.md; Warden, Corsair, Leviathan already covered by their reactors |

## Role-specific (checked, not retrofitted)
- Tube drones (2026-10-14): critical systems at the rear, side thrusters right behind the nose; W tubes come back for a kinetic second ram after the blast. Ram drones only: their nose is the weapon and takes the impact.
- TubeRamAI v1.2 (printed tubes only): hold while the grid holds a `[Bay` block or the Hive Mother PB, `TUBE_SWARM` heartbeat. Every other printable drone already has this through the shared launch block (`tools/launch_block.cs`).
- HiveMotherAI v2.4-v2.6 (printers only, v2.6: release 2 s after the last block, welders stay on, 1 s cooldown; printers.py: single-type cap = Max Drones): FINISH scan counts armour seen only by `CubeExists`, 5 s armour settle, no drone-PB recompile (threw NRE in the game), release on finish. Printer-controller logic, no drone uses it. (`CradleBayAI`'s lane check has the same armour blind spot: separate fix.)

* **Tube ram drones** (large ones too: heavy armour panels round the body, `Nose Camera Setback (m)` for the contact fuse): `Start Mode (WAIT/HUNT)=HUNT` (owner request: a launched tube is a munition and hunts by itself; a gun drone's start mode stays the owner's per-model choice); `[AI Gate]` 20 s / 5 s windows (the AI Offensive block is its only detector, as on the Ballista); recessed side thrusters driven by the terminal guidance (ram geometry); warhead layer with impact / contact fuse (W variants).

* **Ballista**: `Standoff Range=1600` / `Standoff By Weapons=false` (rail sniper vs a 100 m/s charge; other designs wait for the charge-test report, `pc/plans/charge_standoff.plan`); `Salvo=ON` (27-rail alpha strike); `Auto Camera Scan=false` and `[AI Gate]` 20 s / 5 s windows (the AI Offensive block is its detector; Warden and Leviathan run without the AI assist, so their camera scan is their only non-turret detector); point-defence rail battery (off); harasser defence (above); three reactors for the 27-rail recharge; four PD turrets, two of them rotor + hinge AI turrets (Warden, Leviathan, Corsair and Shrike already have rotor + hinge turrets).
* **Wing spacing on the slow warships** (owner, 2026-10-14: "wing spacing can stay low for slow ships"): Warden, Leviathan and Corsair keep the defaults (70 m / 14 m). A scaled try made the Corsair worse in the sim (3 Corsairs vs a parked Hornet, 5 seeds: Hornet untouched in 3 of 5 runs against 1 of 5).
* **Helm split** (Staff PB with the fleet channel and damage sensing): Warden, Leviathan and Corsair already had a Staff PB; Shrike and Ballista got theirs.

## Audited 2026-10-14, already everywhere

Thrust-vector `[Thrust]` PB (every flight script with the TV LINK block; the Gnat thrusts only along its nose axis by design, the Tube ram drones have no reverse thrust and no TV link); Flight/Staff splits; spare PBs; Satellite 400 m and READY mode; asteroid avoidance (inside the FRIENDLY AVOIDANCE block); rocket aim from the launcher that fires next (Wasp Flight and Basilisk are the only scripts with fixed rocket launchers; the Helm ships carry rocket turrets); bays finding a split drone's PB.
