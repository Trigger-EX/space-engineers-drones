// =====================================================================
//  SHRIKE AI  v0.1-shrike  - the Helm script of the Shrike fighter: autonomous AND hand-flyable (ONE Programmable Block)
//  For Space Engineers (vanilla, small grid). Paste into a Programmable Block named "[Helm] ...".
//  A Hornet (HornetDroneAI v2.6) without LAUNCH, FOLLOW and SEEKER (drone-side blocks), without the close-range camera scan, the search patrol and the sensor bubble (size
//  cuts of M3), plus the MANUAL, BATTERY v1.0 and dead-man blocks of docs/design/control_arch.md (sections 2, 3). Everything that is not listed behaves like the Hornet:
//  tracking, camera / AI-block search, LEAD solver, BREACH aiming, KAMIKAZE DEFENCE, FLEET listener, GUARD / HUNT / ASSIST / RTB / IDLE.
//
//  WHAT THE SHIP MUST HAVE (naming rules the hull designers keep)
//   * this PB, custom name starting with "[Helm]"; a Remote Control (the script's reference for mass, velocity, orientation) AND a flight seat
//     (any IMyShipController that CanControlShip and is not the Remote Control; a ship with only a cockpit uses it as the reference too)
//   * fixed guns, tagged "[B1]" .. "[B4]" in their names for up to four batteries (untagged: ballistic B1, rocket B2, railgun B3, shell cannon B4); turrets are designators and
//     fire on their own (a ship with turrets runs the turret-only mode: the fixed batteries refuse a track none of them can hit)
//   * optional: a Timer whose name contains "Deadman" (5 s, Silent; its toolbar cuts thrusters and weapons), cameras, sensor, AI blocks tagged [AI]
//
//  MODES  AUTO = the Hornet modes (_mode: react guard hunt assist rtb idle; Start Mode default REACT, saved in Storage), MANUAL = somebody sits in the seat
//   (held >= Seat Debounce 0.4 s) or "manual" was sent. The tick the seat is taken every gyro / thruster override is released, the dampener switch goes back to the
//   pilot's own state, the AI blocks go off; tracking, sensors, turrets and the lock keep running. Seat empty >= Release Delay 1.5 s: back to _mode, the turn-rate cap
//   ramps from the present rate to Max Turn over 2 s. No script output while MANUAL touches the guns (Auto Fire In Manual = false).
//  COMMANDS (PB argument, IGC "<Tag>_CMD", toolbar)   all Hornet verbs except follow / satellite / seeker / launch / armed / disarm, and
//   manual | auto | pilot (toggle)   MANUAL / AUTO regardless of the seat until the seat changes ("auto" with a pilot aboard = the ship flies itself with a HUD)
//   assist hud | fbw | off           HUD: lead pip + fire window on the cockpit surface; FBW: fly-by-wire blend (stick rate + PD rate to the lead point, lock needed)
//   assist (no word)                 the Hornet ASSIST mode (script aims and fires, you fly)
//   Run with NO argument             menu: each press steps "Run Menu", the highlighted item runs "Run Menu Wait" s after the last press (HUD shows it)
//  CUSTOM DATA  [Shrike] = the Hornet keys (IGC Tag default SHRIKE, Start Mode default REACT, also accepts GUARD/HUNT/ASSIST/IDLE/RTB); [Manual] Seat Debounce, Release Delay, Assist (OFF/HUD/FBW),
//   Assist Max Rate, Pilot Rate Gain, Pilot Rot Sign, Rotation Input Scale, Pilot Priority Rate, Auto Fire In Manual, Scan In Manual, Run Menu, Run Menu Wait,
//   Deadman Timer Name, Seat Exclude (name part, default "gunnery": a seat with that word in its name does not switch the Helm to MANUAL), HUD Surface (cockpit text surface, -1 off); [Battery] Primary (AUTO/B1..B4), Standoff By Weapons, Turret Only (AUTO/ON/OFF), Auto Max Turn
//   (AUTO/ON/OFF); [Fleet] [Kamikaze Defence] [AI Gate] as in the Hornet.
//  LARGE-GRID SHIPS (Corsair, Warden, Leviathan: Me.CubeGrid is a large grid, "Big")   slow to turn, so: (1) the idle search turns steadily at half Max Turn and nods slowly (the Shrike's fast
//   nod tipped a dreadnought back and forth); (2) every working camera scans, out to 1.8 km, also in REACT (no sweep there, the cameras look all round); (3) CAPITAL SHIPS (target radius >= 50 m,
//   whose turrets reach 800 m by themselves): standoff = max(Standoff Range, Capital Standoff Range = 1000; 0 switches this and (4) off), and (4) one in the rear half inside that range makes the
//   ship point its nose AWAY and burn its strongest thrusters until the range is open (+100 m), the target is no longer behind, or 20 s have passed, then turn round and fight (40 s pause before it can flee again);
//   (5) braking distance uses the thrust available in the direction of travel, no random evasion, no jinking while fleeing.
//  STAFF LINK (HELMLINK block, optional)   with a second PB "[Staff] ..." on the construct (StaffAI.cs) the Helm sends a heartbeat (2 Hz), its mode / target / battery
//   (5 Hz) and seat changes over IGC (CurrentConstruct) and obeys the verbs the Staff forwards; the Staff releases every override when the heartbeat stops for 1.5 s.
//   [Link] Enable (AUTO/ON/OFF) (AUTO = on while a [Staff] PB exists, so the single-PB ship is untouched), Ship Tag (SHIP, same in the Staff), Heartbeat (s).
//   To fit 80,000 characters the rarely used keys Aim Point, Breach Large Ships (the `aim` / `breach` commands remain), Listen To IGC Tags, Friendly Fire Check, Guard Radius, Cruise Speed, Max Fire Range, Projectile Speed, Jink Interval (+ Random), Seconds Before Dropping Disabled Target, Ghost Target Time, Wingmate Separation Range / Speed,
//   Lead Scale, AI Confirm Raycast Range / Long Raycast Range / Rays Shorten, Target Stickiness, AI Re-pick Interval / Only Within, AI Takes Over After Stale Lock,
//   Search Patrol (all), Breach Min Target Radius, Breach Stall, Evade Speed When Hit, Close Scan Range, Scan Cone Degrees, All-around Close Scan, and (M3) Split Targets,
//   Projectiles Inherit Ship Velocity, Use Target Acceleration, Aim Margin, Max Track Age To Fire, Target Characters, Target Neutral/Unowned, Manual Lock Range, Jink Speed,
//   Scan Range were removed from Custom Data; their variables keep the Hornet defaults.
//  NOT MEASURED IN THE GAME: stick scale / sign / gyro feel (Pilot Rate Gain, Pilot Rot Sign, Rotation Input Scale: control_arch tests K1/K2), the Run-with-argument
//   toolbar item (K3, hence the menu), Timer re-arm by Stop + Start (does Start restart a counting timer?), DampenersOverride per controller (K7).
// =====================================================================

const string VER = "0.1-shrike";

// ---------------- settings (overwritten from Custom Data) -------------
string Tag = "SHRIKE";
double ProjSpeed = 800;      // m/s  gatling = 800, autocannon = 800 (game data, docs/stats.md; 400 over-led every shot twice). CustomData key "Projectile Speed"
double MaxFireRange = 700;   // gatling rounds vanish 640-800 m
double Standoff = 350;       // preferred engage distance
double MinRange = 80;        // never closer than this
double GuardRadius = 1500;   // GUARD only engages within this of home
double MaxSpeed = 98;        // combat speed cap
double CruiseSpeed = 60;     // non-combat cap (saves power)
double OrbitSpeed = 22;      // tangential speed around the target
double JinkEvery = 1.0, JinkEveryRand = 1.0;   // a new random evasive direction every JinkEvery + rand x JinkEveryRand seconds
double JinkSpeed = 12;       // random evasive velocity
double LockRange = 2500;     // manual "lock" raycast distance
double ScanRange = 1200;     // auto-scan raycast distance
double ScanCone = 35;        // auto-scan half-angle (deg)
double AimKp = 6, AimKd = 0.35, MaxTurn = 5; // gyro tuning (rad/s)
double CapRange = 1000; const double FleeMax = 20; bool _flee; double _fleeT = -99;   // CAPITAL FLEE (see Control)
double FireTol = 1.0;        // multiplier on silhouette fire window
double LeadScale = 1.0;      // scales the lead offset if shots land consistently ahead/behind
bool UseTargetAccel = true;
bool SplitFocus = true;
double DisabledGrace = 5, EvadeSpeed = 20;
int AiSearchEvery = 5; bool AiAssist = true; double AiConfirmRange = 2600;   // confirm rays are this long (0.77 casts/s per camera at 2 km/s charge; both forward cameras reach the full 2.6 km)
double AiConfirmLong = 2600;   // the first forward camera casts this long instead (0.77 casts/s)
double RtbBattery = 0.15;    // fraction
double SepRange = 70, SepSpeed = 25;   // wingmate separation: push away from hive mates closer than this (m) at up to this speed (m/s)
bool AutoScan = true, ShareTargets = true, Jink = true, FriendCheck = true, InheritVel = true;
string StartMode = "REACT";   // a Shrike is combat-ready but passive (REACT) until told otherwise (and again whenever the pilot leaves the seat)

// ---------------- state ----------------------------------------------
enum Mode { IDLE, GUARD, HUNT, ASSIST, RTB, REACT }
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
IMyBroadcastListener _lTgt, _lCrit, _lCmd, _lHome;

