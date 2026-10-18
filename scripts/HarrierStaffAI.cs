// =====================================================================
//  HARRIER STAFF AI  v1.0-harrier-split  - the STAFF half of the two-PB Harrier ("[Staff] ..." Programmable Block, next to "[Flight] ..." = HarrierAI.cs (the Flight half))
//  For Space Engineers (vanilla, small grid). Paste into a second Programmable Block on the SAME grid as the Flight PB, named "[Staff] ...".
//  Everything the drone does that is not the hot path:
//   * FLEET LINK   fleet channel (targets in, target shares out, home, commands) - same [Fleet] section as the Harrier script
//   * FOLLOW       owner tracking, slot geometry, SATELLITE ring - same [Follow] section; computes the velocity the Flight PB should fly
//   * LAUNCH       take-off after the release from a printer - same [Launch] section; Staff flies the burn, Flight lets go of the ship
//   * DISPLAY      the full status screen (mode, target, aim, rockets, battery, hive, follow, launch, link state)
//   * WATCHDOG     the Flight PB silent for 1.5 s (or switched off / destroyed): gyro and thruster overrides released once, dampeners on, "FLIGHT PB DOWN"
//  Link (TransmissionDistance.CurrentConstruct, key "WSPL" + the Flight PB's EntityId; both PBs run Update1) - see the header of HarrierAI.cs (the Flight half).
//  The Staff finds the nearest "[Flight]" PB of its construct (re-checked every 10 s and when the grid changes).
//  CUSTOM DATA (this PB): [Launch], [Follow], [Fleet] (written on first run). IGC Tag, Start Mode, Combat Speed and Wingmate Separation Range
//  are read from the Flight PB's Custom Data ([Harrier] section), so nothing is configured twice.
//  Commands (PB argument, on EITHER PB): the Staff's words (launch, armed, disarm, follow, satellite, reload) are handled here; the Staff forwards every word to the Flight PB and the Flight PB forwards its argument to the Staff.
// =====================================================================

const string VER = "1.0-harrier-split";

// ---------------- settings (read from the Flight PB's Custom Data) -----
string Tag = "HARRIER", StartMode = "SENTRY";
double MaxSpeed = 98, SepRange = 70;

// ---------------- state ----------------------------------------------
enum Mode { IDLE, GUARD, HUNT, ASSIST, RTB, FOLLOW }
Mode _mode = Mode.IDLE;
double _now = 0; long _tick = 0;
Vector3D _dynHome; double _dynHomeT = -99;
IMyShipController _rc; IMyProgrammableBlock _fpb; IMyTextSurface _lcd; IMyCubeGrid _myGrid;
List<IMyGyro> _gyros = new List<IMyGyro>(); List<IMyThrust> _thr = new List<IMyThrust>(); List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
List<IMySensorBlock> _sensors = new List<IMySensorBlock>(); List<IMyFunctionalBlock> _ai = new List<IMyFunctionalBlock>();
List<MyDetectedEntityInfo> _sensed = new List<MyDetectedEntityInfo>();
StringBuilder _sb = new StringBuilder();
string _fk = "", _sig = "", _lsKey = "", _crash = "";
IMyBroadcastListener _lHome, _lCmd, _lSwarm, _lF2S, _lFlt, _lArg;
// mirror of the Flight PB (F2S)
int _fl, _f2sN; long _f2sTick, _tgtId; double _f2sT = -99, _aimErr, _fireWin, _aBrake = 5, _relT = -99; float _batt; string _gate = ""; Vector3D _tgtPos;
bool _down, _folBurn, _folFol; int _errRun;

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
string _flAllowed = "hunt,attack,guard,ready,sentry,follow,escort,form,assist,rtb,idle,stop,launch,abort,split,focus,next,unlock,aialert";
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

// >>> FOLLOW v1.1 (shared block: identical in every scripted combat drone, synced by tools/follow_sync.py) >>>
// FOLLOW mode ("follow" or "escort" command, Start Mode FOLLOW, or launch Mode=FOLLOW): the drone stays with its owner instead of
// holding a fixed home point (GUARD).
// The owner is found two ways:
//  * SHIP: whatever broadcasts the home point, i.e. a ship running a carrier link (HornetMothership, LongbowMothership, GnatMothership,
//    or any script sending "<Tag>_HOME" or "<fleet>_HOME"). The owner's velocity comes from successive position fixes, so older link
//    scripts work too. The link's "<Tag>_HOMER" / "<fleet>_HOMER" radius keeps the drones outside its hull (else Owner Radius).
//  * ON FOOT: the owner's own character, when a drone sensor sees it (about 50 m). It wins while seen, so stepping out of the ship
//    near the drones hands the escort to you.
// What the drone does:
//  * flies its own slot on a ring behind the owner (upper half only in gravity), outside the hull, and matches the owner's velocity;
//    a straight line through the hull is replaced by a detour round it, near the hull it never closes in, and on a collision
//    course with the owner (up to 8 s ahead) it sidesteps;
//  * engages only targets within Engage Radius of the owner, and drops a fight that takes it beyond Leash;
//  * the ring widens (up to 2x) while the owner manoeuvres hard (over 3 m/s2), so a ship that out-turns the drones does not run into them;
//  * link lost: keeps flying the owner's last track for Coast seconds (10 s on foot), then holds that point until the owner is heard again.
// SATELLITE formation (v1.1, command "satellite" / "sat" / "orbit", or [Follow] Formation=SATELLITE): the same owner tracking, hull clearance,
// engage/leash and return logic as FOLLOW, but the drones spread evenly round a VERTICAL CIRCLE centred on the owner and the whole ring turns
// slowly (Orbit Period). Ring radius = Satellite Distance, grown so neighbours stay a wingmate separation apart (more drones = a bigger
// ring), never inside the hull + 45 m, capped at 600 m. Plane: ACROSS = square to the owner's heading (a halo round the
// ship, covers all sides), ALONG = side-on (contains the heading). The ring turns with the owner's (lagged) heading. A drone that engages a
// target leaves its slot (FOLLOW combat rules); when the target is dead it flies back to its slot, which has kept turning. Drones agree on
// the ring phase without a clock: each averages the phase implied by the mates that sit on the ring. In gravity only the upper arc is used
// and the drones sway instead of orbiting. "follow" / "escort" switch back to the tail ring.
// Own [Follow] section in Custom Data (written on first run, "reload" re-reads it).
// Hooks: FollowLoad() after AiGateLoad(); FollowStep() every tick after ReadIGC(); FollowSensed(e) for each sensor detection and
// "Detect Owner" = FoFoot, "Detect Players" = TargetChars || FoFoot in ConfigureSensors(); first line of Home():
// "if (_mode == Mode.FOLLOW && FollowKnown()) return FollowPos();"; the target pick skips targets farther than FoEngage from Home();
// idle translation "want = FollowWant(me, aBrake, Separation(me)) + Evade();"; with a target
// "if (_mode == Mode.FOLLOW && FollowFar(me)) want = FollowBack(me);"; speed cap MaxSpeed in FOLLOW; FollowStatus() in the status.
double FoDist = 60, FoFootDist = 20, FoRadius = 25, FoEngage = 800, FoLeash = 1200, FoCoast = 3;
bool FoFoot = true, FoSat = false, FoSatAlong = false;
double FoSatDist = 400, FoSatPeriod = 180, _foSatPh = 0, _foSatT = -99, _foSatR = 400;
Vector3D _foSlotV;
Vector3D _foP, _foV, _foLinkP, _foLinkV, _foFwd, _foUp;
double _foT = -99, _foLinkT = -99, _foFootT = -99, _foR = 0, _foRT = -99, _foPhase = 0, _foAcc = 0;
bool _foFrame = false, _foOnFoot = false;
IMyBroadcastListener _foLR1, _foLR2; string _foKey = "";

string FoK(MyIni i, string k, string d) { if (!i.ContainsKey("Follow", k)) i.Set("Follow", k, d); return i.Get("Follow", k).ToString(d); }
double FoD(MyIni i, string k, double d, double lo) { if (!i.ContainsKey("Follow", k)) i.Set("Follow", k, d); return Math.Max(lo, i.Get("Follow", k).ToDouble(d)); }

