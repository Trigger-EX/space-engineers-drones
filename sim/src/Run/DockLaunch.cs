// Dock-launch test (launch-protocol, docs/launch-protocol.md). A printed drone is spawned merged to a dummy printer body, the printer's merge
// block (optionally also the drone's) is switched off at --release-at seconds, and the drone's PB runs the REAL script in the blueprint's own
// default state (IDLE, default Custom Data) with NO _JOIN message. The run reports whether and when the drone left, how far, and the mode.
//
//   mono SimHost.exe dock-launch [--model Hornet] [--seconds 30] [--release-at 3] [--both-merges] [--join-at T] [--arm-at T]
//        [--custom-data "INI with \n"] [--seed 1] [--repo DIR] [--trace FILE]
//   --join-at T   also broadcast "<TAG>_JOIN" ("join") from a helper PB-less sender at T s (default: never, the bug scenario)
//   --arm-at T    run the argument "armed" on the drone PB at T s (what HiveMotherAI does through DPb.TryRun)
//
// The dummy printer is code-built: the blueprint's own merge block gets a partner (opposite face, centres one cell apart), a one-cell stalk of
// armor and a 9x9x9 armor box behind it. The box is heavier than the drone, so the drone becomes the split-off body when the merge opens.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using VRage.Game;
using SeSim.Core;
using SeSim.Blueprint;
using SeSim.Api;

namespace SeSim.Run
{
    public static class DockLaunch
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string Arg(string[] a, string k, string d)
        {
            for (int i = 1; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1];
            return d;
        }
        static bool Has(string[] a, string k) { for (int i = 1; i < a.Length; i++) if (a[i] == k) return true; return false; }
        static double Dbl(string[] a, string k, double d) { double r; return double.TryParse(Arg(a, k, null) ?? "", NumberStyles.Float, Inv, out r) ? r : d; }

        public static string RepoArg(string[] a) { return Arg(a, "--repo", null); }

        /// <summary>The blueprint's main grid plus a dummy printer behind its merge block.</summary>
        public static BlueprintData BuildDocked(BlueprintData bp, out long dummyMergeId)
        {
            var src = bp.Grids[0];
            var g = new GridData { EntityId = src.EntityId, DisplayName = src.DisplayName, Large = src.Large, IsStatic = false, Position = src.Position, Forward = src.Forward, Up = src.Up, DampenersEnabled = src.DampenersEnabled };
            g.Blocks.AddRange(src.Blocks);
            BlockData mb = null;
            foreach (var b in src.Blocks) if (b.TypeId == "MergeBlock") { mb = b; break; }
            if (mb == null) throw new InvalidOperationException("blueprint '" + bp.Name + "' has no merge block");
            // 1x1x1 merge: faces toward its local Up (research.md 1.1; same assumption as GridFactory.PairMerges)
            MyBlockOrientation o = mb.Orientation;
            Vector3I ua = Base6Directions.GetIntVector(o.Up);
            var taken = new HashSet<Vector3I>();
            foreach (var b in src.Blocks)
                foreach (var c in Cells(b)) taken.Add(c);
            Vector3I m2 = mb.Min + ua;
            Base6Directions.Direction fwd = o.Forward;
            var pm = new BlockData
            {
                XsiType = mb.XsiType, TypeId = mb.TypeId, Subtype = mb.Subtype, EntityId = 777000001L, Min = m2,
                Orientation = new MyBlockOrientation(fwd, Base6Directions.GetFlippedDirection(o.Up)), CustomName = "DUMMY PRINTER MERGE", Enabled = true
            };
            dummyMergeId = pm.EntityId;
            g.Blocks.Add(pm);
            taken.Add(m2);
            // stalk (3 cells) then a 9x9x9 box, all along ua
            Vector3I e1, e2; Perp(ua, out e1, out e2);
            var cells = new List<Vector3I>();
            for (int k = 1; k <= 3; k++) cells.Add(m2 + ua * k);
            for (int k = 4; k <= 12; k++)
                for (int i = -4; i <= 4; i++)
                    for (int j = -4; j <= 4; j++)
                        cells.Add(m2 + ua * k + e1 * i + e2 * j);
            foreach (var c in cells)
            {
                if (taken.Contains(c)) throw new InvalidOperationException("dummy printer cell " + c + " collides with the drone");
                var ab = new BlockData { XsiType = "MyObjectBuilder_CubeBlock", TypeId = "CubeBlock", Subtype = "SmallBlockArmorBlock", EntityId = 0, Min = c, Orientation = MyBlockOrientation.Identity, Enabled = true };
                g.Blocks.Add(ab); taken.Add(c);
            }
            // the printer must not touch the drone except through the merge pair: no printer cell may be face-adjacent to a drone cell
            var dr = new HashSet<Vector3I>();
            foreach (var b in src.Blocks) foreach (var c in Cells(b)) dr.Add(c);
            foreach (var c in cells)
                foreach (var n in Neigh(c))
                    if (dr.Contains(n)) throw new InvalidOperationException("dummy printer cell " + c + " touches drone cell " + n);
            var res = new BlueprintData { Name = bp.Name + " (docked)", Path = bp.Path };
            res.Grids.Add(g);
            for (int i = 1; i < bp.Grids.Count; i++) res.Grids.Add(bp.Grids[i]);
            return res;
        }