// tracking
class Track
{
    public long Id; public Vector3D Pos, Vel, Acc; public double AccMag;
    public Vector3D LAcc, LSeg, LW, LOR; public int LSegN; public double LSegT0 = -99, LManT = 2;   // LEAD v1.0 state (fresh acceleration, manoeuvres, spin)
    public double Spin, SpinSince = -1, Rad0, Raw, RawMax, DeadAt = -1, DisSince = -1; public Vector3D OF, OU; public bool HasOri;
    public MatrixD O = MatrixD.Identity; public List<Vector3D> Hull = new List<Vector3D>(); public int HullNext;
    public Vector3D Thrust, AimLocal; public double AimT = -99; public double Hl = -99;   // BATTERY: last time no battery could hit it
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
bool _firing = false;
double _holdFireUntil = 0;
Vector3D _jink; double _nextJink = 0; double _orbitSign = 1;
int _scanIdx = 0;
Random _rng = new Random();
string _err = "";
double _aimErrDeg = 0, _fireWinDeg = 0, _leadT = 0;
double _avgMs = 0; int _peakIns = 0;   // instruction counter: PB limit is Runtime.MaxInstructionCount per run
StringBuilder _sb = new StringBuilder();

// (the fleet channel and the other fleets' feeds live in the Staff PB since the Helm split: docs/design/helm_split.md; the Helm hears them as "<Ship Tag>.TGT" and ".S2H")

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

void CtorErr(string w, Exception ex) { _crash = "setup " + w + ": " + ex.Message; Echo(_crash); }   // a failing setup step must not kill the script for good: the rest still runs and the error is on the screen

// >>> MANUAL v1.0 (shared block of the hand-flyable warships: seat detection, MANUAL mode, hand-over, assist levels, dead-man heartbeat, Run menu; docs/design/control_arch.md s2, tools/blocks_sync.py) >>>
// MANUAL is a mode of the Helm script, not another script. While somebody sits in a flight seat (an IMyShipController that CanControlShip and is not the Remote Control)
// the script gives the ship to him: every gyro and thruster override is released the tick the seat is taken, the dampener switch goes back to his state, the AI blocks
// go off, target tracking, sensors and turrets keep running, and ManFly() replaces Control(). The seat must stay taken for "Seat Debounce" before MANUAL is committed
// (until then the ship is only held free), and stay empty for "Release Delay" before the script flies again from its saved auto mode (_mode, in Storage).
// Verbs: manual | auto | pilot (toggle) | assist hud|fbw|off | fire auto|pilot. "manual" / "auto" override the seat until the seat changes. Run without an argument
// (cockpit toolbar fallback when Run-with-argument does not save): each press steps through "Run Menu", the highlighted item runs 2 s after the last press.
// Assist: HUD = lead pip and fire window text on the cockpit surface; FBW = fly-by-wire: the pilot's stick rate (Rotation Input Scale, Pilot Rate Gain, Pilot Rot Sign
// are ASSUMED until the in-game tests K1/K2) plus the PD rate to the lead point, only while a lock exists, faded out by the stick (Pilot Priority Rate).
// Dead-man: a Timer named like "Deadman" is re-armed (Stop + Start) once a second while the script flies; in MANUAL it is stopped.
// Script hooks: ManCfg() at the end of LoadConfig; ManBlocks() at the end of GetBlocks; ManSeat() early in Tick, then "if (Man) ManFly(dt); else if (!AiStep()) Control(dt);";
// ManTick() each Update1 tick and ManMenu() for a Run without argument; "if (ManCmd(w)) return;" at the top of Command; ManMode()/ManHud() in Display; ManSave() / ManLoad(..)
// for Storage; ManCap() as the turn-rate cap of Aim.
// Uses from the script: _rc, _gyros, _thr, _tgt, _aimRef, _now, _tick, _mode, _sb, _ai, _aiOn, _aiKick, _havePrev, _prevErr, _firing, Lead(), AimW(), GyroRate(), SetFire(), Release*(), Command(), PrimGuns().
List<IMyShipController> _seats = new List<IMyShipController>(); IMyShipController _seat;
List<IMyTimerBlock> _dm = new List<IMyTimerBlock>(); IMyTextSurface _hud;
bool _man, _hold, _occ, _rawOcc, _asOv, _userDamp = true, _dampInit, _menuOn; int _force, _asLvl = 1, _menuI; // _force: 0 follow the seat, 1 manual, 2 auto
double _rawT, _rampT = -99, _w0, _asG = 1, _menuT, _pipR, _pipU;
double ManDebounce = 0.4, ManRelease = 1.5, ManAssistMax = 1.5, ManGain = 1.5, ManSign = -1, ManScale = 1, ManPrio = 0.3, ManMenuWait = 2;
bool ManAutoFire, ManScan; string ManMenuItems = "manual,auto,assist hud,assist fbw,assist off,lock,next,unlock,hunt,guard,rtb,idle", ManDmName = "Deadman", ManSeatEx = "gunnery"; int ManHudSurf = 0;
bool Man { get { return _man || _hold; } }

void ManCfg()
{
    const string M = "Manual";
    ManDebounce = Cfg(M, "Seat Debounce", ManDebounce); ManRelease = Cfg(M, "Release Delay", ManRelease);
    string a = Cfg(M, "Assist (OFF/HUD/FBW)", "HUD").Trim().ToUpperInvariant();
    _asLvl = a == "FBW" ? 2 : a == "OFF" ? 0 : 1;
    ManAssistMax = Cfg(M, "Assist Max Rate", ManAssistMax); ManGain = Cfg(M, "Pilot Rate Gain", ManGain); ManSign = Cfg(M, "Pilot Rot Sign", ManSign);
    ManScale = Math.Max(1e-3, Cfg(M, "Rotation Input Scale", ManScale)); ManPrio = Math.Max(1e-3, Cfg(M, "Pilot Priority Rate", ManPrio));
    ManAutoFire = Cfg(M, "Auto Fire In Manual", ManAutoFire); ManScan = Cfg(M, "Scan In Manual", ManScan);
    ManMenuItems = Cfg(M, "Run Menu", ManMenuItems); ManMenuWait = Cfg(M, "Run Menu Wait", ManMenuWait);
    ManDmName = Cfg(M, "Deadman Timer Name", ManDmName); ManSeatEx = Cfg(M, "Seat Exclude (name part)", ManSeatEx).ToLowerInvariant(); ManHudSurf = (int)Cfg(M, "HUD Surface", (double)ManHudSurf);
}

void ManBlocks()
{
    _seats.Clear(); _dm.Clear(); _hud = null;
    GridTerminalSystem.GetBlocksOfType(_seats, b => Own(b) && b.CanControlShip && !(b is IMyRemoteControl) && (ManSeatEx == "" || !b.CustomName.ToLowerInvariant().Contains(ManSeatEx)));   // (a gunnery seat does not take the ship from the Helm)
    string n = ManDmName.ToLowerInvariant();
    if (n != "") GridTerminalSystem.GetBlocksOfType(_dm, b => Own(b) && b.CustomName.ToLowerInvariant().Contains(n));
    try
    {
        var sp = _seats.Count > 0 ? _seats[0] as IMyTextSurfaceProvider : null;
        if (sp != null && ManHudSurf >= 0 && ManHudSurf < sp.SurfaceCount) { _hud = sp.GetSurface(ManHudSurf); _hud.ContentType = ContentType.TEXT_AND_IMAGE; _hud.FontSize = 0.8f; _hud.Font = "Monospace"; }
    }
    catch (Exception) { _hud = null; }
    if (!_dampInit && _rc != null) { _userDamp = _rc.DampenersOverride; _dampInit = true; }   // the pilot's own dampener switch, until MANUAL tracks it
}

bool Seated() { _seat = null; foreach (var c in _seats) if (c.IsFunctional && c.IsUnderControl) { _seat = c; break; } return _seat != null; }

// seat -> mode machine; runs first in Tick so the overrides are gone in the same tick the seat is taken
void ManSeat()
{
    bool raw = Seated();
    if (raw != _rawOcc) { _rawOcc = raw; _rawT = _now; }
    if (raw != _occ && _now - _rawT >= (raw ? ManDebounce : ManRelease)) { _occ = raw; _force = 0; }
    bool want = _force == 1 || (_force == 0 && _occ), hold = raw && !_occ && _force == 0 && !want;
    if ((want && !_man && !_hold) || (hold && !_hold)) { ManFree(); ManDm(false); }
    else if (want && _hold) ManDamp(_userDamp);   // the seat is committed: an AI block that was switched off may have touched the dampeners since
    if (_man && !want) ManExit();
    bool ch = _man != want || _hold != hold;
    _man = want; _hold = hold;
    if (ch) Display();   // the mode line changes at once
}

// hand the ship to the pilot: nothing of mine stays switched on. Guns the pilot set are left alone (only my own Shoot_On is cancelled).
void ManFree()
{
    ReleaseGyros();
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    _prevErr = Vector3D.Zero; _havePrev = false; _asOv = false; _asG = 1;
    BatFire(false, Vector3D.Zero);   // a continuous second battery stops too
    if (_aiOn) { _aiOn = false; _aiKick = false; foreach (var b in _ai) b.Enabled = false; }
    ManDamp(_userDamp);   // after the AI blocks: switching those off may touch the dampeners
    if (_firing) { _firing = false; foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction("Shoot_Off"); }
}

// back to the saved auto mode: the nose is not snapped, the turn-rate cap ramps from the present rate to MaxTurn over 2 s
void ManExit()
{
    ReleaseGyros(); _asOv = false; _firing = true;
    _w0 = _rc != null ? _rc.GetShipVelocities().AngularVelocity.Length() : 0; _rampT = _now; ManDm(true);
}
double ManCap() { double k = (_now - _rampT) / 2; return k >= 1 ? MaxTurn : Math.Min(MaxTurn, _w0 + (MaxTurn - _w0) * Math.Max(0, k)); }

void ManDamp(bool v) { _rc.DampenersOverride = v; foreach (var s in _seats) if (s.IsUnderControl) s.DampenersOverride = v; }
void ManDm(bool arm) { foreach (var t in _dm) { try { t.StopCountdown(); if (arm) t.StartCountdown(); } catch (Exception) { } } }

void ManTick()
{
    if (_menuOn && _now - _menuT > ManMenuWait) { _menuOn = false; var it = ManMenuItems.Split(','); if (it.Length > 0) Command(it[_menuI % it.Length]); }
    if (_tick % 60 == 11) ManDm(!Man);   // dead-man heartbeat: re-armed once a second while the script flies, stopped while a pilot does
}
void ManMenu() { _menuI = _menuOn ? _menuI + 1 : 0; _menuOn = true; _menuT = _now; }

bool ManCmd(string[] w)
{
    switch (w[0])
    {
        case "manual": _force = 1; return true;
        case "auto": _force = Seated() ? 2 : 0; return true;
        case "pilot": _force = Man ? (Seated() ? 2 : 0) : 1; return true;
        case "assist": if (w.Length < 2) return false; _asLvl = w[1] == "fbw" ? 2 : w[1] == "hud" ? 1 : 0; return true;
    }
    return false;
}

string ManSave() { return _asLvl + ";" + (_userDamp ? "1" : "0"); }
void ManLoad(string asl, string dmp) { int a; if (int.TryParse(asl, out a)) _asLvl = Math.Max(0, Math.Min(2, a)); if (dmp == "0" || dmp == "1") { _dampInit = true; _userDamp = dmp == "1"; } }

// MANUAL flight step (replaces Control): lead data for the HUD, optional auto fire, optional fly-by-wire
void ManFly(double dt)
{
    if (_rc == null) return;
    if (_hold) return;
    _userDamp = (_seat != null ? (IMyShipController)_seat : _rc).DampenersOverride;   // the pilot flips it (on his own seat: per-controller state is unverified, K7) with the V key: remembered for the next hand-over
    bool have = false; Vector3D pt = Vector3D.Zero, dir = Vector3D.Zero, gp = Vector3D.Zero;
    var gl = PrimGuns(); int n = 0;
    if (_tgt != null && _aimRef != null)
    {
        foreach (var g in gl) if (g.IsFunctional) { gp += g.GetPosition(); n++; }
        gp = n > 0 ? gp / n : _rc.GetPosition();
        Vector3D mv = _rc.GetShipVelocities().LinearVelocity;
        have = Lead(_tgt, gp, mv, out pt, out dir);
    }
    bool shoot = false; _aimErrDeg = 0; _fireWinDeg = 0;
    if (have)
    {
        MatrixD rm = _aimRef.WorldMatrix;
        Vector3D d = Vector3D.TransformNormal(dir, MatrixD.Transpose(rm));
        _pipR = Math.Atan2(d.X, -d.Z) * 57.2958; _pipU = Math.Atan2(d.Y, -d.Z) * 57.2958;
        double dist = Vector3D.Distance(pt, gp);
        _aimErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(rm.Forward, dir), -1, 1)) * 57.2958;
        _fireWinDeg = BatWin(dist);
        shoot = dist < MaxFireRange && _aimErrDeg < _fireWinDeg && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax;
        if (shoot && FriendCheck) { MatesInLine(gp, rm.Forward, dist); if (_tick % 20 == 0) CheckLineOfFire(gp, rm.Forward, dist); if (_now < _holdFireUntil) shoot = false; }
    }
    if (have && !ManAutoFire && _tick % 10 == 3) BatPick();   // (the pip follows the primary battery even when the script does not fire)
    if (ManAutoFire) { SetFire(shoot); BatFire(have, _rc.GetShipVelocities().LinearVelocity); }
    // fly-by-wire: stick rate + the PD rate to the lead point, the stick fades the assist; no lock: full release (native feel)
    bool ov = false;
    if (_asLvl == 2 && have && _seat != null)
    {
        var ri = _seat.RotationIndicator;
        Vector3D l = new Vector3D(MathHelper.Clamp(ri.X / ManScale, -1, 1), MathHelper.Clamp(ri.Y / ManScale, -1, 1), MathHelper.Clamp(_seat.RollIndicator / ManScale, -1, 1)) * (ManGain * ManSign);
        Vector3D wp = Vector3D.TransformNormal(l, _seat.WorldMatrix);
        double tg = MathHelper.Clamp(1 - wp.Length() / ManPrio, 0, 1);
        _asG = tg < _asG ? Math.Max(tg, _asG - dt / 0.15) : Math.Min(tg, _asG + dt / 0.5);
        Vector3D los = Vector3D.Zero;
        if (_havePrev) los = Vector3D.Cross(_prevAim, dir) / dt;
        if (los.LengthSquared() > 4) los = Vector3D.Normalize(los) * 2;
        _prevAim = dir; _havePrev = true;
        Vector3D wa = AimW(dir, Vector3D.Zero, los, dt);
        if (wa.LengthSquared() > ManAssistMax * ManAssistMax) wa = Vector3D.Normalize(wa) * ManAssistMax;
        Vector3D w = wp + wa * _asG;
        if (w.LengthSquared() > MaxTurn * MaxTurn) w = Vector3D.Normalize(w) * MaxTurn;
        GyroRate(w); ov = true;
    }
    else { _asG = 1; _havePrev = false; }
    if (!ov && _asOv) ReleaseGyros();
    _asOv = ov;
}

string ManMode() { return Man ? "MANUAL" + (_asLvl > 0 ? " ASSIST-" + (_asLvl == 2 ? "FBW" : "HUD") : "") : (_occ ? "AUTO (pilot aboard) " : "AUTO ") + _mode; }

