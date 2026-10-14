// >>> LOOKOUT v1.1 (shared block: harasser defence, identical in SpotterAI (Ballista) and StaffAI (Warden, Leviathan, Corsair), synced by tools/blocks_sync.py; docs/design/ballista_harasser_defence.md) >>>
// ---------------------------------------------------------------------------------------------------------------
//  A small hostile craft that stays around and shoots (an AI drone launched by an enemy warship) is neither a kamikaze (the Helm's KAMIKAZE DEFENCE) nor a wreck, and the ship's heavy fixed guns cannot answer it.
//  This module finds it, and picks a "corner jink": a velocity bias along the corner of the ship's own thrust box that lies across the harasser's line of sight (only acceleration across the line of
//  sight makes a shot miss), changed every 0.8-2.5 s at random so a gunner cannot lead it, plus a roll that puts the harasser above or below the hull (where the top and belly turrets, cameras and
//  AI turrets see it), and kites out of its gun range (Away Speed, within the large target's stand-off band). The Helm applies the bias (HARASSER LINK block) and stays the only writer of thrusters and gyros; the nose stays on the large target. Fire from a bearing nobody sees
//  (the hit direction, no tracked craft there) gets the same dodge. Link: "HDEF<Helm PB EntityId>" .H2A / .HTK (Helm -> here) and .A2H (here -> Helm), CurrentConstruct only.
//  COMMANDS: lookout on | lookout off.  CUSTOM DATA [Harasser Defence]: Enabled, Max Radius (m), Band Min / Max (m), Dwell (s), Latch (s), Jink Speed (m/s), Jink Period Min / Max (s),
//   Harasser Projectile Speed (m/s), Turret Range (m), Phantom Range (m), Roll To Threat, Away Speed (m/s) / Away Within (m) (kiting: also open the range while a harasser is inside its gun range)
// ---------------------------------------------------------------------------------------------------------------
//  v1.1 (2026-10-14): shared with StaffAI. The host declares "bool LkDefOn" (Enabled default: Spotter true, Staff false; the Warden, Leviathan and Corsair Staff Custom Data say true);
//   a copy that is not in a "[Spotter]" PB stands down while a "[Spotter]" PB is on the construct (one Lookout per ship); "Max Fire Range (m)" (was a constant 1700).
//  Hooks: LkLoad() while the host parses Custom Data (before it writes _ini back); LkBind() every ~5 s; LkStep() every Update10 run (in its own try/catch);
//   LkStatus() on the screen; "lookout on|off" commands. Uses from the host: _ini (MyIni), _now (seconds), Me, GridTerminalSystem, IGC.
bool LkOn = true, LkRoll = true;
double LkMaxR = 15, LkBandMin = 30, LkBandMax = 1600, LkDwell = 3, LkLatch = 15, LkJink = 30, LkPerMin = 0.8, LkPerMax = 2.5, LkVp = 800, LkTurR = 800, LkPhR = 1000, LkAway = 40, LkAwayR = 900;
const double LkMargin = 150, LkNose = 0.94; double LkMaxFire = 1700;
class LkT { public long Id; public Vector3D P, V; public double R, Seen, In = -1, Ev = -99, KzT = -99, Rng; public int St; }
Dictionary<long, LkT> _lk = new Dictionary<long, LkT>();
IMyBroadcastListener _lkA, _lkK;
long _lkHid, _lkTid, _lkI1, _lkI2;
double _lkHeard = -99, _lkHitAt = -99, _lkHitDirT = -99, _lkStand = 1600, _lkPhT = -99, _lkNext = -99, _lkBoxT = -99, _lkAF, _lkAB, _lkAR, _lkAL, _lkAU, _lkAD;
int _lkHits = -1, _lkCur = -1, _lkNH, _lkFl, _lkSent;
Vector3D _lkHitDir, _lkTPos, _lkPhU, _lkB, _lkUp;
bool _lkRollOn, _lkYield;
IMyRemoteControl _lkRc;
List<IMyThrust> _lkThr = new List<IMyThrust>();
List<IMyLargeTurretBase> _lkTur = new List<IMyLargeTurretBase>();
Random _lkRng = new Random();
string _lkNote = "off";