void FollowLoad()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    FoDist = FoD(ini, "Slot Distance", FoDist, 10);
    FoFootDist = FoD(ini, "On Foot Distance", FoFootDist, 5);
    FoRadius = FoD(ini, "Owner Radius", FoRadius, 0);
    FoEngage = FoD(ini, "Engage Radius", FoEngage, 50);
    FoLeash = Math.Max(FoEngage, FoD(ini, "Leash", FoLeash, 0));
    FoCoast = FoD(ini, "Coast After Link Loss (s)", FoCoast, 0);
    FoFoot = FoK(ini, "Follow Owner On Foot", FoFoot ? "True" : "False").ToLowerInvariant() != "false";
    string fm = FoK(ini, "Formation (FOLLOW/SATELLITE)", FoSat ? "SATELLITE" : "FOLLOW").Trim().ToUpperInvariant();
    FoSat = fm == "SATELLITE" || fm == "SAT" || fm == "ORBIT";
    FoSatDist = FoD(ini, "Satellite Distance (m)", FoSatDist, 30);
    FoSatPeriod = FoD(ini, "Satellite Orbit Period (s)", FoSatPeriod, 0);
    FoSatAlong = FoK(ini, "Satellite Ring (ACROSS/ALONG)", "ACROSS").Trim().ToUpperInvariant() == "ALONG";
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    int h = 0; foreach (char c in Tag) h = (h * 31 + c) % 997;
    _foPhase = h / 997.0;   // drone types share no slot list: each type starts its ring at its own angle
    string key = Tag + "#" + _flChan;
    if (key == _foKey) return;
    _foKey = key;
    if (_foLR1 != null) IGC.DisableBroadcastListener(_foLR1);
    if (_foLR2 != null) IGC.DisableBroadcastListener(_foLR2);
    _foLR1 = IGC.RegisterBroadcastListener(Tag + "_HOMER");
    _foLR2 = _flChan != "" ? IGC.RegisterBroadcastListener(_flChan + "_HOMER") : null;
}

void FoReadR(IMyBroadcastListener l)
{
    while (l != null && l.HasPendingMessage)
    {
        var m = l.AcceptMessage();
        if (!(m.Data is double)) continue;
        double r = (double)m.Data;
        if (r > 0 && r < 5000) { _foR = r; _foRT = _now; }
    }
}

// commands: satellite / sat / orbit -> FoSetSat(true); follow / escort -> FoSetSat(false). Written to Custom Data so it survives a recompile.
void FoSetSat(bool on)
{
    if (on != FoSat) _foFrame = false;
    FoSat = on;
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    ini.Set("Follow", "Formation (FOLLOW/SATELLITE)", on ? "SATELLITE" : "FOLLOW");
    Me.CustomData = ini.ToString();
}

void FoSet(Vector3D p, Vector3D v, bool foot) { _foP = p; _foV = v; _foT = _now; _foOnFoot = foot; }

void FollowStep()
{
    if (FoSat)
    {
        double sdt = _now - _foSatT;
        if (sdt > 0 && sdt < 1 && FoSatPeriod > 1) _foSatPh += 2 * Math.PI * sdt / FoSatPeriod;
        _foSatT = _now;
    }
    FoReadR(_foLR1); FoReadR(_foLR2);
    if (_dynHomeT > _foLinkT)   // a new position fix from the carrier link
    {
        double dt = _dynHomeT - _foLinkT;
        if (dt > 0.05 && dt < 2)
        {
            Vector3D raw = (_dynHome - _foLinkP) / dt;
            if (raw.LengthSquared() > 150 * 150) _foLinkV = Vector3D.Zero;   // a jump (another carrier, or a respawn): no speed from it
            else
            {
                Vector3D nv = _foLinkV + (raw - _foLinkV) * 0.5;
                _foAcc += (Math.Min(30, (nv - _foLinkV).Length() / dt) - _foAcc) * 0.15;   // how hard the owner manoeuvres (m/s2, smoothed)
                _foLinkV = nv;
            }
        }
        else if (dt >= 2) _foLinkV = Vector3D.Zero;
        _foLinkP = _dynHome; _foLinkT = _dynHomeT;
        if (_now - _foFootT > 1.0) FoSet(_foLinkP, _foLinkV, false);   // the owner on foot wins while the sensor sees them
    }
    if (_rc == null || !FollowKnown()) return;
    // slot frame: forward = the owner's heading (kept while it stops), up = away from gravity (in space the frame's own up)
    Vector3D g = _rc.GetNaturalGravity();
    if (!_foFrame)
    {   // FOLLOW: start with the drone "behind" the owner (no slot on the far side of the hull). SATELLITE: every drone starts from the same
        // world axes, so all of them share one ring plane until the owner moves and turns the frame
        if (FoSat) { _foFwd = Vector3D.Right; _foUp = Vector3D.Up; _foFrame = true; return; }
        Vector3D to = _foP - _rc.GetPosition();
        _foFwd = to.LengthSquared() > 1 ? Vector3D.Normalize(to) : _rc.WorldMatrix.Forward; _foUp = _rc.WorldMatrix.Up; _foFrame = true;
    }
    if (g.LengthSquared() > 0.1) _foUp = -Vector3D.Normalize(g);
    Vector3D v = FollowVel();
    if (v.LengthSquared() > 9) _foFwd = Vector3D.Normalize(_foFwd + (Vector3D.Normalize(v) - _foFwd) * 0.005);   // ~3 s lag: a weaving owner does not swing the ring
    Vector3D right = Vector3D.Cross(_foFwd, _foUp);
    if (right.LengthSquared() < 0.01) right = _rc.WorldMatrix.Right;   // owner flying straight up or down
    right.Normalize();
    _foUp = Vector3D.Normalize(Vector3D.Cross(right, _foFwd));
    if (g.LengthSquared() > 0.1 && Vector3D.Dot(_foUp, g) > 0) _foUp = -_foUp;
}

void FollowSensed(MyDetectedEntityInfo e)
{
    if (!FoFoot || e.IsEmpty() || e.Type != MyDetectedEntityType.CharacterHuman || e.Relationship != MyRelationsBetweenPlayerAndBlock.Owner) return;
    _foFootT = _now;
    FoSet(e.Position, new Vector3D(e.Velocity), true);
}

bool FollowKnown() { return _foT > -50; }
// on foot the drones keep walking your last heading for 10 s (their sensors must get back within ~50 m of you)
double FoCoastNow() { return _foOnFoot ? Math.Max(FoCoast, 10) : FoCoast; }
Vector3D FollowPos() { return _foP + _foV * Math.Min(_now - _foT, FoCoastNow()); }
Vector3D FollowVel() { return _now - _foT <= FoCoastNow() ? _foV : Vector3D.Zero; }
double FollowRadius() { return _foOnFoot ? 1 : (_now - _foRT < 10 ? _foR : FoRadius); }

// This drone's slot: rank among the fresh hive mates of the same type (by grid id) picks the angle on the ring. Ring distance: Slot
// Distance, at least the hull radius + 30 m and wide enough that neighbours sit outside SepRange; on foot On Foot Distance, kept
// inside the sensor's reach (40 m) so the drones keep seeing you.
double _foSlotR = 60;
Vector3D FollowSlot()
{
    int n = 1, k = 0; long me = Me.CubeGrid.EntityId;
    foreach (var m in _mates.Values) { if (_now - m.Seen > 3) continue; n++; if (m.Id < me) k++; }
    bool grav = _rc.GetNaturalGravity().LengthSquared() > 0.1;
    if (FoSat) return FoSatSlot(n, k, grav);
    _foSlotV = Vector3D.Zero;
    double th = grav ? Math.PI * (k + 0.15 + 0.7 * _foPhase) / n : Math.PI * 0.5 + 2 * Math.PI * (k + 0.7 * _foPhase) / n;
    double gap = Math.Max(SepRange * 1.1, 30) / (2 * Math.Sin((grav ? Math.PI * 0.5 : Math.PI) / Math.Max(n, 2)));
    _foSlotR = _foOnFoot ? Math.Min(40, Math.Max(FoFootDist, n * 4)) : Math.Max(Math.Max(FoDist, FollowRadius() + 30), n > 1 ? gap / 0.866 : 0);
    if (!_foOnFoot) _foSlotR *= 1 + MathHelper.Clamp((_foAcc - 3) / 6, 0, 1);   // the owner throws itself around: up to twice the room
    Vector3D right = Vector3D.Cross(_foFwd, _foUp);
    Vector3D side = right * Math.Cos(th) + _foUp * Math.Sin(th);
    return FollowPos() + (-_foFwd * 0.5 + side * 0.866) * _foSlotR;   // 60 deg off the owner's tail
}

