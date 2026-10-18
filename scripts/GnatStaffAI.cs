// =====================================================================
//  GNAT STAFF AI  v1.0-gnat-split  - the STAFF half of the two-PB Gnat ("[Staff] ..." Programmable Block, next to "[Flight] ..." = GnatSwarmAI.cs)
//  For Space Engineers (vanilla, small grid). Paste into a second Programmable Block on the SAME grid as the Flight PB, named "[Staff] ...".
//  Everything the Gnat does that is not the hot path (docs/design/gnat_split.md):
//   * FLEET LINK   fleet channel (targets in, target shares out, home, carrier radius, commands) - same [Fleet] section as the single-PB Gnat
//   * LAUNCH       take-off after the release from a printer - same [Launch] section; Staff flies the burn, Flight lets go of the ship
//   * DISPLAY      mode, target, aim error, battery, alarm, launch and link state
//   * SURVIVAL     STAFF SEARCH (sensors, seeker), SURVIVAL STAFF (damage RTB, limp, threats, missiles, gather), REPAIR CLIENT (medic) - docs/design/survivability.md
//   * WATCHDOG     the Flight PB silent for 1.5 s (or switched off / destroyed): gyro and thruster overrides released once, dampeners on, "FLIGHT PB DOWN"
//  Link (TransmissionDistance.CurrentConstruct, key "WSPL" + the Flight PB's EntityId; both PBs run Update1) - see the SPLIT LINK block of GnatSwarmAI.cs.
//  Gnat only: ".HMR" (Staff -> Flight, double) is the carrier radius from "<fleet channel>_HOMER"; FOL bit 3 is sent ONCE per new fleet home fix (the Flight derives the carrier's
//  velocity from successive fixes, so a repeated point would read as a stopped carrier).
//  The Staff finds the nearest "[Flight]" PB of its construct (re-checked every 10 s and when the grid changes).
//  CUSTOM DATA (this PB): [Launch], [Fleet] (written on first run). IGC Tag and Start Mode are read from the Flight PB's Custom Data ([Gnat] section), so nothing is configured twice.
//  Commands (PB argument, on EITHER PB): the Staff's words (launch, armed, disarm, reload) are handled here; the Staff forwards every word to the Flight PB and the Flight PB forwards its argument to the Staff.
// =====================================================================

const string VER = "1.0-gnat-split";

// ---------------- settings (read from the Flight PB's Custom Data) -----
string Tag = "GNAT", StartMode = "SENTRY";
double MaxSpeed = 98;

// survivability (docs/design/survivability.md): the Flight PB's Custom Data section, which STAFF SEARCH modules this model has, its orbit speed key
readonly string FS = "Gnat", SvOrbK = "Orbit Speed"; const double SvOrbD = 12;
readonly bool SsPatrol = false, SsFeed = false, SsFoot = false, RpDockOk = false;
bool SsHunt() { return false; }
bool SvFightMode() { return _mode == Mode.ATTACK; }   // a fight mode: an attack-together gather window may open
string SvVerb() { return _mode.ToString().ToLowerInvariant(); }   // the command word of the current mode (resumed after a repair)

// ---------------- state ----------------------------------------------
enum Mode { IDLE, FORM, ATTACK, RTB }
Mode _mode = Mode.IDLE;
double _now = 0; long _tick = 0;
Vector3D _dynHome; double _dynHomeT = -99;
IMyShipController _rc; IMyProgrammableBlock _fpb; IMyTextSurface _lcd; IMyCubeGrid _myGrid;
List<IMyGyro> _gyros = new List<IMyGyro>(); List<IMyThrust> _thr = new List<IMyThrust>(); List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
List<IMyFunctionalBlock> _ai = new List<IMyFunctionalBlock>(); List<IMySensorBlock> _sensors = new List<IMySensorBlock>();
StringBuilder _sb = new StringBuilder();
string _fk = "", _sig = "", _lsKey = "", _crash = "";
IMyBroadcastListener _lHome, _lCmd, _lSwarm, _lF2S, _lFlt, _lArg, _lHmr; string _hmrKey = null;
// mirror of the Flight PB (F2S)
int _fl, _f2sN; long _f2sTick, _tgtId; double _f2sT = -99, _aimErr, _fireWin, _aBrake = 5, _relT = -99; float _batt; string _gate = ""; Vector3D _tgtPos;
bool _down, _folBurn, _fhNew, _hmrNew; int _errRun; Vector3D _fhP; double _hmrV, _hmrT = -99; List<long> _gone = new List<long>();

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

// =====================================================================
//  HIVE MATES (lite): position, velocity, target and SWARM flags of the wingmates (gather slots, hand-off, patrol fan-out, missile relay)
class Mate { public long Id; public Vector3D Pos, Vel; public double Seen; public long Tgt; public int Flags; }
Dictionary<long, Mate> _mates = new Dictionary<long, Mate>();
void ReadSwarm()
{
    while (_lSwarm.HasPendingMessage)
    {
        var m = _lSwarm.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, long, int>)m.Data;
        if (d.Item1 == Me.CubeGrid.EntityId) continue;
        Mate mt;
        if (!_mates.TryGetValue(d.Item1, out mt)) { mt = new Mate { Id = d.Item1 }; _mates[d.Item1] = mt; }
        mt.Pos = d.Item2; mt.Vel = d.Item3; mt.Tgt = d.Item4; mt.Flags = d.Item5; mt.Seen = _now;
    }
    _gone.Clear(); foreach (var kv in _mates) if (_now - kv.Value.Seen > 3) _gone.Add(kv.Key); foreach (var id in _gone) _mates.Remove(id);
}

// =====================================================================
//  SETUP
void CtorErr(string w, Exception ex) { _crash = "setup " + w + ": " + ex.Message; Echo(_crash); }

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

// >>> STAFF SEARCH v1.1 (patrol, sensor setup, target feeds, seeker keys; moved from the Flight PB, docs/design/survivability.md) >>>
// The four search modules that left the Flight PB (off the hot path, docs/design/survivability.md s1.1). Keys keep their names in the Flight PB's [<FS>] section;
// the Staff reads them there and writes missing defaults (SvDef, after the first F2S). The Flight's Target Small/Large Grids, Target Neutral/Unowned and Target
// Characters keys are read for the sensors.
//  * SEARCH PATROL (SsPatrol): HUNT with no target for "Search Patrol Starts After (s)" (after a fight: "... After A Fight (s)"): chords of a square of
//    "Search Patrol Radius (m)" round home, with "Listening Stop (s)" halts for the AI blocks; a moving AI (it has found something) holds the stop up to 90 s.
//    .PAT (Staff -> Flight) MyTuple<Vector3D point, double speed, int flags>: bit0 leg active (fly to point), bit1 listening stop (hold point); every 30 ticks
//    and on a change (the Flight drops it after 1.5 s). Mates fan out: start corner by rank among the hive mates, direction and radius from the grid id.
//  * SENSOR SETUP: every sensor of the construct is set up in GetBlocks (50 m all round, enemies, neutral / small / large by the Flight's keys, owner on foot
//    when SsFoot (FOLLOW), players when SsFoot or Target Characters).
//  * TARGET FEEDS (SsFeed): other fleets' "<TAG>_TGT" ("Listen To IGC Tags (target feeds)", own Tag skipped) join the coalesced .TGT relay to the Flight.
//  * SEEKER KEYS: "Seeker Group" and "Seeker Turret Guides Aim" (SeekerGroup, SeekerOn; "seeker on" / "seeker off" Staff word). v1.1: the turret reader is the
//    SEEKER block (tools/sk_block.cs: local in Gnat / Basilisk, in the [Aux] PB for the other 7, reached through AUX LINK's SkBlocks/SkStep/SkStatus proxies).
// Per-script: readonly string FS; readonly bool SsPatrol, SsFeed, SsFoot; bool SsHunt() (the mode is HUNT).
// Hooks: SsBlocks() in GetBlocks after LoadFlightCfg(); SsCfg(ini) after SvCfg(ini) in LoadFlightCfg; SsFeeds() first in Relay(); SsStep() (called by SvWatch).
// Uses: _rc, _fk, _now, _tick, _mode, _fl, _tgtId, _sensors, _mates, _rel, _flChan, Tag, Me, FleetOk, SvT/SvBn/SvDn (key reader), SvHomeKnown(), _svHomeP,
//   SEEKER or AUX LINK (SkBlocks, SkStep, SkStatus).
bool PatrolOn = true, PatrolRandom = true, SeekerOn = true, _ssTS = true, _ssTL = true, _ssTN, _ssTC;
double PatrolRadius = 1200, PatrolAfter = 15, PatrolAfterFight = 90, PatrolListen = 6, PatrolMaxLeg = 40, PatrolSpeed = 60, PatrolHold = 90;
bool _patrolLeg, _patrolFrame, _patrolStopSet; double _patrolPhaseT = -99, _noTgtSince, _listenT = -1, _aiMoveT = -99, _ssLastT = -1, _patrolRadF = 1;
int _patrolK, _patrolDir = 1, _ssMd = -1, _ssPatF = -1, _ssMiss; long _ssPatTk = -99;
Vector3D _listenV, _patrolWp, _patrolC, _patrolE1, _patrolE2, _patrolStop, _ssPatSent;
string ListenTags = "HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER", _ssFeedKey = "", SeekerGroup = "PowerSystems";
List<IMyBroadcastListener> _ssFeed = new List<IMyBroadcastListener>();

void SsCfg(MyIni i)
{
    _ssTS = i.Get(FS, "Target Small Grids").ToBoolean(true); _ssTL = i.Get(FS, "Target Large Grids").ToBoolean(true);
    _ssTN = i.Get(FS, "Target Neutral/Unowned").ToBoolean(false); _ssTC = i.Get(FS, "Target Characters").ToBoolean(false);
    if (SsPatrol)
    {
        PatrolOn = SvBn(i, FS, "Search Patrol When Nothing Found (HUNT)", true);
        PatrolRandom = SvBn(i, FS, "Search Patrol Own Start Corner And Direction", true);
        PatrolRadius = SvDn(i, FS, "Search Patrol Radius (m)", 1200);
        PatrolAfter = SvDn(i, FS, "Search Patrol Starts After (s)", 15);
        PatrolAfterFight = SvDn(i, FS, "Search Patrol Starts After A Fight (s)", 90);
        PatrolListen = SvDn(i, FS, "Search Patrol Listening Stop (s)", 6);
        PatrolSpeed = SvDn(i, FS, "Search Patrol Speed", 60);
    }
    if (SsFeed) ListenTags = SvT(i, FS, "Listen To IGC Tags (target feeds)", "HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER");
    SeekerGroup = SvT(i, FS, "Seeker Group (PowerSystems/Weapons/Propulsion)", "PowerSystems").Trim();
    SeekerOn = SvBn(i, FS, "Seeker Turret Guides Aim", true);
}

void SsBlocks()
{
    _ssMiss = 0;
    foreach (var s in _sensors)
    {
        s.Enabled = true;
        SsF(s, "LeftExtend", "Left"); SsF(s, "RightExtend", "Right"); SsF(s, "TopExtend", "Top"); SsF(s, "BottomExtend", "Bottom");
        SsF(s, "FrontExtend", "Front"); SsF(s, "BackExtend", "Back");
        SsB(s, true, "Detect Enemy", "DetectEnemy"); SsB(s, _ssTN, "Detect Neutral", "DetectNeutral");
        SsB(s, SsFoot, "Detect Owner", "DetectOwner"); SsB(s, false, "Detect Friendly", "DetectFriendly");
        SsB(s, _ssTS, "Detect Small Ships", "DetectSmallShips"); SsB(s, _ssTL, "Detect Large Ships", "DetectLargeShips");
        SsB(s, _ssTL, "Detect Stations", "DetectStations"); SsB(s, SsFoot || _ssTC, "Detect Players", "DetectPlayers");
        SsB(s, false, "Detect Asteroids", "DetectAsteroids"); SsB(s, false, "Detect Floating Objects", "DetectFloatingObjects");
        SsB(s, false, "Detect Subgrids", "DetectSubgrids");
    }
    SkBlocks();
    string k = SsFeed ? ListenTags + "|" + Tag : "";
    if (k == _ssFeedKey) return;
    _ssFeedKey = k;
    foreach (var l in _ssFeed) IGC.DisableBroadcastListener(l);
    _ssFeed.Clear();
    if (k != "") foreach (var t in ListenTags.Split(','))
        { string tt = t.Trim().ToUpperInvariant(); if (tt != "" && tt != Tag.ToUpperInvariant()) _ssFeed.Add(IGC.RegisterBroadcastListener(tt + "_TGT")); }
}
void SsF(IMyTerminalBlock b, string n1, string n2)
{
    if (b.GetProperty(n1) != null) b.SetValueFloat(n1, 50f); else if (b.GetProperty(n2) != null) b.SetValueFloat(n2, 50f); else _ssMiss++;
}
void SsB(IMyTerminalBlock b, bool v, string n1, string n2)
{
    if (b.GetProperty(n1) != null) b.SetValueBool(n1, v); else if (b.GetProperty(n2) != null) b.SetValueBool(n2, v); else _ssMiss++;
}

