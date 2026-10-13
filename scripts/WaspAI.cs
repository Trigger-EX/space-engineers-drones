// =====================================================================
//  WASP DRONE AI  v2.5-wasp  - rocket fire control, lead solver & flight
//  WASP DRONE AI  v2.3-wasp  - rocket fire control, lead solver & flight
//  For Space Engineers (vanilla, small grid). Paste into a Programmable Block.
//  Derived from HORNET DRONE AI v1.8: same tracking, hive (IGC), AI hand-off,
//  damage sensing, printer join; the weapon model and fire control are new.
// ---------------------------------------------------------------------
//  WHAT CHANGED FOR ROCKETS (SmallMissileLauncher: 1 shot/s, 500 explosive
//  damage, 4 m blast, 800 m max trajectory, 4 rockets per launcher)
//   * A rocket is SLOW: it leaves at 100 m/s, accelerates at 600 m/s^2 and
//     tops out at 200 m/s after 1/6 s (25 m). It does NOT inherit the
//     launcher's velocity (Keen forum), so the lead solver integrates the
//     rocket's real speed curve (RktDist/RktTime) against the target's
//     predicted track instead of using one constant bullet speed.
//   * Flight time is long (1.5 s at 300 m, 4 s at 800 m), so the drone sits
//     closer (Standoff 250 m, v2.1), and refuses to fire at a target whose
//     unmodelled manoeuvring over the flight time exceeds its size
//     ("target jinking, miss N m" on the screen).
//   * Rockets are precious (8 per drone) and cannot be recalled: single
//     shots (ShootOnce) in short salvos with a pause to see what happened,
//     fewer and surer shots once the last rockets are left, RTB when empty.
//   * Self/friendly damage: never launch at an intercept closer than
//     SafeRange (blast 4 m); hold fire when a hive mate sits near the
//     flight line or the predicted blast; the camera line-of-fire check is
//     made at the moment of each launch.
//
//  v2.1 (sim audit 1; details in docs/script-fix-notes.md):
//   * AI hand-over: confirm rays down the boresight + a spiral (a ship is only 0.2-0.8 deg wide), one long (2600 m) and short rays
//   * lock upkeep: cheaper rays at known hull points, ghost target 8 s (acceleration trusted 1 s), widening search ellipse, loss sharing
//   * hull-gap fix: a ray that misses the predicted centre is retried at the centre before the search widens
//   * debris / one-block wrecks count as finished (they split off as their own grid and held the guns forever)
//   * stale tracks are not fired on (Max Track Age To Fire), wingmate separation, evasive jink 35-40 m/s every 1-2 s
//   * target feeds from other fleets ("Listen To IGC Tags")
//   * rockets: engage at <= 420 m / 2.0 s of flight (hit rate 31 -> 62 % in the sim), salvo pause 0.8 s, mates extrapolated to
//     the rocket's arrival, fire window shrinks for wrecks of a few blocks (a rocket only detonates on a direct hit)
//   * AI confirm rays aim for the ROCKET lead point (the AI leads rockets by v/150 rad, up to 4 deg)
//
//  v2.3: automatic take-off (LAUNCH block, docs/launch-protocol.md): the drone leaves a printer by itself when the merge pair opens;
//   _JOIN / FLEET join / Armed in Custom Data are only extra triggers. Old ReadJoin/Docked removed.
//  v2.2 (round 2, "Round 2: wasp-lancer" in docs/script-fix-notes.md):
//   * rocket stock unknown/unlimited (creative world, empty launcher inventories): engage from 350 m out to 650 m / 3.5 s of flight instead of
//     250 / 420 m / 2.0 s (sim, locked duels: wins 34.8 -> 43.6 %; teams 30 -> 40 %). With a counted stock (survival) the v2.1 numbers stay.
//   * HUNT search patrol + AI re-pick from the Harrier v2.2 (own direction and radius per drone); 6 of every 8 AI confirm rays go straight
//     down the boresight (more locks: 43 -> 52 % of the ships ever hold a target)
//   * optional "Dash Along AI Heading" (off): measured +4 points in 1v1, -11 in teams (see the notes)
//   * config keys that contain "=" were renamed (MyIni splits there)
//
//  v2.5 (LEAD v1.0, shared block, docs/lead-v1.md): the lead solver predicts the target with its freshest acceleration, held only
//   for as long as its manoeuvres usually last (an AI ship on evasive flips its strafe every 2-5 s); the hull aim point counts only
//   across the line of fire, turns with the target's spin, and keeps "Aim Margin" x the predicted miss of known hull around it
//   Rockets: modelled leaving with the ship's velocity + launch speed, accelerating along the barrel with the speed capped (they curve
//   when fired from a moving ship, Keen bug tracker); "Rocket Velocity Model" CAPPED / NONE (the v2.3 model) / FULL
//
//  COMMANDS (PB argument, toolbar, or IGC "<Tag>_CMD")
//   guard    hold home point, engage anything inside GuardRadius
//   follow   stay with the owner: a ship running a Mothership link, or you on foot near the drone ("escort" works too)
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

