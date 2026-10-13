"""Cradle test ships for scripts/CradleBayAI.cs (control_arch M5, kind 'test': built into out/Blueprints, never packaged).

  Cradle Pod Test   the 2-slot Cradle Pod (designs/pods.py: Pod PB running CradleBayAI, Timer R1/R2, batteries, reactors) with two real Hornets clamped to its merge blocks
                    (attach_drone). No Staff: the sim (dock-launch --cradle, selftest m5) talks to the Pod PB by argument and by IGC.
  Cradle Test       Corsair Wing Lite (the Corsair with two Hornets clamped in its pylon cradles) with REAL scripts in its three PBs: [Helm] ShrikeAI, [Staff] StaffAI and the
                    [Bay] CradleBayAI that the Corsair design carries since M5. The real Corsair Helm / Staff scripts are M6 work; this is the shape the bays and the
                    name-prefix rule ("[SH]") are tested on: Helm block lists with two merged drones, 'Run Staff release 1' with a pilot in the seat.
Module contract: MODELS = {name: callable(lite)}, NO_LITE, DOCS.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
import pods
import corsair

MODELS = {}
NO_LITE = ('Cradle Pod Test', 'Cradle Test')
DOCS = {
    'Cradle Pod Test': 'TEST SHIP (not for the game; control_arch M5): the 2-slot Cradle Pod with two Hornets clamped in its merge cradles and CradleBayAI in the Pod PB. Throwaway.',
    'Cradle Test': 'TEST SHIP (not for the game; control_arch M5): Corsair Wing Lite with ShrikeAI as [Helm], StaffAI as [Staff] and CradleBayAI as [Bay] (two Hornets clamped in the pylon cradles). Throwaway.',
}

def cradle_pod_test(lite=False):
    from core import hornet
    pod = pods.cradle_pod(2, lite=False, name='Cradle Pod Test', tag='POD', ship_tag='SHIP')
    for k in range(2):
        d = hornet(False)
        d.display = 'Hornet'
        pods.attach_drone(pod, k, d)
    return finalize(pod)

def cradle_test(lite=False):
    old = dict(corsair.SCRIPT_MAP)
    corsair.SCRIPT_MAP.update({'CorsairHelmAI.cs': 'ShrikeAI.cs', 'CorsairStaffAI.cs': 'StaffAI.cs'})
    try:
        bp = corsair.corsair_wing(True)
    finally:
        corsair.SCRIPT_MAP.clear(); corsair.SCRIPT_MAP.update(old)
    bp.name = 'Cradle Test'
    return bp

MODELS['Cradle Pod Test'] = cradle_pod_test
MODELS['Cradle Test'] = cradle_test

if __name__ == '__main__':
    for n, f in MODELS.items():
        bp = f(False); bp.validate(); print('ok', n, len(bp.blocks), 'blocks', len(bp.subgrids), 'sub-grids')
