# Audit: physics, mass/inertia, thrust, gyro, power, structure (key `physics_power`)

Scope: `sim/src/Core/{Physics,Body,GridFactory,PowerSystem,Connectivity,World,BlockInst,OrientMath,SimSettings}.cs`,
`sim/src/Blueprint/DefDb.cs`, `defs_stats.json`, `releases/MODELS.md`, and the physics/power claims in `packages/unknowns.md`.

## Method and evidence

- Game data: `defs_stats.json` (thrusters, gyros, batteries, components, constants, power groups).
- Keen source: the public `KeenSoftwareHouse/SpaceEngineers` repo on raw.githubusercontent.com (reachable). It is the **2019 public snapshot**
  (about game 1.19x). Everything marked `[src]` below was read from it. The game may have changed since, so each source-based finding says so.
  Files used (all under `Sources/`): `Sandbox.Game/Game/GameSystems/MyGridGyroSystem.cs`, `Sandbox.Game/Game/EntityComponents/MyEntityThrustComponent.cs`,
  `.../MyThrusterBlockThrustComponent.cs`, `Sandbox.Game/Game/Entities/Blocks/{MyThrust,MyGyro,MyBatteryBlock,MyProjectorBase}.cs`,
  `Sandbox.Game/Game/Entities/Cube/{MyGridPhysics,MyGridShape,MyGridMassComputer,MySlimBlock,MyComponentStack,MyDisconnectHelper}.cs`,
  `Sandbox.Game/Definitions/{MyCubeBlockDefinition,MyEnvironmentDefinition}.cs`, `Sandbox.Game/Engine/Utils/MyFakes.cs`, `Sandbox.Game/Game/MyPerGameSettings.cs`,
  `Sandbox.Game/Game/EntityComponents/MyResourceDistributorComponent.cs`, `SpaceEngineers.Game/SpaceEngineersGame.cs`.
- Independent recomputation: `/tmp/.../scratchpad/recompute.py` (mass, thrust per direction from the built `bp.sbc` + `defs_stats.json`, not from the repo's
  MODELS code), `inertia.py` (box inertia with parallel axis), and C# probes (`probe/Probe*.cs`) linked against `out/sim/SimHost.exe` to read the sim's
  own mass, COM, inertia, acceleration, brownout and spin-down.
- Sim runs: none of the scenario matrix. Only deterministic single-ship physics probes (no seeds involved, 1 to 60 s of sim time each), to read numbers
  back out of the engine.

## Headline

1. The core rigid-body numbers are right. Mass, COM, inertia, thrust per direction, 100 m/s clamp, thrust through the COM and the gyro override
   controller all match the game data and the game source. MODELS.md acceleration figures reproduce exactly. The "about 6 m/s2" is simply
   thrust / component mass (upper bound, see PP-10).
2. **Biggest error found: the "functional" threshold of every block (PP-01).** The game keeps a block functional only while the *critical component
   itself* is intact; `defs_stats.json` records the integrity *before* it. Large thrusters and large batteries are disabled with 1.6 to 5.9 times less
   damage in the game than in the sim; small ion thrusters 1.22 times.
3. Power: the sim ignores two creative-mode facts and one logistics fact that change the endurance story (PP-02, PP-03).
4. Smaller damping/dampener/collision/split differences are listed below with effect sizes.

## Coverage: checked and found CORRECT

