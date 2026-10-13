// =====================================================================
//  HARRIER DRONE AI  v2.2-harrier  - interceptor: hunts drones, rockets, kamikazes
//  For Space Engineers (vanilla, small grid). Paste into a Programmable Block.
//  Derived from HORNET DRONE AI v1.8 (same tracking, hive over IGC, AI hand-off,
//  damage sensing, printer join); the engagement doctrine is new:
//   * Fast, close, nimble: standoff 250 m (175 m against small contacts), orbit
//     30 m/s, jink 16 m/s, hard turn rate. The Harrier airframe has four main
//     engines, so it can run down anything small and break away from big guns.
//   * Target priority: missiles (when closing) and small fast grids first, large
//     grids last (SmallBonus / LargePenalty / FastCloser in Custom Data).
//   * Missiles (MyDetectedEntityType.Missile) are valid targets.
//   * Listens to / publishes target cues on the shared channel "Cue Channel"
//     (default DRONE_CUE), so an Aegis or a carrier can point Harriers at threats.
// ---------------------------------------------------------------------
//  WHAT IT DOES
//   * Finds targets from: sensor blocks (all around, ~50 m), designators
//     (turrets / Custom Turret Controllers), camera raycast lock, an automatic
//     camera scan sweep that yaws and nods up/down, and
//     targets shared over antenna (IGC) by a mothership or other drones.
//   * Tracks each contact (position, velocity, filtered acceleration) and
//     keeps a lock alive with predictive raycasts.
//   * Computes a 2nd-order intercept (lead) point and steers the guns onto
//     it with a PD gyro controller + line-of-sight-rate feed-forward.
//   * Fires only when the lead solution is inside the target's silhouette,
//     in range, and the line of fire is clear of friendlies.
//   * Flies itself: standoff orbit with random jinking, guard a home point,
//     return-to-base on low battery / no ammo. Thrust is power-limited so
//     small batteries never brown out.
//
//  v2.1 (sim audit 1; details in docs/script-fix-notes.md):
//   * AI hand-over: confirm rays down the boresight + a spiral (a ship is only 0.2-0.8 deg wide), one long (2600 m) and short rays
//   * lock upkeep: cheaper rays at known hull points, ghost target 8 s (acceleration trusted 1 s), widening search ellipse, loss sharing
//   * hull-gap fix: a ray that misses the predicted centre is retried at the centre before the search widens
//   * debris / one-block wrecks count as finished (they split off as their own grid and held the guns forever)
//   * stale tracks are not fired on (Max Track Age To Fire), wingmate separation, evasive jink 35-40 m/s every 1-2 s
//   * target feeds from other fleets ("Listen To IGC Tags")
//
//  v2.2 (round 2, "Round 2: hornet-harrier" in docs/script-fix-notes.md):
//   * AI confirm rays: both forward cameras fire the long (2.6 km) ray; after a fight they shorten to the last known range (a ray costs its length)
//   * AI re-pick: an AI block that has held the nose on something for 6 s without a lock is power-cycled once (it may be stuck on a wreck)
//   * HUNT search patrol: with nothing found for 15 s the drone cruises round a square of 1.2 km radius with listening stops for the AI blocks
//   * two cameras that can cast in the same tick never aim at the same point; config keys with '=' in their name were renamed (MyIni splits there)
//
//  COMMANDS (PB argument, toolbar, or IGC "<Tag>_CMD")
//   guard    hold home point, engage anything inside GuardRadius
//   hunt     chase and engage any target, actively scan
//   assist   you fly, script aims + fires when a target is locked
//   rtb      fly home, then idle
//   idle     release all control (dampeners back on)
//   lock     raycast lock on whatever the nose camera points at
//   unlock   drop the current target
//   home     set home point to current position
//   range <m>   set standoff range       reload   re-read CustomData
//
//  Settings live in the PB's Custom Data (written on first run).
// =====================================================================

const string VER = "2.2-harrier";

// ---------------- settings (overwritten from Custom Data) -------------
string Tag = "HARRIER";
string CueTag = "DRONE_CUE"; // shared threat-cue channel (same tuple as <Tag>_TGT)
double ProjSpeed = 800;      // m/s  gatling bullet (Weapons/Ammos data: 800 m/s, 800 m)
double MaxFireRange = 700;   // gatling rounds vanish at 800 m; accuracy falls with flight time
double Standoff = 250;       // preferred engage distance
double SmallStandoffFrac = 0.7; // standoff multiplier against small contacts (radius < 8 m)
double MinRange = 40;        // never closer than this
double SmallBonus = 300, LargePenalty = 200, FastCloser = 250, MissileBonus = 450; // target scoring (lower score wins)
bool TargetMissiles = true;
double GuardRadius = 1500;   // GUARD only engages within this of home
double MaxSpeed = 98;        // combat speed cap (vanilla small-ship limit is 100 m/s)
double CruiseSpeed = 60;     // non-combat cap (saves power)
double OrbitSpeed = 40;      // tangential speed around the target (sim sweep: 30 -> 40 with the faster jink below)
double JinkEvery = 1.0, JinkEveryRand = 1.0;   // a new random evasive direction every JinkEvery + rand x JinkEveryRand seconds
double JinkSpeed = 35;       // random evasive velocity (was 16; lock-duel sweep: +4 points win, -7 % damage taken)
double LockRange = 2500;     // manual "lock" raycast distance
double CloseScanRange = 220; // all-around camera scan distance (small fast contacts)
bool CloseScan = true;
double ScanRange = 1200;     // auto-scan raycast distance
double ScanCone = 35;        // auto-scan half-angle (deg)
double AimKp = 8, AimKd = 0.4, MaxTurn = 6; // gyro tuning (rad/s)
double FireTol = 1.0;        // multiplier on silhouette fire window
double LeadScale = 1.0;      // scales the lead offset if shots land consistently ahead/behind
bool UseTargetAccel = true;
bool SplitFocus = true;
double DisabledGrace = 5, EvadeSpeed = 20;
bool AiAssist = true; double AiConfirmRange = 2600;   // confirm rays are this long (0.77 casts/s per camera at 2 km/s charge; both forward cameras reach the full 2.6 km)
double AiConfirmLong = 2600;   // the first forward camera casts this long instead (0.77 casts/s)
double RtbBattery = 0.15;    // fraction
double SepRange = 70, SepSpeed = 25;   // wingmate separation: push away from hive mates closer than this (m) at up to this speed (m/s)
bool TargetLarge = true, TargetSmall = true, TargetChars = false, TargetNeutral = false;
bool AutoScan = true, ShareTargets = true, Jink = true, FriendCheck = true, InheritVel = true;
string StartMode = "IDLE";   // safe default: drone waits for a command

// ---------------- state ----------------------------------------------
enum Mode { IDLE, GUARD, HUNT, ASSIST, RTB }
Mode _mode = Mode.IDLE;
MyIni _ini = new MyIni();
double _now = 0;
long _tick = 0;
Vector3D _home;
bool _homeSet = false;
Vector3D _dynHome; double _dynHomeT = -99;

// blocks
IMyShipController _rc;
List<IMyGyro> _gyros = new List<IMyGyro>();
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyThrust>[] _thrDir = new List<IMyThrust>[6];
List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
List<IMyCameraBlock> _cams = new List<IMyCameraBlock>();
List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
List<IMyTurretControlBlock> _ctcs = new List<IMyTurretControlBlock>();
List<IMyBatteryBlock> _bats = new List<IMyBatteryBlock>();
IMyTextSurface _lcd;
IMyTerminalBlock _aimRef;

// IGC
IMyBroadcastListener _lTgt, _lCmd, _lHome, _lCue;
string ListenTags = "HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER";   // other fleets' target feeds ("<TAG>_TGT"), same tuple on every fleet
List<IMyBroadcastListener> _lFeed = new List<IMyBroadcastListener>();
void RegisterFeeds()
{
    foreach (var t in ListenTags.Split(','))
    { string tt = t.Trim().ToUpperInvariant(); if (tt != "" && tt != Tag.ToUpperInvariant()) _lFeed.Add(IGC.RegisterBroadcastListener(tt + "_TGT")); }
}


// tracking
class Track
{
    public long Id; public Vector3D Pos, Vel, Acc; public double AccMag;
    public double Spin, SpinSince = -1, Rad0, Raw, RawMax, DeadAt = -1, DisSince = -1; public Vector3D OF, OU; public bool HasOri;
    public MatrixD O = MatrixD.Identity; public List<Vector3D> Hull = new List<Vector3D>(); public int HullNext;
    public Vector3D Thrust, AimLocal; public double AimT = -99;
    // how fast it's rotating, from orientation changes between sightings
    public void Ori(MatrixD o, double dt)
    {
        if (HasOri && dt > 0.04 && dt < 2)
        {
            double a = Math.Max(Math.Acos(MathHelper.Clamp(Vector3D.Dot(OF, o.Forward), -1, 1)), Math.Acos(MathHelper.Clamp(Vector3D.Dot(OU, o.Up), -1, 1)));
            Spin = Spin * 0.7 + a / dt * 0.3;
        }
        OF = o.Forward; OU = o.Up; HasOri = true; O = o;
    } public double Radius, Seen; public string Src;
    public MyDetectedEntityType Type; public string Name;
    public void Set(Vector3D p, Vector3D v, double r, double now, string src)
    {
        double dt = now - Seen;
        if (Seen > 0 && dt > 0.04 && dt < 2)
        {
            Vector3D a = (v - Vel) / dt;
            if (a.LengthSquared() > 10000) a = Vector3D.Normalize(a) * 100;
            Acc = Acc * 0.8 + a * 0.2;
            AccMag = AccMag * 0.8 + a.Length() * 0.2;
            // which way (in its own frame) it usually pushes; its main engines sit on the opposite side
            if (HasOri && a.LengthSquared() > 1) Thrust = Thrust * 0.9 + Vector3D.TransformNormal(Vector3D.Normalize(a), MatrixD.Transpose(O)) * 0.1;
        }
        else if (dt >= 2) { Acc = Vector3D.Zero; AccMag = 0; }
        Pos = p; Vel = v; Radius = Math.Max(r, 1.5); Raw = r; RawMax = Math.Max(RawMax, r); Seen = now; Src = src; Rad0 = Math.Max(Rad0, Radius);
    }
    // Acceleration we trust for prediction. A tumbling or jinking target's acceleration keeps changing
    // direction: the average vector is much smaller than the average size, so it gets faded out.
    public Vector3D EA()
    {
        double m = Acc.Length();
        if (m < 0.5 || AccMag < 1e-3) return Vector3D.Zero;
        double steady = MathHelper.Clamp((m / AccMag - 0.6) / 0.3, 0, 1);
        return Acc * steady;
    }
    // The measured acceleration is only trusted for AccHold seconds after the last sighting: a ghost track that is a few seconds old
    // would otherwise be extrapolated with 0.5 a t^2 (6 m/s^2 over 5 s = 75 m, and a phantom 30 m/s) and drag the guns away from the target.
    const double AccHold = 1.0;
    public Vector3D P(double now) { double t = now - Seen, ta = Math.Min(t, AccHold); return Pos + Vel * t + EA() * (ta * t - 0.5 * ta * ta); }
    public Vector3D V(double now) { return Vel + EA() * Math.Min(now - Seen, AccHold); }
}
Dictionary<long, Track> _contacts = new Dictionary<long, Track>();
List<long> _drop = new List<long>();
Track _tgt;
const double TrackTimeout = 4.0;

