// =====================================================================
//  MANTIS HYBRID AI  v2.1-mantis  - railgun + gatling mid-range drone controller
//  Space Engineers (vanilla, small grid). Paste into a Programmable Block.
//  Derived from LONGBOW SNIPER AI v1.8 (railgun fire control, tracking, hive over IGC, AI
//  hand-off, damage sensing). What is new:
//   * Two weapon groups on one spine: the railgun (alpha strike, single shots, ShootOnce with
//     ammo-drop confirmation, as Longbow) and the gatling(s), which spray whenever THEIR OWN
//     lead solution (800 m/s bullets, computed from the gatling's position) is inside the
//     target's hit window and the target is within Gatling Range.
//   * The engage range is capped at Engage Cap (650 m) so the gatling is always in play; the
//     railgun's own "max accurate range" logic still picks anything closer than the cap.
//   * If the railgun is destroyed the gatlings take over the primary slot (continuous fire).
//   * Ammo / RTB logic counts both weapon groups.
//  v2.1-mantis (ported from Longbow v1.9) (sim-audit pass; details and numbers in docs/script-fix-notes.md)
//   * AI hand-over: confirm rays really cover the boresight (1 in 2 exactly down the nose), one long ray (2.6 km) + short
//     ones (1.3 km); the AI also takes over when the lock has been stale 3 s (1 s beyond 1.4 km).
//   * Lock keeping: cheaper rays, hull-point probes, a stale lock becomes a "ghost" (8 s) searched with 2 rays/tick on a
//     widening spiral; the loss is announced to the hive; after a target is dropped the nose turns to where it was.
//   * Railgun: the shot delay (0.5 s small / 2 s large) is NOT added to the lead (the gun keeps tracking until the slug leaves, so
//     the solution of the moment of firing is the right one; adding it over-led and cut the hit rate from 85 % to 5 %); the ship
//     holds steady while a shot is pending. Accepted shots are confirmed with IsShooting (jinking and the reload timer work
//     without an ammo drop). Stand-off 650 m (Engage Cap) (Preferred Engage Range) instead of the "max accurate range" of 350-500 m,
//     fire only inside stand-off x1.15 + 50 m, aim window capped at 2 m (6 m on big targets), centre aim for small grids (hull points for large ones).
//   * Hive: wingmate separation, friendly-fire gate from IGC positions (no camera needed; also while the AI blocks fly),
//     shot-count based target switching, wrecks (small ship hit, no longer manoeuvring) are dropped.
// ---------------------------------------------------------------------
//  WHAT IT DOES
//   * Detects the weapon and loads its ballistics (speed, range, gravity
//     drop, reload). Unknown weapons fall back to the Custom Data values.
//   * Finds targets with long-range camera raycasts (auto spiral scan while
//     yawing and nodding, or manual "lock"), sensor blocks (all around,
//     ~50 m), designators on the drone, or IGC targets from a
//     mothership. Tracks position, velocity and filtered acceleration.
//   * MAX ACCURATE RANGE: for every target it solves how far away a shot
//     can be taken and still land inside the target if the target dodges
//     after the trigger is pulled:  miss = 0.5*a*t^2 + vErr*t <= k*radius.
//     The drone holds that distance, but never closer than Preferred Engage Range (capped by the weapon's range).
//   * Lead solution includes target acceleration AND gravity drop of the
//     sabot. Fires only after the aim has settled inside the hit window,
//     with a fresh pre-shot raycast and a friendly line-of-fire check.
//   * Shoot-and-scoot: jinks while the railgun recharges, settles for the
//     shot. Tracks the recharge from confirmed shots (ammo used).
//   * Return to base on low battery / out of sabots. Thrust is power-
//     limited so recharging the railgun never browns out the drone.
//
//  COMMANDS (argument, toolbar, or IGC "<Tag>_CMD")
//   guard | hunt | assist | rtb | idle | lock | unlock | home | reload
//   range <m>   force an engagement range (0 = automatic)
// =====================================================================

const string VER = "2.1-mantis";

// ---------------- settings (Custom Data) ----------------
string Tag = "MANTIS";
string StartMode = "IDLE";
double ProjSpeed = 1000, WeaponRange = 1400, ReloadTime = 20; // used for unknown weapons
double ShotDelayCfg = 0;     // trigger -> projectile leaves the barrel, unknown weapons (railgun data: 0.5 s small, 2 s large)
double ShotDelay = 0;        // the value in use (set by DetectWeapon)
bool GravityDrop = true, InheritVel = true, AutoWeapon = true;
double HitFraction = 0.5;    // shot must land within this fraction of target radius
double VelErr = 0.3;         // m/s assumed velocity estimate error
double DodgeSmall = 8, DodgeLarge = 3, DodgeChar = 4; // assumed evasive accel floors m/s^2
double RangeMargin = 0.95;   // never plan shots beyond this fraction of weapon range
double MinEngage = 250;      // closest the railgun will choose to fight
double EngageCap = 650;      // never plan to sit further than this (keeps the gatling in range); 0 = railgun range
bool UseGatling = true;
double GatSpeed = 800, GatRange = 650, GatTol = 1.0; // gatling bullet speed (data: 800 m/s, 800 m), fire range, window multiplier
double ForcedRange = 0;      // >0 overrides automatic range
double GuardRadius = 2000, MaxSpeed = 95, CruiseSpeed = 55, DriftSpeed = 8, JinkSpeed = 11;
double LockRange = 3000, ScanCone = 25;
double CloseScanRange = 200; // all-around camera scan distance
bool CloseScan = true;
double AimKp = 6, AimKd = 0.4, MaxTurn = 4;
int SettleTicks = 8;
double CalmAccel = 0, CalmWait = 2.5;   // railgun: wait up to CalmWait s for a moment when the target is not accelerating harder than CalmAccel m/s^2 (0 = off)
double AimWindowMaxLarge = 6.0;   // ... for a big target (large grid / radius over 12 m), where the hull is wide and every hit cripples something
double AimWindowMax = 2.0;    // railgun: never accept an aim error worth more than this many metres at the target (the whole sphere is far too generous)
double PreferRange = 650;   // stand-off the sniper likes against any target (gatling ships cannot reach it, a slug flies 1 s)
double FireRangeFactor = 1.15, FireRangeSlack = 50; // the railgun only fires inside engage range x factor + slack
double LeadScale = 1.0;      // scales the lead offset if shots land consistently ahead/behind
bool UseTargetAccel = true;
bool SplitFocus = true;
double DisabledGrace = 5, EvadeSpeed = 20;
bool AiAssist = true; double AiConfirmRange = 1300, AiConfirmLong = 2600;
double RtbBattery = 0.2;
bool TargetLarge = true, TargetSmall = true, TargetChars = false, TargetNeutral = false;
bool AutoScan = true, ShareTargets = true, FriendCheck = true;

// ---------------- weapon profile ----------------
string _wName = "custom";
bool _slowWeapon = true;     // single-shot weapon (railgun/artillery) vs continuous

// ---------------- state ----------------
enum Mode { IDLE, GUARD, HUNT, ASSIST, RTB }
Mode _mode = Mode.IDLE;
MyIni _ini = new MyIni();
double _now = 0; long _tick = 0;
Vector3D _home; bool _homeSet = false;
Vector3D _dynHome; double _dynHomeT = -99;

IMyShipController _rc;
List<IMyGyro> _gyros = new List<IMyGyro>();
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyThrust>[] _thrDir = new List<IMyThrust>[6];
List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();   // primary: the railgun(s)
List<IMyUserControllableGun> _gats = new List<IMyUserControllableGun>();   // secondary: gatling(s)
List<IMyCameraBlock> _cams = new List<IMyCameraBlock>();
List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
List<IMyTurretControlBlock> _ctcs = new List<IMyTurretControlBlock>();
List<IMyBatteryBlock> _bats = new List<IMyBatteryBlock>();
IMyTextSurface _lcd;
IMyTerminalBlock _aimRef;
IMyBroadcastListener _lTgt, _lCmd, _lHome;
class Gun
{
    public IMyUserControllableGun B;
    public double ReadyAt = 0, PendingAt = -1, LastShot = -999; public long AmmoAtShot; public bool SawShoot;
}
List<Gun> _gunState = new List<Gun>();

