// >>> BATTERY v1.1 (weapon batteries, one lead solver per class, primary choice, standoff, turret-only mode; docs/design/control_arch.md s3, tools/blocks_sync.py) >>>
// Fixed guns (never turrets) form up to four batteries by the tag in the block name: [B1] .. [B4]. An untagged gun joins by class: ballistic (gatling, autocannon) B1, rocket B2, railgun B3,
// shell (artillery, assault cannon) B4. The class of a battery (BALL ROCK RAIL SHEL: speed, range, minimum range, shot interval, reload, dps, dispersion) comes from its first gun's
// definition id (BatSpec); a battery of mixed guns is treated as its first gun's class.
// PRIMARY battery = the one Control() / Aim() orient the ship by (ProjSpeed, MaxFireRange and _aimRef follow it, and with them the script's own LEAD solver): argmax of
//   score = dps x guns x P(hit) x P(ready)   at the present range, re-evaluated every 10 ticks, switched only after 3 s and when the newcomer scores 25 % more (BatPick).
//   P(hit) = clamp(1 - (LeadMiss(track, T) x Miss Scale + dispersion x range) / track radius, 0, 1), T = range / projectile speed; P(ready): a railgun / shell battery counts when its
//   next shot is due within 1.5 s (the confirmed-shot state machine of the Longbow: ammo drop, IsShooting, retry), other batteries are always ready.
// SECONDARY batteries fire on their own lead solution (BatAim: this class' ProjSpeed through the same Lead()) whenever THEIR boresight is inside the silhouette window, in their own
// range band and the line is clear; continuous fire (Shoot_On/Off) for ballistic guns, ShootOnce for the others (BatShoot). The primary fires through the script's own fire decision.
// STANDOFF (BatStand, every 30 ticks, only on a ship with a non-ballistic battery and Standoff By Weapons=true): the range on a 100 m grid that maximises the sum of the scores minus
//   Threat Weight x total dps x threat(r), threat = 2.5 inside 450 m, 1.0 inside 1,000 m, 0.2 beyond (the enemy is assumed to carry gatlings / rockets of 800 m): a rail or artillery ship
//   kites at 1,000 m, a rocket + gatling ship closes to ~500 m.
// SALVO ([Battery] Salvo OFF/ON/AUTO, default OFF; AUTO = single-shot batteries of 6 or more guns): instead of one gun per slot every working gun of the battery gets ShootOnce in the same tick; one second later the guns whose
//   magazine shrank are counted: 70 % or more = the whole battery recharges together (next salvo after Reload x 1.1 + 0.5 s), fewer = the rest are still charging (ask again in 4 s), none = ask again in 2 s.
// TURRET-ONLY MODE (Turret Only AUTO = the ship has turrets, ON, OFF): a track that every working battery would hit with P < 0.15 is left to the turrets: the fixed batteries refuse it
//   (BatOk) and target selection puts it behind every track a battery can hit (BatHope, debounced 2 s). The Track class carries the field "public double Hl = -99;".
// TURN-RATE CALIBRATION (BatTurn, Auto Max Turn AUTO = large grids, ON, OFF): while the first slews saturate the gyros, the peak angular acceleration a is measured; then
//   MaxTurn = 0.85 x sqrt(2 a x 0.6 rad) and AimKp = MaxTurn / 0.6 (the ship can stop inside the 0.6 rad PD zone); stored in Storage (field 8).
// Hooks: BatCfg() at the end of LoadConfig; BatSort() at the end of GetBlocks (after _guns); PrimGuns() instead of _guns for the gun centre in Control() and for SetFire(); SetFire() starts with
//   "if (_bat[_prim].Cool > 0) { _firing = on; BatShoot(_bat[_prim], on); return; }"; BatOk(_bat[_prim], dist) in the primary's fire decision (Control); BatFire(haveAim, myVel) after it
//   (BatFire(false, ..) in IDLE, Release and ManFly); "+ BatHope(t) + BatKz(t, dist)" in the target score of PruneAndSelect; BatTurn(w) in Aim(); BatSave() / BatLoad(..) for Storage; BatStatus() in Display.
class Bat
{
    public string Cls = "-"; public double Speed = 800, Range = 700, MinR, Cool, Rel, Dps, Dsp, Next, Rdy, Pend = -1, Am, Sc; public bool On, Saw, Want, Sv; public int I; public IMyUserControllableGun Gn; public long[] Ar;
    public List<IMyUserControllableGun> G = new List<IMyUserControllableGun>();
}
Bat[] _bat = { new Bat(), new Bat(), new Bat(), new Bat() };
int _pd; bool PdEx = true; double KzHold = 0; double PdRad = 6;   // point-defence battery (1-based, 0 = none) and the largest track radius it counts as "small"
bool _rkt; int _prim, _batFix; double _primT = -99, _bs0, _br0; bool BatSo = true; string BatTur = "AUTO", BatCal = "AUTO", BatSal = "OFF";
List<IMyUserControllableGun> PrimGuns() { return _bat[_prim].G; }