bool LkCfgB(string k, bool d) { if (!_ini.ContainsKey("Harasser Defence", k)) _ini.Set("Harasser Defence", k, d); return _ini.Get("Harasser Defence", k).ToBoolean(d); }
double LkCfg(string k, double d) { if (!_ini.ContainsKey("Harasser Defence", k)) _ini.Set("Harasser Defence", k, d); return _ini.Get("Harasser Defence", k).ToDouble(d); }
void LkLoad()
{
    LkOn = LkCfgB("Enabled", LkDefOn); LkRoll = LkCfgB("Roll To Threat", LkRoll);
    LkMaxR = LkCfg("Max Radius (m)", LkMaxR); LkBandMin = LkCfg("Band Min (m)", LkBandMin); LkBandMax = LkCfg("Band Max (m)", LkBandMax);
    LkDwell = LkCfg("Dwell (s)", LkDwell); LkLatch = LkCfg("Latch (s)", LkLatch); LkJink = LkCfg("Jink Speed (m/s)", LkJink);
    LkPerMin = LkCfg("Jink Period Min (s)", LkPerMin); LkPerMax = Math.Max(LkPerMin, LkCfg("Jink Period Max (s)", LkPerMax));
    LkAway = LkCfg("Away Speed (m/s)", LkAway); LkAwayR = LkCfg("Away Within (m)", LkAwayR);
    LkMaxFire = LkCfg("Max Fire Range (m)", LkMaxFire);
    LkVp = Math.Max(100, LkCfg("Harasser Projectile Speed (m/s)", LkVp)); LkTurR = LkCfg("Turret Range (m)", LkTurR); LkPhR = LkCfg("Phantom Range (m)", LkPhR);
}

void LkBind()   // the Helm (re-found every 5 s: a rebuilt Helm has a new EntityId and so a new key), the Remote Control, the thrusters, the vanilla turrets
{
    var pbs = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(pbs, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Helm]", StringComparison.OrdinalIgnoreCase));
    var sp = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType(sp, b => b != Me && b.IsSameConstructAs(Me) && b.CustomName.StartsWith("[Spotter]", StringComparison.OrdinalIgnoreCase));
    _lkYield = sp.Count > 0 && !Me.CustomName.StartsWith("[Spotter]", StringComparison.OrdinalIgnoreCase);   // one Lookout per ship: the Spotter's
    long id = pbs.Count > 0 ? pbs[0].EntityId : 0;
    if (id != _lkHid)
    {
        if (_lkA != null) IGC.DisableBroadcastListener(_lkA);
        if (_lkK != null) IGC.DisableBroadcastListener(_lkK);
        _lkHid = id; _lkHeard = -99; _lkHits = -1;
        _lkA = id == 0 ? null : IGC.RegisterBroadcastListener("HDEF" + id + ".H2A");
        _lkK = id == 0 ? null : IGC.RegisterBroadcastListener("HDEF" + id + ".HTK");
    }
    var rcs = new List<IMyRemoteControl>();
    GridTerminalSystem.GetBlocksOfType(rcs, b => b.CubeGrid == Me.CubeGrid && b.CustomName.StartsWith("[SH]"));
    _lkRc = rcs.Count > 0 ? rcs[0] : null;
    _lkThr.Clear(); GridTerminalSystem.GetBlocksOfType(_lkThr, b => b.CubeGrid == Me.CubeGrid);
    _lkTur.Clear(); GridTerminalSystem.GetBlocksOfType(_lkTur, b => b.CubeGrid == Me.CubeGrid && b.CustomName.StartsWith("[SH]"));
}

void LkSee(long id, Vector3D p, Vector3D v, double r, double seen, bool kz)
{
    if (id == 0 || r > LkMaxR * 1.5) return;
    LkT t;
    if (!_lk.TryGetValue(id, out t)) { t = new LkT { Id = id, P = p, V = v, Seen = seen }; _lk[id] = t; }
    else
    {
        double dt = seen - t.Seen;
        if (dt < 0) return;                                                         // an older fix than the one held
        if (v.LengthSquared() < 0.01 && dt > 0.05 && dt < 2) v = (p - t.P) / dt;      // (a turret's target velocity can read 0: difference the positions)
    }
    t.P = p; t.V = v; t.R = r; t.Seen = seen;
    if (kz) t.KzT = _now;
}