// control
Vector3D _prevErr, _prevAim; bool _havePrev = false;
double _powerScale = 1.0;
bool _firing = false;
double _holdFireUntil = 0;
Vector3D _jink; double _nextJink = 0; double _orbitSign = 1;
int _scanIdx = 0;
double _sweepYaw = 0;
Random _rng = new Random();
string _err = "";
double _aimErrDeg = 0, _fireWinDeg = 0, _leadT = 0;
double _avgMs = 0; int _peakIns = 0;   // instruction counter: PB limit is Runtime.MaxInstructionCount per run
StringBuilder _sb = new StringBuilder();

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
string _flAllowed = "hunt,attack,guard,form,assist,rtb,idle,stop,launch,abort,split,focus,next,unlock,aialert";
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

// >>> LAUNCH v1.0 (shared block: identical in every drone script, see docs/launch-protocol.md) >>>
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
bool _lnAuto = true, _lnArmed, _lnHold, _lnForce, _lnLocOk, _lnDock, _lnHomeOk, _lnPending;
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

// the release is only complete when the grid has split: while the printer's merge block is still on this grid the thrusters/gyros found here may be the printer's
bool LnReady()
{
    var g = Me.CubeGrid;
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
    double best = 0;
    foreach (var t in _lnThr) if (t.IsFunctional) best = Math.Max(best, Vector3D.Dot(t.WorldMatrix.Backward, _lnDir));
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
    return _lnArmed ? "Launch: armed\n" : "";
}
// <<< LAUNCH <<<

public Program()
{
    for (int i = 0; i < 6; i++) _thrDir[i] = new List<IMyThrust>();
    LoadConfig();
    FleetLoad(true);
    LaunchLoad();
    _mode = ParseMode(StartMode, Mode.IDLE);
    if (!string.IsNullOrEmpty(Storage))
    {
        var p = Storage.Split(';');
        if (p.Length >= 5)
        {
            _mode = ParseMode(p[0], _mode);
            double x, y, z;
            if (double.TryParse(p[1], out x) && double.TryParse(p[2], out y) && double.TryParse(p[3], out z))
            { _home = new Vector3D(x, y, z); _homeSet = p[4] == "1"; }
        }
    }
    _lTgt = IGC.RegisterBroadcastListener(Tag + "_TGT");
    _lCue = IGC.RegisterBroadcastListener(CueTag);
    _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
    _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME");
    _lSwarm = IGC.RegisterBroadcastListener(Tag + "_SWARM");
    RegisterFeeds();
    GetBlocks();
    if (!_homeSet && _rc != null) { _home = _rc.GetPosition(); _homeSet = true; }
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save()
{
    Storage = string.Join(";", _mode.ToString(), _home.X.ToString("R"), _home.Y.ToString("R"), _home.Z.ToString("R"), _homeSet ? "1" : "0");
}

Mode ParseMode(string s, Mode def)
{
    switch (s.Trim().ToUpperInvariant())
    {
        case "IDLE": return Mode.IDLE; case "GUARD": return Mode.GUARD; case "HUNT": return Mode.HUNT;
        case "ASSIST": return Mode.ASSIST; case "RTB": return Mode.RTB; default: return def;
    }
}

// =====================================================================
public void Main(string arg, UpdateType src)
{
    try { Tick(arg, src); }
    catch (Exception ex) { _crash = ex.Message; Echo("Script error: " + ex.Message); if (_lcd != null) _lcd.WriteText("SCRIPT ERROR\n" + ex.Message + "\nSend this text to Claude."); }
}

void Tick(string arg, UpdateType src)
{
    if ((src & (UpdateType.Trigger | UpdateType.Terminal | UpdateType.Script | UpdateType.Mod)) != 0 && !string.IsNullOrWhiteSpace(arg))
        Command(arg);
    if ((src & UpdateType.Update1) == 0) return;

    double dt = Runtime.TimeSinceLastRun.TotalSeconds;
    if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;
    _now += dt; _tick++;
    _avgMs = _avgMs * 0.98 + Runtime.LastRunTimeMs * 0.02;

    if (_tick % 600 == 0) GetBlocks();
    CheckGrid();
    if (_rc == null || !_rc.IsFunctional) { _err = "No working Remote Control / cockpit"; Display(); Release(); return; }

    ReadIGC(); ReadSwarm();
    if (_tick % 6 == 3) BroadcastStatus();
    StepSlimScan();
    if (_tick % 20 == 7) CheckDamage();
    if (_tick % 6 == 0) PollDesignators();
    if (_tick % 10 == 5) PollSensors();
    if (_tick % 10 == 0) { PruneAndSelect(); PowerAndHealth(); }
    MaintainLock();
    if (_mode != Mode.IDLE) OmniScan();
    if (_tgt == null && !_aiOn && AutoScan && (_mode == Mode.GUARD || _mode == Mode.HUNT)) AutoScanStep();
    if (ShareTargets && _tgt != null && _tgt.Src != "IGC" && _tick % 30 == 0 && _now - _tgt.Seen < 1.0) ShareTarget();

    if (!AiStep()) Control(dt);
    _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount);
    if (_tick % 30 == 0) Display();
}

// =====================================================================
//  COMMANDS
void Command(string a)
{
    var w = a.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (w.Length == 0) return;
    switch (w[0])
    {
        case "guard": SetMode(Mode.GUARD); break;
        case "hunt": SetMode(Mode.HUNT); break;
        case "assist": SetMode(Mode.ASSIST); break;
        case "rtb": SetMode(Mode.RTB); break;
        case "idle": case "stop": SetMode(Mode.IDLE); break;
        case "lock": ManualLock(); break;
        case "unlock": _tgt = null; _contacts.Clear(); break;
        case "next": if (_tgt != null) IgnoreTarget(_tgt, 60, true); break;
        case "split": SplitFocus = true; break;
        case "aialert": _alertT = _now; break;
        case "ai": if (w.Length > 1) AiAssist = w[1] == "on"; break;
        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;
        case "focus": SplitFocus = false; break;
        case "home": if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } break;
        case "range":
            double r; if (w.Length > 1 && double.TryParse(w[1], out r)) Standoff = MathHelper.Clamp(r, MinRange, MaxFireRange); break;
        case "reload": LoadConfig(); FleetLoad(true); LaunchLoad(); GetBlocks(); break;
        case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;
    }
}

void SetMode(Mode m)
{
    _mode = m; _noTgtSince = _now; _patrolLeg = false;
    if (m == Mode.IDLE || m == Mode.ASSIST) Release();
}

