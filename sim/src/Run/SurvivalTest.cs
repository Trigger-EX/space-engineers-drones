// Survival test harness (docs/design/survivability.md, work package F2). One command, modes:
//
//   mono SimHost.exe survival-test --mode medic [--bp DIR] [--seconds 150] [--seed 1] [--gap 150] [--damage-n 4] [--damage-frac 0.6]
//        [--hold-min 8] [--weld-s 12] [--command repair] [--no-stock] [--echo] [--trace FILE] [--repo DIR]
//   --bp DIR       blueprint folder holding "Hornet" and "Medic" (default <repo>/out/Blueprints), e.g. one built with
//                  python3 -I tools/build_all.py --no-zip --only Hornet Medic --out <dir>   (then --bp <dir>/Blueprints)
//   medic          A parked, partly damaged Hornet (--damage-n armour blocks cut to --damage-frac of their integrity, set directly: no event, no
//                  loss of function) is sent the word --command (default "repair": its REPAIR CLIENT asks for a medic) and a Medic starts --gap m away
//                  in IDLE with its blueprint defaults. The sim welds nothing, so the harness plays the weld: once the Medic has held its welder
//                  tip within 1.5 m of a damaged block for --weld-s seconds in a row, every damaged block is restored to full integrity and the patient's projector set to 0 blocks remaining (the patient's
//                  re-scan then finds nothing left, sends WORK with the done bit, and the Medic must go RTB and back to IDLE near its home).
//   Stages reported (times in s): REQ/ACK (the patient's status reads "medic working"), the Medic's modes GO, APPROACH, WORK, the tip held
//   (< 1.5 m, longest unbroken hold and total, must reach --hold-min), welders on in WORK, max relative speed with the tip within 3 m of a block
//   (<= 1 m/s), no collision / collision damage on either ship, writer audit 0 (SimEvents.WriterAudit), repaired (played), RTB, IDLE again within 25 m of home.
//   PASS needs every stage; a FAIL line names the stages missed. The run stops 6 s after the Medic is back in IDLE, or at --seconds.
//   --no-stock     leave the Medic's cargo as the blueprint spawns it (otherwise the [Stock] container is filled to 60 %, the sim does not load the
//                  blueprint's items)
//   failover       not here: PB failover (Flight off then on, Staff limp-home, writer audit) is covered by "one --events 30:!disable-pb Flight;45:!enable-pb Flight"
//                  (survivability.md item 5, F1); this command only prints that and exits 2.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using SeSim.Core;
using SeSim.Blueprint;
using SeSim.Api;

namespace SeSim.Run
{
    public static class SurvivalTest
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Arg(string[] a, string k, string d) { for (int i = 1; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1]; return d; }
        static bool Has(string[] a, string k) { for (int i = 1; i < a.Length; i++) if (a[i] == k) return true; return false; }
        static double Dbl(string[] a, string k, double d) { double r; return double.TryParse(Arg(a, k, null) ?? "", NumberStyles.Float, Inv, out r) ? r : d; }

        sealed class Sink : IEventSink
        {
            public int Collisions, DamageEvents; public double Dmg, FirstAt = -1; public string First = "";
            public void On(ref SimEvent e)
            {
                bool mine = (e.ShipA == 0 || e.ShipA == 1 || e.ShipB == 0 || e.ShipB == 1);
                if (!mine) return;
                if (e.Kind == EventKind.Collision) { Collisions++; if (FirstAt < 0) { FirstAt = e.Tick / SimSettings.TicksPerSecond; First = "ships " + e.ShipA + "-" + e.ShipB + " rel speed " + e.Value.ToString("F1", Inv); } }
                if (e.Kind == EventKind.CollisionDamage) { DamageEvents++; Dmg += e.Value; if (FirstAt < 0) FirstAt = e.Tick / SimSettings.TicksPerSecond; }
            }
            public void EndTick(World w) { }
        }

