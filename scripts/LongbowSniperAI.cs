// =====================================================================
//  LONGBOW SNIPER AI  v2.3  - long-range railgun drone controller
//  Space Engineers (vanilla). Works on small grid (Small Railgun, 1400 m)
//  and large grid (Large Railgun, 2000 m). Paste into a Programmable Block.
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
//  v2.0 (round 2, sim; details and numbers in docs/script-fix-notes.md "Round 2: longbow-mantis")
//   * Aim: the acceleration used for the lead is the one of the last fix interval (Fresh Acceleration Weight 0.8, scale 0.9), not the
//     smoothed and steadiness-gated one (offline on 2,285 sample shots at a jinking Hornet-class target: 36 % -> 55-59 % within 1.5 m).
//   * Power: thrust is served before the railgun recharge, so the thrust throttle (which keeps the recharge at full speed) is lifted while the
//     ship has to travel more than 200 m to its stand-off (Full Thrust While Travelling).
//   * Railgun fire control: a held trigger (Shoot_On left by an AI block) is released, the next attempt comes when the capacitor should be
//     full (ReloadTime after the slug left) and a refused trigger is asked again after 0.5 s; until the script has fired its own first slug the
//     railgun is switched off while the AI blocks search with nothing locked, so the first (fully charged) slug is not spent at 1+ km by the
//     AI (Railgun Off While AI Searches); the thrusters are off while the shot is pending and 0.25 s before the trigger (No Thrust Around The Shot: off-centre thrust
//     turns the ship; with the sim's thruster-torque option a Longbow S goes 35 % -> 63 %).
//  v1.9 (sim-audit pass; details and numbers in docs/script-fix-notes.md)
//   * AI hand-over: confirm rays really cover the boresight (1 in 2 exactly down the nose), one long ray (2.6 km) + short
//     ones (1.3 km); the AI also takes over when the lock has been stale 3 s (1 s beyond 1.4 km).
//   * Lock keeping: cheaper rays, hull-point probes, a stale lock becomes a "ghost" (8 s) searched with 2 rays/tick on a
//     widening spiral; the loss is announced to the hive; after a target is dropped the nose turns to where it was.
//   * Railgun: the shot delay (0.5 s small / 2 s large) is NOT added to the lead (the gun keeps tracking until the slug leaves, so
//     the solution of the moment of firing is the right one; adding it over-led and cut the hit rate from 85 % to 5 %); the ship
//     holds steady while a shot is pending. Accepted shots are confirmed with IsShooting (jinking and the reload timer work
//     without an ammo drop). Stand-off 1000 m (Preferred Engage Range) instead of the "max accurate range" of 350-500 m,
//     fire only inside stand-off x1.15 + 50 m, aim window capped at 2 m (6 m on big targets), centre aim for small grids (hull points for large ones).
//   * Hive: wingmate separation, friendly-fire gate from IGC positions (no camera needed; also while the AI blocks fly),
//     shot-count based target switching, wrecks (small ship hit, no longer manoeuvring) are dropped.
//
//  v2.3 (LEAD v1.0, shared block, docs/lead-v1.md): the lead solver predicts the target with its freshest acceleration, held only
//   for as long as its manoeuvres usually last (an AI ship on evasive flips its strafe every 2-5 s); the hull aim point counts only
//   across the line of fire, turns with the target's spin, and keeps "Aim Margin" x the predicted miss of known hull around it
//
//  COMMANDS (argument, toolbar, or IGC "<Tag>_CMD")
//   guard | follow | hunt | assist | rtb | idle | lock | unlock | home | reload
//   range <m>   force an engagement range (0 = automatic)
//   follow (or escort)   stay with the owner: a ship running a Mothership link, or you on foot near the drone ([Follow] section)
//   satellite (sat, orbit)   FOLLOW, but the drones spread round a slowly turning vertical ring centred on the owner (ring size grows with their number); [Follow] section
//  KAMIKAZE DEFENCE (v2.4; shared block tools/kz_block.cs, docs/kamikaze-defence.md): incoming swarms of small warhead drones are
//   never written off as wrecks, become the priority target, and the drone backs away while firing and breaks sideways just
//   before impact; spare camera rays track the swarm and the tracks are shared with the hive. Own [Kamikaze Defence] Custom Data section.
// =====================================================================

const string VER = "2.4";

// ---------------- settings (Custom Data) ----------------
string Tag = "LONGBOW";
string StartMode = "IDLE";
double ProjSpeed = 1000, WeaponRange = 1400, ReloadTime = 20; // used for unknown weapons
double ShotDelayCfg = 0;     // trigger -> projectile leaves the barrel, unknown weapons (railgun data: 0.5 s small, 2 s large)
double ShotDelay = 0;        // the value in use (set by DetectWeapon)
bool GravityDrop = true, InheritVel = true, AutoWeapon = true;
double HitFraction = 0.5;    // shot must land within this fraction of target radius
double VelErr = 0.3;         // m/s assumed velocity estimate error
double DodgeSmall = 8, DodgeLarge = 3, DodgeChar = 4; // assumed evasive accel floors m/s^2
double RangeMargin = 0.95;   // never plan shots beyond this fraction of weapon range
double MinEngage = 350;      // closest the sniper will choose to fight
double ForcedRange = 0;      // >0 overrides automatic range
double RushOrbit = 60, RushRange = 4000, DodgeRange = 500, RushSpeed = 100;   // anti-rush: a big ship charging us (see the translation block)
double GuardRadius = 2500, MaxSpeed = 90, CruiseSpeed = 50, DriftSpeed = 6, JinkSpeed = 15;
double LockRange = 3000, ScanCone = 25;
double CloseScanRange = 200; // all-around camera scan distance
bool CloseScan = true;
double AimKp = 5, AimKd = 0.4, MaxTurn = 2.5;
int SettleTicks = 8;
double CalmAccel = 0, CalmWait = 2.5;   // railgun: wait up to CalmWait s for a moment when the target is not accelerating harder than CalmAccel m/s^2 (0 = off)
double AimWindowMaxLarge = 6.0;   // ... for a big target (large grid / radius over 12 m), where the hull is wide and every hit cripples something
double AimWindowMax = 2.0;    // railgun: never accept an aim error worth more than this many metres at the target (the whole sphere is far too generous)
double PreferRange = 1000;   // stand-off the sniper likes against any target (gatling ships cannot reach it, a slug flies 1 s)
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
enum Mode { IDLE, GUARD, HUNT, ASSIST, RTB, FOLLOW }
Mode _mode = Mode.IDLE;
MyIni _ini = new MyIni();
double _now = 0; long _tick = 0;
Vector3D _home; bool _homeSet = false, _ready = false;
Vector3D _dynHome; double _dynHomeT = -99;

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
IMyBroadcastListener _lTgt, _lCmd, _lHome;
class Gun
{
    public IMyUserControllableGun B;
    public double ReadyAt = 0, PendingAt = -1, LastShot = -999; public long AmmoAtShot; public bool SawShoot;
}
List<Gun> _gunState = new List<Gun>();

