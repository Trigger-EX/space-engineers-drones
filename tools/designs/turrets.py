"""Custom rotor/hinge AI turrets (scripts/TurretAI.cs) and their mounting on the warships.  Key `turrets`, doc docs/design/turrets.md.

A turret = THREE CubeGrids, copied from the six turrets of the owner's Tech Hunter (pc/enemies/Tech Hunter/bp.sbc):

    host grid ----- AzimuthStator (MotorAdvancedStator) ----- Top grid: rotor top + ElevationHinge + (armour) ----- Head grid: hinge head + guns + camera

  * the stator sits with its Bottom against the hull and its Up pointing OUT; the box is 2 cells long (Up); the rotor top is 0.2 m above the
    lower cell's centre and the hinge sits in the upper cell (sebp_multi.TOP_OFFSETS, measured);
  * the hinge axis is horizontal, the head's Left (the bore) is the hinge's Left = the azimuth axis, so at angle 0/0 the guns point straight
    out of the deck.  Hinge range -90..+90 deg, azimuth free: the turret covers the whole half-space on its outward side;
  * pointing at a direction d (mount frame, `a` = azimuth axis): hinge axis h = +-(a x d)/|a x d|, hinge angle = +-angle(a, d), azimuth angle
    = the turn that brings the hinge axis to h.  TurretAI.cs does exactly this (docs/design/turrets.md s5).

Models:  PD4L  large-grid host (Warden, Leviathan): LargeAdvancedStator / LargeAdvancedRotor / LargeHinge / SmallHingeHead, 4 x SmallGatlingGun   [seen]
         PD2S  small-grid host (Shrike, Corsair): SmallAdvancedStatorSmall / SmallAdvancedRotorSmall / SmallHinge / SmallHingeHead, 2 x SmallGatlingGun [UNSEEN]

Names:   every turret block starts with the prefix `[TU]` (never the ship's `[SH]`, so the Helm's gun scan `Own()` ignores them).
         `[TU] T<n> Azimuth`, `[TU] T<n> Elevation`, `[TU] T<n> Gun <k>`, `[TU] T<n> Camera`; the PB is `[TU] Turret PB`.
API (append-only):  add_turret(host, n, lo_cell, out, fwd=None, model='PD4L', lite=False) -> Mount
                    sweep(mount) / sweep_report(host, mounts) / masks(host, mount) / turret_custom_data(host, mounts)
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import sebp, sebp_multi as MU
from sebp import Blueprint, DIR

PREFIX = '[TU]'
GUN_SUB = ''
GUN_X = 'MyObjectBuilder_SmallGatlingGun'

MODELS_T = {
    'PD4L': dict(host='Large', top='Large', stator='LargeAdvancedStator', rotor='LargeAdvancedRotor', hinge='LargeHinge', head='SmallHingeHead',
                 guns=((-1, -1), (-1, 1), (1, -1), (1, 1)), seen=True, elev='hinge'),
    'PD2S': dict(host='Small', top='Small', stator='SmallAdvancedStatorSmall', rotor='SmallAdvancedRotorSmall', hinge='SmallHinge', head='SmallHingeHead',
                 guns=((-1, 0), (1, 0)), seen=False, elev='hinge'),
    # ROTOR-ELEVATION variants: the elevation joint is a second rotor (axis horizontal, no limit) instead of a hinge, so the guns reach the whole sphere
    # (elevation -180..+170, azimuth free); the head hangs on the rotor head's Top face (the only mount of a rotor head), 1 m / 0.5 m off the pivot along the axis.
    'PD4LR': dict(host='Large', top='Large', stator='LargeAdvancedStator', rotor='LargeAdvancedRotor', head='SmallRotor', hxtype='MyObjectBuilder_MotorRotor',
                  est='LargeStator', estype='MyObjectBuilder_MotorStator', guns=((-1, -1), (-1, 1), (1, -1), (1, 1)), seen=True, elev='rotor'),
    'PD2SR': dict(host='Small', top='Small', stator='SmallAdvancedStatorSmall', rotor='SmallAdvancedRotorSmall', head='SmallAdvancedRotorSmall',
                  hxtype='MyObjectBuilder_MotorAdvancedRotor', est='SmallAdvancedStatorSmall', estype='MyObjectBuilder_MotorAdvancedStator',
                  guns=((-1, 0), (1, 0)), seen=False, elev='rotor'),
}
ELEVATION = os.environ.get('TURRET_ELEVATION', 'hinge').strip().lower()      # 'hinge' (default) | 'rotor': which elevation joint the ships' turrets get
ROTOR_OF = {'PD4L': 'PD4LR', 'PD2S': 'PD2SR'}
HOST_OF = {'PD4L': 'Large', 'PD4LR': 'Large', 'PD2S': 'Small', 'PD2SR': 'Small'}
ROTOR_HEAD_Y = 2                    # rotor models: head cell row of the gun axis (small cells along the elevation axis, from the rotor head at y = 0)

def is_rotor(model): return MODELS_T[model]['elev'] == 'rotor'
def el_range(model):
    """(first bin centre deg, bin step deg, bins, search range deg (lo, hi)) of the elevation joint: hinge -90..+90 in 19 bins, rotor -180..+170 in 36 bins"""
    return (-180.0, 10.0, 36, (-180.0, 180.0)) if is_rotor(model) else (-90.0, 10.0, 19, (-90.0, 90.0))
AZ_TORQUE = 1.0e6                  # N*m written to the stators (TurretAI.cs sets its own values at start; 3.36E+07 is the game's maximum on the large ones)
HINGE_LIMIT = 1.57079637           # +-90 deg: the hinge's own limit (defs_stats MinAngleDeg/MaxAngleDeg)

TORQUE_TXT = '1E+06'

def _nil(tag):
    import xml.etree.ElementTree as ET
    e = ET.Element(tag); e.set(sebp.XSI + 'nil', 'true'); return e

class Mount:
    """one turret: the stator in the host, the top and head grids, and what the checks need"""
    def __init__(self, **kw): self.__dict__.update(kw)
    def blocks(self): return [self.stator] + self.top.blocks + self.head.blocks

def _head_cells(model, lite):
    """armour cells of the head grid (small cells; the head block is (0,0,0), its Left face is -x, the bore).  The Tech Hunter head, with the
    guns 4 cells long instead of the autocannon's 5."""
    g = set(MODELS_T[model]['guns'])
    if is_rotor(model):
        # rotor head: the structure grows from the rotor head's Top face (0,1,0) along the elevation axis (head y); gun rows y = ROTOR_HEAD_Y + gy
        wide = all(z == 0 for (y, z) in g)
        cells = {(x, y, z) for x in (0, -1) for y in range(1, 2 * ROTOR_HEAD_Y) for z in (range(-1, 2) if wide else range(-2, 3))}
        for (y, z) in g:
            for x in range(-3, 1): cells.discard((x, y + ROTOR_HEAD_Y, z))
        cells.discard((-2, ROTOR_HEAD_Y, 0))
        return sorted(cells)
    cells = set()
    # bracket plate behind the camera and around the gun roots; the plus-shaped ring at x = -1..-2 stays out of the camera's view
    wide_y = all(z == 0 for (y, z) in g)            # PD2S: guns side by side along the hinge axis (head y, always horizontal): a flat plate swings low only 0.5 m
    for x in (0, -1):
        for y in range(-1, 2):
            for z in (range(-1, 2) if wide_y else range(-2, 3)):
                cells.add((x, y, z))
    cells.discard((0, 0, 0))                                          # the hinge head
    for (y, z) in g:                                                  # gun cells x = -3..0
        for x in range(-3, 1): cells.discard((x, y, z))
    # keep only what touches something (computed by the validator later), drop what a 2-gun model leaves dangling
    return sorted(cells)

