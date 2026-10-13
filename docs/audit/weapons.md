# Audit: weapons, damage, projectiles, explosions, decoys, turrets (key `weapons`)

Scope: `sim/src/Combat/{Weapons,Projectiles,Explosions,Damage,Decoys,TurretAi,PirateAi,VanillaAi}.cs`, `sim/src/Core/SimSettings.cs`,
`sim/src/Blueprint/DefDb.cs` (what is read from `defs_stats.json`), `sim/src/Api/{WeaponApi,WarheadApi,TurretApi}.cs`, the weapon wiring in
`tools/shapes.json` / `tools/sebp.py`, and the weapon constants the scripts carry.

## How this was checked, and what the evidence is worth

| Tier | Source | Notes |
|---|---|---|
| 1 | `defs_stats.json` (ammo, magazines, weapons, blocks), cross-checked block-vs-top-level weapon records (0 mismatches) | the user's install |
| 2 | Keen's public source, `KeenSoftwareHouse/SpaceEngineers` (shallow clone in `/home/user/keensoftwarehouse/spaceengineers`, commit dated 2022-01-06): `MyProjectile`, `MyMissile`, `MyGunBase`, `MySmallGatlingGun`, `MySmallMissileLauncher(Reload)`, `MyLargeTurretBase`, `MyWarhead(s)`, `MyExplosion`, `MyGridExplosion`, `MySlimBlock`, `MyGridPhysics`, `MyUtilRandomVector3ByDeviatingVector`, `MyRandom`, `MyGameConstants` | **Predates Warfare 2** (railgun capacitor, artillery/assault shells with health pool and ricochet, ShotDelay, AI blocks are not in it). Where it is the only evidence for a behaviour the finding says so. |
| 2 | wiki.gg pages `Block_Weapons`, `Railgun`, `Artillery`, `Assault_Cannon`, `Warhead`, `Decoy` (fetched with curl, text in the scratchpad) | the `Block_Weapons` table has Fire Rate and Reload columns that reproduce the data numbers exactly where the game applies burst+reload (see W-03) |
| 3 | recollection | labelled |

Not reachable: the `ref/` folder (so `EntityComponents.sbc` capacitor numbers could only be cross-checked against the wiki Railgun page, which agrees), the current (post-2022) Keen source, a running game.

Simulation runs (CLAUDE.md rule: few seeds, say what and why): a private copy of the sim (`<scratchpad>/psim`, never written into the repo) with audit switches
`--audit-gun-cadence`, `--audit-burst-reload`, `--audit-explosion-game`, `--audit-linear-shoot-vel`, `--audit-range-lo/hi`, built with `mcs` into
`<scratchpad>/psim_out`, run against a private copy of the repo (blueprints for Hornet, Wasp, Gnat, Basilisk, Longbow S/L built there). 20 seeds per scenario per variant to see
the direction and rough size of each deviation (about 12 variant sets x 4-5 scenarios; every set finished in under 2 minutes). `selftest combat` passes unmodified (110 checks).
The switches are NOT in the repo; the code is described in each finding so it can be re-done.

## Headline

1. **Explosions (rockets, warheads) are far too lethal in the sim**: the sim damages every block inside the radius with `damage*(1-d/r)`; the game walks each block's ray to the blast centre and every block in front absorbs its own integrity. On the small-grid rocket duel (Wasp vs Wasp) the damage dealt per run drops by 74 % and the median time to kill triples when the game's rule is applied (W-01).
2. **The gatling fires 600 rpm in the game, not 700** (wiki says 600 and 900 DPS; the Keen cadence code makes it 6 frames per shot). The sim is 16.7 % too fast for every gatling ship and for the Pirate's two turrets (W-02).
3. **Burst/reload (`ShotsInBurst` + `ReloadTime`) is ignored whenever ammo is infinite** (the default), and ignored for the burst count of artillery and assault-cannon turrets: gatling turrets, rocket turrets and the Basilisk's large launchers are too fast, artillery/assault turrets are 2x too slow (W-03).
4. Smaller but systematic: bullet range is not fixed at 800 m (W-04), the spread distribution is wider than the game's (W-05), armed warheads in the game also go off on weapon fire and on contact (W-07), shells ricochet (W-08), collision damage is a different mechanism (W-09).
5. Blueprint wiring of weapons has defects that can matter in game: the Longbow L's large railgun carries no capacitor component, the Basilisk's launchers hold a wrong inventory volume (W-15).

