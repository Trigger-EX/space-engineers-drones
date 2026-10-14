#!/usr/bin/env python3
"""One-off generator: turn a single-PB drone script (AegisAI, MantisAI, LongbowSniperAI, HornetDroneAI, HarrierAI, LancerAI) into the FLIGHT half of a two-PB drone, the way WaspFlightAI is the flight
half of the Wasp: the FLEET LINK, FOLLOW and LAUNCH blocks and the status screen go to the Staff PB (tools/make_staff.py), the SPLIT LINK block (tools/lk_block.cs)
takes their place. Edits the script in place (git has the original) and asserts every edit hit. Usage: python3 -I tools/split_flight.py AegisAI
"""
import os, re, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AMMO = {"HornetDroneAI": "(int)(_ammoFrac * 100)", "HarrierAI": "(int)(_ammoFrac * 100)", "LancerAI": "(int)(_ammoFrac * 100)", "AegisAI": "(int)(_ammoFrac * 100)", "MantisAI": "(int)(_ammoFrac * 100)", "LongbowSniperAI": "(int)(_ammoFrac * 100)", "BasiliskAI": "_rockets"}
GATE = {"BasiliskAI": "_gateWhy"}      # what the Staff screen shows as the hold reason (default: none)

def gnat(name):
    """GnatSwarmAI: the point-and-burn Gnat has no FOLLOW block (its FORM / satellite ring is its own code in Control), so only FLEET LINK and LAUNCH leave (docs/design/gnat_split.md).
    The FOLLOW hooks, the AMMO entry and the Display swap of the Wasp-style cut do not apply; the Gnat keeps its own full status screen."""
    p = os.path.join(ROOT, "scripts", name + ".cs")
    raw = open(p, encoding="utf-8", newline="").read(); crlf = "\r\n" in raw; s = raw.replace("\r\n", "\n")
    assert "// >>> SPLIT LINK" not in s, "already split"
    def rep(old, new, cnt=1, rx=False):
        nonlocal s
        n = len(re.findall(old, s)) if rx else s.count(old)
        assert n == cnt, f"{name}: expected {cnt} of {old!r}, found {n}"
        s = re.sub(old, new, s) if rx else s.replace(old, new)
    def drop(block):
        nonlocal s
        i = s.index("// >>> " + block); j = s.index("// <<< " + block, i); j = s.index("\n", j) + 1
        s = s[:i] + s[j:]
    for b in ("FLEET LINK", "LAUNCH"): drop(b)
    lk = open(os.path.join(ROOT, "tools", "lk_block.cs"), encoding="utf-8").read()
    m = re.search(r"^public Program\(\)", s, re.M)
    s = s[:m.start()] + lk + 'int LkAmmo() { return -1; }   // the Gnat has no ammo count: the Staff shows "?"\nstring LkGate() { return ""; }\ndouble _fireWinDeg;       // firing window of the last aim (the SPLIT LINK sends it)\nIMyBroadcastListener _lkHmr;   // "<fleet channel>_HOMER" relayed by the Staff (the carrier radius), see ReadIGC\n\n' + s[m.start():]
    rep("FleetLoad(true); LaunchLoad(); KzLoad(); AiGateLoad();", "KzLoad(); AiGateLoad();", 2)
    rep(r' *case "armed": case "launch": case "disarm": LaunchCmd\(w\[0\]\); break;\n', "", 1, True)
    rep('        _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME");', '        LkInit(); _lkHmr = IGC.RegisterBroadcastListener(_lkK + ".HMR");\n        _lHome = IGC.RegisterBroadcastListener(Tag + "_HOME");', 1)
    # ReadIGC: the fleet channel's targets and its carrier radius come over the link
    rep("for (int li = -1; li < _lFeed.Count + FleetTgtL.Count; li++)", "for (int li = -1; li <= _lFeed.Count; li++)", 1)
    rep("li < _lFeed.Count ? _lFeed[li] : FleetTgtL[li - _lFeed.Count];", "li < _lFeed.Count ? _lFeed[li] : _lkTgt;", 1)
    rep(r" *FleetPump\(Command, p => \{ _dynHome = p; _dynHomeT = _now; \}\);\n", "", 1, True)
    i = s.index("    if (_lHomeR2Key != _flChan)"); j = s.index("    if (_dynHomeT > _hvT)", i)
    s = s[:i] + "    while (_lkHmr.HasPendingMessage) { var m = _lkHmr.AcceptMessage(); if (m.Data is double) { _homeR = (double)m.Data; _homeRT = _now; } }\n" + s[j:]
    rep(r"IMyBroadcastListener _lHomeR2; string _lHomeR2Key = null;[^\n]*\n", "", 1, True)
    # Tick
    rep("    ReadIGC();\n    if (!Hot()) StepSlimScan();", "    ReadIGC(); LkRead();\n    if (!Hot()) StepSlimScan();", 1)
    rep(r"    if \(LaunchStep\([^\n]*\n    else if \(!AiStep\(\)\) Control\(dt\);\n", "    if (!LkBurn && !AiStep()) Control(dt);      // (LkBurn: the Staff PB flies the take-off burn, LAUNCH block)\n    LkSend();\n", 1, True)
    rep("if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;", "if (dt <= 0 || dt > 0.5) { if (dt > 0.5 && _tick > 3) { _aiOn = !_aiOn; _firing = false; } dt = 1.0 / 60.0; }   // run gap (PB was off / halted): the Staff watchdog may have switched the AI blocks off, so force the next pass to send the AI and gun state again", 1)
    rep("        Command(arg);\n", '        { Command(arg); if (_lkK != "") IGC.SendBroadcastMessage(_lkK + ".CMD", arg, TransmissionDistance.CurrentConstruct); }   // (the Staff PB handles launch / armed / disarm / reload)\n', 1)
    # the firing window the link sends
    rep("bool fire = false; _aimErrDeg = 0; _latSpd = 0;", "bool fire = false; _aimErrDeg = 0; _fireWinDeg = 0; _latSpd = 0;", 1)
    rep("        fire = inRange && _aimErrDeg < win &&", "        _fireWinDeg = win;\n        fire = inRange && _aimErrDeg < win &&", 1)
    # the take-off burn is the Staff's: GetBlocks runs when the merge opens and must not zero the thrusters under it
    rep("            else t.ThrustOverridePercentage = 0;          // off-axis thrusters are left alone", "            else if (!LkBurn) t.ThrustOverridePercentage = 0;          // off-axis thrusters are left alone (not under the Staff's take-off burn)", 1)
    rep("    _sb.Append(LaunchStatus());\n", '    _sb.Append("Staff : ").Append(LkAlive ? "ok" : "none").Append(\'\\n\');\n', 1)
    # ParseMode: LkRead asks with the sentinel (Mode)(-1) "does this verb change nothing?"; it must not set SatOn, and FORM / SATELLITE differ by SatOn
    rep('        case "SATELLITE": case "SAT": case "ORBIT": SatOn = true; return Mode.FORM; case "IDLE": return Mode.IDLE; case "FORM": return Mode.FORM; case "ATTACK": return Mode.ATTACK; case "RTB": return Mode.RTB;',
        '        case "SATELLITE": case "SAT": case "ORBIT": if ((int)d == -1) return SatOn && !_ready ? Mode.FORM : (Mode)(-2); SatOn = true; return Mode.FORM;      // (d == -1: the Staff link asks whether the verb changes nothing)\n        case "FORM": case "GUARD": if ((int)d == -1) return !SatOn && !_ready ? Mode.FORM : (Mode)(-2); return Mode.FORM;\n        case "IDLE": return Mode.IDLE; case "ATTACK": case "HUNT": return Mode.ATTACK; case "RTB": return Mode.RTB;', 1)
    rep('const string VER = "1.13+launch";', 'const string VER = "1.14-gnat-split";', 1)
    rep('//  GNAT SWARM AI  v1.12 - minimal "point-and-burn" swarm drone\n', '//  GNAT SWARM AI  v1.14 - minimal "point-and-burn" swarm drone (FLIGHT half of a two-PB drone: FLEET LINK and LAUNCH run in the "[Staff] ..." PB, scripts/GnatStaffAI.cs; docs/design/gnat_split.md)\n', 1)
    open(p, "w", encoding="utf-8", newline="").write(s.replace("\n", "\r\n") if crlf else s)
    print("split", name)

