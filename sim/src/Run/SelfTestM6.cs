// M6 acceptance tests (docs/design/control_arch.md section 7 "M6 Assembly"), run on the REAL packaged blueprints with their REAL scripts and Custom Data:
//   python3 -I tools/build_all.py --only Corsair "Corsair Wing" Warden "Warden Wing Test" Leviathan --no-zip
// Run: SimHost.exe selftest m6   (the Leviathan rows only with M6_BIG=1: 8,400 blocks, about 10 s of wall time per simulated minute; skips when a blueprint is missing).
//   1 wiring     [Helm] ShrikeAI and [Staff] StaffAI find exactly the ship's own gyros / thrusters / guns / turrets / batteries / seat / dead-man timer (all names start with the
//                [SH] prefix, nothing of the docked pod or of a clamped drone), the HELMLINK is on, the Staff hears the Helm, the bay PBs see their bays
//   2 manual     a scripted pilot sits, flies, turns and leaves (a Pirate fights the ship): no override fight, the Helm is MANUAL while he sits, the fixed guns stay quiet, the
//                turrets keep killing, AUTO resumes after the release delay and the Helm flies again
//   3 dead-man   Helm PB killed at 15 s while it flies: the Staff clears every override within Trip Time + one step, once, and Timer D (5 s) fires too
//   4 wing       'launch all' on the ships with loaded bays (Corsair Wing, Warden Wing Test): the Helm parks, every bay lets go, the Helm gets its mode back, the drones live and
//                the Staff's roll call counts them; 'wing satellite' puts them on the ring (distance from the ship), 'wing hunt' makes them shoot
// EVIDENCE: the sim's physics, gun, turret, IGC, Timer / Event Controller and merge models (large-grid damage is optimistic, hydrogen unlimited, no conveyor model, RigidLink for
// the connector-docked pod); nothing here was measured in the game. Script state is read by reflection.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;
using SeSim.Api;
using SeSim.Core;

