// =====================================================================
//  TUBE RAM AI  v1.0  - kinetic ram drone (small grid, no weapon)
// ---------------------------------------------------------------------
//  Tube 2x2 / 2x4 / 3x3 / 4x4 (docs/design/tube_drones.md). Sits passive until launched (off its rack or by
//  command), then flies at the nearest enemy and slams into it at full speed. After an impact it backs off
//  and rams again while enough rear thrusters still work. Moves like the Stinger: it points its nose along the
//  push it needs and burns the rear thruster bank (it has no reverse thrust: it brakes by flipping round).
//
//  HUNT   velocity control onto the predicted intercept, friendly avoidance on.
//  TERM   last seconds: proportional navigation at full throttle, friendly avoidance off (a ram must not
//         sidestep its target) but a friendly guard aborts the run when a friend is in the way.
//  EXTEND after an impact or an abort: fly away to the run-up distance, then HUNT again.
//
//  Targets: its own nose camera (and a sensor if fitted) and targets shared over antenna by other drones,
//  carriers and the hive (any of the listed IGC tags). `lock` raycasts what the nose points at.
//  Warheads (the "W" tubes carry a layer of them under the nose armour): armed in TERM only, at the last moment (gap to the hull under
//  Arm Warheads Within, default 20 m, or 0.3 s at the closing speed: gunfire sets armed warheads off) and never
//  within Safe Distance From Launch of the release point; fired on impact (the velocity jump) or by the contact fuse (the nose camera's
//  ray says the hull is less than one tick away). Disarmed on an abort or when the tube falls back out of the terminal phase.
//
//  SEARCH With no target and AI Offensive / AI Flight blocks tagged [AI] on board, `launch` hands the ship to the AI blocks (AI GATE block:
//         switches AI Behavior on, time-shares with a script spin if the AI does nothing) and the nose cameras confirm what the AI points at
//         with boresight / spiral rays. The first valid contact (or an IGC cue) takes the ship back and starts HUNT. Same pattern as the Gnat.
//
//  START: Start Mode (WAIT/HUNT) = HUNT by default: once off its rack (or pasted free) it hunts by itself.
//
//  COMMANDS: launch (hunt) | abort | idle (stop) | rtb | lock | disarm | reload
// =====================================================================
const string VER = "1.0";

string ListenTags = "GNAT,HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER,DRONE_CUE,TUBE";   // a tag that already contains "_" is used as is, others get "_TGT" appended
string Tag = "TUBE", BlockTag = "";
double MaxSpeed = 100, LaunchRange = 3000, ArmRange = 20, SafeDistance = 150, Gain = 2.5;
double TermTime = 2.5, NavGain = 3.5, ImpactDv = 15, RunUp = 0, FriendlyClear = 25, TermRange = 300;
int MaxRams = 0;
double NoseSetback = 0; bool ContactFuse = true, AiSearch = true, StartHunt = true; double AiConfirmRange = 1300, AiConfirmLong = 2600, AiRepick = 20; double _launchTry = -99;
bool AutoLaunch = true, TargetLarge = true, TargetSmall = true, TargetNeutral = false, ReAttack = true;
double AimKp = 9, AimKd = 0.3, MaxTurn = 8;

enum Mode { WAIT, SEARCH, HUNT, TERM, EXTEND, RTB, IDLE }
Mode _mode = Mode.WAIT;
MyIni _ini = new MyIni();
double _now; long _tick; int _peakIns = 0;   // PB limit is Runtime.MaxInstructionCount per run
IMyShipController _rc;
List<IMyGyro> _gyros = new List<IMyGyro>();
List<IMyThrust> _fwd = new List<IMyThrust>();
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyThrust>[] _side = { new List<IMyThrust>(), new List<IMyThrust>(), new List<IMyThrust>(), new List<IMyThrust>() };   // recessed side thrusters by push direction: ship Right, Left, Up, Down
List<IMyWarhead> _heads = new List<IMyWarhead>();
List<IMyCameraBlock> _cams = new List<IMyCameraBlock>();
List<IMySensorBlock> _sensors = new List<IMySensorBlock>();
List<MyDetectedEntityInfo> _sensed = new List<MyDetectedEntityInfo>();
List<IMyShipMergeBlock> _merges = new List<IMyShipMergeBlock>();
List<IMyBroadcastListener> _lTgt = new List<IMyBroadcastListener>();
IMyBroadcastListener _lCmd;
IMyTextSurface _lcd;
IMyCubeGrid _myGrid;
Vector3D _launchPos, _home, _prevVel, _prevErr; bool _armed, _haveHome;
int _rams; double _modeT, _guardT, _slowT, _rangeNow, _closeNow, _tgoNow;
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
string _flAllowed = "hunt,attack,guard,ready,follow,escort,form,assist,rtb,idle,stop,launch,abort,split,focus,next,unlock,aialert";
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