def _armor_head(h, cells, light=True):
    sub = 'SmallBlockArmorBlock'
    for (x, y, z) in cells:
        h.add('MyObjectBuilder_CubeBlock', sub, (x, y, z), color=C.PAL and (0, -0.81, -0.45), mass=20)

def add_turret(host, n, lo_cell, out, fwd=None, model='PD4L', lite=False, tag=PREFIX, color=GUN):
    """Put turret number `n` on `host`.  `lo_cell` = the host cell the stator's Bottom touches the hull with (the LOWER cell of its 2-cell box), `out` = the
    outward direction name ('Up', 'Down', 'Left', ...); the box is lo_cell and lo_cell + out.  `fwd` = the stator's Forward (azimuth zero, default: the
    host's Forward, or Up if out is Forward/Backward).  Returns the Mount; blocks are not collision-checked against the host (reserve the cells first)."""
    if ELEVATION == 'rotor' and model in ROTOR_OF: model = ROTOR_OF[model]         # ship generators ask for PD4L / PD2S; TURRET_ELEVATION=rotor builds the rotor-elevation twins
    M = MODELS_T[model]
    assert host.grid == M['host'], f'{model} needs a {M["host"]} host grid'
    rotor_el = is_rotor(model)
    if fwd is None: fwd = 'Up' if out in ('Forward', 'Backward') else 'Forward'
    o = DIR[out]
    hi_cell = tuple(lo_cell[i] + o[i] for i in range(3))
    mn = tuple(min(lo_cell[i], hi_cell[i]) for i in range(3))
    nm = f'{tag} T{n}'
    az = host.add('MyObjectBuilder_MotorAdvancedStator', M['stator'], mn, fwd, out, f'{nm} Azimuth', color=color, kind='tmount',
                  settings={'Torque': TORQUE_TXT, 'LimitsActive': 'false', 'MinAngle': _nil('MinAngle'), 'MaxAngle': _nil('MaxAngle')})
    # ---- top grid: rotor head at (0,0,0), pedestal-free hinge at (0,1,0) exactly as in the Tech Hunter ----
    top = Blueprint(f'{nm} Top', M['top'], seed=C.seed_for(nm + 'top'), allow_dlc=host.allow_dlc, legacy_shapes=host.legacy_shapes)
    top.display = f'{nm} Top'
    rotor = top.add('MyObjectBuilder_MotorAdvancedRotor', M['rotor'], (0, 0, 0), 'Forward', 'Up', None, kind='turret')
    if rotor_el:
        # the 1 x 2 x 1 stator lies on the rotor head, axis (its Up) = the grid's Left; its own rotor head sits 0.17 / 0.08 cell off the azimuth axis (measured offsets)
        hinge = top.add(M['estype'], M['est'], (-1, 1, 0), 'Backward', 'Left', f'{nm} Elevation', color=color, kind='turret',
                        settings={'Torque': TORQUE_TXT, 'LimitsActive': 'false', 'MinAngle': _nil('MinAngle'), 'MaxAngle': _nil('MaxAngle')})
    else:
        hinge = top.add('MyObjectBuilder_MotorAdvancedStator', M['hinge'], (0, 1, 0), 'Backward', 'Left', f'{nm} Elevation', color=color, kind='turret',
                        settings={'Torque': TORQUE_TXT, 'LimitsActive': 'true', 'MinAngle': f'-{HINGE_LIMIT}', 'MaxAngle': str(HINGE_LIMIT)})
    # ---- head grid ----
    head = Blueprint(f'{nm} Head', 'Small', seed=C.seed_for(nm + 'head'), allow_dlc=host.allow_dlc, legacy_shapes=host.legacy_shapes)
    head.display = f'{nm} Head'
    hb = head.add(M.get('hxtype', 'MyObjectBuilder_MotorAdvancedRotor'), M['head'], (0, 0, 0), 'Forward', 'Up', None, kind='turret')
    gy0 = ROTOR_HEAD_Y if rotor_el else 0
    for (x, y, z) in _head_cells(model, lite):
        head.add('MyObjectBuilder_CubeBlock', 'SmallBlockArmorBlock', (x, y, z), color=(0, -0.81, -0.45), mass=20)
    guns = []
    for k, (y, z) in enumerate(M['guns']):
        # Left-facing gun (muzzle toward -x); the side the gun leans on is set by Up
        g = head.add(GUN_X, GUN_SUB, (-3, y + gy0, z), 'Left', 'Backward' if z > 0 else ('Up' if y != 0 and z == 0 else 'Forward'), f'{nm} Gun {k + 1}', color=color, kind='gun')
        head.fill_ammo(g)
        guns.append(g)
    cam = head.add('MyObjectBuilder_CameraBlock', 'SmallCameraBlock', (-2, gy0, 0), 'Left', 'Backward', f'{nm} Camera', color=color, kind='cam')
    host.mount(top, az, rotor, 0.0)
    top.mount(head, hinge, hb, 0.0)
    return Mount(n=n, model=model, name=nm, host=host, stator=az, top=top, head=head, rotor=rotor, hinge=hinge, headblk=hb, guns=guns, cam=cam,
                 lo_cell=tuple(lo_cell), out=out, fwd=fwd, elev=M['elev'])


