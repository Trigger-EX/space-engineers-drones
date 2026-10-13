// M5 acceptance tests (docs/design/control_arch.md section 7 "M5 Cradle bays"), on the REAL scripts: CradleBayAI.cs (bay PB), StaffAI.cs, ShrikeAI.cs (Helm) and the REAL
// HornetDroneAI.cs in the clamped drones. Blueprints (python3 -I tools/build_all.py --only "Cradle Pod Test" "Cradle Test" "Shrike Wing Test" Warden Hornet --no-zip):
//   "Cradle Pod Test"  the 2-slot Cradle Pod with two Hornets      "Cradle Test"  Corsair Wing Lite (Helm ShrikeAI, Staff StaffAI, Bay CradleBayAI, two Hornets)
// Run: SimHost.exe selftest m5   (also part of `selftest all`; skips when a blueprint is missing). The 10-seed acceptance run is `bash sim/dock_launch_test.sh --cradle`.
//   1 launch   'release all' to the bay PB, 3 seeds on each geometry: no FUSED, every drone separates within 3 s of its release, merge-offs 2 s apart, burn along the lane axis, no drone block in an occupied host cell
//   2 C10      the Helm's and the Staff's block lists hold no clamped drone's block (name prefix [SH]); the control without the prefix does adopt them; the lists do not change when the drones leave
//   3 pilot    a scripted pilot sits in the seat and runs 'Run Staff "release 1"': drone 1 leaves within 3 s, drone 2 stays, no override fight, the Staff saw the ack and the bay table
//   4 link     the Staff on ANOTHER ship (ConnectedConstructs cannot reach the pod's construct): the release is retried over AntennaRelay and acked; with no pod at all the Staff reports 'NO BAY ANSWERED' after 6 tries
//   4b pods    two pods (First Bay 1 and 3): 'release all' from the Staff becomes one 'release a-b <delay>' per pod, the second pod 20 s later
//   5 gates    a spinning host, a lane aimed into the wall, a half-charged drone wait (and say why), 'force' skips them; a drone bridged to the host by an armour block is reported FUSED (bay 1) while bay 2 releases; 'recall' cancels the queued drone
//   6 doors    Warden (large grid, 8 hangar doors, docked pod = another body): 'doors open|close', 'release all' opens the doors first, the pod's bays answer, the doors close 10 s after
// EVIDENCE: the sim's merge/split, IGC (delivery next tick, antenna reach, CurrentConstruct = same body), physics; nothing measured in the game. Script state is read by reflection.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using SeSim.Api;
using SeSim.Blueprint;
using SeSim.Core;

