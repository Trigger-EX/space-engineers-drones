# Final matrix (round 2 scripts, fixed engine), 99.5% of 47,300 runs

47072 runs in 471 scenarios (1v1 9100, 1v2 15600, 1v3 4300, 2v3 4300, mixed 6200, pirate 3372, swarm 4200). Outcomes: mutual-kill 162, no-contest 100, timeout 12765, win 34045.

Total wall time of all runs 28791 s (0.61 s per run on average); settings hash 86da6f4425ff003e.

Command line: `run --seeds 100 --resume --out out/sim/results/final-r2`

## 1v1 matrix

Row model wins against column model (share of runs; the rest are draws, timeouts and losses). The diagonal (mirror fights) shows the share of runs with a decisive result.

|  | Aegis | Basilisk | Gnat | Harrier | Hornet | Lancer | Longbow L | Longbow S | Mantis | Midge | Stinger | Wasp | Watcher |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Aegis | 86% | 64% | 91% | 53% | 63% | 53% | 59% | 41% | 53% | 81% | 92% | 78% | 94% |
| Basilisk | 26% | 83% | 91% | 45% | 51% | 40% | 0% | 0% | 80% | 74% | 96% | 79% | 95% |
| Gnat | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% |
| Harrier | 37% | 47% | 92% | 87% | 50% | 44% | 45% | 12% | 36% | 88% | 90% | 81% | 89% |
| Hornet | 26% | 43% | 86% | 34% | 87% | 49% | 65% | 27% | 40% | 92% | 85% | 80% | 93% |
| Lancer | 33% | 52% | 94% | 46% | 38% | 88% | 48% | 13% | 37% | 79% | 93% | 89% | 91% |
| Longbow L | 36% | 48% | 63% | 46% | 27% | 37% | 56% | 41% | 37% | 32% | 66% | 75% | 29% |
| Longbow S | 50% | 17% | 60% | 80% | 67% | 75% | 21% | 58% | 41% | 58% | 65% | 93% | 49% |
| Mantis | 37% | 13% | 71% | 50% | 48% | 54% | 20% | 22% | 65% | 25% | 57% | 89% | 59% |
| Midge | 1% | 0% | 61% | 4% | 4% | 17% | 6% | 0% | 44% | 62% | 70% | 39% | 64% |
| Stinger | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% |
| Wasp | 3% | 7% | 97% | 5% | 4% | 4% | 12% | 2% | 2% | 28% | 97% | 62% | 93% |
| Watcher | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% |

### Ranking by 1v1 score (win 1, draw/timeout 0.5)

| model | score | sides | hits per shot | avg damage taken |
| :--- | ---: | ---: | ---: | ---: |
| Aegis | 70.5% | 1400 | 36.1% | 2854 |
| Longbow S | 68.5% | 1400 | 72.9% | 1138 |
| Basilisk | 63.8% | 1400 | 45.0% | 25708 |
| Hornet | 63.1% | 1400 | 40.3% | 3126 |
| Harrier | 62.4% | 1400 | 40.9% | 2990 |
| Lancer | 62.1% | 1400 | 35.7% | 2933 |
| Longbow L | 59.3% | 1400 | 79.4% | 9074 |
| Mantis | 56.2% | 1400 | 34.8% | 2833 |
| Midge | 41.2% | 1400 | 28.8% | 3142 |
| Wasp | 37.5% | 1400 | 11.2% | 4791 |
| Watcher | 23.0% | 1400 | - | 5593 |
| Gnat | 21.2% | 1400 | - | 2107 |
| Stinger | 21.0% | 1400 | - | 2201 |

