// >>> SURVIVAL STAFF v1.1 (damage RTB, limp, failover, threat memory, gather, dock actuator, docs/design/survivability.md) >>>
// The Staff side of the survivability work (docs/design/survivability.md s2 items 3, 4, 5, 7, 8, 9, 10, 19, 20; contract s3 keys, s4 IGC). Decisions only:
// the Flight PB (SURVIVAL LINK) flies them. Link (CurrentConstruct, key _fk = "WSPL" + Flight PB id):
//   .SVF  Flight -> Staff  MyTuple<int flags, long attackerId, Vector3D hitDir, Vector3D attackerPos, Vector3D aux, Vector3D home>
//         flags bit0 hit in this report, bit1 Flight yielding, bits 8-15 integrity % (255 unknown), bits 16-23 blocks hit; aux = (largest drop, own fire range, attacker range)
//   .SVO  Flight -> Staff  MyTuple<long tgtId, Vector3D fwd, Vector3D up, Vector3D vel, double radius, int flags (bit0 orientation valid)>
//   .MSL  Flight -> Staff  MyTuple<long id, Vector3D pos, Vector3D vel>       (missiles the Flight saw)
//   .SVS  Staff -> Flight  MyTuple<int flags, long tgtId, Vector3D A, Vector3D B, Vector3D D, Vector3D N>, every 12 ticks, at once on a change of bits 0-11,
//         full resend on a new Flight key / its first F2S / the end of a failover hold. A = via point (bit1) or gather point (bit0); B = blind-side bearing x speed
//         (bit2); D = missile dodge velocity (bit3); N = (standoff floor, standoff add, enemy projectile speed). Bits 4 DAMAGED, 5 RETREATING, 6 GATHER READY,
//         7 GATHERING, 8 LIMP (+ cap m/s in bits 16-23), 9 STAFF HOLDS THE OVERRIDES (Flight yields), 10 FIGHT BACK in RTB, 11 standoff advice, 24-27 threat class.
// Wing (antenna): <Tag>_THREAT (+ <ch>_THREAT) MyTuple<long enemy, Vector3D pos, Vector3D bearing, Vector3D (hit range, dps/s, integrity), long reporter, int flags
//   (bits 0-1 class 1 bullet 2 explosive 3 long, bit2 LAST GASP, bit3 bearing in the target frame)>; <Tag>_MSL MyTuple<long id, Vector3D pos, Vector3D vel, long sender>.
// One writer: the gyro / thruster overrides are written here only while the Flight PB is down (limp-home, _svHold, SVS bit 9) and by the dock actuator
// (FOL bit 2), never while docked or welded (SvDocked) and never during the take-off burn. v1.1 dock actuator (docs/design/staff_split.md s3.2): DkGo() takes
// the actuators (snapshot of the own thrusters / gyros), DkSteer(vel, nose, roof) flies a setpoint with SvSteer from the 3rd tick on (FOL bit 2 reaches the
// Flight first), DkRel() lets go (zero, dampeners on; skipped while the Flight is down: SvWatch / limp-home own the actuators then), DkHold keeps FOL bit 2
// for 2 ticks after that write, SvRepaired() clears the damage latch. Callers: the local REPAIR CLIENT (Gnat, Basilisk) or AUX LINK (setpoints from the [Aux] PB).
// Keys: [Survival] in the Flight PB's Custom Data (SvCfg reads them; SvDef writes the missing defaults once per Flight key, after its first F2S, re-parsing first).
// Per-script hooks (readonly FS, SvOrbK, SvOrbD): bool SvFightMode() (a fight mode: GUARD/HUNT, Gnat ATTACK), string SvVerb() (the mode's command word), const string FS.
// Hooks: SvsRead() after ReadF2S(); SvWatch(burn || _lnS == 2) at the end of the tick and in the no-RC path (replaces Watch); SendFol(burn || SvFly);
//   SvListen() first in Listen(); SvBlocks() in GetBlocks; SvCfg(ini) in LoadFlightCfg; SvStatus() in Display; SvWord(w) first in Local(); FoDist + SvFoX() in SendFol.
// Uses: _rc, _fpb, _fk, _now, _tick, _mode (IDLE/RTB), _fl, _tgtId, _tgtPos, _f2sN, _f2sTick, _down, _gyros, _thr, _guns, _ai, _mates, Tag, _flChan, MaxSpeed,
//   _dynHome, _dynHomeT, _wpMap, Fwd(), FleetV(); REPAIR CLIENT or AUX LINK (RpCfg RpListen RpBlocks RpRead RpStep RpStatus RpStop RpWant SvFly RpMedCh),
//   STAFF SEARCH (Ss*).
List<MyTuple<string, string, string, double>> _svDefs = new List<MyTuple<string, string, string, double>>();
bool SvRtbOn = true, SvLostAx = false, SvLostCam = false, SvFightOn = true, SvLimpHome = true, SvBlind = true, SvStandOn = true, SvDodge = true, SvFlare = true,
    SvHand = true, SvShare = true, SvTogether = true;
double SvRtbI = 0.55, SvGyroF = 0, SvMargin = 300, SvLimpMin = 0.4, SvLimpV = 30, SvBlindK = 2, SvStandM = 150, SvRB = 800, SvRR = 800, SvRL = 2000,
    SvDodgeR = 25, SvFlareI = 1.5, SvFlareL = 2, SvHandI = 0.75, SvHandB = 200, SvHandIn = 100, SvFoXv = 40, SvMem = 120, SvGasp = 0.15, SvExpN = 3, SvExpD = 400,
    SvGMax = 6, SvGSpread = 50, SvGExtra = 150, SvGMate = 2000, SvOrb = 22;
string _svWrote = "", _svLKey = "", _svPrevVerb = "", _svWhy = "", _svFk = "";
IMyBroadcastListener _svLf, _svLo, _svLm, _svLt, _svLt2, _svLw;

string SvT(MyIni i, string s, string k, string d) { _svDefs.Add(new MyTuple<string, string, string, double>(s, k, d, 0)); return i.Get(s, k).ToString(d); }
bool SvBn(MyIni i, string s, string k, bool d) { _svDefs.Add(new MyTuple<string, string, string, double>(s, k, d ? "true" : "false", 0)); return i.Get(s, k).ToBoolean(d); }
double SvDn(MyIni i, string s, string k, double d) { _svDefs.Add(new MyTuple<string, string, string, double>(s, k, null, d)); return i.Get(s, k).ToDouble(d); }
bool SvB(MyIni i, string k, bool d) { return SvBn(i, "Survival", k, d); }
double SvD(MyIni i, string k, double d) { return SvDn(i, "Survival", k, d); }

