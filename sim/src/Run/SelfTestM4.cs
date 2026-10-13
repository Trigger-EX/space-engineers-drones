// M4 acceptance tests (docs/design/control_arch.md section 7 "M4 Staff wing + mothership link"), run on the REAL scripts/StaffAI.cs + ShrikeAI.cs (the ship
// "Shrike Wing Test": two PBs and an antenna) and the REAL HornetDroneAI.cs (the wing):
//   python3 -I tools/build_all.py --only "Shrike Wing Test" Hornet --no-zip
// Run: SimHost.exe selftest m4   (also part of `selftest all`; skips when a blueprint is missing). The ring / follow rows of the acceptance (6 Hornets, 4 Basilisks) are
// the command-line runs `follow-test --owner ship` (FollowTest.cs, see docs/follow-mode.md).
//   1 hunt      the wing (satellite ring, blind: no camera / sensor / AI-block search) learns the target only through the Staff (Helm lock -> .H2S -> Staff -> HORNET_TGT; the Helm's own IGC sharing is switched off in these runs);
//               an unarmed Hornet stands 450 m ahead, the Helm's manual 'lock' takes it, 'wing hunt' goes out as the Staff has the lock: first drone shot < 3 s later (5 seeds)
//   2 flips     wing hunt -> every drone HUNT; wing rtb -> RTB; wing satellite -> FOLLOW and back on a ring round the ship (the FLEET block's default 'Allowed Commands' lacks
//               'satellite': it works because the Staff sends it on the per-type _CMD); wing follow; roll call counts the 6 drones; page 2 text (3 seeds)
//   3 lock      'wing lock' (wing in satellite, no hunt order) sends the Helm's target and 'hunt': first shot < 3 s after the command; with no target it answers 'no target' (3 seeds)
// EVIDENCE: the sim's IGC (delivery next tick, antenna reach), physics and gun model; nothing measured in the game. Script state is read by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using SeSim.Api;
using SeSim.Blueprint;
using SeSim.Combat;
using SeSim.Core;

