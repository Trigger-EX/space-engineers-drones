// SLOT B. IMyGyro over Core.GyroUnit. Pitch/Yaw/Roll are stored raw (rad/s), clamped to +-SimSettings max angular speed;
// the sign convention is applied ONLY in GyroUnit.TargetGridLocal (slot A), never here.
// Getters return the stored raw value (round trip), so scripts that read back what they wrote see their own numbers.
using System;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimGyro : SimFunctionalBlock, IMyGyro
    {
        public GyroUnit Unit { get { return Inst.Gyro; } }
        public SimGyro(BlockInst inst, World world) : base(inst, world) { var u = inst.Gyro; if (u != null) u.Enabled = EnabledValue; }

        protected override void OnEnabledChanged() { var u = Unit; if (u != null) u.Enabled = EnabledValue; Inst.Body.GyroDirty = true; }

        float MaxRate()
        {
            return (float)(Inst.Body.GridSize == VRage.Game.MyCubeSize.Large ? World.Settings.LargeMaxAngularSpeed : World.Settings.SmallMaxAngularSpeed);
        }
        float Clamp(float v)
        {
            if (float.IsNaN(v)) return 0f;
            float m = MaxRate();
            return v < -m ? -m : (v > m ? m : v);
        }

        public bool GyroOverride { get { return Unit.Override; } set { Unit.Override = value; Inst.Body.GyroDirty = true; } }
        public float GyroPower
        {
            get { return Unit.Power; }
            set { float v = float.IsNaN(value) ? 0f : value; Unit.Power = v < 0f ? 0f : (v > 1f ? 1f : v); Inst.Body.GyroDirty = true; }
        }
        public float Pitch { get { return Unit.Pitch; } set { Unit.Pitch = Clamp(value); Inst.Body.GyroDirty = true; } }
        public float Yaw { get { return Unit.Yaw; } set { Unit.Yaw = Clamp(value); Inst.Body.GyroDirty = true; } }
        public float Roll { get { return Unit.Roll; } set { Unit.Roll = Clamp(value); Inst.Body.GyroDirty = true; } }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            const float big = 2 * (float)Math.PI;
            AddBool(r, "Override", b => ((SimGyro)b).GyroOverride, (b, v) => ((SimGyro)b).GyroOverride = v, false);
            AddFloat(r, "Power", b => ((SimGyro)b).GyroPower, (b, v) => ((SimGyro)b).GyroPower = v, 1f, 0f, 1f);
            AddFloat(r, "Yaw", b => ((SimGyro)b).Yaw, (b, v) => ((SimGyro)b).Yaw = v, 0f, -big, big);
            AddFloat(r, "Pitch", b => ((SimGyro)b).Pitch, (b, v) => ((SimGyro)b).Pitch = v, 0f, -big, big);
            AddFloat(r, "Roll", b => ((SimGyro)b).Roll, (b, v) => ((SimGyro)b).Roll = v, 0f, -big, big);
        }
    }
}
