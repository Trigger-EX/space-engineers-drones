// SLOT A. The world and the fixed 60 Hz tick (docs/sim-design.md section 5 "Tick order"). No sub-stepping.
using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;

namespace SeSim.Core
{
    /// <summary>Anything that thinks without a PB: pirate ship, vanilla AI blocks (slot C). Runs in phase 3 of the tick.</summary>
    public interface IShipBrain { void Tick(World w); }

    public sealed class World
    {
        public readonly SimSettings Settings;
        public readonly DetRng Rng;            // engine stream (spread, damage rolls...): fork per subsystem
        public readonly ulong Seed;
        public long Tick;                      // ticks completed
        public double Time { get { return Tick * SimSettings.Dt; } }
        public long TimeMs { get { return (long)Math.Round(Tick * 1000.0 / SimSettings.TicksPerSecond); } }

        public readonly List<Body> Bodies = new List<Body>();
        public readonly IdGen Ids;
        public readonly TeamTable Teams = new TeamTable();
        public readonly SeSim.Blueprint.DefDb Defs;
        public IEventSink Events = NullEventSink.Instance;     // slot D sets Metrics here

        // subsystems (created in the constructor; owners in brackets)
        public SeSim.Api.IgcHub Igc;                    // [B]
        public SeSim.Combat.ProjectileSystem Projectiles;  // [C]
        public SeSim.Combat.CombatSystem Combat;        // [C] weapons, turrets, warheads, explosions
        public SeSim.Run.ScriptHost Scripts;            // [D] set by the scenario runner after construction
        public readonly List<IShipBrain> Brains = new List<IShipBrain>();
        public readonly List<SeSim.Api.SimSensor> Sensors = new List<SeSim.Api.SimSensor>();   // [B] polled every SensorUpdateTicks
        public readonly List<SeSim.Api.SimCamera> Cameras = new List<SeSim.Api.SimCamera>();   // [B] charge accumulation
        /// <summary>M0: every block with an entity id by that id (filled by GridFactory; destroyed blocks stay, flagged Destroyed). Toolbar and Event Controller targets.</summary>
        public readonly Dictionary<long, BlockInst> BlocksById = new Dictionary<long, BlockInst>();
        /// <summary>M0: timers and event controllers that run block automation (SimSettings.BlockAutomation); empty otherwise.</summary>
        public readonly List<SeSim.Api.IBlockAutomation> Automation = new List<SeSim.Api.IBlockAutomation>();

        /// <summary>Physics-only worlds (selftests): no IGC / projectiles / combat subsystems are created.</summary>
        public readonly bool Minimal;
        /// <summary>True: GridFactory does not create slot B API objects (BlockInst.Api stays null). Selftests of the physics core.</summary>
        public bool SkipApi;
        /// <summary>Rate limiter for Collision events: pair key -> last tick.</summary>
        public readonly Dictionary<long, long> CollisionLastTick = new Dictionary<long, long>();
        /// <summary>Contact-episode bookkeeping (SimSettings.CollisionEpisodeEvents): pair key -> an episode started and no event was emitted yet.</summary>
        public readonly Dictionary<long, bool> CollisionEpisodePending = new Dictionary<long, bool>();
        public readonly List<BlockInst> CollisionScratch = new List<BlockInst>();
        /// <summary>Id of the projectile whose impact is being resolved (set by ProjectileSystem.Step), 0 outside. Damage.EmitHit stamps it on Hit events.</summary>
        public long CurrentShotId;
        int _nextBodyId;

        public World(SimSettings settings, SeSim.Blueprint.DefDb defs, ulong seed) : this(settings, defs, seed, false) { }

