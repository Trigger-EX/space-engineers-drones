// >>> SPLIT LINK v1.1 (shared block: identical in every Flight half of a two-PB drone, synced by tools/split_sync.py, see docs/design/wasp_split.md) >>>
// Flight side of the Flight/Staff link; the Staff PB (scripts/<Drone>StaffAI.cs) has the other end. Everything that is not on the hot path lives there:
// FLEET LINK, FOLLOW / SATELLITE geometry, LAUNCH (take-off burn), the status screen. IGC key "WSPL" + this PB's EntityId, TransmissionDistance.CurrentConstruct.
// Script hooks: LkInit() with the other listener setup; ReadIGC(); LkRead(); LkSend() in the tick; "if (!LkBurn && !AiStep()) Control(dt)"; "if (LkBurn) return;" first in Control();
// LkWantOk/_lkWant/LkFar/LkBack/LkEngage/_lkOwner/_lkOwnerP in the FOLLOW code; ReadTgt(_lkTgt); LkAmmo() = the script's ammo count (0-254, 255 unknown).
// Uses: _now, _tick, _mode, _firing, _aiOn, UnderFire, LockStale, _tgt, _aimErrDeg, _fireWinDeg, _batFrac, _gateWhy, _dynHome, _dynHomeT, MaxSpeed, ParseMode, Command.
// WEAKPOINT: .WPT (Staff -> Flight) feeds the BREACH block's _wp* slot, WpSend() carries its .WPF (Flight -> Staff); the relay itself is the Staff PB's WEAKPOINT RELAY block.
string _lkK = ""; IMyBroadcastListener _lkFol, _lkCmd, _lkTgt, _lkWpt;
Vector3D _lkWant, _lkOwnerP, _lkOwnerV; double _lkEng = 800, _lkLeash = 1200, _lkT = -99, _aBrake = 5; int _lkFl, _lkMd = -9; bool _lkOwner;
bool LkAlive { get { return _now - _lkT < 1.0; } }                       // the Staff PB talks (a FOL at least every 0.5 s)
bool LkBurn { get { return LkAlive && (_lkFl & 4) != 0; } }              // Staff flies the take-off burn: Flight lets go of the ship
bool LkWantOk { get { return LkAlive && (_lkFl & 1) != 0; } }
double LkEngage { get { return LkAlive ? _lkEng : 800; } }
void LkInit()
{
    _lkK = "WSPL" + Me.EntityId;
    _lkFol = IGC.RegisterBroadcastListener(_lkK + ".FOL"); _lkCmd = IGC.RegisterBroadcastListener(_lkK + ".S2F"); _lkTgt = IGC.RegisterBroadcastListener(_lkK + ".TGT");
    _lkWpt = IGC.RegisterBroadcastListener(_lkK + ".WPT");   // best fleet weakpoint for my target (WEAKPOINT RELAY, Staff PB) -> the _wp* slot of the BREACH block
}
void WpSend(MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> tup) { if (LkAlive) IGC.SendBroadcastMessage(_lkK + ".WPF", tup, TransmissionDistance.CurrentConstruct); }
bool LkFar(Vector3D me) { double l = LkAlive ? _lkLeash : 1200; return _lkOwner && Vector3D.DistanceSquared(me, _lkOwnerP) > l * l; }
Vector3D LkBack(Vector3D me) { Vector3D d = _lkOwnerP - me; return _lkOwnerV + (d.LengthSquared() > 1 ? Vector3D.Normalize(d) * MaxSpeed : Vector3D.Zero); }
void LkRead()
{
    while (_lkFol.HasPendingMessage)
    {
        var m = _lkFol.AcceptMessage();
        if (!(m.Data is MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>)) continue;
        var d = (MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>)m.Data;
        _lkWant = d.Item1; _lkFl = d.Item6; _lkT = _now; _lkEng = Math.Max(50, d.Item5.X); _lkLeash = Math.Max(_lkEng, d.Item5.Y);
        if ((_lkFl & 1) != 0) { _lkOwner = true; _lkOwnerP = d.Item2; _lkOwnerV = d.Item3; }
        if ((_lkFl & 8) != 0) { _dynHome = d.Item4; _dynHomeT = _now; }
    }
    while (_lkWpt.HasPendingMessage)
    {
        var m = _lkWpt.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)) continue;
        var w = (MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data;
        if (!FleetV(w.Item2) || !FleetV(w.Item3) || !FleetV(w.Item4)) continue;
        _wpId = w.Item1; _wpS = w.Item2; _wpD = w.Item3; _wpI = w.Item4; _wpF = w.Item6; _wpT = _now;
    }
    while (_lkCmd.HasPendingMessage)
    {
        var m = _lkCmd.AcceptMessage();
        if (!(m.Data is string)) continue;
        string v = ((string)m.Data).Trim().ToLowerInvariant();
        if (ParseMode(v.Split(' ')[0], (Mode)(-1)) == _mode) continue;   // a mode verb that changes nothing (a PB argument reaches both PBs)
        Command(v);
    }
}
void LkSend()
{
    int md = (int)_mode;
    if (md == _lkMd && _tick % 12 != 5) return;
    _lkMd = md;
    int f = md | (_firing ? 8 : 0) | (UnderFire ? 16 : 0) | (LockStale ? 32 : 0) | (_aiOn ? 64 : 0) | (LkAmmo() < 0 ? 255 : Math.Min(254, LkAmmo())) << 8;
    var t = _tgt;
    IGC.SendBroadcastMessage(_lkK + ".F2S", new MyTuple<int, long, Vector3D, Vector3D, float, string>(f, t != null ? t.Id : 0, t != null ? t.P(_now) : Vector3D.Zero,
        new Vector3D(_aimErrDeg, _fireWinDeg, _aBrake), (float)_batFrac, LkGate()), TransmissionDistance.CurrentConstruct);
}

// FLEET LINK stubs: the fleet channel is the Staff PB's (WaspStaffAI.cs); target shares go to it on .FLT, fleet targets come back on .TGT
bool FleetV(Vector3D v) { return Math.Abs(v.X) < 1e9 && Math.Abs(v.Y) < 1e9 && Math.Abs(v.Z) < 1e9; }
bool FleetOk(MyTuple<long, Vector3D, Vector3D, double, long> d) { return FleetV(d.Item2) && FleetV(d.Item3) && d.Item4 < 1e5 && !double.IsNaN(d.Item4); }
void FleetSend(MyTuple<long, Vector3D, Vector3D, double, long> tup) { if (LkAlive) IGC.SendBroadcastMessage(_lkK + ".FLT", tup, TransmissionDistance.CurrentConstruct); }
// <<< SPLIT LINK <<<
