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

SCRIPT_LIMITS = {"ShrikeAI.cs": 86000}      # ship Helm scripts: <= 86,000 minified characters (control_arch s1.2 budget 80,000, +6,000 of link and feature code since). The PB limit is 100,000. Past ~80 k move an off-hot-path module into the Staff PB (docs/design/helm_split.md); do not raise this cap (CLAUDE.md)

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
def deadman_kit(bp, name, seat, timer=None, timer_cell=None, groups=('Thrusters', 'Weapons'), ec_cell=None, experimental=False):
    """The dead-man hardware of a Helm ship (control_arch s2.6, M2): Timer D + (optional) Event Controller 'cockpit occupied'.

    Timer D   custom name '<name> Deadman' (ShrikeAI re-arms it once a second while it flies, stops it while a pilot sits), Delay 5000 ms, Silent,
              toolbar = one OnOff_Off per block group of 'groups' (the groups must exist: bp.group(...) first, e.g. 'Thrusters' and 'Weapons').
              Pass an existing 'timer' block to only (re)set its settings and toolbar, or 'timer_cell' to add a TimerBlockSmall there.
    EC        only with ec_cell: slot 0 (cockpit occupied) = Stop timer, slot 1 (cockpit empty) = Start timer, selected block = the seat. Its SelectedEvent is
              the sim's PLACEHOLDER 101 because the game's number is unknown (test K4): needs experimental=True, which also notes it in bp.notes. NEVER ship a
              blueprint built with it; without ec_cell the kit is the Timer alone (the Helm's own heartbeat is then the only arming).
    Returns (timer, ec or None)."""
    import sebp_toolbar as TB
    if timer is None:
        timer = bp.add('MyObjectBuilder_TimerBlock', 'TimerBlockSmall', timer_cell, 'Forward', 'Up', f'{name} Deadman', kind='ctrl')
    timer['settings'] = {'Delay': 5000, 'Silent': True}
    bp.set_toolbars(timer, toolbar=[TB.group(g, 'OnOff_Off') for g in groups])
    ec = None
    if ec_cell is not None:
        if not experimental: raise ValueError('deadman_kit: the Event Controller event id for "cockpit occupied" is a placeholder (K4 open): pass experimental=True or ec_cell=None')
        ec = bp.add('MyObjectBuilder_EventControllerBlock', 'EventControllerSmall', ec_cell, 'Forward', 'Up', f'{name} EC Cockpit', kind='ctrl')
        bp.set_toolbars(ec, toolbar=[TB.block(timer, 'Stop'), TB.block(timer, 'Start')])
        ec['settings'] = TB.event_settings('cockpit', threshold=0, experimental=True)
        ec['links'] = {'selected': [seat]}
    return timer, ec

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

# ---- walkways (a crew member must be able to walk from the entry door to the bridge seats and the hangar) ----
WALK_DOOR = ('MyObjectBuilder_AirtightSlideDoor', 'LargeBlockSlideDoor')   # 1x1x1, non-DLC, mounts on all six faces
WALK_LADDER = ('MyObjectBuilder_Ladder2', 'LadderShaft')                  # 1x1x1, non-DLC, climbable shaft cell

def carve_walkway(S, res, cells, doors=(), ladders=(), name='Walkway', namer=lambda s: s):
    """Clear `cells` (large-grid cells, the corridor) of hull blocks so somebody can walk through them.  Only hull blocks (res.cell_block) may be
    removed; anything else in the way is a design error.  `doors` = {cell: label} get a slide door (the cell stays walkable); `ladders` = cells that get
    a ladder shaft (vertical climbs).  The free corridor cells are then reserved so the conveyor router and later placements keep out.
    Returns the corridor cell set (doors and ladders included)."""
    bp = S.bp
    cells = [tuple(c) for c in cells]
    for c in cells:
        old = S.occ.get(c)
        if old is None: continue
        assert res.cell_block.get(c) is old and old['kind'] in ('armor', 'hull', 'plate', 'accent') or old['sub'].endswith('ArmorBlock') or 'Armor' in old['sub'], \
            f'{bp.name}: {name}: cell {c} is occupied by {old["name"] or old["sub"]}, not a hull block'
        assert len(bp.cells(old)) == 1, f'{bp.name}: {name}: {old["sub"]} at {c} spans several cells'
        bp.blocks.remove(old)
        if old in res.blocks: res.blocks.remove(old)
        res.cell_block.pop(c, None)
        del S.occ[c]
    for c, label in dict(doors).items():
        assert c in cells, f'{name}: door cell {c} is not in the corridor'
        S.add('walkdoor', WALK_DOOR[0], WALK_DOOR[1], tuple(c), 'Forward', 'Up', namer(f'Door {label}'), kind='door')
    for c in ladders:
        S.add('ladder', WALK_LADDER[0], WALK_LADDER[1], tuple(c), 'Forward', 'Up', namer(f'Ladder {c[1]}'), kind='ladder', toward=None)
    for c in cells:
        if c not in S.occ: bp.reserve[c] = f'{name} (walkway)'
    return set(cells)

