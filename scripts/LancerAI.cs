// =====================================================================
//  LANCER DRONE AI  v2.5-lancer  - autocannon burst fire control & flight
//  LANCER DRONE AI  v2.3-lancer  - autocannon burst fire control & flight
//  For Space Engineers (vanilla, small grid). Paste into a Programmable Block.
//  Derived from HORNET DRONE AI v1.8: same tracking, hive (IGC), AI hand-off,
//  damage sensing, printer join; the weapon model and fire control are new.
// ---------------------------------------------------------------------
//  WHAT CHANGED FOR AUTOCANNONS (SmallBlockAutocannon: 150 rpm, 500 damage,
//  800 m/s, 800 m, 16-round clips, 3 clips = 48 shells per cannon)
//   * Few, heavy, straight-flying shells: the lead solver now uses the real
//     800 m/s (the old 400 m/s was wrong) and shells DO inherit our velocity.
//   * Burst discipline: Shoot_On only while the solution is inside a tight
//     window, for at most BurstSeconds, then BurstPause to re-aim/re-check.
//     A hit is worth 5x a gatling bullet, so a shell is never wasted on a
//     marginal shot ("target jinking: miss N m" on the screen).
//   * Longer standoff (420 m: 0.5 s of flight, below the 800 m round limit).
//   * Ammo: shells left are estimated from inventory volume (24 L per
//     16-round clip); below ConserveBelow shells only close, tight shots.
//
//  v2.1 (sim audit 1; details in docs/script-fix-notes.md):
//   * AI hand-over: confirm rays down the boresight + a spiral (a ship is only 0.2-0.8 deg wide), one long (2600 m) and short rays
//   * lock upkeep: cheaper rays at known hull points, ghost target 8 s (acceleration trusted 1 s), widening search ellipse, loss sharing
//   * hull-gap fix: a ray that misses the predicted centre is retried at the centre before the search widens
//   * debris / one-block wrecks count as finished (they split off as their own grid and held the guns forever)
//   * stale tracks are not fired on (Max Track Age To Fire), wingmate separation, evasive jink 35-40 m/s every 1-2 s
//   * target feeds from other fleets ("Listen To IGC Tags")
//   * bursts 2.0 s / pause 0.4 s (was 1.2 / 0.8)
//
//  v2.3: automatic take-off (LAUNCH block, docs/launch-protocol.md): the drone leaves a printer by itself when the merge pair opens;
//   _JOIN / FLEET join / Armed in Custom Data are only extra triggers. Old ReadJoin/Docked removed.
//  v2.2 (round 2, "Round 2: wasp-lancer" in docs/script-fix-notes.md):
//   * HUNT search patrol (square of 1.2 km round home, AI listening stops at the corners) and AI re-pick, from the Harrier v2.2 (R2-R4 there):
//     with nothing found for 15 s the drone no longer waits at home (sim, 1v1 default starts 1.5-3 km: wins 49 -> 61 %, timeouts 28 -> 10 %)
//   * every drone patrols in its own direction and radius (from its grid id): identical scripts no longer fly the same square in lock-step
//   * confirm rays: both forward cameras cast the long ray, rays shorten to the last fight range, the 2nd camera never repeats a point
//   * config keys that contain "=" were renamed (MyIni splits there): "Lead Scale (1: as calculated)", "Split Targets Across Hive (false: focus fire)"
//
//  v2.5 (LEAD v1.0, shared block, docs/lead-v1.md): the lead solver predicts the target with its freshest acceleration, held only
//   for as long as its manoeuvres usually last (an AI ship on evasive flips its strafe every 2-5 s); the hull aim point counts only
//   across the line of fire, turns with the target's spin, and keeps "Aim Margin" x the predicted miss of known hull around it
//
//  COMMANDS (PB argument, toolbar, or IGC "<Tag>_CMD")
//   guard    hold home point, engage anything inside GuardRadius
//   ready    like guard, but holds THIS spot (the carrier link's HOME is ignored), scans all round and fires when something comes; moves only to fight, then returns (Start Mode READY works too)
//   sentry   hold THIS spot, still, until a target comes within Sentry Range (2,500 m = the AI Offensive block's search range), then hunt it
//            down with no guard-radius leash; with nothing left, fly back to the spot and wait again (Start Mode SENTRY works too)
//   follow   stay with the owner: a ship running a Mothership link, or you on foot near the drone ("escort" works too)
//   satellite (sat, orbit)   FOLLOW, but the drones spread round a slowly turning vertical ring centred on the owner (ring size grows with their number); [Follow] section
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
//  KAMIKAZE DEFENCE (v2.6-lancer; shared block tools/kz_block.cs, docs/kamikaze-defence.md): incoming swarms of small warhead drones are
//   never written off as wrecks, become the priority target, and the drone backs away while firing and breaks sideways just
//   before impact; spare camera rays track the swarm and the tracks are shared with the hive. Own [Kamikaze Defence] Custom Data section.
// =====================================================================

const string VER = "2.6-lancer+launch";

// ---------------- settings (overwritten from Custom Data) -------------
string Tag = "LANCER";
double ProjSpeed = 800;      // m/s  autocannon shell (Ammos.sbc AutocannonShell)
bool InheritVel = true;      // bullets/shells inherit the shooter's velocity
double MaxFireRange = 750;   // shells vanish at 800 m
double StandoffHuge = 700, HugeTargetRadius = 38;   // against huge grids (bounding radius over 38 m, e.g. the Tech Hunter; the sim Pirate is under it) stay at the edge of the gun range (charge test 2026-10-14, docs/sim-pending-2026-10-14.md: lone drone vs a 100 m/s charge, 700 m lost 62-65 % of blocks instead of about 80 %, dealt 15-25 % more)
double Standoff = 420;       // preferred engage distance
double MinRange = 100;       // never closer than this
double BurstSeconds = 2.0, BurstPause = 0.4; // 150 rpm: ~5 shells per cannon per burst (sim sweep: 1.2/0.8 -> 2.0/0.4 raised damage dealt 3.9k -> 4.5k)
int ConserveBelow = 12; double ConserveRange = 500; // with this few shells left: close, tight shots only
double GuardRadius = 1500;   // GUARD only engages within this of home
double MaxSpeed = 98;        // combat speed cap
double CruiseSpeed = 60;     // non-combat cap (saves power)
double OrbitSpeed = 40;      // tangential speed around the target (was 22)
double JinkSpeed = 35;       // random evasive velocity (was 12; lock-duel sweep: win 43 -> 49 %, damage taken -25 %)
double LockRange = 2500;     // manual "lock" raycast distance
double CloseScanRange = 120; // all-around camera scan distance
bool CloseScan = true;
double ScanRange = 1200;     // auto-scan raycast distance
double ScanCone = 35;        // auto-scan half-angle (deg)
double AimKp = 6, AimKd = 0.35, MaxTurn = 5; // gyro tuning (rad/s)
double FireTol = 1.0;        // multiplier on silhouette fire window
double LeadScale = 1.0;      // scales the lead offset if shots land consistently ahead/behind
bool UseTargetAccel = true;
bool SplitFocus = true;
double DisabledGrace = 5, EvadeSpeed = 20;
bool AiAssist = true; double AiConfirmRange = 2600;   // confirm rays are this long (1.5 casts/s per camera at 2 km/s charge)
double AiConfirmLong = 2600;   // the first forward camera casts this long instead (0.77 casts/s)
double RtbBattery = 0.15;    // fraction
double SepRange = 70, SepSpeed = 25;   // wingmate separation: push away from hive mates closer than this (m) at up to this speed (m/s)
bool TargetLarge = true, TargetSmall = true, TargetChars = false, TargetNeutral = false;
bool AutoScan = true, ShareTargets = true, Jink = true, FriendCheck = true;
string StartMode = "SENTRY";   // default: hold the spot, wake on a target within Sentry Range (stands down to IDLE while docked)

// ---------------- state ----------------------------------------------
enum Mode { IDLE, GUARD, HUNT, ASSIST, RTB, FOLLOW }
Mode _mode = Mode.IDLE;
MyIni _ini = new MyIni();
double _now = 0;
long _tick = 0;
Vector3D _home;
bool _homeSet = false, _ready = false, _sentry = false;
double SentryRange = 2500, _sentryAt = -1;   // _sentryAt: Start Mode SENTRY waits IDLE until this time (a printer JOIN or arming can still launch it)
// SENTRY: wake on a target this close (the AI Offensive block's search range). CustomData "Sentry Range (m)"
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
IMyBroadcastListener _lTgt, _lCmd, _lHome;


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
Vector3D _jink;
int _scanIdx = 0;
double _sweepYaw = 0;
string _err = "";
double _aimErrDeg = 0, _fireWinDeg = 0, _leadT = 0;
double _avgMs = 0; int _peakIns = 0;   // instruction counter: PB limit is Runtime.MaxInstructionCount per run
StringBuilder _sb = new StringBuilder();


