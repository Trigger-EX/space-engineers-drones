// SLOT A. Block adjacency and grid splitting.
// Edge = two blocks with face-adjacent cells (blueprints are mount-validated, so face contact approximates mount contact),
// except merge-block pairs which form an edge only while MergeUnit.IsConnected (MergeUnit.Adjacent marks the geometric pair).
using System;
using System.Collections.Generic;
using VRageMath;

namespace SeSim.Core
{
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

        /// <summary>If ConnectivityDirty: keep the component with the most mass in b, move each other component into a new Body
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

            // main = heaviest component (ties: the one with the lowest first block index)
            int main = 0; double bestMass = -1;
            for (int c = 0; c < nc; c++)
            {
                double m = 0; var l = comps[c];
                for (int i = 0; i < l.Count; i++) m += l[i].Mass;
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