        static IEnumerable<Vector3I> Neigh(Vector3I c)
        {
            yield return c + new Vector3I(1, 0, 0); yield return c - new Vector3I(1, 0, 0);
            yield return c + new Vector3I(0, 1, 0); yield return c - new Vector3I(0, 1, 0);
            yield return c + new Vector3I(0, 0, 1); yield return c - new Vector3I(0, 0, 1);
        }

        static void Perp(Vector3I a, out Vector3I e1, out Vector3I e2)
        {
            if (a.X != 0) { e1 = new Vector3I(0, 1, 0); e2 = new Vector3I(0, 0, 1); }
            else if (a.Y != 0) { e1 = new Vector3I(1, 0, 0); e2 = new Vector3I(0, 0, 1); }
            else { e1 = new Vector3I(1, 0, 0); e2 = new Vector3I(0, 1, 0); }
        }

        static IEnumerable<Vector3I> Cells(BlockData b)
        {
            // blueprint blocks used by the drones are almost all 1x1x1 or small boxes: the test only needs conservative occupancy,
            // so a block is expanded with its definition size when the DefDb is available through the static hook below
            Vector3I size = SizeOf == null ? Vector3I.One : SizeOf(b);
            Vector3I max = OrientMath.MaxCell(b.Min, size, b.Orientation);
            for (int z = b.Min.Z; z <= max.Z; z++) for (int y = b.Min.Y; y <= max.Y; y++) for (int x = b.Min.X; x <= max.X; x++) yield return new Vector3I(x, y, z);
        }
        [ThreadStatic] static Func<BlockData, Vector3I> SizeOf;

