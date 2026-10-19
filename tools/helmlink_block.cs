// >>> HELMLINK v1.3 (shared block of the Helm scripts: IGC link to the Staff PB on the same construct, docs/design/control_arch.md s1.3, tools/blocks_sync.py) >>>
// The Helm sends, with TransmissionDistance.CurrentConstruct (never leaves the ship, no secret needed), on "<Ship Tag>.<suffix>" (Ship Tag default SHIP, [Link] in Custom Data):
//   .HB     long tick                                                   heartbeat every "Heartbeat (s)" (0.5): the Staff releases every override when it stays silent for 1.5 s
//   .H2S    MyTuple<int flags, long tgtId, Vector3D tgtPos, Vector3D tgtVel, float tgtRadius, float batt>   5 Hz: flags bit0-2 mode (0 IDLE 1 GUARD 2 HUNT 4 RTB 5 MANUAL 6 ASSIST 7 REACT),
//           bit3 pilot seated, bit4 firing, bit6 dead-man timer armed, bit7 degraded (no thruster or no gyro)
//   .PILOT  MyTuple<int mode, int seated>                               on change
//   .FLT    MyTuple<long id, Vector3D pos, Vector3D vel, double radius, long sender>   the Helm's own target shares and "finished" (radius < 0): the Staff copies them to the fleet channel
//   H2S flags bit 8 (256) = "split Helm" (v1.2): the Staff then takes over the fleet channel, the other fleets' target feeds and damage sensing (docs/design/helm_split.md)
//   v1.3 (docs/design/flight_split.md 4.3): H2S bit 9 (512) CFG WANT (CONFIG LINK CfWant: the Staff answers with ".CFG"), bit 10 (1024) AI ASSIST (the runtime AiAssist:
//   the Staff runs the AI-block setup AiGateFind while it is set), bit 11 (2048) SCAN (ScanOn: the Staff casts the camera auto-scan rays, H7)
//   .HUD    MyTuple<int flags, Vector3D tgt(seenAge, kzTgo, kzDist), Vector3D lead(leadT, pipR, pipU), Vector3D win(aimErr, fireWin, AI blocks), Vector3D pwr(batt, ammo, thrust), string>
//           with Display (every 10 ticks while MANUAL, else 30): the Staff writes the cockpit HUD page from it (Cut 2; target range and speed from H2S) and shows the AI /
//           kamikaze lines on its own screen.
//           flags: bit0 MANUAL, 1 seat taken (hold), 2 dampeners, 3 dead-man armed, 4 FIRE, 5 has a dead-man timer, 6-7 assist level, 8-9 primary battery, 10 free (target: H2S),
//           11 AI blocks fly, 12 own search (AI gate), 13 AI alarm, 14 AI ok (_agLive), 15 next kamikaze is at a mate, 16-23 contacts (max 255), 24-27 kamikazes (max 15),
//           28-30 kamikazes at me (max 7). string = mode|primary class|error|first contact
//   .MNU    int 0   a Run of this PB without argument while the Staff is alive (the Staff steps the Run Menu and sends the verb back on .S2H)
//   .LCK    int lock counter   "lock": the Staff casts the ray from the forward camera and answers on .LKR
//   .LST    MyTuple<Vector3D pos, Vector3D vel, double age, double radius>   the target just lost (NoteLost): the Staff's auto-scan puts every 2nd ray round it for 25 s
// and on .LKR  MyTuple<long id, int f, Vector3D hit, Vector3D vel, MyTuple<Vector3D, Vector3D> box, MyTuple<Vector3D, Vector3D> ori(fwd, up)>  (the .SCN tuple, flight_split 4.1: f bits 0-7
//   entity type, 8-15 relationship, 16 has hit, 20-22 source 1 SCAN / 4 LOCK, 24-27 result 0 ok / 1 no forward camera / 2 nothing hit (charge in hit.X) / 3 friendly): a LOCK with
//   result 0 becomes the target, a SCAN hit goes through Valid() (FaSee sees friends too) into the contacts. On a run gap queued hits are stale: harmless (AddContact keeps the newest).
// and listens on .S2H  MyTuple<string cmd, Vector3D rally, double radius>   (cmd = any Command() verb; an EMPTY cmd carries the fleet home point in rally),
//   .TGT  the fleet / feed target tuple (as .FLT), relayed by the Staff,   .DMG  MyTuple<int hits, Vector3D hitDir, float hitDirAge>  hit counter and direction from the Staff's damage scan, 1 Hz (also its liveness beat).
// The Helm counts a hit only when the counter grows while the link was already alive; silence for 3 s after it was heard counts as one hit (a destroyed Staff means damage).
// The link is optional: [Link] Enable (AUTO/ON/OFF) = AUTO switches it on while a PB named "[Staff] ..." is on the construct, so the single-PB ships are untouched.
// Script hooks: HlkCfg() at the end of LoadConfig (Get-only: [Link] stays in this PB's Custom Data and is read, never written); HlkBlocks() at the end of GetBlocks; HlkTick() once per Update1 tick after ManTick(); HlkHud() in Display;
// HlkMenu() for a Run without argument; HlkLock() for "lock"; bool ScanOn (the auto-scan condition, H2S bit 11); the .LST send in NoteLost().
// Uses from the script: _ini, Command(), Man, _occ, _hold, _mode (Mode.IDLE/GUARD/HUNT/ASSIST/RTB), _tgt (Track), _now, _tick, _firing, _dm, _thr, _gyros, _batFrac, _ammoFrac,
// _powerScale, _contacts, _prim, _bat, _err, _fc, _ai, _aiOn, _agT0, _agLive, OwnAlert, AiAssist, CfWant, KzOn, _kz, _kzMine, _leadT, _pipR, _pipU, _aimErrDeg, _fireWinDeg, ManMode(), AddContact(), Valid().
// BLOCK PREFIX (v1.1, M5): [Link] Block Prefix (default empty = off) limits every block list the script builds to blocks whose name starts with it ("[SH]" on the warships), so the
// blocks of drones that are merged to the ship's cradles (they are blocks of the SAME grid while clamped) are never adopted: Own(b) replaces b.IsSameConstructAs(Me) in the lists.
// Ship Tag also falls back to the [Ship] Tag key of the ship designs' Custom Data.
bool Own(IMyTerminalBlock b) { return b.IsSameConstructAs(Me) && (HlkPre == "" || b.CustomName.StartsWith(HlkPre, StringComparison.OrdinalIgnoreCase)); }
bool HlkOn, _hlkLost; string HlkShip = "SHIP", HlkEn = "AUTO", HlkPre = "", _hlkKey = ""; double HlkHb = 0.5, _hlkDmgT = -99; int _hlkMd = -9, _hlkSeat = -9, _hlkHits, _hlkLck; IMyBroadcastListener _hlkIn, _hlkTgt, _hlkDmg, _hlkLkr;
void HlkCfg()
{
    const string L = "Link";
    HlkEn = _ini.Get(L, "Enable (AUTO/ON/OFF)").ToString(HlkEn).Trim().ToUpperInvariant(); HlkShip = _ini.Get(L, "Ship Tag").ToString(_ini.Get("Ship", "Tag").ToString(HlkShip)).Trim().ToUpperInvariant(); HlkPre = _ini.Get(L, "Block Prefix").ToString(HlkPre).Trim(); HlkHb = Math.Max(0.1, _ini.Get(L, "Heartbeat (s)").ToDouble(HlkHb));
}
void HlkBlocks()
{
    var l = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(l, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase));
    HlkOn = HlkEn == "ON" || (HlkEn != "OFF" && l.Count > 0);
    string k = HlkOn ? HlkShip : "";
    if (k == _hlkKey) return;
    foreach (var x in new[] { _hlkIn, _hlkTgt, _hlkDmg, _hlkLkr }) if (x != null) IGC.DisableBroadcastListener(x);
    _hlkIn = k == "" ? null : IGC.RegisterBroadcastListener(k + ".S2H"); _hlkTgt = k == "" ? null : IGC.RegisterBroadcastListener(k + ".TGT"); _hlkDmg = k == "" ? null : IGC.RegisterBroadcastListener(k + ".DMG"); _hlkLkr = k == "" ? null : IGC.RegisterBroadcastListener(k + ".LKR"); _hlkKey = k;
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
    while (_hlkLkr.HasPendingMessage)
    {   // the Staff's rays: lock (H6; result 0 = a grid that is not friendly, the Staff shows the failures on the HUD page) and auto-scan hits (H7)
        var m = _hlkLkr.AcceptMessage();
        if (!(m.Data is MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>>)) continue;
        var d = (MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>>)m.Data; var bx = new BoundingBoxD(d.Item5.Item1, d.Item5.Item2); int f = d.Item2;
        if (f >> 24 != 0) continue;
        var e = new MyDetectedEntityInfo(d.Item1, "", (MyDetectedEntityType)(f & 255), d.Item3, MatrixD.CreateWorld(bx.Center, d.Item6.Item1, d.Item6.Item2), (Vector3)d.Item4, (MyRelationsBetweenPlayerAndBlock)(f >> 8 & 255), bx, 1);   // (TimeStamp 0 would read as IsEmpty())
        if ((f & 4194304) != 0) { _err = ""; _tgt = AddContact(e, "LOCK"); } else if (Valid(e)) AddContact(e, "SCAN");
    }
    var cc = TransmissionDistance.CurrentConstruct;
    if (_tick % Math.Max(1, (int)(HlkHb * 60)) == 3) IGC.SendBroadcastMessage(HlkShip + ".HB", (long)_tick, cc);
    int md = Man ? 5 : _mode == Mode.ASSIST ? 6 : _mode == Mode.REACT ? 7 : (int)_mode, seat = _occ ? 1 : 0;
    if (md != _hlkMd || seat != _hlkSeat) { _hlkMd = md; _hlkSeat = seat; IGC.SendBroadcastMessage(HlkShip + ".PILOT", new MyTuple<int, int>(md, seat), cc); }
    if (_tick % 12 == 5)
    {
        int f = md | seat << 3 | 256 | (CfWant ? 512 : 0) | (AiAssist ? 1024 : 0) | (ScanOn ? 2048 : 0) | (_firing ? 16 : 0) | (_dm.Count > 0 && !Man ? 64 : 0) | (_thr.Count == 0 || _gyros.Count == 0 ? 128 : 0);
        var t = _tgt;
        IGC.SendBroadcastMessage(HlkShip + ".H2S", new MyTuple<int, long, Vector3D, Vector3D, float, float>(f, t != null ? t.Id : 0, t != null ? t.P(_now) : Vector3D.Zero, t != null ? t.V(_now) : Vector3D.Zero, t != null ? (float)t.Radius : 0, (float)_batFrac), cc);
    }
}
bool HlkUp { get { return HlkOn && _now - _hlkDmgT < 3; } }   // the Staff answers (.DMG is its 1 Hz beat)
void HlkMenu() { if (HlkUp) IGC.SendBroadcastMessage(HlkShip + ".MNU", 0, TransmissionDistance.CurrentConstruct); else Command("pilot"); }
void HlkLock() { if (HlkUp) IGC.SendBroadcastMessage(HlkShip + ".LCK", ++_hlkLck, TransmissionDistance.CurrentConstruct); else _err = "lock: no Staff PB"; }
void HlkHud()
{
    if (!HlkOn || _rc == null) return;
    var t = _tgt; var k = _kz.Count > 0 ? _kz[0] : null;
    IGC.SendBroadcastMessage(HlkShip + ".HUD", new MyTuple<int, Vector3D, Vector3D, Vector3D, Vector3D, string>((Man ? 1 : 0) | (_hold ? 2 : 0) | (_rc.DampenersOverride ? 4 : 0) | (_dm.Count > 0 ? Man ? 32 : 40 : 0)
        | (_fireWinDeg > 0 && _aimErrDeg < _fireWinDeg ? 16 : 0) | _asLvl << 6 | _prim << 8 | (_aiOn ? 2048 : 0) | (_agT0 >= 0 ? 4096 : 0) | (OwnAlert ? 8192 : 0) | (_agLive ? 16384 : 0)
        | (k != null && !k.Mine ? 32768 : 0) | Math.Min(255, _contacts.Count) << 16 | (KzOn ? Math.Min(15, _kz.Count) << 24 | Math.Min(7, _kzMine) << 28 : 0),
        new Vector3D(t != null ? _now - t.Seen : 0, k != null ? k.Tgo : 0, k != null ? k.Dist : 0), new Vector3D(_leadT, _pipR, _pipU), new Vector3D(_aimErrDeg, _fireWinDeg, _ai.Count), new Vector3D(_batFrac, _ammoFrac, _powerScale),
        ManMode() + "|" + _bat[_prim].Cls + "|" + _err + "|" + _fc), TransmissionDistance.CurrentConstruct);
}
// <<< HELMLINK v1.3 <<<
