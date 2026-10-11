// SLOT C. Scripted pirate target ship: built in code (no blueprint exists) and flown by a C# brain, not by a PB.
// Hull: large grid, 7 x 4 x 12 cells (17.5 x 10 x 30 m) hollow shell of light armor with a one-layer HEAVY armor nose (z = 0, the
// nose is toward -Z = grid Forward); on the roof 2 LargeGatlingTurret + 1 LargeMissileTurret (3x3x3 each), outside the shell 12
// LargeBlockSmallThrust (2 per direction, exhaust pointing outward), inside 4 LargeBlockGyro, 4 LargeBlockBatteryBlock (full) and a
// LargeBlockRemoteControl. No programmable block / antenna (the brain does not need them). All ids exist in defs_stats.json.
// Mass about 320 t, thrust 691 kN per direction (2.2 m/s^2). The interior is protected by the shell; the exposed turrets and thrusters
// are what attackers can kill ("combat incapable" = no weapon left or no thrust).
// Brain: cruise toward the nearest enemy at PirateCruiseSpeed, inside PirateOrbitRange+100 m circle at PirateOrbitSpeed with the target
// on the starboard side, heading follows the motion; thrusters through ThrustOverride and gyros through GyroOverride (the same units
// the scripts command, via ShipPilot), inertial dampeners off. Turrets are TurretUnit (TurretAi.cs).
using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using SeSim.Core;
using SeSim.Blueprint;

namespace SeSim.Combat
{
    /// <summary>Flight helpers shared by PirateBrain and VanillaAiBrain: drive the same ThrusterUnit/GyroUnit command fields the API
    /// objects write (OverrideN, Pitch/Yaw/Roll), so no physics shortcut is involved.</summary>
    public static class ShipPilot
    {
        /// <summary>Requests a world-frame acceleration: each of the six thruster groups gets the fraction of its capacity needed
        /// (equal fraction for all its working thrusters). Dampeners must be off (Body.DampenersOn = false), otherwise thrusters
        /// without override fight the command.</summary>
        public static void SetAcceleration(Body b, Vector3D accelWorld)
        {
            var th = b.Thrusters;
            double[] cap = b.DirScratch;
            for (int i = 0; i < 6; i++) cap[i] = 0;
            for (int i = 0; i < th.Count; i++) if (th[i].Working) cap[th[i].DirIndex] += th[i].MaxForce;
            Vector3D a = b.DirToLocal(accelWorld);
            double m = b.Mass;
            double fx = a.X * m, fy = a.Y * m, fz = a.Z * m;
            for (int i = 0; i < th.Count; i++)
            {
                var t = th[i];
                double c = cap[t.DirIndex];
                double f = 0;
                if (c > 0 && t.Working)
                {
                    double frac = Want(fx, fy, fz, t.DirIndex) / c; if (frac > 1) frac = 1;
                    f = frac * t.MaxForce;
                }
                t.OverrideN = (float)f;
            }
        }

        // force requested along grid direction group idx (0..5 = +X -X +Y -Y +Z -Z)
        static double Want(double fx, double fy, double fz, int idx)
        {
            switch (idx)
            {
                case 0: return fx > 0 ? fx : 0;
                case 1: return fx < 0 ? -fx : 0;
                case 2: return fy > 0 ? fy : 0;
                case 3: return fy < 0 ? -fy : 0;
                case 4: return fz > 0 ? fz : 0;
                default: return fz < 0 ? -fz : 0;
            }
        }

        /// <summary>Commands a world-frame angular velocity through every gyro. The scripts write Pitch/Yaw/Roll such that the
        /// engine's sign constants map them to the right-handed gyro-local rate; this inverts that map so the brain is independent of
        /// the --gyro-*-sign switches.</summary>
        public static void SetAngularVelocity(Body b, Vector3D wWorld, SimSettings s)
        {
            double max = b.MaxAngularSpeed(s);
            double len = wWorld.Length();
            if (len > max) wWorld *= max / len;
            Vector3D wg = b.DirToLocal(wWorld);
            var gy = b.Gyros;
            for (int i = 0; i < gy.Count; i++)
            {
                var g = gy[i];
                MatrixD rt = g.RotToGrid;
                // gyro-local = grid-local * R^T
                Vector3D lg = new Vector3D(wg.X * rt.M11 + wg.Y * rt.M12 + wg.Z * rt.M13, wg.X * rt.M21 + wg.Y * rt.M22 + wg.Z * rt.M23, wg.X * rt.M31 + wg.Y * rt.M32 + wg.Z * rt.M33);
                g.Pitch = (float)(lg.X * s.GyroPitchSign);
                g.Yaw = (float)(lg.Y * s.GyroYawSign);
                g.Roll = (float)(lg.Z * s.GyroRollSign);
                g.Power = 1f;
                g.Override = true;
            }
            b.GyroDirty = true;
        }

