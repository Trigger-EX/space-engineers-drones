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

bool LkDefOn = true;   // LOOKOUT: Enabled by default in the Spotter
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
    LkLoad();
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
    LkBind();
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
        try { LkStep(); } catch (Exception ex) { _lkNote = "! " + ex.GetType().Name + ": " + ex.Message; }
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
        case "lookout on": LkOn = true; break;
        case "lookout off": LkOn = false; break;
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

// >>> LOOKOUT v1.1 (shared block: harasser defence, identical in SpotterAI (Ballista) and StaffAI (Warden, Leviathan, Corsair), synced by tools/blocks_sync.py; docs/design/ballista_harasser_defence.md) >>>
// ---------------------------------------------------------------------------------------------------------------
//  A small hostile craft that stays around and shoots (an AI drone launched by an enemy warship) is neither a kamikaze (the Helm's KAMIKAZE DEFENCE) nor a wreck, and the ship's heavy fixed guns cannot answer it.
//  This module finds it, and picks a "corner jink": a velocity bias along the corner of the ship's own thrust box that lies across the harasser's line of sight (only acceleration across the line of
//  sight makes a shot miss), changed every 0.8-2.5 s at random so a gunner cannot lead it, plus a roll that puts the harasser above or below the hull (where the top and belly turrets, cameras and
//  AI turrets see it), and kites out of its gun range (Away Speed, within the large target's stand-off band). The Helm applies the bias (HARASSER LINK block) and stays the only writer of thrusters and gyros; the nose stays on the large target. Fire from a bearing nobody sees
//  (the hit direction, no tracked craft there) gets the same dodge. Link: "HDEF<Helm PB EntityId>" .H2A / .HTK (Helm -> here) and .A2H (here -> Helm), CurrentConstruct only.
//  COMMANDS: lookout on | lookout off.  CUSTOM DATA [Harasser Defence]: Enabled, Max Radius (m), Band Min / Max (m), Dwell (s), Latch (s), Jink Speed (m/s), Jink Period Min / Max (s),
//   Harasser Projectile Speed (m/s), Turret Range (m), Phantom Range (m), Roll To Threat, Away Speed (m/s) / Away Within (m) (kiting: also open the range while a harasser is inside its gun range)
// ---------------------------------------------------------------------------------------------------------------
//  v1.1 (2026-10-14): shared with StaffAI. The host declares "bool LkDefOn" (Enabled default: Spotter true, Staff false; the Warden, Leviathan and Corsair Staff Custom Data say true);
//   a copy that is not in a "[Spotter]" PB stands down while a "[Spotter]" PB is on the construct (one Lookout per ship); "Max Fire Range (m)" (was a constant 1700).
//  Hooks: LkLoad() while the host parses Custom Data (before it writes _ini back); LkBind() every ~5 s; LkStep() every Update10 run (in its own try/catch);
//   LkStatus() on the screen; "lookout on|off" commands. Uses from the host: _ini (MyIni), _now (seconds), Me, GridTerminalSystem, IGC.
bool LkOn = true, LkRoll = true;
double LkMaxR = 15, LkBandMin = 30, LkBandMax = 1600, LkDwell = 3, LkLatch = 15, LkJink = 30, LkPerMin = 0.8, LkPerMax = 2.5, LkVp = 800, LkTurR = 800, LkPhR = 1000, LkAway = 40, LkAwayR = 900;
const double LkMargin = 150, LkNose = 0.94; double LkMaxFire = 1700;
class LkT { public long Id; public Vector3D P, V; public double R, Seen, In = -1, Ev = -99, KzT = -99, Rng; public int St; }
Dictionary<long, LkT> _lk = new Dictionary<long, LkT>();
IMyBroadcastListener _lkA, _lkK;
long _lkHid, _lkTid, _lkI1, _lkI2;
double _lkHeard = -99, _lkHitAt = -99, _lkHitDirT = -99, _lkStand = 1600, _lkPhT = -99, _lkNext = -99, _lkBoxT = -99, _lkAF, _lkAB, _lkAR, _lkAL, _lkAU, _lkAD;
int _lkHits = -1, _lkCur = -1, _lkNH, _lkFl, _lkSent;
Vector3D _lkHitDir, _lkTPos, _lkPhU, _lkB, _lkUp;
bool _lkRollOn, _lkYield;
IMyRemoteControl _lkRc;
List<IMyThrust> _lkThr = new List<IMyThrust>();
List<IMyLargeTurretBase> _lkTur = new List<IMyLargeTurretBase>();
Random _lkRng = new Random();
string _lkNote = "off";