class Track
{
    public long Id; public Vector3D Pos, Vel, Acc; public double AccMag;
    public double Spin, SpinSince = -1, Rad0, DeadAt = -1, DisSince = -1; public Vector3D OF, OU; public bool HasOri;
    public MatrixD O = MatrixD.Identity; public List<Vector3D> Hull = new List<Vector3D>(); public int HullNext;
    public Vector3D Thrust, AimLocal; public double AimT = -99;
    public int ShotsAt, NSet; public double LastShotT = -999, QuietSince = -1;   // slugs I have fired at it; since when it has not accelerated
    // how fast it's rotating, from orientation changes between sightings
    public void Ori(MatrixD o, double dt)
    {
        if (HasOri && dt > 0.04 && dt < 2)
        {
            double a = Math.Max(Math.Acos(MathHelper.Clamp(Vector3D.Dot(OF, o.Forward), -1, 1)), Math.Acos(MathHelper.Clamp(Vector3D.Dot(OU, o.Up), -1, 1)));
            Spin = Spin * 0.7 + a / dt * 0.3;
        }
        OF = o.Forward; OU = o.Up; HasOri = true; O = o;
    } public double Radius, Seen, PeakAcc; public string Src;
    public MyDetectedEntityType Type;
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
        PeakAcc = Math.Max(Acc.Length(), PeakAcc * 0.98);
        // a ship that has stopped manoeuvring altogether (after being shot at) has lost its engines or its pilot
        if (src != "IGC")   // (an IGC fix is a mate's smoothed extrapolation: it always looks calm)
        {
            NSet++;
            if (NSet >= 5 && AccMag < 0.4 && PeakAcc < 0.8) { if (QuietSince < 0) QuietSince = now; } else QuietSince = -1;
        }
        Pos = p; Vel = v; Radius = Math.Max(r, 1.5); Seen = now; Src = src; Rad0 = Math.Max(Rad0, Radius);
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
    // acceleration is only trusted for 2 s (a ghost track must not run away on a quadratic)
    public Vector3D P(double now) { double t = now - Seen, ta = Math.Min(t, 2.0); return Pos + Vel * t + EA() * (ta * (t - 0.5 * ta)); }
    public Vector3D V(double now) { return Vel + EA() * Math.Min(now - Seen, 2.0); }
}
Dictionary<long, Track> _contacts = new Dictionary<long, Track>();
List<long> _drop = new List<long>();
Track _tgt;
const double TrackTimeout = 5.0;

Vector3D _prevErr, _prevAim; bool _havePrev = false;
double _powerScale = 1;
bool _firing = false;
int _settled = 0;
double _holdFireUntil = 0;
Vector3D _jink; double _nextJink = 0; double _driftSign = 1;
int _scanIdx = 0;
Random _rng = new Random();
string _err = "", _last = "";
double _aimErrDeg, _winDeg, _leadT, _engage, _drop2, _avgMs; int _peakIns = 0;   // PB limit is Runtime.MaxInstructionCount per run
int _shots = 0;
double _batFrac = 1; int _sabots = 0; bool _hadAmmo = false;
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

public Program()
{
    for (int i = 0; i < 6; i++) _thrDir[i] = new List<IMyThrust>();
    LoadConfig();
    FleetLoad(true);
    _mode = ParseMode(StartMode, Mode.IDLE);
    if (!string.IsNullOrEmpty(Storage))
    {
        var p = Storage.Split(';');
        double x, y, z;
        if (p.Length >= 5 && double.TryParse(p[1], out x) && double.TryParse(p[2], out y) && double.TryParse(p[3], out z))
        { _mode = ParseMode(p[0], _mode); _home = new Vector3D(x, y, z); _homeSet = p[4] == "1"; }
    }
    _lTgt = IGC.RegisterBroadcastListener(Tag + "_TGT");
    _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
    _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME");
    _lJoin = IGC.RegisterBroadcastListener(Tag + "_JOIN");
    _lSwarm = IGC.RegisterBroadcastListener(Tag + "_SWARM");
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

    ReadIGC(); ReadJoin(); ReadSwarm();
    if (_tick % 6 == 3) BroadcastStatus();
    StepSlimScan();
    if (_tick % 20 == 7) CheckDamage();
    if (_tick % 6 == 0) PollDesignators();
    if (_tick % 10 == 5) PollSensors();
    if (_tick % 10 == 0) { PruneAndSelect(); PowerAndHealth(); }
    UpdateGuns();
    MaintainLock(0.25);
    if (_mode != Mode.IDLE) OmniScan();
    if (_tgt == null && !_aiOn && AutoScan && (_mode == Mode.GUARD || _mode == Mode.HUNT)) AutoScanStep();
    if (ShareTargets && _tgt != null && _tgt.Src != "IGC" && _now - _tgt.Seen < 1.0 && _tick % 30 == 0) ShareTarget();

    if (!AiStep()) Control(dt);
    _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount);
    if (_tick % 30 == 0) Display();
}

// =====================================================================
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
        case "range": double r; if (w.Length > 1 && double.TryParse(w[1], out r)) ForcedRange = Math.Max(0, r); break;
        case "reload": LoadConfig(); FleetLoad(true); GetBlocks(); break;
    }
}

void SetMode(Mode m) { _mode = m; if (m == Mode.IDLE || m == Mode.ASSIST) Release(); }