void SvCfg(MyIni i)
{
    _svDefs.Clear();
    if (FS != "Gnat")
    {   // read by the Flight (SURVIVAL LINK): listed here so that their defaults are written in one place
        SvB(i, "Lead-Breaking Jinks", true); SvD(i, "Jink Accel Fraction", 0.8); SvD(i, "Enemy Projectile Speed (m/s)", 800);
        SvD(i, "Jink Period Min (s)", 0.4); SvD(i, "Jink Period Max (s)", 3); SvD(i, "Jink Period Random", 0.3);
    }
    if (FS == "Longbow" || FS == "Mantis") { SvB(i, "Power Guard", true); SvD(i, "Power Guard Hold (s)", 2); }
    // AB2 (sim, survivability.md "Results (AB2)"): damage RTB on integrity only by default. Lost axis / one of two gyros / forward cameras fired on the first
    // lost block, cost about 40 % of the wins vs the Pirate and made 1v1s stalemates; the [Thrust] PB and limp mode keep such a drone fighting.
    SvRtbOn = SvB(i, "Damage RTB", true); SvRtbI = SvD(i, "RTB Integrity Below", 0.55); SvLostAx = SvB(i, "RTB On Lost Thrust Axis", false);
    SvGyroF = SvD(i, "RTB Gyro Fraction", 0); SvLostCam = SvB(i, "RTB On Lost Forward Cameras", false); SvMargin = SvD(i, "Retreat Margin (m)", 300);
    SvFightOn = SvB(i, "Fight Back While Retreating", true); SvLimpMin = SvD(i, "Limp Min Speed Fraction", 0.4);
    SvLimpHome = SvB(i, "Limp Home On Flight Loss", true); SvLimpV = SvD(i, "Limp Home Speed", 30);
    SvBlind = SvB(i, "Blind Side Orbit", true); SvBlindK = SvD(i, "Blind Side Speed Factor", 2);
    SvStandOn = SvB(i, "Standoff By Enemy Weapons", true); SvStandM = SvD(i, "Threat Standoff Margin (m)", 150);
    SvRB = SvD(i, "Threat Range Bullet (m)", 800); SvRR = SvD(i, "Threat Range Rocket (m)", 800); SvRL = SvD(i, "Threat Range Long (m)", 2000);
    SvDodge = SvB(i, "Missile Dodge", true); SvDodgeR = SvD(i, "Missile Dodge Radius (m)", 25);
    SvFlare = SvB(i, "Flare On Missile", true); SvFlareI = SvD(i, "Flare Interval (s)", 1.5); SvFlareL = SvD(i, "Flare Lead (s)", 2);
    SvHand = SvB(i, "Wing Hand-off", true); SvHandI = SvD(i, "Hand-off Integrity", 0.75); SvHandB = SvD(i, "Hand-off Back (m)", 200);
    SvHandIn = SvD(i, "Hand-off In (m)", 100); SvFoXv = SvD(i, "Damaged Formation Extra (m)", 40);
    SvShare = SvB(i, "Share Threats", true); SvMem = SvD(i, "Threat Memory (s)", 120); SvGasp = SvD(i, "Last Gasp Drop", 0.15);
    SvExpN = SvD(i, "Explosive Hit Blocks", 3); SvExpD = SvD(i, "Explosive Drop Min", 400);
    SvTogether = SvB(i, "Attack Together", true); SvGMax = SvD(i, "Gather Max (s)", 6); SvGSpread = SvD(i, "Gather Spread (deg)", 50);
    SvGExtra = SvD(i, "Gather Extra Range (m)", 150); SvGMate = SvD(i, "Gather Mate Radius (m)", 2000);
    RpCfg(i);
    SvOrb = i.Get(FS, SvOrbK).ToDouble(SvOrbD);
}

// the missing [Survival] (and moved) defaults -> the Flight PB's Custom Data: once per Flight key, only after its first F2S, re-parsed right before writing
void SvDef()
{
    if (_fpb == null || _f2sN == 0 || _svWrote == _fk || _svDefs.Count == 0) return;
    var ini = new MyIni(); MyIniParseResult r;
    if (!ini.TryParse(_fpb.CustomData, out r)) return;
    _svWrote = _fk;
    foreach (var d in _svDefs) if (!ini.ContainsKey(d.Item1, d.Item2)) { if (d.Item3 != null) ini.Set(d.Item1, d.Item2, d.Item3); else ini.Set(d.Item1, d.Item2, d.Item4); }
    string o = ini.ToString(); if (o != _fpb.CustomData) _fpb.CustomData = o;
}

void SvListen()
{
    string k = Tag + "|" + _fk + "|" + _flChan + "|" + RpMedCh;
    if (k == _svLKey) return;
    _svLKey = k;
    foreach (var l in new[] { _svLf, _svLo, _svLm, _svLt, _svLt2, _svLw }) if (l != null) IGC.DisableBroadcastListener(l);
    _svLf = _svLo = _svLm = null;
    if (_fk != "") { _svLf = IGC.RegisterBroadcastListener(_fk + ".SVF"); _svLo = IGC.RegisterBroadcastListener(_fk + ".SVO"); _svLm = IGC.RegisterBroadcastListener(_fk + ".MSL"); }
    _svLt = IGC.RegisterBroadcastListener(Tag + "_THREAT"); _svLw = IGC.RegisterBroadcastListener(Tag + "_MSL");
    _svLt2 = _flChan != "" ? IGC.RegisterBroadcastListener(_flChan + "_THREAT") : null;
    RpListen();
    if (_fk != _svFk)
    {   // a new Flight PB: the survival mirror starts again (the threat memory is kept)
        _svFk = _fk; _svFT = -99; _svI = -1; _svOT = -99; _svHomeT = -99; _svFull = true;
    }
}

// ---- blocks
List<IMyCameraBlock> _svCams = new List<IMyCameraBlock>(); List<IMyUserControllableGun> _svFlares = new List<IMyUserControllableGun>();
List<IMyShipMergeBlock> _svMg = new List<IMyShipMergeBlock>(); List<IMyTerminalBlock> _svTmp = new List<IMyTerminalBlock>();
void SvBlocks()
{
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_svCams, b => b.CubeGrid == g);
    GridTerminalSystem.GetBlocksOfType(_svFlares, b => b.CubeGrid == g && b.BlockDefinition.SubtypeName.Contains("FlareLauncher"));
    RpBlocks();
}