// >>> AI GATE v1.3 (shared block core: identical in every drone script that uses AI-block search, synced by tools/ai_gate_sync.py) >>>
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
// v1.3 (2026-10-18, docs/design/flight_split.md): the core keeps only the time-share (AiGate) and its runtime fields. The one-time AI-block setup
// (AiGateFind, AiGateStatus, AiBehaviorSwitch/AiTyped/AiSpeed/AiSearchEvery) is AI GATE SETUP (tools/ags_block.cs), which may run in another PB
// (the Aux, or StaffAI for a Helm); the [AI Gate] section reader AiGateLoad is AI GATE CONFIG (tools/agc_block.cs), or the values come from the Staff
// through CONFIG LINK's AgSet4. AiGateOn/AiWin/ScriptWin/AiHoldMax keep their defaults here; whoever loads the config overwrites them.
// Hooks: AiGateLoad() (AI GATE CONFIG) after FleetLoad(true); AiGateFind(_ai) at the end of FindAi() and AiGateStatus() in the status text (AI GATE SETUP
// hosts); want = AiGate(want) right before "if (want != _aiOn)" in AiStep(). Uses from the script: _rc, _now, UnderFire.
bool AiGateOn = true, _agAi = true, _agLive = false, _agFwdSet = false;
double AiWin = 6, ScriptWin = 8, AiHoldMax = 60, _agT0 = -1, _agMoveT = -99, _agMinV = 0; Vector3D _agFwd;

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
// <<< AI GATE <<<

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

// >>> AI GATE CONFIG v1.0 (sub-block of AI GATE v1.3: the [AI Gate] section, read locally; tools/agc_block.cs, synced by tools/blocks_sync.py) >>>
// Hosts that read their own Custom Data (WaspAI, TubeRamAI). Writes the 8 defaults on first run ("reload" re-reads them) into the AI GATE core fields
// (AiGateOn, AiWin, ScriptWin, AiHoldMax) and the AI GATE SETUP fields (AiBehaviorSwitch, AiTyped, AiSpeed, AiSearchEvery): a host needs both blocks.
// A split Flight / Helm gets these values from its Staff (CONFIG LINK AgSet4 for the core four; the setup four go to the Aux in the SDK cfg).
// Hook: AiGateLoad() after FleetLoad(true).
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
// <<< AI GATE CONFIG v1.0 <<<

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

// >>> KAMIKAZE DEFENCE v1.1 (shared block core: identical in every combat drone script, synced by tools/kz_sync.py, see docs/kamikaze-defence.md) >>>
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
// v1.1 (2026-10-18, docs/design/flight_split.md): config and camera rays live in sub-blocks so a split Flight can drop them. The [Kamikaze Defence]
// section (KzLoad/KzD/KzB) and KzStatus() are in KZ CONFIG (tools/kzc_block.cs) or come from the Staff through CONFIG LINK's KzSet; KzRayStep() is
// KZ RAYS (tools/kzr_block.cs, local rays) or the SEARCH LINK proxy (tools/sq_block.cs, rays cast by the Aux PB). This core needs exactly one KzRayStep().
// Fields KzOn..KzReserve keep their defaults here; whoever loads the config (KzLoad or KzSet) overwrites them.
// Script hooks: KzLoad() (KZ CONFIG) or KzSet(...) (CONFIG LINK) next to every FleetLoad(true); KzAssess() first in the target-selection pass (PruneAndSelect / Housekeeping);
// score += KzScore(t) next to HiveScore(t); "&& !KzLive(t.Id)" on the GUARD/FORM radius skip; "if (KzLive(t.Id)) return false;" after
// the DeadAt line of Disabled(), followed by "if (KzSpent(t)) return true;"; want = KzWant(want, me, cap) right before the speed cap; _sb.Append(KzStatus()) after HiveStatus() (KZ CONFIG hosts).
// Uses from the script: _contacts, _mates, _tgt, _now, _rc, Tag, Track (Id, Pos, Vel, AccMag, Radius, Seen, Src, Type, P(), V()),
// Mate (Pos, Vel, Seen), FleetSend(), KzRayStep().
bool KzOn = true, KzBack = true, KzBreak = true, KzRays = true, KzShareOn = true, KzMates = true;
double KzMaxRadius = 3.5, KzHorizon = 12, KzMiss = 40, KzMissPerKm = 60, KzMinClosing = 15, KzPriority = 2500;
double KzBackSpeed = 60, KzBackRange = 800, KzReact = 10, KzBreakTime = 2, KzBreakSpeed = 40, KzBreakMiss = 20, KzPack = 60, KzReserve = 600;
class KzThreat { public Track T; public double Tgo, Miss, Dist, Urg; public bool Mine; }
List<KzThreat> _kz = new List<KzThreat>();
Dictionary<long, double> _kzT = new Dictionary<long, double>();     // id -> last time it was classed as incoming (latch)
Dictionary<long, KzThreat> _kzById = new Dictionary<long, KzThreat>();
List<long> _kzDrop = new List<long>();
double _kzShareT = -9; int _kzRay = 0, _kzMine = 0; Vector3D _kzSide; Random _kzRng = new Random();

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
// <<< KAMIKAZE DEFENCE <<<

// >>> KZ CONFIG v1.0 (sub-block of KAMIKAZE DEFENCE v1.1: the [Kamikaze Defence] section, read locally; tools/kzc_block.cs, synced by tools/blocks_sync.py) >>>
// Hosts that read their own Custom Data (WaspAI). A split Flight / Helm gets these values from its Staff (CONFIG LINK KzSet) and does not carry this block.
// Writes the defaults of the [Kamikaze Defence] section on first run ("reload" re-reads it) into KAMIKAZE DEFENCE's fields (KzOn .. KzShareOn).
// Hooks: KzLoad() next to every FleetLoad(true); _sb.Append(KzStatus()) after HiveStatus(). Uses from the core: the Kz* fields, _kz, _kzMine.
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

string KzStatus()
{
    if (!KzOn) return "";
    if (_kz.Count == 0) return "Kamikaze: none incoming\n";
    var k = _kz[0];
    return "!! KAMIKAZE x" + _kz.Count + " (" + _kzMine + " at me)  next " + k.Tgo.ToString("0.0") + " s " + k.Dist.ToString("0") + " m" + (k.Mine ? "" : " (mate)") + "\n";
}
// <<< KZ CONFIG v1.0 <<<

// >>> KZ RAYS v1.0 (sub-block of KAMIKAZE DEFENCE v1.1: the kamikaze pack / refresh rays cast by this PB's own cameras; tools/kzr_block.cs, synced by tools/blocks_sync.py) >>>
// Hosts that cast their own camera rays (WaspAI, ShrikeAI, GnatSwarmAI under variant G2). The 8 split drone Flights get KzRayStep() from SEARCH LINK
// instead (a proxy that sends the threats on .KZR; the Aux PB casts the rays and returns hits on .SCN). Called by KzAssess() once per pass while threats exist.
// Uses from the script: _kz, _kzRay, _tgt, _now, _rc, KzPack, KzReserve, CamFor(), Valid(), AddContact().
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
// <<< KZ RAYS v1.0 <<<


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

