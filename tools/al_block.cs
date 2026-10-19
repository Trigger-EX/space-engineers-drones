// >>> AUX LINK v1.1 (the Staff side of the [Aux] PB: REPAIR CLIENT, SEEKER, WEAKPOINT RELAY, SEARCH and AI GATE SETUP run there, docs/design/staff_split.md, flight_split.md) >>>
// REPAIR CLIENT (bay dock, medic, self-projector) and the SEEKER turret reader run in a fourth PB, "[Aux] <Model> PB" (scripts/AuxAI.cs, one script for every
// model). This block gives SURVIVAL STAFF and STAFF SEARCH the same function names as proxies. The Aux PB never writes an override: during a dock approach it
// sends setpoints and the Staff flies them with the SURVIVAL STAFF dock actuator (DkGo / DkSteer / DkRel, FOL bit 2). Link (CurrentConstruct, key _fk):
//   .SDK  Staff -> Aux    MyTuple<int flags, long tgtId, Vector3D aux, string cfg, string prevVerb, string ops>: flags bit0 DAMAGED (_svRtb), bit1 docked / welded,
//         bit2 FLIGHT DOWN, bit3 Staff steers a dock, bit4 under fire, bit5 mode IDLE, bit6 mode RTB, bit7 SeekerOn, bit8 RpDockOk, bit9 AI ASSIST (F2S bit 17);
//         aux = (_svAmin, SvCapV(), MaxSpeed); cfg = Tag \n SeekerGroup \n flChan (FLEET LINK _flChan) \n wpPri \n wpExp \n agSwitch|agTyped|agSpeed|agEvery
//         (wpPri / wpExp: [Fleet] "Weakpoint Priority" / "Weakpoint Expire (s)" of this Staff PB as written, defaults 1,3,2 / 20 written here; the last line
//         is CONFIG SERVER's CfAg, "" until its first parse); ops = queued repair words, one a line ("w<k>" RpWant(k), "s<why>" RpStop(why)). Every 10 ticks,
//         at once on a change or with ops; at once on a new Aux (DKS source), a new key, a Staff run gap and SkBlocks() (the "seeker" word).
//   .DKS  Aux -> Staff    MyTuple<int flags, Vector3D vel, Vector3D fwd, Vector3D up, string status>: bit0 FLY (DkSteer(vel, fwd, up)), bit1 repair session,
//         bit3 REPAIRED (SvRepaired()); status = the Aux's Repair / Project / Seek / weakpoint (WpStatus) / search / AI-setup lines for this screen.
//   v1.1: .SKW is gone (WEAKPOINT RELAY runs in the Aux next to its seeker). The Staff still listens to the wing's <flChan>_WP (receive only) and keeps the
//   turret holes (kind 2 Weapons) in _wpMap for SURVIVAL STAFF's exposure score (SvExpo); entries drop after wpExp s or on DONE.
//   .D2S  Aux -> Staff    string: a PB argument given to the Aux PB -> Command().
// FLY counts while the last DKS is at most 15 ticks old and the Flight is up: its rise calls DkGo(), its fall DkRel(); a Staff run gap drops it without a write.
// Without an [Aux] PB: no bay dock, medic, self-projector or seeker (the screen says so after 5 s); everything else works.
// Hooks: the REPAIR CLIENT / SEEKER call sites of SURVIVAL STAFF and STAFF SEARCH (RpRead in SvsRead, RpStep in SvWatch, SkBlocks in SsBlocks, ...).
// Uses: _fk, _now, _tick, _mode (IDLE/RTB), _fl, _tgtId, _down, Tag, MaxSpeed, RpDockOk, SeekerOn, SeekerGroup, _svRtb, _svDock, _svAmin, _svPrevVerb, SvCapV(),
//   SvB/SvD/SvT, DkGo/DkRel/DkSteer/DkHold/SvRepaired, _dkOn, _dkRelTk, Command(), _flChan, FleetV(), CfAg (CONFIG SERVER).
string RpMedCh = "MEDIC", _alSt = "", _alOps = "", _alCfg = "", _alPv = "", _alWp = "1,3,2\n20"; double _alWx = 20;
IMyBroadcastListener _alLd, _alLa, _alLs; long _alSrc, _alTgt, _alTk = -99, _alDkTk = -99; int _alF = -1, _alDf; double _alDkT = -99;
bool _dkFly, _alFull = true; Vector3D _alV, _alW, _alU;
bool SvFly { get { return _dkFly || DkHold; } }
class WpE { public long Tgt; public Vector3D S; public int F; public double T; }
Dictionary<long, WpE> _wpMap = new Dictionary<long, WpE>(); List<long> _alDel = new List<long>();

