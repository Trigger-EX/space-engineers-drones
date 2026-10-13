// SLOT D. Runs (scenario x seed) jobs on N worker threads in one process (default N = Environment.ProcessorCount).
// Each job builds its own World (no shared mutable state: script types/assemblies, DefDb and parsed blueprints are shared
// read-only). Results are written in job order (deterministic file content regardless of thread timing): workers fill a
// RunResult[] slot; a writer flushes contiguous finished slots to runs.jsonl. --shard i/n splits the job list for multi-process.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using SeSim.Core;

namespace SeSim.Run
{
    public sealed class Job { public Scenario Scenario; public int SeedIndex; }

    public static class ParallelRunner
    {
        /// <summary>Expands scenarios x seeds into jobs (scenario-major, then seed), keeping job order stable.</summary>
        public static List<Job> MakeJobs(List<Scenario> scenarios, int seeds, int seedStart)
        {
            var jobs = new List<Job>(scenarios.Count * seeds);
            for (int i = 0; i < scenarios.Count; i++)
                for (int k = 0; k < seeds; k++) jobs.Add(new Job { Scenario = scenarios[i], SeedIndex = seedStart + k });
            return jobs;
        }

        /// <summary>Every k-th job starting at i (multi-process split).</summary>
        public static List<Job> Shard(List<Job> jobs, int i, int n)
        {
            var l = new List<Job>();
            for (int k = i; k < jobs.Count; k += n) l.Add(jobs[k]);
            return l;
        }

