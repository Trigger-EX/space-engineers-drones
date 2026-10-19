// >>> SEARCH v1.0 (camera and sensor search for the Flight PB, run in the [Aux] PB; tools/sc_block.cs, synced by tools/blocks_sync.py) >>>
// Camera and sensor search moves Flight -> Aux (docs/design/flight_split.md sections 2, 3, 4.1, 6.3, 6.6). Final host: AuxAI (model-independent).
// Content: auto-scan, omni-scan (all-round close scan), hit-look, the manual lock ray, sensor poll, KZ rays from the .KZR table, own CamFor / Forwardish.
// The code is the Flight's AutoScanStep / OmniScan / HitPoint / ManualLock / PollSensors / KzRayStep; every model difference comes in .SCS (p, flags).
// Finds NEW contacts only; the tracked target (MaintainLock, designators, Track, Lead) stays in the Flight. Never writes an override. Valid(), Friendly()
// and AddContact() stay in the Flight: this block sends every non-empty hit except its own grid and planets (friendlies and asteroids feed FaSee).
// Channels (TransmissionDistance.CurrentConstruct, key K = "WSPL"+<Flight PB EntityId>):
//   .SCS  Flight -> Aux   MyTuple<int flags, long tgtId, Vector3D lostPos, Vector3D lostVel, Vector3D hitDir, MyTuple<Vector3D,Vector3D,Vector3D,Vector3D> p>
//         Every 6 ticks + at once on a flag change. Stale at the Aux after 30 ticks: the Aux stops all scanning.
//         flags: bit0 AUTO (no target, AI off, AutoScan on, mode GUARD/HUNT/FOLLOW), bit1 OMNI (mode not IDLE and CloseScan on), bit2 HARD (UnderFire or
//         Alerted), bit3 has target, bit4 AI on, bit5 SENSORS, bit6 HITLOOK (off for the Gnat variant), bit7 KZRAYS, bits 8-15 lock request counter,
//         bit17 lock camera uses Forwardish (0.9) instead of 0.99.
//         p: a = (auto-scan range, scan cone in degrees, spiral N); b = (close-scan range, 900, lock range); c = (lost age, hit-dir age, lost radius);
//         d = (KzPack, KzReserve, 0). The model differences (ScanRange vs WeaponRange x 1.1, N 120/150, Gnat without HitPoint, lock rule and range)
//         are carried in p and the flags.
//         lostPos / lostVel are the Flight's raw _lostPos / _lostVel, c.X = _now - _lostT and c.Y = _now - _hitDirT at sending (never hit / lost: any
//         age >= 25); this block adds the time since the message arrived.
//         Lock: the ray is cast in the tick a counter change arrives. The first .SCS after a silence of more than 30 ticks (Aux start, Flight rebuilt)
//         only learns the counter.
//   .KZR  Flight -> Aux   MyTuple<long id, Vector3D pos, Vector3D vel, double radius, double age, int flags>: bit0 is the Flight's target, bits 8-11 index,
//         12-15 count, 16-31 pass number. From the KzRayStep() proxy, <= 4 per KZ pass (6 Hz), only while threats exist.
//         pos = T.P(_now) and vel = T.Vel at sending, age = _now - T.Seen. A new pass number replaces the table; the rays of KZ RAYS run once per pass
//         (refresh the stalest track that is not bit0, every 2nd pass a pack-search ray round entry (pass/2) % n), pos extrapolated by vel.
//   .SCN  Aux -> Flight   MyTuple<long id, int f, Vector3D hit, Vector3D vel, MyTuple<Vector3D,Vector3D> box, MyTuple<Vector3D,Vector3D> ori(fwd, up)>:
//         f bits 0-7 entity type, 8-15 relationship, 16 has hit, 20-22 source (1 SCAN, 2 SENSOR, 3 KZ, 4 LOCK), 24-27 lock result (0 ok, 1 no forward
//         camera, 2 nothing hit with the charge in hit.X). id 0 = heartbeat. Per non-empty hit, except own grid and planets; friendlies and asteroids
//         are sent because FaSee uses them. About <= 6 per tick. Heartbeat every 30 ticks.
//         Heartbeat: id 0, f 0. A failed lock: id -1, f = 4 << 20 | result << 24 (1 no forward camera, 2 = the ray hit nothing, an asteroid, a planet or
//         its own grid; charge in hit.X). A lock that hit something comes as a normal hit with source 4 and result 0 (Friendly() stays in the Flight).
// Sync: scans only while .SCS is <= 30 ticks old. Aux rebuilt: re-binds to the nearest [Flight] (existing rule), resumes on the next .SCS (<= 6 ticks).
//   Flight rebuilt: new key; the Aux re-scans at 1 Hz while stale. Aux run gap: drops .KZR entries older than 0.5 s (the whole queue of the gap tick).
// Hooks: ScBlocks() in GetBlocks (cameras, sensors); ScStep() every tick (after the key _fk is known); ScStatus() in the status text.
// Uses from the script: _fk, _tick, _now, _rc, Me.
List<IMyCameraBlock> _cams = new List<IMyCameraBlock>(); List<IMySensorBlock> _scSen = new List<IMySensorBlock>();
List<MyDetectedEntityInfo> _sensed = new List<MyDetectedEntityInfo>();
List<MyTuple<long, Vector3D, Vector3D, double, double, int>> _kzT = new List<MyTuple<long, Vector3D, Vector3D, double, double, int>>();
IMyBroadcastListener _scL, _scKL; string _scKey = null, _scSt = "";
long _scTk = -99, _scTgt; int _scF, _scLk, _scN, _scHits, _scHitN, _kzPass = -1, _kzRay, _scanIdx, _omniCam, _omniIdx, _hitIdx;
double _scT, _kzRx; bool _kzNew;
Vector3D _scLp, _scLv, _scHd; MyTuple<Vector3D, Vector3D, Vector3D, Vector3D> _scP;

