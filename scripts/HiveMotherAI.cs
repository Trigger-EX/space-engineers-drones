// =====================================================================
//  HIVE MOTHER  v2.6  - drone printer controller (projector + welders + merge block bays)
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
//  v2.2 CRASH PROOFING: every bay steps inside its own try/catch (one bay's failure puts only that bay in FAULT with the message and the
//  others keep printing); every game-API read that can return null or throw (projector members with no projection, blocks removed from the
//  grid, GetCubeBlock, drone blocks that left the grid) is guarded. BREADCRUMBS: _where names the section being run; on an exception the
//  type, message, section, bay/state and method are shown in the terminal info and on LCD 0, the last 3 errors are kept with timestamps,
//  and the script keeps ticking.
//
//  v2.3 TIMER CHAIN: every generated printer carries three timers per bay ("Timer Build / Release / Reclamp [Bay N]") that run the bay
//  without this script: welders off after the build time, merge block off (the drone leaves on its own LAUNCH block), merge block on +
//  welders on, and round again. Start "Timer Build" once by hand to use it. While ANY of a bay's timers is counting down this script
//  leaves the bay completely alone ("timer cycle running, script paused"); Stop the timers to hand the bay back.
//
//  Keeps "Max Drones" (total) and each tag's "Max Alive" by counting the hive's
//  _SWARM broadcasts. Broadcasts the printer as each tag's home point (_HOME).
//  Optional carrier relay: forwards the turrets' targets to every tag (_TGT).
//
//  v2.4 ARMOUR: in the game GetCubeBlock returns null for plain armour (Sprue Test 2026-10-14), so the FINISH scan never saw the drone's
//  armour and timed out. Cells that CubeExists sees but GetCubeBlock does not are counted as present; their integrity cannot be read, so a
//  complete pass then keeps the welders on for "Armour Settle Seconds" (5 since v2.5) before CHARGE. Merge-pair release CONFIRMED in the game
//  (Sprue Test Merge, 2026-10-14): the welded drone splits off at once when its merge block goes off.
//
//  v2.5 RELEASE ON FINISH: no PB recompile (ApplyAction("Recompile") threw NullReferenceException every tick in the game and stalled CHARGE;
//  a welded PB compiles by itself), Armour Settle Seconds 30 -> 5, CHARGE releases as soon as the drone's
//  batteries are at 95 % (the welded battery keeps the blueprint's charge).
//
//  v2.6 TIGHT CYCLE: release 2 s ("Armour Settle Seconds") after the first scan pass that finds every drone block present and every
//  readable block complete (the last block printed), ARM separates as soon as the drone PB took its orders, Cooldown Seconds 6 -> 1
//  (it still waits until the old drone has left the projection), and the welders stay on from CHARGE to the next BUILD
//  ("Welders Stay On Through Release", not in rack mode).
//
//  COMMANDS: start | stop | max <n> | release [bay] | hold <bay> | unhold <bay> |
//            reload | relay on|off
//  CONFIG: Custom Data ([Hive Mother] and [Bay N] sections, created on first run).
//   [Bay N] keys: Drone Box Min / Drone Box Max ("x,y,z" printer-grid cells),
//   Drone Cell Count, Drone Cell Tolerance, Ignore Cells ("x,y,z;x,y,z"),
//   Skip Finish Check (escape hatch), Launch Burst Seconds.
// =====================================================================
const string VER = "2.6";
const int MaxBays = 12;

int MaxDrones = 8;
bool RackMode = false;
string LaunchTag = "STINGER";
double ChargeSeconds = 25, CooldownSeconds = 1, StallSeconds = 45, BuildTimeout = 600, FaultSeconds = 60;
double NudgeSeconds = 1.5, NudgePower = 0.25, GapSeconds = 1.0, RecompileWait = 3, MatchRadius = 12;
double ArmourSettle = 2, FinishTimeout = 360, FinishFallback = 90, ArmSeconds = 3, JoinSeconds = 40, FusedGrace = 8, LaunchBurst = 4;
string FleetCh = "FLEET";
const int ScanBudget = 150;
int ParallelBuilds = 2;
bool Printing = true, WeldersStayOn = true, DoRecompile = false, Relay = false;
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
    public long ScanIdx; public int PBlocks, PCells, PBad, LBlocks, LCells, LBad, PBlind, LBlind; public double OkSince = -1; public float PInteg, LInteg;
    public List<string> PBadL = new List<string>(); public string LBadText = ""; public double FinishT, Burst = -1;
    // welder kick (one welder at a time)
    public IMyShipWelder KickW; public double KickT; public int KickIdx;
    // take-off
    public bool Armed, LaunchWritten, RackLaunch; public double FusedT;
    public string ProjErr = "";
    // timer chain (Timer Build / Release / Reclamp [Bay N]): while one counts down the script does not touch the bay
    public List<IMyTimerBlock> Timers = new List<IMyTimerBlock>(); public double TimerSeen = -999;
}
class ErrRec { public string Text; public double T, Last; public int N = 1; }
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
// breadcrumbs: _where is set before every risky section; Err() prints it with the exception
string _where = "init", _screen = "";
int _errTotal;
HashSet<IMyTerminalBlock> _dead = new HashSet<IMyTerminalBlock>();
List<ErrRec> _errs = new List<ErrRec>();

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10;      // first, so that a failing setup below still leaves a ticking script that reports it
    _where = "constructor LoadConfig"; try { LoadConfig(); } catch (Exception ex) { Err(ex, null); }
    _where = "constructor GetBlocks"; try { GetBlocks(); } catch (Exception ex) { Err(ex, null); }
    _where = "constructor listeners";
    try { if (RackMode) foreach (var t in TargetTags.Split(',')) if (t.Trim() != "") _lTgt.Add(IGC.RegisterBroadcastListener(t.Trim() + "_TGT")); } catch (Exception ex) { Err(ex, null); }
    try { Show(); Echo(_screen); } catch (Exception) { }
}

