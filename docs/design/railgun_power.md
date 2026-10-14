# Railgun power: Longbow S, Longbow L (+ Seeker), Mantis

Code: `tools/designs/core.py` (`small_rail_power`, `large_rail_power`, `LARGE_RAIL_REACTORS`, `LARGE_RAIL_THRUSTERS`), `tools/designs/extra.py` (`mantis`),
helpers in `tools/designs/_common.py` (`reactor`, `swap_armour`, `power_budget`, `loaded_mass`, `loaded_accel`, `turn_rates`). Checks:
`tools/tests/test_rail_power.py`. The Ballista had a similar change first (`docs/design/ships/Ballista.md` section 9); the warships (Warden, Corsair,
Leviathan) already had reactors for their rails.

## Power for the railgun recharge (2026-10-14)

Owner's brief: "For the railgun ships, I want the railguns to always have sufficient power. Add power systems as required, and thrust as required to maintain
the same manoeuvrability." This section covers the three railgun drones that ran on batteries alone.

### The problem

A railgun's capacitor recharges at a fixed draw: `SmallRailgun` 3.6 MW (0.016 MWh, 20 s), `LargeRailgun` 38 MW (0.5 MWh, 59 s) [data:
`EntityComponents.sbc`, `docs/sim-design.md` "Railgun reload"]. Thrusters (power group 80) and gyros (70) are served before the recharge (90), so the
recharge gets what is left. Before the change the three drones had batteries only (Longbow S and Mantis one `SmallBlockBatteryBlock`, 4 MW / 1 MWh;
Longbow L four `LargeBlockBatteryBlock`, 48 MW / 12 MWh):

* with the battery flat, no recharge at all;
* on a full diagonal manoeuvre the rail got 2.7 MW (Longbow S: 27 s per recharge instead of 20 s), 2.3 MW (Mantis: 31 s) and 7 MW (Longbow L: 5 min 14 s
  instead of 59 s).

### Rules applied (checked by `test_rail_power.py`)

1. **Peak**: reactors + batteries >= 1.05 x need, where need = every railgun recharging + on **each of the three axes the larger of its two opposite
   thruster groups** at full (forward or back, up or down, left or right: opposite thrusters do not fire together, a diagonal manoeuvre fires one group per
   axis) + gyros, AI blocks, cameras, remote control + an allowance of 0.05 MW small / 0.5 MW large for the blocks with no figure in defs_stats (PBs, antenna,
   sensor, turret). `C.power_budget()`.
2. **Sustained**: the reactors alone >= the railgun draw, so a flat battery never stops the rails. Every reactor carries uranium (small grid 5 kg, large grid
   20 kg each; 1 kg is 1 MWh).
3. **Acceleration**: on every one of the six axes, loaded (uranium aboard), not below the original figure.
4. **Turning**: gyro angular acceleration (pitch / yaw / roll about the centre of mass, point masses at the cell centres; `C.turn_rates()`) not below the
   original figure on any axis.

The smallest change that meets all four was kept. Rule 2 fixes the reactor count from below (8 small-grid reactors for 3.6 MW, 3 large-grid for 38 MW);
their mass costs acceleration, every thruster added for it costs 121 kg / 4.4 t and adds demand, and the mass costs turn rate.

### What was added

**Longbow S** (`small_rail_power`): 8 `SmallBlockSmallGenerator` (`Longbow S Reactor 1..8`, 4 MW) in place of the hull armour at z -2..-1 beside the gyro (a
reactor mounts on all six faces like the armour it replaces, and sits on the centre of mass). With one more thruster per direction and no extra gyro the
ship met rules 1 to 3 at 9.2 t, but turned at 88 / 84 / 75 % of the original; a second gyro (614 kg) breaks the 3-thrusters-per-axis budget (the limit is
9.47 t), so the pack is `Longbow S Gyroscope 2` (in place of the armour behind the first gyro, standing on the battery) + one more thruster **pair** per
direction, the Longbow L pattern: `Ion Main L2/R2`, `Ion Brake L2/R2`, `Ion Strafe R3/R4` + `L3/L4`, `Ion Lift L2/R2`, `Ion Dive L2/R2`. 12 to 24
thrusters.

