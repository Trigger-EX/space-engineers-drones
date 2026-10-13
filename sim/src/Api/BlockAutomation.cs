// M0 (docs/design/control_arch.md s5, s6 S3/S7): the automation blocks of a warship.
//   SimTimer            Timer Block: countdown in ticks, on expiry its toolbar runs (Start/Stop/TriggerNow/OnOff_*/Shoot*/Run(arg)...).
//   SimEventController  Event Controller: evaluates ONE event over its SelectedBlocks every 10 ticks, edge-triggered:
//                       slot 0 when the condition becomes true, slot 1 when it becomes false (the first evaluation only records the state).
//   SimButtonPanel      Button Panel: Press(slot) runs that toolbar slot (the scenario's ScriptedPilot presses buttons).
//   SimDoor / SimConnector / SimTextPanel: API shells (door animation is a fixed time, a connector never locks: stays what the blueprint says).
//   SimToolbar          executes ToolbarItems (blueprint toolbars): terminal-block, terminal-group and weapon slots.
// Everything here runs only when SimSettings.BlockAutomation is on (World.Step calls IBlockAutomation.Tick, SimToolbar checks it): off, the
// blocks keep the old inert behaviour (a Timer expiry runs nothing) so older enemy blueprints play as before.
// NOT modelled / assumed (docs control_arch s6 "Unmodelled"): the real Event Controller event numbers (cockpit / power / merge ids are
// placeholders, SimSettings.EcEventIds), the real timer tick granularity (here: fires on the tick the countdown ends), door animation
// length (ApiTuning.DoorSeconds), connector locking.
using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using SeSim.Blueprint;
using SeSim.Core;

namespace SeSim.Api
{
    public interface IBlockAutomation { void Tick(long tick); }

    /// <summary>Runs toolbar items. A missing / destroyed / non-terminal target and an unknown action are skipped silently (the game does the same).</summary>
    public static class SimToolbar
    {
        public static int Executed;                           // metrics / tests (per process)

        /// <summary>Runs every slot of the owner's toolbar, in slot order.</summary>
        public static void RunAll(SimTerminalBlock owner)
        {
            var tb = owner.Inst.Toolbar;
            if (tb == null) return;
            for (int i = 0; i < tb.Length; i++) RunItem(owner, tb[i]);
        }

        /// <summary>Runs one slot (false when the owner has no such slot).</summary>
        public static bool RunSlot(SimTerminalBlock owner, int slot)
        {
            var tb = owner.Inst.Toolbar;
            if (tb == null) return false;
            bool any = false;
            for (int i = 0; i < tb.Length; i++) if (tb[i].Slot == slot) { RunItem(owner, tb[i]); any = true; }
            return any;
        }

        public static void RunItem(SimTerminalBlock owner, ToolbarItem it)
        {
            var w = owner.World;
            if (!w.Settings.BlockAutomation || it == null || owner.Inst.Destroyed) return;
            Executed++;
            switch (it.Kind)
            {
                case "Block":
                {
                    BlockInst t;
                    if (it.BlockId != 0 && w.BlocksById.TryGetValue(it.BlockId, out t) && !t.Destroyed && t.Api != null) Apply(t.Api, it.Action, it.Argument);
                    break;
                }
                case "Group":
                {
                    var body = owner.Inst.Body;
                    for (int g = 0; g < body.Groups.Count; g++)
                    {
                        var gd = body.Groups[g];
                        if (gd.Name != it.GroupName || gd.Construct != owner.Inst.ConstructId) continue;
                        for (int c = 0; c < gd.Cells.Count; c++)
                        {
                            var m = body.BlockAtCell(gd.Cells[c]);
                            if (m != null && !m.Destroyed && m.Api != null) Apply(m.Api, it.Action, it.Argument);
                        }
                    }
                    break;
                }
                case "Weapon":
                    SetWeapons(owner.Inst.Body, it.WeaponType, it.WeaponSubtype, true);
                    break;
            }
        }

        static void Apply(SimTerminalBlock t, string action, string arg)
        {
            if (action == "Run")
            {
                var pb = t as SimProgrammableBlock;
                if (pb != null && pb.World.Scripts != null && pb.Runner != null && !pb.Runner.Dead && pb.IsWorking)
                    pb.World.Scripts.RunArgument(pb, arg ?? "", UpdateType.Terminal);      // "Run" with a String parameter: hypothesised toolbar shape (control_arch K3)
                return;
            }
            var a = t.GetActionWithName(action);
            if (a != null) a.Apply(t);
        }

