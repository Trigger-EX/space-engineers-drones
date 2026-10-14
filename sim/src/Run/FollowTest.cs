// Follow test (FOLLOW mode, tools/follow_block.cs). N copies of a drone run the REAL script and are told "follow"; the owner is a
// virtual point (no body) that flies a scripted path and broadcasts the carrier link's messages on the fleet channel
// ("FLEET_HOME" position + "FLEET_HOMER" radius, every 10 ticks like the Mothership link), or, with --path foot, is a character that
// the drones' sensors report (Relationship Owner) while it is within 50 m of a sensor, with no link at all.
//
//   --owner ship [--ship "Shrike Wing Test"]   (control_arch S12 / M4) the owner is a SPAWNED SHIP (a blueprint with a [Helm] and a [Staff] PB) instead of a virtual point: the
//        drones get HOME / HOMER from its StaffAI (<TYPE>_HOME + FLEET_HOME at the ship's centre of mass, hull radius), nothing is injected. The ship's Helm is told 'idle' (it holds
//        still, dampeners on); paths other than park / fight tow it: its velocity is forced to the path's every tick (position integrates), so the Staff sees a moving ship.
//        Ring checks tighten to ring sd <= --sd-max (1.0 m), plane deviation <= 3 m, orbit rate within 0.1 deg/s of the expected one, nobody inside radius + 5 m.
//   mono SimHost.exe follow-test [--model Hornet] [--count 3] [--path cruise|weave|lost|foot|fight|park] [--speed 40] [--radius 20]
//        [--no-radius] [--stress] [--cmd follow] [--seconds 140] [--seed 1] [--echo] [--trace FILE] [--repo DIR]
//   cruise  0-10 s parked, 10-30 s speeds up along +X, cruise, 60-80 s 90 deg turn, 80-100 s slows to a stop, then parked
//   weave   constant speed, heading swings +/-30 deg (30 s period; --weave-deg, --weave-period) and climbs/dives
//   --stress: pass/fail on hull clearance only (e.g. weave --weave-deg 60 --weave-period 20: the owner turns twice as hard as a drone can)
//   lost    as cruise, but the link goes silent at 50 s: the drones must hold near where the owner was going, not fly back
//   park    owner stationary at the origin (the satellite ring test: --cmd satellite)
//   foot    a character walking at 4 m/s with turns, seen only by the drones' sensors (no link)
//   fight   owner parked; an enemy drone of the same model (HUNT) appears 600 m away at 30 s: drones engage, stay within the
//           leash, and come back afterwards
// Reported per drone after a 25 s settle: distance to the owner (mean / p95 / max), time inside the owner's hull clearance
// (radius + 5 m), closest wingmate, final mode and the PB status line. PASS needs: no time inside the hull, p95 within 3x the slot
// distance, every drone within 1.6x the slot distance at the end, no script death (and for lost: within 150 m of the coast point).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using SeSim.Core;
using SeSim.Blueprint;
using SeSim.Api;

namespace SeSim.Run
{
    public static class FollowTest
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string Arg(string[] a, string k, string d)
        {
            for (int i = 1; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1];
            return d;
        }
        static bool Has(string[] a, string k) { for (int i = 1; i < a.Length; i++) if (a[i] == k) return true; return false; }
        static double Dbl(string[] a, string k, double d) { double r; return double.TryParse(Arg(a, k, null) ?? "", NumberStyles.Float, Inv, out r) ? r : d; }

        sealed class Drone
        {
            public List<Body> Bodies; public SimProgrammableBlock Pb; public string AllCd = "";   // AllCd: the Custom Data of every PB (a split drone's Wingmate Separation Range is in its Flight PB, the Staff is `Pb`)
            public List<SimSensor> Sensors = new List<SimSensor>();
            public List<double> Rad = new List<double>(); public double Ang0, AngPrev, AngSum; public bool AngInit; public double PlaneMax;
            public List<double> Dist = new List<double>(); public int InsideTicks; public double MinMateSettled = double.MaxValue, MinMate = double.MaxValue, MaxDist;
            public Body Main { get { return Pb.Inst.Body; } }
        }

