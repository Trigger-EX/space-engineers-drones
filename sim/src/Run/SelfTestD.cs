// SLOT D. Self-checks for the runner: script assemblies and keys, termination rule, event aggregation, JSON round trip,
// scenario generation, and (selftest scenario) three trivial duels end to end plus a determinism check.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using SeSim.Core;

namespace SeSim.Run
{
    public static class SelfTestD
    {
        static int _fail;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) _fail++; }

        public static int Run(string simDir)
        {
            _fail = 0;
            Console.WriteLine("SelfTestD (runner)");
            string dll = Path.Combine(Path.Combine(simDir, "scripts"), "HornetDroneAI.dll");
            if (!File.Exists(dll)) { Console.WriteLine("  FAIL missing " + dll + " (run sim/build.sh)"); return 1; }
            Type t = Assembly.LoadFrom(dll).GetType("Program");
            bool isGp = t != null && typeof(Sandbox.ModAPI.IMyGridProgram).IsAssignableFrom(t);
            Check(isGp, "Program implements Sandbox.ModAPI.IMyGridProgram");
            var f = t == null ? null : t.GetField("_rng", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            bool det = f != null && f.FieldType == typeof(SimShim.DetRandom);
            Check(det, "script 'Random' resolves to SimShim.DetRandom (field _rng: " + (f == null ? "missing" : f.FieldType.FullName) + ")");

            TestScriptKeys(simDir);
            TestDetRandom();
            TestTermination();
            TestMetricsEvents();
            TestJson();
            TestScenarioGen();
            TestSettingsParse();
            TestInstructionCount(simDir);
            Console.WriteLine(_fail == 0 ? "SelfTestD passed" : "SelfTestD FAILED: " + _fail);
            return _fail;
        }

        // ---------------------------------------------------------------------------------------------------------
        static void TestScriptKeys(string simDir)
        {
            string repo = null;
            string d = simDir;
            for (int i = 0; i < 6 && d != null; i++) { if (File.Exists(Path.Combine(d, "defs_stats.json"))) { repo = d; break; } d = Path.GetDirectoryName(d); }
            if (repo == null) { SeSim.Core.SelfTestSkips.Note("script key test (repo root not found)"); return; }
            if (!Directory.Exists(Path.Combine(repo, "scripts"))) { SeSim.Core.SelfTestSkips.Note("script key test (no scripts/ directory in " + repo + ")"); return; }
            int n = 0, bad = 0;
            foreach (string f in Directory.GetFiles(Path.Combine(repo, "scripts"), "*.cs"))
            {
                string text = File.ReadAllText(f);
                string key = ScriptCompiler.ScriptKey(text);
                // CRLF and trailing blanks must not change the key
                string key2 = ScriptCompiler.ScriptKey(text.Replace("\n", "\r\n") + "  \n\n");
                Type ty = ScriptRegistry.GetProgramType(text, simDir);
                n++;
                if (key != key2 || ty == null || !typeof(Sandbox.ModAPI.IMyGridProgram).IsAssignableFrom(ty)
                    || ScriptRegistry.NameOf(text) != Path.GetFileNameWithoutExtension(f)) bad++;
            }
            Check(n > 0 && bad == 0, "every scripts/*.cs maps to its precompiled Program via the manifest (" + n + " scripts)");
            // a script with edited text compiles on demand, not the one from the manifest
            string edited = "int _x = 1; public Program() { } public void Main(string a, UpdateType u) { _x++; }";
            Type et = null; string err = null;
            try { et = ScriptRegistry.GetProgramType(edited, simDir); } catch (Exception e) { err = e.Message; }
            Check(et != null, "unknown program text is compiled on demand" + (err != null ? " (" + err + ")" : ""));
        }

        /// <summary>il_profiler: the block-counting rewriter reproduces Keen's counting rules on a script whose count is known by hand.</summary>
        static void TestInstructionCount(string simDir)
        {
            if (!File.Exists(IlInstrument.RewriterPath(simDir))) { SeSim.Core.SelfTestSkips.Note("instruction-count test (run tools/ilcount/build.sh to enable)"); return; }
            // blocks entered: Main 1, for body 10, if body 5, else body 5, Helper (called once) 1 + its while body 3, lambda called 4 times 4, try/catch: catch 1 = 30
            string txt = "int _r; public Program() { } void Helper() { int k = 0; while (k < 3) { k++; } }\n"
                       + "public void Main(string a, UpdateType u) { for (int i = 0; i < 10; i++) { if (i % 2 == 0) _r++; else _r--; } Helper();\n"
                       + "  var l = new List<int> { 1, 2, 3, 4 }; l.ForEach(x => _r += x); int z = 0; try { z = 5 / (z - z); } catch (Exception) { _r = 0; } }";
            try
            {
                Type t = ScriptRegistry.GetProgramType(txt, simDir, "blocks");
                var o = (Sandbox.ModAPI.IMyGridProgram)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(t);
                var settings = new SimSettings { CountInstructions = true };
                o.Runtime = new SimRuntimeInfo(settings);
                SimShim.IlCounter.Begin(ScriptRegistry.SitesOf(txt, "blocks").Length, 0);
                t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(o, null);   // binds Main like the game
                SimShim.IlCounter.Begin(ScriptRegistry.SitesOf(txt, "blocks").Length, 0);
                o.Main("", Sandbox.ModAPI.Ingame.UpdateType.Terminal);
                int n = SimShim.IlCounter.Total;
                Check(n == 30 && o.Runtime.CurrentInstructionCount == 30, "instruction counter counts blocks entered like Keen's rewriter (got " + n + ", expected 30)");
                SimShim.IlCounter.Begin(ScriptRegistry.SitesOf(txt, "blocks").Length, 20);
                bool thrown = false;
                try { o.Main("", Sandbox.ModAPI.Ingame.UpdateType.Terminal); } catch (SimShim.ScriptTooComplexException) { thrown = true; }
                Check(thrown, "limit enforcement throws ScriptTooComplexException (not swallowed by the script's own catch)");
            }
            catch (Exception e) { Check(false, "instruction-count test: " + e.GetType().Name + ": " + e.Message); }
        }

        static void TestDetRandom()
        {
            SimShim.DetRandomSeed.Set(12345);
            var a = new SimShim.DetRandom(); var b = new SimShim.DetRandom();
            ulong st = SimShim.DetRandomSeed.Get();
            SimShim.DetRandomSeed.Set(12345);
            var a2 = new SimShim.DetRandom(); var b2 = new SimShim.DetRandom();
            Check(a.Next() == a2.Next() && b.Next() == b2.Next() && SimShim.DetRandomSeed.Get() == st, "DetRandom: same seed state => same streams and same end state");
            SimShim.DetRandomSeed.Set(1); var c = new SimShim.DetRandom(); var d = new SimShim.DetRandom();
            Check(c.Next() != d.Next() || c.Next() != d.Next(), "DetRandom: successive 'new Random()' calls give different streams");
        }

        static void TestTermination()
        {
            var t = new Termination(2, 5);
            var cap = new bool[] { true, true };
            Check(!t.Update(cap, 1), "termination: both capable -> running");
            cap[1] = false;
            Check(!t.Update(cap, 10), "termination: side B beaten but hold not elapsed");
            Check(!t.Update(cap, 14.9), "termination: hold 4.9 s not enough");
            Check(t.Update(cap, 15) && t.Winner == 0 && t.Outcome == "win" && Math.Abs(t.DecisiveAt - 10) < 1e-9, "termination: win for A after 5 s hold, decisive at 10 s");
            var t2 = new Termination(2, 5);
            cap = new bool[] { true, false };
            t2.Update(cap, 1); cap[1] = true; t2.Update(cap, 3); cap[1] = false;
            Check(!t2.Update(cap, 7) && !t2.Update(cap, 7.5), "termination: a side that recovers resets the hold timer");
            var t3 = new Termination(2, 5);
            cap = new bool[] { true, true }; t3.Update(cap, 0); cap[0] = false; cap[1] = false;
            t3.Update(cap, 20);
            Check(t3.Update(cap, 25) && t3.Outcome == "mutual-kill" && t3.Winner == -1, "termination: both beaten after having fought = mutual-kill");
            var t4 = new Termination(2, 5);
            cap = new bool[] { false, false }; t4.Update(cap, 0.2);
            Check(t4.Update(cap, 5.2) && t4.Outcome == "no-contest", "termination: nobody ever capable = no-contest");
            var t5 = new Termination(3, 5);
            cap = new bool[] { true, false, false }; t5.Update(cap, 1);
            Check(t5.Update(cap, 6) && t5.Winner == 0, "termination: three teams, two beaten");
        }

        static ShipInstance Mk(int i, string model, int team)
        {
            var sp = new ShipSpec { Model = model, Team = team, Role = "x" };
            return new ShipInstance { Index = i, Spec = sp, Team = team };
        }

        static void TestMetricsEvents()
        {
            var ships = new List<ShipInstance> { Mk(0, "Hornet", 0), Mk(1, "Gnat", 1), Mk(2, "Gnat", 0) };
            var m = new Metrics(ships);
            Ev(m, EventKind.Shot, 0, -1, 0, 1); Ev(m, EventKind.Shot, 0, -1, 0, 1);
            Ev(m, EventKind.Hit, 0, 1, 90, 2);
            Ev(m, EventKind.Hit, 0, 2, 90, 3); Ev(m, EventKind.FriendlyFire, 0, 2, 90, 3);   // same friendly hit reported twice
            Ev(m, EventKind.FriendlyFire, 0, 2, 50, 4);                                      // only the FriendlyFire event
            Ev(m, EventKind.BlockDestroyed, 0, 1, 12, 5);
            Ev(m, EventKind.Collision, 0, 1, 5, 6);
            Ev(m, EventKind.ScriptException, 2, -1, 0, 7, "boom");
            m.FlushFriendlyFire();   // resolves pending FriendlyFire events
            var r = m.Result.Ships;
            Check(r[0].Shots == 2 && r[0].Hits == 1 && Math.Abs(r[0].DamageDealt - 90) < 1e-9, "metrics: shots, enemy hits and damage dealt");
            Check(r[1].DamageTaken == 90 && r[1].BlocksLost == 1 && r[0].BlocksKilled == 1, "metrics: damage taken, blocks lost / killed");
            Check(r[0].FriendlyHitsDealt == 2 && Math.Abs(r[0].FriendlyDamageDealt - 140) < 1e-9, "metrics: friendly fire counted once per damage (Hit+FriendlyFire dedupe)");
            Check(r[0].Collisions == 1 && r[1].Collisions == 1, "metrics: collision counted for both ships");
            Check(r[2].ScriptExceptions == 1 && r[2].FirstException == "boom", "metrics: script exception recorded");
        }

        static string DefsPath()
        {
            string d = Path.GetDirectoryName(typeof(SelfTestD).Assembly.Location);
            for (int i = 0; i < 6 && d != null; i++) { string p = Path.Combine(d, "defs_stats.json"); if (File.Exists(p)) return p; d = Path.GetDirectoryName(d); }
            return "defs_stats.json";
        }

        static void Ev(Metrics m, EventKind k, int a, int b, double v, long tick, string text = null)
        {
            var e = new SimEvent { Kind = k, ShipA = a, ShipB = b, Value = v, Tick = tick, Text = text };
            m.On(ref e);
        }

        static RunResult Sample()
        {
            var r = new RunResult { Scenario = "1v1/A-vs-B \"q\"", Kind = "1v1", SeedIndex = 3, Seed = 18446744073709551000UL, Outcome = "win", WinnerTeam = 1, DurationS = 61.25, WallMs = 1234.5, CpuMs = 1200, TimeToKillS = 56.2, SettingsHash = "abc" };
            r.Ships.Add(new ShipStats { Ship = 0, Model = "Hornet", Team = 0, Shots = 100, Hits = 7, DamageDealt = 630, DamageTaken = 5000.5, BlocksStart = 400, BlocksEnd = 120, Script = "HornetDroneAI", ScriptRuns = 3600, FirstException = "a\nb" });
            r.Ships.Add(new ShipStats { Ship = 1, Model = "Gnat", Team = 1, Shots = 300, Hits = 40, DamageDealt = 3600, FriendlyDamageDealt = 90, FriendlyHitsDealt = 1, StuckOrCircling = true, StuckKind = "slow", Capable = true, Alive = true });
            return r;
        }

        static void TestJson()
        {
            var r = Sample();
            var sb = new StringBuilder(); JsonOut.AppendRun(sb, r);
            RunResult p = null; string err = null;
            try { p = JsonOut.ParseRun(sb.ToString()); } catch (Exception e) { err = e.Message; }
            Check(p != null, "json: run line parses" + (err != null ? " (" + err + ")" : ""));
            if (p == null) return;
            var sb2 = new StringBuilder(); JsonOut.AppendRun(sb2, p);
            Check(sb.ToString() == sb2.ToString(), "json: AppendRun(ParseRun(line)) == line");
            Check(p.Seed == r.Seed && p.Scenario == r.Scenario && p.Ships[0].FirstException == "a\nb" && p.Ships[1].StuckKind == "slow", "json: seed (ulong), escaped strings and flags survive");
            string path = Path.Combine(Path.GetTempPath(), "sesim-selftest-summary-" + Environment.TickCount + ".csv");
            try
            {
                var list = new List<RunResult> { r, Sample() };
                JsonOut.WriteSummaryCsv(path, list);
                string[] lines = File.ReadAllLines(path);
                Check(lines.Length == 2 && lines[1].StartsWith("\"1v1/A-vs-B \"\"q\"\"\",1v1,2,0,2,0,0,0,"), "csv: header + one quoted scenario row with 0 winsA / 2 winsB");
            }
            finally { try { File.Delete(path); } catch (Exception) { } }
        }

        static void TestScenarioGen()
        {
            var one = ScenarioGen.Matrix1v1();
            Check(one.Count == 91, "scenarios: 91 one-on-one pairs including mirrors (" + one.Count + ")");
            var all = ScenarioGen.All();
            var names = new HashSet<string>();
            bool dup = false;
            foreach (var s in all) if (!names.Add(s.Name)) dup = true;
            Check(!dup && all.Count > 400, "scenarios: all names unique (" + all.Count + " scenarios)");
            int swarmMax = 0, pirate = 0;
            foreach (var s in all) { if (s.Kind == "swarm" && s.Ships.Count > swarmMax) swarmMax = s.Ships.Count; if (s.Kind == "pirate") pirate++; }
            Check(swarmMax == 13, "scenarios: swarm of 12 Gnats vs one ship has 13 ships (" + swarmMax + ")");
            var u = ScenarioGen.Unfair();
            int c12 = 0; foreach (var s in u) if (s.Kind == "1v2") c12++;
            Check(c12 == 156, "scenarios: 156 ordered 1v2 pairs (" + c12 + ")");
            var sel = ScenarioGen.Select("^1v1/Hornet-vs-", null);
            Check(sel.Count > 0 && sel[0].Ships[0].Commands.Count == 1 && sel[0].Ships[0].Commands[0].Value == "hunt", "scenarios: filter works, script drones get the default 'hunt' command");
            ulong s1 = ScenarioRunner.ScenarioSeed(1, "x", 0), s2 = ScenarioRunner.ScenarioSeed(1, "x", 1), s3 = ScenarioRunner.ScenarioSeed(1, "y", 0);
            Check(s1 != s2 && s1 != s3 && s1 == ScenarioRunner.ScenarioSeed(1, "x", 0), "scenarios: seeds depend on name and index, deterministic");
        }

        static void TestSettingsParse()
        {
            var s = new SimSettings();
            s.ApplyOverride("gyro-pitch-sign", "1");
            Check(s.GyroPitchSign == 1, "cli: --gyro-pitch-sign 1 reaches SimSettings");
        }

        // ---------------------------------------------------------------------------------------------------------
        /// <summary>3 trivial duels end to end + determinism. Skipped (not failed) while another slot still throws NotImplemented.</summary>
        public static int RunScenarioTests(string simDir, string repo)
        {
            _fail = 0;
            Console.WriteLine("SelfTestD scenarios (end to end)");
            if (!Directory.Exists(Path.Combine(Path.Combine(repo, "out"), "Blueprints"))) { SeSim.Core.SelfTestSkips.Note("scenario duels: out/Blueprints missing"); return 0; }
            var settings = new SimSettings();
            var opt = new RunOptions { SimDir = simDir };
            string[] names = { "1v1/Hornet-vs-Hornet", "1v1/Gnat-vs-Midge", "1v1/Stinger-vs-Wasp" };
            var results = new List<RunResult>();
            foreach (string nm in names)
            {
                Scenario sc = null;
                foreach (var s in ScenarioGen.Matrix1v1()) if (s.Name == nm) sc = s;
                if (sc == null) { Check(false, "scenario " + nm + " exists"); continue; }
                sc.MaxTicks = 20 * 60;      // 20 s are enough to see scheduling and spawning
                var r = ScenarioRunner.RunOne(sc, 0, 1, settings, repo, opt);
                results.Add(r);
                if (r.Outcome == "error" && r.Error != null && r.Error.Contains("NotImplementedException"))
                {
                    SeSim.Core.SelfTestSkips.Note(nm + ": another slot is not implemented yet: " + FirstLine(r.Error, 3));
                    continue;
                }
                Check(r.Outcome != "error", nm + " runs without engine error (" + r.Outcome + " after " + r.DurationS.ToString("F1") + " s, " + r.WallMs.ToString("F0") + " ms)" + (r.Error != null ? "\n" + r.Error : ""));
                long runs = 0, exc = 0; foreach (var s in r.Ships) { runs += s.ScriptRuns; exc += s.ScriptExceptions; }
                bool scripted = nm != "1v1/Gnat-vs-Midge";
                if (scripted) Check(runs > 20 * 60 * 0.5, nm + ": scripts ran on schedule (" + runs + " Main calls)");
                Check(exc == 0, nm + ": no script exceptions" + (exc > 0 ? " (" + FirstExc(r) + ")" : ""));
                Check(r.Ships.Count == 2 && r.Ships[0].BlocksStart > 10, nm + ": ships spawned (" + (r.Ships.Count > 0 ? r.Ships[0].BlocksStart.ToString() : "-") + " blocks)");
            }
            // determinism: the first duel twice
            {
                Scenario sc = null;
                foreach (var s in ScenarioGen.Matrix1v1()) if (s.Name == names[0]) sc = s;
                sc.MaxTicks = 10 * 60;
                var a = ScenarioRunner.RunOne(sc, 7, 1, settings, repo, opt);
                if (a.Outcome == "error" && a.Error != null && a.Error.Contains("NotImplementedException")) SeSim.Core.SelfTestSkips.Note("determinism: engine not complete yet");
                else
                {
                    var b = ScenarioRunner.RunOne(sc, 7, 1, settings, repo, opt);
                    var sa = new StringBuilder(); var sb = new StringBuilder();
                    JsonOut.AppendRun(sa, a, false); JsonOut.AppendRun(sb, b, false);
                    Check(sa.ToString() == sb.ToString(), "determinism: two runs of the same seed give identical result lines");
                }
            }
            Console.WriteLine(_fail == 0 ? "SelfTestD scenarios passed" : "SelfTestD scenarios FAILED: " + _fail);
            return _fail;
        }

        static string FirstLine(string s, int lines)
        {
            var parts = s.Split('\n'); var sb = new StringBuilder();
            for (int i = 0; i < parts.Length && i < lines; i++) sb.Append(parts[i].Trim()).Append(" | ");
            return sb.ToString();
        }

        static string FirstExc(RunResult r) { foreach (var s in r.Ships) if (s.FirstException != null) return s.FirstException; return "?"; }
    }
}
