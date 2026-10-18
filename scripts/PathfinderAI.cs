// =====================================================================
//  PATHFINDER AI  v1.0  - hive weakpoint finder (stand-alone, printable)
// ---------------------------------------------------------------------
//  The Ballista's Peeper probe as its own model, on the FLEET channel. A tiny unarmed drone with ONE small turret (no ammunition: a
//  target designator) and cameras. It waits near its printer (or where it was released) until the fleet reports a target within
//  "Engage Range" (5 km), flies there and circles it while the turret, set to the targeting group "Seeker Group" (PowerSystems),
//  aims at a power block. Each aimed sample is a LINE from the turret through that block; a camera ray along it gives the hull entry.
//  Lines from different points of the orbit cross at the block itself (least squares, in the TARGET'S OWN FRAME, so the ship may move
//  and turn), which gives the block's depth under the hull. The finding goes to every friendly combatant on the hive weakpoint tag:
//       <ch>_WP  MyTuple<long target, Vector3D s, Vector3D d, Vector3D info, long senderGrid, int flags>   (docs/hive-protocol.md 4b)
//         s = hull entry, d = line into the hull (target frame), info = (0, block depth along d from s or -1, age s), flags = kind | 8 seeker
//         (| 16 when the game gave the block position). Only a block the crossed lines confirm is sent (Send Unconfirmed=false). Every armed drone's Staff PB clusters it and hands it to its Flight PB, which drills
//         it (same side) or aims at the block through its own entry point (other side, depth known).
//  Optional: <Host Tag>_CRIT (Ballista Spotter format) when "Host Tag" is set; the turret's own contact on the fleet <ch>_TGT.
//
//  FLIGHT
//   IDLE    on its printer (merge locked, or a [Bay / Hive Mother block on the grid): everything off. The shared LAUNCH block does the
//           take-off when the printer lets go, then switches to WAIT.
//   WAIT    holds position (dampeners) and listens. A fleet contact of radius >= Min Target Radius within Engage Range -> SCOUT.
//   SCOUT   straight in, then circles the target on the side its home (the wing) is on (Wing Side, Sweep Angle 45 deg; target radius
//           + Orbit Margin m, at least Orbit Min), nose on it: the power block it finds is then on the snipers' side of the hull too.
//           Target lost for Park After s or beyond Leash Range -> HOME.
//   HOME    back to the fleet home (<ch>_HOME) or its take-off point, then WAIT.     HOLD: holds position, engages nothing.
//  Heartbeat <Tag>_SWARM once a second (the printer counts it for Max Drones).
//  COMMANDS: wait | hunt | guard (WAIT)   hold | idle | stop (HOLD)   rtb (HOME)   home (home = here)   reload   armed | launch | disarm
//  CUSTOM DATA [Pathfinder] (defaults written on the first run), [Fleet] (FLEET LINK), [Launch] (LAUNCH).
//  NOT MEASURED IN THE GAME: that a turret without ammunition acquires and aims; that the targeting groups pick subsystem blocks of an
//  enemy grid from outside; HitPosition from GetTargetedEntity (the aim lines do not need it).
// =====================================================================
const string VER = "1.0";
string Tag = "PATHFINDER", HostTag = "", SeekGroup = "PowerSystems", StartMode = "WAIT";
double EngageRange = 5000, LeashRange = 7500, MinRadius = 10, OrbitMargin = 60, OrbitMin = 100, OrbitSpeed = 25, MaxSpeed = 90, TurnRate = 3;
double SettleSec = 1.5, LeadSpeed = 800, RayPast = 30, ParkAfter = 20, TriTol = 3, HomeStop = 30;
bool ShareTargets = true, ShareWp = true, WingSide = true, SendRaw = false; double Sweep = 45;

enum Mode { IDLE, WAIT, SCOUT, HOME, HOLD }
Mode _mode = Mode.IDLE;
IMyRemoteControl _rc;
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyGyro> _gyros = new List<IMyGyro>();
List<IMyCameraBlock> _cams = new List<IMyCameraBlock>();
List<IMyLargeTurretBase> _tur = new List<IMyLargeTurretBase>();
List<IMyShipMergeBlock> _mrg = new List<IMyShipMergeBlock>();
List<IMyTerminalBlock> _bay = new List<IMyTerminalBlock>();
List<IMyThrust>[] _thrDir = new List<IMyThrust>[6];
IMyBroadcastListener _lHome;
IMyTextSurface _lcd;
MyIni _ini = new MyIni();
StringBuilder _sb = new StringBuilder();

class Ct { public Vector3D P, V; public double R, T; }
Dictionary<long, Ct> _cts = new Dictionary<long, Ct>();
Dictionary<long, double> _ign = new Dictionary<long, double>();     // finished targets (radius < 0): ignored for 120 s
List<long> _del = new List<long>();

Vector3D _axis; double _psi = -99;
double _now, _modeT, _swT = -99, _theta, _theta0, _phi0, _lastSample = -99, _lastBeat = -99, _wpT = -99, _shareT = -99, _amax = 6, _upT;
long _tid; int _orbKey = -1;
Vector3D _home; bool _homeSet; double _fleetHomeT = -99;
int _sent, _lines;
string _log = "", _err = "";
int _tick;

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

// >>> THRUST VECTOR LINK v1.0 (shared block: identical in every thrust-driving drone script, synced by tools/thrust_sync.py, see docs/thrust-vectoring.md) >>>
// The flight side of THRUST VECTORING. The work (loss detection, choosing the thruster group to line up) is done by the small [Thrust] PB (scripts/ThrustVectorAI.cs);
// this block only tells it what the ship wants and takes its answer: while a thrust group is lost the PB sends a replacement attitude and Aim flies it for 0.5 s at a time.
// Intact ships (or no [Thrust] PB) never get an answer, so nothing changes.
// Script hooks: TvAim(ref desiredFwd, ref desiredUp, ref ffWorld); as the first line of Aim(); _tvN = forceWorld; in ApplyThrust(); _tvD = distance to the target (m); in the aiming section
// (target held). Uses _now, _aimRef, _powerScale (Peeper: add "double _powerScale = 1;"), IGC.
IMyBroadcastListener _tvL; Vector3D _tvN, _tvF, _tvU; double _tvS = -9, _tvT = -9, _tvD = -1; int _tvX;
void TvAim(ref Vector3D f, ref Vector3D u, ref Vector3D ff)
{
    if (_tvL == null) _tvL = IGC.RegisterBroadcastListener("TVEC.O" + IGC.Me);
    while (_tvL.HasPendingMessage) { var m = _tvL.AcceptMessage(); if (m.Data is MyTuple<Vector3D, Vector3D, int>) { var d = (MyTuple<Vector3D, Vector3D, int>)m.Data; _tvF = d.Item1; _tvU = d.Item2; _tvX = d.Item3; _tvT = _now; } }
    if (_now - _tvS >= ((_tvX & 63) != 0 ? 0.05 : 1))
    {
        _tvS = _now; var r = _aimRef.WorldMatrix;
        IGC.SendBroadcastMessage("TVEC.I", new MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>(_tvN, f, u, r.Forward, r.Up, (int)Math.Min(100, _powerScale * 100) | (int)Math.Min(4194303, _tvD < 0 ? 4194303 : _tvD / 10) << 7), TransmissionDistance.CurrentConstruct);
    }
    _tvD = -1;
    if (_now - _tvT < 0.5 && _tvF.LengthSquared() > 0.5) { f = _tvF; u = _tvU; ff = Vector3D.Zero; }
}
// <<< THRUST VECTOR LINK <<<