// >>> AI GATE v1.2 (shared block: identical in every drone script that uses AI-block search, synced by tools/ai_gate_sync.py) >>>
// The AI blocks tagged [AI] (AI Offensive + AI Flight) come out of a blueprint with "AI Behavior" off and every setting at its
// default, so switching them on (Enabled) may do nothing at all. In-game report 2026-10-13: Full drones in GUARD stood still and
// HUNT drones never saw a pirate 700 m away, because the script handed the ship to the AI blocks and stopped its own search.
// The gate:
//  * tries to switch "AI Behavior" on (a bool terminal property whose id contains "Behavior"; the exact id is not documented);
//  * until the AI blocks have shown that they work (flown the ship once: sped it up or turned it), gives them the ship for
//    AI Window seconds, then runs the script's own search (camera sweep + scan rays) for Script Search Window seconds, and so on;
//    under fire the script searches. While the AI flies the ship it keeps it, up to Max AI Hold seconds. Once the AI has flown the
//    ship it is trusted and gets the ship whenever the script wants it, as before v2.1 (sim: time-sharing a working AI cost most of
//    its kills, 1v1 decisive 90 % -> 35 %).
// v1.1 (opt-in, audit F-03/SL-11, default OFF = the behaviour of v1.0): "Use Typed AI Block API=true" sets IMyFlightMovementBlock.SpeedLimit
// ("AI Flight Speed Limit (m/s)") on the AI Flight blocks and counts a block as switched on when IsAutoPilotEnabled reads true instead of
// when the "Behavior" terminal property was found. Rests on the ModAPI documentation only (not tested in the game; unit of SpeedLimit unconfirmed).
// v1.2 (2026-10-14, from the Ballista Helm, unverified in the game): "AI Search Interval (s)" (default 5, 0 = leave the block alone) is written
// to IMyOffensiveCombatBlock.UpdateTargetInterval: the block rescans only every 30 s by default [wiki], longer than the AI Window, so a
// window often saw no search at all. Scripts with an AI Block Search Assist switch call AiGateFind only while it is on (with it off the
// AI blocks stay as they came, "AI Behavior" off: switched on, the AI Flight block may fly the ship away on its own) and re-run FindAi on `ai on|off`.
// Own [AI Gate] section in Custom Data (written on first run, "reload" re-reads it).
// Hooks: AiGateLoad() after FleetLoad(true); AiGateFind(_ai) at the end of FindAi(); want = AiGate(want) right before
// "if (want != _aiOn)" in AiStep(); AiGateStatus() in the status text.
bool AiGateOn = true, AiBehaviorSwitch = true, AiTyped = false;
double AiSpeed = 100, AiWin = 6, ScriptWin = 8, AiHoldMax = 60; int AiSearchEvery = 5;
bool _agAi = true, _agLive = false, _agFwdSet = false; double _agT0 = -1, _agMoveT = -99, _agMinV = 0; Vector3D _agFwd;
int _agFound = -1;   // -1 not tried, else the number of AI blocks whose behaviour switch was found
List<ITerminalProperty> _agProps = new List<ITerminalProperty>();

void AiGateLoad()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    const string S = "AI Gate";
    if (!ini.ContainsKey(S, "Time-share With Script Search")) ini.Set(S, "Time-share With Script Search", AiGateOn);
    if (!ini.ContainsKey(S, "Switch On AI Behavior")) ini.Set(S, "Switch On AI Behavior", AiBehaviorSwitch);
    if (!ini.ContainsKey(S, "Use Typed AI Block API")) ini.Set(S, "Use Typed AI Block API", AiTyped);
    if (!ini.ContainsKey(S, "AI Flight Speed Limit (m/s)")) ini.Set(S, "AI Flight Speed Limit (m/s)", AiSpeed);
    if (!ini.ContainsKey(S, "AI Window (s)")) ini.Set(S, "AI Window (s)", AiWin);
    if (!ini.ContainsKey(S, "Script Search Window (s)")) ini.Set(S, "Script Search Window (s)", ScriptWin);
    if (!ini.ContainsKey(S, "Max AI Hold (s)")) ini.Set(S, "Max AI Hold (s)", AiHoldMax);
    if (!ini.ContainsKey(S, "AI Search Interval (s)")) ini.Set(S, "AI Search Interval (s)", AiSearchEvery);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    AiGateOn = ini.Get(S, "Time-share With Script Search").ToBoolean(AiGateOn);
    AiBehaviorSwitch = ini.Get(S, "Switch On AI Behavior").ToBoolean(AiBehaviorSwitch);
    AiTyped = ini.Get(S, "Use Typed AI Block API").ToBoolean(AiTyped);
    AiSpeed = Math.Max(1, ini.Get(S, "AI Flight Speed Limit (m/s)").ToDouble(AiSpeed));
    AiWin = Math.Max(1, ini.Get(S, "AI Window (s)").ToDouble(AiWin));
    ScriptWin = Math.Max(1, ini.Get(S, "Script Search Window (s)").ToDouble(ScriptWin));
    AiHoldMax = Math.Max(AiWin, ini.Get(S, "Max AI Hold (s)").ToDouble(AiHoldMax));
    AiSearchEvery = Math.Max(0, ini.Get(S, "AI Search Interval (s)").ToInt32(AiSearchEvery));
}

void AiGateFind(List<IMyFunctionalBlock> ai)
{
    if (AiSearchEvery > 0) foreach (var b in ai) { var oc = b as IMyOffensiveCombatBlock; if (oc != null) oc.UpdateTargetInterval = AiSearchEvery; }
    if (!AiBehaviorSwitch || ai.Count == 0) return;
    int n = 0;
    foreach (var b in ai)
    {
        var fm = AiTyped ? b as IMyFlightMovementBlock : null;
        if (fm != null) fm.SpeedLimit = (float)AiSpeed;
        _agProps.Clear(); b.GetProperties(_agProps);
        ITerminalProperty<bool> sw = null;
        foreach (var p in _agProps)
        {
            string id = p.Id.ToLowerInvariant();
            if (!id.Contains("behavio")) continue;
            var bp = p.As<bool>();
            if (bp != null && (sw == null || id.Contains("activ"))) sw = bp;
        }
        if (sw != null && !sw.GetValue(b)) sw.SetValue(b, true);
        if (fm != null ? fm.IsAutoPilotEnabled : sw != null) n++;
    }
    _agFound = n;
}