// =====================================================================
void LoadConfig()
{
    MyIniParseResult res;
    if (!_ini.TryParse(Me.CustomData, out res)) { _err = "CustomData parse error line " + res.LineNo; return; }
    const string S = "Mantis";
    Tag = Cfg(S, "IGC Tag", Tag);
    StartMode = Cfg(S, "Start Mode (GUARD/HUNT/ASSIST/IDLE)", StartMode);
    AutoWeapon = Cfg(S, "Auto Detect Weapon", AutoWeapon);
    ProjSpeed = Cfg(S, "Custom Projectile Speed", ProjSpeed);
    WeaponRange = Cfg(S, "Custom Weapon Range", WeaponRange);
    ReloadTime = Cfg(S, "Custom Reload Seconds", ReloadTime);
    ShotDelayCfg = Cfg(S, "Custom Shot Delay Seconds", ShotDelayCfg);
    GravityDrop = Cfg(S, "Custom Gravity Drop", GravityDrop);
    InheritVel = Cfg(S, "Projectiles Inherit Ship Velocity", InheritVel);
    HitFraction = Cfg(S, "Hit Fraction Of Target Radius", HitFraction);
    VelErr = Cfg(S, "Velocity Error m/s", VelErr);
    DodgeSmall = Cfg(S, "Assumed Dodge Accel Small Grid", DodgeSmall);
    DodgeLarge = Cfg(S, "Assumed Dodge Accel Large Grid", DodgeLarge);
    DodgeChar = Cfg(S, "Assumed Dodge Accel Character", DodgeChar);
    RangeMargin = Cfg(S, "Range Margin", RangeMargin);
    MinEngage = Cfg(S, "Min Engage Range", MinEngage);
    EngageCap = Cfg(S, "Engage Cap", EngageCap);
    UseGatling = Cfg(S, "Use Gatling", UseGatling);
    GatSpeed = Cfg(S, "Gatling Projectile Speed", GatSpeed);
    GatRange = Cfg(S, "Gatling Range", GatRange);
    GatTol = Cfg(S, "Gatling Fire Window Multiplier", GatTol);
    ForcedRange = Cfg(S, "Forced Range (0 = auto)", ForcedRange);
    GuardRadius = Cfg(S, "Guard Radius", GuardRadius);
    MaxSpeed = Cfg(S, "Combat Speed", MaxSpeed);
    CruiseSpeed = Cfg(S, "Cruise Speed", CruiseSpeed);
    DriftSpeed = Cfg(S, "Drift Speed While Aiming", DriftSpeed);
    JinkSpeed = Cfg(S, "Jink Speed While Reloading", JinkSpeed);
    LockRange = Cfg(S, "Manual Lock Range", LockRange);
    AutoScan = Cfg(S, "Auto Camera Scan", AutoScan);
    CloseScan = Cfg(S, "All-around Close Scan", CloseScan);
    CloseScanRange = Cfg(S, "Close Scan Range", CloseScanRange);
    ScanCone = Cfg(S, "Scan Cone Degrees", ScanCone);
    TargetLarge = Cfg(S, "Target Large Grids", TargetLarge);
    TargetSmall = Cfg(S, "Target Small Grids", TargetSmall);
    TargetChars = Cfg(S, "Target Characters", TargetChars);
    TargetNeutral = Cfg(S, "Target Neutral/Unowned", TargetNeutral);
    ShareTargets = Cfg(S, "Share Targets Over IGC", ShareTargets);
    FriendCheck = Cfg(S, "Friendly Fire Check", FriendCheck);
    RtbBattery = Cfg(S, "RTB Battery Fraction", RtbBattery);
    AimKp = Cfg(S, "Aim Kp", AimKp);
    AimKd = Cfg(S, "Aim Kd", AimKd);
    MaxTurn = Cfg(S, "Max Turn Rate rad/s", MaxTurn);
    SettleTicks = (int)Cfg(S, "Settle Ticks Before Shot", SettleTicks);
    LeadScale = Cfg(S, "Lead Scale (1 = calculated)", LeadScale);
    PreferRange = Cfg(S, "Preferred Engage Range", PreferRange);
    AimWindowMax = Cfg(S, "Max Aim Window m (railgun)", AimWindowMax);
    AimWindowMaxLarge = Cfg(S, "Max Aim Window m (big target)", AimWindowMaxLarge);
    CalmAccel = Cfg(S, "Calm Target Accel m/s2 (0 off)", CalmAccel);
    CalmWait = Cfg(S, "Max Wait For Calm Target s", CalmWait);
    FireRangeFactor = Cfg(S, "Fire Range Factor", FireRangeFactor);
    FireRangeSlack = Cfg(S, "Fire Range Slack", FireRangeSlack);
    UseTargetAccel = Cfg(S, "Use Target Acceleration", UseTargetAccel);
    SplitFocus = Cfg(S, "Split Targets Across Hive (false = focus fire)", SplitFocus);
    DisabledGrace = Cfg(S, "Seconds Before Dropping Disabled Target", DisabledGrace);
    EvadeSpeed = Cfg(S, "Evade Speed When Hit", EvadeSpeed);
    AimMode = Cfg(S, "Aim Point (AUTO/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();
    AiAssist = Cfg(S, "AI Block Search Assist", AiAssist);
    AiConfirmRange = Cfg(S, "AI Confirm Raycast Range", AiConfirmRange);
    AiConfirmLong = Cfg(S, "AI Confirm Long Raycast Range", AiConfirmLong);
    FireStaleMax = Cfg(S, "Max Track Age To Fire Gatling (s)", FireStaleMax);
    SepRange = Cfg(S, "Wingmate Separation Range", SepRange);
    SepSpeed = Cfg(S, "Wingmate Separation Speed", SepSpeed);
    string o = _ini.ToString();
    if (o != Me.CustomData) Me.CustomData = o;
}
string Cfg(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double Cfg(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool Cfg(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

// =====================================================================
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
    _gats.Clear();
    for (int gi = _guns.Count - 1; gi >= 0; gi--) if (_guns[gi] is IMySmallGatlingGun) { _gats.Insert(0, _guns[gi]); _guns.RemoveAt(gi); }
    if (_guns.Count == 0 && _gats.Count > 0) { _guns.AddRange(_gats); _gats.Clear(); }   // railgun lost: gatlings become the primary weapon
    GridTerminalSystem.GetBlocksOfType(_cams, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_turrets, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_ctcs, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_bats, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_sensors, b => b.IsSameConstructAs(Me));
    ConfigureSensors();
    foreach (var c in _cams) c.EnableRaycast = true;
    ScanSlims();
    FindAi();

    // keep per-gun state across refreshes
    var old = _gunState; _gunState = new List<Gun>();
    foreach (var g in _guns)
    {
        Gun s = null;
        foreach (var o in old) if (o.B == g) { s = o; break; }
        if (s == null) s = new Gun { B = g };
        _gunState.Add(s);
    }
    DetectWeapon();

    for (int i = 0; i < 6; i++) _thrDir[i].Clear();
    if (_rc != null)
    {
        MatrixD m = _rc.WorldMatrix;
        Vector3D[] dirs = { m.Forward, m.Backward, m.Left, m.Right, m.Up, m.Down };
        foreach (var t in _thr)
        {
            Vector3D push = t.WorldMatrix.Backward;
            for (int i = 0; i < 6; i++) if (Vector3D.Dot(push, dirs[i]) > 0.9) { _thrDir[i].Add(t); break; }
        }
    }
    _aimRef = _guns.Count > 0 ? (IMyTerminalBlock)_guns[0] : _rc;
    _lcd = Me.GetSurface(0);
    _lcd.ContentType = ContentType.TEXT_AND_IMAGE;
    _lcd.FontSize = 0.72f;
    _lcd.Font = "Monospace";
}

void DetectWeapon()
{
    _wName = "custom"; _slowWeapon = ReloadTime > 2; ShotDelay = ShotDelayCfg;
    if (!AutoWeapon || _guns.Count == 0) return;
    var g0 = _guns[0];
    bool large = g0.CubeGrid.GridSizeEnum == MyCubeSize.Large;
    if (g0 is IMySmallMissileLauncherReload)   // railgun block type
    {
        _wName = large ? "Large Railgun" : "Small Railgun";
        ProjSpeed = large ? 2000 : 1000; WeaponRange = large ? 2000 : 1400; ReloadTime = large ? 60 : 20;
        GravityDrop = true; _slowWeapon = true; ShotDelay = large ? 2.0 : 0.5;
    }
    else if (g0 is IMySmallGatlingGun)
    {
        _wName = "Gatling/Autocannon";
        ProjSpeed = GatSpeed; WeaponRange = 800 * 0.85; ReloadTime = 0; GravityDrop = false; _slowWeapon = false; ShotDelay = 0;
    }
    // artillery / assault cannon: ballistics vary by version - set Custom values and Auto Detect Weapon=false
}

// =====================================================================
//  TARGETS
bool Valid(MyDetectedEntityInfo e)
{
    if (e.IsEmpty() || Ignored(e.EntityId) || e.EntityId == Me.CubeGrid.EntityId) return false;
    switch (e.Type)
    {
        case MyDetectedEntityType.LargeGrid: if (!TargetLarge) return false; break;
        case MyDetectedEntityType.SmallGrid: if (!TargetSmall) return false; break;
        case MyDetectedEntityType.CharacterHuman:
        case MyDetectedEntityType.CharacterOther: if (!TargetChars) return false; break;
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
    if (!_contacts.TryGetValue(e.EntityId, out t)) { t = new Track { Id = e.EntityId }; _contacts[e.EntityId] = t; }
    t.Type = e.Type;
    t.Ori(e.Orientation, _now - t.Seen);
    t.Set(e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() * 0.5, _now, src);
    StoreHull(t, e);
    return t;
}

void PollDesignators()
{
    foreach (var t in _turrets)
    {
        if (!t.IsFunctional || !t.HasTarget) continue;
        var e = t.GetTargetedEntity(); if (Valid(e)) AddContact(e, "TURRET");
    }
    foreach (var c in _ctcs)
    {
        if (!c.IsFunctional || !c.HasTarget) continue;
        var e = c.GetTargetedEntity(); if (Valid(e)) AddContact(e, "CTC");
    }
}

void ReadIGC()
{
    ReadTgt(_lTgt);
    foreach (var fl in FleetTgtL) ReadTgt(fl);
    while (_lCmd.HasPendingMessage) { var msg = _lCmd.AcceptMessage(); if (msg.Data is string) Command((string)msg.Data); }
    while (_lHome.HasPendingMessage) { var msg = _lHome.AcceptMessage(); if (msg.Data is Vector3D) { _dynHome = (Vector3D)msg.Data; _dynHomeT = _now; } }
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
        if (Ignored(d.Item1) || !FleetOk(d)) continue;
        Track t;
        if (_contacts.TryGetValue(d.Item1, out t) && t.Src != "IGC" && _now - t.Seen < 0.5) continue;
        if (t == null) { t = new Track { Id = d.Item1, Type = MyDetectedEntityType.LargeGrid }; _contacts[d.Item1] = t; }
        t.Set(d.Item2, d.Item3, d.Item4, _now, "IGC");
    }
}

void ShareTarget()
{
    var t = _tgt;
    var tup = new MyTuple<long, Vector3D, Vector3D, double, long>(t.Id, t.P(_now), t.V(_now), t.Radius, Me.EntityId);
    IGC.SendBroadcastMessage(Tag + "_TGT", tup);
    FleetSend(tup);
}

IMyCameraBlock CamFor(Vector3D pos, double range)
{
    IMyCameraBlock best = null; double bestRange = 0;
    foreach (var c in _cams)
    {
        if (!c.IsWorking) continue;
        Vector3D d = pos - c.GetPosition(); double len = d.Length();
        if (len < 1 || Vector3D.Dot(d / len, c.WorldMatrix.Forward) < 0.72) continue;
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
    if (e.IsEmpty() || e.Type == MyDetectedEntityType.Planet || e.Type == MyDetectedEntityType.Asteroid) { _err = "Lock: nothing hit (" + cam.AvailableScanRange.ToString("0") + " m charge)"; return; }
    if (Friendly(e)) { _err = "Lock: target is friendly"; return; }
    _err = ""; _tgt = AddContact(e, "LOCK");
}

// ---- LOCK MAINTENANCE AND RE-ACQUISITION (ported from the Hornet, docs/script-fix-notes.md H3) ------------------
//  A camera banks only 2 km/s of ray range and a miss costs as much as a hit, so the rays are cheap (they stop just behind
//  the predicted point) and well aimed. When the lock goes stale (>0.6 s) the target is kept as a "ghost" for GhostTime
//  seconds: two rays per tick sweep a widening spiral round its extrapolated position (uncertainty grows with age squared),
//  and the loss is announced to the hive. A railgun only fires on a fresh fix, so the ghost never shoots by itself.
const double GhostTime = 8.0;       // how long a lost target is kept and searched for
const double AiStaleTakeover = 3.0; // lock stale for this long: let the AI blocks hold the enemy (if there are any)
const double AiStaleTakeoverFar = 1.0;  // ... and for a target beyond 1.4 km, where a lock-refresh ray (1.4-2.6 km of charge) rarely finds a 5 m ship
double FireStaleMax = 1.0;          // gatling: never fire at a track older than this
int _spiralIdx = 0, _probeN = 0; double _lastStaleShare = -9, _ignoreNoteT = -99;
Vector3D _lostPos, _lostVel; double _lostT = -99, _lostRad = 3;   // last known state of the target that was dropped
void NoteLost(Track t)
{
    if (t.Src == "IGC" || t.DeadAt > 0 || t.DisSince >= 0) return;
    _lostPos = t.Pos; _lostVel = t.Vel; _lostT = t.Seen; _lostRad = t.Radius;
}
double Uncertainty(Track t, double age)
{
    return Math.Min(80, t.Radius * 0.35 + 0.5 * Math.Max(t.AccMag, 6.0) * age * age);
}
// returns true if the track was refreshed this call
bool MaintainLock(double maxAge)
{
    if (_tgt == null || _cams.Count == 0 || _aiOn) return false;   // (AI on: the cameras' charge is for the confirm rays)
    double age = _now - _tgt.Seen;
    if (age < maxAge) return false;
    if ((_tgt.Src == "TURRET" || _tgt.Src == "CTC" || _tgt.Src == "SENSOR") && age < 0.5 && maxAge > 0.1) return false;
    Vector3D p = _tgt.P(_now), me = _rc.GetPosition();
    Vector3D dir = p - me; double dist = dir.Length();
    if (dist < 1) return false;
    dir /= dist;
    int rays = age > 0.6 ? 2 : 1;                    // stale: spend the charge on finding it again
    for (int k = 0; k < rays; k++)
    {
        bool probingAim = false;
        Vector3D aim = age > 0.6 ? ReacquirePoint(_tgt, p, dir, age) : ProbePoint(_tgt, p, dir, dist, out probingAim);
        var cam = CamFor(aim, 0);
        if (cam == null) return false;
        var e = cam.Raycast(aim);
        if (probingAim && (e.IsEmpty() || e.EntityId != _tgt.Id)) AimMissed(_tgt);
        if (!e.IsEmpty() && e.EntityId == _tgt.Id)
        {
            if (Valid(e)) { AddContact(e, _tgt.Src == "IGC" ? "LOCK" : _tgt.Src); return true; }
            _tgt.DeadAt = _now; return false;
        }
        if (!e.IsEmpty() && Valid(e)) AddContact(e, "LOCK");   // something else hostile in the way: track it too
    }
    return false;
}
// Where to cast while the target is stale: a golden-angle spiral in the plane across the line of sight, centred on the
// predicted point; its radius covers the growing position uncertainty. The ray reaches a little past the point.
Vector3D ReacquirePoint(Track t, Vector3D centre, Vector3D dir, double age)
{
    double sigma = Uncertainty(t, age);
    _spiralIdx++;
    double rr = sigma * 1.5 * Math.Sqrt(((_spiralIdx % 17) + 0.5) / 17.0), th = _spiralIdx * 2.39996323;
    Vector3D u = Vector3D.Cross(dir, Math.Abs(dir.Y) < 0.95 ? Vector3D.Up : Vector3D.Right); u.Normalize();
    Vector3D v = Vector3D.Cross(dir, u);
    return centre + u * (rr * Math.Cos(th)) + v * (rr * Math.Sin(th)) + dir * Math.Min(t.Radius + sigma * 0.5, 25);
}

void AutoScanStep()
{
    double range = WeaponRange * 1.1;
    IMyCameraBlock cam = null;
    foreach (var c in _cams)
        if (c.IsWorking && Forwardish(c) && c.AvailableScanRange >= range && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    if (cam == null) return;
    _scanIdx++;
    // just lost a target: every other ray goes round where it was last seen, widening with time
    double lostAge = _now - _lostT;
    if (lostAge < 25 && (_scanIdx & 1) == 0)
    {
        Vector3D lp = _lostPos + _lostVel * Math.Min(lostAge, 10);
        double rr = Math.Min(250, _lostRad + 6 * lostAge) * Math.Sqrt(((_scanIdx >> 1) % 23 + 0.5) / 23.0), th2 = _scanIdx * 2.39996323;
        Vector3D dl = lp - cam.GetPosition(); double dd = dl.Length();
        if (dd > 30 && dd < range)
        {
            dl /= dd;
            Vector3D uu = Vector3D.Cross(dl, Math.Abs(dl.Y) < 0.95 ? Vector3D.Up : Vector3D.Right); uu.Normalize();
            Vector3D vv = Vector3D.Cross(dl, uu);
            var le = cam.Raycast(lp + uu * (rr * Math.Cos(th2)) + vv * (rr * Math.Sin(th2)) + dl * 10);
            if (Valid(le)) AddContact(le, "SCAN");
            return;
        }
    }
    const int N = 150;
    double r = ScanCone * Math.Sqrt((_scanIdx % N + 0.5) / N);
    double th = _scanIdx * 2.39996323;
    var e = cam.Raycast(range, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
    if (Valid(e)) AddContact(e, "SCAN");
}

void PruneAndSelect()
{
    _drop.Clear();
    foreach (var kv in _contacts)
    {   // the target I am working on stays alive as a ghost while MaintainLock hunts for it
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
    if (_tgt != null && _tgt.Src == "LOCK" && _mode == Mode.ASSIST) return;

    Vector3D me = _rc.GetPosition(), fwd = _rc.WorldMatrix.Forward, home = Home();
    Track best = null; double bestScore = double.MaxValue;
    foreach (var t in _contacts.Values)
    {
        Vector3D p = t.P(_now);
        if (_mode == Mode.GUARD && Vector3D.Distance(p, home) > GuardRadius) continue;
        Vector3D d = p - me; double dist = d.Length();
        double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(d / Math.Max(dist, 1), fwd), -1, 1)) * 57.2958;
        // a sniper prefers targets it can actually hit: big, slow, already in its accurate envelope
        double env = EngageRange(t);
        double score = Math.Abs(dist - env) * 0.5 + ang * 10 - t.Radius * 4;
        if (t == _tgt) score -= 600;
        score += Math.Max(0, _now - t.Seen - 1) * 150;   // a stale contact loses to a fresh one
        // enough slugs went into a small ship (a hit there usually cripples it, even when no block count shows it): look at the others
        if (t.ShotsAt >= (t.Radius < 12 ? 2 : 6) && _now - t.LastShotT < 90) score += 900;
        if (dist < 150) score -= 500;              // something right on top of us comes first
        score += HiveScore(t);
        if (score < bestScore) { bestScore = score; best = t; }
    }
    _tgt = best;
}

Vector3D Home()
{
    if (_mode != Mode.RTB && _tgt == null && _now - _assistT < 4) return _assist;   // go help a hive member under fire
    return (_now - _dynHomeT < 5) ? _dynHome : _home;
}

// =====================================================================
//  MAX ACCURATE ENGAGEMENT RANGE
//  After the shot, the target can accelerate in any direction with a.
//  Its unpredicted displacement after time t is 0.5*a*t^2 + vErr*t.
//  Solve for the time t at which that equals the allowed miss (k * radius),
//  then range = t * projectile speed, capped by the weapon's range.
double EngageRange(Track t)
{
    double cap = WeaponRange * RangeMargin;
    if (EngageCap > 0) cap = Math.Min(cap, EngageCap);
    if (ForcedRange > 0) return Math.Min(ForcedRange, WeaponRange * RangeMargin);
    double floor = t.Type == MyDetectedEntityType.SmallGrid ? DodgeSmall : t.Type == MyDetectedEntityType.LargeGrid ? DodgeLarge : DodgeChar;
    double a = Math.Max(floor, t.PeakAcc * 1.2);
    double miss = HitFraction * t.Radius;
    double tt = (-VelErr + Math.Sqrt(VelErr * VelErr + 2 * a * miss)) / a;
    // Measured (sim, both sides locked, 40 seeds x 7 opponents): wins of Longbow S by stand-off 350 m 45 %, 500 m 57 %, 700 m 69 %,
    // 900 m 75 %, 1100 m 77 %; the old "max accurate range" gave 73 %. A longer stand-off hits less often but takes far less fire.
    // So the accuracy formula only matters for big targets (it asks for more than PreferRange there).
    double r = Math.Max(tt * ProjSpeed, _slowWeapon ? PreferRange : 0);
    return MathHelper.Clamp(r, Math.Min(MinEngage, cap), cap);
}

// =====================================================================
//  POWER / HEALTH / GUN STATE
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
        if (load > 0.97) _powerScale = Math.Max(0.1, _powerScale * 0.9);
        else if (load < 0.88) _powerScale = Math.Min(1, _powerScale + 0.03);
    }
    long ammo = 0; int working = 0;
    foreach (var g in _guns)
    {
        if (!g.IsFunctional) continue;
        working++;
        var inv = g.GetInventory(0); if (inv != null) ammo += inv.CurrentVolume.RawValue;
    }
    foreach (var g in _gats)
    {
        if (!g.IsFunctional) continue;
        working++;
        var inv = g.GetInventory(0); if (inv != null) ammo += inv.CurrentVolume.RawValue;
    }
    _sabots = ammo > 0 ? 1 : 0;
    if (ammo > 0) _hadAmmo = true;
    if (_mode == Mode.GUARD || _mode == Mode.HUNT)
    {
        if (cap > 0 && _batFrac < RtbBattery) { _err = "Low battery - RTB"; _mode = Mode.RTB; }
        else if (working == 0 || (_hadAmmo && ammo <= 0)) { _err = "No weapon/ammo - RTB"; _mode = Mode.RTB; }
    }
}

long Ammo(IMyUserControllableGun g) { var inv = g.GetInventory(0); return inv != null ? inv.CurrentVolume.RawValue : 0; }

// Single-shot weapons: after the trigger, a drop in ammo confirms the shot and starts the reload timer.
// If nothing happens within 1 s the gun was still charging (or empty), so try again shortly.
void UpdateGuns()
{
    if (!_slowWeapon) return;
    foreach (var s in _gunState)
    {
        if (s.PendingAt < 0) continue;
        if (s.B.IsShooting) s.SawShoot = true;
        if (Ammo(s.B) < s.AmmoAtShot)
        {
            s.PendingAt = -1; s.LastShot = _now; s.ReadyAt = _now + ReloadTime * 1.1 + 0.5; _shots++;
            _last = "Shot " + _shots + " @ " + _engage.ToString("0") + " m";
        }
        else if (s.SawShoot && !s.B.IsShooting)
        {   // the gun was busy and is done: the slug is away (no ammo is used in a creative world)
            s.PendingAt = -1; s.LastShot = _now; s.ReadyAt = _now + ReloadTime + 0.5; _shots++;
            _last = "Shot " + _shots + " @ " + _engage.ToString("0") + " m (gun busy)";
        }
        else if (_now - s.PendingAt > ShotDelay + 1.0)
        {   // no ammo drop and the gun never reported busy: still charging - retry in 2 s
            s.PendingAt = -1; s.ReadyAt = _now + 2;
            _last = "Trigger sent (charging or creative)";
        }
    }
}

Gun ReadyGun()
{
    foreach (var s in _gunState)
        if (s.B.IsFunctional && s.B.IsWorking && s.PendingAt < 0 && _now >= s.ReadyAt) return s;
    return null;
}

double SecondsToReady()
{
    double best = double.MaxValue;
    foreach (var s in _gunState) if (s.B.IsFunctional) best = Math.Min(best, Math.Max(0, s.ReadyAt - _now));
    return best == double.MaxValue ? 0 : best;
}

// =====================================================================
//  CONTROL
void Control(double dt)
{
    var vels = _rc.GetShipVelocities();
    Vector3D myVel = vels.LinearVelocity, grav = _rc.GetNaturalGravity(), me = _rc.GetPosition();
    _myVel = myVel;
    _aimErrDeg = 0; _winDeg = 0;
    if (_mode == Mode.IDLE) { SetFire(false); return; }
    Vector3D up = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : Vector3D.Zero;
    if (_tgt != null) _engage = EngageRange(_tgt);
    double toReady = _slowWeapon ? SecondsToReady() : 0;
    bool pending = false; foreach (var gs in _gunState) if (gs.PendingAt >= 0) pending = true;
    bool aimingPhase = _tgt != null && (pending || (toReady < 3 && (!UnderFire || toReady < 0.5)));   // dodge when hit unless the shot is ready

    // ---------- aiming & fire control ----------
    bool haveAim = false;
    if (_tgt != null && _guns.Count > 0 && _mode != Mode.RTB)
    {
        Vector3D gunPos = Vector3D.Zero; int n = 0;
        foreach (var g in _guns) if (g.IsFunctional) { gunPos += g.GetPosition(); n++; }
        gunPos = n > 0 ? gunPos / n : me;
        Vector3D aimPoint, aimDir; double travel;
        haveAim = Lead(_tgt, gunPos, myVel, grav, out aimPoint, out aimDir, out travel);
        if (haveAim)
        {
            Vector3D los = Vector3D.Zero;
            if (_havePrev) los = Vector3D.Cross(_prevAim, aimDir) / dt;
            if (los.LengthSquared() > 1) los = Vector3D.Normalize(los);
            _prevAim = aimDir; _havePrev = true;
            Aim(aimDir, up, los, dt);

            Vector3D gunFwd = _aimRef.WorldMatrix.Forward;
            double dist = Vector3D.Distance(_tgt.P(_now), gunPos);
            _aimErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(gunFwd, aimDir), -1, 1)) * 57.2958;
            double aimR = _slowWeapon ? Math.Min(HitFraction * _tgt.Radius, (_tgt.Type == MyDetectedEntityType.LargeGrid || _tgt.Radius > 12) ? AimWindowMaxLarge : AimWindowMax) : (AimActive(_tgt) ? Math.Min(HitFraction * _tgt.Radius, 2.5) : HitFraction * _tgt.Radius);
            _winDeg = Math.Max(0.02, Math.Atan(aimR / Math.Max(dist, 1)) * 57.2958);
            bool inRange = travel <= WeaponRange * RangeMargin && (!_slowWeapon || travel <= _engage * FireRangeFactor + FireRangeSlack);
            Vector3D w = vels.AngularVelocity - los;
            bool steady = w.Length() < Math.Max(0.015, _winDeg / 57.2958 * 5);
            _settled = (_aimErrDeg < _winDeg && steady) ? _settled + 1 : 0;
            FireGatling(gunFwd, myVel);

            if (!_slowWeapon) SetFire(inRange && _aimErrDeg < _winDeg * 2 && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax);
            else if (inRange && _settled >= SettleTicks && _now > _holdFireUntil)
            {
                var gun = ReadyGun();
                if (gun == null) _calmSince = -1;
                else if (CalmAccel > 0 && _tgt.AccMag > CalmAccel && (_calmSince < 0 ? (_calmSince = _now) : _calmSince) > _now - CalmWait) { /* target is mid-manoeuvre: a moment later it may be on a straight line */ }
                else
                {
                    // pre-shot refresh: get a fresh raycast fix, fire on the next tick with it
                    if (_now - _tgt.Seen >= 0.1) MaintainLock(0);
                    else if (!FriendCheck || LineClear(gunPos, gunFwd, dist))
                    {
                        gun.AmmoAtShot = Ammo(gun.B); gun.PendingAt = _now; gun.SawShoot = false;
                        gun.B.ApplyAction("ShootOnce");
                        _tgt.ShotsAt++; _tgt.LastShotT = _now;
                        _settled = 0; _calmSince = -1;
                    }
                }
            }
        }
    }
    else _havePrev = false;
    if (haveAim) _sweepUpSet = false;

    if (!haveAim)
    {
        SetFire(false);
        if (_mode == Mode.ASSIST) ReleaseGyros();
        else
        {
            Vector3D fwd = _rc.WorldMatrix.Forward, toHome = Home() - me;
            if (_mode == Mode.RTB && toHome.LengthSquared() > 400) fwd = Vector3D.Normalize(toHome);
            else
            {   // radar sweep: yaw round and nod up/down so the camera cone covers all around
                double la = _now - _lostT;
                Vector3D lp = _lostPos + _lostVel * Math.Min(la, 10), dl = lp - me;
                if (_lostT > 0 && la < 12 && dl.LengthSquared() > 900) fwd = Vector3D.Normalize(dl);   // turn the nose cameras to where the last target was
                else if (AutoScan) fwd = SweepDir(up);
            }
            Aim(fwd, up, Vector3D.Zero, dt);
        }
    }

    // ---------- translation ----------
    if (_mode == Mode.ASSIST) return;
    double mass = _rc.CalculateShipMass().PhysicalMass;
    double aBrake = MinAccel(mass, grav) * 0.5;
    Vector3D want, home = Home();
    if (_mode == Mode.RTB || _tgt == null)
    {
        Vector3D d = home - me; double dist = d.Length();
        double v = Math.Min(Math.Min(CruiseSpeed, dist * 0.5), Math.Sqrt(2 * aBrake * dist));
        want = (dist > 0.5 ? d / dist * v : Vector3D.Zero) + Evade() + Separation(me);
        if (_mode == Mode.RTB && dist < 25 && myVel.Length() < 2) { SetMode(Mode.IDLE); _err = "Home - idle"; return; }
    }
    else
    {
        Vector3D tp = _tgt.P(_now), tv = _tgt.V(_now);
        Vector3D d = tp - me; double dist = d.Length();
        Vector3D dir = dist > 1 ? d / dist : _rc.WorldMatrix.Forward;
        double e = dist - _engage;
        double vr = Math.Sign(e) * Math.Min(Math.Min(Math.Abs(e) * 0.4, MaxSpeed), Math.Sqrt(2 * aBrake * Math.Abs(e)));
        Vector3D upRef = up.LengthSquared() > 0 ? up : _rc.WorldMatrix.Up;
        Vector3D tan = Vector3D.Cross(dir, upRef);
        if (tan.LengthSquared() < 1e-4) tan = _rc.WorldMatrix.Right;
        tan.Normalize();
        Vector3D evade = Vector3D.Zero;
        if (!aimingPhase)
        {   // reloading: be hard to hit
            if (_now > _nextJink)
            {
                _nextJink = _now + 1.5 + _rng.NextDouble() * 2.5;
                if (_rng.NextDouble() < 0.3) _driftSign = -_driftSign;
                Vector3D rnd = new Vector3D(_rng.NextDouble() - 0.5, _rng.NextDouble() - 0.5, _rng.NextDouble() - 0.5);
                rnd -= Vector3D.Dot(rnd, dir) * dir;
                _jink = rnd.LengthSquared() > 1e-6 ? Vector3D.Normalize(rnd) * JinkSpeed : Vector3D.Zero;
            }
            evade = _jink;
        }
        want = tv + dir * vr + tan * DriftSpeed * _driftSign + evade + (aimingPhase ? Vector3D.Zero : Evade()) + Separation(me);
        if (_mode == Mode.GUARD && Vector3D.Distance(me, home) > GuardRadius * 1.3) want = Vector3D.Normalize(home - me) * CruiseSpeed;
    }
    double cap = (_tgt != null && _mode != Mode.RTB) ? MaxSpeed : CruiseSpeed;
    if (want.LengthSquared() > cap * cap) want = Vector3D.Normalize(want) * cap;
    // gentler velocity loop while settling a shot, so thrust kicks don't shake the aim
    Vector3D acc = (want - myVel) * (aimingPhase ? 1.0 : 2.0);
    if (acc.LengthSquared() > 900) acc = Vector3D.Normalize(acc) * 30;
    ApplyThrust((acc - grav) * mass);
}

// Intercept with target acceleration and projectile gravity drop.
// Shooter frame: s*t*dir = rp + rv*t + 0.5*(aT - g)*t^2
bool Lead(Track t, Vector3D shooter, Vector3D shooterVel, Vector3D grav, out Vector3D point, out Vector3D dir, out double travel)
{
    // NOTE on the railgun's shot delay (0.5 s small / 2 s large between the trigger and the slug leaving the barrel): the gun
    // keeps tracking this very solution until the slug is away, so the direction it fires in is the solution of the moment
    // of firing. Leading the target by the delay on top of that over-leads by exactly the delay (measured: hit rate 85 % -> 5 %).
    // The delay costs us elsewhere: the target has delay + flight time to change its mind (EngageRange, CalmGate).
    Vector3D tp = t.P(_now) + AimOffset(t), tv = t.V(_now);
    Vector3D tacc = UseTargetAccel ? t.EA() : Vector3D.Zero;
    Vector3D rp = tp - shooter;
    Vector3D rv = InheritVel ? tv - shooterVel : tv;
    double s = ProjSpeed;
    double a = rv.LengthSquared() - s * s, b = 2 * Vector3D.Dot(rp, rv), c = rp.LengthSquared();
    double tt;
    double disc = b * b - 4 * a * c;
    if (Math.Abs(a) < 1e-6) tt = b != 0 ? -c / b : rp.Length() / s;
    else if (disc < 0) tt = rp.Length() / s;
    else
    {
        double sq = Math.Sqrt(disc), t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a);
        tt = (t1 > 0 && (t1 < t2 || t2 <= 0)) ? t1 : t2;
    }
    if (tt <= 0 || double.IsNaN(tt)) tt = rp.Length() / s;
    Vector3D p = rp;
    Vector3D g = GravityDrop ? grav : Vector3D.Zero;
    for (int i = 0; i < 6; i++) { p = rp + (rv * tt + 0.5 * tacc * tt * tt) * LeadScale - 0.5 * g * tt * tt; tt = p.Length() / s; }
    _leadT = tt; travel = tt * s;
    _drop2 = GravityDrop ? 0.5 * grav.Length() * tt * tt : 0;
    point = shooter + p;
    double len = p.Length();
    if (len < 1) { dir = Vector3D.Zero; return false; }
    dir = p / len;
    return true;
}