static double AccFastW = 0.8, AccFastK = 0.9;   // 0 = the old smoothed, gated acceleration
class Track
{
    public long Id; public Vector3D Pos, Vel, Acc; public double AccMag;
    public Vector3D LAcc, LSeg, LW, LOR; public int LSegN; public double LSegT0 = -99, LManT = 2;   // LEAD v1.0 state (fresh acceleration, manoeuvres, spin)
    public double Spin, SpinSince = -1, Rad0, DeadAt = -1, DisSince = -1; public Vector3D OF, OU; public bool HasOri;
    public MatrixD O = MatrixD.Identity; public List<Vector3D> Hull = new List<Vector3D>(); public int HullNext;
    public Vector3D Thrust, AimLocal; public double AimT = -99;
    public Vector3D AccF;   // fresh acceleration: the last intervals only (the smoothed Acc lags a jinking target by about a second)
    public int ShotsAt, NSet; public double LastShotT = -999, QuietSince = -1;   // slugs I have fired at it; since when it has not accelerated
    // how fast it's rotating, from orientation changes between sightings
    public void Ori(MatrixD o, double dt)
    {
        LeadSpin(this, o, dt);
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
        LeadSample(this, v, now, now - Seen);
        double dt = now - Seen;
        if (Seen > 0 && dt > 0.04 && dt < 2)
        {
            Vector3D a = (v - Vel) / dt;
            if (a.LengthSquared() > 10000) a = Vector3D.Normalize(a) * 100;
            Acc = Acc * 0.8 + a * 0.2;
            AccF = AccF * (1 - AccFastW) + (a.LengthSquared() > 900 ? Vector3D.Normalize(a) * 30 : a) * AccFastW;
            AccMag = AccMag * 0.8 + a.Length() * 0.2;
            // which way (in its own frame) it usually pushes; its main engines sit on the opposite side
            if (HasOri && a.LengthSquared() > 1) Thrust = Thrust * 0.9 + Vector3D.TransformNormal(Vector3D.Normalize(a), MatrixD.Transpose(O)) * 0.1;
        }
        else if (dt >= 2) { Acc = Vector3D.Zero; AccMag = 0; AccF = Vector3D.Zero; }
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
        if (AccFastW > 0)
        {   // measured on a jinking Hornet-class target (offline, 2,285 sample shots at 500-1300 m): constant velocity 29 % within 1.5 m, smoothed and
            // gated acceleration 36 %, the acceleration of the last 0.1-0.25 s 55-59 %
            if (AccF.LengthSquared() < 0.25) return Vector3D.Zero;
            return AccF * AccFastK;
        }
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
bool _shootFlag = false;   // a shot has been confirmed through IsShooting at least once: the gun reports it
bool Coast = true; double _coastOpen = -99;
double ReadyLead = 0.0, RetryAfter = 0.5;   // next attempt this long before the nominal recharge is over / after a refused trigger
// Thrust is served before the railgun's recharge (power groups: Thrust 80, DefenseAdative 90), so an unthrottled ship slows the recharge down
// and a throttled one cannot move. _powerScale throttles while the ship sits still; it is let go while the ship has to travel or dodge.
bool FreeFar = true; double FreeFarDist = 200, _needMoveUntil = -99;
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

// >>> AI GATE v1.1 (shared block: identical in every drone script that uses AI-block search, synced by tools/ai_gate_sync.py) >>>
// The AI blocks tagged [AI] (AI Offensive + AI Flight) come out of a blueprint with "AI Behavior" off and every setting at its
// default, so switching them on (Enabled) may do nothing at all. In-game report 2026-10-13: Full drones in GUARD stood still and
// HUNT drones never saw a pirate 700 m away, because the script handed the ship to the AI blocks and stopped its own search.
// The gate:
//  * tries to switch "AI Behavior" on (a bool terminal property whose id contains "Behavior"; the exact id is not documented);
//  * until the AI blocks have shown that they work (flown the ship once: sped it up or turned it), gives them the ship for
//    AI Window seconds, then runs the script's own search (camera sweep + scan rays) for Script Search Window seconds, and so on;
//    under fire the script searches. While the AI flies the ship it keeps it, up to Max AI Hold seconds. Once the AI has flown the
//    ship it is trusted and gets the ship whenever the script wants it, as before v2.1 (sim: time-sharing a working AI cost most of
//    its kills, 1v1 decisive 90 % -> 35 %).
// v1.1 (opt-in, audit F-03/SL-11, default OFF = the behaviour of v1.0): "Use Typed AI Block API=true" sets IMyFlightMovementBlock.SpeedLimit
// ("AI Flight Speed Limit (m/s)") on the AI Flight blocks and counts a block as switched on when IsAutoPilotEnabled reads true instead of
// when the "Behavior" terminal property was found. Rests on the ModAPI documentation only (not tested in the game; unit of SpeedLimit unconfirmed).
// Own [AI Gate] section in Custom Data (written on first run, "reload" re-reads it).
// Hooks: AiGateLoad() after FleetLoad(true); AiGateFind(_ai) at the end of FindAi(); want = AiGate(want) right before
// "if (want != _aiOn)" in AiStep(); AiGateStatus() in the status text.
bool AiGateOn = true, AiBehaviorSwitch = true, AiTyped = false;
double AiSpeed = 100, AiWin = 6, ScriptWin = 8, AiHoldMax = 60;
bool _agAi = true, _agLive = false, _agFwdSet = false; double _agT0 = -1, _agMoveT = -99, _agMinV = 0; Vector3D _agFwd;
int _agFound = -1;   // -1 not tried, else the number of AI blocks whose behaviour switch was found
List<ITerminalProperty> _agProps = new List<ITerminalProperty>();

void AiGateLoad()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    const string S = "AI Gate";
    if (!ini.ContainsKey(S, "Time-share With Script Search")) ini.Set(S, "Time-share With Script Search", AiGateOn);
    if (!ini.ContainsKey(S, "Switch On AI Behavior")) ini.Set(S, "Switch On AI Behavior", AiBehaviorSwitch);
    if (!ini.ContainsKey(S, "Use Typed AI Block API")) ini.Set(S, "Use Typed AI Block API", AiTyped);
    if (!ini.ContainsKey(S, "AI Flight Speed Limit (m/s)")) ini.Set(S, "AI Flight Speed Limit (m/s)", AiSpeed);
    if (!ini.ContainsKey(S, "AI Window (s)")) ini.Set(S, "AI Window (s)", AiWin);
    if (!ini.ContainsKey(S, "Script Search Window (s)")) ini.Set(S, "Script Search Window (s)", ScriptWin);
    if (!ini.ContainsKey(S, "Max AI Hold (s)")) ini.Set(S, "Max AI Hold (s)", AiHoldMax);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    AiGateOn = ini.Get(S, "Time-share With Script Search").ToBoolean(AiGateOn);
    AiBehaviorSwitch = ini.Get(S, "Switch On AI Behavior").ToBoolean(AiBehaviorSwitch);
    AiTyped = ini.Get(S, "Use Typed AI Block API").ToBoolean(AiTyped);
    AiSpeed = Math.Max(1, ini.Get(S, "AI Flight Speed Limit (m/s)").ToDouble(AiSpeed));
    AiWin = Math.Max(1, ini.Get(S, "AI Window (s)").ToDouble(AiWin));
    ScriptWin = Math.Max(1, ini.Get(S, "Script Search Window (s)").ToDouble(ScriptWin));
    AiHoldMax = Math.Max(AiWin, ini.Get(S, "Max AI Hold (s)").ToDouble(AiHoldMax));
}

void AiGateFind(List<IMyFunctionalBlock> ai)
{
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

bool AiGate(bool want)
{
    if (!want || !AiGateOn || _agLive) { _agT0 = -1; return want; }
    var vs = _rc.GetShipVelocities();
    double v = vs.LinearVelocity.Length();
    if (_agT0 < 0) { _agT0 = _now; _agAi = !UnderFire; _agMoveT = -99; _agMinV = v; _agFwdSet = false; }
    double age = _now - _agT0;
    if (_agAi)
    {
        // The AI flies if it speeds the ship up again (the dampeners alone only slow it), keeps turning it, or has turned it more than
        // 25 deg since the hand-over (it turns fast onto what it found, then may only brake; the script's own sweep coasts on for
        // less than that). Speed and turn rate are ignored for the first 1.5 / 2.5 s while the ship coasts from the script's control.
        if (!_agFwdSet) { _agFwd = _rc.WorldMatrix.Forward; _agFwdSet = true; }
        if (age < 1.0) _agMinV = v;
        else
        {
            _agMinV = Math.Min(_agMinV, v);
            if ((age > 1.5 && v > _agMinV + 3) || (age > 2.5 && vs.AngularVelocity.LengthSquared() > 0.0144) || Vector3D.Dot(_rc.WorldMatrix.Forward, _agFwd) < 0.906)
            { _agMoveT = _now; _agLive = true; }
        }
        bool busy = _now - _agMoveT < 4 && age < AiHoldMax;
        if (!busy && (age >= AiWin || (UnderFire && age >= 1.5))) { _agAi = false; _agT0 = _now; }
    }
    else if (age >= ScriptWin) { _agAi = true; _agT0 = _now; _agMoveT = -99; _agMinV = v; _agFwdSet = false; }
    return _agAi;
}

string AiGateStatus()
{
    if (_agFound < 0) return "";
    if (_agFound == 0 && AiBehaviorSwitch) return "AI Behavior switch not found:\n turn AI Behavior on by hand\n";
    return (AiBehaviorSwitch ? "AI Behavior on (" + _agFound + " blocks)" : "") + (_agLive ? " AI ok\n" : AiBehaviorSwitch ? "\n" : "");
}
// <<< AI GATE <<<

// >>> FOLLOW v1.1 (shared block: identical in every scripted combat drone, synced by tools/follow_sync.py) >>>
// FOLLOW mode ("follow" or "escort" command, Start Mode FOLLOW, or launch Mode=FOLLOW): the drone stays with its owner instead of
// holding a fixed home point (GUARD).
// The owner is found two ways:
//  * SHIP: whatever broadcasts the home point, i.e. a ship running a carrier link (HornetMothership, LongbowMothership, GnatMothership,
//    or any script sending "<Tag>_HOME" or "<fleet>_HOME"). The owner's velocity comes from successive position fixes, so older link
//    scripts work too. The link's "<Tag>_HOMER" / "<fleet>_HOMER" radius keeps the drones outside its hull (else Owner Radius).
//  * ON FOOT: the owner's own character, when a drone sensor sees it (about 50 m). It wins while seen, so stepping out of the ship
//    near the drones hands the escort to you.
// What the drone does:
//  * flies its own slot on a ring behind the owner (upper half only in gravity), outside the hull, and matches the owner's velocity;
//    a straight line through the hull is replaced by a detour round it, near the hull it never closes in, and on a collision
//    course with the owner (up to 8 s ahead) it sidesteps;
//  * engages only targets within Engage Radius of the owner, and drops a fight that takes it beyond Leash;
//  * the ring widens (up to 2x) while the owner manoeuvres hard (over 3 m/s2), so a ship that out-turns the drones does not run into them;
//  * link lost: keeps flying the owner's last track for Coast seconds (10 s on foot), then holds that point until the owner is heard again.
// SATELLITE formation (v1.1, command "satellite" / "sat" / "orbit", or [Follow] Formation=SATELLITE): the same owner tracking, hull clearance,
// engage/leash and return logic as FOLLOW, but the drones spread evenly round a VERTICAL CIRCLE centred on the owner and the whole ring turns
// slowly (Orbit Period). Ring radius = Satellite Distance, grown so neighbours stay a wingmate separation apart (more drones = a bigger
// ring), never inside the hull + 45 m, capped at 600 m. Plane: ACROSS = square to the owner's heading (a halo round the
// ship, covers all sides), ALONG = side-on (contains the heading). The ring turns with the owner's (lagged) heading. A drone that engages a
// target leaves its slot (FOLLOW combat rules); when the target is dead it flies back to its slot, which has kept turning. Drones agree on
// the ring phase without a clock: each averages the phase implied by the mates that sit on the ring. In gravity only the upper arc is used
// and the drones sway instead of orbiting. "follow" / "escort" switch back to the tail ring.
// Own [Follow] section in Custom Data (written on first run, "reload" re-reads it).
// Hooks: FollowLoad() after AiGateLoad(); FollowStep() every tick after ReadIGC(); FollowSensed(e) for each sensor detection and
// "Detect Owner" = FoFoot, "Detect Players" = TargetChars || FoFoot in ConfigureSensors(); first line of Home():
// "if (_mode == Mode.FOLLOW && FollowKnown()) return FollowPos();"; the target pick skips targets farther than FoEngage from Home();
// idle translation "want = FollowWant(me, aBrake, Separation(me)) + Evade();"; with a target
// "if (_mode == Mode.FOLLOW && FollowFar(me)) want = FollowBack(me);"; speed cap MaxSpeed in FOLLOW; FollowStatus() in the status.
double FoDist = 60, FoFootDist = 20, FoRadius = 25, FoEngage = 800, FoLeash = 1200, FoCoast = 3;
bool FoFoot = true, FoSat = false, FoSatAlong = false;
double FoSatDist = 400, FoSatPeriod = 180, _foSatPh = 0, _foSatT = -99, _foSatR = 400;
Vector3D _foSlotV;
Vector3D _foP, _foV, _foLinkP, _foLinkV, _foFwd, _foUp;
double _foT = -99, _foLinkT = -99, _foFootT = -99, _foR = 0, _foRT = -99, _foPhase = 0, _foAcc = 0;
bool _foFrame = false, _foOnFoot = false;
IMyBroadcastListener _foLR1, _foLR2; string _foKey = "";

string FoK(MyIni i, string k, string d) { if (!i.ContainsKey("Follow", k)) i.Set("Follow", k, d); return i.Get("Follow", k).ToString(d); }
double FoD(MyIni i, string k, double d, double lo) { if (!i.ContainsKey("Follow", k)) i.Set("Follow", k, d); return Math.Max(lo, i.Get("Follow", k).ToDouble(d)); }

void FollowLoad()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    FoDist = FoD(ini, "Slot Distance", FoDist, 10);
    FoFootDist = FoD(ini, "On Foot Distance", FoFootDist, 5);
    FoRadius = FoD(ini, "Owner Radius", FoRadius, 0);
    FoEngage = FoD(ini, "Engage Radius", FoEngage, 50);
    FoLeash = Math.Max(FoEngage, FoD(ini, "Leash", FoLeash, 0));
    FoCoast = FoD(ini, "Coast After Link Loss (s)", FoCoast, 0);
    FoFoot = FoK(ini, "Follow Owner On Foot", FoFoot ? "True" : "False").ToLowerInvariant() != "false";
    string fm = FoK(ini, "Formation (FOLLOW/SATELLITE)", FoSat ? "SATELLITE" : "FOLLOW").Trim().ToUpperInvariant();
    FoSat = fm == "SATELLITE" || fm == "SAT" || fm == "ORBIT";
    FoSatDist = FoD(ini, "Satellite Distance (m)", FoSatDist, 30);
    FoSatPeriod = FoD(ini, "Satellite Orbit Period (s)", FoSatPeriod, 0);
    FoSatAlong = FoK(ini, "Satellite Ring (ACROSS/ALONG)", "ACROSS").Trim().ToUpperInvariant() == "ALONG";
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    int h = 0; foreach (char c in Tag) h = (h * 31 + c) % 997;
    _foPhase = h / 997.0;   // drone types share no slot list: each type starts its ring at its own angle
    string key = Tag + "#" + _flChan;
    if (key == _foKey) return;
    _foKey = key;
    if (_foLR1 != null) IGC.DisableBroadcastListener(_foLR1);
    if (_foLR2 != null) IGC.DisableBroadcastListener(_foLR2);
    _foLR1 = IGC.RegisterBroadcastListener(Tag + "_HOMER");
    _foLR2 = _flChan != "" ? IGC.RegisterBroadcastListener(_flChan + "_HOMER") : null;
}

void FoReadR(IMyBroadcastListener l)
{
    while (l != null && l.HasPendingMessage)
    {
        var m = l.AcceptMessage();
        if (!(m.Data is double)) continue;
        double r = (double)m.Data;
        if (r > 0 && r < 5000) { _foR = r; _foRT = _now; }
    }
}

// commands: satellite / sat / orbit -> FoSetSat(true); follow / escort -> FoSetSat(false). Written to Custom Data so it survives a recompile.
void FoSetSat(bool on)
{
    if (on != FoSat) _foFrame = false;
    FoSat = on;
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    ini.Set("Follow", "Formation (FOLLOW/SATELLITE)", on ? "SATELLITE" : "FOLLOW");
    Me.CustomData = ini.ToString();
}

void FoSet(Vector3D p, Vector3D v, bool foot) { _foP = p; _foV = v; _foT = _now; _foOnFoot = foot; }

void FollowStep()
{
    if (FoSat)
    {
        double sdt = _now - _foSatT;
        if (sdt > 0 && sdt < 1 && FoSatPeriod > 1) _foSatPh += 2 * Math.PI * sdt / FoSatPeriod;
        _foSatT = _now;
    }
    FoReadR(_foLR1); FoReadR(_foLR2);
    if (_dynHomeT > _foLinkT)   // a new position fix from the carrier link
    {
        double dt = _dynHomeT - _foLinkT;
        if (dt > 0.05 && dt < 2)
        {
            Vector3D raw = (_dynHome - _foLinkP) / dt;
            if (raw.LengthSquared() > 150 * 150) _foLinkV = Vector3D.Zero;   // a jump (another carrier, or a respawn): no speed from it
            else
            {
                Vector3D nv = _foLinkV + (raw - _foLinkV) * 0.5;
                _foAcc += (Math.Min(30, (nv - _foLinkV).Length() / dt) - _foAcc) * 0.15;   // how hard the owner manoeuvres (m/s2, smoothed)
                _foLinkV = nv;
            }
        }
        else if (dt >= 2) _foLinkV = Vector3D.Zero;
        _foLinkP = _dynHome; _foLinkT = _dynHomeT;
        if (_now - _foFootT > 1.0) FoSet(_foLinkP, _foLinkV, false);   // the owner on foot wins while the sensor sees them
    }
    if (_rc == null || !FollowKnown()) return;
    // slot frame: forward = the owner's heading (kept while it stops), up = away from gravity (in space the frame's own up)
    Vector3D g = _rc.GetNaturalGravity();
    if (!_foFrame)
    {   // FOLLOW: start with the drone "behind" the owner (no slot on the far side of the hull). SATELLITE: every drone starts from the same
        // world axes, so all of them share one ring plane until the owner moves and turns the frame
        if (FoSat) { _foFwd = Vector3D.Right; _foUp = Vector3D.Up; _foFrame = true; return; }
        Vector3D to = _foP - _rc.GetPosition();
        _foFwd = to.LengthSquared() > 1 ? Vector3D.Normalize(to) : _rc.WorldMatrix.Forward; _foUp = _rc.WorldMatrix.Up; _foFrame = true;
    }
    if (g.LengthSquared() > 0.1) _foUp = -Vector3D.Normalize(g);
    Vector3D v = FollowVel();
    if (v.LengthSquared() > 9) _foFwd = Vector3D.Normalize(_foFwd + (Vector3D.Normalize(v) - _foFwd) * 0.005);   // ~3 s lag: a weaving owner does not swing the ring
    Vector3D right = Vector3D.Cross(_foFwd, _foUp);
    if (right.LengthSquared() < 0.01) right = _rc.WorldMatrix.Right;   // owner flying straight up or down
    right.Normalize();
    _foUp = Vector3D.Normalize(Vector3D.Cross(right, _foFwd));
    if (g.LengthSquared() > 0.1 && Vector3D.Dot(_foUp, g) > 0) _foUp = -_foUp;
}

void FollowSensed(MyDetectedEntityInfo e)
{
    if (!FoFoot || e.IsEmpty() || e.Type != MyDetectedEntityType.CharacterHuman || e.Relationship != MyRelationsBetweenPlayerAndBlock.Owner) return;
    _foFootT = _now;
    FoSet(e.Position, new Vector3D(e.Velocity), true);
}

bool FollowKnown() { return _foT > -50; }
// on foot the drones keep walking your last heading for 10 s (their sensors must get back within ~50 m of you)
double FoCoastNow() { return _foOnFoot ? Math.Max(FoCoast, 10) : FoCoast; }
Vector3D FollowPos() { return _foP + _foV * Math.Min(_now - _foT, FoCoastNow()); }
Vector3D FollowVel() { return _now - _foT <= FoCoastNow() ? _foV : Vector3D.Zero; }
double FollowRadius() { return _foOnFoot ? 1 : (_now - _foRT < 10 ? _foR : FoRadius); }

// This drone's slot: rank among the fresh hive mates of the same type (by grid id) picks the angle on the ring. Ring distance: Slot
// Distance, at least the hull radius + 30 m and wide enough that neighbours sit outside SepRange; on foot On Foot Distance, kept
// inside the sensor's reach (40 m) so the drones keep seeing you.
double _foSlotR = 60;
Vector3D FollowSlot()
{
    int n = 1, k = 0; long me = Me.CubeGrid.EntityId;
    foreach (var m in _mates.Values) { if (_now - m.Seen > 3) continue; n++; if (m.Id < me) k++; }
    bool grav = _rc.GetNaturalGravity().LengthSquared() > 0.1;
    if (FoSat) return FoSatSlot(n, k, grav);
    _foSlotV = Vector3D.Zero;
    double th = grav ? Math.PI * (k + 0.15 + 0.7 * _foPhase) / n : Math.PI * 0.5 + 2 * Math.PI * (k + 0.7 * _foPhase) / n;
    double gap = Math.Max(SepRange * 1.1, 30) / (2 * Math.Sin((grav ? Math.PI * 0.5 : Math.PI) / Math.Max(n, 2)));
    _foSlotR = _foOnFoot ? Math.Min(40, Math.Max(FoFootDist, n * 4)) : Math.Max(Math.Max(FoDist, FollowRadius() + 30), n > 1 ? gap / 0.866 : 0);
    if (!_foOnFoot) _foSlotR *= 1 + MathHelper.Clamp((_foAcc - 3) / 6, 0, 1);   // the owner throws itself around: up to twice the room
    Vector3D right = Vector3D.Cross(_foFwd, _foUp);
    Vector3D side = right * Math.Cos(th) + _foUp * Math.Sin(th);
    return FollowPos() + (-_foFwd * 0.5 + side * 0.866) * _foSlotR;   // 60 deg off the owner's tail
}

// SATELLITE slot k of n on the vertical ring (see the header). Mates are ranked by grid id; every drone's rank is the same as seen by the others.
double FoSatBase(int k, int n, bool grav) { return grav ? Math.PI * (k + 0.5) / n : 2 * Math.PI * (k + 0.5 * _foPhase) / n; }
Vector3D FoSatSlot(int n, int k, bool grav)
{
    Vector3D c = FollowPos(), a = FoSatAlong ? _foFwd : Vector3D.Cross(_foFwd, _foUp), nrm = Vector3D.Cross(a, _foUp);
    if (!grav)
    {   // phase consensus: the ring phase each fresh mate sitting on the ring implies, averaged (circular mean) with my own
        double sx = Math.Cos(_foSatPh), sy = Math.Sin(_foSatPh); long me = Me.CubeGrid.EntityId;
        foreach (var m in _mates.Values)
        {
            double age = _now - m.Seen; if (age > 3) continue;
            int r = me < m.Id ? 1 : 0; foreach (var q in _mates.Values) if (_now - q.Seen <= 3 && q.Id < m.Id) r++;
            Vector3D d = m.Pos + m.Vel * age - c; double x = Vector3D.Dot(d, a), y = Vector3D.Dot(d, _foUp), L = Math.Sqrt(x * x + y * y);
            if (Math.Abs(L - _foSatR) > 0.3 * _foSatR || Math.Abs(Vector3D.Dot(d, nrm)) > 0.3 * _foSatR) continue;   // off the ring (fighting)
            double ph = Math.Atan2(y, x) - FoSatBase(r, n, false); sx += Math.Cos(ph); sy += Math.Sin(ph);
        }
        double dp = Math.Atan2(sy, sx) - _foSatPh; dp -= 2 * Math.PI * Math.Floor((dp + Math.PI) / (2 * Math.PI));
        _foSatPh += dp * 0.02;
    }
    double gap = Math.Max(SepRange * 1.1, 40);
    double R = _foOnFoot ? Math.Min(40, Math.Max(FoFootDist, n * 4))
        : Math.Min(600, Math.Max(Math.Max(FoSatDist, FollowRadius() + 45), n > 1 ? gap / (2 * Math.Sin((grav ? Math.PI * 0.5 : Math.PI) / Math.Max(n, 2))) : 0));
    if (!_foOnFoot) R *= 1 + MathHelper.Clamp((_foAcc - 3) / 6, 0, 1);   // the owner throws itself around: up to twice the room
    _foSatR = R; _foSlotR = R;
    double w = FoSatPeriod > 1 ? 2 * Math.PI / FoSatPeriod : 0;
    double th = FoSatBase(k, n, grav);
    if (grav) th += 0.2 * Math.PI / n * Math.Sin(_foSatPh);   // sway by a fifth of the slot spacing: neighbours cannot meet
    else th += _foSatPh;
    _foSlotV = grav ? Vector3D.Zero : (a * -Math.Sin(th) + _foUp * Math.Cos(th)) * (R * w);
    return c + (a * Math.Cos(th) + _foUp * Math.Sin(th)) * R;
}

// sep = the script's wingmate separation (SepRange); on foot the ring is smaller than that, so a 15 m push replaces it.
Vector3D FollowWant(Vector3D me, double aBrake, Vector3D sep)
{
    Vector3D o = FollowPos(), s = FollowSlot();
    double clr = FollowRadius() + (_foOnFoot ? 6 : 15);
    Vector3D goal = s, seg = s - me;
    double sl2 = seg.LengthSquared();
    if (sl2 > 1)
    {   // the straight line to the slot crosses the hull: aim beside the hull first
        double u = Vector3D.Dot(o - me, seg) / sl2;
        if (u > 0 && u < 1)
        {
            Vector3D oc = me + seg * u - o;
            if (oc.LengthSquared() < clr * clr)
            {
                if (oc.LengthSquared() < 0.25) oc = Vector3D.Cross(seg, Vector3D.Cross(_foUp, seg));
                if (oc.LengthSquared() < 0.25) oc = _foUp;
                goal = o + Vector3D.Normalize(oc) * (clr + 10);
            }
        }
    }
    Vector3D d = goal - me; double dist = d.Length();
    double v = Math.Min(Math.Min(MaxSpeed, dist * 0.6), Math.Sqrt(2 * aBrake * dist));
    Vector3D w = FollowVel() + _foSlotV + (dist > 0.5 ? d / dist * v : Vector3D.Zero);
    Vector3D om = me - o; double oml = om.Length(), band = Math.Max(clr, Math.Min(clr + 25, _foSlotR - 8));
    if (oml < band)
    {   // near the hull: no closing in, and get out of the way of an owner turning into the drone
        Vector3D on = oml > 0.5 ? om / oml : _foUp;
        double closing = -Vector3D.Dot(_rc.GetShipVelocities().LinearVelocity - FollowVel(), on);
        double outw = (band - oml) * 0.8 + Math.Max(0, closing), cur = Vector3D.Dot(w - FollowVel(), on);
        if (cur < outw) w += on * (outw - cur);
    }
    Vector3D rv = _rc.GetShipVelocities().LinearVelocity - FollowVel();
    double rv2 = rv.LengthSquared();
    if (rv2 > 4)
    {   // on a collision course with the owner (it out-turns us, or cuts across our path): sidestep early, braking is too late
        double tc = -Vector3D.Dot(om, rv) / rv2;
        if (tc > 0 && tc < 8)
        {
            Vector3D miss = om + rv * tc; double ml = miss.Length(), need = clr + 15;
            if (ml < need)
            {
                Vector3D side = ml > 0.5 ? miss / ml : Vector3D.Cross(rv, _foUp);
                if (side.LengthSquared() < 0.01) side = Vector3D.Cross(rv, _rc.WorldMatrix.Right);
                w += Vector3D.Normalize(side) * Math.Min(MaxSpeed, (need - ml) / Math.Max(tc, 0.5) * 2);
            }
        }
    }
    if (!_foOnFoot) return w + sep;
    foreach (var m in _mates.Values)
    {
        double age = _now - m.Seen; if (age > 1.5) continue;
        Vector3D dm = me - (m.Pos + m.Vel * age); double len = dm.Length();
        if (len < 15 && len > 0.5) w += dm / len * (8 * (1 - len / 15));
    }
    return w;
}

bool FollowFar(Vector3D me) { return FollowKnown() && Vector3D.DistanceSquared(me, FollowPos()) > FoLeash * FoLeash; }
Vector3D FollowBack(Vector3D me) { Vector3D d = FollowPos() - me; return FollowVel() + (d.LengthSquared() > 1 ? Vector3D.Normalize(d) * MaxSpeed : Vector3D.Zero); }

string FollowStatus()
{
    if (_mode != Mode.FOLLOW) return "";
    if (!FollowKnown()) return "Follow : no owner yet (link script on your ship" + (FoFoot ? ", or stand within ~50 m" : "") + ")\n";
    double age = _now - _foT;
    string s = (FoSat ? "Satellite : ring " + _foSatR.ToString("F0") + " m, " + (_mates.Count + 1) + " ships, " : "Follow : ") + (_foOnFoot ? "on foot" : "ship") + (age > 1.5 ? " LOST " + age.ToString("F0") + " s" : "") + "  " + FollowVel().Length().ToString("F0") + " m/s";
    if (_rc != null) s += "  " + Vector3D.Distance(_rc.GetPosition(), FollowPos()).ToString("F0") + " m away";
    return s + "\n";
}
// <<< FOLLOW <<<
// >>> LEAD v1.0 (shared block: identical in every armed drone script, see docs/lead-v1.md) >>>
// Target prediction for the lead solvers. Measured on an offline bench (tools/lead_bench/Bench.cs: rockets, gatling and railgun shots at
// strafing / turning / orbiting / burning targets from 6 m to 120 m long) and in the sim; numbers in docs/lead-v1.md.
//  * Fresh acceleration: 0.8 of the newest velocity-difference sample (the Longbow's v2.2 finding). The old smoothed, steadiness-gated
//    acceleration lagged a strafing target by about a second and was the worst estimate in every pattern.
//  * Manoeuvre horizon: the present acceleration is extrapolated only for as long as this target's manoeuvres usually last (the mean
//    length of its stretches of steady acceleration), then the target flies on at constant velocity. A burn that has already outlasted
//    that is trusted for longer the longer it goes on (an orbit, a chase). Matters for slow shots: a rocket needs about 4 s to cover
//    800 m, and an AI ship with "evasive manoeuvres" flips its strafe every 2-5 s.
//  * Aim point: a hull point counts only across the line of fire (its depth along the line does not change what the shot hits, but a
//    deep point swings sideways when the target turns), is turned with the target's measured spin for at most 1 s, and must leave
//    Lead Margin x the predicted miss of known hull around it, so a long, uncertain shot goes to the middle of the known hull instead
//    of its edge, and a short, sure one may still pick the engines.
// Hooks (one line each, in class Track): fields  public Vector3D LAcc, LSeg, LW, LOR; public int LSegN; public double LSegT0 = -99, LManT = 2;
//   LeadSpin(this, o, dt) first thing in Track.Ori;  LeadSample(this, v, now, now - Seen) first thing in Track.Set.
// Solvers use LeadAcc(t), LeadHold(t), LeadK / LeadKd, LeadOffset; AimOffset filters its hull points through LeadPick.
static double LeadFastW = 0.8, LeadFastK = 0.9, LeadAccMax = 30;
static void LeadSample(Track t, Vector3D v, double now, double dt)
{
    if (dt >= 2) { t.LAcc = Vector3D.Zero; t.LSegN = 0; return; }
    if (t.Seen <= 0 || dt < 0.04) return;
    Vector3D a = (v - t.Vel) / dt;
    if (a.LengthSquared() > LeadAccMax * LeadAccMax) a = Vector3D.Normalize(a) * LeadAccMax;
    t.LAcc = t.LAcc * (1 - LeadFastW) + a * LeadFastW;
    // a new manoeuvre starts when the acceleration turns well away from the mean of the present one
    double am = a.Length(), sm = t.LSeg.Length(), mx = Math.Max(am, sm);
    if (t.LSegN == 0 || (mx > 0.5 && (a - t.LSeg).Length() > 0.6 * mx && now - t.LSegT0 > 0.3))
    {
        if (t.LSegN > 0) t.LManT = MathHelper.Clamp(t.LManT * 0.7 + (now - t.LSegT0) * 0.3, 0.4, 8);
        t.LSeg = a; t.LSegN = 1; t.LSegT0 = now;
    }
    else { t.LSegN++; t.LSeg += (a - t.LSeg) / Math.Min(t.LSegN, 8); }
    if (now - t.LSegT0 > t.LManT) t.LManT = Math.Min(8, t.LManT + 0.3 * dt);
}
// spin (world, rad/s) from two orientations: for a small turn phi, each axis b moves by phi x b, so sum(b_old x b_new) = 2 phi
static void LeadSpin(Track t, MatrixD o, double dt)
{
    if (t.HasOri && dt > 0.04 && dt < 2 && t.LOR.LengthSquared() > 0.5)
        t.LW = t.LW * 0.5 + (Vector3D.Cross(t.OF, o.Forward) + Vector3D.Cross(t.OU, o.Up) + Vector3D.Cross(t.LOR, o.Right)) * (0.25 / dt);
    t.LOR = o.Right;
}
Vector3D LeadAcc(Track t) { return t.LAcc.LengthSquared() < 0.25 ? Vector3D.Zero : t.LAcc * LeadFastK; }
// seconds the present manoeuvre is expected to go on
double LeadHold(Track t)
{
    double age = _now - t.LSegT0;
    return Math.Max(Math.Max(t.LManT - age, 0.3 * t.LManT), 2 * (age - t.LManT));
}
// target displacement = acceleration * LeadK(T, hold) after T seconds; velocity change = acceleration * LeadKd(T, hold)
static double LeadK(double T, double h) { return T <= h ? 0.5 * T * T : h * (T - 0.5 * h); }
static double LeadKd(double T, double h) { return Math.Min(T, h); }
// aim offset (world) T seconds of flight from now: only across the line of fire 'los', turned with the target's spin for at most 1 s
Vector3D LeadOffset(Track t, Vector3D off, Vector3D los, double T)
{
    if (off.LengthSquared() < 1e-6) return off;
    double wl = t.LW.Length(), h = Math.Min(_now - t.Seen + T, 1.0);
    if (wl > 1e-3 && h > 0)
    {
        Vector3D k = t.LW / wl; double an = wl * h, c = Math.Cos(an), s = Math.Sin(an);
        off = off * c + Vector3D.Cross(k, off) * s + k * (Vector3D.Dot(k, off) * (1 - c));
    }
    return off - Vector3D.Dot(off, los) * los;
}
// Predicted miss (m) of a shot with T seconds of flight: what the target's manoeuvring can do in that time.
static double LeadMiss(Track t, double T) { return 0.5 * t.AccMag * T * T; }
// Hull points (target frame) that leave 'need' metres of known hull around them across the line of fire (l, target frame), judged
// against the extent of all hull points along 4 directions (an octagon round the known hull); none: the one with the most.
List<int> _leadKeep = new List<int>();
List<int> LeadPick(List<Vector3D> hull, Vector3D l, double need)
{
    _leadKeep.Clear();
    int n = hull.Count;
    if (l.LengthSquared() < 0.25 || need <= 0) { for (int i = 0; i < n; i++) _leadKeep.Add(i); return _leadKeep; }   // (no line of fire yet)
    Vector3D u = Vector3D.Cross(l, Math.Abs(l.Y) < 0.9 ? Vector3D.Up : Vector3D.Right); u.Normalize();
    Vector3D v = Vector3D.Cross(l, u);
    double x0 = 1e9, x1 = -1e9, y0 = 1e9, y1 = -1e9, p0 = 1e9, p1 = -1e9, q0 = 1e9, q1 = -1e9;
    for (int i = 0; i < n; i++)
    {
        double x = Vector3D.Dot(hull[i], u), y = Vector3D.Dot(hull[i], v), p = (x + y) * 0.7071, q = (x - y) * 0.7071;
        x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
        p0 = Math.Min(p0, p); p1 = Math.Max(p1, p); q0 = Math.Min(q0, q); q1 = Math.Max(q1, q);
    }
    int best = 0; double bm = -1e9;
    for (int i = 0; i < n; i++)
    {
        double x = Vector3D.Dot(hull[i], u), y = Vector3D.Dot(hull[i], v), p = (x + y) * 0.7071, q = (x - y) * 0.7071;
        double m = Math.Min(Math.Min(Math.Min(x - x0, x1 - x), Math.Min(y - y0, y1 - y)), Math.Min(Math.Min(p - p0, p1 - p), Math.Min(q - q0, q1 - q)));
        if (m >= need) _leadKeep.Add(i);
        if (m > bm) { bm = m; best = i; }
    }
    if (_leadKeep.Count == 0 && n > 0) _leadKeep.Add(best);
    return _leadKeep;
}
// <<< LEAD v1.0 <<<

// >>> KAMIKAZE DEFENCE v1.0 (shared block: identical in every combat drone script, synced by tools/kz_sync.py, see docs/kamikaze-defence.md) >>>
// Countermeasures against incoming swarms of small warhead drones (Stingers and the like). A contact is an INCOMING KAMIKAZE when it is
// small (bounding radius <= Max Radius), seen in the last 3 s, closing faster than Min Closing Speed, its straight-line closest approach
// to me (or to a hive mate) is within Miss Distance (+ Miss Distance Per km of range) and that approach is less than Warning Time away,
// and it is alive: fast (closing over 30 m/s), steering (spinning, its velocity changing) or already classed as one a moment ago. Then:
//   1 it is never written off as "disabled" (a kamikaze correcting its course at the 100 m/s cap spins 1-2 rad/s and looked like a
//     tumbling wreck: the drone dropped it, ignored it for 120 s and told the whole hive it was finished); the other way round, a former
//     kamikaze that coasts (no thrust, not steering, over 20 m/s) and is no longer coming for anyone is SPENT: it is dropped at once, so the
//     drone does not keep a harmless wreck as its target while the rest of the pack arrives unseen (the AI-block search only runs without one).
//     Rule 1 already holds while the kamikaze is still far out (closest approach up to 5 x Warning Time away): at the cap it cruises 1.5 km in
//     15 s, and the spin rule (4 s + 5 s grace) used to drop it just as it arrived.
//   2 it becomes the priority target (most urgent first; threats to hive mates count too, a bit less)
//   3 the drone backs away along the threat axis (guns stay on it), which multiplies the time the guns have before it arrives
//   4 inside Break Time of impact it breaks sideways, away from the predicted impact point (a blast reaches only ~4.5 m)
//   5 spare camera charge keeps the other incoming tracks fresh and searches round known kamikazes for the rest of the pack
//   6 incoming tracks are shared with the hive (<Tag>_TGT and the FLEET channel), so wingmates shoot what dives on me
// Own [Kamikaze Defence] section in Custom Data (defaults written on first run, "reload" re-reads them).
// Script hooks: KzLoad() next to every FleetLoad(true); KzAssess() first in the target-selection pass (PruneAndSelect / Housekeeping);
// score += KzScore(t) next to HiveScore(t); "&& !KzLive(t.Id)" on the GUARD/FORM radius skip; "if (KzLive(t.Id)) return false;" after
// the DeadAt line of Disabled(), followed by "if (KzSpent(t)) return true;"; want = KzWant(want, me, cap) right before the speed cap; _sb.Append(KzStatus()) after HiveStatus().
// Uses from the script: _contacts, _mates, _tgt, _now, _rc, Tag, Track (Id, Pos, Vel, AccMag, Radius, Seen, Src, Type, P(), V()),
// Mate (Pos, Vel, Seen), CamFor(), Valid(), AddContact(), FleetSend().
bool KzOn = true, KzBack = true, KzBreak = true, KzRays = true, KzShareOn = true, KzMates = true;
double KzMaxRadius = 3.5, KzHorizon = 12, KzMiss = 40, KzMissPerKm = 60, KzMinClosing = 15, KzPriority = 2500;
double KzBackSpeed = 60, KzBackRange = 800, KzReact = 10, KzBreakTime = 2, KzBreakSpeed = 40, KzBreakMiss = 20, KzPack = 60, KzReserve = 600;
class KzThreat { public Track T; public double Tgo, Miss, Dist, Urg; public bool Mine; }
List<KzThreat> _kz = new List<KzThreat>();
Dictionary<long, double> _kzT = new Dictionary<long, double>();     // id -> last time it was classed as incoming (latch)
Dictionary<long, KzThreat> _kzById = new Dictionary<long, KzThreat>();
List<long> _kzDrop = new List<long>();
double _kzShareT = -9; int _kzRay = 0, _kzMine = 0; Vector3D _kzSide; Random _kzRng = new Random();

void KzLoad()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    const string S = "Kamikaze Defence";
    KzOn = KzB(ini, S, "Enabled", KzOn);
    KzMaxRadius = KzD(ini, S, "Max Kamikaze Radius (m)", KzMaxRadius);
    KzHorizon = KzD(ini, S, "Warning Time (s)", KzHorizon);
    KzMiss = KzD(ini, S, "Miss Distance (m)", KzMiss);
    KzMissPerKm = KzD(ini, S, "Miss Distance Per km Of Range (m)", KzMissPerKm);
    KzMinClosing = KzD(ini, S, "Min Closing Speed (m/s)", KzMinClosing);
    KzPriority = KzD(ini, S, "Priority Bonus", KzPriority);
    KzMates = KzB(ini, S, "Protect Hive Mates", KzMates);
    KzBack = KzB(ini, S, "Back Away", KzBack);
    KzBackSpeed = KzD(ini, S, "Back Away Speed (m/s)", KzBackSpeed);
    KzBackRange = KzD(ini, S, "Back Away Within (m)", KzBackRange);
    KzReact = KzD(ini, S, "Back Away Reaction Time (s)", KzReact);
    KzBreak = KzB(ini, S, "Break Sideways", KzBreak);
    KzBreakTime = KzD(ini, S, "Break Time (s)", KzBreakTime);
    KzBreakSpeed = KzD(ini, S, "Break Speed (m/s)", KzBreakSpeed);
    KzBreakMiss = KzD(ini, S, "Break Miss Distance (m)", KzBreakMiss);
    KzRays = KzB(ini, S, "Track And Pack Search Rays", KzRays);
    KzPack = KzD(ini, S, "Pack Search Radius (m)", KzPack);
    KzReserve = KzD(ini, S, "Camera Charge Reserve (m)", KzReserve);
    KzShareOn = KzB(ini, S, "Share Threats With Hive", KzShareOn);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}
