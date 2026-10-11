// SLOT B. IMyBatteryBlock over Core.BatteryUnit (units: MW, MWh like the game API).
using System;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimBattery : SimFunctionalBlock, IMyBatteryBlock
    {
        public BatteryUnit Unit { get { return Inst.Battery; } }
        public SimBattery(BlockInst inst, World world) : base(inst, world) { var u = inst.Battery; if (u != null) u.Enabled = EnabledValue; }
        protected override void OnEnabledChanged() { var u = Unit; if (u != null) u.Enabled = EnabledValue; }

        public float CurrentOutput { get { return (float)Unit.CurrentOutputMW; } }
        public float MaxOutput { get { return (float)Unit.MaxOutputMW; } }
        public float CurrentStoredPower { get { return (float)Unit.StoredMWh; } }
        public float MaxStoredPower { get { return (float)Unit.MaxStoredMWh; } }
        public float CurrentInput { get { return (float)Unit.CurrentInputMW; } }
        public float MaxInput { get { return (float)Unit.MaxInputMW; } }
        public bool IsCharging { get { return Unit.CurrentInputMW > Unit.CurrentOutputMW; } }
        public bool HasCapacityRemaining { get { return Unit.StoredMWh > 0; } }
        public ChargeMode ChargeMode { get { return Unit.Mode; } set { Unit.Mode = value; } }
    }
}
