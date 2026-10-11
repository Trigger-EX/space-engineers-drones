// REVIEWER (physics audit). Independent oracles for the physics core, written from first principles WITHOUT calling OrientMath,
// Body.RecomputeMassProperties, Raycast, Physics.* maths or the quaternion code to produce the expected values:
//   grid axes in the world come from Cross(Forward, Up) of the placement; inertia is rebuilt in tensor form from the cell boxes;
//   rotation is Rodrigues; a Gauss-Jordan solve replaces the closed-form inverse.
// Run: mono out/sim/SimHost.exe selftest audit
using System;
using System.Collections.Generic;
using System.IO;
using VRageMath;
using VRage.Game;
using Sandbox.ModAPI.Ingame;
using SeSim.Blueprint;
using SeSim.Api;
using SeSim.Combat;

namespace SeSim.Core
{
    public static class SelfTestAudit
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }
        static bool Near(Vector3D a, Vector3D b, double eps) { return (a - b).Length() < eps; }

        public static int Run()
        {
            _fail = 0; _pass = 0;
            Console.WriteLine("SelfTestAudit (independent physics oracles)");
            Guarded("inertia tensor and torque response of a rotated asymmetric body", TestTorqueResponse);
            Guarded("free rotation direction and exactness (Rodrigues)", TestFreeRotation);
            Guarded("gyro axes for all 24 block orientations (through the IMyGyro API)", TestGyroAxes24);
            Guarded("gyro torque ceiling (GyroPower, number of gyros, supply)", TestGyroCeiling);
            Guarded("speed and angular-speed clamps keep direction", TestClamps);
            Guarded("thrust direction for all 24 block orientations on a rotated ship", TestThrust24);
            Guarded("projectile sweep against a rotating and a translating target", TestSweep);
            Guarded("explosion falloff against an oracle (rotated body)", TestExplosion);
            Guarded("camera: forward axis, pitch/yaw, cone, charge, hit chain", TestCamera);
            Guarded("weapon muzzle position and shot direction for all 24 mountings", TestWeaponMounts);
            Guarded("world AABB and bounding sphere of a rotated body", TestBounds);
            Guarded("gyroscopic term conserves energy and momentum", TestGyroscopic);
            Console.WriteLine(_fail == 0 ? "SelfTestAudit passed (" + _pass + " checks)" : "SelfTestAudit FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        static void Guarded(string name, Action a)
        {
            Console.WriteLine(" - " + name);
            try { a(); } catch (Exception e) { Check(false, name + " threw " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static string _root;
        static string Root()
        {
            if (_root != null) return _root;
            string dir = Path.GetDirectoryName(typeof(SelfTestAudit).Assembly.Location);
            for (int i = 0; i < 6 && dir != null; i++) { if (File.Exists(Path.Combine(dir, "defs_stats.json"))) return _root = dir; dir = Path.GetDirectoryName(dir); }
            dir = Directory.GetCurrentDirectory();
            if (File.Exists(Path.Combine(dir, "defs_stats.json"))) return _root = dir;
            throw new FileNotFoundException("defs_stats.json not found (run from the repo root)");
        }
        static DefDb Defs() { return DefDb.LoadCached(Path.Combine(Root(), "defs_stats.json")); }

        sealed class Sink : IEventSink
        {
            public int Hits, Friendly, Shots;
            public void On(ref SimEvent e) { if (e.Kind == EventKind.Hit) Hits++; else if (e.Kind == EventKind.FriendlyFire) Friendly++; else if (e.Kind == EventKind.Shot) Shots++; }
            public void EndTick(World w) { }
        }

        static World NewWorld(bool full, Sink sink = null, SimSettings s = null)
        {
            var w = new World(s ?? new SimSettings(), Defs(), 1, !full);
            w.SkipApi = !full;
            w.Teams.Init(4);
            if (sink != null) w.Events = sink;
            return w;
        }

        static BlockData Bd(string type, string sub, int x, int y, int z, string fwd = null, string up = null)
        {
            var b = new BlockData { XsiType = "MyObjectBuilder_" + type, TypeId = type, Subtype = sub, Min = new Vector3I(x, y, z) };
            if (fwd != null || up != null) b.Orientation = new MyBlockOrientation(OrientMath.ParseDir(fwd ?? "Forward"), OrientMath.ParseDir(up ?? "Up"));
            return b;
        }
        static BlockData Armor(int x, int y, int z) { return Bd("CubeBlock", "SmallBlockArmorBlock", x, y, z); }
        static BlockData Heavy(int x, int y, int z) { return Bd("CubeBlock", "SmallHeavyBlockArmorBlock", x, y, z); }
        static BlockData Battery(int x, int y, int z) { var b = Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", x, y, z); b.Props["CurrentStoredPower"] = "0.05"; return b; }

        static long _idc = 5000;
        static Body Spawn(World w, int team, int ship, Vector3D pos, Vector3D? fwd, Vector3D? up, Vector3D? v, Vector3D? om, bool large, params BlockData[] blocks)
        {
            var bp = new BlueprintData { Name = "audit" };
            _idc += 100000;
            var g = new GridData { DisplayName = "audit", Large = large, EntityId = _idc, DampenersEnabled = false };
            long id = _idc + 1;
            foreach (var b in blocks) { if (b.TypeId != "CubeBlock" && b.EntityId == 0) b.EntityId = id++; g.Blocks.Add(b); }
            bp.Grids.Add(g);
            var at = new SpawnPlacement { Origin = pos, Forward = fwd ?? Vector3D.Forward, Up = up ?? Vector3D.Up, LinearVelocity = v ?? Vector3D.Zero, AngularVelocity = om ?? Vector3D.Zero };
            return GridFactory.Spawn(w, bp, team, ship, at, null)[0];
        }

        static void Run(World w, int n) { for (int i = 0; i < n; i++) w.Step(); }

        static Vector3D DirVec(string d)
        {
            switch (d)
            {
                case "Forward": return new Vector3D(0, 0, -1);
                case "Backward": return new Vector3D(0, 0, 1);
                case "Left": return new Vector3D(-1, 0, 0);
                case "Right": return new Vector3D(1, 0, 0);
                case "Up": return new Vector3D(0, 1, 0);
                default: return new Vector3D(0, -1, 0);
            }
        }
        static readonly string[] Dirs = { "Forward", "Backward", "Left", "Right", "Up", "Down" };
        static List<string[]> All24()
        {
            var l = new List<string[]>();
            foreach (var f in Dirs) foreach (var u in Dirs)
                if (Math.Abs(Vector3D.Dot(DirVec(f), DirVec(u))) < 0.5 && Vector3D.Cross(DirVec(f), DirVec(u)).Length() > 0.5) l.Add(new[] { f, u });
            return l;
        }

        // grid-to-world oracle from the placement alone
        sealed class Frame
        {
            public Vector3D R, U, B;     // grid +X, +Y, +Z in the world
            public Frame(Vector3D fwd, Vector3D up) { fwd = Vector3D.Normalize(fwd); R = Vector3D.Normalize(Vector3D.Cross(fwd, up)); U = Vector3D.Cross(R, fwd); B = -fwd; }
            public Vector3D ToWorld(Vector3D v) { return R * v.X + U * v.Y + B * v.Z; }
            public Vector3D ToGrid(Vector3D v) { return new Vector3D(Vector3D.Dot(v, R), Vector3D.Dot(v, U), Vector3D.Dot(v, B)); }
        }

        // tensor-form inertia about the COM from the cell boxes (grid axes). Returns I[3,3], mass, com (grid metres)
        static double[,] OracleInertia(Body b, out double mass, out Vector3D com)
        {
            double cell = b.CellSize; mass = 0; com = Vector3D.Zero;
            foreach (var k in b.Blocks)
            {
                Vector3D c = new Vector3D(k.Min.X + k.Max.X, k.Min.Y + k.Max.Y, k.Min.Z + k.Max.Z) * (0.5 * cell);
                mass += k.Mass; com += c * k.Mass;
            }
            com = com / mass;
            var I = new double[3, 3];
            foreach (var k in b.Blocks)
            {
                Vector3D c = new Vector3D(k.Min.X + k.Max.X, k.Min.Y + k.Max.Y, k.Min.Z + k.Max.Z) * (0.5 * cell);
                Vector3D size = new Vector3D(k.Max.X - k.Min.X + 1, k.Max.Y - k.Min.Y + 1, k.Max.Z - k.Min.Z + 1) * cell;
                Vector3D r = c - com; double m = k.Mass;
                double[] rr = { r.X, r.Y, r.Z };
                double[] sz = { size.X, size.Y, size.Z };
                double r2 = rr[0] * rr[0] + rr[1] * rr[1] + rr[2] * rr[2];
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        double v = (i == j ? r2 : 0) - rr[i] * rr[j];
                        if (i == j) v += (sz[(i + 1) % 3] * sz[(i + 1) % 3] + sz[(i + 2) % 3] * sz[(i + 2) % 3]) / 12.0;
                        I[i, j] += m * v;
                    }
            }
            return I;
        }

        static Vector3D Solve3(double[,] A, Vector3D rhs)
        {
            double[,] m = new double[3, 4];
            for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) m[i, j] = A[i, j]; }
            m[0, 3] = rhs.X; m[1, 3] = rhs.Y; m[2, 3] = rhs.Z;
            for (int c = 0; c < 3; c++)
            {
                int p = c; for (int r = c + 1; r < 3; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
                for (int j = 0; j < 4; j++) { double t = m[c, j]; m[c, j] = m[p, j]; m[p, j] = t; }
                for (int r = 0; r < 3; r++)
                {
                    if (r == c) continue;
                    double f = m[r, c] / m[c, c];
                    for (int j = c; j < 4; j++) m[r, j] -= f * m[c, j];
                }
            }
            return new Vector3D(m[0, 3] / m[0, 0], m[1, 3] / m[1, 1], m[2, 3] / m[2, 2]);
        }
        static Vector3D Mul3(double[,] A, Vector3D v)
        {
            return new Vector3D(A[0, 0] * v.X + A[0, 1] * v.Y + A[0, 2] * v.Z, A[1, 0] * v.X + A[1, 1] * v.Y + A[1, 2] * v.Z, A[2, 0] * v.X + A[2, 1] * v.Y + A[2, 2] * v.Z);
        }
        static Vector3D Rodrigues(Vector3D v, Vector3D axisUnit, double th)
        {
            return v * Math.Cos(th) + Vector3D.Cross(axisUnit, v) * Math.Sin(th) + axisUnit * (Vector3D.Dot(axisUnit, v) * (1 - Math.Cos(th)));
        }

        // =====================================================================================================
        static void TestTorqueResponse()
        {
            var w = NewWorld(false);
            Vector3D fwd = Vector3D.Normalize(new Vector3D(0.3, 0.2, -1));
            Vector3D up = Vector3D.Normalize(Vector3D.Cross(Vector3D.Cross(fwd, Vector3D.Up), fwd));
            var fr = new Frame(fwd, up);
            var b = Spawn(w, 0, 0, new Vector3D(10, 20, 30), fwd, up, null, null, false,
                Armor(0, 0, 0), Armor(1, 0, 0), Armor(1, 1, 0), Heavy(2, 2, 1), Armor(0, 0, 3), Armor(3, 1, 2), Battery(0, 2, 2));
            double mass; Vector3D com; var I = OracleInertia(b, out mass, out com);
            Check(Math.Abs(b.Mass - mass) < 1e-9 && Near(b.ComLocal, com, 1e-12), "mass and COM agree with the oracle");
            bool tensor = true;
            double[,] mine = { { b.InertiaLocal.M11, b.InertiaLocal.M12, b.InertiaLocal.M13 }, { b.InertiaLocal.M21, b.InertiaLocal.M22, b.InertiaLocal.M23 }, { b.InertiaLocal.M31, b.InertiaLocal.M32, b.InertiaLocal.M33 } };
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) if (Math.Abs(mine[i, j] - I[i, j]) > 1e-9 * (1 + Math.Abs(I[i, j]))) tensor = false;
            Check(tensor, "inertia tensor (incl. products of inertia) agrees with the tensor-form oracle");
            Check(Near(b.RotMatrix.Right, fr.R, 1e-12) && Near(b.RotMatrix.Up, fr.U, 1e-12) && Near(b.RotMatrix.Backward, fr.B, 1e-12), "body orientation = placement Forward/Up (Right = Forward x Up)");
            // one torque pulse
            Vector3D tau = new Vector3D(4000, -2500, 9000);
            b.Torque = tau;
            Physics.Integrate(b, w.Settings, SimSettings.Dt);
            Vector3D dwGrid = Solve3(I, fr.ToGrid(tau)) * SimSettings.Dt;
            Vector3D expect = fr.ToWorld(dwGrid);
            Check(Near(b.AngularVelocity, expect, expect.Length() * 1e-9), "world torque -> angular velocity: R I^-1 R^T tau dt (" + b.AngularVelocity + " vs " + expect + ")");
            // pure force at the COM: no rotation, acceleration F/m
            b.AngularVelocity = Vector3D.Zero; b.LinearVelocity = Vector3D.Zero;
            b.Force = new Vector3D(100, -200, 300);
            Physics.Integrate(b, w.Settings, SimSettings.Dt);
            Check(Near(b.LinearVelocity, new Vector3D(100, -200, 300) * (SimSettings.Dt / mass), 1e-12) && b.AngularVelocity.Length() == 0, "force through the COM: dv = F dt / m, no rotation");
        }

        // =====================================================================================================
        static void TestFreeRotation()
        {
            var w = NewWorld(false);
            Vector3D fwd = Vector3D.Normalize(new Vector3D(-0.4, 0.1, -1));
            Vector3D up = Vector3D.Normalize(Vector3D.Cross(Vector3D.Cross(fwd, Vector3D.Up), fwd));
            var fr = new Frame(fwd, up);
            Vector3D om = new Vector3D(0.7, -1.1, 0.4);
            var b = Spawn(w, 0, 0, Vector3D.Zero, fwd, up, null, om, false, Armor(0, 0, 0), Armor(1, 0, 0), Armor(0, 1, 0), Armor(0, 0, 2));
            Run(w, 100);
            double th = om.Length() * 100.0 / 60.0; Vector3D ax = om / om.Length();
            bool ok = Near(b.RotMatrix.Right, Rodrigues(fr.R, ax, th), 1e-9) && Near(b.RotMatrix.Up, Rodrigues(fr.U, ax, th), 1e-9) && Near(b.RotMatrix.Backward, Rodrigues(fr.B, ax, th), 1e-9);
            Check(ok, "orientation after 100 ticks = Rodrigues rotation of the initial axes about omega (right-handed, world-fixed axis)");
            Check(Near(b.AngularVelocity, om, 1e-12), "omega unchanged without torque (GyroscopicTerm off)");
            // Y-axis right-handed: +omega_y turns Forward toward Left
            var w2 = NewWorld(false);
            var b2 = Spawn(w2, 0, 0, Vector3D.Zero, null, null, null, new Vector3D(0, Math.PI / 2, 0), false, Armor(0, 0, 0), Armor(1, 0, 0));
            Run(w2, 60);
            Check(Near(b2.RotMatrix.Forward, Vector3D.Left, 1e-6), "omega = +Y pi/2 rad/s for 1 s turns Forward (-Z) to Left (-X)");
            var w3 = NewWorld(false);
            var b3 = Spawn(w3, 0, 0, Vector3D.Zero, null, null, null, new Vector3D(Math.PI / 2, 0, 0), false, Armor(0, 0, 0), Armor(1, 0, 0));
            Run(w3, 60);
            Check(Near(b3.RotMatrix.Forward, Vector3D.Up, 1e-6), "omega = +X pi/2 rad/s for 1 s turns Forward (-Z) to Up (nose up)");
            // linear velocity of an off-centre block follows v + w x r
            var w4 = NewWorld(false);
            var b4 = Spawn(w4, 0, 0, Vector3D.Zero, null, null, new Vector3D(1, 2, 3), new Vector3D(0, 1, 0), false, Armor(0, 0, 0), Armor(10, 0, 0));
            Vector3D p = b4.LocalToWorld(new Vector3D(5, 0, 0));
            Vector3D vp = b4.VelocityAtWorld(p);
            Vector3D expect = new Vector3D(1, 2, 3) + Vector3D.Cross(new Vector3D(0, 1, 0), p - b4.Position);
            Check(Near(vp, expect, 1e-12) && Math.Abs(vp.Z - (3 + (-1) * 0 - 2.5)) < 1e-9, "VelocityAtWorld = v + w x r (block at +X of the COM moves toward -Z for +Y spin: vz = " + vp.Z + ")");
        }

        // =====================================================================================================
        static void TestGyroAxes24()
        {
            var combos = All24();
            Check(combos.Count == 24, "24 valid block orientations enumerated");
            int bad = 0; string firstBad = null;
            foreach (var c in combos)
            {
                var w = NewWorld(true);
                var b = Spawn(w, 0, 0, Vector3D.Zero, null, null, null, null, false,
                    Bd("Gyro", "SmallBlockGyro", 0, 0, 0, c[0], c[1]), Battery(1, 0, 0), Armor(0, 1, 0), Armor(1, 1, 0), Armor(0, 0, 1), Armor(1, 0, 1));
                var gy = (IMyGyro)b.Gyros[0].Block.Api;
                Vector3D right = Vector3D.Cross(DirVec(c[0]), DirVec(c[1]));
                Vector3D upAx = DirVec(c[1]), backAx = -DirVec(c[0]);
                for (int axis = 0; axis < 3; axis++)
                {
                    w = NewWorld(true);
                    b = Spawn(w, 0, 0, Vector3D.Zero, null, null, null, null, false,
                        Bd("Gyro", "SmallBlockGyro", 0, 0, 0, c[0], c[1]), Battery(1, 0, 0), Armor(0, 1, 0), Armor(1, 1, 0), Armor(0, 0, 1), Armor(1, 0, 1));
                    gy = (IMyGyro)b.Gyros[0].Block.Api;
                    for (int t = 0; t < 420; t++)
                    {
                        gy.GyroOverride = true;
                        gy.Pitch = axis == 0 ? -1f : 0f; gy.Yaw = axis == 1 ? -1f : 0f; gy.Roll = axis == 2 ? -1f : 0f;   // script law for l = +1 rad/s on one axis
                        w.Step();
                    }
                    Vector3D want = axis == 0 ? right : axis == 1 ? upAx : backAx;
                    Vector3D wGrid = b.DirToLocal(b.AngularVelocity);     // world == grid at identity, but be explicit
                    if (!Near(wGrid, want, 0.01) && firstBad == null) firstBad = c[0] + "/" + c[1] + " axis " + axis + " got " + wGrid + " want " + want;
                    if (!Near(wGrid, want, 0.01)) bad++;
                }
            }
            Check(bad == 0, "Pitch=-1 turns about block Right, Yaw=-1 about block Up, Roll=-1 about block Backward (+Z), all 24 mountings (" + bad + " bad" + (firstBad != null ? ": " + firstBad : "") + ")");
        }

        // =====================================================================================================
        static BlockData[] HeavyCube(int n, params BlockData[] extra)
        {
            var l = new List<BlockData>();
            for (int x = 0; x < n; x++) for (int y = 0; y < n; y++) for (int z = 0; z < n; z++) l.Add(Heavy(x, y, z));
            l.AddRange(extra);
            return l.ToArray();
        }

        static void TestGyroCeiling()
        {
            // 10^3 heavy blocks (112 t, ~4.7e5 kg m2): the wanted torque (I*diff*0.5 = 7e5 N m) is above 448 kN m, so the ceiling binds
            foreach (int cfg in new[] { 0, 1, 2 })
            {
                var w = NewWorld(true);
                BlockData[] extra = cfg == 2
                    ? new[] { Bd("Gyro", "SmallBlockGyro", 10, 5, 5), Bd("Gyro", "SmallBlockGyro", 10, 4, 5), Battery(10, 6, 5) }
                    : new[] { Bd("Gyro", "SmallBlockGyro", 10, 5, 5), Battery(10, 6, 5) };
                var b = Spawn(w, 0, 0, Vector3D.Zero, null, null, null, null, false, HeavyCube(10, extra));
                double power = cfg == 0 ? 1.0 : 0.1;
                foreach (var g in b.Gyros) { var api = (IMyGyro)g.Block.Api; api.GyroOverride = true; api.GyroPower = (float)power; api.Yaw = -3f; }
                double mass; Vector3D com; var I = OracleInertia(b, out mass, out com);
                w.Step();
                Vector3D dL = Mul3(I, b.DirToLocal(b.AngularVelocity));
                double ceilingNm = 448000.0 * power * b.Gyros.Count;
                double got = dL.Length() * 60.0;
                Check(Math.Abs(got - ceilingNm) / ceilingNm < 0.01 && Math.Abs(dL.Y) / dL.Length() > 0.999,
                      "torque ceiling " + b.Gyros.Count + " gyro(s) at GyroPower " + power + ": |dL|/dt = " + got.ToString("F0") + " N m vs " + ceilingNm.ToString("F0"));
            }
            // empty battery: no torque at all
            var w2 = NewWorld(true);
            var b2 = Spawn(w2, 0, 0, Vector3D.Zero, null, null, null, null, false, Bd("Gyro", "SmallBlockGyro", 0, 0, 0), Battery(1, 0, 0), Armor(0, 1, 0));
            b2.Batteries[0].StoredMWh = 0;
            var g2 = (IMyGyro)b2.Gyros[0].Block.Api; g2.GyroOverride = true; g2.Yaw = -1f;
            Run(w2, 30);
            Check(b2.AngularVelocity.Length() < 1e-12, "no battery energy: the gyro produces no torque");
            // override target is clamped by the API at the angular speed cap
            g2.Yaw = -1000f;
            Check(Math.Abs(g2.Yaw) <= 2 * Math.PI + 1e-6 && Math.Abs(g2.Yaw) >= 6.2, "IMyGyro.Yaw setter clamps to 2 pi rad/s (reads back " + g2.Yaw + ")");
        }

        // =====================================================================================================
        static void TestClamps()
        {
            var w = NewWorld(false);
            var b = Spawn(w, 0, 0, Vector3D.Zero, null, null, new Vector3D(150, -50, 20), new Vector3D(8, -5, 3), false, Armor(0, 0, 0), Armor(1, 0, 0));
            Run(w, 1);
            Vector3D v0 = Vector3D.Normalize(new Vector3D(150, -50, 20)), w0 = Vector3D.Normalize(new Vector3D(8, -5, 3));
            Check(Math.Abs(b.LinearVelocity.Length() - 100) < 1e-9 && Near(b.LinearVelocity / 100, v0, 1e-9), "speed clamp keeps the velocity direction");
            Check(Math.Abs(b.AngularVelocity.Length() - 2 * Math.PI) < 1e-9 && Near(b.AngularVelocity / b.AngularVelocity.Length(), w0, 1e-9), "small-grid angular clamp (2 pi) keeps the rotation axis");
            var wl = NewWorld(false);
            var bl = Spawn(wl, 0, 0, Vector3D.Zero, null, null, null, new Vector3D(8, -5, 3), true, Bd("CubeBlock", "LargeBlockArmorBlock", 0, 0, 0));
            Run(wl, 1);
            Check(Math.Abs(bl.AngularVelocity.Length() - Math.PI) < 1e-9, "large-grid angular clamp = pi rad/s");
        }

        // =====================================================================================================
        static void TestThrust24()
        {
            Vector3D fwd = Vector3D.Normalize(new Vector3D(0.6, -0.3, -1)), pre = Vector3D.Cross(Vector3D.Cross(fwd, Vector3D.Up), fwd);
            Vector3D up = Vector3D.Normalize(pre);
            var fr = new Frame(fwd, up);
            int bad = 0; string firstBad = null;
            foreach (var c in All24())
            {
                var w = NewWorld(false);
                var b = Spawn(w, 0, 0, Vector3D.Zero, fwd, up, null, null, false, Bd("Thrust", "SmallBlockSmallThrust", 0, 0, 0, c[0], c[1]), Battery(0, 0, 2), Armor(3, 3, 3));
                b.DampenersOn = false;
                var t = b.Thrusters[0]; t.OverrideN = t.MaxForce;
                Run(w, 30);
                Vector3D want = fr.ToWorld(-DirVec(c[0]));             // exhaust leaves through block Forward, the ship goes the other way
                Vector3D got = b.LinearVelocity; double n = got.Length();
                bool ok = n > 0 && Near(got / n, want, 1e-6) && Math.Abs(n - 30.0 / 60.0 * t.MaxForce / b.Mass) < 1e-4 * n;
                if (!ok) { bad++; if (firstBad == null) firstBad = c[0] + "/" + c[1] + " got " + got + " want dir " + want; }
            }
            Check(bad == 0, "thrust direction and magnitude for all 24 mountings on a rotated ship (" + bad + " bad" + (firstBad != null ? ": " + firstBad : "") + ")");
        }

        // =====================================================================================================
        static void TestSweep()
        {
            // a real gun is needed only for its ammo definition
            var wa = NewWorld(true);
            var gunBody = Spawn(wa, 1, 1, new Vector3D(0, 5000, 0), null, null, null, null, false, Bd("SmallGatlingGun", "", 0, 0, 0));
            var ammo = gunBody.Weapons[0].Ammo;

            // (a) spinning beam: 20 light blocks along X, spin 6 rad/s about +Y; the tip block moves 0.47 m per tick
            foreach (bool sweep in new[] { true, false })
            {
                var s = new SimSettings { ProjectileSweepBodies = sweep };
                var sink = new Sink();
                var w = NewWorld(true, sink, s);
                var l = new List<BlockData>(); for (int x = 0; x < 20; x++) l.Add(Armor(x, 0, 0));
                var beam = Spawn(w, 0, 0, new Vector3D(100, 0, 0), null, null, null, new Vector3D(0, 6, 0), false, l.ToArray());
                Vector3D tip = beam.LocalToWorld(new Vector3D(9.5, 0, 0));
                w.Projectiles.Spawn(tip, new Vector3D(0, 1, 0), ammo, null, 7);
                w.Step();
                if (sweep) Check(sink.Hits == 1, "sweep: a projectile sitting inside the tip block at tick start hits it although the beam rotated 0.1 rad (direction of the pose back-map correct): hits=" + sink.Hits);
                else Check(sink.Hits == 0, "no sweep: the same projectile misses (the test discriminates), hits=" + sink.Hits);
            }
            // (b) translating beam along Z at 90 m/s (1.5 m per tick); projectile at the +Z end block, body moves toward -Z
            foreach (bool sweep in new[] { true, false })
            {
                var s = new SimSettings { ProjectileSweepBodies = sweep };
                var sink = new Sink();
                var w = NewWorld(true, sink, s);
                var l = new List<BlockData>(); for (int z = 0; z < 20; z++) l.Add(Armor(0, 0, z));
                var beam = Spawn(w, 0, 0, new Vector3D(100, 0, 0), null, null, new Vector3D(0, 0, -90), null, false, l.ToArray());
                beam.DampenersOn = false;
                Vector3D end = beam.LocalToWorld(new Vector3D(0, 0, 9.5));
                w.Projectiles.Spawn(end, new Vector3D(0, 1, 0), ammo, null, 7);
                w.Step();
                if (sweep) Check(sink.Hits == 1, "sweep: translating target, projectile at the trailing end block at tick start hits (hits=" + sink.Hits + ")");
                else Check(sink.Hits == 0, "no sweep: the same projectile misses (hits=" + sink.Hits + ")");
            }
            // (c) a fast bullet does not tunnel: 800 m/s (13.3 m per tick) across a 1-block wall
            {
                var sink = new Sink();
                var w = NewWorld(true, sink);
                var wall = Spawn(w, 0, 0, new Vector3D(0, 0, -100), null, null, null, null, false, Armor(0, 0, 0));
                w.Projectiles.Spawn(new Vector3D(0, 0, -93), new Vector3D(0, 0, -800), ammo, null, 7);
                Run(w, 2);
                Check(sink.Hits == 1, "800 m/s bullet crossing a single 0.5 m block inside one tick registers the hit (no tunnelling)");
                var sink2 = new Sink(); var w2 = NewWorld(true, sink2);
                Spawn(w2, 0, 0, new Vector3D(0, 0, -100), null, null, null, null, false, Armor(0, 0, 0));
                w2.Projectiles.Spawn(new Vector3D(0.3, 0, -93), new Vector3D(0, 0, -800), ammo, null, 7);   // 0.3 m beside a 0.25 m half-width block
                Run(w2, 2);
                Check(sink2.Hits == 0, "bullet passing 0.05 m outside the block edge misses");
            }
        }

        // =====================================================================================================
        static void TestExplosion()
        {
            Vector3D fwd = Vector3D.Normalize(new Vector3D(0.2, 0.5, -1));
            Vector3D up = Vector3D.Normalize(Vector3D.Cross(Vector3D.Cross(fwd, Vector3D.Up), fwd));
            var fr = new Frame(fwd, up);
            var sink = new Sink();
            var w = NewWorld(true, sink);
            var l = new List<BlockData>(); for (int x = 0; x < 12; x++) l.Add(Heavy(x, 0, 0));
            Vector3D origin = new Vector3D(50, 60, 70);
            var b = Spawn(w, 1, 1, origin, fwd, up, null, null, false, l.ToArray());
            foreach (var k in b.Blocks) { k.Hp = k.MaxHp = 50000f; k.CriticalHp = 0; }
            Vector3D center = origin + fr.ToWorld(new Vector3D(-0.3, 0.1, 0.05));
            double radius = 4.0, dmg = 400.0;
            Explosions.Explode(w, center, radius, dmg, 0);
            int bad = 0, inside = 0, outside = 0;
            foreach (var k in b.Blocks)
            {
                Vector3D bc = origin + fr.ToWorld(new Vector3D(k.Min.X * 0.5, 0, 0));
                double d = (bc - center).Length();
                double expect = d <= radius ? dmg * (1 - d / radius) : 0;
                double lost = 50000.0 - k.Hp;
                if (d <= radius) inside++; else outside++;
                if (Math.Abs(lost - expect) > 0.1) bad++;
            }
            Check(bad == 0 && inside >= 5 && outside >= 2, "linear falloff damage*(1-d/r) on every block of a rotated body, nothing beyond r (" + inside + " in, " + outside + " out, " + bad + " wrong)");
            Check(sink.Hits == 1, "one Hit event per damaged body");
        }

        // =====================================================================================================
        static void TestCamera()
        {
            Vector3D bf = Vector3D.Normalize(new Vector3D(-0.5, 0.2, -1));
            Vector3D bu = Vector3D.Normalize(Vector3D.Cross(Vector3D.Cross(bf, Vector3D.Up), bf));
            var fr = new Frame(bf, bu);
            int bad = 0; string firstBad = null; int badP = 0, badY = 0;
            foreach (var c in All24())
            {
                var w = NewWorld(true);
                var a = Spawn(w, 0, 0, new Vector3D(0, 0, 0), bf, bu, null, null, false, Bd("CameraBlock", "SmallCameraBlock", 0, 0, 0, c[0], c[1]), Battery(1, 0, 0), Armor(0, 1, 0));
                var cam = (SimCamera)a.Blocks[0].Api;
                cam.EnableRaycast = true;
                Run(w, 12);
                Vector3D gF = DirVec(c[0]), gU = DirVec(c[1]), gR = Vector3D.Cross(gF, gU);
                Vector3D F = fr.ToWorld(gF), U = fr.ToWorld(gU), R = fr.ToWorld(gR);
                Vector3D seen = default(Vector3D); bool got = false;
                SimCamera.DebugCast = (cc, d, dir, hit) => { seen = dir; got = true; };
                cam.Raycast(100, 0f, 0f);
                if (!got || !Near(seen, F, 1e-9)) { bad++; if (firstBad == null) firstBad = c[0] + "/" + c[1] + " dir " + seen + " want " + F; }
                got = false; cam.Raycast(100, 20f, 0f);
                double th = 20 * Math.PI / 180;
                if (!got || !Near(seen, F * Math.Cos(th) + U * Math.Sin(th), 1e-9)) badP++;
                got = false; cam.Raycast(100, 0f, 20f);
                if (!got || !Near(seen, F * Math.Cos(th) - R * Math.Sin(th), 1e-9)) badY++;
                SimCamera.DebugCast = null;
            }
            Check(bad == 0, "Raycast(d,0,0) fires along the block Forward for all 24 mountings on a rotated ship (" + bad + " bad" + (firstBad != null ? ": " + firstBad : "") + ")");
            Check(badP == 0, "Raycast pitch +20 tilts the ray toward block Up (" + badP + " bad)");
            Check(badY == 0, "Raycast yaw +20 tilts the ray toward block Left (MatrixD.CreateRotationY convention) (" + badY + " bad)  [sign inferred, see docs/sim-validation.md]");

            // charge, cone, hit chain on one camera
            var w2 = NewWorld(true);
            var a2 = Spawn(w2, 0, 0, Vector3D.Zero, bf, bu, null, null, false, Bd("CameraBlock", "SmallCameraBlock", 0, 0, 0, "Up", "Backward"), Battery(1, 0, 0), Armor(0, 1, 0));
            var cam2 = (SimCamera)a2.Blocks[0].Api;
            Check(cam2.AvailableScanRange == 0, "no charge before EnableRaycast");
            cam2.EnableRaycast = true;
            Run(w2, 90);
            Check(Math.Abs(cam2.AvailableScanRange - 3000.0) < 1e-6, "90 ticks of charge = 3000 m (2 km/s): " + cam2.AvailableScanRange);
            Vector3D camFwd = fr.ToWorld(Vector3D.Up);
            Vector3D camPos = a2.Blocks[0].WorldCenter;
            var tgtSink = new Sink(); w2.Events = tgtSink;
            // large target 400 m ahead of the camera along its Forward, big enough to be hit despite the ~0.5 m lateral offset
            var big = new List<BlockData>(); for (int x = -3; x <= 3; x++) for (int y = -3; y <= 3; y++) big.Add(Armor(x, y, 0)); big.Add(Battery(5, 5, 0));
            Vector3D tBack = camPos + camFwd * 400;
            // orient the wall so its plane is perpendicular to camFwd: grid Backward = -camFwd -> placement forward = camFwd
            Vector3D tUp = Vector3D.Normalize(Vector3D.Cross(Vector3D.Cross(camFwd, Vector3D.Right), camFwd));
            var tgt = Spawn(w2, 1, 1, tBack, camFwd, tUp, null, null, false, big.ToArray());
            double c0 = cam2.AvailableScanRange;
            var info = cam2.Raycast(1000);
            Check(!info.IsEmpty() && info.EntityId == tgt.EntityId, "ray along the camera's own Forward hits the target placed there");
            Check(info.HitPosition.HasValue && Math.Abs(Vector3D.Dot(info.HitPosition.Value - tBack, camFwd) + 0.25) < 0.01, "hit point lies on the near face of the wall (0.25 m before the block centre)");
            Check(Math.Abs(cam2.AvailableScanRange - (c0 - 1000)) < 1e-9, "a scan consumes exactly the requested distance (hit or miss)");
            Check(info.Position == info.BoundingBox.Center && info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies, "Position = BoundingBox.Center; enemy team");
            double c1 = cam2.AvailableScanRange;
            cam2.Raycast(500, 0f, 0f); Check(Math.Abs(cam2.AvailableScanRange - (c1 - 500)) < 1e-9, "a miss costs charge as well");
            double c2 = cam2.AvailableScanRange;
            var e45 = cam2.Raycast(100, 45f, 0f); var e46 = cam2.Raycast(100, 45.5f, 0f);
            Check(Math.Abs(cam2.AvailableScanRange - (c2 - 100)) < 1e-9, "pitch 45 deg allowed (charge used), 45.5 deg refused (no charge)");
            Check(cam2.CanScan(100, F2(camFwd, fr.ToWorld(Vector3D.Right), 44.5)) && !cam2.CanScan(100, F2(camFwd, fr.ToWorld(Vector3D.Right), 45.5)), "CanScan(distance, direction): cone is the angle to Forward, 44.5 ok / 45.5 not");
            Check(cam2.TimeUntilScan(cam2.AvailableScanRange + 666) == 333 || cam2.TimeUntilScan(cam2.AvailableScanRange + 666) == 334, "TimeUntilScan = ceil(missing / 2 m per ms): " + cam2.TimeUntilScan(cam2.AvailableScanRange + 666));
        }
        static Vector3D F2(Vector3D f, Vector3D side, double deg) { double t = deg * Math.PI / 180; return f * Math.Cos(t) + side * Math.Sin(t); }

        // =====================================================================================================
        static void TestWeaponMounts()
        {
            Vector3D fwd = Vector3D.Normalize(new Vector3D(0.2, -0.4, -1));
            Vector3D up = Vector3D.Normalize(Vector3D.Cross(Vector3D.Cross(fwd, Vector3D.Up), fwd));
            var fr = new Frame(fwd, up);
            Vector3D shipV = new Vector3D(20, -5, 7);
            int bad = 0; string firstBad = null; int n = 0;
            foreach (var c in All24())
            {
                var w = NewWorld(true);
                var a = Spawn(w, 0, 0, new Vector3D(1000, 0, 0), fwd, up, shipV, null, false, Armor(5, 5, 5), Bd("SmallGatlingGun", "", 2, 3, 4, c[0], c[1]));
                var gun = a.Weapons[0]; gun.SpreadRad = 0;
                var blk = gun.Block;
                Vector3I sz = blk.Def.SizeCells;                        // block-local X (right), Y (up), Z (length, along Backward)
                Vector3D gF = DirVec(c[0]), gU = DirVec(c[1]), gR = Vector3D.Cross(gF, gU);
                int[] ext = new int[3];
                double[] axF = { Math.Abs(gF.X), Math.Abs(gF.Y), Math.Abs(gF.Z) }, axU = { Math.Abs(gU.X), Math.Abs(gU.Y), Math.Abs(gU.Z) }, axR = { Math.Abs(gR.X), Math.Abs(gR.Y), Math.Abs(gR.Z) };
                for (int i = 0; i < 3; i++) ext[i] = axR[i] > 0.5 ? sz.X : axU[i] > 0.5 ? sz.Y : sz.Z;
                Vector3D centerGrid = new Vector3D(2 + (ext[0] - 1) * 0.5, 3 + (ext[1] - 1) * 0.5, 4 + (ext[2] - 1) * 0.5) * 0.5;
                Vector3D muzzleGrid = centerGrid + gF * (sz.Z * 0.5 * 0.5);
                Vector3D origin = new Vector3D(1000, 0, 0);
                Vector3D muzzleWorld = origin + fr.ToWorld(muzzleGrid);
                Vector3D dirWorld = fr.ToWorld(gF);
                gun.ShootOnceRequests = 1;
                w.Step();
                n++;
                if (w.Projectiles.Count != 1) { bad++; if (firstBad == null) firstBad = c[0] + "/" + c[1] + " projectiles " + w.Projectiles.Count; continue; }
                Vector3D v = w.Projectiles.Vel[0], p = w.Projectiles.Pos[0];
                double speed = gun.Ammo.Speed;
                // shooter velocity is a pure translation here (no spin), pose before the step == pose at the shot
                bool okV = Near(v, shipV + dirWorld * speed, 1e-6);
                bool okP = Near(p, muzzleWorld + v * SimSettings.Dt, 1e-6) || Near(p, muzzleWorld + shipV * 0 + v * SimSettings.Dt, 1e-3);
                if (!okV || !okP) { bad++; if (firstBad == null) firstBad = c[0] + "/" + c[1] + " v " + v + " want " + (shipV + dirWorld * speed) + " p " + p + " want " + (muzzleWorld + v * SimSettings.Dt); }
            }
            Check(bad == 0 && n == 24, "gatling mounted in all 24 orientations: muzzle point on the barrel end, velocity = ship velocity + Forward * speed (" + bad + " bad" + (firstBad != null ? ": " + firstBad : "") + ")");
        }

        // =====================================================================================================
        static void TestBounds()
        {
            Vector3D fwd = Vector3D.Normalize(new Vector3D(0.7, 0.3, -1));
            Vector3D up = Vector3D.Normalize(Vector3D.Cross(Vector3D.Cross(fwd, Vector3D.Up), fwd));
            var fr = new Frame(fwd, up);
            var w = NewWorld(true);
            Vector3D origin = new Vector3D(-40, 15, 220);
            var b = Spawn(w, 0, 0, origin, fwd, up, null, null, false, Armor(0, 0, 0), Armor(1, 0, 0), Armor(1, 2, 0), Heavy(4, 1, 1), Armor(0, 0, 6), Battery(3, 3, 3));
            Vector3D lmn = new Vector3D(1e18, 1e18, 1e18), lmx = -lmn; double maxR = 0;
            foreach (var k in b.Blocks)
                for (int c = 0; c < 8; c++)
                {
                    Vector3D corner = new Vector3D((((c & 1) != 0 ? k.Max.X + 0.5 : k.Min.X - 0.5)) * 0.5, (((c & 2) != 0 ? k.Max.Y + 0.5 : k.Min.Y - 0.5)) * 0.5, (((c & 4) != 0 ? k.Max.Z + 0.5 : k.Min.Z - 0.5)) * 0.5);
                    lmn = Vector3D.Min(lmn, corner); lmx = Vector3D.Max(lmx, corner);
                    maxR = Math.Max(maxR, (origin + fr.ToWorld(corner) - b.WorldBoundCenter).Length());
                }
            // SE's grid WorldAABB is the axis-aligned box of the ROTATED LOCAL box (looser than the box of the block corners)
            Vector3D mn = new Vector3D(1e18, 1e18, 1e18), mx = -mn;
            for (int c = 0; c < 8; c++)
            {
                Vector3D corner = new Vector3D((c & 1) != 0 ? lmx.X : lmn.X, (c & 2) != 0 ? lmx.Y : lmn.Y, (c & 4) != 0 ? lmx.Z : lmn.Z);
                Vector3D p = origin + fr.ToWorld(corner);
                mn = Vector3D.Min(mn, p); mx = Vector3D.Max(mx, p);
            }
            var box = b.WorldAabb();
            Check(Near(box.Min, mn, 1e-9) && Near(box.Max, mx, 1e-9), "WorldAabb equals the AABB of the rotated local bounding box (rotated body)");
            Check(b.BoundRadius >= maxR - 1e-9, "bounding sphere contains every block corner (R=" + b.BoundRadius.ToString("F3") + " >= " + maxR.ToString("F3") + ")");
        }

        // =====================================================================================================
        static void TestGyroscopic()
        {
            var s = new SimSettings { GyroscopicTerm = true };
            var w = NewWorld(false, null, s);
            var b = Spawn(w, 0, 0, Vector3D.Zero, null, null, null, new Vector3D(0.3, 1.0, 0.4), false, Armor(0, 0, 0), Armor(1, 0, 0), Armor(2, 0, 0), Armor(3, 0, 0), Armor(3, 1, 0), Armor(0, 0, 1));
            double mass; Vector3D com; var I = OracleInertia(b, out mass, out com);
            Func<double> energy = () => { Vector3D wl = b.DirToLocal(b.AngularVelocity); return 0.5 * Vector3D.Dot(wl, Mul3(I, wl)); };
            Func<Vector3D> L = () => b.AngularMomentumWorld();
            double e0 = energy(); Vector3D L0 = L();
            double maxW = 0, maxE = 0, maxLerr = 0;
            for (int i = 0; i < 1200; i++) { w.Step(); maxW = Math.Max(maxW, b.AngularVelocity.Length()); maxE = Math.Max(maxE, Math.Abs(energy() - e0) / e0); maxLerr = Math.Max(maxLerr, (L() - L0).Length() / L0.Length()); }
            Info("max |w| " + maxW.ToString("F3") + " rad/s, max energy error " + maxE.ToString("P2") + ", max |dL|/|L| " + maxLerr.ToString("P2") + ", I = " + I[0,0] + " " + I[1,1] + " " + I[2,2]);
            Check(Math.Abs(energy() - e0) / e0 < 0.02, "GyroscopicTerm on: rotational energy of a tumbling asymmetric body stays within 2% over 20 s (" + ((energy() - e0) / e0).ToString("P2") + ")");
            Check(Near(L(), L0, L0.Length() * 0.01), "GyroscopicTerm on: angular momentum vector conserved within 1%");
        }
    }
}
