// =====================================================================
//  GNAT SWARM AI  v1.12 - minimal "point-and-burn" swarm drone
//  Space Engineers (vanilla, small grid). Paste into a Programmable Block.
// ---------------------------------------------------------------------
//  The drone only has thrusters on its nose axis (one forward, one
//  reverse; a single thruster also works). It moves by turning the whole
//  ship so the thrust points where it needs to accelerate - including
//  sideways to cancel drift - like a rocket or a Lunar lander.
//
//  ATTACK ("yo-yo" runs, per drone)
//   SETUP   fly to a run-in point inside the swarm's attack cone
//   RUN     nose stays on the lead point and the guns fire while the two
//           axial thrusters close in, brake, back off and close again
//           along the line of sight. Small nose tilts trim sideways drift.
//   CORRECT sideways drift got too big: stop shooting, swing the thrust
//           across to fix it, then straight back to RUN
//   BREAK   emergency sidestep if it ever gets too close
//
//  SWARM (over antenna / IGC)
//   * each drone senses all around itself (~50 m sensor), plus camera,
//     turret/turret-controller designators, and targets from the carrier
//   * drones share position, velocity, state and targets
//   * each drone gets a formation slot and its own run-in direction, so
//     the swarm hits from many angles at once without crossing fire
//   * separation keeps drones from colliding; shots are held if a swarm
//     mate is near the line of fire
//   * target format is compatible with the Hornet / Longbow mothership link
//
//  FINDING THE ENEMY (v1.9)
//   * Cameras cannot find a 3 m drone beyond a few hundred metres, and the Gnat has no AI block. Two things were added:
//   * HIT BEARING: when the swarm is shot at, the blocks that lost integrity give the direction of the shooter (maximum
//     likelihood over 64 directions, shared over IGC "<Tag>_HIT"). With no target the swarm then flies a FAN of runs along
//     that bearing, each drone in its own cone slot, sweeping the cone with short camera rays; the sensor and the rays
//     pick the enemy up and the normal attack takes over. Needs a few hits before it moves.
//   * FEEDS: it also listens to the target broadcasts of other fleets ("Listen To IGC Tags": HORNET, WASP, WATCHER, ...).
//   * If the Gnat blueprint has [AI] Offensive + Flight blocks, the v1.8 hybrid search is used as well (it is far better).
//
//  v1.12 (LEAD v1.0, shared block, docs/lead-v1.md): the lead solver predicts the target with its freshest acceleration, held only
//   for as long as its manoeuvres usually last (an AI ship on evasive flips its strafe every 2-5 s); the hull aim point counts only
//   across the line of fire, turns with the target's spin, and keeps "Aim Margin" x the predicted miss of known hull around it
//
//  COMMANDS (argument, toolbar, or IGC "<Tag>_CMD" to command the swarm)
//   form | attack | rtb | idle | lock | unlock | home | reload
//   satellite (sat, orbit): FORM as a slowly turning vertical ring round the carrier / home point (ring size grows with the swarm)
//   follow / escort = form: the formation sphere sits round whatever broadcasts the home point (a ship running a Mothership
//   link), outside its hull, and moves with it (v1.11: the drones match the ship's velocity, so they keep up)
//  KAMIKAZE DEFENCE (v1.13; shared block tools/kz_block.cs, docs/kamikaze-defence.md): incoming swarms of small warhead drones are
//   never written off as wrecks, become the priority target, and the drone backs away while firing and breaks sideways just
//   before impact; spare camera rays track the swarm and the tracks are shared with the hive. Own [Kamikaze Defence] Custom Data section.
// =====================================================================

const string VER = "1.13+launch";

// ---------------- settings (Custom Data) ----------------
string Tag = "GNAT";
string StartMode = "IDLE";
double ProjSpeed = 800, FireRange = 700;   // gatling 800 m/s (docs/stats.md)
double ClosingSpeed = 45;    // m/s closing on the target
double BackoffSpeed = 30;    // m/s backing away from the target
double OrbitSpeed = 12;      // m/s sideways slide kept during runs (harder to hit)
double SetupRange = 650;     // run-in start distance
double NearRange = 140;      // start backing off at this range (+ braking distance)
double FarRange = 480;       // start closing again beyond this range
double BreakRange = 60;      // emergency sidestep inside this (+ target radius)
double ConeDeg = 55;         // half-angle of the swarm's attack cone
double LatMax = 25;          // m/s sideways drift error that forces a CORRECT
double FormRadius = 45;      // formation sphere radius (grows with swarm size)
double SepRadius = 35;       // drones push apart inside this distance
double EngageRadius = 2500;  // FORM mode engages targets this close to home
double MaxSpeed = 98, CruiseSpeed = 70, AccelGain = 1.6;
double AimKp = 7, AimKd = 0.3, MaxTurn = 7;
double RtbBattery = 0.12;
double ScanRange = 1000, ScanCone = 30;
double CloseScanRange = 120; // all-around camera scan distance
bool CloseScan = true;
bool TargetLarge = true, TargetSmall = true, TargetChars = false, TargetNeutral = false;
bool AutoScan = true;
double LeadScale = 1.0;      // scales the lead offset if shots land consistently ahead/behind
bool UseTargetAccel = true;
bool SplitFocus = true;
double DisabledGrace = 5, EvadeSpeed = 20;
bool AiAssist = true; double AiConfirmRange = 1300, AiConfirmLong = 2600;
double RaidCone = 25, RaidScanCone = 40, RaidRay = 600, RaidSpeed = 80, RaidMaxTravel = 700, RaidLife = 35, RaidMinStart = 2.5, RaidMinHold = 1.2;   // hit-bearing raid (v1.9)
string ListenTags = "HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER";   // other fleets' target feeds
double FireStaleMax = 2.0;   // never fire at a track older than this (extrapolation is a guess by then)

// ---------------- state ----------------
enum Mode { IDLE, FORM, ATTACK, RTB }
enum Run { SETUP, RUN, CORRECT, BREAK }
Mode _mode = Mode.IDLE; Run _run = Run.SETUP;
MyIni _ini = new MyIni();
double _now = 0; long _tick = 0;
Vector3D _home; bool _homeSet = false;
Vector3D _dynHome; double _dynHomeT = -99;
Vector3D _hvP, _homeV; double _hvT = -99;   // carrier velocity from successive home fixes (FORM / RTB feed-forward)
IMyBroadcastListener _lHomeR2; string _lHomeR2Key = null;   // "<fleet>_HOMER" (carrier radius from any link)

IMyShipController _rc;
List<IMyGyro> _gyros = new List<IMyGyro>();
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyThrust> _fwdThr = new List<IMyThrust>(), _revThr = new List<IMyThrust>();
List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
List<IMyCameraBlock> _cams = new List<IMyCameraBlock>();
List<IMyBatteryBlock> _bats = new List<IMyBatteryBlock>();
List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
List<IMyTurretControlBlock> _ctcs = new List<IMyTurretControlBlock>();
IMyTerminalBlock _ref;
IMyTextSurface _lcd;
IMyBroadcastListener _lTgt, _lCmd, _lHome, _lSwarm, _lHit, _lHomeR;
double _homeR = 0, _homeRT = -99;   // radius of the carrier the home point sits in (from GnatMothership)
List<IMyBroadcastListener> _lFeed = new List<IMyBroadcastListener>();

class Track
{
    public long Id; public Vector3D Pos, Vel, Acc; public double AccMag;
    public Vector3D LAcc, LSeg, LW, LOR; public int LSegN; public double LSegT0 = -99, LManT = 2;   // LEAD v1.0 state (fresh acceleration, manoeuvres, spin)
    public double Spin, SpinSince = -1, Rad0, DeadAt = -1, DisSince = -1; public Vector3D OF, OU; public bool HasOri;
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
    } public double Radius, Seen; public string Src; public MyDetectedEntityType Type;
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
    public Vector3D P(double now) { double t = now - Seen; return Pos + Vel * t + 0.5 * EA() * t * t; }
    public Vector3D V(double now) { return Vel + EA() * (now - Seen); }
}
class Mate { public long Id; public Vector3D Pos, Vel; public double Seen; public long Tgt; public int Flags; }

Dictionary<long, Track> _contacts = new Dictionary<long, Track>();
Dictionary<long, Mate> _mates = new Dictionary<long, Mate>();
List<long> _drop = new List<long>();
List<long> _ids = new List<long>();
Track _tgt;
int _idx = 0, _n = 1;
Vector3D _attackDir; Vector3D _breakDir; bool _opening = false; double _correctT = 0;

Vector3D _prevErr, _prevAim; bool _havePrev = false;
double _powerScale = 1, _batFrac = 1;
bool _firing = false, _hadAmmo = false;
int _scanIdx = 0;
string _err = "";
double _aimErrDeg, _latSpd, _avgMs; int _peakIns = 0;   // PB limit is Runtime.MaxInstructionCount per run
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

