// =====================================================================
//  STAFF AI  v1.6  - the Staff script of the warships (corvette, frigate, dreadnought; also a second PB on the Shrike test ship)
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
//     'release all' with several pods is cut into one 'release a-b' per pod, 20 s apart ("Pod Gap"). If [Hangar n] Group= names doors, release opens them first and never releases into a closed door (re-opens after "Door Open Wait", "force" overrides;
//     waits for Status Open) and closes them "Door Close After" s after the last bay left; 'doors open|close' does it by hand. 'recall' = 'wing rtb', after "Recall Satellite After" s
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
//   * HELM CONFIG, HUD, MENU, AI SETUP, LOCK, AUTO-SCAN (v1.6, docs/design/flight_split.md H1-H7, ShrikeAI with HELMLINK 1.3): this PB is the only writer of the Helm PB's Custom
//     Data (CONFIG SERVER: [Shrike], [Manual], [Battery], [Kamikaze Defence], [AI Gate] defaults written and parsed, sent as ".CFG" when H2S bit 9 asks; the Helm keeps IGC Tag,
//     Start Mode and [Link] as Get-only reads); the AI GATE SETUP of the Helm's AI blocks while H2S bit 10 is set; the cockpit HUD page from ".HUD" on the Helm's [Manual] HUD Surface;
//     the Helm's Run menu (".MNU": "Run Menu" / "Run Menu Wait" of the Helm's [Manual]); the "lock" ray (".LCK" -> ".LKR"); the camera auto-scan while H2S bit 11 is set (hits go
//     back on ".LKR"); the Helm's AI and kamikaze lines on this PB's screen. None of these writes an override.
//  CUSTOM DATA  [Staff] Ship Tag (SHIP, the Helm's [Link] Ship Tag), Trip Time (s) 1.5, Watch Helm, Cockpit Surface, Helm HUD Surface, Run Menu, Run Menu Wait,
//   Display Name, Wing Surface, Wing Display Name, Mothership, Damage Blocks Per Run, Damage Cells Per Run, Block Prefix (e.g. [SH]: the lists of gyros / thrusters / guns / controllers / AI blocks only take blocks whose name starts with it,
//   so drones clamped in the cradles are not adopted), Pod Gap (s) 20, Door Open Wait (s) 15, Door Close After (s) 10, Recall Satellite After (s) 12; [Wing] Types; [Hangar n] Group, Pod;
//   [Fleet] as in the drones (send side only). Ship Tag also falls back to [Ship] Tag.
//  NOT MEASURED IN THE GAME: IGC latency CurrentConstruct (K8), the number of IGC callback runs a PB survives, Update10 timing of the PB (the trip is checked
//   every 10 ticks, so it lands up to 0.17 s after the 1.5 s).
// =====================================================================

const string VER = "1.6";
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
// Helm config, HUD page, Run menu, AI-block setup and lock ray (v1.6, docs/design/flight_split.md H1-H6): CONFIG SERVER serves the Helm PB's Custom Data
IMyProgrammableBlock _helm; IMyBroadcastListener _hud, _mnu, _lck, _lst; Vector3D _lsP, _lsV; double _lsT = -99, _lsR = 3; int _scanIdx; bool _kzOn = true; List<IMyCameraBlock> _cams = new List<IMyCameraBlock>(); List<IMyFunctionalBlock> _aig = new List<IMyFunctionalBlock>();
MyTuple<int, Vector3D, Vector3D, Vector3D, Vector3D, string> _hd; string[] _hs = { }; IMyTextSurface _hsf;
double _hdT = -99, _fcT, _hmT, _lcT = -99; int _hmI; bool _hmOn, _aiA, _agLive; string _fc0 = "", _lcN = "", _agS = "", HmItems = "manual,auto,assist hud,assist fbw,assist off,lock,next,unlock,hunt,guard,rtb,idle", HmSeatEx = "gunnery"; double HmWait = 2; int HmSurf = 0;
const string FS = "Shrike", CfSh = "KA";
// CONFIG SERVER host hooks: the Helm's tables (tools/cfg_tables.json, model Shrike). Lines: M [Shrike], U [Manual], B [Battery], then K, A (ShrikeAI CfSchema "1/15/15/10/20/4")
string[] CfModel() { return new[] { "IGC Tag", "SHRIKE", "s", "Start Mode (GUARD/HUNT/ASSIST/IDLE)", "REACT", "s", "Sentry Range (m)", "2500", "d", "Standoff Range", "350", "d", "Wingmate Separation Range (m)", "70", "d", "Wingmate Separation Speed (m/s)", "25", "d", "Wingmate Fire Clearance (m)", "14", "d", "Min Range", "80", "d", "Combat Speed", "98", "d", "Orbit Speed", "22", "d", "Evasive Jinking", "true", "b", "Auto Camera Scan", "true", "b", "Share Targets Over IGC", "true", "b", "Fire Window Multiplier", "1", "d", "AI Block Search Assist", "true", "b", "Max Turn Rate rad/s", "5", "d", "Capital Standoff Range", "1000", "d" }; }
readonly string[] CfMan = { "Seat Debounce", "0.4", "d", "Release Delay", "1.5", "d", "Assist (OFF/HUD/FBW)", "HUD", "s", "Assist Max Rate", "1.5", "d", "Pilot Rate Gain", "1.5", "d", "Pilot Rot Sign", "-1", "d", "Rotation Input Scale", "1", "d", "Pilot Priority Rate", "0.3", "d", "Auto Fire In Manual", "false", "b", "Scan In Manual", "false", "b", "Run Menu", "manual,auto,assist hud,assist fbw,assist off,lock,next,unlock,hunt,guard,rtb,idle", "s", "Run Menu Wait", "2", "d", "Deadman Timer Name", "Deadman", "s", "Seat Exclude (name part)", "gunnery", "s", "HUD Surface", "0", "i" },
    CfBat = { "Primary (AUTO/B1..B4)", "AUTO", "s", "Standoff By Weapons", "true", "b", "Loose Fire Beyond Standoff", "true", "b", "Turret Only (AUTO/ON/OFF)", "AUTO", "s", "Auto Max Turn (AUTO/ON/OFF)", "AUTO", "s", "Salvo (AUTO/ON/OFF)", "OFF", "s", "Point Defence (OFF/B1..B4)", "OFF", "s", "Point Defence Max Radius (m)", "6", "d", "Point Defence Exclusive", "true", "b", "Kamikaze Rail Hold Range (m)", "0", "d" };
IMyProgrammableBlock CfTarget() { return _helm; }
string CfKey() { return _helm != null ? Ship + "." : ""; }
string CfExtra(MyIni i)
{   // the [Manual] and [Battery] lines; the menu and HUD keys of [Manual] are used here (the HUD page and the Run menu are this PB's since Cut 2)
    string x = CfLine(i, "Manual", CfMan, "U") + CfLine(i, "Battery", CfBat, "B");
    HmItems = i.Get("Manual", "Run Menu").ToString(HmItems); HmWait = i.Get("Manual", "Run Menu Wait").ToDouble(HmWait); HmSurf = i.Get("Manual", "HUD Surface").ToInt32(HmSurf);
    HmSeatEx = i.Get("Manual", "Seat Exclude (name part)").ToString(HmSeatEx).ToLowerInvariant(); _kzOn = i.Get("Kamikaze Defence", "Enabled").ToBoolean(true);
    return x;
}
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
string _flAllowed = "hunt,attack,guard,ready,sentry,follow,escort,form,assist,rtb,idle,stop,launch,abort,split,focus,next,unlock,aialert";
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