        public static int Run(string[] args, string simDir, string repo)
        {
            string model = Arg(args, "--model", "Hornet");
            double secs = Dbl(args, "--seconds", 30), rel = Dbl(args, "--release-at", 3);
            double joinAt = Dbl(args, "--join-at", -1), armAt = Dbl(args, "--arm-at", -1);
            bool both = Has(args, "--both-merges");
            ulong seed = (ulong)Dbl(args, "--seed", 1);
            string cd = Arg(args, "--custom-data", null);
            string tracePath = Arg(args, "--trace", null);
            var settings = new SimSettings();
            DefDb defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            SizeOf = b => { var d = defs.Get(b.TypeId, b.Subtype); return d.SizeCells; };
            var bp = BlueprintLoader.Get(Path.Combine(Path.Combine(repo, "out"), "Blueprints"), model);
            long dummyId;
            var docked = BuildDocked(bp, out dummyId);
            var w = new World(settings, defs, seed);
            var host = new ScriptHost(w, simDir);
            w.Scripts = host;
            w.Teams.Init(2);
            var idMap = new Dictionary<long, long>();
            var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero };
            var bodies = GridFactory.Spawn(w, docked, 0, 0, at, idMap);
            if (cd != null)
            {
                string text = cd.Replace("\\n", "\n");
                foreach (var b in bodies) foreach (var bi in b.Blocks) { var pbx = bi.Api as SimProgrammableBlock; if (pbx != null) pbx.CustomData = text; }
            }
            BlockApiFactory.FlushPending(w);
            SimProgrammableBlock pb = null; BlockInst printerMerge = null, droneMerge = null;
            foreach (var b in bodies)
                foreach (var bi in b.Blocks)
                {
                    if (bi.Api is SimProgrammableBlock) pb = (SimProgrammableBlock)bi.Api;
                    if (bi.Merge != null) { if (bi.Src.EntityId == dummyId) printerMerge = bi; else droneMerge = bi; }
                }
            if (pb == null || printerMerge == null || droneMerge == null) { Console.Error.WriteLine("test setup failed: pb/merge blocks not found"); return 2; }
            Body printerBody = printerMerge.Body;
            var runner = pb.Runner;
            Console.WriteLine("dock-launch " + model + ": drone blocks " + (bodies[0].Blocks.Count - printerBlockCount(bodies[0], dummyId)) + ", merge connected " + droneMerge.Merge.IsConnected + ", script " + (runner != null ? runner.ScriptName : "none") + (runner != null && runner.Dead ? " DEAD " + runner.DeathReason : ""));
            StreamWriter tw = null;
            if (tracePath != null) { tw = new StreamWriter(tracePath, false, new UTF8Encoding(false)); tw.WriteLine("t,drone_x,drone_y,drone_z,speed,dist_to_printer_merge,mode,grid_split"); }
            long ticks = (long)(secs * SimSettings.TicksPerSecond);
            long relTick = (long)(rel * SimSettings.TicksPerSecond);
            bool released = false, joinSent = false, armSent = false;
            double firstMove = -1, leftAt = -1, splitAt = -1, maxDist = 0, maxSpeed = 0; string modeNow = "?", modeFirst = null; double modeAt = -1;
            Vector3D pm0 = printerMerge.WorldCenter;
            var sb = new StringBuilder();
            for (long t = 0; t < ticks; t++)
            {
                double now = t / SimSettings.TicksPerSecond;
                if (!armSent && armAt >= 0 && now >= armAt) { armSent = true; host.RunArgument(pb, "armed", UpdateType.Terminal); }
                if (!released && t >= relTick)
                {
                    released = true;
                    ((SimMergeBlock)printerMerge.Api).Enabled = false;
                    if (both) ((SimMergeBlock)droneMerge.Api).Enabled = false;
                }
                if (!joinSent && joinAt >= 0 && now >= joinAt)
                {
                    joinSent = true;
                    string tag = model == "Harrier" ? "HARRIER" : "HORNET";
                    SendJoin(w, pb, tag);
                }
                w.Step();
                Body db = pb.Inst.Body;
                if (splitAt < 0 && db != printerBody) splitAt = w.Time;
                Vector3D dp = db.WorldBoundCenter, pmNow = printerMerge.WorldCenter;
                double dist = (dp - pmNow).Length();
                double spd = db.LinearVelocity.Length();
                if (spd > maxSpeed) maxSpeed = spd;
                if (released && dist > maxDist) maxDist = dist;
                if (released && firstMove < 0 && db != printerBody && spd > 1.0) firstMove = w.Time;
                if (released && leftAt < 0 && db != printerBody && dist > 30) leftAt = w.Time;
                if (t % 30 == 0)
                {
                    string e = runner != null ? runner.EchoText : null;
                    string m = ModeOf(e);
                    if (m != null) { if (m != modeNow) { modeNow = m; modeAt = w.Time; if (modeFirst == null) modeFirst = m; } }
                    if (tw != null) tw.WriteLine(w.Time.ToString("F2", Inv) + "," + dp.X.ToString("F1", Inv) + "," + dp.Y.ToString("F1", Inv) + "," + dp.Z.ToString("F1", Inv) + "," + spd.ToString("F2", Inv) + "," + dist.ToString("F1", Inv) + "," + m + "," + (db != printerBody ? 1 : 0));
                    if (t % 120 == 0) sb.Append(string.Format(Inv, "  t={0,5:F1}s mode={1,-6} dist={2,6:F1} m speed={3,5:F1} m/s split={4}\n", w.Time, m, dist, spd, db != printerBody ? "yes" : "no"));
                }
                if (runner != null && runner.Dead) { sb.Append("  SCRIPT DEAD: " + runner.DeathReason + "\n"); break; }
            }
            Console.Write(sb.ToString());
            Console.WriteLine("RESULT " + model + ": split " + (splitAt >= 0 ? splitAt.ToString("F2", Inv) + " s" : "never")
                + ", first movement " + (firstMove >= 0 ? firstMove.ToString("F2", Inv) + " s" : "never")
                + ", >30 m away " + (leftAt >= 0 ? leftAt.ToString("F2", Inv) + " s" : "never")
                + ", max dist " + maxDist.ToString("F1", Inv) + " m, max speed " + maxSpeed.ToString("F1", Inv) + " m/s, final mode " + modeNow
                + (modeAt >= 0 ? " (since " + modeAt.ToString("F1", Inv) + " s)" : ""));
            string last = runner != null ? runner.EchoText : "";
            if (Has(args, "--echo")) Console.WriteLine("--- PB echo ---\n" + last);
            if (tw != null) tw.Dispose();
            bool ok = leftAt >= 0 && modeNow != "IDLE" && modeNow != "?";
            Console.WriteLine(ok ? "LAUNCH OK" : "LAUNCH FAILED (drone did not leave and start working)");
            return ok ? 0 : 1;
        }

        static int printerBlockCount(Body b, long dummyId)
        {
            int n = 0;
            foreach (var bi in b.Blocks) if (bi.Def.Subtype == "SmallBlockArmorBlock" && bi.Src.EntityId == 0 && bi.Src.Props.Count == 0 && bi.Src.CustomName == null) n++;
            return n + 1;   // + the dummy merge block
        }

        static string ModeOf(string echo)
        {
            if (string.IsNullOrEmpty(echo)) return null;
            int i = echo.IndexOf("Mode   : ", StringComparison.Ordinal);
            if (i < 0) return null;
            i += 9; int j = echo.IndexOf('\n', i); if (j < 0) j = echo.Length;
            return echo.Substring(i, j - i).Trim();
        }

        static void SendJoin(World w, SimProgrammableBlock any, string tag)
        {
            // a broadcast from a second, script-less sender is not available in the engine: reuse the drone's own IGC with a different sender
            // is not possible either (a PB never hears its own broadcast), so the message is injected into the hub for every listener
            w.Igc.InjectBroadcast(tag + "_JOIN", "join");
        }
    }
}
