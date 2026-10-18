// >>> REPAIR CLIENT v1.1 (self-projector, repair bay and medic patient side, docs/design/survivability.md, docs/design/staff_split.md) >>>
// Patient side of the repair work (docs/design/survivability.md items 15, 17, 18; channels s4.3, antenna range):
//  * REPAIR BAY (item 17): damaged (SURVIVAL STAFF latch, or the words "repair" / "dock"), Flight in RTB or IDLE, not under fire, an own merge block (RpDockOk):
//    the nearest free, stationary bay advertised on <Tag>_RBAY MyTuple<long bayPb, int bay | flags << 8 (bit0 free, bit1 repair, bit2 stock), Vector3D clampPos,
//    Vector3D axis, Vector3D dockFwd, Vector3D dockUp> within 4 km is asked on <Tag>_RREQ MyTuple<long bayPb, int bay, long droneGrid> (resent once after 5 s) and
//    answers on <Tag>_RACK MyTuple<long droneGrid, long bayPb, int bay, int ok>. Then the Staff flies (FOL bit 2: the Flight lets go): TRANSIT to 20 m + radius
//    out along the axis (the RREQ is renewed every 30 s: the bay holds a reservation 90 s), ALIGN (2 deg, 0.3 m/s, 15 s max), INSERT along -axis at "Dock Speed" .. "Dock Final Speed" with a lateral PD (off by 0.8 m within 3 m:
//    back out, 3 retries, then the bay is dropped and a medic may be called), CONTACT = own merge locked: overrides released, dampeners on; the bay repairs and
//    releases and the LAUNCH block flies the take-off. Aborts (overrides released) on fire, a mode word, "fight", the bay's silence or the Flight PB going down.
//  * MEDIC (item 18): no bay, the Flight IDLE (home) and a free <M>_AD MyTuple<long medic, Vector3D pos, int flags> within 3 km: <M>_REQ MyTuple<long medic,
//    long patient, Vector3D pos, int missing>; on <M>_ACK MyTuple<long patient, long medic, int ok> ok = 1 the self-projector goes on and <M>_WORK
//    MyTuple<long patient, Vector3D centre, Vector3D vel, Vector3D p0, Vector3D p1, int packed (remaining | radius dm << 12 | done << 24 | under fire << 25)>
//    is sent at 1 Hz with the two missing / damaged cells nearest the medic (zero = no point). An ACK ok = 0 while serviced = the medic gave up.
//    Done (nothing missing or damaged, projector RemainingBlocks 0): WORK with the done bit, projector off, the mode from before the damage RTB is sent again.
//    Missing cells: an own-grid cell scan (200 cells a tick) when the grid is new (printed / released / repaired), re-checked every 2 s while serviced.
//  * SELF-PROJECTOR (item 15): the block named "... Projector [Self]": on while a medic works or docked at a bay without a projector (no Hive Mother on the
//    grid), off at a printer bay and on return to duty. Written only on those changes (a cradle bay switches it itself in its REPAIR state).
// Keys ([Survival]): Repair At Bay, Dock Speed (m/s), Dock Final Speed (m/s), Call Medic, Medic Channel. Per-script: bool RpDockOk.
// Hosts (v1.1, docs/design/staff_split.md): GnatStaffAI and BasiliskStaffAI, and AuxAI (the [Aux] PB of the other 7 drones, which reach it through AUX LINK).
// The block never writes a thruster / gyro override or DampenersOverride itself: it calls the host's actuator hooks DkGo() (take the actuators), DkSteer(vel, nose,
// roof) (fly that setpoint), DkRel() (let go) and SvRepaired() (clear the damage latch). SURVIVAL STAFF implements them (the Staff flies, FOL bit 2); AuxAI turns
// them into .DKS setpoints for its Staff. SvFly (FOL bit 2) = flying or the 2 ticks after the release write (DkHold).
// Uses: _svRtb, _svDock, _svAmin, _svPrevVerb, SvCapV(), SvB/SvD/SvT, DkGo/DkSteer/DkRel/DkHold/SvRepaired, _rc, _now, _tick, _mode (IDLE/RTB), _fl, _down, Tag,
//   MaxSpeed, Fwd(), FleetV().
bool RpBayOn = true, RpMedic = true, _rpFly, _rpRetry; double RpDockV = 2, RpDockV0 = 0.3, _rpT, _rpT2, _rpStart, _rpNext, _rpScanT = -99; string RpMedCh = "MEDIC", _rpMsg = "";
int _rpS, _rpReq, _rpTry, _rpScanI, _rpChkI = -1, _rpBadN; long _rpKey, _rpMed;
IMyBroadcastListener _rpLb, _rpLa, _rpLm, _rpLk; IMyShipMergeBlock _rpMerge; IMyProjector _rpProj; IMyCubeGrid _rpScanG;
Vector3D _rpO; class RpB { public long Pb; public int Bay, F; public Vector3D P, P0, Ax, Fw, Up; public double T, Bad = -999; public bool Two; }
Dictionary<long, RpB> _rpBays = new Dictionary<long, RpB>(); Dictionary<long, MyTuple<Vector3D, int, double>> _rpMeds = new Dictionary<long, MyTuple<Vector3D, int, double>>();
Dictionary<long, double> _rpMedBad = new Dictionary<long, double>();
List<Vector3I> _rpCells = new List<Vector3I>(), _rpBad = new List<Vector3I>(), _rpBadDone = new List<Vector3I>();
bool SvFly { get { return _rpFly || DkHold; } }