void BatCfg()
{
    const string S = "Battery";
    string p = Cfg(S, "Primary (AUTO/B1..B4)", "AUTO").Trim().ToUpperInvariant();
    _batFix = p.Length == 2 && p[0] == 'B' && p[1] >= '1' && p[1] <= '4' ? p[1] - '0' : 0;
    BatSo = Cfg(S, "Standoff By Weapons", BatSo);
    LooseFar = Cfg(S, "Loose Fire Beyond Standoff", LooseFar);
    BatTur = Cfg(S, "Turret Only (AUTO/ON/OFF)", BatTur).Trim().ToUpperInvariant();
    BatCal = Cfg(S, "Auto Max Turn (AUTO/ON/OFF)", BatCal).Trim().ToUpperInvariant();
    BatSal = Cfg(S, "Salvo (AUTO/ON/OFF)", BatSal).Trim().ToUpperInvariant();
    string pd = Cfg(S, "Point Defence (OFF/B1..B4)", "OFF").Trim().ToUpperInvariant();   // that battery shoots only small / incoming-kamikaze tracks, one gun at a time; the other batteries leave those tracks alone
    _pd = pd.Length == 2 && pd[0] == 'B' && pd[1] >= '1' && pd[1] <= '4' ? pd[1] - '0' : 0;
    PdRad = Cfg(S, "Point Defence Max Radius (m)", PdRad); PdEx = Cfg(S, "Point Defence Exclusive", PdEx);
    KzHold = Cfg(S, "Kamikaze Rail Hold Range (m)", KzHold);   // 0 = off; > 0: railgun / shell batteries leave a classified incoming kamikaze alone beyond this range
}

void BatSp(Bat b, string c, double sp, double rg, double mr, double cool, double rel, double dps, double dsp)
{ b.Cls = c; b.Speed = sp; b.Range = rg; b.MinR = mr; b.Cool = cool; b.Rel = rel; b.Dps = dps; b.Dsp = dsp; }
bool BatBall(string s) { return s.Contains("Gatling") || s.Contains("Autocannon") || s.Contains("Interior"); }
void BatSpec(Bat b)
{
    b.Cls = "-";
    if (b.G.Count == 0) return;
    string s = b.G[0].BlockDefinition.ToString();   // "MyObjectBuilder_SmallGatlingGun/" (a gatling has no subtype), "...SmallMissileLauncherReload/SmallRailgun"
    bool big = s.Contains("Large");
    if (BatBall(s)) BatSp(b, "BALL", _bs0, _br0, 0, 0, 0, 1000, 0.004);
    else if (s.Contains("Calibre")) BatSp(b, "SHEL", 500, (big ? 2000 : 1400) * 0.85, 150, 1, big ? 12 : 6, big ? 500 : 300, 0.001);   // (before "Missile": the shell guns sit in launcher block types)
    else if (s.Contains("Railgun")) BatSp(b, "RAIL", big ? 2000 : 1000, (big ? 2000 : 1400) * 0.85, 150, 0.25, big ? 60 : 20, big ? 800 : 400, 0);
    else BatSp(b, "ROCK", 200, 680, 100, 0.35, 0, 500, 0.002);
}

int BatN(Bat b) { int n = 0; foreach (var g in b.G) if (g.IsFunctional) n++; return n; }

