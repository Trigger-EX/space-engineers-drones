// SLOT C. Weapon state machines and the combat subsystem entry points (docs/sim-design.md section 7).
// Fire control per weapon:
//   projectile guns (gatling, autocannon, turrets) and rocket launchers: one shot per 60/RateOfFireRpm seconds while ShootOn or a ShootOnce
//     request is pending (phase locked: the next due time advances by the interval, so the average rate is exact at 60 Hz).
//   railgun (slug): trigger -> ShotDelayMs charge -> shot; then ReloadTimeMs (4 s) and the capacitor (PowerSink group DefenseAdative,
//     RailgunChargeMW until RailgunCapacitorMWh is full, 20 s at full supply) must be full again. The shot empties the capacitor.
//   ammo: infinite by default; SimSettings.InfiniteAmmo=false uses floor(inventory volume / magazine volume) magazines, one is loaded at
//     spawn, a magazine swap costs ReloadTimeMs when the def has one (turrets).
// Spawn velocity (docs/sim-design.md 7.1): bullets and slugs = shooter velocity at the muzzle + direction * Speed; rockets = direction *
// MissileInitialSpeed (they do not inherit the launcher's velocity).
using System;
using System.Collections.Generic;
using VRageMath;
using SeSim.Core;
using SeSim.Blueprint;

namespace SeSim.Combat
{
    public static class CombatUtil
    {
        /// <summary>false when the block's API object says the block is switched off. Worlds without API objects: always enabled.</summary>
        public static bool BlockEnabled(BlockInst b)
        {
            var f = b.Api as SeSim.Api.SimFunctionalBlock;
            return f == null || f.EnabledValue;
        }

        /// <summary>Random unit vector inside the cone of half angle maxRad around dir.</summary>
        public static Vector3D Deviate(DetRng r, Vector3D dir, double maxRad, bool uniformInAngle)
        {
            if (maxRad <= 0) return dir;
            double cosT, sinT;
            if (uniformInAngle) { double th = r.NextDouble() * maxRad; cosT = Math.Cos(th); sinT = Math.Sin(th); }
            else
            {
                double cosA = Math.Cos(maxRad);
                cosT = 1.0 - r.NextDouble() * (1.0 - cosA);
                double s2 = 1.0 - cosT * cosT; sinT = s2 > 0 ? Math.Sqrt(s2) : 0;
            }
            double phi = r.NextDouble() * (2 * Math.PI);
            Vector3D a = Math.Abs(dir.X) < 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
            Vector3D u = Vector3D.Cross(dir, a); u *= 1.0 / u.Length();
            Vector3D v = Vector3D.Cross(dir, u);
            double cp = Math.Cos(phi) * sinT, sp = Math.Sin(phi) * sinT;
            return new Vector3D(dir.X * cosT + u.X * cp + v.X * sp, dir.Y * cosT + u.Y * cp + v.Y * sp, dir.Z * cosT + u.Z * cp + v.Z * sp);
        }

