// =====================================================================
//  CRADLE BAY AI  v1.1  - the bay manager of merge-block cradles (Cradle Pod PB, the Corsair's pylon cradles). docs/design/control_arch.md s4.2, grids_bays.md s6.1.
//  For Space Engineers (vanilla). Paste into a Programmable Block named "[Pod] ..." / "[Bay] ..." on the grid that carries the cradle merge blocks. Update10 + an IGC callback.
//  A cut-down HiveMotherAI (FindDrone / Arm / Separate / StillAttached / FUSED check): no projector, welders, stock, FINISH or print cycle - the drones are pasted
//  already built, clamped to a host merge block, and wait IDLE (their LAUNCH block, docs/launch-protocol.md) until the cradle lets go.
//
//  BAY STATES (reported as <Tag>.P2S  MyTuple<int bay, string state, long droneId, float charge>, on change + every 2 s)
//   EMPTY     no drone found in the bay's Drone Box (or the merge block is not connected)
//   DOCKED    a drone is clamped (merge IsConnected, its PB / merge block / batteries found by the cell box)
//   RELEASING = CHARGING (drone batteries < Charge Fraction: set to Recharge) -> READY (all gates ok, waiting for its wave turn) -> ARMING (drone [Launch] Custom
//               Data written, 'armed' run on the drone PB, <TAG>_JOIN / FLEET join sent: launch-protocol s6) -> RELEASED (batteries Auto, host merge off, drone merge off;
//               the grid must split within "Fused Grace" 3 s, else FAULT FUSED)
//   COOLDOWN  the split was seen; the host merge block is switched on again after "Cooldown" s (recovery by hand / phase 2); JOIN is repeated for "Join Seconds"
//   FAULT     FUSED, a missing merge block, an exception; leaves after 60 s unless the bay is held ('hold n' / 'unhold n')
//  BURN-LANE GATES (a release waits, with the reason on the screen, until all of them hold; 'force' skips them; "Wait Max" s later the request is dropped):
//   drone charged (Charge Fraction 0.9), the lane in front of the drone box is clear (GetCubeBlock / CubeExists, armour is invisible to GetCubeBlock in the game, on 5 lines x Lane Cells along Away, host blocks only: it cannot see other
//   ships or rocks), the ship turns slower than Max Turn (0.1 rad/s) and accelerates slower than Max Accel (1.0 m/s2) (the drone leaves with the host's velocity).
//  WAVES: one drone is armed at a time and two merge-offs are at least "Wave Gap" (2 s) apart, bay order. A pod-to-pod gap (20 s) is the Staff's job (it sends 'release a-b delay').
//
//  COMMANDS  (PB argument = the same words, or IGC string on "<Tag>.S2P" broadcast / unicast, from the Staff: "release 1")
//   release <n | a-b | all> [delay_s] [force]   recall   hold <n>   unhold <n>   repair <n>   status   reload
//   n is the GLOBAL bay number: "First Bay" + local index - 1 (a second pod sets First Bay=5). 'recall' cancels requests that did not reach the merge-off and switches
//   the host merge blocks of empty / cooling bays on at once.
//  CUSTOM DATA
//   [Pod]  Tag (IGC base; [Bay] Tag, [Ship] Tag, default SHIP: must equal the Staff's Ship Tag), First Bay, Slots (info), Charge Fraction, Wave Gap, Arm Seconds, Fused Grace,
//          Cooldown, Wait Max, Max Turn, Max Accel, Join Seconds, Lane Check, Lane Cells, Match Radius, Fleet Channel, Fleet Secret
//   [Bay n]  Merge (exact name of the host merge block; blank = the one named "...[Bay n]..."), Drone Box Min / Max (host cells x,y,z), Drone Prefix (name start of the drone's
//            blocks, blank = any block not starting with "["), Drone Tag (<TAG>_JOIN, blank = first word of the drone PB's name), Away (x,y,z in the host grid's axes) or Away X / Y / Z,
//            Timer (the hand-launch backup Timer R(n): stopped when the bay releases)
//  REPAIR BAY (v1.1, docs/design/survivability.md item 17, s4.3). A bay with "Dock Forward" / "Dock Up" (the docked drone RC's axes in host grid axes, from pods.py)
//   advertises on "<Drone Tag>_RBAY" every 2 s while EMPTY: MyTuple<long bayPbId, int bay | flags << 8, Vector3D clampPos, Vector3D axis, Vector3D dockFwd, Vector3D dockUp>
//   (flags 1 free, 2 can repair = "Repair" key and welders named "[Bay n]", 4 stock ok = no "[Stock]" cargo or it holds items; clampPos = merge + axis x half a cell,
//   axis = merge WorldMatrix.Up, UNVERIFIED in the game). "<Drone Tag>_RREQ" MyTuple<long bayPbId, int bay, long droneGrid> reserves a free bay for "Reserve Seconds"
//   (90; the same drone asking again renews it), answered on "<Drone Tag>_RACK" MyTuple<long droneGrid, long bayPbId, int bay, int ok>.
//   A drone whose merge locks while its bay is reserved (or after 'repair n' on a docked bay) is a PATIENT: with welders, state REPAIR (welders on, the drone's
//   "Projector [Self]" on; done when the projector shows no block left to place, or has no projection, and every drone terminal block is functional and full
//   integrity, at most "Repair Max" s), then it is released by itself (CHARGING -> READY -> ARMING -> RELEASED, the same gates; a dropped release is retried).
//   Without welders: dock, charge, release. A drone clamped by hand without a reservation stays DOCKED as before (manual recovery).
//  NOT MEASURED IN THE GAME: that the touching enabled merge blocks lock at paste (C1; the script switches the host merge off and on ONCE when it is not connected),
//   which grid's lattice survives a merge (the Drone Box is host cells; if nothing is found in the box the drone is found by distance to the merge block),
//   IGC across a connector (C4: the Staff retries with ConnectedConstructs, then AntennaRelay, then unicast), the grid split time and the real burn.
// =====================================================================

