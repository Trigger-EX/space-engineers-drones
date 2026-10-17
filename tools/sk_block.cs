// >>> SEEKER v1.0 (the [Seeker] turret reader, cut out of STAFF SEARCH, docs/design/staff_split.md) >>>
// A turret named [Seeker] on this grid is set to "Seeker Group" (PowerSystems / Weapons / Propulsion). Twice a second, when it holds the Flight's target and
// is aimed, the point it shows (the game's block position, else its line with the lead taken off) is raycast by a camera holding twice the ray length (the
// camera charge is shared with the Flight PB). The hull point hit (target frame) goes to WpSeek(): an own-seeker entry of the WEAKPOINT RELAY (ranked first,
// sent to the Flight on .WPT with flag 4096, shared with the wing on <ch>_WP). "seeker on" / "seeker off" (Staff word) switch it; Seek : line on screen.
// Hosts: GnatStaffAI, BasiliskStaffAI (WpSeek = the local WEAKPOINT RELAY) and AuxAI (the [Aux] PB of the 7 split Staffs: WpSeek sends .SKW to the Staff,
// whose AUX LINK calls its WpSeek). The other Staff scripts carry AUX LINK's SkBlocks/SkStep/SkStatus proxies instead.
// Hooks: SkBlocks() at the end of SsBlocks (AuxAI: GetBlocks and on a new seeker configuration); SkStep() in SsStep (AuxAI: Tick); SkStatus() first in SsStatus.
// Uses: _tgtId, _rc, _now, Me, SeekerOn, SeekerGroup, WpSeek().
List<IMyLargeTurretBase> _skT = new List<IMyLargeTurretBase>(); List<IMyCameraBlock> _skCams = new List<IMyCameraBlock>();
double _skT0 = -99; string _skLog = "";

void SkBlocks()
{
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_skCams, b => b.CubeGrid == g);
    foreach (var c in _skCams) c.EnableRaycast = true;
    _skT.Clear();
    if (SeekerOn) GridTerminalSystem.GetBlocksOfType(_skT, b => b.CubeGrid == g && b.CustomName.Contains("[Seeker]"));
    if (_skT.Count == 0) _skLog = ""; else if (_skLog == "") _skLog = "ready, group " + SeekerGroup;   // (re-scans keep the last report)
    foreach (var tu in _skT)
    {
        try { if (SeekerGroup.Length > 0 && tu.GetTargetingGroup() != SeekerGroup) tu.SetTargetingGroup(SeekerGroup); }
        catch (Exception x) { _skLog = "group not set: " + x.Message; }
    }
}

// the turret's view of the Flight's target -> a hull point in the target frame -> the WEAKPOINT RELAY (own-seeker entry)
void SkStep()
{
    if (!SeekerOn || _skT.Count == 0 || _tgtId == 0 || _now - _skT0 < 0.5) return;
    _skT0 = _now;
    foreach (var tu in _skT)
    {
        if (!tu.IsFunctional) { _skLog = "turret damaged"; continue; }
        if (!tu.HasTarget) { _skLog = "turret has no target (group " + tu.GetTargetingGroup() + ")"; continue; }
        var te = tu.GetTargetedEntity();
        if (te.EntityId != _tgtId) { _skLog = "turret on another target"; continue; }
        if (!tu.IsAimed) { _skLog = "turret turning"; continue; }
        Vector3D o = tu.GetPosition(), q;
        bool hp = te.HitPosition.HasValue && (te.HitPosition.Value - te.Position).LengthSquared() > 1;
        if (hp) q = te.HitPosition.Value;                  // the game told us the block itself
        else
        {
            Vector3D d; Vector3D.CreateFromAzimuthAndElevation(tu.Azimuth, tu.Elevation, out d);
            d = Vector3D.TransformNormal(d, tu.WorldMatrix);
            double D = Vector3D.Dot(te.Position - o, d);
            if (D < 5) { _skLog = "turret points away"; continue; }
            // turrets lead their target; take the lead off again (bullets inherit our velocity)
            Vector3D rv = new Vector3D(te.Velocity) - _rc.GetShipVelocities().LinearVelocity;
            string st = tu.BlockDefinition.SubtypeName;
            q = o + d * D - rv * (D / (st.Contains("Calibre") ? 500 : tu is IMyLargeMissileTurret ? 200 : 800));
        }
        IMyCameraBlock cam = null; double best = 0;
        foreach (var c in _skCams)
        {
            if (!c.IsWorking) continue;
            Vector3D cd0 = q - c.GetPosition(); double l0 = cd0.Length();
            if (l0 < 1 || Vector3D.Dot(cd0 / l0, c.WorldMatrix.Forward) < 0.72 || c.AvailableScanRange < 2 * (l0 + 30)) continue;   // the Flight PB shares the charge: twice the ray
            if (c.AvailableScanRange > best) { best = c.AvailableScanRange; cam = c; }
        }
        if (cam == null) { _skLog = "no camera charge/angle"; continue; }
        Vector3D cd = q - cam.GetPosition(); double cl = cd.Length(); cd /= cl;
        var e = cam.Raycast(q + cd * 30);
        if (e.IsEmpty() || e.EntityId != _tgtId || !e.HitPosition.HasValue) { _skLog = (hp ? "block pos given, " : "aim ray, ") + "ray missed hull"; continue; }
        MatrixD inv = MatrixD.Transpose(e.Orientation);
        double hint = hp ? (q - e.HitPosition.Value).Length() : -1;
        int k = SeekerGroup == "PowerSystems" ? 1 : SeekerGroup == "Weapons" ? 2 : SeekerGroup == "Propulsion" ? 3 : 4;
        WpSeek(new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(_tgtId, Vector3D.TransformNormal(e.HitPosition.Value - e.Position, inv),
            Vector3D.TransformNormal(cd, inv), new Vector3D(0, hint, 0), Me.CubeGrid.EntityId, k | 8 | (hp ? 16 : 0)));
        _skLog = (hp ? "block pos given" : "aim ray") + ", " + (q - e.HitPosition.Value).Length().ToString("0") + " m under hull";
        return;
    }
}
string SkStatus() { return _skT.Count > 0 ? "Seek   : " + _skLog + "\n" : ""; }
// <<< SEEKER v1.0 <<<