# =====================================================================================================================
# geometry checks: the swing envelope, the movement mask and the line-of-fire mask
# =====================================================================================================================
AZ_BINS, EL_BINS = 36, 19           # 10 deg bins: azimuth 0..350 (stator angle), hinge -90..+90 (EL_BINS = the hinge models; a rotor elevation has 36 bins, -180..+170: el_range())
FIRE_RANGE = 60.0                   # m of the bore that must be free of the host's own hull

def _occ_cells(host, m):
    """host cells that count as hull for turret m: everything except the stator's own box"""
    own = set(host.cells(m.stator))
    occ = set()
    for b in host.blocks:
        for c in host.cells(b):
            if c not in own: occ.add(c)
    return occ

def _hit(occ, cs, p, margin):
    lo = [math.floor((p[i] - margin) / cs + 0.5) for i in range(3)]
    hi = [math.floor((p[i] + margin) / cs + 0.5) for i in range(3)]
    for x in range(lo[0], hi[0] + 1):
        for y in range(lo[1], hi[1] + 1):
            for z in range(lo[2], hi[2] + 1):
                if (x, y, z) in occ: return True
    return False

def _poses(m, az, el):
    """(top grid pose, head grid pose) in the HOST frame for stator angle az and hinge angle el (rad)"""
    pt = MU.top_pose(m.host, m.stator, m.top, m.rotor, az)
    ph = pt * MU.top_pose(m.top, m.hinge, m.head, m.headblk, el)
    return pt, ph