// >>> SPLIT LINK v1.1 (shared block: identical in every Flight half of a two-PB drone, synced by tools/split_sync.py, see docs/design/wasp_split.md) >>>
// Flight side of the Flight/Staff link; the Staff PB (scripts/<Drone>StaffAI.cs) has the other end. Everything that is not on the hot path lives there:
// FLEET LINK, FOLLOW / SATELLITE geometry, LAUNCH (take-off burn), the status screen. IGC key "WSPL" + this PB's EntityId, TransmissionDistance.CurrentConstruct.
// Script hooks: LkInit() with the other listener setup; ReadIGC(); LkRead(); LkSend() in the tick; "if (!LkBurn && !AiStep()) Control(dt)"; "if (LkBurn) return;" first in Control();
// LkWantOk/_lkWant/LkFar/LkBack/LkEngage/_lkOwner/_lkOwnerP in the FOLLOW code; ReadTgt(_lkTgt); LkAmmo() = the script's ammo count (0-254, 255 unknown).
// Uses: _now, _tick, _mode, _firing, _aiOn, UnderFire, LockStale, _tgt, _aimErrDeg, _fireWinDeg, _batFrac, _gateWhy, _dynHome, _dynHomeT, MaxSpeed, ParseMode, Command.
// WEAKPOINT: .WPT (Staff -> Flight) feeds the BREACH block's _wp* slot, WpSend() carries its .WPF (Flight -> Staff); the relay itself is the Staff PB's WEAKPOINT RELAY block.
string _lkK = ""; IMyBroadcastListener _lkFol, _lkCmd, _lkTgt, _lkWpt;
Vector3D _lkWant, _lkOwnerP, _lkOwnerV; double _lkEng = 800, _lkLeash = 1200, _lkT = -99, _aBrake = 5; int _lkFl, _lkMd = -9; bool _lkOwner;
bool LkAlive { get { return _now - _lkT < 1.0; } }                       // the Staff PB talks (a FOL at least every 0.5 s)
bool LkBurn { get { return LkAlive && (_lkFl & 4) != 0; } }              // Staff flies the take-off burn: Flight lets go of the ship
bool LkWantOk { get { return LkAlive && (_lkFl & 1) != 0; } }
double LkEngage { get { return LkAlive ? _lkEng : 800; } }
void LkInit()
{
    _lkK = "WSPL" + Me.EntityId;
    _lkFol = IGC.RegisterBroadcastListener(_lkK + ".FOL"); _lkCmd = IGC.RegisterBroadcastListener(_lkK + ".S2F"); _lkTgt = IGC.RegisterBroadcastListener(_lkK + ".TGT");
    _lkWpt = IGC.RegisterBroadcastListener(_lkK + ".WPT");   // best fleet weakpoint for my target (WEAKPOINT RELAY, Staff PB) -> the _wp* slot of the BREACH block
}
void WpSend(MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> tup) { if (LkAlive) IGC.SendBroadcastMessage(_lkK + ".WPF", tup, TransmissionDistance.CurrentConstruct); }
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
    while (_lkWpt.HasPendingMessage)
    {
        var m = _lkWpt.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)) continue;
        var w = (MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data;
        if (!FleetV(w.Item2) || !FleetV(w.Item3) || !FleetV(w.Item4)) continue;
        _wpId = w.Item1; _wpS = w.Item2; _wpD = w.Item3; _wpI = w.Item4; _wpF = w.Item6; _wpT = _now;
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
int LkAmmo() { return (int)(_ammoFrac * 100); }   // ammo for the Staff screen (0-254)
string LkGate() { return ""; }

// >>> PRINTER GUARD v1.0 (a drone welded by a printer flies nothing until the merge pair lets go; tools/blocks_sync.py) >>>
// Until its merge block lets go, a drone welded from a projection is part of the PRINTER's grid (docs/audit/printers.md printers-01): every
// thruster, gyro and controller this PB finds on "its" grid or construct may be the printer's (owner, 2026-10-14: a Watcher flew the Watcher Pad).
// PgHold() is true while Me.CubeGrid holds a "[Bay" block or the Hive Mother PB (checked once a second): the caller releases its overrides
// once and returns. The tick the drone has split off, the block lists are read again so the printer's thrusters and gyros drop out.
bool _pgFused, _pgWas; double _pgT = -99;
bool PgHold()
{
    if (_now - _pgT >= 1)
    {
        _pgT = _now;
        var l = new List<IMyTerminalBlock>(); var g = Me.CubeGrid;
        GridTerminalSystem.GetBlocksOfType(l, b => b.CubeGrid == g && b != Me && (b.CustomName.Contains("[Bay") || (b is IMyProgrammableBlock && b.CustomData.Contains("[Hive Mother]"))));
        _pgFused = l.Count > 0;
    }
    if (_pgFused) { if (!_pgWas) Release(); _pgWas = true; Echo("Held: still welded to a printer"); return true; }
    if (_pgWas) { _pgWas = false; GetBlocks(); }
    return false;
}
// <<< PRINTER GUARD v1.0 <<<

// >>> PATROL LINK v1.0 (search patrol leg/waypoint from the Staff PB (.PAT), docs/design/survivability.md) >>>
// HUNT's search patrol (nothing found for a while: fly chords of a square round home with listening stops for the AI blocks) is planned by the Staff PB's
// STAFF SEARCH block; this is only the receiver. .PAT (Staff -> Flight, key _lkK, CurrentConstruct): MyTuple<Vector3D point, double speed, int flags>,
// bit0 leg active (fly to the point at that speed, AI blocks off), bit1 listening stop (hold the point, AI blocks may search). Every 30 ticks and on a change;
// 1.5 s stale = no patrol. A target, damage, IDLE or RTB end it here at once, whatever the Staff last said.
// Hooks: PlInit() after LkInit(); "if (PatrolStep()) want = false;" in AiStep (it is the old PatrolStep); _patrolLeg / _patrolWp / PatrolSpeed in Control's
// no-target branch; "_patrolStopSet ? _patrolWp" in Home(). Uses: _now, _tgt, _mode (IDLE/RTB only), UnderFire, _lkK, IGC.
IMyBroadcastListener _plL; Vector3D _patrolWp; double _plT = -99, PatrolSpeed = 60; int _plF; bool _patrolLeg, _patrolStopSet;
void PlInit() { _plL = IGC.RegisterBroadcastListener(_lkK + ".PAT"); }
bool PatrolStep()
{
    while (_plL.HasPendingMessage)
    {
        var m = _plL.AcceptMessage();
        if (!(m.Data is MyTuple<Vector3D, double, int>)) continue;
        var d = (MyTuple<Vector3D, double, int>)m.Data;
        _patrolWp = d.Item1; PatrolSpeed = Math.Max(5, d.Item2); _plF = d.Item3; _plT = _now;
    }
    bool ok = _now - _plT < 1.5 && _tgt == null && !UnderFire && _mode != Mode.IDLE && _mode != Mode.RTB;
    _patrolLeg = ok && (_plF & 1) != 0; _patrolStopSet = ok && (_plF & 2) != 0;
    return _patrolLeg;
}
// <<< PATROL LINK v1.0 <<<

// SURVIVAL LINK hooks: own max fire range; resend the AI and gun state after the Staff PB held the ship (failover)
double SvRange() { return MaxFireRange; }
void SvResync() { _aiOn = !_aiOn; _firing = false; _aiKick = false; }

// >>> SURVIVAL LINK v1.0 (evade, jinks, survival advice from the Staff PB, docs/design/survivability.md) >>>
// The Flight side of the survivability plan. Only what must react within a tick or what only the Flight PB sees lives here: the dodge on a hit, the
// lead-breaking jinks, missile sightings, per-check hit stats, the railgun power guard, and applying the Staff's advice. Every decision (damage RTB,
// limp caps, failover, threat memory, blind side, gather, missile dodge, flares) is the Staff PB's SURVIVAL STAFF block. Advice older than 0.6 s is
// ignored, so a dead Staff leaves the Flight exactly as before plus the evade, the jinks and the power guard.
// IGC, key _lkK ("WSPL" + this PB's EntityId), TransmissionDistance.CurrentConstruct:
//  .SVF Flight->Staff MyTuple<int flags, long attackerId, Vector3D hitDir, Vector3D attackerPos, Vector3D aux, Vector3D home>: flags bit0 hit in this report,
//       bit1 yielding, bits 8-15 integrity % of the most seen (255 unknown), 16-23 blocks hit in that check; aux = (largest single-block drop, SvRange(),
//       attacker range or 0). The attacker (live contact within 35 deg of the hit side, my target first) is sent while its attribution is < 10 s old.
//       2 Hz; at once after a check with a hit (<= 3 Hz), after a run gap and on a yield change.
//  .SVO Flight->Staff MyTuple<long tgtId, Vector3D fwd, Vector3D up, Vector3D vel, double radius, int flags (bit0 orientation valid)>, 2 Hz with a target.
//  .MSL Flight->Staff MyTuple<long id, Vector3D pos, Vector3D vel>: a missile seen by any detector (SvSee, first line of Valid), <= 1 per 6 ticks per id.
//  .SVS Staff->Flight MyTuple<int flags, long tgtId, Vector3D A, Vector3D B, Vector3D D, Vector3D N>. Bits: 0 gather (A), 1 retreat via (A, wins), 2 orbit
//       bearing (B = bearing x speed), 3 missile dodge (D), 4 DAMAGED, 5 RETREATING, 6 GATHER READY, 7 GATHERING, 8 LIMP, 9 Staff holds the overrides (Flight
//       yields), 10 fight back in RTB, 11 standoff advice (N.X floor, N.Y add), 16-23 limp speed cap m/s, 24-27 threat class; N.Z enemy projectile speed.
// Failover handshake (plan item 5): SvYield is true for the first 6 ticks and while a fresh SVS has bit 9; the tick it ends SvResync() resends the AI and gun state.
// Hooks: SvInit() after LkInit(); SvRead() after LkRead(); "if (!LkBurn && !SvYield && !AiStep()) Control(dt)"; SvSend() after LkSend(); "_svSentT = -99;" in the
// run-gap line; SvLoad(_ini) in LoadConfig; _guns.RemoveAll(SvNotGun) in GetBlocks; SvSee(e) first in Valid(); "_svNb++; _svMax = Math.Max(_svMax, drop);" in
// CheckDamage's hit-block branch and SvDamage() at its end; "| SvSwarm()" in the SWARM flags; SvStatus() on the screen; "if (LkBurn || SvYield) return;" first
// in Release(); Control: SvStand, SvJink, SvOrbit, SvGather, SvHome/SvViaOn/SvFightBack, SvBrake, SvCap, SvWant. Railgun drones: SvPower(load, SvRecharging())
// at the end of PowerAndHealth, SvRails(true) first in Release(), SvRails(!_svRx) in GetBlocks. Per script: double SvRange() (own max fire range),
// void SvResync() (_aiOn = !_aiOn; _firing = false; ...), bool SvRecharging() (railgun drones only).
// Uses: _now, _tick, _rc, _tgt (Id, P, V, OF, OU, HasOri, Radius), _contacts, _guns, _mode (IDLE/RTB only), _hitDir, _hitDirT, _hits, _integ, _home, _dynHome,
// _dynHomeT, _powerScale, UnderFire, EvadeSpeed, MaxSpeed, _lkK, Me, IGC.
Vector3D SvZ;   // (never set: the zero vector, shorter than Vector3D.Zero)
IMyBroadcastListener _svL; int _svF, _svNb, _svBn, _svEh; long _svTid, _svAid; IMyCubeGrid _svG; Random _evRng = new Random();
Vector3D _svA, _svB, _svD, _svN, _svAp, _svE, _svJk, _svJd; bool _svY = true, _svHit, _svRx;
double _svT = -99, _svSentT = -99, _svMax, _svBm, _svI0, _svAr, _svAt = -99, _svEn, _svJn, _svOs = 1, _svRf = -99, _svRo = -99;
Dictionary<long, long> _svMs = new Dictionary<long, long>();
bool SvLJ = true, SvPG = true; double SvJa = 0.8, SvEv = 800, SvJm = 0.4, SvJM = 3, SvJr = 0.3, SvPh = 2;
bool SvFresh { get { return _now - _svT < 0.6; } }
bool SvOn(int b) { return SvFresh && (_svF & b) != 0; }
bool SvYield { get { return _svY; } }
bool SvViaOn { get { return SvOn(2); } }
bool SvFightBack { get { return SvOn(1024); } }
bool SvMine { get { return _tgt != null && _svTid == _tgt.Id; } }
void SvInit() { _svL = IGC.RegisterBroadcastListener(_lkK + ".SVS"); }
// [Survival] keys in this PB's Custom Data: read only (the Staff PB writes the defaults, after our first F2S); the defaults here equal the Staff's
MyIni _svI;
double SvK(string k, double d) { return _svI.Get("Survival", k).ToDouble(d); }
void SvLoad(MyIni i)
{
    _svI = i; SvLJ = i.Get("Survival", "Lead-Breaking Jinks").ToBoolean(true); SvPG = i.Get("Survival", "Power Guard").ToBoolean(true);
    SvJa = SvK("Jink Accel Fraction", 0.8); SvEv = SvK("Enemy Projectile Speed (m/s)", 800); SvJm = SvK("Jink Period Min (s)", 0.4);
    SvJM = SvK("Jink Period Max (s)", 3); SvJr = SvK("Jink Period Random", 0.3); SvPh = SvK("Power Guard Hold (s)", 2);
}
void SvTx<T>(string c, T d) { IGC.SendBroadcastMessage(_lkK + c, d, TransmissionDistance.CurrentConstruct); }
void SvRead()
{
    while (_svL.HasPendingMessage)
    {
        var m = _svL.AcceptMessage();
        if (!(m.Data is MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>)) continue;
        var d = (MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>)m.Data;
        _svF = d.Item1; _svTid = d.Item2; _svA = d.Item3; _svB = d.Item4; _svD = d.Item5; _svN = d.Item6; _svT = _now;
    }
    bool y = _tick < 6 || SvOn(512);                     // yield: start-up, or the Staff flies limp-home (stale for 0.6 s = it is gone: take over)
    if (y != _svY) { if (!y) SvResync(); _svY = y; _svSentT = -99; }
    if (Me.CubeGrid != _svG) { _svG = Me.CubeGrid; _svI0 = 0; }   // merged / split off a printer: a fresh (or repaired) drone, integrity starts again
}
void SvSend()
{
    if (_now - _svSentT < (_svHit ? 0.33 : 0.5)) return;
    _svSentT = _now;
    int ip = _integ < 0 || _svI0 <= 0 ? 255 : (int)Math.Min(100, Math.Round(100 * _integ / _svI0));
    bool a = _now - _svAt < 10;
    SvTx(".SVF", new MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>((_svHit ? 1 : 0) | (_svY ? 2 : 0) | ip << 8 | _svBn << 16,
        a ? _svAid : 0, _now - _hitDirT < 3 ? _hitDir : SvZ, a ? _svAp : SvZ, new Vector3D(_svBm, SvRange(), a ? _svAr : 0),
        _now - _dynHomeT < 5 ? _dynHome : _home));
    _svHit = false; _svBn = 0; _svBm = 0;
    var t = _tgt;
    if (t != null) SvTx(".SVO", new MyTuple<long, Vector3D, Vector3D, Vector3D, double, int>(t.Id, t.OF, t.OU, t.V(_now), t.Radius, t.HasOri ? 1 : 0));
}
// end of CheckDamage: keep the most integrity seen; on a hit, report at once with the attacker (the live contact on the side that was hit, my target first)
void SvDamage()
{
    if (_integ > _svI0) _svI0 = _integ;
    if (_svNb == 0) return;
    _svHit = true; _svBn = Math.Min(255, _svNb); _svBm = _svMax; _svNb = 0; _svMax = 0;
    if (_now - _hitDirT > 1) return;
    Vector3D me = _rc.GetPosition(); double bc = 0; Track b = null;
    foreach (var t in _contacts.Values)
    {
        Vector3D d = t.P(_now) - me; double l = d.Length();
        if (l < 1) continue;
        double c = Vector3D.Dot(d, _hitDir) / l;
        if (c < 0.82) continue;                            // 35 deg
        if (t == _tgt) c = 2;
        if (c > bc) { bc = c; b = t; }
    }
    if (b != null) { _svAid = b.Id; _svAp = b.P(_now); _svAr = Vector3D.Distance(_svAp, me); _svAt = _now; }
}
// any detection path (turret, CTC, camera, sensor) that sees an incoming missile: tell the Staff PB, which tracks it, dodges and fires the flares
void SvSee(MyDetectedEntityInfo e)
{
    var r = e.Relationship;
    if (e.Type != MyDetectedEntityType.Missile || r == MyRelationsBetweenPlayerAndBlock.Owner || r == MyRelationsBetweenPlayerAndBlock.FactionShare || r == MyRelationsBetweenPlayerAndBlock.Friends) return;
    long k; if (_svMs.TryGetValue(e.EntityId, out k) && _tick - k < 6) return;
    if (_svMs.Count > 40) _svMs.Clear();
    _svMs[e.EntityId] = _tick;
    SvTx(".MSL", new MyTuple<long, Vector3D, Vector3D>(e.EntityId, e.Position, new Vector3D(e.Velocity)));
}
bool SvNotGun(IMyTerminalBlock b) { return b.BlockDefinition.SubtypeId.Contains("Flare"); }   // a flare launcher is a SmallMissileLauncher: never a gun (the Staff fires it)
Vector3D SvRnd() { return new Vector3D(_evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5); }
// Under fire: a new velocity of Evade Speed every 1-2.5 s and on every new hit, across the threat axis (moving along it changes nothing for the shooter's
// lead), and in 7 of 10 picks against the present sideways drift (a reversal is what a constant-velocity lead misses most). No axis: random, as before.
Vector3D Evade()
{
    if (!UnderFire) return SvZ;
    if (_now > _svEn || _hits != _svEh)
    {
        _svEh = _hits; _svEn = _now + 1 + _evRng.NextDouble() * 1.5;
        // the threat axis u: the side just hit when that is not my target (someone else shoots), else my target, else the hit side
        Vector3D u = _tgt != null ? _tgt.P(_now) - _rc.GetPosition() : SvZ, h = _now - _hitDirT < 3 ? _hitDir : SvZ, r = SvRnd(), v = _rc.GetShipVelocities().LinearVelocity;
        u = u.LengthSquared() > 1 ? Vector3D.Normalize(u) : h;
        if (h != SvZ && Vector3D.Dot(h, u) < 0.7) u = h;
        if (u != SvZ)
        {
            r -= Vector3D.Dot(r, u) * u; v -= Vector3D.Dot(v, u) * u;
            if (_evRng.NextDouble() < 0.7 && Vector3D.Dot(r, v) > 0) r = -r;
        }
        _svE = r.LengthSquared() > 1e-6 ? Vector3D.Normalize(r) * EvadeSpeed : SvZ;
    }
    return _svE;
}
// Lead-breaking jinks. Turrets lead with constant velocity: over a shot's flight time T a sideways acceleration a puts us 0.5 a T^2 off their aim. So the
// period follows T (range / enemy projectile speed, the Staff's threat class speed when it knows one), the direction turns at least 90 deg every period
// (thrusters stay saturated) and the size is the velocity swing the lateral thrust can make in that period (aLat = weakest-axis acceleration), capped by
// Jink Speed. Also flips the orbit sign now and then (1 in 10 periods). "Lead-Breaking Jinks=false": no jinks at all.
Vector3D SvJink(Vector3D los, double dist, double aLat, double cap, bool on)
{
    if (!on || !SvLJ) return SvZ;
    if (_now >= _svJn)
    {
        double P = MathHelper.Clamp(dist / Math.Max(100, SvFresh && _svN.Z > 0 ? _svN.Z : SvEv) * (1 + SvJr * (2 * _evRng.NextDouble() - 1)), SvJm, Math.Max(SvJm, SvJM));
        _svJn = _now + P;
        Vector3D d = SvRnd(); d -= Vector3D.Dot(d, los) * los;
        if (d.LengthSquared() > 1e-6)
        {
            d.Normalize();
            if (Vector3D.Dot(d, _svJd) > 0) d = -d;
            _svJd = d; _svJk = d * Math.Min(cap, aLat * SvJa * P * 0.5);
        }
        if (_evRng.NextDouble() < 0.1) _svOs = -_svOs;
    }
    return _svJk;
}
// orbit term: the Staff's blind-side bearing for this target (until within 10 deg of it), else the plain orbit
Vector3D SvOrbit(Vector3D dir, Vector3D tan, double orbit)
{
    if (SvOn(4) && SvMine && _svB.LengthSquared() > 0.01)
    {
        Vector3D p = Vector3D.Normalize(_svB), e = p - dir * Vector3D.Dot(dir, p);   // bearing now c = -dir: the part of p across c
        if (e.Length() > 0.17) return Vector3D.Normalize(e) * _svB.Length();
    }
    return tan * orbit * _svOs;
}
// standoff advice (floor from the enemy's weapon range, wing hand-off add): only pushes out to hi or pulls in to lo, never past the script's own value the other way
double SvStand(double s, double lo, double hi)
{
    if (!SvOn(2048) || !SvMine) return s;
    double x = Math.Max(s, _svN.X) + _svN.Y;
    return x > s ? Math.Min(x, Math.Max(s, hi)) : Math.Max(x, Math.Min(s, lo));
}
// gather point of a bounded "attack together" window: only out of my own range and not under fire (the guns still fire at anything in range)
bool SvGather(Vector3D me, double dist, out Vector3D gv)
{
    gv = _svA - me; double l = gv.Length();
    if (!SvOn(1) || SvOn(2) || !SvMine || dist <= SvRange() || UnderFire || l < 1) return false;
    gv *= Math.Min(MaxSpeed, l * 0.5) / l; return true;
}
Vector3D SvHome(Vector3D home) { return SvViaOn ? _svA : home; }   // damage RTB: the retreat via point off the threat axis first
double SvBrake(double a) { return SvOn(256) ? a * 0.6 : a; }      // limp: brake earlier
double SvCap(double c) { int l = SvFresh ? (_svF >> 16) & 255 : 0; return l > 0 ? Math.Min(c, l) : c; }
Vector3D SvWant(Vector3D w) { return SvOn(8) ? _svD + w * 0.3 : w; }   // missile dodge first
int SvSwarm() { return SvFresh ? (_svF >> 1) & 120 : 0; }        // SWARM 8 DAMAGED, 16 RETREATING, 32 GATHER READY, 64 GATHERING
string SvStatus() { return SvFresh ? "\nSurv " + (SvYield ? "HELD " : "") + ((_svF & 48) != 0 ? "DAMAGED " : "") + ((_svF & 8) != 0 ? "DODGE" : "") : ""; }
// Railgun power guard: weapons come first on the power bus (priority 5, gyros 70, thrusters 80) and a charging small railgun draws ~3.6 MW. In a brown-out
// while it matters (under fire, RTB, missile dodge) the recharging rails are switched off for up to "Power Guard Hold (s)", at most every other hold.
void SvRails(bool on) { foreach (var g in _guns) if (g.BlockDefinition.SubtypeId.Contains("Railgun")) g.Enabled = on; _svRx = !on; }
void SvPower(double load, bool rech)
{
    bool need = UnderFire || _mode == Mode.RTB || SvOn(8);
    if (!_svRx && SvPG && need && rech && load > 0.97 && _powerScale < 0.6 && _now - _svRo > SvPh) { SvRails(false); _svRf = _now; }
    else if (_svRx && (!SvPG || !need || _now - _svRf > SvPh)) { SvRails(true); _svRo = _now; }
}
// <<< SURVIVAL LINK v1.0 <<<

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
        _ready = StartMode.Trim().ToUpperInvariant() == "READY"; _sentryAt = StartMode.Trim().ToUpperInvariant() == "SENTRY" ? 5 : -1; _mode = _ready ? Mode.GUARD : ParseMode(StartMode, Mode.IDLE);
        if (!string.IsNullOrEmpty(Storage))
        {
            var p = Storage.Split(';');
            if (p.Length >= 5)
            {
                _mode = ParseMode(p[0], _mode); _ready = p.Length > 5 && p[5] == "1"; _sentry = p.Length > 6 && p[6] == "1"; _sentryAt = -1;
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
        LkInit(); SvInit(); PlInit();
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
    if (dt <= 0 || dt > 0.5) { if (dt > 0.5 && _tick > 3) { _svSentT = -99; _aiOn = !_aiOn; _firing = false; _aiKick = false; } dt = 1.0 / 60.0; }   // run gap (PB was off / halted): the Staff watchdog may have switched the AI blocks off, so force the next pass to send the AI and gun state again
    _now += dt; _tick++;
    if (PgHold()) return;            // PRINTER GUARD: still welded to a printer, fly nothing
    _avgMs = _avgMs * 0.98 + Runtime.LastRunTimeMs * 0.02;

    if (_tick % 600 == 0) GetBlocks();
    CheckGrid();
    if (_rc == null || !_rc.IsFunctional || !_rc.IsSameConstructAs(Me)) { _err = "No working Remote Control / cockpit"; if (!_rcOut || _tick % 60 == 0) { _rcOut = true; GetBlocks(); Display(); Release(); } return; }   // (not on this construct = a severed piece: same as no controller; look for another one at 1 Hz)
    _rcOut = false;

    ReadIGC(); LkRead(); SvRead(); ReadSwarm();
    if (_tick % 6 == 3) BroadcastStatus();
    if (!Hot()) StepSlimScan();
    if (_tick % 20 == 7) CheckDamage();
    if (_sentryAt > 0 && (LkBurn || _mode != Mode.IDLE)) _sentryAt = -1;   // launched or commanded first
    else if (_sentryAt > 0 && _now >= _sentryAt) { _sentryAt = -1; if (!SentryDocked()) Command("sentry"); }   // Start Mode SENTRY, after the 5 s grace: hold this spot
    if (_sentry && _tick % 30 == 11 && SentryDocked()) { SetMode(Mode.IDLE); _err = "Docked: SENTRY stands down (IDLE)"; }   // never fly a printer / carrier: its Staff PB hands over [Launch] Mode after take-off
    if (_tick % 6 == 0) PollDesignators();
    if (_tick % 10 == 5 && !Hot()) PollSensors();
    if (_tick % 10 == 0) { PruneAndSelect(); PowerAndHealth(); }
    MaintainLock();
    if (_mode != Mode.IDLE && !Hot()) OmniScan();
    if (_tgt == null && !_aiOn && AutoScan && (_mode == Mode.GUARD || _mode == Mode.HUNT || _mode == Mode.FOLLOW) && !Hot()) AutoScanStep();
    if (ShareTargets && _tgt != null && _tgt.Src != "IGC" && _tick % 30 == 0 && _now - _tgt.Seen < 1.0 && !Hot()) ShareTarget();

    if (!LkBurn && !SvYield && !AiStep()) Control(dt);
    LkSend(); SvSend();
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
        case "follow": case "escort": SetMode(Mode.FOLLOW); break;
        case "satellite": case "sat": case "orbit": SetMode(Mode.FOLLOW); break;
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
        case "wp": if (w.Length > 1) WpOn = WpShare = w[1] == "on"; break;
        case "focus": SplitFocus = false; break;
        case "ready": SetMode(Mode.GUARD); if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; _ready = true; } break;   // READY: hold this spot (the carrier link's HOME is ignored), scan all round, fight when something comes, come back
        case "sentry": SetMode(Mode.GUARD); if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; _sentry = true; } break;   // SENTRY: hold this spot, hunt down anything that comes within Sentry Range (no leash), come back
        case "home": if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } break;
        case "range":
            double r; if (w.Length > 1 && double.TryParse(w[1], out r)) Standoff = MathHelper.Clamp(r, MinRange, MaxFireRange); break;
        case "reload": LoadConfig(); KzLoad(); AiGateLoad(); GetBlocks(); break;
    }
}

