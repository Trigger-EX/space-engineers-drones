// SLOT D. Output: one JSON object per line per run (out/sim/results/<batch>/runs.jsonl) and summary.csv grouped by scenario.
// Hand-written writer (invariant culture, "R" doubles, escaped strings); no reflection. ParseRun reads a line back through
// MiniJson (used by --resume and the `summarize` command).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SeSim.Run
{
    public static class JsonOut
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static void Str(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        static void Num(StringBuilder sb, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) { sb.Append("null"); return; }
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) sb.Append(((long)v).ToString(Inv));
            else sb.Append(v.ToString("R", Inv));
        }

        static void Key(StringBuilder sb, string k) { sb.Append('"').Append(k).Append("\":"); }
        static void KS(StringBuilder sb, string k, string v, bool comma = true) { Key(sb, k); Str(sb, v); if (comma) sb.Append(','); }
        static void KN(StringBuilder sb, string k, double v, bool comma = true) { Key(sb, k); Num(sb, v); if (comma) sb.Append(','); }
        static void KB(StringBuilder sb, string k, bool v, bool comma = true) { Key(sb, k); sb.Append(v ? "true" : "false"); if (comma) sb.Append(','); }

        public static void AppendRun(StringBuilder sb, RunResult r) { AppendRun(sb, r, true); }

        /// <summary>timing=false omits wall_ms, cpu_ms and the script CPU fields (determinism comparisons).</summary>
        public static void AppendRun(StringBuilder sb, RunResult r, bool timing)
        {
            sb.Append('{');
            KS(sb, "scenario", r.Scenario); KS(sb, "kind", r.Kind);
            KN(sb, "seed_index", r.SeedIndex); KS(sb, "seed", r.Seed.ToString(Inv));
            KS(sb, "outcome", r.Outcome); KN(sb, "winner_team", r.WinnerTeam);
            KN(sb, "duration_s", Math.Round(r.DurationS, 3)); KN(sb, "ttk_s", Math.Round(r.TimeToKillS, 3));
            if (timing) { KN(sb, "wall_ms", Math.Round(r.WallMs, 1)); KN(sb, "cpu_ms", Math.Round(r.CpuMs, 1)); }
            KS(sb, "settings_hash", r.SettingsHash);
            if (r.Error != null) KS(sb, "error", r.Error);
            Key(sb, "ships"); sb.Append('[');
            for (int i = 0; i < r.Ships.Count; i++)
            {
                var s = r.Ships[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                KN(sb, "ship", s.Ship); KS(sb, "model", s.Model); KN(sb, "team", s.Team);
                KB(sb, "alive", s.Alive); KB(sb, "capable", s.Capable);
                KN(sb, "disabled_at_s", Math.Round(s.DisabledAtS, 3)); KN(sb, "destroyed_at_s", Math.Round(s.DestroyedAtS, 3));
                KN(sb, "shots", s.Shots); KN(sb, "hits", s.Hits); KN(sb, "hits_raw", s.HitEvents);
                KN(sb, "hit_rate", s.Shots > 0 ? Math.Round((double)s.Hits / s.Shots, 4) : 0);
                KN(sb, "damage_dealt", Math.Round(s.DamageDealt, 1)); KN(sb, "damage_taken", Math.Round(s.DamageTaken, 1));
                KN(sb, "friendly_damage", Math.Round(s.FriendlyDamageDealt, 1)); KN(sb, "friendly_hits", s.FriendlyHitsDealt);
                KN(sb, "blocks_start", s.BlocksStart); KN(sb, "blocks_end", s.BlocksEnd);
                KN(sb, "weapons_start", s.WeaponsStart); KN(sb, "weapons_end", s.WeaponsEnd);
                KN(sb, "thrusters_start", s.ThrustersStart); KN(sb, "thrusters_end", s.ThrustersEnd);
                KN(sb, "power_start_mw", Math.Round(s.PowerStartMW, 2)); KN(sb, "power_end_mw", Math.Round(s.PowerEndMW, 2));
                KN(sb, "power_blocks_start", s.PowerBlocksStart); KN(sb, "power_blocks_end", s.PowerBlocksEnd);
                KN(sb, "armed_start", s.ArmedStart); KN(sb, "armed_end", s.ArmedEnd);
                KN(sb, "power_zero_at_s", Math.Round(s.PowerZeroAtS, 2)); KN(sb, "armed_zero_at_s", Math.Round(s.ArmedZeroAtS, 2));
                KN(sb, "blocks_lost", s.BlocksLost); KN(sb, "blocks_killed", s.BlocksKilled);
                KN(sb, "mass_lost_frac", Math.Round(s.MassLostFraction, 4));
                KN(sb, "time_on_target_s", Math.Round(s.TimeOnTargetS, 2));
                KN(sb, "script_target_s", Math.Round(s.ScriptTargetS, 2)); KN(sb, "first_script_target_s", Math.Round(s.FirstScriptTargetS, 2));
                KN(sb, "ai_on_s", Math.Round(s.AiOnS, 2));
                KN(sb, "min_enemy_dist_m", s.MinDistanceToEnemy == double.MaxValue ? -1 : Math.Round(s.MinDistanceToEnemy, 1));
                KN(sb, "avg_speed", Math.Round(s.AvgSpeed, 2)); KN(sb, "max_speed", Math.Round(s.MaxSpeed, 2));
                KN(sb, "collisions", s.Collisions); KN(sb, "collision_damage", Math.Round(s.CollisionDamageTaken, 1)); KN(sb, "detonations", s.Detonations);
                KS(sb, "script", s.Script); KN(sb, "script_runs", s.ScriptRuns);
                if (timing) { KN(sb, "script_cpu_ms", Math.Round(s.ScriptCpuMs, 1)); KN(sb, "script_max_tick_ms", Math.Round(s.ScriptMaxTickMs, 3)); }
                KN(sb, "exceptions", s.ScriptExceptions); KS(sb, "first_exception", s.FirstException);
                KB(sb, "stuck", s.StuckOrCircling); KS(sb, "stuck_kind", s.StuckKind, false);
                sb.Append('}');
            }
            sb.Append("]}");
        }

        // ------------------------------------------------------------------------------------------------------
        /// <summary>Reads one runs.jsonl line (fields written by AppendRun).</summary>
        public static RunResult ParseRun(string line)
        {
            object o = SeSim.Core.MiniJson.Parse(line);
            var r = new RunResult();
            r.Scenario = SeSim.Core.MiniJson.Str(o, "scenario", ""); r.Kind = SeSim.Core.MiniJson.Str(o, "kind", "");
            r.SeedIndex = (int)SeSim.Core.MiniJson.Num(o, "seed_index", 0);
            ulong sd; ulong.TryParse(SeSim.Core.MiniJson.Str(o, "seed", "0"), NumberStyles.None, Inv, out sd); r.Seed = sd;
            r.Outcome = SeSim.Core.MiniJson.Str(o, "outcome", ""); r.WinnerTeam = (int)SeSim.Core.MiniJson.Num(o, "winner_team", -1);
            r.DurationS = SeSim.Core.MiniJson.Num(o, "duration_s", 0); r.TimeToKillS = SeSim.Core.MiniJson.Num(o, "ttk_s", -1);
            r.WallMs = SeSim.Core.MiniJson.Num(o, "wall_ms", 0); r.CpuMs = SeSim.Core.MiniJson.Num(o, "cpu_ms", 0);
            r.SettingsHash = SeSim.Core.MiniJson.Str(o, "settings_hash", ""); r.Error = SeSim.Core.MiniJson.Str(o, "error", null);
            var arr = SeSim.Core.MiniJson.Arr(o, "ships");
            if (arr != null)
                for (int i = 0; i < arr.Count; i++)
                {
                    object so = arr[i];
                    var s = new ShipStats();
                    s.Ship = (int)SeSim.Core.MiniJson.Num(so, "ship", i); s.Model = SeSim.Core.MiniJson.Str(so, "model", ""); s.Team = (int)SeSim.Core.MiniJson.Num(so, "team", 0);
                    s.Alive = SeSim.Core.MiniJson.Bool(so, "alive", false); s.Capable = SeSim.Core.MiniJson.Bool(so, "capable", false);
                    s.DisabledAtS = SeSim.Core.MiniJson.Num(so, "disabled_at_s", -1); s.DestroyedAtS = SeSim.Core.MiniJson.Num(so, "destroyed_at_s", -1);
                    s.Shots = (long)SeSim.Core.MiniJson.Num(so, "shots", 0); s.Hits = (long)SeSim.Core.MiniJson.Num(so, "hits", 0); s.HitEvents = (long)SeSim.Core.MiniJson.Num(so, "hits_raw", s.Hits);
                    s.CollisionDamageTaken = SeSim.Core.MiniJson.Num(so, "collision_damage", 0);
                    s.DamageDealt = SeSim.Core.MiniJson.Num(so, "damage_dealt", 0); s.DamageTaken = SeSim.Core.MiniJson.Num(so, "damage_taken", 0);
                    s.FriendlyDamageDealt = SeSim.Core.MiniJson.Num(so, "friendly_damage", 0); s.FriendlyHitsDealt = (long)SeSim.Core.MiniJson.Num(so, "friendly_hits", 0);
                    s.BlocksStart = (int)SeSim.Core.MiniJson.Num(so, "blocks_start", 0); s.BlocksEnd = (int)SeSim.Core.MiniJson.Num(so, "blocks_end", 0);
                    s.WeaponsStart = (int)SeSim.Core.MiniJson.Num(so, "weapons_start", 0); s.WeaponsEnd = (int)SeSim.Core.MiniJson.Num(so, "weapons_end", 0);
                    s.ThrustersStart = (int)SeSim.Core.MiniJson.Num(so, "thrusters_start", 0); s.ThrustersEnd = (int)SeSim.Core.MiniJson.Num(so, "thrusters_end", 0);
                    s.BlocksLost = (long)SeSim.Core.MiniJson.Num(so, "blocks_lost", 0); s.BlocksKilled = (long)SeSim.Core.MiniJson.Num(so, "blocks_killed", 0);
                    s.MassLostFraction = SeSim.Core.MiniJson.Num(so, "mass_lost_frac", 0); s.TimeOnTargetS = SeSim.Core.MiniJson.Num(so, "time_on_target_s", 0);
                    s.ScriptTargetS = SeSim.Core.MiniJson.Num(so, "script_target_s", 0); s.FirstScriptTargetS = SeSim.Core.MiniJson.Num(so, "first_script_target_s", -1);
                    s.AiOnS = SeSim.Core.MiniJson.Num(so, "ai_on_s", 0);
                    double md = SeSim.Core.MiniJson.Num(so, "min_enemy_dist_m", -1); s.MinDistanceToEnemy = md < 0 ? double.MaxValue : md;
                    s.AvgSpeed = SeSim.Core.MiniJson.Num(so, "avg_speed", 0); s.MaxSpeed = SeSim.Core.MiniJson.Num(so, "max_speed", 0);
                    s.Collisions = (long)SeSim.Core.MiniJson.Num(so, "collisions", 0); s.Detonations = (long)SeSim.Core.MiniJson.Num(so, "detonations", 0);
                    s.Script = SeSim.Core.MiniJson.Str(so, "script", ""); s.ScriptRuns = (long)SeSim.Core.MiniJson.Num(so, "script_runs", 0);
                    s.ScriptCpuMs = SeSim.Core.MiniJson.Num(so, "script_cpu_ms", 0); s.ScriptMaxTickMs = SeSim.Core.MiniJson.Num(so, "script_max_tick_ms", 0);
                    s.ScriptExceptions = (long)SeSim.Core.MiniJson.Num(so, "exceptions", 0); s.FirstException = SeSim.Core.MiniJson.Str(so, "first_exception", null);
                    s.StuckOrCircling = SeSim.Core.MiniJson.Bool(so, "stuck", false); s.StuckKind = SeSim.Core.MiniJson.Str(so, "stuck_kind", "");
                    r.Ships.Add(s);
                }
            return r;
        }

        // ------------------------------------------------------------------------------------------------------
        static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        static string F(double v) { return double.IsNaN(v) || double.IsInfinity(v) ? "" : Math.Round(v, 3).ToString("0.###", Inv); }

        /// <summary>Columns: scenario,kind,runs,winsA,winsB,draws,timeouts,errors,avg_duration_s,p50_ttk_s,avg_hits_per_shot_A,
        /// avg_hits_per_shot_B,avg_dmg_taken_A,avg_dmg_taken_B,friendly_fire_runs,exceptions,avg_cpu_ms_per_run,
        /// then mutual_kills,stuck_runs,avg_time_on_target_A,avg_time_on_target_B,ttk_p10_s,ttk_p90_s.
        /// A = team 0 (first-named side), B = team 1. draws = mutual-kill + no-contest; timeouts are counted separately;
        /// avg_hits_per_shot = total hits / total shots of the side over all runs; avg_dmg_taken = damage taken by the side per run;
        /// p50_ttk_s = median time until the loser was beaten over decisive wins; exceptions = script exceptions summed over runs.</summary>
        public static void WriteSummaryCsv(string path, IList<RunResult> results)
        {
            var groups = new List<string>(); var byName = new Dictionary<string, List<RunResult>>();
            for (int i = 0; i < results.Count; i++)
            {
                List<RunResult> l;
                if (!byName.TryGetValue(results[i].Scenario, out l)) { l = new List<RunResult>(); byName[results[i].Scenario] = l; groups.Add(results[i].Scenario); }
                l.Add(results[i]);
            }
            groups.Sort(string.CompareOrdinal);
            var sb = new StringBuilder();
            sb.Append("scenario,kind,runs,winsA,winsB,draws,timeouts,errors,avg_duration_s,p50_ttk_s,avg_hits_per_shot_A,avg_hits_per_shot_B,avg_dmg_taken_A,avg_dmg_taken_B,friendly_fire_runs,exceptions,avg_cpu_ms_per_run,mutual_kills,stuck_runs,avg_time_on_target_A,avg_time_on_target_B,ttk_p10_s,ttk_p90_s\n");
            foreach (string g in groups)
            {
                var l = byName[g];
                int wa = 0, wb = 0, dr = 0, to = 0, er = 0, ff = 0, mk = 0, stuck = 0; long exc = 0;
                double dur = 0, cpu = 0;
                double[] shots = new double[2], hits = new double[2], dmg = new double[2], tot = new double[2];
                var ttk = new List<double>();
                for (int i = 0; i < l.Count; i++)
                {
                    var r = l[i];
                    dur += r.DurationS; cpu += r.CpuMs;
                    switch (r.Outcome)
                    {
                        case "win": if (r.WinnerTeam == 0) wa++; else if (r.WinnerTeam == 1) wb++; if (r.TimeToKillS >= 0) ttk.Add(r.TimeToKillS); break;
                        case "timeout": to++; break;
                        case "mutual-kill": dr++; mk++; break;
                        case "no-contest": dr++; break;
                        case "error": er++; break;
                        default: dr++; break;
                    }
                    bool f = false, st = false;
                    for (int k = 0; k < r.Ships.Count; k++)
                    {
                        var s = r.Ships[k]; int t = s.Team == 0 ? 0 : 1;
                        shots[t] += s.Shots; hits[t] += s.Hits; dmg[t] += s.DamageTaken; tot[t] += s.TimeOnTargetS;
                        exc += s.ScriptExceptions; if (s.FriendlyDamageDealt > 0 || s.FriendlyHitsDealt > 0) f = true; if (s.StuckOrCircling) st = true;
                    }
                    if (f) ff++; if (st) stuck++;
                }
                ttk.Sort();
                int n = l.Count;
                sb.Append(Csv(g)).Append(',').Append(Csv(l[0].Kind)).Append(',').Append(n).Append(',').Append(wa).Append(',').Append(wb).Append(',').Append(dr).Append(',')
                  .Append(to).Append(',').Append(er).Append(',').Append(F(dur / n)).Append(',').Append(ttk.Count > 0 ? F(Pct(ttk, 0.5)) : "").Append(',')
                  .Append(shots[0] > 0 ? F(hits[0] / shots[0]) : "").Append(',').Append(shots[1] > 0 ? F(hits[1] / shots[1]) : "").Append(',')
                  .Append(F(dmg[0] / n)).Append(',').Append(F(dmg[1] / n)).Append(',').Append(ff).Append(',').Append(exc).Append(',').Append(F(cpu / n)).Append(',')
                  .Append(mk).Append(',').Append(stuck).Append(',').Append(F(tot[0] / n)).Append(',').Append(F(tot[1] / n)).Append(',')
                  .Append(ttk.Count > 0 ? F(Pct(ttk, 0.1)) : "").Append(',').Append(ttk.Count > 0 ? F(Pct(ttk, 0.9)) : "").Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static double Pct(List<double> sorted, double p)
        {
            if (sorted.Count == 1) return sorted[0];
            double pos = p * (sorted.Count - 1);
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }
    }
}
