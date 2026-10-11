# Baseline v2.0 (pre-improvement)

47300 runs in 473 scenarios (1v1 9100, 1v2 15600, 1v3 4300, 2v3 4300, mixed 6200, pirate 3600, swarm 4200). Outcomes: mutual-kill 205, no-contest 100, timeout 22982, win 24013.

Total wall time of all runs 32947 s (0.70 s per run on average); settings hash fbe7bd2075bff0ea.

Command line: `run --seeds 100 --resume --out out/sim/results/baseline-v2.0`

## 1v1 matrix

Row model wins against column model (share of runs; the rest are draws, timeouts and losses). The diagonal (mirror fights) shows the share of runs with a decisive result.

|  | Aegis | Basilisk | Gnat | Harrier | Hornet | Lancer | Longbow L | Longbow S | Mantis | Midge | Stinger | Wasp | Watcher |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Aegis | 57% | 60% | 65% | 33% | 29% | 35% | 45% | 54% | 60% | 43% | 69% | 46% | 64% |
| Basilisk | 7% | 66% | 65% | 25% | 51% | 20% | 7% | 0% | 39% | 51% | 70% | 71% | 67% |
| Gnat | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% |
| Harrier | 23% | 38% | 71% | 53% | 23% | 31% | 50% | 54% | 48% | 47% | 64% | 57% | 60% |
| Hornet | 25% | 13% | 54% | 29% | 61% | 32% | 48% | 52% | 44% | 43% | 56% | 42% | 63% |
| Lancer | 28% | 42% | 64% | 19% | 26% | 63% | 58% | 51% | 41% | 48% | 66% | 48% | 48% |
| Longbow L | 9% | 44% | 54% | 12% | 12% | 10% | 45% | 22% | 17% | 12% | 37% | 20% | 22% |
| Longbow S | 14% | 19% | 59% | 9% | 9% | 13% | 37% | 55% | 26% | 10% | 59% | 41% | 39% |
| Mantis | 10% | 28% | 66% | 15% | 14% | 22% | 41% | 29% | 58% | 11% | 51% | 21% | 36% |
| Midge | 13% | 4% | 63% | 18% | 20% | 11% | 50% | 54% | 56% | 62% | 70% | 52% | 64% |
| Stinger | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% |
| Wasp | 14% | 1% | 68% | 15% | 17% | 17% | 44% | 27% | 47% | 11% | 73% | 59% | 69% |
| Watcher | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% |

### Ranking by 1v1 score (win 1, draw/timeout 0.5)

| model | score | sides | hits per shot | avg damage taken |
| :--- | ---: | ---: | ---: | ---: |
| Aegis | 66.4% | 1400 | 39.1% | 2308 |
| Harrier | 64.0% | 1400 | 37.5% | 2199 |
| Lancer | 62.4% | 1400 | 32.7% | 2070 |
| Hornet | 60.7% | 1400 | 24.9% | 2476 |
| Basilisk | 58.0% | 1400 | 28.9% | 19811 |
| Midge | 57.1% | 1400 | 34.4% | 1786 |
| Wasp | 50.2% | 1400 | 13.1% | 3022 |
| Longbow S | 49.7% | 1400 | 51.5% | 2252 |
| Mantis | 48.8% | 1400 | 63.7% | 2444 |
| Longbow L | 46.1% | 1400 | 65.9% | 10049 |
| Watcher | 31.0% | 1400 | - | 3598 |
| Stinger | 28.0% | 1400 | - | 1411 |
| Gnat | 27.5% | 1400 | - | 1241 |

