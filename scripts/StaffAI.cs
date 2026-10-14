// =====================================================================
//  STAFF AI  v1.5  - the Staff script of the warships (corvette, frigate, dreadnought; also a second PB on the Shrike test ship)
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
//   * WING (M4): wing hunt | guard | rtb | follow | satellite | lock | roll | any other drone verb. Sent on every type's <TYPE>_CMD (accepted unconditionally, so 'satellite' works
//     although the FLEET block's default 'Allowed Commands' lacks it) and, for the words that list allows, on the fleet channel. 'wing lock' sends the Helm's target to the wing
//     at once and then 'hunt'. ROLL CALL: the drones' <TYPE>_SWARM beats (gridId, pos, vel, targetId, flags) are collected; alive = heard in the last 3 s, engaged = has a target.
//     HUD PAGE 2 = the roll call (order, counts, per-drone range / speed / target) on cockpit surface 'Wing Surface' (default 2) and on LCDs named '...Wing Display...'; 'page' / 'page 2'
//     flips the PB's own screen between page 1 (status) and 2.
//   * BAYS (M5, scripts/CradleBayAI.cs on the pod PB / the Corsair's [Bay] PB): release <n|all|a-b> [delay_s] [force], hold n, unhold n, recall, doors open|close.
//     The words go out as a string on "<Ship Tag>.S2P" and the bay PBs answer "<Ship Tag>.P2S" (MyTuple<int bay, string state, long droneId, float charge>; bay 0 = the ack of a
//     command). A pod docked by connectors is another construct (C3/C4, unverified), so each command is repeated until a bay acks it: ConnectedConstructs, then AntennaRelay,
//     then a unicast to the pod PB when the grid terminal system lists it ([Hangar n] Pod=), 6 tries; the note says which and, if nobody answers, to use the Timer R(n) by hand.
//     'release all' with several pods is cut into one 'release a-b' per pod, 20 s apart ("Pod Gap"). If [Hangar n] Group= names doors, release opens them first (waits "Door Open Wait"
//     for Status Open) and closes them "Door Close After" s after the last bay left; 'doors open|close' does it by hand. 'recall' = 'wing rtb', after "Recall Satellite After" s
//     'wing satellite', and the bays cancel their pending releases and switch the cradle merge blocks on (docking back is phase 2).
//   * LAUNCH (M6): 'launch [n|a-b|all] [force]' = the Helm goes IDLE (overrides off, dampeners on: the ship comes to rest, which the bays' gates need - an AUTO Helm that hunts or sweeps
//     for targets keeps the ship turning and accelerating, so a plain 'release' waits for ever), then 'release', and when those bays have left (or after 90 s) the Helm gets its old
//     mode back (guard / hunt / rtb; nothing is sent when a pilot flies or the mode changed meanwhile).
//   * TURRET TARGET GROUP (M6, replaces the planned Gunnery PB): [Staff] Turret Target Group (Weapons / Propulsion / PowerSystems; blank = off) and Turret Target Names (words in the
//     turrets' names, e.g. "Artillery,Assault"): those turrets get SetTargetingGroup at start and on 'reload' and aim at that subsystem of their target instead of its centre. Point
//     defence turrets are left alone. Sim: Leviathan vs Pirate, 6 seeds, kill time median 143 s -> 89 s (docs/design/ships/Leviathan.md).
//   * COMMANDS (PB argument, toolbar): wing <cmd>, fleet <cmd>, release / launch / hold / unhold / recall / doors, reload, status; every other word is forwarded to the Helm over IGC (.S2H): hunt, guard, rtb, manual, auto, assist hud, lock, next, unlock, ...
//     Run with NO argument: menu, each press steps "Run Menu", the highlighted item runs "Run Menu Wait" s after the last press (cockpit-toolbar fallback, K3).
//   * DISPLAYS: the PB's own screen, the cockpit surface "Cockpit Surface" (default 1, -1 off) and any LCD named "...Staff Display...": link state, mode, seat,
//     target, power, trips; "Helm HUD Surface" (default 0) of the cockpit gets the HELM LOST text while the Helm is silent.
//   * HELM SPLIT (v1.5, docs/design/helm_split.md): while the Helm's H2S flags carry bit 8 (ShrikeAI with HELMLINK 1.2) this PB also does three jobs the Helm left out to stay under its size gate:
//     FLEET CHANNEL (listens FLEET_TGT / _CMD / _HOME, "Listen Tags" and the other fleets' target feeds, relays them to the Helm as "<Ship Tag>.TGT" and ".S2H" - a fleet verb goes straight to
//     the Helm, never through this PB's Command() - and copies the Helm's own shares (".FLT") to the fleet channel), DAMAGE SENSING (the block-integrity scan, hit counter and hit direction go
//     back as ".DMG", 1 Hz, which is also the liveness beat), and nothing else: the Helm still writes every override. [Staff] Mothership=false (default true) on a ship that carries no drones
//     (Ballista, Shrike): no <Type>_HOME / _HOMER, no fleet HOME, no turret-target relay. An old Helm (no bit 8) is served as before.
//  CUSTOM DATA  [Staff] Ship Tag (SHIP, the Helm's [Link] Ship Tag), Trip Time (s) 1.5, Watch Helm, Cockpit Surface, Helm HUD Surface, Run Menu, Run Menu Wait,
//   Display Name, Wing Surface, Wing Display Name, Mothership, Damage Blocks Per Run, Damage Cells Per Run, Block Prefix (e.g. [SH]: the lists of gyros / thrusters / guns / controllers / AI blocks only take blocks whose name starts with it,
//   so drones clamped in the cradles are not adopted), Pod Gap (s) 20, Door Open Wait (s) 15, Door Close After (s) 10, Recall Satellite After (s) 12; [Wing] Types; [Hangar n] Group, Pod;
//   [Fleet] as in the drones (send side only). Ship Tag also falls back to [Ship] Tag.
//  NOT MEASURED IN THE GAME: IGC latency CurrentConstruct (K8), the number of IGC callback runs a PB survives, Update10 timing of the PB (the trip is checked
//   every 10 ticks, so it lands up to 0.17 s after the 1.5 s).
// =====================================================================

