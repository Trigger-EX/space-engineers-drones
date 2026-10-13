// SLOT A. Forces and integration. All methods allocation-free; called once per tick per body by World.Step.
// Conventions (docs/sim-design.md section 6): row-vector VRageMath matrices, world = TransformNormal(local, RotMatrix).
// Torques and forces are accumulated in grid-local axes inside a step and rotated to the world once.
using System;
using VRageMath;
using VRage.Game;

namespace SeSim.Core
{
    public static class Physics
    {
        const double TwoPi = 2 * Math.PI;

        /// <summary>Fills ThrusterUnit.DesiredForce (override or inertial-dampener demand) and the thruster sinks' RequestMW from the
        /// current commands and velocity. Idempotent; called by PowerSystem.Update (so the power allocation sees this tick's
        /// demand, not last tick's) and again by ApplyThrusters.
        /// Override: min(OverrideN, MaxForce). Dampeners (Body.DampenersOn) on thrusters WITHOUT override: the ship-wide demand
        /// m*|v_axis|/dt*gain along each opposing direction is shared between that direction's free thrusters by MaxForce.
        /// Request = MinPowerMW + (MaxPowerMW-MinPowerMW) * DesiredForce/MaxForce for working thrusters, 0 otherwise.</summary>
        public static void PrepareThrusters(Body b, SimSettings s)
        {
            var th = b.Thrusters;
            int n = th.Count;
            if (n == 0) return;
            bool damp = b.DampenersOn && !b.IsStatic;
            bool game = s.GameDampenerLaw;
            double[] sum = b.DirScratch;
            bool ovX = false, ovY = false, ovZ = false;      // GameDampenerLaw: an override on an axis switches its dampening off
            Vector3D vl = default(Vector3D);
            if (damp)
            {
                for (int i = 0; i < 6; i++) sum[i] = 0;
                for (int i = 0; i < n; i++)
                {
                    var t = th[i];
                    if (t.OverrideN <= 0f && t.Working) sum[t.DirIndex] += t.MaxForce;
                    if (game && t.OverrideN > 0f) { int ax = t.DirIndex >> 1; if (ax == 0) ovX = true; else if (ax == 1) ovY = true; else ovZ = true; }
                }
                vl = Vector3D.TransformNormal(b.LinearVelocity, b.InvWorldRot);
            }
            double dt = SimSettings.Dt, m = b.Mass, gain = s.ThrusterDampenerGain;
            for (int i = 0; i < n; i++)
            {
                var t = th[i];
                double f = 0;
                if (t.Working)
                {
                    if (t.OverrideN > 0f) f = t.OverrideN < t.MaxForce ? t.OverrideN : t.MaxForce;
                    else if (damp)
                    {
                        // component of the velocity along this thruster's push direction; push only when it opposes the motion
                        double vd = vl.X * t.ForceDirLocal.X + vl.Y * t.ForceDirLocal.Y + vl.Z * t.ForceDirLocal.Z;
                        int dax = t.DirIndex >> 1;
                        if (vd < 0 && sum[t.DirIndex] > 0 && !(game && (dax == 0 ? ovX : dax == 1 ? ovY : ovZ)))
                        {
                            double need = game ? -vd * m / 0.5 * gain : -vd * m / dt * gain;   // total force along this direction
                            f = need * (t.MaxForce / sum[t.DirIndex]);
                            if (f > t.MaxForce) f = t.MaxForce;
                        }
                    }
                }
                t.DesiredForce = (float)f;
                var sink = t.Block.Sink;
                if (sink != null)
                {
                    if (t.Working) sink.RequestMW = (double)t.MinPowerMW + ((double)t.MaxPowerMW - (double)t.MinPowerMW) * (t.MaxForce > 0 ? f / t.MaxForce : 0);
                    else sink.RequestMW = 0;
                }
            }
        }

        /// <summary>Gyro sinks request their definition power while the gyro is working (enabled and functional), 0 otherwise.</summary>
        public static void PrepareGyros(Body b)
        {
            var gy = b.Gyros;
            for (int i = 0; i < gy.Count; i++)
            {
                var g = gy[i];
                var k = g.Block.Sink;
                if (k != null) k.RequestMW = g.Working ? g.Block.Def.PowerMW : 0;
            }
        }

