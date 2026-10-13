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
NO_LITE = ('Shrike Test', 'Shrike Test Rocket')
DOCS = {
    'Shrike Test': 'TEST SHIP (not for the game): Hornet hull with a cockpit and a Remote Control, one Programmable Block [Helm] running ShrikeAI (MANUAL mode, assist, '
                   'dead-man heartbeat), a Deadman timer and the groups it switches off. For the simulator\'s scripted pilot.',
    'Shrike Test Rocket': 'TEST SHIP (not for the game): Shrike Test with a gatling [B1] and a SmallMissileLauncher [B2] (BATTERY-lite: two classes).',
}

def _drop(bp, pred):
    """remove blocks and the keep-clear zones they reserved (exhaust, camera view)"""
    for b in [b for b in bp.blocks if pred(b)]:
        bp.blocks.remove(b)
        for c in [c for c, why in bp.reserve.items() if why.endswith(' of ' + (b['name'] or b['sub']))]: del bp.reserve[c]

def _shrike_test(name, weapon_b2=None):
    bp = hornet_frame(name, '', 'MyObjectBuilder_SmallGatlingGun', script_text('ShrikeAI.cs'), ai=True, sensor=True, merge=False)
    bp.legacy_shapes = False                       # sample-derived shapes: cockpit / timer / PB (enabled)
    # the cockpit stands on the deck: antenna, upper camera and the two dive thrusters make room; battery 4's cell takes the PB
    _drop(bp, lambda b: any(t in (b['name'] or '') for t in ('Antenna', 'Camera Up', 'Ion Dive', ' PB', 'Battery 4')))
    bp.add('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', (1, 1, -2), 'Forward', 'Up', f'[Helm] {name} PB',
           extra=pb_extra(script_text('ShrikeAI.cs')), kind='ctrl')
    seat = bp.add('MyObjectBuilder_Cockpit', 'SmallBlockCockpit', (-1, 2, -2), 'Forward', 'Up', f'{name} Seat', kind='ctrl')
    # dive thrusters on the rear deck behind the cockpit (armour stubs on the core's back face carry them)
    bp.armor(-1, 1, 1); bp.armor(1, 1, 1)
    thr(bp, (-1, 2, 1), 'Up', 'Backward', f'{name} Ion Dive L'); thr(bp, (1, 2, 1), 'Up', 'Backward', f'{name} Ion Dive R')
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
    timer['settings'] = {'Delay': 5000, 'Silent': True}
    bp.set_toolbars(timer, toolbar=[TB.group('Thrusters', 'OnOff_Off'), TB.group('Weapons', 'OnOff_Off')])
    return finalize(bp)

def shrike_test(lite=False):
    return _shrike_test('Shrike Test')

def shrike_test_rocket(lite=False):
    return _shrike_test('Shrike Test Rocket', ('MyObjectBuilder_SmallMissileLauncher', ''))

MODELS['Shrike Test'] = shrike_test
MODELS['Shrike Test Rocket'] = shrike_test_rocket

if __name__ == '__main__':
    for n, f in MODELS.items():
        bp = f(False); bp.validate(); print('ok', n, len(bp.blocks), 'blocks', bp.notes)
