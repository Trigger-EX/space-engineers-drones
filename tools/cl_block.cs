// >>> CONFIG LINK v1.0 (Flight / Helm side of the config move, docs/design/flight_split.md 3, 4.1, 4.3, 6.1; tools/cl_block.cs, synced by tools/blocks_sync.py) >>>
// Final hosts: the 9 drone Flights and ShrikeAI. The PB never writes its own Custom Data any more: the Staff's CONFIG SERVER (tools/cs_block.cs) writes
// every default there, parses the values and sends them on .CFG; this block applies them and caches the string in Storage.
// Channel (TransmissionDistance.CurrentConstruct): listener CfKey() + ".CFG" (drones CfKey() = _lkK = "WSPL"+<Flight PB EntityId>; Helm "<Ship Tag>.").
//   .CFG  Staff -> Flight/Helm, string (exact format, the CONFIG SERVER builds the same):
//         line 0  "CF1\t<schema>\t<status>"      status: "ok", or (Longbow, Mantis) the weapon the Staff detected; kept in _cfX, shown after "cfg: Staff, "
//         line i  "<letter>\t<v1>\t<v2>...\n"    one line per section, values in tools/cfg_tables.json order (empty lines are skipped)
//         letters: M the model section (cfg_tables "sections" of the model, WITHOUT the "local" entries IGC Tag / Start Mode), K [Kamikaze Defence]
//         (all 20), A [AI Gate] (only the 4 "block":"core" entries: AiGateOn, AiWin, ScriptWin, AiHoldMax; the 4 setup values go to the Aux in SDK),
//         S [Survival] (all 8). Drone line order M K A S. Helm (4.3): one line per Helm section with its own letter (CfgIn switches on _cfV[0]), then K A.
//         Values: bools "1"/"0" (anything else reads false), doubles and ints ToString("R") (read with double.TryParse, current culture on both PBs;
//         ints are read as doubles and cast by the post-op), strings raw (no tab / newline). Values are sent RAW (as MyIni reads them); the post-ops
//         of cfg_tables.json ((int), Math.Max clamps, .Trim().ToUpperInvariant()) are applied here, in CfgIn / AgSet4 (all are idempotent).
//         schema = cfg_tables.json "version" + "/" + the value count of each line, in line order; drone example (Mantis) "1/84/20/4/8".
//         The Flight accepts a message only if the header schema equals its const CfSchema AND the schema rebuilt from the lines it got equals it too;
//         otherwise nothing is applied and the screen says "! config schema: update Flight and Staff together".
//   WANT: CfWant rides on drone F2S flags bit 16 (SPLIT LINK v1.2: F2S goes out at least every 12 ticks); Helm H2S bit 9 (HELMLINK 1.3). The Staff answers
//         a WANT within one F2S period, at most once per 30 ticks per key, re-reading the Flight's Custom Data at that moment ("reload" reaches both PBs).
//   AI ASSIST (runtime AiAssist) rides beside it: F2S bit 17 / H2S bit 10; the Aux (SDK bit 9) or StaffAI does the AI-block setup.
// Contract: value readers N() (double), B() (bool), T() (string) read the next value of the line being applied (_cfV, cursor _cfI, _cfV[0] = letter).
//   bool CfApply(string): schema check, then applies atomically line by line: K -> KzSet(), A -> AgSet4(), S -> SvSet() (in SURVIVAL LINK v1.1),
//   any other letter -> host CfgIn(). Returns false (nothing applied) on a mismatch. CfRead(): applies each .CFG, then clears CfWant, keeps the string
//   (CfSave) and sets the screen line _cfSt. string CfLoad(): applies the Storage cache at start (status "cfg: cached") and returns the host's own part
//   of Storage. string CfSave(): the suffix the host appends to its Storage in Save() ("\u0001" + the last applied .CFG). bool CfWant: true from start
//   and "reload" until a matching .CFG is applied.
// Host hooks: string CfKey(); const string CfSchema; void CfgIn() (model line(s), one statement per field in cfg_tables.json order with the post-ops,
//   generated from cfg_tables.json); a Helm without SURVIVAL LINK declares "void SvSet() { }". Calls: CfInit() after the link key exists (drones: after
//   LkInit()), "string st = CfLoad();" before the host parses Storage, CfRead() in the tick, "+ CfSave()" in Save(), "CfWant = true;" in "reload",
//   "\n" + _cfSt on the screen. Tag and Start Mode stay local (Get-only reads; the IGC channels are registered in the constructor).
// Sync: until a matching .CFG arrives the PB runs on its Storage cache, else on its field defaults ("cfg: defaults (no Staff)"); after an apply
//   "cfg: Staff". Runtime words (aim, breach, wp, range, split/focus, ai on|off) last until the next start or "reload"; the Staff never pushes config
//   unasked. Flight rebuilt: new key + WANT. Staff rebuilt: answers the next WANT. Run gap: nothing (the config is in memory).
IMyBroadcastListener _cfL; bool CfWant = true; string _cfS = "", _cfX = "", _cfSt = "cfg: defaults (no Staff)"; string[] _cfV; int _cfI;
double N() { double v; double.TryParse(_cfV[_cfI++], out v); return v; }
bool B() { return _cfV[_cfI++] == "1"; }
string T() { return _cfV[_cfI++]; }
void CfInit() { _cfL = IGC.RegisterBroadcastListener(CfKey() + ".CFG"); }
bool CfApply(string s)
{
    var L = s.Split('\n'); var h = L[0].Split('\t'); string c = "1";
    for (int i = 1; i < L.Length; i++) if (L[i] != "") c += "/" + (L[i].Split('\t').Length - 1);
    if (h.Length < 3 || h[0] != "CF1" || h[1] != CfSchema || c != CfSchema) return false;
    _cfX = h[2];
    for (int i = 1; i < L.Length; i++)
    {
        if (L[i] == "") continue;
        _cfV = L[i].Split('\t'); _cfI = 1;
        char k = _cfV[0][0];
        if (k == 'K') KzSet(); else if (k == 'A') AgSet4(); else if (k == 'S') SvSet(); else CfgIn();
    }
    return true;
}
void CfRead()
{
    while (_cfL.HasPendingMessage)
    {
        var m = _cfL.AcceptMessage(); string s = m.Data as string;
        if (s == null) continue;
        if (!CfApply(s)) { _cfSt = "! config schema: update Flight and Staff together"; continue; }
        CfWant = false; _cfS = s; _cfSt = "cfg: Staff" + (_cfX != "ok" && _cfX != "" ? ", " + _cfX : "");
    }
}
string CfLoad()
{
    var p = Storage.Split('\u0001');
    if (p.Length > 1 && CfApply(p[1])) { _cfS = p[1]; _cfSt = "cfg: cached"; }
    return p[0];
}
string CfSave() { return _cfS != "" ? "\u0001" + _cfS : ""; }
void KzSet()
{
    KzOn = B(); KzMaxRadius = N(); KzHorizon = N(); KzMiss = N(); KzMissPerKm = N(); KzMinClosing = N(); KzPriority = N(); KzMates = B(); KzBack = B();
    KzBackSpeed = N(); KzBackRange = N(); KzReact = N(); KzBreak = B(); KzBreakTime = N(); KzBreakSpeed = N(); KzBreakMiss = N(); KzRays = B();
    KzPack = N(); KzReserve = N(); KzShareOn = B();
}
void AgSet4() { AiGateOn = B(); AiWin = Math.Max(1, N()); ScriptWin = Math.Max(1, N()); AiHoldMax = Math.Max(AiWin, N()); }
// <<< CONFIG LINK v1.0 <<<
