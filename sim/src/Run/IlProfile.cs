// il_profiler. Instruction-count profiling (tools/ilcount/, sim/shim/IlCounter.cs). Everything here is inert unless
// SimSettings.CountInstructions is true.
//  * IlInstrument: turns a script text into an instrumented assembly (Keen's InstructionCountingRewriter ported to Roslyn, run as a
//    child process tools/ilcount/IlRewrite.exe, then mcs) cached in <simDir>/scripts_il/<key>.dll + <key>.sites.tsv.
//  * IlStats: per PB record of every Main / constructor run: the count, the tick, the UpdateType, a per-injection-site hit vector of
//    the peak run and the summed hit vector of all runs (to name the cause of the peak: which method / loop produced the counts).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Sandbox.ModAPI.Ingame;
using SimShim;
using SeSim.Core;

namespace SeSim.Run
{
    public static class IlInstrument
    {
        public static string RewriterPath(string simDir) { return Path.Combine(Path.Combine(simDir, "ilcount"), "IlRewrite.exe"); }

        static int Exec(string exe, string args, out string output)
        {
            bool mono = Type.GetType("Mono.Runtime") != null;
            var psi = new ProcessStartInfo
            {
                FileName = mono && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "mono" : exe,
                Arguments = mono && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "\"" + exe + "\" " + args : args,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                string so = p.StandardOutput.ReadToEnd(), se = p.StandardError.ReadToEnd();
                p.WaitForExit();
                output = so + "\n" + se;
                return p.ExitCode;
            }
        }

        /// <summary>Builds (or finds cached) the instrumented dll for a script text. Returns its path; sites file = path without .dll + ".sites.tsv".</summary>
        public static string Build(string scriptText, string key, string simDir, string mode, string plainDll)
        {
            string dir = Path.Combine(Path.GetFullPath(simDir), "scripts_il");
            Directory.CreateDirectory(dir);
            string suffix = mode == "il" ? ".ilc" : "";
            string dll = Path.Combine(dir, key + suffix + ".dll"), sites = Path.Combine(dir, key + suffix + ".sites.tsv");
            if (File.Exists(dll) && File.Exists(sites)) return dll;
            string outp;
            if (mode == "il")
            {
                string cec = Path.Combine(Path.Combine(simDir, "ilcount"), "IlCecil.exe");
                if (!File.Exists(cec)) throw new InvalidOperationException("--instruction-count-mode il needs " + cec + " (run tools/ilcount/build.sh; needs Mono.Cecil)");
                string t2 = dll + "." + Process.GetCurrentProcess().Id + ".tmp";
                int rc2 = Exec(cec, "\"" + plainDll + "\" \"" + t2 + "\" \"" + sites + "\" \"" + Path.Combine(simDir, "SimShim.dll") + "\"", out outp);
                if (rc2 != 0 || !File.Exists(t2)) throw new InvalidOperationException("IlCecil failed: " + outp.Trim());
                File.Delete(dll); File.Move(t2, dll);
                return dll;
            }
            string rw = RewriterPath(simDir);
            if (!File.Exists(rw)) throw new InvalidOperationException("--count-instructions needs " + rw + " (run: SIM_OUT=" + simDir + " bash tools/ilcount/build.sh)");
            string wrapped = ScriptCompiler.Wrap(scriptText);
            int offset = 0; foreach (char c in ScriptCompiler.Usings) if (c == '\n') offset++;
            offset++;                                                    // the "public partial class" line
            string srcIn = Path.Combine(dir, key + ".wrapped.cs"), srcOut = Path.Combine(dir, key + ".il.cs");
            File.WriteAllText(srcIn, wrapped, new UTF8Encoding(false));
            string fw = Path.GetDirectoryName(typeof(object).Assembly.Location);
            var refs = new StringBuilder();
            foreach (string r in new[] { Path.Combine(fw, "mscorlib.dll"), Path.Combine(fw, "System.dll"), Path.Combine(fw, "System.Core.dll"), Path.Combine(Path.Combine(fw, "Facades"), "System.Runtime.dll"),
                                         Path.Combine(simDir, "SeApi.dll"), Path.Combine(simDir, "SimShim.dll") })
                if (File.Exists(r)) refs.Append(" \"").Append(Path.GetFullPath(r)).Append('"');
            int rc = Exec(rw, "\"" + srcIn + "\" \"" + srcOut + "\" \"" + sites + "\" " + offset + refs, out outp);
            if (rc != 0 || !File.Exists(srcOut) || !File.Exists(sites)) throw new InvalidOperationException("IlRewrite failed: " + outp.Trim());
            string tmp = dll + "." + Process.GetCurrentProcess().Id + ".tmp";
            string args = "-langversion:6 -target:library -optimize+ -nowarn:0169,0414,0649,0219,0168,0675"
                        + " -r:\"" + Path.Combine(simDir, "SeApi.dll") + "\" -r:\"" + Path.Combine(simDir, "SimShim.dll") + "\" -out:\"" + tmp + "\" \"" + srcOut + "\"";
            rc = Exec("mcs", args, out outp);
            if (rc != 0 || !File.Exists(tmp)) throw new InvalidOperationException("instrumented script does not compile: " + outp.Trim());
            File.Delete(dll); File.Move(tmp, dll);
            return dll;
        }