// other fleets' target feeds -> the .TGT relay (coalesced per target id there)
void SsFeeds()
{
    foreach (var l in _ssFeed)
        while (l.HasPendingMessage)
        {
            var m = l.AcceptMessage();
            if (!(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
            if (FleetOk(d)) _rel[d.Item1] = d;
        }
}

void SsStep()
{
    if (_tick % 5 == 2) SsPat();
    SkStep();
}

// SEARCH PATROL (was the Flight's PatrolStep; the Flight's AI-move detection is done here from the RC's motion while the AI blocks are on)
void SsPat()
{
    if ((int)_mode != _ssMd) { _ssMd = (int)_mode; _noTgtSince = _now; _patrolLeg = false; _patrolStopSet = false; }
    if (_tgtId != 0) { _ssLastT = _now; _noTgtSince = _now; _patrolStopSet = false; if (_patrolLeg) { _patrolLeg = false; _patrolPhaseT = _now; } }
    if ((_fl & 64) != 0 && PatrolOn)
    {
        var vs = _rc.GetShipVelocities();
        Vector3D refV = _patrolK == 0 ? Vector3D.Zero : _listenV;   // before the first leg the reference is rest
        if ((vs.LinearVelocity - refV).LengthSquared() > 9 || vs.AngularVelocity.LengthSquared() > 0.0144) _aiMoveT = _now;
    }
    if (!SsPatrol || !PatrolOn || !SsHunt() || (_fl & 16) != 0 || _now - _noTgtSince < (_ssLastT < 0 ? PatrolAfter : PatrolAfterFight) || _rc.GetNaturalGravity().LengthSquared() > 0.25)
        _patrolLeg = false;
    else if (!_patrolLeg)
    {
        if (_listenT < 0 || _listenT < _patrolPhaseT) { _listenT = _now; _listenV = _rc.GetShipVelocities().LinearVelocity; }   // a listening stop begins
        if (_now - _patrolPhaseT >= PatrolListen && !(_now - _listenT < PatrolHold && _now - _aiMoveT < 4))
        {
            if (!_patrolFrame)
            {
                _patrolC = SvHomeKnown() ? _svHomeP : _rc.GetPosition(); _patrolE1 = _rc.WorldMatrix.Forward; _patrolE2 = _rc.WorldMatrix.Right; _patrolFrame = true;
                int rank = 0; foreach (var m in _mates.Values) if (m.Id < Me.CubeGrid.EntityId) rank++;
                double a0 = 6.2831853 * rank / (_mates.Count + 1) + (Math.Abs(Me.CubeGrid.EntityId) % 628) * 0.001;
                Vector3D e1 = _patrolE1 * Math.Cos(a0) + _patrolE2 * Math.Sin(a0);
                _patrolE2 = _patrolE2 * Math.Cos(a0) - _patrolE1 * Math.Sin(a0); _patrolE1 = e1;
                int ph = PatrolRandom ? (int)(Math.Abs(Me.CubeGrid.EntityId) % 997) : 0;
                _patrolDir = ((ph / 8) & 1) == 0 ? 1 : -1; _patrolRadF = PatrolRandom ? 0.8 + 0.1 * ((ph / 16) % 5) : 1;
            }
            double th = _patrolK++ * 1.5707963 * _patrolDir;
            _patrolWp = _patrolC + (_patrolE1 * Math.Cos(th) + _patrolE2 * Math.Sin(th)) * (PatrolRadius * _patrolRadF);
            _patrolLeg = true; _patrolPhaseT = _now; _listenT = -1;
        }
    }
    else if (_now - _patrolPhaseT > PatrolMaxLeg || Vector3D.Distance(_rc.GetPosition(), _patrolWp) < 150)
    { _patrolLeg = false; _patrolPhaseT = _now; _patrolStop = _rc.GetPosition(); _patrolStopSet = true; }
    if (_fk == "") return;
    int f = _patrolLeg ? 1 : _patrolStopSet ? 2 : 0;
    Vector3D p = _patrolLeg ? _patrolWp : _patrolStop;
    if (f == _ssPatF && (f == 0 || (p - _ssPatSent).LengthSquared() < 1) && _tick - _ssPatTk < 30) return;
    _ssPatF = f; _ssPatSent = p; _ssPatTk = _tick;
    IGC.SendBroadcastMessage(_fk + ".PAT", new MyTuple<Vector3D, double, int>(p, PatrolSpeed, f), TransmissionDistance.CurrentConstruct);
}

string SsStatus()
{
    string s = SkStatus();
    if (_patrolLeg) s += "Patrol : leg " + _patrolK + ", " + Vector3D.Distance(_rc.GetPosition(), _patrolWp).ToString("0") + " m to go\n";
    else if (_patrolStopSet && SsHunt()) s += "Patrol : listening stop\n";
    if (_ssMiss > 0) s += "! sensor setup: " + _ssMiss + " properties not found\n";
    return s;
}
// <<< STAFF SEARCH v1.1 <<<

// >>> SURVIVAL STAFF v1.1 (damage RTB, limp, failover, threat memory, gather, dock actuator, docs/design/survivability.md) >>>
// The Staff side of the survivability work (docs/design/survivability.md s2 items 3, 4, 5, 7, 8, 9, 10, 19, 20; contract s3 keys, s4 IGC). Decisions only:
// the Flight PB (SURVIVAL LINK) flies them. Link (CurrentConstruct, key _fk = "WSPL" + Flight PB id):
//   .SVF  Flight -> Staff  MyTuple<int flags, long attackerId, Vector3D hitDir, Vector3D attackerPos, Vector3D aux, Vector3D home>
//         flags bit0 hit in this report, bit1 Flight yielding, bits 8-15 integrity % (255 unknown), bits 16-23 blocks hit; aux = (largest drop, own fire range, attacker range)
//   .SVO  Flight -> Staff  MyTuple<long tgtId, Vector3D fwd, Vector3D up, Vector3D vel, double radius, int flags (bit0 orientation valid)>
//   .MSL  Flight -> Staff  MyTuple<long id, Vector3D pos, Vector3D vel>       (missiles the Flight saw)
//   .SVS  Staff -> Flight  MyTuple<int flags, long tgtId, Vector3D A, Vector3D B, Vector3D D, Vector3D N>, every 12 ticks, at once on a change of bits 0-11,
//         full resend on a new Flight key / its first F2S / the end of a failover hold. A = via point (bit1) or gather point (bit0); B = blind-side bearing x speed
//         (bit2); D = missile dodge velocity (bit3); N = (standoff floor, standoff add, enemy projectile speed). Bits 4 DAMAGED, 5 RETREATING, 6 GATHER READY,
//         7 GATHERING, 8 LIMP (+ cap m/s in bits 16-23), 9 STAFF HOLDS THE OVERRIDES (Flight yields), 10 FIGHT BACK in RTB, 11 standoff advice, 24-27 threat class.
// Wing (antenna): <Tag>_THREAT (+ <ch>_THREAT) MyTuple<long enemy, Vector3D pos, Vector3D bearing, Vector3D (hit range, dps/s, integrity), long reporter, int flags
//   (bits 0-1 class 1 bullet 2 explosive 3 long, bit2 LAST GASP, bit3 bearing in the target frame)>; <Tag>_MSL MyTuple<long id, Vector3D pos, Vector3D vel, long sender>.
// One writer: the gyro / thruster overrides are written here only while the Flight PB is down (limp-home, _svHold, SVS bit 9) and by the dock actuator
// (FOL bit 2), never while docked or welded (SvDocked) and never during the take-off burn. v1.1 dock actuator (docs/design/staff_split.md s3.2): DkGo() takes
// the actuators (snapshot of the own thrusters / gyros), DkSteer(vel, nose, roof) flies a setpoint with SvSteer from the 3rd tick on (FOL bit 2 reaches the
// Flight first), DkRel() lets go (zero, dampeners on; skipped while the Flight is down: SvWatch / limp-home own the actuators then), DkHold keeps FOL bit 2
// for 2 ticks after that write, SvRepaired() clears the damage latch. Callers: the local REPAIR CLIENT (Gnat, Basilisk) or AUX LINK (setpoints from the [Aux] PB).
// Keys: [Survival] in the Flight PB's Custom Data (SvCfg reads them; SvDef writes the missing defaults once per Flight key, after its first F2S, re-parsing first).
// Per-script hooks (readonly FS, SvOrbK, SvOrbD): bool SvFightMode() (a fight mode: GUARD/HUNT, Gnat ATTACK), string SvVerb() (the mode's command word), const string FS.
// Hooks: SvsRead() after ReadF2S(); SvWatch(burn || _lnS == 2) at the end of the tick and in the no-RC path (replaces Watch); SendFol(burn || SvFly);
//   SvListen() first in Listen(); SvBlocks() in GetBlocks; SvCfg(ini) in LoadFlightCfg; SvStatus() in Display; SvWord(w) first in Local(); FoDist + SvFoX() in SendFol.
// Uses: _rc, _fpb, _fk, _now, _tick, _mode (IDLE/RTB), _fl, _tgtId, _tgtPos, _f2sN, _f2sTick, _down, _gyros, _thr, _guns, _ai, _mates, Tag, _flChan, MaxSpeed,
//   _dynHome, _dynHomeT, _wpMap, Fwd(), FleetV(); REPAIR CLIENT or AUX LINK (RpCfg RpListen RpBlocks RpRead RpStep RpStatus RpStop RpWant SvFly RpMedCh),
//   STAFF SEARCH (Ss*).
List<MyTuple<string, string, string, double>> _svDefs = new List<MyTuple<string, string, string, double>>();
bool SvRtbOn = true, SvLostAx = false, SvLostCam = false, SvFightOn = true, SvLimpHome = true, SvBlind = true, SvStandOn = true, SvDodge = true, SvFlare = true,
    SvHand = true, SvShare = true, SvTogether = true;
double SvRtbI = 0.55, SvGyroF = 0, SvMargin = 300, SvLimpMin = 0.4, SvLimpV = 30, SvBlindK = 2, SvStandM = 150, SvRB = 800, SvRR = 800, SvRL = 2000,
    SvDodgeR = 25, SvFlareI = 1.5, SvFlareL = 2, SvHandI = 0.75, SvHandB = 200, SvHandIn = 100, SvFoXv = 40, SvMem = 120, SvGasp = 0.15, SvExpN = 3, SvExpD = 400,
    SvGMax = 6, SvGSpread = 50, SvGExtra = 150, SvGMate = 2000, SvOrb = 22;
string _svWrote = "", _svLKey = "", _svPrevVerb = "", _svWhy = "", _svFk = "";
IMyBroadcastListener _svLf, _svLo, _svLm, _svLt, _svLt2, _svLw;

string SvT(MyIni i, string s, string k, string d) { _svDefs.Add(new MyTuple<string, string, string, double>(s, k, d, 0)); return i.Get(s, k).ToString(d); }
bool SvBn(MyIni i, string s, string k, bool d) { _svDefs.Add(new MyTuple<string, string, string, double>(s, k, d ? "true" : "false", 0)); return i.Get(s, k).ToBoolean(d); }
double SvDn(MyIni i, string s, string k, double d) { _svDefs.Add(new MyTuple<string, string, string, double>(s, k, null, d)); return i.Get(s, k).ToDouble(d); }
bool SvB(MyIni i, string k, bool d) { return SvBn(i, "Survival", k, d); }
double SvD(MyIni i, string k, double d) { return SvDn(i, "Survival", k, d); }

void SvCfg(MyIni i)
{
    _svDefs.Clear();
    if (FS != "Gnat")
    {   // read by the Flight (SURVIVAL LINK): listed here so that their defaults are written in one place
        SvB(i, "Lead-Breaking Jinks", true); SvD(i, "Jink Accel Fraction", 0.8); SvD(i, "Enemy Projectile Speed (m/s)", 800);
        SvD(i, "Jink Period Min (s)", 0.4); SvD(i, "Jink Period Max (s)", 3); SvD(i, "Jink Period Random", 0.3);
    }
    if (FS == "Longbow" || FS == "Mantis") { SvB(i, "Power Guard", true); SvD(i, "Power Guard Hold (s)", 2); }
    // AB2 (sim, survivability.md "Results (AB2)"): damage RTB on integrity only by default. Lost axis / one of two gyros / forward cameras fired on the first
    // lost block, cost about 40 % of the wins vs the Pirate and made 1v1s stalemates; the [Thrust] PB and limp mode keep such a drone fighting.
    SvRtbOn = SvB(i, "Damage RTB", true); SvRtbI = SvD(i, "RTB Integrity Below", 0.55); SvLostAx = SvB(i, "RTB On Lost Thrust Axis", false);
    SvGyroF = SvD(i, "RTB Gyro Fraction", 0); SvLostCam = SvB(i, "RTB On Lost Forward Cameras", false); SvMargin = SvD(i, "Retreat Margin (m)", 300);
    SvFightOn = SvB(i, "Fight Back While Retreating", true); SvLimpMin = SvD(i, "Limp Min Speed Fraction", 0.4);
    SvLimpHome = SvB(i, "Limp Home On Flight Loss", true); SvLimpV = SvD(i, "Limp Home Speed", 30);
    SvBlind = SvB(i, "Blind Side Orbit", true); SvBlindK = SvD(i, "Blind Side Speed Factor", 2);
    SvStandOn = SvB(i, "Standoff By Enemy Weapons", true); SvStandM = SvD(i, "Threat Standoff Margin (m)", 150);
    SvRB = SvD(i, "Threat Range Bullet (m)", 800); SvRR = SvD(i, "Threat Range Rocket (m)", 800); SvRL = SvD(i, "Threat Range Long (m)", 2000);
    SvDodge = SvB(i, "Missile Dodge", true); SvDodgeR = SvD(i, "Missile Dodge Radius (m)", 25);
    SvFlare = SvB(i, "Flare On Missile", true); SvFlareI = SvD(i, "Flare Interval (s)", 1.5); SvFlareL = SvD(i, "Flare Lead (s)", 2);
    SvHand = SvB(i, "Wing Hand-off", true); SvHandI = SvD(i, "Hand-off Integrity", 0.75); SvHandB = SvD(i, "Hand-off Back (m)", 200);
    SvHandIn = SvD(i, "Hand-off In (m)", 100); SvFoXv = SvD(i, "Damaged Formation Extra (m)", 40);
    SvShare = SvB(i, "Share Threats", true); SvMem = SvD(i, "Threat Memory (s)", 120); SvGasp = SvD(i, "Last Gasp Drop", 0.15);
    SvExpN = SvD(i, "Explosive Hit Blocks", 3); SvExpD = SvD(i, "Explosive Drop Min", 400);
    SvTogether = SvB(i, "Attack Together", true); SvGMax = SvD(i, "Gather Max (s)", 6); SvGSpread = SvD(i, "Gather Spread (deg)", 50);
    SvGExtra = SvD(i, "Gather Extra Range (m)", 150); SvGMate = SvD(i, "Gather Mate Radius (m)", 2000);
    RpCfg(i);
    SvOrb = i.Get(FS, SvOrbK).ToDouble(SvOrbD);
}

// the missing [Survival] (and moved) defaults -> the Flight PB's Custom Data: once per Flight key, only after its first F2S, re-parsed right before writing
void SvDef()
{
    if (_fpb == null || _f2sN == 0 || _svWrote == _fk || _svDefs.Count == 0) return;
    var ini = new MyIni(); MyIniParseResult r;
    if (!ini.TryParse(_fpb.CustomData, out r)) return;
    _svWrote = _fk;
    foreach (var d in _svDefs) if (!ini.ContainsKey(d.Item1, d.Item2)) { if (d.Item3 != null) ini.Set(d.Item1, d.Item2, d.Item3); else ini.Set(d.Item1, d.Item2, d.Item4); }
    string o = ini.ToString(); if (o != _fpb.CustomData) _fpb.CustomData = o;
}

void SvListen()
{
    string k = Tag + "|" + _fk + "|" + _flChan + "|" + RpMedCh;
    if (k == _svLKey) return;
    _svLKey = k;
    foreach (var l in new[] { _svLf, _svLo, _svLm, _svLt, _svLt2, _svLw }) if (l != null) IGC.DisableBroadcastListener(l);
    _svLf = _svLo = _svLm = null;
    if (_fk != "") { _svLf = IGC.RegisterBroadcastListener(_fk + ".SVF"); _svLo = IGC.RegisterBroadcastListener(_fk + ".SVO"); _svLm = IGC.RegisterBroadcastListener(_fk + ".MSL"); }
    _svLt = IGC.RegisterBroadcastListener(Tag + "_THREAT"); _svLw = IGC.RegisterBroadcastListener(Tag + "_MSL");
    _svLt2 = _flChan != "" ? IGC.RegisterBroadcastListener(_flChan + "_THREAT") : null;
    RpListen();
    if (_fk != _svFk)
    {   // a new Flight PB: the survival mirror starts again (the threat memory is kept)
        _svFk = _fk; _svFT = -99; _svI = -1; _svOT = -99; _svHomeT = -99; _svFull = true;
    }
}

// ---- blocks
List<IMyCameraBlock> _svCams = new List<IMyCameraBlock>(); List<IMyUserControllableGun> _svFlares = new List<IMyUserControllableGun>();
List<IMyShipMergeBlock> _svMg = new List<IMyShipMergeBlock>(); List<IMyTerminalBlock> _svTmp = new List<IMyTerminalBlock>();
void SvBlocks()
{
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_svCams, b => b.CubeGrid == g);
    GridTerminalSystem.GetBlocksOfType(_svFlares, b => b.CubeGrid == g && b.BlockDefinition.SubtypeName.Contains("FlareLauncher"));
    RpBlocks();
}

// ---- mirror of the Flight's reports
int _svFf; long _svAtk, _svOId; Vector3D _svAtkP, _svAux, _svHomeP, _svOF, _svOU, _svOV; double _svFT = -99, _svHomeT = -99, _svAtkT = -99, _svI = -1, _svOT = -99, _svDps;
bool _svOOk, _svFull = true; int _svF2sSeen;
double[] _svIH = new double[12]; int _svIHn;
void SvsRead()
{
    if (_f2sN > 0 && _svF2sSeen == 0) _svFull = true;   // first F2S of this key: full SVS at once
    _svF2sSeen = _f2sN;
    SvDef();
    while (_svLf != null && _svLf.HasPendingMessage)
    {
        var m = _svLf.AcceptMessage();
        if (m.Data is MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>) SvSvf((MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>)m.Data);
    }
    while (_svLo != null && _svLo.HasPendingMessage)
    {
        var m = _svLo.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, double, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, double, int>)m.Data;
        _svOId = d.Item1; _svOF = d.Item2; _svOU = d.Item3; _svOV = d.Item4; _svOOk = (d.Item6 & 1) != 0 && d.Item2.LengthSquared() > 0.5 && d.Item3.LengthSquared() > 0.5; _svOT = _now;
    }
    while (_svLm != null && _svLm.HasPendingMessage)
    {
        var m = _svLm.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D>)m.Data;
        SvMsl(d.Item1, d.Item2, d.Item3, true);
    }
    while (_svLw != null && _svLw.HasPendingMessage)
    {
        var m = _svLw.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, long>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, long>)m.Data;
        if (d.Item4 != Me.CubeGrid.EntityId) SvMsl(d.Item1, d.Item2, d.Item3, false);
    }
    SvWing(_svLt); SvWing(_svLt2);
    RpRead();
}
bool SvHomeKnown() { return _now - _svHomeT < 120; }
bool SvFresh { get { return _now - _svFT < 2; } }
MatrixD SvOri() { return MatrixD.CreateWorld(Vector3D.Zero, Vector3D.Normalize(_svOF), Vector3D.Normalize(_svOU)); }
bool SvOriOk(long id) { return _svOOk && _svOId == id && id != 0 && _now - _svOT < 2; }

