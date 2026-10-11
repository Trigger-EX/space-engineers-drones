// SLOT B. IMyRemoteControl / IMyShipController over the Body.
// GetShipVelocities: LinearVelocity = body COM velocity (world); AngularVelocity = body angular velocity (world, rad/s,
//   right-handed, which is what the game returns). CenterOfMass = world COM. CalculateShipMass: BaseMass = sum block mass,
//   TotalMass = PhysicalMass = BaseMass + inventory mass (none modelled). Gravity: zero (space). CanControlShip = IsWorking.
// DampenersOverride get/set = Body.DampenersOn (grid-wide, like the game). IsUnderControl = false (no player).
using System;
using VRageMath;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public class SimShipController : SimTerminalBlock, IMyShipController
    {
        public SimShipController(BlockInst inst, World world) : base(inst, world) { }
        public bool CanControlShip { get { return IsWorking; } }
        public bool IsUnderControl { get { return false; } }
        public bool DampenersOverride { get { return Inst.Body.DampenersOn; } set { Inst.Body.DampenersOn = value; } }
        public bool IsMainCockpit { get; set; }
        public bool ControlThrusters { get; set; }
        public Vector3D CenterOfMass { get { return Inst.Body.Position; } }
        public MyShipVelocities GetShipVelocities() { return new MyShipVelocities(Inst.Body.LinearVelocity, Inst.Body.AngularVelocity); }
        public double GetShipSpeed() { return Inst.Body.LinearVelocity.Length(); }
        public Vector3D GetNaturalGravity() { return Vector3D.Zero; }
        public Vector3D GetArtificialGravity() { return Vector3D.Zero; }
        public Vector3D GetTotalGravity() { return Vector3D.Zero; }
        public MyShipMass CalculateShipMass() { float m = (float)Inst.Body.Mass; return new MyShipMass(m, m, m); }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            AddBool(r, "DampenersOverride", b => ((SimShipController)b).DampenersOverride, (b, v) => ((SimShipController)b).DampenersOverride = v, true);
        }
    }

    /// <summary>Like the game's MyRemoteControl: a ship controller, NOT an IMyFunctionalBlock (no on/off).</summary>
    public sealed class SimRemoteControl : SimShipController, IMyRemoteControl
    {
        public SimRemoteControl(BlockInst inst, World world) : base(inst, world) { }
    }
}