        /// <summary>Turn-rate command for a rotation error 'rot' (axis*angle): Kp*angle near the target, but never faster than a
        /// constant-deceleration stop (sqrt(2*a*angle)) so a rate-ramped gyro (MyGridGyroSystem ramp, ~2-4 rad/s^2 for large
        /// steps) does not overshoot and oscillate; capped at maxRate.</summary>
        public static Vector3D TurnRate(Vector3D rot, double kp, double accel, double maxRate)
        {
            double ang = rot.Length();
            if (ang < 1e-9) return default(Vector3D);
            double r = Math.Min(kp * ang, Math.Sqrt(2 * accel * ang));
            if (r > maxRate) r = maxRate;
            return rot * (r / ang);
        }

        /// <summary>Rotation vector (axis * angle, world frame) that takes unit vector 'from' onto unit vector 'to' by the shortest turn.</summary>
        public static Vector3D RotationBetween(Vector3D from, Vector3D to)
        {
            Vector3D c = Vector3D.Cross(from, to);
            double sn = c.Length(), cs = Vector3D.Dot(from, to);
            if (sn < 1e-9)
            {
                if (cs > 0) return default(Vector3D);
                Vector3D any = Math.Abs(from.X) < 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
                Vector3D ax = Vector3D.Cross(from, any); ax *= 1.0 / ax.Length();
                return ax * Math.PI;
            }
            return c * (Math.Atan2(sn, cs) / sn);
        }

        /// <summary>Rotation vector (world frame) from the body's current orientation to the orientation with the given forward and up.</summary>
        public static Vector3D RotationToFrame(Body b, Vector3D forward, Vector3D up)
        {
            MatrixD m = MatrixD.CreateWorld(Vector3D.Zero, forward, up);
            QuaternionD qd = QuaternionD.CreateFromRotationMatrix(m.GetOrientation());
            QuaternionD qc = b.Rotation;
            var qcInv = new QuaternionD(-qc.X, -qc.Y, -qc.Z, qc.W);
            QuaternionD e = qd * qcInv;
            e.Normalize();
            double wv = e.W, x = e.X, y = e.Y, z = e.Z;
            if (wv < 0) { wv = -wv; x = -x; y = -y; z = -z; }
            double sn = Math.Sqrt(x * x + y * y + z * z);
            if (sn < 1e-12) return default(Vector3D);
            double ang = 2 * Math.Atan2(sn, wv);
            return new Vector3D(x, y, z) * (ang / sn);
        }

        /// <summary>Largest acceleration (m/s^2) the working thrusters can produce along a world direction (min over the grid axes).</summary>
        public static double MaxAccelAlong(Body b, Vector3D dirWorld)
        {
            var th = b.Thrusters;
            double[] cap = b.DirScratch;
            for (int i = 0; i < 6; i++) cap[i] = 0;
            for (int i = 0; i < th.Count; i++) if (th[i].Working) cap[th[i].DirIndex] += th[i].MaxForce;
            Vector3D d = b.DirToLocal(dirWorld);
            double best = double.MaxValue;
            for (int ax = 0; ax < 3; ax++)
            {
                double comp = ax == 0 ? d.X : (ax == 1 ? d.Y : d.Z);
                double c = Math.Abs(comp);
                if (c < 1e-6) continue;
                double f = cap[ax * 2 + (comp > 0 ? 0 : 1)];
                double a = f / b.Mass / c;
                if (a < best) best = a;
            }
            return best == double.MaxValue ? 0 : best;
        }

        /// <summary>Clamp the length of v.</summary>
        public static Vector3D Clamp(Vector3D v, double max)
        {
            double l = v.Length();
            return l > max ? v * (max / l) : v;
        }

