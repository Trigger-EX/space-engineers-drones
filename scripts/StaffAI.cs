// =====================================================================
//  STAFF AI  v1.0  - the Staff script of the warships (corvette, frigate, dreadnought; also a second PB on the Shrike test ship)
//  For Space Engineers (vanilla). Paste into a Programmable Block named "[Staff] ..." on the same construct as the "[Helm] ..." PB (ShrikeAI).
//  Update10 + an IGC callback on the heartbeat. Staff never flies the ship and never writes an override; it only watches, talks and RELEASES.
//
//  JOBS (docs/design/control_arch.md s1.2, s2.6, s4.1)
//   * HEARTBEAT WATCH (dead-man): the Helm sends "<Ship Tag>.HB" twice a second over IGC (TransmissionDistance.CurrentConstruct). When it stays silent for
//     "Trip Time" (1.5 s) after the first beat, Staff releases every override it can see: GyroOverride off, Pitch/Yaw/Roll 0, ThrustOverridePercentage 0, once:
//     dampeners on, the AI blocks tagged [AI] off, Shoot_Off on the fixed guns; then every second again the gyros and thrusters, and the cockpit HUD says "HELM LOST". It only RELEASES, so it
//     is correct in every mode (AUTO, MANUAL, a pilot aboard). The beats resuming ends the trip. (Hard layer under it: the Timer "Deadman", ShrikeAI / s2.6.)
//   * MOTHERSHIP LINK (HornetMothership v1.1 for a ship): "<Type>_HOME" (the ship's centre of mass, or the grid centre without a controller), "<Type>_HOMER" (hull
//     radius) and the fleet channel HOME, and the enemies the ship's turrets / turret controllers track, plus the Helm's selected target (.H2S), as "<Type>_TGT"
//     and on the fleet channel (FLEET block, send side). Types = [Wing] Types (default HORNET).
//   * COMMANDS (PB argument, toolbar): wing <cmd> (every type's _CMD and the fleet channel), fleet <cmd>, release / recall / doors (STUBS until M5: they answer
//     "not yet"), reload, status; every other word is forwarded to the Helm over IGC (.S2H): hunt, guard, rtb, manual, auto, assist hud, lock, next, unlock, ...
//     Run with NO argument: menu, each press steps "Run Menu", the highlighted item runs "Run Menu Wait" s after the last press (cockpit-toolbar fallback, K3).
//   * DISPLAYS: the PB's own screen, the cockpit surface "Cockpit Surface" (default 1, -1 off) and any LCD named "...Staff Display...": link state, mode, seat,
//     target, power, trips; "Helm HUD Surface" (default 0) of the cockpit gets the HELM LOST text while the Helm is silent.
//  CUSTOM DATA  [Staff] Ship Tag (SHIP, the Helm's [Link] Ship Tag), Trip Time (s) 1.5, Watch Helm, Cockpit Surface, Helm HUD Surface, Run Menu, Run Menu Wait,
//   Display Name; [Wing] Types; [Fleet] as in the drones (send side only).
//  NOT MEASURED IN THE GAME: IGC latency CurrentConstruct (K8), the number of IGC callback runs a PB survives, Update10 timing of the PB (the trip is checked
//   every 10 ticks, so it lands up to 0.17 s after the 1.5 s).
// =====================================================================

const string VER = "1.0";
MyIni _ini = new MyIni();
string Ship = "SHIP", MenuItems = "wing satellite,wing follow,wing hunt,wing rtb,release 1,release all,recall,doors open,doors close", DispName = "staff display";
string[] Types = { "HORNET" };
double Trip = 1.5, MenuWait = 2; bool Watch = true; int CockSurf = 1, HudSurf = 0;

List<IMyGyro> _gyros = new List<IMyGyro>(); List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>(); List<IMyShipController> _ctl = new List<IMyShipController>();
List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>(); List<IMyTurretControlBlock> _ctcs = new List<IMyTurretControlBlock>();
List<IMyTextPanel> _lcds = new List<IMyTextPanel>(); List<IMyFunctionalBlock> _ai = new List<IMyFunctionalBlock>();
IMyBroadcastListener _hb, _h2s, _pil;
double _t, _hbT = -1, _h2sT = -99, _lostAt, _relT = -99, _menuT; int _hbN, _trips, _tick, _menuI, _md, _seat, _fl; bool _lost, _menuOn;
long _tId; Vector3D _tPos, _tVel; float _tRad, _batt; string _note = "";
StringBuilder _sb = new StringBuilder();

// FLEET LINK (shared block, send side): FleetLoad(false) in the constructor, FleetSendHome / FleetSendCmd / FleetSend below.