## 1v2

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Aegis-vs-2xBasilisk | 100 | 1% | 97% | 0% | 2% | 0 | 52.6 | 79.3 | 29.5 |
| Aegis-vs-2xGnat | 100 | 91% | 0% | 0% | 9% | 0 | 103.8 | 19.9 | - |
| Aegis-vs-2xHarrier | 100 | 5% | 90% | 0% | 5% | 0 | 81.2 | 17.7 | 33.8 |
| Aegis-vs-2xHornet | 100 | 3% | 95% | 0% | 2% | 0 | 67.2 | 22.6 | 31.8 |
| Aegis-vs-2xLancer | 100 | 10% | 86% | 0% | 4% | 0 | 100.7 | 21.8 | 29.6 |
| Aegis-vs-2xLongbow L | 100 | 0% | 89% | 0% | 11% | 0 | 88.5 | 77.2 | 77.2 |
| Aegis-vs-2xLongbow S | 100 | 1% | 98% | 0% | 1% | 0 | 74.0 | 25.2 | 67.2 |
| Aegis-vs-2xMantis | 100 | 0% | 97% | 0% | 3% | 0 | 58.7 | 23.5 | 25.3 |
| Aegis-vs-2xMidge | 100 | 15% | 77% | 1% | 7% | 0 | 126.8 | 20.4 | 24.6 |
| Aegis-vs-2xStinger | 100 | 89% | 0% | 0% | 11% | 0 | 113.8 | 20.2 | - |
| Aegis-vs-2xWasp | 100 | 58% | 30% | 2% | 10% | 0 | 111.1 | 27.1 | 2.2 |
| Aegis-vs-2xWatcher | 100 | 96% | 0% | 0% | 4% | 0 | 108.4 | 28.6 | - |
| Basilisk-vs-2xAegis | 100 | 0% | 100% | 0% | 0% | 0 | 50.5 | 18.8 | 76.8 |
| Basilisk-vs-2xGnat | 100 | 91% | 0% | 0% | 9% | 0 | 107.3 | 27.3 | 83.1 |
| Basilisk-vs-2xHarrier | 100 | 4% | 94% | 0% | 2% | 0 | 57.8 | 25.2 | 80.5 |
| Basilisk-vs-2xHornet | 100 | 3% | 95% | 0% | 2% | 0 | 55.6 | 28.2 | 81.0 |
| Basilisk-vs-2xLancer | 100 | 0% | 98% | 0% | 2% | 0 | 61.3 | 19.4 | 77.9 |
| Basilisk-vs-2xLongbow L | 100 | 0% | 73% | 0% | 27% | 0 | 114.5 | - | 88.9 |
| Basilisk-vs-2xLongbow S | 100 | 0% | 67% | 0% | 33% | 0 | 173.3 | 6.1 | 90.8 |
| Basilisk-vs-2xMantis | 100 | 12% | 84% | 1% | 3% | 0 | 75.5 | 25.9 | 73.0 |
| Basilisk-vs-2xMidge | 100 | 31% | 41% | 3% | 25% | 0 | 143.4 | 24.4 | 98.1 |
| Basilisk-vs-2xStinger | 100 | 93% | 0% | 0% | 7% | 0 | 87.8 | 32.3 | - |
| Basilisk-vs-2xWasp | 100 | 58% | 33% | 5% | 4% | 0 | 108.2 | 42.6 | 29.3 |
| Basilisk-vs-2xWatcher | 100 | 96% | 0% | 0% | 4% | 0 | 78.0 | 49.2 | - |
| Gnat-vs-2xAegis | 100 | 0% | 95% | 0% | 5% | 0 | 76.7 | - | 15.9 |
| Gnat-vs-2xBasilisk | 100 | 0% | 98% | 0% | 2% | 0 | 73.2 | - | 14.5 |
| Gnat-vs-2xHarrier | 100 | 0% | 97% | 0% | 3% | 0 | 75.8 | - | 22.4 |
| Gnat-vs-2xHornet | 100 | 0% | 99% | 0% | 1% | 0 | 59.2 | - | 22.3 |
| Gnat-vs-2xLancer | 100 | 0% | 97% | 0% | 3% | 0 | 79.3 | - | 21.4 |
| Gnat-vs-2xLongbow L | 100 | 0% | 61% | 0% | 39% | 0 | 75.5 | - | 79.3 |
| Gnat-vs-2xLongbow S | 100 | 0% | 62% | 0% | 38% | 0 | 47.8 | - | 76.2 |
| Gnat-vs-2xMantis | 100 | 0% | 71% | 0% | 29% | 0 | 61.3 | - | 13.0 |
| Gnat-vs-2xMidge | 100 | 0% | 73% | 0% | 27% | 0 | 194.3 | - | 17.0 |
| Gnat-vs-2xStinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Gnat-vs-2xWasp | 100 | 0% | 99% | 0% | 1% | 0 | 51.0 | - | 10.4 |
| Gnat-vs-2xWatcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Harrier-vs-2xAegis | 100 | 0% | 98% | 0% | 2% | 0 | 77.1 | 22.3 | 24.4 |
| Harrier-vs-2xBasilisk | 100 | 0% | 98% | 0% | 2% | 0 | 57.3 | 82.3 | 29.9 |
| Harrier-vs-2xGnat | 100 | 86% | 0% | 0% | 14% | 0 | 115.1 | 23.1 | - |
| Harrier-vs-2xHornet | 100 | 1% | 95% | 0% | 4% | 0 | 66.8 | 24.4 | 30.7 |
| Harrier-vs-2xLancer | 100 | 3% | 91% | 0% | 6% | 0 | 86.1 | 25.5 | 25.8 |
| Harrier-vs-2xLongbow L | 100 | 0% | 86% | 0% | 14% | 0 | 86.0 | 81.0 | 69.7 |
| Harrier-vs-2xLongbow S | 100 | 1% | 96% | 0% | 3% | 0 | 86.5 | 28.9 | 63.9 |
| Harrier-vs-2xMantis | 100 | 0% | 93% | 0% | 7% | 0 | 65.7 | 21.7 | 25.1 |
| Harrier-vs-2xMidge | 100 | 10% | 83% | 1% | 6% | 0 | 133.3 | 25.0 | 20.0 |
| Harrier-vs-2xStinger | 100 | 88% | 0% | 0% | 12% | 0 | 110.3 | 24.3 | - |
| Harrier-vs-2xWasp | 100 | 62% | 24% | 0% | 14% | 0 | 162.8 | 30.5 | 1.4 |
| Harrier-vs-2xWatcher | 100 | 93% | 0% | 0% | 7% | 0 | 114.2 | 31.6 | - |
| Hornet-vs-2xAegis | 100 | 0% | 98% | 0% | 2% | 0 | 69.2 | 23.5 | 24.9 |
| Hornet-vs-2xBasilisk | 100 | 0% | 96% | 0% | 4% | 0 | 49.4 | 82.8 | 28.7 |
| Hornet-vs-2xGnat | 100 | 88% | 0% | 0% | 12% | 0 | 103.0 | 23.4 | - |
| Hornet-vs-2xHarrier | 100 | 1% | 96% | 0% | 3% | 0 | 78.0 | 20.8 | 32.7 |
| Hornet-vs-2xLancer | 100 | 3% | 93% | 0% | 4% | 0 | 73.9 | 24.4 | 27.0 |
| Hornet-vs-2xLongbow L | 100 | 2% | 75% | 0% | 23% | 0 | 92.0 | 82.7 | 76.1 |
| Hornet-vs-2xLongbow S | 100 | 2% | 94% | 0% | 4% | 0 | 73.0 | 26.3 | 70.2 |
| Hornet-vs-2xMantis | 100 | 0% | 98% | 0% | 2% | 0 | 65.0 | 22.3 | 24.8 |
| Hornet-vs-2xMidge | 100 | 3% | 90% | 2% | 5% | 0 | 128.2 | 24.1 | 23.0 |
| Hornet-vs-2xStinger | 100 | 93% | 0% | 0% | 7% | 0 | 101.3 | 24.7 | - |
| Hornet-vs-2xWasp | 100 | 57% | 32% | 1% | 10% | 0 | 116.8 | 31.6 | 1.5 |
| Hornet-vs-2xWatcher | 100 | 93% | 0% | 0% | 7% | 0 | 105.2 | 33.0 | - |
| Lancer-vs-2xAegis | 100 | 1% | 91% | 0% | 8% | 0 | 69.8 | 25.6 | 24.3 |
| Lancer-vs-2xBasilisk | 100 | 0% | 93% | 0% | 7% | 0 | 62.7 | 81.1 | 30.5 |
| Lancer-vs-2xGnat | 100 | 87% | 0% | 0% | 13% | 0 | 129.0 | 20.5 | - |
| Lancer-vs-2xHarrier | 100 | 2% | 90% | 1% | 7% | 0 | 77.2 | 22.9 | 31.6 |
| Lancer-vs-2xHornet | 100 | 0% | 97% | 0% | 3% | 0 | 77.0 | 20.0 | 31.1 |
| Lancer-vs-2xLongbow L | 100 | 1% | 86% | 0% | 13% | 0 | 111.8 | 80.9 | 68.5 |
| Lancer-vs-2xLongbow S | 100 | 0% | 92% | 0% | 8% | 0 | 89.1 | 23.2 | 61.9 |
| Lancer-vs-2xMantis | 100 | 0% | 94% | 0% | 6% | 0 | 65.7 | 23.1 | 24.2 |
| Lancer-vs-2xMidge | 100 | 3% | 88% | 0% | 9% | 0 | 152.3 | 22.2 | 20.2 |
| Lancer-vs-2xStinger | 100 | 89% | 0% | 0% | 11% | 0 | 116.8 | 22.2 | - |
| Lancer-vs-2xWasp | 100 | 58% | 28% | 0% | 14% | 0 | 132.6 | 29.3 | 1.3 |
| Lancer-vs-2xWatcher | 100 | 78% | 0% | 0% | 22% | 0 | 153.4 | 25.9 | - |
| Longbow L-vs-2xAegis | 100 | 0% | 98% | 0% | 2% | 0 | 63.3 | 64.3 | 65.1 |
| Longbow L-vs-2xBasilisk | 100 | 1% | 33% | 0% | 66% | 0 | 163.6 | 92.4 | 79.9 |
| Longbow L-vs-2xGnat | 100 | 5% | 0% | 0% | 95% | 0 | 227.2 | 78.7 | - |
| Longbow L-vs-2xHarrier | 100 | 1% | 89% | 0% | 10% | 0 | 108.2 | 75.1 | 76.2 |
| Longbow L-vs-2xHornet | 100 | 0% | 99% | 0% | 1% | 0 | 56.2 | 64.7 | 73.9 |
| Longbow L-vs-2xLancer | 100 | 0% | 92% | 0% | 8% | 0 | 112.0 | 71.4 | 66.3 |
| Longbow L-vs-2xLongbow S | 100 | 9% | 44% | 0% | 47% | 0 | 122.2 | 79.4 | 79.1 |
| Longbow L-vs-2xMantis | 100 | 1% | 70% | 1% | 28% | 0 | 51.2 | 83.1 | 68.1 |
| Longbow L-vs-2xMidge | 100 | 0% | 10% | 0% | 90% | 0 | 187.2 | 70.5 | 71.6 |
| Longbow L-vs-2xStinger | 100 | 4% | 0% | 0% | 96% | 0 | 250.1 | 84.2 | - |
| Longbow L-vs-2xWasp | 100 | 2% | 59% | 0% | 39% | 0 | 156.3 | 81.2 | 39.8 |
| Longbow L-vs-2xWatcher | 100 | 3% | 0% | 0% | 97% | 0 | 262.5 | 91.8 | - |
| Longbow S-vs-2xAegis | 100 | 1% | 93% | 0% | 6% | 0 | 91.8 | 59.2 | 29.9 |
| Longbow S-vs-2xBasilisk | 100 | 0% | 50% | 0% | 50% | 0 | 178.1 | 91.8 | 41.5 |
| Longbow S-vs-2xGnat | 100 | 36% | 0% | 0% | 64% | 0 | 233.2 | 64.3 | - |
| Longbow S-vs-2xHarrier | 100 | 0% | 90% | 0% | 10% | 0 | 98.2 | 49.3 | 31.9 |
| Longbow S-vs-2xHornet | 100 | 0% | 95% | 0% | 5% | 0 | 72.5 | 50.3 | 34.5 |
| Longbow S-vs-2xLancer | 100 | 1% | 84% | 0% | 15% | 0 | 106.5 | 60.4 | 26.8 |
| Longbow S-vs-2xLongbow L | 100 | 0% | 67% | 0% | 33% | 0 | 45.8 | 79.8 | 80.5 |
| Longbow S-vs-2xMantis | 100 | 1% | 62% | 0% | 37% | 0 | 57.2 | 59.0 | 26.7 |
| Longbow S-vs-2xMidge | 100 | 7% | 12% | 0% | 81% | 0 | 249.8 | 60.8 | 34.2 |
| Longbow S-vs-2xStinger | 100 | 27% | 0% | 0% | 73% | 0 | 232.8 | 77.5 | - |
| Longbow S-vs-2xWasp | 100 | 6% | 50% | 0% | 44% | 0 | 141.2 | 70.9 | 6.4 |
| Longbow S-vs-2xWatcher | 100 | 10% | 0% | 0% | 90% | 0 | 237.1 | 79.1 | - |
| Mantis-vs-2xAegis | 100 | 0% | 99% | 0% | 1% | 0 | 86.8 | 28.6 | 31.3 |
| Mantis-vs-2xBasilisk | 100 | 0% | 99% | 0% | 1% | 0 | 64.8 | 75.2 | 25.6 |
| Mantis-vs-2xGnat | 100 | 54% | 0% | 0% | 46% | 0 | 203.6 | 17.0 | - |
| Mantis-vs-2xHarrier | 100 | 0% | 97% | 0% | 3% | 0 | 77.8 | 26.1 | 38.7 |
| Mantis-vs-2xHornet | 100 | 0% | 98% | 0% | 2% | 0 | 83.2 | 24.7 | 37.5 |
| Mantis-vs-2xLancer | 100 | 1% | 96% | 0% | 3% | 0 | 96.3 | 22.7 | 29.7 |
| Mantis-vs-2xLongbow L | 100 | 0% | 73% | 0% | 27% | 0 | 49.7 | 53.9 | 72.1 |
| Mantis-vs-2xLongbow S | 100 | 0% | 61% | 0% | 39% | 0 | 53.3 | 33.9 | 69.4 |
| Mantis-vs-2xMidge | 100 | 0% | 62% | 0% | 38% | 0 | 97.0 | 22.6 | 39.7 |
| Mantis-vs-2xStinger | 100 | 57% | 0% | 0% | 43% | 0 | 188.7 | 18.1 | - |
| Mantis-vs-2xWasp | 100 | 27% | 53% | 2% | 18% | 0 | 125.5 | 30.6 | 3.7 |
| Mantis-vs-2xWatcher | 100 | 30% | 0% | 0% | 70% | 0 | 199.6 | 25.1 | - |
| Midge-vs-2xAegis | 100 | 0% | 99% | 0% | 1% | 0 | 85.7 | 17.8 | 20.7 |
| Midge-vs-2xBasilisk | 100 | 0% | 81% | 0% | 19% | 0 | 121.0 | 98.8 | 18.6 |
| Midge-vs-2xGnat | 100 | 47% | 0% | 0% | 53% | 0 | 224.0 | 15.0 | - |
| Midge-vs-2xHarrier | 100 | 0% | 100% | 0% | 0% | 0 | 80.3 | 9.0 | 27.8 |
| Midge-vs-2xHornet | 100 | 0% | 99% | 0% | 1% | 0 | 76.2 | 17.4 | 26.3 |
| Midge-vs-2xLancer | 100 | 0% | 98% | 0% | 2% | 0 | 81.9 | 13.3 | 24.1 |
| Midge-vs-2xLongbow L | 100 | 0% | 56% | 0% | 44% | 0 | 125.3 | - | 74.2 |
| Midge-vs-2xLongbow S | 100 | 0% | 66% | 0% | 34% | 0 | 73.3 | - | 63.9 |
| Midge-vs-2xMantis | 100 | 0% | 70% | 0% | 30% | 0 | 61.6 | 29.9 | 19.3 |
| Midge-vs-2xStinger | 100 | 59% | 0% | 0% | 41% | 0 | 244.3 | 20.6 | - |
| Midge-vs-2xWasp | 100 | 8% | 73% | 0% | 19% | 0 | 127.5 | 25.4 | 5.5 |
| Midge-vs-2xWatcher | 100 | 41% | 0% | 0% | 59% | 0 | 247.5 | 24.4 | - |
| Stinger-vs-2xAegis | 100 | 0% | 96% | 0% | 4% | 0 | 87.3 | - | 17.5 |
| Stinger-vs-2xBasilisk | 100 | 0% | 98% | 0% | 2% | 0 | 51.1 | - | 18.1 |
| Stinger-vs-2xGnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Stinger-vs-2xHarrier | 100 | 0% | 98% | 0% | 2% | 0 | 74.7 | - | 23.8 |
| Stinger-vs-2xHornet | 100 | 0% | 95% | 0% | 5% | 0 | 64.3 | - | 23.6 |
| Stinger-vs-2xLancer | 100 | 0% | 95% | 0% | 5% | 0 | 72.5 | - | 22.3 |
| Stinger-vs-2xLongbow L | 100 | 0% | 73% | 0% | 27% | 0 | 83.8 | - | 76.3 |
| Stinger-vs-2xLongbow S | 100 | 0% | 68% | 0% | 32% | 0 | 56.8 | - | 73.7 |
| Stinger-vs-2xMantis | 100 | 0% | 72% | 0% | 28% | 0 | 61.4 | - | 14.6 |
| Stinger-vs-2xMidge | 100 | 0% | 65% | 0% | 35% | 0 | 185.8 | - | 22.1 |
| Stinger-vs-2xWasp | 100 | 0% | 98% | 0% | 2% | 0 | 52.0 | - | 10.2 |
| Stinger-vs-2xWatcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Wasp-vs-2xAegis | 100 | 0% | 96% | 0% | 4% | 0 | 71.4 | 1.8 | 26.6 |
| Wasp-vs-2xBasilisk | 100 | 0% | 94% | 0% | 6% | 0 | 75.7 | 25.4 | 30.2 |
| Wasp-vs-2xGnat | 100 | 95% | 0% | 0% | 5% | 0 | 84.0 | 13.8 | - |
| Wasp-vs-2xHarrier | 100 | 0% | 98% | 0% | 2% | 0 | 82.0 | 0.5 | 35.9 |
| Wasp-vs-2xHornet | 100 | 0% | 99% | 0% | 1% | 0 | 65.8 | 0.4 | 34.8 |
| Wasp-vs-2xLancer | 100 | 0% | 97% | 0% | 3% | 0 | 77.5 | 0.2 | 30.9 |
| Wasp-vs-2xLongbow L | 100 | 0% | 93% | 0% | 7% | 0 | 86.0 | 59.5 | 77.6 |
| Wasp-vs-2xLongbow S | 100 | 0% | 93% | 0% | 7% | 0 | 80.2 | 1.2 | 75.4 |
| Wasp-vs-2xMantis | 100 | 0% | 98% | 0% | 2% | 0 | 83.6 | 1.5 | 25.8 |
| Wasp-vs-2xMidge | 100 | 1% | 71% | 0% | 28% | 0 | 126.5 | 4.5 | 24.6 |
| Wasp-vs-2xStinger | 100 | 93% | 0% | 0% | 7% | 0 | 66.8 | 13.4 | - |
| Wasp-vs-2xWatcher | 100 | 96% | 0% | 0% | 4% | 0 | 64.2 | 16.6 | - |
| Watcher-vs-2xAegis | 100 | 0% | 97% | 0% | 3% | 0 | 59.7 | - | 23.1 |
| Watcher-vs-2xBasilisk | 100 | 0% | 100% | 0% | 0% | 0 | 50.8 | - | 35.1 |
| Watcher-vs-2xGnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher-vs-2xHarrier | 100 | 0% | 98% | 0% | 2% | 0 | 58.8 | - | 29.6 |
| Watcher-vs-2xHornet | 100 | 0% | 100% | 0% | 0% | 0 | 54.1 | - | 30.1 |
| Watcher-vs-2xLancer | 100 | 0% | 99% | 0% | 1% | 0 | 67.7 | - | 23.2 |
| Watcher-vs-2xLongbow L | 100 | 0% | 48% | 0% | 52% | 0 | 156.8 | - | 74.1 |
| Watcher-vs-2xLongbow S | 100 | 0% | 63% | 0% | 37% | 0 | 79.7 | - | 62.8 |
| Watcher-vs-2xMantis | 100 | 0% | 72% | 0% | 28% | 0 | 60.2 | - | 21.8 |
| Watcher-vs-2xMidge | 100 | 0% | 67% | 0% | 33% | 0 | 189.3 | - | 21.8 |
| Watcher-vs-2xStinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher-vs-2xWasp | 100 | 0% | 100% | 0% | 0% | 0 | 51.7 | - | 11.5 |

