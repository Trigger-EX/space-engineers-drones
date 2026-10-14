// >>> HARASSER LINK v1.0 (shared block of the Ballista Helm: IGC link to the Lookout module of the Spotter PB, docs/design/ballista_harasser_defence.md, tools/blocks_sync.py) >>>
// A small hostile craft that loiters and shoots (an AI drone launched by an enemy warship) is neither a kamikaze nor a wreck. The Spotter PB (same construct, name "[Spotter]...") finds it and
// picks a velocity bias; this block applies it, so the Helm stays the only writer of thrusters and gyros. Key "HDEF<this PB's EntityId>", TransmissionDistance.CurrentConstruct (never leaves the ship).
// The Helm sends (only while a "[Spotter]" PB is on the construct):
//   .H2A  MyTuple<int flags, Vector3D hitDir, double hitAge, double standoff, long tgtId, Vector3D tgtPos>   every 10 ticks: flags bit1 under fire, bit4 MANUAL, bit5 target radius >= 25,
//         bits 8-11 link version (1), bits 16-30 the hit counter; hitDir is zero when no direction is known
//   .HTK  MyTuple<long id, Vector3D pos, Vector3D vel, float radius, float age, int flags>   every 20 ticks, up to 4 contacts of radius <= 15 m seen in the last second
//         (flags bit0 = incoming kamikaze: that stays the KAMIKAZE DEFENCE's job)
// and listens on .A2H  MyTuple<Vector3D bias, Vector3D up, int flags, float ttl, long id1, long id2>   flags bit0 bias valid, bit1 drop the random jink, bit2 up request valid, bit3 a harasser
//   is tracked (else the bias answers fire from an unseen bearing); id1, id2 = the harassers (the Helm does not make one of them its target while a larger one is about, and does not copy its velocity)
// Everything times out after ttl (0.6 s): a silent or missing Spotter changes nothing, and ships without a "[Spotter]" PB are unaffected.
// Script hooks: HdTick() after HlkTick(); "+ HdScore(t)" in the target score; tv zero when HdHar(_tgt.Id); "&& !HdJ()" on the jink term; want = HdWant(want, cap) right before the KzWant line;
// HdUp() instead of Vector3D.Zero as the up of the two Aim() calls of Control() outside gravity; _sb.Append(HdStatus()) after KzStatus().
// Uses from the script: _contacts, _now, _tick, _hitDir, _hitDirT, _hits, UnderFire, Man, _tgt, Standoff, FleetV(), KzLive(), Track, Me, GridTerminalSystem, IGC.
IMyBroadcastListener _hdL; string _hdKey = ""; Vector3D _hdB, _hdU; int _hdF; double _hdT = -99, _hdTtl = 0.6; long _hdI1, _hdI2; bool _hdSp;
void HdTick()
{
    string k = "HDEF" + Me.EntityId;
    if (k != _hdKey) { _hdKey = k; _hdL = IGC.RegisterBroadcastListener(k + ".A2H"); }
    while (_hdL.HasPendingMessage)
    {
        var m = _hdL.AcceptMessage();
        if (!(m.Data is MyTuple<Vector3D, Vector3D, int, float, long, long>)) continue;
        var d = (MyTuple<Vector3D, Vector3D, int, float, long, long>)m.Data;
        if (!FleetV(d.Item1) || !FleetV(d.Item2)) continue;
        _hdB = d.Item1; _hdU = d.Item2; _hdF = d.Item3; _hdTtl = MathHelper.Clamp(d.Item4, 0.2, 2); _hdI1 = d.Item5; _hdI2 = d.Item6; _hdT = _now;
    }
    if (_tick % 300 == 1)
    {
        var l = new List<IMyProgrammableBlock>();
        GridTerminalSystem.GetBlocksOfType(l, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Spotter]", StringComparison.OrdinalIgnoreCase));
        _hdSp = l.Count > 0;
    }
    if (!_hdSp) return;
    var cc = TransmissionDistance.CurrentConstruct;
    if (_tick % 10 == 2)
    {
        var g = _tgt; bool hd = _now - _hitDirT < 5 && _hitDir.LengthSquared() > 0.01;
        int f = (UnderFire ? 2 : 0) | (Man ? 16 : 0) | (g != null && g.Radius >= 25 ? 32 : 0) | 256 | ((_hits & 0x7FFF) << 16);
        IGC.SendBroadcastMessage(k + ".H2A", new MyTuple<int, Vector3D, double, double, long, Vector3D>(f, hd ? _hitDir : Vector3D.Zero, hd ? _now - _hitDirT : 99, Standoff, g != null ? g.Id : 0, g != null ? g.P(_now) : Vector3D.Zero), cc);
    }
    if (_tick % 20 == 7)
    {
        int n = 0;
        foreach (var t in _contacts.Values)
        {
            if (t.Radius > 15 || _now - t.Seen > 1 || n >= 4) continue;
            n++; IGC.SendBroadcastMessage(k + ".HTK", new MyTuple<long, Vector3D, Vector3D, float, float, int>(t.Id, t.P(_now), t.V(_now), (float)t.Radius, (float)(_now - t.Seen), KzLive(t.Id) ? 1 : 0), cc);
        }
    }
}
bool HdOn { get { return _now - _hdT < _hdTtl && (_hdF & 1) != 0; } }
bool HdJ() { return HdOn && (_hdF & 2) != 0; }
bool HdHar(long id) { return id != 0 && _now - _hdT < 1.5 && (_hdF & 8) != 0 && (id == _hdI1 || id == _hdI2); }
double HdScore(Track t) { return HdHar(t.Id) ? 3000 : 0; }
Vector3D HdUp() { return HdOn && (_hdF & 4) != 0 ? _hdU : Vector3D.Zero; }
// adds the bias to the commanded velocity; over the speed cap the rest of the command is shrunk and the bias is kept whole (same rule as KzWant's break)
Vector3D HdWant(Vector3D want, double cap)
{
    if (!HdOn) return want;
    double bl = _hdB.Length(), room = Math.Max(cap - bl, 0);
    if (bl < 0.1) return want;
    if (want.LengthSquared() > room * room) want = want.LengthSquared() > 1e-6 ? Vector3D.Normalize(want) * room : want;
    return want + _hdB;
}
string HdStatus() { return HdOn ? ((_hdF & 8) != 0 ? "!! HARASSER (evading)\n" : "!! FIRE FROM AN UNSEEN BEARING (evading)\n") : ""; }
// <<< HARASSER LINK v1.0 <<<