const string VER = "1.1";
const int MaxBays = 8;

MyIni _ini = new MyIni();
string Tag = "SHIP", FleetCh = "FLEET", _note = "", _err = "", _tagKey = "";
int First = 1, LaneCells = 24;
bool LaneCheck = true;
double ResSecs = 90, RepMax = 600, ChargeFrac = 0.9, WaitMax = 60, WaveGap = 2, ArmMin = 0.5, ArmMax = 3, FusedGrace = 3, Cooldown = 30, MaxTurn = 0.1, MaxAccel = 1.0, JoinSecs = 8, MatchR = 8;
double _now, _lastOff = -99, _acc, _kT = -1; long _tick; Vector3D _v0; bool _hasV;

class Bay
{
    public int N, Slots; public string State = "EMPTY", Msg = "", MergeName = "", TimerName = "", Prefix = "", DTag = "HORNET", TagKey = "", BoxRaw = "|";
    public IMyShipMergeBlock Merge, DMerge; public IMyProgrammableBlock DPb; public IMyTimerBlock Timer;
    public List<IMyBatteryBlock> DBats = new List<IMyBatteryBlock>();
    public List<IMyThrust> DThr = new List<IMyThrust>();
    public bool HasBox, Req, Force, Held, Armed, Written, Relatched, Found; public Vector3I BMin, BMax; public Vector3D Away = new Vector3D(0, 0, -1);
    public double T, ReqAt, ReqT0, OffT, JoinUntil, JoinNext, LaneT; public string LaneWhy = "", Sent = ""; public double SentT; public long DroneId; public float Charge; public int Count;
    // repair bay (v1.1)
    public bool HasDock, RepKey, Patient; public Vector3D DockF, DockU; public long ResGrid; public double ResUntil; public int AdFl = -1;
    public List<IMyShipWelder> Weld = new List<IMyShipWelder>(); public IMyProjector DProj;
}
List<Bay> _bays = new List<Bay>();
List<IMyShipController> _ctl = new List<IMyShipController>();
List<IMyProgrammableBlock> _pbs = new List<IMyProgrammableBlock>();
List<IMyShipMergeBlock> _mgs = new List<IMyShipMergeBlock>();
List<IMyBatteryBlock> _bts = new List<IMyBatteryBlock>(); List<IMyThrust> _ths = new List<IMyThrust>();
List<IMyTimerBlock> _tms = new List<IMyTimerBlock>();
List<IMyShipWelder> _wds = new List<IMyShipWelder>(); List<IMyTerminalBlock> _stock = new List<IMyTerminalBlock>(), _tbs = new List<IMyTerminalBlock>(); List<IMyProjector> _prj = new List<IMyProjector>();
Dictionary<string, IMyBroadcastListener> _req = new Dictionary<string, IMyBroadcastListener>();
IMyBroadcastListener _s2p;
StringBuilder _sb = new StringBuilder();

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    try { LoadConfig(); Blocks(); Listen(); } catch (Exception ex) { _err = "setup: " + ex.Message; Echo(_err); }
}