const string VER = "2.3-wasp+launch";

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
IMyBroadcastListener _lTgt, _lCmd, _lHome;
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
string _flAllowed = "hunt,attack,guard,follow,escort,form,assist,rtb,idle,stop,launch,abort,split,focus,next,unlock,aialert";
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

// >>> AI GATE v1.0 (shared block: identical in every drone script that uses AI-block search, synced by tools/ai_gate_sync.py) >>>
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
// Own [AI Gate] section in Custom Data (written on first run, "reload" re-reads it).
// Hooks: AiGateLoad() after FleetLoad(true); AiGateFind(_ai) at the end of FindAi(); want = AiGate(want) right before
// "if (want != _aiOn)" in AiStep(); AiGateStatus() in the status text.
bool AiGateOn = true, AiBehaviorSwitch = true;
double AiWin = 6, ScriptWin = 8, AiHoldMax = 60;
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
    if (!ini.ContainsKey(S, "AI Window (s)")) ini.Set(S, "AI Window (s)", AiWin);
    if (!ini.ContainsKey(S, "Script Search Window (s)")) ini.Set(S, "Script Search Window (s)", ScriptWin);
    if (!ini.ContainsKey(S, "Max AI Hold (s)")) ini.Set(S, "Max AI Hold (s)", AiHoldMax);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    AiGateOn = ini.Get(S, "Time-share With Script Search").ToBoolean(AiGateOn);
    AiBehaviorSwitch = ini.Get(S, "Switch On AI Behavior").ToBoolean(AiBehaviorSwitch);
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
        _agProps.Clear(); b.GetProperties(_agProps);
        ITerminalProperty<bool> sw = null;
        foreach (var p in _agProps)
        {
            string id = p.Id.ToLowerInvariant();
            if (!id.Contains("behavio")) continue;
            var bp = p.As<bool>();
            if (bp != null && (sw == null || id.Contains("activ"))) sw = bp;
        }
        if (sw == null) continue;
        n++;
        if (!sw.GetValue(b)) sw.SetValue(b, true);
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

// >>> FOLLOW v1.0 (shared block: identical in every scripted combat drone, synced by tools/follow_sync.py) >>>
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
// Own [Follow] section in Custom Data (written on first run, "reload" re-reads it).
// Hooks: FollowLoad() after AiGateLoad(); FollowStep() every tick after ReadIGC(); FollowSensed(e) for each sensor detection and
// "Detect Owner" = FoFoot, "Detect Players" = TargetChars || FoFoot in ConfigureSensors(); first line of Home():
// "if (_mode == Mode.FOLLOW && FollowKnown()) return FollowPos();"; the target pick skips targets farther than FoEngage from Home();
// idle translation "want = FollowWant(me, aBrake, Separation(me)) + Evade();"; with a target
// "if (_mode == Mode.FOLLOW && FollowFar(me)) want = FollowBack(me);"; speed cap MaxSpeed in FOLLOW; FollowStatus() in the status.
double FoDist = 60, FoFootDist = 20, FoRadius = 25, FoEngage = 800, FoLeash = 1200, FoCoast = 3;
bool FoFoot = true;
Vector3D _foP, _foV, _foLinkP, _foLinkV, _foFwd, _foUp;
double _foT = -99, _foLinkT = -99, _foFootT = -99, _foR = 0, _foRT = -99, _foPhase = 0, _foAcc = 0;
bool _foFrame = false, _foOnFoot = false;
IMyBroadcastListener _foLR1, _foLR2; string _foKey = "";