        static int PbRank(SimProgrammableBlock p)
        {
            string n = p.CustomNameValue ?? "";
            return n.StartsWith("[Staff]", StringComparison.OrdinalIgnoreCase) ? 3 : n.StartsWith("[Thrust]", StringComparison.OrdinalIgnoreCase) || n.StartsWith("[Aux]", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }

        static string MedicMode(string echo)
        {   // first line "MEDIC v1.0  GO  WELD"
            if (string.IsNullOrEmpty(echo)) return null;
            int i = echo.LastIndexOf("MEDIC v", StringComparison.Ordinal); if (i < 0) return null;
            int j = echo.IndexOf('\n', i); string line = echo.Substring(i, (j < 0 ? echo.Length : j) - i);
            string[] t = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return t.Length >= 3 ? t[2] : null;
        }

        public static int Run(string[] args, string simDir, string repo)
        {
            string mode = Arg(args, "--mode", "medic");
            if (mode == "failover")
            {
                Console.WriteLine("survival-test --mode failover is not implemented: use  SimHost.exe one ... --events \"30:!disable-pb Flight;45:!enable-pb Flight\"  (item 5, writer audit is printed by every run)");
                return 2;
            }
            if (mode != "medic") throw new ArgumentException("survival-test --mode medic|failover");
            return Medic(args, simDir, repo);
        }

        static int Medic(string[] args, string simDir, string repo)
        {
            double secs = Dbl(args, "--seconds", 150), gap = Dbl(args, "--gap", 150), holdMin = Dbl(args, "--hold-min", 8), weldS = Dbl(args, "--weld-s", 12), frac = Dbl(args, "--damage-frac", 0.6);
            int nDmg = (int)Dbl(args, "--damage-n", 4); ulong seed = (ulong)Dbl(args, "--seed", 1);
            string word = Arg(args, "--command", "repair"), tracePath = Arg(args, "--trace", null);
            string bpDir = Arg(args, "--bp", Path.Combine(Path.Combine(repo, "out"), "Blueprints"));
            var settings = new SimSettings();
            DefDb defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            // defs_stats.json carries no inventory volume for the cargo containers: the sim then gives them none and the Medic reads "stock 0 L". Harness-only fix,
            // before spawning: the game's small-grid volumes (m3, inventory multiplier 1); the Medic carries one SmallBlockLargeContainer (15,625 L).
            foreach (var d in defs.Blocks.Values)
                if (d.TypeId == "CargoContainer" && d.InventoryVolume <= 0 && d.Subtype.StartsWith("SmallBlock", StringComparison.Ordinal))
                    d.InventoryVolume = d.Subtype == "SmallBlockLargeContainer" ? 15.625f : d.Subtype == "SmallBlockMediumContainer" ? 3.375f : 0.125f;
            var hbp = BlueprintLoader.Get(bpDir, "Hornet"); var mbp = BlueprintLoader.Get(bpDir, "Medic");
            var w = new World(settings, defs, seed);
            var host = new ScriptHost(w, simDir);
            w.Scripts = host; w.Teams.Init(2);
            var sink = new Sink(); w.Events = sink;
            var patient = GridFactory.Spawn(w, hbp, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Right, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero }, new Dictionary<long, long>());
            var medic = GridFactory.Spawn(w, mbp, 0, 1, new SpawnPlacement { Origin = new Vector3D(-gap * 0.8, gap * 0.15, gap * 0.6), Forward = Vector3D.Right, Up = Vector3D.Up, LinearVelocity = Vector3D.Zero, AngularVelocity = Vector3D.Zero }, new Dictionary<long, long>());
            foreach (var list in new[] { patient, medic }) foreach (var b in list) foreach (var bi in b.Blocks)
            {
                var pbx = bi.Api as SimProgrammableBlock;
                if (pbx != null && !string.IsNullOrEmpty(bi.Src.StoredCustomData)) pbx.CustomData = bi.Src.StoredCustomData.Replace("\r", "");
            }
            BlockApiFactory.FlushPending(w);
            Body pBody = patient[0], mBody = medic[0];
            SimProgrammableBlock staff = null, medPb = null; var hornetPbs = new List<SimProgrammableBlock>();
            foreach (var b in patient) foreach (var bi in b.Blocks) { var p = bi.Api as SimProgrammableBlock; if (p == null) continue; hornetPbs.Add(p); if (staff == null || PbRank(p) > PbRank(staff)) staff = p; }
            foreach (var b in medic) foreach (var bi in b.Blocks) { var p = bi.Api as SimProgrammableBlock; if (p != null && medPb == null) medPb = p; }
            if (staff == null || medPb == null || medPb.Runner == null) { Console.Error.WriteLine("test setup failed: Hornet Staff PB or Medic PB (with a script) not found"); return 2; }
            var welders = new List<BlockInst>(); BlockInst tipBlock = null;
            foreach (var b in medic) foreach (var bi in b.Blocks) if (bi.Api is SimShipWelder) { welders.Add(bi); if (tipBlock == null) tipBlock = bi; }
            if (tipBlock == null) { Console.Error.WriteLine("test setup failed: the Medic has no ship welder"); return 2; }
            // damage: armour-like blocks, spread over the hull, set directly (no event, stays functional)
            var cand = new List<BlockInst>();
            foreach (var b in patient) foreach (var bi in b.Blocks)
                if (!bi.Destroyed && bi.Api == null && (bi.Src.TypeId == "CubeBlock" || (bi.Def.Subtype ?? "").IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0)) cand.Add(bi);
            var hurt = new List<BlockInst>();
            for (int k = 0; k < nDmg && cand.Count > 0; k++) hurt.Add(cand[(int)((k + 0.5) * cand.Count / nDmg) % cand.Count]);
            foreach (var bi in hurt) bi.Hp = (float)(bi.MaxHp * frac);
            // stock: does the sim let a harness fill the Medic's containers? (report only)
            string stockNote = StockNote(medic, Has(args, "--no-stock"));
            var projectors = new List<SimProjector>();
            foreach (var b in patient) foreach (var bi in b.Blocks) if (bi.Api is SimProjector) projectors.Add((SimProjector)bi.Api);
            Vector3D home = mBody.WorldBoundCenter;
            Console.WriteLine("survival-test medic: Hornet " + pBody.Blocks.Count + " blocks, staff PB '" + staff.CustomNameValue + "' (" + staff.Runner.ScriptName + "), " + hornetPbs.Count + " PBs; Medic " + mBody.Blocks.Count + " blocks, script " + medPb.Runner.ScriptName
                + ", " + welders.Count + " welder(s); " + hurt.Count + " blocks damaged to " + (frac * 100).ToString("F0", Inv) + " %, medic " + gap.ToString("F0", Inv) + " m away; " + stockNote);
            StreamWriter tw = null;
            if (tracePath != null) { tw = new StreamWriter(tracePath, false, new UTF8Encoding(false)); tw.WriteLine("t,mode,dist_centres,tip_dist,rel_speed,medic_speed,welding,patient_status"); }

            double ackAt = -1, goAt = -1, appAt = -1, workAt = -1, rtbAt = -1, idleAfterAt = -1, repairedAt = -1, wasHeld = 0, holdRun = 0, holdMax = 0, holdTotal = 0, minTip = 1e9, maxRelClose = 0, maxRelTip = 0, minCentres = 1e9, doneSeen = -1;
            bool weldOn = false, repaired = false, cmdSent = false, gaveUp = false, dead = false; string deathWhy = "";
            string lastMode = "?", lastStatus = ""; var sb = new StringBuilder();
            long ticks = (long)(secs * SimSettings.TicksPerSecond), stopAt = -1;
            double dt = 1.0 / SimSettings.TicksPerSecond;
            for (long t = 0; t < ticks; t++)
            {
                double now = t * dt;
                if (!cmdSent && now >= 1) { cmdSent = true; host.RunArgument(staff, word, UpdateType.Terminal); sb.Append(string.Format(Inv, "  t={0,6:F1}s word '{1}' to {2}\n", now, word, staff.CustomNameValue)); }
                w.Step();
                if (t % 6 != 0) continue;
                now = w.Time;
                string md = MedicMode(medPb.Runner.EchoText);
                if (md != null && md != lastMode)
                {
                    sb.Append(string.Format(Inv, "  t={0,6:F1}s medic {1} -> {2}\n", now, lastMode, md)); lastMode = md;
                    if (md == "GO" && goAt < 0) goAt = now; if (md == "APPROACH" && appAt < 0) appAt = now; if (md == "WORK" && workAt < 0) workAt = now;
                    if (md == "RTB" && rtbAt < 0 && repaired) rtbAt = now;
                    if (md == "IDLE" && repaired && idleAfterAt < 0 && rtbAt >= 0) { idleAfterAt = now; stopAt = t + (long)(6 * SimSettings.TicksPerSecond); }
                }
                string status = "";
                foreach (var p in hornetPbs) { string e = p.Runner != null ? p.Runner.EchoText : null; if (e == null) continue; int i = e.LastIndexOf("Repair :", StringComparison.Ordinal); if (i >= 0) { int j = e.IndexOf('\n', i); status = e.Substring(i, (j < 0 ? e.Length : j) - i).Trim(); } }
                if (status != "" && status != lastStatus) { sb.Append(string.Format(Inv, "  t={0,6:F1}s patient: {1}\n", now, status)); lastStatus = status; }
                if (ackAt < 0 && status.StartsWith("Repair : medic working", StringComparison.Ordinal)) ackAt = now;
                if (status.IndexOf("gave up", StringComparison.Ordinal) >= 0 || status.IndexOf("medic lost", StringComparison.Ordinal) >= 0 || status.IndexOf("did not answer", StringComparison.Ordinal) >= 0) gaveUp = true;
                if (status.IndexOf("repaired by medic", StringComparison.Ordinal) >= 0 && doneSeen < 0) doneSeen = now;
                if (medPb.Runner.Dead || staff.Runner.Dead) { dead = true; deathWhy = (medPb.Runner.Dead ? "Medic " + medPb.Runner.DeathReason : "Staff " + staff.Runner.DeathReason); break; }
                // geometry
                MatrixD tm = tipBlock.WorldMatrix; Vector3D tip = tm.Translation + tm.Forward * 1.25;
                double tipD = 1e9; foreach (var bi in hurt) { double d = (tip - bi.WorldCenter).Length(); if (d < tipD) tipD = d; }
                double centres = (mBody.WorldBoundCenter - pBody.WorldBoundCenter).Length(); if (centres < minCentres) minCentres = centres;
                double rel = (mBody.LinearVelocity - pBody.LinearVelocity).Length();
                bool wOn = false; foreach (var wb in welders) if (((SimShipWelder)wb.Api).Enabled) wOn = true;
                if (!repaired)
                {
                    if (tipD < minTip) minTip = tipD;
                    if (tipD < 3 && rel > maxRelClose) maxRelClose = rel;
                    if (tipD < 1.5 && rel > maxRelTip) maxRelTip = rel;
                    if (tipD < 1.5) { holdRun += 0.1; holdTotal += 0.1; if (holdRun > holdMax) holdMax = holdRun; if (wOn && md == "WORK") weldOn = true; }
                    else holdRun = 0;
                    if (holdRun >= weldS)
                    {
                        repaired = true; repairedAt = now; foreach (var bi in hurt) bi.Hp = bi.MaxHp;
                        foreach (var pj in projectors) pj.Remaining = 0;   // the sim's projector only counts down from its own welder logic; in the game nothing is missing after the weld
                        sb.Append(string.Format(Inv, "  t={0,6:F1}s HARNESS: tip held {1:F1} s within 1.5 m, damaged blocks restored (the sim welds nothing)\n", now, holdRun));
                    }
                }
                if (tw != null) tw.WriteLine(now.ToString("F1", Inv) + "," + md + "," + centres.ToString("F1", Inv) + "," + tipD.ToString("F2", Inv) + "," + rel.ToString("F2", Inv) + "," + mBody.LinearVelocity.Length().ToString("F2", Inv) + "," + (wOn ? 1 : 0) + "," + status);
                if (t % 300 == 0 && t > 0) sb.Append(string.Format(Inv, "  t={0,6:F1}s medic {1,-8} centres {2,6:F1} m tip {3,6:F1} m rel {4,5:F2} m/s welder {5}\n", now, lastMode, centres, tipD, rel, wOn ? "on" : "off"));
                if (stopAt >= 0 && t >= stopAt) break;
            }
            Console.Write(sb.ToString());
            if (tw != null) tw.Dispose();
            if (Has(args, "--echo")) Console.WriteLine("--- Medic echo ---\n" + medPb.Runner.EchoText + "\n--- Hornet staff echo ---\n" + staff.Runner.EchoText);
            var wa = WriterAudit.Of(w);
            double homeD = (mBody.WorldBoundCenter - home).Length();
            var fail = new List<string>();
            Func<string, bool, string, string> st = (name, ok, info) => { Console.WriteLine("  " + (ok ? "ok  " : "MISS") + " " + name + (info != "" ? ": " + info : "")); if (!ok) fail.Add(name); return name; };
            Console.WriteLine("RESULT survival-test medic seed " + seed + ":");
            st("script alive", !dead, deathWhy);
            st("ACK received (patient: medic working)", ackAt >= 0, ackAt >= 0 ? ackAt.ToString("F1", Inv) + " s" : gaveUp ? "patient gave up / medic refused (STOCK LOW? see --echo)" : "never");
            st("medic GO", goAt >= 0 || gap <= 75, goAt >= 0 ? goAt.ToString("F1", Inv) + " s" : gap <= 75 ? "skipped by design (starts within 60 m + radius)" : "never");
            st("medic APPROACH", appAt >= 0, appAt >= 0 ? appAt.ToString("F1", Inv) + " s" : "never");
            st("medic WORK", workAt >= 0, workAt >= 0 ? workAt.ToString("F1", Inv) + " s" : "never");
            st("tip held < 1.5 m (longest > " + holdMin.ToString("F0", Inv) + " s)", holdMax > holdMin, "longest " + holdMax.ToString("F1", Inv) + " s, total " + holdTotal.ToString("F1", Inv) + " s, closest tip " + (minTip < 1e8 ? minTip.ToString("F2", Inv) : "n/a") + " m");
            st("welders on while holding", weldOn, "");
            st("relative speed with tip within 1.5 m <= 0.6 m/s", maxRelTip <= 0.6, "max " + maxRelTip.ToString("F2", Inv) + " m/s (within 3 m of a block: " + maxRelClose.ToString("F2", Inv) + " m/s; the design's 0.5 m/s is measured from the medic centre)");
            st("no collision / collision damage", sink.Collisions == 0 && sink.DamageEvents == 0, sink.Collisions + " collisions, " + sink.DamageEvents + " damage events, " + sink.Dmg.ToString("F0", Inv) + " hp" + (sink.FirstAt >= 0 ? ", first " + sink.FirstAt.ToString("F1", Inv) + " s " + sink.First : "") + "; closest centres " + minCentres.ToString("F1", Inv) + " m");
            st("writer audit 0", wa.Gyro == 0 && wa.Thrust == 0, "gyro " + wa.Gyro + " thrust " + wa.Thrust);
            st("repaired (played by the harness)", repaired, repaired ? repairedAt.ToString("F1", Inv) + " s" : "hold never reached --weld-s");
            st("medic RTB after the patient's done bit", rtbAt >= 0, rtbAt >= 0 ? rtbAt.ToString("F1", Inv) + " s" : "never");
            st("medic IDLE again near home", idleAfterAt >= 0 && homeD < 25, (idleAfterAt >= 0 ? idleAfterAt.ToString("F1", Inv) + " s" : "never") + ", " + homeD.ToString("F1", Inv) + " m from home");
            Console.WriteLine(fail.Count == 0 ? "SURVIVAL MEDIC PASS" : "SURVIVAL MEDIC FAIL (" + string.Join("; ", fail) + ")");
            return fail.Count == 0 ? 0 : 1;
        }

        static string StockNote(List<Body> medic, bool skip)
        {   // the sim spawns cargo empty (ApiTuning.CargoFillFraction): top the [Stock] containers up to 60 % so the Medic does not refuse with STOCK LOW
            double cur = 0, max = 0; int n = 0;
            foreach (var b in medic) foreach (var bi in b.Blocks)
            {
                if (bi.Api == null || bi.Src.TypeId.IndexOf("CargoContainer", StringComparison.Ordinal) < 0) continue;
                var inv = bi.Api.GetInventory(0) as SimInventory; if (inv == null) continue;
                if (!skip) inv.CurrentVolumeM3 = Math.Max(inv.CurrentVolumeM3, inv.MaxVolumeM3 * 0.6);
                n++; cur += inv.CurrentVolumeM3; max += inv.MaxVolumeM3;
            }
            return n + " [Stock] containers, fill " + (max > 0 ? (cur / max * 100).ToString("F0", Inv) : "?") + " %" + (skip ? " (as spawned)" : " (topped up by the harness)");
        }
    }
}