        /// <summary>Thrusters: force = DesiredForce * PowerSink.SuppliedRatio along ForceDirLocal (= block Backward). Applied at the COM
        /// (game behaviour) unless SimSettings.ThrusterTorque, then at the thruster position (r x F torque). Writes
        /// ThrusterUnit.CurrentForce. See PrepareThrusters.</summary>
        public static void ApplyThrusters(Body b, SimSettings s)
        {
            var th = b.Thrusters;
            int n = th.Count;
            if (n == 0 || b.IsStatic) return;
            PrepareThrusters(b, s);
            bool powerOn = s.PowerEnabled;
            double fx = 0, fy = 0, fz = 0, tx = 0, ty = 0, tz = 0;
            Vector3D com = b.ComLocal;
            for (int i = 0; i < n; i++)
            {
                var t = th[i];
                double f = t.DesiredForce;
                if (f > 0 && powerOn && t.Block.Sink != null) f *= t.Block.Sink.SuppliedRatio;
                t.CurrentForce = (float)f;
                if (f <= 0) continue;
                double dx = t.ForceDirLocal.X * f, dy = t.ForceDirLocal.Y * f, dz = t.ForceDirLocal.Z * f;
                fx += dx; fy += dy; fz += dz;
                double rx = t.PosLocal.X - com.X, ry = t.PosLocal.Y - com.Y, rz = t.PosLocal.Z - com.Z;
                tx += ry * dz - rz * dy; ty += rz * dx - rx * dz; tz += rx * dy - ry * dx;
            }
            if (fx == 0 && fy == 0 && fz == 0 && tx == 0 && ty == 0 && tz == 0) return;
            b.Force += Vector3D.TransformNormal(new Vector3D(fx, fy, fz), b.RotMatrix);
            if (s.ThrusterTorque) b.Torque += Vector3D.TransformNormal(new Vector3D(tx, ty, tz), b.RotMatrix);
        }

        /// <summary>Keen MyGridGyroSystem port (docs/sim-design.md section 6): if any working gyro has Override, the target is the
        /// MaxForce*Power-weighted mean of GyroUnit.TargetGridLocal; velocityDiff = target - localAngularVelocity; ramp frames (120 max,
        /// counted down); desiredTorque = (diff*60/ramp) / invInertiaDiagonal; clamp to sphere radius sum(MaxForce*Power) of
        /// overridden gyros (+ non-overridden gyros' force); times Gyro supplied ratio. Without override: slowdown torque
        /// -localAngularVelocity * mult / invInertia clamped per axis to sum(MaxForce) * mult (mult 5 small / 2 large grid, MAX_SLOWDOWN 0.8; gyros damp rotation like the game).</summary>
        public static void ApplyGyros(Body b, SimSettings s)
        {
            var gy = b.Gyros;
            int n = gy.Count;
            if (n == 0 || b.IsStatic) { b.GyroDirty = false; return; }
            bool powerOn = s.PowerEnabled;
            double sumAll = 0, sumOv = 0, sumOther = 0, ratioNum = 0;
            double tgx = 0, tgy = 0, tgz = 0;
            bool anyOv = false;
            for (int i = 0; i < n; i++)
            {
                var g = gy[i];
                if (!g.Working) continue;
                double mf = g.MaxForce;
                sumAll += mf;
                double r = 1;
                if (powerOn && g.Block.Sink != null) r = g.Block.Sink.SuppliedRatio;
                ratioNum += mf * r;
                if (g.Override)
                {
                    anyOv = true;
                    double wgt = mf * g.Power;
                    sumOv += wgt;
                    if (wgt > 0)
                    {
                        Vector3D tg = g.TargetGridLocal(s);
                        tgx += tg.X * wgt; tgy += tg.Y * wgt; tgz += tg.Z * wgt;
                    }
                }
                else sumOther += mf;
            }
            if (sumAll <= 0) { b.GyroDirty = false; return; }
            double ratio = ratioNum / sumAll;
            Vector3D wl = Vector3D.TransformNormal(b.AngularVelocity, b.InvWorldRot);
            double ix = b.InvInertiaLocal.M11, iy = b.InvInertiaLocal.M22, iz = b.InvInertiaLocal.M33;
            if (ix <= 0) ix = 1e-12; if (iy <= 0) iy = 1e-12; if (iz <= 0) iz = 1e-12;
            double ux, uy, uz;
            if (anyOv)
            {
                if (sumOv <= 0) { b.GyroDirty = false; return; }         // override with zero power: gyros neither steer nor damp
                double dx = tgx / sumOv - wl.X, dy = tgy / sumOv - wl.Y, dz = tgz / sumOv - wl.Z;
                double d2 = dx * dx + dy * dy + dz * dz;
                const double half = Math.PI / 2;
                if (b.GyroDirty || b.GyroRampFrames <= 0)
                {
                    int frames = s.GyroRampFrames;
                    double lim = half * half;
                    b.GyroRampFrames = d2 > lim ? frames : (int)(d2 * (frames - 1) / lim) + 1;
                }
                else if (b.GyroRampFrames > 1) b.GyroRampFrames--;
                double k = SimSettings.TicksPerSecond / (double)b.GyroRampFrames;
                // desired torque = I * (diff * k): the full tensor (not only its diagonal) so off-axis coupling cannot slow the convergence
                double ax = dx * k, ay = dy * k, az = dz * k;
                var it = b.InertiaLocal;
                ux = ax * it.M11 + ay * it.M21 + az * it.M31; uy = ax * it.M12 + ay * it.M22 + az * it.M32; uz = ax * it.M13 + ay * it.M23 + az * it.M33;
                double radius = sumOv + sumOther;
                double len = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                if (len > radius) { double sc = radius / len; ux *= sc; uy *= sc; uz *= sc; }
                ux *= ratio; uy *= ratio; uz *= ratio;
            }
            else
            {
                // idle slowdown: -w * mult / invI, clamped per axis to sum(MaxForce) * mult, scaled by MAX_SLOWDOWN (+ (1-MAX_SLOWDOWN)*(1-supply))
                double f = s.GyroMaxSlowdown + (1 - s.GyroMaxSlowdown) * (1 - ratio);
                double mult = b.GridSize == MyCubeSize.Large ? s.GyroSlowdownMultiplierLarge : s.GyroSlowdownMultiplierSmall;
                double cap = sumAll * mult;
                ux = -wl.X * mult / ix; uy = -wl.Y * mult / iy; uz = -wl.Z * mult / iz;
                if (ux > cap) ux = cap; else if (ux < -cap) ux = -cap;
                if (uy > cap) uy = cap; else if (uy < -cap) uy = -cap;
                if (uz > cap) uz = cap; else if (uz < -cap) uz = -cap;
                ux *= f * ratio; uy *= f * ratio; uz *= f * ratio;
            }
            b.GyroDirty = false;
            b.Torque += Vector3D.TransformNormal(new Vector3D(ux, uy, uz), b.RotMatrix);
        }