void FollowLoad()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    const string S = "Follow";
    if (!ini.ContainsKey(S, "Slot Distance")) ini.Set(S, "Slot Distance", FoDist);
    if (!ini.ContainsKey(S, "On Foot Distance")) ini.Set(S, "On Foot Distance", FoFootDist);
    if (!ini.ContainsKey(S, "Owner Radius")) ini.Set(S, "Owner Radius", FoRadius);
    if (!ini.ContainsKey(S, "Engage Radius")) ini.Set(S, "Engage Radius", FoEngage);
    if (!ini.ContainsKey(S, "Leash")) ini.Set(S, "Leash", FoLeash);
    if (!ini.ContainsKey(S, "Coast After Link Loss (s)")) ini.Set(S, "Coast After Link Loss (s)", FoCoast);
    if (!ini.ContainsKey(S, "Follow Owner On Foot")) ini.Set(S, "Follow Owner On Foot", FoFoot);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    FoDist = Math.Max(10, ini.Get(S, "Slot Distance").ToDouble(FoDist));
    FoFootDist = Math.Max(5, ini.Get(S, "On Foot Distance").ToDouble(FoFootDist));
    FoRadius = Math.Max(0, ini.Get(S, "Owner Radius").ToDouble(FoRadius));
    FoEngage = Math.Max(50, ini.Get(S, "Engage Radius").ToDouble(FoEngage));
    FoLeash = Math.Max(FoEngage, ini.Get(S, "Leash").ToDouble(FoLeash));
    FoCoast = Math.Max(0, ini.Get(S, "Coast After Link Loss (s)").ToDouble(FoCoast));
    FoFoot = ini.Get(S, "Follow Owner On Foot").ToBoolean(FoFoot);
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

void FoSet(Vector3D p, Vector3D v, bool foot) { _foP = p; _foV = v; _foT = _now; _foOnFoot = foot; }

void FollowStep()
{
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
    {   // start with the drone "behind" the owner: no slot on the far side of the hull
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
    double th = grav ? Math.PI * (k + 0.15 + 0.7 * _foPhase) / n : Math.PI * 0.5 + 2 * Math.PI * (k + 0.7 * _foPhase) / n;
    double gap = Math.Max(SepRange * 1.1, 30) / (2 * Math.Sin((grav ? Math.PI * 0.5 : Math.PI) / Math.Max(n, 2)));
    _foSlotR = _foOnFoot ? Math.Min(40, Math.Max(FoFootDist, n * 4)) : Math.Max(Math.Max(FoDist, FollowRadius() + 30), n > 1 ? gap / 0.866 : 0);
    if (!_foOnFoot) _foSlotR *= 1 + MathHelper.Clamp((_foAcc - 3) / 6, 0, 1);   // the owner throws itself around: up to twice the room
    Vector3D right = Vector3D.Cross(_foFwd, _foUp);
    Vector3D side = right * Math.Cos(th) + _foUp * Math.Sin(th);
    return FollowPos() + (-_foFwd * 0.5 + side * 0.866) * _foSlotR;   // 60 deg off the owner's tail
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
    Vector3D w = FollowVel() + (dist > 0.5 ? d / dist * v : Vector3D.Zero);
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
    string s = "Follow : " + (_foOnFoot ? "on foot" : "ship") + (age > 1.5 ? " LOST " + age.ToString("F0") + " s" : "") + "  " + FollowVel().Length().ToString("F0") + " m/s";
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
    return _lnArmed ? "Launch: armed\n" : "";
}
// <<< LAUNCH <<<

public Program()
{
    for (int i = 0; i < 6; i++) _thrDir[i] = new List<IMyThrust>();
    LoadConfig();
    FleetLoad(true); LaunchLoad(); AiGateLoad(); FollowLoad();
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
        case "ASSIST": return Mode.ASSIST; case "RTB": return Mode.RTB; case "FOLLOW": case "ESCORT": return Mode.FOLLOW; default: return def;
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

    ReadIGC(); FollowStep(); ReadSwarm();
    ReadIGC(); ReadSwarm();
    if (_tick % 6 == 3) BroadcastStatus();
    StepSlimScan();
    if (_tick % 20 == 7) CheckDamage();
    if (_tick % 6 == 0) PollDesignators();
    if (_tick % 10 == 5) PollSensors();
    if (_tick % 10 == 0) { PruneAndSelect(); PowerAndHealth(); }
    MaintainLock();
    if (_mode != Mode.IDLE) OmniScan();
    if (_tgt == null && !_aiOn && AutoScan && (_mode == Mode.GUARD || _mode == Mode.HUNT || _mode == Mode.FOLLOW)) AutoScanStep();
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
        case "follow": case "escort": SetMode(Mode.FOLLOW); break;
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
        case "focus": SplitFocus = false; break;
        case "home": if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } break;
        case "range":
            double r; if (w.Length > 1 && double.TryParse(w[1], out r)) Standoff = MathHelper.Clamp(r, MinRange, FireRange); break;
        case "reload": LoadConfig(); FleetLoad(true); LaunchLoad(); AiGateLoad(); FollowLoad(); GetBlocks(); break;
        case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;
    }
}

