#!/usr/bin/env python3
"""One-off generator: the Staff half of a two-PB drone is WaspStaffAI.cs with the drone's name, IGC tag and Custom Data section substituted
(FLEET LINK / FOLLOW / LAUNCH are shared blocks kept identical by tools/fleet_sync.py, follow_sync.py, launch_sync.py).
Usage: python3 -I tools/make_staff.py AegisAI Aegis AEGIS Ammo   ->  scripts/AegisStaffAI.cs   (script, display name, IGC tag, config section)
A name in OPTS gets per-drone options: nolaunch (no LAUNCH block: a ship with no merge block would take off on a fleet "launch" / "join"), rockets (keep the "Rockets" label:
the Flight sends a rocket count, not an ammo percent), speed / sep (the drone's Combat Speed / Wingmate Separation Range defaults)."""
import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OPTS = {"Basilisk": {"nolaunch": True, "rockets": True, "speed": 60, "sep": 160}}

def gnat():
    """scripts/GnatStaffAI.cs from WaspStaffAI.cs for the point-and-burn Gnat (docs/design/gnat_split.md): no FOLLOW block, no hive-mates / sensors, its own Mode enum, the fleet home as an
    EVENT (FOL bit 3 once per fix: the Flight estimates the carrier's velocity from successive fixes) and the fleet channel's carrier radius relayed on .HMR."""
    src = open(os.path.join(ROOT, "scripts", "WaspStaffAI.cs"), encoding="utf-8", newline="").read()
    crlf = "\r\n" in src; s = src.replace("\r\n", "\n")
    def rep(a, b, n=1):
        nonlocal s
        assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)
    def drop(tag):
        nonlocal s
        i = s.index("// >>> " + tag); j = s.index("\n", s.index("// <<< " + tag, i)) + 1; s = s[:i] + s[j:]
    def cut(a, b, keep_b=True):
        nonlocal s
        i = s.index(a); j = s.index(b, i); s = s[:i] + s[j:]
    # header
    i = s.index("//  WASP STAFF AI"); j = s.index("// =====", i)
    s = s[:i] + '''//  GNAT STAFF AI  v1.0-gnat-split  - the STAFF half of the two-PB Gnat ("[Staff] ..." Programmable Block, next to "[Flight] ..." = GnatSwarmAI.cs)
//  For Space Engineers (vanilla, small grid). Paste into a second Programmable Block on the SAME grid as the Flight PB, named "[Staff] ...".
//  Everything the Gnat does that is not the hot path (docs/design/gnat_split.md):
//   * FLEET LINK   fleet channel (targets in, target shares out, home, carrier radius, commands) - same [Fleet] section as the single-PB Gnat
//   * LAUNCH       take-off after the release from a printer - same [Launch] section; Staff flies the burn, Flight lets go of the ship
//   * DISPLAY      mode, target, aim error, battery, alarm, launch and link state
//   * WATCHDOG     the Flight PB silent for 1.5 s (or switched off / destroyed): gyro and thruster overrides released once, dampeners on, "FLIGHT PB DOWN"
//  Link (TransmissionDistance.CurrentConstruct, key "WSPL" + the Flight PB's EntityId; both PBs run Update1) - see the SPLIT LINK block of GnatSwarmAI.cs.
//  Gnat only: ".HMR" (Staff -> Flight, double) is the carrier radius from "<fleet channel>_HOMER"; FOL bit 3 is sent ONCE per new fleet home fix (the Flight derives the carrier's
//  velocity from successive fixes, so a repeated point would read as a stopped carrier).
//  The Staff finds the nearest "[Flight]" PB of its construct (re-checked every 10 s and when the grid changes).
//  CUSTOM DATA (this PB): [Launch], [Fleet] (written on first run). IGC Tag and Start Mode are read from the Flight PB's Custom Data ([Gnat] section), so nothing is configured twice.
//  Commands (PB argument, on EITHER PB): the Staff's words (launch, armed, disarm, reload) are handled here; the Staff forwards every word to the Flight PB and the Flight PB forwards its argument to the Staff.
''' + s[j:]
    rep('const string VER = "1.0-wasp-split";', 'const string VER = "1.0-gnat-split";')
    rep('string Tag = "WASP", StartMode = "IDLE";\ndouble MaxSpeed = 98, SepRange = 70;', 'string Tag = "GNAT", StartMode = "IDLE";')
    rep('// ---------------- settings (read from the Flight PB\'s Custom Data) -----', '// ---------------- settings (read from the Flight PB\'s Custom Data) -----')
    rep("enum Mode { IDLE, GUARD, HUNT, ASSIST, RTB, FOLLOW }", "enum Mode { IDLE, FORM, ATTACK, RTB }")
    rep("List<IMySensorBlock> _sensors = new List<IMySensorBlock>(); List<IMyFunctionalBlock> _ai", "List<IMyFunctionalBlock> _ai")
    rep("List<MyDetectedEntityInfo> _sensed = new List<MyDetectedEntityInfo>();\n", "")
    rep("IMyBroadcastListener _lHome, _lCmd, _lSwarm, _lF2S, _lFlt, _lArg;", "IMyBroadcastListener _lHome, _lCmd, _lF2S, _lFlt, _lArg, _lHmr; string _hmrKey = null;")
    rep("bool _down, _folBurn, _folFol; int _errRun;", "bool _down, _folBurn, _fhNew, _hmrNew; int _errRun; Vector3D _fhP; double _hmrV, _hmrT = -99; List<long> _gone = new List<long>();")
    drop("FOLLOW")
    i = s.index("// =====================================================================\n//  HIVE MATES (lite)"); j = s.index("// =====================================================================\n//  SETUP")
    s = s[:i] + s[j:]
    rep('    try { string m = StartMode.Trim().ToUpperInvariant(); if (m == "SATELLITE" || m == "SAT" || m == "ORBIT") FoSat = true; } catch (Exception ex) { CtorErr("mode", ex); }\n', "")
    rep("// the Flight PB's [Wasp] keys that the Staff shares (read on load, every 10 s and on \"reload\": no duplicated keys, no tag mismatch)", "// the Flight PB's [Gnat] keys that the Staff shares (read on load, every 10 s and on \"reload\": no duplicated keys, no tag mismatch)")
    rep('    Tag = ini.Get("Wasp", "IGC Tag").ToString(Tag);\n    StartMode = ini.Get("Wasp", "Start Mode (GUARD/HUNT/ASSIST/IDLE)").ToString(StartMode);\n    MaxSpeed = ini.Get("Wasp", "Combat Speed").ToDouble(MaxSpeed);\n    SepRange = ini.Get("Wasp", "Wingmate Separation Range").ToDouble(SepRange);\n',
        '    Tag = ini.Get("Gnat", "IGC Tag").ToString(Tag);\n    StartMode = ini.Get("Gnat", "Start Mode (FORM/ATTACK/IDLE)").ToString(StartMode);\n')
    rep("    GridTerminalSystem.GetBlocksOfType(_sensors, b => b.IsSameConstructAs(Me));\n", "")
    rep("void LoadMods() { FleetLoad(true); LaunchLoad(); FollowLoad(); }", "void LoadMods() { FleetLoad(true); LaunchLoad(); }")
    rep("foreach (var l in new[] { _lHome, _lCmd, _lSwarm, _lF2S, _lFlt, _lArg })", "foreach (var l in new[] { _lHome, _lCmd, _lF2S, _lFlt, _lArg })")
    rep(' _lSwarm = IGC.RegisterBroadcastListener(Tag + "_SWARM");', "")
    rep("    ReadIGC();\n    FollowStep();\n    if (_tick % 10 == 5) PollSensors();\n", "    ReadIGC();\n")
    rep("    SendFol(burn);\n", "    SendFol(burn);\n    SendHmr();\n")
    rep("_mode = (Mode)Math.Min(d.Item1 & 7, 5);", "_mode = (Mode)Math.Min(d.Item1 & 7, 3);")
    rep("    FleetPump(Cb, p => { _dynHome = p; _dynHomeT = _now; });\n    ReadSwarm();\n",
        "    FleetPump(Cb, p => { _dynHome = p; _dynHomeT = _now; _fhP = p; _fhNew = true; });      // a fleet-channel home: the Flight is told once per fix (FOL bit 3)\n"
        "    if (_hmrKey != _flChan)\n    {\n        if (_lHmr != null) IGC.DisableBroadcastListener(_lHmr);\n        _lHmr = _flChan != \"\" ? IGC.RegisterBroadcastListener(_flChan + \"_HOMER\") : null; _hmrKey = _flChan;\n    }\n"
        "    while (_lHmr != null && _lHmr.HasPendingMessage) { var m = _lHmr.AcceptMessage(); if (m.Data is double) { _hmrV = (double)m.Data; _hmrT = _now; _hmrNew = true; } }\n")
    # SendFol: the burn bit and the fleet-home event, no owner / want data
    i = s.index("// the Flight PB's follow and take-off data"); j = s.index("// Flight PB dead (silent for 90 ticks")
    s = s[:i] + '''// the Flight PB's take-off and fleet-home data: every 30 ticks, 2 ticks apart during the burn, and at once on a burn change or a new fleet home fix (no FOLLOW: want / owner are zero)
void SendFol(bool burn)
{
    if (_fk == "") return;
    if (burn == _folBurn && !_fhNew && _tick % (burn ? 2 : 30) != 1) return;
    _folBurn = burn;
    int f = (burn ? 4 : 0) | (_fhNew ? 8 : 0);
    _fhNew = false;
    IGC.SendBroadcastMessage(_fk + ".FOL", new MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>(Vector3D.Zero, Vector3D.Zero, Vector3D.Zero, _fhP, new Vector3D(800, 1200, 0), f), TransmissionDistance.CurrentConstruct);
}

// the fleet channel's carrier radius -> Flight (.HMR): on arrival and once a second while it is fresh
void SendHmr()
{
    if (_fk == "" || _now - _hmrT > 5 || !(_hmrNew || _tick % 60 == 3)) return;
    _hmrNew = false;
    IGC.SendBroadcastMessage(_fk + ".HMR", _hmrV, TransmissionDistance.CurrentConstruct);
}

''' + s[j:]
    rep('        case "follow": case "escort": FoSetSat(false); break;\n        case "satellite": case "sat": case "orbit": FoSetSat(true); break;\n', "")
    rep('    _sb.Append("WASP STAFF v")', '    _sb.Append("GNAT STAFF v")')
    rep("    _sb.Append(FollowStatus());\n", "")
    rep('''    _sb.Append("Battery: ").Append((_batt * 100).ToString("0")).Append("%  Rockets ").Append(rk == 255 ? "?" : rk.ToString()).Append('\\n');''', '''    _sb.Append("Battery: ").Append((_batt * 100).ToString("0")).Append("%\\n");''')
    rep("    int rk = (_fl >> 8) & 255;\n", "")
    rep('''    _sb.Append("Hive   : ").Append(_mates.Count + 1).Append((_fl & 16) != 0 ? "  !! UNDER FIRE" : "")''', '''    _sb.Append("Alarm  : ").Append((_fl & 16) != 0 ? "!! UNDER FIRE" : "-")''')
    # references that must be gone
    for bad in ("Follow", "FoSat", "FoSetSat", "_mates", "ReadSwarm", "_sensors", "MaxSpeed", "SepRange", "_lSwarm", "Mode.FOLLOW", "Mode.GUARD", "Mode.HUNT"):
        code = "\\n".join(l for l in s.split("\\n") if not l.lstrip().startswith("//"))
        assert bad not in code, bad
    out = os.path.join(ROOT, "scripts", "GnatStaffAI.cs")
    open(out, "w", encoding="utf-8", newline="").write(s.replace("\n", "\r\n") if crlf else s)
    print("wrote", out)

