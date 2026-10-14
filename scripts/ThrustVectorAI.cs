// =====================================================================
//  Thrust Vector PB v1.0   (THRUST VECTORING; docs/thrust-vectoring.md)
//  A second, small Programmable Block next to a drone's flight PB. It watches the ship's thrusters and, when a whole thrust direction is lost
//  (every thruster that pushed the ship forward was shot off, ...), it tells the flight PB how to turn the ship so a SURVIVING thruster group points
//  along the force the flight PB needs - what the Gnat does on purpose - keeping the guns as near the target as that allows.
//  An intact ship is never touched: the flight PB keeps its own attitude and thrust split.
//
//  Link (IGC, TransmissionDistance.CurrentConstruct, no antenna):
//    flight -> here  "TVEC.I"        MyTuple<Vector3D need, Vector3D wantFwd, Vector3D wantUp, Vector3D gunFwd, Vector3D gunUp, int flags>
//                                    need = force (N) the flight PB asks of the thrusters; wantFwd/wantUp = the attitude it would fly; gunFwd/gunUp = its aim block's axes;
//                                    flags bits 0-6 = thrust power scale in percent, bits 7-28 = distance to the target in 10 m (all ones = none). 20 Hz while a group is lost, else 1 Hz.
//    here -> flight  "TVEC.O<flight PB address>"   MyTuple<Vector3D fwd, Vector3D up, int state>
//                                    fwd = attitude to fly instead (zero = none), state bits 0-5 = lost groups (F,B,L,R,U,D), bits 6-8 = group lined up with the force + 1
//  The flight PB (scripts carrying the THRUST VECTOR LINK block, tools/tv_link_block.cs) replaces its wanted forward/up with the answer while it is fresh (0.5 s).
//  Rated thrust = the best working thrust seen per group on THIS grid (thrusters of a printer or carrier never count); lost = working thrust under Lost Fraction of it.
//
//  Custom Data  [Thrust Vectoring]: Enabled, Lost Fraction, Aim Weight, Hold Time (s), Hysteresis, Min Force (m/s2), Fight Range (m: inside it the guns keep priority). "reload" re-reads, "reset" forgets the rated thrust.
// =====================================================================
const string VER = "1.0";
bool TvOn = true; double TvLostFrac = 0.5, TvAimW = 1.0, TvHold = 1.0, TvHyst = 0.1, TvMinNeed = 0.3, TvFightRange = 1200;

IMyShipController _rc;
List<IMyThrust> _thr = new List<IMyThrust>();
List<IMyThrust>[] _thrDir = new List<IMyThrust>[6];
double[] _cap = new double[6], _live = new double[6], _rated = new double[6];
int _lost = 0, _opt = -1, _in = 0; double _optT = -99, _short = 0, _now = 0, _scanT = -99, _chkT = -99, _dispT = -99, _lastInT = -99;
long _grid = 0, _addr = 0, _tick = 0;
IMyBroadcastListener _lIn;
Vector3D _need;
string _err = "";
StringBuilder _sb = new StringBuilder();
const string S = "Thrust Vectoring";

public Program()
{
    for (int i = 0; i < 6; i++) _thrDir[i] = new List<IMyThrust>();
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    _lIn = IGC.RegisterBroadcastListener("TVEC.I"); _lIn.SetMessageCallback("tv");
    Load(); Scan(); Caps();
}

void Load()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    TvOn = B(ini, "Enabled", TvOn);
    TvLostFrac = D(ini, "Lost Fraction", TvLostFrac);
    TvAimW = D(ini, "Aim Weight", TvAimW);
    TvHold = D(ini, "Hold Time (s)", TvHold);
    TvHyst = D(ini, "Hysteresis", TvHyst);
    TvMinNeed = D(ini, "Min Force (m/s2)", TvMinNeed);
    TvFightRange = D(ini, "Fight Range (m)", TvFightRange);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
}
double D(MyIni i, string k, double d) { if (!i.ContainsKey(S, k)) i.Set(S, k, d); return i.Get(S, k).ToDouble(d); }
bool B(MyIni i, string k, bool d) { if (!i.ContainsKey(S, k)) i.Set(S, k, d); return i.Get(S, k).ToBoolean(d); }

