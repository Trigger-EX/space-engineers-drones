// M2 acceptance tests (docs/design/control_arch.md section 7 "M2 Helm/Staff split + dead-man"), run on the REAL scripts/ShrikeAI.cs (Helm) and
// scripts/StaffAI.cs (Staff) inside the generated test blueprints "Shrike Staff Test" (two PBs, Event Controller, Deadman timer) and "Shrike Test" (one PB):
//   python3 -I tools/build_all.py --only "Shrike Test" "Shrike Staff Test" --no-zip
// Run: SimHost.exe selftest m2   (also part of `selftest all`; the section skips when the blueprints are missing).
//   1 link          heartbeat / mode / target / seat reach the Staff over IGC (CurrentConstruct); the link is OFF on a ship without a Staff PB, ON/OFF by Custom Data
//   2 commands      Staff:hunt/guard/manual/auto are forwarded to the Helm, wing / release verbs answer (since M5 'release' goes out to the bay PBs), the argument-less Run menu of the Staff
//   b dead-man      KillPb Helm at 15 s with overrides on: the Staff releases every gyro / thruster override within Trip Time (1.5 s) + one Update10 step,
//                   guns off, dampeners on; a PB that only pauses (disablepb / enablepb) is followed by "Helm back"
//   c Timer D + EC  Helm killed in AUTO: Timer D fires 5 s later; Helm killed in MANUAL: it does not; Helm killed while a pilot sits: the Event Controller
//                   starts the timer when he leaves; the cockpit HUD says HELM LOST
//   d latency       IGC latency 1 vs 2 ticks: first heartbeat arrives exactly one tick later, no false trip, same win rate band (10 seeds each)
//   e instructions  peak instructions per PB < 30,000 (block model and IL model) when the instrumenting rewriter is built (tools/ilcount/build.sh)
// (a) parity (split vs the same hull with the link off, 20-40 seeds) is a command-line comparison, not part of the self test (too slow and statistical):
//   see docs/design/control_arch.md M2 notes / the commit message for the command lines.
// EVIDENCE: the sim's IGC delivery, Timer / Event Controller semantics (placeholder event id for "cockpit occupied", K4), PB kill and the heartbeat
// timing are the sim's model; nothing here was measured in the game (K3, K4, K8). Script state is read by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using SeSim.Api;
using SeSim.Core;

