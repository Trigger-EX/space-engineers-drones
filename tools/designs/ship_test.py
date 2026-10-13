"""Warship test ships for the M6 sim runs (control_arch M6, kind 'test': built into out/Blueprints, never packaged).

  Warden Wing Test      the Warden (Full) with four real Hornets clamped in the cradles of its docked pod
  Leviathan Wing Test   the Leviathan (Full) with four real Hornets in each of its two pods (8 drones)
Same ships, same scripts, same Custom Data as the packaged ones; only the pods are loaded (the packaged pods are empty: the player pastes drones into them).
Module contract: MODELS = {name: callable(lite)}, NO_LITE, DOCS.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import warden
import leviathan

MODELS = {}
NO_LITE = ('Warden Wing Test', 'Leviathan Wing Test')
DOCS = {
    'Warden Wing Test': 'TEST SHIP (not for the game; control_arch M6): the Warden with four Hornets clamped in its pod. Throwaway.',
    'Leviathan Wing Test': 'TEST SHIP (not for the game; control_arch M6): the Leviathan with four Hornets in each pod. Throwaway.',
}

def warden_wing_test(lite=False):
    return warden.warden(False, name='Warden Wing Test', wing=4)

def leviathan_wing_test(lite=False):
    return leviathan.leviathan(False, name='Leviathan Wing Test', wing=4)

MODELS['Warden Wing Test'] = warden_wing_test
MODELS['Leviathan Wing Test'] = leviathan_wing_test

if __name__ == '__main__':
    for n, f in MODELS.items():
        bp = f(False); bp.validate(); print('ok', n, len(bp.blocks), 'blocks', len(bp.subgrids), 'sub-grids')