// compact cockpit page (<= 8 lines)
string ManHud()
{
    if (_rc == null) return "NO CONTROLLER\n";   // (the Remote Control was destroyed)
    var b = new StringBuilder();
    b.Append("MODE ").Append(ManMode()).Append(_hold ? " (seat taken)" : "").Append('\n');
    if (_tgt != null)
    {
        b.Append("TGT  ").Append(Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()).ToString("0")).Append(" m  v ").Append(_tgt.V(_now).Length().ToString("0")).Append("  seen ").Append((_now - _tgt.Seen).ToString("0.0")).Append("s  T ").Append(_leadT.ToString("0.00")).Append('\n');
        b.Append("PIP  R").Append(_pipR.ToString("+0.0;-0.0")).Append(" U").Append(_pipU.ToString("+0.0;-0.0")).Append("  err ").Append(_aimErrDeg.ToString("0.0")).Append(" win ").Append(_fireWinDeg.ToString("0.0")).Append(_fireWinDeg > 0 && _aimErrDeg < _fireWinDeg ? " FIRE" : "").Append("  B").Append(_prim + 1).Append(_bat[_prim].Cls).Append('\n');
    }
    else b.Append("TGT  none (").Append(_contacts.Count).Append(")\n");
    b.Append("PWR ").Append((_batFrac * 100).ToString("0")).Append("% AMMO ").Append((_ammoFrac * 100).ToString("0")).Append("% THR ").Append((_powerScale * 100).ToString("0")).Append("% DAMP ").Append(_rc.DampenersOverride ? "on" : "off").Append("  DEADMAN ").Append(_dm.Count == 0 ? "-" : Man ? "off" : "armed").Append('\n');
    if (_menuOn) { var it = ManMenuItems.Split(','); b.Append("MENU > ").Append(it[_menuI % it.Length].Trim()).Append(' ').Append(_menuI % it.Length + 1).Append('/').Append(it.Length).Append('\n'); }
    if (_err != "") b.Append("! ").Append(_err).Append('\n');
    return b.ToString();
}
void ManShow()
{
    if (_hud == null) return;
    try { _hud.WriteText(ManHud()); } catch (Exception) { _hud = null; }
}
// <<< MANUAL v1.0 <<<

// >>> BATTERY v1.0 (weapon batteries, one lead solver per class, primary choice, standoff, turret-only mode; docs/design/control_arch.md s3, tools/blocks_sync.py) >>>
// Fixed guns (never turrets) form up to four batteries by the tag in the block name: [B1] .. [B4]. An untagged gun joins by class: ballistic (gatling, autocannon) B1, rocket B2, railgun B3,
// shell (artillery, assault cannon) B4. The class of a battery (BALL ROCK RAIL SHEL: speed, range, minimum range, shot interval, reload, dps, dispersion) comes from its first gun's
// definition id (BatSpec); a battery of mixed guns is treated as its first gun's class.
// PRIMARY battery = the one Control() / Aim() orient the ship by (ProjSpeed, MaxFireRange and _aimRef follow it, and with them the script's own LEAD solver): argmax of
//   score = dps x guns x P(hit) x P(ready)   at the present range, re-evaluated every 10 ticks, switched only after 3 s and when the newcomer scores 25 % more (BatPick).
//   P(hit) = clamp(1 - (LeadMiss(track, T) x Miss Scale + dispersion x range) / track radius, 0, 1), T = range / projectile speed; P(ready): a railgun / shell battery counts when its
//   next shot is due within 1.5 s (the confirmed-shot state machine of the Longbow: ammo drop, IsShooting, retry), other batteries are always ready.
// SECONDARY batteries fire on their own lead solution (BatAim: this class' ProjSpeed through the same Lead()) whenever THEIR boresight is inside the silhouette window, in their own
// range band and the line is clear; continuous fire (Shoot_On/Off) for ballistic guns, ShootOnce for the others (BatShoot). The primary fires through the script's own fire decision.
// STANDOFF (BatStand, every 30 ticks, only on a ship with a non-ballistic battery and Standoff By Weapons=true): the range on a 100 m grid that maximises the sum of the scores minus
//   Threat Weight x total dps x threat(r), threat = 2.5 inside 450 m, 1.0 inside 1,000 m, 0.2 beyond (the enemy is assumed to carry gatlings / rockets of 800 m): a rail or artillery ship
//   kites at 1,000 m, a rocket + gatling ship closes to ~500 m.
// SALVO ([Battery] Salvo OFF/ON/AUTO, default OFF; AUTO = single-shot batteries of 6 or more guns): instead of one gun per slot every working gun of the battery gets ShootOnce in the same tick; one second later the guns whose
//   magazine shrank are counted: 70 % or more = the whole battery recharges together (next salvo after Reload x 1.1 + 0.5 s), fewer = the rest are still charging (ask again in 4 s), none = ask again in 2 s.
// TURRET-ONLY MODE (Turret Only AUTO = the ship has turrets, ON, OFF): a track that every working battery would hit with P < 0.15 is left to the turrets: the fixed batteries refuse it
//   (BatOk) and target selection puts it behind every track a battery can hit (BatHope, debounced 2 s). The Track class carries the field "public double Hl = -99;".
// TURN-RATE CALIBRATION (BatTurn, Auto Max Turn AUTO = large grids, ON, OFF): while the first slews saturate the gyros, the peak angular acceleration a is measured; then
//   MaxTurn = 0.85 x sqrt(2 a x 0.6 rad) and AimKp = MaxTurn / 0.6 (the ship can stop inside the 0.6 rad PD zone); stored in Storage (field 8).
// Hooks: BatCfg() at the end of LoadConfig; BatSort() at the end of GetBlocks (after _guns); PrimGuns() instead of _guns for the gun centre in Control() and for SetFire(); SetFire() starts with
//   "if (_bat[_prim].Cool > 0) { _firing = on; BatShoot(_bat[_prim], on); return; }"; BatOk(_bat[_prim], dist) in the primary's fire decision (Control); BatFire(haveAim, myVel) after it
//   (BatFire(false, ..) in IDLE, Release and ManFly); "+ BatHope(t) + BatKz(t, dist)" in the target score of PruneAndSelect; BatTurn(w) in Aim(); BatSave() / BatLoad(..) for Storage; BatStatus() in Display.
class Bat
{
    public string Cls = "-"; public double Speed = 800, Range = 700, MinR, Cool, Rel, Dps, Dsp, Next, Rdy, Pend = -1, Am, Sc; public bool On, Saw, Want, Sv; public int I; public IMyUserControllableGun Gn; public long[] Ar;
    public List<IMyUserControllableGun> G = new List<IMyUserControllableGun>();
}
Bat[] _bat = { new Bat(), new Bat(), new Bat(), new Bat() };
int _pd; bool PdEx = true; double KzHold = 0; double PdRad = 6;   // point-defence battery (1-based, 0 = none) and the largest track radius it counts as "small"
bool _rkt; int _prim, _batFix; double _primT = -99, _bs0, _br0; bool BatSo = true; string BatTur = "AUTO", BatCal = "AUTO", BatSal = "OFF";
List<IMyUserControllableGun> PrimGuns() { return _bat[_prim].G; }

void BatCfg()
{
    const string S = "Battery";
    string p = Cfg(S, "Primary (AUTO/B1..B4)", "AUTO").Trim().ToUpperInvariant();
    _batFix = p.Length == 2 && p[0] == 'B' && p[1] >= '1' && p[1] <= '4' ? p[1] - '0' : 0;
    BatSo = Cfg(S, "Standoff By Weapons", BatSo);
    LooseFar = Cfg(S, "Loose Fire Beyond Standoff", LooseFar);
    BatTur = Cfg(S, "Turret Only (AUTO/ON/OFF)", BatTur).Trim().ToUpperInvariant();
    BatCal = Cfg(S, "Auto Max Turn (AUTO/ON/OFF)", BatCal).Trim().ToUpperInvariant();
    BatSal = Cfg(S, "Salvo (AUTO/ON/OFF)", BatSal).Trim().ToUpperInvariant();
    string pd = Cfg(S, "Point Defence (OFF/B1..B4)", "OFF").Trim().ToUpperInvariant();   // that battery shoots only small / incoming-kamikaze tracks, one gun at a time; the other batteries leave those tracks alone
    _pd = pd.Length == 2 && pd[0] == 'B' && pd[1] >= '1' && pd[1] <= '4' ? pd[1] - '0' : 0;
    PdRad = Cfg(S, "Point Defence Max Radius (m)", PdRad); PdEx = Cfg(S, "Point Defence Exclusive", PdEx);
    KzHold = Cfg(S, "Kamikaze Rail Hold Range (m)", KzHold);   // 0 = off; > 0: railgun / shell batteries leave a classified incoming kamikaze alone beyond this range
}

void BatSp(Bat b, string c, double sp, double rg, double mr, double cool, double rel, double dps, double dsp)
{ b.Cls = c; b.Speed = sp; b.Range = rg; b.MinR = mr; b.Cool = cool; b.Rel = rel; b.Dps = dps; b.Dsp = dsp; }
bool BatBall(string s) { return s.Contains("Gatling") || s.Contains("Autocannon") || s.Contains("Interior"); }
void BatSpec(Bat b)
{
    b.Cls = "-";
    if (b.G.Count == 0) return;
    string s = b.G[0].BlockDefinition.ToString();   // "MyObjectBuilder_SmallGatlingGun/" (a gatling has no subtype), "...SmallMissileLauncherReload/SmallRailgun"
    bool big = s.Contains("Large");
    if (BatBall(s)) BatSp(b, "BALL", _bs0, _br0, 0, 0, 0, 1000, 0.004);
    else if (s.Contains("Calibre")) BatSp(b, "SHEL", 500, (big ? 2000 : 1400) * 0.85, 150, 1, big ? 12 : 6, big ? 500 : 300, 0.001);   // (before "Missile": the shell guns sit in launcher block types)
    else if (s.Contains("Railgun")) BatSp(b, "RAIL", big ? 2000 : 1000, (big ? 2000 : 1400) * 0.85, 150, 0.25, big ? 60 : 20, big ? 800 : 400, 0);
    else BatSp(b, "ROCK", 200, 680, 100, 0.35, 0, 500, 0.002);
}

int BatN(Bat b) { int n = 0; foreach (var g in b.G) if (g.IsFunctional) n++; return n; }

void BatSort()
{
    if (_bs0 == 0) { _bs0 = ProjSpeed; _br0 = MaxFireRange; }
    foreach (var b in _bat) b.G.Clear();
    foreach (var g in _guns)
    {
        string n = g.CustomName, s = g.BlockDefinition.ToString(); int k = -1;
        for (int i = 0; i < 4; i++) if (n.Contains("[B" + (i + 1) + "]")) k = i;
        if (k < 0) k = BatBall(s) ? 0 : s.Contains("Calibre") ? 3 : s.Contains("Railgun") ? 2 : 1;
        _bat[k].G.Add(g);
    }
    foreach (var b in _bat) { BatSpec(b); b.Sv = b.Rel > 0 && (BatSal == "ON" || (BatSal == "AUTO" && b.G.Count >= 6)) && !(_pd > 0 && b == _bat[_pd - 1]); }
    BatSet(_batFix > 0 && _bat[_batFix - 1].G.Count > 0 ? _batFix - 1 : _bat[_prim].G.Count > 0 ? _prim : BatFirst());
}
int BatFirst() { for (int i = 0; i < 4; i++) if (BatN(_bat[i]) > 0) return i; return 0; }

// make battery i the primary (also the refresh of ProjSpeed, MaxFireRange and the aim reference after a block was lost)
void BatSet(int i)
{
    if (i != _prim)
    {
        if (_firing || _bat[_prim].On) foreach (var g in _bat[_prim].G) if (g.IsFunctional) g.ApplyAction("Shoot_Off");   // (only what the script switched on: a pilot holding the trigger is not cut off by a pip-only primary switch)
        _bat[_prim].On = false; _firing = false; _primT = _now; _prim = i;
    }
    var b = _bat[i];
    ProjSpeed = b.Speed; MaxFireRange = b.Range; _rkt = b.Cls == "ROCK";
    IMyTerminalBlock r = _rc;
    foreach (var g in b.G) if (g.IsFunctional) { r = g; break; }
    _aimRef = r;
}

// unpredictable displacement (m) of the track after T s of flight (the Basilisk's FireGate "unc": the acceleration the lead cannot foresee)
double BatUnc(Track t, double T) { return 0.5 * Math.Max(0, t.AccMag - t.EA().Length()) * T * T; }
double BatPh(Bat b, double r, Track t) { return MathHelper.Clamp(1 - (BatUnc(t, r / b.Speed) + b.Dsp * r) / t.Radius, 0, 1); }
// score of battery b at range r; w = how much a battery that is not ready is discounted (1: not at all counted, 0.5: halved for the standoff planning)
double BatSc(Bat b, double r, double w)
{
    int n = BatN(b);
    if (n == 0 || r < b.MinR || r > b.Range || (_pd > 0 && b == _bat[_pd - 1])) return 0;
    return b.Dps * n * BatPh(b, r, _tgt) * (b.Rel > 0 && _now + 1.5 < b.Rdy ? 1 - w : 1);
}

void BatPick()
{
    if (_tgt == null || _rc == null) return;
    double r = Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()), bm = 0; int bi = -1, p = _prim;
    for (int i = 0; i < 4; i++) { _bat[i].Sc = BatSc(_bat[i], r, 1); if (_bat[i].Sc > bm) { bm = _bat[i].Sc; bi = i; } }
    if (_batFix > 0 && _bat[_batFix - 1].G.Count > 0) p = _batFix - 1;
    else if (BatN(_bat[p]) == 0) p = BatFirst();
    else if (bi >= 0 && bi != p && _now - _primT > 3 && _bat[bi].Sc > _bat[p].Sc * 1.25) p = bi;
    BatSet(p);
    if (_tick % 30 == 3) BatStand();
}