## 1v2

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Aegis-vs-2xBasilisk | 100 | 0% | 66% | 0% | 34% | 0 | 76.0 | 81.7 | 20.1 |
| Aegis-vs-2xGnat | 100 | 69% | 0% | 0% | 31% | 0 | 215.7 | 19.0 | - |
| Aegis-vs-2xHarrier | 100 | 0% | 66% | 0% | 34% | 0 | 144.0 | 25.8 | 27.5 |
| Aegis-vs-2xHornet | 100 | 1% | 71% | 0% | 28% | 0 | 150.1 | 22.5 | 18.1 |
| Aegis-vs-2xLancer | 100 | 0% | 54% | 0% | 46% | 0 | 155.4 | 27.7 | 25.8 |
| Aegis-vs-2xLongbow L | 100 | 2% | 36% | 0% | 62% | 0 | 121.4 | 78.5 | 47.5 |
| Aegis-vs-2xLongbow S | 100 | 16% | 51% | 3% | 30% | 0 | 154.3 | 29.4 | 37.4 |
| Aegis-vs-2xMantis | 100 | 22% | 47% | 2% | 29% | 0 | 161.3 | 30.3 | 31.2 |
| Aegis-vs-2xMidge | 100 | 3% | 65% | 1% | 31% | 0 | 146.6 | 20.0 | 25.3 |
| Aegis-vs-2xStinger | 100 | 70% | 0% | 0% | 30% | 0 | 213.8 | 20.6 | - |
| Aegis-vs-2xWasp | 100 | 14% | 54% | 0% | 32% | 0 | 159.8 | 27.8 | 13.3 |
| Aegis-vs-2xWatcher | 100 | 36% | 0% | 0% | 64% | 0 | 226.6 | 23.6 | - |
| Basilisk-vs-2xAegis | 100 | 0% | 71% | 0% | 29% | 0 | 82.5 | 17.6 | 78.3 |
| Basilisk-vs-2xGnat | 100 | 51% | 0% | 0% | 49% | 0 | 216.8 | 6.6 | - |
| Basilisk-vs-2xHarrier | 100 | 0% | 68% | 0% | 32% | 0 | 85.8 | 18.3 | 82.4 |
| Basilisk-vs-2xHornet | 100 | 13% | 48% | 0% | 39% | 0 | 126.0 | 21.6 | 31.5 |
| Basilisk-vs-2xLancer | 100 | 1% | 70% | 0% | 29% | 0 | 88.8 | 17.6 | 80.2 |
| Basilisk-vs-2xLongbow L | 100 | 1% | 58% | 0% | 41% | 0 | 41.0 | 87.4 | 119.2 |
| Basilisk-vs-2xLongbow S | 100 | 0% | 49% | 0% | 51% | 0 | 197.7 | - | 97.7 |
| Basilisk-vs-2xMantis | 100 | 0% | 64% | 0% | 36% | 0 | 110.3 | 14.5 | 71.9 |
| Basilisk-vs-2xMidge | 100 | 3% | 45% | 0% | 52% | 0 | 205.2 | 12.7 | 94.2 |
| Basilisk-vs-2xStinger | 100 | 48% | 0% | 0% | 52% | 0 | 242.9 | 5.9 | - |
| Basilisk-vs-2xWasp | 100 | 38% | 15% | 2% | 45% | 0 | 110.2 | 28.2 | 20.7 |
| Basilisk-vs-2xWatcher | 100 | 36% | 0% | 0% | 64% | 0 | 232.8 | 15.0 | - |
| Gnat-vs-2xAegis | 100 | 0% | 70% | 0% | 30% | 0 | 170.2 | - | 20.5 |
| Gnat-vs-2xBasilisk | 100 | 0% | 66% | 0% | 34% | 0 | 180.2 | - | 7.5 |
| Gnat-vs-2xHarrier | 100 | 0% | 76% | 0% | 24% | 0 | 173.4 | - | 20.5 |
| Gnat-vs-2xHornet | 100 | 0% | 76% | 0% | 24% | 0 | 170.9 | - | 20.6 |
| Gnat-vs-2xLancer | 100 | 0% | 66% | 0% | 34% | 0 | 171.2 | - | 17.3 |
| Gnat-vs-2xLongbow L | 100 | 0% | 58% | 0% | 42% | 0 | 66.7 | - | 57.1 |
| Gnat-vs-2xLongbow S | 100 | 0% | 62% | 0% | 38% | 0 | 132.1 | - | 39.9 |
| Gnat-vs-2xMantis | 100 | 0% | 71% | 0% | 29% | 0 | 136.8 | - | 44.0 |
| Gnat-vs-2xMidge | 100 | 0% | 74% | 0% | 26% | 0 | 191.0 | - | 19.9 |
| Gnat-vs-2xStinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Gnat-vs-2xWasp | 100 | 0% | 68% | 0% | 32% | 0 | 163.3 | - | 33.0 |
| Gnat-vs-2xWatcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Harrier-vs-2xAegis | 100 | 0% | 69% | 0% | 31% | 0 | 145.3 | 28.5 | 24.8 |
| Harrier-vs-2xBasilisk | 100 | 0% | 65% | 0% | 35% | 0 | 78.5 | 72.2 | 17.1 |
| Harrier-vs-2xGnat | 100 | 65% | 0% | 0% | 35% | 0 | 218.8 | 19.5 | - |
| Harrier-vs-2xHornet | 100 | 5% | 64% | 0% | 31% | 0 | 150.7 | 22.9 | 12.8 |
| Harrier-vs-2xLancer | 100 | 0% | 67% | 0% | 33% | 0 | 142.0 | 27.9 | 24.0 |
| Harrier-vs-2xLongbow L | 100 | 0% | 38% | 0% | 62% | 0 | 79.6 | 80.3 | 46.9 |
| Harrier-vs-2xLongbow S | 100 | 21% | 40% | 0% | 39% | 0 | 175.7 | 32.1 | 31.8 |
| Harrier-vs-2xMantis | 100 | 15% | 53% | 1% | 31% | 0 | 155.2 | 32.3 | 24.2 |
| Harrier-vs-2xMidge | 100 | 0% | 71% | 0% | 29% | 0 | 145.3 | 22.4 | 26.1 |
| Harrier-vs-2xStinger | 100 | 57% | 0% | 0% | 43% | 0 | 221.3 | 17.1 | - |
| Harrier-vs-2xWasp | 100 | 18% | 44% | 2% | 36% | 0 | 161.1 | 27.0 | 4.6 |
| Harrier-vs-2xWatcher | 100 | 45% | 0% | 0% | 55% | 0 | 243.3 | 23.7 | - |
| Hornet-vs-2xAegis | 100 | 0% | 65% | 0% | 35% | 0 | 133.0 | 26.6 | 24.4 |
| Hornet-vs-2xBasilisk | 100 | 0% | 64% | 0% | 36% | 0 | 88.6 | 35.6 | 17.7 |
| Hornet-vs-2xGnat | 100 | 70% | 0% | 0% | 30% | 0 | 217.2 | 18.0 | - |
| Hornet-vs-2xHarrier | 100 | 0% | 67% | 0% | 33% | 0 | 144.3 | 26.5 | 26.6 |
| Hornet-vs-2xLancer | 100 | 0% | 74% | 0% | 26% | 0 | 143.6 | 27.1 | 22.5 |
| Hornet-vs-2xLongbow L | 100 | 2% | 33% | 0% | 65% | 0 | 134.8 | 30.1 | 49.0 |
| Hornet-vs-2xLongbow S | 100 | 21% | 48% | 0% | 31% | 0 | 165.2 | 25.4 | 35.9 |
| Hornet-vs-2xMantis | 100 | 16% | 53% | 0% | 31% | 0 | 148.3 | 27.6 | 25.1 |
| Hornet-vs-2xMidge | 100 | 0% | 63% | 0% | 37% | 0 | 144.2 | 22.4 | 26.4 |
| Hornet-vs-2xStinger | 100 | 62% | 0% | 0% | 38% | 0 | 217.1 | 14.5 | - |
| Hornet-vs-2xWasp | 100 | 9% | 60% | 1% | 30% | 0 | 151.7 | 19.7 | 6.8 |
| Hornet-vs-2xWatcher | 100 | 31% | 0% | 0% | 69% | 0 | 244.7 | 13.6 | - |
| Lancer-vs-2xAegis | 100 | 0% | 70% | 0% | 30% | 0 | 130.2 | 26.6 | 25.4 |
| Lancer-vs-2xBasilisk | 100 | 0% | 64% | 0% | 36% | 0 | 87.3 | 74.4 | 17.3 |
| Lancer-vs-2xGnat | 100 | 59% | 0% | 0% | 41% | 0 | 219.3 | 17.2 | - |
| Lancer-vs-2xHarrier | 100 | 0% | 70% | 0% | 30% | 0 | 142.3 | 24.0 | 27.9 |
| Lancer-vs-2xHornet | 100 | 5% | 62% | 0% | 33% | 0 | 146.0 | 23.9 | 14.5 |
| Lancer-vs-2xLongbow L | 100 | 0% | 34% | 0% | 66% | 0 | 95.8 | 79.8 | 43.9 |
| Lancer-vs-2xLongbow S | 100 | 17% | 41% | 0% | 42% | 0 | 162.2 | 32.1 | 33.2 |
| Lancer-vs-2xMantis | 100 | 17% | 51% | 0% | 32% | 0 | 164.0 | 33.1 | 27.9 |
| Lancer-vs-2xMidge | 100 | 1% | 61% | 0% | 38% | 0 | 158.9 | 20.1 | 26.5 |
| Lancer-vs-2xStinger | 100 | 57% | 0% | 0% | 43% | 0 | 243.5 | 17.6 | - |
| Lancer-vs-2xWasp | 100 | 10% | 58% | 2% | 30% | 0 | 150.2 | 24.1 | 9.6 |
| Lancer-vs-2xWatcher | 100 | 19% | 0% | 0% | 81% | 0 | 256.0 | 20.7 | - |
| Longbow L-vs-2xAegis | 100 | 0% | 70% | 0% | 30% | 0 | 91.4 | 34.6 | 73.2 |
| Longbow L-vs-2xBasilisk | 100 | 8% | 16% | 0% | 76% | 0 | 131.2 | 103.6 | 73.8 |
| Longbow L-vs-2xGnat | 100 | 6% | 0% | 0% | 94% | 0 | 131.5 | 59.7 | - |
| Longbow L-vs-2xHarrier | 100 | 0% | 60% | 0% | 40% | 0 | 85.4 | 35.6 | 77.3 |
| Longbow L-vs-2xHornet | 100 | 2% | 69% | 0% | 29% | 0 | 108.7 | 44.2 | 28.6 |
| Longbow L-vs-2xLancer | 100 | 0% | 63% | 0% | 37% | 0 | 102.8 | 32.2 | 77.1 |
| Longbow L-vs-2xLongbow S | 100 | 0% | 59% | 0% | 41% | 0 | 113.8 | 68.7 | 89.4 |
| Longbow L-vs-2xMantis | 100 | 0% | 72% | 0% | 28% | 0 | 99.1 | 55.1 | 69.6 |
| Longbow L-vs-2xMidge | 100 | 0% | 55% | 0% | 45% | 0 | 138.5 | 48.9 | 72.5 |
| Longbow L-vs-2xStinger | 100 | 7% | 0% | 0% | 93% | 0 | 253.7 | 60.3 | - |
| Longbow L-vs-2xWasp | 100 | 1% | 61% | 0% | 38% | 0 | 94.0 | 43.3 | 41.6 |
| Longbow L-vs-2xWatcher | 100 | 1% | 0% | 0% | 99% | 0 | 254.0 | 91.6 | - |
| Longbow S-vs-2xAegis | 100 | 0% | 68% | 0% | 32% | 0 | 150.1 | 28.9 | 30.8 |
| Longbow S-vs-2xBasilisk | 100 | 0% | 28% | 0% | 72% | 0 | 215.5 | 98.8 | 20.9 |
| Longbow S-vs-2xGnat | 100 | 22% | 0% | 0% | 78% | 0 | 219.9 | 50.7 | - |
| Longbow S-vs-2xHarrier | 100 | 0% | 62% | 0% | 38% | 0 | 131.9 | 25.7 | 30.9 |
| Longbow S-vs-2xHornet | 100 | 0% | 72% | 0% | 28% | 0 | 138.2 | 29.1 | 15.8 |
| Longbow S-vs-2xLancer | 100 | 0% | 65% | 0% | 35% | 0 | 145.8 | 28.3 | 30.2 |
| Longbow S-vs-2xLongbow L | 100 | 1% | 48% | 0% | 51% | 0 | 129.0 | 93.0 | 56.7 |
| Longbow S-vs-2xMantis | 100 | 1% | 59% | 0% | 40% | 0 | 118.4 | 38.9 | 29.8 |
| Longbow S-vs-2xMidge | 100 | 0% | 67% | 0% | 33% | 0 | 137.8 | 21.5 | 32.0 |
| Longbow S-vs-2xStinger | 100 | 10% | 0% | 0% | 90% | 0 | 212.0 | 49.2 | - |
| Longbow S-vs-2xWasp | 100 | 3% | 59% | 1% | 37% | 0 | 168.5 | 42.0 | 4.1 |
| Longbow S-vs-2xWatcher | 100 | 1% | 0% | 0% | 99% | 0 | 248.3 | 78.9 | - |
| Mantis-vs-2xAegis | 100 | 0% | 65% | 0% | 35% | 0 | 138.2 | 21.3 | 32.1 |
| Mantis-vs-2xBasilisk | 100 | 0% | 69% | 0% | 31% | 0 | 81.8 | 62.8 | 17.2 |
| Mantis-vs-2xGnat | 100 | 16% | 0% | 0% | 84% | 0 | 210.4 | 41.9 | - |
| Mantis-vs-2xHarrier | 100 | 0% | 71% | 0% | 29% | 0 | 126.2 | 25.2 | 32.2 |
| Mantis-vs-2xHornet | 100 | 0% | 65% | 0% | 35% | 0 | 139.3 | 21.9 | 20.6 |
| Mantis-vs-2xLancer | 100 | 0% | 69% | 0% | 31% | 0 | 137.0 | 25.5 | 31.1 |
| Mantis-vs-2xLongbow L | 100 | 1% | 66% | 0% | 33% | 0 | 128.7 | 74.7 | 56.6 |
| Mantis-vs-2xLongbow S | 100 | 0% | 59% | 0% | 41% | 0 | 125.7 | 12.7 | 33.6 |
| Mantis-vs-2xMidge | 100 | 0% | 62% | 0% | 38% | 0 | 134.7 | 17.6 | 32.8 |
| Mantis-vs-2xStinger | 100 | 7% | 0% | 0% | 93% | 0 | 222.0 | 27.8 | - |
| Mantis-vs-2xWasp | 100 | 2% | 71% | 0% | 27% | 0 | 156.3 | 25.5 | 8.4 |
| Mantis-vs-2xWatcher | 100 | 9% | 0% | 0% | 91% | 0 | 257.3 | 24.1 | - |
| Midge-vs-2xAegis | 100 | 0% | 66% | 0% | 34% | 0 | 140.2 | 27.1 | 18.9 |
| Midge-vs-2xBasilisk | 100 | 0% | 71% | 0% | 29% | 0 | 145.8 | 98.6 | 10.9 |
| Midge-vs-2xGnat | 100 | 61% | 0% | 0% | 39% | 0 | 216.3 | 18.4 | - |
| Midge-vs-2xHarrier | 100 | 0% | 67% | 0% | 33% | 0 | 141.3 | 25.3 | 19.8 |
| Midge-vs-2xHornet | 100 | 0% | 66% | 0% | 34% | 0 | 148.8 | 25.1 | 16.8 |
| Midge-vs-2xLancer | 100 | 0% | 67% | 0% | 33% | 0 | 138.0 | 24.1 | 17.6 |
| Midge-vs-2xLongbow L | 100 | 22% | 35% | 2% | 41% | 0 | 152.7 | 89.3 | 50.1 |
| Midge-vs-2xLongbow S | 100 | 25% | 39% | 0% | 36% | 0 | 199.8 | 31.6 | 36.2 |
| Midge-vs-2xMantis | 100 | 21% | 45% | 0% | 34% | 0 | 168.6 | 33.0 | 28.1 |
| Midge-vs-2xStinger | 100 | 59% | 0% | 0% | 41% | 0 | 244.3 | 20.6 | - |
| Midge-vs-2xWasp | 100 | 17% | 52% | 0% | 31% | 0 | 161.5 | 28.6 | 5.6 |
| Midge-vs-2xWatcher | 100 | 42% | 0% | 0% | 58% | 0 | 255.6 | 25.8 | - |
| Stinger-vs-2xAegis | 100 | 0% | 61% | 0% | 39% | 0 | 174.8 | - | 21.9 |
| Stinger-vs-2xBasilisk | 100 | 0% | 67% | 0% | 33% | 0 | 171.8 | - | 7.4 |
| Stinger-vs-2xGnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Stinger-vs-2xHarrier | 100 | 0% | 67% | 0% | 33% | 0 | 165.5 | - | 22.0 |
| Stinger-vs-2xHornet | 100 | 0% | 70% | 0% | 30% | 0 | 174.6 | - | 21.2 |
| Stinger-vs-2xLancer | 100 | 0% | 64% | 0% | 36% | 0 | 193.4 | - | 19.6 |
| Stinger-vs-2xLongbow L | 100 | 0% | 65% | 0% | 35% | 0 | 125.3 | - | 48.7 |
| Stinger-vs-2xLongbow S | 100 | 0% | 67% | 0% | 33% | 0 | 153.3 | - | 34.0 |
| Stinger-vs-2xMantis | 100 | 0% | 70% | 0% | 30% | 0 | 134.3 | - | 37.7 |
| Stinger-vs-2xMidge | 100 | 0% | 65% | 0% | 35% | 0 | 185.8 | - | 22.1 |
| Stinger-vs-2xWasp | 100 | 0% | 63% | 0% | 37% | 0 | 181.2 | - | 38.6 |
| Stinger-vs-2xWatcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Wasp-vs-2xAegis | 100 | 2% | 68% | 0% | 30% | 0 | 145.8 | 8.5 | 25.7 |
| Wasp-vs-2xBasilisk | 100 | 0% | 76% | 0% | 24% | 0 | 80.2 | 9.6 | 18.3 |
| Wasp-vs-2xGnat | 100 | 60% | 0% | 0% | 40% | 0 | 186.5 | 26.2 | - |
| Wasp-vs-2xHarrier | 100 | 0% | 65% | 0% | 35% | 0 | 151.0 | 4.7 | 27.5 |
| Wasp-vs-2xHornet | 100 | 0% | 64% | 0% | 36% | 0 | 144.0 | 1.9 | 17.7 |
| Wasp-vs-2xLancer | 100 | 0% | 69% | 1% | 30% | 0 | 149.3 | 5.5 | 24.4 |
| Wasp-vs-2xLongbow L | 100 | 0% | 35% | 0% | 65% | 0 | 81.7 | 54.9 | 47.7 |
| Wasp-vs-2xLongbow S | 100 | 7% | 49% | 1% | 43% | 0 | 169.3 | 10.9 | 40.2 |
| Wasp-vs-2xMantis | 100 | 7% | 50% | 0% | 43% | 0 | 156.5 | 6.1 | 27.7 |
| Wasp-vs-2xMidge | 100 | 0% | 59% | 0% | 41% | 0 | 146.7 | 2.3 | 25.5 |
| Wasp-vs-2xStinger | 100 | 67% | 0% | 0% | 33% | 0 | 214.7 | 32.6 | - |
| Wasp-vs-2xWatcher | 100 | 71% | 0% | 0% | 29% | 0 | 185.2 | 99.8 | - |
| Watcher-vs-2xAegis | 100 | 0% | 73% | 0% | 27% | 0 | 203.8 | - | 22.8 |
| Watcher-vs-2xBasilisk | 100 | 0% | 71% | 0% | 29% | 0 | 207.3 | - | 15.3 |
| Watcher-vs-2xGnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher-vs-2xHarrier | 100 | 0% | 74% | 0% | 26% | 0 | 187.9 | - | 24.4 |
| Watcher-vs-2xHornet | 100 | 0% | 66% | 0% | 34% | 0 | 185.1 | - | 15.7 |
| Watcher-vs-2xLancer | 100 | 0% | 63% | 0% | 37% | 0 | 189.8 | - | 14.9 |
| Watcher-vs-2xLongbow L | 100 | 0% | 33% | 0% | 67% | 0 | 141.5 | - | 67.7 |
| Watcher-vs-2xLongbow S | 100 | 0% | 59% | 0% | 41% | 0 | 164.3 | - | 58.5 |
| Watcher-vs-2xMantis | 100 | 0% | 53% | 0% | 47% | 0 | 174.8 | - | 27.1 |
| Watcher-vs-2xMidge | 100 | 0% | 67% | 0% | 33% | 0 | 189.3 | - | 21.8 |
| Watcher-vs-2xStinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher-vs-2xWasp | 100 | 0% | 64% | 0% | 36% | 0 | 181.9 | - | 101.0 |

