// >>> SURVIVAL LINK v1.0 (evade, jinks, survival advice from the Staff PB, docs/design/survivability.md) >>>
// The Flight side of the survivability plan. Only what must react within a tick or what only the Flight PB sees lives here: the dodge on a hit, the
// lead-breaking jinks, missile sightings, per-check hit stats, the railgun power guard, and applying the Staff's advice. Every decision (damage RTB,
// limp caps, failover, threat memory, blind side, gather, missile dodge, flares) is the Staff PB's SURVIVAL STAFF block. Advice older than 0.6 s is
// ignored, so a dead Staff leaves the Flight exactly as before plus the evade, the jinks and the power guard.
// IGC, key _lkK ("WSPL" + this PB's EntityId), TransmissionDistance.CurrentConstruct:
//  .SVF Flight->Staff MyTuple<int flags, long attackerId, Vector3D hitDir, Vector3D attackerPos, Vector3D aux, Vector3D home>: flags bit0 hit in this report,
//       bit1 yielding, bits 8-15 integrity % of the most seen (255 unknown), 16-23 blocks hit in that check; aux = (largest single-block drop, SvRange(),
//       attacker range or 0). The attacker (live contact within 35 deg of the hit side, my target first) is sent while its attribution is < 10 s old.
//       2 Hz; at once after a check with a hit (<= 3 Hz), after a run gap and on a yield change.
//  .SVO Flight->Staff MyTuple<long tgtId, Vector3D fwd, Vector3D up, Vector3D vel, double radius, int flags (bit0 orientation valid)>, 2 Hz with a target.
//  .MSL Flight->Staff MyTuple<long id, Vector3D pos, Vector3D vel>: a missile seen by any detector (SvSee, first line of Valid), <= 1 per 6 ticks per id.
//  .SVS Staff->Flight MyTuple<int flags, long tgtId, Vector3D A, Vector3D B, Vector3D D, Vector3D N>. Bits: 0 gather (A), 1 retreat via (A, wins), 2 orbit
//       bearing (B = bearing x speed), 3 missile dodge (D), 4 DAMAGED, 5 RETREATING, 6 GATHER READY, 7 GATHERING, 8 LIMP, 9 Staff holds the overrides (Flight
//       yields), 10 fight back in RTB, 11 standoff advice (N.X floor, N.Y add), 16-23 limp speed cap m/s, 24-27 threat class; N.Z enemy projectile speed.
// Failover handshake (plan item 5): SvYield is true for the first 6 ticks and while a fresh SVS has bit 9; the tick it ends SvResync() resends the AI and gun state.
// Hooks: SvInit() after LkInit(); SvRead() after LkRead(); "if (!LkBurn && !SvYield && !AiStep()) Control(dt)"; SvSend() after LkSend(); "_svSentT = -99;" in the
// run-gap line; SvLoad(_ini) in LoadConfig; _guns.RemoveAll(SvNotGun) in GetBlocks; SvSee(e) first in Valid(); "_svNb++; _svMax = Math.Max(_svMax, drop);" in
// CheckDamage's hit-block branch and SvDamage() at its end; "| SvSwarm()" in the SWARM flags; SvStatus() on the screen; "if (LkBurn || SvYield) return;" first
// in Release(); Control: SvStand, SvJink, SvOrbit, SvGather, SvHome/SvViaOn/SvFightBack, SvBrake, SvCap, SvWant. Railgun drones: SvPower(load, SvRecharging())
// at the end of PowerAndHealth, SvRails(true) first in Release(), SvRails(!_svRx) in GetBlocks. Per script: double SvRange() (own max fire range),
// void SvResync() (_aiOn = !_aiOn; _firing = false; ...), bool SvRecharging() (railgun drones only).
// Uses: _now, _tick, _rc, _tgt (Id, P, V, OF, OU, HasOri, Radius), _contacts, _guns, _mode (IDLE/RTB only), _hitDir, _hitDirT, _hits, _integ, _home, _dynHome,
// _dynHomeT, _powerScale, UnderFire, EvadeSpeed, MaxSpeed, _lkK, Me, IGC.
Vector3D SvZ;   // (never set: the zero vector, shorter than Vector3D.Zero)
IMyBroadcastListener _svL; int _svF, _svNb, _svBn, _svEh; long _svTid, _svAid; IMyCubeGrid _svG; Random _evRng = new Random();
Vector3D _svA, _svB, _svD, _svN, _svAp, _svE, _svJk, _svJd; bool _svY = true, _svHit, _svRx;
double _svT = -99, _svSentT = -99, _svMax, _svBm, _svI0, _svAr, _svAt = -99, _svEn, _svJn, _svOs = 1, _svRf = -99, _svRo = -99;
Dictionary<long, long> _svMs = new Dictionary<long, long>();
bool SvLJ = true, SvPG = true; double SvJa = 0.8, SvEv = 800, SvJm = 0.4, SvJM = 3, SvJr = 0.3, SvPh = 2;
bool SvFresh { get { return _now - _svT < 0.6; } }
bool SvOn(int b) { return SvFresh && (_svF & b) != 0; }
bool SvYield { get { return _svY; } }
bool SvViaOn { get { return SvOn(2); } }
bool SvFightBack { get { return SvOn(1024); } }
bool SvMine { get { return _tgt != null && _svTid == _tgt.Id; } }
void SvInit() { _svL = IGC.RegisterBroadcastListener(_lkK + ".SVS"); }
// [Survival] keys in this PB's Custom Data: read only (the Staff PB writes the defaults, after our first F2S); the defaults here equal the Staff's
MyIni _svI;
double SvK(string k, double d) { return _svI.Get("Survival", k).ToDouble(d); }
void SvLoad(MyIni i)
{
    _svI = i; SvLJ = i.Get("Survival", "Lead-Breaking Jinks").ToBoolean(true); SvPG = i.Get("Survival", "Power Guard").ToBoolean(true);
    SvJa = SvK("Jink Accel Fraction", 0.8); SvEv = SvK("Enemy Projectile Speed (m/s)", 800); SvJm = SvK("Jink Period Min (s)", 0.4);
    SvJM = SvK("Jink Period Max (s)", 3); SvJr = SvK("Jink Period Random", 0.3); SvPh = SvK("Power Guard Hold (s)", 2);
}
void SvTx<T>(string c, T d) { IGC.SendBroadcastMessage(_lkK + c, d, TransmissionDistance.CurrentConstruct); }
void SvRead()
{
    while (_svL.HasPendingMessage)
    {
        var m = _svL.AcceptMessage();
        if (!(m.Data is MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>)) continue;
        var d = (MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>)m.Data;
        _svF = d.Item1; _svTid = d.Item2; _svA = d.Item3; _svB = d.Item4; _svD = d.Item5; _svN = d.Item6; _svT = _now;
    }
    bool y = _tick < 6 || SvOn(512);                     // yield: start-up, or the Staff flies limp-home (stale for 0.6 s = it is gone: take over)
    if (y != _svY) { if (!y) SvResync(); _svY = y; _svSentT = -99; }
    if (Me.CubeGrid != _svG) { _svG = Me.CubeGrid; _svI0 = 0; }   // merged / split off a printer: a fresh (or repaired) drone, integrity starts again
}
void SvSend()
{
    if (_now - _svSentT < (_svHit ? 0.33 : 0.5)) return;
    _svSentT = _now;
    int ip = _integ < 0 || _svI0 <= 0 ? 255 : (int)Math.Min(100, Math.Round(100 * _integ / _svI0));
    bool a = _now - _svAt < 10;
    SvTx(".SVF", new MyTuple<int, long, Vector3D, Vector3D, Vector3D, Vector3D>((_svHit ? 1 : 0) | (_svY ? 2 : 0) | ip << 8 | _svBn << 16,
        a ? _svAid : 0, _now - _hitDirT < 3 ? _hitDir : SvZ, a ? _svAp : SvZ, new Vector3D(_svBm, SvRange(), a ? _svAr : 0),
        _now - _dynHomeT < 5 ? _dynHome : _home));
    _svHit = false; _svBn = 0; _svBm = 0;
    var t = _tgt;
    if (t != null) SvTx(".SVO", new MyTuple<long, Vector3D, Vector3D, Vector3D, double, int>(t.Id, t.OF, t.OU, t.V(_now), t.Radius, t.HasOri ? 1 : 0));
}
// end of CheckDamage: keep the most integrity seen; on a hit, report at once with the attacker (the live contact on the side that was hit, my target first)
void SvDamage()
{
    if (_integ > _svI0) _svI0 = _integ;
    if (_svNb == 0) return;
    _svHit = true; _svBn = Math.Min(255, _svNb); _svBm = _svMax; _svNb = 0; _svMax = 0;
    if (_now - _hitDirT > 1) return;
    Vector3D me = _rc.GetPosition(); double bc = 0; Track b = null;
    foreach (var t in _contacts.Values)
    {
        Vector3D d = t.P(_now) - me; double l = d.Length();
        if (l < 1) continue;
        double c = Vector3D.Dot(d, _hitDir) / l;
        if (c < 0.82) continue;                            // 35 deg
        if (t == _tgt) c = 2;
        if (c > bc) { bc = c; b = t; }
    }
    if (b != null) { _svAid = b.Id; _svAp = b.P(_now); _svAr = Vector3D.Distance(_svAp, me); _svAt = _now; }
}
// any detection path (turret, CTC, camera, sensor) that sees an incoming missile: tell the Staff PB, which tracks it, dodges and fires the flares
void SvSee(MyDetectedEntityInfo e)
{
    var r = e.Relationship;
    if (e.Type != MyDetectedEntityType.Missile || r == MyRelationsBetweenPlayerAndBlock.Owner || r == MyRelationsBetweenPlayerAndBlock.FactionShare || r == MyRelationsBetweenPlayerAndBlock.Friends) return;
    long k; if (_svMs.TryGetValue(e.EntityId, out k) && _tick - k < 6) return;
    if (_svMs.Count > 40) _svMs.Clear();
    _svMs[e.EntityId] = _tick;
    SvTx(".MSL", new MyTuple<long, Vector3D, Vector3D>(e.EntityId, e.Position, new Vector3D(e.Velocity)));
}
bool SvNotGun(IMyTerminalBlock b) { return b.BlockDefinition.SubtypeId.Contains("Flare"); }   // a flare launcher is a SmallMissileLauncher: never a gun (the Staff fires it)
Vector3D SvRnd() { return new Vector3D(_evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5, _evRng.NextDouble() - 0.5); }
// Under fire: a new velocity of Evade Speed every 1-2.5 s and on every new hit, across the threat axis (moving along it changes nothing for the shooter's
// lead), and in 7 of 10 picks against the present sideways drift (a reversal is what a constant-velocity lead misses most). No axis: random, as before.
Vector3D Evade()
{
    if (!UnderFire) return SvZ;
    if (_now > _svEn || _hits != _svEh)
    {
        _svEh = _hits; _svEn = _now + 1 + _evRng.NextDouble() * 1.5;
        // the threat axis u: the side just hit when that is not my target (someone else shoots), else my target, else the hit side
        Vector3D u = _tgt != null ? _tgt.P(_now) - _rc.GetPosition() : SvZ, h = _now - _hitDirT < 3 ? _hitDir : SvZ, r = SvRnd(), v = _rc.GetShipVelocities().LinearVelocity;
        u = u.LengthSquared() > 1 ? Vector3D.Normalize(u) : h;
        if (h != SvZ && Vector3D.Dot(h, u) < 0.7) u = h;
        if (u != SvZ)
        {
            r -= Vector3D.Dot(r, u) * u; v -= Vector3D.Dot(v, u) * u;
            if (_evRng.NextDouble() < 0.7 && Vector3D.Dot(r, v) > 0) r = -r;
        }
        _svE = r.LengthSquared() > 1e-6 ? Vector3D.Normalize(r) * EvadeSpeed : SvZ;
    }
    return _svE;
}
// Lead-breaking jinks. Turrets lead with constant velocity: over a shot's flight time T a sideways acceleration a puts us 0.5 a T^2 off their aim. So the
// period follows T (range / enemy projectile speed, the Staff's threat class speed when it knows one), the direction turns at least 90 deg every period
// (thrusters stay saturated) and the size is the velocity swing the lateral thrust can make in that period (aLat = weakest-axis acceleration), capped by
// Jink Speed. Also flips the orbit sign now and then (1 in 10 periods). "Lead-Breaking Jinks=false": no jinks at all.
Vector3D SvJink(Vector3D los, double dist, double aLat, double cap, bool on)
{
    if (!on || !SvLJ) return SvZ;
    if (_now >= _svJn)
    {
        double P = MathHelper.Clamp(dist / Math.Max(100, SvFresh && _svN.Z > 0 ? _svN.Z : SvEv) * (1 + SvJr * (2 * _evRng.NextDouble() - 1)), SvJm, Math.Max(SvJm, SvJM));
        _svJn = _now + P;
        Vector3D d = SvRnd(); d -= Vector3D.Dot(d, los) * los;
        if (d.LengthSquared() > 1e-6)
        {
            d.Normalize();
            if (Vector3D.Dot(d, _svJd) > 0) d = -d;
            _svJd = d; _svJk = d * Math.Min(cap, aLat * SvJa * P * 0.5);
        }
        if (_evRng.NextDouble() < 0.1) _svOs = -_svOs;
    }
    return _svJk;
}
// orbit term: the Staff's blind-side bearing for this target (until within 10 deg of it), else the plain orbit
Vector3D SvOrbit(Vector3D dir, Vector3D tan, double orbit)
{
    if (SvOn(4) && SvMine && _svB.LengthSquared() > 0.01)
    {
        Vector3D p = Vector3D.Normalize(_svB), e = p - dir * Vector3D.Dot(dir, p);   // bearing now c = -dir: the part of p across c
        if (e.Length() > 0.17) return Vector3D.Normalize(e) * _svB.Length();
    }
    return tan * orbit * _svOs;
}
// standoff advice (floor from the enemy's weapon range, wing hand-off add): only pushes out to hi or pulls in to lo, never past the script's own value the other way
double SvStand(double s, double lo, double hi)
{
    if (!SvOn(2048) || !SvMine) return s;
    double x = Math.Max(s, _svN.X) + _svN.Y;
    return x > s ? Math.Min(x, Math.Max(s, hi)) : Math.Max(x, Math.Min(s, lo));
}
// gather point of a bounded "attack together" window: only out of my own range and not under fire (the guns still fire at anything in range)
bool SvGather(Vector3D me, double dist, out Vector3D gv)
{
    gv = _svA - me; double l = gv.Length();
    if (!SvOn(1) || SvOn(2) || !SvMine || dist <= SvRange() || UnderFire || l < 1) return false;
    gv *= Math.Min(MaxSpeed, l * 0.5) / l; return true;
}
Vector3D SvHome(Vector3D home) { return SvViaOn ? _svA : home; }   // damage RTB: the retreat via point off the threat axis first
double SvBrake(double a) { return SvOn(256) ? a * 0.6 : a; }      // limp: brake earlier
double SvCap(double c) { int l = SvFresh ? (_svF >> 16) & 255 : 0; return l > 0 ? Math.Min(c, l) : c; }
Vector3D SvWant(Vector3D w) { return SvOn(8) ? _svD + w * 0.3 : w; }   // missile dodge first
int SvSwarm() { return SvFresh ? (_svF >> 1) & 120 : 0; }        // SWARM 8 DAMAGED, 16 RETREATING, 32 GATHER READY, 64 GATHERING
string SvStatus() { return SvFresh ? "\nSurv " + (SvYield ? "HELD " : "") + ((_svF & 48) != 0 ? "DAMAGED " : "") + ((_svF & 8) != 0 ? "DODGE" : "") : ""; }
// Railgun power guard: weapons come first on the power bus (priority 5, gyros 70, thrusters 80) and a charging small railgun draws ~3.6 MW. In a brown-out
// while it matters (under fire, RTB, missile dodge) the recharging rails are switched off for up to "Power Guard Hold (s)", at most every other hold.
void SvRails(bool on) { foreach (var g in _guns) if (g.BlockDefinition.SubtypeId.Contains("Railgun")) g.Enabled = on; _svRx = !on; }
void SvPower(double load, bool rech)
{
    bool need = UnderFire || _mode == Mode.RTB || SvOn(8);
    if (!_svRx && SvPG && need && rech && load > 0.97 && _powerScale < 0.6 && _now - _svRo > SvPh) { SvRails(false); _svRf = _now; }
    else if (_svRx && (!SvPG || !need || _now - _svRf > SvPh)) { SvRails(true); _svRo = _now; }
}
// <<< SURVIVAL LINK v1.0 <<<
