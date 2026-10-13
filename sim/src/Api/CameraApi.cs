// SLOT B. IMyCameraBlock: raycast with charge (docs/sim-design.md 4.2).
// Charge: while EnableRaycast && IsWorking, AvailableScanRange += CameraChargeMetersPerMs * 1000/60 per tick (2 km/s), capped at
//   SimSettings.CameraMaxCharge (default unlimited [I]).
// Raycast(distance, pitch, yaw): degrees. Direction = camera Forward rotated by pitch about camera Right, then by yaw about
//   camera Up (+pitch aims up; +yaw aims RIGHT like the game's rotation about WorldMatrix.Down; SimSettings.CameraYawRight=false
//   restores the old mirrored sign). |pitch|,|yaw| <= RaycastConeLimit else empty result and no charge used.
// Raycast(Vector3D target): distance = |target - camera|, direction normalised (cone: angle to Forward <= limit);
//   Raycast(distance, Vector3D dir) and CanScan(distance, dir): dir is camera-LOCAL (x right, y up, -z forward) like the game
//   (SimSettings.CameraDirectionLocal=false: WORLD, old model).
// A scan that passes CanScan consumes 'distance' of charge even when it hits nothing. Origin = camera block center.
// The camera's own Body is excluded from hits (SimSettings.CameraHitsOwnBody: only the camera block itself is skipped). Result = Detection.FromRayHit (EntityId of the GRID, HitPosition = hit point,
// Velocity = body velocity, Relationship via TeamTable, BoundingBox = grid world AABB, TimeStamp = World.TimeMs+1).
using System;
using VRageMath;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimCamera : SimFunctionalBlock, IMyCameraBlock
    {
        public double Charge;          // metres
        public bool Raycasting;
        public long Scans, Hits;       // statistics (selftest / debugging)
        /// <summary>Debug hook (null in production): camera, distance, world direction, hit.</summary>
        public static Action<SimCamera, double, Vector3D, bool> DebugCast;
        public SimCamera(BlockInst inst, World world) : base(inst, world) { }
        public bool EnableRaycast { get { return Raycasting; } set { Raycasting = value; } }
        public double AvailableScanRange { get { return Charge; } }
        public bool IsActive { get { return false; } }
        public float RaycastConeLimit { get { return (float)World.Settings.CameraConeLimitDeg; } }
        public double RaycastDistanceLimit { get { return World.Settings.CameraDistanceLimit; } }
        public float RaycastTimeMultiplier { get { return (float)World.Settings.CameraChargeMetersPerMs; } }

        bool Ready { get { return Raycasting && IsWorking; } }

        bool DistanceOk(double distance)
        {
            if (!(distance > 0) || double.IsInfinity(distance)) return false;
            double lim = World.Settings.CameraDistanceLimit;
            return lim < 0 || distance <= lim;
        }

        Vector3D ForwardWorld() { return Vector3D.TransformNormal(Inst.LocalMatrix.Forward, Inst.Body.WorldMatrix); }

        /// <summary>World direction for (pitch, yaw) degrees, or false when outside the cone.</summary>
        bool DirFromAngles(float pitchDeg, float yawDeg, out Vector3D dirWorld)
        {
            dirWorld = default(Vector3D);
            double lim = World.Settings.CameraConeLimitDeg;
            if (float.IsNaN(pitchDeg) || float.IsNaN(yawDeg) || Math.Abs(pitchDeg) > lim || Math.Abs(yawDeg) > lim) return false;
            double p = pitchDeg * (Math.PI / 180.0), y = yawDeg * (Math.PI / 180.0);
            // forward (0,0,-1) -> rotate about X by p: (0, sin p, -cos p); then about Y by y: x' = x cos y + z sin y, z' = -x sin y + z cos y
            if (World.Settings.CameraYawRight) y = -y;   // game: +yaw turns right
            double x0 = 0, y0 = Math.Sin(p), z0 = -Math.Cos(p);
            double cy = Math.Cos(y), sy = Math.Sin(y);
            var local = new Vector3D(x0 * cy + z0 * sy, y0, -x0 * sy + z0 * cy);   // camera-local, Forward = -Z
            // camera-local -> grid -> world. Local matrix rows are Right, Up, Backward (block axes in grid space).
            Vector3D g = Vector3D.TransformNormal(local, Inst.LocalMatrix);
            dirWorld = Vector3D.TransformNormal(g, Inst.Body.WorldMatrix);
            return true;
        }

        bool WithinCone(Vector3D dirWorld)
        {
            double len = dirWorld.Length();
            if (!(len > 1e-9)) return false;
            double c = Vector3D.Dot(dirWorld, ForwardWorld()) / len;
            return c >= Math.Cos(World.Settings.CameraConeLimitDeg * (Math.PI / 180.0)) - 1e-12;
        }

        public bool CanScan(double distance) { return Ready && DistanceOk(distance) && Charge >= distance; }
        /// <summary>The direction argument of the (distance, Vector3D) overloads as a world vector.</summary>
        Vector3D ArgToWorld(Vector3D d)
        {
            return World.Settings.CameraDirectionLocal ? Vector3D.TransformNormal(d, Inst.WorldMatrix) : d;
        }

        public bool CanScan(double distance, Vector3D direction) { return CanScan(distance) && WithinCone(ArgToWorld(direction)); }
        public bool CanScan(Vector3D target)
        {
            Vector3D d = target - Inst.WorldCenter;
            return CanScan(d.Length(), d);
        }

        public int TimeUntilScan(double distance)
        {
            if (!Raycasting) return int.MaxValue;
            if (Charge >= distance) return 0;
            double rate = World.Settings.CameraChargeMetersPerMs;
            if (!(rate > 0)) return int.MaxValue;
            double ms = Math.Ceiling((distance - Charge) / rate - 1e-6);
            return ms > int.MaxValue ? int.MaxValue : (int)ms;
        }

        MyDetectedEntityInfo Cast(double distance, Vector3D dirWorld)
        {
            double len = dirWorld.Length();
            if (!(len > 1e-9)) return default(MyDetectedEntityInfo);
            Vector3D dir = dirWorld / len;
            Charge -= distance;
            Scans++;
            RayHit hit;
            if (!Raycast_RayWorld(Inst.WorldCenter, dir, distance, out hit))
            {
                if (DebugCast != null) DebugCast(this, distance, dir, false);
                return default(MyDetectedEntityInfo);
            }
            Hits++;
            if (DebugCast != null) DebugCast(this, distance, dir, true);
            return Detection.FromRayHit(World, ref hit, Inst.OwnerId);
        }

        bool Raycast_RayWorld(Vector3D from, Vector3D dir, double dist, out RayHit hit)
        {
            if (World.Settings.CameraHitsOwnBody) return SeSim.Core.Raycast.RayWorldSkipBlock(World, from, dir, dist, Inst, out hit);
            return SeSim.Core.Raycast.RayWorld(World, from, dir, dist, Inst.Body, out hit);
        }

        public MyDetectedEntityInfo Raycast(double d, float pitch = 0, float yaw = 0)
        {
            if (!Ready || !DistanceOk(d) || Charge < d) return default(MyDetectedEntityInfo);
            Vector3D dir;
            if (!DirFromAngles(pitch, yaw, out dir)) return default(MyDetectedEntityInfo);
            return Cast(d, dir);
        }

        public MyDetectedEntityInfo Raycast(Vector3D target)
        {
            Vector3D d = target - Inst.WorldCenter;
            double dist = d.Length();
            if (!Ready || !DistanceOk(dist) || Charge < dist || !WithinCone(d)) return default(MyDetectedEntityInfo);
            return Cast(dist, d);
        }

        public MyDetectedEntityInfo Raycast(double distance, Vector3D direction)
        {
            Vector3D dw = ArgToWorld(direction);
            if (!Ready || !DistanceOk(distance) || Charge < distance || !WithinCone(dw)) return default(MyDetectedEntityInfo);
            return Cast(distance, dw);
        }

        /// <summary>Called once per tick by World (phase 9) for every camera in World.Cameras.</summary>
        public void TickCharge(double dt)
        {
            if (Inst.Destroyed) return;
            if (!Raycasting || !IsWorking) return;
            double max = World.Settings.CameraMaxCharge;
            double c = Charge + World.Settings.CameraChargeMetersPerMs * dt * 1000.0;
            Charge = c > max ? max : c;
        }
    }
}
