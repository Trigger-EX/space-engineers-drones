// SLOT B. IMyCubeGrid per Body, IMySlimBlock per BlockInst (lazy), IMyInventory (volume/mass only).
using System;
using VRageMath;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRage.ObjectBuilders;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimCubeGrid : IMyCubeGrid
    {
        public Body Body;
        public string CustomNameValue;
        static readonly VRage.Game.Components.MyEntityComponentContainer EmptyComponents = new VRage.Game.Components.MyEntityComponentContainer();
        public SimCubeGrid(Body body) { Body = body; }

        public VRage.Game.Components.MyEntityComponentContainer Components { get { return EmptyComponents; } }
        public MatrixD WorldMatrix { get { return Body.WorldMatrix; } }
        public long EntityId { get { return Body.EntityId; } }
        public Vector3D GetPosition() { return Body.WorldMatrix.Translation; }
        public IMyInventory GetInventory() { return null; }
        public IMyInventory GetInventory(int i) { return null; }
        public int InventoryCount { get { return 0; } }
        public bool HasInventory { get { return false; } }
        /// <summary>Bounding sphere of all blocks (the game's grid WorldVolume).</summary>
        public BoundingSphereD WorldVolume { get { return new BoundingSphereD(Body.LocalToWorld(Body.BoundCenterLocal), Body.BoundRadius); } }
        public BoundingBoxD WorldAABB { get { return Body.WorldAabb(); } }
        public string DisplayName { get { return Body.DisplayName; } }
        public string Name { get { return Body.EntityId.ToString(); } }
        public bool Closed { get { return !Body.Alive; } }
        public MyCubeSize GridSizeEnum { get { return Body.GridSize; } }
        public float GridSize { get { return (float)Body.CellSize; } }
        public bool IsStatic { get { return Body.IsStatic; } }
        public string CustomName { get { return CustomNameValue ?? Body.DisplayName; } set { CustomNameValue = value; } }
        public Vector3I Min { get { return Body.CellMin; } }
        public Vector3I Max { get { return Body.CellMax; } }
        public bool CubeExists(Vector3I p) { return Body.BlockAtCell(p) != null; }

        public IMySlimBlock GetCubeBlock(Vector3I p)
        {
            BlockInst b = Body.BlockAtCell(p);
            if (b == null) return null;
            return b.Slim ?? (b.Slim = new SimSlimBlock(b));
        }

        public Vector3D GridIntegerToWorld(Vector3I c)
        {
            double s = Body.CellSize;
            return Vector3D.Transform(new Vector3D(c.X * s, c.Y * s, c.Z * s), Body.WorldMatrix);
        }

        public Vector3I WorldToGridInteger(Vector3D w)
        {
            Vector3D l = Body.WorldToLocal(w); double inv = 1.0 / Body.CellSize;
            return new Vector3I((int)Math.Round(l.X * inv), (int)Math.Round(l.Y * inv), (int)Math.Round(l.Z * inv));
        }

        public bool IsSameConstructAs(IMyCubeGrid other)
        {
            var o = other as SimCubeGrid;
            return o != null && o.Body == Body;
        }
    }

    /// <summary>Slim block: integrity view. BuildIntegrity = MaxHp (blocks spawn fully built); CurrentDamage = MaxHp - Hp.</summary>
    public sealed class SimSlimBlock : IMySlimBlock
    {
        public readonly BlockInst Inst;
        public SimSlimBlock(BlockInst inst) { Inst = inst; }
        public float BuildIntegrity { get { return Inst.MaxHp; } }
        public float CurrentDamage { get { return Inst.Destroyed ? Inst.MaxHp : Inst.MaxHp - Inst.Hp; } }
        public float MaxIntegrity { get { return Inst.MaxHp; } }
        public float AccumulatedDamage { get { return 0f; } }   // deformation off
        public float DamageRatio { get { return Inst.MaxHp > 0 ? CurrentDamage / Inst.MaxHp : 0f; } }
        public bool IsDestroyed { get { return Inst.Destroyed; } }
        public bool IsFullIntegrity { get { return Inst.Hp >= Inst.MaxHp; } }
        public float Mass { get { return Inst.Mass; } }
        public Vector3I Position { get { return Inst.Position; } }
        public IMyCubeBlock FatBlock { get { return Inst.Api; } }
        public IMyCubeGrid CubeGrid { get { return Inst.Body.ApiGrid; } }
        public SerializableDefinitionId BlockDefinition
        {
            get
            {
                string t = Inst.Def.TypeId ?? "";
                if (!t.StartsWith("MyObjectBuilder_", StringComparison.Ordinal)) t = "MyObjectBuilder_" + t;
                return new SerializableDefinitionId(t, Inst.Def.Subtype ?? "");
            }
        }
    }

    /// <summary>Inventory view: only volume and mass are modelled (no items). Values in m3 / kg.</summary>
    public sealed class SimInventory : IMyInventory
    {
        public double MaxVolumeM3, CurrentVolumeM3, CurrentMassKg; public int Items;
        public MyFixedPoint CurrentVolume { get { return (MyFixedPoint)CurrentVolumeM3; } }
        public MyFixedPoint MaxVolume { get { return (MyFixedPoint)MaxVolumeM3; } }
        public MyFixedPoint CurrentMass { get { return (MyFixedPoint)CurrentMassKg; } }
        public int ItemCount { get { return Items; } }
    }
}
