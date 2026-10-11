// =====================================================================
//  GNAT SWARM AI  v1.8  - minimal "point-and-burn" swarm drone
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
//  COMMANDS (argument, toolbar, or IGC "<Tag>_CMD" to command the swarm)
//   form | attack | rtb | idle | lock | unlock | home | reload
// =====================================================================

const string VER = "1.8";

// ---------------- settings (Custom Data) ----------------
string Tag = "GNAT";
string StartMode = "IDLE";
double ProjSpeed = 400, FireRange = 700;
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
bool AiAssist = true; double AiConfirmRange = 2600;

// ---------------- state ----------------
enum Mode { IDLE, FORM, ATTACK, RTB }
enum Run { SETUP, RUN, CORRECT, BREAK }
Mode _mode = Mode.IDLE; Run _run = Run.SETUP;
MyIni _ini = new MyIni();
double _now = 0; long _tick = 0;
Vector3D _home; bool _homeSet = false;
Vector3D _dynHome; double _dynHomeT = -99;

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
IMyBroadcastListener _lTgt, _lCmd, _lHome, _lSwarm;

class Track
{
    public long Id; public Vector3D Pos, Vel, Acc; public double AccMag;
    public double Spin, SpinSince = -1, Rad0, DeadAt = -1, DisSince = -1; public Vector3D OF, OU; public bool HasOri;
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
    } public double Radius, Seen; public string Src; public MyDetectedEntityType Type;
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
double _aimErrDeg, _latSpd, _avgMs;
StringBuilder _sb = new StringBuilder();

