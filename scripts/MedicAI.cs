// =====================================================================
//  MEDIC AI  v1.1  - field repair drone (docs/design/survivability.md item 18)
// ---------------------------------------------------------------------
//  One PB on the Medic (tools/designs/medic.py): a ship welder on the nose, component stock in a large cargo container named "[Stock]" (15,625 L,
//  two Hornet rebuilds pre-loaded, conveyor-fed to the welder), ion thrusters on all 6 axes, gyros, Remote Control, antenna. It answers the repair calls of damaged drones (their Staff PB's REPAIR
//  CLIENT), flies to the patient, which holds still under its own Staff, holds the welder tip on each missing or damaged block in turn and goes home.
//
//  IGC (default broadcast distance, antenna; M = "Medic Channel", default MEDIC; section 4.3 of the design):
//   <M>_AD    MyTuple<long medicGrid, Vector3D pos, int flags>   flags bit0 free, bit1 stock ok              medic, every 2 s
//   <M>_REQ   MyTuple<long medicGrid, long patientGrid, Vector3D pos, int missing>                         patient Staff
//   <M>_ACK   MyTuple<long patientGrid, long medicGrid, int ok>                                             medic, on each REQ addressed to it;
//             also ok=0 to the patient being serviced when the medic gives the job up (damaged, stock empty, 'rtb'/'idle')
//   <M>_WORK  MyTuple<long patientGrid, Vector3D centre, Vector3D vel, Vector3D p0, Vector3D p1, int packed>  patient Staff, 1 Hz
//             packed = remaining (0-4095) | radius dm << 12 | done << 24 | under fire << 25. p0/p1 = world work points (nearest to the medic
//             first); a zero vector = no point.
//
//  MODES
//   IDLE      hold the home point (where the PB first ran, or 'home'); answer calls when free with >= 300 L of parts aboard (else ACK 0, STOCK LOW;
//             litres, not a share of the container: 300 L is about a tenth of a Hornet, the damage of a typical hit)
//   GO        cruise to the patient (Cruise Speed) until 60 m + its radius
//   APPROACH  relative speed <= Approach Speed: go round the keep-out sphere (patient radius + medic radius + 1 m) to the stand point on the
//             work point's outward normal (patient centre -> point), welder pointing at the point
//   WORK      slide in along the normal (<= Work Speed inside 3 m) until the tip (welder centre + forward x 1.25 m) is Tip Offset off the point,
//             hold relative to the patient's velocity, welders on. Stay until the patient's WORK drops that point, or Work Hold s (then skip it 60 s)
//   NEXT      back out along the old normal to the keep-out sphere, then APPROACH the next point
//   RTB       done bit (or job lost / given up): welders off; first EXIT the patient without turning (straight out along the line from its centre,
//             at most Work Speed at first, then round its keep-out sphere towards home: the long Medic's nose must not swing or slide along the
//             patient's hull), then fly home, then IDLE
//  SAFETY: the medic centre stays outside the keep-out sphere except on the work point's normal; abort to 25 m standoff while the patient reports
//   under fire or changes speed by > 1 m/s; give the job up when an own block is lost, or the stock is empty. FRIENDLY AVOIDANCE (shared block)
//   in GO and the outer APPROACH; the patient itself is exempt (it is put in _contacts).
//  WRITERS: MedicAI is the only writer of this grid's thruster / gyro overrides and of the welders' Enabled.
//  COMMANDS: home (park here) | rtb | idle (drop the job, hold here) | reload
//  CUSTOM DATA [Medic]: Medic Channel, Work Hold (s), Approach Speed, Work Speed, Tip Offset (m), Cruise Speed, Max Job Range (m)
//  NOT MEASURED IN THE GAME: the welder work sphere (centre and radius: tools/designs/reach.py), that the welder pulls components from the
//   [Stock] containers through the junction, collisions at the tip, ion flame damage on the patient (the design keeps the brake ions' nozzles
//   2.5 m behind the welder tip face; their flames, about 0.8 m, stop short of the patient in the WORK pose).
// =====================================================================
const string VER = "1.1", S = "Medic";
string Ch = "MEDIC";
double HoldS = 20, AppSpd = 5, WorkSpd = 0.5, TipOff = 0.6, Cruise = 40, MaxRange = 3000;
const double TipFwd = 1.25, StockMinL = 300, LostS = 10, AckWaitS = 15;

