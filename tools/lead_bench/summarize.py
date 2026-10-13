import re,collections,sys
for fn in sys.argv[1:]:
    hdr=None; tab=collections.OrderedDict(); cols=[]
    for line in open(fn):
        if not line.startswith('  '):
            w=line.split(); hdr=' '.join(w[:4]+(['t'+w[w.index('truth')+1]] if 'truth' in w else [])); cols.append(hdr); continue
        m=re.match(r'\s+(.*?)\s+shots\s+\d+\s+hit\s+([\d.]+)%',line)
        tab.setdefault(m.group(1),{})[hdr]=float(m.group(2))
    print(fn)
    for name,d in tab.items():
        vals=[d.get(c) for c in cols]; vv=[v for v in vals if v is not None]
        print(f"  {name:32s} avg {sum(vv)/max(1,len(vv)):5.1f}  "+' '.join(f"{v:4.0f}" if v is not None else "   -" for v in vals))
    print('  cols:',' | '.join(cols))