// >>> FLEET LINK v1.0 (shared block: identical in every drone script and carrier relay, see docs/hive-protocol.md) >>>
// A common IGC channel for ALL drone types, used IN ADDITION to the per-type tags (HORNET_TGT, GNAT_CMD, ...), which are untouched.
//   <ch>_TGT   MyTuple<long id, Vector3D pos, Vector3D vel, double radius, long sender>   same tuple as <TAG>_TGT; radius < 0 = finished
//   <ch>_HOME  Vector3D (rally point)        <ch>_CMD  string (one command, e.g. "hunt", "rtb", "launch")
// <ch> = Channel, plus "_" + Secret when a secret is set (IGC reaches every PB in antenna range, so use a secret in multiplayer).
// Own [Fleet] section in Custom Data (defaults are written on first run, "reload" re-reads them):
//   Channel=FLEET   Secret=   Listen Tags=   Extra Broadcast Tags=FLEET   Accept Commands=true   Allowed Commands=...   Accept Home=true
// Script hooks (the only lines a script needs): FleetLoad(true) after LoadConfig(); FleetPump(Command, home setter) in ReadIGC();
// drain every listener in FleetTgtL with the same handler as _lTgt (skip a message when !FleetOk(d)); FleetSend(tuple) next to each
// IGC.SendBroadcastMessage(Tag + "_TGT", ...) (shares and radius -1 "finished" messages).
List<IMyBroadcastListener> FleetTgtL = new List<IMyBroadcastListener>();   // target listeners (fleet channel + Listen Tags)
IMyBroadcastListener _flCmd, _flHome;
List<IMyBroadcastListener> _flAll = new List<IMyBroadcastListener>();
List<string> _flOut = new List<string>();      // full IGC tags that target shares are copied to
string _flKey = "", _flChan = "";              // fingerprint of the registered tags / resolved channel name ("" = off)
string _flAllowed = "hunt,attack,guard,follow,escort,form,assist,rtb,idle,stop,launch,abort,split,focus,next,unlock,aialert";
bool _flCmdOn = true, _flHomeOn = true;

string FleetTag(string t, string ch)
{
    t = t.Trim();
    if (t.Length == 0) return "";
    if (t.ToUpperInvariant() == "FLEET") return ch == "" ? "" : ch + "_TGT";
    return t.IndexOf('_') >= 0 ? t : t.ToUpperInvariant() + "_TGT";
}

void FleetLoad(bool listen)
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    const string S = "Fleet";
    if (!ini.ContainsKey(S, "Channel")) ini.Set(S, "Channel", "FLEET");
    if (!ini.ContainsKey(S, "Secret")) ini.Set(S, "Secret", "");
    if (!ini.ContainsKey(S, "Listen Tags")) ini.Set(S, "Listen Tags", "");
    if (!ini.ContainsKey(S, "Extra Broadcast Tags")) ini.Set(S, "Extra Broadcast Tags", "FLEET");
    if (!ini.ContainsKey(S, "Accept Commands")) ini.Set(S, "Accept Commands", true);
    if (!ini.ContainsKey(S, "Allowed Commands")) ini.Set(S, "Allowed Commands", _flAllowed);
    if (!ini.ContainsKey(S, "Accept Home")) ini.Set(S, "Accept Home", true);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    string ch = ini.Get(S, "Channel").ToString("FLEET").Trim().ToUpperInvariant().Replace(" ", "");
    string sec = ini.Get(S, "Secret").ToString("").Trim().Replace(" ", "");
    if (ch != "" && sec != "") ch = ch + "_" + sec;
    _flChan = ch;
    _flCmdOn = ini.Get(S, "Accept Commands").ToBoolean(true);
    _flHomeOn = ini.Get(S, "Accept Home").ToBoolean(true);
    _flAllowed = ini.Get(S, "Allowed Commands").ToString(_flAllowed).ToLowerInvariant();
    var tin = new List<string>(); var tout = new List<string>();
    if (ch != "") tin.Add(ch + "_TGT");
    foreach (var t in ini.Get(S, "Listen Tags").ToString("").Split(',')) { string x = FleetTag(t, ch); if (x != "" && !tin.Contains(x)) tin.Add(x); }
    foreach (var t in ini.Get(S, "Extra Broadcast Tags").ToString("FLEET").Split(',')) { string x = FleetTag(t, ch); if (x != "" && !tout.Contains(x)) tout.Add(x); }
    _flOut = tout;
    string key = string.Join("|", tin.ToArray()) + "#" + ch + "#" + (listen ? "1" : "0");
    if (key == _flKey) return;
    _flKey = key;
    foreach (var l in _flAll) IGC.DisableBroadcastListener(l);
    _flAll.Clear(); FleetTgtL.Clear(); _flCmd = null; _flHome = null;
    if (!listen) return;
    foreach (var x in tin) { var l = IGC.RegisterBroadcastListener(x); _flAll.Add(l); FleetTgtL.Add(l); }
    if (ch != "")
    {
        _flCmd = IGC.RegisterBroadcastListener(ch + "_CMD"); _flAll.Add(_flCmd);
        _flHome = IGC.RegisterBroadcastListener(ch + "_HOME"); _flAll.Add(_flHome);
    }
}

