// SLOT B. IMyProgrammableBlock. Owns its text surfaces (2 on the small PB: large screen + keyboard; index 0 is the screen)
// and its SimIgc endpoint. Running the program is slot D's ScriptHost: this class only forwards.
// Actions: "Run" (argument run, UpdateType.Trigger), "RunWithDefaultArgument", "Recompile" (ScriptHost.Recompile: new
// Program instance, Storage kept, like the game), OnOff. TryRun(arg) = ScriptHost.RunArgument(this, arg, UpdateType.Script).
// IsRunning: ScriptHost should set `InMain` true around Main (request to slot D); it is false otherwise.
using System;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimProgrammableBlock : SimFunctionalBlock, IMyProgrammableBlock
    {
        public readonly SimTextSurface[] Surfaces;
        public SimIgc Igc;                       // created by BlockApiFactory, registered in World.Igc
        public string ProgramText;               // from blueprint <Program> (exact text)
        public string StorageText;               // from blueprint <Storage>
        public SeSim.Run.PbRunner Runner;        // set by ScriptHost.Attach
        public bool InMain;                      // set by ScriptHost while Main/constructor executes
        public SimProgrammableBlock(BlockInst inst, World world) : base(inst, world)
        {
            Surfaces = new SimTextSurface[] { new SimTextSurface(), new SimTextSurface() };
            ProgramText = inst.Src != null ? (inst.Src.Program ?? "") : "";
            StorageText = inst.Src != null ? (inst.Src.Storage ?? "") : "";
        }
        public IMyTextSurface GetSurface(int index) { return index >= 0 && index < Surfaces.Length ? Surfaces[index] : null; }
        public int SurfaceCount { get { return Surfaces.Length; } }
        public bool IsRunning { get { return InMain; } }

        public bool TryRun(string argument)
        {
            if (Inst.Destroyed || Runner == null || Runner.Dead || !IsWorking || InMain || World.Scripts == null) return false;
            World.Scripts.RunArgument(this, argument ?? "", UpdateType.Script);
            return true;
        }

        public override string DetailedInfo { get { return Runner != null ? (Runner.EchoText ?? "") : ""; } }

        public override void OnDestroyed()
        {
            if (World.Scripts != null) World.Scripts.Detach(this);
            if (Igc != null) Igc.Hub.Unregister(Igc);
        }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            r.Add(new SimAction("Run", b => { var p = (SimProgrammableBlock)b; if (p.World.Scripts != null && p.Runner != null && !p.Runner.Dead && p.IsWorking) p.World.Scripts.RunArgument(p, "", UpdateType.Trigger); }));
            r.Add(new SimAction("RunWithDefaultArgument", b => { var p = (SimProgrammableBlock)b; if (p.World.Scripts != null && p.Runner != null && !p.Runner.Dead && p.IsWorking) p.World.Scripts.RunArgument(p, "", UpdateType.Trigger); }));
            r.Add(new SimAction("Recompile", b => { var p = (SimProgrammableBlock)b; if (p.World.Scripts != null) p.World.Scripts.Recompile(p); }));
        }
    }
}