// Wingmates repel each other (all fly the same orbit at the same speed and would otherwise end up in contact). Uses the
// positions the hive already broadcasts (_SWARM). Large grids keep a wider berth.
double SepRange = 80, SepSpeed = 25;
Vector3D Separation(Vector3D me)
{
    Vector3D push = Vector3D.Zero;
    double rng = SepRange * (Me.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 2.5 : 1.0);
    foreach (var m in _mates.Values)
    {
        double age = _now - m.Seen;
        if (age > 1.5) continue;
        Vector3D d = me - (m.Pos + m.Vel * age);
        double len = d.Length();
        if (len < rng && len > 0.5) push += d / len * (SepSpeed * (1 - len / rng));
    }
    return push;
}

// Friendly-fire gate from the IGC positions of the hive (works with no camera charge): the projectile leaves the barrel
// 'delay' s after the trigger with the ship's velocity added, so both it and the mate are extrapolated linearly and the
// closest approach of the two paths is tested against the mate's size. A piercing slug flies on past the target.
Vector3D _myVel; double _calmSince = -1;
bool MatesInLine(Vector3D gunPos, Vector3D gunFwd, double dist, double speed, double delay)
{
    double clear = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 22 : 9;
    double tmax = (dist + 150) / Math.Max(speed, 1);
    foreach (var m in _mates.Values)
    {
        double age = _now - m.Seen;
        if (age > 1.5) continue;
        Vector3D r0 = (gunPos + _myVel * delay) - (m.Pos + m.Vel * (age + delay));
        Vector3D u = _myVel + gunFwd * speed - m.Vel;
        double uu = u.LengthSquared();
        double tau = uu > 1e-6 ? MathHelper.Clamp(-Vector3D.Dot(r0, u) / uu, 0, tmax) : 0;
        if ((r0 + u * tau).LengthSquared() < clear * clear)
        { _holdFireUntil = _now + 0.4; _err = "Holding fire: wingmate in line"; return true; }
    }
    return false;
}

