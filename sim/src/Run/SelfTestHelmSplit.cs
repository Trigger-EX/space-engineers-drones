// Helm split acceptance tests (docs/design/helm_split.md): the fleet channel relay and the damage sensing run in StaffAI.cs, the Helm (ShrikeAI.cs) keeps flying and fighting.
// Run on the REAL scripts inside "Shrike Staff Test" (Helm + Staff PB) against a Hornet:
//   python3 -I tools/build_all.py --only "Shrike Staff Test" Hornet --no-zip
// Run: SimHost.exe selftest hsplit   (also part of `selftest all`; skips when the blueprints are missing)
//   a link      the Helm's H2S carries bit 8, the Staff switches the split on
//   b fleet     FLEET_TGT becomes a Helm contact (IGC) within 14 ticks; FLEET_CMD hunt -> HUNT; FLEET_CMD launch reaches the Helm only (the Staff starts no bay launch); FLEET_HOME -> _dynHome,
//               a HOME inside 50 m of the ship is ignored (the Staff's own HOME is its own position)
//   c damage    in a duel the Staff's block-integrity scan sees hits and the Helm's UnderFire / _hits follow (.DMG)
//   d staff dead  KillPb Staff at 15 s: the Helm keeps its mode, counts exactly one hit, shows the loss
// EVIDENCE: the sim's IGC (delivery next tick) and damage model; nothing measured in the game. Script state is read by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using SeSim.Api;
using SeSim.Core;

