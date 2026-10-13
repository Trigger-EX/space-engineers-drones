// =====================================================================
//  HIVE MOTHER  v2.1  - drone printer controller (projector + welders + merge block bays)
// ---------------------------------------------------------------------
//  A bay is the blocks whose names contain "[Bay 1]", "[Bay 2]", ...:
//  ONE projector (holding the drone blueprint, Keep Projection ON), its
//  welders, and the merge block the drone latches onto. Drone blocks are
//  recognised by their name prefix ("Gnat", "Hornet", ...) per bay.
//
//  CYCLE PER BAY
//   BUILD     projector + welders on until the projector reports 0 blocks left
//             (RemainingBlocks counts blocks not yet PLACED, not welded ones).
//   FINISH    welders STAY ON until the drone is COMPLETE: every block in the bay's
//             "Drone Box Min/Max" cell box is full integrity (and functional), the
//             cell count matches "Drone Cell Count". Scanned incrementally (150 cells
//             per tick). Stalls are diagnosed per welder and the unfinished blocks
//             are listed on the LCD; welders are never all switched off. Without the
//             box keys it falls back to a name-prefix check plus a conservative timer.
//   CHARGE    welders off, drone stays merged and shares this ship's power; its
//             Programmable Block is recompiled (a welded PB is not auto-compiled)
//             and the drone batteries charge to 95 % (or Max Charge Seconds).
//   ARM       while the drone is still on this grid its PB is visible: the [Launch]
//             section of its Custom Data is written (Auto, Burst Seconds, Away X/Y/Z,
//             Exit Direction, Armed) and the PB is run with the argument "armed".
//   RELEASE   both merge blocks off, the printer nudges away from the drone with
//             the thrusters that push the right way (the thrust gap), then it checks
//             that the drone really is a separate grid (with a grace period) and from
//             then on broadcasts "<Tag>_JOIN" and FLEET_CMD "join" every 2 s for
//             "Join Seconds" (>= 30) and again whenever a new drone shows up.
//   COOLDOWN  waits for the old drone to clear the projection, merge on, repeat.
//  RACK MODE (Stingers): finished drones stay docked; one is released per enemy
//  target seen and told to "launch" on the Rack Launch Command Tag.
//
//  Keeps "Max Drones" (total) and each tag's "Max Alive" by counting the hive's
//  _SWARM broadcasts. Broadcasts the printer as each tag's home point (_HOME).
//  Optional carrier relay: forwards the turrets' targets to every tag (_TGT).
//
//  COMMANDS: start | stop | max <n> | release [bay] | hold <bay> | unhold <bay> |
//            reload | relay on|off
//  CONFIG: Custom Data ([Hive Mother] and [Bay N] sections, created on first run).
//   [Bay N] keys: Drone Box Min / Drone Box Max ("x,y,z" printer-grid cells),
//   Drone Cell Count, Drone Cell Tolerance, Ignore Cells ("x,y,z;x,y,z"),
//   Skip Finish Check (escape hatch), Launch Burst Seconds.
// =====================================================================
const string VER = "2.1";
const int MaxBays = 12;

int MaxDrones = 8;
bool RackMode = false;
string LaunchTag = "STINGER";
double ChargeSeconds = 25, CooldownSeconds = 6, StallSeconds = 45, BuildTimeout = 600, FaultSeconds = 60;
double NudgeSeconds = 1.5, NudgePower = 0.25, GapSeconds = 1.0, RecompileWait = 3, MatchRadius = 12;
double FinishTimeout = 360, FinishFallback = 90, ArmSeconds = 3, JoinSeconds = 40, FusedGrace = 8, LaunchBurst = 4;
string FleetCh = "FLEET";
const int ScanBudget = 150;
int ParallelBuilds = 2;
bool Printing = true, DoRecompile = true, Relay = false;
int _peakIns = 0;

MyIni _ini = new MyIni();
double _now, _dt; long _tick;
Dictionary<string, double> _joinUntil = new Dictionary<string, double>(), _joinNext = new Dictionary<string, double>();
int _rackLaunchLeft = 0;

class Bay
{
    public int N; public string Tag = "GNAT", Prefix = "Gnat"; public bool Held; public int Cap = 8, MaxPrints = 0, Printed = 0;
    public IMyProjector Proj; public List<IMyShipWelder> Welders = new List<IMyShipWelder>(); public IMyShipMergeBlock Merge;
    public IMyShipMergeBlock DMerge; public IMyProgrammableBlock DPb; public List<IMyBatteryBlock> DBats = new List<IMyBatteryBlock>();
    public string State = "IDLE"; public double T; public int Rem = -1; public double LastProgress; public bool SawWork, Recompiled, Nudged;
    public string Msg = ""; public int Faults; public Vector3D Away; public string Exit = "";
    // finish check
    public bool HasBox, SkipFinish, Starved, PassOk, HavePass; public Vector3I BMin, BMax; public int Cells, Tol; public string BoxRaw = "NONE", CfgWarn = "";
    public HashSet<long> Ignore = new HashSet<long>(), Seen = new HashSet<long>();
    public long ScanIdx; public int PBlocks, PCells, PBad, LBlocks, LCells, LBad; public float PInteg, LInteg;
    public List<string> PBadL = new List<string>(); public string LBadText = ""; public double FinishT, Burst = -1;
    // welder kick (one welder at a time)
    public IMyShipWelder KickW; public double KickT; public int KickIdx;
    // take-off
    public bool Armed, LaunchWritten, RackLaunch; public double FusedT;
}
List<Bay> _bays = new List<Bay>();
Dictionary<string, IMyBroadcastListener> _lSwarm = new Dictionary<string, IMyBroadcastListener>();
Dictionary<string, Dictionary<long, double>> _alive = new Dictionary<string, Dictionary<long, double>>();
Dictionary<string, int> _cap = new Dictionary<string, int>();
List<IMyBroadcastListener> _lTgt = new List<IMyBroadcastListener>();
string TargetTags = "GNAT,HORNET,LONGBOW";
Dictionary<long, double> _targets = new Dictionary<long, double>();
int _launched = 0;
IMyTextSurface _lcd;
StringBuilder _sb = new StringBuilder();
List<IMyTerminalBlock> _stock = new List<IMyTerminalBlock>();
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
List<IMyTurretControlBlock> _ctcs = new List<IMyTurretControlBlock>();
List<IMyTerminalBlock> _tmp = new List<IMyTerminalBlock>();
List<IMyProgrammableBlock> _pbs = new List<IMyProgrammableBlock>();
List<IMyBatteryBlock> _bats = new List<IMyBatteryBlock>();
List<IMyShipMergeBlock> _mergesAll = new List<IMyShipMergeBlock>();