void RpCfg(MyIni i)
{
    RpBayOn = SvB(i, "Repair At Bay", true); RpDockV = Math.Max(0.2, SvD(i, "Dock Speed (m/s)", 2)); RpDockV0 = Math.Max(0.05, SvD(i, "Dock Final Speed (m/s)", 0.3));
    RpMedic = SvB(i, "Call Medic", true); RpMedCh = SvT(i, "Survival", "Medic Channel", "MEDIC").Trim().ToUpperInvariant();
    if (RpMedCh == "") RpMedCh = "MEDIC";
}
void RpListen()
{
    foreach (var l in new[] { _rpLb, _rpLa, _rpLm, _rpLk }) if (l != null) IGC.DisableBroadcastListener(l);
    _rpLb = IGC.RegisterBroadcastListener(Tag + "_RBAY"); _rpLa = IGC.RegisterBroadcastListener(Tag + "_RACK");
    _rpLm = IGC.RegisterBroadcastListener(RpMedCh + "_AD"); _rpLk = IGC.RegisterBroadcastListener(RpMedCh + "_ACK");
}
void RpBlocks()
{
    var g = Me.CubeGrid; _rpMerge = null; _rpProj = null;
    var ml = new List<IMyShipMergeBlock>(); GridTerminalSystem.GetBlocksOfType(ml, b => b.CubeGrid == g && !b.CustomName.Contains("Flare"));
    if (ml.Count > 0) _rpMerge = ml[0];
    var pl = new List<IMyProjector>(); GridTerminalSystem.GetBlocksOfType(pl, b => b.CubeGrid == g && b.CustomName.Contains("Projector [Self]"));
    if (pl.Count > 0) _rpProj = pl[0];
}
void RpProj(bool on) { if (_rpProj != null && _rpProj.IsFunctional && _rpProj.Enabled != on) _rpProj.Enabled = on; }

