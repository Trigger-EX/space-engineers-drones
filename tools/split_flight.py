#!/usr/bin/env python3
"""One-off generator: turn a single-PB drone script (AegisAI, MantisAI, LongbowSniperAI) into the FLIGHT half of a two-PB drone, the way WaspFlightAI is the flight
half of the Wasp: the FLEET LINK, FOLLOW and LAUNCH blocks and the status screen go to the Staff PB (tools/make_staff.py), the SPLIT LINK block (tools/lk_block.cs)
takes their place. Edits the script in place (git has the original) and asserts every edit hit. Usage: python3 -I tools/split_flight.py AegisAI
"""
import os, re, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AMMO = {"AegisAI": "(int)(_ammoFrac * 100)", "MantisAI": "(int)(_ammoFrac * 100)", "LongbowSniperAI": "(int)(_ammoFrac * 100)"}

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
    for b in ("FLEET LINK", "FOLLOW", "LAUNCH"): drop(b)
    lk = open(os.path.join(ROOT, "tools", "lk_block.cs"), encoding="utf-8").read()
    m = re.search(r"^public Program\(\)", s, re.M)
    s = s[:m.start()] + lk + f"int LkAmmo() {{ return {AMMO[name]}; }}   // ammo for the Staff screen (0-254)\nstring LkGate() {{ return \"\"; }}\n\n" + s[m.start():]
    rep(r"FleetLoad\(true\); LaunchLoad\(\); ", "", 2, True)
    rep(r" FollowLoad\(\);", "", 2, True)
    rep(r"FoSat = true; ", "", 1, True)
    rep("ReadIGC(); FollowStep(); ReadSwarm();", "ReadIGC(); LkRead(); ReadSwarm();")
    rep(r"FoSetSat\((?:true|false)\); ", "", 2, True)
    rep(r' *case "armed": case "launch": case "disarm": LaunchCmd\(w\[0\]\); break;\n', "", 1, True)
    rep("foreach (var fl in FleetTgtL) ReadTgt(fl);", "ReadTgt(_lkTgt);")
    rep(r" *FleetPump\(Command, p => \{ _dynHome = p; _dynHomeT = _now; \}\);\n", "", 1, True)
    rep("FoEngage", "LkEngage", 2)
    rep("if (_mode == Mode.FOLLOW && FollowKnown()) return FollowPos();", "if (_mode == Mode.FOLLOW && _lkOwner) return _lkOwnerP;")
    rep(r"if \(LaunchStep\([^\n]*?\)\) return;", "if (LkBurn) return;   // take-off burn: the Staff PB flies (LAUNCH block)", 1, True)
    rep("(FollowRadius() + 40)", "65")
    rep("if (_mode == Mode.FOLLOW && FollowKnown()) want = FollowWant(me, aBrake, Separation(me)) + Evade();",
        "if (_mode == Mode.FOLLOW && LkWantOk) want = _lkWant + ((_lkFl & 2) != 0 ? Vector3D.Zero : Separation(me)) + Evade();")
    rep("FollowFar(me)) want = FollowBack(me);", "LkFar(me)) want = LkBack(me);")
    rep("TargetChars || FoFoot", "true"); rep("SetB(s, FoFoot,", "SetB(s, true,")
    rep("FollowSensed(e); ", "")
    rep(r"(double aBrake = MinAccel\(mass, grav\) \* [0-9.]+;)", r"\1 _aBrake = aBrake;", 1, True)
    rep("if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;", "if (dt <= 0 || dt > 0.5) { if (dt > 0.5 && _tick > 3) { _aiOn = !_aiOn; _firing = false; _aiKick = false; } dt = 1.0 / 60.0; }   // run gap (PB was off / halted): the Staff watchdog may have switched the AI blocks off, so force the next pass to send the AI and gun state again")
    rep("if (!AiStep()) Control(dt);", "if (!LkBurn && !AiStep()) Control(dt);\n    LkSend();")
    rep("        Command(arg);\n", '        { Command(arg); if (_lkK != "") IGC.SendBroadcastMessage(_lkK + ".CMD", arg, TransmissionDistance.CurrentConstruct); }   // (the Staff PB handles launch / armed / disarm / follow / satellite / reload)\n')
    rep(r"( *)(_lHome = IGC\.RegisterBroadcastListener\(Tag \+ \"_HOME\"\);)", r"\1LkInit();\n\1\2", 1, True)
    if re.search(r"^ *FaBeacon\(\);\n", s, re.M): rep(r" *FaBeacon\(\);\n", "", 1, True)   # (Wasp Flight leaves the beacon to the Staff)
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

if __name__ == "__main__": main(sys.argv[1])