const string VER = "1.5";
MyIni _ini = new MyIni();
string Ship = "SHIP", BlkPre = "", WingName = "wing display", WingOrder = "-", MenuItems = "wing satellite,wing follow,wing hunt,wing rtb,release 1,release all,launch,recall,doors open,doors close", DispName = "staff display";
string[] Types = { "HORNET" }; string TurGroup = "", TurNames = "";   // [Staff] Turret Target Group / Turret Target Names (M6)
double Trip = 1.5, MenuWait = 2, PodGap = 20, DoorWait = 15, DoorClose = 10, RecallWait = 12; bool Watch = true; int CockSurf = 1, HudSurf = 0, WingSurf = 2, _page = 1;

List<IMyGyro> _gyros = new List<IMyGyro>(); List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>(); List<IMyShipController> _ctl = new List<IMyShipController>();
List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>(); List<IMyTurretControlBlock> _ctcs = new List<IMyTurretControlBlock>();
List<IMyTextPanel> _lcds = new List<IMyTextPanel>(), _wlcds = new List<IMyTextPanel>(); List<IMyFunctionalBlock> _ai = new List<IMyFunctionalBlock>();
IMyBroadcastListener _hb, _h2s, _pil;
class Mate { public string Ty; public Vector3D P, V; public long T; public int F; public double Seen; }
Dictionary<long, Mate> _mates = new Dictionary<long, Mate>(); List<IMyBroadcastListener> _sw = new List<IMyBroadcastListener>(); string _swKey = "";
List<long> _gone = new List<long>(); List<KeyValuePair<double, long>> _ord = new List<KeyValuePair<double, long>>(); double _wingT = -99;
double _t, _hbT = -1, _h2sT = -99, _lostAt, _relT = -99, _menuT; int _hbN, _trips, _tick, _menuI, _md, _seat, _fl; bool _lost, _menuOn;
long _tId; Vector3D _tPos, _tVel; float _tRad, _batt; string _note = "";
// Helm split (v1.5)
bool _split, MothershipOn = true, _dmgSend; int DmgBlocks = 600, DmgCells = 1200, _dmgHits; Vector3D _dmgDir, _homeP; double _dmgDirT = -99, _homeT = -99, _dmgT;
IMyBroadcastListener _flt; List<IMyBroadcastListener> _feed = new List<IMyBroadcastListener>(); Dictionary<long, MyTuple<long, Vector3D, Vector3D, double, long>> _rel = new Dictionary<long, MyTuple<long, Vector3D, Vector3D, double, long>>();
string FeedTags = "HORNET,SHRIKE,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER";   // the other fleets' target feeds (the Helm used to listen to these itself), minus the Helm's own IGC Tag
StringBuilder _sb = new StringBuilder();
// bays (M5)
class BayRec { public string St = "?"; public long Id, Src; public float Ch; public double Seen; }
class Hangar { public string Group = "", Pod = ""; }
Dictionary<int, BayRec> _bay = new Dictionary<int, BayRec>(); List<Hangar> _hg = new List<Hangar>(); List<IMyDoor> _doors = new List<IMyDoor>();
List<string> _rq = new List<string>(), _rqDone = new List<string>(); string _pendRel = "", _rqRoute = "";
double _rqT, _rqNext, _pendT, _closeAt, _recallAt = -1; int _rqN; bool _doorsAuto; IMyBroadcastListener _p2s; string _lsnKey = "", _rqNote = "";

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
string _flAllowed = "hunt,attack,guard,ready,follow,escort,form,assist,rtb,idle,stop,launch,abort,split,focus,next,unlock,aialert";
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
    Ship = Cfg(S, "Ship Tag", _ini.Get("Ship", "Tag").ToString(Ship)).Trim().ToUpperInvariant(); BlkPre = Cfg(S, "Block Prefix", BlkPre).Trim();
    PodGap = Cfg(S, "Pod Gap (s)", PodGap); DoorWait = Cfg(S, "Door Open Wait (s)", DoorWait); DoorClose = Cfg(S, "Door Close After (s)", DoorClose); RecallWait = Cfg(S, "Recall Satellite After (s)", RecallWait);
    _hg.Clear();
    for (int n = 1; n <= 4; n++) if (_ini.ContainsSection("Hangar " + n)) _hg.Add(new Hangar { Group = Cfg("Hangar " + n, "Group", ""), Pod = Cfg("Hangar " + n, "Pod", "") }); Trip = Math.Max(0.5, Cfg(S, "Trip Time (s)", Trip)); Watch = Cfg(S, "Watch Helm", Watch);
    CockSurf = (int)Cfg(S, "Cockpit Surface", (double)CockSurf); HudSurf = (int)Cfg(S, "Helm HUD Surface", (double)HudSurf);
    MenuItems = Cfg(S, "Run Menu", MenuItems); MenuWait = Cfg(S, "Run Menu Wait", MenuWait); DispName = Cfg(S, "Display Name", DispName).ToLowerInvariant();
    WingSurf = (int)Cfg(S, "Wing Surface", (double)WingSurf); WingName = Cfg(S, "Wing Display Name", WingName).ToLowerInvariant();
    MothershipOn = Cfg(S, "Mothership", MothershipOn); DmgBlocks = (int)Cfg(S, "Damage Blocks Per Run", (double)DmgBlocks); DmgCells = (int)Cfg(S, "Damage Cells Per Run", (double)DmgCells);
    TurGroup = Cfg(S, "Turret Target Group", TurGroup).Trim(); TurNames = Cfg(S, "Turret Target Names", TurNames).ToLowerInvariant();
    Types = Cfg("Wing", "Types", string.Join(",", Types)).ToUpperInvariant().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (Types.Length == 0) Types = new[] { "HORNET" };   // an empty list would crash the roll-call page
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}