string C(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double C(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool C(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

void LoadConfig()
{
    MyIniParseResult r; if (!_ini.TryParse(Me.CustomData ?? "", out r)) { _err = "Custom Data line " + r.LineNo; return; }
    const string S = "Pod";
    string tg = _ini.Get("Ship", "Tag").ToString(""); if (_ini.Get("Bay", "Tag").ToString("") != "") tg = _ini.Get("Bay", "Tag").ToString("");
    Tag = C(S, "Tag", tg == "" ? Tag : tg).Trim().ToUpperInvariant();
    First = (int)C(S, "First Bay", (double)First); C(S, "Slots", 0.0);
    ChargeFrac = C(S, "Charge Fraction", ChargeFrac); WaveGap = C(S, "Wave Gap", WaveGap); ArmMax = C(S, "Arm Seconds", ArmMax); FusedGrace = C(S, "Fused Grace", FusedGrace);
    Cooldown = C(S, "Cooldown", Cooldown); WaitMax = C(S, "Wait Max", WaitMax); MaxTurn = C(S, "Max Turn", MaxTurn); MaxAccel = C(S, "Max Accel", MaxAccel);
    JoinSecs = C(S, "Join Seconds", JoinSecs); ResSecs = C(S, "Reserve Seconds", ResSecs); RepMax = C(S, "Repair Max", RepMax); LaneCheck = C(S, "Lane Check", LaneCheck); LaneCells = (int)C(S, "Lane Cells", (double)LaneCells); MatchR = C(S, "Match Radius", MatchR);
    string fc = C(S, "Fleet Channel", FleetCh).Trim().ToUpperInvariant(), fs = C(S, "Fleet Secret", "").Trim().ToUpperInvariant();
    FleetCh = fc == "" ? "" : (fs == "" ? fc : fc + "_" + fs);
    C("Hive Mother", "Note", "marker for the drones' LAUNCH block: a PB with this section on its grid means 'still fused to a bay', so a drone that failed to separate never burns the host's thrusters (launch-protocol s6)");
    var nb = new List<Bay>();
    for (int n = 1; n <= MaxBays; n++)
    {
        string sec = "Bay " + n;
        if (!_ini.ContainsSection(sec)) continue;
        Bay b = null; foreach (var o in _bays) if (o.N == First + n - 1) b = o;
        if (b == null) b = new Bay();
        b.N = First + n - 1; b.Slots = n;
        b.MergeName = C(sec, "Merge", ""); b.TimerName = C(sec, "Timer", ""); b.Prefix = C(sec, "Drone Prefix", ""); b.TagKey = C(sec, "Drone Tag", "").Trim().ToUpperInvariant();
        string a = C(sec, "Away", ""); double ax = 0, ay = 0, az = -1;
        if (a != "") { var p = a.Split(','); if (p.Length == 3 && double.TryParse(p[0], out ax) && double.TryParse(p[1], out ay) && double.TryParse(p[2], out az)) { } else { ax = 0; ay = 0; az = -1; } }
        else { ax = C(sec, "Away X", 0.0); ay = C(sec, "Away Y", 0.0); az = C(sec, "Away Z", -1.0); }
        var av = new Vector3D(ax, ay, az); b.Away = av.LengthSquared() < 1e-6 ? new Vector3D(0, 0, -1) : Vector3D.Normalize(av);
        string bmin = C(sec, "Drone Box Min", "").Trim(), bmax = C(sec, "Drone Box Max", "").Trim();
        if (bmin + "|" + bmax != b.BoxRaw) { b.BoxRaw = bmin + "|" + bmax; b.HasBox = ParseBox(b, bmin, bmax); }
        int fx, fy, fz, ux, uy, uz;      // read only (never written back)
        bool dk = V3(_ini.Get(sec, "Dock Forward").ToString(""), out fx, out fy, out fz) & V3(_ini.Get(sec, "Dock Up").ToString(""), out ux, out uy, out uz);
        b.DockF = new Vector3D(fx, fy, fz); b.DockU = new Vector3D(ux, uy, uz);
        b.HasDock = dk && b.DockF.LengthSquared() > 0.5 && b.DockU.LengthSquared() > 0.5; b.RepKey = _ini.Get(sec, "Repair").ToBoolean(true);
        nb.Add(b);
    }
    _bays = nb;
    string o2 = _ini.ToString(); if (o2 != Me.CustomData) Me.CustomData = o2;
}

static bool V3(string s, out int x, out int y, out int z)
{
    x = y = z = 0; var p = (s ?? "").Split(',');
    return p.Length == 3 && int.TryParse(p[0].Trim(), out x) && int.TryParse(p[1].Trim(), out y) && int.TryParse(p[2].Trim(), out z);
}

bool ParseBox(Bay b, string bmin, string bmax)
{
    int x0, y0, z0, x1, y1, z1;
    if (!V3(bmin, out x0, out y0, out z0) || !V3(bmax, out x1, out y1, out z1)) return false;
    b.BMin = new Vector3I(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)); b.BMax = new Vector3I(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1));
    return true;
}

void Blocks()
{
    GridTerminalSystem.GetBlocksOfType(_mgs, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_tms, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_wds, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("[Bay "));
    GridTerminalSystem.GetBlocksOfType(_stock, b => b.IsSameConstructAs(Me) && b.CustomName.Contains("[Stock]"));
    GridTerminalSystem.GetBlocksOfType(_ctl, b => b.CubeGrid == Me.CubeGrid && b.CanControlShip);
    var own = _ctl.FindAll(c => c.CustomName.StartsWith("["));      // the host's own controllers ("[SH] ...", "[POD] ..."), never a clamped drone's
    if (own.Count > 0) _ctl = own;
    foreach (var b in _bays)
    {
        IMyShipMergeBlock m = null;
        foreach (var x in _mgs) if (b.MergeName != "" ? x.CustomName == b.MergeName : x.CustomName.Contains("[Bay " + b.Slots + "]")) { m = x; break; }
        if (m != b.Merge) { b.Merge = m; b.Relatched = false; }
        b.Timer = null; if (b.TimerName != "") foreach (var x in _tms) if (x.CustomName == b.TimerName) { b.Timer = x; break; }
        b.Weld.Clear(); foreach (var x in _wds) if (x.CustomName.Contains("[Bay " + b.Slots + "]")) b.Weld.Add(x);
    }
}

void Listen()
{
    if (_tagKey == Tag) return;
    _tagKey = Tag;
    if (_s2p != null) IGC.DisableBroadcastListener(_s2p);
    _s2p = IGC.RegisterBroadcastListener(Tag + ".S2P"); _s2p.SetMessageCallback("s2p");
    IGC.UnicastListener.SetMessageCallback("s2p");
}

bool Gone(IMyTerminalBlock x)
{
    if (x == null) return true;
    try { return GridTerminalSystem.GetBlockWithId(x.EntityId) == null; } catch (Exception) { return true; }
}

public void Main(string arg, UpdateType src)
{
    double dt = Runtime.TimeSinceLastRun.TotalSeconds; if (dt > 0 && dt < 5) _now += dt;
    try
    {
        Pump();
        if ((src & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script | UpdateType.Mod)) != 0 && !string.IsNullOrWhiteSpace(arg)) Command(arg, false);
        if ((src & UpdateType.Update10) != 0) Tick();
    }
    catch (Exception ex) { _err = "ERR " + ex.Message; Echo(_err); }
}

