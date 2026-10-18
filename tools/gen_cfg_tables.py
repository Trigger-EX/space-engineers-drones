#!/usr/bin/env python3
"""One-off (flight_split P0): extract the config tables of the 9 drone Flights and the ShrikeAI Helm into tools/cfg_tables.json.

  python3 -I tools/gen_cfg_tables.py            write tools/cfg_tables.json
  python3 -I tools/gen_cfg_tables.py --check    regenerate and diff against tools/cfg_tables.json (exit 1 on a difference)
  options: --src DIR (read the scripts from DIR instead of scripts/: re-run it on the pre-split scripts, e.g. `git show 3e97ae9:scripts/X.cs`)

Why: docs/design/flight_split.md moves every Flight / Helm config read into the Staff (CONFIG SERVER) and the Flight gets the values back on .CFG
(CONFIG LINK). Both sides are generated from this table, so it had to be taken from the readers as they were BEFORE anyone edited them:
LoadConfig (the [<Model>] section), KzLoad ([Kamikaze Defence]), AiGateLoad ([AI Gate]) and SvLoad ([Survival]) in the Flights; LoadConfig,
ManCfg ([Manual]) and BatCfg ([Battery]) in ShrikeAI. Not extracted: HlkCfg ([Link], stays local in the Helm; its default is dynamic) and the
other blocks' own sections (FLEET LINK, LAUNCH, FOLLOW, SPLIT LINK ...), which flight_split does not move.

Every entry is [key, default, type, field, post] (order = the order in which the reader writes / reads the keys = the .CFG value order):
  key      the MyIni key
  default  the default the reader writes (the field initializer when the reader passes the field itself; a literal otherwise)
  type     d double, i int (read as a double and cast, or ToInt32), b bool, s string
  field    the field that receives the value; "local:x" when the reader stores it in a local x and derives fields from it (see "then")
  post     the expression around the raw value, "{v}" = the value read: "(int){v}", "Math.Max(30, {v})", "{v}.Trim().ToUpperInvariant()"; "" = none
An optional 6th element is a dict with extras: "then" (the statements that derive fields from a local), "local" (true: Tag / Start Mode, which
stay local Get-only reads per flight_split 6.1), "block" ([AI Gate] only: "core" = AI GATE core field, sent on .CFG by
AgSet4; "setup" = AI GATE SETUP field, sent to the Aux in the SDK cfg), "default_expr" (the C# initializer text when it is not a plain literal), "src" (the statement).
"shared" holds the sections that are identical in every script (synced blocks); the generator asserts that they are.
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'tools', 'cfg_tables.json')
FLIGHTS = [('Hornet', 'HornetDroneAI'), ('Harrier', 'HarrierAI'), ('Lancer', 'LancerAI'), ('Aegis', 'AegisAI'), ('Wasp', 'WaspFlightAI'),
           ('Longbow', 'LongbowSniperAI'), ('Gnat', 'GnatSwarmAI'), ('Basilisk', 'BasiliskAI'), ('Mantis', 'MantisAI')]
HELM = ('Shrike', 'ShrikeAI')
AGS_FIELDS = {'AiBehaviorSwitch', 'AiTyped', 'AiSpeed', 'AiSearchEvery'}   # AI GATE SETUP fields: go to the Aux in the SDK cfg, not on .CFG
LOCAL_FIELDS = {'Tag', 'StartMode'}   # stay local Get-only reads (flight_split 6.1)

# ---------------------------------------------------------------- C# scanning
def strip_comments(t):
    """Comments -> spaces (strings and chars kept), same length so offsets stay valid."""
    out = []; i = 0; n = len(t)
    while i < n:
        c = t[i]
        if t.startswith('//', i):
            j = t.find('\n', i); j = n if j < 0 else j; out.append(' ' * (j - i)); i = j
        elif t.startswith('/*', i):
            j = t.find('*/', i + 2) + 2; out.append(re.sub(r'[^\n]', ' ', t[i:j])); i = j
        elif c == '"' or (c == '@' and i + 1 < n and t[i + 1] == '"'):
            j = i + (2 if c == '@' else 1); verb = c == '@'
            while True:
                if verb and t[j] == '"':
                    if t[j + 1:j + 2] == '"': j += 2; continue
                    break
                if not verb and t[j] == '\\': j += 2; continue
                if t[j] == '"': break
                j += 1
            out.append(t[i:j + 1]); i = j + 1
        elif c == "'":
            j = i + 1
            while t[j] != "'": j += 2 if t[j] == '\\' else 1
            out.append(t[i:j + 1]); i = j + 1
        else:
            out.append(c); i += 1
    return ''.join(out)

def mask_strings(t):
    """String / char contents -> 'x' (for brace / paren matching)."""
    return re.sub(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])+\'', lambda m: m.group(0)[0] + 'x' * (len(m.group(0)) - 2) + m.group(0)[-1], t)

def class_statements(code):
    """Top-level (class body) statements ending in ';' (fields), with depth tracking; method / class bodies are skipped."""
    m = mask_strings(code); out = []; depth = 0; start = 0; i = 0
    while i < len(m):
        c = m[i]
        if c == '{':
            if depth == 0: start = None
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0: start = i + 1
        elif c == ';' and depth == 0:
            s = code[start:i] if start is not None else ''
            out.append(s.strip()); start = i + 1
        i += 1
    return out

def split_top(s, sep=','):
    m = mask_strings(s); parts = []; d = 0; last = 0
    op, cl = ('([{<', ')]}>') if sep == ',' else ('([{', ')]}')   # (no <> for statements: comparisons)
    for i, c in enumerate(m):
        if c in op: d += 1
        elif c in cl: d -= 1
        elif c == sep and d == 0: parts.append(s[last:i]); last = i + 1
    parts.append(s[last:]); return [p.strip() for p in parts]

FIELD_T = r'(?:double|float|int|long|bool|string)'
def fields(code):
    """name -> (type, initializer text or None) for every class-level field of a simple type."""
    out = {}
    for st in class_statements(strip_comments(code)):
        st = re.sub(r'^(?:(?:public|private|static|readonly|const)\s+)+', '', st)
        m = re.match(r'^(' + FIELD_T + r')\s+([A-Za-z_]\w*\s*(?:=|,|$).*)$', st, re.S)
        if not m or '=>' in st or '(' in st.split('=')[0]: continue
        typ = m.group(1)
        for d in split_top(m.group(2)):
            dm = re.match(r'^([A-Za-z_]\w*)\s*(?:=\s*(.+))?$', d, re.S)
            if dm and dm.group(1) not in out: out[dm.group(1)] = (typ, dm.group(2).strip() if dm.group(2) else None)
    return out

def literal(expr, flds, typ, depth=0):
    """C# initializer -> JSON value, or raise ValueError."""
    if expr is None: return {'string': '', 'bool': False}.get(typ, 0)
    e = expr.strip()
    if re.fullmatch(r'"(?:\\.|[^"\\])*"', e): return json.loads(e)
    if e in ('true', 'false'): return e == 'true'
    if e in flds and depth < 5: return literal(flds[e][1], flds, flds[e][0], depth + 1)
    py = re.sub(r'(?<=\d)[fFdDmMlL]\b', '', e)
    if not re.fullmatch(r'[\d\s.eE+\-*/()]+', py): raise ValueError(e)
    v = eval(py, {'__builtins__': {}})
    if typ in ('int', 'long'): return int(v)
    v = float(v); return int(v) if v.is_integer() and typ != 'float' and abs(v) < 1e15 else v