| Item | Sim value | Game value / source | Verdict |
|---|---|---|---|
| Cell sizes | 0.5 m / 2.5 m | `defs_stats.json` constants (Configuration.sbc) | correct |
| Linear speed cap | 100 / 100 m/s, small and large equal | `SmallShipMaxSpeed`/`LargeShipMaxSpeed` = 100 in the user's Environment.sbc; code: `MyGridPhysics.cs:73-80` | correct |
| How the cap is enforced | velocity vector scaled to the cap after integration; thrust keeps drawing power | `RigidBody.MaxLinearVelocity` (`MyGridPhysics.cs:266-278`) plus an explicit velocity clamp in `MyThrusterBlockThrustComponent.UpdateThrusts` (rescales velocity, does not reduce thrust) | correct |
| Angular cap | 2 pi small, pi large (rad/s) | Environment.sbc 360 / 180 deg/s; `MyEnvironmentDefinition` converts with `MathHelper.ToRadians`; `GetSmallShipMaxAngularVelocity` caps at 36000 deg | correct (unit confirmed) |
| Thrust force | `force_n` (small ion 14,400 N, large-grid small ion 345,600 N, ...), `min(override, max) * SuppliedRatio` | `MyThrust.ThrustForce`, `ApplyThrustModifiers` multiplies by supplied ratio | correct |
| Thrust application point | summed thrust acts at the centre of mass, no torque from thruster placement (`ThrusterTorque=false`) | `AddForce(ADD_BODY_FORCE_AND_BODY_TORQUE, thrust, null, null)`, null position = COM | correct (docs/sim-design.md 6.4 still says "at the thruster position", out of date, PP-12) |
| Thruster power | request = `max_power * override/force` per thruster; min power 2 W idle | `RecomputeOverriddenParameters`: `ThrustOverride / ThrustForce.Length * MaxPowerConsumption`; idle `MinRequiredPowerInput` | correct (sim adds min power, 2e-6 MW, negligible) |
| Thrust power group | Thrust is adaptible: all thrusters get the same ratio `remaining/demand` | distributor `RecomputeResourceDistributionPartial` | correct. Probe: Hornet burning 3 axes (6 thrusters) on 0.8 MW supply gets ratio about 0.62 (0.747 MW / 1.2 MW), as the game formula gives |
| Priority order | SafeZone 1, Defense 5, AIBlock 10, ..., Gyro 70, Thrust 80, DefenseAdative 90, BatteryBlock 95 | `ResourceDistributionGroups.sbc` via `defs_stats.json` | correct |
| Gyro override controller | MyGridGyroSystem.UpdateOverriddenGyros port: target = sum(F*v)/sum(F), ramp 120 frames, `diff*60/ramp / invI`, clamp to sphere of sum F, times Gyro supplied ratio, ramp restarted on every property write | `MyGridGyroSystem.cs` `UpdateOverriddenGyros`, `UpdateOverrideAccelerationRampFrames`, `RecomputeGyroParameters` | correct, including the `(pi/2)^2` ramp threshold and `GyroDirty` reset on setter |
| Gyro force | `force_n` small 448,000, large 33,600,000 N*m | `defs_stats.json`; `MyGyro.MaxGyroForce = ForceMagnitude * power * multiplier` | correct (sim applies `Power` only to the override weight, not to `sumAll`; irrelevant at 100 % power) |
| Mass | sum over blocks of sum(component count * component mass) | `MyCubeBlockDefinition.cs:540-606`: `mass += Count * Definition.Mass; Mass = mass` (destruction off, `SpaceEngineersGame.cs:76`) | correct. `defs_stats.json` `mass_kg` equals the component sum for all 1503 blocks (checked) |
| Block HP | `MaxIntegrity` = sum(count * unit_integrity), no explicit overrides exist in the data | `MyCubeBlockDefinition.cs:574,591`; 0 blocks in the data carry `MaxIntegrity` | correct |
| Damage multipliers | none applied to projectile damage (DeformationRatio unused, DamageRatio = 1 at full build) | `MySlimBlock.DoDamageInternal`: `damage *= DamageRatio` (2 - build/max = 1 when built); `DeformationRatio` only `if (Destruction \|\| ENABLE_VR_BLOCK_DEFORMATION_RATIO)`, both false | correct for weapons (but see PP-07 for collisions) |
| Inertia tensor | uniform box per block (m/3*(hb^2+hc^2)) + parallel axis, about the COM | `MyGridMassComputer`/`MyGridShape`: `HkInertiaTensorComputer.ComputeBoxVolumeMassProperties(size/2, mass)` combined | correct. Sim equals my independent calculation: Hornet 3344 / 3993 / 3144 kg*m2, Gnat 881 / 766 / 623, Longbow L 3.66e6 / 3.79e6 / 2.80e6 |
| Integration | semi-implicit Euler at 60 Hz, quaternion update, velocity clamp before position update | Havok at 60 Hz (`UPDATE_STEPS_PER_SECOND`); same discretisation of the gyro law | correct, stable (gyro law is dead-beat when ramp = 1) |
| PB scheduling | Update1 every tick, Update10/100 with random phase, Once cleared before Main | game semantics | correct (not changed) |
| Battery data | small battery 0.05 MWh / 0.2 MW, large 3 MWh / 12 MW, 3x2x3 battery 1 MWh / 4 MW | `defs_stats.json` | correct; charge efficiency 0.8 matches `StorePower` (`* 0.80f`) |
| Antenna / PB power | 4 kW per km, PB 0.5 kW | `UpdatePowerInput`: `radius/500 * MAX_REQUIRED_POWER_ANTENNA`; `MyEnergyConstants` value not retrievable here | plausible, recollection medium; irrelevant in size (<1 % of a 0.2 MW battery) |
| Ion thrust in space | 100 % | `EffectivenessAtMinInfluence` | correct for vacuum; sim has no atmosphere (all scenarios are space) |

