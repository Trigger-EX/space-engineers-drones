"""Support drones: Watcher (unarmed scout/spotter) and Midge (scriptless AI-block gatling drone).

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  `python3 -I tools/designs/support.py` builds,
validates and prints one line per model and variant.  Scripts are read from scripts/ at call time.

WATCHER  (decision: NO fork, it reuses scripts/HornetDroneAI.cs)
  HornetDroneAI.cs already runs unarmed: with no guns it still tracks with the nose cameras, shares contacts over IGC and keeps a
  standoff distance (see "no guns = scout" in the script).  The Watcher is configured purely through the PB custom data (ini section
  [Hornet]): IGC Tag HORNET (so it feeds the Hornet hive), Start Mode HUNT, long standoff, long scan.  Full = + sensor.
  FLEET (docs/hive-protocol.md): the [Fleet] section makes it ALSO broadcast every contact on the all-types fleet channel
  (Extra Broadcast Tags=FLEET), which every drone script (Hornet, Gnat, Longbow, Wasp, Lancer, Harrier, Aegis, Mantis, Basilisk,
  Stinger) listens to.  Set [Fleet] Secret=<word> on all your PBs in multiplayer (IGC reaches every PB in antenna range).
  Lite = no sensor.  Both keep the merge latch (docking face Backward, as the Hornet).
MIDGE    (scriptless; NO programmable block)
  Full  = EXPERIMENTAL: Small gatling + AI Offensive + AI Flight + AI Defensive, 6-axis ion thrusters, 2 gyros, RC, merge latch.
          The AI block XML shapes are GUESSED (docs/xml-notes.md), the blocks carry no ComponentContainer so the game builds the default
          components; the user has to link/configure them by hand, see scripts/MidgeNotes.md.
  Lite  = DOCUMENTED FALLBACK, not a scriptless design: it is the Gnat Lite frame (gatling, scripted PB, no sensor, no AI blocks) under
          the name "Midge Lite".  It runs GnatSwarmAI.cs.
  No Event Controller / Timer in the Midge: their toolbar entries must reference block ids, which the pasted/welded blocks do not
  have in advance, and the EC shape for toolbar actions was never seen.  See MidgeNotes.md for what a Midge Nest printer must do.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C
from core import gnat_frame

# ---------------------------------------------------------------------------------------------
WATCHER_INI = """[Hornet]
IGC Tag={tag}
Start Mode (GUARD/HUNT/ASSIST/IDLE)=HUNT
Max Fire Range=1500
Standoff Range=900
Min Range=600
Combat Speed=80
Cruise Speed=60
Orbit Speed=30
Evasive Jinking=true
Jink Speed=15
Auto Camera Scan=true
Scan Range=2500
Scan Cone Degrees=45
All-around Close Scan=true
Close Scan Range=200
Share Targets Over IGC=true
Friendly Fire Check=false