bool LkCfgB(string k, bool d) { if (!_ini.ContainsKey("Harasser Defence", k)) _ini.Set("Harasser Defence", k, d); return _ini.Get("Harasser Defence", k).ToBoolean(d); }
double LkCfg(string k, double d) { if (!_ini.ContainsKey("Harasser Defence", k)) _ini.Set("Harasser Defence", k, d); return _ini.Get("Harasser Defence", k).ToDouble(d); }
void LkLoad()
{
    LkOn = LkCfgB("Enabled", LkDefOn); LkRoll = LkCfgB("Roll To Threat", LkRoll);
    LkMaxR = LkCfg("Max Radius (m)", LkMaxR); LkBandMin = LkCfg("Band Min (m)", LkBandMin); LkBandMax = LkCfg("Band Max (m)", LkBandMax);
    LkDwell = LkCfg("Dwell (s)", LkDwell); LkLatch = LkCfg("Latch (s)", LkLatch); LkJink = LkCfg("Jink Speed (m/s)", LkJink);
    LkPerMin = LkCfg("Jink Period Min (s)", LkPerMin); LkPerMax = Math.Max(LkPerMin, LkCfg("Jink Period Max (s)", LkPerMax));
    LkAway = LkCfg("Away Speed (m/s)", LkAway); LkAwayR = LkCfg("Away Within (m)", LkAwayR);
    LkMaxFire = LkCfg("Max Fire Range (m)", LkMaxFire);
    LkVp = Math.Max(100, LkCfg("Harasser Projectile Speed (m/s)", LkVp)); LkTurR = LkCfg("Turret Range (m)", LkTurR); LkPhR = LkCfg("Phantom Range (m)", LkPhR);
}

void LkBind()   // the Helm (re-found every 5 s: a rebuilt Helm has a new EntityId and so a new key), the Remote Control, the thrusters, the vanilla turrets
{
    var pbs = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(pbs, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Helm]", StringComparison.OrdinalIgnoreCase));
    var sp = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(sp, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Spotter]", StringComparison.OrdinalIgnoreCase));
    _lkYield = sp.Count > 0 && !Me.CustomName.StartsWith("[Spotter]", StringComparison.OrdinalIgnoreCase);   // one Lookout per ship: the Spotter's
    long id = pbs.Count > 0 ? pbs[0].EntityId : 0;
    if (id != _lkHid)
    {
        if (_lkA != null) IGC.DisableBroadcastListener(_lkA);
        if (_lkK != null) IGC.DisableBroadcastListener(_lkK);
        _lkHid = id; _lkHeard = -99; _lkHits = -1;
        _lkA = id == 0 ? null : IGC.RegisterBroadcastListener("HDEF" + id + ".H2A");
        _lkK = id == 0 ? null : IGC.RegisterBroadcastListener("HDEF" + id + ".HTK");
    }
    var rcs = new List<IMyRemoteControl>();
    GridTerminalSystem.GetBlocksOfType(rcs, b => b.CubeGrid == Me.CubeGrid && b.CustomName.StartsWith("[SH]"));
    _lkRc = rcs.Count > 0 ? rcs[0] : null;
    _lkThr.Clear(); GridTerminalSystem.GetBlocksOfType(_lkThr, b => b.CubeGrid == Me.CubeGrid);
    _lkTur.Clear(); GridTerminalSystem.GetBlocksOfType(_lkTur, b => b.CubeGrid == Me.CubeGrid && b.CustomName.StartsWith("[SH]"));
}

void LkSee(long id, Vector3D p, Vector3D v, double r, double seen, bool kz)
{
    if (id == 0 || r > LkMaxR * 1.5) return;
    LkT t;
    if (!_lk.TryGetValue(id, out t)) { t = new LkT { Id = id, P = p, V = v, Seen = seen }; _lk[id] = t; }
    else
    {
        double dt = seen - t.Seen;
        if (dt < 0) return;                                                         // an older fix than the one held
        if (v.LengthSquared() < 0.01 && dt > 0.05 && dt < 2) v = (p - t.P) / dt;      // (a turret's target velocity can read 0: difference the positions)
    }
    t.P = p; t.V = v; t.R = r; t.Seen = seen;
    if (kz) t.KzT = _now;
}

