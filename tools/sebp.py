"""Space Engineers blueprint builder with real-definition validation.

Every block is checked against the game's own CubeBlocks definitions:
  * the TypeId/SubtypeId must exist (and grid size must match)
  * no two blocks may overlap
  * every block must be structurally attached: some mount-point rectangle of it must overlap a
    mount-point rectangle of a neighbour on the touching face (exclusion masks respected), and
    the whole grid must be one connected piece
Mount-point face coordinates (block-local box, z=0 is the FRONT face, block Forward = -Z):
  Front (z=0):  X = sx-x mirrored, Y = y      Back (z=Z):  X = x, Y = y
  Left  (x=0):  Z = x, Y = y                  Right (x=X): Z = Z-x, Y = y
  Top   (y=Y):  X = x, Z = Z-y                Bottom(y=0): X = x, Z = y
(derived from thruster/gatling/welder definitions, whose side mounts all land on the same end.)
"""
import json, os, random
from xml.sax.saxutils import escape

DEFS = json.load(open(os.path.join(os.path.dirname(__file__), '..', 'defs.json')))
DIR = {"Forward": (0,0,-1), "Backward": (0,0,1), "Up": (0,1,0), "Down": (0,-1,0), "Left": (-1,0,0), "Right": (1,0,0)}
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def neg(a): return tuple(-v for v in a)

def defkey(xtype, sub):
    t = xtype.replace('MyObjectBuilder_', '')
    for k in (f'{t}/{sub}', f'MyObjectBuilder_{t}/{sub}'):
        if k in DEFS: return k
    return None

