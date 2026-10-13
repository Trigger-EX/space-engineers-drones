// Usage: SIM_OUT=<dir> bash sim/build.sh --no-scripts && mono <dir>/SimHost.exe multipb-probe [--seeds 3] [--model "Corsair Wing"] [--timer-model "Warden Wing"] [--repo DIR]
// Probe for "can several PBs (+ timers) bypass the 100k-char script limit?": synthetic 2-3 PB scripts on a real multi-PB blueprint, measuring IGC unicast latency,
// throughput and queue cap, lockstep age of split control loops, TryRun semantics and timer chains IN THE SIM (a model; see docs/audit/timers_pbs.md for what is unverified in the game).
// The probe scripts read the tick from Me.CustomName ("t=<tick>", written by the host before every Step): a measuring clock only, the real game has none.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;
using SeSim.Blueprint;
using SeSim.Api;

namespace SeSim.Run
{
    public static class MultiPbProbe
    {
        const string Script = @"
string role, mode; long peer, peer2; IMyUnicastListener ul; int N;
public string Log = """", LastSrc = """";
public int Cnt, Sent, SendOk, FirstSeq = -1, LastSeq = -1, PreMax, Runs, Pings, Gaps, Bounce = -1, BounceSelf = -1, Nested = -1, Seq;
public Dictionary<int,int> AgeHist = new Dictionary<int,int>(), PerRun = new Dictionary<int,int>();
int Tk() { int v; string s = Me.CustomName; return s.StartsWith(""t="") && int.TryParse(s.Substring(2), out v) ? v : -1; }
void Inc(Dictionary<int,int> d, int k) { int v; d.TryGetValue(k, out v); d[k] = v + 1; }
public Program() {
  string[] p = Storage.Split(';'); role = p[0]; peer = long.Parse(p[1]); mode = p[2]; N = int.Parse(p[3]); peer2 = p.Length > 4 ? long.Parse(p[4]) : 0;
  ul = IGC.UnicastListener;
  if (mode == ""cb"") { Runtime.UpdateFrequency = UpdateFrequency.None; ul.SetMessageCallback(""m""); }
  else if (mode == ""u10"") Runtime.UpdateFrequency = UpdateFrequency.Update10;
  else if (mode == ""u100"") Runtime.UpdateFrequency = UpdateFrequency.Update100;
  else if (mode == ""none"") Runtime.UpdateFrequency = UpdateFrequency.None;
  else Runtime.UpdateFrequency = UpdateFrequency.Update1;
}
IMyProgrammableBlock Pb(long id) { return GridTerminalSystem.GetBlockWithId(id) as IMyProgrammableBlock; }
public void Main(string arg, UpdateType src) {
  Runs++; int t = Tk(); LastSrc = src.ToString();
  switch (role) {
    case ""ping"":
      if (t >= 120 && (t - 120) % 30 == 0 && Pings < 5) { Pings++; if (IGC.SendUnicastMessage(peer, ""P"", new MyTuple<int,int>(t, 0))) SendOk++; }
      while (ul.HasPendingMessage) { MyIGCMessage m = ul.AcceptMessage(); MyTuple<int,int> d = m.As<MyTuple<int,int>>(); Log += (t - d.Item1) + ""/"" + (d.Item2 - d.Item1) + "" ""; }
      break;
    case ""echo"":
      while (ul.HasPendingMessage) { MyIGCMessage m = ul.AcceptMessage(); MyTuple<int,int> d = m.As<MyTuple<int,int>>(); IGC.SendUnicastMessage(m.Source, ""R"", new MyTuple<int,int>(d.Item1, t)); }
      break;
    case ""burst"":
      if (t == 120) for (int i = 0; i < N; i++) { Sent++; if (IGC.SendUnicastMessage(peer, ""B"", i)) SendOk++; }
      break;
    case ""sink"":
      if (t >= N) {
        if (PreMax == 0) PreMax = ul.MaxWaitingMessages;
        while (ul.HasPendingMessage) { int s = ul.AcceptMessage().As<int>(); if (FirstSeq < 0) FirstSeq = s; LastSeq = s; Cnt++; }
      }
      break;
    case ""stream"":
      if (t >= 120 && t < 720) for (int i = 0; i < N; i++) { Sent++; if (IGC.SendUnicastMessage(peer, ""S"", Seq++)) SendOk++; }
      break;
    case ""drain"": {
      int c = 0;
      while (ul.HasPendingMessage) { int s = ul.AcceptMessage().As<int>(); if (LastSeq >= 0 && s != LastSeq + 1) Gaps++; LastSeq = s; Cnt++; c++; }
      if (t >= 120 && t < 720) Inc(PerRun, c);
      break; }
    case ""ctrl"": {
      if (t >= 100 && t < 700) IGC.SendUnicastMessage(peer, ""C"", new MyTuple<int,int>(t, 0));
      int c = 0;
      while (ul.HasPendingMessage) { MyTuple<int,int> d = ul.AcceptMessage().As<MyTuple<int,int>>(); c++; if (t >= 120) Inc(AgeHist, t - d.Item1); }
      if (t >= 120 && t < 700) Inc(PerRun, c);
      break; }
    case ""work"":
      while (ul.HasPendingMessage) { MyTuple<int,int> d = ul.AcceptMessage().As<MyTuple<int,int>>(); IGC.SendUnicastMessage(peer, ""C"", new MyTuple<int,int>(d.Item1, d.Item2 + 1)); }
      break;
    case ""rpcM"": {
      if (t >= 120 && t < 700) {
        IMyProgrammableBlock w = Pb(peer); bool ok = w.TryRun(""req:"" + t); Sent++; if (ok) SendOk++;
        string r = w.CustomData; if (r.StartsWith(""res:"")) Inc(AgeHist, t - int.Parse(r.Substring(4)));
      }
      break; }
    case ""rpcW"":
      if (arg.StartsWith(""req:"")) { Me.CustomData = ""res:"" + arg.Substring(4); Cnt++; }
      break;
    case ""sem"": {
      IMyProgrammableBlock w = Pb(peer), w2 = Pb(peer2);
      if (t == 300) Log += ""a(TryRun ok)="" + w.TryRun(""ok"") + "" "";
      if (t == 301) Log += ""b(self TryRun)="" + (Me as IMyProgrammableBlock).TryRun(""self"") + "" "";
      if (t == 302) Log += ""c(W->M reentry, see W.Bounce)="" + w.TryRun(""bounce"") + "" "";
      if (t == 303) Log += ""d(M->W->W2 nested)="" + w.TryRun(""deep"") + "" "";
      if (t == 304) ((IMyFunctionalBlock)w2).Enabled = false;
      if (t == 305) Log += ""e(target switched off)="" + w2.TryRun(""ok"") + "" "";
      if (t == 306) ((IMyFunctionalBlock)w2).Enabled = true;
      if (t == 307) Log += ""f(target back on)="" + w2.TryRun(""ok"") + "" "";
      if (t == 308) { bool r; try { r = w.TryRun(""boom""); Log += ""g(callee throws)=returned "" + r + "" ""; } catch (Exception e) { Log += ""g(callee throws)=THROWN into caller "" + e.GetType().Name + "" ""; } }
      if (t == 309) Log += ""h(callee dead after throw)="" + w.TryRun(""ok"") + "" "";
      if (t == 310) Log += ""caller still alive "";
      break; }
    case ""semW"":
      if (arg == ""ok"") Cnt++;
      else if (arg == ""bounce"") { Bounce = Pb(peer).TryRun(""x"") ? 1 : 0; BounceSelf = (Me as IMyProgrammableBlock).TryRun(""y"") ? 1 : 0; }
      else if (arg == ""deep"") Nested = Pb(peer2).TryRun(""ok"") ? 1 : 0;
      else if (arg == ""boom"") throw new InvalidOperationException(""boom"");
      break;
    case ""trig"": {
      IMyTimerBlock tm = GridTerminalSystem.GetBlockWithId(peer) as IMyTimerBlock;
      if (t == 120) { if (mode == ""start"") tm.StartCountdown(); else tm.Trigger(); Log += ""sent@"" + t + "" ""; }
      break; }
    case ""catch"":
      Log += arg + ""@"" + t + "":"" + src + "" "";
      break;
  }
}
";