void BatSort()
{
    if (_bs0 == 0) { _bs0 = ProjSpeed; _br0 = MaxFireRange; }
    foreach (var b in _bat) b.G.Clear();
    foreach (var g in _guns)
    {
        string n = g.CustomName, s = g.BlockDefinition.ToString(); int k = -1;
        for (int i = 0; i < 4; i++) if (n.Contains("[B" + (i + 1) + "]")) k = i;
        if (k < 0) k = BatBall(s) ? 0 : s.Contains("Calibre") ? 3 : s.Contains("Railgun") ? 2 : 1;
        _bat[k].G.Add(g);
    }
    foreach (var b in _bat) { BatSpec(b); b.Sv = b.Rel > 0 && (BatSal == "ON" || (BatSal == "AUTO" && b.G.Count >= 6)) && !(_pd > 0 && b == _bat[_pd - 1]); }
    BatSet(_batFix > 0 && _bat[_batFix - 1].G.Count > 0 ? _batFix - 1 : _bat[_prim].G.Count > 0 ? _prim : BatFirst());
}
int BatFirst() { for (int i = 0; i < 4; i++) if (BatN(_bat[i]) > 0) return i; return 0; }

// make battery i the primary (also the refresh of ProjSpeed, MaxFireRange and the aim reference after a block was lost)
void BatSet(int i)
{
    if (i != _prim)
    {
        if (_firing || _bat[_prim].On) foreach (var g in _bat[_prim].G) if (g.IsFunctional) g.ApplyAction("Shoot_Off");   // (only what the script switched on: a pilot holding the trigger is not cut off by a pip-only primary switch)
        _bat[_prim].On = false; _firing = false; _primT = _now; _prim = i;
    }
    var b = _bat[i];
    ProjSpeed = b.Speed; MaxFireRange = b.Range; _rkt = b.Cls == "ROCK";
    IMyTerminalBlock r = _rc;
    foreach (var g in b.G) if (g.IsFunctional) { r = g; break; }
    _aimRef = r;
}

// unpredictable displacement (m) of the track after T s of flight (the Basilisk's FireGate "unc": the acceleration the lead cannot foresee)
double BatUnc(Track t, double T) { return 0.5 * Math.Max(0, t.AccMag - t.EA().Length()) * T * T; }
double BatPh(Bat b, double r, Track t) { return MathHelper.Clamp(1 - (BatUnc(t, r / b.Speed) + b.Dsp * r) / t.Radius, 0, 1); }
// score of battery b at range r; w = how much a battery that is not ready is discounted (1: not at all counted, 0.5: halved for the standoff planning)
double BatSc(Bat b, double r, double w)
{
    int n = BatN(b);
    if (n == 0 || r < b.MinR || r > b.Range || (_pd > 0 && b == _bat[_pd - 1])) return 0;
    return b.Dps * n * BatPh(b, r, _tgt) * (b.Rel > 0 && _now + 1.5 < b.Rdy ? 1 - w : 1);
}

void BatPick()
{
    if (_tgt == null || _rc == null) return;
    double r = Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()), bm = 0; int bi = -1, p = _prim;
    for (int i = 0; i < 4; i++) { _bat[i].Sc = BatSc(_bat[i], r, 1); if (_bat[i].Sc > bm) { bm = _bat[i].Sc; bi = i; } }
    if (_batFix > 0 && _bat[_batFix - 1].G.Count > 0) p = _batFix - 1;
    else if (BatN(_bat[p]) == 0) p = BatFirst();
    else if (bi >= 0 && bi != p && _now - _primT > 3 && _bat[bi].Sc > _bat[p].Sc * 1.25) p = bi;
    BatSet(p);
    if (_tick % 30 == 3) BatStand();
}

void BatStand()
{
    if (!BatSo || _tgt == null) return;
    double hi = 0, tot = 0; bool nb = false;
    foreach (var b in _bat) { int n = BatN(b); if (n == 0) continue; if (b.Cls != "BALL") nb = true; hi = Math.Max(hi, b.Range); tot += b.Dps * n; }
    double lo = Math.Max(MinRange, 60 + Me.CubeGrid.WorldVolume.Radius), bj = -1e9;
    if (!nb || hi < lo) return;
    for (double r = Math.Ceiling(lo / 100) * 100; r <= hi; r += 100)
    {
        double j = -tot * (r < 450 ? 2.5 : r < 1000 ? 1 : 0.2);
        foreach (var b in _bat) j += BatSc(b, r, 0.5);
        if (j > bj) { bj = j; Standoff = r; }
    }
}