**Mantis** (`mantis` calls `small_rail_power`): the same pack (8 reactors, `Gyroscope 2`, a pair per direction) and three more mains on the battery's
back face (`Ion Main L3`, `R3`, `C`), because its forward acceleration was already twice the other axes: 7 mains keep it. 14 to 27 thrusters.

**Longbow L** and **Longbow L Seeker** (`large_rail_power`, shared frame): 5 `LargeBlockSmallGenerator` (`Reactor 1..5`, 75 MW: 3 for the rail, 2 for the
added thrusters) in place of the hull armour at y -2, z -1..0; `Gyroscope 2` in place of the armour above the port reactor; three more ion thrusters per
direction (`LARGE_RAIL_THRUSTERS`: `Ion Main L3, R3, L4`, `Ion Brake L3, R3, L4`, `Ion Lift M2, L2, R2`, `Ion Dive L3, R3, L4`, `Ion Strafe R5..R7` and
`L5..L7`), 23 to 41 thrusters. The grid search tried 2 / 3 / 4 more thrusters per direction x 3..6 reactors x 0 / 1 extra gyro: the lightest design without a
gyro (2 more per direction, 4 reactors, 229 t) turned at 68-76 % of the original; with the gyro, 2 more per direction falls 1 % short on acceleration
(242 t against the 239 t at which six thrusters still give 8.67 m/s2), so 3 more it is. Within a direction the third thruster has no mirror partner (`L4` only): thrust acts at the centre of mass,
so this costs nothing but symmetry.

The old blocks keep their names and cells; every new block attaches by mount points, the grid is one piece, and no new block stands in an exhaust, muzzle
or camera lane (validated by `tools/sebp.py` in the build). No DLC blocks.

### Before / after

Power (MW; supply = reactors + batteries; thrusters = the larger group per axis, summed over the three axes; "all" = every thruster at once, for reference):

| Model | reactors | batteries | supply | rail | thrusters (all) | other | need | supply / need | reactors / rail |
|---|---|---|---|---|---|---|---|---|---|
| Longbow S before | 0 | 4.0 (1 MWh) | 4.0 | 3.6 | 1.2 (2.4) | 0.08 | 4.88 | 0.82 | 0 |
| Longbow S after | 4.0 (8) | 4.0 (1 MWh) | 8.0 | 3.6 | 2.4 (4.8) | 0.08 | 6.08 | **1.315** | **1.11** |
| Mantis before | 0 | 4.0 (1 MWh) | 4.0 | 3.6 | 1.6 (2.8) | 0.08 | 5.28 | 0.76 | 0 |
| Mantis after | 4.0 (8) | 4.0 (1 MWh) | 8.0 | 3.6 | 3.0 (5.4) | 0.08 | 6.68 | **1.197** | **1.11** |
| Longbow L before | 0 | 48 (12 MWh) | 48 | 38 | 40.3 (77.3) | 0.54 | 78.9 | 0.61 | 0 |
| Longbow L after | 75 (5) | 48 (12 MWh) | 123 | 38 | 70.6 (137.8) | 0.55 | 109.1 | **1.127** | **1.97** |