// SATELLITE slot k of n on the vertical ring (see the header). Mates are ranked by grid id; every drone's rank is the same as seen by the others.
double FoSatBase(int k, int n, bool grav) { return grav ? Math.PI * (k + 0.5) / n : 2 * Math.PI * (k + 0.5 * _foPhase) / n; }
Vector3D FoSatSlot(int n, int k, bool grav)
{
    Vector3D c = FollowPos(), a = FoSatAlong ? _foFwd : Vector3D.Cross(_foFwd, _foUp), nrm = Vector3D.Cross(a, _foUp);
    if (!grav)
    {   // phase consensus: the ring phase each fresh mate sitting on the ring implies, averaged (circular mean) with my own
        double sx = Math.Cos(_foSatPh), sy = Math.Sin(_foSatPh); long me = Me.CubeGrid.EntityId;
        foreach (var m in _mates.Values)
        {
            double age = _now - m.Seen; if (age > 3) continue;
            int r = me < m.Id ? 1 : 0; foreach (var q in _mates.Values) if (_now - q.Seen <= 3 && q.Id < m.Id) r++;
            Vector3D d = m.Pos + m.Vel * age - c; double x = Vector3D.Dot(d, a), y = Vector3D.Dot(d, _foUp), L = Math.Sqrt(x * x + y * y);
            if (Math.Abs(L - _foSatR) > 0.3 * _foSatR || Math.Abs(Vector3D.Dot(d, nrm)) > 0.3 * _foSatR) continue;   // off the ring (fighting)
            double ph = Math.Atan2(y, x) - FoSatBase(r, n, false); sx += Math.Cos(ph); sy += Math.Sin(ph);
        }
        double dp = Math.Atan2(sy, sx) - _foSatPh; dp -= 2 * Math.PI * Math.Floor((dp + Math.PI) / (2 * Math.PI));
        _foSatPh += dp * 0.02;
    }
    double gap = Math.Max(SepRange * 1.1, 40);
    double R = _foOnFoot ? Math.Min(40, Math.Max(FoFootDist, n * 4))
        : Math.Min(600, Math.Max(Math.Max(FoSatDist, FollowRadius() + 45), n > 1 ? gap / (2 * Math.Sin((grav ? Math.PI * 0.5 : Math.PI) / Math.Max(n, 2))) : 0));
    if (!_foOnFoot) R *= 1 + MathHelper.Clamp((_foAcc - 3) / 6, 0, 1);   // the owner throws itself around: up to twice the room
    _foSatR = R; _foSlotR = R;
    double w = FoSatPeriod > 1 ? 2 * Math.PI / FoSatPeriod : 0;
    double th = FoSatBase(k, n, grav);
    if (grav) th += 0.2 * Math.PI / n * Math.Sin(_foSatPh);   // sway by a fifth of the slot spacing: neighbours cannot meet
    else th += _foSatPh;
    _foSlotV = grav ? Vector3D.Zero : (a * -Math.Sin(th) + _foUp * Math.Cos(th)) * (R * w);
    return c + (a * Math.Cos(th) + _foUp * Math.Sin(th)) * R;
}

// sep = the script's wingmate separation (SepRange); on foot the ring is smaller than that, so a 15 m push replaces it.
Vector3D FollowWant(Vector3D me, double aBrake, Vector3D sep)
{
    Vector3D o = FollowPos(), s = FollowSlot();
    double clr = FollowRadius() + (_foOnFoot ? 6 : 15);
    Vector3D goal = s, seg = s - me;
    double sl2 = seg.LengthSquared();
    if (sl2 > 1)
    {   // the straight line to the slot crosses the hull: aim beside the hull first
        double u = Vector3D.Dot(o - me, seg) / sl2;
        if (u > 0 && u < 1)
        {
            Vector3D oc = me + seg * u - o;
            if (oc.LengthSquared() < clr * clr)
            {
                if (oc.LengthSquared() < 0.25) oc = Vector3D.Cross(seg, Vector3D.Cross(_foUp, seg));
                if (oc.LengthSquared() < 0.25) oc = _foUp;
                goal = o + Vector3D.Normalize(oc) * (clr + 10);
            }
        }
    }
    Vector3D d = goal - me; double dist = d.Length();
    double v = Math.Min(Math.Min(MaxSpeed, dist * 0.6), Math.Sqrt(2 * aBrake * dist));
    Vector3D w = FollowVel() + _foSlotV + (dist > 0.5 ? d / dist * v : Vector3D.Zero);
    Vector3D om = me - o; double oml = om.Length(), band = Math.Max(clr, Math.Min(clr + 25, _foSlotR - 8));
    if (oml < band)
    {   // near the hull: no closing in, and get out of the way of an owner turning into the drone
        Vector3D on = oml > 0.5 ? om / oml : _foUp;
        double closing = -Vector3D.Dot(_rc.GetShipVelocities().LinearVelocity - FollowVel(), on);
        double outw = (band - oml) * 0.8 + Math.Max(0, closing), cur = Vector3D.Dot(w - FollowVel(), on);
        if (cur < outw) w += on * (outw - cur);
    }
    Vector3D rv = _rc.GetShipVelocities().LinearVelocity - FollowVel();
    double rv2 = rv.LengthSquared();
    if (rv2 > 4)
    {   // on a collision course with the owner (it out-turns us, or cuts across our path): sidestep early, braking is too late
        double tc = -Vector3D.Dot(om, rv) / rv2;
        if (tc > 0 && tc < 8)
        {
            Vector3D miss = om + rv * tc; double ml = miss.Length(), need = clr + 15;
            if (ml < need)
            {
                Vector3D side = ml > 0.5 ? miss / ml : Vector3D.Cross(rv, _foUp);
                if (side.LengthSquared() < 0.01) side = Vector3D.Cross(rv, _rc.WorldMatrix.Right);
                w += Vector3D.Normalize(side) * Math.Min(MaxSpeed, (need - ml) / Math.Max(tc, 0.5) * 2);
            }
        }
    }
    if (!_foOnFoot) return w + sep;
    foreach (var m in _mates.Values)
    {
        double age = _now - m.Seen; if (age > 1.5) continue;
        Vector3D dm = me - (m.Pos + m.Vel * age); double len = dm.Length();
        if (len < 15 && len > 0.5) w += dm / len * (8 * (1 - len / 15));
    }
    return w;
}

bool FollowFar(Vector3D me) { return FollowKnown() && Vector3D.DistanceSquared(me, FollowPos()) > FoLeash * FoLeash; }
Vector3D FollowBack(Vector3D me) { Vector3D d = FollowPos() - me; return FollowVel() + (d.LengthSquared() > 1 ? Vector3D.Normalize(d) * MaxSpeed : Vector3D.Zero); }

string FollowStatus()
{
    if (_mode != Mode.FOLLOW) return "";
    if (!FollowKnown()) return "Follow : no owner yet (link script on your ship" + (FoFoot ? ", or stand within ~50 m" : "") + ")\n";
    double age = _now - _foT;
    string s = (FoSat ? "Satellite : ring " + _foSatR.ToString("F0") + " m, " + (_mates.Count + 1) + " ships, " : "Follow : ") + (_foOnFoot ? "on foot" : "ship") + (age > 1.5 ? " LOST " + age.ToString("F0") + " s" : "") + "  " + FollowVel().Length().ToString("F0") + " m/s";
    if (_rc != null) s += "  " + Vector3D.Distance(_rc.GetPosition(), FollowPos()).ToString("F0") + " m away";
    return s + "\n";
}
// <<< FOLLOW <<<

