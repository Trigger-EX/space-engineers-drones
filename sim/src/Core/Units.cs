// SLOT A. Physics/power-relevant block state. API classes (slot B) write the "command" fields, physics reads them.
using System;
using VRageMath;

namespace SeSim.Core
{
    public sealed class ThrusterUnit
    {
        public BlockInst Block;
        public Vector3D ForceDirLocal;     // unit, grid-local: the block's BACKWARD direction (thrust pushes the ship along it)
        public Vector3D PosLocal;          // block center, grid-local metres
        public float MaxForce;             // N, Def.ForceN (ion: 14400 small)
        public float MaxPowerMW, MinPowerMW;
        public bool Enabled = true;        // IMyFunctionalBlock.Enabled
        public float OverrideN;            // 0 = no override (IMyThrust.ThrustOverride, Newtons)
        // outputs (written by Physics each tick)
        public float CurrentForce;         // N actually applied after power ratio
        public float DesiredForce;         // N requested this tick (override or dampener), before the power ratio
        public int DirIndex;               // 0..5 = +X -X +Y -Y +Z -Z of ForceDirLocal (grid axes; thrusters are axis aligned)
        public bool Working { get { return Enabled && Block.IsFunctional; } }
    }

    public sealed class GyroUnit
    {
        public BlockInst Block;
        public MatrixD RotToGrid;          // block rotation (OrientMath.Rotation); TransformNormal(v, RotToGrid) = grid-local
        public float MaxForce;             // N*m, Def.GyroForce (448000 small)
        public bool Enabled = true;
        public bool Override;              // IMyGyro.GyroOverride
        public float Power = 1f;           // IMyGyro.GyroPower 0..1 (scales MaxForce)
        public float Pitch, Yaw, Roll;     // raw values as written by scripts (rad/s, clamped to +-max angular speed)
        public bool Working { get { return Enabled && Block.IsFunctional; } }

        /// <summary>Right-handed target angular velocity in GRID-local axes, applying the SimSettings sign convention.</summary>
        public Vector3D TargetGridLocal(SimSettings s)
        {
            var local = new Vector3D(s.GyroPitchSign * Pitch, s.GyroYawSign * Yaw, s.GyroRollSign * Roll);
            return Vector3D.TransformNormal(local, RotToGrid);
        }
    }

    public sealed class BatteryUnit
    {
        public BlockInst Block;
        public double StoredMWh, MaxStoredMWh, MaxOutputMW, MaxInputMW;
        public bool Enabled = true;
        public Sandbox.ModAPI.Ingame.ChargeMode Mode = Sandbox.ModAPI.Ingame.ChargeMode.Auto;
        public double CurrentOutputMW, CurrentInputMW;   // written by PowerSystem
        public bool CanDischarge { get { return Enabled && Block.IsFunctional && StoredMWh > 0 && Mode != Sandbox.ModAPI.Ingame.ChargeMode.Recharge; } }
    }

    /// <summary>A power consumer. Owners set RequestMW every tick before PowerSystem.Update; read SuppliedRatio after.</summary>
    public sealed class PowerSink
    {
        public BlockInst Block;
        public string Group;               // ResourceSinkGroup (Thrust, Gyro, Defense, DefenseAdative, Utility, Charging, BatteryBlock...)
        public int Priority;               // from defs_stats constants.PowerDistributionGroups (lower = served first)
        public double RequestMW;
        public double SuppliedRatio = 1;   // 0..1
        public bool Adaptible;             // filled by PowerSystem.Rebuild from DefDb.PowerAdaptible (partial supply allowed)
        public int Order;                  // stable tie-break assigned by PowerSystem.Rebuild
    }
}
