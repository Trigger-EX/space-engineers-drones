"""Core drones: Hornet (2 gatlings), Gnat, Longbow S, Longbow L.

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Scripts are read from scripts/ at call time.
`python3 -I tools/designs/core.py` builds + validates every model and variant and prints one line each.

DOCKING FACES (printers): the clamp face of the small merge block is its local Up (MERGE_CLAMP_FACE_SMALL in _common.py).
  * Gnat   : merge block set FLUSH into the belly (clamp face = Down), inside the 2 x 2 section: the outline is a hard requirement,
             so no latch may stand off the hull (owner, 2026-10-14).  check_docking's flush mode.
  * Hornet : merge block sits on a 2-cell stalk behind the rear camera, between the main thrusters (clamp face = Backward).
             It sticks out 1 cell beyond the thruster tails. Keep the thrusters OFF while merged.
  * Longbows have no merge block (not printed).
  _common.check_docking() asserts the merge block is the only block reaching the outer layer along its clamp axis, so
  nothing else of the drone can touch a flat printer surface.

RAILGUN POWER (docs/design/railgun_power.md): the Longbows carry small reactors that alone cover the railgun recharge and, with the batteries, cover it on a
  full diagonal manoeuvre (C.power_budget); thrusters and a second gyro were added until acceleration (every axis) and turn rates were back at or above the
  original figures (small_rail_power / large_rail_power, small_rail_power also used by extra.mantis).  tools/tests/test_rail_power.py checks it.

LITE variants: only block types whose XML was seen in Starburst plus the merge block (LITE_KEEPS_MERGE in _common.py; its
  shape is from the Keen source, not seen).  Lite drops Sensor, AI Offensive and AI Flight blocks.  Everything else is shared.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403  (helpers + constants)
import _common as C
import spares

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
    bp = hornet_frame(name, "", "MyObjectBuilder_SmallGatlingGun", script_text("HornetDroneAI.cs"),
                      ai=not lite, sensor=not lite, merge=(not lite) or LITE_KEEPS_MERGE)
    return C.add_support_pbs(bp)      # [Flight] + [Staff] (+ [Thrust]) PBs here, so the Hornets clamped into the Wing ships / cradle pods (attached as sub-grids, which build_all does not visit) carry the Staff PB too

# ---------------------------------------------------------------------------------------------
def gnat_frame(script, merge=True, sensor=True, name="Gnat", ai=True, side=None):
    """Point-and-burn drone: the hull is 2 x 2 cells in section (x 0..1, y 0..1) and long; two axial ion thrusters back-to-back, gyro steering,
    AI Offensive + Flight blocks on the tail, merge latch set flush into the belly (inside the section). Only the Left / Right cameras stand off the sides.
    side (experiment, testships 'Gnat Side*'): "add" = one side thruster across the tail (x 1, y 0..1, z 3, exhaust Up = pushes Down), the rear camera
    moves one cell back (hull 8 long, still inside the 2 x 2 section); "norev" = the same, and the reverse thruster becomes armour."""
    import pods
    bp = Blueprint(name, "Small", seed=7, legacy_shapes=True)
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (1, 0, -2), name=f"{name} Remote Control", kind="ctrl", color=RED)
    bat(bp, (1, 0, -1), f"{name} Battery")
    # docking latch: the merge block takes the belly cell under the antenna, clamp face Down, flush with the hull (nothing below the 2 x 2 section)
    if merge: merge_small(bp, (1, 0, 0), "Down", f"{name} Merge", flush=True)
    else: bp.armor(1, 0, 0, RED)
    g = bp.add("MyObjectBuilder_SmallGatlingGun", "", (0, 0, -3), "Forward", "Up", f"{name} Gatling", GUN, kind="gun")
    bp.keep_clear(g, 6, "muzzle")
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (1, 1, -2), "Forward", "Up", f"{name} Gyroscope", kind="gyro")
    bp.armor(1, 1, -1)
    # the two thrusters sit back-to-back on the same axis (column x 0, y 1), their mount ends touching; the cells in front of and behind them stay free
    if side == "norev": bp.armor(0, 1, -3, RED); bp.armor(0, 1, -2, RED)      # the main thruster's mount face sits on this armour
    else: thr(bp, (0, 1, -3), "Forward", "Up", f"{name} Ion Reverse")
    thr(bp, (0, 1, -1), "Backward", "Up", f"{name} Ion Main")
    ant(bp, (1, 1, 0), "Backward", "Up", f"{name} Antenna", 2500)   # lies along the hull (rear half in the tail cell), not standing on the roof
    cam(bp, (1, 0, -3), "Forward", "Up", f"{name} Camera")
    cam(bp, (2, 0, -2), "Right", "Up", f"{name} Camera Right")
    cam(bp, (-1, 0, 0), "Left", "Up", f"{name} Camera Left")
    if ai:
        # tail section: AI blocks in the lower right column, rear camera on the tail end
        bp.add("MyObjectBuilder_OffensiveCombatBlock", "SmallOffensiveCombat", (1, 0, 1), name=f"{name} AI Offensive [AI]", kind="ai")
        bp.add("MyObjectBuilder_FlightMovementBlock", "SmallFlightMovement", (1, 0, 2), name=f"{name} AI Flight [AI]", kind="ai")
    else:       # no AI blocks: the same tail in armour, so both variants share the hull
        bp.armor(1, 0, 1, RED); bp.armor(1, 0, 2, RED)
    if side: thr(bp, (1, 0, 3), "Up", "Backward", f"{name} Ion Side")       # across the tail, both cells inside the section; the flame goes up
    cam(bp, (1, 0, 4 if side else 3), "Backward", "Up", f"{name} Camera Rear")
    # PB mounts only on its front: it sits beside the tail, facing into it (orientation found by the mount rules)
    pods.orient_attached(bp, "MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (0, 0, 1), name=f"{name} PB",
                         extra=pb_extra(script) if script else '', kind="ctrl", color=RED)
    if sensor:
        for cell in ((1, 1, 2), (1, 1, 3), (1, 1, -3)):    # first cell where the sensor seats on a neighbour (360 degree sensor: any spot will do)
            try: pods.orient_attached(bp, "MyObjectBuilder_SensorBlock", "SmallBlockSensor", cell, name=f"{name} Sensor", kind="sensor"); break
            except AssertionError: pass
        else: raise AssertionError(f"{name}: no cell for the sensor")
    return finalize(bp)

def gnat(lite=False, name=None, side=None):
    name = name or ("Gnat Lite" if lite else "Gnat")
    bp = gnat_frame(script_text("GnatSwarmAI.cs"), merge=(not lite) or LITE_KEEPS_MERGE, sensor=not lite, name=name, ai=not lite, side=side)
    # two PBs (docs/design/gnat_split.md): the frame's PB is the Flight half (GnatSwarmAI), the Staff half (FLEET LINK + LAUNCH, GnatStaffAI) sits on the tail.  No [Thrust] PB: point-and-burn.
    # In gnat(), not gnat_frame: the Stinger fallback shares the frame.  Placed by hand on the tail (armour seat, then the PB): the generic spare search would pick a cell off the 2 x 2 hull.
    import pods
    for b in bp.blocks:
        if b['xtype'] == spares.PB_X: b['name'] = f"[Flight] {name} PB"
    zs = 3
    bp.armor(0, 0, 2, RED)
    pods.orient_attached(bp, spares.PB_X, "SmallProgrammableBlock", (0, 0, zs), name=f"[Staff] {name} PB", kind="ctrl", color=RED, extra=pb_extra(script_text("GnatStaffAI.cs")))
    bp.validate()
    return bp

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
        # second thruster of every pair: acceleration doubled (6.3 -> about 11 m/s^2), so it can run from, or swing round, a ship
        # charging it at 100 m/s (a Tech Hunter rush caught the old one before it could build any speed)
        thr(bp, (-1, -2, 1), "Backward", "Up", f"{tag} Ion Main L2"); thr(bp, (1, -2, 1), "Backward", "Up", f"{tag} Ion Main R2")
        thr(bp, (-2, -2, -3), "Forward", "Up", f"{tag} Ion Brake L2"); thr(bp, (2, -2, -3), "Forward", "Up", f"{tag} Ion Brake R2")
        thr(bp, (-3, -3, -1), "Left", "Up", f"{tag} Ion Strafe R3"); thr(bp, (-3, -3, 0), "Left", "Up", f"{tag} Ion Strafe R4")
        thr(bp, (2, -3, -1), "Right", "Up", f"{tag} Ion Strafe L3"); thr(bp, (2, -3, 0), "Right", "Up", f"{tag} Ion Strafe L4")
        thr(bp, (0, -4, -2), "Down", "Forward", f"{tag} Ion Lift M")
        thr(bp, (-2, 0, -1), "Up", "Backward", f"{tag} Ion Dive L2"); thr(bp, (2, 0, -1), "Up", "Backward", f"{tag} Ion Dive R2")
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
    if L: large_rail_power(bp, tag)
    else: small_rail_power(bp, tag, mains=(((-1, -2, 1), "L2"), ((1, -2, 1), "R2")))
    return finalize(bp)

# ---------------------------------------------------------------------------------------------
# power for the railgun recharge (docs/design/railgun_power.md): reactors that alone cover the capacitor's draw and, with the batteries, cover it on a full
# diagonal manoeuvre (C.power_budget); thrusters / gyros that pay for the extra mass (acceleration on every axis and turn rates not below the original ones)
SMALL_RAIL_REACTORS = [(x, y, z) for z in (-2, -1) for y in (-1, -2) for x in (-1, 1)]     # 8 hull armour cells beside the gyro, on the centre of mass

def small_rail_power(bp, tag, mains=()):
    """Longbow S / Mantis spine (railgun on the centre line, hull x -1..1, y -2..-1, z -3..0, 1 MWh battery under it).
    8 SmallBlockSmallGenerator (4 MW >= the 3.6 MW recharge) in place of hull armour: a reactor mounts on all six faces like the armour it replaces.
    The reactors' 2.2 t cost turn rate, so Gyroscope 2 replaces the armour behind the first gyro (it stands on the battery); with it every axis needs a
    second thruster pair beside the old one (the Longbow L pattern; one more per axis is not enough for the gyro's 614 kg) + `mains` ((cell, label) of the
    extra main engines, which differ: the Mantis has 4 already)."""
    for i, c in enumerate(SMALL_RAIL_REACTORS):
        C.swap_armour(bp, c)
        C.reactor(bp, c, f"{tag} Reactor {i + 1}")
    C.swap_armour(bp, (0, -2, 0))
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (0, -2, 0), "Forward", "Up", f"{tag} Gyroscope 2", kind="gyro")
    for c, s in mains: thr(bp, c, "Backward", "Up", f"{tag} Ion Main {s}")
    thr(bp, (-2, -2, -3), "Forward", "Up", f"{tag} Ion Brake L2"); thr(bp, (2, -2, -3), "Forward", "Up", f"{tag} Ion Brake R2")
    thr(bp, (-3, -3, -1), "Left", "Up", f"{tag} Ion Strafe R3"); thr(bp, (-3, -3, 0), "Left", "Up", f"{tag} Ion Strafe R4")
    thr(bp, (2, -3, -1), "Right", "Up", f"{tag} Ion Strafe L3"); thr(bp, (2, -3, 0), "Right", "Up", f"{tag} Ion Strafe L4")
    thr(bp, (-2, -5, -1), "Down", "Forward", f"{tag} Ion Lift L2"); thr(bp, (2, -5, -1), "Down", "Forward", f"{tag} Ion Lift R2")
    thr(bp, (-2, 0, -1), "Up", "Backward", f"{tag} Ion Dive L2"); thr(bp, (2, 0, -1), "Up", "Backward", f"{tag} Ion Dive R2")
    return bp

LARGE_RAIL_REACTORS = [(-1, -2, -1), (1, -2, -1), (0, -2, -1), (-1, -2, 0), (1, -2, 0)]    # bottom layer of the hull behind the batteries
LARGE_RAIL_THRUSTERS = [   # (cell, exhaust, up, label): three more per direction (four to seven; lift three to six)
    ((-1, -3, 1), "Backward", "Up", "Main L3"), ((1, -3, 1), "Backward", "Up", "Main R3"), ((-2, -2, 1), "Backward", "Up", "Main L4"),
    ((-2, -3, -3), "Forward", "Up", "Brake L3"), ((2, -3, -3), "Forward", "Up", "Brake R3"), ((-3, -2, -3), "Forward", "Up", "Brake L4"),
    ((0, -4, 0), "Down", "Forward", "Lift M2"), ((-1, -4, 0), "Down", "Forward", "Lift L2"), ((1, -4, 0), "Down", "Forward", "Lift R2"),
    ((-1, 0, -1), "Up", "Backward", "Dive L3"), ((1, 0, -1), "Up", "Backward", "Dive R3"), ((-1, 0, 0), "Up", "Backward", "Dive L4"),
    ((-3, -1, -1), "Left", "Up", "Strafe R5"), ((-3, -4, -1), "Left", "Up", "Strafe R6"), ((-3, -4, 0), "Left", "Up", "Strafe R7"),
    ((2, -1, -1), "Right", "Up", "Strafe L5"), ((2, -4, -1), "Right", "Up", "Strafe L6"), ((2, -4, 0), "Right", "Up", "Strafe L7"),
]

def large_rail_power(bp, tag):
    """Longbow L (and its Seeker).  5 LargeBlockSmallGenerator (75 MW; 3 would cover the 38 MW recharge, the other 2 the thrusters added below) in place of
    hull armour at y -2, z -1..0.  Gyroscope 2 replaces the armour above the port reactor (it stands on it): without it pitch / yaw / roll fell to 68-76 %.
    Three more ion thrusters per direction (LARGE_RAIL_THRUSTERS): with the gyro the ship is 1.7 x heavier, so each 4-thruster group needs 7 and the lifts 6.
    Found by trying 2 / 3 / 4 more per direction x 3..6 reactors x 0 / 1 gyro and keeping the lightest that meets every rule (docs/design/railgun_power.md)."""
    for i, c in enumerate(LARGE_RAIL_REACTORS):
        C.swap_armour(bp, c)
        C.reactor(bp, c, f"{tag} Reactor {i + 1}")
    C.swap_armour(bp, (-1, -1, -1))
    bp.add("MyObjectBuilder_Gyro", "LargeBlockGyro", (-1, -1, -1), "Forward", "Up", f"{tag} Gyroscope 2", kind="gyro")
    for c, ex, up, s in LARGE_RAIL_THRUSTERS: thr(bp, c, ex, up, f"{tag} Ion {s}")
    return bp

def longbow_s(lite=False): return C.add_support_pbs(longbow_frame("Small", script_text("LongbowSniperAI.cs"), lite))      # [Flight] + [Staff] (+ [Thrust]) here too, so a Longbow S printed by a Foundry carries its Staff PB
def longbow_l(lite=False): return longbow_frame("Large", script_text("LongbowSniperAI.cs"), lite)

def longbow_l_seeker(lite=False):
    """Option-4 test ship: Longbow L + artillery turret named [Seeker] on its back (large grid, paste only)."""
    return longbow_frame("Large", script_text("LongbowSniperAI.cs"), lite, seeker=True)

MODELS = {"Hornet": hornet, "Gnat": gnat, "Longbow S": longbow_s, "Longbow L": longbow_l, "Longbow L Seeker": longbow_l_seeker}

# The Gnat is a point-and-burn drone (gyro steering, two axial thrusters): only Forward/Backward thrust is required.
REQUIRED_AXES = {"Gnat": ("Forward", "Backward")}

if __name__ == "__main__":
    sys.exit(C.run_module(MODELS, required_axes=REQUIRED_AXES))