        /// <summary>Nearest enemy body (relation Enemies, still owned) to 'from'; null when none.</summary>
        public static Body NearestEnemy(World w, Body self, Vector3D from, double maxDist)
        {
            Body best = null; double bd = maxDist * maxDist;
            var bodies = w.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var o = bodies[i];
                if (!o.Alive || o.Blocks.Count < 2 || ReferenceEquals(o, self)) continue;
                long eo = SeSim.Api.Detection.EffectiveOwner(o);
                if (eo == 0 || w.Teams.Relation(self.OwnerId, eo) != MyRelationsBetweenPlayerAndBlock.Enemies) continue;
                Vector3D d = o.WorldBoundCenter - from;
                double d2 = d.LengthSquared();
                if (d2 < bd) { bd = d2; best = o; }
            }
            return best;
        }
    }

    public static class PirateShipFactory
    {
        sealed class Builder
        {
            public readonly GridData Grid = new GridData { DisplayName = "Pirate", Large = true, EntityId = 9000000, DampenersEnabled = false };
            public readonly HashSet<long> Occupied = new HashSet<long>();
            readonly DefDb _defs; long _nextId = 9000001;
            public Builder(DefDb defs) { _defs = defs; }

            static long Key(int x, int y, int z) { return ((long)(x + 512) << 40) | ((long)(y + 512) << 20) | (long)(z + 512); }

            public BlockData Add(string type, string sub, Vector3I min, Base6Directions.Direction fwd, Base6Directions.Direction up, bool terminal)
            {
                BlockDef d = _defs.Get(type, sub);
                var o = new MyBlockOrientation(fwd, up);
                Vector3I max = OrientMath.MaxCell(min, d.SizeCells, o);
                for (int z = min.Z; z <= max.Z; z++) for (int y = min.Y; y <= max.Y; y++) for (int x = min.X; x <= max.X; x++)
                            if (!Occupied.Add(Key(x, y, z))) throw new InvalidOperationException("pirate layout overlap at " + x + "," + y + "," + z + " (" + type + "/" + sub + ")");
                var b = new BlockData { XsiType = "MyObjectBuilder_" + type, TypeId = type, Subtype = sub, Min = min, Orientation = o };
                if (terminal) b.EntityId = _nextId++;
                Grid.Blocks.Add(b);
                return b;
            }

            public BlockData Add(string type, string sub, int x, int y, int z, bool terminal)
            {
                return Add(type, sub, new Vector3I(x, y, z), Base6Directions.Direction.Forward, Base6Directions.Direction.Up, terminal);
            }
        }

        static Base6Directions.Direction DirOf(Vector3I n)
        {
            if (n.X > 0) return Base6Directions.Direction.Right;
            if (n.X < 0) return Base6Directions.Direction.Left;
            if (n.Y > 0) return Base6Directions.Direction.Up;
            if (n.Y < 0) return Base6Directions.Direction.Down;
            return n.Z > 0 ? Base6Directions.Direction.Backward : Base6Directions.Direction.Forward;
        }

        // thruster on the hull face cell 'baseCell' pointing its exhaust along the outward normal n (ship is pushed along -n)
        static void Thruster(Builder b, Vector3I baseCell, Vector3I n)
        {
            var c1 = new Vector3I(baseCell.X + n.X, baseCell.Y + n.Y, baseCell.Z + n.Z);
            var c2 = new Vector3I(baseCell.X + 2 * n.X, baseCell.Y + 2 * n.Y, baseCell.Z + 2 * n.Z);
            var min = new Vector3I(Math.Min(c1.X, c2.X), Math.Min(c1.Y, c2.Y), Math.Min(c1.Z, c2.Z));
            var up = n.Y != 0 ? Base6Directions.Direction.Backward : Base6Directions.Direction.Up;
            b.Add("Thrust", "LargeBlockSmallThrust", min, DirOf(n), up, true);
        }

        /// <summary>Returns a BlueprintData built in memory (same structure as a loaded bp.sbc), name "Pirate".</summary>
        public static BlueprintData Build(DefDb defs)
        {
            var b = new Builder(defs);
            const int NX = 7, NY = 4, NZ = 12;
            for (int z = 0; z < NZ; z++)
                for (int y = 0; y < NY; y++)
                    for (int x = 0; x < NX; x++)
                    {
                        bool shell = x == 0 || x == NX - 1 || y == 0 || y == NY - 1 || z == 0 || z == NZ - 1;
                        if (!shell) continue;
                        b.Add("CubeBlock", z == 0 ? "LargeHeavyBlockArmorBlock" : "LargeBlockArmorBlock", x, y, z, false);
                    }
            // thrusters: 2 per direction
            Thruster(b, new Vector3I(2, 1, NZ - 1), new Vector3I(0, 0, 1));
            Thruster(b, new Vector3I(4, 2, NZ - 1), new Vector3I(0, 0, 1));
            Thruster(b, new Vector3I(2, 1, 0), new Vector3I(0, 0, -1));
            Thruster(b, new Vector3I(4, 2, 0), new Vector3I(0, 0, -1));
            Thruster(b, new Vector3I(0, 1, 6), new Vector3I(-1, 0, 0));
            Thruster(b, new Vector3I(0, 2, 4), new Vector3I(-1, 0, 0));
            Thruster(b, new Vector3I(NX - 1, 1, 6), new Vector3I(1, 0, 0));
            Thruster(b, new Vector3I(NX - 1, 2, 4), new Vector3I(1, 0, 0));
            Thruster(b, new Vector3I(0, NY - 1, 6), new Vector3I(0, 1, 0));
            Thruster(b, new Vector3I(NX - 1, NY - 1, 6), new Vector3I(0, 1, 0));
            Thruster(b, new Vector3I(1, 0, 6), new Vector3I(0, -1, 0));
            Thruster(b, new Vector3I(5, 0, 6), new Vector3I(0, -1, 0));
            // roof turrets (3x3x3 cells each)
            b.Add("LargeGatlingTurret", "", 2, NY, 1, true).CustomName = "Pirate Gatling A";
            b.Add("LargeMissileTurret", "", 2, NY, 5, true).CustomName = "Pirate Missile";
            b.Add("LargeGatlingTurret", "", 2, NY, 9, true).CustomName = "Pirate Gatling B";
            // interior
            b.Add("Gyro", "LargeBlockGyro", 1, 1, 3, true); b.Add("Gyro", "LargeBlockGyro", 5, 1, 3, true);
            b.Add("Gyro", "LargeBlockGyro", 1, 1, 8, true); b.Add("Gyro", "LargeBlockGyro", 5, 1, 8, true);
            foreach (int z in new[] { 3, 4, 7, 8 })
            {
                var bat = b.Add("BatteryBlock", "LargeBlockBatteryBlock", 3, 1, z, true);
                bat.Props["CurrentStoredPower"] = "3";
            }
            b.Add("RemoteControl", "LargeBlockRemoteControl", 3, 2, 5, true).CustomName = "Pirate RC";

            var bp = new BlueprintData { Name = "Pirate" };
            bp.Grids.Add(b.Grid);
            return bp;
        }
    }

    public sealed class PirateBrain : IShipBrain
    {
        public Body Ship;
        public Body Target;
        public double Kp = 0.8;               // attitude gain (1/s)
        int _orbitDir = 1;
        long _nextAcquire;
        public PirateBrain(Body ship) { Ship = ship; }

        public void Tick(World w)
        {
            var b = Ship;
            if (b == null || !b.Alive || b.Blocks.Count == 0 || b.Mass <= 0) return;
            var s = w.Settings;
            b.DampenersOn = false;
            if (w.Tick >= _nextAcquire || Target == null || !Target.Alive || Target.Blocks.Count == 0)
            {
                _nextAcquire = w.Tick + 30;
                var t = ShipPilot.NearestEnemy(w, b, b.Position, 1e9);
                if (!ReferenceEquals(t, Target)) { Target = t; }
                if (t != null && (w.Tick / 1800) % 2 == 1) _orbitDir = -1; else _orbitDir = 1;   // flip the circling direction every 30 s
            }
            Vector3D v = b.LinearVelocity;
            if (Target == null)
            {
                ShipPilot.SetAcceleration(b, ShipPilot.Clamp(v * -0.5, 3));
                ShipPilot.SetAngularVelocity(b, default(Vector3D), s);
                return;
            }
            Vector3D rel = Target.Position - b.Position;
            double d = rel.Length();
            if (d < 1) return;
            Vector3D dirT = rel * (1.0 / d);
            Vector3D up = Vector3D.Up;
            Vector3D vdes, fwd;
            if (d > s.PirateOrbitRange + 100)
            {
                vdes = dirT * s.PirateCruiseSpeed;
                fwd = dirT;
            }
            else
            {
                Vector3D tang = Vector3D.Cross(up, dirT);
                double tl = tang.Length();
                tang = tl > 1e-6 ? tang * (_orbitDir / tl) : new Vector3D(1, 0, 0);
                double radial = (d - s.PirateOrbitRange) * 0.1; if (radial > 10) radial = 10; if (radial < -10) radial = -10;
                vdes = tang * s.PirateOrbitSpeed + dirT * radial;
                fwd = tang;
            }
            ShipPilot.SetAcceleration(b, ShipPilot.Clamp((vdes - v) * 0.5, 50));
            double fu = Math.Abs(fwd.Y);
            Vector3D upD = fu > 0.98 ? new Vector3D(0, 0, -1) : up;
            Vector3D rot = ShipPilot.RotationToFrame(b, fwd, upD);
            ShipPilot.SetAngularVelocity(b, ShipPilot.Clamp(rot * Kp, s.PirateMaxTurnRate), s);
        }
    }
}
