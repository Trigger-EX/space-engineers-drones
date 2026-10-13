// Fleet search (SimHost search): which drone fleets beat an enemy ship? Any mix of models, MinDrones..MaxDrones drones.
// There are far too many fleets to fight them all (12 models, up to 10 drones: about 650,000), so the search works like breeding:
//   1. every pure fleet (MaxDrones of one model) and InitialRandom random mixes fight MinFights times each;
//   2. then each new fight goes either to a NEW fleet (35 %): a mutation of one of the 8 best fleets (swap / add / remove a drone,
//      move 2-3 drones to another model), a cross of two of them, or a fresh random mix; or to an EXISTING fleet (65 %), picked by
//      UCB (mean score + a bonus that shrinks with its number of fights), so promising fleets collect enough fights to be trusted.
// Score per fight (0..100): drones win (enemy no longer combat capable) = 80 + 20 x fraction of drones still capable; otherwise
// 25 x armed turrets destroyed + 25 x power blocks destroyed + 10 x thrusters destroyed (fractions, so at most 60).
// Ranking = lower confidence bound of the mean score (mean - sd/sqrt(n)), so a lucky fleet with 3 fights does not top the board.
// Runs until Enter (ConsoleStop). Every fight goes to runs.jsonl as it ends (scenario "enemy/<Enemy>-vs-<fleet>"); leaderboard.csv is
// rewritten every 30 s and at the end. Re-running with the same --out continues: earlier fights are read back into the statistics.
// Fights are deterministic per (fleet, seed index); the ORDER in which fleets are tried depends on thread timing.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using SeSim.Core;

namespace SeSim.Run
{
    public sealed class FleetSearch
    {
        public string Enemy;
        public string[] Pool;
        public int MaxDrones = 10, MinDrones = 1, MinFights = 3, InitialRandom = 40, Threads = 4;
        public int CapTicks;                       // 0 = scenario default
        public ulong BaseSeed = 1;
        public SimSettings Settings;
        public string Repo, OutDir;

        sealed class Cand
        {
            public string[] Models; public string Name, Origin; public Scenario Sc;
            public int N, Pending, NextSeed, Wins, Born;
            public double Sum, SumSq, Armed, Power, Thr, Lost, Dur;
            public double Mean { get { return N > 0 ? Sum / N : 0; } }
            public double Sd { get { if (N < 2) return 30; double v = (SumSq - Sum * Sum / N) / (N - 1); return Math.Sqrt(Math.Max(v, 0)); } }
            public double Lcb { get { return N > 0 ? Mean - Math.Max(Sd, 5) / Math.Sqrt(N) : -1e9; } }
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly Dictionary<string, Cand> _byName = new Dictionary<string, Cand>();
        readonly List<Cand> _cands = new List<Cand>();
        readonly object _gate = new object();
        DetRng _rng;
        int _born, _total, _session, _errors, _wins;
        string _leader;

        // ------------------------------------------------------------------------------------------------------------
        // scoring

        /// <summary>Fight score 0..100 (see header) plus the destroyed fractions; false for runs that cannot be scored (errors).</summary>
        public static bool Score(RunResult r, out double score, out double armed, out double power, out double thr, out double lost, out bool win)
        {
            score = armed = power = thr = lost = 0; win = false;
            if (r.Outcome == "error" || r.Ships.Count < 2) return false;
            var e = r.Ships[0];
            armed = Frac(e.ArmedStart, e.ArmedEnd); power = Frac(e.PowerBlocksStart, e.PowerBlocksEnd); thr = Frac(e.ThrustersStart, e.ThrustersEnd);
            int drones = 0, capable = 0;
            for (int i = 1; i < r.Ships.Count; i++) if (r.Ships[i].Team != e.Team) { drones++; if (r.Ships[i].Capable) capable++; }
            lost = drones - capable;
            win = r.Outcome == "win" && r.WinnerTeam >= 0 && r.WinnerTeam != e.Team;
            score = win ? 80 + 20.0 * capable / Math.Max(1, drones) : 25 * armed + 25 * power + 10 * thr;
            return true;
        }

        static double Frac(int start, int end) { return start > 0 ? Math.Max(0, Math.Min(1, (start - end) / (double)start)) : 0; }

        // ------------------------------------------------------------------------------------------------------------
        // candidates

