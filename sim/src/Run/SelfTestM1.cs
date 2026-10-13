// M1 acceptance tests (docs/design/control_arch.md section 7 "M1 Shrike fighter: one PB + MANUAL"), run on the REAL scripts/ShrikeAI.cs inside the
// generated test blueprints "Shrike Test" / "Shrike Test Rocket" (python3 -I tools/build_all.py --only "Shrike Test" "Shrike Test Rocket" Hornet).
// Run: SimHost.exe selftest m1   (also part of `selftest all`; the section skips when the blueprints are missing).
//   1 hand-over     seat taken -> every gyro/thruster override released within 3 ticks, none for the whole seated period, the pilot never fights an override
//   1b dampeners    the pilot's own dampener switch is restored on entry and kept across a visit of the auto mode
//   2 pilot rate    scripted pilot on the Shrike (script live) vs the same hull with the PB disabled: yaw rate and thrust acceleration within 15 %
//   3 release       seat left -> back in the saved auto mode within Release Delay + 0.3 s, lock kept, turn rate ramps from the present rate (no snap)
//   4 verbs/dead-man  manual / auto / pilot with nobody in the seat, the Run menu without an argument, Timer 'Deadman' re-armed in AUTO, stopped in MANUAL,
//                   fires 5 s after the script is killed in AUTO and does not fire when the script is killed in MANUAL
//   6 assist        'assist hud' writes the pip text on the cockpit surface; 'assist fbw' converges on the lock within the assist rate cap and the stick drops it in 0.2 s
//   7 battery       Shrike Test Rocket: a gatling [B1] and a launcher [B2]; the secondary battery fires on its own lead solution
// EVIDENCE: the pilot's stick scale/sign, the gyro feel and every real-game behaviour (CanControlShip of seats, IsUnderControl, DampenersOverride per controller,
// Timer re-arm semantics, toolbar Run without argument) are the sim's model, not measured in the game (control_arch K1-K7). Script state is read by reflection.
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
    public static class SelfTestM1
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }
        static string _repo, _simDir;
        const int Seeds = 5;

        public static int Run(string simDir, string repo)
        {
            _fail = 0; _pass = 0; _simDir = simDir; _repo = repo;
            Console.WriteLine("SelfTestM1 (ShrikeAI: MANUAL mode, hand-over, assist, dead-man heartbeat, Run menu, BATTERY-lite)");
            string bp = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            if (!File.Exists(Path.Combine(bp, "Shrike Test", "bp.sbc")) || !File.Exists(Path.Combine(bp, "Shrike Test Rocket", "bp.sbc")) || !File.Exists(Path.Combine(bp, "Hornet", "bp.sbc")))
            { SelfTestSkips.Note("M1 (build: python3 -I tools/build_all.py --only \"Shrike Test\" \"Shrike Test Rocket\" Hornet)"); Console.WriteLine("  skipped: blueprints missing"); return 0; }
            TestHandOver();
            TestFlicker();
            TestDampeners();
            TestPilotRate();
            TestRelease();
            TestVerbsAndDeadman();
            TestAssist();
            TestBattery();
            Console.WriteLine(_fail == 0 ? "SelfTestM1 passed (" + _pass + " checks)" : "SelfTestM1 FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        // ------------------------------------------------------------------------------------------------------------------ helpers
        static T Fld<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new InvalidOperationException("script has no field " + name);
            return (T)f.GetValue(o);
        }

        static string CdExtra = "";
        static Scenario Duel(string a, string b, string pilot, double dist, bool idleEnemy, string first, params string[] cmdA)
        {
            var sc = new Scenario { Kind = "1v1", Name = "m1/" + a + "-vs-" + b, StartDistanceMin = dist, StartDistanceMax = dist, MaxTicks = 120 * 60, FaceEnemies = true, DecisiveHoldSeconds = 1000 };
            sc.Teams.Add(new TeamSpec(0, a)); sc.Teams.Add(new TeamSpec(1, b));
            var sa = new ShipSpec { Model = a, Team = 0, Role = "A1", Pilot = pilot };
            sa.PbCustomData["Helm"] = "[Shrike]\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=" + (first == "idle" ? "IDLE" : "GUARD") + "\n" + CdExtra;
            sa.Commands.Add(new KeyValuePair<double, string>(0.5, "Helm:" + first));
            for (int i = 0; i + 1 < cmdA.Length; i += 2) sa.Commands.Add(new KeyValuePair<double, string>(double.Parse(cmdA[i], System.Globalization.CultureInfo.InvariantCulture), cmdA[i + 1]));
            var sb = new ShipSpec { Model = b, Team = 1, Role = "B1" };
            if (!idleEnemy) sb.Commands.Add(new KeyValuePair<double, string>(0.5, "hunt"));
            sc.Ships.Add(sa); sc.Ships.Add(sb);
            return sc;
        }

        /// <summary>Runs a scenario; the callback gets the Helm program of ship 0, its ship and the world after every tick.</summary>
        static RunResult Go(Scenario sc, int seed, double seconds, Action<World, ShipInstance, object> tick, bool automation = false)
        {
            var s = new SimSettings(); s.MaxTicks = (int)(seconds * 60) + 2; if (automation) s.BlockAutomation = true;
            var opt = new RunOptions { SimDir = _simDir };
            opt.OnTick = (w, host, ships) =>
            {
                var helm = ships[0].PbsByTag("Helm");
                if (helm.Count == 0 || helm[0].Program == null) return;
                tick(w, ships[0], helm[0].Program);
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
        static SimCockpit Seat(ShipInstance sh) { return sh.Pilot != null ? sh.Pilot.Seat : null; }
        static double Sec(World w) { return w.Tick / 60.0; }

        // ------------------------------------------------------------------------------------------------------------------ 1 hand-over
        static void TestHandOver()
        {
            Console.WriteLine("-- 1 hand-over: overrides released the tick the seat is taken, no fight, AI blocks off --");
            int okSeeds = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                int ovBefore = 0, ovSeated = 0, firstClear = -1, aiOn = 0; long sitTick = -1; int fight = -1; bool manualAt = false;
                var sc = Duel("Shrike Test", "Hornet", "10:sit;10.2:move 0 0 -1;10.6:rot 0 0.5;13:stop;16:leave", 1200, false, "hunt");
                Go(sc, seed, 19, (w, sh, prog) =>
                {
                    var seat = Seat(sh); var b = sh.Main;
                    if (seat != null && seat.IsUnderControl && sitTick < 0) sitTick = w.Tick;
                    if (sitTick < 0) { if (AnyOverride(b)) ovBefore++; return; }
                    if (w.Tick > sitTick && w.Tick <= 16 * 60 + 80)      // seated period + the release delay (the script is still MANUAL until 1.5 s after the pilot left)
                    {
                        bool ov = AnyOverride(b);
                        if (!ov && firstClear < 0) firstClear = (int)(w.Tick - sitTick);
                        if (firstClear >= 0 && ov) ovSeated++;
                        if (Fld<bool>(prog, "_aiOn")) aiOn++;
                    }
                    if (w.Tick == sitTick && AnyOverride(b)) { }
                    if (w.Tick == 12 * 60) manualAt = Fld<bool>(prog, "_man");
                    fight = sh.Pilot.OverrideFightTicks;
                });
                bool ok = sitTick > 0 && ovBefore > 0 && firstClear >= 0 && firstClear <= 3 && ovSeated == 0 && fight == 0 && aiOn == 0 && manualAt;
                if (ok) okSeeds++;
                Info("seed " + seed + ": overrides on before the seat in " + ovBefore + " ticks; cleared " + firstClear + " tick(s) after the seat was taken; overrides during the seated period " + ovSeated +
                     "; OverrideFightTicks " + fight + "; AI blocks on " + aiOn + " ticks; MANUAL at 12 s " + manualAt);
            }
            Check(okSeeds == Seeds, "seat taken: within 3 ticks every gyro/thruster override is off and stays off while seated, OverrideFightTicks 0, AI blocks off (" + okSeeds + "/" + Seeds + " seeds, pilot input starts 0.2 s after sitting)");
            // worst case: stick moved in the very tick the seat is taken -> at most the one tick before the script runs
            int worst = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int fight = -1;
                var sc = Duel("Shrike Test", "Hornet", "10:sit;10:move 0 0 -1;10:rot 0 0.5;12:stop", 1200, false, "hunt");
                Go(sc, seed, 13, (w, sh, prog) => { fight = sh.Pilot.OverrideFightTicks; });
                worst = Math.Max(worst, fight);
            }
            Check(worst <= 2, "stick in the same tick as the seat: at most " + worst + " tick(s) of override/pilot overlap (before the script's next run)");
        }

        static void TestFlicker()
        {
            // a seat held for less than Seat Debounce (0.25 s) frees the ship but never commits MANUAL; the script flies again at once
            int ok = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                bool everMan = false, hold = false, ovBefore = false, ovAfter = false; int holdTicks = 0;
                var sc = Duel("Shrike Test", "Hornet", "10:sit;10.25:leave", 1200, false, "hunt");
                Go(sc, seed, 13, (w, sh, prog) =>
                {
                    var b = sh.Main; long t = w.Tick;
                    if (t == 10 * 60 - 1) ovBefore = AnyOverride(b);
                    if (t >= 10 * 60 && Fld<bool>(prog, "_man")) everMan = true;
                    if (Fld<bool>(prog, "_hold")) { hold = true; holdTicks++; if (AnyOverride(b)) everMan = true; }
                    if (t == 12 * 60) ovAfter = AnyOverride(b);
                });
                if (hold && !everMan && holdTicks >= 10 && holdTicks <= 20 && ovBefore && ovAfter) ok++;
            }
            Check(ok == 3, "seat held 0.25 s (< debounce): ship held free while it lasts, MANUAL never committed, the script flies again afterwards (" + ok + "/3 seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 1b dampeners
        static void TestDampeners()
        {
            Console.WriteLine("-- 1b dampeners: the pilot's own V-key state comes back --");
            int ok = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                bool dAtSit = false, dAfterOff = true, dAtResit = true, autoOff = false; string store = "";
                var sc = Duel("Shrike Test", "Hornet", "8:sit;10:damp off;12:leave;18:sit", 1200, false, "hunt");
                Go(sc, seed, 20, (w, sh, prog) =>
                {
                    var b = sh.Main;
                    if (w.Tick == 8 * 60 + 30) dAtSit = b.DampenersOn;
                    if (w.Tick == 11 * 60) dAfterOff = b.DampenersOn;
                    if (w.Tick == 17 * 60 + 50) autoOff = !b.DampenersOn;
                    if (w.Tick == 18 * 60 + 30) dAtResit = b.DampenersOn;
                    if (w.Tick == 11 * 60) { prog.GetType().GetMethod("Save").Invoke(prog, null); store = ((Sandbox.ModAPI.IMyGridProgram)prog).Storage; }
                });
                var sp = store.Split(';');
                bool stored = sp.Length == 8 && sp[0] == "HUNT" && sp[5] == "1" && sp[6] == "0";   // mode;home x;y;z;set;assist level;pilot dampener switch;calibrated MaxTurn (M3: "0" = not calibrated)
                if (dAtSit && !dAfterOff && autoOff && !dAtResit && stored) ok++;
                if (seed == 0) Info("Storage after 'damp off': " + store);
                Info("seed " + seed + ": dampeners after sitting " + dAtSit + " (AUTO had them off), after 'damp off' " + dAfterOff + ", AUTO flying at 17.8 s off=" + autoOff + ", after sitting again " + dAtResit);
            }
            Check(ok == 3, "dampeners: restored to the pilot's state on entry (on), his 'off' survives the auto visit and is restored on the next entry (" + ok + "/3 seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 2 pilot rate
        static double Force(Body b, int dirIndex) { double f = 0; foreach (var t in b.Thrusters) if (t.DirIndex == dirIndex && t.Working) f += t.MaxForce; return f; }

        static void TestPilotRate()
        {
            Console.WriteLine("-- 2 pilot response with the script live vs the unscripted 'Hornet Pilot Test' (M0 baseline) and vs the analytic rate / F/m --");
            int okS = 0;
            const double gain = 1.5 * 0.6;                 // PilotRateGain x stick
            for (int seed = 0; seed < Seeds; seed++)
            {
                double[] yaw = new double[2], ratio = new double[2];
                for (int variant = 0; variant < 2; variant++)
                {
                    string model = variant == 0 ? "Shrike Test" : "Hornet Pilot Test";
                    string pilot = "5:sit;5.5:rot 0 0.6;8.5:stop;9:move 0 0 -1;12:stop";
                    var sc = Duel(model, "Hornet", pilot, 1200, true, "hunt");
                    if (variant == 1) { sc.Ships[0].PbCustomData.Clear(); sc.Ships[0].Commands.Clear(); }
                    double wSum = 0; int wN = 0; double v1 = 0, v2 = 0, f = 0, m = 1; Vector3D fwd = default(Vector3D);
                    int vv = variant;
                    var s = new SimSettings(); s.MaxTicks = 13 * 60;
                    var opt = new RunOptions { SimDir = _simDir };
                    opt.OnTick = (w, host, ships) =>
                    {
                        var b = ships[0].Main;
                        if (w.Tick >= 7 * 60 && w.Tick < 8 * 60 + 30) { Vector3D up = b.DirToWorld(new Vector3D(0, 1, 0)); wSum += Math.Abs(Vector3D.Dot(b.AngularVelocity, up)); wN++; }
                        if (w.Tick == 9 * 60 + 30) { fwd = b.DirToWorld(new Vector3D(0, 0, -1)); v1 = Vector3D.Dot(b.LinearVelocity, fwd); f = Force(b, 5); m = b.Mass; }
                        if (w.Tick == 12 * 60) v2 = Vector3D.Dot(b.LinearVelocity, fwd);
                    };
                    var r = ScenarioRunner.RunOne(sc, seed, 1UL, s, _repo, opt);
                    if (r.Error != null) Console.WriteLine("  ERROR " + r.Error);
                    yaw[vv] = wN > 0 ? wSum / wN : 0; ratio[vv] = ((v2 - v1) / 2.5) / (f / m);
                }
                bool ok = Math.Abs(yaw[0] - gain) < 0.15 * gain && Math.Abs(yaw[0] - yaw[1]) < 0.15 * yaw[1] && Math.Abs(ratio[0] - 1) < 0.15 && Math.Abs(ratio[0] - ratio[1]) < 0.15 * ratio[1];
                Info("seed " + seed + ": yaw rate " + yaw[0].ToString("F3") + " (Shrike, script live) vs " + yaw[1].ToString("F3") + " (Hornet Pilot Test, no script) vs analytic " + gain.ToString("F3") + " rad/s; thrust accel / (F/m): " + ratio[0].ToString("F3") + " vs " + ratio[1].ToString("F3"));
                if (ok) okS++;
            }
            Check(okS == Seeds, "pilot rate error < 15 % against the unscripted baseline and the analytic response, yaw and thrust (" + okS + "/" + Seeds + " seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 3 release
        static void TestRelease()
        {
            Console.WriteLine("-- 3 seat left: back to the saved auto mode, lock kept, no snap --");
            int okS = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                long leaveTick = 16 * 60, backTick = -1; bool tgtAll = true; int tgtNow = 0; double maxW = 0; bool ramp = true, hunt = false; double worstRatio = 0;
                string modeBack = "";
                var sc = Duel("Shrike Test", "Hornet", "3:sit;4:run Helm lock;5:run Helm hunt;12:rot 0 0.6;13:stop;16:leave", 600, true, "idle");
                Go(sc, seed, 26, (w, sh, prog) =>
                {
                    var b = sh.Main; long t = w.Tick;
                    bool man = Fld<bool>(prog, "_man");
                    if (t > leaveTick && backTick < 0 && !man) { backTick = t; hunt = Fld<object>(prog, "_mode").ToString() == "HUNT"; modeBack = Fld<object>(prog, "_mode").ToString(); }
                    if (t >= 12 * 60 + 30 && t <= leaveTick + 90 && Fld<object>(prog, "_tgt") == null) tgtAll = false;
                    if (backTick > 0 && t - backTick <= 180)
                    {
                        double ww = b.AngularVelocity.Length(); maxW = Math.Max(maxW, ww);
                        double cap = Math.Min(5.0, 0.0 + 5.0 * Math.Max(0, (t - backTick) / 120.0)) * 1.15 + 0.25;   // the 2 s ramp of MaxTurn from ~0 (stick idle 2 s before the pilot left), with margin
                        worstRatio = Math.Max(worstRatio, ww / cap);
                        if (ww > cap) ramp = false;
                    }
                    if (t == leaveTick + 8 * 60 && Fld<object>(prog, "_tgt") != null) tgtNow = 1;
                });
                double dtBack = backTick > 0 ? (backTick - leaveTick) / 60.0 : -1;
                bool ok = backTick > 0 && dtBack <= 1.5 + 0.3 && dtBack >= 1.4 && hunt && tgtAll && ramp && maxW <= 5.01;
                if (ok) okS++;
                Info("seed " + seed + ": auto mode " + modeBack + " " + dtBack.ToString("F2") + " s after leaving (Release Delay 1.5 s); target held all the time " + tgtAll + "; max |omega| in the first 3 s " + maxW.ToString("F2") + " rad/s (cap 5), worst omega/ramp " + worstRatio.ToString("F2") + "; target 8 s after leaving: " + (tgtNow == 1));
            }
            Check(okS == Seeds, "seat released: HUNT again within Release Delay + 0.3 s, lock kept, turn rate inside the 2 s ramp and the cap (" + okS + "/" + Seeds + " seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 4 verbs, menu, dead-man
        static void TestVerbsAndDeadman()
        {
            Console.WriteLine("-- 4 verbs with nobody in the seat, Run menu, dead-man heartbeat --");
            int okV = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                var at = new Dictionary<int, string>();
                // Helm:... commands (argument runs); the pilot script only kills the PB at 20 s
                var sc = Duel("Shrike Test", "Hornet", "20:killpb Helm", 1200, true, "hunt", "2", "Helm:manual", "6", "Helm:auto", "8", "Helm:pilot", "11", "Helm:pilot");
                SimTimer tm = null; int expiriesAt18 = -1; int thrOnAt26 = -1; int thrTotal = 0; long expAt = -1; int prevExp = 0;
                Go(sc, seed, 28, (w, sh, prog) =>
                {
                    if (tm == null) foreach (var bd in sh.Bodies) foreach (var k in bd.Blocks) if (k.Api is SimTimer) tm = (SimTimer)k.Api;
                    long t = w.Tick;
                    if (t % 60 == 30 && t / 60 < 20)
                    {
                        int s = (int)(t / 60);
                        at[s] = (Fld<bool>(prog, "_man") ? "M" : "A") + (tm != null && tm.IsCountingDown ? "T" : "-") + (AnyOverride(sh.Main) ? "O" : "-");
                    }
                    if (t == 18 * 60) expiriesAt18 = tm != null ? tm.Expiries : -1;
                    if (tm != null && tm.Expiries > prevExp) { prevExp = tm.Expiries; if (expAt < 0) expAt = t; }
                    if (t == 27 * 60) { thrTotal = sh.Main.Thrusters.Count; int n = 0; foreach (var th in sh.Main.Thrusters) if (th.Enabled) n++; thrOnAt26 = n; }
                });
                // expected: 1 s AUTO + timer counting; 3..5 s manual (verb), timer stopped, no overrides; 7 s AUTO again; 9,10 manual (pilot toggle), 12.. auto
                string a1 = at.ContainsKey(1) ? at[1] : "?", a4 = at.ContainsKey(4) ? at[4] : "?", a7 = at.ContainsKey(7) ? at[7] : "?", a10 = at.ContainsKey(10) ? at[10] : "?", a14 = at.ContainsKey(14) ? at[14] : "?";
                bool ok = a1.StartsWith("AT") && a4 == "M--" && a7.StartsWith("AT") && a10 == "M--" && a14.StartsWith("AT") && expiriesAt18 == 0 && expAt > 0 && (expAt / 60.0 - 20) > 3.5 && (expAt / 60.0 - 20) < 6.5 && thrOnAt26 == 0 && thrTotal > 0;
                if (ok) okV++;
                Info("seed " + seed + ": 1s " + a1 + " | 4s " + a4 + " | 7s " + a7 + " | 10s " + a10 + " | 14s " + a14 + "  (M=MANUAL A=AUTO, T=timer counting, O=override) ; no expiry before 18 s: " + (expiriesAt18 == 0) +
                     "; Helm killed at 20 s, timer expired at " + (expAt / 60.0).ToString("F1") + " s, thrusters on at 27 s: " + thrOnAt26 + "/" + thrTotal);
            }
            Check(okV == 3, "manual / auto / pilot toggle without a pilot; Timer re-armed in AUTO, stopped in MANUAL; fires ~5 s after the script dies in AUTO (" + okV + "/3 seeds)");

            // the script dies while in MANUAL (forced): the stopped timer never fires, nothing cuts the ship
            int okM = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                int expiries = -1; int thrOn = -1;
                var sc = Duel("Shrike Test", "Hornet", "9:killpb Helm", 1200, true, "hunt", "3", "Helm:manual");
                SimTimer tm = null;
                Go(sc, seed, 20, (w, sh, prog) =>
                {
                    if (tm == null) foreach (var bd in sh.Bodies) foreach (var k in bd.Blocks) if (k.Api is SimTimer) tm = (SimTimer)k.Api;
                    if (w.Tick == 19 * 60) { expiries = tm.Expiries; int n = 0; foreach (var th in sh.Main.Thrusters) if (th.Enabled) n++; thrOn = n; }
                }, true);
                if (expiries == 0 && thrOn > 0) okM++;
            }
            Check(okM == 3, "script killed while MANUAL: the dead-man timer was stopped, it never fires (" + okM + "/3 seeds)");

            // Run without an argument: the menu (first press: item 0 'manual'; a second press within 2 s: item 1 'auto'; executes 2 s after the last press)
            int okR = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                bool m1 = false, m2 = false, m3 = false, m4 = false; string item = "";
                var sc = Duel("Shrike Test", "Hornet", "4:run Helm;10:run Helm;11:run Helm", 1200, true, "hunt");
                Go(sc, seed, 16, (w, sh, prog) =>
                {
                    long t = w.Tick;
                    if (t == 5 * 60) { m1 = Fld<bool>(prog, "_menuOn"); item = Fld<int>(prog, "_menuI").ToString(); }
                    if (t == 7 * 60) m2 = Fld<bool>(prog, "_man") && !Fld<bool>(prog, "_menuOn");       // 2 s after the single press: 'manual' ran
                    if (t == 11 * 60 + 30) m3 = Fld<int>(prog, "_menuI") == 1;
                    if (t == 14 * 60) m4 = !Fld<bool>(prog, "_man");                                      // two presses -> item 1 'auto' ran at 13 s
                });
                if (m1 && m2 && m3 && m4) okR++;
                Info("seed " + seed + ": menu open after the first press " + m1 + " (item " + item + "), 'manual' executed " + m2 + ", second press moved to item 1 " + m3 + ", 'auto' executed " + m4);
            }
            Check(okR == 3, "argument-less Run: cycles the menu, executes the highlighted item after the wait (" + okR + "/3 seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 6 assist
        static void TestAssist()
        {
            Console.WriteLine("-- 6 assist hud / fbw --");
            int okH = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                string text = "";
                var sc = Duel("Shrike Test", "Hornet", "3:sit;4:run Helm lock", 600, true, "idle");
                Go(sc, seed, 12, (w, sh, prog) =>
                {
                    if (w.Tick == 11 * 60) { var s = Seat(sh); if (s != null) text = s.GetSurface(0).GetText(); }
                });
                bool ok = text.Contains("MANUAL ASSIST-HUD") && text.Contains("PIP  R") && text.Contains("TGT");
                if (ok) okH++;
                if (seed == 0) Console.WriteLine("  ---- cockpit surface 0 at 11 s (seed 0) ----\n" + text.Replace("\n", "\n  | ") + "\n  ----");
            }
            Check(okH == Seeds, "assist hud: lead pip and window text on the cockpit surface (" + okH + "/" + Seeds + " seeds)");

            int okF = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                double errStart = 0, errEnd = 999, maxW = 0, gMin = 1, gAtMotion = 1; int dropTicks = -1; bool held = true;
                long motion = 18 * 60;
                var sc = Duel("Shrike Test", "Hornet", "3:sit;4:run Helm lock;5:run Helm assist fbw;6:rot 0 0.2;7:stop;18:rot 0 0.5", 600, true, "idle");
                Go(sc, seed, 20, (w, sh, prog) =>
                {
                    long t = w.Tick; var b = sh.Main;
                    if (t == 7 * 60 + 10) errStart = Fld<double>(prog, "_aimErrDeg");
                    if (t >= 7 * 60 + 30 && t < motion - 1) { maxW = Math.Max(maxW, b.AngularVelocity.Length()); if (Fld<object>(prog, "_tgt") == null) held = false; }
                    if (t == motion - 2) errEnd = Fld<double>(prog, "_aimErrDeg");
                    if (t >= motion - 1 && t <= motion + 30)
                    {
                        double g = Fld<double>(prog, "_asG");
                        if (t == motion - 1) gAtMotion = g;
                        if (dropTicks < 0 && g < 0.05) dropTicks = (int)(t - motion);
                    }
                });
                // the assist rate cap is 1.5 rad/s; the PD plus the line-of-sight feed-forward and the gyro dynamics may exceed it by a little (the cap applies to the assist part)
                bool ok = held && errStart > 3 && errEnd < 2.0 && maxW <= 1.5 * 1.25 && gAtMotion > 0.95 && dropTicks >= 0 && dropTicks <= 12;
                if (ok) okF++;
                Info("seed " + seed + ": aim error " + errStart.ToString("F1") + " deg after the nudge -> " + errEnd.ToString("F2") + " deg at 17.9 s (stick idle), max |omega| " + maxW.ToString("F2") + " rad/s (assist cap 1.5), lock held " + held +
                     "; assist weight " + gAtMotion.ToString("F2") + " before the stick moved, below 0.05 after " + dropTicks + " ticks");
            }
            Check(okF == Seeds, "assist fbw: stick idle converges on the lock inside the assist rate cap; a stick motion drops the assist in <= 0.2 s (" + okF + "/" + Seeds + " seeds)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 7 battery
        static void TestBattery()
        {
            Console.WriteLine("-- 7 BATTERY-lite: gatling [B1] + launcher [B2] --");
            int okB = 0; long shots = 0; CdExtra = "[Shrike]\nFire Window Multiplier=8\n";
            for (int seed = 0; seed < 3; seed++)
            {
                string cls = ""; double next = 0; int prim = -1; int fired = 0;
                var sc = Duel("Shrike Test Rocket", "Hornet", null, 900, true, "hunt");
                var r = Go(sc, seed, 90, (w, sh, prog) =>
                {
                    if (w.Tick == 2 * 60) { var bats = Fld<Array>(prog, "_bat"); cls = Fld<string>(bats.GetValue(0), "Cls") + "/" + Fld<string>(bats.GetValue(1), "Cls"); prim = Fld<int>(prog, "_prim"); }
                    if (w.Tick % 60 == 0) { var bats = Fld<Array>(prog, "_bat"); next = Math.Max(next, Fld<double>(bats.GetValue(1), "Next")); }
                });
                if (cls == "BALL/ROCK" && prim == 0 && next > 0) okB++;
                if (r.Ships.Count > 0) shots += r.Ships[0].Shots;
                Info("seed " + seed + ": classes " + cls + ", primary B" + (prim + 1) + ", secondary last ShootOnce at script time " + next.ToString("F1") + " s, ship shots " + (r.Ships.Count > 0 ? r.Ships[0].Shots : 0) + " fired=" + fired);
            }
            CdExtra = "";
            Check(okB == 3, "BATTERY-lite: classes detected (BALL/ROCK), primary B1, the secondary battery fired on its own solution (" + okB + "/3 seeds)");
        }
    }
}