void CtorErr(string w, Exception ex) { _crash = "setup " + w + ": " + ex.Message; Echo(_crash); }   // a failing setup step must not kill the script for good: the rest still runs and the error is on the screen

public Program()
{
    try
    {
        LoadConfig();
    }
    catch (Exception ex) { CtorErr("config", ex); }
    try
    {
        FleetLoad(true); LaunchLoad(); KzLoad(); AiGateLoad();
    }
    catch (Exception ex) { CtorErr("modules", ex); }
    try
    {
        _mode = ParseMode(StartMode, Mode.IDLE);
        if (!string.IsNullOrEmpty(Storage))
        {
            var p = Storage.Split(';'); double x, y, z;
            if (p.Length >= 5 && double.TryParse(p[1], out x) && double.TryParse(p[2], out y) && double.TryParse(p[3], out z))
            { _mode = ParseMode(p[0], _mode); _home = new Vector3D(x, y, z); _homeSet = p[4] == "1"; }
        }
    }
    catch (Exception ex) { CtorErr("storage", ex); }
    try
    {
        _lTgt = IGC.RegisterBroadcastListener(Tag + "_TGT");
        _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
        _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME");
        _lSwarm = IGC.RegisterBroadcastListener(Tag + "_SWARM");
        _lHit = IGC.RegisterBroadcastListener(Tag + "_HIT");
        _lHomeR = IGC.RegisterBroadcastListener(Tag + "_HOMER");
        foreach (var t in ListenTags.Split(','))
        { string tt = t.Trim().ToUpperInvariant(); if (tt != "" && tt != Tag.ToUpperInvariant()) _lFeed.Add(IGC.RegisterBroadcastListener(tt + "_TGT")); }
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
    Storage = string.Join(";", _mode == Mode.FORM && SatOn ? "SAT" : _mode.ToString(), _home.X.ToString("R"), _home.Y.ToString("R"), _home.Z.ToString("R"), _homeSet ? "1" : "0");
}

Mode ParseMode(string s, Mode d)
{
    switch (s.Trim().ToUpperInvariant())
    {
        case "SATELLITE": case "SAT": case "ORBIT": SatOn = true; return Mode.FORM; case "IDLE": return Mode.IDLE; case "FORM": return Mode.FORM; case "ATTACK": return Mode.ATTACK; case "RTB": return Mode.RTB;
        default: return d;
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
int _errRun, _okRun;

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
    if (_rc == null || !_rc.IsFunctional) { _err = "No working Remote Control"; Display(); Release(); return; }

    ReadIGC();
    StepSlimScan();
    if ((_tick & 1) == 1) CheckDamage();   // every 2nd tick: the drone turns fast, the hit direction must be read in its attitude of the moment
    if (_tick % 6 == 0) { Broadcast(); PollDesignators(); }
    if (_tick % 10 == 5) PollSensors();
    if (_tick % 10 == 0) { Housekeeping(); PowerAndHealth(); }
    MaintainLock();
    if (_mode != Mode.IDLE) OmniScan();
    bool raid = RaidOn;
    if (raid && !_raidActive) { _raidMe = _rc.GetPosition(); _raidPass = 0; }
    _raidActive = raid;
    if (raid) RaidScan();
    else if (_tgt == null && !_aiOn && AutoScan && _mode != Mode.IDLE && _mode != Mode.RTB) AutoScanStep();
    if (_tgt != null && _tgt.Src != "IGC" && _now - _tgt.Seen < 1.0 && _tick % 30 == 0) ShareTarget();
    if (LaunchStep(dt, _mode == Mode.IDLE, StartMode, _dynHomeT > 0, _dynHome, Command)) { }   // automatic take-off after the release from a printer (LAUNCH block); Command('guard') = join the hive
    else if (!AiStep()) Control(dt);
    _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount);
    if (_tick % 30 == 0) Display();
}

void Command(string a)
{
    var w = a.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (w.Length == 0) return;
    switch (w[0])
    {
        case "form": case "guard": case "follow": case "escort": SatOn = false; SetMode(Mode.FORM); break;
        case "satellite": case "sat": case "orbit": SatOn = true; SetMode(Mode.FORM); break;
        case "attack": case "hunt": SetMode(Mode.ATTACK); break;
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
        case "home": if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } break;
        case "reload": LoadConfig(); FleetLoad(true); LaunchLoad(); KzLoad(); AiGateLoad(); GetBlocks(); break;
        case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;
    }
}
void SetMode(Mode m) { _mode = m; _run = Run.SETUP; if (m == Mode.IDLE) Release(); }

// =====================================================================
void LoadConfig()
{
    MyIniParseResult res;
    if (!_ini.TryParse(Me.CustomData, out res)) { _err = "CustomData parse error line " + res.LineNo; return; }
    const string S = "Gnat";
    Tag = Cfg(S, "IGC Tag", Tag);
    StartMode = Cfg(S, "Start Mode (FORM/ATTACK/IDLE)", StartMode);
    ProjSpeed = Cfg(S, "Projectile Speed", ProjSpeed);
    FireRange = Cfg(S, "Fire Range", FireRange);
    ClosingSpeed = Cfg(S, "Closing Speed", ClosingSpeed);
    SetupRange = Cfg(S, "Run-in Start Range", SetupRange);
    BackoffSpeed = Cfg(S, "Back-off Speed", BackoffSpeed);
    OrbitSpeed = Cfg(S, "Orbit Speed", OrbitSpeed);
    NearRange = Cfg(S, "Back-off Range", NearRange);
    FarRange = Cfg(S, "Re-close Range", FarRange);
    BreakRange = Cfg(S, "Emergency Break Range", BreakRange);
    ConeDeg = Cfg(S, "Attack Cone Degrees", ConeDeg);
    LatMax = Cfg(S, "Max Sideways Drift m/s", LatMax);
    FormRadius = Cfg(S, "Formation Radius", FormRadius);
    SatDist = Math.Max(30, Cfg(S, "Satellite Distance (m)", SatDist));
    SatPeriod = Math.Max(0, Cfg(S, "Satellite Orbit Period (s)", SatPeriod));
    SepRadius = Cfg(S, "Separation Radius", SepRadius);
    EngageRadius = Cfg(S, "Engage Radius From Home", EngageRadius);
    MaxSpeed = Cfg(S, "Combat Speed", MaxSpeed);
    CruiseSpeed = Cfg(S, "Cruise Speed", CruiseSpeed);
    AccelGain = Cfg(S, "Velocity Gain", AccelGain);
    AimKp = Cfg(S, "Aim Kp", AimKp);
    AimKd = Cfg(S, "Aim Kd", AimKd);
    MaxTurn = Cfg(S, "Max Turn Rate rad/s", MaxTurn);
    RtbBattery = Cfg(S, "RTB Battery Fraction", RtbBattery);
    AutoScan = Cfg(S, "Auto Camera Scan", AutoScan);
    CloseScan = Cfg(S, "All-around Close Scan", CloseScan);
    CloseScanRange = Cfg(S, "Close Scan Range", CloseScanRange);
    LeadScale = Cfg(S, "Lead Scale (1 = calculated)", LeadScale);
    LeadMargin = Cfg(S, "Aim Margin (predicted misses)", LeadMargin);
    UseTargetAccel = Cfg(S, "Use Target Acceleration", UseTargetAccel);
    SplitFocus = Cfg(S, "Split Targets Across Hive (false = focus fire)", SplitFocus);
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
    FireStaleMax = Cfg(S, "Max Track Age To Fire (s)", FireStaleMax);
    RaidCone = Cfg(S, "Raid Fan Half-angle Degrees", RaidCone);
    RaidScanCone = Cfg(S, "Raid Scan Cone Degrees", RaidScanCone);
    RaidRay = Cfg(S, "Raid Scan Ray Length", RaidRay);
    RaidSpeed = Cfg(S, "Raid Speed", RaidSpeed);
    RaidMaxTravel = Cfg(S, "Raid Max Travel", RaidMaxTravel);
    RaidLife = Cfg(S, "Raid Bearing Memory Seconds", RaidLife);
    RaidMinStart = Cfg(S, "Raid Start Confidence", RaidMinStart);
    RaidMinHold = Cfg(S, "Raid Hold Confidence", RaidMinHold);
    ListenTags = Cfg(S, "Listen To IGC Tags (target feeds)", ListenTags);
    ScanRange = Cfg(S, "Scan Range", ScanRange);
    TargetLarge = Cfg(S, "Target Large Grids", TargetLarge);
    TargetSmall = Cfg(S, "Target Small Grids", TargetSmall);
    TargetChars = Cfg(S, "Target Characters", TargetChars);
    TargetNeutral = Cfg(S, "Target Neutral/Unowned", TargetNeutral);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}
