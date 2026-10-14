"""Ram and Ram Tandem: heavy-armour kinetic impact drones, small grid.  Design record: docs/design/kinetic_ram.md (a line of its own, not a Tube).

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  `python3 -I tools/designs/ram.py` builds + validates every model.

A 3 x 3 cell rod (1.5 m), 16 cells (8 m) long: a PROW of 6 layers of SmallHeavyBlockArmorBlock (54 cubes) carries the hit; everything that matters sits
in the prow's shadow (x, y in 0..2): one 3x2x3 battery, the single SmallBlockLargeThrust (3x2x4, exhaust aft), PB, remote control, gyro.  Only the two
camera stubs, the antenna and the merge stalk leave the 3 x 3 section.  Frame (core.py convention): x = Right, y = Up, z = Backward, nose = z 0, cells 0.5 m.
Ram Tandem adds two small warheads one layer behind the prow.  Thrust is forward only (point-and-burn).

Script: StingerAI.cs is a PLACEHOLDER until scripts/RamAI.cs exists (plan phase P2).  Lite: retired, `lite` is ignored.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C

HEAVY = "SmallHeavyBlockArmorBlock"
PROW = 6                                   # heavy layers, z 0..5
ROD_LEN = 16                               # z 0..15
PROW_COL = (0.07, 0.55, 0.10)
HULL_COL = (0.58, -0.78, -0.25)
ORANGE = (0.98, 0.65, 0.05)

def ram_frame(name, prow=PROW, warheads=False):
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    script = script_text("StingerAI.cs")                         # placeholder, see module docstring
    custom = "[Stinger]\nIGC Tag=RAM\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=IDLE\n"
    zb = prow                                                    # first layer behind the prow
    # prow: full 3x3 heavy layers
    for z in range(prow):
        for y in range(3):
            for x in range(3):
                bp.armor(x, y, z, PROW_COL if z == 0 else HULL_COL, sub=HEAVY)
    # battery 3x2x3 under the top row, directly behind the prow
    C.bat(bp, (0, 0, zb), f"{name} Battery", small=False)
    # top row behind the prow: Remote Control, PB (front face on the prow), gyro on the battery
    if warheads:
        bp.add("MyObjectBuilder_Warhead", "SmallWarhead", (0, 2, zb), "Forward", "Up", f"{name} Warhead 1", ORANGE, kind="warhead")
        bp.add("MyObjectBuilder_Warhead", "SmallWarhead", (2, 2, zb), "Forward", "Up", f"{name} Warhead 2", ORANGE, kind="warhead")
    else:
        bp.armor(0, 2, zb, HULL_COL); bp.armor(2, 2, zb, HULL_COL)
    bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (1, 2, zb), "Forward", "Up", f"{name} PB",
           extra=pb_extra(script), kind="ctrl", custom_data=custom)
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (0, 2, zb + 1), name=f"{name} Remote Control", kind="ctrl")
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (2, 2, zb + 1), "Forward", "Up", f"{name} Gyroscope", kind="gyro")
    bp.armor(1, 2, zb + 1, HULL_COL)
    # spine row above the thruster (z zb+2 .. zb+6) and filler where the battery leaves the top row free
    zt = zb + 3                                                  # thruster 3x2x4: z zt .. zt+3
    for z in range(zb + 2, zt + 4):
        for x in range(3):
            if (x, 2, z) not in bp.occupancy(): bp.armor(x, 2, z, HULL_COL)
    C.thr(bp, (0, 0, zt), "Backward", "Up", f"{name} Ion Main", sub="SmallBlockLargeThrust")
    # camera stubs above and below the rod, seated on armour (camera mounts on its back only)
    C.cam(bp, (1, 3, zb), "Forward", "Up", f"{name} Camera Upper", 3)
    bp.armor(1, 3, zb + 1, HULL_COL)
    C.cam(bp, (1, -1, zb), "Forward", "Up", f"{name} Camera Lower", 3)
    bp.armor(1, -1, zb + 1, HULL_COL)
    # antenna standing on the spine
    C.ant(bp, (1, 3, zt + 1), "Up", "Backward", f"{name} Antenna", 5000)
    # docking latch: stalk on the top row behind the thruster tail, clamp face Backward
    zl = zt + 4
    bp.armor(1, 2, zl, RED); bp.armor(1, 2, zl + 1, RED)
    C.merge_small(bp, (1, 2, zl + 2), "Backward", f"{name} Merge")
    return finalize(bp)

def ram(lite=False):
    return ram_frame("Ram")

def ram_tandem(lite=False):
    return ram_frame("Ram Tandem", warheads=True)

MODELS = {"Ram": ram, "Ram Tandem": ram_tandem}
NO_LITE = set(MODELS)
REQUIRED_AXES = {m: ("Forward",) for m in MODELS}
DOCS = {
    'Ram': 'Kinetic impact drone, 1.5 m square rod 8 m long, about 9 t, about 18 m/s2: six layers of heavy armour in front, one large ion thruster, 4 MW battery, merge latch. No weapon: it flies into the target at 100 m/s.',
    'Ram Tandem': 'Ram with two small warheads one layer behind the heavy prow, fired inside the breach after impact.',
}
_NOTE = 'Thrust only forward. Ram damage is not verified in the game: run the Ram Test (docs/design/kinetic_ram.md section 1.4). The script is a placeholder (StingerAI) until RamAI.cs.'
MODEL_NOTES = {m: _NOTE for m in MODELS}
SCRIPTS = ['StingerAI.cs']

if __name__ == "__main__":
    sys.exit(C.run_module(MODELS, required_axes=REQUIRED_AXES))
