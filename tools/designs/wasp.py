"""Wasp (2 small rocket launchers) and Lancer (2 small autocannons): Hornet-class frames.

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Scripts (WaspAI.cs / LancerAI.cs) are read from
scripts/ at call time.  `python3 -I tools/designs/wasp.py` builds + validates every model and variant, one line each.

Frame = the Hornet frame of core.py (3x2x4 core, 12 ion thrusters, 5 all-around cameras + 2 nose cameras, rear merge latch),
with the weapon pair swapped:
  * Wasp   : SmallMissileLauncher (1x1x4) at z -4..-1 (identical footprint to the Hornet gatlings).
  * Lancer : SmallBlockAutocannon (SmallGatlingGun/SmallBlockAutocannon, 1x1x5).  Five cells would collide with the strafe
             thrusters behind the 4-cell slot, so the pair sits one cell further forward (z -5..-1); the back face of each
             autocannon still seats on the side thrusters and the nose cameras stay between the muzzles.
Muzzle clearance: 6 cells in front of every weapon (keep_clear); the validator fails the build if anything sits there.
Rockets need 6 m (the rocket accelerates for 25 m but is clear of the hull after one cell); the 6-cell zone is the same as the gatling's.

DOCKING: merge block on a 2-cell stalk behind the rear camera, clamp face Backward (as the Hornet).  Keep thrusters OFF while merged.

LITE variants: no Sensor, no AI Offensive / AI Flight blocks (their XML shape was never seen in Starburst).  The merge block is
kept (LITE_KEEPS_MERGE in _common.py) so Lite drones can still be printed.  Both weapons are SmallMissileLauncher/* and
SmallGatlingGun/* shapes seen in Starburst.  Ammo: the XML ships empty magazines (creative world: not needed; survival: load
4 rockets per launcher / 3 clips per autocannon).  Custom data pre-sets the IGC tag and start mode IDLE (printed drones wait for the printer's JOIN).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403  (helpers + constants)
import _common as C

# PB custom data (MyIni).  The scripts add all other keys on first run.
def _custom(section, tag):
    return f"[{section}]\nIGC Tag={tag}\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=IDLE\n"

def frame(name, wtype, wsub, wlabel, wlen, script, custom_data, ai=True, sensor=True, merge=True):
    """Hornet-class drone with two forward weapons `wlen` cells long (muzzles at z = -(wlen) .. nose line)."""
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
    # weapons on the flanks: back end (z = -1) on the side thrusters, side mounts on the core
    zmin = -1 - (wlen - 1)
    for x, s in ((-2, "L"), (2, "R")):
        g = bp.add(wtype, wsub, (x, 0, zmin), "Forward", "Up", f"{name} {wlabel} {s}", GUN, kind="gun")
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

def wasp(lite=False):
    name = "Wasp Lite" if lite else "Wasp"
    return frame(name, "MyObjectBuilder_SmallMissileLauncher", "", "Launcher", 4, script_text("WaspFlightAI.cs"), _custom("Wasp", "WASP"),
                 ai=not lite, sensor=not lite, merge=(not lite) or LITE_KEEPS_MERGE)

def lancer(lite=False):
    name = "Lancer Lite" if lite else "Lancer"
    return C.add_support_pbs(frame(name, "MyObjectBuilder_SmallGatlingGun", "SmallBlockAutocannon", "Autocannon", 5, script_text("LancerAI.cs"), _custom("Lancer", "LANCER"),
                 ai=not lite, sensor=not lite, merge=(not lite) or LITE_KEEPS_MERGE))      # [Flight] + [Staff] (+ [Thrust]) here too, so a Lancer printed by a Foundry carries its Staff PB

MODELS = {"Wasp": wasp, "Lancer": lancer}

if __name__ == "__main__":
    sys.exit(C.run_module(MODELS))
