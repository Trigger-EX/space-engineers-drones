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