void ScBlocks()
{
    GridTerminalSystem.GetBlocksOfType(_cams, b => b.IsSameConstructAs(Me));
    foreach (var c in _cams) c.EnableRaycast = true;
    GridTerminalSystem.GetBlocksOfType(_scSen, b => b.IsSameConstructAs(Me));
}
bool ScOn { get { return _scN > 0 && _tick - _scTk <= 30; } }
void ScStep()
{
    if (_fk != _scKey)
    {
        if (_scL != null) IGC.DisableBroadcastListener(_scL);
        if (_scKL != null) IGC.DisableBroadcastListener(_scKL);
        _scKey = _fk; _scN = 0; _kzT.Clear(); _kzPass = -1;
        _scL = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".SCS") : null;
        _scKL = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".KZR") : null;
    }
    bool gap = Runtime.TimeSinceLastRun.TotalSeconds > 0.5, lk = false;
    while (_scL != null && _scL.HasPendingMessage)
    {
        var m = _scL.AcceptMessage();
        if (!(m.Data is MyTuple<int, long, Vector3D, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>>)) continue;
        var d = (MyTuple<int, long, Vector3D, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>>)m.Data;
        int c = d.Item1 >> 8 & 255;
        if (ScOn && c != _scLk) lk = true;                // (after a silence the counter is only learnt: no lock from a stale request)
        _scLk = c; _scF = d.Item1; _scTgt = d.Item2; _scLp = d.Item3; _scLv = d.Item4; _scHd = d.Item5; _scP = d.Item6;
        _scT = _now; _scTk = _tick; _scN++;
    }
    while (_scKL != null && _scKL.HasPendingMessage)
    {
        var m = _scKL.AcceptMessage();
        if (gap || !(m.Data is MyTuple<long, Vector3D, Vector3D, double, double, int>)) continue;   // run gap: the queue is old
        var d = (MyTuple<long, Vector3D, Vector3D, double, double, int>)m.Data;
        int ps = d.Item6 >> 16 & 65535;
        if (ps != _kzPass) { _kzPass = ps; _kzT.Clear(); _kzNew = true; }
        if (_kzT.Count < 16) _kzT.Add(d); _kzRx = _now;
    }
    if (_fk != "" && _tick % 30 == 17) ScTx(0, 0, Vector3D.Zero, Vector3D.Zero, new MyTuple<Vector3D, Vector3D>(), new MyTuple<Vector3D, Vector3D>());   // heartbeat
    if (_tick % 30 == 0) { _scHitN = _scHits; _scHits = 0; _scSt = ScLine(); }
    bool on = ScOn && _rc != null && _rc.IsFunctional;
    if (on && lk) ManualLock();
    if (!on) { _kzNew = false; return; }
    if ((_scF & 32) != 0 && _tick % 10 == 5) PollSensors();
    if (_kzNew && (_scF & 128) != 0 && _kzT.Count > 0 && _now - _kzRx < 0.5) KzRays();
    _kzNew = false;
    if (Runtime.CurrentInstructionCount > Runtime.MaxInstructionCount * 0.6) return;   // the optional scans wait for the next tick
    if ((_scF & 2) != 0) OmniScan();
    if ((_scF & 1) != 0) AutoScanStep();
}
void ScTx(long id, int f, Vector3D hit, Vector3D vel, MyTuple<Vector3D, Vector3D> box, MyTuple<Vector3D, Vector3D> ori)
{
    IGC.SendBroadcastMessage(_fk + ".SCN", new MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>>(id, f, hit, vel, box, ori),
        TransmissionDistance.CurrentConstruct);
}
// one hit -> .SCN (src 1 SCAN, 2 SENSOR, 3 KZ, 4 LOCK); false for nothing, own grid and planets
bool ScSend(MyDetectedEntityInfo e, int src)
{
    if (_fk == "" || e.IsEmpty() || e.EntityId == Me.CubeGrid.EntityId || e.Type == MyDetectedEntityType.Planet) return false;
    var b = e.BoundingBox;
    ScTx(e.EntityId, ((int)e.Type & 255) | ((int)e.Relationship & 255) << 8 | (e.HitPosition.HasValue ? 65536 : 0) | src << 20, e.HitPosition ?? Vector3D.Zero,
        new Vector3D(e.Velocity), new MyTuple<Vector3D, Vector3D>(b.Min, b.Max), new MyTuple<Vector3D, Vector3D>(e.Orientation.Forward, e.Orientation.Up));
    _scHits++;
    return true;
}
bool Forwardish(IMyCameraBlock c) { return Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) > 0.9; }
IMyCameraBlock CamFor(Vector3D pos, double range)
{
    IMyCameraBlock best = null; double bestRange = 0;
    foreach (var c in _cams)
    {
        if (!c.IsFunctional || !c.IsWorking) continue;
        Vector3D d = pos - c.GetPosition();
        double len = d.Length();
        if (len < 1) continue;
        if (Vector3D.Dot(d / len, c.WorldMatrix.Forward) < 0.72) continue; // ~44 deg cone
        if (c.AvailableScanRange < Math.Max(range, len)) continue;   // a ray costs its length in charge; range 0 = "just to the point"
        if (c.AvailableScanRange > bestRange) { best = c; bestRange = c.AvailableScanRange; }
    }
    return best;
}

