// =====================================================================
//  HIVE MOTHER  v2.0  - drone printer controller (projector + welders + merge block bays)
// ---------------------------------------------------------------------
//  A bay is the blocks whose names contain "[Bay 1]", "[Bay 2]", ...:
//  ONE projector (holding the drone blueprint, Keep Projection ON), its
//  welders, and the merge block the drone latches onto. Drone blocks are
//  recognised by their name prefix ("Gnat", "Hornet", ...) per bay.
//
//  CYCLE PER BAY
//   BUILD     projector + welders on until the projector reports 0 blocks left
//             (after it has reported blocks to build at least once). Stalls are
//             diagnosed: component stock empty ([Stock] cargo), nothing projected,
//             or blocks that no welder can reach. A stalled bay kicks its welders
//             off/on, and after "Build Timeout Seconds" it faults and retries.
//   CHARGE    welders off, drone stays merged and shares this ship's power; its
//             Programmable Block is recompiled (a welded PB is not auto-compiled)
//             and the drone batteries charge to 95 % (or Max Charge Seconds).
//   RELEASE   both merge blocks off, the printer nudges away from the drone with
//             the thrusters that push the right way (the thrust gap), then it checks
//             that the drone really is a separate grid and only then broadcasts
//             "<Tag>_JOIN". If the drone is STILL on the printer's grid the bay
//             FAULTs ("FUSED") instead of looping.
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
// =====================================================================
const string VER = "2.0";
const int MaxBays = 12;

int MaxDrones = 8;
bool RackMode = false;
string LaunchTag = "STINGER";
double ChargeSeconds = 25, CooldownSeconds = 6, StallSeconds = 45, BuildTimeout = 600, FaultSeconds = 60;
double NudgeSeconds = 1.5, NudgePower = 0.25, GapSeconds = 1.0, RecompileWait = 3, MatchRadius = 12;
int ParallelBuilds = 2;
bool Printing = true, DoRecompile = true, Relay = false;
int _peakIns = 0;

MyIni _ini = new MyIni();
double _now; long _tick;

class Bay
{
    public int N; public string Tag = "GNAT", Prefix = "Gnat"; public bool Held; public int Cap = 8, MaxPrints = 0, Printed = 0;
    public IMyProjector Proj; public List<IMyShipWelder> Welders = new List<IMyShipWelder>(); public IMyShipMergeBlock Merge;
    public IMyShipMergeBlock DMerge; public IMyProgrammableBlock DPb; public List<IMyBatteryBlock> DBats = new List<IMyBatteryBlock>();
    public string State = "IDLE"; public double T; public int Rem = -1; public double LastProgress; public bool SawWork, Recompiled, Joined, Nudged; public int Joins;
    public string Msg = ""; public double Kick; public int Faults; public Vector3D Away; public string Exit = "";
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
        }
        _bays.Add(bay);
        int c; if (!_cap.TryGetValue(bay.Tag, out c) || bay.Cap > c) _cap[bay.Tag] = bay.Cap;
        if (!_lSwarm.ContainsKey(bay.Tag))
        {
            _lSwarm[bay.Tag] = IGC.RegisterBroadcastListener(bay.Tag + "_SWARM");
            _alive[bay.Tag] = new Dictionary<long, double>();
        }
    }
    if (iniOk) { string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o; }
    GridTerminalSystem.GetBlocksOfType(_stock, b => b.CustomName.Contains("[Stock]") && b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.IsSameConstructAs(Me) && b.CubeGrid == Me.CubeGrid);
    if (Relay)
    {
        GridTerminalSystem.GetBlocksOfType(_turrets, b => b.IsSameConstructAs(Me));
        GridTerminalSystem.GetBlocksOfType(_ctcs, b => b.IsSameConstructAs(Me));
    }
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.7f;
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
    _now += dt; _tick++;
    if (_tick % 60 == 0) GetBlocks();
    foreach (var kv in _lSwarm)
    {
        var dict = _alive[kv.Key];
        while (kv.Value.HasPendingMessage)
        {
            var m = kv.Value.AcceptMessage();
            if (m.Data is MyTuple<long, Vector3D, Vector3D, long, int>) dict[((MyTuple<long, Vector3D, Vector3D, long, int>)m.Data).Item1] = _now;
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

    int building = 0, docked = 0;
    foreach (var b in _bays) { if (b.State == "BUILD" || b.State == "CHARGE") building++; if (b.State == "DOCKED") docked++; }
    foreach (var b in _bays) StepBay(b, ref building, ref docked);
    _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount);
    if (_tick % 3 == 0) Show(building, docked);
}

