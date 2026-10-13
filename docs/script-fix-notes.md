# Script fix notes (patterns the port agents copy)

One section per script, written by the agent that fixed it.

## Hornet

File: `scripts/HornetDroneAI.cs` (also flies the Watcher). Every pattern below is written so that it can be pasted into the
other combat scripts (Aegis, Harrier, Lancer, Wasp, Basilisk, Longbow, Mantis; Gnat where it has cameras). The names are the
Hornet's; the other scripts use the same fields (`_tgt`, `_contacts`, `_cams`, `_mates`, `_ai`, `_aiOn`, `_rc`, `Valid`,
`AddContact`, `CamFor`, `Forwardish`). Sim numbers are in the report of the hornet lead (60 seeds, same seeds before and after).

### H1. Gatling projectile speed 400 -> 800 (`ProjSpeed`, line ~38)
- `double ProjSpeed = 800;` the config key stays `Projectile Speed`. Gatling and autocannon are 800 m/s in the game data
  (docs/stats.md). With 400 the lead was twice too long (hit rate 1.8 % in locked Hornet duels, 25-30 % after).
- Port: Gnat (`GnatSwarmAI.cs:38`). A script whose saved CustomData still says `Projectile Speed=400` keeps 400 after a paste; tell the
  user to delete that line or the whole `[Hornet]` section.

### H2. AI-confirm rays that really cover the boresight (`AiStep`)
Bug: `r = 3*sqrt((i%13+.5)/13)` never casts closer than 0.59 deg to the nose, a small ship is 0.2-0.8 deg wide at 300-1,200 m, and
each ray was 2,600 m long (one per 1.3 s). Fix, pattern to paste (replaces the `foreach (var c in _cams)` block of `AiStep`):
```csharp
bool longCam = true;
foreach (var c in _cams)
{
    if (!c.IsWorking || !Forwardish(c)) continue;
    double len = longCam ? AiConfirmLong : AiConfirmRange;   // first forward cam 2600 m, the others 1300 m
    longCam = false;
    if (c.AvailableScanRange < len) continue;                // charge builds 2 km/s per camera
    _aiScan++;
    double r = 0, th = _aiScan * 2.39996323;                 // every 2nd ray is exactly down the boresight
    if ((_aiScan & 1) == 1) { int j = (_aiScan >> 1) % 16; r = 0.15 + 2.35 * Math.Pow((j + 0.5) / 16.0, 1.5); }   // 0.15 .. 2.5 deg spiral
    var e = c.Raycast(len, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
    if (Valid(e)) AddContact(e, "AI");
}
```
- Every forward camera may cast on every tick (no `break`); the charge is the only limiter. Config: `AI Confirm Raycast Range` (1300),
  `AI Confirm Long Raycast Range` (2600).
- Effect (default scenario, Hornet): share of ship-runs that ever held a script target 10 % -> 73 % (97-100 % with starts under 2.4 km);
  first script target at about 30 s instead of 90 s.

### H3. Lock maintenance and re-acquisition (`MaintainLock`, `ReacquirePoint`, `Uncertainty`, `NoteLost`, ghost target)
Measured cause of "lock lost after 5-18 s": a camera banks only 2 km/s, a ray at 800 m costs 800 m, a miss costs as much as a hit and
about half the rays at the bounding-box centre miss a small ship; two nose cameras give about 5 rays/s, and once three rays in a row
miss, the prediction error grows with age squared and the lock never comes back. What changed:
1. **Cheap rays**: `CamFor(aim, 0)` now requires charge >= the distance to the aim point (it used `dist+20`), `ProbePoint` runs the ray
   only `min(Radius,12)` m past the centre and scatters 0.35 x radius (was 0.6), aim-point probes only 1 ray in 4 and only while the
   sighting is fresh (`CamFor` takes `Math.Max(range, len)`).
2. **Stale lock = two rays per tick on a widening spiral** around the extrapolated position (pattern below), uncertainty
   `sigma = 0.35 R + 0.5 max(AccMag,6) age^2` capped at 80 m.
3. **Ghost target**: the current non-IGC target is not dropped after `TrackTimeout` (4 s) but kept `GhostTime` (8 s) in `PruneAndSelect`
   (`double lim = (kv.Value == _tgt && kv.Value.Src != "IGC") ? GhostTime : TrackTimeout;`). Selection adds `+ max(0, age-1) * 150` to
   the score so a fresh contact wins over a stale one. The guns only fire at a track younger than `FireStaleMax` (2 s, config
   `Max Track Age To Fire (s)`), and `ShareTarget` only broadcasts a track younger than 1 s.