// a contact closing on a collision course is the Helm's KAMIKAZE DEFENCE's job, not a harasser
bool LkIncoming(LkT t, Vector3D rel)
{
    Vector3D v = t.V - _lkRc.GetShipVelocities().LinearVelocity; double vv = v.LengthSquared(), rng = rel.Length();
    if (vv < 1 || rng < 1) return false;
    double tgo = -Vector3D.Dot(rel, v) / vv;
    if (tgo <= 0 || tgo > 12) return false;
    return (rel + v * tgo).Length() < 40 + 60 * rng / 1000 && -Vector3D.Dot(rel, v) / rng > 15;
}

void LkClassify(Vector3D me)
{
    bool hitFresh = _now - _lkHitAt < 6 && _now - _lkHitDirT < 3;
    var gone = new List<long>();
    foreach (var t in _lk.Values)
    {
        if (_now - t.Seen > 8) { gone.Add(t.Id); continue; }
        Vector3D rel = t.P + t.V * (_now - t.Seen) - me; double rng = rel.Length(); t.Rng = rng;
        bool cand = _now - t.Seen < 3 && _now - t.KzT > 4 && rng >= LkBandMin && rng <= LkBandMax && !LkIncoming(t, rel);
        if (!cand) { t.In = -1; if (_now - t.Ev > LkLatch) t.St = 0; continue; }
        if (t.In < 0) t.In = _now;
        if (_now - t.In < LkDwell) { if (t.St == 0) t.St = 1; continue; }
        bool dmg = hitFresh && rng > 1 && Vector3D.Dot(rel / rng, _lkHitDir) > 0.5;        // damage from about its direction (60 deg)
        bool pers = rng <= LkTurR && _now - t.In >= 5;                                     // it has stayed inside gun range
        if (dmg || pers) t.Ev = _now;
        t.St = _now - t.Ev < LkLatch ? 2 : 1;
    }
    foreach (var id in gone) _lk.Remove(id);
}

void LkBox()
{
    if (_now - _lkBoxT < 2) return;
    _lkBoxT = _now;
    double m = Math.Max(_lkRc.CalculateShipMass().PhysicalMass, 1), f = 0, b = 0, r = 0, l = 0, u = 0, d = 0;
    MatrixD w = _lkRc.WorldMatrix;
    foreach (var t in _lkThr)
    {
        if (!t.IsWorking) continue;
        Vector3D p = t.WorldMatrix.Backward; double th = t.MaxEffectiveThrust;      // thrusters push opposite their exhaust
        if (Vector3D.Dot(p, w.Forward) > 0.9) f += th; else if (Vector3D.Dot(p, w.Backward) > 0.9) b += th;
        else if (Vector3D.Dot(p, w.Right) > 0.9) r += th; else if (Vector3D.Dot(p, w.Left) > 0.9) l += th;
        else if (Vector3D.Dot(p, w.Up) > 0.9) u += th; else if (Vector3D.Dot(p, w.Down) > 0.9) d += th;
    }
    _lkAF = f / m; _lkAB = b / m; _lkAR = r / m; _lkAL = l / m; _lkAU = u / m; _lkAD = d / m;
}