double KzD(MyIni i, string s, string k, double d) { if (!i.ContainsKey(s, k)) i.Set(s, k, d); return i.Get(s, k).ToDouble(d); }
bool KzB(MyIni i, string s, string k, bool d) { if (!i.ContainsKey(s, k)) i.Set(s, k, d); return i.Get(s, k).ToBoolean(d); }

// Closest approach of a threat (p, v) to a protected point (q, u), both on straight lines: time to it, miss distance, closing speed.
bool KzCpa(Vector3D p, Vector3D v, Vector3D q, Vector3D u, out double tgo, out double miss, out double closing)
{
    Vector3D r = q - p, w = v - u;
    double d = r.Length(), ww = w.LengthSquared();
    closing = d > 0.1 ? Vector3D.Dot(w, r) / d : 0;
    tgo = ww > 1 ? Vector3D.Dot(r, w) / ww : -1;
    miss = tgo > 0 ? (r - w * tgo).Length() : d;
    return tgo > 0;
}

bool KzLive(long id) { double t; return KzOn && _kzT.TryGetValue(id, out t) && _now - t < 4; }
// was an incoming kamikaze (in the last minute), is not one now, and coasts (over 20 m/s, no thrust, not steering): a wreck on its way out.
// (A ship that stopped - a sniper settling to fire - is not coasting and stays a target.)
bool KzSpent(Track t) { double s; return KzOn && _kzT.TryGetValue(t.Id, out s) && _now - s >= 4 && t.AccMag < 0.5 && t.Spin < 0.3 && _now - t.Seen < 2 && t.Vel.LengthSquared() > 400; }

