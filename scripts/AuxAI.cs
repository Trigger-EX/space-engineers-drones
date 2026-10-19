// =====================================================================
//  AUX AI  v1.1  - the [Aux] PB of the split drones (docs/design/staff_split.md, docs/design/flight_split.md): off-hot-path modules of the Staff and Flight PBs
//  For Space Engineers (vanilla). Paste into a fourth Programmable Block on the SAME grid as the "[Flight] ..." and "[Staff] ..." PBs, named "[Aux] ...".
//  ONE script for every model (Hornet, Watcher, Harrier, Lancer, Aegis, Wasp, Longbow, Mantis, Basilisk): everything model-specific comes from the Staff
//  (.SDK) and the Flight (.SCS) PBs.
//   * REPAIR CLIENT    repair bay dock (item 17), medic patient side (item 18), self-projector (item 15) - antenna channels <Tag>_RBAY/_RREQ/_RACK, <M>_AD/_REQ/_ACK/_WORK
//   * SEEKER           the [Seeker] turret reader: its hull point goes straight into the WEAKPOINT RELAY here (.WPT flag 4096 to the Flight)
//   * WEAKPOINT RELAY  the wing's shared breach points (<ch>_WP through the antenna, .WPF from / .WPT to the Flight), values from the Staff (.SDK)
//   * SEARCH           the Flight's camera and sensor search: auto-scan, close scan, hit-look, manual lock, sensor poll, kamikaze rays; hits go to the
//                      Flight on .SCN (it keeps Valid / AddContact, tracking, aim and firing)
//   * AI GATE SETUP    "AI Behavior", SpeedLimit and UpdateTargetInterval on the AI blocks, while the Flight runs with AI Assist (SDK bit 9)
//  This PB NEVER writes a thruster or gyro override or DampenersOverride. During a dock approach it sends the setpoint (velocity, nose, roof) to the Staff on
//  .DKS every tick and the Staff flies it (SURVIVAL STAFF dock actuator, FOL bit 2: the Flight lets go). The merge block (Enabled at ALIGN -> INSERT) and the
//  "... Projector [Self]" are the only blocks it switches, plus the [Seeker] turret's targeting group, the cameras' raycast and the AI blocks' setup
//  properties (never their Enabled: the Flight owns that).
//  Link (TransmissionDistance.CurrentConstruct, key "WSPL" + the EntityId of the nearest "[Flight]" PB on this grid, re-checked every 10 s, on a grid change and
//  at 1 Hz while the link is down), see tools/al_block.cs (AUX LINK, the Staff side) and tools/sq_block.cs (SEARCH LINK, the Flight side):
//   .SDK Staff -> Aux (mirror: damage latch, docked, Flight down, under fire, mode IDLE / RTB, target, AI Assist (bit 9), limp cap, repair words;
//        cfg lines: Tag, SeekerGroup, fleet channel, Weakpoint Priority, Weakpoint Expire (s), "agSwitch|agTyped|agSpeed|agEvery")
//   .DKS Aux -> Staff (FLY + setpoint, session, REPAIRED, the status lines for the Staff screen: repair, seek, weakpoint, search, AI setup)
//   .D2S Aux -> Staff (a PB argument given to this PB: the Staff runs it and forwards it to the Flight), .S2F Aux -> Flight (the mode word after a medic repair).
//   .SCS / .KZR Flight -> Aux (search state, lock requests, kamikaze threats), .SCN Aux -> Flight (hits, lock results, heartbeat every 30 ticks)
//   .WPF Flight -> Aux, .WPT Aux -> Flight (weakpoint relay; the Staff no longer listens)
//  Keys: none here. The repair keys (Repair At Bay, Dock Speed (m/s), Dock Final Speed (m/s), Call Medic, Medic Channel) are read from the [Survival] section
//  of the Flight PB's Custom Data (re-read when it changes); the Staff writes their defaults. Seeker, [Fleet] and [AI Gate] setup values come through the Staff.
//  Failure: no Staff message for 30 ticks (or none yet) = link down: no new requests, a dock in progress aborts ("staff silent"), no seeker; a medic session
//  goes on; search goes on while the Flight sends .SCS (stale after 30 ticks: all scanning stops). Flight down (SDK bit 2): the dock aborts. Without this PB
//  the drone loses bay docking, medic calls, the self-projector, the seeker, camera / sensor search, the kamikaze pack rays, wing weakpoint sharing and the
//  AI-behaviour setup (setup already made persists).
// =====================================================================
const string VER = "1.1";