// >>> LAUNCH v1.1 (shared block: identical in every drone script, see docs/launch-protocol.md) >>>
// Automatic take-off for drones that were printed on (merged to) a printer. A drone must leave and start working WITHOUT depending on one message:
//   * it notices on its own that the merge pair opened (no connected merge block on its grid any more, or the grid changed) and then
//   * burns away from the printer for "Burst Seconds" (gyros held at zero rate, thrusters pushed along the Away vector, dampeners off),
//   * clears the merge/antenna/thruster/gyro/battery switches that welding may have left off, and
//   * switches the script into its configured mode (the script's Start Mode, or [Launch] Mode when that is IDLE).
// Other triggers that do the same: Custom Data [Launch] Armed=true while the PB is not docked (a PB recompiled after the release),
// the argument "armed" (printer arms it while the drone is still on its grid), the argument "launch", a "<Tag>_JOIN" broadcast or a
// FLEET "<ch>_CMD" string "join" (only for an IDLE, complete, undocked drone), and a docked -> undocked change seen by the PB itself.
// Custom Data (own [Launch] section; defaults are written on the first run, "reload" re-reads them):
//   Auto=true  Burst Seconds=4  Away X/Y/Z=0 (world vector away from the printer, zero = unknown)  Armed=false  Clear Distance=8
//   Max Speed=25  Mode=GUARD
// Script hooks (the only lines a script needs): LaunchLoad() after LoadConfig() and FleetLoad() (constructor and "reload"); in Command():
//   case "armed": case "launch": case "disarm": LaunchCmd(word); break;   at the very top of Control(dt) (or the tick, before any thrust):
//   if (LaunchStep(dt, <mode is IDLE>, StartMode, <home known>, <home point>, Command)) return;    LaunchStatus() in the display text.
// The block needs the script's fields: Tag (IGC tag) and _flChan (FLEET LINK block); everything else is its own.
const string LnSec = "Launch";
int _lnS;                                   // 0 free, 1 docked and armed (waiting for the separation), 2 take-off burn, 3 launched
bool _lnFused, _lnAuto = true, _lnArmed, _lnHold, _lnForce, _lnLocOk, _lnDock, _lnHomeOk, _lnPending;
double _lnBurst = 4, _lnClear = 8, _lnSpeed = 25, _lnClock, _lnT, _lnJoinT = -999;
string _lnModeCfg = "GUARD", _lnMsg = "", _lnJKey = "";
Vector3D _lnAwayCfg, _lnAwayLoc, _lnDir, _lnP0, _lnHome;
IMyCubeGrid _lnGrid;
long _lnTick; int _lnFree, _lnWait;
IMyBroadcastListener _lnJ1, _lnJ2;
List<IMyShipMergeBlock> _lnMg = new List<IMyShipMergeBlock>();
List<IMyThrust> _lnThr = new List<IMyThrust>();
List<IMyGyro> _lnGy = new List<IMyGyro>();
IMyShipController _lnCtl;

void LaunchLoad()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    if (!ini.ContainsKey(LnSec, "Auto")) ini.Set(LnSec, "Auto", true);
    if (!ini.ContainsKey(LnSec, "Burst Seconds")) ini.Set(LnSec, "Burst Seconds", _lnBurst);
    if (!ini.ContainsKey(LnSec, "Away X")) ini.Set(LnSec, "Away X", 0.0);
    if (!ini.ContainsKey(LnSec, "Away Y")) ini.Set(LnSec, "Away Y", 0.0);
    if (!ini.ContainsKey(LnSec, "Away Z")) ini.Set(LnSec, "Away Z", 0.0);
    if (!ini.ContainsKey(LnSec, "Armed")) ini.Set(LnSec, "Armed", false);
    if (!ini.ContainsKey(LnSec, "Clear Distance")) ini.Set(LnSec, "Clear Distance", _lnClear);
    if (!ini.ContainsKey(LnSec, "Max Speed")) ini.Set(LnSec, "Max Speed", _lnSpeed);
    if (!ini.ContainsKey(LnSec, "Mode")) ini.Set(LnSec, "Mode", _lnModeCfg);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    _lnAuto = ini.Get(LnSec, "Auto").ToBoolean(true);
    _lnBurst = MathHelper.Clamp(ini.Get(LnSec, "Burst Seconds").ToDouble(4), 0.5, 20);
    _lnClear = MathHelper.Clamp(ini.Get(LnSec, "Clear Distance").ToDouble(8), 1, 200);
    _lnSpeed = MathHelper.Clamp(ini.Get(LnSec, "Max Speed").ToDouble(25), 3, 200);
    _lnModeCfg = ini.Get(LnSec, "Mode").ToString("GUARD");
    _lnAwayCfg = new Vector3D(ini.Get(LnSec, "Away X").ToDouble(0), ini.Get(LnSec, "Away Y").ToDouble(0), ini.Get(LnSec, "Away Z").ToDouble(0));
    if (ini.Get(LnSec, "Armed").ToBoolean(false)) _lnArmed = true;
    string fl = _flChan != "" ? _flChan + "_CMD" : "";
    string key = Tag + "|" + fl;
    if (key != _lnJKey)
    {
        _lnJKey = key;
        if (_lnJ1 != null) IGC.DisableBroadcastListener(_lnJ1);
        if (_lnJ2 != null) IGC.DisableBroadcastListener(_lnJ2);
        _lnJ1 = IGC.RegisterBroadcastListener(Tag + "_JOIN");
        _lnJ2 = fl != "" ? IGC.RegisterBroadcastListener(fl) : null;
    }
}

void LnWrite(bool armed)
{
    var ini = new MyIni(); MyIniParseResult r;
    if (!ini.TryParse(Me.CustomData, out r)) return;
    ini.Set(LnSec, "Armed", armed);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}

void LaunchCmd(string w)
{
    switch (w)
    {
        case "armed": LaunchLoad(); _lnHold = false; _lnArmed = true; LnWrite(true); if (_lnS == 3) _lnS = 0; break;
        case "launch": if (_lnS < 2) _lnForce = true; break;
        case "disarm": _lnHold = true; _lnArmed = false; _lnForce = false; if (_lnS == 1) _lnS = 0; LnWrite(false); break;
    }
}

void LnRead()
{
    if (_lnJ1 != null) while (_lnJ1.HasPendingMessage) { _lnJ1.AcceptMessage(); _lnJoinT = _lnClock; }
    if (_lnJ2 != null)
        while (_lnJ2.HasPendingMessage)
        {
            var m = _lnJ2.AcceptMessage();
            if (m.Data is string && ((string)m.Data).Trim().ToLowerInvariant() == "join") _lnJoinT = _lnClock;
        }
}