        /// <summary>Semi-implicit Euler: v += F/m dt; w += I_world^-1 T dt; clamp |v| to MaxSpeed and |w| to MaxAngularSpeed (by grid size);
        /// x += v dt; q = exp(w dt/2) * q. Clears Force/Torque, refreshes world matrix. No gyroscopic term unless
        /// SimSettings.GyroscopicTerm.</summary>
        public static void Integrate(Body b, SimSettings s, double dt)
        {
            b.PrevLinearVelocity = b.LinearVelocity;
            if (b.IsStatic || b.Mass <= 0)
            {
                b.Force = default(Vector3D); b.Torque = default(Vector3D);
                return;
            }
            // linear
            Vector3D v = b.LinearVelocity + b.Force * (b.InvMass * dt);
            double maxV = b.MaxSpeed(s);
            double v2 = v.X * v.X + v.Y * v.Y + v.Z * v.Z;
            if (v2 > maxV * maxV) v *= maxV / Math.Sqrt(v2);
            b.LinearVelocity = v;

            double maxW = b.MaxAngularSpeed(s);
            Vector3D w;
            if (s.GyroscopicTerm)
            {
                // momentum form: L (world) is advanced by the torque, omega follows from L and the inertia tensor in the current
                // orientation (conserves L exactly in free flight; reproduces the w x Iw coupling of an asymmetric body).
                // Orientation uses the omega of the half-step orientation (midpoint rule, second order: the plain explicit form
                // drifted the rotational energy of a tumbling asymmetric body by ~10 % in 20 s, audit SelfTestAudit.TestGyroscopic).
                Vector3D wl0 = Vector3D.TransformNormal(b.AngularVelocity, b.InvWorldRot);
                Vector3D L = Vector3D.TransformNormal(Vector3D.TransformNormal(wl0, b.InertiaLocal), b.RotMatrix) + b.Torque * dt;
                if (s.AngularDamping > 0) L *= 1 - s.AngularDamping * dt;
                Vector3D w0 = OmegaFromL(b, L, b.RotMatrix, maxW);
                QuaternionD qh = RotateQ(b.Rotation, w0, dt * 0.5);
                Vector3D wm = OmegaFromL(b, L, MatrixD.CreateFromQuaternion(qh), maxW);
                b.Position += v * dt;
                b.Rotation = RotateQ(b.Rotation, wm, dt);
                b.UpdateWorldMatrix();
                b.AngularVelocity = OmegaFromL(b, L, b.RotMatrix, maxW);
                b.Force = default(Vector3D); b.Torque = default(Vector3D);
                return;
            }
            // angular: dw_local = I^-1 T_local dt
            Vector3D tl = Vector3D.TransformNormal(b.Torque, b.InvWorldRot);
            Vector3D al = Vector3D.TransformNormal(tl, b.InvInertiaLocal);
            w = b.AngularVelocity + Vector3D.TransformNormal(al, b.RotMatrix) * dt;
            if (s.AngularDamping > 0) w *= 1 - s.AngularDamping * dt;
            double w2 = w.X * w.X + w.Y * w.Y + w.Z * w.Z;
            if (w2 > maxW * maxW) w *= maxW / Math.Sqrt(w2);
            b.AngularVelocity = w;

            b.Position += v * dt;
            RotateBy(b, w, dt);
            b.Force = default(Vector3D); b.Torque = default(Vector3D);
            b.UpdateWorldMatrix();
        }