enum Mode { IDLE, GO, APPROACH, WORK, NEXT, RTB }
Mode _mode = Mode.IDLE;
IMyRemoteControl _rc;
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyGyro> _gyros = new List<IMyGyro>();
List<IMyShipWelder> _weld = new List<IMyShipWelder>();
List<IMyTerminalBlock> _stock = new List<IMyTerminalBlock>(), _all = new List<IMyTerminalBlock>();
List<IMyThrust>[] _thrDir = new List<IMyThrust>[6];
Dictionary<long, int> _contacts = new Dictionary<long, int>();     // (FRIENDLY AVOIDANCE: grids it must not avoid; here only the patient)
IMyBroadcastListener _lReq, _lWork;
IMyTextSurface _lcd;
MyIni _ini = new MyIni();
StringBuilder _sb = new StringBuilder();

double _now, _modeT, _adT = -99, _stockF, _stockL, _minA = 1;
int _tick, _blkMax, _blk;
bool _damaged, _homeSet, _welding;
Vector3D _home;
string _log = "", _err = "";

// patient
long _pid; double _workT = -99, _ackT = -99, _pr = 3, _ptT = -99, _moveT = -99, _fireT = -99;
Vector3D _pc, _pv, _pvPrev, _wo, _woOld; bool _haveWp, _done, _haveWork;
Vector3D _exC, _exV; double _exR, _exT = -99;                       // the patient just left (EXIT in RTB)
int _rem;
Vector3D[] _pts = new Vector3D[2]; int _np;
Dictionary<Vector3I, double> _skip = new Dictionary<Vector3I, double>();

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
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    var h = Storage.Split(',');
    double x, y, z;
    if (h.Length == 4 && h[0] == "home" && double.TryParse(h[1], out x) && double.TryParse(h[2], out y) && double.TryParse(h[3], out z)) { _home = new Vector3D(x, y, z); _homeSet = true; }
    try { Load(); GetBlocks(); } catch (Exception ex) { _err = "init: " + ex.Message; }
}

public void Save() { Storage = _homeSet ? "home," + _home.X.ToString("R") + "," + _home.Y.ToString("R") + "," + _home.Z.ToString("R") : ""; }

void Load()
{
    _ini.Clear();
    MyIniParseResult pr;
    if (!_ini.TryParse(Me.CustomData, out pr)) _ini.Clear();
    Ch = Cfg("Medic Channel", Ch).Trim().ToUpperInvariant();
    HoldS = Cfg("Work Hold (s)", HoldS); AppSpd = Cfg("Approach Speed", AppSpd); WorkSpd = Cfg("Work Speed", WorkSpd);
    TipOff = Cfg("Tip Offset (m)", TipOff); Cruise = Cfg("Cruise Speed", Cruise); MaxRange = Cfg("Max Job Range (m)", MaxRange);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    _lReq = IGC.RegisterBroadcastListener(Ch + "_REQ");
    _lWork = IGC.RegisterBroadcastListener(Ch + "_WORK");
}
string Cfg(string k, string d) { if (!_ini.ContainsKey(S, k)) _ini.Set(S, k, d); return _ini.Get(S, k).ToString(d); }
double Cfg(string k, double d) { if (!_ini.ContainsKey(S, k)) _ini.Set(S, k, d); return _ini.Get(S, k).ToDouble(d); }

