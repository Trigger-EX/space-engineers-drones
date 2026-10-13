// >>> BREACH + SEEKER v1.0 (shared block: identical in every armed drone script, kept in sync by tools/aim_sync.py) >>>
// BREACH (large ships): spreading hits over a big hull chips thousands of armour blocks and destroys none (sim vs a 13,666-block
// warship). BREACH holds ONE hull point and keeps every round on it: the known hull point nearest the target's centre (on a long
// ship that is mid-deck or mid-belly, where the armour over the core is thinnest). Probe rays at it that come back deeper show the
// hole; the drone then aims down the hole, towards the centre. No progress for "Breach Stall (s)", or two probe rays in a row that
// find nothing there: that point is dropped and the next one is used. AUTO uses BREACH on large grids of radius >= Breach Min
// Radius; "aim breach" forces it, "breach off" turns the AUTO switch off.
// SEEKER (in-game test of "let the game find the vitals"): a turret on this drone with [Seeker] in its name is set to the targeting
// group "Seeker Group" (PowerSystems / Weapons / Propulsion; vanilla turrets and Custom Turret Controllers aim at the blocks of
// that group). Twice a second the script reads where that turret points (lead removed), raycasts that line with its cameras, and
// the hull point hit - right over the subsystem - becomes the breach point; the hole is then drilled along the turret's line.
// What the turret reports is shown on the status screen ("Seek:" line), so an in-game test shows whether it works.
// Decoys pull turrets (and the seeker) onto themselves first; static guns are never distracted. "seeker off" ignores the turret.
// The turret must reach the target: gatling / rocket turrets about 800 m, assault cannon 1400 m, artillery 2000 m.
bool BreachAuto = true; double BreachMinRadius = 25, BreachStall = 30, BreachProbe = 30;
string SeekerGroup = "PowerSystems"; bool SeekerOn = true;
long _bId; bool _bOn, _bSeek; Vector3D _bS, _bDir; double _bDepth, _bProgT; int _bMiss;
List<Vector3D> _bBad = new List<Vector3D>();
List<IMyLargeTurretBase> _skT = new List<IMyLargeTurretBase>();
bool _skOk; long _skId; Vector3D _skS, _skDir; double _skT0 = -99; string _skLog = "";

