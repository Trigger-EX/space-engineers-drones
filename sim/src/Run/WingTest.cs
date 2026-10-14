// Wing collision test. N copies of one ship (default Ballista) run the REAL script side by side, optionally against one parked enemy cued
// over IGC, and the closest approach of every pair is reported (hulls touch at the sum of the bounding radii). Reproduces "three freshly
// pasted ships fly into the same spot".
//   mono SimHost.exe wing-test [--model Ballista] [--count 3] [--spacing 120] [--axis x|y|z] [--enemy Hornet] [--enemy-dist 3000] [--cue]
//        [--cmd hunt] [--seconds 120] [--seed 1] [--repo DIR]
// FAIL = any pair of hulls closer than 10 m (bounding spheres' sum is only reported) or a script death.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game.ModAPI.Ingame;
using VRage.Game;
using VRageMath;
using SeSim.Core;
using SeSim.Blueprint;
using SeSim.Api;

namespace SeSim.Run
{
    public static class WingTest
    {
        sealed class Sink : IEventSink
        {
            public int Collisions, CollisionDamage; public double FirstAt = -1; public double Dmg;
            public void On(ref SimEvent e)
            {
                if (e.Kind == EventKind.Collision && e.ShipA >= 0 && e.ShipB >= 0 && e.ShipA != e.ShipB && e.ShipA < Mine && e.ShipB < Mine)
                {
                    int mains = 0; foreach (var b in W.Bodies) if (b.Alive && b.ShipIndex >= 0 && b.ShipIndex < Mine && b.DisplayName == Model && (b.WorldBoundCenter - e.Position).Length() < 45) mains++;
                    if (mains < 2) { Other++; return; }   // a docked part (the Probe Foundry) or a probe, not hull on hull
                    Collisions++; Console.WriteLine("  hull collision: ships " + e.ShipA + "-" + e.ShipB + " at " + (e.Tick / (double)SimSettings.TicksPerSecond).ToString("F2") + " s, rel speed " + e.Value.ToString("F1"));
                    if (FirstAt < 0) FirstAt = e.Tick / (double)SimSettings.TicksPerSecond;
                }
                if (e.Kind == EventKind.CollisionDamage && e.ShipB >= 0 && e.ShipB < Mine) Dmg += e.Value;
            }
            public void EndTick(World w) { }
            public int Mine, Other; public World W; public string Model;
        }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Arg(string[] a, string k, string d) { for (int i = 1; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1]; return d; }
        static bool Has(string[] a, string k) { for (int i = 1; i < a.Length; i++) if (a[i] == k) return true; return false; }
        static double Dbl(string[] a, string k, double d) { double r; return double.TryParse(Arg(a, k, null) ?? "", NumberStyles.Float, Inv, out r) ? r : d; }