// is a merge pair of this grid locked? Also learns the direction away from the partner block (the block of the pair nearest to the PB is the drone's).
bool LnDocked()
{
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_lnMg, b => b.CubeGrid == g && b.IsConnected);
    if (_lnMg.Count == 0) return false;
    if (_lnMg.Count >= 2)
    {
        Vector3D me = Me.GetPosition(); IMyShipMergeBlock d = null, p = null; double best = double.MaxValue;
        foreach (var m in _lnMg) { double q = (m.GetPosition() - me).LengthSquared(); if (q < best) { best = q; d = m; } }
        best = double.MaxValue;
        foreach (var m in _lnMg) if (m != d) { double q = (m.GetPosition() - d.GetPosition()).LengthSquared(); if (q < best) { best = q; p = m; } }
        Vector3D a = d.GetPosition() - p.GetPosition();
        if (a.LengthSquared() > 1e-4) { a.Normalize(); _lnAwayLoc = Vector3D.TransformNormal(a, MatrixD.Transpose(Me.WorldMatrix)); _lnLocOk = true; }
    }
    return true;
}

// every terminal block of the grid works and there is a thruster and a controller: a half-built drone must not take off on a stray "join"
bool LnComplete()
{
    var g = Me.CubeGrid; var l = new List<IMyTerminalBlock>(); bool thr = false, ctl = false;
    GridTerminalSystem.GetBlocksOfType(l, b => b.CubeGrid == g);
    foreach (var b in l) { if (!b.IsFunctional) return false; if (b is IMyThrust) thr = true; if (b is IMyShipController) ctl = true; }
    return thr && ctl;
}

// welded blocks may come off: switch on what the flight needs, the merge block off (it must never re-latch)
void LnPrep()
{
    var g = Me.CubeGrid;
    _lnThr.Clear(); _lnGy.Clear(); _lnCtl = null;
    GridTerminalSystem.GetBlocksOfType(_lnThr, b => b.CubeGrid == g);
    GridTerminalSystem.GetBlocksOfType(_lnGy, b => b.CubeGrid == g);
    foreach (var t in _lnThr) if (!t.Enabled) t.Enabled = true;
    foreach (var y in _lnGy) if (!y.Enabled) y.Enabled = true;
    var bats = new List<IMyBatteryBlock>(); GridTerminalSystem.GetBlocksOfType(bats, b => b.CubeGrid == g);
    foreach (var b in bats) { if (!b.Enabled) b.Enabled = true; if (b.ChargeMode != ChargeMode.Auto) b.ChargeMode = ChargeMode.Auto; }
    var ants = new List<IMyRadioAntenna>(); GridTerminalSystem.GetBlocksOfType(ants, b => b.CubeGrid == g);
    foreach (var a in ants) { if (!a.Enabled) a.Enabled = true; if (!a.EnableBroadcasting) a.EnableBroadcasting = true; }
    var guns = new List<IMyUserControllableGun>(); GridTerminalSystem.GetBlocksOfType(guns, b => b.CubeGrid == g);
    foreach (var b in guns) if (!b.Enabled) b.Enabled = true;
    var cams = new List<IMyCameraBlock>(); GridTerminalSystem.GetBlocksOfType(cams, b => b.CubeGrid == g);
    foreach (var b in cams) if (!b.Enabled) b.Enabled = true;
    var mgs = new List<IMyShipMergeBlock>(); GridTerminalSystem.GetBlocksOfType(mgs, b => b.CubeGrid == g);
    foreach (var b in mgs) if (b.Enabled) b.Enabled = false;
    var ctrls = new List<IMyShipController>(); GridTerminalSystem.GetBlocksOfType(ctrls, b => b.CubeGrid == g && b.CanControlShip);
    foreach (var c in ctrls) if (c is IMyRemoteControl) { _lnCtl = c; break; }
    if (_lnCtl == null && ctrls.Count > 0) _lnCtl = ctrls[0];
}

// the release is only complete when the grid has split (and this grid is no printer): while the printer's merge block is still on this grid the thrusters/gyros found here may be the printer's
bool LnReady()
{
    var g = Me.CubeGrid;
    // still fused to a printer (the Hive Mother PB or a [Bay block is on this grid): the thrusters and gyros found here are the printer's, never burn
    var pr = new List<IMyTerminalBlock>(); GridTerminalSystem.GetBlocksOfType(pr, b => b.CubeGrid == g && b != Me && (b.CustomName.Contains("[Bay") || (b is IMyProgrammableBlock && b.CustomData.Contains("[Hive Mother]"))));
    _lnFused = pr.Count > 0; if (_lnFused) return false;
    GridTerminalSystem.GetBlocksOfType(_lnMg, b => b.CubeGrid == g);
    if (_lnMg.Count < 2 || ++_lnWait > 9) return true;       // (gives up waiting after ~1.7 s)
    _lnPending = true; return false;
}

void LnBegin()
{
    _lnWait = 0; _lnPending = false;
    _lnForce = false; _lnS = 2; _lnT = 0; _lnJoinT = -999;
    LnPrep();
    Vector3D me = Me.GetPosition(), d = Vector3D.Zero; string src = "";
    Vector3D lw = _lnLocOk ? Vector3D.TransformNormal(_lnAwayLoc, Me.WorldMatrix) : Vector3D.Zero;
    if (_lnAwayCfg.LengthSquared() > 0.25 && !(_lnLocOk && Vector3D.Dot(Vector3D.Normalize(_lnAwayCfg), lw) < -0.2)) { d = _lnAwayCfg; src = "Custom Data"; }   // (a stored vector that points INTO the printer is ignored)
    else if (_lnLocOk) { d = lw; src = "merge pair"; }
    else if (LnOwnMerge(me, ref d)) src = "own merge block";
    else if (_lnHomeOk && (me - _lnHome).LengthSquared() > 25) { d = me - _lnHome; src = "home point"; }
    else { d = _lnCtl != null ? _lnCtl.WorldMatrix.Backward : Me.WorldMatrix.Backward; src = "own back"; }
    _lnDir = Vector3D.Normalize(d); _lnP0 = me;
    _lnMsg = "away from " + src;
}

// no printer information: the drone's own merge block sits on the side that faced the printer, so "from the merge block to the centre of mass" points away from it
bool LnOwnMerge(Vector3D me, ref Vector3D d)
{
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_lnMg, b => b.CubeGrid == g);
    if (_lnMg.Count == 0 || _lnCtl == null) return false;
    IMyShipMergeBlock mb = _lnMg[0];
    foreach (var m in _lnMg) if ((m.GetPosition() - me).LengthSquared() < (mb.GetPosition() - me).LengthSquared()) mb = m;
    Vector3D v = _lnCtl.CenterOfMass - mb.GetPosition();
    if (v.LengthSquared() < 0.09) return false;
    d = v; return true;
}