void LkPlan(Vector3D me)
{
    var us = new List<Vector3D>(); var ws = new List<double>();
    _lkI1 = 0; _lkI2 = 0; _lkNH = 0;
    var hs = new List<LkT>();
    foreach (var t in _lk.Values) if (t.St == 2) hs.Add(t);
    hs.Sort((x, y) => x.Rng.CompareTo(y.Rng));
    double reff = LkPhR; Vector3D uH = Vector3D.Zero;
    for (int i = 0; i < hs.Count && i < 3; i++)
    {
        Vector3D r = hs[i].P + hs[i].V * (_now - hs[i].Seen) - me;
        if (r.LengthSquared() < 1) continue;
        us.Add(Vector3D.Normalize(r)); ws.Add(_now - _lkHitAt < 6 ? 1.5 : 1.0);
        if (_lkNH == 0) { reff = hs[i].Rng; uH = us[us.Count - 1]; _lkI1 = hs[i].Id; } else if (_lkNH == 1) _lkI2 = hs[i].Id;
        _lkNH++;
    }
    // fire from a bearing no tracked craft explains (a drone beyond sight, or a big gun): the same dodge, as a bearing only
    if (_now - _lkHitAt < 3 && _now - _lkHitDirT < 3)
    {
        bool explained = false;
        foreach (var t in _lk.Values)
        {
            if (_now - t.Seen > 3) continue;
            Vector3D r = t.P + t.V * (_now - t.Seen) - me;
            if (r.LengthSquared() > 1 && Vector3D.Dot(Vector3D.Normalize(r), _lkHitDir) > 0.64) explained = true;      // within 50 deg of the hit direction
        }
        if (!explained) { _lkPhU = _lkHitDir; _lkPhT = _now; }
    }
    bool phantom = _now - _lkPhT < 10;
    if (phantom) { us.Add(_lkPhU); ws.Add(0.7); }
    if (us.Count == 0) { _lkB = Vector3D.Zero; _lkCur = -1; _lkRollOn = false; _lkFl = 0; return; }
    LkBox();
    MatrixD w = _lkRc.WorldMatrix;
    bool haveT = _lkTid != 0 && _now - _lkHeard < 2; Vector3D uT = Vector3D.Zero; double dT = 0;
    if (haveT) { Vector3D r = _lkTPos - me; dT = r.Length(); if (dT > 1) uT = r / dT; else haveT = false; }
    var cs = new Vector3D[8]; var sc = new double[8]; double max = 0;
    for (int k = 0; k < 8; k++)
    {
        int sr = (k & 1) != 0 ? 1 : -1, su = (k & 2) != 0 ? 1 : -1, sf = (k & 4) != 0 ? 1 : -1;
        Vector3D c = w.Right * sr * (sr > 0 ? _lkAR : _lkAL) + w.Up * su * (su > 0 ? _lkAU : _lkAD) + w.Forward * sf * (sf > 0 ? _lkAF : _lkAB);
        cs[k] = c; double s = 0;
        for (int i = 0; i < us.Count; i++) s += ws[i] * (c - Vector3D.Dot(c, us[i]) * us[i]).Length();
        double cl = c.Length();
        if (haveT && cl > 1)
        {
            double cu = Vector3D.Dot(c, uT) / cl;
            if ((cu > 0.2 && dT < _lkStand - LkMargin) || (cu < -0.2 && dT > LkMaxFire)) s = -1;     // keep the large target inside the stand-off band and the rails' range
        }
        sc[k] = s; if (s > max) max = s;
    }
    if (max <= 0) { for (int k = 0; k < 8; k++) sc[k] = cs[k].Length(); max = 1; }                   // (every corner excluded: ignore the band rather than stand still)
    bool bad = _lkCur < 0 || sc[_lkCur] < 0.6 * max;
    if (bad || _now >= _lkNext)
    {
        var ok = new List<int>();
        for (int k = 0; k < 8; k++) if (sc[k] >= 0.8 * max && k != _lkCur) ok.Add(k);
        if (ok.Count == 0) for (int k = 0; k < 8; k++) if (sc[k] >= 0.8 * max) ok.Add(k);
        _lkCur = ok.Count > 0 ? ok[_lkRng.Next(ok.Count)] : 0;
        _lkNext = _now + MathHelper.Clamp((0.7 + 0.8 * _lkRng.NextDouble()) * reff / LkVp, LkPerMin, LkPerMax);
    }
    Vector3D c0 = cs[_lkCur]; double c0l = c0.Length();
    _lkB = c0l > 1 ? c0 / c0l * LkJink : Vector3D.Zero;
    // kite: while a harasser is inside its gun range, also open the range (the nose stays on the large target; the stand-off band of the large target limits it as for the corners)
    if (_lkNH > 0 && LkAway > 0 && reff < LkAwayR)
    {
        Vector3D a = -uH; double au = haveT ? Vector3D.Dot(a, uT) : 0;
        if (!haveT || !((au > 0.2 && dT < _lkStand - LkMargin) || (au < -0.2 && dT > LkMaxFire))) _lkB += a * LkAway;
        if (_lkB.LengthSquared() > 3600) _lkB = Vector3D.Normalize(_lkB) * 60;
    }
    int fl = 1 | 2 | (_lkNH > 0 ? 8 : 0);
    // roll: the nearest harasser above or below the hull (at most a quarter turn), unless it sits near the nose axis; deadband 20 deg
    _lkRollOn = false;
    if (LkRoll && _lkNH > 0)
    {
        Vector3D n = w.Forward, p = uH - Vector3D.Dot(uH, n) * n;
        if (Math.Abs(Vector3D.Dot(uH, n)) < LkNose && p.LengthSquared() > 0.01)
        {
            p.Normalize(); if (Vector3D.Dot(p, w.Up) < 0) p = -p;
            if (Vector3D.Dot(p, w.Up) < 0.94) { _lkUp = p; _lkRollOn = true; fl |= 4; }
        }
    }
    _lkFl = fl;
}

