// SLOT A. Rigid body = one SE grid (a merged drone+printer is one Body; splitting creates new Bodies).
// State is double precision, world frame. Grid frame conventions: OrientMath.cs header.
//
// Cells: dense occupancy array over the cell box [CellsOrigin, CellsOrigin + Dim). Value = BlockInst.Index of the block
// covering the cell, -1 when empty. The array geometry is fixed at BuildCells (spawn / split); CellMin/CellMax are the tight
// bounds of the LIVE blocks (refreshed by RecomputeMassProperties) and may be smaller than the array box.
// RemoveBlock keeps Body.Blocks compact: the blocks behind the removed one get Index-1 and their cells are rewritten.
//
// Mass properties: Mass, ComLocal, InertiaLocal/InvInertiaLocal (3x3 in the upper left of a MatrixD, symmetric) are about
// the COM in GRID axes. RecomputeMassProperties keeps the grid ORIGIN (cell 0,0,0 centre) where it is in the world and moves
// Position (= world COM) with the COM; linear velocity is adjusted to the rigid-body velocity field at the new COM.
using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using SeSim.Blueprint;

namespace SeSim.Core
{
    public sealed class Body
    {
        public World World;
        public int Id;                         // index-stable handle within the World (never reused in a run)
        public long EntityId;                  // IMyCubeGrid.EntityId (remapped, unique)
        public string DisplayName;
        public MyCubeSize GridSize;
        public double CellSize;                // 0.5 or 2.5
        public int Team;                       // team index (TeamSpec); relationship source
        public long OwnerId;                   // team identity id (majority owner of the grid)
        public int ShipIndex = -1;             // ShipInstance index in the scenario (slot D); -1 = debris
        public bool Alive = true;              // false once removed from World.Bodies
        public bool IsStatic;

        // ---- blocks -----------------------------------------------------------------------------------------
        public readonly List<BlockInst> Blocks = new List<BlockInst>();   // live (not destroyed) blocks, Index-ordered
        public int Version;                    // ++ whenever the block set changes (API caches rebuild on change)
        public int DecoyVersion = -1;          // Version at which DecoyBlocks was built (SeSim.Combat.Decoys cache)
        public readonly List<BlockInst> DecoyBlocks = new List<BlockInst>();
        public int AiCombatVersion = -1;       // Version at which AiCombatBlocks was built (TurretUnit.Extended cache)
        public readonly List<BlockInst> AiCombatBlocks = new List<BlockInst>();   // AI Offensive / Defensive blocks
        public Vector3I CellMin, CellMax;      // occupancy bounds
        public int[] Cells;                    // dense occupancy: cell -> block Index, -1 empty; see CellIndex()
        public Vector3I CellsOrigin;           // min cell of the Cells array box
        public int DimX, DimY, DimZ;           // size of the Cells array box
        public readonly List<ThrusterUnit> Thrusters = new List<ThrusterUnit>();
        public readonly List<GyroUnit> Gyros = new List<GyroUnit>();
        public readonly List<BatteryUnit> Batteries = new List<BatteryUnit>();
        public readonly List<BlockInst> Generators = new List<BlockInst>();   // reactors / hydrogen engines (large enemy ships; drones have none)
        public readonly List<SeSim.Combat.WeaponUnit> Weapons = new List<SeSim.Combat.WeaponUnit>();
        public PowerSystem Power;
        public SeSim.Api.SimCubeGrid ApiGrid;  // slot B, one per body

        // ---- mass properties (grid-local, about COM) ---------------------------------------------------------
        public double Mass, InvMass;
        public Vector3D ComLocal;              // grid-local metres
        public MatrixD InertiaLocal, InvInertiaLocal;   // 3x3 in the upper-left of a MatrixD (row-vector convention)
        public Vector3D BoundCenterLocal; public double BoundRadius;   // bounding sphere of all blocks
        public Vector3D LocalAabbMin, LocalAabbMax;                    // tight box of the live blocks, grid-local metres
        public bool MassDirty = true, ConnectivityDirty, PowerDirty;

        // ---- kinematic state (world) -------------------------------------------------------------------------
        public Vector3D Position;               // world position of the COM
        public QuaternionD Rotation = QuaternionD.Identity;  // grid orientation
        public Vector3D LinearVelocity, AngularVelocity;     // m/s, rad/s (right-handed, world)
        public MatrixD RotMatrix = MatrixD.Identity;         // cached from Rotation (no translation)
        public MatrixD WorldMatrix = MatrixD.Identity;       // cached grid world matrix: Translation = world position of grid origin (cell 0,0,0 center)
        public MatrixD InvWorldRot = MatrixD.Identity;       // transpose of RotMatrix
        public Vector3D WorldBoundCenter;                    // world position of BoundCenterLocal (refreshed by UpdateWorldMatrix)

