"""Core drones: Hornet (2 gatlings), Gnat, Longbow S, Longbow L.

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Scripts are read from scripts/ at call time.
`python3 -I tools/designs/core.py` builds + validates every model and variant and prints one line each.

DOCKING FACES (printers): the clamp face of the small merge block is its local Up (MERGE_CLAMP_FACE_SMALL in _common.py).
  * Gnat   : merge block hangs one cell BELOW the belly (clamp face = Down). It is the lowest block of the drone.
  * Hornet : merge block sits on a 2-cell stalk behind the rear camera, between the main thrusters (clamp face = Backward).
             It sticks out 1 cell beyond the thruster tails. Keep the thrusters OFF while merged.
  * Longbows have no merge block (not printed).
  _common.check_docking() asserts the merge block is the only block reaching the outer layer along its clamp axis, so
  nothing else of the drone can touch a flat printer surface.

LITE variants: only block types whose XML was seen in Starburst plus the merge block (LITE_KEEPS_MERGE in _common.py; its
  shape is from the Keen source, not seen).  Lite drops Sensor, AI Offensive and AI Flight blocks.  Everything else is shared.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403  (helpers + constants)
import _common as C

# ---------------------------------------------------------------------------------------------
def hornet_frame(name, weapon_sub, weapon_type, script, custom_data=None, ai=True, sensor=True, title=None, merge=True):
    """Hornet-class 6-axis small drone: 3x2x4 core, two fixed forward weapons, 12 ion thrusters, rear merge latch."""
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    HULL = (0.58, -0.78, -0.25); ACC = (0.07, 0.55, 0.10)
    # core: x -1..1, y 0..1, z -3..0
    for x in (-1, 0, 1):
        for y in (0, 1): bp.armor(x, y, -3, ACC)
    for i, (x, y) in enumerate(((-1, 0), (1, 0), (-1, 1), (1, 1))):
        bat(bp, (x, y, -2), f"{name} Battery {i+1}")
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (0, 0, -2), name=f"{name} Remote Control", kind="ctrl")
    # PB mounts only on its front face: face it into the nose armour
    bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (0, 1, -2), "Forward", "Up", f"{name} PB",
           extra=pb_extra(script) if script else '', kind="ctrl", custom_data=custom_data)
    for x in (-1, 0, 1): bp.armor(x, 0, -1)
    bp.armor(-1, 1, -1); bp.armor(1, 1, -1)
    # gyro mounts only on its bottom: sit it on the armour below
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (0, 1, -1), "Forward", "Up", f"{name} Gyroscope", kind="gyro")
    for x in (-1, 0, 1):
        for y in (0, 1): bp.armor(x, y, 0)
    # nose cameras: backs on the nose armour
    cam(bp, (0, 0, -4), "Forward", "Up", f"{name} Camera Lower", 3)
    cam(bp, (0, 1, -4), "Forward", "Up", f"{name} Camera Upper", 3)
    # weapons on the flanks (back face on the side thrusters, side mounts on the core)
    for x, s in ((-2, "L"), (2, "R")):
        g = bp.add(weapon_type, weapon_sub, (x, 0, -4), "Forward", "Up", f"{name} {('Gatling' if weapon_type.endswith('GatlingGun') else 'Launcher')} {s}", GUN, kind="gun")
        bp.keep_clear(g, 6, "muzzle")
    thr(bp, (-1, 0, 1), "Backward", "Up", f"{name} Ion Main L"); thr(bp, (1, 0, 1), "Backward", "Up", f"{name} Ion Main R")
    thr(bp, (-2, 1, -2), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (2, 1, -2), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, 0, 0), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, 1, 0), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, 0, 0), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, 1, 0), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-1, -2, -1), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (1, -2, -1), "Down", "Forward", f"{name} Ion Lift R")
    thr(bp, (-1, 2, -1), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (1, 2, -1), "Up", "Backward", f"{name} Ion Dive R")
    # antenna standing on the rear deck
    ant(bp, (0, 2, 0), "Up", "Backward", f"{name} Antenna", 5000)
    # all-around cameras
    cam(bp, (0, 2, -3), "Up", "Backward", f"{name} Camera Up")
    cam(bp, (0, -1, -3), "Down", "Forward", f"{name} Camera Down")
    cam(bp, (0, 1, 1), "Backward", "Up", f"{name} Camera Rear")
    bp.armor(-1, -1, -2, HULL); bp.armor(1, -1, -2, HULL)          # outriggers under the core for the side cameras
    cam(bp, (-2, -1, -2), "Left", "Up", f"{name} Camera Left")
    cam(bp, (2, -1, -2), "Right", "Up", f"{name} Camera Right")
    if merge:
        # docking latch: 2-cell stalk between the main thrusters, clamp face Backward, 1 cell proud of the thruster tails
        bp.armor(0, 0, 1, RED); bp.armor(0, 0, 2, RED)
        merge_small(bp, (0, 0, 3), "Backward", f"{name} Merge")
    if sensor:
        bp.add("MyObjectBuilder_SensorBlock", "SmallBlockSensor", (0, -1, -2), "Down", "Forward", f"{name} Sensor", kind="sensor")
    if ai:
        bp.add("MyObjectBuilder_OffensiveCombatBlock", "SmallOffensiveCombat", (0, -1, -1), name=f"{name} AI Offensive [AI]", kind="ai")
        bp.add("MyObjectBuilder_FlightMovementBlock", "SmallFlightMovement", (0, -1, 0), name=f"{name} AI Flight [AI]", kind="ai")
    return finalize(bp)

def hornet(lite=False):
    name = "Hornet Lite" if lite else "Hornet"
    return hornet_frame(name, "", "MyObjectBuilder_SmallGatlingGun", script_text("HornetDroneAI.cs"),
                        ai=not lite, sensor=not lite, merge=(not lite) or LITE_KEEPS_MERGE)

# ---------------------------------------------------------------------------------------------
def gnat_frame(script, merge=True, sensor=True, name="Gnat", ai=True):
    """Point-and-burn drone: two axial ion thrusters back-to-back, gyro steering, AI Offensive + Flight blocks on the tail, belly merge latch."""
    bp = Blueprint(name, "Small", seed=7, legacy_shapes=True)
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (1, 0, -2), name=f"{name} Remote Control", kind="ctrl", color=RED)
    bat(bp, (1, 0, -1), f"{name} Battery")
    bp.armor(1, 0, 0, RED)
    g = bp.add("MyObjectBuilder_SmallGatlingGun", "", (0, 0, -3), "Forward", "Up", f"{name} Gatling", GUN, kind="gun")
    bp.keep_clear(g, 6, "muzzle")
    # PB mounts only on its front: face it into the battery from the side
    bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (2, 0, -1), "Left", "Up", f"{name} PB",
           extra=pb_extra(script) if script else '', kind="ctrl", color=RED)
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (1, 1, -2), "Forward", "Up", f"{name} Gyroscope", kind="gyro")
    bp.armor(1, 1, -1)
    # the two thrusters sit back-to-back on the same axis, their mount ends touching
    thr(bp, (0, 1, -3), "Forward", "Up", f"{name} Ion Reverse")
    thr(bp, (0, 1, -1), "Backward", "Up", f"{name} Ion Main")
    ant(bp, (1, 1, 0), "Up", "Backward", f"{name} Antenna", 2500)
    cam(bp, (1, 0, -3), "Forward", "Up", f"{name} Camera")
    cam(bp, (2, 0, -2), "Right", "Up", f"{name} Camera Right")
    cam(bp, (1, -1, -2), "Down", "Forward", f"{name} Camera Down")
    if ai:
        # tail section (2 cells longer than v2.7): AI blocks, rear camera on the new tail end
        bp.add("MyObjectBuilder_OffensiveCombatBlock", "SmallOffensiveCombat", (1, 0, 1), name=f"{name} AI Offensive [AI]", kind="ai")
        bp.add("MyObjectBuilder_FlightMovementBlock", "SmallFlightMovement", (1, 0, 2), name=f"{name} AI Flight [AI]", kind="ai")
        cam(bp, (1, 0, 3), "Backward", "Up", f"{name} Camera Rear")
    else:
        cam(bp, (1, 0, 1), "Backward", "Up", f"{name} Camera Rear")
    cam(bp, (-1, 0, 0), "Left", "Up", f"{name} Camera Left")
    cam(bp, (1, 2, -1), "Up", "Backward", f"{name} Camera Up")
    if sensor:
        bp.add("MyObjectBuilder_SensorBlock", "SmallBlockSensor", (2, 0, 0), "Right", "Up", f"{name} Sensor", kind="sensor")
    if merge:
        # docking latch: belly stalk (armour + merge); the merge block is the lowest block, clamp face Down
        bp.armor(1, -1, 0, RED)
        merge_small(bp, (1, -2, 0), "Down", f"{name} Merge")
    return finalize(bp)

def gnat(lite=False):
    name = "Gnat Lite" if lite else "Gnat"
    return gnat_frame(script_text("GnatSwarmAI.cs"), merge=(not lite) or LITE_KEEPS_MERGE, sensor=not lite, name=name, ai=not lite)

# ---------------------------------------------------------------------------------------------
def longbow_frame(grid, script, lite=False, seeker=False):
    L = grid == "Large"
    tag = ("Longbow L" if L else "Longbow S") + (" Seeker" if seeker else "") + (" Lite" if lite else "")
    bp = Blueprint(tag, grid, seed=11 if L else 12, legacy_shapes=True)
    ACC = (0.12, 0.55, 0.15)
    sub = lambda s, l: l if L else s
    rail = bp.add("MyObjectBuilder_SmallMissileLauncherReload", sub("SmallRailgun", "LargeRailgun"), (0, 0, -8), "Forward", "Up", f"{tag} Railgun", DARK, kind="rail")
    bp.keep_clear(rail, 8, "muzzle")
    # nose camera bank: backs on row z=-3
    for y in (-1, -2):
        for x in (-1, 0, 1):
            cam(bp, (x, y, -4), "Forward", "Up", f"{tag} Camera {'U' if y == -1 else 'L'}{x+2}", 3)
    bp.add("MyObjectBuilder_RemoteControl", sub("SmallBlockRemoteControl", "LargeBlockRemoteControl"), (0, -1, -3), name=f"{tag} Remote Control", kind="ctrl")
    for x in (-1, 1): bp.armor(x, -1, -3, ACC); bp.armor(x, -2, -3, ACC)
    bp.armor(0, -2, -3, ACC)
    # small PB mounts on its front, the large one only on its bottom
    bp.add("MyObjectBuilder_MyProgrammableBlock", sub("SmallProgrammableBlock", "LargeProgrammableBlock"), (0, -2, -2), "Forward", "Down" if L else "Up",
           f"{tag} PB", extra=pb_extra(script) if script else '', kind="ctrl")
    bp.armor(0, -1, -2)
    # gyro mounts only on its bottom: stand it on the armour behind
    bp.add("MyObjectBuilder_Gyro", sub("SmallBlockGyro", "LargeBlockGyro"), (0, -1, -1), "Forward", "Up", f"{tag} Gyroscope", kind="gyro")
    if L:
        for i, (x, y) in enumerate([(-1, -1), (1, -1), (-1, -2), (1, -2)]):
            bat(bp, (x, y, -2), f"{tag} Battery {i+1}")
    else:
        for x in (-1, 1): bp.armor(x, -1, -2); bp.armor(x, -2, -2)
        bat(bp, (-1, -4, -1), f"{tag} Battery", small=False)
    for z in (-1, 0):
        for x in (-1, 0, 1):
            for y in (-1, -2):
                if (x, y, z) != (0, -1, -1): bp.armor(x, y, z)
    thr(bp, (-1, -1, 1), "Backward", "Up", f"{tag} Ion Main L"); thr(bp, (1, -1, 1), "Backward", "Up", f"{tag} Ion Main R")
    thr(bp, (-2, -1, -3), "Forward", "Up", f"{tag} Ion Brake L"); thr(bp, (2, -1, -3), "Forward", "Up", f"{tag} Ion Brake R")
    thr(bp, (-3, -2, -1), "Left", "Up", f"{tag} Ion Strafe R1"); thr(bp, (-3, -2, 0), "Left", "Up", f"{tag} Ion Strafe R2")
    thr(bp, (2, -2, -1), "Right", "Up", f"{tag} Ion Strafe L1"); thr(bp, (2, -2, 0), "Right", "Up", f"{tag} Ion Strafe L2")
    if L:
        thr(bp, (-1, -4, -2), "Down", "Forward", f"{tag} Ion Lift L"); thr(bp, (1, -4, -2), "Down", "Forward", f"{tag} Ion Lift R")
    else:   # the small one's battery hangs underneath; lift thrusters go on its sides
        thr(bp, (-2, -5, 0), "Down", "Forward", f"{tag} Ion Lift L"); thr(bp, (2, -5, 0), "Down", "Forward", f"{tag} Ion Lift R")
    thr(bp, (-2, 0, -2), "Up", "Backward", f"{tag} Ion Dive L"); thr(bp, (2, 0, -2), "Up", "Backward", f"{tag} Ion Dive R")
    if L:
        bp.add("MyObjectBuilder_RadioAntenna", "LargeBlockRadioAntenna", (0, 0, 0), "Forward", "Up", f"{tag} Antenna", extra=ant_extra(10000), kind="ant")
    else:
        ant(bp, (0, 0, 0), "Up", "Backward", f"{tag} Antenna", 5000)
    cam(bp, (0, -1, 1), "Backward", "Up", f"{tag} Camera Rear")
    cam(bp, (0, -3, -3), "Down", "Forward", f"{tag} Camera Down")
    cam(bp, (0, 2, -1), "Up", "Backward", f"{tag} Camera Up")
    cam(bp, (-2, -1, 0), "Left", "Up", f"{tag} Camera Left")
    cam(bp, (2, -1, 0), "Right", "Up", f"{tag} Camera Right")
    if seeker:
        # option-4 test: an artillery turret (2 km, the railgun's reach) on the railgun's back, named [Seeker]: the script sets it to
        # the targeting group and drills where it points
        bp.add("MyObjectBuilder_LargeMissileTurret", "LargeCalibreTurret", (-1, 2, -4), "Forward", "Up", f"{tag} Artillery [Seeker]", DARK, kind="turret")
    if not lite:
        bp.add("MyObjectBuilder_SensorBlock", sub("SmallBlockSensor", "LargeBlockSensor"), (-1, -3, -3), "Down", "Forward", f"{tag} Sensor", kind="sensor")
        bp.add("MyObjectBuilder_OffensiveCombatBlock", sub("SmallOffensiveCombat", "LargeOffensiveCombat"), (1, -3, -3), name=f"{tag} AI Offensive [AI]", kind="ai")
        bp.add("MyObjectBuilder_FlightMovementBlock", sub("SmallFlightMovement", "LargeFlightMovement"), (1, -3, -1) if L else (1, -3, -2), name=f"{tag} AI Flight [AI]", kind="ai")
    return finalize(bp)

def longbow_s(lite=False): return longbow_frame("Small", script_text("LongbowSniperAI.cs"), lite)
def longbow_l(lite=False): return longbow_frame("Large", script_text("LongbowSniperAI.cs"), lite)

def longbow_l_seeker(lite=False):
    """Option-4 test ship: Longbow L + artillery turret named [Seeker] on its back (large grid, paste only)."""
    return longbow_frame("Large", script_text("LongbowSniperAI.cs"), lite, seeker=True)

MODELS = {"Hornet": hornet, "Gnat": gnat, "Longbow S": longbow_s, "Longbow L": longbow_l, "Longbow L Seeker": longbow_l_seeker}

# The Gnat is a point-and-burn drone (gyro steering, two axial thrusters): only Forward/Backward thrust is required.
REQUIRED_AXES = {"Gnat": ("Forward", "Backward")}

if __name__ == "__main__":
    sys.exit(C.run_module(MODELS, required_axes=REQUIRED_AXES))
