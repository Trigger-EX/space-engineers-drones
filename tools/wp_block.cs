// >>> WEAKPOINT RELAY v1.1 (shared block: identical in every drone Staff PB, see docs/design/hive_weakpoint.md and docs/hive-protocol.md) >>>
// Fleet side of the shared breach point. The Flight PB sends its own findings on <Flight>.WPF; this PB puts them on the fleet channel <ch>_WP,
// collects the findings of the whole wing, and sends the best hole for the Flight's current target back on <Flight>.WPT (once a second).
//   <ch>_WP  MyTuple<long target, Vector3D s, Vector3D d, Vector3D info, long senderGrid, int flags>
//     s = hull entry and d = drill direction, both in the TARGET frame; info = (hole depth m, vital depth hint m or -1, age s)
//     flags: bits 0-2 kind (0 camera breach point, 1 PowerSystems, 2 Weapons, 3 Propulsion, 4 other), 8 seeker found it, 16 block position
//     from the game, 32 DONE (everybody drops the hole), 64 STALL (that sender gave up), 128 ADOPTED (the sender took it from the wing and its own ray confirmed it)
//     Staff -> Flight (.WPT): bits 8-11 = number of drones that support the hole.
// A drone sends only its OWN confirmed findings; a received hole is never forwarded (no echo). Holes within 4 m of each other are one hole; the best
// one wins (seeker kind by "Weakpoint Priority", then supporters, then depth, then the lowest grid id), so every drone picks the same hole.
// [Fleet] keys: Weakpoint Priority=1,3,2 (power, propulsion, weapons)   Weakpoint Expire (s)=20
// v1.1 (docs/design/survivability.md): the drone's OWN seeker turret is read by the Staff (STAFF SEARCH -> WpSeek): that entry (map key ~own grid id,
// internal flag 4096, kept 3 s) ranks above every other hole, goes to the wing like an own finding, and reaches the Flight with .WPT flag 4096 (the Flight
// takes it as its own seeker point, BreachSet(s, d, true)); .WPT is sent twice a second while it is fresh, else once a second.
// Script hooks: WpStep() in Tick after Relay(); _sb.Append(WpStatus()) in Display(). Uses: _fk, _flChan, _tgtId, _tick, _now, _f2sTick.
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
void WpCfg()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    const string S = "Fleet";
    if (!ini.ContainsKey(S, "Weakpoint Priority")) ini.Set(S, "Weakpoint Priority", "1,3,2");
    if (!ini.ContainsKey(S, "Weakpoint Expire (s)")) ini.Set(S, "Weakpoint Expire (s)", 20.0);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    var l = new List<int>();
    foreach (var p in ini.Get(S, "Weakpoint Priority").ToString("1,3,2").Split(',')) { int k; if (int.TryParse(p.Trim(), out k)) l.Add(k); }
    _wpPri = l.ToArray(); _wpExp = Math.Max(3, ini.Get(S, "Weakpoint Expire (s)").ToDouble(20));
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
// own seeker finding (STAFF SEARCH): into the map as the top-ranked entry, to the wing on a change or every 2 s
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
// <<< WEAKPOINT RELAY v1.1 <<<
