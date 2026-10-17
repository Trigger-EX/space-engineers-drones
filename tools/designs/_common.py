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

SCRIPT_LIMITS = {"ShrikeAI.cs": 86000, "TubeRamAI.cs": 80000}      # ship Helm scripts: <= 86,000 minified characters (control_arch s1.2 budget 80,000, +6,000 of link and feature code since). The PB limit is 100,000. Past ~80 k move an off-hot-path module into the Staff PB (docs/design/helm_split.md); do not raise this cap (CLAUDE.md)

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

def merge_small(bp, mn, clamp_dir, name, color=RED, flush=False):
    """SmallShipSmallMergeBlock whose clamp face points along grid direction `clamp_dir`.  flush=True: the block is set into the hull
    (see check_docking), for drones whose outline must not grow.
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
    if flush: b['flush'] = True
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
    A FLUSH latch (merge_small(..., flush=True): the merge block set into the hull of a drone whose outline is a hard requirement,
    Gnat) may share the outermost layer with other blocks, but nothing may stand beyond it; the printer meets it with a merge block on
    a stalk, which only touches the drone through the clamp face.
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
        if b.get('flush'): assert c[ax] * sg <= reach, f'{bp.name}: {o["name"]} at {c} stands past the flush merge block along {d}; could touch a printer'
        else: assert c[ax] * sg < reach, f'{bp.name}: {o["name"]} at {c} is level with/past the merge block along {d}; could touch a printer'
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


# ---- railgun power (docs/design/railgun_power.md): the capacitor recharge must never starve ----
RAIL_RECHARGE_MW = {'SmallRailgun': 3.6, 'LargeRailgun': 38.0}     # capacitor RechargeDraw (docs/sim-design.md; not in defs_stats.json)
SMALL_LOAD_ALLOWANCE_MW = {'Small': 0.05, 'Large': 0.5}            # PBs, antenna, sensor, turret: blocks with no power figure in defs_stats.json
POWER_MARGIN = 1.05                                                # peak supply / peak demand
REACTOR_URANIUM_KG = {'Small': 5, 'Large': 20}                     # pre-loaded per small reactor (as shrike.py / corsair.py): 10 h / 1.3 h at full output

def reactor(bp, mn, name, fwd="Forward", up="Up", uranium_kg=None, color=DARK):
    """the grid's small reactor (SmallBlockSmallGenerator 0.5 MW / LargeBlockSmallGenerator 15 MW, 1x1x1, mounts on every face) with uranium pre-loaded"""
    from sebp_inv import Item
    sub = "SmallBlockSmallGenerator" if bp.grid == "Small" else "LargeBlockSmallGenerator"
    kg = REACTOR_URANIUM_KG[bp.grid] if uranium_kg is None else uranium_kg
    b = bp.add("MyObjectBuilder_Reactor", sub, mn, fwd, up, name, color, kind="reactor")
    b['sample_shape'] = True            # the game-written reactor shape (inventory + fuel Capacity); a legacy-shape blueprint has none to put the uranium in
    if kg: bp.preload(b, items=[Item.ingot("Uranium", kg)])
    return b

def swap_armour(bp, cell):
    """remove the 1-cell armour block at `cell` (its place is about to be taken by a block that mounts on every face it did, e.g. a reactor)"""
    occ = bp.occupancy()
    b = occ.get(tuple(cell))
    assert b is not None and b['sub'].endswith('ArmorBlock') and len(bp.cells(b)) == 1, f'{bp.name}: no 1-cell armour block at {cell} ({b and b["sub"]})'
    bp.blocks.remove(b)
    return b

def loaded_mass(bp):
    """block masses + pre-loaded items (uranium, magazines), kg"""
    return total_mass(bp) + sum(it.kg() for b in bp.blocks for it in bp.items_of(b))

def loaded_accel(bp):
    """per-direction acceleration with the pre-loaded items aboard, m/s^2"""
    m = loaded_mass(bp)
    return {d: f / m for d, f in thrust_by_dir(bp).items()}

