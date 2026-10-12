# Round-2 baseline: r2-base-team

5490 runs in 183 scenarios (2v3 1290, mixed 1860, pirate 1080, swarm 1260). Outcomes: mutual-kill 8, timeout 2693, win 2789.

Total wall time of all runs 6084 s (1.11 s per run on average); settings hash 86da6f4425ff003e.

Command line: `run --filter ^(mixed|swarm|pirate|2v3)/ --seeds 30 --out out/sim/results/r2-base-team`

## 2v3

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2xAegis-vs-3xGnat | 30 | 67% | 0% | 0% | 33% | 0 | 120.2 | 7.2 | - |
| 2xAegis-vs-3xHornet | 30 | 3% | 60% | 0% | 37% | 0 | 74.2 | 23.6 | 30.0 |
| 2xAegis-vs-3xMidge | 30 | 27% | 40% | 0% | 33% | 0 | 122.4 | 20.0 | 26.9 |
| 2xAegis-vs-3xStinger | 30 | 77% | 0% | 0% | 23% | 0 | 113.5 | 11.3 | - |
| 2xAegis-vs-3xWasp | 30 | 50% | 30% | 0% | 20% | 0 | 115.7 | 22.7 | 2.2 |
| 2xBasilisk-vs-3xGnat | 30 | 70% | 0% | 0% | 30% | 0 | 122.2 | 17.7 | 83.3 |
| 2xBasilisk-vs-3xHornet | 30 | 30% | 23% | 0% | 47% | 0 | 109.6 | 29.7 | 82.5 |
| 2xBasilisk-vs-3xMidge | 30 | 63% | 3% | 0% | 33% | 0 | 134.6 | 22.2 | 97.9 |
| 2xBasilisk-vs-3xStinger | 30 | 70% | 0% | 0% | 30% | 0 | 177.2 | 16.9 | - |
| 2xBasilisk-vs-3xWasp | 30 | 77% | 0% | 0% | 23% | 0 | 107.0 | 36.5 | 37.5 |
| 2xHarrier-vs-3xGnat | 30 | 77% | 0% | 0% | 23% | 0 | 119.7 | 16.4 | - |
| 2xHarrier-vs-3xHornet | 30 | 0% | 73% | 0% | 27% | 0 | 89.3 | 27.0 | 27.9 |
| 2xHarrier-vs-3xMidge | 30 | 43% | 23% | 0% | 33% | 0 | 127.2 | 24.4 | 19.6 |
| 2xHarrier-vs-3xStinger | 30 | 63% | 0% | 0% | 37% | 0 | 108.2 | 19.8 | - |
| 2xHarrier-vs-3xWasp | 30 | 43% | 17% | 7% | 33% | 0 | 108.3 | 30.4 | 1.6 |
| 2xHornet-vs-3xGnat | 30 | 70% | 0% | 0% | 30% | 0 | 111.0 | 17.9 | - |
| 2xHornet-vs-3xMidge | 30 | 7% | 60% | 0% | 33% | 0 | 114.2 | 25.3 | 23.6 |
| 2xHornet-vs-3xStinger | 30 | 80% | 0% | 0% | 20% | 0 | 112.9 | 19.9 | - |
| 2xHornet-vs-3xWasp | 30 | 50% | 13% | 0% | 37% | 0 | 125.7 | 31.2 | 1.6 |
| 2xLancer-vs-3xGnat | 30 | 73% | 0% | 0% | 27% | 0 | 133.8 | 17.9 | - |
| 2xLancer-vs-3xHornet | 30 | 3% | 63% | 0% | 33% | 0 | 95.0 | 24.3 | 25.6 |
| 2xLancer-vs-3xMidge | 30 | 17% | 40% | 0% | 43% | 0 | 130.5 | 25.3 | 22.1 |
| 2xLancer-vs-3xStinger | 30 | 77% | 0% | 0% | 23% | 0 | 125.5 | 19.1 | - |
| 2xLancer-vs-3xWasp | 30 | 57% | 17% | 3% | 23% | 0 | 152.2 | 24.4 | 1.5 |
| 2xLongbow L-vs-3xGnat | 30 | 7% | 0% | 0% | 93% | 0 | 183.3 | 69.1 | - |
| 2xLongbow L-vs-3xHornet | 30 | 0% | 63% | 0% | 37% | 0 | 171.0 | 46.3 | 80.6 |
| 2xLongbow L-vs-3xMidge | 30 | 0% | 0% | 0% | 100% | 0 | - | 53.1 | 92.7 |
| 2xLongbow L-vs-3xStinger | 30 | 0% | 0% | 0% | 100% | 0 | - | 79.2 | - |
| 2xLongbow L-vs-3xWasp | 30 | 0% | 0% | 0% | 100% | 0 | - | 57.2 | 72.0 |
| 2xLongbow S-vs-3xGnat | 30 | 13% | 0% | 0% | 87% | 0 | 218.1 | 51.9 | - |
| 2xLongbow S-vs-3xHornet | 30 | 0% | 77% | 0% | 23% | 0 | 110.8 | 55.5 | 23.9 |
| 2xLongbow S-vs-3xMidge | 30 | 0% | 17% | 0% | 83% | 0 | 277.3 | 57.1 | 24.4 |
| 2xLongbow S-vs-3xStinger | 30 | 10% | 0% | 0% | 90% | 0 | 271.8 | 70.8 | - |
| 2xLongbow S-vs-3xWasp | 30 | 7% | 3% | 0% | 90% | 0 | 200.0 | 60.1 | 9.7 |
| 2xMantis-vs-3xGnat | 30 | 50% | 0% | 0% | 50% | 0 | 201.2 | 16.6 | - |
| 2xMantis-vs-3xHornet | 30 | 0% | 57% | 0% | 43% | 0 | 93.3 | 32.0 | 26.4 |
| 2xMantis-vs-3xMidge | 30 | 0% | 50% | 0% | 50% | 0 | 130.5 | 13.8 | 24.3 |
| 2xMantis-vs-3xStinger | 30 | 53% | 0% | 0% | 47% | 0 | 192.0 | 17.3 | - |
| 2xMantis-vs-3xWasp | 30 | 37% | 20% | 0% | 43% | 0 | 170.3 | 25.0 | 2.7 |
| 2xWasp-vs-3xGnat | 30 | 77% | 0% | 0% | 23% | 0 | 95.5 | 11.6 | - |
| 2xWasp-vs-3xHornet | 30 | 0% | 57% | 0% | 43% | 0 | 78.0 | 1.0 | 27.8 |
| 2xWasp-vs-3xMidge | 30 | 7% | 50% | 7% | 37% | 0 | 156.3 | 4.3 | 18.5 |
| 2xWasp-vs-3xStinger | 30 | 73% | 0% | 0% | 27% | 0 | 87.5 | 9.5 | - |