// >>> LAUNCH v1.1 (shared block: identical in every drone script, see docs/launch-protocol.md) >>>
// Automatic take-off for drones that were printed on (merged to) a printer. A drone must leave and start working WITHOUT depending on one message:
//   * it notices on its own that the merge pair opened (no connected merge block on its grid any more, or the grid changed) and then
//   * burns away from the printer for "Burst Seconds" (gyros held at zero rate, thrusters pushed along the Away vector, dampeners off),
//   * clears the merge/antenna/thruster/gyro/battery switches that welding may have left off, and
//   * switches the script into its configured mode (the script's Start Mode, or [Launch] Mode when that is IDLE).
// Other triggers that do the same: Custom Data [Launch] Armed=true while the PB is not docked (a PB recompiled after the release),
// the argument "armed" (printer arms it while the drone is still on its grid), the argument "launch", a "<Tag>_JOIN" broadcast or a
// FLEET "<ch>_CMD" string "join" (only for an IDLE, complete, undocked drone), and a docked -> undocked change seen by the PB itself.
// Custom Data (own [Launch] section; defaults are written on the first run, "reload" re-reads them):
//   Auto=true  Burst Seconds=4  Away X/Y/Z=0 (world vector away from the printer, zero = unknown)  Armed=false  Clear Distance=8
//   Max Speed=25  Mode=GUARD
// Script hooks (the only lines a script needs): LaunchLoad() after LoadConfig() and FleetLoad() (constructor and "reload"); in Command():
//   case "armed": case "launch": case "disarm": LaunchCmd(word); break;   at the very top of Control(dt) (or the tick, before any thrust):
//   if (LaunchStep(dt, <mode is IDLE>, StartMode, <home known>, <home point>, Command)) return;    LaunchStatus() in the display text.
// The block needs the script's fields: Tag (IGC tag) and _flChan (FLEET LINK block); everything else is its own.
const string LnSec = "Launch";
int _lnS;                                   // 0 free, 1 docked and armed (waiting for the separation), 2 take-off burn, 3 launched
bool _lnFused, _lnAuto = true, _lnArmed, _lnHold, _lnForce, _lnLocOk, _lnDock, _lnHomeOk, _lnPending;
double _lnBurst = 4, _lnClear = 8, _lnSpeed = 25, _lnClock, _lnT, _lnJoinT = -999;
string _lnModeCfg = "GUARD", _lnMsg = "", _lnJKey = "";
Vector3D _lnAwayCfg, _lnAwayLoc, _lnDir, _lnP0, _lnHome;
IMyCubeGrid _lnGrid;
long _lnTick; int _lnFree, _lnWait;
IMyBroadcastListener _lnJ1, _lnJ2;
List<IMyShipMergeBlock> _lnMg = new List<IMyShipMergeBlock>();
List<IMyThrust> _lnThr = new List<IMyThrust>();
List<IMyGyro> _lnGy = new List<IMyGyro>();
IMyShipController _lnCtl;

void LaunchLoad()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    if (!ini.ContainsKey(LnSec, "Auto")) ini.Set(LnSec, "Auto", true);
    if (!ini.ContainsKey(LnSec, "Burst Seconds")) ini.Set(LnSec, "Burst Seconds", _lnBurst);
    if (!ini.ContainsKey(LnSec, "Away X")) ini.Set(LnSec, "Away X", 0.0);
    if (!ini.ContainsKey(LnSec, "Away Y")) ini.Set(LnSec, "Away Y", 0.0);
    if (!ini.ContainsKey(LnSec, "Away Z")) ini.Set(LnSec, "Away Z", 0.0);
    if (!ini.ContainsKey(LnSec, "Armed")) ini.Set(LnSec, "Armed", false);
    if (!ini.ContainsKey(LnSec, "Clear Distance")) ini.Set(LnSec, "Clear Distance", _lnClear);
    if (!ini.ContainsKey(LnSec, "Max Speed")) ini.Set(LnSec, "Max Speed", _lnSpeed);
    if (!ini.ContainsKey(LnSec, "Mode")) ini.Set(LnSec, "Mode", _lnModeCfg);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    _lnAuto = ini.Get(LnSec, "Auto").ToBoolean(true);
    _lnBurst = MathHelper.Clamp(ini.Get(LnSec, "Burst Seconds").ToDouble(4), 0.5, 20);
    _lnClear = MathHelper.Clamp(ini.Get(LnSec, "Clear Distance").ToDouble(8), 1, 200);
    _lnSpeed = MathHelper.Clamp(ini.Get(LnSec, "Max Speed").ToDouble(25), 3, 200);
    _lnModeCfg = ini.Get(LnSec, "Mode").ToString("GUARD");
    _lnAwayCfg = new Vector3D(ini.Get(LnSec, "Away X").ToDouble(0), ini.Get(LnSec, "Away Y").ToDouble(0), ini.Get(LnSec, "Away Z").ToDouble(0));
    if (ini.Get(LnSec, "Armed").ToBoolean(false)) _lnArmed = true;
    string fl = _flChan != "" ? _flChan + "_CMD" : "";
    string key = Tag + "|" + fl;
    if (key != _lnJKey)
    {
        _lnJKey = key;
        if (_lnJ1 != null) IGC.DisableBroadcastListener(_lnJ1);
        if (_lnJ2 != null) IGC.DisableBroadcastListener(_lnJ2);
        _lnJ1 = IGC.RegisterBroadcastListener(Tag + "_JOIN");
        _lnJ2 = fl != "" ? IGC.RegisterBroadcastListener(fl) : null;
    }
}

void LnWrite(bool armed)
{
    var ini = new MyIni(); MyIniParseResult r;
    if (!ini.TryParse(Me.CustomData, out r)) return;
    ini.Set(LnSec, "Armed", armed);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}

void LaunchCmd(string w)
{
    switch (w)
    {
        case "armed": LaunchLoad(); _lnHold = false; _lnArmed = true; LnWrite(true); if (_lnS == 3) _lnS = 0; break;
        case "launch": if (_lnS < 2) _lnForce = true; break;
        case "disarm": _lnHold = true; _lnArmed = false; _lnForce = false; if (_lnS == 1) _lnS = 0; LnWrite(false); break;
    }
}

void LnRead()
{
    if (_lnJ1 != null) while (_lnJ1.HasPendingMessage) { _lnJ1.AcceptMessage(); _lnJoinT = _lnClock; }
    if (_lnJ2 != null)
        while (_lnJ2.HasPendingMessage)
        {
            var m = _lnJ2.AcceptMessage();
            if (m.Data is string && ((string)m.Data).Trim().ToLowerInvariant() == "join") _lnJoinT = _lnClock;
        }
}

// is a merge pair of this grid locked? Also learns the direction away from the partner block (the block of the pair nearest to the PB is the drone's).
bool LnDocked()
{
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_lnMg, b => b.CubeGrid == g && b.IsConnected);
    if (_lnMg.Count == 0) return false;
    if (_lnMg.Count >= 2)
    {
        Vector3D me = Me.GetPosition(); IMyShipMergeBlock d = null, p = null; double best = double.MaxValue;
        foreach (var m in _lnMg) { double q = (m.GetPosition() - me).LengthSquared(); if (q < best) { best = q; d = m; } }
        best = double.MaxValue;
        foreach (var m in _lnMg) if (m != d) { double q = (m.GetPosition() - d.GetPosition()).LengthSquared(); if (q < best) { best = q; p = m; } }
        Vector3D a = d.GetPosition() - p.GetPosition();
        if (a.LengthSquared() > 1e-4) { a.Normalize(); _lnAwayLoc = Vector3D.TransformNormal(a, MatrixD.Transpose(Me.WorldMatrix)); _lnLocOk = true; }
    }
    return true;
}

// every terminal block of the grid works and there is a thruster and a controller: a half-built drone must not take off on a stray "join"
bool LnComplete()
{
    var g = Me.CubeGrid; var l = new List<IMyTerminalBlock>(); bool thr = false, ctl = false;
    GridTerminalSystem.GetBlocksOfType(l, b => b.CubeGrid == g);
    foreach (var b in l) { if (!b.IsFunctional) return false; if (b is IMyThrust) thr = true; if (b is IMyShipController) ctl = true; }
    return thr && ctl;
}

// welded blocks may come off: switch on what the flight needs, the merge block off (it must never re-latch)
void LnPrep()
{
    var g = Me.CubeGrid;
    _lnThr.Clear(); _lnGy.Clear(); _lnCtl = null;
    GridTerminalSystem.GetBlocksOfType(_lnThr, b => b.CubeGrid == g);
    GridTerminalSystem.GetBlocksOfType(_lnGy, b => b.CubeGrid == g);
    foreach (var t in _lnThr) if (!t.Enabled) t.Enabled = true;
    foreach (var y in _lnGy) if (!y.Enabled) y.Enabled = true;
    var bats = new List<IMyBatteryBlock>(); GridTerminalSystem.GetBlocksOfType(bats, b => b.CubeGrid == g);
    foreach (var b in bats) { if (!b.Enabled) b.Enabled = true; if (b.ChargeMode != ChargeMode.Auto) b.ChargeMode = ChargeMode.Auto; }
    var ants = new List<IMyRadioAntenna>(); GridTerminalSystem.GetBlocksOfType(ants, b => b.CubeGrid == g);
    foreach (var a in ants) { if (!a.Enabled) a.Enabled = true; if (!a.EnableBroadcasting) a.EnableBroadcasting = true; }
    var guns = new List<IMyUserControllableGun>(); GridTerminalSystem.GetBlocksOfType(guns, b => b.CubeGrid == g);
    foreach (var b in guns) if (!b.Enabled) b.Enabled = true;
    var cams = new List<IMyCameraBlock>(); GridTerminalSystem.GetBlocksOfType(cams, b => b.CubeGrid == g);
    foreach (var b in cams) if (!b.Enabled) b.Enabled = true;
    var mgs = new List<IMyShipMergeBlock>(); GridTerminalSystem.GetBlocksOfType(mgs, b => b.CubeGrid == g);
    foreach (var b in mgs) if (b.Enabled) b.Enabled = false;
    var ctrls = new List<IMyShipController>(); GridTerminalSystem.GetBlocksOfType(ctrls, b => b.CubeGrid == g && b.CanControlShip);
    foreach (var c in ctrls) if (c is IMyRemoteControl) { _lnCtl = c; break; }
    if (_lnCtl == null && ctrls.Count > 0) _lnCtl = ctrls[0];
}

