// =====================================================================
//  GNAT STAFF AI  v1.0-gnat-split  - the STAFF half of the two-PB Gnat ("[Staff] ..." Programmable Block, next to "[Flight] ..." = GnatSwarmAI.cs)
//  For Space Engineers (vanilla, small grid). Paste into a second Programmable Block on the SAME grid as the Flight PB, named "[Staff] ...".
//  Everything the Gnat does that is not the hot path (docs/design/gnat_split.md):
//   * FLEET LINK   fleet channel (targets in, target shares out, home, carrier radius, commands) - same [Fleet] section as the single-PB Gnat
//   * LAUNCH       take-off after the release from a printer - same [Launch] section; Staff flies the burn, Flight lets go of the ship
//   * DISPLAY      mode, target, aim error, battery, alarm, launch and link state
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
string Tag = "GNAT", StartMode = "IDLE";

// ---------------- state ----------------------------------------------
enum Mode { IDLE, FORM, ATTACK, RTB }
Mode _mode = Mode.IDLE;
double _now = 0; long _tick = 0;
Vector3D _dynHome; double _dynHomeT = -99;
IMyShipController _rc; IMyProgrammableBlock _fpb; IMyTextSurface _lcd; IMyCubeGrid _myGrid;
List<IMyGyro> _gyros = new List<IMyGyro>(); List<IMyThrust> _thr = new List<IMyThrust>(); List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
List<IMyFunctionalBlock> _ai = new List<IMyFunctionalBlock>();
StringBuilder _sb = new StringBuilder();
string _fk = "", _sig = "", _lsKey = "", _crash = "";
IMyBroadcastListener _lHome, _lCmd, _lF2S, _lFlt, _lArg, _lHmr; string _hmrKey = null;
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
        if (m == "" || m == "IDLE") m = _lnModeCfg.Trim().ToUpperInvariant();
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
//  SETUP
void CtorErr(string w, Exception ex) { _crash = "setup " + w + ": " + ex.Message; Echo(_crash); }

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
    GridTerminalSystem.GetBlocksOfType(_ai, b => b.CubeGrid == g && (b.CustomName.Contains("[AI]") || b.BlockDefinition.ToString().Contains("OffensiveCombat") || b.BlockDefinition.ToString().Contains("FlightMovement")));
    var fl = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(fl, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Flight]", StringComparison.OrdinalIgnoreCase));
    var oldK = _fk; _fpb = null; double best = double.MaxValue;
    foreach (var p in fl) { if (p.CubeGrid != g) continue; double q = (p.GetPosition() - Me.GetPosition()).LengthSquared(); if (q < best) { best = q; _fpb = p; } }
    _fk = _fpb != null ? "WSPL" + _fpb.EntityId : "";
    if (_fk != oldK) { _f2sN = 0; _f2sTick = _tick; _down = false; }
    LoadFlightCfg();
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.75f; _lcd.Font = "Monospace";
    string sig = Tag + "|" + _fk;
    if (sig != _sig) { _sig = sig; LoadMods(); }
    Listen();
}

void LoadMods() { FleetLoad(true); LaunchLoad(); }

void Listen()
{
    string k = Tag + "|" + _fk;
    if (k == _lsKey) return;
    _lsKey = k;
    foreach (var l in new[] { _lHome, _lCmd, _lF2S, _lFlt, _lArg }) if (l != null) IGC.DisableBroadcastListener(l);
    _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME"); _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
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
    if (_f2sN > 0 && _down && _tick % 60 == 0) GetBlocks();   // Flight is down: look for it (rebuilt PB = new EntityId) at 1 Hz
    if (_rc == null || !_rc.IsFunctional || !_rc.IsSameConstructAs(Me)) { if (_tick % 60 == 0) GetBlocks(); Watch(false); if (_tick % 30 == 0) Display(); return; }
    ReadIGC();
    bool burn = LaunchStep(dt, _mode == Mode.IDLE, StartMode, _dynHomeT > 0, _dynHome, Cb);   // automatic take-off after the release from a printer (LAUNCH block)
    Relay();
    SendFol(burn);
    SendHmr();
    Watch(burn);
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

// Flight PB dead (silent for 90 ticks after it had spoken, or switched off / destroyed): hand the ship back, once, then every second. Never during the Staff's own take-off burn.
void Watch(bool burn)
{
    bool down = _f2sN > 0 && (_tick - _f2sTick > 90 || (_fpb != null && !_fpb.IsWorking));
    if (!down) { _down = false; return; }
    if (burn || _lnS == 2) return;
    bool first = !_down; _down = true;
    if (!first && _now - _relT < 1) return;
    _relT = _now;
    foreach (var y in _gyros) { y.GyroOverride = false; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
    if (first)
    {
        foreach (var a in _ai) a.Enabled = false;   // the AI blocks the Flight PB switched on would keep flying
        foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction("Shoot_Off");
    }
}

// =====================================================================
//  COMMANDS (PB argument, toolbar): the Staff's own words are handled here, every word goes to the Flight PB (which ignores a mode it is in already)
void Local(string a)
{
    var w = a.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (w.Length == 0) return;
    switch (w[0])
    {
        case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;
        case "reload": GetBlocks(); LoadMods(); break;
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
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