// ---- error log with breadcrumbs ----
void Err(Exception ex, Bay b)
{
    string frame = "";
    try
    {
        string st = ex.ToString();
        if (st != null)
        {
            string f = null;
            foreach (var ln in st.Split('\n')) if (ln.IndexOf("Program.", StringComparison.Ordinal) >= 0) { f = ln.Trim(); break; }       // the script's own method that was running
            if (f == null) { int nl = st.IndexOf('\n'); f = (nl > 0 ? st.Substring(0, nl) : st).Trim(); }
            int pr = f.IndexOf('('); if (pr > 0) f = f.Substring(0, pr);
            if (f.StartsWith("at ")) f = f.Substring(3);
            if (f.Length > 50) f = f.Substring(f.Length - 50);
            frame = f;
        }
    }
    catch (Exception) { }
    string text = ex.GetType().FullName + ": " + ex.Message + " | at: " + _where + (b != null ? " | bay " + b.N + " state " + b.State : "") + (frame != "" ? " | in " + frame : "");
    _errTotal++;
    if (_errs.Count > 0 && _errs[_errs.Count - 1].Text == text) { _errs[_errs.Count - 1].N++; _errs[_errs.Count - 1].Last = _now; }
    else { _errs.Add(new ErrRec { Text = text, T = _now, Last = _now }); if (_errs.Count > 3) _errs.RemoveAt(0); }
    try { Echo("ERROR " + text); } catch (Exception) { }
}

string ErrText()
{
    if (_errs.Count == 0) return "";
    var sb = new StringBuilder("ERRORS (" + _errTotal + " total, last 3):\n");
    foreach (var e in _errs) sb.Append(" t=").Append((int)e.T).Append("s").Append(e.N > 1 ? " x" + e.N + " until t=" + (int)e.Last + "s" : "").Append(": ").Append(e.Text).Append('\n');
    return sb.ToString();
}

// a failing bay goes to FAULT with the message; the other bays are not touched
void BayFail(Bay b, Exception ex)
{
    Err(ex, b);
    try
    {
        if (b.State != "FAULT") { b.State = "FAULT"; b.T = _now; b.Faults++; }
        b.Msg = "EXCEPTION " + ex.GetType().FullName + ": " + ex.Message + " (at " + _where + ")";
        b.KickW = null;
    }
    catch (Exception) { }
}

bool Gone(IMyTerminalBlock x)
{
    if (x == null) return true;
    try { return GridTerminalSystem.GetBlockWithId(x.EntityId) == null; } catch (Exception) { return true; }
}

// every switch of a block that may have been removed goes through here; a block that threw is skipped until the next GetBlocks (10 s), so a removed welder costs one exception, not one per tick
bool SetOn(IMyFunctionalBlock x, bool on)
{
    if (x == null || _dead.Contains(x)) return false;
    try { if (x.Enabled != on) x.Enabled = on; return true; }
    catch (Exception ex) { _dead.Add(x); Err(ex, null); return false; }
}

// projector members are read through these: a projector with no projection (or a removed one) may throw or give nonsense
bool Projecting(Bay b) { try { return b.Proj != null && b.Proj.IsProjecting; } catch (Exception ex) { b.ProjErr = ex.GetType().FullName + " reading IsProjecting"; Err(ex, b); return false; } }
int RemainingOf(Bay b) { try { return b.Proj.RemainingBlocks; } catch (Exception ex) { b.ProjErr = ex.GetType().FullName + " reading RemainingBlocks"; Err(ex, b); return -1; } }
int TotalOf(Bay b) { try { return b.Proj.TotalBlocks; } catch (Exception ex) { b.ProjErr = ex.GetType().FullName + " reading TotalBlocks"; Err(ex, b); return -1; } }
int BuildableOf(Bay b) { try { return b.Proj.BuildableBlocksCount; } catch (Exception ex) { b.ProjErr = ex.GetType().FullName + " reading BuildableBlocksCount"; Err(ex, b); return -1; } }
int RemNow(Bay b) { return Projecting(b) ? Math.Max(-1, RemainingOf(b)) : -1; }

void LoadConfig()
{
    MyIniParseResult r; if (!_ini.TryParse(Me.CustomData ?? "", out r)) return;
    const string S = "Hive Mother";
    MaxDrones = (int)C(S, "Max Drones (all tags)", MaxDrones);
    RackMode = C(S, "Rack Mode", RackMode);
    LaunchTag = C(S, "Rack Launch Command Tag", LaunchTag);
    TargetTags = C(S, "Rack Target Tags (enemy sightings that release a drone)", TargetTags);
    ChargeSeconds = C(S, "Max Charge Seconds", ChargeSeconds);
    CooldownSeconds = C(S, "Cooldown Seconds", CooldownSeconds);
    WeldersStayOn = C(S, "Welders Stay On Through Release", WeldersStayOn);
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
    ArmourSettle = C(S, "Armour Settle Seconds", ArmourSettle);
    FinishFallback = C(S, "Finish Fallback Seconds (used only without Drone Box keys)", FinishFallback);
    ArmSeconds = C(S, "Arm Max Seconds", ArmSeconds);
    JoinSeconds = Math.Max(30, C(S, "Join Seconds", JoinSeconds));
    FusedGrace = C(S, "Fused Grace Seconds", FusedGrace);
    LaunchBurst = C(S, "Launch Burst Seconds", LaunchBurst);
    string fch = C(S, "Fleet Channel", FleetCh).Trim();
    string fsec = C(S, "Fleet Secret", "").Trim();
    FleetCh = fch == "" ? "" : (fsec == "" ? fch.ToUpperInvariant() : fch.ToUpperInvariant() + "_" + fsec.ToUpperInvariant());
    Relay = C(S, "Relay Turret Targets To All Tags", Relay);
    Printing = C(S, "Auto Start", Printing);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}