def func_body(code, name):
    """Body text of the (first) method `name` declared at class level, or None."""
    m = re.search(r'^[ \t]*[\w<>\[\]]+\s+' + re.escape(name) + r'\s*\([^)]*\)\s*\{', code, re.M)
    if not m: return None
    mk = mask_strings(strip_comments(code)); i = m.end(); d = 1
    while d: d += {'{': 1, '}': -1}.get(mk[i], 0); i += 1
    return code[m.end():i - 1]

def call_args(s, at):
    """s[at] == '(' -> (list of args, index after ')')."""
    mk = mask_strings(s); d = 0; i = at
    while True:
        d += {'(': 1, ')': -1}.get(mk[i], 0)
        if d == 0: break
        i += 1
    return split_top(s[at + 1:i]), i + 1

# ---------------------------------------------------------------- readers
TYPEMAP = {'double': 'd', 'float': 'd', 'int': 'i', 'long': 'i', 'bool': 'b', 'string': 's'}

def entry_type(default_expr, field, flds, post):
    if default_expr.startswith('(double)'): return 'i' if '(int)' in post else 'd'
    if re.fullmatch(r'"(?:\\.|[^"\\])*"', default_expr): return 's'
    if default_expr in ('true', 'false'): return 'b'
    if default_expr in flds:
        t = TYPEMAP[flds[default_expr][0]]
        return 'i' if '(int)' in post else ('d' if t == 'i' else t)   # an int field read through Cfg(double) without a cast cannot occur (C# would not compile)
    if re.fullmatch(r'[\d.eE+\-]+[fFdD]?', default_expr): return 'i' if '(int)' in post else 'd'
    return 's' if '.ToString(' in default_expr else 'd'