void BatStand()
{
    if (!BatSo || _tgt == null) return;
    double hi = 0, tot = 0; bool nb = false;
    foreach (var b in _bat) { int n = BatN(b); if (n == 0) continue; if (b.Cls != "BALL") nb = true; hi = Math.Max(hi, b.Range); tot += b.Dps * n; }
    double lo = Math.Max(MinRange, 60 + Me.CubeGrid.WorldVolume.Radius), bj = -1e9;
    if (!nb || hi < lo) return;
    for (double r = Math.Ceiling(lo / 100) * 100; r <= hi; r += 100)
    {
        double j = -tot * (r < 450 ? 2.5 : r < 1000 ? 1 : 0.2);
        foreach (var b in _bat) j += BatSc(b, r, 0.5);
        if (j > bj) { bj = j; Standoff = r; }
    }
}

// silhouette window (deg) of the tracked target at this range: the fire window of every battery
bool LooseFar = true;   // [Battery] Loose Fire Beyond Standoff: while still closing (range > Standoff + 100) the window is the whole silhouette, not the 3 m hull point
double BatWin(double dist) { double aimR = AimActive(_tgt) && !(LooseFar && dist > Standoff + 100) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6; return Math.Max(0.3, Math.Atan(aimR / Math.Max(dist, 1)) * 57.2958) * FireTol; }
bool BatTO() { return BatTur == "ON" || (BatTur == "AUTO" && _turrets.Count > 0); }
bool PdTgt(Track t) { return t != null && (KzLive(t.Id) || t.Radius <= PdRad); }
bool BatOk(Bat b, double d)
{
    if (_pd > 0 && (b == _bat[_pd - 1] ? !PdTgt(_tgt) : PdEx && PdTgt(_tgt))) return false;   // the point-defence battery shoots small / kamikaze tracks only; with PdEx the other batteries leave those tracks alone
    if (KzHold > 0 && (b.Cls == "RAIL" || b.Cls == "SHEL") && _tgt != null && KzLive(_tgt.Id) && d > KzHold) return false;   // slow-reloading heavy guns are not spent on an incoming kamikaze until it is this close
    if (b.Cls == "ROCK" && BatUnc(_tgt, d / 200) > Math.Max(_tgt.Radius * 0.8, 3)) return false;   // rockets cannot be recalled: not at a jinking target (FireGate)
    return !BatTO() || BatPh(b, d, _tgt) >= 0.15;
}
// target score (lower = better) of an incoming kamikaze that the heavy guns will not shoot yet (Kamikaze Rail Hold Range): its priority bonus is cancelled and it is
// ranked behind every other track, so the tracking stays free for a large ship that comes into range; with nothing else about it is still the target (the turrets answer it)
double BatKz(Track t, double dist) { return KzHold > 0 && dist > KzHold && KzLive(t.Id) ? 3000 - KzScore(t) : 0; }
double BatHope(Track t)
{
    if (!BatTO() || _rc == null) return 0;
    double r = Vector3D.Distance(t.P(_now), _rc.GetPosition());
    foreach (var b in _bat) if (BatN(b) > 0 && BatPh(b, r, t) >= 0.15) return _now - t.Hl < 2 ? 1500 : 0;   // (a good sample after a hopeless one counts after 2 s: the acceleration estimate swings)
    t.Hl = _now;
    return 1500;
}

// lead solution of a secondary battery: true when it may fire now
bool BatAim(Bat b, Vector3D myVel)
{
    if (_tgt == null) return false;
    Vector3D gp = Vector3D.Zero, pt, dir; int n = 0; IMyUserControllableGun f = null;
    foreach (var g in b.G) if (g.IsFunctional) { gp += g.GetPosition(); n++; if (f == null) f = g; }
    if (n == 0) return false;
    gp /= n;
    double sp = ProjSpeed, sl = _leadT; bool sr = _rkt; ProjSpeed = b.Speed; _rkt = b.Cls == "ROCK";
    bool ok = Lead(_tgt, gp, myVel, out pt, out dir);
    ProjSpeed = sp; _leadT = sl; _rkt = sr;
    if (!ok) return false;
    Vector3D fw = f.WorldMatrix.Forward;
    double dist = Vector3D.Distance(pt, gp), err = Math.Acos(MathHelper.Clamp(Vector3D.Dot(fw, dir), -1, 1)) * 57.2958;
    bool go = dist >= b.MinR && dist < b.Range && err < BatWin(dist) && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax && BatOk(b, dist);
    if (go && FriendCheck) { MatesInLine(gp, fw, dist); if (_now < _holdFireUntil) go = false; }
    return go;
}

void BatFire(bool allow, Vector3D myVel)
{
    if (allow && _tick % 10 == 3) BatPick();
    for (int i = 0; i < 4; i++)
    {
        var b = _bat[i];
        if (i == _prim || b.G.Count == 0) continue;
        if (!allow) b.Want = false; else if ((_tick + i) % 3 == 0) b.Want = BatAim(b, myVel);
        BatShoot(b, b.Want);
    }
}

long BatAm(IMyUserControllableGun g) { var v = g.GetInventory(0); return v != null ? v.CurrentVolume.RawValue : 0; }
IMyUserControllableGun BatGun(Bat b) { for (int k = 0; k < b.G.Count; k++) { var g = b.G[++b.I % b.G.Count]; if (g.IsFunctional) return g; } return null; }