// =====================================================================
//  CONFIG
void LoadConfig()
{
    MyIniParseResult res;
    if (!_ini.TryParse(Me.CustomData, out res)) { _err = "CustomData parse error line " + res.LineNo; return; }
    const string S = "Harrier";
    Tag = Cfg(S, "IGC Tag", Tag);
    CueTag = Cfg(S, "Cue Channel", CueTag);
    StartMode = Cfg(S, "Start Mode (GUARD/HUNT/ASSIST/IDLE)", StartMode);
    ProjSpeed = Cfg(S, "Projectile Speed", ProjSpeed);
    InheritVel = Cfg(S, "Projectiles Inherit Ship Velocity", InheritVel);
    MaxFireRange = Cfg(S, "Max Fire Range", MaxFireRange);
    Standoff = Cfg(S, "Standoff Range", Standoff);
    SmallStandoffFrac = Cfg(S, "Small Target Standoff Fraction", SmallStandoffFrac);
    SmallBonus = Cfg(S, "Target Priority Small Bonus", SmallBonus);
    LargePenalty = Cfg(S, "Target Priority Large Penalty", LargePenalty);
    FastCloser = Cfg(S, "Target Priority Fast Closer Bonus", FastCloser);
    MissileBonus = Cfg(S, "Target Priority Missile Bonus", MissileBonus);
    TargetMissiles = Cfg(S, "Target Missiles", TargetMissiles);
    MinRange = Cfg(S, "Min Range", MinRange);
    GuardRadius = Cfg(S, "Guard Radius", GuardRadius);
    MaxSpeed = Cfg(S, "Combat Speed", MaxSpeed);
    CruiseSpeed = Cfg(S, "Cruise Speed", CruiseSpeed);
    OrbitSpeed = Cfg(S, "Orbit Speed", OrbitSpeed);
    Jink = Cfg(S, "Evasive Jinking", Jink);
    JinkSpeed = Cfg(S, "Jink Speed", JinkSpeed);
    JinkEvery = Cfg(S, "Jink Interval (s)", JinkEvery);
    JinkEveryRand = Cfg(S, "Jink Interval Random (s)", JinkEveryRand);
    LockRange = Cfg(S, "Manual Lock Range", LockRange);
    AutoScan = Cfg(S, "Auto Camera Scan", AutoScan);
    CloseScan = Cfg(S, "All-around Close Scan", CloseScan);
    CloseScanRange = Cfg(S, "Close Scan Range", CloseScanRange);
    ScanRange = Cfg(S, "Scan Range", ScanRange);
    ScanCone = Cfg(S, "Scan Cone Degrees", ScanCone);
    TargetLarge = Cfg(S, "Target Large Grids", TargetLarge);
    TargetSmall = Cfg(S, "Target Small Grids", TargetSmall);
    TargetChars = Cfg(S, "Target Characters", TargetChars);
    TargetNeutral = Cfg(S, "Target Neutral/Unowned", TargetNeutral);
    ShareTargets = Cfg(S, "Share Targets Over IGC", ShareTargets);
    FriendCheck = Cfg(S, "Friendly Fire Check", FriendCheck);
    ListenTags = Cfg(S, "Listen To IGC Tags (target feeds)", ListenTags);
    FireStaleMax = Cfg(S, "Max Track Age To Fire (s)", FireStaleMax);
    SearchTenths = (int)Cfg(S, "Search Share Tenths (stale lock)", (double)SearchTenths);
    SearchTenthsLate = (int)Cfg(S, "Search Share Tenths (stale > 1.5 s)", (double)SearchTenthsLate);
    SepRange = Cfg(S, "Wingmate Separation Range", SepRange);
    SepSpeed = Cfg(S, "Wingmate Separation Speed", SepSpeed);
    FireTol = Cfg(S, "Fire Window Multiplier", FireTol);
    LeadScale = Cfg(S, "Lead Scale (1: as calculated)", LeadScale);
    UseTargetAccel = Cfg(S, "Use Target Acceleration", UseTargetAccel);
    SplitFocus = Cfg(S, "Split Targets Across Hive (false: focus fire)", SplitFocus);
    DisabledGrace = Cfg(S, "Seconds Before Dropping Disabled Target", DisabledGrace);
    EvadeSpeed = Cfg(S, "Evade Speed When Hit", EvadeSpeed);
    AimMode = Cfg(S, "Aim Point (AUTO/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();
    AiAssist = Cfg(S, "AI Block Search Assist", AiAssist);
    AiConfirmRange = Cfg(S, "AI Confirm Raycast Range", AiConfirmRange);
    AiConfirmLong = Cfg(S, "AI Confirm Long Raycast Range", AiConfirmLong);
    AiConfirmAdaptive = Cfg(S, "AI Confirm Rays Shorten After A Fight", AiConfirmAdaptive);
    AiSpiralMin = Cfg(S, "AI Confirm Spiral Min (deg)", AiSpiralMin);
    AiSpiralMax = Cfg(S, "AI Confirm Spiral Max (deg)", AiSpiralMax);
    AiSpiralPow = Cfg(S, "AI Confirm Spiral Exponent", AiSpiralPow);
    AiStaleTakeover = Cfg(S, "AI Takes Over After Stale Lock (s)", AiStaleTakeover);
    GhostTime = Cfg(S, "Ghost Target Time (s)", GhostTime);
    Stickiness = Cfg(S, "Target Stickiness (score bonus for the current target)", Stickiness);
    AiRepick = Cfg(S, "AI Re-pick Interval (s; 0 off)", AiRepick);
    PatrolOn = Cfg(S, "Search Patrol When Nothing Found (HUNT)", PatrolOn);
    PatrolRadius = Cfg(S, "Search Patrol Radius (m)", PatrolRadius);
    PatrolAfter = Cfg(S, "Search Patrol Starts After (s)", PatrolAfter);
    PatrolAfterFight = Cfg(S, "Search Patrol Starts After A Fight (s)", PatrolAfterFight);
    PatrolListen = Cfg(S, "Search Patrol Listening Stop (s)", PatrolListen);
    PatrolSpeed = Cfg(S, "Search Patrol Speed", PatrolSpeed);
    AiRepickWindow = Cfg(S, "AI Re-pick Only Within (s) Of Last Target", AiRepickWindow);
    RtbBattery = Cfg(S, "RTB Battery Fraction", RtbBattery);
    AimKp = Cfg(S, "Aim Kp", AimKp);
    AimKd = Cfg(S, "Aim Kd", AimKd);
    MaxTurn = Cfg(S, "Max Turn Rate rad/s", MaxTurn);
    string out1 = _ini.ToString();
    if (out1 != Me.CustomData) Me.CustomData = out1;
}
string Cfg(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double Cfg(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool Cfg(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

// =====================================================================
//  BLOCKS
void GetBlocks()
{
    _err = "";
    var ctrls = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(ctrls, b => b.IsSameConstructAs(Me) && b.CanControlShip);
    _rc = null;
    foreach (var c in ctrls) if (c is IMyRemoteControl) { _rc = c; break; }
    if (_rc == null && ctrls.Count > 0) _rc = ctrls[0];

    GridTerminalSystem.GetBlocksOfType(_gyros, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_guns, b => b.IsSameConstructAs(Me) && !(b is IMyLargeTurretBase));
    GridTerminalSystem.GetBlocksOfType(_cams, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_turrets, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_ctcs, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_bats, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_sensors, b => b.IsSameConstructAs(Me));
    ConfigureSensors();
    foreach (var c in _cams) c.EnableRaycast = true;
    ScanSlims();
    FindAi();

    for (int i = 0; i < 6; i++) _thrDir[i].Clear();
    if (_rc != null)
    {
        MatrixD m = _rc.WorldMatrix;
        Vector3D[] dirs = { m.Forward, m.Backward, m.Left, m.Right, m.Up, m.Down };
        foreach (var t in _thr)
        {
            Vector3D push = t.WorldMatrix.Backward; // thrusters push opposite their exhaust
            for (int i = 0; i < 6; i++) if (Vector3D.Dot(push, dirs[i]) > 0.9) { _thrDir[i].Add(t); break; }
        }
    }
    _aimRef = _guns.Count > 0 ? (IMyTerminalBlock)_guns[0] : _rc;
    _lcd = Me.GetSurface(0);
    _lcd.ContentType = ContentType.TEXT_AND_IMAGE;
    _lcd.FontSize = 0.75f;
    _lcd.Font = "Monospace";
}

// =====================================================================
//  TARGET SOURCES
bool Valid(MyDetectedEntityInfo e)
{
    if (e.IsEmpty() || Ignored(e.EntityId) || e.EntityId == Me.CubeGrid.EntityId) return false;
    switch (e.Type)
    {
        case MyDetectedEntityType.LargeGrid: if (!TargetLarge) return false; break;
        case MyDetectedEntityType.SmallGrid: if (!TargetSmall) return false; break;
        case MyDetectedEntityType.CharacterHuman:
        case MyDetectedEntityType.CharacterOther: if (!TargetChars) return false; break;
        case MyDetectedEntityType.Missile: if (!TargetMissiles) return false; break;
        default: return false;
    }
    var r = e.Relationship;
    if (r == MyRelationsBetweenPlayerAndBlock.Enemies) return true;
    return TargetNeutral && (r == MyRelationsBetweenPlayerAndBlock.Neutral || r == MyRelationsBetweenPlayerAndBlock.NoOwnership);
}

bool Friendly(MyDetectedEntityInfo e)
{
    var r = e.Relationship;
    return r == MyRelationsBetweenPlayerAndBlock.Owner || r == MyRelationsBetweenPlayerAndBlock.FactionShare || r == MyRelationsBetweenPlayerAndBlock.Friends;
}

Track AddContact(MyDetectedEntityInfo e, string src)
{
    Track t;
    if (!_contacts.TryGetValue(e.EntityId, out t))
    {
        t = new Track { Id = e.EntityId };
        _contacts[e.EntityId] = t;
    }
    t.Type = e.Type; t.Name = e.Name;
    double rad = e.BoundingBox.Size.Length() * 0.5;
    t.Ori(e.Orientation, _now - t.Seen);
    t.Set(e.Position, new Vector3D(e.Velocity), rad, _now, src);
    StoreHull(t, e);
    return t;
}

void PollDesignators()
{
    foreach (var t in _turrets)
    {
        if (!t.IsFunctional || !t.HasTarget) continue;
        var e = t.GetTargetedEntity();
        if (Valid(e)) AddContact(e, "TURRET");
    }
    foreach (var c in _ctcs)
    {
        if (!c.IsFunctional || !c.HasTarget) continue;
        var e = c.GetTargetedEntity();
        if (Valid(e)) AddContact(e, "CTC");
    }
}

void ReadIGC()
{
    ReadTgt(_lTgt); ReadTgt(_lCue);
    foreach (var fl in _lFeed) ReadTgt(fl);
    foreach (var fl in FleetTgtL) ReadTgt(fl);
    while (_lCmd.HasPendingMessage)
    {
        var msg = _lCmd.AcceptMessage();
        if (msg.Data is string) Command((string)msg.Data);
    }
    while (_lHome.HasPendingMessage)
    {
        var msg = _lHome.AcceptMessage();
        if (msg.Data is Vector3D) { _dynHome = (Vector3D)msg.Data; _dynHomeT = _now; }
    }
    FleetPump(Command, p => { _dynHome = p; _dynHomeT = _now; });
}

void ReadTgt(IMyBroadcastListener l)
{
    while (l.HasPendingMessage)
    {
        var msg = l.AcceptMessage();
        if (msg.Source == IGC.Me || !(msg.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, double, long>)msg.Data;
        if (d.Item4 < 0) { _ignore[d.Item1] = _now + 120; _contacts.Remove(d.Item1); if (_tgt != null && _tgt.Id == d.Item1) _tgt = null; continue; }
        if (Ignored(d.Item1) || d.Item1 == Me.CubeGrid.EntityId || _mates.ContainsKey(d.Item1) || !FleetOk(d)) continue;   // (a feed can name my own grid or a wingmate)
        Track t;
        if (_contacts.TryGetValue(d.Item1, out t) && t.Src != "IGC" && _now - t.Seen < 0.5) continue; // own data is fresher
        if (t == null) { t = new Track { Id = d.Item1, Type = MyDetectedEntityType.LargeGrid, Name = "shared" }; _contacts[d.Item1] = t; }
        t.Set(d.Item2, d.Item3, d.Item4, _now, "IGC");
    }
}

void ShareTarget()
{
    var t = _tgt;
    var tup = new MyTuple<long, Vector3D, Vector3D, double, long>(t.Id, t.P(_now), t.V(_now), t.Radius, Me.EntityId);
    IGC.SendBroadcastMessage(Tag + "_TGT", tup);
    FleetSend(tup);
    if (t.Radius < 8 || t.Type == MyDetectedEntityType.Missile) IGC.SendBroadcastMessage(CueTag, tup);   // small / fast: tell the other drone types too
}

IMyCameraBlock CamFor(Vector3D pos, double range)
{
    IMyCameraBlock best = null; double bestRange = 0;
    foreach (var c in _cams)
    {
        if (!c.IsFunctional || !c.IsWorking) continue;
        Vector3D d = pos - c.GetPosition();
        double len = d.Length();
        if (len < 1) continue;
        if (Vector3D.Dot(d / len, c.WorldMatrix.Forward) < 0.72) continue; // ~44 deg cone
        if (c.AvailableScanRange < Math.Max(range, len)) continue;   // a ray costs its length in charge; range 0 = "just to the point"
        if (c.AvailableScanRange > bestRange) { best = c; bestRange = c.AvailableScanRange; }
    }
    return best;
}

void ManualLock()
{
    IMyCameraBlock cam = null;
    foreach (var c in _cams)
        if (c.IsWorking && Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) > 0.99 && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    if (cam == null) { _err = "No forward camera for lock"; return; }
    var e = cam.Raycast(Math.Min(LockRange, cam.AvailableScanRange));
    if (e.IsEmpty() || e.Type == MyDetectedEntityType.Planet || e.Type == MyDetectedEntityType.Asteroid) { _err = "Lock: nothing hit (range " + cam.AvailableScanRange.ToString("0") + " m)"; return; }
    if (Friendly(e)) { _err = "Lock: target is friendly"; return; }
    _err = "";
    _tgt = AddContact(e, "LOCK");
}

// ---- LOCK MAINTENANCE AND RE-ACQUISITION -------------------------------------------------------------------
//  Each camera banks only 2 km/s of ray range, and a miss costs as much as a hit, so the rays have to be cheap and
//  well aimed: they stop just behind the predicted point (not 20 m past it), most go to the centre of the target, and
//  the scatter around the point grows with the age of the last sighting. If the lock goes stale (>0.6 s) the target
//  is kept as a "ghost" for GhostTime seconds: the guns stay on its extrapolated position, two rays per tick sweep an
//  ever wider spiral around it (the uncertainty grows with age squared) and the loss is announced to the hive.
double Stickiness = 400;             // score bonus (m-equivalents) for staying on the current target
double GhostTime = 8.0;             // how long a lost target is kept and searched for
double AiStaleTakeover = 3.0;       // lock stale for this long: let the AI blocks hold the enemy (if there are any)
double FireStaleMax = 2.0;    // never fire at a track older than this (extrapolation is a guess by then)
int _missRun = 0, _spiralIdx = 0, _staleRay = 0;
bool Search2nd = true;   // when two cameras can cast in the same tick at a stale lock, the second ray goes to the search ellipse
int SearchTenths = 1, SearchTenthsLate = 3;   // of 10 rays at a stale (> 0.6 s) lock, how many go to the widening search ellipse instead of the predicted point
double _lostFlagUntil = -1, _lastStaleShare = -9;
Vector3D _lostPos, _lostVel; double _lostT = -99, _lostRad = 3;   // last known state of the target that was dropped
void NoteLost(Track t)
{
    if (t.Src == "IGC" || t.DeadAt > 0 || t.DisSince >= 0) return;
    _lostPos = t.Pos; _lostVel = t.Vel; _lostT = t.Seen; _lostRad = t.Radius;
}
double Uncertainty(Track t, double age)
{
    // position error (m): silhouette + what a jinking/accelerating target can still do in 'age' seconds
    return Math.Min(80, t.Radius * 0.35 + 0.5 * Math.Max(t.AccMag, 6.0) * age * age);
}
void MaintainLock()
{
    if (_tgt == null || _cams.Count == 0 || _aiOn) return;   // (AI on: the cameras' charge is for the confirm rays)
    double age = _now - _tgt.Seen;
    if (age < 0.2) return;                                 // fresh enough
    if (_tgt.Src == "TURRET" || _tgt.Src == "CTC" || _tgt.Src == "SENSOR") { if (age < 0.5) return; }
    Vector3D p = _tgt.P(_now);
    Vector3D me = _rc.GetPosition();
    Vector3D dir = p - me; double dist = dir.Length();
    if (dist < 1) return;
    dir /= dist;
    int rays = age > 0.6 ? 2 : 1;                          // stale: spend the charge on finding it again
    for (int k = 0; k < rays; k++)
    {
        bool probingAim = false;
        Vector3D aim;
        // stale: alternate between the predicted point (a ray that misses at the centre is often just a gap in the hull) and
        // the widening search ellipse (the target really changed course)
        // (counted per ray, not per loop pass: the second ray of a tick usually has no camera charge, so the loop index never reached 1)
        bool search = false;
        if (age > 0.6) { _staleRay++; search = (_staleRay % 10) < (age > 1.5 ? SearchTenthsLate : SearchTenths); if (k == 1 && Search2nd) search = true; }   // (two rays in one tick: never both at the same point)
        if (search) aim = ReacquirePoint(_tgt, p, dir, age);
        else aim = ProbePoint(_tgt, p, dir, dist, out probingAim);
        var cam = CamFor(aim, 0);
        if (cam == null) return;
        var e = cam.Raycast(aim);
        if (probingAim && (e.IsEmpty() || e.EntityId != _tgt.Id)) AimMissed(_tgt);
        if (!e.IsEmpty() && e.EntityId == _tgt.Id)
        {
            _missRun = 0;
            if (Valid(e)) AddContact(e, _tgt.Src == "IGC" ? "LOCK" : _tgt.Src); else _tgt.DeadAt = _now;
            return;
        }
        _missRun++;
        if (!e.IsEmpty() && Valid(e)) AddContact(e, "LOCK"); // something else hostile in the way - track it too
    }
}
// Where to cast while the target is stale: a golden-angle spiral in the plane across the line of sight, centred on
// the predicted point; its radius covers the growing position uncertainty. The ray reaches a little past the point.
Vector3D ReacquirePoint(Track t, Vector3D centre, Vector3D dir, double age)
{
    // After a miss the true position is within sigma of the prediction, where sigma grows with the square of the age (a target can
    // only change its velocity so fast: 0.5 a t^2, a >= 6 m/s^2). The error is mostly along the old sideways velocity, so the search
    // is an ellipse with its long axis there (round if the target was nearly head-on), low-discrepancy samples, centred on the prediction.
    double sigma = Uncertainty(t, age);
    Vector3D vl = t.Vel - Vector3D.Dot(t.Vel, dir) * dir;
    double vlm = vl.Length(), major = 1.5 * sigma, minor = vlm > 2 ? 0.6 * sigma : 1.5 * sigma;
    Vector3D u;
    if (vlm > 2) u = vl / vlm;
    else { u = Vector3D.Cross(dir, Math.Abs(dir.Y) < 0.95 ? Vector3D.Up : Vector3D.Right); u.Normalize(); }
    Vector3D v = Vector3D.Cross(dir, u);
    _spiralIdx++;
    double s1 = (_spiralIdx * 0.6180339887) % 1.0 * 2 - 1, s2 = (_spiralIdx * 0.7548776662) % 1.0 * 2 - 1;
    return centre + u * (major * s1) + v * (minor * s2) + dir * Math.Min(t.Radius + 0.5 * sigma, 25);
}

void AutoScanStep()
{
    IMyCameraBlock cam = null;
    foreach (var c in _cams)
        if (c.IsWorking && Forwardish(c) && c.AvailableScanRange >= ScanRange && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    if (cam == null) return;
    _scanIdx++;
    // just lost a target: every other ray goes round where it was last seen, widening with time
    double lostAge = _now - _lostT;
    if (lostAge < 25 && (_scanIdx & 1) == 0)
    {
        Vector3D lp = _lostPos + _lostVel * Math.Min(lostAge, 10);
        double rr = Math.Min(250, _lostRad + 6 * lostAge) * Math.Sqrt(((_scanIdx >> 1) % 23 + 0.5) / 23.0), th2 = _scanIdx * 2.39996323;
        Vector3D dl = lp - cam.GetPosition(); double dd = dl.Length();
        if (dd > 30 && dd < ScanRange)
        {
            dl /= dd;
            Vector3D uu = Vector3D.Cross(dl, Math.Abs(dl.Y) < 0.95 ? Vector3D.Up : Vector3D.Right); uu.Normalize();
            Vector3D vv = Vector3D.Cross(dl, uu);
            var le = cam.Raycast(lp + uu * (rr * Math.Cos(th2)) + vv * (rr * Math.Sin(th2)) + dl * 10);
            if (Valid(le)) AddContact(le, "SCAN");
            return;
        }
    }
    const int N = 120;
    double r = ScanCone * Math.Sqrt((_scanIdx % N + 0.5) / N);
    double th = _scanIdx * 2.39996323; // golden-angle spiral
    var e = cam.Raycast(ScanRange, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
    if (Valid(e)) AddContact(e, "SCAN");
}

void PruneAndSelect()
{
    _drop.Clear();
    foreach (var kv in _contacts)
    {
        // the target I am working on stays alive as a ghost while MaintainLock hunts for it
        double lim = (kv.Value == _tgt && kv.Value.Src != "IGC") ? GhostTime : TrackTimeout;
        if (_now - kv.Value.Seen > lim) _drop.Add(kv.Key);
    }
    foreach (var id in _drop)
    {
        Track gone; if (_contacts.TryGetValue(id, out gone) && gone == _tgt) NoteLost(gone);
        _contacts.Remove(id);
    }
    if (_tgt != null && !_contacts.ContainsKey(_tgt.Id)) _tgt = null;
    PruneMates();
    AssessTargets();

    if (_mode == Mode.IDLE || _mode == Mode.RTB) return;
    if (_tgt != null && _tgt.Src == "LOCK" && _mode == Mode.ASSIST) return; // pilot's choice sticks

    Vector3D me = _rc.GetPosition();
    Vector3D fwd = _rc.WorldMatrix.Forward;
    Vector3D myVel = _rc.GetShipVelocities().LinearVelocity;
    Vector3D home = Home();
    Track best = null; double bestScore = double.MaxValue;
    foreach (var t in _contacts.Values)
    {
        Vector3D p = t.P(_now);
        if (_mode == Mode.GUARD && Vector3D.Distance(p, home) > GuardRadius) continue;
        Vector3D d = p - me; double dist = d.Length();
        double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(d / Math.Max(dist, 1), fwd), -1, 1)) * 57.2958;
        double closing = Vector3D.Dot(t.V(_now) - myVel, -d / Math.Max(dist, 1));
        double score = dist + ang * 6 - closing * 3;
        if (t.Type == MyDetectedEntityType.SmallGrid) score -= SmallBonus;       // interceptor: drones and fighters first
        else if (t.Type == MyDetectedEntityType.LargeGrid) score += LargePenalty;
        if (t.Type == MyDetectedEntityType.Missile && closing > 20) score -= MissileBonus;
        else if (t.Radius < 10 && closing > 40) score -= FastCloser;     // something small and fast coming at us
        if (t == _tgt) score -= Stickiness;                            // stickiness
        score += Math.Max(0, (_now - t.Seen) - 1.0) * 150;             // stale tracks lose to fresh sightings
        if (dist < 150) score -= 350;                                  // something right on top of us comes first
        score += HiveScore(t);
        if (score < bestScore) { bestScore = score; best = t; }
    }
    _tgt = best;
}

double StandoffFor(Track t) { return t.Radius < 8 ? Math.Max(MinRange * 1.5, Standoff * SmallStandoffFrac) : Standoff; }

Vector3D Home()
{
    if (_mode != Mode.RTB && _tgt == null && _now - _assistT < 4) return _assist;   // go help a hive member under fire
    return (_now - _dynHomeT < 5) ? _dynHome : _home;
}

// =====================================================================
//  POWER / HEALTH
double _batFrac = 1, _ammoFrac = 1; bool _hadAmmo = false;
void PowerAndHealth()
{
    double cur = 0, max = 0, stored = 0, cap = 0;
    foreach (var b in _bats)
    {
        if (!b.IsWorking) continue;
        cur += b.CurrentOutput; max += b.MaxOutput; stored += b.CurrentStoredPower; cap += b.MaxStoredPower;
    }
    _batFrac = cap > 0 ? stored / cap : 1;
    if (max > 0)
    {
        double load = cur / max;
        if (load > 0.97) _powerScale = Math.Max(0.15, _powerScale * 0.9);
        else if (load < 0.88) _powerScale = Math.Min(1, _powerScale + 0.03);
    }
    else _powerScale = 1;

    double vol = 0, vmax = 0; int working = 0;
    foreach (var g in _guns)
    {
        if (!g.IsFunctional) continue;
        working++;
        var inv = g.GetInventory(0);
        if (inv == null) continue;
        vol += inv.CurrentVolume.RawValue; vmax += inv.MaxVolume.RawValue;
    }
    _ammoFrac = vmax > 0 ? vol / vmax : 0;
    if (vol > 0) _hadAmmo = true;

    if (_mode != Mode.IDLE && _mode != Mode.RTB && _mode != Mode.ASSIST)
    {
        if (cap > 0 && _batFrac < RtbBattery) { _err = "Low battery - RTB"; _mode = Mode.RTB; }
        else if (_guns.Count > 0 && (working == 0 || (_hadAmmo && vol <= 0))) { _err = "No guns/ammo - RTB"; _mode = Mode.RTB; }
    }
}

// =====================================================================
//  CONTROL
void Control(double dt)
{
    if (LaunchStep(dt, _mode == Mode.IDLE, StartMode, _dynHomeT > 0, _dynHome, Command)) return;   // automatic take-off after the release from a printer (LAUNCH block)
    var vels = _rc.GetShipVelocities();
    Vector3D myVel = vels.LinearVelocity;
    Vector3D grav = _rc.GetNaturalGravity();
    Vector3D me = _rc.GetPosition();
    bool shoot = false;
    _aimErrDeg = 0; _fireWinDeg = 0;

    if (_mode == Mode.IDLE) { SetFire(false); return; }

    // ---------- aiming ----------
    Vector3D aimDir = Vector3D.Zero, aimPoint = Vector3D.Zero;
    bool haveAim = false;
    if (_tgt != null && _mode != Mode.RTB)   // (no guns = scout: still tracks with the nose cameras)
    {
        Vector3D gunPos = Vector3D.Zero; int n = 0;
        foreach (var g in _guns) if (g.IsFunctional) { gunPos += g.GetPosition(); n++; }
        gunPos = n > 0 ? gunPos / n : me;
        haveAim = Lead(_tgt, gunPos, myVel, out aimPoint, out aimDir);
        if (haveAim)
        {
            Vector3D losRate = Vector3D.Zero;
            if (_havePrev) losRate = Vector3D.Cross(_prevAim, aimDir) / dt;
            if (losRate.LengthSquared() > 4) losRate = Vector3D.Normalize(losRate) * 2;
            _prevAim = aimDir; _havePrev = true;
            Vector3D up = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : Vector3D.Zero;
            Aim(aimDir, up, losRate, dt);

            Vector3D gunFwd = _aimRef.WorldMatrix.Forward;
            double dist = Vector3D.Distance(aimPoint, gunPos);
            _aimErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(gunFwd, aimDir), -1, 1)) * 57.2958;
            double aimR = AimActive(_tgt) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6;   // aiming at a hull point needs a tighter window
            _fireWinDeg = Math.Max(0.3, Math.Atan(aimR / Math.Max(dist, 1)) * 57.2958) * FireTol;
            shoot = dist < MaxFireRange && _aimErrDeg < _fireWinDeg && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax;
            if (shoot && FriendCheck) { MatesInLine(gunPos, gunFwd, dist); if (_tick % 20 == 0) CheckLineOfFire(gunPos, gunFwd, dist); }
            if (_now < _holdFireUntil) shoot = false;
        }
    }
    else _havePrev = false;
    if (haveAim) _sweepUpSet = false;

    if (!haveAim)
    {
        if (_mode == Mode.ASSIST) { ReleaseGyros(); }
        else
        {
            Vector3D up = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : Vector3D.Zero;
            Vector3D fwd = _rc.WorldMatrix.Forward;
            Vector3D toHome = Home() - me;
            if (_mode == Mode.RTB && toHome.LengthSquared() > 400) fwd = Vector3D.Normalize(toHome);
            else if (myVel.LengthSquared() > 100 && _mode != Mode.GUARD) fwd = Vector3D.Normalize(myVel);
            else
            {
                // radar sweep: yaw round and nod up/down so the camera cone covers all around
                if (AutoScan) fwd = SweepDir(up);
            }
            Aim(fwd, up, Vector3D.Zero, dt);
        }
    }
    SetFire(shoot);

    // ---------- translation ----------
    if (_mode == Mode.ASSIST) return;
    double mass = _rc.CalculateShipMass().PhysicalMass;
    double aBrake = MinAccel(mass, grav) * 0.55;
    Vector3D want;
    Vector3D home = Home();
    if (_mode == Mode.RTB || _tgt == null)
    {
        Vector3D d = home - me; double dist = d.Length();
        double v = Math.Min(Math.Min(CruiseSpeed, dist * 0.8), Math.Sqrt(2 * aBrake * dist));
        want = (dist > 0.5 ? d / dist * v : Vector3D.Zero) + Evade() + Separation(me);
        if (_patrolLeg && _mode == Mode.HUNT && _tgt == null)   // search patrol: cruise towards the current waypoint
        {
            Vector3D pd = _patrolWp - me; double pl = pd.Length();
            want = (pl > 1 ? pd / pl * PatrolSpeed : Vector3D.Zero) + Evade() + Separation(me);
        }
        if (_mode == Mode.RTB && dist < 15 && myVel.Length() < 2) { SetMode(Mode.IDLE); _err = "Home - idle"; return; }
    }
    else
    {
        Vector3D tp = _tgt.P(_now), tv = _tgt.V(_now);
        Vector3D d = tp - me; double dist = d.Length();
        Vector3D dir = dist > 1 ? d / dist : _rc.WorldMatrix.Forward;
        double e = dist - StandoffFor(_tgt);
        double vr = Math.Sign(e) * Math.Min(Math.Min(Math.Abs(e) * 0.6, MaxSpeed), Math.Sqrt(2 * aBrake * Math.Abs(e)));
        if (dist < MinRange) vr = -MaxSpeed * 0.6;
        Vector3D upRef = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : _rc.WorldMatrix.Up;
        Vector3D tan = Vector3D.Cross(dir, upRef);
        if (tan.LengthSquared() < 1e-4) tan = _rc.WorldMatrix.Right;
        tan.Normalize();
        if (_now > _nextJink)
        {
            _nextJink = _now + JinkEvery + _rng.NextDouble() * JinkEveryRand;
            if (_rng.NextDouble() < 0.25) _orbitSign = -_orbitSign;
            Vector3D rnd = new Vector3D(_rng.NextDouble() - 0.5, _rng.NextDouble() - 0.5, _rng.NextDouble() - 0.5);
            rnd -= Vector3D.Dot(rnd, dir) * dir;
            _jink = rnd.LengthSquared() > 1e-6 ? Vector3D.Normalize(rnd) * JinkSpeed : Vector3D.Zero;
        }
        double orbit = dist < MaxFireRange * 1.2 ? OrbitSpeed : 0;
        want = tv + dir * vr + tan * orbit * _orbitSign + (Jink ? _jink : Vector3D.Zero) + Evade() + Separation(me);
        if (_mode == Mode.GUARD && Vector3D.Distance(me, home) > GuardRadius * 1.3) want = Vector3D.Normalize(home - me) * CruiseSpeed;
    }
    double cap = (_tgt != null && _mode != Mode.RTB) ? MaxSpeed : CruiseSpeed;
    if (want.LengthSquared() > cap * cap) want = Vector3D.Normalize(want) * cap;

    Vector3D acc = (want - myVel) * 2.0;
    if (acc.LengthSquared() > 2500) acc = Vector3D.Normalize(acc) * 50;
    ApplyThrust((acc - grav) * mass);
}

bool Lead(Track t, Vector3D shooter, Vector3D shooterVel, out Vector3D point, out Vector3D dir)
{
    Vector3D tp = t.P(_now) + AimOffset(t), tv = t.V(_now), ta = UseTargetAccel ? t.EA() : Vector3D.Zero;
    Vector3D rp = tp - shooter;
    Vector3D rv = InheritVel ? tv - shooterVel : tv;
    double s = ProjSpeed;
    // exact solution for constant velocity, then refine with acceleration
    double a = rv.LengthSquared() - s * s, b = 2 * Vector3D.Dot(rp, rv), c = rp.LengthSquared();
    double tt;
    if (Math.Abs(a) < 1e-6) tt = b != 0 ? -c / b : 0;
    else
    {
        double disc = b * b - 4 * a * c;
        if (disc < 0) { point = tp; dir = Vector3D.Normalize(rp); _leadT = 0; return rp.LengthSquared() > 1; }
        double sq = Math.Sqrt(disc);
        double t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a);
        tt = (t1 > 0 && (t1 < t2 || t2 <= 0)) ? t1 : t2;
    }
    if (tt <= 0 || double.IsNaN(tt)) tt = rp.Length() / s;
    Vector3D p = rp;
    for (int i = 0; i < 4; i++)
    {
        p = rp + (rv * tt + 0.5 * ta * tt * tt) * LeadScale;
        tt = p.Length() / s;
    }
    _leadT = tt;
    point = shooter + p;
    double len = p.Length();
    if (len < 1) { dir = Vector3D.Zero; return false; }
    dir = p / len;
    return true;
}

// Wingmates repel each other: all of them fly the same orbit at the same speed and would otherwise end up in contact.
// Uses the positions the hive already broadcasts (_SWARM); costs a few vector operations per mate per tick.
Vector3D Separation(Vector3D me)
{
    Vector3D push = Vector3D.Zero;
    foreach (var m in _mates.Values)
    {
        double age = _now - m.Seen;
        if (age > 1.5) continue;
        Vector3D d = me - (m.Pos + m.Vel * age);
        double len = d.Length();
        if (len < SepRange && len > 0.5) push += d / len * (SepSpeed * (1 - len / SepRange));
    }
    return push;
}

// Friendly-fire gate from the IGC positions: hold fire while a hive mate stands within MateClear metres of the line
// from the guns to the target (bullets fly 0.5-1 s, so the mate is extrapolated by its age only). Works with no camera
// charge, unlike the camera ray in CheckLineOfFire, which can only look once per second and fails open.
const double MateClear = 14;
void MatesInLine(Vector3D gunPos, Vector3D gunFwd, double dist)
{
    foreach (var m in _mates.Values)
    {
        double age = _now - m.Seen;
        if (age > 1.0) continue;
        Vector3D rel = m.Pos + m.Vel * age - gunPos;
        double along = Vector3D.Dot(rel, gunFwd);
        if (along < 5 || along > dist + 10) continue;
        if ((rel - gunFwd * along).LengthSquared() < MateClear * MateClear) { _holdFireUntil = _now + 0.4; _err = "Holding fire: wingmate in line"; return; }
    }
}

void CheckLineOfFire(Vector3D gunPos, Vector3D gunFwd, double dist)
{
    Vector3D probe = gunPos + gunFwd * Math.Min(dist, MaxFireRange);
    var cam = CamFor(probe, dist + 5);
    if (cam == null) return;
    var e = cam.Raycast(probe);
    if (!e.IsEmpty() && e.EntityId != _tgt.Id && Friendly(e) && e.EntityId != Me.CubeGrid.EntityId)
    { _holdFireUntil = _now + 1.0; _err = "Holding fire: friendly in line"; }
}

void Aim(Vector3D desiredFwd, Vector3D desiredUp, Vector3D ffWorld, double dt)
{
    if (_gyros.Count == 0 || desiredFwd.LengthSquared() < 1e-9) return;
    MatrixD refM = _aimRef.WorldMatrix;
    MatrixD inv = MatrixD.Transpose(refM);
    Vector3D d = Vector3D.Normalize(Vector3D.TransformNormal(desiredFwd, inv));
    Vector3D f = Vector3D.Forward;
    double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(f, d), -1, 1));
    Vector3D axis = Vector3D.Cross(f, d);
    if (axis.LengthSquared() < 1e-12) axis = ang > 1 ? Vector3D.Up : Vector3D.Zero; else axis.Normalize();
    Vector3D err = axis * ang;

    if (desiredUp.LengthSquared() > 0.5 && ang < 0.6)
    {
        Vector3D u = Vector3D.TransformNormal(desiredUp, inv);
        u -= Vector3D.Dot(u, f) * f;
        if (u.LengthSquared() > 1e-4)
        {
            u.Normalize();
            double roll = Math.Atan2(Vector3D.Dot(Vector3D.Cross(Vector3D.Up, u), f), Vector3D.Dot(Vector3D.Up, u));
            err += f * (roll * 0.5); // rotate about the forward axis
        }
    }

    Vector3D derr = (err - _prevErr) / dt;
    if (derr.LengthSquared() > 400) derr = Vector3D.Zero; // spike after mode change
    _prevErr = err;
    Vector3D w = err * AimKp + derr * AimKd;
    Vector3D wWorld = Vector3D.TransformNormal(w, refM) + ffWorld;
    if (wWorld.LengthSquared() > MaxTurn * MaxTurn) wWorld = Vector3D.Normalize(wWorld) * MaxTurn;

    foreach (var g in _gyros)
    {
        if (!g.IsFunctional) continue;
        Vector3D l = Vector3D.TransformNormal(wWorld, MatrixD.Transpose(g.WorldMatrix));
        // SE gyro overrides use the opposite sign to a right-handed angular velocity
        g.Pitch = -(float)l.X;
        g.Yaw = -(float)l.Y;
        g.Roll = -(float)l.Z;
        g.GyroOverride = true;
    }
}

