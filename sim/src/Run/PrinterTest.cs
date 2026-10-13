// Printer test (HiveMotherAI on the REAL 'Mobile Hive' blueprint). The whole blueprint is spawned, its Hive Mother PB runs the real script
// (the text stored in out/Blueprints/<model>/bp.sbc, or --script-file FILE to run another version, e.g. the old v2.0 from git history) for
// --seconds sim seconds. The projectors (and optionally one welder per bay) are replaced by FAULTY stand-ins that behave like the real game
// in the ways that can produce a NullReferenceException in a script:
//
//   --fault none         the plain sim projector semantics (placed-block countdown while a welder works)
//   --fault noproj       projector with NO projection: IsProjecting=false, every counter throws NullReferenceException
//   --fault halfload     IsProjecting=true while the projection stats are not ready (window [--fault-at, +--fault-len] s): counters throw NRE
//   --fault projlost     at --fault-at s the projection is cleared (Keep Projection lost): IsProjecting=false, counters throw NRE from then on
//   --fault projoff      projector stuck switched off (the Enabled setter is ignored): IsProjecting=false, counters throw NRE
//   --fault isproj       IsProjecting itself throws NRE inside the window (projector being rebuilt)
//   --fault welderclosed at --fault-at s welder 1 of every bay is removed: every member access throws NRE
//   --fault all          halfload + welderclosed + projlost on different bays (bay 1,2,3 / 4 = plain) in the same run
//
//   --timer-chain        BlockAutomation on; at --timer-at s (default 20) every 'Timer Build [Bay N]' is started with short delays (Build 30 s, Release 5 s, Reclamp 8 s).
//                        PASS needs: the timers expired, the script showed 'timer cycle running' for the bay, the bay's merge block went off and came back, and the script
//                        left the bay alone meanwhile. Run with --seconds 120.
//   mono SimHost.exe printer-test [--model "Mobile Hive"] [--seconds 320] [--fault none] [--fault-at 40] [--fault-len 3]
//        [--script-file FILE] [--seed 1] [--repo DIR] [--echo] [--expect-clean]
//
// Reported: per 30 s the Hive PB screen's bay lines; how many Main() runs ended in "ERROR ..." (the script's own catch block) vs total
// runs; the first throw site (the faulty stand-in records the script method that touched it); drones that left the printer.
// PASS (exit 0) = the script never died, at least one drone left (a bay passed RELEASE) in the plain run, and with --expect-clean there was no
// "ERROR" text in the terminal info at all. Exit 1 otherwise. The stand-ins model HYPOTHESES about the real API (see docs/foundry-notes.md v2.2).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;
using SeSim.Core;
using SeSim.Blueprint;
using SeSim.Api;

namespace SeSim.Run
{
    public static class PrinterFaults
    {
        public static string Mode = "none";
        public static double At = 40, Len = 3;
        public static string FirstThrow = null;             // "member <- script method" of the first NRE any stand-in threw
        public static int Throws;
        public static readonly Dictionary<string, int> ThrowSites = new Dictionary<string, int>();

        public static Exception Boom(string member)
        {
            Throws++;
            string site = "?";
            try
            {
                foreach (var line in Environment.StackTrace.Split('\n'))
                {
                    string l = line.Trim();
                    if (l.StartsWith("at Program.", StringComparison.Ordinal)) { int p = l.IndexOf('('); site = (p > 0 ? l.Substring(3, p - 3) : l.Substring(3)).Trim(); break; }
                }
            }
            catch (Exception) { }
            string key = member + " <- " + site;
            int n; ThrowSites.TryGetValue(key, out n); ThrowSites[key] = n + 1;
            if (FirstThrow == null) FirstThrow = key;
            return new NullReferenceException("Object reference not set to an instance of an object");
        }
    }