int Alive(string tag) { Dictionary<long, double> d; return _alive.TryGetValue(tag, out d) ? d.Count : 0; }
int AliveAll() { int n = 0; foreach (var d in _alive.Values) n += d.Count; return n; }
int Pending(string tag) { int n = 0; foreach (var b in _bays) if (b.Tag == tag && (b.State == "BUILD" || b.State == "CHARGE" || b.State == "RELEASE")) n++; return n; }
int PendingAll() { int n = 0; foreach (var b in _bays) if (b.State == "BUILD" || b.State == "CHARGE" || b.State == "RELEASE") n++; return n; }

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
            if (b.SawWork && b.Proj.IsProjecting && rem == 0) { b.State = "CHARGE"; b.T = _now; b.Recompiled = false; b.Msg = ""; break; }
            b.Msg = Diagnose(b, t);
            if (_now - b.LastProgress > StallSeconds * 2 && _now - b.Kick > StallSeconds)
            {   // kick the welders: off for one tick, back on next tick
                b.Kick = _now; foreach (var w in b.Welders) w.Enabled = false;
            }
            if (t > BuildTimeout) Fault(b, "build timeout");
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
            if (_targets.Count > _launched && _targets.Count > 0) { _launched++; Release(b); IGC.SendBroadcastMessage(LaunchTag + "_CMD", "launch"); }
            break;
        case "RELEASE":
            Nudge(b, t);
            if (t >= GapSeconds + NudgeSeconds)
            {
                IMyCubeBlock probe = b.DPb != null ? (IMyCubeBlock)b.DPb : (IMyCubeBlock)b.DMerge;
                if (probe != null && probe.CubeGrid == Me.CubeGrid) { Fault(b, "FUSED: drone is still on the printer grid after merge off - grind the drone's merge stalk by hand; see docs"); b.Held = true; break; }
                if (!b.Joined) { b.Joined = true; b.Joins = 0; }
            }
            if (b.Joined && t >= GapSeconds + NudgeSeconds + b.Joins)
            {
                IGC.SendBroadcastMessage(b.Tag + "_JOIN", "join");
                if (++b.Joins >= 3) { b.State = "COOLDOWN"; b.T = _now; }
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
    b.Joined = false; b.Nudged = false; b.DPb = null; b.DMerge = null; b.DBats.Clear();
}

void Fault(Bay b, string why)
{
    b.State = "FAULT"; b.T = _now; b.Msg = why; b.Faults++;
    foreach (var w in b.Welders) w.Enabled = false;
}

string Diagnose(Bay b, double t)
{
    if (!b.Proj.IsProjecting) return "no projection (projector off or blueprint missing)";
    if (_now - b.LastProgress <= StallSeconds) return "";
    if (StockEmpty()) return "STALLED: component stock empty - refill the [Stock] cargo";
    if (b.Rem > 0 && b.Rem == b.Proj.TotalBlocks) return "STALLED at 0 built: first blocks not supported / merge block not aligned?";
    return "STALLED: " + b.Rem + " blocks left - out of weld reach or missing components";
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

void Power(Bay b, bool on)
{
    if (b.Proj.Enabled != true) b.Proj.Enabled = true;      // projection stays up between drones (Keep Projection)
    if (on && _now - b.Kick < 2) return;                    // kicked welders stay off for 2 s
    foreach (var w in b.Welders) if (w.Enabled != on) w.Enabled = on;
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
    b.Merge.Enabled = false;
    if (b.DMerge != null) b.DMerge.Enabled = false;
    b.State = "RELEASE"; b.T = _now; b.Joined = false; b.Joins = 0; b.Nudged = false; b.Printed++;
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
        if (b.Msg != "") _sb.Append("\n   ").Append(b.Msg);
        _sb.Append('\n');
    }
    if (_bays.Count == 0) _sb.Append("No bays: name a projector, welders and\na merge block with [Bay 1]\n");
    _sb.Append("Ins: ").Append(Runtime.CurrentInstructionCount).Append(" / ").Append(Runtime.MaxInstructionCount).Append("  peak ").Append(_peakIns).Append('\n');
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