bool LkDefOn = false;   // LOOKOUT: off unless the Custom Data says [Harasser Defence] Enabled=true (the Warden, Leviathan and Corsair blueprints do; a Shrike does not want it)
double _now { get { return _t; } }
// >>> LOOKOUT v1.1 (shared block: harasser defence, identical in SpotterAI (Ballista) and StaffAI (Warden, Leviathan, Corsair), synced by tools/blocks_sync.py; docs/design/ballista_harasser_defence.md) >>>
// ---------------------------------------------------------------------------------------------------------------
//  A small hostile craft that stays around and shoots (an AI drone launched by an enemy warship) is neither a kamikaze (the Helm's KAMIKAZE DEFENCE) nor a wreck, and the ship's heavy fixed guns cannot answer it.
//  This module finds it, and picks a "corner jink": a velocity bias along the corner of the ship's own thrust box that lies across the harasser's line of sight (only acceleration across the line of
//  sight makes a shot miss), changed every 0.8-2.5 s at random so a gunner cannot lead it, plus a roll that puts the harasser above or below the hull (where the top and belly turrets, cameras and
//  AI turrets see it), and kites out of its gun range (Away Speed, within the large target's stand-off band). The Helm applies the bias (HARASSER LINK block) and stays the only writer of thrusters and gyros; the nose stays on the large target. Fire from a bearing nobody sees
//  (the hit direction, no tracked craft there) gets the same dodge. Link: "HDEF<Helm PB EntityId>" .H2A / .HTK (Helm -> here) and .A2H (here -> Helm), CurrentConstruct only.
//  COMMANDS: lookout on | lookout off.  CUSTOM DATA [Harasser Defence]: Enabled, Max Radius (m), Band Min / Max (m), Dwell (s), Latch (s), Jink Speed (m/s), Jink Period Min / Max (s),
//   Harasser Projectile Speed (m/s), Turret Range (m), Phantom Range (m), Roll To Threat, Away Speed (m/s) / Away Within (m) (kiting: also open the range while a harasser is inside its gun range)
// ---------------------------------------------------------------------------------------------------------------
//  v1.1 (2026-10-14): shared with StaffAI. The host declares "bool LkDefOn" (Enabled default: Spotter true, Staff false; the Warden, Leviathan and Corsair Staff Custom Data say true);
//   a copy that is not in a "[Spotter]" PB stands down while a "[Spotter]" PB is on the construct (one Lookout per ship); "Max Fire Range (m)" (was a constant 1700).
//  Hooks: LkLoad() while the host parses Custom Data (before it writes _ini back); LkBind() every ~5 s; LkStep() every Update10 run (in its own try/catch);
//   LkStatus() on the screen; "lookout on|off" commands. Uses from the host: _ini (MyIni), _now (seconds), Me, GridTerminalSystem, IGC.
bool LkOn = true, LkRoll = true;
double LkMaxR = 15, LkBandMin = 30, LkBandMax = 1600, LkDwell = 3, LkLatch = 15, LkJink = 30, LkPerMin = 0.8, LkPerMax = 2.5, LkVp = 800, LkTurR = 800, LkPhR = 1000, LkAway = 40, LkAwayR = 900;
const double LkMargin = 150, LkNose = 0.94; double LkMaxFire = 1700;
class LkT { public long Id; public Vector3D P, V; public double R, Seen, In = -1, Ev = -99, KzT = -99, Rng; public int St; }
Dictionary<long, LkT> _lk = new Dictionary<long, LkT>();
IMyBroadcastListener _lkA, _lkK;
long _lkHid, _lkTid, _lkI1, _lkI2;
double _lkHeard = -99, _lkHitAt = -99, _lkHitDirT = -99, _lkStand = 1600, _lkPhT = -99, _lkNext = -99, _lkBoxT = -99, _lkAF, _lkAB, _lkAR, _lkAL, _lkAU, _lkAD;
int _lkHits = -1, _lkCur = -1, _lkNH, _lkFl, _lkSent;
Vector3D _lkHitDir, _lkTPos, _lkPhU, _lkB, _lkUp;
bool _lkRollOn, _lkYield;
IMyRemoteControl _lkRc;
List<IMyThrust> _lkThr = new List<IMyThrust>();
List<IMyLargeTurretBase> _lkTur = new List<IMyLargeTurretBase>();
Random _lkRng = new Random();
string _lkNote = "off";

bool LkCfgB(string k, bool d) { if (!_ini.ContainsKey("Harasser Defence", k)) _ini.Set("Harasser Defence", k, d); return _ini.Get("Harasser Defence", k).ToBoolean(d); }
double LkCfg(string k, double d) { if (!_ini.ContainsKey("Harasser Defence", k)) _ini.Set("Harasser Defence", k, d); return _ini.Get("Harasser Defence", k).ToDouble(d); }
void LkLoad()
{
    LkOn = LkCfgB("Enabled", LkDefOn); LkRoll = LkCfgB("Roll To Threat", LkRoll);
    LkMaxR = LkCfg("Max Radius (m)", LkMaxR); LkBandMin = LkCfg("Band Min (m)", LkBandMin); LkBandMax = LkCfg("Band Max (m)", LkBandMax);
    LkDwell = LkCfg("Dwell (s)", LkDwell); LkLatch = LkCfg("Latch (s)", LkLatch); LkJink = LkCfg("Jink Speed (m/s)", LkJink);
    LkPerMin = LkCfg("Jink Period Min (s)", LkPerMin); LkPerMax = Math.Max(LkPerMin, LkCfg("Jink Period Max (s)", LkPerMax));
    LkAway = LkCfg("Away Speed (m/s)", LkAway); LkAwayR = LkCfg("Away Within (m)", LkAwayR);
    LkMaxFire = LkCfg("Max Fire Range (m)", LkMaxFire);
    LkVp = Math.Max(100, LkCfg("Harasser Projectile Speed (m/s)", LkVp)); LkTurR = LkCfg("Turret Range (m)", LkTurR); LkPhR = LkCfg("Phantom Range (m)", LkPhR);
}

void LkBind()   // the Helm (re-found every 5 s: a rebuilt Helm has a new EntityId and so a new key), the Remote Control, the thrusters, the vanilla turrets
{
    var pbs = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(pbs, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Helm]", StringComparison.OrdinalIgnoreCase));
    var sp = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(sp, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Spotter]", StringComparison.OrdinalIgnoreCase));
    _lkYield = sp.Count > 0 && !Me.CustomName.StartsWith("[Spotter]", StringComparison.OrdinalIgnoreCase);   // one Lookout per ship: the Spotter's
    long id = pbs.Count > 0 ? pbs[0].EntityId : 0;
    if (id != _lkHid)
    {
        if (_lkA != null) IGC.DisableBroadcastListener(_lkA);
        if (_lkK != null) IGC.DisableBroadcastListener(_lkK);
        _lkHid = id; _lkHeard = -99; _lkHits = -1;
        _lkA = id == 0 ? null : IGC.RegisterBroadcastListener("HDEF" + id + ".H2A");
        _lkK = id == 0 ? null : IGC.RegisterBroadcastListener("HDEF" + id + ".HTK");
    }
    var rcs = new List<IMyRemoteControl>();
    GridTerminalSystem.GetBlocksOfType(rcs, b => b.CubeGrid == Me.CubeGrid && b.CustomName.StartsWith("[SH]"));
    _lkRc = rcs.Count > 0 ? rcs[0] : null;
    _lkThr.Clear(); GridTerminalSystem.GetBlocksOfType(_lkThr, b => b.CubeGrid == Me.CubeGrid);
    _lkTur.Clear(); GridTerminalSystem.GetBlocksOfType(_lkTur, b => b.CubeGrid == Me.CubeGrid && b.CustomName.StartsWith("[SH]"));
}