### Recomputed MODELS.md figures (3 required ships plus Harrier)

| Ship | Blocks | Mass kg (mine = MODELS) | Thrust N per direction | Accel m/s2 mine / MODELS | Sim probe |
|---|--:|---|---|---|---|
| Gnat (Full) | 19 | 1941.0 / 1941 | F 14,400 B 14,400, others 0 | 7.42 / 7.4 | mass 1941.0, 7.42 along F |
| Hornet (Full) | 54 | 4451.0 / 4451 | 28,800 on each of 6 directions | 6.47 / 6.5 | 6.470 m/s after 1 s, reaches 100 m/s in 15.47 s |
| Longbow L (Full) | 52 | 110,180.8 / 110,181 | 691,200 on each of 6 | 6.27 / 6.3 | mass 110,180.8 |
| Harrier (Full) | 56 | 4693.0 / 4693 | F 57,600, others 28,800 | 12.27 and 6.14 / 12.3 and 6.1 | n/a |

Gyro authority (probe, one gyro at 100 %): Hornet 134 / 112 / 143 rad/s2 about x / y / z, Gnat 508 to 719, Longbow L 9.2 / 8.9 / 12.0. The Hornet
reaches the 6.28 rad/s cap in 0.05 s at full torque; in practice the 120-frame ramp (|diff| above pi/2) and the 100 %-ramped law limit turns to
about 0.5*diff rad/s2, which the sim reproduces.

## Findings

### PP-01 (high) Block "functional" threshold uses integrity BEFORE the critical component; the game uses integrity THROUGH it
- Sim: `DefDb.cs:132` `CriticalIntegrity = integrity_before_critical_component`; `BlockInst.IsFunctional = Hp >= CriticalHp` (`BlockInst.cs:45`).
  `tools/index_stats.py:331-345` computes it as the sum of components listed before the critical one, with the note at line 624-626 that the engine
  value is "approximately that (+/- one unit)".
- Game `[src]` (`MyCubeBlockDefinition.cs:560-581,613`): `integrity += Count * MaxIntegrity;` is executed first, then for the critical component
  `criticalIntegrity = integrity - 1;`, then `CriticalIntegrityRatio = criticalIntegrity / MaxIntegrity`. `MyComponentStack.IsFunctional`
  (`:134`) is `IntegrityRatio > CriticalIntegrityRatio`. So the critical component's own integrity is included, and the "+/- one unit" statement is
  only true when the critical component is worth about 1 HP (Computer). It is not for thrusters (Thrust component 30 each).
- Effect (damage needed to switch the block off, sim vs game, from `defs_stats.json`):
  small ion thruster 160 vs 131 HP (1.22x), large-grid small ion thruster 4000 vs 1601 (2.5x), large-grid large ion thruster 34,700 vs 5,901 (5.9x),
  large battery 10,625 vs 6,626 (1.6x), reactor small 210 vs 201, gyro, small battery, gatling, railgun, PB, camera within 1 to 3 %.
  So large ships keep thrust far too long in the sim (Longbow L, Basilisk, the large pirate ship with 12 `LargeBlockSmallThrust`), and Hornet-class ships keep each thruster about 18 % too long.
  Damage-to-cripple results (disable fraction, time to disable) for large-grid targets are biased in the drones' favour; the enemies' too.
- Fix: in `tools/index_stats.py` store `cum_through_critical - 1` (and regenerate `defs_stats.json`) or compute it in `DefDb.Load` from `components`
  (cumulative `count*unit_integrity` through the critical group, minus 1), and test `Hp > CriticalHp`. Re-run the large-grid scenarios.
- Confidence: high that the 2019 source says this; medium-high that it is unchanged (I know of no change to the formula).

### PP-02 (medium) Creative mode: batteries never discharge in the game; the sim always drains
- Sim: `PowerSystem.Update` drains `StoredMWh` every tick in every mode; no creative-mode switch exists in `SimSettings`.
- Game `[src]` (`MyBatteryBlock.UpdateAfterSimulation100`, lines ~322-360): in `MySession.Static.CreativeMode` the battery never calls `ConsumePower`; an Auto/Recharge battery
  *stores* `ProductionToCapacityMultiplier(3600) * Max / 8` MW worth of charge (times 0.8), i.e. it refills in about 10 s and never drains. In survival the
  drain runs once per 100 ticks with the output of that moment.