void LkSend()
{
    int fl = _lkFl | 256;
    IGC.SendBroadcastMessage("HDEF" + _lkHid + ".A2H", new MyTuple<Vector3D, Vector3D, int, float, long, long>(_lkB, _lkRollOn ? _lkUp : Vector3D.Zero, fl, 0.6f, _lkI1, _lkI2), TransmissionDistance.CurrentConstruct);
    _lkSent++;
}

void LkStep()
{
    if (!LkOn) { _lkNote = "off"; return; }
    if (_lkYield) { _lkNote = "the Spotter PB runs it"; return; }
    if (_lkA == null) { _lkNote = "no Helm"; return; }
    while (_lkA.HasPendingMessage)
    {
        var m = _lkA.AcceptMessage();
        if (!(m.Data is MyTuple<int, Vector3D, double, double, long, Vector3D>)) continue;
        var d = (MyTuple<int, Vector3D, double, double, long, Vector3D>)m.Data;
        if (((d.Item1 >> 8) & 15) != 1) { _lkNote = "link version?"; continue; }
        int hits = (d.Item1 >> 16) & 0x7FFF;
        if (_lkHits >= 0 && hits != _lkHits) _lkHitAt = _now;
        _lkHits = hits; _lkStand = d.Item4; _lkTid = d.Item5; _lkTPos = d.Item6; _lkHeard = _now;
        if (d.Item2.LengthSquared() > 0.01 && d.Item3 < 5) { _lkHitDir = Vector3D.Normalize(d.Item2); _lkHitDirT = _now - d.Item3; }
    }
    while (_lkK.HasPendingMessage)
    {
        var m = _lkK.AcceptMessage();
        if (!(m.Data is MyTuple<long, Vector3D, Vector3D, float, float, int>)) continue;
        var d = (MyTuple<long, Vector3D, Vector3D, float, float, int>)m.Data;
        LkSee(d.Item1, d.Item2, d.Item3, d.Item4, _now - d.Item5, (d.Item6 & 1) != 0);
    }
    if (_now - _lkHeard > 2) { _lkNote = "waiting for the Helm"; return; }              // (no message sent: the Helm drops the bias after its 0.6 s ttl)
    if (_lkRc == null) { _lkNote = "no [SH] Remote Control"; return; }
    foreach (var tu in _lkTur)
    {
        if (!tu.HasTarget) continue;
        var e = tu.GetTargetedEntity();
        if (e.IsEmpty() || e.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) continue;
        LkSee(e.EntityId, e.Position, e.Velocity, e.BoundingBox.Size.Length() * 0.5, _now, false);
    }
    Vector3D me = _lkRc.GetPosition();
    LkClassify(me);
    LkPlan(me);
    LkSend();
    _lkNote = "link OK";
}

string LkStatus()
{
    var s = new StringBuilder("LOOKOUT  ").Append(_lkNote).Append('\n');
    if (!LkOn || _lkYield || _lkA == null) return s.ToString();
    int sus = 0, har = 0; double near = 0;
    foreach (var t in _lk.Values) { if (t.St == 1) sus++; else if (t.St == 2) { har++; if (near == 0 || t.Rng < near) near = t.Rng; } }
    s.Append("Threat   ").Append(har > 0 ? "HARASSER " + near.ToString("0") + " m x" + har : _now - _lkPhT < 10 ? "FIRE FROM " + Math.Round(Math.Atan2(_lkPhU.X, _lkPhU.Z) * 57.3) + " deg (unseen)" : "none").Append("  tracks ").Append(_lk.Count).Append(" (").Append(sus).Append(" suspect)\n");
    s.Append("Dodge    ").Append(_lkB.LengthSquared() > 1 ? "corner " + _lkCur + "  " + _lkB.Length().ToString("0") + " m/s" : "-").Append(_lkRollOn ? "  roll" : "").Append("  sent ").Append(_lkSent).Append('\n');
    return s.ToString();
}
// <<< LOOKOUT v1.1 <<<