void LkSee(long id, Vector3D p, Vector3D v, double r, double seen, bool kz)
{
    if (id == 0 || r > LkMaxR * 1.5) return;
    LkT t;
    if (!_lk.TryGetValue(id, out t)) { t = new LkT { Id = id, P = p, V = v, Seen = seen }; _lk[id] = t; }
    else
    {
        double dt = seen - t.Seen;
        if (dt < 0) return;                                                         // an older fix than the one held
        if (v.LengthSquared() < 0.01 && dt > 0.05 && dt < 2) v = (p - t.P) / dt;      // (a turret's target velocity can read 0: difference the positions)
    }
    t.P = p; t.V = v; t.R = r; t.Seen = seen;
    if (kz) t.KzT = _now;
}

// a contact closing on a collision course is the Helm's KAMIKAZE DEFENCE's job, not a harasser
bool LkIncoming(LkT t, Vector3D rel)
{
    Vector3D v = t.V - _lkRc.GetShipVelocities().LinearVelocity; double vv = v.LengthSquared(), rng = rel.Length();
    if (vv < 1 || rng < 1) return false;
    double tgo = -Vector3D.Dot(rel, v) / vv;
    if (tgo <= 0 || tgo > 12) return false;
    return (rel + v * tgo).Length() < 40 + 60 * rng / 1000 && -Vector3D.Dot(rel, v) / rng > 15;
}

void LkClassify(Vector3D me)
{
    bool hitFresh = _now - _lkHitAt < 6 && _now - _lkHitDirT < 3;
    var gone = new List<long>();
    foreach (var t in _lk.Values)
    {
        if (_now - t.Seen > 8) { gone.Add(t.Id); continue; }
        Vector3D rel = t.P + t.V * (_now - t.Seen) - me; double rng = rel.Length(); t.Rng = rng;
        bool cand = _now - t.Seen < 3 && _now - t.KzT > 4 && rng >= LkBandMin && rng <= LkBandMax && !LkIncoming(t, rel);
        if (!cand) { t.In = -1; if (_now - t.Ev > LkLatch) t.St = 0; continue; }
        if (t.In < 0) t.In = _now;
        if (_now - t.In < LkDwell) { if (t.St == 0) t.St = 1; continue; }
        bool dmg = hitFresh && rng > 1 && Vector3D.Dot(rel / rng, _lkHitDir) > 0.5;        // damage from about its direction (60 deg)
        bool pers = rng <= LkTurR && _now - t.In >= 5;                                     // it has stayed inside gun range
        if (dmg || pers) t.Ev = _now;
        t.St = _now - t.Ev < LkLatch ? 2 : 1;
    }
    foreach (var id in gone) _lk.Remove(id);
}

void LkBox()
{
    if (_now - _lkBoxT < 2) return;
    _lkBoxT = _now;
    double m = Math.Max(_lkRc.CalculateShipMass().PhysicalMass, 1), f = 0, b = 0, r = 0, l = 0, u = 0, d = 0;
    MatrixD w = _lkRc.WorldMatrix;
    foreach (var t in _lkThr)
    {
        if (!t.IsWorking) continue;
        Vector3D p = t.WorldMatrix.Backward; double th = t.MaxEffectiveThrust;      // thrusters push opposite their exhaust
        if (Vector3D.Dot(p, w.Forward) > 0.9) f += th; else if (Vector3D.Dot(p, w.Backward) > 0.9) b += th;
        else if (Vector3D.Dot(p, w.Right) > 0.9) r += th; else if (Vector3D.Dot(p, w.Left) > 0.9) l += th;
        else if (Vector3D.Dot(p, w.Up) > 0.9) u += th; else if (Vector3D.Dot(p, w.Down) > 0.9) d += th;
    }
    _lkAF = f / m; _lkAB = b / m; _lkAR = r / m; _lkAL = l / m; _lkAU = u / m; _lkAD = d / m;
}

void LkPlan(Vector3D me)
{
    var us = new List<Vector3D>(); var ws = new List<double>();
    _lkI1 = 0; _lkI2 = 0; _lkNH = 0;
    var hs = new List<LkT>();
    foreach (var t in _lk.Values) if (t.St == 2) hs.Add(t);
    hs.Sort((x, y) => x.Rng.CompareTo(y.Rng));
    double reff = LkPhR; Vector3D uH = Vector3D.Zero;
    for (int i = 0; i < hs.Count && i < 3; i++)
    {
        Vector3D r = hs[i].P + hs[i].V * (_now - hs[i].Seen) - me;
        if (r.LengthSquared() < 1) continue;
        us.Add(Vector3D.Normalize(r)); ws.Add(_now - _lkHitAt < 6 ? 1.5 : 1.0);
        if (_lkNH == 0) { reff = hs[i].Rng; uH = us[us.Count - 1]; _lkI1 = hs[i].Id; } else if (_lkNH == 1) _lkI2 = hs[i].Id;
        _lkNH++;
    }
    // fire from a bearing no tracked craft explains (a drone beyond sight, or a big gun): the same dodge, as a bearing only
    if (_now - _lkHitAt < 3 && _now - _lkHitDirT < 3)
    {
        bool explained = false;
        foreach (var t in _lk.Values)
        {
            if (_now - t.Seen > 3) continue;
            Vector3D r = t.P + t.V * (_now - t.Seen) - me;
            if (r.LengthSquared() > 1 && Vector3D.Dot(Vector3D.Normalize(r), _lkHitDir) > 0.64) explained = true;      // within 50 deg of the hit direction
        }
        if (!explained) { _lkPhU = _lkHitDir; _lkPhT = _now; }
    }
    bool phantom = _now - _lkPhT < 10;
    if (phantom) { us.Add(_lkPhU); ws.Add(0.7); }
    if (us.Count == 0) { _lkB = Vector3D.Zero; _lkCur = -1; _lkRollOn = false; _lkFl = 0; return; }
    LkBox();
    MatrixD w = _lkRc.WorldMatrix;
    bool haveT = _lkTid != 0 && _now - _lkHeard < 2; Vector3D uT = Vector3D.Zero; double dT = 0;
    if (haveT) { Vector3D r = _lkTPos - me; dT = r.Length(); if (dT > 1) uT = r / dT; else haveT = false; }
    var cs = new Vector3D[8]; var sc = new double[8]; double max = 0;
    for (int k = 0; k < 8; k++)
    {
        int sr = (k & 1) != 0 ? 1 : -1, su = (k & 2) != 0 ? 1 : -1, sf = (k & 4) != 0 ? 1 : -1;
        Vector3D c = w.Right * sr * (sr > 0 ? _lkAR : _lkAL) + w.Up * su * (su > 0 ? _lkAU : _lkAD) + w.Forward * sf * (sf > 0 ? _lkAF : _lkAB);
        cs[k] = c; double s = 0;
        for (int i = 0; i < us.Count; i++) s += ws[i] * (c - Vector3D.Dot(c, us[i]) * us[i]).Length();
        double cl = c.Length();
        if (haveT && cl > 1)
        {
            double cu = Vector3D.Dot(c, uT) / cl;
            if ((cu > 0.2 && dT < _lkStand - LkMargin) || (cu < -0.2 && dT > LkMaxFire)) s = -1;     // keep the large target inside the stand-off band and the rails' range
        }
        sc[k] = s; if (s > max) max = s;
    }
    if (max <= 0) { for (int k = 0; k < 8; k++) sc[k] = cs[k].Length(); max = 1; }                   // (every corner excluded: ignore the band rather than stand still)
    bool bad = _lkCur < 0 || sc[_lkCur] < 0.6 * max;
    if (bad || _now >= _lkNext)
    {
        var ok = new List<int>();
        for (int k = 0; k < 8; k++) if (sc[k] >= 0.8 * max && k != _lkCur) ok.Add(k);
        if (ok.Count == 0) for (int k = 0; k < 8; k++) if (sc[k] >= 0.8 * max) ok.Add(k);
        _lkCur = ok.Count > 0 ? ok[_lkRng.Next(ok.Count)] : 0;
        _lkNext = _now + MathHelper.Clamp((0.7 + 0.8 * _lkRng.NextDouble()) * reff / LkVp, LkPerMin, LkPerMax);
    }
    Vector3D c0 = cs[_lkCur]; double c0l = c0.Length();
    _lkB = c0l > 1 ? c0 / c0l * LkJink : Vector3D.Zero;
    // kite: while a harasser is inside its gun range, also open the range (the nose stays on the large target; the stand-off band of the large target limits it as for the corners)
    if (_lkNH > 0 && LkAway > 0 && reff < LkAwayR)
    {
        Vector3D a = -uH; double au = haveT ? Vector3D.Dot(a, uT) : 0;
        if (!haveT || !((au > 0.2 && dT < _lkStand - LkMargin) || (au < -0.2 && dT > LkMaxFire))) _lkB += a * LkAway;
        if (_lkB.LengthSquared() > 3600) _lkB = Vector3D.Normalize(_lkB) * 60;
    }
    int fl = 1 | 2 | (_lkNH > 0 ? 8 : 0);
    // roll: the nearest harasser above or below the hull (at most a quarter turn), unless it sits near the nose axis; deadband 20 deg
    _lkRollOn = false;
    if (LkRoll && _lkNH > 0)
    {
        Vector3D n = w.Forward, p = uH - Vector3D.Dot(uH, n) * n;
        if (Math.Abs(Vector3D.Dot(uH, n)) < LkNose && p.LengthSquared() > 0.01)
        {
            p.Normalize(); if (Vector3D.Dot(p, w.Up) < 0) p = -p;
            if (Vector3D.Dot(p, w.Up) < 0.94) { _lkUp = p; _lkRollOn = true; fl |= 4; }
        }
    }
    _lkFl = fl;
}

