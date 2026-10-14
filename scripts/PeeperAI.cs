// =====================================================================
//  PEEPER AI  v0.1  - vitals probe for the Ballista railgun drone
// ---------------------------------------------------------------------
//  A tiny unarmed drone with ONE small turret (no ammunition needed to track) and a camera. It flies close to the target the host
//  is engaging and lets the game's own turret targeting find the vital blocks:
//     turret targeting group  PowerSystems -> Weapons -> PowerSystems -> Propulsion -> ...   (power twice as often: the railguns
//     want the reactors and batteries first)
//  Each time the turret is aimed at the target the script locates the block the barrel points at (the turret reports a block position
//  when the game gives one, else the aim line), a camera ray through that point returns the hull hit, and the hit is converted to the
//  TARGET'S OWN FRAME (so it stays valid while the target moves and turns). One report per sample goes to the host:
//       <Host Tag>_CRIT  = MyTuple<long targetId, int group, Vector3D hitInTargetFrame, Vector3D rayDirInTargetFrame, long probeId>
//  group: 0 PowerSystems, 1 Weapons, 2 Propulsion, 3 Default.  The host's SPOTTER keeps the memory of all vital blocks and feeds the
//  railgun Helm; the probe itself keeps nothing but counters (it is expendable).
//
//  FLIGHT
//   DOCKED  on its printer (a block named "[Bay ..." on the grid, or a connected merge block): everything off, waits.
//   CLEAR   3 s away from the printer (<Tag>_HOME), then
//   SCOUT   target = the host's <Host Tag>_TGT feed. Orbits the target on a sphere of (target radius + Orbit Margin) m, the orbit plane
//           and tilt drifting so every side gets scanned, nose on the target (camera). No target for 20 s -> PARK (hold position).
//  Heartbeat <Tag>_SWARM (the printer counts it for Max Drones) every second.
//  COMMANDS (Run argument): scout | park (both also free the probe from the docked state: bench testing) | reload
//  CUSTOM DATA [Peeper]: IGC Tag, Host Tag, Orbit Margin, Orbit Min, Orbit Speed, Max Speed, Turn Rate, Group Seconds, Power Seconds,
//   Settle Seconds, Lead Speed (m/s of the turret's ammunition to take the lead off the aim line; 0 = none), Ray Past m
//  NOT MEASURED IN THE GAME: that a turret without ammunition still acquires and aims; that the targeting groups pick subsystem blocks of
//  an enemy grid from outside; HitPosition from GetTargetedEntity (the aim-line fallback does not need it).
// =====================================================================
const string VER = "0.1";
string Tag = "PEEPER", HostTag = "BALLISTA";
double OrbitMargin = 35, OrbitMin = 60, OrbitSpeed = 0.12, MaxSpeed = 70, TurnRate = 3, GroupSec = 4, PowerSec = 6, SettleSec = 1.5, LeadSpeed = 0, RayPast = 30;
double ParkAfter = 20, ClearSec = 3;

static readonly string[] Groups = { "PowerSystems", "Weapons", "Propulsion", "Default" };
static readonly int[] Cycle = { 0, 1, 0, 2 };     // power twice per round

enum Mode { DOCKED, CLEAR, SCOUT, PARK }
Mode _mode = Mode.DOCKED;
bool _force;
IMyRemoteControl _rc;
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyGyro> _gyros = new List<IMyGyro>();
List<IMyCameraBlock> _cams = new List<IMyCameraBlock>();
List<IMyLargeTurretBase> _tur = new List<IMyLargeTurretBase>();
List<IMyShipMergeBlock> _mrg = new List<IMyShipMergeBlock>();
List<IMyTerminalBlock> _bay = new List<IMyTerminalBlock>();
List<IMyThrust>[] _thrDir = new List<IMyThrust>[6];
IMyBroadcastListener _lTgt, _lHome;
IMyTextSurface _lcd;
MyIni _ini = new MyIni();
StringBuilder _sb = new StringBuilder();

