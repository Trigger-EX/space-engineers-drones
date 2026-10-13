// =====================================================================
//  TURRET AI  v1.0  - drives the rotor + hinge turrets of the warships (tools/designs/turrets.py, docs/design/turrets.md).
//  For Space Engineers (vanilla). Paste into ONE Programmable Block named "[TU] Turret PB" on the ship's main grid. Update1. Needs no other script, but is fed by the Helm.
//
//  A TURRET = azimuth stator "[TU] T<n> Azimuth" (axis = the stator's Up) -> top grid -> hinge "[TU] T<n> Elevation" (axis = the hinge's Up, horizontal) -> head grid with
//  fixed guns "[TU] T<n> Gun <k>" and a camera "[TU] T<n> Camera". At angle 0/0 the guns point along the azimuth axis (straight out of the hull); the hinge runs -90..+90,
//  so a turret covers the half-space on its outward side. Pointing it at a direction d: the hinge axis must be perpendicular to both the azimuth axis a and d, i.e.
//  +-(a x d); the azimuth turns the hinge axis there and the hinge opens by angle(a, d). (The angle conventions come from the owner's Tech Hunter: the top turns
//  by -Angle about the stator's Up; calibrated at start-up anyway.)
//
//  TARGETS (merged by entity id, extrapolated with velocity and a capped acceleration):
//   <Ship Tag>.H2S      the Helm's selected target (5 Hz, TransmissionDistance.CurrentConstruct): MyTuple<int flags, long id, Vector3D pos, Vector3D vel, float radius, float batt>
//   <TAG>_TGT           the fleets' target feeds (same tuple as everywhere): MyTuple<long id, Vector3D pos, Vector3D vel, double radius, long sender>; radius < 0 = finished
//   the ship's vanilla turrets and Custom Turret Controllers ("designators"): what they track, polled 5 Hz
//  FIRE MODE  FREE (every hostile track in range)  HELM (only the Helm's target)  HOLD (never fire)   - Custom Data or the arguments "free" / "helm" / "hold"
//  FIRE GATE  bore within the hit cone of the aim point, range <= Range, track younger than 0.6 s, the bore's line of fire clear of the ship's own hull (generated
//   Fire Mask per mount), a camera ray along the bore that does not meet a friendly (1 s hold), mode allows it.
//  PARK  no target for Park Delay s: hinge to Stow El (0 = guns straight out), azimuth stays. COLLISION: the generated Move Mask (hull vs the swinging head) stops an axis.
//
//  ARGUMENTS  free | helm | hold | park | reload | status
//  CUSTOM DATA  [Ship] Tag ([Link] Ship Tag of the Helm, default SHIP)
//   [Turret]  Prefix [TU], Listen Tags (fleet feeds), Fire Mode, Range 700, Park Delay 8, Gain 4, Max Rate 3, Max Accel 12, Torque, Braking Torque, Lead Accel 0.5, Horizon Margin 0.05
//   [Mount Tn]  Move Mask / Fire Mask (36 azimuth rows of 5 hex digits, bit j = hinge bin j of 19 (-90..+90 in 10 deg), 1 = blocked; written by the generator), Stow El, Rate Scale
//  NOT MEASURED IN THE GAME: the tops attach after the paste, the motor sign conventions, the rotor velocity limit (30 rpm large / 60 rpm small assumed), whether ammunition passes
//   the rotor and hinge (the guns carry their own), the small-grid stators (Shrike, Corsair), the mask accuracy. See packages/unknowns.md.
// =====================================================================

const string VER = "1.0";
const int AzBins = 36, ElBins = 19;
const double MuzzleLen = 1.0, BulletSpeed = 800;

MyIni _ini = new MyIni();
string Pfx = "[TU]", ShipTag = "SHIP", FireMode = "FREE", ListenRaw = "SHRIKE,HORNET,WASP,LANCER,HARRIER,AEGIS,LONGBOW,MANTIS,BASILISK,GNAT,WATCHER", _err = "", _lastArg = "", _tagKey = "";
double Range = 700, ParkDelay = 8, Gain = 4, MaxRate = 3, MaxAccel = 12, TorqueCfg = 1e6, BrakeCfg = 1e5, LeadAccel = 0.5, HorizonMargin = 0.05;
double _now, _lastDisc = -99, _lastTur = -99, _lastHb = -99, _lastEcho = -99; long _tick;
Vector3D _hostV, _lastPos; bool _hasPos;
long _helmId; double _helmSeen = -99; int _helmFlags;

