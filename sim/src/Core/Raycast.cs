// SLOT A. Block-level ray queries shared by cameras (slot B), projectiles and turrets (slot C).
// Algorithm: ray-sphere broadphase per body, transform the ray into grid-local cell space, slab-clip against the cell array
// box, then a 3D DDA (Amanatides-Woo) over Body.Cells. Blocks fill their whole cell box (conservative; "ray vs block boxes").
// No allocation: everything is struct/local; the buffers are supplied by the caller.
using System;
using VRageMath;

namespace SeSim.Core
{
    public struct RayHit
    {
        public Body Body;
        public BlockInst Block;
        public double Distance;       // from ray origin, metres
        public Vector3D Point;        // world hit point on the block's cell face
        public Vector3D NormalWorld;  // face normal
    }

    public static class Raycast
    {
        /// <summary>First block hit on one body within maxDist. dir must be unit length.</summary>
        public static bool RayBody(Body b, Vector3D from, Vector3D dir, double maxDist, out RayHit hit)
        {
            int count = 0;
            return Trace(b, from, dir, maxDist, false, null, ref count, out hit);
        }

        /// <summary>Nearest hit over all live bodies except 'ignore' (may be null).</summary>
        public static bool RayWorld(World w, Vector3D from, Vector3D dir, double maxDist, Body ignore, out RayHit hit)
        {
            hit = default(RayHit);
            bool any = false;
            double best = maxDist;
            var bodies = w.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (ReferenceEquals(b, ignore) || !b.Alive) continue;
                RayHit h; int cnt = 0;
                if (Trace(b, from, dir, best, false, null, ref cnt, out h) && h.Distance <= best)
                {
                    best = h.Distance; hit = h; any = true;
                }
            }
            return any;
        }

