// M0 (docs/design/control_arch.md s6 S2/S4): the hand-flown input of a ship controller, read by Physics.
// A SimShipController owns one PilotInput and hangs it on its Body while someone "sits" in it (SimShipController.AttachToBody).
// Physics applies it block by block: a thruster/gyro with an override ignores the pilot (the override wins), every other
// working thruster/gyro follows it. Body.Pilot == null (every existing scenario) leaves the physics untouched.
using System;
using VRageMath;

namespace SeSim.Core
{
    /// <summary>Marks a SimSettings field added after the first result files: it is left out of Describe()/Hash() while it still has its
    /// default value, so the settings hash of every older run stays the same.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OptInSettingAttribute : Attribute { }

    public sealed class PilotInput
    {
        /// <summary>IsUnderControl: somebody sits in the controller (set by SimShipController.UnderControl / ScriptedPilot).</summary>
        public bool Active;
        /// <summary>IMyShipController.ControlThrusters: the controller drives the thrusters (the pilot's Move reaches them).</summary>
        public bool ControlThrusters = true;
        /// <summary>Controller-local move demand (game MoveIndicator: x right+, y up+, z backward+, W = -1). Clamped by Physics to +-1 per axis.</summary>
        public Vector3D Move;
        /// <summary>RotationIndicator.X (pitch, positive = nose down like a mouse pulled down), .Y (yaw, positive = turn right).</summary>
        public double Pitch, Yaw;
        /// <summary>RollIndicator (positive = roll right, the E key).</summary>
        public double Roll;
        /// <summary>Block rotation of the controller: TransformNormal(controller-local vector, CtlRot) = grid-local vector.</summary>
        public MatrixD CtlRot = MatrixD.Identity;

        public bool AnyMove { get { return Move.X != 0 || Move.Y != 0 || Move.Z != 0; } }
        public bool AnyRot { get { return Pitch != 0 || Yaw != 0 || Roll != 0; } }
        public void Clear() { Move = default(Vector3D); Pitch = Yaw = Roll = 0; }

        /// <summary>Move demand in GRID axes, each component clamped to +-clamp; zero when the controller does not drive the thrusters.</summary>
        public Vector3D MoveGrid(double clamp)
        {
            if (!Active || !ControlThrusters) return default(Vector3D);
            var m = new Vector3D(Cl(Move.X, clamp), Cl(Move.Y, clamp), Cl(Move.Z, clamp));
            return Vector3D.TransformNormal(m, CtlRot);
        }

        /// <summary>Pilot rotation as a target angular velocity in GRID axes (right-handed rad/s), or zero when idle. rate = gain * clamp(input),
        /// about the controller axes with sign 'sign' (all three, assumed -1: positive Pitch = nose down = rotation about -X, positive Yaw = turn
        /// right = about -Y, positive Roll = roll right = about -Z; measure with test K1). The magnitude is limited to maxW.</summary>
        public Vector3D RateGrid(double gain, double sign, double clamp, double maxW)
        {
            if (!Active || !AnyRot) return default(Vector3D);
            var l = new Vector3D(Cl(Pitch, clamp), Cl(Yaw, clamp), Cl(Roll, clamp)) * (gain * sign);
            Vector3D g = Vector3D.TransformNormal(l, CtlRot);
            double n = g.Length();
            if (n > maxW && n > 0) g *= maxW / n;
            return g;
        }

        static double Cl(double v, double c) { return double.IsNaN(v) ? 0 : (v < -c ? -c : (v > c ? c : v)); }
    }
}