void LkSend()
{
    int fl = _lkFl | 256;
    IGC.SendBroadcastMessage("HDEF" + _lkHid + ".A2H", new MyTuple<Vector3D, Vector3D, int, float, long, long>(_lkB, _lkRollOn ? _lkUp : Vector3D.Zero, fl, 0.6f, _lkI1, _lkI2), TransmissionDistance.CurrentConstruct);
    _lkSent++;
}

void LkStep()
{
    if (!LkOn) { _lkNote = "off"; return; }
    if (_lkYield) { _lkNote = "the Spotter PB runs it"; return; }
    if (_lkA == null) { _lkNote = "no Helm"; return; }
    while (_lkA.HasPendingMessage)
    {
        var m = _lkA.AcceptMessage();
        if (!(m.Data is MyTuple<int, Vector3D, double, double, long, Vector3D>)) continue;
        var d = (MyTuple<int, Vector3D, double, double, long, Vector3D>)m.Data;
        if (((d.Item1 >> 8) & 15) != 1) { _lkNote = "link version?"; continue; }
        int hits = (d.Item1 >> 16) & 0x7FFF;
        if (_lkHits >= 0 && hits != _lkHits) _lkHitAt = _now;
        _lkHits = hits; _lkStand = d.Item4; _lkTid = d.Item5; _lkTPos = d.Item6; _lkHeard = _now;
        if (d.Item2.LengthSquared() > 0.01 && d.Item3 < 5) { _lkHitDir = Vector3D.Normalize(d.Item2); _lkHitDirT = _now - d.Item3; }
    }
    while (_lkK.HasPendingMessage)
    {
        var m = _lkK.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, float, float, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, float, float, int>)m.Data;
        LkSee(d.Item1, d.Item2, d.Item3, d.Item4, _now - d.Item5, (d.Item6 & 1) != 0);
    }
    if (_now - _lkHeard > 2) { _lkNote = "waiting for the Helm"; return; }              // (no message sent: the Helm drops the bias after its 0.6 s ttl)
    if (_lkRc == null) { _lkNote = "no [SH] Remote Control"; return; }
    foreach (var tu in _lkTur)
    {
        if (!tu.HasTarget) continue;
        var e = tu.GetTargetedEntity();
        if (e.IsEmpty() || e.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) continue;
        LkSee(e.EntityId, e.Position, e.Velocity, e.BoundingBox.Size.Length() * 0.5, _now, false);
    }
    Vector3D me = _lkRc.GetPosition();
    LkClassify(me);
    LkPlan(me);
    LkSend();
    _lkNote = "link OK";
}

string LkStatus()
{
    var s = new StringBuilder("LOOKOUT  ").Append(_lkNote).Append('\n');
    if (!LkOn || _lkYield || _lkA == null) return s.ToString();
    int sus = 0, har = 0; double near = 0;
    foreach (var t in _lk.Values) { if (t.St == 1) sus++; else if (t.St == 2) { har++; if (near == 0 || t.Rng < near) near = t.Rng; } }
    s.Append("Threat   ").Append(har > 0 ? "HARASSER " + near.ToString("0") + " m x" + har : _now - _lkPhT < 10 ? "FIRE FROM " + Math.Round(Math.Atan2(_lkPhU.X, _lkPhU.Z) * 57.3) + " deg (unseen)" : "none").Append("  tracks ").Append(_lk.Count).Append(" (").Append(sus).Append(" suspect)\n");
    s.Append("Dodge    ").Append(_lkB.LengthSquared() > 1 ? "corner " + _lkCur + "  " + _lkB.Length().ToString("0") + " m/s" : "-").Append(_lkRollOn ? "  roll" : "").Append("  sent ").Append(_lkSent).Append('\n');
    return s.ToString();
}
// <<< LOOKOUT v1.1 <<<

