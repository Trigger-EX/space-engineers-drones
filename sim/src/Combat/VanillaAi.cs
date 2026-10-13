// SLOT C. Approximation of the vanilla AI blocks for scriptless ships (Midge): AI Offensive (Stay at Range, evasive) + AI Flight (speed
// limit from block settings, default 100 here) + fixed guns fired when the target is inside a 5 deg cone and < weapon range.
// Attached by slot D's scenario when a ship has an enabled OffensiveCombatBlock and FlightMovementBlock and no running PB.
// Flies with gyro override + thruster override through the same Core units the scripts use (ShipPilot, no physics shortcuts).
//
// Behaviour (docs/script-audit.md / scripts/MidgeNotes.md "Expected behaviour limits"): the target search runs every
// SearchIntervalS (default 30 s, SimSettings.VanillaSearchIntervalS; the first search happens at a uniformly random time inside the
// first interval, a block that was just switched on does not search at once); target = closest enemy within SearchRange with line of
// sight to its bound centre. Stay at Range keeps StayMin..StayMax metres (radial speed proportional to the error), evasive manoeuvres
// strafe sideways at 30 m/s and flip every 2-5 s. The fixed gun axis is turned onto the first-order lead point (no gun-axis roll
// control); the gun fires inside VanillaFireConeDeg and weapon range. No IGC, no friendly-fire check, no shared targeting.
// The AI only runs while both OffensiveCombat and FlightMovement blocks exist, are enabled and functional (scripts switch them
// with OnOff); a ship without them drifts.
using System;
using System.Collections.Generic;
using VRageMath;
using SeSim.Core;
using SeSim.Api;

namespace SeSim.Combat
{
    public sealed class VanillaAiBrain : IShipBrain
    {
        public Body Ship;
        public double SearchRange = -1, StayMin = -1, StayMax = -1, SpeedLimit = -1;   // < 0: SimSettings.VanillaSearchRange(2500, Data SearchEnemyComponent)/StayMin/Max/SpeedLimit
        public double SearchIntervalS = -1;        // < 0: SimSettings.VanillaSearchIntervalS
        public double EvasiveSpeed = 30;
        public Body Target;
        public double Kp = 6.0;                    // aim gain (1/s)
        /// <summary>True for ships whose PB script switches the AI blocks on and off (the drones' v1.8 hybrid search).
        /// While the AI blocks are off the brain is completely hands-off, so the script owns thrusters, gyros and guns;
        /// when they switch off it releases what it overrode once (like the game's AI handing back control).</summary>
        public bool ScriptShip;
        bool _wasActive, _savedDampeners;

        double _nextSearch = -1, _flipAt;
        int _strafeDir = 1;
        DetRng _rng;
        BlockInst _offensive, _flight;
        int _blocksVersion = -1;

        public VanillaAiBrain(Body ship) { Ship = ship; }

        void FindAiBlocks()
        {
            _blocksVersion = Ship.Version;
            _offensive = _flight = null;
            var bl = Ship.Blocks;
            for (int i = 0; i < bl.Count; i++)
            {
                var g = bl[i].Api as SimGenericFunctional;
                if (g == null) continue;
                string k = g.AiKind;
                if (k == "OffensiveCombat" && _offensive == null) _offensive = bl[i];
                else if (k == "FlightMovement" && _flight == null) _flight = bl[i];
            }
        }

        static bool Active(BlockInst b, SimSettings s)
        {
            if (b == null || b.Destroyed || !b.IsFunctional) return false;
            var g = b.Api as SimGenericFunctional;
            if (s.AiBehaviorNeedsSwitch && g != null && !g.BehaviorOn) return false;
            var f = b.Api as SimFunctionalBlock;
            return f == null || f.EnabledValue;
        }

