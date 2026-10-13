// SLOT A. One placed block of a body. Plain data; hot loops index Body.Blocks by BlockInst.Index.
using System;
using VRageMath;
using SeSim.Blueprint;

namespace SeSim.Core
{
    public sealed class BlockInst
    {
        public int Index;                 // index in Body.Blocks (re-numbered when a body splits)
        public BlockDef Def;
        public BlockData Src;             // blueprint data (CustomName, Props...); never mutated
        public long EntityId;             // remapped per spawn (GridFactory), unique in the World
        public int ConstructId;           // index of the source blueprint grid in its ShipInstance (IsSameConstructAs after merge)
        public Vector3I Min, Max;         // occupied cells (grid integer coords)
        public Vector3I Position;         // = Min (documented simplification of Keen's pivot cell)
        public MyBlockOrientation Orientation;
        public MatrixD LocalMatrix;       // rotation + translation of block center, grid-local metres (OrientMath.LocalMatrix)
        public Vector3D LocalCenter;      // = LocalMatrix.Translation
        public Vector3D HalfExtents;      // half size in grid axes, metres (for ray/explosion tests)
        public MountRect[] MountRects;    // Connectivity cache: grid-space mount rectangles (computed lazily, grid frame never changes)
        public double Radius;             // |HalfExtents| (bounding sphere radius of the block box)

        public float Mass;                // kg (Def.MassKg; + inventory mass later)
        public float MaxHp;               // Def.MaxIntegrity
        public float CriticalHp;          // Def.CriticalIntegrity: at or below this the block is non-functional
        public float Hp;                  // current integrity; 0 = destroyed
        public bool Destroyed;

        public long OwnerId;              // owning identity (team identity); 0 = nobody (armor)
        public Body Body;                 // current body

        // Behaviour components (null when not applicable). Created by GridFactory/BlockApiFactory at spawn.
        public ThrusterUnit Thruster;     // slot A
        public GyroUnit Gyro;             // slot A
        public BatteryUnit Battery;       // slot A
        public PowerSink Sink;            // slot A (consumer record for PowerSystem)
        public SeSim.Combat.WeaponUnit Weapon;   // slot C
        public SeSim.Combat.WarheadUnit Warhead; // slot C
        public SeSim.Combat.TurretUnit Turret;   // slot C (large turrets and turret controllers)
        public MergeUnit Merge;           // slot A (connectivity edge)
        public SeSim.Api.SimTerminalBlock Api;   // slot B; null for blocks without terminal (armor)
        public ToolbarItem[] Toolbar;     // M0: toolbar slots with BlockId remapped to this world's entity ids (null = none)
        public long[] SelectedIds;        // M0: Event Controller SelectedBlocks, remapped (null = none)
        public SeSim.Api.SimSlimBlock Slim;      // slot B; created lazily by SimCubeGrid.GetCubeBlock

        /// <summary>Functional = built above critical integrity (Hp > CriticalHp) and not destroyed (IMyCubeBlock.IsFunctional).</summary>
        public bool IsFunctional { get { return !Destroyed && Hp > CriticalHp; } }

        /// <summary>World position of the block center (uses the body's cached world matrix).</summary>
        public Vector3D WorldCenter { get { return Body.LocalToWorld(LocalCenter); } }

        /// <summary>Block world matrix = LocalMatrix * Body.WorldMatrix (IMyEntity.WorldMatrix of the block).</summary>
        public MatrixD WorldMatrix { get { return LocalMatrix * Body.WorldMatrix; } }
    }

    /// <summary>Merge block state used by Connectivity (slot A) and SimMergeBlock (slot B).</summary>
    public sealed class MergeUnit
    {
        public BlockInst Block;
        public Base6Directions.Direction FaceLocal = Base6Directions.Direction.Up; // research.md 1.1: 1x1x1 merges toward Up
        public MergeUnit Partner;          // the merge block it is locked to (null = not connected)
        public MergeUnit Adjacent;         // the merge block whose face touches ours (set once at spawn, never cleared): the
                                           // pair is NOT an adjacency edge in Connectivity unless Partner == Adjacent (connected)
        public bool IsConnected { get { return Partner != null && Block.IsFunctional && Partner.Block.IsFunctional; } }
    }
}