// One camera ray along the line of fire; a friendly (not in the hive) in the way holds the fire.
bool FriendOnRay(Vector3D gunPos, Vector3D gunFwd, double dist, out bool looked)
{
    looked = false;
    Vector3D probe = gunPos + gunFwd * Math.Max(dist - _tgt.Radius, 10);
    var cam = CamFor(probe, dist + 10);
    if (cam == null) return false;
    looked = true;
    var e = cam.Raycast(probe);
    if (!e.IsEmpty() && e.EntityId != _tgt.Id && Friendly(e) && e.EntityId != Me.CubeGrid.EntityId)
    { _holdFireUntil = _now + 1.5; _err = "Holding fire: friendly in line"; return true; }
    return false;
}

// Railgun: true = the shot may go. No camera charge for the ray: wait up to 1 s for it (a railgun shot is worth waiting
// for), then go ahead; the hive check above never fails open.
double _lineWaitSince = -1;
bool LineClear(Vector3D gunPos, Vector3D gunFwd, double dist)
{
    if (MatesInLine(gunPos, gunFwd, dist, ProjSpeed, _slowWeapon ? ShotDelay : 0)) return false;
    bool looked;
    if (FriendOnRay(gunPos, gunFwd, dist, out looked)) { _lineWaitSince = -1; return false; }
    if (looked) { _lineWaitSince = -1; return true; }
    if (_lineWaitSince < 0 || _now - _lineWaitSince > 4) _lineWaitSince = _now;
    if (_now - _lineWaitSince < 1.0) return false;
    _lineWaitSince = -1;
    return true;
}