def mk_entry(key, default_expr, field, post, flds, src, extra=None):
    typ = entry_type(default_expr, field, flds, post)
    de = default_expr[len('(double)'):] if default_expr.startswith('(double)') else default_expr
    x = dict(extra or {})
    try:
        dv = literal(de, flds, flds[de][0] if de in flds else {'s': 'string', 'b': 'bool', 'i': 'int'}.get(typ, 'double'))
        if de in flds and flds[de][1] is not None and not re.fullmatch(r'"(?:\\.|[^"\\])*"|true|false|-?[\d.]+', flds[de][1].strip()):
            x['default_expr'] = flds[de][1].strip()
    except (ValueError, SyntaxError, KeyError):
        dv = None; x['default_expr'] = (flds[de][1] if de in flds else de)
    if typ == 'i' and isinstance(dv, float) and dv.is_integer(): dv = int(dv)
    if typ == 'd' and isinstance(dv, int) and not isinstance(dv, bool): dv = dv   # JSON keeps 5, the type says double
    if field in LOCAL_FIELDS: x['local'] = True
    x['src'] = re.sub(r'\s+', ' ', src.strip())
    e = [key, dv, typ, field, post]
    if x: e.append(x)
    return e

def statements(body):
    return [s for s in split_top(strip_comments(body), ';') if s.strip()]

def read_cfg_calls(body, flds, helper_names):
    """Readers of the form  X = post(Helper(S, "key", default))  (LoadConfig / ManCfg / BatCfg with Cfg, KzLoad with KzD / KzB)."""
    out = []; consts = dict(re.findall(r'const string (\w+) = "([^"]*)"', body)); last_local = None; sect = None
    for st in statements(body):
        st1 = re.sub(r'\s+', ' ', st.strip())
        m = re.search(r'\b(' + '|'.join(helper_names) + r')\(', st1)
        if not m:
            if last_local and re.search(r'\b' + re.escape(last_local[3][6:]) + r'\b', st1):
                last_local[5].setdefault('then', []).append(st1)   # (mk_entry always adds the extras dict: it holds "src")
            continue
        args, end = call_args(st1, m.end() - 1)
        args = [a for a in args if not re.fullmatch(r'ini|i|_ini', a)]   # KzD(ini, S, k, d) -> (S, k, d)
        sec_arg, key_arg, def_arg = args
        sec = consts.get(sec_arg, sec_arg.strip('"')); key = json.loads(key_arg)
        lhs, rhs = st1.split(' = ', 1) if ' = ' in st1 else (None, st1)
        call = st1[m.start():end]
        post = rhs.replace(call, '{v}', 1)
        if post == '{v}': post = ''
        lm = re.match(r'^(?:string|double|bool|int|var) (\w+)$', lhs or '')
        field = 'local:' + lm.group(1) if lm else lhs
        e = mk_entry(key, def_arg, field, post, flds, st1)
        e_sec = sec
        out.append(e); last_local = e if lm else None
        if sect is None: sect = e_sec
        elif sect != e_sec: raise SystemExit(f'reader mixes sections {sect} / {e_sec}: {st1}')
    return sect, out

def read_ai_gate(body, flds):
    """AiGateLoad: ini.Set(S, k, Field) for the defaults (write order), Field = post(ini.Get(S, k).ToX(Field)) for the parse."""
    sets = re.findall(r'ini\.Set\(S, ("(?:\\.|[^"\\])*"), (\w+)\)', body)
    gets = {}
    for st in statements(body):
        st1 = re.sub(r'\s+', ' ', st.strip())
        m = re.match(r'^(\w+) = (.*ini\.Get\(S, ("(?:\\.|[^"\\])*")\)\.To(\w+)\((\w+)\).*)$', st1)
        if m:
            call = re.search(r'ini\.Get\(S, "(?:\\.|[^"\\])*"\)\.To\w+\(\w+\)', m.group(2)).group(0)
            post = m.group(2).replace(call, '{v}', 1)
            gets[json.loads(m.group(3))] = (m.group(1), '' if post == '{v}' else post, m.group(4), st1)
    out = []
    for k, f in sets:
        k = json.loads(k)
        if k not in gets: raise SystemExit(f'PROBLEM AiGateLoad writes the default of "{k}" but does not parse it: extend read_ai_gate')
        fld, post, conv, st1 = gets[k]
        assert fld == f, (k, fld, f)
        e = mk_entry(k, f, fld, post, flds, st1, {'block': 'setup' if fld in AGS_FIELDS else 'core'})
        if conv == 'Int32': e[2] = 'i'
        out.append(e)
    assert len(out) == len(gets), 'AiGateLoad: Set and Get keys differ'
    return 'AI Gate', out