// Runs once per target-selection pass (6 Hz): classifies the contacts, then spends spare camera charge and shares the threats.
void KzAssess()
{
    _kz.Clear(); _kzById.Clear(); _kzMine = 0;
    if (!KzOn || _rc == null) return;
    Vector3D me = _rc.GetPosition(), mv = _rc.GetShipVelocities().LinearVelocity;
    foreach (var t in _contacts.Values)
    {
        if (_now - t.Seen > 3) continue;
        bool latched = KzLive(t.Id);
        if (!latched && t.Type != MyDetectedEntityType.Missile && t.Radius > KzMaxRadius) continue;
        Vector3D p = t.P(_now), v = t.V(_now);
        double tgo, miss, cl, best = 1e9, bMiss = 0, bDist = 0, bCl = 0; bool mine = false;
        if (KzCpa(p, v, me, mv, out tgo, out miss, out cl) && KzHit(tgo, miss, cl, Vector3D.Distance(p, me), latched))
        { best = tgo; bMiss = miss; bDist = Vector3D.Distance(p, me); bCl = cl; mine = true; }
        if (KzMates)
            foreach (var m in _mates.Values)
            {
                double age = _now - m.Seen; if (age > 1.5) continue;
                Vector3D mp = m.Pos + m.Vel * age; double md = Vector3D.Distance(p, mp);
                if (KzCpa(p, v, mp, m.Vel, out tgo, out miss, out cl) && KzHit(tgo, miss, cl, md, latched) && tgo < best - 1.5)
                { best = tgo; bMiss = miss; bDist = md; bCl = cl; mine = false; }
            }
        if (best > 1e8) continue;
        // drifting debris on a collision course (slow, not steering) is dodged by the guns' own logic, not chased
        if (!latched && bCl < 30 && t.AccMag < 0.6 && t.Spin < 0.3 && t.Type != MyDetectedEntityType.Missile && t.Src != "IGC") continue;
        _kzT[t.Id] = _now;
        if (best > KzHorizon) continue;   // still far out: protected from the wreck rules (above), not yet a threat to act on
        var k = new KzThreat { T = t, Tgo = best, Miss = bMiss, Dist = bDist, Mine = mine, Urg = MathHelper.Clamp((KzHorizon - best) / KzHorizon, 0, 1) };
        _kz.Add(k); _kzById[t.Id] = k; if (mine) _kzMine++;
    }
    _kz.Sort((a, b) => a.Tgo.CompareTo(b.Tgo));
    _kzDrop.Clear(); foreach (var kv in _kzT) if (_now - kv.Value > 60) _kzDrop.Add(kv.Key);
    foreach (var id in _kzDrop) _kzT.Remove(id);
    if (_kz.Count == 0) return;
    if (KzRays) KzRayStep();
    if (KzShareOn && _now - _kzShareT > 0.5) KzShare();
}
bool KzHit(double tgo, double miss, double closing, double dist, bool latched)
{
    if (dist < 60 && closing > 5) return true;   // right on top of us and still coming
    return closing > (latched ? KzMinClosing * 0.5 : KzMinClosing) && tgo < KzHorizon * 5 && miss < KzMiss + KzMissPerKm * dist / 1000;
}

// Score adjustment for target selection (lower = better): the most urgent incoming kamikaze first.
double KzScore(Track t)
{
    KzThreat k;
    if (!_kzById.TryGetValue(t.Id, out k)) return 0;
    return -KzPriority * (0.4 + 0.6 * k.Urg) * (k.Mine ? 1 : 0.6);
}