        // owner position / velocity along the chosen path (t in s)
        [ThreadStatic] static double WeaveDeg, WeavePeriod;

        static void Owner(string path, double t, double spd, out Vector3D p, out Vector3D v)
        {
            if (path == "weave")
            {
                // integrate numerically (cheap: the test calls this once per tick in order, but keep it stateless for clarity)
                p = Vector3D.Zero; v = Vector3D.Zero;
                double dt = 1.0 / 60, x = 0;
                Vector3D pos = Vector3D.Zero;
                for (; x < t; x += dt)
                {
                    double h = Math.Sin(x * 2 * Math.PI / WeavePeriod) * WeaveDeg / 57.2958, c = Math.Sin(x * 2 * Math.PI / 45) * 0.2;
                    Vector3D dir = new Vector3D(Math.Cos(h) * Math.Cos(c), Math.Sin(c), Math.Sin(h) * Math.Cos(c));
                    double s = Math.Min(spd, x * 2);
                    v = dir * s; pos += v * dt;
                }
                p = pos; return;
            }
            if (path == "foot")
            {
                double s = 4, dtt = 1.0 / 60; Vector3D pos = Vector3D.Zero; v = Vector3D.Zero;
                for (double x = 0; x < t; x += dtt)
                {
                    double h = x < 15 ? 0 : (x < 45 ? (x - 15) * 0.05 : 1.5 - (x - 45) * 0.03);
                    double ss = x < 5 || (x > 70 && x < 80) ? 0 : s;
                    v = new Vector3D(Math.Cos(h), 0, Math.Sin(h)) * ss; pos += v * dtt;
                }
                p = pos; return;
            }
            if (path == "fight" || path == "park") { p = Vector3D.Zero; v = Vector3D.Zero; return; }
            // cruise / lost: piecewise, integrated the same way
            {
                double dtt = 1.0 / 60; Vector3D pos = Vector3D.Zero; v = Vector3D.Zero; double s = 0, hd = 0;
                for (double x = 0; x < t; x += dtt)
                {
                    if (x < 10) s = 0;
                    else if (x < 80) s = Math.Min(spd, s + 2 * dtt);
                    else s = Math.Max(0, s - 2.5 * dtt);
                    if (x >= 60 && x < 80) hd += (Math.PI / 2) / 20 * dtt;
                    v = new Vector3D(Math.Cos(hd), 0, Math.Sin(hd)) * s; pos += v * dtt;
                }
                p = pos;
            }
        }

