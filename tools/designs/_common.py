"""Shared helpers for tools/designs/*.py (API is append-only: add, never rename).

Conventions (see docs/research.md section 0 and HANDOFF.md):
  * Thruster: block Forward = exhaust direction, so the thrust that acts on the ship points to the OPPOSITE of fwd.
  * Merge block: the clamp face normal is the block's BlockOrientation *Up* direction for the small 1x1x1 block
    (MERGE_CLAMP_FACE_SMALL) and *Right* for the 1x3x3 SmallShipMergeBlock. Two merge blocks pair when their clamp
    normals oppose.  Use merge_small() so the clamp face points in a chosen GRID direction.
  * Masses/costs come from defs_stats.json (mass = sum of component masses), never hand-typed.
"""
import sys, os, json, zlib
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.normpath(os.path.join(TOOLS, '..'))
if TOOLS not in sys.path: sys.path.insert(0, TOOLS)
import sebp
from sebp import Blueprint, DIR

# ---- merge block clamp faces (local block direction that touches the partner merge block) ----
MERGE_CLAMP_FACE_SMALL = "Up"       # SmallShipSmallMergeBlock 1x1x1  (wiki; UNVERIFIED in game, test T1)
MERGE_CLAMP_FACE_BIG = "Right"      # SmallShipMergeBlock 1x3x3
LITE_KEEPS_MERGE = True             # Lite variants keep the merge block (XML shape is from source, not seen in Starburst)

DARK = (0.0, -0.81, -0.55)
GUN = (0.0, -0.81, -0.45)
RED = (0.98, 0.65, 0.05)
PAL = {"armor": (92,100,112), "cam": (36,36,42), "bat": (70,120,90), "ctrl": (90,110,170), "gyro": (150,150,60),
       "gun": (50,52,58), "thr": (62,66,74), "ant": (185,185,195), "sensor": (120,80,150), "ai": (60,150,170),
       "merge": (210,140,40), "warhead": (190,50,40), "rail": (52,54,60), "accent": (225,130,40)}
OPP = {"Forward": "Backward", "Backward": "Forward", "Up": "Down", "Down": "Up", "Left": "Right", "Right": "Left"}
AXIS_DIRS = ("Forward", "Backward", "Up", "Down", "Left", "Right")

def seed_for(name):
    """stable (hash() is salted per process)"""
    return zlib.crc32(name.encode()) & 0xffff

# ---- block-data lookups ----
_STATS = None
def stats_db():
    global _STATS
    if _STATS is None:
        _STATS = json.load(open(os.path.join(ROOT, 'defs_stats.json')))['blocks']
    return _STATS

def _stat(b):
    return stats_db().get(b['key']) or stats_db().get(b['key'].rstrip('/')) or {}

def block_mass(b):
    return _stat(b).get('mass_kg') or b.get('mass') or 0

def finalize(bp):
    """overwrite the hand-typed masses with the real ones so Blueprint.stats() is right; returns bp"""
    for b in bp.blocks:
        m = _stat(b).get('mass_kg')
        if m: b['mass'] = m
    return bp

# ---- extras ----
def pb_extra(script):
    from xml.sax.saxutils import escape
    return f"<Program>{escape(script)}</Program><Storage /><DefaultRunArgument xsi:nil=\"true\" />"

def bat_extra(mwh):
    """battery pre-charged to full"""
    return (f"<CurrentStoredPower>{mwh}</CurrentStoredPower><ProducerEnabled>true</ProducerEnabled><MaxStoredPower>{mwh}</MaxStoredPower>"
            "<SemiautoEnabled>false</SemiautoEnabled><OnlyDischargeEnabled>false</OnlyDischargeEnabled><ChargeMode>0</ChargeMode>")
BAT_S = bat_extra(0.05)             # SmallBlockSmallBatteryBlock
def ant_extra(r):
    return f"<BroadcastRadius>{r}</BroadcastRadius><ShowShipName>true</ShowShipName><EnableBroadcasting>true</EnableBroadcasting>"

SCRIPT_LIMITS = {"ShrikeAI.cs": 80000}      # ship Helm scripts: <= 80,000 minified characters (control_arch s1.2), the other scripts only the PB limit