// SENTRY (the default Start Mode) stands down while the drone is clamped: a merge block or connector of this grid is locked (printer bay, cradle,
// carrier). Merged, the printer's thrusters are on this construct too.
List<IMyShipMergeBlock> _sdMg = new List<IMyShipMergeBlock>(); List<IMyShipConnector> _sdCn = new List<IMyShipConnector>();
bool SentryDocked()
{
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_sdMg, b => b.CubeGrid == g && b.IsConnected);
    GridTerminalSystem.GetBlocksOfType(_sdCn, b => b.CubeGrid == g && b.Status == MyShipConnectorStatus.Connected);
    return _sdMg.Count + _sdCn.Count > 0;
}

void SetMode(Mode m)
{
    _mode = m; _ready = false; _sentry = false; _sentryAt = -1; _patrolLeg = false; _patrolStopSet = false;
    if (m == Mode.IDLE || m == Mode.ASSIST) Release();
}

// =====================================================================
//  CONFIG
void LoadConfig()
{
    MyIniParseResult res;
    if (!_ini.TryParse(Me.CustomData, out res)) { _err = "CustomData parse error line " + res.LineNo; return; }
    const string S = "Lancer";
    Tag = Cfg(S, "IGC Tag", Tag);
    StartMode = Cfg(S, "Start Mode (GUARD/HUNT/ASSIST/IDLE)", StartMode);
    ProjSpeed = Cfg(S, "Projectile Speed", ProjSpeed);
    InheritVel = Cfg(S, "Projectiles Inherit Ship Velocity", InheritVel);
    MaxFireRange = Cfg(S, "Max Fire Range", MaxFireRange);
    BurstSeconds = Cfg(S, "Burst Seconds", BurstSeconds);
    BurstPause = Cfg(S, "Burst Pause", BurstPause);
    ConserveBelow = (int)Cfg(S, "Conserve Below Shells", (double)ConserveBelow);
    ConserveRange = Cfg(S, "Conserve Max Range", ConserveRange);
    Standoff = Cfg(S, "Standoff Range", Standoff);
    StandoffHuge = Cfg(S, "Standoff Range vs Huge Grids", StandoffHuge);
    MinRange = Cfg(S, "Min Range", MinRange);
    GuardRadius = Cfg(S, "Guard Radius", GuardRadius);
    SentryRange = Cfg(S, "Sentry Range (m)", SentryRange);
    MaxSpeed = Cfg(S, "Combat Speed", MaxSpeed);
    CruiseSpeed = Cfg(S, "Cruise Speed", CruiseSpeed);
    OrbitSpeed = Cfg(S, "Orbit Speed", OrbitSpeed);
    Jink = Cfg(S, "Evasive Jinking", Jink);
    JinkSpeed = Cfg(S, "Jink Speed", JinkSpeed);
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
    FireStaleMax = Cfg(S, "Max Track Age To Fire (s)", FireStaleMax);
    SearchTenths = (int)Cfg(S, "Search Share Tenths (stale lock)", (double)SearchTenths);
    SearchTenthsLate = (int)Cfg(S, "Search Share Tenths (stale > 1.5 s)", (double)SearchTenthsLate);
    SepRange = Cfg(S, "Wingmate Separation Range", SepRange);
    SepSpeed = Cfg(S, "Wingmate Separation Speed", SepSpeed);
    MateClear = Cfg(S, "Wingmate Fire Clearance (m)", MateClear);
    LooseFar = Cfg(S, "Loose Fire Beyond Standoff", LooseFar);
    FireTol = Cfg(S, "Fire Window Multiplier", FireTol);
    LeadScale = Cfg(S, "Lead Scale (1: as calculated)", LeadScale);
    LeadMargin = Cfg(S, "Aim Margin (predicted misses)", LeadMargin);
    UseTargetAccel = Cfg(S, "Use Target Acceleration", UseTargetAccel);
    SplitFocus = Cfg(S, "Split Targets Across Hive (false: focus fire)", SplitFocus);
    DisabledGrace = Cfg(S, "Seconds Before Dropping Disabled Target", DisabledGrace);
    EvadeSpeed = Cfg(S, "Evade Speed When Hit", EvadeSpeed);
    AimMode = Cfg(S, "Aim Point (AUTO/BREACH/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();
    BreachAuto = Cfg(S, "Breach Large Ships (AUTO)", BreachAuto);
    BreachMinRadius = Cfg(S, "Breach Min Target Radius (m)", BreachMinRadius);
    BreachStall = Cfg(S, "Breach Stall (s)", BreachStall);
    WpOn = Cfg(S, "Use Fleet Weakpoints", WpOn);
    WpShare = Cfg(S, "Share Weakpoints", WpShare);
    WpCone = Cfg(S, "Weakpoint Cone (deg)", WpCone);
    WpHold = Cfg(S, "Weakpoint Hold (s)", WpHold);
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
    AiRepickWindow = Cfg(S, "AI Re-pick Only Within (s) Of Last Target", AiRepickWindow);
    RtbBattery = Cfg(S, "RTB Battery Fraction", RtbBattery);
    AimKp = Cfg(S, "Aim Kp", AimKp);
    AimKd = Cfg(S, "Aim Kd", AimKd);
    MaxTurn = Cfg(S, "Max Turn Rate rad/s", MaxTurn);
    SvLoad(_ini);
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
    _guns.RemoveAll(SvNotGun);
    GridTerminalSystem.GetBlocksOfType(_cams, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_turrets, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_ctcs, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_bats, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_sensors, b => b.IsSameConstructAs(Me));
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
    FaSee(e); SvSee(e);
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
    ReadTgt(_lkTgt);
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
    if (_mode == Mode.FOLLOW && _lkOwner) return _lkOwnerP;   // FOLLOW: the owner (ship link or on foot), see the FOLLOW block
    if (_mode != Mode.RTB && _tgt == null && _now - _assistT < 4) return _assist;   // go help a hive member under fire
    if (_mode == Mode.HUNT && _tgt == null && _patrolStopSet) return _patrolWp;   // search patrol: hold the listening stop, not fly home
    return (_now - _dynHomeT < 5) ? _dynHome : _home;
}

// =====================================================================
//  POWER / HEALTH
double _batFrac = 1, _ammoFrac = 1; bool _hadAmmo = false;
int _shells = -1;             // shells left (from inventory volume, 1.5 L each); -1 = unknown (creative / empty inventories)
const double ShellVolM3 = 0.0015;
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
    _shells = _hadAmmo ? (int)Math.Round(volM3 / ShellVolM3) : -1;

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
    if (LkBurn) return;   // take-off burn: the Staff PB flies (LAUNCH block)   // automatic take-off after the release from a printer (LAUNCH block)
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
    if (_tgt != null && (_mode != Mode.RTB || SvFightBack))   // (no guns = scout: still tracks with the nose cameras)
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
            _tvD = (aimPoint - _rc.GetPosition()).Length();
            Aim(aimDir, up, losRate, dt);

            Vector3D gunFwd = _aimRef.WorldMatrix.Forward;
            double dist = Vector3D.Distance(aimPoint, gunPos);
            _aimErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(gunFwd, aimDir), -1, 1)) * 57.2958;
            double aimR = AimActive(_tgt) && !(LooseFar && dist > StandoffFor(_tgt) + 100) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6;   // aiming at a hull point needs a tighter window
            _fireWinDeg = Math.Max(0.3, Math.Atan(aimR / Math.Max(dist, 1)) * 57.2958) * FireTol;
            shoot = FireGate(dist) && _now - _tgt.Seen < FireStaleMax;
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
            else if (myVel.LengthSquared() > 100 && _mode != Mode.GUARD && !((_patrolLeg || _now - _hitDirT < 10) && AutoScan)) fwd = Vector3D.Normalize(myVel);   // (a patrol leg keeps sweeping: the thrusters fly any direction)
            else
            {
                // radar sweep: yaw round and nod up/down so the camera cone covers all around
                if (AutoScan) fwd = SweepDir(up);
            }
            Aim(fwd, up, Vector3D.Zero, dt);
        }
    }
    FireBurst(shoot);

    // ---------- translation ----------
    if (_mode == Mode.ASSIST) return;
    double mass = _rc.CalculateShipMass().PhysicalMass;
    double aBrake = SvBrake(MinAccel(mass, grav) * 0.55); _aBrake = aBrake;
    Vector3D want;
    Vector3D home = Home();
    if (_mode != Mode.RTB && _now - _dynHomeT < 5 && home == _dynHome) { Vector3D o = me - home; home += Vector3D.Normalize(o.LengthSquared() > 1 ? o : _rc.WorldMatrix.Up) * 65; }   // the carrier link's HOME is the ship's centre: hold on a sphere round it, on my side, never inside the hull
    if (_mode == Mode.RTB || _tgt == null)
    {
        Vector3D d = SvHome(home) - me; double dist = d.Length();
        double v = Math.Min(Math.Min(SvViaOn ? MaxSpeed : CruiseSpeed, dist * 0.8), Math.Sqrt(2 * aBrake * dist));
        Vector3D sep = Separation(me);
        if (_mode != Mode.RTB && _tgt == null && sep.LengthSquared() > 1 && Home() == _home) _home += sep * dt;   // idle: the wing spreads out (the home point moves away from the mates, or it pulls the drone back to its paste spot; from the Ballista Helm)
        want = (dist > 0.5 ? d / dist * v : Vector3D.Zero) + Evade() + sep;
        if (_patrolLeg && _mode == Mode.HUNT && _tgt == null)   // search patrol: cruise towards the current waypoint
        {
            Vector3D pd = _patrolWp - me; double pl = pd.Length();
            want = (pl > 1 ? pd / pl * PatrolSpeed : Vector3D.Zero) + Evade() + Separation(me);
        }
        if (_mode == Mode.FOLLOW && LkWantOk) want = _lkWant + ((_lkFl & 2) != 0 ? Vector3D.Zero : Separation(me)) + Evade();   // own slot by the owner, matching its velocity
        if (_mode == Mode.RTB && dist < 15 && myVel.Length() < 2 && !SvViaOn) { SetMode(Mode.IDLE); _err = "Home - idle"; return; }
    }
    else
    {
        Vector3D tp = _tgt.P(_now), tv = _tgt.V(_now);
        Vector3D d = tp - me; double dist = d.Length();
        Vector3D dir = dist > 1 ? d / dist : _rc.WorldMatrix.Forward;
        double e = dist - SvStand(StandoffFor(_tgt), MinRange, SvRange() - 30);
        double vr = Math.Sign(e) * Math.Min(Math.Min(Math.Abs(e) * 0.6, MaxSpeed), Math.Sqrt(2 * aBrake * Math.Abs(e)));
        if (dist < MinRange) vr = -MaxSpeed * 0.6;
        Vector3D upRef = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : _rc.WorldMatrix.Up;
        Vector3D tan = Vector3D.Cross(dir, upRef);
        if (tan.LengthSquared() < 1e-4) tan = _rc.WorldMatrix.Right;
        tan.Normalize();
        _jink = SvJink(dir, dist, aBrake / 0.55, JinkSpeed, Jink);   // lead-breaking jinks (SURVIVAL LINK)
        double orbit = dist < MaxFireRange * 1.2 ? OrbitSpeed : 0;
        want = tv + dir * vr + SvOrbit(dir, tan, orbit) + _jink + Evade() + Separation(me);
        Vector3D gv; if (SvGather(me, dist, out gv)) want = gv + Evade() + Separation(me);   // bounded attack-together window (Staff PB)
        if (_mode == Mode.GUARD && !_sentry && Vector3D.Distance(me, home) > GuardRadius * 1.3) want = Vector3D.Normalize(home - me) * CruiseSpeed;
        if (_mode == Mode.FOLLOW && LkFar(me)) want = LkBack(me);   // leash: drop the fight, back to the owner
    }
    double cap = (_tgt != null && _mode != Mode.RTB) || _mode == Mode.FOLLOW ? MaxSpeed : CruiseSpeed;
    cap = SvCap(SvViaOn ? Math.Max(cap, MaxSpeed) : cap);   // limp cap; the retreat via point is flown at combat speed
    want = KzWant(want, me, cap);   // KAMIKAZE DEFENCE: back away / break sideways
    want = SvWant(want);   // missile dodge (Staff PB)
    want = FaWant(want, me);   // FRIENDLY AVOIDANCE: keep clear of non-enemy grids
    if (want.LengthSquared() > cap * cap) want = Vector3D.Normalize(want) * cap;

    Vector3D acc = (want - myVel) * 2.0;
    if (acc.LengthSquared() > 2500) acc = Vector3D.Normalize(acc) * 50;
    ApplyThrust((acc - grav) * mass);
}

