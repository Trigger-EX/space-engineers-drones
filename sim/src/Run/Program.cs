// SLOT D. CLI entry point: mono out/sim/SimHost.exe <command> [options]
//   selftest [physics|api|combat|runner|audit|m0|m1|m2|m3|m4|scenario|all]   engine self-checks by group (default: the first four);
//                                 `selftest scenario` (or --selftest scenario) also runs 3 short duels end to end
//   list [--filter RX] [--kind K] print scenario names with job counts
//   run   --filter REGEX [--seeds N] [--jobs 4] [--seed 1] [--shard i/n] [--out DIR] [--resume] [--<setting> value ...]
//   one   --scenario NAME --seed-index K [--trace FILE] [--trace-every N] [--profile] [--echo] [--echo-every S [--echo-ship I]]
//                                 single run, optional trace CSV; --echo-every prints ship I's (default 0) PB screens every S seconds
//   scenario overrides for run/one: --commands "T:cmd;..." (script drones' start commands), --start-distance MIN:MAX,
//         --face (ships start pointing at the enemy), --custom-data "INI with \n" (PB CustomData of the script drones)
//   summarize --in runs.jsonl [--out summary.csv]    rebuild summary.csv from a runs.jsonl
//   dock-launch [--model Hornet] [--seconds 30] [--release-at 3] ...   printed drone merged to a dummy printer, merge off, no _JOIN (sim/src/Run/DockLaunch.cs)
//   printer-test [--model "Mobile Hive"] [--fault none|noproj|halfload|...] [--script-file F]   Hive Mother on the real printer blueprint with real-game-like faulty projectors (sim/src/Run/PrinterTest.cs)
//   multipb-probe [--seeds 3] [--model "Warden Wing"]   several synthetic PBs + timers on one grid: IGC latency/throughput, TryRun, timer chains (sim/src/Run/MultiPbProbe.cs)
//   follow-test [--model Hornet] [--count 3] [--path cruise|weave|lost|foot|fight] ...   FOLLOW mode vs a moving owner (sim/src/Run/FollowTest.cs)
//   avoid-test [--model Hornet] [--count 3] [--bystander Hornet] [--beacon]   drones in HUNT with a parked friendly on their line: friendly avoidance (sim/src/Run/AvoidTest.cs)
//   wing-test [--model Ballista] [--count 3] [--spacing 120] [--enemy Hornet] [--cue]   N copies side by side running the real script: closest approach of every pair (sim/src/Run/WingTest.cs)
//   compile-scripts               (re)build out/sim/scripts/*.dll + manifest via ScriptCompiler (same result as build.sh)
//   M0 (docs/design/control_arch.md): --pilot "0:sit;5:move 0 0 -1;20:leave" [--pilot-team 0] puts a ScriptedPilot into the cockpit of our ships (verbs: ScriptedPilot.cs),
//         --pb-custom-data "Helm=<ini>|Staff=<ini>" (CustomData per PB tag), --enemy-scripted (enemies from --enemies run their own PB scripts: "Helm:hunt",
//         "Staff:wing satellite"; implies --block-automation), --block-automation (Timer / Event Controller / Button Panel toolbars run), commands "Tag:arg"
//         (e.g. --commands "0.5:Helm:hunt") go to the PBs whose name starts with [Tag] only.
//   --seeds defaults to DefaultSeeds (a quick check). Pick the count the task needs (CLAUDE.md "Simulations"); the big matrices
//   live in pc/plans/*.plan, which always pass --seeds explicitly.
// Options not listed here are SimSettings fields in kebab case (e.g. --gyro-pitch-sign 1, --friendly-fire false,
// --infinite-ammo false); unknown options are an error. --cap-seconds N changes the fight cap (default 300).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SeSim.Core;

