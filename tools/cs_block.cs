// >>> CONFIG SERVER v1.0 (Staff side of the config move, docs/design/flight_split.md sections 3, 4.1, 6.1; tools/cs_block.cs, synced by tools/blocks_sync.py) >>>
// Final hosts: the 9 drone Staffs and StaffAI (Helm ships). The only writer of the target PB's Custom Data (the Flight / Helm PB): a table-driven MyIni
// default writer and reader. On a WANT it re-parses the target's Custom Data, writes every missing default, reads every value, sends .CFG, and keeps
// the four AI GATE SETUP values in CfAg. Tables: [Kamikaze Defence], [AI Gate] and [Survival] are here (cfg_tables.json "shared", in its order); the
// model section comes from the host (CfModel(), section name FS).
// Channel (TransmissionDistance.CurrentConstruct):
//   .CFG  Staff -> Flight/Helm   string "CF1\t<schema>\t<status>\n" + one line per section, in this order: "M" model, then the host's CfExtra() lines
//         (Helm: its [Manual] / [Battery] lines), then one line per letter of the host's CfSh: "K" [Kamikaze Defence], "A" [AI Gate] (the 4 core values
//         only: Time-share, AI Window, Script Search Window, Max AI Hold), "S" [Survival]. A line = its letter, then "\t" + value for every sent key, in
//         table order, then "\n". Not sent: the model's local keys (IGC Tag, Start Mode ...: the Flight reads them itself) and the 4 AI GATE SETUP values.
//         Values are raw, the receiver applies the post-ops of cfg_tables.json ((int), Math.Max, Trim().ToUpperInvariant()): bools 1/0, d and i
//         ToDouble().ToString("R"), strings as written (MyIni single-line values; a tab in one would break the line).
//         schema = "1" (cfg_tables.json version) + "/" + the number of values of each line, in line order. Drones: "1/<model count without the
//         locals>/20/4/8", e.g. Mantis "1/84/20/4/8", Gnat "1/59/20/4/8".
//         status = "ok", or what CfExtra() set (Longbow / Mantis: the detected weapon name, e.g. "Small Railgun", "Gatling/Autocannon", "custom").
//         Sent when the host's WANT is true (drones: F2S bit 16 CFG WANT on a fresh F2S; Helm: H2S bit 9), at most once per 30 ticks per key. The Staff
//         never sends unasked; a target whose Custom Data does not parse gets nothing (_cfE says so on the Staff screen).
//   CfAg = "agSwitch|agTyped|agSpeed|agEvery" (same value format, raw): the [AI Gate] setup values. Drone Staffs pass it to the Aux in the SDK cfg
//   (AUX LINK v1.1); GnatStaffAI (G2) and StaffAI give it to their own AI GATE SETUP. It is filled on every parse: on a WANT and once on every new key
//   (a Staff that starts next to a running Flight knows the values without a WANT; that parse writes missing defaults but sends nothing).
// Host hooks (define CfModel() above this block: the tests read the first "CfModel()" in the file):
//   string[] CfModel()          flat key / default / type triples of the model section, cfg_tables.json order and types (d double, i int, b bool, s string)
//   IMyProgrammableBlock CfTarget()   the Flight / Helm PB whose Custom Data is served (null: nothing)
//   string CfKey()              the target's link key; the channel is CfKey() + ".CFG" (drones "WSPL"+<Flight PB EntityId> = _fk); "" = no target
//   string CfExtra(MyIni i)     after the model values are read, before the lines are built: may change model values in _cfV (index = triple number
//                               in _cfT; Longbow and Mantis: DetectWeapon, host helpers CfGet / CfPut), set _cfSt, and return extra lines built with
//                               CfLine (Helm sections); "" otherwise
//   const string CfSh           the shared lines, in order ("KAS" drones)
// Call: CfStep(bool want) once a tick (drones: after ReadF2S, want = (_fl & 65536) != 0 && _tick - _f2sTick < 30). _cfE on the screen ("" or the
// target's parse error line).
// Uses: _tick, FS, Me / IGC. Sync: answers every WANT (Staff rebuilt: nothing to do); a new key (Flight rebuilt: the Staff re-keys on its 1 Hz rescan)
// resets the 30-tick gate.
string _cfSt = "", CfAg = "", _cfK = "", _cfSc = "", _cfE = ""; string[] _cfT, _cfV; long _cfTk = -99;
readonly string[] CfKz = {
    "Enabled", "true", "b", "Max Kamikaze Radius (m)", "3.5", "d", "Warning Time (s)", "12", "d", "Miss Distance (m)", "40", "d",
    "Miss Distance Per km Of Range (m)", "60", "d", "Min Closing Speed (m/s)", "15", "d", "Priority Bonus", "2500", "d", "Protect Hive Mates", "true", "b",
    "Back Away", "true", "b", "Back Away Speed (m/s)", "60", "d", "Back Away Within (m)", "800", "d", "Back Away Reaction Time (s)", "10", "d",
    "Break Sideways", "true", "b", "Break Time (s)", "2", "d", "Break Speed (m/s)", "40", "d", "Break Miss Distance (m)", "20", "d",
    "Track And Pack Search Rays", "true", "b", "Pack Search Radius (m)", "60", "d", "Camera Charge Reserve (m)", "600", "d", "Share Threats With Hive", "true", "b"
};
readonly string[] CfAi = {
    "Time-share With Script Search", "true", "b", "Switch On AI Behavior", "true", "B", "Use Typed AI Block API", "false", "B", "AI Flight Speed Limit (m/s)", "100", "D",
    "AI Window (s)", "6", "d", "Script Search Window (s)", "8", "d", "Max AI Hold (s)", "60", "d", "AI Search Interval (s)", "5", "I"
};
readonly string[] CfSv = {
    "Lead-Breaking Jinks", "true", "b", "Power Guard", "true", "b", "Jink Accel Fraction", "0.8", "d", "Enemy Projectile Speed (m/s)", "800", "d",
    "Jink Period Min (s)", "0.4", "d", "Jink Period Max (s)", "3", "d", "Jink Period Random", "0.3", "d", "Power Guard Hold (s)", "2", "d"
};