// ---- mirror of the Flight's reports
int _svFf; long _svAtk, _svOId; Vector3D _svAtkP, _svAux, _svHomeP, _svOF, _svOU, _svOV; double _svFT = -99, _svHomeT = -99, _svAtkT = -99, _svI = -1, _svOT = -99, _svDps;
bool _svOOk, _svFull = true; int _svF2sSeen;
double[] _svIH = new double[12]; int _svIHn;
void SvsRead()
{
    if (_f2sN > 0 && _svF2sSeen == 0) _svFull = true;   // first F2S of this key: full SVS at once
    _svF2sSeen = _f2sN;
    SvDef();
    while (_svLf != null && _svLf.HasPendingMessage)
    {
        var m = _svLf.AcceptMessage();
        if (m.Data is MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>) SvSvf((MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>)m.Data);
    }
    while (_svLo != null && _svLo.HasPendingMessage)
    {
        var m = _svLo.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, double, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, double, int>)m.Data;
        _svOId = d.Item1; _svOF = d.Item2; _svOU = d.Item3; _svOV = d.Item4; _svOOk = (d.Item6 & 1) != 0 && d.Item2.LengthSquared() > 0.5 && d.Item3.LengthSquared() > 0.5; _svOT = _now;
    }
    while (_svLm != null && _svLm.HasPendingMessage)
    {
        var m = _svLm.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D>)m.Data;
        SvMsl(d.Item1, d.Item2, d.Item3, true);
    }
    while (_svLw != null && _svLw.HasPendingMessage)
    {
        var m = _svLw.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, long>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, long>)m.Data;
        if (d.Item4 != Me.CubeGrid.EntityId) SvMsl(d.Item1, d.Item2, d.Item3, false);
    }
    SvWing(_svLt); SvWing(_svLt2);
    RpRead();
}
bool SvHomeKnown() { return _now - _svHomeT < 120; }
bool SvFresh { get { return _now - _svFT < 2; } }
MatrixD SvOri() { return MatrixD.CreateWorld(Vector3D.Zero, Vector3D.Normalize(_svOF), Vector3D.Normalize(_svOU)); }
bool SvOriOk(long id) { return _svOOk && _svOId == id && id != 0 && _now - _svOT < 2; }

void SvSvf(MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D> d)
{
    _svFf = d.Item1; _svFT = _now; if (FleetV(d.Item5)) _svAux = d.Item5;
    if (d.Item6.LengthSquared() > 1 && FleetV(d.Item6)) { _svHomeP = d.Item6; _svHomeT = _now; }
    int ip = (d.Item1 >> 8) & 255;
    double ni = ip == 255 ? -1 : Math.Min(1, ip / 100.0), lost = _svI >= 0 && ni >= 0 ? Math.Max(0, _svI - ni) : 0;
    _svI = ni;
    if ((d.Item1 & 1) == 0 || d.Item2 == 0 || !FleetV(d.Item4) || _rc == null) return;
    _svAtk = d.Item2; _svAtkP = d.Item4; _svAtkT = _now;
    int nb = (d.Item1 >> 16) & 255;
    int cls = _now - _svMslT < 3 || (nb >= SvExpN && d.Item5.X >= SvExpD) ? 2 : d.Item5.Z > 1000 ? 3 : 1;
    Vector3D b = _rc.GetPosition() - d.Item4; if (b.LengthSquared() < 1) return; b.Normalize();
    bool loc = SvOriOk(d.Item2); if (loc) b = Vector3D.TransformNormal(b, MatrixD.Transpose(SvOri()));
    var e = SvRemember(d.Item2, d.Item4, cls, d.Item5.Z, b, lost, loc);
    e.Dps += (_svDps - e.Dps) * 0.3;
    if (_now - e.RepT >= 2) { e.RepT = _now; SvTell(e.Id, e.P, b, d.Item5.Z, cls, false, loc); }
}

// ---- threat memory (item 19): per enemy class votes, the longest range it hit someone at, dps, and where it hit (item 7 samples)
class SvSm { public Vector3D B; public double W, T; public bool L; }
class SvTh { public long Id; public Vector3D P; public int[] N = new int[4]; public double R, RT, Dps, T, RepT = -99; public List<SvSm> S = new List<SvSm>(); }
Dictionary<long, SvTh> _svTh = new Dictionary<long, SvTh>(); List<long> _svDel = new List<long>();
int SvCls(SvTh e) { int c = 0; for (int k = 1; k < 4; k++) if (e.N[k] > (c > 0 ? e.N[c] : 0)) c = k; return c; }
SvTh SvRemember(long id, Vector3D p, int cls, double r, Vector3D b, double w, bool loc)
{
    SvTh e;
    if (!_svTh.TryGetValue(id, out e))
    {
        if (_svTh.Count >= 8) { long o = 0; double ot = 1e18; foreach (var kv in _svTh) if (kv.Value.T < ot) { ot = kv.Value.T; o = kv.Key; } _svTh.Remove(o); }
        e = new SvTh { Id = id }; _svTh[id] = e;
    }
    e.P = p; e.T = _now;
    if (cls > 0 && cls < 4) e.N[cls]++;
    if (r > 0 && r < 1e5 && r >= e.R) { e.R = r; e.RT = _now; }
    if (w > 0 && b.LengthSquared() > 0.5) { e.S.Add(new SvSm { B = b, W = w, T = _now, L = loc }); if (e.S.Count > 16) e.S.RemoveAt(0); }
    return e;
}
void SvTell(long id, Vector3D p, Vector3D b, double r, int cls, bool gasp, bool loc)
{
    if (!SvShare) return;
    var t = new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(id, p, b, new Vector3D(r, _svDps, Math.Max(0, _svI)), Me.CubeGrid.EntityId, (cls & 3) | (gasp ? 4 : 0) | (loc ? 8 : 0));
    IGC.SendBroadcastMessage(Tag + "_THREAT", t);
    if (_flChan != "") IGC.SendBroadcastMessage(_flChan + "_THREAT", t);
}
void SvWing(IMyBroadcastListener l)
{
    while (l != null && l.HasPendingMessage)
    {
        var m = l.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>)m.Data;
        if (d.Item5 == Me.CubeGrid.EntityId || d.Item1 == 0 || !FleetV(d.Item2) || !FleetV(d.Item3) || !FleetV(d.Item4)) continue;
        Vector3D b = d.Item3; double bl = b.Length(); if (bl > 0.5) b /= bl; else b = Vector3D.Zero;
        double w = Math.Max(0, Math.Min(1, d.Item4.Y)) * 2; if ((d.Item6 & 4) != 0) w = Math.Max(w, SvGasp);
        var e = SvRemember(d.Item1, d.Item2, d.Item6 & 3, d.Item4.X, b, w, (d.Item6 & 8) != 0);
        e.Dps += (Math.Max(0, d.Item4.Y) - e.Dps) * 0.3;
    }
}
double SvClsR(int c) { return c == 1 ? SvRB : c == 2 ? SvRR : c == 3 ? SvRL : 0; }
// how far that enemy reaches (its class range or the longest range it hit at, whichever is more); dflt when it is unknown
double SvReach(long id, double dflt)
{
    SvTh e; if (!_svTh.TryGetValue(id, out e)) return dflt;
    int c = SvCls(e); if (c == 0 && e.R <= 0) return dflt;
    return Math.Max(SvClsR(c), e.R * 1.05);
}

