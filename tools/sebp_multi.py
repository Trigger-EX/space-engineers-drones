"""Multi-grid blueprints for sebp: grid poses, docking geometry and connector locks.

CONVENTIONS (verified against the owner's `Venture - Expedition End`, see tools/tests/test_sebp_ext.py: the connector pair
ConnectorMedium <-> Connector is reproduced to 0.003 m and 0.001 rad from the grids' PositionAndOrientation and the block cells):

  * Every CubeGrid has its own <PositionAndOrientation> in WORLD coordinates: Position (metres), Forward, Up and the quaternion
    Orientation (X, Y, Z, W).  The grid frame has Right = Forward x Up, Up, Backward = -Forward as its columns; the quaternion is
    the standard (column-vector) quaternion of that rotation matrix.
  * Grid-local coordinates: cell (i, j, k) has its CENTRE at (i, j, k) * cellSize (2.5 m large, 0.5 m small); the grid origin is the
    centre of cell (0, 0, 0).
  * A block's frame: origin at the centre of its cell box (Min + (extent-1)/2), axes = the block's own Right/Up/Backward expressed
    in grid axes by BlockOrientation (sebp.Blueprint.axes).  A connector contacts its partner on its Forward face.
  * Connector pair in the file: both blocks carry ConnectedEntityId and the same MasterToSlaveTransform (the slave block's frame
    expressed in the master block's frame); the master also carries MasterToSlaveGrid:
        PositionOffset    = master grid origin - slave block origin, in the SLAVE BLOCK frame      (matches to 1e-3 m on both Venture pairs)
        OrientationOffset = quaternion of  slaveBlockRotation^-1 * masterGridRotation              (see test; OrientationAsVector repeats it)
    The game snaps nothing: the distance in a saved lock is whatever it was when the lock was made (Venture: 1.82 m for a
    ConnectorMedium on a Connector although the faces are 1.75 m apart; 2.63 m for two Connectors, faces 2.5 m).  The helpers
    below place the faces exactly `gap` metres apart (default 0).
"""
import math, itertools
import xml.etree.ElementTree as ET
from sebp_xml import E, XSI, _txt

def _num(v):
    """number as text with 9 significant digits (poses need more than the 6 decimals of sebp_xml._txt)"""
    t = '%.9g' % v
    return '0' if t in ('-0', '0') else t

DIRS = {"Forward": (0, 0, -1), "Backward": (0, 0, 1), "Up": (0, 1, 0), "Down": (0, -1, 0), "Left": (-1, 0, 0), "Right": (1, 0, 0)}
CELL = {'Large': 2.5, 'Small': 0.5}

def _dot(a, b): return sum(x * y for x, y in zip(a, b))
def _cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def _add(a, b): return tuple(x + y for x, y in zip(a, b))
def _sub(a, b): return tuple(x - y for x, y in zip(a, b))
def _mul(a, s): return tuple(x * s for x in a)
def _norm(a): return math.sqrt(_dot(a, a))