void CfStep(bool want)
{
    string k = CfKey();
    if (k != _cfK) { _cfK = k; _cfTk = -99; if (k != "") CfSend(false); }
    if (want && k != "" && _tick - _cfTk >= 30) CfSend(true);
}
void CfSend(bool tx)
{
    var p = CfTarget(); if (p == null) return;
    if (tx) _cfTk = _tick;
    var i = new MyIni(); MyIniParseResult r;
    if (!i.TryParse(p.CustomData, out r)) { _cfE = "! Flight Custom Data: error line " + r.LineNo + "\n"; return; }
    _cfE = ""; _cfSt = "ok"; _cfSc = "1"; CfAg = "";
    _cfT = CfModel(); _cfV = CfRd(i, FS, _cfT);
    string x = CfExtra(i), sc = _cfSc; _cfSc = "1";   // CfExtra may change model values first, but its line counts go after M in the schema
    string s = "M" + CfJ(_cfT, _cfV) + "\n" + x; _cfSc += sc.Substring(1);
    foreach (char c in CfSh)
    {
        var t = c == 'K' ? CfKz : c == 'A' ? CfAi : CfSv;
        s += CfLine(i, c == 'K' ? "Kamikaze Defence" : c == 'A' ? "AI Gate" : "Survival", t, c.ToString());
    }
    string o = i.ToString(); if (o != p.CustomData) p.CustomData = o;
    if (tx) IGC.SendBroadcastMessage(_cfK + ".CFG", "CF1\t" + _cfSc + "\t" + _cfSt + "\n" + s, TransmissionDistance.CurrentConstruct);
}
// one section: write the missing defaults, read every value (raw, .CFG format)
string[] CfRd(MyIni i, string s, string[] t)
{
    var v = new string[t.Length / 3];
    for (int n = 0; n < v.Length; n++)
    {
        string k = t[3 * n], d = t[3 * n + 1]; char y = char.ToLower(t[3 * n + 2][0]);
        if (!i.ContainsKey(s, k)) i.Set(s, k, d);
        var g = i.Get(s, k);
        v[n] = y == 'b' ? (g.ToBoolean(d == "true") ? "1" : "0") : y == 's' ? g.ToString(d) : g.ToDouble(double.Parse(d)).ToString("R");
    }
    return v;
}
// the sent values of a section ("\t" + value each); locals are skipped, upper-case types (AI GATE SETUP) go to CfAg; the count goes into the schema
string CfJ(string[] t, string[] v)
{
    string b = ""; int c = 0;
    for (int n = 0; n < v.Length; n++)
    {
        string k = t[3 * n];
        if (k == "IGC Tag" || k.StartsWith("Start Mode")) continue;
        if (char.IsUpper(t[3 * n + 2][0])) CfAg += (CfAg == "" ? "" : "|") + v[n];
        else { b += "\t" + v[n]; c++; }
    }
    _cfSc += "/" + c; return b;
}
string CfLine(MyIni i, string s, string[] t, string l) { return l + CfJ(t, CfRd(i, s, t)) + "\n"; }
// <<< CONFIG SERVER v1.0 <<<
