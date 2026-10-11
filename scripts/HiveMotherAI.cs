// =====================================================================
//  HIVE MOTHER  v1.0  - drone printer controller
// ---------------------------------------------------------------------
//  Runs any number of print bays. A bay is the blocks whose names contain
//  "[Bay 1]", "[Bay 2]", ...: one projector (loaded with the drone
//  blueprint), its welders, and the merge block the drone latches onto.
//
//  CYCLE PER BAY
//   BUILD    projector + welders on until the projector reports 0 blocks
//            left (stalls are reported: usually missing components)
//   CHARGE   drone stays merged: it shares this ship's power and grid
//            until its batteries are full (or Charge Seconds pass)
//   RELEASE  welders off, merge off -> the drone is a separate ship;
//            "<Tag>_JOIN" tells it to start working (FORM / GUARD)
//   COOLDOWN gives it a few seconds to clear the bay, then repeats
//  RACK MODE (Stingers): finished drones stay docked; one is released
//  per enemy target seen, and told to "launch".
//
//  Keeps "Max Drones" alive by counting the hive's status broadcasts.
//  Also broadcasts this ship as the hive's home point.
//
//  COMMANDS: start | stop | max <n> | release (drop all docked) | reload
// =====================================================================
const string VER = "1.0";

string Tag = "GNAT";
string DronePrefix = "Gnat";
int MaxDrones = 8;
bool RackMode = false;
double ChargeSeconds = 20, CooldownSeconds = 5, StallSeconds = 45;
bool Printing = true;

MyIni _ini = new MyIni();
double _now; long _tick;
class Bay
{
    public int N; public IMyProjector Proj; public List<IMyShipWelder> Welders = new List<IMyShipWelder>(); public IMyShipMergeBlock Merge;
    public string State = "IDLE"; public double T; public int LastRemaining = -1; public double LastProgress;
}
List<Bay> _bays = new List<Bay>();
IMyBroadcastListener _lSwarm, _lTgt;
Dictionary<long, double> _alive = new Dictionary<long, double>();
Dictionary<long, double> _targets = new Dictionary<long, double>();
int _launched = 0;
IMyTextSurface _lcd;
StringBuilder _sb = new StringBuilder();