// the release is only complete when the grid has split (and this grid is no printer): while the printer's merge block is still on this grid the thrusters/gyros found here may be the printer's
bool LnReady()
{
    var g = Me.CubeGrid;
    // still fused to a printer (the Hive Mother PB or a [Bay block is on this grid): the thrusters and gyros found here are the printer's, never burn
    var pr = new List<IMyTerminalBlock>(); GridTerminalSystem.GetBlocksOfType(pr, b => b.CubeGrid == g && b != Me && (b.CustomName.Contains("[Bay") || (b is IMyProgrammableBlock && b.CustomData.Contains("[Hive Mother]"))));
    _lnFused = pr.Count > 0; if (_lnFused) return false;
    GridTerminalSystem.GetBlocksOfType(_lnMg, b => b.CubeGrid == g);
    if (_lnMg.Count < 2 || ++_lnWait > 9) return true;       // (gives up waiting after ~1.7 s)
    _lnPending = true; return false;
}

void LnBegin()
{
    _lnWait = 0; _lnPending = false;
    _lnForce = false; _lnS = 2; _lnT = 0; _lnJoinT = -999;
    LnPrep();
    Vector3D me = Me.GetPosition(), d = Vector3D.Zero; string src = "";
    Vector3D lw = _lnLocOk ? Vector3D.TransformNormal(_lnAwayLoc, Me.WorldMatrix) : Vector3D.Zero;
    if (_lnAwayCfg.LengthSquared() > 0.25 && !(_lnLocOk && Vector3D.Dot(Vector3D.Normalize(_lnAwayCfg), lw) < -0.2)) { d = _lnAwayCfg; src = "Custom Data"; }   // (a stored vector that points INTO the printer is ignored)
    else if (_lnLocOk) { d = lw; src = "merge pair"; }
    else if (LnOwnMerge(me, ref d)) src = "own merge block";
    else if (_lnHomeOk && (me - _lnHome).LengthSquared() > 25) { d = me - _lnHome; src = "home point"; }
    else { d = _lnCtl != null ? _lnCtl.WorldMatrix.Backward : Me.WorldMatrix.Backward; src = "own back"; }
    _lnDir = Vector3D.Normalize(d); _lnP0 = me;
    _lnMsg = "away from " + src;
}

// no printer information: the drone's own merge block sits on the side that faced the printer, so "from the merge block to the centre of mass" points away from it
bool LnOwnMerge(Vector3D me, ref Vector3D d)
{
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_lnMg, b => b.CubeGrid == g);
    if (_lnMg.Count == 0 || _lnCtl == null) return false;
    IMyShipMergeBlock mb = _lnMg[0];
    foreach (var m in _lnMg) if ((m.GetPosition() - me).LengthSquared() < (mb.GetPosition() - me).LengthSquared()) mb = m;
    Vector3D v = _lnCtl.CenterOfMass - mb.GetPosition();
    if (v.LengthSquared() < 0.09) return false;
    d = v; return true;
}

// one tick of the take-off: true = the script must not fly this tick
bool LnBurn(double dt, bool idle, string startMode, Action<string> cmd)
{
    _lnT += dt;
    double moved = Vector3D.Dot(Me.GetPosition() - _lnP0, _lnDir);
    double vAway = _lnCtl != null ? Vector3D.Dot(_lnCtl.GetShipVelocities().LinearVelocity, _lnDir) : 0;
    if (_lnT >= _lnBurst || (_lnT >= 0.5 && moved >= _lnClear && vAway > 1))
    {
        foreach (var t in _lnThr) t.ThrustOverridePercentage = 0;
        foreach (var y in _lnGy) { y.GyroOverride = false; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }
        if (_lnCtl != null) _lnCtl.DampenersOverride = true;
        _lnS = 3; _lnArmed = false; LnWrite(false);
        string m = startMode.Trim().ToUpperInvariant();
        if (m == "" || m == "IDLE" || m == "SENTRY") m = _lnModeCfg.Trim().ToUpperInvariant();   // (SENTRY, the default Start Mode, is for a pasted drone: a launched one takes [Launch] Mode)
        _lnMsg = "took off (" + _lnMsg + ", " + moved.ToString("0") + " m, " + _lnT.ToString("0.0") + " s), mode " + m;
        if (idle && m != "" && m != "IDLE") { cmd("home"); cmd(m.ToLowerInvariant()); }
        return false;
    }
    bool cut = vAway >= _lnSpeed;
    double best = -2;
    foreach (var t in _lnThr) if (t.IsFunctional) best = Math.Max(best, Vector3D.Dot(t.WorldMatrix.Backward, _lnDir));
    if (best <= 0.3 && best > -0.2)
    {   // no thruster pushes (nearly) along Away (e.g. a ship with only fore/aft thrusters and a sideways Away): burn along the best thruster axis, which is perpendicular to Away, never toward the printer
        Vector3D alt = Vector3D.Zero; double ab = -2;
        foreach (var t in _lnThr) if (t.IsFunctional) { double q = Vector3D.Dot(t.WorldMatrix.Backward, _lnDir); if (q > ab) { ab = q; alt = t.WorldMatrix.Backward; } }
        if (alt.LengthSquared() > 0.5) { _lnDir = Vector3D.Normalize(alt); _lnP0 = Me.GetPosition(); best = 1; _lnMsg += ", thrust axis"; }
    }
    if (best < 0) best = 0;
    foreach (var t in _lnThr)
    {
        double c = t.IsFunctional ? Vector3D.Dot(t.WorldMatrix.Backward, _lnDir) : 0;
        t.ThrustOverridePercentage = (!cut && best > 0.3 && c > 0.15) ? (float)Math.Max(0.01, c / best) : 0f;
    }
    foreach (var y in _lnGy) { y.GyroOverride = true; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }   // hold the attitude: a zero rate command
    if (_lnCtl != null) _lnCtl.DampenersOverride = false;
    return true;
}

bool LaunchStep(double dt, bool idle, string startMode, bool homeOk, Vector3D home, Action<string> cmd)
{
    _lnTick++; _lnClock += dt;
    _lnHomeOk = homeOk; _lnHome = home;
    LnRead();
    if (_lnS == 2) return LnBurn(dt, idle, startMode, cmd);
    if (_lnS == 3) return false;
    bool changed = Me.CubeGrid != _lnGrid;
    bool join = _lnClock - _lnJoinT < 3;                     // the printer repeats "join" every 2 s
    if (!(changed || (_lnForce && _lnTick % 10 == 0) || _lnTick % (_lnS == 1 || _lnPending ? 10 : 60) == 0 || (join && _lnTick % 10 == 0))) return false;   // (never more than 6 checks a second)
    _lnGrid = Me.CubeGrid;
    _lnDock = LnDocked(); _lnPending = false;
    if (_lnS == 0)
    {
        if (_lnDock)
        {
            if (!_lnHold) { _lnS = 1; _lnFree = 0; if (!_lnArmed) { _lnArmed = true; LnWrite(true); } }
            return false;
        }
        if (_lnHold && !_lnForce) return false;
        bool go = _lnForce || (_lnAuto && (_lnArmed || (join && idle && LnComplete())));
        if (go && LnReady()) { LnBegin(); return LnBurn(dt, idle, startMode, cmd); }
        return false;
    }
    if (_lnDock) { _lnFree = 0; return false; }              // state 1: still locked
    if (++_lnFree < 2 && !changed) return false;             // (two checks in a row, so that one odd reading does not launch)
    if (_lnAuto || _lnForce) { if (LnReady()) { LnBegin(); return LnBurn(dt, idle, startMode, cmd); } return false; }
    _lnS = 0;                                                // Auto=false: wait for the "launch" command
    return false;
}