bool FleetV(Vector3D v) { return Math.Abs(v.X) < 1e9 && Math.Abs(v.Y) < 1e9 && Math.Abs(v.Z) < 1e9; }   // false for NaN and absurd values
bool FleetOk(MyTuple<long, Vector3D, Vector3D, double, long> d)
{
    return d.Item1 != Me.CubeGrid.EntityId && FleetV(d.Item2) && FleetV(d.Item3) && d.Item4 < 1e5 && !double.IsNaN(d.Item4);
}

void FleetPump(Action<string> onCmd, Action<Vector3D> onHome)
{
    while (_flCmd != null && _flCmd.HasPendingMessage)
    {
        var m = _flCmd.AcceptMessage();
        if (!_flCmdOn || !(m.Data is string)) continue;
        string c = ((string)m.Data).Trim().ToLowerInvariant();
        int sp = c.IndexOf(' '); string w = sp < 0 ? c : c.Substring(0, sp);
        if (w.Length > 0 && ("," + _flAllowed + ",").Contains("," + w + ",")) onCmd(c);
    }
    while (_flHome != null && _flHome.HasPendingMessage)
    {
        var m = _flHome.AcceptMessage();
        if (_flHomeOn && m.Data is Vector3D && FleetV((Vector3D)m.Data)) onHome((Vector3D)m.Data);
    }
}

void FleetSend(MyTuple<long, Vector3D, Vector3D, double, long> tup) { foreach (var t in _flOut) IGC.SendBroadcastMessage(t, tup); }
void FleetSendHome(Vector3D p) { if (_flChan != "") IGC.SendBroadcastMessage(_flChan + "_HOME", p); }
void FleetSendCmd(string c) { if (_flChan != "") IGC.SendBroadcastMessage(_flChan + "_CMD", c); }
// <<< FLEET LINK <<<