// the same controller choice as the flight scripts (first Remote Control, else the first controller that can fly the ship)
void Scan()
{
    _scanT = _now; _err = "";
    var ctrls = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(ctrls, b => b.IsSameConstructAs(Me) && b.CanControlShip);
    _rc = null;
    foreach (var c in ctrls) if (c is IMyRemoteControl) { _rc = c; break; }
    if (_rc == null && ctrls.Count > 0) _rc = ctrls[0];
    GridTerminalSystem.GetBlocksOfType(_thr, b => b.IsSameConstructAs(Me));
    for (int i = 0; i < 6; i++) _thrDir[i].Clear();
    if (_grid != Me.CubeGrid.EntityId) { _grid = Me.CubeGrid.EntityId; for (int i = 0; i < 6; i++) _rated[i] = 0; _opt = -1; }
    if (_rc == null) { _err = "No working Remote Control / cockpit"; return; }
    MatrixD m = _rc.WorldMatrix;
    Vector3D[] dirs = { m.Forward, m.Backward, m.Left, m.Right, m.Up, m.Down };
    double[] own = new double[6];
    foreach (var t in _thr)
    {
        Vector3D push = t.WorldMatrix.Backward;   // thrusters push opposite their exhaust
        for (int i = 0; i < 6; i++) if (Vector3D.Dot(push, dirs[i]) > 0.9) { _thrDir[i].Add(t); if (t.CubeGrid == Me.CubeGrid) own[i] += t.MaxThrust; break; }
    }
    for (int i = 0; i < 6; i++) if (own[i] > _rated[i]) _rated[i] = own[i];
}

// 6 Hz: working thrust per group and the lost mask; 1 Hz: thrusters shot away or on a broken-off piece make a rescan
void Caps()
{
    if (_now - _chkT >= 1)
    {
        _chkT = _now; bool bad = false;
        for (int i = 0; i < 6 && !bad; i++) foreach (var t in _thrDir[i]) if (t.Closed || !t.IsSameConstructAs(Me)) { bad = true; break; }
        if (bad || _now - _scanT > 10) Scan();
    }
    int lost = 0;
    for (int i = 0; i < 6; i++)
    {
        double c = 0, l = 0;
        foreach (var t in _thrDir[i]) if (t.IsWorking) { c += t.MaxEffectiveThrust; l += t.MaxThrust; }
        _cap[i] = c; _live[i] = l;
        if (_rated[i] > 0 && l < TvLostFrac * _rated[i]) lost |= 1 << i;
    }
    if (!TvOn) lost = 0;
    if (lost != _lost) { _lost = lost; _opt = -1; }
}

public void Main(string arg, UpdateType src)
{
    _tick++; _now += Runtime.TimeSinceLastRun.TotalSeconds;
    string a = (arg ?? "").Trim().ToLowerInvariant();
    if (a == "reload") { Load(); Scan(); }
    else if (a == "reset") { for (int i = 0; i < 6; i++) _rated[i] = 0; Scan(); }
    if (_rc == null || !_rc.IsFunctional) { if (_now - _scanT >= 1) Scan(); }
    if ((src & UpdateType.Update10) != 0 || _lost != 0 || _now - _chkT >= 1) Caps();
    MyIGCMessage last = default(MyIGCMessage); bool got = false;
    while (_lIn.HasPendingMessage)
    {
        var m = _lIn.AcceptMessage();
        if (m.Data is MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>) { last = m; got = true; }
    }
    if (got) { _in++; _lastInT = _now; Answer(last); }
    if (_now - _dispT >= 1) { _dispT = _now; Display(); }
}