class Trk
{
    public long Id; public Vector3D P, V, A; public double R = 5, Seen, Born, Closing; public string Src = ""; public int Assigned; public bool Missile;
    public Vector3D At(double now) { double dt = Math.Min(now - Seen, 2.0); return P + V * dt; }
}
class Mt
{
    public int N; public IMyMotorStator Az, El; public IMyCameraBlock Cam; public List<IMyUserControllableGun> Guns = new List<IMyUserControllableGun>();
    public int[] Move = new int[AzBins], Fire = new int[AzBins]; public bool HasMasks;
    public string State = "NEW", Why = ""; public double StowEl, RateScale = 1, CmdA, CmdE, T, NoTgtSince, HoldUntil, RayT, CalT, Aim, Range;
    public double SgnA = 1, SgnE = 1, CalA0, CalE0; public int Cal; public long Tid; public bool Shooting, Below; public double ShootT;
}
Dictionary<long, Trk> _trk = new Dictionary<long, Trk>();
List<Mt> _mt = new List<Mt>();
List<IMyMotorStator> _mot = new List<IMyMotorStator>();
List<IMyUserControllableGun> _gunAll = new List<IMyUserControllableGun>();
List<IMyCameraBlock> _camAll = new List<IMyCameraBlock>();
List<IMyShipController> _ctl = new List<IMyShipController>();
List<IMyLargeTurretBase> _vt = new List<IMyLargeTurretBase>();
List<IMyTurretControlBlock> _ctc = new List<IMyTurretControlBlock>();
List<IMyBroadcastListener> _lst = new List<IMyBroadcastListener>();
IMyBroadcastListener _lH2S;
List<long> _drop = new List<long>();
StringBuilder _sb = new StringBuilder();

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    try { LoadConfig(); Discover(); Listen(); } catch (Exception ex) { _err = "setup: " + ex.Message; Echo(_err); }
}

string C(string s, string k, string d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToString(d); }
double C(string s, string k, double d) { if (!_ini.ContainsKey(s, k)) _ini.Set(s, k, d); return _ini.Get(s, k).ToDouble(d); }

void LoadConfig()
{
    MyIniParseResult r; if (!_ini.TryParse(Me.CustomData ?? "", out r)) { _err = "Custom Data line " + r.LineNo; return; }
    const string S = "Turret";
    string tg = _ini.Get("Link", "Ship Tag").ToString(_ini.Get("Ship", "Tag").ToString(ShipTag));
    ShipTag = C(S, "Ship Tag", tg == "" ? ShipTag : tg).Trim().ToUpperInvariant();
    Pfx = C(S, "Prefix", Pfx).Trim(); ListenRaw = C(S, "Listen Tags", ListenRaw);
    FireMode = C(S, "Fire Mode", FireMode).Trim().ToUpperInvariant();
    Range = C(S, "Range", Range); ParkDelay = C(S, "Park Delay", ParkDelay); Gain = C(S, "Gain", Gain); MaxRate = C(S, "Max Rate", MaxRate); MaxAccel = C(S, "Max Accel", MaxAccel);
    TorqueCfg = C(S, "Torque", TorqueCfg); BrakeCfg = C(S, "Braking Torque", BrakeCfg); LeadAccel = C(S, "Lead Accel", LeadAccel); HorizonMargin = C(S, "Horizon Margin", HorizonMargin);
    string o = _ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}

static int Mask(string s, int row)
{
    int v = 0; if (s.Length < (row + 1) * 5) return 0;
    for (int i = 0; i < 5; i++) { int d; if (!int.TryParse(s.Substring(row * 5 + i, 1), System.Globalization.NumberStyles.HexNumber, null, out d)) d = 0; v = v * 16 + d; }
    return v;
}