        // ---- per-tick accumulators ----------------------------------------------------------------------------
        public Vector3D Force, Torque;         // world frame, cleared after Integrate
        public bool DampenersOn = true;        // set via IMyShipController.DampenersOverride
        public List<GroupData> Groups = new List<GroupData>();   // M0: terminal block groups of the blueprint grid(s) (cells in this body's frame; GroupData.Construct = origin grid)
        public PilotInput Pilot;               // hand-flown input of the controller somebody sits in (null = nobody; SimShipController.AttachToBody)
        public bool GyroDirty = true;          // set by SimGyro setters (Keen: SetGyroTorque -> MarkDirty -> ramp frames recomputed)
        public int GyroRampFrames;             // 0 = unset; Keen m_overrideAccelerationRampFrames
        public Vector3D PrevLinearVelocity;    // for acceleration metrics
        public readonly double[] DirScratch = new double[6];   // physics scratch (sum of dampener-capable thrust per direction)

        public double MaxSpeed(SimSettings s) { return GridSize == MyCubeSize.Large ? s.LargeMaxSpeed : s.SmallMaxSpeed; }
        public double MaxAngularSpeed(SimSettings s) { return GridSize == MyCubeSize.Large ? s.LargeMaxAngularSpeed : s.SmallMaxAngularSpeed; }

        // =====================================================================================================
        // cells
        // =====================================================================================================

        /// <summary>Array offset of a cell, or -1 when outside the array box.</summary>
        public int CellIndex(Vector3I c)
        {
            int x = c.X - CellsOrigin.X, y = c.Y - CellsOrigin.Y, z = c.Z - CellsOrigin.Z;
            if ((uint)x >= (uint)DimX || (uint)y >= (uint)DimY || (uint)z >= (uint)DimZ) return -1;
            return x + DimX * (y + DimY * z);
        }

        public BlockInst BlockAtCell(Vector3I c)
        {
            int i = CellIndex(c);
            if (i < 0) return null;
            int bi = Cells[i];
            return bi < 0 ? null : Blocks[bi];
        }

        /// <summary>Allocates the Cells array over the bounds of the live blocks, renumbers Block.Index (= position in Blocks)
        /// and fills the occupancy. Call after Blocks was assembled (spawn, split).</summary>
        public void BuildCells()
        {
            if (Blocks.Count == 0) { Cells = new int[0]; DimX = DimY = DimZ = 0; CellMin = CellMax = CellsOrigin = Vector3I.Zero; return; }
            var mn = Blocks[0].Min; var mx = Blocks[0].Max;
            for (int i = 0; i < Blocks.Count; i++)
            {
                var b = Blocks[i];
                b.Index = i;
                if (b.Min.X < mn.X) mn.X = b.Min.X; if (b.Min.Y < mn.Y) mn.Y = b.Min.Y; if (b.Min.Z < mn.Z) mn.Z = b.Min.Z;
                if (b.Max.X > mx.X) mx.X = b.Max.X; if (b.Max.Y > mx.Y) mx.Y = b.Max.Y; if (b.Max.Z > mx.Z) mx.Z = b.Max.Z;
            }
            CellMin = CellsOrigin = mn; CellMax = mx;
            DimX = mx.X - mn.X + 1; DimY = mx.Y - mn.Y + 1; DimZ = mx.Z - mn.Z + 1;
            long vol = (long)DimX * DimY * DimZ;
            if (vol > 100000000L) throw new InvalidOperationException("grid cell box too large: " + DimX + "x" + DimY + "x" + DimZ);
            if (Cells == null || Cells.Length != vol) Cells = new int[vol];
            for (int i = 0; i < Cells.Length; i++) Cells[i] = -1;
            for (int i = 0; i < Blocks.Count; i++) FillCells(Blocks[i], i);
        }

        void FillCells(BlockInst b, int value)
        {
            for (int z = b.Min.Z; z <= b.Max.Z; z++)
                for (int y = b.Min.Y; y <= b.Max.Y; y++)
                {
                    int o = (b.Min.X - CellsOrigin.X) + DimX * ((y - CellsOrigin.Y) + DimY * (z - CellsOrigin.Z));
                    for (int x = b.Min.X; x <= b.Max.X; x++, o++) Cells[o] = value;
                }
        }