## Mixed teams

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Hornet+Longbow S-vs-Aegis | 30 | 70% | 0% | 0% | 30% | 0 | 64.8 | 39.6 | 15.9 |
| Hornet+Longbow S-vs-Basilisk | 30 | 77% | 0% | 0% | 23% | 0 | 75.2 | 79.5 | 24.0 |
| Hornet+Longbow S-vs-Gnat | 30 | 73% | 0% | 0% | 27% | 0 | 57.6 | 22.3 | - |
| Hornet+Longbow S-vs-Harrier | 30 | 63% | 0% | 0% | 37% | 0 | 60.3 | 34.8 | 20.6 |
| Hornet+Longbow S-vs-Hornet | 30 | 53% | 0% | 0% | 47% | 0 | 70.8 | 33.4 | 13.3 |
| Hornet+Longbow S-vs-Hornet+Longbow S | 30 | 17% | 40% | 0% | 43% | 0 | 118.3 | 32.1 | 23.7 |
| Hornet+Longbow S-vs-Lancer | 30 | 77% | 0% | 0% | 23% | 0 | 55.7 | 34.3 | 13.0 |
| Hornet+Longbow S-vs-Lancer+Mantis | 30 | 20% | 20% | 0% | 60% | 0 | 131.4 | 30.4 | 26.2 |
| Hornet+Longbow S-vs-Longbow L | 30 | 67% | 0% | 0% | 33% | 0 | 45.5 | 77.1 | 35.0 |
| Hornet+Longbow S-vs-Longbow S | 30 | 67% | 0% | 0% | 33% | 0 | 55.8 | 39.2 | 22.7 |
| Hornet+Longbow S-vs-Mantis | 30 | 67% | 0% | 0% | 33% | 0 | 58.2 | 41.4 | 33.3 |
| Hornet+Longbow S-vs-Midge | 30 | 53% | 0% | 0% | 47% | 0 | 49.8 | 28.8 | 8.8 |
| Hornet+Longbow S-vs-Stinger | 30 | 53% | 0% | 0% | 47% | 0 | 60.2 | 27.0 | - |
| Hornet+Longbow S-vs-Wasp | 30 | 63% | 0% | 0% | 37% | 0 | 54.8 | 40.9 | 0.0 |
| Hornet+Longbow S-vs-Wasp+2xGnat | 30 | 50% | 0% | 0% | 50% | 0 | 130.7 | 19.5 | 15.7 |
| Hornet+Longbow S-vs-Watcher | 30 | 70% | 0% | 0% | 30% | 0 | 50.8 | 30.8 | - |
| Lancer+Mantis-vs-Aegis | 30 | 77% | 0% | 0% | 23% | 0 | 68.8 | 29.2 | 22.3 |
| Lancer+Mantis-vs-Basilisk | 30 | 57% | 3% | 0% | 40% | 0 | 54.2 | 74.2 | 24.7 |
| Lancer+Mantis-vs-Gnat | 30 | 67% | 0% | 0% | 33% | 0 | 48.8 | 34.7 | - |
| Lancer+Mantis-vs-Harrier | 30 | 77% | 0% | 0% | 23% | 0 | 64.0 | 22.5 | 28.4 |
| Lancer+Mantis-vs-Hornet | 30 | 60% | 0% | 0% | 40% | 0 | 57.7 | 20.0 | 25.8 |
| Lancer+Mantis-vs-Lancer | 30 | 73% | 0% | 0% | 27% | 0 | 60.2 | 26.1 | 15.5 |
| Lancer+Mantis-vs-Lancer+Mantis | 30 | 40% | 30% | 0% | 30% | 0 | 115.0 | 28.7 | 32.6 |
| Lancer+Mantis-vs-Longbow L | 30 | 67% | 3% | 0% | 30% | 0 | 66.3 | 70.8 | 64.7 |
| Lancer+Mantis-vs-Longbow S | 30 | 63% | 0% | 0% | 37% | 0 | 70.7 | 38.7 | 43.3 |
| Lancer+Mantis-vs-Mantis | 30 | 70% | 0% | 0% | 30% | 0 | 55.0 | 33.0 | 50.8 |
| Lancer+Mantis-vs-Midge | 30 | 73% | 0% | 0% | 27% | 0 | 72.8 | 26.2 | 23.4 |
| Lancer+Mantis-vs-Stinger | 30 | 70% | 0% | 0% | 30% | 0 | 55.8 | 29.6 | - |
| Lancer+Mantis-vs-Wasp | 30 | 60% | 0% | 0% | 40% | 0 | 67.2 | 33.6 | 0.0 |
| Lancer+Mantis-vs-Watcher | 30 | 63% | 0% | 0% | 37% | 0 | 60.5 | 21.9 | - |
| Wasp+2xGnat-vs-Aegis | 30 | 7% | 33% | 0% | 60% | 0 | 165.3 | 23.1 | 8.1 |
| Wasp+2xGnat-vs-Basilisk | 30 | 63% | 7% | 0% | 30% | 0 | 131.3 | 83.1 | 19.7 |
| Wasp+2xGnat-vs-Gnat | 30 | 57% | 0% | 0% | 43% | 0 | 109.2 | 22.0 | - |
| Wasp+2xGnat-vs-Harrier | 30 | 7% | 57% | 0% | 37% | 0 | 150.2 | 16.7 | 25.5 |
| Wasp+2xGnat-vs-Hornet | 30 | 7% | 53% | 0% | 40% | 0 | 157.5 | 12.6 | 26.0 |
| Wasp+2xGnat-vs-Lancer | 30 | 10% | 63% | 0% | 27% | 0 | 202.2 | 0.7 | 23.9 |
| Wasp+2xGnat-vs-Lancer+Mantis | 30 | 3% | 77% | 0% | 20% | 0 | 148.4 | 20.0 | 22.7 |
| Wasp+2xGnat-vs-Longbow L | 30 | 33% | 0% | 0% | 67% | 0 | 129.8 | 75.8 | 51.5 |
| Wasp+2xGnat-vs-Longbow S | 30 | 3% | 0% | 0% | 97% | 0 | 78.2 | 27.3 | 60.2 |
| Wasp+2xGnat-vs-Mantis | 30 | 13% | 0% | 0% | 87% | 0 | 118.6 | 19.7 | 28.2 |
| Wasp+2xGnat-vs-Midge | 30 | 30% | 23% | 0% | 47% | 0 | 178.0 | 11.1 | 21.3 |
| Wasp+2xGnat-vs-Stinger | 30 | 80% | 0% | 0% | 20% | 0 | 63.8 | 14.7 | - |
| Wasp+2xGnat-vs-Wasp | 30 | 53% | 13% | 0% | 33% | 0 | 122.2 | 10.1 | 6.5 |
| Wasp+2xGnat-vs-Wasp+2xGnat | 30 | 20% | 20% | 0% | 60% | 0 | 179.4 | 8.1 | 7.4 |
| Wasp+2xGnat-vs-Watcher | 30 | 73% | 0% | 0% | 27% | 0 | 80.1 | 22.7 | - |
| Watcher+3xGnat-vs-Aegis | 30 | 0% | 47% | 0% | 53% | 0 | 185.3 | - | 11.2 |
| Watcher+3xGnat-vs-Basilisk | 30 | 0% | 60% | 0% | 40% | 0 | 218.3 | 97.7 | 21.9 |
| Watcher+3xGnat-vs-Gnat | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher+3xGnat-vs-Harrier | 30 | 0% | 67% | 0% | 33% | 0 | 161.1 | 25.5 | 22.7 |
| Watcher+3xGnat-vs-Hornet | 30 | 0% | 50% | 0% | 50% | 0 | 166.2 | - | 22.4 |
| Watcher+3xGnat-vs-Hornet+Longbow S | 30 | 0% | 73% | 0% | 27% | 0 | 138.0 | - | 22.8 |
| Watcher+3xGnat-vs-Lancer | 30 | 0% | 60% | 0% | 40% | 0 | 190.9 | - | 21.7 |
| Watcher+3xGnat-vs-Lancer+Mantis | 30 | 0% | 77% | 0% | 23% | 0 | 156.8 | - | 20.2 |
| Watcher+3xGnat-vs-Longbow L | 30 | 0% | 0% | 0% | 100% | 0 | - | 70.7 | 68.7 |
| Watcher+3xGnat-vs-Longbow S | 30 | 0% | 13% | 0% | 87% | 0 | 261.3 | - | 64.4 |
| Watcher+3xGnat-vs-Mantis | 30 | 0% | 17% | 0% | 83% | 0 | 196.8 | - | 23.9 |
| Watcher+3xGnat-vs-Midge | 30 | 0% | 17% | 0% | 83% | 0 | 273.5 | - | 16.0 |
| Watcher+3xGnat-vs-Stinger | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher+3xGnat-vs-Wasp | 30 | 0% | 67% | 0% | 33% | 0 | 125.3 | - | 15.0 |
| Watcher+3xGnat-vs-Wasp+2xGnat | 30 | 0% | 70% | 0% | 30% | 0 | 156.5 | 22.4 | 10.8 |
| Watcher+3xGnat-vs-Watcher | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| Watcher+3xGnat-vs-Watcher+3xGnat | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |

## Hive swarms (Gnats)

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 12xGnat-vs-Aegis | 30 | 27% | 0% | 0% | 73% | 0 | 180.2 | 26.7 | 8.2 |
| 12xGnat-vs-Basilisk | 30 | 43% | 0% | 0% | 57% | 0 | 198.5 | 80.3 | 24.4 |
| 12xGnat-vs-Gnat | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 12xGnat-vs-Harrier | 30 | 27% | 0% | 0% | 73% | 0 | 185.2 | 34.2 | 18.7 |
| 12xGnat-vs-Hornet | 30 | 13% | 0% | 0% | 87% | 0 | 220.8 | 29.2 | 19.2 |
| 12xGnat-vs-Lancer | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 20.3 |
| 12xGnat-vs-Longbow L | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 72.0 |
| 12xGnat-vs-Longbow S | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 70.2 |
| 12xGnat-vs-Mantis | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 18.1 |
| 12xGnat-vs-Midge | 30 | 23% | 0% | 0% | 77% | 0 | 183.2 | 20.8 | 14.7 |
| 12xGnat-vs-Pirate | 30 | 20% | 0% | 0% | 80% | 0 | 117.2 | 89.7 | 11.4 |
| 12xGnat-vs-Stinger | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 12xGnat-vs-Wasp | 30 | 0% | 20% | 0% | 80% | 0 | 279.0 | - | 13.2 |
| 12xGnat-vs-Watcher | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 6xGnat-vs-Aegis | 30 | 13% | 10% | 0% | 77% | 0 | 189.8 | 47.5 | 8.4 |
| 6xGnat-vs-Basilisk | 30 | 13% | 10% | 0% | 77% | 0 | 251.2 | 76.7 | 17.8 |
| 6xGnat-vs-Gnat | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 6xGnat-vs-Harrier | 30 | 23% | 47% | 0% | 30% | 0 | 208.7 | 33.1 | 19.5 |
| 6xGnat-vs-Hornet | 30 | 7% | 37% | 0% | 57% | 0 | 251.0 | 33.3 | 19.2 |
| 6xGnat-vs-Lancer | 30 | 0% | 43% | 0% | 57% | 0 | 244.3 | - | 20.3 |
| 6xGnat-vs-Longbow L | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 70.9 |
| 6xGnat-vs-Longbow S | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 65.8 |
| 6xGnat-vs-Mantis | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 20.9 |
| 6xGnat-vs-Midge | 30 | 13% | 10% | 0% | 77% | 0 | 266.0 | 36.5 | 17.2 |
| 6xGnat-vs-Pirate | 30 | 10% | 33% | 0% | 57% | 0 | 161.0 | 91.8 | 10.8 |
| 6xGnat-vs-Stinger | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 6xGnat-vs-Wasp | 30 | 0% | 80% | 0% | 20% | 0 | 182.6 | - | 13.5 |
| 6xGnat-vs-Watcher | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 9xGnat-vs-Aegis | 30 | 23% | 7% | 0% | 70% | 0 | 200.7 | 35.2 | 9.5 |
| 9xGnat-vs-Basilisk | 30 | 23% | 3% | 0% | 73% | 0 | 218.8 | 84.1 | 19.6 |
| 9xGnat-vs-Gnat | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 9xGnat-vs-Harrier | 30 | 43% | 10% | 0% | 47% | 0 | 166.6 | 36.6 | 19.6 |
| 9xGnat-vs-Hornet | 30 | 23% | 0% | 0% | 77% | 0 | 241.3 | 34.9 | 20.1 |
| 9xGnat-vs-Lancer | 30 | 7% | 7% | 0% | 87% | 0 | 200.2 | 52.6 | 21.0 |
| 9xGnat-vs-Longbow L | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 65.1 |
| 9xGnat-vs-Longbow S | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 69.0 |
| 9xGnat-vs-Mantis | 30 | 0% | 0% | 0% | 100% | 0 | - | - | 17.9 |
| 9xGnat-vs-Midge | 30 | 13% | 0% | 0% | 87% | 0 | 248.0 | 19.6 | 17.1 |
| 9xGnat-vs-Pirate | 30 | 37% | 13% | 0% | 50% | 0 | 153.2 | 89.5 | 9.7 |
| 9xGnat-vs-Stinger | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |
| 9xGnat-vs-Wasp | 30 | 0% | 63% | 0% | 37% | 0 | 225.5 | - | 13.2 |
| 9xGnat-vs-Watcher | 30 | 0% | 0% | 0% | 100% | 0 | - | - | - |