[Fleet]
Channel=FLEET
Secret=
Listen Tags=
Extra Broadcast Tags=FLEET
Accept Commands=true
Accept Home=true
"""

def watcher_frame(name, sensor, tag="HORNET", ai=False):
    """Hornet-class 6-axis frame without weapons: 6 nose cameras, 5000 m antenna, optional sensor, rear merge latch."""
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    HULL = (0.58, -0.78, -0.25); ACC = (0.07, 0.55, 0.10)
    for x in (-1, 0, 1):
        for y in (0, 1): bp.armor(x, y, -3, ACC)
    for i, (x, y) in enumerate(((-1, 0), (1, 0), (-1, 1), (1, 1))):
        bat(bp, (x, y, -2), f"{name} Battery {i+1}")
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (0, 0, -2), name=f"{name} Remote Control", kind="ctrl")
    bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (0, 1, -2), "Forward", "Up", f"{name} PB",
           extra=pb_extra(script_text("HornetDroneAI.cs")), kind="ctrl", custom_data=WATCHER_INI.format(tag=tag))
    for x in (-1, 0, 1): bp.armor(x, 0, -1)
    bp.armor(-1, 1, -1); bp.armor(1, 1, -1)
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (0, 1, -1), "Forward", "Up", f"{name} Gyroscope", kind="gyro")
    for x in (-1, 0, 1):
        for y in (0, 1): bp.armor(x, y, 0)
    # nose camera bank (6 x 3-cell view): more rays charging at once for long-range tracking
    for yi, y in enumerate((0, 1)):
        for x in (-1, 0, 1):
            cam(bp, (x, y, -4), "Forward", "Up", f"{name} Camera {'Lower' if y == 0 else 'Upper'} {x+2}", 3)
    thr(bp, (-1, 0, 1), "Backward", "Up", f"{name} Ion Main L"); thr(bp, (1, 0, 1), "Backward", "Up", f"{name} Ion Main R")
    thr(bp, (-2, 1, -2), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (2, 1, -2), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, 0, 0), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, 1, 0), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, 0, 0), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, 1, 0), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-1, -2, -1), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (1, -2, -1), "Down", "Forward", f"{name} Ion Lift R")
    thr(bp, (-1, 2, -1), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (1, 2, -1), "Up", "Backward", f"{name} Ion Dive R")
    ant(bp, (0, 2, 0), "Up", "Backward", f"{name} Antenna", 5000)
    cam(bp, (0, 2, -3), "Up", "Backward", f"{name} Camera Up")
    cam(bp, (0, -1, -3), "Down", "Forward", f"{name} Camera Down")
    cam(bp, (0, 1, 1), "Backward", "Up", f"{name} Camera Rear")
    bp.armor(-1, -1, -2, HULL); bp.armor(1, -1, -2, HULL)
    cam(bp, (-2, -1, -2), "Left", "Up", f"{name} Camera Left")
    cam(bp, (2, -1, -2), "Right", "Up", f"{name} Camera Right")
    bp.armor(0, 0, 1, RED); bp.armor(0, 0, 2, RED)
    merge_small(bp, (0, 0, 3), "Backward", f"{name} Merge")
    if sensor:
        bp.add("MyObjectBuilder_SensorBlock", "SmallBlockSensor", (0, -1, -2), "Down", "Forward", f"{name} Sensor", kind="sensor")
    if ai:
        bp.add("MyObjectBuilder_OffensiveCombatBlock", "SmallOffensiveCombat", (0, -1, -1), name=f"{name} AI Offensive [AI]", kind="ai")
        bp.add("MyObjectBuilder_FlightMovementBlock", "SmallFlightMovement", (0, -1, 0), name=f"{name} AI Flight [AI]", kind="ai")
    return finalize(bp)

def watcher(lite=False):
    import core
    name = "Watcher Lite" if lite else "Watcher"
    return core.hornet_survival(watcher_frame(name, sensor=not lite, ai=not lite), name)      # Gyroscope 2, [Flight] + [Staff] (+ [Thrust]) here too (a Watcher printed by a Pad carries its Staff PB), survival kit

# ---------------------------------------------------------------------------------------------
def midge_frame(name):
    """Scriptless gatling drone. Core 3 wide x 2 high x 3 deep (z -1..1) behind a centred gatling; AI blocks buried inside."""
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    ACC = (0.07, 0.55, 0.10)
    g = bp.add("MyObjectBuilder_SmallGatlingGun", "", (0, 0, -5), "Forward", "Up", f"{name} Gatling", GUN, kind="gun")
    bp.keep_clear(g, 6, "muzzle")
    for x in (-1, 0, 1):                    # z=-1: nose armour (gatling mounts on its centre cell)
        for y in (0, 1): bp.armor(x, y, -1, ACC)
    for i, x in enumerate((-1, 0, 1)):      # z=0 lower row: batteries
        bat(bp, (x, 0, 0), f"{name} Battery {i+1}")
    bp.add("MyObjectBuilder_OffensiveCombatBlock", "SmallOffensiveCombat", (-1, 1, 0), name=f"{name} AI Offensive", color=PAL["ai"], kind="ai")
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (0, 1, 0), name=f"{name} Remote Control", kind="ctrl")
    bp.add("MyObjectBuilder_FlightMovementBlock", "SmallFlightMovement", (1, 1, 0), name=f"{name} AI Flight", color=PAL["ai"], kind="ai")
    # z=1: AI Defensive + armour; the two gyros stand on the cells below them (gyro mounts on its bottom only)
    bp.add("MyObjectBuilder_DefensiveCombatBlock", "SmallDefensiveCombat", (-1, 0, 1), name=f"{name} AI Defensive", color=PAL["ai"], kind="ai")
    bp.armor(0, 0, 1, RED); bp.armor(1, 0, 1)
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (-1, 1, 1), "Forward", "Up", f"{name} Gyroscope 1", kind="gyro")
    bp.armor(0, 1, 1)
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (1, 1, 1), "Forward", "Up", f"{name} Gyroscope 2", kind="gyro")
    # six axes of ion thrust (exhaust = block Forward)
    thr(bp, (-1, 0, 2), "Backward", "Up", f"{name} Ion Main L"); thr(bp, (1, 0, 2), "Backward", "Up", f"{name} Ion Main R")
    thr(bp, (-2, 1, -2), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (2, 1, -2), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, 0, 0), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, 1, 0), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, 0, 0), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, 1, 0), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-1, -2, -1), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (1, -2, -1), "Down", "Forward", f"{name} Ion Lift R")
    thr(bp, (-1, 2, -1), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (1, 2, -1), "Up", "Backward", f"{name} Ion Dive R")
    ant(bp, (0, 2, 0), "Up", "Backward", f"{name} Antenna", 2500)
    # docking latch, same layout as Hornet: 2-cell stalk between the main thrusters, clamp face Backward
    bp.armor(0, 0, 2, RED); bp.armor(0, 0, 3, RED)
    merge_small(bp, (0, 0, 4), "Backward", f"{name} Merge")
    return finalize(bp)

def midge(lite=False):
    if lite:
        # FALLBACK: the Gnat Lite frame (scripted), NOT scriptless. See scripts/MidgeNotes.md.
        return gnat_frame(script_text("GnatSwarmAI.cs"), merge=LITE_KEEPS_MERGE, sensor=False, name="Midge Lite")
    # survivability item 11: decoys only (scriptless: no flare control, no repair client), inside the bounding box, far from the core
    return C.add_survival_kit(midge_frame("Midge"), decoys=2, flares=0, projector=False, panels=False, model="Midge")

MODELS = {"Watcher": watcher, "Midge": midge}

def main():
    """like _common.run_module, but Midge Lite (a Gnat) only needs forward/backward thrust"""
    bad = 0
    for name, fn in MODELS.items():
        for lite in (False, True):
            label = f'{name}{" Lite" if lite else ""}'
            axes = ("Forward", "Backward") if (name == "Midge" and lite) else None
            try: bp = fn(lite)
            except AssertionError as e:
                print(f'FAIL {label}: {e}'); bad += 1; continue
            ok, t = C.report_line(label, bp, 1.0, axes)
            print(t + (' [EXPERIMENTAL: AI block XML guessed]' if (name == "Midge" and not lite) else '')
                  + (' [FALLBACK: Gnat Lite frame + GnatSwarmAI.cs]' if (name == "Midge" and lite) else ''))
            bad += (not ok)
    return 1 if bad else 0

if __name__ == "__main__":
    sys.exit(main())
