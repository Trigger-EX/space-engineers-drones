// SLOT D. IMyGridProgramRuntimeInfo for one PB. UpdateFrequency set by the script is read by ScriptHost at the next tick.
using System;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Run
{
    public sealed class SimRuntimeInfo : IMyGridProgramRuntimeInfo
    {
        public TimeSpan SinceLast;
        public SimSettings Settings;
        public SimRuntimeInfo(SimSettings s) { Settings = s; }
        public TimeSpan TimeSinceLastRun { get { return SinceLast; } }
        public double LastRunTimeMs { get { return Settings.FakeLastRunTimeMs; } }
        public UpdateFrequency UpdateFrequency { get; set; }
        public int CurrentInstructionCount { get { return Settings.FakeInstructionCount; } }
        public int MaxInstructionCount { get { return Settings.MaxInstructionCount; } }
        public int CurrentCallChainDepth { get { return 0; } }
        public int MaxCallChainDepth { get { return Settings.MaxCallChainDepth; } }
    }
}
