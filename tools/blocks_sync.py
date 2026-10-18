#!/usr/bin/env python3
"""Keep the shared script blocks identical in every script that carries them, driven by tools/blocks.json.

  python3 -I tools/blocks_sync.py --check          exit 1 if any block is missing/different/unlisted; also runs the legacy tools/*_sync.py --check
  python3 -I tools/blocks_sync.py --apply          write the block into every listed script (replace, or insert before the anchor line)
  python3 -I tools/blocks_sync.py --list           print blocks and their scripts
  options: --root DIR (repo root; tests use a temp tree), --no-legacy (skip the legacy sync scripts),
           --only "NAME[,NAME]" (only these blocks, no legacy scripts: parallel work on different blocks applies only its own)
           --in "SCRIPT[,SCRIPT]" (only these scripts: a package writes only the scripts it owns; also the only way to insert into an "arriving" host)
           --final (ignore the "leaving"/"arriving" transition lists: the strict check of the final host lists)

blocks.json entry:  "MANUAL": {"file": "tools/manual_block.cs", "scripts": ["ShrikeAI", "HelmAI"], "anchor": "^public Program\\\\(\\\\)"}
Transition lists (docs/design/flight_split.md "P0 handover"): "scripts" is the FINAL host list. Optional "leaving": scripts that still carry the
block until a package removes it by hand (never UNLISTED; checked, but a stale copy is only a note; --apply never writes them). Optional "arriving":
hosts in "scripts" that do not carry it yet (absent = "pending", not a problem; --apply inserts only into hosts named with --in). Phase 2 deletes both lists.
The block text in `file` starts with a line containing '// >>> MANUAL v<version>' and ends with '// <<< MANUAL v<version>'.
Why one driver: the seven older *_sync.py hard-code their script lists, so a new script (or a new shared block) was silently skipped until somebody
remembered to edit seven files.  UNLISTED is reported when a script carries a block marker but is not in that block's list.
"""
import json, os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))

def load(root):
    with open(os.path.join(root, 'tools', 'blocks.json'), encoding='utf-8') as f:
        return json.load(f)

def read_text(path):
    with open(path, encoding='utf-8', newline='') as f:
        return f.read()

def block_text(root, spec):
    return read_text(os.path.join(root, spec['file'])).replace('\r\n', '\n').rstrip('\n') + '\n'

def markers(name):
    return f'// >>> {name} v', f'// <<< {name} v'

def unlisted(root, name, scripts):
    b, _ = markers(name)
    d = os.path.join(root, 'scripts'); out = []
    for f in sorted(os.listdir(d)):
        if f.endswith('.cs') and f[:-3] not in scripts and b in read_text(os.path.join(d, f)): out.append(f[:-3])
    return out

def sync_one(root, name, spec, apply_, only_in=None, final=False):
    """-> number of problems"""
    bad = 0
    leaving = [] if final else spec.get('leaving', [])
    arriving = [] if final else spec.get('arriving', [])
    want = block_text(root, spec)
    b, e = markers(spec.get('name', name))
    if want.count(b) != 1 or want.count(e) != 1:
        print(f'BAD BLOCK {name}: {spec["file"]} must contain exactly one "{b}" and one "{e}" line'); return 1
    anchor = re.compile(spec.get('anchor', r'^public Program\(\)'), re.M)
    for n in unlisted(root, spec.get('name', name), spec['scripts'] + leaving):
        if only_in is None or n in only_in: print(f'UNLISTED  {name}: {n} carries the block but is not in its scripts list'); bad += 1
    for n in spec['scripts'] + leaving:
        if only_in is not None and n not in only_in: continue
        p = os.path.join(root, 'scripts', n + '.cs')
        if not os.path.exists(p): print(f'MISSING   {name}: scripts/{n}.cs does not exist'); bad += 1; continue
        raw = read_text(p); crlf = '\r\n' in raw
        t = raw.replace('\r\n', '\n')
        i = t.find(b)
        if n in leaving:
            if i < 0: print(f'gone      {name} {n} (leaving: removed)'); continue
            i = t.rfind('\n', 0, i) + 1; j = t.find(e, i); j = t.find('\n', j) + 1 if j >= 0 else -1
            print(f'ok        {name} {n} (leaving)' if j > 0 and t[i:j] == want else f'note      {name} {n}: stale copy (leaving: a phase-1 package removes it; never written)')
            continue
        if i >= 0:
            i = t.rfind('\n', 0, i) + 1                       # the marker line starts here
            j = t.find(e, i); j = t.find('\n', j) + 1 if j >= 0 else -1
            if j <= 0: print(f'BROKEN    {name}: {n} has the begin marker but no end marker'); bad += 1; continue
            if t[i:j] == want: print(f'ok        {name} {n}'); continue
            new = t[:i] + want + t[j:]
        else:
            if n in arriving and not (apply_ and only_in is not None):
                print(f'pending   {name} {n} (arriving: inserted with --apply --only "{name}" --in {n})'); continue
            m = anchor.search(t)
            if not m: print(f'NO ANCHOR {name}: {n}'); bad += 1; continue
            new = t[:m.start()] + want + '\n' + t[m.start():]
        if not apply_: print(f'DIFFERS   {name} {n}'); bad += 1; continue
        if crlf: new = new.replace('\n', '\r\n')
        with open(p, 'w', encoding='utf-8', newline='') as f: f.write(new)
        print(f'updated   {name} {n}')
    return bad

def main(argv):
    root = os.path.normpath(os.path.join(HERE, '..'))
    if '--root' in argv: root = os.path.abspath(argv[argv.index('--root') + 1])
    cfg = load(root)
    if '--list' in argv:
        for name, spec in cfg['blocks'].items():
            print(f'{name:<12} {spec["file"]}  ->  {", ".join(spec["scripts"])}'
                  + (f'  | arriving: {", ".join(spec["arriving"])}' if spec.get('arriving') else '')
                  + (f'  | leaving: {", ".join(spec["leaving"])}' if spec.get('leaving') else ''))
        print('legacy: ' + ', '.join(cfg.get('legacy', [])))
        return 0
    apply_ = '--apply' in argv
    only = [s.strip() for s in argv[argv.index('--only') + 1].split(',')] if '--only' in argv else None
    only_in = [s.strip() for s in argv[argv.index('--in') + 1].split(',')] if '--in' in argv else None
    final = '--final' in argv
    for n in only or []:
        if n not in cfg['blocks']: print(f'--only: no block named {n}'); return 2
    bad = 0
    for name, spec in cfg['blocks'].items():
        if only is None or name in only: bad += sync_one(root, name, spec, apply_, only_in, final)
    if '--no-legacy' not in argv and only is None and only_in is None:
        for s in cfg.get('legacy', []):
            path = os.path.join(root, 'tools', s)
            if not os.path.exists(path): continue
            r = subprocess.run([sys.executable, '-I', path, '--apply' if apply_ else '--check'], capture_output=True, text=True)
            lines = [l for l in (r.stdout + r.stderr).splitlines() if not l.startswith('ok')]
            print(f'{"ok" if r.returncode == 0 else "FAIL"}        legacy {s}' + ('' if r.returncode == 0 else '\n' + '\n'.join('    ' + l for l in lines[:8])))
            if r.returncode: bad += 1
    print('blocks_sync: ' + ('all blocks identical' if not bad else f'{bad} problem(s)'))
    return 1 if bad else 0

if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
