// SLOT C (weapons audit, docs/audit/weapons.md). Checks for the weapon fidelity switches: gatling cadence, burst reload, bullet range factor,
// projectile velocity, firing-block-only ignore, fixed-gun power, and the opt-ins (explosion occlusion, warhead triggers, shell ricochet,
// bullets hitting rockets). Part of SelfTestC (combat group).
using System;
using System.Collections.Generic;
using VRageMath;
using SeSim.Core;
using SeSim.Blueprint;

namespace SeSim.Combat
{
    public static partial class SelfTestC
    {
        static int ShotsOver(SimSettings s, int ticks, bool large, string sub)
        {
            var sink = new Sink();
            var w = NewWorld(11, false, sink, s);
            var at = new SpawnPlacement { Origin = Vector3D.Zero, Forward = Vector3D.Forward, Up = Vector3D.Up };
            _idc += 100000;
            var gun = GridFactory.Spawn(w, Bp(large, _idc, Bd(large ? "SmallMissileLauncher" : "SmallGatlingGun", sub, 0, 0, 0)), 0, 0, at, null)[0];
            gun.Weapons[0].ShootOn = true;
            Run(w, ticks);
            return sink.Shots;
        }

        static BlockData Front(BlockData b) { b.Min = new Vector3I(1, 1, -1); return b; }