        sealed class Ctx
        {
            public World W; public ScriptHost Host; public List<SimProgrammableBlock> Pbs = new List<SimProgrammableBlock>(); public List<SimProgrammableBlock> AllPbs = new List<SimProgrammableBlock>();
            public List<SimTimer> Timers = new List<SimTimer>();
        }

        static string _repo, _simDir, _model;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string Arg(string[] a, string k, string d) { for (int i = 0; i + 1 < a.Length; i++) if (a[i] == k) return a[i + 1]; return d; }

        static Ctx Make(ulong seed, int latency, int nPbs, Func<long[], string[]> storages, bool automation = false) { return Make2(seed, latency, nPbs, (cx, ids) => storages(ids), automation); }

        static Ctx Make2(ulong seed, int latency, int nPbs, Func<Ctx, long[], string[]> storages, bool automation)
        {
            var s = new SimSettings(); s.IgcLatencyTicks = latency; s.BlockAutomation = automation;
            DefDb defs = DefDb.LoadCached(Path.Combine(_repo, "defs_stats.json"));
            var bp = BlueprintLoader.Get(Path.Combine(Path.Combine(_repo, "out"), "Blueprints"), _model);
            var w = new World(s, defs, seed);
            var host = new ScriptHost(w, _simDir);
            w.Scripts = host; w.Teams.Init(2);
            var at = new SpawnPlacement { Origin = Vector3D0(), Forward = new VRageMath.Vector3D(0, 0, -1), Up = new VRageMath.Vector3D(0, 1, 0), LinearVelocity = VRageMath.Vector3D.Zero, AngularVelocity = VRageMath.Vector3D.Zero };
            var bodies = GridFactory.Spawn(w, bp, 0, 0, at, new Dictionary<long, long>());
            var c = new Ctx { W = w, Host = host };
            var pending = w.Igc.PendingScripts.ToArray(); var mainPbs = new List<SimProgrammableBlock>(); foreach (var q0 in pending) if (q0.Inst.ConstructId == 0) mainPbs.Add(q0);                  // attach (= creation) order
            if (Environment.GetEnvironmentVariable("PROBE_DEBUG") != null) foreach (var q in pending) Console.WriteLine("  PB " + q.EntityId + " '" + q.CustomName + "' body " + (q.Inst.Body != null ? q.Inst.Body.EntityId.ToString() : "-") + " construct " + q.Inst.ConstructId);
            foreach (var b in bodies) foreach (var bi in b.Blocks) { if (bi.Api is SimTimer) c.Timers.Add((SimTimer)bi.Api); }
            if (mainPbs.Count < nPbs) throw new InvalidOperationException(_model + " has only " + mainPbs.Count + " PBs on the main construct");
            for (int i = 0; i < pending.Length; i++) { c.AllPbs.Add(pending[i]); if (!mainPbs.Contains(pending[i]) || mainPbs.IndexOf(pending[i]) >= nPbs) w.Igc.PendingScripts.Remove(pending[i]); }
            for (int i = 0; i < nPbs; i++) c.Pbs.Add(mainPbs[i]);
            var ids = new long[nPbs]; for (int i = 0; i < nPbs; i++) ids[i] = c.Pbs[i].EntityId;
            string[] st = storages(c, ids);
            for (int i = 0; i < nPbs; i++) { c.Pbs[i].ProgramText = Script; c.Pbs[i].StorageText = st[i]; }
            BlockApiFactory.FlushPending(w);
            for (int i = 0; i < nPbs; i++) if (c.Pbs[i].Runner == null || c.Pbs[i].Runner.Dead) throw new InvalidOperationException("probe PB " + i + " dead: " + (c.Pbs[i].Runner != null ? c.Pbs[i].Runner.DeathReason : "no runner"));
            return c;
        }
        static VRageMath.Vector3D Vector3D0() { return VRageMath.Vector3D.Zero; }