## Pirate ship

| scenario (A-vs-B) | runs | A wins | B wins | draw | timeout | err | median ttk s | A hit% | B hit% |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2xAegis-vs-Pirate | 30 | 90% | 3% | 3% | 3% | 0 | 87.2 | 85.7 | 16.9 |
| 2xBasilisk-vs-Pirate | 30 | 13% | 57% | 0% | 30% | 0 | 95.3 | 86.7 | 90.1 |
| 2xGnat-vs-Pirate | 30 | 0% | 60% | 0% | 40% | 0 | 72.6 | 95.0 | 8.4 |
| 2xHarrier-vs-Pirate | 30 | 30% | 63% | 0% | 7% | 0 | 89.7 | 88.1 | 29.4 |
| 2xHornet-vs-Pirate | 30 | 27% | 70% | 3% | 0% | 0 | 111.7 | 91.9 | 33.3 |
| 2xLancer-vs-Pirate | 30 | 80% | 20% | 0% | 0% | 0 | 84.7 | 89.6 | 20.9 |
| 2xLongbow L-vs-Pirate | 30 | 83% | 0% | 0% | 17% | 0 | 79.7 | 93.8 | - |
| 2xLongbow S-vs-Pirate | 30 | 37% | 0% | 0% | 63% | 0 | 190.3 | 96.7 | - |
| 2xMantis-vs-Pirate | 30 | 27% | 37% | 0% | 37% | 0 | 114.0 | 79.9 | 14.4 |
| 2xMidge-vs-Pirate | 30 | 0% | 57% | 0% | 43% | 0 | 99.2 | 49.0 | 9.4 |
| 2xStinger-vs-Pirate | 30 | 0% | 50% | 0% | 50% | 0 | 93.0 | - | 6.6 |
| 2xWasp-vs-Pirate | 30 | 0% | 100% | 0% | 0% | 0 | 92.2 | 81.2 | 28.2 |
| 3xAegis-vs-Pirate | 30 | 100% | 0% | 0% | 0% | 0 | 65.9 | 88.8 | 16.1 |
| 3xBasilisk-vs-Pirate | 30 | 27% | 13% | 0% | 60% | 0 | 126.8 | 81.8 | 90.7 |
| 3xGnat-vs-Pirate | 30 | 0% | 63% | 0% | 37% | 0 | 101.2 | 94.0 | 9.9 |
| 3xHarrier-vs-Pirate | 30 | 60% | 40% | 0% | 0% | 0 | 102.2 | 91.2 | 36.4 |
| 3xHornet-vs-Pirate | 30 | 67% | 27% | 0% | 7% | 0 | 101.8 | 88.9 | 32.2 |
| 3xLancer-vs-Pirate | 30 | 100% | 0% | 0% | 0% | 0 | 89.4 | 91.8 | 25.2 |
| 3xLongbow L-vs-Pirate | 30 | 87% | 0% | 0% | 13% | 0 | 82.1 | 91.9 | 83.5 |
| 3xLongbow S-vs-Pirate | 30 | 77% | 0% | 0% | 23% | 0 | 135.7 | 97.3 | 17.4 |
| 3xMantis-vs-Pirate | 30 | 63% | 20% | 0% | 17% | 0 | 184.8 | 75.3 | 16.8 |
| 3xMidge-vs-Pirate | 30 | 7% | 27% | 0% | 67% | 0 | 117.7 | 47.5 | 8.8 |
| 3xStinger-vs-Pirate | 30 | 0% | 60% | 0% | 40% | 0 | 106.9 | - | 5.8 |
| 3xWasp-vs-Pirate | 30 | 0% | 100% | 0% | 0% | 0 | 140.4 | 80.9 | 33.3 |
| Aegis-vs-Pirate | 30 | 17% | 80% | 3% | 0% | 0 | 82.7 | 90.7 | 15.5 |
| Basilisk-vs-Pirate | 30 | 10% | 80% | 0% | 10% | 0 | 64.5 | 72.6 | 88.0 |
| Gnat-vs-Pirate | 30 | 0% | 67% | 0% | 33% | 0 | 52.1 | - | 10.8 |
| Harrier-vs-Pirate | 30 | 0% | 100% | 0% | 0% | 0 | 66.5 | 94.7 | 20.0 |
| Hornet-vs-Pirate | 30 | 0% | 100% | 0% | 0% | 0 | 64.8 | 96.1 | 22.7 |
| Lancer-vs-Pirate | 30 | 3% | 97% | 0% | 0% | 0 | 84.4 | 92.8 | 16.6 |
| Longbow L-vs-Pirate | 30 | 57% | 0% | 0% | 43% | 0 | 99.8 | 94.6 | - |
| Longbow S-vs-Pirate | 30 | 3% | 0% | 0% | 97% | 0 | 285.8 | 91.2 | - |
| Mantis-vs-Pirate | 30 | 0% | 60% | 0% | 40% | 0 | 74.2 | 89.8 | 13.5 |
| Midge-vs-Pirate | 30 | 0% | 67% | 0% | 33% | 0 | 77.9 | 64.5 | 10.0 |
| Stinger-vs-Pirate | 30 | 0% | 60% | 0% | 40% | 0 | 48.2 | - | 9.3 |
| Wasp-vs-Pirate | 30 | 0% | 100% | 0% | 0% | 0 | 64.2 | 90.5 | 25.4 |

