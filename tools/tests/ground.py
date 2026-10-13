"""Ground truth for the sebp tests: the owner's game-written blueprints (ref/user-samples/*/bp.sbc and pc/enemies/Tech Hunter).

ref/ is not tracked by git; in a git worktree the samples are looked up in the main checkout too, or set SEBP_SAMPLES.
Samples written by our own old generator (extract_shapes.looks_generated) are never used as ground truth."""
import os, sys, glob, functools
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
if TOOLS not in sys.path: sys.path.insert(0, TOOLS)
import xml.etree.ElementTree as ET
import sebp
import extract_shapes as X
XSI = sebp.XSI

def have_samples():
    return bool(files())

@functools.lru_cache(maxsize=None)
def files():
    d = X.find_repo_path('ref', 'user-samples')
    th = X.find_repo_path('pc', 'enemies', 'Tech Hunter', 'bp.sbc')
    return tuple(X.sample_files(d, [th]))

@functools.lru_cache(maxsize=None)
def root_of(path):
    return ET.parse(path).getroot()

def name_of(path): return X.sample_name(path)

@functools.lru_cache(maxsize=None)
def game_files():
    return tuple(f for f in files() if not X.looks_generated(root_of(f)))

def path_of(name):
    for f in files():
        if name_of(f) == name: return f
    raise KeyError(name)

def grids(path):
    return X.real_grids(root_of(path))

def blocks(types=None, subs=None, files_=None):
    """yield (sample name, grid element, block element) for the complete game-written blocks of the given types"""
    for f in (files_ or game_files()):
        for g in grids(f):
            gb = g.find('CubeBlocks')
            if gb is None: continue
            for b in gb:
                t = b.get(XSI + 'type').replace('MyObjectBuilder_', '')
                if types is not None and t not in types: continue
                if subs is not None and (b.findtext('SubtypeName') or '') not in subs: continue
                yield name_of(f), g, b

def tag_tree(e, collapse=('Slots', 'SlotsGamepad', 'Items', 'Blocks', 'Waypoints', 'TextPanelsContents', 'ToolIds', 'SelectedBlocks',
                          'dictionary', 'ProjectedGrids', 'RemainingAmmosList', 'DefinitionIds', 'Sprites')):
    """nested tuple of tags (attributes and text ignored); repeated children of list-like elements collapse to the distinct child shapes"""
    kids = [tag_tree(c, collapse) for c in e]
    if e.tag in collapse:
        seen = []
        for k in kids:
            if k not in seen: seen.append(k)
        kids = seen
    return (e.tag, tuple(kids))

def num_equal(a, b, tol=1e-6):
    try: return abs(float(a) - float(b)) <= tol * max(1.0, abs(float(b)))
    except (TypeError, ValueError): return a == b

def xml_equal(a, b, tol=1e-6, path=''):
    """structural equality of two elements: tags, attributes, text (numbers within tol); returns None or a description of the first difference"""
    if a.tag != b.tag: return f'{path}/{a.tag} != {b.tag}'
    if {k: v for k, v in a.attrib.items()} != {k: v for k, v in b.attrib.items()}: return f'{path}/{a.tag} attributes {a.attrib} != {b.attrib}'
    ta, tb = (a.text or '').strip(), (b.text or '').strip()
    if len(a) == 0 and not num_equal(ta, tb, tol): return f'{path}/{a.tag} text {ta!r} != {tb!r}'
    if len(a) != len(b): return f'{path}/{a.tag} has {len(a)} children, expected {len(b)}'
    for x, y in zip(a, b):
        d = xml_equal(x, y, tol, path + '/' + a.tag)
        if d: return d
    return None
