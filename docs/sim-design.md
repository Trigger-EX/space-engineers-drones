# Simulator design (SeSim)

Goal: run the **real, unmodified** `scripts/*.cs` against a functional implementation of the in-game API, on bodies built from
the validated blueprints (`out/Blueprints/<Name>/bp.sbc`) and `defs_stats.json`, for thousands of seeded fights. A fix found in
simulation is a fix to the shipped script. Implementers read only this document and the skeleton in `sim/`.

Status (architect pass): skeleton compiles (`sim/build.sh`), `stubs.cs` carries real VRageMath/MyIni/MyGridProgram behaviour,
all 14 scripts compile into `out/sim/scripts/*.dll`, `mono out/sim/SimHost.exe selftest` passes (maths, orientation, gyro sign,
script assembly contract). Everything else throws `NotImplementedException`.

## 0. Ownership (each file has exactly one owner; the header comment of every file names its slot)

| Slot | Scope | Files |
|---|---|---|
| **A** physics + core + blueprint loader | world, tick loop, bodies, mass/inertia, forces, integration, power, connectivity/splitting, raycast, ids, RNG, settings, events, blueprint + defs loading | `sim/src/Core/{World,Body,BlockInst,Units,Physics,PowerSystem,Connectivity,Raycast,GridFactory,OrientMath,DetRng,SimSettings,Events,MiniJson,SelfTestA}.cs`, `sim/src/Blueprint/{BlueprintLoader,DefDb}.cs` |
| **B** block API + GTS + IGC + sensing + text | every `IMy*` implementation, terminal properties/actions, grid/slim API, GridTerminalSystem, IGC, camera raycast, sensor, MyDetectedEntityInfo builder, API factory; `stubs.cs` additions | `sim/src/Api/*.cs` (TerminalBlock, TerminalProps, CubeGridApi, ThrustApi, GyroApi, CameraApi, SensorApi, BatteryApi, WeaponApi, TurretApi, MergeApi, ProjectorWelderApi, ProgrammableBlockApi, RemoteControlApi, AntennaApi, Igc, TextSurfaceApi, GridTerminalSystemApi, WarheadApi, Detection, BlockApiFactory, SelfTestB), `stubs.cs` |
| **C** combat | weapon state machines, projectiles, damage, explosions, warheads, turret AI, pirate ship + brain, vanilla AI (Midge) | `sim/src/Combat/{Weapons,Projectiles,Damage,Explosions,TurretAi,PirateAi,VanillaAi,SelfTestC}.cs` |
| **D** runner | script compile pipeline + host + runtime info, scenarios + unfair-fight generator, metrics + termination, JSON/CSV output, parallel runner, CLI, build script, script shim | `sim/src/Run/*.cs` (ScriptCompiler, ScriptHost, RuntimeInfo, Scenario, TeamSpec, ScenarioGen, Metrics, JsonOut, ParallelRunner, Program, SelfTestD), `sim/shim/DetRandom.cs`, `sim/build.sh` |

Rules: edit only your files. Need a member/field in another slot's file? Use what exists; if it is missing, add it to your
own file (extension method / helper) or ask that slot. The cross-slot contracts below (fields named in the skeleton) are fixed;
renaming one needs all affected slots. `SimSettings` has one section per slot: add fields only in your section.
`EventKind`: append values only (slot A owns the enum, slot D consumes).

## 1. Architecture

```
            out/Blueprints/<Name>/bp.sbc      defs_stats.json        scripts/*.cs
                     |                              |                       |
           Blueprint.BlueprintLoader          Blueprint.DefDb        build.sh: wrap + mcs
                     \______________ ______________/                        |
                                    v                              out/sim/scripts/<Name>.dll
                          Core.GridFactory.Spawn  ---------------->  Run.ScriptHost (one Program per PB)
                                    |                                         |
  Core.World  Bodies[] -- BlockInst[] -- units (Thruster/Gyro/Battery/Merge/Sink [A], Weapon/Turret/Warhead [C])
              |                       \-- Api objects (SimThrust, SimGyro, SimCamera, ... [B])  <-- scripts call these
              |-- Api.IgcHub [B]   -- Combat.CombatSystem + ProjectileSystem [C]   -- IShipBrain list (pirate, vanilla AI) [C]
              |-- Events -> Run.Metrics [D]
  Run.ScenarioRunner.RunOne -> World.Step() x N -> RunResult -> JsonOut (runs.jsonl, summary.csv); ParallelRunner: 4 threads
```

Assemblies (all built by `sim/build.sh`, plain `mcs`, Mono 6.8):

| Output | Source | Notes |
|---|---|---|
| `out/sim/SeApi.dll` | `stubs.cs` | the in-game API; referenced by engine and scripts (one shared identity, so interfaces match) |
| `out/sim/SimShim.dll` | `sim/shim/*.cs` | `SimShim.DetRandom` for scripts |
| `out/sim/SimHost.exe` | `sim/src/**/*.cs` | C# 7.2, `-optimize+`, references System.Xml + System.Core (System.Xml.Linq is NOT installed) |
| `out/sim/scripts/<Name>.dll` + `manifest.tsv` | `scripts/*.cs` wrapped | C# 6 like the PB (`-langversion:6`) |

Namespaces: `SeSim.Core`, `SeSim.Blueprint`, `SeSim.Api`, `SeSim.Combat`, `SeSim.Run`. Everything runs in one process; worker
threads each own a whole `World`.

## 2. Class list per file

Signatures are in the skeleton; this is what each class is for.

**Core [A]**
- `SimSettings`: all constants/assumptions (time, physics, gyro signs, power, sensing, combat, script fakes); `ApplyOverride(name, value)` for `--kebab-case value` CLI flags.
- `DetRng`: xoshiro256** with SplitMix64 seeding, `Fork(salt)`, static `Mix(seed, salt)` (implemented).
- `OrientMath`: Base6 parsing, block rotation matrix, rotated size, Max cell, block center, local matrix (implemented, see section 6).
- `BlockInst`: placed block (def, source data, ids, cells, local matrix, mass, hp, owner, body, unit refs, API ref). `MergeUnit`: merge pair state.
- `Units`: `ThrusterUnit`, `GyroUnit` (+ `TargetGridLocal` sign convention, implemented), `BatteryUnit`, `PowerSink`.
- `Body`: rigid body = one grid; occupancy array, unit lists, mass properties, state, cached matrices, `RemoveBlock`.
- `Physics`: `ApplyThrusters`, `ApplyGyros` (Keen port), `Integrate`, `ResolveCollisions`.
- `PowerSystem`: per-body priority power allocation and battery drain.
- `Connectivity`: components and grid splitting (merge pairs are edges only while connected).
- `Raycast`: block-exact DDA ray queries (`RayBody`, `RayWorld`, `RayWorldAll`) shared by cameras, turrets, projectiles.
- `GridFactory`: blueprint -> Bodies (`Spawn`, `SpawnProjected`), id remap, units, API objects, combat units, merge pairs.
- `World`: tick loop and subsystem references; `TeamTable` (identities, relations); `IdGen`; `IShipBrain`.
- `Events`: `EventKind`, `SimEvent`, `IEventSink`, `NullEventSink`.
- `MiniJson`: load-time JSON reader for `defs_stats.json`.
- `SelfTestA`: executable conventions (implemented).

