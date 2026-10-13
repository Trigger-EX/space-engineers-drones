// M3 acceptance tests (docs/design/control_arch.md section 7 "M3 Multi-weapon fire control"), run on the REAL scripts/ShrikeAI.cs (BATTERY v1.0) inside the generated
// throwaway test ships (python3 -I tools/build_all.py --only "Shrike Test Rocket" "Shrike Multi Test" "Shrike Basilisk Test"):
//   Shrike Multi Test      Hornet hull, gatling [B1] + SmallMissileLauncher [B2] + SmallRailgun [B3]
//   Shrike Test Rocket     gatling [B1] + launcher [B2]
//   Shrike Basilisk Test   Basilisk hull (large grid, 4 launchers [B2], gatling turret) running ShrikeAI
// Run: SimHost.exe selftest m3   (also part of `selftest all`; skips when the blueprints are missing).
//   1 classes     three batteries detected (BALL / ROCK / RAIL), every battery fires in a fight (rail through the confirmed-shot state machine), no script exception
//   2 primary     primary choice follows the range (rail at long range, gatling close) and does not flap (hysteresis + 3 s dwell)
//   3 solver      the rocket flight model of BATTERY (RkP) against a 60 Hz step-by-step flight (< 1 m); the other classes use the shipped Lead() (see tools/tests/test_battery_solvers.py)
//   c range       rail present + an enemy gatling ship: median fight distance > 900 m; rockets + gatling only: 450-700 m (10 seeds each)
//   d turret-only a turret ship (Basilisk hull) with a small drone beside a large target: every fixed battery refuses the drone, orientation stays on the large target (5 seeds)
//   e max turn    MaxTurn calibration on the large ship within 20 % of the analytic gyro torque / inertia value (3 seeds)
// (a) the lead bench numbers per class and (b) the A/B damage comparisons are command-line runs: see tools/lead_bench/classes.sh and the M3 commit message.
// EVIDENCE: all of it is the sim's model (rocket flight, railgun capacitor, gyro torque); nothing was measured in the game. Script state is read by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using SeSim.Api;
using SeSim.Combat;
using SeSim.Core;