        // world angular velocity for the world angular momentum L when the grid has orientation matrix rot, clamped to maxW
        static Vector3D OmegaFromL(Body b, Vector3D L, MatrixD rot, double maxW)
        {
            Vector3D ll = Vector3D.TransformNormal(L, MatrixD.Transpose(rot));
            Vector3D w = Vector3D.TransformNormal(Vector3D.TransformNormal(ll, b.InvInertiaLocal), rot);
            double wq = w.X * w.X + w.Y * w.Y + w.Z * w.Z;
            if (wq > maxW * maxW) w *= maxW / Math.Sqrt(wq);
            return w;
        }

        static QuaternionD RotateQ(QuaternionD q0, Vector3D w, double dt)
        {
            double wl = Math.Sqrt(w.X * w.X + w.Y * w.Y + w.Z * w.Z);
            if (wl <= 1e-12) return q0;
            double ang = wl * dt * 0.5, sn = Math.Sin(ang) / wl, cs = Math.Cos(ang);
            var q = new QuaternionD(w.X * sn, w.Y * sn, w.Z * sn, cs) * q0;
            q.Normalize();
            return q;
        }

        // q = exp(w dt / 2) * q  (exact rotation by |w| dt about w)
        static void RotateBy(Body b, Vector3D w, double dt)
        {
            double wl = Math.Sqrt(w.X * w.X + w.Y * w.Y + w.Z * w.Z);
            if (wl > 1e-12)
            {
                double ang = wl * dt * 0.5, sn = Math.Sin(ang) / wl, cs = Math.Cos(ang);
                var dq = new QuaternionD(w.X * sn, w.Y * sn, w.Z * sn, cs);
                var q = dq * b.Rotation;
                q.Normalize();
                b.Rotation = q;
            }
        }

        // =====================================================================================================
        // collisions
        // =====================================================================================================

        /// <summary>Broadphase sphere test, then block-centre-in-cell contact search; separating impulse with restitution at the
        /// deepest contact; emits SimEvent.Collision (rate-limited per pair). Damage only if SimSettings.CollisionDamage.
        /// Debris (fewer than CollisionMinBlocks blocks) and bodies of the same ShipIndex do not collide.</summary>
        public static void ResolveCollisions(World w)
        {
            var s = w.Settings;
            if (!s.Collisions) return;
            var bodies = w.Bodies;
            int n = bodies.Count;
            if (n < 2) return;
            for (int i = 0; i < n - 1; i++)
            {
                var a = bodies[i];
                if (!a.Alive || a.Blocks.Count < s.CollisionMinBlocks) continue;
                for (int j = i + 1; j < n; j++)
                {
                    var c = bodies[j];
                    if (!c.Alive || c.Blocks.Count < s.CollisionMinBlocks) continue;
                    if (a.IsStatic && c.IsStatic) continue;
                    if (a.ShipIndex >= 0 && a.ShipIndex == c.ShipIndex) continue;
                    double dx = a.WorldBoundCenter.X - c.WorldBoundCenter.X, dy = a.WorldBoundCenter.Y - c.WorldBoundCenter.Y, dz = a.WorldBoundCenter.Z - c.WorldBoundCenter.Z;
                    double rr = a.BoundRadius + c.BoundRadius;
                    if (dx * dx + dy * dy + dz * dz > rr * rr) continue;
                    Contact(w, s, a, c);
                }
            }
        }