## 1v3

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Aegis-vs-3xGnat | 100 | 82% | 0% | 0% | 18% | 0 | 126.5 | 17.4 | - |
| Aegis-vs-3xHornet | 100 | 0% | 97% | 0% | 3% | 0 | 79.3 | 22.7 | 30.3 |
| Aegis-vs-3xMidge | 100 | 1% | 87% | 0% | 12% | 0 | 124.8 | 21.0 | 18.3 |
| Aegis-vs-3xStinger | 100 | 91% | 0% | 0% | 9% | 0 | 121.3 | 17.2 | - |
| Aegis-vs-3xWasp | 100 | 29% | 58% | 1% | 12% | 0 | 118.8 | 24.4 | 2.5 |
| Basilisk-vs-3xGnat | 100 | 82% | 2% | 0% | 16% | 0 | 116.8 | 28.4 | 51.4 |
| Basilisk-vs-3xHornet | 100 | 0% | 100% | 0% | 0% | 0 | 45.1 | 23.4 | 80.3 |
| Basilisk-vs-3xMidge | 100 | 2% | 75% | 1% | 22% | 0 | 135.3 | 23.5 | 97.1 |
| Basilisk-vs-3xStinger | 100 | 88% | 0% | 0% | 12% | 0 | 100.9 | 34.9 | - |
| Basilisk-vs-3xWasp | 100 | 38% | 61% | 0% | 1% | 0 | 131.0 | 43.4 | 29.5 |
| Harrier-vs-3xGnat | 100 | 85% | 0% | 0% | 15% | 0 | 142.0 | 21.0 | - |
| Harrier-vs-3xHornet | 100 | 0% | 99% | 0% | 1% | 0 | 63.5 | 22.3 | 28.6 |
| Harrier-vs-3xMidge | 100 | 0% | 98% | 0% | 2% | 0 | 137.2 | 26.8 | 18.0 |
| Harrier-vs-3xStinger | 100 | 91% | 0% | 0% | 9% | 0 | 151.0 | 18.7 | - |
| Harrier-vs-3xWasp | 100 | 19% | 58% | 0% | 23% | 0 | 147.2 | 27.0 | 1.8 |
| Hornet-vs-3xGnat | 100 | 92% | 0% | 0% | 8% | 0 | 131.5 | 22.7 | - |
| Hornet-vs-3xMidge | 100 | 0% | 96% | 0% | 4% | 0 | 109.8 | 29.2 | 24.2 |
| Hornet-vs-3xStinger | 100 | 89% | 0% | 0% | 11% | 0 | 125.8 | 23.7 | - |
| Hornet-vs-3xWasp | 100 | 31% | 53% | 1% | 15% | 0 | 120.2 | 30.8 | 1.7 |
| Lancer-vs-3xGnat | 100 | 88% | 0% | 0% | 12% | 0 | 145.1 | 20.7 | - |
| Lancer-vs-3xHornet | 100 | 0% | 98% | 0% | 2% | 0 | 63.7 | 22.5 | 27.6 |
| Lancer-vs-3xMidge | 100 | 0% | 96% | 0% | 4% | 0 | 133.1 | 23.5 | 19.7 |
| Lancer-vs-3xStinger | 100 | 86% | 0% | 0% | 14% | 0 | 149.0 | 21.6 | - |
| Lancer-vs-3xWasp | 100 | 23% | 42% | 1% | 34% | 0 | 157.8 | 27.2 | 1.3 |
| Longbow L-vs-3xGnat | 100 | 0% | 0% | 0% | 100% | 0 | - | 80.9 | - |
| Longbow L-vs-3xHornet | 100 | 0% | 99% | 0% | 1% | 0 | 45.5 | 67.0 | 72.4 |
| Longbow L-vs-3xMidge | 100 | 0% | 13% | 0% | 87% | 0 | 243.5 | 70.7 | 70.1 |
| Longbow L-vs-3xStinger | 100 | 0% | 0% | 0% | 100% | 0 | - | 81.3 | - |
| Longbow L-vs-3xWasp | 100 | 0% | 68% | 0% | 32% | 0 | 134.1 | 77.8 | 37.8 |
| Longbow S-vs-3xGnat | 100 | 3% | 0% | 0% | 97% | 0 | 270.8 | 68.2 | - |
| Longbow S-vs-3xHornet | 100 | 0% | 98% | 0% | 2% | 0 | 64.2 | 47.1 | 33.3 |
| Longbow S-vs-3xMidge | 100 | 0% | 29% | 0% | 71% | 0 | 214.7 | 58.8 | 33.5 |
| Longbow S-vs-3xStinger | 100 | 2% | 0% | 0% | 98% | 0 | 283.5 | 74.6 | - |
| Longbow S-vs-3xWasp | 100 | 0% | 64% | 0% | 36% | 0 | 134.2 | 71.2 | 7.1 |
| Mantis-vs-3xGnat | 100 | 34% | 0% | 0% | 66% | 0 | 248.1 | 17.5 | 35.3 |
| Mantis-vs-3xHornet | 100 | 0% | 100% | 0% | 0% | 0 | 59.8 | 23.7 | 35.5 |
| Mantis-vs-3xMidge | 100 | 0% | 66% | 0% | 34% | 0 | 101.5 | 22.5 | 37.1 |
| Mantis-vs-3xStinger | 100 | 37% | 0% | 0% | 63% | 0 | 239.2 | 17.9 | - |
| Mantis-vs-3xWasp | 100 | 6% | 65% | 0% | 29% | 0 | 121.5 | 29.4 | 4.1 |
| Wasp-vs-3xGnat | 100 | 93% | 0% | 0% | 7% | 0 | 99.2 | 13.1 | - |
| Wasp-vs-3xHornet | 100 | 0% | 100% | 0% | 0% | 0 | 59.5 | 0.3 | 30.8 |
| Wasp-vs-3xMidge | 100 | 0% | 73% | 0% | 27% | 0 | 126.0 | 3.8 | 25.2 |
| Wasp-vs-3xStinger | 100 | 97% | 0% | 0% | 3% | 0 | 79.8 | 13.9 | - |

