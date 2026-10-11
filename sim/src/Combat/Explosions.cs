// SLOT C. Explosions (rockets, warheads) and warhead state.
// Explode: every live block whose centre is within radius of the centre (bodies pre-filtered with World.BodiesInSphere) takes
// damage * (1 - d/r)^ExplosionFalloffPower [I, Keen falloff not documented]; no occlusion. Warhead: Def.WarheadRadius (4.4883)
// and Def.WarheadDamage (15000); an armed warhead whose block is destroyed also detonates (Damage.ApplyCore queues it,
// CombatSystem.UpdateWarheads fires it) [I]. A detonation never runs inside a script call: WarheadUnit.Detonate only queues, the
// explosion happens in World phase 4 of the same tick. The warhead's own block is not hit by its own blast (it is removed
// afterwards); the rest of the carrier ship is (FriendlyFire event with ShipA == ShipB).
using System;
using System.Collections.Generic;
using VRageMath;
using SeSim.Core;

namespace SeSim.Combat
{
    public static class Explosions
    {
        public static void Explode(World w, Vector3D center, double radius, double damage, int attackerShip)
        {
            Explode(w, center, radius, damage, attackerShip, Damage.OwnerOfShip(w, attackerShip), null);
        }

        public static void Explode(World w, Vector3D center, double radius, double damage, int attackerShip, long attackerOwner)
        {
            Explode(w, center, radius, damage, attackerShip, attackerOwner, null);
        }

        /// <summary>'skip' (may be null) is a block that is not damaged (the exploding warhead itself).</summary>
        public static void Explode(World w, Vector3D center, double radius, double damage, int attackerShip, long attackerOwner, BlockInst skip)
        {
            if (radius <= 0 || damage <= 0 || w.Combat == null) return;
            var cs = w.Combat;
            var bodies = cs.ScratchBodies; var blocks = cs.ExpBlocks; var dmg = cs.ExpDamage;
            blocks.Clear(); dmg.Clear();
            w.BodiesInSphere(center, radius, bodies);
            double r2 = radius * radius, inv = 1.0 / radius;
            double pw = w.Settings.ExplosionFalloffPower;
            bool ff = w.Settings.FriendlyFire;
            for (int bi = 0; bi < bodies.Count; bi++)
            {
                var body = bodies[bi];
                if (!ff && Damage.IsFriendly(w, attackerOwner, body)) continue;
                Vector3D lc = body.WorldToLocal(center);
                var list = body.Blocks;
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    double dx = b.LocalCenter.X - lc.X, dy = b.LocalCenter.Y - lc.Y, dz = b.LocalCenter.Z - lc.Z;
                    double d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 > r2 || ReferenceEquals(b, skip)) continue;
                    double f = 1.0 - Math.Sqrt(d2) * inv;
                    if (pw != 1.0) f = Math.Pow(f, pw);
                    float dm = (float)(damage * f);
                    if (dm <= 0f) continue;
                    blocks.Add(b); dmg.Add(dm);
                }
            }
            // apply after collecting (RemoveBlock renumbers Body.Blocks); one event per damaged body with the summed damage
            Body curBody = null; double sum = 0; Vector3D at = center;
            for (int i = 0; i < blocks.Count; i++)
            {
                var blk = blocks[i];
                if (blk.Destroyed) continue;
                var bd = blk.Body;
                if (!ReferenceEquals(bd, curBody))
                {
                    if (curBody != null && sum > 0) Damage.EmitHit(w, attackerShip, Damage.IsFriendly(w, attackerOwner, curBody), curBody.ShipIndex, sum, at);
                    curBody = bd; sum = 0;
                }
                sum += Damage.ApplyCore(w, blk, dmg[i], attackerShip, attackerOwner, DamageKind.Explosion, center, false);
            }
            if (curBody != null && sum > 0) Damage.EmitHit(w, attackerShip, Damage.IsFriendly(w, attackerOwner, curBody), curBody.ShipIndex, sum, at);
            blocks.Clear(); dmg.Clear(); bodies.Clear();
        }
    }

    public sealed class WarheadUnit
    {
        public BlockInst Block;
        public bool Armed, CountingDown;
        public double CountdownLeft, DetonationTime = 10;
        public bool Exploded;
        public bool DetonateQueued;           // set by Detonate()/countdown end/destruction while armed; consumed by Update
        /// <summary>Slot B calls this from IMyWarhead.Detonate (already checked IsArmed): queue the explosion for phase 4 of this tick.</summary>
        public void Detonate(World w) { if (Armed && !Exploded) DetonateQueued = true; }

        /// <summary>Phase 4: countdown and queued detonation (CombatSystem drops the unit once Exploded).</summary>
        public void Update(World w, double dt)
        {
            if (Exploded) return;
            if (CountingDown && !Block.Destroyed)
            {
                CountdownLeft -= dt;
                if (CountdownLeft <= 1e-9) DetonateQueued = true;
            }
            if (!DetonateQueued) return;
            Explode(w);
        }

        void Explode(World w)
        {
            Exploded = true; DetonateQueued = false; CountingDown = false;
            var b = Block; var body = b.Body;
            if (body == null) return;
            Vector3D c = body.LocalToWorld(b.LocalCenter);
            double radius = b.Def.WarheadRadius, damage = b.Def.WarheadDamage;
            int ship = body.ShipIndex;
            var e = new SimEvent { Kind = EventKind.Detonation, Tick = w.Tick, ShipA = ship, ShipB = -1, Value = radius, Position = c };
            w.Events.On(ref e);
            Explosions.Explode(w, c, radius, damage, ship, body.OwnerId, b);
            if (!b.Destroyed) body.RemoveBlock(b, ship);
        }
    }
}
