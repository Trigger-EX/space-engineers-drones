// >>> HELMLINK v1.2 (shared block of the Helm scripts: IGC link to the Staff PB on the same construct, docs/design/control_arch.md s1.3, tools/blocks_sync.py) >>>
// The Helm sends, with TransmissionDistance.CurrentConstruct (never leaves the ship, no secret needed), on "<Ship Tag>.<suffix>" (Ship Tag default SHIP, [Link] in Custom Data):
//   .HB     long tick                                                   heartbeat every "Heartbeat (s)" (0.5): the Staff releases every override when it stays silent for 1.5 s
//   .H2S    MyTuple<int flags, long tgtId, Vector3D tgtPos, Vector3D tgtVel, float tgtRadius, float batt>   5 Hz: flags bit0-2 mode (0 IDLE 1 GUARD 2 HUNT 4 RTB 5 MANUAL 6 ASSIST 7 REACT),
//           bit3 pilot seated, bit4 firing, bit6 dead-man timer armed, bit7 degraded (no thruster or no gyro)
//   .PILOT  MyTuple<int mode, int seated>                               on change
//   .FLT    MyTuple<long id, Vector3D pos, Vector3D vel, double radius, long sender>   the Helm's own target shares and "finished" (radius < 0): the Staff copies them to the fleet channel
//   H2S flags bit 8 (256) = "split Helm" (v1.2): the Staff then takes over the fleet channel, the other fleets' target feeds and damage sensing (docs/design/helm_split.md)
// and listens on .S2H  MyTuple<string cmd, Vector3D rally, double radius>   (cmd = any Command() verb; an EMPTY cmd carries the fleet home point in rally),
//   .TGT  the fleet / feed target tuple (as .FLT), relayed by the Staff,   .DMG  MyTuple<int hits, Vector3D hitDir, float hitDirAge>  hit counter and direction from the Staff's damage scan, 1 Hz (also its liveness beat).
// The Helm counts a hit only when the counter grows while the link was already alive; silence for 3 s after it was heard counts as one hit (a destroyed Staff means damage).
// The link is optional: [Link] Enable (AUTO/ON/OFF) = AUTO switches it on while a PB named "[Staff] ..." is on the construct, so the single-PB ships are untouched.
// Script hooks: HlkCfg() at the end of LoadConfig; HlkBlocks() at the end of GetBlocks; HlkTick() once per Update1 tick after ManTick().
// Uses from the script: Cfg(), Command(), Man, _occ, _mode (Mode.IDLE/GUARD/HUNT/ASSIST/RTB), _tgt (Track), _now, _tick, _firing, _dm, _thr, _gyros, _batFrac.
// BLOCK PREFIX (v1.1, M5): [Link] Block Prefix (default empty = off) limits every block list the script builds to blocks whose name starts with it ("[SH]" on the warships), so the
// blocks of drones that are merged to the ship's cradles (they are blocks of the SAME grid while clamped) are never adopted: Own(b) replaces b.IsSameConstructAs(Me) in the lists.
// Ship Tag also falls back to the [Ship] Tag key of the ship designs' Custom Data.
bool Own(IMyTerminalBlock b) { return b.IsSameConstructAs(Me) && (HlkPre == "" || b.CustomName.StartsWith(HlkPre, StringComparison.OrdinalIgnoreCase)); }
bool HlkOn, _hlkLost; string HlkShip = "SHIP", HlkEn = "AUTO", HlkPre = "", _hlkKey = ""; double HlkHb = 0.5, _hlkDmgT = -99; int _hlkMd = -9, _hlkSeat = -9, _hlkHits; IMyBroadcastListener _hlkIn, _hlkTgt, _hlkDmg;
void HlkCfg()
{
    HlkEn = Cfg("Link", "Enable (AUTO/ON/OFF)", HlkEn).Trim().ToUpperInvariant(); HlkShip = Cfg("Link", "Ship Tag", _ini.Get("Ship", "Tag").ToString(HlkShip)).Trim().ToUpperInvariant(); HlkPre = Cfg("Link", "Block Prefix", HlkPre).Trim(); HlkHb = Math.Max(0.1, Cfg("Link", "Heartbeat (s)", HlkHb));
}
void HlkBlocks()
{
    var l = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(l, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase));
    HlkOn = HlkEn == "ON" || (HlkEn != "OFF" && l.Count > 0);
    string k = HlkOn ? HlkShip : "";
    if (k == _hlkKey) return;
    foreach (var x in new[] { _hlkIn, _hlkTgt, _hlkDmg }) if (x != null) IGC.DisableBroadcastListener(x);
    _hlkIn = k == "" ? null : IGC.RegisterBroadcastListener(k + ".S2H"); _hlkTgt = k == "" ? null : IGC.RegisterBroadcastListener(k + ".TGT"); _hlkDmg = k == "" ? null : IGC.RegisterBroadcastListener(k + ".DMG"); _hlkKey = k;
}
bool FleetV(Vector3D v) { return Math.Abs(v.X) < 1e9 && Math.Abs(v.Y) < 1e9 && Math.Abs(v.Z) < 1e9; }   // false for NaN and absurd values
bool FleetOk(MyTuple<long, Vector3D, Vector3D, double, long> d) { return d.Item1 != Me.CubeGrid.EntityId && FleetV(d.Item2) && FleetV(d.Item3) && d.Item4 < 1e5 && !double.IsNaN(d.Item4); }
void FleetSend(MyTuple<long, Vector3D, Vector3D, double, long> tup) { if (HlkOn) IGC.SendBroadcastMessage(HlkShip + ".FLT", tup, TransmissionDistance.CurrentConstruct); }   // the Staff copies it to the fleet channel
void HlkTick()
{
    if (!HlkOn || _hlkIn == null) return;
    while (_hlkIn.HasPendingMessage)
    {
        var m = _hlkIn.AcceptMessage();
        if (!(m.Data is MyTuple<string, Vector3D, double>)) continue;
        var d = (MyTuple<string, Vector3D, double>)m.Data;
        if (!string.IsNullOrEmpty(d.Item1)) Command(d.Item1); else if (FleetV(d.Item2)) { _dynHome = d.Item2; _dynHomeT = _now; }   // (empty verb: the fleet home point)
    }
    ReadTgt(_hlkTgt);
    while (_hlkDmg.HasPendingMessage)
    {
        var m = _hlkDmg.AcceptMessage();
        if (!(m.Data is MyTuple<int, Vector3D, float>)) continue;
        var d = (MyTuple<int, Vector3D, float>)m.Data;
        if (_now - _hlkDmgT < 3 && d.Item1 > _hlkHits) { _hitAt = _now; _hits++; }   // a hit only when the link was already alive: a recompiled Staff restarts at 0, a recompiled Helm must not read an old count as a hit
        _hlkHits = d.Item1; _hlkDmgT = _now; _hlkLost = false;
        if (d.Item3 < 5 && d.Item2.LengthSquared() > 0.01) { _hitDir = d.Item2; _hitDirT = _now - d.Item3; }
    }
    if (!_hlkLost && _hlkDmgT > -90 && _now - _hlkDmgT > 3) { _hlkLost = true; _hitAt = _now; _hits++; }   // the Staff went silent: count one hit (a destroyed PB means damage)
    var cc = TransmissionDistance.CurrentConstruct;
    if (_tick % Math.Max(1, (int)(HlkHb * 60)) == 3) IGC.SendBroadcastMessage(HlkShip + ".HB", (long)_tick, cc);
    int md = Man ? 5 : _mode == Mode.ASSIST ? 6 : _mode == Mode.REACT ? 7 : (int)_mode, seat = _occ ? 1 : 0;
    if (md != _hlkMd || seat != _hlkSeat) { _hlkMd = md; _hlkSeat = seat; IGC.SendBroadcastMessage(HlkShip + ".PILOT", new MyTuple<int, int>(md, seat), cc); }
    if (_tick % 12 == 5)
    {
        int f = md | seat << 3 | 256 | (_firing ? 16 : 0) | (_dm.Count > 0 && !Man ? 64 : 0) | (_thr.Count == 0 || _gyros.Count == 0 ? 128 : 0);
        var t = _tgt;
        IGC.SendBroadcastMessage(HlkShip + ".H2S", new MyTuple<int, long, Vector3D, Vector3D, float, float>(f, t != null ? t.Id : 0, t != null ? t.P(_now) : Vector3D.Zero, t != null ? t.V(_now) : Vector3D.Zero, t != null ? (float)t.Radius : 0, (float)_batFrac), cc);
    }
}
// <<< HELMLINK v1.2 <<<
