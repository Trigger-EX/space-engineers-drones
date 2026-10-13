#!/usr/bin/env python3
"""Keep the BREACH + SEEKER block (tools/aim_block.cs) identical in every armed drone script, and wire its hooks.

  python3 -I tools/aim_sync.py --check   exit 1 if any script has a missing/different block or a missing hook
  python3 -I tools/aim_sync.py --apply   insert/replace the block (after AimMissed) and add the hooks (idempotent)

Hooks (one line each, same text in every script): AimLabel/AimOffset/ProbePoint/AimMissed switch to BREACH, the lock-ray result
feeds BreachHit, the status line shows AimInfo(), SeekerSetup() runs after the camera scan, config keys and the "breach" command.
"""
import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = ["HornetDroneAI", "GnatSwarmAI", "LongbowSniperAI", "WaspAI", "LancerAI", "HarrierAI", "AegisAI", "MantisAI", "BasiliskAI"]
BEGIN, END = "// >>> BREACH + SEEKER", "// <<< BREACH + SEEKER <<<"

# (anchor text, replacement text). The replacement contains the anchor's meaning plus the hook; applied only when the
# replacement is not present yet.
HOOKS = [
    ('    if (!AimActive(t)) return "CENTER";\n',
     '    if (!AimActive(t)) return "CENTER";\n    if (BreachMode(t)) return "BREACH";\n'),
    ('Vector3D AimOffset(Track t)\n{\n    if (!AimActive(t)) return Vector3D.Zero;\n',
     'Vector3D AimOffset(Track t)\n{\n    SeekerTick(t);\n    if (!AimActive(t)) return Vector3D.Zero;\n    if (BreachMode(t)) return BreachOffset(t);\n'),
    ('return centre + AimOffset(t) + dir * 1.5; }',
     'return centre + AimOffset(t) + dir * (BreachMode(t) ? BreachProbe : 1.5); }'),
    ('void AimMissed(Track t)\n{\n',
     'void AimMissed(Track t)\n{\n    if (BreachMode(t)) { BreachMiss(); return; }\n'),
    ('        if (probingAim && (e.IsEmpty() || e.EntityId != _tgt.Id)) AimMissed(_tgt);\n',
     '        if (probingAim && !e.IsEmpty() && e.EntityId == _tgt.Id) BreachHit(_tgt, e, aim);\n'
     '        else if (probingAim && (e.IsEmpty() || !BreachMode(_tgt))) AimMissed(_tgt);   // breach: a drone in the way is no miss\n'),
    ('" hull pts)";', '" hull pts)" + AimInfo();'),
    ('    AimMode = Cfg(S, "Aim Point (AUTO/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();\n',
     '    AimMode = Cfg(S, "Aim Point (AUTO/BREACH/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();\n'
     '    BreachAuto = Cfg(S, "Breach Large Ships (AUTO)", BreachAuto);\n'
     '    BreachMinRadius = Cfg(S, "Breach Min Target Radius (m)", BreachMinRadius);\n'
     '    BreachStall = Cfg(S, "Breach Stall (s)", BreachStall);\n'
     '    SeekerGroup = Cfg(S, "Seeker Group (PowerSystems/Weapons/Propulsion)", SeekerGroup).Trim();\n'
     '    SeekerOn = Cfg(S, "Seeker Turret Guides Aim", SeekerOn);\n'),
    ('        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;\n',
     '        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;\n'
     '        case "breach": if (w.Length > 1) BreachAuto = w[1] == "on"; break;\n'
     '        case "seeker": if (w.Length > 1) { SeekerOn = w[1] == "on"; SeekerSetup(); } break;\n'),
]
# earlier wirings of a hook (anchor -> list of texts that a previous version of this tool put in its place)
PREVIOUS = {
    'Vector3D AimOffset(Track t)\n{\n    if (!AimActive(t)) return Vector3D.Zero;\n': [
        'Vector3D AimOffset(Track t)\n{\n    if (!AimActive(t)) return Vector3D.Zero;\n    if (BreachMode(t)) return BreachOffset(t);\n'],
    '    AimMode = Cfg(S, "Aim Point (AUTO/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();\n': [
        '    AimMode = Cfg(S, "Aim Point (AUTO/BREACH/ENGINES/FACING/SPREAD/CENTER)", AimMode).Trim().ToUpperInvariant();\n'
        '    BreachAuto = Cfg(S, "Breach Large Ships (AUTO)", BreachAuto);\n'
        '    BreachMinRadius = Cfg(S, "Breach Min Target Radius (m)", BreachMinRadius);\n'
        '    BreachStall = Cfg(S, "Breach Stall (s)", BreachStall);\n'
        '    SeekerGroup = Cfg(S, "Seeker Group (PowerSystems/Weapons/Propulsion)", SeekerGroup).Trim();\n'],
    '        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;\n': [
        '        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;\n'
        '        case "breach": if (w.Length > 1) BreachAuto = w[1] == "on"; break;\n'],
}
CAM_SCAN = "GridTerminalSystem.GetBlocksOfType(_cams"


def block():
    with open(os.path.join(ROOT, "tools", "aim_block.cs"), encoding="utf-8", newline="") as f:
        return f.read().replace("\r\n", "\n").rstrip("\n") + "\n"


def wire(t, name):
    """Returns (new text, list of problems)."""
    probs = []
    for old, new in HOOKS:
        if new in t: continue
        # an older version of the same hook (its first line ends a previous replacement) also counts as the anchor
        prev = [p for p in PREVIOUS.get(old, []) if t.count(p) == 1]
        if prev: t = t.replace(prev[0], new); continue
        if t.count(old) != 1: probs.append("hook anchor %r found %d times" % (old.strip()[:50], t.count(old))); continue
        t = t.replace(old, new)
    # SeekerSetup() right after the camera scan line
    i = t.find(CAM_SCAN)
    if i < 0: probs.append("no camera scan line")
    else:
        j = t.find("\n", i) + 1
        if not t[j:].lstrip(" ").startswith("SeekerSetup();"):
            indent = t[t.rfind("\n", 0, i) + 1:i]
            t = t[:j] + indent + "SeekerSetup();\n" + t[j:]
    return t, probs


def main():
    apply_ = "--apply" in sys.argv
    b = block(); bad = 0
    for n in SCRIPTS:
        p = os.path.join(ROOT, "scripts", n + ".cs")
        with open(p, encoding="utf-8", newline="") as f: raw = f.read()
        crlf = "\r\n" in raw
        t = raw.replace("\r\n", "\n")
        i = t.find(BEGIN)
        if i >= 0:
            j = t.find(END, i); j = t.find("\n", j) + 1
            new = t[:i] + b + t[j:]
        else:
            k = t.find("void AimMissed(Track t)")
            if k < 0: print("NO ANCHOR", n); bad += 1; continue
            k = t.find("\n}\n", k) + 3
            new = t[:k] + "\n" + b + t[k:]
        new, probs = wire(new, n)
        if probs: print("PROBLEM ", n, "; ".join(probs)); bad += 1; continue
        if new == t: print("ok      ", n); continue
        if not apply_: print("DIFFERS ", n); bad += 1; continue
        if crlf: new = new.replace("\n", "\r\n")
        with open(p, "w", encoding="utf-8", newline="") as f: f.write(new)
        print("updated ", n)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
