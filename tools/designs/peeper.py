"""Peeper: tiny small-grid vitals probe for the Ballista (one small turret + camera, 12 ion thrusters, merge latch); Pathfinder: the same
hull as a stand-alone hive weakpoint finder (scripts/PathfinderAI.cs, docs/design/pathfinder.md).

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Script scripts/PeeperAI.cs is read at call time.
`python3 -I tools/designs/peeper.py` builds + validates both variants, one line each.  Design record: docs/design/ships/Ballista.md.

Job: fly close to the target the Ballista is shooting at and let the game's turret targeting groups (PowerSystems / Weapons / Propulsion)
point at the vital blocks; PeeperAI.cs turns that into hull positions in the target's own frame and reports them to the Ballista's Spotter.

Frame (cells of 0.5 m; x Right, y Up, z Backward, nose = -z): core 3 x 2 x 7 (x -1..1, y 0..1, z -2..4) with a 5x5x5 small gatling turret
(SmallGatlingTurret, used as an unarmed target designator: no ammunition is loaded) on top (x -2..2, y 2..6, z -2..2).  Thrusters: main pair
(exhaust aft) at the tail, brake pair at the nose, two strafe thrusters per side, lift pair under the core, dive pair behind the turret.
Docking latch: stalk below the tail, the merge block is the lowest block (clamp face Down), like the Gnat.
LITE = no turret (the unseen-shape block): a plain camera scout that cannot identify vital blocks.
Pathfinder: no latch stalk (outline rule, CLAUDE.md): its printer sets a flush merge block into the hull (printers.DRONES latch=True).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C

CUSTOM = "[Peeper]\nIGC Tag=PEEPER\nHost Tag=BALLISTA\n"
PF_CUSTOM = "[Pathfinder]\nIGC Tag=PATHFINDER\nSeeker Group=PowerSystems\nEngage Range=5000\n"

def peeper(lite=False):
    return _probe("Peeper Lite" if lite else "Peeper", lite, "PeeperAI.cs", CUSTOM, latch=True)

def pathfinder(lite=False):
    return _probe("Pathfinder Lite" if lite else "Pathfinder", lite, "PathfinderAI.cs", PF_CUSTOM, latch=False,
                  colors=((0.10, 0.60, 0.15), (0.35, -0.80, -0.10)))

def _probe(name, lite, script_name, custom, latch, colors=None):
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    ACC, HULL = colors or ((0.07, 0.55, 0.10), (0.58, -0.78, -0.25))
    script = script_text(script_name)
    spec = {(0, 0, 0): "rc", (0, 1, -1): "pb", (0, 1, 1): "gyro",
            (-1, 0, -1): "bat", (1, 0, -1): "bat", (-1, 0, 1): "bat", (1, 0, 1): "bat"}
    for x in (-1, 0, 1):
        for y in (0, 1):
            for z in range(-2, 5):
                role = spec.get((x, y, z))
                if role is None:
                    bp.armor(x, y, z, ACC if z in (-2, 4) else HULL)
                elif role == "rc":
                    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (x, y, z), name=f"{name} Remote Control", kind="ctrl")
                elif role == "pb":
                    bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (x, y, z), "Forward", "Up", f"{name} PB",
                           extra=pb_extra(script), kind="ctrl", custom_data=custom)
                elif role == "gyro":
                    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (x, y, z), "Forward", "Up", f"{name} Gyroscope", kind="gyro")
                elif role == "bat":
                    C.bat(bp, (x, y, z), f"{name} Battery {1 + sum(1 for b in bp.blocks if b['kind'] == 'bat')}")
    if not lite:
        t = bp.add("MyObjectBuilder_LargeGatlingTurret", "SmallGatlingTurret", (-2, 2, -2), "Forward", "Up", f"{name} Turret [Seeker]", GUN, kind="gun")
    # thrusters
    thr(bp, (-1, 0, 5), "Backward", "Up", f"{name} Ion Main L"); thr(bp, (1, 0, 5), "Backward", "Up", f"{name} Ion Main R")
    thr(bp, (-1, 1, -4), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (1, 1, -4), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, 0, 2), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, 1, 2), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, 0, 2), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, 1, 2), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-1, -2, 1), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (1, -2, 1), "Down", "Forward", f"{name} Ion Lift R")
    thr(bp, (-1, 2, 3), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (1, 2, 3), "Up", "Backward", f"{name} Ion Dive R")
    # eyes and ears
    cam(bp, (0, 0, -3), "Forward", "Up", f"{name} Camera Nose", 3)
    cam(bp, (0, 1, -3), "Forward", "Up", f"{name} Camera Nose Upper", 3)
    cam(bp, (0, -1, 1), "Down", "Forward", f"{name} Camera Down")
    ant(bp, (0, 2, 3), "Up", "Backward", f"{name} Antenna", 5000)
    if latch:   # docking latch (Peeper only; the Pathfinder gets a flush merge block from its printer)
        bp.armor(0, -1, 3, RED); bp.armor(0, -2, 3, RED)
        merge_small(bp, (0, -3, 3), "Down", f"{name} Merge")
    return C.add_support_pbs(finalize(bp))      # [Thrust] PB here too, so a printed probe carries it

MODELS = {"Peeper": peeper, "Pathfinder": pathfinder}
DOCS = {'Peeper': 'Tiny unarmed vitals probe (3.5 x 5 x 5.5 m, 4.6 t): one small turret used as a target designator, 12 ion thrusters, merge latch. Printed by the Ballista\'s probe foundry; circles the Ballista\'s target, cycles the turret through the '
        'PowerSystems / Weapons / Propulsion targeting groups and reports the vital blocks it finds. Lite has no turret and finds nothing.'}
DOCS['Pathfinder'] = ('Stand-alone hive weakpoint finder on the Peeper hull (no latch stalk; its printer, the Pathfinder Nest, sets a flush merge block). Waits near its printer until '
                      'the fleet reports a target within 5 km, flies there and circles it; its unarmed turret aims at the PowerSystems group, the aim lines from around the orbit are crossed '
                      'to locate the power block in the target\'s frame, and the block goes to every friendly combatant on the FLEET weakpoint tag (<ch>_WP). Lite has no turret.')
MODEL_NOTES = {'Peeper': 'Does nothing useful without a Ballista (its Spotter) to report to; the turret carries no ammunition by design.',
               'Pathfinder': 'Useful with drones that carry the WEAKPOINT RELAY (every split drone\'s Staff PB: Longbow, Basilisk, Hornet, Wasp, ...); the turret carries no ammunition by design.'}
SCRIPTS = ['PeeperAI.cs', 'PathfinderAI.cs']

if __name__ == "__main__":
    sys.exit(C.run_module(MODELS))