string LaunchStatus()
{
    if (_lnS == 1) return "Launch: docked, waiting for the release\n";
    if (_lnS == 2) return "Launch: take-off burn " + _lnT.ToString("0.0") + " s\n";
    if (_lnS == 3) return _lnMsg != "" ? "Launch: " + _lnMsg + "\n" : "";
    if (_lnFused) return "Launch: held, this grid holds a printer\n";
    return _lnArmed ? "Launch: armed\n" : "";
}
// <<< LAUNCH <<<

// =====================================================================
//  HIVE MATES (lite): position and velocity of the wingmates, for the FOLLOW slots
class Mate { public long Id; public Vector3D Pos, Vel; public double Seen; public long Tgt; public int Flags; }
Dictionary<long, Mate> _mates = new Dictionary<long, Mate>(); List<long> _gone = new List<long>();
void ReadSwarm()
{
    while (_lSwarm.HasPendingMessage)
    {
        var m = _lSwarm.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, long, int>)m.Data;
        if (d.Item1 == Me.CubeGrid.EntityId) continue;
        Mate mt;
        if (!_mates.TryGetValue(d.Item1, out mt)) { mt = new Mate { Id = d.Item1 }; _mates[d.Item1] = mt; }
        mt.Pos = d.Item2; mt.Vel = d.Item3; mt.Tgt = d.Item4; mt.Flags = d.Item5; mt.Seen = _now;
    }
    _gone.Clear(); foreach (var kv in _mates) if (_now - kv.Value.Seen > 3) _gone.Add(kv.Key); foreach (var id in _gone) _mates.Remove(id);
}

// =====================================================================
//  SETUP
void CtorErr(string w, Exception ex) { _crash = "setup " + w + ": " + ex.Message; Echo(_crash); }

// >>> WEAKPOINT RELAY v1.0 (shared block: identical in every drone Staff PB, see docs/design/hive_weakpoint.md and docs/hive-protocol.md) >>>
// Fleet side of the shared breach point. The Flight PB sends its own findings on <Flight>.WPF; this PB puts them on the fleet channel <ch>_WP,
// collects the findings of the whole wing, and sends the best hole for the Flight's current target back on <Flight>.WPT (once a second).
//   <ch>_WP  MyTuple<long target, Vector3D s, Vector3D d, Vector3D info, long senderGrid, int flags>
//     s = hull entry and d = drill direction, both in the TARGET frame; info = (hole depth m, vital depth hint m or -1, age s)
//     flags: bits 0-2 kind (0 camera breach point, 1 PowerSystems, 2 Weapons, 3 Propulsion, 4 other), 8 seeker found it, 16 block position
//     from the game, 32 DONE (everybody drops the hole), 64 STALL (that sender gave up), 128 ADOPTED (the sender took it from the wing and its own ray confirmed it)
//     Staff -> Flight (.WPT): bits 8-11 = number of drones that support the hole.
// A drone sends only its OWN confirmed findings; a received hole is never forwarded (no echo). Holes within 4 m of each other are one hole; the best
// one wins (seeker kind by "Weakpoint Priority", then supporters, then depth, then the lowest grid id), so every drone picks the same hole.
// [Fleet] keys: Weakpoint Priority=1,3,2 (power, propulsion, weapons)   Weakpoint Expire (s)=20
// Script hooks: WpStep() in Tick after Relay(); _sb.Append(WpStatus()) in Display(). Uses: _fk, _flChan, _tgtId, _tick, _now, _f2sTick.
class WpE { public long Tgt, Snd; public Vector3D S, D, I; public int F; public double T; }
Dictionary<long, WpE> _wpMap = new Dictionary<long, WpE>();
IMyBroadcastListener _wpLf, _wpLw;
string _wpKey = null; int[] _wpPri = new int[] { 1, 3, 2 }; double _wpExp = 20, _wpFleetT = -99, _wpFlT = -99, _wpEvSendT = -99; WpE _wpBest; int _wpBestK;
List<WpE> _wpDone = new List<WpE>(); List<long> _wpDel = new List<long>(); MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> _wpLast;
bool WpOkT(MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> d)
{
    double dl = d.Item3.Length();
    return FleetV(d.Item2) && FleetV(d.Item4) && Math.Abs(d.Item2.X) < 1e4 && Math.Abs(d.Item2.Y) < 1e4 && Math.Abs(d.Item2.Z) < 1e4 && dl > 0.9 && dl < 1.1
        && d.Item4.X >= 0 && d.Item4.X <= 100 && d.Item4.Y < 500 && !double.IsNaN(d.Item4.X + d.Item4.Y + d.Item4.Z);
}
void WpCfg()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    const string S = "Fleet";
    if (!ini.ContainsKey(S, "Weakpoint Priority")) ini.Set(S, "Weakpoint Priority", "1,3,2");
    if (!ini.ContainsKey(S, "Weakpoint Expire (s)")) ini.Set(S, "Weakpoint Expire (s)", 20.0);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    var l = new List<int>();
    foreach (var p in ini.Get(S, "Weakpoint Priority").ToString("1,3,2").Split(',')) { int k; if (int.TryParse(p.Trim(), out k)) l.Add(k); }
    _wpPri = l.ToArray(); _wpExp = Math.Max(3, ini.Get(S, "Weakpoint Expire (s)").ToDouble(20));
}
int WpRank(WpE e)
{
    if ((e.F & 8) == 0) return 0;                           // camera breach points rank below anything a turret found
    int k = e.F & 7, i = Array.IndexOf(_wpPri, k);
    return i >= 0 ? 20 - i : 5;
}
WpE WpPut(long snd, MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> d)
{
    WpE e;
    if (!_wpMap.TryGetValue(snd, out e)) { if (_wpMap.Count >= 64) return null; e = new WpE(); _wpMap[snd] = e; }
    e.Tgt = d.Item1; e.Snd = snd; e.S = d.Item2; e.D = d.Item3; e.I = d.Item4; e.F = d.Item6; e.T = _now;
    return e;
}
void WpStep()
{
    string key = _flChan + "|" + _fk;
    if (key != _wpKey)
    {
        if (_wpKey != null) { if (_wpLf != null) IGC.DisableBroadcastListener(_wpLf); if (_wpLw != null) IGC.DisableBroadcastListener(_wpLw); }
        _wpKey = key; _wpMap.Clear(); _wpDone.Clear(); WpCfg();
        _wpLf = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".WPF") : null;
        _wpLw = _flChan != "" ? IGC.RegisterBroadcastListener(_flChan + "_WP") : null;
    }
    else if (_tick % 600 == 1) WpCfg();
    long me = Me.CubeGrid.EntityId;
    // my Flight PB -> map, and on to the fleet channel (changes and events at once, else every 2 s)
    while (_wpLf != null && _wpLf.HasPendingMessage)
    {
        var m = _wpLf.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data;
        if (!WpOkT(d)) continue;
        d.Item5 = me;
        if ((d.Item6 & 96) != 0) WpEvent(d, me);
        else WpPut(me, d);
        bool changed = d.Item1 != _wpLast.Item1 || d.Item6 != _wpLast.Item6 || (d.Item2 - _wpLast.Item2).LengthSquared() > 1;
        if (_wpLw != null && (changed || (d.Item6 & 96) != 0 || _now - _wpFleetT >= 2)) { IGC.SendBroadcastMessage(_flChan + "_WP", d); _wpFleetT = _now; _wpLast = d; }
    }
    // the wing -> map
    while (_wpLw != null && _wpLw.HasPendingMessage)
    {
        var m = _wpLw.AcceptMessage();
        if (m.Source == IGC.Me || !(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data;
        if (d.Item5 == me || d.Item5 == 0 || !WpOkT(d)) continue;
        if ((d.Item6 & 96) != 0) WpEvent(d, d.Item5); else WpPut(d.Item5, d);
    }
    _wpDel.Clear();
    foreach (var kv in _wpMap) if (_now - kv.Value.T > _wpExp) _wpDel.Add(kv.Key);
    foreach (var k in _wpDel) _wpMap.Remove(k);
    for (int i = _wpDone.Count - 1; i >= 0; i--) if (_now - _wpDone[i].T > 3) _wpDone.RemoveAt(i);
    if (_tick % 60 != 3) return;
    // best hole for the Flight's target
    _wpBest = null; _wpBestK = 0;
    if (_tgtId != 0)
    {
        long bs = 0; int br = -1, bk = 0; double bd = -1; WpE bb = null;
        foreach (var a in _wpMap.Values)
        {
            if (a.Tgt != _tgtId) continue;
            int k = 0; double dep = 0; int rank = WpRank(a); long low = a.Snd; WpE rep = a;
            foreach (var b in _wpMap.Values)
            {
                if (b.Tgt != _tgtId || (b.S - a.S).LengthSquared() > 16) continue;
                k++; dep = Math.Max(dep, b.I.X); int rb = WpRank(b); if (rb > rank) { rank = rb; rep = b; } if (b.Snd < low) low = b.Snd;
            }
            if (rank > br || (rank == br && (k > bk || (k == bk && (dep > bd || (dep == bd && low < bs))))))
            { br = rank; bk = k; bd = dep; bs = low; bb = rep; }
        }
        if (bb != null) { _wpBest = bb; _wpBestK = bk; _wpBestDep = bd; }
    }
    if (_fk == "" || _tick - _f2sTick > 90) return;
    if (_wpBest != null)
        IGC.SendBroadcastMessage(_fk + ".WPT", new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(_wpBest.Tgt, _wpBest.S, _wpBest.D,
            new Vector3D(_wpBestDep, _wpBest.I.Y, _now - _wpBest.T + _wpBest.I.Z), _wpBest.Snd, (_wpBest.F & 31) | (Math.Min(_wpBestK, 15) << 8)), TransmissionDistance.CurrentConstruct);
    foreach (var e in _wpDone)
        if (e.Tgt == _tgtId) IGC.SendBroadcastMessage(_fk + ".WPT", new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(e.Tgt, e.S, e.D, e.I, e.Snd, 32), TransmissionDistance.CurrentConstruct);
}
double _wpBestDep;
// DONE: every hole within 4 m of it is gone (and the Flight is told for 3 s); STALL: that sender's support is gone
void WpEvent(MyTuple<long, Vector3D, Vector3D, Vector3D, long, int> d, long snd)
{
    if ((d.Item6 & 32) != 0)
    {
        _wpDel.Clear();
        foreach (var kv in _wpMap) if (kv.Value.Tgt == d.Item1 && (kv.Value.S - d.Item2).LengthSquared() < 16) _wpDel.Add(kv.Key);
        foreach (var k in _wpDel) _wpMap.Remove(k);
        var e = new WpE(); e.Tgt = d.Item1; e.Snd = snd; e.S = d.Item2; e.D = d.Item3; e.I = d.Item4; e.F = 32; e.T = _now;
        _wpDone.Add(e); if (_wpDone.Count > 8) _wpDone.RemoveAt(0);
    }
    else _wpMap.Remove(snd);
}
string WpStatus()
{
    if (_wpBest == null) return _wpMap.Count > 0 ? "Weakpt : " + _wpMap.Count + " report(s), none for the target\n" : "";
    string[] kn = { "breach point", "power", "weapons", "propulsion", "block" };
    return "Weakpt : " + kn[Math.Min(_wpBest.F & 7, 4)] + " hole " + _wpBestDep.ToString("0") + " m, " + _wpBestK + " drone(s), age " + (_now - _wpBest.T).ToString("0") + " s\n";
}
// <<< WEAKPOINT RELAY v1.0 <<<

public Program()
{
    try { GetBlocks(); } catch (Exception ex) { CtorErr("blocks", ex); }
    try { string m = StartMode.Trim().ToUpperInvariant(); if (m == "SATELLITE" || m == "SAT" || m == "ORBIT") FoSat = true; } catch (Exception ex) { CtorErr("mode", ex); }
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

// the Flight PB's [Harrier] keys that the Staff shares (read on load, every 10 s and on "reload": no duplicated keys, no tag mismatch)
void LoadFlightCfg()
{
    if (_fpb == null) return;
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(_fpb.CustomData, out res)) return;
    Tag = ini.Get("Harrier", "IGC Tag").ToString(Tag);
    StartMode = ini.Get("Harrier", "Start Mode (GUARD/HUNT/ASSIST/IDLE)").ToString(StartMode);
    MaxSpeed = ini.Get("Harrier", "Combat Speed").ToDouble(MaxSpeed);
    SepRange = ini.Get("Harrier", "Wingmate Separation Range").ToDouble(SepRange);
}

void GetBlocks()
{
    var ctrls = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(ctrls, b => b.IsSameConstructAs(Me) && b.CanControlShip);
    _rc = null;
    foreach (var c in ctrls) if (c is IMyRemoteControl) { _rc = c; break; }
    if (_rc == null && ctrls.Count > 0) _rc = ctrls[0];
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_gyros, b => b.CubeGrid == g);
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.CubeGrid == g);
    GridTerminalSystem.GetBlocksOfType(_guns, b => b.CubeGrid == g && !(b is IMyLargeTurretBase));
    GridTerminalSystem.GetBlocksOfType(_sensors, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_ai, b => b.CubeGrid == g && (b.CustomName.Contains("[AI]") || b.BlockDefinition.ToString().Contains("OffensiveCombat") || b.BlockDefinition.ToString().Contains("FlightMovement")));
    var fl = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(fl, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Flight]", StringComparison.OrdinalIgnoreCase));
    var oldK = _fk; _fpb = null; double best = double.MaxValue;
    foreach (var p in fl) { if (p.CubeGrid != g) continue; double q = (p.GetPosition() - Me.GetPosition()).LengthSquared(); if (q < best) { best = q; _fpb = p; } }
    _fk = _fpb != null ? "WSPL" + _fpb.EntityId : "";
    if (_fk != oldK) { _f2sN = 0; _f2sTick = _tick; _down = false; }
    LoadFlightCfg();
    _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.FontSize = 0.75f; _lcd.Font = "Monospace";
    string sig = Tag + "|" + _fk;
    if (sig != _sig) { _sig = sig; LoadMods(); }
    Listen();
}