namespace SeSim.Run
{
    public static class SimProgram
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        /// <summary>Seeds per scenario when --seeds is not given: a quick check, not a statistical result.</summary>
        const int DefaultSeeds = 10;

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "help" || args[0] == "--help")
            {
                Console.WriteLine("usage: SimHost.exe selftest [physics|api|combat|runner|scenario|all] | list | run [opts] | one [opts] | summarize --in FILE | compile-scripts  (see docs/sim-design.md)");
                return 0;
            }
            try { return Dispatch(args); }
            catch (NotImplementedException e) { Console.Error.WriteLine("not implemented yet: " + e.StackTrace); return 3; }
            catch (ArgumentException e) { Console.Error.WriteLine("error: " + e.Message); return 2; }
        }

        // ---------------------------------------------------------------------------------------------------------
        sealed class Opts
        {
            public readonly Dictionary<string, string> Kv = new Dictionary<string, string>();
            public readonly List<string> Positional = new List<string>();
            public string Get(string k, string d) { string v; return Kv.TryGetValue(k, out v) ? v : d; }
            public bool Has(string k) { return Kv.ContainsKey(k); }
            public int Int(string k, int d) { string v; if (!Kv.TryGetValue(k, out v)) return d; int r; if (!int.TryParse(v, NumberStyles.Integer, Inv, out r)) throw new ArgumentException("--" + k + " needs an integer"); return r; }
            public double Dbl(string k, double d) { string v; if (!Kv.TryGetValue(k, out v)) return d; double r; if (!double.TryParse(v, NumberStyles.Float, Inv, out r)) throw new ArgumentException("--" + k + " needs a number"); return r; }
        }

        static readonly HashSet<string> Flags = new HashSet<string> { "profile", "resume", "quiet", "no-summary", "echo", "face", "face-away", "endless", "allow-mismatch" };
        static readonly HashSet<string> Known = new HashSet<string> { "filter", "kind", "seeds", "seed", "jobs", "shard", "out", "batch", "scenario", "seed-index", "seed-start", "trace", "trace-every", "profile", "resume", "quiet", "no-summary", "repo", "cap-seconds", "unfair", "in", "echo", "echo-every", "echo-ship", "commands", "start-distance", "face", "face-away", "custom-data", "enemies", "enemy-counts", "enemy-brain", "enemy-fleets", "endless", "target", "max-drones", "min-drones", "models", "min-fights", "allow-mismatch", "pilot", "pilot-team", "pb-custom-data", "events", "event-team" };

        static Opts Parse(string[] a, int start, SimSettings settings)
        {
            var o = new Opts();
            for (int i = start; i < a.Length; i++)
            {
                string t = a[i];
                if (!t.StartsWith("--", StringComparison.Ordinal)) { o.Positional.Add(t); continue; }
                string name = t.Substring(2);
                string val = null;
                int eq = name.IndexOf('=');
                if (eq > 0) { val = name.Substring(eq + 1); name = name.Substring(0, eq); }
                string key = name.ToLowerInvariant();
                bool isFlag = Flags.Contains(key);
                if (val == null)
                {
                    bool nextIsValue = i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal);
                    if (isFlag) val = nextIsValue && (a[i + 1] == "true" || a[i + 1] == "false") ? a[++i] : "true";
                    else if (nextIsValue) val = a[++i];
                    else if (settings.IsSetting(key)) val = "true";    // bare bool setting
                    else throw new ArgumentException("option --" + name + " needs a value");
                }
                if (Known.Contains(key)) o.Kv[key] = val;
                else if (settings.IsSetting(key)) settings.ApplyOverride(key, val);
                else throw new ArgumentException("unknown option --" + name);
            }
            return o;
        }

        static string FindRepoRoot(string hint)
        {
            if (hint != null) return Path.GetFullPath(hint);
            string d = Path.GetDirectoryName(typeof(SimProgram).Assembly.Location);
            for (int i = 0; i < 6 && d != null; i++)
            {
                if (File.Exists(Path.Combine(d, "defs_stats.json"))) return d;
                d = Path.GetDirectoryName(d);
            }
            return Directory.GetCurrentDirectory();
        }

        static bool CheckBlueprints(string repo)
        {
            if (Directory.Exists(Path.Combine(Path.Combine(repo, "out"), "Blueprints"))) return true;
            Console.Error.WriteLine("out/Blueprints is missing: run 'python3 -I tools/build_all.py' in " + repo + " (about 3 minutes)");
            return false;
        }

        static int Dispatch(string[] args)
        {
            string simDir = Path.GetDirectoryName(typeof(SimProgram).Assembly.Location);
            string cmd = args[0];
            if (cmd == "--selftest") cmd = "selftest";
            var settings = new SimSettings();
            switch (cmd)
            {
                case "selftest":
                {
                    // groups: physics (A), api (B), combat (C), runner (D), scenario (3 real duels end to end), all.
                    // No group = physics+api+combat+runner; "scenario" alone = those four plus the duels (old behaviour).
                    // --strict: a section that could not run (missing blueprints / scripts / ilcount) counts as a failure
                    var groups = new HashSet<string>();
                    for (int i = 1; i < args.Length; i++) groups.Add(args[i].TrimStart('-').ToLowerInvariant());
                    bool strict = groups.Remove("strict");
                    SelfTestSkips.Count = 0;
                    foreach (string g in groups)
                        if (g != "physics" && g != "api" && g != "combat" && g != "runner" && g != "audit" && g != "m0" && g != "m1" && g != "m2" && g != "m3" && g != "m4" && g != "m5" && g != "m6" && g != "scenario" && g != "all")
                            throw new ArgumentException("unknown selftest group '" + g + "' (physics, api, combat, runner, audit, m0, m1, m2, m3, m4, m5, m6, scenario, all)");
                    bool all = groups.Count == 0 || groups.Contains("all") || (groups.Count == 1 && groups.Contains("scenario"));
                    int f = 0;
                    if (all || groups.Contains("physics")) f += SeSim.Core.SelfTestA.Run();
                    if (all || groups.Contains("api")) f += SeSim.Api.SelfTestB.Run();
                    if (all || groups.Contains("combat")) f += SeSim.Combat.SelfTestC.Run();
                    if (all || groups.Contains("audit")) f += SeSim.Core.SelfTestAudit.Run();
                    if (all || groups.Contains("runner")) f += SelfTestD.Run(simDir);
                    if (all || groups.Contains("m0")) f += SelfTestM0.Run(simDir, FindRepoRoot(null));
                    if (all || groups.Contains("m1")) f += SelfTestM1.Run(simDir, FindRepoRoot(null));
                    if (all || groups.Contains("m2")) f += SelfTestM2.Run(simDir, FindRepoRoot(null));
                    if (all || groups.Contains("m3")) f += SelfTestM3.Run(simDir, FindRepoRoot(null));
                    if (all || groups.Contains("m4")) f += SelfTestM4.Run(simDir, FindRepoRoot(null));
                    if (all || groups.Contains("m5")) f += SelfTestM5.Run(simDir, FindRepoRoot(null));
                    if (all || groups.Contains("m6")) f += SelfTestM6.Run(simDir, FindRepoRoot(null));
                    if (groups.Contains("scenario") || groups.Contains("all")) f += SelfTestD.RunScenarioTests(simDir, FindRepoRoot(null));
                    if (strict && SelfTestSkips.Count > 0) { Console.WriteLine("SELFTEST --strict: " + SelfTestSkips.Count + " section(s) skipped"); f++; }
                    Console.WriteLine(f == 0 ? "SELFTEST OK" + (SelfTestSkips.Count > 0 ? " (" + SelfTestSkips.Count + " sections skipped; --strict fails on that)" : "") : "SELFTEST FAILURES: " + f);
                    return f == 0 ? 0 : 1;
                }
                case "list":
                {
                    var o = Parse(args, 1, settings);
                    ApplyGenOptions(o, settings);
                    var l = ScenarioGen.Select(o.Get("filter", null), o.Get("kind", null));
                    int seeds = o.Int("seeds", DefaultSeeds);
                    var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    foreach (var sc in l)
                    {
                        Console.WriteLine(sc.Name);
                        int c; counts.TryGetValue(sc.Kind, out c); counts[sc.Kind] = c + 1;
                    }
                    var sb = new StringBuilder();
                    foreach (var kv in counts) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
                    Console.Error.WriteLine(l.Count + " scenarios (" + sb.ToString().Trim() + "), " + (l.Count * (long)seeds) + " runs at " + seeds + " seeds");
                    return 0;
                }
                case "dock-launch":
                    return DockLaunch.Run(args, simDir, FindRepoRoot(DockLaunch.RepoArg(args)));
                case "printer-test":
                    return PrinterTest.Run(args, simDir, FindRepoRoot(DockLaunch.RepoArg(args)));
                case "multipb-probe":
                    return MultiPbProbe.Run(args, simDir, FindRepoRoot(DockLaunch.RepoArg(args)));
                case "follow-test":
                    return FollowTest.Run(args, simDir, FindRepoRoot(DockLaunch.RepoArg(args)));
                case "avoid-test":
                    return AvoidTest.Run(args, simDir, FindRepoRoot(DockLaunch.RepoArg(args)));
                case "wing-test":
                    return WingTest.Run(args, simDir, FindRepoRoot(DockLaunch.RepoArg(args)));
                case "compile-scripts":
                    return CompileScripts(simDir, FindRepoRoot(null));
                case "summarize":
                {
                    var o = Parse(args, 1, settings);
                    string inp = o.Get("in", null);
                    if (inp == null) throw new ArgumentException("summarize needs --in runs.jsonl");
                    var rs = LoadRuns(inp);
                    string outp = o.Get("out", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inp)), "summary.csv"));
                    JsonOut.WriteSummaryCsv(outp, rs);
                    Console.WriteLine(rs.Count + " runs -> " + outp);
                    return 0;
                }
                case "one":
                    return CmdOne(args, simDir, settings);
                case "run":
                    return CmdRun(args, simDir, settings);
                case "search":
                    return CmdSearch(args, simDir, settings);
                default:
                    throw new ArgumentException("unknown command '" + args[0] + "'");
            }
        }

        /// <summary>Scenario generator switches: --unfair curated|full; --enemies DIR (blueprint folders fought "1vX" by every armed
        /// model), --enemy-counts 1,2,3 (team sizes against each enemy), --enemy-brain pirate|hold.</summary>
        static void ApplyGenOptions(Opts o, SimSettings settings)
        {
            ScenarioGen.EnemyScripted = settings.EnemyScripted;       // --enemy-scripted (a SimSettings flag): enemies run their own PB scripts
            if (o.Has("unfair")) ScenarioGen.UnfairMode = o.Get("unfair", "curated");
            if (o.Has("enemies")) ScenarioGen.EnemyDir = Path.GetFullPath(o.Get("enemies", "enemies"));
            if (o.Has("enemy-counts"))
            {
                var l = new List<int>();
                foreach (string p in o.Get("enemy-counts", "").Split(','))
                {
                    int c;
                    if (p.Trim().Length == 0) continue;
                    if (!int.TryParse(p.Trim(), NumberStyles.Integer, Inv, out c) || c < 1 || c > 64) throw new ArgumentException("--enemy-counts needs numbers 1..64 separated by commas");
                    l.Add(c);
                }
                if (l.Count == 0) throw new ArgumentException("--enemy-counts is empty");
                ScenarioGen.EnemyCounts = l.ToArray();
            }
            if (o.Has("enemy-fleets"))
            {
                // inline "3xHornet+2xGnat;Basilisk+Gnat" or a file with one fleet per line ('#' comments)
                string v = o.Get("enemy-fleets", "");
                string[] items = File.Exists(v) ? File.ReadAllLines(v) : v.Split(';');
                var l = new List<string[]>();
                foreach (string it in items)
                {
                    string t = it; int h = t.IndexOf('#'); if (h >= 0) t = t.Substring(0, h);
                    if (t.Trim().Length > 0) l.Add(ScenarioGen.ParseFleet(t));
                }
                if (l.Count == 0) throw new ArgumentException("--enemy-fleets is empty");
                ScenarioGen.EnemyFleets = l;
            }
            if (o.Has("enemy-brain"))
            {
                string b = o.Get("enemy-brain", "pirate");
                if (b != "pirate" && b != "hold") throw new ArgumentException("--enemy-brain must be pirate or hold");
                ScenarioGen.EnemyBrain = b;
            }
        }

        /// <summary>--face: ships start pointing at the nearest enemy. --commands "ai off;hunt;2:lock" ("T:cmd" sends at T s): replaces the start commands of every scripted drone whose default is "hunt"
        /// (sent at 0.5 s in the given order). --start-distance MIN:MAX overrides the team-centroid distance range (m).
        /// Both change the scenario (the run name is unchanged), so they are recorded in meta.json via the command line.</summary>
        static void ApplyScenarioOverrides(Opts o, List<Scenario> scs)
        {
            if (o.Has("commands"))
            {
                string[] parts = o.Get("commands", "").Split(';');
                foreach (var sc in scs)
                    foreach (var sp in sc.Ships)
                    {
                        bool hunt = false;
                        foreach (var kv in sp.Commands) if (kv.Value == "hunt") hunt = true;
                        if (!hunt) continue;
                        sp.Commands.Clear();
                        foreach (string c0 in parts)
                        {
                            string c = c0.Trim(); if (c.Length == 0) continue;
                            double t = 0.5; int colon = c.IndexOf(':');
                            double tt;
                            if (colon > 0 && double.TryParse(c.Substring(0, colon), NumberStyles.Float, Inv, out tt)) { t = tt; c = c.Substring(colon + 1).Trim(); }
                            sp.Commands.Add(new KeyValuePair<double, string>(t, c));
                        }
                    }
            }
            if (o.Has("events"))
            {   // harness events ('!kill-thrust F'): appended to the commands of every scripted drone of --event-team (default 0)
                var evs = SimEvents.Parse(o.Get("events", ""));
                int et = int.Parse(o.Get("event-team", "0"), Inv);
                foreach (var sc in scs) foreach (var sp in sc.Ships) if (sp.Team == et && sp.Commands.Count > 0) foreach (var ev in evs) sp.Commands.Add(ev);
            }
            if (o.Has("custom-data"))
            {
                // PB CustomData for every scripted drone that gets the default "hunt" (\n in the argument = newline), e.g.
                // --custom-data "[Hornet]\nProjectile Speed=800" (the script keeps its defaults for every key not given)
                string cd = o.Get("custom-data", "").Replace("\\n", "\n");
                foreach (var sc in scs)
                    foreach (var sp in sc.Ships)
                    {
                        bool scripted = false;
                        foreach (var kv in sp.Commands) if (kv.Value == "hunt") scripted = true;
                        if (scripted || o.Has("commands")) sp.CustomDataOverride = cd;
                    }
            }
            if (o.Has("pilot"))
            {
                // --pilot "0:sit;5:move 0 0 -1;20:leave": a ScriptedPilot for every ship of team --pilot-team (default 0) that is a blueprint of ours
                int pt = o.Int("pilot-team", 0);
                foreach (var sc in scs)
                    foreach (var sp in sc.Ships)
                        if (sp.Team == pt && !ScenarioGen.IsEnemy(sp.Model) && sp.Model != "Pirate") sp.Pilot = o.Get("pilot", "");
            }
            if (o.Has("pb-custom-data"))
            {
                // --pb-custom-data "Helm=<ini>|Staff=<ini>" (\n = newline; '|' separates PBs): CustomData per PB tag (the PB's CustomName starts with [Tag]) for every ship of the scenario
                foreach (string part in o.Get("pb-custom-data", "").Split('|'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) throw new ArgumentException("--pb-custom-data needs Tag=text pairs separated by |");
                    string tag = part.Substring(0, eq).Trim(), text = part.Substring(eq + 1).Replace("\\n", "\n");
                    foreach (var sc in scs) foreach (var sp in sc.Ships) sp.PbCustomData[tag] = text;
                }
            }
            if (o.Has("face") && o.Get("face", "true") != "false") foreach (var sc in scs) sc.FaceEnemies = true;
            if (o.Has("face-away") && o.Get("face-away", "true") != "false") foreach (var sc in scs) sc.FaceAway = true;
            if (o.Has("start-distance"))
            {
                string[] p = o.Get("start-distance", "").Split(':');
                double a, b;
                if (p.Length != 2 || !double.TryParse(p[0], NumberStyles.Float, Inv, out a) || !double.TryParse(p[1], NumberStyles.Float, Inv, out b) || a <= 0 || b < a)
                    throw new ArgumentException("--start-distance needs MIN:MAX metres");
                foreach (var sc in scs) { sc.StartDistanceMin = a; sc.StartDistanceMax = b; }
            }
        }

        static List<RunResult> LoadRuns(string path)
        {
            // a retried error run appends a second line for the same scenario and seed: the last line wins
            var rs = new List<RunResult>();
            var at = new Dictionary<string, int>();
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length < 2) continue;
                try
                {
                    var r = JsonOut.ParseRun(line);
                    string k = ParallelRunner.RunKey(r.Scenario, r.SeedIndex);
                    int i;
                    if (at.TryGetValue(k, out i)) rs[i] = r; else { at[k] = rs.Count; rs.Add(r); }
                }
                catch (Exception e) { Console.Error.WriteLine("skipping unreadable line: " + e.Message); }
            }
            return rs;
        }

        // ---------------------------------------------------------------------------------------------------------
        static int CmdOne(string[] args, string simDir, SimSettings settings)
        {
            var o = Parse(args, 1, settings);
            string repo = FindRepoRoot(o.Get("repo", null));
            if (!CheckBlueprints(repo)) return 4;
            ApplyGenOptions(o, settings);
            string name = o.Get("scenario", o.Positional.Count > 0 ? o.Positional[0] : null);
            if (name == null) throw new ArgumentException("one needs --scenario NAME (see `list`)");
            Scenario sc = null;
            foreach (var s in ScenarioGen.All()) if (s.Name == name) { sc = s; break; }
            if (sc == null) sc = ScenarioGen.Custom(name, Path.Combine(Path.Combine(repo, "out"), "Blueprints"));      // M0: "kind/A-vs-B" with any blueprint folder
            if (sc == null) throw new ArgumentException("no scenario named '" + name + "' (try: list --filter ...)");
            if (o.Has("cap-seconds")) { sc.MaxTicks = (int)(o.Dbl("cap-seconds", 300) * SimSettings.TicksPerSecond); settings.MaxTicks = sc.MaxTicks; }
            ApplyScenarioOverrides(o, new List<Scenario> { sc });
            int idx = o.Int("seed-index", 0);
            ulong baseSeed = (ulong)o.Int("seed", 1);
            var opt = new RunOptions { SimDir = simDir, TraceEvery = o.Int("trace-every", 6) };
            if (o.Has("echo")) opt.Inspect = (w, host, ships) =>
            {
                foreach (var pr in host.Runners)
                    Console.Error.WriteLine("--- PB echo ship " + pr.ShipIndex + " " + pr.ScriptName + (pr.Dead ? " DEAD: " + pr.DeathReason : "") + " ---\n" + pr.EchoText);
            };
            if (o.Has("echo-every"))
            {
                long every = Math.Max(1, (long)Math.Round(o.Dbl("echo-every", 1) * SimSettings.TicksPerSecond)); int eship = o.Int("echo-ship", 0);
                opt.OnTick = (w, host, ships) =>
                {
                    if (w.Tick % every != 0) return;
                    foreach (var pr in host.Runners)
                        if (pr.ShipIndex == eship) Console.Error.WriteLine("=== t " + (w.Tick / (double)SimSettings.TicksPerSecond).ToString("0.00", Inv) + " ship " + pr.ShipIndex + " " + pr.ScriptName + " ===\n" + pr.EchoText);
                };
            }
            var inspect0 = opt.Inspect;
            opt.Inspect = (w, host, ships) =>
            {
                if (inspect0 != null) inspect0(w, host, ships);
                foreach (var sh in ships)
                    if (sh.Pilot != null)
                        Console.Error.WriteLine("pilot ship " + sh.Index + ": seated " + (sh.Pilot.SeatedTicks / (double)SimSettings.TicksPerSecond).ToString("F1", Inv) + " s, override fight ticks " + sh.Pilot.OverrideFightTicks
                            + ", skipped steps " + sh.Pilot.Skipped);
            };
            StreamWriter tw = null;
            if (o.Has("trace")) { tw = new StreamWriter(o.Get("trace", "trace.csv"), false, new UTF8Encoding(false)); opt.Trace = tw; }
            RunResult r = ScenarioRunner.RunOne(sc, idx, baseSeed, settings, repo, opt);
            if (tw != null) tw.Dispose();
            var sb = new StringBuilder();
            JsonOut.AppendRun(sb, r);
            Console.WriteLine(sb.ToString());
            Console.Error.WriteLine(Describe(r));
            if (o.Has("profile")) Profile(r);
            return r.Outcome == "error" ? 5 : 0;
        }

        static void Profile(RunResult r)
        {
            Console.Error.WriteLine("profile: wall " + r.WallMs.ToString("F0", Inv) + " ms for " + r.DurationS.ToString("F1", Inv) + " sim s = "
                + (r.DurationS > 0 ? (r.WallMs / r.DurationS).ToString("F1", Inv) : "?") + " ms per sim second");
            double scriptTotal = 0;
            foreach (var s in r.Ships) scriptTotal += s.ScriptCpuMs;
            Console.Error.WriteLine("  scripts total " + scriptTotal.ToString("F0", Inv) + " ms (" + (r.WallMs > 0 ? (100 * scriptTotal / r.WallMs).ToString("F0", Inv) : "0") + "% of wall), engine+rest " + (r.WallMs - scriptTotal).ToString("F0", Inv) + " ms");
            foreach (var s in r.Ships)
                Console.Error.WriteLine("  ship " + s.Ship + " " + s.Model + " [" + s.Script + "] runs " + s.ScriptRuns + " cpu " + s.ScriptCpuMs.ToString("F0", Inv) + " ms, max call " + s.ScriptMaxTickMs.ToString("F2", Inv) + " ms");
        }

        static string Describe(RunResult r)
        {
            var sb = new StringBuilder();
            sb.Append(r.Scenario).Append(" #").Append(r.SeedIndex).Append(": ").Append(r.Outcome);
            if (r.Outcome == "win") sb.Append(" team ").Append(r.WinnerTeam);
            sb.Append(" at ").Append(r.DurationS.ToString("F1", Inv)).Append(" s, wall ").Append(r.WallMs.ToString("F0", Inv)).Append(" ms");
            if (r.Error != null) sb.Append("\n  ERROR: ").Append(r.Error);
            foreach (var s in r.Ships)
                sb.Append("\n  ship ").Append(s.Ship).Append(' ').Append(s.Model).Append(" t").Append(s.Team).Append(" blocks ").Append(s.BlocksEnd).Append('/').Append(s.BlocksStart)
                  .Append(" capable=").Append(s.Capable).Append(" shots ").Append(s.Shots).Append(" hits ").Append(s.Hits).Append(" dmg dealt ").Append(s.DamageDealt.ToString("F0", Inv))
                  .Append(" taken ").Append(s.DamageTaken.ToString("F0", Inv)).Append(" exceptions ").Append(s.ScriptExceptions)
                  .Append(s.FirstException != null ? " (" + s.FirstException + ")" : "").Append(s.StuckOrCircling ? " STUCK:" + s.StuckKind : "");
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------------------------------
        static int CmdRun(string[] args, string simDir, SimSettings settings)
        {
            var o = Parse(args, 1, settings);
            string repo = FindRepoRoot(o.Get("repo", null));
            if (!CheckBlueprints(repo)) return 4;
            ApplyGenOptions(o, settings);
            var scenarios = ScenarioGen.Select(o.Get("filter", null), o.Get("kind", null));
            if (scenarios.Count == 0) { Console.Error.WriteLine("no scenario matches the filter"); return 1; }
            if (o.Has("cap-seconds"))
            {
                int ticks = (int)(o.Dbl("cap-seconds", 300) * SimSettings.TicksPerSecond);
                foreach (var sc in scenarios) sc.MaxTicks = ticks;
                settings.MaxTicks = ticks;
            }
            ApplyScenarioOverrides(o, scenarios);
            int seeds = o.Int("seeds", DefaultSeeds), seedStart = o.Int("seed-start", 0);
            if (!o.Has("seeds")) Console.Error.WriteLine("--seeds not given: " + DefaultSeeds + " per scenario (quick check); pass --seeds N for more");
            ulong baseSeed = (ulong)o.Int("seed", 1);
            int threads = o.Int("jobs", Environment.ProcessorCount);
            if (o.Has("endless") && o.Get("endless", "true") != "false") return CmdRunEndless(o, args, simDir, settings, repo, scenarios, seedStart, baseSeed, threads);
            var jobs = ParallelRunner.MakeJobs(scenarios, seeds, seedStart);
            string shard = o.Get("shard", null);
            if (shard != null)
            {
                string[] p = shard.Split('/');
                int si, sn;
                if (p.Length != 2 || !int.TryParse(p[0], out si) || !int.TryParse(p[1], out sn) || sn < 1 || si < 0 || si >= sn) throw new ArgumentException("--shard needs i/n with 0 <= i < n");
                jobs = ParallelRunner.Shard(jobs, si, sn);
            }
            string outDir = o.Get("out", null);
            if (outDir == null)
            {
                string batch = o.Get("batch", DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv));
                outDir = Path.Combine(Path.Combine(Path.Combine(Path.Combine(repo, "out"), "sim"), "results"), batch);
            }
            Directory.CreateDirectory(outDir);
            string runsPath = Path.Combine(outDir, "runs.jsonl");
            HashSet<string> skip = null;
            if (o.Has("resume") && File.Exists(runsPath))
            {
                skip = new HashSet<string>();
                int retry = 0;
                foreach (var r in LoadRuns(runsPath)) { if (r.Outcome == "error") retry++; else skip.Add(ParallelRunner.RunKey(r.Scenario, r.SeedIndex)); }
                Console.Error.WriteLine("resume: " + skip.Count + " runs already in " + runsPath + (retry > 0 ? ", " + retry + " error runs will be run again" : ""));
            }
            SetMeta(o, skip != null, Path.Combine(outDir, "meta.json"), args, settings, repo, simDir, scenarios.Count, jobs.Count, seeds, baseSeed, threads, shard);
            Console.Error.WriteLine(scenarios.Count + " scenarios x " + seeds + " seeds = " + jobs.Count + " runs on " + threads + " threads -> " + outDir);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ParallelRunner.Run(jobs, threads, baseSeed, settings, repo, outDir, skip, o.Has("quiet"));
            var all = LoadRuns(runsPath);
            if (!o.Has("no-summary")) JsonOut.WriteSummaryCsv(Path.Combine(outDir, "summary.csv"), all);
            int errors = 0, wins = 0, timeouts = 0, other = 0;
            foreach (var r in all) { if (r.Outcome == "error") errors++; else if (r.Outcome == "win") wins++; else if (r.Outcome == "timeout") timeouts++; else other++; }
            Console.WriteLine("done: " + all.Count + " runs in file (" + wins + " decisive, " + timeouts + " timeouts, " + other + " draws, " + errors + " errors), wall " + sw.Elapsed.TotalSeconds.ToString("F1", Inv) + " s");
            Console.WriteLine("results: " + outDir);
            return errors > 0 ? 5 : 0;
        }

        /// <summary>run --endless: fights every selected scenario round after round until Enter (see ParallelRunner.RunEndless), then
        /// writes summary.csv over everything in runs.jsonl. Always resumes: running it again continues with new seeds.</summary>
        static int CmdRunEndless(Opts o, string[] args, string simDir, SimSettings settings, string repo, List<Scenario> scenarios, int seedStart, ulong baseSeed, int threads)
        {
            if (o.Has("shard")) throw new ArgumentException("--endless cannot be combined with --shard");
            string outDir = o.Get("out", null) ?? Path.Combine(Path.Combine(Path.Combine(Path.Combine(repo, "out"), "sim"), "results"), "endless");
            Directory.CreateDirectory(outDir);
            string runsPath = Path.Combine(outDir, "runs.jsonl");
            var skip = new HashSet<string>();
            if (File.Exists(runsPath)) foreach (var r in LoadRuns(runsPath)) if (r.Outcome != "error") skip.Add(ParallelRunner.RunKey(r.Scenario, r.SeedIndex));
            SetMeta(o, File.Exists(runsPath), Path.Combine(outDir, "meta.json"), args, settings, repo, simDir, scenarios.Count, -1, -1, baseSeed, threads, null);
            Console.Error.WriteLine(scenarios.Count + " matchups, endless, " + threads + " threads -> " + outDir + (skip.Count > 0 ? " (continuing after " + skip.Count + " earlier fights)" : ""));
            Console.Error.WriteLine("Press Enter to stop: fights in progress finish and everything is saved. Press Enter twice to stop at once.");
            ParallelRunner.RunEndless(scenarios, seedStart, threads, baseSeed, settings, repo, outDir, skip);
            var all = LoadRuns(runsPath);
            JsonOut.WriteSummaryCsv(Path.Combine(outDir, "summary.csv"), all);
            Console.WriteLine("saved: " + all.Count + " fights in " + runsPath + " (summary.csv updated)");
            return 0;
        }

        /// <summary>search: endless fleet search against one enemy blueprint (FleetSearch). --enemies DIR (default "enemies"),
        /// --target NAME (default: the first blueprint there), --max-drones 10, --min-drones 1, --models "Hornet,Gnat,..." (default: every
        /// armed model plus the fleet-only blueprints that exist), --min-fights 3, --jobs, --out (default out/sim/results/search),
        /// --cap-seconds, --enemy-brain, --seed and any engine setting.</summary>
        static int CmdSearch(string[] args, string simDir, SimSettings settings)
        {
            var o = Parse(args, 1, settings);
            string repo = FindRepoRoot(o.Get("repo", null));
            if (!CheckBlueprints(repo)) return 4;
            if (!o.Has("enemies")) o.Kv["enemies"] = "enemies";
            ApplyGenOptions(o, settings);
            var enemies = ScenarioGen.Enemies();
            string target = o.Get("target", enemies[0]);
            if (!enemies.Contains(target)) throw new ArgumentException("--target '" + target + "' is not a blueprint folder in " + ScenarioGen.EnemyDir);
            string bpDir = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            var pool = new List<string>();
            if (o.Has("models"))
            {
                foreach (string m0 in o.Get("models", "").Split(','))
                {
                    string m = m0.Trim(); if (m.Length == 0) continue;
                    ScenarioGen.ParseFleet(m);          // validates the name
                    if (!pool.Contains(m)) pool.Add(m);
                }
            }
            else
            {
                pool.AddRange(ScenarioGen.ArmedModels);
                foreach (string m in ScenarioGen.FleetOnlyModels) if (File.Exists(Path.Combine(Path.Combine(bpDir, m), "bp.sbc"))) pool.Add(m);
            }
            if (pool.Count == 0) throw new ArgumentException("--models is empty");
            var fsr = new FleetSearch
            {
                Enemy = target, Pool = pool.ToArray(), Settings = settings, Repo = repo,
                MaxDrones = o.Int("max-drones", 10), MinDrones = o.Int("min-drones", 1), MinFights = Math.Max(1, o.Int("min-fights", 3)),
                Threads = Math.Max(1, o.Int("jobs", Environment.ProcessorCount)), BaseSeed = (ulong)o.Int("seed", 1),
                OutDir = o.Get("out", null) ?? Path.Combine(Path.Combine(Path.Combine(Path.Combine(repo, "out"), "sim"), "results"), "search")
            };
            if (fsr.MaxDrones < 1 || fsr.MaxDrones > 32 || fsr.MinDrones < 1 || fsr.MinDrones > fsr.MaxDrones) throw new ArgumentException("need 1 <= --min-drones <= --max-drones <= 32");
            if (o.Has("cap-seconds")) { fsr.CapTicks = (int)(o.Dbl("cap-seconds", 300) * SimSettings.TicksPerSecond); settings.MaxTicks = fsr.CapTicks; }
            Directory.CreateDirectory(fsr.OutDir);
            string runsPath = Path.Combine(fsr.OutDir, "runs.jsonl");
            var earlier = File.Exists(runsPath) ? LoadRuns(runsPath) : new List<RunResult>();
            SetMeta(o, File.Exists(runsPath), Path.Combine(fsr.OutDir, "meta.json"), args, settings, repo, simDir, 0, -1, -1, fsr.BaseSeed, fsr.Threads, null);
            fsr.Run(earlier);
            return 0;
        }

        // Run provenance. Resuming into a runs.jsonl made with other scripts, blueprints, engine settings or scenario overrides would
        // mix incomparable runs, so meta.json records them and --resume (and endless / search, which always resume) refuses on a mismatch
        // unless --allow-mismatch is given. Not part of it: seeds, seed-start, jobs, shard, filter, out and the other run-control options.
        static readonly HashSet<string> NotProvenance = new HashSet<string> { "filter", "kind", "seeds", "seed-start", "jobs", "shard", "out", "batch", "resume", "quiet", "no-summary", "profile",
            "trace", "trace-every", "echo", "echo-every", "echo-ship", "repo", "in", "endless", "scenario", "seed-index", "allow-mismatch" };

        sealed class Prov { public string Head, ScriptsHash, BlueprintsHash, Key; public List<string> Overrides = new List<string>(); }

        static string Fnv(IEnumerable<byte[]> parts)
        {
            ulong h = 14695981039346656037UL;
            foreach (var b in parts) { for (int i = 0; i < b.Length; i++) { h ^= b[i]; h *= 1099511628211UL; } h ^= 0xFF; h *= 1099511628211UL; }
            return h.ToString("x16", Inv);
        }

        static Prov MakeProv(Opts o, SimSettings s, string repo, string simDir)
        {
            var p = new Prov();
            p.Head = "unknown";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "-C \"" + repo + "\" rev-parse HEAD") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                using (var pr = System.Diagnostics.Process.Start(psi)) { string h = pr.StandardOutput.ReadToEnd().Trim(); pr.WaitForExit(); if (pr.ExitCode == 0 && h.Length > 0) p.Head = h; }
            }
            catch (Exception) { }
            var parts = new List<byte[]>();
            foreach (var kv in ScriptRegistry.ManifestEntries(simDir)) { parts.Add(Encoding.UTF8.GetBytes(kv.Key)); parts.Add(Encoding.UTF8.GetBytes(kv.Value)); }
            p.ScriptsHash = Fnv(parts);
            parts.Clear();
            string bpDir = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            if (Directory.Exists(bpDir))
            {
                string[] files = Directory.GetFiles(bpDir, "*.sbc", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string f in files) { parts.Add(Encoding.UTF8.GetBytes(f.Substring(bpDir.Length))); parts.Add(File.ReadAllBytes(f)); }
            }
            p.BlueprintsHash = Fnv(parts);
            var keys = new List<string>(o.Kv.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) if (!NotProvenance.Contains(k)) p.Overrides.Add(k + "=" + o.Kv[k]);
            p.Key = Fnv(new List<byte[]> { Encoding.UTF8.GetBytes(s.Hash()), Encoding.UTF8.GetBytes(p.ScriptsHash), Encoding.UTF8.GetBytes(p.BlueprintsHash), Encoding.UTF8.GetBytes(string.Join("\n", p.Overrides)) });
            return p;
        }

        /// <summary>Writes meta.json for a fresh output directory. When resuming it keeps the existing meta.json (the first run's record) after checking
        /// that this run has the same provenance; an old meta.json without provenance only gives a warning.</summary>
        static void SetMeta(Opts o, bool resuming, string path, string[] args, SimSettings s, string repo, string simDir, int scenarios, int jobs, int seeds, ulong baseSeed, int threads, string shard)
        {
            var prov = MakeProv(o, s, repo, simDir);
            if (resuming && File.Exists(path))
            {
                string old = null;
                try { old = MiniJson.Str(MiniJson.Parse(File.ReadAllText(path)), "provenance_key", null); } catch (Exception) { }
                if (old == null) { Console.Error.WriteLine("resume: meta.json has no provenance record (older run), cannot check that scripts, blueprints and settings are unchanged"); return; }
                if (old != prov.Key)
                {
                    string msg = "resume: this run differs from the one that started " + Path.GetDirectoryName(path) + " (scripts, blueprints, engine settings or scenario options changed; now git " + prov.Head + ", scripts " + prov.ScriptsHash + ", blueprints " + prov.BlueprintsHash + ")";
                    if (!o.Has("allow-mismatch")) throw new ArgumentException(msg + ". Use a new --out, or --allow-mismatch to mix them anyway");
                    Console.Error.WriteLine("WARNING: " + msg + "; continuing because of --allow-mismatch");
                }
                return;
            }
            WriteMeta(path, args, s, repo, simDir, scenarios, jobs, seeds, baseSeed, threads, shard, prov);
        }

        static void WriteMeta(string path, string[] args, SimSettings s, string repo, string simDir, int scenarios, int jobs, int seeds, ulong baseSeed, int threads, string shard, Prov prov)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"command_line\": "); Q(sb, string.Join(" ", args)); sb.Append(",\n");
            sb.Append("  \"started_utc\": "); Q(sb, DateTime.UtcNow.ToString("o", Inv)); sb.Append(",\n");
            sb.Append("  \"scenarios\": ").Append(scenarios).Append(", \"jobs\": ").Append(jobs).Append(", \"seeds\": ").Append(seeds)
              .Append(", \"base_seed\": ").Append(baseSeed.ToString(Inv)).Append(", \"threads\": ").Append(threads).Append(",\n");
            sb.Append("  \"shard\": "); Q(sb, shard); sb.Append(",\n");
            sb.Append("  \"settings_hash\": "); Q(sb, s.Hash()); sb.Append(",\n");
            sb.Append("  \"git_head\": "); Q(sb, prov.Head); sb.Append(",\n");
            sb.Append("  \"scripts_hash\": "); Q(sb, prov.ScriptsHash); sb.Append(", \"blueprints_hash\": "); Q(sb, prov.BlueprintsHash); sb.Append(",\n");
            sb.Append("  \"overrides\": ["); for (int i = 0; i < prov.Overrides.Count; i++) { if (i > 0) sb.Append(", "); Q(sb, prov.Overrides[i]); } sb.Append("],\n");
            sb.Append("  \"provenance_key\": "); Q(sb, prov.Key); sb.Append(",\n");
            sb.Append("  \"settings\": {");
            bool first = true;
            foreach (string line in s.Describe().Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("\n    "); Q(sb, line.Substring(0, eq)); sb.Append(": "); Q(sb, line.Substring(eq + 1));
            }
            sb.Append("\n  },\n");
            sb.Append("  \"scripts\": {");
            first = true;
            foreach (var kv in ScriptRegistry.ManifestEntries(simDir))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("\n    "); Q(sb, kv.Value); sb.Append(": "); Q(sb, kv.Key);
            }
            sb.Append("\n  },\n");
            sb.Append("  \"assumptions\": [");
            try
            {
                var defs = SeSim.Blueprint.DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
                first = true;
                lock (defs.Assumptions)
                    foreach (string a in defs.Assumptions)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("\n    "); Q(sb, a);
                    }
            }
            catch (Exception) { }
            sb.Append("\n  ],\n");
            sb.Append("  \"cpu_ms_note\": \"cpu_ms equals wall_ms of the run (one thread per run; per-thread CPU time is not available on Mono)\"\n");
            sb.Append("}\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static void Q(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\"); else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r"); else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
            }
            sb.Append('"');
        }

        // ---------------------------------------------------------------------------------------------------------
        static int CompileScripts(string simDir, string repo)
        {
            string dir = Path.Combine(simDir, "scripts");
            Directory.CreateDirectory(dir);
            var manifest = new StringBuilder();
            int fail = 0;
            string[] files = Directory.GetFiles(Path.Combine(repo, "scripts"), "*.cs");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string text = File.ReadAllText(f);
                string err = ScriptCompiler.Compile(text, Path.Combine(dir, name + ".dll"), simDir);
                if (err != null) { Console.WriteLine("  FAIL " + name + "\n" + err); fail++; continue; }
                manifest.Append(ScriptCompiler.ScriptKey(text)).Append('\t').Append(name).Append(".dll\n");
                Console.WriteLine("  ok " + name);
            }
            File.WriteAllText(Path.Combine(dir, "manifest.tsv"), manifest.ToString(), new UTF8Encoding(false));
            return fail == 0 ? 0 : 1;
        }
    }
}