void RpCfg(MyIni i)
{   // the Aux reads these itself (Flight PB [Survival]); listed here so that the Staff writes their defaults
    SvB(i, "Repair At Bay", true); SvD(i, "Dock Speed (m/s)", 2); SvD(i, "Dock Final Speed (m/s)", 0.3); SvB(i, "Call Medic", true);
    RpMedCh = SvT(i, "Survival", "Medic Channel", "MEDIC").Trim().ToUpperInvariant();
    if (RpMedCh == "") RpMedCh = "MEDIC";
}
void RpListen()
{
    foreach (var l in new[] { _alLd, _alLa, _alLs }) if (l != null) IGC.DisableBroadcastListener(l);
    _alLd = _alLa = _alLs = null;
    if (_fk != "") { _alLd = IGC.RegisterBroadcastListener(_fk + ".DKS"); _alLa = IGC.RegisterBroadcastListener(_fk + ".D2S"); }
    _alLs = _flChan != "" ? IGC.RegisterBroadcastListener(_flChan + "_WP") : null;
    _alFull = true;
}
void RpBlocks()
{   // the [Fleet] weakpoint keys of this PB: the Aux's WEAKPOINT RELAY gets them in the SDK cfg
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    const string S = "Fleet", P = "Weakpoint Priority", E = "Weakpoint Expire (s)";
    if (!ini.ContainsKey(S, P)) ini.Set(S, P, "1,3,2");
    if (!ini.ContainsKey(S, E)) ini.Set(S, E, 20.0);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    _alWx = Math.Max(3, ini.Get(S, E).ToDouble(20));
    _alWp = ini.Get(S, P).ToString("1,3,2").Replace('\n', ' ') + "\n" + _alWx;
}
void RpRead()
{
    while (_alLd != null && _alLd.HasPendingMessage)
    {
        var m = _alLd.AcceptMessage();
        if (!(m.Data is MyTuple<int, Vector3D, Vector3D, Vector3D, string>)) continue;
        var d = (MyTuple<int, Vector3D, Vector3D, Vector3D, string>)m.Data;
        if (m.Source != _alSrc) { _alSrc = m.Source; _alFull = true; }   // a new (or rebuilt) Aux PB: tell it everything at once
        _alDf = d.Item1; _alV = d.Item2; _alW = d.Item3; _alU = d.Item4; _alSt = d.Item5; _alDkT = _now; _alDkTk = _tick;
        if ((d.Item1 & 8) != 0) SvRepaired();
    }
    while (_alLa != null && _alLa.HasPendingMessage) { var m = _alLa.AcceptMessage(); if (m.Data is string) Command((string)m.Data); }
    while (_alLs != null && _alLs.HasPendingMessage)
    {   // the wing's weakpoints (receive only): turret holes for SvExpo
        var m = _alLs.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data;
        if ((d.Item6 & 32) != 0) { _wpMap.Remove(d.Item5); continue; }
        if ((d.Item6 & 7) != 2 || !FleetV(d.Item2) || (!_wpMap.ContainsKey(d.Item5) && _wpMap.Count >= 64)) continue;
        _wpMap[d.Item5] = new WpE { Tgt = d.Item1, S = d.Item2, F = d.Item6, T = _now };
    }
    if (_tick % 60 == 0) { _alDel.Clear(); foreach (var kv in _wpMap) if (_now - kv.Value.T > _alWx) _alDel.Add(kv.Key); foreach (var k in _alDel) _wpMap.Remove(k); }
    bool f = (_alDf & 1) != 0 && _tick - _alDkTk <= 15 && !_down;
    if (Runtime.TimeSinceLastRun.TotalSeconds > 0.5)
    {   // run gap: let go without a write (the Flight may fly already); a FLY still coming re-takes with the 3-tick gate
        if (_dkFly) { _dkOn = false; _dkRelTk = _tick; }
        _dkFly = false; _alFull = true;
    }
    else if (f != _dkFly) { if (f) DkGo(); else DkRel(); _dkFly = f; }
    if (_fk == "") { _alOps = ""; return; }
    int fl = (_svRtb ? 1 : 0) | (_svDock ? 2 : 0) | (_down ? 4 : 0) | (_dkFly ? 8 : 0) | ((_fl & 16) != 0 ? 16 : 0) | (_mode == Mode.IDLE ? 32 : 0)
        | (_mode == Mode.RTB ? 64 : 0) | (SeekerOn ? 128 : 0) | (RpDockOk ? 256 : 0) | ((_fl & 131072) != 0 ? 512 : 0);
    string c = Tag + "\n" + SeekerGroup + "\n" + _flChan + "\n" + _alWp + "\n" + CfAg;
    if (!_alFull && _alOps == "" && fl == _alF && _tgtId == _alTgt && c == _alCfg && _svPrevVerb == _alPv && _tick - _alTk < 10) return;
    IGC.SendBroadcastMessage(_fk + ".SDK", new MyTuple<int, long, Vector3D, string, string, string>(fl, _tgtId, new Vector3D(_svAmin, SvCapV(), MaxSpeed), c, _svPrevVerb, _alOps),
        TransmissionDistance.CurrentConstruct);
    _alF = fl; _alTgt = _tgtId; _alCfg = c; _alPv = _svPrevVerb; _alTk = _tick; _alOps = ""; _alFull = false;
}
void RpStep() { if (_dkFly) DkSteer(_alV, _alW, _alU); }
void RpWant(int k) { _alOps += "w" + k + "\n"; }
void RpStop(string why) { _alOps += "s" + why + "\n"; }
string RpStatus()
{
    if (_now - _alDkT < 3) return _alSt;
    return _alSrc != 0 ? "! Aux PB silent\n" : _now > 5 ? "! no [Aux] PB (repair, medic, seeker off)\n" : "";
}
void SkBlocks() { _alFull = true; }
void SkStep() { }
string SkStatus() { return ""; }
// <<< AUX LINK v1.1 <<<