void Pump()
{
    while (_s2p != null && _s2p.HasPendingMessage) { var m = _s2p.AcceptMessage(); if (m.Data is string) Command((string)m.Data, true); }
    while (IGC.UnicastListener.HasPendingMessage) { var m = IGC.UnicastListener.AcceptMessage(); if (m.Data is string) Command((string)m.Data, true); }
}

void Tick()
{
    _tick++;
    if (_tick % 60 == 0) { Blocks(); Listen(); }
    Kinematics();
    foreach (var b in _bays)
    {
        try { Step(b); }
        catch (Exception ex) { Go(b, "FAULT"); b.Msg = "EXCEPTION " + ex.GetType().Name + ": " + ex.Message; b.Req = false; RepEnd(b); }
    }
    try { RepairIgc(); } catch (Exception ex) { _err = "repair IGC: " + ex.Message; }
    Report(false);
    if (_tick % 3 == 0) Show();
}

// ship turn rate and acceleration from the first controller of the host (never a drone's)
void Kinematics()
{
    foreach (var c in _ctl)
    {
        if (!c.IsFunctional) continue;
        var v = c.GetShipVelocities(); Vector3D lv = v.LinearVelocity;
        _turn = v.AngularVelocity.Length();
        if (_hasV && _now > _kT) _acc = _acc * 0.5 + (lv - _v0).Length() / (_now - _kT) * 0.5;
        _v0 = lv; _kT = _now; _hasV = true; return;
    }
    _turn = 0; _hasV = false; _acc = 0;
}
double _turn;

void Go(Bay b, string s) { if (b.State != s) { b.State = s; b.T = _now; } }

// ---- find the drone: the cell box in host cells; if that finds nothing, the blocks near the merge block ----
bool Mine(Bay b, IMyTerminalBlock x, bool box)
{
    if (x == Me || x.CubeGrid != Me.CubeGrid || x == b.Merge) return false;
    string n = x.CustomName;
    if (n.StartsWith("[Staff] ") || n.StartsWith("[Flight] ")) n = n.Substring(n.IndexOf(' ') + 1);      // a split drone's PBs; "[Thrust] ..." and every other "[" name stay out
    else if (n.StartsWith("[")) return false;
    if (b.Prefix != "" && !n.StartsWith(b.Prefix, StringComparison.OrdinalIgnoreCase)) return false;
    if (box)
    {
        if (!b.HasBox) return false;
        Vector3I p = x.Position;
        return p.X >= b.BMin.X && p.X <= b.BMax.X && p.Y >= b.BMin.Y && p.Y <= b.BMax.Y && p.Z >= b.BMin.Z && p.Z <= b.BMax.Z;
    }
    Vector3D q = x.GetPosition();
    if (Vector3D.Distance(q, b.Merge.GetPosition()) >= MatchR) return false;
    foreach (var o in _bays) if (o != b && o.Merge != null && Vector3D.Distance(q, o.Merge.GetPosition()) < Vector3D.Distance(q, b.Merge.GetPosition())) return false;
    return true;
}

void FindDrone(Bay b)
{
    b.DPb = null; b.DMerge = null; b.DBats.Clear(); b.DThr.Clear();
    if (b.Merge == null) { b.Found = false; return; }
    for (int pass = 0; pass < 2; pass++)
    {
        bool box = pass == 0;
        GridTerminalSystem.GetBlocksOfType(_pbs, x => Mine(b, x, box));
        foreach (var x in _pbs) if (b.DPb == null || (x.CustomName.StartsWith("[Staff]") && !b.DPb.CustomName.StartsWith("[Staff]")) || (x.CustomName.StartsWith("[Flight]") && !b.DPb.CustomName.StartsWith("[Staff]") && !b.DPb.CustomName.StartsWith("[Flight]"))) b.DPb = x;      // Staff first (it holds LAUNCH), then Flight, then a plain PB
        foreach (var x in _mgs) if (Mine(b, x, box)) { b.DMerge = x; break; }
        if (b.DPb == null && b.DMerge == null) continue;
        GridTerminalSystem.GetBlocksOfType(_bts, x => Mine(b, x, box));
        b.DBats.AddRange(_bts);
        GridTerminalSystem.GetBlocksOfType(_ths, x => Mine(b, x, box));
        b.DThr.AddRange(_ths);
        break;
    }
    b.Found = b.DPb != null || b.DMerge != null;
    b.DroneId = b.DPb != null ? b.DPb.EntityId : 0;
    if (b.TagKey != "") b.DTag = b.TagKey;
    else if (b.DPb != null) { string w = b.DPb.CustomName.Split(' ')[0].ToUpperInvariant(); if (w != "" && !w.StartsWith("[")) b.DTag = w; }
    b.Charge = Charge(b);
}

float Charge(Bay b)
{
    float cur = 0, max = 0;
    foreach (var x in b.DBats) { try { cur += x.CurrentStoredPower; max += x.MaxStoredPower; } catch (Exception) { } }
    return max > 0 ? cur / max : 1f;
}

// the drone's blocks are still on the host's grid? (a block that cannot be read counts as gone)
bool Attached(Bay b)
{
    if (b.DPb == null && b.DMerge == null) return false;
    try
    {
        var g = b.Merge.CubeGrid;
        return (b.DPb == null || b.DPb.CubeGrid == g) && (b.DMerge == null || b.DMerge.CubeGrid == g);
    }
    catch (Exception) { return false; }
}

