// SLOT A (physics + core + blueprint loader). See docs/sim-design.md.
// Every tunable/assumed constant lives here so a sensitivity run is a CLI flag, not a code edit.
// Other slots READ these; adding a field: ask slot A (or add it in your own slot's section below, marked with your slot).
using System;

namespace SeSim.Core
{
    public sealed class SimSettings
    {
        // ---- time ---------------------------------------------------------------------------------------
        public const int TicksPerSecond = 60;
        public const double Dt = 1.0 / TicksPerSecond;
        public int MaxTicks = 5 * 60 * TicksPerSecond;          // 5 minute cap

        // ---- physics (slot A) ---------------------------------------------------------------------------
        public double SmallCellSize = 0.5, LargeCellSize = 2.5;  // Data/Configuration.sbc
        public double SmallMaxSpeed = 100, LargeMaxSpeed = 100;  // m/s, Data/Environment.sbc
        public double SmallMaxAngularSpeed = 2 * Math.PI;        // rad/s (360 deg/s, Environment.sbc)
        public double LargeMaxAngularSpeed = Math.PI;            // rad/s (180 deg/s)
        // GYRO SIGN CONVENTION (docs/sim-design.md section 6). The gyro's override target angular velocity, in the
        // gyro block's local frame and RIGHT-HANDED, is (PitchSign*Pitch, YawSign*Yaw, RollSign*Roll).
        // Default -1,-1,-1: the scripts' g.Pitch=-l.X, g.Yaw=-l.Y, g.Roll=-l.Z (l = desired right-handed local rate)
        // then command exactly l, which is what the user observed in game (HANDOFF 3). Keen's old MyGyro.cs agrees on
        // Yaw and Roll (stored.Y = -Yaw, stored.Z = -Roll) and disagrees on Pitch (stored.X = +Pitch): --gyro-pitch-sign +1 tests that.
        public int GyroPitchSign = -1, GyroYawSign = -1, GyroRollSign = -1;
        public int GyroRampFrames = 120;                         // MyGridGyroSystem ACCELARION_RAMP_FRAMES = 60*2
        public double GyroMaxSlowdown = 0.93;                    // MyGridGyroSystem MAX_SLOWDOWN (non-override damping)
        public bool Collisions = true;                           // body-body contact impulses (no damage unless CollisionDamage)
        public bool CollisionDamage = false;
        public bool GridSplitting = true;                        // disconnected parts become separate bodies
        public int MaxBodies = 300;                              // above this many bodies, split-off parts with < 3 blocks are deleted (performance)
        public double ThrusterDampenerGain = 1.0;                // inertial dampeners (only when DampenersOverride is on)
        /// <summary>false (default): thrust acts through the centre of mass like the game (MyEntityThrustComponent applies the summed
        /// thrust at CenterOfMassWorld, so thruster placement never turns a ship). true: per-thruster r x F torque.</summary>
        public bool ThrusterTorque = false;
        public bool GyroscopicTerm = false;                      // add -w x (I w) to the angular acceleration (design: off, Havok-like approximation not needed)
        public double CollisionRestitution = 0.2;                // body-body contact impulse restitution
        public int CollisionMinBlocks = 4;                       // bodies with fewer live blocks (debris) do not collide
        public int CollisionEventCooldownTicks = 30;             // rate limit of EventKind.Collision per body pair
        public double CollisionDamageScale = 0.0005;             // block damage = scale * 0.5 * reducedMass * vn^2 (only if CollisionDamage)
        public double PowerUtilityDefaultMW = 0.0005;            // programmable block / antenna base draw when defs have no number [assumed, SE default]
        public double AntennaKWPerKm = 4.0;                      // antenna draw = Radius/250 kW (design 6.5): 4 kW per km

        // ---- power (slot A) ------------------------------------------------------------------------------
        public bool PowerEnabled = true;
        public double RailgunCapacitorMWh = 0.016;               // blueprint EntityCapacitorComponent StoredPower when full [I]
        public double RailgunChargeMW = 2.88;                    // [I] full charge in 20 s (wiki "~20 s"); see design doc
        public double CameraRaycastMWPerKm = 0;                  // camera power is negligible (3e-5 MW), modelled as constant sink

        // ---- sensing / comms (slot B) --------------------------------------------------------------------
        public double CameraChargeMetersPerMs = 2.0;             // RaycastTimeMultiplier 2.0 -> 2 km/s
        public double CameraConeLimitDeg = 45;                   // RaycastConeLimit (LargeCameraBlock value, assumed for small)
        public double CameraDistanceLimit = -1;                  // -1 = unlimited (RaycastDistanceLimit)
        public double CameraMaxCharge = double.MaxValue;         // [I] no cap documented
        public int SensorUpdateTicks = 10;
        public int IgcMaxWaitingMessages = 25;                   // [I]
        public bool IgcRequireAntenna = true;

