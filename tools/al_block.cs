// >>> AUX LINK v1.0 (the Staff side of the [Aux] PB: REPAIR CLIENT and SEEKER run there, docs/design/staff_split.md) >>>
// REPAIR CLIENT (bay dock, medic, self-projector) and the SEEKER turret reader run in a fourth PB, "[Aux] <Model> PB" (scripts/AuxAI.cs, one script for every
// model). This block gives SURVIVAL STAFF and STAFF SEARCH the same function names as proxies. The Aux PB never writes an override: during a dock approach it
// sends setpoints and the Staff flies them with the SURVIVAL STAFF dock actuator (DkGo / DkSteer / DkRel, FOL bit 2). Link (CurrentConstruct, key _fk):
//   .SDK  Staff -> Aux    MyTuple<int flags, long tgtId, Vector3D aux, string cfg, string prevVerb, string ops>: flags bit0 DAMAGED (_svRtb), bit1 docked / welded,
//         bit2 FLIGHT DOWN, bit3 Staff steers a dock, bit4 under fire, bit5 mode IDLE, bit6 mode RTB, bit7 SeekerOn, bit8 RpDockOk; aux = (_svAmin, SvCapV(),
//         MaxSpeed); cfg = Tag + "\n" + SeekerGroup; ops = queued repair words, one a line ("w<k>" RpWant(k), "s<why>" RpStop(why)). Every 10 ticks, at once on
//         a change or with ops; at once on a new Aux (DKS source), a new key, a Staff run gap and SkBlocks() (the "seeker" word).
//   .DKS  Aux -> Staff    MyTuple<int flags, Vector3D vel, Vector3D fwd, Vector3D up, string status>: bit0 FLY (DkSteer(vel, fwd, up)), bit1 repair session,
//         bit3 REPAIRED (SvRepaired()); status = the Aux's Repair / Project / Seek lines for this screen.
//   .SKW  Aux -> Staff    MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> = the WpSeek() argument (own seeker finding) -> WpSeek().
//   .D2S  Aux -> Staff    string: a PB argument given to the Aux PB -> Command().
// FLY counts while the last DKS is at most 15 ticks old and the Flight is up: its rise calls DkGo(), its fall DkRel(); a Staff run gap drops it without a write.
// Without an [Aux] PB: no bay dock, medic, self-projector or seeker (the screen says so after 5 s); everything else works.
// Hooks: the REPAIR CLIENT / SEEKER call sites of SURVIVAL STAFF and STAFF SEARCH (RpRead in SvsRead, RpStep in SvWatch, SkBlocks in SsBlocks, ...).
// Uses: _fk, _now, _tick, _mode (IDLE/RTB), _fl, _tgtId, _down, Tag, MaxSpeed, RpDockOk, SeekerOn, SeekerGroup, _svRtb, _svDock, _svAmin, _svPrevVerb, SvCapV(),
//   SvB/SvD/SvT, DkGo/DkRel/DkSteer/DkHold/SvRepaired, _dkOn, _dkRelTk, WpSeek(), Command().
string RpMedCh = "MEDIC", _alSt = "", _alOps = "", _alCfg = "", _alPv = "";
IMyBroadcastListener _alLd, _alLa, _alLs; long _alSrc, _alTgt, _alTk = -99, _alDkTk = -99; int _alF = -1, _alDf; double _alDkT = -99;
bool _dkFly, _alFull = true; Vector3D _alV, _alW, _alU;
bool SvFly { get { return _dkFly || DkHold; } }

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
    if (_fk != "") { _alLd = IGC.RegisterBroadcastListener(_fk + ".DKS"); _alLa = IGC.RegisterBroadcastListener(_fk + ".D2S"); _alLs = IGC.RegisterBroadcastListener(_fk + ".SKW"); }
    _alFull = true;
}
void RpBlocks() { }
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
    {
        var m = _alLs.AcceptMessage();
        if (m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>) WpSeek((MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data);
    }
    bool f = (_alDf & 1) != 0 && _tick - _alDkTk <= 15 && !_down;
    if (Runtime.TimeSinceLastRun.TotalSeconds > 0.5)
    {   // run gap: let go without a write (the Flight may fly already); a FLY still coming re-takes with the 3-tick gate
        if (_dkFly) { _dkOn = false; _dkRelTk = _tick; }
        _dkFly = false; _alFull = true;
    }
    else if (f != _dkFly) { if (f) DkGo(); else DkRel(); _dkFly = f; }
    if (_fk == "") { _alOps = ""; return; }
    int fl = (_svRtb ? 1 : 0) | (_svDock ? 2 : 0) | (_down ? 4 : 0) | (_dkFly ? 8 : 0) | ((_fl & 16) != 0 ? 16 : 0) | (_mode == Mode.IDLE ? 32 : 0)
        | (_mode == Mode.RTB ? 64 : 0) | (SeekerOn ? 128 : 0) | (RpDockOk ? 256 : 0);
    string c = Tag + "\n" + SeekerGroup;
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
// <<< AUX LINK v1.0 <<<
