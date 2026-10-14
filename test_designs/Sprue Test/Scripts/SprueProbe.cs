// SprueProbe v2 - in-game test of printer option A (sprue neck + ship grinder), blueprint "Sprue Test" (docs/design/sprue_test.md).
// ONE script, two roles, chosen by the Custom Data line Role=printer | payload. Both PBs of the blueprint carry it.
// PRINTER commands (type into the PB's Argument box, press Run):
//   weld    welders ON, grinder OFF: print the payload; ends by itself when every payload cell is welded to 100 %
//           (refused while the cut payload is still in the bay: push it away first, or "weld force")
//   charge  printer battery Discharge + payload battery Recharge for Charge Seconds, then both Auto
//   cut     welders OFF, grinder ON until the payload splits off or Cut Seconds pass; stops at once if a printer guard cell is ground
//           (refused while the grinder sits in the wrong place: fix it, or "cut force")
//   hand    welders and grinder OFF, payload told to HOLD: grind neck cell N2 (2nd red cell from the yellow stub) by hand
//   push    TryRun("push") on the payload PB through the reference cached before the split
//   auto    weld, charge, cut, wait until the payload is Clear Metres away, weld again ... Cycles times (completes only if the grinder cuts)
//   stop    welders and grinder OFF, batteries Auto      snap  log a snapshot line      reset  clear the log
// PAYLOAD commands: push (thrust Push Percent for Push Seconds; refused while on the printer grid), hold, auto
// Readout: screen 0 of the PB and the terminal info (Echo, every run); every event line also goes to Custom Data below [Log].
// The printer copies the payload's log lines (prefix P:) into its own [Log]; its RESULTS block holds the Q1-Q4 answers.
const string PDEF = "Role=payload|Me Cell=0,-1,-4|Printer PB=Sprue PB|Battery=Sprue Payload Battery|Neck=0,0,3;0,0,2;0,0,1;0,0,0;0,0,-1;0,0,-2;0,0,-3|Cluster=0,0,-4;0,0,-5;0,-1,-4;0,1,-5|Push=hold|Push Percent=2|Push Seconds=1|Push Delay=15";
const int W = 46;
Dictionary<string, string> _cfg = new Dictionary<string, string>(), _r = new Dictionary<string, string>();
bool _pr, _pay, _cdLost, _auto, _split, _seen, _stall, _onPr, _pushed, _projMsg, _gPosOk, _stuck, _movLog, _lostLog;
Vector3I _d;
List<Vector3I> _neck = new List<Vector3I>(), _clus = new List<Vector3I>(), _guard = new List<Vector3I>();
List<string> _log = new List<string>();
double _t, _ts, _tw, _tPl = -1, _tBu = -1, _tCut = -1, _tSp = -1, _lastQ = -1, _lastProg, _push = -1, _spd, _dist0, _gT, _gW, _bat0 = -1, _mov, _vP;
int _runs, _boots, _cyc = 1, _cycles = 2, _gok0, _hb0 = -1;
string _st = "IDLE", _q1 = "?", _n0 = "", _pLast, _pushMode = "hold";
float _nmin0 = 1;
double _nsum0, _allT = -1;
int _nx, _nn0;
string _apiTxt = "";
long _g0;
IMyProgrammableBlock _pp;
IMyBatteryBlock _pb;
Vector3D _lp, _p0;
IMyTextSurface _lcd;
StringBuilder _sb = new StringBuilder();

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    try
    {
        int.TryParse(Storage, out _boots); _boots++;
        Cfg();
        int li = Me.CustomData.IndexOf("[Log]");
        if (li >= 0) foreach (var ol in Me.CustomData.Substring(li + 5).Split('\n')) if (ol.Trim().Length > 0) _log.Add(ol.Trim());
        while (_log.Count > 59) _log.RemoveAt(0);
        foreach (var ol in _log) { int pj = ol.IndexOf("] P:"); if (pj >= 0) _pLast = ol.Substring(pj + 4); }
        if (_pay) { var ppb = GridTerminalSystem.GetBlockWithName(S("Printer PB", "Sprue PB")); _onPr = ppb != null && Same(ppb); _seen = _onPr; }
        _g0 = Me.CubeGrid.EntityId;
        _lp = Me.GetPosition();
        Log("COMPILED boot " + _boots + " role " + Role() + " grid " + Id(_g0) + " owner " + Me.OwnerId + (Me.IsFunctional ? "" : " (not functional yet)"));
        if (_cdLost) Log("Custom Data LOST on weld: running on the built-in payload config -> Q3 custom data NO (C4)");
        try { _lcd = Me.GetSurface(0); _lcd.ContentType = ContentType.TEXT_AND_IMAGE; _lcd.Font = "Monospace"; _lcd.FontSize = 0.42f; } catch (Exception) { _lcd = null; }
    }
    catch (Exception e) { Log("ERROR in constructor: " + e.Message); }
}

public void Save() { Storage = _boots.ToString(); }

public void Main(string arg, UpdateType u)
{
    _runs++;
    double dt = Runtime.TimeSinceLastRun.TotalSeconds; _t += dt; _ts += dt;
    arg = (arg ?? "").Trim().ToLower();
    try
    {
        if (arg.Length > 0) { Cfg(); Log("> " + arg); }
        var p = Me.GetPosition(); if (dt > 0) _spd = Vector3D.Distance(p, _lp) / dt; _lp = p;
        if (_pr) Printer(arg, dt); else if (_pay) Payload(arg); else if (arg.Length > 0) Log("NO CONFIG: Custom Data has no Role= line");
    }
    catch (Exception e) { LogErr("ERROR " + e.Message); }
    try { Show(); } catch (Exception e) { _sb.Clear(); _sb.Append("SHOW ERROR " + e.Message + "\n"); LogErr("SHOW ERROR " + e.Message); }
    try { string txt = _sb.ToString(); if (_lcd != null) _lcd.WriteText(txt); Echo(txt); } catch (Exception) { }
}

