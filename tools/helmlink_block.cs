// >>> HELMLINK v1.0 (shared block of the Helm scripts: IGC link to the Staff PB on the same construct, docs/design/control_arch.md s1.3, tools/blocks_sync.py) >>>
// The Helm sends, with TransmissionDistance.CurrentConstruct (never leaves the ship, no secret needed), on "<Ship Tag>.<suffix>" (Ship Tag default SHIP, [Link] in Custom Data):
//   .HB     long tick                                                   heartbeat every "Heartbeat (s)" (0.5): the Staff releases every override when it stays silent for 1.5 s
//   .H2S    MyTuple<int flags, long tgtId, Vector3D tgtPos, Vector3D tgtVel, float tgtRadius, float batt>   5 Hz: flags bit0-2 mode (0 IDLE 1 GUARD 2 HUNT 4 RTB 5 MANUAL 6 ASSIST),
//           bit3 pilot seated, bit4 firing, bit6 dead-man timer armed, bit7 degraded (no thruster or no gyro)
//   .PILOT  MyTuple<int mode, int seated>                               on change
// and listens on .S2H  MyTuple<string cmd, Vector3D rally, double radius>   (cmd = any Command() verb; rally / radius are for the wing, unused here).
// The link is optional: [Link] Enable (AUTO/ON/OFF) = AUTO switches it on while a PB named "[Staff] ..." is on the construct, so the single-PB ships are untouched.
// Script hooks: HlkCfg() at the end of LoadConfig; HlkBlocks() at the end of GetBlocks; HlkTick() once per Update1 tick after ManTick().
// Uses from the script: Cfg(), Command(), Man, _occ, _mode (Mode.IDLE/GUARD/HUNT/ASSIST/RTB), _tgt (Track), _now, _tick, _firing, _dm, _thr, _gyros, _batFrac.
bool HlkOn; string HlkShip = "SHIP", HlkEn = "AUTO", _hlkKey = ""; double HlkHb = 0.5; int _hlkMd = -9, _hlkSeat = -9; IMyBroadcastListener _hlkIn;
void HlkCfg()
{
    HlkEn = Cfg("Link", "Enable (AUTO/ON/OFF)", HlkEn).Trim().ToUpperInvariant(); HlkShip = Cfg("Link", "Ship Tag", HlkShip).Trim().ToUpperInvariant(); HlkHb = Math.Max(0.1, Cfg("Link", "Heartbeat (s)", HlkHb));
}
void HlkBlocks()
{
    var l = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(l, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase));
    HlkOn = HlkEn == "ON" || (HlkEn != "OFF" && l.Count > 0);
    string k = HlkOn ? HlkShip : "";
    if (k == _hlkKey) return;
    if (_hlkIn != null) IGC.DisableBroadcastListener(_hlkIn);
    _hlkIn = k == "" ? null : IGC.RegisterBroadcastListener(k + ".S2H"); _hlkKey = k;
}
void HlkTick()
{
    if (!HlkOn || _hlkIn == null) return;
    while (_hlkIn.HasPendingMessage)
    {
        var m = _hlkIn.AcceptMessage();
        if (m.Data is MyTuple<string, Vector3D, double>) { string c = ((MyTuple<string, Vector3D, double>)m.Data).Item1; if (!string.IsNullOrEmpty(c)) Command(c); }
    }
    var cc = TransmissionDistance.CurrentConstruct;
    if (_tick % Math.Max(1, (int)(HlkHb * 60)) == 3) IGC.SendBroadcastMessage(HlkShip + ".HB", (long)_tick, cc);
    int md = Man ? 5 : _mode == Mode.ASSIST ? 6 : (int)_mode, seat = _occ ? 1 : 0;
    if (md != _hlkMd || seat != _hlkSeat) { _hlkMd = md; _hlkSeat = seat; IGC.SendBroadcastMessage(HlkShip + ".PILOT", new MyTuple<int, int>(md, seat), cc); }
    if (_tick % 12 == 5)
    {
        int f = md | seat << 3 | (_firing ? 16 : 0) | (_dm.Count > 0 && !Man ? 64 : 0) | (_thr.Count == 0 || _gyros.Count == 0 ? 128 : 0);
        var t = _tgt;
        IGC.SendBroadcastMessage(HlkShip + ".H2S", new MyTuple<int, long, Vector3D, Vector3D, float, float>(f, t != null ? t.Id : 0, t != null ? t.P(_now) : Vector3D.Zero, t != null ? t.V(_now) : Vector3D.Zero, t != null ? (float)t.Radius : 0, (float)_batFrac), cc);
    }
}
// <<< HELMLINK v1.0 <<<