        public void Tick(World w)
        {
            var b = Ship;
            if (b == null || !b.Alive || b.Blocks.Count == 0 || b.Mass <= 0) return;
            var s = w.Settings;
            if (_rng == null) _rng = new DetRng(DetRng.Mix(DetRng.Mix(w.Seed, 0xA1), (ulong)b.EntityId));
            if (_blocksVersion != b.Version) FindAiBlocks();
            double now = w.Time;
            bool active = Active(_offensive, s) && Active(_flight, s);
            if (ScriptShip)
            {
                if (s.ScriptAiBlocksInert) return;      // switched on by the script but never does anything (behaviour off)
                if (!active)
                {
                    if (_wasActive) { _wasActive = false; ReleaseControl(b); }
                    return;
                }
                if (!_wasActive) { _wasActive = true; _savedDampeners = b.DampenersOn; }
            }
            b.DampenersOn = false;
            WeaponUnit gun = FirstGun(b);
            if (!active) { Idle(b, gun, s); return; }

            double interval = SearchIntervalS >= 0 ? SearchIntervalS : s.VanillaSearchIntervalS;
            if (_nextSearch < 0) _nextSearch = s.VanillaSearchOnActivate ? now : now + _rng.NextDouble() * interval;     // first acquisition on activation, or anywhere in the first interval
            if (now >= _nextSearch)
            {
                _nextSearch = now + interval;
                Target = Search(w, b, s);
            }
            if (Target != null && (!Target.Alive || Target.Blocks.Count == 0 || SeSim.Api.Detection.EffectiveOwner(Target) == 0)) Target = null;
            if (Target == null) { Idle(b, gun, s); return; }

            Vector3D rel = Target.Position - b.Position;
            double d = rel.Length();
            if (d < 1) return;
            Vector3D dirT = rel * (1.0 / d);

            // ---- movement: stay at range + evasive strafe ----
            double stayMin = StayMin >= 0 ? StayMin : s.VanillaStayMin, stayMax = StayMax >= 0 ? StayMax : s.VanillaStayMax;
            double speedLimit = SpeedLimit > 0 ? SpeedLimit : s.VanillaSpeedLimit;
            double radial = 0;
            double err = d > stayMax ? d - stayMax : (d < stayMin ? d - stayMin : 0);
            if (err != 0)
            {
                // braking-limited approach: v = sqrt(2 * a * |err|), a = 60% of what the thrusters can do along the line of sight
                double amax = ShipPilot.MaxAccelAlong(b, dirT) * 0.6;
                double vr = Math.Sqrt(2 * amax * Math.Abs(err)) + (Math.Abs(err) < 20 ? 0 : 0);
                if (vr > speedLimit) vr = speedLimit;
                radial = err > 0 ? vr : -vr;
            }
            if (now >= _flipAt) { _strafeDir = -_strafeDir; _flipAt = now + 2 + 3 * _rng.NextDouble(); }
            Vector3D side = Vector3D.Cross(Vector3D.Up, dirT);
            double sl = side.Length();
            side = sl > 1e-6 ? side * (_strafeDir / sl) : new Vector3D(1, 0, 0);
            Vector3D vdes = Target.LinearVelocity + dirT * radial + side * Math.Min(EvasiveSpeed, speedLimit);
            vdes = ShipPilot.Clamp(vdes, speedLimit);
            ShipPilot.SetAcceleration(b, ShipPilot.Clamp((vdes - b.LinearVelocity) * 1.0, 100));

            // ---- aiming: gun axis onto the lead point ----
            Vector3D axis, want = dirT;
            double range = 800;
            if (gun != null)
            {
                axis = b.DirToWorld(gun.DirLocal);
                Vector3D muz = b.LocalToWorld(gun.MuzzleLocal);
                Vector3D aimPt = Decoys.AimPoint(w, Target, muz, b.EntityId);     // working enemy decoy attracts the aim (SimSettings.DecoysFunctional)
                Vector3D r2 = aimPt - muz;
                double speed = gun.Mode == ProjectileSystem.ModeRocket ? 0.5 * (gun.Ammo.MissileInitialSpeed + gun.Ammo.Speed) : (gun.Ammo.Speed > 0 ? gun.Ammo.Speed : gun.Ammo.MissileInitialSpeed);
                Vector3D relV = gun.Mode == ProjectileSystem.ModeRocket ? Target.LinearVelocity : Target.LinearVelocity - b.VelocityAtWorld(muz);
                double ti;
                Vector3D lead = r2;
                if (s.VanillaAimLead && CombatUtil.Intercept(r2, relV, speed, out ti)) lead = r2 + relV * ti;
                double ll = lead.Length();
                if (ll > 1e-6) want = lead * (1.0 / ll);
                if (gun.Ammo.MaxTrajectory > 0) range = gun.Ammo.MaxTrajectory;
            }
            else axis = b.DirToWorld(Vector3D.Forward);
            Vector3D rot = ShipPilot.RotationBetween(axis, want);
            ShipPilot.SetAngularVelocity(b, ShipPilot.TurnRate(rot, Kp, s.VanillaTurnAccel, s.VanillaMaxTurnRate), s);
            if (gun != null)
            {
                double cosErr = axis.X * want.X + axis.Y * want.Y + axis.Z * want.Z;
                gun.ShootOn = cosErr >= Math.Cos(s.VanillaFireConeDeg * Math.PI / 180.0) && d - Target.BoundRadius <= range;
            }
        }