// ---- missiles (item 9)
class SvM { public Vector3D P, V; public double T, RelT = -99; }
Dictionary<long, SvM> _svMs = new Dictionary<long, SvM>();
double _svMslT = -99, _svDodgeEnd = -99, _svFlareT = -99, _svTca = -1; Vector3D _svDodgeV;
void SvMsl(long id, Vector3D p, Vector3D v, bool own)
{
    if (!FleetV(p) || !FleetV(v) || _rc == null) return;
    SvM t; if (!_svMs.TryGetValue(id, out t)) { if (_svMs.Count >= 16) return; t = new SvM(); _svMs[id] = t; }
    t.P = p; t.V = v; t.T = _now;
    if (!own || _now - t.RelT < 0.2) return;
    bool close = SvClosing(p, v, _rc.GetPosition(), _rc.GetShipVelocities().LinearVelocity);   // relay only what closes on a friendly within 1.5 km
    foreach (var mt in _mates.Values) if (!close && _now - mt.Seen < 1.5) close = SvClosing(p, v, mt.Pos, mt.Vel);
    if (close) { t.RelT = _now; IGC.SendBroadcastMessage(Tag + "_MSL", new MyTuple<long, Vector3D, Vector3D, long>(id, p, v, Me.CubeGrid.EntityId)); }
}
bool SvClosing(Vector3D p, Vector3D v, Vector3D f, Vector3D fv) { Vector3D r = p - f; return r.LengthSquared() < 2.25e6 && Vector3D.Dot(r, v - fv) < 0; }
void SvMslStep()
{
    _svDel.Clear(); double best = 99; Vector3D me = _rc.GetPosition(), mv = _rc.GetShipVelocities().LinearVelocity, dv = Vector3D.Zero;
    foreach (var kv in _svMs)
    {
        var t = kv.Value; double age = _now - t.T;
        if (age > 1) { _svDel.Add(kv.Key); continue; }
        Vector3D r = t.P + t.V * age - me, vr = t.V - mv; double vr2 = vr.LengthSquared(); if (vr2 < 1) continue;
        double tca = -Vector3D.Dot(r, vr) / vr2; if (tca <= 0 || tca >= 4 || tca >= best) continue;
        Vector3D miss = r + vr * tca; if (miss.LengthSquared() >= SvDodgeR * SvDodgeR) continue;
        best = tca; Vector3D n = vr / Math.Sqrt(vr2), d = -miss - n * Vector3D.Dot(-miss, n);   // away from the closest-approach point, across the missile's path
        if (d.LengthSquared() < 0.01) d = _rc.WorldMatrix.Up - n * Vector3D.Dot(_rc.WorldMatrix.Up, n);
        dv = d.LengthSquared() > 1e-4 ? Vector3D.Normalize(d) * MaxSpeed : Vector3D.Zero;
    }
    foreach (var k in _svDel) _svMs.Remove(k);
    if (best >= 99) return;
    _svTca = best;
    _svMslT = _now;
    if (SvDodge) { _svDodgeV = dv; _svDodgeEnd = _now + best + 0.5; }
    if (SvFlare && best < SvFlareL && _now - _svFlareT >= SvFlareI && !_svDock && _svFlares.Count > 0)
    { _svFlareT = _now; foreach (var f in _svFlares) if (f.IsFunctional) f.ApplyAction("ShootOnce"); }
}

