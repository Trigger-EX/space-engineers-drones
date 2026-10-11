// SLOT B. Terminal properties and actions (what GetProperty/GetActionWithName/ApplyAction/SetValueFloat reach).
// Only ids the real game has (docs/sim-design.md section 4.4 lists them per block). Unknown id -> null (scripts fall back).
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

namespace SeSim.Api
{
    public sealed class SimProperty<T> : ITerminalProperty<T>
    {
        public string Id { get; private set; }
        public string TypeName { get; private set; }
        readonly Func<SimTerminalBlock, T> _get; readonly Action<SimTerminalBlock, T> _set; readonly T _min, _max, _default;
        readonly Func<SimTerminalBlock, T> _maxFn;

        public SimProperty(string id, Func<SimTerminalBlock, T> get, Action<SimTerminalBlock, T> set, T dflt, T min, T max)
        {
            if (id == null) throw new ArgumentNullException("id");
            if (get == null) throw new ArgumentNullException("get");
            Id = id; TypeName = typeof(T).Name; _get = get; _set = set; _default = dflt; _min = min; _max = max;
        }

        /// <summary>Variant whose maximum depends on the block (thruster override max = MaxThrust).</summary>
        public SimProperty(string id, Func<SimTerminalBlock, T> get, Action<SimTerminalBlock, T> set, T dflt, T min, Func<SimTerminalBlock, T> maxFn)
            : this(id, get, set, dflt, min, default(T)) { _maxFn = maxFn; }

        static SimTerminalBlock Cast(IMyCubeBlock block)
        {
            var b = block as SimTerminalBlock;
            if (b == null) throw new ArgumentException("not a terminal block of this simulator: " + (block == null ? "null" : block.GetType().Name));
            return b;
        }

        public T GetValue(IMyCubeBlock block) { return _get(Cast(block)); }
        public void SetValue(IMyCubeBlock block, T value)
        {
            if (_set == null) throw new InvalidOperationException("property " + Id + " is read-only");
            _set(Cast(block), value);
        }
        public T GetDefaultValue(IMyCubeBlock block) { return _default; }
        public T GetMinimum(IMyCubeBlock block) { return _min; }
        public T GetMaximum(IMyCubeBlock block) { return _maxFn != null ? _maxFn(Cast(block)) : _max; }
    }

    public sealed class SimAction : ITerminalAction
    {
        public string Id { get; private set; }
        public StringBuilder Name { get; private set; }
        readonly Action<SimTerminalBlock> _apply;
        public SimAction(string id, Action<SimTerminalBlock> apply)
        {
            if (id == null) throw new ArgumentNullException("id");
            Id = id; Name = new StringBuilder(id); _apply = apply;
        }
        public void Apply(IMyCubeBlock block)
        {
            var b = block as SimTerminalBlock;
            if (b == null) throw new ArgumentException("not a terminal block of this simulator");
            _apply(b);
        }
        public bool IsEnabled(IMyCubeBlock block) { return true; }
    }

    /// <summary>Per concrete API type, built once (static cache keyed by Type, thread-safe lazy init), shared by all instances.
    /// Includes "OnOff", "OnOff_On", "OnOff_Off" for functional blocks.</summary>
    public sealed class TerminalRegistry
    {
        public readonly Dictionary<string, ITerminalProperty> Properties = new Dictionary<string, ITerminalProperty>();
        public readonly Dictionary<string, ITerminalAction> Actions = new Dictionary<string, ITerminalAction>();
        public void Add(ITerminalProperty p) { Properties[p.Id] = p; }
        public void Add(SimAction a) { Actions[a.Id] = a; }

        static readonly Dictionary<Type, TerminalRegistry> Cache = new Dictionary<Type, TerminalRegistry>();
        static readonly object CacheLock = new object();

        public static TerminalRegistry For(Type t, Action<TerminalRegistry> build)
        {
            lock (CacheLock)
            {
                TerminalRegistry r;
                if (Cache.TryGetValue(t, out r)) return r;
                r = new TerminalRegistry();
                if (build != null) build(r);
                Cache[t] = r;
                return r;
            }
        }

        public ITerminalProperty Property(string id)
        {
            ITerminalProperty p; return id != null && Properties.TryGetValue(id, out p) ? p : null;
        }
        public ITerminalAction Action(string id)
        {
            ITerminalAction a; return id != null && Actions.TryGetValue(id, out a) ? a : null;
        }
    }
}