## 1v3

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Aegis-vs-3xGnat | 100 | 50% | 0% | 0% | 50% | 0 | 245.2 | 18.7 | - |
| Aegis-vs-3xHornet | 100 | 1% | 57% | 0% | 42% | 0 | 142.8 | 20.2 | 10.9 |
| Aegis-vs-3xMidge | 100 | 0% | 69% | 0% | 31% | 0 | 147.3 | 22.5 | 25.1 |
| Aegis-vs-3xStinger | 100 | 41% | 0% | 0% | 59% | 0 | 246.5 | 19.5 | - |
| Aegis-vs-3xWasp | 100 | 2% | 63% | 1% | 34% | 0 | 160.5 | 27.4 | 8.7 |
| Basilisk-vs-3xGnat | 100 | 36% | 0% | 0% | 64% | 0 | 238.4 | 6.7 | - |
| Basilisk-vs-3xHornet | 100 | 2% | 69% | 0% | 29% | 0 | 89.2 | 20.8 | 32.5 |
| Basilisk-vs-3xMidge | 100 | 0% | 64% | 0% | 36% | 0 | 154.6 | 12.6 | 93.3 |
| Basilisk-vs-3xStinger | 100 | 13% | 0% | 0% | 87% | 0 | 277.0 | 5.9 | - |
| Basilisk-vs-3xWasp | 100 | 26% | 22% | 0% | 52% | 0 | 117.5 | 30.5 | 24.8 |
| Harrier-vs-3xGnat | 100 | 54% | 0% | 0% | 46% | 0 | 217.3 | 18.9 | - |
| Harrier-vs-3xHornet | 100 | 2% | 68% | 0% | 30% | 0 | 145.6 | 25.2 | 10.6 |
| Harrier-vs-3xMidge | 100 | 0% | 74% | 0% | 26% | 0 | 148.0 | 22.8 | 23.2 |
| Harrier-vs-3xStinger | 100 | 45% | 0% | 0% | 55% | 0 | 244.0 | 18.9 | - |
| Harrier-vs-3xWasp | 100 | 1% | 66% | 0% | 33% | 0 | 165.5 | 27.9 | 9.0 |
| Hornet-vs-3xGnat | 100 | 57% | 0% | 0% | 43% | 0 | 221.2 | 15.7 | - |
| Hornet-vs-3xMidge | 100 | 0% | 67% | 0% | 33% | 0 | 144.0 | 21.8 | 24.5 |
| Hornet-vs-3xStinger | 100 | 39% | 0% | 0% | 61% | 0 | 245.2 | 17.5 | - |
| Hornet-vs-3xWasp | 100 | 5% | 64% | 1% | 30% | 0 | 158.3 | 18.2 | 6.7 |
| Lancer-vs-3xGnat | 100 | 43% | 0% | 0% | 57% | 0 | 252.0 | 16.3 | - |
| Lancer-vs-3xHornet | 100 | 0% | 66% | 1% | 33% | 0 | 141.0 | 22.2 | 13.7 |
| Lancer-vs-3xMidge | 100 | 0% | 76% | 0% | 24% | 0 | 143.3 | 19.9 | 24.4 |
| Lancer-vs-3xStinger | 100 | 29% | 0% | 0% | 71% | 0 | 260.7 | 18.5 | - |
| Lancer-vs-3xWasp | 100 | 1% | 63% | 2% | 34% | 0 | 147.7 | 25.1 | 7.8 |
| Longbow L-vs-3xGnat | 100 | 1% | 2% | 0% | 97% | 0 | 214.0 | 59.4 | 60.1 |
| Longbow L-vs-3xHornet | 100 | 0% | 75% | 0% | 25% | 0 | 93.3 | 30.6 | 28.8 |
| Longbow L-vs-3xMidge | 100 | 0% | 61% | 0% | 39% | 0 | 121.5 | 45.8 | 69.4 |
| Longbow L-vs-3xStinger | 100 | 0% | 0% | 0% | 100% | 0 | - | 58.8 | - |
| Longbow L-vs-3xWasp | 100 | 0% | 62% | 0% | 38% | 0 | 70.8 | 46.3 | 41.6 |
| Longbow S-vs-3xGnat | 100 | 3% | 0% | 0% | 97% | 0 | 287.5 | 49.4 | - |
| Longbow S-vs-3xHornet | 100 | 0% | 68% | 0% | 32% | 0 | 144.0 | 25.4 | 11.5 |
| Longbow S-vs-3xMidge | 100 | 0% | 73% | 0% | 27% | 0 | 134.3 | 28.2 | 31.0 |
| Longbow S-vs-3xStinger | 100 | 1% | 0% | 0% | 99% | 0 | 258.5 | 54.7 | - |
| Longbow S-vs-3xWasp | 100 | 0% | 70% | 0% | 30% | 0 | 159.3 | 44.4 | 5.0 |
| Mantis-vs-3xGnat | 100 | 1% | 0% | 0% | 99% | 0 | 169.5 | 19.7 | - |
| Mantis-vs-3xHornet | 100 | 0% | 69% | 0% | 31% | 0 | 146.5 | 33.0 | 10.7 |
| Mantis-vs-3xMidge | 100 | 0% | 66% | 0% | 34% | 0 | 138.0 | 19.6 | 32.4 |
| Mantis-vs-3xStinger | 100 | 1% | 0% | 0% | 99% | 0 | 238.3 | 18.4 | - |
| Mantis-vs-3xWasp | 100 | 0% | 62% | 0% | 38% | 0 | 142.9 | 24.1 | 7.4 |
| Wasp-vs-3xGnat | 100 | 62% | 0% | 0% | 38% | 0 | 214.6 | 21.8 | - |
| Wasp-vs-3xHornet | 100 | 0% | 74% | 0% | 26% | 0 | 144.7 | 3.7 | 8.8 |
| Wasp-vs-3xMidge | 100 | 0% | 61% | 0% | 39% | 0 | 158.2 | 5.7 | 23.8 |
| Wasp-vs-3xStinger | 100 | 73% | 0% | 0% | 27% | 0 | 217.8 | 33.5 | - |