// ---------------- configuration and helpers ----------------
string Role() { return _pr ? "printer" : (_pay ? "payload" : "NONE"); }
void Cfg()
{
    _cfg.Clear();
    string head = Head(Me.CustomData);
    _cdLost = head.IndexOf("Role=") < 0 && Me.CustomName.Contains("Payload");
    if (_cdLost) Parse(PDEF.Replace('|', '\n'));
    Parse(head);
    string r = S("Role", "?"); _pr = r == "printer"; _pay = r == "payload";
    _d = Me.Position - V(S("Me Cell", "0,0,0"));
    _neck = L(S("Neck", "")); _clus = L(S("Cluster", "")); _guard = L(S("Guard", ""));
    _cycles = (int)N("Cycles", 2);
}
void Parse(string txt)
{
    foreach (var raw in txt.Split('\n'))
    {
        var l = raw.Trim(); int i = l.IndexOf('=');
        if (i > 0) _cfg[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
    }
}
string Head(string cd)
{
    int a = cd.IndexOf("[Status]"), b = cd.IndexOf("[Log]");
    int c = a < 0 ? b : (b < 0 ? a : Math.Min(a, b));
    return c < 0 ? cd : cd.Substring(0, c);
}
string S(string k, string d) { string v; return _cfg.TryGetValue(k, out v) ? v : d; }
double N(string k, double d) { double v; return double.TryParse(S(k, ""), out v) ? v : d; }
Vector3I V(string s)
{
    var p = s.Split(','); int x = 0, y = 0, z = 0;
    if (p.Length != 3 || !int.TryParse(p[0].Trim(), out x) || !int.TryParse(p[1].Trim(), out y) || !int.TryParse(p[2].Trim(), out z)) Log("bad cell '" + s + "' in Custom Data");
    return new Vector3I(x, y, z);
}
List<Vector3I> L(string s)
{
    var r = new List<Vector3I>();
    foreach (var c in s.Split(';')) if (c.Trim().Length > 0) r.Add(V(c.Trim()));
    return r;
}
string Id(long id) { return (Math.Abs(id) % 1000000).ToString("D6"); }
string F(double v, int n) { return Math.Round(v, n).ToString(); }
string P3(Vector3I v) { return v.X + "," + v.Y + "," + v.Z; }
string Cut(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }
float Pct(IMySlimBlock s) { return s.MaxIntegrity > 0 ? (s.BuildIntegrity - s.CurrentDamage) / s.MaxIntegrity : 0f; }
bool Ex(Vector3I c) { try { return Me.CubeGrid.CubeExists(c); } catch (Exception) { return false; } }
// cells of a list that exist on MY grid: count, number at 100 %, lowest integrity, summed integrity, a map like "OOo.xOO" (O full, o partial, x exists but its
// integrity cannot be read: GetCubeBlock gave null in the game for plain armour, CubeExists still says yes; counted as full; . missing or unreadable)
int Cells(List<Vector3I> cs, out int full, out float mn, out string map, out double sum)
{
    int n = 0; full = 0; mn = 1f; sum = 0; _nx = 0; var m = new StringBuilder();
    foreach (var c in cs)
    {
        var s = Me.CubeGrid.GetCubeBlock(c + _d);
        if (s == null)
        {
            if (Ex(c + _d)) { n++; full++; _nx++; m.Append('x'); } else { m.Append('.'); mn = 0f; }
            continue;
        }
        n++; float p = Pct(s); sum += p; if (p < mn) mn = p;
        if (p >= 0.999f) { full++; m.Append('O'); } else m.Append('o');
    }
    map = m.ToString(); return n;
}
string Map(List<Vector3I> cs) { int f; float m; string map; double s; Cells(cs, out f, out m, out map, out s); return map; }
void Log(string s)
{
    string l = "[" + F(_t, 0) + "s] " + s;
    _log.Add(l); if (_log.Count > 60) _log.RemoveAt(0);
    Write();
}
void Write()
{
    var b = new StringBuilder(Head(Me.CustomData).TrimEnd());
    if (_pay) b.Append("\n[Status]\nStatus=" + Status());
    b.Append("\n[Log]\n");
    foreach (var l in _log) b.Append(l).Append('\n');
    Me.CustomData = b.ToString();
}
bool Same(IMyCubeBlock b) { try { return b != null && b.CubeGrid.EntityId == Me.CubeGrid.EntityId; } catch (Exception) { return false; } }
HashSet<string> _errs = new HashSet<string>();
void LogErr(string s) { if (_errs.Count < 30 && _errs.Add(s)) Log(s); }
IMyTerminalBlock Mine(string n) { var l = new List<IMyTerminalBlock>(); GridTerminalSystem.SearchBlocksOfName(n, l, b => b.CustomName == n && Same(b)); return l.Count > 0 ? l[0] : null; }
string Q(bool v) { return v ? "yes" : "NO"; }
string Fld(string st, string k) { foreach (var w in st.Split(' ')) if (w.StartsWith(k + "=")) return w.Substring(k.Length + 1); return "?"; }

// ---------------- printer ----------------
IMyFunctionalBlock Blk(string n) { return GridTerminalSystem.GetBlockWithName(n) as IMyFunctionalBlock; }
List<IMyFunctionalBlock> Welders()
{
    var l = new List<IMyTerminalBlock>(); var r = new List<IMyFunctionalBlock>();
    GridTerminalSystem.SearchBlocksOfName("Sprue Welder", l);
    foreach (var b in l) { var f = b as IMyFunctionalBlock; if (f != null && Same(b)) r.Add(f); }
    return r;
}
IMyFunctionalBlock Grinder()
{
    var l = new List<IMyTerminalBlock>();
    GridTerminalSystem.SearchBlocksOfName("Grinder", l, b => Same(b));
    return l.Count > 0 ? l[0] as IMyFunctionalBlock : null;
}
void Tools(bool w, bool g)
{
    foreach (var x in Welders()) x.Enabled = w;
    var gr = Grinder(); if (gr != null) gr.Enabled = g; else if (g) Log("no grinder on this grid (place one, name it Sprue Grinder)");
}
void SetPush(string v)
{
    _pushMode = v;
    if (_pp == null) return;
    try
    {
        var o = new StringBuilder(); bool hit = false;
        foreach (var l in _pp.CustomData.Split('\n'))
        {
            if (l.Trim().StartsWith("Push=")) { o.Append("Push=" + v); hit = true; } else o.Append(l);
            o.Append('\n');
        }
        _pp.CustomData = (hit ? "" : "Push=" + v + "\n") + o.ToString().TrimEnd('\n');
    }
    catch (Exception) { Log("payload ref lost (SetPush)"); _pp = null; }
}
string PayStatus()
{
    try { if (_pp != null) foreach (var l in _pp.CustomData.Split('\n')) if (l.StartsWith("Status=")) return l.Substring(7); } catch (Exception) { }
    return "none";
}
void CopyPay()                                    // the payload's [Log] lines into mine, prefix P:, so one Custom Data copy holds both sides
{
    if (_pp == null) return;
    try
    {
        string cd = _pp.CustomData; int i = cd.IndexOf("[Log]"); if (i < 0) return;
        var ls = new List<string>();
        foreach (var l in cd.Substring(i + 5).Split('\n')) if (l.Trim().Length > 0) ls.Add(l.Trim());
        int k = _pLast == null ? -1 : ls.LastIndexOf(_pLast);
        for (int j = k + 1; j < ls.Count; j++) { Log("P:" + ls[j]); if (ls[j].Contains("] PUSH ")) { _pushMode = "pushed"; _stuck = false; if (_st == "FREE") _ts = 0; } }
        if (ls.Count > 0) _pLast = ls[ls.Count - 1];
    }
    catch (Exception) { }
}
bool Gone(IMyCubeBlock b) { try { return b == null || b.CubeGrid == null || b.CubeGrid.GetCubeBlock(b.Position) == null; } catch (Exception) { return true; } }
double PayMoved() { try { return Gone(_pp) ? -1 : Vector3D.Distance(_pp.GetPosition(), Me.GetPosition()) - _dist0; } catch (Exception) { return -1; } }
string PayNeck()
{
    try { if (Gone(_pp)) return "?"; var m = new StringBuilder(); foreach (var c in _neck) { var s = _pp.CubeGrid.GetCubeBlock(c + _d); m.Append(s == null ? (_pp.CubeGrid.CubeExists(c + _d) ? 'x' : '.') : (Pct(s) >= 0.999f ? 'O' : 'o')); } return m.ToString(); }
    catch (Exception) { return "?"; }
}
string PayBat() { try { return _pb == null ? "-" : F(_pb.CurrentStoredPower, 4) + "/" + F(_pb.MaxStoredPower, 4) + " " + _pb.ChargeMode; } catch (Exception) { _pb = null; return "lost"; } }
string Own(long o)
{
    if (o == Me.OwnerId) return "=me";
    if (o == 0) return "0 (Nobody)";
    return o.ToString() + (o.ToString() == S("Blueprint Owner", "") ? " (=BP)" : "");
}
void Go(string s) { _st = s; _ts = 0; if (s == "FREE") _stuck = false; }
bool PayInBay(double mv) { return _split && !Gone(_pp) && mv < N("Clear Metres", 6); }
void StartWeld()
{
    Bats(ChargeMode.Auto);
    _split = false; _seen = false; _stall = false; _projMsg = false; _stuck = false; _pp = null; _pb = null; _pLast = null; _hb0 = -1; _bat0 = -1;
    _allT = -1; _tw = _t; _tPl = -1; _tBu = -1; _tSp = -1; _q1 = "?"; _lastQ = -1; _lastProg = _t;
    var p = Blk(S("Projector", "Sprue Projector")) as IMyProjector; if (p != null) p.Enabled = true;
    Tools(true, false); Go("WELD");
    string ps = "MISSING"; try { if (p != null) ps = (p.IsProjecting ? "on" : "OFF") + " tot " + p.TotalBlocks + " rem " + p.RemainingBlocks; } catch (Exception) { ps = "unreadable"; }
    Log("WELD start, cycle " + _cyc + ": proj " + ps);
}
int GuardFull() { int ok = 0; foreach (var c in _guard) { var s = Me.CubeGrid.GetCubeBlock(c + _d); if (s != null ? Pct(s) >= 0.999f : Ex(c + _d)) ok++; } return ok; }
string Guards()
{
    int n = 0; float mn = 1f;
    foreach (var c in _guard) { var s = Me.CubeGrid.GetCubeBlock(c + _d); n++; if (s == null) { if (!Ex(c + _d)) mn = 0f; } else mn = Math.Min(mn, Pct(s)); }
    return GuardFull() + "/" + n + " full, min " + F(mn * 100, 0) + "%";
}
void StartCut(bool force)
{
    var gr = Grinder();
    if (gr == null) { _auto = false; Log("cut refused: no grinder on this grid (No Grinder fallback: place one first)"); return; }
    string pos = GPos(gr);
    if (pos.Length > 0 && !force) { _auto = false; Log("cut refused: grinder" + pos + " (x+ = toward the welders, y+ = projector side, z+ = toward the printer). Fix it, or: cut force"); return; }
    int f; string map; _nn0 = Cells(_neck, out f, out _nmin0, out map, out _nsum0);
    if (!_split && _tBu < 0 && f < _neck.Count && !force) { _auto = false; Log("cut refused: neck not fully welded (" + map + "): finish weld (BUILT) first, or: cut force"); return; }
    Bats(ChargeMode.Auto);
    if (_pb != null && _pb.CurrentStoredPower < 0.001f)
    {
        Log("payload battery EMPTY (" + F(_pb.CurrentStoredPower, 4) + " MWh): it will be dead after the split. Run charge longer first");
        if (_auto) { _auto = false; Go("IDLE"); return; }
    }
    _n0 = _split ? PayNeck() : map;
    SetPush(_split ? "hold" : "auto");
    _gok0 = GuardFull(); _gT = 0; _gW = 0; _gPosOk = pos.Length == 0;
    Tools(false, true); _tCut = _t; Go(_split ? "CUT2" : "CUT");
    Log("CUT start: neck " + _n0 + (_split ? "" : " min " + F(_nmin0 * 100, 0) + "%") + ", guards " + Guards() + ", grinder pos " + (_gPosOk ? "OK" : "WRONG (forced)") + ", payload bat " + PayBat() +
        (_split ? " (payload already split: grinder on the OTHER grid's neck)" : ""));
}
void Snap()
{
    int f; float m; string map; double s; int nc = Cells(_clus, out f, out m, out map, out s), nn = Cells(_neck, out f, out m, out map, out s);
    int ag = 0, ae = 0; foreach (var c in _neck) { if (Me.CubeGrid.GetCubeBlock(c + _d) != null) ag++; if (Ex(c + _d)) ae++; }
    _apiTxt = "neck cells (armour): GetCubeBlock " + ag + "/" + _neck.Count + ", CubeExists " + ae + "/" + _neck.Count; _r["Qa"] = "API " + _apiTxt;
    string own = "no PB";
    try { if (_pp != null) own = Own(_pp.OwnerId) + ", printer " + Me.OwnerId; } catch (Exception) { }
    string pb = "not found";
    try { if (_pp != null) pb = (Same(_pp) ? "on printer grid" : "other grid") + " working " + Q(_pp.IsWorking) + " owner " + own + " status " + PayStatus(); } catch (Exception) { }
    Log("SNAP c" + _cyc + " Q1 " + _q1 + " cluster " + nc + "/" + _clus.Count + " neck " + nn + "/" + _neck.Count + " (" + _apiTxt + ") | Q3 PB " + pb +
        " | bat " + PayBat() + " MWh (blueprint 0.04) | placed " + (_tPl < 0 ? "-" : F(_tPl - _tw, 0) + "s") + " built " + (_tBu < 0 ? "-" : F(_tBu - _tw, 0) + "s"));
}
string GPos(IMyFunctionalBlock g)                 // "" when the grinder sits where the design puts it, else the move in grid cells
{
    if (g == null) return " NONE";
    var dd = V(S("Grinder Min", "0,0,0")) - (g.Min - _d);
    bool ok = dd == Vector3I.Zero && g.Max - _d == V(S("Grinder Max", "0,0,0")) && g.Orientation.Forward.ToString() == S("Grinder Forward", "Up");
    if (ok) return "";
    return " pos WRONG move x" + (dd.X >= 0 ? "+" : "") + dd.X + " y" + (dd.Y >= 0 ? "+" : "") + dd.Y + " z" + (dd.Z >= 0 ? "+" : "") + dd.Z +
        (g.Orientation.Forward.ToString() == S("Grinder Forward", "Up") ? (dd == Vector3I.Zero ? " turn about tip" : "") : " tip->y+");
}
void Watch()
{
    var p = Mine(S("Payload PB", "Sprue Payload PB")) as IMyProgrammableBlock;
    if (p != null) _pp = p;
    var b = Mine(S("Payload Battery", "Sprue Payload Battery")) as IMyBatteryBlock;
    if (b != null) _pb = b;
    int f; float m; string map; double s; int nc = Cells(_clus, out f, out m, out map, out s);
    if (nc > 0) _seen = true;
    if (_seen && !_split && (nc == 0 || (_pp != null && !Same(_pp))))
    {
        _split = true; _tSp = _t;
        string nmap = Map(_neck), g = Guards();
        try { _dist0 = _pp == null ? 0 : Vector3D.Distance(_pp.GetPosition(), Me.GetPosition()); } catch (Exception) { _dist0 = 0; }
        if (_st != "CUT") SetPush("hold");
        string pg = "?"; try { if (_pp != null) pg = Id(_pp.CubeGrid.EntityId); } catch (Exception) { }
        Log("SPLIT c" + _cyc + " in state " + _st + (_st == "CUT" ? " " + F(_t - _tCut, 1) + "s after cut start" : "") +
            ": printer kept its grid " + Q(Me.CubeGrid.EntityId == _g0) + ", neck left on printer " + nmap + ", payload PB grid " + pg +
            ", payload bat " + PayBat() + ", push " + _pushMode + ", guards " + g);
        if (_st == "CUT") _r["Q2"] = "Q2 cut SPLIT +" + F(_t - _tCut, 0) + "s: grinds OWN grid";
        else _r["Q2h"] = "Q2 hand cut: SPLIT";
        _r["Q2c"] = "Q2c guards " + g;
    }
}
void Printer(string a, double dt)
{
    var proj = Blk(S("Projector", "Sprue Projector")) as IMyProjector;
    if (a == "weld" || a == "weld force")
    {
        double mv = PayMoved();
        if (PayInBay(mv) && a != "weld force") Log("weld refused: the cut payload is still in the bay (moved " + F(mv, 1) + " m): push it away first, delete it, or: weld force");
        else if (!_split && _tBu >= 0 && a != "weld force") Log("weld refused: already BUILT (Q1/Q3 recorded for this payload); next: charge, cut or hand, or: weld force");
        else { if (_split) _cyc++; StartWeld(); }
    }
    else if (a == "cut" || a == "cut force") StartCut(a == "cut force");
    else if (a == "charge") StartCharge();
    else if (a == "hand") { Tools(false, false); Bats(ChargeMode.Auto); SetPush("hold"); Go("HAND"); _auto = false; Log("HAND: payload on HOLD. Grind ONLY neck cell N2 (the 2nd red cell from the yellow stub) with your hand grinder; leave N1"); }
    else if (a == "push") { if (_pp == null) Log("no payload PB known"); else { bool ok = false; try { ok = _pp.TryRun("push"); } catch (Exception) { } if (ok) { _pushMode = "pushed"; _stuck = false; if (_st == "FREE") _ts = 0; } Log("TryRun(push) on the payload PB: " + (ok ? "true" : "false") + (ok ? "" : " -> run push on Sprue Payload PB from its own terminal")); } }
    else if (a == "auto")
    {
        if (PayInBay(PayMoved())) Log("auto refused: the cut payload is still in the bay: push it away or delete it first");
        else if (!_split && _tBu >= 0) { _auto = true; Log("auto: payload already BUILT, continuing with charge"); StartCharge(); }
        else { _auto = true; if (_split) _cyc++; StartWeld(); }
    }
    else if (a == "stop") { _auto = false; Tools(false, false); Bats(ChargeMode.Auto); Go("IDLE"); }
    else if (a == "snap") Snap();
    else if (a == "reset") { _log.Clear(); _r.Clear(); Write(); }
    else if (a.Length > 0) Log("unknown command '" + a + "'; valid: weld charge cut hand push auto stop snap reset");
    Watch();
    if (_runs % 6 == 0) CopyPay();
    var gr = Grinder();
    if (gr != null && gr.Enabled && !_st.StartsWith("CUT")) { gr.Enabled = false; Log("grinder switched OFF (it is ON only during cut; a hand-placed grinder starts ON)"); }
    if (_spd > 0.3) { _mov += dt; if (_mov > 5 && !_movLog) { _movLog = true; Log("PRINTER MOVING " + F(_spd, 2) + " m/s: paste in space, no gravity, nothing touching it"); } } else _mov = 0;
    if (_pp != null)
    {
        string st = PayStatus(); int hb; if (int.TryParse(Fld(st, "hb"), out hb)) { if (_hb0 < 0) _hb0 = hb; }
        string own = "?"; try { own = Own(_pp.OwnerId); } catch (Exception) { }
        _r["Q3"] = "Q3 PB " + (hb > _hb0 && _hb0 >= 0 ? "RUNS hb " + hb : "hb " + (_hb0 < 0 ? "none" : hb.ToString()) + " (not rising?)") + " boot " + Fld(st, "boots") + " cd " + Fld(st, "cd");
        _r["Q3o"] = "Q3 owner " + own + (Me.OwnerId.ToString() == S("Blueprint Owner", "") ? " (void: you own BP id)" : "");
    }
    if (_pb != null && _bat0 >= 0) _r["Q3b"] = "Q3 bat welded " + F(_bat0, 4) + " now " + PayBat();
    int full; float mn; string map; double sum;
    var all = new List<Vector3I>(_neck); all.AddRange(_clus);
    int have = Cells(all, out full, out mn, out map, out sum);
    if (_st == "WELD")
    {
        int rem = -1, tot = 0, bld = 0; bool pon = false;
        try { if (proj != null) { pon = proj.IsProjecting; rem = proj.RemainingBlocks; tot = proj.TotalBlocks; bld = proj.BuildableBlocksCount; } } catch (Exception) { }
        if (!_projMsg && _ts > 5 && (proj == null || !pon || tot == 0))
        { _projMsg = true; _q1 = "no projection"; Log("PROJECTION MISSING (proj " + (proj == null ? "not found" : (pon ? "on" : "OFF") + " tot " + tot) + "): projector ON? blueprint loaded? No Q1 verdict"); }
        else if (!_projMsg && _ts > 5 && tot != all.Count) { _projMsg = true; Log("projection has " + tot + " blocks, expected " + all.Count); }
        if (_tPl < 0 && tot > 0 && rem == 0 && _ts > 5) { _tPl = _t; Log("PLACED: projector Remaining 0 after " + F(_t - _tw, 0) + "s (it does not count armour), payload cells seen on printer grid " + have + "/" + all.Count); }
        double q = have + sum;
        if (q > _lastQ + 0.0005) { _lastQ = q; _lastProg = _t; _stall = false; }
        int neckPres = 0, clO = 0, clo = 0;
        for (int i = 0; i < map.Length; i++) { if (i < _neck.Count) { if (map[i] != '.') neckPres++; } else if (map[i] == 'O') clO++; else if (map[i] == 'o') clo++; }
        int nxs = _nx;
        bool allIn = have == all.Count && full == all.Count;
        if (allIn) { if (_allT < 0) _allT = _t; } else _allT = -1;
        bool settled = allIn && _t - _allT >= (nxs > 0 ? N("Settle Seconds", 25) : 0);
        bool blind = !allIn && neckPres == 0 && tot > 0 && rem == 0 && _tPl >= 0 && _t - _tPl >= N("Blind Seconds", 40) && clO >= _clus.Count - 1 && clo == 0;
        if (settled || blind)
        {
            _tBu = _t; _q1 = _pp != null ? "FUSED" : "?"; Tools(false, false);
            if (_pb != null) _bat0 = _pb.CurrentStoredPower;
            Log("BUILT" + (blind ? " (armour BLIND)" : "") + " in " + F(_t - _tw, 0) + "s: " + (blind
                ? "the neck cells cannot be read by this API (plain armour: GetCubeBlock and CubeExists give nothing); battery, PB and thruster are at 100 % on the PRINTER grid and the projector has 0 left: neck ASSUMED welded -> Q1 " + _q1
                : "every payload cell is a block of the PRINTER grid" + (nxs > 0 ? " (" + nxs + " armour cells seen by CubeExists only, integrity unreadable: waited " + F(N("Settle Seconds", 25), 0) + "s)" : "") + " -> Q1 FUSED")); Snap();
            if (_cyc == 1) _r["Q1"] = "Q1 FUSED (built " + F(_t - _tw, 0) + "s)"; else _r["Q4b"] = "Q4 c" + _cyc + " re-weld BUILT " + F(_t - _tw, 0) + "s";
            if (_auto) StartCharge(); else Go("IDLE");
        }
        else if (!_stall && _t - _lastProg > 45 && tot > 0)
        {
            _stall = true; string why;
            string bad = ""; int k = 0; foreach (var w in Welders()) { k++; if (w.Enabled && !w.IsWorking) bad += " " + k; }
            if (have == 0 && bld == 0 && rem == tot) why = "nothing buildable: ghost not attached? Projector Offset must read 0,1,2 and the ghost neck touch the yellow stub";
            else if (bad.Length > 0) why = "welder" + bad + " ON but not working: power/owner";
            else if (have == 0) { why = "welders on, NO payload block on my grid. If you SEE welded grey payload blocks: Q1 NOT FUSED? (check Info tab grid list and the payload PB screen), do NOT hand-weld. If nothing is welded: Survival (needs components) or a safe zone"; _q1 = "? nothing on my grid"; }
            else if (neckPres == 0 && rem == 0 && clO >= _clus.Count - 1) why = "the neck cells cannot be read (armour is invisible to this API): not a reach problem; the BLIND fallback ends the weld, or check the neck by eye";
            else why = "reach stall (B3)? Wait 2 more minutes, report the map, then hand-weld the . and o cells" + (_cyc > 1 ? ". Cycle 2: a loose red cube in a '.' cell blocks the weld: grind or delete it" : "");
            Log("WELD STALL 45s: payload " + map + " (N1..N7, hub, bat, PB, thr; O full o partial . missing) proj rem " + rem + " bld " + bld + " tot " + tot + ": " + why);
        }
    }
    else if (_st == "CHARGE" && _ts >= N("Charge Seconds", 30))
    {
        Bats(ChargeMode.Auto);
        Log("CHARGED: payload battery " + PayBat() + " MWh");
        Go("IDLE"); if (_auto) StartCut(false);
    }
    else if (_st == "CUT" || _st == "CUT2")
    {
        _gT += dt; if (gr != null && gr.Enabled && gr.IsWorking) _gW += dt;
        string gw = "grinder working " + F(_gW, 0) + " of " + F(_gT, 0) + "s, pos " + (_gPosOk ? "OK" : "WRONG");
        bool valid = _gW >= 0.9 * _gT && _gPosOk;
        if (GuardFull() < _gok0)
        {
            Tools(false, false); _auto = false; SetPush("hold");
            Log("GUARD HIT: a printer cell near the grinder lost integrity, grinder stopped. Q2c collateral YES (D2). guards " + Guards() + ", neck " + Map(_neck));
            _r["Q2c"] = "Q2c GUARD HIT " + Guards(); Go("IDLE");
        }
        else if (_st == "CUT" && _split) { Tools(false, false); Log("CUT OK: grinder off " + F(_t - _tCut, 1) + "s after start, " + gw); Go("FREE"); }
        else if (_ts >= N("Cut Seconds", 60))
        {
            Tools(false, false);
            if (_st == "CUT")
            {
                int f; float m; string mp; double s; int nn = Cells(_neck, out f, out m, out mp, out s);
                bool untouched = nn >= _nn0 && s >= _nsum0 - 0.001 && m >= _nmin0 - 0.001f;
                bool blindN = _nn0 == 0;
                string v = !valid ? "INVALID (D4): grinder not working or wrongly placed. Fix and repeat cut"
                         : blindN ? "NO SPLIT, and this API cannot read the neck (armour): LOOK at it. Neck intact = the grinder did not cut it (D3); cells gone or damaged = D5 (ground, not through)"
                         : untouched ? "UNTOUCHED (valid, D3): the grinder does not grind its own grid. Next: type hand, then grind N2 (2nd red cell from the yellow stub)"
                                     : "ground, not through (D5): run cut again";
                SetPush("hold");
                Log("NO SPLIT after " + F(_ts, 0) + "s: neck " + _n0 + " -> " + mp + ", min " + F(_nmin0 * 100, 0) + "% -> " + F(m * 100, 0) + "%, " + gw + " -> " + v + ". Payload set to HOLD");
                _r["Q2"] = "Q2 cut " + (!valid ? "INVALID" : blindN ? "NO SPLIT (neck unreadable: LOOK)" : (untouched ? "UNTOUCHED" : "ground")) + " grind " + F(_gW, 0) + "/" + F(_gT, 0) + "s";
                _auto = false; Go("IDLE");
            }
            else
            {
                string now = PayNeck();
                string note = now.Trim('.').Length == 0 && _n0.Trim('.').Length == 0 ? " (neck unreadable here: LOOK at the payload's neck)" : "";
                Log("CUT2 end: " + gw + (valid ? "" : " (INVALID, D4)") + "; payload neck before/now " + _n0 + " / " + now + " (N1 N2 '.' = left with the hand cut)" + note + ". Next: push");
                _r["Q2h"] = "Q2 hand SPLIT, CUT2 " + _n0 + ">" + now + (valid ? "" : " INVALID"); Go("FREE");
            }
        }
    }
    else if (_st == "HAND" && _split) Go("FREE");
    else if (_st == "FREE")
    {
        double d = PayMoved();
        if (d > N("Clear Metres", 6))
        {
            Log("PAYLOAD LEFT: moved " + F(d, 1) + " m in " + F(_t - _tSp, 1) + "s after the split; guards now " + Guards() + "; payload status " + PayStatus());
            _r["Q4"] = "Q4 payload LEFT " + F(d, 1) + "m in " + F(_t - _tSp, 0) + "s";
            if (_auto && _cyc < _cycles) { _cyc++; StartWeld(); } else { _auto = false; Go("IDLE"); }
        }
        else if (!_stuck && _ts > 45)
        {
            _stuck = true;
            Log(_pushMode == "hold" ? "payload on HOLD (moved " + F(d, 1) + " m). Next: cut (CUT2 control) if not done, then push"
                                    : "PAYLOAD STUCK: moved " + F(d, 1) + " m, payload bat " + PayBat() + ". Next: push; if TryRun false, Run push on Sprue Payload PB (F2 if it moves 0 m)");
        }
    }
}
void StartCharge()
{
    var mine = Blk(S("Battery", "Sprue Battery")) as IMyBatteryBlock;
    if (mine != null) mine.ChargeMode = ChargeMode.Discharge;
    if (_pb != null) _pb.ChargeMode = ChargeMode.Recharge;
    Log("CHARGE start: payload battery " + PayBat() + " MWh"); Go("CHARGE");
}
void Bats(ChargeMode m)
{
    var mine = Blk(S("Battery", "Sprue Battery")) as IMyBatteryBlock;
    if (mine != null) mine.ChargeMode = m;
    if (_pb != null) try { _pb.ChargeMode = m; } catch (Exception) { _pb = null; }
}

// ---------------- payload ----------------
IMyBatteryBlock MyBat() { return Mine(S("Battery", "Sprue Payload Battery")) as IMyBatteryBlock; }
string MyBatTxt() { var b = MyBat(); return b == null ? "none" : F(b.CurrentStoredPower, 4) + "/" + F(b.MaxStoredPower, 4); }
void Payload(string a)
{
    Cfg();                                   // the printer edits Push= in this PB's Custom Data before a cut
    var pr = GridTerminalSystem.GetBlockWithName(S("Printer PB", "Sprue PB"));
    _onPr = pr != null && Same(pr);
    if (_onPr) _seen = true;
    if (_seen && !_onPr && !_split)
    {
        _split = true; _tSp = _t; _p0 = Me.GetPosition();
        Log("SPLIT seen by the payload: my grid " + Id(Me.CubeGrid.EntityId) + (Me.CubeGrid.EntityId == _g0 ? " (still the grid I compiled on)" : " (a new grid)") +
            ", neck " + Map(_neck) + ", bat " + MyBatTxt() + ", push " + S("Push", "hold"));
    }
    if (a == "push") Push();
    else if (a == "hold" || a == "auto") SetOwn(a);
    else if (a.Length > 0) Log("unknown command '" + a + "'; valid: push hold auto");
    if (_split && !_pushed && S("Push", "hold") == "auto" && _t - _tSp >= N("Push Delay", 15)) Push();
    if (_push >= 0 && _t >= _push)
    {
        double th = 0; var l = Thrusters(); foreach (var x in l) th += x.CurrentThrust;
        Thr(0); _push = -1;
        Log("push end, speed " + F(_spd, 2) + " m/s, thrust was " + F(th, 0) + " N" + (th <= 0 ? " (override ignored: no controller? nudge it by jetpack)" : ""));
    }
    if (_runs % 10 == 0) Write();
}
void SetOwn(string v)
{
    var o = new StringBuilder(); bool hit = false;
    foreach (var l in Head(Me.CustomData).TrimEnd().Split('\n')) { if (l.Trim().StartsWith("Push=")) { o.Append("Push=" + v); hit = true; } else o.Append(l); o.Append('\n'); }
    Me.CustomData = (hit ? "" : "Push=" + v + "\n") + o.ToString(); Cfg(); Write(); Log("push mode " + v);
}
void Push()
{
    if (_onPr) { Log("push refused: I am still on the printer grid (it would push the printer)"); return; }
    _pushed = true; Thr(N("Push Percent", 2) / 100); _push = _t + N("Push Seconds", 1);
    Log("PUSH " + N("Push Percent", 2) + "% for " + N("Push Seconds", 1) + "s");
}
List<IMyThrust> Thrusters() { var l = new List<IMyThrust>(); GridTerminalSystem.GetBlocksOfType(l, x => Same(x)); return l; }
void Thr(double f)
{
    var l = Thrusters();
    foreach (var x in l) { x.Enabled = true; x.ThrustOverridePercentage = (float)f; }
    if (l.Count == 0) Log("no thruster on my grid");
}
string Status()
{
    return "hb=" + _runs + " boots=" + _boots + " cd=" + (_cdLost ? "LOST" : "ok") + " grid=" + Id(Me.CubeGrid.EntityId) + " onprinter=" + (_onPr ? 1 : 0) +
        " neck=" + Map(_neck) + " bat=" + MyBatTxt() + " spd=" + F(_spd, 2);
}

// ---------------- screen (lines kept to W characters; the full text is in [Log]) ----------------
void Ln(string s) { _sb.Append(Cut(s, W)).Append('\n'); }
void Show()
{
    _sb.Clear();
    string i1 = " i" + Runtime.CurrentInstructionCount;
    if (_pr)
    {
        var proj = Blk(S("Projector", "Sprue Projector")) as IMyProjector;
        var gr = Grinder(); var ws = Welders();
        Ln("SPRUE c" + _cyc + "/" + _cycles + " " + _st + " " + F(_ts, 0) + "s t" + F(_t, 0) + i1 + (_mov > 5 ? " MOVING" : ""));
        Ln("grid " + Id(Me.CubeGrid.EntityId) + (Me.CubeGrid.EntityId == _g0 ? " (start)" : " (CHANGED)") + " own " + Me.OwnerId);
        string pj = "MISSING"; try { if (proj != null) pj = (proj.IsProjecting ? "on" : "OFF") + " rem " + proj.RemainingBlocks + " bld " + proj.BuildableBlocksCount + " tot " + proj.TotalBlocks; } catch (Exception) { pj = "unreadable"; }
        Ln("proj " + pj);
        var w = new StringBuilder("weld"); foreach (var x in ws) w.Append(x.Enabled ? (x.IsWorking ? " ON" : " on!") : " off");
        Ln(w + "  grind " + (gr == null ? "NONE" : (gr.Enabled ? "ON" : "off") + (gr.IsWorking ? " wrk" : "")));
        string gp = GPos(gr); Ln("grinder" + (gp.Length == 0 ? " pos OK" : gp));
        Ln("mine: neck " + Map(_neck) + " clus " + Map(_clus));
        string pp = "-"; try { if (_pp != null) pp = (Same(_pp) ? "SAME grid" : "other " + Id(_pp.CubeGrid.EntityId)) + " own " + Own(_pp.OwnerId); } catch (Exception) { _pp = null; pp = "ref lost"; }
        Ln("pay PB " + pp);
        string st = _pp == null ? "-" : PayStatus(); Ln(" " + st); if (st.Length > W - 1) Ln(" " + st.Substring(W - 1));
        Ln("pay bat " + PayBat());
        Ln("guards " + Guards());
        Ln("-- RESULTS");
        foreach (var k in new[] { "Q1", "Qa", "Q2", "Q2h", "Q2c", "Q3", "Q3o", "Q3b", "Q4", "Q4b" }) { string v; if (_r.TryGetValue(k, out v)) Ln(v); else if (k == "Q1") Ln("Q1 " + _q1); }
    }
    else if (_pay)
    {
        Ln("SPRUE PAYLOAD boot " + _boots + " hb " + _runs + " t" + F(_t, 0) + i1);
        Ln("grid " + Id(Me.CubeGrid.EntityId) + (Me.CubeGrid.EntityId == _g0 ? " (compile)" : " (NEW)") + " static " + Q(Me.CubeGrid.IsStatic) + " cd " + (_cdLost ? "LOST" : "ok"));
        Ln("printer PB " + (_onPr ? "SAME grid (fused)" : (_seen ? "gone (split)" : "not visible")));
        Ln("owner " + Me.OwnerId);
        var b = MyBat(); Ln("bat " + (b == null ? "none" : F(b.CurrentStoredPower, 4) + "/" + F(b.MaxStoredPower, 4) + " " + b.ChargeMode));
        Ln("mine: neck " + Map(_neck) + " clus " + Map(_clus));
        Ln("spd " + F(_spd, 2) + " m/s moved " + (_split ? F(Vector3D.Distance(Me.GetPosition(), _p0), 1) : "-") + " m");
        Ln("push " + S("Push", "hold") + (_pushed ? " (done)" : (_split && S("Push", "hold") == "auto" ? " in " + F(N("Push Delay", 15) - (_t - _tSp), 0) + "s" : "")));
    }
    else
    {
        Ln("SPRUE PROBE: NO CONFIG (Custom Data empty)"); Ln("name " + Me.CustomName); Ln("boot " + _boots + " hb " + _runs);
    }
    Ln("--");
    for (int i = Math.Max(0, _log.Count - (_pr ? 4 : 6)); i < _log.Count; i++) Ln(_log[i]);
}