        static void Step(Ctx c, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                string t = "t=" + c.W.Tick;
                for (int k = 0; k < c.AllPbs.Count; k++) c.AllPbs[k].CustomNameValue = t;
                c.W.Step();
            }
        }

        static T F<T>(Ctx c, int i, string name)
        {
            object p = c.Pbs[i].Runner.Program;
            return (T)p.GetType().GetField(name).GetValue(p);
        }
        static string Dead(Ctx c) { var sb = new StringBuilder(); for (int i = 0; i < c.Pbs.Count; i++) if (c.Pbs[i].Runner.Dead) sb.Append(" [PB" + i + " DEAD: " + c.Pbs[i].Runner.DeathReason + "]"); return sb.ToString(); }
        static string Hist(Dictionary<int, int> h) { var sb = new StringBuilder(); var keys = new List<int>(h.Keys); keys.Sort(); foreach (int k in keys) sb.Append(k).Append(':').Append(h[k]).Append(' '); return sb.ToString().Trim(); }

        /// <summary>Runs fn for each seed; prints the result once if identical for all seeds, else every distinct result.</summary>
        static void Seeds(int seeds, string label, Func<ulong, string> fn)
        {
            var distinct = new List<string>(); var cnt = new List<int>();
            for (int s = 1; s <= seeds; s++)
            {
                string r; try { r = fn((ulong)s); } catch (Exception e) { r = "ERROR " + e.Message; }
                int ix = distinct.IndexOf(r); if (ix < 0) { distinct.Add(r); cnt.Add(1); } else cnt[ix]++;
            }
            for (int i = 0; i < distinct.Count; i++) Console.WriteLine("  " + label + " -> " + distinct[i] + (distinct.Count == 1 ? "   (same in all " + seeds + " seeds)" : "   (" + cnt[i] + "/" + seeds + " seeds)"));
        }