void SvSvf(MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D> d)
{
    _svFf = d.Item1; _svFT = _now; if (FleetV(d.Item5)) _svAux = d.Item5;
    if (d.Item6.LengthSquared() > 1 && FleetV(d.Item6)) { _svHomeP = d.Item6; _svHomeT = _now; }
    int ip = (d.Item1 >> 8) & 255;
    double ni = ip == 255 ? -1 : Math.Min(1, ip / 100.0), lost = _svI >= 0 && ni >= 0 ? Math.Max(0, _svI - ni) : 0;
    _svI = ni;
    if ((d.Item1 & 1) == 0 || d.Item2 == 0 || !FleetV(d.Item4) || _rc == null) return;
    _svAtk = d.Item2; _svAtkP = d.Item4; _svAtkT = _now;
    int nb = (d.Item1 >> 16) & 255;
    int cls = _now - _svMslT < 3 || (nb >= SvExpN && d.Item5.X >= SvExpD) ? 2 : d.Item5.Z > 1000 ? 3 : 1;
    Vector3D b = _rc.GetPosition() - d.Item4; if (b.LengthSquared() < 1) return; b.Normalize();
    bool loc = SvOriOk(d.Item2); if (loc) b = Vector3D.TransformNormal(b, MatrixD.Transpose(SvOri()));
    var e = SvRemember(d.Item2, d.Item4, cls, d.Item5.Z, b, lost, loc);
    e.Dps += (_svDps - e.Dps) * 0.3;
    if (_now - e.RepT >= 2) { e.RepT = _now; SvTell(e.Id, e.P, b, d.Item5.Z, cls, false, loc); }
}

// ---- threat memory (item 19): per enemy class votes, the longest range it hit someone at, dps, and where it hit (item 7 samples)
class SvSm { public Vector3D B; public double W, T; public bool L; }
class SvTh { public long Id; public Vector3D P; public int[] N = new int[4]; public double R, RT, Dps, T, RepT = -99; public List<SvSm> S = new List<SvSm>(); }
Dictionary<long, SvTh> _svTh = new Dictionary<long, SvTh>(); List<long> _svDel = new List<long>();
int SvCls(SvTh e) { int c = 0; for (int k = 1; k < 4; k++) if (e.N[k] > (c > 0 ? e.N[c] : 0)) c = k; return c; }
SvTh SvRemember(long id, Vector3D p, int cls, double r, Vector3D b, double w, bool loc)
{
    SvTh e;
    if (!_svTh.TryGetValue(id, out e))
    {
        if (_svTh.Count >= 8) { long o = 0; double ot = 1e18; foreach (var kv in _svTh) if (kv.Value.T < ot) { ot = kv.Value.T; o = kv.Key; } _svTh.Remove(o); }
        e = new SvTh { Id = id }; _svTh[id] = e;
    }
    e.P = p; e.T = _now;
    if (cls > 0 && cls < 4) e.N[cls]++;
    if (r > 0 && r < 1e5 && r >= e.R) { e.R = r; e.RT = _now; }
    if (w > 0 && b.LengthSquared() > 0.5) { e.S.Add(new SvSm { B = b, W = w, T = _now, L = loc }); if (e.S.Count > 16) e.S.RemoveAt(0); }
    return e;
}
void SvTell(long id, Vector3D p, Vector3D b, double r, int cls, bool gasp, bool loc)
{
    if (!SvShare) return;
    var t = new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(id, p, b, new Vector3D(r, _svDps, Math.Max(0, _svI)), Me.CubeGrid.EntityId, (cls & 3) | (gasp ? 4 : 0) | (loc ? 8 : 0));
    IGC.SendBroadcastMessage(Tag + "_THREAT", t);
    if (_flChan != "") IGC.SendBroadcastMessage(_flChan + "_THREAT", t);
}
void SvWing(IMyBroadcastListener l)
{
    while (l != null && l.HasPendingMessage)
    {
        var m = l.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data;
        if (d.Item5 == Me.CubeGrid.EntityId || d.Item1 == 0 || !FleetV(d.Item2) || !FleetV(d.Item3) || !FleetV(d.Item4)) continue;
        Vector3D b = d.Item3; double bl = b.Length(); if (bl > 0.5) b /= bl; else b = Vector3D.Zero;
        double w = Math.Max(0, Math.Min(1, d.Item4.Y)) * 2; if ((d.Item6 & 4) != 0) w = Math.Max(w, SvGasp);
        var e = SvRemember(d.Item1, d.Item2, d.Item6 & 3, d.Item4.X, b, w, (d.Item6 & 8) != 0);
        e.Dps += (Math.Max(0, d.Item4.Y) - e.Dps) * 0.3;
    }
}
double SvClsR(int c) { return c == 1 ? SvRB : c == 2 ? SvRR : c == 3 ? SvRL : 0; }
// how far that enemy reaches (its class range or the longest range it hit at, whichever is more); dflt when it is unknown
double SvReach(long id, double dflt)
{
    SvTh e; if (!_svTh.TryGetValue(id, out e)) return dflt;
    int c = SvCls(e); if (c == 0 && e.R <= 0) return dflt;
    return Math.Max(SvClsR(c), e.R * 1.05);
}

// ---- missiles (item 9)
class SvM { public Vector3D P, V; public double T, RelT = -99; }
Dictionary<long, SvM> _svMs = new Dictionary<long, SvM>();
double _svMslT = -99, _svDodgeEnd = -99, _svFlareT = -99, _svTca = -1; Vector3D _svDodgeV;
void SvMsl(long id, Vector3D p, Vector3D v, bool own)
{
    if (!FleetV(p) || !FleetV(v) || _rc == null) return;
    SvM t; if (!_svMs.TryGetValue(id, out t)) { if (_svMs.Count >= 16) return; t = new SvM(); _svMs[id] = t; }
    t.P = p; t.V = v; t.T = _now;
    if (!own || _now - t.RelT < 0.2) return;
    bool close = SvClosing(p, v, _rc.GetPosition(), _rc.GetShipVelocities().LinearVelocity);   // relay only what closes on a friendly within 1.5 km
    foreach (var mt in _mates.Values) if (!close && _now - mt.Seen < 1.5) close = SvClosing(p, v, mt.Pos, mt.Vel);
    if (close) { t.RelT = _now; IGC.SendBroadcastMessage(Tag + "_MSL", new MyTuple<long, Vector3D, Vector3D, long>(id, p, v, Me.CubeGrid.EntityId)); }
}
bool SvClosing(Vector3D p, Vector3D v, Vector3D f, Vector3D fv) { Vector3D r = p - f; return r.LengthSquared() < 2.25e6 && Vector3D.Dot(r, v - fv) < 0; }
void SvMslStep()
{
    _svDel.Clear(); double best = 99; Vector3D me = _rc.GetPosition(), mv = _rc.GetShipVelocities().LinearVelocity, dv = Vector3D.Zero;
    foreach (var kv in _svMs)
    {
        var t = kv.Value; double age = _now - t.T;
        if (age > 1) { _svDel.Add(kv.Key); continue; }
        Vector3D r = t.P + t.V * age - me, vr = t.V - mv; double vr2 = vr.LengthSquared(); if (vr2 < 1) continue;
        double tca = -Vector3D.Dot(r, vr) / vr2; if (tca <= 0 || tca >= 4 || tca >= best) continue;
        Vector3D miss = r + vr * tca; if (miss.LengthSquared() >= SvDodgeR * SvDodgeR) continue;
        best = tca; Vector3D n = vr / Math.Sqrt(vr2), d = -miss - n * Vector3D.Dot(-miss, n);   // away from the closest-approach point, across the missile's path
        if (d.LengthSquared() < 0.01) d = _rc.WorldMatrix.Up - n * Vector3D.Dot(_rc.WorldMatrix.Up, n);
        dv = d.LengthSquared() > 1e-4 ? Vector3D.Normalize(d) * MaxSpeed : Vector3D.Zero;
    }
    foreach (var k in _svDel) _svMs.Remove(k);
    if (best >= 99) return;
    _svTca = best;
    _svMslT = _now;
    if (SvDodge) { _svDodgeV = dv; _svDodgeEnd = _now + best + 0.5; }
    if (SvFlare && best < SvFlareL && _now - _svFlareT >= SvFlareI && !_svDock && _svFlares.Count > 0)
    { _svFlareT = _now; foreach (var f in _svFlares) if (f.IsFunctional) f.ApplyAction("ShootOnce"); }
}