        public World(SimSettings settings, SeSim.Blueprint.DefDb defs, ulong seed, bool minimal)
        {
            Settings = settings; Defs = defs; Seed = seed; Minimal = minimal;
            Rng = new DetRng(DetRng.Mix(seed, 0xE1));
            Ids = new IdGen(DetRng.Mix(seed, 0x1D));
            if (!minimal)
            {
                Igc = new SeSim.Api.IgcHub(this);
                Combat = new SeSim.Combat.CombatSystem(this);
                Projectiles = new SeSim.Combat.ProjectileSystem(this, 4096);
            }
        }

        /// <summary>One 60 Hz tick:
        /// 1 scripts (ScriptHost.RunDue) -> 2 IGC delivery -> 3 brains + turret AI -> 4 weapons fire -> 5 power ->
        /// 6 thrusters, gyros, integrate, collisions -> 7 projectiles + explosions + warheads -> 8 removals, splits, mass ->
        /// 9 cameras charge, sensors -> 10 Tick++ and Events.EndTick.</summary>
        public void Step()
        {
            const double dt = SimSettings.Dt;
            // 1-2 scripts, IGC
            if (Automation.Count > 0 && Settings.BlockAutomation) for (int i = 0; i < Automation.Count; i++) Automation[i].Tick(Tick);   // timers / event controllers, M0
            if (Scripts != null) Scripts.RunDue();
            if (Igc != null) Igc.Deliver();
            if (Igc != null && Settings.SwarmCueSeconds > 0 && Tick % Math.Max(1, (long)Math.Round(Settings.SwarmCueSeconds * SimSettings.TicksPerSecond)) == 0)
                Igc.InjectTeamCue("DRONE_CUE", Settings.SwarmCueTeam);
            // 3 brains + turret AI
            for (int i = 0; i < Brains.Count; i++) Brains[i].Tick(this);
            if (Combat != null) Combat.UpdateTurrets(dt);
            // 4 weapons fire, warhead countdowns
            if (Combat != null) { Combat.UpdateWeapons(dt); Combat.UpdateWarheads(dt); }
            // 5 power
            int n = Bodies.Count;
            for (int i = 0; i < n; i++) { var b = Bodies[i]; if (b.Alive && b.Power != null) b.Power.Update(dt); }
            // 6 forces, integration, collisions
            for (int i = 0; i < n; i++)
            {
                var b = Bodies[i];
                if (!b.Alive) continue;
                Physics.ApplyThrusters(b, Settings);
                Physics.ApplyGyros(b, Settings);
                Physics.Integrate(b, Settings, dt);
            }
            Physics.ResolveCollisions(this);
            // 7 projectiles, explosions
            if (Projectiles != null) Projectiles.Step(dt);
            // 8 removals, splits, mass properties, power rebuild
            ApplyRemovals();
            // 9 sensing
            for (int i = 0; i < Cameras.Count; i++) Cameras[i].TickCharge(dt);
            if (Sensors.Count > 0 && Settings.SensorUpdateTicks > 0 && Tick % Settings.SensorUpdateTicks == 0)
                for (int i = 0; i < Sensors.Count; i++) Sensors[i].Poll();
            // 10
            Tick++;
            Events.EndTick(this);
        }

        /// <summary>Phase 8. Also callable from tests after RemoveBlock.</summary>
        public void ApplyRemovals()
        {
            int n = Bodies.Count;                       // splits append new bodies; they are already consistent
            for (int i = 0; i < n; i++)
            {
                var b = Bodies[i];
                if (!b.Alive) continue;
                if (b.ConnectivityDirty) Connectivity.SplitIfNeeded(this, b);
                if (b.MassDirty) b.RecomputeMassProperties();
                if (b.PowerDirty && b.Power != null) b.Power.Rebuild();
            }
            for (int i = Bodies.Count - 1; i >= 0; i--)
                if (Bodies[i].Blocks.Count == 0) RemoveBody(Bodies[i]);
        }

        public Body AddBody(Body b)
        {
            b.World = this;
            b.Id = _nextBodyId++;
            b.Alive = true;
            Bodies.Add(b);
            return b;
        }

