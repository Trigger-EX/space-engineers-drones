// SLOT C. Turret AI for large turrets (pirate ship) and target designation for Custom Turret Controllers (drones).
// Target refresh every TurretTargetRefreshTicks (staggered by EntityId): nearest Enemies-relation body with a functional terminal
// block within Range (distance to the target's bounding sphere), line of sight checked with Raycast.RayWorld from the turret to the
// target's bound centre (the first thing hit must be the target or another enemy). Large turrets: azimuth/elevation slew at
// SimSettings.TurretSlewRadPerSec [I] (azimuth unlimited, elevation >= TurretMinElevationDeg), lead = first-order intercept with the
// projectile speed (bullets: relative to the shooter's velocity; rockets: absolute), fire when the aim error is below
// TurretFireConeDeg through their WeaponUnit. Turret controller: designation only (no weapon, no rotation, IsAimed stays false).
// The published Target (MyDetectedEntityInfo via Detection.FromBody, HitPosition null like the game) is rebuilt every tick while a
// target is held, so Position/Velocity are never older than one tick; target SELECTION only changes at refresh ticks.
using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using SeSim.Core;
using SeSim.Api;

namespace SeSim.Combat
{
    public sealed class TurretUnit
    {
        public BlockInst Block;
        public bool IsController;             // TurretControlBlock: designator only
        public WeaponUnit Weapon;             // large turret's gun (null for controllers)
        public float Range;
        public Body TargetBody;               // null = no target
        public MyDetectedEntityInfo Target;   // Detection.FromBody at refresh time
        public double Azimuth, Elevation;     // radians, in block frame
        public bool IsAimed;
        /// <summary>Targeting group ("" = the target's centre; "PowerSystems", "Weapons", "Propulsion"): with
        /// SimSettings.TurretSubsystemTargeting the turret aims at the nearest functional block of that group on its target.</summary>
        public string TargetingGroup = "";
        BlockInst _sub; long _subTick = -1;

        // slot C internals
        public Vector3D AimDirWorld;
        MatrixD _rot, _rotInv;                // block rotation in grid axes and its transpose

        public TurretUnit(BlockInst block, WeaponUnit weapon, float range)
        {
            Block = block; Weapon = weapon; Range = range; IsController = weapon == null;
            _rot = OrientMath.Rotation(block.Orientation);
            _rotInv = MatrixD.Transpose(_rot);
        }

        public void Update(World w, double dt, bool refresh)
        {
            var b = Block; var body = b.Body;
            if (b.Destroyed || body == null) return;
            bool working = b.IsFunctional && CombatUtil.BlockEnabled(b) && (b.Api == null || b.Api.IsWorking);
            if (!working) { Drop(); return; }
            Vector3D muz = body.LocalToWorld(b.LocalCenter);
            if (refresh) Acquire(w, body, muz);
            else if (TargetBody != null && (!TargetBody.Alive || TargetBody.Blocks.Count == 0)) Drop();
            if (TargetBody == null) { IsAimed = false; if (Weapon != null) Weapon.ShootOn = false; return; }

            Target = Detection.FromBody(w, TargetBody, b.OwnerId, null);
            if (IsController) { IsAimed = false; return; }

            // ---- large turret: lead, slew, fire ----
            var s = w.Settings;
            var wp = Weapon;
            Vector3D aimPoint = Decoys.AimPoint(w, TargetBody, muz, b.EntityId);   // nearest working decoy, else the body centre
            if (TargetingGroup.Length > 0 && s.TurretSubsystemTargeting && aimPoint == TargetBody.Position)   // a decoy still wins
            {
                if (refresh || _sub == null || _sub.Destroyed || !_sub.IsFunctional || !ReferenceEquals(_sub.Body, TargetBody)) _sub = Subsystem(TargetBody, muz);
                if (_sub != null) aimPoint = TargetBody.LocalToWorld(_sub.LocalCenter);
            }
            Vector3D rel = aimPoint - muz;
            double dist = rel.Length();
            Vector3D tv = TargetBody.LinearVelocity;
            double speed = wp.Mode == ProjectileSystem.ModeRocket ? 0.5 * (wp.Ammo.MissileInitialSpeed + wp.Ammo.Speed) : (wp.Ammo.Speed > 0 ? wp.Ammo.Speed : wp.Ammo.MissileInitialSpeed);
            Vector3D relVel = wp.Mode == ProjectileSystem.ModeRocket ? tv : tv - body.VelocityAtWorld(muz);
            double tI;
            Vector3D want = rel;
            if (CombatUtil.Intercept(rel, relVel, speed, out tI)) want = rel + relVel * tI;
            double wl = want.Length();
            if (wl < 1e-6) return;
            want = want * (1.0 / wl);

            // desired azimuth/elevation in the block frame
            Vector3D g = body.DirToLocal(want);
            Vector3D l = Vector3D.TransformNormal(g, _rotInv);          // block-local: X right, Y up, Z backward
            double azD = Math.Atan2(l.X, -l.Z);
            double yy = l.Y < -1 ? -1 : (l.Y > 1 ? 1 : l.Y);
            double elD = Math.Asin(yy);
            double minEl = s.TurretMinElevationDeg * Math.PI / 180.0;
            if (elD < minEl) elD = minEl;
            double step = s.TurretSlewRadPerSec * dt;
            double dAz = azD - Azimuth;
            while (dAz > Math.PI) dAz -= 2 * Math.PI;
            while (dAz < -Math.PI) dAz += 2 * Math.PI;
            Azimuth += dAz > step ? step : (dAz < -step ? -step : dAz);
            double dEl = elD - Elevation;
            Elevation += dEl > step ? step : (dEl < -step ? -step : dEl);
            double ce = Math.Cos(Elevation);
            var aimLocal = new Vector3D(Math.Sin(Azimuth) * ce, Math.Sin(Elevation), -Math.Cos(Azimuth) * ce);
            Vector3D ag = Vector3D.TransformNormal(aimLocal, _rot);
            AimDirWorld = body.DirToWorld(ag);
            wp.UseAimDir = true; wp.AimDirWorld = AimDirWorld;

            double cosErr = AimDirWorld.X * want.X + AimDirWorld.Y * want.Y + AimDirWorld.Z * want.Z;
            double cone = Math.Cos(s.TurretFireConeDeg * Math.PI / 180.0);
            IsAimed = cosErr >= cone;
            double rangeToHull = dist - TargetBody.BoundRadius;
            wp.ShootOn = IsAimed && rangeToHull <= Range;
        }