bool AiGate(bool want)
{
    if (!want || !AiGateOn || _agLive) { _agT0 = -1; return want; }
    var vs = _rc.GetShipVelocities();
    double v = vs.LinearVelocity.Length();
    if (_agT0 < 0) { _agT0 = _now; _agAi = !UnderFire; _agMoveT = -99; _agMinV = v; _agFwdSet = false; }
    double age = _now - _agT0;
    if (_agAi)
    {
        // The AI flies if it speeds the ship up again (the dampeners alone only slow it), keeps turning it, or has turned it more than
        // 25 deg since the hand-over (it turns fast onto what it found, then may only brake; the script's own sweep coasts on for
        // less than that). Speed and turn rate are ignored for the first 1.5 / 2.5 s while the ship coasts from the script's control.
        if (!_agFwdSet) { _agFwd = _rc.WorldMatrix.Forward; _agFwdSet = true; }
        if (age < 1.0) _agMinV = v;
        else
        {
            _agMinV = Math.Min(_agMinV, v);
            if ((age > 1.5 && v > _agMinV + 3) || (age > 2.5 && vs.AngularVelocity.LengthSquared() > 0.0144) || Vector3D.Dot(_rc.WorldMatrix.Forward, _agFwd) < 0.906)
            { _agMoveT = _now; _agLive = true; }
        }
        bool busy = _now - _agMoveT < 4 && age < AiHoldMax;
        if (!busy && (age >= AiWin || (UnderFire && age >= 1.5))) { _agAi = false; _agT0 = _now; }
    }
    else if (age >= ScriptWin) { _agAi = true; _agT0 = _now; _agMoveT = -99; _agMinV = v; _agFwdSet = false; }
    return _agAi;
}

string AiGateStatus()
{
    if (_agFound < 0) return "";
    if (_agFound == 0 && AiBehaviorSwitch) return "AI Behavior switch not found:\n turn AI Behavior on by hand\n";
    return (AiBehaviorSwitch ? "AI Behavior on (" + _agFound + " blocks)" : "") + (_agLive ? " AI ok\n" : AiBehaviorSwitch ? "\n" : "");
}
// <<< AI GATE <<<

