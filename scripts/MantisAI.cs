// =====================================================================
//  MANTIS HYBRID AI  v2.5-mantis  - railgun + gatling mid-range drone controller
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
//  v2.2-mantis (round 2, sim; details and numbers in docs/script-fix-notes.md "Round 2: longbow-mantis")
//   * Gatling fire range 650 -> 760 m (the data range is 800 m and the ship holds 650 m, so it used to fire only in the inner half of its range).
//   * Aim: the lead uses the acceleration of the last fix interval (Fresh Acceleration Weight 0.8, scale 0.9) instead of the smoothed gated one.
//   * Power: the thrust throttle is lifted while the ship has to travel more than 200 m (Full Thrust While Travelling).
//   * Railgun: a held trigger is released, the next attempt comes when the capacitor should be full, a refused trigger is repeated after 0.5 s,
//     the thrusters are off while the shot is pending and 0.25 s before the trigger (No Thrust Around The Shot).
//     (Railgun Off While AI Searches exists but is off for the Mantis: its gatling and the AI phase matter more than the first slug.)
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
//  v2.5 (LEAD v1.0, shared block, docs/lead-v1.md): the lead solver predicts the target with its freshest acceleration, held only
//   for as long as its manoeuvres usually last (an AI ship on evasive flips its strafe every 2-5 s); the hull aim point counts only
//   across the line of fire, turns with the target's spin, and keeps "Aim Margin" x the predicted miss of known hull around it
//
//  COMMANDS (argument, toolbar, or IGC "<Tag>_CMD")
//   guard | follow | hunt | assist | rtb | idle | lock | unlock | home | reload
//   range <m>   force an engagement range (0 = automatic)
//   follow (or escort)   stay with the owner: a ship running a Mothership link, or you on foot near the drone ([Follow] section)
//   satellite (sat, orbit)   FOLLOW, but the drones spread round a slowly turning vertical ring centred on the owner (ring size grows with their number); [Follow] section
//  KAMIKAZE DEFENCE (v2.6-mantis; shared block tools/kz_block.cs, docs/kamikaze-defence.md): incoming swarms of small warhead drones are
//   never written off as wrecks, become the priority target, and the drone backs away while firing and breaks sideways just
//   before impact; spare camera rays track the swarm and the tracks are shared with the hive. Own [Kamikaze Defence] Custom Data section.
// =====================================================================

const string VER = "2.6-mantis";

// ---------------- settings (Custom Data) ----------------
string Tag = "MANTIS";
string StartMode = "SENTRY";   // default: hold the spot, wake on a target within Sentry Range (stands down to IDLE while docked)
double ProjSpeed = 1000, WeaponRange = 1400, ReloadTime = 20; // used for unknown weapons
double ShotDelayCfg = 0;     // trigger -> projectile leaves the barrel, unknown weapons (railgun data: 0.5 s small, 2 s large)
double ShotDelay { get { return ShotDelayCfg; } }   // the value in use (the Staff's weapon auto-detect sends it as Shot Delay, CONFIG LINK)
bool GravityDrop = true, InheritVel = true, AutoWeapon = true;
double HitFraction = 0.5;    // shot must land within this fraction of target radius
double VelErr = 0.3;         // m/s assumed velocity estimate error
double DodgeSmall = 8, DodgeLarge = 3, DodgeChar = 4; // assumed evasive accel floors m/s^2
double RangeMargin = 0.95;   // never plan shots beyond this fraction of weapon range
double MinEngage = 250;      // closest the railgun will choose to fight
double EngageCap = 650;      // never plan to sit further than this (keeps the gatling in range); 0 = railgun range
double EngageCapHuge = 1350, HugeTargetRadius = 38;   // against huge grids (radius over 38 m, e.g. the Tech Hunter; the sim Pirate is under it) sit at railgun range instead (charge test 2026-10-14, docs/sim-pending-2026-10-14.md: a lone Mantis vs a 100 m/s Tech Hunter survived 0 % at 650 m, 94-95 % at 1,300-1,400 m, 2.5x the damage); 0 = use Engage Cap
bool UseGatling = true;
double GatSpeed = 800, GatRange = 760, GatTol = 1.0; // gatling bullet speed (data: 800 m/s, 800 m), fire range, window multiplier
double ForcedRange = 0;      // >0 overrides automatic range
double RushOrbit = 60, RushRange = 4000, DodgeRange = 500, RushSpeed = 100;   // anti-rush: a big ship charging us (see the translation block)
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
int _wCls = -1;
bool _slowWeapon { get { return ReloadTime > 2; } }   // single-shot weapon (railgun/artillery) vs continuous

// ---------------- state ----------------
enum Mode { IDLE, GUARD, HUNT, ASSIST, RTB, FOLLOW }
Mode _mode = Mode.IDLE;
MyIni _ini = new MyIni();
double _now = 0; long _tick = 0;
Vector3D _home; bool _homeSet = false, _ready = false, _sentry = false;
double SentryRange = 2500, _sentryAt = -1;   // _sentryAt: Start Mode SENTRY waits IDLE until this time (a printer JOIN or arming can still launch it)
// SENTRY: wake on a target this close (the AI Offensive block's search range). CustomData "Sentry Range (m)"
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
string _err = "";
double _aimErrDeg, _winDeg, _engage;
int _shots = 0;
double _batFrac = 1; int _sabots = 0; bool _hadAmmo = false;


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