void RpRead()
{
    long me = Me.CubeGrid.EntityId;
    while (_rpLb != null && _rpLb.HasPendingMessage)
    {
        var m = _rpLb.AcceptMessage();
        if (!(m.Data is MyTuple<long, int, Vector3D, Vector3D, Vector3D, Vector3D>)) continue;
        var d = (MyTuple<long, int, Vector3D, Vector3D, Vector3D, Vector3D>)m.Data;
        if (!FleetV(d.Item3) || d.Item4.LengthSquared() < 0.5 || d.Item5.LengthSquared() < 0.5 || d.Item6.LengthSquared() < 0.5) continue;
        long k = d.Item1 * 97 + (d.Item2 & 255); RpB b;
        if (!_rpBays.TryGetValue(k, out b)) { if (_rpBays.Count >= 16) continue; b = new RpB { Pb = d.Item1, Bay = d.Item2 & 255 }; _rpBays[k] = b; }
        else { b.P0 = b.P; b.Two = _now - b.T < 6; }
        b.F = d.Item2 >> 8; b.P = d.Item3; b.Ax = Vector3D.Normalize(d.Item4); b.Fw = Vector3D.Normalize(d.Item5); b.Up = Vector3D.Normalize(d.Item6); b.T = _now;
    }
    while (_rpLa != null && _rpLa.HasPendingMessage)
    {
        var m = _rpLa.AcceptMessage();
        if (!(m.Data is MyTuple<long, long, int, int>)) continue;
        var d = (MyTuple<long, long, int, int>)m.Data; RpB b;
        if (d.Item1 != me || _rpS != 1 || _rc == null || d.Item2 * 97 + d.Item3 != _rpKey || !_rpBays.TryGetValue(_rpKey, out b)) continue;
        if (d.Item4 == 0) { b.Bad = _now; _rpS = 0; _rpNext = _now + 10; _rpMsg = "bay refused"; continue; }
        _rpO = Vector3D.TransformNormal(_rpMerge.GetPosition() - _rc.GetPosition(), MatrixD.Transpose(_rc.WorldMatrix));   // merge offset in the RC frame, taken once
        DkGo();                                             // the host takes the actuators (FOL bit 2)
        _rpS = 2; _rpFly = true; _rpStart = _rpT2 = _now; _rpTry = 0; _rpMsg = "";
    }
    while (_rpLm != null && _rpLm.HasPendingMessage)
    {
        var m = _rpLm.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, int>)) continue;
        var d = (MyTuple<long, Vector3D, int>)m.Data;
        if (FleetV(d.Item2) && (_rpMeds.Count < 8 || _rpMeds.ContainsKey(d.Item1))) _rpMeds[d.Item1] = new MyTuple<Vector3D, int, double>(d.Item2, d.Item3, _now);
    }
    while (_rpLk != null && _rpLk.HasPendingMessage)
    {
        var m = _rpLk.AcceptMessage();
        if (!(m.Data is MyTuple<long, long, int>)) continue;
        var d = (MyTuple<long, long, int>)m.Data;
        if (d.Item1 != me || d.Item2 != _rpMed) continue;
        if (d.Item3 != 0 && _rpS == 6) { _rpS = 7; _rpT = _now; _rpStart = _now; RpProj(true); _rpScanT = -99; _rpMsg = ""; }
        else if (d.Item3 == 0 && _rpS >= 6) { _rpMedBad[_rpMed] = _now; RpProj(false); _rpS = 0; _rpNext = _now + 10; _rpMsg = "medic gave up"; }
    }
}

void RpWant(int k) { _rpReq = k; _rpNext = 0; }
void RpStop(string why)
{
    if (_rpS >= 2 && _rpS <= 4) DkRel();
    if (_rpS >= 6) { if (_rpS == 7) RpWork(true); RpProj(false); }
    if (_rpS != 5) { _rpS = 0; }
    _rpFly = false; _rpReq = 0; if (why != "") _rpMsg = why;
}
void RpAbort(string why, double bad)
{
    RpB b; if (_rpBays.TryGetValue(_rpKey, out b)) b.Bad = _now + bad;
    DkRel(); _rpFly = false; _rpS = 0; _rpNext = _now + 10; _rpMsg = why;
}

