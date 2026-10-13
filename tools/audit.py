"""Audit blueprints for unattached blocks using the verified mount-point convention in sebp.py.

  python3 -I tools/audit.py [--types] [--list] bp.sbc [bp.sbc ...]
  python3 -I tools/audit.py --controls reference.sbc                   # negative controls: wrong conventions must break it
  python3 -I tools/audit.py --shape generated.sbc [reference.sbc]      # XML shape diff vs a game-written blueprint

Reads game-written or generated blueprints (every CubeGrid incl. sub-grids), runs the same attachment test the
game uses (MyCubeGrid.CheckMountPointsForSide) and prints, per block type, how many blocks are attached to at
least one neighbour.  Grids with a single block are reported but not counted as failures (nothing to attach to).
(A leading directory argument is accepted and ignored for compatibility with the old `audit.py <tools> <bp>` form.)
"""
import sys, os, collections
import xml.etree.ElementTree as ET
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sebp

def audit(path, show_types=False, list_bad=False, out=print):
    grids = sebp.load_sbc(path)
    per = collections.defaultdict(lambda: [0, 0])          # "Type/Subtype" -> [blocks, attached]
    bad_list = []; skipped = collections.Counter(); pieces = []
    for bp in grids:
        for x in bp.skipped: skipped[(x[0], x[1], x[2])] += 1
        if not bp.blocks: continue
        try: occ = bp.occupancy()
        except AssertionError: occ = bp.occupancy(allow_overlap=True)
        adj = bp.attachment(occ)
        comps = bp.components(adj)
        pieces.append((bp.name, bp.grid, len(bp.blocks), len(comps), max(len(c) for c in comps)))
        for b in bp.blocks:
            k = b['key']; per[k][0] += 1
            ok = bool(adj[id(b)]) or len(bp.blocks) == 1
            per[k][1] += ok
            if not ok: bad_list.append((bp.name, k, b['min'], b['fwd'], b['up']))
    tot = sum(v[0] for v in per.values()); att = sum(v[1] for v in per.values())
    out(f'{path}: {len(grids)} grids, {tot} validated blocks, {att} attached ({100.0*att/max(1,tot):.2f}%), '
        f'{sum(skipped.values())} skipped')
    for name, grid, n, nc, big in pieces:
        out(f'   grid "{name}" ({grid}): {n} blocks in {nc} connected piece(s), largest {big}')
    if show_types:
        out('   per block type (attached/total):')
        for k, (n, a) in sorted(per.items(), key=lambda kv: (kv[1][1]/kv[1][0], -kv[1][0])):
            out(f'     {k:60s} {a:5d}/{n:<5d} {100.0*a/n:6.1f}%' + ('' if a == n else '   <-- FAIL'))
    if skipped:
        out('   skipped (not validated):')
        for (xt, sub, why), c in skipped.most_common(): out(f'     {c:5d}  {xt}/{sub}: {why}')
    if list_bad or not show_types:
        for g, k, mn, f, u in bad_list: out(f'   NOT ATTACHED: grid "{g}" {k} @ {mn} fwd={f} up={u}')
    return per, bad_list, skipped

XSI = sebp.XSI + 'type'

def _block_tags(path, skip_nested=True):
    """xsi:type -> list of child-tag sequences, over the top-level CubeBlocks of every grid"""
    seqs = collections.defaultdict(list)
    for g in ET.parse(path).getroot().iter('CubeGrid'):
        for b in g.find('CubeBlocks'):
            seqs[b.get(XSI)].append([c.tag for c in b])
    return seqs

# tags the game omits when they hold their default value (ShouldSerialize*) -> never "missing"
OPTIONAL = {'EntityId', 'Min', 'IntegrityPercent', 'BuildPercent', 'BlockOrientation', 'ColorMaskHSV', 'SkinSubtypeId',
            'ConstructionInventory', 'ConstructionStockpile', 'Owner', 'BuiltBy', 'CustomName', 'ComponentContainer',
            'PreviousControlledEntityId', 'AttachedPB', 'HudText', 'ConnectedEntityId'}