// ---- damage state (items 3, 4): integrity, thrust per axis (rated = the best seen), gyros, forward cameras
double[] _svRated = new double[6], _svLive = new double[6]; int _svLost, _svGyN, _svGyMax, _svCamN, _svCamMax, _svLatch; double _svF = 1, _svAmin = 1, _svRtbT = -99;
bool _svLimp, _svDam, _svRtb; IMyCubeGrid _svScanG;
int SvAx(Vector3D push, MatrixD m)
{
    if (Vector3D.Dot(push, m.Forward) > 0.9) return 0; if (Vector3D.Dot(push, m.Backward) > 0.9) return 1;
    if (Vector3D.Dot(push, m.Left) > 0.9) return 2; if (Vector3D.Dot(push, m.Right) > 0.9) return 3;
    if (Vector3D.Dot(push, m.Up) > 0.9) return 4; if (Vector3D.Dot(push, m.Down) > 0.9) return 5;
    return -1;
}
void SvScan()
{
    var g = Me.CubeGrid;
    if (g != _svScanG) { _svScanG = g; Array.Clear(_svRated, 0, 6); _svGyMax = 0; _svCamMax = 0; _svLatch = 0; }   // a new grid (released, printed, repaired): maxima start again
    var m = _rc.WorldMatrix; Array.Clear(_svLive, 0, 6);
    foreach (var t in _thr) { if (t.CubeGrid != g || !t.IsWorking) continue; int i = SvAx(t.WorldMatrix.Backward, m); if (i >= 0) _svLive[i] += t.MaxThrust; }
    _svLost = 0; double f = 1, amin = 1e18;
    for (int i = 0; i < 6; i++)
    {
        if (_svLive[i] > _svRated[i]) _svRated[i] = _svLive[i];
        if (_svRated[i] > 0) { double q = _svLive[i] / _svRated[i]; if (q < 0.1) { _svLost |= 1 << i; q = 0.3; } f = Math.Min(f, q); }
        if (_svLive[i] > 0) amin = Math.Min(amin, _svLive[i]);
    }
    double mass = _rc.CalculateShipMass().PhysicalMass;
    _svF = f; _svAmin = mass > 0 && amin < 1e18 ? Math.Max(0.3, amin / mass * 0.5) : 1;
    _svLimp = _svLost != 0 || f < 0.5;
    _svGyN = 0; foreach (var y in _gyros) if (y.CubeGrid == g && y.IsWorking) _svGyN++;
    _svCamN = 0; foreach (var c in _svCams) if (c.IsWorking && Vector3D.Dot(c.WorldMatrix.Forward, m.Forward) > 0.9) _svCamN++;
    _svGyMax = Math.Max(_svGyMax, _svGyN); _svCamMax = Math.Max(_svCamMax, _svCamN);
    if (_svI >= 0) { _svIH[_svIHn++ % 12] = _svI; }
    double old = _svIHn >= 12 ? _svIH[_svIHn % 12] : (_svIHn > 0 ? _svIH[0] : _svI);   // integrity 2 s ago (10-tick samples)
    _svDps = _svI >= 0 && old >= 0 ? Math.Max(0, old - _svI) / 2 : 0;
    _svDam = (_svI >= 0 && _svI < SvHandI) || _svLimp;
    int c0 = (_svI >= 0 && _svI < SvRtbI ? 1 : 0) | (SvLostAx && _svLost != 0 ? 2 : 0) | (_svGyMax >= 2 && _svGyN <= SvGyroF * _svGyMax ? 4 : 0) | (SvLostCam && _svCamMax >= 1 && _svCamN == 0 ? 8 : 0);
    _svLatch &= c0;                                          // a cause that went away (repaired) may fire again
    int nw = c0 & ~_svLatch;
    if (nw != 0 && SvRtbOn && _mode != Mode.IDLE && _mode != Mode.RTB && !_svDock && !_down && _f2sN > 0)
    {
        _svLatch |= nw; _svPrevVerb = SvVerb(); _svRtb = true; _svRtbT = _now; _svViaT = -99;
        _svWhy = (nw & 1) != 0 ? "integrity " + (_svI * 100).ToString("0") + "%" : (nw & 2) != 0 ? "thrust axis lost" : (nw & 4) != 0 ? "gyros lost" : "forward cameras lost";
        Fwd("rtb");
    }
    if (_svRtb && _mode != Mode.IDLE && _mode != Mode.RTB && _now - _svRtbT > 3) _svRtb = false;   // the owner sent it back into a fight some other way
    if (_svI >= 0 && (old - _svI >= SvGasp || (_svI < 0.35 && old >= 0.35)) && _now - _svGaspT > 1)
    {   // last gasp: tell the wing what is killing me, at once
        _svGaspT = _now;
        long id = _now - _svAtkT < 10 ? _svAtk : _tgtId; Vector3D p = _now - _svAtkT < 10 ? _svAtkP : _tgtPos;
        if (id != 0) { Vector3D b = _rc.GetPosition() - p; SvTh e; int cls = _svTh.TryGetValue(id, out e) ? SvCls(e) : 1; SvTell(id, p, b.LengthSquared() > 1 ? Vector3D.Normalize(b) : b, Vector3D.Distance(p, _rc.GetPosition()), cls, true, false); }
    }
}
double _svGaspT = -99;
string SvWord(string[] w)
{
    string v = w[0];
    if (v == "fight") { _svRtb = false; RpStop("fight"); }
    else if (v == "repair") RpWant(1);
    else if (v == "dock") RpWant(2);
    else if (v == "seeker" && w.Length > 1) { SeekerOn = w[1] == "on"; SsBlocks(); }
    else if (",guard,hunt,assist,idle,stop,follow,escort,ready,sentry,satellite,sat,orbit,form,attack,".Contains("," + v + ",")) { _svRtb = false; RpStop(""); }
    return v;
}

// ---- retreat via point (item 3), standoff (item 8), hand-off (item 10), blind side (item 7), gather (item 20)
bool _svViaOn, _svBOn, _svGOn, _svGReady; double _svViaT = -99, _svAdd, _svAddT = -99, _svFloor, _svGStart, _svAncT = -99; long _svGT, _svGPrev, _svAncId;
Vector3D _svViaP, _svBV, _svGP, _svAnc; int _svGMode; Dictionary<long, double> _svGDone = new Dictionary<long, double>();
List<long> _svIds = new List<long>();
void SvVia()
{
    _svViaOn = false;
    if (!_svRtb || _mode != Mode.RTB || !SvHomeKnown()) return;
    bool atk = _svAtk != 0 && _now - _svAtkT < 10; if (!atk && _tgtId == 0) return;
    Vector3D T = atk ? _svAtkP : _tgtPos, M = _rc.GetPosition(), H = _svHomeP;
    double R = SvReach(atk ? _svAtk : _tgtId, 900);
    Vector3D mh = H - M; double l = mh.LengthSquared(), s = l > 1 ? MathHelper.Clamp(Vector3D.Dot(T - M, mh) / l, 0, 1) : 0;
    if (Vector3D.DistanceSquared(M + mh * s, T) >= R * R) return;   // the straight way home keeps out of its reach
    Vector3D u = M - T; double ul = u.Length(); if (ul < 1 || l < 1) return; u /= ul;
    Vector3D h = mh / Math.Sqrt(l);
    if (Vector3D.Dot(h, -u) < 0.5) return;                    // home is not behind the threat
    Vector3D side = h - Vector3D.Dot(h, u) * u;
    if (side.LengthSquared() < 1e-4) side = _rc.WorldMatrix.Right - Vector3D.Dot(_rc.WorldMatrix.Right, u) * u;
    side.Normalize();
    Vector3D V = T + Vector3D.Normalize(u * 0.342 + side * 0.940) * Math.Max(R + SvMargin, ul);   // 70 deg off the threat axis
    if (Vector3D.DistanceSquared(M, V) < 22500) return;
    _svViaP = V; _svViaOn = true;
}
void SvSlow()
{
    Vector3D me = _rc.GetPosition();
    bool uf = (_fl & 16) != 0;
    double ownR = _svAux.Y > 10 ? _svAux.Y : 800, dist = _tgtId != 0 ? Vector3D.Distance(me, _tgtPos) : 0;
    // standoff floor: only with a range advantage
    _svFloor = 0;
    if (SvStandOn && _tgtId != 0) { double r = SvReach(_tgtId, -1); if (r > 0 && r + SvStandM < 0.95 * ownR) _svFloor = r + SvStandM; }
    // hand-off
    double add = 0;
    if (SvHand && _tgtId != 0)
    {
        bool ok = false, dam = false;
        foreach (var m in _mates.Values)
        {
            if (_now - m.Seen > 1.5 || m.Tgt != _tgtId || Vector3D.DistanceSquared(m.Pos, me) > 2.25e6) continue;
            if ((m.Flags & 24) == 0) ok = true; if ((m.Flags & 8) != 0) dam = true;
        }
        if (uf && _svDam && ok) add = SvHandB; else if (!_svDam && _svI >= 0.8 && !uf && dam) add = -SvHandIn;
    }
    if (add != 0) { _svAdd = add; _svAddT = _now; } else if (_now - _svAddT > 5) _svAdd = 0;
    // gather window
    if (_svGOn)
    {
        bool all = SvSlot(me, ownR);
        if (_tgtId != _svGT || !SvFightMode() || (int)_mode != _svGMode || uf || dist <= ownR || _now - _svGStart > SvGMax || all || _svIds.Count < 2)
        { _svGOn = false; _svGReady = false; _svAncT = _now; }
    }
    else if (_tgtId != 0 && _tgtId != _svGPrev && SvTogether && SvFightMode() && !uf && dist > ownR + 200)
    {
        double lt; if (!_svGDone.TryGetValue(_tgtId, out lt) || _now - lt > 60)
        {
            _svGT = _tgtId; SvSlot(me, ownR);
            if (_svIds.Count >= 2) { _svGOn = true; _svGStart = _now; _svGMode = (int)_mode; _svGDone[_tgtId] = _now; _svAncId = _tgtId; if (_svGDone.Count > 16) _svGDone.Clear(); }
        }
    }
    _svGPrev = _tgtId;
    SvBlindSide(me);
}
// gather slot k of the participants (me + fresh mates on this target or none, not retreating, within the mate radius); true = all of them are READY
bool SvSlot(Vector3D me, double ownR)
{
    _svIds.Clear(); long my = Me.CubeGrid.EntityId; _svIds.Add(my); Vector3D c = me; bool all = true;
    foreach (var m in _mates.Values)
    {
        if (_now - m.Seen > 1.5 || (m.Tgt != _svGT && m.Tgt != 0) || (m.Flags & 16) != 0 || Vector3D.DistanceSquared(m.Pos, me) > SvGMate * SvGMate) continue;
        _svIds.Add(m.Id); c += m.Pos; if ((m.Flags & 32) == 0) all = false;
    }
    int n = _svIds.Count; if (n < 2) return false;
    _svIds.Sort(); int k = _svIds.IndexOf(my);
    Vector3D a = c / n - _tgtPos; if (a.LengthSquared() < 1) a = me - _tgtPos; a.Normalize();
    Vector3D g = _rc.GetNaturalGravity(), up = g.LengthSquared() > 0.1 ? -Vector3D.Normalize(g) : Vector3D.Up, e1 = Vector3D.Cross(a, up);
    if (e1.LengthSquared() < 1e-4) e1 = Vector3D.Cross(a, Vector3D.Right);
    e1.Normalize(); Vector3D e2 = Vector3D.Cross(a, e1);
    double phi = Math.Min(SvGSpread, 30.0 * (n - 1)) * Math.PI / 180, th = 2 * Math.PI * k / n;
    _svAnc = a * Math.Cos(phi) + (e1 * Math.Cos(th) + e2 * Math.Sin(th)) * Math.Sin(phi);
    _svGP = _tgtPos + _svAnc * (ownR + SvGExtra);
    _svGReady = Vector3D.DistanceSquared(me, _svGP) < 22500;
    return all && _svGReady;
}
// exposure of bearing b (target -> me, world) to that enemy: hits taken there (own and wing, 30 s decay) and its turrets (weakpoint relay, kind Weapons)
double SvExpo(SvTh e, Vector3D b, MatrixD o, bool ori)
{
    double E = 0;
    if (e != null)
        foreach (var s in e.S)
        {
            Vector3D v = s.B; if (s.L) { if (!ori) continue; v = Vector3D.TransformNormal(v, o); }
            double c = Vector3D.Dot(b, v); if (c > 0) E += s.W * Math.Exp(-(_now - s.T) / 30) * c * c;
        }
    if (ori) foreach (var w in _wpMap.Values)
        {
            if (w.Tgt != _tgtId || (w.F & 7) != 2 || w.S.LengthSquared() < 1) continue;
            double c = Vector3D.Dot(b, Vector3D.TransformNormal(Vector3D.Normalize(w.S), o)); if (c > 0) E += 0.05 * c * c;
        }
    return E;
}
double SvAng(Vector3D a, Vector3D b) { return Math.Acos(MathHelper.Clamp(Vector3D.Dot(a, b), -1, 1)) * 180 / Math.PI; }
void SvBlindSide(Vector3D me)
{
    _svBOn = false;
    if (!SvBlind || _tgtId == 0) return;
    SvTh e; _svTh.TryGetValue(_tgtId, out e);
    bool ori = SvOriOk(_tgtId); MatrixD o = ori ? SvOri() : MatrixD.Identity;
    Vector3D c = me - _tgtPos; if (c.LengthSquared() < 1) return; c.Normalize();
    double ec = SvExpo(e, c, o, ori); if (ec <= 0) return;
    bool slot = (_svGOn || _now - _svAncT < 60) && _svAncId == _tgtId;
    Vector3D e1 = Vector3D.Normalize(Vector3D.Cross(c, Math.Abs(c.X) < 0.9 ? Vector3D.Right : Vector3D.Up)), e2 = Vector3D.Cross(c, e1), best = c; double bc = 1e18;
    for (int i = 0; i < 14; i++)
    {
        Vector3D b;
        if (i == 12) b = c; else if (i == 13) { if (!slot) continue; b = _svAnc; }
        else { double th = (30 * (1 + i % 3)) * (i % 6 < 3 ? 1 : -1) * Math.PI / 180; b = c * Math.Cos(th) + (i < 6 ? e1 : e2) * Math.Sin(th); }
        double cost = SvExpo(e, b, o, ori) + 0.02 * SvAng(c, b) / 30 + (slot ? 0.05 * Math.Max(0, SvAng(b, _svAnc) - 60) / 30 : 0);
        if (cost < bc) { bc = cost; best = b; }
    }
    if (bc >= 0.7 * ec) return;
    _svBV = Vector3D.Normalize(best) * Math.Min(SvBlindK * SvOrb, MaxSpeed * 0.5); _svBOn = true;
}