double MinAccel(double mass, Vector3D grav)
{
    double min = double.MaxValue;
    for (int i = 0; i < 6; i++)
    {
        double f = 0;
        foreach (var t in _thrDir[i]) if (t.IsWorking) f += t.MaxEffectiveThrust;
        if (f > 0) min = Math.Min(min, f);
    }
    if (min == double.MaxValue || mass <= 0) return 1;
    return Math.Max(0.5, min * _powerScale / mass - grav.Length());
}

void ApplyThrust(Vector3D forceWorld)
{
    _rc.DampenersOverride = false;
    MatrixD m = _rc.WorldMatrix;
    // components along Forward, Right, Up of the controller
    double[] comp = {
        Vector3D.Dot(forceWorld, m.Forward), -Vector3D.Dot(forceWorld, m.Forward),
        -Vector3D.Dot(forceWorld, m.Right), Vector3D.Dot(forceWorld, m.Right),
        Vector3D.Dot(forceWorld, m.Up), -Vector3D.Dot(forceWorld, m.Up) };
    for (int i = 0; i < 6; i++)
    {
        double max = 0;
        foreach (var t in _thrDir[i]) if (t.IsWorking) max += t.MaxEffectiveThrust;
        float pct = (comp[i] > 0 && max > 0) ? (float)MathHelper.Clamp(comp[i] / max * _powerScale, 0, 1) : 0f;
        if (pct > 0 && pct < 0.002f) pct = 0.002f;
        foreach (var t in _thrDir[i]) t.ThrustOverridePercentage = pct;
    }
}

