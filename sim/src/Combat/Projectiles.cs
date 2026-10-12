// SLOT C. Projectiles as a struct-of-arrays pool (no per-shot allocation). World phase 7.
// Per tick per live projectile: rockets accelerate (MissileAcceleration up to DesiredSpeed); the segment p0 -> p1 is tested block-exact
// (Raycast.RayBody, DDA over the dense cell array) against every body in that body's own frame, using its pose at the START of the
// tick for p0 and its pose at the END for p1 (SimSettings.ProjectileSweepBodies), so a target moving 1.7 m per tick does not make the
// hit test lag by up to one tick. The shooter's own body is ignored for ProjectileShooterIgnoreS (muzzles inside the hull), after
// that friendly fire is possible. Hit -> Damage.ApplyProjectile (bullet / rocket explosion / railgun slug walk). Bullets and slugs
// expire after MaxTrajectory metres travelled, rockets explode there.
// Ammo kinds (Mode): 0 bullet (Projectile ammo), 1 rocket (Missile with explosion radius), 2 slug (Missile, health pool, no explosion).
using System;
using VRageMath;
using SeSim.Core;
using SeSim.Blueprint;

namespace SeSim.Combat
{
    public sealed class ProjectileSystem
    {
        public const byte ModeBullet = 0, ModeRocket = 1, ModeSlug = 2;

        public readonly World World;
        public int Count;                       // live projectiles are packed in [0, Count)
        public Vector3D[] Pos, Vel;
        public double[] Travelled, Age;
        public AmmoDef[] Ammo;
        public int[] ShooterShip;               // ShipInstance index for attribution
        public Body[] ShooterBody;
        public float[] HealthPool;              // railgun penetration budget
        public byte[] Mode;
        public long[] ShotId;                   // unique id per spawned projectile (Hit events carry it: unique-hit metric)
        long _nextShotId;
        public long TotalSpawned, TotalHits, TotalExpired;

        readonly Body[] _accBody = new Body[8];
        readonly double[] _accDmg = new double[8];
        readonly Vector3D[] _accAt = new Vector3D[8];

        // per-tick old-pose cache for swept tests, indexed by Body.Id
        long[] _poseTick = new long[64];
        MatrixD[] _oldRot = new MatrixD[64];
        Vector3D[] _oldOrigin = new Vector3D[64];

        public ProjectileSystem(World w, int capacity)
        {
            World = w;
            if (capacity < 16) capacity = 16;
            Alloc(capacity);
        }

        void Alloc(int n)
        {
            Pos = new Vector3D[n]; Vel = new Vector3D[n]; Travelled = new double[n]; Age = new double[n];
            Ammo = new AmmoDef[n]; ShooterShip = new int[n]; ShooterBody = new Body[n]; HealthPool = new float[n]; Mode = new byte[n]; ShotId = new long[n];
        }

        void Grow()
        {
            int n = Pos.Length * 2;
            Array.Resize(ref Pos, n); Array.Resize(ref Vel, n); Array.Resize(ref Travelled, n); Array.Resize(ref Age, n);
            Array.Resize(ref Ammo, n); Array.Resize(ref ShooterShip, n); Array.Resize(ref ShooterBody, n);
            Array.Resize(ref HealthPool, n); Array.Resize(ref Mode, n); Array.Resize(ref ShotId, n);
        }

        public static byte ModeOf(AmmoDef a)
        {
            if (!a.IsMissile) return ModeBullet;
            return a.MissileExplosionDamage > 0 && a.MissileExplosionRadius > 0 ? ModeRocket : ModeSlug;   // large railgun slug: radius 1, damage 0
        }

        public void Spawn(Vector3D pos, Vector3D vel, AmmoDef ammo, Body shooter, int shooterShip)
        {
            if (Count == Pos.Length) Grow();
            int i = Count++;
            Pos[i] = pos; Vel[i] = vel; Travelled[i] = 0; Age[i] = 0; Ammo[i] = ammo;
            ShooterShip[i] = shooterShip; ShooterBody[i] = shooter;
            HealthPool[i] = ammo.MissileHealthPool;
            Mode[i] = ModeOf(ammo);
            ShotId[i] = ++_nextShotId;
            TotalSpawned++;
        }

        void Remove(int i)
        {
            int last = --Count;
            if (i != last)
            {
                Pos[i] = Pos[last]; Vel[i] = Vel[last]; Travelled[i] = Travelled[last]; Age[i] = Age[last]; Ammo[i] = Ammo[last];
                ShooterShip[i] = ShooterShip[last]; ShooterBody[i] = ShooterBody[last]; HealthPool[i] = HealthPool[last]; Mode[i] = Mode[last]; ShotId[i] = ShotId[last];
            }
            Ammo[last] = null; ShooterBody[last] = null;
        }