string Cfg(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double Cfg(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool Cfg(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

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
    GridTerminalSystem.GetBlocksOfType(_bats, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_turrets, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_ctcs, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_sensors, b => b.IsSameConstructAs(Me));
    ConfigureSensors();
    foreach (var c in _cams) c.EnableRaycast = true;
    ScanSlims();
    FindAi();
    _ref = _guns.Count > 0 ? (IMyTerminalBlock)_guns[0] : _rc;
    _fwdThr.Clear(); _revThr.Clear();
    if (_ref != null)
    {
        Vector3D nose = _ref.WorldMatrix.Forward;
        foreach (var t in _thr)
        {
            double d = Vector3D.Dot(t.WorldMatrix.Backward, nose); // thrust pushes opposite its exhaust
            if (d > 0.9) _fwdThr.Add(t); else if (d < -0.9) _revThr.Add(t);
            else t.ThrustOverridePercentage = 0;          // off-axis thrusters are left alone
        }
    }
    if (_fwdThr.Count == 0 && _revThr.Count == 0) _err = "No thrusters on the nose axis";
    _lcd = Me.GetSurface(0);
    _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.75f; _lcd.Font = "Monospace";
}

// =====================================================================
//  TARGETS & SWARM COMMS
bool Valid(MyDetectedEntityInfo e)
{
    if (e.IsEmpty() || Ignored(e.EntityId) || e.EntityId == Me.CubeGrid.EntityId || _mates.ContainsKey(e.EntityId)) return false;
    switch (e.Type)
    {
        case MyDetectedEntityType.LargeGrid: if (!TargetLarge) return false; break;
        case MyDetectedEntityType.SmallGrid: if (!TargetSmall) return false; break;
        case MyDetectedEntityType.CharacterHuman: case MyDetectedEntityType.CharacterOther: if (!TargetChars) return false; break;
        default: return false;
    }
    var r = e.Relationship;
    if (r == MyRelationsBetweenPlayerAndBlock.Enemies) return true;
    return TargetNeutral && (r == MyRelationsBetweenPlayerAndBlock.Neutral || r == MyRelationsBetweenPlayerAndBlock.NoOwnership);
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
    while (_lSwarm.HasPendingMessage)
    {
        var m = _lSwarm.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, long, int>)m.Data;
        if (d.Item1 == Me.CubeGrid.EntityId) continue;
        Mate mt;
        if (!_mates.TryGetValue(d.Item1, out mt)) { mt = new Mate { Id = d.Item1 }; _mates[d.Item1] = mt; }
        mt.Pos = d.Item2; mt.Vel = d.Item3; mt.Tgt = d.Item4; mt.Flags = d.Item5; mt.Seen = _now;
        // a mate lost its lock on the target I can still see: answer at once with a fresh fix
        if ((d.Item5 & 4) != 0 && _tgt != null && _tgt.Id == d.Item4 && _tgt.Src != "IGC" && _now - _tgt.Seen < 0.4 && _now - _lastStaleShare > 0.3)
        { _lastStaleShare = _now; ShareTarget(); }
    }
    for (int li = -1; li < _lFeed.Count + FleetTgtL.Count; li++)
    {
        var lt = li < 0 ? _lTgt : li < _lFeed.Count ? _lFeed[li] : FleetTgtL[li - _lFeed.Count];
        while (lt.HasPendingMessage)
        {
            var m = lt.AcceptMessage();
            if (m.Source == IGC.Me || !(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
            if (d.Item4 < 0) { _ignore[d.Item1] = _now + 120; _contacts.Remove(d.Item1); if (_tgt != null && _tgt.Id == d.Item1) _tgt = null; continue; }
            if (Ignored(d.Item1) || _mates.ContainsKey(d.Item1) || d.Item1 == Me.CubeGrid.EntityId || !FleetOk(d)) continue;
            Track t;
            if (_contacts.TryGetValue(d.Item1, out t) && t.Src != "IGC" && _now - t.Seen < 0.3) continue;
            if (t == null) { t = new Track { Id = d.Item1, Type = MyDetectedEntityType.SmallGrid }; _contacts[d.Item1] = t; }
            t.Set(d.Item2, d.Item3, d.Item4, _now, "IGC");
        }
    }
    while (_lHit.HasPendingMessage)
    {
        var m = _lHit.AcceptMessage();
        if (m.Source == IGC.Me || !(m.Data is MyTuple<Vector3D, Vector3D>)) continue;
        var d = (MyTuple<Vector3D, Vector3D>)m.Data;
        AddHit(d.Item2, 0.7);
    }
    while (_lCmd.HasPendingMessage) { var m = _lCmd.AcceptMessage(); if (m.Data is string) Command((string)m.Data); }
    while (_lHome.HasPendingMessage) { var m = _lHome.AcceptMessage(); if (m.Data is Vector3D) { _dynHome = (Vector3D)m.Data; _dynHomeT = _now; } }
    FleetPump(Command, p => { _dynHome = p; _dynHomeT = _now; });
    while (_lHomeR.HasPendingMessage) { var m = _lHomeR.AcceptMessage(); if (m.Data is double) { _homeR = (double)m.Data; _homeRT = _now; } }
    if (_lHomeR2Key != _flChan)
    {
        if (_lHomeR2 != null) IGC.DisableBroadcastListener(_lHomeR2);
        _lHomeR2 = _flChan != "" ? IGC.RegisterBroadcastListener(_flChan + "_HOMER") : null; _lHomeR2Key = _flChan;
    }
    while (_lHomeR2 != null && _lHomeR2.HasPendingMessage) { var m = _lHomeR2.AcceptMessage(); if (m.Data is double) { _homeR = (double)m.Data; _homeRT = _now; } }
    if (_dynHomeT > _hvT)
    {   // a new home fix: the carrier's velocity from the last one (a jump over 150 m/s is another carrier or a respawn, not speed)
        double hd = _dynHomeT - _hvT;
        if (hd > 0.05 && hd < 2) { Vector3D raw = (_dynHome - _hvP) / hd; _homeV = raw.LengthSquared() > 150 * 150 ? Vector3D.Zero : _homeV + (raw - _homeV) * 0.5; }
        else _homeV = Vector3D.Zero;
        _hvP = _dynHome; _hvT = _dynHomeT;
    }
}
Vector3D HomeVel() { return (_now - _dynHomeT < 2 && !(_mode != Mode.RTB && _tgt == null && _now - _assistT < 4)) ? _homeV : Vector3D.Zero; }   // zero while the home point is not the carrier

void Broadcast()
{
    IGC.SendBroadcastMessage(Tag + "_SWARM", new MyTuple<long, Vector3D, Vector3D, long, int>(
        Me.CubeGrid.EntityId, _rc.GetPosition(), _rc.GetShipVelocities().LinearVelocity, _tgt != null ? _tgt.Id : 0, (UnderFire ? 1 : 0) | (OwnAlert ? 2 : 0) | (LockStale ? 4 : 0)));
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
    IMyCameraBlock best = null;
    foreach (var c in _cams)
    {
        if (!c.IsWorking) continue;
        Vector3D d = pos - c.GetPosition(); double len = d.Length();
        if (len < 1 || Vector3D.Dot(d / len, c.WorldMatrix.Forward) < 0.72) continue;
        if (c.AvailableScanRange < Math.Max(range, len)) continue;     // a ray costs its length in charge
        if (best == null || c.AvailableScanRange > best.AvailableScanRange) best = c;
    }
    return best;
}

void ManualLock()
{
    IMyCameraBlock cam = null;
    foreach (var c in _cams) if (c.IsWorking && Forwardish(c) && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    if (cam == null) { _err = "No forward camera for lock"; return; }
    var e = cam.Raycast(Math.Min(2500, cam.AvailableScanRange));
    if (e.IsEmpty() || !(e.Type == MyDetectedEntityType.LargeGrid || e.Type == MyDetectedEntityType.SmallGrid || e.Type == MyDetectedEntityType.CharacterHuman))
    { _err = "Lock: nothing hit"; return; }
    var r = e.Relationship;
    if (r == MyRelationsBetweenPlayerAndBlock.Owner || r == MyRelationsBetweenPlayerAndBlock.FactionShare || r == MyRelationsBetweenPlayerAndBlock.Friends) { _err = "Lock: friendly"; return; }
    _err = ""; _tgt = AddContact(e, "LOCK"); ShareTarget();
}

// ---- LOCK MAINTENANCE AND RE-ACQUISITION (same scheme as the Hornet, see docs/script-fix-notes.md H3) ----
//  A camera banks only 2 km/s of ray range and a miss costs as much as a hit, so rays are cheap (they end just behind the
//  predicted point) and a stale lock gets two rays per tick on a widening spiral. The lost target stays a "ghost" for GhostTime.
const double GhostTime = 8.0;
int _missRun = 0, _spiralIdx = 0, _probeN = 0; double _lastStaleShare = -9;
Vector3D _lostPos, _lostVel; double _lostT = -99, _lostRad = 3;
void NoteLost(Track t)
{
    if (t.Src == "IGC" || t.DeadAt > 0 || t.DisSince >= 0) return;
    _lostPos = t.Pos; _lostVel = t.Vel; _lostT = t.Seen; _lostRad = t.Radius;
}
double Uncertainty(Track t, double age)
{
    return Math.Min(80, t.Radius * 0.35 + 0.5 * Math.Max(t.AccMag, 6.0) * age * age);
}
bool LockStale { get { return _tgt != null && _tgt.Src != "IGC" && _now - _tgt.Seen > 1.2; } }
void MaintainLock()
{
    if (_tgt == null || _cams.Count == 0 || _aiOn) return;
    double age = _now - _tgt.Seen;
    if (age < 0.2) return;
    if ((_tgt.Src == "SENSOR" || _tgt.Src == "TURRET" || _tgt.Src == "CTC") && age < 0.5) return;
    Vector3D p = _tgt.P(_now), me = _rc.GetPosition();
    Vector3D dir = p - me; double dist = dir.Length();
    if (dist < 1) return;
    dir /= dist;
    int rays = age > 0.6 ? 2 : 1;
    for (int k = 0; k < rays; k++)
    {
        bool probingAim = false;
        Vector3D aim = age > 0.6 ? ReacquirePoint(_tgt, p, dir, age) : ProbePoint(_tgt, p, dir, dist, out probingAim);
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
        if (!e.IsEmpty() && Valid(e)) AddContact(e, "LOCK");
    }
}
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
    IMyCameraBlock cam = null;
    foreach (var c in _cams) if (c.IsWorking && Forwardish(c) && c.AvailableScanRange >= ScanRange && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    if (cam == null) return;
    _scanIdx++;
    double lostAge = _now - _lostT;
    if (lostAge < 25 && (_scanIdx & 1) == 0)
    {   // just lost a target: every other ray goes round where it was last seen, widening with time
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
    double r = ScanCone * Math.Sqrt((_scanIdx % 120 + 0.5) / 120), th = _scanIdx * 2.39996323;
    var e = cam.Raycast(ScanRange, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
    if (Valid(e)) AddContact(e, "SCAN");
}

// prune, pick target, work out my place in the swarm
void Housekeeping()
{
    KzAssess();   // KAMIKAZE DEFENCE: classify incoming kamikazes before AssessTargets / scoring
    _drop.Clear();
    foreach (var kv in _contacts)
    {   // the target I am working on stays alive as a ghost while MaintainLock hunts for it
        double lim = (kv.Value == _tgt && kv.Value.Src != "IGC") ? GhostTime : 4;
        if (_now - kv.Value.Seen > lim) _drop.Add(kv.Key);
    }
    foreach (var id in _drop) { Track gone; if (_contacts.TryGetValue(id, out gone) && gone == _tgt) NoteLost(gone); _contacts.Remove(id); }
    _drop.Clear();
    foreach (var kv in _mates) if (_now - kv.Value.Seen > 3) _drop.Add(kv.Key);
    foreach (var id in _drop) _mates.Remove(id);
    if (_tgt != null && !_contacts.ContainsKey(_tgt.Id)) _tgt = null;
    AssessTargets();

    // slot index = rank of my grid id among the swarm (stable, no negotiation needed)
    _ids.Clear(); _ids.Add(Me.CubeGrid.EntityId); foreach (var k in _mates.Keys) _ids.Add(k);
    _ids.Sort(); _idx = _ids.IndexOf(Me.CubeGrid.EntityId); _n = _ids.Count;

    if (_mode != Mode.FORM && _mode != Mode.ATTACK) return;
    Vector3D me = _rc.GetPosition(), home = Home();
    Track best = null; double bestScore = double.MaxValue;
    foreach (var t in _contacts.Values)
    {
        Vector3D p = t.P(_now);
        if (_mode == Mode.FORM && Vector3D.Distance(p, home) > EngageRadius && !KzLive(t.Id)) continue;
        double score = Vector3D.Distance(p, me);
        score += HiveScore(t) + KzScore(t);                    // split across targets (or focus fire)
        if (t == _tgt) score -= 400;
        score += Math.Max(0, (_now - t.Seen) - 1.0) * 150;  // stale tracks lose to fresh sightings
        if (Vector3D.Distance(p, me) < 150) score -= 350;   // something right on top of us comes first
        if (score < bestScore) { bestScore = score; best = t; }
    }
    if (best != _tgt) _run = Run.SETUP;
    _tgt = best;

    if (_tgt != null)
    {   // swarm attack axis: from the target toward the swarm's centre; my run-in direction is my slot in a cone around it
        Vector3D c = me; foreach (var m in _mates.Values) c += m.Pos; c /= _n;
        Vector3D axis = c - _tgt.P(_now);
        axis = axis.LengthSquared() > 1 ? Vector3D.Normalize(axis) : _rc.WorldMatrix.Backward;
        _attackDir = ConePoint(axis, ConeDeg, _idx, _n);
    }
}

Vector3D ConePoint(Vector3D axis, double coneDeg, int i, int n)
{
    Vector3D u = Vector3D.Cross(axis, Math.Abs(axis.Y) < 0.9 ? Vector3D.Up : Vector3D.Right); u.Normalize();
    Vector3D v = Vector3D.Cross(axis, u);
    double th = coneDeg / 57.2958 * Math.Sqrt((i + 0.5) / Math.Max(n, 1)), ph = i * 2.39996323;
    return Vector3D.Normalize(axis * Math.Cos(th) + (u * Math.Cos(ph) + v * Math.Sin(ph)) * Math.Sin(th));
}

// SATELLITE (command "satellite" / "sat" / "orbit", Start Mode SATELLITE): FORM, but the swarm spreads evenly round a VERTICAL CIRCLE centred on
// Home() (the carrier link ship, else the home point) and the whole ring turns slowly (Satellite Orbit Period). Radius = Satellite Distance, grown
// so neighbours stay apart (more drones = a bigger ring), never inside the carrier hull + 45 m. The ring plane is square to the carrier's heading.
// Ring phase: no clock, each drone averages the phase implied by the mates on the ring. Fights and returns work as in FORM. Same behaviour as
// the SATELLITE formation of the other drones (tools/follow_block.cs). In gravity only the upper arc is used and the drones sway.
bool SatOn = false; double SatDist = 100, SatPeriod = 180, _satPh = 0, _satT = -99, _satR = 100; Vector3D _satFwd = Vector3D.Right, _satV;
double SatBase(int k, int n, bool grav) { return grav ? Math.PI * (k + 0.5) / n : 2 * Math.PI * (k + 0.5) / n; }
Vector3D SatSlot()
{
    double dt = _now - _satT; _satT = _now;
    if (dt > 0 && dt < 1 && SatPeriod > 1) _satPh += 2 * Math.PI * dt / SatPeriod;
    Vector3D c = Home(), hv = HomeVel(), up = Vector3D.Up, g = _rc.GetNaturalGravity();
    bool grav = g.LengthSquared() > 0.1;
    if (grav) up = -Vector3D.Normalize(g);
    if (hv.LengthSquared() > 9) _satFwd = Vector3D.Normalize(_satFwd + (Vector3D.Normalize(hv) - _satFwd) * 0.005);
    Vector3D a = Vector3D.Cross(_satFwd, up);
    if (a.LengthSquared() < 0.01) a = _rc.WorldMatrix.Right;
    a.Normalize(); Vector3D fw = Vector3D.Normalize(Vector3D.Cross(up, a));
    int n = _n, k = _idx;
    if (!grav)
    {
        double sx = Math.Cos(_satPh), sy = Math.Sin(_satPh);
        foreach (var m in _mates.Values)
        {
            int r = _ids.IndexOf(m.Id); double age = _now - m.Seen; if (r < 0 || age > 3) continue;
            Vector3D d = m.Pos + m.Vel * age - c; double x = Vector3D.Dot(d, a), y = Vector3D.Dot(d, up), L = Math.Sqrt(x * x + y * y);
            if (Math.Abs(L - _satR) > 0.3 * _satR || Math.Abs(Vector3D.Dot(d, fw)) > 0.3 * _satR) continue;   // off the ring (fighting)
            double ph = Math.Atan2(y, x) - SatBase(r, n, false); sx += Math.Cos(ph); sy += Math.Sin(ph);
        }
        double dp = Math.Atan2(sy, sx) - _satPh; dp -= 2 * Math.PI * Math.Floor((dp + Math.PI) / (2 * Math.PI));
        _satPh += dp * 0.02;
    }
    double R = Math.Min(600, Math.Max(Math.Max(SatDist, HomePad() + 20), n > 1 ? Math.Max(SepRadius * 1.1, 40) / (2 * Math.Sin((grav ? Math.PI * 0.5 : Math.PI) / Math.Max(n, 2))) : 0));
    _satR = R;
    double th = SatBase(k, n, grav);
    if (grav) th += 0.2 * Math.PI / n * Math.Sin(_satPh); else th += _satPh;
    _satV = grav || SatPeriod <= 1 ? Vector3D.Zero : (a * -Math.Sin(th) + up * Math.Cos(th)) * (R * 2 * Math.PI / SatPeriod);
    return c + (a * Math.Cos(th) + up * Math.Sin(th)) * R;
}

Vector3D SpherePoint(int i, int n)
{
    if (n <= 1) return Vector3D.Zero;
    double y = 1 - 2 * (i + 0.5) / n, r = Math.Sqrt(Math.Max(0, 1 - y * y)), ph = i * 2.39996323;
    return new Vector3D(Math.Cos(ph) * r, y, Math.Sin(ph) * r);
}

double HomePad() { return (_now - _homeRT < 5 && _now - _dynHomeT < 5) ? _homeR + 25 : 0; }   // stay outside a carrier's hull
Vector3D Home()
{
    if (_mode != Mode.RTB && _tgt == null && _now - _assistT < 4) return _assist;   // go help a hive member under fire
    return (_now - _dynHomeT < 5) ? _dynHome : _home;
}

void PowerAndHealth()
{
    double cur = 0, max = 0, stored = 0, cap = 0;
    foreach (var b in _bats) { if (!b.IsWorking) continue; cur += b.CurrentOutput; max += b.MaxOutput; stored += b.CurrentStoredPower; cap += b.MaxStoredPower; }
    _batFrac = cap > 0 ? stored / cap : 1;
    if (max > 0) { double load = cur / max; if (load > 0.97) _powerScale = Math.Max(0.2, _powerScale * 0.9); else if (load < 0.88) _powerScale = Math.Min(1, _powerScale + 0.03); }
    long ammo = 0; int working = 0;
    foreach (var g in _guns) { if (!g.IsFunctional) continue; working++; var inv = g.GetInventory(0); if (inv != null) ammo += inv.CurrentVolume.RawValue; }
    if (ammo > 0) _hadAmmo = true;
    if (_mode == Mode.FORM || _mode == Mode.ATTACK)
    {
        if (cap > 0 && _batFrac < RtbBattery) { _err = "Low battery - RTB"; SetMode(Mode.RTB); }
        else if (working == 0 || (_hadAmmo && ammo <= 0)) { _err = "No guns/ammo - RTB"; SetMode(Mode.RTB); }
    }
}

// =====================================================================
//  CONTROL
void Control(double dt)
{
    if (_mode == Mode.IDLE) return;
    var vels = _rc.GetShipVelocities();
    Vector3D myVel = vels.LinearVelocity, grav = _rc.GetNaturalGravity(), me = _rc.GetPosition();
    double mass = _rc.CalculateShipMass().PhysicalMass;
    double aMax = Math.Max(1, Math.Max(AxisThrust(_fwdThr), AxisThrust(_revThr)) * _powerScale / mass);
    Vector3D sep = Separation(me);
    bool fire = false; _aimErrDeg = 0; _latSpd = 0;

    if (_tgt == null && _raidActive && RaidDir().LengthSquared() > 0.5)
    {   // fan run along the bearing of the incoming fire (see HIT BEARING + RAID)
        Vector3D rd = RaidDir();
        double travelled = Vector3D.Distance(me, _raidMe);
        if (travelled >= RaidMaxTravel && myVel.Length() < 25) { _raidMe = me; _raidPass++; travelled = 0; }   // pass finished without contact: next pass on a new slot
        Vector3D want = (travelled < RaidMaxTravel ? rd * RaidSpeed : Vector3D.Zero) + sep + Evade();
        Vector3D accel = (want - myVel) * AccelGain;
        VectorFly(accel, grav, mass, aMax, rd, dt);
        SetFire(false);
        return;
    }
    if (_tgt == null || _mode == Mode.RTB)
    {   // formation slot around home (or fly home)
        bool sat = SatOn && _mode == Mode.FORM; _satV = Vector3D.Zero;
        Vector3D slot = _mode == Mode.RTB ? Home() + SpherePoint(_idx, _n) * HomePad() : sat ? SatSlot() : Home() + SpherePoint(_idx, _n) * (FormRadius + 6 * Math.Sqrt(_n) + HomePad());
        Vector3D d = slot - me; double sd = d.Length();
        double v = Math.Min(Math.Min(CruiseSpeed, sd * 0.4), Math.Sqrt(2 * aMax * 0.4 * sd));
        Vector3D want = HomeVel() + _satV + (sd > 0.5 ? d / sd * v : Vector3D.Zero) + sep + Evade();   // + the carrier's velocity: keep up with it
        if (_mode == Mode.RTB && sd < 20 && myVel.Length() < 2) { SetMode(Mode.IDLE); _err = "Home - idle"; return; }
        Vector3D accel = (want - myVel) * AccelGain;
        // parked drones face outward from the formation, so the swarm's cameras watch every direction
        Vector3D gUp = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : Vector3D.Zero;
        Vector3D faceHint = myVel.LengthSquared() > 25 ? myVel : (_n > 1 && _mode != Mode.RTB ? (sat ? slot - Home() : SpherePoint(_idx, _n)) : (AutoScan && _mode != Mode.RTB ? SweepDir(gUp) : _ref.WorldMatrix.Forward));
        VectorFly(accel, grav, mass, aMax, faceHint, dt);
        SetFire(false);
        return;
    }

    Vector3D tp = _tgt.P(_now), tv = _tgt.V(_now);
    Vector3D rel = tp - me; double dist = rel.Length();
    Vector3D dir = rel / Math.Max(dist, 1);
    Vector3D relVel = myVel - tv;                         // my velocity in the target's frame
    double closing = Vector3D.Dot(relVel, dir);
    Vector3D latVel = relVel - closing * dir;
    _latSpd = latVel.Length();
    Vector3D latDir = _latSpd > 1 ? latVel / _latSpd : Perp(dir);

    // yo-yo: back off when inside NearRange + braking distance, close again beyond FarRange
    double stop = Math.Max(closing, 0) * Math.Max(closing, 0) / (2 * aMax * 0.6);
    if (dist < NearRange + _tgt.Radius + stop) _opening = true;
    else if (dist > FarRange) _opening = false;
    Vector3D desRel = dir * (_opening ? -BackoffSpeed : ClosingSpeed) + latDir * OrbitSpeed;
    double latErr = (latVel - latDir * OrbitSpeed).Length();

    // ---------- run state machine ----------
    if (_run == Run.SETUP && KzLive(_tgt.Id)) _run = Run.RUN;   // KAMIKAZE DEFENCE: it is coming to us - no run-in, nose on it and shoot
    switch (_run)
    {
        case Run.SETUP:
            if (Vector3D.Dot(-dir, _attackDir) > Math.Cos((ConeDeg + 15) / 57.2958) && dist < SetupRange * 1.15) _run = Run.RUN;
            break;
        case Run.RUN:
        case Run.CORRECT:
            if (dist < BreakRange + _tgt.Radius) { _run = Run.BREAK; _breakDir = latDir; }
            else if (_run == Run.RUN && latErr > LatMax) { _run = Run.CORRECT; _correctT = _now; }
            else if (_run == Run.CORRECT && (latErr < LatMax * 0.4 || _now - _correctT > 4)) _run = Run.RUN;
            break;
        case Run.BREAK:
            if (dist > NearRange + _tgt.Radius + 40) _run = Run.RUN;
            break;
    }

    if (_run == Run.RUN)
    {   // ---- shooting: nose on the lead point, thrust only along the nose ----
        Vector3D gunPos = Vector3D.Zero; int n = 0;
        foreach (var g in _guns) if (g.IsFunctional) { gunPos += g.GetPosition(); n++; }
        gunPos = n > 0 ? gunPos / n : me;
        Vector3D aimDir; double tof;
        Lead(_tgt, gunPos, myVel, out aimDir, out tof);
        bool inRange = tof * ProjSpeed < FireRange;
        Vector3D accel = (KzWant(tv + desRel + sep, me, MaxSpeed) - myVel) * AccelGain - grav;   // KAMIKAZE DEFENCE: back away along the nose axis (reverse thrusters)
        double axial = Vector3D.Dot(accel, aimDir);
        Vector3D lat = accel - axial * aimDir;
        // trim drift by tilting the nose toward the needed sideways push:
        // only inside the fire window when in range, up to 25 deg while still closing in
        double win = Math.Max(0.4, Math.Atan((AimActive(_tgt) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6) / Math.Max(dist, 1)) * 57.2958);
        double tilt = Math.Min((inRange ? win * 0.5 : 25) / 57.2958, lat.Length() / aMax);
        Vector3D nose = aimDir;
        if (lat.LengthSquared() > 1e-4 && Math.Abs(axial) > 0.5) nose = Vector3D.Normalize(aimDir + Vector3D.Normalize(lat) * Math.Tan(tilt) * Math.Sign(axial));
        Vector3D los = _havePrev ? Vector3D.Cross(_prevAim, aimDir) / dt : Vector3D.Zero;
        if (los.LengthSquared() > 4) los = Vector3D.Normalize(los) * 2;
        _prevAim = aimDir; _havePrev = true;
        Aim(nose, los, dt);
        Axial(Vector3D.Dot(accel, _ref.WorldMatrix.Forward) * mass);
        _aimErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(_ref.WorldMatrix.Forward, aimDir), -1, 1)) * 57.2958;
        fire = inRange && _aimErrDeg < win && _now - _tgt.Seen < FireStaleMax && LineClear(gunPos, aimDir, dist);
    }
    else
    {
        _havePrev = false;
        Vector3D want;
        if (_run == Run.SETUP)
        {   // go to my run-in point; arrive already moving in at closing speed
            Vector3D sp = tp + _attackDir * SetupRange;
            Vector3D d = sp - me; double dd = d.Length();
            double v = Math.Min(MaxSpeed, Math.Sqrt(2 * aMax * 0.4 * dd) + ClosingSpeed);
            want = tv + (dd > 1 ? d / dd * v : Vector3D.Zero);
            if (dist < NearRange * 1.5) want += -dir * ClosingSpeed;   // never path through the target
        }
        else if (_run == Run.CORRECT) want = tv + desRel;
        else want = tv + _breakDir * 40 - dir * 20;
        want = KzWant(want + sep + Evade(), me, MaxSpeed);   // KAMIKAZE DEFENCE
        if (want.LengthSquared() > MaxSpeed * MaxSpeed) want = Vector3D.Normalize(want) * MaxSpeed;
        VectorFly((want - myVel) * AccelGain, grav, mass, aMax, rel, dt);
    }
    SetFire(fire);
}

Vector3D Perp(Vector3D d)
{
    Vector3D u = Vector3D.Cross(d, Math.Abs(d.Y) < 0.9 ? Vector3D.Up : Vector3D.Right);
    return Vector3D.Normalize(u);
}

Vector3D Separation(Vector3D me)
{
    Vector3D s = Vector3D.Zero;
    foreach (var m in _mates.Values)
    {
        Vector3D d = me - (m.Pos + m.Vel * (_now - m.Seen));
        double len = d.Length();
        if (len < SepRadius && len > 0.1) s += d / len * (1 - len / SepRadius) * 25;
    }
    return s;
}

// Rotate-to-thrust: point the nose (or tail) along the needed acceleration and burn.
void VectorFly(Vector3D accel, Vector3D grav, double mass, double aMax, Vector3D faceHint, double dt)
{
    Vector3D need = accel - grav;                    // what the thrusters must supply
    double mag = need.Length();
    Vector3D fwd = _ref.WorldMatrix.Forward;
    if (mag < 0.15)
    {   // nothing to do: coast, face the hint (target / travel direction)
        if (faceHint.LengthSquared() > 1e-6) Aim(Vector3D.Normalize(faceHint), Vector3D.Zero, dt);
        Axial(0); return;
    }
    Vector3D nd = need / mag;
    bool useRev = _revThr.Count > 0 && (_fwdThr.Count == 0 || Vector3D.Dot(fwd, nd) < -0.2);
    Aim(useRev ? -nd : nd, Vector3D.Zero, dt);
    // burn only the part of the need the nose is lined up with, so we never add sideways error
    double along = Vector3D.Dot(need, fwd);
    double cosA = Math.Abs(along) / mag;
    double gate = MathHelper.Clamp((cosA - 0.85) / 0.15, 0, 1);
    Axial(along * mass * gate);
}

double AxisThrust(List<IMyThrust> l) { double f = 0; foreach (var t in l) if (t.IsWorking) f += t.MaxEffectiveThrust; return f; }

void Axial(double force)
{
    _rc.DampenersOverride = false;
    double fMax = AxisThrust(_fwdThr), rMax = AxisThrust(_revThr);
    float pf = force > 0 && fMax > 0 ? (float)MathHelper.Clamp(force / fMax * _powerScale, 0, 1) : 0f;
    float pr = force < 0 && rMax > 0 ? (float)MathHelper.Clamp(-force / rMax * _powerScale, 0, 1) : 0f;
    if (pf > 0 && pf < 0.002f) pf = 0.002f;
    if (pr > 0 && pr < 0.002f) pr = 0.002f;
    foreach (var t in _fwdThr) t.ThrustOverridePercentage = pf;
    foreach (var t in _revThr) t.ThrustOverridePercentage = pr;
}

// v2.4: refined with the LEAD block's prediction (fresh acceleration held for the expected rest of the target's manoeuvre, aim point
// across the line of fire with a hull margin for the predicted miss)
Vector3D _aimLos; double _aimNeed;   // line of fire and required hull margin for AimOffset's choice (LEAD block)
double LeadMargin = 1.0;             // aim point keeps this many predicted misses of known hull around it
bool Lead(Track t, Vector3D shooter, Vector3D shooterVel, out Vector3D dir, out double tof)
{
    Vector3D rp0 = t.P(_now) - shooter, rv = t.V(_now) - shooterVel, ta = UseTargetAccel ? LeadAcc(t) : Vector3D.Zero;
    double rl = rp0.Length(), h = LeadHold(t);
    Vector3D los = rl > 1 ? rp0 / rl : Vector3D.Zero;
    _aimLos = los; _aimNeed = LeadMargin * LeadMiss(t, rl / ProjSpeed);
    Vector3D off = AimOffset(t), rp = rp0 + LeadOffset(t, off, los, rl / ProjSpeed);
    double s = ProjSpeed, a = rv.LengthSquared() - s * s, b = 2 * Vector3D.Dot(rp, rv), c = rp.LengthSquared(), tt;
    double disc = b * b - 4 * a * c;
    if (Math.Abs(a) < 1e-6 || disc < 0) tt = rp.Length() / s;
    else { double sq = Math.Sqrt(disc), t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a); tt = (t1 > 0 && (t1 < t2 || t2 <= 0)) ? t1 : t2; }
    if (tt <= 0 || double.IsNaN(tt)) tt = rp.Length() / s;
    Vector3D p = rp;
    for (int i = 0; i < 4; i++) { p = rp0 + LeadOffset(t, off, los, tt) + (rv * tt + ta * LeadK(tt, h)) * LeadScale; tt = p.Length() / s; }
    tof = tt;
    dir = p.LengthSquared() > 1 ? Vector3D.Normalize(p) : _ref.WorldMatrix.Forward;
    return true;
}

// hold fire if a swarm mate is within 10 m of the bullet path
bool LineClear(Vector3D from, Vector3D dir, double dist)
{
    double reach = Math.Min(FireRange * 1.15, dist + 400);
    foreach (var m in _mates.Values)
    {
        Vector3D p = m.Pos + m.Vel * (_now - m.Seen) - from;
        double along = Vector3D.Dot(p, dir);
        if (along < 0 || along > reach) continue;
        if ((p - along * dir).LengthSquared() < 100) return false;
    }
    return true;
}

void Aim(Vector3D desiredFwd, Vector3D ffWorld, double dt)
{
    if (_gyros.Count == 0 || desiredFwd.LengthSquared() < 1e-9) return;
    MatrixD refM = _ref.WorldMatrix, inv = MatrixD.Transpose(refM);
    Vector3D d = Vector3D.Normalize(Vector3D.TransformNormal(desiredFwd, inv));
    Vector3D f = Vector3D.Forward;
    double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(f, d), -1, 1));
    Vector3D axis = Vector3D.Cross(f, d);
    if (axis.LengthSquared() < 1e-12) axis = ang > 1 ? Vector3D.Up : Vector3D.Zero; else axis.Normalize();
    Vector3D err = axis * ang;
    Vector3D derr = (err - _prevErr) / dt;
    if (derr.LengthSquared() > 400) derr = Vector3D.Zero;
    _prevErr = err;
    Vector3D w = Vector3D.TransformNormal(err * AimKp + derr * AimKd, refM) + ffWorld;
    if (w.LengthSquared() > MaxTurn * MaxTurn) w = Vector3D.Normalize(w) * MaxTurn;
    foreach (var g in _gyros)
    {
        if (!g.IsFunctional) continue;
        Vector3D l = Vector3D.TransformNormal(w, MatrixD.Transpose(g.WorldMatrix));
        g.Pitch = -(float)l.X; g.Yaw = -(float)l.Y; g.Roll = -(float)l.Z;   // SE gyro sign convention
        g.GyroOverride = true;
    }
}

void SetFire(bool on)
{
    if (on == _firing && !(on && _tick % 30 == 0)) return;   // re-send "on" twice a second (guns can be switched off behind our back)
    _firing = on;
    foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off");
}

void Release()
{
    foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = g.Yaw = g.Roll = 0; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
    SetFire(false); _havePrev = false;
}

void Display()
{
    _sb.Clear();
    _sb.Append("GNAT SWARM AI v").Append(VER).Append('\n');
    _sb.Append("Mode : ").Append(_mode).Append(_tgt != null && _mode != Mode.RTB ? "  " + _run : "").Append('\n');
    _sb.Append("Swarm: #").Append(_idx + 1).Append(" of ").Append(_n).Append('\n');
    if (SatOn && _mode == Mode.FORM) _sb.Append("Satellite : ring ").Append(_satR.ToString("0")).Append(" m\n");
    if (_tgt != null && _rc != null)
    {
        _sb.Append("Tgt  : ").Append(Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()).ToString("0")).Append(" m  ").Append(_tgt.Src).Append('\n');
        _sb.Append("Drift: ").Append(_latSpd.ToString("0.0")).Append(" m/s  aim ").Append(_aimErrDeg.ToString("0.0")).Append(" deg").Append(_firing ? " FIRE" : "").Append('\n');
    }
    else _sb.Append("Tgt  : none (").Append(_contacts.Count).Append(")\n\n");
    _sb.Append("Sens : ").Append(_sensors.Count).Append("  desig ").Append(_turrets.Count + _ctcs.Count).Append('\n');
    _sb.Append("Thr  : fwd ").Append(_fwdThr.Count).Append(" rev ").Append(_revThr.Count).Append("  bat ").Append((_batFrac * 100).ToString("0")).Append("%\n");
    _sb.Append("Run  : ").Append(_avgMs.ToString("0.000")).Append(" ms\n");
    _sb.Append("Ins  : ").Append(Runtime.CurrentInstructionCount).Append(" / ").Append(Runtime.MaxInstructionCount).Append("  peak ").Append(_peakIns).Append('\n');
    if (_sensors.Count > 0 && _sensorSetupMissing > 0) _sb.Append("! Sensor: set ranges to max + Detect Enemy\n");
    _sb.Append(HiveStatus()); _sb.Append(KzStatus());
    _sb.Append(LaunchStatus());
    if (_tgt == null && _raidActive) _sb.Append("RAID bearing ").Append(_axisSpread.ToString("0")).Append(" deg spread, pass ").Append(_raidPass + 1).Append('\n');
    if (Me.OwnerId == 0) _sb.Append("! PB has no owner\n");
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
    if (KzLive(t.Id)) { t.SpinSince = -1; return false; }   // KAMIKAZE DEFENCE: a kamikaze correcting its course spins like a wreck
    if (KzSpent(t)) return true;   // KAMIKAZE DEFENCE: a former kamikaze with no thrust that is no longer coming for anyone
    if (t.Spin > 0.8) { if (t.SpinSince < 0) t.SpinSince = _now; } else t.SpinSince = -1;
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
    _slimMin.Clear(); _slimMax.Clear();     // cell bounds of every block, kept so a destroyed block can still be located
    foreach (var sb in _slims)
    {
        Vector3I mn = sb.Position, mx = sb.Position;
        if (sb.FatBlock != null) { mn = sb.FatBlock.Min; mx = sb.FatBlock.Max; }
        _slimMin.Add(mn); _slimMax.Add(mx);
    }
}
void CheckDamage()
{
    double sum = 0; var g = Me.CubeGrid;
    bool cmp = _integ >= 0 && _slimInt.Count == _slims.Count;
    if (!cmp) { _slimInt.Clear(); for (int i = 0; i < _slims.Count; i++) _slimInt.Add(0f); }
    _hurt.Clear();
    for (int i = 0; i < _slims.Count; i++)
    {
        var s = _slims[i];
        float v = g.CubeExists(s.Position) ? s.BuildIntegrity - s.CurrentDamage : 0f;
        sum += v;
        if (cmp && _slimInt[i] - v > 0.5f) _hurt.Add(i);
        _slimInt[i] = v;
    }
    if (_integ >= 0 && sum < _integ - 0.5)
    {
        _hitAt = _now; _hits++;
        if (_hurt.Count > 0 && cmp && _now - _lastVoteT >= 0.25) { _lastVoteT = _now; HitVotes(g); }   // throttled: one vote costs a few thousand instructions
    }
    _integ = sum;
}
List<float> _slimInt = new List<float>(); List<int> _hurt = new List<int>(); List<Vector3I> _slimMin = new List<Vector3I>(), _slimMax = new List<Vector3I>();

