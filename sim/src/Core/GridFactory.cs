// SLOT A. Builds Bodies from BlueprintData: block instances, units, remapped entity ids, placement in the world.
//
// Order of work in Spawn (matters because a PB script is constructed inside BlockApiFactory.Create and immediately looks at its grid):
//   1. every grid -> Body with all BlockInsts, cells, unit lists (Thruster/Gyro/Battery/Merge/Sink), combat units
//      (World.Combat.AttachUnits), power system, mass properties, placement and velocity;
//   2. only then the slot B API objects, all non-PB terminal blocks first, programmable blocks last, each result stored in
//      BlockInst.Api by this class right after Create returns. (A PB whose Create() runs its script constructor therefore sees
//      every other block's API object; its own Inst.Api is still null at that moment unless SimTerminalBlock's constructor
//      assigns it - see the request in the slot A report.)
using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using SeSim.Blueprint;

namespace SeSim.Core
{
    /// <summary>Where and how to place a blueprint. The blueprint grid's own PositionAndOrientation is ignored;
    /// the grid origin (cell 0,0,0 center) is placed at Origin with the given world Forward/Up.</summary>
    public struct SpawnPlacement
    {
        public Vector3D Origin, Forward, Up, LinearVelocity, AngularVelocity;
    }

    public static class GridFactory
    {
        /// <summary>Spawns every grid of bp (index 0 = main) as Bodies owned by 'team', with ShipIndex 'shipIndex'.
        /// Steps: BlockDef lookup (unknown block = hard error), Min/Max/LocalMatrix (OrientMath), Hp = MaxHp (blueprint
        /// IntegrityPercent/BuildPercent if present), units (Thruster/Gyro/Battery/Merge/Sink), EntityIds via World.Ids
        /// (the old->new id map is returned so PB CustomData/Storage references survive), SeSim.Api.BlockApiFactory.Create
        /// for terminal blocks, SeSim.Combat.CombatSystem.AttachUnits for weapons/warheads/turrets, mass properties,
        /// PowerSystem, merge pairs between the spawned grids (merge faces touching and both enabled => one Body).</summary>
        public static List<Body> Spawn(World w, BlueprintData bp, int team, int shipIndex, SpawnPlacement at, Dictionary<long, long> idMap)
        {
            return SpawnGrids(w, bp.Grids, team, shipIndex, at, idMap, false);
        }

        /// <summary>Spawns a projected grid of a projector (e.g. the Stinger inside "Stinger Rack") as a free ship.
        /// projectedIndex counts the ProjectedGrids of the blueprint's projector blocks in file order. Projected blocks are
        /// skeletons (IntegrityPercent about 1.5e-5); they are spawned fully built.</summary>
        public static List<Body> SpawnProjected(World w, BlueprintData bp, int projectedIndex, int team, int shipIndex, SpawnPlacement at, Dictionary<long, long> idMap)
        {
            int k = 0;
            foreach (var g in bp.Grids)
                foreach (var blk in g.Blocks)
                    foreach (var pg in blk.ProjectedGrids)
                        if (k++ == projectedIndex)
                        {
                            var one = new List<GridData> { pg };
                            return SpawnGrids(w, one, team, shipIndex, at, idMap, true);
                        }
            throw new ArgumentException("blueprint '" + bp.Name + "' has no projected grid #" + projectedIndex + " (found " + k + ")");
        }

        /// <summary>Number of projected grids in the blueprint's projector blocks.</summary>
        public static int CountProjected(BlueprintData bp)
        {
            int k = 0;
            foreach (var g in bp.Grids) foreach (var blk in g.Blocks) k += blk.ProjectedGrids.Count;
            return k;
        }

        // =====================================================================================================

