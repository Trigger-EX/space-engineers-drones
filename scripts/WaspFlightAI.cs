// =====================================================================
//  WASP FLIGHT AI  v2.6-wasp-split  - the FLIGHT half of the two-PB Wasp ("[Flight] ..." Programmable Block)
//  For Space Engineers (vanilla, small grid). Pairs with WaspStaffAI.cs in a second PB named "[Staff] ..." on the SAME grid.
//  Same drone as WaspAI.cs (tracking, lead solver, aim, control, rockets, firing, damage sensing, AI hybrid, kamikaze defence, hive),
//  but the 100,000-character PB limit is no longer in the way: everything that is not on the hot path lives in the Staff PB:
//    FLEET LINK (fleet channel: targets, home, commands), FOLLOW / SATELLITE (slot geometry), LAUNCH (take-off burn), the status display.
//  Why this cut (docs/audit/multi_pb_feasibility.md): the aim loop must not cross a PB boundary (IGC costs 2 ticks), TryRun and timers
//  are not dependable in the game, so Flight keeps the whole hot path and the two PBs only exchange slow data.
//  IGC link (TransmissionDistance.CurrentConstruct, key "WSPL" + Flight PB EntityId, both PBs Update1):
//    .F2S  Flight->Staff  MyTuple<int flags, long tgtId, Vector3D tgtPos, Vector3D aux(aimErrDeg, fireWinDeg, aBrake), float batt, string gateWhy>
//          5 Hz and on a mode change (also the heartbeat). flags: bits 0-2 mode, 3 firing, 4 under fire, 5 lock stale, 6 AI on, 8-15 rockets (255 unknown)
//    .FOL  Staff->Flight  MyTuple<Vector3D want, Vector3D ownerP, Vector3D ownerV, Vector3D dynHome, Vector3D radii(engage, leash, 0), int flags>
//          every 2 ticks in FOLLOW or while a take-off burn runs, else every 30. flags: 0 owner known, 1 on foot, 2 take-off burn, 3 dynHome fresh
//    .S2F  Staff->Flight  string verb (fed to Command; a mode verb equal to the current mode is ignored)
//    .CMD  Flight->Staff  a PB argument given to this PB, for the words that are the Staff's
//    .TGT  Staff->Flight  fleet target relay (the <Tag>_TGT tuple, read by ReadTgt)      .FLT  Flight->Staff  target shares for the fleet channel
//  Staff silent for 1 s: the drone fights on alone (home = _home, engage radius 800, FOLLOW flown as GUARD round the last owner point, no fleet relay).
//  CUSTOM DATA (this PB): all [Wasp] keys of WaspAI.cs (same names), [Kamikaze Defence], [AI Gate]. [Launch] / [Follow] / [Fleet] are in the Staff PB.
//  Commands: see WaspAI.cs (launch / armed / disarm and the fleet commands are handled by the Staff PB).
// =====================================================================

const string VER = "2.6-wasp-split";

// ---------------- settings (overwritten from Custom Data) -------------
string Tag = "WASP";
double ProjSpeed = 200;      // m/s  rocket top speed (informational; the solver uses the Rkt* curve below)
double RktV0 = 100, RktAcc = 600, RktVmax = 200;   // Missile200mm: launch speed, acceleration, cap
string RocketModel = "CAPPED"; // CAPPED: a rocket leaves with our velocity + launch speed, accelerates along the barrel, total speed capped
                              // (fired from a moving ship it curves onto the barrel line; Keen bug tracker); NONE: our velocity is ignored;
                              // FULL: our velocity is kept on top of the rocket's own speed
double LeadMargin = 1.0;      // aim point keeps this many predicted misses of known hull around it (LEAD block)
double MaxFireRangeU = 650, MaxLeadTimeU = 3.5, StandoffU = 350;   // when the rocket stock is unknown/unlimited (creative world: empty inventories) the doctrine is "more rockets, from farther": sim, locked duels, win 34.8 -> 43.6 %
double MaxFireRange = 420;   // rockets die at 800 m; accuracy falls with flight time (sim sweep: 650 -> 420 doubled the hit rate, 31 -> 75 %, and gave more hits per drone)
double MaxLeadTime = 2.0;    // never fire at an intercept further than this many seconds of flight (was 3.2)
double Standoff = 250;       // preferred engage distance (1.3 s of flight)
double MinRange = 60;        // never closer than this
double SafeRange = 25;       // never launch at an intercept closer than this (blast radius 4 m)
double LineClear = 4.5, BlastClear = 9; // hive mates this close to the flight line / predicted blast stop the launch
int SalvoSize = 4; double SalvoPause = 0.8, RocketInterval = 1.05, RippleGap = 0.3;
double JinkGateK = 0.8, JinkGateMin = 3.0; // refuse a launch when the unmodelled manoeuvre over the flight exceeds max(K x radius, Min) metres
int ReserveRockets = 2; double ConserveRange = 400; // with this few rockets left fire only tight, close shots
double GuardRadius = 1500;   // GUARD only engages within this of home
double MaxSpeed = 98;        // combat speed cap
double CruiseSpeed = 60;     // non-combat cap (saves power)
double OrbitSpeed = 40;      // tangential speed around the target (sim sweep: 22 -> 40 raised lock-duel wins 28 -> 37 %, fewer hits taken)
double JinkEvery = 1.0, JinkEveryRand = 1.0;   // a new random evasive direction every JinkEvery + rand x JinkEveryRand seconds
double JinkSpeed = 40;       // random evasive velocity (was 12)
double LockRange = 2500;     // manual "lock" raycast distance
double CloseScanRange = 120; // all-around camera scan distance
bool CloseScan = true;
double ScanRange = 1200;     // auto-scan raycast distance
double ScanCone = 35;        // auto-scan half-angle (deg)
double AimKp = 6, AimKd = 0.35, MaxTurn = 5; // gyro tuning (rad/s)
double FireTol = 1.0;        // multiplier on silhouette fire window
double MinFireWinDeg = 0.1;  // floor of the fire window (deg). Was 0.3 = 3 m at 600 m, wider than a small drone: sim, Gnat hit rate 30 -> 43 % with the aim taken from the launcher that fires next
double MaxSideFireSpeed = 0; // 0 = off. Else: no launch while our speed ACROSS the barrel is above this (m/s): insurance if the rocket velocity model is wrong (sim: 0 -> 15 % hits under the wrong model)
double LeadScale = 1.0;      // scales the lead offset if shots land consistently ahead/behind
bool UseTargetAccel = true;
bool SplitFocus = true;
double DisabledGrace = 5, EvadeSpeed = 20;
int AiBore8 = 6;   // of 8 confirm rays, how many go exactly down the boresight (the rest: Gaussian cone)
bool AiAssist = true; double AiConfirmRange = 1000;   // confirm rays are this long (1.5 casts/s per camera at 2 km/s charge)
double AiCone = 4.5, AiConePitch = 1.5;   // confirm rays spread to this half-angle (deg): the AI aims at the ROCKET lead point (up to 4 s of target motion), not at the target
double AiConfirmLong = 2600;   // the first forward camera casts this long instead (0.77 casts/s)
double RtbBattery = 0.15;    // fraction
double SepRange = 70, SepSpeed = 25;   // wingmate separation: push away from hive mates closer than this (m) at up to this speed (m/s)
bool TargetLarge = true, TargetSmall = true, TargetChars = false, TargetNeutral = false;
bool AutoScan = true, ShareTargets = true, Jink = true, FriendCheck = true;
string StartMode = "IDLE";   // safe default: drone waits for a command