void GetBlocks()
{
    _thr.Clear(); _gyros.Clear(); _weld.Clear(); _stock.Clear(); _all.Clear();
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_gyros, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_weld, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_all, b => b.CubeGrid == Me.CubeGrid);
    foreach (var b in _all)
        if (b.HasInventory && (b.CustomName.Contains("[Stock]") || b.BlockDefinition.TypeIdString.Contains("CargoContainer"))) _stock.Add(b);
    _blk = 0; foreach (var b in _all) if (b.IsFunctional) _blk++;
    if (_blk > _blkMax) _blkMax = _blk;
    if (_blk < _blkMax && !_damaged) { _damaged = true; if (_pid != 0) Drop("own damage"); }
    if (_blk >= _blkMax) _damaged = false;          // repaired (or 'reload')
    var rcs = new List<IMyRemoteControl>();
    GridTerminalSystem.GetBlocksOfType(rcs, b => b.CubeGrid == Me.CubeGrid && b.IsFunctional);
    _rc = rcs.Count > 0 ? rcs[0] : null;
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
    double cur = 0, max = 0;
    foreach (var b in _stock) { var inv = b.GetInventory(0); if (inv == null) continue; cur += (double)inv.CurrentVolume; max += (double)inv.MaxVolume; }
    _stockF = max > 0 ? cur / max : 0; _stockL = cur * 1000;
    if (_pid != 0 && _stock.Count > 0 && cur <= 0) Drop("stock empty");
    _lcd = Me.GetSurface(0);
    _lcd.ContentType = ContentType.TEXT_AND_IMAGE;
    _lcd.FontSize = 0.75f;
}

public void Main(string arg, UpdateType u)
{
    _tick++;
    _now += Runtime.TimeSinceLastRun.TotalSeconds;
    try
    {
        if ((u & (UpdateType.Terminal | UpdateType.Trigger)) != 0 && arg != "") Command(arg.Trim().ToLowerInvariant());
        if (_tick % 60 == 1) GetBlocks();
        if (_rc == null) { _err = "no Remote Control"; Release(); Display(); return; }
        if (!_homeSet) { _home = _rc.GetPosition(); _homeSet = true; }
        ReadIGC();
        if (_now - _adT >= 2) { _adT = _now; IGC.SendBroadcastMessage(Ch + "_AD", new MyTuple<long, Vector3D, int>(Me.CubeGrid.EntityId, _rc.GetPosition(), (Free() ? 1 : 0) | (StockOk() ? 2 : 0))); }
        Step(Math.Max(0.016, Runtime.TimeSinceLastRun.TotalSeconds));
        if (_tick % 20 == 0) Display();
    }
    catch (Exception ex) { _err = ex.GetType().Name + ": " + ex.Message; Release(); }
}

bool Free() { return _pid == 0 && !_damaged && _weld.Count > 0; }
bool StockOk() { return _stockL >= StockMinL; }

void Command(string c)
{
    switch (c)
    {
        case "home": if (_rc != null) { _home = _rc.GetPosition(); _homeSet = true; } if (_pid == 0) SetMode(Mode.IDLE); break;
        case "rtb": if (_pid != 0) Drop("rtb"); SetMode(Mode.RTB); break;
        case "idle": if (_pid != 0) Drop("idle"); if (_rc != null) _home = _rc.GetPosition(); SetMode(Mode.IDLE); break;
        case "reload": Load(); _blkMax = 0; _damaged = false; GetBlocks(); break;
    }
}

void SetMode(Mode m) { if (_mode != m) { _mode = m; _modeT = _now; } }

// gives the job up: tells the patient (ACK 0), welders off, home
void Drop(string why)
{
    if (_pid != 0) IGC.SendBroadcastMessage(Ch + "_ACK", new MyTuple<long, long, int>(_pid, Me.CubeGrid.EntityId, 0));
    _log = "job dropped: " + why;
    EndJob();
}
void EndJob() { if (_haveWork) { _exC = Pc(); _exV = _pv; _exR = _pr; _exT = _now; } _contacts.Remove(_pid); _pid = 0; _pv = Vector3D.Zero; _haveWp = false; _haveWork = false; _np = 0; _skip.Clear(); Weld(false); SetMode(Mode.RTB); }