        // Tests every block centre of the body with fewer blocks against the cells of the other body (in the other body's frame).
        static void Contact(World w, SimSettings s, Body a, Body c)
        {
            Body probe = a.Blocks.Count <= c.Blocks.Count ? a : c;   // its block centres are the sample points
            Body host = ReferenceEquals(probe, a) ? c : a;
            double k = 1.0 / host.CellSize;
            double bestDepth = 0; bool found = false;
            Vector3D bestPointWorld = default(Vector3D), bestNormalWorld = default(Vector3D), bestNormalLocal = default(Vector3D);
            BlockInst bestProbeBlock = null, bestHostBlock = null;
            var pb = probe.Blocks;
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3D sumP = default(Vector3D); int cnt = 0;
                for (int i = 0; i < pb.Count; i++)
                {
                    var blk = pb[i];
                    Vector3D pw = probe.LocalToWorld(blk.LocalCenter);
                    Vector3D pl = host.WorldToLocal(pw);
                    double fx = pl.X * k, fy = pl.Y * k, fz = pl.Z * k;
                    var cell = new Vector3I((int)Math.Floor(fx + 0.5), (int)Math.Floor(fy + 0.5), (int)Math.Floor(fz + 0.5));
                    int ci = host.CellIndex(cell);
                    if (ci < 0) continue;
                    int hbi = host.Cells[ci];
                    if (hbi < 0) continue;
                    // offset inside the cell, in cell units (-0.5..0.5); exit through the nearest face
                    double ox = fx - cell.X, oy = fy - cell.Y, oz = fz - cell.Z;
                    double ax = 0.5 - Math.Abs(ox), ay = 0.5 - Math.Abs(oy), az = 0.5 - Math.Abs(oz);
                    double depth; Vector3D nl;
                    if (ax <= ay && ax <= az) { depth = ax; nl = new Vector3D(ox >= 0 ? 1 : -1, 0, 0); }
                    else if (ay <= az) { depth = ay; nl = new Vector3D(0, oy >= 0 ? 1 : -1, 0); }
                    else { depth = az; nl = new Vector3D(0, 0, oz >= 0 ? 1 : -1); }
                    depth *= host.CellSize;
                    if (pass == 0)
                    {
                        if (!found || depth > bestDepth)
                        {
                            found = true; bestDepth = depth; bestPointWorld = pw; bestNormalLocal = nl;
                            bestProbeBlock = blk; bestHostBlock = host.Blocks[hbi];
                        }
                    }
                    else if (nl.X == bestNormalLocal.X && nl.Y == bestNormalLocal.Y && nl.Z == bestNormalLocal.Z && depth >= 0.5 * bestDepth)
                    {
                        sumP += pw; cnt++;       // average the contacts that share the normal: a flat contact patch pushes through its centre
                    }
                }
                if (pass == 0) { if (!found) return; }
                else if (cnt > 0) bestPointWorld = sumP * (1.0 / cnt);
            }
            bestNormalWorld = host.DirToWorld(bestNormalLocal);                // from host to probe