// ---- failover (item 5): the Flight PB is down -> release once (AI off, guns off), then limp home on our own if allowed; SvWatch replaces Watch()
bool _svHold, _svArr, _svDock; double _svRelT = -99; long _svDownTk; Vector3D _svHomeL; IMyCubeGrid _svDockG; bool _svFused;
bool SvDocked() { return _svDock; }
bool SvDockNow()
{
    var g = Me.CubeGrid;
    if (g != _svDockG)
    {   // a printer / bay on our grid (welded or merged): its thrusters are not ours
        _svDockG = g;
        GridTerminalSystem.GetBlocksOfType(_svTmp, b => b.CubeGrid == g && b != Me && (b.CustomName.Contains("[Bay") || (b is IMyProgrammableBlock && b.CustomData.Contains("[Hive Mother]"))));
        _svFused = _svTmp.Count > 0;
    }
    if (_svFused) return true;
    GridTerminalSystem.GetBlocksOfType(_svMg, b => b.CubeGrid == g);
    foreach (var mg in _svMg) if (mg.IsConnected) return true;
    return false;
}
void SvRel()
{
    foreach (var y in _gyros) { y.GyroOverride = false; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
}
void SvWatch(bool busy)
{
    if (_tick % 10 == 7 || Me.CubeGrid != _svDockG) _svDock = SvDockNow();
    bool rcOk = _rc != null && _rc.IsFunctional && _rc.IsSameConstructAs(Me);
    bool stale = _fpb == null || _tick - _f2sTick > 90 || !_fpb.IsWorking;
    bool down = _svHold ? (_f2sN == 0 || stale) : (_f2sN > 0 && stale);   // (holding: a rebuilt Flight PB takes over only once it has spoken)
    if (!down) _svDownTk = _tick;
    if (!down)
    {
        if (_svHold || _down) _svFull = true;               // the Flight is back: bit 9 cleared at once, we stop writing (no zeroing)
        _svHold = false; _down = false;
    }
    else if (_tick - _svDownTk < 3) { }                     // a Flight PB destroyed mid-tick may have written this tick or the last: wait 2 ticks (one writer)
    else if (busy || _svDock) { if (_svHold) { _svHold = false; _svFull = true; } }
    else
    {
        bool first = !_down; _down = true;
        if (first)
        {
            if (!_svHold) SvRel();
            foreach (var a in _ai) a.Enabled = false;          // the AI blocks the Flight PB switched on would keep flying
            foreach (var gn in _guns) if (gn.IsFunctional) gn.ApplyAction("Shoot_Off");
            bool hk = SvHomeKnown() || _now - _dynHomeT < 5;
            if (!_svHold && SvLimpHome && hk && rcOk) { _svHold = true; _svArr = false; _svHomeL = SvHomeKnown() ? _svHomeP : _dynHome; _svFull = true; }
            _svRelT = _now;
        }
        if (_svHold && rcOk) SvLimpStep();
        else if (!_svHold && _now - _svRelT >= 1) { _svRelT = _now; SvRel(); }
    }
    if (rcOk)
    {
        if (_tick % 10 == 3 && !_svDock) SvScan();
        if (_tick % 2 == 0 && _svMs.Count > 0) SvMslStep();
        if (_tick % 60 == 11) SvVia();
        if (_tick % 30 == 17) { SvSlow(); SvKeep(); }
        SsStep();
        RpStep();
    }
    SvSend();
}
void SvLimpStep()
{
    if (_now - _dynHomeT < 5 && !SvHomeKnown()) _svHomeL = _dynHome;
    if (_svArr) return;                                     // home: released, dampeners on, nothing more is written
    Vector3D me = _rc.GetPosition(), d = _svHomeL - me; double dist = d.Length();
    if (dist < 30 && _rc.GetShipVelocities().LinearVelocity.Length() < 2) { SvRel(); _svArr = true; return; }
    double v = Math.Min(SvLimpV, Math.Sqrt(2 * _svAmin * dist)), cap = SvCapV(); if (cap > 0) v = Math.Min(v, cap);
    SvSteer(dist > 0.5 ? d / dist * v : Vector3D.Zero, dist > 5 ? d : Vector3D.Zero, Vector3D.Zero);
}
// ---- dock actuator (v1.1): the one place a dock approach writes the overrides
List<IMyThrust> _dkThr = new List<IMyThrust>(); List<IMyGyro> _dkGy = new List<IMyGyro>(); long _dkTk, _dkRelTk = -9; bool _dkOn;
bool DkHold { get { return _tick - _dkRelTk < 2; } }
void DkGo() { _dkThr.Clear(); _dkThr.AddRange(_thr); _dkGy.Clear(); _dkGy.AddRange(_gyros); _dkTk = _tick; _dkOn = true; }   // own blocks: once merged the lists hold the host's too
void DkSteer(Vector3D v, Vector3D f, Vector3D u) { if (_dkOn && _tick - _dkTk >= 3) SvSteer(v, f, u); }
void DkRel()
{
    if (!_dkOn) return;                                       // nothing taken: nothing to give back (no stray DampenersOverride write)
    _dkOn = false; _dkRelTk = _tick;
    if (_down) return;                                        // the Flight is down: SvWatch released already, limp-home may own the actuators
    foreach (var y in _dkGy) { y.GyroOverride = false; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }
    foreach (var th in _dkThr) th.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
}
void SvRepaired() { _svRtb = false; }
double SvCapV() { return _svLimp ? MaxSpeed * MathHelper.Clamp(Math.Sqrt(_svF), SvLimpMin, 1) : 0; }
// the Staff's own flying (limp home, dock approach): velocity command vw (world), nose along fw and roof along up (zero = free); thrust split like Flight's ApplyThrust
double[] _svMx = new double[6];
void SvSteer(Vector3D vw, Vector3D fw, Vector3D up)
{
    var m = _rc.WorldMatrix; Vector3D w = Vector3D.Zero;
    if (fw.LengthSquared() > 1e-6)
    {
        fw.Normalize(); Vector3D ax = Vector3D.Cross(m.Forward, fw); double s = ax.Length(), ang = Math.Atan2(s, Vector3D.Dot(m.Forward, fw));
        w = s > 1e-6 ? ax / s * ang : (ang > 1 ? m.Up * ang : Vector3D.Zero);
        if (up.LengthSquared() > 0.5 && ang < 0.6)
        {
            Vector3D u = up - Vector3D.Dot(up, m.Forward) * m.Forward;
            if (u.LengthSquared() > 1e-4) { u.Normalize(); w += m.Forward * Math.Atan2(Vector3D.Dot(Vector3D.Cross(m.Up, u), m.Forward), Vector3D.Dot(m.Up, u)); }
        }
    }
    w *= 2; if (w.LengthSquared() > 2.25) w = Vector3D.Normalize(w) * 1.5;
    foreach (var g in _gyros)
    {
        if (!g.IsFunctional) continue;
        Vector3D l = Vector3D.TransformNormal(w, MatrixD.Transpose(g.WorldMatrix));
        g.Pitch = -(float)l.X; g.Yaw = -(float)l.Y; g.Roll = -(float)l.Z; g.GyroOverride = true;
    }
    Vector3D acc = (vw - _rc.GetShipVelocities().LinearVelocity) * 2; if (acc.LengthSquared() > 2500) acc = Vector3D.Normalize(acc) * 50;
    Vector3D F = (acc - _rc.GetNaturalGravity()) * _rc.CalculateShipMass().PhysicalMass;
    _rc.DampenersOverride = false;
    Array.Clear(_svMx, 0, 6);
    foreach (var t in _thr) { int i = SvAx(t.WorldMatrix.Backward, m); if (i >= 0 && t.IsWorking) _svMx[i] += t.MaxEffectiveThrust; }
    foreach (var t in _thr)
    {
        int i = SvAx(t.WorldMatrix.Backward, m); if (i < 0) continue;
        double c = Vector3D.Dot(F, t.WorldMatrix.Backward);
        float p = c > 0 && _svMx[i] > 0 ? (float)MathHelper.Clamp(c / _svMx[i], 0, 1) : 0f;
        if (p > 0 && p < 0.002f) p = 0.002f;
        t.ThrustOverridePercentage = p;
    }
}

// ---- upkeep (2 Hz): memory decay and expiry
void SvKeep()
{
    _svDel.Clear();
    foreach (var kv in _svTh)
    {
        var e = kv.Value;
        if (_now - e.T > SvMem) { _svDel.Add(kv.Key); continue; }
        if (_now - e.RT > 60) e.R *= 0.995;                   // rObs: 1 %/s after 60 s (this runs twice a second)
        for (int i = e.S.Count - 1; i >= 0; i--) if (_now - e.S[i].T > 150) e.S.RemoveAt(i);
    }
    foreach (var k in _svDel) _svTh.Remove(k);
}

// ---- SVS (Staff -> Flight)
int _svSentF = -1; long _svSentTk = -99;
void SvSend()
{
    if (_fk == "") return;
    int cls = 0; SvTh e; if (_tgtId != 0 && _svTh.TryGetValue(_tgtId, out e)) cls = SvCls(e);
    int cap = (int)Math.Min(255, Math.Round(SvCapV()));
    bool fb = SvFightOn && _svRtb && _svLost == 0;
    if (fb) { fb = false; foreach (var g in _guns) if (g.IsFunctional && !g.BlockDefinition.SubtypeName.Contains("Flare")) { fb = true; break; } }
    int f = (_svViaOn ? 2 : _svGOn ? 1 : 0) | (_svBOn ? 4 : 0) | (_now < _svDodgeEnd ? 8 : 0) | (_svDam ? 16 : 0) | (_svRtb && _mode == Mode.RTB ? 32 : 0)
        | (_svGOn && _svGReady ? 64 : 0) | (_svGOn ? 128 : 0) | (_svLimp ? 256 | (cap << 16) : 0) | (_svHold ? 512 : 0) | (fb ? 1024 : 0)
        | (_svFloor > 0 || _svAdd != 0 ? 2048 : 0) | ((cls & 15) << 24);
    if (((f ^ _svSentF) & 4095) == 0 && !_svFull && _tick - _svSentTk < 12) return;
    _svSentF = f; _svSentTk = _tick; _svFull = false;
    IGC.SendBroadcastMessage(_fk + ".SVS", new MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>(f, _tgtId, _svViaOn ? _svViaP : _svGOn ? _svGP : Vector3D.Zero,
        _svBOn ? _svBV : Vector3D.Zero, _now < _svDodgeEnd ? _svDodgeV : Vector3D.Zero, new Vector3D(_svFloor, _svAdd, cls == 1 ? 800 : cls == 2 ? 200 : cls == 3 ? 1000 : 0)),
        TransmissionDistance.CurrentConstruct);
}
double SvFoX() { return SvHand && _svDam ? SvFoXv : 0; }

string SvStatus()
{
    var s = new StringBuilder();
    if (_svHold) s.Append(_svArr ? "LIMP HOME: home, holding\n" : "LIMP HOME: flying home (Flight PB down)\n");
    if (_svRtb) s.Append("DAMAGED: ").Append(_svWhy).Append(" - RTB").Append(_svViaOn ? " (via)" : "").Append('\n');
    else if (_svDam) s.Append("Damaged: integrity ").Append(_svI >= 0 ? (_svI * 100).ToString("0") + "%" : "?").Append(_svLimp ? "  LIMP" : "").Append('\n');
    SvTh e;
    if (_tgtId != 0 && _svTh.TryGetValue(_tgtId, out e))
    {
        string[] cn = { "?", "bullet", "explosive", "long" };
        s.Append("Threat : ").Append(cn[SvCls(e)]).Append(" r").Append(e.R.ToString("0")).Append(" dps").Append((e.Dps * 100).ToString("0.0")).Append(_svFloor > 0 ? "  floor " + _svFloor.ToString("0") : "").Append('\n');
    }
    if (_svGOn) s.Append("Gather : ").Append(_svIds.Count).Append(" drones").Append(_svGReady ? ", ready" : "").Append('\n');
    if (_svBOn) s.Append("Orbit  : blind side\n");
    if (_now < _svDodgeEnd) s.Append("MISSILE: dodging, tca ").Append(_svTca.ToString("0.0")).Append(" s\n");
    if (_svAdd != 0) s.Append("Wing   : hand-off ").Append(_svAdd.ToString("+0;-0")).Append(" m\n");
    if (_rc != null) s.Append(SsStatus());
    s.Append(RpStatus());
    return s.ToString();
}
// <<< SURVIVAL STAFF v1.1 <<<

// >>> REPAIR CLIENT v1.1 (self-projector, repair bay and medic patient side, docs/design/survivability.md, docs/design/staff_split.md) >>>
// Patient side of the repair work (docs/design/survivability.md items 15, 17, 18; channels s4.3, antenna range):
//  * REPAIR BAY (item 17): damaged (SURVIVAL STAFF latch, or the words "repair" / "dock"), Flight in RTB or IDLE, not under fire, an own merge block (RpDockOk):
//    the nearest free, stationary bay advertised on <Tag>_RBAY MyTuple<long bayPb, int bay | flags << 8 (bit0 free, bit1 repair, bit2 stock), Vector3D clampPos,
//    Vector3D axis, Vector3D dockFwd, Vector3D dockUp> within 4 km is asked on <Tag>_RREQ MyTuple<long bayPb, int bay, long droneGrid> (resent once after 5 s) and
//    answers on <Tag>_RACK MyTuple<long droneGrid, long bayPb, int bay, int ok>. Then the Staff flies (FOL bit 2: the Flight lets go): TRANSIT to 20 m + radius
//    out along the axis (the RREQ is renewed every 30 s: the bay holds a reservation 90 s), ALIGN (2 deg, 0.3 m/s, 15 s max), INSERT along -axis at "Dock Speed" .. "Dock Final Speed" with a lateral PD (off by 0.8 m within 3 m:
//    back out, 3 retries, then the bay is dropped and a medic may be called), CONTACT = own merge locked: overrides released, dampeners on; the bay repairs and
//    releases and the LAUNCH block flies the take-off. Aborts (overrides released) on fire, a mode word, "fight", the bay's silence or the Flight PB going down.
//  * MEDIC (item 18): no bay, the Flight IDLE (home) and a free <M>_AD MyTuple<long medic, Vector3D pos, int flags> within 3 km: <M>_REQ MyTuple<long medic,
//    long patient, Vector3D pos, int missing>; on <M>_ACK MyTuple<long patient, long medic, int ok> ok = 1 the self-projector goes on and <M>_WORK
//    MyTuple<long patient, Vector3D centre, Vector3D vel, Vector3D p0, Vector3D p1, int packed (remaining | radius dm << 12 | done << 24 | under fire << 25)>
//    is sent at 1 Hz with the two missing / damaged cells nearest the medic (zero = no point). An ACK ok = 0 while serviced = the medic gave up.
//    Done (nothing missing or damaged, projector RemainingBlocks 0): WORK with the done bit, projector off, the mode from before the damage RTB is sent again.
//    Missing cells: an own-grid cell scan (200 cells a tick) when the grid is new (printed / released / repaired), re-checked every 2 s while serviced.
//  * SELF-PROJECTOR (item 15): the block named "... Projector [Self]": on while a medic works or docked at a bay without a projector (no Hive Mother on the
//    grid), off at a printer bay and on return to duty. Written only on those changes (a cradle bay switches it itself in its REPAIR state).
// Keys ([Survival]): Repair At Bay, Dock Speed (m/s), Dock Final Speed (m/s), Call Medic, Medic Channel. Per-script: bool RpDockOk.
// Hosts (v1.1, docs/design/staff_split.md): GnatStaffAI and BasiliskStaffAI, and AuxAI (the [Aux] PB of the other 7 drones, which reach it through AUX LINK).
// The block never writes a thruster / gyro override or DampenersOverride itself: it calls the host's actuator hooks DkGo() (take the actuators), DkSteer(vel, nose,
// roof) (fly that setpoint), DkRel() (let go) and SvRepaired() (clear the damage latch). SURVIVAL STAFF implements them (the Staff flies, FOL bit 2); AuxAI turns
// them into .DKS setpoints for its Staff. SvFly (FOL bit 2) = flying or the 2 ticks after the release write (DkHold).
// Uses: _svRtb, _svDock, _svAmin, _svPrevVerb, SvCapV(), SvB/SvD/SvT, DkGo/DkSteer/DkRel/DkHold/SvRepaired, _rc, _now, _tick, _mode (IDLE/RTB), _fl, _down, Tag,
//   MaxSpeed, Fwd(), FleetV().
bool RpBayOn = true, RpMedic = true, _rpFly, _rpRetry; double RpDockV = 2, RpDockV0 = 0.3, _rpT, _rpT2, _rpStart, _rpNext, _rpScanT = -99; string RpMedCh = "MEDIC", _rpMsg = "";
int _rpS, _rpReq, _rpTry, _rpScanI, _rpChkI = -1, _rpBadN; long _rpKey, _rpMed;
IMyBroadcastListener _rpLb, _rpLa, _rpLm, _rpLk; IMyShipMergeBlock _rpMerge; IMyProjector _rpProj; IMyCubeGrid _rpScanG;
Vector3D _rpO; class RpB { public long Pb; public int Bay, F; public Vector3D P, P0, Ax, Fw, Up; public double T, Bad = -999; public bool Two; }
Dictionary<long, RpB> _rpBays = new Dictionary<long, RpB>(); Dictionary<long, MyTuple<Vector3D, int, double>> _rpMeds = new Dictionary<long, MyTuple<Vector3D, int, double>>();
Dictionary<long, double> _rpMedBad = new Dictionary<long, double>();
List<Vector3I> _rpCells = new List<Vector3I>(), _rpBad = new List<Vector3I>(), _rpBadDone = new List<Vector3I>();
bool SvFly { get { return _rpFly || DkHold; } }

void RpCfg(MyIni i)
{
    RpBayOn = SvB(i, "Repair At Bay", true); RpDockV = Math.Max(0.2, SvD(i, "Dock Speed (m/s)", 2)); RpDockV0 = Math.Max(0.05, SvD(i, "Dock Final Speed (m/s)", 0.3));
    RpMedic = SvB(i, "Call Medic", true); RpMedCh = SvT(i, "Survival", "Medic Channel", "MEDIC").Trim().ToUpperInvariant();
    if (RpMedCh == "") RpMedCh = "MEDIC";
}
void RpListen()
{
    foreach (var l in new[] { _rpLb, _rpLa, _rpLm, _rpLk }) if (l != null) IGC.DisableBroadcastListener(l);
    _rpLb = IGC.RegisterBroadcastListener(Tag + "_RBAY"); _rpLa = IGC.RegisterBroadcastListener(Tag + "_RACK");
    _rpLm = IGC.RegisterBroadcastListener(RpMedCh + "_AD"); _rpLk = IGC.RegisterBroadcastListener(RpMedCh + "_ACK");
}
void RpBlocks()
{
    var g = Me.CubeGrid; _rpMerge = null; _rpProj = null;
    var ml = new List<IMyShipMergeBlock>(); GridTerminalSystem.GetBlocksOfType(ml, b => b.CubeGrid == g && !b.CustomName.Contains("Flare"));
    if (ml.Count > 0) _rpMerge = ml[0];
    var pl = new List<IMyProjector>(); GridTerminalSystem.GetBlocksOfType(pl, b => b.CubeGrid == g && b.CustomName.Contains("Projector [Self]"));
    if (pl.Count > 0) _rpProj = pl[0];
}
void RpProj(bool on) { if (_rpProj != null && _rpProj.IsFunctional && _rpProj.Enabled != on) _rpProj.Enabled = on; }

void RpRead()
{
    long me = Me.CubeGrid.EntityId;
    while (_rpLb != null && _rpLb.HasPendingMessage)
    {
        var m = _rpLb.AcceptMessage();
        if (!(m.Data is MyTuple<long, int, Vector3D, Vector3D, Vector3D, Vector3D>)) continue;
        var d = (MyTuple<long, int, Vector3D, Vector3D, Vector3D, Vector3D>)m.Data;
        if (!FleetV(d.Item3) || d.Item4.LengthSquared() < 0.5 || d.Item5.LengthSquared() < 0.5 || d.Item6.LengthSquared() < 0.5) continue;
        long k = d.Item1 * 97 + (d.Item2 & 255); RpB b;
        if (!_rpBays.TryGetValue(k, out b)) { if (_rpBays.Count >= 16) continue; b = new RpB { Pb = d.Item1, Bay = d.Item2 & 255 }; _rpBays[k] = b; }
        else { b.P0 = b.P; b.Two = _now - b.T < 6; }
        b.F = d.Item2 >> 8; b.P = d.Item3; b.Ax = Vector3D.Normalize(d.Item4); b.Fw = Vector3D.Normalize(d.Item5); b.Up = Vector3D.Normalize(d.Item6); b.T = _now;
    }
    while (_rpLa != null && _rpLa.HasPendingMessage)
    {
        var m = _rpLa.AcceptMessage();
        if (!(m.Data is MyTuple<long, long, int, int>)) continue;
        var d = (MyTuple<long, long, int, int>)m.Data; RpB b;
        if (d.Item1 != me || _rpS != 1 || _rc == null || d.Item2 * 97 + d.Item3 != _rpKey || !_rpBays.TryGetValue(_rpKey, out b)) continue;
        if (d.Item4 == 0) { b.Bad = _now; _rpS = 0; _rpNext = _now + 10; _rpMsg = "bay refused"; continue; }
        _rpO = Vector3D.TransformNormal(_rpMerge.GetPosition() - _rc.GetPosition(), MatrixD.Transpose(_rc.WorldMatrix));   // merge offset in the RC frame, taken once
        DkGo();                                             // the host takes the actuators (FOL bit 2)
        _rpS = 2; _rpFly = true; _rpStart = _rpT2 = _now; _rpTry = 0; _rpMsg = "";
    }
    while (_rpLm != null && _rpLm.HasPendingMessage)
    {
        var m = _rpLm.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, int>)) continue;
        var d = (MyTuple<long, Vector3D, int>)m.Data;
        if (FleetV(d.Item2) && (_rpMeds.Count < 8 || _rpMeds.ContainsKey(d.Item1))) _rpMeds[d.Item1] = new MyTuple<Vector3D, int, double>(d.Item2, d.Item3, _now);
    }
    while (_rpLk != null && _rpLk.HasPendingMessage)
    {
        var m = _rpLk.AcceptMessage();
        if (!(m.Data is MyTuple<long, long, int>)) continue;
        var d = (MyTuple<long, long, int>)m.Data;
        if (d.Item1 != me || d.Item2 != _rpMed) continue;
        if (d.Item3 != 0 && _rpS == 6) { _rpS = 7; _rpT = _now; _rpStart = _now; RpProj(true); _rpScanT = -99; _rpMsg = ""; }
        else if (d.Item3 == 0 && _rpS >= 6) { _rpMedBad[_rpMed] = _now; RpProj(false); _rpS = 0; _rpNext = _now + 10; _rpMsg = "medic gave up"; }
    }
}