namespace SeSim.Run
{
    public static class SelfTestM5
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static int Run(string simDir, string repo)
        {
            _fail = 0; _pass = 0;
            Console.WriteLine("SelfTestM5 (cradle bays: CradleBayAI + Staff release/recall/doors, name-prefix rule, IGC fallback)");
            string bpd = Path.Combine(Path.Combine(repo, "out"), "Blueprints");
            foreach (string m in new[] { "Cradle Pod Test", "Cradle Test", "Shrike Wing Test", "Hornet" })
                if (!File.Exists(Path.Combine(bpd, m, "bp.sbc")))
                { SelfTestSkips.Note("M5 (build: python3 -I tools/build_all.py --only \"Cradle Pod Test\" \"Cradle Test\" \"Shrike Wing Test\" Warden Hornet --no-zip)"); Console.WriteLine("  skipped: blueprint " + m + " missing"); return 0; }
            TestLaunch(simDir, repo);
            TestPrefix(simDir, repo);
            TestPilot(simDir, repo);
            TestLink(simDir, repo);
            TestTwoPods(simDir, repo);
            TestGates(simDir, repo);
            if (File.Exists(Path.Combine(bpd, "Warden", "bp.sbc"))) TestDoors(simDir, repo); else { SelfTestSkips.Note("M5 doors (build Warden)"); Console.WriteLine("  skipped: doors (blueprint Warden missing)"); }
            Console.WriteLine(_fail == 0 ? "SelfTestM5 passed (" + _pass + " checks)" : "SelfTestM5 FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        static void Steps(CradleRig rig, double seconds) { long n = (long)Math.Round(seconds * SimSettings.TicksPerSecond); for (long i = 0; i < n; i++) rig.Step(); }
        static void Cmd(CradleRig rig, SimProgrammableBlock pb, string a) { rig.Host.RunArgument(pb, a, UpdateType.Terminal); }
        static T Fld<T>(SimProgrammableBlock pb, string n) { return CradleRig.Fld<T>(pb.Runner.Program, n); }
        static string Join(List<string> l) { return string.Join(" ", l.ToArray()); }

        // ------------------------------------------------------------------------------------------------------------------ 1 launch
        static void TestLaunch(string simDir, string repo)
        {
            Console.WriteLine("-- 1 launch: 'release all' to the bay PB at 3 s; both cradle geometries, 3 seeds each --");
            foreach (string model in new[] { "Cradle Pod Test", "Cradle Test" })
            {
                int ok = 0; var firsts = new List<double>(); string last = "";
                for (int seed = 1; seed <= 3; seed++)
                {
                    var rig = CradleRig.Build(simDir, repo, model, (ulong)seed, null, 0, -1, null);
                    Steps(rig, 0.4); if (rig.Helm != null) Cmd(rig, rig.Helm, "idle");
                    Steps(rig, 2.6); double cmdAt = rig.W.Time; Cmd(rig, rig.Bay, "release all");
                    bool fault = false; for (int i = 0; i < 11 * 60; i++) { rig.Step(); if (i % 6 == 0) { foreach (var tr in rig.Tracks) tr.Mode = CradleRig.ModeOf(tr.Pb); if (Join(rig.BayStates()).Contains("FAULT")) fault = true; } }
                    string detail, result; var f = DockLaunchCradle.Evaluate(rig, cmdAt, "launch", 3, fault, out detail, out result);
                    if (f.Count == 0) { ok++; firsts.Add(rig.Tracks[0].SplitAt - cmdAt); } else { Info(model + " seed " + seed + ": " + string.Join("; ", f.ToArray())); Info(result); }
                    last = result;
                }
                Check(ok == 3, model + ": 3/3 seeds: no FUSED, split within 3 s, 2 s waves, burn on the lane axis, no host-cell overlap (first split " + (firsts.Count > 0 ? firsts[0].ToString("F2", Inv) : "-") + " s)");
                Info(last);
            }
        }

        // ------------------------------------------------------------------------------------------------------------------ 2 C10
        static readonly string[] HelmLists = { "_gyros", "_thr", "_guns", "_cams", "_turrets", "_ctcs", "_bats", "_seats", "_dm", "_ai" };
        static readonly string[] StaffLists = { "_gyros", "_thr", "_guns", "_ctl", "_turrets", "_ctcs", "_ai" };
        static int Count(SimProgrammableBlock pb, string[] names, bool drones)
        {
            int n = 0;
            foreach (string nm in names)
            {
                var l = Fld<IList>(pb, nm);
                foreach (object o in l) { var b = o as SimCubeBlock; if (b != null && (b.Inst.ConstructId > 0) == drones) n++; }
            }
            return n;
        }

        static void TestPrefix(string simDir, string repo)
        {
            Console.WriteLine("-- 2 C10: name-prefix rule ([SH]) keeps the clamped drones' blocks out of the Helm's and the Staff's lists --");
            int clean = 0, same = 0; int hostBefore = 0, hostAfter = 0;
            for (int seed = 1; seed <= 3; seed++)
            {
                var rig = CradleRig.Build(simDir, repo, "Cradle Test", (ulong)seed, null, 0, -1, null);
                Steps(rig, 0.4); Cmd(rig, rig.Helm, "idle"); Steps(rig, 2.6);
                int dh = Count(rig.Helm, HelmLists, true), ds = Count(rig.Staff, StaffLists, true);
                hostBefore = Count(rig.Helm, HelmLists, false);
                if (dh == 0 && ds == 0 && hostBefore > 20) clean++; else Info("seed " + seed + ": drone blocks in the Helm lists " + dh + ", in the Staff lists " + ds + ", host blocks " + hostBefore);
                Cmd(rig, rig.Bay, "release all"); Steps(rig, 12);
                Cmd(rig, rig.Helm, "reload"); Cmd(rig, rig.Staff, "reload"); Steps(rig, 1);
                hostAfter = Count(rig.Helm, HelmLists, false);
                if (Count(rig.Helm, HelmLists, true) == 0 && Count(rig.Staff, StaffLists, true) == 0 && hostAfter == hostBefore) same++;
                else Info("seed " + seed + ": after the launch: drone blocks " + Count(rig.Helm, HelmLists, true) + " / " + Count(rig.Staff, StaffLists, true) + ", host blocks " + hostBefore + " -> " + hostAfter);
            }
            Check(clean == 3, "3/3 seeds, both clamped: no drone block in any Helm list (" + Join(new List<string>(HelmLists)) + ") or Staff list; " + hostBefore + " host blocks listed");
            Check(same == 3, "3/3 seeds, after both drones left and a reload: still no drone block, host list unchanged (" + hostAfter + ")");
            {   // control: without the prefix the lists DO adopt the drones (that is what the rule is for)
                var rig = CradleRig.Build(simDir, repo, "Cradle Test", 1, null, 0, -1, null);
                Steps(rig, 0.4); Cmd(rig, rig.Helm, "idle");
                rig.Helm.CustomData = Regex.Replace(rig.Helm.CustomData, @"(Block Prefix\s*=).*", "$1"); Cmd(rig, rig.Helm, "reload");
                rig.Staff.CustomData = Regex.Replace(rig.Staff.CustomData, @"(Block Prefix\s*=).*", "$1"); Cmd(rig, rig.Staff, "reload");
                Steps(rig, 2);
                int dh = Count(rig.Helm, HelmLists, true), ds = Count(rig.Staff, StaffLists, true);
                Check(dh > 20 && ds > 10, "control without 'Block Prefix': the Helm adopts " + dh + " and the Staff " + ds + " blocks of the clamped drones (thrusters, gyros, batteries, cameras, AI blocks)");
            }
        }

        // ------------------------------------------------------------------------------------------------------------------ 3 pilot
        static void TestPilot(string simDir, string repo)
        {
            Console.WriteLine("-- 3 pilot seated: scripted 'Run Staff \"release 1\"' (the cockpit toolbar item) at 4 s, pilot in the seat from 1 s to 14 s --");
            int ok = 0; string note = "", last = "";
            for (int seed = 1; seed <= 3; seed++)
            {
                var rig = CradleRig.Build(simDir, repo, "Cradle Test", (ulong)seed, null, 0, -1, null);
                rig.AttachPilot("1:sit;4:run Staff release 1;14:leave");
                Steps(rig, 0.4); Cmd(rig, rig.Helm, "idle");
                Steps(rig, 3.6); double cmdAt = rig.W.Time;
                bool seatedAtSplit = false; double split1 = -1;
                for (int i = 0; i < 9 * 60; i++)
                {
                    rig.Step();
                    if (split1 < 0 && rig.Tracks[0].SplitAt >= 0) { split1 = rig.Tracks[0].SplitAt - cmdAt; seatedAtSplit = rig.Ship.Pilot.Seat != null && rig.Ship.Pilot.Seat.IsUnderControl; }
                }
                var st = rig.BayStates();
                string sn = Fld<string>(rig.Staff, "_rqNote");
                var table = Fld<IDictionary>(rig.Staff, "_bay");
                last = "split " + split1.ToString("F2", Inv) + " s after the command, bays " + Join(st) + ", Staff note '" + sn + "', bay table " + table.Count + " rows, override-fight ticks " + rig.Ship.Pilot.OverrideFightTicks + ", speed " + rig.HostBody.LinearVelocity.Length().ToString("F2", Inv);
                bool good = split1 >= 0 && split1 <= 3 && seatedAtSplit && rig.Tracks[1].SplitAt < 0 && st[1] == "DOCKED" && table.Count == 2 && rig.Ship.Pilot.OverrideFightTicks == 0 && rig.Ship.Pilot.Skipped == 0 && sn.Contains("bays answered") && rig.HostBody.LinearVelocity.Length() < 1;
                if (good) ok++; else note += " seed " + seed + ": " + last + ";";
            }
            Check(ok == 3, "3/3 seeds: drone 1 left within 3 s while the pilot was seated, drone 2 stayed DOCKED, the Staff got the ack and a 2-row bay table, no override fight, the ship held still");
            Info(last); if (ok != 3) Info(note);
        }

        // ------------------------------------------------------------------------------------------------------------------ 4 link
        static void TestLink(string simDir, string repo)
        {
            Console.WriteLine("-- 4 link: the Staff is on ANOTHER ship (ConnectedConstructs cannot reach the pod), 'release 1' --");
            int ok = 0; string last = "";
            for (int seed = 1; seed <= 3; seed++)
            {
                var rig = CradleRig.Build(simDir, repo, "Cradle Pod Test", (ulong)seed, null, 0, -1, null, false, "Shrike Wing Test");
                Steps(rig, 0.4); Cmd(rig, rig.Helm, "idle"); Steps(rig, 2.6); double cmdAt = rig.W.Time; Cmd(rig, rig.Staff, "release 1");
                Steps(rig, 8);
                int tries = Fld<int>(rig.Staff, "_rqN"); string note = Fld<string>(rig.Staff, "_rqNote"); var st = rig.BayStates();
                last = "tries " + tries + ", split " + (rig.Tracks[0].SplitAt >= 0 ? (rig.Tracks[0].SplitAt - cmdAt).ToString("F2", Inv) : "never") + " s, bays " + Join(st) + ", note '" + note + "'";
                if (tries == 2 && rig.Tracks[0].SplitAt >= 0 && rig.Tracks[0].SplitAt - cmdAt < 4 && rig.Tracks[1].SplitAt < 0 && note.Contains("bays answered")) ok++;
            }
            Check(ok == 3, "3/3 seeds: try 1 (ConnectedConstructs) is lost, try 2 (AntennaRelay) reaches the pod, the bay acks, drone 1 leaves within 4 s, drone 2 stays");
            Info(last);
            {   // nobody there: the retries end with a clear message
                var rig = CradleRig.Build(simDir, repo, "Cradle Pod Test", 1, null, 0, -1, null, false, "Shrike Wing Test");
                Steps(rig, 0.4); Cmd(rig, rig.Helm, "idle"); Steps(rig, 1);
                rig.Bay.Enabled = false; Cmd(rig, rig.Staff, "release 1"); Steps(rig, 12);
                string note = Fld<string>(rig.Staff, "_rqNote"); int tries = Fld<int>(rig.Staff, "_rqN");
                Check(note.Contains("NO BAY ANSWERED") && tries == 6 && rig.Tracks[0].SplitAt < 0, "pod PB off: 6 tries, then '" + note + "'");
            }
        }

        // second pod: 'release all' becomes one 'release a-b <delay>' per pod (Pod Gap 20 s)
        static void TestTwoPods(string simDir, string repo)
        {
            Console.WriteLine("-- 4b two pods (First Bay 1 and 3): 'release all' from the Staff is cut per pod, 20 s apart --");
            var rig = CradleRig.Build(simDir, repo, "Cradle Pod Test", 1, null, 0, -1, null, false, "Shrike Wing Test");
            var bp = BlueprintLoader.Get(Path.Combine(Path.Combine(repo, "out"), "Blueprints"), "Cradle Pod Test");
            var second = GridFactory.Spawn(rig.W, bp, 0, 2, new SpawnPlacement { Origin = new Vector3D(0, 0, 45), Forward = Vector3D.Forward, Up = Vector3D.Up }, new Dictionary<long, long>());
            BlockApiFactory.FlushPending(rig.W);
            SimProgrammableBlock bay2 = null; var drones2 = new List<SimProgrammableBlock>();
            foreach (var b in second) foreach (var bi in b.Blocks)
            {
                var pb = bi.Api as SimProgrammableBlock; if (pb == null || pb.Runner == null) continue;
                string n = pb.CustomNameValue ?? "";
                if (n.StartsWith("[Pod]")) { bay2 = pb; pb.CustomData = bi.Src.StoredCustomData.Replace("First Bay=1", "First Bay=3"); rig.Host.Recompile(pb); }
                else if (n.StartsWith("Hornet")) drones2.Add(pb);
            }
            Steps(rig, 0.4); Cmd(rig, rig.Helm, "idle"); Steps(rig, 4); double cmdAt = rig.W.Time;
            var table = Fld<IDictionary>(rig.Staff, "_bay");
            Cmd(rig, rig.Staff, "release all");
            double[] sp = { -1, -1, -1, -1 };
            for (int i = 0; i < 30 * 60; i++)
            {
                rig.Step();
                for (int k = 0; k < 2; k++) if (sp[k] < 0 && rig.Tracks[k].SplitAt >= 0) sp[k] = rig.Tracks[k].SplitAt - cmdAt;
                for (int k = 0; k < 2; k++) if (sp[2 + k] < 0 && drones2[k].Inst.Body != bay2.Inst.Body) sp[2 + k] = rig.W.Time - cmdAt;
            }
            Info("bay table " + table.Count + " rows; splits after the command: pod 1 " + sp[0].ToString("F1", Inv) + " / " + sp[1].ToString("F1", Inv) + " s, pod 2 " + sp[2].ToString("F1", Inv) + " / " + sp[3].ToString("F1", Inv) + " s");
            Check(table.Count == 4 && sp[0] >= 0 && sp[0] < 3 && Math.Abs(sp[1] - sp[0] - 2) < 0.4 && Math.Abs(sp[2] - sp[0] - 20) < 1.0 && Math.Abs(sp[3] - sp[2] - 2) < 0.4,
                  "the Staff learned 4 bays from 2 PBs; pod 1 releases at once (drones 2 s apart), pod 2 starts 20 s later (drones 2 s apart)");
        }

        // ------------------------------------------------------------------------------------------------------------------ 5 gates
        static void TestGates(string simDir, string repo)
        {
            Console.WriteLine("-- 5 gates: spin, lane, charge, force, FUSED, recall --");
            {   // spinning host
                var rig = CradleRig.Build(simDir, repo, "Cradle Test", 1, null, 0.4, -1, null);
                Steps(rig, 3); Cmd(rig, rig.Bay, "release all"); Steps(rig, 4);
                bool waits = rig.Tracks[0].SplitAt < 0 && rig.Tracks[1].SplitAt < 0 && rig.BayMsgs().Contains("ship turns");
                Check(waits, "a host turning faster than Max Turn (0.1 rad/s) is not released from: '" + rig.BayMsgs().Trim() + "'");
                Cmd(rig, rig.Bay, "release all force"); Steps(rig, 1);
                Check(rig.Tracks[0].SplitAt >= 0 || rig.BayStates()[0] == "ARMING", "'release all force' skips the gates (drone 1 " + (rig.Tracks[0].SplitAt >= 0 ? "separated" : "arming") + ")");
            }
            {   // lane into the wall
                var rig = CradleRig.Build(simDir, repo, "Cradle Pod Test", 1, null, 0, -1, "0,0,1");
                Steps(rig, 3); Cmd(rig, rig.Bay, "release all"); Steps(rig, 4);
                Check(rig.Tracks[0].SplitAt < 0 && rig.BayMsgs().Contains("lane blocked at"), "a lane aimed into the pod's own wall is refused: '" + rig.BayMsgs().Trim() + "'");
            }
            {   // charge
                var rig = CradleRig.Build(simDir, repo, "Cradle Pod Test", 1, null, 0, 0.3, null);
                Steps(rig, 3); Cmd(rig, rig.Bay, "release all"); Steps(rig, 3);
                var st = rig.BayStates(); float c0 = BayCharge(rig, 0);
                Check(rig.Tracks[0].SplitAt < 0 && st[0] == "CHARGING", "drones at 30 % charge: bay 1 is " + st[0] + " (" + (int)(c0 * 100) + " %) and nobody left");
                bool recharge = true; foreach (var b in rig.Bodies) foreach (var bi in b.Blocks) if (bi.Battery != null && bi.ConstructId > 0 && bi.Battery.Mode != ChargeMode.Recharge) recharge = false;
                Check(recharge, "the drone batteries are on ChargeMode.Recharge while the bay waits (the sim's power model never charges a battery, so the charge itself cannot be tested; set by hand below)");
                foreach (var b in rig.Bodies) foreach (var bi in b.Blocks) if (bi.Battery != null && bi.ConstructId > 0) bi.Battery.StoredMWh = bi.Battery.MaxStoredMWh * 0.95;
                double cmdAt = rig.W.Time; Steps(rig, 8);
                bool auto = true; foreach (var b in rig.Bodies) foreach (var bi in b.Blocks) if (bi.Battery != null && bi.ConstructId > 0 && bi.Battery.Mode != ChargeMode.Auto) auto = false;
                Check(rig.Tracks[0].SplitAt >= 0 && rig.Tracks[1].SplitAt >= 0 && auto, "charged to 95 % by hand: the queued release goes on by itself (split " + (rig.Tracks[0].SplitAt - cmdAt).ToString("F1", Inv) + " s and " + (rig.Tracks[1].SplitAt - cmdAt).ToString("F1", Inv) + " s later) and the batteries are back on Auto");
            }
            {   // FUSED
                var rig = CradleRig.Build(simDir, repo, "Cradle Pod Test", 1, null, 0, -1, null, true);
                Steps(rig, 3); double cmdAt = rig.W.Time; Cmd(rig, rig.Bay, "release all"); bool fault = false; double faultAt = -1;
                for (int i = 0; i < 9 * 60; i++) { rig.Step(); if (faultAt < 0 && rig.BayStates()[0] == "FAULT") { faultAt = rig.W.Time - cmdAt; fault = true; } }
                Check(fault && rig.BayMsgs().Contains("FUSED") && rig.Tracks[0].SplitAt < 0 && rig.Tracks[1].SplitAt >= 0 && CradleRig.ModeOf(rig.Tracks[0].Pb) == "IDLE",
                      "a drone bridged to the pod: bay 1 FAULT FUSED " + faultAt.ToString("F1", Inv) + " s after the command (merge-off + 3 s), the stuck drone does not burn (mode " + CradleRig.ModeOf(rig.Tracks[0].Pb) + "), bay 2 released");
            }
            {   // recall
                var rig = CradleRig.Build(simDir, repo, "Cradle Pod Test", 1, null, 0, -1, null);
                Steps(rig, 3); Cmd(rig, rig.Bay, "release all"); Steps(rig, 0.9); Cmd(rig, rig.Bay, "recall"); Steps(rig, 8);
                var st = rig.BayStates();
                Check(rig.Tracks[0].SplitAt >= 0 && rig.Tracks[1].SplitAt < 0 && st[1] == "DOCKED" && st[0] == "EMPTY", "'recall' after the first merge-off: drone 2 never leaves (" + st[1] + "), bay 1 is " + st[0] + " with its merge block on again");
            }
        }
        static float BayCharge(CradleRig rig, int i) { var l = Fld<IList>(rig.Bay, "_bays"); object b = l[i]; return (float)b.GetType().GetField("Charge").GetValue(b); }

        // ------------------------------------------------------------------------------------------------------------------ 6 doors
        static void TestDoors(string simDir, string repo)
        {
            Console.WriteLine("-- 6 doors: Warden (belly hangar, 8 doors, docked 4-slot pod = another body), Staff doors / release --");
            var settings = new SimSettings();
            var defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            var w = new World(settings, defs, 1); var host = new ScriptHost(w, simDir); w.Scripts = host; w.Teams.Init(2);
            var bp = BlueprintLoader.Get(Path.Combine(Path.Combine(repo, "out"), "Blueprints"), "Warden");
            var bodies = GridFactory.Spawn(w, bp, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, new Dictionary<long, long>());
            BlockApiFactory.FlushPending(w);
            SimProgrammableBlock staff = null, pod = null; var doors = new List<SimDoor>();
            foreach (var b in bodies) foreach (var bi in b.Blocks)
            {
                var pb = bi.Api as SimProgrammableBlock; if (pb != null && pb.Runner != null) { string n = pb.CustomNameValue ?? ""; if (n.StartsWith("[Staff]")) staff = pb; else if (n.StartsWith("[Pod]")) pod = pb; if (pb.Runner.Program != null && !string.IsNullOrEmpty(bi.Src.StoredCustomData)) { pb.CustomData = bi.Src.StoredCustomData; host.RunArgument(pb, "reload", UpdateType.Terminal); } }
                var d = bi.Api as SimDoor; if (d != null) doors.Add(d);
            }
            if (staff == null || pod == null || doors.Count == 0) { Check(false, "Warden has a [Staff] PB, a [Pod] PB and doors (" + (staff != null) + " " + (pod != null) + " " + doors.Count + ")"); return; }
            Action<double> run = secs => { long n = (long)Math.Round(secs * SimSettings.TicksPerSecond); for (long i = 0; i < n; i++) w.Step(); };
            Func<int> open = () => { int n = 0; foreach (var d in doors) if (d.Status == DoorStatus.Open) n++; return n; };
            run(1);
            Check(Fld<IList>(staff, "_doors").Count == doors.Count, "the Staff found the hangar doors through [Hangar 1] Group (" + Fld<IList>(staff, "_doors").Count + " of " + doors.Count + ")");
            host.RunArgument(staff, "doors open", UpdateType.Terminal); run(0.1);
            bool moving = open() < doors.Count; run(15);
            Check(moving && open() == doors.Count, "'doors open': the doors take time and all " + doors.Count + " end Open");
            host.RunArgument(staff, "doors close", UpdateType.Terminal); run(15);
            Check(open() == 0, "'doors close': all Closed");
            host.RunArgument(staff, "release all", UpdateType.Terminal); run(0.3);
            bool opening = open() < doors.Count && Fld<string>(staff, "_pendRel") == "all";
            double t0 = w.Time; double allOpenAt = -1, ackAt = -1, closeAt = -1;
            for (int i = 0; i < 40 * 60; i++)
            {
                w.Step();
                if (allOpenAt < 0 && open() == doors.Count) allOpenAt = w.Time - t0;
                if (ackAt < 0 && Fld<string>(staff, "_rqNote").Contains("bays answered")) ackAt = w.Time - t0;
                if (allOpenAt >= 0 && closeAt < 0 && open() == 0) closeAt = w.Time - t0;
            }
            Info("doors all open at " + allOpenAt.ToString("F1", Inv) + " s, bays answered at " + ackAt.ToString("F1", Inv) + " s, doors closed at " + closeAt.ToString("F1", Inv) + " s; note '" + Fld<string>(staff, "_rqNote") + "'");
            Check(opening && allOpenAt > 0 && ackAt >= allOpenAt && closeAt > ackAt + 8, "'release all': opens the doors first, sends to the pod after they are open (ack " + ackAt.ToString("F1", Inv) + " s after all open " + allOpenAt.ToString("F1", Inv) + " s), closes them about 10 s later (" + closeAt.ToString("F1", Inv) + " s)");
            Check(Fld<IDictionary>(staff, "_bay").Count == 4, "the Staff's bay table has the pod's 4 bays (all EMPTY: the Warden pod is delivered without drones)");
        }
    }
}