namespace SeSim.Run
{
    public static class SelfTestM2
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }
        static string _repo, _simDir;
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static int Run(string simDir, string repo)
        {
            _fail = 0; _pass = 0; _simDir = simDir; _repo = repo;
            Console.WriteLine("SelfTestM2 (Helm/Staff split: HELMLINK over IGC, StaffAI heartbeat watch, Timer D + Event Controller dead-man)");
            string bp = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            if (!File.Exists(Path.Combine(bp, "Shrike Staff Test", "bp.sbc")) || !File.Exists(Path.Combine(bp, "Shrike Test", "bp.sbc")) || !File.Exists(Path.Combine(bp, "Hornet", "bp.sbc")))
            { SelfTestSkips.Note("M2 (build: python3 -I tools/build_all.py --only \"Shrike Test\" \"Shrike Staff Test\" Hornet --no-zip)"); Console.WriteLine("  skipped: blueprints missing"); return 0; }
            TestLink();
            TestCommands();
            TestDeadman();
            TestTimerEc();
            TestLatency();
            TestInstructions();
            Console.WriteLine(_fail == 0 ? "SelfTestM2 passed (" + _pass + " checks)" : "SelfTestM2 FAILED: " + _fail + " of " + (_pass + _fail));
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

        /// <param name="cmds">alternating time, "Tag:arg" pairs sent to our ship</param>
        static Scenario Duel(string a, string pilot, double dist, bool idleEnemy, string helmData, params string[] cmds)
        {
            var sc = new Scenario { Kind = "1v1", Name = "m2/" + a + "-vs-Hornet", StartDistanceMin = dist, StartDistanceMax = dist, MaxTicks = 120 * 60, FaceEnemies = true, DecisiveHoldSeconds = 1000 };
            sc.Teams.Add(new TeamSpec(0, a)); sc.Teams.Add(new TeamSpec(1, "Hornet"));
            var sa = new ShipSpec { Model = a, Team = 0, Role = "A1", Pilot = pilot };
            sa.PbCustomData["Helm"] = "[Shrike]\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=IDLE\n" + (helmData ?? "");
            for (int i = 0; i + 1 < cmds.Length; i += 2) sa.Commands.Add(new KeyValuePair<double, string>(Num(cmds[i]), cmds[i + 1]));
            var sb = new ShipSpec { Model = "Hornet", Team = 1, Role = "B1" };
            if (!idleEnemy) sb.Commands.Add(new KeyValuePair<double, string>(0.5, "hunt"));
            sc.Ships.Add(sa); sc.Ships.Add(sb);
            return sc;
        }

        /// <summary>Runs a scenario; the callback gets the world, our ship and the Helm / Staff programs (Staff null when the ship has none) after every tick.</summary>
        static RunResult Go(Scenario sc, int seed, double seconds, Action<World, ShipInstance, object, object> tick, Action<SimSettings> cfg = null, bool count = false, string mode = "blocks")
        {
            var s = new SimSettings(); s.MaxTicks = (int)(seconds * 60) + 2; s.BlockAutomation = true;
            if (count) { s.CountInstructions = true; s.InstructionCountMode = mode; }
            if (cfg != null) cfg(s);
            var opt = new RunOptions { SimDir = _simDir };
            opt.OnTick = (w, host, ships) =>
            {
                var helm = ships[0].PbsByTag("Helm"); var staff = ships[0].PbsByTag("Staff");
                if (helm.Count == 0 || helm[0].Program == null) return;
                tick(w, ships[0], helm[0].Program, staff.Count > 0 ? staff[0].Program : null);
            };
            var r = ScenarioRunner.RunOne(sc, seed, 1UL, s, _repo, opt);
            if (r.Error != null) Console.WriteLine("  ERROR " + r.Error);
            return r;
        }

        static bool AnyOverride(Body b)
        {
            for (int i = 0; i < b.Gyros.Count; i++) if (b.Gyros[i].Override && b.Gyros[i].Working) return true;
            for (int i = 0; i < b.Thrusters.Count; i++) if (b.Thrusters[i].OverrideN > 0 && b.Thrusters[i].Working) return true;
            return false;
        }
        static string OvList(Body b)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < b.Gyros.Count; i++) if (b.Gyros[i].Override) sb.Append("gyro#").Append(i).Append(b.Gyros[i].Working ? "" : "(not working)").Append(' ');
            for (int i = 0; i < b.Thrusters.Count; i++) if (b.Thrusters[i].OverrideN > 0) sb.Append("thr#").Append(i).Append('=').Append(b.Thrusters[i].OverrideN.ToString("F2")).Append(b.Thrusters[i].Working ? "" : "(not working)").Append(' ');
            return sb.ToString();
        }
        static SimCockpit Seat(ShipInstance sh) { return sh.Pilot != null ? sh.Pilot.Seat : null; }
        static T FindBlock<T>(ShipInstance sh) where T : class
        {
            foreach (var bd in sh.Bodies) foreach (var k in bd.Blocks) { var t = k.Api as T; if (t != null) return t; }
            return null;
        }
        static int ThrustersOn(ShipInstance sh) { int n = 0; foreach (var th in sh.Main.Thrusters) if (th.Enabled) n++; return n; }
        static string PbText(ShipInstance sh, string tag)
        {
            var l = sh.PbsByTag(tag); if (l.Count == 0) return "";
            try { return l[0].Pb.GetSurface(0).GetText(); } catch (Exception) { return ""; }
        }

        // ------------------------------------------------------------------------------------------------------------------ 1 link
        static void TestLink()
        {
            Console.WriteLine("-- 1 link: heartbeat, mode, target and seat reach the Staff; the link is optional --");
            int ok = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int hbN = 0, trips = -1, md = -1, seatN = -1; double age = 99, batt = 0; long tId = 0; bool hlk = false; string txt = "";
                var sc = Duel("Shrike Staff Test", "3:sit;9:leave", 900, true, "", "0.5", "Helm:hunt");
                Go(sc, seed, 12, (w, sh, helm, staff) =>
                {
                    if (staff == null) return;
                    if (w.Tick == 2 * 60) { md = -1; }
                    if (w.Tick == 2 * 60 + 30) { md = Fld<int>(staff, "_md"); seatN = Fld<int>(staff, "_seat"); }
                    if (w.Tick == 8 * 60)
                    {
                        hbN = Fld<int>(staff, "_hbN"); trips = Fld<int>(staff, "_trips"); age = Fld<double>(staff, "_t") - Fld<double>(staff, "_hbT"); batt = Fld<float>(staff, "_batt"); tId = Fld<long>(staff, "_tId");
                        hlk = Fld<bool>(helm, "HlkOn"); txt = PbText(sh, "Staff");
                    }
                    if (w.Tick == 11 * 60) { }
                });
                // at 2.5 s the Helm is in HUNT (2); the pilot sits at 3 s, so no seat yet
                bool o = hlk && hbN >= 8 && trips == 0 && age < 0.7 && batt > 0.5 && md == 2 && seatN == 0 && txt.Contains("HELM  ok") && txt.Contains("MANUAL");
                if (o) ok++;
                Info("seed " + seed + ": link " + hlk + ", heartbeats " + hbN + ", trips " + trips + ", last beat " + age.ToString("F2") + " s ago, mode@2.5s " + md + ", seat " + seatN + ", battery " + batt.ToString("F2") + ", target id " + tId);
                if (seed == 0) Console.WriteLine("  ---- Staff PB screen at 8 s (pilot sitting since 3 s) ----\n  | " + txt.Replace("\n", "\n  | ") + "\n  ----");
            }
            Check(ok == 3, "split ship: heartbeats arrive (>= 8 in 8 s), no trip, mode / seat / battery reach the Staff and its screen (" + ok + "/3 seeds)");

            int ok2 = 0;
            for (int seed = 0; seed < 2; seed++)
            {
                bool a = true, b = true; int hb = -1; string t = "";
                var s1 = Duel("Shrike Test", null, 900, true, "", "0.5", "Helm:hunt");
                Go(s1, seed, 4, (w, sh, helm, staff) => { if (w.Tick == 3 * 60) a = Fld<bool>(helm, "HlkOn"); });
                var s2 = Duel("Shrike Test", null, 900, true, "[Link]\nEnable (AUTO/ON/OFF)=ON\n", "0.5", "Helm:hunt");
                Go(s2, seed, 4, (w, sh, helm, staff) => { if (w.Tick == 3 * 60) b = Fld<bool>(helm, "HlkOn"); });
                bool c = true;
                var s3 = Duel("Shrike Staff Test", null, 900, true, "[Link]\nEnable (AUTO/ON/OFF)=OFF\n", "0.5", "Helm:hunt");
                Go(s3, seed, 6, (w, sh, helm, staff) => { if (staff != null && w.Tick == 5 * 60) { c = Fld<bool>(helm, "HlkOn"); hb = Fld<int>(staff, "_hbN"); t = PbText(sh, "Staff"); } });
                if (!a && b && !c && hb == 0 && t.Contains("no heartbeat yet")) ok2++;
                Info("seed " + seed + ": single PB AUTO link " + a + ", single PB Enable=ON " + b + ", split ship Enable=OFF link " + c + " (Staff heartbeats " + hb + ")");
            }
            Check(ok2 == 2, "link is optional: OFF on a ship without a Staff PB, ON by Custom Data, OFF by Custom Data even with a Staff PB (the Staff never trips without a first beat) (" + ok2 + "/2 seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 2 commands
        static void TestCommands()
        {
            Console.WriteLine("-- 2 commands: Staff forwards verbs to the Helm over IGC, wing / release (M5: sent to the bays, nobody answers on this ship), Run menu --");
            int ok = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                string m1 = "", m2 = "", n1 = "", n2 = ""; bool man1 = false, man2 = true, menu1 = false, menu2 = true; string note3 = "";
                var sc = Duel("Shrike Staff Test", "12:run Staff", 900, true, "", "0.5", "Helm:idle", "2", "Staff:hunt", "4", "Staff:guard", "6", "Staff:manual", "8", "Staff:auto", "10", "Staff:wing satellite", "11", "Staff:release 1");
                Go(sc, seed, 17, (w, sh, helm, staff) =>
                {
                    if (staff == null) return;
                    long t = w.Tick;
                    if (t == 3 * 60) m1 = Fld<object>(helm, "_mode").ToString();
                    if (t == 5 * 60) m2 = Fld<object>(helm, "_mode").ToString();
                    if (t == 7 * 60) man1 = Fld<bool>(helm, "_man");
                    if (t == 9 * 60) man2 = Fld<bool>(helm, "_man");
                    if (t == 10 * 60 + 30) n1 = Fld<string>(staff, "_note");
                    if (t == 11 * 60 + 30) n2 = Fld<string>(staff, "_note");
                    if (t == 13 * 60) menu1 = Fld<bool>(staff, "_menuOn");
                    if (t == 15 * 60 + 30) { menu2 = Fld<bool>(staff, "_menuOn"); note3 = Fld<string>(staff, "_note"); }
                });
                bool o = m1 == "HUNT" && m2 == "GUARD" && man1 && !man2 && n1 == "wing satellite" && n2.Contains("release 1") && menu1 && !menu2 && note3 == "wing satellite";
                if (o) ok++;
                Info("seed " + seed + ": hunt->" + m1 + ", guard->" + m2 + ", manual->" + man1 + ", auto->" + !man2 + ", wing note '" + n1 + "', release note '" + n2 + "', menu open " + menu1 + " then executed " + !menu2 + " ('" + note3 + "')");
            }
            Check(ok == 3, "Staff:hunt / guard / manual / auto reach the Helm, wing and release answer, argument-less Run steps the menu and runs its first item after 2 s (" + ok + "/3 seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ b dead-man
        static void TestDeadman()
        {
            Console.WriteLine("-- b dead-man: KillPb Helm at 15 s with the overrides on --");
            int ok = 0; double worst = 0, best = 99;
            for (int seed = 0; seed < 5; seed++)
            {
                bool pre = false; string ovLeft = ""; long clearAt = -1; int trips = -1; bool reoverride = false; bool gunsOff = true; bool lost = false; string txt = "";
                var sc = Duel("Shrike Staff Test", "15:killpb Helm", 1200, true, "", "0.5", "Helm:hunt");
                Go(sc, seed, 22, (w, sh, helm, staff) =>
                {
                    if (staff == null) return;
                    long t = w.Tick, kill = 15 * 60;
                    if (t == kill - 1) pre = AnyOverride(sh.Main);
                    if (t >= kill && clearAt < 0 && !AnyOverride(sh.Main)) clearAt = t - kill;
                    if (clearAt >= 0 && t > kill && AnyOverride(sh.Main)) reoverride = true;
                    if (t == 18 * 60) ovLeft = OvList(sh.Main);
                    if (t == 20 * 60)
                    {
                        trips = Fld<int>(staff, "_trips"); lost = Fld<bool>(staff, "_lost");
                        foreach (var g in sh.Main.Blocks) { var gun = g.Api as SimUserGun; if (gun != null && gun.Shoot) gunsOff = false; }
                        txt = PbText(sh, "Staff");
                    }
                });
                double sec = clearAt / 60.0;
                bool o = pre && clearAt >= 0 && sec <= 1.5 + 0.2 && sec >= 0.2 && trips == 1 && lost && !reoverride && gunsOff && txt.Contains("HELM LOST");
                if (o) ok++;
                if (clearAt >= 0) { worst = Math.Max(worst, sec); best = Math.Min(best, sec); }
                Info("seed " + seed + ": overrides on at the kill " + pre + "; all released " + sec.ToString("F2") + " s after it; trips " + trips + "; overrides again " + reoverride + "; guns off " + gunsOff + (clearAt < 0 ? "; STILL OVERRIDDEN at 18 s: " + ovLeft : ""));
            }
            Check(ok == 5, "Helm killed at 15 s: the Staff releases every override within Trip Time 1.5 s (+ one Update10 step; measured " + best.ToString("F2") + "-" + worst.ToString("F2") + " s), once, nothing re-asserts them, guns off, 'HELM LOST' on its screen (" + ok + "/5 seeds)");

            // a PB that only pauses: disable at 15 s, enable at 18 s -> the Staff trips, then reports the Helm back, which flies again
            int ok2 = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int trips = -1; bool lost17 = false, lost21 = true, ov21 = false; string note = "";
                var sc = Duel("Shrike Staff Test", "15:disablepb Helm;18:enablepb Helm", 1200, true, "", "0.5", "Helm:hunt");
                Go(sc, seed, 22, (w, sh, helm, staff) =>
                {
                    if (staff == null) return;
                    long t = w.Tick;
                    if (t == 17 * 60 + 30) { lost17 = Fld<bool>(staff, "_lost"); trips = Fld<int>(staff, "_trips"); }
                    if (t == 21 * 60) { lost21 = Fld<bool>(staff, "_lost"); note = Fld<string>(staff, "_note"); ov21 = AnyOverride(sh.Main); }
                });
                if (lost17 && trips == 1 && !lost21 && note.StartsWith("Helm back") && ov21) ok2++;
                Info("seed " + seed + ": lost at 17.5 s " + lost17 + " (trips " + trips + "), lost at 21 s " + lost21 + ", note '" + note + "', Helm flying again (overrides) " + ov21);
            }
            Check(ok2 == 3, "Helm paused 3 s: one trip, 'Helm back' after it resumes, the Helm re-takes the ship (" + ok2 + "/3 seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ c Timer D + EC
        static void TestTimerEc()
        {
            Console.WriteLine("-- c Timer D and the cockpit Event Controller --");
            int okA = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int exp18 = -1, thrTotal = 0, thrOn27 = -1; long expAt = -1; int prev = 0; int trips = -1;
                var sc = Duel("Shrike Staff Test", "20:killpb Helm", 1200, true, "", "0.5", "Helm:hunt");
                SimTimer tm = null;
                Go(sc, seed, 28, (w, sh, helm, staff) =>
                {
                    if (tm == null) tm = FindBlock<SimTimer>(sh);
                    long t = w.Tick;
                    if (t == 18 * 60) exp18 = tm.Expiries;
                    if (tm.Expiries > prev) { prev = tm.Expiries; if (expAt < 0) expAt = t; }
                    if (t == 27 * 60) { thrTotal = sh.Main.Thrusters.Count; thrOn27 = ThrustersOn(sh); trips = staff != null ? Fld<int>(staff, "_trips") : -1; }
                });
                double after = expAt / 60.0 - 20;
                bool o = exp18 == 0 && expAt > 0 && after > 3.5 && after < 6.5 && thrOn27 == 0 && thrTotal > 0 && trips == 1;
                if (o) okA++;
                Info("seed " + seed + ": no expiry before 18 s " + (exp18 == 0) + "; Helm killed at 20 s, Timer D expired " + after.ToString("F2") + " s later; thrusters on at 27 s " + thrOn27 + "/" + thrTotal + "; Staff trips " + trips);
            }
            Check(okA == 3, "Helm killed in AUTO: Timer D (re-armed by the Helm every second) fires ~5 s later and cuts the thrusters; the Staff had tripped first (" + okA + "/3 seeds)");

            int okM = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int exp = -1, on = -1; bool counting = true;
                var sc = Duel("Shrike Staff Test", "9:killpb Helm", 1200, true, "", "0.5", "Helm:hunt", "3", "Staff:manual");
                SimTimer tm = null;
                Go(sc, seed, 20, (w, sh, helm, staff) =>
                {
                    if (tm == null) tm = FindBlock<SimTimer>(sh);
                    if (w.Tick == 19 * 60) { exp = tm.Expiries; counting = tm.IsCountingDown; on = ThrustersOn(sh); }
                });
                if (exp == 0 && !counting && on > 0) okM++;
            }
            Check(okM == 3, "Helm killed in MANUAL (forced, nobody seated): the stopped Timer D never fires (" + okM + "/3 seeds)");

            // a pilot sits, the Helm dies while he flies, he leaves: nobody re-arms the timer, the Event Controller (cockpit empty -> Start) does
            int okE = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int expSeated = -1; bool countingSeated = true; long expAt = -1; int prev = 0; int fired0 = -1, fired1 = -1; string hud = ""; int thrOn = -1, thrTotal = 0;
                var sc = Duel("Shrike Staff Test", "5:sit;8:killpb Helm;12:leave", 1200, true, "", "0.5", "Helm:hunt");
                SimTimer tm = null; SimEventController ec = null;
                Go(sc, seed, 24, (w, sh, helm, staff) =>
                {
                    if (tm == null) { tm = FindBlock<SimTimer>(sh); ec = FindBlock<SimEventController>(sh); }
                    long t = w.Tick;
                    if (t == 11 * 60) { expSeated = tm.Expiries; countingSeated = tm.IsCountingDown; var s = Seat(sh); if (s != null) hud = s.GetSurface(0).GetText(); }
                    if (tm.Expiries > prev) { prev = tm.Expiries; if (expAt < 0) expAt = t; }
                    if (t == 23 * 60) { fired0 = ec.Fired0; fired1 = ec.Fired1; thrOn = ThrustersOn(sh); thrTotal = sh.Main.Thrusters.Count; }
                });
                double after = expAt / 60.0 - 12;
                bool o = expSeated == 0 && !countingSeated && expAt > 0 && after > 4.8 && after < 5.6 && fired0 == 1 && fired1 == 1 && thrOn == 0 && thrTotal > 0 && hud.Contains("HELM LOST");
                if (o) okE++;
                Info("seed " + seed + ": while seated: timer expired " + (expSeated != 0) + ", counting " + countingSeated + ", cockpit HUD has HELM LOST " + hud.Contains("HELM LOST") + "; pilot left at 12 s, Timer D expired " + after.ToString("F2") + " s later; EC fired occupied/empty " + fired0 + "/" + fired1 + "; thrusters on " + thrOn + "/" + thrTotal);
            }
            Check(okE == 3, "Helm died while seated: Timer D stays stopped while he sits, the Event Controller starts it when he leaves and it fires 5 s later; cockpit HUD says HELM LOST (" + okE + "/3 seeds)");

            // control: Helm alive, pilot sits and leaves -> the timer is stopped while seated, re-armed afterwards, never fires
            int okC = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int exp = -1; bool counting = false, seated = true;
                var sc = Duel("Shrike Staff Test", "5:sit;12:leave", 1200, true, "", "0.5", "Helm:hunt");
                SimTimer tm = null;
                Go(sc, seed, 22, (w, sh, helm, staff) =>
                {
                    if (tm == null) tm = FindBlock<SimTimer>(sh);
                    if (w.Tick == 10 * 60) seated = tm.IsCountingDown;
                    if (w.Tick == 21 * 60) { exp = tm.Expiries; counting = tm.IsCountingDown; }
                });
                if (exp == 0 && counting && !seated) okC++;
            }
            Check(okC == 3, "Helm alive, pilot sits and leaves: timer stopped while seated, armed again afterwards, never expires (" + okC + "/3 seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ d latency
        static void TestLatency()
        {
            Console.WriteLine("-- d IGC latency 1 vs 2 ticks --");
            long first1 = -1, first2 = -1;
            for (int lat = 1; lat <= 2; lat++)
            {
                long first = -1;
                var sc = Duel("Shrike Staff Test", null, 900, true, "", "0.5", "Helm:hunt");
                Go(sc, 0, 3, (w, sh, helm, staff) => { if (staff != null && first < 0 && Fld<int>(staff, "_hbN") > 0) first = w.Tick; }, s => s.IgcLatencyTicks = lat);
                if (lat == 1) first1 = first; else first2 = first;
            }
            Check(first1 > 0 && first2 == first1 + 1, "first heartbeat reaches the Staff at tick " + first1 + " (latency 1) and " + first2 + " (latency 2): exactly one tick later");

            int[] wins = new int[3]; int[] falseTrips = new int[3], legit = new int[3]; double[] maxAge = new double[3]; const int N = 10;
            for (int lat = 1; lat <= 2; lat++)
                for (int seed = 0; seed < N; seed++)
                {
                    int prevTrips = 0; double age = 0; int fl = 0, lg = 0;
                    var sc = Duel("Shrike Staff Test", null, 600, false, "", "0.5", "Helm:hunt");
                    sc.DecisiveHoldSeconds = 5;
                    var r = Go(sc, seed, 120, (w, sh, helm, staff) =>
                    {
                        if (staff == null || sh.Main == null) return;
                        var hr = sh.PbsByTag("Helm")[0];
                        var rcb = Fld<IMyTerminalBlock>(helm, "_rc"); bool alive = !hr.Dead && hr.Pb.IsWorking && rcb != null && rcb.IsFunctional;   // the Helm without any controller releases itself and stops the beats: not a false alarm
                        int tr = Fld<int>(staff, "_trips");
                        if (tr > prevTrips) { if (alive) { fl++; Info("false trip: latency " + lat + " seed " + seed + " at " + (w.Tick / 60.0).ToString("F2") + " s, Helm err '" + Fld<string>(helm, "_err") + "' crash '" + Fld<string>(helm, "_crash") + "' hold " + Fld<bool>(helm, "_hold") + " HlkOn " + Fld<bool>(helm, "HlkOn")); } else lg++; prevTrips = tr; }      // a trip while the Helm PB still works is a false alarm; after its block died it is the point
                        if (tr == 0 && alive && w.Tick > 3 * 60 && Fld<int>(staff, "_hbN") > 0) age = Math.Max(age, Fld<double>(staff, "_t") - Fld<double>(staff, "_hbT"));
                    }, s => s.IgcLatencyTicks = lat);
                    if (r.WinnerTeam == 0) wins[lat]++;
                    falseTrips[lat] += fl; legit[lat] += lg; maxAge[lat] = Math.Max(maxAge[lat], age);
                }
            Info("latency 1: " + wins[1] + "/" + N + " wins, false trips " + falseTrips[1] + ", trips after the Helm PB was destroyed " + legit[1] + ", longest heartbeat gap before any trip " + maxAge[1].ToString("F2") + " s;  latency 2: " + wins[2] + "/" + N + " wins, false trips " + falseTrips[2] + ", legit " + legit[2] + ", gap " + maxAge[2].ToString("F2") + " s");
            Check(falseTrips[1] == 0 && falseTrips[2] == 0 && maxAge[1] < 1.0 && maxAge[2] < 1.0, "no false dead-man trip at either latency in " + (2 * N) + " fights (a trip is false when the Helm PB still works and has a controller)");
            Check(Math.Abs(wins[1] - wins[2]) <= 4, "win rate unchanged by one more tick of IGC latency (" + wins[1] + " vs " + wins[2] + " of " + N + ", band +-4 = 2 sigma of a difference)");
        }

        // ------------------------------------------------------------------------------------------------------------------ e instructions
        static void TestInstructions()
        {
            Console.WriteLine("-- e peak instructions per PB --");
            if (!File.Exists(IlInstrument.RewriterPath(_simDir))) { SelfTestSkips.Note("M2 instruction counts (run tools/ilcount/build.sh to enable)"); return; }
            foreach (string mode in new[] { "blocks", "il" })
            {
                int peakHelm = -1, peakStaff = -1;
                var sc = Duel("Shrike Staff Test", null, 600, false, "", "0.5", "Helm:hunt");
                var r = Go(sc, 0, 40, (w, sh, helm, staff) => { }, null, true, mode);
                foreach (var st in r.Ships)
                    if (st.IlJson != null)
                        foreach (string j in st.IlJson)
                        {
                            var m = Regex.Match(j, "\"max\":(\\d+)"); var n = Regex.Match(j, "\"script\":\"(\\w+)\"");
                            if (!m.Success || !n.Success) continue;
                            int v = int.Parse(m.Groups[1].Value);
                            if (st.Ship == 0 && n.Groups[1].Value == "ShrikeAI") peakHelm = v;
                            if (st.Ship == 0 && n.Groups[1].Value == "StaffAI") peakStaff = v;
                        }
                Check(peakHelm > 0 && peakStaff > 0 && peakHelm < 30000 && peakStaff < 30000, "40 s fight, " + mode + " model: peak instructions Helm " + peakHelm + ", Staff " + peakStaff + " (limit 50,000; target < 30,000)");
            }
        }
    }
}