void Blocks()
{
    GridTerminalSystem.GetBlocksOfType(_gyros, b => Own(b)); GridTerminalSystem.GetBlocksOfType(_thr, b => Own(b));
    GridTerminalSystem.GetBlocksOfType(_guns, b => Own(b) && !(b is IMyLargeTurretBase));
    GridTerminalSystem.GetBlocksOfType(_ctl, b => Own(b) && b.CanControlShip);
    GridTerminalSystem.GetBlocksOfType(_turrets, b => Own(b)); GridTerminalSystem.GetBlocksOfType(_ctcs, b => Own(b));
    GridTerminalSystem.GetBlocksOfType(_ai, b => Own(b) && b.CustomName.IndexOf("[AI]", StringComparison.OrdinalIgnoreCase) >= 0);
    GridTerminalSystem.GetBlocksOfType(_lcds, b => b.IsSameConstructAs(Me) && b.CustomName.ToLowerInvariant().Contains(DispName));
    GridTerminalSystem.GetBlocksOfType(_wlcds, b => b.IsSameConstructAs(Me) && b.CustomName.ToLowerInvariant().Contains(WingName));
    if (TurGroup != "" && TurNames != "")   // the turrets named like that go for one subsystem of their target (SetTargetingGroup: Weapons / Propulsion / PowerSystems)
        foreach (var t in _turrets) foreach (var n in TurNames.Split(',')) if (n.Trim() != "" && t.CustomName.ToLowerInvariant().Contains(n.Trim())) { try { t.SetTargetingGroup(TurGroup); } catch (Exception) { } break; }
    _doors.Clear();
    foreach (var h in _hg)
    {
        var g = h.Group == "" ? null : GridTerminalSystem.GetBlockGroupWithName(h.Group); if (g == null) continue;
        var l = new List<IMyTerminalBlock>(); g.GetBlocks(l);
        foreach (var x in l) { var d = x as IMyDoor; if (d != null && !_doors.Contains(d)) _doors.Add(d); }
    }
}
// the host's own blocks: same construct and, with a Block Prefix, named with it (a drone clamped in a cradle is a block of the same grid)
bool Own(IMyTerminalBlock b) { return b.IsSameConstructAs(Me) && (BlkPre == "" || b.CustomName.StartsWith(BlkPre, StringComparison.OrdinalIgnoreCase)); }

void Listen()
{
    if (_lsnKey != Ship)
    {   // (a 'reload' with a new Ship Tag registers the listeners again)
        _lsnKey = Ship; foreach (var l in new[] { _hb, _h2s, _pil, _p2s }) if (l != null) IGC.DisableBroadcastListener(l);
        _hb = IGC.RegisterBroadcastListener(Ship + ".HB"); _h2s = IGC.RegisterBroadcastListener(Ship + ".H2S"); _pil = IGC.RegisterBroadcastListener(Ship + ".PILOT");
        _hb.SetMessageCallback("hb");   // the beat is stamped the moment it arrives, not at the next Update10
        _p2s = IGC.RegisterBroadcastListener(Ship + ".P2S"); _p2s.SetMessageCallback("p2s");
        if (_flt != null) IGC.DisableBroadcastListener(_flt);
        _flt = IGC.RegisterBroadcastListener(Ship + ".FLT");
    }
    SwarmListen();
}

