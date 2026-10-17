// =====================================================================
//  AUX AI  v1.0  - the [Aux] PB of the split drones (docs/design/staff_split.md): REPAIR CLIENT and SEEKER, moved out of the Staff PB
//  For Space Engineers (vanilla). Paste into a fourth Programmable Block on the SAME grid as the "[Flight] ..." and "[Staff] ..." PBs, named "[Aux] ...".
//  ONE script for every model (Hornet, Watcher, Harrier, Lancer, Aegis, Wasp, Longbow, Mantis): everything model-specific comes from the Staff PB.
//   * REPAIR CLIENT  repair bay dock (item 17), medic patient side (item 18), self-projector (item 15) - antenna channels <Tag>_RBAY/_RREQ/_RACK, <M>_AD/_REQ/_ACK/_WORK
//   * SEEKER         the [Seeker] turret reader: its hull point goes to the Staff (.SKW) and from there into the WEAKPOINT RELAY (.WPT flag 4096 to the Flight)
//  This PB NEVER writes a thruster or gyro override or DampenersOverride. During a dock approach it sends the setpoint (velocity, nose, roof) to the Staff on
//  .DKS every tick and the Staff flies it (SURVIVAL STAFF dock actuator, FOL bit 2: the Flight lets go). The merge block (Enabled at ALIGN -> INSERT) and the
//  "... Projector [Self]" are the only blocks it switches, plus the [Seeker] turret's targeting group and the cameras' raycast.
//  Link (TransmissionDistance.CurrentConstruct, key "WSPL" + the EntityId of the nearest "[Flight]" PB on this grid, re-checked every 10 s, on a grid change and
//  at 1 Hz while the link is down), see tools/al_block.cs (AUX LINK, the Staff side):
//   .SDK Staff -> Aux (mirror: damage latch, docked, Flight down, under fire, mode IDLE / RTB, target, Tag, seeker setup, limp cap, repair words)
//   .DKS Aux -> Staff (FLY + setpoint, session, REPAIRED, the status lines for the Staff screen), .SKW Aux -> Staff (seeker finding),
//   .D2S Aux -> Staff (a PB argument given to this PB: the Staff runs it and forwards it to the Flight), .S2F Aux -> Flight (the mode word after a medic repair).
//  Keys: none here. The repair keys (Repair At Bay, Dock Speed (m/s), Dock Final Speed (m/s), Call Medic, Medic Channel) are read from the [Survival] section
//  of the Flight PB's Custom Data (re-read when it changes); the Staff writes their defaults. Seeker Group / Seeker Turret Guides Aim come through the Staff.
//  Failure: no Staff message for 30 ticks (or none yet) = link down: no new requests, a dock in progress aborts ("staff silent"), no seeker; a medic session
//  goes on. Flight down (SDK bit 2): the dock aborts. Without this PB the drone loses bay docking, medic calls, the self-projector and the seeker only.
// =====================================================================

const string VER = "1.0";

// ---------------- mirror of the Staff (.SDK) ---------------------------
string Tag = "", SeekerGroup = "PowerSystems", _svPrevVerb = "";
bool SeekerOn, RpDockOk, _svRtb, _svDock, _down = true;
int _fl; long _tgtId; double _svAmin = 1, _svCap, MaxSpeed = 100;
enum Mode { IDLE, GUARD, RTB }                              // REPAIR CLIENT names IDLE and RTB only; anything else is GUARD here
Mode _mode = Mode.GUARD;

// ---------------- state ----------------------------------------------
double _now = 0; long _tick = 0;
IMyShipController _rc; IMyProgrammableBlock _fpb; IMyTextSurface _lcd; IMyCubeGrid _myGrid;
string _fk = "", _crash = "", _cdTxt = null; int _errRun;
IMyBroadcastListener _lSdk; long _sdkTk = -99, _sdkSrc; int _sdkN, _sdkF;
Vector3D _dkV, _dkF, _dkU; bool _dkRes, _dksFull = true; double _repT = -99; int _dksF = -1; long _dksTk = -99; string _dksSt = "";