def _sample_points(bp, blk_filter=None):
    """local points to test for a grid: every cell centre of the head (margin 0.25), the 27 sample points of every top-grid cell (cell is large)"""
    cs = MU.CELL[bp.grid]
    pts = []
    for b in bp.blocks:
        for c in bp.cells(b):
            base = tuple(v * cs for v in c)
            if bp.grid == 'Small' or cs < 1: pts.append(base)
            else:
                for dx in (-0.45, 0, 0.45):
                    for dy in (-0.45, 0, 0.45):
                        for dz in (-0.45, 0, 0.45):
                            pts.append((base[0] + dx * cs, base[1] + dy * cs, base[2] + dz * cs))
    return pts

def _wrapel(m, e):
    """elevation in degrees as the sweep / mask grid keys it: a rotor wraps into [-180, 180), a hinge keeps its value"""
    return ((e + 180.0) % 360.0) - 180.0 if m.elev == 'rotor' else e

def sweep(m, occ=None, step_az=5.0, step_el=5.0):
    """{(az_deg, el_deg): True if the moving parts clear the host hull} on a (step_az x step_el) deg grid, az 0..355, el -90..90 (hinge) / -180..175 (rotor)"""
    occ = occ if occ is not None else _occ_cells(m.host, m)
    host_cs = MU.CELL[m.host.grid]
    pts_top, pts_head = _sample_points(m.top), _sample_points(m.head)
    out = {}
    lo_e, hi_e = el_range(m.model)[3]
    nA, nE = int(360 / step_az), int((hi_e - lo_e) / step_el) + (0 if m.elev == 'rotor' else 1)
    for i in range(nA):
        az = math.radians(i * step_az)
        pt = MU.top_pose(m.host, m.stator, m.top, m.rotor, az)
        top_ok = not any(_hit(occ, host_cs, pt.apply(p), 0.05) for p in pts_top)
        for j in range(nE):
            e = lo_e + j * step_el
            ph = pt * MU.top_pose(m.top, m.hinge, m.head, m.headblk, math.radians(e))
            ok = top_ok and not any(_hit(occ, host_cs, ph.apply(p), 0.24) for p in pts_head)
            out[(i * step_az, e)] = ok
    return out