void SwarmListen()
{
    string key = string.Join(",", Types); if (key == _swKey) return;
    _swKey = key; foreach (var l in _sw) IGC.DisableBroadcastListener(l);
    _sw.Clear(); foreach (var ty in Types) _sw.Add(IGC.RegisterBroadcastListener(ty + "_SWARM"));
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
        bool sp = (_fl & 256) != 0; if (sp && !_split) { _split = true; SplitStart(); } else if (!sp && _split) { _split = false; FleetLoad(false); }
    }
    while (_flt.HasPendingMessage) { var m = _flt.AcceptMessage(); if (_split && m.Data is MyTuple<long, Vector3D, Vector3D, double, long>) FleetSend((MyTuple<long, Vector3D, Vector3D, double, long>)m.Data); }
    for (int i = 0; i < _sw.Count; i++)
        while (_sw[i].HasPendingMessage)
        {
            var m = _sw[i].AcceptMessage();
            if (!(m.Data is MyTuple<long, Vector3D, Vector3D, long, int>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, long, int>)m.Data;
            if (d.Item1 == Me.CubeGrid.EntityId) continue;
            Mate mt; if (!_mates.TryGetValue(d.Item1, out mt)) { mt = new Mate(); _mates[d.Item1] = mt; }
            mt.Ty = Types[i]; mt.P = d.Item2; mt.V = d.Item3; mt.T = d.Item4; mt.F = d.Item5; mt.Seen = _t;
        }
    while (_p2s.HasPendingMessage)
    {
        var m = _p2s.AcceptMessage();
        if (!(m.Data is MyTuple<int, string, long, float>)) continue;
        var d = (MyTuple<int, string, long, float>)m.Data;
        if (d.Item1 == 0) { if (d.Item2.StartsWith("ACK ") && d.Item3 > 0 && _rq.Contains(d.Item2.Substring(4)) && !_rqDone.Contains(d.Item2.Substring(4))) _rqDone.Add(d.Item2.Substring(4)); continue; }
        BayRec r; if (!_bay.TryGetValue(d.Item1, out r)) { r = new BayRec(); _bay[d.Item1] = r; }
        r.St = d.Item2; r.Id = d.Item3; r.Ch = d.Item4; r.Src = m.Source; r.Seen = _t;
    }
    while (_pil.HasPendingMessage) { var m = _pil.AcceptMessage(); if (m.Data is MyTuple<int, int>) { var d = (MyTuple<int, int>)m.Data; _md = d.Item1; _seat = d.Item2; } }
}