// ---------------- state ----------------------------------------------
enum Mode { IDLE, GUARD, HUNT, ASSIST, RTB, FOLLOW }
Mode _mode = Mode.IDLE;
MyIni _ini = new MyIni();
double _now = 0;
long _tick = 0;
Vector3D _home;
bool _homeSet = false, _ready = false, _sentry = false;
double SentryRange = 2500;   // SENTRY: wake on a target this close (the AI Offensive block's search range). CustomData "Sentry Range (m)"
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
IMyBroadcastListener _lTgt, _lCmd;
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
    public Vector3D LAcc, LSeg, LW, LOR; public int LSegN; public double LSegT0 = -99, LManT = 2;   // LEAD v1.0 state (fresh acceleration, manoeuvres, spin)
    public double Spin, SpinSince = -1, Rad0, Raw, RawMax, DeadAt = -1, DisSince = -1; public Vector3D OF, OU; public bool HasOri;
    public MatrixD O = MatrixD.Identity; public List<Vector3D> Hull = new List<Vector3D>(); public int HullNext;
    public Vector3D Thrust, AimLocal; public double AimT = -99;
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
    } public double Radius, Seen; public string Src;
    public MyDetectedEntityType Type; public string Name;
    public void Set(Vector3D p, Vector3D v, double r, double now, string src)
    {
        LeadSample(this, v, now, now - Seen);
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
bool _firing = false, _showFire = false;
double _holdFireUntil = 0;
Vector3D _jink; double _nextJink = 0; double _orbitSign = 1;
int _scanIdx = 0;
double _sweepYaw = 0;
Random _rng = new Random();
string _err = "";
double _aimErrDeg = 0, _fireWinDeg = 0, _leadT = 0;
double _avgMs = 0; int _peakIns = 0;   // instruction counter: PB limit is Runtime.MaxInstructionCount per run

// >>> AI GATE v1.2 (shared block: identical in every drone script that uses AI-block search, synced by tools/ai_gate_sync.py) >>>
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
// v1.2 (2026-10-14, from the Ballista Helm, unverified in the game): "AI Search Interval (s)" (default 5, 0 = leave the block alone) is written
// to IMyOffensiveCombatBlock.UpdateTargetInterval: the block rescans only every 30 s by default [wiki], longer than the AI Window, so a
// window often saw no search at all. Scripts with an AI Block Search Assist switch call AiGateFind only while it is on (with it off the
// AI blocks stay as they came, "AI Behavior" off: switched on, the AI Flight block may fly the ship away on its own) and re-run FindAi on `ai on|off`.
// Own [AI Gate] section in Custom Data (written on first run, "reload" re-reads it).
// Hooks: AiGateLoad() after FleetLoad(true); AiGateFind(_ai) at the end of FindAi(); want = AiGate(want) right before
// "if (want != _aiOn)" in AiStep(); AiGateStatus() in the status text.
bool AiGateOn = true, AiBehaviorSwitch = true, AiTyped = false;
double AiSpeed = 100, AiWin = 6, ScriptWin = 8, AiHoldMax = 60; int AiSearchEvery = 5;
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
    if (!ini.ContainsKey(S, "AI Search Interval (s)")) ini.Set(S, "AI Search Interval (s)", AiSearchEvery);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    AiGateOn = ini.Get(S, "Time-share With Script Search").ToBoolean(AiGateOn);
    AiBehaviorSwitch = ini.Get(S, "Switch On AI Behavior").ToBoolean(AiBehaviorSwitch);
    AiTyped = ini.Get(S, "Use Typed AI Block API").ToBoolean(AiTyped);
    AiSpeed = Math.Max(1, ini.Get(S, "AI Flight Speed Limit (m/s)").ToDouble(AiSpeed));
    AiWin = Math.Max(1, ini.Get(S, "AI Window (s)").ToDouble(AiWin));
    ScriptWin = Math.Max(1, ini.Get(S, "Script Search Window (s)").ToDouble(ScriptWin));
    AiHoldMax = Math.Max(AiWin, ini.Get(S, "Max AI Hold (s)").ToDouble(AiHoldMax));
    AiSearchEvery = Math.Max(0, ini.Get(S, "AI Search Interval (s)").ToInt32(AiSearchEvery));
}

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
// >>> SPLIT LINK v1.0 (shared block: identical in every Flight half of a two-PB drone, synced by tools/split_sync.py, see docs/design/wasp_split.md) >>>
// Flight side of the Flight/Staff link; the Staff PB (scripts/<Drone>StaffAI.cs) has the other end. Everything that is not on the hot path lives there:
// FLEET LINK, FOLLOW / SATELLITE geometry, LAUNCH (take-off burn), the status screen. IGC key "WSPL" + this PB's EntityId, TransmissionDistance.CurrentConstruct.
// Script hooks: LkInit() with the other listener setup; ReadIGC(); LkRead(); LkSend() in the tick; "if (!LkBurn && !AiStep()) Control(dt)"; "if (LkBurn) return;" first in Control();
// LkWantOk/_lkWant/LkFar/LkBack/LkEngage/_lkOwner/_lkOwnerP in the FOLLOW code; ReadTgt(_lkTgt); LkAmmo() = the script's ammo count (0-254, 255 unknown).
// Uses: _now, _tick, _mode, _firing, _aiOn, UnderFire, LockStale, _tgt, _aimErrDeg, _fireWinDeg, _batFrac, _gateWhy, _dynHome, _dynHomeT, MaxSpeed, ParseMode, Command.
string _lkK = ""; IMyBroadcastListener _lkFol, _lkCmd, _lkTgt;
Vector3D _lkWant, _lkOwnerP, _lkOwnerV; double _lkEng = 800, _lkLeash = 1200, _lkT = -99, _aBrake = 5; int _lkFl, _lkMd = -9; bool _lkOwner;
bool LkAlive { get { return _now - _lkT < 1.0; } }                       // the Staff PB talks (a FOL at least every 0.5 s)
bool LkBurn { get { return LkAlive && (_lkFl & 4) != 0; } }              // Staff flies the take-off burn: Flight lets go of the ship
bool LkWantOk { get { return LkAlive && (_lkFl & 1) != 0; } }
double LkEngage { get { return LkAlive ? _lkEng : 800; } }
void LkInit()
{
    _lkK = "WSPL" + Me.EntityId;
    _lkFol = IGC.RegisterBroadcastListener(_lkK + ".FOL"); _lkCmd = IGC.RegisterBroadcastListener(_lkK + ".S2F"); _lkTgt = IGC.RegisterBroadcastListener(_lkK + ".TGT");
}
bool LkFar(Vector3D me) { double l = LkAlive ? _lkLeash : 1200; return _lkOwner && Vector3D.DistanceSquared(me, _lkOwnerP) > l * l; }
Vector3D LkBack(Vector3D me) { Vector3D d = _lkOwnerP - me; return _lkOwnerV + (d.LengthSquared() > 1 ? Vector3D.Normalize(d) * MaxSpeed : Vector3D.Zero); }
void LkRead()
{
    while (_lkFol.HasPendingMessage)
    {
        var m = _lkFol.AcceptMessage();
        if (!(m.Data is MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>)) continue;
        var d = (MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>)m.Data;
        _lkWant = d.Item1; _lkFl = d.Item6; _lkT = _now; _lkEng = Math.Max(50, d.Item5.X); _lkLeash = Math.Max(_lkEng, d.Item5.Y);
        if ((_lkFl & 1) != 0) { _lkOwner = true; _lkOwnerP = d.Item2; _lkOwnerV = d.Item3; }
        if ((_lkFl & 8) != 0) { _dynHome = d.Item4; _dynHomeT = _now; }
    }
    while (_lkCmd.HasPendingMessage)
    {
        var m = _lkCmd.AcceptMessage();
        if (!(m.Data is string)) continue;
        string v = ((string)m.Data).Trim().ToLowerInvariant();
        if (ParseMode(v.Split(' ')[0], (Mode)(-1)) == _mode) continue;   // a mode verb that changes nothing (a PB argument reaches both PBs)
        Command(v);
    }
}
void LkSend()
{
    int md = (int)_mode;
    if (md == _lkMd && _tick % 12 != 5) return;
    _lkMd = md;
    int f = md | (_firing ? 8 : 0) | (UnderFire ? 16 : 0) | (LockStale ? 32 : 0) | (_aiOn ? 64 : 0) | (LkAmmo() < 0 ? 255 : Math.Min(254, LkAmmo())) << 8;
    var t = _tgt;
    IGC.SendBroadcastMessage(_lkK + ".F2S", new MyTuple<int, long, Vector3D, Vector3D, float, string>(f, t != null ? t.Id : 0, t != null ? t.P(_now) : Vector3D.Zero,
        new Vector3D(_aimErrDeg, _fireWinDeg, _aBrake), (float)_batFrac, LkGate()), TransmissionDistance.CurrentConstruct);
}