## Findings

Severity: critical / high / medium / low / info (task definitions). IDs match the structured result.

### W-01 (high) Explosion damage ignores occlusion and DeformationRatio

- **Sim** (`Explosions.cs:46-60`): every block whose centre lies within `radius` takes `damage * (1 - d/r)^1`, no line of sight, no absorption. Comment calls the falloff "[I, Keen falloff not documented]".
- **Game** (`MyGridExplosion.ComputeDamagedBlocks`, `MyExplosion.ApplyVolumetriDamageToGrid`, 2022 source): for each affected block a ray runs from the block centre to the blast centre (DDA through the grid, Havok rays across grids). The blocks on the ray form a stack, processed from the one nearest the blast: `factor = 1 - (d_block - d_prev)/(R - d_prev)`; damage to the block = `remaining * factor * block.DeformationRatio`; `remaining = max(0, remaining*factor - block.Integrity/DeformationRatio)`. A block is simply removed if `Integrity/DeformationRatio < amount`. So the first block gets the sim's number, everything behind it gets what is left after the blocks in front have soaked up their integrity. Armour blocks also scale damage by DeformationRatio (large heavy 0.4, small heavy 0.16, small light 0.32, cockpit 0.2, merge 0.5; light large armour has none; `defs_stats.json` `integrity.deformation_ratio`). `distanceFactor` uses the block centre and must be > 0, as in the sim, so the radius test itself is right.
- **Effect** (private build, `--audit-explosion-game true`, 20 seeds, `--face --start-distance 500:900 --commands "0.5:ai off;0.5:lock;0.5:hunt"`):

| scenario | metric | sim as is | game rule | 
|---|---|---|---|
| 1v1/Wasp-vs-Wasp (small grid, 500 dmg r 4 m rockets) | avg damage taken per run (ship A) | 8779 | 2250 (-74 %) |
| same | median time to kill | 34.8 s | 101.3 s (x2.9); avg duration 49 -> 108 s |
| 1v1/Basilisk-vs-Basilisk (large grid) | median time to kill | 21.2 s | 27.0 s (+28 %) |
| pirate/Basilisk-vs-Pirate | damage taken by the Pirate | 90.5 k | 60.8 k (-33 %) |

  Splitting the two parts: occlusion alone gives the whole effect on the Wasp duel (ttk 55.6 s, damage 1802); DeformationRatio alone changes nothing there (ttk 34.8 s) because the Wasp's hit blocks are not small armour.
  Warheads are barely affected (15000 damage dwarfs the soak at 4.5 m), but rockets (500 damage; a small-grid light armour block soaks 100/0.32 = 312) are.
- **Confidence**: medium-high for the mechanism (source read line by line); medium that the current game still uses exactly this code (Warfare 2 reuses volumetric explosions; recollection).
- **Fix**: in `Explosions.Explode`, per affected block call `Raycast.RayWorldAll(w, blockCentre, dirToCentre, dist, null, buf)` and apply the stack rule above (about 40 lines; the audit build has a working `GameDamage` in `Explosions.cs`). Add `DeformationRatio` (from `integrity.deformation_ratio`, default 1) to `BlockDef`. Re-run every rocket result (Wasp, Basilisk, Pirate missile turret, Stinger rocket variants).

### W-02 (high) Gatling cadence: 600 rpm effective, 700 in the sim

- **Sim** (`Weapons.cs`, `IntervalS = 60/rpm`, phase-locked): 700 rpm exactly on average = 11.67 shots/s.
- **Game**: `MyWeaponAmmoData.ShootIntervalInMiliseconds = (int)(1000/(RateOfFire/60))` = 85 ms for 700 rpm; `MySmallGatlingGun.CanShoot` and `MyLargeGatlingBarrel.StartShooting` refuse until `now - m_lastTimeShoot >= 85` with `m_lastTimeShoot = now` at each shot and `now` the frame's wall-clock ms at 60 Hz. 5 frames = 83.3 ms < 85 ms, so every shot costs 6 frames = 100 ms = 600 rpm (10 shots/s) as long as the game runs at about 60 UPS. The wiki says "600 RPM", DPS 900 for all three gatling variants; the data sheet's 700 is the nominal value.
  Other weapons are unaffected in practice (autocannon 400 ms = 24 frames, small launcher 1000 ms = 60 frames, 90 rpm turret 666 ms = 40 frames).