        public void RemoveBody(Body b)
        {
            b.Alive = false;
            Bodies.Remove(b);
        }

        /// <summary>Bodies whose bounding sphere intersects the sphere; fills 'result' (cleared first), no allocation.</summary>
        public void BodiesInSphere(Vector3D center, double radius, List<Body> result)
        {
            result.Clear();
            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                if (!b.Alive || b.Blocks.Count == 0) continue;
                double dx = b.WorldBoundCenter.X - center.X, dy = b.WorldBoundCenter.Y - center.Y, dz = b.WorldBoundCenter.Z - center.Z;
                double rr = radius + b.BoundRadius;
                if (dx * dx + dy * dy + dz * dz <= rr * rr) result.Add(b);
            }
        }
    }

    /// <summary>Teams and identities. Identity of team t = TeamTable.IdentityBase + t; every ship of a team is owned by it.</summary>
    public sealed class TeamTable
    {
        public const long IdentityBase = 144115188075855872L;   // looks like an SE identity id; never 0
        public int Count;
        public MyRelationsBetweenPlayerAndBlock[,] Rel = new MyRelationsBetweenPlayerAndBlock[0, 0];  // [observerTeam, targetTeam]
        public long IdentityOf(int team) { return IdentityBase + team; }
        public int TeamOf(long identity) { long t = identity - IdentityBase; return (t >= 0 && t < Count) ? (int)t : -1; }

        /// <summary>Relation of a block owned by 'observerOwner' to a grid/block owned by 'targetOwner' (0 = NoOwnership,
        /// same identity = Owner, otherwise Rel[team, team]: Enemies by default, FactionShare for allies).</summary>
        public MyRelationsBetweenPlayerAndBlock Relation(long observerOwner, long targetOwner)
        {
            if (targetOwner == 0) return MyRelationsBetweenPlayerAndBlock.NoOwnership;
            if (observerOwner == targetOwner) return MyRelationsBetweenPlayerAndBlock.Owner;
            int o = TeamOf(observerOwner), t = TeamOf(targetOwner);
            if (o < 0 || t < 0) return observerOwner == 0 ? MyRelationsBetweenPlayerAndBlock.Neutral : MyRelationsBetweenPlayerAndBlock.Enemies;
            return Rel[o, t];
        }

        /// <summary>Resets to 'teams' teams that are all enemies of each other (a team is Owner of itself).</summary>
        public void Init(int teams)
        {
            Count = teams;
            Rel = new MyRelationsBetweenPlayerAndBlock[teams, teams];
            for (int i = 0; i < teams; i++)
                for (int j = 0; j < teams; j++)
                    Rel[i, j] = i == j ? MyRelationsBetweenPlayerAndBlock.Owner : MyRelationsBetweenPlayerAndBlock.Enemies;
        }

        /// <summary>Makes sure at least 'teams' teams exist, keeping the relations already set.</summary>
        public void Ensure(int teams)
        {
            if (teams <= Count) return;
            var old = Rel; int oc = Count;
            Init(teams);
            for (int i = 0; i < oc; i++) for (int j = 0; j < oc; j++) Rel[i, j] = old[i, j];
        }

        /// <summary>Symmetric alliance: both teams see each other as FactionShare.</summary>
        public void SetAllied(int a, int b)
        {
            Ensure(Math.Max(a, b) + 1);
            if (a == b) return;
            Rel[a, b] = MyRelationsBetweenPlayerAndBlock.FactionShare;
            Rel[b, a] = MyRelationsBetweenPlayerAndBlock.FactionShare;
        }
    }

    /// <summary>Deterministic, world-unique entity ids (SE ids are large positive longs).</summary>
    public sealed class IdGen
    {
        ulong _state;
        public IdGen(ulong seed) { _state = seed; }
        public long Next() { ulong v = DetRng.SplitMix(ref _state); return (long)(v & 0x3FFFFFFFFFFFFFFFUL) | 0x0100000000000000L; }
    }
}
