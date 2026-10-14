#!/usr/bin/env python3
"""One-off generator: the Staff half of a two-PB drone is WaspStaffAI.cs with the drone's name, IGC tag and Custom Data section substituted
(FLEET LINK / FOLLOW / LAUNCH are shared blocks kept identical by tools/fleet_sync.py, follow_sync.py, launch_sync.py).
Usage: python3 -I tools/make_staff.py AegisAI Aegis AEGIS Ammo   ->  scripts/AegisStaffAI.cs   (script, display name, IGC tag, config section)
A name in OPTS gets per-drone options: nolaunch (no LAUNCH block: a ship with no merge block would take off on a fleet "launch" / "join"), rockets (keep the "Rockets" label:
the Flight sends a rocket count, not an ammo percent), speed / sep (the drone's Combat Speed / Wingmate Separation Range defaults)."""
import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OPTS = {"Basilisk": {"nolaunch": True, "rockets": True, "speed": 60, "sep": 160}}

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
if __name__ == "__main__": main(*sys.argv[1:6])