## Problems seen

No script exceptions.

Stuck or circling flags (speed < 1 m/s for 30 s, or no engagement within 1.5x weapon range for 60 s):

| model | ship-runs | flagged | share | kinds |
| :--- | ---: | ---: | ---: | ---: |
| Aegis | 690 | 223 | 32% | slow 221, noengage 2 |
| Basilisk | 690 | 254 | 37% | slow 233, noengage 21 |
| Gnat | 15240 | 13802 | 91% | slow 13761, noengage 41 |
| Harrier | 690 | 203 | 29% | slow 203 |
| Hornet | 1890 | 634 | 34% | slow 609, noengage 25 |
| Lancer | 1230 | 379 | 31% | slow 367, noengage 12 |
| Longbow L | 690 | 205 | 30% | slow 202, noengage 3 |
| Longbow S | 1230 | 394 | 32% | slow 374, noengage 20 |
| Mantis | 1230 | 372 | 30% | slow 364, noengage 8 |
| Midge | 1200 | 486 | 40% | slow 434, noengage 52 |
| Pirate | 1170 | 159 | 14% | noengage 159 |
| Stinger | 1200 | 1183 | 99% | slow 1183 |
| Wasp | 1890 | 606 | 32% | slow 580, noengage 26 |

Friendly fire occurred in 32 ship-runs; top scenarios:
- 2v3/2xAegis-vs-3xWasp: 1 ships, 13779 damage
- 2v3/2xHornet-vs-3xWasp: 2 ships, 12470 damage
- pirate/3xMidge-vs-Pirate: 3 ships, 5207 damage
- 2v3/2xLancer-vs-3xMidge: 3 ships, 3841 damage
- 2v3/2xWasp-vs-3xMidge: 2 ships, 1836 damage
- 2v3/2xMantis-vs-3xMidge: 1 ships, 1450 damage
- 2v3/2xLongbow S-vs-3xMidge: 1 ships, 1240 damage
- 2v3/2xBasilisk-vs-3xHornet: 2 ships, 900 damage

