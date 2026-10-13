// SLOT B. Base API classes. Every terminal block of a body gets exactly one API object (BlockInst.Api), created by
// BlockApiFactory at spawn. API objects are thin: state that physics/combat needs lives in Core/Combat units.
//
// Note on units: GridFactory attaches combat units (Weapon/Turret/Warhead) AFTER BlockApiFactory.Create, so API classes never
// capture a unit reference in their constructor; they read it through BlockInst each time (the `Unit` members are properties).
using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRage.ObjectBuilders;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    /// <summary>Slot-B tunables that have no SimSettings field (SimSettings belongs to slot A). Set once before runs start; not
    /// per-World, so they must not change while worlds are running. Request: slot A may move them into SimSettings.</summary>
    public static class ApiTuning
    {
        /// <summary>Cargo containers report this fraction of their volume as filled (HiveMother's [Stock] check wants non-empty).</summary>
        public static double CargoFillFraction = 0.5;
        /// <summary>Projector+welder printing speed (blocks per second) for the minimal printer model [I].</summary>
        public static double PrintBlocksPerSecond = 2.0;
        /// <summary>Largest antenna radius the game allows for a small-grid radio antenna (m).</summary>
        public static double MaxAntennaRadius = 50000;
        /// <summary>false (default): IGC between enemy teams is never delivered (the game's antenna relay needs non-hostile owners).</summary>
        public static bool IgcAllowEnemyRelay = false;
    }

    public abstract class SimCubeBlock : IMyCubeBlock
    {
        public readonly BlockInst Inst;
        public readonly World World;
        SerializableDefinitionId _defId; bool _defIdSet;
        static readonly VRage.Game.Components.MyEntityComponentContainer EmptyComponents = new VRage.Game.Components.MyEntityComponentContainer();

        protected SimCubeBlock(BlockInst inst, World world) { Inst = inst; World = world; }

        // IMyEntity
        public VRage.Game.Components.MyEntityComponentContainer Components { get { return EmptyComponents; } }
        public MatrixD WorldMatrix { get { return Inst.WorldMatrix; } }
        public long EntityId { get { return Inst.EntityId; } }
        public Vector3D GetPosition() { return Inst.WorldCenter; }
        public virtual IMyInventory GetInventory() { return GetInventory(0); }
        public virtual IMyInventory GetInventory(int i) { return null; }
        public virtual int InventoryCount { get { return 0; } }
        public virtual bool HasInventory { get { return InventoryCount > 0; } }

        public BoundingSphereD WorldVolume
        {
            get
            {
                Vector3D h = Inst.HalfExtents;
                return new BoundingSphereD(Inst.WorldCenter, Math.Sqrt(h.X * h.X + h.Y * h.Y + h.Z * h.Z));
            }
        }

        public BoundingBoxD WorldAABB
        {
            get
            {
                // half extents are in grid axes; rotate into world by |R| (exact AABB of the rotated box)
                MatrixD r = Inst.Body.RotMatrix; Vector3D h = Inst.HalfExtents, c = Inst.WorldCenter;
                double hx = Math.Abs(r.M11) * h.X + Math.Abs(r.M21) * h.Y + Math.Abs(r.M31) * h.Z;
                double hy = Math.Abs(r.M12) * h.X + Math.Abs(r.M22) * h.Y + Math.Abs(r.M32) * h.Z;
                double hz = Math.Abs(r.M13) * h.X + Math.Abs(r.M23) * h.Y + Math.Abs(r.M33) * h.Z;
                return new BoundingBoxD(new Vector3D(c.X - hx, c.Y - hy, c.Z - hz), new Vector3D(c.X + hx, c.Y + hy, c.Z + hz));
            }
        }

        public virtual string DisplayName { get { return DefinitionDisplayNameText; } }
        public string Name { get { return Inst.EntityId.ToString(); } }
        public bool Closed { get { return Inst.Destroyed; } }

        // IMyCubeBlock
        public SerializableDefinitionId BlockDefinition
        {
            get
            {
                if (!_defIdSet)
                {
                    string t = Inst.Def.TypeId ?? "";
                    if (!t.StartsWith("MyObjectBuilder_", StringComparison.Ordinal)) t = "MyObjectBuilder_" + t;
                    _defId = new SerializableDefinitionId(t, Inst.Def.Subtype ?? "");
                    _defIdSet = true;
                }
                return _defId;
            }
        }
        public IMyCubeGrid CubeGrid { get { return Inst.Body.ApiGrid; } }
        public bool IsFunctional { get { return Inst.IsFunctional; } }

        /// <summary>functional && powered (SuppliedRatio of the block's sink > 0). Functional blocks add the Enabled switch.</summary>
        public virtual bool IsWorking
        {
            get
            {
                if (!Inst.IsFunctional) return false;
                var s = Inst.Sink;
                return s == null || s.SuppliedRatio > 0;
            }
        }

        public long OwnerId { get { return Inst.OwnerId; } }
        public Vector3I Position { get { return Inst.Position; } }
        public Vector3I Min { get { return Inst.Min; } }
        public Vector3I Max { get { return Inst.Max; } }
        public MyBlockOrientation Orientation { get { return Inst.Orientation; } }
        public float Mass { get { return Inst.Mass; } }
        public int NumberInGrid { get { return Inst.Index + 1; } }
        public virtual string DisplayNameText { get { return DisplayName; } }
        public string DefinitionDisplayNameText { get { return Inst.Def.Key; } }
        public string GetOwnerFactionTag() { return ""; }

        /// <summary>Called by Body.RemoveBlock when the block is destroyed (stop shooting, drop listeners, ...).</summary>
        public virtual void OnDestroyed() { }
        /// <summary>Called after the block moved to another Body (split/merge release).</summary>
        public virtual void OnBodyChanged() { }
    }

    public class SimTerminalBlock : SimCubeBlock, IMyTerminalBlock
    {
        public string CustomNameValue, CustomDataValue;
        public bool ShowOnHUDValue, ShowInTerminalValue = true;
        protected readonly TerminalRegistry Terminal;   // per-class static registry, see TerminalProps.cs

        public SimTerminalBlock(BlockInst inst, World world) : base(inst, world)
        {
            if (inst.Api == null) inst.Api = this;     // visible to a script constructor that runs before GridFactory stores it
            var src = inst.Src;
            string n = src != null ? src.CustomName : null;
            if (string.IsNullOrEmpty(n)) n = string.IsNullOrEmpty(inst.Def.Subtype) ? inst.Def.TypeId : inst.Def.Subtype;
            CustomNameValue = n ?? "";
            CustomDataValue = (src != null ? src.CustomData : null) ?? "";
            // virtual call is intended: Register of the most derived class runs; it only touches its parameter.
            Terminal = TerminalRegistry.For(GetType(), Register);
        }

        public ITerminalProperty GetProperty(string id) { return Terminal.Property(id); }
        public ITerminalAction GetActionWithName(string name) { return Terminal.Action(name); }
        public void GetProperties(List<ITerminalProperty> resultList, Func<ITerminalProperty, bool> collect = null)
        {
            resultList.Clear();
            foreach (var p in Terminal.Properties.Values) if (collect == null || collect(p)) resultList.Add(p);
        }
        public void GetActions(List<ITerminalAction> resultList, Func<ITerminalAction, bool> collect = null)
        {
            resultList.Clear();
            foreach (var a in Terminal.Actions.Values) if (collect == null || collect(a)) resultList.Add(a);
        }
        public string CustomName { get { return CustomNameValue; } set { CustomNameValue = value ?? ""; } }
        public string CustomData { get { return CustomDataValue; } set { CustomDataValue = value ?? ""; } }
        public override string DisplayName { get { return CustomNameValue; } }
        public virtual string DetailedInfo { get { return ""; } }
        public virtual string CustomInfo { get { return ""; } }
        public bool ShowOnHUD { get { return ShowOnHUDValue; } set { ShowOnHUDValue = value; } }
        public bool ShowInTerminal { get { return ShowInTerminalValue; } set { ShowInTerminalValue = value; } }

        /// <summary>Same construct = same Body (a merged grid is one grid in the game, so after merging everything is the same construct).</summary>
        public bool IsSameConstructAs(IMyTerminalBlock o)
        {
            var c = o as SimCubeBlock;
            return c != null && c.Inst.Body == Inst.Body;
        }

        /// <summary>Registers the terminal properties/actions of this class (called once per concrete type; overrides call base first).</summary>
        protected virtual void Register(TerminalRegistry r) { }

        // helpers for registrations
        protected static void AddBool(TerminalRegistry r, string id, Func<SimTerminalBlock, bool> get, Action<SimTerminalBlock, bool> set, bool dflt)
        { r.Add(new SimProperty<bool>(id, get, set, dflt, false, true)); }
        protected static void AddFloat(TerminalRegistry r, string id, Func<SimTerminalBlock, float> get, Action<SimTerminalBlock, float> set, float dflt, float min, float max)
        { r.Add(new SimProperty<float>(id, get, set, dflt, min, max)); }
    }

    public class SimFunctionalBlock : SimTerminalBlock, IMyFunctionalBlock
    {
        public bool EnabledValue = true;
        public SimFunctionalBlock(BlockInst inst, World world) : base(inst, world)
        {
            EnabledValue = inst.Src == null || inst.Src.Enabled;
        }

        public virtual bool Enabled
        {
            get { return EnabledValue; }
            set { if (EnabledValue == value) return; EnabledValue = value; OnEnabledChanged(); }
        }

        /// <summary>Mirror the switch into units (thruster/gyro/battery) etc.</summary>
        protected virtual void OnEnabledChanged() { }

        public override bool IsWorking { get { return Enabled && base.IsWorking; } }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            AddBool(r, "OnOff", b => ((SimFunctionalBlock)b).Enabled, (b, v) => ((SimFunctionalBlock)b).Enabled = v, true);
            r.Add(new SimAction("OnOff", b => { var f = (SimFunctionalBlock)b; f.Enabled = !f.Enabled; }));
            r.Add(new SimAction("OnOff_On", b => ((SimFunctionalBlock)b).Enabled = true));
            r.Add(new SimAction("OnOff_Off", b => ((SimFunctionalBlock)b).Enabled = false));
        }
    }

    /// <summary>Terminal blocks without behaviour (timer, event controller, decoy...). Cargo containers expose an inventory.</summary>
    public sealed class SimGenericTerminal : SimTerminalBlock
    {
        SimInventory _inv;
        public SimGenericTerminal(BlockInst i, World w) : base(i, w) { }
        public override int InventoryCount { get { return Inst.Def.InventoryVolume > 0 ? 1 : 0; } }
        public override IMyInventory GetInventory(int i)
        {
            if (i != 0 || Inst.Def.InventoryVolume <= 0) return null;
            if (_inv == null)
            {
                _inv = new SimInventory();
                _inv.MaxVolumeM3 = Inst.Def.InventoryVolume;
                _inv.CurrentVolumeM3 = Inst.Def.InventoryVolume * ApiTuning.CargoFillFraction;
            }
            return _inv;
        }
    }

    /// <summary>Functional blocks without behaviour (AI blocks when no brain is attached, lights...).</summary>
    public class SimGenericFunctional : SimFunctionalBlock
    {
        public SimGenericFunctional(BlockInst i, World w) : base(i, w) { }
        /// <summary>The AI blocks' "AI Behavior" switch (terminal id assumed "ActivateBehavior"; off in a pasted blueprint). Only consulted
        /// by VanillaAiBrain when SimSettings.AiBehaviorNeedsSwitch is set.</summary>
        public bool BehaviorOn;
        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            AddBool(r, "ActivateBehavior", b => ((SimGenericFunctional)b).BehaviorOn, (b, v) => ((SimGenericFunctional)b).BehaviorOn = v, false);
        }
        /// <summary>For slot C's VanillaAiBrain: "OffensiveCombat", "FlightMovement", "DefensiveCombat", "BasicMission", ... for the game's
        /// AI blocks (scripts switch them on/off through Enabled while they have no target), else null.</summary>
        public string AiKind
        {
            get
            {
                string t = Inst.Def.TypeId ?? "";
                if (t.IndexOf("OffensiveCombat", StringComparison.Ordinal) >= 0) return "OffensiveCombat";
                if (t.IndexOf("FlightMovement", StringComparison.Ordinal) >= 0) return "FlightMovement";
                if (t.IndexOf("DefensiveCombat", StringComparison.Ordinal) >= 0) return "DefensiveCombat";
                if (t.IndexOf("BasicMission", StringComparison.Ordinal) >= 0) return "BasicMission";
                return null;
            }
        }
    }

    /// <summary>AI Flight (FlightMovementBlock): typed settings are stored (the VanillaAiBrain does not read them yet; the sim's
    /// flight model keeps its own speed limit). IsAutoPilotEnabled = switched on and working; LookAtPosition is not modelled (null).</summary>
    public sealed class SimFlightMovementBlock : SimGenericFunctional, IMyFlightMovementBlock
    {
        public SimFlightMovementBlock(BlockInst i, World w) : base(i, w) { }
        float _speed = 100f;
        public float SpeedLimit { get { return _speed; } set { _speed = float.IsNaN(value) || value < 0f ? 0f : value; } }
        public FlightMode FlightMode { get; set; }
        public bool PrecisionMode { get; set; }
        bool _collision = true;
        public bool CollisionAvoidance { get { return _collision; } set { _collision = value; } }
        public bool IsAutoPilotEnabled { get { return IsWorking && BehaviorOn; } }
        public Vector3D? LookAtPosition { get { return null; } }
    }

    /// <summary>AI Offensive (OffensiveCombatBlock): typed settings are stored only. The attack pattern is a separate component
    /// (IMyOffensiveCombatStayAtRange), as in the game.</summary>
    public sealed class SimOffensiveCombatBlock : SimGenericFunctional, IMyOffensiveCombatBlock
    {
        public SimOffensiveCombatBlock(BlockInst i, World w) : base(i, w) { }
        readonly SimStayAtRange _pattern = new SimStayAtRange();
        int _interval = 3;
        public long SelectedAttackPattern { get; set; }
        public OffensiveCombatTargetPriority TargetPriority { get; set; }
        public int UpdateTargetInterval { get { return _interval; } set { _interval = value < 0 ? 0 : value; } }
        public bool TryGetSelectedAttackPattern(out IMyAttackPatternComponent pattern) { pattern = _pattern; return true; }
    }

    public sealed class SimStayAtRange : IMyOffensiveCombatStayAtRange
    {
        float _min = 0f, _max = 800f;
        public float MinimalDistance { get { return _min; } set { _min = value < 0f ? 0f : value; } }
        public float MaximalDistance { get { return _max; } set { _max = value < 0f ? 0f : value; } }
        public bool EvasiveManeuvers { get; set; }
    }

    /// <summary>Timer Block: PB-visible API only. The countdown runs on the world tick; when it expires nothing happens (the
    /// toolbar actions are not loaded from the blueprint, so there is nothing to run). TriggerDelay is in seconds.</summary>
    public sealed class SimTimer : SimFunctionalBlock, IMyTimerBlock
    {
        long _fireTick = -1;
        float _delay = 10f;
        public SimTimer(BlockInst i, World w) : base(i, w) { }
        public bool Silent { get; set; }
        public float TriggerDelay { get { return _delay; } set { _delay = float.IsNaN(value) ? 1f : Math.Max(1f, Math.Min(value, 3600f)); } }
        public bool IsCountingDown
        {
            get
            {
                if (_fireTick < 0) return false;
                if (World.Tick >= _fireTick) _fireTick = -1;        // expired: actions skipped
                return _fireTick >= 0;
            }
        }
        public void StartCountdown() { if (EnabledValue && Inst.IsFunctional) _fireTick = World.Tick + (long)Math.Round(_delay * SimSettings.TicksPerSecond); }
        public void StopCountdown() { _fireTick = -1; }
        public void Trigger() { }
        protected override void OnEnabledChanged() { if (!EnabledValue) _fireTick = -1; }
    }
}
