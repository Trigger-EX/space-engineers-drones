// =====================================================================
//  STINGER AI  v1.1  - kamikaze interceptor (small grid, warheads)
// ---------------------------------------------------------------------
//  Sits passive until launched (from a printer rack or by command), then
//  flies an intercept course at the nearest enemy, arms its warheads only
//  near the target and well clear of home, and detonates at closest
//  approach. Moves by pointing its forward thrusters (like the Gnat).
//
//  Targets: its own nose camera, a sensor, and targets shared over antenna
//  by Gnats / Hornets / Longbows / carriers (any of the listed IGC tags).
//
//  COMMANDS: launch | abort | disarm | reload
//    launch  only works once it's off the rack (its merge block released)
// =====================================================================
const string VER = "1.1";

string ListenTags = "GNAT,HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER,DRONE_CUE";   // a tag that already contains "_" is used as is, others get "_TGT" appended
string Tag = "STINGER";
double MaxSpeed = 100, LaunchRange = 3000, ArmRange = 200, SafeDistance = 120, Gain = 2.5;
bool AutoLaunch = true, TargetLarge = true, TargetSmall = true, TargetNeutral = false;
double AimKp = 9, AimKd = 0.3, MaxTurn = 8;

enum Mode { WAIT, HUNT, IDLE }
Mode _mode = Mode.WAIT;
MyIni _ini = new MyIni();
double _now; long _tick; int _peakIns = 0;   // PB limit is Runtime.MaxInstructionCount per run
IMyShipController _rc;
List<IMyGyro> _gyros = new List<IMyGyro>();
List<IMyThrust> _fwd = new List<IMyThrust>();
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyWarhead> _heads = new List<IMyWarhead>();
List<IMyCameraBlock> _cams = new List<IMyCameraBlock>();
List<IMySensorBlock> _sensors = new List<IMySensorBlock>();
List<MyDetectedEntityInfo> _sensed = new List<MyDetectedEntityInfo>();
List<IMyShipMergeBlock> _merges = new List<IMyShipMergeBlock>();
List<IMyBroadcastListener> _lTgt = new List<IMyBroadcastListener>();
IMyBroadcastListener _lCmd;
IMyTextSurface _lcd;
IMyCubeGrid _myGrid;
Vector3D _launchPos; bool _armed;
double _prevDist = double.MaxValue;
Vector3D _prevErr;
string _status = "";

class Track { public long Id; public Vector3D Pos, Vel, Acc; public double Radius, Seen; }
Dictionary<long, Track> _contacts = new Dictionary<long, Track>();
Track _tgt;

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
    LoadConfig();
    FleetLoad(true);
    foreach (var t in ListenTags.Split(',')) if (t.Trim() != "") _lTgt.Add(IGC.RegisterBroadcastListener(t.Trim().Contains("_") ? t.Trim() : t.Trim() + "_TGT"));
    _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
    GetBlocks();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

void LoadConfig()
{
    MyIniParseResult r; if (!_ini.TryParse(Me.CustomData, out r)) return;
    const string S = "Stinger";
    ListenTags = C(S, "Listen To IGC Tags", ListenTags);
    Tag = C(S, "Own Command Tag", Tag);
    AutoLaunch = C(S, "Auto Launch When Released", AutoLaunch);
    LaunchRange = C(S, "Max Target Distance", LaunchRange);
    ArmRange = C(S, "Arm Warheads Within", ArmRange);
    SafeDistance = C(S, "Never Arm Within This Of Launch Point", SafeDistance);
    MaxSpeed = C(S, "Max Speed", MaxSpeed);
    TargetLarge = C(S, "Target Large Grids", TargetLarge);
    TargetSmall = C(S, "Target Small Grids", TargetSmall);
    TargetNeutral = C(S, "Target Neutral/Unowned", TargetNeutral);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}