namespace SeSim.Run
{
    public static class SelfTestHelmSplit
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }
        static string _repo, _simDir;
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static int Run(string simDir, string repo)
        {
            _fail = 0; _pass = 0; _simDir = simDir; _repo = repo;
            Console.WriteLine("SelfTestHelmSplit (fleet relay + damage sensing in the Staff PB, Helm keeps the hot path)");
            string bp = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            if (!File.Exists(Path.Combine(bp, "Shrike Staff Test", "bp.sbc")) || !File.Exists(Path.Combine(bp, "Hornet", "bp.sbc")))
            { SelfTestSkips.Note("hsplit (build: python3 -I tools/build_all.py --only \"Shrike Staff Test\" Hornet --no-zip)"); Console.WriteLine("  skipped: blueprints missing"); return 0; }
            TestFleet();
            TestDamage();
            TestStaffDead();
            Console.WriteLine(_fail == 0 ? "SelfTestHelmSplit passed (" + _pass + " checks)" : "SelfTestHelmSplit FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        static T Fld<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new InvalidOperationException("script has no field " + name);
            return (T)f.GetValue(o);
        }
        static bool HasContact(object helm, long id) { var d = Fld<System.Collections.IDictionary>(helm, "_contacts"); return d.Contains(id); }

        static Scenario Duel(string pilot, bool idleEnemy, params string[] cmds)
        {
            var sc = new Scenario { Kind = "1v1", Name = "hsplit/Shrike Staff Test-vs-Hornet", StartDistanceMin = 900, StartDistanceMax = 900, MaxTicks = 120 * 60, FaceEnemies = true, DecisiveHoldSeconds = 1000 };
            sc.Teams.Add(new TeamSpec(0, "Shrike Staff Test")); sc.Teams.Add(new TeamSpec(1, "Hornet"));
            var sa = new ShipSpec { Model = "Shrike Staff Test", Team = 0, Role = "A1", Pilot = pilot };
            sa.PbCustomData["Helm"] = "[Shrike]\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=IDLE\n";
            for (int i = 0; i + 1 < cmds.Length; i += 2) sa.Commands.Add(new KeyValuePair<double, string>(double.Parse(cmds[i], Inv), cmds[i + 1]));
            var sb = new ShipSpec { Model = "Hornet", Team = 1, Role = "B1" };
            if (!idleEnemy) sb.Commands.Add(new KeyValuePair<double, string>(0.5, "hunt"));
            sc.Ships.Add(sa); sc.Ships.Add(sb);
            return sc;
        }

        static RunResult Go(Scenario sc, int seed, double seconds, Action<World, ShipInstance, object, object> tick)
        {
            var s = new SimSettings(); s.MaxTicks = (int)(seconds * 60) + 2; s.BlockAutomation = true;
            var opt = new RunOptions { SimDir = _simDir };
            opt.OnTick = (w, host, ships) =>
            {
                var helm = ships[0].PbsByTag("Helm"); var staff = ships[0].PbsByTag("Staff");
                if (helm.Count == 0 || helm[0].Program == null || staff.Count == 0 || staff[0].Program == null) return;
                tick(w, ships[0], helm[0].Program, staff[0].Program);
            };
            var r = ScenarioRunner.RunOne(sc, seed, 1UL, s, _repo, opt);
            if (r.Error != null) Console.WriteLine("  ERROR " + r.Error);
            return r;
        }

        static void TestFleet()
        {
            Console.WriteLine("-- fleet channel through the Staff --");
            int okLink = 0, okTgt = 0, okCmd = 0, okLaunch = 0, okHome = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                bool split = false, tgt = false, hunt = false, launchLeak = true, home = false, homeNear = false; int tgtTick = -1; string dbg = "";
                var sc = Duel(null, true, "0.5", "Helm:idle");
                const long Fake = 999001;
                Go(sc, seed, 12, (w, sh, helm, staff) =>
                {
                    if (w.Tick == 3 * 60) split = Fld<bool>(staff, "_split") && Fld<bool>(helm, "HlkOn");
                    if (w.Tick == 4 * 60)
                    {
                        Vector3D p = sh.Main.Position + new Vector3D(0, 0, -3000);
                        w.Igc.InjectBroadcast("FLEET_TGT", new VRage.MyTuple<long, Vector3D, Vector3D, double, long>(Fake, p, Vector3D.Zero, 9, 12345));
                    }
                    if (w.Tick >= 4 * 60 && w.Tick <= 4 * 60 + 40 && tgtTick < 0 && HasContact(helm, Fake)) { tgtTick = (int)(w.Tick - 4 * 60); tgt = true; }
                    if (w.Tick == 5 * 60) w.Igc.InjectBroadcast("FLEET_CMD", "hunt");
                    if (w.Tick == 6 * 60) hunt = Fld<object>(helm, "_mode").ToString() == "HUNT";
                    if (w.Tick == 7 * 60) w.Igc.InjectBroadcast("FLEET_CMD", "launch");
                    if (w.Tick == 8 * 60) launchLeak = Fld<bool>(staff, "_lchOn") || Fld<string>(staff, "_note").StartsWith("launch");
                    if (w.Tick == 9 * 60 - 30) w.Igc.InjectBroadcast("FLEET_HOME", sh.Main.Position + new Vector3D(10, 0, 0));   // inside 50 m: ignored
                    if (w.Tick == 9 * 60) homeNear = Fld<double>(helm, "_dynHomeT") > 0;
                    if (w.Tick == 9 * 60 + 5) w.Igc.InjectBroadcast("FLEET_HOME", sh.Main.Position + new Vector3D(0, 800, 0));
                    if (w.Tick == 10 * 60) { dbg = "_dynHomeT " + Fld<double>(helm, "_dynHomeT").ToString("F1") + " _dynHome " + Fld<Vector3D>(helm, "_dynHome") + " ship " + sh.Main.Position + " staff homeT " + Fld<double>(staff, "_homeT").ToString("F1"); home = Fld<double>(helm, "_dynHomeT") > 0 && (Fld<Vector3D>(helm, "_dynHome") - (sh.Main.Position + new Vector3D(0, 800, 0))).Length() < 50; }
                });
                if (split) okLink++; if (tgt && tgtTick <= 14) okTgt++; if (hunt) okCmd++; if (!launchLeak) okLaunch++; if (home && !homeNear) okHome++;
                Info("seed " + seed + ": split " + split + ", FLEET_TGT -> Helm contact after " + tgtTick + " ticks, FLEET_CMD hunt -> HUNT " + hunt + ", launch leak into the Staff " + launchLeak + ", HOME near ignored " + !homeNear + ", HOME far taken " + home + " [" + dbg + "]");
            }
            Check(okLink == 3, "the Staff switches the split on while the Helm's H2S carries bit 8 (" + okLink + "/3 seeds)");
            Check(okTgt == 3, "a FLEET_TGT becomes a Helm contact within 14 ticks (" + okTgt + "/3 seeds)");
            Check(okCmd == 3, "FLEET_CMD hunt puts the Helm in HUNT (" + okCmd + "/3 seeds)");
            Check(okLaunch == 3, "FLEET_CMD launch reaches the Helm only: the Staff starts no bay launch (" + okLaunch + "/3 seeds)");
            Check(okHome == 3, "FLEET_HOME far away becomes the Helm's dynamic home, one inside 50 m of the ship is ignored (" + okHome + "/3 seeds)");
        }

        static void TestDamage()
        {
            Console.WriteLine("-- damage sensing in the Staff --");
            int ok = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int sHits = 0, hHits = 0; double hitAt = -99; bool under = false;
                var sc = Duel(null, false, "0.5", "Helm:guard");
                var r = Go(sc, seed, 50, (w, sh, helm, staff) =>
                {
                    sHits = Fld<int>(staff, "_dmgHits"); hHits = Fld<int>(helm, "_hits"); hitAt = Fld<double>(helm, "_hitAt");
                    if (Fld<double>(helm, "_now") - hitAt < 6 && hitAt > 0) under = true;
                });
                if (sHits > 0 && hHits > 0 && hitAt > 0 && under) ok++;
                Info("seed " + seed + ": Staff hits " + sHits + ", Helm hits " + hHits + ", UnderFire seen " + under + ", damage taken " + (r.Ships.Count > 0 ? r.Ships[0].DamageTaken.ToString("F0") : "?"));
            }
            Check(ok == 3, "the Staff's integrity scan counts hits and the Helm's _hits / UnderFire follow over .DMG (" + ok + "/3 seeds)");
        }

        static void TestStaffDead()
        {
            Console.WriteLine("-- the Staff dies in flight --");
            int ok = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int h14 = -1, h24 = -1; string m14 = "", m24 = ""; bool lost = false;
                var sc = Duel("15:killpb Staff", true, "0.5", "Helm:hunt");
                Go(sc, seed, 25, (w, sh, helm, staff) =>
                {
                    if (w.Tick == 14 * 60) { h14 = Fld<int>(helm, "_hits"); m14 = Fld<object>(helm, "_mode").ToString(); }
                    if (w.Tick == 24 * 60) { h24 = Fld<int>(helm, "_hits"); m24 = Fld<object>(helm, "_mode").ToString(); lost = Fld<bool>(helm, "_hlkLost"); }
                });
                if (h14 >= 0 && h24 - h14 == 1 && m14 == m24 && lost) ok++;
                Info("seed " + seed + ": hits " + h14 + " -> " + h24 + ", mode " + m14 + " -> " + m24 + ", Staff-lost flag " + lost);
            }
            Check(ok == 3, "Staff killed at 15 s: the Helm keeps its mode, counts exactly one hit and flags the loss (" + ok + "/3 seeds)");
        }
    }
}