def script_text(filename):
    """read scripts/<filename> at call time, minified (comments/whitespace only): a Programmable Block takes at most 100,000
    characters, the commented sources are larger. tools/minify_check.sh proves the minified text compiles to the same IL."""
    import sys as _s
    _s.path.insert(0, os.path.join(ROOT, 'tools'))
    from minify_cs import minify
    t = minify(open(os.path.join(ROOT, 'scripts', filename), encoding='utf-8').read())
    lim = SCRIPT_LIMITS.get(filename, 100000)
    if len(t) > lim: raise ValueError(f"{filename}: {len(t)} characters after minifying; the limit is {lim:,}" + ("" if lim == 100000 else " (ship Helm budget, control_arch s1.2; the PB limit is 100,000)"))
    return t

# ---- block helpers ----
def thr(bp, mn, fwd, up, name, sub=None):
    b = bp.add("MyObjectBuilder_Thrust", sub or ("SmallBlockSmallThrust" if bp.grid == "Small" else "LargeBlockSmallThrust"),
               mn, fwd, up, name, DARK, kind="thr")
    bp.keep_clear(b, 4, "exhaust")
    return b

def cam(bp, mn, fwd, up, name, view=3):
    b = bp.add("MyObjectBuilder_CameraBlock", "SmallCameraBlock" if bp.grid == "Small" else "LargeCameraBlock", mn, fwd, up, name, DARK, kind="cam")
    bp.keep_clear(b, view, "camera view")
    return b

def bat(bp, mn, name, fwd="Forward", up="Up", small=True):
    if bp.grid == "Large":
        return bp.add("MyObjectBuilder_BatteryBlock", "LargeBlockBatteryBlock", mn, fwd, up, name, extra=bat_extra(3), kind="bat")
    if small:
        return bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockSmallBatteryBlock", mn, fwd, up, name, extra=BAT_S, kind="bat")
    return bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockBatteryBlock", mn, fwd, up, name, extra=bat_extra(1), kind="bat")

def ant(bp, mn, fwd, up, name, radius):
    sub = "SmallBlockRadioAntenna" if bp.grid == "Small" else "LargeBlockRadioAntenna"
    return bp.add("MyObjectBuilder_RadioAntenna", sub, mn, fwd, up, name, extra=ant_extra(radius), kind="ant")

# ---- merge block ----
def orient_for_up(up_dir, prefer_fwd=("Forward", "Backward", "Left", "Right", "Up", "Down")):
    """(fwd, up) with up == up_dir and fwd perpendicular to it"""
    for f in prefer_fwd:
        if sum(a*b for a, b in zip(DIR[f], DIR[up_dir])) == 0: return f, up_dir
    raise AssertionError(up_dir)

def merge_small(bp, mn, clamp_dir, name, color=RED):
    """SmallShipSmallMergeBlock whose clamp face points along grid direction `clamp_dir`.
    Uses MERGE_CLAMP_FACE_SMALL: if that is "Up" the block's Up is clamp_dir; "Right" -> Right is clamp_dir, etc."""
    assert bp.grid == "Small"
    face = MERGE_CLAMP_FACE_SMALL
    # find an orientation (fwd, up) whose `face` local direction maps to clamp_dir
    cands = []
    for f in DIR:
        for u in DIR:
            if sum(a*b for a, b in zip(DIR[f], DIR[u])): continue
            loc = {"Forward": DIR[f], "Backward": tuple(-v for v in DIR[f]), "Up": DIR[u], "Down": tuple(-v for v in DIR[u]),
                   "Right": sebp.cross(DIR[f], DIR[u]), "Left": tuple(-v for v in sebp.cross(DIR[f], DIR[u]))}
            if loc[face] == DIR[clamp_dir]: cands.append((f, u))
    pref = {"Up": 0, "Down": 1, "Forward": 2, "Backward": 3, "Left": 4, "Right": 5}
    cands.sort(key=lambda fu: (fu[0] == "Up" or fu[0] == "Down", pref[fu[0]], pref[fu[1]]))
    fwd, up = cands[0]
    b = bp.add("MyObjectBuilder_MergeBlock", "SmallShipSmallMergeBlock", mn, fwd, up, name, color, kind="merge")
    b['clamp_dir'] = clamp_dir
    return b

def merge_clamp_dir(bp, b):
    """grid direction the clamp face of merge block b points to (uses MERGE_CLAMP_FACE_SMALL / _BIG)"""
    face = MERGE_CLAMP_FACE_SMALL if b['sub'] == "SmallShipSmallMergeBlock" else MERGE_CLAMP_FACE_BIG
    f, u = DIR[b['fwd']], DIR[b['up']]
    loc = {"Forward": f, "Backward": tuple(-v for v in f), "Up": u, "Down": tuple(-v for v in u),
           "Right": sebp.cross(f, u), "Left": tuple(-v for v in sebp.cross(f, u))}
    d = loc[face]
    return [n for n, v in DIR.items() if v == d][0]