// silhouette window (deg) of the tracked target at this range: the fire window of every battery
bool LooseFar = true;   // [Battery] Loose Fire Beyond Standoff: while still closing (range > Standoff + 100) the window is the whole silhouette, not the 3 m hull point
double BatWin(double dist) { double aimR = AimActive(_tgt) && !(LooseFar && dist > Standoff + 100) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6; return Math.Max(0.3, Math.Atan(aimR / Math.Max(dist, 1)) * 57.2958) * FireTol; }
bool BatTO() { return BatTur == "ON" || (BatTur == "AUTO" && _turrets.Count > 0); }
bool PdTgt(Track t) { return t != null && (KzLive(t.Id) || t.Radius <= PdRad); }
bool BatOk(Bat b, double d)
{
    if (_pd > 0 && (b == _bat[_pd - 1] ? !PdTgt(_tgt) : PdEx && PdTgt(_tgt))) return false;   // the point-defence battery shoots small / kamikaze tracks only; with PdEx the other batteries leave those tracks alone
    if (KzHold > 0 && (b.Cls == "RAIL" || b.Cls == "SHEL") && _tgt != null && KzLive(_tgt.Id) && d > KzHold) return false;   // slow-reloading heavy guns are not spent on an incoming kamikaze until it is this close
    if (b.Cls == "ROCK" && BatUnc(_tgt, d / 200) > Math.Max(_tgt.Radius * 0.8, 3)) return false;   // rockets cannot be recalled: not at a jinking target (FireGate)
    return !BatTO() || BatPh(b, d, _tgt) >= 0.15;
}
// target score (lower = better) of an incoming kamikaze that the heavy guns will not shoot yet (Kamikaze Rail Hold Range): its priority bonus is cancelled and it is
// ranked behind every other track, so the tracking stays free for a large ship that comes into range; with nothing else about it is still the target (the turrets answer it)
double BatKz(Track t, double dist) { return KzHold > 0 && dist > KzHold && KzLive(t.Id) ? 3000 - KzScore(t) : 0; }
double BatHope(Track t)
{
    if (!BatTO() || _rc == null) return 0;
    double r = Vector3D.Distance(t.P(_now), _rc.GetPosition());
    foreach (var b in _bat) if (BatN(b) > 0 && BatPh(b, r, t) >= 0.15) return _now - t.Hl < 2 ? 1500 : 0;   // (a good sample after a hopeless one counts after 2 s: the acceleration estimate swings)
    t.Hl = _now;
    return 1500;
}

// lead solution of a secondary battery: true when it may fire now
bool BatAim(Bat b, Vector3D myVel)
{
    if (_tgt == null) return false;
    Vector3D gp = Vector3D.Zero, pt, dir; int n = 0; IMyUserControllableGun f = null;
    foreach (var g in b.G) if (g.IsFunctional) { gp += g.GetPosition(); n++; if (f == null) f = g; }
    if (n == 0) return false;
    gp /= n;
    double sp = ProjSpeed, sl = _leadT; bool sr = _rkt; ProjSpeed = b.Speed; _rkt = b.Cls == "ROCK";
    bool ok = Lead(_tgt, gp, myVel, out pt, out dir);
    ProjSpeed = sp; _leadT = sl; _rkt = sr;
    if (!ok) return false;
    Vector3D fw = f.WorldMatrix.Forward;
    double dist = Vector3D.Distance(pt, gp), err = Math.Acos(MathHelper.Clamp(Vector3D.Dot(fw, dir), -1, 1)) * 57.2958;
    bool go = dist >= b.MinR && dist < b.Range && err < BatWin(dist) && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax && BatOk(b, dist);
    if (go && FriendCheck) { MatesInLine(gp, fw, dist, b.Cls == "RAIL" ? 0.5 : 0, b.Speed, myVel); if (_now < _holdFireUntil) go = false; }   // v1.1: the mate is checked where it will be when the slug passes (railgun 0.5 s charge + flight time)
    return go;
}