def read_survival(code, flds):
    """SvLoad(MyIni i): Field = i.Get("Survival", k).ToBoolean(lit) and Field = SvK(k, lit)."""
    body = func_body(code, 'SvLoad'); out = []
    for st in statements(body):
        for a in split_top(st, ';'):
            for m in re.finditer(r'(\w+) = (?:i\.Get\("Survival", ("(?:\\.|[^"\\])*")\)\.To(\w+)\(([^)]*)\)|SvK\(("(?:\\.|[^"\\])*"), ([^)]*)\))', a):
                if m.group(2): e = mk_entry(json.loads(m.group(2)), m.group(4).strip(), m.group(1), '', flds, m.group(0))
                else: e = mk_entry(json.loads(m.group(5)), m.group(6).strip(), m.group(1), '', flds, m.group(0))
                out.append((m.start() + body.find(a), e))
    return 'Survival', [e for _, e in sorted(out, key=lambda x: x[0])]

# ---------------------------------------------------------------- self-check: every key read in the source is in the table
KEY_CALL = re.compile(r'\b(?:Cfg|KzD|KzB|SvK)\(\s*(?:ini,\s*)?(?:(?:\w+|"[^"]*")\s*,\s*)?("(?:\\.|[^"\\])*")|'
                      r'\bini\.(?:Set|Get|ContainsKey)\(S, ("(?:\\.|[^"\\])*")|'
                      r'\bi\.Get\("Survival", ("(?:\\.|[^"\\])*")')

def source_keys(code, funcs):
    keys = set()
    for f in funcs:
        b = func_body(code, f)
        if b is None: continue
        for m in KEY_CALL.finditer(strip_comments(b)):
            keys.add(json.loads(next(g for g in m.groups() if g)))
    return keys

def stray_cfg_calls(code, funcs):
    """Cfg(...) calls outside the extracted readers (a reader the generator would miss)."""
    c = strip_comments(code); out = []
    spans = []
    for f in funcs:
        m = re.search(r'^[ \t]*[\w<>\[\]]+\s+' + f + r'\s*\(', c, re.M)
        if m: b = func_body(c, f); spans.append((m.start(), m.start() + len(b) + (m.end() - m.start()) + 2))
    for m in re.finditer(r'\bCfg\(\s*(?:\w+|"[^"]*")\s*,\s*"', c):
        if not any(a <= m.start() <= b for a, b in spans): out.append(c[m.start():c.find(')', m.start()) + 1])
    return out

# ---------------------------------------------------------------- main
def model_tables(code, helm):
    flds = fields(code); secs = {}
    sect, ent = read_cfg_calls(func_body(code, 'LoadConfig'), flds, ['Cfg']); secs[sect] = ent
    if helm:
        for f in ('ManCfg', 'BatCfg'):
            s, e = read_cfg_calls(func_body(code, f), flds, ['Cfg']); secs[s] = e
    s, e = read_cfg_calls(func_body(code, 'KzLoad'), flds, ['KzD', 'KzB']); secs[s] = e
    s, e = read_ai_gate(func_body(code, 'AiGateLoad'), flds); secs[s] = e
    if not helm: s, e = read_survival(code, flds); secs[s] = e
    return secs

