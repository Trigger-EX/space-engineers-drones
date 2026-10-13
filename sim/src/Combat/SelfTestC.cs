// SLOT C. Self-checks for combat: rate of fire, gatling hit probability vs an analytic integral, block damage arithmetic (kills a known
// number of armor blocks), railgun delay/reload/penetration budget, rocket flight time and explosion falloff, warhead radius and
// chain/countdown behaviour, friendly fire switch, turret controller designation, pirate ship build + fight, vanilla AI (Midge) smoke,
// determinism and a speed measurement. Called by `SimHost.exe selftest` (Program.cs); `selftest combat` is not special-cased by
// the other slots. Return the number of failures.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VRageMath;
using VRage.Game;
using SeSim.Core;
using SeSim.Blueprint;

namespace SeSim.Combat
{
    static class ArrExt { public static List<BlockData> Concat1Many(this List<BlockData> l, params BlockData[] more) { l.AddRange(more); return l; } public static BlockData[] Concat1(this BlockData[] a, BlockData b) { var r = new BlockData[a.Length + 1]; Array.Copy(a, r, a.Length); r[a.Length] = b; return r; } }

    public static partial class SelfTestC
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }

        sealed class Sink : IEventSink
        {
            public int Shots, Hits, Friendly, Destroyed, Detonations, Splits;
            public double HitDamage, FriendlyDamage;
            public Vector3D FirstHitPos; public bool HaveHit;
            public List<long> ShotTicks = new List<long>();
            public bool KeepShotTicks;
            public void On(ref SimEvent e)
            {
                switch (e.Kind)
                {
                    case EventKind.Shot: Shots++; if (KeepShotTicks) ShotTicks.Add(e.Tick); break;
                    case EventKind.Hit: Hits++; HitDamage += e.Value; if (!HaveHit) { HaveHit = true; FirstHitPos = e.Position; } break;
                    case EventKind.FriendlyFire: Friendly++; FriendlyDamage += e.Value; break;
                    case EventKind.BlockDestroyed: Destroyed++; break;
                    case EventKind.Detonation: Detonations++; break;
                    case EventKind.GridSplit: Splits++; break;
                }
            }
            public void EndTick(World w) { }
        }

        public static int Run()
        {
            _fail = 0; _pass = 0;
            Console.WriteLine("SelfTestC (combat: weapons, projectiles, damage, explosions, warheads, turrets, pirate, vanilla AI)");
            var sw = Stopwatch.StartNew();
            Guarded("gatling cadence and hit probability", TestGatlingHitRate);
            Guarded("block damage arithmetic", TestKillCount);
            Guarded("moving target: swept hit test", TestSweep);
            Guarded("destruction effects on the body", TestDestruction);
            Guarded("railgun penetration budget", TestRailgunPenetration);
            Guarded("railgun delay, reload and capacitor", TestRailgunTiming);
            Guarded("rocket flight, velocity inheritance and explosion falloff", TestRocket);
            Guarded("autocannon and finite ammo", TestAutocannonAndAmmo);
            Guarded("warheads", TestWarheads);
            Guarded("friendly fire", TestFriendlyFire);
            Guarded("turret controller designation", TestControllerDesignation);
            Guarded("blueprints: combat units attach to every weapon block", TestBlueprints);
            Guarded("pirate ship", TestPirate);
            Guarded("vanilla AI (Midge)", TestVanilla);
            Guarded("sim-fix: collision damage and contact episodes", TestCollisionFix);
            Guarded("weapons audit: cadence, burst reload, range, velocity, firing block, power", TestWeaponFidelity);
            Guarded("weapons audit opt-ins: occlusion, warhead triggers, ricochet, bullets vs rockets", TestWeaponOptIns);
            Guarded("sim-fix: decoys", TestDecoys);
            Guarded("sim-fix: unique hit ids, railgun capacitor data, AI search range", TestSimFixMisc);
            Guarded("turret limits from the definitions (range, turn rate, elevation)", TestTurretLimits);
            Guarded("determinism", TestDeterminism);
            Guarded("speed", TestSpeed);
            Console.WriteLine(_fail == 0 ? "SelfTestC passed (" + _pass + " checks, " + sw.ElapsedMilliseconds + " ms)" : "SelfTestC FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        /// <summary>CubeBlocks.sbc: every large-grid AI turret targets out to 800 m (MaxRangeMeters), small-grid 600 m; the ammo goes
        /// further (artillery 2000 m, assault cannon 1400 m), which only a working AI combat block (or a player lock) unlocks.</summary>
        static void TestTurretLimits()
        {
            var db = Defs();
            var art = db.Get("LargeMissileTurret", "LargeCalibreTurret");
            var ac = db.Get("LargeMissileTurret", "LargeBlockMediumCalibreTurret");
            var gat = db.Get("LargeGatlingTurret", "");
            var sg = db.Get("LargeGatlingTurret", "SmallGatlingTurret");
            Check(art.TurretMaxRange == 800 && ac.TurretMaxRange == 800 && gat.TurretMaxRange == 800, "large turrets MaxRangeMeters 800 (artillery " + art.TurretMaxRange + ", assault cannon " + ac.TurretMaxRange + ")");
            Check(sg.TurretMaxRange == 600, "small gatling turret MaxRangeMeters 600 (" + sg.TurretMaxRange + ")");
            Check(Math.Abs(art.Weapon.Ammo.MaxTrajectory - 2000) < 1 && Math.Abs(ac.Weapon.Ammo.MaxTrajectory - 1400) < 1, "ammo reach artillery 2000 / assault cannon 1400");
            Check(Math.Abs(art.TurretRotRadS - 0.2) < 1e-4 && Math.Abs(ac.TurretRotRadS - 0.4) < 1e-4 && Math.Abs(gat.TurretRotRadS - 1.5) < 1e-4, "turn rates (rad/ms x 1000): artillery 0.2, assault cannon 0.4, gatling 1.5 rad/s");
            Check(art.TurretMinElevDeg == -15 && art.TurretMaxElevDeg == 60, "artillery elevation -15..60 deg");
            var s = new SimSettings();
            Check(!s.TurretRangeFromAmmo && s.TurretAiBlockExtendsRange && s.TurretLimitsFromData, "defaults: game range model, AI block extends, limits from data");
        }

        static void Guarded(string name, Action a)
        {
            Console.WriteLine(" - " + name);
            try { a(); }
            catch (Exception e) { Check(false, name + " threw " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        }

        // =====================================================================================================
        // helpers
        // =====================================================================================================
        static string _root;
        static string Root()
        {
            if (_root != null) return _root;
            string dir = Path.GetDirectoryName(typeof(SelfTestC).Assembly.Location);
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

        static World NewWorld(ulong seed, bool api, IEventSink sink, SimSettings s = null)
        {
            if (s == null) s = new SimSettings();
            s.FixedGunsNeedPower = false;           // test worlds have no batteries; the power gate has its own test (set it after NewWorld, before Spawn)
            var w = new World(s, Defs(), seed, false);
            w.SkipApi = !api;
            w.Teams.Init(4);
            if (sink != null) w.Events = sink;
            return w;
        }

        static BlockData Bd(string type, string sub, int x, int y, int z, string fwd = null, string up = null)
        {
            var b = new BlockData { XsiType = "MyObjectBuilder_" + type, TypeId = type, Subtype = sub, Min = new Vector3I(x, y, z) };
            if (fwd != null || up != null)
                b.Orientation = new MyBlockOrientation(OrientMath.ParseDir(fwd ?? "Forward"), OrientMath.ParseDir(up ?? "Up"));
            return b;
        }
        static BlockData Light(int x, int y, int z) { return Bd("CubeBlock", "SmallBlockArmorBlock", x, y, z); }
        static BlockData Heavy(int x, int y, int z) { return Bd("CubeBlock", "SmallHeavyBlockArmorBlock", x, y, z); }

        static BlueprintData Bp(bool large, long firstId, params BlockData[] blocks)
        {
            var bp = new BlueprintData { Name = "test" };
            var g = new GridData { DisplayName = "test", Large = large, EntityId = firstId, DampenersEnabled = false };
            long id = firstId + 1;
            foreach (var b in blocks) { if (b.TypeId != "CubeBlock" && b.EntityId == 0) b.EntityId = id++; g.Blocks.Add(b); }
            bp.Grids.Add(g);
            return bp;
        }

        static long _idc = 1000;
        static Body Spawn(World w, int team, int ship, Vector3D pos, Vector3D? vel, params BlockData[] blocks)
        {
            var at = new SpawnPlacement { Origin = pos, Forward = Vector3D.Forward, Up = Vector3D.Up, LinearVelocity = vel ?? Vector3D.Zero, AngularVelocity = Vector3D.Zero };
            _idc += 100000;
            return GridFactory.Spawn(w, Bp(false, _idc, blocks), team, ship, at, null)[0];
        }

        static float MinHp(Body b) { float m = float.MaxValue; foreach (var k in b.Blocks) if (k.Hp < m) m = k.Hp; return m; }
        static void Invulnerable(Body b) { foreach (var k in b.Blocks) { k.Hp = k.MaxHp = 1e9f; k.CriticalHp = 0; } }
        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }
        // requests ShootOnce every tick until n shots were fired in total (the game ignores a request while the gun is not ready)
        static void FireN(World w, WeaponUnit u, Sink s, int n, int maxTicks)
        {
            for (int i = 0; i < maxTicks && s.Shots < n; i++) { u.ShootOnceRequests = 1; w.Step(); }
        }

        static List<BlockData> Wall(int nx, int ny, int nz, bool heavy)
        {
            var l = new List<BlockData>();
            for (int z = 0; z < nz; z++) for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++) l.Add(heavy ? Heavy(x, y, z) : Light(x, y, z));
            return l;
        }

        // =====================================================================================================
        // 1. gatling: cadence + hit probability on a 1 m x 1 m wall at 300 m vs the analytic integral
        // =====================================================================================================
        static double AnalyticSquareHit(double dist, double half, double alpha, bool uniformAngle)
        {
            int n = 800, m = 800; long hit = 0;
            double cosA = Math.Cos(alpha);
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5) / n;
                double th = uniformAngle ? u * alpha : Math.Acos(1 - u * (1 - cosA));
                double r = dist * Math.Tan(th);
                for (int j = 0; j < m; j++)
                {
                    double ph = (j + 0.5) / m * 2 * Math.PI;
                    if (Math.Abs(r * Math.Cos(ph)) <= half && Math.Abs(r * Math.Sin(ph)) <= half) hit++;
                }
            }
            return (double)hit / ((double)n * m);
        }

        static void TestGatlingHitRate()
        {
            var sink = new Sink();
            var w = NewWorld(7, false, sink);
            var gun = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "", 0, 0, 0));
            var wall = Spawn(w, 1, 1, new Vector3D(-0.25, -0.25, -300), null, Wall(2, 2, 1, true).ToArray());
            Invulnerable(wall);
            var u = gun.Weapons[0];
            Check(u.Mode == ProjectileSystem.ModeBullet && Math.Abs(u.IntervalS - 60.0 / 700) < 1e-9, "gatling def: bullet, interval 60/700 s");
            Check(Math.Abs(u.SpreadRad - 0.15 * Math.PI / 180) < 1e-9, "gatling spread 0.15 deg");
            u.ShootOn = true;
            int ticks = 7200;
            Run(w, ticks);
            double expectShots = ticks / 60.0 * 600 / 60.0;     // GunCadenceFrames: 85 ms -> 6 frames per shot = 600 rpm (weapons-02)
            Check(Math.Abs(sink.Shots - expectShots) <= 2, "gatling fires " + sink.Shots + " rounds in 120 s (expected " + expectShots.ToString("F1") + ")");
            double L = 299.5;
            double p = AnalyticSquareHit(L, 0.5, 0.15 * Math.PI / 180, false);
            double meas = (double)sink.Hits / sink.Shots;
            double sigma = Math.Sqrt(p * (1 - p) / sink.Shots);
            Check(Math.Abs(meas - p) < 4 * sigma + 0.005, "hit fraction " + meas.ToString("F4") + " vs analytic " + p.ToString("F4") + " (4 sigma = " + (4 * sigma).ToString("F4") + ")");

            // same with the other spread law: sensitivity run
            var s2 = new SimSettings { SpreadUniformInAngle = true };
            var sink2 = new Sink();
            var w2 = NewWorld(7, false, sink2, s2);
            var gun2 = Spawn(w2, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "", 0, 0, 0));
            var wall2 = Spawn(w2, 1, 1, new Vector3D(-0.25, -0.25, -300), null, Wall(2, 2, 1, true).ToArray());
            Invulnerable(wall2);
            gun2.Weapons[0].ShootOn = true; Run(w2, 3600);
            double p2 = AnalyticSquareHit(L, 0.5, 0.15 * Math.PI / 180, true), m2 = (double)sink2.Hits / sink2.Shots;
            double sg2 = Math.Sqrt(p2 * (1 - p2) / sink2.Shots);
            Check(Math.Abs(m2 - p2) < 4 * sg2 + 0.005, "uniform-in-angle law: hit fraction " + m2.ToString("F4") + " vs analytic " + p2.ToString("F4"));
        }

        // =====================================================================================================
        // 2. damage arithmetic: 10 gatling hits (90) kill exactly five 100-hp light blocks in a line; autocannon one per shell
        // =====================================================================================================
        static void TestKillCount()
        {
            var sink = new Sink();
            var w = NewWorld(3, false, sink);
            var gun = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "", 0, 0, 0));
            var line = Spawn(w, 1, 1, new Vector3D(0, 0, -50), null, Wall(1, 1, 5, false).ToArray());
            var u = gun.Weapons[0];
            u.SpreadRad = 0;
            Check(line.Blocks.Count == 5 && Math.Abs(line.Blocks[0].Hp - 100f) < 1e-3, "5 light blocks of 100 hp");
            FireN(w, u, sink, 10, 200);
            Run(w, 60);
            Check(sink.Shots == 10, "10 ShootOnce requests fire 10 rounds (got " + sink.Shots + ")");
            Check(sink.Destroyed == 5 && line.Blocks.Count == 0, "10 x 90 damage destroys exactly 5 light blocks (destroyed " + sink.Destroyed + ")");
            Check(Math.Abs(sink.HitDamage - 500) < 1e-3, "absorbed damage excludes overkill: " + sink.HitDamage.ToString("F1") + " of 500");
            // 9 shots: 4 dead, 5th at 100-90 = 10... (4 blocks need 8 hits, the 9th leaves 10 hp)
            var sink2 = new Sink();
            var w2 = NewWorld(3, false, sink2);
            var g2 = Spawn(w2, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "", 0, 0, 0));
            var l2 = Spawn(w2, 1, 1, new Vector3D(0, 0, -50), null, Wall(1, 1, 5, false).ToArray());
            g2.Weapons[0].SpreadRad = 0; FireN(w2, g2.Weapons[0], sink2, 9, 200);
            Run(w2, 60);
            Check(sink2.Destroyed == 4 && l2.Blocks.Count == 1 && Math.Abs(l2.Blocks[0].Hp - 10f) < 1e-3, "9 hits: 4 dead, last block at 10 hp (destroyed " + sink2.Destroyed + ", hp " + (l2.Blocks.Count > 0 ? l2.Blocks[0].Hp : -1) + ")");
            // direct API
            var w3 = NewWorld(3, false, null);
            var tgt = Spawn(w3, 1, 1, Vector3D.Zero, null, Heavy(0, 0, 0));
            var blk = tgt.Blocks[0];
            float a1 = Damage.ApplyToBlock(w3, blk, 500f, 0, DamageKind.Bullet, Vector3D.Zero);
            float a2 = Damage.ApplyToBlock(w3, blk, 500f, 0, DamageKind.Bullet, Vector3D.Zero);
            Check(Math.Abs(a1 - 500f) < 1e-3 && Math.Abs(a2 - 60f) < 1e-3 && blk.Destroyed, "ApplyToBlock returns absorbed damage (500, then 60 of 560 hp)");
        }



        // =====================================================================================================
        // destruction effects: thruster/gun loss, critical integrity, grid break-up
        // =====================================================================================================
        static void TestDestruction()
        {
            var sink = new Sink();
            var w = NewWorld(13, false, sink);
            // a spine: gun - armor - armor - armor - thruster - battery in a row along +X
            var ship = Spawn(w, 0, 0, Vector3D.Zero, null,
                Bd("SmallGatlingGun", "", 0, 0, 0), Light(0, 0, 4), Light(1, 0, 4), Light(2, 0, 4),
                Bd("Thrust", "SmallBlockSmallThrust", 3, 0, 4), Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 4, 0, 4), Light(0, 0, 3), Light(0, 0, 2), Light(0, 0, 1));
            double m0 = ship.Mass; int th0 = ship.Thrusters.Count;
            var gun = ship.Weapons[0];
            gun.ShootOn = true; Run(w, 30);
            int s1 = sink.Shots;
            Check(s1 > 3, "intact gun fires (" + s1 + " rounds in 0.5 s)");
            // below critical integrity (320 of 621 hp): the gun stops without being destroyed
            var gb = gun.Block;
            Damage.ApplyToBlock(w, gb, gb.Hp - gb.CriticalHp + 10f, 5, DamageKind.Bullet, Vector3D.Zero);
            Check(!gb.IsFunctional && !gb.Destroyed, "gun below critical integrity is non-functional but still there");
            Run(w, 30);
            Check(sink.Shots == s1, "a non-functional gun does not fire");
            // destroy the thruster: thrust and mass drop, the gun block hp is untouched
            var tb = ship.Thrusters[0].Block;
            Damage.ApplyToBlock(w, tb, 1e6f, 5, DamageKind.Bullet, Vector3D.Zero);
            Run(w, 2);
            Check(ship.Thrusters.Count == th0 - 1 && ship.Mass < m0, "destroyed thruster leaves the body (thrusters " + ship.Thrusters.Count + ", mass " + m0.ToString("F0") + " -> " + ship.Mass.ToString("F0") + ")");
            // cut the spine in the middle: the battery side (battery only: 1 block) splits off
            int bodies0 = w.Bodies.Count;
            foreach (var k in new List<BlockInst>(ship.Blocks)) if (k.Min.X == 1 && k.Min.Z == 4) { Damage.ApplyToBlock(w, k, 1e6f, 5, DamageKind.Bullet, Vector3D.Zero); break; }
            Run(w, 2);
            Check(w.Bodies.Count == bodies0 + 1, "cutting the spine splits the grid (bodies " + bodies0 + " -> " + w.Bodies.Count + ")");
            // destroyed gun block is gone from the unit lists
            Damage.ApplyToBlock(w, gb, 1e6f, 5, DamageKind.Bullet, Vector3D.Zero);
            Run(w, 2);
            Check(gb.Destroyed && !gb.Body.Weapons.Contains(gun) && w.Combat.Weapons.IndexOf(gun) < 0, "destroyed gun leaves the weapon lists");
        }

        // =====================================================================================================
        // moving target: exact lead shots must hit a 1 m target moving 95 m/s (needs the in-tick sweep)
        // =====================================================================================================
        static int SweepTrial(bool sweep, double dist, double speed, int trial)
        {
            var s = new SimSettings { ProjectileSweepBodies = sweep, BulletRangeMin = 1.0 };
            var sink = new Sink();
            var w = NewWorld((ulong)(100 + trial), false, sink, s);
            var gun = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "", 0, 0, 0));
            double ang = trial * 0.7;                                  // target velocity direction (perpendicular-ish) varies with the trial
            Vector3D vt = new Vector3D(Math.Cos(ang), Math.Sin(ang), 0.3); vt.Normalize(); vt *= speed;
            Vector3D start = new Vector3D(-vt.X * 0.2, -vt.Y * 0.2, -dist);
            var tgt = Spawn(w, 1, 1, start - new Vector3D(0.25, 0.25, 0), vt, Wall(2, 2, 1, true).ToArray());
            Invulnerable(tgt);
            var u = gun.Weapons[0];
            u.SpreadRad = 0;
            // exact intercept against the target centre at tick 0 (gun fires at t = 0 from the muzzle)
            Vector3D muz = gun.LocalToWorld(u.MuzzleLocal);
            Vector3D rel = tgt.Position - muz; double ti;
            if (!CombatUtil.Intercept(rel, vt, 800, out ti)) return -1;
            Vector3D want = rel + vt * ti; want.Normalize();
            // rotate the gun to point at 'want': gun looks along -Z; make a body rotation
            Vector3D axis = Vector3D.Cross(Vector3D.Forward, want); double sn = axis.Length(), cs = Vector3D.Dot(Vector3D.Forward, want);
            gun.Rotation = QuaternionD.CreateFromAxisAngle(axis * (1.0 / sn), Math.Atan2(sn, cs));
            gun.UpdateWorldMatrix();
            // the muzzle moved with the rotation: recompute the aim from the new muzzle (iterate twice)
            for (int it = 0; it < 3; it++)
            {
                muz = gun.LocalToWorld(u.MuzzleLocal);
                rel = tgt.Position - muz;
                if (!CombatUtil.Intercept(rel, vt, 800, out ti)) return -1;
                want = rel + vt * ti; want.Normalize();
                axis = Vector3D.Cross(Vector3D.Forward, want); sn = axis.Length(); cs = Vector3D.Dot(Vector3D.Forward, want);
                gun.Rotation = QuaternionD.CreateFromAxisAngle(axis * (1.0 / sn), Math.Atan2(sn, cs));
                gun.UpdateWorldMatrix();
            }
            FireN(w, u, sink, 1, 5);
            if (Environment.GetEnvironmentVariable("SIM_TRACE") != null && trial == 0 && sweep)
            {
                Console.WriteLine("    sweep trial: ti=" + ti + " want=" + want + " dir=" + gun.DirToWorld(u.DirLocal) + " muz=" + gun.LocalToWorld(u.MuzzleLocal) + " tgt=" + tgt.Position + " vt=" + vt);
                for (int k = 0; k < 40; k++) { w.Step(); if (w.Projectiles.Count > 0) Console.WriteLine("    k=" + k + " bullet=" + w.Projectiles.Pos[0] + " tgt=" + tgt.Position + " hits=" + sink.Hits + " shots=" + sink.Shots); }
            }
            Run(w, 120);
            return sink.Hits;
        }

        static void TestSweep()
        {
            int okOn = 0, okOff = 0, n = 24;
            for (int i = 0; i < n; i++)
            {
                double dist = 200 + 25 * i;
                if (SweepTrial(true, dist, 95, i) > 0) okOn++;
                if (SweepTrial(false, dist, 95, i) > 0) okOff++;
            }
            Info("exact-lead single shots at a 1 m target moving 95 m/s: sweep on " + okOn + "/" + n + ", sweep off " + okOff + "/" + n);
            Check(okOn >= n - 1, "swept hit test hits exact-lead shots (" + okOn + "/" + n + ")");
            Check(okOff < okOn, "without the sweep the tick quantisation costs hits (" + okOff + "/" + n + ")");
        }

        // =====================================================================================================
        // 3. railgun penetration: pool 8000 walks 80 light blocks (100 hp) / 14 heavy (560) + 160 into the 15th
        // =====================================================================================================
        static void TestRailgunPenetration()
        {
            foreach (bool heavy in new[] { false, true })
            {
                var sink = new Sink();
                var w = NewWorld(5, false, sink);
                var gun = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallMissileLauncherReload", "SmallRailgun", 0, 0, 0));
                var line = Spawn(w, 1, 1, new Vector3D(0, 0, -150), null, Wall(1, 1, 100, heavy).ToArray());
                var u = gun.Weapons[0];
                Check(u.Mode == ProjectileSystem.ModeSlug, "railgun is a slug weapon");
                FireN(w, u, sink, 1, 10);
                Run(w, 120);
                float hp = heavy ? 560f : 100f;
                int expectDead = (int)Math.Floor(8000f / hp);
                Check(sink.Destroyed == expectDead && line.Blocks.Count == 100 - expectDead,
                    (heavy ? "heavy" : "light") + ": slug destroyed " + sink.Destroyed + " blocks (expected " + expectDead + ")");
                if (heavy)
                {
                    BlockInst next = null; foreach (var k in line.Blocks) if (k.Hp < k.MaxHp) next = k;
                    Check(next != null && Math.Abs(next.Hp - 400f) < 1e-2, "heavy: the next block keeps 400 of 560 hp (pool 160 left): " + (next != null ? next.Hp.ToString("F1") : "none"));
                }
                Check(w.Projectiles.Count == 0, "slug consumed");
                Check(Math.Abs(sink.HitDamage - 8000) < 1e-2, "total absorbed damage equals the health pool (" + sink.HitDamage.ToString("F1") + ")");
            }
        }

        // =====================================================================================================
        // 4. railgun: 500 ms delay, 4 s reload, capacitor recharge through the power system
        // =====================================================================================================
        static void TestRailgunTiming()
        {
            var sink = new Sink { KeepShotTicks = true };
            var w = NewWorld(5, false, sink);
            var ship = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallMissileLauncherReload", "SmallRailgun", 0, 0, 0), Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 3, 0, 0));
            var u = ship.Weapons[0];
            var bat = ship.Batteries[0];
            bat.MaxOutputMW = 100; bat.StoredMWh = bat.MaxStoredMWh = 100;
            Check(u.Sink != null && u.Sink.Group == "DefenseAdative", "railgun has a DefenseAdative power sink");
            Check(Math.Abs(u.CapacitorMWh - u.CapacitorFull) < 1e-12, "capacitor starts full");
            u.ShootOn = true;                                  // hold the trigger: shoots whenever allowed
            Run(w, 60 * 45);
            Check(sink.Shots >= 2, "at least two shots in 45 s (got " + sink.Shots + ")");
            double t1 = sink.ShotTicks[0] / 60.0, t2 = sink.ShotTicks[1] / 60.0;
            Check(Math.Abs(t1 - 0.5) < 0.03, "first slug leaves 500 ms after the trigger (t = " + t1.ToString("F3") + ")");
            double recharge = Defs() != null ? new SimSettings().RailgunCapacitorMWh * 3600.0 / new SimSettings().RailgunChargeMW : 0;
            Check(Math.Abs((t2 - t1) - (recharge + 0.5)) < 0.15, "second slug after capacitor recharge " + recharge.ToString("F1") + " s + 0.5 s delay (gap " + (t2 - t1).ToString("F2") + " s)");
            Check(t2 - t1 > 4.0, "gap exceeds the 4 s reload");
            // without power the capacitor never refills
            var sink2 = new Sink { KeepShotTicks = true };
            var w2 = NewWorld(5, false, sink2);
            var s2 = Spawn(w2, 0, 0, Vector3D.Zero, null, Bd("SmallMissileLauncherReload", "SmallRailgun", 0, 0, 0));
            s2.Weapons[0].ShootOn = true;
            Run(w2, 60 * 30);
            Check(sink2.Shots == 1, "no battery: one slug from the initial charge, then nothing (shots " + sink2.Shots + ")");
        }

        // =====================================================================================================
        // 5. rocket
        // =====================================================================================================
        static void TestRocket()
        {
            // flight time in open space: 800 m, 100 -> 200 m/s at 600 m/s^2
            var sink = new Sink();
            var w = NewWorld(11, false, sink);
            var l = Spawn(w, 0, 0, Vector3D.Zero, new Vector3D(0, 0, -50), Bd("SmallMissileLauncher", "", 0, 0, 0));
            var u = l.Weapons[0];
            u.SpreadRad = 0; u.ShootOnceRequests = 1;
            u.Update(w, SimSettings.Dt);
            Check(w.Projectiles.Count == 1, "rocket spawned");
            Vector3D v = w.Projectiles.Vel[0];
            Check(Math.Abs(v.Z + 150) < 1e-6 && Math.Abs(v.X) < 1e-9 && Math.Abs(v.Y) < 1e-9, "rocket starts at 100 m/s along the muzzle PLUS the 50 m/s ship velocity (RocketInherit 1): " + v);
            {
                // RocketInherit 1, ship moving sideways at 60 m/s: the rocket bends onto the barrel line; drift after 2 s about 29 m
                // (closed form of the scripts' RktPath: V0 = 100, A = 600, cap 200)
                var wc = NewWorld(11, false, new Sink());
                var lc = Spawn(wc, 0, 0, Vector3D.Zero, new Vector3D(60, 0, 0), Bd("SmallMissileLauncher", "", 0, 0, 0));
                var uc = lc.Weapons[0]; uc.SpreadRad = 0; uc.ShootOnceRequests = 1; uc.Update(wc, SimSettings.Dt);
                Vector3D p0 = wc.Projectiles.Pos[0];
                for (int k = 0; k < 120; k++) wc.Projectiles.Step(SimSettings.Dt);
                Vector3D dp = wc.Projectiles.Pos[0] - p0, vc = wc.Projectiles.Vel[0];
                Check(Math.Abs(dp.X - 29.3) < 1.5 && Math.Abs(vc.Length() - 200) < 1e-6 && vc.X < 1, "sideways launch curves onto the barrel: drift " + dp.X.ToString("F1") + " m, along " + (-dp.Z).ToString("F1") + " m, v " + vc);
                var w0 = NewWorld(11, false, new Sink()); w0.Settings.RocketInherit = 0;
                var l0 = Spawn(w0, 0, 0, Vector3D.Zero, new Vector3D(0, 0, -50), Bd("SmallMissileLauncher", "", 0, 0, 0));
                var u0 = l0.Weapons[0]; u0.SpreadRad = 0; u0.ShootOnceRequests = 1; u0.Update(w0, SimSettings.Dt);
                Vector3D v0 = w0.Projectiles.Vel[0];
                Check(Math.Abs(v0.Z + 100) < 1e-6 && Math.Abs(v0.X) < 1e-9, "RocketInherit 0: rocket starts at 100 m/s along the muzzle WITHOUT the ship velocity: " + v0);
            }
            // bullets inherit
            var gun = Spawn(w, 0, 0, new Vector3D(100, 0, 0), new Vector3D(0, 0, -50), Bd("SmallGatlingGun", "", 0, 0, 0));
            var gu = gun.Weapons[0]; gu.SpreadRad = 0; gu.ShootOnceRequests = 1;
            int n0 = w.Projectiles.Count;
            gu.Update(w, SimSettings.Dt);
            Vector3D bv = w.Projectiles.Vel[n0];
            Check(Math.Abs(bv.Z + 850) < 1e-6, "gatling bullet = ship velocity + 800 m/s: " + bv.Z.ToString("F3"));
            // remove the bullet; run until the rocket is gone
            w.Projectiles.Count = 0;
            w.Projectiles.Spawn(Vector3D.Zero, new Vector3D(0, 0, -100), u.Ammo, l, 0);
            int tick = 0;
            while (w.Projectiles.Count > 0 && tick < 1000) { w.Projectiles.Step(SimSettings.Dt); tick++; }
            double tFlight = tick / 60.0;
            double analytic = (200.0 - 100.0) / 600.0 + (800.0 - (100 + 200) / 2.0 * (200.0 - 100.0) / 600.0) / 200.0;
            Check(Math.Abs(tFlight - analytic) < 0.04, "rocket range-end time " + tFlight.ToString("F3") + " s vs analytic " + analytic.ToString("F3") + " s");

            // explosion falloff on a heavy wall at 400 m
            var sink2 = new Sink();
            var w2 = NewWorld(11, false, sink2);
            var sh = Spawn(w2, 0, 0, Vector3D.Zero, null, Bd("SmallMissileLauncher", "", 0, 0, 0));
            var wallBlocks = Wall(11, 11, 1, true);
            var wall = Spawn(w2, 1, 1, new Vector3D(-2.5, -2.5, -400), null, wallBlocks.ToArray());
            sh.Weapons[0].SpreadRad = 0; FireN(w2, sh.Weapons[0], sink2, 1, 5);
            Run(w2, 200);
            Check(sink2.HaveHit, "rocket hit the wall");
            Vector3D P = sink2.FirstHitPos;
            Check(Math.Abs(P.X) < 0.01 && Math.Abs(P.Y) < 0.01 && Math.Abs(P.Z + 399.75) < 0.01, "explosion centred on the hit face " + P);
            int checkedBlocks = 0, bad = 0; double maxErr = 0;
            foreach (var k in wall.Blocks)
            {
                Vector3D c = wall.LocalToWorld(k.LocalCenter);
                double d = (c - P).Length();
                double dmg = d <= 4.0 ? 500.0 * (1 - d / 4.0) : 0;
                double expectHp = 560.0 - dmg;
                double err = Math.Abs(k.Hp - expectHp); if (err > maxErr) maxErr = err;
                if (err > 0.5) bad++;
                checkedBlocks++;
            }
            Check(bad == 0, "all " + checkedBlocks + " wall blocks match damage*(1-d/r) (max error " + maxErr.ToString("F3") + " hp)");
            double expTotal = 0; foreach (var k in wall.Blocks) expTotal += 560.0 - k.Hp;
            Check(sink2.Hits == 1 && Math.Abs(sink2.HitDamage - expTotal) < 0.5, "one Hit event per explosion and body, carrying the summed damage (" + sink2.Hits + " event, " + sink2.HitDamage.ToString("F1") + " of " + expTotal.ToString("F1") + ")");
        }

        // =====================================================================================================
        // 6. autocannon + finite ammo
        // =====================================================================================================
        static void TestAutocannonAndAmmo()
        {
            var sink = new Sink();
            var w = NewWorld(2, false, sink);
            var ac = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "SmallBlockAutocannon", 0, 0, 0));
            var line = Spawn(w, 1, 1, new Vector3D(0, 0, -50), null, Wall(1, 1, 5, false).ToArray());
            var u = ac.Weapons[0];
            u.SpreadRad = 0; FireN(w, u, sink, 3, 300);
            Run(w, 60);
            Check(sink.Shots == 3 && sink.Destroyed == 3, "autocannon: 3 shells destroy 3 light blocks (shots " + sink.Shots + ", destroyed " + sink.Destroyed + ")");
            Check(Math.Abs(u.IntervalS - 0.4) < 1e-9, "autocannon interval 0.4 s");

            // finite ammo: autocannon 3 clips x 16 = 48 shells (0.072 m3 / 24 L), no reload delay
            var s = new SimSettings { InfiniteAmmo = false };
            var sink2 = new Sink();
            var w2 = NewWorld(2, false, sink2, s);
            var g = Spawn(w2, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "SmallBlockAutocannon", 0, 0, 0));
            var gu = g.Weapons[0]; gu.SpreadRad = 0;
            Check(gu.RoundsLeftInMag + gu.MagazinesLeft * 16 == 48, "finite ammo: 48 shells loaded (" + (gu.RoundsLeftInMag + gu.MagazinesLeft * 16) + ")");
            gu.ShootOn = true;
            Run(w2, 60 * 40);
            Check(sink2.Shots == 48, "autocannon runs dry after 48 shells (shots " + sink2.Shots + ")");
        }

        // =====================================================================================================
        // 7. warheads
        // =====================================================================================================
        static BlockData ArmedWarhead(int x, bool armed)
        {
            var b = Bd("Warhead", "SmallWarhead", x, 0, 0);
            b.Props["IsArmed"] = armed ? "true" : "false";
            b.Props["CountdownMs"] = "1000";
            return b;
        }

        static Body WarheadRow(World w, int team, bool armed, int ship = 0)
        {
            var blocks = new List<BlockData> { ArmedWarhead(0, armed) };
            for (int x = 1; x <= 9; x++) blocks.Add(Light(x, 0, 0));
            return Spawn(w, team, ship, Vector3D.Zero, null, blocks.ToArray());
        }

        static void TestWarheads()
        {
            var sink = new Sink();
            var w = NewWorld(4, false, sink);
            var row = WarheadRow(w, 0, true);
            var wh = row.Blocks[0].Warhead;
            Check(wh != null && wh.Armed && Math.Abs(wh.DetonationTime - 1.0) < 1e-9, "warhead unit from blueprint (armed, 1 s countdown)");
            Check(row.Blocks[0].Def.WarheadRadius > 4.48 && row.Blocks[0].Def.WarheadRadius < 4.49, "radius 4.4883 m");
            wh.Detonate(w);
            Run(w, 2);
            // blocks 1..8 have centres at 0.5..4.0 m (< 4.4883) and die, block 9 at 4.5 m survives
            int alive = row.Blocks.Count;
            Check(sink.Detonations == 1, "one Detonation event");
            Check(alive == 1 && row.Blocks[0].Def.Subtype == "SmallBlockArmorBlock" && row.Blocks[0].Index == 0 && wh.Block.Destroyed, "blast kills the 8 blocks within 4.4883 m and the warhead; the block at 4.5 m survives (alive " + alive + ")");
            Check(Math.Abs(row.Blocks[0].Hp - 100f) < 1e-3, "block outside the radius undamaged");
            // armed warhead destroyed by damage detonates; unarmed does not
            var sink2 = new Sink();
            var w2 = NewWorld(4, false, sink2);
            var r2 = WarheadRow(w2, 0, true);
            Damage.ApplyToBlock(w2, r2.Blocks[0], 1e6f, 1, DamageKind.Bullet, Vector3D.Zero);
            Run(w2, 3);
            Check(sink2.Detonations == 1 && r2.Blocks.Count == 1, "armed warhead destroyed by damage explodes");
            var sink3 = new Sink();
            var w3 = NewWorld(4, false, sink3);
            var r3 = WarheadRow(w3, 0, false);
            Damage.ApplyToBlock(w3, r3.Blocks[0], 1e6f, 1, DamageKind.Bullet, Vector3D.Zero);
            Run(w3, 3);
            Check(sink3.Detonations == 0 && r3.Blocks.Count == 9, "unarmed warhead destroyed by damage does not explode");
            // Detonate() on an unarmed warhead does nothing; countdown does
            r3 = WarheadRow(w3, 0, false, 5);
            var u3 = r3.Blocks[0].Warhead;
            u3.Detonate(w3); Run(w3, 3);
            Check(sink3.Detonations == 0, "Detonate() ignored when not armed");
            u3.CountingDown = true; u3.CountdownLeft = u3.DetonationTime;
            int t0 = 0; while (sink3.Detonations == 0 && t0 < 200) { w3.Step(); t0++; }
            Check(sink3.Detonations == 1 && Math.Abs(t0 - 60) <= 2, "countdown of 1 s detonates after " + t0 + " ticks");
            // own blast hits friendly parts (FriendlyFire with ShipA == ShipB), 'Hit' stays enemy-only
            Check(sink.Friendly == 1 && sink.Hits == 0, "carrier damage reported as one FriendlyFire event (" + sink.Friendly + "), no Hit (" + sink.Hits + ")");
            // enemy blast reports Hit
            var sink4 = new Sink();
            var w4 = NewWorld(4, false, sink4);
            var wr = WarheadRow(w4, 0, true, 0);
            var victim = Spawn(w4, 1, 1, new Vector3D(0, 1.0, 0), null, Wall(4, 1, 1, false).ToArray());
            wr.Blocks[0].Warhead.Detonate(w4); Run(w4, 2);
            Check(sink4.Hits == 1 && sink4.Friendly == 1, "blast on an enemy body: one Hit (" + sink4.Hits + "), plus one FriendlyFire for the carrier (" + sink4.Friendly + ")");
        }

        // =====================================================================================================
        // 8. friendly fire
        // =====================================================================================================
        static void TestFriendlyFire()
        {
            foreach (bool ff in new[] { true, false })
            {
                var s = new SimSettings { FriendlyFire = ff };
                var sink = new Sink();
                var w = NewWorld(6, false, sink, s);
                var gun = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "", 0, 0, 0));
                var mate = Spawn(w, 0, 1, new Vector3D(0, 0, -50), null, Wall(1, 1, 3, false).ToArray());
                gun.Weapons[0].SpreadRad = 0; FireN(w, gun.Weapons[0], sink, 4, 200);
                Run(w, 60);
                if (ff) Check(sink.Friendly > 0 && sink.Hits == 0 && mate.Blocks.Count < 3, "friendly fire on: FriendlyFire events " + sink.Friendly + ", blocks left " + mate.Blocks.Count);
                else Check(sink.Friendly == 0 && sink.Hits == 0 && mate.Blocks.Count == 3 && Math.Abs(mate.Blocks[0].Hp - 100f) < 1e-3, "friendly fire off: no damage, no event");
            }
        }

        // =====================================================================================================
        // 9. turret controller designation (needs the API objects)
        // =====================================================================================================
        static void TestControllerDesignation()
        {
            var sink = new Sink();
            var w = NewWorld(8, true, sink);
            var me = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("TurretControlBlock", "SmallTurretControlBlock", 0, 0, 0), Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 2, 0, 0));
            var enemy = Spawn(w, 1, 1, new Vector3D(0, 0, -500), null, Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 0, 0, 0), Heavy(1, 0, 0), Heavy(2, 0, 0));
            var ctl = me.Blocks[0];
            Check(ctl.Turret != null && ctl.Turret.IsController && Math.Abs(ctl.Turret.Range - 600) < 1e-3, "controller: designator with range 600");
            Run(w, 110);   // turrets retarget every 100 ticks
            var api = ctl.Api as SeSim.Api.SimTurretControlBlock;
            Check(api != null && api.IsWorking, "controller API object working");
            Check(ctl.Turret.TargetBody == enemy, "enemy at 500 m designated");
            if (api != null)
            {
                Check(api.HasTarget, "HasTarget true");
                var info = api.GetTargetedEntity();
                Check(!info.IsEmpty() && info.EntityId == enemy.EntityId && info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies, "GetTargetedEntity: enemy grid id and relationship");
            }
            enemy.Position = new Vector3D(0, 0, -900); enemy.UpdateWorldMatrix();
            Run(w, 110);   // turrets retarget every 100 ticks
            Check(ctl.Turret.TargetBody == null, "enemy beyond 600 m dropped at the next refresh");
            // line of sight blocked by a third body
            enemy.Position = new Vector3D(0, 0, -500); enemy.UpdateWorldMatrix();
            var shield = Spawn(w, 2, 2, new Vector3D(-5, -5, -100), null, Wall(40, 40, 2, true).ToArray());
            Run(w, 110);   // turrets retarget every 100 ticks
            Check(ctl.Turret.TargetBody != enemy, "target behind a neutral-team wall is not visible (target " + (ctl.Turret.TargetBody != null ? ctl.Turret.TargetBody.DisplayName + "#" + ctl.Turret.TargetBody.Id : "none") + ")");
        }


        // =====================================================================================================
        // real blueprints: every weapon / turret / controller / warhead block gets its combat unit
        // =====================================================================================================
        static void TestBlueprints()
        {
            string bpDir = Path.Combine(Root(), "out", "Blueprints");
            if (!Directory.Exists(bpDir)) { SeSim.Core.SelfTestSkips.Note("out/Blueprints missing: weapon unit test skipped"); return; }
            string[] names = { "Aegis", "Basilisk", "Gnat", "Harrier", "Hornet", "Lancer", "Longbow L", "Longbow S", "Mantis", "Midge", "Wasp", "Watcher", "Stinger Rack" };
            int missing = 0, total = 0;
            foreach (var name in names)
            {
                if (!File.Exists(Path.Combine(bpDir, name, "bp.sbc"))) { SeSim.Core.SelfTestSkips.Note(name + ": blueprint missing, skipped"); continue; }
                var w = NewWorld(1, true, null);
                var bp = BlueprintLoader.Get(bpDir, name);
                var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up };
                var bodies = GridFactory.Spawn(w, bp, 0, 0, at, null);
                var cnt = new SortedDictionary<string, int>();
                int rail = 0, ctl = 0, lt = 0, wh = 0, armed = 0;
                foreach (var b in bodies)
                    foreach (var k in b.Blocks)
                    {
                        string kind = k.Def.Kind;
                        bool want = kind == "WeaponBlock" || kind == "LargeTurretBase" || kind == "TurretControlBlock" || kind == "Warhead";
                        if (!want) continue;
                        total++;
                        if (kind == "Warhead") { if (k.Warhead == null) missing++; else { wh++; if (k.Warhead.Armed) armed++; } }
                        else if (kind == "TurretControlBlock") { if (k.Turret == null || !k.Turret.IsController) missing++; else ctl++; }
                        else
                        {
                            if (k.Weapon == null || (kind == "LargeTurretBase" && k.Turret == null)) { missing++; continue; }
                            if (kind == "LargeTurretBase") lt++;
                            if (k.Weapon.Mode == ProjectileSystem.ModeSlug) { rail++; if (k.Weapon.Sink == null) missing++; }
                            string key = k.Weapon.Def.Id + (k.Weapon.Mode == 1 ? "(rocket)" : k.Weapon.Mode == 2 ? "(slug)" : "");
                            int c; cnt.TryGetValue(key, out c); cnt[key] = c + 1;
                        }
                    }
                var sb = new System.Text.StringBuilder();
                foreach (var kv in cnt) sb.Append(' ').Append(kv.Value).Append('x').Append(kv.Key);
                Info(name + ": weapons" + (sb.Length == 0 ? " none" : sb.ToString()) + "; controllers " + ctl + ", large turrets " + lt + ", warheads " + wh + " (armed " + armed + ")");
            }
            Check(missing == 0, "all " + total + " weapon/turret/controller/warhead blocks got their units (missing " + missing + ")");
        }

        // =====================================================================================================
        // 10. pirate
        // =====================================================================================================
        sealed class PirateResult { public Sink S; public Body Pirate, Dummy; public World W; public double Dist0, DistEnd; public Vector3D Fwd; public string Hash; }

        static PirateResult RunPirate(ulong seed, int ticks, bool print)
        {
            var sink = new Sink();
            var w = NewWorld(seed, true, sink);
            var bp = PirateShipFactory.Build(w.Defs);
            var at = new SpawnPlacement { Origin = new Vector3D(0, 0, 0), Forward = Vector3D.Forward, Up = Vector3D.Up };
            var pirate = GridFactory.Spawn(w, bp, 1, 1, at, null)[0];
            w.Brains.Add(new PirateBrain(pirate));
            var blocks = Wall(16, 16, 3, true);
            blocks.Add(Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 8, 8, -1));
            var dummy = Spawn(w, 0, 0, new Vector3D(0, 0, -900), null, blocks.ToArray());
            var r = new PirateResult { S = sink, Pirate = pirate, Dummy = dummy, W = w };
            r.Dist0 = (dummy.Position - pirate.Position).Length();
            for (int i = 0; i < ticks; i++)
            {
                w.Step();
                if (print && Environment.GetEnvironmentVariable("SIM_TRACE") != null && i % 300 == 0)
                    Console.WriteLine("    t=" + (i / 60) + " dist " + (dummy.Position - pirate.Position).Length().ToString("F0") + " speed " + pirate.LinearVelocity.Length().ToString("F1") + " w " + pirate.AngularVelocity.Length().ToString("F3") + " power " + pirate.Power.LastSuppliedMW.ToString("F2") + "/" + pirate.Power.LastDemandMW.ToString("F2") + " stored " + pirate.Power.StoredMWh().ToString("F2") + " blocks " + pirate.Blocks.Count);
            }
            r.DistEnd = (dummy.Position - pirate.Position).Length();
            r.Fwd = pirate.DirToWorld(Vector3D.Forward);
            r.Hash = pirate.Position.X.ToString("R") + "|" + pirate.Position.Z.ToString("R") + "|" + sink.Shots + "|" + sink.Hits + "|" + sink.HitDamage.ToString("R") + "|" + dummy.Blocks.Count;
            return r;
        }

        static void TestPirate()
        {
            var w0 = NewWorld(1, true, null);
            var bp = PirateShipFactory.Build(w0.Defs);
            var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up };
            var p0 = GridFactory.Spawn(w0, bp, 1, 1, at, null)[0];
            Info("pirate: " + p0.Blocks.Count + " blocks, mass " + (p0.Mass / 1000).ToString("F0") + " t, thrusters " + p0.Thrusters.Count + ", gyros " + p0.Gyros.Count + ", weapons " + p0.Weapons.Count + ", batteries " + p0.Batteries.Count);
            Check(p0.Thrusters.Count == 12 && p0.Gyros.Count == 4 && p0.Weapons.Count == 3 && p0.Batteries.Count == 4, "pirate has 12 thrusters, 4 gyros, 3 turret guns, 4 batteries");
            Check(p0.GridSize == MyCubeSize.Large && p0.Mass > 200000 && p0.Mass < 450000, "large grid, mass in the 200..450 t range");
            var dirs = new int[6]; foreach (var t in p0.Thrusters) dirs[t.DirIndex]++;
            Check(dirs[0] == 2 && dirs[1] == 2 && dirs[2] == 2 && dirs[3] == 2 && dirs[4] == 2 && dirs[5] == 2, "two thrusters push along each of the six directions");
            var comps = new List<List<BlockInst>>();
            Check(Connectivity.Components(p0, comps) == 1, "pirate is one connected component");
            int turrets = 0; foreach (var t in w0.Combat.Turrets) if (t.Block.Body == p0) turrets++;
            Check(turrets == 3, "three turret units");

            var r = RunPirate(21, 60 * 60, true);
            var pr = r.Pirate;
            Info("pirate after 60 s: dist " + r.Dist0.ToString("F0") + " -> " + r.DistEnd.ToString("F0") + " m, speed " + pr.LinearVelocity.Length().ToString("F1") + ", shots " + r.S.Shots + ", hits " + r.S.Hits + ", dummy blocks " + r.Dummy.Blocks.Count + "/" + (16 * 16 * 3 + 1));
            Check(r.DistEnd < r.Dist0 - 100, "pirate closes on the target");
            Check(r.S.Shots > 20 && r.S.Hits > 5, "turrets fire and hit (shots " + r.S.Shots + ", hits " + r.S.Hits + ")");
            Check(r.Dummy.Blocks.Count < 16 * 16 * 3 + 1, "dummy lost blocks");
            // the brain must not depend on the gyro sign switches
            var flipped = new SimSettings { GyroPitchSign = 1, GyroYawSign = 1, GyroRollSign = 1 };
            var w2 = NewWorld(21, true, new Sink(), flipped);
            var pb2 = GridFactory.Spawn(w2, PirateShipFactory.Build(w2.Defs), 1, 1, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Left, Up = Vector3D.Up }, null)[0];
            w2.Brains.Add(new PirateBrain(pb2));
            var d2 = Spawn(w2, 0, 0, new Vector3D(0, 0, -900), null, Wall(8, 8, 2, true).ToArray().Concat1(Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 3, 3, -1)));
            for (int i = 0; i < 60 * 6; i++) { w2.Step(); if (Environment.GetEnvironmentVariable("SIM_TRACE") != null && i % 60 == 0) { Vector3D tt = d2.Position - pb2.Position; tt.Normalize(); Console.WriteLine("    flipped t=" + i / 60 + " dot " + Vector3D.Dot(pb2.DirToWorld(Vector3D.Forward), tt).ToString("F3") + " w " + pb2.AngularVelocity.Length().ToString("F3")); } }
            Vector3D toT = d2.Position - pb2.Position; toT.Normalize();
            Check(Vector3D.Dot(pb2.DirToWorld(Vector3D.Forward), toT) > 0.9, "pirate turns onto the target also with flipped gyro signs (dot " + Vector3D.Dot(pb2.DirToWorld(Vector3D.Forward), toT).ToString("F3") + ")");
        }

        // =====================================================================================================
        // 11. vanilla AI
        // =====================================================================================================
        static void TestVanilla()
        {
            string bpDir = Path.Combine(Root(), "out", "Blueprints");
            if (!File.Exists(Path.Combine(bpDir, "Midge", "bp.sbc"))) { SeSim.Core.SelfTestSkips.Note("out/Blueprints/Midge missing: vanilla AI smoke test skipped"); return; }
            var s = new SimSettings { VanillaSearchIntervalS = 2 };
            var sink = new Sink();
            var w = NewWorld(31, true, sink, s);
            var bp = BlueprintLoader.Get(bpDir, "Midge");
            var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up };
            var bodies = GridFactory.Spawn(w, bp, 0, 0, at, null);
            var midge = bodies[0];
            var brain = new VanillaAiBrain(midge);
            w.Brains.Add(brain);
            var blocks = Wall(10, 10, 3, true);
            blocks.Add(Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 5, 5, -1));
            var dummy = Spawn(w, 1, 1, new Vector3D(0, 0, -1200), null, blocks.ToArray());
            Info("Midge: " + midge.Blocks.Count + " blocks, mass " + midge.Mass.ToString("F0") + " kg, weapons " + midge.Weapons.Count + ", thrusters " + midge.Thrusters.Count);
            double dmin = double.MaxValue;
            for (int i = 0; i < 60 * 90; i++)
            {
                w.Step();
                double d = (dummy.Position - midge.Position).Length();
                if (Environment.GetEnvironmentVariable("SIM_TRACE") != null && i % 300 == 0) Console.WriteLine("    t=" + (i / 60) + " range " + d.ToString("F0") + " speed " + midge.LinearVelocity.Length().ToString("F1") + " w " + midge.AngularVelocity.Length().ToString("F2") + " target " + (brain.Target != null) + " shots " + sink.Shots + " owner " + SeSim.Api.Detection.EffectiveOwner(dummy) + " minhp " + MinHp(dummy));
                if (i > 60 * 60 && d < dmin) dmin = d;
            }
            double dEnd = (dummy.Position - midge.Position).Length();
            Info("Midge after 90 s: range " + dEnd.ToString("F0") + " m, speed " + midge.LinearVelocity.Length().ToString("F1") + ", shots " + sink.Shots + ", hits " + sink.Hits + ", dummy blocks " + dummy.Blocks.Count + "/301");
            Check(brain.Target == dummy, "vanilla AI acquired the dummy");
            Check(dEnd > 150 && dEnd < 800, "stays at range (" + dEnd.ToString("F0") + " m)");
            Check(sink.Shots > 50 && sink.Hits > 0, "fixed gun fires and hits (shots " + sink.Shots + ", hits " + sink.Hits + ")");
        }

        // =====================================================================================================
        // 12. determinism, 13. speed
        // =====================================================================================================
        static void TestDeterminism()
        {
            var a = RunPirate(99, 60 * 25, false);
            var b = RunPirate(99, 60 * 25, false);
            Check(a.Hash == b.Hash, "same seed, same result: " + a.Hash);
            var c = RunPirate(100, 60 * 25, false);
            Check(a.Hash != c.Hash, "different seed, different result");
        }

        static void TestSpeed()
        {
            var sink = new Sink();
            var w = NewWorld(5, false, sink);
            var wall = Spawn(w, 1, 9, new Vector3D(-8, -8, -400), null, Wall(32, 32, 4, true).ToArray());
            Invulnerable(wall);
            for (int i = 0; i < 4; i++)
            {
                var g = Spawn(w, 0, i, new Vector3D(i * 3, 0, 0), null, Bd("SmallGatlingGun", "", 0, 0, 0));
                g.Weapons[0].ShootOn = true;
            }
            var sw = Stopwatch.StartNew();
            Run(w, 60 * 60);
            sw.Stop();
            Info("60 s with 4 gatlings on a 4096-block wall: " + sw.ElapsedMilliseconds + " ms (" + sink.Shots + " shots, " + sink.Hits + " hits)");
            Check(sw.ElapsedMilliseconds < 3000, "combat engine cost for 60 s with 4 guns firing is below 3 s");
        }
    }
}