// a contact closing on a collision course is the Helm's KAMIKAZE DEFENCE's job, not a harasser
bool LkIncoming(LkT t, Vector3D rel)
{
    Vector3D v = t.V - _lkRc.GetShipVelocities().LinearVelocity; double vv = v.LengthSquared(), rng = rel.Length();
    if (vv < 1 || rng < 1) return false;
    double tgo = -Vector3D.Dot(rel, v) / vv;
    if (tgo <= 0 || tgo > 12) return false;
    return (rel + v * tgo).Length() < 40 + 60 * rng / 1000 && -Vector3D.Dot(rel, v) / rng > 15;
}

void LkClassify(Vector3D me)
{
    bool hitFresh = _now - _lkHitAt < 6 && _now - _lkHitDirT < 3;
    var gone = new List<long>();
    foreach (var t in _lk.Values)
    {
        if (_now - t.Seen > 8) { gone.Add(t.Id); continue; }
        Vector3D rel = t.P + t.V * (_now - t.Seen) - me; double rng = rel.Length(); t.Rng = rng;
        bool cand = _now - t.Seen < 3 && _now - t.KzT > 4 && rng >= LkBandMin && rng <= LkBandMax && !LkIncoming(t, rel);
        if (!cand) { t.In = -1; if (_now - t.Ev > LkLatch) t.St = 0; continue; }
        if (t.In < 0) t.In = _now;
        if (_now - t.In < LkDwell) { if (t.St == 0) t.St = 1; continue; }
        bool dmg = hitFresh && rng > 1 && Vector3D.Dot(rel / rng, _lkHitDir) > 0.5;        // damage from about its direction (60 deg)
        bool pers = rng <= LkTurR && _now - t.In >= 5;                                     // it has stayed inside gun range
        if (dmg || pers) t.Ev = _now;
        t.St = _now - t.Ev < LkLatch ? 2 : 1;
    }
    foreach (var id in gone) _lk.Remove(id);
}

void LkBox()
{
    if (_now - _lkBoxT < 2) return;
    _lkBoxT = _now;
    double m = Math.Max(_lkRc.CalculateShipMass().PhysicalMass, 1), f = 0, b = 0, r = 0, l = 0, u = 0, d = 0;
    MatrixD w = _lkRc.WorldMatrix;
    foreach (var t in _lkThr)
    {
        if (!t.IsWorking) continue;
        Vector3D p = t.WorldMatrix.Backward; double th = t.MaxEffectiveThrust;      // thrusters push opposite their exhaust
        if (Vector3D.Dot(p, w.Forward) > 0.9) f += th; else if (Vector3D.Dot(p, w.Backward) > 0.9) b += th;
        else if (Vector3D.Dot(p, w.Right) > 0.9) r += th; else if (Vector3D.Dot(p, w.Left) > 0.9) l += th;
        else if (Vector3D.Dot(p, w.Up) > 0.9) u += th; else if (Vector3D.Dot(p, w.Down) > 0.9) d += th;
    }
    _lkAF = f / m; _lkAB = b / m; _lkAR = r / m; _lkAL = l / m; _lkAU = u / m; _lkAD = d / m;
}