void Aim(Vector3D desiredFwd, Vector3D desiredUp, Vector3D ffWorld, double dt)
{
    if (_gyros.Count == 0 || desiredFwd.LengthSquared() < 1e-9) return;
    MatrixD refM = _aimRef.WorldMatrix, inv = MatrixD.Transpose(refM);
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
            err += f * (roll * 0.5);
        }
    }
    Vector3D derr = (err - _prevErr) / dt;
    if (derr.LengthSquared() > 400) derr = Vector3D.Zero;
    _prevErr = err;
    Vector3D w = err * AimKp + derr * AimKd;
    Vector3D wWorld = Vector3D.TransformNormal(w, refM) + ffWorld;
    if (wWorld.LengthSquared() > MaxTurn * MaxTurn) wWorld = Vector3D.Normalize(wWorld) * MaxTurn;
    foreach (var g in _gyros)
    {
        if (!g.IsFunctional) continue;
        Vector3D l = Vector3D.TransformNormal(wWorld, MatrixD.Transpose(g.WorldMatrix));
        g.Pitch = -(float)l.X; g.Yaw = -(float)l.Y; g.Roll = -(float)l.Z;   // SE gyro sign convention
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
    return Math.Max(0.3, min * _powerScale / mass - grav.Length());
}

void ApplyThrust(Vector3D forceWorld)
{
    _rc.DampenersOverride = false;
    MatrixD m = _rc.WorldMatrix;
    double fw = Vector3D.Dot(forceWorld, m.Forward), rt = Vector3D.Dot(forceWorld, m.Right), upc = Vector3D.Dot(forceWorld, m.Up);
    double[] comp = { fw, -fw, -rt, rt, upc, -upc };
    for (int i = 0; i < 6; i++)
    {
        double max = 0;
        foreach (var t in _thrDir[i]) if (t.IsWorking) max += t.MaxEffectiveThrust;
        float pct = (comp[i] > 0 && max > 0) ? (float)MathHelper.Clamp(comp[i] / max * _powerScale, 0, 1) : 0f;
        if (pct > 0 && pct < 0.002f) pct = 0.002f;
        foreach (var t in _thrDir[i]) t.ThrustOverridePercentage = pct;
    }
}