        static List<Body> SpawnGrids(World w, List<GridData> grids, int team, int shipIndex, SpawnPlacement at, Dictionary<long, long> idMap, bool forceFull)
        {
            if (idMap == null) idMap = new Dictionary<long, long>();
            w.Teams.Ensure(team + 1);
            long ident = w.Teams.IdentityOf(team);
            var s = w.Settings;
            var origin = new int[grids.Count];
            for (int i = 0; i < origin.Length; i++) origin[i] = i;
            if (s.FuseMergedGrids && grids.Count > 1 && !forceFull)
            {
                var from = new List<List<int>>();
                grids = GridFuse.Fuse(w.Defs, grids, from);
                origin = new int[grids.Count];
                for (int i = 0; i < origin.Length; i++) origin[i] = from[i][0];
            }
            var result = new List<Body>(grids.Count);

            MatrixD w0 = MatrixD.CreateWorld(at.Origin, at.Forward, at.Up);
            MatrixD g0 = MatrixD.CreateWorld(grids[0].Position, grids[0].Forward, grids[0].Up);
            MatrixD g0inv = MatrixD.Invert(g0);

            for (int gi = 0; gi < grids.Count; gi++)
            {
                var g = grids[gi];
                MatrixD wm = gi == 0 ? w0 : (MatrixD.CreateWorld(g.Position, g.Forward, g.Up) * g0inv) * w0;
                var body = new Body();
                body.DisplayName = g.DisplayName;
                body.GridSize = g.Large ? MyCubeSize.Large : MyCubeSize.Small;
                body.CellSize = g.Large ? w.Defs.LargeCellSize : w.Defs.SmallCellSize;
                body.Team = team; body.OwnerId = ident; body.ShipIndex = shipIndex; body.IsStatic = g.IsStatic;
                body.DampenersOn = g.DampenersEnabled;
                body.Rotation = QuaternionD.CreateFromRotationMatrix(wm.GetOrientation());
                body.Rotation.Normalize();
                body.Position = wm.Translation;               // grid origin until the COM is known
                w.AddBody(body);
                body.EntityId = MapId(w, idMap, g.EntityId);
                BuildBlocks(w, body, g, origin[gi], ident, forceFull, idMap);
                body.UpdateWorldMatrix();
                body.RecomputeMassProperties();
                body.LinearVelocity = at.LinearVelocity;
                body.AngularVelocity = at.AngularVelocity;
                if (gi > 0 && result.Count > 0)
                    body.LinearVelocity = at.LinearVelocity + Vector3D.Cross(at.AngularVelocity, body.Position - result[0].Position);
                body.ApiGrid = new SeSim.Api.SimCubeGrid(body);
                body.Power = new PowerSystem { Body = body };
                body.Power.Rebuild();
                body.Groups.AddRange(g.Groups);
                result.Add(body);
            }
            RemapToolbars(result, idMap);

            for (int i = 0; i < result.Count; i++) PairMerges(result[i]);

            if (!w.SkipApi)
            {
                for (int i = 0; i < result.Count; i++) CreateApis(w, result[i], false);
                for (int i = 0; i < result.Count; i++) CreateApis(w, result[i], true);
            }
            return result;
        }

