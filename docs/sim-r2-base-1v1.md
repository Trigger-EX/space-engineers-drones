# Round-2 baseline: r2-base-1v1

4550 runs in 91 scenarios (1v1 4550). Outcomes: mutual-kill 37, no-contest 50, timeout 1823, win 2640.

Total wall time of all runs 1366 s (0.30 s per run on average); settings hash 86da6f4425ff003e.

Command line: `run --filter ^1v1/ --seeds 50 --out out/sim/results/r2-base-1v1`

## 1v1 matrix

Row model wins against column model (share of runs; the rest are draws, timeouts and losses). The diagonal (mirror fights) shows the share of runs with a decisive result.

|  | Aegis | Basilisk | Gnat | Harrier | Hornet | Lancer | Longbow L | Longbow S | Mantis | Midge | Stinger | Wasp | Watcher |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Aegis | 72% | 46% | 62% | 26% | 46% | 18% | 40% | 14% | 14% | 36% | 64% | 58% | 60% |
| Basilisk | 28% | 66% | 66% | 30% | 46% | 18% | 0% | 0% | 60% | 68% | 74% | 56% | 62% |
| Gnat | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% |
| Harrier | 36% | 38% | 64% | 64% | 52% | 18% | 50% | 30% | 36% | 56% | 58% | 76% | 62% |
| Hornet | 20% | 20% | 52% | 10% | 64% | 22% | 40% | 14% | 20% | 52% | 62% | 50% | 72% |
| Lancer | 50% | 38% | 64% | 38% | 46% | 64% | 60% | 22% | 28% | 64% | 66% | 66% | 66% |
| Longbow L | 6% | 42% | 58% | 12% | 18% | 10% | 46% | 52% | 44% | 34% | 52% | 46% | 28% |
| Longbow S | 52% | 26% | 62% | 32% | 58% | 50% | 8% | 62% | 36% | 54% | 68% | 66% | 52% |
| Mantis | 56% | 4% | 68% | 42% | 44% | 40% | 16% | 24% | 68% | 30% | 58% | 76% | 58% |
| Midge | 6% | 0% | 68% | 4% | 12% | 6% | 4% | 0% | 32% | 60% | 76% | 34% | 64% |
| Stinger | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% |
| Wasp | 2% | 6% | 74% | 0% | 6% | 2% | 4% | 0% | 0% | 22% | 72% | 62% | 66% |
| Watcher | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% |

### Ranking by 1v1 score (win 1, draw/timeout 0.5)

| model | score | sides | hits per shot | avg damage taken |
| :--- | ---: | ---: | ---: | ---: |
| Lancer | 65.1% | 700 | 37.2% | 1516 |
| Longbow S | 64.6% | 700 | 64.4% | 929 |
| Harrier | 63.6% | 700 | 40.6% | 1738 |
| Basilisk | 60.3% | 700 | 41.3% | 19836 |
| Mantis | 58.8% | 700 | 38.7% | 1949 |
| Aegis | 58.1% | 700 | 37.6% | 2375 |
| Longbow L | 56.4% | 700 | 68.8% | 8559 |
| Hornet | 53.8% | 700 | 37.0% | 2517 |
| Midge | 46.1% | 700 | 30.6% | 2323 |
| Wasp | 40.2% | 700 | 7.5% | 3561 |
| Watcher | 28.9% | 700 | - | 4223 |
| Gnat | 27.2% | 700 | - | 1550 |
| Stinger | 26.8% | 700 | - | 1671 |

## Problems seen

No script exceptions.

Stuck or circling flags (speed < 1 m/s for 30 s, or no engagement within 1.5x weapon range for 60 s):

| model | ship-runs | flagged | share | kinds |
| :--- | ---: | ---: | ---: | ---: |
| Aegis | 700 | 229 | 33% | slow 226, noengage 3 |
| Basilisk | 700 | 269 | 38% | slow 208, noengage 61 |
| Gnat | 700 | 644 | 92% | slow 644 |
| Harrier | 700 | 224 | 32% | slow 224 |
| Hornet | 700 | 230 | 33% | slow 226, noengage 4 |
| Lancer | 700 | 212 | 30% | slow 211, noengage 1 |
| Longbow L | 700 | 237 | 34% | slow 236, noengage 1 |
| Longbow S | 700 | 214 | 31% | slow 214 |
| Mantis | 700 | 210 | 30% | slow 210 |
| Midge | 700 | 245 | 35% | slow 222, noengage 23 |
| Stinger | 700 | 645 | 92% | slow 645 |
| Wasp | 700 | 230 | 33% | slow 218, noengage 12 |

## Cost

| kind | runs | avg wall ms | avg sim s | wall ms per sim s |
| :--- | ---: | ---: | ---: | ---: |
| 1v1 | 4550 | 300 | 190.0 | 1.6 |

| script | total cpu ms | Main calls | ms per call | slowest call ms |
| :--- | ---: | ---: | ---: | ---: |
| LongbowSniperAI | 217705 | 16383069 | 0.0133 | 40.83 |
| HornetDroneAI | 194672 | 15414117 | 0.0126 | 32.64 |
| GnatSwarmAI | 104894 | 9192354 | 0.0114 | 25.01 |
| MantisAI | 76628 | 7173743 | 0.0107 | 20.09 |
| AegisAI | 74550 | 7628438 | 0.0098 | 30.44 |
| LancerAI | 71761 | 7189104 | 0.0100 | 11.59 |
| WaspAI | 70822 | 7446553 | 0.0095 | 13.60 |
| HarrierAI | 70180 | 7184429 | 0.0098 | 18.86 |
| BasiliskAI | 68920 | 7756542 | 0.0089 | 14.00 |
| StingerAI | 9802 | 9233228 | 0.0011 | 9.81 |
| (brain) | 0 | 0 | 0.0000 | 0.00 |

