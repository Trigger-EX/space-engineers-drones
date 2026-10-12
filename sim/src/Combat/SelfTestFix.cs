// SLOT C (simfix round). Checks for the artefact fixes of docs/sim-audit-1.md section 7: kinetic-energy collision damage and one
// Collision event per contact episode, functional decoys, unique projectile ids on Hit events, the railgun capacitor from
// Data/EntityComponents.sbc, the AI search range setting. Part of SelfTestC (combat group).
using System;
using System.Collections.Generic;
using System.IO;
using VRageMath;
using SeSim.Core;
using SeSim.Blueprint;

namespace SeSim.Combat
{
    public static partial class SelfTestC
    {
        sealed class FixSink : IEventSink
        {
            public int Collisions, Shots; public int Hits; public double[] CollDmg = new double[8];
            public HashSet<long> HitIds = new HashSet<long>(); public int ZeroIdHits;
            public void On(ref SimEvent e)
            {
                switch (e.Kind)
                {
                    case EventKind.Collision: Collisions++; break;
                    case EventKind.CollisionDamage: if ((uint)e.ShipB < 8u) CollDmg[e.ShipB] += e.Value; break;
                    case EventKind.Shot: Shots++; break;
                    case EventKind.Hit: Hits++; if (e.Id == 0) ZeroIdHits++; else HitIds.Add(e.Id); break;
                }
            }
            public void EndTick(World w) { }
        }

        static double HpLost(Body b) { double s = 0; foreach (var k in b.Blocks) s += k.MaxHp - k.Hp; return s; }

        static void TestCollisionFix()
        {
            // 1. one head-on impact: damage per body = scale * 0.5 * reducedMass * vn^2 (to within the impulse bookkeeping), also for teammates with friendly fire off
            foreach (bool mates in new[] { false, true })
            {
                var sink = new FixSink();
                var s = new SimSettings { FriendlyFire = !mates };
                var w = NewWorld(3, false, sink, s);
                var a = Spawn(w, 0, 0, new Vector3D(0, 0, 0), new Vector3D(0, 0, -10), Wall(4, 4, 1, true).ToArray());
                var b = Spawn(w, mates ? 0 : 1, 1, new Vector3D(0, 0, -4), null, Wall(4, 4, 1, true).ToArray());
                double mu = a.Mass * b.Mass / (a.Mass + b.Mass);
                double expect = s.CollisionDamageScale * 0.5 * mu * 100.0;
                Run(w, 60 * 2);
                Check(sink.Collisions == 1, (mates ? "teammates: " : "enemies: ") + "one Collision event for a single impact (" + sink.Collisions + ")");
                Check(sink.CollDmg[0] > 0 && sink.CollDmg[1] > 0 && Math.Abs(sink.CollDmg[0] - expect) < 0.2 * expect && Math.Abs(sink.CollDmg[1] - expect) < 0.2 * expect,
                    (mates ? "teammates (friendly fire off): " : "enemies: ") + "collision damage " + sink.CollDmg[0].ToString("F1") + " / " + sink.CollDmg[1].ToString("F1") + " vs 0.5*mu*v^2*scale " + expect.ToString("F1"));
                Check(Math.Abs(HpLost(a) - sink.CollDmg[0]) < 0.05 * expect + 1e-3, "block integrity lost equals the reported collision damage");
            }
            {
                var sink = new FixSink();
                var w = NewWorld(3, false, sink, new SimSettings { CollisionDamage = false });
                var a = Spawn(w, 0, 0, Vector3D.Zero, new Vector3D(0, 0, -10), Wall(4, 4, 1, true).ToArray());
                Spawn(w, 1, 1, new Vector3D(0, 0, -4), null, Wall(4, 4, 1, true).ToArray());
                Run(w, 60 * 2);
                Check(sink.Collisions == 1 && HpLost(a) == 0, "CollisionDamage=false: impulses and the event only, no damage");
            }
            // 2. sustained contact: one event per episode (episode events) vs one per 30 ticks (legacy)
            foreach (bool episodes in new[] { true, false })
            {
                var sink = new FixSink();
                var w = NewWorld(3, false, sink, new SimSettings { CollisionEpisodeEvents = episodes, CollisionDamage = false });
                var a = Spawn(w, 0, 0, Vector3D.Zero, null, Wall(4, 4, 1, true).ToArray());
                var b = Spawn(w, 1, 1, new Vector3D(0, 0, -3), null, Wall(4, 4, 1, true).ToArray());
                b.IsStatic = true;
                for (int i = 0; i < 240; i++) { a.LinearVelocity = new Vector3D(0, 0, -2); w.Step(); }
                if (episodes) Check(sink.Collisions == 1, "4 s of sustained contact = " + sink.Collisions + " Collision event (episode events)");
                else Check(sink.Collisions >= 6, "legacy cooldown counting: " + sink.Collisions + " events in 4 s of contact");
            }
        }