public Program()
{
    LoadConfig();
    FleetLoad(true); AiGateLoad();
    foreach (var t in ListenTags.Split(',')) if (t.Trim() != "") _lTgt.Add(IGC.RegisterBroadcastListener(t.Trim().Contains("_") ? t.Trim() : t.Trim() + "_TGT"));
    _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD");
    GetBlocks();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

void LoadConfig()
{
    MyIniParseResult r; if (!_ini.TryParse(Me.CustomData, out r)) return;
    const string S = "Tube";
    ListenTags = C(S, "Listen To IGC Tags", ListenTags);
    Tag = C(S, "IGC Tag", Tag);
    BlockTag = C(S, "Block Tag", BlockTag);
    AutoLaunch = C(S, "Auto Launch When Released", AutoLaunch);
    AiSearch = C(S, "AI Block Search", AiSearch);
    StartHunt = C(S, "Start Mode (WAIT/HUNT)", StartHunt ? "HUNT" : "WAIT").Trim().ToUpperInvariant() != "WAIT";
    AiRepick = C(S, "AI Re-pick After (s)", AiRepick);
    AiConfirmRange = C(S, "AI Confirm Raycast Range", AiConfirmRange);
    AiConfirmLong = C(S, "AI Confirm Long Raycast Range", AiConfirmLong);
    LaunchRange = C(S, "Max Target Distance", LaunchRange);
    ArmRange = C(S, "Arm Warheads Within", ArmRange);
    ContactFuse = C(S, "Warhead Contact Fuse", ContactFuse);
    NoseSetback = C(S, "Nose Camera Setback (m)", NoseSetback);   // nose cameras behind the front face (Tube L 1x1: one 2.5 m block)
    SafeDistance = C(S, "Safe Distance From Launch", SafeDistance);
    MaxSpeed = C(S, "Max Speed", MaxSpeed);
    TermTime = C(S, "Terminal Time", TermTime);
    NavGain = C(S, "Nav Gain", NavGain);
    ReAttack = C(S, "Re-Attack", ReAttack);
    MaxRams = (int)C(S, "Max Rams", (double)MaxRams);
    RunUp = C(S, "Run Up Distance", RunUp);
    ImpactDv = C(S, "Impact Dv", ImpactDv);
    FriendlyClear = C(S, "Friendly Clearance", FriendlyClear);
    TargetLarge = C(S, "Target Large Grids", TargetLarge);
    TargetSmall = C(S, "Target Small Grids", TargetSmall);
    TargetNeutral = C(S, "Target Neutral/Unowned", TargetNeutral);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}
string C(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double C(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }
bool C(string s, string k, bool d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToBoolean(d); }

bool Mine(IMyTerminalBlock b)
{
    if (b.CubeGrid != Me.CubeGrid) return false;
    if (BlockTag == "")
    {   // default: the PB's own name without the trailing " PB" ("Tube 3x3")
        string n = Me.CustomName; if (n.EndsWith(" PB")) n = n.Substring(0, n.Length - 3);
        BlockTag = n;
    }
    return b.CustomName.Contains(BlockTag);
}

void GetBlocks()
{
    _myGrid = Me.CubeGrid;
    var ctrls = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(ctrls, Mine);
    _rc = ctrls.Count > 0 ? ctrls[0] : null;
    GridTerminalSystem.GetBlocksOfType(_gyros, Mine);
    GridTerminalSystem.GetBlocksOfType(_thr, Mine);
    GridTerminalSystem.GetBlocksOfType(_heads, Mine);
    GridTerminalSystem.GetBlocksOfType(_cams, Mine);
    GridTerminalSystem.GetBlocksOfType(_sensors, Mine);
    GridTerminalSystem.GetBlocksOfType(_merges, Mine);
    foreach (var c in _cams) c.EnableRaycast = true;
    FindAi();
    _fwd.Clear();
    foreach (var l in _side) l.Clear();
    if (_rc != null) foreach (var t in _thr)
    {
        Vector3D p = t.WorldMatrix.Backward;   // a thruster pushes along its Backward
        MatrixD m = _rc.WorldMatrix;
        if (Vector3D.Dot(p, m.Forward) > 0.9) _fwd.Add(t);
        else if (Vector3D.Dot(p, m.Right) > 0.9) _side[0].Add(t);
        else if (Vector3D.Dot(p, m.Left) > 0.9) _side[1].Add(t);
        else if (Vector3D.Dot(p, m.Up) > 0.9) _side[2].Add(t);
        else if (Vector3D.Dot(p, m.Down) > 0.9) _side[3].Add(t);
    }
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.8f;
}

bool Docked() { foreach (var m in _merges) if (m.IsConnected) return true; return false; }

bool Valid(MyDetectedEntityInfo e)
{
    FaSee(e);
    if (e.IsEmpty() || e.EntityId == Me.CubeGrid.EntityId) return false;
    if (e.Type == MyDetectedEntityType.LargeGrid ? !TargetLarge : e.Type == MyDetectedEntityType.SmallGrid ? !TargetSmall : true) return false;
    var r = e.Relationship;
    return r == MyRelationsBetweenPlayerAndBlock.Enemies || (TargetNeutral && (r == MyRelationsBetweenPlayerAndBlock.Neutral || r == MyRelationsBetweenPlayerAndBlock.NoOwnership));
}

double _fcT = -99; string _fc = "";   // last new contact: distance, speed, source (AI / CAM / SENSOR / LOCK / IGC) and when (as the Ballista's Helm)
void Add(long id, Vector3D p, Vector3D v, double r, string src = "IGC")
{
    Track t;
    if (!_contacts.TryGetValue(id, out t))
    {
        t = new Track { Id = id }; _contacts[id] = t;
        _fcT = _now; _fc = Vector3D.Distance(p, Me.GetPosition()).ToString("0") + " m v " + v.Length().ToString("0") + " " + src;
    }
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
    FleetPump(Command, p => { _home = p; _haveHome = true; });
    if (_tick % 10 == 0)
    {
        foreach (var s in _sensors) { if (!s.IsWorking) continue; _sensed.Clear(); s.DetectedEntities(_sensed); foreach (var e in _sensed) if (Valid(e)) Add(e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() / 2, "SENSOR"); }
        var drop = new List<long>(); foreach (var kv in _contacts) if (_now - kv.Value.Seen > 5) drop.Add(kv.Key);
        foreach (var id in drop) _contacts.Remove(id);
        if (_tgt != null && !_contacts.ContainsKey(_tgt.Id)) _tgt = null;
    }
    if (_rc == null) { _status = "No remote control"; Show(); return; }

    bool docked = Docked();
    if (docked && _mode != Mode.WAIT && _mode != Mode.IDLE) { AiOff(); Release(); _mode = Mode.WAIT; }
    // Start Mode HUNT (default): off the rack (or pasted free) it goes hunting by itself, with the AI blocks if nobody gave it a target;
    // WAIT: only `launch` or a target in range (Auto Launch When Released) starts it. Tried every 2 s, not every tick (Launch re-scans the blocks)
    if (_mode == Mode.WAIT && !docked && ((AutoLaunch && Nearest() != null) || (StartHunt && _now - _launchTry > 2))) { _launchTry = _now; Launch(); }
    if (_mode == Mode.SEARCH) Search(dt);
    else if (_mode == Mode.HUNT || _mode == Mode.TERM) Hunt(dt);
    else if (_mode == Mode.EXTEND) Extend(dt);
    else if (_mode == Mode.RTB) Rtb(dt);
    _prevVel = _rc.GetShipVelocities().LinearVelocity;
    _peakIns = Math.Max(_peakIns, Runtime.CurrentInstructionCount);
    if (_tick % 20 == 0) Show();
}

void SetMode(Mode m) { _mode = m; _modeT = _now; }

void Command(string a)
{
    if (a.StartsWith("launch") || a.StartsWith("hunt")) { if (!Docked()) Launch(); else _status = "Still docked"; }
    else if (a == "abort") { Disarm(); AiOff(); Release(); SetMode(Mode.IDLE); }
    else if (a == "idle" || a == "stop") { Disarm(); AiOff(); Release(); SetMode(Mode.IDLE); }
    else if (a == "rtb") { if (!Docked()) { AiOff(); SetMode(Mode.RTB); } }
    else if (a == "lock") Lock();
    else if (a == "disarm") Disarm();
    else if (a == "reload") { LoadConfig(); FleetLoad(true); AiGateLoad(); GetBlocks(); }
}

void Lock()
{
    foreach (var c in _cams)
    {
        if (!c.IsWorking || _rc == null || Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) < 0.97 || !c.CanScan(LaunchRange)) continue;
        var e = c.Raycast(LaunchRange);
        if (!e.IsEmpty() && Valid(e)) { Add(e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() / 2, "LOCK"); _tgt = _contacts[e.EntityId]; _status = "Locked " + e.Name; }
        else _status = "Nothing to lock";
        return;
    }
    _status = "No camera ready";
}

Track Nearest()
{
    Track best = null; double bd = LaunchRange; Vector3D me = Me.GetPosition();
    foreach (var t in _contacts.Values) { double d = Vector3D.Distance(t.Pos, me); if (d < bd) { bd = d; best = t; } }
    return best;
}

void Launch()
{
    if (_tgt == null) _tgt = Nearest();
    GetBlocks();
    if (_tgt == null)
    {
        if (AiSearch && _ai.Count > 0 && _rc != null) { _launchPos = _rc.GetPosition(); _rams = 0; SetMode(Mode.SEARCH); _status = "Searching with the AI blocks"; }
        else _status = "No target in range";
        return;
    }
    SetMode(Mode.HUNT); _launchPos = _rc.GetPosition(); _rams = 0; _slowT = 0;
}

// the segment me -> p passes within FriendlyClear of a non-enemy grid that is not the target (FRIENDLY AVOIDANCE obstacle list)
bool FriendlyInWay(Vector3D me, Vector3D p)
{
    Vector3D seg = p - me; double sl = seg.Length(); if (sl < 1) return false;
    Vector3D u = seg / sl;
    foreach (var kv in _fa)
    {
        if (_contacts.ContainsKey(kv.Key)) continue;
        var o = kv.Value; Vector3D oc = o.P + o.V * Math.Min(_now - o.T, 3);
        double along = Vector3D.Dot(oc - me, u);
        if (along < -o.R || along > sl + o.R) continue;
        double off = (oc - me - u * MathHelper.Clamp(along, 0, sl)).Length();
        if (off < o.R + FriendlyClear + _faR) return true;
    }
    return false;
}

void Hunt(double dt)
{
    if (_tgt == null) { _tgt = Nearest(); if (_tgt == null) { _status = "Lost target"; Release(); SetMode(AiSearch && _ai.Count > 0 ? Mode.SEARCH : Mode.WAIT); return; } }
    // keep the lock with the nose camera when possible
    double age = _now - _tgt.Seen;
    Vector3D me = _rc.GetPosition(), myVel = _rc.GetShipVelocities().LinearVelocity, g = _rc.GetNaturalGravity();
    Vector3D tp = _tgt.Pos + _tgt.Vel * age, tv = _tgt.Vel;
    if (age > 0.2)
        foreach (var c in _cams)
        {
            double d0 = Vector3D.Distance(tp, c.GetPosition());
            if (!c.IsWorking || !c.CanScan(d0 + 20) || Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) < 0.9) continue;
            var e = c.Raycast(tp + Vector3D.Normalize(tp - c.GetPosition()) * 3);
            if (!e.IsEmpty() && Valid(e)) { Add(e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() / 2, "CAM"); if (e.EntityId != _tgt.Id) _tgt = _contacts[e.EntityId]; }
            break;
        }
    Vector3D rel = tp - me; double dist = rel.Length(); Vector3D los = rel / Math.Max(dist, 1);
    double closing = Vector3D.Dot(myVel - tv, los);
    double tgo = MathHelper.Clamp(dist / Math.Max(20, closing), 0, 10);
    _rangeNow = dist; _closeNow = closing; _tgoNow = tgo;

    // impact: a sudden velocity change, or touching the target while the closing speed has turned
    bool hit = _tick > 5 && (myVel - _prevVel).Length() > ImpactDv && dist < _tgt.Radius + 30;
    if (hit && _armed) { _rams++; Boom("impact at " + closing.ToString("0") + " m/s"); return; }   // warhead tube: the nose is in the hull, fire now
    if (_armed && dist - _tgt.Radius > Math.Max(ArmRange, Math.Max(closing, 0) * 0.3) * 2 + 20) Disarm();   // fell back out of the terminal phase (overshoot, lost lock)
    if (hit) { _rams++; Disarm(); _status = "IMPACT " + _rams + " at " + closing.ToString("0") + " m/s";
        if (ReAttack && (MaxRams == 0 || _rams < MaxRams) && Healthy()) SetMode(Mode.EXTEND); else SetMode(Mode.HUNT); return; }

    Vector3D pred = tp + tv * tgo + 0.5 * _tgt.Acc * tgo * tgo;
    Vector3D want, nose;
    double fmax = 0; foreach (var t in _fwd) if (t.IsWorking) fmax += t.MaxEffectiveThrust;
    double mass = _rc.CalculateShipMass().PhysicalMass, amax = fmax > 0 ? fmax / mass : 1;
    if (_mode == Mode.HUNT)
    {
        if (tgo < TermTime || dist < TermRange) { SetMode(Mode.TERM); _guardT = 0; }
        want = Vector3D.Normalize(pred - me) * MaxSpeed;
        bool launching = Vector3D.Distance(me, _launchPos) < SafeDistance;
        if (launching) want = Vector3D.Normalize(want) * Math.Min(MaxSpeed, 30);
        want = FaWant(want, me);   // FRIENDLY AVOIDANCE: keep clear of non-enemy grids
        Vector3D need = (want - myVel) * Gain - g;
        nose = need.LengthSquared() > 0.01 ? Vector3D.Normalize(need) : _rc.WorldMatrix.Forward;
        Burn(need, nose, fmax, mass, dt);
    }
    else
    {   // TERM: proportional navigation, all thrust used, speed held at the cap
        if (_now - _guardT > 0.25)
        {
            _guardT = _now;
            bool blocked = FriendlyInWay(me, pred);
            if (!blocked)
                foreach (var c in _cams)
                {
                    if (!c.IsWorking || Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) < 0.97 || !c.CanScan(dist * 1.2 + 20)) continue;
                    var he = c.Raycast(dist * 1.2 + 20);
                    if (!he.IsEmpty() && he.EntityId != _tgt.Id && he.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies && (he.Type == MyDetectedEntityType.LargeGrid || he.Type == MyDetectedEntityType.SmallGrid)) { blocked = true; }
                    break;
                }
            if (!blocked && _tgt.Radius > 0)
                foreach (var kv in _fa) { if (_contacts.ContainsKey(kv.Key)) continue; if (Vector3D.Distance(kv.Value.P, tp) < FriendlyClear + kv.Value.R) { blocked = true; break; } }
            if (blocked) { _status = "ABORT friendly in the way"; Disarm(); SetMode(Mode.EXTEND); return; }
        }
        // proportional navigation: lateral acceleration from the line-of-sight rate, the rest of the thrust goes along the velocity
        Vector3D vrel = myVel - tv, vHat = vrel.LengthSquared() > 1 ? Vector3D.Normalize(vrel) : _rc.WorldMatrix.Forward;
        Vector3D losRate = Vector3D.Cross(rel, tv - myVel) / Math.Max(dist * dist, 1);
        Vector3D aLat = Vector3D.Cross(losRate, los) * (NavGain * Math.Max(closing, 20));
        aLat -= g - Vector3D.Dot(g, vHat) * vHat;
        // the side thrusters take what they can of aLat (the nose stays on the target); only the rest tilts the nose
        Vector3D sf = _rc.WorldMatrix.Forward, side = aLat - Vector3D.Dot(aLat, sf) * sf;
        side *= SideScale(side, mass);
        Vector3D aTilt = aLat - side;
        double lm = Math.Min(aTilt.Length(), amax * 0.95);
        if (aTilt.Length() > lm && lm > 0) aTilt = Vector3D.Normalize(aTilt) * lm;
        double along = Math.Sqrt(Math.Max(amax * amax - lm * lm, 0));
        Vector3D dir = myVel.Length() > 5 ? Vector3D.Normalize(myVel) : los;
        if (myVel.Length() < MaxSpeed * 0.9) dir = Vector3D.Normalize(dir + los);   // below the cap: lean along the line of sight
        nose = Vector3D.Normalize(dir * along + aTilt);
        Aim(nose, dt);
        double al = Vector3D.Dot(nose, _rc.WorldMatrix.Forward);
        float pct = al > 0.9 && myVel.Length() < MaxSpeed * 1.02 ? 1f : 0.001f;
        foreach (var t in _fwd) t.ThrustOverridePercentage = pct;
        Lateral(side, mass);   // the line-of-sight correction goes to the side thrusters first, so the nose can stay on the target
        _rc.DampenersOverride = false;
        // warheads (only when the player fitted some): arm in the terminal phase, never near home
        bool clear = Vector3D.Distance(me, _launchPos) > SafeDistance;
        if (_heads.Count > 0)
        {
            // arm at the last moment (gap to the hull under Arm Warheads Within or 0.3 s at the closing speed): an armed warhead goes off when
            // gunfire hits it, so arming 200 m out let the target's rockets blow the tube up short of the hull (sim, Basilisk vs Tube 4x4 W)
            if (!_armed && clear && dist - _tgt.Radius < Math.Max(ArmRange, Math.Max(closing, 0) * 0.3)) { foreach (var w in _heads) w.IsArmed = true; _armed = true; }
            // contact fuse: the nose camera sits flush in the front face, so its ray measures the gap to the hull ahead; fire when the
            // nose touches during the next tick (the impact check above only sees the hit a tick later, by the velocity jump)
            if (_armed && ContactFuse && dist < 100)
                foreach (var c in _cams)
                {
                    if (!c.IsWorking || Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) < 0.97 || !c.CanScan(45)) continue;
                    var he = c.Raycast(45);
                    if (!he.IsEmpty() && he.HitPosition.HasValue && Valid(he) && Vector3D.Distance(he.HitPosition.Value, c.GetPosition()) < NoseSetback + Math.Max(1.5, Math.Max(closing, 20) * dt * 1.5))
                    { Boom("contact at " + closing.ToString("0") + " m/s"); return; }
                    break;
                }
        }
        // cannot close: a target running at our speed
        if (closing < 5) { _slowT += dt; if (_slowT > 15) _status = "CANNOT CLOSE"; } else _slowT = 0;
        if (_now - _modeT > 20 && closing < 0) { SetMode(Mode.HUNT); }   // overshot: re-plan
    }
    _status = (_mode == Mode.TERM ? "TERM " : "HUNT ") + dist.ToString("0") + " m  tgo " + tgo.ToString("0.0") + "s  rams " + _rams;
}