void SetFire(bool on)
{
    // Re-send "on" twice a second: AI blocks (or anything else) may have switched the guns off behind my back when they
    // handed control over, and a script that only sends changes would then believe it is still firing.
    if (on == _firing && !(on && _tick % 30 == 0)) return;
    _firing = on;
    foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off");
}

void ReleaseGyros() { foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = g.Yaw = g.Roll = 0; } _havePrev = false; }

void Release()
{
    ReleaseGyros();
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
    SetFire(false);
}

// =====================================================================
//  DISPLAY
void Display()
{
    _sb.Clear();
    _sb.Append("HARRIER DRONE AI v").Append(VER).Append('\n');
    _sb.Append("Mode   : ").Append(_mode).Append('\n');
    if (_tgt != null)
    {
        double dist = _rc != null ? Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()) : 0;
        _sb.Append("Target : ").Append(_tgt.Type == MyDetectedEntityType.SmallGrid ? "SMALL " : _tgt.Type == MyDetectedEntityType.LargeGrid ? "LARGE " : "").Append(_tgt.Src).Append('\n');
        double mySpd = _rc != null ? _rc.GetShipVelocities().LinearVelocity.Length() : 0;
        _sb.Append("Range  : ").Append(dist.ToString("0")).Append(" m  tgt ").Append(_tgt.V(_now).Length().ToString("0")).Append(" me ").Append(mySpd.ToString("0")).Append(" m/s\n");
        _sb.Append("Lead   : ").Append(_leadT.ToString("0.00")).Append(" s  acc ").Append(_tgt.EA().Length().ToString("0.0")).Append("/").Append(_tgt.AccMag.ToString("0.0")).Append(" x").Append(LeadScale.ToString("0.00")).Append('\n');
        _sb.Append("Aim err: ").Append(_aimErrDeg.ToString("0.00")).Append(" / ").Append(_fireWinDeg.ToString("0.00")).Append(" deg").Append(_firing ? "  FIRING" : "").Append('\n');
    }
    else _sb.Append("Target : none (").Append(_contacts.Count).Append(" contacts)\n\n\n\n");
    _sb.Append("Battery: ").Append((_batFrac * 100).ToString("0")).Append("%  Ammo ").Append((_ammoFrac * 100).ToString("0")).Append("%\n");
    _sb.Append("Power  : ").Append((_powerScale * 100).ToString("0")).Append("% thrust cap\n");
    double scan = 0; foreach (var c in _cams) scan += c.AvailableScanRange;
    _sb.Append("Cams ").Append(_cams.Count).Append(" scan ").Append((scan / 1000).ToString("0.0")).Append("km  Gyro ").Append(_gyros.Count).Append(" Thr ").Append(_thr.Count).Append('\n');
    _sb.Append("Desig  : ").Append(_turrets.Count + _ctcs.Count).Append("  Sens ").Append(_sensors.Count).Append("  Guns ").Append(_guns.Count).Append('\n');
    _sb.Append("Run    : ").Append(_avgMs.ToString("0.000")).Append(" ms\n");
    _sb.Append("Ins    : ").Append(Runtime.CurrentInstructionCount).Append(" / ").Append(Runtime.MaxInstructionCount).Append("  peak ").Append(_peakIns).Append('\n');
    if (_sensors.Count > 0 && _sensorSetupMissing > 0) _sb.Append("! Sensor: set ranges to max + Detect Enemy\n");
    _sb.Append(HiveStatus());
    _sb.Append(LaunchStatus());
    if (Me.OwnerId == 0) _sb.Append("! PB has no owner: set ownership\n");
    if (_err != "") _sb.Append("! ").Append(_err).Append('\n');
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}