// ---- gatling group: its own lead solution (bullets at GatSpeed from the gatling's own position), continuous fire
bool _gatFiring = false; double _gatErrDeg = 0, _gatWinDeg = 0, _gatDist = 0;
void SetGat(bool on)
{
    if (on == _gatFiring && !(on && _tick % 30 == 0)) return;   // re-send "on" twice a second
    _gatFiring = on;
    foreach (var g in _gats) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off");
}

void FireGatling(Vector3D gunFwd, Vector3D myVel)
{
    _gatErrDeg = 0; _gatWinDeg = 0;
    if (!UseGatling || _gats.Count == 0 || _tgt == null) { SetGat(false); return; }
    Vector3D gp = Vector3D.Zero; int n = 0;
    foreach (var g in _gats) if (g.IsFunctional) { gp += g.GetPosition(); n++; }
    if (n == 0) { SetGat(false); return; }
    gp = gp / n;
    Vector3D tp = _tgt.P(_now) + AimOffset(_tgt), tv = _tgt.V(_now), ta = UseTargetAccel ? _tgt.EA() : Vector3D.Zero;
    Vector3D rp = tp - gp, rv = InheritVel ? tv - myVel : tv;
    double s = Math.Max(GatSpeed, 100);
    double a = rv.LengthSquared() - s * s, b = 2 * Vector3D.Dot(rp, rv), c = rp.LengthSquared(), tt;
    double disc = b * b - 4 * a * c;
    if (Math.Abs(a) < 1e-6) tt = b != 0 ? -c / b : rp.Length() / s;
    else if (disc < 0) tt = rp.Length() / s;
    else { double sq = Math.Sqrt(disc), t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a); tt = (t1 > 0 && (t1 < t2 || t2 <= 0)) ? t1 : t2; }
    if (tt <= 0 || double.IsNaN(tt)) tt = rp.Length() / s;
    Vector3D p = rp;
    for (int i = 0; i < 4; i++) { p = rp + (rv * tt + 0.5 * ta * tt * tt) * LeadScale; tt = p.Length() / s; }
    double dist = p.Length();
    if (dist < 1 || double.IsNaN(dist)) { SetGat(false); return; }
    _gatDist = dist;
    _gatErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(gunFwd, p / dist), -1, 1)) * 57.2958;
    double aimR = AimActive(_tgt) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6;
    _gatWinDeg = Math.Max(0.3, Math.Atan(aimR / dist) * 57.2958) * GatTol;
    bool shoot = dist < GatRange && _gatErrDeg < _gatWinDeg && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax;
    if (shoot && FriendCheck)
    {
        MatesInLine(gp, gunFwd, dist, GatSpeed, 0);
        bool looked; if (_tick % 20 == 0) FriendOnRay(gp, gunFwd, dist, out looked);
    }
    if (_now < _holdFireUntil) shoot = false;
    SetGat(shoot);
}

void SetFire(bool on)
{
    if (!on) SetGat(false);
    if (on == _firing && !(on && _tick % 30 == 0)) return;   // re-send "on" twice a second
    _firing = on;
    foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off");
}

void ReleaseGyros() { foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = g.Yaw = g.Roll = 0; } _havePrev = false; }

void Release()
{
    SetGunsEnabled(true);   // (the friendly-fire guard of the AI phase may have switched them off)
    ReleaseGyros();
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
    SetFire(false);
}