// ---- AI block search (same pattern as the Gnat: the AI blocks fly, the nose cameras confirm what they point at) ----
List<IMyFunctionalBlock> _ai = new List<IMyFunctionalBlock>();
bool _aiOn = false; int _aiScan = 0; double _aiSince = -99;
bool UnderFire { get { return false; } }   // (AI GATE hook: tubes have no damage sensing)
bool IsAiBlock(IMyTerminalBlock b)
{
    if (!Mine(b)) return false;
    if (b.CustomName.Contains("[AI]")) return true;
    string d = b.BlockDefinition.ToString();
    return d.Contains("OffensiveCombat") || d.Contains("FlightMovement");
}
void FindAi()
{
    GridTerminalSystem.GetBlocksOfType(_ai, b => IsAiBlock(b));
    foreach (var b in _ai) b.Enabled = _aiOn;   // never let them fight the script
    // (AI GATE v1.2 writes [AI Gate] AI Search Interval (s)=5 to the AI Offensive blocks: the detector rescans every 5 s, not 30)
    AiGateFind(_ai);
}
void AiOff()
{
    _aiOn = false; _agT0 = -1;
    foreach (var b in _ai) b.Enabled = false;
}

void Search(double dt)
{
    if (_tgt == null) _tgt = Nearest();
    if (_tgt != null)
    {   // found (a confirm ray or an IGC cue): the script takes the ship back
        AiOff(); foreach (var g in _gyros) g.GyroOverride = false;
        _launchPos = _rc.GetPosition(); _slowT = 0; SetMode(Mode.HUNT); return;
    }
    bool want = AiGate(_ai.Count > 0);   // AI GATE: the AI blocks may do nothing (AI Behavior off): time-share with a script spin
    if (want != _aiOn)
    {
        _aiOn = want;
        if (want) { Release(); _aiSince = _now; }   // thrusters and gyros go to the AI
        foreach (var b in _ai) b.Enabled = want;
    }
    else if (_aiOn)
    {
        foreach (var b in _ai) if (!b.Enabled) b.Enabled = true;   // somebody else switched them off while they should fly the ship
        // re-pick (Ballista Helm): the AI holds whatever it chose first (a wreck, a decoy); after AI Re-pick After s on without a contact
        // although an enemy was seen in the last minute, power-cycle it for one tick so it starts a fresh search
        if (AiRepick > 0 && _now - _aiSince > AiRepick && _now - _fcT < 60) { foreach (var b in _ai) b.Enabled = false; _aiSince = _now; return; }
    }
    if (!_aiOn)
    {   // the script's own window: coast and spin slowly so the nose cameras sweep the sky
        foreach (var t in _thr) t.ThrustOverridePercentage = 0;
        foreach (var gy in _gyros) { gy.GyroOverride = true; gy.Pitch = 0; gy.Roll = 0; gy.Yaw = 0.6f; }
    }
    // the AI points the nose at what it found: the first nose camera casts the long ray (a target held at 1.3-2.6 km), any other the short
    // frequent one; 1 ray in 2 straight down the boresight, the rest on a 0.15-2.5 deg spiral (Ballista Helm / Gnat pattern)
    bool longCam = true;
    foreach (var c in _cams)
    {
        if (!c.IsWorking || Vector3D.Dot(c.WorldMatrix.Forward, _rc.WorldMatrix.Forward) < 0.9) continue;
        double len = longCam ? AiConfirmLong : AiConfirmRange;
        longCam = false;
        if (c.AvailableScanRange < len) continue;
        _aiScan++;
        double r = 0, th = _aiScan * 2.39996323;
        if ((_aiScan & 1) == 1) { int j = (_aiScan >> 1) % 16; r = 0.15 + 2.35 * Math.Pow((j + 0.5) / 16.0, 1.5); }
        var e = c.Raycast(len, (float)(r * Math.Sin(th)), (float)(r * Math.Cos(th)));
        if (Valid(e)) Add(e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() / 2, "AI");
    }
    _status = "SEARCH " + (_aiOn ? "AI flying" : "script spin") + "  contacts " + _contacts.Count;
}