// persist Hold: GetBlocks re-reads the ini every 10 s, so a runtime-only flag would be overwritten
void SetHold(Bay b, bool v)
{
    b.Held = v;
    try { MyIniParseResult r; if (_ini.TryParse(Me.CustomData ?? "", out r)) { _ini.Set("Bay " + b.N, "Hold", v); string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o; } } catch (Exception ex) { Err(ex, b); }
}
string C(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double C(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool C(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

void GetBlocks()
{
    _dead.Clear();
    var old = _bays; var nb = new List<Bay>();      // built aside: a failure half way keeps the previous bay list
    var projs = new List<IMyProjector>(); var welders = new List<IMyShipWelder>(); var merges = new List<IMyShipMergeBlock>(); var timers = new List<IMyTimerBlock>();
    _where = "GetBlocks find bay blocks";
    GridTerminalSystem.GetBlocksOfType(projs, b => b != null && b.IsSameConstructAs(Me) && b.CustomName.Contains("[Bay"));
    GridTerminalSystem.GetBlocksOfType(welders, b => b != null && b.IsSameConstructAs(Me) && b.CustomName.Contains("[Bay"));
    GridTerminalSystem.GetBlocksOfType(merges, b => b != null && b.IsSameConstructAs(Me) && b.CustomName.Contains("[Bay"));
    GridTerminalSystem.GetBlocksOfType(timers, b => b != null && b.IsSameConstructAs(Me) && b.CustomName.Contains("[Bay"));
    _cap.Clear();
    MyIniParseResult r; bool iniOk = _ini.TryParse(Me.CustomData ?? "", out r);
    for (int n = 1; n <= MaxBays; n++)
    {
        string tag = "[Bay " + n + "]"; string sec = "Bay " + n;
        Bay bay = null; foreach (var o in old) if (o.N == n) bay = o;
        if (bay == null) bay = new Bay { N = n };
        _where = "GetBlocks bay " + n + " assign blocks";
        var oldProj = bay.Proj; var oldMerge = bay.Merge;
        bay.Proj = null; bay.Merge = null; bay.Welders.Clear(); bay.Timers.Clear();
        foreach (var p in projs) if (p.CustomName.Contains(tag)) bay.Proj = p;
        foreach (var m in merges) if (m.CustomName.Contains(tag)) bay.Merge = m;
        foreach (var w in welders) if (w.CustomName.Contains(tag)) bay.Welders.Add(w);
        foreach (var tm in timers) if (tm.CustomName.Contains(tag)) bay.Timers.Add(tm);
        if (bay.Proj == null || bay.Merge == null) continue;
        if ((oldProj != null && oldProj != bay.Proj) || (oldMerge != null && oldMerge != bay.Merge)) { bay.DPb = null; bay.DMerge = null; bay.DBats.Clear(); bay.KickW = null; }   // a replaced block: forget what was derived from the old one
        if (bay.KickW != null && !bay.Welders.Contains(bay.KickW)) bay.KickW = null;
        bay.ProjErr = "";
        _where = "GetBlocks bay " + n + " config";
        if (iniOk) try
        {
            bay.Tag = C(sec, "Tag", bay.Tag); bay.Prefix = C(sec, "Prefix", bay.Prefix); bay.Cap = (int)C(sec, "Max Alive", bay.Cap); bay.MaxPrints = (int)C(sec, "Max Prints", bay.MaxPrints);
            bool held = C(sec, "Hold", bay.Held); bay.Held = held;
            string exl = C(sec, "Exit Direction", bay.Exit);   // blank = straight away from the merge block
            bay.Exit = exl;
            bay.SkipFinish = C(sec, "Skip Finish Check", false);
            bay.Burst = C(sec, "Launch Burst Seconds", LaunchBurst);
            bay.Cells = (int)C(sec, "Drone Cell Count", 0.0);
            bay.Tol = (int)C(sec, "Drone Cell Tolerance", 2.0);
            string bmin = C(sec, "Drone Box Min", "").Trim(), bmax = C(sec, "Drone Box Max", "").Trim(), ign = C(sec, "Ignore Cells", "").Trim();
            ParseBox(bay, bmin, bmax, ign);
        }
        catch (Exception ex) { Err(ex, bay); bay.CfgWarn = "config error: " + ex.Message; }
        nb.Add(bay);
        int c; if (!_cap.TryGetValue(bay.Tag, out c) || bay.Cap > c) _cap[bay.Tag] = bay.Cap;
        if (!_lSwarm.ContainsKey(bay.Tag) || !_alive.ContainsKey(bay.Tag))
        {
            _lSwarm[bay.Tag] = IGC.RegisterBroadcastListener(bay.Tag + "_SWARM");
            _alive[bay.Tag] = new Dictionary<long, double>();
            _joinUntil[bay.Tag] = 0; _joinNext[bay.Tag] = 0;
        }
    }
    _bays = nb;
    _where = "GetBlocks save config";
    if (iniOk) { string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o; }
    _where = "GetBlocks KeepProjection";
    foreach (var b2 in _bays) KeepProjection(b2);
    _where = "GetBlocks stock/thrusters";
    GridTerminalSystem.GetBlocksOfType(_stock, b => b != null && b.CustomName.Contains("[Stock]") && b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_thr, b => b != null && b.IsSameConstructAs(Me) && b.CubeGrid == Me.CubeGrid);
    if (Relay)
    {
        _where = "GetBlocks turrets";
        GridTerminalSystem.GetBlocksOfType(_turrets, b => b != null && b.IsSameConstructAs(Me));
        GridTerminalSystem.GetBlocksOfType(_ctcs, b => b != null && b.IsSameConstructAs(Me));
    }
    _where = "GetBlocks LCD surface";
    IMyTextSurface sf = null;
    try { if (Me.SurfaceCount > 0) sf = Me.GetSurface(0); } catch (Exception ex) { Err(ex, null); }
    if (sf != null) { _lcd = sf; try { _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.7f; } catch (Exception ex) { Err(ex, null); } }
}

// Keep Projection must stay on, or the projector clears the blueprint as soon as the last block is placed
void KeepProjection(Bay b)
{
    if (b == null || b.Proj == null) return;
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
    catch (Exception ex) { Err(ex, null); }
    try { if (_errs.Count > 0 && _screen.IndexOf("ERRORS") < 0) Show(); } catch (Exception) { }
    try { Echo(_screen); } catch (Exception) { }          // every run, so that the terminal info never goes blank between redraws
}

void Sect(string name, Action a) { _where = name; try { a(); } catch (Exception ex) { Err(ex, null); } }

void Tick(string arg, UpdateType src)
{
    if (!string.IsNullOrWhiteSpace(arg)) Sect("command '" + arg + "'", () => Command(arg.Trim().ToLowerInvariant()));
    if ((src & UpdateType.Update10) == 0) return;
    double dt = Runtime.TimeSinceLastRun.TotalSeconds; if (dt <= 0 || dt > 2) dt = 1 / 6.0;
    _now += dt; _dt = dt; _tick++;
    if (_tick % 60 == 0) Sect("GetBlocks", GetBlocks);
    Sect("IGC swarm messages", ReadSwarm);
    Sect("IGC target messages", ReadTargets);
    Sect("expire drones/targets", Expire);
    Sect("IGC home broadcast", () => { if (_tick % 6 == 0) { foreach (var tag in _lSwarm.Keys) IGC.SendBroadcastMessage(tag + "_HOME", Me.CubeGrid.WorldVolume.Center); } });
    if (Relay && _tick % 2 == 0) Sect("relay targets", RelayTargets);
    Sect("send joins", SendJoins);
    int building = 0, docked = 0;
    foreach (var b in _bays) { if (b.State == "BUILD" || b.State == "FINISH" || b.State == "CHARGE") building++; if (b.State == "DOCKED") docked++; }
    var bays = _bays;       // (GetBlocks may replace the list; this tick works on the one it started with)
    foreach (var b in bays)
    {
        _where = "bay " + b.N + " " + b.State;
        try { StepBay(b, ref building, ref docked); }
        catch (Exception ex) { BayFail(b, ex); }
    }
    try { _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount); } catch (Exception) { }
    if (_tick % 3 == 0) Sect("display", Show);
}

void ReadSwarm()
{
    foreach (var kv in _lSwarm)
    {
        Dictionary<long, double> dict; if (kv.Value == null || !_alive.TryGetValue(kv.Key, out dict)) continue;
        while (kv.Value.HasPendingMessage)
        {
            var m = kv.Value.AcceptMessage();
            if (m.Data is MyTuple<long, Vector3D, Vector3D, long, int>)
            {
                long id = ((MyTuple<long, Vector3D, Vector3D, long, int>)m.Data).Item1;
                if (!dict.ContainsKey(id)) { _joinNext[kv.Key] = 0; double ju; _joinUntil.TryGetValue(kv.Key, out ju); _joinUntil[kv.Key] = Math.Max(ju, _now + 6); }   // a new drone appeared: say join again
                dict[id] = _now;
            }
        }
    }
}

void ReadTargets()
{
    foreach (var l in _lTgt)
    {
        if (l == null) continue;
        while (l.HasPendingMessage)
        {
            var m = l.AcceptMessage();
            if (!(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
            if (d.Item4 < 0) _targets.Remove(d.Item1); else _targets[d.Item1] = _now;
        }
    }
}

void Expire()
{
    var drop = new List<long>();
    foreach (var dict in _alive.Values)
    {
        drop.Clear(); foreach (var kv in dict) if (_now - kv.Value > 6) drop.Add(kv.Key);
        foreach (var id in drop) dict.Remove(id);
    }
    drop.Clear(); foreach (var kv in _targets) if (_now - kv.Value > 6) drop.Add(kv.Key);
    foreach (var id in drop) _targets.Remove(id);
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
        double ju, jn; if (!_joinUntil.TryGetValue(tag, out ju) || !_joinNext.TryGetValue(tag, out jn)) continue;
        if (_now >= ju || _now < jn) continue;
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
    if (b.Proj == null || b.Merge == null) { Fault(b, "bay blocks missing (projector or merge block)"); return; }
    if (_tick % 30 == 7)
    {   // blocks destroyed or removed since GetBlocks: do not touch them, rebuild the bay list now
        _where = "bay " + b.N + " " + b.State + " check blocks still exist";
        if (Gone(b.Proj) || Gone(b.Merge)) { Fault(b, "projector or merge block was removed/destroyed"); GetBlocks(); return; }
        b.Welders.RemoveAll(w => Gone(w));
        if (b.KickW != null && !b.Welders.Contains(b.KickW)) b.KickW = null;
    }
    _where = "bay " + b.N + " " + b.State + " timer chain";
    if (TimerChainRunning(b))
    {   // the player's timers own this bay: no power switching, no merge switching, no state changes
        b.Msg = "timer cycle running - script paused (stop the bay's timers to resume)";
        return;
    }
    if (b.Msg.StartsWith("timer cycle running"))
    {   // handed back: whatever the timers left behind (welders, merge block) is re-evaluated from scratch; a finished drone still in the bay is recognised by BUILD -> FINISH
        b.Msg = ""; if (b.State != "FAULT") { b.State = "IDLE"; b.T = _now; b.DPb = null; b.DMerge = null; b.DBats.Clear(); b.KickW = null; }
    }
    switch (b.State)
    {
        case "IDLE":
            if (building < ParallelBuilds && Wants(b, docked)) { StartBuild(b); building++; Power(b, true); }
            else Power(b, false);
            break;
        case "BUILD":
            Power(b, true);
            _where = "bay " + b.N + " BUILD projector read";
            int rem = RemNow(b);
            if (rem != b.Rem) { b.Rem = rem; b.LastProgress = _now; }
            if (rem > 0) b.SawWork = true;
            // rem == 0 for a while without ever seeing work: a half-built drone from a faulted cycle is still in the bay
            if ((b.SawWork || t > 15) && rem == 0) { EnterFinish(b); break; }
            _where = "bay " + b.N + " BUILD diagnose";
            b.Msg = Diagnose(b);
            _where = "bay " + b.N + " BUILD stall";
            Stall(b);
            if (b.Starved) b.T += _dt;
            else if (t > BuildTimeout) Fault(b, "build timeout: " + (rem >= 0 ? rem + " of " + TotalOf(b) + " blocks not placed" : "no projection") + (b.Msg != "" ? " (" + b.Msg + ")" : ""));
            break;
        case "FINISH":
            Power(b, true);        // welders stay on until the drone is complete
            _where = "bay " + b.N + " FINISH check";
            StepFinish(b, t);
            break;
        case "CHARGE":
            Power(b, StayOn());
            _where = "bay " + b.N + " CHARGE find drone";
            FindDrone(b);
            _where = "bay " + b.N + " CHARGE recompile drone PB";
            if (DoRecompile && !b.Recompiled && b.DPb != null) { b.Recompiled = true; try { b.DPb.Enabled = true; b.DPb.ApplyAction("Recompile");   /* a terminal button, not an action: probably a no-op, the welded PB compiles itself; ARM's TryRun is the real test */ } catch (Exception ex) { Err(ex, b); b.Msg = "drone PB recompile failed: " + ex.GetType().FullName; } b.Recompiled = true; b.T = _now; t = 0; }
            if (b.DMerge == null && t > 8) { Fault(b, "no drone merge block found (prefix " + b.Prefix + ")"); break; }
            _where = "bay " + b.N + " CHARGE batteries";
            bool ready = t > (b.Recompiled ? RecompileWait : 0) && DroneCharged(b);   // the welded battery keeps the blueprint's charge: usually ready at once
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
            Power(b, StayOn());
            _where = "bay " + b.N + " ARM drone PB";
            if (!b.Armed) b.Armed = Arm(b);
            if (b.Armed || t >= ArmSeconds) Separate(b);      // TryRun is synchronous: an armed drone has already read its orders
            break;
        case "RELEASE":
            _where = "bay " + b.N + " RELEASE nudge";
            Nudge(b, t);
            if (t >= GapSeconds + NudgeSeconds)
            {
                _where = "bay " + b.N + " RELEASE check separation";
                if (StillAttached(b))
                {   // grid split can lag a few ticks behind the merge block switching off: re-assert, wait, only then fault
                    if (b.FusedT <= 0) b.FusedT = _now;
                    SetOn(b.Merge, false); SetOn(b.DMerge, false);
                    b.Msg = "separating... drone still on the printer grid";
                    if (_now - b.FusedT > FusedGrace) { Fault(b, "FUSED: drone is still on the printer grid " + FusedGrace + " s after merge off - grind the drone's merge stalk by hand; see docs"); SetHold(b, true); }
                    break;
                }
                b.FusedT = 0; b.Msg = "";
                b.State = "COOLDOWN"; b.T = _now;
                _joinUntil[b.Tag] = Math.Max(_joinUntil[b.Tag], _now + JoinSeconds); _joinNext[b.Tag] = 0;
                if (b.RackLaunch) _rackLaunchLeft = 3;
            }
            break;
        case "COOLDOWN":
            _where = "bay " + b.N + " COOLDOWN projector read";
            bool cleared = RemNow(b) > 0;
            if (t > CooldownSeconds && (cleared || t > CooldownSeconds * 4)) { SetOn(b.Merge, true); b.State = "IDLE"; b.T = _now; b.DPb = null; b.DMerge = null; if (_launched > 0 && _targets.Count == 0) _launched = 0; }
            break;
        case "FAULT":
            Power(b, false);
            if (!b.Held && t > FaultSeconds) { b.State = "IDLE"; b.T = _now; }
            break;
    }
}

// true while any of the bay's timers counts down (plus 3 s: a chain hands over from one timer to the next inside one tick)
bool TimerChainRunning(Bay b)
{
    if (b.Timers.Count == 0) return false;
    foreach (var tm in b.Timers) { try { if (tm.IsCountingDown) b.TimerSeen = _now; } catch (Exception) { } }
    return _now - b.TimerSeen < 3;
}

void StartBuild(Bay b)
{
    b.State = "BUILD"; b.T = _now; b.Rem = -1; b.LastProgress = _now; b.SawWork = false; SetOn(b.Merge, true); b.Msg = "";
    b.Nudged = false; b.DPb = null; b.DMerge = null; b.DBats.Clear(); b.KickW = null; b.Starved = false; b.Armed = false; b.LaunchWritten = false; b.Recompiled = false; b.ProjErr = "";
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
    ResetScan(b); b.HavePass = false; b.LBad = 0; b.LBlocks = 0; b.LCells = 0; b.LBlind = 0; b.LInteg = -1; b.LBadText = ""; b.OkSince = -1;
}

void ResetScan(Bay b)
{
    b.ScanIdx = 0; b.Seen.Clear(); b.PBlocks = b.PCells = b.PBad = b.PBlind = 0; b.PInteg = 0; b.PBadL.Clear(); b.PassOk = false;
}

// FINISH: the projector only counts PLACED blocks. A placed block is a skeleton until the welders have put every component in.
void StepFinish(Bay b, double t)
{
    int rem = RemNow(b);
    bool done = false; string note = "";
    if (b.SkipFinish) { done = true; note = "finish check skipped (Skip Finish Check)"; }
    else
    {
        if (b.HasBox) { _where = "bay " + b.N + " FINISH scan box"; done = ScanBox(b, rem) && rem <= 0; }
        // (FallbackCheck gets the real time since FINISH began: t is held back while Starved, which froze this timer for ever)
        else { _where = "bay " + b.N + " FINISH name check"; done = FallbackCheck(b, _now - b.FinishT) && rem <= 0; note = "no Drone Box keys: block-name check + " + (int)FinishFallback + " s timer, armour not verified" + (b.CfgWarn != "" ? " (" + b.CfgWarn + ")" : ""); }
        if (rem > 0) note = rem + " blocks not placed yet. " + note;
    }
    if (done) { b.State = "CHARGE"; b.T = _now; b.Recompiled = false; b.Msg = ""; return; }
    _where = "bay " + b.N + " FINISH diagnose";
    string d = Diagnose(b);
    b.Msg = (d != "" ? d + "\n   " : "") + (b.LBadText != "" ? b.LBadText + "\n   " : "") + note;
    Stall(b);
    if (b.Starved) b.T += _dt;
    else if (t > FinishTimeout)
        Fault(b, "finish timeout: " + (b.HavePass ? b.LBad + " unfinished, " + b.LBlocks + "/" + b.Cells + " blocks. " + b.LBadText : "scan never completed") + (rem > 0 ? " " + rem + " blocks never placed (unsupported?)" : ""));
}

// incremental walk over the drone's cell box; returns true when a complete pass found everything welded
bool ScanBox(Bay b, int rem)
{
    IMyCubeGrid g = null;
    try { g = b.Merge.CubeGrid; } catch (Exception ex) { Err(ex, b); }
    if (g == null) { b.LBadText = "printer grid not readable (merge block has no grid)"; return false; }
    int sx = b.BMax.X - b.BMin.X + 1, sy = b.BMax.Y - b.BMin.Y + 1, sz = b.BMax.Z - b.BMin.Z + 1;
    long total = (long)sx * sy * sz;
    if (b.ScanIdx == 0) { b.Seen.Clear(); b.PassOk = rem <= 0; }       // a pass that started while blocks were still unplaced proves nothing
    for (int n = 0; n < ScanBudget && b.ScanIdx < total; n++)
    {
        long i = b.ScanIdx++;
        int x = b.BMin.X + (int)(i % sx), y = b.BMin.Y + (int)((i / sx) % sy), z = b.BMin.Z + (int)(i / ((long)sx * sy));
        if (b.Ignore.Contains(Key(x, y, z))) continue;
        try
        {
            var s = g.GetCubeBlock(new Vector3I(x, y, z));        // null for an empty cell, AND for plain armour (seen in the game, Sprue Test 2026-10-14)
            if (s == null)
            {   // armour: CubeExists still sees it, its integrity cannot be read; counted as present, the pass then waits Armour Settle Seconds with the welders on
                if (g.CubeExists(new Vector3I(x, y, z)) && b.Seen.Add(Key(x, y, z))) { b.PCells++; b.PBlocks++; b.PBlind++; }
                continue;
            }
            var fb = s.FatBlock;                                  // null for armour: only terminal/functional blocks have one
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
                        foreach (var w in b.Welders) { try { bd = Math.Min(bd, Vector3D.Distance(wp, w.GetPosition())); } catch (Exception) { } }
                        e += " (" + Math.Round(bd, 1) + " m from nearest welder)";
                    }
                    b.PBadL.Add(e);
                }
            }
        }
        catch (Exception ex)
        {   // one unreadable cell must not stop the scan; it counts as unfinished so the drone is not released on a guess
            Err(ex, b); b.PBad++;
            if (b.PBadL.Count < 3) b.PBadL.Add("cell " + x + "," + y + "," + z + " unreadable (" + ex.GetType().FullName + ")");
        }
    }
    if (b.ScanIdx < total) return false;
    // pass finished
    bool prog = !b.HavePass || b.PBad < b.LBad || b.PInteg > b.LInteg + 0.5f || b.PBlocks > b.LBlocks;
    if (prog) b.LastProgress = _now;
    b.LBad = b.PBad; b.LBlocks = b.PBlocks; b.LCells = b.PCells; b.LBlind = b.PBlind; b.LInteg = b.PInteg; b.HavePass = true;
    bool ok = b.PassOk && b.PBad == 0 && b.PBlocks >= b.Cells - b.Tol;
    if (!ok) b.OkSince = -1;
    else if (b.PBlind > 0)
    {   // armour integrity is unreadable: keep welding for Armour Settle Seconds after the first complete pass
        if (b.OkSince < 0) b.OkSince = _now;
        if (_now - b.OkSince < ArmourSettle) { ok = false; b.LBadText = "all blocks present (" + b.PBlind + " armour cells unreadable): welding on for " + (int)(ArmourSettle - (_now - b.OkSince)) + " s more"; ResetScan(b); return false; }
    }
    if (b.PBad > 0) b.LBadText = b.PBad + " unfinished: " + string.Join("; ", b.PBadL.ToArray());
    else if (b.PBlocks < b.Cells - b.Tol) b.LBadText = "only " + b.PBlocks + " of " + b.Cells + " drone blocks present";
    else b.LBadText = "";
    ResetScan(b);
    return ok;
}

// no cell box configured: terminal blocks named like the drone, plus a conservative minimum time
bool FallbackCheck(Bay b, double t)
{
    if (_tick % 6 != 0 && b.HavePass) return b.HavePass && b.LBad == 0 && b.LBlocks > 0 && t >= FinishFallback;
    GridTerminalSystem.GetBlocksOfType(_tmp, x => x != null && x.CustomName.StartsWith(b.Prefix) && !x.CustomName.Contains("[Bay") && x.IsSameConstructAs(Me));
    int bad = 0, n = 0; float integ = 0; var bl = new List<string>();
    foreach (var x in _tmp)
    {
        n++; bool u;
        try
        {
            var gr = x.CubeGrid; var s = gr != null ? gr.GetCubeBlock(x.Position) : null;      // null: block just removed
            u = !x.IsFunctional || (s != null && !s.IsFullIntegrity);
            if (s != null) integ += s.BuildIntegrity;
        }
        catch (Exception ex) { Err(ex, b); u = true; }
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
        try
        {
            if (!w.IsFunctional) { broken++; why = "damaged"; }
            else if (!w.Enabled) { off++; why = "off"; }
            else if (!w.IsWorking) { nopower++; why = "no power"; }
            if (why != null && first.Length < 60) first += (first == "" ? "" : ", ") + ShortName(w) + " " + why;
        }
        catch (Exception) { broken++; }
    }
    if (off + broken + nopower == 0) return "";
    return "welders: " + off + " off, " + broken + " damaged, " + nopower + " unpowered (" + first + ")";
}
string ShortName(IMyTerminalBlock x) { string n = x.CustomName; int i = n.IndexOf(']'); return (i >= 0 ? n.Substring(i + 1) : n).Trim(); }

string Diagnose(Bay b)
{
    b.Starved = false;
    if (!Projecting(b)) return "no projection (projector off or blueprint missing)" + (b.ProjErr != "" ? " [" + b.ProjErr + "]" : "");
    if (_now - b.LastProgress <= StallSeconds) return "";
    if (StockEmpty()) { b.Starved = true; return "STALLED: component stock empty - refill the [Stock] cargo (timeouts paused)"; }
    string wr = WelderReport(b);
    if (wr != "") return "STALLED: " + wr;
    if (b.State == "FINISH") return "STALLED: projection done but blocks are not getting finished: out of weld reach or missing components";
    if (b.Rem > 0 && b.Rem == TotalOf(b)) return "STALLED at 0 built: first blocks not supported / merge block not aligned?";
    int bc = BuildableOf(b);
    if (b.Rem > 0 && bc == 0) return "STALLED: " + b.Rem + " blocks left, none buildable - unsupported (nothing to attach to) or out of weld reach";
    return "STALLED: " + b.Rem + " blocks left (" + bc + " buildable) - out of weld reach or missing components";
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
        bool fn = false; try { fn = w.IsFunctional; } catch (Exception) { }
        if (!fn) continue;
        b.KickIdx = (b.KickIdx + i + 1) % n; b.KickW = w; b.KickT = _now; SetOn(w, false); return;
    }
}