            // impulse on (host = body 1, probe = body 2); n points from host to probe
            Vector3D n = bestNormalWorld;
            Vector3D rh = bestPointWorld - host.Position, rp = bestPointWorld - probe.Position;
            Vector3D vrel = probe.VelocityAtWorld(bestPointWorld) - host.VelocityAtWorld(bestPointWorld);
            double vn = Vector3D.Dot(vrel, n);
            double invMh = host.IsStatic ? 0 : host.InvMass, invMp = probe.IsStatic ? 0 : probe.InvMass;
            double denom = invMh + invMp;
            if (!host.IsStatic)
            {
                Vector3D rn = Vector3D.Cross(rh, n);
                denom += Vector3D.Dot(Vector3D.Cross(host.ApplyInvInertiaWorld(rn), rh), n);
            }
            if (!probe.IsStatic)
            {
                Vector3D rn = Vector3D.Cross(rp, n);
                denom += Vector3D.Dot(Vector3D.Cross(probe.ApplyInvInertiaWorld(rn), rp), n);
            }
            if (denom <= 0) return;
            // positional correction: push apart along n in proportion to inverse masses
            double corr = bestDepth + 1e-4;
            double wh = invMh / (invMh + invMp), wp = invMp / (invMh + invMp);
            if (!host.IsStatic) { host.Position -= n * (corr * wh); host.UpdateWorldMatrix(); }
            if (!probe.IsStatic) { probe.Position += n * (corr * wp); probe.UpdateWorldMatrix(); }
            // contact episode bookkeeping (one Collision event per episode, SimSettings.CollisionEpisodeEvents)
            long key = ((long)Math.Min(host.Id, probe.Id) << 32) | (uint)Math.Max(host.Id, probe.Id);
            bool newEpisode = false;
            if (s.CollisionEpisodeEvents)
            {
                long lastContact;
                newEpisode = !w.CollisionLastTick.TryGetValue(key, out lastContact) || w.Tick - lastContact > s.CollisionEpisodeGapTicks;
                w.CollisionLastTick[key] = w.Tick;                        // every contact tick extends the episode
                if (newEpisode) w.CollisionEpisodePending[key] = true;
            }
            if (vn >= 0) return;                                          // already separating
            double jimp = -(1 + s.CollisionRestitution) * vn / denom;
            Vector3D imp = n * jimp;
            if (!host.IsStatic)
            {
                host.LinearVelocity -= imp * host.InvMass;
                host.AngularVelocity -= host.ApplyInvInertiaWorld(Vector3D.Cross(rh, imp));
            }
            if (!probe.IsStatic)
            {
                probe.LinearVelocity += imp * probe.InvMass;
                probe.AngularVelocity += probe.ApplyInvInertiaWorld(Vector3D.Cross(rp, imp));
            }

            // event: first closing contact of an episode (episode events), or rate limited per pair (legacy)
            bool emit;
            if (s.CollisionEpisodeEvents)
            {
                bool pend;
                emit = w.CollisionEpisodePending.TryGetValue(key, out pend) && pend;
                if (emit) w.CollisionEpisodePending[key] = false;
            }
            else
            {
                long last;
                emit = !w.CollisionLastTick.TryGetValue(key, out last) || w.Tick - last >= s.CollisionEventCooldownTicks;
                if (emit) w.CollisionLastTick[key] = w.Tick;
            }
            if (emit)
            {
                var e = new SimEvent { Kind = EventKind.Collision, Tick = w.Tick, ShipA = host.ShipIndex, ShipB = probe.ShipIndex, Value = -vn, Position = bestPointWorld };
                w.Events.On(ref e);
            }
            if (s.CollisionDamage)
            {
                // kinetic energy of the closing motion at the contact: 0.5 * reducedMass * vn^2, scaled to hit points, shared by the blocks of
                // each body within CollisionDamagePatchM of the contact point (the deepest block always included). Not weapon fire: it
                // also applies between teammates and with FriendlyFire off.
                double mu = 1.0 / denom;                                  // effective mass at the contact
                float dmg = (float)(s.CollisionDamageScale * 0.5 * mu * vn * vn);
                if (dmg > 0)
                {
                    CollisionDamageTo(w, host, bestHostBlock, bestPointWorld, dmg, probe.ShipIndex, s);
                    CollisionDamageTo(w, probe, bestProbeBlock, bestPointWorld, dmg, host.ShipIndex, s);
                }
            }
        }

        static void CollisionDamageTo(World w, Body body, BlockInst deepest, Vector3D at, float total, int attackerShip, SimSettings s)
        {
            var list = w.CollisionScratch;
            list.Clear();
            double r2 = s.CollisionDamagePatchM * s.CollisionDamagePatchM;
            var bl = body.Blocks;
            for (int i = 0; i < bl.Count; i++)
            {
                var blk = bl[i];
                if (ReferenceEquals(blk, deepest)) continue;
                Vector3D p = body.LocalToWorld(blk.LocalCenter);
                double dx = p.X - at.X, dy = p.Y - at.Y, dz = p.Z - at.Z;
                if (dx * dx + dy * dy + dz * dz <= r2) list.Add(blk);
            }
            if (deepest != null) list.Add(deepest);
            if (list.Count == 0) return;
            float each = total / list.Count;
            for (int i = 0; i < list.Count; i++)
                SeSim.Combat.Damage.ApplyToBlock(w, list[i], each, attackerShip, SeSim.Combat.DamageKind.Collision, at);
            list.Clear();
        }
    }
}