        static void TestDecoys()
        {
            var w = NewWorld(5, true, new FixSink());
            var ship = Spawn(w, 1, 1, new Vector3D(100, 0, 0), null, Wall(6, 6, 2, true).Concat1Many(
                Bd("Decoy", "SmallDecoy", 6, 0, 0), Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 3, 3, 3)).ToArray());
            BlockInst dec = null; foreach (var k in ship.Blocks) if (k.Def.TypeId == "Decoy") dec = k;
            Check(dec != null, "decoy block placed");
            if (dec == null) return;
            Vector3D shooter = new Vector3D(100, 0, -500);
            Vector3D want = ship.LocalToWorld(dec.LocalCenter);
            Check((Decoys.AimPoint(w, ship, shooter, 1) - want).Length() < 1e-9, "aim point = working decoy");
            Check(Decoys.HasWorking(w, ship), "body has a working decoy");
            var off = new SimSettings { DecoysFunctional = false };
            var w2 = NewWorld(5, true, null, off);
            var ship2 = Spawn(w2, 1, 1, new Vector3D(100, 0, 0), null, Bd("Decoy", "SmallDecoy", 6, 0, 0), Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 3, 3, 3), Light(0, 0, 0), Light(1, 0, 0));
            Check((Decoys.AimPoint(w2, ship2, shooter, 1) - ship2.Position).Length() < 1e-9 && !Decoys.HasWorking(w2, ship2), "DecoysFunctional=false: decoys inert, aim at the body centre");
            float crit = dec.CriticalHp;
            dec.Hp = Math.Max(0f, crit - 1f);
            if (crit > 0) Check(!Decoys.HasWorking(w, ship) && (Decoys.AimPoint(w, ship, shooter, 1) - ship.Position).Length() < 1e-9, "non-functional decoy has priority 0");
            dec.Hp = dec.MaxHp;