void Listen()
{
    string key = ShipTag + "|" + ListenRaw;
    if (key == _tagKey) return;
    _tagKey = key;
    foreach (var l in _lst) IGC.DisableBroadcastListener(l);
    _lst.Clear();
    if (_lH2S != null) IGC.DisableBroadcastListener(_lH2S);
    _lH2S = IGC.RegisterBroadcastListener(ShipTag + ".H2S");
    foreach (var t in ListenRaw.Split(','))
    {
        string tt = t.Trim().ToUpperInvariant(); if (tt == "") continue;
        _lst.Add(IGC.RegisterBroadcastListener(tt.IndexOf('_') >= 0 ? tt : tt + "_TGT"));
    }
}

// parse "[TU] T3 Azimuth" -> 3, "Azimuth"
bool NameOf(string name, out int n, out string role)
{
    n = 0; role = "";
    if (!name.StartsWith(Pfx + " T", StringComparison.OrdinalIgnoreCase)) return false;
    string rest = name.Substring(Pfx.Length + 2); int sp = rest.IndexOf(' ');
    if (sp <= 0 || !int.TryParse(rest.Substring(0, sp), out n)) return false;
    role = rest.Substring(sp + 1).Trim(); return true;
}

void Discover()
{
    _lastDisc = _now;
    GridTerminalSystem.GetBlocksOfType(_mot, b => b.IsSameConstructAs(Me) && b.CustomName.StartsWith(Pfx, StringComparison.OrdinalIgnoreCase));
    GridTerminalSystem.GetBlocksOfType(_gunAll, b => b.IsSameConstructAs(Me) && b.CustomName.StartsWith(Pfx, StringComparison.OrdinalIgnoreCase));
    GridTerminalSystem.GetBlocksOfType(_camAll, b => b.IsSameConstructAs(Me) && b.CustomName.StartsWith(Pfx, StringComparison.OrdinalIgnoreCase));
    GridTerminalSystem.GetBlocksOfType(_ctl, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_vt, b => b.IsSameConstructAs(Me) && !b.CustomName.StartsWith(Pfx, StringComparison.OrdinalIgnoreCase));
    GridTerminalSystem.GetBlocksOfType(_ctc, b => b.IsSameConstructAs(Me));
    var nm = new List<Mt>();
    foreach (var az in _mot)
    {
        int n; string role;
        if (!NameOf(az.CustomName, out n, out role) || role != "Azimuth") continue;
        Mt m = null; foreach (var o in _mt) if (o.N == n) m = o;
        bool fresh = m == null; if (fresh) m = new Mt { N = n };
        m.Az = az; m.El = null; m.Cam = null; m.Guns.Clear();
        foreach (var e in _mot) { int n2; string r2; if (NameOf(e.CustomName, out n2, out r2) && n2 == n && r2 == "Elevation") m.El = e; }
        foreach (var g in _gunAll) { int n2; string r2; if (NameOf(g.CustomName, out n2, out r2) && n2 == n && r2.StartsWith("Gun")) m.Guns.Add(g); }
        foreach (var c in _camAll) { int n2; string r2; if (NameOf(c.CustomName, out n2, out r2) && n2 == n && r2.StartsWith("Camera")) m.Cam = c; }
        string sec = "Mount T" + n;
        m.StowEl = C(sec, "Stow El", 0.0); m.RateScale = C(sec, "Rate Scale", 1.0);
        string mv = C(sec, "Move Mask", ""), fr = C(sec, "Fire Mask", "");
        m.HasMasks = mv.Length >= AzBins * 5 && fr.Length >= AzBins * 5;
        for (int i = 0; i < AzBins; i++) { m.Move[i] = m.HasMasks ? Mask(mv, i) : 0; m.Fire[i] = m.HasMasks ? Mask(fr, i) : 0; }
        if (m.El == null || m.Guns.Count == 0) { m.State = "OFF"; m.Why = m.El == null ? "no hinge" : "no guns"; }
        else if (m.State == "OFF" || m.State == "NEW") { m.State = "CAL"; m.Cal = 0; m.Why = ""; }
        nm.Add(m);
    }
    foreach (var m in _mt) if (!nm.Contains(m)) Shoot(m, false);
    _mt = nm;
    foreach (var m in _mt)
    {
        if (m.Az == null || m.El == null) continue;
        if (!m.Az.IsAttached) m.Az.Attach(); if (!m.El.IsAttached) m.El.Attach();
        m.Az.Torque = (float)TorqueCfg; m.Az.BrakingTorque = (float)BrakeCfg; m.El.Torque = (float)TorqueCfg; m.El.BrakingTorque = (float)BrakeCfg;
        m.Az.RotorLock = false; m.El.RotorLock = false;
        if (m.Cam != null) m.Cam.EnableRaycast = true;
        m.El.LowerLimitRad = -1.5708f; m.El.UpperLimitRad = 1.5708f;
    }
    string o2 = _ini.ToString(); if (o2 != Me.CustomData) Me.CustomData = o2;
}