void Answer(MyIGCMessage m)
{
    var d = (MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>)m.Data;
    _addr = m.Source; _need = d.Item1;
    Vector3D fwd = Vector3D.Zero, up = Vector3D.Zero;
    if (TvOn && _lost != 0 && _rc != null) Decide(d.Item1, d.Item2, d.Item3, d.Item4, d.Item5, d.Item6, ref fwd, ref up);
    else _opt = -1;
    int state = _lost | ((_opt >= 0 && _opt < 6 ? _opt + 1 : 0) << 6);
    IGC.SendBroadcastMessage("TVEC.O" + m.Source, new MyTuple<Vector3D, Vector3D, int>(fwd, up, state), TransmissionDistance.CurrentConstruct);
}

Vector3D Rot(Vector3D v, Vector3D axis, double ang) { double c = Math.Cos(ang), s = Math.Sin(ang); return v * c + Vector3D.Cross(axis, v) * s + axis * (Vector3D.Dot(axis, v) * (1 - c)); }
Vector3D Across(Vector3D v, Vector3D n) { Vector3D r = v - Vector3D.Dot(v, n) * n; return r.LengthSquared() > 1e-8 ? Vector3D.Normalize(r) : Vector3D.Zero; }

// Score "fly the plain attitude" against "line group k up with the force" for each surviving group k: thrust shortfall + aim error (weighted) + turn.
// A group's push direction e_k makes a fixed angle th_k with the gun forward, so lining it up with the force nd puts the gun on the cone of half-angle th_k round nd;
// the point of that cone nearest the wanted forward a is the new forward. Roll: map (gun forward, e_k across it) onto (new forward, nd across it).
void Decide(Vector3D need, Vector3D wantFwd, Vector3D wantUp, Vector3D gf, Vector3D gu, int flags, ref Vector3D fwd, ref Vector3D up)
{
    double mass = _rc.CalculateShipMass().PhysicalMass, F = need.Length();
    if (mass <= 0 || F < TvMinNeed * mass || wantFwd.LengthSquared() < 1e-9) { _opt = -1; return; }
    double ps = Math.Max(0.01, (flags & 127) / 100.0);
    bool fight = ((flags >> 7) & 4194303) * 10.0 < TvFightRange;
    Vector3D nd = need / F, a = Vector3D.Normalize(wantFwd), grav = _rc.GetNaturalGravity();
    MatrixD rm = _rc.WorldMatrix;
    Vector3D[] e = { rm.Forward, rm.Backward, rm.Left, rm.Right, rm.Up, rm.Down };
    double psi = Math.Acos(MathHelper.Clamp(Vector3D.Dot(nd, a), -1, 1));
    double[] cost = new double[7], sh = new double[7]; Vector3D[] fws = new Vector3D[7];
    // The force asked for can exceed anything one group gives (the flight PB asks for up to 50 m/s2): compare options on the force they can DELIVER along the needed
    // direction, against what the strongest group alone could deliver (Fe = min(F, strongest group)).
    double top = 0; for (int k = 0; k < 6; k++) top = Math.Max(top, _cap[k] * ps);
    double Fe = Math.Min(F, top);
    if (Fe <= 0) { _opt = -1; return; }
    // option 6: the plain aim - the force is split over the groups in the attitude the plain aim reaches
    Vector3D ax = Vector3D.Cross(gf, a); double an = Math.Acos(MathHelper.Clamp(Vector3D.Dot(gf, a), -1, 1));
    Vector3D ndb = ax.LengthSquared() > 1e-12 ? Rot(nd, Vector3D.Normalize(ax), -an) : nd;
    double got = 0;
    for (int k = 0; k < 6; k++) { double n = Vector3D.Dot(ndb, e[k]); if (n > 0) got += Math.Min(_cap[k] * ps, F * n) * n; }
    sh[6] = Math.Max(0, Fe - got) / Fe; fws[6] = a;
    double W = TvAimW * (fight ? 1.0 : 0.2);
    if (grav.LengthSquared() > 1 && Vector3D.Dot(nd, -Vector3D.Normalize(grav)) > 0.3 && sh[6] > 0.2) W = 0;   // in gravity do not fall to keep the guns on
    cost[6] = sh[6] + 0.1 * an / Math.PI;
    for (int k = 0; k < 6; k++)
    {
        double cap = _cap[k] * ps;
        if (cap <= 0) { cost[k] = 1e9; continue; }
        double th = Math.Acos(MathHelper.Clamp(Vector3D.Dot(gf, e[k]), -1, 1));
        Vector3D p = Across(a, nd); if (p.LengthSquared() < 0.5) p = Across(gf, nd);
        Vector3D fw = p.LengthSquared() > 0.5 ? Vector3D.Normalize(nd * Math.Cos(th) + p * Math.Sin(th)) : nd;
        fws[k] = fw; sh[k] = Math.Max(0, Fe - cap) / Fe;
        cost[k] = sh[k] + W * Math.Abs(psi - th) / Math.PI + 0.1 * Math.Acos(MathHelper.Clamp(Vector3D.Dot(gf, fw), -1, 1)) / Math.PI;
    }
    int best = 6; double bc = cost[6] - (_opt == 6 || _opt < 0 ? TvHyst : 0);
    for (int k = 0; k < 6; k++) { double c = cost[k] - (_opt == k ? TvHyst : 0); if (c < bc) { bc = c; best = k; } }
    if (_opt >= 0 && best != _opt && _now - _optT < TvHold && sh[_opt] < 0.9) best = _opt;
    if (best != _opt) { _opt = best; _optT = _now; }
    _short = sh[best];
    if (best == 6) return;
    Vector3D fwv = fws[best];
    fwd = fwv; up = wantUp;
    Vector3D u2 = Across(e[best], gf), w2 = Across(nd, fwv);
    if (u2.LengthSquared() > 0.5 && w2.LengthSquared() > 0.5)
    {
        Vector3D u3 = Vector3D.Cross(gf, u2), w3 = Vector3D.Cross(fwv, w2);
        up = fwv * Vector3D.Dot(gu, gf) + w2 * Vector3D.Dot(gu, u2) + w3 * Vector3D.Dot(gu, u3);
    }
}