        /// <summary>Weapon toolbar slot: a hand-fired gun type (all fixed guns of that definition on the body) shoots / stops.</summary>
        public static void SetWeapons(Body body, string type, string subtype, bool on)
        {
            for (int i = 0; i < body.Blocks.Count; i++)
            {
                var b = body.Blocks[i];
                var g = b.Api as SimUserGun;
                if (g == null || b.Destroyed) continue;
                if (type != null && b.Def.TypeId != type && b.Def.TypeId != "MyObjectBuilder_" + type) continue;
                if (!string.IsNullOrEmpty(subtype) && b.Def.Subtype != subtype) continue;
                g.Shoot = on;
            }
        }
    }

    /// <summary>Timer Block. TriggerDelay in seconds (1..3600 like the game's UI; the saved Delay is milliseconds). StartCountdown restarts. With
    /// BlockAutomation off the expiry runs nothing (the old behaviour).</summary>
    public sealed class SimTimer : SimFunctionalBlock, IMyTimerBlock, IBlockAutomation
    {
        long _fireTick = -1;
        float _delay = 10f;
        public SimTimer(BlockInst i, World w) : base(i, w)
        {
            double ms;
            if (i.Src != null && i.Src.Props.ContainsKey("Delay") && (ms = i.Src.PropNum("Delay", -1)) >= 0) _delay = (float)Math.Max(1.0, Math.Min(ms / 1000.0, 3600.0));
            Silent = i.Src != null && i.Src.PropBool("Silent", false);
            w.Automation.Add(this);
        }
        public bool Silent { get; set; }
        public int Expiries;                                   // how often the countdown ran out (tests)
        public float TriggerDelay { get { return _delay; } set { _delay = float.IsNaN(value) ? 1f : Math.Max(1f, Math.Min(value, 3600f)); } }
        public bool IsCountingDown
        {
            get
            {
                if (_fireTick < 0) return false;
                if (World.Tick >= _fireTick) Expire();
                return _fireTick >= 0;
            }
        }
        public void StartCountdown() { if (EnabledValue && Inst.IsFunctional) _fireTick = World.Tick + (long)Math.Round(_delay * SimSettings.TicksPerSecond); }
        public void StopCountdown() { _fireTick = -1; }
        /// <summary>"Trigger Now": runs the toolbar immediately; does not start the countdown.</summary>
        public void Trigger() { if (World.Settings.BlockAutomation && IsWorking) SimToolbar.RunAll(this); }
        public void Tick(long tick) { if (_fireTick >= 0 && tick >= _fireTick) Expire(); }
        void Expire()
        {
            _fireTick = -1;
            Expiries++;
            if (World.Settings.BlockAutomation && IsWorking) SimToolbar.RunAll(this);
        }
        protected override void OnEnabledChanged() { if (!EnabledValue) _fireTick = -1; }
        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            r.Add(new SimAction("Start", b => ((SimTimer)b).StartCountdown()));
            r.Add(new SimAction("Stop", b => ((SimTimer)b).StopCountdown()));
            r.Add(new SimAction("TriggerNow", b => ((SimTimer)b).Trigger()));
        }
    }

    /// <summary>Event Controller (IMyEventControllerBlock). Event ids: SimSettings.EcEventIds. Threshold: fraction 0..1 for integrity / stored power / cargo
    /// fill; ignored for boolean events (cockpit occupied, merge / connector connected). Base condition: value &gt;= Threshold; ConditionInvert (=
    /// IsLowerOrEqualCondition): value &lt;= Threshold (booleans: inverted). ANDGate: all selected blocks must satisfy it, else any.</summary>
    public sealed class SimEventController : SimFunctionalBlock, IMyEventControllerBlock, IBlockAutomation
    {
        int _state = -1;                                        // -1 unknown, 0 false, 1 true
        readonly int _phase;
        public int Fired0, Fired1;                              // slot 0 / slot 1 trigger counts (tests)
        public bool IsAndModeEnabled { get; set; }
        public bool IsLowerOrEqualCondition { get; set; }
        public float Threshold { get; set; }
        public int SelectedEvent;
        public SimEventController(BlockInst i, World w) : base(i, w)
        {
            var s = i.Src;
            SelectedEvent = (int)(s != null ? s.PropNum("SelectedEvent", 0) : 0);
            Threshold = (float)(s != null ? s.PropNum("Threshold", 0) : 0);
            IsAndModeEnabled = s != null && s.PropBool("ANDGate", false);
            IsLowerOrEqualCondition = s != null && s.PropBool("ConditionInvert", false);
            _phase = (int)((ulong)i.EntityId % 10UL);
            w.Automation.Add(this);
        }

        static readonly Dictionary<string, Dictionary<int, string>> MapCache = new Dictionary<string, Dictionary<int, string>>();
        static string EventName(string table, int id)
        {
            Dictionary<int, string> m;
            lock (MapCache)
            {
                if (!MapCache.TryGetValue(table, out m))
                {
                    m = new Dictionary<int, string>();
                    foreach (string part in (table ?? "").Split(','))
                    {
                        int eq = part.IndexOf('='); int v;
                        if (eq > 0 && int.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) m[v] = part.Substring(0, eq).Trim().ToLowerInvariant();
                    }
                    MapCache[table ?? ""] = m;
                }
            }
            string n; return m.TryGetValue(id, out n) ? n : null;
        }

        public string EventLabel { get { return EventName(World.Settings.EcEventIds, SelectedEvent); } }
        public bool Condition { get { return Evaluate(); } }

        public void Tick(long tick)
        {
            if ((tick + _phase) % 10 != 0 || !IsWorking) return;
            int c = Evaluate() ? 1 : 0;
            if (_state < 0) { _state = c; return; }
            if (c == _state) return;
            _state = c;
            if (c == 1) Fired0++; else Fired1++;
            SimToolbar.RunSlot(this, c == 1 ? 0 : 1);
        }

        bool Evaluate()
        {
            string ev = EventLabel;
            var ids = Inst.SelectedIds;
            if (ev == null || ids == null || ids.Length == 0) return false;
            bool all = IsAndModeEnabled, result = all;
            int seen = 0;
            for (int k = 0; k < ids.Length; k++)
            {
                BlockInst b;
                if (!World.BlocksById.TryGetValue(ids[k], out b)) continue;
                double v; bool boolean;
                if (!Value(ev, b, out v, out boolean)) continue;
                seen++;
                bool ok;
                if (boolean) ok = IsLowerOrEqualCondition ? v < 0.5 : v >= 0.5;
                else ok = IsLowerOrEqualCondition ? v <= Threshold : v >= Threshold;
                if (all) { if (!ok) return false; }
                else if (ok) return true;
            }
            return seen > 0 && all;
        }

        static bool Value(string ev, BlockInst b, out double v, out bool boolean)
        {
            v = 0; boolean = false;
            switch (ev)
            {
                case "integrity":
                    v = b.Destroyed || b.MaxHp <= 0 ? 0 : Math.Max(0, b.Hp) / b.MaxHp; return true;
                case "cockpit":
                {
                    boolean = true;
                    var c = b.Api as SimShipController;
                    v = c != null && !b.Destroyed && c.IsUnderControl ? 1 : 0; return c != null;
                }
                case "power":
                {
                    var u = b.Battery;
                    if (u == null) return false;
                    v = b.Destroyed || u.MaxStoredMWh <= 0 ? 0 : u.StoredMWh / u.MaxStoredMWh; return true;
                }
                case "merge":
                {
                    boolean = true;
                    v = b.Merge != null && !b.Destroyed && b.Merge.IsConnected ? 1 : 0; return b.Merge != null;
                }
                case "connector":
                {
                    boolean = true;
                    var c = b.Api as SimConnector;
                    v = c != null && !b.Destroyed && c.Status == MyShipConnectorStatus.Connected ? 1 : 0; return c != null;
                }
                case "cargo":
                {
                    var inv = b.Api != null ? b.Api.GetInventory(0) as SimInventory : null;
                    if (inv == null || inv.MaxVolumeM3 <= 0) return false;
                    v = inv.CurrentVolumeM3 / inv.MaxVolumeM3; return true;
                }
            }
            return false;
        }
    }

    public sealed class SimButtonPanel : SimFunctionalBlock, IMyButtonPanel
    {
        public int Presses;
        public SimButtonPanel(BlockInst i, World w) : base(i, w) { AnyoneCanUse = i.Src == null || i.Src.PropBool("AnyoneCanUse", true); }
        public bool AnyoneCanUse { get; set; }
        /// <summary>A player (the scripted pilot) presses button 'slot' (0-based toolbar slot).</summary>
        public bool Press(int slot) { Presses++; return IsWorking && SimToolbar.RunSlot(this, slot); }
    }

    /// <summary>Doors (Door / AirtightSlideDoor / AirtightHangarDoor): Open -> Opening for ApiTuning.DoorSeconds -> Open, and back.</summary>
    public sealed class SimDoor : SimFunctionalBlock, IMyDoor
    {
        bool _open; long _changed = -1000000;
        public SimDoor(BlockInst i, World w) : base(i, w) { _open = i.Src != null && (i.Src.Prop("State", "") == "Open" || i.Src.PropBool("Open", false)); }
        long Ticks { get { return (long)Math.Round(ApiTuning.DoorSeconds * SimSettings.TicksPerSecond); } }
        public DoorStatus Status
        {
            get
            {
                if (World.Tick - _changed < Ticks) return _open ? DoorStatus.Opening : DoorStatus.Closing;
                return _open ? DoorStatus.Open : DoorStatus.Closed;
            }
        }
        public void OpenDoor() { if (EnabledValue && IsWorking && !_open) { _open = true; _changed = World.Tick; } }
        public void CloseDoor() { if (EnabledValue && IsWorking && _open) { _open = false; _changed = World.Tick; } }
        public void ToggleDoor() { if (_open) CloseDoor(); else OpenDoor(); }
        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            AddBool(r, "Open", b => ((SimDoor)b)._open, (b, v) => { if (v) ((SimDoor)b).OpenDoor(); else ((SimDoor)b).CloseDoor(); }, false);
            r.Add(new SimAction("Open", b => ((SimDoor)b).ToggleDoor()));
            r.Add(new SimAction("Open_On", b => ((SimDoor)b).OpenDoor()));
            r.Add(new SimAction("Open_Off", b => ((SimDoor)b).CloseDoor()));
        }
    }

    /// <summary>Ship connector: never locks by itself (a pod stays welded in the sim, control_arch s6). Status is Connected only when the blueprint
    /// carries a ConnectedEntityId.</summary>
    public sealed class SimConnector : SimFunctionalBlock, IMyShipConnector
    {
        public SimConnector(BlockInst i, World w) : base(i, w)
        {
            _connected = i.Src != null && i.Src.PropNum("ConnectedEntityId", 0) != 0;
            ThrowOut = i.Src != null && i.Src.PropBool("ThrowOut", false);
            CollectAll = i.Src != null && i.Src.PropBool("CollectAll", false);
        }
        bool _connected;
        public bool ThrowOut { get; set; }
        public bool CollectAll { get; set; }
        public MyShipConnectorStatus Status { get { return _connected ? MyShipConnectorStatus.Connected : MyShipConnectorStatus.Unconnected; } }
        public void Connect() { }
        public void Disconnect() { _connected = false; }
        public void ToggleConnect() { if (_connected) Disconnect(); else Connect(); }
    }

    /// <summary>LCD panel: one text surface (kept text; sprites are only counted).</summary>
    public sealed class SimTextPanel : SimFunctionalBlock, IMyTextPanel
    {
        public readonly SimTextSurface Surface = new SimTextSurface();
        public SimTextPanel(BlockInst i, World w) : base(i, w) { }
        public ContentType ContentType { get { return Surface.ContentType; } set { Surface.ContentType = value; } }
        public float FontSize { get { return Surface.FontSize; } set { Surface.FontSize = value; } }
        public string Font { get { return Surface.Font; } set { Surface.Font = value; } }
        public bool WriteText(string s, bool append = false) { return Surface.WriteText(s, append); }
        public bool WriteText(System.Text.StringBuilder s, bool append = false) { return Surface.WriteText(s, append); }
        public string GetText() { return Surface.GetText(); }
        public MySpriteDrawFrame DrawFrame() { return Surface.DrawFrame(); }
        public Vector2 SurfaceSize { get { return Surface.SurfaceSize; } }
        public Vector2 TextureSize { get { return Surface.TextureSize; } }
        public Color ScriptBackgroundColor { get { return Surface.ScriptBackgroundColor; } set { Surface.ScriptBackgroundColor = value; } }
        public Color ScriptForegroundColor { get { return Surface.ScriptForegroundColor; } set { Surface.ScriptForegroundColor = value; } }
        public Color FontColor { get { return Surface.FontColor; } set { Surface.FontColor = value; } }
        public Color BackgroundColor { get { return Surface.BackgroundColor; } set { Surface.BackgroundColor = value; } }
        public TextAlignment Alignment { get { return Surface.Alignment; } set { Surface.Alignment = value; } }
    }

    /// <summary>A terminal block group of a blueprint grid (IMyBlockGroup).</summary>
    public sealed class SimBlockGroup : IMyBlockGroup
    {
        readonly Body _body; readonly GroupData _data;
        public SimBlockGroup(Body body, GroupData data) { _body = body; _data = data; }
        public string Name { get { return _data.Name; } }
        public void GetBlocks(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null)
        {
            blocks.Clear();
            for (int i = 0; i < _data.Cells.Count; i++)
            {
                var b = _body.BlockAtCell(_data.Cells[i]);
                if (b == null || b.Destroyed || b.Api == null || blocks.Contains(b.Api)) continue;
                if (collect == null || collect(b.Api)) blocks.Add(b.Api);
            }
        }
    }
}