def main(name):
    p = os.path.join(ROOT, "scripts", name + ".cs")
    raw = open(p, encoding="utf-8", newline="").read(); crlf = "\r\n" in raw; s = raw.replace("\r\n", "\n")
    assert "// >>> SPLIT LINK" not in s, "already split"
    def rep(old, new, cnt=1, rx=False):
        nonlocal s
        n = len(re.findall(old, s)) if rx else s.count(old)
        assert n == cnt, f"{name}: expected {cnt} of {old!r}, found {n}"
        s = re.sub(old, new, s) if rx else s.replace(old, new)
    def drop(block):
        nonlocal s
        i = s.index("// >>> " + block); j = s.index("// <<< " + block, i); j = s.index("\n", j) + 1
        s = s[:i] + s[j:]
    hasL = "// >>> LAUNCH" in s       # (the Basilisk has no merge block and no LAUNCH block)
    for b in ("FLEET LINK", "FOLLOW") + (("LAUNCH",) if hasL else ()): drop(b)
    lk = open(os.path.join(ROOT, "tools", "lk_block.cs"), encoding="utf-8").read()
    m = re.search(r"^public Program\(\)", s, re.M)
    s = s[:m.start()] + lk + f"int LkAmmo() {{ return {AMMO[name]}; }}   // ammo for the Staff screen (0-254)\nstring LkGate() {{ return {GATE.get(name, '\"\"')}; }}\n\n" + s[m.start():]
    nl = len(re.findall(r"\n *LaunchLoad\(\);(?=\n)", s)) if hasL else 0      # (Hornet / Harrier call LaunchLoad() on a line of its own in the constructor)
    if nl: rep(r"\n *LaunchLoad\(\);(?=\n)", "", nl, True)
    rep(r'(?m)^( *(?:case "reload": LoadConfig\(\); )?)FleetLoad\(true\); ', r"\1", 2, True)   # (code lines only: the hook comments name FleetLoad(true) too)
    if hasL: rep(r"LaunchLoad\(\); ", "", 2 - nl, True)
    rep(r" FollowLoad\(\);", "", 2, True)
    rep(r"FoSat = true; ", "", 1, True)
    rep("ReadIGC(); FollowStep(); ReadSwarm();", "ReadIGC(); LkRead(); ReadSwarm();")
    rep(r"FoSetSat\((?:true|false)\); ", "", 2, True)
    if hasL: rep(r' *case "armed": case "launch": case "disarm": LaunchCmd\(w\[0\]\); break;\n', "", 1, True)
    rep("foreach (var fl in FleetTgtL) ReadTgt(fl);", "ReadTgt(_lkTgt);")
    rep(r" *FleetPump\(Command, p => \{ _dynHome = p; _dynHomeT = _now; \}\);\n", "", 1, True)
    rep("FoEngage", "LkEngage", 2)
    rep("if (_mode == Mode.FOLLOW && FollowKnown()) return FollowPos();", "if (_mode == Mode.FOLLOW && _lkOwner) return _lkOwnerP;")
    if hasL: rep(r"if \(LaunchStep\([^\n]*?\)\) return;", "if (LkBurn) return;   // take-off burn: the Staff PB flies (LAUNCH block)", 1, True)
    rep("(FollowRadius() + 40)", "65")
    rep("if (_mode == Mode.FOLLOW && FollowKnown()) want = FollowWant(me, aBrake, Separation(me)) + Evade();",
        "if (_mode == Mode.FOLLOW && LkWantOk) want = _lkWant + ((_lkFl & 2) != 0 ? Vector3D.Zero : Separation(me)) + Evade();")
    rep("FollowFar(me)) want = FollowBack(me);", "LkFar(me)) want = LkBack(me);")
    rep("TargetChars || FoFoot", "true"); rep("SetB(s, FoFoot,", "SetB(s, true,")
    rep("FollowSensed(e); ", "")
    rep(r"(double aBrake = MinAccel\(mass, grav\) \* [0-9.]+;)", r"\1 _aBrake = aBrake;", 1, True)
    rep("if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;", "if (dt <= 0 || dt > 0.5) { if (dt > 0.5 && _tick > 3) { _aiOn = !_aiOn; _firing = false; } dt = 1.0 / 60.0; }   // run gap (PB was off / halted): the Staff watchdog may have switched the AI blocks off, so force the next pass to send the AI and gun state again")
    rep("if (!AiStep()) Control(dt);", "if (!LkBurn && !AiStep()) Control(dt);\n    LkSend();")
    rep("        Command(arg);\n", '        { Command(arg); if (_lkK != "") IGC.SendBroadcastMessage(_lkK + ".CMD", arg, TransmissionDistance.CurrentConstruct); }   // (the Staff PB handles launch / armed / disarm / follow / satellite / reload)\n')
    rep(r"( *)(_lHome = IGC\.RegisterBroadcastListener\(Tag \+ \"_HOME\"\);)", r"\1LkInit();\n\1\2", 1, True)
    if re.search(r"^ *FaBeacon\(\);\n", s, re.M): rep(r" *FaBeacon\(\);\n", "", 1, True)   # (Wasp Flight leaves the beacon to the Staff)
    if name == "BasiliskAI":      # code nothing calls once the status screen has left: HiveStatus / AiStatus (only the old screen) and MatesInLine / MateClear (never called)
        def cut(a, b):
            nonlocal s
            i = s.index(a); j = s.index(b, i); s = s[:i] + s[j:]
        cut("string HiveStatus()", "class Mate {"); cut("string AiStatus()", "bool AiModeOk()")
        cut("// Friendly-fire gate from the IGC positions", "void CheckLineOfFire(")
    # the status screen lives in the Staff PB
    i = s.index("\nvoid Display()\n{"); j = s.index("\n}\n", i) + 3
    disp = '''
void Display()
{
    string aim = "";
    if (_tgt != null) aim = "\\nAim: " + AimLabel(_tgt) + " (" + _tgt.Hull.Count + " hull pts)" + AimInfo();
    string s = "FLIGHT " + VER + "  " + _mode + "  tgt " + (_tgt != null ? _tgt.Src : "-") + "  staff " + (LkAlive ? "ok" : "none") + aim + (_err != ""? "\\n! " + _err : "") + (_crash != "" ? "\\n! Script error: " + _crash : "");
    if (_lcd != null) _lcd.WriteText(s);
    Echo(s);
}
'''
    s = s[:i] + disp + s[j:]
    open(p, "w", encoding="utf-8", newline="").write(s.replace("\n", "\r\n") if crlf else s)
    print("split", name)

if __name__ == "__main__": (gnat if sys.argv[1] == "GnatSwarmAI" else main)(sys.argv[1])
