// M0 self-checks (docs/design/control_arch.md section 6 S14, section 7 "M0 Groundwork"): cockpit as ship controller, pilot input in the physics
// with per-block override priority, the ScriptedPilot, Timer / Event Controller / Button Panel / toolbar groups, the merge fuse pass, per-PB
// commands and CustomData, KillPb, enemy-scripted. Run: SimHost.exe selftest m0   (also part of `selftest all`).
// Ships are built in code (no blueprint files) except the two integration sections at the end, which use the generated test blueprints
// "Hornet Pilot Test" and "Twin PB Test" (python3 -I tools/build_all.py --only "Hornet Pilot Test" "Twin PB Test") and skip when they are missing.
// EVIDENCE: everything here is the sim's model. What the real game does with MoveIndicator / RotationIndicator scale and signs, the Event
// Controller event numbers, the Timer tick granularity and the toolbar Run-with-argument item is NOT tested by this file (control_arch K1-K4).
using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using VRageMath;
using SeSim.Api;
using SeSim.Blueprint;
using SeSim.Core;

namespace SeSim.Run
{
    public static class SelfTestM0
    {
        static int _fail, _pass;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (ok) _pass++; else _fail++; }
        static void Info(string s) { Console.WriteLine("  info " + s); }
        static bool Within(double v, double expect, double rel) { return Math.Abs(v - expect) <= rel * Math.Abs(expect); }

        static string _repo, _simDir;
        static DefDb _defs;

        public static int Run(string simDir, string repo)
        {
            _fail = 0; _pass = 0; _simDir = simDir; _repo = repo;
            Console.WriteLine("SelfTestM0 (cockpit, pilot input, automation blocks, merge fuse, per-PB commands)");
            _defs = DefDb.LoadCached(Path.Combine(repo, "defs_stats.json"));
            TestCockpitApi();
            TestThrustResponse();
            TestRotationResponse();
            TestOverrideWins();
            TestClosedLoopPilot();
            TestTimerToolbar();
            TestEventControllers();
            TestGroupsAndButtons();
            TestMergeFuse();
            TestKillPb();
            TestOptInHash();
            TestIntegration();
            Console.WriteLine(_fail == 0 ? "SelfTestM0 passed (" + _pass + " checks)" : "SelfTestM0 FAILED: " + _fail + " of " + (_pass + _fail));
            return _fail;
        }

        // ------------------------------------------------------------------------------------------------------------------ builders
        static int _ids;
        static BlockData Bd(string type, string sub, int x, int y, int z, string fwd = null, string up = null, string name = null)
        {
            var b = new BlockData { XsiType = "MyObjectBuilder_" + type, TypeId = type, Subtype = sub, Min = new Vector3I(x, y, z), EntityId = 5000 + (++_ids) };
            if (fwd != null || up != null) b.Orientation = new MyBlockOrientation(OrientMath.ParseDir(fwd ?? "Forward"), OrientMath.ParseDir(up ?? "Up"));
            if (name != null) b.CustomName = name;
            return b;
        }
        static BlockData Thrust(int x, int y, int z, string fwd, string up = "Up") { return Bd("Thrust", "SmallBlockSmallThrust", x, y, z, fwd, up); }
        static BlockData Gyro(int x, int y, int z) { return Bd("Gyro", "SmallBlockGyro", x, y, z); }
        static BlockData Bat(int x, int y, int z, double stored = 0.05)
        {
            var b = Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", x, y, z);
            b.Props["CurrentStoredPower"] = stored.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return b;
        }
        static BlockData Armor(int x, int y, int z) { var b = Bd("CubeBlock", "SmallBlockArmorBlock", x, y, z); b.EntityId = 0; return b; }

        /// <summary>A test ship: SmallBlockCockpit (3x3x3 at the origin), 2 forward, 1 backward, 1 each right / left / up / down thruster, 2 gyros, 2 batteries.
        /// Returns the blocks; block order is fixed (indices used by the tests).</summary>
        static List<BlockData> ShipBlocks()
        {
            var l = new List<BlockData>
            {
                Bd("Cockpit", "SmallBlockCockpit", 0, 0, 0, null, null, "Seat"),
                Thrust(0, 0, 3, "Backward"), Thrust(2, 2, 3, "Backward"),          // push forward (-Z)
                Thrust(0, 0, -2, "Forward"),                                         // push backward
                Thrust(-2, 1, 1, "Left"), Thrust(3, 1, 1, "Right"),                  // push right (+X) / left
                Thrust(1, -2, 1, "Down", "Forward"), Thrust(1, 3, 1, "Up", "Backward"),   // push up / down
                Gyro(5, 0, 0), Gyro(5, 1, 0), Bat(6, 0, 0), Bat(6, 1, 0),
            };
            return l;
        }

        static BlueprintData Bp(params List<BlockData>[] grids)
        {
            var bp = new BlueprintData { Name = "m0test" };
            int n = 0;
            foreach (var blocks in grids)
            {
                var g = new GridData { DisplayName = "g" + n, Large = false, EntityId = 100 + n, DampenersEnabled = false };
                foreach (var b in blocks) g.Blocks.Add(b);
                bp.Grids.Add(g); n++;
            }
            return bp;
        }

        static World NewWorld(ulong seed, bool power = false, Action<SimSettings> cfg = null)
        {
            var s = new SimSettings();
            s.PowerEnabled = power;
            if (cfg != null) cfg(s);
            var w = new World(s, _defs, seed);
            w.Teams.Init(3);
            w.Scripts = new ScriptHost(w, _simDir);
            return w;
        }

        static Vector3D RandomUnit(DetRng r)
        {
            for (;;) { var v = new Vector3D(r.NextGaussian(), r.NextGaussian(), r.NextGaussian()); double l = v.Length(); if (l > 1e-3) return v / l; }
        }

        static SpawnPlacement RandomPose(DetRng r, Vector3D pos)
        {
            Vector3D f = RandomUnit(r), u = RandomUnit(r);
            u = u - f * Vector3D.Dot(u, f); u.Normalize();
            return new SpawnPlacement { Origin = pos, Forward = f, Up = u };
        }

        static ShipInstance Ship(World w, List<Body> bodies, int team, int index, string pilot)
        {
            var sh = new ShipInstance { Index = index, Spec = new ShipSpec { Model = "t", Team = team }, Team = team, Main = bodies[0] };
            sh.Bodies.AddRange(bodies);
            if (pilot != null) { sh.Pilot = new ScriptedPilot(sh, w.Scripts, pilot); w.Brains.Add(sh.Pilot); }
            return sh;
        }

        static void Steps(World w, double seconds) { long n = (long)Math.Round(seconds * SimSettings.TicksPerSecond); for (long i = 0; i < n; i++) w.Step(); }

        static SimCockpit Seat(Body b) { foreach (var k in b.Blocks) if (k.Api is SimCockpit) return (SimCockpit)k.Api; return null; }

        /// <summary>Sum of MaxForce of the working thrusters pushing the ship along grid direction (index 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z).</summary>
        static double Force(Body b, int dirIndex) { double f = 0; foreach (var t in b.Thrusters) if (t.DirIndex == dirIndex && t.Working) f += t.MaxForce; return f; }