bool StockEmpty()
{
    if (_stock.Count == 0) return false;      // creative world / no stock tracking
    foreach (var s in _stock)
    {
        try
        {
            var inv = s.GetInventory(0);
            if (inv != null && inv.CurrentVolume.RawValue > 2000) return false;     // more than 0.002 m3 of items
        }
        catch (Exception) { return false; }       // unreadable stock: do not claim it is empty (that would pause the timeouts)
    }
    return true;
}

// Welders are only switched OFF outside BUILD/FINISH. In those two states "off" requests are ignored,
// and every welder (except the one being kicked for 2 s) is forced back on each tick.
// v2.6: welders stay on from CHARGE to the next BUILD, so the next drone is welded the moment the old one clears the projection (not in rack mode)
bool StayOn() { return WeldersStayOn && !RackMode; }
void Power(Bay b, bool on)
{
    _where = "bay " + b.N + " " + b.State + " Power projector";
    SetOn(b.Proj, true);      // projection stays up between drones (Keep Projection)
    if (!on && (b.State == "BUILD" || b.State == "FINISH")) return;
    _where = "bay " + b.N + " " + b.State + " Power welders";
    if (!on) b.KickW = null;
    else if (b.KickW != null && _now - b.KickT >= 2) { SetOn(b.KickW, true); b.KickW = null; }
    foreach (var w in b.Welders) { if (on && w == b.KickW) continue; SetOn(w, on); }
}