// >>> SPLIT LINK v1.2 (shared block: identical in every Flight half of a two-PB drone, synced by tools/split_sync.py, see docs/design/wasp_split.md) >>>
// Flight side of the Flight/Staff link; the Staff PB (scripts/<Drone>StaffAI.cs) has the other end. Everything that is not on the hot path lives there:
// FLEET LINK, FOLLOW / SATELLITE geometry, LAUNCH (take-off burn), the status screen. IGC key "WSPL" + this PB's EntityId, TransmissionDistance.CurrentConstruct.
// Script hooks: LkInit() with the other listener setup; ReadIGC(); LkRead(); LkSend() in the tick; "if (!LkBurn && !AiStep()) Control(dt)"; "if (LkBurn) return;" first in Control();
// LkWantOk/_lkWant/LkFar/LkBack/LkEngage/_lkOwner/_lkOwnerP in the FOLLOW code; ReadTgt(_lkTgt); LkAmmo() = the script's ammo count (0-254, 255 unknown).
// Uses: _now, _tick, _mode, _firing, _aiOn, UnderFire, LockStale, _tgt, _aimErrDeg, _fireWinDeg, _batFrac, _gateWhy, _dynHome, _dynHomeT, MaxSpeed, ParseMode, Command, CfWant (CONFIG LINK), AiAssist.
// WEAKPOINT: .WPT (Aux -> Flight) feeds the BREACH block's _wp* slot, WpSend() carries its .WPF (Flight -> Aux); the relay itself is the Aux PB's WEAKPOINT RELAY block.
// v1.2 (flight_split): F2S flags bit 16 CFG WANT (CONFIG LINK CfWant; F2S goes out at least every 12 ticks), bit 17 AI ASSIST (runtime AiAssist: the Aux does the AI-block setup).
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
    _lkWpt = IGC.RegisterBroadcastListener(_lkK + ".WPT");   // best fleet weakpoint for my target (WEAKPOINT RELAY, Aux PB) -> the _wp* slot of the BREACH block
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
    int f = md | (_firing ? 8 : 0) | (UnderFire ? 16 : 0) | (LockStale ? 32 : 0) | (_aiOn ? 64 : 0) | (LkAmmo() < 0 ? 255 : Math.Min(254, LkAmmo())) << 8 | (CfWant ? 65536 : 0) | (AiAssist ? 131072 : 0);
    var t = _tgt;
    IGC.SendBroadcastMessage(_lkK + ".F2S", new MyTuple<int, long, Vector3D, Vector3D, float, string>(f, t != null ? t.Id : 0, t != null ? t.P(_now) : Vector3D.Zero,
        new Vector3D(_aimErrDeg, _fireWinDeg, _aBrake), (float)_batFrac, LkGate()), TransmissionDistance.CurrentConstruct);
}

// FLEET LINK stubs: the fleet channel is the Staff PB's (WaspStaffAI.cs); target shares go to it on .FLT, fleet targets come back on .TGT
bool FleetV(Vector3D v) { return Math.Abs(v.X) < 1e9 && Math.Abs(v.Y) < 1e9 && Math.Abs(v.Z) < 1e9; }
bool FleetOk(MyTuple<long, Vector3D, Vector3D, double, long> d) { return FleetV(d.Item2) && FleetV(d.Item3) && d.Item4 < 1e5 && !double.IsNaN(d.Item4); }
void FleetSend(MyTuple<long, Vector3D, Vector3D, double, long> tup) { if (LkAlive) IGC.SendBroadcastMessage(_lkK + ".FLT", tup, TransmissionDistance.CurrentConstruct); }
// <<< SPLIT LINK <<<
int LkAmmo() { return _sabots; }
double _fireWinDeg { get { return _winDeg; } }   // ammo for the Staff screen (0-254)
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

// SURVIVAL LINK hooks: own max fire range; resend the AI and gun state after the Staff PB held the ship (failover)
double SvRange() { return WeaponRange * RangeMargin; }
void SvResync() { _aiOn = !_aiOn; _firing = false; }
bool SvRecharging() { return SecondsToReady() > 1; }   // railgun capacitor still filling (power guard)

// >>> SURVIVAL LINK v1.1 (evade, jinks, survival advice from the Staff PB, docs/design/survivability.md) >>>
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
// run-gap line; (v1.1: the [Survival] values come from the Staff on .CFG: CONFIG LINK calls SvSet() for the S line); _guns.RemoveAll(SvNotGun) in GetBlocks; SvSee(e) first in Valid(); "_svNb++; _svMax = Math.Max(_svMax, drop);" in
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
// [Survival] values (the Staff writes the defaults and sends them on .CFG): CONFIG LINK's S line, tools/cfg_tables.json "shared" order; defaults = the Staff's
void SvSet() { SvLJ = B(); SvPG = B(); SvJa = N(); SvEv = N(); SvJm = N(); SvJM = N(); SvJr = N(); SvPh = N(); }
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
// <<< SURVIVAL LINK v1.1 <<<

