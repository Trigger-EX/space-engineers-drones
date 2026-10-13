// =====================================================================
//  SPOTTER AI  v0.1  - vitals memory and probe-bay control for the Ballista railgun drone
// ---------------------------------------------------------------------
//  Runs in the Ballista's second Programmable Block ("[Spotter][SH] ..."). Three jobs:
//
//  1. MEMORY. The Peeper probes report every vital block they find as a position in the TARGET'S OWN FRAME:
//         <Host Tag>_CRIT = (target id, group 0 PowerSystems / 1 Weapons / 2 Propulsion / 3 Default, position, ray direction, probe id)
//     The Spotter keeps all of them per target: nearby reports of one group are merged into one vital (position averaged, hit count
//     raised), a vital that nobody has seen for "Expire Seconds" while the probes keep reporting other blocks is forgotten (destroyed or a
//     false pick). The memory is saved in Storage, so a recompile or a lost probe loses nothing.
//
//  2. FEED. For the target the Helm is shooting at, the best remembered vital is sent to the Helm every "Refresh Seconds":
//         <Host Tag>_AIM = (target id, position in the target frame)       -> ShrikeAI makes it the BREACH point and drills it
//     Order: "Priority" (default 0,1,2 = power, weapons, propulsion; railguns want the reactors and batteries first), then the vitals
//     seen most often, then the freshest. A vital that has been fed for "Hold Seconds" without being removed is marked tried and the next
//     one is used; when every vital of every group was tried the marks are cleared.
//
//  3. BAY. The probe foundry (a small-grid printer docked on two connectors, its Programmable Block named "Hive Mother PB") is started when
//     the Helm has a target of at least "Min Target Radius" m (default 25 = the Helm's Breach Min Radius: only such targets are drilled) and no probe is alive, and stopped when the target is gone or enough probes
//     live. The Spotter also presses Connect on the foundry connectors whenever they are connectable (a pasted blueprint arrives unlocked).
//     Probes are counted by their <Probe Tag>_SWARM heartbeat.
//
//  COMMANDS (Run argument): reset | dump | bay on | bay off | feed on | feed off | reload
//  CUSTOM DATA [Spotter]: Host Tag, Probe Tag, Priority, Hold Seconds, Expire Seconds, Refresh Seconds, Merge Radius, Min Target Radius,
//   Max Probes, Print Cooldown, Bay Control, Hive Block Name, Connector Name
// =====================================================================
const string VER = "0.1";
string HostTag = "BALLISTA", ProbeTag = "PEEPER", HiveName = "Hive Mother PB", ConnName = "Foundry Connector";
string PrioText = "0,1,2";
double HoldSec = 45, ExpireSec = 90, RefreshSec = 4, MergeR = 6, MinRadius = 25, PrintCooldown = 15;
int MaxProbes = 1;
bool BayControl = true, FeedOn = true;

class Vital
{
    public int G; public Vector3D P, D; public int Hits = 1; public double First, Last, Fed = -1; public bool Tried;
}
class Mem
{
    public long Id; public List<Vital> V = new List<Vital>(); public double LastAny, Touched;
}
Dictionary<long, Mem> _mem = new Dictionary<long, Mem>();
Dictionary<long, double> _probes = new Dictionary<long, double>();
IMyBroadcastListener _lCrit, _lTgt, _lSwarm;
MyIni _ini = new MyIni();
StringBuilder _sb = new StringBuilder();
IMyTextSurface _lcd;
IMyProgrammableBlock _hive;
List<IMyShipConnector> _conn = new List<IMyShipConnector>();
int[] _prio = { 0, 1, 2, 3 };

double _now, _tgtSeen = -99, _lastFeed = -99, _lastBay = -99, _lastSave = -99, _noProbeSince = -99, _lastProbeSeen = -99, _saveGap = 15, _holdBayUntil = -99;
long _tid; double _tRad; Vector3D _tPos;
Vector3D? _fedP; int _fedG = -1; long _fedT;
bool _bayWant, _bayKnown, _dirty;
string _log = "", _err = "", _bayNote = "", _dumpText = "";
int _tick, _sent;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    try { Load(); LoadStorage(); GetBlocks(); } catch (Exception ex) { _err = "init: " + ex.Message; }
}

public void Save() { SaveStorage(); }