// >>> AI GATE SETUP v1.0 (sub-block of AI GATE v1.3: one-time AI-block setup; tools/ags_block.cs, synced by tools/blocks_sync.py) >>>
// Switches "AI Behavior" on, sets IMyFlightMovementBlock.SpeedLimit (typed API, opt-in) and IMyOffensiveCombatBlock.UpdateTargetInterval on the [AI]
// blocks. Off the hot path (terminal property writes that persist in the world), so it may run in another PB than the AI GATE core: final hosts AuxAI
// (drones), StaffAI (Helm ships), GnatStaffAI (variant G2), WaspAI and TubeRamAI (standalone, with the core). The Aux sets the four fields from the
// SDK cfg line "agSwitch|agTyped|agSpeed|agEvery" with its own host code (AgSet(string) in AuxAI.cs) and calls AiGateFind when SDK bit 9 AI ASSIST rises,
// when those values change, and on its 600-tick block rescan while bit 9 is set (flight_split.md 6.4).
// Hooks: AiGateFind(list of [AI] blocks) after the AI blocks are found; AiGateStatus() in the status text.
// Uses from the script: _agLive (declared by the AI GATE core; a host without the core declares "bool _agLive;" itself, false = no "AI ok" suffix).
bool AiBehaviorSwitch = true, AiTyped = false; double AiSpeed = 100; int AiSearchEvery = 5, _agFound = -1;   // _agFound: -1 not tried, else the number of AI blocks whose behaviour switch was found
List<ITerminalProperty> _agProps = new List<ITerminalProperty>();

void AiGateFind(List<IMyFunctionalBlock> ai)
{
    if (AiSearchEvery > 0) foreach (var b in ai) { var oc = b as IMyOffensiveCombatBlock; if (oc != null) oc.UpdateTargetInterval = AiSearchEvery; }
    if (!AiBehaviorSwitch || ai.Count == 0) return;
    int n = 0;
    foreach (var b in ai)
    {
        var fm = AiTyped ? b as IMyFlightMovementBlock : null;
        if (fm != null) fm.SpeedLimit = (float)AiSpeed;
        _agProps.Clear(); b.GetProperties(_agProps);
        ITerminalProperty<bool> sw = null;
        foreach (var p in _agProps)
        {
            string id = p.Id.ToLowerInvariant();
            if (!id.Contains("behavio")) continue;
            var bp = p.As<bool>();
            if (bp != null && (sw == null || id.Contains("activ"))) sw = bp;
        }
        if (sw != null && !sw.GetValue(b)) sw.SetValue(b, true);
        if (fm != null ? fm.IsAutoPilotEnabled : sw != null) n++;
    }
    _agFound = n;
}

string AiGateStatus()
{
    if (_agFound < 0) return "";
    if (_agFound == 0 && AiBehaviorSwitch) return "AI Behavior switch not found:\n turn AI Behavior on by hand\n";
    return (AiBehaviorSwitch ? "AI Behavior on (" + _agFound + " blocks)" : "") + (_agLive ? " AI ok\n" : AiBehaviorSwitch ? "\n" : "");
}
// <<< AI GATE SETUP v1.0 <<<

// >>> CONFIG SERVER v1.0 (Staff side of the config move, docs/design/flight_split.md sections 3, 4.1, 6.1; tools/cs_block.cs, synced by tools/blocks_sync.py) >>>
// Final hosts: the 9 drone Staffs and StaffAI (Helm ships). The only writer of the target PB's Custom Data (the Flight / Helm PB): a table-driven MyIni
// default writer and reader. On a WANT it re-parses the target's Custom Data, writes every missing default, reads every value, sends .CFG, and keeps
// the four AI GATE SETUP values in CfAg. Tables: [Kamikaze Defence], [AI Gate] and [Survival] are here (cfg_tables.json "shared", in its order); the
// model section comes from the host (CfModel(), section name FS).
// Channel (TransmissionDistance.CurrentConstruct):
//   .CFG  Staff -> Flight/Helm   string "CF1\t<schema>\t<status>\n" + one line per section, in this order: "M" model, then the host's CfExtra() lines
//         (Helm: its [Manual] / [Battery] lines), then one line per letter of the host's CfSh: "K" [Kamikaze Defence], "A" [AI Gate] (the 4 core values
//         only: Time-share, AI Window, Script Search Window, Max AI Hold), "S" [Survival]. A line = its letter, then "\t" + value for every sent key, in
//         table order, then "\n". Not sent: the model's local keys (IGC Tag, Start Mode ...: the Flight reads them itself) and the 4 AI GATE SETUP values.
//         Values are raw, the receiver applies the post-ops of cfg_tables.json ((int), Math.Max, Trim().ToUpperInvariant()): bools 1/0, d and i
//         ToDouble().ToString("R"), strings as written (MyIni single-line values; a tab in one would break the line).
//         schema = "1" (cfg_tables.json version) + "/" + the number of values of each line, in line order. Drones: "1/<model count without the
//         locals>/20/4/8", e.g. Mantis "1/84/20/4/8", Gnat "1/59/20/4/8".
//         status = "ok", or what CfExtra() set (Longbow / Mantis: the detected weapon name, e.g. "Small Railgun", "Gatling/Autocannon", "custom").
//         Sent when the host's WANT is true (drones: F2S bit 16 CFG WANT on a fresh F2S; Helm: H2S bit 9), at most once per 30 ticks per key. The Staff
//         never sends unasked; a target whose Custom Data does not parse gets nothing (_cfE says so on the Staff screen).
//   CfAg = "agSwitch|agTyped|agSpeed|agEvery" (same value format, raw): the [AI Gate] setup values. Drone Staffs pass it to the Aux in the SDK cfg
//   (AUX LINK v1.1); GnatStaffAI (G2) and StaffAI give it to their own AI GATE SETUP. It is filled on every parse: on a WANT and once on every new key
//   (a Staff that starts next to a running Flight knows the values without a WANT; that parse writes missing defaults but sends nothing).
// Host hooks (define CfModel() above this block: the tests read the first "CfModel()" in the file):
//   string[] CfModel()          flat key / default / type triples of the model section, cfg_tables.json order and types (d double, i int, b bool, s string)
//   IMyProgrammableBlock CfTarget()   the Flight / Helm PB whose Custom Data is served (null: nothing)
//   string CfKey()              the target's link key; the channel is CfKey() + ".CFG" (drones "WSPL"+<Flight PB EntityId> = _fk); "" = no target
//   string CfExtra(MyIni i)     after the model values are read, before the lines are built: may change model values in _cfV (index = triple number
//                               in _cfT; Longbow and Mantis: DetectWeapon, host helpers CfGet / CfPut), set _cfSt, and return extra lines built with
//                               CfLine (Helm sections); "" otherwise
//   const string CfSh           the shared lines, in order ("KAS" drones)
// Call: CfStep(bool want) once a tick (drones: after ReadF2S, want = (_fl & 65536) != 0 && _tick - _f2sTick < 30). _cfE on the screen ("" or the
// target's parse error line).
// Uses: _tick, FS, Me / IGC. Sync: answers every WANT (Staff rebuilt: nothing to do); a new key (Flight rebuilt: the Staff re-keys on its 1 Hz rescan)
// resets the 30-tick gate.
string _cfSt = "", CfAg = "", _cfK = "", _cfSc = "", _cfE = ""; string[] _cfT, _cfV; long _cfTk = -99;
readonly string[] CfKz = {
    "Enabled", "true", "b", "Max Kamikaze Radius (m)", "3.5", "d", "Warning Time (s)", "12", "d", "Miss Distance (m)", "40", "d",
    "Miss Distance Per km Of Range (m)", "60", "d", "Min Closing Speed (m/s)", "15", "d", "Priority Bonus", "2500", "d", "Protect Hive Mates", "true", "b",
    "Back Away", "true", "b", "Back Away Speed (m/s)", "60", "d", "Back Away Within (m)", "800", "d", "Back Away Reaction Time (s)", "10", "d",
    "Break Sideways", "true", "b", "Break Time (s)", "2", "d", "Break Speed (m/s)", "40", "d", "Break Miss Distance (m)", "20", "d",
    "Track And Pack Search Rays", "true", "b", "Pack Search Radius (m)", "60", "d", "Camera Charge Reserve (m)", "600", "d", "Share Threats With Hive", "true", "b"
};
readonly string[] CfAi = {
    "Time-share With Script Search", "true", "b", "Switch On AI Behavior", "true", "B", "Use Typed AI Block API", "false", "B", "AI Flight Speed Limit (m/s)", "100", "D",
    "AI Window (s)", "6", "d", "Script Search Window (s)", "8", "d", "Max AI Hold (s)", "60", "d", "AI Search Interval (s)", "5", "I"
};
readonly string[] CfSv = {
    "Lead-Breaking Jinks", "true", "b", "Power Guard", "true", "b", "Jink Accel Fraction", "0.8", "d", "Enemy Projectile Speed (m/s)", "800", "d",
    "Jink Period Min (s)", "0.4", "d", "Jink Period Max (s)", "3", "d", "Jink Period Random", "0.3", "d", "Power Guard Hold (s)", "2", "d"
};