- Effect: in a creative world the battery capacity is never limiting; only the output cap (MW) is. `unknowns.md` endurance figures ("30 minutes on one axis",
  "Gnat 15 minutes", "RTB Battery Fraction") are survival numbers. In the sim's 5-minute matches the drain is at most about a third of a Hornet pack (0.8 MW
  for 300 s is 0.067 MWh of 0.2), so scenario results change little; `endless` runs and RTB logic are affected.
- Fix: add `SimSettings.CreativeBatteries` (default = what the owner's world is; research.md says creative is assumed for ammo) that skips the drain and keeps
  charge topped up. Record the owner's mode in `docs/sim-design.md`.
- Confidence: medium-high (2019 source, behaviour I also remember from play).

### PP-03 (medium) Printed drones: battery charge claims in unknowns.md are unsupported; scripts never charge batteries; the sim cannot model charging
- `packages/unknowns.md` ("A printed drone leaves with a nearly empty battery ... starts at 30 %"): the projector builds a welded block from a **clone of the original
  blueprint block** (`MyProjectorBase.Build`, lines ~1285-1305, `objectBuilder = blockBuilder.Clone(); SetupForProjector()`; only Owner, ShareMode and EntityId are reset).
  `MyBatteryBlock.Init` uses `ob.CurrentStoredPower` when it is >= 0 (`:209-212`). The generator writes `CurrentStoredPower = max` (`tools/designs/_common.py:64`).
  So a projector-built battery most likely carries the blueprint's *full* charge. (The same mechanism explains why a welded PB can keep its `Program` text,
  tests T5/T6.) The sim already behaves this way (`GridFactory` reads `CurrentStoredPower`), so sim and source agree; the documentation is the odd one out.
- The CHARGE phase of `HiveMotherAI` cannot charge anything: the only `ChargeMode` write in the printer script is `Auto` (`HiveMotherAI.cs:860`), and in the game Auto
  batteries do not charge each other (the distributor treats all batteries as one storage group fed from non-storage sources, `RecomputeResourceDistributionPartial`).
  Printers have no generator. Charging would need drone batteries in Recharge and printer batteries in Discharge. The sim has no battery charging at all
  (`PowerSystem` header: "Battery recharge is not modelled"; `BatteryUnit.MaxInputMW` unused), so it cannot test this either.
- Fix: correct the unknowns text; if charging is wanted, set drone batteries to `ChargeMode.Recharge` and printer batteries to `Discharge` during CHARGE, restore Auto
  at release (the existing line 860 already restores Auto), and add input/0.8-efficiency charging to `PowerSystem`.
- Confidence: medium (projector path read in source; creative-mode refill (PP-02) also makes the point moot in a creative world).

### PP-04 (medium) Idle gyro damping: sim is about 4.7 times too weak on small grids (multiplier 5 and MAX_SLOWDOWN 0.8 missing)
- Sim: `Physics.ApplyGyros` idle branch: torque `-w/invI` clamped to `sumAll`, times `GyroMaxSlowdown = 0.93`. A 1 rad/s spin decays with time constant about 1.08 s (probe: 0 after 60 s; half-life 0.75 s).
- Game `[src]` (`MyGridGyroSystem.UpdateGyros`): `SlowdownTorque = -w * torqueSlowdownMultiplier / invTensor`, clamped to `m_maxGyroForce * multiplier`, times
  `slowdown = (1-MAX_SLOWDOWN)*(1-supplied)+MAX_SLOWDOWN`. `MyFakes.SLOWDOWN_FACTOR_TORQUE_MULTIPLIER = 5.0` (small), `..._LARGE_SHIP = 2.0` (`MyFakes.cs:700-701`).
  `MAX_SLOWDOWN = MyFakes.WELD_LANDING_GEARS ? 0.8f : 0.93f` and `WELD_LANDING_GEARS = true` (`MyFakes.cs:529`), so 0.8, not 0.93.
  Game time constant on a small grid is about 1/(5*0.8) = 0.25 s (large: 1/(2*0.8) = 0.6 s).
- Effect: only when no gyro is in override (script released the gyros, script crashed or stopped, wrecks with surviving gyros, scriptless ships using only
  gyro damping). While any gyro override is active the game uses the override law only (no idle slowdown) and the sim matches.