// ---- damage state (items 3, 4): integrity, thrust per axis (rated = the best seen), gyros, forward cameras
double[] _svRated = new double[6], _svLive = new double[6]; int _svLost, _svGyN, _svGyMax, _svCamN, _svCamMax, _svLatch; double _svF = 1, _svAmin = 1, _svRtbT = -99;
bool _svLimp, _svDam, _svRtb; IMyCubeGrid _svScanG;
int SvAx(Vector3D push, MatrixD m)
{
    if (Vector3D.Dot(push, m.Forward) > 0.9) return 0; if (Vector3D.Dot(push, m.Backward) > 0.9) return 1;
    if (Vector3D.Dot(push, m.Left) > 0.9) return 2; if (Vector3D.Dot(push, m.Right) > 0.9) return 3;
    if (Vector3D.Dot(push, m.Up) > 0.9) return 4; if (Vector3D.Dot(push, m.Down) > 0.9) return 5;
    return -1;
}
void SvScan()
{
    var g = Me.CubeGrid;
    if (g != _svScanG) { _svScanG = g; Array.Clear(_svRated, 0, 6); _svGyMax = 0; _svCamMax = 0; _svLatch = 0; }   // a new grid (released, printed, repaired): maxima start again
    var m = _rc.WorldMatrix; Array.Clear(_svLive, 0, 6);
    foreach (var t in _thr) { if (t.CubeGrid != g || !t.IsWorking) continue; int i = SvAx(t.WorldMatrix.Backward, m); if (i >= 0) _svLive[i] += t.MaxThrust; }
    _svLost = 0; double f = 1, amin = 1e18;
    for (int i = 0; i < 6; i++)
    {
        if (_svLive[i] > _svRated[i]) _svRated[i] = _svLive[i];
        if (_svRated[i] > 0) { double q = _svLive[i] / _svRated[i]; if (q < 0.1) { _svLost |= 1 << i; q = 0.3; } f = Math.Min(f, q); }
        if (_svLive[i] > 0) amin = Math.Min(amin, _svLive[i]);
    }
    double mass = _rc.CalculateShipMass().PhysicalMass;
    _svF = f; _svAmin = mass > 0 && amin < 1e18 ? Math.Max(0.3, amin / mass * 0.5) : 1;
    _svLimp = _svLost != 0 || f < 0.5;
    _svGyN = 0; foreach (var y in _gyros) if (y.CubeGrid == g && y.IsWorking) _svGyN++;
    _svCamN = 0; foreach (var c in _svCams) if (c.IsWorking && Vector3D.Dot(c.WorldMatrix.Forward, m.Forward) > 0.9) _svCamN++;
    _svGyMax = Math.Max(_svGyMax, _svGyN); _svCamMax = Math.Max(_svCamMax, _svCamN);
    if (_svI >= 0) { _svIH[_svIHn++ % 12] = _svI; }
    double old = _svIHn >= 12 ? _svIH[_svIHn % 12] : (_svIHn > 0 ? _svIH[0] : _svI);   // integrity 2 s ago (10-tick samples)
    _svDps = _svI >= 0 && old >= 0 ? Math.Max(0, old - _svI) / 2 : 0;
    _svDam = (_svI >= 0 && _svI < SvHandI) || _svLimp;
    int c0 = (_svI >= 0 && _svI < SvRtbI ? 1 : 0) | (SvLostAx && _svLost != 0 ? 2 : 0) | (_svGyMax >= 2 && _svGyN <= SvGyroF * _svGyMax ? 4 : 0) | (SvLostCam && _svCamMax >= 1 && _svCamN == 0 ? 8 : 0);
    _svLatch &= c0;                                          // a cause that went away (repaired) may fire again
    int nw = c0 & ~_svLatch;
    if (nw != 0 && SvRtbOn && _mode != Mode.IDLE && _mode != Mode.RTB && !_svDock && !_down && _f2sN > 0)
    {
        _svLatch |= nw; _svPrevVerb = SvVerb(); _svRtb = true; _svRtbT = _now; _svViaT = -99;
        _svWhy = (nw & 1) != 0 ? "integrity " + (_svI * 100).ToString("0") + "%" : (nw & 2) != 0 ? "thrust axis lost" : (nw & 4) != 0 ? "gyros lost" : "forward cameras lost";
        Fwd("rtb");
    }
    if (_svRtb && _mode != Mode.IDLE && _mode != Mode.RTB && _now - _svRtbT > 3) _svRtb = false;   // the owner sent it back into a fight some other way
    if (_svI >= 0 && (old - _svI >= SvGasp || (_svI < 0.35 && old >= 0.35)) && _now - _svGaspT > 1)
    {   // last gasp: tell the wing what is killing me, at once
        _svGaspT = _now;
        long id = _now - _svAtkT < 10 ? _svAtk : _tgtId; Vector3D p = _now - _svAtkT < 10 ? _svAtkP : _tgtPos;
        if (id != 0) { Vector3D b = _rc.GetPosition() - p; SvTh e; int cls = _svTh.TryGetValue(id, out e) ? SvCls(e) : 1; SvTell(id, p, b.LengthSquared() > 1 ? Vector3D.Normalize(b) : b, Vector3D.Distance(p, _rc.GetPosition()), cls, true, false); }
    }
}
double _svGaspT = -99;
string SvWord(string[] w)
{
    string v = w[0];
    if (v == "fight") { _svRtb = false; RpStop("fight"); }
    else if (v == "repair") RpWant(1);
    else if (v == "dock") RpWant(2);
    else if (v == "seeker" && w.Length > 1) { SeekerOn = w[1] == "on"; SsBlocks(); }
    else if (",guard,hunt,assist,idle,stop,follow,escort,ready,sentry,satellite,sat,orbit,form,attack,".Contains("," + v + ",")) { _svRtb = false; RpStop(""); }
    return v;
}