// ---- the lane in front of the drone box: 5 lines of Lane Cells cells along Away, host blocks only ----
string Lane(Bay b)
{
    if (!b.HasBox) return "";
    if (_now - b.LaneT < 0.5) return b.LaneWhy;
    b.LaneT = _now; b.LaneWhy = "";
    var g = Me.CubeGrid;
    double[] a = { b.Away.X, b.Away.Y, b.Away.Z };
    int ax = 0; double best = 0;
    for (int i = 0; i < 3; i++) if (Math.Abs(a[i]) > best) { best = Math.Abs(a[i]); ax = i; }
    int sg = a[ax] < 0 ? -1 : 1;
    int[] mn = { b.BMin.X, b.BMin.Y, b.BMin.Z }, mx = { b.BMax.X, b.BMax.Y, b.BMax.Z };
    int u = (ax + 1) % 3, v = (ax + 2) % 3, f = sg > 0 ? mx[ax] : mn[ax];
    int cu = (mn[u] + mx[u]) / 2, cv = (mn[v] + mx[v]) / 2, du = (mx[u] - mn[u]) / 2, dv = (mx[v] - mn[v]) / 2;
    int[] ou = { 0, du, -du, du, -du }, ov = { 0, dv, dv, -dv, -dv };
    for (int l = 0; l < 5; l++)
        for (int k = 1; k <= LaneCells; k++)
        {
            var c = new int[3]; c[ax] = f + sg * k; c[u] = cu + ou[l]; c[v] = cv + ov[l];
            var q = new Vector3I(c[0], c[1], c[2]);
            if (g.GetCubeBlock(q) != null || g.CubeExists(q)) { b.LaneWhy = c[0] + "," + c[1] + "," + c[2]; return b.LaneWhy; }
        }
    return "";
}

// "" = free to go; else why not (charge is judged separately)
string Gate(Bay b)
{
    if (b.Force) return "";
    if (b.Held) return "held";
    if (LaneCheck) { string l = Lane(b); if (l != "") return "lane blocked at " + l; }
    if (_turn > MaxTurn) return "ship turns " + _turn.ToString("0.00") + " rad/s";
    if (MaxAccel > 0 && _acc > MaxAccel) return "ship accelerates " + _acc.ToString("0.0") + " m/s2";
    return "";
}

// ---- the state machine ----
void Step(Bay b)
{
    double t = _now - b.T;
    if (b.Merge == null || Gone(b.Merge)) { b.Merge = null; Go(b, "FAULT"); b.Msg = "host merge block missing (Merge=" + b.MergeName + ")"; b.Req = false; return; }
    switch (b.State)
    {
        case "EMPTY":
            if (_tick % 6 == 1 || b.Req) FindDrone(b);
            if (b.Found && b.Merge.IsConnected)
            {
                if (b.Patient || Res(b)) { Patient(b); break; }      // a drone that asked for this bay (or a patient back from FAULT)
                Go(b, "DOCKED"); b.Msg = ""; break;
            }
            if (!b.Found && !b.Merge.IsConnected) b.Patient = false;
            if (!b.Merge.IsConnected && !b.Relatched && b.Merge.Enabled && _now > 2) { b.Relatched = true; b.Merge.Enabled = false; b.T = _now; b.Msg = "re-latching the merge block (C1)"; break; }
            if (b.Relatched && !b.Merge.Enabled && t > 0.5) { b.Merge.Enabled = true; b.T = _now; }
            if (b.Req) { b.Req = false; b.Msg = b.Found ? "merge block not connected: nothing to release" : "bay empty: nothing to release"; }
            else if (b.Msg == "" || b.Msg.StartsWith("re-latching")) b.Msg = b.Merge.IsConnected ? "" : "merge open, no drone (C1: did the paste lock?)";
            break;
        case "DOCKED": case "CHARGING": case "READY":
            if (_tick % 30 == 2) Thr(b, false);
            if (_tick % 6 == 1) FindDrone(b);
            if (!b.Found || !b.Merge.IsConnected)
            {
                if (b.Req) b.Msg = "release dropped: the drone is gone";
                Drop(b, "EMPTY"); break;
            }
            b.Charge = Charge(b);
            if (!b.Req && b.Patient && t > 10) Auto(b);      // a patient's release that was dropped (gates) is asked again
            if (!b.Req) { if (b.State != "DOCKED") Drop(b, "DOCKED"); break; }
            if (b.Patient && b.Charge < ChargeFrac) b.ReqT0 = _now;      // charging does not count against Wait Max
            if (_now < b.ReqAt) { b.Msg = "release in " + (b.ReqAt - _now).ToString("0") + " s"; break; }
            if (_now - b.ReqT0 > WaitMax && !b.Force) { b.Msg = "release dropped after " + WaitMax + " s: " + (b.Msg.StartsWith("waiting") ? b.Msg.Substring(9) : b.Msg) + " ('release n force' skips the gates)"; Drop(b, "DOCKED"); break; }
            string why = Gate(b);
            if (why != "") { b.Msg = "waiting: " + why; if (b.State != "DOCKED") Go(b, "DOCKED"); break; }
            if (b.Charge < ChargeFrac && !b.Force)
            {
                foreach (var x in b.DBats) { try { if (x.ChargeMode != ChargeMode.Recharge) x.ChargeMode = ChargeMode.Recharge; } catch (Exception) { } }
                b.Msg = "waiting: charge " + (int)(b.Charge * 100) + " %"; Go(b, "CHARGING"); break;
            }
            Go(b, "READY"); b.Msg = "waiting for the wave";
            foreach (var o in _bays) if (o != b && o.State == "ARMING") return;      // one drone is armed at a time
            Go(b, "ARMING"); b.Armed = false; b.Written = false; b.JoinNext = 0; b.Msg = "";
            break;
        case "REPAIR":
            if (_tick % 30 == 2) Thr(b, false);
            if (_tick % 6 == 1) FindDrone(b);
            if (!b.Found || !b.Merge.IsConnected) { RepEnd(b); Drop(b, "EMPTY"); break; }
            if (_tick % 6 != 4) break;
            Weld(b, true); Proj(b, true);
            int rem = -1; try { if (b.DProj != null && b.DProj.IsProjecting) rem = b.DProj.RemainingBlocks; } catch (Exception) { rem = -1; }
            string bad = Damaged(b);
            if (t > 3 && rem <= 0 && bad == "") { RepEnd(b); Auto(b); b.Msg = "repaired"; break; }
            b.Msg = "repair " + (int)t + " s: " + (rem > 0 ? rem + " blocks to place " : rem < 0 ? "(no self-projection: damaged blocks only) " : "") + bad;
            if (t > RepMax) { RepEnd(b); Auto(b); b.Msg = "repair gave up after " + RepMax + " s: releasing it as it is"; }
            break;
        case "ARMING":
            if (!b.Armed) b.Armed = Arm(b);
            if (b.Armed && t > 0.1) Join(b, false);
            if (t >= ArmMin && (b.Armed || t >= ArmMax) && _now - _lastOff >= WaveGap)
            {
                string w2 = Gate(b);
                if (w2 != "") { b.Msg = "waiting: " + w2; Go(b, "DOCKED"); break; }      // lane / turn changed while arming: back to the gates
                Separate(b);
            }
            break;
        case "RELEASED":
            if (!Attached(b))
            {
                Go(b, "COOLDOWN"); b.Count++; b.Msg = "split " + (_now - b.OffT).ToString("0.0") + " s after merge off"; b.JoinUntil = _now + JoinSecs; b.JoinNext = _now + 1;
                break;
            }
            b.Merge.Enabled = false; if (b.DMerge != null) b.DMerge.Enabled = false;
            b.Msg = "separating... drone still on the host grid";
            if (_now - b.OffT > FusedGrace) { Go(b, "FAULT"); b.Held = true; b.Msg = "FUSED: the drone is still on the host grid " + FusedGrace + " s after merge off - grind its merge stalk by hand ('unhold' to retry)"; }
            break;
        case "COOLDOWN":
            if (_now < b.JoinUntil) Join(b, true);
            if (t > Cooldown) { b.Merge.Enabled = true; Go(b, "EMPTY"); b.Msg = ""; b.DPb = null; b.DMerge = null; b.Found = false; b.DBats.Clear(); }
            break;
        case "FAULT":
            if (!b.Held && t > 60) { Go(b, "EMPTY"); b.Msg = ""; }
            break;
    }
}