// >>> CONFIG LINK v1.0 (Flight / Helm side of the config move, docs/design/flight_split.md 3, 4.1, 4.3, 6.1; tools/cl_block.cs, synced by tools/blocks_sync.py) >>>
// Final hosts: the 9 drone Flights and ShrikeAI. The PB never writes its own Custom Data any more: the Staff's CONFIG SERVER (tools/cs_block.cs) writes
// every default there, parses the values and sends them on .CFG; this block applies them and caches the string in Storage.
// Channel (TransmissionDistance.CurrentConstruct): listener CfKey() + ".CFG" (drones CfKey() = _lkK = "WSPL"+<Flight PB EntityId>; Helm "<Ship Tag>.").
//   .CFG  Staff -> Flight/Helm, string (exact format, the CONFIG SERVER builds the same):
//         line 0  "CF1\t<schema>\t<status>"      status: "ok", or (Longbow, Mantis) the weapon the Staff detected; kept in _cfX, shown after "cfg: Staff, "
//         line i  "<letter>\t<v1>\t<v2>...\n"    one line per section, values in tools/cfg_tables.json order (empty lines are skipped)
//         letters: M the model section (cfg_tables "sections" of the model, WITHOUT the "local" entries IGC Tag / Start Mode), K [Kamikaze Defence]
//         (all 20), A [AI Gate] (only the 4 "block":"core" entries: AiGateOn, AiWin, ScriptWin, AiHoldMax; the 4 setup values go to the Aux in SDK),
//         S [Survival] (all 8). Drone line order M K A S. Helm (4.3): one line per Helm section with its own letter (CfgIn switches on _cfV[0]), then K A.
//         Values: bools "1"/"0" (anything else reads false), doubles and ints ToString("R") (read with double.TryParse, current culture on both PBs;
//         ints are read as doubles and cast by the post-op), strings raw (no tab / newline). Values are sent RAW (as MyIni reads them); the post-ops
//         of cfg_tables.json ((int), Math.Max clamps, .Trim().ToUpperInvariant()) are applied here, in CfgIn / AgSet4 (all are idempotent).
//         schema = cfg_tables.json "version" + "/" + the value count of each line, in line order; drone example (Mantis) "1/84/20/4/8".
//         The Flight accepts a message only if the header schema equals its const CfSchema AND the schema rebuilt from the lines it got equals it too;
//         otherwise nothing is applied and the screen says "! config schema: update Flight and Staff together".
//   WANT: CfWant rides on drone F2S flags bit 16 (SPLIT LINK v1.2: F2S goes out at least every 12 ticks); Helm H2S bit 9 (HELMLINK 1.3). The Staff answers
//         a WANT within one F2S period, at most once per 30 ticks per key, re-reading the Flight's Custom Data at that moment ("reload" reaches both PBs).
//   AI ASSIST (runtime AiAssist) rides beside it: F2S bit 17 / H2S bit 10; the Aux (SDK bit 9) or StaffAI does the AI-block setup.
// Contract: value readers N() (double), B() (bool), T() (string) read the next value of the line being applied (_cfV, cursor _cfI, _cfV[0] = letter).
//   bool CfApply(string): schema check, then applies atomically line by line: K -> KzSet(), A -> AgSet4(), S -> SvSet() (in SURVIVAL LINK v1.1),
//   any other letter -> host CfgIn(). Returns false (nothing applied) on a mismatch. CfRead(): applies each .CFG, then clears CfWant, keeps the string
//   (CfSave) and sets the screen line _cfSt. string CfLoad(): applies the Storage cache at start (status "cfg: cached") and returns the host's own part
//   of Storage. string CfSave(): the suffix the host appends to its Storage in Save() ("\u0001" + the last applied .CFG). bool CfWant: true from start
//   and "reload" until a matching .CFG is applied.
// Host hooks: string CfKey(); const string CfSchema; void CfgIn() (model line(s), one statement per field in cfg_tables.json order with the post-ops,
//   generated from cfg_tables.json); a Helm without SURVIVAL LINK declares "void SvSet() { }". Calls: CfInit() after the link key exists (drones: after
//   LkInit()), "string st = CfLoad();" before the host parses Storage, CfRead() in the tick, "+ CfSave()" in Save(), "CfWant = true;" in "reload",
//   "\n" + _cfSt on the screen. Tag and Start Mode stay local (Get-only reads; the IGC channels are registered in the constructor).
// Sync: until a matching .CFG arrives the PB runs on its Storage cache, else on its field defaults ("cfg: defaults (no Staff)"); after an apply
//   "cfg: Staff". Runtime words (aim, breach, wp, range, split/focus, ai on|off) last until the next start or "reload"; the Staff never pushes config
//   unasked. Flight rebuilt: new key + WANT. Staff rebuilt: answers the next WANT. Run gap: nothing (the config is in memory).
IMyBroadcastListener _cfL; bool CfWant = true; string _cfS = "", _cfX = "", _cfSt = "cfg: defaults (no Staff)"; string[] _cfV; int _cfI;
double N() { double v; double.TryParse(_cfV[_cfI++], out v); return v; }
bool B() { return _cfV[_cfI++] == "1"; }
string T() { return _cfV[_cfI++]; }
void CfInit() { _cfL = IGC.RegisterBroadcastListener(CfKey() + ".CFG"); }
bool CfApply(string s)
{
    var L = s.Split('\n'); var h = L[0].Split('\t'); string c = "1";
    for (int i = 1; i < L.Length; i++) if (L[i] != "") c += "/" + (L[i].Split('\t').Length - 1);
    if (h.Length < 3 || h[0] != "CF1" || h[1] != CfSchema || c != CfSchema) return false;
    _cfX = h[2];
    for (int i = 1; i < L.Length; i++)
    {
        if (L[i] == "") continue;
        _cfV = L[i].Split('\t'); _cfI = 1;
        char k = _cfV[0][0];
        if (k == 'K') KzSet(); else if (k == 'A') AgSet4(); else if (k == 'S') SvSet(); else CfgIn();
    }
    return true;
}
void CfRead()
{
    while (_cfL.HasPendingMessage)
    {
        var m = _cfL.AcceptMessage(); string s = m.Data as string;
        if (s == null) continue;
        if (!CfApply(s)) { _cfSt = "! config schema: update Flight and Staff together"; continue; }
        CfWant = false; _cfS = s; _cfSt = "cfg: Staff" + (_cfX != "ok" && _cfX != "" ? ", " + _cfX : "");
    }
}
string CfLoad()
{
    var p = Storage.Split('\u0001');
    if (p.Length > 1 && CfApply(p[1])) { _cfS = p[1]; _cfSt = "cfg: cached"; }
    return p[0];
}
string CfSave() { return _cfS != "" ? "\u0001" + _cfS : ""; }
void KzSet()
{
    KzOn = B(); KzMaxRadius = N(); KzHorizon = N(); KzMiss = N(); KzMissPerKm = N(); KzMinClosing = N(); KzPriority = N(); KzMates = B(); KzBack = B();
    KzBackSpeed = N(); KzBackRange = N(); KzReact = N(); KzBreak = B(); KzBreakTime = N(); KzBreakSpeed = N(); KzBreakMiss = N(); KzRays = B();
    KzPack = N(); KzReserve = N(); KzShareOn = B();
}
void AgSet4() { AiGateOn = B(); AiWin = Math.Max(1, N()); ScriptWin = Math.Max(1, N()); AiHoldMax = Math.Max(AiWin, N()); }
// <<< CONFIG LINK v1.0 <<<

// >>> DAMAGE LINK v1.0 (Flight side of the damage-sensing move, docs/design/flight_split.md 3, 4.1, 6.2; tools/dl_block.cs, synced by tools/blocks_sync.py) >>>
// Final hosts: the 9 drone Flights (GnatSwarmAI since flight_split P2-G2: its .DMG comes from GnatStaffAI's GNAT HIT BEARING, after each 2-tick check with a
// drop, <= 30 Hz). The Staff's DAMAGE SENSE (tools/ds_block.cs) runs the
// slim scan and CheckDamage and sends the result; Evade and the jinks stay in the Flight and read _hits / _hitDir as before.
// Channel (TransmissionDistance.CurrentConstruct, key _lkK = "WSPL"+<Flight PB EntityId>):
//   .DMG  Staff -> Flight   MyTuple<int, double, Vector3D, double, int, double> = (hits, integ, hitDir, hitDirAge, blocksHit, maxDrop); hits is a
//         cumulative counter (CheckDamage's _hits), integ the summed integrity (-1 unknown), hitDirAge the age of hitDir at send time (s),
//         blocksHit / maxDrop of the checks since the previous message (capped age 99 s for hitDirAge). After each CheckDamage with a drop (<= 3 Hz),
//         else 1 Hz.
// Receiver: _integ = integ, _hitDir / _hitDirT = hitDir / now - hitDirAge; when hits rose while the link was alive: _hitAt = _now, _hits += the rise,
//   _svNb += blocksHit, _svMax = max(_svMax, maxDrop) (SURVIVAL LINK's per-check stats); then SvDamage() (SURVIVAL LINK) on every message.
// Sync: first message, or a counter that went down (a new Staff): copy the counter, no hit. .DMG silent for 3 s after it was heard: _hitAt = _now
//   once and _integ = -1 (the helm_split rule); the next message counts as a first one.
// Hooks: DlInit() after LkInit(); DlRead() after LkRead(). Uses: _now, _lkK, _hits, _hitAt, _hitDir, _hitDirT, _integ, _svNb, _svMax, SvDamage().
IMyBroadcastListener _dlL; double _dlT = -99; int _dlH = -1;
void DlInit() { _dlL = IGC.RegisterBroadcastListener(_lkK + ".DMG"); }
void DlRead()
{
    while (_dlL.HasPendingMessage)
    {
        var m = _dlL.AcceptMessage();
        if (!(m.Data is MyTuple<int, double, Vector3D, double, int, double>)) continue;
        var d = (MyTuple<int, double, Vector3D, double, int, double>)m.Data;
        if (_dlH >= 0 && d.Item1 > _dlH) { _hitAt = _now; _hits += d.Item1 - _dlH; _svNb += d.Item5; _svMax = Math.Max(_svMax, d.Item6); }
        _dlH = d.Item1; _dlT = _now; _integ = d.Item2;
        if (d.Item4 < 1e3) { _hitDir = d.Item3; _hitDirT = _now - d.Item4; }
        SvDamage();
    }
    if (_dlH >= 0 && _now - _dlT > 3) { _hitAt = _now; _integ = -1; _dlH = -1; }
}
// <<< DAMAGE LINK v1.0 <<<