// Velocity command adjustment: back away from incoming kamikazes, and break sideways just before one arrives.
Vector3D KzWant(Vector3D want, Vector3D me, double cap)
{
    if (!KzOn || _kzMine == 0 || _rc == null) return want;
    Vector3D mv = _rc.GetShipVelocities().LinearVelocity, away = Vector3D.Zero, brk = Vector3D.Zero;
    double urg = 0, wsum = 0, brkTgo = KzBreakTime;
    foreach (var k in _kz)
    {
        if (!k.Mine || _now - k.T.Seen > 3) continue;
        Vector3D p = k.T.P(_now), v = k.T.V(_now);
        double tgo, miss, cl;
        if (!KzCpa(p, v, me, mv, out tgo, out miss, out cl) || cl < 2) continue;
        Vector3D r = me - p; double d = r.Length(); if (d < 0.5) continue;
        double a = MathHelper.Clamp((KzReact - tgo) / KzReact, 0, 1);
        if (KzBack && d < KzBackRange) { away += r / d * a; wsum += a; urg = Math.Max(urg, a); }
        if (KzBreak && tgo < brkTgo && miss < KzBreakMiss + k.T.Radius)
        {   // widen the miss: move along the miss vector (from where it will pass to where I will be); dead centre: a fixed random side
            brkTgo = tgo;
            Vector3D w = mv - v, m = r + w * tgo;
            m -= Vector3D.Dot(m, w) / Math.Max(w.LengthSquared(), 1e-6) * w;
            if (m.LengthSquared() < 1)
            {
                if (_kzSide.LengthSquared() < 0.5 || Math.Abs(Vector3D.Dot(_kzSide, Vector3D.Normalize(w))) > 0.5)
                {
                    _kzSide = Vector3D.Cross(w, new Vector3D(_kzRng.NextDouble() - 0.5, _kzRng.NextDouble() - 0.5, _kzRng.NextDouble() - 0.5));
                    _kzSide = _kzSide.LengthSquared() > 1e-6 ? Vector3D.Normalize(_kzSide) : Vector3D.Zero;
                }
                m = _kzSide;
            }
            if (m.LengthSquared() > 1e-6) brk = Vector3D.Normalize(m) * KzBreakSpeed;
        }
    }
    if (urg > 0 && away.LengthSquared() > 0.0625 * wsum * wsum)   // (threats from opposite sides cancel out: then only the break helps)
    {
        Vector3D u = Vector3D.Normalize(away);
        double s = KzBackSpeed * urg, along = Vector3D.Dot(want, u);
        if (along < s) want += u * (s - along);
    }
    want += brk;
    if (want.LengthSquared() > cap * cap && brk.LengthSquared() > 1)
    {   // over the cap: keep the break whole, shrink the rest
        Vector3D rest = want - brk; double room = Math.Max(0, cap - brk.Length());
        if (rest.LengthSquared() > room * room) rest = Vector3D.Normalize(rest) * room;
        want = rest + brk;
    }
    return want;
}

// Spare camera charge: one ray refreshes the stalest incoming track that is not the current target, one (every 2nd pass) searches
// round a known kamikaze for the rest of its pack (they launch together). A camera is used only with KzReserve metres of charge left over.
void KzRayStep()
{
    Vector3D me = _rc.GetPosition();
    KzThreat st = null;
    foreach (var k in _kz) if (k.T != _tgt && _now - k.T.Seen > 0.25 && (st == null || k.T.Seen < st.T.Seen)) st = k;
    if (st != null)
    {
        Vector3D p = st.T.P(_now), d = p - me; double len = d.Length();
        if (len > 1)
        {
            Vector3D aim = p + d / len * Math.Min(st.T.Radius + 2, 8);
            var c = CamFor(aim, len + KzReserve);
            if (c != null) { var e = c.Raycast(aim); if (Valid(e)) AddContact(e, "KZ"); }
        }
    }
    if ((++_kzRay & 1) == 1) return;
    var pk = _kz[(_kzRay >> 1) % _kz.Count];
    if (_now - pk.T.Seen > 1) return;
    Vector3D pc = pk.T.P(_now), dl = pc - me; double dd = dl.Length();
    if (dd < 30) return;
    dl /= dd;
    Vector3D uu = Vector3D.Cross(dl, Math.Abs(dl.Y) < 0.95 ? Vector3D.Up : Vector3D.Right); uu.Normalize();
    Vector3D vv = Vector3D.Cross(dl, uu);
    double rr = KzPack * Math.Sqrt(((_kzRay >> 1) % 17 + 0.5) / 17.0), th = _kzRay * 2.39996323;
    Vector3D pt = pc + uu * (rr * Math.Cos(th)) + vv * (rr * Math.Sin(th)) + dl * KzPack;
    var cam = CamFor(pt, dd + KzPack + KzReserve);
    if (cam == null) return;
    var pe = cam.Raycast(pt);
    if (Valid(pe)) AddContact(pe, "KZ");
}

// The two most urgent incoming tracks I see myself go to the hive twice a second (same tuple as a target share).
void KzShare()
{
    _kzShareT = _now; int n = 0;
    foreach (var k in _kz)
    {
        if (k.T.Src == "IGC" || _now - k.T.Seen > 0.5) continue;
        var tup = new MyTuple<long, Vector3D, Vector3D, double, long>(k.T.Id, k.T.P(_now), k.T.V(_now), k.T.Radius, Me.EntityId);
        IGC.SendBroadcastMessage(Tag + "_TGT", tup); FleetSend(tup);
        if (++n >= 2) break;
    }
}

string KzStatus()
{
    if (!KzOn) return "";
    if (_kz.Count == 0) return "Kamikaze: none incoming\n";
    var k = _kz[0];
    return "!! KAMIKAZE x" + _kz.Count + " (" + _kzMine + " at me)  next " + k.Tgo.ToString("0.0") + " s " + k.Dist.ToString("0") + " m" + (k.Mine ? "" : " (mate)") + "\n";
}
// <<< KAMIKAZE DEFENCE <<<

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

void CtorErr(string w, Exception ex) { _crash = "setup " + w + ": " + ex.Message; Echo(_crash); }   // a failing setup step must not kill the script for good: the rest still runs and the error is on the screen

// >>> FRIENDLY AVOIDANCE v1.0 (shared block: identical in every combat drone script, synced by tools/blocks_sync.py, see docs/friendly-avoidance.md) >>>
// Keeps the drone off every grid that is not an enemy: the carrier, the ships it escorts, other wings, neutral and unowned grids.
// Hive mates repel each other through Separation(), but only mates on the same IGC tag, and the cameras never target a friend, so a
// drone orbiting a target flew into a Ballista (other tag, not a mate, not a contact) without ever knowing it was there.
// Two sources fill the obstacle list _fa:
//   BEACON   every script with this block (and the Shrike Helm) broadcasts its grid's bounding sphere (centre, velocity, radius) on the
//            IGC channel DRONE_SPACE, 4 Hz; no relationship, tag or camera needed. Needs antennas in range, as every IGC message does.
//   SIGHTING any non-enemy grid that Valid() sees (sensors, cameras, designators, omni scan) is remembered with its position, velocity and
//            hull radius for 12 s, so grids without a beacon (a neutral hulk, another player's ship) are avoided too.
// FaWant() then bends the velocity the drone is about to command, per obstacle (sphere = grid radius + my radius + 15 m margin):
//   1 BARRIER   closing speed on the sphere is capped at sqrt(2 x 4 m/s2 x gap), so the drone can always stop in time
//   2 RAMP      inside margin + 40 m a radial push away from it (up to 50 m/s, more inside the margin)
//   3 SIDESTEP  if the straight line (actual or commanded velocity) passes inside the margin within 8 s, a push sideways
//   ROCKS    asteroids seen by a camera ray (the scans all go through Valid()) are kept as 40 m spheres on the hit point for 60 s, so the same
//            barrier / ramp / sidestep keep the drone off them. Voxels give no hull radius and sensors give no hit point: only rays count.
// Ignored: RTB and IDLE (docking, parked), and any obstacle whose sphere I am inside (launching from a bay, merged, docked).
// No Custom Data keys (the 100,000-character limit); constants are below. Registers its listener on the first call.
// Script hooks: FaSee(e) first line of Valid(); want = FaWant(want, me) right after the KzWant line (the speed cap comes after).
// Enemies are skipped: every contact (_contacts holds only valid targets, my target included), since IGC does not tell friend from foe.
// Uses from the script: _rc, _now, _mode, _contacts, Valid(), Mode.RTB / Mode.IDLE.
class FaO { public Vector3D P, V; public double R, T; public bool B, K; }
Dictionary<long, FaO> _fa = new Dictionary<long, FaO>(); IMyBroadcastListener _faL; double _faT, _faR = 6;
FaO FaGet(long id) { FaO o; if (!_fa.TryGetValue(id, out o)) _fa[id] = o = new FaO(); return o; }
void FaSee(MyDetectedEntityInfo e)
{
    if (e.Type == MyDetectedEntityType.Asteroid)   // rock: only a camera ray gives a point on it; one 40 m sphere per 40 m cell, kept 60 s
    {
        if (e.HitPosition == null || _fa.Count > 150) return;
        Vector3D h = e.HitPosition.Value; FaO a = FaGet(((long)(h.X / 40) << 40) ^ ((long)(h.Y / 40) << 20) ^ (long)(h.Z / 40));
        a.P = h; a.V = Vector3D.Zero; a.R = 40; a.T = _now; a.B = false; a.K = true; return;
    }
    if (e.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || (e.Type != MyDetectedEntityType.LargeGrid && e.Type != MyDetectedEntityType.SmallGrid) || e.EntityId == Me.CubeGrid.EntityId) return;
    FaO o = FaGet(e.EntityId);
    if (o.B && _now - o.T < 1) return;   // (a fresh beacon fix beats a sighting)
    o.P = e.Position; o.V = e.Velocity; o.R = Math.Max(e.BoundingBox.Size.Length() * 0.5, 2); o.T = _now; o.B = false;
}
Vector3D FaWant(Vector3D want, Vector3D me)
{
    if (_faL == null) _faL = IGC.RegisterBroadcastListener("DRONE_SPACE");
    while (_faL.HasPendingMessage)
    {
        var m = _faL.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, float>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, float>)m.Data;
        if (d.Item1 == Me.CubeGrid.EntityId) continue;
        FaO q = FaGet(d.Item1);
        q.P = d.Item2; q.V = d.Item3; q.R = d.Item4; q.T = _now; q.B = true;
    }
    Vector3D mv = _rc.GetShipVelocities().LinearVelocity, push = Vector3D.Zero;
    if (_now - _faT >= 0.25)
    {
        _faT = _now; var v = Me.CubeGrid.WorldVolume; _faR = v.Radius;
        IGC.SendBroadcastMessage("DRONE_SPACE", new MyTuple<long, Vector3D, Vector3D, float>(Me.CubeGrid.EntityId, v.Center, mv, (float)v.Radius));
        var gone = new List<long>(); foreach (var kv in _fa) if (_now - kv.Value.T > (kv.Value.B ? 3 : kv.Value.K ? 60 : 12)) gone.Add(kv.Key);
        foreach (var id in gone) _fa.Remove(id);
    }
    if (_mode == Mode.RTB || _mode == Mode.IDLE) return want;
    foreach (var kv in _fa)
    {
        var o = kv.Value;
        if (_contacts.ContainsKey(kv.Key)) continue;   // an enemy's beacon (or my target's): the fight is meant to close in on it
        Vector3D rel = me - (o.P + o.V * Math.Min(_now - o.T, 3));
        double dist = rel.Length(), r = o.R + _faR, inner = r + 15;
        if ((dist < r && !o.K) || dist < 1) continue;   // inside a ship's bounding sphere: launching, merged or docked (a rock's sphere is only a hit point's margin: still avoid it)
        Vector3D n = rel / dist;
        double cl = -Vector3D.Dot(want - o.V, n), lim = Math.Sqrt(8 * Math.Max(dist - inner, 0));
        if (cl > lim) want += n * (cl - lim);
        if (dist < inner + 40) push += n * 50 * Math.Min(1.5, (inner + 40 - dist) / 40 + (dist < inner ? 0.5 : 0));
        for (int k = 0; k < 2; k++)
        {
            Vector3D w = (k == 0 ? mv : want) - o.V; double ww = w.LengthSquared(), t = -Vector3D.Dot(rel, w) / Math.Max(ww, 1);
            if (ww < 4 || t <= 0 || t > 8) continue;
            Vector3D miss = rel + w * t; double ml = miss.Length();
            if (ml < inner) push += (ml > 1 ? miss / ml : Vector3D.Up) * 50 * (1 - t / 8) * (1 - ml / inner);
        }
    }
    return want + push;
}
// <<< FRIENDLY AVOIDANCE v1.0 <<<