// one tick of the take-off: true = the script must not fly this tick
bool LnBurn(double dt, bool idle, string startMode, Action<string> cmd)
{
    _lnT += dt;
    double moved = Vector3D.Dot(Me.GetPosition() - _lnP0, _lnDir);
    double vAway = _lnCtl != null ? Vector3D.Dot(_lnCtl.GetShipVelocities().LinearVelocity, _lnDir) : 0;
    if (_lnT >= _lnBurst || (_lnT >= 0.5 && moved >= _lnClear && vAway > 1))
    {
        foreach (var t in _lnThr) t.ThrustOverridePercentage = 0;
        foreach (var y in _lnGy) { y.GyroOverride = false; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }
        if (_lnCtl != null) _lnCtl.DampenersOverride = true;
        _lnS = 3; _lnArmed = false; LnWrite(false);
        string m = startMode.Trim().ToUpperInvariant();
        if (m == "" || m == "IDLE" || m == "SENTRY") m = _lnModeCfg.Trim().ToUpperInvariant();   // (SENTRY, the default Start Mode, is for a pasted drone: a launched one takes [Launch] Mode)
        _lnMsg = "took off (" + _lnMsg + ", " + moved.ToString("0") + " m, " + _lnT.ToString("0.0") + " s), mode " + m;
        if (idle && m != "" && m != "IDLE") { cmd("home"); cmd(m.ToLowerInvariant()); }
        return false;
    }
    bool cut = vAway >= _lnSpeed;
    double best = -2;
    foreach (var t in _lnThr) if (t.IsFunctional) best = Math.Max(best, Vector3D.Dot(t.WorldMatrix.Backward, _lnDir));
    if (best <= 0.3 && best > -0.2)
    {   // no thruster pushes (nearly) along Away (e.g. a ship with only fore/aft thrusters and a sideways Away): burn along the best thruster axis, which is perpendicular to Away, never toward the printer
        Vector3D alt = Vector3D.Zero; double ab = -2;
        foreach (var t in _lnThr) if (t.IsFunctional) { double q = Vector3D.Dot(t.WorldMatrix.Backward, _lnDir); if (q > ab) { ab = q; alt = t.WorldMatrix.Backward; } }
        if (alt.LengthSquared() > 0.5) { _lnDir = Vector3D.Normalize(alt); _lnP0 = Me.GetPosition(); best = 1; _lnMsg += ", thrust axis"; }
    }
    if (best < 0) best = 0;
    foreach (var t in _lnThr)
    {
        double c = t.IsFunctional ? Vector3D.Dot(t.WorldMatrix.Backward, _lnDir) : 0;
        t.ThrustOverridePercentage = (!cut && best > 0.3 && c > 0.15) ? (float)Math.Max(0.01, c / best) : 0f;
    }
    foreach (var y in _lnGy) { y.GyroOverride = true; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }   // hold the attitude: a zero rate command
    if (_lnCtl != null) _lnCtl.DampenersOverride = false;
    return true;
}

bool LaunchStep(double dt, bool idle, string startMode, bool homeOk, Vector3D home, Action<string> cmd)
{
    _lnTick++; _lnClock += dt;
    _lnHomeOk = homeOk; _lnHome = home;
    LnRead();
    if (_lnS == 2) return LnBurn(dt, idle, startMode, cmd);
    if (_lnS == 3) return false;
    bool changed = Me.CubeGrid != _lnGrid;
    bool join = _lnClock - _lnJoinT < 3;                     // the printer repeats "join" every 2 s
    if (!(changed || (_lnForce && _lnTick % 10 == 0) || _lnTick % (_lnS == 1 || _lnPending ? 10 : 60) == 0 || (join && _lnTick % 10 == 0))) return false;   // (never more than 6 checks a second)
    _lnGrid = Me.CubeGrid;
    _lnDock = LnDocked(); _lnPending = false;
    if (_lnS == 0)
    {
        if (_lnDock)
        {
            if (!_lnHold) { _lnS = 1; _lnFree = 0; if (!_lnArmed) { _lnArmed = true; LnWrite(true); } }
            return false;
        }
        if (_lnHold && !_lnForce) return false;
        bool go = _lnForce || (_lnAuto && (_lnArmed || (join && idle && LnComplete())));
        if (go && LnReady()) { LnBegin(); return LnBurn(dt, idle, startMode, cmd); }
        return false;
    }
    if (_lnDock) { _lnFree = 0; return false; }              // state 1: still locked
    if (++_lnFree < 2 && !changed) return false;             // (two checks in a row, so that one odd reading does not launch)
    if (_lnAuto || _lnForce) { if (LnReady()) { LnBegin(); return LnBurn(dt, idle, startMode, cmd); } return false; }
    _lnS = 0;                                                // Auto=false: wait for the "launch" command
    return false;
}

string LaunchStatus()
{
    if (_lnS == 1) return "Launch: docked, waiting for the release\n";
    if (_lnS == 2) return "Launch: take-off burn " + _lnT.ToString("0.0") + " s\n";
    if (_lnS == 3) return _lnMsg != "" ? "Launch: " + _lnMsg + "\n" : "";
    if (_lnFused) return "Launch: held, this grid holds a printer\n";
    return _lnArmed ? "Launch: armed\n" : "";
}
// <<< LAUNCH <<<

public Program()
{
    for (int i = 0; i < 6; i++) _thrDir[i] = new List<IMyThrust>();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    try { Load(); FleetLoad(true); LaunchLoad(); GetBlocks(); } catch (Exception ex) { _err = "init: " + ex.Message; }
}

void Load()
{
    _ini.Clear();
    MyIniParseResult pr;
    if (!_ini.TryParse(Me.CustomData, out pr)) _ini.Clear();
    Tag = Cfg("IGC Tag", Tag).ToUpperInvariant(); HostTag = Cfg("Host Tag", HostTag).Trim().ToUpperInvariant();
    SeekGroup = Cfg("Seeker Group", SeekGroup).Trim(); StartMode = Cfg("Start Mode", StartMode).Trim().ToUpperInvariant();
    EngageRange = Cfg("Engage Range", EngageRange); LeashRange = Cfg("Leash Range", LeashRange); MinRadius = Cfg("Min Target Radius", MinRadius);
    OrbitMargin = Cfg("Orbit Margin", OrbitMargin); OrbitMin = Cfg("Orbit Min", OrbitMin); OrbitSpeed = Cfg("Orbit Speed", OrbitSpeed);
    MaxSpeed = Cfg("Max Speed", MaxSpeed); TurnRate = Cfg("Turn Rate", TurnRate); SettleSec = Cfg("Settle Seconds", SettleSec);
    LeadSpeed = Cfg("Lead Speed", LeadSpeed); RayPast = Cfg("Ray Past", RayPast); ParkAfter = Cfg("Park After", ParkAfter);
    TriTol = Cfg("Line Tolerance", TriTol); HomeStop = Cfg("Home Stop", HomeStop);
    ShareTargets = Cfg("Share Targets", ShareTargets); ShareWp = Cfg("Share Weakpoints", ShareWp);
    WingSide = Cfg("Wing Side", WingSide); Sweep = Cfg("Sweep Angle", Sweep); SendRaw = Cfg("Send Unconfirmed", SendRaw);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    if (_lHome != null) IGC.DisableBroadcastListener(_lHome);
    _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME");
}
const string SEC = "Pathfinder";
string Cfg(string k, string d) { if (!_ini.ContainsKey(SEC, k)) _ini.Set(SEC, k, d); return _ini.Get(SEC, k).ToString(d); }
double Cfg(string k, double d) { if (!_ini.ContainsKey(SEC, k)) _ini.Set(SEC, k, d); return _ini.Get(SEC, k).ToDouble(d); }
bool Cfg(string k, bool d) { if (!_ini.ContainsKey(SEC, k)) _ini.Set(SEC, k, d); return _ini.Get(SEC, k).ToBoolean(d); }