- Fix: in `ApplyGyros` idle branch multiply the demand by 5 (small) or 2 (large), clamp to `sumAll * multiplier`, and set `GyroMaxSlowdown = 0.8`.

### PP-05 (low) No Havok angular damping
- Sim: no damping anywhere; a spinning body with dead gyros spins for ever.
- Game `[src]`: `RigidBody.AngularDamping = MyPerGameSettings.DefaultAngularDamping` (0.1) and `LinearDamping = 0.0` for every ship (`MyGridPhysics.cs:236-237`, `MyPerGameSettings.cs:215-216`).
  About 10 %/s, i.e. e-folding about 10 s: a tumbling wreck at 1 rad/s is at 37 % after 10 s.
- Effect: tumbling wrecks (the "over-led shots at a tumbling wreck" scenario in HANDOFF 3) rotate too long in the sim. Medium confidence on the exact decay law (Havok applies
  `1 - damping*dt` or `(1-damping)^t`; both give about 0.1/s).
- Fix: after the gyro torque, `w *= 1 - 0.1*dt` (ships only).

### PP-06 (low-medium) Inertial dampener model differs (stopping law and per-axis disable)
- Sim (`Physics.PrepareThrusters`): demand `m*|v|/dt*gain`, i.e. dead-beat stop within one tick when thrust suffices; non-overridden thrusters keep damping even when other thrusters on the same axis are overridden.
- Game `[src]` (`MyEntityThrustComponent.ComputeBaseThrust`): `slowdownAcceleration = -localVelocity / 0.5 s`, force `m * that` (exponential approach, time constant 0.5 s), clamped to the available
  thrust; `slowdownControl = IsZeroVector(controlThrust) * IsZeroVector(fuelData.ThrustOverride)`: if **any net thrust override exists on an axis, dampening on that whole axis is off** for every thruster.
- Effect: the drone scripts set `DampenersOverride = false` while flying and zero all overrides on release, so they are unaffected. It matters for scriptless/AI-block ships and for the
  printer undock "nudge" (`HiveMotherAI.Nudge` overrides the printer's thrusters on one axis; in the game the printer's dampeners stop fighting that axis, in the sim the opposite thrusters brake it).
- Fix: use `-v/0.5*m` and disable the whole axis if any thruster on it has an override.

### PP-07 (medium) Collision damage is a different model, not an energy model
- Sim: HP damage `0.0005 * 0.5 * mu * vn^2` shared by blocks within 1.5 m (`SimSettings.cs:57,65`), impulses with restitution 0.2; scale constants are `[I]` (assumed).
- Game `[src]` (`MyGridPhysics.PerformDeformation`, `ApplyDeformation`): for separating velocity above 2 m/s the deformation offset is `(0.3 + max(0,(|v| - BreakImpulse/m)/15)) * 3 m` (cap 20 m),
  every block whose `max(lowerRatioLimit, block.DeformationRatio) * deformation > 0.7 * gridSize` is removed outright (not HP-based), the soft area is 1.2 m (small) or 4 m (large) planar and
  1.5*offset deep, and a `GRID_DESTRUCTION` explosion of radius `min(max(gridSize, offset), 10)` is generated. `block.DeformationRatio` (armor 0.1 to 0.46 in `defs_stats.json`) is what decides, and is unused by the sim.
  Applies only with "destructible blocks" on (default).
- Effect: rams at 10 to 30 m/s remove columns of blocks several metres deep in the game; the sim spreads a few hundred HP over the contact patch (Hornet-on-Hornet at 20 m/s: about 220 HP per ship).
  Unvalidated scale; matters for kamikaze/Stinger, docking collisions and rams.
- Fix: model it as destruction by depth (offset formula above, remove blocks inside the soft volume with `DeformationRatio` scaling) or at least calibrate `CollisionDamageScale` against one in-game ram.

### PP-08 (medium) Grid splitting: connectivity by face contact (game: mount-point neighbours) and identity by mass (game: block count)
- Sim: `Connectivity.Visit` joins any two face-adjacent cells; the heaviest component keeps the body (`SplitIfNeeded`).
- Game `[src]`: `MyDisconnectHelper` walks `MySlimBlock.Neighbours` (blocks connected through compatible mount points, not mere contact) and keeps the group with the **most blocks** that has physics
  (`m_largestGroupWithPhysics`); the other groups become new grids.