// drone parts belong to the bay whose merge block is nearest
Bay Nearest(Vector3D p)
{
    Bay best = null; double bd = MatchRadius;
    foreach (var b in _bays)
    {
        if (b.Merge == null) continue;
        try { double d = Vector3D.Distance(p, b.Merge.GetPosition()); if (d < bd) { bd = d; best = b; } } catch (Exception) { }
    }
    return best;
}

void FindDrone(Bay b)
{
    if (b.DPb != null && b.DMerge != null) return;
    Vector3D mp;
    try { mp = b.Merge.GetPosition(); } catch (Exception ex) { Err(ex, b); return; }
    GridTerminalSystem.GetBlocksOfType(_mergesAll, x => x != null && x.CustomName.StartsWith(b.Prefix) && !x.CustomName.Contains("[Bay"));
    b.DMerge = null; double bd = 1e9;
    foreach (var m in _mergesAll) { try { Vector3D q = m.GetPosition(); double d = Vector3D.Distance(q, mp); if (d < bd && d < MatchRadius && Nearest(q) == b) { bd = d; b.DMerge = m; } } catch (Exception) { } }
    // a split drone's PBs are "[Staff] <Prefix> PB" (holds LAUNCH and the [Launch] keys), "[Flight] <Prefix> PB" and "[Thrust] <Prefix> PB" (never armed): prefer Staff, then Flight, then a plain "<Prefix> PB"; the nearest to the merge block within a rank
    GridTerminalSystem.GetBlocksOfType(_pbs, x => x != null && x != Me && !x.CustomName.Contains("[Bay") && (x.CustomName.StartsWith(b.Prefix) || x.CustomName.StartsWith("[Staff] " + b.Prefix) || x.CustomName.StartsWith("[Flight] " + b.Prefix)));
    b.DPb = null; bd = 1e9; int br = 9;
    foreach (var p in _pbs) { try { Vector3D q = p.GetPosition(); double d = Vector3D.Distance(q, mp); int r = p.CustomName.StartsWith("[Staff]") ? 0 : p.CustomName.StartsWith("[Flight]") ? 1 : 2; if ((r < br || (r == br && d < bd)) && Nearest(q) == b) { br = r; bd = d; b.DPb = p; } } catch (Exception) { } }
    GridTerminalSystem.GetBlocksOfType(_bats, x => x != null && x.CustomName.StartsWith(b.Prefix) && !x.CustomName.Contains("[Bay"));
    b.DBats.Clear();
    foreach (var x in _bats) { try { if (Nearest(x.GetPosition()) == b) b.DBats.Add(x); } catch (Exception) { } }
}

