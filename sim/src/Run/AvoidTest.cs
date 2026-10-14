// Friendly-avoidance test (tools/fa_block.cs). N copies of a drone run the REAL script in HUNT towards a parked enemy while a parked
// FRIENDLY ship (team 0, no script, so nothing beacons for it) sits on the line between them. Without the avoidance block the drones fly
// through it; with it they sidestep or brake. --beacon injects the DRONE_SPACE beacon the friendly's script would send (else the drones can
// only see it with their cameras / sensors).
//   mono SimHost.exe avoid-test [--model Hornet] [--count 3] [--bystander Hornet] [--bx 30] [--mode hunt|transit] [--hx 400] [--beacon] [--rock] [--seconds 90] [--seed 1] [--repo DIR]
// Reported: closest approach of each drone to the bystander's centre, ticks inside the two hulls' bounding spheres, whether the enemy was
// engaged (min distance to it). FAIL = any drone within 10 m of the two bounding spheres (or a script death).
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
    public static class AvoidTest
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Arg(string[] a, string k, string d) { for (int i = 1; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1]; return d; }
        static bool Has(string[] a, string k) { for (int i = 1; i < a.Length; i++) if (a[i] == k) return true; return false; }
        static double Dbl(string[] a, string k, double d) { double r; return double.TryParse(Arg(a, k, null) ?? "", NumberStyles.Float, Inv, out r) ? r : d; }

        public static int Run(string[] args, string simDir, string repo)
        {
            string model = Arg(args, "--model", "Hornet"), by = Arg(args, "--bystander", "Hornet");
            int count = (int)Dbl(args, "--count", 3);
            double secs = Dbl(args, "--seconds", 90); ulong seed = (ulong)Dbl(args, "--seed", 1);
            bool rock = Has(args, "--rock"), beacon = Has(args, "--beacon"), transit = Arg(args, "--mode", "hunt") == "transit";   // transit: GUARD with a HOME beyond the friendly (a straight run through it), no enemy cue
            var settings = new SimSettings();
            DefDb defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            string bpDir = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            var bp = BlueprintLoader.Get(bpDir, model);
            var w = new World(settings, defs, seed);
            var host = new ScriptHost(w, simDir);
            w.Scripts = host; w.Teams.Init(2);
            var drones = new List<List<Body>>(); var pbs = new List<SimProgrammableBlock>();
            for (int i = 0; i < count; i++)
            {
                var at = new SpawnPlacement { Origin = new Vector3D(-300, (i - (count - 1) / 2.0) * 12, (i % 2) * 6), Forward = Vector3D.Right, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero };
                drones.Add(GridFactory.Spawn(w, bp, 0, i, at, new Dictionary<long, long>()));
            }
            var friend = GridFactory.Spawn(w, BlueprintLoader.Get(bpDir, by), 0, count, new SpawnPlacement { Origin = new Vector3D(Dbl(args, "--bx", 30), 0, 0), Forward = Vector3D.Right, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero }, new Dictionary<long, long>());
            var foe = GridFactory.Spawn(w, bp, 1, 0, new SpawnPlacement { Origin = new Vector3D(380, 0, 0), Forward = Vector3D.Left, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero }, new Dictionary<long, long>());
            BlockApiFactory.FlushPending(w);
            foreach (var d in drones) foreach (var b in d) foreach (var bi in b.Blocks) if (bi.Api is SimProgrammableBlock && pbs.Count == drones.IndexOf(d)) pbs.Add((SimProgrammableBlock)bi.Api);
            foreach (var p in pbs) if (p.Runner == null) { Console.Error.WriteLine("test setup failed: no scripted PB in " + model); return 2; }
            Body fb = friend[0], eb = foe[0];
            Vector3D rockAt = Vector3D.Zero;
            var sensors = new List<SimSensor>();
            foreach (var d in drones) foreach (var b in d) foreach (var bi in b.Blocks) if (bi.Api is SimSensor) sensors.Add((SimSensor)bi.Api);
            if (rock && sensors.Count == 0) { Console.Error.WriteLine("--rock needs a drone with a sensor"); return 2; }
            var minF = new double[count]; var minE = new double[count]; var inside = new int[count];
            for (int i = 0; i < count; i++) { minF[i] = minE[i] = double.MaxValue; }
            long ticks = (long)(secs * SimSettings.TicksPerSecond);
            for (long t = 0; t < ticks; t++)
            {
                if (t == 60) foreach (var p in pbs) host.RunArgument(p, transit ? "guard" : "hunt", UpdateType.Terminal);
                if (beacon && t % 15 == 0)
                    w.Igc.InjectBroadcast("DRONE_SPACE", new MyTuple<long, Vector3D, Vector3D, float>(987654321L, fb.WorldBoundCenter, Vector3D.Zero, (float)fb.BoundRadius));
                if (transit && t >= 60 && t % 15 == 0) w.Igc.InjectBroadcast("HORNET_HOME", new Vector3D(Dbl(args, "--hx", 400), 0, 0));   // --hx = --bx: the carrier link's HOME is the ship's centre
                if (!transit && t >= 60 && t % 15 == 0 && eb.Alive)   // the enemy is cued over IGC (a parked ship 700 m away is beyond what the HUNT sweep finds quickly)
                    w.Igc.InjectBroadcast("HORNET_TGT", new MyTuple<long, Vector3D, Vector3D, double, long>(eb.EntityId, eb.WorldBoundCenter, Vector3D.Zero, eb.BoundRadius, 0));
                w.Step();
                if (rock)   // an asteroid reported by every working sensor with a hit point at the origin (a camera ray would give the same entity)
                    foreach (var s in sensors)
                        if (s.IsWorking) s.Detected.Add(new MyDetectedEntityInfo(555000111L, "Rock", MyDetectedEntityType.Asteroid, new Vector3D(0, 0, 0), MatrixD.Identity, Vector3.Zero,
                            MyRelationsBetweenPlayerAndBlock.NoOwnership, new BoundingBoxD(new Vector3D(-500, -500, -500), new Vector3D(500, 500, 500)), (long)(t * 16)));
                for (int i = 0; i < count; i++)
                {
                    var m = drones[i][0]; if (!m.Alive) continue;
                    double df = (m.WorldBoundCenter - (rock ? rockAt : fb.WorldBoundCenter)).Length(), de = (m.WorldBoundCenter - eb.WorldBoundCenter).Length();
                    if (df < minF[i]) minF[i] = df; if (de < minE[i]) minE[i] = de;
                    if (df < m.BoundRadius + (rock ? 40 : fb.BoundRadius)) inside[i]++;
                }
            }
            bool ok = true;
            Console.WriteLine("avoid-test " + model + " x" + count + " vs parked friendly " + by + " (radius " + fb.BoundRadius.ToString("F1", Inv) + " m)" + (beacon ? " with beacon" : " sighting only") + ", enemy " + (eb.Alive ? "alive" : "destroyed"));
            for (int i = 0; i < count; i++)
            {
                bool dead = pbs[i].Runner.Dead && (pbs[i].Runner.DeathReason ?? "") != "detached" && (pbs[i].Runner.DeathReason ?? "") != "block destroyed";
                bool good = !dead && inside[i] == 0 && minF[i] >= drones[i][0].BoundRadius + (rock ? 40 : fb.BoundRadius) + 10;   // at least 10 m of air between the two bounding spheres
                ok &= good;
                Console.WriteLine(string.Format(Inv, "  drone {0}: closest to friendly {1,6:F1} m (hulls touch at {2:F1}) | inside {3,4:F1} s | closest to enemy {4,6:F0} m | {5}",
                    i, minF[i], drones[i][0].BoundRadius + fb.BoundRadius, inside[i] / (double)SimSettings.TicksPerSecond, minE[i], dead ? "SCRIPT DEAD " + pbs[i].Runner.DeathReason : good ? "ok" : "FAIL"));
            }
            Console.WriteLine(ok ? "AVOID OK" : "AVOID FAILED");
            return ok ? 0 : 1;
        }
    }
}