// fly away from the target (sideways of the line) until the run-up distance is reached, then hunt again
void Extend(double dt)
{
    if (_tgt == null) { Release(); SetMode(Mode.WAIT); return; }
    Vector3D me = _rc.GetPosition(), myVel = _rc.GetShipVelocities().LinearVelocity, g = _rc.GetNaturalGravity();
    Vector3D rel = me - _tgt.Pos; double dist = rel.Length();
    double fmax = 0; foreach (var t in _fwd) if (t.IsWorking) fmax += t.MaxEffectiveThrust;
    double mass = _rc.CalculateShipMass().PhysicalMass, amax = fmax > 0 ? fmax / mass : 1;
    double run = RunUp > 0 ? RunUp : MathHelper.Clamp(1.3 * MaxSpeed * MaxSpeed / (2 * amax), 200, 800);
    if (dist > run || _now - _modeT > 10) { SetMode(Mode.HUNT); return; }
    Vector3D away = dist > 1 ? rel / dist : -_rc.WorldMatrix.Forward;
    Vector3D side = Vector3D.Cross(away, _rc.WorldMatrix.Up); side = side.LengthSquared() > 0.01 ? Vector3D.Normalize(side) : _rc.WorldMatrix.Right;
    Vector3D want = Vector3D.Normalize(away + side * 0.4) * MaxSpeed * 0.6;
    want = FaWant(want, me);
    Vector3D need = (want - myVel) * Gain - g;
    Vector3D nose = need.LengthSquared() > 0.01 ? Vector3D.Normalize(need) : _rc.WorldMatrix.Forward;
    Burn(need, nose, fmax, mass, dt);
    _status = "EXTEND " + dist.ToString("0") + " / " + run.ToString("0") + " m  rams " + _rams;
}