- **Effect** (`--audit-gun-cadence true`, 20 seeds): pirate/2xHornet-vs-Pirate median ttk 96.7 -> 117.8 s (+22 %), avg duration 101.8 -> 123.7 s; pirate/Hornet-vs-Pirate ttk 61.4 -> 64.6 s; Hornet-vs-Hornet unchanged win split (6 vs 5 of 20) but 1v1 gatling durations ~ +10 %. The Pirate's own two gatling turrets slow by the same factor.
- **Confidence**: medium-high (source code + wiki agree; assumes the current game keeps the cadence rule).
- **Fix**: model the gun as `required ticks = ceil(int(1000/(rpm/60)) / 16.667)` between shots, last shot time reset at each shot (no phase lock); keep `RateOfFire` for everything else. Cheap in-game check: one gatling, one 140-round box, time to empty: 14.0 s (600 rpm) vs 12.0 s (700 rpm).

### W-03 (medium) ShotsInBurst + ReloadTime not applied with infinite ammo; slug path ignores burst count

- **Sim**: `ReloadTimeMs` is only used when `InfiniteAmmo=false` and a magazine empties (`TakeRound`), and for railgun/artillery/assault "slugs" as a flat reload after each shot (`FireSlug`). With the default (infinite ammo) no gatling turret, rocket launcher or rocket turret ever reloads, and artillery/assault turrets (`shots_in_burst 2`) wait the full reload after every shell.
- **Game / data**: `MySmallMissileLauncher.Shoot`: after `ShotsInBurst` rockets `m_nextShootTime = now + ReloadTime`, independent of ammo. The wiki `Block_Weapons` Fire Rate and Reload columns confirm the data semantics for turrets too: Artillery Turret 10 rpm (data: 2 shells per 0.75 s then 12 s = 9.4 rpm), Assault Cannon Turret (large) 20 rpm (2 shells per 6.3 s = 19 rpm), small turrets 10 rpm (burst 1, 6 s), static artillery 5 rpm, gatling turrets reload 4 s/6 s, large rocket launcher 4 s, rocket turrets 4 s/6 s.
- **Deviation per weapon** (sustained fire, sim value / game value): large gatling turret (140 rounds, 4 s) 11.67/s vs 140/(14+4) = 7.8/s at 600 rpm (1.5x too fast, together with W-02); Basilisk `LargeMissileLauncher` (19 rockets, 4 s) 2.0/s vs 19/13 s = 1.46/s (1.37x); large rocket turret (6, 4 s) 1.5/s vs 0.82/s (1.8x); large artillery turret and large assault turret: sim 1 shell per reload, game 2 (sim 1.9x too slow). Pirate DPS: 2 gatling turrets 2100/s sim vs about 1400/s; missile turret 750/s vs about 410/s.
- **Effect** (`--audit-burst-reload true` with cadence, 20 seeds): pirate/Basilisk-vs-Pirate damage taken by the Basilisk 29.9 k -> 28.4 k, Pirate 87.9 k -> 82.6 k; 2xHornet ttk unchanged (117.8 s): small in the Pirate duels because hits, not cadence, limit them.
- **Fix**: count shots per weapon and apply `ReloadTimeMs` after `ShotsInBurst` shots for all modes; do the same in the slug path; with finite ammo keep magazine swaps separate (and note `MagazineCapacity` of a rocket is 1, which makes the finite-ammo path reload after EVERY rocket, `Weapons.cs:178`).

### W-04 (medium) Bullet range is randomised; the sim uses a fixed 800 m

- **Sim**: `MaxTrajectory` exact (`Projectiles.cs`, "expire after MaxTrajectory metres").
- **Game**: `MyProjectile.Start`: `m_maxTrajectory = MaxTrajectory * GetRandomFloat(0.8, 1.2)` in the 2022 source; the wiki table footnote says "Range randomized between 80 % and 100 % of the printed value on every shot" for gatling, autocannon and their turrets (not for rockets: `MyMissile` uses `MaxTrajectory` as is). `docs/research.md` marked this "not in the data files [I, remove...]"; it is in the code, not the data.
- **Effect** (`--audit-range-lo 0.8 --audit-range-hi 1.0`, bullets only): pirate/Basilisk-vs-Pirate Basilisk hit rate 0.61 -> 0.43, Pirate gatling hit rate 0.83 -> 0.37 (Pirate fires from 800 m), median ttk 53.8 -> 78 s. Hornet duels (350 m) unaffected.
- **Fix**: per-bullet range factor U(0.8, 1.0) (wiki) or U(0.8, 1.2) (source); decide with a range test (see W-02 style: fire at a far wall and read the impact distance distribution).

