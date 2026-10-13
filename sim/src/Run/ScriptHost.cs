// SLOT D. Runs PB programs exactly like the game schedules them (docs/sim-design.md section 3.3).
// Instantiation: FormatterServices.GetUninitializedObject(type) as IMyGridProgram; set Runtime, Storage, Me, Echo,
// GridTerminalSystem, IGC_ContextGetter through Sandbox.ModAPI.IMyGridProgram; DetRandomSeed.Set(Mix(worldSeed, pb EntityId));
// then invoke the parameterless constructor on that object. Constructor exception -> PB dead (EventKind.ScriptException).
// Scheduling per tick (World phase 1), PBs in a fixed order (attach order = ship index, then block index):
//   flags = Update1 if freq has Update1; Update10 if (tick + phase10) % 10 == 0; Update100 if (tick + phase100) % 100 == 0;
//   Once if requested last run (then cleared, before Main runs, like the game). One Main call with the OR of all due flags.
//   phase10 = EntityId-derived 0..9, phase100 0..99 (deterministic), mimicking the game's staggering.
// Pending argument runs (scenario commands, TryRun, ApplyAction("Run"), IGC callbacks) run first in the same tick, one Main call
// each, with their own UpdateType (Terminal / Trigger / Script / IGC). TimeSinceLastRun = sim time since this PB's previous Main
// call (zero on the first). Exceptions escaping Main stop the PB permanently (game: "script terminated") and are logged once.
// Measure real CPU per Main with Stopwatch for metrics only (never visible to scripts: LastRunTimeMs is fake-constant).
//
// Deterministic Random: every PB owns an RNG stream (PbRunner.RngState). Before each constructor/Main the thread-static
// SimShim.DetRandomSeed is loaded with it and afterwards stored back, so two PBs (or two worlds on one thread) never share a
// stream and `new Random()` inside Main returns a fresh seed each time, deterministically.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using Sandbox.ModAPI.Ingame;
using SimShim;
using SeSim.Core;
using SeSim.Api;

namespace SeSim.Run
{
    public sealed class PbRunner
    {
        public SimProgrammableBlock Pb;
        public Sandbox.ModAPI.IMyGridProgram Program;
        public SimRuntimeInfo Runtime;
        public SimGridTerminalSystem Gts;
        public string ScriptName;
        public int Phase10, Phase100;
        public double LastRunTime = -1;          // sim seconds of the previous Main call (-1 = never)
        public long LastRunTick = -1;
        public bool Dead; public string DeathReason;
        public readonly Queue<KeyValuePair<string, UpdateType>> PendingArgs = new Queue<KeyValuePair<string, UpdateType>>();
        public long Runs; public double CpuMs, MaxCpuMs;   // metrics only
        public string EchoText = "";
        public ulong RngState;
        public bool EchoSeen;
        public int ShipIndex = -1;
        public Action<string> EchoAction;
        public Func<IMyIntergridCommunicationSystem> IgcGetter;
        public int Recompiles;
        public IlStats Il;                       // SimSettings.CountInstructions only
    }

    public sealed class ScriptHost
    {
        public readonly World World;
        public readonly List<PbRunner> Runners = new List<PbRunner>();
        public readonly string SimDir;
        public ScriptHost(World w, string simDir) { World = w; SimDir = simDir; }

        const long TicksPer100ns = 10000000L;
        static long TickSpan(long ticks) { return (long)Math.Round(ticks * (double)TicksPer100ns / SimSettings.TicksPerSecond); }

        /// <summary>Called by BlockApiFactory for every PB with a non-empty program. Constructs the Program immediately.</summary>
        public PbRunner Attach(SimProgrammableBlock pb)
        {
            if (pb.Runner != null) return pb.Runner;
            var r = new PbRunner();
            r.Pb = pb;
            r.ShipIndex = pb.Inst.Body != null ? pb.Inst.Body.ShipIndex : -1;
            ulong id = (ulong)pb.Inst.EntityId;
            r.Phase10 = (int)(DetRng.Mix(id, 10) % 10UL);
            r.Phase100 = (int)(DetRng.Mix(id, 100) % 100UL);
            r.Runtime = new SimRuntimeInfo(World.Settings);
            r.Gts = new SimGridTerminalSystem(pb);
            r.RngState = DetRng.Mix(World.Seed, id);
            var runner = r;
            r.EchoAction = s =>
            {
                string t = s ?? "";
                if (!runner.EchoSeen) { runner.EchoText = t; runner.EchoSeen = true; }
                else runner.EchoText = runner.EchoText + "\n" + t;
            };
            r.IgcGetter = () => pb.Igc;
            try { r.ScriptName = ScriptRegistry.NameOf(pb.ProgramText); } catch { r.ScriptName = "?"; }
            pb.Runner = r;
            Runners.Add(r);
            Construct(r, pb.StorageText);
            try { r.ScriptName = ScriptRegistry.NameOf(pb.ProgramText); } catch { }
            return r;
        }