void SetMode(Mode m)
{
    _mode = m; _noTgtSince = _now; _patrolLeg = false; _patrolStopSet = false;
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
    AimMode = Cfg(S, "Aim Point (AUTO/BREACH/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();
    BreachAuto = Cfg(S, "Breach Large Ships (AUTO)", BreachAuto);
    BreachMinRadius = Cfg(S, "Breach Min Target Radius (m)", BreachMinRadius);
    BreachStall = Cfg(S, "Breach Stall (s)", BreachStall);
    SeekerGroup = Cfg(S, "Seeker Group (PowerSystems/Weapons/Propulsion)", SeekerGroup).Trim();
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
    ReadTgt(_lTgt);
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
        if (_mode == Mode.FOLLOW && Vector3D.DistanceSquared(p, home) > FoEngage * FoEngage) continue;   // FOLLOW: only threats near the owner
        Vector3D d = p - me; double dist = d.Length();
        double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(d / Math.Max(dist, 1), fwd), -1, 1)) * 57.2958;
        double closing = Vector3D.Dot(t.V(_now) - myVel, -d / Math.Max(dist, 1));
        double score = dist + ang * 6 - closing * 3;
        if (t.Type == MyDetectedEntityType.SmallGrid) score -= 150;   // small = more likely drones/fighters shooting at us
        if (t == _tgt) score -= Stickiness;                            // stickiness
        score += Math.Max(0, (_now - t.Seen) - 1.0) * 150;             // stale tracks lose to fresh sightings
        if (dist < 150) score -= 350;                                  // something right on top of us comes first
        score += HiveScore(t);
        if (score < bestScore) { bestScore = score; best = t; }
    }
    _tgt = best;
}

Vector3D Home()
{
    if (_mode == Mode.FOLLOW && FollowKnown()) return FollowPos();   // FOLLOW: the owner (ship link or on foot), see the FOLLOW block
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
            aimR = Math.Min(aimR, Math.Max(_tgt.Raw * 0.8, 0.5));   // a rocket must HIT a block to detonate: a wreck of one or two blocks is only ~0.5 m wide
            _fireWinDeg = Math.Max(0.3, Math.Atan(aimR / Math.Max(dist, 1)) * 57.2958) * FireTol;
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
        if (_dash && _mode == Mode.HUNT && _tgt == null) want = _dashDir * DashSpeed + Separation(me);   // dash along the AI's heading
        if (_mode == Mode.FOLLOW && FollowKnown()) want = FollowWant(me, aBrake, Separation(me)) + Evade();   // own slot by the owner, matching its velocity
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
        if (_mode == Mode.GUARD && Vector3D.Distance(me, home) > GuardRadius * 1.3) want = Vector3D.Normalize(home - me) * CruiseSpeed;
        if (_mode == Mode.FOLLOW && FollowFar(me)) want = FollowBack(me);   // leash: drop the fight, back to the owner
    }
    double cap = (_tgt != null && _mode != Mode.RTB) || _mode == Mode.FOLLOW ? MaxSpeed : (_dash ? DashSpeed + 5 : CruiseSpeed);
    if (want.LengthSquared() > cap * cap) want = Vector3D.Normalize(want) * cap;

    Vector3D acc = (want - myVel) * 2.0;
    if (acc.LengthSquared() > 2500) acc = Vector3D.Normalize(acc) * 50;
    ApplyThrust((acc - grav) * mass);
}