void ReadIGC()
{
    long me = Me.CubeGrid.EntityId;
    while (_lReq.HasPendingMessage)
    {
        var msg = _lReq.AcceptMessage();
        if (!(msg.Data is MyTuple<long, long, Vector3D, int>)) continue;
        var d = (MyTuple<long, long, Vector3D, int>)msg.Data;
        if (d.Item1 != me) continue;
        bool ok = d.Item2 == _pid;                     // a repeated request of my patient: ACK again
        if (!ok)
        {
            string no = !Free() ? (_pid != 0 ? "busy" : _damaged ? "damaged" : "no welder") : !StockOk() ? "STOCK LOW" : (d.Item3 - _rc.GetPosition()).Length() > MaxRange ? "too far" : "";
            ok = no == "";
            _log = ok ? "job " + (d.Item2 % 100000) + ", " + d.Item4 + " missing" : "refused " + (d.Item2 % 100000) + ": " + no;
            if (ok) { _pid = d.Item2; _pc = d.Item3; _pv = _pvPrev = Vector3D.Zero; _workT = _now; _haveWork = false; _haveWp = false; _np = 0; _done = false; _rem = d.Item4; _contacts[_pid] = 1; SetMode(Mode.GO); }
        }
        if (ok) _ackT = _now;
        IGC.SendBroadcastMessage(Ch + "_ACK", new MyTuple<long, long, int>(d.Item2, me, ok ? 1 : 0));
    }
    while (_lWork.HasPendingMessage)
    {
        var msg = _lWork.AcceptMessage();
        if (!(msg.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, Vector3D, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, Vector3D, int>)msg.Data;
        if (_pid == 0 || d.Item1 != _pid) continue;
        if (_haveWork && (d.Item3 - _pv).Length() > 1) _moveT = _now;     // the patient is not holding still
        _pvPrev = _pv; _pc = d.Item2; _pv = d.Item3; _workT = _now; _haveWork = true;
        int p = d.Item6;
        _rem = p & 4095; _pr = Math.Max(1, ((p >> 12) & 4095) * 0.1); _done = (p >> 24 & 1) != 0;
        if ((p >> 25 & 1) != 0) _fireT = _now;
        _np = 0;
        if (d.Item4.LengthSquared() > 1e-6) _pts[_np++] = d.Item4 - d.Item2;     // kept relative to the centre
        if (d.Item5.LengthSquared() > 1e-6 && (d.Item5 - d.Item4).LengthSquared() > 0.01) _pts[_np++] = d.Item5 - d.Item2;
        PickPoint();
    }
}

Vector3I Key(Vector3D o) { return new Vector3I((int)Math.Round(o.X * 2), (int)Math.Round(o.Y * 2), (int)Math.Round(o.Z * 2)); }

// keeps the current point while the patient still lists it (it moves to the new offset), else takes the first listed point not skipped
void PickPoint()
{
    var gone = new List<Vector3I>(); foreach (var kv in _skip) if (_now - kv.Value > 60) gone.Add(kv.Key);
    foreach (var k in gone) _skip.Remove(k);
    if (_haveWp)
        for (int i = 0; i < _np; i++)
            if ((_pts[i] - _wo).LengthSquared() < 0.36 && !_skip.ContainsKey(Key(_pts[i]))) { _wo = _pts[i]; return; }
    bool had = _haveWp; Vector3D old = _wo;
    _haveWp = false;
    for (int i = 0; i < _np; i++) if (!_skip.ContainsKey(Key(_pts[i]))) { _wo = _pts[i]; _haveWp = true; _ptT = _now; break; }
    if (had && (!_haveWp || (old - _wo).LengthSquared() > 0.01))
    {
        _woOld = old;
        if (_mode == Mode.WORK) { SetMode(Mode.NEXT); _log = "point done, " + _rem + " left"; }
    }
}

Vector3D Pc() { return _pc + _pv * Math.Min(_now - _workT, 3); }

void Step(double dt)
{
    _minA = MinA();
    Vector3D pos = _rc.GetPosition(), vel = _rc.GetShipVelocities().LinearVelocity;
    Vector3D want = Vector3D.Zero, face = Vector3D.Zero;
    bool fa = false, weld = false;
    if (_pid != 0)
    {
        if (_done) { _log = "patient repaired"; EndJob(); }
        else if (_haveWork ? _now - _workT > LostS : _now - _ackT > AckWaitS) { _log = "patient lost"; EndJob(); }
    }
    double cap = Cruise;
    if (_mode == Mode.IDLE || _mode == Mode.RTB)
    {
        Vector3D e = _home - pos; double d = e.Length();
        if (_mode == Mode.RTB && d < 5) SetMode(Mode.IDLE);
        want = Toward(_home, pos, Cruise);
        if (d > 20) face = e;
        if (_mode == Mode.RTB && _now - _exT < 40)
        {   // EXIT: leave the patient without turning (face stays zero: the gyros hold the attitude)
            Vector3D c = _exC + _exV * (_now - _exT), mc = Me.CubeGrid.WorldVolume.Center, u = Dir(mc - c, -_rc.WorldMatrix.Forward), h = Dir(_home - c, u);
            double rl = (mc - c).Length(), Rk = _exR + Me.CubeGrid.WorldVolume.Radius + 1;
            if (rl < Rk + 1) { want = _exV + u * Math.Min(AppSpd, WorkSpd + (_now - _exT)); face = Vector3D.Zero; cap = AppSpd + _exV.Length(); }
            else if (Vector3D.Dot(u, h) < 0.85 && rl < Rk + 10)
            {
                Vector3D t = h - u * Vector3D.Dot(h, u);
                t = t.LengthSquared() < 1e-6 ? Perp(u) : Vector3D.Normalize(t);
                want = _exV + Toward(c + Vector3D.Normalize(u + t * 0.6) * (Rk + 2), mc, AppSpd); face = Vector3D.Zero; cap = AppSpd + _exV.Length();
            }
            else _exT = -99;
        }
    }
    else if (_mode == Mode.GO)
    {
        Vector3D c = Pc(), e = c - pos; double d = e.Length(), stop = 60 + _pr;
        want = _pv + e / Math.Max(d, 1) * Math.Min(Cruise, Brake(d - stop, AppSpd));
        face = e; fa = true;
        if (d < stop) SetMode(Mode.APPROACH);
    }
    else
    {
        // APPROACH / NEXT / WORK: geometry around the patient (all relative to its centre c)
        Vector3D c = Pc(), mc = Me.CubeGrid.WorldVolume.Center;
        double Rk = _pr + Me.CubeGrid.WorldVolume.Radius + 1;
        IMyShipWelder w = Welder();
        Vector3D tip = w != null ? w.GetPosition() + w.WorldMatrix.Forward * TipFwd : pos;
        Vector3D toTip = tip - mc;
        bool hold = _now - _fireT < 3 || _now - _moveT < 3;
        if (!_haveWp || hold)
        {   // nothing to weld yet, or the patient is under fire / moving: wait at the standoff on my own bearing
            Vector3D u = Dir(mc - c, -_rc.WorldMatrix.Forward);
            want = Toward(c + u * (Rk + (hold ? 25 : 8)), mc, AppSpd);
            face = c - mc; fa = (mc - c).Length() > Rk + 20;
            if (_mode != Mode.APPROACH) SetMode(Mode.APPROACH);
            if (hold) _log = _now - _fireT < 3 ? "patient under fire: standing off" : "patient moving: standing off";
        }
        else
        {
            Vector3D n = Dir(_wo, mc - c), wp = c + _wo;
            Vector3D rel = mc - c; double rl = rel.Length();
            double axial = Vector3D.Dot(rel, n), lat = (rel - n * axial).Length();
            bool onAxis = axial > 0 && lat < 1.0;
            if (_mode == Mode.NEXT)
            {   // back out along the old normal until outside the keep-out sphere
                Vector3D no = Dir(_woOld, rel);
                want = _pv + no * ((tip - c - _woOld).Length() < 3 ? WorkSpd : AppSpd); face = -no;
                if (rl >= Rk - 0.2 || Vector3D.Dot(rel, no) < 0) SetMode(Mode.APPROACH);
            }
            else if (_mode == Mode.APPROACH)
            {
                Vector3D stand = c + n * Math.Max(Rk, _wo.Length() + toTip.Length() + TipOff + 1.5);
                Vector3D tgt = stand;
                Vector3D u = Dir(rel, n);
                if (rl < Rk - 0.3 && !onAxis) tgt = c + u * (Rk + 1);                                  // inside the sphere off the normal: straight out first
                else if (Vector3D.Dot(u, n) < 0.85)
                {   // walk round the sphere towards the normal (never through the patient)
                    Vector3D t = n - u * Vector3D.Dot(n, u);
                    t = t.LengthSquared() < 1e-6 ? Perp(u) : Vector3D.Normalize(t);
                    tgt = c + Vector3D.Normalize(u + t * 0.6) * (Rk + 2);
                }
                want = Toward(tgt, mc, AppSpd);
                face = rl < 3 * Rk ? -n : c - mc; fa = rl > Rk + 20;
                double dq = (stand - mc).Length();
                if (dq < 1.5 && AimErr(w, -n) < 0.09 && (vel - _pv).Length() < 0.5) { SetMode(Mode.WORK); _ptT = _now; }
            }
            if (_mode == Mode.WORK)
            {
                Vector3D goal = wp + n * TipOff, e = goal - tip;
                double ea = Vector3D.Dot(e, n); Vector3D el = e - n * ea;
                bool aligned = AimErr(w, -n) < 0.09;
                if (!aligned || el.Length() > 0.5) ea = Math.Max(ea, 0);                             // only slide in while aligned and on the line
                if (!onAxis && rl < Rk - 0.3) { SetMode(Mode.NEXT); _woOld = _wo; }                  // pushed off the normal inside the sphere: back out
                double sp = (tip - wp).Length() < 3 ? WorkSpd : AppSpd;
                Vector3D v = (n * ea + el) * 0.8; if (v.Length() > sp) v = Vector3D.Normalize(v) * sp;
                want = _pv + v; face = -n;
                weld = (goal - tip).Length() < 1.2;
                if (_now - _ptT > HoldS)
                {   // worked long enough on this point: skip it for 60 s
                    _skip[Key(_wo)] = _now; _woOld = _wo; _log = "point skipped (hold time)"; PickPoint();
                    if (_mode == Mode.WORK) SetMode(Mode.NEXT);
                }
                else _log = "welding, " + _rem + " left, tip " + (goal - tip).Length().ToString("0.0") + " m";
            }
        }
        if (!fa) cap = AppSpd;
    }
    Vector3D faw = FaWant(want, pos);                      // always called: it beacons this grid on DRONE_SPACE
    if (fa) want = faw;
    cap += _pid != 0 ? _pv.Length() : 0;
    if (want.Length() > cap) want = Vector3D.Normalize(want) * cap;
    Weld(weld);
    Fly(want, vel);
    if (face.LengthSquared() > 1e-4) Aim(face, Welder()); else Gyros(Vector3D.Zero);
}

static Vector3D Dir(Vector3D v, Vector3D fb) { return v.LengthSquared() > 0.01 ? Vector3D.Normalize(v) : (fb.LengthSquared() > 1e-6 ? Vector3D.Normalize(fb) : Vector3D.Up); }
static Vector3D Perp(Vector3D u) { Vector3D p = Vector3D.Cross(u, Vector3D.Up); if (p.LengthSquared() < 0.01) p = Vector3D.Cross(u, Vector3D.Right); return Vector3D.Normalize(p); }
double Brake(double d, double vmin) { return Math.Max(vmin, Math.Sqrt(2 * 0.6 * Math.Max(_minA, 0.5) * Math.Max(0, d))); }
Vector3D Toward(Vector3D tgt, Vector3D from, double sp) { Vector3D e = tgt - from; double d = e.Length(); return _pv + (d > 0.05 ? e / d * Math.Min(sp, Math.Min(Brake(d, 0), d * 0.8)) : Vector3D.Zero); }

IMyShipWelder Welder() { foreach (var w in _weld) if (w.IsFunctional) return w; return null; }
double AimErr(IMyShipWelder w, Vector3D f)
{
    Vector3D cur = w != null ? w.WorldMatrix.Forward : _rc.WorldMatrix.Forward;
    return Math.Acos(MathHelper.Clamp(Vector3D.Dot(cur, Vector3D.Normalize(f)), -1, 1));
}

void Weld(bool on)
{
    _welding = on;
    foreach (var w in _weld) if (w.Enabled != on) w.Enabled = on;
}

double MinA()
{
    double mass = _rc.CalculateShipMass().PhysicalMass, m = double.MaxValue;
    for (int i = 0; i < 6; i++) { double f = 0; foreach (var t in _thrDir[i]) if (t.IsWorking) f += t.MaxEffectiveThrust; m = Math.Min(m, f / Math.Max(mass, 1)); }
    return m;
}

void Fly(Vector3D wantV, Vector3D vel)
{
    double mass = _rc.CalculateShipMass().PhysicalMass;
    Vector3D grav = _rc.GetNaturalGravity();
    Vector3D a = (wantV - vel) * 2.0;
    double am = a.Length(), amax = Math.Max(1, _minA * 0.9); if (am > amax) a *= amax / am;
    ApplyThrust((a - grav) * mass);
}

void ApplyThrust(Vector3D f)
{
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

// points the welder's Forward (or the Remote Control's) along fwd
void Aim(Vector3D fwd, IMyTerminalBlock refB)
{
    MatrixD refM = (refB ?? _rc).WorldMatrix;
    Vector3D d = Vector3D.Normalize(Vector3D.TransformNormal(fwd, MatrixD.Transpose(refM)));
    double ang = Math.Acos(MathHelper.Clamp(Vector3D.Dot(Vector3D.Forward, d), -1, 1));
    Vector3D axis = Vector3D.Cross(Vector3D.Forward, d);
    axis = axis.LengthSquared() < 1e-12 ? (ang > 1 ? Vector3D.Up : Vector3D.Zero) : Vector3D.Normalize(axis);
    Vector3D w = Vector3D.TransformNormal(axis * ang * 2.5, refM);
    if (w.LengthSquared() > 9) w = Vector3D.Normalize(w) * 3;
    Gyros(w);
}
void Gyros(Vector3D w)
{
    foreach (var g in _gyros)
    {
        if (!g.IsFunctional) continue;
        Vector3D l = Vector3D.TransformNormal(w, MatrixD.Transpose(g.WorldMatrix));
        g.Pitch = -(float)l.X; g.Yaw = -(float)l.Y; g.Roll = -(float)l.Z;      // SE gyro sign convention (as the drone scripts)
        g.GyroOverride = true;
    }
}

void Release()
{
    foreach (var g in _gyros) { g.GyroOverride = false; g.Pitch = g.Yaw = g.Roll = 0; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    foreach (var w in _weld) w.Enabled = false;
    if (_rc != null) _rc.DampenersOverride = true;
}

void Display()
{
    _sb.Clear();
    _sb.Append("MEDIC v").Append(VER).Append("  ").Append(_mode).Append(_welding ? "  WELD" : "").Append('\n');
    _sb.Append("Channel ").Append(Ch).Append("  stock ").Append(_stockL.ToString("0")).Append(" L (").Append((_stockF * 100).ToString("0")).Append(" %)").Append(StockOk() ? "" : "  STOCK LOW").Append('\n');
    if (_pid != 0 && _rc != null)
        _sb.Append("Patient ").Append(_pid % 100000).Append("  ").Append((Pc() - _rc.GetPosition()).Length().ToString("0")).Append(" m  r ").Append(_pr.ToString("0.0"))
           .Append("  left ").Append(_rem).Append('\n');
    else _sb.Append("No patient").Append(Free() ? "  (free)" : "").Append('\n');
    if (_homeSet && _rc != null) _sb.Append("Home ").Append((_home - _rc.GetPosition()).Length().ToString("0")).Append(" m\n");
    _sb.Append(_log).Append('\n');
    _sb.Append("Thr ").Append(_thr.Count).Append(" Gyro ").Append(_gyros.Count).Append(" Welder ").Append(_weld.Count).Append(_damaged ? "  DAMAGED" : "").Append('\n');
    if (_err != "") _sb.Append("! ").Append(_err).Append('\n');
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
