// SLOT B. Maps a BlockInst to its API object by TypeId (blueprint xsi type without "MyObjectBuilder_"):
//   Thrust->SimThrust, Gyro->SimGyro, CameraBlock->SimCamera, SensorBlock->SimSensor, BatteryBlock->SimBattery,
//   SmallGatlingGun->SimGatlingGun, SmallMissileLauncher->SimMissileLauncher, SmallMissileLauncherReload->SimRailgun,
//   LargeGatlingTurret/InteriorTurret->SimLargeGatlingTurret, LargeMissileTurret->SimLargeMissileTurret, TurretControlBlock->SimTurretControlBlock,
//   MergeBlock->SimMergeBlock, Projector->SimProjector, ShipWelder->SimShipWelder, MyProgrammableBlock->SimProgrammableBlock,
//   RemoteControl->SimRemoteControl, RadioAntenna->SimRadioAntenna, Warhead->SimWarhead,
//   OffensiveCombatBlock/FlightMovementBlock/DefensiveCombatBlock/BasicMissionBlock/Decoy/... (functional) -> SimGenericFunctional,
//   TimerBlock/EventControllerBlock/CargoContainer/ButtonPanel -> SimGenericTerminal, CubeBlock (armor) / non-terminal defs -> null.
// Applies blueprint state: CustomName, CustomData, Enabled (in the classes), BroadcastRadius, EnableBroadcasting, FieldMin/Max,
// Detect* (in the classes), ChargeMode, CurrentStoredPower (here); registers cameras/sensors in World lists, antennas and PB
// endpoints in World.Igc.
//
// PB scripts are attached AFTER the whole spawn (their constructors scan the grid): Create queues the PB in
// IgcHub.PendingScripts; GridFactory should call BlockApiFactory.FlushPending(world) at the end of Spawn (request to slot A),
// and IgcHub.Deliver (World phase 2) flushes anything left, so a PB spawned without the explicit call is attached at the end
// of its first tick and first runs on the next one.
using System;
using System.Collections.Generic;
using SeSim.Core;

namespace SeSim.Api
{
    public static class BlockApiFactory
    {
        const string Prefix = "MyObjectBuilder_";
        static string Norm(string t) { return t != null && t.StartsWith(Prefix, StringComparison.Ordinal) ? t.Substring(Prefix.Length) : (t ?? ""); }

        public static SimTerminalBlock Create(BlockInst b, World w)
        {
            if (w.Igc == null) w.Igc = new IgcHub(w);
            SimTerminalBlock api = Make(b, w);
            if (api == null) return null;
            b.Api = api;
            var cam = api as SimCamera; if (cam != null) w.Cameras.Add(cam);
            var sen = api as SimSensor; if (sen != null) w.Sensors.Add(sen);
            var ant = api as SimRadioAntenna; if (ant != null) w.Igc.RegisterAntenna(ant);
            var bat = api as SimBattery; if (bat != null) ApplyBattery(bat);
            var pb = api as SimProgrammableBlock;
            if (pb != null)
            {
                pb.Igc = new SimIgc(pb, w.Igc);
                w.Igc.Register(pb.Igc);
                if (!string.IsNullOrEmpty(pb.ProgramText)) w.Igc.PendingScripts.Add(pb);
            }
            return api;
        }

        static SimTerminalBlock Make(BlockInst b, World w)
        {
            string t = Norm(b.Def.TypeId);
            switch (t)
            {
                case "Thrust": return new SimThrust(b, w);
                case "Gyro": return new SimGyro(b, w);
                case "CameraBlock": return new SimCamera(b, w);
                case "SensorBlock": return new SimSensor(b, w);
                case "BatteryBlock": return new SimBattery(b, w);
                case "SmallGatlingGun": return new SimGatlingGun(b, w);
                case "SmallMissileLauncher": return new SimMissileLauncher(b, w);
                case "SmallMissileLauncherReload": return new SimRailgun(b, w);
                case "LargeGatlingTurret":
                case "InteriorTurret": return new SimLargeGatlingTurret(b, w);
                case "LargeMissileTurret": return new SimLargeMissileTurret(b, w);
                case "TurretControlBlock": return new SimTurretControlBlock(b, w);
                case "MergeBlock": return new SimMergeBlock(b, w);
                case "Projector": return new SimProjector(b, w);
                case "ShipWelder": return new SimShipWelder(b, w);
                case "MyProgrammableBlock": return new SimProgrammableBlock(b, w);
                case "RemoteControl": return new SimRemoteControl(b, w);
                case "RadioAntenna": return new SimRadioAntenna(b, w);
                case "Warhead": return new SimWarhead(b, w);
                case "CubeBlock": return null;
                case "TimerBlock":
                case "EventControllerBlock":
                case "CargoContainer":
                case "ButtonPanel": return new SimGenericTerminal(b, w);
            }
            if (!b.Def.IsTerminal) return null;
            return new SimGenericFunctional(b, w);
        }

        static void ApplyBattery(SimBattery bat)
        {
            BatteryUnit u = bat.Unit;
            var src = bat.Inst.Src;
            if (u == null || src == null) return;
            string s;
            if (src.Props.TryGetValue("ChargeMode", out s))
            {
                int m;
                if (int.TryParse(s.Trim(), out m) && m >= 0 && m <= 2) u.Mode = (Sandbox.ModAPI.Ingame.ChargeMode)m;
                else
                {
                    Sandbox.ModAPI.Ingame.ChargeMode cm;
                    if (Enum.TryParse(s.Trim(), true, out cm)) u.Mode = cm;
                }
            }
            double v;
            if (src.Props.TryGetValue("CurrentStoredPower", out s)
                && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                u.StoredMWh = Math.Max(0, Math.Min(v, u.MaxStoredMWh > 0 ? u.MaxStoredMWh : v));
        }

        /// <summary>Attaches the programs of all PBs created since the last flush (after the grid is complete).</summary>
        public static void FlushPending(World w)
        {
            var hub = w.Igc;
            if (hub == null || hub.PendingScripts.Count == 0 || w.Scripts == null) return;
            // new PBs created while attaching (none expected) are handled by the next flush
            var list = hub.PendingScripts.ToArray();
            hub.PendingScripts.Clear();
            for (int i = 0; i < list.Length; i++)
            {
                SimProgrammableBlock pb = list[i];
                if (!pb.Inst.Destroyed && pb.Runner == null) w.Scripts.Attach(pb);
            }
        }
    }
}
