// SLOT A. Block adjacency and grid splitting.
// Edge (SimSettings.SplitByMountPoints, default) = two blocks whose mount-point rectangles lie on the same plane with opposite
// normals and overlap (MyCubeGrid.CheckMountPointsForSide, the rule of tools/sebp.py attachment(), data from defs.json "mounts");
// a block type missing from defs.json counts as fully mounted on every face. SplitByMountPoints=false: two blocks with face-adjacent
// cells (the old approximation). Merge-block pairs form an edge only while MergeUnit.IsConnected (MergeUnit.Adjacent marks the geometric pair).
using System;
using System.Collections.Generic;
using VRageMath;

namespace SeSim.Core
{
    /// <summary>One mount rectangle of a block in grid corner space (cell i spans [i, i+1)), shrunk by 0.001 like the game.</summary>
    public struct MountRect
    {
        public int Axis, Plane, Sign;      // face plane: grid axis 0..2, integer plane coordinate, outward normal sign (+1/-1)
        public double Lo0, Lo1, Hi0, Hi1;  // rectangle on the two other axes (ascending axis order)
        public int Excl, Prop;
    }

    /// <summary>Mount-point data from defs.json (next to defs_stats.json), loaded once per process. Port of tools/sebp.py
    /// mount_rects(). Null when defs.json cannot be found (Connectivity then falls back to face contact).</summary>
    public static class MountDb
    {
        sealed class Entry { public bool Full; public string[] Side; public double[] R; public int[] Excl, Prop; }
        static readonly object Lock = new object();
        static Dictionary<string, Entry> _db;
        static bool _tried;

        static Dictionary<string, Entry> Db()
        {
            lock (Lock)
            {
                if (_tried) return _db;
                _tried = true;
                string path = Find();
                if (path == null) return null;
                try
                {
                    var root = MiniJson.Parse(System.IO.File.ReadAllText(path)) as Dictionary<string, object>;
                    if (root == null) return null;
                    var db = new Dictionary<string, Entry>();
                    foreach (var kv in root)
                    {
                        var m = MiniJson.Arr(kv.Value, "mounts");
                        var e = new Entry();
                        if (m == null) e.Full = true;
                        else
                        {
                            e.Side = new string[m.Count]; e.R = new double[m.Count * 4]; e.Excl = new int[m.Count]; e.Prop = new int[m.Count];
                            for (int i = 0; i < m.Count; i++)
                            {
                                e.Side[i] = MiniJson.Str(m[i], "side", "");
                                e.R[i * 4] = MiniJson.Num(m[i], "sx", 0); e.R[i * 4 + 1] = MiniJson.Num(m[i], "sy", 0);
                                e.R[i * 4 + 2] = MiniJson.Num(m[i], "ex", 0); e.R[i * 4 + 3] = MiniJson.Num(m[i], "ey", 0);
                                e.Excl[i] = ParseInt(MiniJson.Str(m[i], "excl", null)); e.Prop[i] = ParseInt(MiniJson.Str(m[i], "prop", null));
                            }
                        }
                        db[kv.Key] = e;
                    }
                    _db = db;
                }
                catch (Exception) { _db = null; }
                return _db;
            }
        }

        static int ParseInt(string s) { int v; return s != null && int.TryParse(s, out v) ? v : 0; }

        static string Find()
        {
            string[] starts = { System.IO.Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory };
            foreach (var st in starts)
            {
                string d = st;
                for (int i = 0; i < 7 && !string.IsNullOrEmpty(d); i++)
                {
                    string p = System.IO.Path.Combine(d, "defs.json");
                    if (System.IO.File.Exists(p) && System.IO.File.Exists(System.IO.Path.Combine(d, "defs_stats.json"))) return p;
                    d = System.IO.Path.GetDirectoryName(d);
                }
            }
            return null;
        }

        public static bool Available { get { return Db() != null; } }

        static readonly Vector3I[] SideNormal = { new Vector3I(0, 0, -1), new Vector3I(0, 0, 1), new Vector3I(-1, 0, 0), new Vector3I(1, 0, 0), new Vector3I(0, 1, 0), new Vector3I(0, -1, 0) };
        static readonly string[] SideName = { "Front", "Back", "Left", "Right", "Top", "Bottom" };