def radius(m):
    """radius of the sphere around the hinge centre that holds the head and everything on it (+ 0.25 m)"""
    c0 = (0.0, 0.0, 0.0)
    r = 0.0
    cs = MU.CELL[m.head.grid]
    for b in m.head.blocks:
        for c in m.head.cells(b): r = max(r, math.sqrt(sum((v * cs) ** 2 for v in c)))
    return r + 0.25

def hinge_centre(m):
    """pivot of the elevation joint (= the head grid's origin, the hinge head / rotor head cell) in the host frame (metres)"""
    return _poses(m, 0.0, 0.0)[1].apply((0.0, 0.0, 0.0))

def fire_mask(m, occ=None, az_bins=AZ_BINS, el_bins=None):
    """blocked[(ai, ej)] for the BORE of every bin centre: a ray from each gun muzzle along the bore through the host hull, FIRE_RANGE m"""
    occ = occ if occ is not None else _occ_cells(m.host, m)
    e0, estep, el_bins, _ = el_range(m.model)
    cs = MU.CELL[m.host.grid]; hs = MU.CELL[m.head.grid]
    muz = []
    for g in m.guns:
        cells = m.head.cells(g)
        tip = min(cells, key=lambda c: c[0])                          # the bore is the head's -x
        muz.append(tuple(v * hs for v in (tip[0] - 0.5, tip[1], tip[2])))
    blocked = {}
    for ai in range(az_bins):
        for ej in range(el_bins):
            az, el = math.radians(ai * 360 / az_bins), math.radians(e0 + ej * estep)
            _, ph = _poses(m, az, el)
            d = ph.rot.apply((-1.0, 0.0, 0.0))
            bad = False
            for mp in muz:
                o = ph.apply(mp)
                t = 0.0
                while t <= FIRE_RANGE:
                    if _hit(occ, cs, (o[0] + d[0] * t, o[1] + d[1] * t, o[2] + d[2] * t), 0.0): bad = True; break
                    t += 1.0 if cs > 1 else 0.5
                if bad: break
            blocked[(ai, ej)] = bad
    return blocked

def move_mask(m, occ=None, az_bins=AZ_BINS, el_bins=None):
    """blocked[(ai, ej)] = the moving parts hit the hull somewhere in the 10 deg x 10 deg bin around the bin centre (5 deg grid, edges included)"""
    sw = sweep(m, occ)
    f0, fstep, el_bins, (lo_e, hi_e) = el_range(m.model)
    blocked = {}
    for ai in range(az_bins):
        for ej in range(el_bins):
            a0, e0 = ai * 10.0, f0 + ej * fstep
            bad = False
            for da in (-5.0, 0.0, 5.0):
                for de in (-5.0, 0.0, 5.0):
                    e = e0 + de
                    if m.elev == 'rotor': e = _wrapel(m, e)
                    elif e < lo_e or e > hi_e: continue
                    if not sw[((a0 + da) % 360.0, e)]: bad = True
            blocked[(ai, ej)] = bad
    return blocked