        void Idle(Body b, WeaponUnit gun, SimSettings s)
        {
            if (gun != null) gun.ShootOn = false;
            // no brain without AI blocks: gyros hold/dampen by themselves, thrusters off
            ShipPilot.SetAcceleration(b, default(Vector3D));
        }

        void ReleaseControl(Body b)
        {
            for (int i = 0; i < b.Thrusters.Count; i++) b.Thrusters[i].OverrideN = 0f;
            for (int i = 0; i < b.Gyros.Count; i++) { var g = b.Gyros[i]; g.Override = false; g.Pitch = g.Yaw = g.Roll = 0f; }
            b.GyroDirty = true;
            for (int i = 0; i < b.Weapons.Count; i++) if (b.Weapons[i].Block.Turret == null) b.Weapons[i].ShootOn = false;
            b.DampenersOn = _savedDampeners;
            Target = null; _nextSearch = -1;
        }

        static WeaponUnit FirstGun(Body b)
        {
            for (int i = 0; i < b.Weapons.Count; i++)
                if (b.Weapons[i].Block.Turret == null && !b.Weapons[i].Block.Destroyed) return b.Weapons[i];
            return null;
        }

        Body Search(World w, Body b, SimSettings s)
        {
            double range = SearchRange > 0 ? SearchRange : s.VanillaSearchRange;
            bool prefer = s.DecoysFunctional && s.DecoyPreferTarget, bestHas = false;
            Body best = null; double bd = range * range;
            var bodies = w.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var o = bodies[i];
                if (!o.Alive || o.Blocks.Count < 2 || ReferenceEquals(o, b)) continue;
                long eo = SeSim.Api.Detection.EffectiveOwner(o);
                if (eo == 0 || w.Teams.Relation(b.OwnerId, eo) != VRage.Game.MyRelationsBetweenPlayerAndBlock.Enemies) continue;
                Vector3D dv = o.WorldBoundCenter - b.Position;
                double d2 = dv.LengthSquared();
                bool has = prefer && Decoys.HasWorking(w, o);
                if (d2 >= range * range) continue;
                if (bestHas && !has) continue;                       // a body with a working decoy beats any body without
                if (has == bestHas && d2 >= bd) continue;
                double d = Math.Sqrt(d2);
                RayHit h;
                if (Raycast.RayWorld(w, b.Position, dv * (1.0 / d), d, b, out h) && !ReferenceEquals(h.Body, o))
                {
                    long ho = SeSim.Api.Detection.EffectiveOwner(h.Body);
                    if (ho == 0 || w.Teams.Relation(b.OwnerId, ho) != VRage.Game.MyRelationsBetweenPlayerAndBlock.Enemies) continue;
                }
                bd = d2; best = o; bestHas = has;
            }
            return best;
        }
    }
}