// ---------------- mirror of the Staff (.SDK) ---------------------------
string Tag = "", SeekerGroup = "PowerSystems", _svPrevVerb = "";
bool SeekerOn, RpDockOk, _svRtb, _svDock, _down = true;
int _fl; long _tgtId; double _svAmin = 1, _svCap, MaxSpeed = 100;
string _flChan = "", _wpPriS = "", _wpExpS = "", _agLine = null;   // SDK cfg lines 3-6 (WEAKPOINT RELAY, AI GATE SETUP)
bool _agLive, _aiA, _agDo; long _sdkUpTk = -999;                  // _agLive: no AI GATE core here (no "AI ok" suffix); _aiA: SDK bit 9 AI ASSIST
List<IMyFunctionalBlock> _ai = new List<IMyFunctionalBlock>();
long _f2sTick { get { return Math.Max(_scTk, _sdkUpTk); } }       // WEAKPOINT RELAY: the Flight was heard (.SCS, or the Staff says it is up)
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
void WpCfg() { }                                           // WEAKPOINT RELAY v1.2 hook: the [Fleet] values come in the SDK cfg (WpSet in ReadSdk)
// AI GATE SETUP values from the SDK cfg line "agSwitch|agTyped|agSpeed|agEvery" (bools 1/0 or true/false); a missing or bad field keeps its value
void AgSet(string s)
{
    var a = (s ?? "").Split('|'); bool b; double x;
    if (a.Length > 0 && AgB(a[0], out b)) AiBehaviorSwitch = b;
    if (a.Length > 1 && AgB(a[1], out b)) AiTyped = b;
    if (a.Length > 2 && double.TryParse(a[2], out x)) AiSpeed = Math.Max(1, x);           // (the AI GATE CONFIG post-ops)
    if (a.Length > 3 && double.TryParse(a[3], out x)) AiSearchEvery = Math.Max(0, (int)x);
}
bool AgB(string s, out bool b) { s = s.Trim(); b = s == "1"; return b || s == "0" || bool.TryParse(s, out b); }
// the [AI] blocks (the Flight's IsAiBlock rule) -> AI GATE SETUP; never their Enabled
void AgFind()
{
    GridTerminalSystem.GetBlocksOfType(_ai, b => b.IsSameConstructAs(Me) && (b.CustomName.Contains("[AI]") || b.BlockDefinition.ToString().Contains("OffensiveCombat")
        || b.BlockDefinition.ToString().Contains("FlightMovement")));
    AiGateFind(_ai);
}
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

// >>> AI GATE SETUP v1.0 (sub-block of AI GATE v1.3: one-time AI-block setup; tools/ags_block.cs, synced by tools/blocks_sync.py) >>>
// Switches "AI Behavior" on, sets IMyFlightMovementBlock.SpeedLimit (typed API, opt-in) and IMyOffensiveCombatBlock.UpdateTargetInterval on the [AI]
// blocks. Off the hot path (terminal property writes that persist in the world), so it may run in another PB than the AI GATE core: final hosts AuxAI
// (drones), StaffAI (Helm ships), GnatStaffAI (variant G2), WaspAI and TubeRamAI (standalone, with the core). The Aux sets the four fields from the
// SDK cfg line "agSwitch|agTyped|agSpeed|agEvery" with its own host code (AgSet(string) in AuxAI.cs) and calls AiGateFind when SDK bit 9 AI ASSIST rises,
// when those values change, and on its 600-tick block rescan while bit 9 is set (flight_split.md 6.4).
// Hooks: AiGateFind(list of [AI] blocks) after the AI blocks are found; AiGateStatus() in the status text.
// Uses from the script: _agLive (declared by the AI GATE core; a host without the core declares "bool _agLive;" itself, false = no "AI ok" suffix).
bool AiBehaviorSwitch = true, AiTyped = false; double AiSpeed = 100; int AiSearchEvery = 5, _agFound = -1;   // _agFound: -1 not tried, else the number of AI blocks whose behaviour switch was found
List<ITerminalProperty> _agProps = new List<ITerminalProperty>();