void BatShoot(Bat b, bool on)
{
    if (b.Pend >= 0) BatCheck(b);
    if (b.Cool > 0)
    {
        if (!on || _now < b.Next || (b.Rel > 0 && (b.Pend >= 0 || _now < b.Rdy))) return;
        b.Next = _now + b.Cool;
        if (b.Sv)
        {
            if (b.Ar == null || b.Ar.Length != b.G.Count) b.Ar = new long[b.G.Count];
            for (int k = 0; k < b.G.Count; k++) { var q = b.G[k]; if (q.IsFunctional) { b.Ar[k] = BatAm(q); q.ShootOnce(); } else b.Ar[k] = -1; }
            b.Pend = _now; b.Saw = false; b.Gn = null;
        }
        else if (b.Rel > 0)
        {
            var g = BatGun(b);
            if (g == null) return;
            b.Gn = g; b.Pend = _now; b.Saw = false; b.Am = BatAm(g); g.ShootOnce();
        }
        else foreach (var g in b.G) if (g.IsFunctional) g.ShootOnce();
    }
    else if (on != b.On || (on && _tick % 30 == 0)) { b.On = on; foreach (var g in b.G) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off"); }
}
// single-shot guns: an ammo drop or a busy-then-idle gun confirms the shot and starts the reload; nothing within 3 s = still charging, ask again in 2 s
void BatCheck(Bat b)
{
    if (b.Sv)
    {   // SALVO: every working gun was asked at once; after 1 s count the ones whose magazine shrank. Most of them fired: the whole battery recharges together; few did: the rest are still charging, ask again in 4 s
        if (_now - b.Pend < 1) return;
        int n = 0, f = 0;
        for (int k = 0; k < b.G.Count && k < b.Ar.Length; k++) if (b.Ar[k] >= 0) { n++; if (BatAm(b.G[k]) < b.Ar[k]) f++; }
        b.Pend = -1;
        b.Rdy = _now + (f == 0 ? 2 : f * 10 >= n * 7 ? b.Rel * 1.1 + 0.5 : 4);
        return;
    }
    var g = b.Gn;
    if (g == null || g.Closed) { b.Pend = -1; return; }
    if (g.IsShooting) b.Saw = true;
    if (BatAm(g) < b.Am) { b.Pend = -1; b.Rdy = _now + b.Rel * 1.1 / b.G.Count + 0.5; }
    else if (b.Saw && !g.IsShooting) { b.Pend = -1; b.Rdy = _now + b.Rel / b.G.Count - 1; }
    else if (_now - b.Pend > 3) { b.Pend = -1; b.Rdy = _now + 2; }
}

// ROCK class lead solver (the Basilisk's, "capped" rocket model): a rocket leaves at ship velocity + 100 m/s along the barrel, accelerates at 600 m/s^2 along it up to 200 m/s and
// bends towards the barrel direction; Newton on the flight time T finds the barrel direction d with rocket(d, T) = target(T). Lead() starts with
// "if (_rkt) return BatRkt(t, shooter, shooterVel, out point, out dir);" while a rocket battery is being solved.
double RkT(double d) { return d <= 25 ? (Math.Sqrt(1e4 + 1200 * d) - 100) / 600 : 1 / 6.0 + (d - 25) / 200; }
void RkP(double T, double ua, double us, out double s, out double q, out double sv)
{
    double V = 200, A = 600, va = ua + 100, t1, th;
    if (va * va + us * us >= V * V) { t1 = 0; th = Math.Atan2(us, va); }
    else { t1 = (Math.Sqrt(V * V - us * us) - va) / A; th = Math.Asin(MathHelper.Clamp(us / V, -1, 1)); }
    if (T <= t1) { s = va * T + 0.5 * A * T * T; q = us * T; sv = va + A * T; return; }
    s = va * t1 + 0.5 * A * t1 * t1; q = us * t1;
    double k = A / V, h = T - t1, c = Math.Tan(0.5 * th), x = c * Math.Exp(-k * h);
    q += V / k * 2 * (Math.Atan(c) - Math.Atan(x));
    s += V * (h - Math.Log((1 + c * c) / (1 + x * x)) / k);
    sv = V * (1 - x * x) / (1 + x * x);
}
bool BatRkt(Track t, Vector3D shooter, Vector3D shooterVel, out Vector3D point, out Vector3D dir)
{
    Vector3D rp = t.P(_now) - shooter, tv = t.V(_now), ta = LeadAcc(t);
    double rl = rp.Length();
    point = shooter + rp; dir = Vector3D.Zero;
    if (rl < 1 || double.IsNaN(rl)) return false;
    Vector3D los = rp / rl, d = los, p = rp;
    double tt = RkT(rl), h = LeadHold(t), f = 99;
    _aimLos = los; _aimNeed = LeadMargin * LeadMiss(t, tt);
    Vector3D off = AimOffset(t);
    for (int i = 0; i < 12; i++)
    {
        p = rp + LeadOffset(t, off, los, tt) + (tv * tt + ta * LeadK(tt, h)) * LeadScale;
        Vector3D w = p, up = shooterVel - Vector3D.Dot(shooterVel, d) * d; double us = up.Length(), s, q, sv;
        RkP(tt, Vector3D.Dot(shooterVel, d), us, out s, out q, out sv);
        if (us > 1e-3) w -= up * (q / us);
        double wl = w.Length();
        if (wl < 1) break;
        d = w / wl; f = s - wl;
        tt = MathHelper.Clamp(tt - f / Math.Max(sv - Vector3D.Dot(d, tv + ta * LeadKd(tt, h)), 20), 0.02, 12);
    }
    _leadT = tt; point = shooter + p; dir = d;
    return Math.Abs(f) < 2 && !double.IsNaN(tt) && !double.IsNaN(d.X);
}

double _caA, _caT, _caS; Vector3D _caW; bool _caDone;
void BatTurn(Vector3D cmd)
{
    if (_caDone || BatCal == "OFF" || (BatCal != "ON" && Me.CubeGrid.GridSizeEnum != MyCubeSize.Large)) return;
    double dt = _now - _caT;
    if (dt < 0.1) return;
    Vector3D w = _rc.GetShipVelocities().AngularVelocity;
    bool sat = (cmd - w).Length() > 0.8;
    if (_caT > 0 && dt < 0.2 && sat) { _caA = Math.Max(_caA, (w - _caW).Length() / dt); _caS += dt; }
    _caW = w; _caT = _now;
    if (_caS > 3 || (_caS > 0.5 && !sat)) { _caDone = true; MaxTurn = MathHelper.Clamp(0.85 * Math.Sqrt(1.2 * _caA), 0.15, MaxTurn); AimKp = MaxTurn / 0.6; }
}
string BatSave() { return _caDone ? MaxTurn.ToString("0.###") : "0"; }
void BatLoad(string s) { double v; if (BatCal != "OFF" && double.TryParse(s, out v) && v > 0) { MaxTurn = v; AimKp = v / 0.6; _caDone = true; } }

string BatStatus()
{
    var s = new StringBuilder("Batt");
    for (int i = 0; i < 4; i++) if (_bat[i].G.Count > 0) s.Append(" B").Append(i + 1).Append(i == _prim ? "* " : " ").Append(_bat[i].Cls).Append('x').Append(BatN(_bat[i]));
    return s.Append(" so ").Append(Standoff.ToString("0")).Append(" w ").Append(MaxTurn.ToString("0.0")).Append(BatTO() ? " TO\n" : "\n").ToString();
}
// <<< BATTERY v1.0 <<<

// >>> HELMLINK v1.2 (shared block of the Helm scripts: IGC link to the Staff PB on the same construct, docs/design/control_arch.md s1.3, tools/blocks_sync.py) >>>
// The Helm sends, with TransmissionDistance.CurrentConstruct (never leaves the ship, no secret needed), on "<Ship Tag>.<suffix>" (Ship Tag default SHIP, [Link] in Custom Data):
//   .HB     long tick                                                   heartbeat every "Heartbeat (s)" (0.5): the Staff releases every override when it stays silent for 1.5 s
//   .H2S    MyTuple<int flags, long tgtId, Vector3D tgtPos, Vector3D tgtVel, float tgtRadius, float batt>   5 Hz: flags bit0-2 mode (0 IDLE 1 GUARD 2 HUNT 4 RTB 5 MANUAL 6 ASSIST 7 REACT),
//           bit3 pilot seated, bit4 firing, bit6 dead-man timer armed, bit7 degraded (no thruster or no gyro)
//   .PILOT  MyTuple<int mode, int seated>                               on change
//   .FLT    MyTuple<long id, Vector3D pos, Vector3D vel, double radius, long sender>   the Helm's own target shares and "finished" (radius < 0): the Staff copies them to the fleet channel
//   H2S flags bit 8 (256) = "split Helm" (v1.2): the Staff then takes over the fleet channel, the other fleets' target feeds and damage sensing (docs/design/helm_split.md)
// and listens on .S2H  MyTuple<string cmd, Vector3D rally, double radius>   (cmd = any Command() verb; an EMPTY cmd carries the fleet home point in rally),
//   .TGT  the fleet / feed target tuple (as .FLT), relayed by the Staff,   .DMG  MyTuple<int hits, Vector3D hitDir, float hitDirAge>  hit counter and direction from the Staff's damage scan, 1 Hz (also its liveness beat).
// The Helm counts a hit only when the counter grows while the link was already alive; silence for 3 s after it was heard counts as one hit (a destroyed Staff means damage).
// The link is optional: [Link] Enable (AUTO/ON/OFF) = AUTO switches it on while a PB named "[Staff] ..." is on the construct, so the single-PB ships are untouched.
// Script hooks: HlkCfg() at the end of LoadConfig; HlkBlocks() at the end of GetBlocks; HlkTick() once per Update1 tick after ManTick().
// Uses from the script: Cfg(), Command(), Man, _occ, _mode (Mode.IDLE/GUARD/HUNT/ASSIST/RTB), _tgt (Track), _now, _tick, _firing, _dm, _thr, _gyros, _batFrac.
// BLOCK PREFIX (v1.1, M5): [Link] Block Prefix (default empty = off) limits every block list the script builds to blocks whose name starts with it ("[SH]" on the warships), so the
// blocks of drones that are merged to the ship's cradles (they are blocks of the SAME grid while clamped) are never adopted: Own(b) replaces b.IsSameConstructAs(Me) in the lists.
// Ship Tag also falls back to the [Ship] Tag key of the ship designs' Custom Data.
bool Own(IMyTerminalBlock b) { return b.IsSameConstructAs(Me) && (HlkPre == "" || b.CustomName.StartsWith(HlkPre, StringComparison.OrdinalIgnoreCase)); }
bool HlkOn, _hlkLost; string HlkShip = "SHIP", HlkEn = "AUTO", HlkPre = "", _hlkKey = ""; double HlkHb = 0.5, _hlkDmgT = -99; int _hlkMd = -9, _hlkSeat = -9, _hlkHits; IMyBroadcastListener _hlkIn, _hlkTgt, _hlkDmg;
void HlkCfg()
{
    HlkEn = Cfg("Link", "Enable (AUTO/ON/OFF)", HlkEn).Trim().ToUpperInvariant(); HlkShip = Cfg("Link", "Ship Tag", _ini.Get("Ship", "Tag").ToString(HlkShip)).Trim().ToUpperInvariant(); HlkPre = Cfg("Link", "Block Prefix", HlkPre).Trim(); HlkHb = Math.Max(0.1, Cfg("Link", "Heartbeat (s)", HlkHb));
}
void HlkBlocks()
{
    var l = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(l, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase));
    HlkOn = HlkEn == "ON" || (HlkEn != "OFF" && l.Count > 0);
    string k = HlkOn ? HlkShip : "";
    if (k == _hlkKey) return;
    foreach (var x in new[] { _hlkIn, _hlkTgt, _hlkDmg }) if (x != null) IGC.DisableBroadcastListener(x);
    _hlkIn = k == "" ? null : IGC.RegisterBroadcastListener(k + ".S2H"); _hlkTgt = k == "" ? null : IGC.RegisterBroadcastListener(k + ".TGT"); _hlkDmg = k == "" ? null : IGC.RegisterBroadcastListener(k + ".DMG"); _hlkKey = k;
}
bool FleetV(Vector3D v) { return Math.Abs(v.X) < 1e9 && Math.Abs(v.Y) < 1e9 && Math.Abs(v.Z) < 1e9; }   // false for NaN and absurd values
bool FleetOk(MyTuple<long, Vector3D, Vector3D, double, long> d) { return d.Item1 != Me.CubeGrid.EntityId && FleetV(d.Item2) && FleetV(d.Item3) && d.Item4 < 1e5 && !double.IsNaN(d.Item4); }
void FleetSend(MyTuple<long, Vector3D, Vector3D, double, long> tup) { if (HlkOn) IGC.SendBroadcastMessage(HlkShip + ".FLT", tup, TransmissionDistance.CurrentConstruct); }   // the Staff copies it to the fleet channel
void HlkTick()
{
    if (!HlkOn || _hlkIn == null) return;
    while (_hlkIn.HasPendingMessage)
    {
        var m = _hlkIn.AcceptMessage();
        if (!(m.Data is MyTuple<string, Vector3D, double>)) continue;
        var d = (MyTuple<string, Vector3D, double>)m.Data;
        if (!string.IsNullOrEmpty(d.Item1)) Command(d.Item1); else if (FleetV(d.Item2)) { _dynHome = d.Item2; _dynHomeT = _now; }   // (empty verb: the fleet home point)
    }
    ReadTgt(_hlkTgt);
    while (_hlkDmg.HasPendingMessage)
    {
        var m = _hlkDmg.AcceptMessage();
        if (!(m.Data is MyTuple<int, Vector3D, float>)) continue;
        var d = (MyTuple<int, Vector3D, float>)m.Data;
        if (_now - _hlkDmgT < 3 && d.Item1 > _hlkHits) { _hitAt = _now; _hits++; }   // a hit only when the link was already alive: a recompiled Staff restarts at 0, a recompiled Helm must not read an old count as a hit
        _hlkHits = d.Item1; _hlkDmgT = _now; _hlkLost = false;
        if (d.Item3 < 5 && d.Item2.LengthSquared() > 0.01) { _hitDir = d.Item2; _hitDirT = _now - d.Item3; }
    }
    if (!_hlkLost && _hlkDmgT > -90 && _now - _hlkDmgT > 3) { _hlkLost = true; _hitAt = _now; _hits++; }   // the Staff went silent: count one hit (a destroyed PB means damage)
    var cc = TransmissionDistance.CurrentConstruct;
    if (_tick % Math.Max(1, (int)(HlkHb * 60)) == 3) IGC.SendBroadcastMessage(HlkShip + ".HB", (long)_tick, cc);
    int md = Man ? 5 : _mode == Mode.ASSIST ? 6 : _mode == Mode.REACT ? 7 : (int)_mode, seat = _occ ? 1 : 0;
    if (md != _hlkMd || seat != _hlkSeat) { _hlkMd = md; _hlkSeat = seat; IGC.SendBroadcastMessage(HlkShip + ".PILOT", new MyTuple<int, int>(md, seat), cc); }
    if (_tick % 12 == 5)
    {
        int f = md | seat << 3 | 256 | (_firing ? 16 : 0) | (_dm.Count > 0 && !Man ? 64 : 0) | (_thr.Count == 0 || _gyros.Count == 0 ? 128 : 0);
        var t = _tgt;
        IGC.SendBroadcastMessage(HlkShip + ".H2S", new MyTuple<int, long, Vector3D, Vector3D, float, float>(f, t != null ? t.Id : 0, t != null ? t.P(_now) : Vector3D.Zero, t != null ? t.V(_now) : Vector3D.Zero, t != null ? (float)t.Radius : 0, (float)_batFrac), cc);
    }
}
// <<< HELMLINK v1.2 <<<

// >>> FRIENDLY BEACON v1.0 (shared block, synced by tools/blocks_sync.py, see docs/friendly-avoidance.md) >>>
// The sending half of FRIENDLY AVOIDANCE for the scripts that have no room for the whole block (Mantis, Wasp: 100,000-character limit) and
// for the Shrike Helm (80,900 budget): broadcasts this grid's bounding sphere (id, centre, velocity, radius) on the IGC channel DRONE_SPACE,
// so drones that do avoid friendly grids keep clear of this one. Hook: FaBeacon() in BroadcastStatus() (10 Hz). Uses _rc.
void FaBeacon()
{
    var v = Me.CubeGrid.WorldVolume;
    IGC.SendBroadcastMessage("DRONE_SPACE", new MyTuple<long, Vector3D, Vector3D, float>(Me.CubeGrid.EntityId, v.Center, _rc.GetShipVelocities().LinearVelocity, (float)v.Radius));
}
// <<< FRIENDLY BEACON v1.0 <<<

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


// >>> HARASSER LINK v1.0 (shared block of the Ballista Helm: IGC link to the Lookout module of the Spotter PB, docs/design/ballista_harasser_defence.md, tools/blocks_sync.py) >>>
// A small hostile craft that loiters and shoots (an AI drone launched by an enemy warship) is neither a kamikaze nor a wreck. The Spotter PB (same construct, name "[Spotter]...") finds it and
// picks a velocity bias; this block applies it, so the Helm stays the only writer of thrusters and gyros. Key "HDEF<this PB's EntityId>", TransmissionDistance.CurrentConstruct (never leaves the ship).
// The Helm sends (only while a "[Spotter]" PB is on the construct):
//   .H2A  MyTuple<int flags, Vector3D hitDir, double hitAge, double standoff, long tgtId, Vector3D tgtPos>   every 10 ticks: flags bit1 under fire, bit4 MANUAL, bit5 target radius >= 25,
//         bits 8-11 link version (1), bits 16-30 the hit counter; hitDir is zero when no direction is known
//   .HTK  MyTuple<long id, Vector3D pos, Vector3D vel, float radius, float age, int flags>   every 20 ticks, up to 4 contacts of radius <= 15 m seen in the last second
//         (flags bit0 = incoming kamikaze: that stays the KAMIKAZE DEFENCE's job)
// and listens on .A2H  MyTuple<Vector3D bias, Vector3D up, int flags, float ttl, long id1, long id2>   flags bit0 bias valid, bit1 drop the random jink, bit2 up request valid, bit3 a harasser
//   is tracked (else the bias answers fire from an unseen bearing); id1, id2 = the harassers (the Helm does not make one of them its target while a larger one is about, and does not copy its velocity)
// Everything times out after ttl (0.6 s): a silent or missing Spotter changes nothing, and ships without a "[Spotter]" PB are unaffected.
// Script hooks: HdTick() after HlkTick(); "+ HdScore(t)" in the target score; tv zero when HdHar(_tgt.Id); "&& !HdJ()" on the jink term; want = HdWant(want, cap) right before the KzWant line;
// HdUp() instead of Vector3D.Zero as the up of the two Aim() calls of Control() outside gravity; _sb.Append(HdStatus()) after KzStatus().
// Uses from the script: _contacts, _now, _tick, _hitDir, _hitDirT, _hits, UnderFire, Man, _tgt, Standoff, FleetV(), KzLive(), Track, Me, GridTerminalSystem, IGC.
IMyBroadcastListener _hdL; string _hdKey = ""; Vector3D _hdB, _hdU; int _hdF; double _hdT = -99, _hdTtl = 0.6; long _hdI1, _hdI2; bool _hdSp;
void HdTick()
{
    string k = "HDEF" + Me.EntityId;
    if (k != _hdKey) { _hdKey = k; _hdL = IGC.RegisterBroadcastListener(k + ".A2H"); }
    while (_hdL.HasPendingMessage)
    {
        var m = _hdL.AcceptMessage();
        if (!(m.Data is MyTuple<Vector3D, Vector3D, int, float, long, long>)) continue;
        var d = (MyTuple<Vector3D, Vector3D, int, float, long, long>)m.Data;
        if (!FleetV(d.Item1) || !FleetV(d.Item2)) continue;
        _hdB = d.Item1; _hdU = d.Item2; _hdF = d.Item3; _hdTtl = MathHelper.Clamp(d.Item4, 0.2, 2); _hdI1 = d.Item5; _hdI2 = d.Item6; _hdT = _now;
    }
    if (_tick % 300 == 1)
    {
        var l = new List<IMyProgrammableBlock>();
        GridTerminalSystem.GetBlocksOfType(l, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Spotter]", StringComparison.OrdinalIgnoreCase));
        _hdSp = l.Count > 0;
    }
    if (!_hdSp) return;
    var cc = TransmissionDistance.CurrentConstruct;
    if (_tick % 10 == 2)
    {
        var g = _tgt; bool hd = _now - _hitDirT < 5 && _hitDir.LengthSquared() > 0.01;
        int f = (UnderFire ? 2 : 0) | (Man ? 16 : 0) | (g != null && g.Radius >= 25 ? 32 : 0) | 256 | ((_hits & 0x7FFF) << 16);
        IGC.SendBroadcastMessage(k + ".H2A", new MyTuple<int, Vector3D, double, double, long, Vector3D>(f, hd ? _hitDir : Vector3D.Zero, hd ? _now - _hitDirT : 99, Standoff, g != null ? g.Id : 0, g != null ? g.P(_now) : Vector3D.Zero), cc);
    }
    if (_tick % 20 == 7)
    {
        int n = 0;
        foreach (var t in _contacts.Values)
        {
            if (t.Radius > 15 || _now - t.Seen > 1 || n >= 4) continue;
            n++; IGC.SendBroadcastMessage(k + ".HTK", new MyTuple<long, Vector3D, Vector3D, float, float, int>(t.Id, t.P(_now), t.V(_now), (float)t.Radius, (float)(_now - t.Seen), KzLive(t.Id) ? 1 : 0), cc);
        }
    }
}
bool HdOn { get { return _now - _hdT < _hdTtl && (_hdF & 1) != 0; } }
bool HdJ() { return HdOn && (_hdF & 2) != 0; }
bool HdHar(long id) { return id != 0 && _now - _hdT < 1.5 && (_hdF & 8) != 0 && (id == _hdI1 || id == _hdI2); }
double HdScore(Track t) { return HdHar(t.Id) ? 3000 : 0; }
Vector3D HdUp() { return HdOn && (_hdF & 4) != 0 ? _hdU : Vector3D.Zero; }
// adds the bias to the commanded velocity; over the speed cap the rest of the command is shrunk and the bias is kept whole (same rule as KzWant's break)
Vector3D HdWant(Vector3D want, double cap)
{
    if (!HdOn) return want;
    double bl = _hdB.Length(), room = Math.Max(cap - bl, 0);
    if (bl < 0.1) return want;
    if (want.LengthSquared() > room * room) want = want.LengthSquared() > 1e-6 ? Vector3D.Normalize(want) * room : want;
    return want + _hdB;
}
string HdStatus() { return HdOn ? ((_hdF & 8) != 0 ? "!! HARASSER (evading)\n" : "!! FIRE FROM AN UNSEEN BEARING (evading)\n") : ""; }
// <<< HARASSER LINK v1.0 <<<

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
                if (p.Length >= 7) ManLoad(p[5], p[6]);
                if (p.Length >= 8) BatLoad(p[7]);
            }
        }
    }
    catch (Exception ex) { CtorErr("storage", ex); }
    try
    {
        _lTgt = IGC.RegisterBroadcastListener(Tag + "_TGT");
        _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
        _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME");
        _lCrit = IGC.RegisterBroadcastListener(Tag + "_AIM");
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
    Storage = string.Join(";", _mode.ToString(), _home.X.ToString("R"), _home.Y.ToString("R"), _home.Z.ToString("R"), _homeSet ? "1" : "0", ManSave(), BatSave());
}

