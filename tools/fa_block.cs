// >>> FRIENDLY AVOIDANCE v1.0 (shared block: identical in every combat drone script, synced by tools/blocks_sync.py, see docs/friendly-avoidance.md) >>>
// Keeps the drone off every grid that is not an enemy: the carrier, the ships it escorts, other wings, neutral and unowned grids.
// Hive mates repel each other through Separation(), but only mates on the same IGC tag, and the cameras never target a friend, so a
// drone orbiting a target flew into a Ballista (other tag, not a mate, not a contact) without ever knowing it was there.
// Two sources fill the obstacle list _fa:
//   BEACON   every script with this block (and the Shrike Helm) broadcasts its grid's bounding sphere (centre, velocity, radius) on the
//            IGC channel DRONE_SPACE, 4 Hz; no relationship, tag or camera needed. Needs antennas in range, as every IGC message does.
//   SIGHTING any non-enemy grid that Valid() sees (sensors, cameras, designators, omni scan) is remembered with its position, velocity and
//            hull radius for 12 s, so grids without a beacon (a neutral hulk, another player's ship) are avoided too.
// FaWant() then bends the velocity the drone is about to command, per obstacle (sphere = grid radius + my radius + 15 m margin):
//   1 BARRIER   closing speed on the sphere is capped at sqrt(2 x 4 m/s2 x gap), so the drone can always stop in time
//   2 RAMP      inside margin + 40 m a radial push away from it (up to 50 m/s, more inside the margin)
//   3 SIDESTEP  if the straight line (actual or commanded velocity) passes inside the margin within 8 s, a push sideways
//   ROCKS    asteroids seen by a camera ray (the scans all go through Valid()) are kept as 40 m spheres on the hit point for 60 s, so the same
//            barrier / ramp / sidestep keep the drone off them. Voxels give no hull radius and sensors give no hit point: only rays count.
// Ignored: RTB and IDLE (docking, parked), and any obstacle whose sphere I am inside (launching from a bay, merged, docked).
// No Custom Data keys (the 100,000-character limit); constants are below. Registers its listener on the first call.
// Script hooks: FaSee(e) first line of Valid(); want = FaWant(want, me) right after the KzWant line (the speed cap comes after).
// Enemies are skipped: every contact (_contacts holds only valid targets, my target included), since IGC does not tell friend from foe.
// Uses from the script: _rc, _now, _mode, _contacts, Valid(), Mode.RTB / Mode.IDLE.
class FaO { public Vector3D P, V; public double R, T; public bool B, K; }
Dictionary<long, FaO> _fa = new Dictionary<long, FaO>(); IMyBroadcastListener _faL; double _faT, _faR = 6;
FaO FaGet(long id) { FaO o; if (!_fa.TryGetValue(id, out o)) _fa[id] = o = new FaO(); return o; }
void FaSee(MyDetectedEntityInfo e)
{
    if (e.Type == MyDetectedEntityType.Asteroid)   // rock: only a camera ray gives a point on it; one 40 m sphere per 40 m cell, kept 60 s
    {
        if (e.HitPosition == null || _fa.Count > 150) return;
        Vector3D h = e.HitPosition.Value; FaO a = FaGet(((long)(h.X / 40) << 40) ^ ((long)(h.Y / 40) << 20) ^ (long)(h.Z / 40));
        a.P = h; a.V = Vector3D.Zero; a.R = 40; a.T = _now; a.B = false; a.K = true; return;
    }
    if (e.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || (e.Type != MyDetectedEntityType.LargeGrid && e.Type != MyDetectedEntityType.SmallGrid) || e.EntityId == Me.CubeGrid.EntityId) return;
    FaO o = FaGet(e.EntityId);
    if (o.B && _now - o.T < 1) return;   // (a fresh beacon fix beats a sighting)
    o.P = e.Position; o.V = e.Velocity; o.R = Math.Max(e.BoundingBox.Size.Length() * 0.5, 2); o.T = _now; o.B = false;
}
Vector3D FaWant(Vector3D want, Vector3D me)
{
    if (_faL == null) _faL = IGC.RegisterBroadcastListener("DRONE_SPACE");
    while (_faL.HasPendingMessage)
    {
        var m = _faL.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, float>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, float>)m.Data;
        if (d.Item1 == Me.CubeGrid.EntityId) continue;
        FaO q = FaGet(d.Item1);
        q.P = d.Item2; q.V = d.Item3; q.R = d.Item4; q.T = _now; q.B = true;
    }
    Vector3D mv = _rc.GetShipVelocities().LinearVelocity, push = Vector3D.Zero;
    if (_now - _faT >= 0.25)
    {
        _faT = _now; var v = Me.CubeGrid.WorldVolume; _faR = v.Radius;
        IGC.SendBroadcastMessage("DRONE_SPACE", new MyTuple<long, Vector3D, Vector3D, float>(Me.CubeGrid.EntityId, v.Center, mv, (float)v.Radius));
        var gone = new List<long>(); foreach (var kv in _fa) if (_now - kv.Value.T > (kv.Value.B ? 3 : kv.Value.K ? 60 : 12)) gone.Add(kv.Key);
        foreach (var id in gone) _fa.Remove(id);
    }
    if (_mode == Mode.RTB || _mode == Mode.IDLE) return want;
    foreach (var kv in _fa)
    {
        var o = kv.Value;
        if (_contacts.ContainsKey(kv.Key)) continue;   // an enemy's beacon (or my target's): the fight is meant to close in on it
        Vector3D rel = me - (o.P + o.V * Math.Min(_now - o.T, 3));
        double dist = rel.Length(), r = o.R + _faR, inner = r + 15;
        if ((dist < r && !o.K) || dist < 1) continue;   // inside a ship's bounding sphere: launching, merged or docked (a rock's sphere is only a hit point's margin: still avoid it)
        Vector3D n = rel / dist;
        double cl = -Vector3D.Dot(want - o.V, n), lim = Math.Sqrt(8 * Math.Max(dist - inner, 0));
        if (cl > lim) want += n * (cl - lim);
        if (dist < inner + 40) push += n * 50 * Math.Min(1.5, (inner + 40 - dist) / 40 + (dist < inner ? 0.5 : 0));
        for (int k = 0; k < 2; k++)
        {
            Vector3D w = (k == 0 ? mv : want) - o.V; double ww = w.LengthSquared(), t = -Vector3D.Dot(rel, w) / Math.Max(ww, 1);
            if (ww < 4 || t <= 0 || t > 8) continue;
            Vector3D miss = rel + w * t; double ml = miss.Length();
            if (ml < inner) push += (ml > 1 ? miss / ml : Vector3D.Up) * 50 * (1 - t / 8) * (1 - ml / inner);
        }
    }
    return want + push;
}
// <<< FRIENDLY AVOIDANCE v1.0 <<<