public Program()
{
    LoadConfig();
    _lSwarm = IGC.RegisterBroadcastListener(Tag + "_SWARM");
    _lTgt = IGC.RegisterBroadcastListener(Tag + "_TGT");
    GetBlocks();
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

void LoadConfig()
{
    MyIniParseResult r; if (!_ini.TryParse(Me.CustomData, out r)) return;
    const string S = "Hive Mother";
    Tag = C(S, "Drone IGC Tag", Tag);
    DronePrefix = C(S, "Drone Block Name Prefix", DronePrefix);
    MaxDrones = (int)C(S, "Max Drones", MaxDrones);
    RackMode = C(S, "Rack Mode (keep docked, launch on targets)", RackMode);
    ChargeSeconds = C(S, "Max Charge Seconds", ChargeSeconds);
    CooldownSeconds = C(S, "Cooldown Seconds", CooldownSeconds);
    StallSeconds = C(S, "Stall Warning Seconds", StallSeconds);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}
string C(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double C(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool C(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

void GetBlocks()
{
    var old = _bays; _bays = new List<Bay>();
    var projs = new List<IMyProjector>(); var welders = new List<IMyShipWelder>(); var merges = new List<IMyShipMergeBlock>();
    GridTerminalSystem.GetBlocksOfType(projs, b => b.CustomName.Contains("[Bay"));
    GridTerminalSystem.GetBlocksOfType(welders, b => b.CustomName.Contains("[Bay"));
    GridTerminalSystem.GetBlocksOfType(merges, b => b.CustomName.Contains("[Bay") && !b.CustomName.StartsWith(DronePrefix));
    for (int n = 1; n <= 8; n++)
    {
        string tag = "[Bay " + n + "]";
        Bay bay = null; foreach (var o in old) if (o.N == n) bay = o;
        if (bay == null) bay = new Bay { N = n };
        bay.Proj = null; bay.Merge = null; bay.Welders.Clear();
        foreach (var p in projs) if (p.CustomName.Contains(tag)) bay.Proj = p;
        foreach (var m in merges) if (m.CustomName.Contains(tag)) bay.Merge = m;
        foreach (var w in welders) if (w.CustomName.Contains(tag)) bay.Welders.Add(w);
        if (bay.Proj != null && bay.Merge != null) _bays.Add(bay);
    }
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.75f;
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
    while (_lSwarm.HasPendingMessage)
    {
        var m = _lSwarm.AcceptMessage();
        if (m.Data is MyTuple<long, Vector3D, Vector3D, long, int>) _alive[((MyTuple<long, Vector3D, Vector3D, long, int>)m.Data).Item1] = _now;
    }
    while (_lTgt.HasPendingMessage)
    {
        var m = _lTgt.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
        if (d.Item4 < 0) _targets.Remove(d.Item1); else _targets[d.Item1] = _now;
    }
    var drop = new List<long>();
    foreach (var kv in _alive) if (_now - kv.Value > 6) drop.Add(kv.Key);
    foreach (var id in drop) _alive.Remove(id);
    drop.Clear(); foreach (var kv in _targets) if (_now - kv.Value > 6) drop.Add(kv.Key);
    foreach (var id in drop) _targets.Remove(id);
    if (_tick % 6 == 0) IGC.SendBroadcastMessage(Tag + "_HOME", Me.CubeGrid.WorldVolume.Center);

    int building = 0, docked = 0;
    foreach (var b in _bays) { if (b.State == "BUILD" || b.State == "CHARGE") building++; if (b.State == "DOCKED") docked++; }
    foreach (var b in _bays) StepBay(b, ref building, ref docked);
    if (_tick % 3 == 0) Show(building, docked);
}

void StepBay(Bay b, ref int building, ref int docked)
{
    double t = _now - b.T;
    switch (b.State)
    {
        case "IDLE":
            bool want = Printing && (RackMode ? docked < _bays.Count : _alive.Count + building < MaxDrones);
            Power(b, false);
            if (want) { b.State = "BUILD"; b.T = _now; b.LastRemaining = -1; b.LastProgress = _now; b.Merge.Enabled = true; building++; }
            break;
        case "BUILD":
            Power(b, true);
            int rem = b.Proj.IsProjecting ? b.Proj.RemainingBlocks : -1;
            if (rem != b.LastRemaining) { b.LastRemaining = rem; b.LastProgress = _now; }
            bool done = (b.Proj.IsProjecting && rem == 0) || (!b.Proj.IsProjecting && b.Merge.IsConnected && t > 5);
            if (done) { b.State = "CHARGE"; b.T = _now; }
            break;
        case "CHARGE":
            Power(b, false);
            if (DroneCharged() || t > ChargeSeconds)
            {
                if (RackMode) { b.State = "DOCKED"; docked++; }
                else Release(b);
            }
            break;
        case "DOCKED":   // rack: wait for a target
            if (_targets.Count > _launched && _targets.Count > 0) { _launched++; Release(b); IGC.SendBroadcastMessage(Tag + "_CMD", "launch"); }
            break;
        case "RELEASE":
            if (t > 1.0) { IGC.SendBroadcastMessage(Tag + "_JOIN", "join"); b.State = "COOLDOWN"; b.T = _now; }
            break;
        case "COOLDOWN":
            if (t > CooldownSeconds) { b.Merge.Enabled = true; b.State = "IDLE"; b.T = _now; if (_launched > 0 && _targets.Count == 0) _launched = 0; }
            break;
    }
}

void Release(Bay b) { Power(b, false); b.Merge.Enabled = false; b.State = "RELEASE"; b.T = _now; }

void Power(Bay b, bool on)
{
    if (b.Proj.Enabled != true) b.Proj.Enabled = true;      // projection stays up between drones
    foreach (var w in b.Welders) if (w.Enabled != on) w.Enabled = on;
}

// the drone's batteries are on this grid while it's merged: find them by name
bool DroneCharged()
{
    var bats = new List<IMyBatteryBlock>();
    GridTerminalSystem.GetBlocksOfType(bats, x => x.CustomName.StartsWith(DronePrefix));
    if (bats.Count == 0) return false;
    foreach (var x in bats) if (x.CurrentStoredPower < x.MaxStoredPower * 0.95f) return false;
    return true;
}

void Command(string a)
{
    var w = a.Split(' ');
    switch (w[0])
    {
        case "start": Printing = true; break;
        case "stop": Printing = false; foreach (var b in _bays) Power(b, false); break;
        case "max": int n; if (w.Length > 1 && int.TryParse(w[1], out n)) MaxDrones = n; break;
        case "release": foreach (var b in _bays) if (b.State == "DOCKED" || b.State == "CHARGE") Release(b); break;
        case "reload": LoadConfig(); GetBlocks(); break;
    }
}

void Show(int building, int docked)
{
    _sb.Clear();
    _sb.Append("HIVE MOTHER v").Append(VER).Append(Printing ? "  PRINTING" : "  STOPPED").Append('\n');
    _sb.Append(RackMode ? "Rack: " + docked + " docked, targets " + _targets.Count : "Drones alive: " + _alive.Count + " / " + MaxDrones).Append('\n');
    foreach (var b in _bays)
    {
        _sb.Append("Bay ").Append(b.N).Append(": ").Append(b.State);
        if (b.State == "BUILD")
        {
            _sb.Append("  ").Append(b.LastRemaining >= 0 ? b.LastRemaining + " blocks left" : "no projection!");
            if (_now - b.LastProgress > StallSeconds) _sb.Append("  STALLED: components?");
        }
        _sb.Append("  welders ").Append(b.Welders.Count).Append('\n');
    }
    if (_bays.Count == 0) _sb.Append("No bays: name a projector, welders and\na merge block with [Bay 1]\n");
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