void CfStep(bool want)
{
    string k = CfKey();
    if (k != _cfK) { _cfK = k; _cfTk = -99; if (k != "") CfSend(false); }
    if (want && k != "" && _tick - _cfTk >= 30) CfSend(true);
}
void CfSend(bool tx)
{
    var p = CfTarget(); if (p == null) return;
    if (tx) _cfTk = _tick;
    var i = new MyIni(); MyIniParseResult r;
    if (!i.TryParse(p.CustomData, out r)) { _cfE = "! Flight Custom Data: error line " + r.LineNo + "\n"; return; }
    _cfE = ""; _cfSt = "ok"; _cfSc = "1"; CfAg = "";
    _cfT = CfModel(); _cfV = CfRd(i, FS, _cfT);
    string x = CfExtra(i), sc = _cfSc; _cfSc = "1";   // CfExtra may change model values first, but its line counts go after M in the schema
    string s = "M" + CfJ(_cfT, _cfV) + "\n" + x; _cfSc += sc.Substring(1);
    foreach (char c in CfSh)
    {
        var t = c == 'K' ? CfKz : c == 'A' ? CfAi : CfSv;
        s += CfLine(i, c == 'K' ? "Kamikaze Defence" : c == 'A' ? "AI Gate" : "Survival", t, c.ToString());
    }
    string o = i.ToString(); if (o != p.CustomData) p.CustomData = o;
    if (tx) IGC.SendBroadcastMessage(_cfK + ".CFG", "CF1\t" + _cfSc + "\t" + _cfSt + "\n" + s, TransmissionDistance.CurrentConstruct);
}
// one section: write the missing defaults, read every value (raw, .CFG format)
string[] CfRd(MyIni i, string s, string[] t)
{
    var v = new string[t.Length / 3];
    for (int n = 0; n < v.Length; n++)
    {
        string k = t[3 * n], d = t[3 * n + 1]; char y = char.ToLower(t[3 * n + 2][0]);
        if (!i.ContainsKey(s, k)) i.Set(s, k, d);
        var g = i.Get(s, k);
        v[n] = y == 'b' ? (g.ToBoolean(d == "true") ? "1" : "0") : y == 's' ? g.ToString(d) : g.ToDouble(double.Parse(d)).ToString("R");
    }
    return v;
}
// the sent values of a section ("\t" + value each); locals are skipped, upper-case types (AI GATE SETUP) go to CfAg; the count goes into the schema
string CfJ(string[] t, string[] v)
{
    string b = ""; int c = 0;
    for (int n = 0; n < v.Length; n++)
    {
        string k = t[3 * n];
        if (k == "IGC Tag" || k.StartsWith("Start Mode")) continue;
        if (char.IsUpper(t[3 * n + 2][0])) CfAg += (CfAg == "" ? "" : "|") + v[n];
        else { b += "\t" + v[n]; c++; }
    }
    _cfSc += "/" + c; return b;
}
string CfLine(MyIni i, string s, string[] t, string l) { return l + CfJ(t, CfRd(i, s, t)) + "\n"; }
// <<< CONFIG SERVER v1.0 <<<

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
    LkLoad();
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}