void AiGateFind(List<IMyFunctionalBlock> ai)
{
    if (AiSearchEvery > 0) foreach (var b in ai) { var oc = b as IMyOffensiveCombatBlock; if (oc != null) oc.UpdateTargetInterval = AiSearchEvery; }
    if (!AiBehaviorSwitch || ai.Count == 0) return;
    int n = 0;
    foreach (var b in ai)
    {
        var fm = AiTyped ? b as IMyFlightMovementBlock : null;
        if (fm != null) fm.SpeedLimit = (float)AiSpeed;
        _agProps.Clear(); b.GetProperties(_agProps);
        ITerminalProperty<bool> sw = null;
        foreach (var p in _agProps)
        {
            string id = p.Id.ToLowerInvariant();
            if (!id.Contains("behavio")) continue;
            var bp = p.As<bool>();
            if (bp != null && (sw == null || id.Contains("activ"))) sw = bp;
        }
        if (sw != null && !sw.GetValue(b)) sw.SetValue(b, true);
        if (fm != null ? fm.IsAutoPilotEnabled : sw != null) n++;
    }
    _agFound = n;
}

string AiGateStatus()
{
    if (_agFound < 0) return "";
    if (_agFound == 0 && AiBehaviorSwitch) return "AI Behavior switch not found:\n turn AI Behavior on by hand\n";
    return (AiBehaviorSwitch ? "AI Behavior on (" + _agFound + " blocks)" : "") + (_agLive ? " AI ok\n" : AiBehaviorSwitch ? "\n" : "");
}
// <<< AI GATE SETUP v1.0 <<<

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

