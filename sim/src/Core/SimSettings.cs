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
        /// <summary>Kinetic-energy collision damage between bodies (sim-audit-1 section 7.7): each body takes
        /// CollisionDamageScale * 0.5 * reducedMass * vn^2 (vn = closing speed along the contact normal), shared by the blocks within
        /// CollisionDamagePatchM of the contact point. Applies to teammates too (a collision is not weapon friendly fire, so it ignores
        /// FriendlyFire=false). false = the old behaviour (impulses only).</summary>
        public bool CollisionDamage = true;
        public double CollisionDamagePatchM = 1.5;               // [I] blocks within this distance of the contact point share the damage (at least the deepest block)
        /// <summary>true (default): one Collision event per contact EPISODE (first closing contact after at least CollisionEpisodeGapTicks
        /// without contact between the pair); false: the old one event per CollisionEventCooldownTicks of contact.</summary>
        public bool CollisionEpisodeEvents = true;
        public int CollisionEpisodeGapTicks = 30;                // contact episode ends after this many ticks without contact (0.5 s)
        public bool GridSplitting = true;                        // disconnected parts become separate bodies
        public int MaxBodies = 300;                              // above this many bodies, split-off parts with < 3 blocks are deleted (performance)
        public double ThrusterDampenerGain = 1.0;                // inertial dampeners (only when DampenersOverride is on)
        /// <summary>false (default): thrust acts through the centre of mass like the game (MyEntityThrustComponent applies the summed
        /// thrust at CenterOfMassWorld, so thruster placement never turns a ship). true: per-thruster r x F torque.</summary>
        public bool ThrusterTorque = false;
        /// <summary>[I] Large turret engagement range = its ammo's max range (artillery 2000 m, assault cannon 1400 m) when true;
        /// a flat 800 m (the turret blocks' MaxRangeMeters) when false. Gatling/rocket turrets are 800 m either way.</summary>
        public bool TurretRangeFromAmmo = true;
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
        public double RailgunChargeMW = 2.88;                    // small railgun stored-power rate: RechargeDraw 3.6 MW * ChargeMultiplier 0.8 (EntityComponents.sbc, SmallRailgun) = 20 s for 0.016 MWh
        /// <summary>true (default): the railgun capacitor uses Data/EntityComponents.sbc numbers: the block REQUESTS RechargeDraw (small 3.6 MW,
        /// large 38 MW) from the DefenseAdative group and stores supplied * ChargeMultiplier (0.8) until Capacity is full (small 0.016 MWh = 20 s,
        /// large 0.5 MWh = 59.2 s). false: the old model (request = RailgunChargeMW, store = supplied, large = 3 x small capacity, 60 s).</summary>
        public bool RailgunCapacitorFromData = true;
        public double RailgunRechargeDrawSmallMW = 3.6, RailgunRechargeDrawLargeMW = 38;   // EntityCapacitorComponent RechargeDraw
        public double RailgunChargeMultiplier = 0.8;                                          // EntityCapacitorComponent ChargeMultiplier
        public double RailgunCapacityLargeMWh = 0.5;                                          // EntityCapacitorComponent Capacity, LargeRailgun
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
        /// <summary>Large turrets are Defense power sinks like in the game: a ship whose reactors, engines and batteries are all gone has
        /// silent turrets. False = turrets work without power (old model).</summary>
        public bool TurretsNeedPower = true;
        public double TurretIdlePowerMW = 0.002;                 // [I] per turret when the definition has no power value
        /// <summary>Turrets honour the targeting group ("Weapons", "Propulsion", "PowerSystems"; set in the terminal or by
        /// IMyLargeTurretBase.SetTargetingGroup): they aim at the nearest functional block of that group on their target instead of
        /// its centre (a working decoy still wins). Default "" = the centre, as before.</summary>
        public bool TurretSubsystemTargeting = true;
        public double PirateCruiseSpeed = 40, PirateOrbitSpeed = 25, PirateOrbitRange = 700, PirateMaxTurnRate = 0.5;   // [I] pirate brain
        /// <summary>Pirate evasive manoeuvres (like the vanilla AI's "evasive" option): strafe at PirateEvasiveSpeed across the line
        /// of sight, flipping every 2-5 s, on top of its orbit. Off by default (the matrix results so far are without it).</summary>
        public bool PirateEvasive = false;
        public double PirateEvasiveSpeed = 30;
        /// <summary>Rockets (Missile ammo with explosion) at launch [I]: 0 = the old model (barrel direction x MissileInitialSpeed, ship
        /// velocity ignored); 1 = ship velocity + barrel x MissileInitialSpeed, accelerating along the barrel with the total speed capped at
        /// DesiredSpeed, so a rocket fired from a ship moving sideways curves onto the barrel line (Keen bug tracker "Rocket doesn't move in
        /// a straight line": fired from a moving ship it curves; answer: the rocket's max speed does not stack with the ship's velocity);
        /// 2 = full Galilean inheritance (ship velocity kept, own speed on top).</summary>
        public int RocketInherit = 1;
        public double VanillaSearchIntervalS = 30;               // AI Offensive default search interval (MidgeNotes.md)
        public double VanillaFireConeDeg = 5;                    // vanilla AI fires fixed guns inside this cone
        public bool VanillaSearchOnActivate = true;              // [I] AI Offensive searches as soon as it is enabled (then every interval)
        /// <summary>The AI blocks on SCRIPT ships never act, although the script switches them on (in-game report 2026-10-13: the blocks come
        /// out of a blueprint with "AI Behavior" off and default settings, so a pasted Full drone's AI does nothing). Worst case for the
        /// scripts' hybrid search; scriptless Midges are not affected.</summary>
        public bool ScriptAiBlocksInert = false;
        /// <summary>AI blocks act only while their "AI Behavior" switch (terminal bool "ActivateBehavior") is on; blueprints load it off,
        /// so a script has to switch it on (the AI GATE block in the drone scripts does). False: Enabled alone is enough (old model).</summary>
        public bool AiBehaviorNeedsSwitch = false;
        /// <summary>AI Offensive enemy search radius, metres from the grid. Data/EntityComponents.sbc SearchEnemyComponent "DefaultEnemySearch"
        /// SearchRadius = 2500 (the container of both OffensiveCombat blocks forces that component; wiki "2500 m"). Every default start
        /// distance above this (up to 3000 m in ScenarioGen) is therefore dead on arrival for an AI-only ship. Use 3500 to make every spawn acquirable
        /// (a what-if, not game behaviour).</summary>
        public double VanillaSearchRange = 2500;
        /// <summary>AI Flight "Speed Limit" for scriptless/hybrid ships. Data/EntityComponents.sbc AutopilotComponent SmallFlightMovement/LargeFlightMovement
        /// carries AutopilotSpeedLimit 100, which is the slider MAXIMUM (Environment.sbc ship max 100; the Remote Control definition has the same 100);
        /// the wiki and scripts/MidgeNotes.md give the block's default as 10 m/s and say to raise it. A blueprint cannot preset it. Kept at 10 (what a
        /// pasted block does); --vanilla-speed-limit 100 is the what-if for a player who raised the slider.</summary>
        public double VanillaSpeedLimit = 10;
        public double VanillaStayMin = 300, VanillaStayMax = 600; // [I] AI Offensive engagement band (pattern defaults unknown)
        public bool VanillaAimLead = true;                       // [I] AI aims fixed guns at the intercept point (false: at the target centre)
        public double VanillaTurnAccel = 2.0;                    // [I] AI turn profile: stop-able deceleration (rad/s^2)
        public double VanillaMaxTurnRate = 3.0;                  // [I] AI turn-rate cap (rad/s)

        // ---- decoys (sim-audit-1 section 7.4) ------------------------------------------------------------------
        /// <summary>Decoy blocks work as in the game (wiki: enemy turrets and homing missiles prefer decoys; Data/CubeBlocks_Weapons.sbc
        /// Decoy has PriorityModifier 10 and NotWorkingPriorityMultiplier 0, i.e. a switched-off or non-functional decoy has priority 0).
        /// Model: an enemy shooter whose automatic targeting (large turrets, Custom Turret Controller designation, the AI block stand-in)
        /// can choose between bodies prefers a body with a working decoy (DecoyPreferTarget), and aims at the nearest working decoy block
        /// instead of the body centre (DecoyAimProbability). Rockets in the sim are unguided, so a "homing missile" is modelled as the
        /// AI/turret rocket aim point only. Scripts that aim from their own camera rays are not affected. false: decoys are inert (old).</summary>
        public bool DecoysFunctional = true;
        public bool DecoyPreferTarget = true;                    // bodies with a working decoy are chosen before bodies without, if in range
        public double DecoyAimProbability = 1.0;                 // [I] probability that the aim point is the decoy; rolled per (shooter, target) every DecoyRerollS
        public double DecoyRerollS = 10;

        // ---- metrics (slot D) -----------------------------------------------------------------------------------
        /// <summary>true (default): ShipStats.Hits counts projectiles that hit an enemy at least once (a piercing slug or a splash that
        /// damages several bodies/ticks is one hit), so hit_rate = hits/shots is at most 1. The old event count stays as hits_raw.</summary>
        public bool HitCountUnique = true;

        // ---- scripts (slot D) ----------------------------------------------------------------------------
        public double FakeLastRunTimeMs = 0.05;                  // Runtime.LastRunTimeMs (constant, deterministic)
        public int FakeInstructionCount = 0;                     // Runtime.CurrentInstructionCount
        public int MaxInstructionCount = 50000;                  // wiki value
        public int MaxCallChainDepth = 1000;                     // [I]
        /// <summary>Swarm cue (test harness for the kamikaze scenarios, docs/kamikaze-defence.md): every SwarmCueSeconds (0 = off) each PB of
        /// team SwarmCueTeam that listens to "DRONE_CUE" receives the true position / velocity / bounding radius of every enemy ship, as a
        /// carrier's radar or turret designators would feed a Stinger swarm (Stingers only see 50 m by sensor on their own).</summary>
        public double SwarmCueSeconds = 0;
        public int SwarmCueTeam = 1;

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