bool DroneCharged(Bay b)
{
    if (b.DBats.Count == 0) return true;
    foreach (var x in b.DBats) { try { if (x.CurrentStoredPower < x.MaxStoredPower * 0.95f) return false; } catch (Exception) { } }
    return true;
}

void Release(Bay b)
{
    Power(b, StayOn());
    FindDrone(b);
    b.Away = Vector3D.Zero;
    if (b.DMerge != null)
    {
        try { Vector3D d = b.DMerge.GetPosition() - b.Merge.GetPosition(); if (d.LengthSquared() > 1e-6) b.Away = -Vector3D.Normalize(d); }
        catch (Exception ex) { Err(ex, b); b.DMerge = null; }
    }
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
    if (b.DPb == null && b.DMerge == null) return false;
    // a drone block that was removed/destroyed (or left the grid terminal system) cannot be read: that counts as "not attached"
    try
    {
        var g = b.Merge.CubeGrid; if (g == null) return false;
        return (b.DPb == null || b.DPb.CubeGrid == g) && (b.DMerge == null || b.DMerge.CubeGrid == g);
    }
    catch (Exception ex) { Err(ex, b); return false; }
}

// take-off orders, written while the drone PB can still be seen through GridTerminalSystem
bool Arm(Bay b)
{
    FindDrone(b);
    if (b.DPb == null) { b.Msg = "ARM: drone PB not visible - relying on the JOIN broadcast"; return false; }
    try
    {
        if (!b.LaunchWritten)
        {
            b.LaunchWritten = true;
            _where = "bay " + b.N + " ARM write drone Custom Data";
            var ini = new MyIni(); MyIniParseResult r;
            if (ini.TryParse(b.DPb.CustomData ?? "", out r))
            {
                ini.Set("Launch", "Auto", !RackMode);                 // rack drones wait for the launch command instead
                ini.Set("Launch", "Burst Seconds", b.Burst >= 0 ? b.Burst : LaunchBurst);
                Vector3D da = -b.Away;      // b.Away is the PRINTER's push direction (towards the printer); the drone must burn the other way
                ini.Set("Launch", "Away X", da.X); ini.Set("Launch", "Away Y", da.Y); ini.Set("Launch", "Away Z", da.Z);
                ini.Set("Launch", "Exit Direction", b.Exit == null ? "" : b.Exit.Trim());
                ini.Set("Launch", "Armed", true);
                b.DPb.CustomData = ini.ToString();
            }
            else b.Msg = "ARM: drone Custom Data unreadable - relying on the JOIN broadcast";
        }
        _where = "bay " + b.N + " ARM run drone PB";
        b.DPb.Enabled = true;
        bool ok = b.DPb.TryRun("armed");
        if (!ok) b.Msg = "ARM: drone PB did not run (TryRun false: not compiled or not working yet?) - relying on the JOIN broadcast";
        else b.Msg = "";
        return ok;
    }
    catch (Exception ex)
    {   // arming is best effort: the release must still happen (JOIN / auto take-off cover it)
        Err(ex, b); b.Msg = "ARM failed (" + ex.GetType().FullName + ") - relying on the JOIN broadcast"; b.DPb = null; return false;
    }
}