// FLEET LINK stubs: the fleet channel is the Staff PB's (WaspStaffAI.cs); target shares go to it on .FLT, fleet targets come back on .TGT
bool FleetV(Vector3D v) { return Math.Abs(v.X) < 1e9 && Math.Abs(v.Y) < 1e9 && Math.Abs(v.Z) < 1e9; }
bool FleetOk(MyTuple<long, Vector3D, Vector3D, double, long> d) { return FleetV(d.Item2) && FleetV(d.Item3) && d.Item4 < 1e5 && !double.IsNaN(d.Item4); }
void FleetSend(MyTuple<long, Vector3D, Vector3D, double, long> tup) { if (LkAlive) IGC.SendBroadcastMessage(_lkK + ".FLT", tup, TransmissionDistance.CurrentConstruct); }
// <<< SPLIT LINK <<<
int LkAmmo() { return _rockets; }   // rockets for the Staff screen (negative = unknown)
string LkGate() { return _gateWhy; }

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
        KzLoad(); AiGateLoad();
    }
    catch (Exception ex) { CtorErr("modules", ex); }
    try
    {
        _ready = StartMode.Trim().ToUpperInvariant() == "READY"; _sentry = StartMode.Trim().ToUpperInvariant() == "SENTRY"; _mode = _ready || _sentry ? Mode.GUARD : ParseMode(StartMode, Mode.IDLE);
        if (!string.IsNullOrEmpty(Storage))
        {
            var p = Storage.Split(';');
            if (p.Length >= 5)
            {
                _mode = ParseMode(p[0], _mode); _ready = p.Length > 5 && p[5] == "1"; _sentry = p.Length > 6 && p[6] == "1";
                double x, y, z;
                if (double.TryParse(p[1], out x) && double.TryParse(p[2], out y) && double.TryParse(p[3], out z))
                { _home = new Vector3D(x, y, z); _homeSet = p[4] == "1"; }
            }
        }
    }
    catch (Exception ex) { CtorErr("storage", ex); }
    try
    {
        _lTgt = IGC.RegisterBroadcastListener(Tag + "_TGT");
        _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
        LkInit();
        _lSwarm = IGC.RegisterBroadcastListener(Tag + "_SWARM");
        RegisterFeeds();
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
    Storage = string.Join(";", _mode.ToString(), _home.X.ToString("R"), _home.Y.ToString("R"), _home.Z.ToString("R"), _homeSet ? "1" : "0", _ready ? "1" : "0", _sentry ? "1" : "0");
}

Mode ParseMode(string s, Mode def)
{
    switch (s.Trim().ToUpperInvariant())
    {
        case "IDLE": return Mode.IDLE; case "GUARD": return (int)def == -1 && (_ready || _sentry) ? (Mode)(-2) : Mode.GUARD; case "HUNT": return Mode.HUNT;
        case "SENTRY": return (int)def == -1 && _sentry && _mode == Mode.GUARD ? Mode.GUARD : def;   // (def == -1: the Staff link asks whether the verb changes nothing)
        case "ASSIST": return Mode.ASSIST; case "RTB": return Mode.RTB; case "FOLLOW": case "ESCORT": return Mode.FOLLOW; case "SATELLITE": case "SAT": case "ORBIT": return Mode.FOLLOW; default: return def;
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
        { Command(arg); if (_lkK != "") IGC.SendBroadcastMessage(_lkK + ".CMD", arg, TransmissionDistance.CurrentConstruct); }   // (the Staff PB handles launch / armed / disarm / follow / satellite / reload)
    if ((src & UpdateType.Update1) == 0) return;

    double dt = Runtime.TimeSinceLastRun.TotalSeconds;
    if (dt <= 0 || dt > 0.5) { if (dt > 0.5 && _tick > 3) { _aiOn = !_aiOn; _firing = false; _aiKick = false; } dt = 1.0 / 60.0; }   // run gap (PB was off / halted): the Staff watchdog may have switched the AI blocks off, so force the next pass to send the AI and gun state again (a zero dt at start-up or after a terminal run in the same tick is not a gap: flipping _aiOn then left the AI blocks off)
    _now += dt; _tick++;
    _avgMs = _avgMs * 0.98 + Runtime.LastRunTimeMs * 0.02;

    if (_tick % 600 == 0) GetBlocks();
    CheckGrid();
    if (_rc == null || !_rc.IsFunctional || !_rc.IsSameConstructAs(Me)) { _err = "No working Remote Control / cockpit"; if (!_rcOut || _tick % 60 == 0) { _rcOut = true; GetBlocks(); Display(); Release(); } return; }   // (not on this construct = a severed piece: same as no controller; look for another one at 1 Hz)
    _rcOut = false;

    ReadIGC(); LkRead(); ReadSwarm();
    if (_tick % 6 == 3) BroadcastStatus();
    if (!Hot()) StepSlimScan();
    if (_tick % 20 == 7) CheckDamage();
    if (_tick % 6 == 0) PollDesignators();
    if (_tick % 10 == 5 && !Hot()) PollSensors();
    if (_tick % 10 == 0) { PruneAndSelect(); PowerAndHealth(); }
    MaintainLock();
    if (_mode != Mode.IDLE && !Hot()) OmniScan();
    if (_tgt == null && !_aiOn && AutoScan && (_mode == Mode.GUARD || _mode == Mode.HUNT || _mode == Mode.FOLLOW) && !Hot()) AutoScanStep();
    if (ShareTargets && _tgt != null && _tgt.Src != "IGC" && _tick % 30 == 0 && _now - _tgt.Seen < 1.0 && !Hot()) ShareTarget();

    if (!LkBurn && !AiStep()) Control(dt);
    _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount);
    LkSend();
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
        case "follow": case "escort": case "satellite": case "sat": case "orbit": SetMode(Mode.FOLLOW); break;
        case "hunt": SetMode(Mode.HUNT); break;
        case "assist": SetMode(Mode.ASSIST); break;
        case "rtb": SetMode(Mode.RTB); break;
        case "idle": case "stop": SetMode(Mode.IDLE); break;
        case "lock": ManualLock(); break;
        case "unlock": _tgt = null; _contacts.Clear(); break;
        case "next": if (_tgt != null) IgnoreTarget(_tgt, 60, true); break;
        case "split": SplitFocus = true; break;
        case "aialert": _alertT = _now; break;
        case "ai": if (w.Length > 1) { AiAssist = w[1] == "on"; FindAi(); } break;
        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;
        case "breach": if (w.Length > 1) BreachAuto = w[1] == "on"; break;
        case "seeker": if (w.Length > 1) { SeekerOn = w[1] == "on"; SeekerSetup(); } break;
        case "focus": SplitFocus = false; break;
        case "ready": SetMode(Mode.GUARD); if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; _ready = true; } break;   // READY: hold this spot (the carrier link's HOME is ignored), scan all round, fight when something comes, come back
        case "sentry": SetMode(Mode.GUARD); if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; _sentry = true; } break;   // SENTRY: hold this spot, hunt down anything that comes within Sentry Range (no leash), come back
        case "home": if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } break;
        case "range":
            double r; if (w.Length > 1 && double.TryParse(w[1], out r)) Standoff = MathHelper.Clamp(r, MinRange, FireRange); break;
        case "reload": LoadConfig(); KzLoad(); AiGateLoad(); GetBlocks(); break;
    }
}