            // target preference: pirate turrets choose the farther body that carries a working decoy
            foreach (bool decoys in new[] { true, false })
            {
                var s = new SimSettings { DecoysFunctional = decoys };
                var wp = NewWorld(8, true, new FixSink(), s);
                var pirate = GridFactory.Spawn(wp, PirateShipFactory.Build(wp.Defs), 1, 1, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null)[0];
                var near = Spawn(wp, 0, 0, new Vector3D(-100, 0, -300), null, Wall(6, 6, 2, true).Concat1Many(Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 3, 3, 3)).ToArray());
                var far = Spawn(wp, 2, 2, new Vector3D(100, 0, -400), null, Wall(6, 6, 2, true).Concat1Many(Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 3, 3, 3), Bd("Decoy", "SmallDecoy", 6, 0, 0)).ToArray());
                Invulnerable(near); Invulnerable(far);
                for (int i = 0; i < 60; i++) wp.Step();
                int onFar = 0, onNear = 0;
                foreach (var t in wp.Combat.Turrets) if (t.Block.Body == pirate) { if (ReferenceEquals(t.TargetBody, far)) onFar++; else if (ReferenceEquals(t.TargetBody, near)) onNear++; }
                if (decoys) Check(onFar == 3 && onNear == 0, "decoys on: all 3 pirate turrets target the farther body with the decoy (far " + onFar + ", near " + onNear + ")");
                else Check(onNear == 3, "decoys off: turrets take the nearest body (near " + onNear + ", far " + onFar + ")");
            }
        }

        static void TestSimFixMisc()
        {
            // unique ids on Hit events: one slug through a plank of blocks = several Hit events (one per tick) with ONE id
            var sink = new FixSink();
            var w = NewWorld(5, false, sink);
            var gun = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallMissileLauncherReload", "SmallRailgun", 0, 0, 0));
            var plank = Spawn(w, 1, 1, new Vector3D(0, 0, -150), null, Wall(1, 1, 100, false).ToArray());
            var u = gun.Weapons[0];
            for (int i = 0; i < 20 && sink.Shots < 1; i++) { u.ShootOnceRequests = 1; w.Step(); }
            Run(w, 120);
            Check(sink.Shots == 1 && sink.Hits >= 2 && sink.HitIds.Count == 1 && sink.ZeroIdHits == 0,
                "one slug: " + sink.Hits + " raw Hit events, " + sink.HitIds.Count + " distinct projectile id(s), no id-less hits");

            // metrics: Hits counts unique projectiles, HitEvents keeps the raw count; HitCountUnique=false restores the old number
            foreach (bool uniq in new[] { true, false })
            {
                var ships = new List<SeSim.Run.ShipInstance> { MkShip(0, 0), MkShip(1, 1) };
                var m = new SeSim.Run.Metrics(ships);
                m.W = new World(new SimSettings { HitCountUnique = uniq }, Defs(), 1, true);
                Action<long> hit = id => { var e = new SimEvent { Kind = EventKind.Hit, ShipA = 0, ShipB = 1, Value = 10, Id = id }; m.On(ref e); };
                var sh = new SimEvent { Kind = EventKind.Shot, ShipA = 0, ShipB = -1 }; m.On(ref sh); m.On(ref sh);
                hit(7); hit(7); hit(7); hit(8); hit(0);
                var r = m.Result.Ships[0];
                Check(r.HitEvents == 5 && r.Hits == (uniq ? 3 : 5), "metrics hits (unique=" + uniq + "): raw " + r.HitEvents + ", counted " + r.Hits + " of 2 shots (id 0 events count alone)");
            }

            // railgun capacitor from the data: small 20.0 s, large 59.2 s between shots, request = RechargeDraw
            {
                var ss = new SimSettings();
                Check(Math.Abs(ss.RailgunCapacitorMWh * 3600.0 / (ss.RailgunRechargeDrawSmallMW * ss.RailgunChargeMultiplier) - 20.0) < 1e-6, "small railgun: 0.016 MWh / (3.6 MW * 0.8) = 20 s");
                Check(Math.Abs(ss.RailgunCapacityLargeMWh * 3600.0 / (ss.RailgunRechargeDrawLargeMW * ss.RailgunChargeMultiplier) - 59.2105) < 1e-3, "large railgun: 0.5 MWh / (38 MW * 0.8) = 59.2 s");
                var sk = new FixSink();
                var wl = NewWorld(5, false, sk);
                var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up };
                _idc += 100000;
                var lg = GridFactory.Spawn(wl, Bp(true, _idc, Bd("SmallMissileLauncherReload", "LargeRailgun", 0, 0, 0), Bd("BatteryBlock", "LargeBlockBatteryBlock", 3, 0, 0)), 0, 0, at, null)[0];
                var lu = lg.Weapons[0]; var lb = lg.Batteries[0];
                lb.MaxOutputMW = 100; lb.StoredMWh = lb.MaxStoredMWh = 100;
                lu.ShootOn = true;
                var ticks = new List<long>();
                int last = 0;
                for (int i = 0; i < 60 * 150; i++) { wl.Step(); if (sk.Shots != last) { ticks.Add(i); last = sk.Shots; } }
                Check(ticks.Count >= 2 && Math.Abs((ticks[1] - ticks[0]) / 60.0 - (59.2105 + 2.0)) < 0.4, "large railgun gap between shots " + (ticks.Count >= 2 ? ((ticks[1] - ticks[0]) / 60.0).ToString("F2") : "n/a") + " s (expected 59.2 s charge + 2 s shot delay)");
            }

            // AI search range: a Midge at 3000 m sees nothing with the data range 2500, sees the dummy with VanillaSearchRange 3500
            string bpDir = Path.Combine(Root(), "out", "Blueprints");
            if (!File.Exists(Path.Combine(bpDir, "Midge", "bp.sbc"))) { Info("out/Blueprints/Midge missing: search range test skipped"); return; }
            foreach (double range in new[] { 2500.0, 3500.0 })
            {
                var s = new SimSettings { VanillaSearchRange = range, VanillaSearchIntervalS = 2 };
                var wv = NewWorld(31, true, new FixSink(), s);
                var bp = BlueprintLoader.Get(bpDir, "Midge");
                var midge = GridFactory.Spawn(wv, bp, 0, 0, new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up }, null)[0];
                var brain = new VanillaAiBrain(midge);
                wv.Brains.Add(brain);
                var blocks = Wall(10, 10, 3, true); blocks.Add(Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 5, 5, -1));
                var dummy = Spawn(wv, 1, 1, new Vector3D(0, 0, -3000), null, blocks.ToArray());
                Run(wv, 60 * 6);
                Check((brain.Target == dummy) == (range > 3000), "VanillaSearchRange " + range + ": dummy at 3000 m " + (brain.Target == dummy ? "acquired" : "not acquired"));
            }
        }

        static SeSim.Run.ShipInstance MkShip(int i, int team)
        {
            var sp = new SeSim.Run.ShipSpec { Model = "x", Team = team, Role = "x" };
            return new SeSim.Run.ShipInstance { Index = i, Spec = sp, Team = team };
        }

    }
}