// ---- Update10 ----------------------------------------------------------------------------------------------------------------------------------
void Step()
{
    _tick++;
    if (_tick % 60 == 0) { Blocks(); if (_split) ScanSlims(); }
    if (_menuOn && _t - _menuT > MenuWait) { _menuOn = false; var it = MenuItems.Split(','); if (it.Length > 0) Command(it[_menuI % it.Length]); }
    bool lost = Watch && _hbN > 0 && _t - _hbT > Trip;
    if (lost && !_lost) { _lost = true; _lostAt = _t; _trips++; ReleaseAll(true); }
    else if (lost && _t - _relT >= 1) ReleaseAll(false);
    else if (!lost && _lost) { _lost = false; _note = "Helm back after " + (_t - _lostAt).ToString("0.0") + " s"; }
    if (_tick % 6 == 0) { _gone.Clear(); foreach (var kv in _mates) if (_t - kv.Value.Seen > 6) _gone.Add(kv.Key); foreach (var k in _gone) _mates.Remove(k); }
    if (MothershipOn) Mothership();
    if (_split) SplitStep();
    BayStep();
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

// ---- Helm split (v1.5): fleet channel relay + damage sensing for the Helm ---------------------------------------------------------------------
bool Hot() { return Runtime.CurrentInstructionCount > Runtime.MaxInstructionCount * 0.6; }

void SplitStart()
{
    var hl = new List<IMyProgrammableBlock>(); string ht = "SHRIKE";
    GridTerminalSystem.GetBlocksOfType(hl, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Helm]", StringComparison.OrdinalIgnoreCase));
    var h = new MyIni(); var si = new MyIni(); MyIniParseResult r;
    if (hl.Count > 0 && h.TryParse(hl[0].CustomData, out r) && si.TryParse(Me.CustomData, out r))
    {   // [Fleet] keys set on the Helm before the split are copied once, where this PB still has the default
        string[] k = { "Channel", "Secret", "Listen Tags", "Extra Broadcast Tags", "Accept Commands", "Allowed Commands", "Accept Home" }, df = { "fleet", "", "", "fleet", "true", _flAllowed, "true" };
        bool ch = false;
        for (int i = 0; i < k.Length; i++)
        {
            if (!h.ContainsKey("Fleet", k[i])) continue;
            string hv = h.Get("Fleet", k[i]).ToString().Trim(), sv = si.Get("Fleet", k[i]).ToString(df[i]).Trim();
            if (hv.ToLowerInvariant() != df[i] && sv.ToLowerInvariant() == df[i] && hv != sv) { si.Set("Fleet", k[i], hv); ch = true; }
        }
        if (ch) Me.CustomData = si.ToString();
        ht = h.Get("Shrike", "IGC Tag").ToString(ht).Trim().ToUpperInvariant();
    }
    FleetLoad(true);
    foreach (var l in _feed) IGC.DisableBroadcastListener(l);
    _feed.Clear();
    foreach (var t in FeedTags.Split(',')) { string x = t.Trim().ToUpperInvariant(); if (x != "" && x != ht && x != Ship) _feed.Add(IGC.RegisterBroadcastListener(x + "_TGT")); }
    ScanSlims(); _dmgSend = true; _dmgT = -99;
}

void SplitStep()
{
    // fleet / feed targets -> the Helm (the latest per id, at most 20 per run), "finished" tuples pass the same way
    _rel.Clear();
    foreach (var l in FleetTgtL) RelayRead(l);
    foreach (var l in _feed) RelayRead(l);
    int n = 0;
    foreach (var kv in _rel) { if (++n > 20) break; IGC.SendBroadcastMessage(Ship + ".TGT", kv.Value, TransmissionDistance.CurrentConstruct); }
    while (_flCmd != null && _flCmd.HasPendingMessage)
    {
        var m = _flCmd.AcceptMessage();
        if (m.Source == IGC.Me || !_flCmdOn || !(m.Data is string)) continue;
        string c = ((string)m.Data).Trim().ToLowerInvariant(); int sp = c.IndexOf(' '); string w = sp < 0 ? c : c.Substring(0, sp);
        if (w.Length > 0 && ("," + _flAllowed + ",").Contains("," + w + ",")) Fwd(c);   // straight to the Helm: a fleet "launch" must not start a bay launch here
    }
    while (_flHome != null && _flHome.HasPendingMessage)
    {
        var m = _flHome.AcceptMessage();
        if (m.Source == IGC.Me || !_flHomeOn || !(m.Data is Vector3D)) continue;
        Vector3D p = (Vector3D)m.Data;
        if (FleetV(p) && (p - Me.GetPosition()).LengthSquared() > 2500) { _homeP = p; _homeT = _t; _dmgSend = true; FwdHome(p); }   // (my own Staff's HOME is my own position: not a home)
    }
    if (_t - _homeT < 5 && _tick % 6 == 1) FwdHome(_homeP);
    if (!Hot()) StepSlimScan();
    if (!Hot()) CheckDamage();
    if (_dmgSend || _tick % 6 == 2) { _dmgSend = false; IGC.SendBroadcastMessage(Ship + ".DMG", new MyTuple<int, Vector3D, float>(_dmgHits, _dmgDir, (float)Math.Min(99, _t - _dmgDirT)), TransmissionDistance.CurrentConstruct); }
}

void RelayRead(IMyBroadcastListener l)
{
    while (l.HasPendingMessage)
    {
        var m = l.AcceptMessage();
        if (m.Source == IGC.Me || !(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
        if (FleetOk(d)) _rel[d.Item1] = d;
    }
}

void FwdHome(Vector3D p) { IGC.SendBroadcastMessage(Ship + ".S2H", new MyTuple<string, Vector3D, double>("", p, 0), TransmissionDistance.CurrentConstruct); }

// damage sensing (moved from the Helm): every block's integrity, a drop means the ship is being hit. Incremental: DmgCells cells / DmgBlocks blocks per run.
List<IMySlimBlock> _slims = new List<IMySlimBlock>(), _slimsNew = new List<IMySlimBlock>(); List<double> _slimHp = new List<double>(); HashSet<Vector3I> _slimSeen = new HashSet<Vector3I>();
IMyCubeGrid _slimsGrid, _scanGrid; bool _slimBusy; int _sx, _sy, _sz, _mnx, _mny, _mnz, _mxx, _mxy, _mxz, _cdI; double _integ = -1, _cdSum; Vector3D _cdHd; bool _cdCmp;
const int SlimMaxCells = 30000;
void ScanSlims()
{
    var g = Me.CubeGrid;
    if (_slimsGrid != g) { _slims.Clear(); _integ = -1; }
    _slimBusy = false;
    Vector3I mn = g.Min, mx = g.Max;
    if ((long)(mx.X - mn.X + 1) * (mx.Y - mn.Y + 1) * (mx.Z - mn.Z + 1) > SlimMaxCells) return;   // docked to something big: skip
    _scanGrid = g; _slimsNew.Clear(); _slimSeen.Clear();
    _mnx = mn.X; _mny = mn.Y; _mnz = mn.Z; _mxx = mx.X; _mxy = mx.Y; _mxz = mx.Z;
    _sx = _mnx; _sy = _mny; _sz = _mnz; _slimBusy = true;
}
void StepSlimScan()
{
    if (!_slimBusy) return;
    var g = _scanGrid;
    if (g != Me.CubeGrid) { _slimBusy = false; return; }
    for (int n = 0; n < DmgCells; n++)
    {
        var p = new Vector3I(_sx, _sy, _sz);
        if (g.CubeExists(p)) { var s = g.GetCubeBlock(p); if (s != null && _slimSeen.Add(s.Position)) _slimsNew.Add(s); }
        if (++_sz > _mxz) { _sz = _mnz; if (++_sy > _mxy) { _sy = _mny; if (++_sx > _mxx) { _slimBusy = false; break; } } }
    }
    if (_slimBusy) return;
    _slims.Clear(); _slims.AddRange(_slimsNew); _slimsGrid = g; _integ = -1;   // swap in the finished pass
}
void CheckDamage()
{
    var g = Me.CubeGrid; int n = _slims.Count;
    if (_cdI == 0)
    {
        _cdCmp = _integ >= 0 && _slimHp.Count == n;
        if (!_cdCmp) { _slimHp.Clear(); for (int i = 0; i < n; i++) _slimHp.Add(0); }
        _cdSum = 0; _cdHd = Vector3D.Zero;
    }
    if (_slimHp.Count != n) { _cdI = 0; return; }   // (a new scan pass was swapped in)
    Vector3D gc = g.WorldVolume.Center;   // which side was hit: blocks that lost integrity since the last pass, as directions from the grid centre (the shooter is out that way)
    for (int e = Math.Min(n, _cdI + DmgBlocks); _cdI < e; _cdI++)
    {
        var s = _slims[_cdI];
        double v = g.CubeExists(s.Position) ? s.BuildIntegrity - s.CurrentDamage : 0;
        _cdSum += v;
        if (_cdCmp && _slimHp[_cdI] - v > 0.5) { Vector3D d = g.GridIntegerToWorld(s.Position) - gc; double l = d.Length(); if (l > 0.3) _cdHd += d / l; }
        _slimHp[_cdI] = v;
    }
    if (_cdI < n) return;
    _cdI = 0; Vector3D hd = _cdHd;
    if (hd.LengthSquared() > 0.01)
    {
        hd.Normalize();
        Vector3D m = _t - _dmgDirT < 3 ? _dmgDir * 0.6 + hd * 0.4 : hd;
        if (m.LengthSquared() > 0.01) { _dmgDir = Vector3D.Normalize(m); _dmgDirT = _t; _dmgSend = true; }
    }
    if (_integ >= 0 && _cdSum < _integ - 0.5) { _dmgHits++; _dmgSend = true; }
    _integ = _cdSum;
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
    ShareHelmTarget();   // what the Helm shoots, the wing shoots
}

// sender = my grid: the Helm skips it, else it takes its own extrapolated target back as a fresh IGC sighting and a lost target never goes stale
bool ShareHelmTarget()
{
    if (_tId == 0 || _t - _h2sT >= 1.0 || _tRad <= 0) return false;
    Send(new MyTuple<long, Vector3D, Vector3D, double, long>(_tId, _tPos, _tVel, _tRad, Me.CubeGrid.EntityId)); return true;
}

void Share(MyDetectedEntityInfo e)
{
    if (e.IsEmpty() || e.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) return;
    Send(new MyTuple<long, Vector3D, Vector3D, double, long>(e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() * 0.5, Me.CubeGrid.EntityId));
}
void Send(MyTuple<long, Vector3D, Vector3D, double, long> tup) { foreach (var ty in Types) IGC.SendBroadcastMessage(ty + "_TGT", tup); FleetSend(tup); }

// ---- commands ----------------------------------------------------------------------------------------------------------------------------------
void Command(string a)
{
    a = a.Trim(); string c = a.ToLowerInvariant();
    int sp = c.IndexOf(' '); string w = sp < 0 ? c : c.Substring(0, sp), rest = sp < 0 ? "" : c.Substring(sp + 1).Trim();
    switch (w)
    {
        case "wing": Wing(rest); break;
        case "page": _page = rest == "2" ? 2 : rest == "1" ? 1 : 3 - _page; _note = "page " + _page; break;
        case "fleet": FleetSendCmd(rest); _note = "fleet " + rest; break;
        case "release": _note = "release " + rest; Release(rest); break;
        case "launch": Launch(rest); break;
        case "dock": Bays("recall"); Wing("rtb"); Doors("open"); _note = "dock: wing rtb, hangar doors open (no automatic cradle docking yet)"; break;
        case "deploy": _depSat = true; Launch("all"); break;   // one button: every bay lets go, then the wing takes up the satellite ring
        case "hold": case "unhold": Bays(c); break;
        case "recall": Bays("recall"); Wing("rtb"); _recallAt = _t + RecallWait; _note = "recall: wing rtb, satellite in " + RecallWait.ToString("0") + " s, bays cancelled"; break;
        case "doors": Doors(rest); break;
        case "reload": Load(); if (_split) SplitStart(); else FleetLoad(false); Blocks(); Listen(); break;
        case "status": _note = "status"; Show(); break;
        default: Fwd(c); break;
    }
}

void Wing(string cmd)
{
    if (cmd == "") { _note = "wing: " + Alive() + " alive"; return; }
    if (cmd == "roll" || cmd == "status") { _page = 2; _note = "wing roll call: " + Alive() + " alive"; return; }
    if (cmd == "lock")
    {   // the Helm's target goes out now (no waiting for the next step), then the wing hunts it
        if (!ShareHelmTarget()) { _note = "wing lock: the ship has no target"; return; }
        cmd = "hunt";
    }
    foreach (var ty in Types) IGC.SendBroadcastMessage(ty + "_CMD", cmd);   // the per-type channel accepts every verb, the fleet one only its 'Allowed Commands'
    FleetSendCmd(cmd); WingOrder = cmd; _wingT = _t; _note = "wing " + cmd;
}

// ---- bays (M5) -------------------------------------------------------------------------------------------------------------------------------
bool DoorsOpen() { foreach (var d in _doors) if (d.IsFunctional && d.Status != DoorStatus.Open) return false; return true; }

void Doors(string a)
{
    string[] p = a.Split(' '); bool open = p[0] == "open";
    if (_doors.Count == 0) { _note = "doors: no door in the [Hangar n] groups"; return; }
    if (p[0] != "open" && p[0] != "close") { _note = "doors open|close"; return; }
    foreach (var d in _doors) { if (open) d.OpenDoor(); else d.CloseDoor(); }
    _doorsAuto = false; _note = "doors " + p[0] + " (" + _doors.Count + ")";
}

// launch: park the Helm (IDLE) while the bays let go, then give it its mode back
bool _depSat; string _lchBack = ""; double _lchT; List<int> _lchSet = new List<int>(); bool _lchOn;
void Launch(string rest)
{
    if (rest == "" || rest.StartsWith("force")) rest = ("all " + rest).Trim();   // (launch, launch force)
    string[] p = rest.Split(' ')[0].Split('-'); int a, b = -1; _lchSet.Clear();
    foreach (var k in _bay.Keys) if (p[0] == "all" || (int.TryParse(p[0], out a) && k >= a && k <= (p.Length > 1 && int.TryParse(p[1], out b) ? b : a))) _lchSet.Add(k);
    _lchBack = _md == 1 || _md == 2 || _md == 4 || _md == 7 ? ModeNames[_md].ToLowerInvariant() : "";
    if (_lchBack != "") Fwd("idle");
    Release(rest); _lchT = _t; _lchOn = true; _note = "launch " + rest + (_lchBack != "" ? ": Helm idle until the bays have let go" : "");
}

void Release(string rest)
{
    if (_doors.Count > 0 && !DoorsOpen()) { foreach (var d in _doors) d.OpenDoor(); _doorsAuto = true; _pendRel = rest; _pendT = _t; _rqNote = "release " + rest + ": opening the hangar doors"; return; }
    SendRelease(rest);
}

// 'release all' with several pods (bay PBs that answered): one 'release a-b <delay>' per pod, Pod Gap seconds apart; the bay PB itself spaces its drones by 2 s
void SendRelease(string rest)
{
    var cmds = new List<string>();
    if (rest.StartsWith("all") || rest == "")
    {
        string tail = rest.Length > 3 ? rest.Substring(3) : "";
        var lo = new Dictionary<long, int>(); var hi = new Dictionary<long, int>();
        foreach (var kv in _bay) { if (kv.Value.Src == 0) continue; if (!lo.ContainsKey(kv.Value.Src) || kv.Key < lo[kv.Value.Src]) lo[kv.Value.Src] = kv.Key; if (!hi.ContainsKey(kv.Value.Src) || kv.Key > hi[kv.Value.Src]) hi[kv.Value.Src] = kv.Key; }
        var order = new List<KeyValuePair<int, long>>(); foreach (var kv in lo) order.Add(new KeyValuePair<int, long>(kv.Value, kv.Key));
        order.Sort((x, y) => x.Key.CompareTo(y.Key));
        if (order.Count < 2) cmds.Add("release all" + tail);
        else for (int i = 0; i < order.Count; i++) cmds.Add("release " + order[i].Key + "-" + hi[order[i].Value] + " " + (i * PodGap).ToString("0") + tail);
    }
    else cmds.Add("release " + rest);
    Bays(cmds.ToArray()); _closeAt = _t + DoorClose;
}

void Bays(params string[] cmds)
{
    _rq.Clear(); _rqDone.Clear(); _rq.AddRange(cmds); _rqT = _t; _rqN = 0; _rqNext = _t; _rqRoute = "";
    SendBays();
}

void SendBays()
{
    int r = _rqN % 3; bool uni = false;
    if (r == 2) foreach (var h in _hg) { var pb = h.Pod == "" ? null : GridTerminalSystem.GetBlockWithName(h.Pod); if (pb == null) continue; uni = true; foreach (var c in _rq) if (!_rqDone.Contains(c)) IGC.SendUnicastMessage(pb.EntityId, Ship + ".S2P", c); }
    _rqRoute = uni ? "unicast to the pod PB" : r == 1 ? "AntennaRelay" : "ConnectedConstructs";
    if (!uni) foreach (var c in _rq) if (!_rqDone.Contains(c)) IGC.SendBroadcastMessage(Ship + ".S2P", c, r == 1 ? TransmissionDistance.AntennaRelay : TransmissionDistance.ConnectedConstructs);
    _rqN++; _rqNext = _t + (_rqN < 3 ? 0.8 : 1.5);
    _rqNote = _rq[0] + (_rq.Count > 1 ? " +" + (_rq.Count - 1) : "") + ": sent via " + _rqRoute + (_rqN > 1 ? " (try " + _rqN + ")" : "");
}

void BayStep()
{
    if (_recallAt > 0 && _t >= _recallAt) { _recallAt = -1; Wing("satellite"); }
    if (_rq.Count > 0)
    {
        if (_rqDone.Count >= _rq.Count) { _rqNote = _rq[0] + (_rq.Count > 1 ? " +" + (_rq.Count - 1) : "") + ": the bays answered"; _rq.Clear(); }
        else if (_t >= _rqNext)
        {
            if (_rqN >= 6) { _rqNote = _rq[0] + ": NO BAY ANSWERED after 6 tries (pod PB running? C3/C4: IGC across the connector) - hand launch: its Timer R(n) / cockpit slot"; _rq.Clear(); }
            else SendBays();
        }
    }
    if (_lchOn)
    {
        bool left = _t - _lchT > 3 && _pendRel == "" && _rq.Count == 0;
        foreach (var n in _lchSet) { BayRec r; if (left && _bay.TryGetValue(n, out r) && _t - r.Seen < 6 && r.St != "EMPTY" && r.St != "COOLDOWN" && r.St != "FAULT") left = false; }
        if (left || _t - _lchT > 90) { _lchOn = false; if (_depSat) { _depSat = false; _recallAt = _t + 4; } if (_lchBack != "" && _md == 0) Fwd(_lchBack); _rqNote = "launch over: Helm " + (_lchBack != "" && _md == 0 ? _lchBack : "unchanged"); }
    }
    if (_pendRel != "" && (DoorsOpen() || _t - _pendT > DoorWait))
    {
        if (!DoorsOpen()) _rqNote = "doors still not open after " + DoorWait.ToString("0") + " s: releasing anyway";
        string r = _pendRel; _pendRel = ""; SendRelease(r);
    }
    if (_doorsAuto && _pendRel == "")
    {
        bool busy = _rq.Count > 0; foreach (var b in _bay.Values) if (_t - b.Seen < 5 && (b.St == "CHARGING" || b.St == "READY" || b.St == "ARMING" || b.St == "RELEASED")) busy = true;
        if (busy) _closeAt = _t + DoorClose;
        else if (_t >= _closeAt) { foreach (var d in _doors) d.CloseDoor(); _doorsAuto = false; _rqNote = "hangar doors closing"; }
    }
}

string BayText()
{
    if (_bay.Count == 0) return "";
    var k = new List<int>(_bay.Keys); k.Sort(); var s = new StringBuilder("BAY ");
    foreach (var n in k) { var r = _bay[n]; s.Append(n).Append(' ').Append(_t - r.Seen > 6 ? "?" : r.St).Append("  "); }
    return s.Append('\n').ToString();
}

int Alive() { int n = 0; foreach (var m in _mates.Values) if (_t - m.Seen < 3) n++; return n; }

void Fwd(string cmd) { IGC.SendBroadcastMessage(Ship + ".S2H", new MyTuple<string, Vector3D, double>(cmd, Me.CubeGrid.WorldVolume.Center, Me.CubeGrid.WorldVolume.Radius), TransmissionDistance.CurrentConstruct); _note = "to Helm: " + cmd; }

// ---- display -----------------------------------------------------------------------------------------------------------------------------------
static readonly string[] ModeNames = { "IDLE", "GUARD", "HUNT", "FOLLOW", "RTB", "MANUAL", "ASSIST", "REACT" };

// page 2: the wing roll call. alive = heard within 3 s; the list is sorted by range, at most 8 rows
string WingText()
{
    var b = _sb; b.Clear(); int n = 0, eng = 0, fire = 0, tgt = 0;
    _ord.Clear();
    foreach (var kv in _mates)
    {
        var m = kv.Value; if (_t - m.Seen >= 3) continue;
        n++; if (m.T != 0) eng++; if ((m.F & 1) != 0) fire++; if (_tId != 0 && m.T == _tId) tgt++;
        _ord.Add(new KeyValuePair<double, long>(Vector3D.Distance(m.P, Me.GetPosition()), kv.Key));
    }
    _ord.Sort((x, y) => x.Key.CompareTo(y.Key));
    b.Append("WING  ").Append(string.Join(",", Types)).Append("  order ").Append(WingOrder);
    if (_wingT > -90) b.Append(" (").Append((_t - _wingT).ToString("0")).Append(" s)");
    b.Append('\n').Append("ROLL  ").Append(n).Append(" alive  ").Append(eng).Append(" engaged  ").Append(tgt).Append(" on ship target  ").Append(fire).Append(" under fire\n");
    for (int i = 0; i < _ord.Count && i < 8; i++)
    {
        var m = _mates[_ord[i].Value];
        b.Append(m.Ty.Length > 4 ? m.Ty.Substring(0, 4) : m.Ty).Append(' ').Append((_ord[i].Value % 10000).ToString("0000")).Append("  ").Append(_ord[i].Key.ToString("0").PadLeft(5)).Append(" m  ")
         .Append(m.V.Length().ToString("0").PadLeft(3)).Append(" m/s  ").Append(m.T == 0 ? "-" : m.T == _tId ? "SHIP TGT" : "tgt").Append((m.F & 1) != 0 ? "  HIT" : "").Append('\n');
    }
    if (_ord.Count > 8) b.Append("+ ").Append(_ord.Count - 8).Append(" more\n");
    if (n == 0) b.Append("no drone heard (their ").Append(Types[0]).Append("_SWARM)\n");
    return b.ToString();
}

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
    b.Append(_split ? "SPLIT  fleet relay + damage scan on (hits " + _dmgHits + ")\n" : "SPLIT  off (Helm without bit 8)\n");
    b.Append(BayText()); if (_rqNote != "") b.Append(_rqNote).Append('\n');
    b.Append("PWR ").Append((_batt * 100).ToString("0")).Append("%  DEADMAN ").Append((_fl & 64) != 0 ? "armed" : "-").Append("  WING ").Append(string.Join(",", Types)).Append(' ').Append(Alive()).Append(" up\n");
    if (_menuOn) { var it = MenuItems.Split(','); b.Append("MENU > ").Append(it[_menuI % it.Length].Trim()).Append(' ').Append(_menuI % it.Length + 1).Append('/').Append(it.Length).Append('\n'); }
    if (_note != "") b.Append(_note).Append('\n');
    return b.ToString();
}

void Show()
{
    string s = Text(), w = WingText();
    Echo(_page == 2 ? w : s);
    try { Me.GetSurface(0).WriteText(_page == 2 ? w : s); } catch (Exception) { }
    foreach (var l in _lcds) try { l.WriteText(s); } catch (Exception) { }
    foreach (var l in _wlcds) try { l.WriteText(w); } catch (Exception) { }
    foreach (var c in _ctl)
    {
        var sp = c as IMyTextSurfaceProvider; if (sp == null) continue;
        try
        {
            if (CockSurf >= 0 && CockSurf < sp.SurfaceCount && CockSurf != HudSurf) sp.GetSurface(CockSurf).WriteText(s);
            if (WingSurf >= 0 && WingSurf < sp.SurfaceCount && WingSurf != HudSurf && WingSurf != CockSurf) sp.GetSurface(WingSurf).WriteText(w);
            if (_lost && HudSurf >= 0 && HudSurf < sp.SurfaceCount) sp.GetSurface(HudSurf).WriteText("HELM LOST\n" + (_t - _hbT).ToString("0.0") + " s without heartbeat\nOverrides released by STAFF\n");
        }
        catch (Exception) { }
        break;
    }
}