void LoadMods() { FleetLoad(true); LaunchLoad(); FollowLoad(); }

void Listen()
{
    string k = Tag + "|" + _fk;
    if (k == _lsKey) return;
    _lsKey = k;
    foreach (var l in new[] { _lHome, _lCmd, _lSwarm, _lF2S, _lFlt, _lArg }) if (l != null) IGC.DisableBroadcastListener(l);
    _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME"); _lCmd = IGC.RegisterBroadcastListener(Tag + "_CMD"); _lSwarm = IGC.RegisterBroadcastListener(Tag + "_SWARM");
    _lF2S = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".F2S") : null; _lFlt = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".FLT") : null; _lArg = _fk != "" ? IGC.RegisterBroadcastListener(_fk + ".CMD") : null;
}

// =====================================================================
public void Main(string arg, UpdateType src)
{
    try { Tick(arg, src); if (_errRun > 0 && _tick % 300 == 0) { _errRun = 0; _crash = ""; } }
    catch (Exception ex)
    {
        if (_crash != ex.Message || _errRun++ % 60 == 0) { _crash = ex.Message; Echo("Script error: " + ex.Message); if (_lcd != null) _lcd.WriteText("STAFF SCRIPT ERROR\n" + ex.Message + "\nSend this text to Claude."); }
    }
}

void Tick(string arg, UpdateType src)
{
    if ((src & (UpdateType.Trigger | UpdateType.Terminal | UpdateType.Script | UpdateType.Mod)) != 0 && !string.IsNullOrWhiteSpace(arg)) Command(arg);
    if ((src & UpdateType.Update1) == 0) return;
    double dt = Runtime.TimeSinceLastRun.TotalSeconds;
    if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;
    _now += dt; _tick++;
    if (_tick % 600 == 0 || Me.CubeGrid != _myGrid) { _myGrid = Me.CubeGrid; GetBlocks(); }
    ReadF2S();
    if (_f2sN > 0 && _down && _tick % 60 == 0) GetBlocks();   // Flight is down: look for it (rebuilt PB = new EntityId) at 1 Hz
    if (_rc == null || !_rc.IsFunctional || !_rc.IsSameConstructAs(Me)) { if (_tick % 60 == 0) GetBlocks(); Watch(false); if (_tick % 30 == 0) Display(); return; }
    ReadIGC();
    FollowStep();
    if (_tick % 10 == 5) PollSensors();
    bool burn = LaunchStep(dt, _mode == Mode.IDLE, StartMode, _dynHomeT > 0, _dynHome, Cb);   // automatic take-off after the release from a printer (LAUNCH block)
    Relay();
    WpStep();
    SendFol(burn);
    Watch(burn);
    if (_tick % 30 == 0) Display();
}