Mode ParseMode(string s, Mode def)
{
    switch (s.Trim().ToUpperInvariant())
    {
        case "IDLE": return Mode.IDLE; case "GUARD": return Mode.GUARD; case "HUNT": return Mode.HUNT;
        case "ASSIST": return Mode.ASSIST; case "RTB": return Mode.RTB; case "REACT": return Mode.REACT; default: return def;
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
    if ((src & (UpdateType.Trigger | UpdateType.Terminal | UpdateType.Script | UpdateType.Mod)) != 0)
    {
        if (!string.IsNullOrWhiteSpace(arg)) Command(arg); else ManMenu();   // Run without an argument: the cockpit-toolbar menu (MANUAL block)
    }
    if ((src & UpdateType.Update1) == 0) return;

    double dt = Runtime.TimeSinceLastRun.TotalSeconds;
    if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;
    _now += dt; _tick++;

    if (_tick % 600 == 0) GetBlocks();
    CheckGrid();
    if (_rc == null || !_rc.IsFunctional || !_rc.IsSameConstructAs(Me)) { _err = "No working Remote Control / cockpit"; if (!_rcOut || _tick % 60 == 0) { _rcOut = true; GetBlocks(); Display(); Release(); } return; }   // (not on this construct = a severed piece: same as no controller; look for another one at 1 Hz)
    _rcOut = false;
    ManSeat();   // MANUAL: seat -> mode machine first, so the overrides are gone the tick the seat is taken

    ReadIGC(); ReadSwarm();
    if (_tick % 6 == 3) BroadcastStatus();
    if (_tick % 6 == 0) PollDesignators();
    if (_tick % 10 == 0) { PruneAndSelect(); PowerAndHealth(); }
    MaintainLock();
    if (_tgt == null && !_aiOn && AutoScan && (_mode == Mode.HUNT || Gd && (!Calm || Big)) && (!Man || ManScan) && !Hot()) AutoScanStep();
    if (ShareTargets && _tgt != null && _tgt.Src != "IGC" && _tick % 30 == 0 && _now - _tgt.Seen < 1.0 && !Hot()) ShareTarget();

    ManTick(); HlkTick(); HdTick();
    if (Man) ManFly(dt); else if (!AiStep()) Control(dt);
    _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount);
    if (_tick % (Man ? 10 : 30) == 0) Display();
}

// =====================================================================
//  COMMANDS
void Command(string a)
{
    var w = a.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (w.Length == 0 || ManCmd(w)) return;
    switch (w[0])
    {
        case "guard": SetMode(Mode.GUARD); break;
        case "react": SetMode(Mode.REACT); break;
        case "hunt": SetMode(Mode.HUNT); break;
        case "assist": SetMode(Mode.ASSIST); break;
        case "rtb": SetMode(Mode.RTB); break;
        case "idle": case "stop": SetMode(Mode.IDLE); break;
        case "lock": ManualLock(); break;
        case "split": SplitFocus = true; break;
        case "focus": SplitFocus = false; break;
        case "unlock": _tgt = null; _contacts.Clear(); break;
        case "next": if (_tgt != null) IgnoreTarget(_tgt, 60, true); break;
        case "aialert": _alertT = _now; break;
        case "ai": if (w.Length > 1) { AiAssist = w[1] == "on"; FindAi(); } break;
        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;
        case "breach": if (w.Length > 1) BreachAuto = w[1] == "on"; break;
        case "home": if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } break;
        case "range":
            double r; if (w.Length > 1 && double.TryParse(w[1], out r)) Standoff = MathHelper.Clamp(r, MinRange, MaxFireRange); break;
        case "reload": LoadConfig(); KzLoad(); AiGateLoad(); GetBlocks(); break;
    }
}

void SetMode(Mode m)
{
    _mode = m;
    if ((m == Mode.IDLE || m == Mode.ASSIST) && !Man) Release();
}

// =====================================================================
//  CONFIG
void LoadConfig()
{
    MyIniParseResult res;
    if (!_ini.TryParse(Me.CustomData, out res)) { _err = "CustomData parse error line " + res.LineNo; return; }
    const string S = "Shrike";
    Tag = Cfg(S, "IGC Tag", Tag);
    StartMode = Cfg(S, "Start Mode (GUARD/HUNT/ASSIST/IDLE)", StartMode);
    Standoff = Cfg(S, "Standoff Range", Standoff);
    SepRange = Cfg(S, "Wingmate Separation Range (m)", SepRange); SepSpeed = Cfg(S, "Wingmate Separation Speed (m/s)", SepSpeed); MateClear = Cfg(S, "Wingmate Fire Clearance (m)", MateClear);
    MinRange = Cfg(S, "Min Range", MinRange);
    MaxSpeed = Cfg(S, "Combat Speed", MaxSpeed);
    OrbitSpeed = Cfg(S, "Orbit Speed", OrbitSpeed);
    Jink = Cfg(S, "Evasive Jinking", Jink);
    AutoScan = Cfg(S, "Auto Camera Scan", AutoScan);
    ShareTargets = Cfg(S, "Share Targets Over IGC", ShareTargets);
    FireTol = Cfg(S, "Fire Window Multiplier", FireTol);
    AiAssist = Cfg(S, "AI Block Search Assist", AiAssist);
    AiSearchEvery = (int)Cfg(S, "AI Search Interval (s)", (double)AiSearchEvery);
    MaxTurn = Cfg(S, "Max Turn Rate rad/s", MaxTurn);
    CapRange = Cfg(S, "Capital Standoff Range", CapRange);
    ManCfg(); HlkCfg(); BatCfg();
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
    GridTerminalSystem.GetBlocksOfType(ctrls, b => Own(b) && b.CanControlShip);
    _rc = null;
    foreach (var c in ctrls) if (c is IMyRemoteControl) { _rc = c; break; }
    if (_rc == null) foreach (var c in ctrls) if (c.IsMainCockpit) { _rc = c; break; }
    if (_rc == null && ctrls.Count > 0) _rc = ctrls[0];

    GridTerminalSystem.GetBlocksOfType(_gyros, b => Own(b));
    GridTerminalSystem.GetBlocksOfType(_thr, b => Own(b));
    GridTerminalSystem.GetBlocksOfType(_guns, b => Own(b) && !(b is IMyLargeTurretBase));
    GridTerminalSystem.GetBlocksOfType(_cams, b => Own(b));
    GridTerminalSystem.GetBlocksOfType(_turrets, b => Own(b));
    GridTerminalSystem.GetBlocksOfType(_ctcs, b => Own(b));
    GridTerminalSystem.GetBlocksOfType(_bats, b => Own(b));
    foreach (var c in _cams) c.EnableRaycast = true;
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
    BatSort(); ManBlocks(); HlkBlocks();   // BATTERY / MANUAL / HELMLINK blocks: batteries, _aimRef, seats, dead-man timer, cockpit surface, Staff link
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
    return (e.Type == MyDetectedEntityType.LargeGrid || e.Type == MyDetectedEntityType.SmallGrid) && e.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies;   // (grids only: no characters, no neutrals)
}

bool Friendly(MyDetectedEntityInfo e)
{
    var r = e.Relationship;
    return r == MyRelationsBetweenPlayerAndBlock.Owner || r == MyRelationsBetweenPlayerAndBlock.FactionShare || r == MyRelationsBetweenPlayerAndBlock.Friends;
}