## 2v3

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2xAegis-vs-3xGnat | 100 | 99% | 0% | 0% | 1% | 0 | 104.3 | 14.5 | - |
| 2xAegis-vs-3xHornet | 100 | 6% | 89% | 2% | 3% | 0 | 86.2 | 22.4 | 28.3 |
| 2xAegis-vs-3xMidge | 100 | 44% | 52% | 2% | 2% | 0 | 110.4 | 20.6 | 27.3 |
| 2xAegis-vs-3xStinger | 100 | 98% | 0% | 0% | 2% | 0 | 93.1 | 15.5 | - |
| 2xAegis-vs-3xWasp | 100 | 78% | 16% | 2% | 4% | 0 | 103.3 | 22.7 | 2.1 |
| 2xBasilisk-vs-3xGnat | 100 | 89% | 0% | 0% | 11% | 0 | 91.3 | 22.9 | 81.8 |
| 2xBasilisk-vs-3xHornet | 100 | 81% | 11% | 0% | 8% | 0 | 98.9 | 40.4 | 82.6 |
| 2xBasilisk-vs-3xMidge | 100 | 67% | 10% | 0% | 23% | 0 | 142.5 | 28.5 | 96.4 |
| 2xBasilisk-vs-3xStinger | 100 | 98% | 0% | 0% | 2% | 0 | 84.2 | 28.6 | - |
| 2xBasilisk-vs-3xWasp | 100 | 99% | 1% | 0% | 0% | 0 | 111.2 | 38.3 | 45.3 |
| 2xHarrier-vs-3xGnat | 100 | 97% | 0% | 0% | 3% | 0 | 105.8 | 18.6 | - |
| 2xHarrier-vs-3xHornet | 100 | 4% | 95% | 0% | 1% | 0 | 86.2 | 27.3 | 25.7 |
| 2xHarrier-vs-3xMidge | 100 | 44% | 52% | 0% | 4% | 0 | 130.1 | 25.9 | 20.3 |
| 2xHarrier-vs-3xStinger | 100 | 97% | 0% | 0% | 3% | 0 | 96.7 | 19.7 | - |
| 2xHarrier-vs-3xWasp | 100 | 84% | 7% | 0% | 9% | 0 | 123.0 | 27.5 | 1.5 |
| 2xHornet-vs-3xGnat | 100 | 93% | 0% | 0% | 7% | 0 | 104.8 | 18.5 | 37.0 |
| 2xHornet-vs-3xMidge | 100 | 40% | 54% | 3% | 3% | 0 | 115.8 | 26.5 | 22.3 |
| 2xHornet-vs-3xStinger | 100 | 98% | 0% | 0% | 2% | 0 | 90.8 | 20.4 | - |
| 2xHornet-vs-3xWasp | 100 | 86% | 7% | 0% | 7% | 0 | 124.5 | 29.3 | 1.1 |
| 2xLancer-vs-3xGnat | 100 | 99% | 0% | 0% | 1% | 0 | 115.7 | 17.8 | - |
| 2xLancer-vs-3xHornet | 100 | 2% | 96% | 0% | 2% | 0 | 98.0 | 23.8 | 25.7 |
| 2xLancer-vs-3xMidge | 100 | 23% | 67% | 4% | 6% | 0 | 149.0 | 24.4 | 20.5 |
| 2xLancer-vs-3xStinger | 100 | 94% | 0% | 0% | 6% | 0 | 115.6 | 19.5 | - |
| 2xLancer-vs-3xWasp | 100 | 81% | 7% | 1% | 11% | 0 | 156.7 | 24.6 | 1.1 |
| 2xLongbow L-vs-3xGnat | 100 | 3% | 0% | 0% | 97% | 0 | 155.5 | 66.7 | - |
| 2xLongbow L-vs-3xHornet | 100 | 0% | 73% | 0% | 27% | 0 | 172.3 | 79.0 | 77.2 |
| 2xLongbow L-vs-3xMidge | 100 | 0% | 1% | 0% | 99% | 0 | 130.8 | 72.5 | 86.6 |
| 2xLongbow L-vs-3xStinger | 100 | 0% | 0% | 0% | 100% | 0 | - | 73.8 | - |
| 2xLongbow L-vs-3xWasp | 100 | 0% | 5% | 0% | 95% | 0 | 140.5 | 76.4 | 58.9 |
| 2xLongbow S-vs-3xGnat | 100 | 20% | 0% | 0% | 80% | 0 | 244.8 | 57.3 | - |
| 2xLongbow S-vs-3xHornet | 100 | 0% | 81% | 0% | 19% | 0 | 117.7 | 58.6 | 23.1 |
| 2xLongbow S-vs-3xMidge | 100 | 4% | 10% | 0% | 86% | 0 | 212.4 | 63.8 | 29.9 |
| 2xLongbow S-vs-3xStinger | 100 | 14% | 0% | 0% | 86% | 0 | 263.2 | 61.6 | - |
| 2xLongbow S-vs-3xWasp | 100 | 3% | 29% | 0% | 68% | 0 | 172.1 | 67.0 | 8.3 |
| 2xMantis-vs-3xGnat | 100 | 65% | 0% | 0% | 35% | 0 | 200.5 | 15.6 | - |
| 2xMantis-vs-3xHornet | 100 | 0% | 91% | 0% | 9% | 0 | 97.3 | 24.4 | 29.9 |
| 2xMantis-vs-3xMidge | 100 | 3% | 59% | 0% | 38% | 0 | 138.2 | 19.1 | 27.0 |
| 2xMantis-vs-3xStinger | 100 | 62% | 0% | 0% | 38% | 0 | 166.8 | 16.2 | - |
| 2xMantis-vs-3xWasp | 100 | 51% | 29% | 1% | 19% | 0 | 139.7 | 24.4 | 4.9 |
| 2xWasp-vs-3xGnat | 100 | 100% | 0% | 0% | 0% | 0 | 91.0 | 9.2 | - |
| 2xWasp-vs-3xHornet | 100 | 0% | 99% | 0% | 1% | 0 | 75.5 | 0.9 | 27.9 |
| 2xWasp-vs-3xMidge | 100 | 4% | 73% | 1% | 22% | 0 | 160.2 | 5.3 | 20.4 |
| 2xWasp-vs-3xStinger | 100 | 99% | 0% | 0% | 1% | 0 | 76.0 | 9.0 | - |