void RpWant(int k) { _rpReq = k; _rpNext = 0; }
void RpStop(string why)
{
    if (_rpS >= 2 && _rpS <= 4) DkRel();
    if (_rpS >= 6) { if (_rpS == 7) RpWork(true); RpProj(false); }
    if (_rpS != 5) { _rpS = 0; }
    _rpFly = false; _rpReq = 0; if (why != "") _rpMsg = why;
}
void RpAbort(string why, double bad)
{
    RpB b; if (_rpBays.TryGetValue(_rpKey, out b)) b.Bad = _now + bad;
    DkRel(); _rpFly = false; _rpS = 0; _rpNext = _now + 10; _rpMsg = why;
}

void RpStep()
{
    RpScan();
    bool uf = (_fl & 16) != 0, home = _mode == Mode.RTB || _mode == Mode.IDLE;
    if (_rpS == 0)
    {
        if (_tick % 30 != 21 || _now < _rpNext || !(_svRtb || _rpReq > 0) || _svDock || uf || _down || !(home || _rpReq > 0)) return;
        Vector3D me = _rc.GetPosition(); RpB best = null; double bd = 1.6e7; bool any = false;
        foreach (var kv in _rpBays)
        {
            var b = kv.Value; double q = Vector3D.DistanceSquared(b.P, me);
            if (_now - b.T > 5 || (b.F & 1) == 0 || q > 1.6e7) continue;
            if (_now - b.Bad < 60) continue;
            any = true;
            if (b.Two && (b.P - b.P0).LengthSquared() < 4 && q < bd) { bd = q; best = b; _rpKey = kv.Key; }
        }
        if (RpBayOn && RpDockOk && _rpMerge != null && _rpReq != 3 && best != null)
        {
            IGC.SendBroadcastMessage(Tag + "_RREQ", new MyTuple<long, int, long>(best.Pb, best.Bay, Me.CubeGrid.EntityId));
            _rpS = 1; _rpT = _now; _rpRetry = false; _rpMsg = "asking bay " + best.Bay; return;
        }
        if (!RpMedic || _rpReq == 2 || (any && RpDockOk && _rpMerge != null && RpBayOn) || !(_mode == Mode.IDLE || _rpReq > 0)) return;
        long mb = 0; double md = 9e6;
        foreach (var kv in _rpMeds)
        {
            double bt; if (_now - kv.Value.Item3 > 6 || (kv.Value.Item2 & 1) == 0 || (_rpMedBad.TryGetValue(kv.Key, out bt) && _now - bt < 60)) continue;
            double q = Vector3D.DistanceSquared(kv.Value.Item1, me); if (q < md) { md = q; mb = kv.Key; }
        }
        if (mb == 0) return;
        _rpMed = mb; _rpTry = 0; RpReq(); _rpS = 6; _rpMsg = "calling medic";
        return;
    }
    if (_rpS == 1)
    {
        if (_now - _rpT < 5) return;
        RpB b;
        if (!_rpRetry && _rpBays.TryGetValue(_rpKey, out b)) { _rpRetry = true; _rpT = _now; IGC.SendBroadcastMessage(Tag + "_RREQ", new MyTuple<long, int, long>(b.Pb, b.Bay, Me.CubeGrid.EntityId)); return; }
        RpAbort("bay did not answer", 0); return;
    }
    if (_rpS == 5)
    {   // docked: the bay repairs and releases (LAUNCH flies the take-off); back to duty when the merge pair has opened
        if (_rpMerge == null || !_rpMerge.IsConnected) { if (_svDock) return; RpProj(false); _rpS = 0; SvRepaired(); _rpReq = 0; _rpMsg = "repaired, released"; }
        return;
    }
    if (_rpS >= 6) { RpMedicStep(uf); return; }
    // dock approach (2 transit, 3 align, 4 insert)
    RpB y;
    if (!_rpBays.TryGetValue(_rpKey, out y) || _now - y.T > 10) { RpAbort("bay silent", 60); return; }
    if (uf) { RpAbort("under fire", 30); return; }
    if (_down || _rpMerge == null || !_rpMerge.IsFunctional) { RpAbort("aborted", 0); return; }
    if (!home && _rpReq == 0) { RpAbort("mode changed", 0); return; }
    if (_now - _rpStart > 180) { RpAbort("dock timeout", 600); return; }
    if (_rpMerge.IsConnected)
    {
        DkRel(); _rpFly = false; _rpS = 5; _rpMsg = "docked";
        var hm = new List<IMyProgrammableBlock>(); GridTerminalSystem.GetBlocksOfType(hm, b => b.CubeGrid == Me.CubeGrid && b.CustomData.Contains("[Hive Mother]"));
        RpProj(hm.Count == 0);                               // a printer bay projects the drone itself (and keeps ours off); a cradle bay works through ours
        return;
    }
    if (_now - _rpT2 > 30) { _rpT2 = _now; IGC.SendBroadcastMessage(Tag + "_RREQ", new MyTuple<long, int, long>(y.Pb, y.Bay, Me.CubeGrid.EntityId)); }   // the bay holds us 90 s: renew
    MatrixD R = MatrixD.CreateWorld(Vector3D.Zero, y.Fw, Vector3D.Normalize(y.Up - y.Fw * Vector3D.Dot(y.Up, y.Fw)));
    Vector3D G = y.P + y.Ax * (Me.CubeGrid.GridSize * 0.5) - Vector3D.TransformNormal(_rpO, R), L = G + y.Ax * (20 + Me.CubeGrid.WorldVolume.Radius);
    Vector3D M = _rc.GetPosition(), v; var mw = _rc.WorldMatrix;
    if (_rpS == 2)
    {
        Vector3D d = L - M; double dist = d.Length(), sp = Math.Min(Math.Min(40, MaxSpeed), Math.Min(dist * 0.5, Math.Sqrt(2 * _svAmin * dist))), cap = SvCapV();
        if (cap > 0) sp = Math.Min(sp, cap);
        if (dist < 2) { _rpS = 3; _rpT = _now; }
        DkSteer(dist > 0.1 ? d / dist * sp : Vector3D.Zero, dist > 100 ? d : R.Forward, dist > 100 ? Vector3D.Zero : R.Up);
        return;
    }
    if (_rpS == 3)
    {
        v = (L - M) * 0.5; if (v.LengthSquared() > 4) v = Vector3D.Normalize(v) * 2;
        bool al = Vector3D.Dot(mw.Forward, R.Forward) > 0.9994 && Vector3D.Dot(mw.Up, R.Up) > 0.9994 && _rc.GetShipVelocities().LinearVelocity.LengthSquared() < 0.09;
        if (al || _now - _rpT > 15) { _rpS = 4; _rpMerge.Enabled = true; }   // (LAUNCH switched it off after the last take-off)
        DkSteer(v, R.Forward, R.Up);
        return;
    }
    Vector3D rel = M - G, lat; double along = Vector3D.Dot(rel, y.Ax); lat = rel - y.Ax * along;
    if ((lat.LengthSquared() > 0.64 && along < 3) || along < -1)
    {
        if (++_rpTry > 3) { RpAbort("dock failed", 600); return; }
        _rpS = 3; _rpT = _now; return;                      // back out to the line-up point and try again
    }
    DkSteer(-y.Ax * MathHelper.Clamp(0.5 * along, RpDockV0, RpDockV) - lat, R.Forward, R.Up);
}