// >>> SEARCH LINK v1.0 (Flight side of the search move, docs/design/flight_split.md 3, 4.1, 4.2, 6.3, 6.6; tools/sq_block.cs, synced by tools/blocks_sync.py) >>>
// Final hosts: the 8 drone Flights (not the Gnat under G2). The Aux PB's SEARCH block (tools/sc_block.cs) casts the auto-scan, the all-round close scan,
// the hit-look, the manual lock ray, the sensor poll and the KZ rays; this block tells it what to do and rebuilds the contacts it finds. The tracked
// target (MaintainLock, designators, Track, Lead) stays in the Flight.
// Channels (TransmissionDistance.CurrentConstruct, key _lkK = "WSPL"+<Flight PB EntityId>):
//   .SCS  Flight -> Aux   MyTuple<int, long, Vector3D, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>>
//         = (flags, tgtId, lostPos, lostVel, hitDir, p). Every 6 ticks + at once on a flag change (and after a run gap). Stale at the Aux after 30 ticks.
//         flags: bit0 AUTO (the host's old auto-scan condition, e.g. no target, AI off, AutoScan on, mode GUARD/HUNT/FOLLOW), bit1 OMNI (mode not IDLE
//         and CloseScan on), bit2 HARD (UnderFire or Alerted), bit3 has target, bit4 AI on, bit5 SENSORS, bit6 HITLOOK, bit7 KZRAYS (KzRays), bits 8-15
//         lock request counter (+1 per "lock" word), bit17 lock camera uses Forwardish (0.9) instead of 0.99. Bits 0, 5, 6 and 17 come from the host's
//         int SqF() (the 8: AUTO | 96; Gnat: AUTO (its own condition) | bit17, no sensor, no hit-look).
//         lostPos / lostVel: the dropped target's last state (NoteLost; the Aux extrapolates lostPos + lostVel * min(lost age, 10)).
//         p: a = (auto-scan range, scan cone in degrees, spiral N) from the host's SqA(); b = (close-scan range, 900 = hard-scan range, lock range);
//         c = (lost age, hit-dir age, lost radius) in s, s, m; d = (KzPack, KzReserve, 0).
//   .KZR  Flight -> Aux   MyTuple<long, Vector3D, Vector3D, double, double, int> = (id, pos, vel, radius, age, flags): one per threat of _kz (most urgent
//         first), <= 4 per KZ pass (6 Hz), only while threats exist (KzAssess calls KzRayStep() then). pos / vel are the track extrapolated to the send
//         time (Track.P / V), age = now - last sighting. flags: bit0 the Flight's target, bits 8-11 index, 12-15 count, 16-31 pass number (low 16 bits).
//   .SCN  Aux -> Flight   MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>> = (id, f, hit, vel, box(min, max),
//         ori(fwd, up)). f bits 0-7 entity type, 8-15 relationship, 16 has hit, 20-22 source (1 SCAN, 2 SENSOR, 3 KZ, 4 LOCK), 24-27 lock result (0 ok,
//         1 no forward camera, 2 nothing hit with the charge in hit.X). id 0 = heartbeat (every 30 ticks); any message counts as a heartbeat.
// Rebuild (4.2): new MyDetectedEntityInfo(id, "", type, hit?, MatrixD.CreateWorld(box centre, fwd, up), vel, rel, box, 1) (TimeStamp 1: 0 would read
//   IsEmpty()), then Valid(e) (FaSee, SvSee) and AddContact(e, "SCAN" / "SENSOR" / "KZ"). A LOCK result is not Valid()-filtered (as ManualLock was):
//   friendly -> "Lock: target is friendly", else it becomes _tgt. Fallback if the game rejects the constructor: AddContactRaw(...), about +0.3k.
// Sync: Flight run gap: SqGap() in the run-gap line sends .SCS at once and drops queued .SCN. Aux silent for 2 s: SqStatus() = "! Aux PB silent: camera
//   search off". Hooks: SqInit() after LkInit(); SqRead() after LkRead(); SqSend() after LkSend(); "lock" -> _sqLk++; SqStatus() on the screen; host
//   Vector3D SqA(), int SqF(). Uses: _now, _tick, _lkK, _tgt, _aiOn, _mode (IDLE), CloseScan, CloseScanRange, LockRange, UnderFire, Alerted, KzRays, KzPack,
//   KzReserve, _kz, _kzRay, _lostPos, _lostVel, _lostT, _lostRad, _hitDir, _hitDirT, _err, Valid, Friendly, AddContact.
IMyBroadcastListener _sqL; double _sqT = -99; int _sqF = -1, _sqLk;
void SqInit() { _sqL = IGC.RegisterBroadcastListener(_lkK + ".SCN"); }
void SqGap() { _sqF = -1; while (_sqL.HasPendingMessage) _sqL.AcceptMessage(); }
string SqStatus() { return _now - _sqT > 2 ? "\n! Aux PB silent: camera search off" : ""; }
void SqSend()
{
    int f = SqF() | (_mode != Mode.IDLE && CloseScan ? 2 : 0) | (UnderFire || Alerted ? 4 : 0) | (_tgt != null ? 8 : 0) | (_aiOn ? 16 : 0) | (KzRays ? 128 : 0) | (_sqLk & 255) << 8;
    if (f == _sqF && _tick % 6 != 1) return;
    _sqF = f;
    IGC.SendBroadcastMessage(_lkK + ".SCS", new MyTuple<int, long, Vector3D, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>>(f, _tgt != null ? _tgt.Id : 0,
        _lostPos, _lostVel, _hitDir, new MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>(SqA(), new Vector3D(CloseScanRange, 900, LockRange),
        new Vector3D(_now - _lostT, _now - _hitDirT, _lostRad), new Vector3D(KzPack, KzReserve, 0))), TransmissionDistance.CurrentConstruct);
}
void SqRead()
{
    while (_sqL.HasPendingMessage)
    {
        var m = _sqL.AcceptMessage();
        if (!(m.Data is MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>>)) continue;
        var d = (MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>>)m.Data;
        _sqT = _now;
        if (d.Item1 == 0) continue;
        int f = d.Item2, s = f >> 20 & 7, r = f >> 24 & 15;
        if (s == 4 && r > 0) { _err = r == 1 ? "No forward camera for lock" : "Lock: nothing hit (range " + d.Item3.X.ToString("0") + " m)"; continue; }
        var b = new BoundingBoxD(d.Item5.Item1, d.Item5.Item2);
        var e = new MyDetectedEntityInfo(d.Item1, "", (MyDetectedEntityType)(f & 255), (f & 65536) != 0 ? d.Item3 : (Vector3D?)null,
            MatrixD.CreateWorld(b.Center, d.Item6.Item1, d.Item6.Item2), (Vector3)d.Item4, (MyRelationsBetweenPlayerAndBlock)(f >> 8 & 255), b, 1);
        if (s == 4) { if (Friendly(e)) _err = "Lock: target is friendly"; else { _err = ""; _tgt = AddContact(e, "LOCK"); } }
        else if (Valid(e)) AddContact(e, s == 2 ? "SENSOR" : s == 3 ? "KZ" : "SCAN");
    }
}
// KAMIKAZE DEFENCE v1.1 hook (replaces KZ RAYS): the Aux casts the refresh and pack rays for these threats and returns the hits as source KZ
void KzRayStep()
{
    int n = Math.Min(4, _kz.Count); _kzRay++;
    for (int i = 0; i < n; i++)
    {
        var t = _kz[i].T;
        IGC.SendBroadcastMessage(_lkK + ".KZR", new MyTuple<long, Vector3D, Vector3D, double, double, int>(t.Id, t.P(_now), t.V(_now), t.Radius, _now - t.Seen,
            (t == _tgt ? 1 : 0) | i << 8 | n << 12 | (_kzRay & 65535) << 16), TransmissionDistance.CurrentConstruct);
    }
}
// <<< SEARCH LINK v1.0 <<<

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
        _ready = StartMode.Trim().ToUpperInvariant() == "READY"; _sentryAt = StartMode.Trim().ToUpperInvariant() == "SENTRY" ? 5 : -1; _mode = _ready ? Mode.GUARD : ParseMode(StartMode, Mode.IDLE);
        string st = CfLoad();
        if (st != "")
        {
            var p = st.Split(';');
            double x, y, z;
            if (p.Length >= 5 && double.TryParse(p[1], out x) && double.TryParse(p[2], out y) && double.TryParse(p[3], out z))
            { _mode = ParseMode(p[0], _mode); _ready = p.Length > 5 && p[5] == "1"; _sentry = p.Length > 6 && p[6] == "1"; _sentryAt = -1; _home = new Vector3D(x, y, z); _homeSet = p[4] == "1"; }
        }
    }
    catch (Exception ex) { CtorErr("storage", ex); }
    try
    {
        _lTgt = IGC.RegisterBroadcastListener(Tag + "_TGT");
        _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
        LkInit(); CfInit(); DlInit(); SqInit(); SvInit();
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
    Storage = string.Join(";", _mode.ToString(), _home.X.ToString("R"), _home.Y.ToString("R"), _home.Z.ToString("R"), _homeSet ? "1" : "0", _ready ? "1" : "0", _sentry ? "1" : "0") + CfSave();
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
    if (dt <= 0 || dt > 0.5) { if (dt > 0.5 && _tick > 3) { _svSentT = -99; SqGap(); _aiOn = !_aiOn; _firing = false; } dt = 1.0 / 60.0; }   // run gap (PB was off / halted): the Staff watchdog may have switched the AI blocks off, so force the next pass to send the AI and gun state again (a zero dt at start-up or after a terminal run in the same tick is not a gap: flipping _aiOn then left the AI blocks off)
    _now += dt; _tick++;
    if (PgHold()) return;            // PRINTER GUARD: still welded to a printer, fly nothing

    if (_tick % 600 == 0) GetBlocks();
    CheckGrid();
    if (_rc == null || !_rc.IsFunctional || !_rc.IsSameConstructAs(Me)) { _err = "No working Remote Control / cockpit"; if (!_rcOut || _tick % 60 == 0) { _rcOut = true; GetBlocks(); Display(); Release(); } return; }   // (not on this construct = a severed piece: same as no controller; look for another one at 1 Hz)
    _rcOut = false;

    ReadIGC(); LkRead(); CfRead(); DlRead(); SqRead(); SvRead(); ReadSwarm();
    if (_tick % 6 == 3) BroadcastStatus();
    if (_sentryAt > 0 && (LkBurn || _mode != Mode.IDLE)) _sentryAt = -1;   // launched or commanded first
    else if (_sentryAt > 0 && _now >= _sentryAt) { _sentryAt = -1; if (!SentryDocked()) Command("sentry"); }   // Start Mode SENTRY, after the 5 s grace: hold this spot
    if (_sentry && _tick % 30 == 11 && SentryDocked()) { SetMode(Mode.IDLE); _err = "Docked: SENTRY stands down (IDLE)"; }   // never fly a printer / carrier: its Staff PB hands over [Launch] Mode after take-off
    if (_tick % 6 == 0) PollDesignators();
    if (_tick % 10 == 0) { PruneAndSelect(); PowerAndHealth(); }
    UpdateGuns();
    MaintainLock(0.25);
    if (ShareTargets && _tgt != null && _tgt.Src != "IGC" && _now - _tgt.Seen < 1.0 && _tick % 30 == 0 && !Hot()) ShareTarget();

    if (!LkBurn && !SvYield && !AiStep()) Control(dt);
    LkSend(); SqSend(); SvSend();
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
        case "follow": case "escort": SetMode(Mode.FOLLOW); break;
        case "satellite": case "sat": case "orbit": SetMode(Mode.FOLLOW); break;
        case "hunt": SetMode(Mode.HUNT); break;
        case "assist": SetMode(Mode.ASSIST); break;
        case "rtb": SetMode(Mode.RTB); break;
        case "idle": case "stop": SetMode(Mode.IDLE); break;
        case "lock": _sqLk++; break;
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
        case "range": double r; if (w.Length > 1 && double.TryParse(w[1], out r)) ForcedRange = Math.Max(0, r); break;
        case "reload": CfWant = true; GetBlocks(); break;
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

void SetMode(Mode m) { _mode = m; _ready = false; _sentry = false; _sentryAt = -1; if (m == Mode.IDLE || m == Mode.ASSIST) Release(); }

// =====================================================================
void LoadConfig()   // Tag and Start Mode only, Get-only (the IGC channels are registered in the constructor); the rest comes from the Staff (CONFIG LINK)
{
    MyIniParseResult res;
    if (!_ini.TryParse(Me.CustomData, out res)) { _err = "CustomData parse error line " + res.LineNo; return; }
    Tag = _ini.Get("Mantis", "IGC Tag").ToString(Tag); StartMode = _ini.Get("Mantis", "Start Mode (GUARD/HUNT/ASSIST/IDLE)").ToString(StartMode);
}
string CfKey() { return _lkK; }
Vector3D SqA() { return new Vector3D(WeaponRange * 1.1, ScanCone, 150); }   // SEARCH LINK: auto-scan range, cone, spiral N
int SqF() { return (_tgt == null && !_aiOn && AutoScan && (_mode == Mode.GUARD || _mode == Mode.HUNT || _mode == Mode.FOLLOW) ? 1 : 0) | 96; }   // SEARCH LINK: AUTO, SENSORS, HITLOOK
// CONFIG LINK host hooks (generated from tools/cfg_tables.json, flight_split P1-F): the [Mantis] values of the .CFG M line, in table order
const string CfSchema = "1/84/20/4/8";
void CfgIn()
{
    AutoWeapon = B();
    ProjSpeed = N();
    WeaponRange = N();
    ReloadTime = N();
    ShotDelayCfg = N();
    GravityDrop = B();
    InheritVel = B();
    HitFraction = N();
    VelErr = N();
    DodgeSmall = N();
    DodgeLarge = N();
    DodgeChar = N();
    RangeMargin = N();
    MinEngage = N();
    EngageCap = N();
    EngageCapHuge = N();
    UseGatling = B();
    GatSpeed = N();
    GatRange = N();
    GatTol = N();
    ForcedRange = N();
    GuardRadius = N();
    SentryRange = N();
    MaxSpeed = N();
    CruiseSpeed = N();
    DriftSpeed = N();
    RushOrbit = N();
    RushRange = N();
    DodgeRange = N();
    RushSpeed = N();
    JinkSpeed = N();
    LockRange = N();
    AutoScan = B();
    CloseScan = B();
    CloseScanRange = N();
    ScanCone = N();
    TargetLarge = B();
    TargetSmall = B();
    TargetChars = B();
    TargetNeutral = B();
    ShareTargets = B();
    FriendCheck = B();
    RtbBattery = N();
    AimKp = N();
    AimKd = N();
    MaxTurn = N();
    SettleTicks = (int)N();
    LeadScale = N();
    LeadMargin = N();
    PreferRange = N();
    AimWindowMax = N();
    AimWindowMaxLarge = N();
    CalmAccel = N();
    CalmWait = N();
    AccFastW = N();
    AccFastK = N();
    FreeFar = B();
    FreeFarDist = N();
    Coast = B();
    ReadyLead = N();
    RetryAfter = N();
    HoldRailInSearch = B();
    HoldRailMaxS = N();
    FireRangeFactor = N();
    FireRangeSlack = N();
    UseTargetAccel = B();
    SplitFocus = B();
    DisabledGrace = N();
    EvadeSpeed = N();
    AimMode = T().Trim().ToUpperInvariant();
    BreachAuto = B();
    BreachMinRadius = N();
    BreachStall = N();
    WpOn = B();
    WpShare = B();
    WpCone = N();
    WpHold = N();
    AiAssist = B();
    AiConfirmRange = N();
    AiConfirmLong = N();
    FireStaleMax = N();
    SepRange = N();
    SepSpeed = N();
    LooseFar = B();
}

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
    _guns.RemoveAll(SvNotGun); SvRails(!_svRx);
    int wc = _guns.Count > 0 ? _guns[0].BlockDefinition.SubtypeId.GetHashCode() : 0;
    if (wc != _wCls) { CfWant = true; _wCls = wc; }   // primary gun type changed (e.g. the railgun lost): the Staff's weapon auto-detect runs again on the next .CFG
    _gats.Clear();
    for (int gi = _guns.Count - 1; gi >= 0; gi--) if (_guns[gi] is IMySmallGatlingGun) { _gats.Insert(0, _guns[gi]); _guns.RemoveAt(gi); }
    if (_guns.Count == 0 && _gats.Count > 0) { _guns.AddRange(_gats); _gats.Clear(); }   // railgun lost: gatlings become the primary weapon
    GridTerminalSystem.GetBlocksOfType(_cams, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_turrets, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_ctcs, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_bats, b => b.IsSameConstructAs(Me));
    foreach (var c in _cams) c.EnableRaycast = true;
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


// =====================================================================
//  TARGETS
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
    ReadTgt(_lkTgt);
    while (_lCmd.HasPendingMessage) { var msg = _lCmd.AcceptMessage(); if (msg.Data is string) Command((string)msg.Data); }
    while (_lHome.HasPendingMessage) { var msg = _lHome.AcceptMessage(); if (msg.Data is Vector3D) { _dynHome = (Vector3D)msg.Data; _dynHomeT = _now; } }
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
        if (_sentry && _mode == Mode.GUARD) { if (t != _tgt && Vector3D.DistanceSquared(p, me) > SentryRange * SentryRange && !KzLive(t.Id)) continue; }   // SENTRY: wake on anything inside Sentry Range, then chase it anywhere
        else if (_mode == Mode.GUARD && Vector3D.Distance(p, home) > GuardRadius && !KzLive(t.Id)) continue;
        if (_mode == Mode.FOLLOW && Vector3D.DistanceSquared(p, home) > LkEngage * LkEngage) continue;   // FOLLOW: only threats near the owner
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
    if ((_ready || _sentry) && _mode == Mode.GUARD) return _home;
    if (_mode == Mode.FOLLOW && _lkOwner) return _lkOwnerP;   // FOLLOW: the owner (ship link or on foot), see the FOLLOW block
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
    if (ForcedRange > 0) return Math.Min(ForcedRange, WeaponRange * RangeMargin);
    if (EngageCapHuge > 0 && t.Radius > HugeTargetRadius) return Math.Min(cap, EngageCapHuge);
    if (EngageCap > 0) cap = Math.Min(cap, EngageCap);
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
        if (_now < _needMoveUntil) _powerScale = 1;
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
    if (_mode == Mode.GUARD || _mode == Mode.HUNT || _mode == Mode.FOLLOW)
    {
        if (cap > 0 && _batFrac < RtbBattery) { _err = "Low battery - RTB"; _mode = Mode.RTB; }
        else if (working == 0 || (_hadAmmo && ammo <= 0)) { _err = "No weapon/ammo - RTB"; _mode = Mode.RTB; }
    }
    SvPower(max > 0 ? cur / max : 0, SvRecharging());   // railgun power guard
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
        }
        else if (s.SawShoot && !s.B.IsShooting)
        {   // the gun was busy and is done: the slug is away (no ammo is used in a creative world)
            // (the capacitor is empty from this moment and full again ReloadTime later: try then, not half a second later)
            _shootFlag = true;
            s.PendingAt = -1; s.LastShot = _now; s.ReadyAt = _now + ReloadTime - ReadyLead; _shots++;
        }
        else if (_shootFlag && !s.SawShoot && _now - s.PendingAt > 0.25)
        {   // this gun does report IsShooting for an accepted shot and did not: the capacitor was not full yet - ask again very soon
            s.PendingAt = -1; s.ReadyAt = _now + RetryAfter;
        }
        else if (_now - s.PendingAt > ShotDelay + 1.0)
        {   // no ammo drop and the gun never reported busy: still charging - retry in 2 s
            s.PendingAt = -1; s.ReadyAt = _now + 2;
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
    if (LkBurn) return;   // take-off burn: the Staff PB flies (LAUNCH block)   // automatic take-off after the release from a printer (LAUNCH block)
    var vels = _rc.GetShipVelocities();
    Vector3D myVel = vels.LinearVelocity, grav = _rc.GetNaturalGravity(), me = _rc.GetPosition();
    _myVel = myVel;
    _aimErrDeg = 0; _winDeg = 0;
    if (_mode == Mode.IDLE) { SetFire(false); return; }
    Vector3D up = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : Vector3D.Zero;
    if (_tgt != null) _engage = SvStand(EngageRange(_tgt), MinEngage, WeaponRange * RangeMargin - 30);
    double toReady = _slowWeapon ? SecondsToReady() : 0;
    bool pending = false; foreach (var gs in _gunState) if (gs.PendingAt >= 0) pending = true;
    bool aimingPhase = _tgt != null && (pending || (toReady < 3 && (!UnderFire || toReady < 0.5)));   // dodge when hit unless the shot is ready

    // ---------- aiming & fire control ----------
    bool haveAim = false;
    if (_tgt != null && _guns.Count > 0 && (_mode != Mode.RTB || SvFightBack))
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
            _tvD = (aimPoint - _rc.GetPosition()).Length();
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
    double aBrake = SvBrake(MinAccel(mass, grav) * 0.5); _aBrake = aBrake;
    Vector3D want, home = Home(), lat = Vector3D.Zero; double threat = 0;
    if (_mode != Mode.RTB && _now - _dynHomeT < 5 && home == _dynHome) { Vector3D o = me - home; home += Vector3D.Normalize(o.LengthSquared() > 1 ? o : _rc.WorldMatrix.Up) * 65; }   // the carrier link's HOME is the ship's centre: hold on a sphere round it, on my side, never inside the hull
    if (_mode == Mode.RTB || _tgt == null)
    {
        Vector3D d = SvHome(home) - me; double dist = d.Length();
        double v = Math.Min(Math.Min(SvViaOn ? MaxSpeed : CruiseSpeed, dist * 0.5), Math.Sqrt(2 * aBrake * dist));
        Vector3D sep = Separation(me);
        if (_mode != Mode.RTB && _tgt == null && sep.LengthSquared() > 1 && Home() == _home) _home += sep * dt;   // idle: the wing spreads out (the home point moves away from the mates, or it pulls the drone back to its paste spot; from the Ballista Helm)
        want = (dist > 0.5 ? d / dist * v : Vector3D.Zero) + Evade() + sep;
        if (_mode == Mode.FOLLOW && LkWantOk) want = _lkWant + ((_lkFl & 2) != 0 ? Vector3D.Zero : Separation(me)) + Evade();   // own slot by the owner, matching its velocity
        if (_mode == Mode.RTB && dist < 25 && myVel.Length() < 2 && !SvViaOn) { SetMode(Mode.IDLE); _err = "Home - idle"; return; }
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
        if (!aimingPhase) evade = SvJink(dir, dist, aBrake / 0.5, JinkSpeed, true);   // reloading: be hard to hit (lead-breaking jinks, SURVIVAL LINK)
        // ANTI-RUSH: a big ship (radius >= 25 m) closing at 25+ m/s, or running 40+ m/s straight at us, within Rush Detect Range. Out-running beats out-turning it: flee along its heading
        // at Rush Flee Speed (the game's own cap, so a 100 m/s charger never gains). If it still gets inside Rush Dodge Range, burst across its path
        // (its heading follows slowly), every drone in its own direction round the charger's heading, so it can only turn on one of them.
        double closing = -Vector3D.Dot(tv - myVel, dir);
        lat = SvOrbit(dir, tan, DriftSpeed);
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
        Vector3D gv; if (SvGather(me, dist, out gv)) want = gv + Evade() + Separation(me);   // bounded attack-together window (Staff PB)
        if (_mode == Mode.GUARD && !_sentry && Vector3D.Distance(me, home) > GuardRadius * 1.3) want = Vector3D.Normalize(home - me) * CruiseSpeed;
        if (_mode == Mode.FOLLOW && LkFar(me)) want = LkBack(me);   // leash: drop the fight, back to the owner
    }
    double cap = (_tgt != null && _mode != Mode.RTB) || _mode == Mode.FOLLOW ? MaxSpeed : CruiseSpeed;
    cap = SvCap(SvViaOn ? Math.Max(cap, MaxSpeed) : cap);   // limp cap; the retreat via point is flown at combat speed
    want = KzWant(want, me, cap);   // KAMIKAZE DEFENCE: back away / break sideways
    want = SvWant(want);   // missile dodge (Staff PB)
    want = FaWant(want, me);   // FRIENDLY AVOIDANCE: keep clear of non-enemy grids and rocks
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
    travel = tt * s;
    point = shooter + p;
    double len = p.Length();
    if (len < 1) { dir = Vector3D.Zero; return false; }
    dir = p / len;
    return true;
}