double _now, _tgtSeen = -99, _modeT, _swT = -99, _cycT, _theta, _theta0, _phi0, _orbKey = -1, _lastSample = -99, _lastBeat = -99, _scanT = -99;
long _tid; Vector3D _tPos, _tVel; double _tRad; bool _haveTgt;
Vector3D _home; bool _homeSet;
int _cyc, _want, _sent; int[] _perGroup = new int[4];
string _log = "", _err = "";
bool _docked;
int _tick;

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
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    try { Load(); GetBlocks(); } catch (Exception ex) { _err = "init: " + ex.Message; }
}

void Load()
{
    _ini.Clear();
    MyIniParseResult pr;
    if (!_ini.TryParse(Me.CustomData, out pr)) _ini.Clear();
    Tag = Cfg("IGC Tag", Tag).ToUpperInvariant(); HostTag = Cfg("Host Tag", HostTag).ToUpperInvariant();
    OrbitMargin = Cfg("Orbit Margin", OrbitMargin); OrbitMin = Cfg("Orbit Min", OrbitMin); OrbitSpeed = Cfg("Orbit Speed", OrbitSpeed);
    MaxSpeed = Cfg("Max Speed", MaxSpeed); TurnRate = Cfg("Turn Rate", TurnRate);
    GroupSec = Cfg("Group Seconds", GroupSec); PowerSec = Cfg("Power Seconds", PowerSec); SettleSec = Cfg("Settle Seconds", SettleSec);
    LeadSpeed = Cfg("Lead Speed", LeadSpeed); RayPast = Cfg("Ray Past", RayPast);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    _lTgt = IGC.RegisterBroadcastListener(HostTag + "_TGT");
    _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME");
}
string Cfg(string k, string d) { if (!_ini.ContainsKey("Peeper", k)) _ini.Set("Peeper", k, d); return _ini.Get("Peeper", k).ToString(d); }
double Cfg(string k, double d) { if (!_ini.ContainsKey("Peeper", k)) _ini.Set("Peeper", k, d); return _ini.Get("Peeper", k).ToDouble(d); }

void GetBlocks()
{
    _thr.Clear(); _gyros.Clear(); _cams.Clear(); _tur.Clear(); _mrg.Clear(); _bay.Clear();
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_gyros, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_cams, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_tur, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_mrg, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_bay, b => b.CustomName.Contains("[Bay "));     // anything of a printer bay on this grid = still fused to the printer
    var rcs = new List<IMyRemoteControl>();
    GridTerminalSystem.GetBlocksOfType(rcs, b => b.CubeGrid == Me.CubeGrid);
    _rc = rcs.Count > 0 ? rcs[0] : null;
    foreach (var c in _cams) c.EnableRaycast = true;
    for (int i = 0; i < 6; i++) _thrDir[i].Clear();
    if (_rc != null)
    {
        MatrixD m = _rc.WorldMatrix;
        Vector3D[] dirs = { m.Forward, m.Backward, m.Left, m.Right, m.Up, m.Down };
        foreach (var t in _thr)
        {
            Vector3D push = t.WorldMatrix.Backward;       // thrusters push opposite their exhaust
            for (int i = 0; i < 6; i++) if (Vector3D.Dot(push, dirs[i]) > 0.9) { _thrDir[i].Add(t); break; }
        }
    }
    _lcd = Me.GetSurface(0);
    _lcd.ContentType = ContentType.TEXT_AND_IMAGE;
    _lcd.FontSize = 0.8f;
}

public void Main(string arg, UpdateType u)
{
    _tick++;
    _now += Runtime.TimeSinceLastRun.TotalSeconds;
    try
    {
        if ((u & (UpdateType.Terminal | UpdateType.Trigger)) != 0 && arg != "") Command(arg.Trim().ToLowerInvariant());
        if (_tick % 60 == 1) GetBlocks();
        ReadIGC();
        _docked = IsDocked();
        if (_rc == null || !_rc.IsFunctional) { _err = "no Remote Control"; Release(); Display(); return; }
        Step();
        if (_now - _lastBeat > 1) { _lastBeat = _now; Beat(); }
        if (_tick % 20 == 0) Display();
    }
    catch (Exception ex) { _err = ex.GetType().Name + ": " + ex.Message; Release(); }
}