// =====================================================================
void Display()
{
    _sb.Clear();
    _sb.Append("MANTIS HYBRID AI v").Append(VER).Append('\n');
    _sb.Append("Mode   : ").Append(_mode).Append("   ").Append(_wName).Append('\n');
    if (_tgt != null)
    {
        double dist = _rc != null ? Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()) : 0;
        _sb.Append("Target : ").Append(_tgt.Type == MyDetectedEntityType.SmallGrid ? "SMALL " : _tgt.Type == MyDetectedEntityType.LargeGrid ? "LARGE " : "").Append(_tgt.Src)
           .Append(" r=").Append(_tgt.Radius.ToString("0")).Append("m\n");
        _sb.Append("Range  : ").Append(dist.ToString("0")).Append(" / engage ").Append(_engage.ToString("0")).Append(ForcedRange > 0 ? " (forced)" : " (auto)").Append('\n');
        _sb.Append("Flight : ").Append(_leadT.ToString("0.00")).Append(" s  drop ").Append(_drop2.ToString("0.0")).Append(" m\n");
        _sb.Append("Aim    : ").Append(_aimErrDeg.ToString("0.000")).Append(" / ").Append(_winDeg.ToString("0.000")).Append(" deg  settle ").Append(Math.Min(_settled, SettleTicks)).Append('/').Append(SettleTicks).Append('\n');
    }
    else _sb.Append("Target : none (").Append(_contacts.Count).Append(" contacts)\n\n\n\n");
    if (_gats.Count > 0) _sb.Append("Gatling: ").Append(_gatFiring ? "FIRING " : "hold ").Append(_gatErrDeg.ToString("0.00")).Append(" / ").Append(_gatWinDeg.ToString("0.00")).Append(" deg  ").Append(_gats.Count).Append(" gun(s)\n");
    if (_slowWeapon)
    {
        double r = SecondsToReady();
        _sb.Append("Weapon : ").Append(r <= 0 ? "READY" : "charging " + r.ToString("0") + "s").Append("  shots ").Append(_shots).Append('\n');
    }
    _sb.Append("Battery: ").Append((_batFrac * 100).ToString("0")).Append("%  ammo ").Append(_hadAmmo ? (_sabots > 0 ? "yes" : "EMPTY") : "none loaded").Append('\n');
    _sb.Append("Thrust cap: ").Append((_powerScale * 100).ToString("0")).Append("%\n");
    double scan = 0; foreach (var c in _cams) scan += c.AvailableScanRange;
    _sb.Append("Sens ").Append(_sensors.Count).Append("  Cams ").Append(_cams.Count).Append(" charge ").Append((scan / 1000).ToString("0.0")).Append(" km\n");
    _sb.Append("Run    : ").Append(_avgMs.ToString("0.000")).Append(" ms\n");
    _sb.Append("Ins    : ").Append(Runtime.CurrentInstructionCount).Append(" / ").Append(Runtime.MaxInstructionCount).Append("  peak ").Append(_peakIns).Append('\n');
    if (_last != "") _sb.Append(_last).Append('\n');
    if (_sensors.Count > 0 && _sensorSetupMissing > 0) _sb.Append("! Sensor: set ranges to max + Detect Enemy\n");
    _sb.Append(HiveStatus());
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
    // its neighbours are probably close by: search round the spot with the cameras before the AI blocks (which would point
    // at the nearest enemy, i.e. this wreck) take over
    _lostPos = t.Pos; _lostVel = Vector3D.Zero; _lostT = _now; _lostRad = Math.Max(t.Radius, 30); _ignoreNoteT = _now;
    // radius -1 = "this one is finished" for every drone (and stays compatible with the carrier link)
    if (tell) { var fin = new MyTuple<long, Vector3D, Vector3D, double, long>(t.Id, t.Pos, t.Vel, -1, Me.EntityId); IGC.SendBroadcastMessage(Tag + "_TGT", fin); FleetSend(fin); }
}

// Finished = no longer an enemy (its owned blocks are gone), blown down to under 45% of the
// largest size seen, or tumbling out of control for 4 s.
bool Disabled(Track t)
{
    if (t.DeadAt > 0) return true;
    if (t.Spin > 0.8) { if (t.SpinSince < 0) t.SpinSince = _now; } else t.SpinSince = -1;
    // a small ship I have already put a slug into that no longer accelerates at all is a wreck, even if most of it is still there
    if (t.ShotsAt >= 1 && t.Type == MyDetectedEntityType.SmallGrid && t.QuietSince > 0 && _now - t.QuietSince > 6 && t.Src != "IGC" && (t.Vel.LengthSquared() > 4 || (t.Rad0 > 3 && t.Radius < 0.9 * t.Rad0))) return true;   // (a ship hovering motionless is not a wreck unless it also lost size)
    return (t.SpinSince > 0 && _now - t.SpinSince > 4) || (t.Rad0 > 3 && t.Radius < 0.45 * t.Rad0);
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
        // a mate lost its lock on my target: answer at once with a fresh fix instead of waiting for the next cycle
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
// AUTO: a small-grid target is shot at its centre (a piercing slug / burst through the middle of a compact ship crosses its
// battery, PB, controller and engines; measured in the sim: 52 % -> 82 % wins), a large grid at real hull points (its
// bounding-box centre is often empty space, in-game finding of v1.6). The other modes always use hull points.
bool AimActive(Track t)
{
    if (AimMode == "CENTER" || t.Hull.Count < 3) return false;
    if (AimMode == "AUTO" && t.Type == MyDetectedEntityType.SmallGrid) return false;
    return true;
}
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
// While the AI blocks have the controls they also pull the trigger at whatever is in front of the nose, wingmates included
// (found in the sim: 13,000-50,000 damage slugs into the neighbour 70 m away during the first seconds). So: switch the guns
// off while a hive mate is in the line of fire, back on as soon as it is clear or the script takes over again.
bool _gunsOff = false; double _friendRay = -99;   // last time an AI confirm ray hit a friendly (not necessarily in my hive)
void SetGunsEnabled(bool on)
{
    if (on != _gunsOff) return;
    _gunsOff = !on;
    foreach (var g in _guns) if (g.IsFunctional) g.Enabled = on;
    foreach (var g in _gats) if (g.IsFunctional) g.Enabled = on;
}
void AiFriendGuard()
{
    if (!FriendCheck || _aimRef == null) return;
    _myVel = _rc.GetShipVelocities().LinearVelocity;
    SetGunsEnabled(!(MatesInLine(_aimRef.GetPosition(), _aimRef.WorldMatrix.Forward, WeaponRange, ProjSpeed, 0) || _now - _friendRay < 1.5));
}
bool AiStep()
{
    if (_tgt != null) _lastTgtT = _now;
    // The AI also takes over while my lock is stale (nothing seen for AiStaleTakeover s): it keeps the nose on the enemy
    // and the confirm rays below pick it up again, which hands control straight back to the script.
    bool want = AiAssist && _ai.Count > 0 && AiModeOk() &&
        ((_tgt == null && _now - _lastTgtT > 2 && _now - _ignoreNoteT > 15) || (_tgt != null && _now - _tgt.Seen > (Vector3D.DistanceSquared(_tgt.Pos, _rc.GetPosition()) > 1400.0 * 1400.0 ? AiStaleTakeoverFar : AiStaleTakeover)));
    if (want != _aiOn)
    {
        _aiOn = want;
        _firing = false; _gatFiring = false;                       // guns' state is unknown after any hand-over: SetFire sends it again
        if (want) Release();                    // thrusters and gyros go to the AI
        foreach (var b in _ai) b.Enabled = want;
    }
    if (!_aiOn) { SetGunsEnabled(true); if (_tick % 300 == 7) foreach (var g in _guns) if (g.IsFunctional && !g.Enabled) g.Enabled = true; return false; }   // (also heals guns left disabled by a script restart during the AI phase)
    AiFriendGuard();
    // The AI points the guns at what it found (about 0.1 deg off), so the enemy is dead ahead of the nose cameras.
    // Every forward camera casts as soon as it holds AiConfirmRange of charge. Pattern: 1 ray in 2 exactly down the
    // boresight, the rest in a spiral widening from 0.15 to 2.5 deg (a small ship is only 0.2-0.8 deg wide at 300-1200 m;
    // the old pattern never cast closer than 0.59 deg). The first forward camera is the long one (AiConfirmLong): it waits
    // for the charge to find a target the AI already holds at 1.3-2.6 km.
    bool longCam = true;
    foreach (var c in _cams)
    {
        if (!c.IsWorking || !Forwardish(c)) continue;
        double len = longCam ? AiConfirmLong : AiConfirmRange;
        longCam = false;
        if (c.AvailableScanRange < len) continue;
        _aiScan++;
        double r = 0, th = _aiScan * 2.39996323;
        if ((_aiScan & 1) == 1) { int j = (_aiScan >> 1) % 16; r = 0.15 + 2.35 * Math.Pow((j + 0.5) / 16.0, 1.5); }
        var e = c.Raycast(len, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
        if (Valid(e)) AddContact(e, "AI");
        else if (!e.IsEmpty() && Friendly(e) && e.EntityId != Me.CubeGrid.EntityId) _friendRay = _now;
    }
    return true;
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
//  * "<Tag>_JOIN" from a printer: an IDLE drone (fresh off the printer) starts working; drones that
//    are already busy ignore it.
IMyCubeGrid _myGrid;
IMyBroadcastListener _lJoin;
void CheckGrid() { if (Me.CubeGrid != _myGrid) { _myGrid = Me.CubeGrid; GetBlocks(); } }
void ReadJoin()
{
    while (_lJoin.HasPendingMessage)
    {
        _lJoin.AcceptMessage();
        if (_mode == Mode.IDLE && !Docked()) { GetBlocks(); if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } SetMode(Mode.GUARD); }
    }
}
// still latched to a printer?
bool Docked()
{
    var merges = new List<IMyShipMergeBlock>();
    GridTerminalSystem.GetBlocksOfType(merges, b => b.CubeGrid == Me.CubeGrid);
    foreach (var m in merges) if (m.IsConnected) return true;
    return false;
}
