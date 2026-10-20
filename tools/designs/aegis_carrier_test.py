"""Aegis Carrier test ship (sim / generator only, never packaged): 'Aegis Carrier Wing' = the carrier with FOUR Hornets in each of its six pods = 24 drones (31 CubeGrids, about 64,000 PCU).

Module contract: MODELS = {name: callable(lite)}, NO_LITE, DOCS.   Built by `python3 -I tools/build_all.py --no-zip --only "Aegis Carrier Wing"`; the plan's sim smoke test uses it
(docs/design/ships/Aegis_Carrier.md section 9).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import aegis_carrier as A


def aegis_carrier_wing(lite=False):
    return A.aegis_carrier(False, name='Aegis Carrier Wing', wing=4)


MODELS = {'Aegis Carrier Wing': aegis_carrier_wing}
NO_LITE = tuple(MODELS)
DOCS = {'Aegis Carrier Wing': 'Test ship: the Aegis Carrier with 24 Hornets clamped in its six pods (about 64,000 PCU). Built for the simulator, never packaged.'}