// "lock": a ray straight ahead from the forward camera with the most charge (Friendly() and the new target are the Flight's)
void ManualLock()
{
    double fw = (_scF & 131072) != 0 ? 0.9 : 0.99, rng = _scP.Item2.Z > 0 ? _scP.Item2.Z : 2500;
    IMyCameraBlock cam = null;
    foreach (var c in _cams)
        if (c.IsWorking && Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) > fw && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    var z = new MyTuple<Vector3D, Vector3D>();
    if (cam == null) { ScTx(-1, 4 << 20 | 1 << 24, Vector3D.Zero, Vector3D.Zero, z, z); return; }
    var e = cam.Raycast(Math.Min(rng, cam.AvailableScanRange));
    if (e.Type == MyDetectedEntityType.Asteroid || !ScSend(e, 4)) ScTx(-1, 4 << 20 | 2 << 24, new Vector3D(cam.AvailableScanRange, 0, 0), Vector3D.Zero, z, z);
}

// auto-scan: a golden-angle spiral over the scan cone from the forward camera with the most charge; just lost a target: every other ray round its last place
void AutoScanStep()
{
    double range = _scP.Item1.X, lostAge = _scP.Item3.X + _now - _scT;
    IMyCameraBlock cam = null;
    foreach (var c in _cams)
        if (c.IsWorking && Forwardish(c) && c.AvailableScanRange >= range && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    if (cam == null) return;
    _scanIdx++;
    if (lostAge < 25 && (_scanIdx & 1) == 0)
    {
        Vector3D lp = _scLp + _scLv * Math.Min(lostAge, 10);
        double rr = Math.Min(250, _scP.Item3.Z + 6 * lostAge) * Math.Sqrt(((_scanIdx >> 1) % 23 + 0.5) / 23.0), th2 = _scanIdx * 2.39996323;
        Vector3D dl = lp - cam.GetPosition(); double dd = dl.Length();
        if (dd > 30 && dd < range)
        {
            dl /= dd;
            Vector3D uu = Vector3D.Cross(dl, Math.Abs(dl.Y) < 0.95 ? Vector3D.Up : Vector3D.Right); uu.Normalize();
            Vector3D vv = Vector3D.Cross(dl, uu);
            ScSend(cam.Raycast(lp + uu * (rr * Math.Cos(th2)) + vv * (rr * Math.Sin(th2)) + dl * 10), 1);
            return;
        }
    }
    int N = _scP.Item1.Z >= 1 ? (int)_scP.Item1.Z : 120;
    double r = _scP.Item1.Y * Math.Sqrt((_scanIdx % N + 0.5) / N), th = _scanIdx * 2.39996323; // golden-angle spiral
    ScSend(cam.Raycast(range, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th))), 1);
}

