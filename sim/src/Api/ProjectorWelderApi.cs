// SLOT B. Minimal projector and welder (printers are a later phase): the projector reports its pre-loaded projection
// (BlockData.ProjectedGrids[0] block count) as TotalBlocks; RemainingBlocks counts down while at least one enabled working welder of
// the same body exists, at ApiTuning.PrintBlocksPerSecond (2 blocks/s [I]); IsProjecting = Enabled && has projection.
// Progress is advanced lazily when a property is read (assumes the welder state is constant between reads).
using System;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimProjector : SimFunctionalBlock, IMyProjector
    {
        public int Total, Remaining;
        double _printed; long _lastTick = -1;
        public SimProjector(BlockInst inst, World world) : base(inst, world)
        {
            if (inst.Src != null && inst.Src.ProjectedGrids.Count > 0) Total = inst.Src.ProjectedGrids[0].Blocks.Count;
            Remaining = Total;
        }

        bool WelderActive()
        {
            var blocks = Inst.Body.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                var w = blocks[i].Api as SimShipWelder;
                if (w != null && w.IsWorking) return true;
            }
            return false;
        }

        void Advance()
        {
            long t = World.Tick;
            if (_lastTick < 0) { _lastTick = t; return; }
            long dt = t - _lastTick; _lastTick = t;
            if (dt <= 0 || Remaining <= 0 || !Enabled || Inst.Destroyed) return;
            if (!WelderActive()) return;
            _printed += dt * SimSettings.Dt * ApiTuning.PrintBlocksPerSecond;
            int whole = (int)_printed; _printed -= whole;
            Remaining = Math.Max(0, Remaining - whole);
        }

        public bool IsProjecting { get { Advance(); return Enabled && Total > 0 && Remaining >= 0; } }
        public int RemainingBlocks { get { Advance(); return Remaining; } }
        public int TotalBlocks { get { return Total; } }
        public int BuildableBlocksCount { get { Advance(); return Remaining; } }
    }

    public sealed class SimShipWelder : SimFunctionalBlock, IMyShipWelder
    {
        public SimShipWelder(BlockInst inst, World world) : base(inst, world) { }
    }
}
