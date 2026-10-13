"""Paste probes for the generator extension (inventories, sample-derived shapes, toolbars, block groups, docked grids).

  python3 -I tools/probe_ext.py out/probes_ext

Like probe_blueprints.py: tiny blueprints, one feature each, so an in-game paste failure (or a wrong result after the paste) can be pinned
on one thing.  Order = risk, highest first.  After pasting each one, look at what the folder name says to look at:

  E01  small gatling gun: inventory shows 4 x NATO 25x184mm box
  E02  small railgun: 3 slugs in the inventory and the capacitor charged
  E03  small reactor: 5 kg uranium, reactor shows fuel
  E04  small cargo container with components and a magazine
  E05  timer + button panel with toolbar slots (group "Guns", terminal block action); open the timer's toolbar in the terminal
  E06  fighter cockpit with a populated toolbar (group + weapon slot)
  E07  small hydrogen engine full + hydrogen tank full
  E10  large gatling turret with 24 boxes      E11  large railgun, charged, 6 slugs     E12  artillery turret with 6 shells
  E13  large reactor (small generator) with uranium                  E14  large bridge cockpit seat
  E20  large ship + small pod docked on connectors, UNLOCKED (the tested form): both grids appear, connector shows "ready to lock"?
  E21  the same, LOCKED in the Venture style (UNVERIFIED): does the paste come back connected?
"""
import sys, os, zlib, itertools
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sebp, sebp_toolbar as TB
from sebp import Blueprint, Item
import probe_blueprints as P

M = 'MyObjectBuilder_'

def small(name, items):
    bp = Blueprint(name, 'Small', seed=zlib.crc32(name.encode()) & 0xffff)
    for x in range(4):
        for z in range(4): bp.armor(x, 0, z)
    for xtype, sub, kw in items:
        P.place(bp, xtype, sub, kw.pop('name', f'{name} {xtype}'), **kw)
    bp.validate()
    return bp

def large(name, items):
    bp = Blueprint(name, 'Large', seed=zlib.crc32(name.encode()) & 0xffff)
    for x in range(6):
        for z in range(6): bp.armor(x, 0, z, sub='LargeBlockArmorBlock')
    for xtype, sub, kw in items:
        P.place(bp, xtype, sub, kw.pop('name', f'{name} {xtype}'), **kw)
    bp.validate()
    return bp

def kw(**k): return k

def timer_probe():
    bp = small('E05_Timer_Button_Toolbars', [('SmallGatlingGun', '', kw(name='Gun', items=[Item.ammo('NATO_25x184mm', 2)]))])
    gun = bp.blocks[-1]
    bp.group('Guns', [gun])
    P.place(bp, 'TimerBlock', 'TimerBlockSmall', 'Timer', toolbar=[TB.group('Guns', 'OnOff_Off'), TB.block(gun, 'OnOff_On')])
    timer = bp.blocks[-1]
    P.place(bp, 'ButtonPanel', 'ButtonPanelSmall', 'Panel', toolbar=[TB.block(timer, 'TriggerNow'), TB.group('Guns', 'Shoot')])
    bp.validate()
    return bp

def cockpit_probe():
    bp = small('E06_Cockpit_Toolbar', [('SmallGatlingGun', '', kw(name='Gun'))])
    bp.group('Guns', [bp.blocks[-1]])
    P.place(bp, 'Cockpit', 'DBSmallBlockFighterCockpit', 'Cockpit',
            toolbar=[TB.group('Guns', 'OnOff'), TB.weapon('SmallGatlingGun', '')], settings={'IsMainCockpit': True})
    bp.validate()
    return bp

def docked_probe(lock):
    ship = Blueprint('E21_Docked_Pod_Locked' if lock else 'E20_Docked_Pod_Unlocked', 'Large', seed=77 + lock)
    for x in range(5):
        for z in range(5): ship.armor(x, 0, z, sub='LargeBlockArmorBlock')
    P.place(ship, 'ShipConnector', 'Connector', 'Ship dock')
    sc = ship.blocks[-1]
    pod = Blueprint('Pod', 'Small', seed=78)
    for x in range(4):
        for z in range(4): pod.armor(x, 0, z)
    P.place(pod, 'ShipConnector', 'ConnectorSmall', 'Pod dock')
    pc = pod.blocks[-1]
    ship.validate(); pod.validate()
    ship.dock(pod, sc, pc, lock=lock)
    return ship

def build_all():
    out = [
        small('E01_Gatling_4_NATO', [('SmallGatlingGun', '', kw(items=[Item.ammo('NATO_25x184mm', 4)]))]),
        small('E02_Railgun_Charged', [('SmallMissileLauncherReload', 'SmallRailgun', kw(items=[Item.ammo('SmallRailgunAmmo', 3)], state={'capacitor': 'full'}))]),
        small('E03_Reactor_5kg_Uranium', [('Reactor', 'SmallBlockSmallGenerator', kw(items=[Item.ingot('Uranium', 5)]))]),
        small('E04_Cargo_Items', [('CargoContainer', 'SmallBlockSmallContainer', kw(items=[Item.component('SteelPlate', 20), Item.ammo('NATO_25x184mm', 1)]))]),
        timer_probe(), cockpit_probe(),
        small('E07_HydrogenEngine_Tank_Full', [('HydrogenEngine', 'SmallHydrogenEngine', kw(state={'fuel': 'full'})),
                                               ('OxygenTank', 'SmallHydrogenTankSmall', kw(state={'filled': 'full'}))]),
        large('E10_LargeGatling_24_NATO', [('LargeGatlingTurret', '', kw(items=[Item.ammo('NATO_25x184mm', 24)]))]),
        large('E11_LargeRailgun_Charged', [('SmallMissileLauncherReload', 'LargeRailgun', kw(items=[Item.ammo('LargeRailgunAmmo', 6)], state={'capacitor': 'full'}))]),
        large('E12_Artillery_Turret_6', [('LargeMissileTurret', 'LargeCalibreTurret', kw(items=[Item.ammo('LargeCalibreAmmo', 6)]))]),
        large('E13_LargeReactor_Uranium', [('Reactor', 'LargeBlockSmallGenerator', kw(items=[Item.ingot('Uranium', 50)]))]),
        large('E14_Bridge_Cockpit_Seat', [('Cockpit', 'LargeBlockCockpitSeat', kw())]),
        docked_probe(False), docked_probe(True),
    ]
    return out

def main(outdir):
    palette = {'armor': (92, 100, 112), 'ai': (60, 150, 170), 'gun': (50, 52, 58)}
    for bp in build_all():
        d = os.path.join(outdir, bp.name)
        bp.write(d, version='probe-ext')
        bp.thumb(os.path.join(d, 'thumb.png'), bp.name, 'extension probe', palette)
        extra = f', {len(bp.subgrids)} sub-grid(s)' if bp.subgrids else ''
        print(f'{bp.name}: {len(bp.blocks)} blocks{extra} -> {d}   {sorted({c for _, c in bp.shape_summary().values()})}')

if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'out/probes_ext')