        // ------------------------------------------------------------------------------------------------------------------ tests
        static void TestCockpitApi()
        {
            Console.WriteLine("-- cockpit as ship controller --");
            var w = NewWorld(1);
            var bodies = GridFactory.Spawn(w, Bp(ShipBlocks()), 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null);
            var b = bodies[0];
            var seat = Seat(b);
            Check(seat != null, "Cockpit block -> SimCockpit");
            var asCtl = (IMyShipController)seat; var asCock = (IMyCockpit)seat;
            Check(!asCtl.IsUnderControl && asCtl.CanControlShip && asCtl.ControlThrusters, "empty seat: not under control, CanControlShip, ControlThrusters");
            Check(asCtl.MoveIndicator == Vector3.Zero && asCtl.RotationIndicator == Vector2.Zero && asCtl.RollIndicator == 0f, "no pilot: indicators are zero");
            Check(asCock.SurfaceCount == 4 && asCock.GetSurface(3) != null && asCock.GetSurface(4) == null, "SmallBlockCockpit has 4 text surfaces (default table)");
            seat.SetUnderControl(true);
            seat.Input.Move = new Vector3D(0, 0, -1); seat.Input.Yaw = 0.25; seat.Input.Roll = -0.5; seat.Input.Pitch = 0.1;
            Check(asCtl.IsUnderControl && asCtl.MoveIndicator == new Vector3(0, 0, -1) && Math.Abs(asCtl.RotationIndicator.Y - 0.25f) < 1e-6 && Math.Abs(asCtl.RotationIndicator.X - 0.1f) < 1e-6 && asCtl.RollIndicator == -0.5f, "seated: indicators follow the pilot input");
            Check(ReferenceEquals(b.Pilot, seat.Input), "the Body carries the pilot input while somebody sits");
            seat.SetUnderControl(false);
            Check(!asCtl.IsUnderControl && b.Pilot == null && asCtl.MoveIndicator == Vector3.Zero, "leaving clears input and the Body link");
            // GetBlocksOfType<IMyShipController> sees it (a cockpit used to be a generic functional block)
            var rcBp = new List<BlockData> { Bd("RemoteControl", "SmallBlockRemoteControl", 0, 0, 0), Bd("Cockpit", "PassengerSeatSmall", 3, 0, 0), Bd("MyProgrammableBlock", "SmallProgrammableBlock", 0, 2, 0) };
            var w2 = NewWorld(2);
            var b2 = GridFactory.Spawn(w2, Bp(rcBp), 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null)[0];
            var pb = (SimProgrammableBlock)null; foreach (var k in b2.Blocks) if (k.Api is SimProgrammableBlock) pb = (SimProgrammableBlock)k.Api;
            var gts = new SimGridTerminalSystem(pb);
            var ctl = new List<IMyShipController>(); gts.GetBlocksOfType(ctl);
            Check(ctl.Count == 2, "GetBlocksOfType<IMyShipController> lists the remote control and the cockpit (" + ctl.Count + ")");
            var rc = (IMyRemoteControl)null; foreach (var c in ctl) if (c is IMyRemoteControl) rc = (IMyRemoteControl)c;
            Check(rc != null && !rc.IsUnderControl, "a Remote Control nobody drives is not under control");
            IMyCockpit pass = null; foreach (var c in ctl) if (c is IMyCockpit) pass = (IMyCockpit)c;
            Check(pass != null && !pass.CanControlShip, "a passenger seat cannot control the ship (CanControlShip false)");
        }

        // a ship flown by the scripted pilot for 'seconds'; returns the body
        static Body Fly(ulong seed, string pilot, double seconds, bool damp, Action<Body, SimCockpit> before = null, bool power = false, Action<SimSettings> cfg = null)
        {
            var w = NewWorld(seed, power, cfg);
            var rng = new DetRng(DetRng.Mix(seed, 77));
            var bodies = GridFactory.Spawn(w, Bp(ShipBlocks()), 0, 0, RandomPose(rng, Vector3D.Zero), null);
            var b = bodies[0]; b.DampenersOn = damp;
            if (before != null) before(b, Seat(b));
            Ship(w, bodies, 0, 0, pilot);
            Steps(w, seconds);
            return b;
        }

        static void TestThrustResponse()
        {
            Console.WriteLine("-- thrust response of the pilot input (3 seeds, ship in a random orientation, no script) --");
            var axes = new[] { new { n = "forward", move = "0 0 -1", dir = 5, local = new Vector3D(0, 0, -1) }, new { n = "backward", move = "0 0 1", dir = 4, local = new Vector3D(0, 0, 1) },
                               new { n = "right", move = "1 0 0", dir = 0, local = new Vector3D(1, 0, 0) }, new { n = "up", move = "0 1 0", dir = 2, local = new Vector3D(0, 1, 0) } };
            foreach (var ax in axes)
                for (ulong seed = 1; seed <= 3; seed++)
                {
                    Body probe = null;
                    var b = Fly(seed, "0:sit;0:move " + ax.move, 10, false, (bb, s) => probe = bb);
                    double m = b.Mass, f = Force(b, ax.dir);
                    double expect = f / m * 10;                                   // v = F/m t (mass is constant: no ammo, no battery use)
                    Vector3D dirWorld = b.DirToWorld(ax.local);
                    double along = Vector3D.Dot(b.LinearVelocity, dirWorld);
                    double side = (b.LinearVelocity - dirWorld * along).Length();
                    Check(Within(along, expect, 0.05) && side < 0.02 * expect, ax.n + " seed " + seed + ": v " + along.ToString("F2") + " m/s vs analytic F/m*t = " + expect.ToString("F2") + " (side " + side.ToString("F3") + ")");
                }
            // half stick = half thrust; the other axes are dampened while one axis is driven
            {
                var b = Fly(5, "0:sit;0:move 0 0 -0.5", 10, false);
                double expect = Force(b, 5) / b.Mass * 10 * 0.5;
                Check(Within(Vector3D.Dot(b.LinearVelocity, b.DirToWorld(new Vector3D(0, 0, -1))), expect, 0.05), "half stick gives half the thrust");
            }
            {
                // dampeners on, drifting sideways at 5 m/s: forward input does not brake forward, the sideways drift is cancelled
                Body b = null;
                b = Fly(6, "0:sit;0:move 0 0 -1", 6, true, (bb, s) => { bb.LinearVelocity = bb.DirToWorld(new Vector3D(1, 0, 0)) * 5; });
                Vector3D lv = Vector3D.TransformNormal(b.LinearVelocity, b.InvWorldRot);
                double expectF = Force(b, 5) / b.Mass * 6;
                Check(Within(-lv.Z, expectF, 0.06) && Math.Abs(lv.X) < 0.3, "dampeners on: forward driven (" + (-lv.Z).ToString("F1") + " vs " + expectF.ToString("F1") + " m/s), sideways drift cancelled (" + lv.X.ToString("F2") + " m/s)");
            }
            {
                // ControlThrusters=false: the seat does not drive the thrusters
                var b = Fly(7, "0:sit;0:move 0 0 -1", 3, false, (bb, s) => s.ControlThrusters = false);
                Check(b.LinearVelocity.Length() < 1e-6, "ControlThrusters=false: pilot move input does nothing");
            }
        }