double _fcT = -99; string _fc = "";   // last new contact: distance, speed, source (CAM / AI / IGC / ...) and when
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
    // SPOTTER feed: <Tag>_AIM = (target id, vital block in the target frame) from the Spotter PB: it becomes the breach point, drilled along the line of fire
    while (_lCrit.HasPendingMessage)
    {
        var msg = _lCrit.AcceptMessage();
        if (!(msg.Data is MyTuple<long, Vector3D>)) continue;
        var d = (MyTuple<long, Vector3D>)msg.Data; Track t;
        Vector3D fl;
        if (!_contacts.TryGetValue(d.Item1, out t) || !t.HasOri || (fl = t.P(_now) - Me.GetPosition()).LengthSquared() < 1) continue;
        if (_bId != t.Id) { _bId = t.Id; _bOn = false; _bBad.Clear(); }
        if (!BreachBad(d.Item2) && (!_bOn || (_bS - d.Item2).LengthSquared() > 25)) BreachSet(d.Item2, Vector3D.TransformNormal(Vector3D.Normalize(fl), MatrixD.Transpose(t.O)));   // (a stalled point is in _bBad: it is not fed back in)
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
        if (d.Item5 == Me.CubeGrid.EntityId || Ignored(d.Item1) || d.Item1 == Me.CubeGrid.EntityId || _mates.ContainsKey(d.Item1) || !FleetOk(d)) continue;   // (a feed can name my own grid or a wingmate)
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
double GhostTime = 8.0;             // how long a lost target is kept and searched for
double AiStaleTakeover = 3.0;       // lock stale for this long: let the AI blocks hold the enemy (if there are any)
double FireStaleMax = 2.0;    // never fire at a track older than this (extrapolation is a guess by then)
int _missRun = 0, _spiralIdx = 0, _staleRay = 0;
bool Search2nd = true;   // when two cameras can cast in the same tick at a stale lock, the second ray goes to the search ellipse
int SearchTenths = 1, SearchTenthsLate = 3;   // of 10 rays at a stale (> 0.6 s) lock, how many go to the widening search ellipse instead of the predicted point
double _lastStaleShare = -9;
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
    double sr = Big ? 1800 : ScanRange;   // a warship looks 1.8 km out through every camera it has (a ray costs its length in charge; a capital ship's turrets reach only 800 m)
    foreach (var c in _cams)
        if (c.IsWorking && (Big || Forwardish(c)) && c.AvailableScanRange >= sr && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    if (cam == null) return;
    _scanIdx++;
    // just lost a target: every other ray goes round where it was last seen, widening with time
    double lostAge = _now - _lostT;
    if (lostAge < 25 && (_scanIdx & 1) == 0)
    {
        Vector3D lp = _lostPos + _lostVel * Math.Min(lostAge, 10);
        double rr = Math.Min(250, _lostRad + 6 * lostAge) * Math.Sqrt(((_scanIdx >> 1) % 23 + 0.5) / 23.0), th2 = _scanIdx * 2.39996323;
        Vector3D dl = lp - cam.GetPosition(); double dd = dl.Length();
        if (dd > 30 && dd < sr && Vector3D.Dot(dl / dd, cam.WorldMatrix.Forward) > 0.75)
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
    var e = cam.Raycast(sr, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
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

    if (!Man && (_mode == Mode.IDLE || _mode == Mode.RTB)) return;
    if (_tgt != null && _tgt.Src == "LOCK" && (_mode == Mode.ASSIST || Man)) return; // pilot's choice sticks

    Vector3D me = _rc.GetPosition();
    Vector3D fwd = _rc.WorldMatrix.Forward;
    Vector3D myVel = _rc.GetShipVelocities().LinearVelocity;
    Vector3D home = Home();
    Track best = null; double bestScore = double.MaxValue;
    foreach (var t in _contacts.Values)
    {
        Vector3D p = t.P(_now);
        if (Gd && !Man && Vector3D.Distance(p, home) > GuardRadius && !KzLive(t.Id)) continue;
        Vector3D d = p - me; double dist = d.Length();
        double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(d / Math.Max(dist, 1), fwd), -1, 1)) * 57.2958;
        double closing = Vector3D.Dot(t.V(_now) - myVel, -d / Math.Max(dist, 1));
        double score = dist + ang * 6 - closing * 3;
        if (t.Type == MyDetectedEntityType.SmallGrid) score -= 150;   // small = more likely drones/fighters shooting at us
        if (t == _tgt) score -= Stickiness;                            // stickiness
        score += Math.Max(0, (_now - t.Seen) - 1.0) * 150;             // stale tracks lose to fresh sightings
        if (dist < 150) score -= 350;                                  // something right on top of us comes first
        score += HiveScore(t) + KzScore(t) + BatHope(t) + BatKz(t, dist) + HdScore(t);
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

    if (!Man && _mode != Mode.IDLE && _mode != Mode.RTB && _mode != Mode.ASSIST)
    {
        if (cap > 0 && _batFrac < RtbBattery) { _err = "Low battery - RTB"; _mode = Mode.RTB; }
        else if (_guns.Count > 0 && (working == 0 || (_hadAmmo && vol <= 0))) { _err = "No guns/ammo - RTB"; _mode = Mode.RTB; }
    }
}

// =====================================================================
//  CONTROL
void Control(double dt)
{
    var vels = _rc.GetShipVelocities();
    Vector3D myVel = vels.LinearVelocity;
    Vector3D grav = _rc.GetNaturalGravity();
    Vector3D me = _rc.GetPosition();
    bool shoot = false;
    _aimErrDeg = 0; _fireWinDeg = 0;

    if (_mode == Mode.IDLE) { SetFire(false); BatFire(false, myVel); return; }

    // ---------- CAPITAL FLEE ----------
    // A capital ship (radius >= 50 m) has turrets that reach 800 m by themselves, and a slow ship cannot turn its guns onto one that is behind it (180 degrees take ~18 s).
    // So: it keeps a longer standoff (Capital Standoff Range), and when such a ship is in the rear half inside that range the nose is pointed AWAY from it and the
    // strongest thrusters burn until the range is open (or Flee Time ends), then the ship turns round and fights. Fixed guns are silent meanwhile, the turrets are not.
    Vector3D toT = Vector3D.Zero; double so = Standoff, tDist = 1;
    if (_tgt != null && _mode != Mode.RTB)
    {
        toT = _tgt.P(_now) - me; tDist = Math.Max(toT.Length(), 1);
        bool capital = Big && CapRange > 0 && _tgt.Radius >= 50;   // (Capital Standoff Range=0 switches the whole thing off)
        if (capital) so = Math.Max(so, CapRange);
        double cosA = Vector3D.Dot(toT / tDist, _rc.WorldMatrix.Forward);
        if (!_flee && capital && tDist < so && cosA < -0.35 && _now - _fleeT > FleeMax + 40) { _flee = true; _fleeT = _now; }
        if (_flee && (!capital || tDist > so + 100 || cosA > 0.2 || _now - _fleeT > FleeMax)) _flee = false;
    }
    else _flee = false;

    // ---------- aiming ----------
    Vector3D aimDir = Vector3D.Zero, aimPoint = Vector3D.Zero;
    bool haveAim = false;
    if (_flee) { Aim(-toT / tDist, Vector3D.Zero, Vector3D.Zero, dt); _havePrev = false; }
    else if (_tgt != null && _mode != Mode.RTB)   // (no guns = scout: still tracks with the nose cameras)
    {
        Vector3D gunPos = Vector3D.Zero; int n = 0;
        foreach (var g in PrimGuns()) if (g.IsFunctional) { gunPos += g.GetPosition(); n++; }
        gunPos = n > 0 ? gunPos / n : me;
        haveAim = Lead(_tgt, gunPos, myVel, out aimPoint, out aimDir);
        if (haveAim)
        {
            Vector3D losRate = Vector3D.Zero;
            if (_havePrev) losRate = Vector3D.Cross(_prevAim, aimDir) / dt;
            if (losRate.LengthSquared() > 4) losRate = Vector3D.Normalize(losRate) * 2;
            _prevAim = aimDir; _havePrev = true;
            Vector3D up = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : HdUp();
            _tvD = (aimPoint - _rc.GetPosition()).Length();
            Aim(aimDir, up, losRate, dt);

            Vector3D gunFwd = _aimRef.WorldMatrix.Forward;
            double dist = Vector3D.Distance(aimPoint, gunPos);
            _aimErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(gunFwd, aimDir), -1, 1)) * 57.2958;
            _fireWinDeg = BatWin(dist);   // (aiming at a hull point needs a tighter window)
            shoot = dist < MaxFireRange && _aimErrDeg < _fireWinDeg && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax && BatOk(_bat[_prim], dist);
            if (shoot && FriendCheck) { MatesInLine(gunPos, gunFwd, dist); if (_tick % 20 == 0) CheckLineOfFire(gunPos, gunFwd, dist); }
            if (_now < _holdFireUntil) shoot = false;
        }
    }
    else _havePrev = false;
    if (haveAim) _sweepUpSet = false;

    if (!haveAim && !_flee)
    {
        if (_mode == Mode.ASSIST) { ReleaseGyros(); }
        else
        {
            Vector3D up = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : HdUp();
            Vector3D fwd = _rc.WorldMatrix.Forward;
            Vector3D toHome = Home() - me;
            if (_mode == Mode.RTB && toHome.LengthSquared() > 400) fwd = Vector3D.Normalize(toHome);
            else if (myVel.LengthSquared() > 100 && !Gd && !(_now - _hitDirT < 10 && AutoScan)) fwd = Vector3D.Normalize(myVel);
            else
            {
                // radar sweep: yaw round and nod up/down so the camera cone covers all around
                if (AutoScan && !Calm) fwd = SweepDir(up);
            }
            Aim(fwd, up, Vector3D.Zero, dt);
        }
    }
    SetFire(shoot); BatFire(haveAim, myVel);

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
        Vector3D sep = Separation(me);
        if (_mode != Mode.RTB && _tgt == null && sep.LengthSquared() > 1 && _now - _assistT >= 4) _home += sep * dt;   // idle: the wing spreads out (the home point moves away from the mates, or it would pull the ship back to its paste spot)
        want = (dist > 0.5 ? d / dist * v : Vector3D.Zero) + Evade() + sep;
        if (_mode == Mode.RTB && dist < 15 && myVel.Length() < 2) { SetMode(Mode.IDLE); _err = "Home - idle"; return; }
    }
    else
    {
        Vector3D tp = _tgt.P(_now), tv = HdHar(_tgt.Id) ? Vector3D.Zero : _tgt.V(_now);
        Vector3D d = tp - me; double dist = d.Length();
        Vector3D dir = dist > 1 ? d / dist : _rc.WorldMatrix.Forward;
        double e = dist - so - (_flee ? 100 : 0);
        double ab = AccelDir(e > 0 ? -dir : dir, mass) * 0.55;   // braking comes from the thrusters on the far side of the way it moves
        double vr = Math.Sign(e) * Math.Min(Math.Min(Math.Abs(e) * 0.6, MaxSpeed), Math.Sqrt(2 * ab * Math.Abs(e)));
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
        double orbit = !_flee && dist < Math.Max(MaxFireRange, Standoff) * 1.2 ? OrbitSpeed : 0;
        want = tv + dir * vr + tan * orbit * _orbitSign + (Jink && !_flee && !HdJ() ? _jink : Vector3D.Zero) + Evade() + Separation(me);
        if (Gd && Vector3D.Distance(me, home) > GuardRadius * 1.3) want = Vector3D.Normalize(home - me) * CruiseSpeed;
    }
    double cap = _tgt != null && _mode != Mode.RTB ? MaxSpeed : CruiseSpeed;
    want = HdWant(want, cap);   // HARASSER LINK: the Spotter's dodge bias (corner jink across the harasser's line of sight)
    want = KzWant(want, me, cap);   // KAMIKAZE DEFENCE: back away / break sideways
    want = FaWant(want, me);   // FRIENDLY AVOIDANCE: keep clear of non-enemy grids (wingmates, carriers, other ships)
    if (want.LengthSquared() > cap * cap) want = Vector3D.Normalize(want) * cap;

    Vector3D acc = (want - myVel) * 2.0;
    if (acc.LengthSquared() > 2500) acc = Vector3D.Normalize(acc) * 50;
    ApplyThrust((acc - grav) * mass);
}

// Gun intercept (v2.4): constant-velocity solution, then refined with the LEAD block's prediction (fresh acceleration held for
// the expected rest of the target's manoeuvre, aim point across the line of fire with a hull margin for the predicted miss).
Vector3D _aimLos; double _aimNeed;   // line of fire and required hull margin for AimOffset's choice
double LeadMargin = 1.0;             // aim point keeps this many predicted misses of known hull around it
bool Lead(Track t, Vector3D shooter, Vector3D shooterVel, out Vector3D point, out Vector3D dir)
{
    if (_rkt) return BatRkt(t, shooter, shooterVel, out point, out dir);   // BATTERY: the rocket class has its own flight model
    Vector3D c = t.P(_now), tv = t.V(_now), ta = UseTargetAccel ? LeadAcc(t) : Vector3D.Zero;
    Vector3D rp0 = c - shooter, rv = InheritVel ? tv - shooterVel : tv;
    double s = ProjSpeed, rl = rp0.Length(), h = LeadHold(t);
    Vector3D los = rl > 1 ? rp0 / rl : Vector3D.Zero;
    _aimLos = los; _aimNeed = LeadMargin * LeadMiss(t, rl / s);
    Vector3D off = AimOffset(t), tp = c + LeadOffset(t, off, los, rl / s), rp = tp - shooter;
    // exact solution for constant velocity, then refine with acceleration
    double a = rv.LengthSquared() - s * s, b = 2 * Vector3D.Dot(rp, rv), cc = rp.LengthSquared();
    double tt;
    if (Math.Abs(a) < 1e-6) tt = b != 0 ? -cc / b : 0;
    else
    {
        double disc = b * b - 4 * a * cc;
        if (disc < 0) { point = tp; dir = Vector3D.Normalize(rp); _leadT = 0; return rp.LengthSquared() > 1; }
        double sq = Math.Sqrt(disc);
        double t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a);
        tt = (t1 > 0 && (t1 < t2 || t2 <= 0)) ? t1 : t2;
    }
    if (tt <= 0 || double.IsNaN(tt)) tt = rp.Length() / s;
    Vector3D p = rp;
    for (int i = 0; i < 4; i++)
    {
        p = rp0 + LeadOffset(t, off, los, tt) + (rv * tt + ta * LeadK(tt, h)) * LeadScale;
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
double MateClear = 14;   // hold fire while a hive mate is this close to the line of fire
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
    TvAim(ref desiredFwd, ref desiredUp, ref ffWorld);
    if (_gyros.Count == 0 || desiredFwd.LengthSquared() < 1e-9) return;
    Vector3D wWorld = AimW(desiredFwd, desiredUp, ffWorld, dt);
    double cap = ManCap();   // MaxTurn, ramped up from the present rate for 2 s after the pilot leaves
    if (wWorld.LengthSquared() > cap * cap) wWorld = Vector3D.Normalize(wWorld) * cap;
    BatTurn(wWorld);
    GyroRate(wWorld);
}

