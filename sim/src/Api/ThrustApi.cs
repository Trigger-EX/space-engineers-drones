// SLOT B. IMyThrust over Core.ThrusterUnit.
// ThrustOverride is Newtons (clamped 0..MaxThrust); ThrustOverridePercentage = ThrustOverride / MaxThrust (0..1).
// MaxEffectiveThrust = MaxThrust in space (ion effectiveness 1 in vacuum). CurrentThrust = ThrusterUnit.CurrentForce.
// CurrentThrustPercentage = CurrentThrust / MaxThrust * 100 (game: MyThrust.CurrentStrength * 100 [I, from memory of Keen source]).
// GridThrustDirection = direction the thruster pushes the ship, grid integer vector = block Backward (MDK: "direction of thrust").
using System;
using VRageMath;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimThrust : SimFunctionalBlock, IMyThrust
    {
        public ThrusterUnit Unit { get { return Inst.Thruster; } }
        public SimThrust(BlockInst inst, World world) : base(inst, world) { var u = inst.Thruster; if (u != null) u.Enabled = EnabledValue; }

        protected override void OnEnabledChanged() { var u = Unit; if (u != null) u.Enabled = EnabledValue; }

        public float ThrustOverride
        {
            get { return Unit.OverrideN; }
            set
            {
                var u = Unit;
                float v = float.IsNaN(value) ? 0f : value;
                u.OverrideN = v < 0f ? 0f : (v > u.MaxForce ? u.MaxForce : v);
            }
        }

        public float ThrustOverridePercentage
        {
            get { var u = Unit; return u.MaxForce > 0f ? u.OverrideN / u.MaxForce : 0f; }
            set
            {
                var u = Unit;
                float v = float.IsNaN(value) ? 0f : value;
                u.OverrideN = (v < 0f ? 0f : (v > 1f ? 1f : v)) * u.MaxForce;
            }
        }

        public float MaxThrust { get { return Unit.MaxForce; } }
        public float MaxEffectiveThrust { get { return Unit.MaxForce; } }
        public float CurrentThrust { get { return Unit.CurrentForce; } }
        public float CurrentThrustPercentage { get { var u = Unit; return u.MaxForce > 0f ? u.CurrentForce / u.MaxForce * 100f : 0f; } }

        public Vector3I GridThrustDirection
        {
            get
            {
                var o = Inst.Orientation;
                return Base6Directions.GetIntVector(o.TransformDirection(Base6Directions.Direction.Backward));
            }
        }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            // property "Override" is in Newtons (terminal slider 0..MaxThrust; the GUI prints it as percent)
            r.Add(new SimProperty<float>("Override", b => ((SimThrust)b).ThrustOverride, (b, v) => ((SimThrust)b).ThrustOverride = v,
                0f, 0f, b => ((SimThrust)b).MaxThrust));
        }
    }
}