public Program()
{
    LoadConfig();
    _mode = ParseMode(StartMode, Mode.IDLE);
    if (!string.IsNullOrEmpty(Storage))
    {
        var p = Storage.Split(';'); double x, y, z;
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

Mode ParseMode(string s, Mode d)
{
    switch (s.Trim().ToUpperInvariant())
    {
        case "IDLE": return Mode.IDLE; case "FORM": return Mode.FORM; case "ATTACK": return Mode.ATTACK; case "RTB": return Mode.RTB;
        default: return d;
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
    if (_rc == null || !_rc.IsFunctional) { _err = "No working Remote Control"; Display(); Release(); return; }

    ReadIGC(); ReadJoin();
    if (_tick % 20 == 7) CheckDamage();
    if (_tick % 6 == 0) { Broadcast(); PollDesignators(); }
    if (_tick % 10 == 5) PollSensors();
    if (_tick % 10 == 0) { Housekeeping(); PowerAndHealth(); }
    MaintainLock();
    if (_mode != Mode.IDLE) OmniScan();
    if (_tgt == null && !_aiOn && AutoScan && _mode != Mode.IDLE && _mode != Mode.RTB) AutoScanStep();
    if (_tgt != null && _tgt.Src != "IGC" && _tick % 30 == 0) ShareTarget();
    if (!AiStep()) Control(dt);
    if (_tick % 30 == 0) Display();
}

void Command(string a)
{
    var w = a.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (w.Length == 0) return;
    switch (w[0])
    {
        case "form": case "guard": SetMode(Mode.FORM); break;
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
        case "focus": SplitFocus = false; break;
        case "home": if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } break;
        case "reload": LoadConfig(); GetBlocks(); break;
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
    UseTargetAccel = Cfg(S, "Use Target Acceleration", UseTargetAccel);
    SplitFocus = Cfg(S, "Split Targets Across Hive (false = focus fire)", SplitFocus);
    DisabledGrace = Cfg(S, "Seconds Before Dropping Disabled Target", DisabledGrace);
    EvadeSpeed = Cfg(S, "Evade Speed When Hit", EvadeSpeed);
    AimMode = Cfg(S, "Aim Point (AUTO/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();
    AiAssist = Cfg(S, "AI Block Search Assist", AiAssist);
    AiConfirmRange = Cfg(S, "AI Confirm Raycast Range", AiConfirmRange);
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
    }
    while (_lTgt.HasPendingMessage)
    {
        var m = _lTgt.AcceptMessage();
        if (m.Source == IGC.Me || !(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
        if (d.Item4 < 0) { _ignore[d.Item1] = _now + 120; _contacts.Remove(d.Item1); if (_tgt != null && _tgt.Id == d.Item1) _tgt = null; continue; }
        if (Ignored(d.Item1)) continue;
        Track t;
        if (_contacts.TryGetValue(d.Item1, out t) && t.Src != "IGC" && _now - t.Seen < 0.3) continue;
        if (t == null) { t = new Track { Id = d.Item1, Type = MyDetectedEntityType.SmallGrid }; _contacts[d.Item1] = t; }
        t.Set(d.Item2, d.Item3, d.Item4, _now, "IGC");
    }
    while (_lCmd.HasPendingMessage) { var m = _lCmd.AcceptMessage(); if (m.Data is string) Command((string)m.Data); }
    while (_lHome.HasPendingMessage) { var m = _lHome.AcceptMessage(); if (m.Data is Vector3D) { _dynHome = (Vector3D)m.Data; _dynHomeT = _now; } }
}

void Broadcast()
{
    IGC.SendBroadcastMessage(Tag + "_SWARM", new MyTuple<long, Vector3D, Vector3D, long, int>(
        Me.CubeGrid.EntityId, _rc.GetPosition(), _rc.GetShipVelocities().LinearVelocity, _tgt != null ? _tgt.Id : 0, (UnderFire ? 1 : 0) | (OwnAlert ? 2 : 0)));
}

void ShareTarget()
{
    var t = _tgt;
    IGC.SendBroadcastMessage(Tag + "_TGT", new MyTuple<long, Vector3D, Vector3D, double, long>(t.Id, t.P(_now), t.V(_now), t.Radius, Me.EntityId));
}

IMyCameraBlock CamFor(Vector3D pos, double range)
{
    IMyCameraBlock best = null;
    foreach (var c in _cams)
    {
        if (!c.IsWorking || c.AvailableScanRange < range) continue;
        Vector3D d = pos - c.GetPosition(); double len = d.Length();
        if (len < 1 || Vector3D.Dot(d / len, c.WorldMatrix.Forward) < 0.72) continue;
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

void MaintainLock()
{
    if (_tgt == null || _cams.Count == 0 || _now - _tgt.Seen < 0.25) return;
    if ((_tgt.Src == "SENSOR" || _tgt.Src == "TURRET" || _tgt.Src == "CTC") && _now - _tgt.Seen < 0.5) return;
    Vector3D p = _tgt.P(_now), me = _rc.GetPosition();
    Vector3D dir = p - me; double dist = dir.Length();
    if (dist < 1) return;
    bool probingAim;
    Vector3D aim = ProbePoint(_tgt, p, dir / dist, dist, out probingAim);
    var cam = CamFor(aim, dist + 20);
    if (cam == null) return;
    var e = cam.Raycast(aim);
    if (probingAim && (e.IsEmpty() || e.EntityId != _tgt.Id)) AimMissed(_tgt);
    if (!e.IsEmpty() && e.EntityId == _tgt.Id) { if (Valid(e)) AddContact(e, "LOCK"); else _tgt.DeadAt = _now; }
    else if (Valid(e)) AddContact(e, "LOCK");
}

void AutoScanStep()
{
    IMyCameraBlock cam = null;
    foreach (var c in _cams) if (c.IsWorking && Forwardish(c) && c.AvailableScanRange >= ScanRange && (cam == null || c.AvailableScanRange > cam.AvailableScanRange)) cam = c;
    if (cam == null) return;
    _scanIdx++;
    double r = ScanCone * Math.Sqrt((_scanIdx % 120 + 0.5) / 120), th = _scanIdx * 2.39996323;
    var e = cam.Raycast(ScanRange, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
    if (Valid(e)) AddContact(e, "SCAN");
}

// prune, pick target, work out my place in the swarm
void Housekeeping()
{
    _drop.Clear();
    foreach (var kv in _contacts) if (_now - kv.Value.Seen > 4) _drop.Add(kv.Key);
    foreach (var id in _drop) _contacts.Remove(id);
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
        if (_mode == Mode.FORM && Vector3D.Distance(p, home) > EngageRadius) continue;
        double score = Vector3D.Distance(p, me);
        score += HiveScore(t);                    // split across targets (or focus fire)
        if (t == _tgt) score -= 400;
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

Vector3D SpherePoint(int i, int n)
{
    if (n <= 1) return Vector3D.Zero;
    double y = 1 - 2 * (i + 0.5) / n, r = Math.Sqrt(Math.Max(0, 1 - y * y)), ph = i * 2.39996323;
    return new Vector3D(Math.Cos(ph) * r, y, Math.Sin(ph) * r);
}

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

    if (_tgt == null || _mode == Mode.RTB)
    {   // formation slot around home (or fly home)
        Vector3D slot = _mode == Mode.RTB ? Home() : Home() + SpherePoint(_idx, _n) * (FormRadius + 6 * Math.Sqrt(_n));
        Vector3D d = slot - me; double sd = d.Length();
        double v = Math.Min(Math.Min(CruiseSpeed, sd * 0.4), Math.Sqrt(2 * aMax * 0.4 * sd));
        Vector3D want = (sd > 0.5 ? d / sd * v : Vector3D.Zero) + sep + Evade();
        if (_mode == Mode.RTB && sd < 20 && myVel.Length() < 2) { SetMode(Mode.IDLE); _err = "Home - idle"; return; }
        Vector3D accel = (want - myVel) * AccelGain;
        // parked drones face outward from the formation, so the swarm's cameras watch every direction
        Vector3D gUp = grav.LengthSquared() > 0.1 ? -Vector3D.Normalize(grav) : Vector3D.Zero;
        Vector3D faceHint = myVel.LengthSquared() > 25 ? myVel : (_n > 1 && _mode != Mode.RTB ? SpherePoint(_idx, _n) : (AutoScan && _mode != Mode.RTB ? SweepDir(gUp) : _ref.WorldMatrix.Forward));
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
        Vector3D accel = (tv + desRel + sep - myVel) * AccelGain - grav;
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
        fire = inRange && _aimErrDeg < win && LineClear(gunPos, aimDir, dist);
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
        want += sep + Evade();
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

bool Lead(Track t, Vector3D shooter, Vector3D shooterVel, out Vector3D dir, out double tof)
{
    Vector3D rp = t.P(_now) + AimOffset(t) - shooter, rv = t.V(_now) - shooterVel, ta = UseTargetAccel ? t.EA() : Vector3D.Zero;
    double s = ProjSpeed, a = rv.LengthSquared() - s * s, b = 2 * Vector3D.Dot(rp, rv), c = rp.LengthSquared(), tt;
    double disc = b * b - 4 * a * c;
    if (Math.Abs(a) < 1e-6 || disc < 0) tt = rp.Length() / s;
    else { double sq = Math.Sqrt(disc), t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a); tt = (t1 > 0 && (t1 < t2 || t2 <= 0)) ? t1 : t2; }
    if (tt <= 0 || double.IsNaN(tt)) tt = rp.Length() / s;
    Vector3D p = rp;
    for (int i = 0; i < 4; i++) { p = rp + (rv * tt + 0.5 * ta * tt * tt) * LeadScale; tt = p.Length() / s; }
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
    if (on == _firing) return;
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
    if (_tgt != null && _rc != null)
    {
        _sb.Append("Tgt  : ").Append(Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()).ToString("0")).Append(" m  ").Append(_tgt.Src).Append('\n');
        _sb.Append("Drift: ").Append(_latSpd.ToString("0.0")).Append(" m/s  aim ").Append(_aimErrDeg.ToString("0.0")).Append(" deg").Append(_firing ? " FIRE" : "").Append('\n');
    }
    else _sb.Append("Tgt  : none (").Append(_contacts.Count).Append(")\n\n");
    _sb.Append("Sens : ").Append(_sensors.Count).Append("  desig ").Append(_turrets.Count + _ctcs.Count).Append('\n');
    _sb.Append("Thr  : fwd ").Append(_fwdThr.Count).Append(" rev ").Append(_revThr.Count).Append("  bat ").Append((_batFrac * 100).ToString("0")).Append("%\n");
    _sb.Append("Run  : ").Append(_avgMs.ToString("0.000")).Append(" ms\n");
    if (_sensors.Count > 0 && _sensorSetupMissing > 0) _sb.Append("! Sensor: set ranges to max + Detect Enemy\n");
    _sb.Append(HiveStatus());
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
    if (tell) IGC.SendBroadcastMessage(Tag + "_TGT", new MyTuple<long, Vector3D, Vector3D, double, long>(t.Id, t.Pos, t.Vel, -1, Me.EntityId));
}

// Finished = no longer an enemy (its owned blocks are gone), blown down to under 45% of the
// largest size seen, or tumbling out of control for 4 s.
bool Disabled(Track t)
{
    if (t.DeadAt > 0) return true;
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
void ScanSlims()
{
    _slims.Clear();
    var g = Me.CubeGrid; var seen = new HashSet<Vector3I>();
    Vector3I mn = g.Min, mx = g.Max;
    for (int x = mn.X; x <= mx.X; x++) for (int y = mn.Y; y <= mx.Y; y++) for (int z = mn.Z; z <= mx.Z; z++)
    {
        var p = new Vector3I(x, y, z);
        if (!g.CubeExists(p)) continue;
        var s = g.GetCubeBlock(p);
        if (s != null && seen.Add(s.Position)) _slims.Add(s);
    }
    _integ = -1;
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
bool _probeFlip = false;
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
    _probeFlip = !_probeFlip; probingAim = false;
    if (_probeFlip && AimActive(t)) { probingAim = true; return centre + AimOffset(t) + dir * 1.5; }
    if (_now - t.Seen < 0.6 && t.Hull.Count < 48)
    {
        Vector3D r = new Vector3D(_evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5);
        r -= Vector3D.Dot(r, dir) * dir;
        if (r.LengthSquared() > 1e-6) centre += Vector3D.Normalize(r) * t.Radius * 0.6 * _evRng.NextDouble();
    }
    return centre + dir * Math.Min(t.Radius * 0.5, 10);
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
    if (_tgt != null) _lastTgtT = _now;
    bool want = AiAssist && _ai.Count > 0 && _tgt == null && AiModeOk() && _now - _lastTgtT > 2;
    if (want != _aiOn)
    {
        _aiOn = want;
        if (want) Release();                    // thrusters and gyros go to the AI
        foreach (var b in _ai) b.Enabled = want;
    }
    if (!_aiOn) return false;
    // the AI points the guns at what it found: check a tight cone straight ahead, one ray per tick
    foreach (var c in _cams)
    {
        if (!c.IsWorking || !Forwardish(c) || c.AvailableScanRange < AiConfirmRange) continue;
        _aiScan++;
        double r = 3 * Math.Sqrt((_aiScan % 13 + 0.5) / 13.0), th = _aiScan * 2.39996323;
        var e = c.Raycast(AiConfirmRange, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
        if (Valid(e)) AddContact(e, "AI");
        break;
    }
    return true;
}
string AiStatus()
{
    if (_ai.Count == 0) return OwnAlert ? "AI alarm: searching\n" : "";
    return "AI: " + (_aiOn ? "searching" : "standby") + " (" + _ai.Count + " blocks)" + (OwnAlert ? "  ALARM" : "") + "\n";
}
bool AiModeOk() { return _mode == Mode.FORM || _mode == Mode.ATTACK; }

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
        if (_mode == Mode.IDLE && !Docked()) { GetBlocks(); if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } SetMode(Mode.FORM); }
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