        // ---- combat (slot C) -----------------------------------------------------------------------------
        public bool InfiniteAmmo = true;                         // research.md 4: creative world default
        public bool FriendlyFire = true;
        public double ExplosionFalloffPower = 1.0;               // damage * (1 - d/r)^p  [I]
        public double TurretTargetRefreshTicks = 10;
        public double RailgunInitialCharge = 1.0;                // [I] capacitor fraction at spawn (drones sit charged before a fight)
        public bool SpreadUniformInAngle = false;                // [I] false: shots uniform in solid angle inside the cone; true: uniform in elevation angle
        public double ProjectileShooterIgnoreS = 0.2;            // projectiles pass through the shooter's own body for this long
        public bool ProjectileSweepBodies = true;                // test the segment in the target's pose at tick start..end (no tick-quantised lag)
        public double TurretSlewRadPerSec = 1.0;                 // [I] large turret azimuth/elevation speed
        public double TurretMinElevationDeg = -10;               // [I] large turret elevation lower limit
        public double TurretFireConeDeg = 1.0;                   // turret fires when aim error is below this
        public double PirateCruiseSpeed = 40, PirateOrbitSpeed = 25, PirateOrbitRange = 700, PirateMaxTurnRate = 0.5;   // [I] pirate brain
        public double VanillaSearchIntervalS = 30;               // AI Offensive default search interval (MidgeNotes.md)
        public double VanillaFireConeDeg = 5;                    // vanilla AI fires fixed guns inside this cone
        public bool VanillaSearchOnActivate = true;              // [I] AI Offensive searches as soon as it is enabled (then every interval)
        public double VanillaSpeedLimit = 10;                    // AI Flight "Speed Limit" default 10 m/s (research.md 3.3; blueprints carry no component data)
        public double VanillaStayMin = 300, VanillaStayMax = 600; // [I] AI Offensive engagement band (pattern defaults unknown)
        public bool VanillaAimLead = true;                       // [I] AI aims fixed guns at the intercept point (false: at the target centre)
        public double VanillaTurnAccel = 2.0;                    // [I] AI turn profile: stop-able deceleration (rad/s^2)
        public double VanillaMaxTurnRate = 3.0;                  // [I] AI turn-rate cap (rad/s)

        // ---- scripts (slot D) ----------------------------------------------------------------------------
        public double FakeLastRunTimeMs = 0.05;                  // Runtime.LastRunTimeMs (constant, deterministic)
        public int FakeInstructionCount = 0;                     // Runtime.CurrentInstructionCount
        public int MaxInstructionCount = 50000;                  // wiki value
        public int MaxCallChainDepth = 1000;                     // [I]

        public SimSettings Clone() { return (SimSettings)MemberwiseClone(); }

        static System.Reflection.FieldInfo[] _fields;
        static System.Reflection.FieldInfo[] Fields()
        {
            var f = _fields;
            if (f == null)
            {
                var l = new System.Collections.Generic.List<System.Reflection.FieldInfo>(typeof(SimSettings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
                l.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                _fields = f = l.ToArray();
            }
            return f;
        }

        static string Norm(string s) { return s.TrimStart('-').Replace("-", "").Replace("_", "").ToLowerInvariant(); }

        /// <summary>Parses "--name value" pairs whose name matches a public field (kebab-case to PascalCase). Slot D calls it from the CLI.
        /// Accepts "gyro-pitch-sign", "--gyro-pitch-sign", "GyroPitchSign". Unknown name or unparsable value: ArgumentException.</summary>
        public void ApplyOverride(string name, string value)
        {
            string n = Norm(name);
            foreach (var f in Fields())
            {
                if (Norm(f.Name) != n) continue;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                try
                {
                    if (f.FieldType == typeof(bool))
                    {
                        string v = (value ?? "true").Trim().ToLowerInvariant();
                        if (v == "true" || v == "1" || v == "on" || v == "yes") f.SetValue(this, true);
                        else if (v == "false" || v == "0" || v == "off" || v == "no") f.SetValue(this, false);
                        else throw new FormatException("bool expected");
                    }
                    else if (f.FieldType == typeof(int)) f.SetValue(this, int.Parse(value, ci));
                    else if (f.FieldType == typeof(long)) f.SetValue(this, long.Parse(value, ci));
                    else if (f.FieldType == typeof(double)) f.SetValue(this, double.Parse(value, System.Globalization.NumberStyles.Float, ci));
                    else if (f.FieldType == typeof(float)) f.SetValue(this, float.Parse(value, System.Globalization.NumberStyles.Float, ci));
                    else if (f.FieldType == typeof(string)) f.SetValue(this, value);
                    else throw new ArgumentException("unsupported setting type " + f.FieldType.Name);
                }
                catch (FormatException) { throw new ArgumentException("bad value '" + value + "' for setting " + f.Name); }
                return;
            }
            throw new ArgumentException("unknown setting '" + name + "'");
        }

        public bool IsSetting(string name)
        {
            string n = Norm(name);
            foreach (var f in Fields()) if (Norm(f.Name) == n) return true;
            return false;
        }

        /// <summary>All settings as "Name=value" lines sorted by name (invariant culture). For meta.json and the settings hash.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in Fields())
            {
                object v = f.GetValue(this);
                sb.Append(f.Name).Append('=').Append(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>64-bit FNV-1a of Describe(), hex.</summary>
        public string Hash()
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in Describe()) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16");
        }
    }
}
