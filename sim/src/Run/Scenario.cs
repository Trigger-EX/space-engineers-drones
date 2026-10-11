// SLOT D. Scenario description (data), the per-run ship records and the ScenarioRunner (spawn, command schedule, tick loop).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using SeSim.Core;
using SeSim.Blueprint;
using SeSim.Combat;

namespace SeSim.Run
{
    public sealed class ShipSpec
    {
        public string Model;              // blueprint folder name ("Hornet"), "Stinger" (= projected grid of "Stinger Rack"), "Pirate"
        public int Team;
        public string Role;               // free label for reports ("A1", "B2")
        /// <summary>Commands sent as argument runs, e.g. (0.5 s, "hunt"). Default per model in ScenarioGen.DefaultCommands.</summary>
        public readonly List<KeyValuePair<double, string>> Commands = new List<KeyValuePair<double, string>>();
        public string CustomDataOverride; // optional: replaces the PB CustomData (script config) before construction
    }

    public sealed class Scenario
    {
        public string Name;               // "1v1/Hornet-vs-Gnat", "2v3/..." (stable; used as group key in the CSV)
        public string Kind;               // "1v1", "1v2", "1v3", "2v3", "mixed", "swarm", "pirate"
        public readonly List<TeamSpec> Teams = new List<TeamSpec>();
        public readonly List<ShipSpec> Ships = new List<ShipSpec>();
        public double StartDistanceMin = 1500, StartDistanceMax = 3000;   // between team centroids (HANDOFF 9)
        public double TeamSpread = 60;    // ships of one team start within this radius of their centroid
        public bool RandomOrientation = true;
        /// <summary>After placement, turn every ship so its controller's Forward points at the nearest enemy team centroid
        /// (keeps the random roll). Used for the "pilot points the drone and presses lock" validation runs (`--face`).</summary>
        public bool FaceEnemies;
        public int MaxTicks = 5 * 60 * 60;
        public double DecisiveHoldSeconds = 5;   // a side must stay beaten this long before early termination
    }

    /// <summary>Runtime record of one spawned ship (main Body + its PBs).</summary>
    public sealed class ShipInstance
    {
        public int Index; public ShipSpec Spec; public int Team;
        public Body Main;                                  // largest body carrying the ship's control (re-pointed after splits)
        public readonly List<Body> Bodies = new List<Body>();
        public readonly List<PbRunner> Pbs = new List<PbRunner>();
        public readonly List<BlockInst> Controllers = new List<BlockInst>();   // remote controls / cockpits at spawn
        public bool HasBrain;                              // Midge (vanilla AI) / Pirate: no PB and no controller needed to be capable
        public double WeaponRange;                         // longest ammo range of its guns (m); 0 = none
        public double InitialMass; public int InitialBlocks;
        public bool Disabled, Destroyed; public double DisabledAt = -1, DestroyedAt = -1;
        /// <summary>Combat-capable = has a working (PB running OR a brain) + controller + battery charge + any working thruster
        /// + any functional weapon or warhead. Evaluated every 10 ticks by Metrics.</summary>
        public bool CombatCapable;
    }

    public sealed class RunOptions
    {
        public TextWriter Trace;          // per-sample CSV (see ScenarioRunner.WriteTraceHeader)
        public int TraceEvery = 6;        // ticks
        public string SimDir;             // default: directory of SimHost.exe
        public TextWriter Log;            // progress / diagnostics for `one`
        public Action<World, ScriptHost, List<ShipInstance>> Inspect;   // called after the tick loop (before Finish), e.g. to print PB echo text
        public Action<World, ScriptHost, List<ShipInstance>> OnTick;    // debugging hook, called after every World.Step
    }