// ---- retreat via point (item 3), standoff (item 8), hand-off (item 10), blind side (item 7), gather (item 20)
bool _svViaOn, _svBOn, _svGOn, _svGReady; double _svViaT = -99, _svAdd, _svAddT = -99, _svFloor, _svGStart, _svAncT = -99; long _svGT, _svGPrev, _svAncId;
Vector3D _svViaP, _svBV, _svGP, _svAnc; int _svGMode; Dictionary<long, double> _svGDone = new Dictionary<long, double>();
List<long> _svIds = new List<long>();
void SvVia()
{
    _svViaOn = false;
    if (!_svRtb || _mode != Mode.RTB || !SvHomeKnown()) return;
    bool atk = _svAtk != 0 && _now - _svAtkT < 10; if (!atk && _tgtId == 0) return;
    Vector3D T = atk ? _svAtkP : _tgtPos, M = _rc.GetPosition(), H = _svHomeP;
    double R = SvReach(atk ? _svAtk : _tgtId, 900);
    Vector3D mh = H - M; double l = mh.LengthSquared(), s = l > 1 ? MathHelper.Clamp(Vector3D.Dot(T - M, mh) / l, 0, 1) : 0;
    if (Vector3D.DistanceSquared(M + mh * s, T) >= R * R) return;   // the straight way home keeps out of its reach
    Vector3D u = M - T; double ul = u.Length(); if (ul < 1 || l < 1) return; u /= ul;
    Vector3D h = mh / Math.Sqrt(l);
    if (Vector3D.Dot(h, -u) < 0.5) return;                    // home is not behind the threat
    Vector3D side = h - Vector3D.Dot(h, u) * u;
    if (side.LengthSquared() < 1e-4) side = _rc.WorldMatrix.Right - Vector3D.Dot(_rc.WorldMatrix.Right, u) * u;
    side.Normalize();
    Vector3D V = T + Vector3D.Normalize(u * 0.342 + side * 0.940) * Math.Max(R + SvMargin, ul);   // 70 deg off the threat axis
    if (Vector3D.DistanceSquared(M, V) < 22500) return;
    _svViaP = V; _svViaOn = true;
}
void SvSlow()
{
    Vector3D me = _rc.GetPosition();
    bool uf = (_fl & 16) != 0;
    double ownR = _svAux.Y > 10 ? _svAux.Y : 800, dist = _tgtId != 0 ? Vector3D.Distance(me, _tgtPos) : 0;
    // standoff floor: only with a range advantage
    _svFloor = 0;
    if (SvStandOn && _tgtId != 0) { double r = SvReach(_tgtId, -1); if (r > 0 && r + SvStandM < 0.95 * ownR) _svFloor = r + SvStandM; }
    // hand-off
    double add = 0;
    if (SvHand && _tgtId != 0)
    {
        bool ok = false, dam = false;
        foreach (var m in _mates.Values)
        {
            if (_now - m.Seen > 1.5 || m.Tgt != _tgtId || Vector3D.DistanceSquared(m.Pos, me) > 2.25e6) continue;
            if ((m.Flags & 24) == 0) ok = true; if ((m.Flags & 8) != 0) dam = true;
        }
        if (uf && _svDam && ok) add = SvHandB; else if (!_svDam && _svI >= 0.8 && !uf && dam) add = -SvHandIn;
    }
    if (add != 0) { _svAdd = add; _svAddT = _now; } else if (_now - _svAddT > 5) _svAdd = 0;
    // gather window
    if (_svGOn)
    {
        bool all = SvSlot(me, ownR);
        if (_tgtId != _svGT || !SvFightMode() || (int)_mode != _svGMode || uf || dist <= ownR || _now - _svGStart > SvGMax || all || _svIds.Count < 2)
        { _svGOn = false; _svGReady = false; _svAncT = _now; }
    }
    else if (_tgtId != 0 && _tgtId != _svGPrev && SvTogether && SvFightMode() && !uf && dist > ownR + 200)
    {
        double lt; if (!_svGDone.TryGetValue(_tgtId, out lt) || _now - lt > 60)
        {
            _svGT = _tgtId; SvSlot(me, ownR);
            if (_svIds.Count >= 2) { _svGOn = true; _svGStart = _now; _svGMode = (int)_mode; _svGDone[_tgtId] = _now; _svAncId = _tgtId; if (_svGDone.Count > 16) _svGDone.Clear(); }
        }
    }
    _svGPrev = _tgtId;
    SvBlindSide(me);
}
// gather slot k of the participants (me + fresh mates on this target or none, not retreating, within the mate radius); true = all of them are READY
bool SvSlot(Vector3D me, double ownR)
{
    _svIds.Clear(); long my = Me.CubeGrid.EntityId; _svIds.Add(my); Vector3D c = me; bool all = true;
    foreach (var m in _mates.Values)
    {
        if (_now - m.Seen > 1.5 || (m.Tgt != _svGT && m.Tgt != 0) || (m.Flags & 16) != 0 || Vector3D.DistanceSquared(m.Pos, me) > SvGMate * SvGMate) continue;
        _svIds.Add(m.Id); c += m.Pos; if ((m.Flags & 32) == 0) all = false;
    }
    int n = _svIds.Count; if (n < 2) return false;
    _svIds.Sort(); int k = _svIds.IndexOf(my);
    Vector3D a = c / n - _tgtPos; if (a.LengthSquared() < 1) a = me - _tgtPos; a.Normalize();
    Vector3D g = _rc.GetNaturalGravity(), up = g.LengthSquared() > 0.1 ? -Vector3D.Normalize(g) : Vector3D.Up, e1 = Vector3D.Cross(a, up);
    if (e1.LengthSquared() < 1e-4) e1 = Vector3D.Cross(a, Vector3D.Right);
    e1.Normalize(); Vector3D e2 = Vector3D.Cross(a, e1);
    double phi = Math.Min(SvGSpread, 30.0 * (n - 1)) * Math.PI / 180, th = 2 * Math.PI * k / n;
    _svAnc = a * Math.Cos(phi) + (e1 * Math.Cos(th) + e2 * Math.Sin(th)) * Math.Sin(phi);
    _svGP = _tgtPos + _svAnc * (ownR + SvGExtra);
    _svGReady = Vector3D.DistanceSquared(me, _svGP) < 22500;
    return all && _svGReady;
}
// exposure of bearing b (target -> me, world) to that enemy: hits taken there (own and wing, 30 s decay) and its turrets (weakpoint relay, kind Weapons)
double SvExpo(SvTh e, Vector3D b, MatrixD o, bool ori)
{
    double E = 0;
    if (e != null)
        foreach (var s in e.S)
        {
            Vector3D v = s.B; if (s.L) { if (!ori) continue; v = Vector3D.TransformNormal(v, o); }
            double c = Vector3D.Dot(b, v); if (c > 0) E += s.W * Math.Exp(-(_now - s.T) / 30) * c * c;
        }
    if (ori) foreach (var w in _wpMap.Values)
        {
            if (w.Tgt != _tgtId || (w.F & 7) != 2 || w.S.LengthSquared() < 1) continue;
            double c = Vector3D.Dot(b, Vector3D.TransformNormal(Vector3D.Normalize(w.S), o)); if (c > 0) E += 0.05 * c * c;
        }
    return E;
}
double SvAng(Vector3D a, Vector3D b) { return Math.Acos(MathHelper.Clamp(Vector3D.Dot(a, b), -1, 1)) * 180 / Math.PI; }
void SvBlindSide(Vector3D me)
{
    _svBOn = false;
    if (!SvBlind || _tgtId == 0) return;
    SvTh e; _svTh.TryGetValue(_tgtId, out e);
    bool ori = SvOriOk(_tgtId); MatrixD o = ori ? SvOri() : MatrixD.Identity;
    Vector3D c = me - _tgtPos; if (c.LengthSquared() < 1) return; c.Normalize();
    double ec = SvExpo(e, c, o, ori); if (ec <= 0) return;
    bool slot = (_svGOn || _now - _svAncT < 60) && _svAncId == _tgtId;
    Vector3D e1 = Vector3D.Normalize(Vector3D.Cross(c, Math.Abs(c.X) < 0.9 ? Vector3D.Right : Vector3D.Up)), e2 = Vector3D.Cross(c, e1), best = c; double bc = 1e18;
    for (int i = 0; i < 14; i++)
    {
        Vector3D b;
        if (i == 12) b = c; else if (i == 13) { if (!slot) continue; b = _svAnc; }
        else { double th = (30 * (1 + i % 3)) * (i % 6 < 3 ? 1 : -1) * Math.PI / 180; b = c * Math.Cos(th) + (i < 6 ? e1 : e2) * Math.Sin(th); }
        double cost = SvExpo(e, b, o, ori) + 0.02 * SvAng(c, b) / 30 + (slot ? 0.05 * Math.Max(0, SvAng(b, _svAnc) - 60) / 30 : 0);
        if (cost < bc) { bc = cost; best = b; }
    }
    if (bc >= 0.7 * ec) return;
    _svBV = Vector3D.Normalize(best) * Math.Min(SvBlindK * SvOrb, MaxSpeed * 0.5); _svBOn = true;
}

