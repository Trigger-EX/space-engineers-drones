// SLOT A. Self-checks for maths, orientation, the gyro sign convention and the physics core (thrusters, gyros, mass/inertia, power,
// integration limits, conservation, splitting, raycast, blueprint + defs loading, determinism, collisions).
// Run: mono out/sim/SimHost.exe selftest           (all slots; slot A part includes the physics block below)
//      mono out/sim/SimHost.exe selftest physics   (any command line that contains the word "physics" runs ONLY this class)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VRageMath;
using VRage.Game;
using SeSim.Blueprint;

namespace SeSim.Core
{
    /// <summary>Counts self-test sections that did not run (missing blueprints, scripts, ...); `selftest --strict` fails when it is not 0.</summary>
    public static class SelfTestSkips
    {
        public static int Count;
        public static void Note(string msg) { Count++; Console.WriteLine("  SKIP " + msg); }
    }

    public static class SelfTestA
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static bool Near(Vector3D a, Vector3D b, double eps = 1e-9) { return (a - b).Length() < eps; }
        static bool Rel(double a, double b, double rel) { return Math.Abs(a - b) <= rel * Math.Max(Math.Abs(b), 1e-12); }
        static void Info(string s) { Console.WriteLine("  info " + s); }

        /// <summary>True when the process was started with the word "physics" on its command line.</summary>
        public static bool PhysicsOnly()
        {
            foreach (var a in Environment.GetCommandLineArgs()) if (a == "physics" || a == "--physics") return true;
            return false;
        }