void Command(string c)
{
    switch (c)
    {
        case "scout": _force = true; SetMode(Mode.SCOUT); break;
        case "park": _force = true; SetMode(Mode.PARK); break;
        case "reload": Load(); GetBlocks(); break;
        case "armed": case "launch": break;     // the printer's launch orders: nothing to do, the probe leaves when its grid is free of the printer (IsDocked)
    }
}

bool IsDocked()
{
    if (_force) return false;
    foreach (var m in _mrg) if (m.IsConnected) return true;
    foreach (var b in _bay) if (b.CubeGrid == Me.CubeGrid) return true;
    return false;
}

void SetMode(Mode m) { if (_mode != m) { _mode = m; _modeT = _now; } }

void ReadIGC()
{
    while (_lTgt.HasPendingMessage)
    {
        var msg = _lTgt.AcceptMessage();
        if (!(msg.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, double, long>)msg.Data;
        if (d.Item4 < 0) { if (d.Item1 == _tid) _haveTgt = false; continue; }      // target finished
        if (d.Item1 != _tid) _orbKey = -1;
        _tid = d.Item1; _tPos = d.Item2; _tVel = d.Item3; _tRad = d.Item4; _tgtSeen = _now; _haveTgt = true;
    }
    while (_lHome.HasPendingMessage)
    {
        var msg = _lHome.AcceptMessage();
        if (msg.Data is Vector3D) { _home = (Vector3D)msg.Data; _homeSet = true; }
    }
}

Vector3D TgtPos() { return _tPos + _tVel * (_now - _tgtSeen); }

void Step()
{
    if (_docked) { SetMode(Mode.DOCKED); Release(); Rest(); return; }
    if (_mode == Mode.DOCKED) SetMode(Mode.CLEAR);
    bool fresh = _haveTgt && _now - _tgtSeen < ParkAfter;
    double dt = Math.Max(0.016, Runtime.TimeSinceLastRun.TotalSeconds);
    Vector3D pos = _rc.GetPosition(), vel = _rc.GetShipVelocities().LinearVelocity;
    Vector3D wantV = Vector3D.Zero, face = Vector3D.Zero;
    if (_mode == Mode.CLEAR)
    {
        Vector3D away = _homeSet && (pos - _home).LengthSquared() > 1 ? Vector3D.Normalize(pos - _home) : _rc.WorldMatrix.Backward;
        wantV = away * 15;
        if (_now - _modeT > ClearSec) SetMode(fresh ? Mode.SCOUT : Mode.PARK);
    }
    else if (_mode == Mode.SCOUT || _mode == Mode.PARK)
    {
        if (_mode == Mode.PARK && fresh) SetMode(Mode.SCOUT);
        if (_mode == Mode.SCOUT && !fresh) { SetMode(Mode.PARK); _orbKey = -1; }
        if (_mode == Mode.SCOUT)
        {
            Vector3D tp = TgtPos();
            double R = Math.Max(OrbitMin, _tRad + OrbitMargin);
            if (_orbKey < 0)
            {   // start the orbit where the probe is, so the first leg is straight in and never crosses the target
                Vector3D rel = pos - tp; double rl = rel.Length();
                _theta = _theta0 = Math.Atan2(rel.Z, rel.X); _phi0 = rl > 1 ? Math.Asin(MathHelper.Clamp(rel.Y / rl, -1, 1)) : 0; _orbKey = 1;
            }
            _theta += OrbitSpeed * dt;
            double phi = MathHelper.Clamp(_phi0 + 0.7 * Math.Sin(0.37 * (_theta - _theta0)), -1.2, 1.2);      // the tilt sweeps up and down: every side gets its turn
            Vector3D off = new Vector3D(Math.Cos(_theta) * Math.Cos(phi), Math.Sin(phi), Math.Sin(_theta) * Math.Cos(phi)) * R;
            Vector3D want = tp + off;
            Vector3D e = want - pos;
            double sp = Math.Min(MaxSpeed, Math.Sqrt(2 * 6 * Math.Max(0, e.Length() - 2)) + 0.3);   // brake in time (about 6 m/s2)
            wantV = _tVel + (e.LengthSquared() > 0.01 ? Vector3D.Normalize(e) * sp : Vector3D.Zero);
            face = tp - pos; _tvD = face.Length();
            Sample(tp);
        }
        else wantV = Vector3D.Zero;
    }
    Fly(wantV, vel, dt);
    if (face.LengthSquared() > 1) Aim(face); else HoldAttitude();
}

void Fly(Vector3D wantV, Vector3D vel, double dt)
{
    double mass = _rc.CalculateShipMass().PhysicalMass;
    Vector3D grav = _rc.GetNaturalGravity();
    Vector3D a = (wantV - vel) * 1.5;
    double am = a.Length(); if (am > 12) a *= 12 / am;
    ApplyThrust((a - grav) * mass);
}

double _powerScale = 1; IMyTerminalBlock _aimRef { get { return _rc; } }   // (THRUST VECTOR LINK block: the probe has no power throttle and its camera block is the controller)
void ApplyThrust(Vector3D f)
{
    _tvN = f;
    _rc.DampenersOverride = false;
    MatrixD m = _rc.WorldMatrix;
    double[] comp = { Vector3D.Dot(f, m.Forward), -Vector3D.Dot(f, m.Forward), -Vector3D.Dot(f, m.Right), Vector3D.Dot(f, m.Right), Vector3D.Dot(f, m.Up), -Vector3D.Dot(f, m.Up) };
    for (int i = 0; i < 6; i++)
    {
        double max = 0;
        foreach (var t in _thrDir[i]) if (t.IsWorking) max += t.MaxEffectiveThrust;
        float pct = (comp[i] > 0 && max > 0) ? (float)MathHelper.Clamp(comp[i] / max, 0, 1) : 0f;
        foreach (var t in _thrDir[i]) t.ThrustOverridePercentage = pct;
    }
}

Vector3D _prevErr;
void Aim(Vector3D fwd)
{
    Vector3D tvU = Vector3D.Zero, tvF = Vector3D.Zero; TvAim(ref fwd, ref tvU, ref tvF);
    MatrixD refM = _rc.WorldMatrix;
    Vector3D d = Vector3D.Normalize(Vector3D.TransformNormal(fwd, MatrixD.Transpose(refM)));
    double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(Vector3D.Forward, d), -1, 1));
    Vector3D axis = Vector3D.Cross(Vector3D.Forward, d);
    axis = axis.LengthSquared() < 1e-12 ? (ang > 1 ? Vector3D.Up : Vector3D.Zero) : Vector3D.Normalize(axis);
    Vector3D w = Vector3D.TransformNormal(axis * ang * 2.5, refM);
    if (w.LengthSquared() > TurnRate * TurnRate) w = Vector3D.Normalize(w) * TurnRate;
    Gyros(w);
}
void HoldAttitude() { Gyros(Vector3D.Zero); }
void Gyros(Vector3D w)
{
    foreach (var g in _gyros)
    {
        if (!g.IsFunctional) continue;
        Vector3D l = Vector3D.TransformNormal(w, MatrixD.Transpose(g.WorldMatrix));
        g.Pitch = -(float)l.X; g.Yaw = -(float)l.Y; g.Roll = -(float)l.Z;      // SE gyro sign convention (confirmed in game for the drone scripts)
        g.GyroOverride = true;
    }
}

