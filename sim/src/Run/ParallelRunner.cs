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

        static string Fmt(double s)
        {
            int t = (int)s;
            return (t / 3600) + "h" + ((t / 60) % 60).ToString("00") + "m" + (t % 60).ToString("00") + "s";
        }
    }
}