bool _leadOk = false;
// Gun intercept (v2.4): constant-velocity solution, then refined with the LEAD block's prediction (fresh acceleration held for
// the expected rest of the target's manoeuvre, aim point across the line of fire with a hull margin for the predicted miss).
Vector3D _aimLos; double _aimNeed;   // line of fire and required hull margin for AimOffset's choice
double LeadMargin = 1.0;             // aim point keeps this many predicted misses of known hull around it
bool Lead(Track t, Vector3D shooter, Vector3D shooterVel, out Vector3D point, out Vector3D dir)
{
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
        if (disc < 0) { point = tp; dir = Vector3D.Normalize(rp); _leadT = 0; _leadOk = false; return rp.LengthSquared() > 1; }
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
    _leadT = tt; _leadOk = true;
    point = shooter + p;
    double len = p.Length();
    if (len < 1) { dir = Vector3D.Zero; return false; }
    dir = p / len;
    return true;
}

// ---- fire control
string _gateWhy = "";
bool _bursting = false; double _burstStart = 0, _burstEnd = -99;

// Every reason NOT to open fire. A shell does 500 damage, so shots are only taken inside a tight window.
bool FireGate(double dist)
{
    _gateWhy = "";
    if (!_leadOk) { _gateWhy = "no lead solution"; return false; }
    if (_now < _holdFireUntil) { _gateWhy = "holding fire"; return false; }
    if (dist > MaxFireRange) { _gateWhy = "out of range"; return false; }
    if (_aimErrDeg >= _fireWinDeg) { _gateWhy = "not on target"; return false; }
    double unc = 0.5 * Math.Max(0, _tgt.AccMag - _tgt.EA().Length()) * _leadT * _leadT;
    if (unc > Math.Max(_tgt.Radius * 0.8, 1.5)) { _gateWhy = "target jinking: miss " + unc.ToString("0.0") + " m"; return false; }
    if (_shells >= 0 && _shells <= ConserveBelow && (dist > ConserveRange || _aimErrDeg > 0.5 * _fireWinDeg)) { _gateWhy = "saving last shells"; return false; }
    return true;
}