// =====================================================================
//  SENSORS (360 deg, ~50 m) + idle sweep
List<IMySensorBlock> _sensors = new List<IMySensorBlock>();
List<MyDetectedEntityInfo> _sensed = new List<MyDetectedEntityInfo>();
Vector3D _sweepUp; bool _sweepUpSet = false;

int _sensorSetupMissing = 0;

// Sets the sensor up through its terminal settings by name. Any setting the game doesn't recognise is
// skipped (and reported on screen) instead of breaking the script.
void ConfigureSensors()
{
    _sensorSetupMissing = 0;
    foreach (var s in _sensors)
    {
        s.Enabled = true;
        SetF(s, 50f, "LeftExtend", "Left"); SetF(s, 50f, "RightExtend", "Right");
        SetF(s, 50f, "TopExtend", "Top"); SetF(s, 50f, "BottomExtend", "Bottom");
        SetF(s, 50f, "FrontExtend", "Front"); SetF(s, 50f, "BackExtend", "Back");
        SetB(s, true, "Detect Enemy", "DetectEnemy");
        SetB(s, TargetNeutral, "Detect Neutral", "DetectNeutral");
        SetB(s, false, "Detect Owner", "DetectOwner");
        SetB(s, false, "Detect Friendly", "DetectFriendly");
        SetB(s, TargetSmall, "Detect Small Ships", "DetectSmallShips");
        SetB(s, TargetLarge, "Detect Large Ships", "DetectLargeShips");
        SetB(s, TargetLarge, "Detect Stations", "DetectStations");
        SetB(s, TargetChars, "Detect Players", "DetectPlayers");
        SetB(s, false, "Detect Asteroids", "DetectAsteroids");
        SetB(s, false, "Detect Floating Objects", "DetectFloatingObjects");
        SetB(s, false, "Detect Subgrids", "DetectSubgrids");
    }
}
void SetF(IMyTerminalBlock b, float v, string n1, string n2)
{
    if (b.GetProperty(n1) != null) b.SetValueFloat(n1, v);
    else if (b.GetProperty(n2) != null) b.SetValueFloat(n2, v);
    else _sensorSetupMissing++;
}
void SetB(IMyTerminalBlock b, bool v, string n1, string n2)
{
    if (b.GetProperty(n1) != null) b.SetValueBool(n1, v);
    else if (b.GetProperty(n2) != null) b.SetValueBool(n2, v);
    else _sensorSetupMissing++;
}

void PollSensors()
{
    foreach (var s in _sensors)
    {
        if (!s.IsWorking) continue;
        _sensed.Clear();
        s.DetectedEntities(_sensed);
        foreach (var e in _sensed) if (Valid(e)) AddContact(e, "SENSOR");
    }
}

// Idle search attitude: keep yawing round while nodding the nose up and down (+/-34 deg, 16 s period),
// so the camera scan cone covers above and below as well as the horizon.
Vector3D SweepDir(Vector3D gravUp)
{
    if (!_sweepUpSet) { _sweepUp = _rc.WorldMatrix.Up; _sweepUpSet = true; }
    Vector3D upRef = gravUp.LengthSquared() > 0.5 ? gravUp : _sweepUp;
    Vector3D h = _rc.WorldMatrix.Forward;
    h -= Vector3D.Dot(h, upRef) * upRef;
    if (h.LengthSquared() < 1e-4) h = Vector3D.Cross(upRef, _rc.WorldMatrix.Right);
    h.Normalize();
    h = Vector3D.Transform(h, MatrixD.CreateFromAxisAngle(upRef, -0.035));
    double pitch = 0.6 * Math.Sin(_now * 0.4);
    return Vector3D.Normalize(h * Math.Cos(pitch) + upRef * Math.Sin(pitch));
}