        public static int Run()
        {
            _fail = 0; _pass = 0;
            Console.WriteLine("SelfTestA (maths, orientation, gyro sign, physics core)");
            var sw = Stopwatch.StartNew();
            Conventions();
            Physics_();
            Console.WriteLine(_fail == 0 ? "SelfTestA passed (" + _pass + " checks, " + sw.ElapsedMilliseconds + " ms)" : "SelfTestA FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        // =====================================================================================================
        // conventions (architect)
        // =====================================================================================================
        static void Conventions()
        {
            // VRageMath conventions
            Check(Near(MatrixD.Identity.Forward, new Vector3D(0, 0, -1)), "Identity.Forward = -Z");
            var ry = MatrixD.CreateFromAxisAngle(Vector3D.Up, Math.PI / 2);
            Check(Near(Vector3D.TransformNormal(Vector3D.Forward, ry), Vector3D.Left), "CreateFromAxisAngle(Up, +90deg) turns Forward to Left (right-handed)");
            var q = QuaternionD.CreateFromAxisAngle(Vector3D.Up, Math.PI / 2);
            Check(Near(Vector3D.Transform(Vector3D.Forward, q), Vector3D.Left), "Quaternion rotation matches matrix rotation");
            var mq = MatrixD.CreateFromQuaternion(QuaternionD.CreateFromRotationMatrix(ry));
            Check(Near(mq.Forward, ry.Forward) && Near(mq.Up, ry.Up), "Matrix -> Quaternion -> Matrix round trip");
            var w = MatrixD.CreateWorld(new Vector3D(1, 2, 3), Vector3D.Right, Vector3D.Up);
            var wi = MatrixD.Invert(w);
            Check(Near(Vector3D.Transform(Vector3D.Transform(new Vector3D(5, -1, 2), w), wi), new Vector3D(5, -1, 2), 1e-9), "Invert(CreateWorld)");
            Check(double.IsNaN(Vector3D.Normalize(Vector3D.Zero).X), "Normalize(zero) is NaN like VRageMath");

            // Block orientation: thruster with Forward=Backward (exhaust to +Z) pushes the ship toward -Z (Forward)
            var o = new MyBlockOrientation(Base6Directions.Direction.Backward, Base6Directions.Direction.Up);
            var r = OrientMath.Rotation(o);
            Check(Near(r.Forward, Vector3D.Backward), "thruster BlockOrientation Forward=Backward -> block Forward = +Z");
            Check(Near(r.Backward, Vector3D.Forward), "its thrust direction (block Backward) = grid Forward");
            // Antenna Forward=Up, Up=Backward, size 1x1x2 -> 2 cells along grid Y
            var oa = new MyBlockOrientation(Base6Directions.Direction.Up, Base6Directions.Direction.Backward);
            var sz = OrientMath.RotatedSize(new Vector3I(1, 1, 2), oa);
            Check(sz == new Vector3I(1, 2, 1), "1x1x2 block with Forward=Up spans 2 cells in Y");
            Check(Near(OrientMath.Rotation(oa).Right, Vector3D.Cross(Vector3D.Up, Vector3D.Backward)), "Right = Cross(Forward, Up)");
            Check(oa.TransformDirection(Base6Directions.Direction.Backward) == Base6Directions.Direction.Down, "TransformDirection(Backward) = flipped Forward");

            // Gyro sign convention: a script computes l = desired right-handed rate in gyro-local axes and writes
            // Pitch=-l.X, Yaw=-l.Y, Roll=-l.Z. With the default signs the commanded grid-local rate must equal R*l.
            var s = new SimSettings();
            var gyroO = new MyBlockOrientation(Base6Directions.Direction.Down, Base6Directions.Direction.Forward); // arbitrary mount
            var g = new GyroUnit { RotToGrid = OrientMath.Rotation(gyroO) };
            var lGrid = new Vector3D(0.3, -0.7, 1.1);                                   // desired grid-local rate
            var l = Vector3D.TransformNormal(lGrid, MatrixD.Transpose(g.RotToGrid));   // what the script computes (gyro-local)
            g.Pitch = -(float)l.X; g.Yaw = -(float)l.Y; g.Roll = -(float)l.Z;
            Check(Near(g.TargetGridLocal(s), lGrid, 1e-6), "default gyro signs: script law commands exactly the desired rate");
            Info("positive Yaw (default signs) turns the nose toward +Right; positive Pitch nose toward Down; positive Roll Up toward Right");
        }

        // =====================================================================================================
        // helpers for physics tests
        // =====================================================================================================
        static string _root;
        static string Root()
        {
            if (_root != null) return _root;
            string dir = Path.GetDirectoryName(typeof(SelfTestA).Assembly.Location);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "defs_stats.json"))) return _root = dir;
                dir = Path.GetDirectoryName(dir);
            }
            dir = Directory.GetCurrentDirectory();
            if (File.Exists(Path.Combine(dir, "defs_stats.json"))) return _root = dir;
            throw new FileNotFoundException("defs_stats.json not found (run from the repo or out/sim)");
        }

        static DefDb Defs() { return DefDb.LoadCached(Path.Combine(Root(), "defs_stats.json")); }

        static World NewWorld(ulong seed = 1, SimSettings s = null)
        {
            var w = new World(s ?? new SimSettings(), Defs(), seed, true);
            w.SkipApi = true;
            w.Teams.Init(3);
            return w;
        }

        static BlockData Bd(string type, string sub, int x, int y, int z, string fwd = null, string up = null)
        {
            var b = new BlockData { XsiType = "MyObjectBuilder_" + type, TypeId = type, Subtype = sub, Min = new Vector3I(x, y, z) };
            if (fwd != null || up != null)
                b.Orientation = new MyBlockOrientation(OrientMath.ParseDir(fwd ?? "Forward"), OrientMath.ParseDir(up ?? "Up"));
            return b;
        }
        static BlockData Armor(int x, int y, int z) { return Bd("CubeBlock", "SmallBlockArmorBlock", x, y, z); }
        static BlockData HeavyArmor(int x, int y, int z) { return Bd("CubeBlock", "SmallHeavyBlockArmorBlock", x, y, z); }
        static BlockData Thrust(int x, int y, int z, string fwd, string up = "Up") { return Bd("Thrust", "SmallBlockSmallThrust", x, y, z, fwd, up); }
        static BlockData Gyro(int x, int y, int z) { return Bd("Gyro", "SmallBlockGyro", x, y, z); }
        static BlockData Battery(int x, int y, int z, double stored = 0.05)
        {
            var b = Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", x, y, z);
            b.Props["CurrentStoredPower"] = stored.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return b;
        }

        static BlueprintData Bp(bool large, params BlockData[] blocks)
        {
            var bp = new BlueprintData { Name = "test" };
            var g = new GridData { DisplayName = "test", Large = large, EntityId = 1, DampenersEnabled = false };
            foreach (var b in blocks) g.Blocks.Add(b);
            bp.Grids.Add(g);
            return bp;
        }

        static SpawnPlacement Place(Vector3D pos, Vector3D? v = null, Vector3D? w = null, Vector3D? fwd = null, Vector3D? up = null)
        {
            return new SpawnPlacement { Origin = pos, Forward = fwd ?? Vector3D.Forward, Up = up ?? Vector3D.Up, LinearVelocity = v ?? Vector3D.Zero, AngularVelocity = w ?? Vector3D.Zero };
        }

        static Body Spawn(World w, int team, int ship, SpawnPlacement at, bool large, params BlockData[] blocks)
        {
            var list = GridFactory.Spawn(w, Bp(large, blocks), team, ship, at, null);
            return list[0];
        }

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        static Vector3D TotalMomentum(World w)
        {
            Vector3D p = Vector3D.Zero;
            foreach (var b in w.Bodies) p += b.LinearVelocity * b.Mass;
            return p;
        }
        static Vector3D TotalAngularMomentum(World w)   // about the world origin
        {
            Vector3D l = Vector3D.Zero;
            foreach (var b in w.Bodies) l += b.AngularMomentumWorld() + Vector3D.Cross(b.Position, b.LinearVelocity * b.Mass);
            return l;
        }

        static ulong StateHash(World w)
        {
            ulong h = 14695981039346656037UL;
            Action<double> add = d => { h ^= (ulong)BitConverter.DoubleToInt64Bits(d); h *= 1099511628211UL; };
            foreach (var b in w.Bodies)
            {
                add(b.Position.X); add(b.Position.Y); add(b.Position.Z);
                add(b.LinearVelocity.X); add(b.LinearVelocity.Y); add(b.LinearVelocity.Z);
                add(b.AngularVelocity.X); add(b.AngularVelocity.Y); add(b.AngularVelocity.Z);
                add(b.Rotation.X); add(b.Rotation.Y); add(b.Rotation.Z); add(b.Rotation.W);
                foreach (var bat in b.Batteries) add(bat.StoredMWh);
            }
            return h;
        }

        // =====================================================================================================
        // physics tests
        // =====================================================================================================
        static void Physics_()
        {
            Console.WriteLine("-- physics core --");
            TestSettingsAndJson();
            TestMassInertia();
            TestThrusterBasics();
            TestThrusterOrientations();
            TestTorqueFromOffset();
            TestGyro();
            TestLimits();
            TestConservation();
            TestPower();
            TestDampeners();
            TestSplit();
            TestRemoveBlockKinematics();
            TestRemovalFuzz();
            TestRaycast();
            TestCollision();
            TestDeterminism();
            TestBlueprints();
        }

        static void TestSettingsAndJson()
        {
            var s = new SimSettings();
            s.ApplyOverride("--gyro-pitch-sign", "1"); s.ApplyOverride("collision-damage", "true"); s.ApplyOverride("SmallMaxSpeed", "55.5");
            Check(s.GyroPitchSign == 1 && s.CollisionDamage && s.SmallMaxSpeed == 55.5, "SimSettings.ApplyOverride: kebab/Pascal names, int/bool/double");
            bool threw = false; try { s.ApplyOverride("--no-such-setting", "1"); } catch (ArgumentException) { threw = true; }
            Check(threw, "SimSettings.ApplyOverride: unknown setting is an error");
            Check(new SimSettings().Hash() != s.Hash() && new SimSettings().Hash() == new SimSettings().Hash(), "SimSettings.Hash is stable and sensitive");
            var j = MiniJson.Parse("{\"a\":[1,2.5,-3e2,{\"b\":\"x\\n\\u0041\"}],\"c\":true,\"d\":null,\"e\":NaN}") as Dictionary<string, object>;
            var arr = MiniJson.Arr(j, "a");
            Check(arr != null && arr.Count == 4 && (double)arr[2] == -300 && MiniJson.Str(arr[3], "b", "") == "x\nA" && MiniJson.Bool(j, "c", false) && double.IsNaN(MiniJson.Num(j, "e", 0)),
                  "MiniJson parses numbers, strings with escapes, bool, null, NaN");
        }

        static void TestMassInertia()
        {
            var w = NewWorld();
            var one = Spawn(w, 0, 0, Place(new Vector3D(100, 0, 0)), false, Armor(0, 0, 0));
            Check(Rel(one.Mass, 20, 1e-9) && Near(one.ComLocal, Vector3D.Zero), "single armor block: mass 20 kg, COM at the block centre");
            Check(Rel(one.InertiaLocal.M11, 20.0 / 6 * 0.25, 1e-9) && Rel(one.InertiaLocal.M22, one.InertiaLocal.M11, 1e-12), "single cube inertia = m a^2 / 6");
            Check(Near(one.Position, new Vector3D(100, 0, 0)), "Position = COM = placed origin for a single block");
            Check(Rel(one.BoundRadius, Math.Sqrt(3) * 0.25, 1e-9), "bound radius of one small block = half diagonal");

            var two = Spawn(w, 0, 1, Place(new Vector3D(0, 100, 0)), false, Armor(0, 0, 0), Armor(1, 0, 0));
            double a2 = 20.0 / 6 * 0.25;
            Check(Rel(two.Mass, 40, 1e-9) && Near(two.ComLocal, new Vector3D(0.25, 0, 0), 1e-12), "two blocks: mass 40, COM halfway");
            Check(Rel(two.InertiaLocal.M11, 2 * a2, 1e-9), "two blocks along X: Ixx = 2 * cube inertia");
            Check(Rel(two.InertiaLocal.M33, 2 * (a2 + 20 * 0.25 * 0.25), 1e-9) && Rel(two.InertiaLocal.M22, two.InertiaLocal.M33, 1e-12), "two blocks along X: Izz includes the parallel-axis term");
            Check(Near(two.Position, new Vector3D(0.25, 100, 0), 1e-12), "world COM follows the block layout (origin placement preserved)");
            // I * I^-1 = identity on an asymmetric body
            var asym = Spawn(w, 0, 2, Place(new Vector3D(0, 0, 100)), false, Armor(0, 0, 0), Armor(1, 0, 0), Armor(1, 1, 0), HeavyArmor(2, 2, 1), Armor(0, 0, 3));
            var I = asym.InertiaLocal; var Iv = asym.InvInertiaLocal;
            var prod = new MatrixD(I.M11, I.M12, I.M13, 0, I.M21, I.M22, I.M23, 0, I.M31, I.M32, I.M33, 0, 0, 0, 0, 1) * Iv;
            bool ident = Math.Abs(prod.M11 - 1) < 1e-9 && Math.Abs(prod.M22 - 1) < 1e-9 && Math.Abs(prod.M33 - 1) < 1e-9 && Math.Abs(prod.M12) < 1e-9 && Math.Abs(prod.M13) < 1e-9 && Math.Abs(prod.M21) < 1e-9 && Math.Abs(prod.M23) < 1e-9 && Math.Abs(prod.M31) < 1e-9 && Math.Abs(prod.M32) < 1e-9;
            Check(ident && Rel(asym.Mass, 4 * 20 + 112, 1e-9), "asymmetric body: inertia times inverse inertia = identity; mass = sum");
            // positive definite
            double d1 = I.M11, d2 = I.M11 * I.M22 - I.M12 * I.M21, d3 = I.M11 * (I.M22 * I.M33 - I.M23 * I.M32) - I.M12 * (I.M21 * I.M33 - I.M23 * I.M31) + I.M13 * (I.M21 * I.M32 - I.M22 * I.M31);
            Check(d1 > 0 && d2 > 0 && d3 > 0, "inertia tensor is positive definite");
            // large block: 2.5 m cell, 500 kg
            var big = Spawn(w, 0, 3, Place(new Vector3D(500, 500, 500)), true, Bd("CubeBlock", "LargeBlockArmorBlock", 0, 0, 0));
            Check(Rel(big.InertiaLocal.M11, 500.0 / 6 * 6.25, 1e-9) && big.GridSize == MyCubeSize.Large, "large grid cube: m a^2 / 6 with a = 2.5 m");
        }

        static void TestThrusterBasics()
        {
            var w = NewWorld();
            // thruster (Forward=Backward) at z=0..1 pushes the ship toward -Z; a battery and armor around it, all on the COM line
            var b = Spawn(w, 0, 0, Place(new Vector3D(0, 0, 0)), false,
                Thrust(0, 0, 0, "Backward"), Battery(0, 0, 2), Armor(0, 0, 3), Armor(0, 0, 4));
            b.DampenersOn = false;
            double m = b.Mass;
            Check(Rel(m, 121 + 146.4 + 20 + 20, 1e-6), "mass of thruster+battery+2 armor from defs_stats.json");
            var t = b.Thrusters[0];
            Check(Near(t.ForceDirLocal, Vector3D.Forward), "thruster with BlockOrientation Forward=Backward: ForceDirLocal = ship Forward (-Z)");
            t.OverrideN = t.MaxForce;
            Run(w, 60);
            double expectV = t.MaxForce / m * 1.0;
            // the thruster is off the COM axis in Y/X? it is on the line x=y=0 so no torque from it; COM x,y = 0
            Check(Near(b.LinearVelocity, new Vector3D(0, 0, -expectV), expectV * 1e-4), "acceleration = F/m: 1 s at 100% gives v = F/m (" + (-b.LinearVelocity.Z).ToString("F4") + " vs " + expectV.ToString("F4") + ")");
            Check(Math.Abs(b.AngularVelocity.Length()) < 1e-9, "thrust through the COM produces no rotation");
            Check(Rel(t.CurrentForce, t.MaxForce, 1e-6), "CurrentForce = MaxForce with full power");
            Check(Math.Abs(b.Position.Z - (-0.5 * expectV * 1.0)) < expectV * 0.02, "position after 1 s = a t^2 / 2 within 2% (semi-implicit Euler)");
            // half override
            var w2 = NewWorld();
            var b2 = Spawn(w2, 0, 0, Place(Vector3D.Zero), false, Thrust(0, 0, 0, "Backward"), Battery(0, 0, 2), Armor(0, 0, 3), Armor(0, 0, 4));
            b2.DampenersOn = false; b2.Thrusters[0].OverrideN = b2.Thrusters[0].MaxForce * 0.5f;
            Run(w2, 120);
            Check(Rel(-b2.LinearVelocity.Z, 0.5 * 14400 / b2.Mass * 2.0, 1e-4), "50% override over 2 s gives half the force");
            // override above max is clamped; disabled thruster does nothing
            var w3 = NewWorld();
            var b3 = Spawn(w3, 0, 0, Place(Vector3D.Zero), false, Thrust(0, 0, 0, "Backward"), Battery(0, 0, 2), Armor(0, 0, 3));
            b3.DampenersOn = false; b3.Thrusters[0].OverrideN = 1e9f;
            Run(w3, 60);
            Check(Rel(-b3.LinearVelocity.Z, 14400 / b3.Mass, 1e-4), "override above MaxForce is clamped to MaxForce");
            b3.Thrusters[0].Enabled = false; double v0 = b3.LinearVelocity.Z; Run(w3, 30);
            Check(Math.Abs(b3.LinearVelocity.Z - v0) < 1e-9, "disabled thruster gives no force");
            // world rotation: ship yawed 90 degrees (Forward = Left, -X): thrust must follow
            var w4 = NewWorld();
            var b4 = Spawn(w4, 0, 0, Place(Vector3D.Zero, null, null, Vector3D.Left, Vector3D.Up), false, Thrust(0, 0, 0, "Backward"), Battery(0, 0, 2), Armor(0, 0, 3));
            b4.DampenersOn = false; b4.Thrusters[0].OverrideN = b4.Thrusters[0].MaxForce; Run(w4, 60);
            Check(b4.LinearVelocity.X < 0 && Math.Abs(b4.LinearVelocity.Z) < 1e-6 && Rel(b4.LinearVelocity.Length(), 14400 / b4.Mass, 1e-4), "thrust follows the ship orientation in the world");
        }

        static void TestThrusterOrientations()
        {
            // all six block Forward directions: exhaust = Forward, push = Backward
            string[] fwd = { "Forward", "Backward", "Left", "Right", "Up", "Down" };
            Vector3D[] push = { Vector3D.Backward, Vector3D.Forward, Vector3D.Right, Vector3D.Left, Vector3D.Down, Vector3D.Up };
            bool all = true;
            for (int i = 0; i < 6; i++)
            {
                string up = (fwd[i] == "Up" || fwd[i] == "Down") ? "Backward" : "Up";
                var w = NewWorld();
                var b = Spawn(w, 0, 0, Place(Vector3D.Zero), false, Thrust(0, 0, 0, fwd[i], up), Armor(5, 5, 5));
                all &= Near(b.Thrusters[0].ForceDirLocal, push[i]);
            }
            Check(all, "BlockOrientation.Forward = X pushes the ship toward the opposite of X (all 6 directions)");
        }

        static void TestTorqueFromOffset()
        {
            var w = NewWorld();
            w.Settings.ThrusterTorque = true;      // optional mode; the default applies thrust at the COM (checked below)
            // thruster 2 cells (1 m) to the +X side of a heavier armor block row, pushing -Z: torque = r x F
            var b = Spawn(w, 0, 0, Place(Vector3D.Zero), false,
                Thrust(4, 0, 0, "Backward"), HeavyArmor(0, 0, 0), HeavyArmor(1, 0, 0), HeavyArmor(2, 0, 0), HeavyArmor(3, 0, 0), Battery(0, 0, 3));
            b.DampenersOn = false;
            var t = b.Thrusters[0];
            t.OverrideN = t.MaxForce;
            Vector3D r = t.PosLocal - b.ComLocal;
            Vector3D F = t.ForceDirLocal * t.MaxForce;
            Vector3D tau = Vector3D.Cross(r, F);
            Vector3D expectW = b.ApplyInvInertiaWorld(tau) * SimSettings.Dt;
            w.Step();
            Check(Near(b.AngularVelocity, expectW, Math.Abs(expectW.Length()) * 1e-6 + 1e-12) && expectW.Length() > 0, "off-centre thruster torque = (r - COM) x F -> angular velocity after 1 tick");
            Check(tau.Y > 0, "thruster on +X pushing -Z gives +Y torque (right-handed)");
            Check(Near(b.LinearVelocity, F * (SimSettings.Dt / b.Mass), 1e-9), "linear acceleration unaffected by the lever arm (" + b.LinearVelocity + " vs " + F * (SimSettings.Dt / b.Mass) + ")");
            // default (game): thrust through the COM, no rotation from an off-centre thruster
            var w2 = NewWorld();
            var b2 = Spawn(w2, 0, 0, Place(Vector3D.Zero), false,
                Thrust(4, 0, 0, "Backward"), HeavyArmor(0, 0, 0), HeavyArmor(1, 0, 0), HeavyArmor(2, 0, 0), HeavyArmor(3, 0, 0), Battery(0, 0, 3));
            b2.DampenersOn = false;
            b2.Thrusters[0].OverrideN = b2.Thrusters[0].MaxForce;
            for (int i = 0; i < 60; i++) w2.Step();
            Check(b2.AngularVelocity.Length() < 1e-12 && b2.LinearVelocity.Length() > 0, "default ThrusterTorque=false: off-centre thruster accelerates without turning (w=" + b2.AngularVelocity.Length() + ")");
        }

        static void TestGyro()
        {
            // --- sign convention + convergence: small ship with a gyro, battery, armor
            var w = NewWorld();
            var b = Spawn(w, 0, 0, Place(Vector3D.Zero), false, Gyro(0, 0, 0), Battery(1, 0, 0), Armor(0, 1, 0), Armor(1, 1, 0), Armor(0, 0, 1), Armor(1, 0, 1));
            var g = b.Gyros[0];
            g.Override = true; g.Yaw = -1f;                         // default signs: target = +1 rad/s about local +Y
            Vector3D fwd1 = Vector3D.Zero;
            for (int i = 0; i < 300; i++) { b.GyroDirty = true; w.Step(); if (i == 59) fwd1 = b.RotMatrix.Forward; }
            Check(Math.Abs(b.AngularVelocity.Y - 1.0) < 1e-3 && Math.Abs(b.AngularVelocity.X) < 1e-3 && Math.Abs(b.AngularVelocity.Z) < 1e-3,
                  "gyro Yaw=-1 (default signs) -> angular velocity converges to +1 rad/s about +Y (" + b.AngularVelocity + ")");
            Check(fwd1.X < -0.1, "positive rate about Up turns the nose to the Left (-X), as right-handed rotation requires (forward after 1 s: " + fwd1 + ")");
            // switchable constant: flip the yaw sign and the same command turns the other way
            var s2 = new SimSettings(); s2.GyroYawSign = 1;
            var w2 = NewWorld(1, s2);
            var b2 = Spawn(w2, 0, 0, Place(Vector3D.Zero), false, Gyro(0, 0, 0), Battery(1, 0, 0), Armor(0, 1, 0));
            b2.Gyros[0].Override = true; b2.Gyros[0].Yaw = -1f;
            for (int i = 0; i < 300; i++) { b2.GyroDirty = true; w2.Step(); }
            Check(b2.AngularVelocity.Y < -0.9, "SimSettings.GyroYawSign is the single switch for the yaw sign");

            // --- the script law: desired world rate -> gyro local -> Pitch=-l.X, Yaw=-l.Y, Roll=-l.Z, arbitrary gyro mounting
            var w3 = NewWorld();
            var b3 = Spawn(w3, 0, 0, Place(Vector3D.Zero, null, null, Vector3D.Normalize(new Vector3D(0.3, 0.2, -1)), Vector3D.Normalize(Vector3D.Cross(Vector3D.Cross(new Vector3D(0.3, 0.2, -1), Vector3D.Up), new Vector3D(0.3, 0.2, -1)))), false,
                Bd("Gyro", "SmallBlockGyro", 0, 0, 0, "Down", "Forward"), Battery(1, 0, 0), Armor(0, 1, 0), Armor(1, 1, 0), Armor(0, 0, 1), Armor(1, 0, 1));
            var gg = b3.Gyros[0];
            Vector3D want = new Vector3D(0.4, -0.7, 0.5);             // desired world angular velocity
            gg.Override = true;
            for (int i = 0; i < 400; i++)
            {
                Vector3D wGrid = b3.DirToLocal(want);
                Vector3D l = Vector3D.TransformNormal(wGrid, MatrixD.Transpose(gg.RotToGrid));   // gyro-local, like the scripts
                gg.Pitch = -(float)l.X; gg.Yaw = -(float)l.Y; gg.Roll = -(float)l.Z; b3.GyroDirty = true;
                w3.Step();
            }
            Check(Near(b3.AngularVelocity, want, 2e-3), "script law (Pitch=-l.X,Yaw=-l.Y,Roll=-l.Z) with a rotated gyro and a rotated ship reaches the desired world rate");

            // --- torque limit: huge inertia, one tick: dw = MaxForce * invI * dt
            var cubes = new List<BlockData>();
            for (int x = 0; x < 10; x++) for (int y = 0; y < 10; y++) for (int z = 0; z < 10; z++) cubes.Add(HeavyArmor(x, y, z));
            cubes.Add(Gyro(10, 5, 5)); cubes.Add(Battery(10, 6, 5));
            var w4 = NewWorld();
            var b4 = Spawn(w4, 0, 0, Place(Vector3D.Zero), false, cubes.ToArray());
            b4.Gyros[0].Override = true; b4.Gyros[0].Yaw = -3f; b4.GyroDirty = true;
            w4.Step();
            Vector3D tq = new Vector3D(0, 448000, 0);
            Vector3D expect = b4.ApplyInvInertiaWorld(tq) * SimSettings.Dt;
            Check(Near(b4.AngularVelocity, expect, expect.Length() * 0.02), "gyro torque is limited to MaxForce (448 kN m): dw = tau/I dt (" + b4.AngularVelocity.Y.ToString("E3") + " vs " + expect.Y.ToString("E3") + ")");
            Check(Rel(b4.Gyros[0].MaxForce, 448000, 1e-9), "gyro MaxForce from defs_stats.json");

            // --- angular speed cap 2 pi rad/s (small) and pi (large)
            var w5 = NewWorld();
            var b5 = Spawn(w5, 0, 0, Place(Vector3D.Zero), false, Gyro(0, 0, 0), Battery(1, 0, 0), Armor(0, 1, 0));
            b5.Gyros[0].Override = true; b5.Gyros[0].Yaw = -100f;
            for (int i = 0; i < 600; i++) { b5.GyroDirty = true; w5.Step(); }
            Check(Math.Abs(b5.AngularVelocity.Length() - 2 * Math.PI) < 1e-6, "angular speed capped at 2 pi rad/s (360 deg/s) on small grids");
            var w6 = NewWorld();
            var b6 = Spawn(w6, 0, 0, Place(Vector3D.Zero), true, Bd("Gyro", "LargeBlockGyro", 0, 0, 0), Bd("BatteryBlock", "LargeBlockBatteryBlock", 1, 0, 0));
            b6.Gyros[0].Override = true; b6.Gyros[0].Yaw = -100f;
            for (int i = 0; i < 1200; i++) { b6.GyroDirty = true; w6.Step(); }
            Check(Math.Abs(b6.AngularVelocity.Length() - Math.PI) < 1e-6, "angular speed capped at pi rad/s (180 deg/s) on large grids");

            // --- idle gyros damp rotation (MAX_SLOWDOWN)
            var w7 = NewWorld();
            var b7 = Spawn(w7, 0, 0, Place(Vector3D.Zero, null, new Vector3D(0, 1, 0)), false, Gyro(0, 0, 0), Battery(1, 0, 0), Armor(0, 1, 0), Armor(1, 1, 0));
            Run(w7, 600);
            Check(b7.AngularVelocity.Length() < 1e-3, "without override the gyros slow the rotation down (idle damping)");
            // --- a ship without gyros keeps spinning
            var w8 = NewWorld();
            var b8 = Spawn(w8, 0, 0, Place(Vector3D.Zero, null, new Vector3D(0, 1, 0)), false, Armor(0, 0, 0), Armor(1, 0, 0));
            Run(w8, 600);
            Check(Near(b8.AngularVelocity, new Vector3D(0, 1, 0), 1e-9), "without gyros the angular velocity is constant");
            // --- gyro Power scales authority; Power=0 override gives no torque
            var w9 = NewWorld();
            var b9 = Spawn(w9, 0, 0, Place(Vector3D.Zero), false, Gyro(0, 0, 0), Battery(1, 0, 0), Armor(0, 1, 0));
            b9.Gyros[0].Override = true; b9.Gyros[0].Yaw = -1f; b9.Gyros[0].Power = 0f;
            for (int i = 0; i < 60; i++) { b9.GyroDirty = true; w9.Step(); }
            Check(b9.AngularVelocity.Length() < 1e-9, "GyroPower = 0: override gives no torque");
        }

        static void TestLimits()
        {
            var s = new SimSettings();
            var w = NewWorld(1, s);
            var b = Spawn(w, 0, 0, Place(Vector3D.Zero, new Vector3D(0, 0, -99.9)), false, Thrust(0, 0, 0, "Backward"), Battery(0, 0, 2), Armor(0, 0, 3));
            b.DampenersOn = false;
            var t = b.Thrusters[0]; t.MaxForce = 1e9f; t.MaxPowerMW = 0.2f; t.OverrideN = 1e9f;
            Run(w, 120);
            Check(Math.Abs(b.LinearVelocity.Length() - 100.0) < 1e-9, "max speed clamp: |v| = 100 m/s (SmallShipMaxSpeed)");
            // large grid clamp as well and diagonal
            var w2 = NewWorld();
            var b2 = Spawn(w2, 0, 0, Place(Vector3D.Zero, new Vector3D(80, 80, 80)), false, Armor(0, 0, 0));
            Run(w2, 5);
            Check(Math.Abs(b2.LinearVelocity.Length() - 100.0) < 1e-9, "initial velocity above the cap is clamped on the first tick");
        }

        static void TestConservation()
        {
            // symmetric body (3x3x3 cube): free flight keeps v and omega; orientation advances by |w| t about the axis
            var cube = new List<BlockData>();
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) for (int z = 0; z < 3; z++) cube.Add(Armor(x, y, z));
            var w = NewWorld();
            Vector3D v0 = new Vector3D(3, -2, 1), w0 = new Vector3D(0.5, 1.0, -0.7);
            var b = Spawn(w, 0, 0, Place(new Vector3D(10, 20, 30), v0, w0), false, cube.ToArray());
            Vector3D x0 = b.Position; Vector3D L0 = b.AngularMomentumWorld(); double ke0 = 0.5 * b.Mass * v0.LengthSquared();
            Run(w, 600);
            double T = 10.0;
            Check(Near(b.LinearVelocity, v0, 1e-9) && Near(b.AngularVelocity, w0, 1e-9), "free flight: v and omega constant");
            Check(Near(b.Position, x0 + v0 * T, 1e-6), "free flight: position = x0 + v t");
            Check(Near(b.AngularMomentumWorld(), L0, 1e-9), "free flight: angular momentum constant");
            double ang = w0.Length() * T;
            var expectQ = QuaternionD.CreateFromAxisAngle(w0 / w0.Length(), ang);
            Vector3D fExpect = Vector3D.Transform(Vector3D.Forward, expectQ);
            Check(Near(b.RotMatrix.Forward, fExpect, 1e-6), "free flight: orientation advances by |w| t about the axis (exact exponential)");
            // gyroscopic term keeps |L| for an asymmetric body (opt in)
            var s = new SimSettings(); s.GyroscopicTerm = true;
            var w2 = NewWorld(1, s);
            var b2 = Spawn(w2, 0, 0, Place(Vector3D.Zero, null, new Vector3D(0.3, 2.0, 0.4)), false, Armor(0, 0, 0), Armor(1, 0, 0), Armor(2, 0, 0), Armor(3, 0, 0), Armor(3, 1, 0), Armor(0, 0, 1));
            double Lm0 = b2.AngularMomentumWorld().Length();
            Run(w2, 600);
            Check(Math.Abs(b2.AngularMomentumWorld().Length() - Lm0) / Lm0 < 0.01, "GyroscopicTerm on: |L| of a tumbling asymmetric body is conserved within 1% (drift " + ((b2.AngularMomentumWorld().Length() - Lm0) / Lm0).ToString("P3") + ")");
        }

        static void TestPower()
        {
            // battery with 1.8 s of energy at 0.2 MW: E = 0.2 MW * 1.8 s / 3600 = 1e-4 MWh
            var w = NewWorld();
            var b = Spawn(w, 0, 0, Place(Vector3D.Zero), false, Thrust(0, 0, 0, "Backward"), Battery(0, 0, 2, 1e-4), Armor(0, 0, 3));
            b.DampenersOn = false; b.Thrusters[0].OverrideN = b.Thrusters[0].MaxForce;
            Run(w, 60);
            double used = 1e-4 - b.Batteries[0].StoredMWh;
            Check(Rel(used, 0.2 * 1.0 / 3600.0, 2e-3), "battery drain = thruster power * time (" + used.ToString("E4") + " vs " + (0.2 / 3600).ToString("E4") + " MWh)");
            Run(w, 90);
            double vEmpty = b.LinearVelocity.Z;
            Check(b.Batteries[0].StoredMWh == 0 && b.Thrusters[0].CurrentForce == 0, "empty battery: stored 0 and no thrust");
            Run(w, 60);
            Check(Math.Abs(b.LinearVelocity.Z - vEmpty) < 1e-9, "empty battery: velocity no longer changes");
            // thrust time: ~1.8 s of full thrust before cut-off
            double tCut = -vEmpty * b.Mass / 14400.0;
            Check(Math.Abs(tCut - 1.8) < 0.05, "full thrust lasts about 1.8 s on 1e-4 MWh (" + tCut.ToString("F3") + " s)");
            // no battery at all: no thrust
            var w2 = NewWorld();
            var b2 = Spawn(w2, 0, 0, Place(Vector3D.Zero), false, Thrust(0, 0, 0, "Backward"), Armor(0, 0, 3));
            b2.DampenersOn = false; b2.Thrusters[0].OverrideN = b2.Thrusters[0].MaxForce; Run(w2, 60);
            Check(b2.LinearVelocity.Length() < 1e-12, "no power source: thrusters do nothing");
            // partial supply: 4 thrusters (0.8 MW) on one small battery (0.2 MW max out): Thrust group is adaptible -> ratio 0.25
            var w3 = NewWorld();
            var b3 = Spawn(w3, 0, 0, Place(Vector3D.Zero), false,
                Thrust(0, 0, 0, "Backward"), Thrust(2, 0, 0, "Backward"), Thrust(4, 0, 0, "Backward"), Thrust(6, 0, 0, "Backward"), Battery(1, 1, 0));
            b3.DampenersOn = false;
            foreach (var t in b3.Thrusters) t.OverrideN = t.MaxForce;
            w3.Step();
            double f = 0; foreach (var t in b3.Thrusters) f += t.CurrentForce;
            Check(Rel(f, 4 * 14400 * 0.25, 0.01), "power-limited thrust: adaptible group gets the ratio supply/demand (" + f.ToString("F0") + " N of 57600 N)");
            // gyro group has priority over thrust: gyro keeps full power even with starving thrusters
            var w4 = NewWorld();
            var b4 = Spawn(w4, 0, 0, Place(Vector3D.Zero), false, Gyro(0, 0, 0), Thrust(2, 0, 0, "Backward"), Thrust(4, 0, 0, "Backward"), Thrust(6, 0, 0, "Backward"), Thrust(8, 0, 0, "Backward"), Battery(1, 1, 0));
            foreach (var t in b4.Thrusters) t.OverrideN = t.MaxForce;
            w4.Step();
            Check(b4.Gyros[0].Block.Sink.SuppliedRatio == 1.0 && b4.Thrusters[0].Block.Sink.SuppliedRatio < 0.3, "gyro (priority 70) is served before thrust (80)");
            // PowerEnabled = false
            var s = new SimSettings(); s.PowerEnabled = false;
            var w5 = NewWorld(1, s);
            var b5 = Spawn(w5, 0, 0, Place(Vector3D.Zero), false, Thrust(0, 0, 0, "Backward"), HeavyArmor(0, 0, 3), HeavyArmor(0, 0, 4), HeavyArmor(0, 0, 5), HeavyArmor(0, 0, 6));
            b5.DampenersOn = false; b5.Thrusters[0].OverrideN = b5.Thrusters[0].MaxForce; Run(w5, 60);
            Check(Rel(-b5.LinearVelocity.Z, 14400 / b5.Mass, 1e-4), "PowerEnabled=false: thrusters work without batteries");
        }

        static void TestDampeners()
        {
            var w = NewWorld();
            // thrusters pushing +Z (default orientation Forward=Forward) and -Z, ship moving -Z at 10 m/s: the +Z pair brakes
            var b = Spawn(w, 0, 0, Place(Vector3D.Zero, new Vector3D(0, 0, -10)), false,
                Thrust(-2, 0, 0, "Forward"), Thrust(2, 0, 0, "Forward"), Thrust(-2, 0, 4, "Backward"), Thrust(2, 0, 4, "Backward"),
                Battery(-1, 0, 3), Battery(1, 0, 3), Armor(0, 0, 3));
            b.DampenersOn = true;
            Run(w, 600);
            Check(b.LinearVelocity.Length() < 0.05, "inertial dampeners bring the ship to rest (" + b.LinearVelocity.Length().ToString("E2") + " m/s)");
            double fmax = 0; foreach (var t in b.Thrusters) fmax = Math.Max(fmax, t.CurrentForce);
            Check(fmax <= 14400 + 1e-3, "dampener thrust never exceeds MaxForce");
            var w2 = NewWorld();
            var b2 = Spawn(w2, 0, 0, Place(Vector3D.Zero, new Vector3D(0, 0, -10)), false, Thrust(0, 0, 0, "Forward"), Battery(0, 1, 0), Armor(1, 0, 3));
            b2.DampenersOn = false; Run(w2, 120);
            Check(Math.Abs(b2.LinearVelocity.Z + 10) < 1e-9, "dampeners off: velocity kept");
            var w3 = NewWorld();
            var b3 = Spawn(w3, 0, 0, Place(Vector3D.Zero, new Vector3D(0, 0, -10)), false, Thrust(0, 0, 0, "Forward"), Thrust(0, 2, 0, "Backward"), Battery(0, 1, 0), Armor(1, 0, 3));
            b3.DampenersOn = true; b3.Thrusters[0].OverrideN = 1;      // overridden thruster is excluded from the dampeners
            Run(w3, 30);
            Check(Math.Abs(b3.LinearVelocity.Z + 10) < 0.05, "a thruster with override does not dampen");
        }

        static void TestSplit()
        {
            // a bar of 12 blocks along X, one thruster+gyro; spinning and moving; cut in the middle
            var blocks = new List<BlockData>();
            for (int x = 0; x < 12; x++) blocks.Add(Armor(x, 0, 0));
            var w = NewWorld();
            var b = Spawn(w, 0, 7, Place(new Vector3D(5, 6, 7), new Vector3D(1, 2, 3), new Vector3D(0.2, 0.9, -0.3)), false, blocks.ToArray());
            Vector3D P0 = TotalMomentum(w), L0 = TotalAngularMomentum(w);
            // world position of block 3 and 9 before
            Vector3D p3 = b.Blocks[3].WorldCenter, p9 = b.Blocks[9].WorldCenter;
            Vector3D v3 = b.VelocityAtWorld(p3), v9 = b.VelocityAtWorld(p9);
            b.RemoveBlock(b.Blocks[6], 1);
            w.ApplyRemovals();
            Check(w.Bodies.Count == 2, "removing the middle block splits the grid in two bodies");
            Check(w.Bodies[0].Blocks.Count == 6 && w.Bodies[1].Blocks.Count == 5, "parts have 6 and 5 blocks");
            Check(w.Bodies[1].ShipIndex == 7 && w.Bodies[1].Team == b.Team && w.Bodies[1].EntityId != w.Bodies[0].EntityId, "debris keeps ShipIndex/Team and gets a new EntityId");
            Check(Near(w.Bodies[0].Blocks[3].WorldCenter, p3, 1e-9) && Near(w.Bodies[1].Blocks[2].WorldCenter, p9, 1e-9), "block positions do not jump on a split");
            Check(Near(w.Bodies[0].VelocityAtWorld(p3), v3, 1e-9) && Near(w.Bodies[1].VelocityAtWorld(p9), v9, 1e-9), "every surviving block keeps its velocity through the split");
            // momentum conservation including the removed block: m_rem * v_rem
            var wc = NewWorld();
            var bc = Spawn(wc, 0, 7, Place(new Vector3D(5, 6, 7), new Vector3D(1, 2, 3), new Vector3D(0.2, 0.9, -0.3)), false, blocks.ToArray());
            Vector3D remPos = bc.Blocks[6].WorldCenter; Vector3D remVel = bc.VelocityAtWorld(remPos);
            Vector3D Pafter = TotalMomentum(w) + remVel * 20.0;
            Vector3D Lafter = TotalAngularMomentum(w) + Vector3D.Cross(remPos, remVel * 20.0) + Vector3D.Zero;
            // the removed block also carries its own spin inertia about its centre
            Vector3D Lself = Vector3D.TransformNormal(Vector3D.TransformNormal(bc.DirToLocal(bc.AngularVelocity), new MatrixD(20.0 / 6 * 0.25, 0, 0, 0, 0, 20.0 / 6 * 0.25, 0, 0, 0, 0, 20.0 / 6 * 0.25, 0, 0, 0, 0, 1)), bc.RotMatrix);
            Check(Near(Pafter, P0, P0.Length() * 1e-9), "linear momentum conserved by the split (parts + removed block)");
            Check(Near(Lafter + Lself, L0, L0.Length() * 1e-6 + 1e-6), "angular momentum conserved by the split (parts + removed block, incl. its own spin)");
            // no splitting setting: small part is deleted
            var s = new SimSettings(); s.GridSplitting = false;
            var w2 = NewWorld(1, s);
            var b2 = Spawn(w2, 0, 0, Place(Vector3D.Zero), false, blocks.ToArray());
            b2.RemoveBlock(b2.Blocks[3], 0); w2.ApplyRemovals();
            Check(w2.Bodies.Count == 1 && w2.Bodies[0].Blocks.Count == 8, "GridSplitting=false: the lighter part is deleted, the heavier kept");
            // merge pair edge: merge blocks glued face to face are one structure only while locked
            var w3 = NewWorld();
            var m1 = Bd("MergeBlock", "SmallShipSmallMergeBlock", 0, 0, 0);                // Up = +Y
            var m2 = Bd("MergeBlock", "SmallShipSmallMergeBlock", 0, 1, 0, "Forward", "Down"); // Up = -Y
            var b3 = Spawn(w3, 0, 0, Place(Vector3D.Zero, new Vector3D(0, 0, -5)), false, Armor(0, -1, 0), m1, m2, Armor(0, 2, 0));
            Check(b3.Blocks[1].Merge.Partner == b3.Blocks[2].Merge && b3.Blocks[1].Merge.IsConnected, "merge blocks with touching Up faces are paired and connected");
            b3.Blocks[1].Merge.Partner = null; b3.Blocks[2].Merge.Partner = null; b3.ConnectivityDirty = true;
            w3.ApplyRemovals();
            Check(w3.Bodies.Count == 2 && Near(w3.Bodies[1].LinearVelocity, new Vector3D(0, 0, -5), 1e-9), "releasing the merge pair splits the bodies, both keep the velocity");
        }

        static void TestRemoveBlockKinematics()
        {
            // heavy block removed from one end: the COM moves, the rest keeps its world positions and velocities
            var w = NewWorld();
            var b = Spawn(w, 0, 0, Place(Vector3D.Zero, new Vector3D(0, 0, -3), new Vector3D(0, 0.8, 0)), false, HeavyArmor(0, 0, 0), Armor(1, 0, 0), Armor(2, 0, 0), Armor(3, 0, 0));
            Vector3D pRest = b.Blocks[3].WorldCenter; Vector3D vRest = b.VelocityAtWorld(pRest);
            double massBefore = b.Mass;
            b.RemoveBlock(b.Blocks[0], 0); w.ApplyRemovals();
            Check(Rel(b.Mass, massBefore - 112, 1e-12) && Near(b.Blocks[2].WorldCenter, pRest, 1e-9) && Near(b.VelocityAtWorld(pRest), vRest, 1e-9),
                  "RemoveBlock + mass update: mass drops, COM moves, surviving blocks keep position and velocity");
            Check(b.Blocks[0].Index == 0 && b.Blocks[2].Index == 2 && b.BlockAtCell(new Vector3I(3, 0, 0)) == b.Blocks[2] && b.BlockAtCell(new Vector3I(0, 0, 0)) == null, "cells re-indexed after removal");
            while (b.Blocks.Count > 0) b.RemoveBlock(b.Blocks[0], 0);
            w.ApplyRemovals();
            Check(w.Bodies.Count == 0, "a body without blocks leaves the world");
        }


        // random block losses on a spinning, moving Hornet: invariants after every split
        static void TestRemovalFuzz()
        {
            string bpDir = Path.Combine(Root(), Path.Combine("out", "Blueprints"));
            if (!Directory.Exists(Path.Combine(bpDir, "Hornet"))) return;
            var bp = BlueprintLoader.Get(bpDir, "Hornet");
            int badInv = 0; string firstBad = null; double worstP = 0, worstL = 0, worstM = 0; int splits = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var w = NewWorld((ulong)seed);
                var rng = new DetRng((ulong)seed * 7919);
                var main = GridFactory.Spawn(w, bp, 0, 0, Place(new Vector3D(5, -3, 8), new Vector3D(7, -2, 4), new Vector3D(0.4, 1.1, -0.6), Vector3D.Normalize(new Vector3D(1, 0.2, -0.3)), Vector3D.Up), null)[0];
                double m0 = main.Mass; Vector3D P0 = TotalMomentum(w), L0 = TotalAngularMomentum(w);
                Vector3D Prem = Vector3D.Zero, Lrem = Vector3D.Zero; double Mrem = 0;
                for (int step = 0; step < 45 && w.Bodies.Count > 0; step++)
                {
                    var body = w.Bodies[rng.NextInt(w.Bodies.Count)];
                    var blk = body.Blocks[rng.NextInt(body.Blocks.Count)];
                    Vector3D pos = body.LocalToWorld(blk.LocalCenter), vel = body.VelocityAtWorld(pos);
                    Vector3D wl = body.DirToLocal(body.AngularVelocity);
                    double sx = blk.Mass / 3.0 * (blk.HalfExtents.Y * blk.HalfExtents.Y + blk.HalfExtents.Z * blk.HalfExtents.Z), sy = blk.Mass / 3.0 * (blk.HalfExtents.X * blk.HalfExtents.X + blk.HalfExtents.Z * blk.HalfExtents.Z), sz = blk.Mass / 3.0 * (blk.HalfExtents.X * blk.HalfExtents.X + blk.HalfExtents.Y * blk.HalfExtents.Y);
                    Vector3D spin = body.DirToWorld(new Vector3D(wl.X * sx, wl.Y * sy, wl.Z * sz));
                    Prem += vel * blk.Mass; Lrem += Vector3D.Cross(pos, vel * blk.Mass) + spin; Mrem += blk.Mass;
                    int nb = w.Bodies.Count;
                    body.RemoveBlock(blk, 1);
                    w.ApplyRemovals();
                    if (w.Bodies.Count > nb) splits++;
                    var comps = new List<List<BlockInst>>();
                    foreach (var b in w.Bodies)
                    {
                        bool ok = b.Alive && b.Blocks.Count > 0 && Connectivity.Components(b, comps) == 1;
                        for (int i = 0; ok && i < b.Blocks.Count; i++)
                        {
                            var x = b.Blocks[i];
                            ok = x.Index == i && x.Body == b && !x.Destroyed && b.BlockAtCell(x.Min) == x && b.BlockAtCell(x.Max) == x;
                        }
                        if (ok)
                        {
                            double ms = 0; foreach (var x in b.Blocks) ms += x.Mass;
                            ok = Math.Abs(ms - b.Mass) < 1e-6 && b.Position.IsValid() && b.InvMass > 0;
                            foreach (var x in b.Blocks) if ((x.LocalCenter - b.BoundCenterLocal).Length() + x.Radius > b.BoundRadius + 1e-9) ok = false;
                        }
                        if (!ok) { badInv++; if (firstBad == null) firstBad = "seed " + seed + " step " + step + " body " + b; }
                    }
                    double msum = Mrem; foreach (var b in w.Bodies) msum += b.Mass;
                    worstM = Math.Max(worstM, Math.Abs(msum - m0));
                    worstP = Math.Max(worstP, (TotalMomentum(w) + Prem - P0).Length() / (m0 * 10));
                    worstL = Math.Max(worstL, (TotalAngularMomentum(w) + Lrem - L0).Length() / Math.Max(1.0, L0.Length()));
                }
            }
            Check(badInv == 0, "removal fuzz: cells/indices/mass/connectivity invariants hold after every split (" + splits + " splits)" + (firstBad != null ? " first failure: " + firstBad : ""));
            Check(worstM < 1e-6 && worstP < 1e-9 && worstL < 1e-6, "removal fuzz: mass, linear and angular momentum conserved (worst rel errors M " + worstM.ToString("E1") + ", P " + worstP.ToString("E1") + ", L " + worstL.ToString("E1") + ")");
        }

        static void TestRaycast()
        {
            var w = NewWorld();
            // row of 3 blocks along -Z at z = 0,-1,-2 (cell size 0.5)
            var b = Spawn(w, 0, 0, Place(new Vector3D(0, 0, -10)), false, Armor(0, 0, 0), Armor(0, 0, -1), Armor(0, 0, -2));
            RayHit h;
            // from the front (-Z side) looking toward +Z: hits z=-2 block first, its face at z = -10 - 1.0 - 0.25
            bool hit = Raycast.RayBody(b, new Vector3D(0, 0, -20), Vector3D.Backward, 100, out h);
            Check(hit && h.Block == b.BlockAtCell(new Vector3I(0, 0, -2)) && Math.Abs(h.Distance - (20 - 10 - 1.0 - 0.25)) < 1e-9 && Near(h.NormalWorld, Vector3D.Forward) && Math.Abs(h.Point.Z - (-11.25)) < 1e-9,
                  "ray hits the nearest block, exact distance and face normal (d = " + h.Distance.ToString("F4") + ")");
            hit = Raycast.RayBody(b, new Vector3D(0, 0, 0), Vector3D.Forward, 100, out h);
            Check(hit && h.Block == b.BlockAtCell(new Vector3I(0, 0, 0)) && Math.Abs(h.Distance - (10 - 0.25)) < 1e-9 && Near(h.NormalWorld, Vector3D.Backward), "ray from behind hits the first block from the other side");
            Check(!Raycast.RayBody(b, new Vector3D(0, 0, -20), Vector3D.Backward, 5, out h), "maxDist shorter than the distance: no hit");
            Check(!Raycast.RayBody(b, new Vector3D(1, 0, -20), Vector3D.Backward, 100, out h), "ray passing beside the grid: no hit");
            hit = Raycast.RayBody(b, new Vector3D(0.1, 0.1, -10), Vector3D.Right, 100, out h);
            Check(hit && h.Distance == 0 || (hit && h.Distance < 0.2), "ray starting inside a block hits at distance ~0");
            var buf = new RayHit[8];
            int n = Raycast.RayWorldAll(w, new Vector3D(0, 0, -20), Vector3D.Backward, 100, null, buf);
            Check(n == 3 && buf[0].Distance < buf[1].Distance && buf[1].Distance < buf[2].Distance && buf[0].Block.Min.Z == -2 && buf[2].Block.Min.Z == 0, "RayWorldAll: all 3 blocks, sorted by distance");
            Check(Raycast.RayWorld(w, new Vector3D(0, 0, -20), Vector3D.Backward, 100, b, out h) == false, "RayWorld ignores the 'ignore' body");

            RaycastBrute(false);
            RaycastBrute(true);
            // performance
            var wp = NewWorld();
            var pblocks = new List<BlockData>();
            for (int x = 0; x < 7; x++) for (int y = 0; y < 6; y++) for (int z = 0; z < 8; z++) if ((x + y * 3 + z * 5) % 3 != 0) pblocks.Add(Armor(x - 3, y - 3, z - 4));
            var rb = Spawn(wp, 0, 0, Place(new Vector3D(10, -4, 7)), false, pblocks.ToArray());
            var sw = Stopwatch.StartNew();
            RayHit tmpH; int hh2 = 0;
            for (int i = 0; i < 200000; i++) { if (Raycast.RayBody(rb, new Vector3D(10 + (i % 7) * 0.3, -4, -20), Vector3D.Backward, 100, out tmpH)) hh2++; }
            Info("RayBody: " + (sw.Elapsed.TotalMilliseconds * 1000 / 200000).ToString("F2") + " us per ray (" + hh2 + " hits)");
        }


        static void RaycastBrute(bool large)
        {
            string kind = large ? "large grid" : "small grid";
            var rng = new DetRng(large ? 777UL : 12345UL);
            var blocks = new List<BlockData>(); var used = new HashSet<long>();
            for (int i = 0; i < 160; i++)
            {
                int x = rng.NextInt(7), y = rng.NextInt(6), z = rng.NextInt(8);
                if (!used.Add(x + 100L * y + 10000L * z)) continue;
                blocks.Add(large ? Bd("CubeBlock", "LargeBlockArmorBlock", x - 3, y - 3, z - 4) : Armor(x - 3, y - 3, z - 4));
            }
            double sc = large ? 5.0 : 1.0;
            var w2 = NewWorld();
            Vector3D ax = Vector3D.Normalize(new Vector3D(0.3, 1.0, -0.4));
            var q = QuaternionD.CreateFromAxisAngle(ax, 0.9);
            Vector3D fwd = Vector3D.Transform(Vector3D.Forward, q), upv = Vector3D.Transform(Vector3D.Up, q);
            var rb = Spawn(w2, 0, 0, Place(new Vector3D(10, -4, 7), null, null, fwd, upv), large, blocks.ToArray());
            int mismatch = 0, hits = 0, allMismatch = 0;
            var buf2 = new RayHit[64];
            for (int i = 0; i < 4000; i++)
            {
                Vector3D o = new Vector3D(rng.Range(-6, 6) * sc + 10, rng.Range(-6, 6) * sc - 4, rng.Range(-6, 6) * sc + 7);
                Vector3D target = rb.Position + new Vector3D(rng.Range(-1.5, 1.5), rng.Range(-1.5, 1.5), rng.Range(-1.5, 1.5)) * sc;
                Vector3D d = Vector3D.Normalize(i % 4 == 0 ? new Vector3D(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1)) : target - o);
                double maxD = rng.Range(1, 30) * sc;
                RayHit fast; bool fh = Raycast.RayBody(rb, o, d, maxD, out fast);
                Vector3D ol = rb.WorldToLocal(o), dl = rb.DirToLocal(d);
                double bestT = double.MaxValue; BlockInst bestB = null; int cnt = 0;
                foreach (var blk in rb.Blocks)
                {
                    double t0 = double.NegativeInfinity, t1 = double.PositiveInfinity; bool miss = false;
                    for (int a = 0; a < 3; a++)
                    {
                        double oo = a == 0 ? ol.X : a == 1 ? ol.Y : ol.Z, dd = a == 0 ? dl.X : a == 1 ? dl.Y : dl.Z;
                        double c = a == 0 ? blk.LocalCenter.X : a == 1 ? blk.LocalCenter.Y : blk.LocalCenter.Z, hh = a == 0 ? blk.HalfExtents.X : a == 1 ? blk.HalfExtents.Y : blk.HalfExtents.Z;
                        if (Math.Abs(dd) < 1e-15) { if (oo < c - hh || oo > c + hh) { miss = true; break; } continue; }
                        double ta = (c - hh - oo) / dd, tb = (c + hh - oo) / dd; if (ta > tb) { double tmp = ta; ta = tb; tb = tmp; }
                        if (ta > t0) t0 = ta; if (tb < t1) t1 = tb;
                    }
                    if (miss || t0 > t1 || t1 < 0 || t0 > maxD) continue;
                    cnt++;
                    double te = Math.Max(t0, 0);
                    if (te < bestT) { bestT = te; bestB = blk; }
                }
                bool bh = bestB != null;
                if (bh) hits++;
                if (fh != bh || (fh && (fast.Block != bestB || Math.Abs(fast.Distance - bestT) > 1e-6))) mismatch++;
                int na = Raycast.RayWorldAll(w2, o, d, maxD, null, buf2);
                if (na != cnt) allMismatch++;
            }
            Check(mismatch == 0 && hits > 500, "raycast (" + kind + ") agrees with brute-force slab tests on 4000 random rays against a rotated body (" + hits + " hits, " + mismatch + " mismatches)");
            Check(allMismatch == 0, "RayWorldAll (" + kind + ") returns exactly the blocks a brute-force test finds (" + allMismatch + " mismatching rays)");
        }

        static void TestCollision()
        {
            var cube = new List<BlockData>();
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) for (int z = 0; z < 3; z++) cube.Add(HeavyArmor(x, y, z));
            var w = NewWorld();
            var a = Spawn(w, 0, 0, Place(new Vector3D(0, 0, 0), new Vector3D(2, 0, 0)), false, cube.ToArray());
            var c = Spawn(w, 1, 1, Place(new Vector3D(6, 0, 0), new Vector3D(-2, 0, 0)), false, cube.ToArray());
            Vector3D p0 = TotalMomentum(w);
            Run(w, 300);
            double vrel = c.LinearVelocity.X - a.LinearVelocity.X;
            Check(vrel > 0 && Math.Abs(vrel - 0.2 * 4) < 0.25, "head-on collision: relative velocity reversed with restitution 0.2 (" + vrel.ToString("F3") + " vs 0.8)");
            Check(Near(TotalMomentum(w), p0, 1e-6 * a.Mass), "collision conserves linear momentum");
            Check(a.Position.X < c.Position.X - 1.4, "bodies do not interpenetrate after contact (gap " + (c.Position.X - a.Position.X).ToString("F3") + " m)");
            // same ship index does not collide
            var w2 = NewWorld();
            var a2 = Spawn(w2, 0, 5, Place(new Vector3D(0, 0, 0), new Vector3D(2, 0, 0)), false, cube.ToArray());
            var c2 = Spawn(w2, 0, 5, Place(new Vector3D(6, 0, 0), new Vector3D(-2, 0, 0)), false, cube.ToArray());
            Run(w2, 200);
            Check(a2.LinearVelocity.X > 1.9, "bodies of the same ShipIndex do not collide");
        }

        static void TestDeterminism()
        {
            Func<ulong> run = () =>
            {
                var w = NewWorld(99);
                var b = Spawn(w, 0, 0, Place(new Vector3D(1, 2, 3), new Vector3D(1, 0, 0)), false,
                    Gyro(0, 0, 0), Thrust(2, 0, 0, "Backward"), Thrust(0, 2, 0, "Left", "Up"), Battery(1, 1, 0), Armor(1, 0, 0), HeavyArmor(0, 0, 3));
                b.DampenersOn = false;
                for (int i = 0; i < 1200; i++)
                {
                    if (b.Thrusters.Count > 1)
                    {
                        b.Thrusters[0].OverrideN = 14400f * (float)(0.5 + 0.5 * Math.Sin(i * 0.01));
                        b.Thrusters[1].OverrideN = (i % 100) < 50 ? 14400f : 0;
                    }
                    if (b.Gyros.Count > 0) { b.Gyros[0].Override = true; b.Gyros[0].Yaw = (float)Math.Sin(i * 0.02); b.Gyros[0].Pitch = (float)Math.Cos(i * 0.013); }
                    b.GyroDirty = true;
                    w.Step();
                    if (i == 700) { b.RemoveBlock(b.Blocks[4], 0); }
                }
                return StateHash(w);
            };
            ulong h1 = run(), h2 = run();
            Check(h1 == h2 && h1 != 0, "determinism: identical runs give identical state hashes (" + h1.ToString("x16") + ")");
        }

        static void TestBlueprints()
        {
            string bpDir = Path.Combine(Root(), Path.Combine("out", "Blueprints"));
            var defs = Defs();
            Check(defs.Blocks.Count > 1000 && defs.PowerPriority["Thrust"] == 80 && defs.PowerPriority["Gyro"] == 70 && defs.PowerAdaptible["Thrust"] && !defs.PowerAdaptible["Gyro"],
                  "DefDb: " + defs.Blocks.Count + " blocks, power priorities and adaptible flags from constants");
            BlockDef thr = defs.Get("Thrust", "SmallBlockSmallThrust");
            Check(thr.ThrustForceN == 14400 && thr.MassKg == 121f && thr.SizeCells.Z == 2 && thr.PowerGroup == "Thrust", "DefDb: small ion thruster 14400 N, 121 kg, 1x1x2");
            BlockDef rg = defs.Get("SmallMissileLauncherReload", "SmallRailgun");
            Check(rg.Weapon != null && rg.Weapon.ShotDelayMs == 500 && rg.Weapon.ReloadTimeMs == 4000 && rg.Weapon.Ammo.Speed == 1000 && rg.Weapon.Ammo.MissileHealthPool == 8000 && rg.Weapon.Ammo.IsMissile,
                  "DefDb: small railgun weapon + slug ammo");
            BlockDef gat = defs.Get("SmallGatlingGun", "");
            Check(gat.Weapon != null && gat.Weapon.RateOfFireRpm == 700 && gat.Weapon.Ammo.MassDamage == 90 && !gat.Weapon.Ammo.IsMissile && gat.Weapon.MagazineCapacity == 140, "DefDb: gatling 700 rpm, 90 dmg/hit, magazine 140");
            Check(defs.Get("Projector", "SmallProjector") != null, "DefDb: MyObjectBuilder_ prefixed keys resolve (Projector)");
            if (!Directory.Exists(bpDir)) { SelfTestSkips.Note("out/Blueprints missing: blueprint tests skipped (python3 -I tools/build_all.py)"); return; }
            var sw = Stopwatch.StartNew();
            var bp = BlueprintLoader.Get(bpDir, "Hornet");
            Info("Hornet bp loaded in " + sw.ElapsedMilliseconds + " ms");
            Check(bp.Grids.Count == 1 && bp.Grids[0].Blocks.Count > 50, "Hornet blueprint: 1 grid, " + bp.Grids[0].Blocks.Count + " blocks");
            int pbs = 0; foreach (var blk in bp.Grids[0].Blocks) if (blk.TypeId == "MyProgrammableBlock") { pbs++; Check(!string.IsNullOrEmpty(blk.Program) && blk.Program.Contains("class") || blk.Program.Contains("void Main"), "Hornet PB program text loaded (" + blk.Program.Length + " chars)"); }
            var w = NewWorld(5);
            sw.Restart();
            var bodies = GridFactory.Spawn(w, bp, 0, 0, Place(new Vector3D(0, 0, 0)), null);
            var hb = bodies[0];
            Info("Hornet spawned in " + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms; mass " + hb.Mass.ToString("F1") + " kg, " + hb.Blocks.Count + " blocks, " + hb.Thrusters.Count + " thrusters, " + hb.Gyros.Count + " gyros, " + hb.Batteries.Count + " batteries");
            // MODELS.md: Hornet Full 4451 kg, 6.5 m/s2 in each of the six directions (2 thrusters per axis side)
            Check(Math.Abs(hb.Mass - 4451) < 1.0, "Hornet mass = 4451 kg (docs MODELS.md)");
            var sum = new double[6];
            foreach (var t in hb.Thrusters) sum[t.DirIndex] += t.MaxForce;
            bool accOk = true; string accs = "";
            for (int i = 0; i < 6; i++) { double a = sum[i] / hb.Mass; accs += a.ToString("F2") + " "; accOk &= Math.Abs(a - 6.5) < 0.1; }
            Check(accOk, "Hornet thrust-to-mass 6.5 m/s2 in all six directions (" + accs.Trim() + ")");
            // each Hornet thruster pushes opposite to its block Forward
            bool dirOk = true;
            foreach (var t in hb.Thrusters)
            {
                var r = OrientMath.Rotation(t.Block.Orientation);
                dirOk &= Near(t.ForceDirLocal, -r.Forward) && Near(t.ForceDirLocal, r.Backward);
            }
            Check(dirOk, "every Hornet thruster pushes along its block Backward (exhaust = block Forward)");
            // full thrust forward: 12 thrusters, battery limited
            double stored = hb.Power.StoredMWh();
            Check(hb.Batteries.Count == 4 && Rel(stored, 4 * 0.05, 1e-6), "Hornet batteries: 4 x 0.05 MWh from CurrentStoredPower");
            // Pb sink and camera/RC sinks exist
            int sinks = hb.Power.Sinks.Count;
            Check(sinks >= hb.Thrusters.Count + hb.Gyros.Count + 3, "Hornet power sinks: thrusters + gyro + utility blocks (" + sinks + ")");
            // acceleration test: all +Z-pushing thrusters at 100%
            hb.DampenersOn = false;
            foreach (var t in hb.Thrusters) if (t.DirIndex == 3) t.OverrideN = t.MaxForce;     // push -Y (down)
            Vector3D forceSum = Vector3D.Zero; foreach (var t in hb.Thrusters) if (t.DirIndex == 3) forceSum += t.ForceDirLocal * t.MaxForce;
            Run(w, 60);
            double vexp = forceSum.Length() / hb.Mass * 1.0;
            double power = 0; foreach (var t in hb.Thrusters) if (t.DirIndex == 3) power += t.MaxPowerMW;
            Info("Hornet 1 s full 'down' thrust: dv = " + hb.LinearVelocity.Length().ToString("F3") + " (ideal " + vexp.ToString("F3") + "), thrust demand " + power.ToString("F2") + " MW vs battery output cap 0.8 MW, omega " + hb.AngularVelocity.Length().ToString("E2"));
            Check(hb.LinearVelocity.Y < 0 && hb.LinearVelocity.Length() <= vexp * 1.0001 && hb.LinearVelocity.Length() > 0.5 * vexp, "Hornet accelerates along the thrust axis, limited only by battery output");

            // every blueprint loads and spawns (units, cells, mass) - catches unknown blocks and size/orientation problems
            sw.Restart();
            int count = 0; double totalMs = 0;
            foreach (var dir in Directory.GetDirectories(bpDir))
            {
                string name = Path.GetFileName(dir);
                var data = BlueprintLoader.Get(bpDir, name);
                var wx = NewWorld(7);
                var sw2 = Stopwatch.StartNew();
                var bl = GridFactory.Spawn(wx, data, 1, 0, Place(Vector3D.Zero), null);
                totalMs += sw2.Elapsed.TotalMilliseconds;
                // M0 merge fuse: grids whose merge blocks touch (Corsair Wing) spawn as ONE body, so bodies <= grids; the block total must match
                int gb = 0, bb = 0; foreach (var g in data.Grids) gb += g.Blocks.Count; foreach (var bo in bl) bb += bo.Blocks.Count;
                bool ok = bl.Count >= 1 && bl.Count <= data.Grids.Count && bl[0].Mass > 0 && gb == bb;
                // cells: every block occupies its cells
                foreach (var bo in bl) { foreach (var blk in bo.Blocks) { if (bo.BlockAtCell(blk.Min) != blk || bo.BlockAtCell(blk.Max) != blk) { ok = false; break; } } }
                if (!ok) Check(false, "blueprint '" + name + "' spawns consistently");
                count++;
            }
            Check(true, count + " blueprints load and spawn (spawn " + totalMs.ToString("F0") + " ms total, load+spawn " + sw.ElapsedMilliseconds + " ms)");
            // connectivity: the validated blueprints must be ONE connected structure under the face-adjacency rule (else the first hit shatters them)
            {
                var comps = new List<List<BlockInst>>();
                int multi = 0; string firstMulti = null;
                foreach (var dir in Directory.GetDirectories(bpDir))
                {
                    string name = Path.GetFileName(dir);
                    var data = BlueprintLoader.Get(bpDir, name);
                    var wx = NewWorld(7);
                    var bl = GridFactory.Spawn(wx, data, 1, 0, Place(Vector3D.Zero), null);
                    int nc = Connectivity.Components(bl[0], comps);
                    if (nc != 1) { multi++; if (firstMulti == null) firstMulti = name + " (" + nc + " components, sizes " + string.Join("/", comps.ConvertAll(l => l.Count.ToString())) + ")"; }
                }
                Check(multi == 0, "every blueprint is one face-connected structure at spawn" + (multi > 0 ? " - " + multi + " are not, first: " + firstMulti : ""));
            }

            // cross-check against the python tools: out/dist/MODELS.md (mass, block count and the six thrust/mass accelerations of every Full model)
            string modelsMd = Path.Combine(Root(), Path.Combine("out", Path.Combine("dist", "MODELS.md")));
            if (File.Exists(modelsMd))
            {
                int rows = 0, bad = 0; string firstBad = null;
                foreach (string line in File.ReadAllLines(modelsMd))
                {
                    if (!line.StartsWith("| ")) continue;
                    var c = line.Split('|');
                    if (c.Length < 9 || c[2].Trim() != "Full") continue;
                    string name = c[1].Trim(); int blocksExp; double massExp;
                    if (!int.TryParse(c[4].Trim(), out blocksExp) || !double.TryParse(c[5].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out massExp)) continue;
                    if (!Directory.Exists(Path.Combine(bpDir, name))) continue;
                    var data = BlueprintLoader.Get(bpDir, name);
                    var wx = NewWorld(11);
                    var spawned = GridFactory.Spawn(wx, data, 0, 0, Place(Vector3D.Zero), null);
                    if (spawned.Count < data.Grids.Count) continue;   // merge-fused Wing models: the sim body is ship + drones, MODELS.md lists the ship grid
                    var bl = spawned[0];
                    // accel column: "F6.5 B6.5 U6.5 D6.5 L6.5 R6.5"
                    string accel = c[7];
                    double[] exp = new double[6]; int ok = 0;
                    foreach (string tok in accel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        double v; if (tok.Length < 2 || !double.TryParse(tok.Substring(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) continue;
                        int idx = "FBUDLR".IndexOf(tok[0]); if (idx >= 0) { exp[idx] = v; ok++; }
                    }
                    var sums = new double[6];
                    foreach (var th in bl.Thrusters) sums[th.DirIndex] += th.MaxForce;
                    // ship Forward = -Z (DirIndex 5), Back +Z (4), Up +Y (2), Down -Y (3), Left -X (1), Right +X (0)
                    double[] got = { sums[5], sums[4], sums[2], sums[3], sums[1], sums[0] };
                    bool accGood = ok == 6;
                    for (int i = 0; i < 6 && accGood; i++) accGood = Math.Abs(got[i] / bl.Mass - exp[i]) <= 0.06;
                    bool good = Math.Abs(bl.Mass - massExp) <= 1.5 && bl.Blocks.Count == blocksExp && accGood;
                    rows++;
                    if (!good) { bad++; if (firstBad == null) firstBad = name + " mass " + bl.Mass.ToString("F1") + "/" + massExp + " blocks " + bl.Blocks.Count + "/" + blocksExp + " accel " + string.Join(",", Array.ConvertAll(got, x => (x / bl.Mass).ToString("F2"))) + " vs " + accel.Trim(); }
                }
                Check(rows >= 20 && bad == 0, "all " + rows + " Full models of out/dist/MODELS.md: block count, mass and six-direction acceleration match the python tools" + (firstBad != null ? " (first mismatch: " + firstBad + ")" : ""));
            }
            else SelfTestSkips.Note("out/dist/MODELS.md missing: model cross-check skipped");

            // engine cost: Hornet with script-like gyro writes and thruster commands
            {
                var wb = NewWorld(21);
                var hbod = GridFactory.Spawn(wb, bp, 0, 0, Place(Vector3D.Zero), null)[0];
                hbod.DampenersOn = false;
                hbod.Gyros[0].Override = true;
                int N = 20000;
                var swb = Stopwatch.StartNew();
                int gc0 = GC.CollectionCount(0); long mem0 = GC.GetTotalMemory(false);
                for (int i = 0; i < N; i++)
                {
                    hbod.Gyros[0].Yaw = (float)Math.Sin(i * 0.01); hbod.Gyros[0].Pitch = (float)Math.Cos(i * 0.007); hbod.GyroDirty = true;
                    for (int k = 0; k < hbod.Thrusters.Count; k++) hbod.Thrusters[k].OverrideN = ((i + k * 37) % 200) < 100 ? 14400f : 0f;
                    wb.Step();
                }
                double us = swb.Elapsed.TotalMilliseconds * 1000 / N;
                int gcs = GC.CollectionCount(0) - gc0;
                bool finite = hbod.Position.IsValid() && hbod.LinearVelocity.IsValid() && hbod.AngularVelocity.IsValid();
                Check(finite && hbod.LinearVelocity.Length() <= 100.0 + 1e-9, "Hornet 20000 ticks (333 s) with gyro and thruster commands: finite state, speed " + hbod.LinearVelocity.Length().ToString("F1") + " m/s");
                Info("engine cost: " + us.ToString("F2") + " us per tick for one Hornet (power + thrusters + gyros + integrate), 60 s fight = " + (us * 3600 / 1000).ToString("F1") + " ms; gen0 GCs during 20000 ticks: " + gcs);
                Check(gcs <= 1, "Hornet steady state allocates (almost) nothing per tick (" + gcs + " gen0 collections in 20000 ticks)");
            }

            // Stinger from the rack projection
            var rack = BlueprintLoader.Get(bpDir, "Stinger Rack");
            int np = GridFactory.CountProjected(rack);
            var ws = NewWorld(8);
            var st = GridFactory.SpawnProjected(ws, rack, 0, 1, 3, Place(Vector3D.Zero), null);
            bool allFull = true; foreach (var blk in st[0].Blocks) allFull &= blk.Hp >= blk.MaxHp;
            Check(np >= 1 && st.Count == 1 && st[0].Blocks.Count > 5 && allFull && st[0].ShipIndex == 3, "Stinger spawns from the rack projection (" + np + " projections, " + st[0].Blocks.Count + " blocks, full integrity)");
            Info("Stinger: mass " + st[0].Mass.ToString("F0") + " kg, " + st[0].Thrusters.Count + " thrusters");
        }
    }
}