Collisions: Gnat 13, Basilisk 13, Longbow L 8, Midge 4, Hornet 3, Longbow S 3, Watcher 3, Lancer 1, Mantis 1, Pirate 1

## Cost

| kind | runs | avg wall ms | avg sim s | wall ms per sim s |
| :--- | ---: | ---: | ---: | ---: |
| 2v3 | 1290 | 737 | 208.2 | 3.5 |
| mixed | 1860 | 773 | 199.8 | 3.9 |
| pirate | 1080 | 367 | 154.9 | 2.4 |
| swarm | 1260 | 2619 | 282.8 | 9.3 |

| script | total cpu ms | Main calls | ms per call | slowest call ms |
| :--- | ---: | ---: | ---: | ---: |
| GnatSwarmAI | 2903275 | 225984728 | 0.0128 | 68.92 |
| HornetDroneAI | 411197 | 29907300 | 0.0137 | 55.99 |
| LongbowSniperAI | 370086 | 25301901 | 0.0146 | 21.27 |
| WaspAI | 225551 | 20306128 | 0.0111 | 24.59 |
| MantisAI | 177421 | 13777268 | 0.0129 | 16.82 |
| LancerAI | 144600 | 12228197 | 0.0118 | 32.02 |
| BasiliskAI | 89399 | 8282725 | 0.0108 | 22.77 |
| HarrierAI | 76124 | 6750644 | 0.0113 | 22.29 |
| AegisAI | 72424 | 6766914 | 0.0107 | 28.97 |
| StingerAI | 15021 | 13737503 | 0.0011 | 16.07 |
| (brain) | 0 | 0 | 0.0000 | 0.00 |

