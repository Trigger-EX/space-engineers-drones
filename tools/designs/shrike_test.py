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
NO_LITE = ('Shrike Test', 'Shrike Test Rocket', 'Shrike Staff Test')
DOCS = {
    'Shrike Test': 'TEST SHIP (not for the game): Hornet hull with a cockpit and a Remote Control, one Programmable Block [Helm] running ShrikeAI (MANUAL mode, assist, '
                   'dead-man heartbeat), a Deadman timer and the groups it switches off. For the simulator\'s scripted pilot.',
    'Shrike Staff Test': 'TEST SHIP (not for the game): Shrike Test split in two Programmable Blocks, [Helm] (ShrikeAI) and [Staff] (StaffAI, control_arch M2): IGC heartbeat watch '
                         '(the Staff releases every override when the Helm falls silent), mothership link, displays; plus an Event Controller (cockpit occupied, PLACEHOLDER event id) that stops / starts '
                         'the Deadman timer and a cockpit toolbar that Runs both PBs (argument-less menus).',
    'Shrike Test Rocket': 'TEST SHIP (not for the game): Shrike Test with a gatling [B1] and a SmallMissileLauncher [B2] (BATTERY-lite: two classes).',
}

def _drop(bp, pred):
    """remove blocks and the keep-clear zones they reserved (exhaust, camera view)"""
    for b in [b for b in bp.blocks if pred(b)]:
        bp.blocks.remove(b)
        for c in [c for c, why in bp.reserve.items() if why.endswith(' of ' + (b['name'] or b['sub']))]: del bp.reserve[c]

def _shrike_test(name, weapon_b2=None, staff=False):
    bp = hornet_frame(name, '', 'MyObjectBuilder_SmallGatlingGun', script_text('ShrikeAI.cs'), ai=True, sensor=True, merge=False)
    bp.legacy_shapes = False                       # sample-derived shapes: cockpit / timer / PB (enabled)
    # the cockpit stands on the rear deck (the antenna's place); the Hornet's cameras, dive thrusters, PB and 4 batteries stay where they are
    _drop(bp, lambda b: 'Antenna' in (b['name'] or ''))
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

def shrike_test_rocket(lite=False):
    return _shrike_test('Shrike Test Rocket', ('MyObjectBuilder_SmallMissileLauncher', ''))

MODELS['Shrike Test'] = shrike_test
MODELS['Shrike Test Rocket'] = shrike_test_rocket
MODELS['Shrike Staff Test'] = shrike_staff_test

if __name__ == '__main__':
    for n, f in MODELS.items():
        bp = f(False); bp.validate(); print('ok', n, len(bp.blocks), 'blocks', bp.notes)