class Rot:
    """3x3 rotation as three COLUMN vectors (the images of the local Right, Up, Backward axes)"""
    __slots__ = ('c',)
    def __init__(self, right, up, back): self.c = (tuple(right), tuple(up), tuple(back))
    @classmethod
    def identity(cls): return cls((1, 0, 0), (0, 1, 0), (0, 0, 1))
    @classmethod
    def from_axes(cls, fwd='Forward', up='Up'):
        """one of the 24 axis-aligned orientations: where the local Forward and Up point (names of grid directions)"""
        f, u = DIRS[fwd], DIRS[up]
        if _dot(f, u) != 0: raise ValueError(f'forward {fwd} and up {up} are not perpendicular')
        return cls(_cross(f, u), u, _mul(f, -1))
    @classmethod
    def from_forward_up(cls, f, u):
        f = _mul(f, 1 / _norm(f)); u = _sub(u, _mul(f, _dot(u, f))); u = _mul(u, 1 / _norm(u))
        return cls(_cross(f, u), u, _mul(f, -1))
    @classmethod
    def from_quat(cls, x, y, z, w):
        n = math.sqrt(x*x + y*y + z*z + w*w); x, y, z, w = x/n, y/n, z/n, w/n
        return cls((1-2*(y*y+z*z), 2*(x*y+z*w), 2*(x*z-y*w)), (2*(x*y-z*w), 1-2*(x*x+z*z), 2*(y*z+x*w)), (2*(x*z+y*w), 2*(y*z-x*w), 1-2*(x*x+y*y)))
    def apply(self, v):
        return tuple(sum(self.c[j][i] * v[j] for j in range(3)) for i in range(3))
    def inv(self):
        r = self.c
        return Rot((r[0][0], r[1][0], r[2][0]), (r[0][1], r[1][1], r[2][1]), (r[0][2], r[1][2], r[2][2]))
    def __mul__(self, o):
        """self * o : apply o first, then self"""
        return Rot(*(self.apply(col) for col in o.c))
    @property
    def right(self): return self.c[0]
    @property
    def up(self): return self.c[1]
    @property
    def back(self): return self.c[2]
    @property
    def forward(self): return _mul(self.c[2], -1)
    def quat(self):
        """(x, y, z, w), w >= 0 when possible"""
        m = [[self.c[j][i] for j in range(3)] for i in range(3)]
        tr = m[0][0] + m[1][1] + m[2][2]
        if tr > 0:
            s = math.sqrt(tr + 1) * 2; w = 0.25 * s; x = (m[2][1]-m[1][2])/s; y = (m[0][2]-m[2][0])/s; z = (m[1][0]-m[0][1])/s
        elif m[0][0] > m[1][1] and m[0][0] > m[2][2]:
            s = math.sqrt(1 + m[0][0] - m[1][1] - m[2][2]) * 2; w = (m[2][1]-m[1][2])/s; x = 0.25*s; y = (m[0][1]+m[1][0])/s; z = (m[0][2]+m[2][0])/s
        elif m[1][1] > m[2][2]:
            s = math.sqrt(1 + m[1][1] - m[0][0] - m[2][2]) * 2; w = (m[0][2]-m[2][0])/s; x = (m[0][1]+m[1][0])/s; y = 0.25*s; z = (m[1][2]+m[2][1])/s
        else:
            s = math.sqrt(1 + m[2][2] - m[0][0] - m[1][1]) * 2; w = (m[1][0]-m[0][1])/s; x = (m[0][2]+m[2][0])/s; y = (m[1][2]+m[2][1])/s; z = 0.25*s
        if w < 0: x, y, z, w = -x, -y, -z, -w
        return (x, y, z, w)
    def angle_to(self, o):
        """rotation angle (rad) between two orientations"""
        t = (self.inv() * o)
        tr = t.c[0][0] + t.c[1][1] + t.c[2][2]
        return math.acos(max(-1.0, min(1.0, (tr - 1) / 2)))
    def axis_names(self):
        """(fwd, up) names if this is one of the 24 axis-aligned orientations, else None"""
        def nm(v):
            for k, d in DIRS.items():
                if all(abs(a - b) < 1e-6 for a, b in zip(v, d)): return k
        f, u = nm(self.forward), nm(self.up)
        return (f, u) if f and u else None
    def __repr__(self): return f'Rot(fwd={tuple(round(v, 4) for v in self.forward)}, up={tuple(round(v, 4) for v in self.up)})'

ALL_24 = [Rot.from_axes(f, u) for f in DIRS for u in DIRS if _dot(DIRS[f], DIRS[u]) == 0]

class Pose:
    """position (metres) + rotation of a frame in its parent's frame; for a CubeGrid in a blueprint the parent is the world"""
    __slots__ = ('pos', 'rot')
    def __init__(self, pos=(0.0, 0.0, 0.0), rot=None):
        self.pos = tuple(float(v) for v in pos); self.rot = rot or Rot.identity()
    @classmethod
    def axes(cls, pos=(0, 0, 0), fwd='Forward', up='Up'): return cls(pos, Rot.from_axes(fwd, up))
    def __mul__(self, o):
        """self * o : pose of o's frame in self's parent (o is expressed in self's frame)"""
        return Pose(_add(self.pos, self.rot.apply(o.pos)), self.rot * o.rot)
    def inv(self):
        ri = self.rot.inv(); return Pose(_mul(ri.apply(self.pos), -1), ri)
    def apply(self, p): return _add(self.pos, self.rot.apply(p))
    def distance_to(self, o): return _norm(_sub(self.pos, o.pos))
    def __repr__(self): return f'Pose(pos={tuple(round(v, 4) for v in self.pos)}, {self.rot})'

    def element(self, tag='PositionAndOrientation'):
        """<PositionAndOrientation> exactly as the game writes it (Position, Forward, Up, Orientation quaternion)"""
        po = ET.Element(tag)
        po.append(E('Position', x=_num(self.pos[0]), y=_num(self.pos[1]), z=_num(self.pos[2])))
        f, u = self.rot.forward, self.rot.up
        po.append(E('Forward', x=_num(f[0]), y=_num(f[1]), z=_num(f[2])))
        po.append(E('Up', x=_num(u[0]), y=_num(u[1]), z=_num(u[2])))
        q = ET.SubElement(po, 'Orientation')
        for t, v in zip('XYZW', self.rot.quat()): q.append(E(t, _num(v)))
        return po
    @classmethod
    def from_element(cls, po):
        """read a <PositionAndOrientation> / <MasterToSlaveTransform> element"""
        p = po.find('Position')
        pos = (float(p.get('x')), float(p.get('y')), float(p.get('z')))
        f, u = po.find('Forward'), po.find('Up')
        rot = Rot.from_forward_up((float(f.get('x')), float(f.get('y')), float(f.get('z'))), (float(u.get('x')), float(u.get('y')), float(u.get('z'))))
        return cls(pos, rot)