        public static int Run(string[] args, string simDir, string repo)
        {
            _simDir = simDir; _repo = repo; _model = Arg(args, "--model", "Corsair Wing"); string timerModel = Arg(args, "--timer-model", "Warden Wing");
            int seeds = int.Parse(Arg(args, "--seeds", "3"), Inv);
            Console.WriteLine("multipb-probe on '" + _model + "', " + seeds + " seeds per case. [SIM] numbers only; the game is not modelled to this precision.");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Latency(seeds); Throughput(seeds); Lockstep(seeds); TryRunSemantics(seeds); _model = timerModel; Timers(seeds);
            Console.WriteLine("done in " + (sw.ElapsedMilliseconds / 1000.0).ToString("F1", Inv) + " s wall");
            return 0;
        }

        // ---- (1) round trip -------------------------------------------------------------------------------------------------
        static void Latency(int seeds)
        {
            Console.WriteLine("== (1) unicast PB-A -> PB-B -> PB-A, same grid, 5 pings; each entry = round-trip ticks / one-way ticks (send run -> B's run that read it) ==");
            foreach (int lat in new[] { 1, 2 })
                foreach (string mode in new[] { "u1", "cb", "u10", "u100" })
                    foreach (bool rev in new[] { false, true })
                    {
                        string md = mode; int l = lat; bool rv = rev;
                        Seeds(seeds, "IgcLatencyTicks=" + l + " B=" + md + " " + (rv ? "B runs before A" : "A runs before B"), seed =>
                        {
                            int a = rv ? 1 : 0, b = rv ? 0 : 1;
                            var c = Make(seed, l, 2, ids => { var st = new string[2]; st[a] = "ping;" + ids[b] + ";u1;0"; st[b] = "echo;" + ids[a] + ";" + md + ";0"; return st; });
                            Step(c, 400);
                            return "rtt/oneway: " + F<string>(c, a, "Log").Trim() + " (sent " + F<int>(c, a, "SendOk") + "/5)" + Dead(c);
                        });
                    }
        }