## Mixed teams

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Hornet+Longbow S-vs-Aegis | 100 | 91% | 5% | 0% | 4% | 0 | 71.9 | 33.3 | 22.3 |
| Hornet+Longbow S-vs-Basilisk | 100 | 68% | 0% | 0% | 32% | 0 | 69.1 | 76.0 | 27.1 |
| Hornet+Longbow S-vs-Gnat | 100 | 88% | 0% | 0% | 12% | 0 | 59.2 | 25.5 | - |
| Hornet+Longbow S-vs-Harrier | 100 | 93% | 2% | 0% | 5% | 0 | 70.5 | 31.9 | 23.1 |
| Hornet+Longbow S-vs-Hornet | 100 | 99% | 0% | 0% | 1% | 0 | 94.2 | 33.6 | 24.6 |
| Hornet+Longbow S-vs-Hornet+Longbow S | 100 | 24% | 38% | 1% | 37% | 0 | 146.9 | 22.8 | 23.6 |
| Hornet+Longbow S-vs-Lancer | 100 | 99% | 0% | 0% | 1% | 0 | 77.2 | 30.6 | 18.1 |
| Hornet+Longbow S-vs-Lancer+Mantis | 100 | 45% | 28% | 0% | 27% | 0 | 156.3 | 30.3 | 21.8 |
| Hornet+Longbow S-vs-Longbow L | 100 | 86% | 2% | 1% | 11% | 0 | 63.1 | 66.4 | 65.2 |
| Hornet+Longbow S-vs-Longbow S | 100 | 94% | 0% | 0% | 6% | 0 | 72.2 | 33.9 | 51.8 |
| Hornet+Longbow S-vs-Mantis | 100 | 93% | 0% | 0% | 7% | 0 | 73.5 | 38.9 | 23.0 |
| Hornet+Longbow S-vs-Midge | 100 | 97% | 0% | 0% | 3% | 0 | 70.2 | 28.7 | 17.5 |
| Hornet+Longbow S-vs-Stinger | 100 | 86% | 0% | 0% | 14% | 0 | 71.9 | 26.4 | - |
| Hornet+Longbow S-vs-Wasp | 100 | 98% | 0% | 0% | 2% | 0 | 57.7 | 32.6 | 2.6 |
| Hornet+Longbow S-vs-Wasp+2xGnat | 100 | 88% | 1% | 0% | 11% | 0 | 118.0 | 25.4 | 13.1 |
| Hornet+Longbow S-vs-Watcher | 100 | 93% | 0% | 0% | 7% | 0 | 57.5 | 31.5 | - |
| Lancer+Mantis-vs-Aegis | 100 | 93% | 3% | 1% | 3% | 0 | 78.2 | 24.8 | 25.1 |
| Lancer+Mantis-vs-Basilisk | 100 | 97% | 0% | 1% | 2% | 0 | 65.5 | 74.1 | 25.2 |
| Lancer+Mantis-vs-Gnat | 100 | 91% | 0% | 0% | 9% | 0 | 64.3 | 16.2 | - |
| Lancer+Mantis-vs-Harrier | 100 | 98% | 0% | 0% | 2% | 0 | 66.4 | 24.4 | 27.6 |
| Lancer+Mantis-vs-Hornet | 100 | 98% | 0% | 0% | 2% | 0 | 72.8 | 26.5 | 29.7 |
| Lancer+Mantis-vs-Lancer | 100 | 98% | 0% | 0% | 2% | 0 | 76.7 | 27.7 | 27.5 |
| Lancer+Mantis-vs-Lancer+Mantis | 100 | 42% | 46% | 0% | 12% | 0 | 122.4 | 27.5 | 27.8 |
| Lancer+Mantis-vs-Longbow L | 100 | 91% | 0% | 0% | 9% | 0 | 69.0 | 66.3 | 73.6 |
| Lancer+Mantis-vs-Longbow S | 100 | 90% | 1% | 0% | 9% | 0 | 85.5 | 28.9 | 53.5 |
| Lancer+Mantis-vs-Mantis | 100 | 97% | 0% | 0% | 3% | 0 | 75.5 | 30.3 | 32.0 |
| Lancer+Mantis-vs-Midge | 100 | 97% | 0% | 0% | 3% | 0 | 81.5 | 21.5 | 27.4 |
| Lancer+Mantis-vs-Stinger | 100 | 92% | 0% | 0% | 8% | 0 | 63.2 | 17.2 | - |
| Lancer+Mantis-vs-Wasp | 100 | 100% | 0% | 0% | 0% | 0 | 85.8 | 29.6 | 0.7 |
| Lancer+Mantis-vs-Watcher | 100 | 99% | 0% | 0% | 1% | 0 | 67.0 | 24.0 | - |
| Wasp+2xGnat-vs-Aegis | 100 | 6% | 81% | 0% | 13% | 0 | 135.7 | 9.9 | 20.7 |
| Wasp+2xGnat-vs-Basilisk | 100 | 45% | 42% | 0% | 13% | 0 | 111.8 | 78.3 | 26.1 |
| Wasp+2xGnat-vs-Gnat | 100 | 96% | 0% | 0% | 4% | 0 | 73.1 | 17.0 | - |
| Wasp+2xGnat-vs-Harrier | 100 | 9% | 70% | 1% | 20% | 0 | 156.2 | 14.9 | 24.4 |
| Wasp+2xGnat-vs-Hornet | 100 | 15% | 72% | 0% | 13% | 0 | 127.5 | 13.2 | 26.4 |
| Wasp+2xGnat-vs-Lancer | 100 | 7% | 68% | 0% | 25% | 0 | 182.3 | 2.1 | 24.6 |
| Wasp+2xGnat-vs-Lancer+Mantis | 100 | 2% | 88% | 1% | 9% | 0 | 132.0 | 9.8 | 22.4 |
| Wasp+2xGnat-vs-Longbow L | 100 | 56% | 0% | 0% | 44% | 0 | 114.8 | 75.2 | 75.9 |
| Wasp+2xGnat-vs-Longbow S | 100 | 7% | 0% | 0% | 93% | 0 | 124.5 | 28.7 | 72.2 |
| Wasp+2xGnat-vs-Mantis | 100 | 21% | 15% | 0% | 64% | 0 | 165.8 | 19.9 | 25.9 |
| Wasp+2xGnat-vs-Midge | 100 | 51% | 8% | 0% | 41% | 0 | 134.0 | 12.7 | 18.4 |
| Wasp+2xGnat-vs-Stinger | 100 | 97% | 0% | 0% | 3% | 0 | 53.2 | 12.5 | - |
| Wasp+2xGnat-vs-Wasp | 100 | 41% | 45% | 0% | 14% | 0 | 117.8 | 16.3 | 9.5 |
| Wasp+2xGnat-vs-Wasp+2xGnat | 100 | 36% | 28% | 0% | 36% | 0 | 128.8 | 10.9 | 9.9 |
| Wasp+2xGnat-vs-Watcher | 100 | 98% | 0% | 0% | 2% | 0 | 64.3 | 21.9 | - |
| Watcher+3xGnat-vs-Aegis | 100 | 2% | 78% | 0% | 20% | 0 | 145.5 | 38.6 | 18.3 |
| Watcher+3xGnat-vs-Basilisk | 100 | 5% | 85% | 0% | 10% | 0 | 107.8 | 87.0 | 33.8 |
| Watcher+3xGnat-vs-Gnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher+3xGnat-vs-Harrier | 100 | 0% | 89% | 0% | 11% | 0 | 158.8 | 0.0 | 23.5 |
| Watcher+3xGnat-vs-Hornet | 100 | 2% | 84% | 0% | 14% | 0 | 150.2 | 34.9 | 24.9 |
| Watcher+3xGnat-vs-Hornet+Longbow S | 100 | 0% | 84% | 0% | 16% | 0 | 136.8 | 14.0 | 23.8 |
| Watcher+3xGnat-vs-Lancer | 100 | 0% | 76% | 0% | 24% | 0 | 192.7 | - | 23.1 |
| Watcher+3xGnat-vs-Lancer+Mantis | 100 | 0% | 90% | 0% | 10% | 0 | 151.2 | 100.0 | 19.0 |
| Watcher+3xGnat-vs-Longbow L | 100 | 2% | 1% | 0% | 97% | 0 | 213.0 | 57.7 | 84.6 |
| Watcher+3xGnat-vs-Longbow S | 100 | 1% | 5% | 0% | 94% | 0 | 270.0 | 28.7 | 65.9 |
| Watcher+3xGnat-vs-Mantis | 100 | 0% | 13% | 0% | 87% | 0 | 219.2 | - | 19.1 |
| Watcher+3xGnat-vs-Midge | 100 | 1% | 23% | 0% | 76% | 0 | 254.8 | 23.4 | 17.8 |
| Watcher+3xGnat-vs-Stinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher+3xGnat-vs-Wasp | 100 | 0% | 94% | 0% | 6% | 0 | 102.8 | - | 13.5 |
| Watcher+3xGnat-vs-Wasp+2xGnat | 100 | 0% | 92% | 0% | 8% | 0 | 120.8 | 22.5 | 11.8 |
| Watcher+3xGnat-vs-Watcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher+3xGnat-vs-Watcher+3xGnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |

## Hive swarms (Gnats)

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 12xGnat-vs-Aegis | 100 | 39% | 13% | 0% | 48% | 0 | 192.0 | 34.1 | 14.6 |
| 12xGnat-vs-Basilisk | 100 | 77% | 5% | 0% | 18% | 0 | 109.0 | 82.3 | 32.0 |
| 12xGnat-vs-Gnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 12xGnat-vs-Harrier | 100 | 49% | 0% | 0% | 51% | 0 | 169.8 | 32.3 | 19.0 |
| 12xGnat-vs-Hornet | 100 | 27% | 4% | 0% | 69% | 0 | 200.8 | 31.7 | 20.2 |
| 12xGnat-vs-Lancer | 100 | 8% | 0% | 0% | 92% | 0 | 205.3 | 23.7 | 20.8 |
| 12xGnat-vs-Longbow L | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 77.1 |
| 12xGnat-vs-Longbow S | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 67.5 |
| 12xGnat-vs-Mantis | 100 | 7% | 0% | 0% | 93% | 0 | 223.2 | 31.6 | 17.3 |
| 12xGnat-vs-Midge | 100 | 13% | 0% | 0% | 87% | 0 | 217.5 | 22.5 | 14.8 |
| 12xGnat-vs-Pirate | 100 | 30% | 4% | 0% | 66% | 0 | 147.0 | 87.7 | 12.0 |
| 12xGnat-vs-Stinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 12xGnat-vs-Wasp | 100 | 0% | 75% | 0% | 25% | 0 | 237.8 | - | 11.6 |
| 12xGnat-vs-Watcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 6xGnat-vs-Aegis | 100 | 4% | 61% | 0% | 35% | 0 | 177.8 | 43.0 | 15.2 |
| 6xGnat-vs-Basilisk | 100 | 36% | 47% | 0% | 17% | 0 | 156.5 | 83.6 | 30.7 |
| 6xGnat-vs-Gnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 6xGnat-vs-Harrier | 100 | 11% | 55% | 0% | 34% | 0 | 215.2 | 35.7 | 17.4 |
| 6xGnat-vs-Hornet | 100 | 5% | 61% | 0% | 34% | 0 | 195.9 | 34.9 | 20.5 |
| 6xGnat-vs-Lancer | 100 | 1% | 46% | 0% | 53% | 0 | 228.2 | 34.3 | 20.5 |
| 6xGnat-vs-Longbow L | 100 | 1% | 0% | 0% | 99% | 0 | 102.8 | 66.8 | 80.0 |
| 6xGnat-vs-Longbow S | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 67.8 |
| 6xGnat-vs-Mantis | 100 | 1% | 2% | 0% | 97% | 0 | 277.3 | 35.9 | 17.6 |
| 6xGnat-vs-Midge | 100 | 6% | 3% | 0% | 91% | 0 | 266.0 | 35.1 | 16.4 |
| 6xGnat-vs-Pirate | 100 | 11% | 35% | 0% | 54% | 0 | 154.7 | 90.4 | 11.0 |
| 6xGnat-vs-Stinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 6xGnat-vs-Wasp | 100 | 0% | 90% | 0% | 10% | 0 | 145.1 | - | 11.5 |
| 6xGnat-vs-Watcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 9xGnat-vs-Aegis | 100 | 27% | 33% | 0% | 40% | 0 | 204.2 | 38.8 | 14.6 |
| 9xGnat-vs-Basilisk | 100 | 57% | 20% | 0% | 23% | 0 | 147.2 | 85.0 | 30.0 |
| 9xGnat-vs-Gnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 9xGnat-vs-Harrier | 100 | 30% | 26% | 0% | 44% | 0 | 232.2 | 34.2 | 19.0 |
| 9xGnat-vs-Hornet | 100 | 11% | 24% | 0% | 65% | 0 | 236.8 | 28.6 | 20.0 |
| 9xGnat-vs-Lancer | 100 | 2% | 13% | 0% | 85% | 0 | 262.8 | 38.8 | 20.0 |
| 9xGnat-vs-Longbow L | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 80.8 |
| 9xGnat-vs-Longbow S | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 67.8 |
| 9xGnat-vs-Mantis | 100 | 7% | 0% | 0% | 93% | 0 | 218.5 | 32.7 | 18.0 |
| 9xGnat-vs-Midge | 100 | 12% | 0% | 0% | 88% | 0 | 225.5 | 21.2 | 16.1 |
| 9xGnat-vs-Pirate | 100 | 34% | 13% | 0% | 53% | 0 | 154.0 | 89.3 | 11.4 |
| 9xGnat-vs-Stinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 9xGnat-vs-Wasp | 100 | 0% | 84% | 0% | 16% | 0 | 197.7 | - | 11.1 |
| 9xGnat-vs-Watcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |

## Pirate ship

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2xAegis-vs-Pirate | 100 | 88% | 8% | 1% | 3% | 0 | 92.8 | 87.1 | 14.0 |
| 2xBasilisk-vs-Pirate | 100 | 37% | 47% | 0% | 16% | 0 | 133.8 | 83.0 | 90.3 |
| 2xGnat-vs-Pirate | 100 | 0% | 56% | 0% | 44% | 0 | 68.2 | 93.5 | 9.3 |
| 2xHarrier-vs-Pirate | 100 | 29% | 70% | 0% | 1% | 0 | 102.2 | 92.3 | 29.0 |
| 2xHornet-vs-Pirate | 100 | 23% | 73% | 0% | 4% | 0 | 111.9 | 91.5 | 30.8 |
| 2xLancer-vs-Pirate | 100 | 66% | 33% | 0% | 1% | 0 | 104.3 | 90.9 | 21.7 |
| 2xLongbow L-vs-Pirate | 100 | 77% | 0% | 0% | 23% | 0 | 102.8 | 93.4 | 84.0 |
| 2xLongbow S-vs-Pirate | 100 | 43% | 0% | 0% | 57% | 0 | 185.3 | 94.9 | 26.5 |
| 2xMantis-vs-Pirate | 100 | 51% | 34% | 0% | 15% | 0 | 99.5 | 81.2 | 15.3 |
| 2xMidge-vs-Pirate | 100 | 1% | 68% | 0% | 31% | 0 | 118.0 | 55.1 | 9.0 |
| 2xStinger-vs-Pirate | 100 | 0% | 57% | 0% | 43% | 0 | 79.2 | - | 6.5 |
| 3xAegis-vs-Pirate | 100 | 99% | 1% | 0% | 0% | 0 | 76.6 | 87.5 | 14.1 |
| 3xBasilisk-vs-Pirate | 100 | 58% | 20% | 0% | 22% | 0 | 117.8 | 82.0 | 92.0 |
| 3xGnat-vs-Pirate | 100 | 0% | 54% | 0% | 46% | 0 | 98.7 | 89.7 | 8.8 |
| 3xHarrier-vs-Pirate | 100 | 66% | 31% | 0% | 3% | 0 | 105.5 | 91.6 | 33.6 |
| 3xHornet-vs-Pirate | 100 | 66% | 33% | 0% | 1% | 0 | 116.0 | 91.3 | 33.1 |
| 3xLancer-vs-Pirate | 100 | 93% | 5% | 0% | 2% | 0 | 76.8 | 89.7 | 21.6 |
| 3xLongbow L-vs-Pirate | 100 | 90% | 0% | 0% | 10% | 0 | 71.6 | 93.7 | - |
| 3xLongbow S-vs-Pirate | 100 | 78% | 0% | 0% | 22% | 0 | 160.2 | 96.1 | 29.0 |
| 3xMantis-vs-Pirate | 100 | 89% | 5% | 0% | 6% | 0 | 98.2 | 83.4 | 16.8 |
| 3xMidge-vs-Pirate | 100 | 8% | 30% | 0% | 62% | 0 | 147.8 | 49.4 | 9.2 |
| 3xStinger-vs-Pirate | 100 | 0% | 55% | 0% | 45% | 0 | 119.2 | - | 5.8 |
| Aegis-vs-Pirate | 100 | 36% | 62% | 0% | 2% | 0 | 92.1 | 84.9 | 12.6 |
| Basilisk-vs-Pirate | 100 | 14% | 72% | 0% | 14% | 0 | 64.8 | 80.7 | 81.0 |
| Gnat-vs-Pirate | 100 | 0% | 71% | 0% | 29% | 0 | 53.5 | 98.9 | 9.7 |
| Harrier-vs-Pirate | 100 | 1% | 99% | 0% | 0% | 0 | 73.2 | 94.0 | 23.5 |
| Hornet-vs-Pirate | 100 | 5% | 95% | 0% | 0% | 0 | 77.7 | 92.4 | 24.8 |
| Lancer-vs-Pirate | 100 | 7% | 93% | 0% | 0% | 0 | 77.5 | 93.0 | 18.7 |
| Longbow L-vs-Pirate | 100 | 50% | 0% | 0% | 50% | 0 | 162.2 | 95.8 | - |
| Longbow S-vs-Pirate | 100 | 10% | 0% | 0% | 90% | 0 | 222.2 | 93.7 | - |
| Mantis-vs-Pirate | 100 | 3% | 69% | 0% | 28% | 0 | 68.4 | 83.3 | 13.5 |
| Midge-vs-Pirate | 100 | 0% | 69% | 0% | 31% | 0 | 74.0 | 66.6 | 9.9 |
| Stinger-vs-Pirate | 100 | 0% | 54% | 0% | 46% | 0 | 52.3 | - | 9.5 |
| Wasp-vs-Pirate | 72 | 0% | 97% | 0% | 3% | 0 | 77.8 | 84.2 | 20.2 |