void BatFire(bool allow, Vector3D myVel)
{
    if (allow && _tick % 10 == 3) BatPick();
    for (int i = 0; i < 4; i++)
    {
        var b = _bat[i];
        if (i == _prim || b.G.Count == 0) continue;
        if (!allow) b.Want = false; else if ((_tick + i) % 3 == 0) b.Want = BatAim(b, myVel);
        BatShoot(b, b.Want);
    }
}

long BatAm(IMyUserControllableGun g) { var v = g.GetInventory(0); return v != null ? v.CurrentVolume.RawValue : 0; }
IMyUserControllableGun BatGun(Bat b) { for (int k = 0; k < b.G.Count; k++) { var g = b.G[++b.I % b.G.Count]; if (g.IsFunctional) return g; } return null; }

void BatShoot(Bat b, bool on)
{
    if (b.Pend >= 0) BatCheck(b);
    if (b.Cool > 0)
    {
        if (!on || _now < b.Next || (b.Rel > 0 && (b.Pend >= 0 || _now < b.Rdy))) return;
        b.Next = _now + b.Cool;
        if (b.Sv)
        {
            if (b.Ar == null || b.Ar.Length != b.G.Count) b.Ar = new long[b.G.Count];
            for (int k = 0; k < b.G.Count; k++) { var q = b.G[k]; if (q.IsFunctional) { b.Ar[k] = BatAm(q); q.ShootOnce(); } else b.Ar[k] = -1; }
            b.Pend = _now; b.Saw = false; b.Gn = null;
        }
        else if (b.Rel > 0)
        {
            var g = BatGun(b);
            if (g == null) return;
            b.Gn = g; b.Pend = _now; b.Saw = false; b.Am = BatAm(g); g.ShootOnce();
        }
        else foreach (var g in b.G) if (g.IsFunctional) g.ShootOnce();
    }
    else if (on != b.On || (on && _tick % 30 == 0)) { b.On = on; foreach (var g in b.G) if (g.IsFunctional) g.ApplyAction(on ? "Shoot_On" : "Shoot_Off"); }
}
// single-shot guns: an ammo drop or a busy-then-idle gun confirms the shot and starts the reload; nothing within 3 s = still charging, ask again in 2 s
void BatCheck(Bat b)
{
    if (b.Sv)
    {   // SALVO: every working gun was asked at once; after 1 s count the ones whose magazine shrank. Most of them fired: the whole battery recharges together; few did: the rest are still charging, ask again in 4 s
        if (_now - b.Pend < 1) return;
        int n = 0, f = 0;
        for (int k = 0; k < b.G.Count && k < b.Ar.Length; k++) if (b.Ar[k] >= 0) { n++; if (BatAm(b.G[k]) < b.Ar[k]) f++; }
        b.Pend = -1;
        b.Rdy = _now + (f == 0 ? 2 : f * 10 >= n * 7 ? b.Rel * 1.1 + 0.5 : 4);
        return;
    }
    var g = b.Gn;
    if (g == null || g.Closed) { b.Pend = -1; return; }
    if (g.IsShooting) b.Saw = true;
    if (BatAm(g) < b.Am) { b.Pend = -1; b.Rdy = _now + b.Rel * 1.1 / b.G.Count + 0.5; }
    else if (b.Saw && !g.IsShooting) { b.Pend = -1; b.Rdy = _now + b.Rel / b.G.Count - 1; }
    else if (_now - b.Pend > 3) { b.Pend = -1; b.Rdy = _now + 2; }
}