void SetMode(Mode m)
{
    _mode = m; _ready = false; _sentry = false; _noTgtSince = _now; _patrolLeg = false; _patrolStopSet = false;
    if (m == Mode.IDLE || m == Mode.ASSIST) Release();
}

// =====================================================================
//  CONFIG
void LoadConfig()
{
    MyIniParseResult res;
    if (!_ini.TryParse(Me.CustomData, out res)) { _err = "CustomData parse error line " + res.LineNo; return; }
    const string S = "Wasp";
    Tag = Cfg(S, "IGC Tag", Tag);
    StartMode = Cfg(S, "Start Mode (GUARD/HUNT/ASSIST/IDLE)", StartMode);
    ProjSpeed = Cfg(S, "Projectile Speed", ProjSpeed);
    RktV0 = Cfg(S, "Rocket Launch Speed", RktV0);
    RktAcc = Cfg(S, "Rocket Acceleration", RktAcc);
    RktVmax = Cfg(S, "Rocket Top Speed", RktVmax);
    RocketModel = Cfg(S, "Rocket Velocity Model (CAPPED/NONE/FULL)", RocketModel).Trim().ToUpperInvariant();
    LeadMargin = Cfg(S, "Aim Margin (predicted misses)", LeadMargin);
    MaxFireRange = Cfg(S, "Max Fire Range", MaxFireRange);
    MaxLeadTime = Cfg(S, "Max Flight Time", MaxLeadTime);
    SafeRange = Cfg(S, "Min Launch Distance", SafeRange);
    LineClear = Cfg(S, "Friendly Flight Line Clearance", LineClear);
    BlastClear = Cfg(S, "Friendly Blast Clearance", BlastClear);
    SalvoSize = (int)Cfg(S, "Salvo Size", (double)SalvoSize);
    SalvoPause = Cfg(S, "Salvo Pause", SalvoPause);
    RocketInterval = Cfg(S, "Seconds Between Shots Per Launcher", RocketInterval);
    RippleGap = Cfg(S, "Seconds Between Launchers", RippleGap);
    JinkGateK = Cfg(S, "Jink Gate Radius Factor", JinkGateK);
    JinkGateMin = Cfg(S, "Jink Gate Min Miss (m)", JinkGateMin);
    ReserveRockets = (int)Cfg(S, "Conserve Below Rockets", (double)ReserveRockets);
    ConserveRange = Cfg(S, "Conserve Max Range", ConserveRange);
    Standoff = Cfg(S, "Standoff Range", Standoff);
    StandoffU = Cfg(S, "Standoff Range (unlimited rockets)", StandoffU);
    MaxFireRangeU = Cfg(S, "Max Fire Range (unlimited rockets)", MaxFireRangeU);
    MaxLeadTimeU = Cfg(S, "Max Flight Time (unlimited rockets)", MaxLeadTimeU);
    MinRange = Cfg(S, "Min Range", MinRange);
    GuardRadius = Cfg(S, "Guard Radius", GuardRadius);
    SentryRange = Cfg(S, "Sentry Range (m)", SentryRange);
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
    LooseFar = Cfg(S, "Loose Fire Beyond Standoff", LooseFar);
    FireTol = Cfg(S, "Fire Window Multiplier", FireTol);
    MinFireWinDeg = Cfg(S, "Minimum Fire Window (deg)", MinFireWinDeg);
    MaxSideFireSpeed = Cfg(S, "Max Sideways Speed To Fire (0 off)", MaxSideFireSpeed);
    LeadScale = Cfg(S, "Lead Scale (1: as calculated)", LeadScale);
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
    AiBore8 = (int)Cfg(S, "AI Confirm Boresight Rays (of 8)", (double)AiBore8);
    AiConfirmAdaptive = Cfg(S, "AI Confirm Rays Shorten After A Fight", AiConfirmAdaptive);
    AiSpiralMin = Cfg(S, "AI Confirm Spiral Min (deg)", AiSpiralMin);
    AiSpiralMax = Cfg(S, "AI Confirm Spiral Max (deg)", AiSpiralMax);
    AiSpiralPow = Cfg(S, "AI Confirm Spiral Exponent", AiSpiralPow);
    AiStaleTakeover = Cfg(S, "AI Takes Over After Stale Lock (s)", AiStaleTakeover);
    GhostTime = Cfg(S, "Ghost Target Time (s)", GhostTime);
    Stickiness = Cfg(S, "Target Stickiness (score bonus for the current target)", Stickiness);
    AiRepick = Cfg(S, "AI Re-pick Interval (s; 0 off)", AiRepick);
    DashOn = Cfg(S, "Dash Along AI Heading", DashOn);
    DashAfter = Cfg(S, "Dash Starts After AI Stint (s)", DashAfter);
    DashLen = Cfg(S, "Dash Length (s)", DashLen);
    DashRegap = Cfg(S, "Dash Re-aim By AI (s)", DashRegap);
    DashSpeed = Cfg(S, "Dash Speed", DashSpeed);
    PatrolOn = Cfg(S, "Search Patrol When Nothing Found (HUNT)", PatrolOn);
    PatrolRandom = Cfg(S, "Search Patrol Own Start Corner And Direction", PatrolRandom);
    PatrolRadius = Cfg(S, "Search Patrol Radius (m)", PatrolRadius);
    PatrolAfter = Cfg(S, "Search Patrol Starts After (s)", PatrolAfter);
    PatrolAfterFight = Cfg(S, "Search Patrol Starts After A Fight (s)", PatrolAfterFight);
    PatrolListen = Cfg(S, "Search Patrol Listening Stop (s)", PatrolListen);
    PatrolSpeed = Cfg(S, "Search Patrol Speed", PatrolSpeed);
    AiRepickWindow = Cfg(S, "AI Re-pick Only Within (s) Of Last Target", AiRepickWindow);
    AiCone = Cfg(S, "AI Confirm Cone Yaw (deg)", AiCone);
    AiConePitch = Cfg(S, "AI Confirm Cone Pitch (deg)", AiConePitch);
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
    SeekerSetup();
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

double _fcT = -99; string _fc = "";   // last new contact: distance, speed, source (CAM / AI / IGC / ...) and when (from the Ballista Helm)
Track AddContact(MyDetectedEntityInfo e, string src)
{
    Track t;
    if (!_contacts.TryGetValue(e.EntityId, out t))
    {
        t = new Track { Id = e.EntityId };
        _contacts[e.EntityId] = t;
        if (_rc != null) { _fcT = _now; _fc = Vector3D.Distance(e.Position, _rc.GetPosition()).ToString("0") + " m v " + new Vector3D(e.Velocity).Length().ToString("0") + " " + src; }   // first sight of a grid, for the status screen
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
    ReadTgt(_lTgt);
    foreach (var fl in _lFeed) ReadTgt(fl);
    ReadTgt(_lkTgt);
    while (_lCmd.HasPendingMessage)
    {
        var msg = _lCmd.AcceptMessage();
        if (msg.Data is string) Command((string)msg.Data);
    }
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
double GhostTime = 8.0;       // how long a lost target is kept and searched for
double AiStaleTakeover = 3.0; // lock stale for this long: let the AI blocks hold the enemy (if there are any)
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
        if (probingAim && !e.IsEmpty() && e.EntityId == _tgt.Id) BreachHit(_tgt, e, aim);
        else if (probingAim && (e.IsEmpty() || !BreachMode(_tgt))) AimMissed(_tgt);   // breach: a drone in the way is no miss
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
    KzAssess();   // KAMIKAZE DEFENCE: classify incoming kamikazes before AssessTargets / scoring
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
        if (_sentry && _mode == Mode.GUARD) { if (t != _tgt && Vector3D.DistanceSquared(p, me) > SentryRange * SentryRange && !KzLive(t.Id)) continue; }   // SENTRY: wake on anything inside Sentry Range, then chase it anywhere
        else if (_mode == Mode.GUARD && Vector3D.Distance(p, home) > GuardRadius && !KzLive(t.Id)) continue;
        if (_mode == Mode.FOLLOW && Vector3D.DistanceSquared(p, home) > LkEngage * LkEngage) continue;   // FOLLOW: only threats near the owner
        Vector3D d = p - me; double dist = d.Length();
        double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(d / Math.Max(dist, 1), fwd), -1, 1)) * 57.2958;
        double closing = Vector3D.Dot(t.V(_now) - myVel, -d / Math.Max(dist, 1));
        double score = dist + ang * 6 - closing * 3;
        if (t.Type == MyDetectedEntityType.SmallGrid) score -= 150;   // small = more likely drones/fighters shooting at us
        if (t == _tgt) score -= Stickiness;                            // stickiness
        score += Math.Max(0, (_now - t.Seen) - 1.0) * 150;             // stale tracks lose to fresh sightings
        if (dist < 150) score -= 350;                                  // something right on top of us comes first
        score += HiveScore(t) + KzScore(t);
        if (score < bestScore) { bestScore = score; best = t; }
    }
    _tgt = best;
}