        public static int Run(string[] args, string simDir, string repo)
        {
            string model = Arg(args, "--model", "Hornet"), path = Arg(args, "--path", "cruise");
            int count = (int)Dbl(args, "--count", 3);
            double spd = Dbl(args, "--speed", 40), radius = Dbl(args, "--radius", 20);
            bool sendR = !Has(args, "--no-radius");
            double secs = Dbl(args, "--seconds", path == "foot" ? 110 : 140);
            ulong seed = (ulong)Dbl(args, "--seed", 1);
            WeaveDeg = Dbl(args, "--weave-deg", 30); WeavePeriod = Dbl(args, "--weave-period", 30);
            string tracePath = Arg(args, "--trace", null);
            bool shipOwner = Arg(args, "--owner", "point") == "ship";
            string shipName = Arg(args, "--ship", "Shrike Wing Test");
            double sdMax = Dbl(args, "--sd-max", 1.0);
            string cmd = Arg(args, "--cmd", "follow");   // e.g. guard / form, to compare with the other modes; satellite = the vertical ring
            bool satellite = cmd == "satellite" || cmd == "sat" || cmd == "orbit";
            var settings = new SimSettings(); settings.CountInstructions = Has(args, "--count-instructions"); settings.InstructionCountMode = Arg(args, "--instruction-count-mode", "blocks"); settings.EnforceInstructionLimit = Has(args, "--enforce-instruction-limit");
            DefDb defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            var bp = BlueprintLoader.Get(Path.Combine(Path.Combine(repo, "out"), "Blueprints"), model);
            var w = new World(settings, defs, seed);
            var host = new ScriptHost(w, simDir);
            w.Scripts = host;
            w.Teams.Init(2);
            var rng = new Random((int)seed * 7919 + 17);
            var drones = new List<Drone>();
            for (int i = 0; i < count; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2, r = path == "foot" ? 20 + rng.NextDouble() * 20 : 100 + rng.NextDouble() * 200, y = (rng.NextDouble() - 0.5) * (path == "foot" ? 10 : 80);
                Vector3D o = new Vector3D(Math.Cos(a) * r, y, Math.Sin(a) * r);
                var at = new SpawnPlacement { Origin = o, Forward = Vector3D.Normalize(-o), Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero };
                var d = new Drone { Bodies = GridFactory.Spawn(w, bp, 0, i, at, new Dictionary<long, long>()) };
                drones.Add(d);
            }
            BlockApiFactory.FlushPending(w);
            foreach (var d in drones)
                foreach (var b in d.Bodies)
                    foreach (var bi in b.Blocks)
                    {
                        if (bi.Api is SimProgrammableBlock)
                        {   // a drone with two PBs (Wasp Split: [Flight] + [Staff]): the Staff PB is the one that has the FOLLOW screen, the [Follow] Custom Data and takes the commands (it forwards them to Flight)
                            var pbx = (SimProgrammableBlock)bi.Api; d.AllCd += "\n" + (pbx.CustomData ?? "");
                            if (d.Pb == null || ((pbx.CustomName ?? "").StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase) && !(d.Pb.CustomName ?? "").StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase))) d.Pb = pbx;
                        }
                        if (bi.Api is SimSensor) d.Sensors.Add((SimSensor)bi.Api);
                    }
            string flightCd = Arg(args, "--flight-custom-data", null);   // text appended to the Custom Data of every PB whose name starts with [Flight] / [Helm] ("\n" = newline), e.g. "[Thrust Vectoring]\nEnabled=false"
            if (flightCd != null)
                foreach (var d in drones) foreach (var b in d.Bodies) foreach (var bi in b.Blocks)
                {
                    var fp = bi.Api as SimProgrammableBlock;
                    if (fp != null && ((fp.CustomName ?? "").StartsWith("[Flight]", StringComparison.OrdinalIgnoreCase) || (fp.CustomName ?? "").StartsWith("[Helm]", StringComparison.OrdinalIgnoreCase))) fp.CustomData = (fp.CustomData ?? "") + "\n" + flightCd.Replace("\\n", "\n");
                }
            foreach (var d in drones) if (d.Pb == null || d.Pb.Runner == null) { Console.Error.WriteLine("test setup failed: no scripted PB in " + model); return 2; }
            Drone enemy = null;
            Drone ship = null; SimProgrammableBlock shipHelm = null, shipStaff = null;
            if (shipOwner)
            {
                var sbp = BlueprintLoader.Get(Path.Combine(Path.Combine(repo, "out"), "Blueprints"), shipName);
                var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Right, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero };
                ship = new Drone { Bodies = GridFactory.Spawn(w, sbp, 0, count, at, new Dictionary<long, long>()) };
                BlockApiFactory.FlushPending(w);
                foreach (var b in ship.Bodies) foreach (var bi in b.Blocks)
                {
                    var pbb = bi.Api as SimProgrammableBlock; if (pbb == null || pbb.Runner == null) continue;
                    if (pbb.CustomNameValue.StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase)) shipStaff = pbb;
                    else if (pbb.CustomNameValue.StartsWith("[Helm]", StringComparison.OrdinalIgnoreCase)) shipHelm = pbb;
                }
                if (shipStaff == null || shipHelm == null) { Console.Error.WriteLine("test setup failed: " + shipName + " has no [Helm] + [Staff] PB pair"); return 2; }
                ship.Pb = shipStaff;
                radius = ship.Bodies[0].BoundRadius;
                string tagU = model.Split(' ')[0].ToUpperInvariant();
                shipStaff.CustomData = shipStaff.CustomData.Replace("Types=HORNET", "Types=" + tagU);
            }
            var events = SimEvents.Parse(Arg(args, "--events", null));   // "30:!kill-thrust F;..."
            StreamWriter tw = null;
            if (tracePath != null) { tw = new StreamWriter(tracePath, false, new UTF8Encoding(false)); tw.WriteLine("t,owner_x,owner_y,owner_z,owner_speed,drone,dist,speed,mode"); }
            long ticks = (long)(secs * SimSettings.TicksPerSecond);
            double settle = 25, slot = 0, lostAt = path == "lost" ? 50 : -1;
            Vector3D coast = Vector3D.Zero; bool coastSet = false;
            long ownerCharId = 990000001L;
            var sb = new StringBuilder();
            int ownerSeen = 0;
            for (long t = 0; t < ticks; t++)
            {
                double now = t / (double)SimSettings.TicksPerSecond;
                if (shipOwner && t == 0) host.RunArgument(shipStaff, "reload", UpdateType.Terminal);
                if (shipOwner && t == 90) host.RunArgument(shipHelm, "idle", UpdateType.Terminal);   // the Helm starts in HUNT: 'idle' after its first runs, then it holds heading and place
                if (t == 60) foreach (var d in drones) host.RunArgument(d.Pb, cmd, UpdateType.Terminal);
                foreach (var ev in events) if ((long)Math.Round(ev.Key * SimSettings.TicksPerSecond) == t) foreach (var d in drones) Console.WriteLine("t=" + now.ToString("0.0") + " " + SimEvents.Run(d.Main, ev.Value));
                Vector3D op, ov; Owner(path, now, spd, out op, out ov);
                if (shipOwner)
                {
                    var sb0 = ship.Bodies[0];
                    if (path != "park" && path != "fight") sb0.LinearVelocity = ov;   // towed along the path
                    op = sb0.Position; ov = sb0.LinearVelocity;
                }
                bool link = !shipOwner && path != "foot" && (lostAt < 0 || now < lostAt);
                if (lostAt >= 0 && now >= lostAt && !coastSet) { coastSet = true; coast = op + ov * 3; }
                if (link && t % 10 == 0)
                {
                    w.Igc.InjectBroadcast("FLEET_HOME", op);
                    if (sendR) w.Igc.InjectBroadcast("FLEET_HOMER", radius);
                }
                if (path == "fight" && enemy == null && now >= 30)
                {
                    var at = new SpawnPlacement { Origin = new Vector3D(600, 50, 0), Forward = Vector3D.Left, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero };
                    enemy = new Drone { Bodies = GridFactory.Spawn(w, bp, 1, 0, at, new Dictionary<long, long>()) };
                    BlockApiFactory.FlushPending(w);
                    foreach (var b in enemy.Bodies) foreach (var bi in b.Blocks) if (bi.Api is SimProgrammableBlock && enemy.Pb == null) enemy.Pb = (SimProgrammableBlock)bi.Api;
                    if (enemy.Pb != null) host.RunArgument(enemy.Pb, "hunt", UpdateType.Terminal);
                }
                w.Step();
                if (path == "foot")
                {   // the owner's character, reported by every working sensor within 50 m (sensor boxes are +/-50 m)
                    var ch = new MyDetectedEntityInfo(ownerCharId, "Owner", MyDetectedEntityType.CharacterHuman, null, MatrixD.Identity, (Vector3)ov,
                        MyRelationsBetweenPlayerAndBlock.Owner, new BoundingBoxD(op - Vector3D.One, op + Vector3D.One), (long)(now * 1000));
                    foreach (var d in drones)
                        foreach (var s in d.Sensors)
                        {
                            if (!s.IsWorking || (s.Inst.WorldCenter - op).LengthSquared() > 50 * 50) continue;
                            bool have = false; foreach (var e in s.Detected) if (e.EntityId == ownerCharId) { have = true; break; }
                            if (!have) { s.Detected.Add(ch); ownerSeen++; }
                        }
                }
                double clear = (path == "foot" ? 1 : radius) + 5;
                for (int i = 0; i < drones.Count; i++)
                {
                    var d = drones[i];
                    if (!d.Main.Alive) continue;
                    Vector3D dp = d.Main.WorldBoundCenter;
                    double dist = (dp - op).Length();
                    if (dist < clear) d.InsideTicks++;
                    for (int j = i + 1; j < drones.Count; j++)
                        if (drones[j].Main.Alive) { double m = (drones[j].Main.WorldBoundCenter - dp).Length(); if (m < d.MinMate) d.MinMate = m; if (m < drones[j].MinMate) drones[j].MinMate = m; }
                    if (satellite && now >= settle && t % 6 == 0 && path != "cruise")
                    {   // ring metrics in the world frame the drones start from: ring plane spans Y,Z (normal X) for ACROSS
                        Vector3D rel = dp - op; double ang = Math.Atan2(rel.Y, rel.Z);
                        if (!d.AngInit) { d.AngInit = true; d.AngPrev = ang; }
                        double da = ang - d.AngPrev; da -= 2 * Math.PI * Math.Floor((da + Math.PI) / (2 * Math.PI)); d.AngSum += da; d.AngPrev = ang;
                        d.Rad.Add(dist); if (Math.Abs(rel.X) > d.PlaneMax) d.PlaneMax = Math.Abs(rel.X);
                        for (int j = 0; j < drones.Count; j++) if (j != i && drones[j].Main.Alive) { double m = (drones[j].Main.WorldBoundCenter - dp).Length(); if (m < d.MinMateSettled) d.MinMateSettled = m; }
                    }
                    if (now >= settle && t % 6 == 0) { d.Dist.Add(dist); if (dist > d.MaxDist) d.MaxDist = dist; }
                    if (tw != null && t % 30 == 0)
                        tw.WriteLine(string.Format(Inv, "{0:F2},{1:F1},{2:F1},{3:F1},{4:F1},{5},{6:F1},{7:F1},{8}", now, op.X, op.Y, op.Z, ov.Length(), i, dist, d.Main.LinearVelocity.Length(), ModeOf(d.Pb.Runner.EchoText)));
                }
                if (t % 600 == 0)
                {
                    sb.Append(string.Format(Inv, "  t={0,5:F0}s owner ({1,7:F0},{2,5:F0},{3,7:F0}) {4,4:F0} m/s |", now, op.X, op.Y, op.Z, ov.Length()));
                    foreach (var d in drones) sb.Append(string.Format(Inv, " {0,6:F0} m", d.Main.Alive ? (d.Main.WorldBoundCenter - op).Length() : -1));
                    if (satellite) foreach (var d in drones) { Vector3D rl = d.Main.WorldBoundCenter - op; sb.Append(string.Format(Inv, " [{0:F0},{1:F0},{2:F0}]", rl.X, rl.Y, rl.Z)); }
                    if (enemy != null) sb.Append(enemy.Main.Alive ? "  enemy alive" : "  enemy gone");
                    sb.Append('\n');
                }
                bool dead = false; foreach (var d in drones) if (d.Pb.Runner.Dead && !Shot(d)) { sb.Append("  SCRIPT DEAD: " + d.Pb.Runner.DeathReason + "\n"); dead = true; }
                if (dead) break;
            }
            Console.WriteLine("follow-test " + model + " x" + count + ", path " + path + (path != "foot" && path != "fight" ? ", " + spd.ToString("F0", Inv) + " m/s" : "") + ", owner radius " + (sendR ? radius.ToString("F0", Inv) + " m (sent)" : "not sent"));
            Console.Write(sb.ToString());
            Vector3D opEnd, ovEnd; Owner(path, secs, spd, out opEnd, out ovEnd);
            if (shipOwner) { opEnd = ship.Bodies[0].Position; ovEnd = ship.Bodies[0].LinearVelocity; Console.WriteLine("  owner ship " + shipName + ": hull radius " + radius.ToString("F1", Inv) + " m, Staff: " + Line(shipStaff.Runner.EchoText, "WING").Replace("\n", " ")); }
            // slot distance the block aims for (FoDist 60, FoFootDist 20, radius + 30, ring spacing for n drones: see FollowSlot)
            slot = Math.Max(path == "foot" ? 20 : 60, (path == "foot" ? 1 : (sendR ? radius : 25)) + 30);
            double sep = 70;   // the drone's own "Wingmate Separation Range" (Custom Data), which sizes the ring for several drones
            var sm = System.Text.RegularExpressions.Regex.Match(drones[0].AllCd, @"Wingmate Separation Range\s*=\s*([0-9.]+)");
            if (sm.Success) double.TryParse(sm.Groups[1].Value, NumberStyles.Float, Inv, out sep);
            double leash = 1200;   // [Follow] Leash (2200 for the snipers)
            var lm = System.Text.RegularExpressions.Regex.Match(drones[0].Pb.CustomData ?? "", @"\nLeash\s*=\s*([0-9.]+)");
            if (lm.Success) double.TryParse(lm.Groups[1].Value, NumberStyles.Float, Inv, out leash);
            double gap = Math.Max(sep * 1.1, 30) / (2 * Math.Sin(Math.PI / Math.Max(count, 2))) / 0.866;
            if (count > 1) slot = Math.Max(slot, gap);
            bool ok = true;
            for (int i = 0; i < drones.Count; i++)
            {
                var d = drones[i];
                d.Dist.Sort();
                double mean = 0; foreach (var x in d.Dist) mean += x; mean /= Math.Max(1, d.Dist.Count);
                double p95 = d.Dist.Count > 0 ? d.Dist[(int)(d.Dist.Count * 0.95)] : 0;
                double endDist = d.Main.Alive ? (d.Main.WorldBoundCenter - opEnd).Length() : -1;
                string mode = ModeOf(d.Pb.Runner.EchoText);
                string fol = Line(d.Pb.Runner.EchoText, "Follow : ");
                string satLine = Line(d.Pb.Runner.EchoText, "Satellite : ring ");
                double ringR = 0; { var sr = System.Text.RegularExpressions.Regex.Match(satLine, @"^([0-9]+) m"); if (sr.Success) double.TryParse(sr.Groups[1].Value, NumberStyles.Float, Inv, out ringR); }
                if (satellite && ringR > 0) slot = ringR;   // the ring the drones report replaces the FOLLOW slot distance
                bool good = d.Main.Alive && !d.Pb.Runner.Dead && d.InsideTicks == 0 && p95 < slot * 3 && endDist < slot * 1.6;
                if (Has(args, "--stress")) good = !d.Pb.Runner.Dead && d.InsideTicks == 0;   // an owner that out-turns the drones: only the hull counts
                if (satellite) good = d.Main.Alive && !d.Pb.Runner.Dead && d.InsideTicks == 0 && ringR > 0 && Math.Abs(endDist - ringR) < 0.25 * ringR && !(mode == "FOLLOW" ? false : mode == "RTB");
                if (path == "lost") good = d.Main.Alive && !d.Pb.Runner.Dead && (d.Main.WorldBoundCenter - coast).Length() < 150;
                bool crippled = mode == "RTB" || mode == "IDLE" || !d.Main.Alive || Shot(d) || (d.Pb.Runner.EchoText ?? "").Contains("No working Remote Control");   // lost guns / thrust / PB in the fight: not a FOLLOW fault
                bool engaged = !Line(d.Pb.Runner.EchoText, "Target : ").StartsWith("none", StringComparison.Ordinal);   // still fighting at the end
                if (path == "fight") good = (!d.Pb.Runner.Dead || Shot(d)) && (crippled || (d.MaxDist < leash * 1.15 && (engaged || endDist < slot * 1.6)));
                if (path == "fight" && crippled) fol = "(crippled in the fight: " + mode + ")";
                if (satellite && d.Rad.Count > 0 && path == "park")
                {   // park: every sample is on the settled ring: radius spread, plane deviation, orbit rate, spacing
                    double rm = 0; foreach (var x in d.Rad) rm += x; rm /= d.Rad.Count;
                    double sd = 0; foreach (var x in d.Rad) sd += (x - rm) * (x - rm); sd = Math.Sqrt(sd / d.Rad.Count);
                    double rate = d.AngSum / Math.Max(1, secs - settle) * 180 / Math.PI;
                    Console.WriteLine(string.Format(Inv, "    ring: radius {0:F0} +/- {1:F1} m, plane deviation max {2:F0} m, orbit {3:F2} deg/s (expected {4:F2}), closest mate {5:F0} m",
                        rm, sd, d.PlaneMax, rate, SatPeriodDeg(drones[0]), d.MinMateSettled == double.MaxValue ? -1 : d.MinMateSettled));
                    if (shipOwner) good &= sd <= sdMax && d.PlaneMax <= 3 && Math.Abs(Math.Abs(rate) - SatPeriodDeg(drones[0])) <= 0.1;
                    good &= sd < 0.12 * rm && d.PlaneMax < 0.2 * rm && Math.Abs(Math.Abs(rate) - SatPeriodDeg(drones[0])) < 0.5 * SatPeriodDeg(drones[0]) + 0.3
                         && (d.MinMateSettled > 0.5 * 2 * rm * Math.Sin(Math.PI / Math.Max(count, 2)) || count < 2);
                }
                ok &= good;
                Console.WriteLine(string.Format(Inv, "  drone {0}: dist mean {1,5:F0} p95 {2,5:F0} max {3,5:F0} m | end {4,5:F0} m | inside hull {5,4:F1} s | closest mate {6,5:F0} m | mode {7} | {8} {9}",
                    i, mean, p95, d.MaxDist, endDist, d.InsideTicks / (double)SimSettings.TicksPerSecond, d.MinMate == double.MaxValue ? -1 : d.MinMate, mode, fol, good ? "ok" : "FAIL"));
                if (Has(args, "--echo")) Console.WriteLine("--- PB echo ---\n" + d.Pb.Runner.EchoText);
            }
            if (path == "lost") Console.WriteLine(string.Format(Inv, "  coast point ({0:F0},{1:F0},{2:F0}); drones should hold near it", coast.X, coast.Y, coast.Z));
            if (path == "foot") Console.WriteLine("  owner character reports injected into sensors: " + ownerSeen);
            if (path == "fight") Console.WriteLine("  enemy " + (enemy == null ? "never spawned" : enemy.Main.Alive ? "SURVIVED" : "destroyed / split"));
            Console.WriteLine(string.Format(Inv, "  expected slot distance ~{0:F0} m", slot));
            if (tw != null) tw.Dispose();
            if (settings.CountInstructions) IlReport.Dump(host, "follow-test " + model + " " + string.Join(" ", args), args);
            Console.WriteLine(ok ? "FOLLOW OK" : "FOLLOW FAILED");
            return ok ? 0 : 1;
        }

        static double SatPeriodDeg(Drone d)
        {
            var m = System.Text.RegularExpressions.Regex.Match(d.Pb.CustomData ?? "", @"Satellite Orbit Period \(s\)\s*=\s*([0-9.]+)");
            double p = 180; if (m.Success) double.TryParse(m.Groups[1].Value, NumberStyles.Float, Inv, out p);
            return p > 1 ? 360 / p : 0;
        }

        // a PB shot off or destroyed in the fight is a combat loss, not a script failure
        static bool Shot(Drone d) { string r = d.Pb.Runner.DeathReason ?? ""; return r == "detached" || r == "block destroyed"; }

        static string ModeOf(string echo) { return Line(echo, "Mode   : "); }
        static string Line(string echo, string key)
        {
            if (string.IsNullOrEmpty(echo)) return "?";
            int i = echo.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "-";
            i += key.Length; int j = echo.IndexOf('\n', i); if (j < 0) j = echo.Length;
            return echo.Substring(i, j - i).Trim();
        }
    }
}