// ROCK class lead solver (the Basilisk's, "capped" rocket model): a rocket leaves at ship velocity + 100 m/s along the barrel, accelerates at 600 m/s^2 along it up to 200 m/s and
// bends towards the barrel direction; Newton on the flight time T finds the barrel direction d with rocket(d, T) = target(T). Lead() starts with
// "if (_rkt) return BatRkt(t, shooter, shooterVel, out point, out dir);" while a rocket battery is being solved.
double RkT(double d) { return d <= 25 ? (Math.Sqrt(1e4 + 1200 * d) - 100) / 600 : 1 / 6.0 + (d - 25) / 200; }
void RkP(double T, double ua, double us, out double s, out double q, out double sv)
{
    double V = 200, A = 600, va = ua + 100, t1, th;
    if (va * va + us * us >= V * V) { t1 = 0; th = Math.Atan2(us, va); }
    else { t1 = (Math.Sqrt(V * V - us * us) - va) / A; th = Math.Asin(MathHelper.Clamp(us / V, -1, 1)); }
    if (T <= t1) { s = va * T + 0.5 * A * T * T; q = us * T; sv = va + A * T; return; }
    s = va * t1 + 0.5 * A * t1 * t1; q = us * t1;
    double k = A / V, h = T - t1, c = Math.Tan(0.5 * th), x = c * Math.Exp(-k * h);
    q += V / k * 2 * (Math.Atan(c) - Math.Atan(x));
    s += V * (h - Math.Log((1 + c * c) / (1 + x * x)) / k);
    sv = V * (1 - x * x) / (1 + x * x);
}
bool BatRkt(Track t, Vector3D shooter, Vector3D shooterVel, out Vector3D point, out Vector3D dir)
{
    Vector3D rp = t.P(_now) - shooter, tv = t.V(_now), ta = LeadAcc(t);
    double rl = rp.Length();
    point = shooter + rp; dir = Vector3D.Zero;
    if (rl < 1 || double.IsNaN(rl)) return false;
    Vector3D los = rp / rl, d = los, p = rp;
    double tt = RkT(rl), h = LeadHold(t), f = 99;
    _aimLos = los; _aimNeed = LeadMargin * LeadMiss(t, tt);
    Vector3D off = AimOffset(t);
    for (int i = 0; i < 12; i++)
    {
        p = rp + LeadOffset(t, off, los, tt) + (tv * tt + ta * LeadK(tt, h)) * LeadScale;
        Vector3D w = p, up = shooterVel - Vector3D.Dot(shooterVel, d) * d; double us = up.Length(), s, q, sv;
        RkP(tt, Vector3D.Dot(shooterVel, d), us, out s, out q, out sv);
        if (us > 1e-3) w -= up * (q / us);
        double wl = w.Length();
        if (wl < 1) break;
        d = w / wl; f = s - wl;
        tt = MathHelper.Clamp(tt - f / Math.Max(sv - Vector3D.Dot(d, tv + ta * LeadKd(tt, h)), 20), 0.02, 12);
    }
    _leadT = tt; point = shooter + p; dir = d;
    return Math.Abs(f) < 2 && !double.IsNaN(tt) && !double.IsNaN(d.X);
}

double _caA, _caT, _caS; Vector3D _caW; bool _caDone;
void BatTurn(Vector3D cmd)
{
    if (_caDone || BatCal == "OFF" || (BatCal != "ON" && Me.CubeGrid.GridSizeEnum != MyCubeSize.Large)) return;
    double dt = _now - _caT;
    if (dt < 0.1) return;
    Vector3D w = _rc.GetShipVelocities().AngularVelocity;
    bool sat = (cmd - w).Length() > 0.8;
    if (_caT > 0 && dt < 0.2 && sat) { _caA = Math.Max(_caA, (w - _caW).Length() / dt); _caS += dt; }
    _caW = w; _caT = _now;
    if (_caS > 3 || (_caS > 0.5 && !sat)) { _caDone = true; MaxTurn = MathHelper.Clamp(0.85 * Math.Sqrt(1.2 * _caA), 0.15, MaxTurn); AimKp = MaxTurn / 0.6; }
}
string BatSave() { return _caDone ? MaxTurn.ToString("0.###") : "0"; }
void BatLoad(string s) { double v; if (BatCal != "OFF" && double.TryParse(s, out v) && v > 0) { MaxTurn = v; AimKp = v / 0.6; _caDone = true; } }

string BatStatus()
{
    var s = new StringBuilder("Batt");
    for (int i = 0; i < 4; i++) if (_bat[i].G.Count > 0) s.Append(" B").Append(i + 1).Append(i == _prim ? "* " : " ").Append(_bat[i].Cls).Append('x').Append(BatN(_bat[i]));
    return s.Append(" so ").Append(Standoff.ToString("0")).Append(" w ").Append(MaxTurn.ToString("0.0")).Append(BatTO() ? " TO\n" : "\n").ToString();
}
// <<< BATTERY v1.1 <<<
