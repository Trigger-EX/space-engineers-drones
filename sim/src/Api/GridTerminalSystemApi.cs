// SLOT B. IMyGridTerminalSystem of one PB: all terminal blocks of the PB's current Body (in game: the PB's grid group;
// merged = same grid). Cached block list rebuilt when Body.Version changed. Order = Body.Blocks order (BlockInst.Index =
// blueprint order), deterministic. GetBlocksOfType<T>: 'is T' filter then the predicate; the list is cleared first (like the game).
// Destroyed blocks are not listed; blocks below critical integrity still are (IsFunctional false). Block groups: none defined
// in most blueprints (M0: parsed from <BlockGroups> when present), so GetBlockGroups is usually empty and GetBlockGroupWithName null.
using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimGridTerminalSystem : IMyGridTerminalSystem
    {
        public readonly SimProgrammableBlock Pb;
        readonly List<SimTerminalBlock> _cache = new List<SimTerminalBlock>();
        Body _cachedBody; int _cachedVersion = -1;
        public SimGridTerminalSystem(SimProgrammableBlock pb) { Pb = pb; }

        List<SimTerminalBlock> Blocks()
        {
            Body b = Pb.Inst.Body;
            if (b == _cachedBody && b.Version == _cachedVersion) return _cache;
            _cache.Clear();
            var src = b.Blocks;
            for (int i = 0; i < src.Count; i++)
            {
                BlockInst k = src[i];
                if (k.Destroyed) continue;
                SimTerminalBlock t = k.Api;
                if (t != null) _cache.Add(t);
            }
            _cachedBody = b; _cachedVersion = b.Version;
            return _cache;
        }

        public void GetBlocks(List<IMyTerminalBlock> blocks)
        {
            blocks.Clear();
            var c = Blocks();
            for (int i = 0; i < c.Count; i++) blocks.Add(c[i]);
        }

        public void GetBlocksOfType<T>(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null) where T : class
        {
            blocks.Clear();
            var c = Blocks();
            for (int i = 0; i < c.Count; i++)
            {
                SimTerminalBlock t = c[i];
                if (!(t is T)) continue;
                if (collect == null || collect(t)) blocks.Add(t);
            }
        }

        public void GetBlocksOfType<T>(List<T> l, Func<T, bool> f = null) where T : class
        {
            l.Clear();
            var c = Blocks();
            for (int i = 0; i < c.Count; i++)
            {
                T t = c[i] as T;
                if (t == null) continue;
                if (f == null || f(t)) l.Add(t);
            }
        }

        public void SearchBlocksOfName(string name, List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null)
        {
            blocks.Clear();
            var c = Blocks();
            for (int i = 0; i < c.Count; i++)
            {
                SimTerminalBlock t = c[i];
                if (name != null && t.CustomNameValue.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (collect == null || collect(t)) blocks.Add(t);
            }
        }

        public IMyTerminalBlock GetBlockWithName(string name)
        {
            var c = Blocks();
            for (int i = 0; i < c.Count; i++) if (string.Equals(c[i].CustomNameValue, name, StringComparison.Ordinal)) return c[i];
            return null;
        }

        public IMyTerminalBlock GetBlockWithId(long id)
        {
            var c = Blocks();
            for (int i = 0; i < c.Count; i++) if (c[i].Inst.EntityId == id) return c[i];
            return null;
        }

        // M0: the blueprint's <BlockGroups> of this PB's body (cells resolve to blocks at call time, destroyed members drop out)
        public void GetBlockGroups(List<IMyBlockGroup> blockGroups, Func<IMyBlockGroup, bool> collect = null)
        {
            blockGroups.Clear();
            Body b = Pb.Inst.Body;
            for (int i = 0; i < b.Groups.Count; i++)
            {
                var g = new SimBlockGroup(b, b.Groups[i]);
                if (collect == null || collect(g)) blockGroups.Add(g);
            }
        }
        public IMyBlockGroup GetBlockGroupWithName(string name)
        {
            Body b = Pb.Inst.Body;
            for (int i = 0; i < b.Groups.Count; i++) if (b.Groups[i].Name == name) return new SimBlockGroup(b, b.Groups[i]);
            return null;
        }
    }
}