Vector3D Home()
{
    if ((_ready || _sentry) && _mode == Mode.GUARD) return _home;
    if (_mode == Mode.FOLLOW && _lkOwner) return _lkOwnerP;   // FOLLOW: the owner point from the Staff PB (the last one when the Staff is gone)
    if (_mode != Mode.RTB && _tgt == null && _now - _assistT < 4) return _assist;   // go help a hive member under fire
    if (_mode == Mode.HUNT && _tgt == null && _patrolStopSet) return _patrolStop;   // search patrol: hold the listening stop, not fly home
    return (_now - _dynHomeT < 5) ? _dynHome : _home;
}

// =====================================================================
//  POWER / HEALTH
double _batFrac = 1, _ammoFrac = 1; bool _hadAmmo = false;
int _rockets = -1;            // rockets left (from inventory volume, 60 L each); -1 = unknown (creative / empty inventories)
const double RocketVolM3 = 0.06;
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

    double vol = 0, vmax = 0; int working = 0; double volM3 = 0;
    foreach (var g in _guns)
    {
        if (!g.IsFunctional) continue;
        working++;
        var inv = g.GetInventory(0);
        if (inv == null) continue;
        vol += inv.CurrentVolume.RawValue; vmax += inv.MaxVolume.RawValue; volM3 += inv.CurrentVolume.RawValue / 1e6;
    }
    _ammoFrac = vmax > 0 ? vol / vmax : 0;
    if (vol > 0) _hadAmmo = true;
    _rockets = _hadAmmo ? (int)Math.Round(volM3 / RocketVolM3) : -1;

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
    if (LkBurn) return;   // take-off burn: the Staff PB flies (LAUNCH block)
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
        Vector3D gunPos = NextGunPos(me);   // each rocket leaves its OWN launcher (about 1.4 m off the ship axis): aim from the one that fires next, not from the average
        haveAim = Lead(_tgt, gunPos, myVel, out aimPoint, out aimDir);
        if (haveAim)
        {
            Vector3D losRate = Vector3D.Zero;
            if (_havePrev) losRate = Vector3D.Cross(_prevAim, aimDir) / dt;
            if (losRate.LengthSquared() > 4) losRate = Vector3D.Normalize(losRate) * 2;
            _prevAim = aimDir; _havePrev = true;
            Vector3D up = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : Vector3D.Zero;
            _tvD = (aimPoint - _rc.GetPosition()).Length();
            Aim(aimDir, up, losRate, dt);

            Vector3D gunFwd = _aimRef.WorldMatrix.Forward;
            double dist = Vector3D.Distance(aimPoint, gunPos);
            _aimErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(gunFwd, aimDir), -1, 1)) * 57.2958;
            double aimR = AimActive(_tgt) && !(LooseFar && dist > (_rockets >= 0 ? Standoff : StandoffU) + 100) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6;   // aiming at a hull point needs a tighter window
            aimR = Math.Min(aimR, Math.Max(_tgt.Raw * 0.8, 0.5));   // a rocket must HIT a block to detonate: a wreck of one or two blocks is only ~0.5 m wide
            _fireWinDeg = Math.Max(MinFireWinDeg, Math.Atan(aimR / Math.Max(dist, 1)) * 57.2958) * FireTol;
            _gunPos = gunPos; _gunFwd = gunFwd; _impact = aimPoint; _impactDist = dist;
            shoot = FireGate(dist, aimPoint, gunPos);
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
            if (_dash) fwd = _dashDir;
            else if (_mode == Mode.RTB && toHome.LengthSquared() > 400) fwd = Vector3D.Normalize(toHome);
            else if (myVel.LengthSquared() > 100 && _mode != Mode.GUARD && !((_patrolLeg || _now - _hitDirT < 10) && AutoScan)) fwd = Vector3D.Normalize(myVel);   // (a patrol leg keeps sweeping: the thrusters fly any direction)
            else
            {
                // radar sweep: yaw round and nod up/down so the camera cone covers all around
                if (AutoScan) fwd = SweepDir(up);
            }
            Aim(fwd, up, Vector3D.Zero, dt);
        }
    }
    FireRockets(shoot);

    // ---------- translation ----------
    if (_mode == Mode.ASSIST) return;
    double mass = _rc.CalculateShipMass().PhysicalMass;
    double aBrake = MinAccel(mass, grav) * 0.55; _aBrake = aBrake;
    Vector3D want;
    Vector3D home = Home();
    if (_mode != Mode.RTB && _now - _dynHomeT < 5 && home == _dynHome) { Vector3D o = me - home; home += Vector3D.Normalize(o.LengthSquared() > 1 ? o : _rc.WorldMatrix.Up) * 65; }   // the carrier link's HOME is the ship's centre: hold on a 65 m sphere round it, on my side, never inside the hull
    if (_mode == Mode.RTB || _tgt == null)
    {
        Vector3D d = home - me; double dist = d.Length();
        double v = Math.Min(Math.Min(CruiseSpeed, dist * 0.8), Math.Sqrt(2 * aBrake * dist));
        Vector3D sep = Separation(me);
        if (_mode != Mode.RTB && _tgt == null && sep.LengthSquared() > 1 && Home() == _home) _home += sep * dt;   // idle: the wing spreads out (the home point moves away from the mates, or it pulls the drone back to its paste spot; from the Ballista Helm)
        want = (dist > 0.5 ? d / dist * v : Vector3D.Zero) + Evade() + sep;
        if (_patrolLeg && _mode == Mode.HUNT && _tgt == null)   // search patrol: cruise towards the current waypoint
        {
            Vector3D pd = _patrolWp - me; double pl = pd.Length();
            want = (pl > 1 ? pd / pl * PatrolSpeed : Vector3D.Zero) + Evade() + Separation(me);
        }
        if (_dash && _mode == Mode.HUNT && _tgt == null) want = _dashDir * DashSpeed + Separation(me);   // dash along the AI's heading
        if (_mode == Mode.FOLLOW && LkWantOk) want = _lkWant + ((_lkFl & 2) != 0 ? Vector3D.Zero : Separation(me)) + Evade();   // own slot by the owner (computed by the Staff PB), matching its velocity
        if (_mode == Mode.RTB && dist < 15 && myVel.Length() < 2) { SetMode(Mode.IDLE); _err = "Home - idle"; return; }
    }
    else
    {
        Vector3D tp = _tgt.P(_now), tv = _tgt.V(_now);
        Vector3D d = tp - me; double dist = d.Length();
        Vector3D dir = dist > 1 ? d / dist : _rc.WorldMatrix.Forward;
        double e = dist - (_rockets >= 0 ? Standoff : StandoffU);
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
        double orbit = dist < FireRange * 1.2 ? OrbitSpeed : 0;
        want = tv + dir * vr + tan * orbit * _orbitSign + (Jink ? _jink : Vector3D.Zero) + Evade() + Separation(me);
        if (_mode == Mode.GUARD && !_sentry && Vector3D.Distance(me, home) > GuardRadius * 1.3) want = Vector3D.Normalize(home - me) * CruiseSpeed;
        if (_mode == Mode.FOLLOW && LkFar(me)) want = LkBack(me);   // leash: drop the fight, back to the owner
    }
    double cap = (_tgt != null && _mode != Mode.RTB) || _mode == Mode.FOLLOW ? MaxSpeed : (_dash ? DashSpeed + 5 : CruiseSpeed);
    want = KzWant(want, me, cap);   // KAMIKAZE DEFENCE: back away / break sideways
    want = FaWant(want, me);   // FRIENDLY AVOIDANCE: keep clear of non-enemy grids and rocks
    if (want.LengthSquared() > cap * cap) want = Vector3D.Normalize(want) * cap;

    Vector3D acc = (want - myVel) * 2.0;
    if (acc.LengthSquared() > 2500) acc = Vector3D.Normalize(acc) * 50;
    ApplyThrust((acc - grav) * mass);
}