// ---- failover (item 5): the Flight PB is down -> release once (AI off, guns off), then limp home on our own if allowed; SvWatch replaces Watch()
bool _svHold, _svArr, _svDock; double _svRelT = -99; long _svDownTk; Vector3D _svHomeL; IMyCubeGrid _svDockG; bool _svFused;
bool SvDocked() { return _svDock; }
bool SvDockNow()
{
    var g = Me.CubeGrid;
    if (g != _svDockG)
    {   // a printer / bay on our grid (welded or merged): its thrusters are not ours
        _svDockG = g;
        GridTerminalSystem.GetBlocksOfType(_svTmp, b => b.CubeGrid == g && b != Me && (b.CustomName.Contains("[Bay") || (b is IMyProgrammableBlock && b.CustomData.Contains("[Hive Mother]"))));
        _svFused = _svTmp.Count > 0;
    }
    if (_svFused) return true;
    GridTerminalSystem.GetBlocksOfType(_svMg, b => b.CubeGrid == g);
    foreach (var mg in _svMg) if (mg.IsConnected) return true;
    return false;
}
void SvRel()
{
    foreach (var y in _gyros) { y.GyroOverride = false; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
}
void SvWatch(bool busy)
{
    if (_tick % 10 == 7 || Me.CubeGrid != _svDockG) _svDock = SvDockNow();
    bool rcOk = _rc != null && _rc.IsFunctional && _rc.IsSameConstructAs(Me);
    bool stale = _fpb == null || _tick - _f2sTick > 90 || !_fpb.IsWorking;
    bool down = _svHold ? (_f2sN == 0 || stale) : (_f2sN > 0 && stale);   // (holding: a rebuilt Flight PB takes over only once it has spoken)
    if (!down) _svDownTk = _tick;
    if (!down)
    {
        if (_svHold || _down) _svFull = true;               // the Flight is back: bit 9 cleared at once, we stop writing (no zeroing)
        _svHold = false; _down = false;
    }
    else if (_tick - _svDownTk < 3) { }                     // a Flight PB destroyed mid-tick may have written this tick or the last: wait 2 ticks (one writer)
    else if (busy || _svDock) { if (_svHold) { _svHold = false; _svFull = true; } }
    else
    {
        bool first = !_down; _down = true;
        if (first)
        {
            if (!_svHold) SvRel();
            foreach (var a in _ai) a.Enabled = false;          // the AI blocks the Flight PB switched on would keep flying
            foreach (var gn in _guns) if (gn.IsFunctional) gn.ApplyAction("Shoot_Off");
            bool hk = SvHomeKnown() || _now - _dynHomeT < 5;
            if (!_svHold && SvLimpHome && hk && rcOk) { _svHold = true; _svArr = false; _svHomeL = SvHomeKnown() ? _svHomeP : _dynHome; _svFull = true; }
            _svRelT = _now;
        }
        if (_svHold && rcOk) SvLimpStep();
        else if (!_svHold && _now - _svRelT >= 1) { _svRelT = _now; SvRel(); }
    }
    if (rcOk)
    {
        if (_tick % 10 == 3 && !_svDock) SvScan();
        if (_tick % 2 == 0 && _svMs.Count > 0) SvMslStep();
        if (_tick % 60 == 11) SvVia();
        if (_tick % 30 == 17) { SvSlow(); SvKeep(); }
        SsStep();
        RpStep();
    }
    SvSend();
}
void SvLimpStep()
{
    if (_now - _dynHomeT < 5 && !SvHomeKnown()) _svHomeL = _dynHome;
    if (_svArr) return;                                     // home: released, dampeners on, nothing more is written
    Vector3D me = _rc.GetPosition(), d = _svHomeL - me; double dist = d.Length();
    if (dist < 30 && _rc.GetShipVelocities().LinearVelocity.Length() < 2) { SvRel(); _svArr = true; return; }
    double v = Math.Min(SvLimpV, Math.Sqrt(2 * _svAmin * dist)), cap = SvCapV(); if (cap > 0) v = Math.Min(v, cap);
    SvSteer(dist > 0.5 ? d / dist * v : Vector3D.Zero, dist > 5 ? d : Vector3D.Zero, Vector3D.Zero);
}
// ---- dock actuator (v1.1): the one place a dock approach writes the overrides
List<IMyThrust> _dkThr = new List<IMyThrust>(); List<IMyGyro> _dkGy = new List<IMyGyro>(); long _dkTk, _dkRelTk = -9; bool _dkOn;
bool DkHold { get { return _tick - _dkRelTk < 2; } }
void DkGo() { _dkThr.Clear(); _dkThr.AddRange(_thr); _dkGy.Clear(); _dkGy.AddRange(_gyros); _dkTk = _tick; _dkOn = true; }   // own blocks: once merged the lists hold the host's too
void DkSteer(Vector3D v, Vector3D f, Vector3D u) { if (_dkOn && _tick - _dkTk >= 3) SvSteer(v, f, u); }
void DkRel()
{
    if (!_dkOn) return;                                       // nothing taken: nothing to give back (no stray DampenersOverride write)
    _dkOn = false; _dkRelTk = _tick;
    if (_down) return;                                        // the Flight is down: SvWatch released already, limp-home may own the actuators
    foreach (var y in _dkGy) { y.GyroOverride = false; y.Pitch = 0; y.Yaw = 0; y.Roll = 0; }
    foreach (var th in _dkThr) th.ThrustOverridePercentage = 0;
    if (_rc != null) _rc.DampenersOverride = true;
}
void SvRepaired() { _svRtb = false; }
double SvCapV() { return _svLimp ? MaxSpeed * MathHelper.Clamp(Math.Sqrt(_svF), SvLimpMin, 1) : 0; }
// the Staff's own flying (limp home, dock approach): velocity command vw (world), nose along fw and roof along up (zero = free); thrust split like Flight's ApplyThrust
double[] _svMx = new double[6];
void SvSteer(Vector3D vw, Vector3D fw, Vector3D up)
{
    var m = _rc.WorldMatrix; Vector3D w = Vector3D.Zero;
    if (fw.LengthSquared() > 1e-6)
    {
        fw.Normalize(); Vector3D ax = Vector3D.Cross(m.Forward, fw); double s = ax.Length(), ang = Math.Atan2(s, Vector3D.Dot(m.Forward, fw));
        w = s > 1e-6 ? ax / s * ang : (ang > 1 ? m.Up * ang : Vector3D.Zero);
        if (up.LengthSquared() > 0.5 && ang < 0.6)
        {
            Vector3D u = up - Vector3D.Dot(up, m.Forward) * m.Forward;
            if (u.LengthSquared() > 1e-4) { u.Normalize(); w += m.Forward * Math.Atan2(Vector3D.Dot(Vector3D.Cross(m.Up, u), m.Forward), Vector3D.Dot(m.Up, u)); }
        }
    }
    w *= 2; if (w.LengthSquared() > 2.25) w = Vector3D.Normalize(w) * 1.5;
    foreach (var g in _gyros)
    {
        if (!g.IsFunctional) continue;
        Vector3D l = Vector3D.TransformNormal(w, MatrixD.Transpose(g.WorldMatrix));
        g.Pitch = -(float)l.X; g.Yaw = -(float)l.Y; g.Roll = -(float)l.Z; g.GyroOverride = true;
    }
    Vector3D acc = (vw - _rc.GetShipVelocities().LinearVelocity) * 2; if (acc.LengthSquared() > 2500) acc = Vector3D.Normalize(acc) * 50;
    Vector3D F = (acc - _rc.GetNaturalGravity()) * _rc.CalculateShipMass().PhysicalMass;
    _rc.DampenersOverride = false;
    Array.Clear(_svMx, 0, 6);
    foreach (var t in _thr) { int i = SvAx(t.WorldMatrix.Backward, m); if (i >= 0 && t.IsWorking) _svMx[i] += t.MaxEffectiveThrust; }
    foreach (var t in _thr)
    {
        int i = SvAx(t.WorldMatrix.Backward, m); if (i < 0) continue;
        double c = Vector3D.Dot(F, t.WorldMatrix.Backward);
        float p = c > 0 && _svMx[i] > 0 ? (float)MathHelper.Clamp(c / _svMx[i], 0, 1) : 0f;
        if (p > 0 && p < 0.002f) p = 0.002f;
        t.ThrustOverridePercentage = p;
    }
}

// ---- upkeep (2 Hz): memory decay and expiry
void SvKeep()
{
    _svDel.Clear();
    foreach (var kv in _svTh)
    {
        var e = kv.Value;
        if (_now - e.T > SvMem) { _svDel.Add(kv.Key); continue; }
        if (_now - e.RT > 60) e.R *= 0.995;                   // rObs: 1 %/s after 60 s (this runs twice a second)
        for (int i = e.S.Count - 1; i >= 0; i--) if (_now - e.S[i].T > 150) e.S.RemoveAt(i);
    }
    foreach (var k in _svDel) _svTh.Remove(k);
}

// ---- SVS (Staff -> Flight)
int _svSentF = -1; long _svSentTk = -99;
void SvSend()
{
    if (_fk == "") return;
    int cls = 0; SvTh e; if (_tgtId != 0 && _svTh.TryGetValue(_tgtId, out e)) cls = SvCls(e);
    int cap = (int)Math.Min(255, Math.Round(SvCapV()));
    bool fb = SvFightOn && _svRtb && _svLost == 0;
    if (fb) { fb = false; foreach (var g in _guns) if (g.IsFunctional && !g.BlockDefinition.SubtypeName.Contains("Flare")) { fb = true; break; } }
    int f = (_svViaOn ? 2 : _svGOn ? 1 : 0) | (_svBOn ? 4 : 0) | (_now < _svDodgeEnd ? 8 : 0) | (_svDam ? 16 : 0) | (_svRtb && _mode == Mode.RTB ? 32 : 0)
        | (_svGOn && _svGReady ? 64 : 0) | (_svGOn ? 128 : 0) | (_svLimp ? 256 | (cap << 16) : 0) | (_svHold ? 512 : 0) | (fb ? 1024 : 0)
        | (_svFloor > 0 || _svAdd != 0 ? 2048 : 0) | ((cls & 15) << 24);
    if (((f ^ _svSentF) & 4095) == 0 && !_svFull && _tick - _svSentTk < 12) return;
    _svSentF = f; _svSentTk = _tick; _svFull = false;
    IGC.SendBroadcastMessage(_fk + ".SVS", new MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>(f, _tgtId, _svViaOn ? _svViaP : _svGOn ? _svGP : Vector3D.Zero,
        _svBOn ? _svBV : Vector3D.Zero, _now < _svDodgeEnd ? _svDodgeV : Vector3D.Zero, new Vector3D(_svFloor, _svAdd, cls == 1 ? 800 : cls == 2 ? 200 : cls == 3 ? 1000 : 0)),
        TransmissionDistance.CurrentConstruct);
}
double SvFoX() { return SvHand && _svDam ? SvFoXv : 0; }

string SvStatus()
{
    var s = new StringBuilder();
    if (_svHold) s.Append(_svArr ? "LIMP HOME: home, holding\n" : "LIMP HOME: flying home (Flight PB down)\n");
    if (_svRtb) s.Append("DAMAGED: ").Append(_svWhy).Append(" - RTB").Append(_svViaOn ? " (via)" : "").Append('\n');
    else if (_svDam) s.Append("Damaged: integrity ").Append(_svI >= 0 ? (_svI * 100).ToString("0") + "%" : "?").Append(_svLimp ? "  LIMP" : "").Append('\n');
    SvTh e;
    if (_tgtId != 0 && _svTh.TryGetValue(_tgtId, out e))
    {
        string[] cn = { "?", "bullet", "explosive", "long" };
        s.Append("Threat : ").Append(cn[SvCls(e)]).Append(" r").Append(e.R.ToString("0")).Append(" dps").Append((e.Dps * 100).ToString("0.0")).Append(_svFloor > 0 ? "  floor " + _svFloor.ToString("0") : "").Append('\n');
    }
    if (_svGOn) s.Append("Gather : ").Append(_svIds.Count).Append(" drones").Append(_svGReady ? ", ready" : "").Append('\n');
    if (_svBOn) s.Append("Orbit  : blind side\n");
    if (_now < _svDodgeEnd) s.Append("MISSILE: dodging, tca ").Append(_svTca.ToString("0.0")).Append(" s\n");
    if (_svAdd != 0) s.Append("Wing   : hand-off ").Append(_svAdd.ToString("+0;-0")).Append(" m\n");
    if (_rc != null) s.Append(SsStatus());
    s.Append(RpStatus());
    return s.ToString();
}
// <<< SURVIVAL STAFF v1.1 <<<
