#!/usr/bin/env python3
"""Build, re-validate and package the Lite variants ("<Name> Lite") of every drone / printer / warship blueprint.

    python3 -I tools/build_lite.py [--jobs N] [--only "Name" ...] [--out DIR] [--no-zip] [--zip]

Same pipeline and options as tools/build_all.py (which now builds the Full variants only), restricted to lite=True: blueprints go to
out/Blueprints/<Name> Lite/, the zips are Drones_v<VERSION>_Lite / Printers_v<VERSION>_Lite / Warships_v<VERSION>_Lite in out/dist/,
the table is out/dist/MODELS_Lite.md.

RETIRED: the Lite variants are not built or shipped until the owner asks for them back.  To bring them back set RETIRED = False below
(the Lite design code in tools/designs/*.py is untouched).
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_all                                    # noqa: E402

RETIRED = True

if __name__ == '__main__':
    if RETIRED:
        sys.exit('build_lite.py: the Lite variants are retired (set RETIRED = False in this file to build them again)')
    sys.exit(build_all.main(sys.argv[1:], lites=(True,)))