        /// <summary>Clears and re-fills the unit lists (Thrusters, Gyros, Batteries, Weapons) from Blocks.</summary>
        public void RebuildUnitLists()
        {
            Thrusters.Clear(); Gyros.Clear(); Batteries.Clear(); Weapons.Clear(); Generators.Clear();
            for (int i = 0; i < Blocks.Count; i++)
            {
                var b = Blocks[i];
                if (b.Thruster != null) Thrusters.Add(b.Thruster);
                if (b.Gyro != null) Gyros.Add(b.Gyro);
                if (b.Battery != null) Batteries.Add(b.Battery);
                if (b.Def.GeneratorMW > 0) Generators.Add(b);
                if (b.Weapon != null) Weapons.Add(b.Weapon);
            }
        }

        // =====================================================================================================
        // mass properties
        // =====================================================================================================

        /// <summary>Recomputes mass, COM, inertia tensor (sum of uniform boxes with parallel-axis terms), bounding sphere and
        /// tight bounds from the live blocks. The grid origin stays fixed in the world; Position follows the COM.</summary>
        public void RecomputeMassProperties()
        {
            UpdateWorldMatrix();
            Vector3D originWorld = WorldMatrix.Translation;
            Vector3D oldComWorld = Position;
            MassDirty = false;
            int n = Blocks.Count;
            if (n == 0)
            {
                Mass = 0; InvMass = 0; InertiaLocal = MatrixD.Identity; InvInertiaLocal = MatrixD.Identity;
                BoundRadius = 0; return;
            }
            double m = 0, cx = 0, cy = 0, cz = 0;
            double lminx = double.MaxValue, lminy = double.MaxValue, lminz = double.MaxValue;
            double lmaxx = double.MinValue, lmaxy = double.MinValue, lmaxz = double.MinValue;
            Vector3I imin = Blocks[0].Min, imax = Blocks[0].Max;
            for (int i = 0; i < n; i++)
            {
                var b = Blocks[i];
                double bm = b.Mass;
                m += bm; cx += bm * b.LocalCenter.X; cy += bm * b.LocalCenter.Y; cz += bm * b.LocalCenter.Z;
                double hx = b.HalfExtents.X, hy = b.HalfExtents.Y, hz = b.HalfExtents.Z;
                double ax = b.LocalCenter.X, ay = b.LocalCenter.Y, az = b.LocalCenter.Z;
                if (ax - hx < lminx) lminx = ax - hx; if (ax + hx > lmaxx) lmaxx = ax + hx;
                if (ay - hy < lminy) lminy = ay - hy; if (ay + hy > lmaxy) lmaxy = ay + hy;
                if (az - hz < lminz) lminz = az - hz; if (az + hz > lmaxz) lmaxz = az + hz;
                if (b.Min.X < imin.X) imin.X = b.Min.X; if (b.Min.Y < imin.Y) imin.Y = b.Min.Y; if (b.Min.Z < imin.Z) imin.Z = b.Min.Z;
                if (b.Max.X > imax.X) imax.X = b.Max.X; if (b.Max.Y > imax.Y) imax.Y = b.Max.Y; if (b.Max.Z > imax.Z) imax.Z = b.Max.Z;
            }
            CellMin = imin; CellMax = imax;
            LocalAabbMin = new Vector3D(lminx, lminy, lminz); LocalAabbMax = new Vector3D(lmaxx, lmaxy, lmaxz);
            Vector3D com = m > 0 ? new Vector3D(cx / m, cy / m, cz / m) : (LocalAabbMin + LocalAabbMax) * 0.5;

            double ixx = 0, iyy = 0, izz = 0, ixy = 0, ixz = 0, iyz = 0;
            for (int i = 0; i < n; i++)
            {
                var b = Blocks[i];
                double bm = b.Mass;
                double rx = b.LocalCenter.X - com.X, ry = b.LocalCenter.Y - com.Y, rz = b.LocalCenter.Z - com.Z;
                double hx2 = b.HalfExtents.X * b.HalfExtents.X, hy2 = b.HalfExtents.Y * b.HalfExtents.Y, hz2 = b.HalfExtents.Z * b.HalfExtents.Z;
                // box about its own centre: m/12 (b^2 + c^2) with full sides 2h  ->  m/3 (h_b^2 + h_c^2)
                ixx += bm * ((hy2 + hz2) / 3.0 + ry * ry + rz * rz);
                iyy += bm * ((hx2 + hz2) / 3.0 + rx * rx + rz * rz);
                izz += bm * ((hx2 + hy2) / 3.0 + rx * rx + ry * ry);
                ixy -= bm * rx * ry; ixz -= bm * rx * rz; iyz -= bm * ry * rz;
            }
            Mass = m; InvMass = m > 0 ? 1.0 / m : 0;
            InertiaLocal = new MatrixD(ixx, ixy, ixz, 0, ixy, iyy, iyz, 0, ixz, iyz, izz, 0, 0, 0, 0, 1);
            double det = ixx * (iyy * izz - iyz * iyz) - ixy * (ixy * izz - iyz * ixz) + ixz * (ixy * iyz - iyy * ixz);
            if (Math.Abs(det) < 1e-300)
            {
                InvInertiaLocal = MatrixD.Identity;
            }
            else
            {
                double id = 1.0 / det;
                double a11 = (iyy * izz - iyz * iyz) * id, a12 = (ixz * iyz - ixy * izz) * id, a13 = (ixy * iyz - ixz * iyy) * id;
                double a22 = (ixx * izz - ixz * ixz) * id, a23 = (ixy * ixz - ixx * iyz) * id, a33 = (ixx * iyy - ixy * ixy) * id;
                InvInertiaLocal = new MatrixD(a11, a12, a13, 0, a12, a22, a23, 0, a13, a23, a33, 0, 0, 0, 0, 1);
            }

            BoundCenterLocal = (LocalAabbMin + LocalAabbMax) * 0.5;
            double r = 0;
            for (int i = 0; i < n; i++)
            {
                var b = Blocks[i];
                double dx = b.LocalCenter.X - BoundCenterLocal.X, dy = b.LocalCenter.Y - BoundCenterLocal.Y, dz = b.LocalCenter.Z - BoundCenterLocal.Z;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz) + b.Radius;
                if (d > r) r = d;
            }
            BoundRadius = r;