void Drop(Bay b, string to)
{
    b.Req = false; b.Force = false; b.Armed = false;
    foreach (var x in b.DBats) { try { if (x.ChargeMode != ChargeMode.Auto) x.ChargeMode = ChargeMode.Auto; } catch (Exception) { } }
    Go(b, to);
}

// the drone's take-off orders, written while its PB is visible through the grid (launch-protocol s6): [Launch] Away (world, away from the host), Armed, then 'armed'
bool Arm(Bay b)
{
    if (b.DPb == null) { b.Msg = "no drone PB visible: relying on the separation and JOIN"; return true; }
    try
    {
        if (!b.Written)
        {
            b.Written = true;
            var ini = new MyIni(); MyIniParseResult r;
            if (ini.TryParse(b.DPb.CustomData ?? "", out r))
            {
                Vector3D d = Vector3D.TransformNormal(b.Away, Me.CubeGrid.WorldMatrix); d.Normalize();
                ini.Set("Launch", "Auto", true); ini.Set("Launch", "Away X", d.X); ini.Set("Launch", "Away Y", d.Y); ini.Set("Launch", "Away Z", d.Z); ini.Set("Launch", "Armed", true);
                b.DPb.CustomData = ini.ToString();
            }
        }
        b.DPb.Enabled = true;
        if (b.DPb.TryRun("armed")) { b.Msg = ""; return true; }
        b.Msg = "drone PB did not run 'armed' (not compiled?): relying on the separation and JOIN";
        return false;
    }
    catch (Exception ex) { b.Msg = "arm failed (" + ex.GetType().Name + "): relying on the separation and JOIN"; b.DPb = null; return true; }
}

void Join(Bay b, bool repeat)
{
    if (repeat) { if (_now < b.JoinNext) return; b.JoinNext = _now + 2; }
    else { if (b.JoinNext > 0) return; b.JoinNext = 1; }
    IGC.SendBroadcastMessage(b.DTag + "_JOIN", "join");
    if (FleetCh != "") IGC.SendBroadcastMessage(FleetCh + "_CMD", "join");
}

// a docked drone burns nothing: its thrusters are off while it is clamped (DOCKED / CHARGING / READY) and come on when it is armed or released
void Thr(Bay b, bool on) { foreach (var x in b.DThr) { try { if (x.Enabled != on) x.Enabled = on; } catch (Exception) { } } }