**Blueprint [A]**: `BlueprintLoader` (`BlueprintData`/`GridData`/`BlockData`, process cache), `DefDb` (`BlockDef`, `WeaponDef`, `AmmoDef`, power priorities).

**Api [B]**: `SimCubeBlock`/`SimTerminalBlock`/`SimFunctionalBlock` bases (+ `SimGenericTerminal`/`SimGenericFunctional`),
`SimProperty<T>`/`SimAction`/`TerminalRegistry`, `SimCubeGrid`/`SimSlimBlock`/`SimInventory`, `SimThrust`, `SimGyro`, `SimCamera`,
`SimSensor`, `SimBattery`, `SimUserGun` (+ `SimGatlingGun`, `SimMissileLauncher`, `SimRailgun`), `SimLargeTurret` (+ gatling/missile),
`SimTurretControlBlock`, `SimMergeBlock`, `SimProjector`, `SimShipWelder`, `SimProgrammableBlock`, `SimShipController`/`SimRemoteControl`,
`SimRadioAntenna`, `IgcHub`/`SimIgc`/`SimBroadcastListener`/`SimUnicastListener`, `SimTextSurface`, `SimGridTerminalSystem`,
`SimWarhead`, `Detection`, `BlockApiFactory`, `SelfTestB`.

**Combat [C]**: `WeaponUnit` + `CombatSystem` (Weapons.cs), `ProjectileSystem`, `Damage` (+ `DamageKind`), `Explosions` + `WarheadUnit`,
`TurretUnit`, `PirateShipFactory` + `PirateBrain`, `VanillaAiBrain`, `SelfTestC`.

**Run [D]**: `ScriptCompiler` + `ScriptRegistry`, `ScriptHost` + `PbRunner`, `SimRuntimeInfo`, `ShipSpec`/`Scenario`/`ShipInstance`/`ScenarioRunner`,
`TeamSpec`, `ScenarioGen`, `ShipStats`/`RunResult`/`Metrics`, `JsonOut`, `Job`/`ParallelRunner`, `SimProgram` (CLI), `SelfTestD`; `SimShim.DetRandom`.

## 3. Script host