        /// <summary>First-order intercept: smallest t > 0 with |relPos + relVel*t| = speed*t. false when none exists.</summary>
        public static bool Intercept(Vector3D relPos, Vector3D relVel, double speed, out double t)
        {
            t = 0;
            double a = Vector3D.Dot(relVel, relVel) - speed * speed;
            double b = 2 * Vector3D.Dot(relPos, relVel);
            double c = Vector3D.Dot(relPos, relPos);
            if (Math.Abs(a) < 1e-9)
            {
                if (b >= 0) return false;
                t = -c / b; return t > 0;
            }
            double disc = b * b - 4 * a * c;
            if (disc < 0) return false;
            double sq = Math.Sqrt(disc);
            double t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a);
            if (t1 > t2) { double x = t1; t1 = t2; t2 = x; }
            if (t1 > 0) { t = t1; return true; }
            if (t2 > 0) { t = t2; return true; }
            return false;
        }
    }

    /// <summary>One fixed gun or turret barrel. Commanded by SimUserGun / TurretUnit; stepped in World phase 4.</summary>
    public sealed class WeaponUnit
    {
        public BlockInst Block;
        public WeaponDef Def;
        public AmmoDef Ammo;
        public Vector3D MuzzleLocal;          // grid-local: block center + block Forward * (block length/2)
        public Vector3D DirLocal;             // grid-local block Forward (turrets: overridden by TurretUnit aim each tick)
        // commands
        public bool ShootOn;                  // Shoot_On / Shoot_Off
        public int ShootOnceRequests;         // ShootOnce actions since the last tick; any number = one attempt this tick (not queued, like the game)
        // state
        public double NextShotTime;           // seconds; rate of fire 60/RateOfFireRpm
        public double ChargeReadyTime;        // railgun: trigger -> projectile after ShotDelayMs
        public bool PendingShot;              // railgun: shot scheduled at ChargeReadyTime
        public double ReloadUntil;            // after a magazine (ShotsInBurst) is emptied: ReloadTimeMs
        public int RoundsLeftInMag, MagazinesLeft;   // MagazinesLeft ignored when SimSettings.InfiniteAmmo
        public double CapacitorMWh;           // railgun: must be full to fire; recharges through PowerSink at RailgunChargeMW
        public PowerSink Sink;
        public int ShotsFired;
        public bool IsShooting { get { return ShootOn || PendingShot; } }

        // ---- slot C internals ----
        public byte Mode;                     // ProjectileSystem.ModeBullet / ModeRocket / ModeSlug
        public double IntervalS;              // 60 / rate of fire
        public double SpreadRad;              // cone half angle (tests may set 0)
        public bool UseAimDir;                // turret barrel: AimDirWorld and TurretBarrel replace the fixed muzzle
        public Vector3D AimDirWorld;
        public double TurretBarrel;           // metres from the block centre to the muzzle along AimDirWorld
        public DetRng Rng;
        public bool IsRailgun { get { return Mode == ProjectileSystem.ModeSlug; } }
        public double CapacitorFull;
        bool _ammoInit;

        public void InitAmmo(SimSettings s)
        {
            _ammoInit = true;
            if (Def == null) return;
            int cap = Def.MagazineCapacity > 0 ? Def.MagazineCapacity : 1;
            double vol = Block.Def.InventoryVolume * 1000.0;       // m3 -> litres (magazine volume is in litres)
            int mags = Def.MagazineVolume > 0 && vol > 0 ? (int)Math.Floor(vol / Def.MagazineVolume + 1e-4) : 1;
            if (mags < 1) mags = 1;
            RoundsLeftInMag = cap; MagazinesLeft = mags - 1;
        }

        /// <summary>Muzzle position and unit fire direction in world space (before spread).</summary>
        public void MuzzleWorld(out Vector3D pos, out Vector3D dir)
        {
            var body = Block.Body;
            if (UseAimDir) { dir = AimDirWorld; pos = body.LocalToWorld(Block.LocalCenter) + dir * TurretBarrel; }
            else { dir = body.DirToWorld(DirLocal); pos = body.LocalToWorld(MuzzleLocal); }
        }

        /// <summary>Fire if commanded and ready; spawns projectiles through World.Projectiles with muzzle position/velocity:
        /// direction = DirLocal rotated to world + spread (uniform in a cone of DeviateShotAngleDeg); velocity =
        /// shooter velocity at muzzle + dir*speed for Projectile ammo; Missile ammo (rockets): dir*MissileInitialSpeed WITHOUT
        /// ship velocity (HANDOFF/Keen thread: rockets do not inherit); railgun slug (Missile, acceleration 0, speed 1000):
        /// inherits ship velocity [I]. Emits EventKind.Shot.</summary>
        public void Update(World w, double dt)
        {
            var b = Block;
            if (b.Destroyed || b.Body == null || w.Projectiles == null) return;
            var s = w.Settings;
            if (!_ammoInit) InitAmmo(s);
            double t = w.Time;
            bool operational = b.IsFunctional && CombatUtil.BlockEnabled(b);
            // ShootOnce is "try one shot now": the game does not queue it. API requests made since the last tick (scripts run in
            // phase 1) are valid for this tick only; any number of them means one attempt.
            bool once = ShootOnceRequests > 0;
            ShootOnceRequests = 0;

            if (Mode == ProjectileSystem.ModeSlug) UpdateCapacitor(s, dt, operational);
            if (!operational) { PendingShot = false; return; }

            if (Mode == ProjectileSystem.ModeSlug)
            {
                if (PendingShot)
                {
                    if (t + 1e-9 >= ChargeReadyTime) FireSlug(w, t);
                    return;
                }
                if (!(ShootOn || once)) return;
                if (t + 1e-9 < ReloadUntil || t + 1e-9 < NextShotTime) return;
                if (CapacitorMWh < CapacitorFull * 0.9999) return;
                if (!TakeRound(s, t, false)) return;
                double delay = Def != null ? Def.ShotDelayMs * 0.001 : 0;
                if (delay <= 0) { ChargeReadyTime = t; FireSlug(w, t); }
                else { PendingShot = true; ChargeReadyTime = t + delay; }
                return;
            }

            if (!(ShootOn || once)) return;
            if (t + 1e-9 < NextShotTime || t + 1e-9 < ReloadUntil) return;
            if (!TakeRound(s, t, true)) return;
            FireProjectile(w, t);
            // phase-locked cadence while firing continuously; restart at 'now' after an idle gap
            if (t - NextShotTime > IntervalS) NextShotTime = t + IntervalS; else NextShotTime += IntervalS;
        }

        // consumes one round; false when none left or a magazine swap delay started
        bool TakeRound(SimSettings s, double t, bool burstReload)
        {
            if (s.InfiniteAmmo) return true;
            if (RoundsLeftInMag <= 0)
            {
                if (MagazinesLeft <= 0) return false;
                MagazinesLeft--; RoundsLeftInMag = Def != null && Def.MagazineCapacity > 0 ? Def.MagazineCapacity : 1;
                if (burstReload && Def != null && Def.ReloadTimeMs > 0) { ReloadUntil = t + Def.ReloadTimeMs * 0.001; return false; }
            }
            RoundsLeftInMag--;
            return true;
        }

        /// <summary>Capacity of this railgun's capacitor in MWh (SimSettings.RailgunCapacitorFromData: EntityCapacitorComponent Capacity,
        /// small 0.016 / large 0.5; else the old 3 x small for large).</summary>
        public static double CapacityOf(SimSettings s, bool large)
        {
            if (!large) return s.RailgunCapacitorMWh;
            return s.RailgunCapacitorFromData ? s.RailgunCapacityLargeMWh : s.RailgunCapacitorMWh * 3.0;
        }

        void UpdateCapacitor(SimSettings s, double dt, bool operational)
        {
            bool large = Block.Def.Large;
            if (CapacitorFull <= 0) CapacitorFull = CapacityOf(s, large);
            if (Sink == null) { CapacitorMWh = CapacitorFull; return; }
            if (operational && CapacitorMWh < CapacitorFull)
            {
                // data model: the sink requests RechargeDraw and only ChargeMultiplier of what it gets is stored; old model: request = stored rate
                double draw = s.RailgunCapacitorFromData ? (large ? s.RailgunRechargeDrawLargeMW : s.RailgunRechargeDrawSmallMW) : s.RailgunChargeMW;
                double mult = s.RailgunCapacitorFromData ? s.RailgunChargeMultiplier : 1.0;
                Sink.RequestMW = draw;
                CapacitorMWh += Sink.SuppliedRatio * draw * mult * dt / 3600.0;       // ratio of the previous power update
                if (CapacitorMWh > CapacitorFull) CapacitorMWh = CapacitorFull;
            }
            else Sink.RequestMW = 0;
        }

        void FireSlug(World w, double t)
        {
            PendingShot = false;
            Emit(w, t);
            CapacitorMWh = 0;
            double rel = Def != null ? Def.ReloadTimeMs * 0.001 : 0;
            ReloadUntil = t + rel;
            NextShotTime = t + IntervalS;
        }

        void FireProjectile(World w, double t) { Emit(w, t); }

        void Emit(World w, double t)
        {
            var body = Block.Body;
            Vector3D pos, dir;
            MuzzleWorld(out pos, out dir);
            dir = CombatUtil.Deviate(Rng, dir, SpreadRad, w.Settings.SpreadUniformInAngle);
            Vector3D vel;
            if (Mode == ProjectileSystem.ModeRocket) vel = dir * Ammo.MissileInitialSpeed;
            else vel = body.VelocityAtWorld(pos) + dir * (Ammo.Speed > 0 ? Ammo.Speed : Ammo.MissileInitialSpeed);
            w.Projectiles.Spawn(pos, vel, Ammo, body, body.ShipIndex);
            ShotsFired++;
            var e = new SimEvent { Kind = EventKind.Shot, Tick = w.Tick, ShipA = body.ShipIndex, ShipB = -1, Value = Mode, Position = pos };
            w.Events.On(ref e);
        }
    }

    /// <summary>Owns all combat units of a World; created by World ctor.</summary>
    public sealed class CombatSystem
    {
        public readonly World World;
        public readonly List<WeaponUnit> Weapons = new List<WeaponUnit>();
        public readonly List<TurretUnit> Turrets = new List<TurretUnit>();
        public readonly List<WarheadUnit> Warheads = new List<WarheadUnit>();
        // scratch buffers shared by Explosions (no allocation per blast)
        public readonly List<Body> ScratchBodies = new List<Body>();
        public readonly List<BlockInst> ExpBlocks = new List<BlockInst>();
        public readonly List<float> ExpDamage = new List<float>();

        public CombatSystem(World w) { World = w; }

        /// <summary>Called by GridFactory for every block: creates WeaponUnit/TurretUnit/WarheadUnit when Def says so.</summary>
        public void AttachUnits(BlockInst b)
        {
            var w = World; var d = b.Def; var body = b.Body;
            if (b.Weapon != null || b.Turret != null || b.Warhead != null) return;     // already attached (re-attach after a body change)
            switch (d.Kind)
            {
                case "WeaponBlock":
                case "LargeTurretBase":
                    if (d.Weapon == null) return;
                    var u = MakeWeapon(b);
                    b.Weapon = u; Weapons.Add(u);
                    if (d.Kind == "LargeTurretBase")
                    {
                        float range = 800f;
                        if (w.Settings.TurretRangeFromAmmo && d.Weapon.Ammo != null && d.Weapon.Ammo.MaxTrajectory > 0) range = d.Weapon.Ammo.MaxTrajectory;
                        var t = new TurretUnit(b, u, range);
                        b.Turret = t; Turrets.Add(t);
                    }
                    break;
                case "TurretControlBlock":
                {
                    var t = new TurretUnit(b, null, d.TurretControlRange > 0 ? d.TurretControlRange : 600f);
                    b.Turret = t; Turrets.Add(t);
                    break;
                }
                case "Warhead":
                {
                    var wu = new WarheadUnit { Block = b };
                    var src = b.Src;
                    wu.Armed = src != null && src.PropBool("IsArmed", false);
                    double ms = src != null ? src.PropNum("CountdownMs", 10000) : 10000;
                    wu.DetonationTime = ms > 0 ? ms * 0.001 : 10;
                    wu.CountdownLeft = wu.DetonationTime;
                    b.Warhead = wu; Warheads.Add(wu);
                    break;
                }
            }
        }

        WeaponUnit MakeWeapon(BlockInst b)
        {
            var w = World; var d = b.Def; var s = w.Settings;
            var u = new WeaponUnit { Block = b, Def = d.Weapon, Ammo = d.Weapon.Ammo };
            MatrixD rot = OrientMath.Rotation(b.Orientation);
            u.DirLocal = rot.Forward;
            double cell = b.Body != null ? b.Body.CellSize : (d.Large ? w.Defs.LargeCellSize : w.Defs.SmallCellSize);
            double halfLen = d.SizeCells.Z * cell * 0.5;
            u.MuzzleLocal = b.LocalCenter + u.DirLocal * halfLen;
            u.TurretBarrel = Math.Max(d.SizeCells.X, Math.Max(d.SizeCells.Y, d.SizeCells.Z)) * cell * 0.5;
            u.Mode = ProjectileSystem.ModeOf(u.Ammo);
            u.IntervalS = d.Weapon.RateOfFireRpm > 0 ? 60.0 / d.Weapon.RateOfFireRpm : 1.0;
            u.SpreadRad = d.Weapon.DeviateShotAngleDeg * Math.PI / 180.0;
            ulong salt = b.EntityId != 0 ? (ulong)b.EntityId : (ulong)((b.Body != null ? b.Body.Id : 0) * 100003L + b.Index + 17);
            u.Rng = new DetRng(DetRng.Mix(DetRng.Mix(w.Seed, 0xC0DE), salt));
            u.CapacitorFull = WeaponUnit.CapacityOf(s, d.Large);   // small 20 s, large 59 s with the data numbers (EntityComponents.sbc)
            if (u.Mode == ProjectileSystem.ModeSlug)
            {
                double init = b.Src != null ? b.Src.PropNum("StoredPower", double.NaN) : double.NaN;
                u.CapacitorMWh = double.IsNaN(init) ? u.CapacitorFull * Math.Max(0, Math.Min(1, s.RailgunInitialCharge)) : Math.Min(init, u.CapacitorFull);
                u.Sink = new PowerSink { Block = b, Group = d.PowerGroup ?? "DefenseAdative", RequestMW = 0 };
            }
            u.InitAmmo(s);
            return u;
        }

        /// <summary>World phase 3: turret target refresh + aim; phase 4: weapons fire; warhead countdowns.</summary>
        public void UpdateTurrets(double dt)
        {
            int n = Turrets.Count;
            if (n == 0) return;
            int period = (int)World.Settings.TurretTargetRefreshTicks; if (period < 1) period = 1;
            long tick = World.Tick;
            for (int i = 0; i < Turrets.Count; i++)
            {
                var t = Turrets[i];
                if (t.Block.Destroyed) { Turrets.RemoveAt(i--); continue; }
                bool refresh = ((tick + (t.Block.EntityId % period) + period) % period) == 0;
                t.Update(World, dt, refresh);
            }
        }

        public void UpdateWeapons(double dt)
        {
            for (int i = 0; i < Weapons.Count; i++)
            {
                var u = Weapons[i];
                if (u.Block.Destroyed) { Weapons.RemoveAt(i--); continue; }
                u.Update(World, dt);
            }
        }

        public void UpdateWarheads(double dt)
        {
            for (int i = 0; i < Warheads.Count; i++)
            {
                var u = Warheads[i];
                if (u.Exploded) { Warheads.RemoveAt(i--); continue; }
                if (u.Block.Destroyed && !u.DetonateQueued) { Warheads.RemoveAt(i--); continue; }
                u.Update(World, dt);
                if (u.Exploded) { Warheads.RemoveAt(i--); }
            }
        }
    }
}