void PollSensors()
{
    foreach (var s in _scSen)
    {
        if (!s.IsWorking) continue;
        _sensed.Clear();
        s.DetectedEntities(_sensed);
        foreach (var e in _sensed) ScSend(e, 2);
    }
}

// close scan: cameras that don't face forward fire short rays in a spiral over their whole view cone (3 a tick, as their charge allows).
// Under fire or with nothing to track they reach max(4 x close range, hard range); the forward cameras join only under fire with no target, AI off and no
// auto-scan (AUTO clear), so they keep their charge for the long auto-scan, the AI confirm rays and a lock.
void OmniScan()
{
    if (_cams.Count == 0) return;
    bool hard = (_scF & 4) != 0, free = (_scF & 8) == 0;
    double rng = hard || free ? Math.Max(_scP.Item2.X * 4, _scP.Item2.Y) : _scP.Item2.X, hAge = _scP.Item3.Y + _now - _scT;
    bool look = (_scF & 64) != 0 && hard && free && hAge < 10 && _scHd.LengthSquared() > 0.5, fw = hard && free && (_scF & 17) == 0;
    for (int shots = 0, tries = 0; shots < 3 && tries < _cams.Count; tries++)
    {
        var c = _cams[_omniCam++ % _cams.Count];
        if (!c.IsWorking || (Forwardish(c) && !fw) || c.AvailableScanRange < rng * 2) continue; // keep charge for tracking
        _omniIdx++;
        MyDetectedEntityInfo e; Vector3D hp;
        if (look && HitPoint(c, rng, out hp)) e = c.Raycast(hp);   // just hit: look where the shots came from
        else
        {
            double r = 40 * Math.Sqrt((_omniIdx % 89 + 0.5) / 89.0), th = _omniIdx * 2.39996323;
            e = c.Raycast(rng, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
        }
        ScSend(e, 1);
        shots++;
    }
}
// A point rng metres out, inside 35 deg of the side that was hit (golden-angle spiral), that camera c can see; false if none of 4 tries is in its cone.
bool HitPoint(IMyCameraBlock c, double rng, out Vector3D pt)
{
    Vector3D a = Vector3D.Cross(_scHd, Math.Abs(_scHd.Y) < 0.9 ? Vector3D.Up : Vector3D.Right); a.Normalize();
    Vector3D b = Vector3D.Cross(_scHd, a), cp = c.GetPosition();
    for (int k = 0; k < 4; k++)
    {
        _hitIdx++;
        double r = Math.Tan(0.61 * Math.Sqrt((_hitIdx % 97 + 0.5) / 97.0)), th = _hitIdx * 2.39996323;
        Vector3D d = Vector3D.Normalize(_scHd + (a * Math.Cos(th) + b * Math.Sin(th)) * r);
        pt = cp + d * rng;
        if (Vector3D.Dot(d, c.WorldMatrix.Forward) > 0.72) return true;
    }
    pt = Vector3D.Zero; return false;
}

// KZ RAYS on the .KZR table, once per KZ pass: one ray refreshes the stalest incoming track that is not the Flight's target, one (every 2nd pass)
// searches round a known kamikaze for the rest of its pack. A camera is used only with KzReserve metres of charge left over.
void KzRays()
{
    Vector3D me = _rc.GetPosition();
    double dt = _now - _kzRx, pk = _scP.Item4.X, res = _scP.Item4.Y;
    int si = -1, n = _kzT.Count;
    for (int i = 0; i < n; i++) { var k = _kzT[i]; if ((k.Item6 & 1) == 0 && k.Item5 + dt > 0.25 && (si < 0 || k.Item5 > _kzT[si].Item5)) si = i; }
    if (si >= 0)
    {
        var k = _kzT[si];
        Vector3D p = k.Item2 + k.Item3 * dt, d = p - me; double len = d.Length();
        if (len > 1)
        {
            Vector3D aim = p + d / len * Math.Min(k.Item4 + 2, 8);
            var c = CamFor(aim, len + res);
            if (c != null) ScSend(c.Raycast(aim), 3);
        }
    }
    if ((++_kzRay & 1) == 1) return;
    var q = _kzT[(_kzRay >> 1) % n];
    if (q.Item5 + dt > 1) return;
    Vector3D pc = q.Item2 + q.Item3 * dt, dl = pc - me; double dd = dl.Length();
    if (dd < 30) return;
    dl /= dd;
    Vector3D uu = Vector3D.Cross(dl, Math.Abs(dl.Y) < 0.95 ? Vector3D.Up : Vector3D.Right); uu.Normalize();
    Vector3D vv = Vector3D.Cross(dl, uu);
    double rr = pk * Math.Sqrt(((_kzRay >> 1) % 17 + 0.5) / 17.0), th = _kzRay * 2.39996323;
    Vector3D pt = pc + uu * (rr * Math.Cos(th)) + vv * (rr * Math.Sin(th)) + dl * pk;
    var cam = CamFor(pt, dd + pk + res);
    if (cam != null) ScSend(cam.Raycast(pt), 3);
}

string ScLine()
{
    if (_scN == 0) return _fk == "" ? "" : "Search : waiting for the Flight (.SCS)\n";
    if (!ScOn) return "Search : FLIGHT SILENT, scan off\n";
    if ((_scF & 163) == 0) return "Search : idle (" + _cams.Count + " cam, " + _scSen.Count + " sensor)\n";
    return "Search :" + ((_scF & 1) != 0 ? " auto" : "") + ((_scF & 2) != 0 ? ((_scF & 4) != 0 ? " close(far)" : " close") : "") + ((_scF & 32) != 0 && _scSen.Count > 0 ? " sensor" : "")
        + ((_scF & 128) != 0 && _kzT.Count > 0 ? " kz" + _kzT.Count : "") + ", " + _scHitN * 2 + " hits/s\n";
}
string ScStatus() { return _scSt; }
// <<< SEARCH v1.0 <<<
