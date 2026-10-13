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
                 guns=((-1, -1), (-1, 1), (1, -1), (1, 1)), seen=True),
    'PD2S': dict(host='Small', top='Small', stator='SmallAdvancedStatorSmall', rotor='SmallAdvancedRotorSmall', hinge='SmallHinge', head='SmallHingeHead',
                 guns=((-1, 0), (1, 0)), seen=False),
}
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
    M = MODELS_T[model]
    assert host.grid == M['host'], f'{model} needs a {M["host"]} host grid'
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
    hinge = top.add('MyObjectBuilder_MotorAdvancedStator', M['hinge'], (0, 1, 0), 'Backward', 'Left', f'{nm} Elevation', color=color, kind='turret',
                    settings={'Torque': TORQUE_TXT, 'LimitsActive': 'true', 'MinAngle': f'-{HINGE_LIMIT}', 'MaxAngle': str(HINGE_LIMIT)})
    # ---- head grid ----
    head = Blueprint(f'{nm} Head', 'Small', seed=C.seed_for(nm + 'head'), allow_dlc=host.allow_dlc, legacy_shapes=host.legacy_shapes)
    head.display = f'{nm} Head'
    hb = head.add('MyObjectBuilder_MotorAdvancedRotor', M['head'], (0, 0, 0), 'Forward', 'Up', None, kind='turret')
    for (x, y, z) in _head_cells(model, lite):
        head.add('MyObjectBuilder_CubeBlock', 'SmallBlockArmorBlock', (x, y, z), color=(0, -0.81, -0.45), mass=20)
    guns = []
    for k, (y, z) in enumerate(M['guns']):
        # Left-facing gun (muzzle toward -x); the side the gun leans on is set by Up
        g = head.add(GUN_X, GUN_SUB, (-3, y, z), 'Left', 'Backward' if z > 0 else ('Up' if y != 0 and z == 0 else 'Forward'), f'{nm} Gun {k + 1}', color=color, kind='gun')
        head.fill_ammo(g)
        guns.append(g)
    cam = head.add('MyObjectBuilder_CameraBlock', 'SmallCameraBlock', (-2, 0, 0), 'Left', 'Backward', f'{nm} Camera', color=color, kind='cam')
    host.mount(top, az, rotor, 0.0)
    top.mount(head, hinge, hb, 0.0)
    return Mount(n=n, model=model, name=nm, host=host, stator=az, top=top, head=head, rotor=rotor, hinge=hinge, headblk=hb, guns=guns, cam=cam,
                 lo_cell=tuple(lo_cell), out=out, fwd=fwd)


# =====================================================================================================================
# geometry checks: the swing envelope, the movement mask and the line-of-fire mask
# =====================================================================================================================
AZ_BINS, EL_BINS = 36, 19           # 10 deg bins: azimuth 0..350 (stator angle), hinge -90..+90
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

def sweep(m, occ=None, step_az=5.0, step_el=5.0):
    """{(az_deg, el_deg): True if the moving parts clear the host hull} on a (step_az x step_el) deg grid, az 0..355, el -90..90"""
    occ = occ if occ is not None else _occ_cells(m.host, m)
    host_cs = MU.CELL[m.host.grid]
    pts_top, pts_head = _sample_points(m.top), _sample_points(m.head)
    out = {}
    nA, nE = int(360 / step_az), int(180 / step_el) + 1
    for i in range(nA):
        az = math.radians(i * step_az)
        pt = MU.top_pose(m.host, m.stator, m.top, m.rotor, az)
        top_ok = not any(_hit(occ, host_cs, pt.apply(p), 0.05) for p in pts_top)
        for j in range(nE):
            e = -90.0 + j * step_el
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
    """hinge centre in the host frame (metres)"""
    return MU.top_pose(m.host, m.stator, m.top, m.rotor, 0.0).apply(tuple(v * MU.CELL[m.top.grid] for v in m.hinge['min']))

def fire_mask(m, occ=None, az_bins=AZ_BINS, el_bins=EL_BINS):
    """blocked[(ai, ej)] for the BORE of every bin centre: a ray from each gun muzzle along the bore through the host hull, FIRE_RANGE m"""
    occ = occ if occ is not None else _occ_cells(m.host, m)
    cs = MU.CELL[m.host.grid]; hs = MU.CELL[m.head.grid]
    muz = []
    for g in m.guns:
        cells = m.head.cells(g)
        tip = min(cells, key=lambda c: c[0])                          # the bore is the head's -x
        muz.append(tuple(v * hs for v in (tip[0] - 0.5, tip[1], tip[2])))
    blocked = {}
    for ai in range(az_bins):
        for ej in range(el_bins):
            az, el = math.radians(ai * 360 / az_bins), math.radians(-90 + ej * 180 / (el_bins - 1))
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

def move_mask(m, occ=None, az_bins=AZ_BINS, el_bins=EL_BINS):
    """blocked[(ai, ej)] = the moving parts hit the hull somewhere in the 10 deg x 10 deg bin around the bin centre (5 deg grid, edges included)"""
    sw = sweep(m, occ)
    blocked = {}
    for ai in range(az_bins):
        for ej in range(el_bins):
            a0, e0 = ai * 10.0, -90.0 + ej * 10.0
            bad = False
            for da in (-5.0, 0.0, 5.0):
                for de in (-5.0, 0.0, 5.0):
                    e = e0 + de
                    if e < -90 or e > 90: continue
                    if not sw[((a0 + da) % 360.0, e)]: bad = True
            blocked[(ai, ej)] = bad
    return blocked

def _pack(blocked):
    """36 rows x 19 bits -> 36 x 5 hex digits (bit j = hinge bin j, -90 deg first)"""
    out = []
    for ai in range(AZ_BINS):
        v = 0
        for ej in range(EL_BINS):
            if blocked[(ai, ej)]: v |= 1 << ej
        out.append('%05X' % v)
    return ''.join(out)

def masks(m):
    """(move mask string, fire mask string) of turret m; 1 = blocked"""
    occ = _occ_cells(m.host, m)
    return _pack(move_mask(m, occ)), _pack(fire_mask(m, occ))

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
        L += ['', f'[Mount T{m.n}]', 'Move Mask=' + mv, 'Fire Mask=' + fr]
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
        assert cov >= min_cover, f'{host.name}: {m.name}: only {cov:.0%} of its bins have a free line of fire'
        # parked (az 0, hinge 0) and every azimuth at hinge 0 must be free: the script parks there
        for ai in range(AZ_BINS):
            assert not mv[(ai, EL_BINS // 2)], f'{host.name}: {m.name}: the parked pose (hinge 0, azimuth {ai * 10}) hits the hull'
        rep[m.name] = dict(cover=round(cov, 3), blocked_move=sum(mv.values()), bins=len(mv))
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