// Short bursts: open fire when the gate is open, stop after BurstSeconds or the moment the gate closes, then pause.
void FireBurst(bool gate)
{
    if (_bursting)
    {
        if (!gate || _now - _burstStart > BurstSeconds) { SetFire(false); _bursting = false; _burstEnd = _now; }
    }
    else if (gate && _now - _burstEnd > BurstPause) { SetFire(true); _bursting = true; _burstStart = _now; }
    _showFire = _bursting;
}

// Wingmates repel each other: all of them fly the same orbit at the same speed and would otherwise end up in contact.
// Uses the positions the hive already broadcasts (_SWARM); costs a few vector operations per mate per tick.
double StandoffFor(Track t) { return t.RawMax > HugeTargetRadius ? StandoffHuge : Standoff; }
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

// Friendly-fire gate from the IGC positions: hold fire while a hive mate stands within MateClear metres of the line
// from the guns to the target (bullets fly 0.5-1 s, so the mate is extrapolated by its age only). Works with no camera
// charge, unlike the camera ray in CheckLineOfFire, which can only look once per second and fails open.
double MateClear = 14;   // [config] Wingmate Fire Clearance (m)
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
    if (LkBurn || SvYield) return;   // the Staff PB flies (take-off burn, limp-home): never zero its overrides
    ReleaseGyros();
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
    SetFire(false);
}