// ---------------------------------------------------------------- tracks
void Upsert(long id, Vector3D p, Vector3D v, double r, string src, bool keepV)
{
    Trk t; if (!_trk.TryGetValue(id, out t)) { t = new Trk { Id = id, Born = _now, Seen = _now, P = p, V = v }; _trk[id] = t; }
    double dt = _now - t.Seen;
    if (t.Src != "" && dt > 0.04 && dt < 1.5)
    {
        Vector3D v2 = v;
        if (v.LengthSquared() < 0.01) v2 = (p - t.P) / dt * 0.5 + t.V * 0.5;                // a source without velocity (turret designators)
        if (!keepV) { Vector3D a = (v2 - t.V) / dt; double am = a.Length(); if (am > 30) a *= 30 / am; t.A = t.A * 0.6 + a * 0.4; }
        v = v2;
    }
    t.P = p; t.V = v; if (r > 0) t.R = r; t.Seen = _now; t.Src = src;
}

void ReadIgc()
{
    while (_lH2S != null && _lH2S.HasPendingMessage)
    {
        var m = _lH2S.AcceptMessage();
        if (m.Data is MyTuple<int, long, Vector3D, Vector3D, float, float>)
        {
            var d = (MyTuple<int, long, Vector3D, Vector3D, float, float>)m.Data;
            _helmFlags = d.Item1;
            if (d.Item2 != 0) { _helmId = d.Item2; _helmSeen = _now; Upsert(d.Item2, d.Item3, d.Item4, d.Item5, "HELM", false); }
        }
    }
    foreach (var l in _lst)
        while (l.HasPendingMessage)
        {
            var m = l.AcceptMessage();
            if (!(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
            if (d.Item1 == 0 || d.Item5 == Me.EntityId) continue;
            if (d.Item4 < 0) { _trk.Remove(d.Item1); continue; }
            Upsert(d.Item1, d.Item2, d.Item3, d.Item4, "IGC", false);
        }
}

void ReadTurrets()
{
    _lastTur = _now;
    foreach (var t in _vt)
    {
        if (!t.IsWorking || !t.HasTarget) continue;
        var e = t.GetTargetedEntity(); if (e.IsEmpty()) continue;
        AddInfo(e, "TUR");
    }
    foreach (var t in _ctc)
    {
        if (!t.IsWorking || !t.HasTarget) continue;
        var e = t.GetTargetedEntity(); if (e.IsEmpty()) continue;
        AddInfo(e, "CTC");
    }
}

void AddInfo(MyDetectedEntityInfo e, string src)
{
    if (e.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) return;
    if (e.Type != MyDetectedEntityType.SmallGrid && e.Type != MyDetectedEntityType.LargeGrid && e.Type != MyDetectedEntityType.Missile) return;
    Vector3D p = e.HitPosition.HasValue ? e.HitPosition.Value : e.Position;
    double r = Math.Max(1, Math.Min(60, 0.5 * (e.BoundingBox.Max - e.BoundingBox.Min).Length() / 1.7));
    Upsert(e.EntityId, p, e.Velocity, r, src, false);
    Trk t; if (_trk.TryGetValue(e.EntityId, out t)) t.Missile = e.Type == MyDetectedEntityType.Missile;
}

void Prune()
{
    _drop.Clear();
    foreach (var kv in _trk) if (_now - kv.Value.Seen > 2.5) _drop.Add(kv.Key);
    foreach (var k in _drop) _trk.Remove(k);
    foreach (var kv in _trk) kv.Value.Assigned = 0;
}

// ---------------------------------------------------------------- aiming
// first-order intercept of a bullet (speed BulletSpeed relative to the shooter) with a target that has velocity and a decaying acceleration
bool Lead(Trk t, Vector3D muz, out Vector3D aim, out double tof)
{
    Vector3D r0 = t.At(_now) - muz, vr = t.V - _hostV, a = t.A * LeadAccel;
    double T = r0.Length() / BulletSpeed;
    for (int i = 0; i < 5; i++)
    {
        Vector3D r = r0 + vr * T + a * (0.5 * T * T);
        double nt = r.Length() / BulletSpeed;
        if (Math.Abs(nt - T) < 1e-4) { T = nt; break; }
        T = nt;
    }
    aim = r0 + vr * T + a * (0.5 * T * T); tof = T;
    return aim.LengthSquared() > 1 && T < 4;
}

static double SA(Vector3D u, Vector3D v, Vector3D ax) { return Math.Atan2(Vector3D.Dot(ax, Vector3D.Cross(u, v)), Vector3D.Dot(u, v)); }
static double Wrap(double x) { while (x > Math.PI) x -= 2 * Math.PI; while (x < -Math.PI) x += 2 * Math.PI; return x; }
static bool Blocked(int[] mask, double az, double el)
{
    int ai = (int)Math.Round(MathHelper.ToDegrees(Wrap(az) < 0 ? Wrap(az) + 2 * Math.PI : Wrap(az)) / 10.0) % AzBins;
    int ej = (int)Math.Round((MathHelper.ToDegrees(el) + 90) / 10.0); if (ej < 0) ej = 0; if (ej >= ElBins) ej = ElBins - 1;
    return ((mask[ai] >> ej) & 1) != 0;
}

Vector3D MuzzleOf(Mt m, out Vector3D bore)
{
    Vector3D p = Vector3D.Zero, f = Vector3D.Zero; int n = 0;
    foreach (var g in m.Guns) if (g.IsFunctional) { p += g.GetPosition(); f += g.WorldMatrix.Forward; n++; }
    if (n == 0) { bore = m.Az.WorldMatrix.Up; return m.Az.GetPosition(); }
    bore = Vector3D.Normalize(f); return p / n + bore * MuzzleLen;
}

bool MayFire(Trk t)
{
    if (FireMode == "HOLD") return false;
    if (FireMode == "HELM") return t.Id == _helmId && _now - _helmSeen < 2;
    return true;
}

double Score(Mt m, Trk t, Vector3D muz, Vector3D bore, Vector3D a, out Vector3D d, out double rng)
{
    d = Vector3D.Zero; rng = 0; Vector3D aim; double tof;
    if (_now - t.Seen > 1.5 || !Lead(t, muz, out aim, out tof)) return -1e9;
    rng = aim.Length(); if (rng > Range * 1.15) return -1e9;
    d = aim / rng;
    double cosphi = Vector3D.Dot(a, d);
    if (cosphi < -Math.Sin(HorizonMargin)) return -1e9;                                    // below this mount's horizon: the hull is in the way
    Vector3D rv = t.V - _hostV, rn = (t.At(_now) - muz); double dist = Math.Max(1, rn.Length()); rn /= dist;
    double closing = -Vector3D.Dot(rv, rn); t.Closing = closing;
    double sc = 100 - dist / 10 - 20 * Math.Acos(MathHelper.Clamp(Vector3D.Dot(bore, d), -1, 1)) / Math.PI;
    if (t.Id == _helmId && _now - _helmSeen < 2) sc += 200;
    if (closing > 30 && dist / closing < 12) sc += 600 + closing;                          // inbound: missile, kamikaze, diving fighter
    if (t.R < 8) sc += 100;
    if (t.Missile) sc += 400;
    sc -= 150 * Math.Max(0, t.Assigned - 2);
    return sc;
}

void Control(Mt m, double dt)
{
    if (m.State == "OFF") return;
    if (!m.Az.IsFunctional || !m.El.IsFunctional || !m.Az.IsAttached || !m.El.IsAttached) { Stop(m); m.Why = !m.Az.IsAttached || !m.El.IsAttached ? "top detached" : "stator damaged"; return; }
    Vector3D a = m.Az.WorldMatrix.Up, h = m.El.WorldMatrix.Up, bore, muz = MuzzleOf(m, out bore);
    double curA = m.Az.Angle, curE = m.El.Angle;
    if (m.State == "CAL") { Calibrate(m, dt, curA, curE); return; }
    // ---- pick a target (every 6th tick per mount)
    Trk best = null; double bs = -1e9; Vector3D d = Vector3D.Zero; double rng = 0;
    Trk cur = null; if (m.Tid != 0) _trk.TryGetValue(m.Tid, out cur);
    if (cur == null || (_tick + m.N) % 6 == 0)
    {
        foreach (var kv in _trk)
        {
            Vector3D d2; double r2; double s = Score(m, kv.Value, muz, bore, a, out d2, out r2);
            if (kv.Key == m.Tid) s += 40;                                                  // hysteresis
            if (s > bs) { bs = s; best = kv.Value; }
        }
        if (best != null && bs > -1e8) { if (best.Id != m.Tid) m.Tid = best.Id; cur = best; } else { cur = null; m.Tid = 0; }
    }
    bool have = false; double rateA = 0, rateE = 0, errAng = 9, tol = 1;
    if (cur != null)
    {
        cur.Assigned++;
        double sc = Score(m, cur, muz, bore, a, out d, out rng);
        have = sc > -1e8;
    }
    if (have)
    {
        m.NoTgtSince = _now; m.State = "TRACK";
        Vector3D c = Vector3D.Cross(a, d); double sn = c.Length(), cs = Vector3D.Dot(a, d), phi = Math.Atan2(sn, cs);
        m.Below = phi > Math.PI / 2; if (m.Below) phi = Math.PI / 2;
        Vector3D hd = sn > 1e-3 ? c / sn : h; double sg = Vector3D.Dot(h, hd) >= 0 ? 1 : -1; hd *= sg;
        double ea = phi < 0.04 ? 0 : -SA(h, hd, a), ee = (-sg * phi) - curE;
        // feed-forward from the line-of-sight rate
        Vector3D rv = cur.V - _hostV, rn = Vector3D.Normalize(cur.At(_now) - muz); double dist = Math.Max(20, (cur.At(_now) - muz).Length());
        Vector3D dd = Vector3D.Cross(Vector3D.Cross(rn, rv) / dist, d);
        double ffa = 0, ffe = 0;
        if (sn > 0.12)
        {
            Vector3D dp = d - a * cs; double dpl = dp.LengthSquared();
            ffa = -Vector3D.Dot(a, Vector3D.Cross(dp, dd)) / dpl; ffe = sg * Vector3D.Dot(a, dd) / sn;
        }
        rateA = Gain * ea + ffa; rateE = Gain * ee + ffe;
        errAng = Math.Acos(MathHelper.Clamp(Vector3D.Dot(bore, d), -1, 1));
        m.Aim = errAng;
        double rad = Math.Max(1.0, cur.R);
        tol = MathHelper.Clamp(Math.Atan((0.5 * rad + 0.4) / Math.Max(rng, 30)), 0.004, 0.06);
        m.Range = rng;
    }
    else
    {
        if (m.State == "TRACK") { m.State = "PARK"; }
        Shoot(m, false);
        if (_now - m.NoTgtSince > ParkDelay) { m.State = "PARK"; rateE = Gain * (m.StowEl - curE); }
        else rateE = 0;
        rateA = 0;
    }
    // ---- limits: the hull mask, the slew limit, the rate cap
    double mr = MaxRate * m.RateScale;
    rateA = MathHelper.Clamp(rateA, -mr, mr); rateE = MathHelper.Clamp(rateE, -mr, mr);
    if (m.HasMasks)
    {
        if (!Blocked(m.Move, curA, curE))
        {
            if (Blocked(m.Move, curA + rateA * 0.12, curE)) rateA = 0;
            if (Blocked(m.Move, curA, curE + rateE * 0.12)) rateE = 0;
            if (Blocked(m.Move, curA + rateA * 0.12, curE + rateE * 0.12)) { rateA = 0; rateE = 0; }
        }
    }
    if ((curE > 1.55 && rateE > 0) || (curE < -1.55 && rateE < 0)) rateE = 0;
    double ma = MaxAccel * dt;
    m.CmdA += MathHelper.Clamp(rateA - m.CmdA, -ma, ma); m.CmdE += MathHelper.Clamp(rateE - m.CmdE, -ma, ma);
    m.Az.TargetVelocityRad = (float)(m.CmdA * m.SgnA); m.El.TargetVelocityRad = (float)(m.CmdE * m.SgnE);
    // ---- fire gate
    bool fire = have && !m.Below && errAng <= tol && rng <= Range && _now - cur.Seen < 0.6 && MayFire(cur) && _now >= m.HoldUntil
        && !(m.HasMasks && Blocked(m.Fire, curA, curE));
    if (fire && _now - m.RayT > 0.5) { m.RayT = _now; FriendlyCheck(m, rng); fire = _now >= m.HoldUntil; }
    Shoot(m, fire);
}

void FriendlyCheck(Mt m, double rng)
{
    if (m.Cam == null || !m.Cam.IsFunctional || !m.Cam.CanScan(rng)) return;
    var e = m.Cam.Raycast(rng, 0, 0);
    if (e.IsEmpty()) return;
    long g = Me.CubeGrid.EntityId;
    if (e.EntityId == g || e.EntityId == m.Az.CubeGrid.EntityId || e.EntityId == m.El.CubeGrid.EntityId || e.EntityId == m.Cam.CubeGrid.EntityId) return;
    if (e.Relationship == MyRelationsBetweenPlayerAndBlock.Owner || e.Relationship == MyRelationsBetweenPlayerAndBlock.FactionShare || e.Relationship == MyRelationsBetweenPlayerAndBlock.Friends)
        m.HoldUntil = _now + 1.0;
}

void Shoot(Mt m, bool on)
{
    if (on == m.Shooting && _now - m.ShootT < 1.0) return;
    m.Shooting = on; m.ShootT = _now;
    foreach (var g in m.Guns) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off");
}

void Stop(Mt m)
{
    if (m.Az != null) m.Az.TargetVelocityRad = 0;
    if (m.El != null) m.El.TargetVelocityRad = 0;
    m.CmdA = m.CmdE = 0; Shoot(m, false);
}

// quick sign check: +0.35 rad/s on the hinge for 10 ticks, then on the azimuth; the motor's Angle must increase (else the sign is flipped). 25 ticks in all.
void Calibrate(Mt m, double dt, double curA, double curE)
{
    m.CalT += dt;
    if (m.Cal == 0) { m.CalA0 = curA; m.CalE0 = curE; m.Cal = 1; m.CalT = 0; }
    if (m.Cal == 1)
    {
        m.El.TargetVelocityRad = 0.35f; m.Az.TargetVelocityRad = 0;
        if (m.CalT > 0.25)
        {
            double d = curE - m.CalE0; m.SgnE = d >= 0 ? 1 : -1; if (Math.Abs(d) < 0.01) { m.Why = "hinge did not move"; }
            m.El.TargetVelocityRad = 0; m.Cal = 2; m.CalT = 0;
        }
    }
    else if (m.Cal == 2)
    {
        m.Az.TargetVelocityRad = 0.35f;
        if (m.CalT > 0.25)
        {
            double d = Wrap(curA - m.CalA0); m.SgnA = d >= 0 ? 1 : -1; if (Math.Abs(d) < 0.01) { m.Why = "azimuth did not move"; }
            m.Az.TargetVelocityRad = 0; m.Cal = 0; m.State = "PARK"; m.NoTgtSince = _now - ParkDelay;
        }
    }
}

// ---------------------------------------------------------------- main
void Command(string arg)
{
    string a = (arg ?? "").Trim().ToLowerInvariant();
    if (a == "free" || a == "helm" || a == "hold") { FireMode = a.ToUpperInvariant(); _ini.Set("Turret", "Fire Mode", FireMode); Me.CustomData = _ini.ToString(); }
    else if (a == "park") { foreach (var m in _mt) { m.Tid = 0; m.NoTgtSince = -99; } _trk.Clear(); }
    else if (a == "reload") { _tagKey = ""; _mt.Clear(); LoadConfig(); Discover(); Listen(); }
}

public void Main(string argument, UpdateType updateSource)
{
    try
    {
        double dt = Runtime.TimeSinceLastRun.TotalSeconds; if (dt <= 0 || dt > 0.5) dt = 1.0 / 60;
        _now += dt; _tick++;
        if (!string.IsNullOrEmpty(argument)) { _lastArg = argument; Command(argument); }
        if (_now - _lastDisc > 5) { LoadConfig(); Discover(); Listen(); }
        // host velocity: any ship controller, else the PB's own motion
        Vector3D pos = Me.GetPosition();
        IMyShipController c = null; foreach (var x in _ctl) if (x.IsFunctional) { c = x; break; }
        if (c != null) _hostV = c.GetShipVelocities().LinearVelocity;
        else { if (_hasPos) _hostV = _hostV * 0.8 + (pos - _lastPos) / dt * 0.2; }
        _lastPos = pos; _hasPos = true;
        ReadIgc();
        if (_now - _lastTur > 0.2) ReadTurrets();
        Prune();
        int n = 0;
        foreach (var m in _mt)
        {
            if (((_tick + n++) & 1) != 0 && m.State != "CAL") continue;
            if (Runtime.CurrentInstructionCount > 30000) break;
            double d2 = _now - m.T; if (d2 <= 0 || d2 > 0.5) d2 = 2.0 / 60; m.T = _now;
            try { Control(m, d2); } catch (Exception ex) { Stop(m); m.State = "OFF"; m.Why = "exc: " + ex.Message; _err = m.Why; }
        }
        if (_now - _lastHb > 1)
        {
            _lastHb = _now; int act = 0, fi = 0; foreach (var m in _mt) { if (m.State == "TRACK") act++; if (m.Shooting) fi++; }
            IGC.SendBroadcastMessage(ShipTag + ".TUR", new MyTuple<int, int, int>(_mt.Count, act, fi), TransmissionDistance.CurrentConstruct);
        }
        if (_now - _lastEcho > 0.5) { _lastEcho = _now; Show(); }
    }
    catch (Exception ex)
    {
        _err = "main: " + ex.Message; foreach (var m in _mt) Stop(m);
        Echo(_err);
    }
}

void Show()
{
    _sb.Clear();
    _sb.Append("TURRET AI v").Append(VER).Append("  ").Append(ShipTag).Append("  fire ").Append(FireMode).Append("  tracks ").Append(_trk.Count)
       .Append("  IL ").Append(Runtime.CurrentInstructionCount).Append('\n');
    if (_err != "") _sb.Append("ERR ").Append(_err).Append('\n');
    foreach (var m in _mt)
    {
        _sb.Append('T').Append(m.N).Append(' ').Append(m.State);
        if (m.Az != null && m.El != null) _sb.Append(" az ").Append(MathHelper.ToDegrees(m.Az.Angle).ToString("0")).Append(" el ").Append(MathHelper.ToDegrees(m.El.Angle).ToString("0"));
        if (m.State == "TRACK") _sb.Append(" tgt ").Append(m.Tid % 10000).Append(" err ").Append(MathHelper.ToDegrees(m.Aim).ToString("0.0")).Append(" r ").Append(m.Range.ToString("0"));
        if (m.Shooting) _sb.Append(" FIRE");
        if (m.Below) _sb.Append(" below");
        if (!m.HasMasks) _sb.Append(" nomask");
        if (m.Why != "") _sb.Append(" (").Append(m.Why).Append(')');
        _sb.Append('\n');
    }
    string s = _sb.ToString(); Echo(s);
    var sf = Me.GetSurface(0); if (sf != null) { sf.ContentType = ContentType.TEXT_AND_IMAGE; sf.FontSize = 0.8f; sf.WriteText(s); }
}