// Time to fly d metres after launch (the rocket speed curve).
double RktTime(double d)
{
    double ta = Math.Max(0, (RktVmax - RktV0) / Math.Max(RktAcc, 1));
    double da = RktV0 * ta + 0.5 * RktAcc * ta * ta;
    if (d <= da) return (-RktV0 + Math.Sqrt(RktV0 * RktV0 + 2 * RktAcc * d)) / Math.Max(RktAcc, 1e-6);
    return ta + (d - da) / Math.Max(RktVmax, 1);
}

double FireRange { get { return _rockets >= 0 ? MaxFireRange : MaxFireRangeU; } }
// Rocket flight in the barrel frame for a launch from a ship moving at ua along the barrel and us across it (CAPPED model): distance
// along the barrel s, sideways drift q (towards us) and speed along the barrel sv after T seconds. Phase 1: launch velocity
// (ua + V0, us), +A along the barrel until the speed reaches Vmax; phase 2: speed Vmax, the angle th to the barrel decays as
// tan(th/2) = tan(th0/2) exp(-A/Vmax t). Closed form, within 1 m of a 60 Hz step-by-step flight.
void RktPath(double T, double ua, double us, out double s, out double q, out double sv)
{
    double V = RktVmax, A = Math.Max(RktAcc, 1e-3), va = ua + RktV0, t1, th;
    if (va * va + us * us >= V * V) { t1 = 0; th = Math.Atan2(us, va); }
    else { t1 = (Math.Sqrt(V * V - us * us) - va) / A; th = Math.Asin(MathHelper.Clamp(us / V, -1, 1)); }
    if (T <= t1) { s = va * T + 0.5 * A * T * T; q = us * T; sv = va + A * T; return; }
    s = va * t1 + 0.5 * A * t1 * t1; q = us * t1;
    double k = A / V, h = T - t1, c = Math.Tan(0.5 * th), x = c * Math.Exp(-k * h);
    q += V / k * 2 * (Math.Atan(c) - Math.Atan(x));
    s += V * (h - Math.Log((1 + c * c) / (1 + x * x)) / k);
    sv = V * (1 - x * x) / (1 + x * x);
}

// Rocket intercept (v2.4): find the barrel direction d and flight time T with  rocket(d, T) = target(T).  The target is predicted
// with the LEAD block (fresh acceleration held for the expected rest of its manoeuvre, aim point across the line of fire); the
// rocket follows RocketModel. Newton on T: f(T) = s(T) - |target(T) - sideways drift(T)|.
bool _leadOk = false;
Vector3D _aimLos; double _aimNeed;   // line of fire and required hull margin for AimOffset's choice
bool Lead(Track t, Vector3D shooter, Vector3D shooterVel, out Vector3D point, out Vector3D dir)
{
    Vector3D rp = t.P(_now) - shooter, tv = t.V(_now), ta = UseTargetAccel ? LeadAcc(t) : Vector3D.Zero;
    double rl = rp.Length();
    if (rl < 1 || double.IsNaN(rl)) { point = shooter + rp; dir = Vector3D.Zero; _leadOk = false; return false; }
    Vector3D los = rp / rl, d = los, p = rp;
    double tt = RktTime(rl), h = LeadHold(t), f = 99;
    _aimLos = los; _aimNeed = LeadMargin * LeadMiss(t, tt);
    Vector3D off = AimOffset(t);
    int model = RocketModel == "NONE" ? 0 : RocketModel == "FULL" ? 2 : 1;
    for (int i = 0; i < 12; i++)
    {
        p = rp + LeadOffset(t, off, los, tt) + (tv * tt + ta * LeadK(tt, h)) * LeadScale;
        Vector3D w = p, cv = tv + ta * LeadKd(tt, h);
        double s, q, sv;
        if (model == 1)
        {
            double ua = Vector3D.Dot(shooterVel, d); Vector3D up = shooterVel - ua * d; double us = up.Length();
            RktPath(tt, ua, us, out s, out q, out sv);
            if (us > 1e-3) w -= up * (q / us);
        }
        else
        {
            RktPath(tt, 0, 0, out s, out q, out sv);
            if (model == 2) { w -= shooterVel * tt; cv -= shooterVel; }
        }
        double wl = w.Length();
        if (wl < 1) break;
        d = w / wl; f = s - wl;
        tt = MathHelper.Clamp(tt - f / Math.Max(sv - Vector3D.Dot(d, cv), 20), 0.02, 12);
    }
    _leadOk = Math.Abs(f) < 2 && !double.IsNaN(tt) && !double.IsNaN(d.X);
    _leadT = tt;
    point = shooter + p;
    dir = d;
    return _leadOk;
}

// ---- fire control state
Vector3D _gunPos, _gunFwd, _impact; double _impactDist;
string _gateWhy = "";
double _salvoPauseUntil = 0, _lastShot = -99; int _salvoCount = 0;
// Position of the launcher FireRockets will use next (first in list among those ready soonest), or the fallback.
Vector3D NextGunPos(Vector3D fallback)
{
    Vector3D best = fallback; double bt = double.MaxValue;
    foreach (var g in _guns)
    {
        if (!g.IsFunctional) continue;
        double nx; if (!_nextShot.TryGetValue(g.EntityId, out nx)) nx = 0;
        nx = Math.Max(_now, Math.Max(nx, Math.Max(_lastShot + RippleGap, _salvoPauseUntil)));
        if (nx < bt - 1e-6) { bt = nx; best = g.GetPosition(); }
    }
    return best;
}
Dictionary<long, double> _nextShot = new Dictionary<long, double>();

