// >>> AI GATE CONFIG v1.0 (sub-block of AI GATE v1.3: the [AI Gate] section, read locally; tools/agc_block.cs, synced by tools/blocks_sync.py) >>>
// Hosts that read their own Custom Data (WaspAI, TubeRamAI). Writes the 8 defaults on first run ("reload" re-reads them) into the AI GATE core fields
// (AiGateOn, AiWin, ScriptWin, AiHoldMax) and the AI GATE SETUP fields (AiBehaviorSwitch, AiTyped, AiSpeed, AiSearchEvery): a host needs both blocks.
// A split Flight / Helm gets these values from its Staff (CONFIG LINK AgSet4 for the core four; the setup four go to the Aux in the SDK cfg).
// Hook: AiGateLoad() after FleetLoad(true).
void AiGateLoad()
{
    var ini = new MyIni(); MyIniParseResult res;
    if (!ini.TryParse(Me.CustomData, out res)) return;
    const string S = "AI Gate";
    if (!ini.ContainsKey(S, "Time-share With Script Search")) ini.Set(S, "Time-share With Script Search", AiGateOn);
    if (!ini.ContainsKey(S, "Switch On AI Behavior")) ini.Set(S, "Switch On AI Behavior", AiBehaviorSwitch);
    if (!ini.ContainsKey(S, "Use Typed AI Block API")) ini.Set(S, "Use Typed AI Block API", AiTyped);
    if (!ini.ContainsKey(S, "AI Flight Speed Limit (m/s)")) ini.Set(S, "AI Flight Speed Limit (m/s)", AiSpeed);
    if (!ini.ContainsKey(S, "AI Window (s)")) ini.Set(S, "AI Window (s)", AiWin);
    if (!ini.ContainsKey(S, "Script Search Window (s)")) ini.Set(S, "Script Search Window (s)", ScriptWin);
    if (!ini.ContainsKey(S, "Max AI Hold (s)")) ini.Set(S, "Max AI Hold (s)", AiHoldMax);
    if (!ini.ContainsKey(S, "AI Search Interval (s)")) ini.Set(S, "AI Search Interval (s)", AiSearchEvery);
    string o = ini.ToString(); if (o != Me.CustomData) Me.CustomData = o;
    AiGateOn = ini.Get(S, "Time-share With Script Search").ToBoolean(AiGateOn);
    AiBehaviorSwitch = ini.Get(S, "Switch On AI Behavior").ToBoolean(AiBehaviorSwitch);
    AiTyped = ini.Get(S, "Use Typed AI Block API").ToBoolean(AiTyped);
    AiSpeed = Math.Max(1, ini.Get(S, "AI Flight Speed Limit (m/s)").ToDouble(AiSpeed));
    AiWin = Math.Max(1, ini.Get(S, "AI Window (s)").ToDouble(AiWin));
    ScriptWin = Math.Max(1, ini.Get(S, "Script Search Window (s)").ToDouble(ScriptWin));
    AiHoldMax = Math.Max(AiWin, ini.Get(S, "Max AI Hold (s)").ToDouble(AiHoldMax));
    AiSearchEvery = Math.Max(0, ini.Get(S, "AI Search Interval (s)").ToInt32(AiSearchEvery));
}
// <<< AI GATE CONFIG v1.0 <<<