void GetBlocks()
{
    _thr.Clear(); _gyros.Clear(); _cams.Clear(); _tur.Clear(); _mrg.Clear(); _bay.Clear();
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_gyros, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_cams, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_tur, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_mrg, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_bay, b => b.CubeGrid == Me.CubeGrid && b != Me && (b.CustomName.Contains("[Bay") || (b is IMyProgrammableBlock && b.CustomData.Contains("[Hive Mother]"))));
    var rcs = new List<IMyRemoteControl>();
    GridTerminalSystem.GetBlocksOfType(rcs, b => b.CubeGrid == Me.CubeGrid);
    _rc = rcs.Count > 0 ? rcs[0] : null;
    foreach (var c in _cams) c.EnableRaycast = true;
    for (int i = 0; i < 6; i++) _thrDir[i].Clear();
    if (_rc != null)
    {
        MatrixD m = _rc.WorldMatrix;
        Vector3D[] dirs = { m.Forward, m.Backward, m.Left, m.Right, m.Up, m.Down };
        foreach (var t in _thr)
        {
            Vector3D push = t.WorldMatrix.Backward;       // thrusters push opposite their exhaust
            for (int i = 0; i < 6; i++) if (Vector3D.Dot(push, dirs[i]) > 0.9) { _thrDir[i].Add(t); break; }
        }
        double mass = Math.Max(1, _rc.CalculateShipMass().PhysicalMass), lo = double.MaxValue;
        for (int i = 0; i < 6; i++) { double f = 0; foreach (var t in _thrDir[i]) if (t.IsWorking) f += t.MaxEffectiveThrust; lo = Math.Min(lo, f / mass); }
        _amax = MathHelper.Clamp(lo, 0.5, 30);           // the weakest axis: the orbit and the braking must work on every side
    }
    _lcd = Me.GetSurface(0);
    _lcd.ContentType = ContentType.TEXT_AND_IMAGE;
    _lcd.FontSize = 0.8f;
}

public void Main(string arg, UpdateType u)
{
    _tick++;
    double dt = Runtime.TimeSinceLastRun.TotalSeconds;
    _now += dt;
    try
    {
        if ((u & (UpdateType.Terminal | UpdateType.Trigger)) != 0 && arg != "") Command(arg.Trim().ToLowerInvariant());
        if (_tick % 60 == 1) GetBlocks();
        ReadIGC();
        if (LaunchStep(dt, _mode == Mode.IDLE, StartMode, _homeSet, _home, Command)) { if (_tick % 20 == 0) Display(); return; }
        if (_rc == null || !_rc.IsFunctional) { _err = "no Remote Control"; Release(); Display(); return; }
        Step(Math.Max(0.016, dt));
        if (_now - _lastBeat > 1) { _lastBeat = _now; Beat(); }
        if (_tick % 20 == 0) Display();
    }
    catch (Exception ex) { _err = ex.GetType().Name + ": " + ex.Message; Release(); }
}

void Command(string c)
{
    int sp = c.IndexOf(' '); string w = sp < 0 ? c : c.Substring(0, sp);
    switch (w)
    {
        case "wait": case "hunt": case "guard": case "attack": case "scout": if (!Docked()) SetMode(Mode.WAIT); break;
        case "hold": case "park": case "idle": case "stop": if (_mode != Mode.IDLE) SetMode(Mode.HOLD); break;
        case "rtb": if (_mode != Mode.IDLE) SetMode(Mode.HOME); break;
        case "home": if (_rc != null && _now - _fleetHomeT > 10) { _home = _rc.GetPosition(); _homeSet = true; } break;
        case "reload": Load(); FleetLoad(true); LaunchLoad(); GetBlocks(); break;
        case "armed": case "launch": case "disarm": LaunchCmd(w); break;
    }
}

bool Docked()
{
    foreach (var m in _mrg) if (m.IsConnected) return true;
    return _bay.Count > 0;
}

void SetMode(Mode m) { if (_mode != m) { _mode = m; _modeT = _now; _orbKey = -1; if (m != Mode.SCOUT) _tid = 0; } }