            // move the COM: origin stays, Position follows; velocity = rigid velocity field at the new COM
            ComLocal = com;
            Position = originWorld + Vector3D.TransformNormal(ComLocal, RotMatrix);
            LinearVelocity += Vector3D.Cross(AngularVelocity, Position - oldComWorld);
            UpdateWorldMatrix();
        }

        // =====================================================================================================
        // frames
        // =====================================================================================================

        /// <summary>Refreshes RotMatrix, InvWorldRot and WorldMatrix from Position/Rotation/ComLocal. Call after any state change.</summary>
        public void UpdateWorldMatrix()
        {
            RotMatrix = MatrixD.CreateFromQuaternion(Rotation);
            InvWorldRot = MatrixD.Transpose(RotMatrix);
            Vector3D origin = Position - Vector3D.TransformNormal(ComLocal, RotMatrix);
            WorldMatrix = RotMatrix;
            WorldMatrix.M41 = origin.X; WorldMatrix.M42 = origin.Y; WorldMatrix.M43 = origin.Z;
            WorldBoundCenter = origin + Vector3D.TransformNormal(BoundCenterLocal, RotMatrix);
        }

        /// <summary>Grid-local metres -> world.</summary>
        public Vector3D LocalToWorld(Vector3D local)
        {
            return new Vector3D(
                local.X * WorldMatrix.M11 + local.Y * WorldMatrix.M21 + local.Z * WorldMatrix.M31 + WorldMatrix.M41,
                local.X * WorldMatrix.M12 + local.Y * WorldMatrix.M22 + local.Z * WorldMatrix.M32 + WorldMatrix.M42,
                local.X * WorldMatrix.M13 + local.Y * WorldMatrix.M23 + local.Z * WorldMatrix.M33 + WorldMatrix.M43);
        }

        /// <summary>World -> grid-local metres.</summary>
        public Vector3D WorldToLocal(Vector3D world)
        {
            double x = world.X - WorldMatrix.M41, y = world.Y - WorldMatrix.M42, z = world.Z - WorldMatrix.M43;
            return new Vector3D(
                x * InvWorldRot.M11 + y * InvWorldRot.M21 + z * InvWorldRot.M31,
                x * InvWorldRot.M12 + y * InvWorldRot.M22 + z * InvWorldRot.M32,
                x * InvWorldRot.M13 + y * InvWorldRot.M23 + z * InvWorldRot.M33);
        }

        /// <summary>Grid-local direction -> world.</summary>
        public Vector3D DirToWorld(Vector3D local) { return Vector3D.TransformNormal(local, RotMatrix); }
        /// <summary>World direction -> grid-local.</summary>
        public Vector3D DirToLocal(Vector3D world) { return Vector3D.TransformNormal(world, InvWorldRot); }

        public Vector3D VelocityAtWorld(Vector3D world) { return LinearVelocity + Vector3D.Cross(AngularVelocity, world - Position); }