        /// <summary>Grid-space mount rectangles of a placed block (null when no data: caller treats it as face contact).</summary>
        public static MountRect[] Rects(BlockInst b)
        {
            var db = Db();
            if (db == null || b.Def == null) return null;
            Entry e;
            if (!db.TryGetValue(b.Def.Key, out e)) return null;
            double X = b.Def.SizeCells.X, Y = b.Def.SizeCells.Y, Z = b.Def.SizeCells.Z;
            int cnt = e.Full ? 6 : e.Side.Length;
            var res = new MountRect[cnt];
            // grid directions of the block axes: Right = Forward x Up, Up, Backward
            Vector3I f = Base6Directions.GetIntVector(b.Orientation.Forward), u = Base6Directions.GetIntVector(b.Orientation.Up);
            Vector3I r = new Vector3I(f.Y * u.Z - f.Z * u.Y, f.Z * u.X - f.X * u.Z, f.X * u.Y - f.Y * u.X);
            Vector3I[] ax = { r, u, new Vector3I(-f.X, -f.Y, -f.Z) };
            double[] size = { X, Y, Z };
            var pts = new double[2, 3];
            for (int m = 0; m < cnt; m++)
            {
                int side; double sx, sy, ex, ey;
                if (e.Full)
                {
                    side = m; sx = 0; sy = 0;
                    ex = (m < 2) ? X : (m < 4 ? Z : X); ey = (m < 2) ? Y : (m < 4 ? Y : Z);
                }
                else
                {
                    side = Array.IndexOf(SideName, e.Side[m]);
                    if (side < 0) side = 0;
                    sx = e.R[m * 4]; sy = e.R[m * 4 + 1]; ex = e.R[m * 4 + 2]; ey = e.R[m * 4 + 3];
                }
                for (int q = 0; q < 2; q++)
                {
                    double uu = q == 0 ? sx : ex, vv = q == 0 ? sy : ey, px, py, pz;
                    switch (side)
                    {
                        case 0: px = X - uu; py = vv; pz = 0; break;
                        case 1: px = uu; py = vv; pz = Z; break;
                        case 2: px = 0; py = vv; pz = uu; break;
                        case 3: px = X; py = vv; pz = Z - uu; break;
                        case 4: px = uu; py = Y; pz = Z - vv; break;
                        default: px = uu; py = 0; pz = vv; break;
                    }
                    double[] loc = { px, py, pz };
                    double gx = b.Min.X, gy = b.Min.Y, gz = b.Min.Z;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3I a = ax[i];
                        double d = loc[i];
                        int k = a.X != 0 ? 0 : (a.Y != 0 ? 1 : 2);
                        int sg = k == 0 ? a.X : (k == 1 ? a.Y : a.Z);
                        double add = sg > 0 ? d : size[i] - d;
                        if (k == 0) gx += add; else if (k == 1) gy += add; else gz += add;
                    }
                    pts[q, 0] = gx; pts[q, 1] = gy; pts[q, 2] = gz;
                }
                Vector3I nl = SideNormal[side];
                int nx = nl.X * r.X + nl.Y * u.X + nl.Z * (-f.X), ny = nl.X * r.Y + nl.Y * u.Y + nl.Z * (-f.Y), nz = nl.X * r.Z + nl.Y * u.Z + nl.Z * (-f.Z);
                int axis = nx != 0 ? 0 : (ny != 0 ? 1 : 2);
                int o0 = axis == 0 ? 1 : 0, o1 = axis == 2 ? 1 : 2;
                const double Shrink = 0.001;
                res[m] = new MountRect
                {
                    Axis = axis, Plane = (int)Math.Round(pts[0, axis]), Sign = axis == 0 ? nx : (axis == 1 ? ny : nz),
                    Lo0 = Math.Min(pts[0, o0], pts[1, o0]) + Shrink, Lo1 = Math.Min(pts[0, o1], pts[1, o1]) + Shrink,
                    Hi0 = Math.Max(pts[0, o0], pts[1, o0]) - Shrink, Hi1 = Math.Max(pts[0, o1], pts[1, o1]) - Shrink,
                    Excl = e.Full ? 0 : e.Excl[m], Prop = e.Full ? 0 : e.Prop[m]
                };
            }
            return res;
        }
    }

    public static class Connectivity
    {
        [ThreadStatic] static int[] _label;
        [ThreadStatic] static int[] _queue;

        /// <summary>Connected components of b's live blocks. 'result' gets one list per component (inner lists are reused and
        /// cleared when present); returns the component count. Block order inside a component follows Body.Blocks.</summary>
        public static int Components(Body b, List<List<BlockInst>> result)
        {
            var blocks = b.Blocks;
            int n = blocks.Count;
            if (b.World != null && b.World.Settings.SplitByMountPoints && MountDb.Available) return ComponentsMounts(b, result);
            if (_label == null || _label.Length < n) { _label = new int[Math.Max(n, 64) * 2]; _queue = new int[_label.Length]; }
            var label = _label; var queue = _queue;
            for (int i = 0; i < n; i++) label[i] = -1;
            int comps = 0;
            for (int s = 0; s < n; s++)
            {
                if (label[s] >= 0) continue;
                int head = 0, tail = 0;
                queue[tail++] = s; label[s] = comps;
                while (head < tail)
                {
                    int cur = queue[head++];
                    var bi = blocks[cur];
                    // walk all faces of the block box
                    for (int z = bi.Min.Z; z <= bi.Max.Z; z++)
                        for (int y = bi.Min.Y; y <= bi.Max.Y; y++)
                            for (int x = bi.Min.X; x <= bi.Max.X; x++)
                            {
                                if (x == bi.Min.X) Visit(b, bi, x - 1, y, z, label, queue, ref tail, comps);
                                if (x == bi.Max.X) Visit(b, bi, x + 1, y, z, label, queue, ref tail, comps);
                                if (y == bi.Min.Y) Visit(b, bi, x, y - 1, z, label, queue, ref tail, comps);
                                if (y == bi.Max.Y) Visit(b, bi, x, y + 1, z, label, queue, ref tail, comps);
                                if (z == bi.Min.Z) Visit(b, bi, x, y, z - 1, label, queue, ref tail, comps);
                                if (z == bi.Max.Z) Visit(b, bi, x, y, z + 1, label, queue, ref tail, comps);
                            }
                }
                comps++;
            }
            while (result.Count < comps) result.Add(new List<BlockInst>());
            while (result.Count > comps) result.RemoveAt(result.Count - 1);
            for (int c = 0; c < comps; c++) result[c].Clear();
            for (int i = 0; i < n; i++) result[label[i]].Add(blocks[i]);
            return comps;
        }

        // mount-point adjacency (MyCubeGrid.CheckMountPointsForSide): union-find over blocks sharing overlapping opposite-normal rectangles
        static int ComponentsMounts(Body b, List<List<BlockInst>> result)
        {
            var blocks = b.Blocks;
            int n = blocks.Count;
            var rects = new MountRect[n][];
            var index = new Dictionary<long, List<int>>();       // (axis, plane, sign) -> list of block index * 64 + rect index
            for (int i = 0; i < n; i++)
            {
                var bi = blocks[i];
                var rs = bi.MountRects;
                if (rs == null) { rs = MountDb.Rects(bi) ?? FaceRects(bi); bi.MountRects = rs; }
                rects[i] = rs;
                for (int k = 0; k < rs.Length; k++)
                {
                    long key = RectKey(rs[k].Axis, rs[k].Plane, rs[k].Sign);
                    List<int> l; if (!index.TryGetValue(key, out l)) index[key] = l = new List<int>();
                    l.Add(i * 64 + k);
                }
            }
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int i = 0; i < n; i++)
            {
                var bi = blocks[i];
                var rs = rects[i];
                for (int k = 0; k < rs.Length; k++)
                {
                    var r = rs[k];
                    List<int> l;
                    if (!index.TryGetValue(RectKey(r.Axis, r.Plane, -r.Sign), out l)) continue;
                    for (int q = 0; q < l.Count; q++)
                    {
                        int j = l[q] / 64;
                        if (j == i) continue;
                        var o = rects[j][l[q] % 64];
                        var bj = blocks[j];
                        if (((r.Excl & o.Prop) != 0 || (r.Prop & o.Excl) != 0) && bi.Def.Key != bj.Def.Key) continue;
                        if (!(r.Lo0 <= o.Hi0 && o.Lo0 <= r.Hi0 && r.Lo1 <= o.Hi1 && o.Lo1 <= r.Hi1)) continue;
                        if (bi.Merge != null && bj.Merge != null && bi.Merge.Adjacent == bj.Merge
                            && !(bi.Merge.Partner == bj.Merge && bi.Merge.IsConnected)) continue;
                        int a = Find(parent, i), c = Find(parent, j);
                        if (a != c) { if (a < c) parent[c] = a; else parent[a] = c; }
                    }
                }
            }
            if (_label == null || _label.Length < n) { _label = new int[Math.Max(n, 64) * 2]; _queue = new int[_label.Length]; }
            var label = _label;
            for (int i = 0; i < n; i++) label[i] = -1;
            int comps = 0;
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                if (label[root] < 0) label[root] = comps++;
                label[i] = label[root];
            }
            while (result.Count < comps) result.Add(new List<BlockInst>());
            while (result.Count > comps) result.RemoveAt(result.Count - 1);
            for (int c = 0; c < comps; c++) result[c].Clear();
            for (int i = 0; i < n; i++) result[label[i]].Add(blocks[i]);
            return comps;
        }

        static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }

        static long RectKey(int axis, int plane, int sign) { return ((long)(plane + 100000) * 8 + axis * 2 + (sign > 0 ? 1 : 0)); }

        // block type without mount data: full face rectangles over the block box (face contact)
        static MountRect[] FaceRects(BlockInst bi)
        {
            var res = new MountRect[6];
            int[] mn = { bi.Min.X, bi.Min.Y, bi.Min.Z }, mx = { bi.Max.X + 1, bi.Max.Y + 1, bi.Max.Z + 1 };
            int m = 0;
            for (int axis = 0; axis < 3; axis++)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    int o0 = axis == 0 ? 1 : 0, o1 = axis == 2 ? 1 : 2;
                    res[m++] = new MountRect { Axis = axis, Plane = sg > 0 ? mx[axis] : mn[axis], Sign = sg,
                        Lo0 = mn[o0] + 0.001, Lo1 = mn[o1] + 0.001, Hi0 = mx[o0] - 0.001, Hi1 = mx[o1] - 0.001 };
                }
            return res;
        }

        static void Visit(Body b, BlockInst from, int x, int y, int z, int[] label, int[] queue, ref int tail, int comp)
        {
            int ci = b.CellIndex(new Vector3I(x, y, z));
            if (ci < 0) return;
            int j = b.Cells[ci];
            if (j < 0 || label[j] >= 0) return;
            var to = b.Blocks[j];
            if (from.Merge != null && to.Merge != null && from.Merge.Adjacent == to.Merge)
            {
                // merge blocks glued face to face are one structure only while locked
                if (!(from.Merge.Partner == to.Merge && from.Merge.IsConnected)) return;
            }
            label[j] = comp; queue[tail++] = j;
        }

        [ThreadStatic] static List<List<BlockInst>> _comps;

        /// <summary>If ConnectivityDirty: keep the component with the most blocks in b (SplitMainByBlockCount; else the most mass), move each other component into a new Body
        /// (same velocity at its COM, same team/owner, ShipIndex kept so debris is attributed, gets a new grid EntityId and a new
        /// SimCubeGrid). Without SimSettings.GridSplitting the small parts are deleted. Returns the number of new bodies.</summary>
        public static int SplitIfNeeded(World w, Body b)
        {
            if (!b.ConnectivityDirty) return 0;
            b.ConnectivityDirty = false;
            if (b.Blocks.Count == 0) return 0;
            if (_comps == null) _comps = new List<List<BlockInst>>();
            var comps = _comps;
            int nc = Components(b, comps);
            if (nc <= 1) return 0;

            // main = the component with the most blocks like the game (MyDisconnectHelper keeps the largest group); SplitMainByBlockCount=false:
            // the heaviest (ties: the one with the lowest first block index)
            int main = 0; double bestMass = -1;
            bool byCount = w.Settings.SplitMainByBlockCount;
            for (int c = 0; c < nc; c++)
            {
                double m = 0; var l = comps[c];
                if (byCount) m = l.Count;
                else for (int i = 0; i < l.Count; i++) m += l[i].Mass;
                if (m > bestMass + 1e-9) { bestMass = m; main = c; }
            }
            Vector3D oldCom = b.Position, oldV = b.LinearVelocity, oldW = b.AngularVelocity;
            var s = w.Settings;
            int created = 0, deleted = 0;

            // detach the other components first (their blocks are still indexed in b)
            var parts = new List<List<BlockInst>>(nc - 1);
            for (int c = 0; c < nc; c++)
                if (c != main) parts.Add(new List<BlockInst>(comps[c]));

            // rebuild the main body from its own component
            var keep = new List<BlockInst>(comps[main]);
            b.Blocks.Clear();
            b.Blocks.AddRange(keep);
            b.BuildCells();
            b.RebuildUnitLists();
            b.Version++; b.MassDirty = true; b.PowerDirty = true; b.GyroDirty = true;
            b.RecomputeMassProperties();

            for (int p = 0; p < parts.Count; p++)
            {
                var part = parts[p];
                bool drop = !s.GridSplitting || (w.Bodies.Count >= s.MaxBodies && part.Count < 3);
                if (drop)
                {
                    for (int i = 0; i < part.Count; i++)
                    {
                        var blk = part[i];
                        blk.Destroyed = true; blk.Hp = 0;
                        if (blk.Merge != null) { if (blk.Merge.Partner != null) blk.Merge.Partner.Partner = null; blk.Merge.Partner = null; }
                        if (blk.Api != null) blk.Api.OnDestroyed();
                        var ev = new SimEvent { Kind = EventKind.BlockDestroyed, Tick = w.Tick, ShipA = -1, ShipB = b.ShipIndex, Value = blk.Mass, Position = b.LocalToWorld(blk.LocalCenter) };
                        w.Events.On(ref ev);
                    }
                    deleted++;
                    continue;
                }
                var nb = new Body();
                nb.DisplayName = b.DisplayName + " [part " + (p + 1) + "]";
                nb.GridSize = b.GridSize; nb.CellSize = b.CellSize;
                nb.Team = b.Team; nb.OwnerId = b.OwnerId; nb.ShipIndex = b.ShipIndex; nb.IsStatic = b.IsStatic;
                nb.DampenersOn = b.DampenersOn;
                nb.Groups = b.Groups;                                // same grid frame: the block groups' cells still resolve
                nb.Rotation = b.Rotation;
                nb.AngularVelocity = oldW;
                nb.Position = b.WorldMatrix.Translation;             // same grid origin / frame as the parent
                nb.World = w;
                nb.Blocks.AddRange(part);
                for (int i = 0; i < part.Count; i++) part[i].Body = nb;
                w.AddBody(nb);
                nb.EntityId = w.Ids.Next();
                nb.ApiGrid = new SeSim.Api.SimCubeGrid(nb);
                nb.BuildCells();
                nb.RebuildUnitLists();
                nb.UpdateWorldMatrix();
                nb.RecomputeMassProperties();
                nb.LinearVelocity = oldV + Vector3D.Cross(oldW, nb.Position - oldCom);
                nb.Power = new PowerSystem { Body = nb };
                nb.Power.Rebuild();
                nb.GyroDirty = true;
                for (int i = 0; i < part.Count; i++) if (part[i].Api != null) part[i].Api.OnBodyChanged();
                created++;
            }
            b.Power.Rebuild();
            var e = new SimEvent { Kind = EventKind.GridSplit, Tick = w.Tick, ShipA = b.ShipIndex, ShipB = b.ShipIndex, Value = nc, Position = b.Position };
            w.Events.On(ref e);
            return created;
        }
    }
}
