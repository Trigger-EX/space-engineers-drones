// Helm split acceptance tests (docs/design/helm_split.md): the fleet channel relay and the damage sensing run in StaffAI.cs, the Helm (ShrikeAI.cs) keeps flying and fighting.
// Run on the REAL scripts inside "Shrike Staff Test" (Helm + Staff PB) against a Hornet:
//   python3 -I tools/build_all.py --only "Shrike Staff Test" Hornet --no-zip
// Run: SimHost.exe selftest hsplit   (also part of `selftest all`; skips when the blueprints are missing)
//   a link      the Helm's H2S carries bit 8, the Staff switches the split on
//   b fleet     FLEET_TGT becomes a Helm contact (IGC) within 14 ticks; FLEET_CMD hunt -> HUNT; FLEET_CMD launch reaches the Helm only (the Staff starts no bay launch); FLEET_HOME -> _dynHome,
//               a HOME inside 50 m of the ship is ignored (the Staff's own HOME is its own position)
//   c damage    in a duel the Staff's block-integrity scan sees hits and the Helm's UnderFire / _hits follow (.DMG)
//   d staff dead  KillPb Staff at 15 s: the Helm keeps its mode, counts exactly one hit, shows the loss
// flight_split.md (H1-H7, the Helm's config / HUD / menu / lock / AI setup / auto-scan in the Staff):
//   e config    the Staff writes the Helm's Custom Data defaults and answers the Helm's CFG WANT (H2S bit 9): values set in [Shrike] / [Manual] / [Battery] /
//               [Kamikaze Defence] / [AI Gate] reach the Helm's fields, "cfg: Staff", CfWant cleared, the Helm never writes its own Custom Data
//   f HUD/menu  a pilot sits: the Staff's HUD page shows MODE MANUAL; two argument-less Runs of the Helm show "MENU > guard 2/2" and the 2nd item reaches the Helm
//   g lock      "lock" facing the enemy: the Staff's ray (.LCK -> .LKR) becomes the Helm's target with source LOCK; AI ASSIST (H2S bit 10) seen by the Staff
//   h scan      while the Helm asks for the auto-scan (H2S bit 11) the Staff casts the camera rays (hits come back on .LKR as SCAN contacts)
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
            TestConfig();
            TestHudMenuLock();
            TestScan();
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

        static Scenario Duel(string pilot, bool idleEnemy, params string[] cmds) { return Duel2(pilot, idleEnemy, "", cmds); }
        static Scenario Duel2(string pilot, bool idleEnemy, string helmCd, params string[] cmds)
        {
            var sc = new Scenario { Kind = "1v1", Name = "hsplit/Shrike Staff Test-vs-Hornet", StartDistanceMin = 900, StartDistanceMax = 900, MaxTicks = 120 * 60, FaceEnemies = true, DecisiveHoldSeconds = 1000 };
            sc.Teams.Add(new TeamSpec(0, "Shrike Staff Test")); sc.Teams.Add(new TeamSpec(1, "Hornet"));
            var sa = new ShipSpec { Model = "Shrike Staff Test", Team = 0, Role = "A1", Pilot = pilot };
            sa.PbCustomData["Helm"] = "[Shrike]\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=IDLE\n" + helmCd;
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

        static string HelmCd(object helm) { var me = typeof(Sandbox.ModAPI.Ingame.MyGridProgram).GetProperty("Me", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); var pb = me != null ? me.GetValue(helm, null) as IMyProgrammableBlock : null; return pb != null ? pb.CustomData : ""; }

        static void TestConfig()
        {
            Console.WriteLine("-- the Helm's config comes from the Staff (CONFIG SERVER -> .CFG -> CONFIG LINK) --");
            int ok = 0, okDef = 0;
            const string cd = "Combat Speed=55\n[Manual]\nPilot Rate Gain=2.25\nAssist (OFF/HUD/FBW)=fbw\n[Battery]\nStandoff By Weapons=false\n[Kamikaze Defence]\nWarning Time (s)=9\n[AI Gate]\nAI Window (s)=7\n";
            for (int seed = 0; seed < 3; seed++)
            {
                string st = "", dbg = ""; bool want = true, vals = false, defs = false; int at = -1;
                var sc = Duel2(null, true, cd, "0.5", "Helm:idle");
                Go(sc, seed, 4, (w, sh, helm, staff) =>
                {
                    if (at < 0 && !Fld<bool>(helm, "CfWant")) at = (int)w.Tick;
                    if (w.Tick == 3 * 60)
                    {
                        st = Fld<string>(helm, "_cfSt"); want = Fld<bool>(helm, "CfWant");
                        double ms = Fld<double>(helm, "MaxSpeed"), g = Fld<double>(helm, "ManGain"), kh = Fld<double>(helm, "KzHorizon"), aw = Fld<double>(helm, "AiWin"); int al = Fld<int>(helm, "_asLvl"); bool so = Fld<bool>(helm, "BatSo");
                        vals = ms == 55 && g == 2.25 && kh == 9 && aw == 7 && al == 2 && !so;
                        string c = HelmCd(helm); defs = c.Contains("Orbit Speed=22") && c.Contains("[Battery]") && c.Contains("Max AI Hold (s)=60") && c.Contains("Seat Exclude (name part)=gunnery");
                        dbg = "MaxSpeed " + ms + " ManGain " + g + " KzHorizon " + kh + " AiWin " + aw + " asLvl " + al + " BatSo " + so;
                    }
                });
                if (st.StartsWith("cfg: Staff") && !want && vals) ok++; if (defs) okDef++;
                Info("seed " + seed + ": '" + st + "', WANT cleared at tick " + at + ", " + dbg + ", Staff wrote the defaults " + defs);
            }
            Check(ok == 3, "the Helm asks (H2S bit 9) and applies the Staff's .CFG: [Shrike] [Manual] [Battery] [Kamikaze Defence] [AI Gate] values arrive, 'cfg: Staff' (" + ok + "/3 seeds)");
            Check(okDef == 3, "the Staff writes the missing defaults into the Helm PB's Custom Data (" + okDef + "/3 seeds)");
        }

        static void TestHudMenuLock()
        {
            Console.WriteLine("-- HUD page, Run menu, lock ray and AI setup in the Staff --");
            int okHud = 0, okMenu = 0, okLock = 0, okAi = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                string hud = "", menu = "", lockSrc = "", mode = ""; bool ai = false; long enemy = 0;
                var sc = Duel2("2:seat;2.1:sit;4:run Helm;4.2:run Helm;9:leave", true, "[Manual]\nRun Menu=hunt,guard\nRun Menu Wait=1.5\n", "0.5", "Helm:idle");
                Go(sc, seed, 13, (w, sh, helm, staff) =>
                {
                    var hs = Fld<object>(staff, "_hsf") as IMyTextSurface;
                    if (w.Tick == 3 * 60 && hs != null) hud = hs.GetText();
                    if (w.Tick == 4 * 60 + 20 && hs != null) menu = hs.GetText();
                    if (w.Tick == 7 * 60) { mode = Fld<object>(helm, "_mode").ToString(); ai = Fld<bool>(staff, "_aiA"); }
                });
                var sl = Duel(null, true, "0.5", "Helm:ai off", "0.6", "Helm:idle", "3", "Helm:lock");
                string note = "";
                Go(sl, seed, 4, (w, sh, helm, staff) =>
                {
                    if (w.Tick > 3 * 60 && lockSrc != "LOCK") { var tg = Fld<object>(helm, "_tgt"); if (tg != null) { lockSrc = (string)tg.GetType().GetField("Src").GetValue(tg); enemy = (long)tg.GetType().GetField("Id").GetValue(tg); } }
                    if (w.Tick == 4 * 60 - 2) note = Fld<string>(staff, "_lcN");
                });
                if (hud.Contains("MODE MANUAL")) okHud++; if (menu.Contains("MENU > guard 2/2") && mode == "GUARD") okMenu++; if (lockSrc == "LOCK") okLock++; if (ai) okAi++;
                Info("seed " + seed + ": HUD '" + hud.Split('\n')[0] + "', menu line " + menu.Contains("MENU > guard 2/2") + ", mode after the menu " + mode + ", lock -> target source '" + lockSrc + "' id " + enemy + " (Staff: " + note + "), Staff AI ASSIST " + ai);
            }
            Check(okHud == 3, "a seated pilot: the Staff writes the HUD page (MODE MANUAL) from the Helm's .HUD (" + okHud + "/3 seeds)");
            Check(okMenu == 3, "two argument-less Runs of the Helm: the Staff shows MENU > guard 2/2 and the item reaches the Helm (" + okMenu + "/3 seeds)");
            Check(okLock == 3, "'lock' facing the enemy: the Staff's ray comes back on .LKR and becomes the Helm's target (source LOCK) (" + okLock + "/3 seeds)");
            Check(okAi == 3, "the Staff sees AI ASSIST (H2S bit 10) and runs the AI-block setup (" + okAi + "/3 seeds)");
        }

        static void TestScan()
        {
            Console.WriteLine("-- the camera auto-scan in the Staff --");
            int ok = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int bit = 0, scan = 0, rays = 0; string dbg = "";
                var sc = Duel(null, true, "0.5", "Helm:ai off", "0.6", "Helm:hunt");   // ("ai off": the AI blocks would find it first)
                Go(sc, seed, 15, (w, sh, helm, staff) =>
                {
                    if ((Fld<int>(staff, "_fl") & 2048) != 0) bit++;
                    rays = Fld<int>(staff, "_scanIdx");
                    if (w.Tick % 180 == 0) { var tg = Fld<object>(helm, "_tgt"); dbg += " t" + w.Tick / 60 + ":" + Fld<object>(helm, "_mode") + "/" + (tg == null ? "-" : (string)tg.GetType().GetField("Src").GetValue(tg)) + "/ai" + Fld<bool>(helm, "_aiOn")  + "/rays" + Fld<int>(staff, "_scanIdx"); }
                    foreach (var o in Fld<System.Collections.IDictionary>(helm, "_contacts").Values) if ((string)o.GetType().GetField("Src").GetValue(o) == "SCAN") { scan++; break; }
                });
                if (bit > 0 && rays >= 20) ok++;
                Info("seed " + seed + ": ticks with H2S bit 11 at the Staff " + bit + ", ticks with a SCAN contact in the Helm " + scan + dbg);
            }
            Check(ok == 3, "H2S bit 11 SCAN: the Staff casts the auto-scan rays while the Helm asks (" + ok + "/3 seeds; SCAN contacts are info only: the pre-split Helm found none in this 900 m set-up either)");
        }
    }
}
