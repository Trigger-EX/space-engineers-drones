# Open-tests run, 2026-10-14 (`pc/run_pending.sh`, owner's PC, i5-13600K, about 7 h)

SimPack `361de7e`. Rounds reached: charge lines 2,560 seeds each (N=640), the loose-fire A/B 640 seeds per scenario vs the Pirate and 320 vs the Tech Hunter,
Ballista wing 320, 1v1 matrix 320 (29,120 fights). No script exceptions anywhere. `charge-Hornet-default` is missing from the upload (the 700 m row is the useful one anyway).

## 1. Charge test (Tech Hunter rushing at 100 m/s from 3 km, one drone, 120 s)

`python3 -I tools/charge_standoff_report.py <results>`. surv% = drone still capable at the end; dmgout = damage it dealt.

| Design | Shipped | Result | Best |
|---|---|---|---|
| Hornet | 350 | never survives; 700 m: lost 62 % of blocks (83 % at 200), dmgout 9.1k (7.3k) | 700 |
| Harrier | 250 | never survives; 700 m: lost 64 % (80 % default), dmgout 8.7k (7.8k) | 700 (the report says 600: its sort is not monotone here, 700 is better on every column) |
| Lancer | 420 | never survives; 700 m: lost 65 % (78 %), dmgout 14.7k (12.0k); 750 deals more (16.6k) | 700-750 |
| Aegis | huge-grid key | never survives; 780 m: lost 66 % (73 %), dmgout 54k (37k) | 780 |
| Basilisk | huge-grid key | never survives, standoff barely matters (dmgout 16-17.5k) | 800 (marginal) |
| Wasp | 350 in the sim | every row identical: the sim's drones have unlimited rockets, so `Standoff Range (unlimited rockets)` (350) applies and the swept key is ignored | not measured |
| Longbow S | auto (about 1,400) | 100 % survive from 1,000 m up, 0 % at 900 m and below; default is already right | default |
| Longbow L | auto (about 1,440) | same: 100 % from 1,000 m, 0 % below 1,000 m; default right | default |
| **Mantis** | Engage Cap 650 | **default 0 % survive (lost 87 %, dmgout 13k); Forced Range 1,300-1,400: 94-95 % survive, dmgout 33k** | 1,300-1,400 vs huge grids |
| Ballista | 1,600 | 95 % survive at every standoff 800-1,700 (the rails stop the charge first); standoff only moves the closest approach | keep 1,600 |

Reading: against a lone fast charge the gun drones die whatever they do; a longer standoff only buys about 10 more seconds and 10-50 % more damage.
The railgun designs live or die on staying past about 1,000 m, and only the Mantis ships closer than that.

## 2. Loose fire window (retrofit of 2026-10-14) on vs off

- Pirate, 36 scenarios x 640 seeds per arm: drone wins 36.30 % on vs 36.25 % off; per model within 1.3 points either way.
- Tech Hunter, 40 fleets x 320 seeds: damage dealt to it within +/-2 % per fleet, both directions; same win/timeout split.
- Verdict: no measurable effect in the sim, good or bad. Keep it (it was meant for real-game hull-point jitter, which the sim does not model).

## 3. Ballista wing (Tech Hunter holding at 2.2 km, 120 s; and flown like the pirate, 300 s)

- Lock-on: in 2- and 4-ship wings every Ballista fired in every fight (0 of 1,920 never fired). A single Ballista at 2.2 km never fired in 13 of 320 fights (4 %).
- Hit rate 95-98 % (hold), 80-91 % (pirate; falls with wing size).
- **Friendly fire**: rare but heavy. 5 of 1,280 Ballista-fights in the 4-wing put a salvo into a mate (2 of 640 vs the pirate-flown Tech Hunter; never in smaller wings); against the holding Tech Hunter all the damage the Ballistas took came from that (715 per ship on average). Fixed, see 5.
- Damage: 4 Ballistas strip 10 % of the Tech Hunter's blocks in 120 s and 18 % in 300 s; it was disabled once in 320 fights. One Ballista is never hurt.

## 4. 1v1 matrix (29,120 fights, current scripts)

No exceptions. Win % against other models: Aegis 64, Hornet 56, Harrier 54, Longbow S 54, Lancer 53, Mantis 51, Basilisk 49, Longbow L 45, Midge 24, Wasp 19, Gnat 15.
24 % timeouts; the most common stuck flags are `noengage` (Basilisk, Wasp, Gnat, Lancer, Harrier, Aegis, about 1,000-1,500 fights each).

## 5. Follow-up, applied the same day (owner: "good changes; go ahead")

A/B in this repo's sim, pack mode (see the note below), Tech Hunter flown like the pirate (circles its target at 700 m), 1 and 3 drones, 30 seeds per fleet; old values set back through Custom Data. TH blocks lost / share of drones disabled:

| Fleet | old | 620-670 m | 700 m (shipped) |
|---|---|---|---|
| 3 Hornet | 752 / 100 % | 989 / 89 % | 1,071 / 84 % |
| 3 Harrier | 899 / 99 % | 1,178 / 83 % | 1,266 / 76 % |
| 3 Lancer | 929 / 99 % | 1,181 / 63 % | 1,227 / 73 % |
| 3 Aegis (720 -> 780) | 1,214 / 63 % | - | 902 / 53 % |
| 3 Basilisk (750 -> 800) | 577 / 100 % | - | 602 / 98 % |
| 3 Mantis (650 -> rail range) | 900 / 98 % | - | 84 / 0 % (but 3.5x the damage points: 13-15 rail hits in 300 s, all hits) |

- **Hornet, Harrier, Lancer**: new key `Standoff Range vs Huge Grids` (default 700) in `[Hornet]` / `[Harrier]` / `[Lancer]`; huge = track bounding radius over 38 m (the Tech Hunter; the sim Pirate is under it, so Pirate fights are unchanged: 18 scenarios x 60 seeds identical).
- **Mantis**: `Engage Cap vs Huge Grids` (default 1350, 0 = off): against huge grids it sits at rail range. Survives where it died, deals more damage, removes fewer blocks (rail damage is concentrated). Owner-approved; kept.
- **Aegis, Basilisk**: 780 / 800 m tried and dropped (worse or no different vs the circling Tech Hunter); they keep 720 / 750.
- **Wasp** (unlimited-rocket key, 20 seeds, charge): 150 m 78 % blocks lost ... 650 m 67 %, dmgout 17k -> 24k; same pattern as the gun drones. Not changed (only a re-run was asked); its key is `Standoff Range (unlimited rockets)`.
- **Ballista friendly fire**: the wingmate fire gate checked the mate where it was at the trigger, using the mate's remote-control position. A full salvo went into a mate 150 m ahead that crossed the line during the 0.5 s railgun charge, and the RC sits 13 m off the hull centre. Now the mate is checked where it will be when the slug passes it, and the SWARM position is the hull centre (ShrikeAI, BATTERY v1.1, MANUAL v1.1). 4 Ballistas: 7 incidents in 480 fights -> 0, same shots, hits and damage.
- `pc/plans/charge_standoff.plan` regenerated: Hornet/Harrier/Lancer sweep the huge-grid key, the Wasp sweeps its unlimited-rockets key. Delete old `results/charge-*` folders before re-running it (different settings, the resume check refuses to mix them).

**Sim note (pack vs repo):** the SimPack has no `defs.json`, so its block connectivity falls back to face contact; in the repo the mount-point check runs and a fight against the 13,666-block Tech Hunter is about 80x slower (2 s vs 170 s), with the same outcome in the fight checked. The A/B above used pack mode, like the owner's run.
