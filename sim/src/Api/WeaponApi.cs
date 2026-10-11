// SLOT B. Fixed guns: API surface only; firing logic is Combat.WeaponUnit (slot C).
// Actions: "Shoot_On" -> Unit.ShootOn=true, "Shoot_Off" -> false, "ShootOnce" -> Unit.ShootOnceRequests++, "Shoot" toggles,
// plus OnOff actions. Property "Shoot" (bool). IsShooting = Unit.IsShooting. A gun that is not Enabled/functional ignores
// ShootOnce and drops ShootOn when switched off. Slot C should additionally require `Block.Api.IsWorking` before firing.
// Class by blueprint type: SmallGatlingGun -> SimGatlingGun (IMySmallGatlingGun; also the autocannon subtype),
// SmallMissileLauncher -> SimMissileLauncher (IMySmallMissileLauncher), SmallMissileLauncherReload -> SimRailgun
// (IMySmallMissileLauncherReload: small railgun and reloadable rocket launcher).
// Inventory (GetInventory(0)): MaxVolume = Def.InventoryVolume (blueprint inventory volume, m3). CurrentVolume = loaded ammo:
// 0 with SimSettings.InfiniteAmmo (creative world: blueprint inventories are empty and guns need no ammo, research.md 4),
// else (MagazinesLeft*capacity + RoundsLeftInMag) * magazine_volume/capacity (magazine volume in litres -> /1000 m3).
using System;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using SeSim.Core;
using SeSim.Combat;

namespace SeSim.Api
{
    public abstract class SimUserGun : SimFunctionalBlock, IMyUserControllableGun
    {
        readonly SimInventory _inv = new SimInventory();
        public WeaponUnit Unit { get { return Inst.Weapon; } }
        protected SimUserGun(BlockInst inst, World world) : base(inst, world) { }
        public bool IsShooting { get { var u = Unit; return u != null && u.IsShooting; } }

        protected override void OnEnabledChanged() { var u = Unit; if (!EnabledValue && u != null) u.ShootOn = false; }

        public override int InventoryCount { get { return Inst.Def.InventoryVolume > 0 ? 1 : 0; } }
        public override IMyInventory GetInventory(int i)
        {
            if (i != 0 || Inst.Def.InventoryVolume <= 0) return null;
            _inv.MaxVolumeM3 = Inst.Def.InventoryVolume;
            double cur = 0;
            var u = Unit;
            if (u != null && !World.Settings.InfiniteAmmo && u.Def != null && u.Def.MagazineCapacity > 0)
            {
                double rounds = (double)u.MagazinesLeft * u.Def.MagazineCapacity + u.RoundsLeftInMag;
                cur = rounds * (u.Def.MagazineVolume / 1000.0 / u.Def.MagazineCapacity);
            }
            _inv.CurrentVolumeM3 = cur > _inv.MaxVolumeM3 ? _inv.MaxVolumeM3 : cur;
            return _inv;
        }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            AddBool(r, "Shoot", b => { var u = ((SimUserGun)b).Unit; return u != null && u.ShootOn; },
                (b, v) => { var g = (SimUserGun)b; if (g.Unit != null) g.Unit.ShootOn = v && g.EnabledValue; }, false);
            r.Add(new SimAction("Shoot_On", b => { var g = (SimUserGun)b; if (g.Unit != null) g.Unit.ShootOn = g.EnabledValue; }));
            r.Add(new SimAction("Shoot_Off", b => { var g = (SimUserGun)b; if (g.Unit != null) g.Unit.ShootOn = false; }));
            r.Add(new SimAction("Shoot", b => { var g = (SimUserGun)b; if (g.Unit != null) g.Unit.ShootOn = !g.Unit.ShootOn && g.EnabledValue; }));
            r.Add(new SimAction("ShootOnce", b => { var g = (SimUserGun)b; if (g.Unit != null && g.EnabledValue && g.Inst.IsFunctional) g.Unit.ShootOnceRequests++; }));
        }
    }
    public sealed class SimGatlingGun : SimUserGun, IMySmallGatlingGun { public SimGatlingGun(BlockInst i, World w) : base(i, w) { } }
    public sealed class SimMissileLauncher : SimUserGun, IMySmallMissileLauncher { public SimMissileLauncher(BlockInst i, World w) : base(i, w) { } }
    public sealed class SimRailgun : SimUserGun, IMySmallMissileLauncherReload { public SimRailgun(BlockInst i, World w) : base(i, w) { } }
}