    /// <summary>Spawns a Scenario into a fresh World and runs it to completion. One instance per run (single thread).</summary>
    public sealed class ScenarioRunner
    {
        public static ulong ScenarioSeed(ulong baseSeed, string name, int seedIndex)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < name.Length; i++) { h ^= name[i]; h *= 1099511628211UL; }
            return DetRng.Mix(DetRng.Mix(baseSeed, h), (ulong)seedIndex + 1);
        }

        public static string DefaultSimDir() { return Path.GetDirectoryName(typeof(ScenarioRunner).Assembly.Location); }

        public static RunResult RunOne(Scenario sc, int seedIndex, ulong baseSeed, SimSettings settings, string repoRoot)
        {
            return RunOne(sc, seedIndex, baseSeed, settings, repoRoot, null);
        }

        // pirate blueprint cache (built in code by slot C, immutable afterwards)
        static readonly object PirateLock = new object();
        static BlueprintData _pirateBp; static DefDb _pirateDefs;

        static BlueprintData PirateBlueprint(DefDb defs)
        {
            lock (PirateLock)
            {
                if (_pirateBp == null || _pirateDefs != defs) { _pirateBp = PirateShipFactory.Build(defs); _pirateDefs = defs; }
                return _pirateBp;
            }
        }

        struct Cmd { public long Tick; public ShipInstance Ship; public string Arg; }

        public static RunResult RunOne(Scenario sc, int seedIndex, ulong baseSeed, SimSettings settings, string repoRoot, RunOptions opt)
        {
            if (opt == null) opt = new RunOptions();
            string simDir = opt.SimDir ?? DefaultSimDir();
            var s = settings.Clone();
            ulong seed = ScenarioSeed(baseSeed, sc.Name, seedIndex);
            long maxTicks = Math.Min(sc.MaxTicks, s.MaxTicks);
            var ships = new List<ShipInstance>();
            Metrics m = new Metrics(BuildShipList(sc, ships));
            var sw = Stopwatch.StartNew();
            World w = null; ScriptHost host = null;
            string error = null; bool timedOut = false;
            try
            {
                DefDb defs = DefDb.LoadCached(Path.Combine(repoRoot, "defs_stats.json"));
                string bpDir = Path.Combine(Path.Combine(repoRoot, "out"), "Blueprints");
                w = new World(s, defs, seed);
                w.Events = m;
                host = new ScriptHost(w, simDir);
                w.Scripts = host;
                w.Teams.Init(Math.Max(2, sc.Teams.Count));
                for (int i = 0; i < sc.Teams.Count; i++)
                    for (int k = 0; k < sc.Teams[i].AlliedWith.Count; k++) w.Teams.SetAllied(i, sc.Teams[i].AlliedWith[k]);

                var cmds = SpawnAll(w, sc, ships, defs, bpDir, host, seed);
                m.Setup(w, sc, host);
                if (opt.Trace != null) WriteTraceHeader(opt.Trace, ships);

                int ci = 0;
                while (!m.Finished && w.Tick < maxTicks)
                {
                    while (ci < cmds.Count && cmds[ci].Tick <= w.Tick)
                    {
                        var c = cmds[ci++];
                        for (int k = 0; k < c.Ship.Pbs.Count; k++) host.RunArgument(c.Ship.Pbs[k].Pb, c.Arg, UpdateType.Terminal);
                    }
                    w.Step();
                    if (opt.OnTick != null) opt.OnTick(w, host, ships);
                    if (opt.Trace != null && w.Tick % Math.Max(1, opt.TraceEvery) == 0) WriteTraceRow(opt.Trace, w, ships, m);
                }
                timedOut = !m.Finished;
                if (opt.Inspect != null) opt.Inspect(w, host, ships);
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace;
            }
            RunResult res = m.Result;
            res.Scenario = sc.Name; res.Kind = sc.Kind; res.SeedIndex = seedIndex; res.Seed = seed;
            res.SettingsHash = s.Hash();
            try
            {
                if (w != null) m.Finish(w, timedOut, error);
                else { res.Outcome = "error"; res.Error = error; }
            }
            catch (Exception e)
            {
                res.Outcome = "error"; res.Error = (error ?? "") + "\nfinish: " + e;
            }
            sw.Stop();
            res.WallMs = sw.Elapsed.TotalMilliseconds;
            res.CpuMs = res.WallMs;        // one run = one thread = one core; per-thread CPU time is not exposed by Mono
            if (opt.Trace != null) opt.Trace.Flush();
            return res;
        }

        static List<ShipInstance> BuildShipList(Scenario sc, List<ShipInstance> ships)
        {
            for (int i = 0; i < sc.Ships.Count; i++)
                ships.Add(new ShipInstance { Index = i, Spec = sc.Ships[i], Team = sc.Ships[i].Team });
            return ships;
        }

        // ---------------------------------------------------------------------------------------------------------
        // spawn
        // ---------------------------------------------------------------------------------------------------------
        static List<Cmd> SpawnAll(World w, Scenario sc, List<ShipInstance> ships, DefDb defs, string bpDir, ScriptHost host, ulong seed)
        {
            var rng = new DetRng(DetRng.Mix(seed, 0xD5CE));
            int n = ships.Count;
            int nt = Math.Max(2, sc.Teams.Count);

            // 1. spawn every ship at the origin with a random orientation
            for (int i = 0; i < n; i++)
            {
                var sh = ships[i];
                var spec = sh.Spec;
                Vector3D fwd = RandomUnit(rng), up;
                if (sc.RandomOrientation)
                {
                    Vector3D u = RandomUnit(rng);
                    up = u - fwd * Vector3D.Dot(u, fwd);
                    if (up.LengthSquared() < 1e-6) up = Math.Abs(fwd.Y) < 0.9 ? Vector3D.Up : Vector3D.Right;
                    up.Normalize();
                    // Up must be exactly orthogonal to Forward
                    up = up - fwd * Vector3D.Dot(up, fwd); up.Normalize();
                }
                else { fwd = Vector3D.Forward; up = Vector3D.Up; }
                var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = fwd, Up = up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero };
                var idMap = new Dictionary<long, long>();
                List<Body> bodies;
                if (spec.Model == "Pirate")
                {
                    bodies = GridFactory.Spawn(w, PirateBlueprint(defs), spec.Team, i, at, idMap);
                    sh.HasBrain = true;
                }
                else if (spec.Model == "Stinger")
                {
                    var bp = BlueprintLoader.Get(bpDir, "Stinger Rack");
                    bodies = GridFactory.SpawnProjected(w, bp, 0, spec.Team, i, at, idMap);
                }
                else
                {
                    var bp = BlueprintLoader.Get(bpDir, spec.Model);
                    bodies = GridFactory.Spawn(w, bp, spec.Team, i, at, idMap);
                    if (spec.Model == "Midge") sh.HasBrain = true;
                }
                sh.Bodies.AddRange(bodies);
                sh.Main = bodies[0];
                for (int k = 0; k < bodies.Count; k++) { sh.InitialMass += bodies[k].Mass; sh.InitialBlocks += bodies[k].Blocks.Count; }
                for (int k = 0; k < bodies.Count; k++)
                {
                    var bl = bodies[k].Blocks;
                    for (int q = 0; q < bl.Count; q++)
                    {
                        var b = bl[q];
                        if (b.Api is SeSim.Api.SimShipController) sh.Controllers.Add(b);
                        if (b.Def.Weapon != null && b.Def.Weapon.Ammo != null && b.Def.Weapon.Ammo.MaxTrajectory > sh.WeaponRange) sh.WeaponRange = b.Def.Weapon.Ammo.MaxTrajectory;
                    }
                }
                if (spec.Model == "Midge") w.Brains.Add(new VanillaAiBrain(sh.Main));
                else if (spec.Model == "Pirate") w.Brains.Add(new PirateBrain(sh.Main));
                else if (HasAiBlocks(sh.Main)) w.Brains.Add(new VanillaAiBrain(sh.Main) { ScriptShip = true });   // v1.8 hybrid search: the script toggles the AI blocks
            }

            // 2. placement: team centroids on a polygon, ships in a sphere around them, pairwise separation
            double dist = rng.Range(sc.StartDistanceMin, sc.StartDistanceMax);
            Vector3D axis = RandomUnit(rng);
            Vector3D e1 = Vector3D.Cross(axis, Math.Abs(axis.Y) < 0.9 ? Vector3D.Up : Vector3D.Right); e1.Normalize();
            Vector3D e2 = Vector3D.Cross(axis, e1);
            var centroid = new Vector3D[nt];
            if (nt == 2) { centroid[0] = axis * (-dist / 2); centroid[1] = axis * (dist / 2); }
            else
            {
                double r = dist / (2 * Math.Sin(Math.PI / nt));
                for (int t = 0; t < nt; t++) { double a = 2 * Math.PI * t / nt; centroid[t] = (e1 * Math.Cos(a) + e2 * Math.Sin(a)) * r; }
            }
            var counts = new int[nt];
            for (int i = 0; i < n; i++) counts[ships[i].Team]++;
            var placed = new List<int>();
            var pos = new Vector3D[n];
            for (int i = 0; i < n; i++)
            {
                var sh = ships[i];
                double spread = sc.TeamSpread * Math.Sqrt(Math.Max(1.0, counts[sh.Team] / 2.0));
                double ri = sh.Main.BoundRadius;
                Vector3D p = centroid[sh.Team];
                for (int attempt = 0; attempt < 400; attempt++)
                {
                    if (attempt > 0 && attempt % 40 == 0) spread *= 1.25;
                    Vector3D c = centroid[sh.Team] + RandomInBall(rng) * spread;
                    bool ok = true;
                    for (int k = 0; k < placed.Count && ok; k++)
                    {
                        int j = placed[k];
                        double sep = Math.Max(25.0, ri + ships[j].Main.BoundRadius + 10.0);
                        if ((c - pos[j]).LengthSquared() < sep * sep) ok = false;
                    }
                    p = c;
                    if (ok) break;
                }
                pos[i] = p; placed.Add(i);
                Vector3D delta = p - sh.Main.Position;
                for (int k = 0; k < sh.Bodies.Count; k++) { sh.Bodies[k].Position += delta; sh.Bodies[k].UpdateWorldMatrix(); }
            }

            if (sc.FaceEnemies)
                for (int i = 0; i < n; i++)
                {
                    var sh = ships[i];
                    Vector3D tgt = default(Vector3D); double best = double.MaxValue;
                    for (int t = 0; t < nt; t++)
                    {
                        if (t == sh.Team || counts[t] == 0) continue;
                        double d2 = (centroid[t] - pos[i]).LengthSquared();
                        if (d2 < best) { best = d2; tgt = centroid[t]; }
                    }
                    if (best == double.MaxValue) continue;
                    // the enemy's actual position when it is a single ship (exact boresight for 1v1)
                    for (int j = 0; j < n; j++) if (ships[j].Team != sh.Team && counts[ships[j].Team] == 1 && (centroid[ships[j].Team] - tgt).LengthSquared() < 1e-6) tgt = ships[j].Main.WorldBoundCenter;
                    FaceTowards(sh, tgt);
                }

            // 3. script configuration, then attach every PB (constructors run here, after the whole world exists)
            for (int i = 0; i < n; i++)
            {
                var sh = ships[i];
                if (sh.Spec.CustomDataOverride == null) continue;
                for (int k = 0; k < sh.Bodies.Count; k++)
                {
                    var bl = sh.Bodies[k].Blocks;
                    for (int q = 0; q < bl.Count; q++)
                    {
                        var pb = bl[q].Api as SeSim.Api.SimProgrammableBlock;
                        if (pb != null) pb.CustomData = sh.Spec.CustomDataOverride;
                    }
                }
            }
            SeSim.Api.BlockApiFactory.FlushPending(w);
            for (int r = 0; r < host.Runners.Count; r++)
            {
                int si = host.Runners[r].ShipIndex;
                if ((uint)si < (uint)n) ships[si].Pbs.Add(host.Runners[r]);
            }

            // 4. command schedule
            var cmds = new List<Cmd>();
            for (int i = 0; i < n; i++)
            {
                var spec = ships[i].Spec;
                for (int k = 0; k < spec.Commands.Count; k++)
                    cmds.Add(new Cmd { Tick = (long)Math.Round(spec.Commands[k].Key * SimSettings.TicksPerSecond), Ship = ships[i], Arg = spec.Commands[k].Value });
            }
            // stable sort by tick (insertion sort keeps spec order for equal ticks)
            for (int a = 1; a < cmds.Count; a++)
            {
                var c = cmds[a]; int b = a - 1;
                while (b >= 0 && cmds[b].Tick > c.Tick) { cmds[b + 1] = cmds[b]; b--; }
                cmds[b + 1] = c;
            }
            return cmds;
        }

        static void FaceTowards(ShipInstance sh, Vector3D target)
        {
            Body m = sh.Main;
            Vector3D fl = Vector3D.Forward;
            for (int q = 0; q < m.Blocks.Count; q++) if (m.Blocks[q].Api is SeSim.Api.SimShipController) { fl = m.Blocks[q].LocalMatrix.Forward; break; }
            // aim the controller's forward ray from the ship's bound centre at the target
            Vector3D from = m.DirToWorld(fl), to = target - m.WorldBoundCenter;
            if (to.LengthSquared() < 1e-6) return;
            to.Normalize();
            Vector3D axis = Vector3D.Cross(from, to);
            double sn = axis.Length(), cs = Vector3D.Dot(from, to);
            if (sn < 1e-9) { if (cs > 0) return; axis = Math.Abs(from.X) < 0.9 ? Vector3D.Cross(from, Vector3D.Right) : Vector3D.Cross(from, Vector3D.Up); sn = axis.Length(); }
            axis /= sn;
            double ang = Math.Atan2(sn, cs);
            MatrixD best = m.RotMatrix; double bestDot = -2;
            foreach (double sg in new[] { 1.0, -1.0 })
            {
                MatrixD r = m.RotMatrix * MatrixD.CreateFromAxisAngle(axis, sg * ang);
                double d = Vector3D.Dot(Vector3D.TransformNormal(fl, r), to);
                if (d > bestDot) { bestDot = d; best = r; }
            }
            MatrixD delta = MatrixD.Transpose(m.RotMatrix) * best;     // world-space rotation applied to every body of the ship
            Vector3D pivot = m.Position;
            for (int k = 0; k < sh.Bodies.Count; k++)
            {
                Body b = sh.Bodies[k];
                Vector3D rel = Vector3D.TransformNormal(b.Position - pivot, delta);
                b.Position = pivot + rel;
                b.Rotation = QuaternionD.CreateFromRotationMatrix(b.RotMatrix * delta);
                b.Rotation.Normalize();
                b.UpdateWorldMatrix();
            }
        }

        static bool HasAiBlocks(Body b)
        {
            for (int i = 0; i < b.Blocks.Count; i++)
            {
                var g = b.Blocks[i].Api as SeSim.Api.SimGenericFunctional;
                if (g != null && (g.AiKind == "OffensiveCombat" || g.AiKind == "FlightMovement")) return true;
            }
            return false;
        }

        static Vector3D RandomUnit(DetRng rng)
        {
            for (int i = 0; i < 100; i++)
            {
                var v = new Vector3D(rng.NextGaussian(), rng.NextGaussian(), rng.NextGaussian());
                double l = v.Length();
                if (l > 1e-6) return v / l;
            }
            return Vector3D.Forward;
        }

        static Vector3D RandomInBall(DetRng rng)
        {
            Vector3D u = RandomUnit(rng);
            return u * Math.Pow(rng.NextDouble(), 1.0 / 3.0);
        }

        // ---------------------------------------------------------------------------------------------------------
        // trace
        // ---------------------------------------------------------------------------------------------------------
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static void WriteTraceHeader(TextWriter tw, List<ShipInstance> ships)
        {
            var sb = new StringBuilder("t");
            for (int i = 0; i < ships.Count; i++)
            {
                string p = "s" + i + "_";
                foreach (string c in new[] { "x", "y", "z", "vx", "vy", "vz", "speed", "w", "blocks", "cap", "shots", "hits", "dmgtaken", "enemy_dist", "closing", "aim_err_deg", "ai_on", "script_tgt" })
                    sb.Append(',').Append(p).Append(c);
            }
            tw.WriteLine(sb.ToString());
        }

        /// <summary>Angle (deg) between the best fixed gun axis and the nearest live enemy main body; 180 when none.</summary>
        static double AimError(Body b, Body e)
        {
            double best = 180;
            if (e == null) return best;
            for (int k = 0; k < b.Weapons.Count; k++)
            {
                var g = b.Weapons[k];
                if (g.Block.Turret != null || g.Block.Destroyed) continue;
                Vector3D ax = b.DirToWorld(g.DirLocal);
                Vector3D to = e.WorldBoundCenter - b.LocalToWorld(g.MuzzleLocal);
                double l = to.Length(); if (l < 1e-6) continue;
                double c = Vector3D.Dot(ax, to / l); if (c > 1) c = 1; if (c < -1) c = -1;
                double a = Math.Acos(c) * 180 / Math.PI;
                if (a < best) best = a;
            }
            return best;
        }

        static void WriteTraceRow(TextWriter tw, World w, List<ShipInstance> ships, Metrics m)
        {
            var sb = new StringBuilder();
            sb.Append(w.Time.ToString("F3", Inv));
            for (int i = 0; i < ships.Count; i++)
            {
                var sh = ships[i]; var st = m.Result.Ships[i];
                var b = sh.Main;
                Body enemy = null; double ed = double.MaxValue;
                if (b != null && b.Alive)
                    for (int j = 0; j < ships.Count; j++)
                    {
                        var o = ships[j].Main;
                        if (ships[j].Team == sh.Team || o == null || !o.Alive) continue;
                        double d = (o.Position - b.Position).Length();
                        if (d < ed) { ed = d; enemy = o; }
                    }
                if (b != null && b.Alive)
                {
                    sb.Append(',').Append(b.Position.X.ToString("F1", Inv)).Append(',').Append(b.Position.Y.ToString("F1", Inv)).Append(',').Append(b.Position.Z.ToString("F1", Inv))
                      .Append(',').Append(b.LinearVelocity.X.ToString("F1", Inv)).Append(',').Append(b.LinearVelocity.Y.ToString("F1", Inv)).Append(',').Append(b.LinearVelocity.Z.ToString("F1", Inv))
                      .Append(',').Append(b.LinearVelocity.Length().ToString("F1", Inv)).Append(',').Append(b.AngularVelocity.Length().ToString("F2", Inv)).Append(',').Append(b.Blocks.Count);
                }
                else sb.Append(",,,,,,,,,0");
                sb.Append(',').Append(sh.CombatCapable ? 1 : 0).Append(',').Append(st.Shots).Append(',').Append(st.Hits).Append(',').Append(st.DamageTaken.ToString("F0", Inv));
                if (enemy != null)
                {
                    Vector3D rel = enemy.Position - b.Position;
                    double closing = -Vector3D.Dot(enemy.LinearVelocity - b.LinearVelocity, rel / ed);
                    sb.Append(',').Append(ed.ToString("F1", Inv)).Append(',').Append(closing.ToString("F1", Inv)).Append(',').Append(AimError(b, enemy).ToString("F2", Inv));
                }
                else sb.Append(",,,");
                // AI blocks enabled (hybrid search active) and whether the script shows a target on its status screen
                int ai = 0;
                if (b != null && b.Alive)
                    for (int k = 0; k < b.Blocks.Count; k++)
                    {
                        var g = b.Blocks[k].Api as SeSim.Api.SimGenericFunctional;
                        if (g != null && g.AiKind == "OffensiveCombat" && g.EnabledValue) { ai = 1; break; }
                    }
                int tgt = -1;
                for (int k = 0; k < sh.Pbs.Count; k++)
                {
                    string e = sh.Pbs[k].EchoText;
                    if (e == null) continue;
                    int ti = e.IndexOf("Target", StringComparison.Ordinal);
                    if (ti >= 0) { tgt = e.IndexOf("none", ti, Math.Min(30, e.Length - ti), StringComparison.Ordinal) >= 0 ? 0 : 1; break; }
                }
                sb.Append(',').Append(ai).Append(',').Append(tgt);
            }
            tw.WriteLine(sb.ToString());
        }
    }
}