void ReadIGC()
{
    foreach (var l in FleetTgtL)
        while (l.HasPendingMessage)
        {
            var msg = l.AcceptMessage();
            if (!(msg.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)msg.Data;
            if (!FleetOk(d) || d.Item5 == Me.EntityId) continue;
            if (d.Item4 < 0) { _cts.Remove(d.Item1); _ign[d.Item1] = _now + 120; continue; }      // target finished
            if (_ign.ContainsKey(d.Item1)) continue;
            Ct c; if (!_cts.TryGetValue(d.Item1, out c)) { if (_cts.Count >= 32) continue; c = new Ct(); _cts[d.Item1] = c; }
            if (_now - c.T < 0.3 && c.T == _upT) continue;                                  // my own turret sighting is younger: keep it
            c.P = d.Item2; c.V = d.Item3; c.R = d.Item4; c.T = _now;
        }
    FleetPump(Command, p => { _home = p; _homeSet = true; _fleetHomeT = _now; });
    while (_lHome.HasPendingMessage)
    {
        var msg = _lHome.AcceptMessage();
        if (msg.Data is Vector3D && FleetV((Vector3D)msg.Data)) { _home = (Vector3D)msg.Data; _homeSet = true; _fleetHomeT = _now; }
    }
    if (_tick % 120 == 7)
    {
        _del.Clear();
        foreach (var kv in _cts) if (_now - kv.Value.T > 60) _del.Add(kv.Key);
        foreach (var k in _del) _cts.Remove(k);
        _del.Clear();
        foreach (var kv in _ign) if (_now > kv.Value) _del.Add(kv.Key);
        foreach (var k in _del) _ign.Remove(k);
    }
}

Vector3D CtPos(Ct c) { return c.P + c.V * (_now - c.T); }

// the biggest fresh contact inside Engage Range (ties: the nearest)
long Pick(Vector3D me)
{
    long best = 0; double br = -1, bd = 0;
    foreach (var kv in _cts)
    {
        var c = kv.Value;
        if (_now - c.T > 5 || c.R < MinRadius) continue;
        double d = (CtPos(c) - me).Length();
        if (d > EngageRange) continue;
        if (c.R > br + 0.5 || (Math.Abs(c.R - br) <= 0.5 && d < bd)) { best = kv.Key; br = c.R; bd = d; }
    }
    return best;
}

void Step(double dt)
{
    Vector3D pos = _rc.GetPosition(), vel = _rc.GetShipVelocities().LinearVelocity;
    if (_mode == Mode.IDLE)
    {
        // pasted or bench-tested (never docked, nothing armed): start working on our own after 3 s; on the printer: stay off
        if (Docked() || _lnS == 1 || _lnArmed || _now < 3) { Release(); return; }
        if (!_homeSet) { _home = pos; _homeSet = true; }
        SetMode(StartMode == "HOLD" ? Mode.HOLD : StartMode == "IDLE" ? Mode.IDLE : Mode.WAIT);
        if (_mode == Mode.IDLE) { Release(); return; }
    }
    if (!_homeSet) { _home = pos; _homeSet = true; }
    Vector3D wantV = Vector3D.Zero, face = Vector3D.Zero;
    Ct t = null;
    if (_mode == Mode.WAIT)
    {
        long id = Pick(pos);
        if (id != 0) { SetMode(Mode.SCOUT); _tid = id; _log = "target " + (CtPos(_cts[id]) - pos).Length().ToString("0") + " m away: going"; }
        else { Release(); return; }
    }
    if (_mode == Mode.SCOUT)
    {
        if (_tid == 0 || !_cts.TryGetValue(_tid, out t) || _now - t.T > ParkAfter || (CtPos(t) - pos).Length() > LeashRange)
        {
            long id = Pick(pos);                         // the next one in range, else home
            if (id != 0 && id != _tid) { _tid = id; _orbKey = -1; t = _cts[id]; }
            else { SetMode(Mode.HOME); _log = "target lost: home"; t = null; }
        }
    }
    if (_mode == Mode.SCOUT && t != null)
    {
        Vector3D tp = CtPos(t);
        double R = Math.Max(OrbitMin, t.R + OrbitMargin);
        double near = (tp - pos).Length();
        Vector3D off;
        if (WingSide)
        {   // a circle on the side the wing comes from (the home point): the block found is then on the snipers' side of the hull too
            if (_orbKey < 0) { _axis = (_home - tp).LengthSquared() > R * R ? Vector3D.Normalize(_home - tp) : Vector3D.Normalize(pos - tp); _orbKey = 1; _psi = -99; }
            else if ((_home - tp).LengthSquared() > R * R) _axis = Vector3D.Normalize(_axis * 0.995 + Vector3D.Normalize(_home - tp) * 0.005);
            Vector3D b1 = Vector3D.Cross(_axis, Math.Abs(_axis.Y) < 0.9 ? Vector3D.Up : Vector3D.Right); b1.Normalize();
            Vector3D b2 = Vector3D.Cross(_axis, b1);
            double al = MathHelper.Clamp(Sweep, 10, 80) * Math.PI / 180;
            if (_psi < -9) { Vector3D rel = pos - tp; _psi = Math.Atan2(Vector3D.Dot(rel, b2), Vector3D.Dot(rel, b1)); }   // the nearest point of the circle first
            if (near < R * 1.5) _psi += Math.Min(OrbitSpeed, Math.Sqrt(0.5 * _amax * R * Math.Sin(al))) / (R * Math.Sin(al)) * dt;   // centripetal within half the thrust
            off = (_axis * Math.Cos(al) + (b1 * Math.Cos(_psi) + b2 * Math.Sin(_psi)) * Math.Sin(al)) * R;
        }
        else
        {
            if (_orbKey < 0)
            {   // start the orbit on the bearing the drone comes from: the first leg is straight in and never crosses the target
                Vector3D rel = pos - tp; double rl = rel.Length();
                _theta = _theta0 = Math.Atan2(rel.Z, rel.X); _phi0 = rl > 1 ? Math.Asin(MathHelper.Clamp(rel.Y / rl, -1, 1)) : 0; _orbKey = 1;
            }
            if (near < R * 1.5) _theta += Math.Min(OrbitSpeed, Math.Sqrt(0.5 * _amax * R)) / R * dt;      // circle only once there (centripetal within half the thrust)
            double phi = MathHelper.Clamp(_phi0 + 0.7 * Math.Sin(0.37 * (_theta - _theta0)), -1.2, 1.2);   // the tilt sweeps up and down: every side gets its turn
            off = new Vector3D(Math.Cos(_theta) * Math.Cos(phi), Math.Sin(phi), Math.Sin(_theta) * Math.Cos(phi)) * R;
        }
        Vector3D e = tp + off - pos;
        double sp = Math.Min(MaxSpeed, Math.Sqrt(2 * 0.8 * _amax * Math.Max(0, e.Length() - 2)) + 0.3);   // brake in time
        wantV = t.V + (e.LengthSquared() > 0.01 ? Vector3D.Normalize(e) * sp : Vector3D.Zero);
        face = tp - pos; _tvD = face.Length();
        if (near < 1500) Sample(t, tp);
        else _log = "going, " + near.ToString("0") + " m";
    }
    else if (_mode == Mode.HOME)
    {
        if (Pick(pos) != 0) { SetMode(Mode.WAIT); Release(); return; }
        Vector3D e = _home - pos; double el = e.Length();
        if (el < HomeStop && vel.Length() < 2) { SetMode(Mode.WAIT); Release(); return; }
        double sp = Math.Min(MaxSpeed, Math.Sqrt(2 * 0.8 * _amax * Math.Max(0, el - 5)) + 0.3);
        wantV = el > 0.1 ? e / el * sp : Vector3D.Zero;
        if (el > 20) face = e;
    }
    else { Release(); return; }                          // HOLD: dampeners keep it where it is
    Fly(wantV, vel);
    if (face.LengthSquared() > 1) Aim(face); else HoldAttitude();
    if (ShareWp) WpBeat();
}

void Fly(Vector3D wantV, Vector3D vel)
{
    double mass = _rc.CalculateShipMass().PhysicalMass;
    Vector3D grav = _rc.GetNaturalGravity();
    Vector3D a = (wantV - vel) * 1.5;
    double am = a.Length(), cap = Math.Max(2, _amax * 2); if (am > cap) a *= cap / am;
    ApplyThrust((a - grav) * mass);
}

double _powerScale = 1; IMyTerminalBlock _aimRef { get { return _rc; } }   // (THRUST VECTOR LINK block: no power throttle, the controller is the reference)
void ApplyThrust(Vector3D f)
{
    _tvN = f; _released = false;
    _rc.DampenersOverride = false;
    MatrixD m = _rc.WorldMatrix;
    double[] comp = { Vector3D.Dot(f, m.Forward), -Vector3D.Dot(f, m.Forward), -Vector3D.Dot(f, m.Right), Vector3D.Dot(f, m.Right), Vector3D.Dot(f, m.Up), -Vector3D.Dot(f, m.Up) };
    for (int i = 0; i < 6; i++)
    {
        double max = 0;
        foreach (var t in _thrDir[i]) if (t.IsWorking) max += t.MaxEffectiveThrust;
        float pct = (comp[i] > 0 && max > 0) ? (float)MathHelper.Clamp(comp[i] / max, 0, 1) : 0f;
        foreach (var t in _thrDir[i]) t.ThrustOverridePercentage = pct;
    }
}

void Aim(Vector3D fwd)
{
    Vector3D tvU = Vector3D.Zero, tvF = Vector3D.Zero; TvAim(ref fwd, ref tvU, ref tvF);
    MatrixD refM = _rc.WorldMatrix;
    Vector3D d = Vector3D.Normalize(Vector3D.TransformNormal(fwd, MatrixD.Transpose(refM)));
    double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(Vector3D.Forward, d), -1, 1));
    Vector3D axis = Vector3D.Cross(Vector3D.Forward, d);
    axis = axis.LengthSquared() < 1e-12 ? (ang > 1 ? Vector3D.Up : Vector3D.Zero) : Vector3D.Normalize(axis);
    Vector3D w = Vector3D.TransformNormal(axis * ang * 2.5, refM);
    if (w.LengthSquared() > TurnRate * TurnRate) w = Vector3D.Normalize(w) * TurnRate;
    Gyros(w);
}
void HoldAttitude() { Gyros(Vector3D.Zero); }
void Gyros(Vector3D w)
{
    foreach (var g in _gyros)
    {
        if (!g.IsFunctional) continue;
        Vector3D l = Vector3D.TransformNormal(w, MatrixD.Transpose(g.WorldMatrix));
        g.Pitch = -(float)l.X; g.Yaw = -(float)l.Y; g.Roll = -(float)l.Z;      // SE gyro sign convention (confirmed in game for the drone scripts)
        g.GyroOverride = true;
    }
}