void Blocks()
{
    GridTerminalSystem.GetBlocksOfType(_gyros, b => Own(b)); GridTerminalSystem.GetBlocksOfType(_thr, b => Own(b));
    GridTerminalSystem.GetBlocksOfType(_guns, b => Own(b) && !(b is IMyLargeTurretBase));
    GridTerminalSystem.GetBlocksOfType(_ctl, b => Own(b) && b.CanControlShip);
    GridTerminalSystem.GetBlocksOfType(_turrets, b => Own(b)); GridTerminalSystem.GetBlocksOfType(_ctcs, b => Own(b));
    GridTerminalSystem.GetBlocksOfType(_ai, b => Own(b) && b.CustomName.IndexOf("[AI]", StringComparison.OrdinalIgnoreCase) >= 0);
    GridTerminalSystem.GetBlocksOfType(_aig, b => Own(b) && (b.CustomName.Contains("[AI]") || b.BlockDefinition.ToString().Contains("OffensiveCombat") || b.BlockDefinition.ToString().Contains("FlightMovement")));   // the Helm's own AI-block rule (IsAiBlock): AiGateFind sets these up
    GridTerminalSystem.GetBlocksOfType(_cams, b => Own(b)); foreach (var c in _cams) c.EnableRaycast = true;
    var hl = new List<IMyProgrammableBlock>(); GridTerminalSystem.GetBlocksOfType(hl, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Helm]", StringComparison.OrdinalIgnoreCase)); _helm = hl.Count > 0 ? hl[0] : null;
    _hsf = null;
    foreach (var c in _ctl)
    {   // the cockpit HUD page goes to the Helm's [Manual] HUD Surface of the first flight seat (not the Remote Control, not a seat named with Seat Exclude)
        var sp = c as IMyTextSurfaceProvider;
        if (sp == null || c is IMyRemoteControl || (HmSeatEx != "" && c.CustomName.ToLowerInvariant().Contains(HmSeatEx)) || HmSurf < 0 || HmSurf >= sp.SurfaceCount) continue;
        try { _hsf = sp.GetSurface(HmSurf); _hsf.ContentType = ContentType.TEXT_AND_IMAGE; _hsf.FontSize = 0.8f; _hsf.Font = "Monospace"; } catch (Exception) { _hsf = null; }
        break;
    }
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
        _lsnKey = Ship; foreach (var l in new[] { _hb, _h2s, _pil, _p2s, _hud, _mnu, _lck, _lst }) if (l != null) IGC.DisableBroadcastListener(l);
        _hb = IGC.RegisterBroadcastListener(Ship + ".HB"); _h2s = IGC.RegisterBroadcastListener(Ship + ".H2S"); _pil = IGC.RegisterBroadcastListener(Ship + ".PILOT");
        _hb.SetMessageCallback("hb");   // the beat is stamped the moment it arrives, not at the next Update10
        _p2s = IGC.RegisterBroadcastListener(Ship + ".P2S"); _p2s.SetMessageCallback("p2s");
        _hud = IGC.RegisterBroadcastListener(Ship + ".HUD"); _hud.SetMessageCallback("hud");   // (v1.6) the HUD page is written in the run the Helm's data arrives
        _mnu = IGC.RegisterBroadcastListener(Ship + ".MNU"); _mnu.SetMessageCallback("mnu"); _lck = IGC.RegisterBroadcastListener(Ship + ".LCK"); _lck.SetMessageCallback("lck");
        _lst = IGC.RegisterBroadcastListener(Ship + ".LST");
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
        if (_split) CfStep((_fl & 512) != 0);   // CONFIG SERVER: H2S bit 9 CFG WANT -> .CFG
        if (CfAg != _agS)
        {   // the four AI GATE SETUP values of the Helm's [AI Gate] section (CONFIG SERVER parse)
            _agS = CfAg; var a = CfAg.Split('|'); double v;
            if (a.Length == 4) { AiBehaviorSwitch = a[0] == "1"; AiTyped = a[1] == "1"; double.TryParse(a[2], out v); AiSpeed = Math.Max(1, v); double.TryParse(a[3], out v); AiSearchEvery = Math.Max(0, (int)v); if (_aiA) AiGateFind(_aig); }
        }
        bool ai = (_fl & 1024) != 0; if (ai && !_aiA) AiGateFind(_aig); _aiA = ai;   // H2S bit 10 AI ASSIST rises: set the AI blocks up (they keep the settings; the Helm alone switches them on and off)
    }
    while (_hud.HasPendingMessage)
    {
        var m = _hud.AcceptMessage();
        if (!(m.Data is MyTuple<int, Vector3D, Vector3D, Vector3D, Vector3D, string>)) continue;
        _hd = (MyTuple<int, Vector3D, Vector3D, Vector3D, Vector3D, string>)m.Data; _hdT = _t; _hs = _hd.Item6.Split('|'); _agLive = (_hd.Item1 & 16384) != 0;
        if (_hs.Length > 3 && _hs[3] != _fc0) { _fc0 = _hs[3]; _fcT = _t; }
        HudW();
    }
    while (_mnu.HasPendingMessage) { _mnu.AcceptMessage(); _hmI = _hmOn ? _hmI + 1 : 0; _hmOn = true; _hmT = _t; HudW(); }   // a Run of the Helm PB without argument: step its Run Menu
    while (_lck.HasPendingMessage) { _lck.AcceptMessage(); Lock(); }
    while (_lst.HasPendingMessage) { var m = _lst.AcceptMessage(); if (m.Data is MyTuple<Vector3D, Vector3D, double, double>) { var d = (MyTuple<Vector3D, Vector3D, double, double>)m.Data; _lsP = d.Item1; _lsV = d.Item2; _lsT = _t - d.Item3; _lsR = d.Item4; } }
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
    if (_tick % 60 == 0) { Blocks(); if (_split) ScanSlims(); if (_aiA) AiGateFind(_aig); }
    if (_hmOn && _t - _hmT > HmWait) { _hmOn = false; var it = HmItems.Split(','); if (it.Length > 0) Fwd(it[_hmI % it.Length].Trim()); }   // the Helm's menu item goes back to the Helm
    if (_split && (_fl & 2048) != 0 && _t - _h2sT < 0.5) ScanStep();   // H2S bit 11 SCAN: the Helm's camera auto-scan (H7)
    if (_menuOn && _t - _menuT > MenuWait) { _menuOn = false; var it = MenuItems.Split(','); if (it.Length > 0) Command(it[_menuI % it.Length]); }
    bool lost = Watch && _hbN > 0 && _t - _hbT > Trip;
    if (lost && !_lost) { _lost = true; _lostAt = _t; _trips++; ReleaseAll(true); }
    else if (lost && _t - _relT >= 1) ReleaseAll(false);
    else if (!lost && _lost) { _lost = false; _note = "Helm back after " + (_t - _lostAt).ToString("0.0") + " s"; }
    if (_tick % 6 == 0) { _gone.Clear(); foreach (var kv in _mates) if (_t - kv.Value.Seen > 6) _gone.Add(kv.Key); foreach (var k in _gone) _mates.Remove(k); }
    if (MothershipOn) Mothership();
    if (_split) SplitStep();
    BayStep();
    if (LkOn && _tick % 30 == 1) LkBind();
    try { LkStep(); } catch (Exception ex) { _lkNote = "! " + ex.GetType().Name + ": " + ex.Message; }
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

// ---- Helm HUD page, Run menu, lock ray and camera auto-scan (v1.6, flight_split.md H3, H6, H7) -----------------------------------------------------
// The page the Helm wrote itself before Cut 2 (MANUAL ManHud), from its .HUD data; target range and speed come from H2S. Only while the Helm is alive.
void HudW()
{
    if (_hsf == null || _lost || _t - _hdT > 2 || _hs.Length < 4) return;
    var h = _hd; int f = h.Item1; var s = _hs; var b = new StringBuilder();
    b.Append("MODE ").Append(s[0]).Append((f & 2) != 0 ? " (seat taken)" : "").Append('\n');
    if (_tId != 0)
    {
        b.Append("TGT  ").Append(Vector3D.Distance(_tPos, Me.GetPosition()).ToString("0")).Append(" m  v ").Append(_tVel.Length().ToString("0")).Append("  seen ").Append(h.Item2.X.ToString("0.0")).Append("s  T ").Append(h.Item3.X.ToString("0.00")).Append('\n');
        b.Append("PIP  R").Append(h.Item3.Y.ToString("+0.0;-0.0")).Append(" U").Append(h.Item3.Z.ToString("+0.0;-0.0")).Append("  err ").Append(h.Item4.X.ToString("0.0")).Append(" win ").Append(h.Item4.Y.ToString("0.0")).Append((f & 16) != 0 ? " FIRE" : "").Append("  B").Append((f >> 8 & 3) + 1).Append(s[1]).Append('\n');
    }
    else b.Append("TGT  none (").Append(f >> 16 & 255).Append(")\n");
    b.Append("PWR ").Append((h.Item5.X * 100).ToString("0")).Append("% AMMO ").Append((h.Item5.Y * 100).ToString("0")).Append("% THR ").Append((h.Item5.Z * 100).ToString("0")).Append("% DAMP ").Append((f & 4) != 0 ? "on" : "off").Append("  DEADMAN ").Append((f & 32) == 0 ? "-" : (f & 8) != 0 ? "armed" : "off").Append('\n');
    if (_hmOn) { var it = HmItems.Split(','); b.Append("MENU > ").Append(it[_hmI % it.Length].Trim()).Append(' ').Append(_hmI % it.Length + 1).Append('/').Append(it.Length).Append('\n'); }
    if (s[2] != "") b.Append("! ").Append(s[2]).Append('\n');
    if (_t - _lcT < 5) b.Append(_lcN).Append('\n');
    try { _hsf.WriteText(b.ToString()); } catch (Exception) { _hsf = null; }
}

// the Helm's ray reference: its Remote Control, else the main cockpit, else the first controller (ShrikeAI GetBlocks)
IMyShipController Ref() { foreach (var c in _ctl) if (c is IMyRemoteControl) return c; foreach (var c in _ctl) if (c.IsMainCockpit) return c; return _ctl.Count > 0 ? _ctl[0] : null; }