## Problems seen

No script exceptions.

Stuck or circling flags (speed < 1 m/s for 30 s, or no engagement within 1.5x weapon range for 60 s):

| model | ship-runs | flagged | share | kinds |
| :--- | ---: | ---: | ---: | ---: |
| Aegis | 7800 | 1416 | 18% | noengage 1412, slow 4 |
| Basilisk | 7800 | 2051 | 26% | noengage 1983, slow 68 |
| Gnat | 58500 | 52093 | 89% | slow 51999, noengage 94 |
| Harrier | 7800 | 1369 | 18% | noengage 1365, slow 4 |
| Hornet | 14100 | 2648 | 19% | noengage 2643, slow 5 |
| Lancer | 9600 | 1774 | 18% | noengage 1770, slow 4 |
| Longbow L | 7800 | 2446 | 31% | slow 2412, noengage 34 |
| Longbow S | 9600 | 3091 | 32% | slow 2982, noengage 109 |
| Mantis | 9600 | 2925 | 30% | slow 2909, noengage 16 |
| Midge | 11700 | 4344 | 37% | slow 3762, noengage 582 |
| Pirate | 3672 | 524 | 14% | noengage 524 |
| Stinger | 11700 | 11175 | 96% | slow 11175 |
| Wasp | 13572 | 3004 | 22% | noengage 2997, slow 7 |

Friendly fire occurred in 193 ship-runs; top scenarios:
- 1v3/Harrier-vs-3xWasp: 3 ships, 31088 damage
- 1v3/Hornet-vs-3xWasp: 2 ships, 20736 damage
- 2v3/2xHornet-vs-3xWasp: 2 ships, 20531 damage
- 1v2/Hornet-vs-2xWasp: 2 ships, 19420 damage
- 1v3/Mantis-vs-3xWasp: 2 ships, 16228 damage
- 1v2/Mantis-vs-2xLongbow L: 1 ships, 14360 damage
- 2v3/2xBasilisk-vs-3xWasp: 10 ships, 13850 damage
- pirate/3xMidge-vs-Pirate: 14 ships, 13120 damage

Collisions: Gnat 77, Midge 76, Longbow L 60, Basilisk 24, Wasp 17, Watcher 15, Hornet 9, Mantis 8, Lancer 6, Longbow S 3, Aegis 2, Pirate 1

## Cost

| kind | runs | avg wall ms | avg sim s | wall ms per sim s |
| :--- | ---: | ---: | ---: | ---: |
| 1v1 | 9100 | 260 | 156.6 | 1.7 |
| 1v2 | 15600 | 406 | 151.2 | 2.7 |
| 1v3 | 4300 | 478 | 174.4 | 2.7 |
| 2v3 | 4300 | 681 | 176.0 | 3.9 |
| mixed | 6200 | 649 | 164.9 | 3.9 |
| pirate | 3372 | 346 | 154.4 | 2.2 |
| swarm | 4200 | 2361 | 268.4 | 8.8 |

| script | total cpu ms | Main calls | ms per call | slowest call ms |
| :--- | ---: | ---: | ---: | ---: |
| GnatSwarmAI | 9477362 | 767173977 | 0.0124 | 201.00 |
| LongbowSniperAI | 2748728 | 193536658 | 0.0142 | 146.09 |
| HornetDroneAI | 2729854 | 182247505 | 0.0150 | 68.30 |
| WaspAI | 1411428 | 114385244 | 0.0123 | 40.83 |
| MantisAI | 1046759 | 85388441 | 0.0123 | 65.66 |
| LancerAI | 964018 | 76384444 | 0.0126 | 30.41 |
| BasiliskAI | 750953 | 64032948 | 0.0117 | 59.98 |
| HarrierAI | 731813 | 59505364 | 0.0123 | 99.70 |
| AegisAI | 674237 | 56271183 | 0.0120 | 95.66 |
| StingerAI | 132249 | 119397580 | 0.0011 | 41.51 |
| (brain) | 0 | 0 | 0.0000 | 0.00 |