// >>> WEAKPOINT RELAY v1.2 (shared block: the [Aux] PB of the drones and GnatStaffAI, see docs/design/hive_weakpoint.md, docs/hive-protocol.md, docs/design/flight_split.md 6.5) >>>
// Fleet side of the shared breach point. The Flight PB sends its own findings on <Flight>.WPF; this PB puts them on the fleet channel <ch>_WP,
// collects the findings of the whole wing, and sends the best hole for the Flight's current target back on <Flight>.WPT (once a second).
//   <ch>_WP  MyTuple<long target, Vector3D s, Vector3D d, Vector3D info, long senderGrid, int flags>
//     s = hull entry and d = drill direction, both in the TARGET frame; info = (hole depth m, vital depth hint m or -1, age s)
//     flags: bits 0-2 kind (0 camera breach point, 1 PowerSystems, 2 Weapons, 3 Propulsion, 4 other), 8 seeker found it, 16 block position
//     from the game, 32 DONE (everybody drops the hole), 64 STALL (that sender gave up), 128 ADOPTED (the sender took it from the wing and its own ray confirmed it)
//     Relay -> Flight (.WPT): bits 8-11 = number of drones that support the hole.
// A drone sends only its OWN confirmed findings; a received hole is never forwarded (no echo). Holes within 4 m of each other are one hole; the best
// one wins (seeker kind by "Weakpoint Priority", then supporters, then depth, then the lowest grid id), so every drone picks the same hole.
// [Fleet] keys: Weakpoint Priority=1,3,2 (power, propulsion, weapons)   Weakpoint Expire (s)=20
// v1.1 (docs/design/survivability.md): the drone's OWN seeker turret (SEEKER -> WpSeek): that entry (map key ~own grid id,
// internal flag 4096, kept 3 s) ranks above every other hole, goes to the wing like an own finding, and reaches the Flight with .WPT flag 4096 (the Flight
// takes it as its own seeker point, BreachSet(s, d, true)); .WPT is sent twice a second while it is fresh, else once a second.
// v1.2 (docs/design/flight_split.md 3, 6.5): hosts AuxAI (the 8 drones with an [Aux] PB) and GnatStaffAI. WpCfg() is a host hook: it gets the two
// [Fleet] values and passes them to WpSet(priority text, expire s). AuxAI: the Staff writes the defaults and sends both values in the .SDK cfg (lines 4-5),
// AuxAI calls WpSet when they change and its WpCfg() is empty; WpSeek is called locally by SEEKER (no .SKW). A Staff host keeps the v1.1 behaviour with:
//   void WpCfg() { var ini = new MyIni(); MyIniParseResult r; if (!ini.TryParse(Me.CustomData, out r)) return;
//     if (!ini.ContainsKey("Fleet", "Weakpoint Priority")) ini.Set("Fleet", "Weakpoint Priority", "1,3,2");
//     if (!ini.ContainsKey("Fleet", "Weakpoint Expire (s)")) ini.Set("Fleet", "Weakpoint Expire (s)", 20.0);
//     string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
//     WpSet(ini.Get("Fleet", "Weakpoint Priority").ToString("1,3,2"), ini.Get("Fleet", "Weakpoint Expire (s)").ToDouble(20)); }
// Script hooks: WpStep() every tick (Staff: in Tick after Relay()); WpStatus() in the status text (AuxAI: in the .DKS status); WpCfg() (host).
// Uses: _fk, _flChan, _tgtId, _tick, _now, _f2sTick (the tick the Flight was last heard: .WPT only while it is <= 90 ticks old), FleetV(), WpCfg().
class WpE { public long Tgt, Snd; public Vector3D S, D, I; public int F; public double T; }
Dictionary<long, WpE> _wpMap = new Dictionary<long, WpE>();
IMyBroadcastListener _wpLf, _wpLw;
string _wpKey = null; int[] _wpPri = new int[] { 1, 3, 2 }; double _wpExp = 20, _wpFleetT = -99, _wpFlT = -99, _wpEvSendT = -99; WpE _wpBest; int _wpBestK;
List<WpE> _wpDone = new List<WpE>(); List<long> _wpDel = new List<long>(); MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> _wpLast;
bool WpOkT(MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> d)
{
    double dl = d.Item3.Length();
    return FleetV(d.Item2) && FleetV(d.Item4) && Math.Abs(d.Item2.X) < 1e4 && Math.Abs(d.Item2.Y) < 1e4 && Math.Abs(d.Item2.Z) < 1e4 && dl > 0.9 && dl < 1.1
        && d.Item4.X >= 0 && d.Item4.X <= 100 && d.Item4.Y < 500 && !double.IsNaN(d.Item4.X + d.Item4.Y + d.Item4.Z);
}
// the two [Fleet] values (from the host's WpCfg(), or from the .SDK cfg in AuxAI)
void WpSet(string pri, double exp)
{
    var l = new List<int>();
    foreach (var p in (pri ?? "").Split(',')) { int k; if (int.TryParse(p.Trim(), out k)) l.Add(k); }
    _wpPri = l.ToArray(); _wpExp = Math.Max(3, exp);
}
int WpRank(WpE e)
{
    if ((e.F & 4096) != 0) return 100;                     // own seeker turret, read this second
    if ((e.F & 8) == 0) return 0;                           // camera breach points rank below anything a turret found
    int k = e.F & 7, i = Array.IndexOf(_wpPri, k);
    return i >= 0 ? 20 - i : 5;
}
WpE WpPut(long snd, MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> d)
{
    WpE e;
    if (!_wpMap.TryGetValue(snd, out e)) { if (_wpMap.Count >= 64) return null; e = new WpE(); _wpMap[snd] = e; }
    e.Tgt = d.Item1; e.Snd = snd; e.S = d.Item2; e.D = d.Item3; e.I = d.Item4; e.F = d.Item6; e.T = _now;
    return e;
}
void WpStep()
{
    string key = _flChan + "|" + _fk;
    if (key != _wpKey)
    {
        if (_wpKey != null) { if (_wpLf != null) IGC.DisableBroadcastListener(_wpLf); if (_wpLw != null) IGC.DisableBroadcastListener(_wpLw); }
        _wpKey = key; _wpMap.Clear(); _wpDone.Clear(); WpCfg();
        _wpLf = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".WPF") : null;
        _wpLw = _flChan != "" ? IGC.RegisterBroadcastListener(_flChan + "_WP") : null;
    }
    else if (_tick % 600 == 1) WpCfg();
    long me = Me.CubeGrid.EntityId;
    // my Flight PB -> map, and on to the fleet channel (changes and events at once, else every 2 s)
    while (_wpLf != null && _wpLf.HasPendingMessage)
    {
        var m = _wpLf.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data;
        if (!WpOkT(d)) continue;
        d.Item5 = me;
        if ((d.Item6 & 96) != 0) WpEvent(d, me);
        else WpPut(me, d);
        bool changed = d.Item1 != _wpLast.Item1 || d.Item6 != _wpLast.Item6 || (d.Item2 - _wpLast.Item2).LengthSquared() > 1;
        if (_wpLw != null && (changed || (d.Item6 & 96) != 0 || _now - _wpFleetT >= 2)) { IGC.SendBroadcastMessage(_flChan + "_WP", d); _wpFleetT = _now; _wpLast = d; }
    }
    // the wing -> map
    while (_wpLw != null && _wpLw.HasPendingMessage)
    {
        var m = _wpLw.AcceptMessage();
        if (m.Source == IGC.Me || !(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data;
        if (d.Item5 == me || d.Item5 == 0 || !WpOkT(d)) continue;
        if ((d.Item6 & 96) != 0) WpEvent(d, d.Item5); else WpPut(d.Item5, d);
    }
    _wpDel.Clear();
    foreach (var kv in _wpMap) if (_now - kv.Value.T > ((kv.Value.F & 4096) != 0 ? 3 : _wpExp)) _wpDel.Add(kv.Key);
    foreach (var k in _wpDel) _wpMap.Remove(k);
    for (int i = _wpDone.Count - 1; i >= 0; i--) if (_now - _wpDone[i].T > 3) _wpDone.RemoveAt(i);
    WpE sk; bool skf = _wpMap.TryGetValue(~me, out sk) && sk.Tgt == _tgtId && _tgtId != 0;
    if (_tick % (skf ? 30 : 60) != 3) return;
    // best hole for the Flight's target
    _wpBest = null; _wpBestK = 0;
    if (_tgtId != 0)
    {
        long bs = 0; int br = -1, bk = 0; double bd = -1; WpE bb = null;
        foreach (var a in _wpMap.Values)
        {
            if (a.Tgt != _tgtId) continue;
            int k = 0; double dep = 0; int rank = WpRank(a); long low = a.Snd; WpE rep = a;
            foreach (var b in _wpMap.Values)
            {
                if (b.Tgt != _tgtId || (b.S - a.S).LengthSquared() > 16) continue;
                k++; dep = Math.Max(dep, b.I.X); int rb = WpRank(b); if (rb > rank) { rank = rb; rep = b; } if (b.Snd < low) low = b.Snd;
            }
            if (rank > br || (rank == br && (k > bk || (k == bk && (dep > bd || (dep == bd && low < bs))))))
            { br = rank; bk = k; bd = dep; bs = low; bb = rep; }
        }
        if (bb != null) { _wpBest = bb; _wpBestK = bk; _wpBestDep = bd; }
    }
    if (_fk == "" || _tick - _f2sTick > 90) return;
    if (_wpBest != null)
        IGC.SendBroadcastMessage(_fk + ".WPT", new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(_wpBest.Tgt, _wpBest.S, _wpBest.D,
            new Vector3D(_wpBestDep, _wpBest.I.Y, _now - _wpBest.T + _wpBest.I.Z), _wpBest.Snd, (_wpBest.F & 4127) | (Math.Min(_wpBestK, 15) << 8)), TransmissionDistance.CurrentConstruct);
    foreach (var e in _wpDone)
        if (e.Tgt == _tgtId) IGC.SendBroadcastMessage(_fk + ".WPT", new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(e.Tgt, e.S, e.D, e.I, e.Snd, 32), TransmissionDistance.CurrentConstruct);
}
double _wpBestDep, _wpSkT = -99; MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> _wpSkLast;
// own seeker finding (SEEKER): into the map as the top-ranked entry, to the wing on a change or every 2 s
void WpSeek(MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> d)
{
    if (!WpOkT(d)) return;
    long me = Me.CubeGrid.EntityId;
    var e = WpPut(~me, d); if (e != null) { e.Snd = me; e.F |= 4096; }
    if (_flChan != "" && (d.Item1 != _wpSkLast.Item1 || (d.Item2 - _wpSkLast.Item2).LengthSquared() > 1 || _now - _wpSkT >= 2))
    { IGC.SendBroadcastMessage(_flChan + "_WP", d); _wpSkT = _now; _wpSkLast = d; }
}
// DONE: every hole within 4 m of it is gone (and the Flight is told for 3 s); STALL: that sender's support is gone
void WpEvent(MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> d, long snd)
{
    if ((d.Item6 & 32) != 0)
    {
        _wpDel.Clear();
        foreach (var kv in _wpMap) if (kv.Value.Tgt == d.Item1 && (kv.Value.S - d.Item2).LengthSquared() < 16) _wpDel.Add(kv.Key);
        foreach (var k in _wpDel) _wpMap.Remove(k);
        var e = new WpE(); e.Tgt = d.Item1; e.Snd = snd; e.S = d.Item2; e.D = d.Item3; e.I = d.Item4; e.F = 32; e.T = _now;
        _wpDone.Add(e); if (_wpDone.Count > 8) _wpDone.RemoveAt(0);
    }
    else _wpMap.Remove(snd);
}
string WpStatus()
{
    if (_wpBest == null) return _wpMap.Count > 0 ? "Weakpt : " + _wpMap.Count + " report(s), none for the target\n" : "";
    string[] kn = { "breach point", "power", "weapons", "propulsion", "block" };
    return "Weakpt : " + kn[Math.Min(_wpBest.F & 7, 4)] + " hole " + _wpBestDep.ToString("0") + " m, " + _wpBestK + " drone(s), age " + (_now - _wpBest.T).ToString("0") + " s\n";
}
// <<< WEAKPOINT RELAY v1.2 <<<

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
    RpBlocks(); SkBlocks(); ScBlocks();
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
    if (_agDo || (_aiA && _tick % 600 == 0)) { _agDo = false; AgFind(); }   // flight_split.md 6.4: bit 9 rose, the values changed, the 600-tick rescan
    ScStep(); WpStep();
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
        if ((f & 4) == 0) _sdkUpTk = _tick;
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
        _flChan = c.Length > 2 ? c[2].Trim() : "";
        if (c.Length > 4 && c[3].Trim() != "" && (c[3] != _wpPriS || c[4] != _wpExpS)) { double x; _wpPriS = c[3]; _wpExpS = c[4]; WpSet(c[3], double.TryParse(c[4], out x) ? x : 20); }
        bool ai = (f & 512) != 0; string ag = c.Length > 5 ? c[5] : "";
        if (ag != _agLine) { _agLine = ag; AgSet(ag); _agDo |= ai; }
        _agDo |= ai && !_aiA; _aiA = ai;
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
    string st = RpStatus() + SkStatus() + WpStatus() + ScStatus() + (_aiA ? AiGateStatus() : "");
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
    s.Append(RpStatus()).Append(SkStatus()).Append(WpStatus()).Append(ScStatus()).Append(_aiA ? AiGateStatus() : "");
    if (Me.OwnerId == 0) s.Append("! PB has no owner: set ownership\n");
    if (_crash != "") s.Append("! ").Append(_crash).Append('\n');
    if (_lcd != null) _lcd.WriteText(s.ToString());
    Echo(s.ToString());
}
