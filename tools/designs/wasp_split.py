"""Wasp Split (test ship, kind 'test'): the Wasp frame with TWO Programmable Blocks on one small grid.

  [Flight] Wasp Split PB   scripts/WaspFlightAI.cs  - the hot path (tracking, lead, aim, control, rockets, firing, hive, AI hybrid, kamikaze defence)
  [Staff]  Wasp Split PB   scripts/WaspStaffAI.cs   - fleet link, FOLLOW / SATELLITE, take-off (LAUNCH), the status display, the Flight watchdog

Why: WaspAI.cs is 98,971 of the 100,000 minified characters a Programmable Block accepts.  The two PBs talk over IGC with
TransmissionDistance.CurrentConstruct (docs/audit/multi_pb_feasibility.md measured the latency: 2 ticks, so the aim loop stays whole in Flight).
Same hull as the Wasp (core.py hornet frame via wasp.frame): the Staff PB sits in the free cell behind the core at (-1, 1, 1), its front face seated on
the armour at (-1, 1, 0) (the cell shrike_test.py uses on the identical frame).  No DLC blocks, one grid, so no sebp_multi.
Not packaged (kind 'test'); wasp.py / WaspAI.cs are untouched.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import wasp as W

NO_LITE = ('Wasp Split',)
DOCS = {
    'Wasp Split': 'TEST SHIP (not for the game yet): the Wasp split over TWO Programmable Blocks on one grid, "[Flight] Wasp Split PB" (WaspFlightAI: tracking, lead, aim, control, rockets, '
                  'firing) and "[Staff] Wasp Split PB" (WaspStaffAI: fleet link, FOLLOW / SATELLITE, take-off, display, Flight watchdog), linked by IGC (CurrentConstruct). Gets the Wasp '
                  'past the 100,000-character limit. Custom Data: the Flight PB holds every [Wasp] key, the Staff PB [Launch] / [Follow] / [Fleet].',
}

def wasp_split(lite=False):
    name = 'Wasp Split'
    bp = W.frame(name, 'MyObjectBuilder_SmallMissileLauncher', '', 'Launcher', 4, script_text('WaspFlightAI.cs'), W._custom('Wasp', 'WASP'),
                 ai=True, sensor=True, merge=True)
    pb = [b for b in bp.blocks if b['name'] == f'{name} PB'][0]
    pb['name'] = f'[Flight] {name} PB'
    bp.add('MyObjectBuilder_MyProgrammableBlock', 'SmallProgrammableBlock', (-1, 1, 1), 'Forward', 'Up', f'[Staff] {name} PB',
           extra=pb_extra(script_text('WaspStaffAI.cs')), kind='ctrl')
    return finalize(bp)

MODELS = {'Wasp Split': wasp_split}

if __name__ == '__main__':
    sys.exit(C.run_module(MODELS))