void LkPlan(Vector3D me)
{
    var us = new List<Vector3D>(); var ws = new List<double>();
    _lkI1 = 0; _lkI2 = 0; _lkNH = 0;
    var hs = new List<LkT>();
    foreach (var t in _lk.Values) if (t.St == 2) hs.Add(t);
    hs.Sort((x, y) => x.Rng.CompareTo(y.Rng));
    double reff = LkPhR; Vector3D uH = Vector3D.Zero;
    for (int i = 0; i < hs.Count && i < 3; i++)
    {
        Vector3D r = hs[i].P + hs[i].V * (_now - hs[i].Seen) - me;
        if (r.LengthSquared() < 1) continue;
        us.Add(Vector3D.Normalize(r)); ws.Add(_now - _lkHitAt < 6 ? 1.5 : 1.0);
        if (_lkNH == 0) { reff = hs[i].Rng; uH = us[us.Count - 1]; _lkI1 = hs[i].Id; } else if (_lkNH == 1) _lkI2 = hs[i].Id;
        _lkNH++;
    }
    // fire from a bearing no tracked craft explains (a drone beyond sight, or a big gun): the same dodge, as a bearing only
    if (_now - _lkHitAt < 3 && _now - _lkHitDirT < 3)
    {
        bool explained = false;
        foreach (var t in _lk.Values)
        {
            if (_now - t.Seen > 3) continue;
            Vector3D r = t.P + t.V * (_now - t.Seen) - me;
            if (r.LengthSquared() > 1 && Vector3D.Dot(Vector3D.Normalize(r), _lkHitDir) > 0.64) explained = true;      // within 50 deg of the hit direction
        }
        if (!explained) { _lkPhU = _lkHitDir; _lkPhT = _now; }
    }
    bool phantom = _now - _lkPhT < 10;
    if (phantom) { us.Add(_lkPhU); ws.Add(0.7); }
    if (us.Count == 0) { _lkB = Vector3D.Zero; _lkCur = -1; _lkRollOn = false; _lkFl = 0; return; }
    LkBox();
    MatrixD w = _lkRc.WorldMatrix;
    bool haveT = _lkTid != 0 && _now - _lkHeard < 2; Vector3D uT = Vector3D.Zero; double dT = 0;
    if (haveT) { Vector3D r = _lkTPos - me; dT = r.Length(); if (dT > 1) uT = r / dT; else haveT = false; }
    var cs = new Vector3D[8]; var sc = new double[8]; double max = 0;
    for (int k = 0; k < 8; k++)
    {
        int sr = (k & 1) != 0 ? 1 : -1, su = (k & 2) != 0 ? 1 : -1, sf = (k & 4) != 0 ? 1 : -1;
        Vector3D c = w.Right * sr * (sr > 0 ? _lkAR : _lkAL) + w.Up * su * (su > 0 ? _lkAU : _lkAD) + w.Forward * sf * (sf > 0 ? _lkAF : _lkAB);
        cs[k] = c; double s = 0;
        for (int i = 0; i < us.Count; i++) s += ws[i] * (c - Vector3D.Dot(c, us[i]) * us[i]).Length();
        double cl = c.Length();
        if (haveT && cl > 1)
        {
            double cu = Vector3D.Dot(c, uT) / cl;
            if ((cu > 0.2 && dT < _lkStand - LkMargin) || (cu < -0.2 && dT > LkMaxFire)) s = -1;     // keep the large target inside the stand-off band and the rails' range
        }
        sc[k] = s; if (s > max) max = s;
    }
    if (max <= 0) { for (int k = 0; k < 8; k++) sc[k] = cs[k].Length(); max = 1; }                   // (every corner excluded: ignore the band rather than stand still)
    bool bad = _lkCur < 0 || sc[_lkCur] < 0.6 * max;
    if (bad || _now >= _lkNext)
    {
        var ok = new List<int>();
        for (int k = 0; k < 8; k++) if (sc[k] >= 0.8 * max && k != _lkCur) ok.Add(k);
        if (ok.Count == 0) for (int k = 0; k < 8; k++) if (sc[k] >= 0.8 * max) ok.Add(k);
        _lkCur = ok.Count > 0 ? ok[_lkRng.Next(ok.Count)] : 0;
        _lkNext = _now + MathHelper.Clamp((0.7 + 0.8 * _lkRng.NextDouble()) * reff / LkVp, LkPerMin, LkPerMax);
    }
    Vector3D c0 = cs[_lkCur]; double c0l = c0.Length();
    _lkB = c0l > 1 ? c0 / c0l * LkJink : Vector3D.Zero;
    // kite: while a harasser is inside its gun range, also open the range (the nose stays on the large target; the stand-off band of the large target limits it as for the corners)
    if (_lkNH > 0 && LkAway > 0 && reff < LkAwayR)
    {
        Vector3D a = -uH; double au = haveT ? Vector3D.Dot(a, uT) : 0;
        if (!haveT || !((au > 0.2 && dT < _lkStand - LkMargin) || (au < -0.2 && dT > LkMaxFire))) _lkB += a * LkAway;
        if (_lkB.LengthSquared() > 3600) _lkB = Vector3D.Normalize(_lkB) * 60;
    }
    int fl = 1 | 2 | (_lkNH > 0 ? 8 : 0);
    // roll: the nearest harasser above or below the hull (at most a quarter turn), unless it sits near the nose axis; deadband 20 deg
    _lkRollOn = false;
    if (LkRoll && _lkNH > 0)
    {
        Vector3D n = w.Forward, p = uH - Vector3D.Dot(uH, n) * n;
        if (Math.Abs(Vector3D.Dot(uH, n)) < LkNose && p.LengthSquared() > 0.01)
        {
            p.Normalize(); if (Vector3D.Dot(p, w.Up) < 0) p = -p;
            if (Vector3D.Dot(p, w.Up) < 0.94) { _lkUp = p; _lkRollOn = true; fl |= 4; }
        }
    }
    _lkFl = fl;
}