- Effect: blocks that touch but have no shared mount point (a gun's side face on an armour block, thruster flanks, cameras) stay attached in the sim after their real mount neighbour dies, so the sim
  fragments less than the game; and which fragment keeps the original grid (and therefore the PB, `Me.CubeGrid`) can differ when the heavy part has fewer blocks.
- Fix: build adjacency from the mount points in `defs.json` (the generator's validator already does this), and choose the main body by block count.

### PP-09 (low) Power allocation corner cases
- A non-adaptible group that cannot be fully served starves **all lower-priority groups** in the game (`RecomputeResourceDistributionPartial`: "None of the lower priority groups will get any power either"); the sim sets only that group's ratio to 0 and
  lets later groups use the remaining power (`PowerSystem.cs` loop). Rare brownout case.
- Source order: the game supplies loads from batteries first (storage), then reactors by group priority (SolarPanels 1, Battery 2, Reactors 3); the sim lets generators carry the load first (`fromBat = used - gen`). Only matters for enemy ships that have reactors plus batteries.
- Solar panels, wind turbines are ignored (`DefDb` sets `GeneratorMW` only for Reactor/HydrogenEngine).

### PP-10 (low) Inventory mass (ammo, cargo) is not added
- Sim: `BlockInst.Mass = Def.MassKg` ("+ inventory mass later").
- Game `[src]` (`MyGridShape.UpdateMassFromInventories`): physical mass includes every inventory's `CurrentMass / InventorySizeMultiplier`. Gatling drum 35 kg (140 rounds, 16 L), a small gatling holds
  64 L = 4 drums = 140 kg; small rocket launcher 240 L / 60 L = 4 rockets of 45 kg = 180 kg; small railgun 48 L / 8 L = 6 slugs of 10 kg = 60 kg (`defs_stats.json`).
- Effect: a Hornet with two full gatlings is about 4731 kg instead of 4451 (+6 %), so the 6.47 m/s2 becomes about 6.1; Wasp (two rocket launchers) about +8 %. MODELS.md accelerations are upper bounds.
  With `InfiniteAmmo` the guns may hold little, so the true figure depends on the world.

### PP-11 (info) Gyro pitch sign rests on in-game flight evidence, not on the source
- Keen's 2019 `MyGyro.cs` exposes `Pitch => +stored.X`, `Yaw => -stored.Y`, `Roll => -stored.Z`; the scripts use `Pitch = -l.X` (all three negated) and the user confirmed flight and aiming (HANDOFF 3). The sim defaults to `(-1,-1,-1)`.
  docs/sim-design.md 6.3 already records this and test T10. No change needed; the pitch evidence is indirect (arbitrary targets, all axes exercised).

### PP-12 (low) Documentation out of date
- `docs/sim-design.md` 6.4 says thrusters push "at the thruster position (torque from offset)"; the code default is the COM (`ThrusterTorque=false`), which is what the game does `[src]`.
- Same section says gyro idle slowdown "like the game"; see PP-04.

### PP-13 (low) Full inertia tensor vs the game's diagonal
- The game reads only `InverseInertiaTensor` M11, M22, M33 for the override law (comment "Only the InverseInertiaTensor seems to be correct"); Havok stores a diagonal inertia. The sim uses the full tensor including
  products of inertia. Probe: Hornet products are below 1 % of the diagonal, Longbow L up to 11 % (Iyz 3.1e5 vs 3.8e6). Small effect on turn convergence only.

### PP-14 (info) Thruster flame damage not modelled
- `MyThrust.ThrustDamage` damages blocks in the flame every 10 ticks (`30 * FlameDamage` HP per block hit; small ion 0.1 so 3 HP per update, only for blocks with `DeformationRatio` above 0.25 on small grids; large-grid flames are stronger). The generator's `keep_clear` rule
  keeps friendly blocks out of the flame; the sim has no flame damage. Negligible for enemies.

### PP-15 (info) Timing latency of PB commands
- The sim applies script commands in the same tick (scripts, then power, then thrust). The game applies them at the grid system's next update, up to 1 tick (16.7 ms) later. Negligible.

## Items I could not check
- `MyEnergyConstants` antenna/PB power values (file not retrievable); `EntityComponents.sbc` railgun capacitor numbers (`RailgunRechargeDraw 3.6 MW`, `ChargeMultiplier 0.8`, capacity 0.016 MWh): they are not in `defs_stats.json` and I did not have the raw ref folder.
- Current-version differences: all `[src]` statements come from the 2019 public repository.
- Whether the owner plays creative or survival (decides PP-02 and the battery story in PP-03).