        // ---- (2) throughput -------------------------------------------------------------------------------------------------
        static void Throughput(int seeds)
        {
            Console.WriteLine("== (2a) burst: A sends N unicast ints in ONE run at tick 120; B (Update1) starts reading at tick 130 ==");
            foreach (int n in new[] { 10, 25, 26, 100, 1000 })
            {
                int nn = n;
                Seeds(seeds, "N=" + n, seed =>
                {
                    var c = Make(seed, 1, 2, ids => new[] { "burst;" + ids[1] + ";u1;" + nn, "sink;" + ids[0] + ";u1;130" });
                    Step(c, 200);
                    var h = c.W.Igc;
                    return "send calls ok " + F<int>(c, 0, "SendOk") + "/" + F<int>(c, 0, "Sent") + ", B listener MaxWaitingMessages " + F<int>(c, 1, "PreMax") + ", B read " + F<int>(c, 1, "Cnt") + " (first seq " + F<int>(c, 1, "FirstSeq") + ", last seq " + F<int>(c, 1, "LastSeq") + "), hub sent/delivered/dropped " + h.Sent + "/" + h.Delivered + "/" + h.Dropped + Dead(c);
                });
            }
            Console.WriteLine("== (2b) sustained: A sends R msgs EVERY tick for 600 ticks (10 s); B drains all of its queue on each of its runs ==");
            foreach (string mode in new[] { "u1", "u10", "u100" })
                foreach (int r in new[] { 1, 3, 25, 26, 100 })
                {
                    string md = mode; int rr = r;
                    Seeds(seeds, "B=" + md + " R=" + rr + "/tick", seed =>
                    {
                        var c = Make(seed, 1, 2, ids => new[] { "stream;" + ids[1] + ";u1;" + rr, "drain;" + ids[0] + ";" + md + ";0" });
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        Step(c, 800);
                        var h = c.W.Igc;
                        return "sent " + F<int>(c, 0, "Sent") + ", B received " + F<int>(c, 1, "Cnt") + " (" + (F<int>(c, 1, "Cnt") * 100.0 / Math.Max(1, F<int>(c, 0, "Sent"))).ToString("F0", Inv) + " %), seq gaps seen " + F<int>(c, 1, "Gaps") + ", dropped by queue cap " + h.Dropped + ", msgs per B run {" + Hist(F<Dictionary<int, int>>(c, 1, "PerRun")) + "}";
                    });
                }
        }

        // ---- (3) lockstep ---------------------------------------------------------------------------------------------------
        static void Lockstep(int seeds)
        {
            Console.WriteLine("== (3) control loop split across PBs: M (Update1) sends its state EVERY tick, workers compute and answer; age = ticks between M's send and M reading the answer ==");
            foreach (int lat in new[] { 1, 2 })
            {
                foreach (string mode in new[] { "u1", "cb", "u10" })
                {
                    string md = mode; int l = lat;
                    Seeds(seeds, "2 PBs  M<->W  W=" + md + " IgcLatencyTicks=" + l, seed =>
                    {
                        var c = Make(seed, l, 2, ids => new[] { "ctrl;" + ids[1] + ";u1;0", "work;" + ids[0] + ";" + md + ";0" });
                        Step(c, 800);
                        return "age histogram (age:count) {" + Hist(F<Dictionary<int, int>>(c, 0, "AgeHist")) + "}, answers read per M run {" + Hist(F<Dictionary<int, int>>(c, 0, "PerRun")) + "}" + Dead(c);
                    });
                }
                int l3 = lat;
                Seeds(seeds, "3 PBs  M->W1->W2->M (all Update1) IgcLatencyTicks=" + l3, seed =>
                {
                    var c = Make(seed, l3, 3, ids => new[] { "ctrl;" + ids[1] + ";u1;0", "work;" + ids[2] + ";u1;0", "work;" + ids[0] + ";u1;0" });
                    Step(c, 800);
                    return "age histogram {" + Hist(F<Dictionary<int, int>>(c, 0, "AgeHist")) + "}, answers per M run {" + Hist(F<Dictionary<int, int>>(c, 0, "PerRun")) + "}" + Dead(c);
                });
            }
            Seeds(seeds, "3 PBs  chain M->W1->W2->M with run order W2, W1, M", seed =>
            {
                // pbs[2]=M sends to pbs[1]=W1; W1 forwards to pbs[0]=W2; W2 answers pbs[2]
                var c = Make(seed, 1, 3, ids => new[] { "work;" + ids[2] + ";u1;0", "work;" + ids[0] + ";u1;0", "ctrl;" + ids[1] + ";u1;0" });
                Step(c, 800);
                return "age histogram {" + Hist(F<Dictionary<int, int>>(c, 2, "AgeHist")) + "}" + Dead(c);
            });
            Console.WriteLine("-- zero-lag alternative: M calls W.TryRun(request) every tick and reads W.CustomData in the same run (synchronous RPC); age = ticks --");
            Seeds(seeds, "TryRun RPC, W has no update frequency", seed =>
            {
                var c = Make(seed, 1, 2, ids => new[] { "rpcM;" + ids[1] + ";u1;0", "rpcW;" + ids[0] + ";none;0" });
                Step(c, 800);
                return "TryRun true " + F<int>(c, 0, "SendOk") + "/" + F<int>(c, 0, "Sent") + ", W ran " + F<int>(c, 1, "Cnt") + "x, answer age histogram {" + Hist(F<Dictionary<int, int>>(c, 0, "AgeHist")) + "}, W last UpdateType " + F<string>(c, 1, "LastSrc") + Dead(c);
            });
        }

