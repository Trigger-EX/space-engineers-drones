// M6: a body that is bolted to another body of the same ship. The sim fuses merge-linked grids into one Body (GridFuse) but not connector-linked ones
// (the Warden / Leviathan pods hang on their hangar connectors as a separate small-grid Body: another size class cannot share a lattice). Left alone such a pod would
// drift away from the moving ship, and the bay PB on it would measure its own tumble instead of the ship's. RigidLink keeps the pod in the pose it was spawned in,
// relative to the host: every tick (before the physics) it copies position, rotation and the rigid velocity field of the host onto the pod.
// Warship scenarios only (Scenario.RigidSubgrids). Limits: the pod adds no mass or inertia to the host (about 10 t of 2,563 t on the Warden), the pod's own power / thrust
// is ignored, the link never breaks (a real connector can be shot off or unlocked).
using System;
using VRageMath;
using SeSim.Core;

namespace SeSim.Run
{
    public sealed class RigidLink : IShipBrain
    {
        readonly Body _child, _parent; bool _init; MatrixD _relRot; Vector3D _relPos;
        public RigidLink(Body child, Body parent) { _child = child; _parent = parent; }

        public void Tick(World w)
        {
            if (!_child.Alive || !_parent.Alive) return;
            if (!_init)
            {
                _relRot = _child.RotMatrix * MatrixD.Transpose(_parent.RotMatrix);                         // row-vector convention: child = rel * parent
                _relPos = Vector3D.TransformNormal(_child.Position - _parent.Position, _parent.InvWorldRot);
                _init = true; return;
            }
            MatrixD rot = _relRot * _parent.RotMatrix;
            _child.Rotation = QuaternionD.CreateFromRotationMatrix(rot); _child.Rotation.Normalize();
            _child.Position = _parent.Position + Vector3D.TransformNormal(_relPos, _parent.RotMatrix);
            _child.AngularVelocity = _parent.AngularVelocity;
            _child.LinearVelocity = _parent.VelocityAtWorld(_child.Position);
            _child.UpdateWorldMatrix();
        }
    }
}