void Display()
{
    string n = "FBLRUD", l = "";
    for (int i = 0; i < 6; i++) if ((_lost & (1 << i)) != 0) l += (l.Length > 0 ? "," : "") + n[i];
    _sb.Clear();
    _sb.Append("THRUST VECTOR v").Append(VER).Append(TvOn ? "" : "  (OFF)").Append('\n');
    if (_err != "") _sb.Append("! ").Append(_err).Append('\n');
    _sb.Append("Flight : ").Append(_in == 0 ? "no message yet" : "link OK, " + (_now - _lastInT).ToString("0.0") + " s ago").Append('\n');
    _sb.Append(l == "" ? "Thrust : all groups ok\n" : "!! THRUST LOST " + l + "  " + (_opt >= 0 && _opt < 6 ? "vectoring " + n[_opt] : "aim as is") + (_short > 0.05 ? "  short " + (_short * 100).ToString("0") + "%" : "") + "\n");
    _sb.Append("Groups : ");
    for (int i = 0; i < 6; i++) _sb.Append(n[i]).Append(' ').Append(_rated[i] > 0 ? (_live[i] / _rated[i] * 100).ToString("0") + "%" : "-").Append(i < 5 ? "  " : "\n");
    var s = Me.GetSurface(0); s.ContentType = ContentType.TEXT_AND_IMAGE; s.FontSize = 0.8f; s.Font = "Monospace"; s.WriteText(_sb);
    Echo(_sb.ToString());
}