def _pack(blocked, el_bins=EL_BINS):
    """36 rows x el_bins bits -> 36 rows of ceil(el_bins / 4) hex digits (bit j = elevation bin j, first = -90 (hinge, 19 bins, 5 digits) / -180 (rotor, 36 bins, 9 digits))"""
    out = []
    nd = (el_bins + 3) // 4
    for ai in range(AZ_BINS):
        v = 0
        for ej in range(el_bins):
            if blocked[(ai, ej)]: v |= 1 << ej
        out.append('%0*X' % (nd, v))
    return ''.join(out)

def masks(m):
    """(move mask string, fire mask string) of turret m; 1 = blocked"""
    occ = _occ_cells(m.host, m)
    nb = el_range(m.model)[2]
    return _pack(move_mask(m, occ), nb), _pack(fire_mask(m, occ), nb)

def coverage(fire_blocked):
    """fraction of the (azimuth x hinge) bins whose line of fire is clear"""
    n = sum(1 for v in fire_blocked.values() if not v)
    return n / len(fire_blocked)

def sphere_conflicts(mounts):
    """pairs of mounts on one host whose swing spheres overlap"""
    out = []
    ctr = [(hinge_centre(m), radius(m)) for m in mounts]
    for i in range(len(mounts)):
        for j in range(i + 1, len(mounts)):
            if MU._norm(MU._sub(ctr[i][0], ctr[j][0])) < ctr[i][1] + ctr[j][1]: out.append((mounts[i].name, mounts[j].name))
    return out

def turret_custom_data(host, mounts, ship_tag, extra=''):
    """Custom Data of the turret PB: [Ship], [Turret] settings and one [Mount Tn] section per turret with its masks"""
    L = ['[Ship]', f'Tag={ship_tag}', '', '[Turret]', f'Prefix={PREFIX}', 'Fire Mode=FREE', 'Range=700', 'Park Delay=8', 'Gain=4', 'Max Rate=3', 'Max Accel=12']
    if extra: L.append(extra)
    for m in mounts:
        mv, fr = masks(m)
        L += ['', f'[Mount T{m.n}]']
        if m.elev == 'rotor': L += ['Elevation=ROTOR', 'El Min=-180', 'El Step=10', 'El Bins=36']
        L += ['Move Mask=' + mv, 'Fire Mask=' + fr]
    return '\n'.join(L) + '\n'


# =====================================================================================================================
# glue shared by the ship generators
# =====================================================================================================================
def all_blocks(mounts):
    """every block of the turret grids (rotor tops, hinge, head parts) of the given mounts"""
    return [b for m in mounts for b in (m.top.blocks + m.head.blocks)]

