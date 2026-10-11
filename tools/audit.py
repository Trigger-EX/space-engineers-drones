import sys, xml.etree.ElementTree as ET
sys.path.insert(0, sys.argv[1]); from sebp import Blueprint
XSI = '{http://www.w3.org/2001/XMLSchema-instance}type'
for path in sys.argv[2:]:
    r = ET.parse(path).getroot(); grid = r.findtext('.//GridSizeEnum')
    bp = Blueprint(path.split('/')[-2], grid)
    for b in r.findall('.//CubeBlocks/MyObjectBuilder_CubeBlock'):
        mn = b.find('Min'); o = b.find('BlockOrientation')
        bp.add(b.get(XSI), b.findtext('SubtypeName') or '', (int(mn.get('x')), int(mn.get('y')), int(mn.get('z'))),
               o.get('Forward'), o.get('Up'), name=b.findtext('CustomName') or b.findtext('SubtypeName'))
    # attachment check without stopping at the first problem
    rects = {id(b): bp.mount_rects(b) for b in bp.blocks}
    occ = {}
    for b in bp.blocks:
        for c in bp.cells(b): occ[c] = b
    bad = []
    for b in bp.blocks:
        ok = False
        for (axis, pc, ns, lo, hi, ex, pr) in rects[id(b)]:
            nbs = set()
            for c in bp.cells(b):
                for dd in (-1, 1):
                    q = list(c); q[axis] += dd; q = tuple(q)
                    if q in occ and occ[q] is not b: nbs.add(id(occ[q]))
            for nb in [x for x in bp.blocks if id(x) in nbs]:
                for (a2, pc2, ns2, lo2, hi2, ex2, pr2) in rects[id(nb)]:
                    if a2 == axis and pc2 == pc and ns2 == -ns and not (ex & pr2) and not (ex2 & pr):
                        ov = max(0, min(hi[0], hi2[0]) - max(lo[0], lo2[0])) * max(0, min(hi[1], hi2[1]) - max(lo[1], lo2[1]))
                        if ov > 1e-6: ok = True
        if not ok: bad.append(b['name'])
    print(f'{bp.name}: {len(bp.blocks)} blocks, {len(bad)} not attached:')
    for n in bad: print('     -', n)