        public void AddForceAtWorld(Vector3D forceWorld, Vector3D pointWorld)
        {
            Force += forceWorld;
            Torque += Vector3D.Cross(pointWorld - Position, forceWorld);
        }
        public void AddTorqueWorld(Vector3D torqueWorld) { Torque += torqueWorld; }

        /// <summary>I_world^-1 * torque (both in world axes).</summary>
        public Vector3D ApplyInvInertiaWorld(Vector3D torqueWorld)
        {
            Vector3D tl = Vector3D.TransformNormal(torqueWorld, InvWorldRot);
            Vector3D al = Vector3D.TransformNormal(tl, InvInertiaLocal);
            return Vector3D.TransformNormal(al, RotMatrix);
        }

        /// <summary>Total angular momentum about the COM, world axes (tests, metrics).</summary>
        public Vector3D AngularMomentumWorld()
        {
            Vector3D wl = Vector3D.TransformNormal(AngularVelocity, InvWorldRot);
            return Vector3D.TransformNormal(Vector3D.TransformNormal(wl, InertiaLocal), RotMatrix);
        }

        // =====================================================================================================
        // block removal
        // =====================================================================================================

        /// <summary>Removes a block (destroyed or detached): updates lists, Cells, Version, MassDirty, ConnectivityDirty.
        /// Emits SimEvent.BlockDestroyed. Callers: Combat.Damage (slot C), merge/split code.</summary>
        public void RemoveBlock(BlockInst b, int attackerShip)
        {
            if (b.Destroyed || b.Body != this) return;
            b.Destroyed = true; b.Hp = 0;
            int idx = b.Index;
            if (idx < 0 || idx >= Blocks.Count || !ReferenceEquals(Blocks[idx], b)) { idx = Blocks.IndexOf(b); if (idx < 0) return; }
            // clear the cells first, then close the gap and rewrite the cells of the blocks behind it
            FillCells(b, -1);
            Blocks.RemoveAt(idx);
            for (int i = idx; i < Blocks.Count; i++) { var bb = Blocks[i]; bb.Index = i; FillCells(bb, i); }
            if (b.Thruster != null) Thrusters.Remove(b.Thruster);
            if (b.Gyro != null) Gyros.Remove(b.Gyro);
            if (b.Battery != null) Batteries.Remove(b.Battery);
            if (b.Def.GeneratorMW > 0) Generators.Remove(b);
            if (b.Weapon != null) Weapons.Remove(b.Weapon);
            if (b.Merge != null)
            {
                var p = b.Merge.Partner;
                if (p != null) p.Partner = null;
                b.Merge.Partner = null;
                if (b.Merge.Adjacent != null && b.Merge.Adjacent.Adjacent == b.Merge) b.Merge.Adjacent.Adjacent = null;
                b.Merge.Adjacent = null;
            }
            Version++;
            MassDirty = true; ConnectivityDirty = true; PowerDirty = true;
            if (b.Api != null) b.Api.OnDestroyed();
            if (World != null)
            {
                var e = new SimEvent { Kind = EventKind.BlockDestroyed, Tick = World.Tick, ShipA = attackerShip, ShipB = ShipIndex, Value = b.Mass, Position = LocalToWorld(b.LocalCenter) };
                World.Events.On(ref e);
            }
        }

        /// <summary>World-aligned bounding box of the live blocks (oriented tight box, then axis aligned).</summary>
        public BoundingBoxD WorldAabb()
        {
            if (Blocks.Count == 0) return new BoundingBoxD(Position, Position);
            Vector3D c = (LocalAabbMin + LocalAabbMax) * 0.5, h = (LocalAabbMax - LocalAabbMin) * 0.5;
            Vector3D cw = LocalToWorld(c);
            double ex = Math.Abs(RotMatrix.M11) * h.X + Math.Abs(RotMatrix.M21) * h.Y + Math.Abs(RotMatrix.M31) * h.Z;
            double ey = Math.Abs(RotMatrix.M12) * h.X + Math.Abs(RotMatrix.M22) * h.Y + Math.Abs(RotMatrix.M32) * h.Z;
            double ez = Math.Abs(RotMatrix.M13) * h.X + Math.Abs(RotMatrix.M23) * h.Y + Math.Abs(RotMatrix.M33) * h.Z;
            return new BoundingBoxD(new Vector3D(cw.X - ex, cw.Y - ey, cw.Z - ez), new Vector3D(cw.X + ex, cw.Y + ey, cw.Z + ez));
        }

        public override string ToString() { return "Body#" + Id + " " + DisplayName + " (" + Blocks.Count + " blocks)"; }
    }
}