        public static int Run(string[] args, string simDir, string repo)
        {
            string model = Arg(args, "--model", "Ballista"), enemy = Arg(args, "--enemy", null), cmd = Arg(args, "--cmd", null), axis = Arg(args, "--axis", "y");
            int count = (int)Dbl(args, "--count", 3);
            double spacing = Dbl(args, "--spacing", 120), edist = Dbl(args, "--enemy-dist", 3000), secs = Dbl(args, "--seconds", 120); ulong seed = (ulong)Dbl(args, "--seed", 1);
            var settings = new SimSettings();
            DefDb defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            string bpDir = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            var bp = BlueprintLoader.Get(bpDir, model);
            var w = new World(settings, defs, seed);
            var host = new ScriptHost(w, simDir);
            w.Scripts = host; w.Teams.Init(2);
            var sink = new Sink { Mine = count, W = w, Model = model }; w.Events = sink;
            var ships = new List<List<Body>>(); var pbs = new List<SimProgrammableBlock>();
            Vector3D ax = axis == "x" ? Vector3D.Right : axis == "z" ? Vector3D.Backward : Vector3D.Up;
            for (int i = 0; i < count; i++)
            {
                var at = new SpawnPlacement { Origin = ax * ((i - (count - 1) / 2.0) * spacing), Forward = Vector3D.Right, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero };
                ships.Add(GridFactory.Spawn(w, bp, 0, i, at, new Dictionary<long, long>()));
            }
            Body eb = null;
            if (enemy != null)
            {
                var foe = GridFactory.Spawn(w, BlueprintLoader.Get(bpDir, enemy), 1, 0, new SpawnPlacement { Origin = new Vector3D(edist, 0, 0), Forward = Vector3D.Left, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero }, new Dictionary<long, long>());
                eb = foe[0];
            }
            foreach (var d in ships) foreach (var b in d) foreach (var bi in b.Blocks)   // the Custom Data the blueprint carries (Tag, Start Mode, Standoff ...), set before the constructors run
            {
                var pb = bi.Api as SimProgrammableBlock;
                if (pb != null && !string.IsNullOrEmpty(bi.Src.StoredCustomData)) pb.CustomData = bi.Src.StoredCustomData.Replace("\r", "");
            }
            BlockApiFactory.FlushPending(w);
            foreach (var d in ships) { SimProgrammableBlock first = null; foreach (var b in d) foreach (var bi in b.Blocks) if (first == null && bi.Api is SimProgrammableBlock && ((SimProgrammableBlock)bi.Api).Runner != null && ((SimProgrammableBlock)bi.Api).Runner.ScriptName.IndexOf("Shrike", StringComparison.Ordinal) >= 0) first = (SimProgrammableBlock)bi.Api; if (first == null) { Console.Error.WriteLine("test setup failed: no Helm PB in " + model); return 2; } pbs.Add(first); }
            var minPair = new double[count, count]; for (int i = 0; i < count; i++) for (int j = 0; j < count; j++) minPair[i, j] = double.MaxValue;
            double hullTouch = ships[0][0].BoundRadius * 2;
            long ticks = (long)(secs * SimSettings.TicksPerSecond);
            for (long t = 0; t < ticks; t++)
            {
                if (cmd != null && t == 60) foreach (var p in pbs) host.RunArgument(p, cmd, UpdateType.Terminal);
                if (eb != null && Has(args, "--cue") && t >= 60 && t % 15 == 0 && eb.Alive)
                    w.Igc.InjectBroadcast(Arg(args, "--tag", "BALLISTA") + "_TGT", new MyTuple<long, Vector3D, Vector3D, double, long>(eb.EntityId, eb.WorldBoundCenter, Vector3D.Zero, eb.BoundRadius, 0));
                w.Step();
                for (int i = 0; i < count; i++) for (int j = i + 1; j < count; j++)
                {
                    double d = (ships[i][0].WorldBoundCenter - ships[j][0].WorldBoundCenter).Length();
                    if (d < minPair[i, j]) minPair[i, j] = d;
                }
                if (t % (long)(SimSettings.TicksPerSecond * Dbl(args, "--every", 15)) == 0)
                {
                    Console.Write(string.Format(Inv, "t={0,4:F0}s", t / (double)SimSettings.TicksPerSecond));
                    for (int i = 0; i < count; i++) { var c = ships[i][0].WorldBoundCenter; var v = ships[i][0].LinearVelocity; Console.Write(string.Format(Inv, "  [{0,6:F0} {1,6:F0} {2,6:F0} v{3,4:F0}]", c.X, c.Y, c.Z, v.Length())); }
                    Console.WriteLine();
                }
            }
            bool ok = true;
            if (eb != null) Console.WriteLine("  enemy " + (eb.Alive ? "alive, " + eb.Blocks.Count + " blocks left" : "destroyed"));
            Console.WriteLine("wing-test " + model + " x" + count + " spacing " + spacing + " " + axis + (enemy != null ? ", enemy " + enemy + " at " + edist : ", no enemy") + (cmd != null ? ", cmd " + cmd : "") + "; hulls touch at ~" + hullTouch.ToString("F0", Inv) + " m (sum of radii)");
            Console.WriteLine("  hull-on-hull collisions: " + sink.Collisions + " (other contacts, e.g. a trailing foundry: " + sink.Other + ")" + (sink.FirstAt >= 0 ? " (first at " + sink.FirstAt.ToString("F1", Inv) + " s)" : "") + ", collision damage " + sink.Dmg.ToString("F0", Inv));
            ok &= sink.Collisions == 0;
            for (int i = 0; i < count; i++) for (int j = i + 1; j < count; j++)
            {
                bool good = minPair[i, j] > 10 + (ships[i][0].BoundRadius + ships[j][0].BoundRadius) * 0.6; ok &= good;   // the radius is of the bounding sphere (the Ballista is a flat slab): 60 % of the sum is a safe hull proxy
                Console.WriteLine(string.Format(Inv, "  pair {0}-{1}: closest {2,6:F1} m | {3}", i, j, minPair[i, j], good ? "ok" : "FAIL"));
            }
            for (int i = 0; i < count; i++) { var r = pbs[i].Runner; if (r.Dead) { Console.WriteLine("  ship " + i + " SCRIPT DEAD " + r.DeathReason); ok = false; } }
            if (Has(args, "--echo")) Console.WriteLine("--- Helm 0 echo ---\n" + pbs[0].Runner.EchoText);
            Console.WriteLine(ok ? "WING OK" : "WING FAILED");
            return ok ? 0 : 1;
        }
    }
}
