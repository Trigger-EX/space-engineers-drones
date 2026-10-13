// Dock-launch with a REAL cradle (control_arch M5, S13). The host is a blueprint with merge-block cradles and drones already clamped to them (GridFuse turns the
// touching CubeGrids into one body): "Cradle Pod Test" (the 2-slot Cradle Pod with two Hornets) or "Cradle Test" (Corsair Wing Lite with the real Helm / Staff / Bay
// scripts). The bay PB (scripts/CradleBayAI.cs) is told 'release all' (PB argument, or 'Run Staff "release 1"' with --via staff); the drones run their REAL scripts in the
// blueprint's default state. Measured per drone: when the grid split (after the command), how far / which way the drone burned, whether any drone block
// sat inside an occupied host cell (the lane check of the burn vector), the final mode; across drones: the gap between two merge-offs.
//
//   mono SimHost.exe dock-launch --cradle [--model "Cradle Pod Test"] [--seconds 20] [--release-at 3] [--seed 1] [--cmd "release all"] [--via bay|staff]
//        [--helm idle|hunt|none] [--spin RAD_S] [--charge F] [--away "x,y,z"] [--expect launch|stay] [--max-split 3] [--repo DIR] [--trace FILE]
//   --helm CMD    the command the [Helm] PB (ShrikeAI in 'Cradle Test') gets at 0.3 s; default idle: the ship holds still (hunt: it flies and turns, the gates must wait)
//   --spin W      the host turns about its up axis at W rad/s when the run starts (the bay's Max Turn gate is 0.1)
//   --charge F    every drone battery starts at F of its capacity (the bay waits for Charge Fraction 0.9)
//   --away "x,y,z"  rewrites Away of every [Bay n] (in host axes), e.g. "0,0,1" aims the lane into the pod's own wall: the lane gate must refuse
//   --expect stay   the run PASSES when no drone left (negative controls); default launch
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using SeSim.Core;
using SeSim.Blueprint;
using SeSim.Api;

namespace SeSim.Run
{
    /// <summary>A spawned cradle host with its drones, PBs and the per-tick measurements of the launches.</summary>
    public sealed class CradleRig
    {
        public World W; public ScriptHost Host; public DefDb Defs; public List<Body> Bodies;
        public SimProgrammableBlock Bay, Staff, Helm; public List<SimProgrammableBlock> Drones = new List<SimProgrammableBlock>();
        public List<Track> Tracks = new List<Track>(); public string Model;
        public Vector3D AwayLocal = new Vector3D(0, 0, -1);

        public sealed class Track
        {
            public SimProgrammableBlock Pb; public int Index; public Vector3D Start;
            public double SplitAt = -1, LeftAt = -1, BurnDeg = -1, Lateral = -1, MinSep = 1e9; public int OverlapTicks; public string Mode = "?";
            public bool Burned;
        }