def generate(src):
    shared_names = ('Kamikaze Defence', 'AI Gate', 'Survival')
    doc = {'_doc': 'Generated by tools/gen_cfg_tables.py (flight_split P0) from the pre-split readers; see its docstring for the entry format. '
                   'CONFIG SERVER (Staff) and CONFIG LINK (Flight / Helm CfgIn) are generated from / tested against this file (flight_split.md 9.3c). '
                   'Section order inside a model = the .CFG line order: M (the model section; Helm: its sections in this order), K, A, S.',
           'version': 1, 'shared': {}, 'models': {}}
    problems = []
    for model, script in FLIGHTS + [HELM]:
        code = open(os.path.join(src, script + '.cs'), encoding='utf-8').read()
        helm = (model, script) == HELM
        secs = model_tables(code, helm)
        entry = {'script': script, 'pb': 'Helm' if helm else 'Flight', 'sections': {}}
        for s, e in secs.items():
            if s in shared_names:
                if s not in doc['shared']: doc['shared'][s] = e
                elif [x[:5] for x in doc['shared'][s]] != [x[:5] for x in e]: problems.append(f'{script}: shared section [{s}] differs from the first script')
                entry['sections'][s] = 'shared'
            else:
                entry['sections'][s] = e
        # self-check: every key the readers touch is in the table; no Cfg(...) outside the extracted readers (except the known ones)
        funcs = ['LoadConfig', 'KzLoad', 'AiGateLoad', 'SvLoad'] + (['ManCfg', 'BatCfg'] if helm else [])
        have = set()
        for s, e in secs.items(): have |= {x[0] for x in e}
        miss = source_keys(code, funcs) - have
        if miss: problems.append(f'{script}: keys read in the source but not in the table: {sorted(miss)}')
        stray = [c for c in stray_cfg_calls(code, funcs + ['Cfg', 'HlkCfg'])]
        if stray: problems.append(f'{script}: Cfg(...) outside the extracted readers: {stray}')
        # a table field the constructor sets before LoadConfig() runs would make the initializer the wrong default
        ctor = strip_comments(func_body(code, 'Program') or ''); ctor = ctor[:ctor.find('LoadConfig()')] if 'LoadConfig()' in ctor else ''
        pre = sorted({x[3] for e in secs.values() for x in e if not x[3].startswith('local:') and re.search(r'\b' + x[3] + r'\s*=[^=]', ctor)})
        if pre: problems.append(f'{script}: the constructor sets table fields before LoadConfig(): {pre}')
        unres = [f'[{s}] {x[0]}' for s, e in secs.items() for x in e if x[1] is None]
        if unres: problems.append(f'{script}: unresolved defaults: {unres}')
        entry['counts'] = {s: len(e) for s, e in secs.items()}
        doc['models'][model] = entry
    return doc, problems

def dump(doc):
    """One entry per line (diffable)."""
    def ent(e): return json.dumps(e, ensure_ascii=False)
    L = ['{', f' "_doc": {json.dumps(doc["_doc"])},', f' "version": {doc["version"]},', ' "shared": {']
    sh = list(doc['shared'].items())
    for k, (s, es) in enumerate(sh):
        L.append(f'  {json.dumps(s)}: [')
        L += [f'   {ent(e)}' + (',' if i < len(es) - 1 else '') for i, e in enumerate(es)]
        L.append('  ]' + (',' if k < len(sh) - 1 else ''))
    L += [' },', ' "models": {']
    ms = list(doc['models'].items())
    for k, (m, d) in enumerate(ms):
        L.append(f'  {json.dumps(m)}: {{"script": {json.dumps(d["script"])}, "pb": {json.dumps(d["pb"])}, "counts": {json.dumps(d["counts"])}, "sections": {{')
        ss = list(d['sections'].items())
        for j, (s, es) in enumerate(ss):
            if es == 'shared': L.append(f'   {json.dumps(s)}: "shared"' + (',' if j < len(ss) - 1 else '')); continue
            L.append(f'   {json.dumps(s)}: [')
            L += [f'    {ent(e)}' + (',' if i < len(es) - 1 else '') for i, e in enumerate(es)]
            L.append('   ]' + (',' if j < len(ss) - 1 else ''))
        L.append('  }}' + (',' if k < len(ms) - 1 else ''))
    L += [' }', '}']
    return '\n'.join(L) + '\n'

def main(argv):
    src = os.path.abspath(argv[argv.index('--src') + 1]) if '--src' in argv else os.path.join(ROOT, 'scripts')
    doc, problems = generate(src)
    for p in problems: print('PROBLEM', p)
    text = dump(doc)
    assert json.loads(text) == json.loads(json.dumps(doc)), 'dump() lost data'
    for m, d in doc['models'].items(): print(f'{m:<9} {d["script"]:<16} ' + ', '.join(f'{s} {n}' for s, n in d['counts'].items()))
    if '--check' in argv:
        old = open(OUT, encoding='utf-8').read() if os.path.exists(OUT) else ''
        same = old == text
        print('cfg_tables.json: ' + ('identical to a fresh extraction' if same else 'DIFFERS from a fresh extraction'))
        return 0 if same and not problems else 1
    if problems: print('not written: fix the problems first'); return 1
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f: f.write(text)
    print('wrote', os.path.relpath(OUT, ROOT))
    return 0

if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