// fly home at 40 m/s, flip-and-burn stop inside 50 m, then idle
void Rtb(double dt)
{
    Vector3D me = _rc.GetPosition(), myVel = _rc.GetShipVelocities().LinearVelocity, g = _rc.GetNaturalGravity();
    Vector3D home = _haveHome ? _home : _launchPos;
    Vector3D rel = home - me; double dist = rel.Length();
    if (dist < 50 && myVel.Length() < 3) { Release(); SetMode(Mode.IDLE); _status = "Home"; return; }
    double fmax = 0; foreach (var t in _fwd) if (t.IsWorking) fmax += t.MaxEffectiveThrust;
    double mass = _rc.CalculateShipMass().PhysicalMass;
    double sp = Math.Min(40, Math.Sqrt(2 * Math.Max(fmax / mass * 0.5, 1) * Math.Max(dist - 5, 0)));
    Vector3D want = (dist > 1 ? rel / dist : Vector3D.Zero) * sp;
    want = FaWant(want, me);
    Vector3D need = (want - myVel) * Gain - g;
    Vector3D nose = need.LengthSquared() > 0.01 ? Vector3D.Normalize(need) : _rc.WorldMatrix.Forward;
    Burn(need, nose, fmax, mass, dt);
    _status = "RTB " + dist.ToString("0") + " m";
}