void Release()
{
    foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = g.Yaw = g.Roll = 0; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
}
// docked: the turret may idle
void Rest() { }

// ---------------------------------------------------------------------------------------------------------------
//  VITALS SAMPLING
// ---------------------------------------------------------------------------------------------------------------
double CycleLen(int i) { return Cycle[i] == 0 ? PowerSec : GroupSec; }

void Sample(Vector3D tp)
{
    if (_tur.Count == 0) { _log = "no turret"; return; }
    if (_now - _cycT > CycleLen(_cyc)) { _cyc = (_cyc + 1) % Cycle.Length; _cycT = _now; }
    _want = Cycle[_cyc];
    string wg = Groups[_want];
    foreach (var tu in _tur)
    {
        if (!tu.IsFunctional) { _log = "turret damaged"; continue; }
        string cur;
        try { cur = tu.GetTargetingGroup(); } catch (Exception) { cur = ""; }
        if (cur != wg)
        {
            try { tu.SetTargetingGroup(wg); _swT = _now; } catch (Exception x) { _log = "group not set: " + x.Message; }
            continue;
        }
        if (_now - _swT < SettleSec || _now - _lastSample < 0.4) continue;
        if (!tu.HasTarget) { _log = wg + ": turret has no target"; continue; }
        if (!tu.IsAimed) { _log = wg + ": turret turning"; continue; }
        var te = tu.GetTargetedEntity();
        if (te.EntityId != _tid) { _log = wg + ": turret on another target"; continue; }
        Vector3D o = tu.GetPosition(), q;
        bool hp = te.HitPosition.HasValue && (te.HitPosition.Value - te.Position).LengthSquared() > 1;
        if (hp) q = te.HitPosition.Value;
        else
        {
            Vector3D d; Vector3D.CreateFromAzimuthAndElevation(tu.Azimuth, tu.Elevation, out d);
            d = Vector3D.TransformNormal(d, tu.WorldMatrix);
            double D = Vector3D.Dot(tp - o, d);
            if (D < 5) { _log = wg + ": turret points away"; continue; }
            q = o + d * D;
            if (LeadSpeed > 1)
            {
                Vector3D rv = _tVel - _rc.GetShipVelocities().LinearVelocity;
                q -= rv * (D / LeadSpeed);
            }
        }
        IMyCameraBlock cam = null;
        foreach (var c in _cams)
        {
            if (!c.IsFunctional) continue;
            Vector3D cd0 = q - c.GetPosition();
            if (cd0.LengthSquared() < 1) continue;
            if (Vector3D.Dot(Vector3D.Normalize(cd0), c.WorldMatrix.Forward) < 0.7) continue;
            if (c.CanScan(cd0.Length() + RayPast)) { cam = c; break; }
        }
        if (cam == null) { _log = wg + ": no camera can reach"; continue; }
        Vector3D cd = q - cam.GetPosition(); double cl = cd.Length(); cd /= cl;
        var e = cam.Raycast(q + cd * RayPast);
        if (e.IsEmpty() || e.EntityId != _tid || !e.HitPosition.HasValue) { _log = wg + ": ray missed hull"; continue; }
        MatrixD inv = MatrixD.Transpose(e.Orientation);
        Vector3D local = Vector3D.TransformNormal(e.HitPosition.Value - e.Position, inv);
        Vector3D dir = Vector3D.TransformNormal(cd, inv);
        IGC.SendBroadcastMessage(HostTag + "_CRIT", new MyTuple<long, int, Vector3D, Vector3D, long>(_tid, _want, local, dir, Me.CubeGrid.EntityId));
        _lastSample = _now; _sent++; _perGroup[_want]++;
        _log = wg + ": " + (hp ? "block pos" : "aim line") + ", " + (q - e.HitPosition.Value).Length().ToString("0") + " m under hull";
    }
}

