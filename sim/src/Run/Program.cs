// SLOT D. CLI entry point: mono out/sim/SimHost.exe <command> [options]
//   selftest [physics|api|combat|runner|scenario|all]   engine self-checks by group (default: the first four);
//                                 `selftest scenario` (or --selftest scenario) also runs 3 short duels end to end
//   list [--filter RX] [--kind K] print scenario names with job counts
//   run   --filter REGEX --seeds 100 [--jobs 4] [--seed 1] [--shard i/n] [--out DIR] [--resume] [--<setting> value ...]
//   one   --scenario NAME --seed-index K [--trace FILE] [--trace-every N] [--profile] [--echo]   single run, optional trace CSV
//   scenario overrides for run/one: --commands "T:cmd;..." (script drones' start commands), --start-distance MIN:MAX,
//         --face (ships start pointing at the enemy), --custom-data "INI with \n" (PB CustomData of the script drones)
//   summarize --in runs.jsonl [--out summary.csv]    rebuild summary.csv from a runs.jsonl
//   compile-scripts               (re)build out/sim/scripts/*.dll + manifest via ScriptCompiler (same result as build.sh)
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

        static readonly HashSet<string> Flags = new HashSet<string> { "profile", "resume", "quiet", "no-summary", "echo", "face" };
        static readonly HashSet<string> Known = new HashSet<string> { "filter", "kind", "seeds", "seed", "jobs", "shard", "out", "batch", "scenario", "seed-index", "seed-start", "trace", "trace-every", "profile", "resume", "quiet", "no-summary", "repo", "cap-seconds", "unfair", "in", "echo", "commands", "start-distance", "face", "custom-data" };

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
                    var groups = new HashSet<string>();
                    for (int i = 1; i < args.Length; i++) groups.Add(args[i].TrimStart('-').ToLowerInvariant());
                    foreach (string g in groups)
                        if (g != "physics" && g != "api" && g != "combat" && g != "runner" && g != "audit" && g != "scenario" && g != "all")
                            throw new ArgumentException("unknown selftest group '" + g + "' (physics, api, combat, runner, audit, scenario, all)");
                    bool all = groups.Count == 0 || groups.Contains("all") || (groups.Count == 1 && groups.Contains("scenario"));
                    int f = 0;
                    if (all || groups.Contains("physics")) f += SeSim.Core.SelfTestA.Run();
                    if (all || groups.Contains("api")) f += SeSim.Api.SelfTestB.Run();
                    if (all || groups.Contains("combat")) f += SeSim.Combat.SelfTestC.Run();
                    if (all || groups.Contains("audit")) f += SeSim.Core.SelfTestAudit.Run();
                    if (all || groups.Contains("runner")) f += SelfTestD.Run(simDir);
                    if (groups.Contains("scenario") || groups.Contains("all")) f += SelfTestD.RunScenarioTests(simDir, FindRepoRoot(null));
                    Console.WriteLine(f == 0 ? "SELFTEST OK" : "SELFTEST FAILURES: " + f);
                    return f == 0 ? 0 : 1;
                }
                case "list":
                {
                    var o = Parse(args, 1, settings);
                    if (o.Has("unfair")) ScenarioGen.UnfairMode = o.Get("unfair", "curated");
                    var l = ScenarioGen.Select(o.Get("filter", null), o.Get("kind", null));
                    int seeds = o.Int("seeds", 100);
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
                default:
                    throw new ArgumentException("unknown command '" + args[0] + "'");
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
            if (o.Has("face") && o.Get("face", "true") != "false") foreach (var sc in scs) sc.FaceEnemies = true;
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
            var rs = new List<RunResult>();
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length < 2) continue;
                try { rs.Add(JsonOut.ParseRun(line)); } catch (Exception e) { Console.Error.WriteLine("skipping unreadable line: " + e.Message); }
            }
            return rs;
        }

        // ---------------------------------------------------------------------------------------------------------
        static int CmdOne(string[] args, string simDir, SimSettings settings)
        {
            var o = Parse(args, 1, settings);
            string repo = FindRepoRoot(o.Get("repo", null));
            if (!CheckBlueprints(repo)) return 4;
            if (o.Has("unfair")) ScenarioGen.UnfairMode = o.Get("unfair", "curated");
            string name = o.Get("scenario", o.Positional.Count > 0 ? o.Positional[0] : null);
            if (name == null) throw new ArgumentException("one needs --scenario NAME (see `list`)");
            Scenario sc = null;
            foreach (var s in ScenarioGen.All()) if (s.Name == name) { sc = s; break; }
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
            if (o.Has("unfair")) ScenarioGen.UnfairMode = o.Get("unfair", "curated");
            var scenarios = ScenarioGen.Select(o.Get("filter", null), o.Get("kind", null));
            if (scenarios.Count == 0) { Console.Error.WriteLine("no scenario matches the filter"); return 1; }
            if (o.Has("cap-seconds"))
            {
                int ticks = (int)(o.Dbl("cap-seconds", 300) * SimSettings.TicksPerSecond);
                foreach (var sc in scenarios) sc.MaxTicks = ticks;
                settings.MaxTicks = ticks;
            }
            ApplyScenarioOverrides(o, scenarios);
            int seeds = o.Int("seeds", 100), seedStart = o.Int("seed-start", 0);
            ulong baseSeed = (ulong)o.Int("seed", 1);
            int threads = o.Int("jobs", Environment.ProcessorCount);
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
                foreach (var r in LoadRuns(runsPath)) skip.Add(ParallelRunner.RunKey(r.Scenario, r.SeedIndex));
                Console.Error.WriteLine("resume: " + skip.Count + " runs already in " + runsPath);
            }
            WriteMeta(Path.Combine(outDir, "meta.json"), args, settings, repo, simDir, scenarios.Count, jobs.Count, seeds, baseSeed, threads, shard);
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

        static void WriteMeta(string path, string[] args, SimSettings s, string repo, string simDir, int scenarios, int jobs, int seeds, ulong baseSeed, int threads, string shard)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"command_line\": "); Q(sb, string.Join(" ", args)); sb.Append(",\n");
            sb.Append("  \"started_utc\": "); Q(sb, DateTime.UtcNow.ToString("o", Inv)); sb.Append(",\n");
            sb.Append("  \"scenarios\": ").Append(scenarios).Append(", \"jobs\": ").Append(jobs).Append(", \"seeds\": ").Append(seeds)
              .Append(", \"base_seed\": ").Append(baseSeed.ToString(Inv)).Append(", \"threads\": ").Append(threads).Append(",\n");
            sb.Append("  \"shard\": "); Q(sb, shard); sb.Append(",\n");
            sb.Append("  \"settings_hash\": "); Q(sb, s.Hash()); sb.Append(",\n");
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