        static void TestRotationResponse()
        {
            Console.WriteLine("-- rotation response of the pilot input --");
            double gain = new SimSettings().PilotRateGain;
            var cases = new[] { new { n = "yaw +1 (turn right)", s = "rot 0 1", axis = new Vector3D(0, -1, 0) }, new { n = "pitch +1 (nose down)", s = "rot 1 0", axis = new Vector3D(-1, 0, 0) },
                                new { n = "roll +1 (roll right)", s = "roll 1", axis = new Vector3D(0, 0, -1) } };
            foreach (var c in cases)
                for (ulong seed = 1; seed <= 3; seed++)
                {
                    var b = Fly(seed, "0:sit;0:" + c.s, 10, false);
                    Vector3D wl = Vector3D.TransformNormal(b.AngularVelocity, b.InvWorldRot);
                    double along = Vector3D.Dot(wl, c.axis), off = (wl - c.axis * along).Length();
                    Check(Within(along, gain, 0.05) && off < 0.03 * gain, c.n + " seed " + seed + ": rate " + along.ToString("F3") + " rad/s vs analytic " + gain.ToString("F3") + " (off-axis " + off.ToString("F3") + ")");
                }
            // the response is torque limited at first: the ramp follows omega = alpha t until the target rate
            {
                var b = Fly(9, "0:sit;0:rot 0 1", 0.05, false);
                double torque = 0; foreach (var g in b.Gyros) torque += g.MaxForce;
                Vector3D I = new Vector3D(b.InertiaLocal.M11, b.InertiaLocal.M22, b.InertiaLocal.M33);
                double alpha = torque / I.Y;
                double w50 = -Vector3D.TransformNormal(b.AngularVelocity, b.InvWorldRot).Y;
                Info("yaw after 50 ms: " + w50.ToString("F3") + " rad/s (torque-limited alpha " + alpha.ToString("F1") + " rad/s2 would give " + Math.Min(gain, alpha * 0.05).ToString("F3") + ")");
                Check(w50 > 0.25 * gain && w50 <= gain * 1.01, "the rate target is reached in a fraction of a second, not instantly");
            }
            // idle: letting go of the stick leaves the normal gyro slowdown in charge
            {
                var b = Fly(11, "0:sit;0:rot 0 1;5:rot 0 0", 12, false);
                Check(b.AngularVelocity.Length() < 0.05, "stick released: the ship stops turning (" + b.AngularVelocity.Length().ToString("F3") + " rad/s)");
            }
        }

        static void TestOverrideWins()
        {
            Console.WriteLine("-- per-block override priority (the pilot yields block by block) --");
            double gain = new SimSettings().PilotRateGain;
            // 1. gyro A overridden at 0 rad/s, gyro B free: the pilot's yaw only gets B's share of the authority
            {
                var b = Fly(1, "0:sit;0:rot 0 1", 12, false, (bb, s) =>
                {
                    var ga = (SimGyro)bb.Gyros[0].Block.Api; ga.GyroOverride = true; ga.Yaw = 0; ga.Pitch = 0; ga.Roll = 0;
                });
                double w = -Vector3D.TransformNormal(b.AngularVelocity, b.InvWorldRot).Y;
                var ga2 = (SimGyro)b.Gyros[0].Block.Api;
                Check(Within(w, gain / 2, 0.06), "one of two gyros overridden: the pilot's yaw settles at the capacity-weighted mean " + w.ToString("F3") + " rad/s (expect " + (gain / 2).ToString("F3") + ")");
                Check(ga2.GyroOverride && ga2.Yaw == 0, "the overridden gyro keeps its own command (the pilot never writes it)");
            }
            // 2. both gyros overridden: the pilot has no say at all
            {
                var b = Fly(2, "0:sit;0:rot 0 1;0:roll 1", 8, false, (bb, s) => { foreach (var g in bb.Gyros) { var a = (SimGyro)g.Block.Api; a.GyroOverride = true; a.Yaw = a.Pitch = a.Roll = 0; } });
                Check(b.AngularVelocity.Length() < 0.02, "all gyros overridden: pilot rotation input is ignored (" + b.AngularVelocity.Length().ToString("F4") + " rad/s)");
            }
            // 3. one forward thruster overridden at 50 %, the other free: pilot forward gives 0.5 + 1.0 thrusters
            {
                Body bb0 = null;
                var b = Fly(3, "0:sit;0:move 0 0 -1", 10, false, (bb, s) =>
                {
                    bb0 = bb;
                    ThrusterUnit f1 = null; foreach (var t in bb.Thrusters) if (t.DirIndex == 5) { f1 = t; break; }
                    ((SimThrust)f1.Block.Api).ThrustOverridePercentage = 0.5f;
                });
                double perThruster = 0; foreach (var t in b.Thrusters) if (t.DirIndex == 5) { perThruster = t.MaxForce; break; }
                double expect = (0.5 + 1.0) * perThruster / b.Mass * 10;
                double along = Vector3D.Dot(b.LinearVelocity, b.DirToWorld(new Vector3D(0, 0, -1)));
                Check(Within(along, expect, 0.05), "one forward thruster overridden at 50 %: pilot full forward gives 1.5 thrusters (" + along.ToString("F2") + " vs " + expect.ToString("F2") + " m/s)");
            }
            // 4. both forward thrusters overridden at 25 %: the pilot cannot add anything; the override also freezes dampening on that axis
            {
                var b = Fly(4, "0:sit;0:move 0 0 -1", 10, true, (bb, s) =>
                {
                    foreach (var t in bb.Thrusters) if (t.DirIndex == 5) ((SimThrust)t.Block.Api).ThrustOverridePercentage = 0.25f;
                });
                double perThruster = 0; int n = 0; foreach (var t in b.Thrusters) if (t.DirIndex == 5) { perThruster = t.MaxForce; n++; }
                double expect = 0.25 * n * perThruster / b.Mass * 10;
                double along = Vector3D.Dot(b.LinearVelocity, b.DirToWorld(new Vector3D(0, 0, -1)));
                Check(Within(along, expect, 0.06), "all forward thrusters overridden at 25 %: v = " + along.ToString("F2") + " vs " + expect.ToString("F2") + " m/s (the override wins, the pilot adds nothing)");
            }
            // 5. no pilot, no change: a ship without a seated pilot behaves exactly as before (override only)
            {
                var b = Fly(5, null, 5, false, (bb, s) => { foreach (var t in bb.Thrusters) if (t.DirIndex == 5) ((SimThrust)t.Block.Api).ThrustOverridePercentage = 1f; });
                double expect = Force(b, 5) / b.Mass * 5;
                double along = Vector3D.Dot(b.LinearVelocity, b.DirToWorld(new Vector3D(0, 0, -1)));
                Check(Within(along, expect, 0.02), "no pilot: thruster overrides work as before (" + along.ToString("F2") + " vs " + expect.ToString("F2") + " m/s)");
            }
        }