// Wingmates repel each other (all fly the same orbit at the same speed and would otherwise end up in contact). Uses the
// positions the hive already broadcasts (_SWARM). Large grids keep a wider berth.
double SepRange = 80, SepSpeed = 25;
bool LooseFar = true;   // Loose Fire Beyond Standoff: while still closing (range > standoff + 100 m) the fire window is the whole silhouette, not the 3 m hull point (from the Ballista Helm, docs/retrofit.md)
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
    TvAim(ref desiredFwd, ref desiredUp, ref ffWorld);
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
    _tvN = forceWorld;
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
bool _gatFiring = false; double _gatErrDeg = 0, _gatWinDeg = 0;
void SetGat(bool on)
{
    if (on == _gatFiring && _tick % 30 != 0) return;   // re-send the state twice a second (the AI blocks may have switched the guns on or off)
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
    Vector3D c0 = _tgt.P(_now) - gp, off = AimOffset(_tgt), tv = _tgt.V(_now), ta = UseTargetAccel ? _tgt.EA() : Vector3D.Zero;
    double h = LeadHold(_tgt), rl = c0.Length();
    Vector3D los = rl > 1 ? c0 / rl : Vector3D.Zero;
    Vector3D rp = c0 + LeadOffset(_tgt, off, los, rl / Math.Max(GatSpeed, 100)), rv = InheritVel ? tv - myVel : tv;
    double s = Math.Max(GatSpeed, 100);
    double a = rv.LengthSquared() - s * s, b = 2 * Vector3D.Dot(rp, rv), c = rp.LengthSquared(), tt;
    double disc = b * b - 4 * a * c;
    if (Math.Abs(a) < 1e-6) tt = b != 0 ? -c / b : rp.Length() / s;
    else if (disc < 0) tt = rp.Length() / s;
    else { double sq = Math.Sqrt(disc), t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a); tt = (t1 > 0 && (t1 < t2 || t2 <= 0)) ? t1 : t2; }
    if (tt <= 0 || double.IsNaN(tt)) tt = rp.Length() / s;
    Vector3D p = rp;
    for (int i = 0; i < 4; i++) { p = c0 + LeadOffset(_tgt, off, los, tt) + (rv * tt + ta * LeadK(tt, h)) * LeadScale; tt = p.Length() / s; }
    double dist = p.Length();
    if (dist < 1 || double.IsNaN(dist)) { SetGat(false); return; }
    _gatErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(gunFwd, p / dist), -1, 1)) * 57.2958;
    double aimR = AimActive(_tgt) && !(LooseFar && dist > _engage + 100) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6;
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
    if (on == _firing && _tick % 30 != 0) return;   // re-send the state twice a second (the AI blocks may have switched the guns on or off)
    _firing = on;
    foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off");
}