def main(script, name, tag, section, leash=""):
    o = OPTS.get(name, {})
    src = open(os.path.join(ROOT, "scripts", "WaspStaffAI.cs"), encoding="utf-8", newline="").read()
    crlf = "\r\n" in src; s = src.replace("\r\n", "\n")
    low = name.lower(); up = name.upper()
    for a, b in (("WASP STAFF AI  v1.0-wasp-split", f"{up} STAFF AI  v1.0-{low}-split"), ("two-PB Wasp", f"two-PB {name}"), ("WaspFlightAI.cs", f"{script}.cs (the Flight half)"),
                 ("WaspAI.cs", f"the {name} script"), ('"1.0-wasp-split"', f'"1.0-{low}-split"'), ('string Tag = "WASP"', f'string Tag = "{tag}"'),
                 ('[Wasp] section', f'[{section}] section'), ("[Wasp] keys", f"[{section}] keys"), ('ini.Get("Wasp",', f'ini.Get("{section}",'),
                 ('"WASP STAFF v"', f'"{up} STAFF v"'), ("the Wasp", f"the {name}")) + (() if o.get("rockets") else (("%  Rockets ", "%  Ammo "),)):
        s = s.replace(a, b)
    if leash:
        a = "void LoadMods() { FleetLoad(true); LaunchLoad(); FollowLoad(); }"
        assert a in s
        s = s.replace(a, "void LoadMods() { FleetLoad(true); LaunchLoad(); FollowLoad(); FoLeash = Math.Max(FoLeash, " + leash + "); }   // (a sniper stands far out: the owner leash is at least this long)")
    if "speed" in o:
        a = "double MaxSpeed = 98, SepRange = 70;"; assert a in s
        s = s.replace(a, f"double MaxSpeed = {o['speed']}, SepRange = {o['sep']};")
    if o.get("nolaunch"):
        def rep(a, b, n=1):
            nonlocal s
            assert s.count(a) == n, (a, s.count(a)); s = s.replace(a, b)
        i = s.index("// >>> LAUNCH"); j = s.index("\n", s.index("// <<< LAUNCH", i)) + 1; s = s[:i] + s[j:]
        rep("FleetLoad(true); LaunchLoad(); FollowLoad();", "FleetLoad(true); FollowLoad();")
        rep("bool burn = LaunchStep(dt, _mode == Mode.IDLE, StartMode, _dynHomeT > 0, _dynHome, Cb);   // automatic take-off after the release from a printer (LAUNCH block)", "bool burn = false;      // no LAUNCH block on this ship (no merge block, not printable)")
        rep("if (burn || _lnS == 2) return;", "if (burn) return;")
        rep('        case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;\n', "")
        rep("    _sb.Append(LaunchStatus());\n", "")
        rep("//   * LAUNCH       take-off after the release from a printer - same [Launch] section; Staff flies the burn, Flight lets go of the ship\n", "")
        rep("CUSTOM DATA (this PB): [Launch], [Follow], [Fleet]", "CUSTOM DATA (this PB): [Follow], [Fleet]")
    out = os.path.join(ROOT, "scripts", f"{name}StaffAI.cs")
    open(out, "w", encoding="utf-8", newline="").write(s.replace("\n", "\r\n") if crlf else s)
    print("wrote", out)
if __name__ == "__main__": gnat() if sys.argv[1] == "GnatSwarmAI" else main(*sys.argv[1:6])
