"""Compatibility shim: the drone designs now live in tools/designs/core.py (see tools/designs/_common.py).

Old names kept: hornet_frame, gnat(script, merge, sensor, name), longbow(grid, script), thr, cam, pb_extra, ant_extra, BAT_S,
PAL, DARK, GUN.  New code should import tools/designs/core.py (MODELS) instead.
"""
import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'designs'))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sebp import Blueprint
from _common import DARK, GUN, PAL, BAT_S, pb_extra, ant_extra, thr, cam
from core import hornet_frame, gnat_frame as gnat, longbow_frame as longbow, MODELS