class Blueprint:
    def __init__(self, name, grid='Small', seed=1):
        self.name, self.grid, self.blocks = name, grid, []
        self.rng = random.Random(seed)
        self.reserve = {}       # cell -> reason (must stay empty: exhaust, camera view, muzzle)
        self.groups = {}

    def add(self, xtype, sub, mn, fwd='Forward', up='Up', name=None, color=(0, -0.8, -0.3), extra='', kind='armor', mass=0, custom_data=None):
        k = defkey(xtype, sub)
        assert k, f'{self.name}: unknown block {xtype}/{sub}'
        d = DEFS[k]
        assert d['grid'] == self.grid, f'{self.name}: {sub} is a {d["grid"]} grid block'
        assert sum(a*b for a, b in zip(DIR[fwd], DIR[up])) == 0, f'{name}: forward/up not perpendicular'
        b = dict(xtype=xtype, sub=sub, min=tuple(mn), fwd=fwd, up=up, name=name, color=color, extra=extra,
                 kind=kind, size=tuple(d['size']), mounts=d['mounts'], mass=mass, custom_data=custom_data, key=k)
        self.blocks.append(b)
        return b

    def armor(self, x, y, z, color=None, sub=None):
        sub = sub or ('SmallBlockArmorBlock' if self.grid == 'Small' else 'LargeBlockArmorBlock')
        return self.add('MyObjectBuilder_CubeBlock', sub, (x, y, z), color=color or (0, -0.8, -0.3),
                        mass=25 if self.grid == 'Small' else 500)

    # ---------- geometry ----------
    @staticmethod
    def axes(b):
        f = DIR[b['fwd']]; u = DIR[b['up']]; r = cross(f, u)
        return [r, u, neg(f)]          # local X, Y, Z (Z = backward) in grid directions

    def ext(self, b):
        ax = self.axes(b); e = [0, 0, 0]
        for i, s in enumerate(b['size']):
            for k in range(3): e[k] += abs(ax[i][k]) * s
        return e

    def cells(self, b):
        e = self.ext(b); x, y, z = b['min']
        return [(x+i, y+j, z+k) for i in range(e[0]) for j in range(e[1]) for k in range(e[2])]

    def to_grid(self, b, p):
        """block-local corner-space point -> grid corner space (cell i spans [i, i+1))."""
        ax = self.axes(b); e = self.ext(b); g = list(b['min'])
        for i in range(3):
            a = ax[i]; k = [abs(v) for v in a].index(1); sgn = a[k]
            g[k] += p[i] if sgn > 0 else (b['size'][i] - p[i])
        return tuple(g)

    def mount_rects(self, b):
        """world mount rectangles: (axis, plane_coord, normal_sign, (lo,hi) on other two axes, excl, prop)."""
        X, Y, Z = b['size']
        mps = b['mounts']
        if mps is None:   # no MountPoints element = full mount on every face
            mps = [dict(side=s, sx=0, sy=0, ex=w, ey=h, excl=None, prop=None) for s, w, h in
                   (('Front', X, Y), ('Back', X, Y), ('Left', Z, Y), ('Right', Z, Y), ('Top', X, Z), ('Bottom', X, Z))]
        out = []
        for m in mps:
            pts = []
            for (u, v) in ((m['sx'], m['sy']), (m['ex'], m['ey'])):
                s = m['side']
                if s == 'Front':   p = (X - u, v, 0)
                elif s == 'Back':  p = (u, v, Z)
                elif s == 'Left':  p = (0, v, u)
                elif s == 'Right': p = (X, v, Z - u)
                elif s == 'Top':   p = (u, Y, Z - v)
                elif s == 'Bottom':p = (u, 0, v)
                else: raise ValueError(s)
                pts.append(self.to_grid(b, p))
            nl = {'Front': (0,0,-1), 'Back': (0,0,1), 'Left': (-1,0,0), 'Right': (1,0,0), 'Top': (0,1,0), 'Bottom': (0,-1,0)}[m['side']]
            ax = self.axes(b); n = [0,0,0]
            for i in range(3):
                for k in range(3): n[k] += nl[i] * ax[i][k]
            axis = [abs(v) for v in n].index(1)
            others = [i for i in range(3) if i != axis]
            lo = tuple(min(pts[0][i], pts[1][i]) for i in others); hi = tuple(max(pts[0][i], pts[1][i]) for i in others)
            out.append((axis, pts[0][axis], n[axis], lo, hi, int(m['excl']) if m.get('excl') else 0, int(m['prop']) if m.get('prop') else 0))
        return out

    # ---------- validation ----------
    def validate(self, require_connected=True):
        occ = {}
        for b in self.blocks:
            for c in self.cells(b):
                assert c not in occ, f'{self.name}: overlap at {c}: {b["name"] or b["sub"]} vs {occ[c]["name"] or occ[c]["sub"]}'
                occ[c] = b
        for c, why in self.reserve.items():
            assert c not in occ, f'{self.name}: {why} blocked by {occ[c]["name"] or occ[c]["sub"]} at {c}'
        rects = {id(b): self.mount_rects(b) for b in self.blocks}
        adj = {id(b): set() for b in self.blocks}
        byid = {id(b): b for b in self.blocks}
        for b in self.blocks:
            for (axis, pc, ns, lo, hi, ex, pr) in rects[id(b)]:
                # neighbour cell across this face
                probe = [0, 0, 0]
                others = [i for i in range(3) if i != axis]
                probe[axis] = pc if ns > 0 else pc - 1
                for o, l, h in zip(others, lo, hi): probe[o] = int((l + h) / 2) if h - l >= 1 else int(l)
                cand = set()
                # any neighbour overlapping the rectangle footprint
                for c in self.cells(b):
                    for d in (-1, 1):
                        q = list(c); q[axis] += d
                        q = tuple(q)
                        if q in occ and occ[q] is not b: cand.add(id(occ[q]))
                for cid in cand:
                    nb = byid[cid]
                    for (a2, pc2, ns2, lo2, hi2, ex2, pr2) in rects[cid]:
                        if a2 != axis or pc2 != pc or ns2 != -ns: continue
                        if (ex & pr2) or (ex2 & pr): continue
                        ov = 1.0
                        for i in range(2):
                            ov *= max(0.0, min(hi[i], hi2[i]) - max(lo[i], lo2[i]))
                        if ov > 1e-6:
                            adj[id(b)].add(cid); adj[cid].add(id(b))
        problems = [b for b in self.blocks if not adj[id(b)]]
        assert not problems, f'{self.name}: not attached: ' + ', '.join(f'{p["name"] or p["sub"]}@{p["min"]}' for p in problems)
        if require_connected:
            start = id(self.blocks[0]); seen = {start}; todo = [start]
            while todo:
                for n in adj[todo.pop()]:
                    if n not in seen: seen.add(n); todo.append(n)
            lost = [byid[i] for i in adj if i not in seen]
            assert not lost, f'{self.name}: separate pieces: ' + ', '.join(f'{p["name"] or p["sub"]}@{p["min"]}' for p in lost)
        return occ

    def keep_clear(self, b, n, why):
        """reserve n cells in front of block b's forward face (exhaust, muzzle, camera view)."""
        f = DIR[b['fwd']]; cs = self.cells(b)
        tip = min(cs, key=lambda c: -sum(a*d for a, d in zip(c, f)))
        for i in range(1, n + 1):
            self.reserve[tuple(tip[k] + f[k]*i for k in range(3))] = f'{why} of {b["name"] or b["sub"]}'

    # ---------- output ----------
    def xml_block(self, b):
        rid = self.rng.randint(10**17, 9*10**17)
        x, y, z = b['min']; h, s, v = b['color']
        o = f'            <MyObjectBuilder_CubeBlock xsi:type="{b["xtype"]}">\n'
        o += f'              <SubtypeName>{b["sub"]}</SubtypeName>\n' if b['sub'] else '              <SubtypeName />\n'
        if b['name']: o += f'              <EntityId>{rid}</EntityId>\n'
        o += f'              <Min x="{x}" y="{y}" z="{z}" />\n              <BlockOrientation Forward="{b["fwd"]}" Up="{b["up"]}" />\n'
        o += f'              <ColorMaskHSV x="{h}" y="{s}" z="{v}" />\n'
        if b['name']:
            o += ('              <ShareMode>Faction</ShareMode>\n'
                  f'              <CustomName>{escape(b["name"])}</CustomName>\n'
                  '              <ShowOnHUD>false</ShowOnHUD>\n              <ShowInTerminal>true</ShowInTerminal>\n'
                  '              <ShowInToolbarConfig>true</ShowInToolbarConfig>\n')
            if b['custom_data']: o += f'              <CustomData>{escape(b["custom_data"])}</CustomData>\n'
            o += '              <Enabled>true</Enabled>\n'
            if b['extra']: o += '              ' + b['extra'] + '\n'
        return o + '            </MyObjectBuilder_CubeBlock>\n'

    def write(self, outdir, display=None):
        display = display or self.name
        rid = self.rng.randint(10**17, 9*10**17)
        xml = f'''<?xml version="1.0"?>
<Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <ShipBlueprints>
    <ShipBlueprint xsi:type="MyObjectBuilder_ShipBlueprintDefinition">
      <Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="{escape(display)}" />
      <DisplayName>{escape(display)}</DisplayName>
      <CubeGrids>
        <CubeGrid>
          <SubtypeName />
          <EntityId>{rid}</EntityId>
          <PersistentFlags>CastShadows InScene</PersistentFlags>
          <PositionAndOrientation>
            <Position x="0" y="0" z="0" />
            <Forward x="0" y="0" z="-1" />
            <Up x="0" y="1" z="0" />
            <Orientation>
              <X>0</X>
              <Y>0</Y>
              <Z>0</Z>
              <W>1</W>
            </Orientation>
          </PositionAndOrientation>
          <LocalPositionAndOrientation xsi:nil="true" />
          <GridSizeEnum>{self.grid}</GridSizeEnum>
          <CubeBlocks>
{"".join(self.xml_block(b) for b in self.blocks)}          </CubeBlocks>
          <DisplayName>{escape(display)}</DisplayName>
          <DestructibleBlocks>true</DestructibleBlocks>
          <IsRespawnGrid>false</IsRespawnGrid>
          <LocalCoordSys>0</LocalCoordSys>
          <DampenersEnabled>true</DampenersEnabled>
        </CubeGrid>
      </CubeGrids>
    </ShipBlueprint>
  </ShipBlueprints>
</Definitions>
'''
        os.makedirs(outdir, exist_ok=True)
        open(os.path.join(outdir, 'bp.sbc'), 'w', encoding='utf-8').write(xml)

    def stats(self):
        occ = {}
        for b in self.blocks:
            for c in self.cells(b): occ[c] = b
        xs, ys, zs = zip(*occ)
        cell = 0.5 if self.grid == 'Small' else 2.5
        return dict(blocks=len(self.blocks), mass=sum(b['mass'] for b in self.blocks),
                    dims=tuple(round((max(a)-min(a)+1)*cell, 1) for a in (xs, ys, zs)))

    def thumb(self, path, title, subtitle, palette):
        from PIL import Image, ImageDraw
        at = {}
        for b in self.blocks:
            for c in self.cells(b): at[c] = b
        W = H = 512
        img = Image.new('RGB', (W, H)); d = ImageDraw.Draw(img)
        for i in range(H):
            t = i / H; d.line([(0, i), (W, i)], fill=(int(14+18*t), int(17+18*t), int(22+22*t)))
        xs, ys, zs = zip(*at)
        span = max(max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs)) + 1
        S = min(60, 300 / span)
        cx, cy, cz = (max(xs)+min(xs))/2, (max(ys)+min(ys))/2, (max(zs)+min(zs))/2
        def proj(x, y, z):
            x -= cx; y -= cy; z -= cz
            return (W/2 + (x+z)*0.866*S, H/2 + 30 + (x-z)*0.5*S - y*S)
        def sh(c, f): return tuple(max(0, min(255, int(v*f))) for v in c)
        for (x, y, z) in sorted(at, key=lambda c: (c[0]+c[1]-c[2], c[1])):
            b = at[(x, y, z)]; col = palette.get(b['kind'], (100, 100, 110))
            p = lambda dx, dy, dz: proj(x+dx, y+dy, z+dz)
            top = [p(-.5,.5,-.5), p(.5,.5,-.5), p(.5,.5,.5), p(-.5,.5,.5)]
            right = [p(.5,.5,-.5), p(.5,.5,.5), p(.5,-.5,.5), p(.5,-.5,-.5)]
            front = [p(-.5,.5,-.5), p(.5,.5,-.5), p(.5,-.5,-.5), p(-.5,-.5,-.5)]
            if (x, y+1, z) not in at: d.polygon(top, fill=sh(col, 1.15), outline=sh(col, .55))
            if (x+1, y, z) not in at: d.polygon(right, fill=sh(col, .8), outline=sh(col, .5))
            if (x, y, z-1) not in at: d.polygon(front, fill=sh(col, .62), outline=sh(col, .4))
        d.text((16, 16), title, fill=(235, 170, 60))
        d.text((16, 32), subtitle, fill=(180, 186, 196))
        img.save(path)
