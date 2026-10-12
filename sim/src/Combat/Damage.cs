// SLOT C. Block damage. Deformation off. Damage numbers: Ammo ProjectileMassDamage per hit (gatling 90, autocannon 500);
// railgun: the slug walks the blocks on its path, each block takes min(pool, block Hp) and the pool shrinks by that amount
// (MissileHealthPool 8000, [I] semantics, docs/stats.md); rockets: Explosions.Explode(radius 4, damage 500). A block reaching
// Hp <= 0 -> Body.RemoveBlock (slot A) which emits BlockDestroyed and marks mass/connectivity dirty.
//
// Events (see sim-design.md section 9): ONE EventKind.Hit per projectile impact on a victim body (ShipA attacker, ShipB victim,
// Value = damage absorbed, overkill not counted). A bullet is one Hit; a rocket/warhead blast is one Hit per body it damaged with the
// summed damage; a railgun slug is one Hit per body it passed through. So hits/shots stays a hit rate (<= 1 per body). A hit on a body whose relation to the attacker is Owner/FactionShare/Friends emits ONLY
// EventKind.FriendlyFire (same fields) and no Hit; own-ship hits (warhead blast on the carrier, ShipA == ShipB) are FriendlyFire
// too, so slot D should skip ShipA == ShipB when it computes "friendly damage". With SimSettings.FriendlyFire == false such
// hits do nothing at all.
// An armed warhead whose block is destroyed is queued to detonate (processed by CombatSystem.UpdateWarheads, at most one tick later).
using System;
using VRageMath;
using VRage.Game;
using SeSim.Core;
using SeSim.Blueprint;

namespace SeSim.Combat
{
    public enum DamageKind { Bullet, Railgun, Explosion, Collision }

    public static class Damage
    {
        // result of the last slug block hit (ApplyProjectile does not emit for slugs; ProjectileSystem sums per body)
        [ThreadStatic] public static Body LastSlugBody;
        [ThreadStatic] public static float LastSlugDamage;

        /// <summary>Returns damage actually absorbed (<= amount).</summary>
        public static float ApplyToBlock(World w, BlockInst b, float amount, int attackerShip, DamageKind kind, Vector3D at)
        {
            return ApplyCore(w, b, amount, attackerShip, OwnerOfShip(w, attackerShip), kind, at);
        }

        /// <summary>Projectile hit resolution; returns true if the projectile is consumed.</summary>
        public static bool ApplyProjectile(World w, ref RayHit hit, AmmoDef ammo, ref float healthPool, int attackerShip)
        {
            return ApplyProjectile(w, ref hit, ammo, ref healthPool, attackerShip, OwnerOfShip(w, attackerShip));
        }

        /// <summary>Same with the attacker's owner identity known (ProjectileSystem passes the shooter body's owner).
        /// Bullet: full MassDamage to the block hit, consumed. Rocket (Missile with explosion radius): explodes at the hit point,
        /// consumed. Slug (Missile with health pool, no explosion): the block absorbs min(pool, Hp); consumed when the pool is empty
        /// or the block survived, otherwise the slug continues (returns false; the caller re-casts the same segment).</summary>
        public static bool ApplyProjectile(World w, ref RayHit hit, AmmoDef ammo, ref float healthPool, int attackerShip, long attackerOwner)
        {
            if (!ammo.IsMissile)
            {
                ApplyCore(w, hit.Block, ammo.MassDamage, attackerShip, attackerOwner, DamageKind.Bullet, hit.Point);
                return true;
            }
            if (ammo.MissileExplosionRadius > 0 && ammo.MissileExplosionDamage > 0)
            {
                Explosions.Explode(w, hit.Point, ammo.MissileExplosionRadius, ammo.MissileExplosionDamage, attackerShip, attackerOwner);
                return true;
            }
            // penetrating slug
            BlockInst blk = hit.Block;
            if (blk.Destroyed) return false;
            float hp = blk.Hp;
            float want = hp < healthPool ? hp : healthPool;
            float got = ApplyCore(w, blk, want, attackerShip, attackerOwner, DamageKind.Railgun, hit.Point, false);
            LastSlugBody = blk.Body; LastSlugDamage = got;
            if (got <= 0f) return true;                                             // filtered (friendly fire off): stop, never loop
            healthPool -= got;
            if (healthPool <= 1e-3f) { healthPool = 0; return true; }
            return !blk.Destroyed;                                                  // survived with pool left cannot happen: stop anyway
        }

        public static bool IsFriendly(World w, long attackerOwner, Body victim)
        {
            if (attackerOwner == 0 || victim == null) return false;
            var rel = w.Teams.Relation(attackerOwner, victim.OwnerId);
            return rel == MyRelationsBetweenPlayerAndBlock.Owner || rel == MyRelationsBetweenPlayerAndBlock.FactionShare
                || rel == MyRelationsBetweenPlayerAndBlock.Friends;
        }

        /// <summary>Owner identity of the ship with this ShipInstance index (first live body found), 0 when unknown.</summary>
        public static long OwnerOfShip(World w, int ship)
        {
            if (ship < 0) return 0;
            var bodies = w.Bodies;
            for (int i = 0; i < bodies.Count; i++) if (bodies[i].ShipIndex == ship) return bodies[i].OwnerId;
            return 0;
        }

        public static void EmitHit(World w, int attackerShip, bool friendly, int victimShip, double damage, Vector3D at)
        {
            var e = new SimEvent { Kind = friendly ? EventKind.FriendlyFire : EventKind.Hit, Tick = w.Tick, ShipA = attackerShip, ShipB = victimShip, Value = damage, Position = at, Id = w.CurrentShotId };
            w.Events.On(ref e);
        }

        /// <summary>The one place block Hp is reduced. Returns the damage absorbed.</summary>
        public static float ApplyCore(World w, BlockInst b, float amount, int attackerShip, long attackerOwner, DamageKind kind, Vector3D at)
        {
            return ApplyCore(w, b, amount, attackerShip, attackerOwner, kind, at, true);
        }

        /// <summary>emit=false: no Hit/FriendlyFire event (the caller sums damage per body and calls EmitHit).</summary>
        public static float ApplyCore(World w, BlockInst b, float amount, int attackerShip, long attackerOwner, DamageKind kind, Vector3D at, bool emit)
        {
            if (b == null || b.Destroyed || amount <= 0f) return 0f;
            Body body = b.Body;
            bool friendly = IsFriendly(w, attackerOwner, body);
            if (friendly && !w.Settings.FriendlyFire && kind != DamageKind.Collision) return 0f;   // a collision is not weapon fire
            float absorbed = amount < b.Hp ? amount : b.Hp;
            b.Hp -= amount;
            if (absorbed > 0f && emit)
            {
                if (kind == DamageKind.Collision)
                {
                    var ce = new SimEvent { Kind = EventKind.CollisionDamage, Tick = w.Tick, ShipA = attackerShip, ShipB = body.ShipIndex, Value = absorbed, Position = at };
                    w.Events.On(ref ce);
                }
                else EmitHit(w, attackerShip, friendly, body.ShipIndex, absorbed, at);
            }
            if (b.Hp <= 0f)
            {
                var wh = b.Warhead;
                if (wh != null && wh.Armed && !wh.Exploded) wh.DetonateQueued = true;
                body.RemoveBlock(b, attackerShip);
            }
            return absorbed;
        }
    }
}