namespace SeSim.Run
{
    public static class SelfTestM4
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static int Run(string simDir, string repo)
        {
            _fail = 0; _pass = 0;
            Console.WriteLine("SelfTestM4 (Staff wing: HOME / HOMER, wing verbs, forwarded Helm target, roll call, page 2)");
            string bpd = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            foreach (string m in new[] { "Shrike Wing Test", "Hornet" })
                if (!File.Exists(Path.Combine(bpd, m, "bp.sbc")))
                { SelfTestSkips.Note("M4 (build: python3 -I tools/build_all.py --only \"Shrike Wing Test\" Hornet --no-zip)"); Console.WriteLine("  skipped: blueprint " + m + " missing"); return 0; }
            TestHunt(simDir, repo);
            TestHuntControls(simDir, repo);
            TestFlips(simDir, repo);
            TestLock(simDir, repo);
            Console.WriteLine(_fail == 0 ? "SelfTestM4 passed (" + _pass + " checks)" : "SelfTestM4 FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        sealed class Rig
        {
            public World W; public ScriptHost Host; public Body Ship; public SimProgrammableBlock Helm, Staff;
            public List<SimProgrammableBlock> Drones = new List<SimProgrammableBlock>(); public List<Body> DroneBodies = new List<Body>();
            public Body Enemy; public string Bp; public DefDb Defs; public string BpDir;
            public object Prog(SimProgrammableBlock p) { return p.Runner.Program; }
        }

        static T Fld<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new InvalidOperationException("script has no field " + name);
            return (T)f.GetValue(o);
        }

        static Rig Build(string simDir, string repo, ulong seed, int n, bool blind)
        {
            var rig = new Rig();
            var settings = new SimSettings();
            rig.Defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            rig.BpDir = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            var w = new World(settings, rig.Defs, seed); rig.W = w;
            rig.Host = new ScriptHost(w, simDir); w.Scripts = rig.Host; w.Teams.Init(2);
            var hbp = BlueprintLoader.Get(rig.BpDir, "Hornet");
            var sbp = BlueprintLoader.Get(rig.BpDir, "Shrike Wing Test");
            var rng = new Random((int)seed * 7919 + 5);
            var sh = GridFactory.Spawn(w, sbp, 0, n, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Right, Up = Vector3D.Up }, new Dictionary<long, long>());
            rig.Ship = sh[0];
            var dr = new List<List<Body>>();
            for (int i = 0; i < n; i++)
            {   // the ring is 100 m round the ship; start them scattered 100-250 m out, like follow-test
                double a = rng.NextDouble() * Math.PI * 2, r = 100 + rng.NextDouble() * 150, y = (rng.NextDouble() - 0.5) * 60;
                var o = new Vector3D(Math.Cos(a) * r, y, Math.Sin(a) * r);
                dr.Add(GridFactory.Spawn(w, hbp, 0, i, new SpawnPlacement { Origin = o, Forward = Vector3D.Normalize(-o), Up = Vector3D.Up }, new Dictionary<long, long>()));
            }
            BlockApiFactory.FlushPending(w);
            foreach (var b in sh) foreach (var bi in b.Blocks)
            {
                var pb = bi.Api as SimProgrammableBlock; if (pb == null || pb.Runner == null) continue;
                if (pb.CustomNameValue.StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase)) rig.Staff = pb; else if (pb.CustomNameValue.StartsWith("[Helm]", StringComparison.OrdinalIgnoreCase)) rig.Helm = pb;
            }
            foreach (var g in dr) { rig.DroneBodies.Add(g[0]); foreach (var b in g) foreach (var bi in b.Blocks) { var pb = bi.Api as SimProgrammableBlock; if (pb != null && pb.Runner != null) { rig.Drones.Add(pb); break; } } }
            if (blind)
                foreach (var d in rig.Drones)
                {
                    string cd = d.CustomData ?? "";
                    foreach (string k in new[] { "Auto Camera Scan", "All-around Close Scan", "AI Block Search Assist" }) cd = Regex.Replace(cd, "(" + Regex.Escape(k) + @"\s*=\s*)\w+", "${1}false");
                    d.CustomData = cd;
                }
            // the Helm (Hornet code) also shares its own sightings on HORNET_TGT / FLEET_TGT: switched off here, so the Staff is the only way the wing learns the ship's target
            rig.Helm.CustomData = Regex.Replace(rig.Helm.CustomData ?? "", @"(Share Targets Over IGC\s*=\s*)\w+", "${1}false");
            rig.Host.RunArgument(rig.Helm, "reload", UpdateType.Terminal);
            rig.Host.RunArgument(rig.Staff, "reload", UpdateType.Terminal);
            foreach (var d in rig.Drones) rig.Host.RunArgument(d, "reload", UpdateType.Terminal);
            return rig;
        }

        // the Helm starts in HUNT (Start Mode); 'idle' once it has run, so the ship holds its heading and place
        static void Settle(Rig rig) { Run(rig, 1, null); Cmd(rig, rig.Helm, "idle"); Run(rig, 1, null); }

        static void SpawnEnemy(Rig rig, Vector3D at)
        {
            var hbp = BlueprintLoader.Get(rig.BpDir, "Hornet");
            var g = GridFactory.Spawn(rig.W, hbp, 1, 0, new SpawnPlacement { Origin = at, Forward = Vector3D.Left, Up = Vector3D.Up }, new Dictionary<long, long>());
            BlockApiFactory.FlushPending(rig.W);
            rig.Enemy = g[0];
            foreach (var b in g) foreach (var bi in b.Blocks) { var pb = bi.Api as SimProgrammableBlock; if (pb != null && pb.Runner != null && Environment.GetEnvironmentVariable("M4_LIVE") == null) rig.Host.KillPb(pb, "test target: no script"); }   // an unarmed, passive target
        }

        static int DroneShots(Rig rig) { int n = 0; foreach (var b in rig.DroneBodies) foreach (var u in b.Weapons) n += u.ShotsFired; return n; }
        static string Mode(SimProgrammableBlock pb) { var m = Regex.Match(pb.Runner.EchoText ?? "", @"Mode   : (\w+)"); return m.Success ? m.Groups[1].Value : "?"; }
        static string Echo(SimProgrammableBlock pb) { return pb.Runner.EchoText ?? ""; }
        static void Run(Rig rig, double seconds, Action<long> each)
        {
            long end = rig.W.Tick + (long)(seconds * 60);
            while (rig.W.Tick < end) { rig.W.Step(); if (each != null) each(rig.W.Tick); }
        }
        static bool StaffHasTarget(Rig rig) { return Fld<long>(rig.Prog(rig.Staff), "_tId") != 0 && Fld<double>(rig.Prog(rig.Staff), "_t") - Fld<double>(rig.Prog(rig.Staff), "_h2sT") < 1.0; }
        static void Cmd(Rig rig, SimProgrammableBlock pb, string a) { rig.Host.RunArgument(pb, a, UpdateType.Terminal); }

        // ------------------------------------------------------------------------------------------------------------------ 1 hunt
        static void TestHunt(string simDir, string repo)
        {
            Console.WriteLine("-- 1 hunt: blind wing in satellite; the ship's Helm locks an unarmed Hornet 450 m ahead and 'wing hunt' goes out as the Staff has the lock: first drone shot < 3 s --");
            int ok = 0, n = 5; var lat = new List<double>();
            for (int seed = 1; seed <= n; seed++)
            {
                var rig = Build(simDir, repo, (ulong)seed, 6, true);
                bool spawned = false; long tLock = -1, tFire = -1; int s0 = 0; string note = "";
                Settle(rig);
                Cmd(rig, rig.Staff, "wing satellite");
                Run(rig, 8, null);
                Run(rig, 38, t =>
                {
                    if (!spawned && t >= 0) { spawned = true; SpawnEnemy(rig, rig.Ship.Position + rig.Ship.DirToWorld(Vector3D.Forward) * 450); }
                    if (spawned && tLock < 0 && t % 60 == 30) Cmd(rig, rig.Helm, "lock");   // the Helm's manual lock: a camera raycast along the nose, retried every second
                    if (spawned && tLock < 0 && StaffHasTarget(rig)) { tLock = t; s0 = DroneShots(rig); Cmd(rig, rig.Staff, "wing hunt"); }   // the pilot presses 'wing hunt' as the lock lands
                    if (tLock >= 0 && tFire < 0 && DroneShots(rig) > s0) tFire = t;
                });
                if (Environment.GetEnvironmentVariable("M4_DEBUG") != null) Console.WriteLine("--- Helm ---\n" + Echo(rig.Helm) + "\n--- Staff ---\n" + Echo(rig.Staff) + "\n--- drone0 ---\n" + Echo(rig.Drones[0]) + "\nenemy at " + (rig.Enemy.Position - rig.Ship.Position) + " ship fwd " + rig.Ship.DirToWorld(Vector3D.Forward) + " aiOn " + Fld<bool>(rig.Prog(rig.Helm), "_aiOn") + " contacts " + ((System.Collections.ICollection)Fld<object>(rig.Prog(rig.Helm), "_contacts")).Count + " mode " + Fld<object>(rig.Prog(rig.Helm), "_mode") + " err '" + Fld<string>(rig.Prog(rig.Helm), "_err") + "'" + " drones " + string.Join(" ", rig.DroneBodies.ConvertAll(b => ((b.Position - rig.Ship.Position).Length()).ToString("F0"))));
                int hunting = 0; foreach (var d in rig.Drones) if (Mode(d) == "HUNT") hunting++;
                double dt = tLock < 0 || tFire < 0 ? double.NaN : (tFire - tLock) / 60.0;
                Info("seed " + seed + ": Staff has the lock at " + (tLock < 0 ? "never" : (tLock / 60.0).ToString("F1", Inv) + " s") + ", first drone shot " + (tFire < 0 ? "never" : (dt).ToString("F2", Inv) + " s later") + ", drones in HUNT " + hunting + "/6, enemy hp alive " + rig.Enemy.Alive + note);
                if (tLock >= 0 && tFire >= 0 && dt < 3 && hunting == 6) { ok++; lat.Add(dt); }
            }
            Check(ok == n, "wing hunt: the blind wing fires " + (lat.Count > 0 ? lat.Min2().ToString("F2", Inv) + "-" + lat.Max2().ToString("F2", Inv) + " s" : "-") + " after the Staff has the Helm's lock (< 3 s) (" + ok + "/" + n + " seeds)");
        }

        // controls for 1: without the Staff (PB killed) the blind wing never learns the target; with the Staff but without the 'wing hunt' order it stays in satellite
        static void TestHuntControls(string simDir, string repo)
        {
            int noStaff = 0, noHuntShots = 0, noHuntHunt = 0;
            for (int seed = 1; seed <= 2; seed++)
                foreach (bool kill in new[] { true, false })
                {
                    var rig = Build(simDir, repo, (ulong)seed, 6, true);
                    Settle(rig); Cmd(rig, rig.Staff, "wing satellite"); Run(rig, 8, null);
                    if (kill) rig.Host.KillPb(rig.Staff, "control: no Staff");
                    SpawnEnemy(rig, rig.Ship.Position + rig.Ship.DirToWorld(Vector3D.Forward) * 450);
                    Run(rig, 15, t => { if (t % 60 == 30) Cmd(rig, rig.Helm, "lock"); });
                    if (kill) noStaff += DroneShots(rig); else { noHuntShots += DroneShots(rig); noHuntHunt += Count(rig, "HUNT"); }
                }
            Info("controls (2 seeds each, 15 s after the lock): Staff PB killed -> drone shots " + noStaff + "; Staff alive but no 'wing hunt' (FOLLOW combat takes the shared target inside its Engage Radius) -> drone shots " + noHuntShots + ", drones in HUNT " + noHuntHunt + "/12");
            Check(noStaff == 0, "control: with the Staff PB dead the blind wing never fires (the target reaches it only through the Staff)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 2 flips
        static void TestFlips(string simDir, string repo)
        {
            Console.WriteLine("-- 2 flips: wing hunt / rtb / satellite / follow, roll call, page 2 --");
            int ok = 0, n = 3;
            for (int seed = 1; seed <= n; seed++)
            {
                var rig = Build(simDir, repo, (ulong)seed, 6, false);
                Run(rig, 2, null);
                Cmd(rig, rig.Staff, "wing satellite"); Run(rig, 40, null);
                string mSat1 = Mode(rig.Drones[0]); double r1 = RingR(rig);
                Cmd(rig, rig.Staff, "wing hunt"); Run(rig, 2, null);
                int hunt = Count(rig, "HUNT");
                Cmd(rig, rig.Staff, "wing rtb"); Run(rig, 2, null);
                int rtb = Count(rig, "RTB");
                Cmd(rig, rig.Staff, "wing satellite"); Run(rig, 60, null);
                int fol = Count(rig, "FOLLOW"); double r2 = RingR(rig);
                Cmd(rig, rig.Staff, "wing follow"); Run(rig, 40, null);
                double r3 = RingR(rig);
                Cmd(rig, rig.Staff, "page 2"); Run(rig, 1, null);
                string p2 = Echo(rig.Staff);
                var m = Regex.Match(p2, @"ROLL  (\d+) alive");
                int alive = m.Success ? int.Parse(m.Groups[1].Value) : -1;
                bool order = p2.Contains("order follow");
                bool row = Regex.IsMatch(p2, @"HORN \d{4}\s+\d+ m\s+\d+ m/s");
                double inHull = 0; foreach (var b in rig.DroneBodies) if ((b.WorldBoundCenter - rig.Ship.Position).Length() < rig.Ship.BoundRadius + 5) inHull++;
                Info("seed " + seed + ": satellite ring " + r1.ToString("F0", Inv) + " m (" + mSat1 + "), hunt " + hunt + "/6, rtb " + rtb + "/6, satellite again " + fol + "/6 on " + r2.ToString("F0", Inv) + " m, follow " + r3.ToString("F0", Inv) + " m, roll call " + alive + " alive, page 2 order follow " + order + ", row " + row);
                if (seed == 1) Info("page 2 text:\n    | " + p2.Replace("\n", "\n    | "));
                bool good = Math.Abs(r1 - 100) < 15 && hunt == 6 && rtb == 6 && fol == 6 && Math.Abs(r2 - 100) < 15 && r3 > 60 && r3 < 120 && alive == 6 && order && row && inHull == 0;
                if (good) ok++;
            }
            Check(ok == n, "wing satellite -> ring ~100 m, hunt -> 6 HUNT, rtb -> 6 RTB, satellite again -> ring, follow -> FOLLOW slot (60 m, 89 m for six), roll call = 6 alive, page 2 lists the drones (" + ok + "/" + n + " seeds)");
        }
        static int Count(Rig rig, string mode) { int c = 0; foreach (var d in rig.Drones) if (Mode(d) == mode) c++; return c; }
        static double RingR(Rig rig) { double s = 0; foreach (var b in rig.DroneBodies) s += (b.WorldBoundCenter - rig.Ship.Position).Length(); return s / rig.DroneBodies.Count; }

        // ------------------------------------------------------------------------------------------------------------------ 3 lock
        static void TestLock(string simDir, string repo)
        {
            Console.WriteLine("-- 3 lock: 'wing lock' hands the Helm's target to a blind wing in satellite and orders hunt --");
            int ok = 0, n = 3;
            for (int seed = 1; seed <= n; seed++)
            {
                var rig = Build(simDir, repo, (ulong)seed, 6, true);
                Settle(rig);
                Cmd(rig, rig.Staff, "wing satellite"); Run(rig, 20, null);
                Cmd(rig, rig.Staff, "wing lock"); Run(rig, 1, null);
                string noTgt = Fld<string>(rig.Prog(rig.Staff), "_note");
                SpawnEnemy(rig, rig.Ship.Position + rig.Ship.DirToWorld(Vector3D.Forward) * 450);
                int t0 = DroneShots(rig); long tCmd = -1, tFire = -1; bool sent = false;
                Run(rig, 25, t =>
                {
                    if (!sent && t % 60 == 30) Cmd(rig, rig.Helm, "lock");
                    if (!sent && StaffHasTarget(rig)) { sent = true; Cmd(rig, rig.Staff, "wing lock"); tCmd = t; }
                    if (sent && tFire < 0 && DroneShots(rig) > t0) tFire = t;
                });
                double dt = tCmd < 0 || tFire < 0 ? double.NaN : (tFire - tCmd) / 60.0;
                int hunting = Count(rig, "HUNT");
                Info("seed " + seed + ": without a target the Staff says '" + noTgt + "', after the lock: first shot " + (tFire < 0 ? "never" : dt.ToString("F2", Inv) + " s after 'wing lock'") + ", HUNT " + hunting + "/6");
                if (noTgt.Contains("no target") && tFire >= 0 && dt < 3 && hunting == 6) ok++;
            }
            Check(ok == n, "wing lock: 'no target' without a lock; with one the blind wing hunts and fires < 3 s after the command (" + ok + "/" + n + " seeds)");
        }
    }

    static class ListExt
    {
        public static double Min2(this List<double> l) { double m = double.MaxValue; foreach (var x in l) if (x < m) m = x; return m; }
        public static double Max2(this List<double> l) { double m = double.MinValue; foreach (var x in l) if (x > m) m = x; return m; }
    }
}