def power_budget(bp):
    """peak electrical demand against the peak supply (reactors + batteries) and the sustained supply (reactors alone), MW.
    Demand = every railgun recharging + on each of the three axes the LARGER of its two opposite thruster groups at full (forward or back, up or down, left or
    right: opposite thrusters do not fire together; a diagonal manoeuvre fires one group per axis) + gyros, AI blocks, cameras, remote control + an allowance for
    the blocks without a figure.  thrust_all_MW is every thruster at once (for reference); battery_MWh is the stored energy."""
    rail = loads = gen = bat = mwh = 0.0
    grp = {d: 0.0 for d in AXIS_DIRS}
    for b in bp.blocks:
        st = _stat(b).get('stats', {})
        if b['sub'] in RAIL_RECHARGE_MW: rail += RAIL_RECHARGE_MW[b['sub']]
        elif b['xtype'] == 'MyObjectBuilder_Thrust': grp[OPP[b['fwd']]] += st.get('max_power_mw') or 0
        elif b['xtype'] == 'MyObjectBuilder_Reactor': gen += st.get('max_output_mw') or 0
        elif b['xtype'] == 'MyObjectBuilder_BatteryBlock': bat += st.get('max_output_mw') or 0; mwh += st.get('max_stored_mwh') or 0
        else: loads += st.get('required_power_mw') or st.get('power_mw') or 0
    loads += SMALL_LOAD_ALLOWANCE_MW[bp.grid]
    thr = max(grp['Forward'], grp['Backward']) + max(grp['Up'], grp['Down']) + max(grp['Left'], grp['Right'])
    need = rail + thr + loads
    return dict(rail_MW=rail, thrust_MW=round(thr, 3), thrust_all_MW=round(sum(grp.values()), 3), other_MW=round(loads, 3), peak_need_MW=round(need, 3),
                reactor_MW=gen, battery_MW=bat, battery_MWh=mwh, peak_supply_MW=gen + bat, margin=round((gen + bat) / need, 3) if need else None)

