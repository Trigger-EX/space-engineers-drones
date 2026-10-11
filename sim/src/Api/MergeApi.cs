// SLOT B. IMyShipMergeBlock over Core.MergeUnit. Enabled=false on either side breaks the pair (Partner=null on both),
// marks the Body ConnectivityDirty (slot A splits it at end of tick) and emits EventKind.MergeReleased.
// Re-locking (Enabled=true with faces within 0.1 m and aligned) is slot A's MergeUnit logic (printers, later).
using System;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimMergeBlock : SimFunctionalBlock, IMyShipMergeBlock
    {
        public MergeUnit Unit { get { return Inst.Merge; } }
        public SimMergeBlock(BlockInst inst, World world) : base(inst, world) { }
        public bool IsConnected { get { var u = Unit; return u != null && u.IsConnected; } }

        public override bool Enabled
        {
            get { return EnabledValue; }
            set
            {
                if (EnabledValue == value) return;
                EnabledValue = value;
                if (!value) Release();
            }
        }

        /// <summary>Breaks the lock (both sides), flags the body for splitting, emits MergeReleased.</summary>
        public void Release()
        {
            var u = Unit;
            if (u == null || u.Partner == null) return;
            MergeUnit p = u.Partner;
            u.Partner = null;
            if (p.Partner == u) p.Partner = null;
            Inst.Body.ConnectivityDirty = true;
            if (p.Block != null && p.Block.Body != null) p.Block.Body.ConnectivityDirty = true;
            var e = new SimEvent();
            e.Kind = EventKind.MergeReleased; e.Tick = World.Tick; e.ShipA = Inst.Body.ShipIndex; e.ShipB = p.Block != null && p.Block.Body != null ? p.Block.Body.ShipIndex : -1;
            e.Value = 1; e.Position = Inst.WorldCenter;
            World.Events.On(ref e);
        }

        public override void OnDestroyed() { Release(); }
    }
}