## 2v3

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2xAegis-vs-3xGnat | 100 | 63% | 0% | 0% | 37% | 0 | 216.5 | 16.4 | - |
| 2xAegis-vs-3xHornet | 100 | 18% | 40% | 1% | 41% | 0 | 169.7 | 22.2 | 11.4 |
| 2xAegis-vs-3xMidge | 100 | 14% | 52% | 3% | 31% | 0 | 188.8 | 20.2 | 25.6 |
| 2xAegis-vs-3xStinger | 100 | 59% | 0% | 0% | 41% | 0 | 213.5 | 16.5 | - |
| 2xAegis-vs-3xWasp | 100 | 24% | 44% | 0% | 32% | 0 | 174.1 | 23.4 | 11.1 |
| 2xBasilisk-vs-3xGnat | 100 | 51% | 0% | 0% | 49% | 0 | 219.7 | 6.1 | 90.5 |
| 2xBasilisk-vs-3xHornet | 100 | 34% | 3% | 0% | 63% | 0 | 107.7 | 21.4 | 27.5 |
| 2xBasilisk-vs-3xMidge | 100 | 21% | 20% | 0% | 59% | 0 | 199.7 | 10.7 | 90.3 |
| 2xBasilisk-vs-3xStinger | 100 | 51% | 0% | 0% | 49% | 0 | 231.2 | 5.7 | - |
| 2xBasilisk-vs-3xWasp | 100 | 66% | 1% | 0% | 33% | 0 | 88.5 | 30.0 | 36.4 |
| 2xHarrier-vs-3xGnat | 100 | 59% | 0% | 0% | 41% | 0 | 215.0 | 16.5 | - |
| 2xHarrier-vs-3xHornet | 100 | 19% | 48% | 1% | 32% | 0 | 186.3 | 23.0 | 8.6 |
| 2xHarrier-vs-3xMidge | 100 | 8% | 52% | 0% | 40% | 0 | 164.0 | 20.3 | 22.7 |
| 2xHarrier-vs-3xStinger | 100 | 59% | 0% | 0% | 41% | 0 | 188.5 | 18.7 | - |
| 2xHarrier-vs-3xWasp | 100 | 15% | 46% | 0% | 39% | 0 | 163.2 | 27.4 | 11.2 |
| 2xHornet-vs-3xGnat | 100 | 60% | 0% | 0% | 40% | 0 | 243.2 | 10.3 | - |
| 2xHornet-vs-3xMidge | 100 | 4% | 60% | 3% | 33% | 0 | 186.2 | 13.9 | 22.7 |
| 2xHornet-vs-3xStinger | 100 | 55% | 0% | 0% | 45% | 0 | 242.7 | 8.6 | - |
| 2xHornet-vs-3xWasp | 100 | 15% | 37% | 0% | 48% | 0 | 178.1 | 10.0 | 6.2 |
| 2xLancer-vs-3xGnat | 100 | 63% | 0% | 0% | 37% | 0 | 219.7 | 15.5 | - |
| 2xLancer-vs-3xHornet | 100 | 10% | 44% | 1% | 45% | 0 | 187.5 | 23.0 | 9.0 |
| 2xLancer-vs-3xMidge | 100 | 2% | 64% | 0% | 34% | 0 | 184.8 | 20.1 | 23.9 |
| 2xLancer-vs-3xStinger | 100 | 51% | 0% | 0% | 49% | 0 | 216.7 | 17.2 | - |
| 2xLancer-vs-3xWasp | 100 | 25% | 47% | 0% | 28% | 0 | 186.1 | 25.5 | 9.9 |
| 2xLongbow L-vs-3xGnat | 100 | 3% | 0% | 0% | 97% | 0 | 192.3 | 54.2 | 53.8 |
| 2xLongbow L-vs-3xHornet | 100 | 0% | 19% | 0% | 81% | 0 | 227.2 | 45.2 | 27.8 |
| 2xLongbow L-vs-3xMidge | 100 | 0% | 27% | 0% | 73% | 0 | 182.2 | 56.4 | 88.9 |
| 2xLongbow L-vs-3xStinger | 100 | 0% | 0% | 0% | 100% | 0 | - | 66.2 | - |
| 2xLongbow L-vs-3xWasp | 100 | 1% | 8% | 0% | 91% | 0 | 130.0 | 56.2 | 62.3 |
| 2xLongbow S-vs-3xGnat | 100 | 14% | 0% | 0% | 86% | 0 | 250.2 | 46.8 | - |
| 2xLongbow S-vs-3xHornet | 100 | 0% | 59% | 0% | 41% | 0 | 163.3 | 49.7 | 10.8 |
| 2xLongbow S-vs-3xMidge | 100 | 0% | 64% | 0% | 36% | 0 | 163.7 | 42.8 | 27.0 |
| 2xLongbow S-vs-3xStinger | 100 | 7% | 0% | 0% | 93% | 0 | 279.7 | 56.3 | - |
| 2xLongbow S-vs-3xWasp | 100 | 0% | 38% | 0% | 62% | 0 | 208.4 | 59.9 | 8.3 |
| 2xMantis-vs-3xGnat | 100 | 8% | 0% | 0% | 92% | 0 | 264.3 | 24.0 | - |
| 2xMantis-vs-3xHornet | 100 | 1% | 53% | 0% | 46% | 0 | 186.7 | 22.7 | 10.6 |
| 2xMantis-vs-3xMidge | 100 | 0% | 63% | 0% | 37% | 0 | 185.3 | 17.4 | 27.5 |
| 2xMantis-vs-3xStinger | 100 | 8% | 0% | 0% | 92% | 0 | 246.8 | 15.1 | - |
| 2xMantis-vs-3xWasp | 100 | 3% | 59% | 1% | 37% | 0 | 185.3 | 19.6 | 12.4 |
| 2xWasp-vs-3xGnat | 100 | 68% | 0% | 0% | 32% | 0 | 214.9 | 18.4 | - |
| 2xWasp-vs-3xHornet | 100 | 7% | 50% | 0% | 43% | 0 | 161.8 | 8.2 | 6.5 |
| 2xWasp-vs-3xMidge | 100 | 4% | 59% | 0% | 37% | 0 | 164.0 | 8.6 | 22.2 |
| 2xWasp-vs-3xStinger | 100 | 68% | 0% | 0% | 32% | 0 | 214.9 | 19.0 | - |