def shape_diff(gen_path, ref_path, out=print):
    gen, ref = _block_tags(gen_path), _block_tags(ref_path)
    bad = 0
    for t in sorted(gen):
        gs = gen[t]
        if t not in ref:
            out(f'  {t.replace("MyObjectBuilder_", ""):32s} not in reference (shape is from source/guess)'); continue
        rs = ref[t]
        must = set.intersection(*[set(x) for x in rs]) - OPTIONAL
        union = set().union(*[set(x) for x in rs])
        before = set(); both = set()
        for seq in rs:
            for i, a in enumerate(seq):
                for b in seq[i+1:]: before.add((a, b)); both.add(frozenset((a, b)))
        probs = []
        for seq in gs[:1]:
            miss = sorted(must - set(seq)); extra = sorted(set(seq) - union - OPTIONAL)
            if miss: probs.append('missing ' + ','.join(miss))
            if extra: probs.append('not in reference ' + ','.join(extra))
            for i, a in enumerate(seq):
                for b in seq[i+1:]:
                    if (b, a) in before and (a, b) not in before: probs.append(f'order {a} before {b} (reference has {b} first)')
        bad += bool(probs)
        out(f'  {t.replace("MyObjectBuilder_", ""):32s} ' + ('OK' if not probs else '; '.join(probs[:6])))
    return bad

def controls(path, out=print):
    """Is the validator discriminating?  Re-run the connectivity test with deliberately wrong face conventions and
    random block orientations; a game-written ship must stay ONE piece per grid only under the verified convention."""
    import random
    F = sebp.Blueprint.FACE_MAP; orig = dict(F)
    def run(label, newmap=None, rnd=False):
        F.clear(); F.update(orig); F.update(newmap or {})
        pieces = grids = att = tot = 0
        for bp in sebp.load_sbc(path):
            if len(bp.blocks) < 2: continue
            if rnd:
                r = random.Random(5)
                ors = [(f, u) for f in sebp.DIR for u in sebp.DIR if sum(a*b for a, b in zip(sebp.DIR[f], sebp.DIR[u])) == 0]
                for b in bp.blocks:
                    if b['mounts'] is not None: b['fwd'], b['up'] = r.choice(ors)
            adj = bp.attachment(bp.occupancy(allow_overlap=True)); grids += 1
            pieces += len(bp.components(adj))
            for b in bp.blocks: tot += 1; att += bool(adj[id(b)])
        out(f'   {label:42s} attached {att}/{tot}  pieces {pieces} (grids {grids})')
    out('negative controls (pieces must equal grids for the true convention):')
    run('verified convention')
    run('Front: no X mirror', {'Front': lambda u, v, X, Y, Z: (u, v, 0)})
    run('Back: X mirrored', {'Back': lambda u, v, X, Y, Z: (X - u, v, Z)})
    run('Left: u -> Z-u', {'Left': lambda u, v, X, Y, Z: (0, v, Z - u)})
    run('Right: u -> u', {'Right': lambda u, v, X, Y, Z: (X, v, u)})
    run('Top: v -> v (no Z flip)', {'Top': lambda u, v, X, Y, Z: (u, Y, v)})
    run('Bottom: v -> Z-v', {'Bottom': lambda u, v, X, Y, Z: (u, 0, Z - v)})
    run('Top/Bottom swapped', {'Top': lambda u, v, X, Y, Z: (u, 0, v), 'Bottom': lambda u, v, X, Y, Z: (u, Y, Z - v)})
    run('Left/Right swapped', {'Left': lambda u, v, X, Y, Z: (X, v, Z - u), 'Right': lambda u, v, X, Y, Z: (0, v, u)})
    run('Front/Back planes swapped', {'Front': lambda u, v, X, Y, Z: (X - u, v, Z), 'Back': lambda u, v, X, Y, Z: (u, v, 0)})
    run('random orientation of every non-default block', None, True)
    F.clear(); F.update(orig)

if __name__ == '__main__':
    if '--controls' in sys.argv:
        controls([x for x in sys.argv[1:] if not x.startswith('--')][0]); sys.exit(0)
    if '--shape' in sys.argv:
        a = [x for x in sys.argv[1:] if not x.startswith('--')]
        ref = a[1] if len(a) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'ref', 'user-samples', 'Starburst', 'bp.sbc')
        print(f'shape check {a[0]} against {ref}')
        sys.exit(1 if shape_diff(a[0], ref) else 0)
    args = [a for a in sys.argv[1:] if os.path.isfile(a) or a.startswith('--')]
    types = '--types' in args; lst = '--list' in args
    for p in [a for a in args if not a.startswith('--')]: audit(p, types, lst)