// All the reasons NOT to launch. Rockets can't be recalled, so be strict.
bool FireGate(double dist, Vector3D impact, Vector3D gunPos)
{
    _gateWhy = "";
    if (!_leadOk) { _gateWhy = "no lead solution"; return false; }
    if (_now < _holdFireUntil) { _gateWhy = "holding fire"; return false; }
    if (_now - _tgt.Seen > FireStaleMax) { _gateWhy = "track stale"; return false; }
    if (dist > FireRange || _leadT > (_rockets >= 0 ? MaxLeadTime : MaxLeadTimeU)) { _gateWhy = "out of range"; return false; }
    if (dist < SafeRange) { _gateWhy = "too close (blast)"; return false; }
    if (_aimErrDeg >= _fireWinDeg) { _gateWhy = "not on target"; return false; }
    if (MaxSideFireSpeed > 0) { Vector3D v = _rc.GetShipVelocities().LinearVelocity; double sd = (v - Vector3D.Dot(v, _gunFwd) * _gunFwd).Length(); if (sd > MaxSideFireSpeed) { _gateWhy = "moving sideways " + sd.ToString("0") + " m/s"; return false; } }
    // manoeuvring we cannot predict (average acceleration vs average size), integrated over the flight
    double unc = 0.5 * Math.Max(0, _tgt.AccMag - _tgt.EA().Length()) * _leadT * _leadT;
    if (unc > Math.Max(_tgt.Radius * JinkGateK, JinkGateMin)) { _gateWhy = "target jinking: miss " + unc.ToString("0") + " m"; return false; }
    if (_rockets >= 0 && _rockets <= ReserveRockets && (dist > ConserveRange || _aimErrDeg > 0.5 * _fireWinDeg)) { _gateWhy = "saving last rockets"; return false; }
    if (FriendCheck && MatesInTheWay(gunPos, impact)) { _gateWhy = "hive mate near flight line"; return false; }
    return true;
}

// Hive mates (from IGC) near the rocket's path or near the point it will detonate. A mate is moved to where it will be when the
// rocket passes it (rocket time of flight to that point) and when the rocket arrives; the old version used its position "now",
// 1-3 s too early for a mate flying 30-40 m/s.
bool MatesInTheWay(Vector3D from, Vector3D to)
{
    Vector3D seg = to - from; double l2 = Math.Max(seg.LengthSquared(), 1), len = Math.Sqrt(l2);
    foreach (var m in _mates.Values)
    {
        double age = _now - m.Seen;
        if (age > 2) continue;
        Vector3D mp = m.Pos + m.Vel * age;
        double s = MathHelper.Clamp(Vector3D.Dot(mp - from, seg) / l2, 0, 1);
        for (int it = 0; it < 2; it++)
        {
            mp = m.Pos + m.Vel * (age + RktTime(s * len));        // where the mate is when the rocket is level with it
            s = MathHelper.Clamp(Vector3D.Dot(mp - from, seg) / l2, 0, 1);
        }
        if (Vector3D.Distance(mp, from + seg * s) < LineClear + 3) return true;
        if (Vector3D.Distance(m.Pos + m.Vel * (age + _leadT), to) < BlastClear + 3) return true;
    }
    return false;
}

// One launcher at a time, RippleGap apart, each at most once per RocketInterval; SalvoSize rockets, then a pause.
void FireRockets(bool gate)
{
    _showFire = _now - _lastShot < 0.4;
    if (!gate || _now < _salvoPauseUntil || _now < _lastShot + RippleGap) return;
    foreach (var g in _guns)
    {
        if (!g.IsFunctional) continue;
        double nx; if (_nextShot.TryGetValue(g.EntityId, out nx) && _now < nx) continue;
        if (FriendCheck) { CheckLineOfFire(_gunPos, _gunFwd, _impactDist); if (_now < _holdFireUntil) return; }
        g.ShootOnce();
        _nextShot[g.EntityId] = _now + RocketInterval;
        _lastShot = _now; _showFire = true;
        if (++_salvoCount >= SalvoSize) { _salvoCount = 0; _salvoPauseUntil = _now + SalvoPause; }
        return;
    }
}

// Wingmates repel each other: all of them fly the same orbit at the same speed and would otherwise end up in contact.
// Uses the positions the hive already broadcasts (_SWARM); costs a few vector operations per mate per tick.
bool LooseFar = true;   // Loose Fire Beyond Standoff: while still closing (range > standoff + 100 m) the fire window is the whole silhouette, not the 3 m hull point (from the Ballista Helm, docs/retrofit.md)
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

void CheckLineOfFire(Vector3D gunPos, Vector3D gunFwd, double dist)
{
    Vector3D probe = gunPos + gunFwd * Math.Min(dist, FireRange);
    var cam = CamFor(probe, dist + 5);
    if (cam == null) return;
    var e = cam.Raycast(probe);
    if (!e.IsEmpty() && e.EntityId != _tgt.Id && Friendly(e) && e.EntityId != Me.CubeGrid.EntityId)
    { _holdFireUntil = _now + 1.0; _err = "Holding fire: friendly in line"; }
}