        void Construct(PbRunner r, string storage)
        {
            var pb = r.Pb;
            Type t;
            string cmode = World.Settings.CountInstructions ? World.Settings.InstructionCountMode : null;
            try { t = ScriptRegistry.GetProgramType(pb.ProgramText, SimDir, cmode); if (cmode != null && r.Il == null) r.Il = new IlStats(ScriptRegistry.SitesOf(pb.ProgramText, cmode)); }
            catch (Exception e) { Kill(r, e, "compile"); return; }
            Sandbox.ModAPI.IMyGridProgram gp;
            object o;
            try
            {
                o = FormatterServices.GetUninitializedObject(t);
                gp = (Sandbox.ModAPI.IMyGridProgram)o;
                r.EchoSeen = false;
                gp.Runtime = r.Runtime;
                gp.Storage = storage ?? "";
                gp.Me = pb;
                gp.Echo = r.EchoAction;
                gp.GridTerminalSystem = r.Gts;
                gp.IGC_ContextGetter = r.IgcGetter;
            }
            catch (Exception e) { Kill(r, e, "init"); return; }
            ConstructorInfo ctor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (ctor == null) { Kill(r, new MissingMethodException("Program has no parameterless constructor"), "init"); return; }
            DetRandomSeed.Set(r.RngState);
            pb.InMain = true;
            if (r.Il != null) r.Il.Begin(World.Settings.EnforceInstructionLimit ? World.Settings.MaxInstructionCount : 0);
            long t0 = Stopwatch.GetTimestamp();
            try { ctor.Invoke(o, null); }
            catch (TargetInvocationException e) { r.Program = gp; Kill(r, e.InnerException ?? e, "constructor"); return; }
            catch (Exception e) { r.Program = gp; Kill(r, e, "constructor"); return; }
            finally { pb.InMain = false; r.RngState = DetRandomSeed.Get(); AddCpu(r, t0, false); if (r.Il != null) r.Il.End(World.Tick, UpdateType.None, true, IlCounter.Dead); }
            r.Program = gp;
            r.Dead = false; r.DeathReason = null;
        }

        static void AddCpu(PbRunner r, long t0, bool countRun)
        {
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            r.CpuMs += ms;
            if (ms > r.MaxCpuMs) r.MaxCpuMs = ms;
            if (countRun) r.Runs++;
        }

        void Kill(PbRunner r, Exception e, string where)
        {
            r.Dead = true;
            string frame = "";
            string st = e.StackTrace;
            if (st != null)
            {
                int nl = st.IndexOf('\n');
                frame = (nl > 0 ? st.Substring(0, nl) : st).Trim();
            }
            r.DeathReason = where + ": " + e.GetType().Name + ": " + e.Message + (frame.Length > 0 ? " @ " + frame : "");
            var ev = new SimEvent { Kind = EventKind.ScriptException, Tick = World.Tick, ShipA = r.ShipIndex, ShipB = -1, Text = (r.ScriptName ?? "?") + " " + r.DeathReason };
            World.Events.On(ref ev);
        }

        /// <summary>PB.TryRun: runs Main now (nested inside the caller's Main), true if it ran. False when the PB is dead, off, destroyed or already
        /// running (no re-entrancy). The caller's RNG stream and instruction counter are saved and restored around the inner run.</summary>
        public bool RunNow(SimProgrammableBlock pb, string argument, UpdateType type)
        {
            var r = pb.Runner;
            if (r == null || r.Program == null || r.Dead || pb.InMain || pb.Inst.Destroyed || !pb.IsWorking) return false;
            ulong outerRng = DetRandomSeed.Get();
            int total = 0, limit = 0, depth = 0, maxDepth = 0; bool dead = false; int[] sites = null;
            if (r.Il != null) sites = IlCounter.Push(out total, out limit, out depth, out maxDepth, out dead);
            try { RunMain(r, argument ?? "", type); }
            finally { if (r.Il != null) IlCounter.Pop(sites, total, limit, depth, maxDepth, dead); DetRandomSeed.Set(outerRng); }
            return true;
        }