// =====================================================================
//  CLOSE-RANGE ALL-AROUND SCAN
//  Cameras that don't face forward fire short rays in a spiral over their
//  whole view cone, as fast as their charge allows, so enemies above,
//  below, beside or behind are found without the drone turning to look.
int _omniCam = 0, _omniIdx = 0;
bool Forwardish(IMyCameraBlock c) { return Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) > 0.9; }
void OmniScan()
{
    if (!CloseScan || _cams.Count == 0) return;
    for (int shots = 0, tries = 0; shots < 3 && tries < _cams.Count; tries++)
    {
        var c = _cams[_omniCam++ % _cams.Count];
        bool hard = UnderFire || Alerted;
        double rng = hard ? CloseScanRange * 4 : CloseScanRange;
        if (!c.IsWorking || (Forwardish(c) && !(hard && _tgt == null && !_aiOn)) || c.AvailableScanRange < rng * 2) continue; // keep charge for tracking
        _omniIdx++;
        double r = 40 * Math.Sqrt((_omniIdx % 89 + 0.5) / 89.0), th = _omniIdx * 2.39996323;
        var e = c.Raycast(rng, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
        if (Valid(e)) AddContact(e, "SCAN");
        shots++;
    }
}

// =====================================================================
//  v1.8: TARGET STATUS, DAMAGE SENSING, SPLIT FOCUS, ASSIST
Dictionary<long, double> _ignore = new Dictionary<long, double>();
Dictionary<long, int> _share = new Dictionary<long, int>(), _count = new Dictionary<long, int>();
List<Track> _live = new List<Track>();
List<IMySlimBlock> _slims = new List<IMySlimBlock>();
double _integ = -1, _hitAt = -99; int _hits = 0;
Vector3D _assist; double _assistT = -99;
Vector3D _evade; double _evadeNext = 0; int _evadeHits = 0; Random _evRng = new Random();
string _crash = "";
bool UnderFire { get { return _now - _hitAt < 6; } }
bool Ignored(long id) { double u; return _ignore.TryGetValue(id, out u) && u > _now; }

void IgnoreTarget(Track t, double secs, bool tell)
{
    _ignore[t.Id] = _now + secs; _contacts.Remove(t.Id);
    if (_tgt == t) _tgt = null;
    // radius -1 = "this one is finished" for every drone (and stays compatible with the carrier link)
    if (tell) { var fin = new MyTuple<long, Vector3D, Vector3D, double, long>(t.Id, t.Pos, t.Vel, -1, Me.EntityId); IGC.SendBroadcastMessage(Tag + "_TGT", fin); FleetSend(fin); }
}

// Finished = no longer an enemy (its owned blocks are gone), blown down to under 45% of the
// largest size seen, or tumbling out of control for 4 s.
bool Disabled(Track t)
{
    if (t.DeadAt > 0) return true;
    if (t.Spin > 0.8) { if (t.SpinSince < 0) t.SpinSince = _now; } else t.SpinSince = -1;
    return (t.SpinSince > 0 && _now - t.SpinSince > 4) || (t.Rad0 > 3 && t.Radius < 0.45 * t.Rad0) || (t.Type != MyDetectedEntityType.Missile && ((t.RawMax > 1.6 && t.Raw < 0.4 * t.RawMax) || t.Raw < 0.75));   // (last two: a small ship reduced to a block or two; or a lone block / debris, which splits off as its own grid and would otherwise hold the guns forever; missiles are small by nature and exempt)
}

// Runs before target selection: drops finished targets, then works out how many hive members
// each live target should get (bigger ships get more) and how many are on each already.
void AssessTargets()
{
    _drop.Clear(); _live.Clear();
    foreach (var t in _contacts.Values)
    {
        if (Disabled(t)) { if (t.DisSince < 0) t.DisSince = _now; if (_now - t.DisSince > DisabledGrace) _drop.Add(t.Id); }
        else { t.DisSince = -1; _live.Add(t); }
    }
    foreach (var id in _drop) { Track t; if (_contacts.TryGetValue(id, out t)) IgnoreTarget(t, 120, true); }
    _share.Clear(); _count.Clear();
    foreach (var m in _mates.Values) if (m.Tgt != 0) { int c; _count.TryGetValue(m.Tgt, out c); _count[m.Tgt] = c + 1; }
    double sw = 0; foreach (var t in _live) sw += Math.Sqrt(MathHelper.Clamp(t.Radius, 3, 80));
    int n = _mates.Count + 1;
    foreach (var t in _live) _share[t.Id] = Math.Max(1, (int)Math.Round(n * Math.Sqrt(MathHelper.Clamp(t.Radius, 3, 80)) / Math.Max(sw, 1)));
    // help a hive member that's taking hits if I have nothing better to do
    double best = 2000; Vector3D me = _rc.GetPosition();
    foreach (var m in _mates.Values)
        if ((m.Flags & 1) != 0 && _now - m.Seen < 3) { double d = Vector3D.Distance(m.Pos, me); if (d < best) { best = d; _assist = m.Pos; _assistT = _now; } }
}

// Score adjustment (lower = better). SPLIT spreads the hive across live targets; FOCUS piles on.
double HiveScore(Track t)
{
    if (t.DisSince >= 0) return 5000;     // finished targets only if nothing else is around
    int cnt; _count.TryGetValue(t.Id, out cnt);
    if (!SplitFocus) return -250 * cnt;
    int sh; if (!_share.TryGetValue(t.Id, out sh)) sh = 1;
    if (t == _tgt)
    {   // keep it if I'm among the first <share> members on it (by id); otherwise make room
        int rank = 0; long me = Me.CubeGrid.EntityId;
        foreach (var m in _mates.Values) if (m.Tgt == t.Id && m.Id < me) rank++;
        return rank < sh ? -300 : 900;
    }
    return cnt >= sh ? 900 : -250 * (sh - cnt);
}

// Every block's integrity; a drop means this drone is being hit.
// Incremental: walking the whole bounding box in one tick blows the PB instruction limit once the drone is
// merged into a printer (one huge grid). Starts a pass; StepSlimScan() does SlimCellsPerTick cells per tick.
List<IMySlimBlock> _slimsNew = new List<IMySlimBlock>(); HashSet<Vector3I> _slimSeen = new HashSet<Vector3I>();
IMyCubeGrid _slimsGrid, _scanGrid; bool _slimBusy = false; int _sx, _sy, _sz, _mnx, _mny, _mnz, _mxx, _mxy, _mxz;
const int SlimCellsPerTick = 200, SlimMaxCells = 30000;
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
    for (int n = 0; n < SlimCellsPerTick; n++)
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
    double sum = 0; var g = Me.CubeGrid;
    foreach (var s in _slims) if (g.CubeExists(s.Position)) sum += s.BuildIntegrity - s.CurrentDamage;
    if (_integ >= 0 && sum < _integ - 0.5) { _hitAt = _now; _hits++; }
    _integ = sum;
}

// Random evasive velocity while under fire (new direction every 1-2.5 s and on every new hit).
Vector3D Evade()
{
    if (!UnderFire) return Vector3D.Zero;
    if (_now > _evadeNext || _hits != _evadeHits)
    {
        _evadeHits = _hits; _evadeNext = _now + 1 + _evRng.NextDouble() * 1.5;
        var r = new Vector3D(_evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5);
        _evade = r.LengthSquared() > 1e-6 ? Vector3D.Normalize(r) * EvadeSpeed : Vector3D.Zero;
    }
    return _evade;
}

string HiveStatus()
{
    string s = "Hive: " + (_mates.Count + 1) + (SplitFocus ? " SPLIT" : " FOCUS");
    if (UnderFire) s += "  !! UNDER FIRE (" + _hits + ")";
    if (_tgt != null && _tgt.DisSince >= 0) s += "  tgt disabled";
    if (_tgt != null) s += "\nAim: " + AimLabel(_tgt) + " (" + _tgt.Hull.Count + " hull pts)";
    return s + "\n" + AiStatus() + (_crash != "" ? "! Script error: " + _crash + "\n" : "");
}

class Mate { public long Id; public Vector3D Pos, Vel; public double Seen; public long Tgt; public int Flags; }
Dictionary<long, Mate> _mates = new Dictionary<long, Mate>();
IMyBroadcastListener _lSwarm;
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
        if ((d.Item5 & 4) != 0 && ShareTargets && _tgt != null && _tgt.Id == d.Item4 && _tgt.Src != "IGC" && _now - _tgt.Seen < 0.4 && _now - _lastStaleShare > 0.3)
        { _lastStaleShare = _now; ShareTarget(); }
    }
}
void BroadcastStatus()
{
    IGC.SendBroadcastMessage(Tag + "_SWARM", new MyTuple<long, Vector3D, Vector3D, long, int>(
        Me.CubeGrid.EntityId, _rc.GetPosition(), _rc.GetShipVelocities().LinearVelocity, _tgt != null ? _tgt.Id : 0, (UnderFire ? 1 : 0) | (OwnAlert ? 2 : 0) | (LockStale ? 4 : 0)));
}
void PruneMates() { _drop.Clear(); foreach (var kv in _mates) if (_now - kv.Value.Seen > 3) _drop.Add(kv.Key); foreach (var id in _drop) _mates.Remove(id); }

// =====================================================================
//  v1.8: AIM POINTS
//  Scripts can't see which blocks an enemy has. Instead, every camera ray that hits a target is
//  kept as a point on its hull, in the target's own frame, so the drone aims at real hull rather
//  than the middle of its bounding box (often empty space on big ships).
//   ENGINES  rear-most known hull, judged from which way the target accelerates
//   FACING   the hull nearest us (strips armour and weapons that can see us)
//   SPREAD   rotates through the hull every few seconds; each drone picks different points
//   CENTER   old behaviour      AUTO  ENGINES once it has seen the target thrust, else SPREAD
string AimMode = "AUTO";
List<int> _aimIdx = new List<int>();
int _probeN = 0;
bool AimActive(Track t) { return AimMode != "CENTER" && t.Hull.Count >= 3; }
string AimLabel(Track t)
{
    if (!AimActive(t)) return "CENTER";
    return AimMode == "AUTO" ? (t.Thrust.LengthSquared() > 0.09 ? "ENGINES" : "SPREAD") : AimMode;
}
Vector3D AimOffset(Track t)
{
    if (!AimActive(t)) return Vector3D.Zero;
    if (_now - t.AimT > 0.5)
    {
        string mode = AimLabel(t);
        int n = t.Hull.Count, slot = (int)Math.Abs(Me.CubeGrid.EntityId % 9973);
        int step = slot + (int)(_now / 4);
        if (mode == "SPREAD") t.AimLocal = t.Hull[step % n];
        else
        {
            Vector3D axis = mode == "ENGINES" ? -t.Thrust
                : Vector3D.TransformNormal(_rc.GetPosition() - t.P(_now), MatrixD.Transpose(t.O));
            if (axis.LengthSquared() < 1e-6) axis = Vector3D.Forward;
            axis.Normalize();
            _aimIdx.Clear(); for (int i = 0; i < n; i++) _aimIdx.Add(i);
            _aimIdx.Sort((a, b) => Vector3D.Dot(t.Hull[b], axis).CompareTo(Vector3D.Dot(t.Hull[a], axis)));
            t.AimLocal = t.Hull[_aimIdx[step % Math.Max(1, n / 4)]];   // one of the best quarter
        }
        t.AimT = _now;
    }
    return Vector3D.TransformNormal(t.AimLocal, t.O);
}
void StoreHull(Track t, MyDetectedEntityInfo e)
{
    if (!e.HitPosition.HasValue || !(e.Type == MyDetectedEntityType.LargeGrid || e.Type == MyDetectedEntityType.SmallGrid)) return;
    Vector3D l = Vector3D.TransformNormal(e.HitPosition.Value - e.Position, MatrixD.Transpose(e.Orientation));
    foreach (var h in t.Hull) if ((h - l).LengthSquared() < 4) return;
    if (t.Hull.Count < 48) t.Hull.Add(l); else t.Hull[t.HullNext++ % 48] = l;
}
// Lock-refresh rays alternate between the target's centre (with a little scatter, to map more hull)
// and the current aim point. If a ray at the aim point no longer hits, that part is gone: forget it.
Vector3D ProbePoint(Track t, Vector3D centre, Vector3D dir, double dist, out bool probingAim)
{
    probingAim = false;
    int k = ++_probeN;
    if ((k & 3) == 0 && AimActive(t) && _now - t.Seen < 0.5) { probingAim = true; return centre + AimOffset(t) + dir * 1.5; }
    // A ray at the bounding-box centre often passes between the blocks of a small ship (about half of them miss).
    // Hull points kept from earlier hits are real blocks in the target's own frame: aim at one of those instead
    // (2 rays in 3), with a little scatter, and let it run 2 m past so a slightly late ray still clips the hull.
    if (t.Hull.Count >= 3 && (k % 3) != 0)
    {
        Vector3D h = Vector3D.TransformNormal(t.Hull[_evRng.Next(t.Hull.Count)], t.O);
        Vector3D r = new Vector3D(_evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5);
        return centre + h + r * 0.8 + dir * 2;
    }
    if (_now - t.Seen < 0.6 && t.Hull.Count < 48)
    {
        Vector3D r = new Vector3D(_evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5);
        r -= Vector3D.Dot(r, dir) * dir;
        if (r.LengthSquared() > 1e-6) centre += Vector3D.Normalize(r) * t.Radius * 0.35 * _evRng.NextDouble();
    }
    return centre + dir * Math.Min(t.Radius, 12);
}
void AimMissed(Track t)
{
    for (int i = t.Hull.Count - 1; i >= 0; i--) if ((t.Hull[i] - t.AimLocal).LengthSquared() < 9) t.Hull.RemoveAt(i);
    t.AimT = -99;
}