        public void Step(double dt)
        {
            var w = World;
            int i = 0;
            double ignoreS = w.Settings.ProjectileShooterIgnoreS;
            while (i < Count)
            {
                AmmoDef ammo = Ammo[i];
                byte mode = Mode[i];
                Vector3D p0 = Pos[i];
                Vector3D v0 = Vel[i], v1 = v0;
                if (mode == ModeRocket && ammo.MissileAcceleration > 0)
                {
                    double sp = v0.Length();
                    double cap = ammo.Speed > 0 ? ammo.Speed : sp;
                    if (sp < cap && sp > 0)
                    {
                        double sp2 = sp + ammo.MissileAcceleration * dt; if (sp2 > cap) sp2 = cap;
                        v1 = v0 * (sp2 / sp);
                    }
                }
                Vector3D vavg = (v0 + v1) * 0.5;
                double segLen = vavg.Length() * dt;
                bool expired = false;
                double remain = ammo.MaxTrajectory - Travelled[i];
                if (ammo.MaxTrajectory > 0 && segLen >= remain) { segLen = remain > 0 ? remain : 0; expired = true; }
                Vector3D dir = segLen > 0 ? vavg * (1.0 / vavg.Length()) : v0;
                Vector3D p1 = new Vector3D(p0.X + dir.X * segLen, p0.Y + dir.Y * segLen, p0.Z + dir.Z * segLen);
                Body ignore = Age[i] < ignoreS ? ShooterBody[i] : null;
                Body shooter = ShooterBody[i];
                long owner = shooter != null ? shooter.OwnerId : 0;
                int ship = ShooterShip[i];
                w.CurrentShotId = ShotId[i];

                bool consumed = false;
                if (segLen > 0)
                {
                    RayHit hit;
                    if (mode == ModeSlug)
                    {
                        float pool = HealthPool[i];
                        int guard = 0, nacc = 0;
                        while (guard++ < 512 && FindHit(p0, p1, ignore, owner, out hit))
                        {
                            Damage.LastSlugBody = null; Damage.LastSlugDamage = 0;
                            bool done = Damage.ApplyProjectile(w, ref hit, ammo, ref pool, ship, owner);
                            // one Hit event per body the slug damaged
                            var vb = Damage.LastSlugBody; float vd = Damage.LastSlugDamage;
                            if (vb != null && vd > 0)
                            {
                                int k = 0; while (k < nacc && !ReferenceEquals(_accBody[k], vb)) k++;
                                if (k == nacc && nacc < _accBody.Length) { _accBody[k] = vb; _accDmg[k] = 0; _accAt[k] = hit.Point; nacc++; }
                                if (k < nacc) _accDmg[k] += vd;
                            }
                            if (done) { consumed = true; break; }
                        }
                        for (int k = 0; k < nacc; k++)
                        {
                            Damage.EmitHit(w, ship, Damage.IsFriendly(w, owner, _accBody[k]), _accBody[k].ShipIndex, _accDmg[k], _accAt[k]);
                            _accBody[k] = null;
                        }
                        if (nacc > 0) TotalHits++;
                        HealthPool[i] = pool;
                    }
                    else if (FindHit(p0, p1, ignore, owner, out hit))
                    {
                        TotalHits++;
                        float pool = 0;
                        Damage.ApplyProjectile(w, ref hit, ammo, ref pool, ship, owner);
                        consumed = true;
                    }
                }
                if (!consumed && expired)
                {
                    consumed = true; TotalExpired++;
                    if (mode == ModeRocket)
                    {
                        // detonates where it ran out of range (no block hit): explosion centred on the last position
                        Explosions.Explode(w, p1, ammo.MissileExplosionRadius, ammo.MissileExplosionDamage, ship, owner);
                    }
                }
                w.CurrentShotId = 0;
                if (consumed) { Remove(i); continue; }
                Pos[i] = p1; Vel[i] = v1; Travelled[i] += segLen; Age[i] += dt;
                i++;
            }
        }

        // =====================================================================================================
        // swept segment test
        // =====================================================================================================