## Mixed teams

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Hornet+Longbow S-vs-Aegis | 100 | 65% | 3% | 1% | 31% | 0 | 167.4 | 28.8 | 30.0 |
| Hornet+Longbow S-vs-Basilisk | 100 | 39% | 0% | 0% | 61% | 0 | 163.7 | 27.4 | 13.5 |
| Hornet+Longbow S-vs-Gnat | 100 | 67% | 0% | 0% | 33% | 0 | 148.0 | 22.0 | - |
| Hornet+Longbow S-vs-Harrier | 100 | 55% | 6% | 1% | 38% | 0 | 160.0 | 22.6 | 28.4 |
| Hornet+Longbow S-vs-Hornet | 100 | 56% | 4% | 2% | 38% | 0 | 152.7 | 26.5 | 28.0 |
| Hornet+Longbow S-vs-Hornet+Longbow S | 100 | 31% | 26% | 0% | 43% | 0 | 204.7 | 27.1 | 25.0 |
| Hornet+Longbow S-vs-Lancer | 100 | 63% | 8% | 0% | 29% | 0 | 154.8 | 25.9 | 29.8 |
| Hornet+Longbow S-vs-Lancer+Mantis | 100 | 34% | 29% | 0% | 37% | 0 | 204.7 | 26.5 | 26.1 |
| Hornet+Longbow S-vs-Longbow L | 100 | 58% | 0% | 0% | 42% | 0 | 107.2 | 31.7 | 59.1 |
| Hornet+Longbow S-vs-Longbow S | 100 | 63% | 0% | 0% | 37% | 0 | 141.0 | 32.1 | 34.6 |
| Hornet+Longbow S-vs-Mantis | 100 | 62% | 0% | 0% | 38% | 0 | 154.7 | 28.5 | 26.9 |
| Hornet+Longbow S-vs-Midge | 100 | 60% | 1% | 0% | 39% | 0 | 150.7 | 21.3 | 31.6 |
| Hornet+Longbow S-vs-Stinger | 100 | 58% | 0% | 0% | 42% | 0 | 141.0 | 23.7 | - |
| Hornet+Longbow S-vs-Wasp | 100 | 72% | 3% | 0% | 25% | 0 | 154.7 | 20.0 | 6.7 |
| Hornet+Longbow S-vs-Wasp+2xGnat | 100 | 23% | 4% | 0% | 73% | 0 | 244.2 | 18.1 | 8.8 |
| Hornet+Longbow S-vs-Watcher | 100 | 65% | 0% | 0% | 35% | 0 | 170.3 | 17.1 | - |
| Lancer+Mantis-vs-Aegis | 100 | 59% | 4% | 0% | 37% | 0 | 167.7 | 26.3 | 30.9 |
| Lancer+Mantis-vs-Basilisk | 100 | 52% | 3% | 0% | 45% | 0 | 128.8 | 77.1 | 16.7 |
| Lancer+Mantis-vs-Gnat | 100 | 71% | 0% | 0% | 29% | 0 | 141.3 | 26.5 | - |
| Lancer+Mantis-vs-Harrier | 100 | 68% | 5% | 0% | 27% | 0 | 141.0 | 26.8 | 31.1 |
| Lancer+Mantis-vs-Hornet | 100 | 55% | 2% | 0% | 43% | 0 | 149.2 | 22.8 | 23.1 |
| Lancer+Mantis-vs-Lancer | 100 | 58% | 7% | 0% | 35% | 0 | 147.8 | 26.3 | 28.0 |
| Lancer+Mantis-vs-Lancer+Mantis | 100 | 32% | 23% | 0% | 45% | 0 | 199.7 | 27.4 | 24.6 |
| Lancer+Mantis-vs-Longbow L | 100 | 60% | 0% | 0% | 40% | 0 | 96.8 | 57.4 | 48.5 |
| Lancer+Mantis-vs-Longbow S | 100 | 65% | 1% | 0% | 34% | 0 | 133.2 | 30.9 | 32.4 |
| Lancer+Mantis-vs-Mantis | 100 | 68% | 1% | 1% | 30% | 0 | 142.3 | 33.1 | 21.3 |
| Lancer+Mantis-vs-Midge | 100 | 62% | 1% | 0% | 37% | 0 | 146.7 | 20.3 | 30.1 |
| Lancer+Mantis-vs-Stinger | 100 | 69% | 0% | 0% | 31% | 0 | 152.3 | 24.6 | - |
| Lancer+Mantis-vs-Wasp | 100 | 56% | 3% | 0% | 41% | 0 | 163.8 | 26.0 | 5.6 |
| Lancer+Mantis-vs-Watcher | 100 | 63% | 0% | 0% | 37% | 0 | 165.2 | 17.1 | - |
| Wasp+2xGnat-vs-Aegis | 100 | 24% | 16% | 0% | 60% | 0 | 167.2 | 6.6 | 19.3 |
| Wasp+2xGnat-vs-Basilisk | 100 | 2% | 2% | 0% | 96% | 0 | 155.0 | 12.2 | 15.5 |
| Wasp+2xGnat-vs-Gnat | 100 | 56% | 0% | 0% | 44% | 0 | 173.4 | 32.3 | - |
| Wasp+2xGnat-vs-Harrier | 100 | 16% | 21% | 0% | 63% | 0 | 191.0 | 2.0 | 22.0 |
| Wasp+2xGnat-vs-Hornet | 100 | 23% | 24% | 0% | 53% | 0 | 205.0 | 5.7 | 20.6 |
| Wasp+2xGnat-vs-Lancer | 100 | 12% | 16% | 0% | 72% | 0 | 186.1 | 1.9 | 21.0 |
| Wasp+2xGnat-vs-Lancer+Mantis | 100 | 5% | 19% | 0% | 76% | 0 | 250.2 | 5.2 | 19.3 |
| Wasp+2xGnat-vs-Longbow L | 100 | 45% | 0% | 0% | 55% | 0 | 132.2 | 34.6 | 51.6 |
| Wasp+2xGnat-vs-Longbow S | 100 | 41% | 0% | 0% | 59% | 0 | 186.2 | 4.4 | 46.0 |
| Wasp+2xGnat-vs-Mantis | 100 | 50% | 0% | 0% | 50% | 0 | 177.3 | 7.0 | 29.9 |
| Wasp+2xGnat-vs-Midge | 100 | 23% | 23% | 0% | 54% | 0 | 202.1 | 4.9 | 23.8 |
| Wasp+2xGnat-vs-Stinger | 100 | 70% | 0% | 0% | 30% | 0 | 199.8 | 46.2 | - |
| Wasp+2xGnat-vs-Wasp | 100 | 40% | 11% | 0% | 49% | 0 | 171.8 | 7.0 | 6.9 |
| Wasp+2xGnat-vs-Wasp+2xGnat | 100 | 10% | 12% | 0% | 78% | 0 | 208.8 | 8.1 | 11.7 |
| Wasp+2xGnat-vs-Watcher | 100 | 70% | 0% | 0% | 30% | 0 | 179.0 | 134.5 | - |
| Watcher+3xGnat-vs-Aegis | 100 | 0% | 29% | 0% | 71% | 0 | 245.8 | 0.0 | 20.9 |
| Watcher+3xGnat-vs-Basilisk | 100 | 0% | 21% | 0% | 79% | 0 | 216.8 | - | 7.9 |
| Watcher+3xGnat-vs-Gnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher+3xGnat-vs-Harrier | 100 | 0% | 38% | 0% | 62% | 0 | 247.4 | - | 22.0 |
| Watcher+3xGnat-vs-Hornet | 100 | 0% | 30% | 0% | 70% | 0 | 245.8 | - | 16.2 |
| Watcher+3xGnat-vs-Hornet+Longbow S | 100 | 0% | 34% | 0% | 66% | 0 | 246.8 | - | 17.8 |
| Watcher+3xGnat-vs-Lancer | 100 | 0% | 19% | 0% | 81% | 0 | 258.0 | - | 19.2 |
| Watcher+3xGnat-vs-Lancer+Mantis | 100 | 0% | 28% | 0% | 72% | 0 | 245.2 | 5.6 | 19.4 |
| Watcher+3xGnat-vs-Longbow L | 100 | 0% | 1% | 0% | 99% | 0 | 198.3 | - | 73.0 |
| Watcher+3xGnat-vs-Longbow S | 100 | 0% | 3% | 0% | 97% | 0 | 254.7 | - | 61.3 |
| Watcher+3xGnat-vs-Mantis | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 23.6 |
| Watcher+3xGnat-vs-Midge | 100 | 0% | 29% | 0% | 71% | 0 | 245.2 | - | 20.3 |
| Watcher+3xGnat-vs-Stinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher+3xGnat-vs-Wasp | 100 | 0% | 56% | 0% | 44% | 0 | 245.2 | - | 34.2 |
| Watcher+3xGnat-vs-Wasp+2xGnat | 100 | 0% | 52% | 0% | 48% | 0 | 216.9 | - | 33.9 |
| Watcher+3xGnat-vs-Watcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher+3xGnat-vs-Watcher+3xGnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |

