// SLOT B. The ONE place that builds MyDetectedEntityInfo (cameras, sensors, turrets, turret controllers all call it).
// EntityId = grid EntityId; Name = grid DisplayName when Relationship is Owner/FactionShare/Friends, else "Small Grid"/"Large Grid";
// Type = SmallGrid/LargeGrid; Orientation = body RotMatrix; Velocity = (Vector3)body.LinearVelocity; BoundingBox = world AABB;
// Relationship = TeamTable.Relation(observerOwner, body.OwnerId) where body.OwnerId becomes 0 (NoOwnership) once the body has
// no functional terminal block left (wreck); TimeStamp = World.TimeMs + 1 (never 0, because IsEmpty() tests TimeStamp == 0).
using System;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public static class Detection
    {
        /// <summary>Owner identity a detector sees: the body's team identity while it still has a functional terminal block, else 0.</summary>
        public static long EffectiveOwner(Body b)
        {
            if (b.OwnerId == 0) return 0;
            var blocks = b.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                BlockInst k = blocks[i];
                if (k.Api != null && k.IsFunctional) return b.OwnerId;
            }
            return 0;
        }

        public static MyDetectedEntityInfo FromBody(World w, Body target, long observerOwner, Vector3D? hitPosition)
        {
            long owner = EffectiveOwner(target);
            MyRelationsBetweenPlayerAndBlock rel = w.Teams.Relation(observerOwner, owner);
            bool small = target.GridSize == MyCubeSize.Small;
            string name;
            if (rel == MyRelationsBetweenPlayerAndBlock.Owner || rel == MyRelationsBetweenPlayerAndBlock.FactionShare || rel == MyRelationsBetweenPlayerAndBlock.Friends)
                name = target.DisplayName ?? (small ? "Small Grid" : "Large Grid");
            else name = small ? "Small Grid" : "Large Grid";
            Vector3D v = target.LinearVelocity;
            return new MyDetectedEntityInfo(
                target.EntityId, name, small ? MyDetectedEntityType.SmallGrid : MyDetectedEntityType.LargeGrid,
                hitPosition, target.RotMatrix, new Vector3((float)v.X, (float)v.Y, (float)v.Z), rel, target.WorldAabb(), w.TimeMs + 1);
        }

        public static MyDetectedEntityInfo FromRayHit(World w, ref RayHit hit, long observerOwner)
        {
            return FromBody(w, hit.Body, observerOwner, hit.Point);
        }
    }
}
