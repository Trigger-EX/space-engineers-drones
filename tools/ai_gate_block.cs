// >>> AI GATE v1.3 (shared block core: identical in every drone script that uses AI-block search, synced by tools/ai_gate_sync.py) >>>
// The AI blocks tagged [AI] (AI Offensive + AI Flight) come out of a blueprint with "AI Behavior" off and every setting at its
// default, so switching them on (Enabled) may do nothing at all. In-game report 2026-10-13: Full drones in GUARD stood still and
// HUNT drones never saw a pirate 700 m away, because the script handed the ship to the AI blocks and stopped its own search.
// The gate:
//  * tries to switch "AI Behavior" on (a bool terminal property whose id contains "Behavior"; the exact id is not documented);
//  * until the AI blocks have shown that they work (flown the ship once: sped it up or turned it), gives them the ship for
//    AI Window seconds, then runs the script's own search (camera sweep + scan rays) for Script Search Window seconds, and so on;
//    under fire the script searches. While the AI flies the ship it keeps it, up to Max AI Hold seconds. Once the AI has flown the
//    ship it is trusted and gets the ship whenever the script wants it, as before v2.1 (sim: time-sharing a working AI cost most of
//    its kills, 1v1 decisive 90 % -> 35 %).
// v1.1 (opt-in, audit F-03/SL-11, default OFF = the behaviour of v1.0): "Use Typed AI Block API=true" sets IMyFlightMovementBlock.SpeedLimit
// ("AI Flight Speed Limit (m/s)") on the AI Flight blocks and counts a block as switched on when IsAutoPilotEnabled reads true instead of
// when the "Behavior" terminal property was found. Rests on the ModAPI documentation only (not tested in the game; unit of SpeedLimit unconfirmed).
// v1.2 (2026-10-14, from the Ballista Helm, unverified in the game): "AI Search Interval (s)" (default 5, 0 = leave the block alone) is written
// to IMyOffensiveCombatBlock.UpdateTargetInterval: the block rescans only every 30 s by default [wiki], longer than the AI Window, so a
// window often saw no search at all. Scripts with an AI Block Search Assist switch call AiGateFind only while it is on (with it off the
// AI blocks stay as they came, "AI Behavior" off: switched on, the AI Flight block may fly the ship away on its own) and re-run FindAi on `ai on|off`.
// v1.3 (2026-10-18, docs/design/flight_split.md): the core keeps only the time-share (AiGate) and its runtime fields. The one-time AI-block setup
// (AiGateFind, AiGateStatus, AiBehaviorSwitch/AiTyped/AiSpeed/AiSearchEvery) is AI GATE SETUP (tools/ags_block.cs), which may run in another PB
// (the Aux, or StaffAI for a Helm); the [AI Gate] section reader AiGateLoad is AI GATE CONFIG (tools/agc_block.cs), or the values come from the Staff
// through CONFIG LINK's AgSet4. AiGateOn/AiWin/ScriptWin/AiHoldMax keep their defaults here; whoever loads the config overwrites them.
// Hooks: AiGateLoad() (AI GATE CONFIG) after FleetLoad(true); AiGateFind(_ai) at the end of FindAi() and AiGateStatus() in the status text (AI GATE SETUP
// hosts); want = AiGate(want) right before "if (want != _aiOn)" in AiStep(). Uses from the script: _rc, _now, UnderFire.
bool AiGateOn = true, _agAi = true, _agLive = false, _agFwdSet = false;
double AiWin = 6, ScriptWin = 8, AiHoldMax = 60, _agT0 = -1, _agMoveT = -99, _agMinV = 0; Vector3D _agFwd;

bool AiGate(bool want)
{
    if (!want || !AiGateOn || _agLive) { _agT0 = -1; return want; }
    var vs = _rc.GetShipVelocities();
    double v = vs.LinearVelocity.Length();
    if (_agT0 < 0) { _agT0 = _now; _agAi = !UnderFire; _agMoveT = -99; _agMinV = v; _agFwdSet = false; }
    double age = _now - _agT0;
    if (_agAi)
    {
        // The AI flies if it speeds the ship up again (the dampeners alone only slow it), keeps turning it, or has turned it more than
        // 25 deg since the hand-over (it turns fast onto what it found, then may only brake; the script's own sweep coasts on for
        // less than that). Speed and turn rate are ignored for the first 1.5 / 2.5 s while the ship coasts from the script's control.
        if (!_agFwdSet) { _agFwd = _rc.WorldMatrix.Forward; _agFwdSet = true; }
        if (age < 1.0) _agMinV = v;
        else
        {
            _agMinV = Math.Min(_agMinV, v);
            if ((age > 1.5 && v > _agMinV + 3) || (age > 2.5 && vs.AngularVelocity.LengthSquared() > 0.0144) || Vector3D.Dot(_rc.WorldMatrix.Forward, _agFwd) < 0.906)
            { _agMoveT = _now; _agLive = true; }
        }
        bool busy = _now - _agMoveT < 4 && age < AiHoldMax;
        if (!busy && (age >= AiWin || (UnderFire && age >= 1.5))) { _agAi = false; _agT0 = _now; }
    }
    else if (age >= ScriptWin) { _agAi = true; _agT0 = _now; _agMoveT = -99; _agMinV = v; _agFwdSet = false; }
    return _agAi;
}
// <<< AI GATE <<<