## Hive swarms (Gnats)

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 12xGnat-vs-Aegis | 100 | 3% | 0% | 0% | 97% | 0 | 279.3 | 5.6 | 18.5 |
| 12xGnat-vs-Basilisk | 100 | 4% | 0% | 0% | 96% | 0 | 170.4 | 31.4 | 9.2 |
| 12xGnat-vs-Gnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 12xGnat-vs-Harrier | 100 | 3% | 0% | 0% | 97% | 0 | 262.8 | 7.9 | 19.2 |
| 12xGnat-vs-Hornet | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 14.3 |
| 12xGnat-vs-Lancer | 100 | 1% | 0% | 0% | 99% | 0 | 278.5 | 2.1 | 17.9 |
| 12xGnat-vs-Longbow L | 100 | 8% | 0% | 0% | 92% | 0 | 221.2 | 41.3 | 61.3 |
| 12xGnat-vs-Longbow S | 100 | 3% | 0% | 0% | 97% | 0 | 251.5 | 1.9 | 59.2 |
| 12xGnat-vs-Mantis | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 18.9 |
| 12xGnat-vs-Midge | 100 | 1% | 0% | 0% | 99% | 0 | 172.3 | 0.9 | 19.2 |
| 12xGnat-vs-Pirate | 100 | 31% | 2% | 0% | 67% | 0 | 154.8 | 45.4 | 8.3 |
| 12xGnat-vs-Stinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 12xGnat-vs-Wasp | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 21.6 |
| 12xGnat-vs-Watcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 6xGnat-vs-Aegis | 100 | 0% | 5% | 0% | 95% | 0 | 274.7 | 2.5 | 18.4 |
| 6xGnat-vs-Basilisk | 100 | 1% | 3% | 0% | 96% | 0 | 278.2 | 77.9 | 7.6 |
| 6xGnat-vs-Gnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 6xGnat-vs-Harrier | 100 | 0% | 7% | 0% | 93% | 0 | 249.3 | - | 19.1 |
| 6xGnat-vs-Hornet | 100 | 0% | 8% | 0% | 92% | 0 | 278.3 | - | 15.8 |
| 6xGnat-vs-Lancer | 100 | 0% | 1% | 0% | 99% | 0 | 292.5 | 4.0 | 18.0 |
| 6xGnat-vs-Longbow L | 100 | 2% | 0% | 0% | 98% | 0 | 256.9 | 73.1 | 62.5 |
| 6xGnat-vs-Longbow S | 100 | 0% | 0% | 0% | 100% | 0 | - | 5.3 | 57.9 |
| 6xGnat-vs-Mantis | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 18.4 |
| 6xGnat-vs-Midge | 100 | 0% | 5% | 0% | 95% | 0 | 273.5 | - | 18.7 |
| 6xGnat-vs-Pirate | 100 | 5% | 41% | 0% | 54% | 0 | 221.2 | 46.1 | 6.4 |
| 6xGnat-vs-Stinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 6xGnat-vs-Wasp | 100 | 0% | 28% | 0% | 72% | 0 | 270.3 | - | 20.8 |
| 6xGnat-vs-Watcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 9xGnat-vs-Aegis | 100 | 0% | 0% | 0% | 100% | 0 | - | 3.9 | 18.9 |
| 9xGnat-vs-Basilisk | 100 | 3% | 0% | 0% | 97% | 0 | 216.2 | 33.3 | 8.4 |
| 9xGnat-vs-Gnat | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 9xGnat-vs-Harrier | 100 | 1% | 0% | 0% | 99% | 0 | 176.0 | 4.6 | 18.5 |
| 9xGnat-vs-Hornet | 100 | 0% | 0% | 0% | 100% | 0 | - | 1.8 | 14.4 |
| 9xGnat-vs-Lancer | 100 | 0% | 0% | 0% | 100% | 0 | - | 1.9 | 17.9 |
| 9xGnat-vs-Longbow L | 100 | 2% | 0% | 0% | 98% | 0 | 197.8 | 51.0 | 62.5 |
| 9xGnat-vs-Longbow S | 100 | 2% | 0% | 0% | 98% | 0 | 261.9 | 5.8 | 56.7 |
| 9xGnat-vs-Mantis | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 16.4 |
| 9xGnat-vs-Midge | 100 | 0% | 0% | 0% | 100% | 0 | - | - | 18.4 |
| 9xGnat-vs-Pirate | 100 | 18% | 10% | 0% | 72% | 0 | 215.6 | 44.3 | 7.2 |
| 9xGnat-vs-Stinger | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 9xGnat-vs-Wasp | 100 | 1% | 0% | 0% | 99% | 0 | 275.8 | 6.3 | 15.7 |
| 9xGnat-vs-Watcher | 100 | 0% | 0% | 0% | 100% | 0 | - | - | - |