namespace SeSim.Run
{
    public static class SelfTestM6
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }
        static string _repo, _simDir;
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static bool Big { get { return Environment.GetEnvironmentVariable("M6_BIG") != null; } }

        public static int Run(string simDir, string repo)
        {
            _fail = 0; _pass = 0; _simDir = simDir; _repo = repo;
            Console.WriteLine("SelfTestM6 (the assembled warships: Corsair, Warden" + (Big ? ", Leviathan" : "") + " with their real scripts and Custom Data)");
            string bp = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            foreach (string m in new[] { "Corsair", "Corsair Wing", "Warden", "Warden Wing Test", "Pirate-not-needed" })
                if (m != "Pirate-not-needed" && !File.Exists(Path.Combine(bp, m, "bp.sbc")))
                { SelfTestSkips.Note("M6 (build: python3 -I tools/build_all.py --only Corsair \"Corsair Wing\" Warden \"Warden Wing Test\" --no-zip)"); Console.WriteLine("  skipped: blueprint " + m + " missing"); return 0; }
            var ships = new List<string> { "Corsair", "Warden" }; if (Big) ships.Add("Leviathan");
            foreach (string s in ships) TestWiring(s);
            foreach (string s in ships) TestManual(s);
            foreach (string s in ships) TestDeadman(s);
            TestWing("Corsair Wing", 2);
            TestWing("Warden Wing Test", 4);
            if (Big && File.Exists(Path.Combine(bp, "Leviathan Wing Test", "bp.sbc"))) TestWing("Leviathan Wing Test", 8);
            Console.WriteLine(_fail == 0 ? "SelfTestM6 passed (" + _pass + " checks)" : "SelfTestM6 FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        // ------------------------------------------------------------------------------------------------------------------ helpers
        static T Fld<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new InvalidOperationException("script has no field " + name);
            return (T)f.GetValue(o);
        }
        static double Num(string s) { return double.Parse(s, Inv); }

        /// <summary>Our ship (blueprint Custom Data, rigid pod) against an enemy; cmds = alternating time, "Tag:arg".</summary>
        static bool _idleEnemy;
        static Scenario Fight(string a, string enemy, double dist, string pilot, params string[] cmds)
        {
            var sc = new Scenario { Kind = "ship", Name = "ship/" + a + "-vs-" + enemy, StartDistanceMin = dist, StartDistanceMax = dist, MaxTicks = 200 * 60, FaceEnemies = true, DecisiveHoldSeconds = 1000, BlueprintCustomData = true, RigidSubgrids = true };
            sc.Teams.Add(new TeamSpec(0, a)); sc.Teams.Add(new TeamSpec(1, enemy));
            var sa = new ShipSpec { Model = a, Team = 0, Role = "A1", Pilot = pilot };
            for (int i = 0; i + 1 < cmds.Length; i += 2) sa.Commands.Add(new KeyValuePair<double, string>(Num(cmds[i]), cmds[i + 1]));
            var sb = new ShipSpec { Model = enemy, Team = 1, Role = "B1" };
            if (enemy != "Pirate" && !_idleEnemy) ScenarioGen.DefaultCommands(sb);
            sc.Ships.Add(sa); sc.Ships.Add(sb);
            return sc;
        }

        static RunResult Go(Scenario sc, int seed, double seconds, Action<World, ShipInstance, object, object, ScriptHost> tick, bool count = false)
        {
            var s = new SimSettings(); s.MaxTicks = (int)(seconds * 60) + 2; s.BlockAutomation = true;
            if (count) { s.CountInstructions = true; s.InstructionCountMode = "blocks"; }
            var opt = new RunOptions { SimDir = _simDir };
            opt.OnTick = (w, host, ships) =>
            {
                var helm = ships[0].PbsByTag("Helm"); var staff = ships[0].PbsByTag("Staff");
                if (helm.Count == 0 || helm[0].Program == null) return;
                tick(w, ships[0], helm[0].Program, staff.Count > 0 ? staff[0].Program : null, host);
            };
            var r = ScenarioRunner.RunOne(sc, seed, 1UL, s, _repo, opt);
            if (r.Error != null) Console.WriteLine("  ERROR " + r.Error);
            return r;
        }

        static bool AnyOverride(ShipInstance sh)
        {
            foreach (var b in sh.Bodies)
            {
                if (!b.Alive) continue;
                for (int i = 0; i < b.Gyros.Count; i++) if (b.Gyros[i].Override && b.Gyros[i].Working) return true;
                for (int i = 0; i < b.Thrusters.Count; i++) if (b.Thrusters[i].OverrideN > 0 && b.Thrusters[i].Working) return true;
            }
            return false;
        }
        static T FindBlock<T>(ShipInstance sh) where T : class
        {
            foreach (var bd in sh.Bodies) foreach (var k in bd.Blocks) { var t = k.Api as T; if (t != null) return t; }
            return null;
        }
        static int Count(object list) { return list is ICollection ? ((ICollection)list).Count : -1; }
        static string Name(object o) { var t = o as IMyTerminalBlock; return t != null ? t.CustomName : "?"; }
        /// <summary>the blocks of the host ship (not the pod, not a clamped drone) that implement T and carry the [SH] prefix</summary>
        static int Own<T>(ShipInstance sh) where T : class
        {
            int n = 0;
            foreach (var bd in sh.Bodies) foreach (var k in bd.Blocks) { var t = k.Api as T; var tb = k.Api as IMyTerminalBlock; if (t != null && tb != null && tb.CustomName.StartsWith("[SH]")) n++; }
            return n;
        }
        static int BadNames(object list)
        {
            int bad = 0; var l = list as IEnumerable; if (l == null) return -1;
            foreach (var o in l) if (!Name(o).StartsWith("[SH]")) bad++;
            return bad;
        }

        // ------------------------------------------------------------------------------------------------------------------ 1 wiring
        static void TestWiring(string model)
        {
            Console.WriteLine("-- 1 wiring: " + model + " --");
            int gy = 0, th = 0, gn = 0, tu = 0, ba = 0, se = 0, dm = 0, cams = 0, rcs = 0;
            int hg = -1, ht = -1, hn = -1, hu = -1, hb = -1, hs = -1, hd = -1, hc = -1, sg = -1, st = -1, sn = -1, su = -1, bad = 0; bool link = false; int hbN = 0, trips = -1; string txt = "", ctlName = "";
            var sc = Fight(model, "Hornet", 1800, null, "0.5", "Helm:guard");
            Go(sc, 0, 8, (w, sh, helm, staff, host) =>
            {
                if (w.Tick != 7 * 60 || staff == null) return;
                gy = Own<IMyGyro>(sh); th = Own<IMyThrust>(sh); gn = Own<IMyUserControllableGun>(sh); tu = Own<IMyLargeTurretBase>(sh); ba = Own<IMyBatteryBlock>(sh); se = Own<IMyShipController>(sh); cams = Own<IMyCameraBlock>(sh); rcs = Own<IMyRemoteControl>(sh);
                dm = 0; foreach (var bd in sh.Bodies) foreach (var k in bd.Blocks) { var t = k.Api as IMyTimerBlock; if (t != null && t.CustomName.StartsWith("[SH]") && t.CustomName.ToLowerInvariant().Contains("deadman")) dm++; }
                hg = Count(Fld<object>(helm, "_gyros")); ht = Count(Fld<object>(helm, "_thr")); hn = Count(Fld<object>(helm, "_guns")); hu = Count(Fld<object>(helm, "_turrets")); hb = Count(Fld<object>(helm, "_bats"));
                hs = Count(Fld<object>(helm, "_seats")); hd = Count(Fld<object>(helm, "_dm")); hc = Count(Fld<object>(helm, "_cams"));
                sg = Count(Fld<object>(staff, "_gyros")); st = Count(Fld<object>(staff, "_thr")); sn = Count(Fld<object>(staff, "_guns")); su = Count(Fld<object>(staff, "_turrets"));
                bad = 0; foreach (string f in new[] { "_gyros", "_thr", "_guns", "_turrets", "_bats", "_seats", "_dm", "_cams" }) bad += Math.Max(0, BadNames(Fld<object>(helm, f)));
                foreach (string f in new[] { "_gyros", "_thr", "_guns", "_turrets" }) bad += Math.Max(0, BadNames(Fld<object>(staff, f)));
                link = Fld<bool>(helm, "HlkOn"); hbN = Fld<int>(staff, "_hbN"); trips = Fld<int>(staff, "_trips"); txt = Staff(sh); var rc = Fld<IMyTerminalBlock>(helm, "_rc"); ctlName = rc != null ? rc.CustomName : "none";
            });
            Info(model + ": ship has gyros " + gy + " thrusters " + th + " guns " + gn + " turrets " + tu + " batteries " + ba + " seats " + se + " cameras " + cams + " deadman timers " + dm + "; Helm lists " + hg + "/" + ht + "/" + hn + "/" + hu + "/" + hb + "/" + hs + "/" + hd + "/" + hc + ", Staff lists " + sg + "/" + st + "/" + sn + "/" + su + "; reference block '" + ctlName + "'");
            Check(gy > 0 && th > 0 && hg == gy && ht == th && hn == gn - tu && hu == tu && hb == ba && hs == se - rcs && hd == dm && dm == 1 && hc == cams, model + ": the Helm's lists hold exactly the ship's own gyros, thrusters, guns, turrets, batteries, seats, cameras and its dead-man timer");
            Check(sg == gy && st == th && sn == gn - tu && su == tu && bad == 0, model + ": the Staff's lists agree and no listed block lacks the [SH] prefix (no pod or drone block adopted)");
            Check(link && hbN >= 8 && trips == 0 && txt.Contains("HELM  ok"), model + ": HELMLINK on, the Staff has " + hbN + " heartbeats in 7 s, no trip");
        }
        static bool IsDrone(Body b) { foreach (var k in b.Blocks) { var pb = k.Api as SimProgrammableBlock; if (pb != null && pb.CustomNameValue != null && !pb.CustomNameValue.StartsWith("[")) return true; } return false; }
        static string Staff(ShipInstance sh) { var l = sh.PbsByTag("Staff"); if (l.Count == 0) return ""; try { return l[0].Pb.GetSurface(0).GetText(); } catch (Exception) { return ""; } }

        // ------------------------------------------------------------------------------------------------------------------ 2 manual
        static void TestManual(string model)
        {
            Console.WriteLine("-- 2 manual: " + model + " (pilot sits 1-20 s, an idle Hornet 420 / 650 m away) --");
            int okSeeds = 0; const int N = 2;
            for (int seed = 0; seed < N; seed++)
            {
                bool manSeated = true, quietGuns = true, manAfter = true, flyAfter = false, modeBack = false; int fight = -1; int blocksA = -1, blocksB = -1; string modeAfter = ""; int turretsFired = 0;
                _idleEnemy = true;
                var sc = Fight(model, "Hornet", model == "Corsair" ? 420 : 650, "1:sit;4:move 0 0 -1;7:stop;9:rot 0 1;12:stop;20:leave", "0.5", "Helm:hunt");
                _idleEnemy = false;
                var r = Go(sc, seed, 36, (w, sh, helm, staff, host) =>
                {
                    long t = w.Tick; var pi = sh.Pilot;
                    double sec = t / 60.0;
                    int eb = 0; foreach (var b0 in w.Bodies) if (b0.Alive && b0.ShipIndex == 1) eb += b0.Blocks.Count;
                    if (sec >= 2.0 && sec < 20.0 && !Fld<bool>(helm, "_man")) manSeated = false;
                    if (sec >= 2.0 && sec < 20.0 && t % 30 == 0)
                        foreach (var bd in sh.Bodies) foreach (var k in bd.Blocks) { var g = k.Api as SimUserGun; if (g != null && g.Shoot && !(g is IMyLargeTurretBase)) quietGuns = false; }
                    if (t == 2 * 60) blocksA = eb;
                    if (t == 19 * 60) blocksB = eb;
                    if (t == 21 * 60 + 40) { manAfter = Fld<bool>(helm, "_man"); modeAfter = Fld<object>(helm, "_mode").ToString(); }
                    if (t == 26 * 60) { flyAfter = AnyOverride(sh); modeBack = Fld<object>(helm, "_mode").ToString() == "HUNT" && !Fld<bool>(helm, "_man"); }
                    if (t == 35 * 60 && pi != null) fight = pi.OverrideFightTicks;
                });
                bool o = manSeated && quietGuns && !manAfter && fight == 0 && blocksA > 0 && blocksB < blocksA && modeBack;
                if (o) okSeeds++;
                Info("seed " + seed + ": MANUAL the whole seat time " + manSeated + ", fixed guns quiet " + quietGuns + ", override-fight ticks " + fight + ", enemy blocks " + blocksA + " -> " + blocksB + " (only turrets and point defence shoot), 1.7 s after the release manual=" + manAfter + ", mode " + modeAfter + "; at 26 s Helm flying again " + flyAfter + " (a ship with nothing left to fight idles), mode back " + modeBack);
            }
            Check(okSeeds == N, model + ": manual flight with no override fight, quiet fixed guns, working turrets, AUTO resumes (" + okSeeds + "/" + N + " seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 3 dead-man
        static void TestDeadman(string model)
        {
            Console.WriteLine("-- 3 dead-man: " + model + " (Helm PB killed at 15 s) --");
            int ok = 0; double worst = 0; const int N = 2;
            for (int seed = 0; seed < N; seed++)
            {
                bool pre = false, re = false; long clearAt = -1; int trips = -1; string txt = ""; int expiries = -1; SimTimer tm = null;
                var sc = Fight(model, "Hornet", 1500, "15:killpb Helm", "0.5", "Helm:hunt");
                Go(sc, seed, 24, (w, sh, helm, staff, host) =>
                {
                    if (staff == null) return;
                    if (tm == null) tm = FindBlock<SimTimer>(sh);
                    long t = w.Tick, kill = 15 * 60;
                    if (t == kill - 1) pre = AnyOverride(sh);
                    if (t >= kill && clearAt < 0 && !AnyOverride(sh)) clearAt = t - kill;
                    if (clearAt >= 0 && t > kill && AnyOverride(sh)) re = true;
                    if (t == 23 * 60) { trips = Fld<int>(staff, "_trips"); txt = Staff(sh); expiries = tm != null ? tm.Expiries : -1; }
                });
                double sec = clearAt / 60.0; worst = Math.Max(worst, sec);
                bool o = pre && clearAt >= 0 && sec <= 1.7 && trips == 1 && !re && txt.Contains("HELM LOST") && expiries >= 1;
                if (o) ok++;
                Info("seed " + seed + ": overrides on at the kill " + pre + ", all released " + sec.ToString("F2") + " s after it, trips " + trips + ", re-asserted " + re + ", Timer D expiries " + expiries);
            }
            Check(ok == N, model + ": Helm killed while flying: the Staff releases every override within 1.7 s (worst " + worst.ToString("F2") + " s), once, 'HELM LOST', Timer D fires (" + ok + "/" + N + " seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 4 wing
        static void TestWing(string model, int bays)
        {
            Console.WriteLine("-- 4 wing: " + model + " (launch all at 4 s while the Helm hunts, satellite at 30 s, hunt at 50 s) --");
            int ok = 0; const int N = 2;
            for (int seed = 0; seed < N; seed++)
            {
                string bayTxt = "", modeBack = "", note = ""; int alive = 0, left = 0; double ringMin = 1e9, ringMax = 0; double firstShot = -1; int lchOn = 0;
                var sc = Fight(model, "Pirate", 2400, null, "0.5", "Helm:hunt", "4", "Staff:launch all", "30", "Staff:wing satellite", "50", "Staff:wing hunt");
                var r = Go(sc, seed, 70, (w, sh, helm, staff, host) =>
                {
                    if (staff == null) return;
                    long t = w.Tick;
                    if (t == 20 * 60) { bayTxt = Staff(sh); modeBack = Fld<object>(helm, "_mode").ToString(); note = Fld<string>(staff, "_rqNote"); lchOn = Fld<bool>(staff, "_lchOn") ? 1 : 0; }
                    if (t == 29 * 60) { var bd = Fld<IDictionary>(staff, "_bay"); foreach (DictionaryEntry e in bd) { string stt = Fld<string>(e.Value, "St"); if (stt == "COOLDOWN" || stt == "EMPTY") left++; } }
                    if (t == 29 * 60) { var m = Fld<IDictionary>(staff, "_mates"); alive = m.Count; }
                    if (t == 48 * 60)
                    {
                        // distance of every drone body from the ship
                        foreach (var b0 in w.Bodies)
                            if (b0.Alive && b0.ShipIndex == 0 && b0 != sh.Main && IsDrone(b0)) { double d = Vector3D.Distance(b0.Position, sh.Main.Position); ringMin = Math.Min(ringMin, d); ringMax = Math.Max(ringMax, d); }
                    }
                });
                bool o = left == bays && modeBack == "HUNT" && alive >= bays && lchOn == 0 && ringMin > 20 && ringMax < 400;
                if (o) ok++;
                Info("seed " + seed + ": bays let go " + left + "/" + bays + " by 29 s, Helm mode at 20 s " + modeBack + " (launch over " + (lchOn == 0) + "), roll call " + alive + ", drones " + ringMin.ToString("F0") + "-" + ringMax.ToString("F0") + " m from the ship at 48 s (satellite since 30 s); Staff note '" + note + "'");
                if (seed == 0) Console.WriteLine("  ---- Staff PB screen at 20 s ----\n  | " + bayTxt.Replace("\n", "\n  | ") + "\n  ----");
            }
            Check(ok == N, model + ": 'launch all' on a hunting ship releases every bay, hands the Helm its mode back, the wing lives and flies the ring (" + ok + "/" + N + " seeds)");
        }
    }
}