def check_docking(bp, b):
    """The docking face must be the exterior: the merge block is the ONLY block reaching its outermost layer along the
    clamp axis (nothing else on the drone can touch a flat printer surface), and the clamp-side cell is free.
    Returns the clamp direction; raises AssertionError otherwise."""
    d = merge_clamp_dir(bp, b); v = DIR[d]
    ax = [abs(c) for c in v].index(1); sg = v[ax]
    mcell = bp.cells(b)[0]
    reach = mcell[ax] * sg                     # outermost layer index, signed so bigger = further out
    occ = bp.occupancy()
    nxt = tuple(mcell[k] + v[k] for k in range(3))
    assert nxt not in occ, f'{bp.name}: clamp-side cell {nxt} of {b["name"]} is occupied by {occ[nxt]["name"]}'
    for c, o in occ.items():
        if o is b: continue
        assert c[ax] * sg < reach, f'{bp.name}: {o["name"]} at {c} is level with/past the merge block along {d}; could touch a printer'
    for c in list(bp.reserve):                 # keep a printer's merge block cell free of drone exhaust/muzzle/camera zones
        assert c != nxt, f'{bp.name}: {bp.reserve[c]} crosses the docking cell'
    return d

# ---- physics reports ----
def total_mass(bp): return sum(block_mass(b) for b in bp.blocks)

def thrust_by_dir(bp):
    """N of force the ship can push toward each of the six grid directions (thrust = opposite of exhaust fwd)"""
    out = {d: 0.0 for d in AXIS_DIRS}
    for b in bp.blocks:
        f = _stat(b).get('stats', {}).get('force_n')
        if b['xtype'] == 'MyObjectBuilder_Thrust' and f: out[OPP[b['fwd']]] += f
    return out

def twr(bp):
    """per-direction acceleration in m/s^2 (force / mass): {dir: a}, mass"""
    m = total_mass(bp)
    return {d: f / m for d, f in thrust_by_dir(bp).items()}, m

COST_KEYS = ("Iron", "Nickel", "Cobalt", "Silicon", "Silver", "Gold", "Platinum", "Magnesium", "Uranium")
def cost(bp):
    tot = {}
    for b in bp.blocks:
        for k, v in (_stat(b).get('ingot_cost') or {}).items(): tot[k] = tot.get(k, 0) + v
    return tot

def block_counts(bp):
    c = {}
    for b in bp.blocks: c[b['kind']] = c.get(b['kind'], 0) + 1
    return c

def report_line(label, bp, min_acc=1.0, axes=None):
    """validate (raises on error) and return (ok, one-line text)"""
    try:
        bp.validate()
        finalize(bp)
        acc, m = twr(bp)
        merges = [b for b in bp.blocks if b['kind'] == 'merge']
        extra = ''
        for mb in merges: extra += f' dock={check_docking(bp, mb)}'
        low = [d for d, a in acc.items() if a < min_acc and (axes is None or d in axes)]
        ok = not low
        cs = cost(bp); st = bp.stats()
        counts = ' '.join(f'{k}{v}' for k, v in sorted(block_counts(bp).items()))
        t = (f'{"PASS" if ok else "FAIL"} {label}: {len(bp.blocks)} blocks ({counts}) mass {m:.0f} kg dims {st["dims"]} m | accel m/s2 '
             + ' '.join(f'{d[0] if d not in ("Backward","Down") else d[:2]}{a:.1f}' for d, a in acc.items())
             + f' | cost ' + ' '.join(f'{k} {cs[k]:.0f}' for k in COST_KEYS if cs.get(k)) + extra
             + (f' | LOW axes {low}' if low else ''))
        return ok, t
    except AssertionError as e:
        return False, f'FAIL {label}: {e}'

def run_module(models, min_acc=1.0, argv=None, required_axes=None):
    """standard `python3 -I tools/designs/<module>.py` main: build + validate every model and variant"""
    bad = 0
    for name, fn in models.items():
        for lite in (False, True):
            label = f'{name}{" Lite" if lite else ""}'
            try:
                bp = fn(lite)
            except AssertionError as e:
                print(f'FAIL {label}: {e}'); bad += 1; continue
            ok, t = report_line(label, bp, min_acc, (required_axes or {}).get(name))
            print(t); bad += (not ok)
    return 1 if bad else 0
