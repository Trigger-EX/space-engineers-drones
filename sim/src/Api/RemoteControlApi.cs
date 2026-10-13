// SLOT B. IMyRemoteControl / IMyShipController over the Body.
// GetShipVelocities: LinearVelocity = body COM velocity (world); AngularVelocity = body angular velocity (world, rad/s,
//   right-handed, which is what the game returns). CenterOfMass = world COM. CalculateShipMass: BaseMass = sum block mass,
//   TotalMass = PhysicalMass = BaseMass + inventory mass (none modelled). Gravity: zero (space). CanControlShip = IsWorking.
// DampenersOverride get/set = Body.DampenersOn (grid-wide, like the game).
// M0 (control_arch S2): IsUnderControl / MoveIndicator / RotationIndicator / RollIndicator come from the PilotInput the scenario's ScriptedPilot
// (or a test) fills; AttachToBody hangs it on the Body for Physics. A Remote Control is under control only when a test says so (K5 unmeasured).
// SimCockpit: a seat ship controller with N text surfaces (N = the blueprint's MySerializedTextPanelData count, else a per-subtype default).
using System;
using VRageMath;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public class SimShipController : SimTerminalBlock, IMyShipController
    {
        public SimShipController(BlockInst inst, World world) : base(inst, world) { }
        public readonly PilotInput Input = new PilotInput();
        public virtual bool CanControlShip { get { return IsWorking; } }
        public bool IsUnderControl { get { return Input.Active && Inst.IsFunctional; } }
        public Vector3 MoveIndicator { get { return IsUnderControl ? new Vector3(Input.Move.X, Input.Move.Y, Input.Move.Z) : Vector3.Zero; } }
        public Vector2 RotationIndicator { get { return IsUnderControl ? new Vector2((float)Input.Pitch, (float)Input.Yaw) : Vector2.Zero; } }
        public float RollIndicator { get { return IsUnderControl ? (float)Input.Roll : 0f; } }
        public bool HandBrake { get; set; }
        /// <summary>Somebody sits down (true) or leaves (false). Hangs / removes the input on the Body.</summary>
        public void SetUnderControl(bool on) { Input.Active = on; if (!on) Input.Clear(); AttachToBody(); }
        /// <summary>Re-attaches the input to the controller's current Body (a split can move the cockpit to another Body).</summary>
        Body _attached;
        public override void OnBodyChanged() { AttachToBody(); }
        public void AttachToBody()
        {
            Body b = Inst.Body;
            if (b == null) return;
            if (_attached != null && !ReferenceEquals(_attached, b) && ReferenceEquals(_attached.Pilot, Input)) _attached.Pilot = null;
            _attached = b;
            Input.CtlRot = OrientMath.Rotation(Inst.Orientation);
            if (IsUnderControl) b.Pilot = Input;
            else if (ReferenceEquals(b.Pilot, Input)) b.Pilot = null;
        }
        public bool DampenersOverride { get { return Inst.Body.DampenersOn; } set { Inst.Body.DampenersOn = value; } }
        public bool IsMainCockpit { get; set; }
        public bool ControlThrusters { get { return Input.ControlThrusters; } set { Input.ControlThrusters = value; } }
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

    /// <summary>Cockpit / flight seat: a ship controller with text surfaces. ControlThrusters is true (a seat drives the ship).</summary>
    public sealed class SimCockpit : SimShipController, IMyCockpit
    {
        public readonly SimTextSurface[] Surfaces;
        public SimCockpit(BlockInst inst, World world) : base(inst, world)
        {
            Input.ControlThrusters = true;
            int n = (int)(inst.Src != null ? inst.Src.PropNum("TextSurfaces", 0) : 0);
            if (n <= 0) n = DefaultSurfaces(inst.Def.Subtype);
            Surfaces = new SimTextSurface[n];
            for (int i = 0; i < n; i++) Surfaces[i] = new SimTextSurface();
        }
        /// <summary>Surfaces of the cockpits in the owner's samples (control_arch s2.1); 1 for the open ones and unknown subtypes.</summary>
        public static int DefaultSurfaces(string subtype)
        {
            switch (subtype)
            {
                case "DBSmallBlockFighterCockpit": case "LargeBlockCockpitSeat": return 6;
                case "OpenCockpitLarge": return 5;
                case "SmallBlockCockpit": return 4;
                default: return 1;
            }
        }
        /// <summary>Passenger seats / benches are cockpits that cannot fly the ship (recollection, medium confidence: control_arch s2.1, test K5).</summary>
        public override bool CanControlShip { get { string s = Inst.Def.Subtype ?? ""; return IsWorking && s.IndexOf("Passenger", StringComparison.Ordinal) < 0; } }
        public IMyTextSurface GetSurface(int index) { return index >= 0 && index < Surfaces.Length ? Surfaces[index] : null; }
        public int SurfaceCount { get { return Surfaces.Length; } }
    }

    /// <summary>Like the game's MyRemoteControl: a ship controller, NOT an IMyFunctionalBlock (no on/off).</summary>
    public sealed class SimRemoteControl : SimShipController, IMyRemoteControl
    {
        public SimRemoteControl(BlockInst inst, World world) : base(inst, world) { }
    }
}