void RpReq()
{
    IGC.SendBroadcastMessage(RpMedCh + "_REQ", new MyTuple<long, long, Vector3D, int>(_rpMed, Me.CubeGrid.EntityId, _rc.GetPosition(), Math.Max(_rpBadDone.Count, _rpProj != null ? _rpProj.RemainingBlocks : 0)));
    _rpT = _now; _rpTry++;
}
void RpMedicStep(bool uf)
{
    MyTuple<Vector3D, int, double> md;
    bool seen = _rpMeds.TryGetValue(_rpMed, out md) && _now - md.Item3 < 10;
    if (!seen || _now - _rpStart > 900) { _rpMedBad[_rpMed] = _now; RpStop("medic lost"); _rpNext = _now + 10; return; }
    if (_rpS == 6)
    {
        if (_now - _rpT < 5) return;
        if (_rpTry < 3) RpReq(); else { _rpMedBad[_rpMed] = _now; _rpS = 0; _rpNext = _now + 10; _rpMsg = "medic did not answer"; }
        return;
    }
    if (_now - _rpT < 1) return;
    _rpT = _now;
    if (_rpChkI < 0 && _now - _rpScanT > 2) { _rpChkI = 0; _rpBad.Clear(); }
    bool done = _rpScanT > 0 && _rpBadDone.Count == 0 && (_rpProj == null || !_rpProj.IsProjecting || _rpProj.RemainingBlocks == 0);
    RpWork(done);
    if (!done) return;
    RpProj(false); _rpS = 0; SvRepaired(); _rpReq = 0; _rpMsg = "repaired by medic";
    if (_svPrevVerb != "") Fwd(_svPrevVerb);
}
void RpWork(bool done)
{
    if (_rc == null) return;
    var g = Me.CubeGrid; MyTuple<Vector3D, int, double> md; Vector3D mp = _rpMeds.TryGetValue(_rpMed, out md) ? md.Item1 : _rc.GetPosition(), p0 = Vector3D.Zero, p1 = Vector3D.Zero;
    double d0 = 1e18, d1 = 1e18;
    foreach (var c in _rpBadDone)
    {
        Vector3D w = g.GridIntegerToWorld(c); double q = Vector3D.DistanceSquared(w, mp);
        if (q < d0) { p1 = p0; d1 = d0; p0 = w; d0 = q; } else if (q < d1) { p1 = w; d1 = q; }
    }
    var v = g.WorldVolume;
    int pk = Math.Min(4095, _rpBadDone.Count) | (Math.Min(4095, (int)(v.Radius * 10)) << 12) | (done ? 1 << 24 : 0) | ((_fl & 16) != 0 ? 1 << 25 : 0);
    IGC.SendBroadcastMessage(RpMedCh + "_WORK", new MyTuple<long, Vector3D, Vector3D, Vector3D, Vector3D, int>(g.EntityId, v.Center, _rc.GetShipVelocities().LinearVelocity, p0, p1, pk));
}
// own-grid cells: a full scan when the grid is new (the intact drone), and while a medic works a re-check of those cells (missing, or damaged)
void RpScan()
{
    var g = Me.CubeGrid;
    if (_svDock) return;
    if (g != _rpScanG) { _rpScanG = g; _rpCells.Clear(); _rpScanI = 0; _rpChkI = -1; _rpBadDone.Clear(); _rpScanT = -99; }
    Vector3I a = g.Min, s = g.Max - g.Min + Vector3I.One; int n = s.X * s.Y * s.Z;
    if (_rpScanI < n && n < 200000)
    {
        for (int k = 0; k < 200 && _rpScanI < n; k++, _rpScanI++)
        {
            int i = _rpScanI; var c = new Vector3I(a.X + i % s.X, a.Y + (i / s.X) % s.Y, a.Z + i / (s.X * s.Y));
            if (g.CubeExists(c) && _rpCells.Count < 4000) _rpCells.Add(c);
        }
        return;
    }
    if (_rpChkI < 0) return;
    for (int k = 0; k < 200 && _rpChkI < _rpCells.Count; k++, _rpChkI++)
    {
        var c = _rpCells[_rpChkI]; var b = g.CubeExists(c) ? g.GetCubeBlock(c) : null;
        if (b == null || b.CurrentDamage > 0 || b.BuildIntegrity < b.MaxIntegrity) _rpBad.Add(c);
    }
    if (_rpChkI >= _rpCells.Count) { _rpBadDone.Clear(); _rpBadDone.AddRange(_rpBad); _rpChkI = -1; _rpScanT = _now; }
}
string RpStatus()
{
    string[] sn = { "", "asking bay", "dock transit", "dock align", "dock insert", "docked", "calling medic", "medic working" };
    string s = _rpS > 0 ? "Repair : " + sn[_rpS] + (_rpS == 7 ? ", " + _rpBadDone.Count + " cells" : "") + (_rpS == 4 && _rpTry > 0 ? ", retry " + _rpTry : "") + "\n" : _rpMsg != "" && _svRtb ? "Repair : " + _rpMsg + "\n" : "";
    if (_rpProj != null && _rpProj.Enabled && _rpProj.IsProjecting) s += "Project: " + _rpProj.RemainingBlocks + " blocks to build\n";
    return s;
}
// <<< REPAIR CLIENT v1.1 <<<

// >>> SEEKER v1.0 (the [Seeker] turret reader, cut out of STAFF SEARCH, docs/design/staff_split.md) >>>
// A turret named [Seeker] on this grid is set to "Seeker Group" (PowerSystems / Weapons / Propulsion). Twice a second, when it holds the Flight's target and
// is aimed, the point it shows (the game's block position, else its line with the lead taken off) is raycast by a camera holding twice the ray length (the
// camera charge is shared with the Flight PB). The hull point hit (target frame) goes to WpSeek(): an own-seeker entry of the WEAKPOINT RELAY (ranked first,
// sent to the Flight on .WPT with flag 4096, shared with the wing on <ch>_WP). "seeker on" / "seeker off" (Staff word) switch it; Seek : line on screen.
// Hosts: GnatStaffAI, BasiliskStaffAI (WpSeek = the local WEAKPOINT RELAY) and AuxAI (the [Aux] PB of the 7 split Staffs: WpSeek sends .SKW to the Staff,
// whose AUX LINK calls its WpSeek). The other Staff scripts carry AUX LINK's SkBlocks/SkStep/SkStatus proxies instead.
// Hooks: SkBlocks() at the end of SsBlocks (AuxAI: GetBlocks and on a new seeker configuration); SkStep() in SsStep (AuxAI: Tick); SkStatus() first in SsStatus.
// Uses: _tgtId, _rc, _now, Me, SeekerOn, SeekerGroup, WpSeek().
List<IMyLargeTurretBase> _skT = new List<IMyLargeTurretBase>(); List<IMyCameraBlock> _skCams = new List<IMyCameraBlock>();
double _skT0 = -99; string _skLog = "";

void SkBlocks()
{
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_skCams, b => b.CubeGrid == g);
    foreach (var c in _skCams) c.EnableRaycast = true;
    _skT.Clear();
    if (SeekerOn) GridTerminalSystem.GetBlocksOfType(_skT, b => b.CubeGrid == g && b.CustomName.Contains("[Seeker]"));
    if (_skT.Count == 0) _skLog = ""; else if (_skLog == "") _skLog = "ready, group " + SeekerGroup;   // (re-scans keep the last report)
    foreach (var tu in _skT)
    {
        try { if (SeekerGroup.Length > 0 && tu.GetTargetingGroup() != SeekerGroup) tu.SetTargetingGroup(SeekerGroup); }
        catch (Exception x) { _skLog = "group not set: " + x.Message; }
    }
}

// the turret's view of the Flight's target -> a hull point in the target frame -> the WEAKPOINT RELAY (own-seeker entry)
void SkStep()
{
    if (!SeekerOn || _skT.Count == 0 || _tgtId == 0 || _now - _skT0 < 0.5) return;
    _skT0 = _now;
    foreach (var tu in _skT)
    {
        if (!tu.IsFunctional) { _skLog = "turret damaged"; continue; }
        if (!tu.HasTarget) { _skLog = "turret has no target (group " + tu.GetTargetingGroup() + ")"; continue; }
        var te = tu.GetTargetedEntity();
        if (te.EntityId != _tgtId) { _skLog = "turret on another target"; continue; }
        if (!tu.IsAimed) { _skLog = "turret turning"; continue; }
        Vector3D o = tu.GetPosition(), q;
        bool hp = te.HitPosition.HasValue && (te.HitPosition.Value - te.Position).LengthSquared() > 1;
        if (hp) q = te.HitPosition.Value;                  // the game told us the block itself
        else
        {
            Vector3D d; Vector3D.CreateFromAzimuthAndElevation(tu.Azimuth, tu.Elevation, out d);
            d = Vector3D.TransformNormal(d, tu.WorldMatrix);
            double D = Vector3D.Dot(te.Position - o, d);
            if (D < 5) { _skLog = "turret points away"; continue; }
            // turrets lead their target; take the lead off again (bullets inherit our velocity)
            Vector3D rv = new Vector3D(te.Velocity) - _rc.GetShipVelocities().LinearVelocity;
            string st = tu.BlockDefinition.SubtypeName;
            q = o + d * D - rv * (D / (st.Contains("Calibre") ? 500 : tu is IMyLargeMissileTurret ? 200 : 800));
        }
        IMyCameraBlock cam = null; double best = 0;
        foreach (var c in _skCams)
        {
            if (!c.IsWorking) continue;
            Vector3D cd0 = q - c.GetPosition(); double l0 = cd0.Length();
            if (l0 < 1 || Vector3D.Dot(cd0 / l0, c.WorldMatrix.Forward) < 0.72 || c.AvailableScanRange < 2 * (l0 + 30)) continue;   // the Flight PB shares the charge: twice the ray
            if (c.AvailableScanRange > best) { best = c.AvailableScanRange; cam = c; }
        }
        if (cam == null) { _skLog = "no camera charge/angle"; continue; }
        Vector3D cd = q - cam.GetPosition(); double cl = cd.Length(); cd /= cl;
        var e = cam.Raycast(q + cd * 30);
        if (e.IsEmpty() || e.EntityId != _tgtId || !e.HitPosition.HasValue) { _skLog = (hp ? "block pos given, " : "aim ray, ") + "ray missed hull"; continue; }
        MatrixD inv = MatrixD.Transpose(e.Orientation);
        double hint = hp ? (q - e.HitPosition.Value).Length() : -1;
        int k = SeekerGroup == "PowerSystems" ? 1 : SeekerGroup == "Weapons" ? 2 : SeekerGroup == "Propulsion" ? 3 : 4;
        WpSeek(new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(_tgtId, Vector3D.TransformNormal(e.HitPosition.Value - e.Position, inv),
            Vector3D.TransformNormal(cd, inv), new Vector3D(0, hint, 0), Me.CubeGrid.EntityId, k | 8 | (hp ? 16 : 0)));
        _skLog = (hp ? "block pos given" : "aim ray") + ", " + (q - e.HitPosition.Value).Length().ToString("0") + " m under hull";
        return;
    }
}
string SkStatus() { return _skT.Count > 0 ? "Seek   : " + _skLog + "\n" : ""; }
// <<< SEEKER v1.0 <<<