        public static string[] LoadSites(string dll)
        {
            string f = Path.ChangeExtension(dll, null) + ".sites.tsv";
            var l = new List<string>();
            foreach (string line in File.ReadAllLines(f))
            {
                int t = line.IndexOf('\t');
                if (t > 0) l.Add(line.Substring(t + 1));
            }
            return l.ToArray();
        }
    }

    public static class IlReport
    {
        /// <summary>printer-test / dock-launch / follow-test: one JSON line per instrumented PB, appended to --il-out FILE or printed as "IL {...}".</summary>
        public static void Dump(ScriptHost host, string label, string[] args)
        {
            string path = null;
            for (int i = 1; i < args.Length - 1; i++) if (args[i] == "--il-out") path = args[i + 1];
            var sb = new StringBuilder();
            foreach (var r in host.Runners)
            {
                if (r.Il == null) continue;
                bool dead = r.Dead && r.DeathReason != "detached" && r.DeathReason != "block destroyed";
                sb.Append(path != null ? "" : "IL ").Append("{\"label\":\"").Append(label.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\",\"dead\":").Append(dead ? "true" : "false")
                  .Append(",\"il\":").Append(r.Il.ToJson(r.ScriptName, r.ShipIndex, SimSettings.Dt)).Append('\n');
                sb.Length -= 1; sb.Append("}\n");
            }
            if (sb.Length == 0) return;
            if (path != null) File.AppendAllText(path, sb.ToString()); else Console.Write(sb.ToString());
        }
    }

    public sealed class IlStats
    {
        public readonly string[] Sites;
        public long Runs;
        public int CtorCount = -1;
        public int Max = -1, MaxDepth, Overruns;
        public long MaxTick = -1; public string MaxType = "";
        public readonly List<int> Counts = new List<int>(), Ticks = new List<int>();
        public readonly List<string> Types = new List<string>();
        readonly long[] _siteTotal; readonly int[] _peakSites;

        public IlStats(string[] sites) { Sites = sites; _siteTotal = new long[sites.Length]; _peakSites = new int[sites.Length]; }

        public void Begin(int limit) { IlCounter.Begin(Sites.Length, limit); }

        /// <summary>Called after the constructor / a Main run (also when it threw).</summary>
        public void End(long tick, UpdateType ut, bool ctor, bool tooComplex)
        {
            int c = IlCounter.Total;
            if (IlCounter.MaxDepth > MaxDepth) MaxDepth = IlCounter.MaxDepth;
            if (tooComplex) Overruns++;
            if (ctor) { CtorCount = c; return; }
            Runs++;
            Counts.Add(c); Ticks.Add((int)tick); Types.Add(ut.ToString());
            var s = IlCounter.Sites;
            for (int i = 0; i < _siteTotal.Length; i++) _siteTotal[i] += s[i];
            if (c > Max)
            {
                Max = c; MaxTick = tick; MaxType = ut.ToString();
                Array.Copy(s, _peakSites, _peakSites.Length);
            }
        }