def walk_check(S, start, goals, passable, name='walkway'):
    """BFS over `passable` cells (the corridor and the rooms it joins) that holds no block except doors / ladders: every cell of `goals` must be reached from
    `start` (the outside cell in front of the entry door).  Returns the number of cells reached."""
    seen = {tuple(start)}
    todo = [tuple(start)]
    pas = {tuple(c) for c in passable} | seen
    while todo:
        c = todo.pop()
        for d in DIR.values():
            n = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
            if n in seen or n not in pas: continue
            b = S.occ.get(n)
            if b is not None and b['kind'] not in ('door', 'ladder'): continue
            seen.add(n); todo.append(n)
    miss = [tuple(g) for g in goals if tuple(g) not in seen]
    assert not miss, f'{S.bp.name}: {name}: cannot walk from {tuple(start)} to {miss}'
    return len(seen)

# ---- conveyor lines (the game's own saves list every face-adjacent pair of conveyor ports; see sebp.Blueprint._conveyor_xml) ----
def link_conveyors(bp, terminals=()):
    """Fill bp.conveyor_pairs: (1) every face-adjacent pair of junction cubes (kind 'conv'); (2) hydrogen thrusters (kind 'thr') whose back-face centre cell touches
    a junction or a tank (kind 'tank'): the pair is (the thruster's port cell, the neighbour's cell); (3) `terminals` = [(junction cell, block name)]: the cell of
    the named block that touches that junction.  Returns the number of pairs."""
    occ = bp.occupancy()
    pairs = set()
    def add(a, b):
        pairs.add((a, b) if a <= b else (b, a))
    conv = {c for c, b in occ.items() if b['kind'] == 'conv'}
    for c in conv:
        for d in DIR.values():
            n = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
            if n in conv: add(c, n)
    for b in bp.blocks:
        if b['kind'] != 'thr': continue
        cs = bp.cells(b)
        lo = [min(c[i] for c in cs) for i in range(3)]; hi = [max(c[i] for c in cs) for i in range(3)]
        mid = [(lo[i] + hi[i]) // 2 for i in range(3)]
        d = DIR[b['fwd']]                                   # exhaust direction; the port is on the opposite (Back) face centre
        port = list(mid); out = list(mid)
        for i in range(3):
            if d[i] > 0: port[i] = lo[i]; out[i] = lo[i] - 1
            elif d[i] < 0: port[i] = hi[i]; out[i] = hi[i] + 1
        n = occ.get(tuple(out))
        if n is not None and n['kind'] in ('conv', 'tank'): add(tuple(port), tuple(out))
    by_name = {}
    for c, b in occ.items(): by_name.setdefault(b['name'], []).append(c)
    for j, name in terminals:
        for d in DIR.values():
            n = (j[0] + d[0], j[1] + d[1], j[2] + d[2])
            if n in by_name.get(name, ()) and occ[n]['kind'] != 'conv': add(j, n); break
    bp.conveyor_pairs = sorted(pairs)
    return len(pairs)

def route_walkway(S, res, env, waypoints, avoid=(), armour_cost=5):
    """Cells of a corridor through the hull from waypoint to waypoint (each leg a Dijkstra over 6-neighbour steps): a free cell inside `env` costs 1, a
    1-cell hull armour block whose faces are not exposed to space costs `armour_cost` (it will be carved), everything else (machinery, windows, skin, reserved
    cells, `avoid`) is a wall.  The waypoints themselves are always allowed.  Returns the ordered unique cells."""
    import heapq
    bp = S.bp
    env = set(env); avoid = {tuple(c) for c in avoid}
    resv = set()
    for v in res.reserved.values(): resv |= set(v.cells)
    wps = [tuple(w) for w in waypoints]
    def cost(c):
        if c in wps: return 1
        if c in avoid or c in bp.reserve or c in resv or c not in env: return None
        b = S.occ.get(c)
        if b is None: return 1
        if res.cell_block.get(c) is b and len(bp.cells(b)) == 1 and (b['sub'].endswith('ArmorBlock') or b['kind'] in ('armor', 'plate', 'dark', 'accent', 'stripe')) \
                and not res.exposure.get(c) and not res.flush.get(c):
            return armour_cost
        return None
    out = [wps[0]]
    for a, b in zip(wps, wps[1:]):
        dist = {a: 0}; prev = {}; h = [(0, a)]; found = False
        while h:
            d0, c = heapq.heappop(h)
            if c == b: found = True; break
            if d0 > dist.get(c, 1e9): continue
            for dv in DIR.values():
                n = (c[0] + dv[0], c[1] + dv[1], c[2] + dv[2])
                k = cost(n)
                if k is None: continue
                if d0 + k < dist.get(n, 1e9): dist[n] = d0 + k; prev[n] = c; heapq.heappush(h, (d0 + k, n))
        assert found, f'{bp.name}: no walkway route from {a} to {b}'
        leg = [b]
        while leg[-1] != a: leg.append(prev[leg[-1]])
        for c in reversed(leg[:-1]):
            if c not in out: out.append(c)
    return out

# ---- gravity generator (large grid only; the field is centred on the block, gravity pulls toward the block's Down) ----
def place_gravity(S, res, env, target, field_m, name, accel=9.81, color=None):
    """One LargeBlock flat gravity generator (`GravityGenerator/`, no DLC) on a floor: the free interior cell nearest to `target` whose Bottom face stands on a
    block, upright (Up = ship Up, so the field pulls toward the ship's Down).  `field_m` = (x, y, z) field box in metres, centred on the block.
    Returns the block.  The XML (FieldSize / GravityAcceleration) is the object-builder shape of Keen's source, never seen in a game file."""
    bp = S.bp
    resv = set()
    for v in res.reserved.values(): resv |= set(v.cells)
    env = set(env)
    cands = [c for c in env if c[0] == 0 and c not in S.occ and c not in resv and c not in bp.reserve and (c[0], c[1] - 1, c[2]) in S.occ]
    cands.sort(key=lambda c: (sum((c[i] - target[i]) ** 2 for i in range(3)), c))
    extra = f'<FieldSize x="{field_m[0]}" y="{field_m[1]}" z="{field_m[2]}" /><GravityAcceleration>{accel}</GravityAcceleration>'
    for c in cands[:200]:
        below = S.occ[(c[0], c[1] - 1, c[2])]
        if below['kind'] in ('door', 'conv', 'thr', 'turret'): continue
        b = bp.add('MyObjectBuilder_GravityGenerator', '', c, 'Forward', 'Up', name, color or GUN, extra=extra, kind='gravity')
        if len(bp.cells(b)) == 1 and not any(q in S.occ or q in bp.reserve for q in bp.cells(b)) and _attached_up(bp, b, S.occ):
            return S.put('gravity', b)
        bp.blocks.pop()
    raise AssertionError(f'{bp.name}: no floor cell for the gravity generator near {target}')

def _attached_up(bp, b, occ):
    import pods
    c = bp.cells(b)[0]
    n = occ.get((c[0], c[1] - 1, c[2]))
    return n is not None and pods._attached_to(bp, b, [n])

# ---- interior dressing: round slopes / corners in the concave edges of a room ----
def chamfer_room(S, res, env, box, name='room', max_blocks=None):
    """Fill the concave edges and corners of the room `box` = ((x0, y0, z0), (x1, y1, z1)) (inclusive cells) with round armour slopes / corners (light, no DLC)
    so it stops looking like a cut-out shell.  A cell qualifies when 2 (edge) or 3 (corner) of its faces touch hull armour on different axes and the room is
    open at least two cells deep in front of it; walkway, reserved and system cells are left alone.  Returns the number of blocks placed."""
    import hulls as H
    bp = S.bp
    lo, hi = box
    resv = set()
    for v in res.reserved.values(): resv |= set(v.cells)
    env = set(env)
    def hull_wall(c):
        b = S.occ.get(c)
        return b is not None and res.cell_block.get(c) is b and len(bp.cells(b)) == 1 and (b['sub'].endswith('ArmorBlock') or b['kind'] in ('armor', 'plate', 'dark', 'accent', 'stripe'))
    def free(c):
        return c not in S.occ and c in env and c not in resv and c not in bp.reserve
    todo = []
    for x in range(lo[0], hi[0] + 1):
        for y in range(lo[1], hi[1] + 1):
            for z in range(lo[2], hi[2] + 1):
                c = (x, y, z)
                if not free(c): continue
                walls = [d for d in DIR.values() if hull_wall((c[0] + d[0], c[1] + d[1], c[2] + d[2]))]
                if len(walls) != 2: continue                 # edges only: a corner cell next to its edge slopes would press an open face on a neighbour
                axes = {next(i for i in range(3) if d[i]) for d in walls}
                if len(axes) != len(walls): continue
                # every other neighbour must be free (an edge, not a nook with equipment beside it)
                others = [d for d in DIR.values() if d not in walls]
                ok = True
                for d in others:
                    n = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
                    if not (free(n) or (n in S.occ and (lo[0] <= n[0] <= hi[0] and lo[1] <= n[1] <= hi[1] and lo[2] <= n[2] <= hi[2]) and False)): ok = False
                if not ok: continue
                E = [tuple(-v for v in d) for d in walls]            # the faces that look into the room
                # two cells of open room in front of the edge (no narrowing of a passage)
                if any(not free((c[0] + 2 * e[0], c[1] + 2 * e[1], c[2] + 2 * e[2])) for e in E): continue
                todo.append((c, E))
    cellset = {c for c, E in todo}
    todo = [(c, E) for c, E in todo if (-c[0], c[1], c[2]) in cellset]               # the ships are mirror-symmetric about x = 0
    todo = [(c, E) for c, E in todo if not any((c[0] + e[0], c[1] + e[1], c[2] + e[2]) in cellset for e in E)]   # no two slopes face each other
    placed = 0
    chams = []
    st = dict(heavy=False, color=(0.0, -0.81, -0.35), round=True, kind='armor')
    for c, E in todo:
        if max_blocks is not None and placed >= max_blocks: break
        kind = 'slope' if len(E) == 2 else 'corner'
        try:
            b = H._emit(res, c, kind, E, st)
        except H.HullError:
            continue
        S.occ[c] = b
        res.blocks.remove(b)               # res.check() treats the whole envelope as hull; these are checked against the real walls below
        chams.append(b)
        placed += 1
    H.check_mount_faces(bp, set(S.occ), None, blocks=chams)     # full mount faces on walls, slanted faces on free air
    return placed


# ---- support PBs (appended for THRUST VECTORING and the Flight/Staff split; docs/thrust-vectoring.md) ----
SUPPORT_STAFF = {'AEGIS': 'AegisStaffAI.cs', 'MANTIS': 'MantisStaffAI.cs', 'LONGBOW': 'LongbowStaffAI.cs', 'WASP': 'WaspStaffAI.cs',
                 'HORNET': 'HornetStaffAI.cs', 'HARRIER': 'HarrierStaffAI.cs', 'LANCER': 'LancerStaffAI.cs', 'BASILISK': 'BasiliskStaffAI.cs', 'GNAT': 'GnatStaffAI.cs'}
_SMALL_PB, _LARGE_PB = 'SmallProgrammableBlock', 'LargeProgrammableBlock'
NO_SUPPORT_PBS = ('Twin PB Test', 'Hornet Pilot Test')   # sim probe ships with a pinned PB count (SelfTestM0): no support PBs

def _orientations():
    out = []
    for f in AXIS_DIRS:
        for u in AXIS_DIRS:
            if sum(a * b for a, b in zip(DIR[f], DIR[u])) == 0: out.append((f, u))
    return out

def free_pb_spot(bp, sub, near, name='[Support] PB'):
    """a free cell + orientation where a Programmable Block `sub` attaches to the hull, cell next to as many blocks as possible (inside the hull is safest) and near `near`
    (a cell); never in a reserved cell (exhaust, muzzle, camera view). Returns (cell, fwd, up) or None."""
    from itertools import product
    occ = bp.occupancy()
    cells = [c for c in occ]
    lo = [min(c[i] for c in cells) - 1 for i in range(3)]; hi = [max(c[i] for c in cells) + 1 for i in range(3)]
    steps = [DIR[d] for d in ('Forward', 'Backward', 'Left', 'Right', 'Up', 'Down')]
    cand = []
    for cell in product(*[range(lo[i], hi[i] + 1) for i in range(3)]):
        if cell in occ or cell in bp.reserve: continue
        nb = sum(1 for s in steps if (cell[0] + s[0], cell[1] + s[1], cell[2] + s[2]) in occ)
        if nb == 0: continue
        d = sum(abs(cell[i] - near[i]) for i in range(3))
        cand.append((-min(nb, 4), d, cell))
    cand.sort()
    index = {}
    for b in bp.blocks:
        for r in bp.mount_rects(b): index.setdefault((r[0], r[1], r[2]), []).append((b, r))
    for _, _, cell in cand[:400]:
        for fwd, up in _orientations():
            b = bp.add('MyObjectBuilder_MyProgrammableBlock', sub, cell, fwd, up, name, kind='ctrl')
            try:
                ok = any(any(nb is not b and lo_[0] <= hi2[0] and lo2[0] <= hi_[0] and lo_[1] <= hi2[1] and lo2[1] <= hi_[1]
                             for (nb, (a2, pc2, ns2, lo2, hi2, ex2, pr2)) in index.get((axis, pc, -ns), ()))
                         for (axis, pc, ns, lo_, hi_, ex, pr) in bp.mount_rects(b))
            finally:
                bp.blocks.remove(b)
            if ok: return cell, fwd, up
    return None

def add_support_pbs(bp):
    """Every drone / ship whose flight script carries the THRUST VECTOR LINK gets a small `[Thrust] ... PB` (scripts/ThrustVectorAI.cs); a Flight half of a split drone
    (SPLIT LINK) is renamed `[Flight] ...` and gets its `[Staff] ...` PB (scripts/<Drone>StaffAI.cs). Idempotent: a design that already carries them is left alone."""
    import re
    from xml.sax.saxutils import unescape
    if bp.name.replace(' Lite', '') in NO_SUPPORT_PBS: return bp
    pbs = [b for b in bp.blocks if b['sub'] in (_SMALL_PB, _LARGE_PB) and '<Program>' in (b.get('extra') or '')]
    prog = {id(b): unescape(b['extra']) for b in pbs}
    names = [(b['name'] or '') for b in pbs]
    sub = _LARGE_PB if bp.grid == 'Large' else _SMALL_PB
    def base(b):
        n = b['name'] or 'PB'
        n = re.sub(r'^(\[[^\]]*\])+\s*', '', n)
        return n[:-3] if n.endswith(' PB') else n
    for b in list(pbs):
        p = prog[id(b)]
        if 'LkInit(' in p and not any(n.startswith('[Staff]') for n in names):
            m = re.search(r'string Tag\s*=\s*"([A-Z]+)"', p)
            assert m and m.group(1) in SUPPORT_STAFF, f'{bp.name}: no Staff script known for the Flight half ({m and m.group(1)})'
            if not (b['name'] or '').startswith('[Flight]'): b['name'] = f'[Flight] {base(b)} PB'
            spot = free_pb_spot(bp, sub, b['min'])
            assert spot, f'{bp.name}: no free cell for the Staff PB'
            bp.add('MyObjectBuilder_MyProgrammableBlock', sub, spot[0], spot[1], spot[2], f'[Staff] {base(b)} PB', extra=pb_extra(script_text(SUPPORT_STAFF[m.group(1)])), kind='ctrl')
            names.append('[Staff]')
    if any('TvAim(' in p for p in prog.values()) and not any(n.startswith('[Thrust]') for n in names):
        b0 = [b for b in pbs if 'TvAim(' in prog[id(b)]][0]
        spot = free_pb_spot(bp, sub, b0['min'])
        assert spot, f'{bp.name}: no free cell for the Thrust PB'
        bp.add('MyObjectBuilder_MyProgrammableBlock', sub, spot[0], spot[1], spot[2], f'[Thrust] {base(b0)} PB', extra=pb_extra(script_text('ThrustVectorAI.cs')), kind='ctrl')
    return bp