public Program()
{
    for (int i = 0; i < 6; i++) _thrDir[i] = new List<IMyThrust>();
    try
    {
        LoadConfig();
    }
    catch (Exception ex) { CtorErr("config", ex); }
    try
    {
        FleetLoad(true); LaunchLoad(); KzLoad(); AiGateLoad(); FoLeash = Math.Max(FoLeash, 2200); FollowLoad();
    }
    catch (Exception ex) { CtorErr("modules", ex); }
    try
    {
        _ready = StartMode.Trim().ToUpperInvariant() == "READY"; _mode = _ready ? Mode.GUARD : ParseMode(StartMode, Mode.IDLE);
        if (!string.IsNullOrEmpty(Storage))
        {
            var p = Storage.Split(';');
            double x, y, z;
            if (p.Length >= 5 && double.TryParse(p[1], out x) && double.TryParse(p[2], out y) && double.TryParse(p[3], out z))
            { _mode = ParseMode(p[0], _mode); _ready = p.Length > 5 && p[5] == "1"; _home = new Vector3D(x, y, z); _homeSet = p[4] == "1"; }
        }
    }
    catch (Exception ex) { CtorErr("storage", ex); }
    try
    {
        _lTgt = IGC.RegisterBroadcastListener(Tag + "_TGT");
        _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
        _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME");
        _lSwarm = IGC.RegisterBroadcastListener(Tag + "_SWARM");
    }
    catch (Exception ex) { CtorErr("listeners", ex); }
    try
    {
        GetBlocks();
        if (!_homeSet && _rc != null) { _home = _rc.GetPosition(); _homeSet = true; }
    }
    catch (Exception ex) { CtorErr("blocks", ex); }
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save()
{
    Storage = string.Join(";", _mode.ToString(), _home.X.ToString("R"), _home.Y.ToString("R"), _home.Z.ToString("R"), _homeSet ? "1" : "0", _ready ? "1" : "0");
}

Mode ParseMode(string s, Mode def)
{
    switch (s.Trim().ToUpperInvariant())
    {
        case "IDLE": return Mode.IDLE; case "GUARD": return Mode.GUARD; case "HUNT": return Mode.HUNT;
        case "ASSIST": return Mode.ASSIST; case "RTB": return Mode.RTB; case "FOLLOW": case "ESCORT": return Mode.FOLLOW; case "SATELLITE": case "SAT": case "ORBIT": FoSat = true; return Mode.FOLLOW; default: return def;
    }
}

// =====================================================================
public void Main(string arg, UpdateType src)
{
    try { Tick(arg, src); if (_errRun > 0 && ++_okRun >= 300) { _errRun = 0; _crash = ""; } }
    catch (Exception ex)
    {   // a repeating error: hand the ship back once (no stale overrides), then report on change or at 1 Hz only (not 60 text writes a second)
        _okRun = 0;
        if (_errRun++ == 0) { try { Release(); } catch (Exception) { } }
        if (_crash != ex.Message || _errRun % 60 == 1) { _crash = ex.Message; Echo("Script error: " + ex.Message); if (_lcd != null) _lcd.WriteText("SCRIPT ERROR\n" + ex.Message + "\nSend this text to Claude."); }
    }
}
int _errRun, _okRun; bool _rcOut;

bool Hot() { return Runtime.CurrentInstructionCount > Runtime.MaxInstructionCount * 0.6; }   // instruction guard: above 60% of the limit the optional stages (scans, polls, sharing) wait for the next tick

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
    if (_rc == null || !_rc.IsFunctional || !_rc.IsSameConstructAs(Me)) { _err = "No working Remote Control / cockpit"; if (!_rcOut || _tick % 60 == 0) { _rcOut = true; GetBlocks(); Display(); Release(); } return; }   // (not on this construct = a severed piece: same as no controller; look for another one at 1 Hz)
    _rcOut = false;

    ReadIGC(); FollowStep(); ReadSwarm();
    if (_tick % 6 == 3) BroadcastStatus();
    if (!Hot()) StepSlimScan();
    if (_tick % 20 == 7) CheckDamage();
    if (_tick % 6 == 0) PollDesignators();
    if (_tick % 10 == 5 && !Hot()) PollSensors();
    if (_tick % 10 == 0) { PruneAndSelect(); PowerAndHealth(); }
    UpdateGuns();
    MaintainLock(0.25);
    if (_mode != Mode.IDLE && !Hot()) OmniScan();
    if (_tgt == null && !_aiOn && AutoScan && (_mode == Mode.GUARD || _mode == Mode.HUNT || _mode == Mode.FOLLOW) && !Hot()) AutoScanStep();
    if (ShareTargets && _tgt != null && _tgt.Src != "IGC" && _now - _tgt.Seen < 1.0 && _tick % 30 == 0 && !Hot()) ShareTarget();

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
        case "follow": case "escort": FoSetSat(false); SetMode(Mode.FOLLOW); break;
        case "satellite": case "sat": case "orbit": FoSetSat(true); SetMode(Mode.FOLLOW); break;
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
        case "breach": if (w.Length > 1) BreachAuto = w[1] == "on"; break;
        case "seeker": if (w.Length > 1) { SeekerOn = w[1] == "on"; SeekerSetup(); } break;
        case "focus": SplitFocus = false; break;
        case "ready": SetMode(Mode.GUARD); if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; _ready = true; } break;   // READY: hold this spot (the carrier link's HOME is ignored), scan all round, fight when something comes, come back
        case "home": if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } break;
        case "range": double r; if (w.Length > 1 && double.TryParse(w[1], out r)) ForcedRange = Math.Max(0, r); break;
        case "reload": LoadConfig(); FleetLoad(true); LaunchLoad(); KzLoad(); AiGateLoad(); FoLeash = Math.Max(FoLeash, 2200); FollowLoad(); GetBlocks(); break;
        case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;
    }
}

void SetMode(Mode m) { _mode = m; _ready = false; if (m == Mode.IDLE || m == Mode.ASSIST) Release(); }

// =====================================================================
void LoadConfig()
{
    MyIniParseResult res;
    if (!_ini.TryParse(Me.CustomData, out res)) { _err = "CustomData parse error line " + res.LineNo; return; }
    const string S = "Longbow";
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
    ForcedRange = Cfg(S, "Forced Range (0: auto)", ForcedRange);
    GuardRadius = Cfg(S, "Guard Radius", GuardRadius);
    MaxSpeed = Cfg(S, "Combat Speed", MaxSpeed);
    CruiseSpeed = Cfg(S, "Cruise Speed", CruiseSpeed);
    DriftSpeed = Cfg(S, "Drift Speed While Aiming", DriftSpeed);
    RushOrbit = Cfg(S, "Rush Dodge Speed", RushOrbit);
    RushRange = Cfg(S, "Rush Detect Range", RushRange);
    DodgeRange = Cfg(S, "Rush Dodge Range", DodgeRange);
    RushSpeed = Cfg(S, "Rush Flee Speed", RushSpeed);
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
    LeadScale = Cfg(S, "Lead Scale (1: as calculated)", LeadScale);
    LeadMargin = Cfg(S, "Aim Margin (predicted misses)", LeadMargin);
    PreferRange = Cfg(S, "Preferred Engage Range", PreferRange);
    AimWindowMax = Cfg(S, "Max Aim Window m (railgun)", AimWindowMax);
    AimWindowMaxLarge = Cfg(S, "Max Aim Window m (big target)", AimWindowMaxLarge);
    CalmAccel = Cfg(S, "Calm Target Accel m/s2 (0 off)", CalmAccel);
    CalmWait = Cfg(S, "Max Wait For Calm Target s", CalmWait);
    AccFastW = Cfg(S, "Fresh Acceleration Weight (0: old smoothing)", AccFastW);
    AccFastK = Cfg(S, "Fresh Acceleration Scale", AccFastK);
    FreeFar = Cfg(S, "Full Thrust While Travelling", FreeFar);
    FreeFarDist = Cfg(S, "Travelling Distance m", FreeFarDist);
    Coast = Cfg(S, "No Thrust Around The Shot", Coast);
    ReadyLead = Cfg(S, "Retrigger Lead s", ReadyLead);
    RetryAfter = Cfg(S, "Retrigger After Refusal s", RetryAfter);
    HoldRailInSearch = Cfg(S, "Railgun Off While AI Searches", HoldRailInSearch);
    HoldRailMaxS = Cfg(S, "Railgun Off Longest After First Lock s", HoldRailMaxS);
    FireRangeFactor = Cfg(S, "Fire Range Factor", FireRangeFactor);
    FireRangeSlack = Cfg(S, "Fire Range Slack", FireRangeSlack);
    UseTargetAccel = Cfg(S, "Use Target Acceleration", UseTargetAccel);
    SplitFocus = Cfg(S, "Split Targets Across Hive (false: focus fire)", SplitFocus);
    DisabledGrace = Cfg(S, "Seconds Before Dropping Disabled Target", DisabledGrace);
    EvadeSpeed = Cfg(S, "Evade Speed When Hit", EvadeSpeed);
    AimMode = Cfg(S, "Aim Point (AUTO/BREACH/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();
    BreachAuto = Cfg(S, "Breach Large Ships (AUTO)", BreachAuto);
    BreachMinRadius = Cfg(S, "Breach Min Target Radius (m)", BreachMinRadius);
    BreachStall = Cfg(S, "Breach Stall (s)", BreachStall);
    SeekerGroup = Cfg(S, "Seeker Group (PowerSystems/Weapons/Propulsion)", SeekerGroup).Trim();
    SeekerOn = Cfg(S, "Seeker Turret Guides Aim", SeekerOn);
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
    GridTerminalSystem.GetBlocksOfType(_cams, b => b.IsSameConstructAs(Me));
    SeekerSetup();
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
        ProjSpeed = 800; WeaponRange = 800 * 0.85; ReloadTime = 0; GravityDrop = false; _slowWeapon = false; ShotDelay = 0;
    }
    // artillery / assault cannon: ballistics vary by version - set Custom values and Auto Detect Weapon=false
}

// =====================================================================
//  TARGETS
bool Valid(MyDetectedEntityInfo e)
{
    FaSee(e);
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
        if (Ignored(d.Item1) || d.Item1 == Me.CubeGrid.EntityId || _mates.ContainsKey(d.Item1) || !FleetOk(d)) continue;   // (a feed can name my own grid or a wingmate)
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
        if (probingAim && !e.IsEmpty() && e.EntityId == _tgt.Id) BreachHit(_tgt, e, aim);
        else if (probingAim && (e.IsEmpty() || !BreachMode(_tgt))) AimMissed(_tgt);   // breach: a drone in the way is no miss
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
    KzAssess();   // KAMIKAZE DEFENCE: classify incoming kamikazes before AssessTargets / scoring
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
        if (_mode == Mode.GUARD && Vector3D.Distance(p, home) > GuardRadius && !KzLive(t.Id)) continue;
        if (_mode == Mode.FOLLOW && Vector3D.DistanceSquared(p, home) > FoEngage * FoEngage) continue;   // FOLLOW: only threats near the owner
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
        score += HiveScore(t) + KzScore(t);
        if (score < bestScore) { bestScore = score; best = t; }
    }
    _tgt = best;
}

Vector3D Home()
{
    if (_ready && _mode == Mode.GUARD) return _home;
    if (_mode == Mode.FOLLOW && FollowKnown()) return FollowPos();   // FOLLOW: the owner (ship link or on foot), see the FOLLOW block
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
    if (ForcedRange > 0) return Math.Min(ForcedRange, WeaponRange * RangeMargin);
    double floor = t.Type == MyDetectedEntityType.SmallGrid ? DodgeSmall : t.Type == MyDetectedEntityType.LargeGrid ? DodgeLarge : DodgeChar;
    double a = Math.Max(floor, t.PeakAcc * 1.2);
    double miss = HitFraction * t.Radius;
    double tt = (-VelErr + Math.Sqrt(VelErr * VelErr + 2 * a * miss)) / a;
    // Measured (sim, both sides locked, 40 seeds x 7 opponents): wins of Longbow S by stand-off 350 m 45 %, 500 m 57 %, 700 m 69 %,
    // 900 m 75 %, 1100 m 77 %; the old "max accurate range" gave 73 %. A longer stand-off hits less often but takes far less fire.
    // So the accuracy formula only matters for big targets (it asks for more than PreferRange there).
    double r = Math.Max(tt * ProjSpeed, _slowWeapon ? PreferRange : 0);
    return MathHelper.Clamp(r, Math.Min(MinEngage, WeaponRange * RangeMargin), WeaponRange * RangeMargin);
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
        if (_now < _needMoveUntil) _powerScale = 1;
    }
    long ammo = 0; int working = 0;
    foreach (var g in _guns)
    {
        if (!g.IsFunctional) continue;
        working++;
        var inv = g.GetInventory(0); if (inv != null) ammo += inv.CurrentVolume.RawValue;
    }
    _sabots = ammo > 0 ? 1 : 0;
    if (ammo > 0) _hadAmmo = true;
    if (_mode == Mode.GUARD || _mode == Mode.HUNT || _mode == Mode.FOLLOW)
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
    // A railgun is fired with ShootOnce only. If the trigger is held (an AI block or an old toggle left Shoot_On set) it fires by itself the
    // moment it is charged, at whatever the nose points at, and IsShooting never ends so no shot is ever confirmed. Let go of it.
    if (!_aiOn && _tick % 6 == 2)
        foreach (var s in _gunState) if (s.PendingAt < 0 && s.B.IsFunctional && s.B.IsShooting) { s.B.ApplyAction("Shoot_Off"); }
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
            // (the capacitor is empty from this moment and full again ReloadTime later: try then, not half a second later)
            _shootFlag = true;
            s.PendingAt = -1; s.LastShot = _now; s.ReadyAt = _now + ReloadTime - ReadyLead; _shots++;
            _last = "Shot " + _shots + " @ " + _engage.ToString("0") + " m (gun busy)";
        }
        else if (_shootFlag && !s.SawShoot && _now - s.PendingAt > 0.25)
        {   // this gun does report IsShooting for an accepted shot and did not: the capacitor was not full yet - ask again very soon
            s.PendingAt = -1; s.ReadyAt = _now + RetryAfter;
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
    if (LaunchStep(dt, _mode == Mode.IDLE, StartMode, _dynHomeT > 0, _dynHome, Command)) return;   // automatic take-off after the release from a printer (LAUNCH block)
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
                        gun.B.ShootOnce();
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
    Vector3D want, home = Home(), lat = Vector3D.Zero; double threat = 0;
    if (_mode != Mode.RTB && _now - _dynHomeT < 5 && home == _dynHome) { Vector3D o = me - home; home += Vector3D.Normalize(o.LengthSquared() > 1 ? o : _rc.WorldMatrix.Up) * (FollowRadius() + 40); }   // the carrier link's HOME is the ship's centre: hold on a sphere round it, on my side, never inside the hull
    if (_mode == Mode.RTB || _tgt == null)
    {
        Vector3D d = home - me; double dist = d.Length();
        double v = Math.Min(Math.Min(CruiseSpeed, dist * 0.5), Math.Sqrt(2 * aBrake * dist));
        want = (dist > 0.5 ? d / dist * v : Vector3D.Zero) + Evade() + Separation(me);
        if (_mode == Mode.FOLLOW && FollowKnown()) want = FollowWant(me, aBrake, Separation(me)) + Evade();   // own slot by the owner, matching its velocity
        if (_mode == Mode.RTB && dist < 25 && myVel.Length() < 2) { SetMode(Mode.IDLE); _err = "Home - idle"; return; }
    }
    else
    {
        Vector3D tp = _tgt.P(_now), tv = _tgt.V(_now);
        Vector3D d = tp - me; double dist = d.Length();
        Vector3D dir = dist > 1 ? d / dist : _rc.WorldMatrix.Forward;
        double e = dist - _engage;
        if (_slowWeapon && FreeFar && Math.Abs(e) > FreeFarDist) { _needMoveUntil = _now + 1.0; _powerScale = 1; }
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
        // ANTI-RUSH: a big ship (radius >= 25 m) closing at 25+ m/s, or running 40+ m/s straight at us, within Rush Detect Range. Out-running beats out-turning it: flee along its heading
        // at Rush Flee Speed (the game's own cap, so a 100 m/s charger never gains). If it still gets inside Rush Dodge Range, burst across its path
        // (its heading follows slowly), every drone in its own direction round the charger's heading, so it can only turn on one of them.
        double closing = -Vector3D.Dot(tv - myVel, dir);
        lat = tan * DriftSpeed * _driftSign;
        double tsp = tv.Length();   // charging = fast and pointing at us (the closing speed alone drops to 0 once we match its speed)
        if (_tgt.Radius >= 25 && dist < RushRange && (closing > 25 || (tsp > 40 && -Vector3D.Dot(tv, dir) > 0.7 * tsp)))
        {
            threat = dist < DodgeRange ? 2 : 1;
            if (threat == 2)
            {
                Vector3D tvd = tv.LengthSquared() > 1 ? Vector3D.Normalize(tv) : -dir, u = Vector3D.Cross(tvd, upRef);
                u = u.LengthSquared() > 1e-4 ? Vector3D.Normalize(u) : tan;
                double th = (int)(Me.EntityId & 7) * 0.785398;
                lat = (u * Math.Cos(th) + Vector3D.Cross(tvd, u) * Math.Sin(th)) * RushOrbit;
            }
        }
        want = (threat == 2 ? tv * 0.3 : tv + dir * vr) + lat + evade + (aimingPhase ? Vector3D.Zero : Evade()) + Separation(me);
        if (_mode == Mode.GUARD && Vector3D.Distance(me, home) > GuardRadius * 1.3) want = Vector3D.Normalize(home - me) * CruiseSpeed;
        if (_mode == Mode.FOLLOW && FollowFar(me)) want = FollowBack(me);   // leash: drop the fight, back to the owner
    }
    double cap = (_tgt != null && _mode != Mode.RTB) || _mode == Mode.FOLLOW ? MaxSpeed : CruiseSpeed;
    want = KzWant(want, me, cap);   // KAMIKAZE DEFENCE: back away / break sideways
    want = FaWant(want, me);   // FRIENDLY AVOIDANCE: keep clear of non-enemy grids
    if (threat > 0) cap = Math.Max(cap, RushSpeed);
    if (threat == 2 && want.LengthSquared() > cap * cap)
    {   // dodging: keep the sideways burst, give up part of the rest instead
        Vector3D r = want - lat; double qa = r.LengthSquared(), qb = Vector3D.Dot(r, lat), qc = lat.LengthSquared() - cap * cap;
        want = r * (qa > 1e-6 && qc < 0 ? Math.Min(1, (-qb + Math.Sqrt(qb * qb - qa * qc)) / qa) : 0) + lat;
    }
    if (want.LengthSquared() > cap * cap) want = Vector3D.Normalize(want) * cap;
    // gentler velocity loop while settling a shot, so thrust kicks don't shake the aim
    Vector3D acc = (want - myVel) * (aimingPhase ? 1.0 : 2.0);
    // thrusters off while the shot is pending and in a short window before the trigger (off-centre thrust turns the ship, a velocity change
    // moves the slug, which inherits the velocity of the moment it leaves the barrel). The window is bounded (0.25 s, once per 1.25 s): coasting
    // for as long as a shot is merely wanted (mate in the line, aim not settled) stalls the ship - an unbounded version took 2xMantis vs 3xGnat from 88 % to 40 %
    if (_slowWeapon && Coast)
    {   // a window of 0.25 s right before the trigger, at most once per 1.25 s (a shot that is held back for other reasons must not stall the ship)
        bool pre = !pending && toReady <= 0.05 && _settled >= SettleTicks - 3;
        if (pre && _now - _coastOpen > 1.25) _coastOpen = _now;
        if (pending || (pre && _now - _coastOpen <= 0.25)) acc = Vector3D.Zero;
    }
    if (acc.LengthSquared() > 900) acc = Vector3D.Normalize(acc) * 30;
    ApplyThrust((acc - grav) * mass);
}