        static void TestWeaponFidelity()
        {
            // 1. cadence: 700 rpm gatling = 6 frames per shot (600 rpm) by default, 700 rpm phase-locked with GunCadenceFrames=false
            {
                int on = ShotsOver(new SimSettings(), 600, false, "");
                int off = ShotsOver(new SimSettings { GunCadenceFrames = false }, 600, false, "");
                Check(Math.Abs(on - 100) <= 1, "gatling cadence: " + on + " shots in 10 s (6 frames each = 100)");
                Check(Math.Abs(off - 117) <= 2, "GunCadenceFrames=false: " + off + " shots in 10 s (700 rpm = 116.7)");
            }
            // 2. burst: the large launcher (120 rpm, 19-rocket burst, 4 s reload) waits ReloadTimeMs after 19 shots with infinite ammo
            {
                int on = ShotsOver(new SimSettings(), 1800, true, "LargeMissileLauncher");
                int off = ShotsOver(new SimSettings { BurstReload = false }, 1800, true, "LargeMissileLauncher");
                Check(on >= 44 && on <= 50, "large launcher burst/reload: " + on + " rockets in 30 s (expect 47)");
                Check(off >= 59 && off <= 62, "BurstReload=false: " + off + " rockets in 30 s (120 rpm = 61)");
            }
            // 3. bullet range factor in [0.8, 1.0] * 800; velocity = grid linear velocity; passes only the firing block
            {
                var w = NewWorld(5, false, new Sink());
                var ship = Spawn(w, 0, 0, Vector3D.Zero, new Vector3D(3, 0, 0), Bd("SmallGatlingGun", "", 0, 0, 0));
                ship.AngularVelocity = new Vector3D(0, 0, 4);       // spinning: the muzzle moves relative to the centre of mass only if it is off it
                ship.Weapons[0].ShootOn = true;
                double lo = 1e9, hi = 0; double dev = 0; int n = 0;
                for (int i = 0; i < 120; i++)
                {
                    w.Step();
                    var P = w.Projectiles;
                    for (int k = 0; k < P.Count; k++) if (P.Age[k] < 0.02) { lo = Math.Min(lo, P.MaxTraj[k]); hi = Math.Max(hi, P.MaxTraj[k]); }
                }
                Check(lo >= 640 - 1e-6 && hi <= 800 + 1e-6 && hi - lo > 40, "bullet range factor: shots expire between " + lo.ToString("F0") + " and " + hi.ToString("F0") + " m");
                var w2 = NewWorld(5, false, new Sink(), new SimSettings { ProjectileLinearVelocity = true });
                var sh2 = Spawn(w2, 0, 0, Vector3D.Zero, new Vector3D(3, 0, 0), Bd("SmallGatlingGun", "", 0, 0, 0), Light(1, 0, 0));
                sh2.AngularVelocity = new Vector3D(0, 0, 4);
                sh2.Weapons[0].ShootOn = true; w2.Step();
                var P2 = w2.Projectiles;
                if (P2.Count > 0)
                {
                    Vector3D rel = P2.Vel[0] - sh2.LinearVelocity; double speed = rel.Length();
                    Check(Math.Abs(speed - 800) < 0.5, "projectile velocity = grid linear velocity + 800 along the barrel (|v - vgrid| = " + speed.ToString("F2") + ")");
                }
                else Check(false, "gatling spawned no projectile");
            }
            // firing block only: a block right in front of the muzzle stops the bullet; the whole-body ignore (old) lets it pass for 0.2 s
            foreach (bool only in new[] { true, false })
            {
                var s = new SimSettings { ProjectileIgnoreFiringBlockOnly = only, BulletRangeMin = 1.0 };
                var w = NewWorld(5, false, new Sink(), s);
                var ship = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "", 0, 0, 0), Light(0, 0, -1));
                Invulnerable(ship);
                BlockInst front = null; foreach (var k in ship.Blocks) if (k.Def.Kind != "WeaponBlock") front = k;
                front.Hp = front.MaxHp = 1e9f;
                ship.Weapons[0].ShootOn = true;
                Run(w, 30);
                Check(only ? front.MaxHp - front.Hp > 0 : front.MaxHp - front.Hp == 0, only ? "own block in front of the muzzle is hit (firing block ignored only)" : "ProjectileIgnoreFiringBlockOnly=false: the shooter's own body is ignored for 0.2 s");
            }
            // 4. fixed gun needs power
            {
                var sink = new Sink();
                var w = NewWorld(5, false, sink); w.Settings.FixedGunsNeedPower = true;
                var ship = Spawn(w, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "", 0, 0, 0));
                ship.Weapons[0].ShootOn = true; Run(w, 60);
                Check(sink.Shots <= 1, "fixed gun without any power source does not fire (" + sink.Shots + " shots; the first tick runs before the first power update)");
                var sink2 = new Sink();
                var w2 = NewWorld(5, false, sink2); w2.Settings.FixedGunsNeedPower = true;
                var ship2 = Spawn(w2, 0, 0, Vector3D.Zero, null, Bd("SmallGatlingGun", "", 0, 0, 0), Bd("BatteryBlock", "SmallBlockSmallBatteryBlock", 2, 0, 0));
                ship2.Weapons[0].ShootOn = true; Run(w2, 60);
                Check(sink2.Shots > 0, "fixed gun with a battery fires (" + sink2.Shots + " shots)");
            }
        }

        static void TestWeaponOptIns()
        {
            // explosion occlusion: heavy blocks in a row behind the first soak the blast
            foreach (bool occ in new[] { false, true })
            {
                var w = NewWorld(3, false, new Sink(), new SimSettings { ExplosionOcclusion = occ });
                var row = Spawn(w, 1, 1, new Vector3D(0, 0, 0), null, Heavy(2, 0, 0), Heavy(3, 0, 0), Heavy(4, 0, 0));
                var blk = row.Blocks;
                BlockInst first = null, last = null; double bx = 1e9, ex = -1e9;
                foreach (var k in blk) { if (k.LocalCenter.X < bx) { bx = k.LocalCenter.X; first = k; } if (k.LocalCenter.X > ex) { ex = k.LocalCenter.X; last = k; } }
                float h1 = first.Hp, h3 = last.Hp;
                Explosions.Explode(w, new Vector3D(first.LocalCenter.X - 0.6, first.LocalCenter.Y, first.LocalCenter.Z), 4.0, 500.0, 0);
                float d1 = h1 - first.Hp, d3 = h3 - last.Hp;
                if (occ) Check(d1 > 0 && d3 == 0, "ExplosionOcclusion: first block takes " + d1.ToString("F0") + ", the one two blocks behind " + d3.ToString("F0"));
                else Check(d1 > 0 && d3 > 0, "no occlusion (default): first block " + d1.ToString("F0") + ", last " + d3.ToString("F0"));
            }
            // warheads: armed + bullet damage (not destroyed), armed + contact above 5 m/s
            foreach (bool on in new[] { false, true })
            {
                var w = NewWorld(3, false, new Sink(), new SimSettings { WarheadWeaponDetonate = on });
                var b = WarheadRow(w, 1, true, 1);
                var wb = b.Blocks.Find(k => k.Warhead != null);
                Damage.ApplyToBlock(w, wb, 90f, 0, DamageKind.Bullet, Vector3D.Zero);
                Run(w, 3);
                Check(wb.Warhead.Exploded == on, "WarheadWeaponDetonate=" + on + ": armed warhead hit by one bullet exploded=" + wb.Warhead.Exploded);
            }
            foreach (bool on in new[] { false, true })
            {
                var w = NewWorld(3, false, new Sink(), new SimSettings { WarheadContactDetonate = on, CollisionDamage = false });
                var a = Spawn(w, 0, 0, Vector3D.Zero, new Vector3D(0, 0, -10), Wall(3, 3, 1, true).Concat1Many(Front(ArmedWarhead(1, true))).ToArray());
                Spawn(w, 1, 1, new Vector3D(0, 0, -6), null, Wall(4, 4, 1, true).ToArray());
                Run(w, 120);
                var wh = a.Blocks.Find(k => k.Warhead != null);
                bool gone = wh == null || wh.Warhead.Exploded;
                Check(gone == on, "WarheadContactDetonate=" + on + ": armed warhead rammed at 10 m/s exploded=" + gone);
            }
            // ricochet: grazing shell on a long beam
            foreach (bool ric in new[] { false, true })
            {
                int ones = 0, trials = 20;
                for (int t = 0; t < trials; t++)
                {
                    var w = NewWorld((ulong)(50 + t), false, new Sink(), new SimSettings { ShellRicochet = ric });
                    var beam = Spawn(w, 1, 1, new Vector3D(0, 0, 0), null, Wall(2, 2, 60, true).ToArray());
                    Invulnerable(beam); foreach (var k in beam.Blocks) { k.Hp = 560f; k.MaxHp = 560f; }
                    int before = beam.Blocks.Count;
                    var ammo = w.Defs.Weapons["LargeCalibreGun"].Ammo;
                    Vector3D d = new Vector3D(-0.087, 0, -0.9962); d.Normalize();
                    Vector3D c = beam.Position;
                    w.Projectiles.Spawn(new Vector3D(c.X + 0.5 + 0.087 * 12 / 0.9962, c.Y, c.Z + 12), d * 500, ammo, null, 7);
                    Run(w, 30);
                    if (before - beam.Blocks.Count <= 1) ones++;
                }
                Check(ric ? ones >= 12 : ones <= 2, "ShellRicochet=" + ric + ": " + ones + "/" + trials + " grazing artillery shells spent on one block");
            }
            // bullets hit rockets
            foreach (bool on in new[] { false, true })
            {
                var w = NewWorld(3, false, new Sink(), new SimSettings { BulletsHitRockets = on });
                var rk = w.Defs.Weapons["SmallMissileLauncher"].Ammo; var bu = w.Defs.Weapons["GatlingGun"].Ammo;
                var shooterA = Spawn(w, 0, 0, new Vector3D(500, 0, 0), null, Light(0, 0, 0));
                var shooterB = Spawn(w, 1, 1, new Vector3D(-500, 0, 0), null, Light(0, 0, 0));
                w.Projectiles.Spawn(new Vector3D(0, 0, 0), new Vector3D(0, 0, -100), rk, shooterA, 0);
                w.Projectiles.Spawn(new Vector3D(0, 0, -60), new Vector3D(0, 0, 800), bu, shooterB, 1);
                Run(w, 6);
                Check(on ? w.Projectiles.Count == 0 : w.Projectiles.Count >= 1, "BulletsHitRockets=" + on + ": projectiles left " + w.Projectiles.Count);
            }
        }
    }
}