        /// <summary>Nearest hit over all live bodies including the caller's own, skipping only the block 'skip' (a camera: the game
        /// ignores just the camera block itself, so the first own block in front of the lens is returned).</summary>
        public static bool RayWorldSkipBlock(World w, Vector3D from, Vector3D dir, double maxDist, BlockInst skip, out RayHit hit)
        {
            hit = default(RayHit);
            bool any = false;
            double best = maxDist;
            var bodies = w.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (!b.Alive) continue;
                RayHit h; int cnt = 0;
                if (Trace(b, from, dir, best, false, null, ref cnt, out h, skip) && h.Distance <= best)
                {
                    best = h.Distance; hit = h; any = true;
                }
            }
            return any;
        }

        /// <summary>All distinct blocks along the ray on all bodies, sorted by distance (railgun penetration). Returns count written.</summary>
        public static int RayWorldAll(World w, Vector3D from, Vector3D dir, double maxDist, Body ignore, RayHit[] buffer)
        {
            int count = 0;
            var bodies = w.Bodies;
            for (int i = 0; i < bodies.Count && count < buffer.Length; i++)
            {
                var b = bodies[i];
                if (ReferenceEquals(b, ignore) || !b.Alive) continue;
                RayHit h;
                Trace(b, from, dir, maxDist, true, buffer, ref count, out h);
            }
            // insertion sort by distance (count is small)
            for (int i = 1; i < count; i++)
            {
                RayHit x = buffer[i]; int j = i - 1;
                while (j >= 0 && buffer[j].Distance > x.Distance) { buffer[j + 1] = buffer[j]; j--; }
                buffer[j + 1] = x;
            }
            return count;
        }

        /// <summary>Trace through one body. all=false: stop at the first block (result in 'first'); all=true: append every distinct
        /// block to buf starting at count (until buf is full).</summary>
        static bool Trace(Body b, Vector3D from, Vector3D dir, double maxDist, bool all, RayHit[] buf, ref int count, out RayHit first, BlockInst skip = null)
        {
            first = default(RayHit);
            var cells = b.Cells;
            if (!b.Alive || b.Blocks.Count == 0 || cells == null || cells.Length == 0) return false;

            // ---- broadphase: ray vs bounding sphere ----
            double ocx = b.WorldBoundCenter.X - from.X, ocy = b.WorldBoundCenter.Y - from.Y, ocz = b.WorldBoundCenter.Z - from.Z;
            double tca = ocx * dir.X + ocy * dir.Y + ocz * dir.Z;
            double r = b.BoundRadius;
            double d2 = ocx * ocx + ocy * ocy + ocz * ocz - tca * tca;
            if (d2 > r * r) return false;
            double thc = Math.Sqrt(r * r - d2);
            if (tca + thc < 0 || tca - thc > maxDist) return false;

            // ---- ray into cell space: cell i spans [i, i+1) after the +0.5 shift; param s in cell units ----
            double k = 1.0 / b.CellSize;
            double rx = from.X - b.WorldMatrix.M41, ry = from.Y - b.WorldMatrix.M42, rz = from.Z - b.WorldMatrix.M43;
            var ir = b.InvWorldRot;
            double lx = rx * ir.M11 + ry * ir.M21 + rz * ir.M31, ly = rx * ir.M12 + ry * ir.M22 + rz * ir.M32, lz = rx * ir.M13 + ry * ir.M23 + rz * ir.M33;
            double dx = dir.X * ir.M11 + dir.Y * ir.M21 + dir.Z * ir.M31, dy = dir.X * ir.M12 + dir.Y * ir.M22 + dir.Z * ir.M32, dz = dir.X * ir.M13 + dir.Y * ir.M23 + dir.Z * ir.M33;
            double ox = lx * k + 0.5 - b.CellsOrigin.X, oy = ly * k + 0.5 - b.CellsOrigin.Y, oz = lz * k + 0.5 - b.CellsOrigin.Z;
            int nx = b.DimX, ny = b.DimY, nz = b.DimZ;

            // slab clip against [0,n] on each axis
            double sEnter = 0, sExit = maxDist * k;
            int enterAxis = -1; double enterSign = 0;
            const double Eps = 1e-14;
            // X
            if (Math.Abs(dx) < Eps) { if (ox < 0 || ox > nx) return false; }
            else
            {
                double t1 = (0 - ox) / dx, t2 = (nx - ox) / dx; double sg = dx > 0 ? -1 : 1;
                if (t1 > t2) { double tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > sEnter) { sEnter = t1; enterAxis = 0; enterSign = sg; }
                if (t2 < sExit) sExit = t2;
            }
            if (Math.Abs(dy) < Eps) { if (oy < 0 || oy > ny) return false; }
            else
            {
                double t1 = (0 - oy) / dy, t2 = (ny - oy) / dy; double sg = dy > 0 ? -1 : 1;
                if (t1 > t2) { double tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > sEnter) { sEnter = t1; enterAxis = 1; enterSign = sg; }
                if (t2 < sExit) sExit = t2;
            }
            if (Math.Abs(dz) < Eps) { if (oz < 0 || oz > nz) return false; }
            else
            {
                double t1 = (0 - oz) / dz, t2 = (nz - oz) / dz; double sg = dz > 0 ? -1 : 1;
                if (t1 > t2) { double tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > sEnter) { sEnter = t1; enterAxis = 2; enterSign = sg; }
                if (t2 < sExit) sExit = t2;
            }
            if (sEnter > sExit) return false;

            // ---- DDA setup ----
            double px = ox + dx * sEnter, py = oy + dy * sEnter, pz = oz + dz * sEnter;
            int ix = (int)Math.Floor(px), iy = (int)Math.Floor(py), iz = (int)Math.Floor(pz);
            if (ix < 0) ix = 0; else if (ix >= nx) ix = nx - 1;
            if (iy < 0) iy = 0; else if (iy >= ny) iy = ny - 1;
            if (iz < 0) iz = 0; else if (iz >= nz) iz = nz - 1;
            int stx = dx > 0 ? 1 : -1, sty = dy > 0 ? 1 : -1, stz = dz > 0 ? 1 : -1;
            double inf = double.PositiveInfinity;
            double tdx = Math.Abs(dx) < Eps ? inf : Math.Abs(1.0 / dx);
            double tdy = Math.Abs(dy) < Eps ? inf : Math.Abs(1.0 / dy);
            double tdz = Math.Abs(dz) < Eps ? inf : Math.Abs(1.0 / dz);
            double tmx = Math.Abs(dx) < Eps ? inf : ((dx > 0 ? ix + 1 : ix) - ox) / dx;
            double tmy = Math.Abs(dy) < Eps ? inf : ((dy > 0 ? iy + 1 : iy) - oy) / dy;
            double tmz = Math.Abs(dz) < Eps ? inf : ((dz > 0 ? iz + 1 : iz) - oz) / dz;
            double sCur = sEnter;
            int axis = enterAxis; double sign = enterSign;
            int lastBlock = -1;
            var blocks = b.Blocks;
            for (;;)
            {
                int bi = cells[ix + nx * (iy + ny * iz)];
                if (bi >= 0 && bi != lastBlock && (skip == null || !ReferenceEquals(blocks[bi], skip)))
                {
                    var h = new RayHit();
                    h.Body = b; h.Block = blocks[bi]; h.Distance = sCur * b.CellSize;
                    h.Point = new Vector3D(from.X + dir.X * h.Distance, from.Y + dir.Y * h.Distance, from.Z + dir.Z * h.Distance);
                    Vector3D nl;
                    if (axis == 0) nl = new Vector3D(sign, 0, 0);
                    else if (axis == 1) nl = new Vector3D(0, sign, 0);
                    else if (axis == 2) nl = new Vector3D(0, 0, sign);
                    else nl = new Vector3D(-dx, -dy, -dz);         // ray starts inside the block
                    h.NormalWorld = Vector3D.TransformNormal(nl, b.RotMatrix);
                    if (!all) { first = h; return true; }
                    if (count >= buf.Length) return count > 0;
                    buf[count++] = h;
                    if (count >= buf.Length) return true;
                    lastBlock = bi;
                }
                else if (bi < 0) lastBlock = -1;   // (a skipped block's cell is passed through)
                // advance to the next cell
                if (tmx < tmy && tmx < tmz)
                {
                    if (tmx > sExit) break;
                    ix += stx; if ((uint)ix >= (uint)nx) break;
                    sCur = tmx; tmx += tdx; axis = 0; sign = -stx;
                }
                else if (tmy < tmz)
                {
                    if (tmy > sExit) break;
                    iy += sty; if ((uint)iy >= (uint)ny) break;
                    sCur = tmy; tmy += tdy; axis = 1; sign = -sty;
                }
                else
                {
                    if (tmz > sExit) break;
                    iz += stz; if ((uint)iz >= (uint)nz) break;
                    sCur = tmz; tmz += tdz; axis = 2; sign = -stz;
                }
            }
            return all && count > 0;
        }
    }
}