        Cand Get(string[] models, string origin)
        {
            var m = (string[])models.Clone();
            Array.Sort(m, StringComparer.Ordinal);
            var sc = ScenarioGen.Build("enemy", new[] { Enemy }, m);
            if (CapTicks > 0) sc.MaxTicks = CapTicks;
            string name = sc.Name.Substring(sc.Name.IndexOf("-vs-", StringComparison.Ordinal) + 4);
            Cand c;
            if (_byName.TryGetValue(name, out c)) return c;
            c = new Cand { Models = m, Name = name, Origin = origin, Sc = sc, Born = _born++ };
            _byName[name] = c; _cands.Add(c);
            return c;
        }

        bool IsNew(string[] models)
        {
            var m = (string[])models.Clone(); Array.Sort(m, StringComparer.Ordinal);
            var sc = ScenarioGen.Build("enemy", new[] { Enemy }, m);
            return !_byName.ContainsKey(sc.Name.Substring(sc.Name.IndexOf("-vs-", StringComparison.Ordinal) + 4));
        }

        int R(int n) { return n <= 1 ? 0 : (int)(_rng.NextDouble() * n) % n; }

        string[] RandomFleet()
        {
            int size = _rng.NextDouble() < 0.5 ? MaxDrones : MinDrones + R(MaxDrones - MinDrones + 1);
            int kinds = 1 + R(Math.Min(4, Pool.Length));
            var picks = new List<string>();
            while (picks.Count < kinds) { string p = Pool[R(Pool.Length)]; if (!picks.Contains(p)) picks.Add(p); }
            var l = new List<string>();
            for (int i = 0; i < size; i++) l.Add(i < picks.Count ? picks[i] : picks[R(picks.Count)]);
            return l.ToArray();
        }

        string[] Mutate(string[] f)
        {
            var l = new List<string>(f);
            double u = _rng.NextDouble();
            if (u < 0.4) l[R(l.Count)] = Pool[R(Pool.Length)];           // swap one
            else if (u < 0.6 && l.Count < MaxDrones) l.Add(_rng.NextDouble() < 0.5 ? l[R(l.Count)] : Pool[R(Pool.Length)]); // add one
            else if (u < 0.8 && l.Count > MinDrones) l.RemoveAt(R(l.Count));                                               // remove one
            else
            {   // move 2-3 drones of one model to another model
                string from = l[R(l.Count)], to = Pool[R(Pool.Length)];
                int k = 2 + R(2);
                for (int i = 0; i < l.Count && k > 0; i++) if (l[i] == from) { l[i] = to; k--; }
            }
            return l.ToArray();
        }

        string[] Cross(string[] a, string[] b)
        {
            var all = new List<string>(a); all.AddRange(b);
            int size = Math.Max(MinDrones, Math.Min(MaxDrones, _rng.NextDouble() < 0.5 ? a.Length : b.Length));
            var l = new List<string>();
            while (l.Count < size && all.Count > 0) { int i = R(all.Count); l.Add(all[i]); all.RemoveAt(i); }
            return l.ToArray();
        }

        List<Cand> Ranked(int minN)
        {
            var l = new List<Cand>();
            foreach (var c in _cands) if (c.N >= minN) l.Add(c);
            l.Sort((x, y) => { int k = y.Lcb.CompareTo(x.Lcb); return k != 0 ? k : string.CompareOrdinal(x.Name, y.Name); });
            return l;
        }

        Cand MakeNew()
        {
            var top = Ranked(MinFights);
            if (top.Count > 8) top.RemoveRange(8, top.Count - 8);
            for (int tries = 0; tries < 40; tries++)
            {
                double u = _rng.NextDouble();
                string[] f; string origin;
                if (top.Count == 0 || u < 0.15) { f = RandomFleet(); origin = "random"; }
                else if (u < 0.85 || top.Count < 2)
                {
                    var p = top[Math.Min(top.Count - 1, R(top.Count) * R(top.Count) / Math.Max(1, top.Count - 1))];   // favours the best
                    f = Mutate(p.Models); origin = "mutant of " + p.Name;
                }
                else
                {
                    var a = top[R(top.Count)]; var b = top[R(top.Count)];
                    if (a == b) continue;
                    f = Cross(a.Models, b.Models); origin = "cross of " + a.Name + " / " + b.Name;
                }
                if (f.Length < MinDrones || f.Length > MaxDrones || !IsNew(f)) continue;
                return Get(f, origin);
            }
            return null;
        }

