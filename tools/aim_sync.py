#!/usr/bin/env python3
"""Keep the BREACH + SEEKER block (tools/aim_block.cs) identical in every armed drone script, and wire its hooks.

  python3 -I tools/aim_sync.py --check   exit 1 if any script has a missing/different block or a missing hook
  python3 -I tools/aim_sync.py --apply   insert/replace the block (after AimMissed) and add the hooks (idempotent)

Hooks (one line each, same text in every script): AimLabel/AimOffset/ProbePoint/AimMissed switch to BREACH, the lock-ray result
feeds BreachHit, the status line shows AimInfo(), config keys (since flight_split: the CfgIn() assignments, values from the Staff) and the
"breach" / "wp" commands.
v1.3 (docs/design/survivability.md): the SEEKER turret moved to the Staff PB (STAFF SEARCH); its finding comes back as the .WPT slot with flag
4096. So the seeker hooks are gone, and --apply also strips them from scripts wired by an older version (SeekerTick -> WpTick, the two seeker
keys, the "seeker" command, the SeekerSetup() line after the camera scan).
"""
import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = ["HornetDroneAI", "GnatSwarmAI", "LongbowSniperAI", "WaspFlightAI", "LancerAI", "HarrierAI", "AegisAI", "MantisAI", "BasiliskAI"]
BEGIN, END = "// >>> BREACH + SEEKER", "// <<< BREACH + SEEKER <<<"
# Left at v1.0: the legacy single-PB Wasp (99.8k, no blueprint since the Wasp runs WaspFlightAI) has no SPLIT LINK to carry the weakpoint relay
# and no room for it (docs/design/hive_weakpoint.md section 5).
FROZEN = ["WaspAI"]

def unlisted():
    """Scripts that carry this block's marker but are not in SCRIPTS (or are on EXCLUDE): a new script would otherwise silently skip the sync."""
    d = os.path.join(ROOT, "scripts"); skip = globals().get("EXCLUDE", [])
    out = []
    for f in sorted(os.listdir(d)):
        if f.endswith(".cs") and f[:-3] not in FROZEN and (f[:-3] not in SCRIPTS or f[:-3] in skip):
            with open(os.path.join(d, f), encoding="utf-8") as h:
                if BEGIN in h.read(): out.append(f[:-3])
    return out

# (anchor text, replacement text). The replacement contains the anchor's meaning plus the hook; applied only when the
# replacement is not present yet.
HOOKS = [
    ('    if (!AimActive(t)) return "CENTER";\n',
     '    if (!AimActive(t)) return "CENTER";\n    if (BreachMode(t)) return "BREACH";\n'),
    ('Vector3D AimOffset(Track t)\n{\n    if (!AimActive(t)) return Vector3D.Zero;\n',
     'Vector3D AimOffset(Track t)\n{\n    WpTick(t);\n    if (!AimActive(t)) return Vector3D.Zero;\n    if (BreachMode(t)) return BreachOffset(t);\n'),
    ('return centre + AimOffset(t) + dir * 1.5; }',
     'return centre + AimOffset(t) + dir * (BreachMode(t) ? BreachProbe : 1.5); }'),
    ('void AimMissed(Track t)\n{\n',
     'void AimMissed(Track t)\n{\n    if (BreachMode(t)) { BreachMiss(); return; }\n'),
    ('        if (probingAim && (e.IsEmpty() || e.EntityId != _tgt.Id)) AimMissed(_tgt);\n',
     '        if (probingAim && !e.IsEmpty() && e.EntityId == _tgt.Id) BreachHit(_tgt, e, aim);\n'
     '        else if (probingAim && (e.IsEmpty() || !BreachMode(_tgt))) AimMissed(_tgt);   // breach: a drone in the way is no miss\n'),
    ('" hull pts)";', '" hull pts)" + AimInfo();'),
    # flight_split P1-F: the [<Model>] keys come from the Staff on .CFG (CONFIG LINK); the BREACH / weakpoint keys are assignments in the
    # generated CfgIn() (tools/cfg_tables.json order), so the hook is now a check that CfgIn carries them (the Staff's table writes the keys)
    ('    AimMode = T().Trim().ToUpperInvariant();\n',
     '    AimMode = T().Trim().ToUpperInvariant();\n'
     '    BreachAuto = B();\n'
     '    BreachMinRadius = N();\n'
     '    BreachStall = N();\n'
     '    WpOn = B();\n'
     '    WpShare = B();\n'
     '    WpCone = N();\n'
     '    WpHold = N();\n'),
    ('        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;\n',
     '        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;\n'
     '        case "breach": if (w.Length > 1) BreachAuto = w[1] == "on"; break;\n'
     '        case "wp": if (w.Length > 1) WpOn = WpShare = w[1] == "on"; break;\n'),
]
# earlier wirings of a hook (anchor -> list of texts that a previous version of this tool put in its place)
PREVIOUS = {
    'Vector3D AimOffset(Track t)\n{\n    if (!AimActive(t)) return Vector3D.Zero;\n': [
        'Vector3D AimOffset(Track t)\n{\n    SeekerTick(t);\n    if (!AimActive(t)) return Vector3D.Zero;\n    if (BreachMode(t)) return BreachOffset(t);\n',
        'Vector3D AimOffset(Track t)\n{\n    if (!AimActive(t)) return Vector3D.Zero;\n    if (BreachMode(t)) return BreachOffset(t);\n'],
    '        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;\n': [
        '        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;\n'
        '        case "breach": if (w.Length > 1) BreachAuto = w[1] == "on"; break;\n'
        '        case "seeker": if (w.Length > 1) { SeekerOn = w[1] == "on"; SeekerSetup(); } break;\n'
        '        case "wp": if (w.Length > 1) WpOn = WpShare = w[1] == "on"; break;\n',
        '        case "aim": if (w.Length > 1) AimMode = w[1].ToUpperInvariant(); break;\n'
        '        case "breach": if (w.Length > 1) BreachAuto = w[1] == "on"; break;\n'
        '        case "seeker": if (w.Length > 1) { SeekerOn = w[1] == "on"; SeekerSetup(); } break;\n',
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
    # v1.2 put SeekerSetup() right after the camera scan line: the seeker is the Staff PB's now (v1.3), take the line out
    i = t.find(CAM_SCAN)
    if i < 0: probs.append("no camera scan line")
    else:
        j = t.find("\n", i) + 1
        k = t.find("\n", j) + 1
        if t[j:k].strip() == "SeekerSetup();": t = t[:j] + t[k:]
    for bad in ("SeekerSetup(", "SeekerTick(", "SeekerOn", "SeekerGroup"):
        if bad in t: probs.append("seeker code left outside the block: " + bad)
    return t, probs


def main():
    apply_ = "--apply" in sys.argv
    b = block(); bad = 0
    for n in unlisted(): print("UNLISTED ", n, "carries the block but is not in SCRIPTS (add it, or remove the block)"); bad += 1
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
