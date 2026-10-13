// SLOT B. Self-checks for the block API: terminal property/action round trips, GTS filtering, camera charge/cone/raycast,
// sensor box, IGC reach/relay/timing/queue/payload rules, detection info shape. Called by `SimHost.exe selftest` (and
// `selftest api`). Returns the number of failures; sections that need a not-yet-implemented core feature (slot A) are reported
// as SKIP, never as OK.
//
// The tests build a tiny hand-made World (uninitialised object + reflection for the readonly fields) so they do not depend on
// World/GridFactory/BlueprintLoader; sections that call Raycast/TeamTable/Body helpers use the real slot-A code.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using VRageMath;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SeSim.Blueprint;
using SeSim.Core;

namespace SeSim.Api
{
    public static class SelfTestB
    {
        static int _fails, _checks, _skips;

        static void Check(bool ok, string name)
        {
            _checks++;
            if (!ok) { _fails++; Console.WriteLine("  FAIL " + name); }
        }

        static void Section(string name, Action a)
        {
            int f0 = _fails, c0 = _checks;
            try { a(); }
            catch (NotImplementedException e)
            {
                _skips++;
                Console.WriteLine("  SKIP " + name + " (core not implemented yet: " + FirstFrame(e) + ")");
                return;
            }
            catch (Exception e)
            {
                _fails++;
                Console.WriteLine("  FAIL " + name + ": " + e.GetType().Name + ": " + e.Message + "\n" + FirstFrame(e));
                return;
            }
            Console.WriteLine("  " + (_fails == f0 ? "ok   " : "FAIL ") + name + " (" + (_checks - c0) + " checks)");
        }

        static string FirstFrame(Exception e)
        {
            string s = e.StackTrace ?? ""; int i = s.IndexOf('\n');
            return (i > 0 ? s.Substring(0, i) : s).Trim();
        }