// the recessed side thrusters push the sideways part of an acceleration `acc` (m/s2) without turning the flank to the target
void Lateral(Vector3D acc, double mass)
{
    MatrixD m = _rc.WorldMatrix;
    Vector3D[] dir = { m.Right, m.Left, m.Up, m.Down };
    for (int k = 0; k < 4; k++)
    {
        double cap = 0; foreach (var t in _side[k]) if (t.IsWorking) cap += t.MaxEffectiveThrust / mass;
        double a = Math.Max(0, Vector3D.Dot(acc, dir[k]));
        float pct = cap > 0 ? (float)MathHelper.Clamp(a / cap, 0, 1) : 0;
        foreach (var t in _side[k]) t.ThrustOverridePercentage = Math.Max(pct, 0.001f);
    }
}

// the share (0..1) of the sideways acceleration `acc` the working side thrusters can push
double SideScale(Vector3D acc, double mass)
{
    MatrixD m = _rc.WorldMatrix; double s = 1;
    double[] c = { Vector3D.Dot(acc, m.Right), Vector3D.Dot(acc, m.Up) };
    for (int k = 0; k < 2; k++)
    {
        if (Math.Abs(c[k]) < 1e-3) continue;
        double cap = 0; foreach (var t in _side[k * 2 + (c[k] > 0 ? 0 : 1)]) if (t.IsWorking) cap += t.MaxEffectiveThrust / mass;
        s = Math.Min(s, cap / Math.Abs(c[k]));
    }
    return s;
}

// point-and-burn: nose along the needed push; burn only the aligned part (the side thrusters take the rest of the push)
void Burn(Vector3D need, Vector3D nose, double fmax, double mass, double dt)
{
    if (need.LengthSquared() > 0.01) Aim(nose, dt);
    Lateral(need, mass);
    double along = Vector3D.Dot(need, _rc.WorldMatrix.Forward), mag = need.Length();
    double gate = mag > 0 ? MathHelper.Clamp((along / mag - 0.8) / 0.2, 0, 1) : 0;
    float pct = fmax > 0 ? (float)MathHelper.Clamp(along * mass / fmax * gate, 0, 1) : 0;
    foreach (var t in _fwd) t.ThrustOverridePercentage = Math.Max(pct, 0.001f);
    _rc.DampenersOverride = false;
}

// enough of the rear bank and the gyros still work to try another run
bool Healthy()
{
    int w = 0; foreach (var t in _fwd) if (t.IsWorking) w++;
    int gy = 0; foreach (var x in _gyros) if (x.IsWorking) gy++;
    return _rc != null && _fwd.Count > 0 && w * 2 >= _fwd.Count && gy > 0;
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

// fire every warhead (arming any that is not armed yet) and stop
void Boom(string why)
{
    foreach (var w in _heads) { if (!w.IsArmed) w.IsArmed = true; w.Detonate(); }
    _status = "DETONATED: " + why; AiOff(); SetMode(Mode.IDLE);
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
    string tl = _tgt != null ? (_rangeNow.ToString("0") + " m  closing " + _closeNow.ToString("0") + " m/s  tgo " + _tgoNow.ToString("0.0") + " s") : "none";
    int w = 0; foreach (var t in _fwd) if (t.IsWorking) w++;
    string s = "TUBE RAM v" + VER + "\nMode: " + _mode + (Docked() ? "  (docked)" : "") + "\nTarget : " + tl + "\nSpeed: " + (_rc != null ? _rc.GetShipVelocities().LinearVelocity.Length().ToString("0") : "-") + " m/s   Rams: " + _rams +
               "\nThrusters: " + w + " / " + _fwd.Count + "\nContacts: " + _contacts.Count + (_heads.Count > 0 ? "\nWarheads: " + _heads.Count + (_armed ? " ARMED" : " safe") : "") +
               "\n" + (_fc != "" ? "1st contact " + _fc + " " + (_now - _fcT).ToString("0") + " s ago\n" : "") + AiGateStatus() + _status + "\nIns: " + Runtime.CurrentInstructionCount + " / " + Runtime.MaxInstructionCount + "  peak " + _peakIns;
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
