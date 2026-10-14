# Rotor + hinge AI turrets (v2.8)

Key `turrets`. Generator `tools/designs/turrets.py`, script `scripts/TurretAI.cs` (v1.0), tests `tools/tests/test_turrets.py`.
Nothing here has been pasted or run in the game. The simulator has no rotor model (section 9), so there is no combat result for these turrets, only geometry, compile and
file-format checks.

## 1. Why

What the repo learned about the vanilla point-defence turrets (`docs/design/systems_catalogue.md` 2.6, `control_arch.md` 3.5, `kamikaze-defence.md`, `tech-hunter-study.md`):

* a vanilla turret shoots at 600 m (small) / 800 m (large) until an AI block or a pilot lock raises its range; a small fast drone is gone before it slews: gatling turret 1.5 rad/s,
  autocannon turret 1.0, assault turret 0.4;
* it has no lead logic for jinking targets (the Hornet's solver exists because the vanilla one over-led a tumbling wreck), no friendly-fire check against a wingmate, and the ship cannot
  tell it which target to shoot;
* the ship's own gunnery (batteries `[B1]..[B4]`) is fixed forward and needs the whole hull to turn; against small targets that is exactly what fails (`control_arch.md` 3.5: turret-only mode).

So: put the *proven* pieces (lead solver from the drones, the Helm's target, hull-aware fire gating) on a mount that turns faster than the ship ever could. A custom turret is a rotor
(azimuth) + hinge (elevation) carrying fixed `SmallGatlingGun`s and a camera, driven by one Programmable Block per ship.

## 2. Geometry (measured, not assumed)

The owner's Tech Hunter (`pc/enemies/Tech Hunter/bp.sbc`) carries six of these turrets and the game wrote them, so every block shape and every pose below comes from a game-written file:

```
host grid --- MotorAdvancedStator (LargeAdvancedStator) --- top grid: LargeAdvancedRotor (cell 0,0,0) + LargeHinge (cell 0,1,0) --- head grid (small): SmallHingeHead (cell 0,0,0) + guns
```

* The top block is the first block of its grid, cell (0,0,0), default orientation, written with **no** `Min`, orientation, colour or owner, only `ParentEntityId` and
  `YieldLastComponent`; the stator carries `TopBlockId` (= the top's EntityId). `sebp.Blueprint.mount()` writes both links; `extract_shapes.py --merge-types` pulled the four Motor
  types out of the Tech Hunter alone (the owner's `ref/user-samples` is not in the repo).
* Pose of the top block in the stator block's frame (origin = centre of the stator's cell box, `sebp_multi.block_pose`): translate `(0, -d, 0)`, then turn about the stator's **Up**
  by **-CurrentAngle** (forward = (sin a, 0, -cos a)). `d`: LargeAdvancedStator -> LargeAdvancedRotor 1.0502 m (12 pairs), LargeStator -> SmallRotor 0.8293 m, LargeHinge -> LargeHingeHead
  or SmallHingeHead 0.000 m (15 pairs). `test_top_offsets_reproduce_the_tech_hunter` replays all 29 pairs of the file: 28 are within 2 cm / 0.02 rad, one LargeHinge/LargeHingeHead
  pair of the sample is 0.35 m off (a displaced hinge).
* The hull sits against the stator's **Bottom**; the box is 2 cells long along Up. The rotor head lies 0.2 m above the lower cell's centre (inside the box), the hinge in the upper
  cell. Large host: the hinge centre is 3.95 m above the deck, everything of the turret fits the old 3 x 3 x 3 footprint of the vanilla large turret.
* The small-grid stators (`SmallAdvancedStatorSmall`, `SmallHinge`: Shrike, Corsair) appear in no sample. Their offsets are the large values / 5 and are marked `UNSEEN` in
  `TOP_OFFSETS`; so is their XML shape (generic `MotorAdvancedStator/*`). The Lite ships have no small-grid turret.
* **Hinge axis horizontal, bore = the hinge's Left = the azimuth axis.** At angle 0 / 0 the guns point straight out of the hull; the hinge runs -90..+90 (`MinAngleDeg`/`MaxAngleDeg`
  of the definition), the azimuth is free (`LimitsActive` false). A turret covers the half-space on its outward side; the hull covers the rest.

### Pointing law

With `a` = azimuth axis (the stator's Up), `h` = current hinge axis (the hinge block's Up), `d` = unit direction to the aim point, `c = a x d`, `phi = atan2(|c|, a.d)` (clamped to
pi/2 = the horizon), `hd = +-c/|c|` with the sign that makes `h.hd >= 0`, `sg` that sign:

```
azimuth error  ea = -signedAngle(h, hd; about a)         (the top turns by -Angle about Up)
hinge target   theta* = -sg * phi          error ee = theta* - El.Angle
feed-forward   from the line-of-sight rate: d' = (r x v_rel / |r|^2) x d;  az' = -a.(d_perp x d')/|d_perp|^2,  el' = sg (a.d')/|c|
```

`test_pd4l_pointing_law` / `test_pd2s_pointing_law` drive the real pose chain with 200 random start angles and targets: bore error below 1e-6 rad. The same formulas are in
`TurretAI.cs Control()`; `test_the_script_uses_the_same_formulas` greps four of its expressions.

## 3. Models

| model | host | blocks | PCU | mass | guns | fire | notes |
|---|---|---|---|---|---|---|---|
| **PD4L** | large grid (Warden, Leviathan) | `LargeAdvancedStator` 1,006 kg + `LargeAdvancedRotor` + `LargeHinge` 1,006 + `SmallHingeHead` + 4 x `SmallGatlingGun` + camera + 21 light armour | 568 | 4.0 t (3.0 t moving) | 4 x 1,050 dps (catalogue 2.6), 560 rounds each | the Tech Hunter chain, head layout copied from its autocannon pod | seen |
| **PD2S** | small grid (Shrike, Corsair) | `SmallAdvancedStatorSmall` 188 kg + `SmallAdvancedRotorSmall` + `SmallHinge` + `SmallHingeHead` + 2 x `SmallGatlingGun` + camera + 13 armour | 400 | 1.4 t (1.2 t moving) | 2 x 1,050 dps | flat plate, guns side by side along the hinge axis so the head swings low only 0.5 m | stators UNSEEN |

* Head layout (small cells, head block (0,0,0), bore = -x): bracket plate at x = 0, -1, the camera at (-2,0,0) between the guns, guns `Min (-3, y, z)` pointing -x; PD4L guns at
  (y,z) = (+-1,+-1), PD2S at (+-1,0). Both validate with `sebp.validate` (mount points: every block touches its neighbour).
* Moving inertia / slew (`slew_rates`): PD4L azimuth 616 kg m2, hinge 1,022 kg m2; PD2S 174 / 395. With the written `Torque 1E+06` that is 160-5,700 rad/s2: **torque never limits**.
  The turn rate is the rotor's velocity cap (30 rpm large = 3.1 rad/s, 60 rpm small; from game knowledge, not in `defs.json`, test K-T3). Script cap `Max Rate 3` rad/s, slew
  limit `Max Accel 12` rad/s2, so a turret follows a 100 m/s crosser down to about 33 m (the vanilla gatling turret to about 67 m).
* Ammunition: each gun carries its own 4 magazines (`fill_ammo`); whether a conveyor passes a rotor/hinge is unknown (K-T9, test 42), so no feed is assumed. 560 rounds at 700 rpm = 48 s of fire.
* A turret takes the cells of the vanilla turret it replaces; the swing sphere (centre = hinge centre, radius = farthest head cell + 0.25 m: 1.9 m PD4L, 1.8 m PD2S) must lie inside
  that footprint (`sphere_conflicts`, `sweep`).

## 4. Names, the Helm, and who sees what

* Every turret block is named `[TU] T<n> Azimuth | Elevation | Gun <k> | Camera`; the PB is `[TU] Turret PB`. **Never `[SH]`.** `ShrikeAI.cs Own(b)` = same construct AND name starts
  with `[Link] Block Prefix`; with `Block Prefix=[SH]` (every ship's Helm Custom Data now has it, the Shrike's was empty before) the Helm collects no turret gun as a fixed gun,
  no turret camera as a ship camera. Without that line the Shrike's Helm would sort the PD2S guns into a battery and fire them forward. `test_the_helm_ignores_the_turrets`.
* The PB does not start with `[Staff]` (the Helm switches its Staff link on by that word).
* The guns live on the head grid, so they cannot be in a host block group: the dead-man timer's `Weapons` group and the cockpit toolbar do not reach them. `hold` on the Turret PB
  (or `Fire Mode=HOLD`) does. Listed in `packages/tests.md`.

## 5. TurretAI.cs

17,316 minified characters (limit 100,000), Update1, ~1.5 k instructions per active mount per update, mounts alternate ticks, skip above 30 k instructions.

**Discovery** (start, `reload`, every 5 s): motors named `[TU] T<n> Azimuth|Elevation`, guns `... Gun`, camera `... Camera` on the construct; tops that are not attached get `Attach()`;
sets `Torque`/`BrakingTorque` (Custom Data), the hinge limits +-1.5708, `RotorLock` off. First 0.5 s per mount: a **sign check** (+0.35 rad/s on the hinge then the azimuth, the motor's
`Angle` must increase, else that axis' sign is flipped).

**Targets**, merged by entity id, extrapolated with velocity and a capped, decaying acceleration (`Lead Accel` 0.5):

| source | tuple | note |
|---|---|---|
| `<Ship Tag>.H2S` | `MyTuple<int,long,Vector3D,Vector3D,float,float>` 5 Hz, `CurrentConstruct` | the Helm's selected target: +200 score |
| `<TAG>_TGT` for `Listen Tags` | `MyTuple<long,Vector3D,Vector3D,double,long>`, radius < 0 = finished | the fleets' shared tracks, the Helm's own shares |
| vanilla turrets and Custom Turret Controllers | `GetTargetedEntity()` at 5 Hz | velocity may be 0 (audit F-12): differenced from successive positions |

No own search: a PD turret would sweep a ray over a sphere and find nothing; the Helm, the wing and the vanilla turrets already do. (Follow-up in section 10.)

**Per mount, every second update:** score every track (distance, angular travel, +600 for an inbound track that arrives within 12 s, +400 missile, +100 small, -150 per mount
beyond two already on it, hysteresis 40), take the best reachable one (above the mount's horizon, within `Range` x 1.15), solve `Lead` (iterative intercept, bullet 800 m/s relative to
the ship, shooter velocity = the ship's), compute (ea, ee) and the feed-forward, rate = `Gain` x error + feed-forward, clamp to `Max Rate`, slew-limit by `Max Accel`.

**Masks** (generated, in the PB's Custom Data `[Mount Tn]`): `Move Mask` = bins where the swinging head or the top parts would hit the hull, `Fire Mask` = bins whose bore, from
each muzzle, meets the host hull within 60 m. 36 azimuth rows (stator `Angle`, 10 deg) x 19 hinge bins (-90..+90), 5 hex digits per row, bit j = hinge bin j, 1 = blocked.
`Move Mask` stops an axis whose next 0.12 s of travel would enter a blocked bin; `Fire Mask` gates fire. 5 deg sampling, bins OR-ed (`move_mask`, `fire_mask`).

**Fire gate:** bore within `atan((0.5 r + 0.4)/range)` (0.004..0.06 rad) of the aim direction, range <= `Range` (700), track younger than 0.6 s, `Fire Mode` allows (`FREE` any hostile
track, `HELM` only the Helm's, `HOLD` never), current bin not in the `Fire Mask`, and a camera ray along the bore (every 0.5 s while firing) that does not hit a friendly that is not
the ship (then 1 s hold). Shooting is `ApplyAction("Shoot_On"/"Shoot_Off")`, the same as the Helm's.

**Parking:** no target for `Park Delay` (8 s): hinge to `Stow El` (0 = guns straight out), azimuth stays. On any exception or detached top: velocities 0, guns off. A dead PB
leaves the rotors at their last velocity (K-T10: the hinge limits and the braking torque are the only stop).

Arguments: `free | helm | hold | park | reload`. Status on the PB screen: per mount state (CAL / TRACK / PARK / OFF), angles, target, aim error, FIRE. A 1 Hz heartbeat
`<Ship Tag>.TUR MyTuple<int mounts, int tracking, int firing>` goes out on the construct.

## 6. Ships

| ship | turrets | where | replaces | PCU | mass | coverage (fire mask) |
|---|---|---|---|---|---|---|
| Shrike (Full) | 1 x PD2S | dorsal spine, lower cell (0,9,22), between canopy and antenna | nothing | +400 +PB 100 | +1.4 t | 98 % |
| Corsair (Full) | 2 x PD2S | the two pod-roof sites | the two `SmallGatlingTurret` PD Roof | +800 +PB 100 -450 | +2.7 t | 85 % |
| Warden (Full, Lite) | 4 x PD4L | `Stern P/S` dorsal, `Belly Aft P/S` ventral | 4 of the 8 `LargeGatlingTurret` | +2,272 +PB 100 -900 | +16 t | 80 % dorsal (tower), 96 % ventral |
| Leviathan (Full) | 8 x PD4L | `Deck P/S`, `Stern P1/S1/P2/S2`, `Keel 1/2` | 8 of the 20 gatling turrets | +4,544 +PB 100 -1,800 | +32 t | 85-98 %, keel 47 % (recess) |
| Leviathan Lite | 10 x PD4L | as Full + the two `Deck` rocket sites (a gatling stood there in Lite) | | | | |

Kept on purpose: the other four gatling turrets on the Warden, twelve on the Leviathan, and every assault/artillery/rocket turret. They are what the simulator models, so its
results stay comparable, and the vanilla turrets keep working if the custom ones do not (a dead script, a detached top). `dps_sustained` in `numbers()` now counts the custom guns
at 1,050 each (Warden 15.2 k -> 32.0 k, Leviathan 52.5 k -> 86.1 k); `dps_vanilla` is the old figure. Lite Shrike and Corsair have no turrets (unseen shapes).

PB placement: Shrike avionics bay, Corsair centre corridor, Warden bridge deck, Leviathan bridge deck (first free cell where the PB attaches).

## 7. Checks

* `python3 -I tools/tests/test_turrets.py` (19 tests, ~45 s): geometry vs the Tech Hunter, stator tag order, link resolution, file round trip, pointing law, model validation,
  masks, naming, per-ship counts and swing spheres.
* Every ship generator calls `turrets.check_mounts` at the end: no two swing spheres overlap, the parked pose (hinge 0) is free at every azimuth, every turret sees at least 25 % (40 % keel) of
  its bins.
* `tools/compile_check.sh`, `minify_check.sh`: TurretAI compiles (C# 6) against `stubs.cs`; the stub got `IMyMotorStator` & co (`pbapi_names.txt` lists the official member names).
* Old tests updated: Warden/Leviathan/Corsair/Shrike counts and names, `test_m6` (the new PB, section `Mount T`).

## 8. Not measured (all in `packages/unknowns.md` as K-T1..)

K-T1 the tops attach after the paste (and the blueprint file as a whole pastes: the stator XML is the game's own, but not on a small-grid host); K-T2 `TargetVelocityRad` sign vs `Angle`
(the script checks at start); K-T3 the rotor velocity cap (30 / 60 rpm); K-T4 clang/jitter at `Torque 1E+06` and `BrakingTorque 1E+05`; K-T5 the small-grid stator offsets (0.21 m, 0.166 m
are the large values / 5); K-T6 hinge `Angle` range as read by a script (-pi/2..pi/2 assumed); K-T7 that the Helm ignores `[TU]` guns; K-T8 mask accuracy (sampled at 5 degrees, spheres
of 0.25 m per head cell); K-T9 ammunition through the rotor/hinge; K-T10 what the rotors do if the PB dies; K-T11 `GetTargetedEntity` velocity (audit F-12); K-T12 that bullets inherit the
ship's velocity from a gun on a subgrid (the lead assumes it).

## 9. Simulator

`sim/src/Run/RigidLink.cs` keeps every non-main grid of a `ship/` scenario in its spawn pose, so the turret heads ride along as inert mass; the sim has no `IMyMotorStator`, so TurretAI
finds no mounts and idles (compile checked: `out/sim/scripts/TurretAI.dll`). The vanilla turrets the sim models are the ones that stay. A rotor model (`SimMotorStator`: velocity
mode, angle limits, the pose chain of `top_pose`) plus a head-grid weapon model would make the turrets measurable; it is the biggest follow-up.

## 10. Follow-ups

* `SimMotorStator` + RigidLink angle term (sim); turret scenarios with 3-10 seeds against the Hornet pair, Pirate and a kamikaze swarm.
* Own search with the head cameras (a slow sweep with `Raycast` at 10 Hz) so a ship without a Helm target still has coverage.
* Heads with `SmallBlockAutocannon` x 4 (the Tech Hunter head unchanged), `SmallBlockMediumCalibreGun` (long range), and a rocket head with the Basilisk's `RktPath`.
* Staff watchdog: heartbeat `.TUR` missing for 3 s -> switch the vanilla turrets' targeting on (they are on already; this would be for a Custom Turret Controller variant).
* A gunner seat that takes a mount (`IsUnderControl` handover).
* Group the head guns on the head grid and give the cockpit a Run slot with `hold` / `free`.