void Load()
{
    _ini.Clear();
    MyIniParseResult pr;
    if (!_ini.TryParse(Me.CustomData, out pr)) _ini.Clear();
    HostTag = Cfg("Host Tag", HostTag).ToUpperInvariant(); ProbeTag = Cfg("Probe Tag", ProbeTag).ToUpperInvariant();
    PrioText = Cfg("Priority", PrioText);
    HoldSec = Cfg("Hold Seconds", HoldSec); ExpireSec = Cfg("Expire Seconds", ExpireSec); RefreshSec = Cfg("Refresh Seconds", RefreshSec);
    MergeR = Cfg("Merge Radius", MergeR); MinRadius = Cfg("Min Target Radius", MinRadius); MaxProbes = (int)Cfg("Max Probes", MaxProbes);
    PrintCooldown = Cfg("Print Cooldown", PrintCooldown); BayControl = Cfg("Bay Control", BayControl ? 1 : 0) > 0.5;
    HiveName = Cfg("Hive Block Name", HiveName); ConnName = Cfg("Connector Name", ConnName);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    var p = new List<int>();
    foreach (var s in PrioText.Split(',')) { int n; if (int.TryParse(s.Trim(), out n) && n >= 0 && n <= 3 && !p.Contains(n)) p.Add(n); }
    for (int i = 0; i < 4; i++) if (!p.Contains(i)) p.Add(i);
    _prio = p.ToArray();
    _lCrit = IGC.RegisterBroadcastListener(HostTag + "_CRIT");
    _lTgt = IGC.RegisterBroadcastListener(HostTag + "_TGT");
    _lSwarm = IGC.RegisterBroadcastListener(ProbeTag + "_SWARM");
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.8f;
}
string Cfg(string k, string d) { if (!_ini.ContainsKey("Spotter", k)) _ini.Set("Spotter", k, d); return _ini.Get("Spotter", k).ToString(d); }
double Cfg(string k, double d) { if (!_ini.ContainsKey("Spotter", k)) _ini.Set("Spotter", k, d); return _ini.Get("Spotter", k).ToDouble(d); }

void GetBlocks()
{
    _hive = null; _conn.Clear();
    var pbs = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(pbs, b => b.CustomName.Contains(HiveName));
    if (pbs.Count > 0) _hive = pbs[0];
    GridTerminalSystem.GetBlocksOfType(_conn, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains(ConnName));
}

public void Main(string arg, UpdateType u)
{
    _tick++;
    _now += Runtime.TimeSinceLastRun.TotalSeconds;
    try
    {
        if ((u & (UpdateType.Terminal | UpdateType.Trigger)) != 0 && arg != "") Command(arg.Trim().ToLowerInvariant());
        if (_tick % 30 == 1) GetBlocks();
        Read();
        Prune();
        Feed();
        Bay();
        if (_dirty && _now - _lastSave > _saveGap) SaveStorage();
        if (_tick % 3 == 0) Display();
    }
    catch (Exception ex) { _err = ex.GetType().Name + ": " + ex.Message; }
}

void Command(string c)
{
    switch (c)
    {
        case "reset": _mem.Clear(); _fedP = null; _dumpText = ""; _dirty = true; SaveStorage(); break;
        case "dump": _dumpText = Dump(); break;
        case "bay on": BayControl = true; break;
        case "bay off": BayControl = false; break;
        case "feed on": FeedOn = true; break;
        case "feed off": FeedOn = false; break;
        case "reload": Load(); GetBlocks(); break;
    }
}

