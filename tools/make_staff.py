#!/usr/bin/env python3
"""One-off generator: the Staff half of a two-PB drone is WaspStaffAI.cs with the drone's name, IGC tag and Custom Data section substituted
(FLEET LINK / FOLLOW / LAUNCH are shared blocks kept identical by tools/fleet_sync.py, follow_sync.py, launch_sync.py).
Usage: python3 -I tools/make_staff.py AegisAI Aegis AEGIS Ammo   ->  scripts/AegisStaffAI.cs   (script, display name, IGC tag, config section)"""
import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
def main(script, name, tag, section, leash=""):
    src = open(os.path.join(ROOT, "scripts", "WaspStaffAI.cs"), encoding="utf-8", newline="").read()
    crlf = "\r\n" in src; s = src.replace("\r\n", "\n")
    low = name.lower(); up = name.upper()
    for a, b in (("WASP STAFF AI  v1.0-wasp-split", f"{up} STAFF AI  v1.0-{low}-split"), ("two-PB Wasp", f"two-PB {name}"), ("WaspFlightAI.cs", f"{script}.cs (the Flight half)"),
                 ("WaspAI.cs", f"the {name} script"), ('"1.0-wasp-split"', f'"1.0-{low}-split"'), ('string Tag = "WASP"', f'string Tag = "{tag}"'),
                 ('[Wasp] section', f'[{section}] section'), ("[Wasp] keys", f"[{section}] keys"), ('ini.Get("Wasp",', f'ini.Get("{section}",'),
                 ('"WASP STAFF v"', f'"{up} STAFF v"'), ("%  Rockets ", "%  Ammo "), ("the Wasp", f"the {name}")):
        s = s.replace(a, b)
    if leash:
        a = "void LoadMods() { FleetLoad(true); LaunchLoad(); FollowLoad(); }"
        assert a in s
        s = s.replace(a, "void LoadMods() { FleetLoad(true); LaunchLoad(); FollowLoad(); FoLeash = Math.Max(FoLeash, " + leash + "); }   // (a sniper stands far out: the owner leash is at least this long)")
    out = os.path.join(ROOT, "scripts", f"{name}StaffAI.cs")
    open(out, "w", encoding="utf-8", newline="").write(s.replace("\n", "\r\n") if crlf else s)
    print("wrote", out)
if __name__ == "__main__": main(*sys.argv[1:6])