// Intercept with target acceleration and projectile gravity drop.
Vector3D _aimLos; double _aimNeed;   // line of fire and required hull margin for AimOffset's choice (LEAD block)
double LeadMargin = 1.0;             // aim point keeps this many predicted misses of known hull around it
// Shooter frame: s*t*dir = rp + rv*t + 0.5*(aT - g)*t^2
bool Lead(Track t, Vector3D shooter, Vector3D shooterVel, Vector3D grav, out Vector3D point, out Vector3D dir, out double travel)
{
    // NOTE on the railgun's shot delay (0.5 s small / 2 s large between the trigger and the slug leaving the barrel): the gun
    // keeps tracking this very solution until the slug is away, so the direction it fires in is the solution of the moment
    // of firing. Leading the target by the delay on top of that over-leads by exactly the delay (measured: hit rate 85 % -> 5 %).
    // The delay costs us elsewhere: the target has delay + flight time to change its mind (EngageRange, CalmGate).
    // v2.4: the LEAD block's manoeuvre horizon and aim point (across the line of fire, hull margin for the predicted miss)
    Vector3D c0 = t.P(_now), tv = t.V(_now);
    Vector3D tacc = UseTargetAccel ? t.EA() : Vector3D.Zero;
    Vector3D rp0 = c0 - shooter;
    double rl = rp0.Length(), h = LeadHold(t);
    Vector3D los = rl > 1 ? rp0 / rl : Vector3D.Zero;
    _aimLos = los; _aimNeed = LeadMargin * LeadMiss(t, rl / ProjSpeed);
    Vector3D off = AimOffset(t), tp = c0 + LeadOffset(t, off, los, rl / ProjSpeed);
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
    for (int i = 0; i < 6; i++) { p = rp0 + LeadOffset(t, off, los, tt) + (rv * tt + tacc * LeadK(tt, h)) * LeadScale - 0.5 * g * tt * tt; tt = p.Length() / s; }
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

void SetFire(bool on)
{
    if (on == _firing && _tick % 30 != 0) return;   // re-send the state twice a second (the AI blocks may have switched the guns on or off)
    _firing = on;
    foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off");
}

void ReleaseGyros() { foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = g.Yaw = g.Roll = 0; } _havePrev = false; }

void Release()
{
    SetGunsEnabled(true); HoldRail(false);   // (the friendly-fire guard of the AI phase may have switched them off)
    ReleaseGyros();
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
    SetFire(false);
}

// =====================================================================
void Display()
{
    _sb.Clear();
    _sb.Append("LONGBOW SNIPER AI v").Append(VER).Append('\n');
    _sb.Append("Mode   : ").Append(_ready ? "READY" : _mode.ToString()).Append("   ").Append(_wName).Append('\n');
    _sb.Append(FollowStatus());
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
    _sb.Append(HiveStatus()); _sb.Append(KzStatus());
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
        SetB(s, FoFoot, "Detect Owner", "DetectOwner");   // FOLLOW on foot (owner entities are never targets: Valid)
        SetB(s, false, "Detect Friendly", "DetectFriendly");
        SetB(s, TargetSmall, "Detect Small Ships", "DetectSmallShips");
        SetB(s, TargetLarge, "Detect Large Ships", "DetectLargeShips");
        SetB(s, TargetLarge, "Detect Stations", "DetectStations");
        SetB(s, TargetChars || FoFoot, "Detect Players", "DetectPlayers");
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
        foreach (var e in _sensed) { FollowSensed(e); if (Valid(e)) AddContact(e, "SENSOR"); }
    }
}