public Program()
{
    try { GetBlocks(); } catch (Exception ex) { CtorErr("blocks", ex); }
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

// the Flight PB's [Gnat] keys that the Staff shares (read on load, every 10 s and on "reload": no duplicated keys, no tag mismatch)
void LoadFlightCfg()
{
    if (_fpb == null) return;
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(_fpb.CustomData, out res)) return;
    Tag = ini.Get("Gnat", "IGC Tag").ToString(Tag);
    StartMode = ini.Get("Gnat", "Start Mode (FORM/ATTACK/IDLE)").ToString(StartMode);
    MaxSpeed = ini.Get("Gnat", "Combat Speed").ToDouble(MaxSpeed);
    SvCfg(ini); SsCfg(ini);
}

void GetBlocks()
{
    var ctrls = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(ctrls, b => b.IsSameConstructAs(Me) && b.CanControlShip);
    _rc = null;
    foreach (var c in ctrls) if (c is IMyRemoteControl) { _rc = c; break; }
    if (_rc == null && ctrls.Count > 0) _rc = ctrls[0];
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_gyros, b => b.CubeGrid == g);
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.CubeGrid == g);
    GridTerminalSystem.GetBlocksOfType(_guns, b => b.CubeGrid == g && !(b is IMyLargeTurretBase));
    GridTerminalSystem.GetBlocksOfType(_sensors, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_ai, b => b.CubeGrid == g && (b.CustomName.Contains("[AI]") || b.BlockDefinition.ToString().Contains("OffensiveCombat") || b.BlockDefinition.ToString().Contains("FlightMovement")));
    var fl = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(fl, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Flight]", StringComparison.OrdinalIgnoreCase));
    var oldK = _fk; _fpb = null; double best = double.MaxValue;
    foreach (var p in fl) { if (p.CubeGrid != g) continue; double q = (p.GetPosition() - Me.GetPosition()).LengthSquared(); if (q < best) { best = q; _fpb = p; } }
    _fk = _fpb != null ? "WSPL" + _fpb.EntityId : "";
    if (_fk != oldK) { _f2sN = 0; _f2sTick = _tick; _down = false; }
    LoadFlightCfg();
    SvBlocks(); SsBlocks();
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.75f; _lcd.Font = "Monospace";
    string sig = Tag + "|" + _fk;
    if (sig != _sig) { _sig = sig; LoadMods(); }
    Listen();
}

void LoadMods() { FleetLoad(true); LaunchLoad(); }

void Listen()
{
    SvListen();
    string k = Tag + "|" + _fk;
    if (k == _lsKey) return;
    _lsKey = k;
    foreach (var l in new[] { _lHome, _lCmd, _lSwarm, _lF2S, _lFlt, _lArg }) if (l != null) IGC.DisableBroadcastListener(l);
    _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME"); _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD"); _lSwarm = IGC.RegisterBroadcastListener(Tag + "_SWARM");
    _lF2S = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".F2S") : null; _lFlt = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".FLT") : null; _lArg = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".CMD") : null;
}

// =====================================================================
public void Main(string arg, UpdateType src)
{
    try { Tick(arg, src); if (_errRun > 0 && _tick % 300 == 0) { _errRun = 0; _crash = ""; } }
    catch (Exception ex)
    {
        if (_crash != ex.Message || _errRun++ % 60 == 0) { _crash = ex.Message; Echo("Script error: " + ex.Message); if (_lcd != null) _lcd.WriteText("STAFF SCRIPT ERROR\n" + ex.Message + "\nSend this text to Claude."); }
    }
}

void Tick(string arg, UpdateType src)
{
    if ((src & (UpdateType.Trigger | UpdateType.Terminal | UpdateType.Script | UpdateType.Mod)) != 0 && !string.IsNullOrWhiteSpace(arg)) Command(arg);
    if ((src & UpdateType.Update1) == 0) return;
    double dt = Runtime.TimeSinceLastRun.TotalSeconds;
    if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;
    _now += dt; _tick++;
    if (_tick % 600 == 0 || Me.CubeGrid != _myGrid) { _myGrid = Me.CubeGrid; GetBlocks(); }
    ReadF2S();
    SvsRead();
    if (_down && _tick % 60 == 0) GetBlocks();   // Flight is down: look for it (rebuilt PB = new EntityId) at 1 Hz
    if (_rc == null || !_rc.IsFunctional || !_rc.IsSameConstructAs(Me)) { if (_tick % 60 == 0) GetBlocks(); SvWatch(false); if (_tick % 30 == 0) Display(); return; }
    ReadIGC();
    bool burn = LaunchStep(dt, _mode == Mode.IDLE, StartMode, _dynHomeT > 0, _dynHome, Cb);   // automatic take-off after the release from a printer (LAUNCH block)
    Relay();
    WpStep();
    SendFol(burn || SvFly);
    SendHmr();
    SvWatch(burn || _lnS == 2);
    if (_tick % 30 == 0) Display();
}

void ReadF2S()
{
    while (_lF2S != null && _lF2S.HasPendingMessage)
    {
        var m = _lF2S.AcceptMessage();
        if (!(m.Data is MyTuple<int, long, Vector3D, Vector3D, float, string>)) continue;
        var d = (MyTuple<int, long, Vector3D, Vector3D, float, string>)m.Data;
        _fl = d.Item1; _mode = (Mode)Math.Min(d.Item1 & 7, 3); _tgtId = d.Item2; _tgtPos = d.Item3; _aimErr = d.Item4.X; _fireWin = d.Item4.Y; if (d.Item4.Z > 0.1) _aBrake = d.Item4.Z;
        _batt = d.Item5; _gate = d.Item6; _f2sT = _now; _f2sTick = _tick; _f2sN++;
    }
}

// Tag_HOME, Tag_CMD (only the words that are the Staff's), Tag_SWARM, the fleet channel (home, commands -> also to Flight), target shares to the fleet channel
void ReadIGC()
{
    while (_lHome.HasPendingMessage) { var m = _lHome.AcceptMessage(); if (m.Data is Vector3D) { _dynHome = (Vector3D)m.Data; _dynHomeT = _now; } }
    while (_lCmd.HasPendingMessage) { var m = _lCmd.AcceptMessage(); if (m.Data is string) Local((string)m.Data); }
    while (_lArg != null && _lArg.HasPendingMessage) { var m = _lArg.AcceptMessage(); if (m.Data is string) Local((string)m.Data); }   // a PB argument given to the Flight PB
    FleetPump(Cb, p => { _dynHome = p; _dynHomeT = _now; _fhP = p; _fhNew = true; });      // a fleet-channel home: the Flight is told once per fix (FOL bit 3)
    if (_hmrKey != _flChan)
    {
        if (_lHmr != null) IGC.DisableBroadcastListener(_lHmr);
        _lHmr = _flChan != "" ? IGC.RegisterBroadcastListener(_flChan + "_HOMER") : null; _hmrKey = _flChan;
    }
    ReadSwarm();
    while (_lHmr != null && _lHmr.HasPendingMessage) { var m = _lHmr.AcceptMessage(); if (m.Data is double) { _hmrV = (double)m.Data; _hmrT = _now; _hmrNew = true; } }
    while (_lFlt != null && _lFlt.HasPendingMessage)
    {
        var m = _lFlt.AcceptMessage();
        if (m.Data is MyTuple<long, Vector3D, Vector3D, double, long>) FleetSend((MyTuple<long, Vector3D, Vector3D, double, long>)m.Data);
    }
}

// fleet targets -> Flight (K.TGT): coalesced per target id, at most 10 a tick
Dictionary<long, MyTuple<long, Vector3D, Vector3D, double, long>> _rel = new Dictionary<long, MyTuple<long, Vector3D, Vector3D, double, long>>();
void Relay()
{
    SsFeeds();
    foreach (var l in FleetTgtL)
        while (l.HasPendingMessage)
        {
            var m = l.AcceptMessage();
            if (m.Source == IGC.Me || !(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
            if (FleetOk(d)) _rel[d.Item1] = d;
        }
    if (_fk == "" || _tick - _f2sTick > 90) { _rel.Clear(); return; }
    _gone.Clear();
    foreach (var kv in _rel) { if (_gone.Count >= 10) break; IGC.SendBroadcastMessage(_fk + ".TGT", kv.Value, TransmissionDistance.CurrentConstruct); _gone.Add(kv.Key); }
    foreach (var id in _gone) _rel.Remove(id);
}

// the Flight PB's take-off and fleet-home data: every 30 ticks, 2 ticks apart during the burn, and at once on a burn change or a new fleet home fix (no FOLLOW: want / owner are zero)
void SendFol(bool burn)
{
    if (_fk == "") return;
    if (burn == _folBurn && !_fhNew && _tick % (burn ? 2 : 30) != 1) return;
    _folBurn = burn;
    int f = (burn ? 4 : 0) | (_fhNew ? 8 : 0);
    _fhNew = false;
    IGC.SendBroadcastMessage(_fk + ".FOL", new MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>(Vector3D.Zero, Vector3D.Zero, Vector3D.Zero, _fhP, new Vector3D(800, 1200, 0), f), TransmissionDistance.CurrentConstruct);
}

// the fleet channel's carrier radius -> Flight (.HMR): on arrival and once a second while it is fresh
void SendHmr()
{
    if (_fk == "" || _now - _hmrT > 5 || !(_hmrNew || _tick % 60 == 3)) return;
    _hmrNew = false;
    IGC.SendBroadcastMessage(_fk + ".HMR", _hmrV, TransmissionDistance.CurrentConstruct);
}

// =====================================================================
//  COMMANDS (PB argument, toolbar): the Staff's own words are handled here, every word goes to the Flight PB (which ignores a mode it is in already)
void Local(string a)
{
    var w = a.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (w.Length == 0) return;
    SvWord(w);
    switch (w[0])
    {
        case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;
        case "reload": _svWrote = ""; GetBlocks(); LoadMods(); break;
    }
}
void Fwd(string c) { if (_fk != "") IGC.SendBroadcastMessage(_fk + ".S2F", c, TransmissionDistance.CurrentConstruct); }
void Command(string a) { Local(a); Fwd(a); }
void Cb(string c) { Local(c); Fwd(c); }   // fleet commands and the take-off's mode change: the Staff's part, then the Flight PB's

// =====================================================================
//  DISPLAY
void Display()
{
    _sb.Clear();
    _sb.Append("GNAT STAFF v").Append(VER).Append('\n');
    if (_fpb == null) _sb.Append("! no [Flight] PB on this construct\n");
    else if (_down || (_f2sN > 0 && !_fpb.IsWorking)) _sb.Append("FLIGHT PB DOWN  ").Append((_now - _f2sT).ToString("0.0")).Append(" s silent\n");
    else if (_f2sN == 0) _sb.Append("Flight : waiting for the first message\n");
    else _sb.Append("Flight : link OK  F2S ").Append((_now - _f2sT).ToString("0.00")).Append(" s ago\n");
    _sb.Append("Mode   : ").Append(_mode).Append('\n');
    if (_rc == null || !_rc.IsFunctional) _sb.Append("! No working Remote Control / cockpit\n");
    if (_tgtId != 0)
    {
        _sb.Append("Target : ").Append(_tgtId).Append("  range ").Append(_rc != null ? Vector3D.Distance(_tgtPos, _rc.GetPosition()).ToString("0") : "?").Append(" m\n");
        _sb.Append("Aim err: ").Append(_aimErr.ToString("0.00")).Append(" / ").Append(_fireWin.ToString("0.00")).Append(" deg").Append((_fl & 8) != 0 ? "  FIRING" : "").Append('\n');
    }
    else _sb.Append("Target : none\n\n");
    _sb.Append("Battery: ").Append((_batt * 100).ToString("0")).Append("%\n");
    if (_gate != "" && _tgtId != 0) _sb.Append("Hold   : ").Append(_gate).Append('\n');
    _sb.Append("Alarm  : ").Append((_fl & 16) != 0 ? "!! UNDER FIRE" : "-").Append((_fl & 32) != 0 ? "  lock stale" : "").Append((_fl & 64) != 0 ? "  AI searching" : "").Append('\n');
    _sb.Append(LaunchStatus());
    if (Me.OwnerId == 0) _sb.Append("! PB has no owner: set ownership\n");
    if (_crash != "") _sb.Append("! ").Append(_crash).Append('\n');
    _sb.Append(WpStatus());
    _sb.Append(SvStatus());
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