def turn_rates(bp):
    """moment of inertia about the centre of mass (point masses at the cell centres, items included) and the gyro angular acceleration, rad/s^2, about
    pitch (x), yaw (y) and roll (z).  Gyro torque = the GyroForce in defs_stats (N m)."""
    cs = 0.5 if bp.grid == 'Small' else 2.5
    pts = []
    for b in bp.blocks:
        m = block_mass(b) + sum(it.kg() for it in bp.items_of(b)); cells = bp.cells(b)
        for c in cells: pts.append((m / len(cells), (c[0] + .5) * cs, (c[1] + .5) * cs, (c[2] + .5) * cs))
    M = sum(p[0] for p in pts)
    com = [sum(p[0] * p[i] for p in pts) / M for i in (1, 2, 3)]
    I = [sum(p[0] * ((p[2] - com[1]) ** 2 + (p[3] - com[2]) ** 2) for p in pts),
         sum(p[0] * ((p[1] - com[0]) ** 2 + (p[3] - com[2]) ** 2) for p in pts),
         sum(p[0] * ((p[1] - com[0]) ** 2 + (p[2] - com[1]) ** 2) for p in pts)]
    tq = sum(_stat(b).get('stats', {}).get('force_n') or 0 for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Gyro')
    return dict(com_m=[round(c, 2) for c in com], inertia_kgm2=[round(i) for i in I], gyro_torque_Nm=tq,
                pitch_yaw_roll_rad_s2=[round(tq / i, 2) for i in I])


# ---- survival kit (docs/design/survivability.md items 11, 12, 13, 15): decoys, one flare launcher, a self-projector, skin panels ----
# Every part goes into a free cell INSIDE the drone's outline (CLAUDE.md outline rule): `outline` = ((x0, y0, z0), (x1, y1, z1)) inclusive, or a set of cells;
# default = the strict bounding box of the occupied cells (never +-1).  A part that finds no cell is skipped and recorded in bp.kit['skipped'] / bp.notes.
KIT_SUBS = {'Small': dict(decoy=('MyObjectBuilder_Decoy', 'SmallDecoy'), flare=('MyObjectBuilder_SmallMissileLauncher', 'SmallFlareLauncher'),
                          proj=('MyObjectBuilder_Projector', 'SmallProjector'), panel=('MyObjectBuilder_CubeBlock', 'SmallArmorPanelLight')),
            'Large': dict(decoy=('MyObjectBuilder_Decoy', 'LargeDecoy'), flare=('MyObjectBuilder_SmallMissileLauncher', 'LargeFlareLauncher'),
                          proj=('MyObjectBuilder_Projector', 'LargeProjector'), panel=('MyObjectBuilder_CubeBlock', 'LargeArmorPanelLight'))}
KIT_FLARE_CLEAR = 3                  # cells kept free in front of the flare launcher's muzzle
KIT_FLARE_CLIPS = 2                  # FlareClip magazines preloaded (4 flares each)
KIT_COLOR = (0.08, -0.6, 0.05)       # olive: decoys / flare / projector stand out from the hull
CRIT_X = ('RemoteControl', 'Cockpit', 'MyProgrammableBlock', 'Gyro', 'BatteryBlock', 'Reactor', 'HydrogenEngine')

def kit_outline(bp):
    """strict bounding box of the occupied cells: ((x0, y0, z0), (x1, y1, z1)), inclusive"""
    cs = list(bp.occupancy())
    return (tuple(min(c[i] for c in cs) for i in range(3)), tuple(max(c[i] for c in cs) for i in range(3)))

def in_outline(c, outline):
    if isinstance(outline, (set, frozenset)): return tuple(c) in outline
    lo, hi = outline
    return all(lo[i] <= c[i] <= hi[i] for i in range(3))

def critical_blocks(bp):
    """controller, programmable blocks, gyros, power: the blocks whose loss disables (or blinds) the drone"""
    return [b for b in bp.blocks if b['xtype'].replace('MyObjectBuilder_', '') in CRIT_X]

def _add3(a, b, k=1): return (a[0] + k * b[0], a[1] + k * b[1], a[2] + k * b[2])

def open_faces(bp, occ=None, box=None, blocks=None):
    """[(block, direction, ray cells)] for every critical block face with no occupied cell between it and the bounding box (the survivability audit's
    cover 0; minimum over the block's cells: the ray is that of the first block cell that sees open space)"""
    occ = occ if occ is not None else bp.occupancy()
    box = box or kit_outline(bp)
    out = []
    for b in (blocks if blocks is not None else critical_blocks(bp)):
        for d in AXIS_DIRS:
            v = DIR[d]
            for c in bp.cells(b):
                ray = []; p = _add3(c, v); hit = False
                while in_outline(p, box):
                    if p in occ and occ[p] is not b: hit = True; break
                    if p not in occ: ray.append(p)
                    p = _add3(p, v)
                if not hit:
                    out.append((b, d, ray)); break
    return out

def _dock_ok(bp, cell):
    """a new block at `cell` keeps every merge block's docking face clear (check_docking's rule)"""
    for m in bp.blocks:
        if m['kind'] != 'merge': continue
        d = merge_clamp_dir(bp, m); v = DIR[d]
        ax = [abs(c) for c in v].index(1); sg = v[ax]
        reach = bp.cells(m)[0][ax] * sg
        if (cell[ax] * sg > reach) if m.get('flush') else (cell[ax] * sg >= reach): return False
    return True

def _attached(bp, b):
    """True if block b (already in bp.blocks) has at least one mount-point neighbour"""
    rects = bp.mount_rects(b)
    for nb in bp.blocks:
        if nb is b: continue
        for (axis, pc, ns, lo, hi, ex, pr) in rects:
            for (a2, pc2, ns2, lo2, hi2, ex2, pr2) in bp.mount_rects(nb):
                if a2 == axis and pc2 == pc and ns2 == -ns and lo[0] <= hi2[0] and lo2[0] <= hi[0] and lo[1] <= hi2[1] and lo2[1] <= hi[1]:
                    if ((ex & pr2) or (pr & ex2)) and b['key'] != nb['key']: continue
                    return True
    return False

def _try_place(bp, xtype, sub, cell, orients, name, kind, color=KIT_COLOR, **kw):
    """add the block at the first orientation where it attaches; returns the block or None (nothing left behind)"""
    for fwd, up in orients:
        b = bp.add(xtype, sub, cell, fwd, up, name, color, kind=kind, **kw)
        if _attached(bp, b): return b
        bp.blocks.remove(b)
    return None

def kit_free_cells(bp, outline, avoid=()):
    """free cells inside the outline that are not reserved (exhaust, muzzle, camera lanes), not in `avoid` and keep the docking faces clear"""
    from itertools import product
    occ = bp.occupancy()
    if isinstance(outline, (set, frozenset)): cand = sorted(outline)
    else: cand = list(product(*[range(outline[0][i], outline[1][i] + 1) for i in range(3)]))
    avoid = {tuple(c) for c in avoid}
    return [c for c in cand if c not in occ and c not in bp.reserve and c not in avoid and _dock_ok(bp, c)]

def _crit_centroid(bp):
    cr = critical_blocks(bp) or bp.blocks
    pts = [c for b in cr for c in bp.cells(b)]
    return tuple(sum(p[i] for p in pts) / len(pts) for i in range(3))

def _d(a, b): return sum((a[i] - b[i]) ** 2 for i in range(3)) ** 0.5

def _near_crit(bp, cell, occ):
    """number of critical blocks among the 26 neighbours of `cell`"""
    crit = {id(b) for b in critical_blocks(bp)}
    seen = set()
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                o = occ.get((cell[0] + dx, cell[1] + dy, cell[2] + dz))
                if o is not None and id(o) in crit: seen.add(id(o))
    return len(seen)

def _cover_gain(cell, opens):
    return sum(1 for (b, d, ray) in opens if cell in ray)

def _enclosure(cell, occ):
    return sum(1 for v in DIR.values() if _add3(cell, v) in occ)

def add_survival_kit(bp, outline=None, decoys=2, flares=1, projector=True, model=None, panels=True, avoid=(),
                     decoy_cells=None, flare_at=None, projector_at=None):
    """Survival kit (docs/design/survivability.md items 11, 12, 13, 15), every part inside `outline` (default: the strict bounding box, kit_outline):
      projector  one Small/LargeProjector '<Model> Projector [Self]', Enabled off, holding the drone itself as a ghost (self_projection(), filled LAST so the
                 ghost is the finished drone).  Cell: the attachable free cell that closes the most open critical faces, then the most enclosed one.
      flare      `flares` (0/1) Small/LargeFlareLauncher '<Model> Flare [Flare]', muzzle outward with KIT_FLARE_CLEAR free cells, KIT_FLARE_CLIPS FlareClips loaded.
      decoys     `decoys` Small/LargeDecoy '<Model> Decoy n' at the free attachable cells farthest from the critical blocks (RC, PBs, gyros, power), the pair
                 as far apart as possible; cells next to a critical block are avoided.
      panels     (item 13) a light armour panel in the first free cell outside each remaining open critical face, plate on the far face if it mounts there,
                 else on the near face.
    `avoid` = cells that must stay empty (e.g. welder lanes); decoy_cells / flare_at=(cell, fwd) / projector_at override the search.  Call it after
    add_support_pbs (the projection must hold the support PBs).  Records what went where in bp.kit; validates."""
    model = model or bp.name
    S = KIT_SUBS[bp.grid]
    outline = outline or kit_outline(bp)
    kit = dict(outline=outline, decoys=[], flare=None, projector=None, panels=[], skipped=[])
    bp.kit = kit
    all_or = [(f, u) for f in AXIS_DIRS for u in AXIS_DIRS if sum(a * b for a, b in zip(DIR[f], DIR[u])) == 0]
    box = kit_outline(bp)
    def skip(what):
        kit['skipped'].append(what); bp.notes.append(f'survival kit: {what}')
    # 1. projector
    if projector:
        cells = [projector_at] if projector_at else kit_free_cells(bp, outline, avoid)
        occ = bp.occupancy(); opens = open_faces(bp, occ, box)
        cells.sort(key=lambda c: (-_cover_gain(c, opens), -_enclosure(c, occ), c))
        pj = None
        for c in cells:
            pj = _try_place(bp, *S['proj'], c, [('Forward', 'Up')], f'{model} Projector [Self]', 'proj', enabled=False)
            if pj: break
        if pj: kit['projector'] = pj
        else: skip('no cell for the self-projector')
    # 2. flare launcher
    if flares:
        occ = bp.occupancy(); opens = open_faces(bp, occ, box)
        if flare_at: tries = [flare_at]
        else:
            tries = []
            for c in kit_free_cells(bp, outline, avoid):
                for f in ('Up', 'Left', 'Right', 'Down', 'Forward', 'Backward'):
                    lane = [_add3(c, DIR[f], i) for i in range(1, KIT_FLARE_CLEAR + 1)]
                    if any(p in occ for p in lane): continue
                    back = occ.get(_add3(c, DIR[OPP[f]]))
                    tries.append((-_cover_gain(c, opens), back is None, -_enclosure(c, occ), ('Up', 'Left', 'Right', 'Down', 'Forward', 'Backward').index(f), c, f))
            tries.sort()
            tries = [(t[4], t[5]) for t in tries]
        fl = None
        for c, f in tries:
            ors = [(f, u) for (ff, u) in all_or if ff == f]
            fl = _try_place(bp, *S['flare'], c, ors, f'{model} Flare [Flare]', 'flare')
            if fl:
                bp.keep_clear(fl, KIT_FLARE_CLEAR, 'muzzle')
                try:
                    from sebp_inv import Item
                    bp.preload(fl, items=[Item.ammo('FlareClip', KIT_FLARE_CLIPS)])
                except Exception as e:       # an inventory shape the generator does not know: ship it empty, note it
                    bp.notes.append(f'survival kit: flare clips not preloaded ({e})')
                break
        if fl: kit['flare'] = fl
        else: skip('no cell for the flare launcher')
    # 3. decoys
    if decoys:
        occ = bp.occupancy(); cen = _crit_centroid(bp)
        if decoy_cells: pool = [tuple(c) for c in decoy_cells]
        else:
            pool = []
            for c in kit_free_cells(bp, outline, avoid):
                b = _try_place(bp, *S['decoy'], c, [('Forward', 'Up')], 'probe', 'decoy')
                if b: bp.blocks.remove(b); pool.append(c)
            pool.sort(key=lambda c: (_near_crit(bp, c, occ) > 0, -_d(c, cen), c))
        placed = []
        if not decoy_cells and len(pool) >= 2 and decoys >= 2:
            top = [c for c in pool if _near_crit(bp, c, occ) == 0] or pool
            far = _d(top[0], cen)
            top = [c for c in top if _d(c, cen) >= 0.7 * far][:40] or top[:40]
            best = max(((a, b) for i, a in enumerate(top) for b in top[i + 1:]), key=lambda p: (round(_d(p[0], p[1]), 3), _d(p[0], cen) + _d(p[1], cen)), default=None)
            if best: pool = list(best) + [c for c in pool if c not in best]
        for c in pool:
            if len(placed) >= decoys: break
            if c in bp.occupancy(): continue
            b = _try_place(bp, *S['decoy'], c, [('Forward', 'Up')], f'{model} Decoy {len(placed) + 1}', 'decoy')
            if b: placed.append(b)
        kit['decoys'] = placed
        if len(placed) < decoys: skip(f'{decoys - len(placed)} of {decoys} decoys found no cell')
    # 4. skin panels on the faces still open (item 13)
    if panels:
        occ = bp.occupancy()
        free = set(kit_free_cells(bp, outline, avoid))
        n = 0
        for (b, d, ray) in open_faces(bp, occ, box):
            if not ray or ray[0] not in free or ray[0] in bp.occupancy(): continue
            c = ray[0]
            far = [(f, u) for (f, u) in all_or if sebp.cross(DIR[f], DIR[u]) == DIR[d]]
            near = [(f, u) for (f, u) in all_or if sebp.cross(DIR[f], DIR[u]) == tuple(-x for x in DIR[d])]
            p = _try_place(bp, *S['panel'], c, far + near, None, 'panel', color=(0.58, -0.78, -0.25))
            if p is None:          # no panel orientation mounts: a light armour cube (same mass as the panel on small grid) where it does
                p = _try_place(bp, 'MyObjectBuilder_CubeBlock', 'SmallBlockArmorBlock' if bp.grid == 'Small' else 'LargeBlockArmorBlock', c, [('Forward', 'Up')],
                               None, 'armor', color=(0.58, -0.78, -0.25))
            if p:
                n += 1; kit['panels'].append(p)
    bp.validate()
    for m in bp.blocks:
        if m['kind'] == 'merge': check_docking(bp, m)
    for b in kit['decoys'] + [kit['flare'], kit['projector']] + kit['panels']:
        if b is not None: assert all(in_outline(c, outline) for c in bp.cells(b)), f'{bp.name}: survival kit block {b["name"] or b["sub"]} outside the outline'
    if kit['projector'] is not None: self_projection(bp, kit['projector'])
    return bp

def self_projection(bp, pj):
    """fill projector `pj` with the drone itself (item 15): printer_lib.projector_settings with T = 0, so the ghost's first block lands on the real one
    (ProjectionOffset = projector cell - first block min, printer_lib's verified rule).  The ghost is built from the drone with every projector emptied first
    (no projection inside a projection)."""
    import copy
    import printer_lib as PL
    d = copy.deepcopy(bp)
    for b in d.blocks:
        if b['xtype'] == 'MyObjectBuilder_Projector':
            for k in ('ProjectedGrids', 'ProjectionOffset', 'ProjectionRotation', 'KeepProjection', 'ProjectionsRemaining'): b['settings'].pop(k, None)
    sett, off = PL.projector_settings(d, (0, 0, 0), tuple(pj['min']), seed_text='self')
    if bp.grid != 'Small': sett['ProjectedGrids'] = sett['ProjectedGrids'].replace('<GridSizeEnum>Small</GridSizeEnum>', f'<GridSizeEnum>{bp.grid}</GridSizeEnum>')
    pj['settings'].update(sett)
    pj['self_offset'] = off
    return off

def add_gyro(bp, cell, name, fwd='Forward', up='Up'):
    """a second gyroscope (survivability item 14: no gyro single point of failure) in place of the 1-cell armour block at `cell` (or in a free cell).
    The gyro mounts on its bottom only: the cell below must hold a block with a top mount."""
    if tuple(cell) in bp.occupancy(): swap_armour(bp, cell)
    return bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro" if bp.grid == "Small" else "LargeBlockGyro", cell, fwd, up, name, kind="gyro")

def _cell_cover(occ, box, cell, own=()):
    """{direction: occupied cells between `cell` and the bounding box} (cells in `own` not counted)"""
    out = {}
    for d in AXIS_DIRS:
        v = DIR[d]; n = 0; p = _add3(cell, v)
        while in_outline(p, box):
            if p in occ and p not in own: n += 1
            p = _add3(p, v)
        out[d] = n
    return out

def bury_swap(bp, blocks=None, avoid=()):
    """Survivability item 14, goal 3: a 1-cell critical block with an open face (cover 0) trades places with a 1-cell plain armour block whose cell is covered
    on all six faces (the occupied cells do not change, so welder reach and the printers' clearances stay as they were).  The armour cube takes the block's
    old cell.  The block gets the first orientation under which bp.validate() passes (mounts, one piece); candidates are tried best cover first.
    Default blocks: programmable blocks and power blocks (batteries, reactors).  The remote control and the gyros keep their place and orientation (the
    scripts read the ship axes from them).  Returns [(block name, old cell, new cell)]."""
    occ = bp.occupancy(); box = kit_outline(bp)
    if blocks is None:
        blocks = [b for b in critical_blocks(bp) if b['xtype'].replace('MyObjectBuilder_', '') in ('MyProgrammableBlock', 'BatteryBlock', 'Reactor')]
    blocks = [b for b in blocks if len(bp.cells(b)) == 1]
    avoid = {tuple(c) for c in avoid}
    all_or = [(f, u) for f in AXIS_DIRS for u in AXIS_DIRS if sum(a * b for a, b in zip(DIR[f], DIR[u])) == 0]
    done = []
    for b in blocks:
        occ = bp.occupancy()
        cb = tuple(b['min'])
        if min(_cell_cover(occ, box, cb).values()) > 0: continue
        cands = []
        for a in bp.blocks:
            if not (a['xtype'] == 'MyObjectBuilder_CubeBlock' and a['sub'].endswith('ArmorBlock') and len(bp.cells(a)) == 1): continue
            ca = tuple(a['min'])
            if ca in avoid: continue
            cv = _cell_cover(occ, box, ca)
            if min(cv.values()) < 1: continue
            cands.append((-min(cv.values()), -sum(cv.values()), _d(ca, cb), ca, a))
        cands.sort(key=lambda t: t[:4])
        old = (b['min'], b['fwd'], b['up'])
        for _, _, _, ca, a in cands:
            a['min'] = cb; b['min'] = ca
            ok = False
            for f, u in [(b['fwd'], b['up'])] + all_or:
                b['fwd'], b['up'] = f, u
                try:
                    bp.validate(); ok = True; break
                except AssertionError:
                    pass
            if ok:
                done.append((b['name'], cb, ca)); break
            a['min'] = ca; b['min'], b['fwd'], b['up'] = old
    bp.validate()
    return done