### 3.1 Compile
`sim/build.sh` wraps every `scripts/<Name>.cs` exactly like `tools/compile_check.sh` (same `using` block), plus ONE extra line
`using Random = SimShim.DetRandom;`, then `public partial class Program : MyGridProgram {` + text + `}`, compiled with
`mcs -langversion:6 -target:library -optimize+ -r:SeApi.dll -r:SimShim.dll` into `out/sim/scripts/<Name>.dll`, one assembly per
script (each defines its own global `Program`, so the same type name can be loaded many times). `manifest.tsv` maps
`ScriptKey(text)` = sha1 of the text with `\r` removed and trailing whitespace trimmed -> dll. The script text is never changed;
the alias only redirects `new Random()` (time-seeded in .NET) to a deterministic subclass of `System.Random` (verified by
`selftest`: Hornet's `_rng` field is `SimShim.DetRandom`).

### 3.2 Which script a PB runs
The PB runs the text of its own blueprint `<Program>` (`BlockData.Program`). `ScriptRegistry.GetProgramType(text)` looks up the
key; unknown text (edited blueprint) is compiled on demand to `out/sim/scripts/cache/<key>.dll` under a process-wide lock. All
current blueprints' programs match a `scripts/*.cs` file byte-for-byte (checked during this pass). PB without program = no runner.
`out/Blueprints` is rebuilt by `python3 -I tools/build_all.py` only when missing (takes ~3 min).

### 3.3 Instantiate and run (game-faithful)
1. `obj = FormatterServices.GetUninitializedObject(type)` cast to `Sandbox.ModAPI.IMyGridProgram`.
2. Set `Runtime` (`SimRuntimeInfo`), `Storage` (blueprint `<Storage>` or ""), `Me` (the `SimProgrammableBlock`), `Echo`
   (stores text into `PbRunner.EchoText`, shown as `DetailedInfo`), `GridTerminalSystem` (`SimGridTerminalSystem` of this PB),
   `IGC_ContextGetter = () => pb.Igc`.
3. `SimShim.DetRandomSeed.Set(DetRng.Mix(worldSeed, (ulong)pb.EntityId))` (thread-static; set again before every Main call
   that could construct a Random, i.e. always - it is one store).
4. Invoke the parameterless constructor on `obj` (`type.GetConstructor(Type.EmptyTypes).Invoke(obj, null)`). The base
   `MyGridProgram()` constructor binds `Main(string, UpdateType)` / `Main(string)` and `Save()` delegates (in `stubs.cs`).
   Constructor exception -> PB dead, `EventKind.ScriptException`.
5. Scheduling each tick (World phase 1), PBs in fixed order (ship index, block index): pending argument runs first (one `Main`
   per argument: scenario commands use `UpdateType.Terminal`, `TryRun` uses `Script`, `ApplyAction("Run")` uses `Trigger`); then
   one `Main("", flags)` where flags = OR of due `Update1` (every tick), `Update10` (`(tick + phase10) % 10 == 0`), `Update100`
   (`(tick + phase100) % 100 == 0`), `Once` (requested during the previous run; cleared before the call). phase10/100 come from the
   PB EntityId (deterministic stagger like the game). `Runtime.UpdateFrequency` is read at scheduling time, so a script that changes
   it takes effect next tick (game behaviour).
6. `Runtime.TimeSinceLastRun` = sim time since this PB's previous `Main` call (TimeSpan.Zero on the first call);
   `LastRunTimeMs` = `SimSettings.FakeLastRunTimeMs` (0.05), `CurrentInstructionCount` = 0, `MaxInstructionCount` = 50000.
   Real CPU per call is measured with a Stopwatch for metrics only.
7. An exception escaping `Main` terminates the PB permanently (the game stops the script); logged once with message and the
   first stack frame. All drone scripts catch internally, so this is rare and always a real bug.
8. `ApplyAction("Recompile")` (HiveMother) = new instance via steps 1-4, keeping `Storage`. `Save()` is called at the end of a
   run only when `--call-save` is given (the game calls it on world save, which never happens in a fight).
9. PB destroyed, below critical integrity or disabled -> no more runs (`ScriptHost.Detach`). Its grid changing (split/merge) is
   visible to the script through `Me.CubeGrid` exactly like the game.

## 4. API implementation

### 4.1 stubs.cs rules
`stubs.cs` is compiled into `SeApi.dll` and is both the compile-check stand-in and the runtime API. Only members the real API
has may be added; every addition needs a source (MDK-SE wiki page, malforge pbapi, or Keen source) noted in
`docs/script-audit.md`. Verified this pass against MDK-SE wiki pages: IMyThrust (7 members), IMyGyro (5), IMyCameraBlock
(6 properties, CanScan x3, Raycast x3, TimeUntilScan), MyGridProgram (properties with protected setters, IGC getter-only),
MyDetectedEntityInfo (readonly fields, ctor order, Position = BoundingBox.Center, IsEmpty), IMyShipController,
IMyIntergridCommunicationSystem, IMyBroadcastListener/IMyMessageProvider, TerminalPropertyExtensions (namespace
`Sandbox.ModAPI.Interfaces`, SetValueFloat/Bool/GetValue...), TerminalBlockExtentions (ApplyAction, HasAction, GetId).
From memory (not re-fetched, flagged): Sandbox.ModAPI.IMyGridProgram shape, ITerminalAction/ITerminalProperty<T> members,
MyIni details, MyFixedPoint scale, IMyBatteryBlock ChargeMode, IMyRadioAntenna, IMySensorBlock extends, IMyGridTerminalSystem overloads,
`IsEmpty() => TimeStamp == 0`.
Changes this pass that keep all 14 scripts compiling: VRageMath types got real maths (row-vector `MatrixD` with M11..M44 and
Forward=-Z, XNA formulas; `Normalize(zero)` = NaN like the game); `SetValueFloat/SetValueBool` moved to
`Sandbox.ModAPI.Interfaces.TerminalPropertyExtensions` and now dispatch through `GetProperty(id).Cast<T>().SetValue(...)`;
`ApplyAction` dispatches through `GetActionWithName(name).Apply(block)`; `MyGridProgram` uses real properties and implements
`Sandbox.ModAPI.IMyGridProgram`; `MyDetectedEntityInfo` became immutable with the real constructor; `MyIni` parses and writes.

### 4.2 Block classes (slot B) - behaviour contract
- **Common**: `IsWorking` = `IsFunctional && Enabled(if functional) && power SuppliedRatio > 0` for powered blocks.
  `IsSameConstructAs` = same Body. `BlockDefinition.ToString()` = `"MyObjectBuilder_<Type>/<Subtype>"` (scripts test it with
  `Contains("OffensiveCombat")`). `CubeGrid` = the body's `SimCubeGrid`. `OwnerId` = team identity. `WorldMatrix` = block local
  matrix * grid world matrix. `GetPosition()` = block center.
- **Thrust**: `ThrustOverride` N (0..MaxThrust), `ThrustOverridePercentage` (0..1, = N/MaxThrust), `MaxEffectiveThrust` = MaxThrust in
  space; `CurrentThrust` from physics. Thrust force acts along the block's **Backward** (exhaust = block Forward; HANDOFF 4.4).
- **Gyro**: stores raw Pitch/Yaw/Roll (rad/s, clamped to the grid's max angular speed), `GyroPower` 0..1, `GyroOverride`. Sign
  convention is applied in `GyroUnit.TargetGridLocal` only (section 6.3).
- **Camera**: charge 2.0 m per ms while `EnableRaycast` and working (2 km/s), unlimited accumulation; cone 45 deg; scan consumes the
  requested distance; origin = camera center; own body excluded; result from `Detection` (grid id, hit point, velocity, relation,
  AABB, `TimeStamp = TimeMs+1`). `Raycast(distance, pitch, yaw)`: degrees about camera Right then Up. `CanScan(target)` checks
  charge >= distance and cone.
- **Sensor**: box field from the six extends (0.1..50 m) in sensor frame; refresh every 10 ticks; Detect* size and relation filters.
- **Battery**: MW/MWh from `BatteryUnit`; `ChargeMode`.
- **Guns**: actions `Shoot_On`, `Shoot_Off`, `ShootOnce`, `Shoot` toggle, `OnOff`, `OnOff_On`, `OnOff_Off`; property `Shoot` (bool).
- **Turrets / turret controller**: `HasTarget`, `GetTargetedEntity()` from `TurretUnit` (slot C).
- **Merge**: `IsConnected`; `Enabled=false` releases (section 7.6).
- **PB**: 2 surfaces (index 0 = main screen), `Run`, `Recompile`, `TryRun`.
- **Remote control** (ship controller, not functional): velocities (world, angular right-handed rad/s), `CenterOfMass`, mass,
  zero gravity, `DampenersOverride` = body dampeners.
- **Antenna**: `Radius` (blueprint BroadcastRadius; 0..50000), `EnableBroadcasting`; power radius/250 kW.
- **Warhead**: `IsArmed`, `Detonate` (only when armed), countdown.
- **Text surfaces**: keep last text, no rendering.
- **GridTerminalSystem**: all live terminal blocks of the PB's Body in block order; list cleared first; `is T` then predicate.

### 4.3 IGC
Messages sent during tick t are queued in `IgcHub` and delivered in phase 2 after all scripts ran, so every receiver sees them
on tick t+1 (independent of PB order). Reach (`AntennaRelay`): same Body, or a chain of working broadcasting antennas where each
hop's sender radius >= hop distance (BFS, rebuilt once per tick when messages are pending; receiving antenna only needs to be
working). `CurrentConstruct`/`ConnectedConstructs`: same Body. A PB does not receive its own broadcast. Listener queue cap 25,
overflow drops the oldest [I]. `Me` = PB EntityId. `MyIGCMessage.Source` = sender `IGC.Me`. Payload type check: anything not
immutable (e.g. `List<>`) throws `InvalidOperationException` in the sender (the game rejects it too).

### 4.4 Terminal property / action ids (only these; unknown -> null)
- Functional blocks: action `OnOff`, `OnOff_On`, `OnOff_Off`; property `OnOff` (bool).
- Sensor: `Left`, `Right`, `Bottom`, `Top`, `Back`, `Front` (float); `Detect Players`, `Detect Floating Objects`, `Detect Small Ships`,
  `Detect Large Ships`, `Detect Stations`, `Detect Subgrids`, `Detect Asteroids`, `Detect Owner`, `Detect Friendly`, `Detect Neutral`,
  `Detect Enemy` (bool). (Scripts try `LeftExtend` first and fall back to `Left`: the first lookup returning null is intended.)
- Thrust: `Override` (float, Newtons). Gyro: `Override` (bool), `Power`, `Yaw`, `Pitch`, `Roll` (float).
- Guns: `Shoot` (bool); actions above. PB: actions `Run`, `RunWithDefaultArgument`, `Recompile`.
- Battery: `ChargeMode` not exposed as property (API property only). Antenna: `Radius` (float), `EnableBroadCast` (bool) [I].

## 5. Time stepping and tick order

Fixed 60 Hz (`SimSettings.Dt = 1/60`), **no sub-stepping**: the game runs physics and PB updates at 60 Hz; projectiles use swept
segments (13.3 m/tick at 800 m/s) so nothing tunnels. `World.Step()`:

| Phase | Owner | Work |
|---|---|---|
| 1 | D | `ScriptHost.RunDue()`: argument runs, then due PBs (section 3.3) |
| 2 | B | `IgcHub.Deliver()` |
| 3 | C | `IShipBrain.Tick` (pirate, vanilla AI), `CombatSystem.UpdateTurrets` (target refresh every 10 ticks, aim) |
| 4 | C | `CombatSystem.UpdateWeapons` (fire -> `ProjectileSystem.Spawn`), `UpdateWarheads` (countdowns) |
| 5 | A | `PowerSystem.Update(dt)` per body (requests set by units during phases 1-4 and last tick's thrust) |
| 6 | A | per body `Physics.ApplyThrusters`, `ApplyGyros`, `Integrate`; then `ResolveCollisions` |
| 7 | C | `ProjectileSystem.Step` (hits, damage, explosions) |
| 8 | A | apply removals: `Connectivity.SplitIfNeeded`, `RecomputeMassProperties` for dirty bodies, `PowerSystem.Rebuild` |
| 9 | B | camera charge (`SimCamera.TickCharge`), sensors (`Poll` every 10 ticks) |
| 10 | A/D | `Tick++`, `Events.EndTick(world)` (metrics sampling, termination) |

PBs therefore read the state integrated at the end of the previous tick, like in game (scripts run before physics).

## 6. Frames, orientation maths, physics

### 6.1 Frames
VRageMath row-vector convention: `Transform(v, M) = v * M`; matrix rows are Right (M11..M13), Up (M21..), Backward (M31..),
Translation (M41..); Forward = -Backward. Grid frame: +X Right, +Y Up, +Z Backward; cell (x,y,z) center at (x,y,z)*cellSize
(0.5 m small, 2.5 m large). Body state: `Position` = world COM, `Rotation` (QuaternionD), `WorldMatrix` = rotation with
Translation = world position of the grid origin = `Position - TransformNormal(ComLocal, RotMatrix)`. Angular velocity is world-frame,
right-handed.

### 6.2 Block orientation (implemented in `OrientMath`, tested in `SelfTestA`)
Blueprint `<BlockOrientation Forward="X" Up="Y"/>` (absent = Forward/Up). Block rotation R: `R.Forward = dir(Forward)`,
`R.Up = dir(Up)`, `R.Right = Cross(R.Forward, R.Up)`; block-local vector -> grid: `TransformNormal(v, R)`. Def size (size_cells)
is along block-local X/Y/Z(backward); grid extent = |R applied to size|; `<Min>` is the min cell; Max = Min + extent - 1;
center = (Min+Max)/2 * cellSize. Same convention as `tools/sebp.py` (`axes()`, `ext()`, `cells()`).
Block world matrix = `LocalMatrix * Body.WorldMatrix`. Thruster force direction (grid) = `R.Backward`; gun muzzle direction =
`R.Forward`; camera view = `R.Forward`; merge face = block local Up (research.md 1.1).

### 6.3 Gyro (Keen MyGridGyroSystem port) and the sign convention
Sign: target angular velocity in the gyro's local frame, right-handed, = `(GyroPitchSign*Pitch, GyroYawSign*Yaw, GyroRollSign*Roll)`,
default **(-1, -1, -1)**; grid target = `TransformNormal(local, R_gyro)`. Single switch: `SimSettings.GyroPitchSign/YawSign/RollSign`
(CLI `--gyro-pitch-sign 1`).
Justification:
1. Ground truth (HANDOFF 3): with `g.Pitch=-l.X; g.Yaw=-l.Y; g.Roll=-l.Z` (l = desired right-handed rate in gyro-local axes,
   computed as `axis*angle` from `Cross(Forward, desired)` in `Aim()`), drones turn onto targets in game. Targets lie in arbitrary
   directions, so all three axes are exercised; the only convention under which that law converges is target = l, i.e. all three
   signs -1. With any single sign +1 that axis would diverge (turn away) and aiming would visibly fail.
2. Independent evidence from the real game: Keen's `MyGyro` stores `m_gyroOverrideVelocity` and exposes `Yaw => -stored.Y`,
   `Roll => -stored.Z` (so Yaw/Roll are minus the right-handed rate: agrees), while `Pitch => +stored.X` in the 2019 public source
   (disagrees). The stored vector is transformed by the block orientation and compared with the grid's local angular velocity
   (`velocityDiff = target - localAngularVelocity` in `MyGridGyroSystem.UpdateOverriddenGyros`), which confirms the stored vector
   is a right-handed rate in block axes. The pitch disagreement is unresolved (setters were added to the ingame interface in 2018
   after that source); in-game test T10 settles it. Practical meaning of the default: positive Yaw turns the nose to the ship's
   right, positive Pitch pitches the nose down, positive Roll rolls clockwise seen from behind.
Gyro torque (per body, all `Working` gyros): if any has `Override`: target = sum(TargetGridLocal * MaxForce*Power) / sum(MaxForce*Power)
over overridden gyros; `diff = target - localAngVel`; ramp frames (`Body.GyroRampFrames`): whenever gyro settings were
written since last tick (`Body.GyroDirty`, set by every SimGyro setter, like Keen's `SetGyroTorque -> MarkDirty`) it is recomputed
as 120 if |diff|^2 > (pi/2)^2 else `(int)(|diff|^2 * 119/(pi/2)^2) + 1`; otherwise it counts down by 1 per tick to 1 (Keen); `desiredTorque = diff * 60 / ramp / invInertiaDiag` (component-wise, grid axes);
clamp to sphere radius `sum(MaxForce*Power of overridden) + sum(MaxForce of others)`; times Gyro-group SuppliedRatio. Without
override: slowdown torque `-localAngVel * mult / invInertiaDiag` clamped per axis to sum(MaxForce)*mult (mult 5 small grid, 2 large; MyFakes), times
`MAX_SLOWDOWN + (1-MAX_SLOWDOWN)*(1-ratio)` (0.8; time constant about 0.25 s small, 0.6 s large) - gyros damp rotation when idle, like the game. Override values are clamped to
the max angular speed (2*pi rad/s small grid, pi large; Environment.sbc 360/180 deg/s).

### 6.4 Rigid body
Mass = sum of block `mass_kg` (+ inventory later). Inertia about the COM: each block = uniform box of its cell extent
(`m/12*(b^2+c^2)` ...) + parallel axis term; stored as 3x3 in grid axes, inverted once per mass change; world inverse inertia
= R * I^-1 * R^T. Semi-implicit Euler: `v += F/m*dt`, `w += Iw^-1 * T * dt`, clamp |v| <= 100 m/s (SmallShipMaxSpeed) and
|w| <= max angular speed, `x += v*dt`, `q += 0.5*dt*(w,0)*q`, normalize. No gyroscopic term, no gravity, no drag.
Thrusters: force `min(OverrideN, MaxForce) * SuppliedRatio(Thrust)` through the centre of mass (SimSettings.ThrusterTorque=false, like the game; true = torque from the thruster offset). Inertial
dampeners when `Body.DampenersOn` (RC `DampenersOverride` true) and a thruster has no override: thrust against the velocity
component along its push direction, `min(MaxForce, m*|v_along|/dt*gain)`. Scripts switch dampeners off; the pirate/vanilla AI use them. Opt-in `GameDampenerLaw`: demand `m*|v|/0.5 s`, an override on an axis switches that whole axis off. Opt-in `AngularDamping` (game 0.1/s), `CreativeBatteries`, `PowerCascade`, `PowerBatteriesFirst`. Grid splitting uses defs.json mount points and keeps the fragment with the most blocks (`SplitByMountPoints`, `SplitMainByBlockCount`).
Collisions: sphere broadphase, then block AABB overlap in one body's frame; separating impulse (restitution 0.2) at the
deepest pair; `EventKind.Collision`; damage only with `--collision-damage`.

### 6.5 Power
One network per Body. Sources: batteries (`min(MaxOutputMW, stored*3600/dt)`, not in Recharge mode). Sinks: thrusters
(`MaxPowerMW * force/MaxForce`, min power when idle), gyros (0.002 MW each when working), cameras/sensors/RC/PB/antenna
(Utility, constant from def or radius/250 kW), AI blocks (AIBlock 0.01 MW), railgun capacitor charging (DefenseAdative,
`RailgunChargeMW` until `RailgunCapacitorMWh` full [I]), guns (Defense, 0 MW: no def field). Allocation in ascending group priority
(`PowerDistributionGroups`); groups flagged `adaptible` (Thrust, DefenseAdative, Charging) take a partial ratio, non-adaptible
groups get all or nothing. Drain = supplied MW * dt / 3600 spread over discharging batteries by max output. Blueprint
`CurrentStoredPower` sets initial charge (else `initial_charge_ratio`). Empty batteries => no thrust, no gyro torque, no railgun.

### 6.6 Damage model and grid splitting
Block Hp starts at `MaxIntegrity` (sum of component integrity). `IsFunctional` = Hp >= `CriticalIntegrity`
(`integrity.critical_integrity`; functional = Hp > CriticalHp). Hp <= 0 -> `Body.RemoveBlock`: block leaves Cells/lists, `Version++`, mass and
connectivity dirty, `SimEvent.BlockDestroyed`, API object `OnDestroyed()`. Phase 8 splits disconnected parts into new bodies
(main = heaviest part; PB scripts see `Me.CubeGrid` change if theirs moved). Loss of PB or RC, or of all charged batteries, makes
the ship combat-incapable (section 9); the bodies stay in the world as wrecks (detectable, NoOwnership when no functional
terminal block remains).

## 7. Combat (slot C)

### 7.1 Weapons (data: `defs_stats.json` weapons/ammo, docs/stats.md)
| Weapon | Fire | Projectile |
|---|---|---|
| Gatling (`SmallGatlingGun/`) | 700 rpm, magazine 140, spread 0.15 deg | 800 m/s, 800 m, 90 dmg/hit, inherits ship velocity |
| Autocannon (`SmallGatlingGun/SmallBlockAutocannon`) | 150 rpm, mag 16, 0.15 deg | 800 m/s, 800 m, 500 dmg/hit, inherits |
| Rocket (`SmallMissileLauncher/`) | 60 rpm, 0.1 deg | starts 100 m/s along muzzle (no ship velocity), +600 m/s^2 to 200 m/s, 800 m, explosion r 4 m / 500 |
| Small railgun (`SmallMissileLauncherReload/SmallRailgun`) | trigger -> 500 ms charge -> shot; then 4000 ms reload; capacitor must be full (0.016 MWh, 3.6 MW draw x 0.8 = 20 s, section 15.1) | 1000 m/s, 1400 m, 0 deg, penetrating pool 8000, inherits ship velocity [I] |
| Large turrets (pirate) | per their weapon defs (LargeGatlingTurret 700 rpm etc.) | as above |
| Warhead | `Detonate()` when armed, or countdown | radius 4.4883 m, 15000 |
Ammo: infinite by default (`--infinite-ammo false` uses capacities in research.md 4). Spread: uniform direction in the cone.
Muzzle = block center + Forward * half length. Projectiles ignore the shooter body for 0.2 s, then friendly fire is possible
(`--friendly-fire false` disables damage to Owner/FactionShare bodies). Gravity off.
### 7.1b Weapon fidelity (docs/audit/weapons.md, SimSettings "weapons fidelity")
Defaults (wiki / Keen source): `GunCadenceFrames` (shots every ceil(int(1000/(rpm/60))/16.667) ticks from the last shot: the 700 rpm gatling fires 600 rpm), `BurstReload` (ReloadTimeMs after every ShotsInBurst shots in all ammo modes, also artillery/assault shell bursts; finite-ammo magazine swaps are then free), `BulletRangeMin/Max` 0.8/1.0 (bullets only), `ProjectileLinearVelocity`, `ProjectileIgnoreFiringBlockOnly`, `FixedGunsNeedPower` (0.0002 MW sink; test worlds without batteries switch it off). Old behaviour: set each to false (range min/max to 1).
Opt-in, default off, unverified in the game (in-game tests W2/W3 pending): `ExplosionOcclusion` (block-to-blast ray, stack rule with DeformationRatio), `WarheadWeaponDetonate`, `WarheadContactDetonate` (> `WarheadContactSpeed` 5 m/s), `ShellRicochet` (angle from the normal, ammo ricochet fields, `MissileRicochetDamage`), `BulletsHitRockets` (`RocketHitRadiusM`). Not done: collision deformation (weapons-09, no mechanism specified and unverifiable), warhead radius scaling.
### 7.2 Projectiles: SoA pool, swept segment vs `Raycast.RayWorld`; `MaxTrajectory` distance; rockets explode on hit or at range end.
### 7.3 Damage: bullet = full damage to the first block hit; railgun walks `RayWorldAll`, each block takes min(pool, Hp), pool
decreases; explosion: blocks with center within r of the blast take `damage*(1-d/r)^p` (p = 1) [I, Keen falloff unpublished].
### 7.4 Turret AI: refresh every 10 ticks, nearest enemy body within range with LOS, slew and lead, fire when error < 1 deg.
Turret controller (drones): designation only.
### 7.5 Pirate: code-built large-grid ship (Combat/PirateAi.cs header) flown by `PirateBrain`; vanilla AI (`VanillaAiBrain`) flies
scriptless ships (Midge) through the same thruster/gyro units.
### 7.6 Merge (docked drones): a printer + docked drones load as ONE Body (the bp has one grid); merge pairs found by GridFactory
(two merge blocks whose Up faces touch). Turning either merge block off clears the pair, phase 8 splits the drone off as its own
Body with the same velocity. Printer cycles (HiveMotherAI build/charge/release) are a later phase: projector/welder are minimal.

## 8. Data loading (slot A)
`DefDb` reads `defs_stats.json` keys `blocks` (`"Type/Subtype"`), `weapons`, `ammo`, `magazines`, `constants`:
`mass_kg`, `integrity.sum_of_components` -> MaxIntegrity, `integrity.critical_integrity` (cumulative through the critical component - 1) -> CriticalIntegrity, `integrity.deformation_ratio` -> DeformationRatio (unused),
`size_cells`, `grid`, `stats.force_n` (thrust and gyro), `stats.max_power_mw`/`power_mw`/`required_power_mw`, `stats.power_sink_group`,
battery `max_stored_mwh`/`max_output_mw`/`max_input_mw`/`initial_charge_ratio`, sensor `max_range_m`, turret controller `max_range_m`,
warhead `explosion_radius_m`/`explosion_damage`, weapon `stats.weapon` (rate_of_fire_rpm, shots_in_burst, deviate_shot_angle_deg,
shot_delay_ms, reload_time_ms, ammo_detail.*), `constants.PowerDistributionGroups`. Blueprint XML -> `BlockData`: xsi type,
`SubtypeName`, `EntityId`, `Min`, `BlockOrientation`, `CustomName`, `CustomData`, `Program`, `Storage`, `Enabled`, every other
simple child into `Props` (vectors as `Name.x`), `ProjectedGrids` recursively. An unknown block key is a hard error.

## 9. Teams, relationships, detection, events, termination

- Team t has identity `TeamTable.IdentityBase + t`; all its blocks are owned by it (armor: 0). Relation(observer, target):
  target owner 0 -> NoOwnership; same identity -> Owner; allied teams -> FactionShare; else Enemies. Grid owner = team identity
  while it has a functional terminal block, else 0. Scripts' `Friendly()` / enemy checks see exactly these values.
- `MyDetectedEntityInfo` is built only in `Api/Detection.cs` (field rules in its header).
- Events (`SimEvent`) are emitted at the source: Shot/Detonation (C), Hit/FriendlyFire (C), BlockDestroyed/GridSplit/Collision (A),
  MergeReleased (B), ScriptException (D). Metrics (D) aggregates per ship.
- Combat-capable ship (checked every 10 ticks): (running PB with a working RC/controller, or a brain) AND stored power > 0 AND
  >= 1 working thruster AND >= 1 functional weapon or armed-capable warhead. Stinger counts as capable while its warheads exist.
- Decisive outcome: all ships of every team but one are not combat-capable for `DecisiveHoldSeconds` (5 s) -> winner = that team.
  All teams incapable -> "mutual-kill". 5-minute cap -> "timeout" (draw). Engine exception -> "error" (kept, never dropped).
- Stuck/circling flag: speed < 1 m/s for 30 s while an enemy is capable, or 60 s without closing within 1.5x weapon range.

## 10. Scenarios (slot D)
Models (Full blueprints): Aegis, Basilisk, Gnat, Harrier, Hornet, Lancer, Longbow L, Longbow S, Mantis, Midge (scriptless,
vanilla AI brain), Stinger (projected grid of "Stinger Rack"), Wasp, Watcher; plus "Pirate" (code-built). 1v1: all unordered
pairs incl. mirrors (13*14/2 = 91). Unfair: 1v2, 1v3, 2v3 for every ordered pair of distinct models (sizes capped so a batch stays
in budget: default only the 1v2 for all pairs, 1v3/2v3 for a curated list via `--unfair full|curated`). Mixed teams (Watcher+3 Gnats,
Hornet+Longbow S, Wasp+2 Gnats, Lancer+Mantis) vs each single model and vs each other. Pirate: each model x1/x2/x3 vs Pirate.
Placement: team centroids 1.5-3 km apart (uniform), ships within 60 m of their centroid with >= 25 m separation, random
orientation, zero velocity. Default start commands per model (`ScenarioGen.DefaultCommands`, e.g. `hunt`) sent at 0.5 s as
Terminal argument runs. Seeds per scenario: `--seeds N` (default 10, a quick check; the PC plans pass their own counts); seed = Mix(baseSeed, hash(scenarioName), seedIndex).

## 11. Determinism
Same binary + same scenario + same seed => identical `runs.jsonl` line (except wall/cpu timing fields). Rules: no
`System.Random`, `DateTime`, `Environment.TickCount`, `Guid` or hash-of-object ordering in the engine; iterate `List`s, never
`Dictionary`/`HashSet` where order matters; fixed PB and body order; all randomness from `World.Rng.Fork(salt)`; script `Random`
via `SimShim.DetRandom`; IDs from `IdGen` (seeded); IGC delivery after all scripts. `Parallel` only across runs. Slot D adds a
selftest that runs one scenario twice and compares the result lines.

## 12. Output
- `out/sim/results/<batch>/runs.jsonl`: one JSON object per run:
  `{"scenario","kind","seed_index","seed","outcome","winner_team","duration_s","wall_ms","cpu_ms","settings_hash",
  "ships":[{"ship","model","team","alive","capable","disabled_at_s","destroyed_at_s","shots","hits" (unique projectiles that hit an enemy), "hits_raw" (old Hit-event count), "collision_damage","hit_rate","damage_dealt",
  "damage_taken","friendly_damage","blocks_lost","blocks_killed","mass_lost_frac","time_on_target_s","min_enemy_dist_m",
  "avg_speed","max_speed","collisions","script","script_runs","script_cpu_ms","script_max_tick_ms","exceptions","first_exception",
  "stuck"}]}`.
- `out/sim/results/<batch>/summary.csv`, one row per scenario (columns in `JsonOut.WriteSummaryCsv` doc comment).
- `out/sim/results/<batch>/meta.json`: git-independent header (settings, assumptions from `DefDb.Assumptions`, script keys, command line).

## 13. Performance plan
Budget: a 60 s fight ~1-3 s CPU; full matrix (~9100 1v1 runs + unfair/mixed/pirate) on 4 cores in a few hours.
- Scripts dominate (each Update1 script runs 3600x/min: raycasts, list scans). Engine overhead per tick per ship must stay
  below ~20 us. Run scripts only when due (section 3.3).
- No per-tick allocation in the engine: preallocated lists, struct `RayHit`, SoA projectiles, events as `ref struct` passing,
  `List.Clear()` reuse, no LINQ/lambdas/`foreach` over interfaces in hot paths (foreach over `List<T>` is fine).
- Cache per body per tick: `WorldMatrix`, `InvWorldRot`; block local matrices are constant; block world matrices computed on demand.
- Raycast: sphere broadphase + DDA over a dense cell array (small drones < 5k cells) - O(cells on path).
- Mass/inertia/connectivity recomputed only when a block is removed (dirty flags, phase 8).
- Sensors every 10 ticks, turret refresh every 10, metrics heavy checks every 10.
- GTS block lists cached per Body.Version; `GetBlocksOfType` iterates the cached array.
- IGC: payloads are boxed once by the sender (unavoidable, the game boxes too); queues reuse storage.
- Early termination on decisive outcome; one World per thread; `ParallelRunner` threads = cores (4).
- Profiling hook: `--profile` prints per-phase ms per tick (Stopwatch around phases) and per-script CPU.

## 14. Work order per slot (milestones)
1. A: DefDb + BlueprintLoader + GridFactory + mass/inertia + Integrate; selftest: Hornet mass equals MODELS.md, inertia positive definite, orientation of each Hornet thruster pushes along the expected axis.
2. B: base classes, GTS, thrust/gyro/RC/battery/PB/text surfaces -> a Hornet constructs and runs `Main` without exceptions (with D).
3. D: ScriptHost + RuntimeInfo + minimal ScenarioRunner (`one --scenario 1v1/Hornet-vs-Hornet`), trace output.
4. A: thrusters, gyros (Keen port), power; B: camera/sensor/IGC; C: weapons/projectiles/damage.
5. D: metrics, termination, JSONL/CSV, ParallelRunner; C: rockets, railgun, warheads, turrets, pirate, vanilla AI.
6. All: determinism test, performance pass (`--profile`), then the full matrix.

## 15. Known approximations (report them with results)
Gyro pitch sign (6.3), explosion falloff, railgun penetration semantics, no deformation, block Position = Min cell, sensor box vs AABB
test, turret slew rates, vanilla AI behaviour, IGC queue cap, camera charge cap, instruction count not measured (scripts see 0), power of
guns, no gravity/atmosphere, no inventory mass, merge holding force.

### 15.1 Simulator artefacts fixed in the simfix round (docs/sim-audit-1.md section 7), each behind a SimSettings option
Every option is a CLI flag in kebab case (`--decoys-functional false`), is part of `Describe()`/`Hash()` (so `settings_hash` in runs.jsonl and
meta.json changes automatically with any new default: the post-simfix hash differs from the baseline's `fbe7bd2075bff0ea` even when all
new options are switched back, because the option set itself is part of the hashed text) and can be reverted one by one to the baseline behaviour.
| artefact | option(s), default | model and evidence |
|---|---|---|
| AI search range (7.1) | `VanillaSearchRange` 2500 | `Data/EntityComponents.sbc` `SearchEnemyComponent/DefaultEnemySearch` `SearchRadius 2500`, forced on both `OffensiveCombatBlock` containers (`EntityContainers.sbc`); wiki "2500 m". So 2500 is the game value, not a guess: starts beyond it (ScenarioGen goes to 3000 m) really are dead on arrival for an AI-only ship. `--vanilla-search-range 3500` makes every spawn acquirable (a what-if). `VanillaAiBrain.SearchRange` per brain (-1) overrides. |
| AI Flight speed limit (7.2) | `VanillaSpeedLimit` 10 | `EntityComponents.sbc` `AutopilotComponent` `SmallFlightMovement`/`LargeFlightMovement` has `AutopilotSpeedLimit 100`, as does the Remote Control definition; `Environment.sbc` ship max is 100, so 100 is the slider maximum. The default of a freshly pasted AI Flight block is 10 m/s (wiki, `scripts/MidgeNotes.md`; a blueprint cannot preset it). The two are not contradictory, so 10 stays; `--vanilla-speed-limit 100` is the what-if for a raised slider. Unverified in game. |
| Decoys (7.4) | `DecoysFunctional` true, `DecoyPreferTarget` true, `DecoyAimProbability` 1, `DecoyRerollS` 10 | Wiki: enemy turrets and homing missiles prefer decoys; `CubeBlocks_Weapons.sbc` Decoy has `PriorityModifier 10`, `NotWorkingPriorityMultiplier 0` (a switched-off/non-functional decoy counts 0). Sim: (a) automatic targeting that can choose between bodies (large turrets, Custom Turret Controller designation, AI-block stand-in `VanillaAiBrain.Search`) picks a body with a working decoy before any body without one (within range and line of sight, nearest first among equals); (b) turrets and the AI-block stand-in (fixed guns and rockets) aim at the nearest working decoy block instead of the body centre. Sim rockets/slugs are unguided, so "homing missiles" are covered only through the rocket aim point. Scripts aim from their own camera hits and the controller still reports the grid centre, so scripts are not misled (in the game a controller target is the grid too). `DecoyAimProbability` < 1 rolls per (shooter, target, 10 s) with the deterministic hash. The `Combat/Decoys.cs` cache is per `Body.Version`. |
| Collision damage (7.7) | `CollisionDamage` true, `CollisionDamageScale` 0.0005, `CollisionDamagePatchM` 1.5 | Per impact tick with a closing speed vn at the contact: E = 0.5 * reducedMass * vn^2 (reducedMass from `denom`, includes the lever arms), HP = `CollisionDamageScale` * E for EACH body, shared equally by the blocks within `CollisionDamagePatchM` of the contact point (the deepest block always). The hit-point scale is an assumption [I]: a 10 m/s impact of two 1.6 t walls costs 22 HP per wall, 100 m/s costs 2.2 kHP (gatling hit = 90). Applies to teammates and when `FriendlyFire` is off (a collision is not weapon fire). Reported as `EventKind.CollisionDamage` (ShipStats.CollisionDamageTaken, JSON `collision_damage`, also part of `damage_taken`), not as Hit/FriendlyFire, so it does not count as hits or damage dealt. |
| Collision counter (7.6) | `CollisionEpisodeEvents` true, `CollisionEpisodeGapTicks` 30 | One `Collision` event per contact episode (first closing contact after 30 ticks without contact between the pair), instead of one per 30 ticks of contact (old: `CollisionEventCooldownTicks`, used when the option is false). Baseline "thousands of collisions" for wingmates in permanent contact become 1 per episode. |
| Hits per shot over 100 % (7.6) | `HitCountUnique` true | `ShipStats.Hits` (JSON `hits`, `hit_rate`) counts projectiles that hit an enemy at least once (every Hit event carries the projectile id; a slug that passes through a ship for several ticks, or a splash that damages several bodies, is one hit; id 0 events, e.g. warheads, count alone). `hits_raw` (JSON) / `ShipStats.HitEvents` keep the old event count. hit_rate is at most 1. Damage dealt/taken are unchanged. Rank by damage for piercing and splash weapons all the same. |
| Railgun reload (7.10) | `RailgunCapacitorFromData` true, `RailgunRechargeDrawSmallMW` 3.6, `RailgunRechargeDrawLargeMW` 38, `RailgunChargeMultiplier` 0.8, `RailgunCapacityLargeMWh` 0.5 | Verified, not inferred: `EntityComponents.sbc` `EntityCapacitorComponent` `SmallRailgun`: Capacity 0.016 MWh, RechargeDraw 3.6 MW, ChargeMultiplier 0.8 -> 0.016 / (3.6 * 0.8) h = 20.0 s (the old 20 s [I] was right for the small one; the wiki "~20 s"). `LargeRailgun`: 0.5 MWh, 38 MW, 0.8 -> 59.2 s (old model 60 s at only 2.88 MW draw). The data-sheet `ReloadTime 4000 ms` / `ShotDelay 500 ms (small) 2000 ms (large)` / `RateOfFire 20` are weaker than the capacitor: a shot needs trigger + delay, then the 4 s reload AND a full capacitor, so the interval at full power is delay + 20 s (small) / 61.2 s (large). The sink now requests the RechargeDraw (small 3.6 MW, large 38 MW) and stores ChargeMultiplier of what it is supplied, so a power-limited ship recharges 25 % slower than before and a large railgun draws 38 MW (the old model asked 2.88 MW for both). `--railgun-capacitor-from-data false` restores the old numbers. The in-game charge curve itself (linear? starts when?) is still unverified (test T9). |
Remaining artefacts of audit section 7 NOT changed (no evidence to fix them): 7.3 the stand-in's aim/lead and 730 m hold, 7.5 `CameraMaxCharge`
unlimited, 7.8 IGC blocked between enemy teams, 7.9 instruction count, 7.11 the "disabled" definition, the `slow` flag (it marks parked ships without a target).

## 16. M0 additions: pilots, automation blocks, merge fuse, per-PB scenarios (docs/design/control_arch.md section 6/7, S1-S9, S14)
Everything below is **opt-in or inert** for the older scenarios: `SimSettings` fields marked `[OptInSetting]` are left out of `Describe()`/`Hash()` while at their defaults (the settings hash of every older result file stays valid),
and the Hornet-vs-Hornet result lines are byte-identical before/after (checked with 3 seeds, wall/cpu fields aside). Evidence: all of it is the sim's MODEL; real-game scale/sign of the indicators, the Event Controller event numbers, timer granularity and the toolbar `Run`-with-argument item are unmeasured (control_arch K1-K4).

| What | Where | Behaviour |
|---|---|---|
| Cockpit as ship controller (S1/S2/S3) | `stubs.cs`, `Api/RemoteControlApi.cs` | `Cockpit` blocks are `SimCockpit : SimShipController, IMyCockpit` (N text surfaces from the blueprint's `MySerializedTextPanelData`, else a per-subtype table), `IsUnderControl`, `MoveIndicator` (x right+, y up+, z backward+), `RotationIndicator` (pitch+ = nose down, yaw+ = turn right), `RollIndicator`, `HandBrake`, `ControlThrusters` (true on a seat). Passenger seats: `CanControlShip` false. A Remote Control is under control only if a test says so. `stubs.cs` also gets `Vector2`, `IMyDoor`, `IMyShipConnector`, `IMyButtonPanel`, `IMyEventControllerBlock`, `IMyTextPanel`, `MySprite`/`MySpriteDrawFrame`/`DrawFrame`. |
| Pilot input in the physics (S4) | `Core/PilotInput.cs`, `Core/Physics.cs`, `Body.Pilot` | While somebody sits (`SimShipController.SetUnderControl(true)` hangs the input on the Body): every thruster WITHOUT override follows the Move demand on its axis (stick x max force; the opposite thrusters get 0; an axis with input is not dampened, free axes still are); gyros WITHOUT override follow a rate target `PilotRateGain (1.5 rad/s) x clamp(indicator)` about the controller axes (rate controller, `PilotGyroRampFrames` 2, torque limited). Overridden blocks ignore the pilot ("override wins per block"); with overridden and free gyros the target is the capacity-weighted mean (one of two gyros overridden at 0: the pilot's yaw settles at half rate). Settings: `PilotRateGain`, `PilotRotSign` (-1), `PilotInputClamp` (1), `PilotGyroRampFrames`. |
| ScriptedPilot (S5) | `Run/ScriptedPilot.cs`, `ShipSpec.Pilot`, CLI `--pilot "0:sit;5:move 0 0 -1;20:leave"` | Timeline verbs `sit leave move rot roll stop damp fly-to hold face-target fire fire-aligned press button run killpb disablepb enablepb` (header of the file). A brain (phase 3), so the indicators are what the physics sees the same tick. Counts `OverrideFightTicks` (pilot input while any gyro/thruster override is on) and prints it with `one`. |
| Per-PB commands and CustomData (S6) | `Scenario.cs` | A command `Tag:arg` (e.g. `--commands "0.5:Helm:hunt"`, `ShipSpec.Commands`) goes only to PBs whose `CustomName` starts with `[Tag]`; `Helm` falls back to every PB on a ship with no tagged PB; a role tag (Helm/Staff/Gunnery/Pod) nobody carries drops the command; any other text with a colon is still one plain argument for all PBs. `ShipSpec.PbCustomData[tag]` / `--pb-custom-data "Helm=<ini>|Staff=<ini>"` set CustomData per tag after the all-PB `--custom-data`. |
| `--enemy-scripted` (S6) | `ScenarioGen`, `Scenario.cs` | Enemy blueprints from `--enemies` keep their own PBs: no Pirate/hold brain, default commands `Helm:hunt` at 0.5 s and `Staff:wing satellite` at 1 s, the hybrid AI-block search brain is attached like for our own drones, grids merge-fused (below) and the largest kept. Implies `--block-automation`. |
| Timer, Event Controller, Button Panel, groups (S7 subset) | `Api/BlockAutomation.cs`, `BlueprintLoader`, `GridFactory` | `<Toolbar>` slots (terminal block / terminal group / weapon items), EC `<SelectedBlocks>` and the grid's `<BlockGroups>` (cells) are parsed. Timer: countdown in ticks (`Delay` ms from the blueprint), expiry runs its toolbar; actions `Start Stop TriggerNow OnOff* Shoot* Open* Run` (Run with a String parameter sends that argument to the PB). Event Controller: one event over the selected blocks every 10 ticks, edge-triggered (slot 0 on becoming true, slot 1 on becoming false; the first evaluation only records the state), events `cockpit` (occupied), `power` (stored fraction), `integrity`, `merge`, `connector`, `cargo`; threshold 0..1, `ConditionInvert` = lower-or-equal, `ANDGate` = all selected blocks. Event NUMBERS: seen in samples cargo=1 integrity=2 connector=7 welder=14; cockpit=101 power=102 merge=103 are PLACEHOLDERS (`--ec-event-ids` maps them; K4 open). Executed only with `--block-automation` (auto-on for scenarios with a pilot and for `--enemy-scripted`). `GetBlockGroups` now lists the blueprint's groups. Doors (3 s), connectors (never lock), LCD panels (text kept) are API shells. |
| Merge fuse (S9) | `Core/GridFuse.cs`, `GridFactory.SpawnGrids` | Grids of one blueprint whose ENABLED merge blocks touch face to face (the `PairMerges` test) are spawned as ONE body (blocks of the other grids re-expressed in the root lattice: 24 axis rotations, whole-cell offsets within 3 cm); `BlockInst.ConstructId` keeps the origin grid, `GroupData.Construct` likewise; switching both merge blocks off splits the body again (Connectivity). `FuseMergedGrids` false = old behaviour. Connector/rotor/piston links are NOT fused. |
| KillPb (S8) | `ScriptHost.KillPb`, pilot verb `killpb TAG` | The PB stops for good, overrides stay in the blocks (dead-man test). `TryRun` (synchronous, real result) and the sticky "Script too complex" (`--enforce-instruction-limit`) already existed. |
| Ad-hoc scenarios | `ScenarioGen.Custom`, `one --scenario "kind/A-vs-B"` | A and B are `+`-separated blueprint folders of `out/Blueprints` (`Nx` count prefix): `one --scenario "pilot/Hornet Pilot Test-vs-Hornet" --pilot "1:sit;1:move 0 0 -1"`. |

Tests: `SimHost.exe selftest m0` (also in `selftest all`; 105 checks): cockpit API, thrust response 3 seeds x 4 axes vs the analytic F/m*t (<= 5 %), rotation response (yaw/pitch/roll), override-wins (gyro, thruster, pure override, no pilot), closed loops, Timer/EC/Button/groups, merge fuse (+ rotated lattice, split, disabled), KillPb, opt-in hash, and an integration section that uses the generated test ships `Hornet Pilot Test` and `Twin PB Test` (skipped when `out/Blueprints` lacks them: `python3 -I tools/build_all.py --only "Hornet Pilot Test" "Twin PB Test"`).

## Survival metrics (reporting only)
Per ship, runs.jsonl now also carries `lost` (disabled or destroyed), `lost_at_s`, `hits_taken`, `hits_by_class` and `dmg_by_class` (both `[bullet, rail, explosion, collision]`) and `hitmap` (`[["construct:x:y:z:Block", block damage events, integrity absorbed, destroyed 0/1], ...]`, ships with hits only). They come from a new `EventKind.BlockDamage` emitted in `Damage.ApplyCore` (no effect on the fight). `summary.csv` gets extra columns at the end (ships_A ... runs_all_B_lost: survival rate, lost per enemy kill, damage dealt/taken ratio, first loss time, mean life of lost ships, hits per ship by class) and `hitmap.json` is written next to it (per ship model, aggregated over seeds and runs). Tools: `python3 -I tools/sim_survival.py RESULTS [--ships] [--side B]`, A/B `python3 -I tools/sim_survival.py AFTER --compare BEFORE` (or `pc/compare_runs.py --survival AFTER BEFORE`), `--hitmap --model M`; `tools/sim_report.py` has a Survival section. Side A = team 0, B = all other teams.