        // ---------------------------------------------------------------------------------------------------------
        // fixtures
        static void SetField(object o, string name, object v)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null) throw new InvalidOperationException("no field " + name);
            f.SetValue(o, v);
        }

        static World MakeWorld()
        {
            var w = (World)FormatterServices.GetUninitializedObject(typeof(World));
            SetField(w, "Settings", new SimSettings());
            SetField(w, "Bodies", new List<Body>());
            SetField(w, "Teams", new TeamTable());
            SetField(w, "Brains", new List<IShipBrain>());
            SetField(w, "Sensors", new List<SimSensor>());
            SetField(w, "Cameras", new List<SimCamera>());
            w.Igc = new IgcHub(w);
            return w;
        }

        static Body MakeBody(World w, Vector3D pos, int team, MyCubeSize size = MyCubeSize.Small)
        {
            var b = new Body();
            b.World = w; b.Id = w.Bodies.Count; b.EntityId = 1000 + b.Id; b.DisplayName = "Body" + b.Id;
            b.GridSize = size; b.CellSize = size == MyCubeSize.Small ? 0.5 : 2.5; b.Team = team; b.OwnerId = (TeamTable.IdentityBase + team);
            b.Position = pos; b.Rotation = QuaternionD.Identity; b.RotMatrix = MatrixD.Identity; b.InvWorldRot = MatrixD.Identity;
            b.WorldMatrix = MatrixD.CreateTranslation(pos); b.Mass = 1000; b.InvMass = 1e-3;
            b.BoundCenterLocal = Vector3D.Zero; b.BoundRadius = 2; b.CellMin = Vector3I.Zero; b.CellMax = Vector3I.Zero;
            b.ApiGrid = new SimCubeGrid(b);
            w.Bodies.Add(b);
            return b;
        }

        static BlockInst AddBlock(Body b, string typeId, string subtype, Vector3I cell, string name = null, Dictionary<string, string> props = null)
        {
            var def = new BlockDef();
            def.Key = typeId + "/" + subtype; def.TypeId = typeId; def.Subtype = subtype; def.SizeCells = Vector3I.One; def.Large = b.GridSize == MyCubeSize.Large;
            def.MassKg = 10; def.MaxIntegrity = 100; def.CriticalIntegrity = 50; def.IsTerminal = typeId != "CubeBlock";
            def.ThrustForceN = 14400; def.GyroForce = 448000; def.SensorMaxRange = 50; def.InventoryVolume = typeId.StartsWith("SmallGatling") ? 0.064f : 0f;
            var src = new BlockData();
            src.TypeId = typeId; src.XsiType = "MyObjectBuilder_" + typeId; src.Subtype = subtype; src.Enabled = true; src.CustomName = name;
            src.CustomData = ""; src.Min = cell;
            if (props != null) foreach (var kv in props) src.Props[kv.Key] = kv.Value;
            var i = new BlockInst();
            i.Index = b.Blocks.Count; i.Def = def; i.Src = src; i.EntityId = 5000 + 100 * b.Id + i.Index; i.Min = cell; i.Max = cell; i.Position = cell;
            i.Orientation = MyBlockOrientation.Identity;
            double cs = b.CellSize;
            i.LocalMatrix = MatrixD.CreateTranslation(new Vector3D(cell.X * cs, cell.Y * cs, cell.Z * cs));
            i.LocalCenter = i.LocalMatrix.Translation; i.HalfExtents = new Vector3D(cs / 2, cs / 2, cs / 2); i.Radius = i.HalfExtents.Length();
            i.Mass = 10; i.MaxHp = 100; i.CriticalHp = 50; i.Hp = 100; i.OwnerId = b.OwnerId; i.Body = b;
            b.Blocks.Add(i); b.Version++;
            if (typeId == "Thrust") i.Thruster = new ThrusterUnit { Block = i, MaxForce = 14400, ForceDirLocal = new Vector3D(0, 0, 1) };
            if (typeId == "Gyro") i.Gyro = new GyroUnit { Block = i, RotToGrid = MatrixD.Identity, MaxForce = 448000 };
            if (typeId == "BatteryBlock") i.Battery = new BatteryUnit { Block = i, StoredMWh = 0.05, MaxStoredMWh = 0.05, MaxOutputMW = 0.5 };
            if (typeId == "MergeBlock") i.Merge = new MergeUnit { Block = i };
            BlockApiFactory.Create(i, b.World);
            return i;
        }

        /// <summary>Slot-A bookkeeping after hand-assembling a body: cells, mass properties, world matrices.</summary>
        static void Finish(Body b) { b.BuildCells(); b.RecomputeMassProperties(); b.UpdateWorldMatrix(); }

        // ---------------------------------------------------------------------------------------------------------
        public static int Run()
        {
            _fails = _checks = _skips = 0;
            Console.WriteLine("SelfTestB (block API):");
            Section("immutable payload types", TestImmutable);
            Section("terminal properties and actions", TestProps);
            Section("grid terminal system", TestGts);
            Section("grid / slim block / inventory", TestGrid);
            Section("camera charge and cone", TestCameraCharge);
            Section("camera raycast and detection info", TestCameraRay);
            Section("sensor box", TestSensor);
            Section("IGC delivery, range, relay, queues", TestIgc);
            Section("PB, text surface, antenna, merge, warhead API", TestMisc);
            Section("Hornet blueprint through the API", TestBlueprint);
            Console.WriteLine("SelfTestB: " + _checks + " checks, " + _fails + " failed, " + _skips + " skipped sections");
            return _fails;
        }

        // ---------------------------------------------------------------------------------------------------------
        static void TestImmutable()
        {
            Check(ImmutableTypes.Check(typeof(int)), "int");
            Check(ImmutableTypes.Check(typeof(string)), "string");
            Check(ImmutableTypes.Check(typeof(Vector3D)), "Vector3D");
            Check(ImmutableTypes.Check(typeof(MyTuple<long, Vector3D, Vector3D, double, long>)), "MyTuple5");
            Check(ImmutableTypes.Check(typeof(MyTuple<string, MyTuple<int, bool>>)), "nested tuple");
            Check(!ImmutableTypes.Check(typeof(List<int>)), "List rejected");
            Check(!ImmutableTypes.Check(typeof(int[])), "array rejected");
            Check(!ImmutableTypes.Check(typeof(MyTuple<int, List<int>>)), "tuple with List rejected");
            Check(!ImmutableTypes.Check(typeof(object)), "object rejected");
        }

        static void TestProps()
        {
            var w = MakeWorld(); var b = MakeBody(w, Vector3D.Zero, 0);
            var gy = AddBlock(b, "Gyro", "SmallBlockGyro", new Vector3I(0, 0, 0), "G");
            var th = AddBlock(b, "Thrust", "SmallBlockSmallThrust", new Vector3I(1, 0, 0), "T");
            var se = AddBlock(b, "SensorBlock", "SmallBlockSensor", new Vector3I(2, 0, 0), "S");
            var an = AddBlock(b, "RadioAntenna", "SmallBlockRadioAntenna", new Vector3I(3, 0, 0), "A", new Dictionary<string, string> { { "BroadcastRadius", "5000" }, { "EnableBroadcasting", "true" } });
            var gun = AddBlock(b, "SmallGatlingGun", "", new Vector3I(4, 0, 0), "Gun");
            var g = (IMyGyro)gy.Api; var t = (IMyThrust)th.Api; var s = (IMySensorBlock)se.Api;

            // gyro: raw storage round trip, clamping, dirty flag
            b.GyroDirty = false;
            g.GyroOverride = true; Check(b.GyroDirty && g.GyroOverride, "gyro override sets dirty");
            g.Pitch = 1.25f; g.Yaw = -2f; g.Roll = 0.5f;
            Check(g.Pitch == 1.25f && g.Yaw == -2f && g.Roll == 0.5f, "gyro raw round trip");
            g.Yaw = 100f; Check(Math.Abs(g.Yaw - (float)(2 * Math.PI)) < 1e-5, "gyro clamp to 2pi (small grid)");
            g.GyroPower = 3f; Check(g.GyroPower == 1f, "gyro power clamp");
            ((IMyTerminalBlock)g).SetValueFloat("Pitch", -0.75f); Check(g.Pitch == -0.75f, "SetValueFloat Pitch");
            Check(((IMyTerminalBlock)g).GetValueFloat("Pitch") == -0.75f, "GetValueFloat Pitch");
            ((IMyTerminalBlock)g).SetValueBool("Override", false); Check(!g.GyroOverride, "SetValueBool Override");
            Check(gy.Gyro.Pitch == -0.75f, "raw value reaches GyroUnit");

            // thruster: Newtons, percentage, property "Override"
            t.ThrustOverride = 5000f; Check(th.Thruster.OverrideN == 5000f && Math.Abs(t.ThrustOverridePercentage - 5000f / 14400f) < 1e-6, "thrust override N / pct");
            t.ThrustOverridePercentage = 0.5f; Check(th.Thruster.OverrideN == 7200f, "thrust pct sets N");
            t.ThrustOverride = 1e9f; Check(t.ThrustOverride == 14400f, "thrust override clamp to max");
            t.ThrustOverride = -4f; Check(t.ThrustOverride == 0f, "thrust override clamp at 0");
            ((IMyTerminalBlock)t).SetValueFloat("Override", 100f); Check(th.Thruster.OverrideN == 100f, "thruster property Override");
            Check(t.GridThrustDirection == new Vector3I(0, 0, 1), "GridThrustDirection = Backward");

            // OnOff
            ((IMyTerminalBlock)t).ApplyAction("OnOff_Off"); Check(!t.Enabled && !th.Thruster.Enabled, "OnOff_Off mirrors to unit");
            ((IMyTerminalBlock)t).ApplyAction("OnOff"); Check(t.Enabled && th.Thruster.Enabled, "OnOff toggles");
            Check(((IMyTerminalBlock)t).GetValueBool("OnOff"), "OnOff property");

            // sensor
            Check(((IMyTerminalBlock)s).GetProperty("LeftExtend") == null, "LeftExtend property absent (scripts fall back to Left)");
            Check(((IMyTerminalBlock)s).GetProperty("Left") != null, "Left property present");
            ((IMyTerminalBlock)s).SetValueFloat("Left", 20f); Check(s.LeftExtend == 20f, "sensor Left");
            ((IMyTerminalBlock)s).SetValueFloat("Front", 500f); Check(s.FrontExtend == 50f, "sensor extend clamped to max range");
            ((IMyTerminalBlock)s).SetValueBool("Detect Enemy", false); Check(!((SimSensor)se.Api).DetectEnemy, "Detect Enemy off");
            ((IMyTerminalBlock)s).SetValueBool("Detect Small Ships", true); Check(((SimSensor)se.Api).DetectSmallShips, "Detect Small Ships on");
            string[] ids = { "Detect Players", "Detect Floating Objects", "Detect Small Ships", "Detect Large Ships", "Detect Stations", "Detect Subgrids",
                             "Detect Asteroids", "Detect Owner", "Detect Friendly", "Detect Neutral", "Detect Enemy" };
            bool all = true; foreach (string id in ids) all &= ((IMyTerminalBlock)s).GetProperty(id) != null;
            Check(all, "all Detect* ids");
            Check(((IMyTerminalBlock)s).GetProperty("DetectEnemy") == null, "DetectEnemy (no space) absent");

            // antenna
            var ant = (IMyRadioAntenna)an.Api;
            Check(ant.Radius == 5000f && ant.EnableBroadcasting && ant.IsBroadcasting, "antenna from blueprint");
            ((IMyTerminalBlock)ant).SetValueFloat("Radius", 123f); Check(ant.Radius == 123f, "antenna Radius property");
            ((IMyTerminalBlock)ant).SetValueBool("EnableBroadCast", false); Check(!ant.EnableBroadcasting && !ant.IsBroadcasting, "antenna EnableBroadCast property");

            // guns: no unit attached here -> actions must not crash; with a unit they toggle it
            gun.Weapon = new SeSim.Combat.WeaponUnit { Block = gun };
            var gg = (IMyUserControllableGun)gun.Api;
            ((IMyTerminalBlock)gg).ApplyAction("Shoot_On"); Check(gun.Weapon.ShootOn && gg.IsShooting, "Shoot_On");
            ((IMyTerminalBlock)gg).ApplyAction("Shoot_Off"); Check(!gun.Weapon.ShootOn, "Shoot_Off");
            ((IMyTerminalBlock)gg).ApplyAction("ShootOnce"); ((IMyTerminalBlock)gg).ApplyAction("ShootOnce"); Check(gun.Weapon.ShootOnceRequests == 2, "ShootOnce counts");
            ((IMyTerminalBlock)gg).ApplyAction("Shoot_On"); gg.Enabled = false; Check(!gun.Weapon.ShootOn, "disabling a gun stops shooting");
            ((IMyTerminalBlock)gg).ApplyAction("ShootOnce"); Check(gun.Weapon.ShootOnceRequests == 2, "ShootOnce ignored while disabled");
            Check(!((IMyTerminalBlock)gg).HasAction("NoSuchAction"), "unknown action absent");
            ((IMyTerminalBlock)gg).ApplyAction("NoSuchAction");   // must not throw
            Check(gg.GetInventory(0) != null && gg.GetInventory(0).CurrentVolume.RawValue == 0 && gg.GetInventory(0).MaxVolume.RawValue == 64000, "gun inventory empty/64 L in creative");
        }

        static void TestGts()
        {
            var w = MakeWorld(); var b = MakeBody(w, Vector3D.Zero, 0);
            var armor = AddBlock(b, "CubeBlock", "LargeBlockArmor", new Vector3I(9, 0, 0));
            Check(armor.Api == null, "armor has no API object");
            var pb = AddBlock(b, "MyProgrammableBlock", "SmallProgrammableBlock", new Vector3I(0, 0, 0), "PB");
            var t1 = AddBlock(b, "Thrust", "SmallBlockSmallThrust", new Vector3I(1, 0, 0), "Ion 1");
            var t2 = AddBlock(b, "Thrust", "SmallBlockSmallThrust", new Vector3I(2, 0, 0), "Ion 2 [x]");
            var g1 = AddBlock(b, "Gyro", "SmallBlockGyro", new Vector3I(3, 0, 0), "Gyro");
            var gts = new SimGridTerminalSystem((SimProgrammableBlock)pb.Api);
            var list = new List<IMyTerminalBlock>();
            gts.GetBlocksOfType<IMyThrust>(list); Check(list.Count == 2 && list[0] == t1.Api && list[1] == t2.Api, "thrusters in block order");
            gts.GetBlocksOfType<IMyThrust>(list, x => x.CustomName.Contains("[x]")); Check(list.Count == 1 && list[0] == t2.Api, "predicate, list cleared first");
            var typed = new List<IMyGyro>(); gts.GetBlocksOfType(typed); Check(typed.Count == 1, "typed overload");
            var gens = new List<IMyFunctionalBlock>(); gts.GetBlocksOfType(gens); Check(gens.Count == 4, "functional blocks incl. PB");
            gts.GetBlocks(list); Check(list.Count == 4, "GetBlocks = terminal blocks only (4)");
            Check(gts.GetBlockWithName("Gyro") == g1.Api && gts.GetBlockWithId(t2.EntityId) == t2.Api && gts.GetBlockWithName("nope") == null, "lookup by name/id");
            gts.SearchBlocksOfName("ion", list); Check(list.Count == 2, "SearchBlocksOfName is case-insensitive substring");
            // removal invalidates the cache through Body.Version
            t1.Destroyed = true; b.Blocks.Remove(t1); b.Version++;
            gts.GetBlocksOfType<IMyThrust>(list); Check(list.Count == 1 && list[0] == t2.Api, "destroyed block disappears");
            // below critical integrity: still listed, IsFunctional false
            t2.Hp = 10; gts.GetBlocksOfType<IMyThrust>(list); Check(list.Count == 1 && !list[0].IsFunctional, "damaged block listed, not functional");
            // IsSameConstructAs
            var b2 = MakeBody(w, new Vector3D(100, 0, 0), 1); var other = AddBlock(b2, "Thrust", "SmallBlockSmallThrust", new Vector3I(0, 0, 0), "Other");
            Check(!pb.Api.IsSameConstructAs(other.Api) && pb.Api.IsSameConstructAs(g1.Api), "IsSameConstructAs = same body");
            Check(pb.Api.BlockDefinition.ToString() == "MyObjectBuilder_MyProgrammableBlock/SmallProgrammableBlock", "BlockDefinition.ToString: " + pb.Api.BlockDefinition);
        }

        static void TestGrid()
        {
            var w = MakeWorld(); var b = MakeBody(w, new Vector3D(10, 20, 30), 0);
            var t1 = AddBlock(b, "Thrust", "SmallBlockSmallThrust", new Vector3I(1, 0, 0), "Ion");
            var grid = b.ApiGrid;
            Check(grid.EntityId == b.EntityId && grid.GridSizeEnum == MyCubeSize.Small && grid.GridSize == 0.5f, "grid basics");
            Check(t1.Api.CubeGrid == grid, "block.CubeGrid");
            Vector3D wp = grid.GridIntegerToWorld(new Vector3I(2, 0, 0));
            Check((wp - new Vector3D(11, 20, 30)).Length() < 1e-9, "GridIntegerToWorld");
            var back = grid.WorldToGridInteger(new Vector3D(11, 20, 30));
            Check(back == new Vector3I(2, 0, 0) && grid.IsSameConstructAs(grid), "WorldToGridInteger");
            var sl = new SimSlimBlock(t1);
            Check(sl.BuildIntegrity == 100f && sl.CurrentDamage == 0f && sl.Position == new Vector3I(1, 0, 0), "slim block undamaged");
            t1.Hp = 40f; Check(sl.CurrentDamage == 60f && Math.Abs(sl.DamageRatio - 0.6f) < 1e-6 && sl.BuildIntegrity - sl.CurrentDamage == 40f, "slim block damage");
            Check(sl.FatBlock == t1.Api, "slim FatBlock");
            Check(t1.Api.WorldAABB.Max.X > t1.Api.WorldAABB.Min.X, "block AABB");
            Finish(b);
            Check(grid.CubeExists(new Vector3I(1, 0, 0)) && !grid.CubeExists(new Vector3I(5, 5, 5)) && grid.GetCubeBlock(new Vector3I(1, 0, 0)) != null, "CubeExists / GetCubeBlock");
            Check(grid.GetCubeBlock(new Vector3I(1, 0, 0)).Position == new Vector3I(1, 0, 0) && grid.Min == new Vector3I(1, 0, 0) && grid.Max == grid.Min, "slim position / grid bounds");
            Check(grid.WorldVolume.Radius > 0 && (grid.WorldVolume.Center - b.LocalToWorld(b.BoundCenterLocal)).Length() < 1e-9, "WorldVolume");
        }

        static void TestCameraCharge()
        {
            var w = MakeWorld(); var b = MakeBody(w, Vector3D.Zero, 0);
            var ci = AddBlock(b, "CameraBlock", "SmallCameraBlock", Vector3I.Zero, "Cam");
            var cam = (SimCamera)ci.Api;
            Check(w.Cameras.Count == 1 && w.Cameras[0] == cam, "camera registered in World.Cameras");
            for (int i = 0; i < 60; i++) cam.TickCharge(SimSettings.Dt);
            Check(cam.AvailableScanRange == 0 && !cam.EnableRaycast, "no charge while raycast is off");
            cam.EnableRaycast = true;
            for (int i = 0; i < 60; i++) cam.TickCharge(SimSettings.Dt);
            Check(Math.Abs(cam.AvailableScanRange - 2000.0) < 1e-6, "2 km after 1 s: " + cam.AvailableScanRange);
            Check(cam.CanScan(1999) && !cam.CanScan(2001), "CanScan(distance)");
            Check(cam.TimeUntilScan(1000) == 0 && cam.TimeUntilScan(3000) == 500, "TimeUntilScan: " + cam.TimeUntilScan(3000));
            Check(cam.CanScan(1000, new Vector3D(0, 0, -1)) && !cam.CanScan(1000, new Vector3D(0, 0, 1)) && !cam.CanScan(1000, new Vector3D(1, 0, -0.9)), "CanScan cone (45 deg)");
            Check(cam.CanScan(new Vector3D(0, 0, -500)) && !cam.CanScan(new Vector3D(0, 0, 500)), "CanScan(target)");
            // cone limit on pitch/yaw: empty result, no charge used
            double c0 = cam.AvailableScanRange;
            var e = cam.Raycast(100, 50f, 0f); Check(e.IsEmpty() && cam.AvailableScanRange == c0, "pitch beyond cone: empty, no charge");
            e = cam.Raycast(1e6); Check(e.IsEmpty() && cam.AvailableScanRange == c0, "distance beyond charge: empty, no charge");
            // disabling the block stops charging
            cam.Enabled = false; double c1 = cam.AvailableScanRange; cam.TickCharge(SimSettings.Dt); Check(cam.AvailableScanRange == c1, "disabled camera does not charge");
            cam.Enabled = true;
        }

        static void TestCameraRay()
        {
            var w = MakeWorld(); w.Teams.Init(2);
            var a = MakeBody(w, new Vector3D(0, 0, 0), 0);
            var cam = (SimCamera)AddBlock(a, "CameraBlock", "SmallCameraBlock", Vector3I.Zero, "Cam").Api;
            var tgt = MakeBody(w, new Vector3D(0, 0, -500), 1);
            AddBlock(tgt, "Thrust", "SmallBlockSmallThrust", Vector3I.Zero, "Hull");
            tgt.LinearVelocity = new Vector3D(3, 0, 0);
            Finish(a); Finish(tgt);
            cam.EnableRaycast = true; for (int i = 0; i < 600; i++) cam.TickCharge(SimSettings.Dt);
            double c0 = cam.AvailableScanRange;
            var e = cam.Raycast(1000);
            Check(!e.IsEmpty(), "ray hits the target 500 m ahead");
            Check(e.EntityId == tgt.EntityId, "EntityId = target grid id");
            Check(e.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies, "enemy relation: " + e.Relationship);
            Check(e.HitPosition.HasValue && Math.Abs(e.HitPosition.Value.Z + 499.75) < 0.6, "hit position ~ near face: " + (e.HitPosition.HasValue ? e.HitPosition.Value.ToString() : "null"));
            Check(Math.Abs(e.Velocity.X - 3f) < 1e-6, "velocity");
            Check(e.Type == MyDetectedEntityType.SmallGrid && e.Name == "Small Grid", "type / anonymised name: " + e.Name);
            Check(Math.Abs(cam.AvailableScanRange - (c0 - 1000)) < 1e-9, "scan consumed 1000 m");
            Check(e.TimeStamp > 0 && e.Position == e.BoundingBox.Center, "timestamp / Position = box centre");
            var miss = cam.Raycast(1000, 10f, 0f); Check(miss.IsEmpty(), "ray 10 deg off misses");
            var viaVec = cam.Raycast(tgt.Position); Check(!viaVec.IsEmpty() && viaVec.EntityId == tgt.EntityId, "Raycast(Vector3D)");
            var viaDir = cam.Raycast(800, new Vector3D(0, 0, -1)); Check(!viaDir.IsEmpty(), "Raycast(distance, direction)");
            // ally shows the real name
            tgt.OwnerId = a.OwnerId; tgt.Team = 0;
            var friendly = cam.Raycast(1000); Check(friendly.Relationship == MyRelationsBetweenPlayerAndBlock.Owner && friendly.Name == tgt.DisplayName, "own grid: relation Owner + real name");
        }

        static void TestSensor()
        {
            var w = MakeWorld(); w.Teams.Init(2);
            var a = MakeBody(w, Vector3D.Zero, 0);
            var si = AddBlock(a, "SensorBlock", "SmallBlockSensor", Vector3I.Zero, "Sen");
            var s = (SimSensor)si.Api;
            var tgt = MakeBody(w, new Vector3D(0, 0, -30), 1);
            AddBlock(tgt, "Thrust", "SmallBlockSmallThrust", Vector3I.Zero, "Hull");
            tgt.BoundRadius = 2;
            Finish(a); Finish(tgt);
            Check(w.Sensors.Count == 1, "sensor registered");
            s.FrontExtend = 50; s.LeftExtend = s.RightExtend = s.TopExtend = s.BottomExtend = 10; s.BackExtend = 1;
            s.DetectSmallShips = true; s.DetectEnemy = true;
            s.Poll(); var l = new List<MyDetectedEntityInfo>(); s.DetectedEntities(l);
            Check(l.Count == 1 && l[0].EntityId == tgt.EntityId && s.IsActive, "enemy small ship 30 m ahead detected");
            s.FrontExtend = 20; w.Tick += 10; s.Poll(); s.DetectedEntities(l); Check(l.Count == 0, "outside the box: not detected");
            s.FrontExtend = 50; s.DetectEnemy = false; w.Tick += 10; s.Poll(); s.DetectedEntities(l); Check(l.Count == 0, "Detect Enemy off filters");
            s.LeftExtend = 0.2f; Check(Math.Abs(s.LeftExtend - 1f) < 1e-6, "sensor extent minimum is 1 m");
            s.DetectEnemy = true; s.DetectSmallShips = false; w.Tick += 10; s.Poll(); s.DetectedEntities(l); Check(l.Count == 0, "Detect Small Ships off filters");
            s.DetectSmallShips = true; w.Tick += 3; s.Poll(); s.DetectedEntities(l); Check(l.Count == 0, "poll is rate-limited (list unchanged within 10 ticks)");
            w.Tick += 10; s.Poll(); s.DetectedEntities(l); Check(l.Count == 1, "detected again after the poll interval");
        }

        static SimProgrammableBlock AddPb(Body b, string name, Vector3I cell)
        {
            return (SimProgrammableBlock)AddBlock(b, "MyProgrammableBlock", "SmallProgrammableBlock", cell, name).Api;
        }
        static SimRadioAntenna AddAntenna(Body b, double radius, Vector3I cell)
        {
            return (SimRadioAntenna)AddBlock(b, "RadioAntenna", "SmallBlockRadioAntenna", cell, "Ant",
                new Dictionary<string, string> { { "BroadcastRadius", radius.ToString(System.Globalization.CultureInfo.InvariantCulture) }, { "EnableBroadcasting", "true" } }).Api;
        }

        static void TestIgc()
        {
            var w = MakeWorld();
            var A = MakeBody(w, new Vector3D(0, 0, 0), 0); var B = MakeBody(w, new Vector3D(3000, 0, 0), 0); var C = MakeBody(w, new Vector3D(6000, 0, 0), 0);
            var pa = AddPb(A, "pa", new Vector3I(0, 0, 0)); var pb = AddPb(B, "pb", new Vector3I(0, 0, 0)); var pc = AddPb(C, "pc", new Vector3I(0, 0, 0));
            var aa = AddAntenna(A, 5000, new Vector3I(1, 0, 0)); var ab = AddAntenna(B, 5000, new Vector3I(1, 0, 0)); var ac = AddAntenna(C, 5000, new Vector3I(1, 0, 0));
            IMyIntergridCommunicationSystem ia = pa.Igc, ib = pb.Igc, ic = pc.Igc;
            Check(ia.Me == pa.EntityId, "IGC.Me = PB EntityId");
            var lb = ib.RegisterBroadcastListener("T1"); var lb2 = ib.RegisterBroadcastListener("T1");
            Check(lb == lb2 && lb.Tag == "T1" && lb.IsActive, "same tag -> same listener");
            var lc = ic.RegisterBroadcastListener("T1"); var lcOther = ic.RegisterBroadcastListener("T2");
            var la = ia.RegisterBroadcastListener("T1");

            var msg = new MyTuple<long, Vector3D, double>(42L, new Vector3D(1, 2, 3), 7.5);
            ia.SendBroadcastMessage("T1", msg);
            Check(!lb.HasPendingMessage, "not visible before delivery phase");
            w.Igc.Deliver();
            Check(lb.HasPendingMessage && lc.HasPendingMessage, "delivered to B (3 km, radius 5 km) and C (relay through B)");
            Check(!la.HasPendingMessage, "sender does not receive its own broadcast");
            Check(!lcOther.HasPendingMessage, "other tag untouched");
            var m = lb.AcceptMessage();
            Check(m.Tag == "T1" && m.Source == pa.EntityId && m.As<MyTuple<long, Vector3D, double>>().Item1 == 42L, "message content / Source");
            Check(!lb.HasPendingMessage && lb.AcceptMessage().Tag == null, "AcceptMessage on empty queue -> default");

            // range: A radius 1 km cannot reach B (3 km)
            lc.AcceptMessage();
            aa.Radius = 1000; w.Tick++;   // new tick: antenna graph is rebuilt
            ia.SendBroadcastMessage("T1", "x"); w.Igc.Deliver();
            Check(!lb.HasPendingMessage && !lc.HasPendingMessage, "out of antenna range: not delivered");
            Check(!ia.IsEndpointReachable(pb.EntityId) && ib.IsEndpointReachable(pa.EntityId), "reachability is directional (sender radius)");
            // CurrentConstruct never leaves the body
            aa.Radius = 5000; w.Tick++;
            ia.SendBroadcastMessage("T1", "y", TransmissionDistance.CurrentConstruct); w.Igc.Deliver();
            Check(!lb.HasPendingMessage, "CurrentConstruct does not cross bodies");
            // relay: A (1 km) -> B? no. Move C next to A: A reaches C directly, B (3 km) relays nothing; check relay through B for far C
            ia.SendBroadcastMessage("T1", "z"); w.Igc.Deliver();
            Check(lb.HasPendingMessage && lc.HasPendingMessage, "relay chain A -> B -> C");
            lb.AcceptMessage(); lc.AcceptMessage();
            ab.EnableBroadcasting = false; w.Tick++;
            ia.SendBroadcastMessage("T1", "w"); w.Igc.Deliver();
            Check(lb.HasPendingMessage && !lc.HasPendingMessage, "B receives but cannot relay when its antenna is not broadcasting");
            lb.AcceptMessage(); ab.EnableBroadcasting = true;

            // same body works without antennas
            var p2 = AddPb(A, "pa2", new Vector3I(0, 1, 0)); var l2 = p2.Igc.RegisterBroadcastListener("T1");
            aa.Enabled = false; w.Tick++;
            ia.SendBroadcastMessage("T1", "same", TransmissionDistance.CurrentConstruct); w.Igc.Deliver();
            Check(l2.HasPendingMessage && !lb.HasPendingMessage, "same construct delivery without antenna");
            aa.Enabled = true; l2.AcceptMessage();

            // payload rules
            bool threw = false;
            try { ia.SendBroadcastMessage("T1", new List<int> { 1 }); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "mutable payload rejected");

            // queue cap drops the oldest
            w.Tick++;
            int cap = lb.MaxWaitingMessages;
            for (int i = 0; i < cap + 5; i++) ia.SendBroadcastMessage("T1", i);
            w.Igc.Deliver();
            int first = lb.AcceptMessage().As<int>(); int n = 1; while (lb.HasPendingMessage) { lb.AcceptMessage(); n++; }
            Check(n == cap && first == 5, "queue cap " + cap + " keeps the newest (first=" + first + ", n=" + n + ")");

            // unicast
            var uni = ((SimUnicastListener)ib.UnicastListener);
            w.Tick++;
            Check(ia.SendUnicastMessage(pb.EntityId, "U", "hello") && !uni.HasPendingMessage, "unicast send ok, not visible yet");
            w.Igc.Deliver();
            Check(uni.HasPendingMessage && uni.AcceptMessage().As<string>() == "hello", "unicast delivered to the addressee only");
            Check(!ia.SendUnicastMessage(123456789L, "U", "x"), "unicast to unknown address fails");

            // enemies never receive
            w.Teams.Init(2);
            var E = MakeBody(w, new Vector3D(100, 0, 0), 1); var pe = AddPb(E, "pe", Vector3I.Zero); AddAntenna(E, 5000, new Vector3I(1, 0, 0));
            var le = pe.Igc.RegisterBroadcastListener("T1"); w.Tick++;
            ia.SendBroadcastMessage("T1", "enemy"); w.Igc.Deliver();
            Check(!le.HasPendingMessage && !ia.IsEndpointReachable(pe.EntityId) && !pe.Igc.IsEndpointReachable(pa.EntityId), "enemy team: not reachable, nothing delivered");
            lb.AcceptMessage(); lc.AcceptMessage();
            // disabled listener
            ib.DisableBroadcastListener(lb); w.Tick++;
            ia.SendBroadcastMessage("T1", "after"); w.Igc.Deliver();
            Check(!lb.HasPendingMessage, "disabled listener receives nothing");
            var ls = new List<IMyBroadcastListener>(); ib.GetBroadcastListeners(ls); Check(ls.Count == 0, "GetBroadcastListeners after disable");
        }

        static void TestMisc()
        {
            var w = MakeWorld(); var b = MakeBody(w, Vector3D.Zero, 0);
            var pbi = AddBlock(b, "MyProgrammableBlock", "SmallProgrammableBlock", Vector3I.Zero, "PB");
            var pb = (SimProgrammableBlock)pbi.Api;
            Check(pb.SurfaceCount == 2 && pb.GetSurface(0) != null && pb.GetSurface(2) == null, "PB surfaces");
            var surf = pb.GetSurface(0);
            surf.WriteText("ab"); surf.WriteText("cd", true); Check(surf.GetText() == "abcd", "WriteText append");
            surf.WriteText(new System.Text.StringBuilder("zz")); Check(surf.GetText() == "zz", "WriteText StringBuilder");
            pb.CustomData = "k=v"; Check(pb.CustomData == "k=v", "CustomData");
            Check(pb.CustomName == "PB" && !pb.IsRunning, "PB basics");
            Check(pb.TryRun("x") == false, "TryRun false without a runner");
            Check(pb.GetActionWithName("Recompile") != null && pb.GetActionWithName("Run") != null, "PB actions");
            // RC
            var rc = (SimRemoteControl)AddBlock(b, "RemoteControl", "SmallBlockRemoteControl", new Vector3I(1, 0, 0), "RC").Api;
            b.LinearVelocity = new Vector3D(1, 2, 3); b.AngularVelocity = new Vector3D(0, 0.5, 0);
            var v = rc.GetShipVelocities();
            Check(v.LinearVelocity == b.LinearVelocity && v.AngularVelocity == b.AngularVelocity, "GetShipVelocities");
            rc.DampenersOverride = false; Check(!b.DampenersOn, "DampenersOverride -> Body");
            Check(rc.CalculateShipMass().PhysicalMass == 1000f && rc.GetNaturalGravity() == Vector3D.Zero, "mass / gravity");
            Check(rc.GetProperty("OnOff") == null, "RC has no OnOff (not functional block)");
            // battery
            var bat = (SimBattery)AddBlock(b, "BatteryBlock", "SmallBlockSmallBatteryBlock", new Vector3I(2, 0, 0), "Bat", new Dictionary<string, string> { { "CurrentStoredPower", "0.02" }, { "ChargeMode", "2" } }).Api;
            Check(Math.Abs(bat.CurrentStoredPower - 0.02f) < 1e-7 && bat.ChargeMode == ChargeMode.Discharge, "battery from blueprint props");
            bat.ChargeMode = ChargeMode.Recharge; Check(bat.Unit.Mode == ChargeMode.Recharge, "ChargeMode set");
            // merge: release emits event and clears the pair
            var m1 = (SimMergeBlock)AddBlock(b, "MergeBlock", "SmallShipSmallMergeBlock", new Vector3I(3, 0, 0), "M1").Api;
            var b2 = MakeBody(w, new Vector3D(5, 0, 0), 0);
            var m2 = (SimMergeBlock)AddBlock(b2, "MergeBlock", "SmallShipSmallMergeBlock", new Vector3I(0, 0, 0), "M2").Api;
            m1.Inst.Merge.Partner = m2.Inst.Merge; m2.Inst.Merge.Partner = m1.Inst.Merge;
            Check(m1.IsConnected && m2.IsConnected, "merge connected");
            var sink = new Rec(); w.Events = sink; b.ConnectivityDirty = false;
            m1.Enabled = false;
            Check(!m1.IsConnected && !m2.IsConnected && b.ConnectivityDirty && sink.Count(EventKind.MergeReleased) == 1, "merge release (pair cleared, body dirty, event)");
            // warhead
            var wh = AddBlock(b, "Warhead", "SmallWarhead", new Vector3I(4, 0, 0), "W");
            wh.Warhead = new SeSim.Combat.WarheadUnit { Block = wh };
            var wa = (SimWarhead)wh.Api; wa.IsArmed = true; Check(wa.IsArmed, "warhead armed");
            wa.DetonationTime = 3f; Check(wa.StartCountdown() && wa.IsCountingDown && wa.DetonationTime == 3f, "warhead countdown");
            wa.StopCountdown(); Check(!wa.IsCountingDown, "warhead stop");
            // antenna power request lands in the sink
            var an = AddAntenna(b, 5000, new Vector3I(5, 0, 0));
            an.Inst.Sink = new PowerSink { Block = an.Inst };
            an.Radius = 2500; Check(Math.Abs(an.Inst.Sink.RequestMW - 0.01) < 1e-9, "antenna power = radius/250 kW");
        }

        static void TestBlueprint()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(SelfTestB).Assembly.Location), "..", ".."));
            string bpDir = System.IO.Path.Combine(root, "out/Blueprints"), defs = System.IO.Path.Combine(root, "defs_stats.json");
            if (!System.IO.File.Exists(System.IO.Path.Combine(bpDir, "Hornet/bp.sbc")) || !System.IO.File.Exists(defs))
            { _skips++; Console.WriteLine("  SKIP out/Blueprints/Hornet missing (run tools/build_all.py): blueprint section not run"); return; }
            var w = new World(new SimSettings(), DefDb.Load(defs), 3UL, true);
            w.Teams.Init(2);
            var bp = BlueprintLoader.Get(bpDir, "Hornet");
            var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up };
            var bodies = GridFactory.Spawn(w, bp, 0, 0, at, new Dictionary<long, long>());
            Body b = bodies[0];
            int terminal = 0, withApi = 0;
            foreach (var k in b.Blocks) { if (k.Def.IsTerminal) { terminal++; if (k.Api != null) withApi++; } else if (k.Api != null) { Check(false, "armor/structure has API: " + k.Def.Key); } }
            Check(terminal > 0 && terminal == withApi, "every terminal block has an API object (" + withApi + "/" + terminal + ")");
            SimProgrammableBlock pb = null; foreach (var k in b.Blocks) if (k.Api is SimProgrammableBlock) pb = (SimProgrammableBlock)k.Api;
            Check(pb != null && pb.ProgramText.Length > 1000 && pb.Igc != null && w.Igc.Endpoints.Contains(pb.Igc), "PB with program text and IGC endpoint");
            Check(w.Igc.PendingScripts.Contains(pb), "PB queued for attach (FlushPending)");
            var gts = new SimGridTerminalSystem(pb);
            var th = new List<IMyThrust>(); gts.GetBlocksOfType(th);
            var cams = new List<IMyCameraBlock>(); gts.GetBlocksOfType(cams);
            var guns = new List<IMyUserControllableGun>(); gts.GetBlocksOfType(guns);
            var gy = new List<IMyGyro>(); gts.GetBlocksOfType(gy);
            var bats = new List<IMyBatteryBlock>(); gts.GetBlocksOfType(bats);
            var rcs = new List<IMyRemoteControl>(); gts.GetBlocksOfType(rcs);
            var sens = new List<IMySensorBlock>(); gts.GetBlocksOfType(sens);
            var ants = new List<IMyRadioAntenna>(); gts.GetBlocksOfType(ants);
            Check(th.Count == b.Thrusters.Count && th.Count > 0, "GTS thrusters = body thrusters (" + th.Count + ")");
            Check(cams.Count == w.Cameras.Count && cams.Count > 0 && gy.Count == b.Gyros.Count && gy.Count > 0, "cameras / gyros (" + cams.Count + "/" + gy.Count + ")");
            Check(guns.Count > 0 && rcs.Count == 1 && sens.Count == 1 && ants.Count == 1 && bats.Count > 0, "guns/RC/sensor/antenna/batteries present");
            Check(ants[0].Radius == 5000f && ants[0].EnableBroadcasting, "antenna radius from blueprint");
            double stored = 0; foreach (var x in bats) stored += x.CurrentStoredPower;
            Check(stored > 0, "batteries charged from blueprint (" + stored + " MWh)");
            Check(pb.BlockDefinition.ToString().StartsWith("MyObjectBuilder_MyProgrammableBlock"), "PB definition id: " + pb.BlockDefinition);
            // every block the scripts configure through properties accepts them
            var s = (IMyTerminalBlock)sens[0];
            foreach (string id in new[] { "Left", "Right", "Top", "Bottom", "Front", "Back" }) Check(s.GetProperty(id) != null, "sensor property " + id);
            foreach (var c in cams) { c.EnableRaycast = true; Check(c.EnableRaycast, "EnableRaycast"); }
            Check(b.ApiGrid.GetCubeBlock(bodies[0].Blocks[0].Min) != null, "GetCubeBlock at first block");
            // the slim-block scan used by the scripts: every cell visited once, unique Position per block
            var seen = new HashSet<Vector3I>(); int cellsHit = 0;
            for (int x = b.CellMin.X; x <= b.CellMax.X; x++) for (int y = b.CellMin.Y; y <= b.CellMax.Y; y++) for (int z = b.CellMin.Z; z <= b.CellMax.Z; z++)
            { var p = new Vector3I(x, y, z); if (b.ApiGrid.CubeExists(p)) { cellsHit++; var sl = b.ApiGrid.GetCubeBlock(p); seen.Add(sl.Position); } }
            Check(seen.Count == b.Blocks.Count && cellsHit >= seen.Count, "slim scan finds each block once by Position (" + seen.Count + " blocks, " + cellsHit + " cells)");
        }

        sealed class Rec : IEventSink
        {
            readonly List<SimEvent> _l = new List<SimEvent>();
            public void On(ref SimEvent e) { _l.Add(e); }
            public void EndTick(World w) { }
            public int Count(EventKind k) { int n = 0; foreach (var e in _l) if (e.Kind == k) n++; return n; }
        }
    }
}