        void RunMain(PbRunner r, string argument, UpdateType type)
        {
            var pb = r.Pb;
            long tick = World.Tick;
            r.Runtime.SinceLast = r.LastRunTick < 0 ? TimeSpan.Zero : new TimeSpan(TickSpan(tick - r.LastRunTick));
            r.LastRunTick = tick; r.LastRunTime = tick * SimSettings.Dt;
            r.EchoSeen = false;
            DetRandomSeed.Set(r.RngState);
            pb.InMain = true;
            if (r.Il != null) r.Il.Begin(World.Settings.EnforceInstructionLimit ? World.Settings.MaxInstructionCount : 0);
            long t0 = Stopwatch.GetTimestamp();
            try { r.Program.Main(argument, type); }
            catch (Exception e) { Kill(r, e, "Main"); }
            finally { pb.InMain = false; r.RngState = DetRandomSeed.Get(); AddCpu(r, t0, true); if (r.Il != null) r.Il.End(tick, type, false, IlCounter.Dead); }
        }

        /// <summary>Phase 1 of World.Step.</summary>
        public void RunDue()
        {
            long tick = World.Tick;
            var runners = Runners;
            for (int i = 0; i < runners.Count; i++)
            {
                PbRunner r = runners[i];
                if (r.Dead || r.Program == null) continue;
                var pb = r.Pb;
                if (pb.Inst.Destroyed) { r.Dead = true; r.DeathReason = "block destroyed"; continue; }
                if (!pb.IsWorking) continue;                        // off, below critical integrity or unpowered: no run, flags stay
                while (r.PendingArgs.Count > 0 && !r.Dead)
                {
                    var a = r.PendingArgs.Dequeue();
                    RunMain(r, a.Key, a.Value);
                }
                if (r.Dead) continue;
                UpdateFrequency f = r.Runtime.UpdateFrequency;
                if (f == UpdateFrequency.None) continue;
                UpdateType ut = UpdateType.None;
                if ((f & UpdateFrequency.Update1) != 0) ut |= UpdateType.Update1;
                if ((f & UpdateFrequency.Update10) != 0 && (tick + r.Phase10) % 10 == 0) ut |= UpdateType.Update10;
                if ((f & UpdateFrequency.Update100) != 0 && (tick + r.Phase100) % 100 == 0) ut |= UpdateType.Update100;
                if ((f & UpdateFrequency.Once) != 0) { ut |= UpdateType.Once; r.Runtime.UpdateFrequency = f & ~UpdateFrequency.Once; }
                if (ut != UpdateType.None) RunMain(r, "", ut);
            }
        }

        /// <summary>Queues an argument run (executed at the start of the next phase 1; never re-entrant).</summary>
        public void RunArgument(SimProgrammableBlock pb, string argument, UpdateType type)
        {
            var r = pb.Runner;
            if (r == null || r.Dead) return;
            r.PendingArgs.Enqueue(new KeyValuePair<string, UpdateType>(argument ?? "", type));
        }

        /// <summary>New Program instance, Storage kept (the game discards state, keeps Storage).</summary>
        public void Recompile(SimProgrammableBlock pb)
        {
            var r = pb.Runner;
            if (r == null) return;
            string storage = r.Program != null ? r.Program.Storage : pb.StorageText;
            r.PendingArgs.Clear();
            r.Runtime.UpdateFrequency = UpdateFrequency.None;
            r.Recompiles++;
            r.Program = null;
            Construct(r, storage);
        }

        /// <summary>Scenario event (control_arch S8, the dead-man test): the PB stops running for good, like a crashed script; the overrides it set stay in
        /// the thrusters and gyros. No ScriptException is counted (it is the test, not a script bug).</summary>
        public void KillPb(SimProgrammableBlock pb, string reason)
        {
            var r = pb.Runner;
            if (r == null || r.Dead) return;
            r.Dead = true; r.DeathReason = reason ?? "killed";
            r.PendingArgs.Clear();
        }

        /// <summary>PB destroyed / disabled: no more runs.</summary>
        public void Detach(SimProgrammableBlock pb)
        {
            var r = pb.Runner;
            if (r == null) return;
            if (!r.Dead) { r.Dead = true; r.DeathReason = "detached"; }
        }
    }
}
