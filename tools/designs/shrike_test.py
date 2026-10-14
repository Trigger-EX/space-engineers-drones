"""Shrike Test: sim/generator test ships for scripts/ShrikeAI.cs (control_arch M1, kind 'test': built into out/Blueprints, never packaged).

  Shrike Test        the Hornet hull (3x2x4 core, two fixed gatlings, 12 ion thrusters, cameras, sensor, AI blocks) with a SmallBlockCockpit on the deck
                     and the Remote Control kept: ONE Programmable Block '[Helm] Shrike Test PB' running ShrikeAI, a Timer 'Shrike Test Deadman'
                     (5 s, Silent; toolbar: groups Thrusters and Weapons OnOff_Off, the hard dead-man of control_arch s2.6) and the block groups the
                     timer refers to. The two dive thrusters moved behind the cockpit, so the hull keeps its six-axis thrust. No Event
                     Controller (its event numbers are placeholders, K4): in MANUAL the script stops the timer itself, which is what the sim tests.
  Shrike Test Rocket   the same, but the right-hand gun is a SmallMissileLauncher tagged [B2], the left gatling [B1]: exercises BATTERY-lite.
Naming rules the real Shrike hull must keep (see ShrikeAI.cs header): PB name starts with '[Helm]'; a Remote Control (the script's reference) AND a flight
seat (IMyShipController that CanControlShip, not the RC); a Timer whose name contains 'Deadman'; fixed guns tagged [B1]/[B2] (optional).
Module contract: MODELS = {name: callable(lite)}, NO_LITE = names without a Lite variant, DOCS.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import sebp_toolbar as TB
from core import hornet_frame

MODELS = {}
NO_LITE = ('Shrike Test', 'Shrike Test Rocket', 'Shrike Staff Test', 'Shrike Wing Test', 'Shrike Multi Test', 'Shrike Basilisk Test')
DOCS = {
    'Shrike Test': 'TEST SHIP (not for the game): Hornet hull with a cockpit and a Remote Control, one Programmable Block [Helm] running ShrikeAI (MANUAL mode, assist, '
                   'dead-man heartbeat), a Deadman timer and the groups it switches off. For the simulator\'s scripted pilot.',
    'Shrike Staff Test': 'TEST SHIP (not for the game): Shrike Test split in two Programmable Blocks, [Helm] (ShrikeAI) and [Staff] (StaffAI, control_arch M2): IGC heartbeat watch '
                         '(the Staff releases every override when the Helm falls silent), mothership link, displays; plus an Event Controller (cockpit occupied, PLACEHOLDER event id) that stops / starts '
                         'the Deadman timer and a cockpit toolbar that Runs both PBs (argument-less menus).',
    'Shrike Wing Test': 'TEST SHIP (not for the game; control_arch M4): Shrike Staff Test with its Radio Antenna kept (IGC between grids needs one; the drones have theirs), for the '
                        'Staff wing tests: follow-test --owner ship, wing hunt / rtb / satellite (selftest m4). Throwaway.',
    'Shrike Multi Test': 'TEST SHIP (not for the game; control_arch M3): Shrike Test with THREE fixed batteries: gatling [B1] (left flank), SmallMissileLauncher [B2] (right flank) and a SmallRailgun [B3] on the roof, '
                         'to exercise BATTERY v1.0 (primary choice, standoff, rail confirm state machine). Throwaway.',
    'Shrike Basilisk Test': 'TEST SHIP (not for the game; control_arch M3): the Basilisk hull (large grid, 4 LargeMissileLauncher tagged [B2], gatling turret on the roof) running ShrikeAI instead of BasiliskAI: '
                            'turret-only mode (a turret ship), large-grid MaxTurn calibration, rocket battery. Throwaway.',
    'Shrike Test Rocket': 'TEST SHIP (not for the game): Shrike Test with a gatling [B1] and a SmallMissileLauncher [B2] (BATTERY-lite: two classes).',
}

def _drop(bp, pred):
    """remove blocks and the keep-clear zones they reserved (exhaust, camera view)"""
    for b in [b for b in bp.blocks if pred(b)]:
        bp.blocks.remove(b)
        for c in [c for c, why in bp.reserve.items() if why.endswith(' of ' + (b['name'] or b['sub']))]: del bp.reserve[c]

def _shrike_test(name, weapon_b2=None, staff=False, rail=False, antenna=False):
    bp = hornet_frame(name, '', 'MyObjectBuilder_SmallGatlingGun', script_text('ShrikeAI.cs'), ai=True, sensor=True, merge=False)
    bp.legacy_shapes = False                       # sample-derived shapes: cockpit / timer / PB (enabled)
    # the cockpit stands on the rear deck (the antenna's place); the Hornet's cameras, dive thrusters, PB and 4 batteries stay where they are
    _drop(bp, lambda b: 'Antenna' in (b['name'] or ''))
    if antenna: ant(bp, (2, 2, 0), 'Up', 'Backward', f'{name} Antenna', 2500)    # beside the seat (IGC between grids needs an antenna)
    pb = [b for b in bp.blocks if b['name'] == f'{name} PB'][0]
    pb['name'] = f'[Helm] {name} PB'
    seat = bp.add('MyObjectBuilder_Cockpit', 'OpenCockpitSmall', (-1, 2, 0), 'Forward', 'Up', f'{name} Seat', kind='ctrl')
    timer = bp.add('MyObjectBuilder_TimerBlock', 'TimerBlockSmall', (0, 0, 1), 'Forward', 'Up', f'{name} Deadman', kind='ctrl')
    bp.group('Thrusters', [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Thrust'])
    guns = [b for b in bp.blocks if b['kind'] == 'gun']
    guns.sort(key=lambda b: b['min'][0])
    if weapon_b2:
        r = guns[-1]; bp.blocks.remove(r)
        for c in [c for c, why in bp.reserve.items() if why.endswith(' of ' + r['name'])]: del bp.reserve[c]
        g = bp.add(weapon_b2[0], weapon_b2[1], tuple(r['min']), 'Forward', 'Up', f'{name} Launcher [B2]', GUN, kind='gun')
        bp.keep_clear(g, 6, 'muzzle')
        guns = [guns[0], g]
    guns[0]['name'] = guns[0]['name'] + ' [B1]'
    if rail:
        # a small railgun (1x2x8) on the roof along the centre line: the roof camera makes room, the muzzle ends 4 cells ahead of the nose
        _drop(bp, lambda b: (b['name'] or '').endswith('Camera Up'))
        r3 = bp.add('MyObjectBuilder_SmallMissileLauncherReload', 'SmallRailgun', (0, 2, -8), 'Forward', 'Up', f'{name} Railgun [B3]', GUN, kind='gun', state={'capacitor': 'full'})
        bp.keep_clear(r3, 8, 'muzzle')
        guns.append(r3)
        # the railgun's capacitor recharges at 3.6 MW (EntityComponents.sbc): six small reactors with uranium, 0.5 MW each (the 4 small batteries give 0.8 MW)
        from sebp_inv import Item
        for x in (-2, 2):
            for z in (-1, 0, 1):
                bp.add('MyObjectBuilder_Reactor', 'SmallBlockSmallGenerator', (x, -1, z), 'Forward', 'Up', f'{name} Reactor {"L" if x < 0 else "R"}{z + 2}', kind='power', items=[Item.ingot('Uranium', 5)])
    bp.group('Weapons', guns)
    deadman_kit(bp, name, seat, timer=timer, ec_cell=(1, 1, 1) if staff else None, experimental=staff)     # Timer D (5 s, Silent, cuts the groups) [+ the cockpit EC on the split ship]
    if staff:
        # the second PB in the free cell behind the core (its front face goes into the armour at z = 0)
        sp = bp.add('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', (-1, 1, 1), 'Forward', 'Up', f'[Staff] {name} PB', extra=pb_extra(script_text('StaffAI.cs')), kind='ctrl')
        bp.set_toolbars(seat, toolbar=[TB.run(pb), TB.run(sp)])                              # Run (no argument) = the menus of the two PBs
    return finalize(bp)

def shrike_test(lite=False):
    return _shrike_test('Shrike Test')

def shrike_staff_test(lite=False):
    return _shrike_test('Shrike Staff Test', staff=True)

def shrike_wing_test(lite=False):
    return _shrike_test('Shrike Wing Test', staff=True, antenna=True)

def shrike_multi_test(lite=False):
    return _shrike_test('Shrike Multi Test', ('MyObjectBuilder_SmallMissileLauncher', ''), rail=True)

def shrike_basilisk_test(lite=False):
    """The Basilisk hull (large grid, 4 launchers, gatling turret on the roof) with ShrikeAI in a '[Helm]' PB instead of BasiliskAI; launchers tagged [B2]."""
    import extra as X
    bp = X.basilisk(lite=False, staff=False)
    name = 'Shrike Basilisk Test'
    pb = [b for b in bp.blocks if (b['name'] or '').endswith(' PB')][0]
    pb['name'] = f'[Helm] {name} PB'
    pb['extra'] = pb_extra(script_text('ShrikeAI.cs'))
    pb['custom_data'] = '[Shrike]\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=GUARD\nStandoff Range=600\nEvasive Jinking=false\nOrbit Speed=10\nCombat Speed=40\nMin Range=150\n'
    for b in bp.blocks:
        if 'Launcher' in (b['name'] or ''): b['name'] = b['name'] + ' [B2]'
    bp.name = name
    return bp

def shrike_test_rocket(lite=False):
    return _shrike_test('Shrike Test Rocket', ('MyObjectBuilder_SmallMissileLauncher', ''))

MODELS['Shrike Test'] = shrike_test
MODELS['Shrike Test Rocket'] = shrike_test_rocket
MODELS['Shrike Staff Test'] = shrike_staff_test
MODELS['Shrike Wing Test'] = shrike_wing_test
MODELS['Shrike Multi Test'] = shrike_multi_test
MODELS['Shrike Basilisk Test'] = shrike_basilisk_test

if __name__ == '__main__':
    for n, f in MODELS.items():
        bp = f(False); bp.validate(); print('ok', n, len(bp.blocks), 'blocks', bp.notes)