// one ray result to the Helm on .LKR, as the .SCN tuple (flight_split 4.1): f = source / result bits; type, relationship and "has hit" are added here
void Scn(MyDetectedEntityInfo e, int f, Vector3D h)
{
    var b = e.BoundingBox; var o = e.Orientation;
    IGC.SendBroadcastMessage(Ship + ".LKR", new MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>>(e.EntityId, f | (int)e.Type | (int)e.Relationship << 8 | (e.HitPosition.HasValue ? 65536 : 0), h, new Vector3D(e.Velocity), new MyTuple<Vector3D, Vector3D>(b.Min, b.Max), new MyTuple<Vector3D, Vector3D>(o.Forward, o.Up)), TransmissionDistance.CurrentConstruct);
}

// "lock" (.LCK): the Helm's old ManualLock, cast here: the forward camera with the most charge, 2,500 m; results 1 no camera, 2 nothing hit, 3 friendly
void Lock()
{
    var r = Ref(); IMyCameraBlock cam = null;
    if (r != null) foreach (var c in _cams) if (c.IsWorking && Vector3D.Dot(c.WorldMatrix.Forward, r.WorldMatrix.Forward) > 0.99 && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    var e = new MyDetectedEntityInfo(); int res = 1;
    if (cam != null)
    {
        e = cam.Raycast(Math.Min(2500, cam.AvailableScanRange)); var rl = e.Relationship;
        res = e.IsEmpty() || e.Type == MyDetectedEntityType.Planet || e.Type == MyDetectedEntityType.Asteroid ? 2 : rl == MyRelationsBetweenPlayerAndBlock.Owner || rl == MyRelationsBetweenPlayerAndBlock.FactionShare || rl == MyRelationsBetweenPlayerAndBlock.Friends ? 3 : 0;
    }
    _lcT = _t; _lcN = res == 1 ? "! No forward camera for lock" : res == 2 ? "! Lock: nothing hit (range " + cam.AvailableScanRange.ToString("0") + " m)" : res == 3 ? "! Lock: target is friendly" : "LOCK " + Vector3D.Distance(e.Position, Me.GetPosition()).ToString("0") + " m";
    Scn(e, 4 << 20 | res << 24, res == 2 ? new Vector3D(cam.AvailableScanRange, 0, 0) : e.HitPosition ?? e.Position);
    HudW();
}

// the Helm's camera auto-scan (its old AutoScanStep, H7), while H2S bit 11 is set: one ray per charged camera per run (the Helm cast one per tick; the
// charge, 2 km/s per camera, is the limit either way). Every 2nd ray goes round the target the Helm just lost (.LST) for 25 s, the rest along a 120-step
// golden-angle spiral in the 35 deg cone; a warship looks 1,800 m out through every camera, a small ship 1,200 m through its forward ones.
void ScanStep()
{
    var r = Ref(); if (r == null) return;
    bool big = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large; double sr = big ? 1800 : 1200;
    for (int k = 0; k < 10 && !Hot(); k++)
    {
        IMyCameraBlock cam = null;
        foreach (var c in _cams) if (c.IsWorking && (big || Vector3D.Dot(c.WorldMatrix.Forward, r.WorldMatrix.Forward) > 0.9) && c.AvailableScanRange >= sr && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
        if (cam == null) return;
        _scanIdx++;
        double la = _t - _lsT; MyDetectedEntityInfo e;
        Vector3D lp = _lsP + _lsV * Math.Min(la, 10), dl = lp - cam.GetPosition(); double dd = dl.Length();
        if (la < 25 && (_scanIdx & 1) == 0 && dd > 30 && dd < sr && Vector3D.Dot(dl / dd, cam.WorldMatrix.Forward) > 0.75)
        {
            double rr = Math.Min(250, _lsR + 6 * la) * Math.Sqrt(((_scanIdx >> 1) % 23 + 0.5) / 23.0), th2 = _scanIdx * 2.39996323;
            dl /= dd;
            Vector3D uu = Vector3D.Cross(dl, Math.Abs(dl.Y) < 0.95 ? Vector3D.Up : Vector3D.Right); uu.Normalize();
            Vector3D vv = Vector3D.Cross(dl, uu);
            e = cam.Raycast(lp + uu * (rr * Math.Cos(th2)) + vv * (rr * Math.Sin(th2)) + dl * 10);
        }
        else
        {
            double rs = 35 * Math.Sqrt((_scanIdx % 120 + 0.5) / 120), th = _scanIdx * 2.39996323;
            e = cam.Raycast(sr, (float)(rs * Math.Sin(th)), (float)(rs * Math.Cos(th)));
        }
        if (!e.IsEmpty() && e.EntityId != Me.CubeGrid.EntityId && e.Type != MyDetectedEntityType.Planet) Scn(e, 1 << 20, e.HitPosition ?? e.Position);   // (friends too: the Helm's FaSee uses them)
    }
}

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
        case "lookout": LkOn = rest == "on"; if (LkOn) LkBind(); _note = "lookout " + (LkOn ? "on" : "off"); break;
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
    if (_pendRel != "" && (DoorsOpen() || _pendRel.Contains("force")))
    {   // a closed door is a blocked launch lane: never release into it ('release ... force' overrides)
        string r = _pendRel; _pendRel = ""; SendRelease(r);
    }
    else if (_pendRel != "" && _t - _pendT > DoorWait)
    {
        foreach (var d in _doors) if (d.Status != DoorStatus.Open) { d.Enabled = true; d.OpenDoor(); }
        _rqNote = "release " + _pendRel + " HELD: hangar doors not open after " + (_t - _pendT).ToString("0") + " s (damaged/unpowered?) - open by hand or 'release " + _pendRel + " force'";
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
    b.Append(_split ? "SPLIT  fleet relay + damage scan on (hits " + _dmgHits + ")\n" : "SPLIT  off (Helm without bit 8)\n").Append(_cfE);
    if (_t - _hdT < 3)
    {   // the Helm's AI and kamikaze lines (they left its screen with flight_split H4)
        int f = _hd.Item1, n = (int)_hd.Item4.Z, kz = f >> 24 & 15;
        if (n == 0) b.Append((f & 8192) != 0 ? "AI alarm: searching\n" : "");
        else b.Append(_fc0 != "" ? "1st contact " + _fc0 + " " + (_t - _fcT).ToString("0") + " s ago\n" : "").Append(AiGateStatus()).Append("AI: ").Append((f & 2048) != 0 ? "searching" : (f & 4096) != 0 ? "own search" : "standby").Append(" (").Append(n).Append(" blocks)").Append((f & 8192) != 0 ? "  ALARM" : "").Append('\n');
        if (_kzOn) b.Append(kz == 0 ? "Kamikaze: none incoming\n" : "!! KAMIKAZE x" + kz + " (" + (f >> 28 & 7) + " at me)  next " + _hd.Item2.Y.ToString("0.0") + " s " + _hd.Item2.Z.ToString("0") + " m" + ((f & 32768) != 0 ? " (mate)" : "") + "\n");
    }
    b.Append(BayText()); if (_rqNote != "") b.Append(_rqNote).Append('\n');
    b.Append("PWR ").Append((_batt * 100).ToString("0")).Append("%  DEADMAN ").Append((_fl & 64) != 0 ? "armed" : "-").Append("  WING ").Append(string.Join(",", Types)).Append(' ').Append(Alive()).Append(" up\n");
    if (_menuOn) { var it = MenuItems.Split(','); b.Append("MENU > ").Append(it[_menuI % it.Length].Trim()).Append(' ').Append(_menuI % it.Length + 1).Append('/').Append(it.Length).Append('\n'); }
    if (LkOn) b.Append(LkStatus());
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