bool _released;
void Release()
{
    if (_released && _tick % 60 != 0) return;
    _released = true;
    foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = g.Yaw = g.Roll = 0; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
}

// ---------------------------------------------------------------------------------------------------------------
//  WEAKPOINT FINDING: aim lines -> hull entry + the block where the lines cross (target frame)
// ---------------------------------------------------------------------------------------------------------------
// one block hypothesis: the lines that pass within Line Tolerance of one point
class Cl { public long Tid; public List<Vector3D> P = new List<Vector3D>(), U = new List<Vector3D>(), S = new List<Vector3D>(), D = new List<Vector3D>(); public List<double> T = new List<double>(); public Vector3D V; public bool Ok, Hp; public double Res, Spread; }
List<Cl> _cls = new List<Cl>();
Cl _best; double _bestT = -99, _tol = 3;

int GroupKind() { return SeekGroup == "PowerSystems" ? 1 : SeekGroup == "Weapons" ? 2 : SeekGroup == "Propulsion" ? 3 : 4; }

void Sample(Ct t, Vector3D tp)
{
    if (_tur.Count == 0) { _log = "no turret"; return; }
    foreach (var tu in _tur)
    {
        if (!tu.IsFunctional) { _log = "turret damaged"; continue; }
        string cur;
        try { cur = tu.GetTargetingGroup(); } catch (Exception) { cur = ""; }
        if (SeekGroup.Length > 0 && cur != SeekGroup)
        {
            try { tu.SetTargetingGroup(SeekGroup); _swT = _now; } catch (Exception x) { _log = "group not set: " + x.Message; }
            continue;
        }
        if (_now - _swT < SettleSec || _now - _lastSample < 0.4) continue;
        if (!tu.HasTarget) { _log = "turret has no target"; continue; }
        var te = tu.GetTargetedEntity();
        if (te.EntityId != _tid) { _log = "turret on another target"; continue; }
        // the turret's own sighting keeps the track fresh (and goes to the fleet)
        t.P = te.Position; t.V = te.Velocity; t.T = _now; _upT = _now;
        if (ShareTargets && _now - _shareT > 0.5 && !te.IsEmpty())
        {
            _shareT = _now;
            FleetSend(new MyTuple<long, Vector3D, Vector3D, double, long>(te.EntityId, te.Position, te.Velocity, Math.Max(t.R, te.BoundingBox.Size.Length() / 2), Me.EntityId));
        }
        if (!tu.IsAimed) { _log = "turret turning"; continue; }
        Vector3D o = tu.GetPosition(), q;
        bool hp = te.HitPosition.HasValue && (te.HitPosition.Value - te.Position).LengthSquared() > 1;
        if (hp) q = te.HitPosition.Value;                // the game told us the block itself
        else
        {
            Vector3D d; Vector3D.CreateFromAzimuthAndElevation(tu.Azimuth, tu.Elevation, out d);
            d = Vector3D.TransformNormal(d, tu.WorldMatrix);
            double D = Vector3D.Dot(te.Position - o, d);
            if (D < 5) { _log = "turret points away"; continue; }
            q = o + d * D;
            if (LeadSpeed > 1) q -= (te.Velocity - _rc.GetShipVelocities().LinearVelocity) * (D / LeadSpeed);   // turrets lead; take it off again
        }
        IMyCameraBlock cam = null;
        foreach (var c in _cams)
        {
            if (!c.IsFunctional) continue;
            Vector3D cd0 = q - c.GetPosition();
            if (cd0.LengthSquared() < 1) continue;
            if (Vector3D.Dot(Vector3D.Normalize(cd0), c.WorldMatrix.Forward) < 0.7) continue;
            if (c.CanScan(cd0.Length() + RayPast)) { cam = c; break; }
        }
        if (cam == null) { _log = "no camera can reach"; continue; }
        Vector3D cp = cam.GetPosition(), cd = q - cp; double cl = cd.Length(); cd /= cl;
        var e = cam.Raycast(q + cd * RayPast);
        if (e.IsEmpty() || e.EntityId != _tid || !e.HitPosition.HasValue) { _log = "ray missed hull"; continue; }
        _lastSample = _now;
        // the turret's line (o -> q) goes to the block; the camera's ray gives the hull entry s and the drill direction d
        MatrixD inv = MatrixD.Transpose(e.Orientation);
        Vector3D sL = Vector3D.TransformNormal(e.HitPosition.Value - e.Position, inv), dL = Vector3D.TransformNormal(cd, inv);
        Vector3D pL = Vector3D.TransformNormal(o - e.Position, inv), uL = Vector3D.Normalize(Vector3D.TransformNormal(q - o, inv));
        _tol = TriTol + 0.015 * (q - o).Length();         // 1 deg of turret aim error is 1.7 m at 100 m
        var k = AddLine(_tid, pL, uL, sL, dL, t.R, hp, hp ? Vector3D.TransformNormal(q - e.Position, inv) : Vector3D.Zero);
        _lines++;
        if (HostTag != "")
            IGC.SendBroadcastMessage(HostTag + "_CRIT", new MyTuple<long, int, Vector3D, Vector3D, long>(_tid, GroupKind() - 1, sL, dL, Me.CubeGrid.EntityId));
        _log = SeekGroup + ": " + (hp ? "block pos" : "aim line") + ", " + (k.Ok ? "block " + (k.V - sL).Length().ToString("0") + " m under hull (" + k.P.Count + " lines)"
            : (q - e.HitPosition.Value).Length().ToString("0") + " m under hull, " + k.P.Count + " line(s)");
        Choose();
        if (_best == k) WpSend(true);
        return;
    }
}

// a line from p along unit u, through hull entry s: join the block hypothesis it fits, else start a new one
Cl AddLine(long tid, Vector3D p, Vector3D u, Vector3D s, Vector3D sd, double rad, bool hp, Vector3D hpL)
{
    for (int i = _cls.Count - 1; i >= 0; i--)
    {
        var c = _cls[i];
        for (int j = c.T.Count - 1; j >= 0; j--) if (_now - c.T[j] > 60) Drop(c, j);
        if (c.T.Count == 0 || (c.Tid != tid && _now - c.T[c.T.Count - 1] > 20)) { if (_best == c) _best = null; _cls.RemoveAt(i); }
    }
    Cl best = null; double bd = _tol;
    foreach (var c in _cls)
    {
        if (c.Tid != tid) continue;
        double d = c.P.Count >= 2 && c.Spread > 0.05 ? LineDist(c.V, p, u) : LineLine(c.P[c.P.Count - 1], c.U[c.U.Count - 1], p, u);
        if (d < bd) { bd = d; best = c; }
    }
    if (best == null)
    {
        if (_cls.Count >= 8) { int w = 0; for (int i = 1; i < _cls.Count; i++) if (_cls[i].T[_cls[i].T.Count - 1] < _cls[w].T[_cls[w].T.Count - 1]) w = i; if (_best == _cls[w]) _best = null; _cls.RemoveAt(w); }
        best = new Cl(); best.Tid = tid; _cls.Add(best);
    }
    best.P.Add(p); best.U.Add(u); best.S.Add(s); best.D.Add(sd); best.T.Add(_now);
    if (best.P.Count > 12) Drop(best, 0);
    if (hp) { best.Hp = true; best.V = hpL; best.Ok = true; return best; }
    Solve(best, rad);
    return best;
}