void LkSend()
{
    int fl = _lkFl | 256;
    IGC.SendBroadcastMessage("HDEF" + _lkHid + ".A2H", new MyTuple<Vector3D, Vector3D, int, float, long, long>(_lkB, _lkRollOn ? _lkUp : Vector3D.Zero, fl, 0.6f, _lkI1, _lkI2), TransmissionDistance.CurrentConstruct);
    _lkSent++;
}

void LkStep()
{
    if (!LkOn) { _lkNote = "off"; return; }
    if (_lkYield) { _lkNote = "the Spotter PB runs it"; return; }
    if (_lkA == null) { _lkNote = "no Helm"; return; }
    while (_lkA.HasPendingMessage)
    {
        var m = _lkA.AcceptMessage();
        if (!(m.Data is MyTuple<int, Vector3D, double, double, long, Vector3D>)) continue;
        var d = (MyTuple<int, Vector3D, double, double, long, Vector3D>)m.Data;
        if (((d.Item1 >> 8) & 15) != 1) { _lkNote = "link version?"; continue; }
        int hits = (d.Item1 >> 16) & 0x7FFF;
        if (_lkHits >= 0 && hits != _lkHits) _lkHitAt = _now;
        _lkHits = hits; _lkStand = d.Item4; _lkTid = d.Item5; _lkTPos = d.Item6; _lkHeard = _now;
        if (d.Item2.LengthSquared() > 0.01 && d.Item3 < 5) { _lkHitDir = Vector3D.Normalize(d.Item2); _lkHitDirT = _now - d.Item3; }
    }
    while (_lkK.HasPendingMessage)
    {
        var m = _lkK.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, float, float, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, float, float, int>)m.Data;
        LkSee(d.Item1, d.Item2, d.Item3, d.Item4, _now - d.Item5, (d.Item6 & 1) != 0);
    }
    if (_now - _lkHeard > 2) { _lkNote = "waiting for the Helm"; return; }              // (no message sent: the Helm drops the bias after its 0.6 s ttl)
    if (_lkRc == null) { _lkNote = "no [SH] Remote Control"; return; }
    foreach (var tu in _lkTur)
    {
        if (!tu.HasTarget) continue;
        var e = tu.GetTargetedEntity();
        if (e.IsEmpty() || e.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) continue;
        LkSee(e.EntityId, e.Position, e.Velocity, e.BoundingBox.Size.Length() * 0.5, _now, false);
    }
    Vector3D me = _lkRc.GetPosition();
    LkClassify(me);
    LkPlan(me);
    LkSend();
    _lkNote = "link OK";
}

string LkStatus()
{
    var s = new StringBuilder("LOOKOUT  ").Append(_lkNote).Append('\n');
    if (!LkOn || _lkYield || _lkA == null) return s.ToString();
    int sus = 0, har = 0; double near = 0;
    foreach (var t in _lk.Values) { if (t.St == 1) sus++; else if (t.St == 2) { har++; if (near == 0 || t.Rng < near) near = t.Rng; } }
    s.Append("Threat   ").Append(har > 0 ? "HARASSER " + near.ToString("0") + " m x" + har : _now - _lkPhT < 10 ? "FIRE FROM " + Math.Round(Math.Atan2(_lkPhU.X, _lkPhU.Z) * 57.3) + " deg (unseen)" : "none").Append("  tracks ").Append(_lk.Count).Append(" (").Append(sus).Append(" suspect)\n");
    s.Append("Dodge    ").Append(_lkB.LengthSquared() > 1 ? "corner " + _lkCur + "  " + _lkB.Length().ToString("0") + " m/s" : "-").Append(_lkRollOn ? "  roll" : "").Append("  sent ").Append(_lkSent).Append('\n');
    return s.ToString();
}
// <<< LOOKOUT v1.1 <<<

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
    _sb.Append(LkStatus());
    _sb.Append("Ins ").Append(Runtime.CurrentInstructionCount).Append('/').Append(Runtime.MaxInstructionCount).Append('\n');
    if (_dumpText != "") _sb.Append(_dumpText);
    if (_err != "") _sb.Append("! ").Append(_err).Append('\n');
    string s = _sb.ToString();
    _lcd.WriteText(s);
    Echo(s);
}
