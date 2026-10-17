# Survivability audit

`python3 -I tools/survivability_audit.py [--check] [--strict] [--only "Hornet" ...] [--ships] [--summary]`
(test: `python3 -I tools/tests/test_survivability_audit.py`). A design check like `blocks_sync.py --check`: it builds the in-memory Full blueprints
(model function, `add_support_pbs`, `finalize`, as `build_all.py` does; nothing is written) and reports how few hits disable each drone. Lite is not covered.

## What it computes
* **Systems**: controller (RemoteControl / cockpit), flight PB (name prefix `[Flight]`; a PB without a prefix counts as flight; `[Staff]` and `[Thrust]` PBs are listed
  but their loss does not stop the drone), gyros, thrusters per push direction (`OPP[fwd]`, six axes), power (battery, reactor, hydrogen engine), guns (mission kill, not a kill set).
* **Kill sets**: a block dies when one of its cells is hit, so the smallest cell set that disables a system is one cell per block of it. Disabling systems: all controllers,
  all flight PBs, all gyros, all power, all thrusters of one axis the drone thrusts on (axes with no thruster, e.g. the Gnat's sides, are not counted). The smallest of these is the
  drone's kill set. A system of one block is a single point of failure (SPOF); every cell of that block is a one-cell kill set.
* **Exposure (cover)**: for each critical block and each of the six directions, the number of occupied cells (all cells of multi-cell blocks, the block's own excluded) between the
  block and the outside of the bounding box, minimum over the block's cells. 0 means an open face. Thrusters are summarised only (their exhaust is open by design) and are left out of the open-face share.
* **Clustering**: per system with several blocks the max pairwise distance, and the radius of the smallest ball (candidate centres: blocks, pair midpoints, centroid) that holds all of them.
  `CLUSTERED` when that radius is within `BLAST_RADIUS_M` (2.5 m small grid, 5 m large: a guess, not measured). The detail also says when the whole bounding-box diagonal fits in two blast radii.
* **Score** 0..100 (heuristic, only for ranking): 100 - 15 per unwaived SPOF - 3 per waived SPOF - 5 per clustered system (thruster axes excluded) - 30 x share of non-thruster critical (block, face) pairs with 0 cover.

## --check rule
Exit 1 if a drone has a single-cell kill set in a system that is not in `WAIVED_SPOF = ('controller', 'flight PB')`, or lacks a controller / flight PB / gyro / power block entirely.
Waived because one RemoteControl is the design (a second one removes the SPOF anyway) and exactly one PB writes the overrides (CLAUDE.md, Wasp Split). `--strict` waives nothing.

## Not modelled
Structural cut points (a hit that splits the grid, mount-point adjacency is in `Blueprint.attachment`), armour hit points and block health, damage spread by weapon type, subgrids (main grid only),
conveyor / power-network links. A kill set of 1 is a statement about block count, not about how hard that block is to hit: read it together with the cover column.