// ---------------------------------------------------------------------------------------------------------------
//  MEMORY
// ---------------------------------------------------------------------------------------------------------------
void Read()
{
    while (_lTgt.HasPendingMessage)
    {
        var msg = _lTgt.AcceptMessage();
        if (!(msg.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, double, long>)msg.Data;
        if (d.Item4 < 0) { if (d.Item1 == _tid) _tgtSeen = -99; continue; }
        if (_tid != d.Item1) { _fedP = null; }
        _tid = d.Item1; _tPos = d.Item2; _tRad = d.Item4; _tgtSeen = _now;
    }
    while (_lCrit.HasPendingMessage)
    {
        var msg = _lCrit.AcceptMessage();
        if (!(msg.Data is MyTuple<long, int, Vector3D, Vector3D, long>)) continue;
        var d = (MyTuple<long, int, Vector3D, Vector3D, long>)msg.Data;
        Remember(d.Item1, d.Item2, d.Item3, d.Item4);
        _probes[d.Item5] = _now;
    }
    while (_lSwarm.HasPendingMessage)
    {
        var msg = _lSwarm.AcceptMessage();
        if (!(msg.Data is MyTuple<long, Vector3D, Vector3D, long, int>)) continue;
        _probes[((MyTuple<long, Vector3D, Vector3D, long, int>)msg.Data).Item1] = _now;
    }
}

void Remember(long tid, int g, Vector3D p, Vector3D dir)
{
    if (g < 0 || g > 3) return;
    Mem m;
    if (!_mem.TryGetValue(tid, out m)) { m = new Mem { Id = tid }; _mem[tid] = m; }
    m.LastAny = _now; m.Touched = _now; _dirty = true;
    foreach (var v in m.V)
    {
        if (v.G != g || (v.P - p).LengthSquared() > MergeR * MergeR) continue;
        v.P = (v.P * v.Hits + p) / (v.Hits + 1); v.D = dir; v.Hits++; v.Last = _now;
        return;
    }
    if (m.V.Count >= 40)
    {
        int old = 0;
        for (int i = 1; i < m.V.Count; i++) if (m.V[i].Last < m.V[old].Last) old = i;
        m.V.RemoveAt(old);
    }
    m.V.Add(new Vital { G = g, P = p, D = dir, First = _now, Last = _now });
    _log = "new " + GroupName(g) + " vital (" + m.V.Count + " known)";
}

void Prune()
{
    if (_tick % 10 != 0) return;
    foreach (var m in _mem.Values)
    {
        if (_now - m.LastAny > 20) continue;                         // the probes are silent: nothing is proven about the old vitals
        for (int i = m.V.Count - 1; i >= 0; i--)
            if (_now - m.V[i].Last > ExpireSec) { m.V.RemoveAt(i); _dirty = true; }
    }
    if (_mem.Count > 4)                                              // keep the four freshest targets
    {
        long oldest = 0; double ot = double.MaxValue;
        foreach (var m in _mem.Values) if (m.Touched < ot) { ot = m.Touched; oldest = m.Id; }
        _mem.Remove(oldest);
    }
}

Vital Best(Mem m)
{
    for (int pass = 0; pass < 2; pass++)
    {
        foreach (int g in _prio)
        {
            Vital b = null;
            foreach (var v in m.V)
            {
                if (v.G != g || v.Tried) continue;
                if (b == null || v.Hits > b.Hits || (v.Hits == b.Hits && v.Last > b.Last)) b = v;
            }
            if (b != null) return b;
        }
        if (pass == 0) foreach (var v in m.V) v.Tried = false;       // everything was tried: start over
    }
    return null;
}

// ---------------------------------------------------------------------------------------------------------------
//  FEED TO THE HELM
// ---------------------------------------------------------------------------------------------------------------
bool TgtFresh() { return _now - _tgtSeen < 8; }

void Feed()
{
    if (!FeedOn || !TgtFresh()) return;
    Mem m;
    if (!_mem.TryGetValue(_tid, out m) || m.V.Count == 0) { _fedP = null; return; }
    Vital b = Best(m);
    if (b == null) return;
    if (b.Fed < 0) b.Fed = _now;
    else if (_now - b.Fed > HoldSec) { b.Tried = true; b.Fed = -1; b = Best(m); if (b == null) return; if (b.Fed < 0) b.Fed = _now; }
    bool changed = !_fedP.HasValue || _fedT != _tid || (_fedP.Value - b.P).LengthSquared() > 1;
    if (!changed && _now - _lastFeed < RefreshSec) return;
    IGC.SendBroadcastMessage(HostTag + "_AIM", new MyTuple<long, Vector3D>(_tid, b.P), TransmissionDistance.CurrentConstruct);
    _lastFeed = _now; _fedP = b.P; _fedG = b.G; _fedT = _tid; _sent++;
}

// ---------------------------------------------------------------------------------------------------------------
//  BAY
// ---------------------------------------------------------------------------------------------------------------
int Live()
{
    int n = 0;
    foreach (var t in _probes.Values) if (_now - t < 4) n++;
    return n;
}

void Bay()
{
    // connectors: lock whenever the pair is lined up
    foreach (var c in _conn)
        if (c.IsFunctional && c.Status == MyShipConnectorStatus.Connectable) c.Connect();
    if (!BayControl || _hive == null || _now - _lastBay < 2) { if (_hive == null) _bayNote = "no foundry PB found"; return; }
    _lastBay = _now;
    int live = Live();
    if (live > 0) _lastProbeSeen = _now;
    else if (_lastProbeSeen > 0 && _noProbeSince < _lastProbeSeen) { _noProbeSince = _now; _holdBayUntil = _now + PrintCooldown; }   // a probe was lost: wait before the next
    bool want = TgtFresh() && _tRad >= MinRadius && live < MaxProbes && _now > _holdBayUntil;
    if (want != _bayWant || !_bayKnown || _tick % 300 == 0)
    {
        _bayWant = want; _bayKnown = true;
        bool ok = want ? (_hive.TryRun("max " + MaxProbes) & _hive.TryRun("start")) : _hive.TryRun("stop");
        _bayNote = (want ? "printing" : "idle") + (ok ? "" : " (foundry PB busy)");
    }
}

string GroupName(int g) { return g == 0 ? "power" : g == 1 ? "weapons" : g == 2 ? "propulsion" : "default"; }

// ---------------------------------------------------------------------------------------------------------------
//  STORAGE  (one vital per line:  target|group|x|y|z|hits)
// ---------------------------------------------------------------------------------------------------------------
void SaveStorage()
{
    var s = new StringBuilder();
    foreach (var m in _mem.Values)
        foreach (var v in m.V)
            s.Append(m.Id).Append('|').Append(v.G).Append('|').Append(v.P.X.ToString("0.00")).Append('|').Append(v.P.Y.ToString("0.00")).Append('|').Append(v.P.Z.ToString("0.00")).Append('|').Append(v.Hits).Append('\n');
    Storage = s.ToString(); _lastSave = _now; _dirty = false;
}

void LoadStorage()
{
    foreach (var line in Storage.Split('\n'))
    {
        var w = line.Split('|');
        long id; int g, h; double x, y, z;
        if (w.Length < 6 || !long.TryParse(w[0], out id) || !int.TryParse(w[1], out g) || !double.TryParse(w[2], out x) || !double.TryParse(w[3], out y) || !double.TryParse(w[4], out z) || !int.TryParse(w[5], out h)) continue;
        Mem m;
        if (!_mem.TryGetValue(id, out m)) { m = new Mem { Id = id }; _mem[id] = m; }
        m.V.Add(new Vital { G = g, P = new Vector3D(x, y, z), Hits = h, First = 0, Last = 0 });
    }
}

string Dump()
{
    var s = new StringBuilder();
    foreach (var m in _mem.Values)
    {
        s.Append("target ").Append(m.Id).Append('\n');
        foreach (var v in m.V) s.Append("  ").Append(GroupName(v.G)).Append(" hits ").Append(v.Hits).Append(" at ").Append(v.P.X.ToString("0")).Append(',').Append(v.P.Y.ToString("0")).Append(',').Append(v.P.Z.ToString("0")).Append(v.Tried ? " tried" : "").Append('\n');
    }
    return s.ToString();
}

void Display()
{
    _sb.Clear();
    _sb.Append("SPOTTER v").Append(VER).Append("  host ").Append(HostTag).Append('\n');
    Mem m = null;
    if (TgtFresh()) { _mem.TryGetValue(_tid, out m); _sb.Append("Target ").Append(((_tPos - Me.GetPosition()).Length() / 1000).ToString("0.00")).Append(" km  r ").Append(_tRad.ToString("0")).Append(" m\n"); }
    else _sb.Append("Target none\n");
    int[] n = new int[4];
    if (m != null) foreach (var v in m.V) n[v.G]++;
    _sb.Append("Vitals  power ").Append(n[0]).Append("  weapons ").Append(n[1]).Append("  propulsion ").Append(n[2]).Append('\n');
    _sb.Append("Feed    ").Append(!FeedOn ? "off" : _fedP.HasValue && _fedT == _tid ? GroupName(_fedG) + " (" + _sent + " sent)" : "-").Append('\n');
    _sb.Append("Probes  ").Append(Live()).Append('/').Append(MaxProbes).Append("  bay ").Append(BayControl ? _bayNote : "manual").Append('\n');
    _sb.Append("Conn    ");
    foreach (var c in _conn) _sb.Append(c.Status == MyShipConnectorStatus.Connected ? "L" : c.Status == MyShipConnectorStatus.Connectable ? "c" : "-");
    _sb.Append("  mem ").Append(_mem.Count).Append(" tgt\n");
    _sb.Append(_log).Append('\n');
    _sb.Append("Ins ").Append(Runtime.CurrentInstructionCount).Append('/').Append(Runtime.MaxInstructionCount).Append('\n');
    if (_dumpText != "") _sb.Append(_dumpText);
    if (_err != "") _sb.Append("! ").Append(_err).Append('\n');
    string s = _sb.ToString();
    _lcd.WriteText(s);
    Echo(s);
}