// merge blocks off: the drone leaves
void Separate(Bay b)
{
    _where = "bay " + b.N + " Separate batteries";
    foreach (var x in b.DBats) { try { if (x.ChargeMode != ChargeMode.Auto) x.ChargeMode = ChargeMode.Auto; } catch (Exception ex) { Err(ex, b); } }   // a drone left on Recharge never uses its batteries
    _where = "bay " + b.N + " Separate merge blocks off";
    SetOn(b.Merge, false);
    SetOn(b.DMerge, false);
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
        try
        {
            if (th.CustomName.StartsWith(b.Prefix)) continue;                   // never the drone's own thrusters
            if (Vector3D.Dot(th.WorldMatrix.Backward, b.Away) > 0.85) th.ThrustOverridePercentage = on ? (float)NudgePower : 0f;
        }
        catch (Exception) { }       // a thruster that was shot off since GetBlocks
    }
    b.Nudged = on;
}

void RelayTargets()
{
    foreach (var t in _turrets) { try { if (t.IsFunctional && t.HasTarget) SendTarget(t.GetTargetedEntity()); } catch (Exception) { } }
    foreach (var c in _ctcs) { try { if (c.IsFunctional && c.HasTarget) SendTarget(c.GetTargetedEntity()); } catch (Exception) { } }
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
        case "stop": Printing = false; foreach (var b in _bays) { try { Power(b, false); } catch (Exception ex) { Err(ex, b); } } break;
        case "max": if (w.Length > 1 && int.TryParse(w[1], out n)) MaxDrones = n; break;
        case "release":
            foreach (var b in _bays)
                if ((b.State == "DOCKED" || b.State == "CHARGE") && (w.Length < 2 || (int.TryParse(w[1], out n) && n == b.N))) Release(b);
            break;
        case "hold": if (w.Length > 1 && int.TryParse(w[1], out n)) foreach (var b in _bays) if (b.N == n) SetHold(b, true); break;
        case "unhold": if (w.Length > 1 && int.TryParse(w[1], out n)) foreach (var b in _bays) if (b.N == n) { SetHold(b, false); if (b.State == "FAULT") b.State = "IDLE"; } break;
        case "relay": Relay = w.Length < 2 || w[1] != "off"; break;
        case "reload": LoadConfig(); GetBlocks(); _errs.Clear(); _errTotal = 0; break;
    }
}