void ReadF2S()
{
    while (_lF2S != null && _lF2S.HasPendingMessage)
    {
        var m = _lF2S.AcceptMessage();
        if (!(m.Data is MyTuple<int, long, Vector3D, Vector3D, float, string>)) continue;
        var d = (MyTuple<int, long, Vector3D, Vector3D, float, string>)m.Data;
        _fl = d.Item1; _mode = (Mode)Math.Min(d.Item1 & 7, 5); _tgtId = d.Item2; _tgtPos = d.Item3; _aimErr = d.Item4.X; _fireWin = d.Item4.Y; if (d.Item4.Z > 0.1) _aBrake = d.Item4.Z;
        _batt = d.Item5; _gate = d.Item6; _f2sT = _now; _f2sTick = _tick; _f2sN++;
    }
}

// Tag_HOME, Tag_CMD (only the words that are the Staff's), Tag_SWARM, the fleet channel (home, commands -> also to Flight), target shares to the fleet channel
void ReadIGC()
{
    while (_lHome.HasPendingMessage) { var m = _lHome.AcceptMessage(); if (m.Data is Vector3D) { _dynHome = (Vector3D)m.Data; _dynHomeT = _now; } }
    while (_lCmd.HasPendingMessage) { var m = _lCmd.AcceptMessage(); if (m.Data is string) Local((string)m.Data); }
    while (_lArg != null && _lArg.HasPendingMessage) { var m = _lArg.AcceptMessage(); if (m.Data is string) Local((string)m.Data); }   // a PB argument given to the Flight PB
    FleetPump(Cb, p => { _dynHome = p; _dynHomeT = _now; });
    ReadSwarm();
    while (_lFlt != null && _lFlt.HasPendingMessage)
    {
        var m = _lFlt.AcceptMessage();
        if (m.Data is MyTuple<long, Vector3D, Vector3D, double, long>) FleetSend((MyTuple<long, Vector3D, Vector3D, double, long>)m.Data);
    }
}

// fleet targets -> Flight (K.TGT): coalesced per target id, at most 10 a tick
Dictionary<long, MyTuple<long, Vector3D, Vector3D, double, long>> _rel = new Dictionary<long, MyTuple<long, Vector3D, Vector3D, double, long>>();
void Relay()
{
    foreach (var l in FleetTgtL)
        while (l.HasPendingMessage)
        {
            var m = l.AcceptMessage();
            if (m.Source == IGC.Me || !(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
            if (FleetOk(d)) _rel[d.Item1] = d;
        }
    if (_fk == "" || _tick - _f2sTick > 90) { _rel.Clear(); return; }
    _gone.Clear();
    foreach (var kv in _rel) { if (_gone.Count >= 10) break; IGC.SendBroadcastMessage(_fk + ".TGT", kv.Value, TransmissionDistance.CurrentConstruct); _gone.Add(kv.Key); }
    foreach (var id in _gone) _rel.Remove(id);
}

// the Flight PB's follow and take-off data: 2 ticks apart in FOLLOW / during the burn, else every 30 ticks and on every change
void SendFol(bool burn)
{
    if (_fk == "") return;
    bool fol = _mode == Mode.FOLLOW;
    if (burn == _folBurn && fol == _folFol && _tick % (fol || burn ? 2 : 30) != 1) return;
    _folBurn = burn; _folFol = fol;
    bool known = FollowKnown();
    Vector3D want = known && fol ? FollowWant(_rc.GetPosition(), _aBrake, Vector3D.Zero) : Vector3D.Zero;
    int f = (known ? 1 : 0) | (_foOnFoot ? 2 : 0) | (burn ? 4 : 0) | (_now - _dynHomeT < 5 ? 8 : 0);
    IGC.SendBroadcastMessage(_fk + ".FOL", new MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>(want, known ? FollowPos() : Vector3D.Zero, known ? FollowVel() : Vector3D.Zero,
        _dynHome, new Vector3D(FoEngage, FoLeash, 0), f), TransmissionDistance.CurrentConstruct);
}

void PollSensors()
{
    foreach (var s in _sensors)
    {
        if (!s.IsWorking) continue;
        _sensed.Clear(); s.DetectedEntities(_sensed);
        foreach (var e in _sensed) FollowSensed(e);
    }
}

// Flight PB dead (silent for 90 ticks after it had spoken, or switched off / destroyed): hand the ship back, once, then every second. Never during the Staff's own take-off burn.
void Watch(bool burn)
{
    bool down = _f2sN > 0 && (_tick - _f2sTick > 90 || (_fpb != null && !_fpb.IsWorking));
    if (!down) { _down = false; return; }
    if (burn || _lnS == 2) return;
    bool first = !_down; _down = true;
    if (!first && _now - _relT < 1) return;
    _relT = _now;
    foreach (var y in _gyros) { y.GyroOverride = false; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
    if (first)
    {
        foreach (var a in _ai) a.Enabled = false;   // the AI blocks the Flight PB switched on would keep flying
        foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction("Shoot_Off");
    }
}

// =====================================================================
//  COMMANDS (PB argument, toolbar): the Staff's own words are handled here, every word goes to the Flight PB (which ignores a mode it is in already)
void Local(string a)
{
    var w = a.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (w.Length == 0) return;
    switch (w[0])
    {
        case "follow": case "escort": FoSetSat(false); break;
        case "satellite": case "sat": case "orbit": FoSetSat(true); break;
        case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;
        case "reload": GetBlocks(); LoadMods(); break;
    }
}
void Fwd(string c) { if (_fk != "") IGC.SendBroadcastMessage(_fk + ".S2F", c, TransmissionDistance.CurrentConstruct); }
void Command(string a) { Local(a); Fwd(a); }
void Cb(string c) { Local(c); Fwd(c); }   // fleet commands and the take-off's mode change: the Staff's part, then the Flight PB's

// =====================================================================
//  DISPLAY
void Display()
{
    _sb.Clear();
    _sb.Append("HARRIER STAFF v").Append(VER).Append('\n');
    if (_fpb == null) _sb.Append("! no [Flight] PB on this construct\n");
    else if (_down || (_f2sN > 0 && !_fpb.IsWorking)) _sb.Append("FLIGHT PB DOWN  ").Append((_now - _f2sT).ToString("0.0")).Append(" s silent\n");
    else if (_f2sN == 0) _sb.Append("Flight : waiting for the first message\n");
    else _sb.Append("Flight : link OK  F2S ").Append((_now - _f2sT).ToString("0.00")).Append(" s ago\n");
    _sb.Append("Mode   : ").Append(_mode).Append('\n');
    if (_rc == null || !_rc.IsFunctional) _sb.Append("! No working Remote Control / cockpit\n");
    _sb.Append(FollowStatus());
    if (_tgtId != 0)
    {
        _sb.Append("Target : ").Append(_tgtId).Append("  range ").Append(_rc != null ? Vector3D.Distance(_tgtPos, _rc.GetPosition()).ToString("0") : "?").Append(" m\n");
        _sb.Append("Aim err: ").Append(_aimErr.ToString("0.00")).Append(" / ").Append(_fireWin.ToString("0.00")).Append(" deg").Append((_fl & 8) != 0 ? "  FIRING" : "").Append('\n');
    }
    else _sb.Append("Target : none\n\n");
    int rk = (_fl >> 8) & 255;
    _sb.Append("Battery: ").Append((_batt * 100).ToString("0")).Append("%  Ammo ").Append(rk == 255 ? "?" : rk.ToString()).Append('\n');
    if (_gate != "" && _tgtId != 0) _sb.Append("Hold   : ").Append(_gate).Append('\n');
    _sb.Append("Hive   : ").Append(_mates.Count + 1).Append((_fl & 16) != 0 ? "  !! UNDER FIRE" : "").Append((_fl & 32) != 0 ? "  lock stale" : "").Append((_fl & 64) != 0 ? "  AI searching" : "").Append('\n');
    _sb.Append(LaunchStatus());
    if (Me.OwnerId == 0) _sb.Append("! PB has no owner: set ownership\n");
    if (_crash != "") _sb.Append("! ").Append(_crash).Append('\n');
    _sb.Append(WpStatus());
    string s = _sb.ToString();
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
