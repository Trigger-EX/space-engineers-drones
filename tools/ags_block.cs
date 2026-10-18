// >>> AI GATE SETUP v1.0 (sub-block of AI GATE v1.3: one-time AI-block setup; tools/ags_block.cs, synced by tools/blocks_sync.py) >>>
// Switches "AI Behavior" on, sets IMyFlightMovementBlock.SpeedLimit (typed API, opt-in) and IMyOffensiveCombatBlock.UpdateTargetInterval on the [AI]
// blocks. Off the hot path (terminal property writes that persist in the world), so it may run in another PB than the AI GATE core: final hosts AuxAI
// (drones), StaffAI (Helm ships), GnatStaffAI (variant G2), WaspAI and TubeRamAI (standalone, with the core). The Aux sets the four fields from the
// SDK cfg line "agSwitch|agTyped|agSpeed|agEvery" with its own host code (AgSet(string) in AuxAI.cs) and calls AiGateFind when SDK bit 9 AI ASSIST rises,
// when those values change, and on its 600-tick block rescan while bit 9 is set (flight_split.md 6.4).
// Hooks: AiGateFind(list of [AI] blocks) after the AI blocks are found; AiGateStatus() in the status text.
// Uses from the script: _agLive (declared by the AI GATE core; a host without the core declares "bool _agLive;" itself, false = no "AI ok" suffix).
bool AiBehaviorSwitch = true, AiTyped = false; double AiSpeed = 100; int AiSearchEvery = 5, _agFound = -1;   // _agFound: -1 not tried, else the number of AI blocks whose behaviour switch was found
List<ITerminalProperty> _agProps = new List<ITerminalProperty>();

void AiGateFind(List<IMyFunctionalBlock> ai)
{
    if (AiSearchEvery > 0) foreach (var b in ai) { var oc = b as IMyOffensiveCombatBlock; if (oc != null) oc.UpdateTargetInterval = AiSearchEvery; }
    if (!AiBehaviorSwitch || ai.Count == 0) return;
    int n = 0;
    foreach (var b in ai)
    {
        var fm = AiTyped ? b as IMyFlightMovementBlock : null;
        if (fm != null) fm.SpeedLimit = (float)AiSpeed;
        _agProps.Clear(); b.GetProperties(_agProps);
        ITerminalProperty<bool> sw = null;
        foreach (var p in _agProps)
        {
            string id = p.Id.ToLowerInvariant();
            if (!id.Contains("behavio")) continue;
            var bp = p.As<bool>();
            if (bp != null && (sw == null || id.Contains("activ"))) sw = bp;
        }
        if (sw != null && !sw.GetValue(b)) sw.SetValue(b, true);
        if (fm != null ? fm.IsAutoPilotEnabled : sw != null) n++;
    }
    _agFound = n;
}

string AiGateStatus()
{
    if (_agFound < 0) return "";
    if (_agFound == 0 && AiBehaviorSwitch) return "AI Behavior switch not found:\n turn AI Behavior on by hand\n";
    return (AiBehaviorSwitch ? "AI Behavior on (" + _agFound + " blocks)" : "") + (_agLive ? " AI ok\n" : AiBehaviorSwitch ? "\n" : "");
}
// <<< AI GATE SETUP v1.0 <<<