void Aim(Vector3D desiredFwd, Vector3D desiredUp, Vector3D ffWorld, double dt)
{
    TvAim(ref desiredFwd, ref desiredUp, ref ffWorld);
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
    _tvN = forceWorld;
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

//  DISPLAY
void Display()
{
    string aim = "";
    if (_tgt != null) aim = "\nAim: " + AimLabel(_tgt) + " (" + _tgt.Hull.Count + " hull pts)" + AimInfo();
    string s = "WASP FLIGHT " + VER + "  " + (_sentry && _mode == Mode.GUARD ? "SENTRY" : _mode.ToString()) + "  tgt " + (_tgt != null ? _tgt.Src : "-") + "  staff " + (LkAlive ? "ok" : "none") + aim + (_fc != "" ? "\n1st contact " + _fc + " " + (_now - _fcT).ToString("0") + " s ago" : "") + (_err != ""? "\n! " + _err : "") + (_crash != "" ? "\n! Script error: " + _crash : "");
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
        SetB(s, true, "Detect Owner", "DetectOwner");   // FOLLOW on foot (owner entities are never targets: Valid)
        SetB(s, false, "Detect Friendly", "DetectFriendly");
        SetB(s, TargetSmall, "Detect Small Ships", "DetectSmallShips");
        SetB(s, TargetLarge, "Detect Large Ships", "DetectLargeShips");
        SetB(s, TargetLarge, "Detect Stations", "DetectStations");
        SetB(s, true, "Detect Players", "DetectPlayers");
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
        foreach (var e in _sensed) { if (Valid(e)) AddContact(e, "SENSOR"); }
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
    if (AiAssist) AiGateFind(_ai);   // (AI Assist off: the AI blocks stay as they came, "AI Behavior" off)
}
bool AiStep()
{
    if (_tgt != null) { _lastTgtT = _now; _lastTgtDist = Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()); }
    // The AI also takes over while my lock is stale (nothing seen for AiStaleTakeover s): it keeps the nose on the enemy
    // and the confirm rays below pick it up again, which hands control straight back to the script.
    bool want = AiAssist && _ai.Count > 0 && AiModeOk() &&
        ((_tgt == null && _now - _lastTgtT > 2) || (_tgt != null && _now - _tgt.Seen > AiStaleTakeover));
    if (_tgt != null) { _noTgtSince = _now; _patrolStopSet = false; if (_patrolLeg) { _patrolLeg = false; _patrolPhaseT = _now; } }   // (a target ends the patrol leg at once, not only when the target is gone again)
    if (_aiOn && PatrolOn)
    {   // is the AI flying / turning the ship? (the patrol leaves it alone while it is; its own speed jitters, so movement in the last 4 s counts)
        var vs = _rc.GetShipVelocities();
        Vector3D refV = _patrolK == 0 ? Vector3D.Zero : _listenV;   // before the first leg the reference is rest
        if ((vs.LinearVelocity - refV).LengthSquared() > 9 || vs.AngularVelocity.LengthSquared() > 0.0144) _aiMoveT = _now;
    }
    if (_aiOn) _noseAvg = _noseAvg * 0.97 + _rc.WorldMatrix.Forward * 0.03;   // where the AI keeps the nose (smoothed over ~0.5 s: the AI wobbles)
    if (PatrolStep()) want = false;            // (runs without AI blocks too: the listening stops are then the script's camera sweep)     // on a patrol leg the script flies and the AI blocks stay off until the next listening stop
    else if (want && DashCheck()) want = false; // on a dash the script flies along the AI's heading with the AI blocks off
    else if (!want) _dash = false;
    want = AiGate(want);   // AI GATE: the AI blocks may do nothing (AI Behavior off): time-share with the script's own search
    if (want != _aiOn)
    {
        _aiOn = want;
        _firing = false;                        // guns' state is unknown after any hand-over: SetFire sends it again
        if (want) { Release(); _aiSince = _now; }   // thrusters and gyros go to the AI
        _aiKick = false;
        foreach (var b in _ai) b.Enabled = want;
        if (!want) foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction("Shoot_Off");   // the AI may leave a launcher in continuous fire; the script launches single rockets itself
    }
    if (!_aiOn) { if (_dash) AiRays(); return false; }
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
    AiRays();
    return true;
}
void AiRays()
{
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
        // The AI block aims the launchers at the ROCKET lead point (target velocity x flight time) and wobbles a little, so the
        // enemy is typically 0-3 deg yaw / 0-1.5 deg pitch off the nose (measured in the sim: median 1.5 / 0.8 deg), densest near
        // the axis. AiBore8 of every 8 rays go straight down the boresight (sim: more boresight rays = more locks, 43 -> 52 % with 8 of 8); the rest are Gaussian-distributed (low-discrepancy sequence).
        double yaw = 0, pit = 0;
        if ((_aiScan % 8) >= AiBore8)
        {
            double u1 = (_aiScan * 0.6180339887) % 1.0, u2 = (_aiScan * 0.7548776662) % 1.0;
            double rr = Math.Sqrt(-2 * Math.Log(u1 + 0.002)), an = 6.2831853 * u2;
            yaw = MathHelper.Clamp(AiCone * 0.45 * rr * Math.Cos(an), -AiCone, AiCone);
            pit = MathHelper.Clamp(AiConePitch * 0.45 * rr * Math.Sin(an), -AiConePitch, AiConePitch);
        }
        var e = c.Raycast(len, (float)pit, (float)yaw);
        if (Valid(e)) AddContact(e, "AI");
        else if (_dash && !e.IsEmpty()) { _dash = false; _dashEnd = _now; _dashBlockT = _now + 10; }   // something that is not a live enemy (asteroid, wreck, friend) is ahead: stop dashing
        else if (!e.IsEmpty() && e.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies && (e.Type == MyDetectedEntityType.SmallGrid || e.Type == MyDetectedEntityType.LargeGrid)
                 && Ignored(e.EntityId) && e.BoundingBox.Size.Length() * 0.5 >= 1.6) AiHeldIgnored(e.EntityId);
    }
}
// ---- DASH ----------------------------------------------------------------------------------------------------------
//  The AI blocks hold the nose on the enemy they found but fly the ship slowly (a speed-limited "stay at range" approach), and a confirm ray
//  only hits a 5 m ship at a few hundred metres. So once the AI has been manoeuvring for DashAfter seconds without a lock, the script flies
//  DashLen seconds at DashSpeed along the smoothed nose heading with the AI blocks off (rays keep casting down the heading), then gives the
//  ship back to the AI for DashRegap seconds to re-aim. Ends the moment a contact exists. Needs AI blocks, HUNT mode and open space.
bool DashOn = false; double DashAfter = 4, DashLen = 8, DashRegap = 2.5, DashSpeed = 90;
bool _dash = false; double _dashT0 = -99, _dashEnd = -99, _dashBlockT = -99; Vector3D _dashDir, _noseAvg; int _dashN = 0;
bool DashCheck()
{
    if (!DashOn || _mode != Mode.HUNT || _rc == null || _tgt != null || _rc.GetNaturalGravity().LengthSquared() > 0.25) { _dash = false; return false; }
    if (_dash)
    {
        if (_now - _dashT0 <= DashLen) return true;
        _dash = false; _dashEnd = _now; return false;
    }
    bool first = _dashN == 0 || _now - _dashEnd > 25;
    if (_aiOn && _now > _dashBlockT && _now - _aiSince > (first ? DashAfter : DashRegap) && _now - _aiMoveT < 2 && _noseAvg.LengthSquared() > 0.25)
    {
        _dash = true; _dashT0 = _now; _dashN++; _dashDir = Vector3D.Normalize(_noseAvg);
        return true;
    }
    return false;
}
// ---- SEARCH PATROL ---------------------------------------------------------------------------------------------------
//  HUNT means "go and find them". The AI blocks only see out to their own search range, so a drone that has had no target for
//  PatrolAfter seconds stops waiting at home: it flies chords of a square of "radius" PatrolRadius around the home point (cruise speed,
//  AI blocks off), and stops every leg for PatrolListen seconds with the AI blocks on, which is when they look around. As soon as there is
//  a target, or the drone takes damage, the patrol ends. Needs AI blocks, HUNT mode and open space (no planet gravity).
bool PatrolOn = true, PatrolRandom = true;
double PatrolRadius = 1200, PatrolAfter = 15, PatrolAfterFight = 90, PatrolListen = 6, PatrolMaxLeg = 40, PatrolSpeed = 60;
bool _patrolLeg = false, _patrolFrame = false, _patrolStopSet = false; double _patrolPhaseT = -99, _noTgtSince = 0, _listenT = -1, _aiMoveT = -99, PatrolHold = 90; int _patrolK = 0; Vector3D _listenV;
Vector3D _patrolWp, _patrolC, _patrolE1, _patrolE2, _patrolStop; double _patrolRadF = 1; int _patrolDir = 1;
bool PatrolStep()
{
    if (!PatrolOn || _mode != Mode.HUNT || _rc == null || UnderFire || _now - _noTgtSince < (_lastTgtT < 0 ? PatrolAfter : PatrolAfterFight) || _rc.GetNaturalGravity().LengthSquared() > 0.25)
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
            // fan out: hive mates start at different corners (slot = rank by grid id), plus a per-drone twist. Drones pasted from one
            // blueprint face the same way, so without this they all flew the same first leg (in-game report 2026-10-13).
            int rank = 0; foreach (var m in _mates.Values) if (m.Id < Me.CubeGrid.EntityId) rank++;
            double a0 = 6.2831853 * rank / (_mates.Count + 1) + (Math.Abs(Me.CubeGrid.EntityId) % 628) * 0.001;
            Vector3D e1 = _patrolE1 * Math.Cos(a0) + _patrolE2 * Math.Sin(a0);
            _patrolE2 = _patrolE2 * Math.Cos(a0) - _patrolE1 * Math.Sin(a0); _patrolE1 = e1;
            // every drone gets its own direction and radius (from its grid id), so identical scripts do not fly the same square in lock-step
            // and miss each other (sim: mirror-image starts kept two patrolling ships 2.5-2.9 km apart for the whole 300 s). The first corner stays "forward".
            int ph = PatrolRandom ? (int)(Math.Abs(Me.CubeGrid.EntityId) % 997) : 0;
            _patrolDir = ((ph / 8) & 1) == 0 ? 1 : -1; _patrolRadF = PatrolRandom ? 0.8 + 0.1 * ((ph / 16) % 5) : 1;
        }
        double th = _patrolK++ * 1.5707963 * _patrolDir;
        _patrolWp = _patrolC + (_patrolE1 * Math.Cos(th) + _patrolE2 * Math.Sin(th)) * (PatrolRadius * _patrolRadF);
        _patrolLeg = true; _patrolPhaseT = _now; _listenT = -1;
    }
    else if (_now - _patrolPhaseT > PatrolMaxLeg || Vector3D.Distance(_rc.GetPosition(), _patrolWp) < 150) { _patrolLeg = false; _patrolPhaseT = _now; _patrolStop = _rc.GetPosition(); _patrolStopSet = true; }
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