// Idle search attitude: keep yawing round while nodding the nose up and down (+/-34 deg, 16 s period),
// so the camera scan cone covers above and below as well as the horizon.
Vector3D SweepDir(Vector3D gravUp)
{
    if (_now - _hitDirT < 10)
    {   // just hit: circle the nose cameras 15-45 deg round the side that was hit, where the shooter is
        Vector3D a = Vector3D.Cross(_hitDir, Math.Abs(_hitDir.Y) < 0.9 ? Vector3D.Up : Vector3D.Right); a.Normalize();
        Vector3D b = Vector3D.Cross(_hitDir, a);
        double ph = _now * 1.3, rr = 0.27 + 0.5 * (0.5 + 0.5 * Math.Sin(_now * 0.37));
        return Vector3D.Normalize(_hitDir + (a * Math.Cos(ph) + b * Math.Sin(ph)) * rr);
    }
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
// Under fire (or on a hive alarm) the side cameras reach HardScanRange: the shooter is usually a turret or gatling at up to 800 m.
// The forward cameras stay out of it while the long auto-scan, the AI confirm rays or a lock need them: short close-scan rays used to
// drain them below the auto-scan's range, so a drone under fire from 700 m never cast a long ray (in-game report 2026-10-13).
double HardScanRange = 900;
void OmniScan()
{
    if (!CloseScan || _cams.Count == 0) return;
    for (int shots = 0, tries = 0; shots < 3 && tries < _cams.Count; tries++)
    {
        var c = _cams[_omniCam++ % _cams.Count];
        bool hard = UnderFire || Alerted;
        // nothing to track: the side cameras look far too (a 120 m close scan never finds the ship that is shooting from 700 m)
        double rng = hard || _tgt == null ? Math.Max(CloseScanRange * 4, HardScanRange) : CloseScanRange;
        if (!c.IsWorking || (Forwardish(c) && !(hard && _tgt == null && !_aiOn && !AutoScan)) || c.AvailableScanRange < rng * 2) continue; // keep charge for tracking
        _omniIdx++;
        MyDetectedEntityInfo e; Vector3D hp;
        if (hard && _tgt == null && _now - _hitDirT < 10 && HitPoint(c, rng, out hp)) e = c.Raycast(hp);   // just hit: look where the shots came from
        else
        {
            double r = 40 * Math.Sqrt((_omniIdx % 89 + 0.5) / 89.0), th = _omniIdx * 2.39996323;
            e = c.Raycast(rng, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
        }
        if (Valid(e)) AddContact(e, "SCAN");
        shots++;
    }
}
// A point rng metres out, inside 35 deg of the side that was hit (golden-angle spiral), that camera c can see; false if none of 4 tries is in its cone.
int _hitIdx = 0;
bool HitPoint(IMyCameraBlock c, double rng, out Vector3D pt)
{
    Vector3D a = Vector3D.Cross(_hitDir, Math.Abs(_hitDir.Y) < 0.9 ? Vector3D.Up : Vector3D.Right); a.Normalize();
    Vector3D b = Vector3D.Cross(_hitDir, a), cp = c.GetPosition();
    for (int k = 0; k < 4; k++)
    {
        _hitIdx++;
        double r = Math.Tan(0.61 * Math.Sqrt((_hitIdx % 97 + 0.5) / 97.0)), th = _hitIdx * 2.39996323;
        Vector3D d = Vector3D.Normalize(_hitDir + (a * Math.Cos(th) + b * Math.Sin(th)) * r);
        pt = cp + d * rng;
        if (Vector3D.Dot(d, c.WorldMatrix.Forward) > 0.72) return true;
    }
    pt = Vector3D.Zero; return false;
}

// =====================================================================
//  v1.8: TARGET STATUS, DAMAGE SENSING, SPLIT FOCUS, ASSIST
Dictionary<long, double> _ignore = new Dictionary<long, double>();
Dictionary<long, int> _share = new Dictionary<long, int>(), _count = new Dictionary<long, int>();
List<Track> _live = new List<Track>();
List<IMySlimBlock> _slims = new List<IMySlimBlock>();
List<double> _slimHp = new List<double>(); Vector3D _hitDir; double _hitDirT = -99;
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
    if (KzLive(t.Id)) { t.SpinSince = -1; return false; }   // KAMIKAZE DEFENCE: a kamikaze correcting its course spins like a wreck
    if (KzSpent(t)) return true;   // KAMIKAZE DEFENCE: a former kamikaze with no thrust that is no longer coming for anyone
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
    // which side was hit: blocks that lost integrity since the last check, as directions from the grid centre (the shooter is out that way)
    bool cmp = _integ >= 0 && _slimHp.Count == _slims.Count;
    if (!cmp) { _slimHp.Clear(); for (int i = 0; i < _slims.Count; i++) _slimHp.Add(0); }
    Vector3D hd = Vector3D.Zero, gc = g.WorldVolume.Center;
    for (int i = 0; i < _slims.Count; i++)
    {
        var s = _slims[i];
        double v = g.CubeExists(s.Position) ? s.BuildIntegrity - s.CurrentDamage : 0;
        sum += v;
        if (cmp && _slimHp[i] - v > 0.5) { Vector3D d = g.GridIntegerToWorld(s.Position) - gc; double l = d.Length(); if (l > 0.3) hd += d / l; }
        _slimHp[i] = v;
    }
    if (hd.LengthSquared() > 0.01)
    {
        hd.Normalize();
        Vector3D m = _now - _hitDirT < 3 ? _hitDir * 0.6 + hd * 0.4 : hd;
        if (m.LengthSquared() > 0.01) { _hitDir = Vector3D.Normalize(m); _hitDirT = _now; }
    }
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
    if (_tgt != null) s += "\nAim: " + AimLabel(_tgt) + " (" + _tgt.Hull.Count + " hull pts)" + AimInfo();
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
    if (BreachMode(t)) return "BREACH";
    return AimMode == "AUTO" ? (t.Thrust.LengthSquared() > 0.09 ? "ENGINES" : "SPREAD") : AimMode;
}
Vector3D AimOffset(Track t)
{
    SeekerTick(t);
    if (!AimActive(t)) return Vector3D.Zero;
    if (BreachMode(t)) return BreachOffset(t);
    if (_now - t.AimT > 0.5)
    {
        string mode = AimLabel(t);
        int slot = (int)Math.Abs(Me.CubeGrid.EntityId % 9973);
        int step = slot + (int)(_now / 4);
        // only hull points with enough known hull round them for this shot's predicted miss (LEAD block)
        var keep = LeadPick(t.Hull, Vector3D.TransformNormal(_aimLos, MatrixD.Transpose(t.O)), _aimNeed);
        int n = keep.Count;
        if (mode == "SPREAD") t.AimLocal = t.Hull[keep[step % n]];
        else
        {
            Vector3D axis = mode == "ENGINES" ? -t.Thrust
                : Vector3D.TransformNormal(_rc.GetPosition() - t.P(_now), MatrixD.Transpose(t.O));
            if (axis.LengthSquared() < 1e-6) axis = Vector3D.Forward;
            axis.Normalize();
            _aimIdx.Clear(); _aimIdx.AddRange(keep);
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
    if ((k & 3) == 0 && AimActive(t) && _now - t.Seen < 0.5) { probingAim = true; return centre + AimOffset(t) + dir * (BreachMode(t) ? BreachProbe : 1.5); }
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
    if (BreachMode(t)) { BreachMiss(); return; }
    for (int i = t.Hull.Count - 1; i >= 0; i--) if ((t.Hull[i] - t.AimLocal).LengthSquared() < 9) t.Hull.RemoveAt(i);
    t.AimT = -99;
}

// >>> BREACH + SEEKER v1.0 (shared block: identical in every armed drone script, kept in sync by tools/aim_sync.py) >>>
// BREACH (large ships): spreading hits over a big hull chips thousands of armour blocks and destroys none (sim vs a 13,666-block
// warship). BREACH holds ONE hull point and keeps every round on it: the known hull point nearest the target's centre (on a long
// ship that is mid-deck or mid-belly, where the armour over the core is thinnest). Probe rays at it that come back deeper show the
// hole; the drone then aims down the hole, towards the centre. No progress for "Breach Stall (s)", or two probe rays in a row that
// find nothing there: that point is dropped and the next one is used. AUTO uses BREACH on large grids of radius >= Breach Min
// Radius; "aim breach" forces it, "breach off" turns the AUTO switch off.
// SEEKER (in-game test of "let the game find the vitals"): a turret on this drone with [Seeker] in its name is set to the targeting
// group "Seeker Group" (PowerSystems / Weapons / Propulsion; vanilla turrets and Custom Turret Controllers aim at the blocks of
// that group). Twice a second the script reads where that turret points (lead removed), raycasts that line with its cameras, and
// the hull point hit - right over the subsystem - becomes the breach point; the hole is then drilled along the turret's line.
// What the turret reports is shown on the status screen ("Seek:" line), so an in-game test shows whether it works.
// Decoys pull turrets (and the seeker) onto themselves first; static guns are never distracted. "seeker off" ignores the turret.
// The turret must reach the target: gatling / rocket turrets about 800 m, assault cannon 1400 m, artillery 2000 m.
bool BreachAuto = true; double BreachMinRadius = 25, BreachStall = 30, BreachProbe = 30;
string SeekerGroup = "PowerSystems"; bool SeekerOn = true;
long _bId; bool _bOn, _bSeek; Vector3D _bS, _bDir; double _bDepth, _bProgT; int _bMiss;
List<Vector3D> _bBad = new List<Vector3D>();
List<IMyLargeTurretBase> _skT = new List<IMyLargeTurretBase>();
bool _skOk; long _skId; Vector3D _skS, _skDir; double _skT0 = -99; string _skLog = "";

bool BreachMode(Track t)
{
    if (AimMode == "BREACH") return true;
    return AimMode == "AUTO" && BreachAuto && t.Type == MyDetectedEntityType.LargeGrid && t.Radius >= BreachMinRadius;
}
bool BreachBad(Vector3D h) { foreach (var q in _bBad) if ((q - h).LengthSquared() < 36) return true; return false; }
void BreachSet(Vector3D s, Vector3D dir, bool seek)
{
    _bS = s; _bDir = dir; _bSeek = seek; _bOn = true; _bDepth = 0; _bProgT = _now; _bMiss = 0;
}
Vector3D BreachOffset(Track t)
{
    if (_bId != t.Id) { _bId = t.Id; _bOn = false; _bBad.Clear(); _skOk = false; }
    if (_bOn && _now - _bProgT > BreachStall) BreachDrop();
    // a seeker point replaces a plain breach point at once, and an old seeker point once its hole has stalled for 6 s
    if (_skOk && _skId == t.Id && !BreachBad(_skS) && (!_bOn || !_bSeek || ((_skS - _bS).LengthSquared() > 64 && _now - _bProgT > 6)))
    { BreachSet(_skS, _skDir, true); _skOk = false; }
    if (!_bOn)
    {
        double best = double.MaxValue; Vector3D s = t.Hull[0];
        foreach (var h in t.Hull) { if (BreachBad(h)) continue; double d = h.LengthSquared(); if (d < best) { best = d; s = h; } }
        if (best == double.MaxValue) _bBad.Clear();
        double l = s.Length();
        BreachSet(s, l > 1e-3 ? -s / l : Vector3D.Zero, false);
    }
    Vector3D a = _bS + _bDir * _bDepth;                    // down the hole
    t.AimLocal = a;
    return Vector3D.TransformNormal(a, t.O);
}
// a probe ray at the breach point hit the target: how far past the surface point did it get?
void BreachHit(Track t, MyDetectedEntityInfo e, Vector3D aim)
{
    if (!_bOn || _bId != t.Id || !e.HitPosition.HasValue || !AimActive(t) || !BreachMode(t)) return;
    _bMiss = 0;
    Vector3D rd = aim - Me.GetPosition();
    if (rd.LengthSquared() < 1) return;
    rd.Normalize();
    double d = Vector3D.Dot(e.HitPosition.Value - (e.Position + Vector3D.TransformNormal(_bS, e.Orientation)), rd);
    if (d > _bDepth + 0.5) { _bDepth = Math.Min(d, 60); _bProgT = _now; }
}
void BreachMiss() { if (++_bMiss >= 2) BreachDrop(); }
void BreachDrop()
{
    if (_bOn) { _bBad.Add(_bS); if (_bBad.Count > 12) _bBad.RemoveAt(0); }
    _bOn = false; _bSeek = false; _bMiss = 0;
}
string AimInfo()
{
    string s = "";
    if (_bOn && _tgt != null && _bId == _tgt.Id && BreachMode(_tgt)) s += " hole " + _bDepth.ToString("0") + " m" + (_bSeek ? " (seeker)" : "");
    if (_skT.Count > 0) s += "\nSeek: " + _skLog;
    return s;
}
// seeker turrets: [Seeker] in the name, on this grid; set to the targeting group (needs the 1.200+ turret API)
void SeekerSetup()
{
    _skT.Clear();
    if (!SeekerOn) { _skLog = ""; return; }
    GridTerminalSystem.GetBlocksOfType(_skT, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("[Seeker]"));
    if (_skT.Count == 0) _skLog = ""; else if (_skLog == "") _skLog = "ready, group " + SeekerGroup;   // (re-scans keep the last report)
    foreach (var tu in _skT)
    {
        try { if (SeekerGroup.Length > 0 && tu.GetTargetingGroup() != SeekerGroup) tu.SetTargetingGroup(SeekerGroup); }
        catch (Exception x) { _skLog = "group not set: " + x.Message; }
    }
}
// called from AimOffset every tick (rate-limited here), whatever the aim mode, so it also maps the hull of a turret-fed track
void SeekerTick(Track t)
{
    if (!SeekerOn || _skT.Count == 0 || _now - _skT0 < 0.5) return;
    _skT0 = _now;
    foreach (var tu in _skT)
    {
        if (!tu.IsFunctional) { _skLog = "turret damaged"; continue; }
        if (!tu.HasTarget) { _skLog = "turret has no target (group " + tu.GetTargetingGroup() + ")"; continue; }
        var te = tu.GetTargetedEntity();
        if (te.EntityId != t.Id) { _skLog = "turret on another target"; continue; }
        if (!tu.IsAimed) { _skLog = "turret turning"; continue; }
        Vector3D o = tu.GetPosition(), q;
        bool hp = te.HitPosition.HasValue && (te.HitPosition.Value - te.Position).LengthSquared() > 1;
        if (hp) q = te.HitPosition.Value;                  // the game told us the block itself
        else
        {
            Vector3D d; Vector3D.CreateFromAzimuthAndElevation(tu.Azimuth, tu.Elevation, out d);
            d = Vector3D.TransformNormal(d, tu.WorldMatrix);
            double D = Vector3D.Dot(t.P(_now) - o, d);
            if (D < 5) { _skLog = "turret points away"; continue; }
            // turrets lead their target; take the lead off again (bullets inherit our velocity)
            Vector3D rv = t.V(_now) - _rc.GetShipVelocities().LinearVelocity;
            q = o + d * D - rv * (D / SeekerShotSpeed(tu));
        }
        var cam = CamFor(q, 0);
        if (cam == null) { _skLog = "no camera charge/angle"; continue; }
        Vector3D cd = q - cam.GetPosition(); double cl = cd.Length(); if (cl < 1) continue; cd /= cl;
        var e = cam.Raycast(q + cd * BreachProbe);
        if (e.IsEmpty() || e.EntityId != t.Id || !e.HitPosition.HasValue)
        { _skLog = (hp ? "block pos given, " : "aim ray, ") + "ray missed hull"; continue; }
        StoreHull(t, e);                                   // a turret-fed track has no camera lock: these rays map its hull
        MatrixD inv = MatrixD.Transpose(e.Orientation);
        _skS = Vector3D.TransformNormal(e.HitPosition.Value - e.Position, inv);
        _skDir = Vector3D.TransformNormal(cd, inv);
        _skOk = true; _skId = t.Id;
        _skLog = (hp ? "block pos given" : "aim ray") + ", " + (q - e.HitPosition.Value).Length().ToString("0") + " m under hull";
        return;
    }
}
// muzzle speed of the turret's ammunition (m/s), to take its lead off again
double SeekerShotSpeed(IMyLargeTurretBase tu)
{
    string st = tu.BlockDefinition.SubtypeName;
    if (st.Contains("Calibre")) return 500;               // artillery / assault cannon shells
    if (tu is IMyLargeMissileTurret) return 200;          // rockets (mean speed over a few hundred metres)
    return 800;                                            // gatling, autocannon, interior turret
}
// <<< BREACH + SEEKER <<<

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
    AiGateFind(_ai);
}
// While the AI blocks have the controls they also pull the trigger at whatever is in front of the nose, wingmates included
// (found in the sim: 13,000-50,000 damage slugs into the neighbour 70 m away during the first seconds). So: switch the guns
// off while a hive mate is in the line of fire, back on as soon as it is clear or the script takes over again.
bool _gunsOff = false; double _friendRay = -99;   // last time an AI confirm ray hit a friendly (not necessarily in my hive)
void SetGunsEnabled(bool on)
{
    if (on != _gunsOff) return;
    _gunsOff = !on;
    ApplyRail();
}
// While the AI blocks search (nothing locked) they pull the trigger at whatever they find, at 1+ km, and the first slug of the fight (the
// capacitor starts full) is gone before the script has a lock. Until the script has fired its own first slug the railgun is switched off in
// that phase, back on for the script's aimed shot (later in the fight the AI's shots at what it found are welcome: 2xLongbow S vs 3xGnat 20 %
// with this limit, 10 % with the railgun off whenever nothing is locked, 22 % without).
bool HoldRailInSearch = true, _railHeld = false;
double HoldRailMaxS = 60, _firstLockT = -1;   // give the hold up if the script has not fired its own first slug 60 s after its first lock (it cannot aim: let the AI shoot)
void ApplyRail() { bool on = !_gunsOff && !_railHeld; foreach (var g in _guns) if (g.IsFunctional && g.Enabled != on) g.Enabled = on; }
void HoldRail(bool hold) { if (hold == _railHeld) return; _railHeld = hold; ApplyRail(); }
void AiFriendGuard()
{
    HoldRail(HoldRailInSearch && _slowWeapon && _tgt == null && _shots == 0 && (_firstLockT < 0 || _now - _firstLockT < HoldRailMaxS));
    if (!FriendCheck || _aimRef == null) return;
    _myVel = _rc.GetShipVelocities().LinearVelocity;
    SetGunsEnabled(!(MatesInLine(_aimRef.GetPosition(), _aimRef.WorldMatrix.Forward, WeaponRange, ProjSpeed, 0) || _now - _friendRay < 1.5));
}
bool AiStep()
{
    if (_tgt != null) { _lastTgtT = _now; if (_firstLockT < 0) _firstLockT = _now; }
    // The AI also takes over while my lock is stale (nothing seen for AiStaleTakeover s): it keeps the nose on the enemy
    // and the confirm rays below pick it up again, which hands control straight back to the script.
    bool want = AiAssist && _ai.Count > 0 && AiModeOk() &&
        ((_tgt == null && _now - _lastTgtT > 2 && _now - _ignoreNoteT > 15) || (_tgt != null && _now - _tgt.Seen > (Vector3D.DistanceSquared(_tgt.Pos, _rc.GetPosition()) > 1400.0 * 1400.0 ? AiStaleTakeoverFar : AiStaleTakeover)));
    want = AiGate(want);   // AI GATE: the AI blocks may do nothing (AI Behavior off): time-share with the script's own search
    if (want != _aiOn)
    {
        _aiOn = want;
        _firing = false;                       // guns' state is unknown after any hand-over: SetFire sends it again
        if (want) Release();                    // thrusters and gyros go to the AI
        foreach (var b in _ai) b.Enabled = want;
    }
    if (!_aiOn) { SetGunsEnabled(true); HoldRail(false); if (_tick % 300 == 7) foreach (var g in _guns) if (g.IsFunctional && !g.Enabled) g.Enabled = true; return false; }   // (also heals guns left disabled by a script restart during the AI phase)
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
    return AiGateStatus() + "AI: " + (_aiOn ? "searching" : _agT0 >= 0 ? "own search" : "standby") + " (" + _ai.Count + " blocks)" + (OwnAlert ? "  ALARM" : "") + "\n";
}
bool AiModeOk() { return _mode == Mode.GUARD || _mode == Mode.HUNT; }

// =====================================================================
//  v1.8: PRINTER SUPPORT
//  * A drone printed while merged to a printer shares the printer's grid. Blocks are re-scanned
//    whenever this PB's grid changes (merged / released), so it never drives the printer's thrusters.
//  * "<Tag>_JOIN" from a printer: an IDLE drone (fresh off the printer) starts working; drones that
//    are already busy ignore it.
IMyCubeGrid _myGrid;
void CheckGrid() { if (Me.CubeGrid != _myGrid) { _myGrid = Me.CubeGrid; GetBlocks(); } }