void Drop(Cl c, int j) { c.P.RemoveAt(j); c.U.RemoveAt(j); c.S.RemoveAt(j); c.D.RemoveAt(j); c.T.RemoveAt(j); }
double LineDist(Vector3D v, Vector3D p, Vector3D u) { Vector3D w = v - p; return (w - u * Vector3D.Dot(w, u)).Length(); }
double LineLine(Vector3D p0, Vector3D u0, Vector3D p, Vector3D u)
{
    Vector3D n = Vector3D.Cross(u0, u); double nl = n.Length();
    if (nl < 0.05) return LineDist(p, p0, u0);           // (nearly) parallel: their distance apart
    return Math.Abs(Vector3D.Dot(p - p0, n / nl));
}

// least-squares point nearest to every line: sum (I - u u') V = sum (I - u u') p
void Solve(Cl c, double rad)
{
    int n = c.P.Count;
    double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0; Vector3D b = Vector3D.Zero;
    c.Spread = 0;
    for (int i = 0; i < n; i++)
    {
        Vector3D u = c.U[i], p = c.P[i];
        a00 += 1 - u.X * u.X; a01 -= u.X * u.Y; a02 -= u.X * u.Z; a11 += 1 - u.Y * u.Y; a12 -= u.Y * u.Z; a22 += 1 - u.Z * u.Z;
        b += p - u * Vector3D.Dot(u, p);
        for (int j = 0; j < i; j++) c.Spread = Math.Max(c.Spread, Math.Acos(MathHelper.Clamp(Vector3D.Dot(u, c.U[j]), -1, 1)));
    }
    if (c.Hp) return;
    double c00 = a11 * a22 - a12 * a12, c01 = a02 * a12 - a01 * a22, c02 = a01 * a12 - a02 * a11;
    double det = a00 * c00 + a01 * c01 + a02 * c02;
    c.Ok = false;
    if (n < 2 || c.Spread < 0.05 || Math.Abs(det) < 1e-9) return;
    double c11 = a00 * a22 - a02 * a02, c12 = a01 * a02 - a00 * a12, c22 = a00 * a11 - a01 * a01;
    c.V = new Vector3D(c00 * b.X + c01 * b.Y + c02 * b.Z, c01 * b.X + c11 * b.Y + c12 * b.Z, c02 * b.X + c12 * b.Y + c22 * b.Z) / det;
    c.Res = 0;
    for (int i = 0; i < n; i++) c.Res = Math.Max(c.Res, LineDist(c.V, c.P[i], c.U[i]));
    Vector3D s = c.S[n - 1], d = c.D[n - 1];
    double h = Vector3D.Dot(c.V - s, d);
    // a block: >= 3 lines from >= 6 deg apart that cross within the tolerance, inside the target, not in front of the hull
    c.Ok = n >= 3 && c.Spread >= 0.1 && c.Res <= _tol && c.V.Length() < rad + 5 && h > -2 && h < 80;
}

// the hypothesis to report: confirmed blocks first, then the most recent lines (a switch needs a clear margin)
void Choose()
{
    Cl b = null; double bs = -1;
    foreach (var c in _cls)
    {
        if (c.Tid != _tid) continue;
        int k = 0; foreach (var x in c.T) if (_now - x < 20) k++;
        double sc = (c.Ok ? 100 : 0) + k + (c == _best ? 2 : 0);
        if (sc > bs) { bs = sc; b = c; }
    }
    if (b != _best) { _best = b; _bestT = _now; }
}

void WpBeat() { if (_best != null && _best.Tid == _tid && _now - _wpT >= 2 && _now - _best.T[_best.T.Count - 1] < 20) WpSend(false); }
void WpSend(bool fresh)
{
    if (!ShareWp || _flChan == "" || _best == null || !(_best.Ok || SendRaw)) return;   // only a block the lines confirm: a stray hull point would pull the wing off its own hole
    var c = _best; int n = c.P.Count;
    Vector3D s = c.S[n - 1], d = c.D[n - 1]; double hint = -1;
    if (c.Ok) { Vector3D w = c.V - s; hint = w.Length(); if (hint > 0.5) d = w / hint; else hint = 0; }   // d through the block: V = s + d * hint exactly
    int f = GroupKind() | 8 | (c.Hp ? 16 : 0);
    IGC.SendBroadcastMessage(_flChan + "_WP", new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(c.Tid, s, d, new Vector3D(0, hint, _now - c.T[n - 1]), Me.CubeGrid.EntityId, f));
    _wpT = _now; _sent++;
}

void Beat()
{
    Vector3D v = _rc != null ? _rc.GetShipVelocities().LinearVelocity : Vector3D.Zero;
    IGC.SendBroadcastMessage(Tag + "_SWARM", new MyTuple<long, Vector3D, Vector3D, long, int>(Me.CubeGrid.EntityId, Me.GetPosition(), v, _tid, 0));
}

void Display()
{
    _sb.Clear();
    _sb.Append("PATHFINDER v").Append(VER).Append("  ").Append(_mode).Append('\n');
    Ct t; bool ht = _tid != 0 && _cts.TryGetValue(_tid, out t);
    _sb.Append("Fleet ").Append(_flChan == "" ? "off" : _flChan).Append("  contacts ").Append(_cts.Count).Append("  range ").Append((EngageRange / 1000).ToString("0.#")).Append(" km\n");
    if (ht) { t = _cts[_tid]; _sb.Append("Target ").Append((CtPos(t) - Me.GetPosition()).Length().ToString("0")).Append(" m, r ").Append(t.R.ToString("0")).Append('\n'); }
    _sb.Append("Group ").Append(SeekGroup).Append("  lines ").Append(_lines).Append("  sent ").Append(_sent).Append('\n');
    if (_best != null)
        _sb.Append("Block: ").Append(_best.Ok ? (_best.Hp ? "game pos" : "crossed, " + _best.P.Count + " lines, " + _best.Res.ToString("0.0") + " m") : "unconfirmed, " + _best.P.Count + " line(s)").Append('\n');
    _sb.Append(_log).Append('\n');
    _sb.Append(LaunchStatus());
    _sb.Append("Thr ").Append(_thr.Count).Append(" Cam ").Append(_cams.Count).Append(" Tur ").Append(_tur.Count).Append(" a ").Append(_amax.ToString("0.0")).Append('\n');
    _sb.Append("Ins ").Append(Runtime.CurrentInstructionCount).Append('/').Append(Runtime.MaxInstructionCount).Append('\n');
    if (_err != "") _sb.Append("! ").Append(_err).Append('\n');
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