        static void TestClosedLoopPilot()
        {
            Console.WriteLine("-- ScriptedPilot closed loops --");
            {   // fly-to: arrive at a world point and stop
                var w = NewWorld(21);
                var rng = new DetRng(21);
                var bodies = GridFactory.Spawn(w, Bp(ShipBlocks()), 0, 0, RandomPose(rng, Vector3D.Zero), null);
                bodies[0].DampenersOn = true;
                Ship(w, bodies, 0, 0, "0:sit;0:fly-to 150 40 -60 20");
                Steps(w, 30);
                double d = (bodies[0].Position - new Vector3D(150, 40, -60)).Length();
                Check(d < 12 && bodies[0].LinearVelocity.Length() < 5, "fly-to 150 40 -60 reached in 30 s: " + d.ToString("F1") + " m off, speed " + bodies[0].LinearVelocity.Length().ToString("F1") + " m/s");
            }
            {   // face-target: the cockpit's forward points at the enemy
                var w = NewWorld(22);
                var rng = new DetRng(22);
                var bodies = GridFactory.Spawn(w, Bp(ShipBlocks()), 0, 0, RandomPose(rng, Vector3D.Zero), null);
                var enemy = GridFactory.Spawn(w, Bp(new List<BlockData> { Armor(0, 0, 0), Armor(1, 0, 0), Armor(2, 0, 0) }), 1, 1, RandomPose(rng, new Vector3D(300, 100, -500)), null);
                bodies[0].DampenersOn = true;
                Ship(w, bodies, 0, 0, "0:sit;0:face-target");
                Ship(w, enemy, 1, 1, null);
                Steps(w, 20);
                Vector3D fwd = bodies[0].DirToWorld(Vector3D.Forward);
                Vector3D to = enemy[0].WorldBoundCenter - bodies[0].Position; to.Normalize();
                double ang = Math.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(fwd, to)))) * 180 / Math.PI;
                Check(ang < 2.0, "face-target: the nose is " + ang.ToString("F2") + " deg off the enemy after 20 s (rotation indicators only)");
            }
        }

        // ----------------------------------------------------------------------------------------------------------- automation blocks
        static BlockData WithToolbar(BlockData b, params ToolbarItem[] items) { foreach (var it in items) b.Toolbar.Add(it); return b; }
        static ToolbarItem Block(int slot, string action, BlockData target, string arg = null) { return new ToolbarItem { Slot = slot, Kind = "Block", Action = action, BlockId = target.EntityId, Argument = arg }; }

        static void TestTimerToolbar()
        {
            Console.WriteLine("-- Timer Block toolbar --");
            foreach (bool automation in new[] { false, true })
            {
                var blocks = ShipBlocks();
                var g0 = blocks[8];                                           // gyro 0
                var timer = Bd("TimerBlock", "TimerBlockSmall", 8, 0, 0, null, null, "Timer");
                timer.Props["Delay"] = "2000";
                WithToolbar(timer, Block(0, "OnOff_Off", g0));
                blocks.Add(timer);
                var w = NewWorld(31, false, s => s.BlockAutomation = automation);
                var bodies = GridFactory.Spawn(w, Bp(blocks), 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null);
                var t = (SimTimer)null; foreach (var k in bodies[0].Blocks) if (k.Api is SimTimer) t = (SimTimer)k.Api;
                var gy = (SimGyro)bodies[0].Gyros[0].Block.Api;
                Check(Math.Abs(t.TriggerDelay - 2f) < 1e-6, "Timer delay read from the blueprint (Delay 2000 ms -> 2 s)");
                t.StartCountdown();
                Steps(w, 1.5);
                Check(t.IsCountingDown && gy.Enabled, "after 1.5 s the countdown is still running");
                Steps(w, 0.8);
                Check(!t.IsCountingDown, "after 2.3 s the countdown has ended");
                Check(gy.Enabled == !automation, automation ? "BlockAutomation on: the expiry ran the toolbar (gyro switched off)" : "BlockAutomation off: the expiry runs nothing (old behaviour)");
                if (automation)
                {
                    gy.Enabled = true; t.StartCountdown(); Steps(w, 1); t.StartCountdown(); Steps(w, 1.5);
                    Check(t.IsCountingDown, "StartCountdown restarts the countdown");
                    t.StopCountdown(); Steps(w, 3);
                    Check(gy.Enabled, "StopCountdown cancels it (no expiry)");
                    gy.Enabled = true; ((IMyTerminalBlock)t).ApplyAction("TriggerNow");
                    Check(!gy.Enabled, "TriggerNow runs the toolbar at once");
                }
            }
        }

        static void TestEventControllers()
        {
            Console.WriteLine("-- Event Controller (cockpit occupied / stored power / block integrity) --");
            var blocks = ShipBlocks();
            var seat = blocks[0]; var g0 = blocks[8]; var thr0 = blocks[1]; var bat0 = blocks[10];
            var timer = Bd("TimerBlock", "TimerBlockSmall", 8, 0, 3, null, null, "Timer D"); timer.Props["Delay"] = "3000";
            WithToolbar(timer, Block(0, "OnOff_Off", thr0));
            // EC cockpit: slot 0 (occupied) Stop timer, slot 1 (empty) Start timer  -- the control_arch dead-man backup
            var ecC = Bd("EventControllerBlock", "EventControllerSmall", 9, 0, 3, null, null, "EC Cockpit"); ecC.Props["SelectedEvent"] = "101"; ecC.Props["Threshold"] = "0"; ecC.Selected.Add(seat.EntityId);
            WithToolbar(ecC, Block(0, "Stop", timer), Block(1, "Start", timer));
            // EC power: battery fraction <= 0.15 (ConditionInvert) -> switch gyro 0 off
            var ecP = Bd("EventControllerBlock", "EventControllerSmall", 10, 0, 3, null, null, "EC Power"); ecP.Props["SelectedEvent"] = "102"; ecP.Props["Threshold"] = "0.15"; ecP.Props["ConditionInvert"] = "true"; ecP.Selected.Add(bat0.EntityId);
            WithToolbar(ecP, Block(0, "OnOff_Off", g0));
            // EC integrity: gyro 1 integrity <= 0.5 -> switch thruster 2 off; AND gate irrelevant with one block
            var gy1 = blocks[9]; var thr1 = blocks[2];
            var ecI = Bd("EventControllerBlock", "EventControllerSmall", 11, 0, 3, null, null, "EC Core"); ecI.Props["SelectedEvent"] = "2"; ecI.Props["Threshold"] = "0.5"; ecI.Props["ConditionInvert"] = "true"; ecI.Selected.Add(gy1.EntityId);
            WithToolbar(ecI, Block(0, "OnOff_Off", thr1));
            blocks.Add(timer); blocks.Add(ecC); blocks.Add(ecP); blocks.Add(ecI);
            var w = NewWorld(41, false, s => s.BlockAutomation = true);
            var bodies = GridFactory.Spawn(w, Bp(blocks), 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null);
            var b = bodies[0];
            var cockpit = Seat(b);
            SimTimer tm = null; SimEventController ec1 = null, ec2 = null, ec3 = null;
            foreach (var k in b.Blocks)
            {
                if (k.Api is SimTimer) tm = (SimTimer)k.Api;
                var ec = k.Api as SimEventController;
                if (ec != null) { if (ec.CustomName == "EC Cockpit") ec1 = ec; else if (ec.CustomName == "EC Power") ec2 = ec; else ec3 = ec; }
            }
            Check(ec1 != null && ec1.EventLabel == "cockpit" && ec2.EventLabel == "power" && ec3.EventLabel == "integrity", "event numbers map to cockpit / power / integrity (SimSettings.EcEventIds)");
            Check(ec1.IsAndModeEnabled == false && ec2.IsLowerOrEqualCondition && Math.Abs(ec2.Threshold - 0.15f) < 1e-6, "EC settings read from the blueprint (Threshold, ConditionInvert)");
            Steps(w, 1);
            Check(!tm.IsCountingDown, "empty seat at start: the first evaluation only records the state (no trigger)");
            cockpit.SetUnderControl(true); Steps(w, 0.5);
            Check(ec1.Fired0 == 1 && !tm.IsCountingDown, "seat taken: slot 0 ran (Stop timer)");
            cockpit.SetUnderControl(false); Steps(w, 0.5);
            Check(ec1.Fired1 == 1 && tm.IsCountingDown, "seat left: slot 1 ran (Start timer)");
            Steps(w, 3.3);
            var thr0Api = (SimThrust)null; foreach (var t in b.Thrusters) if (t.DirIndex == 5) { thr0Api = (SimThrust)t.Block.Api; break; }
            Check(!tm.IsCountingDown && !thr0Api.Enabled, "timer D expired after 3 s and switched the thruster off (dead-man chain: EC -> timer -> toolbar)");
            cockpit.SetUnderControl(true); Steps(w, 0.5);
            Check(tm.Expiries == 1 && !tm.IsCountingDown, "re-entering stops it again (no second expiry)");
            // power
            var gy0 = (SimGyro)b.Gyros[0].Block.Api;
            Check(gy0.Enabled && ec2.Fired0 == 0, "battery full: power EC quiet");
            foreach (var bt in b.Batteries) bt.StoredMWh = bt.MaxStoredMWh * 0.1;
            Steps(w, 0.5);
            b.Batteries[0].StoredMWh = b.Batteries[0].MaxStoredMWh * 0.1;   // the EC watches battery 0 only
            Steps(w, 0.5);
            Check(ec2.Fired0 == 1 && !gy0.Enabled, "stored power 10 % <= 15 %: power EC ran its slot (gyro off)");
            // integrity
            var gy1Api = (SimGyro)b.Gyros[1].Block.Api;
            Check(ec3.Fired0 == 0, "integrity EC quiet while undamaged");
            b.Gyros[1].Block.Hp = b.Gyros[1].Block.MaxHp * 0.4f;
            Steps(w, 0.5);
            Check(ec3.Fired0 == 1, "block integrity 40 % <= 50 %: integrity EC ran its slot");
        }

        static void TestGroupsAndButtons()
        {
            Console.WriteLine("-- block groups, group toolbar slots, Button Panel --");
            var blocks = ShipBlocks();
            var fwd1 = blocks[1]; var fwd2 = blocks[2];
            var btn = Bd("ButtonPanel", "ButtonPanelSmall", 8, 0, 3, null, null, "Panel");
            btn.Toolbar.Add(new ToolbarItem { Slot = 0, Kind = "Group", Action = "OnOff_Off", GroupName = "Fwd" });
            btn.Toolbar.Add(new ToolbarItem { Slot = 1, Kind = "Group", Action = "OnOff_On", GroupName = "Fwd" });
            blocks.Add(btn);
            var bp = Bp(blocks);
            var grp = new GroupData { Name = "Fwd", Construct = 0 };
            grp.Cells.Add(new Vector3I(0, 0, 3)); grp.Cells.Add(new Vector3I(2, 2, 3));
            bp.Grids[0].Groups.Add(grp);
            var w = NewWorld(51, false, s => s.BlockAutomation = true);
            var b = GridFactory.Spawn(w, bp, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null)[0];
            var panel = (SimButtonPanel)null; foreach (var k in b.Blocks) if (k.Api is SimButtonPanel) panel = (SimButtonPanel)k.Api;
            var thrusters = new List<SimThrust>(); foreach (var t in b.Thrusters) if (t.DirIndex == 5) thrusters.Add((SimThrust)t.Block.Api);
            Check(thrusters.Count == 2, "two forward thrusters in the group");
            // PB-side API: GetBlockGroups
            var pbBlocks = new List<BlockData>(blocks) { Bd("MyProgrammableBlock", "SmallProgrammableBlock", 9, 0, 3) };
            var bp2 = Bp(pbBlocks); bp2.Grids[0].Groups.Add(grp);
            var w2 = NewWorld(52, false);
            var b2 = GridFactory.Spawn(w2, bp2, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null)[0];
            SimProgrammableBlock pb = null; foreach (var k in b2.Blocks) if (k.Api is SimProgrammableBlock) pb = (SimProgrammableBlock)k.Api;
            var gts = new SimGridTerminalSystem(pb);
            var groups = new List<IMyBlockGroup>(); gts.GetBlockGroups(groups);
            var members = new List<IMyTerminalBlock>(); if (groups.Count == 1) groups[0].GetBlocks(members);
            Check(groups.Count == 1 && groups[0].Name == "Fwd" && members.Count == 2 && gts.GetBlockGroupWithName("Fwd") != null && gts.GetBlockGroupWithName("x") == null, "GTS.GetBlockGroups lists the blueprint's group with its 2 thrusters");
            panel.Press(0);
            Check(!thrusters[0].Enabled && !thrusters[1].Enabled, "Button Panel slot 0: group action OnOff_Off switched both thrusters off");
            panel.Press(1);
            Check(thrusters[0].Enabled && thrusters[1].Enabled && panel.Presses == 2, "slot 1: OnOff_On switched them on");
            // a destroyed group member drops out of the group and the action survives
            b.Thrusters[0].Block.Hp = 0;
            Check(!panel.Press(7), "an empty slot does nothing");
        }

        // ----------------------------------------------------------------------------------------------------------- merge fuse
        static void TestMergeFuse()
        {
            Console.WriteLine("-- merge fuse pass (S9) --");
            // host: armor + merge block at (0,0,0) facing +X; drone: armor + merge block, offset one cell, facing -X
            Func<int, string, BlockData> merge = (x, face) => { var m = Bd("MergeBlock", "SmallShipSmallMergeBlock", x, 0, 0, null, null, "M"); return m; };
            // merge block faces Up (local Up): host merge Forward-Up (identity) faces +Y. Put the drone ABOVE: centre one cell up, drone merge rotated to face down.
            var host = new List<BlockData> { Armor(0, 0, 0), Armor(1, 0, 0), Armor(2, 0, 0), Bd("MergeBlock", "SmallShipSmallMergeBlock", 1, 1, 0, null, null, "Host Merge"), Thrust(0, 0, 1, "Backward"), Bat(2, 0, 1), Gyro(0, 1, 0) };
            var drone = new List<BlockData> { Bd("MergeBlock", "SmallShipSmallMergeBlock", 0, 0, 0, "Forward", "Down", "Drone Merge"), Armor(0, 1, 0), Armor(1, 1, 0), Gyro(0, 2, 0), Bat(0, 1, 1) };
            var bp = Bp(host, drone);
            bp.Grids[1].Position = new Vector3D(0.5, 1.0, 0);               // drone grid origin: its merge block (cell 0,0,0) sits at host cell (1,2,0): x +1 cell, y +2 cells
            bp.Grids[1].Forward = Vector3D.Forward; bp.Grids[1].Up = Vector3D.Up;
            bp.Grids[0].Position = Vector3D.Zero;
            {
                var w = NewWorld(61);
                var bodies = GridFactory.Spawn(w, bp, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null);
                Check(bodies.Count == 1 && bodies[0].Blocks.Count == host.Count + drone.Count, "two grids with touching enabled merge blocks spawn as ONE body (" + bodies.Count + " body, " + bodies[0].Blocks.Count + " blocks)");
                int c0 = 0, c1 = 0; foreach (var k in bodies[0].Blocks) { if (k.ConstructId == 0) c0++; else if (k.ConstructId == 1) c1++; }
                Check(c0 == host.Count && c1 == drone.Count, "ConstructId keeps the origin grid per block (" + c0 + " host, " + c1 + " drone)");
                var merges = new List<SimMergeBlock>(); foreach (var k in bodies[0].Blocks) if (k.Api is SimMergeBlock) merges.Add((SimMergeBlock)k.Api);
                Check(merges.Count == 2 && merges[0].IsConnected && merges[1].IsConnected, "the merge pair is a locked pair in the fused body");
                // release: both sides off -> two bodies, and each keeps its own blocks
                merges[0].Enabled = false; merges[1].Enabled = false;
                Steps(w, 1);
                Check(w.Bodies.Count == 2, "switching the merge pair off splits the ship into two bodies again (" + w.Bodies.Count + ")");
                int total = 0; foreach (var b in w.Bodies) { total += b.Blocks.Count; bool allSame = true; int cid = b.Blocks[0].ConstructId; foreach (var k in b.Blocks) if (k.ConstructId != cid) allSame = false; Check(allSame, "  body of " + b.Blocks.Count + " blocks holds blocks of one origin grid only"); }
                Check(total == host.Count + drone.Count, "no block lost in the split");
            }
            {   // a disabled merge block in the blueprint: two bodies from the start
                host[3].Enabled = false;
                var w = NewWorld(62);
                var bodies = GridFactory.Spawn(w, bp, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null);
                Check(bodies.Count == 2, "a switched-off host merge block: no fuse, two bodies");
                host[3].Enabled = true;
            }
            {   // the same pair, drone grid rotated 90 deg about Y in the blueprint: fused through a rotated lattice
                var drone2 = new List<BlockData>();
                foreach (var d in drone) drone2.Add(d.Clone());
                var bp2 = Bp(host, drone2);
                bp2.Grids[1].Position = new Vector3D(0.5, 1.0, 0);
                // yaw 90 deg: grid Forward (-Z) -> -X ... world = local * R: choose Forward=Left, Up=Up. Drone cell (i,j,k) -> offset (-k.., ...); place so the merge block still touches
                bp2.Grids[1].Forward = Vector3D.Left; bp2.Grids[1].Up = Vector3D.Up;
                var w = NewWorld(63);
                var bodies = GridFactory.Spawn(w, bp2, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null);
                Check(bodies.Count == 1 && bodies[0].Blocks.Count == host.Count + drone.Count, "a drone grid yawed 90 deg fuses too (rotated lattice): " + bodies.Count + " body");
                // the drone's blocks keep their world positions: compare the gyro of the drone (0,2,0) in the drone frame to where the fused body has it
                BlockInst droneBat = null; foreach (var k in bodies[0].Blocks) if (k.ConstructId == 1 && k.Def.Kind == "BatteryBlock") droneBat = k;
                // drone cell (0,1,1) with the drone yawed so its +Z points along host +X: host cell (1+1, 2+1, 0) = (2,3,0) -> (1.0, 1.5, 0.0) m
                Check(droneBat != null && (droneBat.LocalCenter - new Vector3D(1.0, 1.5, 0.0)).Length() < 1e-6, "the yawed drone's battery (drone cell 0,1,1) lands at host cell (2,3,0) = (1.0, 1.5, 0) m (" + (droneBat == null ? "missing" : droneBat.LocalCenter.ToString()) + ")");
            }
            {   // ordinary multi-grid blueprints (no merge pair) are left alone
                var other = new List<BlockData> { Armor(0, 0, 0), Gyro(1, 0, 0) };
                var bp3 = Bp(host, other); bp3.Grids[1].Position = new Vector3D(20, 0, 0);
                var w = NewWorld(64);
                var bodies = GridFactory.Spawn(w, bp3, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null);
                Check(bodies.Count == 2, "two grids without a merge pair stay two bodies");
                var w4 = NewWorld(65, false, s => s.FuseMergedGrids = false);
                var bodies4 = GridFactory.Spawn(w4, bp, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null);
                Check(bodies4.Count == 2, "FuseMergedGrids=false: the touching pair stays two bodies (old behaviour)");
            }
        }

        // ----------------------------------------------------------------------------------------------------------- PBs
        const string Spinner = "public Program() { Runtime.UpdateFrequency = UpdateFrequency.Update1; }\n"
            + "public void Main(string arg, UpdateType u) { if (arg.Length > 0) Echo(arg); var l = new List<IMyGyro>(); GridTerminalSystem.GetBlocksOfType(l); foreach (var g in l) { g.GyroOverride = true; g.Yaw = 0.5f; g.Pitch = 0f; g.Roll = 0f; } }\n";

        static void TestKillPb()
        {
            Console.WriteLine("-- KillPb scenario step (dead-man test) --");
            var blocks = ShipBlocks();
            var pb = Bd("MyProgrammableBlock", "SmallProgrammableBlock", 8, 0, 3, null, null, "[Helm] PB"); pb.Program = Spinner; blocks.Add(pb);
            var pb2 = Bd("MyProgrammableBlock", "SmallProgrammableBlock", 9, 0, 3, null, null, "[Staff] PB"); pb2.Program = "public Program() { Runtime.UpdateFrequency = UpdateFrequency.Update10; } public void Main(string a, UpdateType u) { }"; blocks.Add(pb2);
            var w = NewWorld(71, true, s => s.BlockAutomation = true);
            var bodies = GridFactory.Spawn(w, Bp(blocks), 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null);
            SeSim.Api.BlockApiFactory.FlushPending(w);
            var sh = Ship(w, bodies, 0, 0, null);
            foreach (var r in w.Scripts.Runners) sh.Pbs.Add(r);
            Check(sh.PbsByTag("Helm").Count == 1 && sh.PbsByTag("Staff").Count == 1 && sh.PbsByTag("Pod").Count == 0, "PbsByTag matches the [Helm] / [Staff] name prefixes");
            sh.Pilot = new ScriptedPilot(sh, w.Scripts, "3:killpb Helm"); w.Brains.Add(sh.Pilot);
            Steps(w, 2);
            double w2 = bodies[0].AngularVelocity.Length();
            Check(!sh.PbsByTag("Helm")[0].Dead && w2 > 0.3, "the Helm script spins the ship by gyro override (" + w2.ToString("F2") + " rad/s at 2 s)");
            Steps(w, 2);
            var helm = sh.PbsByTag("Helm")[0];
            Check(helm.Dead && helm.DeathReason.StartsWith("killed by scenario") && !sh.PbsByTag("Staff")[0].Dead, "KillPb at 3 s: the Helm PB is dead, the Staff PB lives (" + helm.DeathReason + ")");
            Check(bodies[0].Gyros[0].Override && Math.Abs(bodies[0].AngularVelocity.Length() - 0.5) < 0.1, "the overrides stay in the gyros after the PB died: the ship keeps its commanded 0.5 rad/s (" + bodies[0].AngularVelocity.Length().ToString("F2") + ")");
            // per-tag command delivery
            helm.PendingArgs.Clear();
            var cmdShip = sh;
            var tagged = cmdShip.PbsByTag("Staff");
            Check(tagged.Count == 1, "Staff command target resolved");
        }

        static void TestOptInHash()
        {
            Console.WriteLine("-- settings hash --");
            var a = new SimSettings(); var b = new SimSettings();
            Check(a.Hash() == b.Hash() && a.Describe().IndexOf("PilotRateGain", StringComparison.Ordinal) < 0, "opt-in M0 settings are invisible in Describe()/Hash() at their defaults (older run hashes stay valid)");
            b.PilotRateGain = 2.0;
            Check(b.Hash() != a.Hash() && b.Describe().IndexOf("PilotRateGain=2", StringComparison.Ordinal) >= 0, "a changed opt-in setting shows up in the hash");
            var c = new SimSettings(); c.ApplyOverride("block-automation", "true"); c.ApplyOverride("enemy-scripted", "true"); c.ApplyOverride("pilot-rate-gain", "1");
            Check(c.BlockAutomation && c.EnemyScripted && c.PilotRateGain == 1.0, "--block-automation / --enemy-scripted / --pilot-rate-gain parse as settings");
        }

        // ----------------------------------------------------------------------------------------------------------- integration with generated blueprints
        static void TestIntegration()
        {
            Console.WriteLine("-- scenario integration (generated test blueprints) --");
            string bpDir = Path.Combine(Path.Combine(_repo, "out"), "Blueprints");
            if (!File.Exists(Path.Combine(bpDir, "Hornet Pilot Test", "bp.sbc")) || !File.Exists(Path.Combine(bpDir, "Twin PB Test", "bp.sbc")) || !File.Exists(Path.Combine(bpDir, "Hornet", "bp.sbc")))
            { SelfTestSkips.Note("M0 integration (build: python3 -I tools/build_all.py --only \"Hornet Pilot Test\" \"Twin PB Test\" Hornet)"); return; }
            IntegrationPilotHornet(bpDir);
            IntegrationDeadman(bpDir);
            IntegrationTwinPb(bpDir);
            IntegrationEnemyScripted(bpDir);
        }

        static Scenario Duel(string a, string b, string pilot, string cmdA)
        {
            var sc = new Scenario { Kind = "1v1", Name = "m0/" + a + "-vs-" + b, StartDistanceMin = 1500, StartDistanceMax = 1500, MaxTicks = 60 * 60 };
            sc.Teams.Add(new TeamSpec(0, a)); sc.Teams.Add(new TeamSpec(1, b));
            var sa = new ShipSpec { Model = a, Team = 0, Role = "A1", Pilot = pilot };
            if (cmdA != null) sa.Commands.Add(new KeyValuePair<double, string>(0.5, cmdA));
            var sb = new ShipSpec { Model = b, Team = 1, Role = "B1" };
            sc.Ships.Add(sa); sc.Ships.Add(sb);
            return sc;
        }

        static void IntegrationPilotHornet(string bpDir)
        {
            // "scripted pilot flies a Hornet with an added cockpit 10 s with no script": thrust within 5 % of the analytic response (3 seeds), power model ON
            for (int seed = 0; seed < 3; seed++)
            {
                var sc = Duel("Hornet Pilot Test", "Hornet", "1:sit;1:move 0 0 -1;11:stop", null);
                double vAlong = 0, expect = 0, side = 0; Body probe = null; string err = null;
                var opt = new RunOptions { SimDir = _simDir, OnTick = null };
                var s = new SimSettings(); s.MaxTicks = 12 * 60;
                ShipInstance ps = null; Vector3D fwdAt11 = default(Vector3D); double massAt1 = 0, fAt1 = 0; double vAt1 = 0;
                opt.OnTick = (w, host, ships) =>
                {
                    ps = ships[0];
                    var b = ps.Main;
                    if (w.Tick == 60) { massAt1 = b.Mass; fAt1 = Force(b, 5); vAt1 = b.LinearVelocity.Length(); Vector3D f = b.DirToWorld(new Vector3D(0, 0, -1)); fwdAt11 = f; probe = b; }
                    if (w.Tick == 11 * 60) { vAlong = Vector3D.Dot(b.LinearVelocity, fwdAt11); side = (b.LinearVelocity - fwdAt11 * vAlong).Length(); }
                };
                var r = ScenarioRunner.RunOne(sc, seed, 1UL, s, _repo, opt);
                err = r.Error;
                expect = fAt1 / massAt1 * 10 + 0;
                // the Hornet's batteries are asked for the thrust: a power shortfall would show as a lower speed; report instead of hiding it
                Check(err == null && Within(vAlong, expect, 0.05) && side < 0.03 * expect, "Hornet Pilot Test seed " + seed + ": 10 s forward stick = " + vAlong.ToString("F2") + " m/s vs analytic F/m*t " + expect.ToString("F2") + " (side " + side.ToString("F3") + ", power model on)" + (err != null ? " ERROR " + err : ""));
            }
        }

        static int EnabledThrusters(Body b) { int n = 0; foreach (var t in b.Thrusters) if (t.Enabled) n++; return n; }

        static void IntegrationDeadman(string bpDir)
        {
            // the dead-man backup of control_arch s2.6 from the GENERATED XML: EC Cockpit (placeholder event) -> Timer D (5 s) -> group 'Thrusters' off;
            // the Button Panel switches them on again; the cockpit toolbar slot 1 is Trigger Now on Timer D
            Func<string, double, int[]> run = (pilot, until) =>
            {
                var sc = Duel("Hornet Pilot Test", "Hornet", pilot, null);
                var res = new List<int>(); int total = 0;
                var s = new SimSettings(); s.MaxTicks = (int)(until * 60) + 5;
                var opt = new RunOptions { SimDir = _simDir };
                opt.OnTick = (w, host, ships) =>
                {
                    if (w.Tick % 30 == 0 && w.Tick / 30 <= 2 * until) res.Add(EnabledThrusters(ships[0].Main));
                    total = ships[0].Main.Thrusters.Count;
                };
                var r = ScenarioRunner.RunOne(sc, 0, 1UL, s, _repo, opt);
                if (r.Error != null) Console.WriteLine("  ERROR " + r.Error);
                res.Add(total);
                return res.ToArray();
            };
            // sit 1 s .. 3 s, leave: EC slot 1 starts Timer D at ~3 s, it expires at ~8 s and the thrusters go off; the panel (at 11 s) switches them on again
            int[] a = run("1:sit;3:leave;11:button Hornet Pilot Test Panel 0", 13);
            int ntot = a[a.Length - 1];
            Func<int, int> atSecond = sec => a[sec * 2];
            Check(atSecond(2) == ntot && atSecond(6) == ntot, "empty seat: Timer D counts down, thrusters still on at 6 s (" + atSecond(6) + "/" + ntot + ")");
            Check(atSecond(9) == 0, "Timer D expired (5 s after the pilot left): the toolbar switched the thrusters off at 9 s (" + atSecond(9) + "/" + ntot + ")");
            Check(atSecond(12) == ntot, "Button Panel slot 0 (group Thrusters OnOff_On) switched them on again (" + atSecond(12) + "/" + ntot + ")");
            // a pilot who stays seated never trips it
            int[] b = run("1:sit", 12);
            Check(b[b.Length - 2] == b[b.Length - 1], "seated pilot: the dead-man never fires (" + b[b.Length - 2] + "/" + b[b.Length - 1] + " thrusters on at 12 s)");
            // cockpit toolbar slot 1 = Trigger Now on Timer D: cuts the thrusters at once
            int[] c = run("1:sit;4:press 1", 6);
            Check(c[2] == c[c.Length - 1] && c[c.Length - 2] == 0, "cockpit toolbar 'press 1' (TriggerNow Timer D) switched the thrusters off (" + c[c.Length - 2] + " of " + c[c.Length - 1] + " on at 6 s)");
        }

        static void IntegrationTwinPb(string bpDir)
        {
            // per-PB commands and CustomData: two PBs on one ship ([Helm] runs HornetDroneAI, [Staff] runs the same script here only as a probe)
            var sc = Duel("Twin PB Test", "Hornet", null, "Helm:hunt");
            foreach (var sp in sc.Ships) if (sp.Team == 0) { sp.PbCustomData["Helm"] = "[Hornet]\nIGC Tag=HELMTEST"; sp.PbCustomData["Staff"] = "[Hornet]\nIGC Tag=STAFFTEST"; }
            string helmCd = null, staffCd = null; int helmRuns = 0, staffRuns = 0, ncmd = 0;
            var opt = new RunOptions { SimDir = _simDir };
            var s = new SimSettings(); s.MaxTicks = 6 * 60;
            opt.Inspect = (w, host, ships) =>
            {
                foreach (var r in ships[0].Pbs)
                {
                    if (r.Pb.CustomNameValue.StartsWith("[Helm]")) { helmCd = r.Pb.CustomData; helmRuns = (int)r.Runs; }
                    if (r.Pb.CustomNameValue.StartsWith("[Staff]")) { staffCd = r.Pb.CustomData; staffRuns = (int)r.Runs; }
                }
                ncmd = ships[0].Pbs.Count;
            };
            var res = ScenarioRunner.RunOne(sc, 0, 1UL, s, _repo, opt);
            Check(res.Error == null && ncmd == 2, "Twin PB Test spawns with two PBs" + (res.Error != null ? " ERROR " + res.Error : ""));
            Check(helmCd != null && helmCd.Contains("HELMTEST") && staffCd != null && staffCd.Contains("STAFFTEST"), "PbCustomData goes to the PB with the matching [Tag] name prefix only");
            Check(helmRuns > 100, "the 'Helm:hunt' command reached the [Helm] PB and it runs (" + helmRuns + " Main calls)");
        }

        static void IntegrationEnemyScripted(string bpDir)
        {
            // --enemy-scripted: copy the Hornet blueprint into a temp enemies dir; its own PB flies and fights, no Pirate brain
            string tmp = Path.Combine(Path.GetTempPath(), "m0enemies_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string old = ScenarioGen.EnemyDir; bool oldS = ScenarioGen.EnemyScripted;
            try
            {
                Directory.CreateDirectory(Path.Combine(tmp, "Hornet Enemy"));
                File.Copy(Path.Combine(bpDir, "Hornet", "bp.sbc"), Path.Combine(tmp, "Hornet Enemy", "bp.sbc"));
                ScenarioGen.EnemyDir = tmp; ScenarioGen.EnemyScripted = true;
                var sc = ScenarioGen.Build("enemy", new[] { "Hornet Enemy" }, new[] { "Hornet" });
                Check(sc.Ships[0].Commands.Count == 2 && sc.Ships[0].Commands[0].Value == "Helm:hunt" && sc.Ships[0].Commands[1].Value == "Staff:wing satellite", "enemy-scripted default commands: 'Helm:hunt' and 'Staff:wing satellite' (dropped at spawn when no [Staff] PB exists)");
                sc.StartDistanceMin = sc.StartDistanceMax = 700; sc.MaxTicks = 50 * 60;
                var s = new SimSettings(); s.EnemyScripted = true; s.MaxTicks = 50 * 60;
                int pbs = 0; long runs = 0; bool brain = true; int shots = 0;
                var opt = new RunOptions { SimDir = _simDir };
                opt.Inspect = (w, host, ships) => { pbs = ships[0].Pbs.Count; foreach (var r in ships[0].Pbs) runs += r.Runs; brain = ships[0].HasBrain; };
                var res = ScenarioRunner.RunOne(sc, 0, 1UL, s, _repo, opt);
                double eAvg = 0, eMax = 0; foreach (var sh in res.Ships) if (sh.Ship == 0) { shots = (int)sh.Shots; eAvg = sh.AvgSpeed; eMax = sh.MaxSpeed; }
                Check(res.Error == null && pbs >= 1 && runs > 500 && !brain, "enemy ship runs its own PB script (" + runs + " Main calls), no brain" + (res.Error != null ? " ERROR " + res.Error : ""));
                Info("the scripted enemy: " + shots + " shots, top speed " + eMax.ToString("F0") + " m/s in " + res.DurationS.ToString("F0") + " s (" + res.Outcome + ", winner team " + res.WinnerTeam + "); was a hard check until the Hornet got its [Thrust] PB: the duel now ends at about 19 s with the enemy still searching (AI blocks fly it), see docs/thrust-vectoring.md");
            }
            finally
            {
                ScenarioGen.EnemyDir = old; ScenarioGen.EnemyScripted = oldS;
                try { Directory.Delete(tmp, true); } catch { }
            }
        }
    }
}