bool BreachMode(Track t)
{
    if (AimMode == "BREACH") return true;
    return AimMode == "AUTO" && BreachAuto && t.Type == MyDetectedEntityType.LargeGrid && t.Radius >= BreachMinRadius;
}
bool BreachBad(Vector3D h) { foreach (var q in _bBad) if ((q - h).LengthSquared() < 36) return true; return false; }
void BreachSet(Vector3D s, Vector3D dir, bool seek)
{
    _bS = s; _bDir = dir; _bSeek = seek; _bOn = true; _bDepth = 0; _bProgT = _now; _bMiss = 0;
}
Vector3D BreachOffset(Track t)
{
    if (_bId != t.Id) { _bId = t.Id; _bOn = false; _bBad.Clear(); _skOk = false; }
    SeekerTick(t);
    if (_bOn && _now - _bProgT > BreachStall) BreachDrop();
    // a seeker point replaces a plain breach point at once, and an old seeker point once its hole has stalled for 6 s
    if (_skOk && _skId == t.Id && !BreachBad(_skS) && (!_bOn || !_bSeek || ((_skS - _bS).LengthSquared() > 64 && _now - _bProgT > 6)))
    { BreachSet(_skS, _skDir, true); _skOk = false; }
    if (!_bOn)
    {
        double best = double.MaxValue; Vector3D s = t.Hull[0];
        foreach (var h in t.Hull) { if (BreachBad(h)) continue; double d = h.LengthSquared(); if (d < best) { best = d; s = h; } }
        if (best == double.MaxValue) _bBad.Clear();
        double l = s.Length();
        BreachSet(s, l > 1e-3 ? -s / l : Vector3D.Zero, false);
    }
    Vector3D a = _bS + _bDir * _bDepth;                    // down the hole
    t.AimLocal = a;
    return Vector3D.TransformNormal(a, t.O);
}
// a probe ray at the breach point hit the target: how far past the surface point did it get?
void BreachHit(Track t, MyDetectedEntityInfo e, Vector3D aim)
{
    if (!_bOn || _bId != t.Id || !e.HitPosition.HasValue || !AimActive(t) || !BreachMode(t)) return;
    _bMiss = 0;
    Vector3D rd = aim - Me.GetPosition();
    if (rd.LengthSquared() < 1) return;
    rd.Normalize();
    double d = Vector3D.Dot(e.HitPosition.Value - (e.Position + Vector3D.TransformNormal(_bS, e.Orientation)), rd);
    if (d > _bDepth + 0.5) { _bDepth = Math.Min(d, 60); _bProgT = _now; }
}
void BreachMiss() { if (++_bMiss >= 2) BreachDrop(); }
void BreachDrop()
{
    if (_bOn) { _bBad.Add(_bS); if (_bBad.Count > 12) _bBad.RemoveAt(0); }
    _bOn = false; _bSeek = false; _bMiss = 0;
}
string AimInfo()
{
    string s = "";
    if (_bOn && _tgt != null && _bId == _tgt.Id && BreachMode(_tgt)) s += " hole " + _bDepth.ToString("0") + " m" + (_bSeek ? " (seeker)" : "");
    if (_skT.Count > 0) s += "\nSeek: " + _skLog;
    return s;
}
// seeker turrets: [Seeker] in the name, on this grid; set to the targeting group (needs the 1.200+ turret API)
void SeekerSetup()
{
    _skT.Clear();
    if (!SeekerOn) { _skLog = ""; return; }
    GridTerminalSystem.GetBlocksOfType(_skT, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("[Seeker]"));
    if (_skT.Count == 0) _skLog = ""; else if (_skLog == "") _skLog = "ready, group " + SeekerGroup;   // (re-scans keep the last report)
    foreach (var tu in _skT)
    {
        try { if (SeekerGroup.Length > 0 && tu.GetTargetingGroup() != SeekerGroup) tu.SetTargetingGroup(SeekerGroup); }
        catch (Exception x) { _skLog = "group not set: " + x.Message; }
    }
}
void SeekerTick(Track t)
{
    if (!SeekerOn || _skT.Count == 0 || _now - _skT0 < 0.5) return;
    _skT0 = _now;
    foreach (var tu in _skT)
    {
        if (!tu.IsFunctional) { _skLog = "turret damaged"; continue; }
        if (!tu.HasTarget) { _skLog = "turret has no target (group " + tu.GetTargetingGroup() + ")"; continue; }
        var te = tu.GetTargetedEntity();
        if (te.EntityId != t.Id) { _skLog = "turret on another target"; continue; }
        if (!tu.IsAimed) { _skLog = "turret turning"; continue; }
        Vector3D o = tu.GetPosition(), q;
        bool hp = te.HitPosition.HasValue && (te.HitPosition.Value - te.Position).LengthSquared() > 1;
        if (hp) q = te.HitPosition.Value;                  // the game told us the block itself
        else
        {
            Vector3D d; Vector3D.CreateFromAzimuthAndElevation(tu.Azimuth, tu.Elevation, out d);
            d = Vector3D.TransformNormal(d, tu.WorldMatrix);
            double D = Vector3D.Dot(t.P(_now) - o, d);
            if (D < 5) { _skLog = "turret points away"; continue; }
            // turrets lead their target; take the lead off again (bullets inherit our velocity)
            Vector3D rv = t.V(_now) - _rc.GetShipVelocities().LinearVelocity;
            q = o + d * D - rv * (D / SeekerShotSpeed(tu));
        }
        var cam = CamFor(q, 0);
        if (cam == null) { _skLog = "no camera charge/angle"; continue; }
        Vector3D cd = q - cam.GetPosition(); double cl = cd.Length(); if (cl < 1) continue; cd /= cl;
        var e = cam.Raycast(q + cd * BreachProbe);
        if (e.IsEmpty() || e.EntityId != t.Id || !e.HitPosition.HasValue)
        { _skLog = (hp ? "block pos given, " : "aim ray, ") + "ray missed hull"; continue; }
        MatrixD inv = MatrixD.Transpose(e.Orientation);
        _skS = Vector3D.TransformNormal(e.HitPosition.Value - e.Position, inv);
        _skDir = Vector3D.TransformNormal(cd, inv);
        _skOk = true; _skId = t.Id;
        _skLog = (hp ? "block pos given" : "aim ray") + ", " + (q - e.HitPosition.Value).Length().ToString("0") + " m under hull";
        return;
    }
}
// muzzle speed of the turret's ammunition (m/s), to take its lead off again
double SeekerShotSpeed(IMyLargeTurretBase tu)
{
    string st = tu.BlockDefinition.SubtypeName;
    if (st.Contains("Calibre")) return 500;               // artillery / assault cannon shells
    if (tu is IMyLargeMissileTurret) return 200;          // rockets (mean speed over a few hundred metres)
    return 800;                                            // gatling, autocannon, interior turret
}
// <<< BREACH + SEEKER <<<