        /// <summary>The fleet the next fight goes to (under _gate).</summary>
        Cand Next()
        {
            Cand best = null;
            foreach (var c in _cands) if (c.N + c.Pending < MinFights && (best == null || c.Born < best.Born)) best = c;
            if (best != null) return best;
            if (_rng.NextDouble() < 0.35) { var n = MakeNew(); if (n != null) return n; }
            double lnT = Math.Log(_total + 2), bestU = double.MinValue;
            foreach (var c in _cands)
            {
                if (c.N == 0) continue;
                double u = c.Mean + 25 * Math.Sqrt(lnT / (c.N + c.Pending));
                if (u > bestU) { bestU = u; best = c; }
            }
            return best ?? MakeNew() ?? _cands[R(_cands.Count)];
        }

        void Add(Cand c, RunResult r)
        {
            double s, a, p, t, lost; bool win;
            if (!Score(r, out s, out a, out p, out t, out lost, out win)) { _errors++; return; }
            c.N++; c.Sum += s; c.SumSq += s * s; c.Armed += a; c.Power += p; c.Thr += t; c.Lost += lost; c.Dur += r.DurationS;
            if (win) { c.Wins++; _wins++; }
            if (r.SeedIndex >= c.NextSeed) c.NextSeed = r.SeedIndex + 1;
            _total++;
        }

        // ------------------------------------------------------------------------------------------------------------
        // run

        public void Run(List<RunResult> earlier)
        {
            _rng = new DetRng(DetRng.Mix(BaseSeed, 0xF1EE7));
            Directory.CreateDirectory(OutDir);
            string path = Path.Combine(OutDir, "runs.jsonl");
            string prefix = "enemy/" + Enemy + "-vs-";
            int resumed = 0;
            if (earlier != null)
                foreach (var r in earlier)
                {
                    if (r.Scenario == null || !r.Scenario.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    string[] m;
                    try { m = ScenarioGen.ParseFleet(r.Scenario.Substring(prefix.Length)); } catch (ArgumentException) { continue; }
                    Add(Get(m, "earlier"), r); resumed++;
                }
            foreach (string p in Pool) { var f = new string[MaxDrones]; for (int i = 0; i < f.Length; i++) f[i] = p; Get(f, "pure"); }
            for (int i = 0, guard = 0; i < InitialRandom && guard < InitialRandom * 20; guard++) { var f = RandomFleet(); if (IsNew(f)) { Get(f, "random"); i++; } }
            if (File.Exists(path))
            {
                byte[] all = File.ReadAllBytes(path);
                if (all.Length > 0 && all[all.Length - 1] != (byte)'\n') File.AppendAllText(path, "\n");
            }
            Console.Error.WriteLine("fleet search vs " + Enemy + ": " + Pool.Length + " models (" + string.Join(", ", Pool) + "), " + MinDrones + "-" + MaxDrones
                + " drones, " + Threads + " threads -> " + OutDir + (resumed > 0 ? " (continuing after " + resumed + " earlier fights)" : ""));
            Console.Error.WriteLine("Press Enter to stop: fights in progress finish and everything is saved. Press Enter twice to stop at once.");

            var stop = new ConsoleStop();
            var ui = new LiveLine();
            bool abandoned = false;
            int running = 0;
            string simDir = ScenarioRunner.DefaultSimDir();
            var line = new StringBuilder(8192);
            double lastBoard = 0;
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var wr = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                ThreadStart work = () =>
                {
                    var opt = new RunOptions { SimDir = simDir };
                    while (true)
                    {
                        Cand c; int seed;
                        lock (_gate)
                        {
                            if (stop.Level > 0) return;
                            c = Next(); seed = c.NextSeed++; c.Pending++; running++;
                        }
                        RunResult r;
                        try { r = ScenarioRunner.RunOne(c.Sc, seed, BaseSeed, Settings, Repo, opt); }
                        catch (Exception e) { r = new RunResult { Scenario = c.Sc.Name, Kind = c.Sc.Kind, SeedIndex = seed, Outcome = "error", Error = e.ToString() }; }
                        lock (_gate)
                        {
                            c.Pending--; running--;
                            if (abandoned) return;
                            line.Clear(); JsonOut.AppendRun(line, r);
                            wr.Write(line.ToString()); wr.Write('\n'); wr.Flush();
                            _session++;
                            Add(c, r);
                            var top = Ranked(Math.Max(5, MinFights));
                            if (top.Count > 0 && top[0].Name != _leader)
                            {
                                _leader = top[0].Name;
                                ui.Event("leader: " + Describe(top[0]));
                            }
                        }
                    }
                };
                var ts = new Thread[Threads];
                for (int i = 0; i < Threads; i++) { ts[i] = new Thread(work, 64 * 1024 * 1024) { IsBackground = true, Name = "search-" + i }; ts[i].Start(); }
                while (true)
                {
                    bool alive = false;
                    for (int i = 0; i < Threads; i++) if (ts[i].IsAlive) { alive = true; break; }
                    lock (_gate)
                    {
                        if (!alive) break;
                        if (stop.Level >= 2) { abandoned = true; break; }
                        if (ui.Due())
                        {
                            int lv = stop.Level;
                            ui.Show("fights " + _session + (resumed > 0 ? " (+" + resumed + ")" : "") + "  " + (_session / Math.Max(0.001, ui.ElapsedS)).ToString("F2", Inv) + "/s  "
                                + LiveLine.Fmt(ui.ElapsedS) + "  fleets " + _cands.Count + "  drone wins " + _wins + (_errors > 0 ? "  errors " + _errors : "")
                                + (lv == 0 ? "  [Enter = stop & save]" : lv == 1 ? "  finishing " + running + " [Enter = drop them]" : "  stopping"));
                        }
                        if (ui.ElapsedS - lastBoard >= 30) { lastBoard = ui.ElapsedS; WriteBoard(); }
                    }
                    Thread.Sleep(200);
                }
                lock (_gate) { wr.Flush(); WriteBoard(); }
            }
            stop.Dispose();
            ui.End();
            Console.Error.WriteLine("stopped: " + _session + " fights this session, " + _total + " in total over " + _cands.Count + " fleets" + (abandoned ? " (fights in progress dropped)" : ""));
            PrintTop(10);
        }