public Program()
{
    try { Load(); FleetLoad(false); } catch (Exception ex) { Echo("setup: " + ex.Message); }
    try { Blocks(); Listen(); } catch (Exception ex) { Echo("setup: " + ex.Message); }
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

string Cfg(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double Cfg(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool Cfg(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

void Load()
{
    MyIniParseResult res;
    if (!_ini.TryParse(Me.CustomData, out res)) { _note = "CustomData parse error line " + res.LineNo; return; }
    const string S = "Staff";
    Ship = Cfg(S, "Ship Tag", Ship).Trim().ToUpperInvariant(); Trip = Math.Max(0.5, Cfg(S, "Trip Time (s)", Trip)); Watch = Cfg(S, "Watch Helm", Watch);
    CockSurf = (int)Cfg(S, "Cockpit Surface", (double)CockSurf); HudSurf = (int)Cfg(S, "Helm HUD Surface", (double)HudSurf);
    MenuItems = Cfg(S, "Run Menu", MenuItems); MenuWait = Cfg(S, "Run Menu Wait", MenuWait); DispName = Cfg(S, "Display Name", DispName).ToLowerInvariant();
    Types = Cfg("Wing", "Types", string.Join(",", Types)).ToUpperInvariant().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}

void Blocks()
{
    GridTerminalSystem.GetBlocksOfType(_gyros, b => b.IsSameConstructAs(Me)); GridTerminalSystem.GetBlocksOfType(_thr, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_guns, b => b.IsSameConstructAs(Me) && !(b is IMyLargeTurretBase));
    GridTerminalSystem.GetBlocksOfType(_ctl, b => b.IsSameConstructAs(Me) && b.CanControlShip);
    GridTerminalSystem.GetBlocksOfType(_turrets, b => b.IsSameConstructAs(Me)); GridTerminalSystem.GetBlocksOfType(_ctcs, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_ai, b => b.IsSameConstructAs(Me) && b.CustomName.IndexOf("[AI]", StringComparison.OrdinalIgnoreCase) >= 0);
    GridTerminalSystem.GetBlocksOfType(_lcds, b => b.IsSameConstructAs(Me) && b.CustomName.ToLowerInvariant().Contains(DispName));
}

void Listen()
{
    _hb = IGC.RegisterBroadcastListener(Ship + ".HB"); _h2s = IGC.RegisterBroadcastListener(Ship + ".H2S"); _pil = IGC.RegisterBroadcastListener(Ship + ".PILOT");
    _hb.SetMessageCallback("hb");   // the beat is stamped the moment it arrives, not at the next Update10
}

public void Main(string arg, UpdateType src)
{
    double dt = Runtime.TimeSinceLastRun.TotalSeconds; if (dt > 0 && dt < 5) _t += dt;
    try
    {
        Pump();
        if ((src & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script | UpdateType.Mod)) != 0)
        {
            if (!string.IsNullOrWhiteSpace(arg)) Command(arg); else { _menuI = _menuOn ? _menuI + 1 : 0; _menuOn = true; _menuT = _t; }
        }
        if ((src & UpdateType.Update10) != 0) Step();
    }
    catch (Exception ex) { _note = "ERR " + ex.Message; Echo(_note); }
}

// ---- IGC in -----------------------------------------------------------------------------------------------------------------------------------
void Pump()
{
    while (_hb.HasPendingMessage) { var m = _hb.AcceptMessage(); if (m.Data is long) { _hbT = _t; _hbN++; } }
    while (_h2s.HasPendingMessage)
    {
        var m = _h2s.AcceptMessage();
        if (!(m.Data is MyTuple<int, long, Vector3D, Vector3D, float, float>)) continue;
        var d = (MyTuple<int, long, Vector3D, Vector3D, float, float>)m.Data;
        _fl = d.Item1; _tId = d.Item2; _tPos = d.Item3; _tVel = d.Item4; _tRad = d.Item5; _batt = d.Item6; _h2sT = _t;
    }
    while (_pil.HasPendingMessage) { var m = _pil.AcceptMessage(); if (m.Data is MyTuple<int, int>) { var d = (MyTuple<int, int>)m.Data; _md = d.Item1; _seat = d.Item2; } }
}

// ---- Update10 ----------------------------------------------------------------------------------------------------------------------------------
void Step()
{
    _tick++;
    if (_tick % 60 == 0) Blocks();
    if (_menuOn && _t - _menuT > MenuWait) { _menuOn = false; var it = MenuItems.Split(','); if (it.Length > 0) Command(it[_menuI % it.Length]); }
    bool lost = Watch && _hbN > 0 && _t - _hbT > Trip;
    if (lost && !_lost) { _lost = true; _lostAt = _t; _trips++; ReleaseAll(true); }
    else if (lost && _t - _relT >= 1) ReleaseAll(false);
    else if (!lost && _lost) { _lost = false; _note = "Helm back after " + (_t - _lostAt).ToString("0.0") + " s"; }
    Mothership();
    if (_tick % 3 == 0 || _lost) Show();
}

// the dead-man action: release only, never fly. 'first' adds the one-off parts (dampeners on, guns off).
void ReleaseAll(bool first)
{
    _relT = _t;
    if (first)
    {
        foreach (var a in _ai) a.Enabled = false;   // the vanilla AI blocks the Helm switched on would keep flying with their own overrides, and win against the release
        foreach (var c in _ctl) c.DampenersOverride = true;
        foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction("Shoot_Off");
    }
    foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = 0; g.Yaw = 0; g.Roll = 0; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
}

// ---- mothership link ---------------------------------------------------------------------------------------------------------------------------
void Mothership()
{
    Vector3D home = Me.CubeGrid.WorldVolume.Center;
    foreach (var c in _ctl) if (c.IsFunctional) { home = c.CenterOfMass; break; }
    double rad = Me.CubeGrid.WorldVolume.Radius;
    foreach (var ty in Types) { IGC.SendBroadcastMessage(ty + "_HOME", home); IGC.SendBroadcastMessage(ty + "_HOMER", rad); }
    FleetSendHome(home);
    if (_flChan != "") IGC.SendBroadcastMessage(_flChan + "_HOMER", rad);
    foreach (var t in _turrets) if (t.IsFunctional && t.HasTarget) Share(t.GetTargetedEntity());
    foreach (var c in _ctcs) if (c.IsFunctional && c.HasTarget) Share(c.GetTargetedEntity());
    if (_tId != 0 && _t - _h2sT < 1.0 && _tRad > 0) Send(new MyTuple<long, Vector3D, Vector3D, double, long>(_tId, _tPos, _tVel, _tRad, Me.EntityId));   // what the Helm shoots, the wing shoots
}

void Share(MyDetectedEntityInfo e)
{
    if (e.IsEmpty() || e.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) return;
    Send(new MyTuple<long, Vector3D, Vector3D, double, long>(e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() * 0.5, Me.EntityId));
}
void Send(MyTuple<long, Vector3D, Vector3D, double, long> tup) { foreach (var ty in Types) IGC.SendBroadcastMessage(ty + "_TGT", tup); FleetSend(tup); }

// ---- commands ----------------------------------------------------------------------------------------------------------------------------------
void Command(string a)
{
    a = a.Trim(); string c = a.ToLowerInvariant();
    int sp = c.IndexOf(' '); string w = sp < 0 ? c : c.Substring(0, sp), rest = sp < 0 ? "" : c.Substring(sp + 1).Trim();
    switch (w)
    {
        case "wing": Wing(rest); break;   // STUB for M4: the verbs go out; ring, roll call and the satellite allowance of the FLEET block come in M4
        case "fleet": FleetSendCmd(rest); _note = "fleet " + rest; break;
        case "release": case "recall": case "doors": _note = w + ": bay control comes with the cradle bays (M5)"; break;
        case "reload": Load(); FleetLoad(false); Blocks(); break;
        case "status": _note = "status"; Show(); break;
        default: Fwd(c); break;
    }
}

void Wing(string cmd)
{
    if (cmd == "") return;
    foreach (var ty in Types) IGC.SendBroadcastMessage(ty + "_CMD", cmd);
    FleetSendCmd(cmd); _note = "wing " + cmd;
}

void Fwd(string cmd) { IGC.SendBroadcastMessage(Ship + ".S2H", new MyTuple<string, Vector3D, double>(cmd, Me.CubeGrid.WorldVolume.Center, Me.CubeGrid.WorldVolume.Radius), TransmissionDistance.CurrentConstruct); _note = "to Helm: " + cmd; }

// ---- display -----------------------------------------------------------------------------------------------------------------------------------
static readonly string[] ModeNames = { "IDLE", "GUARD", "HUNT", "FOLLOW", "RTB", "MANUAL", "ASSIST" };

string Text()
{
    var b = _sb; b.Clear();
    b.Append("STAFF v").Append(VER).Append("  ").Append(Ship).Append('\n');
    if (_hbN == 0) b.Append("HELM  no heartbeat yet\n");
    else if (_lost) b.Append("HELM LOST ").Append((_t - _hbT).ToString("0.0")).Append(" s  trips ").Append(_trips).Append('\n');
    else b.Append("HELM  ok ").Append((_t - _hbT).ToString("0.0")).Append(" s ago  trips ").Append(_trips).Append('\n');
    int m = _md; b.Append("MODE  ").Append(m >= 0 && m < ModeNames.Length ? ModeNames[m] : "?").Append(_seat == 1 ? "  pilot aboard" : "").Append((_fl & 16) != 0 ? "  FIRING" : "").Append((_fl & 128) != 0 ? "  DEGRADED" : "").Append('\n');
    if (_tId != 0 && _t - _h2sT < 2) b.Append("TGT   ").Append(Vector3D.Distance(_tPos, Me.GetPosition()).ToString("0")).Append(" m  v ").Append(_tVel.Length().ToString("0")).Append('\n');
    else b.Append("TGT   none\n");
    b.Append("PWR ").Append((_batt * 100).ToString("0")).Append("%  DEADMAN ").Append((_fl & 64) != 0 ? "armed" : "-").Append("  WING ").Append(string.Join(",", Types)).Append('\n');
    if (_menuOn) { var it = MenuItems.Split(','); b.Append("MENU > ").Append(it[_menuI % it.Length].Trim()).Append(' ').Append(_menuI % it.Length + 1).Append('/').Append(it.Length).Append('\n'); }
    if (_note != "") b.Append(_note).Append('\n');
    return b.ToString();
}

void Show()
{
    string s = Text();
    Echo(s);
    try { Me.GetSurface(0).WriteText(s); } catch (Exception) { }
    foreach (var l in _lcds) try { l.WriteText(s); } catch (Exception) { }
    foreach (var c in _ctl)
    {
        var sp = c as IMyTextSurfaceProvider; if (sp == null) continue;
        try
        {
            if (CockSurf >= 0 && CockSurf < sp.SurfaceCount && CockSurf != HudSurf) sp.GetSurface(CockSurf).WriteText(s);
            if (_lost && HudSurf >= 0 && HudSurf < sp.SurfaceCount) sp.GetSurface(HudSurf).WriteText("HELM LOST\n" + (_t - _hbT).ToString("0.0") + " s without heartbeat\nOverrides released by STAFF\n");
        }
        catch (Exception) { }
        break;
    }
}