// =====================================================================
//  v1.8: AI-BLOCK HYBRID SEARCH
//  AI Offensive/Defensive blocks spot enemies all around out to 2.5 km, but don't share the target
//  with scripts. So: while there's no target, the script hands the thrusters and gyros to the AI blocks
//  tagged [AI] (Offensive + Flight). The AI turns the guns onto whatever it finds; the nose cameras
//  fire rays straight ahead, and the moment one hits an enemy the script switches the AI off and takes
//  over with its own lock, lead, hull aiming and fire control.
//  Alarm: an AI Defensive block (NOT tagged) can run this PB with "aialert" when it detects something,
//  which makes this drone and its hive search hard for 15 s.
List<IMyFunctionalBlock> _ai = new List<IMyFunctionalBlock>();
bool _aiOn = false; double _lastTgtT = -99, _alertT = -99; int _aiScan = 0;
double AiSpiralMin = 0.15, AiSpiralMax = 2.5, AiSpiralPow = 1.5;   // confirm-ray spiral (deg off the boresight, every 2nd ray; the others go down the boresight)
bool AiConfirmAdaptive = true; double _lastTgtDist = -1;
bool _aiKick = false; double _aiSince = -99, _aiKickAt = -99;
double AiRepick = 6, AiRepickWindow = 40;   // see AiStep
bool OwnAlert { get { return _now - _alertT < 15; } }
// my own lock has gone stale (flag 4 in the swarm status): mates that still see the target answer at once with a fresh fix
bool LockStale { get { return _tgt != null && _tgt.Src != "IGC" && _now - _tgt.Seen > 1.2; } }
bool Alerted
{
    get
    {
        if (OwnAlert) return true;
        foreach (var m in _mates.Values) if ((m.Flags & 2) != 0 && _now - m.Seen < 3) return true;
        return false;
    }
}
bool IsAiBlock(IMyTerminalBlock b)
{
    if (!b.IsSameConstructAs(Me)) return false;
    if (b.CustomName.Contains("[AI]")) return true;
    string d = b.BlockDefinition.ToString();
    return d.Contains("OffensiveCombat") || d.Contains("FlightMovement");
}
void FindAi()
{
    GridTerminalSystem.GetBlocksOfType(_ai, b => IsAiBlock(b));
    foreach (var b in _ai) b.Enabled = _aiOn;   // never let them fight the script
}
bool AiStep()
{
    if (_tgt != null) { _lastTgtT = _now; _lastTgtDist = Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()); }
    // The AI also takes over while my lock is stale (nothing seen for AiStaleTakeover s): it keeps the nose on the enemy
    // and the confirm rays below pick it up again, which hands control straight back to the script.
    bool want = AiAssist && _ai.Count > 0 && AiModeOk() &&
        ((_tgt == null && _now - _lastTgtT > 2) || (_tgt != null && _now - _tgt.Seen > AiStaleTakeover));
    if (_tgt != null) { _noTgtSince = _now; if (_patrolLeg) { _patrolLeg = false; _patrolPhaseT = _now; } }   // (a target ends the patrol leg at once, not only when the target is gone again)
    if (_aiOn && PatrolOn)
    {   // is the AI flying / turning the ship? (the patrol leaves it alone while it is; its own speed jitters, so movement in the last 4 s counts)
        var vs = _rc.GetShipVelocities();
        Vector3D refV = _patrolK == 0 ? Vector3D.Zero : _listenV;   // before the first leg the reference is rest
        if ((vs.LinearVelocity - refV).LengthSquared() > 9 || vs.AngularVelocity.LengthSquared() > 0.0144) _aiMoveT = _now;
    }
    if (want && PatrolStep()) want = false;     // on a patrol leg the script flies and the AI blocks stay off until the next listening stop
    if (want != _aiOn)
    {
        _aiOn = want;
        _firing = false;                        // guns' state is unknown after any hand-over: SetFire sends it again
        if (want) { Release(); _aiSince = _now; }   // thrusters and gyros go to the AI
        _aiKick = false;
        foreach (var b in _ai) b.Enabled = want;
    }
    if (!_aiOn) return false;
    // Re-pick: the AI holds whatever it chose first (a wreck, a decoy) and may never turn onto the live enemy beside it. If the AI has been
    // on for AiRepick seconds although I fought within AiRepickWindow seconds (so enemies are about), power-cycle it for one tick: it starts
    // a fresh search. Never before the first contact, so the opening search is left to the AI block.
    if (_aiKick) { _aiKick = false; foreach (var b in _ai) b.Enabled = true; return true; }
    if (AiRepick > 0 && _now - Math.Max(_aiSince, _aiKickAt) > AiRepick && _now - _lastTgtT < AiRepickWindow)
    {
        _aiKick = true; _aiKickAt = _now;
        foreach (var b in _ai) b.Enabled = false;
        return true;
    }
    // The AI points the guns at what it found (about 0.1 deg off), so the enemy is dead ahead of the nose cameras.
    // Every forward camera casts a ray as soon as it holds AiConfirmRange of charge (~1.5 casts/s each; charge builds
    // 2 km/s per camera). The pattern puts 1 ray in 2 exactly down the boresight, the rest in a spiral that widens
    // from 0.15 to 2.5 deg: a small ship is only 0.2-0.8 deg wide at 300-1200 m, so the old 0.59 deg minimum
    // offset never hit it. The rays are not wasted on tracking: the AI is on only while there is no target.
    // The first forward camera is the "long-range" one: it waits for AiConfirmLong of charge (one ray per 1.3 s) so a
    // target the AI already holds at 1.3-2.6 km is still found; the others cast the short, frequent rays.
    bool longCam = true;
    foreach (var c in _cams)
    {
        if (!c.IsWorking || !Forwardish(c)) continue;
        double len = longCam ? AiConfirmLong : AiConfirmRange;
        longCam = false;
        // fighting this enemy a moment ago: it is probably still about that far away, and a ray costs its length (cheaper rays = more of them);
        // only for the first 6 s of an AI stint - if nothing is found by then the AI is on something else, farther away, and the full length is needed
        if (AiConfirmAdaptive && _now - _lastTgtT < 25 && _now - _aiSince < 6 && _lastTgtDist > 0) len = Math.Min(len, Math.Max(500, _lastTgtDist * 1.5 + 200));
        if (c.AvailableScanRange < len) continue;
        _aiScan++;
        double r = 0, th = _aiScan * 2.39996323;
        if ((_aiScan & 1) == 1) { int j = (_aiScan >> 1) % 16; r = AiSpiralMin + (AiSpiralMax - AiSpiralMin) * Math.Pow((j + 0.5) / 16.0, AiSpiralPow); }
        var e = c.Raycast(len, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
        if (Valid(e)) AddContact(e, "AI");
        else if (!e.IsEmpty() && e.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies && (e.Type == MyDetectedEntityType.SmallGrid || e.Type == MyDetectedEntityType.LargeGrid)
                 && Ignored(e.EntityId) && e.BoundingBox.Size.Length() * 0.5 >= 1.6) AiHeldIgnored(e.EntityId);
    }
    return true;
}
// ---- SEARCH PATROL ---------------------------------------------------------------------------------------------------
//  HUNT means "go and find them". The AI blocks only see out to their own search range, so a drone that has had no target for
//  PatrolAfter seconds stops waiting at home: it flies chords of a square of "radius" PatrolRadius around the home point (cruise speed,
//  AI blocks off), and stops every leg for PatrolListen seconds with the AI blocks on, which is when they look around. As soon as there is
//  a target, or the drone takes damage, the patrol ends. Needs AI blocks, HUNT mode and open space (no planet gravity).
bool PatrolOn = true;
double PatrolRadius = 1200, PatrolAfter = 15, PatrolAfterFight = 90, PatrolListen = 4, PatrolMaxLeg = 40, PatrolSpeed = 60;
bool _patrolLeg = false, _patrolFrame = false; double _patrolPhaseT = -99, _noTgtSince = 0, _listenT = -1, _aiMoveT = -99, PatrolHold = 90; int _patrolK = 0; Vector3D _listenV;
Vector3D _patrolWp, _patrolC, _patrolE1, _patrolE2;
bool PatrolStep()
{
    if (!PatrolOn || _mode != Mode.HUNT || _ai.Count == 0 || _rc == null || UnderFire || _now - _noTgtSince < (_lastTgtT < 0 ? PatrolAfter : PatrolAfterFight) || _rc.GetNaturalGravity().LengthSquared() > 0.25)
    { _patrolLeg = false; return false; }
    if (!_patrolLeg)
    {
        if (_listenT < 0 || _listenT < _patrolPhaseT) { _listenT = _now; _listenV = _rc.GetShipVelocities().LinearVelocity; }   // a listening stop begins
        if (_now - _patrolPhaseT < PatrolListen) return false;
        // The AI blocks need a few seconds to turn onto what they found and for a confirm ray to hit it. If the ship is being flown
        // (velocity changed by more than 3 m/s, or it is turning) the AI has a target: leave it on, up to PatrolHold seconds.
        if (_now - _listenT < PatrolHold && _now - _aiMoveT < 4) return false;
        if (!_patrolFrame)
        {
            _patrolC = Home(); _patrolE1 = _rc.WorldMatrix.Forward; _patrolE2 = _rc.WorldMatrix.Right; _patrolFrame = true;
        }
        double th = _patrolK++ * 1.5707963;
        _patrolWp = _patrolC + (_patrolE1 * Math.Cos(th) + _patrolE2 * Math.Sin(th)) * PatrolRadius;
        _patrolLeg = true; _patrolPhaseT = _now; _listenT = -1;
    }
    else if (_now - _patrolPhaseT > PatrolMaxLeg || Vector3D.Distance(_rc.GetPosition(), _patrolWp) < 150) { _patrolLeg = false; _patrolPhaseT = _now; }
    return _patrolLeg;
}

// The AI blocks keep their nose on an enemy that I wrote off as finished (tumbling, shrunk): if two confirm rays in 10 s find it there it is
// still a live target for them (an AI that never lets go would otherwise idle this drone until its next search), so it comes off my ignore list.
long _heldId = 0; double _heldT = -99; int _heldN = 0;
void AiHeldIgnored(long id)
{
    if (id != _heldId || _now - _heldT > 10) { _heldId = id; _heldN = 0; }
    _heldT = _now;
    if (++_heldN >= 2) { _ignore.Remove(id); _heldN = 0; }
}
string AiStatus()
{
    if (_ai.Count == 0) return OwnAlert ? "AI alarm: searching\n" : "";
    return "AI: " + (_aiOn ? "searching" : "standby") + " (" + _ai.Count + " blocks)" + (OwnAlert ? "  ALARM" : "") + "\n";
}
bool AiModeOk() { return _mode == Mode.GUARD || _mode == Mode.HUNT; }

// =====================================================================
//  v1.8: PRINTER SUPPORT
//  * A drone printed while merged to a printer shares the printer's grid. Blocks are re-scanned
//    whenever this PB's grid changes (merged / released), so it never drives the printer's thrusters.
//  * The take-off after the release (v2.2) is the LAUNCH block above (docs/launch-protocol.md): it no longer waits for
//    one "<Tag>_JOIN" message; JOIN, FLEET "join", the argument "armed" and [Launch] Armed are only extra triggers.
IMyCubeGrid _myGrid;
void CheckGrid() { if (Me.CubeGrid != _myGrid) { _myGrid = Me.CubeGrid; GetBlocks(); } }
