// Dock-launch test (launch-protocol, docs/launch-protocol.md). A printed drone is spawned merged to a dummy printer body, the printer's merge
// block (optionally also the drone's) is switched off at --release-at seconds, and the drone's PB runs the REAL script in the blueprint's own
// default state (IDLE, default Custom Data) with NO _JOIN message. The run reports whether and when the drone left, how far, and the mode.
//
//   (--cradle: a real merge-block cradle host with clamped drones instead of the dummy printer: see DockLaunchCradle.cs)
//   mono SimHost.exe dock-launch [--model Hornet] [--seconds 30] [--release-at 3] [--both-merges] [--join-at T] [--arm-at T]
//        [--custom-data "INI with \n"] [--seed 1] [--repo DIR] [--trace FILE]
//   --join-at T   also broadcast "<TAG>_JOIN" ("join") from a helper PB-less sender at T s (default: never, the bug scenario)
//   --recompile-at T   terminal action "Recompile" on the drone PB at T s (Custom Data survives, the script state does not). Switches
//        SimSettings.AllowRecompileAction on; the sim recompile is instant and cannot fail, so the result is labelled OPTIMISTIC
//   --arm-at T    run the argument "armed" on the drone PB at T s (what HiveMotherAI does through DPb.TryRun)
//   --return-at S the harness plays a repair bay from S s on (docs/design/staff_split.md s10): "<TAG>_RBAY" for the dummy printer's merge every 2 s
//        (clamp face, axis = its Up, dockFwd / dockUp = the drone RC's printed pose, both carried with the printer body), "<TAG>_RACK ok" every 0.5 s
//        from S + 3 s (the REPAIR CLIENT takes it only while it asks), and the word "dock" to the drone PB at S + 0.1 s. TAG: --tag, else the model's
//        first word. PASS = CONTACT (the drone's merge within 0.35 m of the printer's clamp cell, aligned, < 1 m/s) and writer audit 0 from S on. The sim has no
//        merge re-lock in flight, so the test ends 2 s after contact (it cannot see "docked").
//   --events "T:!disable-pb Aux;T2:!enable-pb Aux"   switch PBs whose name contains the text off / on at T s (as SimEvents)
//   every run prints the override writer audit (ticks where two PBs wrote gyro / thruster overrides of one grid, SimEvents.WriterAudit)
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
        const double ClearM = 12;   // 'left the printer': drone centre this far from the printer's merge block (the drone is 5 m long)

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
            if (Has(args, "--cradle")) return DockLaunchCradle.Run(args, simDir, repo);      // a real cradle host (M5, DockLaunchCradle.cs)
            string model = Arg(args, "--model", "Hornet");
            double secs = Dbl(args, "--seconds", 30), rel = Dbl(args, "--release-at", 3);
            double joinAt = Dbl(args, "--join-at", -1), armAt = Dbl(args, "--arm-at", -1), recAt = Dbl(args, "--recompile-at", -1);
            bool both = Has(args, "--both-merges");
            ulong seed = (ulong)Dbl(args, "--seed", 1);
            string cd = Arg(args, "--custom-data", null);
            string tracePath = Arg(args, "--trace", null);
            double retAt = Dbl(args, "--return-at", -1);
            string rtag = Arg(args, "--tag", model.Split(new[] { (char)32 }, 2)[0].ToUpperInvariant());
            var events = SimEvents.Parse(Arg(args, "--events", null)); int evI = 0;
            var settings = new SimSettings(); settings.CountInstructions = Has(args, "--count-instructions"); settings.InstructionCountMode = Arg(args, "--instruction-count-mode", "blocks"); settings.EnforceInstructionLimit = Has(args, "--enforce-instruction-limit");
            if (recAt >= 0) settings.AllowRecompileAction = true;
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
            SimProgrammableBlock pb = null; BlockInst printerMerge = null, droneMerge = null, droneRc = null; var allPbs = new List<SimProgrammableBlock>();
            foreach (var b in bodies)
                foreach (var bi in b.Blocks)
                {
                    if (bi.Api is SimProgrammableBlock) { var cand = (SimProgrammableBlock)bi.Api; allPbs.Add(cand); if (pb == null || PbRank(cand) > PbRank(pb)) pb = cand; }   // the [Staff] PB of a split drone (launch, 'armed', the Mode line), else the Flight / only PB, never [Thrust] / [Aux]
                    if (droneRc == null && bi.Src.TypeId == "RemoteControl") droneRc = bi;   // the [Staff] PB of a split drone (launch, 'armed', the Mode line), else the Flight / only PB, never the [Thrust] one
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
            bool released = false, joinSent = false, armSent = false, recSent = false;
            double firstMove = -1, leftAt = -1, splitAt = -1, maxDist = 0, maxSpeed = 0; string modeNow = "?", modeFirst = null; double modeAt = -1;
            Vector3D pm0 = printerMerge.WorldCenter;
            // repair-bay play (--return-at): the drone RC's printed pose in the printer body's frame
            MatrixD inv0 = printerBody.InvWorldRot; Vector3D bayF = Vector3D.Zero, bayU = Vector3D.Zero;
            if (droneRc != null) { var rm = droneRc.WorldMatrix; bayF = Vector3D.TransformNormal(rm.Forward, inv0); bayU = Vector3D.TransformNormal(rm.Up, inv0); }
            bool dockSent = false; double contactAt = -1, minGap = 1e9; long bayN = 0, waG0 = -1, waT0 = -1;
            var sb = new StringBuilder();
            for (long t = 0; t < ticks; t++)
            {
                double now = t / SimSettings.TicksPerSecond;
                if (!armSent && armAt >= 0 && now >= armAt) { armSent = true; host.RunArgument(pb, "armed", UpdateType.Terminal); }
                if (!recSent && recAt >= 0 && now >= recAt) { recSent = true; pb.ApplyAction("Recompile"); runner = pb.Runner; }
                if (!released && t >= relTick)
                {
                    released = true;
                    ((SimMergeBlock)printerMerge.Api).Enabled = false;
                    if (both) ((SimMergeBlock)droneMerge.Api).Enabled = false;
                }
                if (!joinSent && joinAt >= 0 && now >= joinAt)
                {
                    joinSent = true;
                    string tag = model.Split(new[] { (char)32 }, 2)[0].ToUpperInvariant();   // WASP, LANCER, HORNET, HARRIER ...
                    SendJoin(w, pb, tag);
                }
                while (evI < events.Count && now >= events[evI].Key)
                {
                    string[] ep = events[evI].Value.TrimStart('!').Split(new[] { (char)32 }, 2); evI++;
                    if (ep.Length == 2 && (ep[0] == "disable-pb" || ep[0] == "enable-pb"))
                    {
                        int n = 0; foreach (var p in allPbs) if ((p.CustomNameValue ?? "").IndexOf(ep[1], StringComparison.OrdinalIgnoreCase) >= 0) { p.Enabled = ep[0] == "enable-pb"; n++; }
                        sb.Append(string.Format(Inv, "  t={0,5:F1}s event {1} {2}: {3} PBs\n", now, ep[0], ep[1], n));
                    }
                    else throw new ArgumentException("dock-launch --events: only !disable-pb / !enable-pb <name part>: " + events[evI - 1].Value);
                }
                if (retAt >= 0 && now >= retAt)
                {
                    Vector3D ax = printerMerge.WorldMatrix.Up, clamp = printerMerge.WorldCenter + ax * (printerBody.CellSize * 0.5);
                    if ((t - (long)(retAt * SimSettings.TicksPerSecond)) % 120 == 0)
                    {
                        bayN++;
                        w.Igc.InjectBroadcast(rtag + "_RBAY", new VRage.MyTuple<long, int, Vector3D, Vector3D, Vector3D, Vector3D>(BayPb, 1 | 7 << 8, clamp, ax,
                            Vector3D.Normalize(Vector3D.TransformNormal(bayF, printerBody.RotMatrix)), Vector3D.Normalize(Vector3D.TransformNormal(bayU, printerBody.RotMatrix))));
                    }
                    if (waG0 < 0) { var a0 = WriterAudit.Of(w); waG0 = a0.Gyro; waT0 = a0.Thrust; }   // the return is judged on its own (the take-off hand-over counts before)
                    if (!dockSent && now >= retAt + 0.1) { dockSent = true; host.RunArgument(pb, "dock", UpdateType.Terminal); sb.Append(string.Format(Inv, "  t={0,5:F1}s bay: word 'dock' to {1}\n", now, pb.CustomNameValue)); }
                    if (now >= retAt + 3 && t % 30 == 0) w.Igc.InjectBroadcast(rtag + "_RACK", new VRage.MyTuple<long, long, int, int>(pb.Inst.Body.EntityId, BayPb, 1, 1));
                    Vector3D want = printerMerge.WorldCenter + ax * printerBody.CellSize;
                    double gap = (droneMerge.WorldCenter - want).Length();
                    if (gap < minGap) minGap = gap;
                    if (contactAt < 0 && gap < 0.35 && Vector3D.Dot(droneMerge.WorldMatrix.Up, -ax) > 0.99 && (pb.Inst.Body.LinearVelocity - printerBody.LinearVelocity).Length() < 1)
                    { contactAt = now; sb.Append(string.Format(Inv, "  t={0,5:F1}s bay: CONTACT, merge gap {1:F2} m\n", now, gap)); }
                    if (contactAt >= 0 && now > contactAt + 2) break;
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
                if (released && leftAt < 0 && db != printerBody && dist > ClearM) leftAt = w.Time;
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
                + ", > 12 m away " + (leftAt >= 0 ? leftAt.ToString("F2", Inv) + " s" : "never")
                + ", max dist " + maxDist.ToString("F1", Inv) + " m, max speed " + maxSpeed.ToString("F1", Inv) + " m/s, final mode " + modeNow
                + (modeAt >= 0 ? " (since " + modeAt.ToString("F1", Inv) + " s)" : ""));
            string last = runner != null ? runner.EchoText : "";
            if (Has(args, "--echo")) Console.WriteLine("--- PB echo ---\n" + last);
            if (tw != null) tw.Dispose();
            var wa = WriterAudit.Of(w);
            Console.WriteLine("writer audit gyro " + wa.Gyro + " thrust " + wa.Thrust);
            bool ok = leftAt >= 0 && modeNow != "IDLE" && modeNow != "?";
            if (retAt >= 0)
            {
                long rg = wa.Gyro - Math.Max(0, waG0), rt = wa.Thrust - Math.Max(0, waT0);
                Console.WriteLine("RETURN " + model + ": " + bayN + " bay adverts, min merge gap " + minGap.ToString("F2", Inv) + " m, contact " + (contactAt >= 0 ? contactAt.ToString("F1", Inv) + " s" : "never")
                    + ", writer audit from " + retAt.ToString("F0", Inv) + " s: gyro " + rg + " thrust " + rt);
                bool rok = contactAt >= 0 && rg == 0 && rt == 0;
                Console.WriteLine(rok ? "RETURN OK (contact; the sim has no merge re-lock)" : "RETURN FAILED");
                return rok ? 0 : 1;
            }
            if (settings.CountInstructions) IlReport.Dump(host, "dock-launch " + model + " " + string.Join(" ", args), args);
            string optimistic = recAt >= 0 ? " (optimistic: sim Recompile is instant)" : "";
            Console.WriteLine(ok ? "LAUNCH OK" + optimistic : "LAUNCH FAILED (drone did not leave and start working)" + optimistic);
            return ok ? 0 : 1;
        }

        const long BayPb = 777000002L;   // the harness bay's "PB" id (--return-at)
        static int PbRank(SimProgrammableBlock p)
        {   // [Staff] first; [Thrust] and [Aux] (docs/design/staff_split.md) never: they have no Mode line and take no launch words
            string n = p.CustomNameValue ?? "";
            return n.StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase) ? 3 : n.StartsWith("[Thrust]", StringComparison.OrdinalIgnoreCase) || n.StartsWith("[Aux]", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
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
            int i = echo.IndexOf("Mode   : ", StringComparison.Ordinal), skip = 9;
            if (i < 0) { i = echo.IndexOf("Mode : ", StringComparison.Ordinal); skip = 7; }   // Gnat prints "Mode : FORM"
            if (i < 0) return null;
            i += skip; int j = echo.IndexOf('\n', i); if (j < 0) j = echo.Length;
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
