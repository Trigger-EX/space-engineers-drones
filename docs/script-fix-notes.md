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