void RpStep()
{
    RpScan();
    bool uf = (_fl & 16) != 0, home = _mode == Mode.RTB || _mode == Mode.IDLE;
    if (_rpS == 0)
    {
        if (_tick % 30 != 21 || _now < _rpNext || !(_svRtb || _rpReq > 0) || _svDock || uf || _down || !(home || _rpReq > 0)) return;
        Vector3D me = _rc.GetPosition(); RpB best = null; double bd = 1.6e7; bool any = false;
        foreach (var kv in _rpBays)
        {
            var b = kv.Value; double q = Vector3D.DistanceSquared(b.P, me);
            if (_now - b.T > 5 || (b.F & 1) == 0 || q > 1.6e7) continue;
            if (_now - b.Bad < 60) continue;
            any = true;
            if (b.Two && (b.P - b.P0).LengthSquared() < 4 && q < bd) { bd = q; best = b; _rpKey = kv.Key; }
        }
        if (RpBayOn && RpDockOk && _rpMerge != null && _rpReq != 3 && best != null)
        {
            IGC.SendBroadcastMessage(Tag + "_RREQ", new MyTuple<long, int, long>(best.Pb, best.Bay, Me.CubeGrid.EntityId));
            _rpS = 1; _rpT = _now; _rpRetry = false; _rpMsg = "asking bay " + best.Bay; return;
        }
        if (!RpMedic || _rpReq == 2 || (any && RpDockOk && _rpMerge != null && RpBayOn) || !(_mode == Mode.IDLE || _rpReq > 0)) return;
        long mb = 0; double md = 9e6;
        foreach (var kv in _rpMeds)
        {
            double bt; if (_now - kv.Value.Item3 > 6 || (kv.Value.Item2 & 1) == 0 || (_rpMedBad.TryGetValue(kv.Key, out bt) && _now - bt < 60)) continue;
            double q = Vector3D.DistanceSquared(kv.Value.Item1, me); if (q < md) { md = q; mb = kv.Key; }
        }
        if (mb == 0) return;
        _rpMed = mb; _rpTry = 0; RpReq(); _rpS = 6; _rpMsg = "calling medic";
        return;
    }
    if (_rpS == 1)
    {
        if (_now - _rpT < 5) return;
        RpB b;
        if (!_rpRetry && _rpBays.TryGetValue(_rpKey, out b)) { _rpRetry = true; _rpT = _now; IGC.SendBroadcastMessage(Tag + "_RREQ", new MyTuple<long, int, long>(b.Pb, b.Bay, Me.CubeGrid.EntityId)); return; }
        RpAbort("bay did not answer", 0); return;
    }
    if (_rpS == 5)
    {   // docked: the bay repairs and releases (LAUNCH flies the take-off); back to duty when the merge pair has opened
        if (_rpMerge == null || !_rpMerge.IsConnected) { if (_svDock) return; RpProj(false); _rpS = 0; SvRepaired(); _rpReq = 0; _rpMsg = "repaired, released"; }
        return;
    }
    if (_rpS >= 6) { RpMedicStep(uf); return; }
    // dock approach (2 transit, 3 align, 4 insert)
    RpB y;
    if (!_rpBays.TryGetValue(_rpKey, out y) || _now - y.T > 10) { RpAbort("bay silent", 60); return; }
    if (uf) { RpAbort("under fire", 30); return; }
    if (_down || _rpMerge == null || !_rpMerge.IsFunctional) { RpAbort("aborted", 0); return; }
    if (!home && _rpReq == 0) { RpAbort("mode changed", 0); return; }
    if (_now - _rpStart > 180) { RpAbort("dock timeout", 600); return; }
    if (_rpMerge.IsConnected)
    {
        DkRel(); _rpFly = false; _rpS = 5; _rpMsg = "docked";
        var hm = new List<IMyProgrammableBlock>(); GridTerminalSystem.GetBlocksOfType(hm, b => b.CubeGrid == Me.CubeGrid && b.CustomData.Contains("[Hive Mother]"));
        RpProj(hm.Count == 0);                               // a printer bay projects the drone itself (and keeps ours off); a cradle bay works through ours
        return;
    }
    if (_now - _rpT2 > 30) { _rpT2 = _now; IGC.SendBroadcastMessage(Tag + "_RREQ", new MyTuple<long, int, long>(y.Pb, y.Bay, Me.CubeGrid.EntityId)); }   // the bay holds us 90 s: renew
    MatrixD R = MatrixD.CreateWorld(Vector3D.Zero, y.Fw, Vector3D.Normalize(y.Up - y.Fw * Vector3D.Dot(y.Up, y.Fw)));
    Vector3D G = y.P + y.Ax * (Me.CubeGrid.GridSize * 0.5) - Vector3D.TransformNormal(_rpO, R), L = G + y.Ax * (20 + Me.CubeGrid.WorldVolume.Radius);
    Vector3D M = _rc.GetPosition(), v; var mw = _rc.WorldMatrix;
    if (_rpS == 2)
    {
        Vector3D d = L - M; double dist = d.Length(), sp = Math.Min(Math.Min(40, MaxSpeed), Math.Min(dist * 0.5, Math.Sqrt(2 * _svAmin * dist))), cap = SvCapV();
        if (cap > 0) sp = Math.Min(sp, cap);
        if (dist < 2) { _rpS = 3; _rpT = _now; }
        DkSteer(dist > 0.1 ? d / dist * sp : Vector3D.Zero, dist > 100 ? d : R.Forward, dist > 100 ? Vector3D.Zero : R.Up);
        return;
    }
    if (_rpS == 3)
    {
        v = (L - M) * 0.5; if (v.LengthSquared() > 4) v = Vector3D.Normalize(v) * 2;
        bool al = Vector3D.Dot(mw.Forward, R.Forward) > 0.9994 && Vector3D.Dot(mw.Up, R.Up) > 0.9994 && _rc.GetShipVelocities().LinearVelocity.LengthSquared() < 0.09;
        if (al || _now - _rpT > 15) { _rpS = 4; _rpMerge.Enabled = true; }   // (LAUNCH switched it off after the last take-off)
        DkSteer(v, R.Forward, R.Up);
        return;
    }
    Vector3D rel = M - G, lat; double along = Vector3D.Dot(rel, y.Ax); lat = rel - y.Ax * along;
    if ((lat.LengthSquared() > 0.64 && along < 3) || along < -1)
    {
        if (++_rpTry > 3) { RpAbort("dock failed", 600); return; }
        _rpS = 3; _rpT = _now; return;                      // back out to the line-up point and try again
    }
    DkSteer(-y.Ax * MathHelper.Clamp(0.5 * along, RpDockV0, RpDockV) - lat, R.Forward, R.Up);
}