## Pirate ship

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2xAegis-vs-Pirate | 100 | 58% | 40% | 0% | 2% | 0 | 81.4 | 91.9 | 39.1 |
| 2xBasilisk-vs-Pirate | 100 | 16% | 62% | 0% | 22% | 0 | 102.8 | 82.3 | 90.7 |
| 2xGnat-vs-Pirate | 100 | 0% | 56% | 0% | 44% | 0 | 70.9 | 78.7 | 8.1 |
| 2xHarrier-vs-Pirate | 100 | 31% | 65% | 0% | 4% | 0 | 95.4 | 91.6 | 35.6 |
| 2xHornet-vs-Pirate | 100 | 23% | 73% | 1% | 3% | 0 | 111.3 | 57.1 | 31.7 |
| 2xLancer-vs-Pirate | 100 | 44% | 52% | 1% | 3% | 0 | 104.5 | 89.6 | 25.3 |
| 2xLongbow L-vs-Pirate | 100 | 74% | 0% | 0% | 26% | 0 | 35.0 | 124.5 | 77.5 |
| 2xLongbow S-vs-Pirate | 100 | 33% | 0% | 0% | 67% | 0 | 170.0 | 111.7 | 1.9 |
| 2xMantis-vs-Pirate | 100 | 12% | 56% | 0% | 32% | 0 | 133.3 | 85.5 | 16.6 |
| 2xMidge-vs-Pirate | 100 | 1% | 68% | 0% | 31% | 0 | 118.0 | 55.1 | 9.0 |
| 2xStinger-vs-Pirate | 100 | 0% | 57% | 0% | 43% | 0 | 79.2 | - | 6.6 |
| 2xWasp-vs-Pirate | 100 | 0% | 99% | 0% | 1% | 0 | 94.7 | 83.9 | 35.6 |
| 3xAegis-vs-Pirate | 100 | 87% | 12% | 0% | 1% | 0 | 71.7 | 88.2 | 40.8 |
| 3xBasilisk-vs-Pirate | 100 | 31% | 26% | 0% | 43% | 0 | 133.2 | 79.4 | 92.4 |
| 3xGnat-vs-Pirate | 100 | 2% | 56% | 0% | 42% | 0 | 107.9 | 52.8 | 7.5 |
| 3xHarrier-vs-Pirate | 100 | 69% | 30% | 0% | 1% | 0 | 94.3 | 90.2 | 39.2 |
| 3xHornet-vs-Pirate | 100 | 68% | 32% | 0% | 0% | 0 | 110.6 | 56.5 | 33.7 |
| 3xLancer-vs-Pirate | 100 | 80% | 16% | 0% | 4% | 0 | 108.2 | 88.4 | 28.0 |
| 3xLongbow L-vs-Pirate | 100 | 87% | 0% | 0% | 13% | 0 | 35.2 | 121.0 | 79.9 |
| 3xLongbow S-vs-Pirate | 100 | 75% | 0% | 0% | 25% | 0 | 163.0 | 116.5 | 4.5 |
| 3xMantis-vs-Pirate | 100 | 41% | 35% | 1% | 23% | 0 | 167.4 | 82.3 | 17.7 |
| 3xMidge-vs-Pirate | 100 | 8% | 30% | 0% | 62% | 0 | 147.8 | 49.4 | 9.2 |
| 3xStinger-vs-Pirate | 100 | 0% | 55% | 0% | 45% | 0 | 119.2 | - | 6.0 |
| 3xWasp-vs-Pirate | 100 | 1% | 97% | 0% | 2% | 0 | 113.9 | 81.5 | 37.5 |
| Aegis-vs-Pirate | 100 | 10% | 87% | 0% | 3% | 0 | 79.0 | 92.9 | 30.9 |
| Basilisk-vs-Pirate | 100 | 9% | 73% | 0% | 18% | 0 | 71.6 | 80.6 | 86.8 |
| Gnat-vs-Pirate | 100 | 0% | 72% | 0% | 28% | 0 | 56.7 | 100.0 | 8.5 |
| Harrier-vs-Pirate | 100 | 3% | 97% | 0% | 0% | 0 | 76.2 | 94.2 | 27.8 |
| Hornet-vs-Pirate | 100 | 0% | 100% | 0% | 0% | 0 | 75.0 | 57.9 | 26.3 |
| Lancer-vs-Pirate | 100 | 4% | 95% | 0% | 1% | 0 | 80.2 | 92.2 | 22.8 |
| Longbow L-vs-Pirate | 100 | 45% | 11% | 0% | 44% | 0 | 162.1 | 126.3 | 46.7 |
| Longbow S-vs-Pirate | 100 | 5% | 0% | 0% | 95% | 0 | 186.2 | 107.8 | 10.0 |
| Mantis-vs-Pirate | 100 | 0% | 90% | 0% | 10% | 0 | 86.5 | 91.5 | 16.2 |
| Midge-vs-Pirate | 100 | 0% | 69% | 0% | 31% | 0 | 74.0 | 66.6 | 9.9 |
| Stinger-vs-Pirate | 100 | 0% | 54% | 0% | 46% | 0 | 52.3 | - | 9.6 |
| Wasp-vs-Pirate | 100 | 0% | 100% | 0% | 0% | 0 | 73.2 | 84.3 | 29.8 |