        string Describe(Cand c)
        {
            return c.Name + "  win " + (100.0 * c.Wins / Math.Max(1, c.N)).ToString("F0", Inv) + "% of " + c.N + ", score " + c.Mean.ToString("F1", Inv);
        }

        void PrintTop(int k)
        {
            var top = Ranked(MinFights);
            Console.WriteLine("best fleets (ranked by score lower bound; full list in leaderboard.csv):");
            Console.WriteLine("  rank  win%  fights  score  turrets%  power%  fleet");
            for (int i = 0; i < top.Count && i < k; i++)
            {
                var c = top[i];
                Console.WriteLine("  " + (i + 1).ToString().PadLeft(4) + "  " + (100.0 * c.Wins / c.N).ToString("F0", Inv).PadLeft(4) + "  " + c.N.ToString().PadLeft(6)
                    + "  " + c.Mean.ToString("F1", Inv).PadLeft(5) + "  " + (100 * c.Armed / c.N).ToString("F0", Inv).PadLeft(8) + "  " + (100 * c.Power / c.N).ToString("F0", Inv).PadLeft(6) + "  " + c.Name);
            }
            Console.WriteLine("leaderboard: " + Path.Combine(OutDir, "leaderboard.csv"));
        }

        void WriteBoard()
        {
            var sb = new StringBuilder();
            sb.Append("rank,fleet,drones,fights,drone_wins,win_pct,mean_score,score_lcb,turrets_destroyed_pct,power_destroyed_pct,thrusters_destroyed_pct,drones_lost_avg,avg_duration_s,origin\n");
            var all = Ranked(1);
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                sb.Append(i + 1).Append(",\"").Append(c.Name).Append("\",").Append(c.Models.Length).Append(',').Append(c.N).Append(',').Append(c.Wins).Append(',')
                  .Append((100.0 * c.Wins / c.N).ToString("F1", Inv)).Append(',').Append(c.Mean.ToString("F2", Inv)).Append(',').Append(c.Lcb.ToString("F2", Inv)).Append(',')
                  .Append((100 * c.Armed / c.N).ToString("F1", Inv)).Append(',').Append((100 * c.Power / c.N).ToString("F1", Inv)).Append(',')
                  .Append((100 * c.Thr / c.N).ToString("F1", Inv)).Append(',').Append((c.Lost / c.N).ToString("F2", Inv)).Append(',')
                  .Append((c.Dur / c.N).ToString("F1", Inv)).Append(",\"").Append(c.Origin.Replace("\"", "'")).Append("\"\n");
            }
            string p = Path.Combine(OutDir, "leaderboard.csv"), tmp = p + ".tmp";
            try
            {
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
            }
            catch (IOException) { }     // e.g. open in a spreadsheet on Windows: try again next time
        }
    }
}