        public static string RunKey(string scenario, int seedIndex) { return scenario + "#" + seedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>Runs all jobs; appends to outDir/runs.jsonl as results complete in order. Returns the results in job order.
        /// 'skip' (optional) = RunKeys already present in runs.jsonl (resume): these jobs are not run and not returned.</summary>
        public static List<RunResult> Run(List<Job> jobs, int threads, ulong baseSeed, SimSettings settings, string repoRoot, string outDir)
        {
            return Run(jobs, threads, baseSeed, settings, repoRoot, outDir, null, false);
        }

        public static List<RunResult> Run(List<Job> jobs, int threads, ulong baseSeed, SimSettings settings, string repoRoot, string outDir,
                                          HashSet<string> skip, bool quiet)
        {
            if (threads < 1) threads = 1;
            Directory.CreateDirectory(outDir);
            var todo = new List<Job>(jobs.Count);
            for (int i = 0; i < jobs.Count; i++)
                if (skip == null || !skip.Contains(RunKey(jobs[i].Scenario.Name, jobs[i].SeedIndex))) todo.Add(jobs[i]);
            int total = todo.Count;
            var results = new RunResult[total];
            var done = new bool[total];
            string path = Path.Combine(outDir, "runs.jsonl");
            var sw = Stopwatch.StartNew();
            var gate = new object();
            int next = -1, flushed = 0, finished = 0;
            long lastReport = 0;
            int errors = 0;
            var line = new StringBuilder(8192);
            string simDir = ScenarioRunner.DefaultSimDir();

            if (skip != null && skip.Count > 0 && File.Exists(path))
            {   // a crashed earlier run may have left a partial last line: end it so appended records start on their own line
                byte[] all = File.ReadAllBytes(path);
                if (all.Length > 0 && all[all.Length - 1] != (byte)'\n') File.AppendAllText(path, "\n");
            }
            using (var fs = new FileStream(path, skip != null && skip.Count > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var wr = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                ThreadStart work = () =>
                {
                    var opt = new RunOptions { SimDir = simDir };
                    while (true)
                    {
                        int idx = Interlocked.Increment(ref next);
                        if (idx >= total) return;
                        var job = todo[idx];
                        RunResult r;
                        try { r = ScenarioRunner.RunOne(job.Scenario, job.SeedIndex, baseSeed, settings, repoRoot, opt); }
                        catch (Exception e)
                        {
                            r = new RunResult { Scenario = job.Scenario.Name, Kind = job.Scenario.Kind, SeedIndex = job.SeedIndex, Outcome = "error", Error = e.ToString() };
                        }
                        lock (gate)
                        {
                            results[idx] = r; done[idx] = true; finished++;
                            if (r.Outcome == "error") errors++;
                            while (flushed < total && done[flushed])
                            {
                                line.Clear();
                                JsonOut.AppendRun(line, results[flushed]);
                                wr.Write(line.ToString()); wr.Write('\n');
                                flushed++;
                            }
                            wr.Flush();
                            long now = sw.ElapsedMilliseconds;
                            if (!quiet && (now - lastReport >= 10000 || finished == total))
                            {
                                lastReport = now;
                                double rate = finished / Math.Max(0.001, now / 1000.0);
                                double eta = rate > 0 ? (total - finished) / rate : 0;
                                Console.Error.WriteLine("[" + finished + "/" + total + "] " + rate.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                                    + " runs/s, errors " + errors + ", elapsed " + Fmt(now / 1000.0) + ", eta " + Fmt(eta));
                            }
                        }
                    }
                };
                var ts = new Thread[threads];
                for (int i = 0; i < threads; i++)
                {
                    ts[i] = new Thread(work, 64 * 1024 * 1024) { IsBackground = false, Name = "sim-" + i };
                    ts[i].Start();
                }
                for (int i = 0; i < threads; i++) ts[i].Join();
            }
            return new List<RunResult>(results);
        }

        /// <summary>--endless: runs rounds forever (round r = seed index seedStart+r for every scenario, so all matchups grow evenly)
        /// until the user presses Enter (or Ctrl+C) once: no new fights start, the ones in progress finish and are saved. A second
        /// Enter / Ctrl+C drops the fights in progress and stops at once. Results are appended to runs.jsonl as each fight ends
        /// (completion order), so nothing finished is ever lost; 'skip' (resume) = RunKeys already in the file. A live status line
        /// shows the count (one line every 10 s when stderr is redirected). Returns the number of fights run in this session.</summary>
        public static int RunEndless(List<Scenario> scenarios, int seedStart, int threads, ulong baseSeed, SimSettings settings, string repoRoot,
                                     string outDir, HashSet<string> skip)
        {
            if (threads < 1) threads = 1;
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "runs.jsonl");
            int ns = scenarios.Count;
            var perScenario = new int[ns];
            int already = 0;
            if (skip != null)
                for (int i = 0; i < ns; i++)
                    for (int k = seedStart; skip.Contains(RunKey(scenarios[i].Name, k)); k++) { perScenario[i]++; already++; }
            if (skip != null && skip.Count > 0 && File.Exists(path))
            {
                byte[] all = File.ReadAllBytes(path);
                if (all.Length > 0 && all[all.Length - 1] != (byte)'\n') File.AppendAllText(path, "\n");
            }
            var gate = new object();
            long seq = -1;                // job sequence: round = seq / ns, scenario = seq % ns
            int stopLevel = 0;            // 1 = finish fights in progress, 2 = stop now
            bool abandoned = false;
            int finished = 0, errors = 0, wins = 0, timeouts = 0, running = 0;
            var line = new StringBuilder(8192);
            string simDir = ScenarioRunner.DefaultSimDir();
            var sw = Stopwatch.StartNew();
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            ConsoleCancelEventHandler onCancel = (o, e) =>
            {
                if (Volatile.Read(ref stopLevel) >= 2) return;            // third Ctrl+C: let it kill the process
                e.Cancel = true; Interlocked.Increment(ref stopLevel);
            };
            Console.CancelKeyPress += onCancel;
            var reader = new Thread(() =>
            {
                try
                {
                    while (Volatile.Read(ref stopLevel) < 2)
                    {
                        string l = Console.In.ReadLine();
                        if (l == null) return;                             // no keyboard (stdin redirected or closed)
                        Interlocked.Increment(ref stopLevel);
                    }
                }
                catch (Exception) { }
            }) { IsBackground = true, Name = "stdin" };
            reader.Start();

            bool live = !Console.IsErrorRedirected;
            int lastLen = 0;
            long lastReport = -100000;
            Action<bool> status = (force) =>
            {
                long now = sw.ElapsedMilliseconds;
                if (!force && (live ? now - lastReport < 1000 : now - lastReport < 10000)) return;
                lastReport = now;
                int minPer = int.MaxValue; for (int i = 0; i < ns; i++) if (perScenario[i] < minPer) minPer = perScenario[i];
                double rate = finished / Math.Max(0.001, now / 1000.0);
                string msg = "fights " + finished + (already > 0 ? " (+" + already + " earlier)" : "") + "  " + rate.ToString("F2", inv) + "/s  " + Fmt(now / 1000.0)
                    + "  every matchup >= " + minPer + (errors > 0 ? "  errors " + errors : "")
                    + (stopLevel == 0 ? "  [Enter = stop & save]" : stopLevel == 1 ? "  finishing " + running + " in progress [Enter = drop them]" : "  stopping");
                if (live)
                {
                    string pad = msg.Length < lastLen ? new string(' ', lastLen - msg.Length) : "";
                    Console.Error.Write("\r" + msg + pad); lastLen = msg.Length;
                }
                else Console.Error.WriteLine(msg);
            };

            using (var fs = new FileStream(path, already > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var wr = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                ThreadStart work = () =>
                {
                    var opt = new RunOptions { SimDir = simDir };
                    while (true)
                    {
                        Job job; int si;
                        lock (gate)
                        {
                            if (stopLevel > 0) return;
                            while (true)
                            {
                                seq++;
                                si = (int)(seq % ns);
                                int seedIndex = seedStart + (int)(seq / ns);
                                if (skip != null && skip.Contains(RunKey(scenarios[si].Name, seedIndex))) continue;
                                job = new Job { Scenario = scenarios[si], SeedIndex = seedIndex };
                                break;
                            }
                            running++;
                        }
                        RunResult r;
                        try { r = ScenarioRunner.RunOne(job.Scenario, job.SeedIndex, baseSeed, settings, repoRoot, opt); }
                        catch (Exception e)
                        {
                            r = new RunResult { Scenario = job.Scenario.Name, Kind = job.Scenario.Kind, SeedIndex = job.SeedIndex, Outcome = "error", Error = e.ToString() };
                        }
                        lock (gate)
                        {
                            running--;
                            if (abandoned) return;
                            line.Clear();
                            JsonOut.AppendRun(line, r);
                            wr.Write(line.ToString()); wr.Write('\n'); wr.Flush();
                            finished++; perScenario[si]++;
                            if (r.Outcome == "error") errors++; else if (r.Outcome == "win") wins++; else if (r.Outcome == "timeout") timeouts++;
                        }
                    }
                };
                var ts = new Thread[threads];
                for (int i = 0; i < threads; i++)
                {
                    ts[i] = new Thread(work, 64 * 1024 * 1024) { IsBackground = true, Name = "sim-" + i };
                    ts[i].Start();
                }
                while (true)
                {
                    bool alive = false;
                    for (int i = 0; i < threads; i++) if (ts[i].IsAlive) { alive = true; break; }
                    lock (gate)
                    {
                        if (!alive) break;
                        if (stopLevel >= 2) { abandoned = true; break; }
                        status(false);
                    }
                    Thread.Sleep(200);
                }
                lock (gate) { wr.Flush(); status(true); }
            }
            Console.CancelKeyPress -= onCancel;
            if (live) Console.Error.WriteLine();
            Console.Error.WriteLine("stopped: " + finished + " fights saved this session (" + wins + " decisive, " + timeouts + " timeouts, " + errors + " errors)"
                + (abandoned ? ", fights in progress dropped" : ""));
            return finished;
        }

        static string Fmt(double s)
        {
            int t = (int)s;
            return (t / 3600) + "h" + ((t / 60) % 60).ToString("00") + "m" + (t % 60).ToString("00") + "s";
        }
    }
}