void RpReq()
{
    IGC.SendBroadcastMessage(RpMedCh + "_REQ", new MyTuple<long, long, Vector3D, int>(_rpMed, Me.CubeGrid.EntityId, _rc.GetPosition(), Math.Max(_rpBadDone.Count, _rpProj != null ? _rpProj.RemainingBlocks : 0)));
    _rpT = _now; _rpTry++;
}
void RpMedicStep(bool uf)
{
    MyTuple<Vector3D, int, double> md;
    bool seen = _rpMeds.TryGetValue(_rpMed, out md) && _now - md.Item3 < 10;
    if (!seen || _now - _rpStart > 900) { _rpMedBad[_rpMed] = _now; RpStop("medic lost"); _rpNext = _now + 10; return; }
    if (_rpS == 6)
    {
        if (_now - _rpT < 5) return;
        if (_rpTry < 3) RpReq(); else { _rpMedBad[_rpMed] = _now; _rpS = 0; _rpNext = _now + 10; _rpMsg = "medic did not answer"; }
        return;
    }
    if (_now - _rpT < 1) return;
    _rpT = _now;
    if (_rpChkI < 0 && _now - _rpScanT > 2) { _rpChkI = 0; _rpBad.Clear(); }
    bool done = _rpScanT > 0 && _rpBadDone.Count == 0 && (_rpProj == null || !_rpProj.IsProjecting || _rpProj.RemainingBlocks == 0);
    RpWork(done);
    if (!done) return;
    RpProj(false); _rpS = 0; SvRepaired(); _rpReq = 0; _rpMsg = "repaired by medic";
    if (_svPrevVerb != "") Fwd(_svPrevVerb);
}
void RpWork(bool done)
{
    if (_rc == null) return;
    var g = Me.CubeGrid; MyTuple<Vector3D, int, double> md; Vector3D mp = _rpMeds.TryGetValue(_rpMed, out md) ? md.Item1 : _rc.GetPosition(), p0 = Vector3D.Zero, p1 = Vector3D.Zero;
    double d0 = 1e18, d1 = 1e18;
    foreach (var c in _rpBadDone)
    {
        Vector3D w = g.GridIntegerToWorld(c); double q = Vector3D.DistanceSquared(w, mp);
        if (q < d0) { p1 = p0; d1 = d0; p0 = w; d0 = q; } else if (q < d1) { p1 = w; d1 = q; }
    }
    var v = g.WorldVolume;
    int pk = Math.Min(4095, _rpBadDone.Count) | (Math.Min(4095, (int)(v.Radius * 10)) << 12) | (done ? 1 << 24 : 0) | ((_fl & 16) != 0 ? 1 << 25 : 0);
    IGC.SendBroadcastMessage(RpMedCh + "_WORK", new MyTuple<long, Vector3D, Vector3D, Vector3D, Vector3D, int>(g.EntityId, v.Center, _rc.GetShipVelocities().LinearVelocity, p0, p1, pk));
}
// own-grid cells: a full scan when the grid is new (the intact drone), and while a medic works a re-check of those cells (missing, or damaged)
void RpScan()
{
    var g = Me.CubeGrid;
    if (_svDock) return;
    if (g != _rpScanG) { _rpScanG = g; _rpCells.Clear(); _rpScanI = 0; _rpChkI = -1; _rpBadDone.Clear(); _rpScanT = -99; }
    Vector3I a = g.Min, s = g.Max - g.Min + Vector3I.One; int n = s.X * s.Y * s.Z;
    if (_rpScanI < n && n < 200000)
    {
        for (int k = 0; k < 200 && _rpScanI < n; k++, _rpScanI++)
        {
            int i = _rpScanI; var c = new Vector3I(a.X + i % s.X, a.Y + (i / s.X) % s.Y, a.Z + i / (s.X * s.Y));
            if (g.CubeExists(c) && _rpCells.Count < 4000) _rpCells.Add(c);
        }
        return;
    }
    if (_rpChkI < 0) return;
    for (int k = 0; k < 200 && _rpChkI < _rpCells.Count; k++, _rpChkI++)
    {
        var c = _rpCells[_rpChkI]; var b = g.CubeExists(c) ? g.GetCubeBlock(c) : null;
        if (b == null || b.CurrentDamage > 0 || b.BuildIntegrity < b.MaxIntegrity) _rpBad.Add(c);
    }
    if (_rpChkI >= _rpCells.Count) { _rpBadDone.Clear(); _rpBadDone.AddRange(_rpBad); _rpChkI = -1; _rpScanT = _now; }
}
string RpStatus()
{
    string[] sn = { "", "asking bay", "dock transit", "dock align", "dock insert", "docked", "calling medic", "medic working" };
    string s = _rpS > 0 ? "Repair : " + sn[_rpS] + (_rpS == 7 ? ", " + _rpBadDone.Count + " cells" : "") + (_rpS == 4 && _rpTry > 0 ? ", retry " + _rpTry : "") + "\n" : _rpMsg != "" && _svRtb ? "Repair : " + _rpMsg + "\n" : "";
    if (_rpProj != null && _rpProj.Enabled && _rpProj.IsProjecting) s += "Project: " + _rpProj.RemainingBlocks + " blocks to build\n";
    return s;
}
// <<< REPAIR CLIENT v1.1 <<<