public Program()
{
    LoadConfig();
    GetBlocks();
    if (RackMode) foreach (var t in TargetTags.Split(',')) if (t.Trim() != "") _lTgt.Add(IGC.RegisterBroadcastListener(t.Trim() + "_TGT"));
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

void LoadConfig()
{
    MyIniParseResult r; if (!_ini.TryParse(Me.CustomData, out r)) return;
    const string S = "Hive Mother";
    MaxDrones = (int)C(S, "Max Drones (all tags)", MaxDrones);
    RackMode = C(S, "Rack Mode (keep docked, launch on targets)", RackMode);
    LaunchTag = C(S, "Rack Launch Command Tag", LaunchTag);
    TargetTags = C(S, "Rack Target Tags (enemy sightings that release a drone)", TargetTags);
    ChargeSeconds = C(S, "Max Charge Seconds", ChargeSeconds);
    CooldownSeconds = C(S, "Cooldown Seconds", CooldownSeconds);
    StallSeconds = C(S, "Stall Warning Seconds", StallSeconds);
    BuildTimeout = C(S, "Build Timeout Seconds", BuildTimeout);
    FaultSeconds = C(S, "Fault Retry Seconds", FaultSeconds);
    ParallelBuilds = (int)C(S, "Parallel Builds", ParallelBuilds);
    DoRecompile = C(S, "Recompile Drone PB Before Release", DoRecompile);
    RecompileWait = C(S, "Recompile Wait Seconds", RecompileWait);
    NudgeSeconds = C(S, "Release Nudge Seconds", NudgeSeconds);
    NudgePower = C(S, "Release Nudge Power (0-1)", NudgePower);
    GapSeconds = C(S, "Release Gap Seconds", GapSeconds);
    MatchRadius = C(S, "Drone Match Radius m", MatchRadius);
    FinishTimeout = C(S, "Finish Timeout Seconds", FinishTimeout);
    FinishFallback = C(S, "Finish Fallback Seconds (used only without Drone Box keys)", FinishFallback);
    ArmSeconds = C(S, "Arm Max Seconds", ArmSeconds);
    JoinSeconds = Math.Max(30, C(S, "Join Seconds (join is re-sent every 2 s, minimum 30)", JoinSeconds));
    FusedGrace = C(S, "Fused Grace Seconds", FusedGrace);
    LaunchBurst = C(S, "Launch Burst Seconds", LaunchBurst);
    string fch = C(S, "Fleet Channel (join command for freshly released drones, blank = off)", FleetCh).Trim();
    string fsec = C(S, "Fleet Secret", "").Trim();
    FleetCh = fch == "" ? "" : (fsec == "" ? fch.ToUpperInvariant() : fch.ToUpperInvariant() + "_" + fsec.ToUpperInvariant());
    Relay = C(S, "Relay Turret Targets To All Tags", Relay);
    Printing = C(S, "Auto Start", Printing);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}
string C(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double C(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool C(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

void GetBlocks()
{
    var old = _bays; _bays = new List<Bay>();
    var projs = new List<IMyProjector>(); var welders = new List<IMyShipWelder>(); var merges = new List<IMyShipMergeBlock>();
    GridTerminalSystem.GetBlocksOfType(projs, b => b.IsSameConstructAs(Me) && b.CustomName.Contains("[Bay"));
    GridTerminalSystem.GetBlocksOfType(welders, b => b.IsSameConstructAs(Me) && b.CustomName.Contains("[Bay"));
    GridTerminalSystem.GetBlocksOfType(merges, b => b.IsSameConstructAs(Me) && b.CustomName.Contains("[Bay"));
    _cap.Clear();
    MyIniParseResult r; bool iniOk = _ini.TryParse(Me.CustomData, out r);
    for (int n = 1; n <= MaxBays; n++)
    {
        string tag = "[Bay " + n + "]"; string sec = "Bay " + n;
        Bay bay = null; foreach (var o in old) if (o.N == n) bay = o;
        if (bay == null) bay = new Bay { N = n };
        bay.Proj = null; bay.Merge = null; bay.Welders.Clear();
        foreach (var p in projs) if (p.CustomName.Contains(tag)) bay.Proj = p;
        foreach (var m in merges) if (m.CustomName.Contains(tag)) bay.Merge = m;
        foreach (var w in welders) if (w.CustomName.Contains(tag)) bay.Welders.Add(w);
        if (bay.Proj == null || bay.Merge == null) continue;
        if (iniOk)
        {
            bay.Tag = C(sec, "Tag", bay.Tag); bay.Prefix = C(sec, "Prefix", bay.Prefix); bay.Cap = (int)C(sec, "Max Alive", bay.Cap); bay.MaxPrints = (int)C(sec, "Max Prints", bay.MaxPrints);
            bool held = C(sec, "Hold", bay.Held); bay.Held = held;
            bay.Exit = C(sec, "Exit Direction (printer axis the drone leaves along, blank means straight away from the merge block)", bay.Exit);
            bay.SkipFinish = C(sec, "Skip Finish Check", false);
            bay.Burst = C(sec, "Launch Burst Seconds", LaunchBurst);
            bay.Cells = (int)C(sec, "Drone Cell Count", 0.0);
            bay.Tol = (int)C(sec, "Drone Cell Tolerance", 2.0);
            string bmin = C(sec, "Drone Box Min", "").Trim(), bmax = C(sec, "Drone Box Max", "").Trim(), ign = C(sec, "Ignore Cells", "").Trim();
            ParseBox(bay, bmin, bmax, ign);
        }
        _bays.Add(bay);
        int c; if (!_cap.TryGetValue(bay.Tag, out c) || bay.Cap > c) _cap[bay.Tag] = bay.Cap;
        if (!_lSwarm.ContainsKey(bay.Tag))
        {
            _lSwarm[bay.Tag] = IGC.RegisterBroadcastListener(bay.Tag + "_SWARM");
            _alive[bay.Tag] = new Dictionary<long, double>();
            _joinUntil[bay.Tag] = 0; _joinNext[bay.Tag] = 0;
        }
    }
    if (iniOk) { string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o; }
    foreach (var b2 in _bays) KeepProjection(b2);
    GridTerminalSystem.GetBlocksOfType(_stock, b => b.CustomName.Contains("[Stock]") && b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.IsSameConstructAs(Me) && b.CubeGrid == Me.CubeGrid);
    if (Relay)
    {
        GridTerminalSystem.GetBlocksOfType(_turrets, b => b.IsSameConstructAs(Me));
        GridTerminalSystem.GetBlocksOfType(_ctcs, b => b.IsSameConstructAs(Me));
    }
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.7f;
}

// Keep Projection must stay on, or the projector clears the blueprint as soon as the last block is placed
void KeepProjection(Bay b)
{
    try { if (!b.Proj.GetValue<bool>("KeepProjection")) b.Proj.SetValue<bool>("KeepProjection", true); } catch (Exception) { }
}

static long Key(int x, int y, int z) { return ((long)(x + 1048576) << 42) | ((long)(y + 1048576) << 21) | (long)(z + 1048576); }

static bool ParseV3(string s, out int x, out int y, out int z)
{
    x = y = z = 0; var p = s.Split(',');
    return p.Length == 3 && int.TryParse(p[0].Trim(), out x) && int.TryParse(p[1].Trim(), out y) && int.TryParse(p[2].Trim(), out z);
}

void ParseBox(Bay b, string bmin, string bmax, string ign)
{
    string raw = bmin + "|" + bmax + "|" + ign;
    if (raw == b.BoxRaw) return;
    b.BoxRaw = raw; b.HasBox = false; b.CfgWarn = ""; b.Ignore.Clear(); b.ScanIdx = 0; b.Seen.Clear();
    int x0, y0, z0, x1, y1, z1;
    if (bmin == "" && bmax == "") return;
    if (!ParseV3(bmin, out x0, out y0, out z0) || !ParseV3(bmax, out x1, out y1, out z1)) { b.CfgWarn = "Drone Box keys unreadable (use x,y,z)"; return; }
    int ax = Math.Min(x0, x1), ay = Math.Min(y0, y1), az = Math.Min(z0, z1), bx = Math.Max(x0, x1), by = Math.Max(y0, y1), bz = Math.Max(z0, z1);
    long vol = (long)(bx - ax + 1) * (by - ay + 1) * (bz - az + 1);
    if (vol > 200000) { b.CfgWarn = "Drone Box too large (" + vol + " cells)"; return; }
    b.BMin = new Vector3I(ax, ay, az); b.BMax = new Vector3I(bx, by, bz);
    foreach (var c in ign.Split(';')) { int ix, iy, iz; if (c.Trim() != "" && ParseV3(c, out ix, out iy, out iz)) b.Ignore.Add(Key(ix, iy, iz)); }
    b.HasBox = true;
}

public void Main(string arg, UpdateType src)
{
    try { Tick(arg, src); }
    catch (Exception ex) { Echo("ERROR " + ex.Message); if (_lcd != null) _lcd.WriteText("HIVE MOTHER ERROR\n" + ex.Message); }
}

void Tick(string arg, UpdateType src)
{
    if (!string.IsNullOrWhiteSpace(arg)) Command(arg.Trim().ToLowerInvariant());
    if ((src & UpdateType.Update10) == 0) return;
    double dt = Runtime.TimeSinceLastRun.TotalSeconds; if (dt <= 0 || dt > 2) dt = 1 / 6.0;
    _now += dt; _dt = dt; _tick++;
    if (_tick % 60 == 0) GetBlocks();
    foreach (var kv in _lSwarm)
    {
        var dict = _alive[kv.Key];
        while (kv.Value.HasPendingMessage)
        {
            var m = kv.Value.AcceptMessage();
            if (m.Data is MyTuple<long, Vector3D, Vector3D, long, int>)
            {
                long id = ((MyTuple<long, Vector3D, Vector3D, long, int>)m.Data).Item1;
                if (!dict.ContainsKey(id)) { _joinNext[kv.Key] = 0; _joinUntil[kv.Key] = Math.Max(_joinUntil[kv.Key], _now + 6); }   // a new drone appeared: say join again
                dict[id] = _now;
            }
        }
    }
    foreach (var l in _lTgt)
        while (l.HasPendingMessage)
        {
            var m = l.AcceptMessage();
            if (!(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
            if (d.Item4 < 0) _targets.Remove(d.Item1); else _targets[d.Item1] = _now;
        }
    var drop = new List<long>();
    foreach (var dict in _alive.Values)
    {
        drop.Clear(); foreach (var kv in dict) if (_now - kv.Value > 6) drop.Add(kv.Key);
        foreach (var id in drop) dict.Remove(id);
    }
    drop.Clear(); foreach (var kv in _targets) if (_now - kv.Value > 6) drop.Add(kv.Key);
    foreach (var id in drop) _targets.Remove(id);
    if (_tick % 6 == 0) { foreach (var tag in _lSwarm.Keys) IGC.SendBroadcastMessage(tag + "_HOME", Me.CubeGrid.WorldVolume.Center); }
    if (Relay && _tick % 2 == 0) RelayTargets();

    SendJoins();
    int building = 0, docked = 0;
    foreach (var b in _bays) { if (b.State == "BUILD" || b.State == "FINISH" || b.State == "CHARGE") building++; if (b.State == "DOCKED") docked++; }
    foreach (var b in _bays) StepBay(b, ref building, ref docked);
    _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount);
    if (_tick % 3 == 0) Show(building, docked);
}

int Alive(string tag) { Dictionary<long, double> d; return _alive.TryGetValue(tag, out d) ? d.Count : 0; }
int AliveAll() { int n = 0; foreach (var d in _alive.Values) n += d.Count; return n; }
bool InCycle(Bay b) { return b.State == "BUILD" || b.State == "FINISH" || b.State == "CHARGE" || b.State == "ARM" || b.State == "RELEASE"; }
int Pending(string tag) { int n = 0; foreach (var b in _bays) if (b.Tag == tag && InCycle(b)) n++; return n; }
int PendingAll() { int n = 0; foreach (var b in _bays) if (InCycle(b)) n++; return n; }

// "join" is repeated, never a single shot: the drone may still be compiling, charging or waiting for a mode
void SendJoins()
{
    foreach (var tag in _lSwarm.Keys)
    {
        if (_now >= _joinUntil[tag] || _now < _joinNext[tag]) continue;
        _joinNext[tag] = _now + 2;
        IGC.SendBroadcastMessage(tag + "_JOIN", "join");
        if (FleetCh != "") IGC.SendBroadcastMessage(FleetCh + "_CMD", "join");
        if (_rackLaunchLeft > 0) { _rackLaunchLeft--; IGC.SendBroadcastMessage(LaunchTag + "_CMD", "launch"); }
    }
}

bool Wants(Bay b, int docked)
{
    if (!Printing || b.Held) return false;
    if (b.MaxPrints > 0 && b.Printed >= b.MaxPrints) return false;       // scriptless drones never report in: cap the prints instead
    if (RackMode) return docked < _bays.Count;
    int cap; if (!_cap.TryGetValue(b.Tag, out cap)) cap = b.Cap;
    return Alive(b.Tag) + Pending(b.Tag) < cap && AliveAll() + PendingAll() < MaxDrones;
}

void StepBay(Bay b, ref int building, ref int docked)
{
    double t = _now - b.T;
    switch (b.State)
    {
        case "IDLE":
            Power(b, false);
            if (building < ParallelBuilds && Wants(b, docked)) { StartBuild(b); building++; }
            break;
        case "BUILD":
            Power(b, true);
            int rem = b.Proj.IsProjecting ? b.Proj.RemainingBlocks : -1;
            if (rem != b.Rem) { b.Rem = rem; b.LastProgress = _now; }
            if (rem > 0) b.SawWork = true;
            // rem == 0 for a while without ever seeing work: a half-built drone from a faulted cycle is still in the bay
            if ((b.SawWork || t > 15) && b.Proj.IsProjecting && rem == 0) { EnterFinish(b); break; }
            b.Msg = Diagnose(b);
            Stall(b);
            if (b.Starved) b.T += _dt;
            else if (t > BuildTimeout) Fault(b, "build timeout: " + (rem >= 0 ? rem + " of " + b.Proj.TotalBlocks + " blocks not placed" : "no projection") + (b.Msg != "" ? " (" + b.Msg + ")" : ""));
            break;
        case "FINISH":
            Power(b, true);        // welders stay on until the drone is complete
            StepFinish(b, t);
            break;
        case "CHARGE":
            Power(b, false);
            FindDrone(b);
            if (DoRecompile && !b.Recompiled && b.DPb != null) { b.DPb.Enabled = true; b.DPb.ApplyAction("Recompile"); b.Recompiled = true; b.T = _now; t = 0; }
            if (b.DMerge == null && t > 8) { Fault(b, "no drone merge block found (prefix " + b.Prefix + ")"); break; }
            bool ready = t > (b.Recompiled ? RecompileWait : 0) && DroneCharged(b);
            if (ready || t > ChargeSeconds + RecompileWait)
            {
                if (RackMode) { b.State = "DOCKED"; docked++; }
                else Release(b);
            }
            break;
        case "DOCKED":   // rack: wait for a target
            if (_targets.Count > _launched && _targets.Count > 0) { _launched++; Release(b); }
            break;
        case "ARM":      // the drone is still on this grid: its PB is visible, so give it its take-off orders now
            Power(b, false);
            if (!b.Armed) b.Armed = Arm(b);
            if ((b.Armed && t >= 0.5) || t >= ArmSeconds) Separate(b);
            break;
        case "RELEASE":
            Nudge(b, t);
            if (t >= GapSeconds + NudgeSeconds)
            {
                if (StillAttached(b))
                {   // grid split can lag a few ticks behind the merge block switching off: re-assert, wait, only then fault
                    if (b.FusedT <= 0) b.FusedT = _now;
                    b.Merge.Enabled = false; if (b.DMerge != null) b.DMerge.Enabled = false;
                    b.Msg = "separating... drone still on the printer grid";
                    if (_now - b.FusedT > FusedGrace) { Fault(b, "FUSED: drone is still on the printer grid " + FusedGrace + " s after merge off - grind the drone's merge stalk by hand; see docs"); b.Held = true; }
                    break;
                }
                b.FusedT = 0; b.Msg = "";
                b.State = "COOLDOWN"; b.T = _now;
                _joinUntil[b.Tag] = Math.Max(_joinUntil[b.Tag], _now + JoinSeconds); _joinNext[b.Tag] = 0;
                if (b.RackLaunch) _rackLaunchLeft = 3;
            }
            break;
        case "COOLDOWN":
            bool cleared = b.Proj.IsProjecting && b.Proj.RemainingBlocks > 0;
            if (t > CooldownSeconds && (cleared || t > CooldownSeconds * 4)) { b.Merge.Enabled = true; b.State = "IDLE"; b.T = _now; b.DPb = null; b.DMerge = null; if (_launched > 0 && _targets.Count == 0) _launched = 0; }
            break;
        case "FAULT":
            Power(b, false);
            if (!b.Held && t > FaultSeconds) { b.State = "IDLE"; b.T = _now; }
            break;
    }
}

void StartBuild(Bay b)
{
    b.State = "BUILD"; b.T = _now; b.Rem = -1; b.LastProgress = _now; b.SawWork = false; b.Merge.Enabled = true; b.Msg = "";
    b.Nudged = false; b.DPb = null; b.DMerge = null; b.DBats.Clear(); b.KickW = null; b.Starved = false; b.Armed = false; b.LaunchWritten = false; b.Recompiled = false;
    KeepProjection(b);
}

void Fault(Bay b, string why)
{
    b.State = "FAULT"; b.T = _now; b.Msg = why; b.Faults++;
    Power(b, false);     // the only place (besides IDLE/CHARGE/...) welders are switched off after a build
}

void EnterFinish(Bay b)
{
    b.State = "FINISH"; b.T = _now; b.FinishT = _now; b.LastProgress = _now; b.Msg = ""; b.Recompiled = false; b.Starved = false;
    ResetScan(b); b.HavePass = false; b.LBad = 0; b.LBlocks = 0; b.LCells = 0; b.LInteg = -1; b.LBadText = "";
}

void ResetScan(Bay b)
{
    b.ScanIdx = 0; b.Seen.Clear(); b.PBlocks = b.PCells = b.PBad = 0; b.PInteg = 0; b.PBadL.Clear(); b.PassOk = false;
}

// FINISH: the projector only counts PLACED blocks. A placed block is a skeleton until the welders have put every component in.
void StepFinish(Bay b, double t)
{
    int rem = b.Proj.IsProjecting ? b.Proj.RemainingBlocks : -1;
    bool done = false; string note = "";
    if (b.SkipFinish) { done = true; note = "finish check skipped (Skip Finish Check)"; }
    else
    {
        if (b.HasBox) done = ScanBox(b, rem) && rem <= 0;
        else { done = FallbackCheck(b, t) && rem <= 0; note = "no Drone Box keys: block-name check + " + (int)FinishFallback + " s timer, armour not verified" + (b.CfgWarn != "" ? " (" + b.CfgWarn + ")" : ""); }
        if (rem > 0) note = rem + " blocks not placed yet. " + note;
    }
    if (done) { b.State = "CHARGE"; b.T = _now; b.Recompiled = false; b.Msg = ""; return; }
    string d = Diagnose(b);
    b.Msg = (d != "" ? d + "\n   " : "") + (b.LBadText != "" ? b.LBadText + "\n   " : "") + note;
    Stall(b);
    if (b.Starved) b.T += _dt;
    else if (t > FinishTimeout)
        Fault(b, "finish timeout: " + (b.HavePass ? b.LBad + " unfinished, " + b.LCells + "/" + b.Cells + " cells. " + b.LBadText : "scan never completed") + (rem > 0 ? " " + rem + " blocks never placed (unsupported?)" : ""));
}

// incremental walk over the drone's cell box; returns true when a complete pass found everything welded
bool ScanBox(Bay b, int rem)
{
    var g = b.Merge.CubeGrid;
    int sx = b.BMax.X - b.BMin.X + 1, sy = b.BMax.Y - b.BMin.Y + 1, sz = b.BMax.Z - b.BMin.Z + 1;
    long total = (long)sx * sy * sz;
    if (b.ScanIdx == 0) { b.Seen.Clear(); b.PassOk = rem <= 0; }       // a pass that started while blocks were still unplaced proves nothing
    for (int n = 0; n < ScanBudget && b.ScanIdx < total; n++)
    {
        long i = b.ScanIdx++;
        int x = b.BMin.X + (int)(i % sx), y = b.BMin.Y + (int)((i / sx) % sy), z = b.BMin.Z + (int)(i / ((long)sx * sy));
        if (b.Ignore.Contains(Key(x, y, z))) continue;
        var s = g.GetCubeBlock(new Vector3I(x, y, z));
        if (s == null) continue;
        var fb = s.FatBlock;
        if (fb != null && fb == b.Merge) continue;
        Vector3I sp = s.Position;
        if (b.Ignore.Contains(Key(sp.X, sp.Y, sp.Z))) continue;
        b.PCells++;
        if (!b.Seen.Add(Key(sp.X, sp.Y, sp.Z))) continue;               // multi-cell block: count it once
        b.PBlocks++; b.PInteg += s.BuildIntegrity;
        bool bad = !s.IsFullIntegrity || (fb != null && !fb.IsFunctional);
        if (bad)
        {
            b.PBad++;
            if (b.PBadL.Count < 4)
            {
                string nm = fb != null ? fb.DefinitionDisplayNameText : "block";
                int pct = s.MaxIntegrity > 0 ? (int)(100 * s.BuildIntegrity / s.MaxIntegrity) : 0;
                string e = nm + " @" + sp.X + "," + sp.Y + "," + sp.Z + " " + pct + "%";
                if (b.PBadL.Count < 3 && b.Welders.Count > 0)
                {
                    Vector3D wp = g.GridIntegerToWorld(sp); double bd = 1e9;
                    foreach (var w in b.Welders) bd = Math.Min(bd, Vector3D.Distance(wp, w.GetPosition()));
                    e += " (" + Math.Round(bd, 1) + " m from nearest welder)";
                }
                b.PBadL.Add(e);
            }
        }
    }
    if (b.ScanIdx < total) return false;
    // pass finished
    bool prog = !b.HavePass || b.PBad < b.LBad || b.PInteg > b.LInteg + 0.5f || b.PBlocks > b.LBlocks;
    if (prog) b.LastProgress = _now;
    b.LBad = b.PBad; b.LBlocks = b.PBlocks; b.LCells = b.PCells; b.LInteg = b.PInteg; b.HavePass = true;
    bool ok = b.PassOk && b.PBad == 0 && b.PCells >= b.Cells - b.Tol;
    if (b.PBad > 0) b.LBadText = b.PBad + " unfinished: " + string.Join("; ", b.PBadL.ToArray());
    else if (b.PCells < b.Cells - b.Tol) b.LBadText = "only " + b.PCells + " of " + b.Cells + " drone cells present";
    else b.LBadText = "";
    ResetScan(b);
    return ok;
}

// no cell box configured: terminal blocks named like the drone, plus a conservative minimum time
bool FallbackCheck(Bay b, double t)
{
    if (_tick % 6 != 0 && b.HavePass) return b.HavePass && b.LBad == 0 && b.LBlocks > 0 && t >= FinishFallback;
    GridTerminalSystem.GetBlocksOfType(_tmp, x => x.CustomName.StartsWith(b.Prefix) && !x.CustomName.Contains("[Bay") && x.IsSameConstructAs(Me));
    int bad = 0, n = 0; float integ = 0; var bl = new List<string>();
    foreach (var x in _tmp)
    {
        n++; var s = x.CubeGrid.GetCubeBlock(x.Position); bool u = !x.IsFunctional || (s != null && !s.IsFullIntegrity);
        if (s != null) integ += s.BuildIntegrity;
        if (u) { bad++; if (bl.Count < 4) bl.Add(x.CustomName); }
    }
    if (!b.HavePass || bad < b.LBad || integ > b.LInteg + 0.5f || n > b.LBlocks) b.LastProgress = _now;
    b.LBad = bad; b.LBlocks = n; b.LInteg = integ; b.HavePass = true;
    b.LBadText = bad > 0 ? bad + " unfinished: " + string.Join("; ", bl.ToArray()) : "";
    return bad == 0 && n > 0 && t >= FinishFallback;
}

// per-welder report: why would a welder not be welding?
string WelderReport(Bay b)
{
    int off = 0, broken = 0, nopower = 0; string first = "";
    foreach (var w in b.Welders)
    {
        string why = null;
        if (!w.IsFunctional) { broken++; why = "damaged"; }
        else if (!w.Enabled) { off++; why = "off"; }
        else if (!w.IsWorking) { nopower++; why = "no power"; }
        if (why != null && first.Length < 60) first += (first == "" ? "" : ", ") + ShortName(w) + " " + why;
    }
    if (off + broken + nopower == 0) return "";
    return "welders: " + off + " off, " + broken + " damaged, " + nopower + " unpowered (" + first + ")";
}
string ShortName(IMyTerminalBlock x) { string n = x.CustomName; int i = n.IndexOf(']'); return (i >= 0 ? n.Substring(i + 1) : n).Trim(); }

string Diagnose(Bay b)
{
    b.Starved = false;
    if (!b.Proj.IsProjecting) return "no projection (projector off or blueprint missing)";
    if (_now - b.LastProgress <= StallSeconds) return "";
    if (StockEmpty()) { b.Starved = true; return "STALLED: component stock empty - refill the [Stock] cargo (timeouts paused)"; }
    string wr = WelderReport(b);
    if (wr != "") return "STALLED: " + wr;
    if (b.State == "FINISH") return "STALLED: projection done but blocks are not getting finished: out of weld reach or missing components";
    if (b.Rem > 0 && b.Rem == b.Proj.TotalBlocks) return "STALLED at 0 built: first blocks not supported / merge block not aligned?";
    if (b.Rem > 0 && b.Proj.BuildableBlocksCount == 0) return "STALLED: " + b.Rem + " blocks left, none buildable - unsupported (nothing to attach to) or out of weld reach";
    return "STALLED: " + b.Rem + " blocks left (" + b.Proj.BuildableBlocksCount + " buildable) - out of weld reach or missing components";
}

// stalled: nudge ONE welder at a time (off for 2 s, then on again), at most once per StallSeconds. Never all of them.
void Stall(Bay b)
{
    if (b.Starved || _now - b.LastProgress <= StallSeconds) return;
    int n = b.Welders.Count;
    if (b.KickW != null || n == 0 || _now - b.KickT < StallSeconds) return;
    for (int i = 0; i < n; i++)
    {
        var w = b.Welders[(b.KickIdx + i) % n];
        if (!w.IsFunctional) continue;
        b.KickIdx = (b.KickIdx + i + 1) % n; b.KickW = w; b.KickT = _now; w.Enabled = false; return;
    }
}

bool StockEmpty()
{
    if (_stock.Count == 0) return false;      // creative world / no stock tracking
    foreach (var s in _stock)
    {
        var inv = s.GetInventory(0);
        if (inv != null && inv.CurrentVolume.RawValue > 2000) return false;     // more than 0.002 m3 of items
    }
    return true;
}

// Welders are only switched OFF outside BUILD/FINISH. In those two states "off" requests are ignored,
// and every welder (except the one being kicked for 2 s) is forced back on each tick.
void Power(Bay b, bool on)
{
    if (b.Proj.Enabled != true) b.Proj.Enabled = true;      // projection stays up between drones (Keep Projection)
    if (!on && (b.State == "BUILD" || b.State == "FINISH")) return;
    if (!on) b.KickW = null;
    else if (b.KickW != null && _now - b.KickT >= 2) { b.KickW.Enabled = true; b.KickW = null; }
    foreach (var w in b.Welders) { if (on && w == b.KickW) continue; if (w.Enabled != on) w.Enabled = on; }
}

// drone parts belong to the bay whose merge block is nearest
Bay Nearest(Vector3D p)
{
    Bay best = null; double bd = MatchRadius;
    foreach (var b in _bays) { double d = Vector3D.Distance(p, b.Merge.GetPosition()); if (d < bd) { bd = d; best = b; } }
    return best;
}

void FindDrone(Bay b)
{
    if (b.DPb != null && b.DMerge != null) return;
    GridTerminalSystem.GetBlocksOfType(_mergesAll, x => x.CustomName.StartsWith(b.Prefix) && !x.CustomName.Contains("[Bay"));
    b.DMerge = null; double bd = 1e9;
    foreach (var m in _mergesAll) { double d = Vector3D.Distance(m.GetPosition(), b.Merge.GetPosition()); if (d < bd && d < MatchRadius && Nearest(m.GetPosition()) == b) { bd = d; b.DMerge = m; } }
    GridTerminalSystem.GetBlocksOfType(_pbs, x => x != Me && x.CustomName.StartsWith(b.Prefix) && !x.CustomName.Contains("[Bay"));
    b.DPb = null; bd = 1e9;
    foreach (var p in _pbs) { double d = Vector3D.Distance(p.GetPosition(), b.Merge.GetPosition()); if (d < bd && Nearest(p.GetPosition()) == b) { bd = d; b.DPb = p; } }
    GridTerminalSystem.GetBlocksOfType(_bats, x => x.CustomName.StartsWith(b.Prefix) && !x.CustomName.Contains("[Bay"));
    b.DBats.Clear();
    foreach (var x in _bats) if (Nearest(x.GetPosition()) == b) b.DBats.Add(x);
}

bool DroneCharged(Bay b)
{
    if (b.DBats.Count == 0) return true;
    foreach (var x in b.DBats) if (x.CurrentStoredPower < x.MaxStoredPower * 0.95f) return false;
    return true;
}

void Release(Bay b)
{
    Power(b, false);
    FindDrone(b);
    b.Away = Vector3D.Zero;
    if (b.DMerge != null) { Vector3D d = b.DMerge.GetPosition() - b.Merge.GetPosition(); if (d.LengthSquared() > 1e-6) b.Away = -Vector3D.Normalize(d); }
    if (!string.IsNullOrEmpty(b.Exit))      // sideways exit: the printer moves the opposite way (printer grid axes, see the bay's Exit Direction)
    {
        MatrixD g = Me.CubeGrid.WorldMatrix; Vector3D ex = Vector3D.Zero;
        switch (b.Exit.Trim())
        {
            case "Forward": ex = g.Forward; break; case "Backward": ex = g.Backward; break;
            case "Up": ex = g.Up; break; case "Down": ex = g.Down; break;
            case "Left": ex = g.Left; break; case "Right": ex = g.Right; break;
        }
        if (ex.LengthSquared() > 0.5) b.Away = -ex;
    }
    b.RackLaunch = RackMode; b.Armed = false; b.LaunchWritten = false; b.FusedT = 0; b.Msg = "";
    b.State = "ARM"; b.T = _now; b.Nudged = false;
}

// still on the printer grid? Needs BOTH located drone blocks (when two were found) on the merge block's grid.
bool StillAttached(Bay b)
{
    var g = b.Merge.CubeGrid;
    if (b.DPb == null && b.DMerge == null) return false;
    return (b.DPb == null || b.DPb.CubeGrid == g) && (b.DMerge == null || b.DMerge.CubeGrid == g);
}

// take-off orders, written while the drone PB can still be seen through GridTerminalSystem
bool Arm(Bay b)
{
    FindDrone(b);
    if (b.DPb == null) { b.Msg = "ARM: drone PB not visible - relying on the JOIN broadcast"; return false; }
    if (!b.LaunchWritten)
    {
        b.LaunchWritten = true;
        var ini = new MyIni(); MyIniParseResult r;
        if (ini.TryParse(b.DPb.CustomData, out r))
        {
            ini.Set("Launch", "Auto", !RackMode);                 // rack drones wait for the launch command instead
            ini.Set("Launch", "Burst Seconds", b.Burst >= 0 ? b.Burst : LaunchBurst);
            ini.Set("Launch", "Away X", b.Away.X); ini.Set("Launch", "Away Y", b.Away.Y); ini.Set("Launch", "Away Z", b.Away.Z);
            ini.Set("Launch", "Exit Direction", b.Exit == null ? "" : b.Exit.Trim());
            ini.Set("Launch", "Armed", true);
            b.DPb.CustomData = ini.ToString();
        }
        else b.Msg = "ARM: drone Custom Data unreadable - relying on the JOIN broadcast";
    }
    b.DPb.Enabled = true;
    bool ok = b.DPb.TryRun("armed");
    if (!ok) b.Msg = "ARM: drone PB did not run (not compiled?) - relying on the JOIN broadcast";
    else b.Msg = "";
    return ok;
}

// merge blocks off: the drone leaves
void Separate(Bay b)
{
    foreach (var x in b.DBats) if (x.ChargeMode != ChargeMode.Auto) x.ChargeMode = ChargeMode.Auto;   // a drone left on Recharge never uses its batteries
    b.Merge.Enabled = false;
    if (b.DMerge != null) b.DMerge.Enabled = false;
    b.State = "RELEASE"; b.T = _now; b.Nudged = false; b.Printed++;
}

// the thrust gap: the printer pushes itself away from the drone for a moment
void Nudge(Bay b, double t)
{
    if (b.Away.LengthSquared() < 0.5) return;
    bool on = t < NudgeSeconds;
    if (!on && !b.Nudged) return;
    foreach (var th in _thr)
    {
        if (th.CustomName.StartsWith(b.Prefix)) continue;                   // never the drone's own thrusters
        if (Vector3D.Dot(th.WorldMatrix.Backward, b.Away) > 0.85) th.ThrustOverridePercentage = on ? (float)NudgePower : 0f;
    }
    b.Nudged = on;
}

void RelayTargets()
{
    foreach (var t in _turrets) if (t.IsFunctional && t.HasTarget) SendTarget(t.GetTargetedEntity());
    foreach (var c in _ctcs) if (c.IsFunctional && c.HasTarget) SendTarget(c.GetTargetedEntity());
}
void SendTarget(MyDetectedEntityInfo e)
{
    if (e.IsEmpty() || e.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) return;
    var msg = new MyTuple<long, Vector3D, Vector3D, double, long>(e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() * 0.5, Me.EntityId);
    foreach (var tag in _lSwarm.Keys) IGC.SendBroadcastMessage(tag + "_TGT", msg);
}

void Command(string a)
{
    var w = a.Split(' ');
    int n;
    switch (w[0])
    {
        case "start": Printing = true; break;
        case "stop": Printing = false; foreach (var b in _bays) Power(b, false); break;
        case "max": if (w.Length > 1 && int.TryParse(w[1], out n)) MaxDrones = n; break;
        case "release":
            foreach (var b in _bays)
                if ((b.State == "DOCKED" || b.State == "CHARGE") && (w.Length < 2 || (int.TryParse(w[1], out n) && n == b.N))) Release(b);
            break;
        case "hold": if (w.Length > 1 && int.TryParse(w[1], out n)) foreach (var b in _bays) if (b.N == n) b.Held = true; break;
        case "unhold": if (w.Length > 1 && int.TryParse(w[1], out n)) foreach (var b in _bays) if (b.N == n) { b.Held = false; if (b.State == "FAULT") b.State = "IDLE"; } break;
        case "relay": Relay = w.Length < 2 || w[1] != "off"; break;
        case "reload": LoadConfig(); GetBlocks(); break;
    }
}

void Show(int building, int docked)
{
    _sb.Clear();
    _sb.Append("HIVE MOTHER v").Append(VER).Append(Printing ? "  PRINTING" : "  STOPPED").Append('\n');
    if (RackMode) _sb.Append("Rack: ").Append(docked).Append(" docked, targets ").Append(_targets.Count).Append('\n');
    else _sb.Append("Drones alive: ").Append(AliveAll()).Append(" / ").Append(MaxDrones).Append("   building ").Append(PendingAll()).Append('\n');
    foreach (var b in _bays)
    {
        _sb.Append("Bay ").Append(b.N).Append(' ').Append(b.Tag).Append(": ").Append(b.State);
        if (b.Printed > 0) _sb.Append(" #").Append(b.Printed);
        if (b.Held) _sb.Append(" (held)");
        if (b.State == "BUILD") _sb.Append("  ").Append(b.Rem >= 0 ? b.Rem + " left" : "no projection");
        if (b.State == "FINISH")
        {
            long tot = b.HasBox ? (long)(b.BMax.X - b.BMin.X + 1) * (b.BMax.Y - b.BMin.Y + 1) * (b.BMax.Z - b.BMin.Z + 1) : 0;
            _sb.Append("  ").Append(b.HasBox ? "scan " + (tot > 0 ? 100 * b.ScanIdx / tot : 0) + "%" : "name check").Append(b.HavePass ? ", " + b.LBad + " unfinished" : "").Append(", ").Append((int)(_now - b.FinishT)).Append(" s");
        }
        if (b.Msg != "") _sb.Append("\n   ").Append(b.Msg);
        _sb.Append('\n');
    }
    if (_bays.Count == 0) _sb.Append("No bays: name a projector, welders and\na merge block with [Bay 1]\n");
    _sb.Append("Ins: ").Append(Runtime.CurrentInstructionCount).Append(" / ").Append(Runtime.MaxInstructionCount).Append("  peak ").Append(_peakIns).Append('\n');
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