string C(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double C(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool C(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

// only blocks on this PB's own grid: while docked the rack's blocks are on the same grid
void GetBlocks()
{
    _myGrid = Me.CubeGrid;
    var ctrls = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(ctrls, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("Stinger"));
    _rc = ctrls.Count > 0 ? ctrls[0] : null;
    GridTerminalSystem.GetBlocksOfType(_gyros, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("Stinger"));
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("Stinger"));
    GridTerminalSystem.GetBlocksOfType(_heads, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("Stinger"));
    GridTerminalSystem.GetBlocksOfType(_cams, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("Stinger"));
    GridTerminalSystem.GetBlocksOfType(_sensors, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("Stinger"));
    GridTerminalSystem.GetBlocksOfType(_merges, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("Stinger"));
    foreach (var c in _cams) c.EnableRaycast = true;
    _fwd.Clear();
    if (_rc != null) foreach (var t in _thr) if (Vector3D.Dot(t.WorldMatrix.Backward, _rc.WorldMatrix.Forward) > 0.9) _fwd.Add(t);
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.8f;
}

bool Docked() { foreach (var m in _merges) if (m.IsConnected) return true; return false; }

bool Valid(MyDetectedEntityInfo e)
{
    if (e.IsEmpty() || e.EntityId == Me.CubeGrid.EntityId) return false;
    if (e.Type == MyDetectedEntityType.LargeGrid ? !TargetLarge : e.Type == MyDetectedEntityType.SmallGrid ? !TargetSmall : true) return false;
    var r = e.Relationship;
    return r == MyRelationsBetweenPlayerAndBlock.Enemies || (TargetNeutral && (r == MyRelationsBetweenPlayerAndBlock.Neutral || r == MyRelationsBetweenPlayerAndBlock.NoOwnership));
}

void Add(long id, Vector3D p, Vector3D v, double r)
{
    Track t;
    if (!_contacts.TryGetValue(id, out t)) { t = new Track { Id = id }; _contacts[id] = t; }
    double dt = _now - t.Seen;
    if (t.Seen > 0 && dt > 0.05 && dt < 2) t.Acc = t.Acc * 0.7 + (v - t.Vel) / dt * 0.3;
    t.Pos = p; t.Vel = v; t.Radius = Math.Max(r, 1.5); t.Seen = _now;
}

public void Main(string arg, UpdateType src)
{
    try { Tick(arg, src); }
    catch (Exception ex) { _status = "ERROR " + ex.Message; Echo(_status); }
}

void Tick(string arg, UpdateType src)
{
    if (!string.IsNullOrWhiteSpace(arg)) Command(arg.Trim().ToLowerInvariant());
    if ((src & UpdateType.Update1) == 0) return;
    double dt = Runtime.TimeSinceLastRun.TotalSeconds; if (dt <= 0 || dt > 0.5) dt = 1 / 60.0;
    _now += dt; _tick++;
    if (Me.CubeGrid != _myGrid || _tick % 600 == 0) GetBlocks();
    for (int li = 0; li < _lTgt.Count + FleetTgtL.Count; li++)
    {
        var l = li < _lTgt.Count ? _lTgt[li] : FleetTgtL[li - _lTgt.Count];
        while (l.HasPendingMessage)
        {
            var m = l.AcceptMessage();
            if (!(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
            if (!FleetOk(d)) continue;
            if (d.Item4 < 0) { _contacts.Remove(d.Item1); continue; }      // reported finished by the hive
            Add(d.Item1, d.Item2, d.Item3, d.Item4);
        }
    }
    while (_lCmd.HasPendingMessage) { var m = _lCmd.AcceptMessage(); if (m.Data is string) Command(((string)m.Data).ToLowerInvariant()); }
    FleetPump(Command, p => { });
    if (_tick % 10 == 0)
    {
        foreach (var s in _sensors) { if (!s.IsWorking) continue; _sensed.Clear(); s.DetectedEntities(_sensed); foreach (var e in _sensed) if (Valid(e)) Add(e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() / 2); }
        var drop = new List<long>(); foreach (var kv in _contacts) if (_now - kv.Value.Seen > 5) drop.Add(kv.Key);
        foreach (var id in drop) _contacts.Remove(id);
        if (_tgt != null && !_contacts.ContainsKey(_tgt.Id)) _tgt = null;
    }
    if (_rc == null) { _status = "No remote control"; Show(); return; }

    bool docked = Docked();
    if (_mode == Mode.WAIT && !docked && AutoLaunch && Nearest() != null) Launch();
    if (_mode == Mode.HUNT) Hunt(dt);
    _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount);
    if (_tick % 20 == 0) Show();
}

void Command(string a)
{
    if (a.StartsWith("launch")) { if (!Docked()) Launch(); else _status = "Still docked"; }
    else if (a == "abort") { _mode = Mode.IDLE; Disarm(); Release(); }
    else if (a == "disarm") Disarm();
    else if (a == "reload") { LoadConfig(); FleetLoad(true); GetBlocks(); }
}

Track Nearest()
{
    Track best = null; double bd = LaunchRange; Vector3D me = Me.GetPosition();
    foreach (var t in _contacts.Values) { double d = Vector3D.Distance(t.Pos, me); if (d < bd) { bd = d; best = t; } }
    return best;
}

void Launch()
{
    _tgt = Nearest();
    if (_tgt == null) { _status = "No target in range"; return; }
    GetBlocks();
    _mode = Mode.HUNT; _launchPos = _rc.GetPosition(); _prevDist = double.MaxValue;
}

void Hunt(double dt)
{
    if (_tgt == null) { _tgt = Nearest(); if (_tgt == null) { _status = "Lost target"; Release(); _mode = Mode.WAIT; return; } }
    // keep the lock with the nose camera when possible
    double age = _now - _tgt.Seen;
    Vector3D me = _rc.GetPosition(), myVel = _rc.GetShipVelocities().LinearVelocity, g = _rc.GetNaturalGravity();
    Vector3D tp = _tgt.Pos + _tgt.Vel * age, tv = _tgt.Vel;
    if (age > 0.2)
        foreach (var c in _cams)
        {
            double d0 = Vector3D.Distance(tp, c.GetPosition());
            if (!c.IsWorking || !c.CanScan(d0 + 20)) continue;
            var e = c.Raycast(tp + Vector3D.Normalize(tp - c.GetPosition()) * 3);
            if (!e.IsEmpty() && Valid(e)) { Add(e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() / 2); if (e.EntityId != _tgt.Id) _tgt = _contacts[e.EntityId]; }
            break;
        }
    Vector3D rel = tp - me; double dist = rel.Length();
    double closing = Math.Max(20, Vector3D.Dot(myVel - tv, rel / Math.Max(dist, 1)));
    double tgo = MathHelper.Clamp(dist / closing, 0, 10);
    Vector3D pred = tp + tv * tgo + 0.5 * _tgt.Acc * tgo * tgo;
    Vector3D want = Vector3D.Normalize(pred - me) * MaxSpeed;
    Vector3D need = (want - myVel) * Gain - g;
    // point-and-burn: nose along the needed push; burn only the aligned part
    Vector3D fwd = _rc.WorldMatrix.Forward;
    if (need.LengthSquared() > 0.01) Aim(Vector3D.Normalize(need), dt);
    double along = Vector3D.Dot(need, fwd), mag = need.Length();
    double gate = mag > 0 ? MathHelper.Clamp((along / mag - 0.8) / 0.2, 0, 1) : 0;
    double fmax = 0; foreach (var t in _fwd) if (t.IsWorking) fmax += t.MaxEffectiveThrust;
    float pct = fmax > 0 ? (float)MathHelper.Clamp(along * _rc.CalculateShipMass().PhysicalMass / fmax * gate, 0, 1) : 0;
    foreach (var t in _fwd) t.ThrustOverridePercentage = Math.Max(pct, 0.001f);
    _rc.DampenersOverride = false;

    // arm close to the target, never near home; detonate at closest approach
    bool clearOfHome = Vector3D.Distance(me, _launchPos) > SafeDistance;
    if (!_armed && clearOfHome && dist < ArmRange) { foreach (var w in _heads) w.IsArmed = true; _armed = true; }
    // The blast reaches only 4.5 m, so detonate on the HULL, not on the target's centre: while armed and close, every
    // tick a short ray from a forward camera measures the distance to the first block ahead (works for a 60 m ship as
    // well as a 5 m one). The centre-distance rule stays as the fallback when no camera has charge.
    bool boom = false;
    if (_armed && dist < 100)
    {
        double clos = Math.Max(20, Vector3D.Dot(myVel - tv, rel / Math.Max(dist, 1)));
        foreach (var c in _cams)
        {
            if (!c.IsWorking || Vector3D.Dot(c.WorldMatrix.Forward, fwd) < 0.97 || !c.CanScan(45)) continue;
            var he = c.Raycast(45);
            if (!he.IsEmpty() && he.HitPosition.HasValue && Valid(he) && Vector3D.Distance(he.HitPosition.Value, c.GetPosition()) < Math.Max(5, clos * dt * 2.5)) boom = true;
            break;
        }
    }
    if (_armed && (boom || dist < 3 + Math.Min(_tgt.Radius * 0.3, 6) || (dist < 30 && dist > _prevDist + 0.05)))
        foreach (var w in _heads) w.Detonate();
    _prevDist = dist;
    _status = "HUNT " + dist.ToString("0") + " m  tgo " + tgo.ToString("0.0") + "s" + (_armed ? "  ARMED" : "");
}

void Aim(Vector3D desired, double dt)
{
    MatrixD m = _rc.WorldMatrix, inv = MatrixD.Transpose(m);
    Vector3D d = Vector3D.Normalize(Vector3D.TransformNormal(desired, inv));
    double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(Vector3D.Forward, d), -1, 1));
    Vector3D axis = Vector3D.Cross(Vector3D.Forward, d);
    axis = axis.LengthSquared() > 1e-12 ? Vector3D.Normalize(axis) : Vector3D.Zero;
    Vector3D err = axis * ang, derr = (err - _prevErr) / dt; _prevErr = err;
    if (derr.LengthSquared() > 400) derr = Vector3D.Zero;
    Vector3D w = Vector3D.TransformNormal(err * AimKp + derr * AimKd, m);
    if (w.LengthSquared() > MaxTurn * MaxTurn) w = Vector3D.Normalize(w) * MaxTurn;
    foreach (var gy in _gyros)
    {
        Vector3D l = Vector3D.TransformNormal(w, MatrixD.Transpose(gy.WorldMatrix));
        gy.Pitch = -(float)l.X; gy.Yaw = -(float)l.Y; gy.Roll = -(float)l.Z; gy.GyroOverride = true;   // SE gyro sign convention
    }
}

void Disarm() { foreach (var w in _heads) w.IsArmed = false; _armed = false; }
void Release()
{
    foreach (var g in _gyros) { g.GyroOverride = false; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
}

void Show()
{
    string s = "STINGER v" + VER + "\nMode: " + _mode + (Docked() ? "  (docked)" : "") + "\nContacts: " + _contacts.Count +
               "\nWarheads: " + _heads.Count + (_armed ? " ARMED" : " safe") + "\n" + _status +
               "\nIns: " + Runtime.CurrentInstructionCount + " / " + Runtime.MaxInstructionCount + "  peak " + _peakIns;
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