The Seeker has the same power plant and thrusters as the Longbow L. Every thruster at once would still not stop the small drones' rails (8.0 MW against
4.8 + 3.7 MW on the Longbow S: the rail gets 3.1 MW, 23 s per recharge); on the Longbow L it would. Uranium: Longbow S / Mantis 40 kg (10 h at full reactor
output), Longbow L 100 kg (1.3 h at full output, 2.6 h at the rail's 38 MW).

Mass, PCU and acceleration (m/s2 with the uranium aboard; the original ships carried no items):

| Model | blocks | mass kg | PCU | Forward | Backward | Up | Down | Left | Right |
|---|---|---|---|---|---|---|---|---|---|
| Longbow S before | 55 | 6,320 | 1,085 | 4.56 | 4.56 | 4.56 | 4.56 | 4.56 | 4.56 |
| Longbow S after | 67 | 10,430 (+40 U) | 1,470 | 5.50 | 5.50 | 5.50 | 5.50 | 5.50 | 5.50 |
| Mantis before | 55 | 6,589 | 1,114 | 8.74 | 4.37 | 4.37 | 4.37 | 4.37 | 4.37 |
| Mantis after | 68 | 10,819 (+40 U) | 1,511 | 9.28 | 5.30 | 5.30 | 5.30 | 5.30 | 5.30 |
| Longbow L before | 65 | 159,430 | 1,258 | 8.67 | 8.67 | 6.50 | 8.67 | 8.67 | 8.67 |
| Longbow L after | 83 | 272,052 (+100 U) | 1,643 | 8.89 | 8.89 | 7.62 | 8.89 | 8.89 | 8.89 |
| Longbow L Seeker before | 66 | 173,654 | 1,533 | 7.96 | 7.96 | 5.97 | 7.96 | 7.96 | 7.96 |
| Longbow L Seeker after | 84 | 286,276 (+100 U) | 1,918 | 8.45 | 8.45 | 7.24 | 8.45 | 8.45 | 8.45 |

(Blocks, mass and PCU as `build_all.py` writes them, support PBs included.) Mass: Longbow S +65 %, Mantis +64 %, Longbow L +71 %.

Turning (gyro torque / moment of inertia, rad/s2, pitch / yaw / roll):

| Model | gyros | before | after | after / before |
|---|---|---|---|---|
| Longbow S | 1 to 2 | 38.9 / 47.5 / 71.5 | 63.1 / 69.0 / 93.4 | 162 / 145 / 131 % |
| Mantis | 1 to 2 | 35.5 / 44.6 / 65.5 | 55.2 / 62.5 / 86.4 | 155 / 140 / 132 % |
| Longbow L | 1 to 2 | 6.71 / 5.97 / 7.25 | 8.70 / 7.76 / 8.12 | 130 / 130 / 112 % |
| Longbow L Seeker | 1 to 2 | 4.86 / 5.63 / 5.32 | 6.69 / 7.38 / 6.55 | 138 / 131 / 123 % |

A gyro is all or nothing: one extra doubles the torque, so every ship comes out turning faster, and the thrusters that pay for the gyro's mass raise the
accelerations above the original too (the lifts of the Longbow L most: 5 lifts gave 6.35 m/s2, 6 give 7.62).

Recharge time on a full diagonal manoeuvre (power left after the thrusters, x 0.8 charge efficiency): Longbow S 27 s to 20 s, Mantis 31 s to 20 s,
Longbow L 314 s to 59 s, that is the capacitor's own rate; with a flat battery the reactors alone still recharge the rail at its full rate when the
thrusters are idle.

### Printers

The Longbow Foundry and the Mantis Foundry still build and validate (`build_all.py --only "Longbow Foundry" "Mantis Foundry"`: welder reach to every
drone block, docking stalk, bay geometry). A printed drone costs more (iron: Longbow S 9.9 t instead of 5.9 t, Mantis 10.3 t instead of 6.2 t; plus silver
for the reactors) and takes longer to weld.

**Known limitation:** the projection carries no items, so a printed Longbow S / Mantis has **empty reactors**; until uranium is put in (by hand, or through
conveyors, which the printer bays do not have) its rail runs on the battery as before. Pasted blueprints carry the uranium.

### Not verified

Nothing of this has run in the game: that the game serves the recharge after the thrusters in the order assumed (power groups from the definitions), that
the reactors written with uranium start producing at once, that the reactor block XML loads cleanly (the Longbows / Mantis are legacy-shape blueprints;
their reactors use the game-written `Reactor` shape from the samples, `sample_shape`), and the turn and acceleration figures (computed from defs_stats masses
and forces; the game's inertia uses block boxes, not cell points). No sim run: the change is in the blueprints and the sim builds its power model from them
(`sim/src/Blueprint/DefDb.cs` reads the reactor output); the Longbow L's new mass will change its sim results (the Tech Hunter fleets use it).