// Distance a rocket has flown t seconds after launch, and the inverse (time to cover d metres).
double RktDist(double t)
{
    double ta = Math.Max(0, (RktVmax - RktV0) / Math.Max(RktAcc, 1));
    if (t <= ta) return RktV0 * t + 0.5 * RktAcc * t * t;
    return RktV0 * ta + 0.5 * RktAcc * ta * ta + RktVmax * (t - ta);
}
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
        g.ApplyAction("ShootOnce");
        _nextShot[g.EntityId] = _now + RocketInterval;
        _lastShot = _now; _showFire = true;
        if (++_salvoCount >= SalvoSize) { _salvoCount = 0; _salvoPauseUntil = _now + SalvoPause; }
        return;
    }
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
    _sb.Append("WASP DRONE AI v").Append(VER).Append('\n');
    _sb.Append("Mode   : ").Append(_mode).Append('\n');
    _sb.Append(FollowStatus());
    if (_tgt != null)
    {
        double dist = _rc != null ? Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()) : 0;
        _sb.Append("Target : ").Append(_tgt.Type == MyDetectedEntityType.SmallGrid ? "SMALL " : _tgt.Type == MyDetectedEntityType.LargeGrid ? "LARGE " : "").Append(_tgt.Src).Append('\n');
        double mySpd = _rc != null ? _rc.GetShipVelocities().LinearVelocity.Length() : 0;
        _sb.Append("Range  : ").Append(dist.ToString("0")).Append(" m  tgt ").Append(_tgt.V(_now).Length().ToString("0")).Append(" me ").Append(mySpd.ToString("0")).Append(" m/s\n");
        _sb.Append("Lead   : ").Append(_leadT.ToString("0.00")).Append(" s flight  acc ").Append(LeadAcc(_tgt).Length().ToString("0.0")).Append("/").Append(_tgt.AccMag.ToString("0.0")).Append(" for ").Append(LeadHold(_tgt).ToString("0.0")).Append(" s  x").Append(LeadScale.ToString("0.00")).Append('\n');
        _sb.Append("Aim err: ").Append(_aimErrDeg.ToString("0.00")).Append(" / ").Append(_fireWinDeg.ToString("0.00")).Append(" deg").Append(_showFire ? "  FIRING" : "").Append('\n');
    }
    else _sb.Append("Target : none (").Append(_contacts.Count).Append(" contacts)\n\n\n\n");
    _sb.Append("Battery: ").Append((_batFrac * 100).ToString("0")).Append("%  Rockets ").Append(_rockets >= 0 ? _rockets.ToString() : "?").Append("\n");
    if (_gateWhy != "" && _tgt != null) _sb.Append("Hold   : ").Append(_gateWhy).Append('\n');
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
    // radius -1 = "this one is finished" for every drone (and stays compatible with the carrier link)
    if (tell) { var fin = new MyTuple<long, Vector3D, Vector3D, double, long>(t.Id, t.Pos, t.Vel, -1, Me.EntityId); IGC.SendBroadcastMessage(Tag + "_TGT", fin); FleetSend(fin); }
}

// Finished = no longer an enemy (its owned blocks are gone), blown down to under 45% of the
// largest size seen, or tumbling out of control for 4 s.
bool Disabled(Track t)
{
    if (t.DeadAt > 0) return true;
    if (t.Spin > 0.8) { if (t.SpinSince < 0) t.SpinSince = _now; } else t.SpinSince = -1;
    return (t.SpinSince > 0 && _now - t.SpinSince > 4) || (t.Rad0 > 3 && t.Radius < 0.45 * t.Rad0) || (t.RawMax > 1.6 && t.Raw < 0.4 * t.RawMax) || t.Raw < 0.75;   // (last two: a small ship reduced to a block or two; or a lone block / debris, which splits off as its own grid and would otherwise hold the guns forever)
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
// Decoys pull turrets (and the seeker) onto themselves first; static guns are never distracted.
bool BreachAuto = true; double BreachMinRadius = 25, BreachStall = 30, BreachProbe = 30;
string SeekerGroup = "PowerSystems"; double SeekerShotSpeed = 800;
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
    SeekerTick(t);
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
    GridTerminalSystem.GetBlocksOfType(_skT, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("[Seeker]"));
    if (_skT.Count == 0) _skLog = ""; else if (_skLog == "") _skLog = "ready, group " + SeekerGroup;   // (re-scans keep the last report)
    foreach (var tu in _skT)
    {
        try { if (SeekerGroup.Length > 0 && tu.GetTargetingGroup() != SeekerGroup) tu.SetTargetingGroup(SeekerGroup); }
        catch (Exception x) { _skLog = "group not set: " + x.Message; }
    }
}
void SeekerTick(Track t)
{
    if (_skT.Count == 0 || _now - _skT0 < 0.5) return;
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
            q = o + d * D - rv * (D / SeekerShotSpeed);
        }
        var cam = CamFor(q, 0);
        if (cam == null) { _skLog = "no camera charge/angle"; continue; }
        Vector3D cd = q - cam.GetPosition(); double cl = cd.Length(); if (cl < 1) continue; cd /= cl;
        var e = cam.Raycast(q + cd * BreachProbe);
        if (e.IsEmpty() || e.EntityId != t.Id || !e.HitPosition.HasValue)
        { _skLog = (hp ? "block pos given, " : "aim ray, ") + "ray missed hull"; continue; }
        MatrixD inv = MatrixD.Transpose(e.Orientation);
        _skS = Vector3D.TransformNormal(e.HitPosition.Value - e.Position, inv);
        _skDir = Vector3D.TransformNormal(cd, inv);
        _skOk = true; _skId = t.Id;
        _skLog = (hp ? "block pos given" : "aim ray") + ", " + (q - e.HitPosition.Value).Length().ToString("0") + " m under hull";
        return;
    }
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
    AiGateFind(_ai);
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
