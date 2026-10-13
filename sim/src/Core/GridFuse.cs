// M0 (docs/design/control_arch.md s6 S9, grids_bays 7.2): the merge fuse pass. A blueprint with several CubeGrids whose enabled Merge Blocks
// touch (faces toward each other, centres one cell apart: the same test as GridFactory.PairMerges) is ONE grid in the game after the
// merge. GridFuse turns those GridData into a single GridData (blocks of the other grids re-expressed in the root grid's lattice: integer
// rotation + integer cell offset), so GridFactory builds one Body and PairMerges / Connectivity then see an ordinary locked merge pair
// that a script can switch off to split the ship again (DockLaunch does the same with a code-built dummy).
//   * Each block keeps its origin grid in BlockData.Origin -> BlockInst.ConstructId (the drone box of a cradle bay, the [SH] prefix rule).
//   * Only grids of the same size class whose relative pose is lattice-aligned (rotation = one of the 24 axis rotations, translation a
//     whole number of cells within 0.03 m) are fused; anything else stays a separate Body. Merge blocks that are disabled in the blueprint do
//     not fuse (the grids spawn as two bodies).
//   * Connector / rotor / piston / landing gear links are NOT fused (not parsed, no API): such grids spawn as separate bodies that do not
//     stay together. ScenarioGen.EnemyBlueprint therefore keeps only the grids that fuse with the main one.
// Pure function of the blueprint data; the input is never modified (blueprints are cached and shared between runs).
using System;
using System.Collections.Generic;
using VRageMath;
using SeSim.Blueprint;

namespace SeSim.Core
{
    public static class GridFuse
    {
        const double Tol = 0.03;      // metres

        sealed class Xf
        {
            public MatrixD Rel;                    // cell/direction rotation from the other grid into the root grid (integers)
            public Vector3I Off;                   // cell offset
            public Vector3I Cell(Vector3I c)
            {
                Vector3D v = Vector3D.TransformNormal(new Vector3D(c.X, c.Y, c.Z), Rel);
                return new Vector3I((int)Math.Round(v.X) + Off.X, (int)Math.Round(v.Y) + Off.Y, (int)Math.Round(v.Z) + Off.Z);
            }
            public Base6Directions.Direction Dir(Base6Directions.Direction d)
            {
                Vector3D v = Vector3D.TransformNormal(Base6Directions.GetVector(d), Rel);
                return Base6Directions.GetDirection(new Vector3I((int)Math.Round(v.X), (int)Math.Round(v.Y), (int)Math.Round(v.Z)));
            }
        }

        static MatrixD Orient(GridData g) { return MatrixD.CreateWorld(Vector3D.Zero, g.Forward, g.Up); }
        static double CellOf(GridData g, DefDb defs) { return g.Large ? defs.LargeCellSize : defs.SmallCellSize; }

        /// <summary>Transform of grid 'o' into the lattice of grid 'r', or null when the poses are not lattice-aligned.</summary>
        static Xf Align(GridData r, GridData o, DefDb defs)
        {
            if (r.Large != o.Large) return null;
            double s = CellOf(r, defs);
            MatrixD ra = Orient(r), rb = Orient(o);
            MatrixD rel = rb * MatrixD.Transpose(ra);
            for (int i = 0; i < 3; i++)
            {
                Vector3D row = i == 0 ? rel.Right : i == 1 ? rel.Up : rel.Backward;
                double a = Math.Abs(row.X) + Math.Abs(row.Y) + Math.Abs(row.Z);
                if (Math.Abs(a - 1) > 1e-3) return null;
                if (Math.Max(Math.Abs(row.X), Math.Max(Math.Abs(row.Y), Math.Abs(row.Z))) < 1 - 1e-3) return null;
            }
            for (int i = 0; i < 3; i++)                       // round the rotation to integers
            {
                Vector3D row = i == 0 ? rel.Right : i == 1 ? rel.Up : rel.Backward;
                row = new Vector3D(Math.Round(row.X), Math.Round(row.Y), Math.Round(row.Z));
                if (i == 0) rel.Right = row; else if (i == 1) rel.Up = row; else rel.Backward = row;
            }
            Vector3D t = Vector3D.TransformNormal(o.Position - r.Position, MatrixD.Transpose(ra)) / s;
            var off = new Vector3I((int)Math.Round(t.X), (int)Math.Round(t.Y), (int)Math.Round(t.Z));
            if (Math.Abs(t.X - off.X) * s > Tol || Math.Abs(t.Y - off.Y) * s > Tol || Math.Abs(t.Z - off.Z) * s > Tol) return null;
            return new Xf { Rel = rel, Off = off };
        }

        static bool IsMerge(BlockData b) { return b.TypeId == "MergeBlock" && b.Enabled; }