// PD rate (world frame, rad/s) that turns the aim reference onto desiredFwd (and rolls to desiredUp), plus a feed-forward; also used by the MANUAL fly-by-wire assist
Vector3D AimW(Vector3D desiredFwd, Vector3D desiredUp, Vector3D ffWorld, double dt)
{
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
    return Vector3D.TransformNormal(w, refM) + ffWorld;
}

void GyroRate(Vector3D wWorld)
{
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

// components along the six thruster groups (Forward, Backward, Left, Right, Up, Down of the controller)
double[] Comp(Vector3D f) { MatrixD m = _rc.WorldMatrix; double x = Vector3D.Dot(f, m.Forward), r = Vector3D.Dot(f, m.Right), u = Vector3D.Dot(f, m.Up); return new[] { x, -x, -r, r, u, -u }; }
// acceleration the working thrusters can give along the world direction u (the weakest group that u needs, scaled by its share of u)
double AccelDir(Vector3D u, double mass)
{
    double[] c = Comp(u); double a = 1e9;
    for (int i = 0; i < 6; i++)
    {
        if (c[i] < 0.1) continue;
        double f = 0; foreach (var t in _thrDir[i]) if (t.IsWorking) f += t.MaxEffectiveThrust;
        a = Math.Min(a, f * _powerScale / mass / c[i]);
    }
    return a > 1e8 ? 1 : a;
}

void ApplyThrust(Vector3D forceWorld)
{
    _tvN = forceWorld;
    _rc.DampenersOverride = false;
    double[] comp = Comp(forceWorld);
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
    if (_bat[_prim].Cool > 0) { _firing = on; BatShoot(_bat[_prim], on); return; }   // single-shot class (BATTERY)
    if (on == _firing && !(on && _tick % 30 == 0)) return;
    _firing = on;
    foreach (var g in PrimGuns()) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off");
}

void ReleaseGyros() { foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = g.Yaw = g.Roll = 0; } _havePrev = false; }

void Release()
{
    ReleaseGyros();
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
    SetFire(false); BatFire(false, Vector3D.Zero);
}

// =====================================================================
//  DISPLAY
void Display()
{
    _sb.Clear();
    _sb.Append("SHRIKE AI v").Append(VER).Append('\n').Append(ManHud()).Append(BatStatus());
    _sb.Append("Ins ").Append(Runtime.CurrentInstructionCount).Append('/').Append(Runtime.MaxInstructionCount).Append('\n');
    _sb.Append(UnderFire ? "UNDER FIRE\n" : "").Append(AiStatus()).Append(_crash != "" ? "! Script error: " + _crash + "\n" : ""); _sb.Append(KzStatus()).Append(HdStatus());
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    ManShow();
    Echo(s);
}

// =====================================================================
//  IDLE SWEEP (the Hornet's 50 m sensor bubble is not carried: turrets, cameras and the AI blocks see for a warship; control_arch M3 size cut)
Vector3D _sweepUp; bool _sweepUpSet = false;

// Idle search attitude: keep yawing round while nodding the nose up and down (+/-34 deg, 16 s period),
// so the camera scan cone covers above and below as well as the horizon.
Vector3D SweepDir(Vector3D gravUp)
{
    if (_now - _hitDirT < 10)
    {   // just hit: circle the nose cameras 15-45 deg round the side that was hit, where the shooter is
        Vector3D a = Vector3D.Cross(_hitDir, Math.Abs(_hitDir.Y) < 0.9 ? Vector3D.Up : Vector3D.Right); a.Normalize();
        Vector3D b = Vector3D.Cross(_hitDir, a);
        double ph = _now * 1.3;
        return Vector3D.Normalize(_hitDir + (a * Math.Cos(ph) + b * Math.Sin(ph)) * 0.5);
    }
    if (!_sweepUpSet) { _sweepUp = _rc.WorldMatrix.Up; _sweepUpSet = true; }
    Vector3D upRef = gravUp.LengthSquared() > 0.5 ? gravUp : _sweepUp;
    Vector3D h = _rc.WorldMatrix.Forward;
    h -= Vector3D.Dot(h, upRef) * upRef;
    if (h.LengthSquared() < 1e-4) h = Vector3D.Cross(upRef, _rc.WorldMatrix.Right);
    h.Normalize();
    h = Vector3D.Transform(h, MatrixD.CreateFromAxisAngle(upRef, Big ? -0.5 * MaxTurn / AimKp : -0.035));   // (a big ship turns steadily at half its turn rate and nods slowly: the fast nod tipped it back and forth)
    double pitch = Big ? 0.35 * Math.Sin(_now * 0.05) : 0.6 * Math.Sin(_now * 0.4);
    return Vector3D.Normalize(h * Math.Cos(pitch) + upRef * Math.Sin(pitch));
}

// =====================================================================
//  CAMERA HELPERS (the close-range all-around scan of the Hornet is not carried: a warship has turrets, sensors and AI blocks for that; control_arch M3)
bool Forwardish(IMyCameraBlock c) { return Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) > 0.9; }

// =====================================================================
//  v1.8: TARGET STATUS, DAMAGE SENSING, SPLIT FOCUS, ASSIST
Dictionary<long, double> _ignore = new Dictionary<long, double>();
Dictionary<long, int> _share = new Dictionary<long, int>(), _count = new Dictionary<long, int>();
List<Track> _live = new List<Track>();
Vector3D _hitDir; double _hitDirT = -99;   // damage sensing runs in the Staff PB (".DMG"); HlkTick sets these
double _hitAt = -99; int _hits = 0;
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

// Random evasive velocity while under fire (new direction every 1-2.5 s and on every new hit).
Vector3D Evade()
{
    if (!UnderFire || Big) return Vector3D.Zero;
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
    FaBeacon();
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


// >>> BREACH v1.0-lite (the BREACH half of tools/aim_block.cs without SEEKER: Shrike and the Helm scripts; kept in sync by tools/blocks_sync.py) >>>
// BREACH (large ships): spreading hits over a big hull chips thousands of armour blocks and destroys none (sim vs a 13,666-block
// warship). BREACH holds ONE hull point and keeps every round on it: the known hull point nearest the target's centre (on a long
// ship that is mid-deck or mid-belly, where the armour over the core is thinnest). Probe rays at it that come back deeper show the
// hole; the drone then aims down the hole, towards the centre. No progress for "Breach Stall (s)", or two probe rays in a row that
// find nothing there: that point is dropped and the next one is used. AUTO uses BREACH on large grids of radius >= Breach Min
// Radius; "aim breach" forces it, "breach off" turns the AUTO switch off.
bool BreachAuto = true; double BreachMinRadius = 25, BreachStall = 30, BreachProbe = 30;
long _bId; bool _bOn; Vector3D _bS, _bDir; double _bDepth, _bProgT; int _bMiss;
List<Vector3D> _bBad = new List<Vector3D>();

bool BreachMode(Track t)
{
    if (AimMode == "BREACH") return true;
    return AimMode == "AUTO" && BreachAuto && t.Type == MyDetectedEntityType.LargeGrid && t.Radius >= BreachMinRadius;
}
bool BreachBad(Vector3D h) { foreach (var q in _bBad) if ((q - h).LengthSquared() < 36) return true; return false; }
void BreachSet(Vector3D s, Vector3D dir)
{
    _bS = s; _bDir = dir; _bOn = true; _bDepth = 0; _bProgT = _now; _bMiss = 0;
}
Vector3D BreachOffset(Track t)
{
    if (_bId != t.Id) { _bId = t.Id; _bOn = false; _bBad.Clear(); }
    if (_bOn && _now - _bProgT > BreachStall) BreachDrop();
    if (!_bOn)
    {
        double best = double.MaxValue; Vector3D s = t.Hull[0];
        foreach (var h in t.Hull) { if (BreachBad(h)) continue; double d = h.LengthSquared(); if (d < best) { best = d; s = h; } }
        if (best == double.MaxValue) _bBad.Clear();
        double l = s.Length();
        BreachSet(s, l > 1e-3 ? -s / l : Vector3D.Zero);
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
    _bOn = false; _bMiss = 0;
}
string AimInfo()
{
    string s = "";
    if (_bOn && _tgt != null && _bId == _tgt.Id && BreachMode(_tgt)) s += " hole " + _bDepth.ToString("0") + " m";
    return s;
}
// <<< BREACH v1.0-lite <<<

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
    if (!Own(b)) return false;
    if (b.CustomName.Contains("[AI]")) return true;
    string d = b.BlockDefinition.ToString();
    return d.Contains("OffensiveCombat") || d.Contains("FlightMovement");
}
void FindAi()
{
    GridTerminalSystem.GetBlocksOfType(_ai, b => IsAiBlock(b));
    foreach (var b in _ai) b.Enabled = _aiOn;   // never let them fight the script
    if (AiAssist && AiSearchEvery > 0) foreach (var b in _ai) { var oc = b as IMyOffensiveCombatBlock; if (oc != null) oc.UpdateTargetInterval = AiSearchEvery; }   // the block rescans every 30 s by default: a 6 s AI window would often see no search at all
    if (AiAssist) AiGateFind(_ai);   // (AI Assist off: the AI blocks stay as they came, "AI Behavior" off - switching it on lets the AI Flight block fly the ship away on its own)
}
bool AiStep()
{
    if (_tgt != null) { _lastTgtT = _now; _lastTgtDist = Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()); }
    // The AI also takes over while my lock is stale (nothing seen for AiStaleTakeover s): it keeps the nose on the enemy
    // and the confirm rays below pick it up again, which hands control straight back to the script.
    bool want = AiAssist && _ai.Count > 0 && AiModeOk() &&
        ((_tgt == null && _now - _lastTgtT > 2) || (_tgt != null && _now - _tgt.Seen > AiStaleTakeover));
    want = AiGate(want);   // AI GATE: the AI blocks may do nothing (AI Behavior off): time-share with the script's own search
    if (want && FaYield()) want = false;   // FRIENDLY AVOIDANCE: the AI Flight block does not dodge friends; the script flies while one is close or in the way
    if (want != _aiOn)
    {
        _aiOn = want;
        _firing = false;                        // guns' state is unknown after any hand-over: SetFire sends it again
        if (want) { Release(); _aiSince = _now; }   // thrusters and gyros go to the AI
        _aiKick = false;
        foreach (var b in _ai) b.Enabled = want;
    }
    if (!_aiOn) return false;
    if (!_aiKick) foreach (var b in _ai) if (!b.Enabled) b.Enabled = true;   // somebody else (the Staff's dead-man release) switched them off while I believe they fly the ship
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
// (the Hornet's SEARCH PATROL for HUNT is not carried: a warship is sent to its targets by the Staff / the pilot; control_arch M3 size cut)

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
    return (_fc != "" ? "1st contact " + _fc + " " + (_now - _fcT).ToString("0") + " s ago\n" : "") + AiGateStatus() + "AI: " + (_aiOn ? "searching" : _agT0 >= 0 ? "own search" : "standby") + " (" + _ai.Count + " blocks)" + (OwnAlert ? "  ALARM" : "") + "\n";
}
// The AI Flight block flies straight at what it found, and so do all the wingmates' AI blocks: three ships at one target meet in one spot. While a friendly grid is
// in the ramp zone or on the straight line of my velocity (FaWant on a zero command pushes), the AI is switched off for 3 s and Control() flies with FaWant.
double _faYT = -99;
bool FaYield()
{
    if (FaWant(Vector3D.Zero, _rc.GetPosition()).LengthSquared() > 25) _faYT = _now;
    return _now - _faYT < 3;
}
bool AiModeOk() { return _mode == Mode.HUNT || Gd && !Calm; }
// REACT: weapons, tracking and turrets live, but the ship does not sweep, scan or hand itself to the AI blocks until something wakes it:
// an AI block alarm ("aialert" from an AI Defensive block, or a mate's alarm), damage taken, or a target already held.
bool Big { get { return Me.CubeGrid.GridSizeEnum == MyCubeSize.Large; } }   // a large-grid ship: slow to turn, huge, many turrets
bool Gd { get { return _mode == Mode.GUARD || _mode == Mode.REACT; } }
bool Calm { get { return _mode == Mode.REACT && _tgt == null && !Alerted && !UnderFire && _now - _hitDirT >= 10; } }

// =====================================================================
//  v1.8: PRINTER SUPPORT
//  * A drone printed while merged to a printer shares the printer's grid. Blocks are re-scanned
//    whenever this PB's grid changes (merged / released), so it never drives the printer's thrusters.
//  * The take-off after the release (v2.2) is the LAUNCH block above (docs/launch-protocol.md): it no longer waits for
//    one "<Tag>_JOIN" message; JOIN, FLEET "join", the argument "armed" and [Launch] Armed are only extra triggers.
IMyCubeGrid _myGrid;
void CheckGrid() { if (Me.CubeGrid != _myGrid) { _myGrid = Me.CubeGrid; GetBlocks(); } }