void Show()
{
    int docked = 0; foreach (var d0 in _bays) if (d0.State == "DOCKED") docked++;
    _sb.Clear();
    _sb.Append("HIVE MOTHER v").Append(VER).Append(Printing ? "  PRINTING" : "  STOPPED");
    if (_errs.Count > 0) _sb.Append("   !! ").Append(_errTotal).Append(" ERROR").Append(_errTotal == 1 ? "" : "S");
    _sb.Append('\n');
    if (RackMode) _sb.Append("Rack: ").Append(docked).Append(" docked, targets ").Append(_targets.Count).Append('\n');
    else _sb.Append("Drones alive: ").Append(AliveAll()).Append(" / ").Append(MaxDrones).Append("   building ").Append(PendingAll()).Append('\n');
    foreach (var b in _bays)
    {
        try
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
        }
        catch (Exception ex) { _sb.Append("(display error ").Append(ex.GetType().FullName).Append(')'); Err(ex, b); }
        _sb.Append('\n');
    }
    if (_bays.Count == 0) _sb.Append("No bays: name a projector, welders and\na merge block with [Bay 1]\n");
    _sb.Append(ErrText());
    int ins = 0, max = 0; try { ins = Runtime.CurrentInstructionCount; max = Runtime.MaxInstructionCount; } catch (Exception) { }
    _sb.Append("Ins: ").Append(ins).Append(" / ").Append(max).Append("  peak ").Append(_peakIns).Append('\n');
    _screen = _sb.ToString();
    if (_lcd != null) { try { _lcd.WriteText(_screen); } catch (Exception) { } }
}