void Beat()
{
    Vector3D v = _rc != null ? _rc.GetShipVelocities().LinearVelocity : Vector3D.Zero;
    IGC.SendBroadcastMessage(Tag + "_SWARM", new MyTuple<long, Vector3D, Vector3D, long, int>(Me.CubeGrid.EntityId, Me.GetPosition(), v, _tid, 0));
}

void Display()
{
    _sb.Clear();
    _sb.Append("PEEPER v").Append(VER).Append("  ").Append(_mode).Append('\n');
    _sb.Append("Host ").Append(HostTag).Append("  tgt ").Append(_haveTgt ? ((TgtPos() - Me.GetPosition()).Length().ToString("0") + " m r " + _tRad.ToString("0")) : "none").Append('\n');
    _sb.Append("Group ").Append(Groups[_want]).Append('\n');
    _sb.Append("Reports ").Append(_sent).Append("  P").Append(_perGroup[0]).Append(" W").Append(_perGroup[1]).Append(" R").Append(_perGroup[2]).Append('\n');
    _sb.Append(_log).Append('\n');
    _sb.Append("Thr ").Append(_thr.Count).Append(" Cam ").Append(_cams.Count).Append(" Tur ").Append(_tur.Count).Append('\n');
    _sb.Append("Ins ").Append(Runtime.CurrentInstructionCount).Append('/').Append(Runtime.MaxInstructionCount).Append('\n');
    if (_err != "") _sb.Append("! ").Append(_err).Append('\n');
    string s = _sb.ToString();
    _lcd.WriteText(s);
    Echo(s);
}
