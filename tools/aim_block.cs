// >>> BREACH + SEEKER v1.3 (shared block: identical in every armed drone script, kept in sync by tools/aim_sync.py) >>>
// BREACH (large ships): spreading hits over a big hull chips thousands of armour blocks and destroys none (sim vs a 13,666-block
// warship). BREACH holds ONE hull point and keeps every round on it: the known hull point nearest the target's centre (on a long
// ship that is mid-deck or mid-belly, where the armour over the core is thinnest). Probe rays at it that come back deeper show the
// hole; the drone then aims down the hole, towards the centre. No progress for "Breach Stall (s)", or two probe rays in a row that
// find nothing there: that point is dropped and the next one is used. AUTO uses BREACH on large grids of radius >= Breach Min
// Radius; "aim breach" forces it, "breach off" turns the AUTO switch off.
// SEEKER (v1.3: moved to the Staff PB's STAFF SEARCH block, docs/design/survivability.md): a turret on this drone with [Seeker] in its name is
// set to the targeting group "Seeker Group" (PowerSystems / Weapons / Propulsion) and the Staff PB raycasts where it points twice a second; the hull
// point hit, right over the subsystem, reaches this PB as the .WPT slot with flag 4096 (own seeker) and becomes the breach point at once (BreachSet(.., true)),
// drilled along the turret's line. The "Seek:" report, "seeker on|off" and the two seeker keys are the Staff PB's now.
// FLEET WEAKPOINT (v1.1, docs/design/hive_weakpoint.md): a sniper at 2 km cannot find a weakpoint with an 800 m turret, so the wing shares
// holes. This PB sends its own confirmed hole (and every seeker sample) to its Staff PB (.WPF), which relays it to the fleet channel
// (<ch>_WP) and sends back the best hole of the whole wing for our target (.WPT, read by LkRead into _wp*). Points are in the TARGET frame,
// so they stay true while the ships move. WpAdopt takes the relayed hole when it is within the cone of our own line of sight (or aims
// through it at the vital part when its depth is known); our own probe rays confirm it or throw it out. Own turret beats the relay, and a hole that is being drilled
// is kept (Weakpoint Hold) instead of being swapped for a fresh seeker hole.
bool WpOn = true, WpShare = true; double WpCone = 40, WpHold = 10;
long _wpId; Vector3D _wpS, _wpD, _wpI; int _wpF; double _wpT = -99, _wpSwT = -99, _wpSendT = -99;   // slot from the Staff PB: best fleet hole (I = depth, vital depth hint, age)
bool _bOk, _wpAdopt; double _bHint = -1; int _wpKind; List<Vector3D> _wpRej = new List<Vector3D>();
long _wpEvId; Vector3D _wpEvS, _wpEvD; double _wpEvT = -99; int _skSk, _wpEvF;
bool BreachAuto = true; double BreachMinRadius = 25, BreachStall = 30, BreachProbe = 30;
long _bId; bool _bOn, _bSeek; Vector3D _bS, _bDir; double _bDepth, _bProgT; int _bMiss;
List<Vector3D> _bBad = new List<Vector3D>();