4. **Hand-over to the AI blocks while stale**: in `AiStep`, `want` is also true when `_tgt != null && _now - _tgt.Seen > 3`. The AI keeps
   the nose on the enemy and the H2 rays refresh the same track id, which switches the AI off again. `MaintainLock` returns at once
   while `_aiOn` (the cameras' charge belongs to the confirm rays).
5. **Search memory**: `NoteLost(track)` stores position/velocity/time when a ghost is finally dropped; `AutoScanStep` aims every other
   ray at `lostPos + lostVel*min(age,10)` with a spiral radius `min(250, rad + 6*age)` for 25 s (needs a camera whose cone holds the point).
6. **Share the loss**: swarm status flag `4` = "my lock is stale" (`LockStale`: own-source target older than 1.2 s). A mate that has the
   same target id with a sighting younger than 0.4 s answers with `ShareTarget()` at once (rate limit 0.3 s) instead of waiting for its
   0.5 s cycle. IGC fixes arrive as `Src == "IGC"` and refresh the ghost.

Spiral across the line of sight (paste into `MaintainLock`):
```csharp
Vector3D ReacquirePoint(Track t, Vector3D centre, Vector3D dir, double age)
{
    double sigma = Uncertainty(t, age);
    _spiralIdx++;
    double rr = sigma * 1.5 * Math.Sqrt(((_spiralIdx % 17) + 0.5) / 17.0), th = _spiralIdx * 2.39996323;
    Vector3D u = Vector3D.Cross(dir, Math.Abs(dir.Y) < 0.95 ? Vector3D.Up : Vector3D.Right); u.Normalize();
    Vector3D v = Vector3D.Cross(dir, u);
    return centre + u * (rr * Math.Cos(th)) + v * (rr * Math.Sin(th)) + dir * Math.Min(t.Radius + sigma * 0.5, 25);
}
// in MaintainLock:  int rays = age > 0.6 ? 2 : 1;  aim = age > 0.6 ? ReacquirePoint(...) : ProbePoint(...);  var cam = CamFor(aim, 0);
```
Scripts with `MaintainLock(double)` (Longbow, Mantis) keep their signature; only the point selection, the `CamFor(aim, 0)` charge rule and
the ghost/stale gates need porting.
- Effect (locked start 800-950 m, AI off, Hornet-Hornet): median kill 130 s -> 17 s, hit rate 1.8 -> 30 %, share of runs with a kill
  45 % timeouts -> 30 %. Hornet-Wasp 25 -> 77 % wins.

### H4. Guns stuck off after an AI hand-over (`SetFire`, `AiStep`) - found while checking H3, affects every hybrid script
The script only sent `Shoot_On`/`Shoot_Off` when its own flag `_firing` changed. After the AI blocks (in the sim: the AI stand-in
hands back control and shuts the guns) were switched off by the script, the guns were off while the script still believed it was
firing: Hornet-vs-Longbow S held a script target for 250 s at 350 m, aim error 0.0 deg, `Shoot_On` never re-sent, 0 shots. Fix:
```csharp
void SetFire(bool on)
{
    if (on == _firing && !(on && _tick % 30 == 0)) return;   // re-send "on" twice a second
    _firing = on;
    foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off");
}
// and in AiStep when the AI state flips:  _firing = false;
```
- Effect: Hornet-vs-Longbow S wins 68 % -> 93 % (starts 1.5-2.4 km); timeouts 15 % -> 3 %. Every script that has `AiStep` + `SetFire`
  has the same pattern (Aegis, Harrier, Lancer, Mantis, Wasp, Basilisk, Longbow). Whether the real AI block touches the guns is unknown
  (the sim assumes it does), the re-send is harmless either way.

### H5. Wingmate separation (`Separation`)
Repulsion velocity added to `want` in both translation branches, from the `_mates` (IGC `_SWARM`) positions:
```csharp
Vector3D Separation(Vector3D me)
{
    Vector3D push = Vector3D.Zero;
    foreach (var m in _mates.Values)
    {
        double age = _now - m.Seen; if (age > 1.5) continue;
        Vector3D d = me - (m.Pos + m.Vel * age); double len = d.Length();
        if (len < SepRange && len > 0.5) push += d / len * (SepSpeed * (1 - len / SepRange));   // 70 m, 25 m/s (small grids); use ~150 m for large
    }
    return push;
}
// want = tv + dir*vr + tan*orbit*_orbitSign + jink + Evade() + Separation(me);
```
Config `Wingmate Separation Range` / `Wingmate Separation Speed`.

### H6. Friendly-fire gate from IGC positions (`MatesInLine`)
The old camera check ran every 20th tick and returned "clear" when no camera had charge. New, no camera needed, every tick while the
trigger is down:
```csharp
void MatesInLine(Vector3D gunPos, Vector3D gunFwd, double dist)
{
    foreach (var m in _mates.Values)
    {
        double age = _now - m.Seen; if (age > 1.0) continue;
        Vector3D rel = m.Pos + m.Vel * age - gunPos;
        double along = Vector3D.Dot(rel, gunFwd);
        if (along < 5 || along > dist + 10) continue;
        if ((rel - gunFwd * along).LengthSquared() < MateClear * MateClear) { _holdFireUntil = _now + 0.4; return; }   // MateClear = 14 m
    }
}
```
For rockets/slugs extrapolate the mate to the projectile's arrival time (`age + dist / speed`) and use a clearance of mate radius + blast radius + 5 m.

### Not changed / for the other agents
- Scripts-only search (`ai off; hunt`, no lock) still finds nothing: a 3 m radius target covers 1e-4 of the 35 deg scan cone, two cameras
  give 3 rays/s. The Hornet always has its AI blocks in the game; only the AI blocks can find a small grid beyond 120 m.
- Hornet stand-off, orbit and jink were left alone (not limiting in any trace).

### Measured effect (private sim, 60 seeds, same seeds before/after; Hornet side; "ttk" = median kill time of Hornet wins)
| scenario / mode | before: win / timeout / hit % / script-target runs | after |
|---|---|---|
| Hornet-Hornet, default 1.5-3 km | 30 / 28 / 23.5 / 10 % | 35 / 28 / 25.1 / 73 % |
| Hornet-Longbow S, 1.5-2.4 km | 75 / 3 / 29.1 / 3 % | 93 / 3 / 37.3 / 100 % |
| Hornet-Wasp, 1.5-2.4 km | 67 / 2 / 17.2 / 12 % | 95 / 0 / 40.7 / 100 % |
| Hornet-Hornet, 1.5-2.4 km | 38 / 0 / 27.0 / 11 % | 47 / 3 / 25.8 / 98 % |
| Hornet-Pirate, default | 0 / 0 / 53.5 / 98 % (dealt 28k) | 0 / 0 / 95.8 (dealt 53k) |
| lock at 800-950 m, AI off, Hornet-Hornet | 25 / 45 / 1.8 / ttk 130 s | 38 / 30 / 30.1 / ttk 17 s |
| lock at 800-950 m, AI off, Hornet-Wasp | 25 / 28 / 2.2 | 77 / 13 / 35.6 |
| lock at 800-950 m, AI on, Hornet-Wasp | 40 / 2 / 4.3 | 95 / 0 / 36.7 |
| 2xHornet-3xWasp, 1.5-2.4 km | 15 / 17 / 8.5, coll 14 | 93 / 0 / 30.8, coll 0 |
Default-mode timeouts (28-45 %) are the sim's 2.5 km AI search range against 1.5-3 km starts, not a script effect.


## Gnat (`scripts/GnatSwarmAI.cs` v1.9, `scripts/GnatMothership.cs` v1.1)

Written by the gnat-swarm agent. Private sim, 20 seeds per scenario, same seeds before/after (the "before" is the v1.8 script,
rebuilt in a private copy of the repo with the same opponent scripts). "Gnat side" columns; swarm = 6/9/12 Gnats vs one enemy.

### Why the Gnat found nothing (audit section 2, confirmed)
The Gnat blueprint has a 50 m sensor and six cameras but **no AI block, no turret**. A ray hits a 3 m ship with probability about
1e-4 per ray inside the 35 deg scan cone, a camera banks 2 km/s of ray length and the Gnat has only one forward camera. Blind
search by raycast beyond a few hundred metres is arithmetically impossible (4 R^3 / r^2 of ray length for a full sphere at range R),
for one drone and for twelve. So the only ways for a Gnat to find anything are: (1) a feed (carrier turrets, Watcher, another
fleet's IGC targets), (2) information the enemy gives away, (3) AI blocks on the Gnat (the v1.8 hybrid is already in the script).
v1.9 does (1) and (2) and measures (3).

### G1. Ported from the Hornet (H1-H4, H5 already existed)
- `ProjSpeed` 400 -> 800 (H1). `SetFire` re-sends `Shoot_On` twice a second, `_firing` reset on AI hand-over (H4).
- `CamFor` charges the exact ray length; `MaintainLock` rewritten with `ReacquirePoint`, `Uncertainty`, `NoteLost`, `LockStale`,
  hull-point probes, ghost target (`GhostTime` 8 s in `Housekeeping`), stale-track score penalty, `FireStaleMax` 2 s, loss sharing
  over `_SWARM` flag 4, `AutoScanStep` search around the last known position (H3).
- `AiStep`: boresight rays + 0.15-2.5 deg spiral, long ray (2,600 m) only when the drone has two or more forward cameras. The Gnat
  has one, so it casts the 1,300 m ray only (a lone camera would starve waiting for 2,600 m of charge) (H2). Inert unless the
  blueprint has `[AI]` blocks.
- Not ported: `MatesInLine` (the Gnat's `LineClear` already uses the IGC mate positions every tick), `Separation` (already there).

### G2. Locked fights (target handed over, 500-700 m, facing, `0.5:lock;0.5:hunt`) - the swarm logic itself
| scenario | before: win / median ttk / hit % | after |
|---|---|---|
| 12xGnat-Hornet | 100 / 7.8 s / 7.3 | 100 / 3.9 s / 18.3 |
| 12xGnat-Aegis | 95 / 6.8 s / 8.7 | 100 / 3.8 s / 20.1 |
| 12xGnat-Midge | 85 / 6.0 s / 3.9 | 100 / 3.2 s / 13.6 |
| 12xGnat-Wasp | 100 / 8.7 s / 8.1 | 100 / 4.2 s / 18.9 |
| 6xGnat-Hornet | 90 / 8.7 s / 5.7 | 100 / 4.9 s / 22.8 |
| 6xGnat-Midge | 100 / 7.3 s / 7.9 | 100 / 4.2 s / 17.6 |
| 6xGnat-Longbow S | 95 / 9.7 s / 13.8 | 95 / 4.7 s / 28.8 |
Slots, attack cone, yo-yo, separation and hold-fire were not the problem; the speed constant and the lock were. Orbit speed, closing
speed, near/far range were left alone (not limiting in any trace).

### G3. Hit bearing + raid (new; `CheckDamage`, `HitVotes`, `HitAxis`, `RaidOn`, `RaidDir`, `RaidScan`, `Control`)
Idea: an enemy that shoots at the swarm tells it where it is. A projectile hits the first block along its path, so the probability
that block B is the one damaged from direction u is proportional to the area of B that the shooter can see, which is the sum over
B's exposed faces of `max(0, u . normal)`. Divided by the same sum over the whole drone (its silhouette from u) it is a likelihood.
- `CheckDamage` (now every 2nd tick, the drone turns fast) finds the blocks whose integrity dropped; `HitVotes` (throttled to 4/s)
  adds `log(w_B(u) / W(u))` for each of 64 world-frame directions u (so tumbling does not matter) with a 12 s forgetting time.
  Exposed-face counts come from `CubeExists` of the neighbour cells (block bounds are cached when the block list is scanned, so a
  destroyed block can still be located). Cost: about 200 `CubeExists` calls and 64 x 20 flops per vote, instruction count not
  measured (`FakeInstructionCount` is 0 in the sim); the throttle is the safety margin.
- `HitAxis` returns the best direction, a strength (decayed hit count x how sharply the likelihood peaks) and the 80 % spread angle.
  The best axis is shared on `<Tag>_HIT` (`MyTuple<Vector3D,Vector3D>`: position, axis) and mixed in by the receivers as a soft vote.
- Measured accuracy (sim, truth taken from the trace): with 1-3 hits the axis is 75-150 deg off (useless), from about 6 hits the
  median error is 13-38 deg. The raid therefore waits for strength >= 2.5 (`Raid Start Confidence`) and holds above 1.2.
- `RaidOn` (ATTACK, or FORM within 0.5 x EngageRadius of home; no target; no AI blocks active): the swarm flies a FAN along the axis,
  each drone in its own `ConePoint` slot (25 deg half angle, 80 m/s, 700 m of travel, then a new pass with the slots shifted by 5),
  with `Separation` and `Evade`. `RaidScan`: every camera whose cone contains the axis casts 600 m rays on a golden-angle spiral over
  the bearing cone (each drone with its own offset in the sequence), the 50 m sensor catches what the fan flies past, and the normal
  attack takes over the moment anything is found (the target is shared over `_TGT`).
- Limits: only works against enemies that hit the swarm with direct-fire projectiles. Rockets (Wasp) and splash give blocks damaged
  without regard to exposure: 12xGnat-Wasp and 6xGnat-Wasp are unchanged. A single railgun slug per 20 s (Longbow S) gives too few
  hits. A lone Gnat (19 blocks) is disabled by the first burst, so 1v1 and 1v2 Gnat fights are unchanged (0 wins, 100 % losses).

### G4. Feeds and carrier
- `Listen To IGC Tags (target feeds)`: `HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER` (`_lFeed`). Same tuple
  format on every fleet, so a Gnat now attacks what its Hornet/Wasp/Aegis escort has locked. Own mates and own grid are filtered.
- `GnatMothership.cs` also broadcasts `<Tag>_HOMER` (grid radius); the Gnats hold station outside the carrier (`HomePad`) instead of
  inside its hull, and RTB stops on a shell around it.
- Watcher (lead's scout mode) shares on `HORNET_TGT`; the Gnats now read it. The Watcher itself still has no sensor that can find a
  small ship, so `Watcher+3xGnat` only gains where the enemy hits the Gnats (table below).

### G5. Measured effect, no lock, Gnat side (before > after), start distance 1.5-2.4 km (removes the sim's 2.5 km AI search range artefact)
20 seeds, same seeds; "before" = v1.8 script, "after" = the final v1.9 script. `win` = Gnat team wins, `lose` = enemy wins, rest timeout.
| scenario | win % | lose % | timeout % | median ttk s | hit % | damage dealt/ship | taken/ship | alive % |
|---|---|---|---|---|---|---|---|---|
| 12xGnat-Hornet | 5 > 35 | 0 > 0 | 95 > 65 | 147 > 192 | 4 > 25 | 47 > 378 | 3182 > 2420 | 40 > 50 |
| 12xGnat-Aegis | 5 > 45 | 0 > 0 | 95 > 55 | 130 > 220 | 37 > 30 | 36 > 623 | 1903 > 1183 | 62 > 75 |
| 12xGnat-Midge | 0 > 20 | 0 > 0 | 100 > 80 | - > 237 | - > 30 | 0 > 186 | 1780 > 1112 | 65 > 77 |
| 12xGnat-Pirate | 25 > 55 | 0 > 10 | 75 > 35 | 114 > 113 | 44 > 89 | 4394 > 11948 | 1774 > 1463 | 61 > 67 |
| 6xGnat-Hornet | 0 > 5 | 95 > 45 | 5 > 50 | - > 266 | - > 39 | 0 > 149 | 4985 > 4634 | 1 > 11 |
| 6xGnat-Midge | 0 > 5 | 20 > 5 | 80 > 90 | - > 132 | - > 37 | 0 > 95 | 3084 > 2364 | 37 > 49 |
| 6xGnat-Aegis | 0 > 0 | 25 > 10 | 75 > 90 | - | - | 0 > 0 | 3306 > 2465 | 35 > 48 |
| 6xGnat-Pirate | 15 > 5 | 30 > 30 | 55 > 65 | 82 > 220 | 59 > 90 | 2838 > 8541 | 2161 > 1949 | 51 > 50 |
| 12xGnat-Wasp, -Longbow S, 6xGnat-Wasp, -Longbow S | 0 > 0 | 0-35 > same | | | | 0 > 0 | unchanged | |
| 12xGnat-Gnat, 6xGnat-Gnat, 1v1 Gnat-Gnat | 0 > 0 | 0 > 0 | 100 > 100 | | | 0 > 0 | 0 | 100 |
| 1v1 Gnat vs Hornet/Wasp/Midge/Longbow S, 1v2 vs 2x Hornet/Midge/Wasp | 0 > 0 | 100 > 100 (Midge 100 > 90) | 0 | | | 0 > 0 | unchanged | 0 |
| Wasp+2xGnat vs Aegis / Hornet / Midge | 35 / 10 / 20 > 35 / 15 / 20 | 45 / 85 / 50 > 35 / 80 / 40 | | 156 / 160 / 165 > 156 / 151 / 165 | | | | |
| Watcher+3xGnat vs Aegis / Hornet / Midge | 0 > 0 | 50 / 95 / 50 > 50 / 90 / 45 | | | | | | |
Default distance 1.5-3 km (same seeds, final script): 12xGnat-Hornet 5 > 5 % (hit 3 > 34 %), -Aegis 0 > 10, -Midge 0 > 15, -Pirate 20 > 30
(hit 50 > 89 %, damage dealt 2.9k > 7.2k), 6xGnat-Hornet losses 70 > 35 %. Default-mode timeouts are the sim: the stand-in AI needs
(D - 730 m) / 10 m/s to arrive, so anything starting beyond about 2.1 km cannot finish inside 300 s, and 33 % start beyond 2.5 km.
Friendly fire 0, collisions 0-2 events per 20 runs (before 0-1). Noise: 20 seeds give about +-10 points on a win rate.

### G6. Gnat with AI blocks (blueprint change, not applied) - the real fix for detection
`tools/designs/core.py::gnat_frame` with one `SmallOffensiveCombat` at (2,1,-1) and one `SmallFlightMovement` at (2,1,-2) (both
`[AI]`-named; the 21-block frame validates in `build_all.py`, accel 6.4 m/s^2 with the two blocks) and the same v1.9 script, private repo, 20 seeds,
1.5-2.4 km:
| scenario | no AI blocks (v1.9) win / lose / timeout | with AI blocks win / lose / timeout, Gnat-side damage taken per ship |
|---|---|---|
| 12xGnat-Hornet | 35 / 0 / 65 | 40 / 0 / 60, 43 (was 2420) |
| 12xGnat-Aegis | 45 / 0 / 55 | 65 / 0 / 35, 54 |
| 12xGnat-Midge | 20 / 0 / 80 | 35 / 0 / 65, 1 |
| 12xGnat-Wasp | 0 / 0 / 100 | 35 / 0 / 65, 0 |
| 12xGnat-Longbow S | 0 / 0 / 100 | 45 / 0 / 55, 76 |
| 12xGnat-Pirate | 55 / 10 / 35 | 100 / 0 / 0, 495 |
| 6xGnat-Hornet | 5 / 45 / 50 | 40 / 0 / 60, 3 |
| 6xGnat-Wasp | 0 / 35 / 65 | 25 / 0 / 75, 0 |
| 1v1 Gnat-Hornet | 0 / 100 / 0 | 0 / 25 / 70 |
| 1v1 Gnat-Midge | 0 / 90 / 10 | 0 / 0 / 100 |
| 12xGnat-Gnat, 1v1 Gnat-Gnat | 0 / 0 / 100 | 0 / 0 / 100 (see "Mirror" below) |
With AI blocks the swarm survives (alive 97-100 %) and finds Wasps and railguns that never hit it. The remaining timeouts are the
stand-in AI's 10 m/s approach, and most of the finding and flying in these runs is the stand-in's ([I] model: 2.5 km search, 10 m/s),
so read the size of the effect as an upper estimate. **Recommendation: add an AI Offensive + AI Flight pair to the Gnat blueprint**
(`tools/designs/core.py`, outside the scripts, so not done here). Without them a Gnat swarm is a spotter-fed weapon: it needs a
carrier with turrets, a Hornet/Wasp/Aegis escort or a working Watcher.

### Mirror Gnat-vs-Gnat
Without AI blocks nobody shoots first, so nobody is ever hit or sees anything: 0 % decisive before and after, with 1, 6 or 12 drones, in
every start distance. It cannot be fixed in the script (arithmetic above). With the AI blocks of G6 on both sides it is still 0 % in the
sim for 1v1 and 6x, and 12xGnat vs one Gnat won only 44 % in an early run: in the mirror the sim's AI stand-in never walks the two
Gnats together (trace: both drift, 2,000 m apart, `ai_on` = 1, no target), while the same Gnat approaches a Midge or Hornet. I did not
find the cause (possibly an engine quirk of `VanillaAi.Search` against a Gnat-sized target, possibly the stand-in's 10 m/s hold). Treat the
Gnat mirror as unresolved in the sim, not as proof about the game.

### Unverified assumptions about the real game
- Block integrity (`IMySlimBlock.CurrentDamage`, `CubeExists`) is readable per block and updates fast enough that a check every
  2nd tick sees a burst; that gatling bullets damage the first block they meet (no wide splash); that `FatBlock.Min/Max` exist for
  multi-cell blocks. The hit bearing estimate depends on all three.
- Instruction cost of `HitVotes` (about 200 API calls + 64 direction loop per vote, 4 votes/s at most) and of `HitAxis` (about 64 exp,
  64 acos per 0.1 s) is not modelled; the in-game limit may force a smaller direction set (`HN`).
- Camera charge cap, sensor behaviour with 50 m extents (`ConfigureSensors` sets every direction) and IGC reach between fleets as in the
  Hornet notes. 14 IGC listeners per PB (5 own + 9 feeds) have not been tried in game.
- Old drones keep their saved `Projectile Speed=400` in CustomData; delete that line or the `[Gnat]` section after pasting v1.9.

## Longbow and Mantis (`scripts/LongbowSniperAI.cs` v1.9, `scripts/MantisAI.cs` v2.1-mantis; `LongbowMothership.cs` unchanged)

Both scripts share their code (Mantis is Longbow plus a gatling group), so every change below is in both files unless it says
otherwise. `LongbowMothership.cs` only relays commands and turret targets and needed nothing. Sim numbers: private build, same
seeds before and after (`before` = the v1.8/v2.0 scripts in the same world with today's Hornet; opponents are the other models as they
are in `out/Blueprints` today, i.e. only the Hornet is ported yet, the rest still fly with the stand-in AI most of the time).

### R1. Ports of the Hornet fixes (H2-H6), what differs for the railgun scripts
- **H2 confirm rays** (`AiStep`): same pattern (1 ray in 2 down the boresight, long 2600 m ray on the first forward camera, 1300 m on the
  others), and every forward camera may cast on every tick. Longbow has six nose cameras, Mantis three.
- **H3 lock keeping**: `MaintainLock(double)` keeps its signature (the main loop calls it with 0.25 and the pre-shot refresh with 0),
  `CamFor` takes `Math.Max(range, len)`, `ProbePoint` is the hull-point version, `ReacquirePoint`/`Uncertainty`/`NoteLost`, the 8 s ghost in
  `PruneAndSelect` (`TrackTimeout` stays 5 s for the others) and a stale-score penalty. `Track.P/V` now trust the acceleration for 2 s at
  most (a ghost must not run away on `0.5 a t^2`). A railgun never fires on a ghost: the pre-shot refresh needs a fix younger than 0.1 s.
- **H4 guns after an AI hand-over**: `SetFire`/`SetGat` re-send "on" twice a second and `_firing`/`_gatFiring` are reset on every flip.
  Longbow's fallback `ProjSpeed = 400` for an unknown gatling is now 800 (the railgun path was always right). Mantis already used
  `GatSpeed` 800, so the "400 m/s gatling fallback" of the audit does not exist in `MantisAI.cs`.
- **Takeover when the lock is stale**: after 3 s, after 1 s when the target is beyond 1.4 km (new). Trace Hornet-vs-Mantis seed 3: the
  script got a lock at 1.9 km and then never saw the target again for 3 s because every refresh ray needs 1.4-2.6 km of charge and a 5 m
  ship at that range is rarely hit (the 3 s of refresh rays all came back empty in the trace); the AI's boresight rays are far better there.
- **H5 separation** (`Separation`): 80 m x2.5 on large grids, 25 m/s. **H6 friendly fire** (`MatesInLine`): see R5.
- **Loss sharing** (flag 4) and `ShareTarget` only for tracks younger than 1 s.

### R2. The railgun shot delay does not belong in the lead (the main railgun finding)
The weapon data has `ShotDelay` 500 ms (small) / 2,000 ms (large) between the trigger and the slug leaving the barrel, and the sim models
it (`Weapons.cs`: `ShootOnce` -> `PendingShot` -> `FireSlug` at `ChargeReadyTime`, direction and velocity taken at that moment). The audit
asked for the delay in the lead. I implemented it (`Lead` aimed at `P(now+delay)` from `shooter + v*delay`) and the hit rate fell from
80-89 % to 4-9 % (locked duels, 500 m, Longbow S vs Hornet / Wasp / mirror, 20 seeds): the ship keeps turning onto the solution of the
present until the slug is away, so the direction it fires in is already the solution for the moment of firing, and adding the delay over-leads by
exactly the delay. What the delay does cost is elsewhere, and that is what the code does with it:
- the ship holds steady while a shot is pending (`aimingPhase` is true while `PendingAt >= 0`; a velocity change during the delay shifts the
  inherited slug velocity by `dv * flight time`), 2 s for the large railgun;
- `UpdateGuns` waits `ShotDelay + 1` s before it calls a trigger "not accepted" (the old 1 s window cut the large railgun's 2 s delay short);
- `DetectWeapon` sets `ShotDelay` (0.5 / 2.0, `Custom Shot Delay Seconds` for unknown weapons, 0 for gatlings).
If the real game fires the slug in the direction the barrel had when the trigger was pulled (not when the charge ends) the lead needs the delay
after all; that is the one thing to check in game (test T9 of docs/research.md).

### R3. Shot confirmation without ammo use (`UpdateGuns`, `Gun.SawShoot`)
The script confirmed a shot only by an ammo drop. In the sim (creative world) there is none, so the script thought the gun was always
ready every 2 s (`SecondsToReady() < 3` all the time) and **never jinked** between shots. A trigger that the gun accepts shows `IsShooting` for
the charge delay, a refused one never does: `SawShoot && !IsShooting` is now "the slug is away" (reload timer `ReloadTime + 0.5`). An ammo
drop still wins when it exists. Whether a real railgun reports `IsShooting` during its charge is unknown; if it does not, the old
"retry in 2 s" path runs as before.

### R4. Stand-off and aim point (the biggest measured effects)
**Stand-off** (`EngageRange`, `PreferRange`, `FireRangeFactor`/`FireRangeSlack`): the old "max accurate range" gave 350-500 m for small
targets. Forced ranges, Longbow S, wins % (`range <m>` command, 40 seeds, 7 opponents):
| stand-off m | 350 | 500 | 700 | 900 | 1100 | old formula |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| both sides locked, 800-950 m start: win % (damage taken) | 45 (4,449) | 57 (3,060) | 69 (1,105) | 75 (1,084) | 77 (859) | 73 (1,446) |

Hybrid AI, 1.5-2.4 km: 350 m 37 %, 500 m 59 %, 700 m 68 %, 1,000 m 79 % (5 % timeouts), 1,300 m 59 % (27 % timeouts), old formula 71 %.
Hit rate falls with range (99 % at 350 m, 67 % at 1,100 m) but the fire taken falls much faster (gatling ships reach 700-800 m).
`PreferRange` = 1,000 m (Longbow, any target), 650 m (Mantis: its gatling reaches 650 m; locked duels, forced 650 m 79 %, formula 72 %, 400 m 71 %,
500 m 72 %, 800 and 1,000 m cost 14 % timeouts); the accuracy formula still decides for big targets. Longbow L (default scenario, 40 seeds x 7 opponents): 600 m 41 %, 900 m 63 %,
1,200 m 61 %, 1,500 m 59 %, 1,800 m 51 %, old formula 52 %. The railgun used to fire at any distance inside 95 % of its range (a Longbow S
shot at 1,410 m, 1.3 s of flight); it now fires only inside stand-off x 1.15 + 50 m. A fire-range factor of 1.4 changed nothing.
**Aim point** (`AimActive`): AUTO now shoots a small-grid target at its centre and a large grid at hull points (the v1.6 rule that came
from the in-game "all aimed at centre mass" report). Small ships, hybrid AI, 5 opponents, 40 seeds, win % hull-point AUTO -> centre:
Longbow S 52 -> 82, Mantis 52 -> 75, Longbow L 42 -> 56. A slug that passes the middle of a compact ship crosses its battery, PB, controller
and engines; a hull point on the surface only grazes. Against the Pirate (large, 60 seeds, win %): AUTO 81 / 52 / 41 (L / S / Mantis), ENGINES
74 / 54 / 37, SPREAD 74 / 25 / 52, FACING 54 / 0 / 48, CENTER 11 / 0 / 9, so the hull rule stays for large grids. The aim window for a
railgun is capped (`Max Aim Window m (railgun)` 2 m, `Max Aim Window m (big target)` 6 m): 2 m everywhere cost up to 17 points against the Pirate (2xLongbow S 33 % at 2 m, 50 % at 4 m).
The railgun damages by pool: one slug through the centre of a small ship does about 3,800; a graze 350-1,500 (the Aegis is "capable" after one
centre hit, not after two grazes).

### R5. Friendly fire and collisions
- `MatesInLine` (every hive mate, from the IGC positions, no camera): exact closest approach between the slug path (`myVel + dir * speed`, from the
  moment it leaves the barrel) and the mate's linear path, tested against 9 m (22 m large). `LineClear` (the camera ray, which used to fail open
  when no camera had charge) now waits up to 1 s for charge and only then goes ahead; the hive check never fails open.
- **AI phase guard** (`AiFriendGuard`, `SetGunsEnabled`): while the AI blocks fly, they also pull the trigger at whatever is in front of the nose.
  Trace `2v3/2xLongbow L-vs-3xMidge` seed 21: the first slug at t = 18 s went into the wingmate 68 m away (13,011 damage); `3xLongbow L-vs-Pirate`
  seed 39: 50,000. The guns are now switched off (`Enabled = false`) while a hive mate or an AI confirm ray that hit a friendly (non-hive
  mate, e.g. the Hornet of a Hornet + Longbow team) is in the line, and `Release()` switches them on again.
- **Separation** (H5): collisions in all my runs (5,100 / 4,400 / 5,100 ship-runs of Longbow S / L / Mantis): 932 / 828 / 76 -> 6 / 28 / 3 events;
  friendly fire 2 / 5 / 3 ship-runs (853 / 90,179 / 1,500 damage) -> 1 / 0 / 0 (862 / 0 / 0). The one left is the hive-less case: a Hornet
  that is not in the Longbow hive crossing the line 0.5 s after the trigger.

### R6. Target switching, wrecks, search after a kill
- `Track.ShotsAt`: after 2 slugs into a small ship (6 into a big one) the target gets +900 in `PruneAndSelect`, so a hive with other contacts moves on.
- `Track.QuietSince`/`Disabled`: a small grid that was hit at least once and has not accelerated (smoothed 0.4 m/s^2, peak 0.8) for 6 s is a wreck
  (sim `capable = false` needs only the PB, the controller, the battery, all engines or all weapons gone, so a wreck keeps 60-70 % of its
  blocks). IGC fixes never count: a mate's extrapolation always looks calm. After the grace time it is ignored for 120 s and the position
  is remembered (`IgnoreTarget`), the nose turns to it for 12 s and the cameras search round it (spiral up to 250 m) for 25 s before the AI
  blocks (which would aim at the nearest enemy, that wreck) take over (15 s).
- Not a fix: **1v2/2v3 timeouts** went up (e.g. Longbow S vs 2 Midges, win / loss / timeout: 0 / 100 / 0 before, 4 / 26 / 70 after) because the Longbow now
  survives and the fights are damage-limited: 1 slug per 22 s, 3,800 damage per centre hit against 35-block ships that need 8+ hits
  (trace: 13 shots, 8 hits, one Midge disabled and one still capable at 300 s). They are not search failures.

### R7. Things tried that did not help (the config switches stay, default off or unchanged)
- `Calm Target Accel` (wait up to 2.5 s for a moment when the target is not accelerating): hit rate 79 -> 65-68 %, wins 75 -> 73-74 % (locked duels).
- `Fire Range Factor` 1.4 instead of 1.15: Longbow S 75 -> 71 %. `Split Targets` off (focus fire) in 1v2/2v3: no difference outside the noise.
- `Jink Speed While Reloading` 0 / 15 / 30: wins 68 / 73 / 73 % (locked duels); the jink only matters when something shoots at the ship.
- Hull-point probing for lock refresh on big targets (the Hornet's pattern is tuned for small ships): Pirate wins 45 -> 42 %, reverted.
- Aim mode ENGINES for every large grid, ReadyAt 2 s earlier: both lowered Pirate and 1v1 wins, reverted.

### R8. Measured effect (100 seeds per cell, same seeds before and after, private sim)
Per model, all opponents pooled; win / loss / timeout %, median kill time s, hit %, damage dealt, damage taken. Before = v1.8 / v2.0 scripts in the
same world (today's Hornet; the other opponents are still mostly flown by the stand-in AI, which is why Longbow S "wins" 95 % against the Aegis,
Harrier and Lancer: they rarely get a script lock and do not close on a ship that sits at 1,000 m. Re-measure when those scripts are ported).
| suite | model | before | after |
| :--- | :--- | :--- | :--- |
| 1v1, 1.5-2.4 km, 11 opponents (1,100 ship-runs) | Longbow S | 32 / 54 / 9, 135 s, 48 %, 10,442 / 4,546 | 71 / 16 / 13, 103 s, 70 %, 12,002 / 668 |
| | Longbow L | 26 / 62 / 5, 66 s, 64 %, 14,866 / 21,686 | 48 / 29 / 19, 84 s, 67 %, 15,133 / 13,997 |
| | Mantis | 30 / 62 / 3, 123 s, 66 %, 7,202 / 4,931 | 54 / 42 / 1, 89 s, 49 %, 7,247 / 3,172 |
| 1v1, default 1.5-3 km, 6 opponents | Longbow S | 26 / 35 / 36, 141 s, 41 %, 3,686 / 2,690 | 45 / 18 / 37, 101 s, 64 %, 4,108 / 750 |
| | Longbow L | 16 / 44 / 40, 67 s, 50 %, 768 / 12,235 | 36 / 20 / 43, 94 s, 59 %, 1,410 / 7,333 |
| | Mantis | 23 / 38 / 36, 131 s, 56 %, 2,723 / 2,702 | 37 / 26 / 35, 93 s, 34 %, 2,550 / 1,498 |
| 1v1 locked start, AI off | Longbow S | 53 / 30 / 14, 27 s, 92 %, 14,749 / 2,577 | 65 / 15 / 18, 5 s, 89 %, 14,562 / 984 |
| | Mantis | 55 / 34 / 10, 30 s, 51 %, 12,430 / 3,158 | 61 / 30 / 7, 26 s, 54 %, 13,169 / 2,542 |
| 1v2 (5 opponent types) | Longbow S | 1 / 96 / 3, 185 s, 30 %, 1,195 / 10,689 | 9 / 38 / 52, 238 s, 61 %, 6,282 / 4,422 |
| | Longbow L | 0 / 96 / 4, 197 s, 42 %, 784 / 29,395 | 2 / 75 / 24, 203 s, 56 %, 1,881 / 22,916 |
| | Mantis | 1 / 98 / 1, 164 s, 27 %, 1,346 / 10,302 | 9 / 87 / 4, 210 s, 27 %, 4,832 / 8,658 |
| 2v3 (Hornet, Wasp, Midge) | Longbow S | 1 / 82 / 17, 157 s, 52 %, 2,300 / 14,536 | 8 / 29 / 63, 273 s, 65 %, 5,710 / 7,585 |
| | Longbow L | 0 / 46 / 54, -, 54 %, 1,814 / 88,416 | 0 / 31 / 69, 221 s, 61 %, 2,672 / 68,440 |
| | Mantis | 0 / 88 / 12, -, 23 %, 2,961 / 15,085 | 13 / 68 / 19, 207 s, 25 %, 6,466 / 11,747 |
| mixed (Hornet + Longbow S, Lancer + Mantis) | Longbow S | 77 / 21 / 2, 116 s, 34 %, 1,415 / 1,890 | 81 / 16 / 4, 99 s, 62 %, 3,019 / 1,062 |
| | Mantis | 71 / 26 / 3, 149 s, 29 %, 1,582 / 4,396 | 70 / 25 / 5, 106 s, 23 %, 3,395 / 4,371 |
| Pirate (1x / 2x / 3x) | Longbow S | 55 / 0 / 45, 154 s, 113 %, 68,278 / 71 | 52 / 0 / 48, 158 s, 111 %, 71,227 / 4 |
| | Longbow L | 73 / 4 / 24, 69 s, 124 %, 69,875 / 9,970 | 80 / 0 / 20, 95 s, 116 %, 68,410 / 0 |
| | Mantis | 34 / 41 / 25, 101 s, 86 %, 84,892 / 6,597 | 46 / 36 / 19, 91 s, 87 %, 83,181 / 5,059 |
Scripts only (`ai off; hunt`, 40 seeds): unchanged. At 400-900 m starts 77-83 % timeouts for all three (the losses are the opponents finding
us), at the default distance 100 % timeouts with 0 shots, before and after. A 5 m ship covers about 1e-4 of the 25 deg scan cone and the
cameras bank 2 km/s, so ray scans cannot find it; only the AI blocks, a turret or a designator can (same finding as the Hornet).
Ship-run totals over the five suites: collisions 932 / 828 / 76 -> 6 / 28 / 3, friendly-fire ship-runs 2 / 5 / 3 -> 1 / 0 / 0 (Longbow S / L / Mantis).
CPU: 0.009-0.014 ms per Main call before and 0.011-0.014 after (sim host time, not the in-game instruction count, which the
sim does not model; the new code adds at most 4 raycasts per tick and a few loops over the hive mates).

Per opponent (win / loss / timeout %, before -> after):

**1v1, starts 1.5-2.4 km, hybrid AI**: win / loss / timeout %, before -> after (100 seeds per cell; mirror cells pool both sides, 200)

| fight | Longbow S | Longbow L | Mantis |
| :--- | :--- | :--- | :--- |
| 1v1 vs Aegis | 24/75/0 -> 95/0/5 | 17/83/0 -> 42/36/21 | 15/77/2 -> 71/28/0 |
| 1v1 vs Basilisk | 26/0/74 -> 31/0/69 | 66/3/30 -> 56/0/44 | 44/52/3 -> 48/49/3 |
| 1v1 vs Harrier | 21/74/4 -> 94/2/4 | 17/81/2 -> 51/33/14 | 12/85/0 -> 56/37/0 |
| 1v1 vs Hornet | 4/94/2 -> 89/3/8 | 5/95/0 -> 33/61/5 | 6/93/1 -> 57/39/3 |
| 1v1 vs Lancer | 16/81/3 -> 91/0/9 | 19/80/0 -> 49/27/24 | 16/78/4 -> 74/22/2 |
| 1v1 vs Longbow L | 71/22/6 -> 42/44/14 | 34/34/1 -> 37/37/9 | 71/25/2 -> 36/61/0 |
| 1v1 vs Longbow S | 42/42/1 -> 49/49/1 | 22/71/6 -> 44/42/14 | 35/50/5 -> 27/72/1 |
| 1v1 vs Mantis | 50/35/5 -> 72/27/1 | 25/71/2 -> 61/36/0 | 41/41/2 -> 48/48/0 |
| 1v1 vs Midge | 23/76/1 -> 74/0/26 | 20/70/10 -> 39/5/56 | 15/82/3 -> 34/61/4 |
| 1v1 vs Wasp | 37/57/6 -> 95/0/5 | 31/64/4 -> 74/8/18 | 36/59/5 -> 96/3/0 |

**1v1, default starts 1.5-3 km**: win / loss / timeout %, before -> after (100 seeds per cell; mirror cells pool both sides, 200)

| fight | Longbow S | Longbow L | Mantis |
| :--- | :--- | :--- | :--- |
| 1v1 vs Aegis | 14/54/29 -> 67/0/33 | 9/45/45 -> 33/12/55 | 10/60/29 -> 43/22/31 |
| 1v1 vs Hornet | 6/56/38 -> 53/3/44 | 10/53/37 -> 23/38/39 | 5/55/40 -> 40/17/41 |
| 1v1 vs Longbow L | 37/22/41 -> 27/35/38 |  | 41/17/42 -> 20/38/42 |
| 1v1 vs Longbow S | 28/28/37 -> 32/32/36 | 22/37/41 -> 35/27/38 | 29/26/40 -> 22/40/37 |
| 1v1 vs Mantis | 26/29/40 -> 40/22/37 | 17/41/42 -> 38/20/42 | 29/29/34 -> 30/30/36 |
| 1v1 vs Wasp | 41/27/32 -> 64/0/36 | 20/44/36 -> 53/5/42 | 21/47/31 -> 74/1/24 |

**1v2 / 2v3 / mixed, starts 1.5-2.4 km**: win / loss / timeout %, before -> after (100 seeds per cell; mirror cells pool both sides, 200)

| fight | Longbow S | Longbow L | Mantis |
| :--- | :--- | :--- | :--- |
| 1v2 vs Aegis | 1/99/0 -> 8/36/56 | 0/100/0 -> 0/94/6 | 0/100/0 -> 0/100/0 |
| 1v2 vs Hornet | 0/100/0 -> 2/83/15 | 0/100/0 -> 1/99/0 | 0/100/0 -> 0/100/0 |
| 1v2 vs Lancer | 0/99/1 -> 10/31/58 | 1/99/0 -> 1/93/6 | 1/99/0 -> 0/96/4 |
| 1v2 vs Midge | 0/100/0 -> 4/26/70 | 0/89/11 -> 1/17/82 | 0/100/0 -> 0/100/0 |
| 1v2 vs Wasp | 5/83/12 -> 22/16/62 | 1/90/9 -> 5/70/25 | 3/93/3 -> 44/40/15 |
| 2v3 vs Hornet | 0/93/7 -> 1/77/22 | 0/92/8 -> 0/91/9 | 0/100/0 -> 0/92/8 |
| 2v3 vs Midge | 0/95/5 -> 3/6/91 | 0/38/62 -> 0/1/99 | 0/98/2 -> 2/91/7 |
| 2v3 vs Wasp | 3/59/38 -> 21/3/76 | 0/9/91 -> 1/0/99 | 0/67/33 -> 37/20/42 |
| mixed vs Aegis | 96/3/1 -> 100/0/0 |  | 91/5/4 -> 89/8/3 |
| mixed vs Hornet |  |  | 78/20/1 -> 85/12/2 |
| mixed vs Hornet/Longbow S |  |  | 1/98/1 -> 0/96/4 |
| mixed vs Lancer | 91/6/3 -> 100/0/0 |  |  |
| mixed vs Lancer/Mantis | 3/93/4 -> 7/79/14 |  |  |
| mixed vs Longbow S |  |  | 93/3/4 -> 79/7/14 |
| mixed vs Mantis | 98/1/1 -> 96/0/4 |  |  |
| mixed vs Wasp | 99/1/0 -> 100/0/0 |  | 92/3/5 -> 99/0/1 |

**1v1, locked start 800-950 m facing, AI off (both sides script only)**: win / loss / timeout %, before -> after (100 seeds per cell; mirror cells pool both sides, 200)

| fight | Longbow S | Longbow L | Mantis |
| :--- | :--- | :--- | :--- |
| 1v1 vs Aegis | 56/38/5 -> 87/6/7 | 1/75/24 -> 2/74/24 | 50/41/7 -> 65/25/9 |
| 1v1 vs Basilisk | 25/3/72 -> 25/1/74 | 28/13/59 -> 26/16/58 | 30/56/14 -> 40/51/9 |
| 1v1 vs Harrier | 54/43/3 -> 89/5/6 | 4/71/25 -> 2/73/25 | 67/25/8 -> 76/17/7 |
| 1v1 vs Hornet | 77/17/6 -> 82/5/13 | 2/75/23 -> 4/74/22 | 64/13/23 -> 76/12/12 |
| 1v1 vs Lancer | 83/17/0 -> 93/2/5 | 0/63/37 -> 0/63/37 | 54/21/25 -> 86/5/9 |
| 1v1 vs Longbow L | 50/3/47 -> 54/4/42 | 29/29/20 -> 28/28/19 | 97/1/2 -> 94/1/5 |
| 1v1 vs Longbow S | 40/40/6 -> 44/44/6 | 3/50/47 -> 4/54/42 | 51/38/4 -> 34/62/3 |
| 1v1 vs Mantis | 38/51/4 -> 62/34/3 | 1/97/2 -> 1/94/5 | 43/43/10 -> 42/42/8 |
| 1v1 vs Midge | 24/75/1 -> 50/22/28 | 2/97/1 -> 3/96/1 | 20/80/0 -> 28/64/7 |
| 1v1 vs Wasp | 94/4/2 -> 88/1/11 | 3/94/3 -> 5/94/1 | 84/12/4 -> 91/6/3 |

(Longbow L cannot take the `lock` command in the locked-start harness: its cameras sit 2.5 m off the axis and miss a 5 m ship at 900 m, so its cells
there say nothing about its fire control. The cells where Mantis lost ground, Mantis vs Longbow L 71 -> 36 and vs Longbow S 35 -> 27, are the other
side getting better; Mantis stays at 650 m because the blueprint's CustomData sets `Engage Cap=650` and a saved key beats the new code default.)

### R9. Remaining weaknesses
- One slug per 22 s (large railgun: per 60 s) is far less damage per second than a gatling ship; against two or three ships the Longbows
  now survive but rarely finish (2v3 timeouts up to 69 %, Longbow S 1v2 52 %), and the large Basilisk and the Pirate need 8+ hits (Basilisk 44-69 % timeouts for
  the railgun ships, Pirate 19-48 %).
- Default-mode timeouts (35-44 %) are the sim's 2.5 km AI search against 1.5-3 km starts and its 10 m/s approach; not script behaviour.
- Scripts-only search finds nothing (above). Longbow L is weak against fast gatling ships at any range (Hornet 5 -> 33 %): 2 s charge delay,
  one shot a minute.
- Friendly fire from a ship that is not in the hive (a Hornet in a Hornet + Longbow team, different IGC tag) is only covered by one camera ray.
- Mantis cannot outrange a Longbow while the blueprint caps it at 650 m (`tools/designs/extra.py`, not a script change).
- The wreck rule and the 6 s / 0.4 m/s^2 thresholds were tuned on the sim's wrecks (coasting hulls); a game wreck that keeps spinning is caught by
  the old `Spin` rule, one that thrusts blindly is not.

### R10. Unverified assumptions about the real game
- Direction of a railgun slug = barrel direction when the charge ends (R2). `IsShooting` of a railgun is true during the charge (R3).
- Slug damage model (`MissileHealthPool` pierces block by block) is [I] in the sim; the "centre shot beats hull-point shot on small ships" result
  depends on it. In game the hull-point rule of v1.6 may still be right for small ships; `Aim Point` = ENGINES/SPREAD restores it per ship.
- The AI blocks pull the trigger on their own and ignore friendlies, and `Enabled = false` on the guns stops them (R5). If the real AI block
  honours a friendly check the guard is merely redundant; if toggling `Enabled` re-initialises the railgun charge in game, switch it off with
  `Friendly Fire Check = false` (the camera/IGC gates in `Control` stay).
- Camera charge cap: the sim has none; a cap below 2,600 m disables the long confirm ray (the 1,300 m rays still work).
- Saved CustomData wins over code defaults: drones with an old `[Longbow]` / `[Mantis]` section keep `AI Confirm Raycast Range=2600` (the new
  default is 1300) and any `Projectile Speed`/`Custom Projectile Speed`; delete those lines after pasting.

### R11. Reproduce
`bash sim/build.sh`, rebuild the three blueprints (`python3 -I tools/build_all.py --only "Longbow S" "Longbow L" "Mantis"`), then
`mono $SIM_OUT/SimHost.exe run --filter '^1v1/(M-vs-O|O-vs-M)$' --seeds 100 --start-distance 1500:2400 --jobs 2` with `M=(Longbow S|Longbow L|Mantis)` and
`O=(Hornet|Aegis|Wasp|Mantis|Longbow S|Longbow L|Midge|Harrier|Lancer|Basilisk)` pasted into the pattern (the 1v1 list has one order per pair).
Locked duels: `--face --start-distance 800:950 --commands "0.5:ai off;0.5:lock;0.5:hunt"`, forced stand-off: append `;0.6:range 900`.
Custom-data switches in `--custom-data` must not contain `=` in the key (the parser splits at the first one: `Forced Range (0 = auto)` cannot be set that way).


## Wasp, Lancer, Harrier (`scripts/WaspAI.cs` v2.1-wasp, `scripts/LancerAI.cs` v2.1-lancer, `scripts/HarrierAI.cs` v2.1-harrier)

Written by the wasp-lancer-harrier agent. Private sim copy of the repo (`repo-base` = scripts as they were, `repo-work` = scripts after each
edit, same opponents in both), same seeds before and after, Hornet from the Hornet lead's version in both. "Before" = the v2.0 scripts.
Hybrid = default AI-block search with `--start-distance 1500:2400` (removes the 2.5 km search-range artefact); locked = `--face --start-distance
800:950 --commands "0.5:ai off;0.5:lock;0.5:hunt"` (isolates tracking + gunnery). Scripts-only (`ai off; hunt`) is unchanged: 0 locks, 0 shots, before and after
(raycasts cannot find a 3-6 m ship, see Hornet notes).

### W1. Ported from the Hornet (patch applied to all three; the pattern text is in the Hornet section)
H2 confirm rays (boresight + spiral, one 2,600 m camera, rest 1,300 m), H3 lock upkeep (`MaintainLock`, `ReacquirePoint`, `Uncertainty`, `NoteLost`,
ghost target 8 s, loss sharing flag 4, `AutoScanStep` search around the last position), H4 `SetFire` re-send + `_firing` reset on every AI flip,
H5 `Separation`, H6 `MatesInLine` (Lancer, Harrier). H1 needed nothing (Lancer/Harrier already had 800 m/s, the Wasp has its own rocket curve).
Wasp: no `MatesInLine`; `MatesInTheWay` was rewritten instead (W6) and `FireGate` refuses tracks older than `FireStaleMax`.
Also: `Shoot_Off` is sent to the launchers whenever the AI blocks are handed back (an AI that leaves a launcher in continuous fire would
otherwise empty it).

### W2. Hull-gap fix: the biggest single effect (new, applies to every script with `MaintainLock`; the Hornet should take it too)
Found with a ray log (script prediction vs the true position from the trace): during a locked duel the prediction was within 0.4-1.6 m of the target
and still about 1 ray in 3 missed, because a small ship is full of gaps (the rays go to the bounding-box centre or a hull point). After
three misses in a row the Hornet-style logic switched to the widening search ellipse, whose rays go to points 13-100 m from the real position, and the
lock never came back (aim error grew 0.7 -> 27 deg while the guns followed a ghost). Fix, in `MaintainLock`:
```csharp
// stale (> 0.6 s): most rays go back to the predicted point (hull points / centre), a few to the widening search ellipse
bool search = false;
if (age > 0.6) { _staleRay++; search = (_staleRay % 10) < (age > 1.5 ? SearchTenthsLate : SearchTenths); }   // defaults 1 and 3
if (search) aim = ReacquirePoint(_tgt, p, dir, age);
else aim = ProbePoint(_tgt, p, dir, dist, out probingAim);
```
(`_staleRay` counts rays, not loop passes: the second ray of a tick almost never has camera charge, so a `k & 1` test in the loop never selected the search.)
Share of stale rays that go to the search ellipse, Harrier locked, 60 seeds x 8 opponents: 0/10 58.1 %, 2+2 56.7, 1+3 57.2, 3+3 55.6, 5+5 53.1; Lancer 0/10 50.7, 1+3 53.6, 3+3 49.3; Wasp 36.4, 35.1, 34.2: more
search is never better inside the +-3 point noise, so the share stays small (1/10, 3/10 after 1.5 s) as insurance for a target that really changes course.
`ReacquirePoint` is now an ellipse, not a disc: sigma = `Uncertainty(t, age)` (0.35 R + 0.5 max(AccMag, 6) age^2, capped 80 m), long axis
1.5 sigma along the target's old sideways velocity (the error of a manoeuvring ship lies there), short axis 0.6 sigma (round if the target was head-on), golden-ratio/plastic-number
low-discrepancy samples, ray ends `min(R + 0.5 sigma, 25)` m behind the point. The ray budget (2 forward cameras x 2 km/s, a ray costs its length: 5 rays/s at 800 m) is the limit: in the
locked Harrier-vs-Midge set 24 of 40 duels are won, in every one the lock held to the end (about 250 shots); 9 are lost with a lock that vanished before the first shot (the
Midge reverses its 5-9 m/s strafe, the prediction drifts 1-3 m in a second, three hull-gap misses and it is gone) and 7 never got the starting `lock` (harness artefact).
Effect of the first version of this fix, with the other changes of this section still at their old values (locked, AI off, 27 scenarios, 30 seeds): win 25.0 -> 42.7 %, timeouts 24.9 -> 7.0 %.

### W3. Track extrapolation and wrecks (new)
- `Track.P/V` trust the measured acceleration for `AccHold` = 1 s only. A ghost track is now extrapolated with constant velocity after that
  (it was `0.5 a t^2` for up to 8 s: 6 m/s^2 x 5 s = 75 m and a phantom 30 m/s, the guns followed it away).
- `Track.Raw`/`RawMax` keep the unfloored bounding-box radius (`Radius` is floored at 1.5 m). `Disabled()` now also treats a ship that has shrunk below 40 % of its
  largest raw radius, or that is below 0.75 m (one block: debris splits off as its own grid and stays "Enemies" while it has a powered block), as finished
  (Harrier keeps missiles out of the rule). Without it the Wasp fired 300-440 rockets at a one-block remnant of a Gnat while the live Gnats sat 60 m away
  (Wasp vs 3xGnat: 80 % wins, 20 % timeouts, hit rate 7 %; after: 100 % wins, hit rate 21 %).
- A rocket only detonates on a direct hit, so the Wasp's fire window also shrinks with `Raw` (min 0.5 m): no more rockets thrown at a 0.4 m block.

### W4. Evasion and gunnery constants (sweeps, 60 seeds x 8 opponents, locked, one parameter set at a time)
| script | change | locked-duel win % (before > after) | damage taken |
|---|---|---|---|
| all | new `Jink Interval (s)` / `Jink Interval Random (s)` 1.5+2.0 -> 1.0+1.0 | (part of the rows below) | |
| Wasp | `Jink Speed` 12 -> 40, `Orbit Speed` 22 -> 40 | 28.1 > 38.1 | 5976 > 5117 |
| Harrier | `Jink Speed` 16 -> 35, `Orbit Speed` 30 -> 40 | 56.7 > 60.8 | 3354 > 3136 |
| Lancer | `Jink Speed` 12 -> 35, `Orbit Speed` 22 -> 40 | 42.6 > 49.0 | 4143 > 3095 |
| Lancer | `Burst Seconds` 1.2 -> 2.0, `Burst Pause` 0.8 -> 0.4 | 49.0 > 52.0 | dealt 3.9k > 4.5k |
Standoff (Harrier 200/250/320, Lancer 300/420/550, Wasp 170-450), fire-window multiplier (0.7-2.0), `Aim Point` mode, jink gate and orbit speed
beyond these values were flat or worse inside the +-3 point noise of a 480-run set, so they are unchanged.

### W5. Rocket doctrine (Wasp): fewer, closer, surer shots
Measured with the lock set (60 seeds x 8 opponents): at `Max Fire Range` 650 / `Max Flight Time` 3.2 s the Wasp spent 8.5 rockets per drone for 2.6 hits (31 %);
the long shots miss (4 s of flight) and the 2 s salvo pause then kept it from shooting when the target was close. Now `Max Fire Range` 420,
`Max Flight Time` 2.0, `Standoff` 250, `Salvo Pause` 0.8, `Seconds Between Launchers` 0.3: 6.8 rockets, 4.2 hits (62 %), damage per rocket 583 -> 821,
wins unchanged at 37.8 %. In hybrid runs the ratio is 11 % (AI-fired long shots dominate there: the sim AI fires at 650+ m) and the win rate stayed within noise
(32 % vs 35 %, 60 seeds). The rocket curve (100 m/s start, +600 m/s^2, 200 m/s cap, no velocity inheritance) matches the sim; nothing to change in `RktDist/RktTime`.
`SafeRange` 25 m is enough: no self-damage in any run. Real rockets are limited, so the hit-per-rocket gain is worth more in game than in the sim.

### W6. Rocket friendly fire (Wasp `MatesInTheWay`)
A mate is moved to where it will be when the rocket passes it (`RktTime(distance along the line)`, two iterations) and the blast point is checked against
the mate at arrival time, with +3 m margin. The old check used the mate's position "now" (1-3 s early for a mate flying 30-40 m/s).
Friendly damage per ship in the matrices: Harrier and Lancer 0 (was 1-6 per ship). Wasp 29-31 (was 0-15), but every one of the 4 runs with friendly damage had `script_target_s` = 0: the
rockets were fired by the sim's AI stand-in, which has no friendly check (Harrier-vs-3xWasp, 2v3 with 3 Wasps), not by the script. The script gate itself never fired on a mate in these runs.

### W7. Acquisition with rocket guns: why the Wasp finds targets 3 times slower than the Harrier
The sim AI (and probably the real one) turns the launchers onto the rocket LEAD point: target velocity x flight time, i.e. v/150 rad, so the enemy
sits 0-3 deg yaw / 0-1.5 deg pitch off the nose (measured by logging the ray bearing against the trace: median 1.5 / 0.8 deg), against 0.4 deg for 800 m/s guns. The
Wasp's confirm rays therefore use a Gaussian cone (`AI Confirm Cone Yaw/Pitch`, 4.5 / 1.5 deg, 1 ray in 4 down the boresight) and 1,000 m short rays.
Result: first script target for the Wasp is still about 110 s and only 23-36 % of the 1v1 runs get one (44-66 % in the multi-ship sets; Harrier/Lancer: 96-98 %, median 30 s). Casting wider (14 deg) or
narrower (3 deg) cones, or wide short rays while under fire, changed nothing (26-30 %): a 5-6 m ship at 1-2 km covers 0.1-0.3 deg and there are 2 forward cameras and
2 km/s of charge each. It is a ray-budget limit, not a pattern problem. A Wasp needs a target feed (below) or turrets/designators.

### W8. Target feeds from other fleets (new in all three)
`Listen To IGC Tags (target feeds)` (default `HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER`, own tag skipped) registers one listener per tag;
`ReadTgt(listener)` is shared by all. Feeds naming my own grid or a wingmate are ignored. In the sim every team is "Enemies" and IGC between teams is blocked, so
the only place it can matter is inside mixed teams, and there the effect is small (Lancer + Mantis vs the 13 single-ship opponents: 93.1 > 95.4 %, mostly the Lancer fixes). **Unverified in game**: whether enemy broadcasts reach a PB.
If they do, an enemy fleet's feed names my ships; `MaintainLock` drops those (friendly relation) but a malicious feed could still waste cycles.

### W9. Measured effect (private sim, same seeds; "win" = that model's side wins; pooled over all runs in which the model is on the field)
| set | model | n | win % | lose % | timeout % | hit % | damage dealt /ship | damage taken /ship | median kill s | ever held a script target % |
|---|---|---|---|---|---|---|---|---|---|---|
| 1v1 hybrid 1.5-2.4 km, 30 seeds x 24 scenarios | Harrier | 300 | 51.3 > 78.7 | 37.7 > 18.7 | 1.0 > 0.7 | 37.8 > 41.8 | 9.0k > 11.2k | 4.8k > 2.4k | 139 > 92 | 18 > 95 |
|  | Lancer | 300 | 54.7 > 78.7 | 40.3 > 21.0 | 1.0 > 0.0 | 34.3 > 40.1 | 8.6k > 11.3k | 4.6k > 2.2k | 152 > 99 | 17 > 98 |
|  | Wasp (*) | 300 | 29.7 > 28.7 | 66.0 > 69.7 | 1.7 > 0.7 | 5.0 > 12.4 | 3.7k > 3.8k | 6.4k > 6.7k | 145 > 141 | 23 > 36 |
| 1v1 locked 800-950 m, AI off, same 24 scenarios | Harrier | 300 | 34.7 > 55.3 | 39.7 > 34.0 | 25.0 > 10.0 | 43.3 > 41.7 | 7.2k > 9.1k | 4.0k > 3.5k | 16 > 18 | 94 > 94 |
|  | Lancer | 300 | 23.0 > 59.0 | 39.7 > 25.7 | 37.3 > 15.3 | 47.6 > 36.5 | 7.9k > 10.7k | 3.6k > 2.6k | 26 > 20 | 97 > 97 |
|  | Wasp (*) | 300 | 18.7 > 31.7 | 62.7 > 64.0 | 16.7 > 4.0 | 35.2 > 66.6 | 2.7k > 4.7k | 6.1k > 6.0k | 21 > 32 | 99 > 99 |
| 1v1 default 1.5-3 km, same 24 scenarios | Harrier | 300 | 36.7 > 50.0 | 20.3 > 13.0 | 37.0 > 36.7 | 38.2 > 42.0 | 6.0k > 7.3k | 3.0k > 1.6k | 141 > 97 | 11 > 62 |
|  | Lancer | 300 | 36.3 > 53.3 | 26.0 > 12.7 | 35.3 > 34.0 | 31.9 > 37.9 | 5.3k > 6.9k | 3.0k > 1.4k | 162 > 104 | 8 > 64 |
|  | Wasp (*) | 300 | 21.0 > 20.7 | 42.7 > 45.0 | 32.7 > 33.7 | 4.4 > 8.0 | 2.7k > 2.5k | 4.0k > 4.3k | 149 > 146 | 15 > 23 |
| 1v2 hybrid, 20 seeds x 36 scenarios | Harrier | 280 | 38.6 > 48.6 | 51.8 > 46.8 | 9.3 > 4.6 | 42.7 > 47.2 | 19.7k > 24.7k | 5.2k > 4.5k | 188 > 101 | 25 > 96 |
|  | Lancer | 280 | 32.9 > 47.9 | 54.3 > 45.4 | 12.1 > 6.8 | 36.3 > 42.7 | 15.5k > 28.3k | 5.4k > 4.6k | 182 > 124 | 22 > 94 |
|  | Wasp (*) | 280 | 34.6 > 28.6 | 60.7 > 66.8 | 3.9 > 4.6 | 23.2 > 24.7 | 7.0k > 6.5k | 5.6k > 6.7k | 181 > 89 | 24 > 44 |
| 1v3 / 2v3 / pirate / mixed hybrid, 20 seeds x 81 team scenarios | Harrier | 300 | 28.0 > 53.7 | 57.0 > 45.3 | 14.7 > 1.0 | 23.5 > 26.5 | 10.3k > 14.4k | 6.7k > 5.3k | 229 > 135 | 29 > 96 |
|  | Lancer | 660 | 54.8 > 69.5 | 30.6 > 27.1 | 14.1 > 3.2 | 24.6 > 28.5 | 8.8k > 14.1k | 4.0k > 4.0k | 160 > 124 | 21 > 96 |
|  | Wasp (*) | 700 | 49.1 > 47.7 | 35.1 > 46.1 | 15.3 > 6.0 | 16.4 > 13.5 | 8.1k > 7.0k | 6.1k > 6.8k | 182 > 113 | 20 > 53 |
(*) The Wasp's "after" opponents include the improved Harrier and Lancer (they are in the same fights), so its overall win rate hides the gain. Against opponents that are NOT
one of the three (Hornet, Aegis, Mantis, Midge, Longbow S, Basilisk, Gnat, Stinger, Watcher, Pirate): hybrid 1v1 24.4 > 29.4 % (hit 3.7 > 13.4 %, ever locked 31 > 48 %), hybrid 1v2
32.5 > 32.5 % (median kill 186 > 86 s, locked 26 > 60 %), team scenarios 53.3 > 57.2 % (kill 185 > 106 s, locked 20 > 66 %), locked 1v1 10.0 > 31.1 % (hit 38 > 90 %, timeouts 11 > 6 %).
"lose" and "timeout" columns do not add to 100 - win because of mutual kills and draws. The "ever held a script target" column in the locked rows includes the pilot's `lock`.
Default-mode timeouts (33-37 % for every model, before and after) are the sim's 2.5 km AI search against 1.5-3 km starts; in that set the median kill time shortens the same way as in the hybrid set.

1v1 hybrid win % per opponent (before > after), 30 seeds each:
- Harrier vs Aegis 27>93, Basilisk 70>100, Harrier(mirror) 38>48, Hornet 13>77, Lancer 30>57, Longbow S 73>90, Mantis 83>90, Midge 63>87, Wasp 77>97.
- Lancer vs Aegis 30>90, Basilisk 57>93, Harrier 60>43, Hornet 3>77, Lancer(mirror) 48>50, Longbow S 90>93, Mantis 80>100, Midge 67>97, Wasp 63>93.
- Wasp vs Aegis 23>23, Basilisk 0>0, Harrier 23>3, Hornet 0>10, Lancer 30>7, Longbow S 47>50, Mantis 60>73, Midge 17>20, Wasp(mirror) 48>50.

1v1 locked (AI off) win % per opponent:
- Lancer vs Aegis 10>83, Basilisk 77>80, Harrier 10>40, Hornet 10>60, Lancer(mirror) 18>35, Longbow S 13>67, Mantis 17>50, Midge 33>60, Wasp 23>80.
- Wasp vs Aegis 17>80, Basilisk 0>0, Harrier 20>10, Hornet 10>37, Lancer 30>20, Longbow S 0>0, Mantis 10>30, Midge 23>40, Wasp(mirror) 38>50.
Locked, the Wasp still loses to Harrier (20>10) and Lancer (30>20), and fires 0 rockets against Longbow S and the Basilisk (it is dead first).

### W10. Remaining weaknesses
- **Wasp**: finds a target in only a third of the 1v1 fights (W7) and then needs about 10 rocket hits to kill a 54-block drone while a gatling ship needs 15-20 s; against
  Harrier/Lancer/Hornet (all of which now hold a lock after 30 s and fly 40-60 m/s jinks) it is killed first (hybrid win 3-10 %). Mirror 50 %. It beats slow or AI-flown targets
  (Aegis 80 % locked, Mantis 30-73 %, Gnat/Stinger/Watcher 90-100 %). Longbow S and the Basilisk kill it before it can fire. Fixing this needs a target feed, a designator turret,
  a blueprint change (a nose-aligned gatling for the AI to lead, or AI Defensive blocks) or a different role (spotter-fed missile boat).
- Harrier vs Lancer is 57 / 43 hybrid and 37 / 40 locked, the Lancer mirror 50 % (timeouts 15-35 % of the locked mirrors: two jinking ships at 420 m rarely land the 6-8 hits needed).
- Locked (AI off) duels still time out in 4-15 %: the lock is lost for good when three hull-gap misses coincide with a course change (W2); with AI blocks (hybrid) it comes back, scripts-only it does not.
- Default-mode timeouts (33-37 %) are the sim's 2.5 km AI search against starts up to 3 km (not the scripts).
- The jink/orbit/standoff/fire-window/salvo constants sit on a plateau inside the +-3 point noise of a 480-run set; the remaining levers are acquisition (Wasp) and ray budget (all).
- Hornet: at the time of writing `HornetDroneAI.cs` still sends every stale ray to the spiral disc (no centre probes, W2) and `Track.P/V` extrapolate acceleration without the `AccHold` limit (W3); the Hornet owner should take W2/W3 (measured here: locked duels +15-20 points).

### W11. Unverified assumptions about the real game
- The AI Offensive block points rocket launchers at a lead point and shoots rockets at its own range; confirm-ray cones for the Wasp are tuned on that stand-in (median 1.5 deg yaw).
- Camera charge cap: the sim has none; a cap below 2,600 m disables the long confirm ray (the short rays still work).
- Rockets do not inherit launcher velocity (kept from v2.0; the sim agrees), 100 m/s start, +600 m/s^2, 200 m/s cap, explosion on contact only.
- Debris grids report "Enemies" while they have a powered block (sim); in game a block-only grid may be "NoOwnership", in which case the debris rule is merely redundant.
- Instruction count: the sim's `FakeInstructionCount` is 0. New work per tick: up to 2 rays, a loop over mates (separation, line check, with `RktTime` x2 per mate in the Wasp),
  a handful of vector operations; sim CPU per call rose 0.0076 -> 0.0084-0.0098 ms.
- Saved CustomData wins over code defaults: ships with an old `[Wasp]` / `[Lancer]` / `[Harrier]` section keep `Jink Speed`, `Orbit Speed`, `Max Fire Range`, `AI Confirm Raycast Range=2600`
  etc. Delete the section after pasting v2.1.

### W12. Reproduce
Private repo copy (`tar` of the repo without `.git` and `out`, plus `out/Blueprints`), `bash sim/build.sh`, then `python3 -I tools/build_all.py --only "Wasp" "Lancer" "Harrier"` after each edit.
Sets: 1v1 `^(1v1/((Wasp|Lancer|Harrier)-vs-(Hornet|Aegis|Longbow S|Midge|Mantis|Basilisk)|(Hornet|Aegis|Longbow S|Midge|Mantis|Basilisk)-vs-(Wasp|Lancer|Harrier))|1v1/(Wasp-vs-Wasp|Lancer-vs-Lancer|Harrier-vs-Harrier|Harrier-vs-Lancer|Harrier-vs-Wasp|Lancer-vs-Wasp))$`,
1v2 `^1v2/(Wasp|Lancer|Harrier)-vs-2x`, teams `^(pirate/M|mixed/.*M|1v3/M|2v3/2xM)` with `M=(Wasp|Lancer|Harrier)`; modes: hybrid `--start-distance 1500:2400`,
locked `--face --start-distance 800:950 --commands "0.5:ai off;0.5:lock;0.5:hunt"`, scripts-only `--commands "0.5:ai off;0.5:hunt"`, parameter sweeps `--custom-data "[Wasp]\nJink Speed=40\n..."`.
Debugging a lock: add `Echo` of a ray log to `AiStatus()` and run `one --echo --cap-seconds N --trace t.csv`, compare the logged predicted positions with `s<i>_x/y/z` in the trace.

## Aegis, Basilisk, Stinger (`scripts/AegisAI.cs` v2.1-aegis, `scripts/BasiliskAI.cs` v2.1-basilisk, `scripts/StingerAI.cs` v1.1; `HiveMotherAI.cs` unchanged)

### A1. Ported from the Hornet (Aegis and Basilisk)
H1 not needed (Aegis gatling is 800 already; Basilisk uses the rocket curve). H2 (confirm rays, long + short camera), H3 (cheap lock rays,
ghost target 8 s, stale hand-over to the AI blocks, search memory, hive loss flag), H4 (`SetFire` re-send), H5 (`Separation`; Aegis 70 m /
25 m/s, Basilisk 160 m / 12 m/s) and the stale-track fire gate (`FireStaleMax`, in `Control` for Aegis, in `FireGate` for Basilisk) are
pasted as written in the Hornet section (a script, kept outside the repo, applied the same text to both files; every hunk asserted unique).
Aegis also has `MatesInLine` (H6). Basilisk keeps its rocket `MatesInTheWay` but extrapolates the mate to the time the rocket passes (`RktTime`)
and the blast point (`_leadT`), with +10 m on the clearances because a large mate is not a point.

### A2. Tuning that the sim supported
- **Basilisk `Standoff` 600 -> 300 m** against small grids (rocket flight 3 s -> 1.5 s): 1v1 vs Harrier/Hornet/Lancer/Mantis/Midge/Wasp/Basilisk wins 50 -> 66 %,
  hit 17 -> 28 %. Sweep (20 seeds, same 7 opponents): 600 m 50 %, 400 m 62 %, 350 m 61 %, 300 m 66 %, 250 m (MinRange 100) 64 %.
- **`StandoffLarge` 600 m (Aegis and Basilisk) when the target radius > 25 m** (`LargeTargetRadius`): at 300/220 m the Pirate's turrets win.
  Aegis vs Pirate with standoff 220/400/600: 0/15/25 % wins; Basilisk vs Pirate back to 15 % (300 m alone gave 5 %).
- Aegis `Standoff` 220 sits on a plateau (150: 65 %, 220: 68 %, 300: 56 %; orbit 30/jink 10: 66 %): unchanged.
- `AI Confirm Cone (deg)` (Basilisk, default 2.5): a 4.5 deg cone did not help vs Longbow S at 1.3 km (script target 0 -> 5 %); kept as a setting only.

### A3. Measured effect (private sim, 20 seeds, same seeds, starts 1.5-2.4 km, opponents' blueprints frozen; win / loss / timeout %, hit %, damage dealt / taken per run)
| side | before: win/loss/timeout % | hit % | dmg dealt / taken | after: win/loss/timeout % | hit % | dmg dealt / taken |
|---|---|---|---|---|---|---|
| Aegis 1v1 | 45 / 43 / 3 | 38.0 | 10152 / 5310 | 66 / 28 / 2 | 40.3 | 12069 / 3443 |
| Aegis 1v2 | 4 / 96 / 0 | 23.8 | 3382 / 9941 | 10 / 89 / 1 | 24.0 | 6895 / 9377 |
| Aegis 2v3 | 13 / 85 / 2 | 22.5 | 15518 / 25624 | 43 / 57 / 0 | 21.5 | 28817 / 19251 |
| Aegis mixed | 25 / 55 / 20 | 21.1 | 10608 / 4345 | 44 / 44 / 12 | 14.4 | 14161 / 2907 |
| Aegis pirate | 15 / 85 / 0 | 93.5 | 74228 / 10670 | 20 / 80 / 0 | 87.9 | 130707 / 8094 |
| Aegis ALL | 26 / 65 / 5 | 33.1 | 12341 / 9201 | 44 / 51 / 3 | 29.7 | 18950 / 7062 |
| Basilisk 1v1 | 44 / 44 / 11 | 28.0 | 7754 / 39122 | 54 / 36 / 9 | 38.0 | 8771 / 34235 |
| Basilisk 1v2 | 17 / 77 / 6 | 20.8 | 6687 / 57412 | 18 / 78 / 3 | 29.0 | 7594 / 52971 |
| Basilisk 2v3 | 42 / 25 / 33 | 17.8 | 25082 / 160646 | 75 / 17 / 8 | 29.4 | 32138 / 103698 |
| Basilisk mixed | 19 / 54 / 28 | 13.7 | 9930 / 36206 | 35 / 54 / 11 | 24.6 | 13903 / 39706 |
| Basilisk pirate | 15 / 70 / 15 | 77.7 | 142450 / 27867 | 15 / 65 / 20 | 79.1 | 156509 / 30225 |
| Basilisk ALL | 32 / 52 / 16 | 26.5 | 16393 / 58809 | 43 / 47 / 8 | 37.4 | 19338 / 48778 |

Per opponent (Aegis, 1v1, win %): Harrier 55 -> 90, Hornet 15 -> 65, Lancer 65 -> 90, Wasp 60 -> 95, Midge 70 -> 85, Mantis 10 -> 35, Longbow S 0 -> 15, Basilisk 85 -> 90.
Basilisk: Mantis 60 -> 85, Midge 80 -> 95, Lancer 40 -> 60, Harrier 35 -> 50, 2xBasilisk-vs-3xMidge 5 -> 65, vs 3xHornet 20 -> 60.
Friendly fire: Basilisk 2v3 40 -> 25 damage per run, collisions 0.9 -> 0.2; Aegis 5 -> 0. The "script target" share of Aegis runs rose from 10-20 % to 95-100 %.

### A4. Stinger: why 96 % idle, and what was checked
- **Structural, not a bug**: the Stinger blueprint is a Gnat frame (sensor 50 m, cameras, no AI block, no turret). It waits in `Mode.WAIT` until a contact
  arrives; contacts come from its sensor or from IGC `<TAG>_TGT` broadcasts. No scenario of the matrix has a friendly feeder on the Stinger side
  (and enemy IGC is blocked), so a Stinger never launches (`avg_speed` 0) and just gets shot (Stinger-vs-X: 0 % wins, 340 runs, unchanged by my edits).
- **Listen tags** (`ListenTags`) were GNAT, HORNET, LONGBOW: an Aegis/Wasp/Lancer/Harrier/Mantis/Basilisk/Watcher team never fed it. Now all of them plus
  `DRONE_CUE` (Aegis threat cues); a tag that contains "_" is used verbatim.
- **Verification with a feed** (private copy of the sim with extra scenarios `Hornet+2xStinger-vs-X`, `Aegis+2xStinger-vs-X`; nothing in `sim/` changed; start 800-1000 m,
  facing, `lock` at 0.5 s so the Hornet shares): launch on first contact works, the Stinger accelerates at 6 m/s^2 to 75-100 m/s (about 25 s from 800 m), arms inside
  200 m and 120 m from the launch point, and warheads detonate (1.4-2.2 per run, 2 Stingers). Damage dealt by Stingers 1.4-6.4k per run (Hornet team); 10-80 % of runs have a Stinger that hit.
  Before the tag fix an Aegis team's Stingers never moved (0 detonations); after it 0.7-1.8 detonations per run (+0.3-3.9k damage per run).
- **Most Stingers die on the way**: the target's gatling kills a 20-block Stinger in 5-10 s, the approach needs 15-25 s; an armed warhead whose block dies explodes where it is.
- **Terminal detonation**: now a 45 m forward camera ray each tick when armed and < 100 m, detonate when the first hull block is < max(5, closing x dt x 2.5) m away
  (4.5 m radius; centre distance is wrong for a 60 m ship). Centre-distance rule kept as fallback (capped at 3 + 6 m). No measurable change in the fed scenarios
  (they die first), correct by the game rules.
- Not changed: the Stinger cannot find a target itself (50 m sensor). Real fix = blueprint (AI blocks as for the Hornet) or the rack/Hive feed; outside these files.

### A5. Not fixed / observed
- Basilisk and Aegis vs Longbow S: Basilisk 0 %, Aegis 15 %. The railgun sits at 1.3 km (rocket range 800 m) and the Basilisk gets no script target (5 %): the sim AI's nose is 4 deg off, beyond the confirm cone.
- 1v2 against two gatling ships stays near 0-18 % for both (they are outnumbered and slow).
- Aegis point-defence priority (rockets/Stingers first, carrier attackers) was not measurable: no scenario has a carrier and the sim's decoys are inert. Aegis vs Wasp already 95 %.
- HiveMotherAI: no bug found that affects the sim (printer is not in the matrix).
- Assumptions: AI blocks hand back guns as in the sim (H4), camera charge unlimited, `IMyWarhead.Detonate()` only when armed (sim claims game behaviour), Stinger 6 m/s^2 from the blueprint.

### A6. Reproduce
Frozen blueprint snapshot (`out/Blueprints` copied, only Aegis/Basilisk/Stinger Rack replaced for "after"), `SimHost.exe run --repo <snapshot>`, filter
`^1v1/(Aegis|Basilisk)-vs-X$|^1v1/X-vs-(Aegis|Basilisk)$|^1v2/(Aegis|Basilisk)-vs-2x(Hornet|Wasp|Longbow S|Harrier|Midge)$|^2v3/2x(Aegis|Basilisk)-vs-3x(Hornet|Wasp|Midge)$|^pirate/(Aegis|Basilisk)-vs-Pirate$|^mixed/.*-vs-(Aegis|Basilisk)$`
(X = Aegis|Basilisk|Harrier|Hornet|Lancer|Longbow S|Mantis|Midge|Wasp), `--seeds 20 --start-distance 1500:2400`. Remember `build_all.py --only` takes one model per call.

## Round 2: hornet-harrier (`scripts/HornetDroneAI.cs` v2.2-hornet, `scripts/HarrierAI.cs` v2.2-harrier; the Watcher runs the Hornet script)

Written by the hornet-harrier agent. Private copy of the repo and sim (frozen opponents: all other scripts exactly as in the round-2 baseline, settings hash
86da6f4425ff003e; checked on 1v1/Harrier-vs-Hornet, 30 seeds: the unmodified scripts reproduce the baseline file bit for bit), same seeds before and after, `--jobs 2`. "orig" = the scripts at the start of this
round. Modes: **hybrid** = default AI-block search with `--start-distance 1500:2400` (removes the 2.5 km search-range artefact), **default** = the shipped
1.5-3 km starts (compared with `out/sim/results/r2-base-1v1` / `r2-base-team`, same seed prefix), **locked** = `--face --start-distance 800:950 --commands "0.5:ai off;0.5:lock;0.5:hunt"`.
"Win" = the Hornet/Harrier side wins, pooled over every ship-run of the two models in the set.

### R1. The Hornet gets the Harrier's tracking (Hornet only; biggest single gain)
`HornetDroneAI.cs` was still v1.8 plus the round-1 H1-H6 patches and lacked the later fixes that Wasp/Lancer/Harrier received (W2, W3, W8 of the section above):
`Track.Raw/RawMax` + `AccHold` (acceleration trusted 1 s, constant velocity afterwards), the ellipse `ReacquirePoint` and the predicted-point/search split in
`MaintainLock` (`SearchTenths`), the debris rule in `Disabled`, `RegisterFeeds`/`ReadTgt` (other fleets' `_TGT` feeds, own grid and wingmates filtered), `Jink Interval` keys.
Pasted from the Harrier and checked with `diff` (the two files now differ only in doctrine constants, target scoring, missiles and the cue channel). In the Hornet's hybrid
1v1 set: **win 57.6 -> 70.2 %, loss 38.6 -> 28.1 %, median time from first script target to kill 71 -> 39 s** (Hornet vs Harrier 23 -> 40 %, vs Lancer 23 -> 53, vs Longbow S 27 -> 63,
vs Midge 63 -> 100, vs Aegis 13 -> 47). The Hornet's own constants (standoff 350, orbit 22, jink 12) stay: sweeps of standoff 200/250/450, orbit 0/40, jink 0/35 and
Evade Speed 20/45 were all inside the noise (+-2.5 points at n=360) in locked and hybrid mode.

### R2. AI confirm rays (`AiStep`)
Measured with a ray log (debug build): the first script target of a hybrid fight needs about 25 confirm rays at 2 km. The two forward cameras fire at the same instant,
every 1.3 s (2.6 km of charge each), one down the boresight and one on the spiral. The AI nose is 0.2-0.5 deg off, the target is 0.1 deg wide and sparse, so about 3 % of the rays hit:
a pure ray-budget limit (changing the spiral from 2.5 to 0.8 or 1.2 deg changed nothing, keys `AI Confirm Spiral Min/Max/Exponent` stay for experiments).
- `AiConfirmRange` default 1300 -> 2600: the second forward camera used to fire 1.3 km rays that cannot reach a target farther away. **First script target (median) 32 -> 22 s,
  time to kill 82 -> 67 s** (Hornet, 13 scenarios x 30 seeds), win rate unchanged.
- `AI Confirm Rays Shorten After A Fight` (new, default on): for the first 6 s of an AI stint within 25 s of a fight the ray length is `1.5 x last range + 200 m` (min 500): a ray costs its
  length, so close fights re-lock with more rays per second. The 6 s limit matters: without it the rays stayed 1,058 m long while the AI had picked a Gnat 1.5 km away (33 s without a lock in a 12-Gnat fight).
  Pooled hybrid +0.7 / +0.9 points (1v1 / teams), inside the noise, kept because it is free.
- `Search2nd`: two cameras that can cast in the same tick no longer aim at the same point (the second ray goes to the search ellipse). Neutral, kept.

### R3. AI re-pick (`AiStep`, `AiRepick`, `AiRepickWindow`)
Trace of 2xHornet-vs-3xMidge: after Midge #3 was reduced to one block the sim AI kept its nose on the wreck (the stand-in keeps a target until it is dead or its next 30 s search), the
Hornet's confirm rays hit nothing it wanted, and the healthy Hornet idled in "AI on, no lock" for 25 s while two untouched Midges shot it. Now an AI stint that lasts more than
6 s although the drone had a target within the last 40 s is power-cycled for one tick (`Enabled=false`, next tick `true`), which makes the AI search again. It never fires before the
first contact, so the opening search stays with the AI block. 2xHornet-vs-3xMidge 4 -> 10 wins of 20, 2xHarrier-vs-3xMidge 6 -> 8. Assumption: an AI block that is switched off and on picks its target again.
The related `AiHeldIgnored` (an enemy I wrote off as finished that two confirm rays find under the AI's nose is taken off the ignore list again) is inert in the measured sets (identical runs) and
kept as a safeguard.

### R4. HUNT search patrol (`PatrolStep`, `Control`) - the default-mode artefact
59 % of the baseline timeouts are ships that start beyond the AI block's search range (2,500 m in the stand-in) and wait at home. In HUNT mode with `[AI]` blocks, in open space
(natural gravity < 0.5 m/s^2), a drone that has had no target for 15 s (90 s after a fight) now flies the chords of a square of radius 1,200 m round its home point at 60 m/s with the AI blocks off
and stops for a 4 s listening stop (AI on) at every corner. The AI engagement test keeps the AI on while it flies the ship (velocity changed by 3 m/s or turning in the last 4 s, up to 90 s) -
the first version used an instantaneous speed test, the AI's own speed jitters through 3 m/s, and it dragged drones away from fights they were about to lock (hybrid 70.5 -> 66.9 %, fixed to 70.1).
Damage ends the patrol. Config: `Search Patrol When Nothing Found (HUNT)`, `Radius`, `Starts After`, `Starts After A Fight`, `Listening Stop`, `Speed`.
**Default mode (1.5-3 km), 1v1 with Hornet/Harrier, 30 seeds: win 40.2 -> 61.8 %, timeouts 35.6 -> 8.1 %** (Hornet 34.5 -> 62.6, Harrier 46.0 -> 61.0; Harrier losses 17.6 -> 28.6 because more fights are
fought); default-mode team sets (2v3, mixed, pirate, swarm, 20 seeds): win 45.6 -> 65.2 %, timeouts 35.4 -> 12.9 %, timeouts without a shot (nobody ever in range) 413 of 1,530 -> 15 of 1,020 runs. Inert in `ai off` modes, for
the Watcher (no AI blocks) and in GUARD. Assumption about the real game: the AI blocks stop searching beyond some range and start flying the ship once they hold a target.

### R5. Bug in every script: config keys that contain "="
`Cfg(S, "Split Targets Across Hive (false = focus fire)", ...)` and `"Lead Scale (1 = calculated)"` cannot be set from Custom Data: `MyIni` splits `key=value` at the first "=", so
the key is read back as `Split Targets Across Hive (false ` and the typed value is ignored (in the sim `--custom-data` with the key had no effect at all; every `reload` also appends one more
mangled line). Renamed in Hornet/Harrier to `Split Targets Across Hive (false: focus fire)` and `Lead Scale (1: as calculated)`. **Same keys, not changed (not my files):** Aegis L434/L432, Basilisk L439/L437,
Gnat L392/L390, Lancer L434/L432, Wasp L460/L458 (`Split ...`, `Lead Scale ...`), Longbow L414/L436/L446/L451 and Mantis L431/L453/L463/L468 (`Forced Range (0 = auto)`, `Fresh Acceleration Weight (0 = old smoothing)`, plus the two above).
Focus fire could therefore never be tested before: with the key fixed, `Split=false` vs `true` is flat (2v3 and mixed sets, 20 seeds, +-0).

### R6. Tuned and left alone (all flat inside the noise; keys exist where it was cheap)
Aim modes AUTO/CENTER/FACING/SPREAD/ENGINES (locked Hornet, 360 runs each: 56.4/56.9/58.1/56.1/57.2 %); standoff, orbit, jink, evade speed (R1); `Target Stickiness` 400/1200/3000;
`AI Takes Over After Stale Lock` 1.5/3/6 s; `Ghost Target Time` 4/8/14 s; AI re-pick interval 3/6/12 s. Awareness rays (spare camera charge spent on neighbours of the target, side contacts kept 12 s, so the
next target of a swarm is known when the first dies) were built and removed: no gain in swarms, -6 points in the 1v2/1v3 set (Wasp fights). Shot statistics from a private diagnostic build of the sim: the gun
axis is within 0.17 deg (median) of the constant-velocity intercept at every range 300-800 m, so the 36 % hit rate is the target's manoeuvring plus the 0.15 deg spread, not aiming; a duel is decided by
accumulated damage: 22 of 64 disabled Hornets lost all weapons, 22 all power (4 small batteries), 15 the RC (101 hp, non-functional below 85), 5 the PB.

### R7. Watcher (Hornet script in scout mode) with armed squads
Private harness `Wt` (not in the repo; `ScenarioGen.Build` teams, 20 seeds; "blind" = armed receivers have `Auto Camera Scan`, `All-around Close Scan` and `AI Block Search Assist` off, 800-1,100 m, the Watcher gets `lock` at 0.5 s)
so the squad can only get its target from the Watcher over HORNET_TGT / FLEET: Watcher+2xHornet vs Aegis 17 wins, Watcher+Harrier+Hornet vs Lancer 15, Watcher+2xHornet vs Hornet 13, Watcher+Mantis+Lancer vs Hornet 14,
Watcher+2xWasp vs Lancer 13, Watcher+Longbow S vs Aegis 12, Watcher+3xGnat vs Hornet 16 / vs Aegis 11; the same blind 2xHornet squad without the Watcher wins 0 of 20 against Aegis. Median time to kill 11-50 s, the Watcher is never destroyed.
Hybrid (normal AI search) 30 seeds: the Watcher changes nothing for 2xHornet vs Aegis/Lancer/Midge (30/30 wins either way, kill time 56 -> 53, 56 -> 50, 50 -> 45 s) and draws fire in 2v2 (2xHornet vs 2xHornet 14 -> 20 wins of 30).
**By itself the Watcher finds nothing** (first script target in 0 of 500 baseline ship-runs): it has no AI block, ray search cannot find a small ship (see the Hornet section). It needs `[AI]` blocks in the blueprint
(`tools/designs/support.py`, not my file) to scout on its own; the patrol and re-pick code are inert without them.

### R8. Measured effect (hybrid unless noted; orig -> final; 30 seeds 1v1, 20 seeds teams)
| set | model | n | win % | loss % | timeout % | damage taken | median kill s |
|---|---|---|---|---|---|---|---|
| 1v1 hybrid (28 scenarios with Hornet or Harrier) | Hornet | 420 | 57.6 > 70.0 | 38.6 > 28.1 | 1.0 > 0.7 | 3686 > 2999 | 110 > 69 |
|  | Harrier | 420 | 71.4 > 70.2 | 26.7 > 29.0 | 0.7 > 0.5 | 2559 > 2583 | 91 > 81 |
|  | Harrier, 60 seeds, without the Hornet/mirror scenarios | 660 | 74.2 > 73.2 | 24.1 > 25.5 | 0.3 > 0.8 | 2009 > 2079 | 89 > 80 |
| 2v3 + mixed teams with Hornet/Harrier | Hornet | 1080 | 80.7 > 84.2 | 14.0 > 11.0 | 5.3 > 4.4 | 3391 > 3042 | 89 > 79 |
|  | Harrier | 280 | 61.8 > 66.1 | 37.9 > 33.2 | 0.4 > 0.7 | 4498 > 4323 | 120 > 116 |
| swarm 6/9/12 Gnat, 1v2, 1v3, pirate | Hornet | 360 | 32.8 > 42.2 | 55.0 > 46.1 | 12.2 > 11.4 | 5718 > 5191 | 132 > 106 |
|  | Harrier | 360 | 35.6 > 35.8 | 54.2 > 53.9 | 10.3 > 10.0 | 5726 > 5998 | 105 > 106 |
| 1v1 default 1.5-3 km (r2 baseline files) | Hornet | 420 | 34.5 > 62.6 | 28.3 > 28.8 | 34.8 > 6.9 | 2540 > 2880 | 114 > 91 |
|  | Harrier | 420 | 46.0 > 61.0 | 17.6 > 28.6 | 36.4 > 9.3 | 1620 > 2708 | 90 > 98 |
| team/swarm/pirate default (r2 baseline files) | Hornet | 1260 | 49.9 > 73.0 | 13.0 > 16.7 | 36.9 > 10.2 | 2593 > 3796 | 92 > 107 |
|  | Harrier | 460 | 37.6 > 49.8 | 36.1 > 39.6 | 25.4 > 10.7 | 4575 > 5273 | 117 > 114 |
The Harrier's pooled 1v1 row drops by 1.2 points only because the improved Hornet is now among its opponents (the 60-seed row without Hornet and mirror scenarios: 74.2 > 73.2, i.e. unchanged, but 11 s faster; all round-2 features switched off by Custom Data give 74.2 as well).
Friendly fire: 0 for Hornet/Harrier in the 1v1 sets, 0.1-0.4 % of team ship-runs (as before); wingmate collisions 0-0.01 per ship-run. No script exceptions in any run; CPU per `Main` call 0.0145 -> 0.0111 ms (sim), peak tick 9 ms.
Robustness (Hornet+Harrier pooled, 1v1, 20 seeds; hybrid / default start distances; orig > final win %): `--vanilla-search-range 3500` 64.5 > 69.8 / 63.6 > 65.9; `--thruster-torque true` 57.9 > 56.6 / 39.8 > 49.5;
`--gyro-pitch-sign 1` (the convention the user's game contradicts) 62.1 > 57.0 / 40.5 > 47.1. In the two alternative-physics runs the hybrid result is flat to slightly worse (loss 33 > 37 and 31 > 36 %): the
gain comes from acquisition and the AI hand-over, which do not depend on how well the ship aims, while those variants only change how well it aims.
Scripts-only (`ai off; hunt`): unchanged 0 shots in 840 runs, with or without `--face --start-distance 400:900` (ray search cannot find a small ship, as in the Hornet section).

### R9. Still weak / needs from other files
- Harrier vs Longbow S 23 % (hybrid, unchanged), Hornet vs Harrier 30 % (Hornet side), Hornet/Harrier vs Lancer 53 %, vs Pirate 5-10 % solo (35-80 % in groups of 2-3): the sniper outranges and retreats at the same 98 m/s, slug damage kills in 1-2 hits; the pirate's roof turrets are not
  distinguishable from armour with ray hits (hull-point aim modes are flat), so shots are spread over about 30 blocks per kill.
- 12 idle Gnats take 300 s for one Hornet (13 of 20 timeouts): after each kill the next target costs 10-30 s of AI search plus confirm rays; the AI does most of the killing in that fight.
- Blueprint needs (not allowed here): Watcher with `[AI]` blocks; a third and fourth forward camera on Hornet/Harrier (acquisition at 2 km is limited to 1.5 rays/s, 3 % per ray, so 4 cameras would halve the 22 s to the first lock);
  RC and PB sit one armour plate behind the nose cameras (RC dies to one 90-damage hit once the plate is gone).
- Unverified in the game: power-cycling AI blocks re-triggers their search; AI blocks fly the ship once they hold a target and idle without one (patrol listening logic); camera charge builds past 2.6 km (the long confirm ray of both cameras);
  `MyIni` really splits keys at "=" (R5). A saved `[Hornet]`/`[Harrier]` Custom Data section keeps the old `AI Confirm Raycast Range=1300`: delete the section after pasting v2.2.

### R10. Reproduce
Private repo copy + `SIM_OUT=<dir> bash sim/build.sh`, `python3 -I tools/build_all.py --only "Hornet" "Harrier" "Watcher"` after each script edit (run `SimHost.exe` from inside the copy or pass `--repo`).
Sets: 1v1 `^1v1/((Hornet|Harrier)-vs-|.*-vs-(Hornet|Harrier)$)` 30 seeds; teams `^(2v3|mixed)/.*(Hornet|Harrier)` 20 seeds; unfair `^(swarm/(6|9|12)xGnat-vs-(Hornet|Harrier)|1v3/(Hornet|Harrier)-vs-3x(Gnat|Midge|Wasp)|1v2/(Hornet|Harrier)-vs-2x(Wasp|Midge|Gnat|Longbow S|Longbow L|Lancer)|pirate/.*(Hornet|Harrier))` 20 seeds,
hybrid `--start-distance 1500:2400`; parameter sweeps with `--custom-data "[Hornet]\nKey=value\n[Harrier]\nKey=value"` (keys must not contain "=").

## Round 2: wasp-lancer (`scripts/WaspAI.cs` v2.2-wasp, `scripts/LancerAI.cs` v2.2-lancer)

Written by the wasp-lancer agent. Method: private copy of the repo + sim (`repo`), and a **control copy** (`ctl`) that differs only in `WaspAI.cs` / `LancerAI.cs` (the
v2.1 originals, blueprints rebuilt); same seeds, `--jobs 2`. "Before" = ctl, "after" = final scripts; every number below is a pooled ship-run share for that model
(mirror fights count both sides; "win" = that model's side wins). Opponents are the blueprints of the moment the copy was made: **Hornet and Harrier already
were the v2.2 ones of the hornet-harrier agent** (stronger than in `r2-base-*`), everything else as in the round-2 baseline (settings hash 86da6f4425ff003e), so the
ctl rows are not identical to the files in `out/sim/results/r2-base-1v1` (Wasp 23.1 / 43.3 / 32.1 %, Lancer 47.4 / 17.4 / 34.8 % win / lose / timeout there; ctl reproduces
Wasp 23.3 / 47.1 / 28.1 and Lancer 49.3 / 21.7 / 28.3). Modes as in the earlier sections: **default** (shipped 1.5-3 km starts), **hybrid** (`--start-distance 1500:2400`),
**locked** (`--face --start-distance 800:950 --commands "0.5:ai off;0.5:lock;0.5:hunt"`), **scripts-only** (`--commands "0.5:ai off;0.5:hunt"`, also with `--face --start-distance 400:900`).

### V1. Lancer and Wasp take the Harrier v2.2 search and AI hand-over (R2-R5 of the section above)
Pasted with a script (`diff` against `HarrierAI.cs` shows only doctrine differences): `AiRays` with both forward cameras on the long ray + shortening to the last fight range, `AiRepick`
(power-cycle an AI block that held the nose 6 s without a lock), `AiHeldIgnored`, `Search2nd`, **HUNT search patrol** (`PatrolStep`, square of 1.2 km, AI listening stops), config keys with "="
renamed (R5), `GhostTime`/`AiStaleTakeover`/`Stickiness` made settings. Effect, 1v1 default (30 seeds x 14 opponents):
Lancer win 49.3 -> 61.4, lose 21.7 -> 28.3, timeout 28.3 -> 9.5 %, ever held a script target 69 -> 91 %; Wasp win 23.3 -> 31.9, lose 47.1 -> 53.1, timeout 28.1 -> 12.9 %, target 30 -> 49 %.
Hybrid and locked are unchanged for the Lancer (67.6 -> 67.9, 55.0 -> 54.0): the whole gain is the patrol (R4), i.e. the "no fight at all" timeouts. The losses rise because fights now happen.
**Patrol start**: every drone gets its own direction (+-) and radius (0.8-1.2 x 1.2 km, from its grid id; `Search Patrol Own Start Corner And Direction`), the first corner stays "forward".
Reason: with mirror-image starts two identical scripts flew the same square in lock-step and stayed 2.5-2.9 km apart for the whole 300 s (Harrier-vs-Lancer, Lancer mirror: 6 of 30 runs, identical
`ai_on_s` / `avg_speed` in both ships). Random start corner as well (first version) made hybrid worse (timeouts 1.9 -> 3.1 %), so the first corner is not randomised.

### V2. Wasp: rocket doctrine when the stock is unknown (`MaxFireRangeU`, `MaxLeadTimeU`, `StandoffU`, `FireRange`)
Locked duels (isolates gunnery): launchers fire from `Max Fire Range` 420 m / 2.0 s of flight / standoff 250 m (round-1 W5, chosen for hit rate per rocket). Gatling and rail ships shoot from 700-1400 m, so the Wasp spent
most of the fight "out of range" while being shot (gate log of a duel: out of range 965 ticks, not on target 0, 0 launches in 19 s). Sweeps (420 runs each, win %): standoff 150/250/350 = 35.2 / 34.8 / 40.5;
fire range 300/420/550(3 s) = 31.7 / 34.8 / 38.6; **standoff 350 + range 650 + 3.5 s = 43.6** (450/650: 41.7), hit rate falls 28 -> 16 % but damage dealt rises 3.7k -> 4.9k per ship. Jink 0/15/40 = 25.7/33.8/34.8, orbit 0 = 34.3,
salvo pause 0.2, window x1.5 / x0.7, jink gate off/x2, "do not fire while the target accelerates": all inside +-2.5 (a hold-fire gate on the instantaneous target acceleration is worse: 34.8 at 6 m/s^2).
Rule: the longer doctrine applies when `_rockets < 0` (launcher inventories empty = creative/unknown stock); with a counted stock (survival, `_rockets >= 0`) the v2.1 numbers stay, because 8 rockets cannot be spent at 16 % hits.
Config: `... (unlimited rockets)` keys. Effect: locked 35.5 -> 43.6 %, team proxy (1v3/2v3/mixed with Wasp, 10 seeds) 30.5 -> 40.4 (old doctrine with the other changes: 37.9).
Rocket friendly fire stays ~0 (3 of 820 Wasp ship-runs with friendly damage, all in scenarios where the Wasps are the opposing team; probably the sim AI, not checked).

### V3. Wasp acquisition: what limits it, what was tried
Wasp ship-runs in hybrid fights (360 runs, not the mirror): **with a script target win 68 % / lose 28 %, without 7 % / 88 %**; median first target 18 s, 43 % of the ships ever get one. The Wasp is killed
within 10-15 s of the first enemy lock, and the enemy locks (gatling lead is exact, the AI holds the nose on a ship that sits still) long before the Wasp does.
- The AI leads rockets by `v_target/150` rad (verified against the trace: nose error 0.4-3.4 deg = |v_perp|/150 for 1-10 m/s targets, 5-10 deg once the script ship closes in at 40-55 m/s; a circling 40 m/s ship would be 15 deg off), a 5 m ship at 1-2 km is 0.15-0.3 deg: a cone cannot find it.
  Rays: 6 of 8 straight down the boresight (`AI Confirm Boresight Rays (of 8)`, was 2 of 8): ever-locked 43 -> 52 % (hybrid, 8 of 8 gives 52 and the same win rate), win +1.
- **Lead-line rays (tried, removed)**: derive the target bearing from the nose turn rate (n - T (w x n) - v_me_perp/150, one ray per assumed flight time T, ray length 150 T). The model of the offset is right, but the AI's attitude loop
  jitters at 0.1-0.5 rad/s (limit cycle), so the turn rate is noise; 0 hits in 2,000 rays, predicted bearing 11-16 deg off against a 2.6 deg nose error (windows 0.5-3 s).
- **Hot rays (tried, removed)**: while hit, short (750 m) rays in a wide flat cone (14 x 4 deg): lock 48.3 -> 49.8 %, win 34.5 -> 34.8: the Wasp is hit only in the last 5-20 s of its life, too late.
- **Dash along the AI heading (kept, off by default: `Dash Along AI Heading`)**: the AI block flies at its speed limit (10 m/s in the sim, the wiki default) and a ray needs a few hundred metres, so after `Dash Starts After AI Stint` (4 s) of AI flight the script flies 8 s at
  90 m/s along the smoothed nose heading with the AI off (rays keep casting, anything that is not a live enemy ahead aborts it), 2.5 s back to the AI to re-aim, repeat. 1v1 hybrid, 60 seeds: win 34.0 -> 38.7, hit rate 10 -> 17 %, ever-locked 49 -> 55 %
  (against the control: Aegis 0 -> 13, Midge 20 -> 67, Mantis 7 -> 23 %); but with several drones it is bad: Wasp team set (74 scenarios, 15 seeds) win 30.5 -> 30.5 / **timeout 33.7 -> 43.6** with the dash against **41.4 / 18.9** without, mirror fights 13 % mutual kills
  (head-on closing speed 180 m/s), 1v3 vs Gnats 73 -> 33 %. Dashing Wasps are hard to lock for the enemy and cannot lock themselves, i.e. stalemates. Left in for experiments.
- Nothing helps against enemies that lock early and orbit at 40-60 m/s (Harrier, Lancer, Hornet, Aegis, Mantis): unlocked Wasps lose 85-95 % of those fights, locked ones still hit only 4-6 % (rockets, 2 s of flight, 35-40 m/s jinks). The Wasp
  beats Gnat, Stinger, Watcher (90-100 %) and sometimes Midge / Longbow L (30 / 20 %).

### V4. Lancer: tried and flat (hybrid unless noted, 420 runs, base 67.6 +-2.4)
Standoff 300 / 550 = 66.9 / 66.0, max fire range 650 = 64.5, burst 3 s / 0.3 s = 67.9, fire window x1.3 = 67.6, jink 50 = 66.9, orbit 25 = 68.8, min range 250 = 67.6; split vs focus fire in 1v2/1v3/2v3/mixed (1,290 ship-runs) 55.7 vs 55.9;
confirm ray range 1300 / 1800 / 2600: 67.9 / 70.0 / 68.1 (hybrid), 60.7 / 61.9 / 61.4 (default), team proxy 67.8 / 67.5 / 68.7, pirate 73.1 / 66.9 / 70.3 (60 seeds; ctl 75.3): kept 2600 like the Harrier. Patrol radius 1500 / 1800 or speed 90 = 61.2 / 61.4 / 59.8 (default,
base 60.5), **patrol on the six corners of an octahedron** (+-forward, +-right, +-up; tried for the "enemy is in the plane the square never enters" timeouts) 59.5, timeouts 10.5 -> 11.4 %: the sim AI searches once per 30 s of absolute time,
so what limits finding is how many search points the patrol visits, not the geometry. Aim point on the outermost hull points (guns, thrusters, turrets sit outside) 69.5 vs 67.9 in 1v1 but **52 vs 66 % against the Pirate**: removed.
Hull-point aiming matters a lot against a big target: Lancer vs Pirate (3 scenarios, 30 seeds) AUTO/FACING/SPREAD/ENGINES = 66-70 %, `CENTER` = 33 %.
**Dash (same code as the Wasp) is harmful for the Lancer**: ever-locked 96 -> 58 %, win 68 -> 44 %, collisions 0.10 per ship-run (the AI hands the nose to a lock within seconds; leaving the heading loses it) - not included in the Lancer.

### V5. Measured effect (30 seeds x 14 opponents 1v1, 420 ship-runs; teams 15 seeds)
| set | model | win % before > after | lose % | timeout % | hit % | ever-locked % | damage dealt / taken per ship |
|---|---|---|---|---|---|---|---|
| 1v1 default | Lancer | 49.3 > 61.4 | 21.7 > 28.3 | 28.3 > 9.5 | 35.4 > 36.7 | 68.6 > 91.2 | 6.3k > 8.8k / 1.9k > 2.4k |
| | Wasp | 23.3 > 31.9 | 47.1 > 53.1 | 28.1 > 12.9 | 7.4 > 12.0 | 29.5 > 48.8 | 2.3k > 3.3k / 4.1k > 4.5k |
| 1v1 hybrid | Lancer | 67.6 > 67.9 | 29.3 > 29.5 | 1.7 > 1.7 | 36.6 > 37.8 | 95.5 > 96.2 | 9.2k > 9.5k / 2.6k > 2.7k |
| | Wasp | 33.1 > 34.5 | 63.6 > 61.4 | 2.4 > 2.6 | 8.6 > 10.7 | 41.4 > 48.3 | 3.4k > 3.6k / 5.6k > 5.4k |
| 1v1 locked | Lancer | 55.0 > 54.0 | 27.4 > 27.6 | 17.6 > 18.3 | 41.0 > 40.0 | 94.8 > 94.8 | 11.5k > 11.1k |
| | Wasp | 35.5 > 43.6 | 54.3 > 49.5 | 9.5 > 6.4 | 28.2 > 16.3 | 97 > 97 | 3.8k > 4.9k / 5.2k > 4.7k |
| scripts-only (also `--face 400:900`) | Lancer, Wasp | 0 > 0 | 5-14 > 5-14 | 86-95 | 0 shots (Lancer 92 % of 80 shots in the face set) | 0 | unchanged |
| teams default (1v2, 1v3, 2v3, mixed, pirate, swarm with the model) | Lancer (1,230) | 52.8 > 66.6 | 18.0 > 23.0 | 29.1 > 10.4 | 37.2 > 36.0 | 71 > 94 | 14.2k > 15.6k / 2.3k > 2.8k |
| | Wasp (1,905) | 30.5 > 41.4 | 35.2 > 39.5 | 33.7 > 18.9 | | 39 > 53 | 5.0k > 8.0k / 5.7k > 6.3k |
Per opponent, 1v1 default, win before > after: Lancer vs Aegis 50>57, Basilisk 37>57, Gnat 60>97, Harrier 57>50, Hornet 47>23 (timeouts 0>20: two patrollers miss each other), Lancer 32>43, Longbow L 57>63, Longbow S 23>37, Mantis 30>37, Midge 63>77, Stinger 63>90, Wasp 70>93, Watcher 70>93.
Wasp vs Aegis 0>3, Basilisk 7>7, Gnat 73>97, Harrier 3>3, Hornet 7>7, Lancer 3>0, Longbow L 7>20, Longbow S 0>10, Mantis 0>7, Midge 20>30, Stinger 80>100, Wasp 33>37, Watcher 60>90.
Robustness (1v1 default, 30 seeds, win before > after): `--thruster-torque true` Wasp 27.1 > 35.5, Lancer 36.2 > 38.8; `--gyro-pitch-sign 1` Wasp 42.6 > 44.3, Lancer 46.9 > 47.1; `--vanilla-search-range 3500` Wasp 32.6 > 35.2, Lancer 70.2 > 67.1
(with every start acquirable the patrol is not needed and costs a few timeouts, 0.5 > 2.1 %). Friendly damage and collisions: 0 for both in 1v1; Lancer teams 0.0 % of ship-runs with friendly damage (ctl 0.3 % in the mixed set), Wasp 3 of 820 (see V2). No script exceptions; CPU per `Main` call
0.0101 > 0.0123 ms (Lancer), 0.0091 > 0.0114 ms (Wasp), peak tick 11.7 / 18.5 ms (sim).

### V6. Still weak
- **Wasp**: no way found to lock an enemy that orbits at 40-60 m/s from the AI nose (see V3); unlocked it dies in 10-15 s. Needs a target feed (a Watcher / scout with `[AI]` blocks over FLEET, designator turrets), a nose-aligned gatling for the AI to lead or AI Defensive blocks (blueprint, not my files).
  Rockets stay at 4-6 % hits against jinking 35-40 m/s targets; vs the Pirate 0 % (260 blocks, 3 roof turrets, hit rate 90 % but too little damage on the turrets before it dies).
- **Lancer**: a sniper that kites at equal speed (Longbow S holds 900 m at 90 m/s, outside the 750 m fire range) is never caught (win 37 %); Lancer mirror 43 %; Hornet 23 % (v2.2 Hornet, patrol mismatch); solo Lancer vs the Pirate 7 % (2x 60 %, 3x 90 %), 12xGnat-vs-Lancer 100 % timeouts
  (the Lancer disables 8 of 12 in 300 s but spends 2/3 of the time in AI mode between locks, hit rate 20 %). Losses rise where fights now happen (vs Mantis 40 > 63 %, Aegis 20 > 40 %).
- Scripts-only: unchanged, 0 shots. Ray search cannot find a 5 m ship (7 cameras x 2 km/s of charge over the whole sphere: about 30,000 s per expected hit).
- Two patrolling ships with the same script still miss each other when each is more than ~2.5 km from the other's square (Hornet-vs-Lancer default timeouts 0 > 20 %); the search is limited by the AI block's search timer, not by the patrol geometry.

### V7. Assumptions about the real game
- AI Flight speed limit 10 m/s (the wiki default of the block); the sim's AI approach at that speed is what makes the dash and the patrol matter. If the player raises the slider the AI approaches faster and the patrol is less needed (3500 m search-range what-if: -3 points for the Lancer).
- The AI Offensive block leads rockets with a mean rocket speed (150 m/s) and holds its nose on the target between searches; a block that is switched off and on searches again (R3 of the section above); the AI block's search interval is 30 s.
- Launchers of a creative-world ship have empty inventories (`_rockets` = -1 = "unlimited": long doctrine); loaded launchers (survival) report a stock and keep the v2.1 doctrine. Unverified: what the inventory of a spawned Wasp reports in a creative world.
- Camera charge accumulates without a cap (sim); `MyIni` splits keys at "=" (the renamed keys). Saved CustomData wins over code defaults: a `[Wasp]` / `[Lancer]` section from v2.1 keeps `Max Fire Range=420`, `AI Confirm Raycast Range=1300` etc. (Wasp: the new `(unlimited rockets)` keys are added on first run); delete the section after pasting v2.2.

### V8. Reproduce
Private copy (`tar` of the repo without `.git` and `out/sim`, `SIM_OUT=<dir> bash sim/build.sh`), `python3 -I tools/build_all.py --only Wasp Lancer` after each edit; control copy = same with the original two scripts and its own `SIM_OUT`.
Sets: 1v1 `^1v1/(M-vs-|.*-vs-M$)` 30 seeds (M = Wasp | Lancer); teams `^(1v2|1v3|2v3|mixed|pirate|swarm)/.*M` 15 seeds; pirate `^pirate/.*Lancer` 30-60 seeds; sweeps with `--custom-data "[Wasp]\nKey=value"`.
Debugging used: a per-gate counter in `FireGate` printed in the status screen (out of range / not on target / track stale / OPEN ticks), and for the Wasp a pending-rocket log (predicted impact point vs the track at impact time: miss < 1 m for a steady target at 1.2-1.8 s of flight, 3-4 m against a jinking Wasp).


## Round 2: longbow-mantis (`scripts/LongbowSniperAI.cs` v2.0, `scripts/MantisAI.cs` v2.2-mantis; `LongbowMothership.cs` unchanged)

Task: railgun duty cycle with the corrected 20 s capacitor, positioning while charging, survive Wasp / Midge swarms. Baseline = the round-2 baseline
(`docs/sim-r2-base-1v1.md`, `-team.md`, settings hash `86da6f4425ff003e`). All comparisons below are the same scenarios and the same seeds (0..29, team sets 0..39),
run on a private copy of `out/Blueprints` frozen at the baseline state (so other agents' rebuilds cannot move the opponents), one change at a time.
Everything below is a sim result unless it is listed under the assumptions at the end.

**Dev setting.** At the default search range (2,500 m, starts 1.5-3 km) about a third of the 1v1 runs of these models are "never in range" (136 / 163 / 134 of 420
ship-runs of Longbow S / L / Mantis in `r2-base-1v1`) and say nothing about fighting. Almost all tuning was therefore done with `--vanilla-search-range 3500`
(settings hash `7386a044ade6fa95`: every run is a real fight; timeouts of the three models fall from 40 / 49 / 32 % to 11 / 18 / 2 %), and every final change was
re-measured at the default range and scripts-only as well.

### P1. What the traces showed (no script change yet)
- **The railgun cycle is what the data says**: slug to slug 21.2-22.4 s when the ship sits still (capacitor 0.016 MWh, recharge draw 3.6 MW x 0.8 = 20 s, plus the
  0.5 s trigger delay; `ReloadTime + 0.5` = 20.5 s was a fair estimate of the recharge). The Longbow S has ONE small-grid battery of 4 MW.
- **Thrust is served before the recharge** (power groups: Thrust 80, DefenseAdative 90, `defs_stats.json`). The recharge only runs at full speed while the thrusters
  take less than about 0.4 MW. The old `_powerScale` throttle (load > 97 % -> thrust x 0.9 every 1/6 s, floor 10 %) enforces that, and the price is that a
  recharging Longbow cannot move: in the 2v3 traces most of the 5 s samples of `_powerScale` read the floor of 0.10 (`Thrust cap 10 %` on the PB screen), including the approach to the stand-off after the first slug.
  If the thrusters are not throttled the capacitor gets what is left (2.4 MW of thrust -> 44 % rate -> 45 s per shot).
- **A held trigger**: in `2v3/2xLongbow S-vs-3xMidge` seed 1 (an instrumented sim build printed `ShootOn` at every accepted trigger; `2v3/2xLongbow S-vs-3xGnat` seed 5 shows the same as 14 sim shots against 6 shots the script confirmed) the railgun had `ShootOn = true` from t = 86 s to the end (set by the AI stand-in during a hand-over; `Release()`
  calls `SetFire(false)` but `SetFire` returns when `on == _firing`, and `_firing` is false after every hand-over). A railgun with the trigger held fires by itself the moment
  it is charged, at whatever the nose points at, and `IsShooting` never ends, so the script never confirms a shot, "refuses" every 3.5 s and never jinks (it believes a shot is due).
- **The first slug of the fight is spent by the AI.** The AI blocks (sim stand-in: fire when the gun axis is inside 5 deg and the target inside weapon range) shoot the full
  capacitor at 1.2-1.5 km before the script has any lock; the script's own first trigger is then refused (`2v3/2xLongbow S-vs-3xGnat` seed 5: trigger at t = 23.0 and 26.5 s
  refused, accepted at 30.7 s). A Longbow gets 2-4 shots in a 1v1, so losing the first one matters.
- **Why shots miss** (134 slugs of Longbow S vs Hornet / Harrier / Lancer / Aegis, closest approach of slug and target body centre from the trace): hits pass within 0.2-1.5 m
  (median 0.9 m), misses 0.6-8.7 m (median 3.0 m); the aim error at the trigger is 0.000-0.012 deg (0.2 m at 1 km), far inside the window, so the miss is the target moving
  during the ~1 s of slug flight. An offline replay of the target tracks (`out` traces, 2,285 sample shots at 500-1,300 m, miss < 1.5 m):

  | lead model | within 1.5 m |
  | :--- | ---: |
  | constant velocity | 28.7 % |
  | the script's acceleration filter (0.8/0.2 per fix, steadiness gate, fixes every 0.25 s) | 36.2 % |
  | the same without the gate | 40.7 % |
  | acceleration of the last 0.25 s interval | 55.0 % |
  | the same x 0.8 / decaying with 1.5 s | 56.9 % / 57.1 % |
  | last 0.1 s interval, x 0.8 | 58.9 % |
  | constant-turn-rate arc | 53.9 % |

  So the smoothing (time constant about 1 s) and the gate were worth 20 points against a jinking target. Hit probability does not depend noticeably on |a| or jerk (56-62 % in
  every bucket), which is why waiting for a "calm" moment did not help in round 1.
- **Mantis gatling gate**: `GATGATES` counters in three Harrier-vs-Mantis fights: the target was inside the 650 m gatling gate in only 21 % of the ticks (400 of 1,899; 445 of
  1,688; 408 of 2,202) and when it was, the fix was fresh and the aim inside the window in 75-100 % of them. The ship holds 650 m and the gatling reaches 800 m, so it
  stood just outside its own gate while the Harrier shot at it from 700 m.
- **The AI trap** (not fixed, not mine): the stand-in AI picks the closest enemy BODY every 30 s, and a wreck or fragment is a body. `2v3/2xLongbow S-vs-3xGnat` seed 5: both ships
  sat at 597 m from the wreck of the first Gnat for 120 s (`ai_on_s` 243 of 300) while the nearest intact Gnat stood 11 m farther, at 608 m, the script ignoring the wreck (wreck rule) and the AI
  flying at nothing else. Most 2v3 timeouts are this and not DPS (see remaining weaknesses).

### P2. Changes (both files unless it says otherwise; every new key is in the `[Longbow]` / `[Mantis]` section and defaults are given)
1. **Fresh acceleration for the lead** (`Track.AccF`, `Track.EA`, keys `Fresh Acceleration Weight` 0.8, `Fresh Acceleration Scale` 0.9; 0 = old behaviour). `AccF` is the
   acceleration of the last fix intervals (weight 0.8 per fix, clamped at 30 m/s^2) and replaces the smoothed, steadiness-gated one in `P()`, `V()` and `Lead()`; the smoothed `Acc` stays for
   `PeakAcc` (stand-off), `QuietSince` (wrecks) and the calm gate. Fixes still come every 0.25 s (`MaintainLock(0.12)` and a scale of 1.0 were not better).
2. **Full thrust while travelling** (`PowerAndHealth`, `Control`; keys `Full Thrust While Travelling` true, `Travelling Distance m` 200). While the ship is more than 200 m from its
   stand-off the `_powerScale` throttle is lifted (the recharge takes what is left; the ship is not recharging usefully while it flies 600 m anyway); the throttle is back as soon as it arrives.
   Lifting it also while under fire changed nothing (tested, removed).
3. **Held trigger** (`UpdateGuns`, `SetFire`): a railgun that reports `IsShooting` with no shot pending is sent `Shoot_Off` (every 0.1 s, not in the AI phase); `SetFire` re-sends
   `Shoot_Off` as well as `Shoot_On` twice a second (Mantis: also `SetGat`).
4. **Shot timing** (`UpdateGuns`): after a confirmed slug `ReadyAt = now + ReloadTime` (was + 0.5 s), and once a shot has been confirmed through `IsShooting` a trigger that did not
   show `IsShooting` within 0.25 s is judged refused and asked again after 0.5 s (was 1.5 s judgement + 2 s wait). Keys `Retrigger Lead s` 0, `Retrigger After Refusal s` 0.5.
5. **The first slug is kept** (`AiFriendGuard`, `HoldRail`, `ApplyRail`; key `Railgun Off While AI Searches`, Longbow true, Mantis false): until the script has fired its own first
   slug, the railgun is disabled (`Enabled = false`) while the AI blocks search and nothing is locked, and enabled again when the script takes over (and after `Railgun Off Longest After First Lock s` = 60 s if the script has still not fired: it cannot aim, let the AI shoot). The limit "until the first slug" matters:
   with the railgun off whenever nothing is locked, `2v3/2xLongbow S-vs-3xGnat` fell 22 -> 10 % (the AI's shots at stationary targets are good ones); with the limit it is 20 %.
6. **No thrust around the shot** (`Control`; key `No Thrust Around The Shot` true): translation thrust is zero from the trigger until the slug has left (0.5 s; 2 s for the large railgun) and in a window of
   0.25 s before the trigger (at most once per 1.25 s). Costs nothing measurable when thrust has no torque and is the largest robustness gain when it has (below). Unbounded versions stall: coasting for as
   long as the settle counter was up (3 ticks before the trigger, or 1.5 s) kept a ship that was waiting for a shot it was not allowed to take (mate in the line, aim not settled) coasting forever
   (2xMantis vs 3xGnat 88 -> 40 %, vs 3xStinger 92 -> 42 %); coasting only while pending lost part of the Mantis gain (100 seeds, 6 opponents: 67 -> 60 %).
7. **Mantis only: gatling fire range 650 -> 760 m** (`Gatling Range`, the data range is 800 m and bullets are randomised 80-100 %).


### P3. Measured effect (final scripts; same scenarios and seeds before / after; win / loss / timeout %)

**1v1, search range 3,500 m (dev setting), 30 seeds, 13 opponents (mirrors count both ships)**  (win / loss / timeout %, before -> after)

| opponent | Longbow S | Longbow L | Mantis |
| :--- | ---: | ---: | ---: |
| **all 13 opponents** | 65/23/11 -> 73/14/11 | 51/29/18 -> 56/26/16 | 57/39/2 -> 61/35/3 |
| Aegis | 70/30/0 -> 100/0/0 | 23/67/10 -> 60/33/3 | 67/33/0 -> 60/37/0 |
| Basilisk | 27/0/73 -> 33/0/67 | 63/0/37 -> 57/0/43 | 7/93/0 -> 17/80/3 |
| Gnat | 100/0/0 -> 100/0/0 | 97/0/3 -> 93/0/7 | 100/0/0 -> 100/0/0 |
| Harrier | 60/40/0 -> 83/10/7 | 17/80/3 -> 50/40/3 | 37/60/0 -> 50/47/0 |
| Hornet | 83/13/3 -> 90/3/7 | 47/47/7 -> 47/50/0 | 67/33/0 -> 73/17/10 |
| Lancer | 70/27/3 -> 93/3/3 | 20/77/3 -> 47/50/3 | 53/40/0 -> 43/50/3 |
| Longbow L | 13/87/0 -> 37/43/13 | 38/38/17 -> 43/43/10 | 27/73/0 -> 47/53/0 |
| Longbow S | 48/48/0 -> 43/43/0 | 87/13/0 -> 43/37/13 | 27/70/0 -> 43/57/0 |
| Mantis | 70/27/0 -> 57/43/0 | 73/27/0 -> 53/47/0 | 45/45/3 -> 47/47/3 |
| Midge | 57/0/43 -> 77/0/23 | 37/10/53 -> 33/17/50 | 43/50/7 -> 47/50/0 |
| Stinger | 97/0/3 -> 100/0/0 | 80/0/20 -> 97/0/3 | 100/0/0 -> 100/0/0 |
| Wasp | 100/0/0 -> 100/0/0 | 60/10/27 -> 80/7/10 | 97/0/0 -> 97/0/0 |
| Watcher | 70/0/30 -> 70/0/30 | 40/0/60 -> 37/0/63 | 87/0/13 -> 83/0/17 |
| hits per shot % | 65 -> 75 | 68 -> 81 | 37 -> 36 |

**1v1, default settings (hash 86da6f4425ff003e, search range 2,500 m, "before" = `out/sim/results/r2-base-1v1`, seeds 0..29)**  (win / loss / timeout %, before -> after)

| opponent | Longbow S | Longbow L | Mantis |
| :--- | ---: | ---: | ---: |
| **all 13 opponents** | 44/15/40 -> 51/8/39 | 33/17/49 -> 34/16/48 | 41/25/32 -> 41/23/34 |
| Aegis | 47/23/30 -> 70/0/30 | 7/33/60 -> 30/13/53 | 50/17/33 -> 33/30/33 |
| Basilisk | 23/0/77 -> 30/0/70 | 50/0/50 -> 50/0/50 | 7/70/23 -> 13/60/27 |
| Gnat | 67/0/33 -> 67/0/33 | 50/0/50 -> 50/0/50 | 63/0/37 -> 63/0/37 |
| Harrier | 33/23/43 -> 53/0/47 | 10/50/40 -> 27/27/40 | 30/43/27 -> 30/40/27 |
| Hornet | 57/13/30 -> 63/3/33 | 23/23/53 -> 17/33/50 | 47/17/37 -> 43/10/47 |
| Lancer | 50/23/27 -> 73/3/23 | 7/57/37 -> 27/40/33 | 40/30/30 -> 33/30/33 |
| Longbow L | 3/53/43 -> 10/33/53 | 23/23/47 -> 27/27/43 | 13/47/40 -> 37/23/40 |
| Longbow S | 30/30/37 -> 27/27/37 | 53/3/43 -> 33/10/53 | 20/40/40 -> 23/37/40 |
| Mantis | 40/20/40 -> 37/23/40 | 47/13/40 -> 23/37/40 | 30/30/37 -> 28/28/40 |
| Midge | 47/0/53 -> 63/0/37 | 33/7/60 -> 23/17/60 | 33/33/33 -> 33/37/27 |
| Stinger | 70/0/30 -> 70/0/30 | 57/0/43 -> 63/0/37 | 70/0/30 -> 70/0/30 |
| Wasp | 70/0/30 -> 70/0/30 | 40/7/50 -> 57/0/40 | 83/0/13 -> 83/0/13 |
| Watcher | 53/0/47 -> 53/0/47 | 37/0/63 -> 27/0/73 | 63/0/37 -> 53/0/47 |
| hits per shot % | 65 -> 75 | 70 -> 81 | 39 -> 36 |

About a third of these default runs are "never in range" before and after (that is the 40 / 48 % timeouts); the fights that do happen gain what the dev setting shows.

**Locked duels, scripts only (`--face --start-distance 800:950 --commands "0.5:ai off;0.5:lock;0.5:hunt"`, Longbow L cannot lock in this harness)**  (win / loss / timeout %, before -> after)

| opponent | Longbow S | Mantis |
| :--- | ---: | ---: |
| **all 13 opponents** | 61/17/21 -> 70/11/17 | 61/28/10 -> 62/27/11 |
| Aegis | 87/7/7 -> 90/0/10 | 80/7/13 -> 77/10/13 |
| Basilisk | 17/10/73 -> 27/0/73 | 13/87/0 -> 13/83/3 |
| Gnat | 63/0/37 -> 63/0/37 | 70/0/30 -> 70/0/30 |
| Harrier | 47/43/10 -> 97/3/0 | 73/20/7 -> 57/43/0 |
| Hornet | 80/10/10 -> 93/0/7 | 67/17/17 -> 73/10/17 |
| Lancer | 70/20/10 -> 93/0/7 | 47/47/7 -> 63/30/7 |
| Longbow L | 43/0/57 -> 60/0/40 | 100/0/0 -> 100/0/0 |
| Longbow S | 47/47/3 -> 43/43/3 | 40/57/0 -> 53/47/0 |
| Mantis | 57/40/0 -> 47/53/0 | 42/42/13 -> 42/42/13 |
| Midge | 43/13/43 -> 63/17/20 | 33/67/0 -> 37/60/3 |
| Stinger | 67/0/33 -> 67/0/33 | 67/0/33 -> 67/0/33 |
| Wasp | 90/0/10 -> 97/0/3 | 90/7/3 -> 87/7/7 |
| Watcher | 100/0/0 -> 100/0/0 | 93/0/7 -> 87/0/13 |
| hits per shot % | 72 -> 83 | 49 -> 44 |

**Team sets, search range 3,500 m (62 scenarios; 1v2 / 1v3 / 2v3 / mixed / pirate)**  (my side win / loss / timeout %, before -> after; 40 seeds; 62 scenarios in both runs)

| scenario set | before | after |
| :--- | ---: | ---: |
| all |  31/ 38/ 31 |  34/ 38/ 28 |
| 1v2 |   2/ 76/ 22 |   5/ 76/ 18 |
| 1v3 |   0/ 66/ 34 |   1/ 70/ 29 |
| 2v3 |  16/ 27/ 56 |  20/ 25/ 54 |
| mixed |  85/  8/  6 |  86/  8/  6 |
| pirate |  48/ 11/ 41 |  55/ 11/ 35 |

Where the sides can win (before -> after):

| scenario | before | after |
| :--- | ---: | ---: |
| 2v3/2xMantis-vs-3xGnat |  62/  0/ 38 |  90/  0/ 10 |
| 2v3/2xMantis-vs-3xStinger |  82/  0/ 18 |  92/  0/  8 |
| 2v3/2xMantis-vs-3xWasp |  38/ 30/ 32 |  60/ 22/ 15 |
| 2v3/2xLongbow S-vs-3xGnat |  22/  0/ 78 |  22/  0/ 78 |
| 2v3/2xLongbow S-vs-3xStinger |  22/  0/ 78 |  18/  0/ 82 |
| 2v3/2xLongbow S-vs-3xWasp |   8/  8/ 85 |  10/  8/ 82 |
| 2v3/2xLongbow S-vs-3xMidge |   0/ 15/ 85 |   5/ 12/ 82 |
| 2v3/2xLongbow L-vs-3xHornet |   0/ 85/ 15 |   0/ 60/ 40 |
| 1v2/Mantis-vs-2xWasp |  25/ 70/  5 |  38/ 58/  2 |
| 1v2/Longbow S-vs-2xWasp |   5/ 15/ 80 |  25/ 35/ 40 |
| 1v2/Longbow L-vs-2xWasp |   0/ 18/ 82 |   2/ 40/ 58 |
| 1v3/Longbow L-vs-3xWasp |   0/ 22/ 78 |   0/ 60/ 40 |
| pirate/Longbow S-vs-Pirate |   0/  0/100 |   2/  0/ 98 |
| pirate/2xLongbow S-vs-Pirate |  35/  0/ 65 |  48/  0/ 52 |
| pirate/3xLongbow S-vs-Pirate |  80/  0/ 20 |  82/  0/ 18 |
| pirate/Longbow L-vs-Pirate |  55/  0/ 45 |  50/  0/ 50 |
| pirate/2xMantis-vs-Pirate |  32/ 25/ 42 |  62/ 20/ 18 |
| pirate/3xMantis-vs-Pirate |  60/ 12/ 28 |  90/  2/  8 |
| mixed/Hornet+Longbow S-vs-Hornet+Longbow S |  28/ 42/ 28 |  22/ 42/ 35 |
| mixed/Hornet+Longbow S-vs-Lancer+Mantis |  30/ 32/ 38 |  48/ 20/ 32 |
| mixed/Lancer+Mantis-vs-Lancer+Mantis |  52/ 40/  8 |  58/ 35/  8 |

**Team sets, default settings (47 scenarios of the sets that contain these models, "before" = `out/sim/results/r2-base-team`, seeds 0..29)**  (my side win / loss / timeout %, before -> after; 30 seeds; 47 scenarios in both runs)

| scenario set | before | after |
| :--- | ---: | ---: |
| all |  30/ 10/ 59 |  33/  9/ 58 |
| 2v3 |  12/ 19/ 69 |  15/ 17/ 68 |
| mixed |  58/  6/ 36 |  60/  6/ 35 |
| pirate |  48/ 13/ 39 |  56/  9/ 35 |
| swarm |   0/  0/100 |   0/  1/ 99 |

**Robustness** (1v1 set, search range 3,500 m, 30 seeds; win / loss / timeout %):

| engine setting | model | before | after |
| :--- | :--- | ---: | ---: |
| `--thruster-torque true` | Longbow S | 35/25/38 | 60/18/22 |
| `--thruster-torque true` | Longbow L | 45/24/30 | 57/20/21 |
| `--thruster-torque true` | Mantis | 48/40/12 | 61/32/6 |
| `--gyro-pitch-sign 1` | Longbow S | 20/56/24 | 23/42/34 |
| `--gyro-pitch-sign 1` | Longbow L | 42/38/16 | 31/37/30 |
| `--gyro-pitch-sign 1` | Mantis | 24/55/21 | 26/55/19 |


### P4. Tried and not adopted (same seeds; win / loss / timeout % over the 13-opponent 1v1 set at search range 3,500, 30 seeds, Longbow S unless it says otherwise)
Noise of one such cell is about +-2.5 points (390 ship-runs); differences below 4 points are not claimed.
| change | result |
| :--- | :--- |
| **Close in while nobody damages me** (stand-off x 0.5 over 15 s after the first slug, back at the first damage) | Longbow S 65/23/11 -> 58/39/3 (vs Midge 57/0/43 -> 43/53/3: the Midge shoots back at 500 m); removed |
| **Ignore fragments** (bounding box half-diagonal < 1.7 m: debris of a shot-up ship is a "small grid", up to every tenth slug of a 2v3 went into one) | 2v3 sets 17 -> 6 % wins, 2xMantis vs 3xGnat 60 -> 0 %: each `IgnoreTarget` blocks the AI for 15 s and turns the nose to the spot (wreck rule), and the stand-in AI is trapped on the fragment anyway (it takes the closest body); removed |
| `AiStaleTakeover` 3 -> 8 s | L 51/29/19, S 65/22/12, Mantis 55/43/1: no effect (Mantis -2) |
| `Max Aim Window m (railgun)` 2 -> 1.0 / 0.6 m | S 66/20/13 and 68/20/11 (hit rate 65 %, damage per hit 3.5k unchanged): the aim error at the trigger is already 0.01 deg, the window is not what limits |
| `Preferred Engage Range` 1000 -> 800 / 1200 (Longbow, after the other changes) | S 76 -> 72 / 70 % wins, hit rate 76 -> 80 / 70 %; 1000 stays |
| `Mantis` stand-off 650 -> 500 / 800 (cap 900) | Mantis 66/31/1 -> 63/29/7 and 61/28/9; vs Longbow S 43 -> 67 / 60 % but vs Longbow L 37 -> 13 %, Basilisk 43/57/0 -> 17/0/83 (it stays out of the rockets and cannot kill); no net gain |
| `Aim Point` CENTER / FACING / SPREAD / ENGINES on the Basilisk (30 seeds, identical custom data in both runs) | CENTER is the best against the Basilisk (S 27 -> 37, L 63 -> 77 %; its core sits at the centre of its box) and the worst against the Pirate (S 2x: 33 -> 3 %, L: 50 -> 0 %); AUTO stays |
| thrust lift while under fire (`Full Thrust While Under Fire`) | identical to travel-only in 1v1 and team sets; removed |
| `MaintainLock(0.12)` (more fixes) and `Fresh Acceleration Scale` 1.0 | S 72 / 73 % vs 75 %, Mantis 57 / 59 vs 61: no gain |
| `No Thrust Around The Shot` unbounded: 1.5 s before the shot / 3 settle ticks before the trigger | S 75 -> 62 %, Mantis 61 -> 49 % (the ship coasts while it is out of range or waiting for the aim) / S 75 -> 71 % and 2xMantis vs 3xGnat 88 -> 40 % (stall while a shot is held back); the adopted window is bounded |
| scripts-only search (`ai off`, starts 500-900 m, no lock) | 82-87 % timeouts before and after: the spiral scan cannot find a 5 m ship in a 25 deg cone; only locked duels measure the fire control |


### P5. Remaining weaknesses (sim)
- **Team and unfair fights moved little** (table above: all scenarios 31 -> 34 % wins, +-3 at 40 seeds; the gains are Mantis vs Wasp / Gnat / Stinger and the Pirate). A Longbow against two or three
  gatling ships still dies (1v2 / 1v3 vs Hornet, Harrier, Lancer: 90-100 % losses before and after) and against Wasp / Midge / Gnat swarms the fight ends by the clock: 2v3 timeouts
  are about 54 %. Two reasons that are not DPS: the **AI trap** (P1; any fight in which the last enemy has to be found by the AI blocks after the first kills) and the
  cadence (one slug per 21 s, 5-8 hits for a 54-block Wasp: 22 shots for three Wasps in 300 s is the whole budget). The fix needs the stand-in AI to ignore wrecks / fragments (engine), or a
  spotter (Watcher / Hornet over FLEET), or a sensor on the Longbow.
- **Scripts-only search finds nothing**: `ai off; hunt` from the default start 95 % timeouts, 0 wins, before and after (also 82-87 % at 500-900 m). The fire control itself is better
  (locked duels above: Longbow S 61 -> 69 %, hit rate 72 -> 82 %).
- **Cells that got worse** (n = 30 per cell, +-9 points; the dev-setting table above): Mantis vs Aegis 67 -> 60 % (100 seeds: 69 -> 61) and vs Lancer 53 -> 43 (100 seeds: 42 -> 48), Longbow L vs Watcher 40 -> 37
  (default range 37 -> 27), Mantis vs Watcher 87 -> 83 (default 63 -> 53); Longbow S vs Mantis 70 -> 57 and Longbow L vs Mantis 73 -> 53 are the Mantis meeting a better Longbow, and the Mantis column shows it
  (Longbow S 27 -> 43). At the default range the Mantis total is unchanged (41 -> 41 %), the gain of the dev setting (57 -> 61 %) is mostly Lancer / Harrier / Longbow / Basilisk.
- **Longbow L** wins 56 % in the dev setting but loses to every gatling ship it cannot kill with its first slug (Hornet 53 / 40, Lancer 40 / 50, Harrier 53 / 43 win / loss): one slug
  per 60 s, 2 s trigger delay. **Longbow S vs Longbow L** 30 / 60 (the L has 2,000 m of range and a slug that kills it). **Mantis** vs the two Longbows 33-47 % wins, vs Harrier 47 / 47, vs Basilisk 20 / 77.
- **Big or hard-to-kill targets**: Longbow S vs Basilisk 20 / 0 / 80, vs Watcher 53 / 0 / 47: the slug does 350-1,500 on a graze and about 3,800 through the middle of a small ship,
  and the capable-test needs the single PB / RC / all batteries / all guns gone. An aim point that found the PB / RC (or the single gun of the Midge) would need the target's layout, which a script
  cannot read; CENTER is better against the Basilisk and fatal against the Pirate.
- **Friendly fire**: none from the Longbows in 40 seeds of the team set, 90 gatling damage in one Lancer + Mantis mirror run; collisions 12 / 3 / 0 events (Longbow L / S / Mantis), as before.
- **Per-tick cost** is unchanged (sim wall clock per `Main` call 0.0153 / 0.0160 / 0.0137 ms against 0.0151 / 0.0165 / 0.0139; no loop was added to the tick, `HoldRail` / `ApplyRail` touch the gun list once per state change).
- With **`--gyro-pitch-sign 1`** (the sign convention the in-game test disproved; scripts cannot aim) the hold of the first slug costs: Longbow L 42 -> 31 %, S 20 -> 23, Mantis 24 -> 26 (L was 42 -> 21 before the 60 s limit).
  This is not a regression in the real game but it shows the hold relies on the script's own aim.

### P6. Assumptions about the real game (unverified; the sim cannot tell)
- A railgun in a creative world reports `IsShooting` while it charges for an accepted shot (R3 of round 1); the early refusal and the held-trigger release depend on it, and fall back to the old
  1.5 s / ammo-drop paths when it does not. If `IsShooting` is true for a whole recharge, `Shoot_Off` is sent every 0.1 s while no shot is pending, which is harmless. If it is reported later than 0.25 s
  after the trigger, an accepted shot is judged refused and the ship asks again every 0.75 s until the capacitor is full again (the cycle model degrades to round 1 behaviour).
- Thrust is served before the recharge of the railgun (power groups from `ResourceDistributionGroups.sbc`: Thrust 80, DefenseAdative 90). If the game distributes proportionally, change 2 is neutral.
- A disabled railgun keeps its charge (`Railgun Off While AI Searches`). If disabling drops the charge, set the key to false; the cost is the first slug.
- Off-centre thrust turns the ship (the sim's default `ThrusterTorque` is false; the real game applies the force at the thruster). `No Thrust Around The Shot` is the answer to that; with torque on in the
  sim the Longbow S goes 35 -> 60 %, without torque it costs nothing measurable.
- The AI blocks pull the trigger of fixed guns and leave the toggle on (sim stand-in only); the real behaviour is unknown.
- Detected-entity velocities are exact (the fresh acceleration is a difference of two of them over 0.1-0.25 s; with noisy velocities lower `Fresh Acceleration Weight` to 0.5 or set it to 0).
- Gatling bullets reach 800 m (data range); the 80-100 % range randomisation means some shots at 700-760 m fall short.
- Saved CustomData wins over code defaults: drones pasted from an older package keep their `[Longbow]` / `[Mantis]` keys; the new keys are added with the defaults on the next start, the changed default is
  Mantis `Gatling Range` (an old section would not contain it unless it was saved by v2.1, which wrote 650).

### P7. Reproduce
`bash sim/build.sh`, `python3 -I tools/build_all.py --only "Longbow S" "Longbow L" "Mantis" --no-zip`, then (private copy of `out/Blueprints` via `--repo DIR`, DIR holding `defs_stats.json` and `out/Blueprints`):
`SimHost.exe run --repo DIR --filter '^1v1/.*(Longbow|Mantis)' --seeds 30 --jobs 2 --vanilla-search-range 3500` (dev setting; drop the last flag for the default), locked duels
`--filter '^1v1/.*(Longbow S|Mantis)' --face --start-distance 800:950 --commands '0.5:ai off;0.5:lock;0.5:hunt'`, team set `--seeds 40 --vanilla-search-range 3500` with the filter
`^(2v3/2x(Longbow S|Longbow L|Mantis)-vs-3x(Hornet|Wasp|Midge|Gnat|Stinger)|1v2/(Longbow S|Longbow L|Mantis)-vs-2x(Hornet|Wasp|Midge|Harrier|Lancer)|1v3/(Longbow S|Longbow L|Mantis)-vs-3x(Hornet|Wasp|Midge)|pirate/[23]?x?(Longbow S|Longbow L|Mantis)-vs-Pirate|mixed/Hornet\+Longbow S-vs-(Hornet|Lancer|Wasp\+2xGnat|Aegis|Longbow S|Mantis|Hornet\+Longbow S|Lancer\+Mantis)|mixed/Lancer\+Mantis-vs-(Hornet|Aegis|Lancer|Longbow S|Midge|Lancer\+Mantis))$`;
robustness `--thruster-torque true`, `--gyro-pitch-sign 1`. The offline lead study replays `SimHost.exe one --trace-every 2` CSVs (10 seeds each of Hornet / Harrier / Lancer / Aegis vs Longbow S): the
predictors are the filter of `Track.Set` with different weights. Debug builds of the sim (extra `Console.Error` lines in `Weapons.cs`: refused triggers, capacitor ratio, slug spawn position) were built from a copy outside the repo.


## Round 2: aegis-basilisk (`scripts/AegisAI.cs` v2.2-aegis, `scripts/BasiliskAI.cs` v2.2-basilisk)

Written by the aegis-basilisk agent. Method: a private copy of the repo + sim in which every other script is exactly the round-2 baseline one (settings hash 86da6f4425ff003e; the control
copy `ctl` reproduces `out/sim/results/r2-base-1v1` and, on `^(2v3|mixed|pirate|swarm)/`, `r2-base-team` number for number), same seeds 0..29, `--jobs 2`. "Before" = the v2.1 scripts of both
models in `ctl`, "after" = the final files. The opponents are therefore the round-2 baseline ones (v1.8 Hornet, v2.1 Harrier/Lancer/Wasp, old Longbow/Mantis), weaker than the scripts now in `scripts/`. All numbers are pooled over ship-runs of that model (mirror fights count both sides; "win" = that model's side wins). Modes: **default** (shipped 1.5-3 km starts, AI blocks
as in the baseline), **hybrid** (`--start-distance 1500:2400`, removes the 2.5 km search-range artefact), **locked** (`--face --start-distance 800:950 --commands "0.5:ai off;0.5:lock;0.5:hunt"`, isolates gunnery),
**scripts-only** (`--commands "0.5:ai off;0.5:hunt"`, also with `--face --start-distance 400:900`).

### X1. Both scripts were still missing the whole tracking/search stack of the other drones (the biggest effect)
v2.1 of both files carried only the round-1 H1-H6 patches. They lacked the later Harrier/Lancer fixes: `AccHold` (acceleration trusted 1 s), `Raw/RawMax` and the debris rule, the **hull-gap fix** in `MaintainLock`
(W2 of the Wasp/Lancer/Harrier section: stale-lock rays mostly go back to the predicted point, a share goes to the widening ellipse), `Stickiness/GhostTime/AiStaleTakeover` as settings, `Jink Interval` keys, other fleets'
`_TGT` feeds, and from the Hornet/Harrier v2.2 work (R2-R5 there): long confirm rays on both cameras that shorten after a fight, `AiRepick`, `AiHeldIgnored`, the HUNT search patrol, and the config keys that contain "=".
Ported with `patch` from the Hornet v1.8 -> v2.2 diff, the Lancer v2.1 -> v2.2 diff (per-drone patrol direction/radius) and a hand-fixed remainder (the Basilisk has no `AiRays` function, the Aegis has the escort code). Effect:

| set | model | win % before > after | lose % | timeout % | damage dealt / taken per ship |
|---|---|---|---|---|---|
| 1v1 default (14 scenarios x 30 seeds = 420 ship-runs) | Aegis | 40.5 > 71.2 | 23.3 > 19.5 | 35.5 > 7.4 | 6.4k > 11.1k / 2.5k > 2.5k |
| | Basilisk | 42.1 > 55.7 | 20.2 > 26.2 | 36.7 > 16.2 | 6.3k > 9.1k / 20.1k > 25.8k |
| 1v1 hybrid | Aegis | 61.9 > 75.5 | 33.1 > 22.4 | 1.4 > 0.7 | 10.1k > 12.5k / 3.7k > 2.9k |
| | Basilisk | 57.4 > 61.9 | 31.4 > 28.1 | 9.0 > 9.5 | 9.1k > 10.2k / 30.4k > 28.1k |
| 1v1 locked (gunnery only) | Aegis | 47.4 > 67.4 | 29.0 > 21.9 | 23.3 > 10.5 | 11.6k > 14.1k / 3.2k > 2.8k |
| | Basilisk | 52.1 > 54.8 | 29.8 > 27.6 | 16.7 > 16.4 | 8.3k > 9.2k / 27.9k > 27.2k |
| 1v2 + 1v3 (solo vs 2-3 enemies, 1,230 ship-runs each) | Aegis | 47.6 > 69.3 | 17.7 > 25.0 | 34.6 > 5.5 | 7.8k > 13.4k / 2.1k > 3.1k |
| | Basilisk | 44.2 > 64.6 | 16.0 > 21.3 | 39.6 > 13.7 | 4.5k > 6.8k / 15.7k > 23.0k |
| 2v3 + mixed + swarm (510) | Aegis | 32.0 > 53.9 | 28.0 > 37.3 | 40.0 > 8.0 | 9.7k > 15.8k / 3.7k > 5.1k |
| | Basilisk | 41.4 > 61.8 | 19.4 > 24.9 | 39.2 > 13.3 | 10.0k > 14.5k / 22.9k > 30.9k |
| pirate (180) | Aegis | 82.8 > 84.4 | 14.4 > 12.2 | 1.1 > 2.2 | |
| | Basilisk | 19.4 > 30.6 | 38.9 > 41.7 | 41.7 > 27.8 | |
Locked mode isolates the tracking port (W2/W3: Aegis +20 points, Basilisk +3: rockets against 35-40 m/s jinkers are limited by the lead, see X5); the default/hybrid difference is the patrol (R4 of the Hornet section) and the confirm-ray work.
Per opponent, 1v1 default, win % before > after (Aegis): Basilisk 36>66, Gnat 70>93, Harrier 30>56, Hornet 53>86, Lancer 20>53, Longbow L 33>93 (before 60 % timeouts), Longbow S 23>46, Mantis 16>66, Midge 33>76, Stinger 53>86, Wasp 60>86, Watcher 66>96.
(Basilisk): Aegis 43>23 (the opponent got much stronger), Gnat 66>86, Harrier 33>46, Hornet 40>63, Lancer 20>40, Longbow L 0>0, Longbow S 0>0, Mantis 70>86, Midge 66>73, Stinger 73>96, Wasp 46>83, Watcher 63>96.
Timeouts split (Basilisk, 154 > 65 of 420 ship-runs): nobody ever fought 121 > 26, I shot but nobody died 3 > 4, enemy shot at me and I never fired (Longbow S/L, X6) 23 > 35.
Aegis (140 > 28 timeouts): nobody ever fought 135 > 20 (the rest: I shot but nobody died 3 > 3, enemy shot and I never fired 2 > 1, I never shot and nobody did 0 > 4: Midge / Stinger / Mantis starts beyond the AI's reach).

### X2. AI confirm rays: parallax between the camera and the gun axis (new, Basilisk; transferable to every script with confirm rays)
In default mode the Basilisk sat in AI mode for 117-200 s of 300 against a passive Gnat / Watcher / Stinger at 1.4-2 km with the nose 0.02-0.05 deg on the target (trace) and **zero confirm hits in 178 rays**. The AI turns the
*guns* (in the sim the first gun's axis) onto the target; the cameras sit 1-3 m beside that axis, so the camera boresight passes 0.1-0.2 deg beside a 3 m target at 1.5 km (parallax 3 m / 1.5 km), and the spiral rings
(0.16 deg and out) hit only by chance. Fix (`ConvergePoint`, `IsCvgRay`, `AiRays`): 2 of every 8 confirm rays no longer go down the boresight but through a point on a gun axis (or on the centre line of all functional guns)
600 / 1200 / 2400 m ahead, cycling through the guns; a target anywhere on the AI's line of fire in 0.3 x range ... range is on the ray's path. Same code in the Aegis. Config `AI Confirm Convergence Rays`, `... Per 8` (2; 4 gives 54.2 against 55.0 %, no better).
Traces: Basilisk vs Gnat seeds 0-2 (timeout, lock never) now win at 190 / 82 / 194 s with 1 hit in 82-142 rays; vs Watcher 22 / 82 / 8 rays. Same build with and without the convergence rays, 1v1 default, Basilisk (non-mirror, 360 ship-runs): win 50.6 > 55.0 %, timeouts 21.7 > 17.2 %,
vs Gnat 66>86, Stinger 76>96, Watcher 70>96; hybrid unchanged (57.8 > 57.2, the AI is close enough there); the Aegis, which locks in 90 % of its runs anyway, 73.1 > 72.6 (all 420 ship-runs, noise). It is geometry, not budget: the rays cost the same.
It does not help against the Longbow S: there the rocket-leading AI nose is 2-7 deg off the target (394 rays, 0 hits; X6). Assumption about the real game: the AI blocks aim the weapons / the grid forward, not the camera;
convergence on the centre line of the guns covers both within a couple of metres.

### X3. Basilisk: the shipped blueprint overrode the tuned standoff (found by accident)
`tools/designs/extra.py:203` writes `Standoff Range=600` into the Basilisk PB's Custom Data. Saved Custom Data beats code defaults, so **in the game the Basilisk stood off at 600 m, not the 300 m the round-1 sweep chose**. The sim does not see
it: `BlueprintLoader` reads the `<CustomData>` element, the blueprints store the PB Custom Data in the mod-storage component, and runs with and without `Standoff Range=300` are bit-identical. Fix in the script: the key is now
`Standoff Range vs Small Grids` (a stale line in an old PB is ignored; `range` command and the other keys unchanged). **Needs (not my file):** delete `extra="Standoff Range=600\n"` in the Basilisk design. All other shipped Custom Data
lines (Aegis home/cue channels, Harrier `Target Priority Small Bonus=300`, Mantis `Engage Cap=650`, `Min Engage Range=250`) equal the script defaults.

### X4. Basilisk doctrine sweeps (1v1 hybrid, 12 opponents x 30 seeds = 360 ship-runs, +-2.6 points noise, one change at a time)
Standoff alone (build without convergence rays, base 300 m = 57.8 %): 450 m 55.8, 180 m 62.8; fire-window multiplier alone x2 60.8, x4 60.6. On the final build (base 57.2): window x2 + standoff 250 m 61.1, 180 m 64.7, 150 m (+ Min Range 100) 65.0;
window x3 + 180 m 65.0; + Salvo Pause 1.2 s 66.1 (not adopted: with a counted rocket stock it would waste them); orbit 25 + jink 10: 61.7, orbit 18 + jink 9: 61.4, orbit 0 + jink 0 (old build): 57.5 (all inside the noise). Adopted: `Standoff` 300 > **180 m**, and a new
`Fire Window Multiplier (unlimited rockets)` = **2** that applies only while the launchers report no stock (`_rockets < 0`, creative / empty inventories; with a counted stock the window stays 1, as in the Wasp section V2): +7.5 points together
(hybrid, non-mirror 57.2 > 64.7), locked pooled 52.9 > 54.8. Why the window: the aim error is 1-3 deg for most of a lock (one Basilisk-vs-Lancer duel, 2,484 locked ticks: < 0.3 deg 11 %, 0.3-1 deg 44 %, 1-3 deg 45 %), because the target's
35-40 m/s jinks move the 1.5 s rocket lead point by ~10 deg per jink; the 0.3 deg window of v2.1 let the gate open in 72 of those ticks ("not on target" 2,175, "target jinking" 177, "track too old" 60). Mean achieved turn rate in those duels was 0.10 of 0.18 rad/s commanded (one large gyro), so part of the error is actuator lag; it was not separated from the lead error.

### X5. Basilisk: wingmate collisions (priority 4)
Two Basilisks collided in the team set (65 ship-run collision events in 1,920 ship-runs, collision damage 35k). In the traces both are AI-flown (the AI blocks have no wingmate avoidance) and sit 15-30 m apart for 100 s. `Separation` now adds a close push
(`Wingmate Close Separation Range` 70 m, `... Speed` 22 m/s, on top of the 160 m / 12 m/s one), and `AiStep` takes the ship away from the AI blocks for 3 s whenever a wingmate is closer than **45 m** (`Take Over From AI When A
Wingmate Is Closer Than`, 0 = off) so the script can fly them apart (the no-target branch of `Control` then flees from the mates instead of flying home). Team set: collision events 65 > 6 (collision damage 35k > 17k; the 6 are two Basilisk-Basilisk contacts in 1v2 seed 21 while the script flew, and two Gnat rams), win in
the 2x/3x-Basilisk scenarios 69.7 > 69.2 (70 m / 5 s hold: 67.0, so the shorter hold is cheaper; push-only without the takeover: collisions unchanged at 0.04 per ship-run). Friendly fire went the other way: 8 > 22 ship-runs with friendly damage (1,980 > 7,120 hp over 1,920 ship-runs, about 0.02 % of the damage dealt), 90 hp per rocket.
Checked: a second flight-line test (rocket path along the launcher axis instead of to the aim point) changes nothing (identical runs), the hits come from AI-flown launchers (the sim AI has no friendly check, `ai_on` 30-138 s in those runs) and
from the wider window; not fixed.

### X6. Basilisk and the snipers; dash tried
Basilisk vs Longbow S: 80 % timeouts, vs Longbow L 43 %: the sniper holds 1,330 m (railgun standoff), the AI approaches at 10 m/s and its rocket-leading nose is 2-7 deg off the target (394 confirm rays, 0 hits), and with a lock the Longbow
flies 60-75 m/s, as fast as the Basilisk's 60 m/s cap. A **dash along the AI heading** (the Wasp V3 idea, ported: after 25 s of AI flight without a lock, no wingmate, few hits taken, 12 s at 60 m/s along the smoothed nose heading, AI off, rays keep
casting) gives Longbow S 0 > 20 % wins and in 1v1 default +3.6 / solo-vs-teams +3.5 points, **but** -3.1 points in hybrid fights and Lancer 43 > 16 %, Harrier 56 > 40 (gating by recent hits did not change that). Left in the file, **off**
(`Dash Along AI Heading`, default false), like in the Wasp.

### X7. Large and huge targets (Pirate)
`LargeTargetRadius` (25 m) treated the Longbow L (bounding radius 28-33 m), the Basilisk (24.6 m, so it flickers around the limit) and the Pirate (42 m) alike. Pirate duels (pirate/1x-3x, 180 runs per cell): Aegis stand-off 300 / 450 /
600 / 720 m: 63 / 78 / 76 / 85 % (fire from 780 m), Basilisk 450 / 600 / 650 / 700 / 750 m (fire from 800 m, 4.3 s of flight): 29 / 24 / 29 / 32 / 41 % (37 % in the final build). But 720 m cost the Aegis 17 points against the Longbow L (93 > 76 %): no turrets there,
closer is better. So a third class: **huge = Track.RawMax > 38 m** (`HugeTargetRadius`, `Huge()`): Aegis `StandoffHuge` 720 / `MaxFireRangeHuge` 780, Basilisk `StandoffHuge` 750 / `MaxFireRangeHuge` 800 / `MaxLeadTimeHuge` 4.3; the
large class keeps 600 / 700. Pirate: Aegis 82.8 > 84.4 % (the port alone gave 76.1, the huge class recovers it), Basilisk 19.4 > 30.6 %. Solo vs the Pirate: Aegis 16 > 40 %, Basilisk 10 > 13 %; groups of two: Aegis 90 > 83, Basilisk 13 > 30; three: Aegis 100 > 100, Basilisk 26 > 36 %.

### X8. Aegis: GUARD / escort, decoys, and what was flat
- **GUARD** (`PickTarget`): the contact filter `Distance(p, home) > GuardRadius` dropped every contact once the AI blocks had flown the ship away from its station (the stand-in has no tether), so a guarding Aegis never locked (8 % of ship-runs,
  nose on the enemy at 0.01-0.5 deg for 120 s, 452 shots, 4 % hits). A contact within 1.2 x `MaxFireRange` of my own guns now counts as a threat whatever the distance to the carrier. 7 opponents x 30 seeds (non-mirror), `--commands "0.5:guard"`: script lock
  8 > 43 % of the runs, win 31.4 > 34.8 % (v2.1 29.5), timeouts 36 % either way (81 of 210 ship-runs: nobody comes to a station that holds still). There is no carrier in any scenario, so the escort geometry (`EscortWant`, `Station`) itself is untested.
- **Decoys**: with `--decoys-functional false` the Aegis scores 79.2 instead of 81.1 % (hybrid, 360 ship-runs): two roof decoys are worth about +2 points in 1v1 because the enemy scripts aim from their own camera hits and ignore them. Only the AI stand-in
  and the Pirate's turrets pick decoy bodies first. Rolling the roof towards the enemy (the decoys would then be in the line of fire) was therefore not built.
- Flat inside the noise (hybrid, Aegis, 360 ship-runs, base 80.6): jink 35 + orbit 40 77.2; standoff 300 / 150: 81.7 / 82.2; `AI Takes Over After Stale Lock` 8 s + ghost 15 s: 82.2, 1.5 s: 81.9.
- Hit rate in lost fights is 2-3 % (136 shots, 3 hits at 540 m): the Aegis sits in AI mode flying 8 m/s while a Lancer shoots from 500 m; the first script lock decides these duels (Lancer 4-12 s, Aegis 18-70 s).

### X9. Robustness (1v1 default, 30 seeds x 14 opponents = 420 ship-runs, win % before > after)
| engine assumption | Aegis | Basilisk |
|---|---|---|
| default | 40.5 > 71.2 | 42.1 > 55.7 |
| `--thruster-torque true` | 39.0 > 48.1 (timeouts 36.7 > 22.9) | 50.0 > 67.4 |
| `--gyro-pitch-sign 1` (aim much worse) | 42.9 > 45.7 (timeouts 36.0 > 26.0, losses 18.1 > 25.0) | 36.4 > 40.5 |
| `--vanilla-search-range 3500` | 63.6 > 75.0 | 57.9 > 60.2 |
Scripts-only (`ai off; hunt`): Aegis 0 > 0 % wins (97 % timeouts), with `--face --start-distance 400:900` also 0 > 0 (no ray search finds a 5 m ship); Basilisk 4.5 > 4.3 and, near and facing, 78.6 > 79.3 (large target: ray search works).
No script exceptions in any run. CPU per `Main` call 0.0090 > 0.0111 ms (Aegis), 0.0088 > 0.0098 ms (Basilisk), median peak tick 1.0 / ~1 ms (sim wall clock; outliers up to 60 ms are thread scheduling).

### X10. Still weak
- Basilisk vs Longbow S/L (0 %), vs Lancer (40 %), Harrier (46 %), Aegis (23 %): rockets against 35-40 m/s jinkers; one large gyro; the sniper outruns it. Needs (blueprint): a second large gyro and more thrust, or a gatling battery that does not depend on rocket lead.
- Aegis vs Lancer 53 %, Harrier 56 %, Longbow S 46 %: duels decided by who gets the first script lock (Lancer 4-12 s after the start, Aegis 18-70 s with the AI approaching at 10 m/s).
- Scripts-only: nothing finds a small ship by rays (Aegis 0 wins). Anything that needs the AI blocks stays on the stand-in's assumptions (see X11).
- Unfair fights: a lone Aegis / Basilisk against 2-3 Hornets / Harriers / Lancers / Mantis / Longbow S still wins 0 % (before: 0 %, but 20-56 % timeouts; now 90-96 % losses because the fights happen), and 2xAegis vs 3xHornet is 0 / 100 (was 3 / 60). No doctrine
  change was made for focus fire, split or retreat: `Split Targets Across Hive` is flat (R5 of the Hornet section) and a retreat from equal-speed (98 m/s) gun ships does not break contact.
- Friendly fire of AI-flown Basilisk launchers (X5).

### X11. Assumptions about the real game
- The AI Offensive block turns the guns / grid forward (not the cameras) onto its target (X2); a block that is switched off and on searches again (R3 Hornet section); AI search every 30 s; AI flight speed limit 10 m/s (the sim exposes no AI-block property: raising it
  from the script, if the block has a speed-limit property in the game, would be the cheapest lever for the approach phase, not testable here).
- Saved Custom Data wins over code defaults (X3): old PBs keep their old keys; delete the `[Aegis]` / `[Basilisk]` sections when pasting v2.2 (new keys: `AI Confirm Convergence Rays (Per 8)`, `Wingmate Close Separation Range/Speed`, `Take Over From AI ...`,
  `Fire Window Multiplier (unlimited rockets)`, `Standoff Range vs Small Grids / Huge Grids`, `Max Fire Range vs Huge Grids`, `Max Flight Time vs Huge Grids`, `Dash ...`).
- Rocket launchers of a creative-world ship report no stock (`_rockets = -1`), so the x2 fire window applies; with loaded launchers the v2.1 window is kept.
- The Pirate's turret range is about 800 m (sim) and Longbow L has no turrets; a real turret ship of radius 28-38 m is treated as "large", not "huge".

### X12. Reproduce
Private copy `ab2-repo` = the baseline-state repo with only `AegisAI.cs` / `BasiliskAI.cs` replaced (`SIM_OUT=<own dir> bash sim/build.sh`, then `python3 -I tools/build_all.py --only Aegis` and `--only Basilisk` after every edit);
control copy = the v2.1 pair. Sets: 1v1 `^1v1/(Aegis|Basilisk)-vs-|^1v1/.*-vs-(Aegis|Basilisk)$` (default; `--start-distance 1500:2400` hybrid; the locked flags above), teams
`^(2v3|mixed|pirate|swarm|1v2|1v3)/.*(Aegis|Basilisk)` (2,580 runs), 2x/3x Basilisk collision set `^(2v3|pirate|1v2|1v3)/.*[23]xBasilisk`, Pirate `^pirate/.*(Aegis|Basilisk)-vs-Pirate`, GUARD `--commands "0.5:guard"`, sweeps with
`--custom-data "[Basilisk]\nIGC Tag=BASILISK\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=IDLE\nKey=value"` (keys must not contain "="), alternative physics `--thruster-torque true`, `--gyro-pitch-sign 1`, `--vanilla-search-range 3500`.
Pooling and per-opponent tables are 40 lines of Python over `runs.jsonl` (`seed_index < 30`, mirror scenarios counted from both sides). Debug builds (gate counters, confirm-ray counters, gyro command vs rate) lived in a separate copy.

## v2.1: first in-game report (2026-10-13): guard stood still, hunt blind

Report: Full drones in `guard` stood still; `hunt` drones waited ~20 s, all flew the same way and never saw a pirate ship 700 m away.

Cause: the `[AI]` blocks paste with AI Behavior off (MidgeNotes.md), so they never act; the scripts released thrusters and gyros to
them and skipped `AutoScanStep()` while `_aiOn`. HUNT's search patrol then flew its first leg along the drone's own Forward (the same
for drones pasted from one blueprint), nose along the flight path. Under fire the close scan's 480 m rays used the nose cameras and
kept their charge below the 1,200 m auto-scan.

Sim settings added: `--script-ai-blocks-inert` (AI blocks on script ships never act), `--ai-behavior-needs-switch` (they act only once
the bool terminal property `ActivateBehavior` is on; the id in the game is unverified, the scripts match any bool id containing "behavio").

| 3 Hornets vs pirate at 700 m, 40 seeds | before | after |
|---|---|---|
| hunt, AI inert: wins / runs where the pirate was found | 0 / 1 | 4 / 15 |
| guard, AI inert: wins / runs where the pirate was found | 0 / 1 | 5 / 16 |
| hunt / guard, AI working: wins | 28 / 23 | 30 / 24 |

All models (36 pirate scenarios x 20 seeds, drone-side wins of 720): inert hunt 23 -> 44, inert guard 24 -> 47, needs-switch 23 -> 211.

Fixes: AI GATE shared block (time-shares the AI blocks with the script's search until they have flown the ship once). Its first
version time-shared always and cut 1v1 decisive results from 90 % to 35 % with working AI blocks: the AI turns onto a far target
within a second and then only brakes, which a "speeds up / keeps turning" test missed; now a 25 deg heading change also counts, and a
proven AI is trusted. Side cameras cast 900 m rays with no target; the nose cameras stay with the auto-scan; hit direction from blocks
that lost integrity (within ~16 deg in a traced run) steers the sweep and the side cameras; patrol fan-out, sweep on legs, hold at
stops, patrol without AI blocks.

Regression check (all 91 1v1 pairs x 20 seeds, working AI blocks, measured before v2.2/v2.3 were merged): without the Watcher
77.6 % -> 77.4 % decisive (1,560 fights). Watcher fights 60.0 % -> 24.2 %: the Watcher (Hornet script, no guns, no AI blocks) used to
sit at home in `hunt` because the patrol needed AI blocks; now it patrols and an attacker rarely catches it in 300 s.
