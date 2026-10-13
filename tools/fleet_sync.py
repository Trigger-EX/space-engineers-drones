#!/usr/bin/env python3
"""Keep the FLEET LINK block (tools/fleet_block.cs) identical in every drone script and carrier relay.

  python3 -I tools/fleet_sync.py --check   exit 1 if any script has a missing/different block
  python3 -I tools/fleet_sync.py --apply   replace the block (or insert it before 'public Program()') in every script
Hooks that call the block (FleetLoad / FleetPump / FleetSend / FleetTgtL) are NOT touched; see docs/hive-protocol.md section 7.
"""
import os, re, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = ["HornetDroneAI", "GnatSwarmAI", "LongbowSniperAI", "WaspAI", "LancerAI", "HarrierAI", "AegisAI", "MantisAI",
           "BasiliskAI", "ShrikeAI", "StaffAI", "StingerAI", "HornetMothership", "GnatMothership", "LongbowMothership"]
BEGIN, END = "// >>> FLEET LINK", "// <<< FLEET LINK <<<"

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
    with open(os.path.join(ROOT, "tools", "fleet_block.cs"), encoding="utf-8", newline="") as f:
        return f.read().replace("\r\n", "\n").rstrip("\n") + "\n"

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
            cur = t[i:j]
            if cur == b: print("ok      ", n); continue
            new = t[:i] + b + t[j:]
        else:
            m = re.search(r"^public Program\(\)", t, re.M)
            if not m: print("NO ANCHOR", n); bad += 1; continue
            new = t[:m.start()] + b + "\n" + t[m.start():]
        if not apply_: print("DIFFERS ", n); bad += 1; continue
        if crlf: new = new.replace("\n", "\r\n")
        with open(p, "w", encoding="utf-8", newline="") as f: f.write(new)
        print("updated ", n)
    return 1 if bad else 0

sys.exit(main())