    /// <summary>Projector stand-in. Same placed-block countdown as SimProjector, plus the real-game null behaviours selected by PrinterFaults.</summary>
    public sealed class FaultyProjector : SimFunctionalBlock, IMyProjector
    {
        public int Total, Remaining; public int BayNo; public string Mode;
        double _printed; long _lastTick = -1;
        public FaultyProjector(BlockInst inst, World world, int bay, string mode) : base(inst, world)
        {
            if (inst.Src != null && inst.Src.ProjectedGrids.Count > 0) Total = inst.Src.ProjectedGrids[0].Blocks.Count;
            Remaining = Total; BayNo = bay; Mode = mode;
        }
        double Now { get { return World.Tick * SimSettings.Dt; } }
        bool InWindow { get { double t = Now; return t >= PrinterFaults.At && t < PrinterFaults.At + PrinterFaults.Len; } }
        bool WelderActive()
        {
            var blocks = Inst.Body.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                var a = blocks[i].Api;
                if ((a is SimShipWelder || a is FaultyWelder) && a.IsWorking) return true;
            }
            return false;
        }
        void Advance()
        {
            long t = World.Tick;
            if (_lastTick < 0) { _lastTick = t; return; }
            long dt = t - _lastTick; _lastTick = t;
            if (dt <= 0 || Remaining <= 0 || !EnabledValue || Inst.Destroyed) return;
            if (!WelderActive()) return;
            _printed += dt * SimSettings.Dt * ApiTuning.PrintBlocksPerSecond;
            int whole = (int)_printed; _printed -= whole;
            Remaining = Math.Max(0, Remaining - whole);
        }
        bool Cleared { get { return Mode == "noproj" || Mode == "projoff" || (Mode == "projlost" && Now >= PrinterFaults.At); } }
        public override bool Enabled
        {
            get { return Mode == "projoff" ? false : EnabledValue; }
            set { if (Mode == "projoff") return; EnabledValue = value; }
        }
        public bool IsProjecting
        {
            get
            {
                if (Mode == "isproj" && InWindow) throw PrinterFaults.Boom("IsProjecting");
                if (Cleared) return false;
                if (Mode == "halfload" && InWindow) return true;     // clipboard active, statistics not ready
                Advance(); return EnabledValue && Total > 0 && Remaining >= 0;
            }
        }
        void Guard(string member)
        {
            if (Cleared || (Mode == "halfload" && InWindow)) throw PrinterFaults.Boom(member);
        }
        public int RemainingBlocks { get { Guard("RemainingBlocks"); Advance(); return Remaining; } }
        public int TotalBlocks { get { Guard("TotalBlocks"); return Total; } }
        public int BuildableBlocksCount { get { Guard("BuildableBlocksCount"); Advance(); return Remaining; } }
    }

    /// <summary>Welder stand-in that can be 'closed' (removed from the grid after the script took its reference): then every member throws.</summary>
    public sealed class FaultyWelder : SimFunctionalBlock, IMyShipWelder
    {
        public bool Closed2;
        public FaultyWelder(BlockInst inst, World world) : base(inst, world) { }
        public override bool Enabled
        {
            get { if (Closed2) throw PrinterFaults.Boom("welder.Enabled"); return EnabledValue; }
            set { if (Closed2) throw PrinterFaults.Boom("welder.Enabled="); EnabledValue = value; }
        }
        public new bool IsFunctional { get { if (Closed2) throw PrinterFaults.Boom("welder.IsFunctional"); return Inst.IsFunctional; } }
        public override bool IsWorking { get { if (Closed2) return false; return base.IsWorking; } }
    }

    public static class PrinterTest
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Arg(string[] a, string k, string d) { for (int i = 1; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1]; return d; }
        static bool Has(string[] a, string k) { for (int i = 1; i < a.Length; i++) if (a[i] == k) return true; return false; }
        static double Dbl(string[] a, string k, double d) { double r; return double.TryParse(Arg(a, k, null) ?? "", NumberStyles.Float, Inv, out r) ? r : d; }

        static int BayOf(string name)
        {
            int i = name == null ? -1 : name.IndexOf("[Bay ", StringComparison.Ordinal);
            if (i < 0) return -1;
            int j = name.IndexOf(']', i); int n;
            return j > i + 5 && int.TryParse(name.Substring(i + 5, j - i - 5), out n) ? n : -1;
        }

        // BlueprintLoader only reads <CustomData>; tools/sebp.py (like the current game) stores Custom Data in the block's ModStorage entry with this GUID
        const string CustomDataGuid = "74de02b3-27f9-4960-b1c4-27351f2b06d1";
        static string ModStorageCustomData(string bpPath, string blockName)
        {
            var doc = new XmlDocument(); doc.Load(bpPath);
            foreach (XmlNode blk in doc.SelectNodes("//*[CustomName]"))
            {
                if (blk["CustomName"].InnerText != blockName) continue;
                foreach (XmlNode k in blk.SelectNodes(".//Key"))
                    if (k.InnerText == CustomDataGuid && k.ParentNode["Value"] != null) return k.ParentNode["Value"].InnerText;
            }
            return null;
        }

        public static int Run(string[] args, string simDir, string repo)
        {
            string model = Arg(args, "--model", "Mobile Hive");
            double secs = Dbl(args, "--seconds", 320);
            string fault = Arg(args, "--fault", "none");
            PrinterFaults.At = Dbl(args, "--fault-at", 40); PrinterFaults.Len = Dbl(args, "--fault-len", 3);
            ulong seed = (ulong)Dbl(args, "--seed", 1);
            string scriptFile = Arg(args, "--script-file", null);
            bool timerChain = Has(args, "--timer-chain"); double timerAt = Dbl(args, "--timer-at", 20);
            var settings = new SimSettings(); if (timerChain) settings.BlockAutomation = true; settings.CountInstructions = Has(args, "--count-instructions"); settings.InstructionCountMode = Arg(args, "--instruction-count-mode", "blocks"); settings.EnforceInstructionLimit = Has(args, "--enforce-instruction-limit");
            DefDb defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            var bp = BlueprintLoader.Get(Path.Combine(Path.Combine(repo, "out"), "Blueprints"), model);
            var w = new World(settings, defs, seed);
            var host = new ScriptHost(w, simDir);
            w.Scripts = host;
            w.Teams.Init(2);
            var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero };
            var bodies = GridFactory.Spawn(w, bp, 0, 0, at, new Dictionary<long, long>());
            // swap in the stand-ins BEFORE any script constructor runs (FlushPending attaches the PBs)
            var timers = new List<SimTimer>(); var bayMerges = new List<SimMergeBlock>(); bool timersStarted = false; int pausedLines = 0, mergeOffSeen = 0, mergeOnAfter = 0; bool mergeWasOff = false;
            SimProgrammableBlock hive = null; var projs = new List<FaultyProjector>(); var welders = new List<FaultyWelder>();
            foreach (var b in bodies)
                foreach (var bi in b.Blocks)
                {
                    string nm = bi.Api != null ? bi.Api.CustomName : null;
                    if (bi.Api is SimProgrammableBlock && nm != null && nm.StartsWith("Hive Mother", StringComparison.Ordinal)) hive = (SimProgrammableBlock)bi.Api;
                    if (bi.Api is SimTimer && nm != null && nm.StartsWith("Timer ", StringComparison.Ordinal)) timers.Add((SimTimer)bi.Api);
                    if (bi.Api is SimMergeBlock && nm != null && nm.Contains("[Bay ") && nm.StartsWith("Merge", StringComparison.Ordinal)) bayMerges.Add((SimMergeBlock)bi.Api);
                    if (bi.Api is SimProjector)
                    {
                        int bay = BayOf(nm);
                        string mode = fault;
                        if (fault == "all") mode = bay == 1 ? "halfload" : bay == 3 ? "projlost" : "none";
                        var fp = new FaultyProjector(bi, w, bay, mode); fp.CustomNameValue = nm; fp.CustomDataValue = bi.Api.CustomDataValue; fp.EnabledValue = ((SimProjector)bi.Api).EnabledValue;
                        bi.Api = fp; projs.Add(fp);
                    }
                    else if (bi.Api is SimShipWelder && (fault == "welderclosed" || fault == "all"))
                    {
                        int bay = BayOf(nm);
                        var fw = new FaultyWelder(bi, w); fw.CustomNameValue = nm; fw.EnabledValue = ((SimShipWelder)bi.Api).EnabledValue;
                        bi.Api = fw; welders.Add(fw);
                    }
                }
            if (hive == null) { Console.Error.WriteLine("test setup failed: no 'Hive Mother' PB in " + model); return 2; }
            if (scriptFile != null) hive.ProgramText = File.ReadAllText(scriptFile);
            string cdata = ModStorageCustomData(Path.Combine(Path.Combine(Path.Combine(repo, "out"), "Blueprints"), Path.Combine(model, "bp.sbc")), hive.CustomName);
            // the sim never PLACES projected blocks (docs/foundry-notes.md), so the drone box can never fill: by default every bay gets Skip Finish Check=true
            // (BUILD -> FINISH -> CHARGE still run). --real-finish keeps the blueprint's box keys (FINISH then stalls on "0 of N drone blocks present").
            if (cdata != null && !Has(args, "--real-finish"))
                for (int bn = 1; bn <= 12; bn++) cdata = cdata.Replace("[Bay " + bn + "]\n", "[Bay " + bn + "]\nSkip Finish Check=true\n");
            if (cdata != null && !Has(args, "--no-custom-data")) hive.CustomData = cdata;
            Console.WriteLine("Hive PB Custom Data from the blueprint: " + (cdata == null ? "none" : cdata.Length + " chars, Drone Box keys " + (cdata.Contains("Drone Box Min") ? "present" : "absent")));
            BlockApiFactory.FlushPending(w);
            var runner = hive.Runner;
            Console.WriteLine("printer-test " + model + ": fault=" + fault + " at " + PrinterFaults.At.ToString("F0", Inv) + " s, projectors " + projs.Count + ", stand-in welders " + welders.Count
                + ", script " + (runner != null ? runner.ScriptName : "none") + " (" + (hive.ProgramText.Contains("v2.2") ? "v2.2" : hive.ProgramText.Contains("HIVE MOTHER  v2.1") ? "v2.1" : hive.ProgramText.Contains("HIVE MOTHER  v2.0") ? "v2.0" : "?") + ")"
                + (runner != null && runner.Dead ? " DEAD " + runner.DeathReason : ""));
            int runs = 0, errRuns = 0; string firstErr = null; double firstErrT = -1; string lastErr = null;
            long ticks = (long)(secs * SimSettings.TicksPerSecond);
            long lastRuns = 0; bool closed = false; int released = 0;
            var sb = new StringBuilder(); var seenReleased = new HashSet<int>(); string lastBays = "";
            var bayState = new Dictionary<int, string>(); var printed = new Dictionary<int, int>(); var statesSeen = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (long t = 0; t < ticks; t++)
            {
                double now = t / SimSettings.TicksPerSecond;
                if (!closed && now >= PrinterFaults.At && welders.Count > 0)
                {
                    closed = true;
                    foreach (var fw in welders) if (fw.CustomNameValue != null && fw.CustomNameValue.StartsWith("Welder 1 ", StringComparison.Ordinal)) fw.Closed2 = true;
                }
                if (timerChain && !timersStarted && now >= timerAt)
                {
                    timersStarted = true;
                    foreach (var tm in timers)
                    {
                        if (tm.CustomName.StartsWith("Timer Build", StringComparison.Ordinal)) { tm.TriggerDelay = 30; tm.StartCountdown(); }
                        else if (tm.CustomName.StartsWith("Timer Release", StringComparison.Ordinal)) tm.TriggerDelay = 5;
                        else if (tm.CustomName.StartsWith("Timer Reclamp", StringComparison.Ordinal)) tm.TriggerDelay = 8;
                    }
                }
                if (timerChain && timersStarted && t % 6 == 0)
                {
                    bool anyOff = false; foreach (var mb in bayMerges) if (!mb.Enabled) anyOff = true;
                    if (anyOff) { if (!mergeWasOff) mergeOffSeen++; } else if (mergeWasOff) mergeOnAfter++;
                    mergeWasOff = anyOff;
                }
                w.Step();
                if (runner == null || runner.Dead) break;
                if (runner.Runs != lastRuns)
                {
                    lastRuns = runner.Runs; runs++;
                    string e = runner.EchoText ?? "";
                    if (e.StartsWith("ERROR", StringComparison.Ordinal) || e.Contains("\nERROR ") ) { errRuns++; lastErr = e.Split('\n')[0]; if (firstErr == null) { firstErr = lastErr; firstErrT = w.Time; } }
                    else if (e.Contains("ERRORS (") ) { errRuns++; if (firstErr == null) { firstErr = "(screen shows ERRORS)"; firstErrT = w.Time; } }
                    if (e.Contains("timer cycle running")) pausedLines++;
                    foreach (var line in e.Split('\n'))
                    {
                        if (!line.StartsWith("Bay ", StringComparison.Ordinal)) continue;
                        int c = line.IndexOf(' ', 4); int n; if (c < 0 || !int.TryParse(line.Substring(4, c - 4), out n)) continue;
                        int i1 = line.IndexOf(": ", StringComparison.Ordinal); if (i1 < 0) continue;
                        string st = line.Substring(i1 + 2).Split(' ')[0];
                        string prev; bayState.TryGetValue(n, out prev);
                        if (prev == "RELEASE" && st != "RELEASE" && st != "FAULT") { if (seenReleased.Add(n * 1000 + (int)(w.Time / 20))) released++; }
                        bayState[n] = st; int sc; statesSeen.TryGetValue(st, out sc); statesSeen[st] = sc + 1;
                        int pi = line.IndexOf(" #", StringComparison.Ordinal); if (pi > 0) { int pn; string rest = line.Substring(pi + 2).Split(' ')[0]; if (int.TryParse(rest, out pn)) printed[n] = pn; }
                    }
                }
                if (t % (long)(30 * SimSettings.TicksPerSecond) == 0 && t > 0)
                {
                    string e = runner.EchoText ?? "";
                    var lines = new List<string>(); foreach (var line in e.Split('\n')) if (line.StartsWith("Bay ", StringComparison.Ordinal) || line.StartsWith("   ") || line.StartsWith("ERROR") || line.Contains("ERRORS")) lines.Add(line.Length > 150 ? line.Substring(0, 150) : line);
                    sb.Append("  t=" + w.Time.ToString("F0", Inv) + "s runs=" + runs + " errRuns=" + errRuns + " | " + string.Join(" / ", lines.ToArray()) + "\n");
                }
            }
            Console.Write(sb.ToString());
            int totalPrinted = 0; foreach (var kv in printed) totalPrinted += kv.Value;
            var ss = new StringBuilder(); foreach (var kv in statesSeen) ss.Append(kv.Key).Append(' ');
            Console.WriteLine("RESULT fault=" + fault + ": Hive runs " + runs + ", runs whose terminal info shows ERROR " + errRuns + (firstErr != null ? " (first at " + firstErrT.ToString("F1", Inv) + " s: " + firstErr + ")" : "")
                + ", script dead " + (runner == null || runner.Dead ? "YES " + (runner != null ? runner.DeathReason : "") : "no") + ", drones released (printed counters) " + totalPrinted
                + ", stand-in throws " + PrinterFaults.Throws + ", bay states seen: " + ss.ToString().Trim());
            if (PrinterFaults.FirstThrow != null) Console.WriteLine("FIRST THROW SITE: " + PrinterFaults.FirstThrow);
            foreach (var kv in PrinterFaults.ThrowSites) Console.WriteLine("  throw site " + kv.Key + " x" + kv.Value);
            if (lastErr != null) Console.WriteLine("LAST ERROR TEXT: " + lastErr);
            if (Has(args, "--echo")) Console.WriteLine("--- Hive PB terminal info (last run) ---\n" + runner.EchoText);
            int expiries = 0; foreach (var tm in timers) expiries += tm.Expiries;
            if (timerChain) Console.WriteLine("TIMER CHAIN: timers " + timers.Count + ", expiries " + expiries + ", script screens showing 'timer cycle running' " + pausedLines + ", merge blocks went off " + mergeOffSeen + "x and on again " + mergeOnAfter + "x");
            bool chainOk = !timerChain || (timers.Count > 0 && expiries >= 3 && pausedLines > 0 && mergeOffSeen > 0 && mergeOnAfter > 0);
            bool ok = chainOk && runner != null && !runner.Dead && (fault != "none" || timerChain || (statesSeen.ContainsKey("BUILD") && statesSeen.ContainsKey("FINISH") && statesSeen.ContainsKey("CHARGE"))) && (!Has(args, "--expect-clean") || errRuns == 0);
            if (settings.CountInstructions) IlReport.Dump(host, "printer-test " + model + " fault=" + fault, args);
            Console.WriteLine(ok ? "PRINTER OK" : "PRINTER FAILED");
            return ok ? 0 : 1;
        }
    }
}
