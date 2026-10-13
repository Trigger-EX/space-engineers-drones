#!/usr/bin/env python3
"""Whitespace/comment minifier for in-game scripts: the Programmable Block accepts at most 100,000 characters.

  python3 -I tools/minify_cs.py scripts/HornetDroneAI.cs [...]   print original -> minified sizes
  from minify_cs import minify; small = minify(text)

Only comments and whitespace are removed; identifiers, literals and the token order are untouched, so the minified script is the
same program (checked by compiling both: tools/minify_check.sh). Strings ("...", @"...", $"..." with nested code), char literals,
and comments are tokenised properly. A space is kept between two tokens only where removing it would join them into a different
token (identifier/number/keyword runs, or operator characters that would form a new operator such as "+ +", "- -", "/ /", "< <").
"""
import sys

OPS = set("+-*/%<>=!&|^?:~")
WORD = lambda c: c.isalnum() or c == '_' or c == '@' or ord(c) > 127


def _scan_string(t, i):
    """t[i] starts a string or char literal (", ', @", $", $@", @$"). Returns the end index (exclusive)."""
    j = i
    verbatim = interp = False
    while t[j] in '@$':
        if t[j] == '@': verbatim = True
        else: interp = True
        j += 1
    q = t[j]; j += 1
    while True:
        c = t[j]
        if verbatim and c == q:
            if j + 1 < len(t) and t[j + 1] == q: j += 2; continue
            return j + 1
        if not verbatim and c == '\\': j += 2; continue
        if c == q: return j + 1
        if interp and c == '{':
            if t[j + 1] == '{': j += 2; continue
            # code inside the hole: skip balanced braces, strings inside
            depth = 1; j += 1
            while depth:
                c = t[j]
                if c in '"\'' or (c in '@$' and j + 1 < len(t) and t[j + 1] in '"$@'):
                    j = _scan_string(t, j); continue
                if c == '{': depth += 1
                elif c == '}': depth -= 1
                j += 1
            continue
        if interp and c == '}' and t[j + 1] == '}': j += 2; continue
        if c == '\n' and not verbatim: raise ValueError("newline in string at %d" % i)
        j += 1


def tokens(t):
    i, n = 0, len(t)
    out = []
    while i < n:
        c = t[i]
        if c in ' \t\r\n': i += 1; out.append(None); continue          # None = "there was whitespace here"
        if t.startswith('//', i):
            j = t.find('\n', i); i = n if j < 0 else j; out.append(None); continue
        if t.startswith('/*', i):
            j = t.find('*/', i + 2); i = j + 2; out.append(None); continue
        if c == '#': raise ValueError("preprocessor directive at %d: not supported" % i)
        if c in '"\'' or (c in '@$' and i + 1 < n and (t[i + 1] in '"' or (t[i + 1] in '@$' and i + 2 < n and t[i + 2] == '"'))):
            j = _scan_string(t, i); out.append(t[i:j]); i = j; continue
        if WORD(c):
            j = i
            while j < n and (WORD(t[j]) or (t[j] == '.' and j > i and t[i].isdigit() and j + 1 < n and t[j + 1].isdigit())):
                j += 1
            out.append(t[i:j]); i = j; continue
        out.append(c); i += 1
    return out


def minify(t):
    toks = tokens(t.replace('\r\n', '\n'))
    res = []
    prev = None; gap = False
    for k in toks:
        if k is None: gap = True; continue
        if prev is not None and gap:
            a, b = prev[-1], k[0]
            need = (WORD(a) and WORD(b)) or (a in OPS and b in OPS) or (a == '.' and b.isdigit()) or (a.isdigit() and b == '.')
            if need: res.append(' ')
        elif prev is not None and not gap:
            pass
        res.append(k); prev = k; gap = False
    # break into lines of ~200 chars at statement ends (the in-game editor copes badly with one giant line)
    s = ''.join(res)
    out, line = [], []
    ln = 0
    for k in _split_keep(s):
        line.append(k); ln += len(k)
        if ln > 200 and k in (';', '{', '}'):
            out.append(''.join(line)); line = []; ln = 0
    if line: out.append(''.join(line))
    return '\n'.join(out) + '\n'


def _split_keep(s):
    """split the minified text into tokens again (strings intact) so line breaks never land inside a literal"""
    for k in tokens(s):
        if k is not None: yield k
        else: yield ' '


if __name__ == '__main__':
    for p in sys.argv[1:]:
        t = open(p, encoding='utf-8').read()
        m = minify(t)
        print(f"{p}: {len(t)} -> {len(m)} chars" + ("  OVER 100000" if len(m) > 100000 else ""))