void ReleaseGyros() { foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = g.Yaw = g.Roll = 0; } _havePrev = false; }

void Release()
{
    SvRails(true);
    if (LkBurn || SvYield) return;   // the Staff PB flies (take-off burn, limp-home): never zero its overrides
    SetGunsEnabled(true); HoldRail(false);   // (the friendly-fire guard of the AI phase may have switched them off)
    ReleaseGyros();
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
    SetFire(false);
}

// =====================================================================
void Display()
{
    string aim = "";
    if (_tgt != null) aim = "\nAim: " + AimLabel(_tgt) + " (" + _tgt.Hull.Count + " hull pts)" + AimInfo();
    string s = "FLIGHT " + VER + "  " + (_sentry && _mode == Mode.GUARD ? "SENTRY" : _mode.ToString()) + "  tgt " + (_tgt != null ? _tgt.Src : "-") + "  staff " + (LkAlive ? "ok" : "none") + aim + SvStatus() + "\n" + _cfSt + SqStatus() + (_fc != "" ? "\n1st contact " + _fc + " " + (_now - _fcT).ToString("0") + " s ago" : "") + (_err != ""? "\n! " + _err : "") + (_crash != "" ? "\n! Script error: " + _crash : "");
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}

// =====================================================================
//  SENSORS (360 deg, ~50 m) + idle sweep
Vector3D _sweepUp; bool _sweepUpSet = false;