namespace SeSim.Run
{
    public static class SelfTestM3
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }
        static string _repo, _simDir;
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static int Run(string simDir, string repo)
        {
            _fail = 0; _pass = 0; _simDir = simDir; _repo = repo;
            Console.WriteLine("SelfTestM3 (BATTERY v1.0: classes, primary choice, rocket solver, standoff, turret-only mode, MaxTurn calibration)");
            string bp = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            foreach (string m in new[] { "Shrike Multi Test", "Shrike Test Rocket", "Shrike Basilisk Test", "Hornet" })
                if (!File.Exists(Path.Combine(bp, m, "bp.sbc")))
                { SelfTestSkips.Note("M3 (build: python3 -I tools/build_all.py --only \"Shrike Test Rocket\" \"Shrike Multi Test\" \"Shrike Basilisk Test\" Hornet --no-zip)"); Console.WriteLine("  skipped: blueprint " + m + " missing"); return 0; }
            string probe = Environment.GetEnvironmentVariable("M3_PROBE");
            if (!string.IsNullOrEmpty(probe)) { Probe(probe); return 0; }
            TestClasses();
            TestPrimary();
            TestRocketModel();
            TestRange();
            TestTurretOnly();
            TestMaxTurn();
            Console.WriteLine(_fail == 0 ? "SelfTestM3 passed (" + _pass + " checks)" : "SelfTestM3 FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        // ------------------------------------------------------------------------------------------------------------------ helpers
        static T Fld<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new InvalidOperationException("script has no field " + name);
            return (T)f.GetValue(o);
        }
        static object BatOf(object helm, int i) { return ((Array)Fld<object>(helm, "_bat")).GetValue(i); }
        static double Num(string s) { return double.Parse(s, Inv); }

        /// <summary>Scenario from names (ScenarioGen.Custom) with the Helm of ship 0 started in HUNT at 0.5 s and the given Helm Custom Data.</summary>
        static Scenario Duel(string name, double dist, string helmData, bool face = true)
        {
            var sc = ScenarioGen.Custom(name, Path.Combine(Path.Combine(_repo, "out"), "Blueprints"));
            if (sc == null) throw new InvalidOperationException("no scenario " + name);
            sc.StartDistanceMin = dist; sc.StartDistanceMax = dist; sc.FaceEnemies = face; sc.DecisiveHoldSeconds = 1000;
            var sa = sc.Ships[0];
            sa.Commands.Clear(); sa.Commands.Add(new KeyValuePair<double, string>(0.5, "Helm:hunt"));
            sa.PbCustomData["Helm"] = "[Shrike]\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=IDLE\n" + (helmData ?? "");
            return sc;
        }

        static RunResult Go(Scenario sc, int seed, double seconds, Action<World, ShipInstance, object, List<ShipInstance>> tick)
        {
            var s = new SimSettings(); s.MaxTicks = (int)(seconds * 60) + 2; s.BlockAutomation = true;
            var opt = new RunOptions { SimDir = _simDir };
            opt.OnTick = (w, host, ships) =>
            {
                var helm = ships[0].PbsByTag("Helm");
                _last = ships[0];
                if (helm.Count == 0 || helm[0].Program == null) return;
                tick(w, ships[0], helm[0].Program, ships);
            };
            var r = ScenarioRunner.RunOne(sc, seed, 1UL, s, _repo, opt);
            if (r.Error != null) Console.WriteLine("  ERROR " + r.Error);
            return r;
        }

        static double Dist(ShipInstance a, ShipInstance b) { return Vector3D.Distance(a.Main.Position, b.Main.Position); }
        static bool Alive(ShipInstance s) { return !s.Destroyed && !s.Disabled; }
        static double Median(List<double> l) { if (l.Count == 0) return double.NaN; l.Sort(); return l[l.Count / 2]; }
        static int Shots(ShipInstance sh, Func<WeaponUnit, bool> pick)
        {
            int n = 0;
            foreach (var bd in sh.Bodies) foreach (var u in bd.Weapons) if (pick(u)) n += u.ShotsFired;
            return n;
        }

        // ------------------------------------------------------------------------------------------------------------------ 1 classes
        static void TestClasses()
        {
            Console.WriteLine("-- 1 classes: gatling [B1], launcher [B2], railgun [B3] on the Hornet hull; every battery fires (a held Hornet at 450 m, Standoff By Weapons off) --");
            int ok = 0, n = 3;
            for (int seed = 0; seed < n; seed++)
            {
                string cls = ""; int cnt = 0; string err = "";
                var sc = Duel("1v1/Shrike Multi Test-vs-Hornet", 700, "[Shrike]\nStandoff Range=450\n[Battery]\nStandoff By Weapons=false\n");
                sc.Ships[1].Commands.Clear();
                var r = Go(sc, seed, 70, (w, sh, helm, ships) =>
                {
                    if (w.Tick == 60)
                        for (int i = 0; i < 4; i++) { var b = BatOf(helm, i); cls += Fld<string>(b, "Cls") + (i < 3 ? "/" : ""); cnt += Fld<System.Collections.IList>(b, "G").Count; }
                    if (w.Tick == 70 * 60) err = Fld<string>(helm, "_crash");
                });
                var ship = r.Ships[0];
                int sBall = Shots(_last, u => u.Mode == ProjectileSystem.ModeBullet), sRock = Shots(_last, u => u.Mode == ProjectileSystem.ModeRocket), sRail = Shots(_last, u => u.Mode == ProjectileSystem.ModeSlug);
                Info("seed " + seed + ": classes " + cls + " (" + cnt + " guns), shots gatling " + sBall + " rocket " + sRock + " rail " + sRail + ", damage dealt " + ship.DamageDealt.ToString("0") + ", script error '" + err + "', exceptions " + ship.ScriptExceptions);
                if (cls == "BALL/ROCK/RAIL/-" && cnt == 3 && sBall > 0 && sRock > 0 && sRail > 0 && err == "" && ship.ScriptExceptions == 0) ok++;
            }
            Check(ok == n, "three batteries detected (BALL/ROCK/RAIL on B1..B3) and each one fired, no script error (" + ok + "/" + n + " seeds)");
        }
        static ShipInstance _last;

        // ------------------------------------------------------------------------------------------------------------------ 2 primary
        static void TestPrimary()
        {
            Console.WriteLine("-- 2 primary choice follows the range, no flapping (a held Hornet at 1100 m, moved to 350 m at 25 s) --");
            int ok = 0, n = 3;
            for (int seed = 0; seed < n; seed++)
            {
                bool farRail = false, nearBall = false; int switches = 0, last = -1; double lastT = -99, minGap = 99;
                var sc = Duel("1v1/Shrike Multi Test-vs-Hornet", 1100, "[Battery]\nStandoff By Weapons=false\n");
                sc.Ships[1].Commands.Clear();
                Go(sc, seed, 60, (w, sh, helm, ships) =>
                {
                    int p = Fld<int>(helm, "_prim"); double t = w.Tick / 60.0;
                    if (t > 3 && p != last) { if (last >= 0) { switches++; minGap = Math.Min(minGap, t - lastT); } last = p; lastT = t; }
                    if (t > 3 && t < 25 && p == 2) farRail = true;
                    if (w.Tick == 25 * 60) { var e = ships[1].Main; e.Position = sh.Main.Position + sh.Main.RotMatrix.Forward * 350; e.LinearVelocity = sh.Main.LinearVelocity; }
                    if (t > 28 && p == 0) nearBall = true;
                });
                Info("seed " + seed + ": rail was primary at long range " + farRail + ", gatling primary at 350 m " + nearBall + ", switches " + switches + ", shortest gap " + (minGap > 90 ? "-" : minGap.ToString("0.0")) + " s");
                if (farRail && nearBall && switches <= 6 && minGap >= 2.9) ok++;
            }
            Check(ok == n, "rail primary at long range, gatling close, <= 6 switches in 60 s and never two within the 3 s dwell (" + ok + "/" + n + " seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 3 rocket model
        static void TestRocketModel()
        {
            Console.WriteLine("-- 3 rocket flight model RkP against a 60 Hz step-by-step flight (launch at ship velocity + 100 m/s, +600 m/s^2 along the barrel up to 200 m/s, velocity bends to the barrel) --");
            // the script object of a ship (any Helm), only for the method
            object helm = null;
            var sc = Duel("1v1/Shrike Test Rocket-vs-Hornet", 900, "");
            Go(sc, 0, 1, (w, sh, h, ships) => { if (helm == null) helm = h; });
            var m = helm.GetType().GetMethod("RkP", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            double worst = 0; int cases = 0;
            foreach (double ua in new[] { 0.0, 60, -40 })
                foreach (double us in new[] { 0.0, 30, 80 })
                    foreach (double T in new[] { 0.05, 0.5, 2.0, 4.0 })
                    {
                        // truth: barrel along +x, ship moves (ua along, us across); 60 Hz Euler with the speed cap as the bench/sim do
                        Vector3D d = new Vector3D(1, 0, 0), v = new Vector3D(ua, us, 0) + d * 100, p = Vector3D.Zero;
                        int steps = (int)Math.Round(T * 600);
                        double dt = 1.0 / 600;
                        for (int i = 0; i < steps; i++)
                        {
                            double spd = v.Length();
                            v += d * 600 * dt; if (v.Length() > 200) v = Vector3D.Normalize(v) * 200;
                            p += v * dt;
                        }
                        var args = new object[] { T, ua, us, 0.0, 0.0, 0.0 };
                        m.Invoke(helm, args);
                        double s = (double)args[3], q = (double)args[4];
                        // the model: s along the barrel, q = drift towards the barrel side (sideways drift of the launch velocity: ship velocity across the barrel)
                        double errAlong = Math.Abs(s - p.X), errSide = Math.Abs(q - p.Y);
                        worst = Math.Max(worst, Math.Max(errAlong, errSide)); cases++;
                    }
            Info("36 cases (ship speed along -40..60 m/s, across 0..80 m/s, flight 0.05..4 s): worst position difference to the stepped flight " + worst.ToString("0.00") + " m");
            Check(worst < 1.5, "RkP within 1.5 m of the step-by-step flight in " + cases + " cases");
        }

        // ------------------------------------------------------------------------------------------------------------------ c range
        static double FightMedian(string ours, bool chase, int seeds, out string perSeed)
        {
            var meds = new List<double>();
            for (int seed = 0; seed < seeds; seed++)
            {
                var d = new List<double>(); bool engaged = false;
                var sc = Duel("1v1/" + ours + "-vs-Hornet", 1500, "");
                sc.Ships[1].Commands.Clear(); if (chase) sc.Ships[1].Commands.Add(new KeyValuePair<double, string>(0.5, "hunt"));
                Go(sc, seed, 150, (w, sh, helm, ships) =>
                {
                    if (w.Tick % 60 != 0 || !ships[0].CombatCapable || !ships[1].CombatCapable) return;
                    double dd = Dist(ships[0], ships[1]);
                    if (!engaged && Shots(ships[0], u => true) > 0) engaged = true;       // the fight starts with our first shot (the approach is not part of it)
                    if (engaged) d.Add(dd);
                });
                if (d.Count > 0) meds.Add(Median(d));
            }
            meds.Sort();
            perSeed = string.Join(" ", meds.ConvertAll(x => x.ToString("0")).ToArray());
            return meds.Count > 0 ? meds[meds.Count / 2] : double.NaN;
        }

        static void TestRange()
        {
            Console.WriteLine("-- c standoff by weapons: the median fight distance from our first shot on (10 seeds each; the enemy Hornet holds its position so the distance is the ship's own choice) --");
            string ps;
            double med = FightMedian("Shrike Multi Test", false, 10, out ps);
            Info("rail present, enemy gatling ship (held Hornet): per-seed medians " + ps + " -> median " + med.ToString("0") + " m");
            Check(med > 900, "rail present, enemy gatling ship: median fight distance " + med.ToString("0") + " m > 900 m");
            med = FightMedian("Shrike Test Rocket", false, 10, out ps);
            Info("rockets + gatling only, enemy Hornet (held): per-seed medians " + ps + " -> median " + med.ToString("0") + " m");
            Check(med >= 450 && med <= 700, "rockets + gatling only: median fight distance " + med.ToString("0") + " m in 450-700 m");
            med = FightMedian("Shrike Multi Test", true, 10, out ps);
            Info("(information) rail present against a HUNTING Hornet that closes in on its own 350 m standoff: per-seed medians " + ps + " -> median " + med.ToString("0") + " m");
            med = FightMedian("Shrike Test Rocket", true, 10, out ps);
            Info("(information) rockets + gatling against a HUNTING Hornet: per-seed medians " + ps + " -> median " + med.ToString("0") + " m");
        }

        // ------------------------------------------------------------------------------------------------------------------ d turret-only
        // The Helm only learns of a second target through its cameras, the AI blocks and turret designators; the Basilisk hull's turret shoots the drone itself and never reports it.
        // So the drone is reported to the script as a perfect sighting every 0.1 s (what a designator or the Staff would send): a Track made by reflection with the drone's true position.
        static void TestTurretOnly()
        {
            Console.WriteLine("-- d turret-only mode: a drone the fixed batteries cannot hit is left to the turrets, the ship stays on the large target (5 seeds, 10 s window) --");
            int ok = 0, n = 5, flips = 0;
            for (int seed = 0; seed < n; seed++)
            {
                int bigOn = 0, allOn = 0, bigOff = 0, refused = 0, samples = 0, turretLost = 0;
                for (int pass = 0; pass < 2; pass++)       // pass 0: Turret Only AUTO (this ship has a turret), pass 1: OFF (the drone competes for the target)
                {
                    var sc = Duel("1v1/Shrike Basilisk Test-vs-Pirate+Gnat", 1100, pass == 0 ? "" : "[Battery]\nTurret Only (AUTO/ON/OFF)=OFF\n");
                    object trk = null, pir = null; var trackType = (Type)null; long id = 987654321L; var rnd = new Random(seed * 7 + 1); double segEnd = 0; double ax = 0, ay = 0, ox = 0, oy = 0, vx = 0, vy = 0; bool flip = true;
                    Go(sc, seed, 40, (w, sh, helm, ships) =>
                    {
                        double now = Fld<double>(helm, "_now");
                        if (trackType == null) { trackType = helm.GetType().GetNestedType("Track", BindingFlags.NonPublic | BindingFlags.Public); trk = Activator.CreateInstance(trackType); trackType.GetField("Id").SetValue(trk, id); trackType.GetField("Type").SetValue(trk, VRage.Game.ModAPI.Ingame.MyDetectedEntityType.SmallGrid); }
                        if (w.Tick > 120 && w.Tick % 6 == 0 && Alive(ships[1]))
                        {   // the large target, reported the same way (the turrets of this hull often pick the real Gnat first)
                            if (pir == null) { pir = Activator.CreateInstance(trackType); trackType.GetField("Id").SetValue(pir, id + 1); trackType.GetField("Type").SetValue(pir, VRage.Game.ModAPI.Ingame.MyDetectedEntityType.LargeGrid); }
                            trackType.GetMethod("Set").Invoke(pir, new object[] { ships[1].Main.Position, ships[1].Main.LinearVelocity, 35.0, now, "SIM" });
                            ((System.Collections.IDictionary)Fld<object>(helm, "_contacts"))[id + 1] = pir;
                        }
                        if (w.Tick > 120)
                        {   // a drone 300 m ahead that jinks like the vanilla AI's evasive option (random 35 m/s^2 pushes of 0.12-0.3 s, each followed by the opposite push): no gun can hit it
                            if (now >= segEnd) { segEnd = now + 0.12 + 0.18 * rnd.NextDouble(); flip = !flip; if (!flip) { double an = rnd.NextDouble() * 2 * Math.PI; ax = 35 * Math.Cos(an); ay = 35 * Math.Sin(an); } else { ax = -ax; ay = -ay; } }   // (a push and the opposite push: the velocity stays small, the acceleration never settles)
                            vx += ax / 60; vy += ay / 60; ox += vx / 60; oy += vy / 60;
                            if (w.Tick % 6 == 0)
                            {
                                var m = sh.Main.RotMatrix;
                                Vector3D pos = sh.Main.Position + m.Forward * 300 + m.Right * ox + m.Up * oy, vel = sh.Main.LinearVelocity + m.Right * vx + m.Up * vy;
                                trackType.GetMethod("Set").Invoke(trk, new object[] { pos, vel, 2.0, now, "SIM" });
                                ((System.Collections.IDictionary)Fld<object>(helm, "_contacts"))[id] = trk;
                            }
                        }
                        if (w.Tick < 5 * 60 || w.Tick >= 15 * 60) return;
                        object tgt = Fld<object>(helm, "_tgt");
                        bool big = tgt != null && Fld<double>(tgt, "Radius") > 8;
                        if (pass == 0) { allOn++; if (big) bigOn++; } else if (big) bigOff++;
                        if (pass == 0 && w.Tick % 30 == 0)
                        {
                            var m = helm.GetType().GetMethod("BatHope", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            var to = helm.GetType().GetMethod("BatTO", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            if (!(bool)to.Invoke(helm, null)) turretLost++;       // the Pirate shot the turret off: the mode is (correctly) off
                            else { samples++; if ((double)m.Invoke(helm, new[] { trk }) > 0) refused++; }
                            if (Environment.GetEnvironmentVariable("M3_DBG") == seed.ToString() && samples % 2 == 1)
                            {
                                var ph = helm.GetType().GetMethod("BatPh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); var bn = helm.GetType().GetMethod("BatN", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                double rr = Vector3D.Distance(sh.Main.Position, sh.Main.Position + sh.Main.RotMatrix.Forward * 300);
                                var s = "  dbg t " + (w.Tick / 60.0).ToString("0.0") + " drone r=" + Fld<double>(trk, "Radius").ToString("0.0") + " accMag " + Fld<double>(trk, "AccMag").ToString("0.0");
                                for (int i = 0; i < 4; i++) { var bb = BatOf(helm, i); s += " | B" + (i + 1) + " n" + bn.Invoke(helm, new[] { bb }) + " ph " + ((double)ph.Invoke(helm, new object[] { bb, rr, trk })).ToString("0.00"); }
                                { var rcF = Fld<IMyShipController>(helm, "_rc"); if (rcF == null) return; var P = trackType.GetMethod("P"); Vector3D pp = (Vector3D)P.Invoke(trk, new object[] { now }); double r2 = Vector3D.Distance(pp, rcF.GetPosition()); s += " | r2 " + r2.ToString("0"); for (int i = 0; i < 4; i++) { var bb = BatOf(helm, i); s += " ph" + (i + 1) + "=" + ((double)ph.Invoke(helm, new object[] { bb, r2, trk })).ToString("0.00") + "/n" + bn.Invoke(helm, new[] { bb }); } }
                                s += " | TO " + to.Invoke(helm, null) + " turrets " + ((System.Collections.IList)Fld<object>(helm, "_turrets")).Count + " | hope " + ((double)m.Invoke(helm, new[] { trk })).ToString("0") + " Hl " + (Fld<double>(trk, "Hl") - now).ToString("0.0") + " tgtR " + (Fld<object>(helm, "_tgt") != null ? Fld<double>(Fld<object>(helm, "_tgt"), "Radius").ToString("0") : "-");
                                Console.WriteLine(s);
                            }
                        }
                    });
                }
                if (bigOff < allOn * 0.9) flips++;
                Info("seed " + seed + ": large target kept " + bigOn + "/" + allOn + " ticks with turret-only mode (without it " + bigOff + "/" + allOn + "), drone refused by every fixed battery at " + refused + "/" + samples + " samples" + (turretLost > 0 ? " (the turret was lost in " + turretLost + " more: the mode is off then)" : ""));
                if (allOn > 0 && bigOn >= allOn * 0.9 && samples >= 5 && refused >= samples * 0.8) ok++;
            }
            Check(ok == n, "drone refused by all fixed batteries, orientation kept on the large target for >= 90 % of the 10 s window (" + ok + "/" + n + " seeds; with the mode off the ship turned to the drone in " + flips + " of them)");
        }

        // ------------------------------------------------------------------------------------------------------------------ e max turn
        static void TestMaxTurn()
        {
            Console.WriteLine("-- e MaxTurn calibration against the analytic gyro torque / inertia (3 seeds) --");
            // In this sim (the game's override ramp) the rate controller accelerates at most 0.5 x (command - rate) rad/s^2, so a strong-gyro ship (the Basilisk at full gyro power) is ramp-limited and torque
            // does not show: the analytic reference holds for a ship whose gyros are the limit. So the Basilisk's gyro power is set to 5 % (a heavy ship with weak gyros: torque/inertia ~ 0.5 rad/s^2).
            int ok = 0, n = 3;
            for (int seed = 0; seed < n; seed++)
            {
                double mt = 0, an = 0; bool done = false, turned = false, powered = false; int axis = -1; Vector3D wPeak = Vector3D.Zero;
                var sc = Duel("1v1/Shrike Basilisk Test-vs-Hornet", 900, "");
                sc.Ships[1].Commands.Clear();
                Go(sc, seed, 80, (w, sh, helm, ships) =>
                {
                    var b = sh.Main;
                    foreach (var g in b.Gyros) g.Power = 0.05f;   // (a heavy ship with weak gyros)
                    if (!turned && Fld<object>(helm, "_tgt") != null)
                    {   // the first lock: turn the ship 100 degrees about its up axis, so the script has a big slew to make
                        turned = true;
                        MatrixD delta = MatrixD.CreateFromAxisAngle(b.RotMatrix.Up, 100 * Math.PI / 180);
                        b.Rotation = QuaternionD.CreateFromRotationMatrix(b.RotMatrix * delta); b.Rotation.Normalize(); b.UpdateWorldMatrix(); b.AngularVelocity = Vector3D.Zero;
                    }
                    bool d = Fld<bool>(helm, "_caDone");
                    if (!d && turned) { Vector3D wl = Vector3D.TransformNormal(b.AngularVelocity, b.InvWorldRot); if (wl.Length() > wPeak.Length()) wPeak = wl; }
                    if (d && !done)
                    {
                        done = true; mt = Fld<double>(helm, "MaxTurn");
                        double torque = 0; foreach (var g in b.Gyros) if (g.Working) torque += g.MaxForce * g.Power;
                        var it = b.InertiaLocal; double[] inertia = { it.M11, it.M22, it.M33 };
                        axis = Math.Abs(wPeak.X) >= Math.Abs(wPeak.Y) && Math.Abs(wPeak.X) >= Math.Abs(wPeak.Z) ? 0 : Math.Abs(wPeak.Y) >= Math.Abs(wPeak.Z) ? 1 : 2;
                        an = 0.85 * Math.Sqrt(1.2 * torque / inertia[axis]);
                    }
                });
                double dev = an > 0 ? Math.Abs(mt - an) / an : 9;
                Info("seed " + seed + ": calibrated=" + done + ", MaxTurn " + mt.ToString("0.000") + " rad/s, analytic 0.85*sqrt(2*0.6*torque/inertia) about " + "XYZ"[Math.Max(axis, 0)] + " = " + an.ToString("0.000") + " (" + (dev * 100).ToString("0.0") + " % off)");
                if (done && dev < 0.2) ok++;
            }
            Check(ok == n, "MaxTurn within 20 % of the analytic value (" + ok + "/" + n + " seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ probe (development)
        // M3_PROBE="scenario|seed|seconds|every|Helm custom data (\n)"  prints the Helm's battery state every <every> s
        static void Probe(string spec)
        {
            var p = spec.Split('|');
            var sc = Duel(p[0], p.Length > 5 ? Num(p[5]) : 1200, p.Length > 4 ? p[4].Replace("\\n", "\n") : "");
            if (p.Length > 6 && p[6] == "hold") sc.Ships[1].Commands.Clear();
            int seed = int.Parse(p[1]); double secs = Num(p[2]), every = Num(p[3]);
            Go(sc, seed, secs, (w, sh, helm, ships) =>
            {
                if (w.Tick % (int)(every * 60) != 0) return;
                var sb = new System.Text.StringBuilder();
                sb.Append("t ").Append((w.Tick / 60.0).ToString("0.0")).Append(" prim B").Append(Fld<int>(helm, "_prim") + 1).Append(" so ").Append(Fld<double>(helm, "Standoff").ToString("0"));
                for (int i = 0; i < 4; i++) { var b = BatOf(helm, i); if (Fld<System.Collections.IList>(b, "G").Count == 0) continue; sb.Append(" | B").Append(i + 1).Append(' ').Append(Fld<string>(b, "Cls")).Append(" sc ").Append(Fld<double>(b, "Sc").ToString("0")).Append(" rdy ").Append((Fld<double>(b, "Rdy") - Fld<double>(helm, "_now")).ToString("0.0")).Append(" want ").Append(Fld<bool>(b, "Want") ? 1 : 0); }
                var tg = Fld<object>(helm, "_tgt");
                for (int k = 1; k < ships.Count; k++) if (Alive(ships[k])) sb.Append(" | dist").Append(k).Append(' ').Append(Dist(ships[0], ships[k]).ToString("0"));
                { var cs = (System.Collections.IDictionary)Fld<object>(helm, "_contacts"); sb.Append(" | contacts"); foreach (System.Collections.DictionaryEntry kv in cs) sb.Append(" r").Append(Fld<double>(kv.Value, "Radius").ToString("0.0")).Append(Fld<string>(kv.Value, "Src")); }
                if (tg != null) sb.Append(" | tgt r ").Append(Fld<double>(tg, "Radius").ToString("0.0"));
                int sBall = Shots(ships[0], u => u.Mode == ProjectileSystem.ModeBullet), sRock = Shots(ships[0], u => u.Mode == ProjectileSystem.ModeRocket), sRail = Shots(ships[0], u => u.Mode == ProjectileSystem.ModeSlug);
                foreach (var bd in ships[0].Bodies) foreach (var u in bd.Weapons) if (u.Mode == ProjectileSystem.ModeSlug) sb.Append(" | cap ").Append((u.CapacitorMWh / Math.Max(u.CapacitorFull, 1e-9) * 100).ToString("0")).Append("% sup ").Append(u.Sink != null ? u.Sink.SuppliedRatio.ToString("0.00") : "-").Append(" pend ").Append(u.PendingShot ? 1 : 0).Append(" reload ").Append(Math.Max(0, u.ReloadUntil - w.Time).ToString("0.0"));
                sb.Append(" | shots ").Append(sBall).Append('/').Append(sRock).Append('/').Append(sRail).Append(" err '").Append(Fld<string>(helm, "_crash")).Append('\'');
                Console.WriteLine(sb.ToString());
            });
        }
    }
}
