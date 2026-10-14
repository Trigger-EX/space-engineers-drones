#!/usr/bin/env python3
"""Keep the LAUNCH block (tools/launch_block.cs) identical in every drone script that carries it.

  python3 -I tools/launch_sync.py --check            exit 1 if a script has a DIFFERENT block or a REQUIRED script lacks it
  python3 -I tools/launch_sync.py --apply            replace the block in every script that has it, and insert it into the REQUIRED ones
  python3 -I tools/launch_sync.py --apply --add Wasp,Lancer   also insert it (before 'public Program()') into the named scripts
Scripts that do not have the block yet are reported as 'absent' and do not fail the check (they are integrated one by one, see
docs/launch-protocol.md section 6). The hooks that call the block (LaunchLoad / LaunchCmd / LaunchStep / LaunchStatus) are NOT touched.
Copy of the mechanism of tools/fleet_sync.py (same CRLF handling).
"""
import os, re, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = ["HornetDroneAI", "GnatSwarmAI", "WaspAI", "WaspStaffAI", "LancerAI", "HarrierAI", "BasiliskAI", "StingerAI", "AegisStaffAI", "MantisStaffAI", "LongbowStaffAI"]
REQUIRED = ["HornetDroneAI", "HarrierAI"]
EXCLUDE = ["BasiliskAI"]        # no merge block on this ship and no room: the block is deliberately NOT carried (never added, never required)
BEGIN, END = "// >>> LAUNCH", "// <<< LAUNCH <<<"

def unlisted():
    """Scripts that carry this block's marker but are not in SCRIPTS (or are on EXCLUDE): a new script would otherwise silently skip the sync."""
    d = os.path.join(ROOT, "scripts"); skip = globals().get("EXCLUDE", [])
    out = []
    for f in sorted(os.listdir(d)):
        if f.endswith(".cs") and (f[:-3] not in SCRIPTS or f[:-3] in skip):
            with open(os.path.join(d, f), encoding="utf-8") as h:
                if BEGIN in h.read(): out.append(f[:-3])
    return out

def block():
    with open(os.path.join(ROOT, "tools", "launch_block.cs"), encoding="utf-8", newline="") as f:
        return f.read().replace("\r\n", "\n").rstrip("\n") + "\n"

def main():
    apply_ = "--apply" in sys.argv
    add = []
    if "--add" in sys.argv:
        i = sys.argv.index("--add")
        if i + 1 < len(sys.argv): add = [x for x in sys.argv[i + 1].split(",") if x]
    b = block(); bad = 0
    for n in unlisted(): print("UNLISTED ", n, "carries the block but is not in SCRIPTS (add it, or remove the block)"); bad += 1
    for n in SCRIPTS:
        p = os.path.join(ROOT, "scripts", n + ".cs")
        if not os.path.exists(p): print("MISSING  ", n); continue
        if n in EXCLUDE: print("excluded ", n); continue
        with open(p, encoding="utf-8", newline="") as f: raw = f.read()
        crlf = "\r\n" in raw
        t = raw.replace("\r\n", "\n")
        i = t.find(BEGIN)
        if i >= 0:
            j = t.find(END, i); j = t.find("\n", j) + 1
            cur = t[i:j]
            if cur == b: print("ok       ", n); continue
            new = t[:i] + b + t[j:]
        else:
            if n not in REQUIRED and n not in add: print("absent   ", n, "(not integrated yet)"); continue
            m = re.search(r"^public Program\(\)", t, re.M)
            if not m: print("NO ANCHOR", n); bad += 1; continue
            new = t[:m.start()] + b + "\n" + t[m.start():]
        if not apply_: print("DIFFERS  " if i >= 0 else "MISSING  ", n); bad += 1; continue
        if crlf: new = new.replace("\n", "\r\n")
        with open(p, "w", encoding="utf-8", newline="") as f: f.write(new)
        print("updated  ", n)
    return 1 if bad else 0

sys.exit(main())