        /// <summary>Nearest block hit by the segment p0..p1 over all bodies (except 'ignore'; friendly bodies too when friendly fire is off).</summary>
        bool FindHit(Vector3D p0, Vector3D p1, Body ignore, long owner, out RayHit best)
        {
            best = default(RayHit);
            var w = World;
            bool sweep = w.Settings.ProjectileSweepBodies;
            bool ff = w.Settings.FriendlyFire;
            var bodies = w.Bodies;
            double bestFrac = double.MaxValue;
            bool found = false;
            double sx = p1.X - p0.X, sy = p1.Y - p0.Y, sz = p1.Z - p0.Z;
            double slen2 = sx * sx + sy * sy + sz * sz;
            const double dt = SimSettings.Dt;
            for (int bi = 0; bi < bodies.Count; bi++)
            {
                Body b = bodies[bi];
                if (!b.Alive || b.Blocks.Count == 0 || ReferenceEquals(b, ignore)) continue;
                if (!ff && Damage.IsFriendly(w, owner, b)) continue;
                // broadphase: distance of the bound centre to the segment <= radius + body motion this tick
                double margin = b.BoundRadius;
                if (sweep)
                {
                    double vl = b.LinearVelocity.Length() * dt, wl = b.AngularVelocity.Length() * dt * b.BoundRadius;
                    margin += vl + wl;
                }
                double cx = b.WorldBoundCenter.X - p0.X, cy = b.WorldBoundCenter.Y - p0.Y, cz = b.WorldBoundCenter.Z - p0.Z;
                double t = slen2 > 0 ? (cx * sx + cy * sy + cz * sz) / slen2 : 0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double dx = cx - sx * t, dy = cy - sy * t, dz = cz - sz * t;
                if (dx * dx + dy * dy + dz * dz > margin * margin) continue;

                Vector3D start = p0;
                if (sweep && (b.LinearVelocity.X != 0 || b.LinearVelocity.Y != 0 || b.LinearVelocity.Z != 0 ||
                              b.AngularVelocity.X != 0 || b.AngularVelocity.Y != 0 || b.AngularVelocity.Z != 0))
                    start = StartInNewPose(b, p0);
                double ex = p1.X - start.X, ey = p1.Y - start.Y, ez = p1.Z - start.Z;
                double len = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                if (len < 1e-9) continue;
                var dir = new Vector3D(ex / len, ey / len, ez / len);
                RayHit h;
                if (!Raycast.RayBody(b, start, dir, len, out h)) continue;
                double frac = h.Distance / len;
                if (frac < bestFrac) { bestFrac = frac; best = h; found = true; }
            }
            return found;
        }

        // Maps p0 (world, tick start) into the body's frame at tick start and back out with the pose at tick end, so a ray from
        // the result to p1 is the projectile path seen in the target's frame (straight-line approximation inside one tick).
        Vector3D StartInNewPose(Body b, Vector3D p0)
        {
            int id = b.Id;
            if (id >= _poseTick.Length)
            {
                int n = Math.Max(_poseTick.Length * 2, id + 1);
                Array.Resize(ref _poseTick, n); Array.Resize(ref _oldRot, n); Array.Resize(ref _oldOrigin, n);
            }
            long stamp = World.Tick + 1;
            if (_poseTick[id] != stamp)
            {
                const double dt = SimSettings.Dt;
                Vector3D w = b.AngularVelocity;
                double wl = w.Length();
                QuaternionD q = b.Rotation;
                if (wl > 1e-12)
                {
                    double ang = wl * dt * 0.5, sn = Math.Sin(ang) / wl, cs = Math.Cos(ang);
                    var dqInv = new QuaternionD(-w.X * sn, -w.Y * sn, -w.Z * sn, cs);   // inverse of the tick's rotation increment
                    q = dqInv * b.Rotation;
                    q.Normalize();
                }
                MatrixD rot = MatrixD.CreateFromQuaternion(q);
                Vector3D oldPos = b.Position - b.LinearVelocity * dt;
                _oldRot[id] = rot;
                _oldOrigin[id] = oldPos - Vector3D.TransformNormal(b.ComLocal, rot);
                _poseTick[id] = stamp;
            }
            MatrixD r = _oldRot[id];
            Vector3D rel = p0 - _oldOrigin[id];
            // local = rel * R^T
            double lx = rel.X * r.M11 + rel.Y * r.M12 + rel.Z * r.M13;
            double ly = rel.X * r.M21 + rel.Y * r.M22 + rel.Z * r.M23;
            double lz = rel.X * r.M31 + rel.Y * r.M32 + rel.Z * r.M33;
            return b.LocalToWorld(new Vector3D(lx, ly, lz));
        }
    }
}