        // toolbar targets and Event Controller selections refer to blueprint entity ids: translate them to this world's ids (all blocks exist by now)
        static void RemapToolbars(List<Body> bodies, Dictionary<long, long> idMap)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                var bl = bodies[i].Blocks;
                for (int q = 0; q < bl.Count; q++)
                {
                    var src = bl[q].Src;
                    if (src == null) continue;
                    if (src.Toolbar.Count > 0)
                    {
                        var arr = new ToolbarItem[src.Toolbar.Count];
                        for (int k = 0; k < arr.Length; k++)
                        {
                            var o = src.Toolbar[k]; long nid;
                            arr[k] = new ToolbarItem { Slot = o.Slot, Kind = o.Kind, Action = o.Action, GroupName = o.GroupName, WeaponType = o.WeaponType, WeaponSubtype = o.WeaponSubtype,
                                                       Argument = o.Argument, BlockId = o.BlockId != 0 && idMap.TryGetValue(o.BlockId, out nid) ? nid : 0 };
                        }
                        bl[q].Toolbar = arr;
                    }
                    if (src.Selected.Count > 0)
                    {
                        var ids = new List<long>();
                        for (int k = 0; k < src.Selected.Count; k++) { long nid; if (idMap.TryGetValue(src.Selected[k], out nid)) ids.Add(nid); }
                        bl[q].SelectedIds = ids.ToArray();
                    }
                }
            }
        }

        static long MapId(World w, Dictionary<long, long> idMap, long old)
        {
            if (old == 0) return w.Ids.Next();           // grids without id still need one
            long n;
            if (!idMap.TryGetValue(old, out n)) { n = w.Ids.Next(); idMap[old] = n; }
            return n;
        }

        static void BuildBlocks(World w, Body body, GridData g, int constructId, long ident, bool forceFull, Dictionary<long, long> idMap)
        {
            var defs = w.Defs; var s = w.Settings;
            double cell = body.CellSize;
            bool skeletonNoted = false;
            body.Blocks.Capacity = Math.Max(body.Blocks.Capacity, g.Blocks.Count);
            for (int i = 0; i < g.Blocks.Count; i++)
            {
                var bd = g.Blocks[i];
                BlockDef def = defs.Get(bd.TypeId, bd.Subtype);
                if (def.Large != g.Large)
                    throw new InvalidOperationException("block " + def.Key + " is " + (def.Large ? "large" : "small") + " but grid '" + g.DisplayName + "' is " + (g.Large ? "large" : "small"));
                var b = new BlockInst();
                b.Index = i; b.Def = def; b.Src = bd;
                b.EntityId = bd.EntityId == 0 ? 0 : MapId(w, idMap, bd.EntityId);
                if (b.EntityId != 0) w.BlocksById[b.EntityId] = b;
                b.ConstructId = bd.Origin >= 0 ? bd.Origin : constructId;
                b.Orientation = bd.Orientation;
                b.Min = bd.Min;
                b.Max = OrientMath.MaxCell(bd.Min, def.SizeCells, bd.Orientation);
                b.Position = b.Min;
                MatrixD rot = OrientMath.Rotation(bd.Orientation);
                b.LocalMatrix = OrientMath.LocalMatrix(bd.Orientation, b.Min, b.Max, cell);
                b.LocalCenter = b.LocalMatrix.Translation;
                b.HalfExtents = new Vector3D((b.Max.X - b.Min.X + 1) * cell * 0.5, (b.Max.Y - b.Min.Y + 1) * cell * 0.5, (b.Max.Z - b.Min.Z + 1) * cell * 0.5);
                b.Radius = b.HalfExtents.Length();
                b.Mass = def.MassKg; b.MaxHp = def.MaxIntegrity; b.CriticalHp = def.CriticalIntegrity;
                double ip = bd.PropNum("IntegrityPercent", -1);
                if (forceFull || ip < 0) b.Hp = b.MaxHp;
                else if (ip < 0.01) { b.Hp = b.MaxHp; if (!skeletonNoted) { skeletonNoted = true; NoteOnce(defs, "blocks stored as projection skeletons (IntegrityPercent < 0.01) are spawned at full integrity"); } }
                else b.Hp = (float)(b.MaxHp * Math.Min(1.0, ip));
                b.OwnerId = def.IsTerminal ? ident : 0;
                b.Body = body;

                // ---- units ----
                switch (def.Kind)
                {
                    case "Thrust":
                    {
                        Vector3D back = rot.Backward;
                        var t = new ThrusterUnit
                        {
                            Block = b, ForceDirLocal = back, PosLocal = b.LocalCenter,
                            MaxForce = def.ThrustForceN, MaxPowerMW = def.ThrustMaxPowerMW, MinPowerMW = def.ThrustMinPowerMW,
                            Enabled = bd.Enabled
                        };
                        t.DirIndex = back.X > 0.5 ? 0 : back.X < -0.5 ? 1 : back.Y > 0.5 ? 2 : back.Y < -0.5 ? 3 : back.Z > 0.5 ? 4 : 5;
                        b.Thruster = t;
                        b.Sink = new PowerSink { Block = b, Group = def.PowerGroup ?? "Thrust", Priority = defs.PriorityOf(def.PowerGroup ?? "Thrust"), RequestMW = 0 };
                        break;
                    }
                    case "Gyro":
                        b.Gyro = new GyroUnit { Block = b, RotToGrid = rot, MaxForce = def.GyroForce, Enabled = bd.Enabled };
                        if (def.PowerMW > 0)
                            b.Sink = new PowerSink { Block = b, Group = def.PowerGroup ?? "Gyro", Priority = defs.PriorityOf(def.PowerGroup ?? "Gyro"), RequestMW = def.PowerMW };
                        break;
                    case "BatteryBlock":
                    {
                        double max = def.BatteryMaxMWh;
                        double stored = bd.PropNum("CurrentStoredPower", def.BatteryInitialRatio * max);
                        if (stored > max) stored = max; if (stored < 0) stored = 0;
                        var mode = Sandbox.ModAPI.Ingame.ChargeMode.Auto;
                        int cm = (int)bd.PropNum("ChargeMode", 0);
                        if (cm == 1) mode = Sandbox.ModAPI.Ingame.ChargeMode.Recharge; else if (cm == 2) mode = Sandbox.ModAPI.Ingame.ChargeMode.Discharge;
                        b.Battery = new BatteryUnit { Block = b, StoredMWh = stored, MaxStoredMWh = max, MaxOutputMW = def.BatteryMaxOutMW, MaxInputMW = def.BatteryMaxInMW, Enabled = bd.Enabled, Mode = mode };
                        break;
                    }
                    case "MergeBlock":
                        b.Merge = new MergeUnit { Block = b };
                        break;
                }
                if (b.Sink == null && def.PowerGroup != null && def.IsTerminal && def.Weapon == null
                    && def.Kind != "BatteryBlock" && def.Kind != "LargeTurretBase" && def.Kind != "WeaponBlock")
                {
                    double mw = def.PowerMW;
                    if (def.Kind == "ProgrammableBlock" && mw <= 0) mw = s.PowerUtilityDefaultMW;
                    else if (def.Kind == "RadioAntenna")
                        mw = bd.PropBool("EnableBroadcasting", true) ? bd.PropNum("BroadcastRadius", 0) * s.AntennaKWPerKm / 1000.0 / 1000.0 : 0;
                    if (mw > 0 && bd.Enabled)
                        b.Sink = new PowerSink { Block = b, Group = def.PowerGroup, Priority = defs.PriorityOf(def.PowerGroup), RequestMW = mw };
                }
                if (b.Sink == null && def.Kind == "LargeTurretBase" && s.TurretsNeedPower)
                {
                    double tw = def.PowerMW > 0 ? def.PowerMW : s.TurretIdlePowerMW;
                    b.Sink = new PowerSink { Block = b, Group = def.PowerGroup ?? "Defense", Priority = defs.PriorityOf(def.PowerGroup ?? "Defense"), RequestMW = bd.Enabled ? tw : 0 };
                }
                if (b.Sink == null && def.Kind == "WeaponBlock" && def.Weapon != null && s.FixedGunsNeedPower)
                {   // a fixed gun refuses to fire when out of power (MyUserControllableGun.CanShoot); only an electric supply is checked, the draw is tiny
                    b.Sink = new PowerSink { Block = b, Group = def.PowerGroup ?? "Defense", Priority = defs.PriorityOf(def.PowerGroup ?? "Defense"), RequestMW = bd.Enabled ? s.FixedGunPowerMW : 0 };
                }
                body.Blocks.Add(b);
            }
            body.BuildCells();
            body.RebuildUnitLists();
            // combat units need Body.Blocks / cells in place
            if (w.Combat != null)
                for (int i = 0; i < body.Blocks.Count; i++)
                    if (body.Blocks[i].Def.IsTerminal) w.Combat.AttachUnits(body.Blocks[i]);
            body.RebuildUnitLists();                       // AttachUnits adds WeaponUnits to BlockInst.Weapon
        }

        static void NoteOnce(DefDb defs, string msg)
        {
            lock (defs.Assumptions) { if (!defs.Assumptions.Contains(msg)) defs.Assumptions.Add(msg); }
        }

        static void CreateApis(World w, Body body, bool pbPass)
        {
            var blocks = body.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (!b.Def.IsTerminal) continue;
                bool isPb = b.Def.Kind == "ProgrammableBlock";
                if (isPb != pbPass) continue;
                var api = SeSim.Api.BlockApiFactory.Create(b, w);
                if (api != null) b.Api = api;
            }
        }

        // merge blocks of one body whose faces touch become a locked pair (Adjacent + Partner)
        static void PairMerges(Body body)
        {
            var merges = new List<BlockInst>();
            foreach (var b in body.Blocks) if (b.Merge != null) merges.Add(b);
            for (int i = 0; i < merges.Count; i++)
            {
                var a = merges[i];
                if (a.Merge.Adjacent != null) continue;
                Vector3D ua = Vector3D.TransformNormal(Base6Directions.GetVector(a.Merge.FaceLocal), OrientMath.Rotation(a.Orientation));
                for (int j = i + 1; j < merges.Count; j++)
                {
                    var c = merges[j];
                    if (c.Merge.Adjacent != null) continue;
                    Vector3D uc = Vector3D.TransformNormal(Base6Directions.GetVector(c.Merge.FaceLocal), OrientMath.Rotation(c.Orientation));
                    if ((ua + uc).Length() > 1e-6) continue;                        // faces must point at each other
                    Vector3D d = c.LocalCenter - a.LocalCenter;
                    double gap = body.CellSize;                                     // 1x1x1 merge blocks: centres one cell apart
                    if ((d - ua * gap).Length() > 1e-6) continue;
                    a.Merge.Adjacent = c.Merge; c.Merge.Adjacent = a.Merge;
                    if (a.Src.Enabled && c.Src.Enabled) { a.Merge.Partner = c.Merge; c.Merge.Partner = a.Merge; }
                    break;
                }
            }
        }
    }
}