### W-05 (medium) Spread distribution

- **Sim**: default `SpreadUniformInAngle=false`: direction uniform over the solid angle of the cone (mean off-axis angle 2/3 of the maximum). Setting comment "[I]".
- **Game**: `MyUtilRandomVector3ByDeviatingVector.GetNext`: polar angle theta = `GetRandomFloat(-max, max)`, azimuth phi = `GetRandomFloat(0, 2pi)`, i.e. |theta| uniform (mean off-axis = max/2), which is the sim's `SpreadUniformInAngle=true` option. Bullets use the hashed variant (`GetRandom(hash,...)`: a 1024-entry table, the same `hash` for theta and phi, so each hash gives a point on a spiral instead of filling the disc; the hash is `shotCounter + direction.GetHashCode()`). Rockets use the plain variant.
- **Effect** (`--spread-uniform-in-angle true`, 20 seeds): pirate/2xHornet-vs-Pirate median ttk 96.7 -> 78.2 s (-19 %), Hornet-vs-Pirate 61.4 -> 48 s (-22 %), Pirate gatling hit rate 0.23 -> 0.35. The numeric value of the cone (0.15 deg gun, 0.3 deg turret, 0.1 deg launcher) is right.
- **Fix**: default `SpreadUniformInAngle=true`; optional: hash-table spiral for gun bullets.

### W-06 (low) Projectiles inherit v + w x r instead of the grid's linear velocity

- **Sim** (`Weapons.cs:231,234`): `body.VelocityAtWorld(muzzle)` = CoM velocity plus rotation at the muzzle.
- **Game**: `m_gunBase.Shoot(Parent.Physics.LinearVelocity)` (gatling, `MySmallGatlingGun.Shoot`), `ShootMissile()` uses `Parent.Physics.LinearVelocity`; turret barrels the same. A yawing drone (up to 2 pi rad/s, muzzle 2-3 m off the CoM) therefore gives the sim up to 10-15 m/s of extra sideways velocity (0.7-1.0 deg at 800 m/s) that the game does not have. Scripts assume the game behaviour.
- **Effect** (`--audit-linear-shoot-vel true`): 2xHornet-vs-Pirate A wins 1 -> 4 of 20, hit rates -2 points; small.
- **Fix**: use `body.LinearVelocity`.

### W-07 (high) Armed warheads: sim needs block destruction, game detonates on weapon fire and contact

- **Sim**: `Damage.ApplyCore` queues a detonation only when an armed warhead's Hp reaches 0 (486 Hp small); no contact trigger; `Detonate()` needs `IsArmed`.
- **Game**: wiki Warhead page: "After being armed, a Warhead is set off by collision with other blocks or voxels, by damage from thruster plumes, by weapon fire, by neighbouring warhead explosions"; "An armed warhead will explode very easily after just one shot"; unarmed ones "just get destroyed like regular blocks after a few shots"; a countdown works without arming; the manual Detonate button needs arming (sim agrees on those two). Source (2022): `MyWarhead.ContactPointCallback` explodes when the separating velocity exceeds 5 m/s (that version has no `IsArmed` test); `OnDestroy` explodes at once when the damage type was Bullet and after a random 0-500 ms otherwise; radius = `min(30, (1 + 0.024 n) * 4.4883)` with n warheads within 1.7 m (small grid; 8.5 m large), all of them marked to explode together.
- **Why it matters**: `docs/kamikaze-defence.md` counts Stingers "shot without detonating"; in the game an armed Stinger (the script arms inside `ArmRange` 200 m) that is hit explodes. The defence results are therefore optimistic, and a Stinger that touches anything above 5 m/s detonates by itself.
- **Confidence**: wiki statement high; "one shot" and the exact trigger rule medium (current build unverified). The 5 m/s contact rule is from the old source.
- **Fix**: armed + any damage from a weapon -> detonate; armed + contact closing speed > 5 m/s -> detonate; radius scaling (info-level).

### W-08 (medium) No ricochet for assault-cannon / artillery shells