void Separate(Bay b)
{
    foreach (var x in b.DBats) { try { if (x.ChargeMode != ChargeMode.Auto) x.ChargeMode = ChargeMode.Auto; } catch (Exception) { } }
    Thr(b, true);
    if (b.Timer != null) { try { b.Timer.StopCountdown(); } catch (Exception) { } }
    b.Merge.Enabled = false; if (b.DMerge != null) b.DMerge.Enabled = false;
    b.OffT = _now; _lastOff = _now; b.Req = false; b.Force = false; b.JoinNext = 0; b.Msg = ""; b.Patient = false;
    Go(b, "RELEASED");
}

// ---- repair bay (v1.1): advertise, reserve, repair a patient ----
bool Res(Bay b) { return b.ResGrid != 0 && _now < b.ResUntil; }
bool Free(Bay b, long g)
{
    try { return b.HasDock && b.State == "EMPTY" && !b.Held && !b.Found && b.Merge != null && b.Merge.Enabled && !b.Merge.IsConnected && (!Res(b) || b.ResGrid == g); }
    catch (Exception) { return false; }
}
bool StockOk()
{
    if (_stock.Count == 0) return true;
    foreach (var s in _stock) { try { var i = s.GetInventory(0); if (i == null || i.CurrentVolume.RawValue > 2000) return true; } catch (Exception) { return true; } }
    return false;
}

void RepairIgc()
{
    foreach (var b in _bays) if (b.HasDock && !_req.ContainsKey(b.DTag)) _req[b.DTag] = IGC.RegisterBroadcastListener(b.DTag + "_RREQ");
    foreach (var kv in _req)
        while (kv.Value.HasPendingMessage)
        {
            var m = kv.Value.AcceptMessage();
            if (!(m.Data is MyTuple<long, int, long>)) continue;
            var q = (MyTuple<long, int, long>)m.Data;
            if (q.Item1 != Me.EntityId) continue;
            int ok = 0;
            foreach (var b in _bays) if (b.N == q.Item2 && b.DTag == kv.Key && q.Item3 != 0 && Free(b, q.Item3)) { ok = 1; b.ResGrid = q.Item3; b.ResUntil = _now + ResSecs; }
            IGC.SendBroadcastMessage(kv.Key + "_RACK", new MyTuple<long, long, int, int>(q.Item3, Me.EntityId, q.Item2, ok));
        }
    if (_tick % 12 != 5) return;
    bool st = StockOk();
    foreach (var b in _bays)
    {
        b.AdFl = -1;
        if (!b.HasDock || b.State != "EMPTY" || b.Merge == null) continue;
        try
        {
            b.AdFl = (Free(b, 0) ? 1 : 0) | (b.RepKey && b.Weld.Count > 0 ? 2 : 0) | (st ? 4 : 0);
            MatrixD gm = b.Merge.CubeGrid.WorldMatrix; Vector3D ax = b.Merge.WorldMatrix.Up;
            IGC.SendBroadcastMessage(b.DTag + "_RBAY", new MyTuple<long, int, Vector3D, Vector3D, Vector3D, Vector3D>(Me.EntityId, b.N | b.AdFl << 8,
                b.Merge.GetPosition() + ax * (b.Merge.CubeGrid.GridSize * 0.5), ax, Vector3D.Normalize(Vector3D.TransformNormal(b.DockF, gm)), Vector3D.Normalize(Vector3D.TransformNormal(b.DockU, gm))));
        }
        catch (Exception) { b.AdFl = -1; }
    }
}

// a docked drone becomes a patient: REPAIR with welders, else straight to the release (charge first)
void Patient(Bay b)
{
    b.Patient = true; b.ResGrid = 0; b.Msg = "";
    b.DProj = null; GridTerminalSystem.GetBlocksOfType(_prj, x => Mine(b, x, b.HasBox) && x.CustomName.Contains("Projector [Self]"));
    if (_prj.Count > 0) b.DProj = _prj[0];
    if (b.RepKey && b.Weld.Count > 0) { Go(b, "REPAIR"); Thr(b, false); Weld(b, true); Proj(b, true); }
    else Auto(b);
}
void Auto(Bay b) { Go(b, "DOCKED"); b.Req = true; b.Force = false; b.ReqAt = _now; b.ReqT0 = _now; }
void Weld(Bay b, bool on) { foreach (var x in b.Weld) { try { if (x.Enabled != on) x.Enabled = on; } catch (Exception) { } } }
void Proj(Bay b, bool on) { try { if (b.DProj != null && b.DProj.Enabled != on) b.DProj.Enabled = on; } catch (Exception) { b.DProj = null; } }
void RepEnd(Bay b) { Weld(b, false); Proj(b, false); }

// "" when every drone terminal block is functional and at full integrity (armour cannot be read: see HiveMotherAI v2.4), else the count and the first one
string Damaged(Bay b)
{
    GridTerminalSystem.GetBlocksOfType(_tbs, x => Mine(b, x, b.HasBox));
    int n = 0; string f = "";
    foreach (var x in _tbs)
    {
        bool u;
        try { var s = x.CubeGrid.GetCubeBlock(x.Position); u = !x.IsFunctional || (s != null && !s.IsFullIntegrity); } catch (Exception) { u = true; }
        if (u) { n++; if (f == "") f = x.CustomName; }
    }
    return _tbs.Count == 0 ? "no drone blocks found" : n == 0 ? "" : n + " damaged (" + f + ")";
}

// ---- IGC out ----
void Report(bool all)
{
    foreach (var b in _bays)
    {
        if (!all && b.State == b.Sent && _now - b.SentT < 2) continue;
        b.Sent = b.State; b.SentT = _now;
        var m = new MyTuple<int, string, long, float>(b.N, b.State, b.DroneId, b.Charge);
        IGC.SendBroadcastMessage(Tag + ".P2S", m, TransmissionDistance.ConnectedConstructs);
        IGC.SendBroadcastMessage(Tag + ".P2S", m);       // AntennaRelay: the fallback when ConnectedConstructs does not cross the connector (C4)
    }
}