// ---- v1.9: HIT BEARING + RAID ------------------------------------------------------------------------------------
// A drone with no AI block cannot find a 3 m ship by raycast beyond a few hundred metres (the search cone is far too big),
// but an enemy that shoots at the swarm tells it where it is. A projectile hits the first block along its path, so the
// chance that block B is the one hit from direction u is proportional to the area of B the shooter can see: the sum over
// B's exposed faces of max(0, u.normal). Divided by the same sum over the whole drone (its silhouette from u) this is a
// likelihood; every damaged block multiplies it in for each of 64 candidate directions (world frame, so tumbling does not
// matter), with exponential forgetting. The best direction is the bearing of the shooter. It is shared over IGC ("<Tag>_HIT").
// While there is no target the swarm flies a FAN of runs along it (a cone slot per drone) and sweeps the cone with short
// camera rays; the 50 m sensor catches whatever the drones fly past.
const int HN = 64;
Vector3D[] _hd; float[] _lp = new float[HN]; double _histT = 0, _histMass = 0, _hitLastT = -99, _hitShareT = -9;
int[] _faces = new int[6 * 40];   // per damaged-block slot: exposed face counts (+x,-x,+y,-y,+z,-z)
int[] _allFaces = new int[6];
Vector3D _raidMe; bool _raidActive = false; int _raidScan = 0, _raidPass = 0;
void InitDirs() { if (_hd == null) { _hd = new Vector3D[HN]; for (int i = 0; i < HN; i++) _hd[i] = SpherePoint(i, HN); } }
// exposed face counts of the cells [mn..mx] (the block); faces towards cells of the block itself do not count
void CountFaces(IMyCubeGrid g, Vector3I mn, Vector3I mx, int[] f, int off)
{
    for (int i = 0; i < 6; i++) f[off + i] = 0;
    for (int x = mn.X; x <= mx.X; x++) for (int y = mn.Y; y <= mx.Y; y++) for (int z = mn.Z; z <= mx.Z; z++)
    {
        if (x == mn.X && !g.CubeExists(new Vector3I(x - 1, y, z))) f[off + 1]++;
        if (x == mx.X && !g.CubeExists(new Vector3I(x + 1, y, z))) f[off + 0]++;
        if (y == mn.Y && !g.CubeExists(new Vector3I(x, y - 1, z))) f[off + 3]++;
        if (y == mx.Y && !g.CubeExists(new Vector3I(x, y + 1, z))) f[off + 2]++;
        if (z == mn.Z && !g.CubeExists(new Vector3I(x, y, z - 1))) f[off + 5]++;
        if (z == mx.Z && !g.CubeExists(new Vector3I(x, y, z + 1))) f[off + 4]++;
    }
}
void HitVotes(IMyCubeGrid g)
{
    InitDirs();
    Vector3D o = g.GridIntegerToWorld(Vector3I.Zero);
    Vector3D ex = Vector3D.Normalize(g.GridIntegerToWorld(new Vector3I(1, 0, 0)) - o), ey = Vector3D.Normalize(g.GridIntegerToWorld(new Vector3I(0, 1, 0)) - o), ez = Vector3D.Normalize(g.GridIntegerToWorld(new Vector3I(0, 0, 1)) - o);
    Decay();
    // silhouette from direction u: exposed faces of the whole drone (every present block that is not part of a hurt block is counted too)
    for (int i = 0; i < 6; i++) _allFaces[i] = 0;
    int[] tmp = _faces; int hurtN = Math.Min(_hurt.Count, 4);   // (4: the 64-direction loop costs ~100 instructions per hurt block per direction; 6 could reach the 50k tick limit)
    int sn = _slims.Count, stride = Math.Max(1, (sn + VoteMaxBlocks - 1) / VoteMaxBlocks);   // cap the per-tick work: above VoteMaxBlocks blocks only every stride-th one is counted (x stride)
    for (int i = 0; i < sn; i++)
    {
        int slot = -1;
        for (int q = 0; q < hurtN; q++) if (_hurt[q] == i) slot = q;
        if (slot < 0 && (i % stride != 0 || !g.CubeExists(_slims[i].Position))) continue;      // a destroyed block that was not just hit is gone
        Vector3I mn = _slimMin[i], mx = _slimMax[i];
        if (slot >= 0) { CountFaces(g, mn, mx, tmp, slot * 6); for (int k = 0; k < 6; k++) _allFaces[k] += tmp[slot * 6 + k]; }
        else { CountFaces(g, mn, mx, _tmpF, 0); for (int k = 0; k < 6; k++) _allFaces[k] += _tmpF[k] * stride; }
    }
    float wgt = 1f / hurtN;
    for (int j = 0; j < HN; j++)
    {
        Vector3D u = _hd[j];
        double ux = Vector3D.Dot(u, ex), uy = Vector3D.Dot(u, ey), uz = Vector3D.Dot(u, ez);
        double[] c = _cs; c[0] = Math.Max(0, ux); c[1] = Math.Max(0, -ux); c[2] = Math.Max(0, uy); c[3] = Math.Max(0, -uy); c[4] = Math.Max(0, uz); c[5] = Math.Max(0, -uz);
        double W = 0.3; for (int k = 0; k < 6; k++) W += _allFaces[k] * c[k];
        double lik = 0;
        for (int q = 0; q < hurtN; q++)
        {
            double wb = 0.05; for (int k = 0; k < 6; k++) wb += _faces[q * 6 + k] * c[k];
            lik += Math.Log(wb / W);
        }
        _lp[j] += (float)(lik * wgt);
    }
    _histMass += 1.0; _hitLastT = _now; _axisT = -9;
    if (_now - _hitShareT > 0.5)
    {
        double st; Vector3D ax = HitAxis(out st);
        if (st >= 1) { _hitShareT = _now; IGC.SendBroadcastMessage(Tag + "_HIT", new MyTuple<Vector3D, Vector3D>(_rc.GetPosition(), ax)); }
    }
}
const int VoteMaxBlocks = 160;
int[] _tmpF = new int[6]; double[] _cs = new double[6]; double _lastVoteT = -9;
void Decay()
{
    double dec = Math.Exp(-(_now - _histT) / 12.0); _histT = _now;
    for (int k = 0; k < HN; k++) _lp[k] = (float)(_lp[k] * dec);
    _histMass *= dec;
}
// a mate's axis: a soft vote (von Mises-like) around it
void AddHit(Vector3D axis, double w)
{
    InitDirs(); Decay();
    for (int k = 0; k < HN; k++) _lp[k] += (float)(2.5 * w * Vector3D.Dot(_hd[k], axis));
    _histMass += w; _hitLastT = _now; _axisT = -9;
}
// best bearing + strength (decayed number of hits x how sharply the likelihood peaks); cached for 0.1 s
double[] _sp = new double[18];
Vector3D _axisC; double _axisS, _axisT = -9, _axisSpread = 40;   // spread = half-angle (deg) holding ~80% of the likelihood
Vector3D HitAxis(out double strength)
{
    if (_now - _axisT < 0.1) { strength = _axisS; return _axisC; }
    _axisT = _now; strength = 0; _axisC = Vector3D.Zero; _axisS = 0;
    if (_hd == null || _histMass < 0.05) return Vector3D.Zero;
    int best = 0; for (int k = 1; k < HN; k++) if (_lp[k] > _lp[best]) best = k;
    Vector3D v = Vector3D.Zero; double near = 0, all = 0;
    for (int k = 0; k < 18; k++) _sp[k] = 0;
    for (int j = 0; j < HN; j++)
    {
        double w = Math.Exp(_lp[j] - _lp[best]), c = Vector3D.Dot(_hd[best], _hd[j]);
        all += w; if (c > 0.82) { near += w; v += _hd[j] * w; }
        _sp[Math.Min(17, (int)(Math.Acos(MathHelper.Clamp(c, -1, 1)) * 11.4592))] += w;     // 5 degree bins around the peak
    }
    _axisS = _histMass * Math.Exp(-(_now - _histT) / 12.0) * (near / all);
    { double cum = 0; _axisSpread = 90; for (int k = 0; k < 18; k++) { cum += _sp[k]; if (cum >= 0.8 * all) { _axisSpread = 5 * (k + 1); break; } } }
    _axisC = v.LengthSquared() > 1e-12 ? Vector3D.Normalize(v) : _hd[best];
    strength = _axisS; return _axisC;
}
bool RaidOn
{
    get
    {
        if (_tgt != null || !(_mode == Mode.ATTACK || _mode == Mode.FORM) || _aiOn || _now - _hitLastT >= RaidLife) return false;
        if (_mode == Mode.FORM && Vector3D.Distance(_rc.GetPosition(), Home()) > EngageRadius * 0.5) return false;   // guard mode: do not wander off
        double st; HitAxis(out st);
        return st >= (_raidActive ? RaidMinHold : RaidMinStart);   // wait until the bearing is trustworthy (a few hits)
    }
}
Vector3D RaidDir()
{
    double st; Vector3D axis = HitAxis(out st);
    if (st < 1e-3) return Vector3D.Zero;
    return _n > 1 ? ConePoint(axis, RaidCone, (_idx + _raidPass * 5) % _n, _n) : axis;
}
void RaidScan()
{   // every drone sweeps the whole bearing cone with its own low-discrepancy offset (no sector bookkeeping needed)
    double st; Vector3D axis = HitAxis(out st); if (st < 1e-3) return;
    Vector3D u = Perp(axis), v = Vector3D.Cross(axis, u);
    foreach (var c in _cams)
    {
        if (!c.IsWorking || c.AvailableScanRange < RaidRay || Vector3D.Dot(axis, c.WorldMatrix.Forward) < 0.72) continue;
        _raidScan++;
        double i = _raidScan + _idx * 53.0, frac = (i * 0.6180339887) % 1.0;
        double tr = Math.Tan(Math.Max(8, Math.Min(RaidScanCone, _axisSpread * 1.1)) / 57.2958 * Math.Sqrt(frac)), th = i * 2.39996323;
        var e = c.Raycast(c.GetPosition() + Vector3D.Normalize(axis + u * (tr * Math.Cos(th)) + v * (tr * Math.Sin(th))) * RaidRay);   // (the (double, Vector3D) overload wants a camera-LOCAL direction; a world point is unambiguous)
        if (Valid(e)) AddContact(e, "SCAN");
    }
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
    // half the rays at the bounding-box centre miss a small ship: aim at hull points kept from earlier hits instead
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
    if (_tgt != null) _lastTgtT = _now;
    // the AI also takes over while my lock is stale (nothing seen for 3 s): the confirm rays below pick the enemy up again
    bool want = AiAssist && _ai.Count > 0 && AiModeOk() &&
        ((_tgt == null && _now - _lastTgtT > 2) || (_tgt != null && _now - _tgt.Seen > 3.0));
    want = AiGate(want);   // AI GATE: the AI blocks may do nothing (AI Behavior off): time-share with the script's own search
    if (want != _aiOn)
    {
        _aiOn = want;
        _firing = false;                        // guns' state is unknown after any hand-over: SetFire sends it again
        if (want) Release();                    // thrusters and gyros go to the AI
        foreach (var b in _ai) b.Enabled = want;
    }
    if (!_aiOn) return false;
    // the AI points the guns at what it found (~0.1 deg off): every second ray goes down the boresight, the rest on a
    // 0.15-2.5 deg spiral. The first forward camera casts the long ray, the others the short, frequent one.
    int nFwd = 0; foreach (var c in _cams) if (c.IsWorking && Forwardish(c)) nFwd++;
    bool longCam = nFwd > 1;          // a drone with a single nose camera only casts the short ray (the long one would starve it)
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
    }
    return true;
}
string AiStatus()
{
    if (_ai.Count == 0) return OwnAlert ? "AI alarm: searching\n" : "";
    return AiGateStatus() + "AI: " + (_aiOn ? "searching" : _agT0 >= 0 ? "own search" : "standby") + " (" + _ai.Count + " blocks)" + (OwnAlert ? "  ALARM" : "") + "\n";
}
bool AiModeOk() { return _mode == Mode.FORM || _mode == Mode.ATTACK; }

// =====================================================================
//  v1.8: PRINTER SUPPORT
//  * A drone printed while merged to a printer shares the printer's grid. Blocks are re-scanned
//    whenever this PB's grid changes (merged / released), so it never drives the printer's thrusters.
//  * Take-off after the release is the LAUNCH block (docs/launch-protocol.md); "<Tag>_JOIN" is only an extra trigger there.
IMyCubeGrid _myGrid;
void CheckGrid() { if (Me.CubeGrid != _myGrid) { _myGrid = Me.CubeGrid; GetBlocks(); } }