- **Data** (`ammo.MediumCalibreShell/LargeCalibreShell`): `MissileMin/MaxRicochetAngle` 40/70 and 50/80, probabilities 0.25/0.85, `MissileRicochetDamage` 2000/7000. Wiki: artillery ricochets with 85 % at 0-10 deg surface angle falling linearly to 25 % at 40 deg and never beyond; on a ricochet only 7000 (assault 2000) damage goes to the block and is subtracted from the pool. Assault: 85 % up to 20 deg, 25 % at 50 deg.
- **Sim**: `ModeSlug` walks blocks until the pool (17000 / 4000) is empty at any angle. Grazing hits on a hull side are over-rewarded; shells aimed at angled armour in the game do about 40 % (7000/17000) of the damage.
- Affects the Seeker artillery turret (`LargeCalibreTurret`), pirates with assault cannons if any are added, and any future ship with these guns.
- **Fix**: at the first block hit, compute the surface angle from `RayHit.NormalWorld`, roll the ricochet probability from the four data values, apply the ricochet damage and end (or deflect) the shell.

### W-09 (medium) Collision damage uses a placeholder formula

- **Sim**: `CollisionDamageScale 0.0005 * 0.5 * mu * vn^2` shared over blocks within 1.5 m (22 HP for two 1.6 t walls at 10 m/s). Marked [I].
- **Game**: `MyGridPhysics.PerformDeformation/ApplyDeformation`: below 2 m/s separating velocity nothing happens; above, a deformation offset `(0.3 + max(0,(|v| - dv)/15))*3` m (hard cap 20 m) is applied over a soft area (1.2 m planar small grid, 4 m large) and every block whose `DeformationRatio * deformation` exceeds `0.7 * gridSize` is REMOVED regardless of its Integrity. So a 10 m/s ram deletes the blocks in a patch about 1 m deep; armour with low DeformationRatio (heavy 0.16-0.4) resists, light blocks do not.
- Consequences: wingmate collisions, Stinger ramming and Longbow L mutual-kill results are optimistic about survival; the sim cannot be calibrated to the energy formula. Not quantified (needs a different mechanism).
- **Fix**: implement the deformation rule (soft-area patch, removal threshold) or at least a speed threshold 2 m/s and patch-deletion.

### W-10 (low) Fixed guns fire without power; power draws unmodelled

