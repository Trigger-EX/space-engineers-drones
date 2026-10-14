"""Test ships for the simulator and the generator (control_arch M0, kind 'test': built into out/Blueprints, never packaged).

  Hornet Pilot Test   the Hornet frame without script, antenna and dive thrusters, with a SmallBlockCockpit on top (the seat the sim's ScriptedPilot
                      sits in), a Timer 'Timer D' + an Event Controller 'EC Cockpit' (cockpit occupied -> timer stop / start: the dead-man backup of
                      docs/design/control_arch.md s2.6), a Button Panel, a block group 'Thrusters' and a toolbar on the cockpit.
  Twin PB Test        the Hornet with TWO Programmable Blocks named '[Helm] ...' and '[Staff] ...' (the PB split of the warships), the Helm one running
                      HornetDroneAI, the Staff one running HornetMothership; a Button Panel whose toolbar Runs the Staff PB.
Used by `SimHost.exe selftest m0` (section integration) and by hand: `SimHost.exe one --scenario "1v1/Hornet Pilot Test-vs-Hornet" --pilot ...`.
The Event Controller carries the sim's PLACEHOLDER event number for 'cockpit occupied' (sebp_toolbar.EVENTS_PLACEHOLDER; the real number is unknown, K4).
Module contract: MODELS = {name: callable(lite)}, NO_LITE = names without a Lite variant, DOCS.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import sebp_toolbar as TB
from core import hornet_frame, gnat

MODELS = {}
NO_LITE = ('Hornet Pilot Test', 'Twin PB Test', 'Gnat Side', 'Gnat Side NoRev')
DOCS = {
    'Hornet Pilot Test': 'TEST SHIP (not for the game): Hornet frame with a cockpit instead of antenna, dive thrusters and PB. A Timer, an Event Controller (placeholder event id), a Button Panel, '
                         'a block group and a cockpit toolbar exercise the simulator\'s automation blocks. The simulator\'s scripted pilot sits in the seat.',
    'Gnat Side': 'TEST SHIP (experiment): the Gnat plus one side ion thruster across the tail; GnatSwarmAI rolls it onto the sideways need (RUN drift trim, VectorFly).',
    'Gnat Side NoRev': 'TEST SHIP (experiment): Gnat Side without the reverse thruster (a side thruster instead of the backward one).',
    'Twin PB Test': 'TEST SHIP (not for the game): Hornet with two Programmable Blocks, [Helm] (HornetDroneAI) and [Staff] (HornetMothership), and a Button Panel whose toolbar runs the Staff PB.',
}

def _drop(bp, pred):
    """remove blocks and the keep-clear zones they reserved (exhaust, camera view)"""
    for b in [b for b in bp.blocks if pred(b)]:
        bp.blocks.remove(b)
        for c in [c for c, why in bp.reserve.items() if why.endswith(' of ' + (b['name'] or b['sub']))]: del bp.reserve[c]

def hornet_pilot_test(lite=False):
    name = 'Hornet Pilot Test'
    bp = hornet_frame(name, '', 'MyObjectBuilder_SmallGatlingGun', None, ai=False, sensor=False, merge=False)
    bp.legacy_shapes = False                       # the sample-derived shapes: toolbar-capable Timer / Button Panel / Event Controller / cockpit
    # cockpit on the deck: needs the antenna, the upper camera, the dive thrusters and the PB out of the way
    _drop(bp, lambda b: any(t in (b['name'] or '') for t in ('Antenna', 'Camera Up', 'Ion Dive', ' PB')))
    seat = bp.add('MyObjectBuilder_Cockpit', 'SmallBlockCockpit', (-1, 2, -2), 'Forward', 'Up', f'{name} Seat', kind='ctrl')
    # automation blocks behind the core (z = 1 row, y = 1: armour in front of them)
    timer = bp.add('MyObjectBuilder_TimerBlock', 'TimerBlockSmall', (-1, 1, 1), 'Forward', 'Up', f'{name} Timer D', kind='ctrl')
    ec = bp.add('MyObjectBuilder_EventControllerBlock', 'EventControllerSmall', (1, 1, 1), 'Forward', 'Up', f'{name} EC Cockpit', kind='ctrl')
    panel = bp.add('MyObjectBuilder_ButtonPanel', 'ButtonPanelSmall', (0, 0, 1), 'Forward', 'Up', f'{name} Panel', kind='ctrl')
    thr = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Thrust']
    bp.group('Thrusters', thr)
    gun = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_SmallGatlingGun']
    # Timer D (5 s): cut the thrusters (what the hard dead-man does); EC Cockpit: occupied -> Stop timer, empty -> Start timer
    bp.set_toolbars(timer, toolbar=[TB.group('Thrusters', 'OnOff_Off')])
    bp.set_toolbars(ec, toolbar=[TB.block(timer, 'Stop'), TB.block(timer, 'Start')])
    timer['settings'] = {'Delay': 5000, 'Silent': True}
    ec['settings'] = TB.event_settings('cockpit', threshold=0, experimental=True)
    ec['links'] = {'selected': [seat]}
    # button 0: thrusters on again; cockpit toolbar: slot 0 the gatling, slot 1 Trigger Now on Timer D
    bp.set_toolbars(panel, toolbar=[TB.group('Thrusters', 'OnOff_On')])
    bp.set_toolbars(seat, toolbar=[TB.weapon('SmallGatlingGun', ''), TB.block(timer, 'TriggerNow')])
    return finalize(bp)

def twin_pb_test(lite=False):
    name = 'Twin PB Test'
    bp = hornet_frame(name, '', 'MyObjectBuilder_SmallGatlingGun', script_text('HornetDroneAI.cs'), ai=False, sensor=False, merge=False)
    bp.legacy_shapes = False
    pb = [b for b in bp.blocks if b['name'] == f'{name} PB'][0]
    pb['name'] = f'[Helm] {name} PB'
    # second PB where the fourth battery was (front face into the nose armour, like the first)
    _drop(bp, lambda b: b['name'] == f'{name} Battery 4')
    staff = bp.add('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', (1, 1, -2), 'Forward', 'Up', f'[Staff] {name} PB',
                   extra=pb_extra(script_text('HornetMothership.cs')), kind='ctrl')
    panel = bp.add('MyObjectBuilder_ButtonPanel', 'ButtonPanelSmall', (0, 0, 1), 'Forward', 'Up', f'{name} Panel', kind='ctrl')
    bp.set_toolbars(panel, toolbar=[TB.run(staff)])
    return finalize(bp)

MODELS['Hornet Pilot Test'] = hornet_pilot_test
MODELS['Twin PB Test'] = twin_pb_test
MODELS['Gnat Side'] = lambda lite=False: gnat(name='Gnat Side', side='add')
MODELS['Gnat Side NoRev'] = lambda lite=False: gnat(name='Gnat Side NoRev', side='norev')

if __name__ == '__main__':
    for n, f in MODELS.items():
        bp = f(False); bp.validate(); print('ok', n, len(bp.blocks), 'blocks', bp.notes)