        static double Pct(List<int> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            int i = (int)Math.Ceiling(p * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, i))];
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // sim-time windows ("phases") in seconds: launch/search, early fight, mid, late
        public static readonly double[] WindowEdges = { 0, 10, 30, 60, 120, 1e9 };

        static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

        void TopSites(StringBuilder sb, long[] counts, int n, double divisor)
        {
            var idx = new List<int>();
            for (int i = 0; i < counts.Length; i++) if (counts[i] > 0) idx.Add(i);
            idx.Sort((a, b) => counts[b].CompareTo(counts[a]));
            sb.Append('[');
            for (int k = 0; k < idx.Count && k < n; k++)
            {
                string[] p = Sites[idx[k]].Split('\t');
                if (k > 0) sb.Append(',');
                sb.Append("{\"site\":").Append(idx[k]).Append(",\"kind\":\"").Append(Esc(p[0])).Append("\",\"method\":\"").Append(Esc(p[1])).Append("\",\"line\":").Append(p[2])
                  .Append(",\"n\":").Append((counts[idx[k]] / divisor).ToString("0.###", Inv)).Append('}');
            }
            sb.Append(']');
        }

        /// <summary>One JSON object for this PB: distribution, windows, peak cause, mean cause.</summary>
        public string ToJson(string script, int ship, double tickSeconds)
        {
            var sorted = new List<int>(Counts); sorted.Sort();
            double sum = 0; foreach (int c in Counts) sum += c;
            var sb = new StringBuilder();
            sb.Append("{\"script\":\"").Append(Esc(script)).Append("\",\"ship\":").Append(ship).Append(",\"runs\":").Append(Runs)
              .Append(",\"ctor\":").Append(CtorCount).Append(",\"max\":").Append(Max).Append(",\"max_tick\":").Append(MaxTick).Append(",\"max_type\":\"").Append(MaxType)
              .Append("\",\"mean\":").Append((Runs > 0 ? sum / Runs : 0).ToString("0.##", Inv))
              .Append(",\"p50\":").Append(Pct(sorted, 0.5).ToString(Inv)).Append(",\"p99\":").Append(Pct(sorted, 0.99).ToString(Inv))
              .Append(",\"max_depth\":").Append(MaxDepth).Append(",\"overruns\":").Append(Overruns);
            sb.Append(",\"windows\":[");
            for (int w = 0; w + 1 < WindowEdges.Length; w++)
            {
                var l = new List<int>(); double s = 0;
                for (int i = 0; i < Counts.Count; i++)
                {
                    double t = Ticks[i] * tickSeconds;
                    if (t >= WindowEdges[w] && t < WindowEdges[w + 1]) { l.Add(Counts[i]); s += Counts[i]; }
                }
                l.Sort();
                if (w > 0) sb.Append(',');
                sb.Append("{\"from_s\":").Append(WindowEdges[w].ToString(Inv)).Append(",\"to_s\":").Append(w + 2 == WindowEdges.Length ? "-1" : WindowEdges[w + 1].ToString(Inv))
                  .Append(",\"n\":").Append(l.Count).Append(",\"mean\":").Append((l.Count > 0 ? s / l.Count : 0).ToString("0.##", Inv))
                  .Append(",\"p99\":").Append(Pct(l, 0.99).ToString(Inv)).Append(",\"max\":").Append(l.Count > 0 ? l[l.Count - 1] : 0).Append('}');
            }
            sb.Append("],\"peak_sites\":");
            var pk = new long[_peakSites.Length]; for (int i = 0; i < pk.Length; i++) pk[i] = _peakSites[i];
            TopSites(sb, pk, 8, 1);
            sb.Append(",\"mean_sites\":");
            TopSites(sb, _siteTotal, 8, Math.Max(1, Runs));
            sb.Append('}');
            return sb.ToString();
        }
    }
}
