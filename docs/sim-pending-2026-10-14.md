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
- **Friendly fire**: in the 4-wing all the damage the Ballistas take vs the holding Tech Hunter is friendly (715 per ship per fight; 227 vs the pirate-flown one; 0 in single ships). The 45 m `Wingmate Fire Clearance` does not fully stop it.
- Damage: 4 Ballistas strip 10 % of the Tech Hunter's blocks in 120 s and 18 % in 300 s; it was disabled once in 320 fights. One Ballista is never hurt.

## 4. 1v1 matrix (29,120 fights, current scripts)

No exceptions. Win % against other models: Aegis 64, Hornet 56, Harrier 54, Longbow S 54, Lancer 53, Mantis 51, Basilisk 49, Longbow L 45, Midge 24, Wasp 19, Gnat 15.
24 % timeouts; the most common stuck flags are `noengage` (Basilisk, Wasp, Gnat, Lancer, Harrier, Aegis, about 1,000-1,500 fights each).
