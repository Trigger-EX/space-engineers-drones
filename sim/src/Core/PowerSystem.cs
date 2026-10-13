// SLOT A. Per-body electrical network (one network per Body = per grid, like the game; merged ships share power).
// Sources: batteries, plus reactors / hydrogen engines at constant output (only enemy blueprints such as user warships have them). Sinks: PowerSink records (thrusters, gyros, utility blocks, railguns...).
// Groups are served in ascending priority (DefDb.PowerPriority, from ResourceDistributionGroups.sbc): non-adaptible groups get
// all or nothing, adaptible groups (Thrust, DefenseAdative, Charging, ...) a partial ratio. Battery recharge is not modelled
// (nothing on a drone produces surplus); batteries only discharge.
using System;
using System.Collections.Generic;

namespace SeSim.Core
{
    public sealed class PowerSystem
    {
        public Body Body;
        public readonly List<PowerSink> Sinks = new List<PowerSink>();   // sorted by (Priority, Group, Order) at Rebuild
        public double LastDemandMW, LastSuppliedMW, LastSupplyCapMW;

        static readonly Comparison<PowerSink> SinkOrder = (x, y) =>
        {
            int c = x.Priority.CompareTo(y.Priority);
            if (c != 0) return c;
            c = string.CompareOrdinal(x.Group, y.Group);
            return c != 0 ? c : x.Order.CompareTo(y.Order);
        };

        /// <summary>Supply = sum of discharging batteries' min(MaxOutputMW, StoredMWh*3600/dt). Serve sinks in Priority order
        /// (whole group gets the same ratio); drain batteries proportionally to their max output; recharge batteries only from
        /// surplus of other sources (none on drones). Empty battery bank => every SuppliedRatio = 0 (no thrust, no gyro).
        /// Thruster demand is refreshed first (Physics.PrepareThrusters) so the allocation sees this tick's commands.</summary>
        public void Update(double dt)
        {
            var body = Body;
            var s = body.World.Settings;
            Physics.PrepareThrusters(body, s);
            Physics.PrepareGyros(body);
            var sinks = Sinks;
            int n = sinks.Count;
            if (!s.PowerEnabled)
            {
                for (int i = 0; i < n; i++) sinks[i].SuppliedRatio = 1;
                LastSuppliedMW = LastDemandMW = 0;
                return;
            }

            // ---- supply ----
            double supply = 0;
            var bats = body.Batteries;
            for (int i = 0; i < bats.Count; i++)
            {
                var b = bats[i];
                b.CurrentOutputMW = 0; b.CurrentInputMW = 0;
                if (b.CanDischarge)
                {
                    double cap = b.StoredMWh * 3600.0 / dt;
                    supply += cap < b.MaxOutputMW ? cap : b.MaxOutputMW;
                }
            }
            double gen = GeneratorMW();
            double batSupply = supply;
            supply += gen;
            LastSupplyCapMW = supply;

            // ---- demand by group, priority order ----
            double remaining = supply, demandTotal = 0, used = 0;
            int idx = 0;
            while (idx < n)
            {
                string group = sinks[idx].Group;
                bool adapt = sinks[idx].Adaptible;
                int end = idx;
                double demand = 0;
                while (end < n && sinks[end].Group == group)
                {
                    var k = sinks[end];
                    if (!k.Block.Destroyed && k.RequestMW > 0) demand += k.RequestMW;
                    end++;
                }
                double ratio;
                if (demand <= 1e-15) ratio = 1;
                else if (remaining >= demand * (1 - 1e-12)) ratio = 1;
                else if (adapt && remaining > 0) ratio = remaining / demand;
                else ratio = 0;
                for (int i = idx; i < end; i++) sinks[i].SuppliedRatio = ratio;
                double got = demand * ratio;
                remaining -= got; if (remaining < 0) remaining = 0;
                used += got; demandTotal += demand;
                idx = end;
            }
            LastDemandMW = demandTotal; LastSuppliedMW = used;

            // ---- drain ----
            double fromBat = used - gen;                  // generators carry the load first, batteries the rest
            if (fromBat > 0 && batSupply > 0)
            {
                double scale = fromBat / batSupply; if (scale > 1) scale = 1;
                for (int i = 0; i < bats.Count; i++)
                {
                    var b = bats[i];
                    if (!b.CanDischarge) continue;
                    double cap = b.StoredMWh * 3600.0 / dt;
                    if (cap > b.MaxOutputMW) cap = b.MaxOutputMW;
                    double outMW = cap * scale;
                    b.CurrentOutputMW = outMW;
                    b.StoredMWh -= outMW * dt / 3600.0;
                    if (b.StoredMWh < 0) b.StoredMWh = 0;
                }
            }
        }

        /// <summary>After block removal / split / merge: collects the sinks of the live blocks (BlockInst.Sink and WeaponUnit.Sink),
        /// fills Priority/Adaptible from DefDb and sorts.</summary>
        public void Rebuild()
        {
            var defs = Body.World.Defs;
            Sinks.Clear();
            int order = 0;
            var blocks = Body.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.Sink != null) Add(defs, b.Sink, ref order);
                if (b.Weapon != null && b.Weapon.Sink != null && !ReferenceEquals(b.Weapon.Sink, b.Sink)) Add(defs, b.Weapon.Sink, ref order);
            }
            Sinks.Sort(SinkOrder);
            Body.PowerDirty = false;
        }

        void Add(SeSim.Blueprint.DefDb defs, PowerSink k, ref int order)
        {
            if (k.Group == null) k.Group = "Utility";
            int p; bool a;
            k.Priority = defs.PowerPriority.TryGetValue(k.Group, out p) ? p : 1000;
            k.Adaptible = defs.PowerAdaptible.TryGetValue(k.Group, out a) && a;
            k.Order = order++;
            Sinks.Add(k);
        }

        /// <summary>Output available from working reactors / hydrogen engines (constant, fuel not modelled).</summary>
        public double GeneratorMW()
        {
            double g = 0;
            var gs = Body.Generators;
            for (int i = 0; i < gs.Count; i++)
            {
                var b = gs[i];
                if (b.IsFunctional && (b.Src == null || b.Src.Enabled)) g += b.Def.GeneratorMW;
            }
            return g;
        }

        public double StoredMWh()
        {
            double sum = 0;
            var bats = Body.Batteries;
            for (int i = 0; i < bats.Count; i++) sum += bats[i].StoredMWh;
            return sum;
        }

        public double MaxStoredMWh()
        {
            double sum = 0;
            var bats = Body.Batteries;
            for (int i = 0; i < bats.Count; i++) sum += bats[i].MaxStoredMWh;
            return sum;
        }
    }
}