## Problems seen

No script exceptions.

Stuck or circling flags (speed < 1 m/s for 30 s, or no engagement within 1.5x weapon range for 60 s):

| model | ship-runs | flagged | share | kinds |
| :--- | ---: | ---: | ---: | ---: |
| Aegis | 7800 | 2407 | 31% | slow 2392, noengage 15 |
| Basilisk | 7800 | 3236 | 41% | slow 2682, noengage 554 |
| Gnat | 58500 | 54009 | 92% | slow 53992, noengage 17 |
| Harrier | 7800 | 2441 | 31% | slow 2436, noengage 5 |
| Hornet | 14100 | 4449 | 32% | slow 4409, noengage 40 |
| Lancer | 9600 | 3004 | 31% | slow 2971, noengage 33 |
| Longbow L | 7800 | 2466 | 32% | slow 2460, noengage 6 |
| Longbow S | 9600 | 3022 | 31% | slow 3017, noengage 5 |
| Mantis | 9600 | 2970 | 31% | slow 2960, noengage 10 |
| Midge | 11700 | 3883 | 33% | slow 3763, noengage 120 |
| Pirate | 3900 | 459 | 12% | noengage 459 |
| Stinger | 11700 | 11226 | 96% | slow 11226 |
| Wasp | 14100 | 4599 | 33% | slow 4525, noengage 74 |

Friendly fire occurred in 566 ship-runs; top scenarios:
- 2v3/2xLongbow L-vs-3xGnat: 5 ships, 106731 damage
- 2v3/2xLongbow S-vs-3xWasp: 7 ships, 53158 damage
- 2v3/2xMantis-vs-3xWasp: 5 ships, 52586 damage
- pirate/3xLongbow L-vs-Pirate: 1 ships, 48950 damage
- 2v3/2xLancer-vs-3xWasp: 3 ships, 47010 damage
- 1v3/Longbow S-vs-3xWasp: 4 ships, 44992 damage
- 1v3/Hornet-vs-3xWasp: 3 ships, 39296 damage
- 1v2/Midge-vs-2xLongbow L: 3 ships, 34818 damage

Collisions: Longbow L 3260, Basilisk 3200, Longbow S 1938, Mantis 530, Watcher 321, Wasp 256, Lancer 235, Midge 150, Hornet 114, Gnat 107, Aegis 60, Harrier 34, Pirate 1

## Cost

| kind | runs | avg wall ms | avg sim s | wall ms per sim s |
| :--- | ---: | ---: | ---: | ---: |
| 1v1 | 9100 | 336 | 214.4 | 1.6 |
| 1v2 | 15600 | 531 | 221.2 | 2.4 |
| 1v3 | 4300 | 576 | 232.1 | 2.5 |
| 2v3 | 4300 | 855 | 248.9 | 3.4 |
| mixed | 6200 | 798 | 243.8 | 3.3 |
| pirate | 3600 | 314 | 154.5 | 2.0 |
| swarm | 4200 | 2234 | 296.3 | 7.5 |

| script | total cpu ms | Main calls | ms per call | slowest call ms |
| :--- | ---: | ---: | ---: | ---: |
| GnatSwarmAI | 9797753 | 943773524 | 0.0104 | 368.58 |
| HornetDroneAI | 3558928 | 292154870 | 0.0122 | 205.17 |
| LongbowSniperAI | 2866492 | 229258640 | 0.0125 | 76.05 |
| WaspAI | 1640387 | 177280013 | 0.0093 | 28.08 |
| MantisAI | 1192882 | 122649016 | 0.0097 | 39.55 |
| LancerAI | 1130917 | 122037517 | 0.0093 | 84.94 |
| BasiliskAI | 933655 | 99720193 | 0.0094 | 276.04 |
| AegisAI | 892411 | 95874703 | 0.0093 | 41.89 |
| HarrierAI | 855142 | 96212064 | 0.0089 | 26.41 |
| StingerAI | 133463 | 170982138 | 0.0008 | 67.33 |
| (brain) | 0 | 0 | 0.0000 | 0.00 |