        /// <summary>Block types of the vanilla targeting groups (TargetGroups.sbc, approximated by block kind).</summary>
        public static bool InGroup(BlockInst x, string group)
        {
            string k = x.Def.Kind;
            switch (group)
            {
                case "PowerSystems": return k == "Reactor" || k == "BatteryBlock" || k == "HydrogenEngine" || k == "SolarPanel" || k == "WindTurbine";
                case "Propulsion": return k == "Thrust";
                case "Weapons": return x.Def.Weapon != null || k == "LargeTurretBase" || k == "WeaponBlock";
                default: return false;
            }
        }

        BlockInst Subsystem(Body t, Vector3D from)
        {
            BlockInst best = null; double bd = double.MaxValue;
            var bl = t.Blocks;
            for (int i = 0; i < bl.Count; i++)
            {
                var x = bl[i];
                if (x.Destroyed || !x.IsFunctional || !InGroup(x, TargetingGroup)) continue;
                double d = (t.LocalToWorld(x.LocalCenter) - from).LengthSquared();
                if (d < bd) { bd = d; best = x; }
            }
            return best;
        }

        void Drop()
        {
            TargetBody = null; IsAimed = false; Target = default(MyDetectedEntityInfo);
            if (Weapon != null) Weapon.ShootOn = false;
        }

        void Acquire(World w, Body body, Vector3D from)
        {
            Body best = null; double bestD = double.MaxValue; bool bestHas = false;
            bool prefer = w.Settings.DecoysFunctional && w.Settings.DecoyPreferTarget;
            var bodies = w.Bodies;
            long owner = Block.OwnerId;
            for (int i = 0; i < bodies.Count; i++)
            {
                var o = bodies[i];
                if (!o.Alive || o.Blocks.Count == 0 || ReferenceEquals(o, body)) continue;
                long eo = Detection.EffectiveOwner(o);
                if (eo == 0) continue;
                if (w.Teams.Relation(owner, eo) != MyRelationsBetweenPlayerAndBlock.Enemies) continue;
                double dx = o.WorldBoundCenter.X - from.X, dy = o.WorldBoundCenter.Y - from.Y, dz = o.WorldBoundCenter.Z - from.Z;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d - o.BoundRadius > Range) continue;
                bool has = prefer && Decoys.HasWorking(w, o);
                if (bestHas && !has) continue;                       // a body with a working decoy beats any body without
                if (has == bestHas && d >= bestD) continue;
                // line of sight: first block hit must belong to an enemy body
                if (d > 1e-6)
                {
                    var dir = new Vector3D(dx / d, dy / d, dz / d);
                    RayHit h;
                    if (Raycast.RayWorld(w, from, dir, d, body, out h) && !ReferenceEquals(h.Body, o))
                    {
                        long ho = Detection.EffectiveOwner(h.Body);
                        if (ho == 0 || w.Teams.Relation(owner, ho) != MyRelationsBetweenPlayerAndBlock.Enemies) continue;
                    }
                }
                best = o; bestD = d; bestHas = has;
            }
            if (!ReferenceEquals(best, TargetBody))
            {
                TargetBody = best;
                if (best == null) Drop();
            }
        }
    }
}
