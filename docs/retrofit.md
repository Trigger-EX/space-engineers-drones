# Retrofit list: general improvements to carry to every blueprint

**Rule (CLAUDE.md "Retrofit list"):** when a drone or ship gets an improvement that is not tied to its role (detection, fire control, flight safety, wing behaviour, diagnostics, robustness), add a row to **Pending** in the same commit: what changed, where it came from (script, commit), which other models could use it. When the owner asks for the end-of-day retrofit, apply every Pending row to every blueprint it fits, test, and move it to **Applied**. A change that only makes sense for one model's role goes under **Role-specific** with one line of why, so nobody re-checks it.

Shared script blocks (`tools/blocks.json`, `tools/*_sync.py`) are the cheapest way to retrofit: put the change in the block and run `python3 -I tools/blocks_sync.py --apply`.

## Pending

(none)

## Needs the owner's call

| Item | Why it is not applied |
|---|---|
| Wing spacing for the large Helm ships (`Wingmate Separation Range / Speed / Fire Clearance`, the Ballista has 300 m / 12 m/s / 45 m) | Scaled values (Corsair 150 / 15 / 25, Warden 400 / 10 / 45, Leviathan 700 / 8 / 80) made the Corsair worse in the sim: 3 Corsairs in a line vs a parked Hornet, 5 seeds, the Hornet was left untouched in 3 of 5 runs against 1 of 5 with the defaults. Warden showed no difference. Not applied; a game report of Wardens or Corsairs crowding each other would justify a tuned try. |
| Longbow S / L and Mantis railgun recharge under full thrust | The Ballista got power for a full rail recharge. Every other rail carrier already covers its rails' recharge draw (Longbow S 4.0 MW for 3.6, Longbow L 48 for 38, Mantis 4.0 for 3.6, Warden, Corsair and Leviathan 1.2 to 2x), but the ion thrusters are served first, so at full thrust the Longbow L recharge stops (48 MW against 38 + 77) and the small ones slow down. Fix would be more batteries (mass, layout); not done without a game report. |
| Harasser defence (Lookout module + `HARASSER LINK`) on Warden / Leviathan / Corsair | The Helm side is already in every ShrikeAI ship (inactive without a Lookout). The Lookout lives in the Ballista's Spotter PB; those ships have many turrets that kill small craft, which the railgun-only Ballista lacked. Would need a Lookout PB each. |

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

## Role-specific (checked, not retrofitted)

* **Ballista**: `Standoff Range=1600` / `Standoff By Weapons=false` (rail sniper vs a 100 m/s charge; other designs wait for the charge-test report, `pc/plans/charge_standoff.plan`); `Salvo=ON` (27-rail alpha strike); `Auto Camera Scan=false` and `[AI Gate]` 20 s / 5 s windows (the AI Offensive block is its detector; Warden and Leviathan run without the AI assist, so their camera scan is their only non-turret detector); point-defence rail battery (off); harasser defence (above); three reactors for the 27-rail recharge; four PD turrets, two of them rotor + hinge AI turrets (Warden, Leviathan, Corsair and Shrike already have rotor + hinge turrets).
* **Helm split** (Staff PB with the fleet channel and damage sensing): Warden, Leviathan and Corsair already had a Staff PB; Shrike and Ballista got theirs.

## Audited 2026-10-14, already everywhere

Thrust-vector `[Thrust]` PB (every flight script with the TV LINK block; the Gnat thrusts only along its nose axis by design, the Tube ram drones have no reverse thrust and no TV link); Flight/Staff splits; spare PBs; Satellite 400 m and READY mode; asteroid avoidance (inside the FRIENDLY AVOIDANCE block); rocket aim from the launcher that fires next (Wasp Flight and Basilisk are the only scripts with fixed rocket launchers; the Helm ships carry rocket turrets); bays finding a split drone's PB.