        public static CradleRig Build(string simDir, string repo, string model, ulong seed, Action<SimSettings> cfg, double spin, double charge, string away)
        {
            var rig = new CradleRig { Model = model };
            var settings = new SimSettings(); if (cfg != null) cfg(settings);
            rig.Defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            var w = new World(settings, rig.Defs, seed); rig.W = w;
            rig.Host = new ScriptHost(w, simDir); w.Scripts = rig.Host; w.Teams.Init(2);
            var bp = BlueprintLoader.Get(Path.Combine(Path.Combine(repo, "out"), "Blueprints"), model);
            rig.Bodies = GridFactory.Spawn(w, bp, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up, AngularVelocity = new Vector3D(0, spin, 0) }, new Dictionary<long, long>());
            BlockApiFactory.FlushPending(w);
            foreach (var b in rig.Bodies)
                foreach (var bi in b.Blocks)
                {
                    var pb = bi.Api as SimProgrammableBlock; if (pb == null || pb.Runner == null) continue;
                    string n = pb.CustomNameValue ?? "";
                    if (n.StartsWith("[Pod]") || n.StartsWith("[Bay]")) rig.Bay = pb;
                    else if (n.StartsWith("[Staff]")) rig.Staff = pb;
                    else if (n.StartsWith("[Helm]")) rig.Helm = pb;
                    else if (n.StartsWith("Hornet")) rig.Drones.Add(pb);
                }
            // the game keeps a PB's Custom Data in its ModStorage component; the sim loader only records it (BlockData.StoredCustomData): copy it into the PBs and let them reload
            foreach (var b in rig.Bodies)
                foreach (var bi in b.Blocks)
                {
                    var pb = bi.Api as SimProgrammableBlock; if (pb == null || pb.Runner == null || string.IsNullOrEmpty(bi.Src.StoredCustomData)) continue;      // (a script that ran its constructor has already written its defaults: the file wins)
                    pb.CustomData = bi.Src.StoredCustomData.Replace("\r", ""); rig.Host.RunArgument(pb, "reload", UpdateType.Terminal);
                }
            if (rig.Bay == null || rig.Drones.Count == 0) throw new InvalidOperationException("model '" + model + "': no [Pod]/[Bay] PB or no Hornet PB found");
            if (charge >= 0)
                foreach (var b in rig.Bodies) foreach (var bi in b.Blocks) if (bi.Battery != null && bi.ConstructId > 0) bi.Battery.StoredMWh = bi.Battery.MaxStoredMWh * charge;
            if (away != null)
            {
                string cd = rig.Bay.CustomData ?? "";
                cd = Regex.Replace(cd, @"(?m)^Away\s*=.*$", "Away=" + away);
                cd = Regex.Replace(cd, @"(?m)^Away [XYZ]\s*=.*\n?", "");
                rig.Bay.CustomData = cd;
                string[] p = away.Split(','); rig.AwayLocal = new Vector3D(double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture));
                rig.Host.RunArgument(rig.Bay, "reload", UpdateType.Terminal);
            }
            int i = 0;
            foreach (var d in rig.Drones) rig.Tracks.Add(new Track { Pb = d, Index = i++, Start = d.Inst.WorldCenter });
            return rig;
        }

        public Body HostBody { get { return Bay.Inst.Body; } }
        public static T Fld<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new InvalidOperationException("script has no field " + name);
            return (T)f.GetValue(o);
        }
        /// <summary>The bay PB's states, in bay order ("EMPTY", "DOCKED", ...) read from the script's _bays list.</summary>
        public List<string> BayStates()
        {
            var r = new List<string>(); var prog = Bay.Runner.Program; if (prog == null) return r;
            var list = Fld<System.Collections.IList>(prog, "_bays");
            foreach (object b in list) r.Add((string)b.GetType().GetField("State").GetValue(b));
            return r;
        }
        public string BayMsgs()
        {
            var sb = new StringBuilder(); var prog = Bay.Runner.Program; if (prog == null) return "";
            foreach (object b in Fld<System.Collections.IList>(prog, "_bays")) { string m = (string)b.GetType().GetField("Msg").GetValue(b); if (m != "") sb.Append(m).Append(" | "); }
            return sb.ToString();
        }
        public static string ModeOf(SimProgrammableBlock pb)
        {
            var m = Regex.Match(pb.Runner != null ? pb.Runner.EchoText ?? "" : "", @"Mode\s*:\s*(\w+)"); return m.Success ? m.Groups[1].Value : "?";
        }

        /// <summary>One step of the world plus the measurements.</summary>
        public void Step()
        {
            W.Step();
            Body hb = HostBody; double now = W.Time;
            Vector3D away = Vector3D.TransformNormal(AwayLocal, hb.WorldMatrix); if (away.LengthSquared() > 1e-9) away.Normalize();
            foreach (var t in Tracks)
            {
                Body db = t.Pb.Inst.Body;
                if (db == hb) continue;
                if (t.SplitAt < 0) t.SplitAt = now;
                Vector3D pos = t.Pb.Inst.WorldCenter, rel = pos - t.Start; double dist = rel.Length();
                if (dist < 25)
                {
                    var g = hb.ApiGrid as SimCubeGrid;
                    foreach (var bi in db.Blocks)
                    {
                        if (bi.Destroyed) continue;
                        if (hb.BlockAtCell(g.WorldToGridInteger(bi.WorldCenter)) != null) { t.OverlapTicks++; break; }
                    }
                }
                if (!t.Burned && now - t.SplitAt >= 1.5)
                {
                    t.Burned = true; Vector3D rv = db.LinearVelocity - hb.LinearVelocity;
                    t.BurnDeg = rv.Length() < 0.3 ? 180 : Math.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(Vector3D.Normalize(rv), away)))) * 180 / Math.PI;
                }
                if (t.LeftAt < 0 && dist > 12) { t.LeftAt = now; t.Lateral = (rel - away * Vector3D.Dot(rel, away)).Length(); }
            }
        }
    }

    public static class DockLaunchCradle
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Arg(string[] a, string k, string d) { for (int i = 1; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1]; return d; }
        static double Dbl(string[] a, string k, double d) { double r; return double.TryParse(Arg(a, k, null) ?? "", NumberStyles.Float, Inv, out r) ? r : d; }

        public static int Run(string[] args, string simDir, string repo)
        {
            string model = Arg(args, "--model", "Cradle Pod Test"), cmd = Arg(args, "--cmd", "release all"), via = Arg(args, "--via", "bay"), expect = Arg(args, "--expect", "launch"), helm = Arg(args, "--helm", "idle");
            double secs = Dbl(args, "--seconds", 20), rel = Dbl(args, "--release-at", 3), spin = Dbl(args, "--spin", 0), charge = Dbl(args, "--charge", -1), maxSplit = Dbl(args, "--max-split", 3);
            ulong seed = (ulong)Dbl(args, "--seed", 1);
            var rig = CradleRig.Build(simDir, repo, model, seed, null, spin, charge, Arg(args, "--away", null));
            var pbCmd = via == "staff" ? rig.Staff : rig.Bay;
            if (pbCmd == null) { Console.Error.WriteLine("model has no " + via + " PB"); return 2; }
            string trace = Arg(args, "--trace", null); StreamWriter tw = null;
            if (trace != null) { tw = new StreamWriter(trace, false, new UTF8Encoding(false)); tw.WriteLine("t,bay_states,split_flags,drone0_dist"); }
            Console.WriteLine("dock-launch --cradle " + model + ": " + rig.Drones.Count + " drones, host " + rig.HostBody.Blocks.Count + " blocks, bay PB '" + rig.Bay.CustomNameValue + "' script " + rig.Bay.Runner.ScriptName + (rig.Bay.Runner.Dead ? " DEAD " + rig.Bay.Runner.DeathReason : ""));
            long ticks = (long)(secs * SimSettings.TicksPerSecond), relTick = (long)(rel * SimSettings.TicksPerSecond); bool sent = false; double cmdAt = -1;
            bool fault = false; string lastStates = "";
            for (long t = 0; t < ticks; t++)
            {
                if (t == 20 && rig.Helm != null && helm != "none") rig.Host.RunArgument(rig.Helm, helm, UpdateType.Terminal);      // the Helm starts in HUNT: 'idle' holds the ship still
                if (!sent && t >= relTick) { sent = true; cmdAt = rig.W.Time; rig.Host.RunArgument(pbCmd, cmd, UpdateType.Terminal); }
                rig.Step();
                if (t % 6 == 0)
                {
                    foreach (var tr in rig.Tracks) tr.Mode = CradleRig.ModeOf(tr.Pb);
                    string st = string.Join(" ", rig.BayStates().ToArray());
                    if (st.Contains("FAULT")) fault = true;
                    if (st != lastStates) { lastStates = st; Console.WriteLine("  t=" + rig.W.Time.ToString("F2", Inv) + "  bays: " + st); }
                    if (tw != null) tw.WriteLine(rig.W.Time.ToString("F2", Inv) + "," + st + "," + (rig.Tracks[0].SplitAt >= 0 ? 1 : 0) + "," + (rig.Tracks[0].Pb.Inst.WorldCenter - rig.Tracks[0].Start).Length().ToString("F1", Inv));
                }
            }
            if (tw != null) tw.Dispose();
            return Report(rig, cmdAt, expect, maxSplit, fault, Has(args, "--echo"));
        }
        static bool Has(string[] a, string k) { for (int i = 1; i < a.Length; i++) if (a[i] == k) return true; return false; }

        /// <summary>Prints the RESULT line and the verdict; 0 = pass.</summary>
        public static int Report(CradleRig rig, double cmdAt, string expect, double maxSplit, bool fault, bool echo)
        {
            var sb = new StringBuilder(); var fails = new List<string>();
            var splits = new List<double>(); int left = 0; double maxBurn = 0, maxLat = 0; int overlap = 0;
            foreach (var t in rig.Tracks)
            {
                sb.Append(string.Format(Inv, "  drone {0}: split {1}, burn {2} deg, >12 m {3}, lateral {4} m, host-cell overlaps {5} ticks, mode {6}\n", t.Index + 1,
                    t.SplitAt >= 0 ? (t.SplitAt - cmdAt).ToString("F2", Inv) + " s after the command" : "never", t.BurnDeg >= 0 ? t.BurnDeg.ToString("F1", Inv) : "-",
                    t.LeftAt >= 0 ? (t.LeftAt - cmdAt).ToString("F2", Inv) + " s" : "never", t.Lateral >= 0 ? t.Lateral.ToString("F2", Inv) : "-", t.OverlapTicks, t.Mode));
                if (t.SplitAt >= 0) splits.Add(t.SplitAt);
                if (t.LeftAt >= 0) left++;
                if (t.BurnDeg > maxBurn) maxBurn = t.BurnDeg; if (t.Lateral > maxLat) maxLat = t.Lateral; overlap += t.OverlapTicks;
            }
            splits.Sort();
            double gapMin = 1e9, gapMax = 0; for (int i = 1; i < splits.Count; i++) { double g = splits[i] - splits[i - 1]; gapMin = Math.Min(gapMin, g); gapMax = Math.Max(gapMax, g); }
            Console.Write(sb.ToString());
            Console.WriteLine("RESULT cradle " + rig.Model + ": split " + splits.Count + "/" + rig.Tracks.Count + (splits.Count > 0 ? ", first " + (splits[0] - cmdAt).ToString("F2", Inv) + " s after the command" : "")
                + (splits.Count > 1 ? ", wave gap " + gapMin.ToString("F2", Inv) + ".." + gapMax.ToString("F2", Inv) + " s" : "") + ", max burn angle " + maxBurn.ToString("F1", Inv) + " deg, max lateral " + maxLat.ToString("F2", Inv)
                + " m, overlap ticks " + overlap + ", FAULT " + (fault ? "yes" : "no") + ", bays " + string.Join(" ", rig.BayStates().ToArray()));
            string msgs = rig.BayMsgs(); if (msgs != "") Console.WriteLine("  bay messages: " + msgs);
            if (echo) Console.WriteLine("--- bay PB echo ---\n" + rig.Bay.Runner.EchoText);
            if (expect == "stay")
            {
                if (splits.Count > 0) fails.Add("a drone left although the release had to be refused");
            }
            else
            {
                if (fault) fails.Add("a bay went FAULT");
                if (splits.Count < rig.Tracks.Count) fails.Add("not every drone separated");
                for (int i = 0; i < splits.Count; i++) if (splits[i] - cmdAt > maxSplit + 2.0 * i) fails.Add("drone " + (i + 1) + " split " + (splits[i] - cmdAt).ToString("F2", Inv) + " s after the command (limit " + (maxSplit + 2.0 * i).ToString("F0", Inv) + ")");
                if (splits.Count > 1 && (gapMin < 1.8 || gapMax > 2.6)) fails.Add("wave gap " + gapMin.ToString("F2", Inv) + ".." + gapMax.ToString("F2", Inv) + " s (want 2.0 +- 0.5)");
                if (maxBurn > 10) fails.Add("burn vector " + maxBurn.ToString("F1", Inv) + " deg off the lane axis");
                if (overlap > 0) fails.Add("a drone block sat inside an occupied host cell (" + overlap + " ticks)");
                if (left < rig.Tracks.Count) fails.Add("not every drone got 12 m away");
                foreach (var t in rig.Tracks) if (t.Mode == "IDLE" || t.Mode == "?") fails.Add("drone " + (t.Index + 1) + " is " + t.Mode + " at the end");
            }
            if (fails.Count == 0) { Console.WriteLine(expect == "stay" ? "CRADLE OK (refused as expected)" : "CRADLE OK"); return 0; }
            foreach (var f in fails) Console.WriteLine("  FAIL: " + f);
            Console.WriteLine("CRADLE FAILED");
            return 1;
        }
    }
}