# ---------------------------------------------------------------------------------------------------------------
# block frames
# ---------------------------------------------------------------------------------------------------------------
def block_pose(bp, b):
    """pose of block b's frame in its grid frame (origin = centre of the block's cell box)"""
    cs = CELL[bp.grid]
    e = bp.ext(b)
    centre = tuple((b['min'][i] + (e[i] - 1) / 2) * cs for i in range(3))
    right, up, back = bp.axes(b)
    return Pose(centre, Rot(right, up, back))

def face_depth(bp, b, face):
    """distance (m) from the block centre to its face `face` (a LOCAL direction name, e.g. 'Forward' for a connector)"""
    cs = CELL[bp.grid]
    axis = {'Right': 0, 'Left': 0, 'Up': 1, 'Down': 1, 'Backward': 2, 'Forward': 2}[face]
    return b['size'][axis] * cs / 2

def contact_point(bp, b, face='Forward', gap=0.0):
    """(point in grid frame, outward normal in grid frame) of the centre of the block's `face`"""
    bpose = block_pose(bp, b)
    n = bpose.rot.apply(DIRS[face])
    return _add(bpose.pos, _mul(n, face_depth(bp, b, face) + gap)), n

def dock_candidates(parent_bp, parent_block, child_bp, child_block, parent_face='Forward', child_face='Forward', gap=0.0):
    """all four axis-aligned poses of the child grid in the parent grid frame that put the two block faces against each other
    (face normals opposite, face centres `gap` metres apart).  Sorted so that the child's up stays closest to the parent's up first."""
    cp, n_p = contact_point(parent_bp, parent_block, parent_face, 0.0)
    cpos_c = block_pose(child_bp, child_block).pos
    n_c_local = block_pose(child_bp, child_block).rot.apply(DIRS[child_face])        # child grid frame
    contact_c = _add(cpos_c, _mul(n_c_local, face_depth(child_bp, child_block, child_face)))
    out = []
    for R in ALL_24:
        if _norm(_add(R.apply(n_c_local), n_p)) > 1e-9: continue                      # R n_c must be -n_p
        P = _sub(_add(cp, _mul(n_p, gap)), R.apply(contact_c))
        out.append(Pose(P, R))
    out.sort(key=lambda p: (-_dot(p.rot.up, (0, 1, 0)), -_dot(p.rot.forward, (0, 0, -1))))
    return out

def dock_pose(parent_bp, parent_block, child_bp, child_block, parent_face='Forward', child_face='Forward', gap=0.0, roll=0):
    """pose of the child grid in the parent grid frame with the two faces (default: connector Forward faces) against each other.
    `roll` picks one of the four orientations about the contact normal (0 keeps the child's up closest to the parent's up)."""
    c = dock_candidates(parent_bp, parent_block, child_bp, child_block, parent_face, child_face, gap)
    if not c: raise ValueError('no axis-aligned docking orientation found')
    return c[roll % len(c)]

def relative_block_transform(master_world, master_block_pose, slave_world, slave_block_pose):
    """MasterToSlaveTransform of a lock: the slave block frame expressed in the master block frame, given both grids' WORLD poses"""
    mb = master_world * master_block_pose
    sb = slave_world * slave_block_pose
    return mb.inv() * sb

def master_to_slave_grid(master_world, slave_world, slave_block_pose):
    """(PositionOffset, OrientationOffset quaternion) of MasterToSlaveGrid (see module doc)"""
    sb = slave_world * slave_block_pose
    d = _sub(master_world.pos, sb.pos)
    pos = sb.rot.inv().apply(d)
    q = (sb.rot.inv() * master_world.rot).quat()
    return pos, q

def lock_elements(master_world, master_block_pose, slave_world, slave_block_pose, master_bp, slave_bp):
    """the three field elements shared by a locked pair: (MasterToSlaveTransform element, MasterToSlaveGrid element for the master)"""
    t = relative_block_transform(master_world, master_block_pose, slave_world, slave_block_pose).element('MasterToSlaveTransform')
    pos, q = master_to_slave_grid(master_world, slave_world, slave_block_pose)
    g = ET.Element('MasterToSlaveGrid')
    for tag in ('OrientationOffset', 'PositionOffset', 'OrientationAsVector'):
        e = ET.SubElement(g, tag)
        vals = pos if tag == 'PositionOffset' else q
        for k, v in zip('XYZW', vals): e.append(E(k, _num(v)))
    return t, g