// ---- commands ----
void Command(string a, bool ig)
{
    a = a.Trim().ToLowerInvariant(); var w = a.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (w.Length == 0) return;
    int n; _hit = 0;
    switch (w[0])
    {
        case "release":
            string sel = w.Length > 1 ? w[1] : "all"; int lo = 0, hi = 999; double delay = 0;
            if (sel != "all") { var p = sel.Split('-'); if (!int.TryParse(p[0], out lo)) { lo = 0; hi = -1; } else hi = p.Length > 1 && int.TryParse(p[1], out n) ? n : lo; }
            for (int i = 2; i < w.Length; i++) { double d; if (double.TryParse(w[i], out d)) delay = d; }
            bool force = a.Contains(" force"); int hit = 0;
            foreach (var b in _bays)
            {
                if (b.N < lo || b.N > hi) continue;
                hit++;
                if (b.State == "EMPTY") FindDrone(b);
                if (b.State == "EMPTY" && b.Found && b.Merge != null && b.Merge.IsConnected) Go(b, "DOCKED");
                if (b.Req) { if (force) b.Force = true; continue; }      // already queued: a repeated command (a retry) changes nothing, except that 'force' is taken over
                if (b.State != "DOCKED" && b.State != "EMPTY") continue;
                if (b.Held) { b.Msg = "held"; continue; }
                b.Req = true; b.Force = force; b.ReqAt = _now + delay; b.ReqT0 = b.ReqAt; b.Msg = delay > 0 ? "release in " + delay.ToString("0") + " s" : "";
            }
            _hit = hit; _note = hit == 0 ? "release " + sel + ": no such bay" : "release " + sel + (force ? " force" : "");
            break;
        case "hold": case "unhold":
            if (w.Length > 1 && int.TryParse(w[1], out n)) foreach (var b in _bays) if (b.N == n)
            {
                _hit++; b.Held = w[0] == "hold"; if (b.Held) { b.Req = false; } else if (b.State == "FAULT") Go(b, "EMPTY");
            }
            _note = a; break;
        case "recall":
            foreach (var b in _bays)
            {
                b.Patient = false; if (b.State == "REPAIR") RepEnd(b);
                if (b.State == "DOCKED" || b.State == "CHARGING" || b.State == "READY" || b.State == "ARMING" || b.State == "REPAIR") Drop(b, "DOCKED");
                else if (b.State == "COOLDOWN" || b.State == "EMPTY") { if (b.Merge != null) b.Merge.Enabled = true; Go(b, "EMPTY"); b.Found = false; }
                b.Msg = "";
            }
            _hit = _bays.Count; _note = "recall: requests cancelled, host merge blocks on"; break;
        case "repair":      // treat a docked drone as a patient: repair (with welders) or charge, then release it
            if (w.Length > 1 && int.TryParse(w[1], out n)) foreach (var b in _bays) if (b.N == n && b.State == "DOCKED" && !b.Req) { _hit++; Patient(b); }
            _note = a; break;
        case "reload": LoadConfig(); Blocks(); Listen(); _err = ""; break;
        case "status": _hit = _bays.Count; _note = "status"; break;
    }
    if (ig || w[0] == "status") Report(true);
    if (ig)
    {   // the ack the Staff waits for: bay 0, "ACK <the command>", droneId = how many bays took it (a pod that has none of the numbers answers 0)
        var ack = new MyTuple<int, string, long, float>(0, "ACK " + a, _hit, 0f);
        IGC.SendBroadcastMessage(Tag + ".P2S", ack, TransmissionDistance.ConnectedConstructs); IGC.SendBroadcastMessage(Tag + ".P2S", ack);
    }
}
int _hit;

// ---- display ----
void Show()
{
    var b = _sb; b.Clear();
    b.Append("CRADLE BAY v").Append(VER).Append("  ").Append(Tag).Append("  turn ").Append(_turn.ToString("0.00")).Append(" acc ").Append(_acc.ToString("0.0")).Append('\n');
    foreach (var y in _bays)
    {
        b.Append("Bay ").Append(y.N).Append(' ').Append(y.State == "CHARGING" || y.State == "READY" || y.State == "ARMING" || y.State == "RELEASED" ? "RELEASING " : "").Append(y.State);
        if (y.Found) b.Append("  ").Append(y.DTag).Append(' ').Append((int)(y.Charge * 100)).Append('%');
        if (y.Held) b.Append(" (held)");
        if (y.Patient) b.Append(" PATIENT");
        if (y.HasDock && y.State == "EMPTY") b.Append(Res(y) ? "  dock reserved " + (int)(y.ResUntil - _now) + " s" : y.AdFl < 0 ? "" : (y.AdFl & 1) != 0 ? "  dock free" : "  dock busy");
        if (y.Count > 0) b.Append(" #").Append(y.Count);
        b.Append('\n');
        if (y.Msg != "") b.Append("   ").Append(y.Msg).Append('\n');
    }
    if (_bays.Count == 0) b.Append("No [Bay n] sections in Custom Data\n");
    if (_note != "") b.Append(_note).Append('\n');
    if (_err != "") b.Append(_err).Append('\n');
    string s = b.ToString();
    Echo(s);
    try { Me.GetSurface(0).WriteText(s); } catch (Exception) { }
}