        // ---- (4) timers -----------------------------------------------------------------------------------------------------
        static void Timers(int seeds)
        {
            Console.WriteLine("== (4) timer blocks (BlockAutomation on). Sender PB acts at tick 120; 'catch' PB logs argument@tick:UpdateType ==");
            foreach (bool rev in new[] { false, true })
            {
                bool rv = rev;
                string ord = rv ? "catch PB runs before sender PB" : "sender PB runs before catch PB";
                Seeds(seeds, "PB -> Timer.Trigger() -> toolbar Run(catch)  [" + ord + "]", seed => TimerCase(seed, rv, "trigger", 1, false));
                Seeds(seeds, "PB -> Timer.Trigger() -> TriggerNow x3 chain -> Run(catch)  [" + ord + "]", seed => TimerCase(seed, rv, "trigger", 3, false));
                Seeds(seeds, "PB -> Timer.StartCountdown() (1 s) -> Start x4 chain (1 s each) -> Run(catch)  [" + ord + "]", seed => TimerCase(seed, rv, "start", 4, true));
            }
        }

        static string TimerCase(ulong seed, bool rev, string mode, int links, bool delayed)
        {
            int cat = rev ? 0 : 1, snd = rev ? 1 : 0;
            var c = Make2(seed, 1, 2, (cx, ids) =>
            {
                var st = new string[2];
                st[cat] = "catch;0;none;0";
                st[snd] = "trig;" + cx.Timers[0].EntityId + ";" + mode + ";0";
                return st;
            }, true);
            var tm = c.Timers;
            if (tm.Count < links) throw new InvalidOperationException("blueprint has only " + tm.Count + " timers");
            for (int k = 0; k < links; k++)
            {
                tm[k].TriggerDelay = 1;
                if (k < links - 1) tm[k].Inst.Toolbar = new[] { new ToolbarItem { Slot = 0, Kind = "Block", Action = delayed ? "Start" : "TriggerNow", BlockId = tm[k + 1].Inst.EntityId } };
                else tm[k].Inst.Toolbar = new[] { new ToolbarItem { Slot = 0, Kind = "Block", Action = "Run", BlockId = c.Pbs[cat].EntityId, Argument = "done" } };
            }
            Step(c, 700);
            int exp = 0; for (int k = 0; k < links; k++) exp += tm[k].Expiries;
            return "sender " + F<string>(c, snd, "Log").Trim() + " | catch PB: " + F<string>(c, cat, "Log").Trim() + " | timer expiries " + exp + Dead(c);
        }

        // ---- (5) TryRun -----------------------------------------------------------------------------------------------------
        static void TryRunSemantics(int seeds)
        {
            Console.WriteLine("== (5) TryRun semantics in the sim: M (PB0) calls W (PB1) and W2 (PB2) ==");
            Seeds(seeds, "TryRun cases", seed =>
            {
                var c = Make(seed, 1, 3, ids => new[] { "sem;" + ids[1] + ";u1;0;" + ids[2], "semW;" + ids[0] + ";none;0;" + ids[2], "semW;" + ids[0] + ";none;0" });
                Step(c, 400);
                return "M: " + F<string>(c, 0, "Log").Trim() + " | W saw: its TryRun(M)=" + F<int>(c, 1, "Bounce") + " (1=true), its TryRun(self)=" + F<int>(c, 1, "BounceSelf") + ", nested TryRun(W2)=" + F<int>(c, 1, "Nested") + " | W2 ran " + F<int>(c, 2, "Cnt") + "x | W: " + (c.Pbs[1].Runner.Dead ? "DEAD (" + c.Pbs[1].Runner.DeathReason + ")" : "alive") + ", M: " + (c.Pbs[0].Runner.Dead ? "DEAD" : "alive");
            });
        }
    }
}
