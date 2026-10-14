# Extra Programmable Blocks: where a ship needs one

Rule (CLAUDE.md "Script size"): the 100,000-char cap is per PB, budget ~80k minified, an over-budget script moves an off-hot-path module to another PB over IGC. A blueprint gets an extra PB when its script needs that, not as a blanket three. Assessed per family (read-only agents, each challenged by a second one), on origin/main at 1395231. Release zips not rebuilt, nothing simulated.

## Done
| Blueprint | Change |
|---|---|
| Ballista | blank `[SH] Spare PB` (3c in `ships/Ballista.md`): reserved for the Staff half of the Helm cut |
| Wasp, Aegis, Mantis, Longbow S | `C.add_support_pbs()` in the model function: a printer projects that function's grid, build_all only adds `[Staff]`/`[Thrust]` to the top-level blueprint, so printed copies were Flight-only |
| Peeper | same, `[Thrust]` PB for the printed probe |
| Basilisk, Basilisk Seeker | split done (`docs/design/basilisk_split.md`): `[Flight]` BasiliskAI 82.1k + `[Staff]` BasiliskStaffAI 23k in the cell the spare found, `[Thrust]` from build_all |
| Gnat | split done (`docs/design/gnat_split.md`): `[Flight]` GnatSwarmAI 76.4k + `[Staff]` GnatStaffAI 22.4k on the tail, no `[Thrust]` (point-and-burn) |
| Shrike, Ballista | the Helm split (`docs/design/helm_split.md`): the reserved spares became `[Staff] Shrike` and `[Staff][SH] Ballista Staff` running StaffAI |

No change needed: Midge (scriptless by design), Cradle Pod (CradleBayAI 17k), Watcher, Hornet/Lancer/Harrier (3 PBs), Longbow L / L Seeker (not printable, 3 from build_all), the 17 printers (HiveMotherAI 34k), Corsair/Warden/Leviathan (their `[Staff]` PB takes the Helm cut). Lite variants are retired.

## Still to do
Nothing from this list: every over-budget script now has its Staff half (Gnat, Basilisk, Shrike Helm). The Flight halves sit at 76-82k; the next cuts if one grows are in the split docs.

## Bug found on the way: fixed in 4d8fccd
Since 51f75cd the drone PBs are `[Flight]/[Staff]/[Thrust] <Drone> PB`; `CradleBayAI.Mine()` skipped names starting with `[` and `HiveMotherAI.FindDrone()` took only names starting with the bay Prefix, so neither found a split drone's PB (no `[Launch]` keys, no `armed`, JOIN fallback only). Both now accept `[Staff] <Prefix>` / `[Flight] <Prefix>` and arm the Staff first, never `[Thrust]`.
