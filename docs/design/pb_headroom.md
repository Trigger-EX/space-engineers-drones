# Extra Programmable Blocks: where a ship needs one

Rule (CLAUDE.md "Script size"): the 100,000-char cap is per PB, budget ~80k minified, an over-budget script moves an off-hot-path module to another PB over IGC. A blueprint gets an extra PB when its script needs that, not as a blanket three. Assessed per family (read-only agents, each challenged by a second one), on origin/main at 1395231. Release zips not rebuilt, nothing simulated.

## Done
| Blueprint | Change |
|---|---|
| Ballista | blank `[SH] Spare PB` (3c in `ships/Ballista.md`): reserved for the Staff half of the Helm cut |
| Wasp, Aegis, Mantis, Longbow S | `C.add_support_pbs()` in the model function: a printer projects that function's grid, build_all only adds `[Staff]`/`[Thrust]` to the top-level blueprint, so printed copies were Flight-only |
| Peeper | same, `[Thrust]` PB for the printed probe |
| Basilisk, Basilisk Seeker | split done (`docs/design/basilisk_split.md`): `[Flight]` BasiliskAI 82.1k + `[Staff]` BasiliskStaffAI 23k in the cell the spare found, `[Thrust]` from build_all |
| Gnat, Shrike | one blank `[Spare] <Model> PB <n>` (`tools/designs/spares.py`: inside the existing hull, mount-seated, most enclosed free cell) reserved for the Staff half below. Gnat/Basilisk/Shrike wrappers sit in `gnat()`, `basilisk()`, `shrike_shipped()` (MODELS entry) so the Stinger fallback frame, `shrike_test` and the warship modules are unchanged |

No change needed: Midge (scriptless by design), Cradle Pod (CradleBayAI 17k), Watcher, Hornet/Lancer/Harrier (3 PBs), Longbow L / L Seeker (not printable, 3 from build_all), the 17 printers (HiveMotherAI 34k), Corsair/Warden/Leviathan (their `[Staff]` PB takes the Helm cut). Lite variants are retired.

## Still to do (script work, each needs sim runs; the blank PB is waiting for it)
* **Gnat**: GnatSwarmAI 87,228. New `GnatStaffAI.cs` (FLEET LINK 3.6k + LAUNCH 9.6k + display + SPLIT LINK, ~24-33k) on `[Staff] Gnat PB`; Flight ~76k. `tools/split_flight.py` asserts on FOLLOW hooks the Gnat lacks and has no Gnat AMMO entry; `make_staff.py` needs a no-FOLLOW option; `SUPPORT_STAFF['GNAT']`; Hive Mother `FindDrone` (below). No `[Thrust]` PB (point-and-burn).
* **ShrikeAI Helm**: done (docs/design/helm_split.md): FLEET LINK receive and damage sensing moved into StaffAI.cs (Helm 81.1k, gate 86,000 restored). Ballista: `[Staff][SH] Ballista Staff` (the spare); Shrike: `[Staff] Shrike`.

## Bug found on the way (not fixed here)
Since 51f75cd the drone PBs are `[Flight]/[Staff]/[Thrust] <Drone> PB`. `CradleBayAI.Mine()` skips names starting with `[`, and `HiveMotherAI.FindDrone()` takes only names starting with the bay Prefix, so neither finds a split drone's PB: no `[Launch]` keys written, no `armed` run, JOIN fallback only. Both must accept `[Staff] `/`[Flight] ` + Prefix (arm the Staff PB, never `[Thrust]`). Spare names `[Spare] ...` stay invisible to both.