// (sensor setup: the Staff PB's STAFF SEARCH block writes the sensor properties)


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
bool Forwardish(IMyCameraBlock c) { return Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) > 0.9; }
// Under fire (or on a hive alarm) the side cameras reach HardScanRange: the shooter is usually a turret or gatling at up to 800 m.
// The forward cameras stay out of it while the long auto-scan, the AI confirm rays or a lock need them: short close-scan rays used to
// drain them below the auto-scan's range, so a drone under fire from 700 m never cast a long ray (in-game report 2026-10-13).
// A point rng metres out, inside 35 deg of the side that was hit (golden-angle spiral), that camera c can see; false if none of 4 tries is in its cone.

// =====================================================================
//  v1.8: TARGET STATUS, DAMAGE SENSING, SPLIT FOCUS, ASSIST
Dictionary<long, double> _ignore = new Dictionary<long, double>();
Dictionary<long, int> _share = new Dictionary<long, int>(), _count = new Dictionary<long, int>();
List<Track> _live = new List<Track>();
Vector3D _hitDir; double _hitDirT = -99;
double _integ = -1, _hitAt = -99; int _hits = 0;
Vector3D _assist; double _assistT = -99;
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
    ApplyRail();
    foreach (var g in _gats) if (g.IsFunctional) g.Enabled = on;
}
// While the AI blocks search (nothing locked) they pull the trigger at whatever they find, at 1+ km, and the first slug of the fight (the
// capacitor starts full) is gone before the script has a lock. Until the script has fired its own first slug the railgun is switched off in
// that phase, back on for the script's aimed shot (later in the fight the AI's shots at what it found are welcome: 2xLongbow S vs 3xGnat 20 %
// with this limit, 10 % with the railgun off whenever nothing is locked, 22 % without).
bool HoldRailInSearch = false, _railHeld = false;
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
        _firing = false; _gatFiring = false;                       // guns' state is unknown after any hand-over: SetFire sends it again
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
bool AiModeOk() { return _mode == Mode.GUARD || _mode == Mode.HUNT; }

// =====================================================================
//  v1.8: PRINTER SUPPORT
//  * A drone printed while merged to a printer shares the printer's grid. Blocks are re-scanned
//    whenever this PB's grid changes (merged / released), so it never drives the printer's thrusters.
//  * "<Tag>_JOIN" from a printer: an IDLE drone (fresh off the printer) starts working; drones that
//    are already busy ignore it.
IMyCubeGrid _myGrid;
void CheckGrid() { if (Me.CubeGrid != _myGrid) { _myGrid = Me.CubeGrid; GetBlocks(); } }