def check_mounts(host, mounts, min_cover=0.5):
    """raise AssertionError when a turret's moving parts can hit the host hull anywhere in the range the script may use it in AFTER the generated
    mask (that is the masks' job, so here only: the PARKED pose and the whole upper hemisphere of the top parts), when two swing spheres overlap,
    or when a turret sees less than `min_cover` of its half-space.  Returns {mount name: dict(cover, blocked_move)}."""
    bad = sphere_conflicts(mounts)
    assert not bad, f'{host.name}: turret swing spheres overlap: {bad}'
    rep = {}
    for m in mounts:
        occ = _occ_cells(host, m)
        mv = move_mask(m, occ); fb = fire_mask(m, occ)
        cov = coverage(fb)
        if m.elev == 'rotor': min_cover = min_cover / 2          # a rotor elevation sweeps the whole sphere: the half below the deck is hull by construction
        assert cov >= min_cover, f'{host.name}: {m.name}: only {cov:.0%} of its bins have a free line of fire'
        # parked (az 0, hinge 0) and every azimuth at hinge 0 must be free: the script parks there
        e0, estep, _nb, _ = el_range(m.model); j0 = int(round(-e0 / estep))
        stuck = [ai * 10 for ai in range(AZ_BINS) if mv[(ai, j0)]]
        if m.elev == 'rotor':
            # the 5 m elevation stator swings a 3.75 m (large) radius round the azimuth axis: azimuths where it meets the hull are masked off (Move Mask), the turret works the rest;
            # at least 60 % of the circle must be free or the site is too tight for this model
            assert len(stuck) <= 0.4 * AZ_BINS, f'{host.name}: {m.name}: the elevation stator hits the hull at {len(stuck) * 10} deg of azimuth, the site is too tight for the rotor model'
        else:
            assert not stuck, f'{host.name}: {m.name}: the parked pose (hinge 0, azimuth {stuck[0]}) hits the hull'
        rep[m.name] = dict(cover=round(cov, 3), blocked_move=sum(mv.values()), bins=len(mv), blocked_az=len(stuck) * 10)
    return rep

def mounts_stats(mounts):
    """{'blocks', 'pcu', 'mass_kg', 'guns', 'moving_mass_kg'} of the turret grids + their stators (stators sit in the host: counted here too)"""
    st = C.stats_db()
    blocks = all_blocks(mounts) + [m.stator for m in mounts]
    def stat(b): return C._stat(b)
    return dict(turrets=len(mounts), blocks=len(blocks), pcu=sum(stat(b).get('pcu') or 0 for b in blocks), mass_kg=round(sum(C.block_mass(b) for b in blocks)),
                guns=sum(len(m.guns) for m in mounts), moving_mass_kg=round(sum(C.block_mass(b) for b in all_blocks(mounts))))

def slew_rates(m):
    """analytic angular acceleration (rad/s2) of the azimuth and hinge axes with `AZ_TORQUE` (docs/design/turrets.md s3) and the moment of inertia (kg m2)"""
    def inertia(bp, pose_pts, axis):
        s = 0.0
        for b, p in pose_pts:
            r2 = sum(v * v for v in p) - sum(a * v for a, v in zip(axis, p)) ** 2
            s += C.block_mass(b) * r2
        return s
    hs = MU.CELL[m.head.grid]; ts = MU.CELL[m.top.grid]
    hc = hinge_centre(m); 
    # azimuth axis through the stator; points of head parts relative to the axis in the HOST frame at angle 0 (axis = host 'out')
    pt, ph = _poses(m, 0.0, 0.0)
    out = DIR[m.out]
    pts = []
    for b in m.head.blocks:
        for c in m.head.cells(b): pts.append((b, ph.apply(tuple(v * hs for v in c)), len(m.head.cells(b))))
    for b in m.top.blocks:
        for c in m.top.cells(b): pts.append((b, pt.apply(tuple(v * ts for v in c)), len(m.top.cells(b))))
    stc = MU.block_pose(m.host, m.stator).pos
    def I_about(axis, centre, only_head=False):
        s = 0.0
        for b, p, n in pts:
            if only_head and not any(b is x for x in m.head.blocks): continue
            q = MU._sub(p, centre); r2 = sum(v * v for v in q) - sum(a * v for a, v in zip(axis, q)) ** 2
            s += C.block_mass(b) / n * r2
        return s
    Ia = I_about(out, stc)
    h0 = pt.rot.apply(MU.block_pose(m.top, m.hinge).rot.up)
    Ie = I_about(h0, hc, True)
    return dict(I_az=round(Ia), I_el=round(Ie), alpha_az=round(AZ_TORQUE / Ia, 1), alpha_el=round(AZ_TORQUE / Ie, 1))