bool BreachMode(Track t)
{
    if (AimMode == "BREACH") return true;
    return AimMode == "AUTO" && BreachAuto && t.Type == MyDetectedEntityType.LargeGrid && t.Radius >= BreachMinRadius;
}
bool BreachBad(Vector3D h) { foreach (var q in _bBad) if ((q - h).LengthSquared() < 36) return true; return false; }
void BreachSet(Vector3D s, Vector3D dir, bool seek)
{
    _bS = s; _bDir = dir; _bSeek = seek; _bOn = true; _bDepth = 0; _bProgT = _now; _bMiss = 0; _bOk = false; _wpAdopt = false; _bHint = -1;
}
Vector3D BreachOffset(Track t)
{
    if (_bId != t.Id) { _bId = t.Id; _bOn = false; _bBad.Clear(); _wpRej.Clear(); _bOk = false; _wpAdopt = false; }
    if (_bOn && _now - _bProgT > BreachStall) BreachDrop();
    if (WpOn || (_wpF & 4096) != 0) WpAdopt(t);
    if (!_bOn)
    {
        double best = double.MaxValue; Vector3D s = t.Hull[0];
        foreach (var h in t.Hull) { if (BreachBad(h)) continue; double d = h.LengthSquared(); if (d < best) { best = d; s = h; } }
        if (best == double.MaxValue) _bBad.Clear();
        double l = s.Length();
        BreachSet(s, l > 1e-3 ? -s / l : Vector3D.Zero, false);
    }
    Vector3D a = _bS + _bDir * _bDepth;                    // down the hole
    t.AimLocal = a;
    return Vector3D.TransformNormal(a, t.O);
}
// a probe ray at the breach point hit the target: how far past the surface point did it get?
void BreachHit(Track t, MyDetectedEntityInfo e, Vector3D aim)
{
    if (!_bOn || _bId != t.Id || !e.HitPosition.HasValue || !AimActive(t) || !BreachMode(t)) return;
    _bMiss = 0;
    Vector3D rd = aim - Me.GetPosition();
    if (rd.LengthSquared() < 1) return;
    rd.Normalize();
    double d = Vector3D.Dot(e.HitPosition.Value - (e.Position + Vector3D.TransformNormal(_bS, e.Orientation)), rd);
    // a relayed hole that my own ray cannot see (it stops on the hull well before the entry) is not for me
    if (_wpAdopt && !_bOk && d < -6) { _wpRej.Add(_bS); if (_wpRej.Count > 6) _wpRej.RemoveAt(0); BreachDrop(1); return; }
    if (Math.Abs(d) < 3 || d > _bDepth + 0.5) _bOk = true;
    if (d > _bDepth + 0.5) { _bDepth = Math.Min(d, 60); _bProgT = _now; }
    if (WpShare && _bOk && (_bDepth >= 55 || (_bHint >= 0 && _bDepth >= _bHint + 3))) BreachDrop(2);   // through to the vital part: tell the wing, take the next point
}
void BreachMiss() { if (++_bMiss >= 2) BreachDrop(); }
void BreachDrop(int why = 0)                             // why: 0 stalled / missed, 1 not visible from here (relay), 2 done
{
    if (_bOn)
    {
        _bBad.Add(_bS); if (_bBad.Count > 12) _bBad.RemoveAt(0);
        if (why != 1 && WpShare && (_bOk || why == 2)) { _wpEvS = _bS; _wpEvD = _bDir; _wpEvF = why == 2 ? 32 : 64; _wpEvT = _now; _wpEvId = _bId; }
    }
    _bOn = false; _bSeek = false; _bMiss = 0; _bOk = false; _wpAdopt = false;
}
bool WpRejected(Vector3D s) { foreach (var q in _wpRej) if ((q - s).LengthSquared() < 36) return true; return false; }
void WpTake(Vector3D s, Vector3D d, double depth, double hint)
{
    BreachSet(s, d, true); _bDepth = Math.Max(0, depth); _bHint = hint; _wpAdopt = true; _wpSwT = _now; _wpKind = _wpF & 31;
}
// the best hole of the wing for this target (from the Staff PB, refreshed every second): take it when it beats what I hold
void WpAdopt(Track t)
{
    if (_wpId != t.Id || _now - _wpT > 3) return;
    if ((_wpF & 4096) != 0)                                // my own seeker (Staff PB): replaces a plain or relayed point at once, an old seeker point once its hole stalled 6 s
    {
        if (!BreachBad(_wpS) && (!_bOn || !_bSeek || (_wpAdopt && (_wpS - _bS).LengthSquared() > 16) || ((_wpS - _bS).LengthSquared() > 64 && _now - _bProgT > 6)))
        { BreachSet(_wpS, _wpD, true); _bHint = _wpI.Y; _skSk = _wpF & 7; }
        return;
    }
    if ((_wpF & 32) != 0)                                  // a wingmate finished this hole: never aim there again
    {
        if (!BreachBad(_wpS)) { _bBad.Add(_wpS); if (_bBad.Count > 12) _bBad.RemoveAt(0); }
        if (_bOn && (_bS - _wpS).LengthSquared() < 36) { _bOn = false; _bSeek = false; _bOk = false; _wpAdopt = false; }
        _wpT = -99; return;
    }
    if ((_bOn && _bSeek && !_wpAdopt) || BreachBad(_wpS) || WpRejected(_wpS) || _now - _wpSwT < 6) return;   // my own turret beats the relay
    if (_bOn && (_bS - _wpS).LengthSquared() < 16) return;                                              // already on it
    // a hole that is being drilled (progress within "Weakpoint Hold (s)", 3 m deep) is not given up for a fresh seeker hole: the finder is often dead
    // before the wing has drilled the new one (sim, Pathfinder A/B); only a relayed hole that is already 3 m deeper than mine takes over
    bool prog = _bOn && _bDepth >= 3 && _now - _bProgT < WpHold;
    bool better = !_bOn || _wpI.X >= _bDepth + 3 || (!prog && (((_wpF & 8) != 0 && !_bSeek) || (_now - _bProgT > 6 && _wpI.X > _bDepth + 1)));
    if (!better) return;
    Vector3D me = Vector3D.TransformNormal(Me.GetPosition() - t.P(_now), MatrixD.Transpose(t.O));   // me, in the target frame
    double ll = me.Length(); if (ll < 1) return;
    Vector3D los = -me / ll;                               // from me to the target
    if (Vector3D.Dot(los, _wpD) >= Math.Cos(WpCone * Math.PI / 180)) { WpTake(_wpS, _wpD, _wpI.X - 2, _wpI.Y); return; }   // same side: the same hole
    if (_wpI.Y < 0) return;                                // other side and no depth: keep my own point
    Vector3D V = _wpS + _wpD * _wpI.Y, u = V - me; double ul = u.Length(); if (ul < 1) return;   // the vital part: aim at it through my own entry
    u /= ul; double bestOff = 12; Vector3D h = Vector3D.Zero; bool found = false;
    foreach (var q in t.Hull)
    {
        if (BreachBad(q)) continue;
        Vector3D w = q - me; double a = Vector3D.Dot(w, u); if (a <= 0 || a >= ul) continue;
        double off = (w - u * a).Length(); if (off < bestOff) { bestOff = off; h = q; found = true; }
    }
    if (found) { Vector3D v = V - h; double vl = v.Length(); if (vl > 1) WpTake(h, v / vl, 0, vl); }
}
// my own findings, to the Staff PB: my hole once my own ray has confirmed it; done / stalled holes for 3 s (seeker samples come from the Staff PB itself)
void WpTick(Track t)
{
    if (!WpShare || !LkAlive || _now - _wpSendT < 0.5) return;
    bool ev = _now - _wpEvT < 3, hole = _bOn && _bOk && _bId == t.Id && BreachMode(t);
    if (!ev && !hole) return;
    if (!ev && _now - _wpSendT < 1.0) return;
    _wpSendT = _now;
    long id = t.Id; Vector3D s, d; double dep = 0, hint = -1, age = 0; int f = 0;
    if (ev) { id = _wpEvId; s = _wpEvS; d = _wpEvD; f = _wpEvF; }
    else
    {
        s = _bS; d = _bDir; dep = _bDepth; hint = _bHint; age = _now - _bProgT;
        if (_wpAdopt) f = (_wpKind & 31) | 128; else if (_bSeek) f = 8 | _skSk;
    }
    WpSend(new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(id, s, d, new Vector3D(dep, hint, age), Me.CubeGrid.EntityId, f));
}
string AimInfo()
{
    string s = "";
    if (_bOn && _tgt != null && _bId == _tgt.Id && BreachMode(_tgt)) s += " hole " + _bDepth.ToString("0") + " m" + (_wpAdopt ? " (fleet " + ((_wpF >> 8) & 15) + ")" : _bSeek ? " (seeker)" : "");
    return s;
}
// <<< BREACH + SEEKER <<<