- Game `CanShoot` returns OutOfPower unless `ResourceSink.IsPoweredByType(Electricity)`; `MAX_REQUIRED_POWER_SHIP_GUN = 10/50000 MW = 0.0002 MW`, turrets `100/50000 = 0.002 MW` (the sim's `TurretIdlePowerMW 0.002` is exactly right). `WeaponUnit.Update` only tests `IsFunctional && Enabled` (`Weapons.cs:~118`), so a drone with dead batteries keeps shooting in the sim, turrets stop (as the game). Effect small (batteries last the whole fight in all logged runs; `power_zero_at_s`).

### W-11 (low) Turret aim, lead and target choice differ from `MyLargeTurretBase`

- Game (2022 source): fires when elevation error <= 0.1 rad (5.7 deg) and azimuth within 0.001 rad of its own predicted point (so it must be fully converged in azimuth, elevation may lag); prediction is its own formula using `DesiredSpeed` (200 for the rocket, no acceleration correction in that source) with the turret's velocity subtracted, evaluated on the target BLOCK's position; targets are individual blocks (nearest block, decoys first, targeting groups since 1.19x), searched out to `Range + 100`.
- Sim: exact first-order intercept, rocket speed (100+200)/2, aims at the body's centre of mass (or the targeting-group block), fire cone 1 deg total. Likely makes sim turrets a little more accurate than the game's; the sim's range and tracking rule (`Range + 100`, kept target) match the game, as do rotation speeds (`RotationSpeed 0.0015 rad/ms` = 1.5 rad/s etc.) and elevation limits (read from `CubeBlocks`).

### W-12 (low) Own hull does not block the first 0.2 s of fire

- Sim ignores the shooter's body for `ProjectileShooterIgnoreS 0.2 s` (160 m for a bullet). Game: the ignore list is the gun block itself (`m_shootIgnoreEntities = { this }`), so own structure in front of the muzzle stops the bullet. Matters for turrets that can aim across their own hull (Pirate roof turrets, limit -43 deg) and for guns with a block ahead of the muzzle. Rockets are similar (spawn at the muzzle; the server adds no offset).

### W-13 (info) Hit volumes are whole cell boxes

- `Raycast.cs` "Blocks fill their whole cell box (conservative)". Slopes, corners, thin blocks and the open parts of thrusters/turrets are mesh-shaped in the game (hit position -> `GetTargetedBlock`). The sim therefore hits slightly more often than the game on slope-heavy hulls. Not quantified.

### W-14 (low) Projectiles cannot hit each other

- Game: `MyMissile` is a destroyable object (any damage detonates it), explosions detonate missiles within 2R, and railgun/artillery shells are missiles that turrets with "Target Rockets" can shoot (wiki). Gatling fire can therefore kill incoming rockets; the sim lets everything pass. Matters for Basilisk/Wasp salvos against a gatling-armed target and for Stinger swarms; effect probably small (needs an exact bullet to cross a 45 kg rocket).

### W-15 (medium) Blueprint weapon wiring (`tools/shapes.json` wildcards)

- Checked in the generated `bp.sbc` (private build): 
  - `SmallMissileLauncher/*` is used by `SmallMissileLauncher` and `LargeMissileLauncher` but carries `<Volume>0.3` and `CurrentAmmoMagazineName LargeCalibreAmmo` (the artillery gun). Definitions: 0.24 m3 (4 rockets) and 1.14 m3 (19 rockets). `MyInventory.Init` takes a saved `Volume` over the definition, so a pasted Basilisk launcher would hold 5 rockets, not 19. The magazine name is harmless (`MyGunBase.Init` falls back to the first magazine of the weapon).
  - `SmallGatlingGun/*` is used for `SmallBlockAutocannon`: Volume 0.064 vs definition 0.072 (2 clips instead of 3).
  - `LargeRailgun` (Longbow L) falls to `SmallMissileLauncherReload/*`: Volume 0.24 is right, but there is NO `MyEntityCapacitorComponent` (only the small railgun's own shape has it) and the magazine name is `Missile200mm`. `sebp.py` itself notes that a present ComponentContainer must hold every default component. Unknown what a large railgun without a capacitor does (never fires, or fires without charging). The sim gives it a full capacitor, so its Longbow L results assume the capacitor exists.
  - Turrets and the Basilisk's turret have no known shape (guessed).
- Only matters in worlds that enforce ammo (research.md T8) and for the large railgun in general. Fix: add `LargeRailgun` (StoredPower 0.5, check the Starburst/Keen sample) and per-subtype container volumes; test the Longbow L before trusting any result.

## Parameter-by-parameter table (sim value / game value / verdict)

| Parameter | Sim | Game / data | Verdict |
|---|---|---|---|
| Gatling RoF | 700 rpm phase-locked | 85 ms interval, 6 frames = 600 rpm effective (wiki 600) | WRONG (W-02) |
| Gatling magazine 140, box 16 L, mass 35 kg | from `magazines` | same | correct (finite mode: 4 boxes = 560 rounds) |
| Gatling bullet speed / range / damage | 800 m/s / 800 m / 90 | `LargeCaliber` 800 / 800 / 90 (blocks), 33 characters | speed, damage correct; range fixed vs randomised (W-04) |
| Bullet hits | first block only, then consumed | `MyProjectile.Update` kills after one hit | correct, no penetration |
| Bullet inherits shooter velocity | yes (+ w x r) | `initialVelocity + dir*speed` with grid linear velocity | correct, minor error (W-06) |
| Spread | 0.15 deg gun / 0.3 turret (data), solid-angle uniform | theta uniform (-max, max), hash-correlated phi | value correct, distribution wrong (W-05) |
| Autocannon | 150 rpm, 500 dmg, mag 16, 800 m/s | `AutocannonShell` same; 400 ms = 24 frames | correct |
| Small rocket | 60 rpm, 0.1 deg, 500 dmg r 4 m | `Missile`/`SmallMissileLauncher` same; 1000 ms | numbers correct; blast model wrong (W-01) |
| Rocket flight | v0 = ship v + 100 along barrel, +600 m/s2 along barrel, total speed cap 200, range 800 (path length) | `MyGunBase.AddMissile` `initialVelocity += dir*InitialSpeed`; `MyMissile`: `LinearVelocity += Forward*Accel*dt`, `MaxLinearVelocity = DesiredSpeed`, explodes at 800 m from origin | correct (the "curving rocket" model is the real one) |
| Large launcher | 120 rpm, no burst | 19-round burst then 4 s | WRONG (W-03) |
| Rocket/gatling turrets | no reload | burst 6 / 140 then 4-6 s (wiki) | WRONG (W-03) |
| Small railgun | 500 ms ShotDelay, 4 s reload, capacitor 0.016 MWh, 3.6 MW x 0.8 = 20 s | wiki: 16 kWh, 3.6 MW, 20 % loss, 20 s (variable +/- 10 s); data ShotDelay 500 | correct (variance not modelled, info) |
| Large railgun | 2000 ms ShotDelay, 0.5 MWh, 38 MW x 0.8 = 59.2 s | wiki: 500 kWh, 38 MW, 60 s | correct |
| Railgun slug | pool 8000 / 50000 walks blocks (min(pool, Hp)), speed 1000 / 2000, 1400 / 2000 m, spread 0 | wiki: penetration damage 8000 / 50000, one block wide; data | correct |
| Artillery / assault | pool 17000 / 4000, 500 m/s, 2000 / 1400 m, reload 12 / 6 s | wiki same | pool/speed/range correct; no ricochet (W-08), burst 2 ignored (W-03) |
| Turret range | `Range` capped at `MaxRangeMeters` (800 large / 600 small), +100 search, AI block extends | `m_searchingRange = shootingRange + 100`; slider max = `MaxRangeMeters` | correct (the AI-block extension is a community report, unverified) |
| Turret rotation / elevation | `RotationSpeed*1000` rad/s (1.5 gatling, 1.0 missile, 0.4 assault, 0.2 artillery), limits from definitions | `step = m_rotationSpeed * ms` | correct |
| Turret power | 0.002 MW | `MAX_REQUIRED_POWER_TURRET = 100/50000` | correct |
| Fixed gun power | none | 0.0002 MW and IsPowered required | minor (W-10) |
| Warhead radius / damage | 4.4883 m / 22.4415 m, 15000 (read from data) | same (Keen source comments the same radii) | correct; cluster scaling `1 + 0.024n` not modelled (info) |
| Warhead triggers | Detonate (armed), countdown, destroyed while armed | + weapon fire, contact > 5 m/s, plume, neighbours | WRONG (W-07) |
| Warhead countdown default | 10 s | `CountdownMs` default 10000 | correct |
| Explosion damage per block | `damage*(1-d/r)` | volumetric with absorption | WRONG (W-01) |
| Block HP / critical | `MaxIntegrity`, `CriticalIntegrity` from components | `MySlimBlock` integrity, no armour damage multiplier in data (`general_damage_multiplier` 1.0 or absent for all 1503 blocks) | correct (no small/large-grid damage multiplier exists in data or source) |
| Deformation from bullets | off | cosmetic bone deformation, `0.000664*MassDamage` m; negligible for gatling, 0.33 m for autocannon | acceptable |
| Decoys | working decoy body preferred, aim at nearest working decoy | wiki: ~10x priority, decreases with distance; source: nearest decoy beats any non-decoy | approximately correct |
| Friendly fire | on, by relation | projectiles hit any grid except the gun block | correct |
| Infinite ammo | default on | `CreativeMode` skips the ammo check; real world type unresolved (T8) | correct for creative |
| Recoil / hit impulse / explosion push | none | `BackkickForce` 0.2-150 kN for one frame, grids get no explosion impulse in the volumetric path | negligible |
| ShootOnce / Shoot_On / Shoot_Off actions | present | terminal control ids `ShootOnce`, `Shoot` with On/Off actions (verified in `MyUserControllableGun`) | correct (this removes the "name from memory" doubt for the Wasp) |

## Things I could not check

- The post-2022 source: railgun capacitor mechanics, `ShotsInBurst` for turrets (only the wiki confirms), ricochet details, AI-block range extension, decoy priority formula, current `MyWarhead` triggers.
- `EntityComponents.sbc` values beyond what the wiki states (3.6 MW, 38 MW, 20 % loss, 16 kWh, 500 kWh agree).
- Turret target filters in the current build (`Missiles` default).
- A running game: all W-items marked medium confidence would be settled by these short in-game tests: (a) one gatling with one 140-round box, time to empty (14 s vs 12 s); (b) one rocket into a 5-thick armour wall, count destroyed blocks behind the first; (c) fire a gatling at a far wall and read the impact distances (range spread); (d) shoot an armed small warhead once with a gatling; (e) fire a large-railgun blueprint paste and read its capacitor in the terminal.