// =====================================================================
//  DISPLAY
void Display()
{
    string aim = "";
    if (_tgt != null) aim = "\nAim: " + AimLabel(_tgt) + " (" + _tgt.Hull.Count + " hull pts)" + AimInfo();
    string s = "FLIGHT " + VER + "  " + (_sentry && _mode == Mode.GUARD ? "SENTRY" : _mode.ToString()) + "  tgt " + (_tgt != null ? _tgt.Src : "-") + "  staff " + (LkAlive ? "ok" : "none") + aim + SvStatus() + (_fc != "" ? "\n1st contact " + _fc + " " + (_now - _fcT).ToString("0") + " s ago" : "") + (_err != ""? "\n! " + _err : "") + (_crash != "" ? "\n! Script error: " + _crash : "");
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}

// =====================================================================
//  SENSORS (360 deg, ~50 m) + idle sweep
List<IMySensorBlock> _sensors = new List<IMySensorBlock>();
List<MyDetectedEntityInfo> _sensed = new List<MyDetectedEntityInfo>();
Vector3D _sweepUp; bool _sweepUpSet = false;

// (sensor setup: the Staff PB's STAFF SEARCH block writes the sensor properties)

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
        if (cmp && _slimHp[i] - v > 0.5) { _svNb++; _svMax = Math.Max(_svMax, _slimHp[i] - v); Vector3D d = g.GridIntegerToWorld(s.Position) - gc; double l = d.Length(); if (l > 0.3) hd += d / l; }
        _slimHp[i] = v;
    }
    if (hd.LengthSquared() > 0.01)
    {
        hd.Normalize();
        Vector3D m = _now - _hitDirT < 3 ? _hitDir * 0.6 + hd * 0.4 : hd;
        if (m.LengthSquared() > 0.01) { _hitDir = Vector3D.Normalize(m); _hitDirT = _now; }
    }
    if (_integ >= 0 && sum < _integ - 0.5) { _hitAt = _now; _hits++; }
    _integ = sum; SvDamage();
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
        if ((d.Item5 & 4) != 0 && ShareTargets && _tgt != null && _tgt.Id == d.Item4 && _tgt.Src != "IGC" && _now - _tgt.Seen < 0.4 && _now - _lastStaleShare > 0.3)
        { _lastStaleShare = _now; ShareTarget(); }
    }
}
void BroadcastStatus()
{
    IGC.SendBroadcastMessage(Tag + "_SWARM", new MyTuple<long, Vector3D, Vector3D, long, int>(
        Me.CubeGrid.EntityId, _rc.GetPosition(), _rc.GetShipVelocities().LinearVelocity, _tgt != null ? _tgt.Id : 0, (UnderFire ? 1 : 0) | (OwnAlert ? 2 : 0) | (LockStale ? 4 : 0) | SvSwarm()));
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
    WpTick(t);
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

// >>> BREACH + SEEKER v1.3 (shared block: identical in every armed drone script, kept in sync by tools/aim_sync.py) >>>
// BREACH (large ships): spreading hits over a big hull chips thousands of armour blocks and destroys none (sim vs a 13,666-block
// warship). BREACH holds ONE hull point and keeps every round on it: the known hull point nearest the target's centre (on a long
// ship that is mid-deck or mid-belly, where the armour over the core is thinnest). Probe rays at it that come back deeper show the
// hole; the drone then aims down the hole, towards the centre. No progress for "Breach Stall (s)", or two probe rays in a row that
// find nothing there: that point is dropped and the next one is used. AUTO uses BREACH on large grids of radius >= Breach Min
// Radius; "aim breach" forces it, "breach off" turns the AUTO switch off.
// SEEKER (v1.3: moved to the Staff PB's STAFF SEARCH block, docs/design/survivability.md): a turret on this drone with [Seeker] in its name is
// set to the targeting group "Seeker Group" (PowerSystems / Weapons / Propulsion) and the Staff PB raycasts where it points twice a second; the hull
// point hit, right over the subsystem, reaches this PB as the .WPT slot with flag 4096 (own seeker) and becomes the breach point at once (BreachSet(.., true)),
// drilled along the turret's line. The "Seek:" report, "seeker on|off" and the two seeker keys are the Staff PB's now.
// FLEET WEAKPOINT (v1.1, docs/design/hive_weakpoint.md): a sniper at 2 km cannot find a weakpoint with an 800 m turret, so the wing shares
// holes. This PB sends its own confirmed hole (and every seeker sample) to its Staff PB (.WPF), which relays it to the fleet channel
// (<ch>_WP) and sends back the best hole of the whole wing for our target (.WPT, read by LkRead into _wp*). Points are in the TARGET frame,
// so they stay true while the ships move. WpAdopt takes the relayed hole when it is within the cone of our own line of sight (or aims
// through it at the vital part when its depth is known); our own probe rays confirm it or throw it out. Own turret beats the relay, and a hole that is being drilled
// is kept (Weakpoint Hold) instead of being swapped for a fresh seeker hole.
bool WpOn = true, WpShare = true; double WpCone = 40, WpHold = 10;
long _wpId; Vector3D _wpS, _wpD, _wpI; int _wpF; double _wpT = -99, _wpSwT = -99, _wpSendT = -99;   // slot from the Staff PB: best fleet hole (I = depth, vital depth hint, age)
bool _bOk, _wpAdopt; double _bHint = -1; int _wpKind; List<Vector3D> _wpRej = new List<Vector3D>();
long _wpEvId; Vector3D _wpEvS, _wpEvD; double _wpEvT = -99; int _skSk, _wpEvF;
bool BreachAuto = true; double BreachMinRadius = 25, BreachStall = 30, BreachProbe = 30;
long _bId; bool _bOn, _bSeek; Vector3D _bS, _bDir; double _bDepth, _bProgT; int _bMiss;
List<Vector3D> _bBad = new List<Vector3D>();

bool BreachMode(Track t)
{
    if (AimMode == "BREACH") return true;
    return AimMode == "AUTO" && BreachAuto && t.Type == MyDetectedEntityType.LargeGrid && t.Radius >= BreachMinRadius;
}
bool BreachBad(Vector3D h) { foreach (var q in _bBad) if ((q - h).LengthSquared() < 36) return true; return false; }
void BreachSet(Vector3D s, Vector3D dir, bool seek)
{
    _bS = s; _bDir = dir; _bSeek = seek; _bOn = true; _bDepth = 0; _bProgT = _now; _bMiss = 0; _bOk = false; _wpAdopt = false; _bHint = -1;
}
Vector3D BreachOffset(Track t)
{
    if (_bId != t.Id) { _bId = t.Id; _bOn = false; _bBad.Clear(); _wpRej.Clear(); _bOk = false; _wpAdopt = false; }
    if (_bOn && _now - _bProgT > BreachStall) BreachDrop();
    if (WpOn || (_wpF & 4096) != 0) WpAdopt(t);
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
    // a relayed hole that my own ray cannot see (it stops on the hull well before the entry) is not for me
    if (_wpAdopt && !_bOk && d < -6) { _wpRej.Add(_bS); if (_wpRej.Count > 6) _wpRej.RemoveAt(0); BreachDrop(1); return; }
    if (Math.Abs(d) < 3 || d > _bDepth + 0.5) _bOk = true;
    if (d > _bDepth + 0.5) { _bDepth = Math.Min(d, 60); _bProgT = _now; }
    if (WpShare && _bOk && (_bDepth >= 55 || (_bHint >= 0 && _bDepth >= _bHint + 3))) BreachDrop(2);   // through to the vital part: tell the wing, take the next point
}
void BreachMiss() { if (++_bMiss >= 2) BreachDrop(); }
void BreachDrop(int why = 0)                             // why: 0 stalled / missed, 1 not visible from here (relay), 2 done
{
    if (_bOn)
    {
        _bBad.Add(_bS); if (_bBad.Count > 12) _bBad.RemoveAt(0);
        if (why != 1 && WpShare && (_bOk || why == 2)) { _wpEvS = _bS; _wpEvD = _bDir; _wpEvF = why == 2 ? 32 : 64; _wpEvT = _now; _wpEvId = _bId; }
    }
    _bOn = false; _bSeek = false; _bMiss = 0; _bOk = false; _wpAdopt = false;
}
bool WpRejected(Vector3D s) { foreach (var q in _wpRej) if ((q - s).LengthSquared() < 36) return true; return false; }
void WpTake(Vector3D s, Vector3D d, double depth, double hint)
{
    BreachSet(s, d, true); _bDepth = Math.Max(0, depth); _bHint = hint; _wpAdopt = true; _wpSwT = _now; _wpKind = _wpF & 31;
}
// the best hole of the wing for this target (from the Staff PB, refreshed every second): take it when it beats what I hold
void WpAdopt(Track t)
{
    if (_wpId != t.Id || _now - _wpT > 3) return;
    if ((_wpF & 4096) != 0)                                // my own seeker (Staff PB): replaces a plain or relayed point at once, an old seeker point once its hole stalled 6 s
    {
        if (!BreachBad(_wpS) && (!_bOn || !_bSeek || (_wpAdopt && (_wpS - _bS).LengthSquared() > 16) || ((_wpS - _bS).LengthSquared() > 64 && _now - _bProgT > 6)))
        { BreachSet(_wpS, _wpD, true); _bHint = _wpI.Y; _skSk = _wpF & 7; }
        return;
    }
    if ((_wpF & 32) != 0)                                  // a wingmate finished this hole: never aim there again
    {
        if (!BreachBad(_wpS)) { _bBad.Add(_wpS); if (_bBad.Count > 12) _bBad.RemoveAt(0); }
        if (_bOn && (_bS - _wpS).LengthSquared() < 36) { _bOn = false; _bSeek = false; _bOk = false; _wpAdopt = false; }
        _wpT = -99; return;
    }
    if ((_bOn && _bSeek && !_wpAdopt) || BreachBad(_wpS) || WpRejected(_wpS) || _now - _wpSwT < 6) return;   // my own turret beats the relay
    if (_bOn && (_bS - _wpS).LengthSquared() < 16) return;                                              // already on it
    // a hole that is being drilled (progress within "Weakpoint Hold (s)", 3 m deep) is not given up for a fresh seeker hole: the finder is often dead
    // before the wing has drilled the new one (sim, Pathfinder A/B); only a relayed hole that is already 3 m deeper than mine takes over
    bool prog = _bOn && _bDepth >= 3 && _now - _bProgT < WpHold;
    bool better = !_bOn || _wpI.X >= _bDepth + 3 || (!prog && (((_wpF & 8) != 0 && !_bSeek) || (_now - _bProgT > 6 && _wpI.X > _bDepth + 1)));
    if (!better) return;
    Vector3D me = Vector3D.TransformNormal(Me.GetPosition() - t.P(_now), MatrixD.Transpose(t.O));   // me, in the target frame
    double ll = me.Length(); if (ll < 1) return;
    Vector3D los = -me / ll;                               // from me to the target
    if (Vector3D.Dot(los, _wpD) >= Math.Cos(WpCone * Math.PI / 180)) { WpTake(_wpS, _wpD, _wpI.X - 2, _wpI.Y); return; }   // same side: the same hole
    if (_wpI.Y < 0) return;                                // other side and no depth: keep my own point
    Vector3D V = _wpS + _wpD * _wpI.Y, u = V - me; double ul = u.Length(); if (ul < 1) return;   // the vital part: aim at it through my own entry
    u /= ul; double bestOff = 12; Vector3D h = Vector3D.Zero; bool found = false;
    foreach (var q in t.Hull)
    {
        if (BreachBad(q)) continue;
        Vector3D w = q - me; double a = Vector3D.Dot(w, u); if (a <= 0 || a >= ul) continue;
        double off = (w - u * a).Length(); if (off < bestOff) { bestOff = off; h = q; found = true; }
    }
    if (found) { Vector3D v = V - h; double vl = v.Length(); if (vl > 1) WpTake(h, v / vl, 0, vl); }
}
// my own findings, to the Staff PB: my hole once my own ray has confirmed it; done / stalled holes for 3 s (seeker samples come from the Staff PB itself)
void WpTick(Track t)
{
    if (!WpShare || !LkAlive || _now - _wpSendT < 0.5) return;
    bool ev = _now - _wpEvT < 3, hole = _bOn && _bOk && _bId == t.Id && BreachMode(t);
    if (!ev && !hole) return;
    if (!ev && _now - _wpSendT < 1.0) return;
    _wpSendT = _now;
    long id = t.Id; Vector3D s, d; double dep = 0, hint = -1, age = 0; int f = 0;
    if (ev) { id = _wpEvId; s = _wpEvS; d = _wpEvD; f = _wpEvF; }
    else
    {
        s = _bS; d = _bDir; dep = _bDepth; hint = _bHint; age = _now - _bProgT;
        if (_wpAdopt) f = (_wpKind & 31) | 128; else if (_bSeek) f = 8 | _skSk;
    }
    WpSend(new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(id, s, d, new Vector3D(dep, hint, age), Me.CubeGrid.EntityId, f));
}
string AimInfo()
{
    string s = "";
    if (_bOn && _tgt != null && _bId == _tgt.Id && BreachMode(_tgt)) s += " hole " + _bDepth.ToString("0") + " m" + (_wpAdopt ? " (fleet " + ((_wpF >> 8) & 15) + ")" : _bSeek ? " (seeker)" : "");
    return s;
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
    if (PatrolStep()) want = false;            // (runs without AI blocks too: the listening stops are then the script's camera sweep)     // on a patrol leg the script flies and the AI blocks stay off until the next listening stop
    want = AiGate(want);   // AI GATE: the AI blocks may do nothing (AI Behavior off): time-share with the script's own search
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
        double r = 0, th = _aiScan * 2.39996323;
        if ((_aiScan & 1) == 1) { int j = (_aiScan >> 1) % 16; r = AiSpiralMin + (AiSpiralMax - AiSpiralMin) * Math.Pow((j + 0.5) / 16.0, AiSpiralPow); }
        var e = c.Raycast(len, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
        if (Valid(e)) AddContact(e, "AI");
        else if (!e.IsEmpty() && e.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies && (e.Type == MyDetectedEntityType.SmallGrid || e.Type == MyDetectedEntityType.LargeGrid)
                 && Ignored(e.EntityId) && e.BoundingBox.Size.Length() * 0.5 >= 1.6) AiHeldIgnored(e.EntityId);
    }
}
// (SEARCH PATROL: planned by the Staff PB's STAFF SEARCH block, flown through PATROL LINK)

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
