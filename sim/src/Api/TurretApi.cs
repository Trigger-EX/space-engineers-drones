// SLOT B. Large turrets (pirate ship) and Custom Turret Controllers (drones). API surface only; targeting, aiming and
// firing are Combat.TurretUnit (slot C). HasTarget/GetTargetedEntity read TurretUnit.Target (a MyDetectedEntityInfo built by
// Detection.FromBody at the last refresh, EntityId of the target GRID). A target whose body is no longer alive does not count.
using System;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using SeSim.Core;
using SeSim.Combat;

namespace SeSim.Api
{
    public abstract class SimLargeTurret : SimFunctionalBlock, IMyLargeConveyorTurretBase
    {
        public TurretUnit Unit { get { return Inst.Turret; } }
        protected SimLargeTurret(BlockInst inst, World world) : base(inst, world) { }
        public bool IsShooting { get { var u = Unit; return u != null && u.Weapon != null && u.Weapon.IsShooting; } }
        /// <summary>Turrets fire on their own AI in the sim (no Shoot_On/ShootOnce action exists for them): Shoot reads the weapon's
        /// state, the setter and ShootOnce() are accepted and ignored, exactly like ApplyAction("Shoot_On") on a turret.</summary>
        public bool Shoot { get { return IsShooting; } set { } }
        public void ShootOnce() { }
        public bool HasTarget { get { var u = Unit; return u != null && IsWorking && u.TargetBody != null && u.TargetBody.Alive; } }
        public MyDetectedEntityInfo GetTargetedEntity() { return HasTarget ? Unit.Target : default(MyDetectedEntityInfo); }
        public bool IsAimed { get { var u = Unit; return u != null && u.IsAimed; } }
        public float Range
        {
            get { var u = Unit; return u != null ? u.Range : 0f; }
            set { var u = Unit; if (u != null) u.Range = float.IsNaN(value) || value < 0f ? 0f : value; }
        }
        public bool AIEnabled { get { return true; } }
        /// <summary>Game convention (VRageMath CreateFromAzimuthAndElevation): local aim = (-sin az cos el, sin el, -cos az cos el);
        /// TurretUnit keeps az with the opposite sign, so it is negated here.</summary>
        public float Azimuth { get { var u = Unit; return u != null ? (float)(-u.Azimuth) : 0f; } }
        public float Elevation { get { var u = Unit; return u != null ? (float)u.Elevation : 0f; } }
        public string GetTargetingGroup() { var u = Unit; return u != null ? u.TargetingGroup : ""; }
        public void SetTargetingGroup(string groupSubtypeId) { var u = Unit; if (u != null) u.TargetingGroup = groupSubtypeId ?? ""; }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            AddFloat(r, "Range", b => ((SimLargeTurret)b).Range, (b, v) => ((SimLargeTurret)b).Range = v, 800f, 0f, 800f);
        }
    }
    public sealed class SimLargeGatlingTurret : SimLargeTurret, IMyLargeGatlingTurret { public SimLargeGatlingTurret(BlockInst i, World w) : base(i, w) { } }
    public sealed class SimLargeMissileTurret : SimLargeTurret, IMyLargeMissileTurret { public SimLargeMissileTurret(BlockInst i, World w) : base(i, w) { } }

    /// <summary>Custom Turret Controller without rotors (the drones' use): a fixed-direction designator. Its AI picks the nearest
    /// enemy in Range (Def.TurretControlRange, 600 m) with line of sight from the block; it does not rotate or fire anything.</summary>
    public sealed class SimTurretControlBlock : SimFunctionalBlock, IMyTurretControlBlock
    {
        public TurretUnit Unit { get { return Inst.Turret; } }
        public SimTurretControlBlock(BlockInst inst, World world) : base(inst, world) { }
        public bool HasTarget { get { var u = Unit; return u != null && IsWorking && u.TargetBody != null && u.TargetBody.Alive; } }
        public MyDetectedEntityInfo GetTargetedEntity() { return HasTarget ? Unit.Target : default(MyDetectedEntityInfo); }
        public bool IsAimed { get { var u = Unit; return u != null && u.IsAimed; } }
        public float Range
        {
            get { var u = Unit; return u != null ? u.Range : 0f; }
            set { var u = Unit; if (u != null) u.Range = float.IsNaN(value) || value < 0f ? 0f : value; }
        }
        public bool AIEnabled { get { return true; } }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            AddFloat(r, "Range", b => ((SimTurretControlBlock)b).Range, (b, v) => ((SimTurretControlBlock)b).Range = v, 600f, 0f, 800f);
        }
    }
}