        /// <summary>True when merge block a (grid r) and merge block b (grid o, mapped by xf) touch face to face.</summary>
        static bool Touch(BlockData a, BlockData b, Xf xf)
        {
            Vector3I ua = Base6Directions.GetIntVector(a.Orientation.TransformDirection(Base6Directions.Direction.Up));
            var ubDir = xf.Dir(b.Orientation.TransformDirection(Base6Directions.Direction.Up));
            Vector3I ub = Base6Directions.GetIntVector(ubDir);
            if (ua.X + ub.X != 0 || ua.Y + ub.Y != 0 || ua.Z + ub.Z != 0) return false;
            Vector3I cb = xf.Cell(b.Min);
            return cb.X - a.Min.X == ua.X && cb.Y - a.Min.Y == ua.Y && cb.Z - a.Min.Z == ua.Z;
        }

        /// <summary>Fused copy of the grid list (the input list and its data stay untouched). Grids that do not fuse are returned as they are.
        /// The group root is its lowest index, so grid 0 stays the main grid. 'fusedFrom' (optional) receives, per result grid, the original indices.</summary>
        public static List<GridData> Fuse(DefDb defs, IList<GridData> grids, List<List<int>> fusedFrom = null)
        {
            int n = grids.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            bool anyLink = false;
            for (int i = 0; i < n; i++)
            {
                bool hasMerge = false;
                foreach (var b in grids[i].Blocks) if (IsMerge(b)) { hasMerge = true; break; }
                if (!hasMerge) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (find(i) == find(j)) continue;
                    var xf = Align(grids[i], grids[j], defs);
                    if (xf == null) continue;
                    bool linked = false;
                    foreach (var a in grids[i].Blocks)
                    {
                        if (!IsMerge(a)) continue;
                        foreach (var b in grids[j].Blocks) if (IsMerge(b) && Touch(a, b, xf)) { linked = true; break; }
                        if (linked) break;
                    }
                    if (linked) { parent[find(j)] = find(i); anyLink = true; }
                }
            }
            var result = new List<GridData>();
            if (!anyLink)
            {
                for (int i = 0; i < n; i++) { result.Add(grids[i]); if (fusedFrom != null) fusedFrom.Add(new List<int> { i }); }
                return result;
            }
            var done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (done[i]) continue;
                int root = find(i);
                var members = new List<int>();
                for (int k = i; k < n; k++) if (find(k) == root) { members.Add(k); done[k] = true; }
                if (members.Count == 1) { result.Add(grids[i]); if (fusedFrom != null) fusedFrom.Add(members); continue; }
                var r = grids[members[0]];
                var f = new GridData { EntityId = r.EntityId, DisplayName = r.DisplayName, Large = r.Large, IsStatic = r.IsStatic, Position = r.Position, Forward = r.Forward, Up = r.Up, DampenersEnabled = r.DampenersEnabled };
                var kept = new List<int> { members[0] };
                var apart = new List<int>();
                AddGrid(f, r, members[0], null, defs);
                for (int m = 1; m < members.Count; m++)
                {
                    var xf = Align(r, grids[members[m]], defs);
                    if (xf == null) { apart.Add(members[m]); continue; }      // cannot happen for a chain of aligned pairs; keep it separate if it does
                    AddGrid(f, grids[members[m]], members[m], xf, defs);
                    kept.Add(members[m]);
                }
                result.Add(f); if (fusedFrom != null) fusedFrom.Add(kept);
                foreach (int a in apart) { result.Add(grids[a]); if (fusedFrom != null) fusedFrom.Add(new List<int> { a }); }
            }
            return result;
        }

        static void AddGrid(GridData into, GridData from, int origin, Xf xf, DefDb defs)
        {
            // multi-cell blocks: GridFactory re-derives Max from Min + orientation, so only the min corner of the transformed box has to be right
            foreach (var b in from.Blocks)
            {
                var c = b.Clone();
                c.Origin = origin;
                if (xf != null)
                {
                    Vector3I ext = OrientMath.RotatedSize(defs.Get(b.TypeId, b.Subtype).SizeCells, b.Orientation);
                    Vector3I p1 = xf.Cell(b.Min), p2 = xf.Cell(new Vector3I(b.Min.X + ext.X - 1, b.Min.Y + ext.Y - 1, b.Min.Z + ext.Z - 1));
                    c.Min = new Vector3I(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Min(p1.Z, p2.Z));
                    c.Orientation = new MyBlockOrientation(xf.Dir(b.Orientation.Forward), xf.Dir(b.Orientation.Up));
                }
                into.Blocks.Add(c);
            }
            foreach (var g in from.Groups)
            {
                var ng = new GroupData { Name = g.Name, Construct = origin };
                foreach (var cell in g.Cells) ng.Cells.Add(xf == null ? cell : xf.Cell(cell));
                into.Groups.Add(ng);
            }
        }
    }
}