// ---------------- host hooks for REPAIR CLIENT and SEEKER (docs/design/staff_split.md s3.2): no override is written here
void DkGo() { }
void DkSteer(Vector3D v, Vector3D f, Vector3D u) { _dkV = v; _dkF = f; _dkU = u; }
void DkRel() { _dkRes = true; }
bool DkHold { get { return false; } }
void SvRepaired() { _svRtb = false; _repT = _now; }        // DKS bit 3 for 2 s; the Staff clears its latch
double SvCapV() { return _svCap; }
void Fwd(string c) { if (_fk != "") IGC.SendBroadcastMessage(_fk + ".S2F", c, TransmissionDistance.CurrentConstruct); }
void WpSeek(MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> d) { if (_fk != "") IGC.SendBroadcastMessage(_fk + ".SKW", d, TransmissionDistance.CurrentConstruct); }
bool SvB(MyIni i, string k, bool d) { return i.Get("Survival", k).ToBoolean(d); }
double SvD(MyIni i, string k, double d) { return i.Get("Survival", k).ToDouble(d); }
string SvT(MyIni i, string s, string k, string d) { return i.Get(s, k).ToString(d); }
bool FleetV(Vector3D v) { return Math.Abs(v.X) < 1e9 && Math.Abs(v.Y) < 1e9 && Math.Abs(v.Z) < 1e9; }   // false for NaN and absurd values

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

public Program()
{
    try { GetBlocks(); } catch (Exception ex) { _crash = "setup: " + ex.Message; Echo(_crash); }
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

void GetBlocks()
{
    var ctrls = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(ctrls, b => b.IsSameConstructAs(Me) && b.CanControlShip);
    _rc = null;
    foreach (var c in ctrls) if (c is IMyRemoteControl) { _rc = c; break; }
    if (_rc == null && ctrls.Count > 0) _rc = ctrls[0];
    var g = Me.CubeGrid;
    var fl = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(fl, b => b != Me && b.CubeGrid == g && b.CustomName.StartsWith("[Flight]", StringComparison.OrdinalIgnoreCase));
    var oldK = _fk; _fpb = null; double best = double.MaxValue;
    foreach (var p in fl) { double q = (p.GetPosition() - Me.GetPosition()).LengthSquared(); if (q < best) { best = q; _fpb = p; } }
    _fk = _fpb != null ? "WSPL" + _fpb.EntityId : "";
    if (_fk != oldK)
    {
        if (_lSdk != null) IGC.DisableBroadcastListener(_lSdk);
        _lSdk = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".SDK") : null;
        _sdkN = 0; _cdTxt = null; _dksFull = true;
    }
    RpBlocks(); SkBlocks();
    LoadCfg();
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.75f; _lcd.Font = "Monospace";
}

// [Survival] of the Flight PB (read only), when its text changed
void LoadCfg()
{
    if (_fpb == null || _fpb.CustomData == _cdTxt) return;
    _cdTxt = _fpb.CustomData;
    var ini = new MyIni(); MyIniParseResult r;
    if (!ini.TryParse(_cdTxt, out r)) return;
    string ch = RpMedCh;
    RpCfg(ini);
    if (ch != RpMedCh && Tag != "") RpListen();
}

public void Main(string arg, UpdateType src)
{
    try { Tick(arg, src); if (_errRun > 0 && _tick % 300 == 0) { _errRun = 0; _crash = ""; } }
    catch (Exception ex)
    {
        if (_crash != ex.Message || _errRun++ % 60 == 0) { _crash = ex.Message; Echo("Script error: " + ex.Message); if (_lcd != null) _lcd.WriteText("AUX SCRIPT ERROR\n" + ex.Message + "\nSend this text to Claude."); }
    }
}

void Tick(string arg, UpdateType src)
{
    if ((src & (UpdateType.Trigger | UpdateType.Terminal | UpdateType.Script | UpdateType.Mod)) != 0 && !string.IsNullOrWhiteSpace(arg) && _fk != "")
        IGC.SendBroadcastMessage(_fk + ".D2S", arg, TransmissionDistance.CurrentConstruct);   // the Staff runs it (and forwards it to the Flight)
    if ((src & UpdateType.Update1) == 0) return;
    double dt = Runtime.TimeSinceLastRun.TotalSeconds;
    if (dt > 0.5) _dksFull = true;                            // run gap: everything to the Staff again
    if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;
    _now += dt; _tick++;
    if (_tick % 600 == 0 || Me.CubeGrid != _myGrid || (_down && _tick % 60 == 0)) { _myGrid = Me.CubeGrid; GetBlocks(); }
    if (_tick % 60 == 30) LoadCfg();
    ReadSdk();
    bool was = _down, stale = _sdkN == 0 || _tick - _sdkTk > 30;
    _down = stale || (_sdkF & 4) != 0;
    if (was && !_down) _dksFull = true;
    if (stale && _rpS >= 1 && _rpS <= 4) RpAbort("staff silent", 0);
    RpRead();
    if (_rc != null && _rc.IsFunctional && _rc.IsSameConstructAs(Me)) { RpStep(); if (!_down) SkStep(); }
    SendDks();
    if (_tick % 30 == 0) Display();
}

// .SDK: the mirror first, then the repair words (one message: a stale DAMAGED bit cannot restart a request after "fight")
void ReadSdk()
{
    while (_lSdk != null && _lSdk.HasPendingMessage)
    {
        var m = _lSdk.AcceptMessage();
        if (!(m.Data is MyTuple<int, long, Vector3D, string, string, string>)) continue;
        var d = (MyTuple<int, long, Vector3D, string, string, string>)m.Data;
        if (m.Source != _sdkSrc) { _sdkSrc = m.Source; _dksFull = true; }   // a new (or rebuilt) Staff PB
        int f = d.Item1; _sdkF = f; _sdkTk = _tick; _sdkN++;
        _svRtb = (f & 1) != 0 && _now - _repT >= 2;          // just repaired: the Staff's latch clears with its next message
        _svDock = (f & 2) != 0; _fl = f & 16;
        _mode = (f & 32) != 0 ? Mode.IDLE : (f & 64) != 0 ? Mode.RTB : Mode.GUARD;
        RpDockOk = (f & 256) != 0;
        _tgtId = d.Item2;
        if (FleetV(d.Item3)) { _svAmin = Math.Max(0.3, d.Item3.X); _svCap = d.Item3.Y; if (d.Item3.Z > 1) MaxSpeed = d.Item3.Z; }
        _svPrevVerb = d.Item5 ?? "";
        var c = (d.Item4 ?? "").Split('\n');
        if (c[0] != Tag) { Tag = c[0]; RpListen(); }
        bool so = (f & 128) != 0; string sg = c.Length > 1 ? c[1] : "";
        if (so != SeekerOn || sg != SeekerGroup) { SeekerOn = so; SeekerGroup = sg; SkBlocks(); }
        foreach (var l in (d.Item6 ?? "").Split('\n'))
        {
            if (l.Length < 1) continue;
            if (l[0] == 'w' && l.Length > 1) RpWant(l[1] - '0');
            else if (l[0] == 's') RpStop(l.Substring(1));
        }
    }
}

// .DKS: every tick while flying a dock, else every 30 ticks and at once on a change
void SendDks()
{
    if (_fk == "") return;
    int f = (_rpFly ? 1 : 0) | (_rpS > 0 ? 2 : 0) | (_now - _repT < 2 ? 8 : 0);
    string st = RpStatus() + SkStatus();
    if (!_rpFly && !_dksFull && !_dkRes && f == _dksF && st == _dksSt && _tick - _dksTk < 30) return;
    IGC.SendBroadcastMessage(_fk + ".DKS", new MyTuple<int, Vector3D, Vector3D, Vector3D, string>(f, _rpFly ? _dkV : Vector3D.Zero, _rpFly ? _dkF : Vector3D.Zero, _rpFly ? _dkU : Vector3D.Zero, st),
        TransmissionDistance.CurrentConstruct);
    _dksF = f; _dksSt = st; _dksTk = _tick; _dksFull = false; _dkRes = false;
    if (!_rpFly) _dkV = _dkF = _dkU = Vector3D.Zero;
}

void Display()
{
    var s = new StringBuilder();
    s.Append("AUX PB v").Append(VER).Append(' ').Append(Tag).Append('\n');
    if (_fpb == null) s.Append("! no [Flight] PB on this grid\n");
    else s.Append("Flight : ").Append(_fpb.IsWorking ? "PB on" : "PB OFF").Append((_sdkF & 4) != 0 && _sdkN > 0 ? "  FLIGHT PB DOWN" : "").Append('\n');
    if (_sdkN == 0) s.Append("Staff  : waiting for the first message\n");
    else if (_tick - _sdkTk > 30) s.Append("STAFF PB SILENT  ").Append(((_tick - _sdkTk) / 60.0).ToString("0.0")).Append(" s\n");
    else s.Append("Staff  : link OK\n");
    if (_rc == null || !_rc.IsFunctional) s.Append("! No working Remote Control / cockpit\n");
    s.Append(RpStatus()).Append(SkStatus());
    if (Me.OwnerId == 0) s.Append("! PB has no owner: set ownership\n");
    if (_crash != "") s.Append("! ").Append(_crash).Append('\n');
    if (_lcd != null) _lcd.WriteText(s.ToString());
    Echo(s.ToString());
}
