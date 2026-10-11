"""All drone designs, built and validated against real block definitions."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from sebp import Blueprint

DARK = (0.0, -0.81, -0.55)
GUN = (0.0, -0.81, -0.45)
PAL = {"armor": (92,100,112), "cam": (36,36,42), "bat": (70,120,90), "ctrl": (90,110,170), "gyro": (150,150,60),
       "gun": (50,52,58), "thr": (62,66,74), "ant": (185,185,195), "sensor": (120,80,150), "ai": (60,150,170),
       "merge": (210,140,40), "warhead": (190,50,40), "rail": (52,54,60), "accent": (225,130,40)}

def pb_extra(script):
    from xml.sax.saxutils import escape
    return f"<Program>{escape(script)}</Program><Storage /><DefaultRunArgument xsi:nil=\"true\" />"

BAT_S = ("<CurrentStoredPower>0.05</CurrentStoredPower><ProducerEnabled>true</ProducerEnabled><MaxStoredPower>0.05</MaxStoredPower>"
         "<SemiautoEnabled>false</SemiautoEnabled><OnlyDischargeEnabled>false</OnlyDischargeEnabled><ChargeMode>0</ChargeMode>")
def ant_extra(r): return f"<BroadcastRadius>{r}</BroadcastRadius><ShowShipName>true</ShowShipName><EnableBroadcasting>true</EnableBroadcasting>"

def thr(bp, mn, fwd, up, name, sub=None):
    b = bp.add("MyObjectBuilder_Thrust", sub or ("SmallBlockSmallThrust" if bp.grid == "Small" else "LargeBlockSmallThrust"),
               mn, fwd, up, name, DARK, kind="thr", mass=121 if bp.grid == "Small" else 4380)
    bp.keep_clear(b, 4, "exhaust")
    return b

def cam(bp, mn, fwd, up, name, view=3):
    b = bp.add("MyObjectBuilder_CameraBlock", "SmallCameraBlock" if bp.grid == "Small" else "LargeCameraBlock", mn, fwd, up, name, DARK, kind="cam")
    bp.keep_clear(b, view, "camera view")
    return b

# ---------------------------------------------------------------------------------------------
def hornet_frame(name, weapon_sub, weapon_type, script, custom_data=None, ai=True, sensor=True, title=None):
    """Hornet-class 6-axis small drone: 3x2x4 core, two fixed forward weapons, 12 ion thrusters."""
    bp = Blueprint(name, "Small", seed=hash(name) & 0xffff)
    HULL = (0.58, -0.78, -0.25); ACC = (0.07, 0.55, 0.10)
    # core: x -1..1, y 0..1, z -3..0
    for x in (-1, 0, 1):
        for y in (0, 1): bp.armor(x, y, -3, ACC)
    bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockSmallBatteryBlock", (-1, 0, -2), name=f"{name} Battery 1", extra=BAT_S, kind="bat", mass=146)
    bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockSmallBatteryBlock", (1, 0, -2), name=f"{name} Battery 2", extra=BAT_S, kind="bat", mass=146)
    bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockSmallBatteryBlock", (-1, 1, -2), name=f"{name} Battery 3", extra=BAT_S, kind="bat", mass=146)
    bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockSmallBatteryBlock", (1, 1, -2), name=f"{name} Battery 4", extra=BAT_S, kind="bat", mass=146)
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (0, 0, -2), name=f"{name} Remote Control", kind="ctrl", mass=38)
    # PB mounts only on its front face: face it into the nose armour
    bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (0, 1, -2), "Forward", "Up", f"{name} PB",
           extra=pb_extra(script), kind="ctrl", mass=138, custom_data=custom_data)
    for x in (-1, 0, 1): bp.armor(x, 0, -1)
    bp.armor(-1, 1, -1); bp.armor(1, 1, -1)
    # gyro mounts only on its bottom: sit it on the armour below
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (0, 1, -1), "Forward", "Up", f"{name} Gyroscope", kind="gyro", mass=614)
    for x in (-1, 0, 1):
        for y in (0, 1): bp.armor(x, y, 0)
    # nose cameras: backs on the nose armour
    cam(bp, (0, 0, -4), "Forward", "Up", f"{name} Camera Lower", 3)
    cam(bp, (0, 1, -4), "Forward", "Up", f"{name} Camera Upper", 3)
    # weapons on the flanks (back face on the side thrusters, side mounts on the core)
    for x, s in ((-2, "L"), (2, "R")):
        g = bp.add(weapon_type, weapon_sub, (x, 0, -4), "Forward", "Up", f"{name} {('Gatling' if weapon_type.endswith('GatlingGun') else 'Launcher')} {s}", GUN, kind="gun", mass=148)
        bp.keep_clear(g, 6, "muzzle")
    thr(bp, (-1, 0, 1), "Backward", "Up", f"{name} Ion Main L"); thr(bp, (1, 0, 1), "Backward", "Up", f"{name} Ion Main R")
    thr(bp, (-2, 1, -2), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (2, 1, -2), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, 0, 0), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, 1, 0), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, 0, 0), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, 1, 0), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-1, -2, -1), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (1, -2, -1), "Down", "Forward", f"{name} Ion Lift R")
    thr(bp, (-1, 2, -1), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (1, 2, -1), "Up", "Backward", f"{name} Ion Dive R")
    # antenna standing on the rear deck
    bp.add("MyObjectBuilder_RadioAntenna", "SmallBlockRadioAntenna", (0, 2, 0), "Up", "Backward", f"{name} Antenna", extra=ant_extra(5000), kind="ant", mass=72)
    # all-around cameras
    cam(bp, (0, 2, -3), "Up", "Backward", f"{name} Camera Up")
    cam(bp, (0, -1, -3), "Down", "Forward", f"{name} Camera Down")
    cam(bp, (0, 0, 1), "Backward", "Up", f"{name} Camera Rear")
    bp.armor(-1, -1, -2, HULL); bp.armor(1, -1, -2, HULL)          # outriggers under the core for the side cameras
    cam(bp, (-2, -1, -2), "Left", "Up", f"{name} Camera Left")
    cam(bp, (2, -1, -2), "Right", "Up", f"{name} Camera Right")
    if sensor:
        bp.add("MyObjectBuilder_SensorBlock", "SmallBlockSensor", (0, -1, -2), "Down", "Forward", f"{name} Sensor", kind="sensor")
    if ai:
        bp.add("MyObjectBuilder_OffensiveCombatBlock", "SmallOffensiveCombat", (0, -1, -1), name=f"{name} AI Offensive [AI]", kind="ai", mass=156)
        bp.add("MyObjectBuilder_FlightMovementBlock", "SmallFlightMovement", (0, -1, 0), name=f"{name} AI Flight [AI]", kind="ai", mass=156)
    return bp

# ---------------------------------------------------------------------------------------------
def gnat(script, merge=True, sensor=True, name="Gnat"):
    """Minimal point-and-burn drone: two axial ion thrusters back-to-back, gyro steering."""
    bp = Blueprint(name, "Small", seed=7)
    RED = (0.98, 0.65, 0.05)
    # spine (x=1, y=0): camera | remote control | battery | armour | rear camera
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (1, 0, -2), name=f"{name} Remote Control", kind="ctrl", color=RED, mass=38)
    bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockSmallBatteryBlock", (1, 0, -1), name=f"{name} Battery", extra=BAT_S, kind="bat", mass=146)
    bp.armor(1, 0, 0, RED)
    g = bp.add("MyObjectBuilder_SmallGatlingGun", "", (0, 0, -3), "Forward", "Up", f"{name} Gatling", GUN, kind="gun", mass=148)
    bp.keep_clear(g, 6, "muzzle")
    # PB mounts only on its front: face it into the battery from the side
    bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (2, 0, -1), "Left", "Up", f"{name} PB", extra=pb_extra(script), kind="ctrl", color=RED, mass=138)
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (1, 1, -2), "Forward", "Up", f"{name} Gyroscope", kind="gyro", mass=614)
    bp.armor(1, 1, -1)
    # the two thrusters sit back-to-back on the same axis, their mount ends touching
    thr(bp, (0, 1, -3), "Forward", "Up", f"{name} Ion Reverse")
    thr(bp, (0, 1, -1), "Backward", "Up", f"{name} Ion Main")
    bp.add("MyObjectBuilder_RadioAntenna", "SmallBlockRadioAntenna", (1, 1, 0), "Up", "Backward", f"{name} Antenna", extra=ant_extra(2500), kind="ant", mass=72)
    cam(bp, (1, 0, -3), "Forward", "Up", f"{name} Camera")
    cam(bp, (2, 0, -2), "Right", "Up", f"{name} Camera Right")
    cam(bp, (1, -1, -2), "Down", "Forward", f"{name} Camera Down")
    cam(bp, (1, 0, 1), "Backward", "Up", f"{name} Camera Rear")
    cam(bp, (-1, 0, 0), "Left", "Up", f"{name} Camera Left")
    cam(bp, (1, 2, -1), "Up", "Backward", f"{name} Camera Up")
    if sensor:
        bp.add("MyObjectBuilder_SensorBlock", "SmallBlockSensor", (2, 0, 0), "Right", "Up", f"{name} Sensor", kind="sensor")
    if merge:
        # print-bay latch on the belly, merge face (local Right) pointing down
        bp.add("MyObjectBuilder_MergeBlock", "SmallShipSmallMergeBlock", (1, -1, 0), "Forward", "Right", f"{name} Merge", RED, kind="merge", mass=80)
    return bp

# ---------------------------------------------------------------------------------------------
def longbow(grid, script):
    L = grid == "Large"
    tag = "Longbow L" if L else "Longbow S"
    bp = Blueprint(tag, grid, seed=11 if L else 12)
    HULL = (0.24, -0.55, -0.35); ACC = (0.12, 0.55, 0.15)
    sub = lambda s, l: l if L else s
    rail = bp.add("MyObjectBuilder_SmallMissileLauncherReload", sub("SmallRailgun", "LargeRailgun"), (0, 0, -8), "Forward", "Up", f"{tag} Railgun", DARK, kind="rail", mass=14470 if L else 1364)
    bp.keep_clear(rail, 8, "muzzle")
    # nose camera bank: backs on row z=-3
    for y in (-1, -2):
        for x in (-1, 0, 1):
            cam(bp, (x, y, -4), "Forward", "Up", f"{tag} Camera {'U' if y == -1 else 'L'}{x+2}", 3)
    bp.add("MyObjectBuilder_RemoteControl", sub("SmallBlockRemoteControl", "LargeBlockRemoteControl"), (0, -1, -3), name=f"{tag} Remote Control", kind="ctrl", mass=700 if L else 38)
    for x in (-1, 1): bp.armor(x, -1, -3, ACC); bp.armor(x, -2, -3, ACC)
    bp.armor(0, -2, -3, ACC)
    # small PB mounts on its front, the large one only on its bottom
    bp.add("MyObjectBuilder_MyProgrammableBlock", sub("SmallProgrammableBlock", "LargeProgrammableBlock"), (0, -2, -2), "Forward", "Down" if L else "Up",
           f"{tag} PB", extra=pb_extra(script), kind="ctrl", mass=700 if L else 138)
    bp.armor(0, -1, -2)
    # gyro mounts only on its bottom: stand it on the armour behind
    bp.add("MyObjectBuilder_Gyro", sub("SmallBlockGyro", "LargeBlockGyro"), (0, -1, -1), "Forward", "Up", f"{tag} Gyroscope", kind="gyro", mass=12817 if L else 614)
    if L:
        bat = ("<CurrentStoredPower>3</CurrentStoredPower><ProducerEnabled>true</ProducerEnabled><MaxStoredPower>3</MaxStoredPower>"
               "<SemiautoEnabled>false</SemiautoEnabled><OnlyDischargeEnabled>false</OnlyDischargeEnabled><ChargeMode>0</ChargeMode>")
        for i, (x, y) in enumerate([(-1, -1), (1, -1), (-1, -2), (1, -2)]):
            bp.add("MyObjectBuilder_BatteryBlock", "LargeBlockBatteryBlock", (x, y, -2), name=f"{tag} Battery {i+1}", extra=bat, kind="bat", mass=3845)
    else:
        for x in (-1, 1): bp.armor(x, -1, -2); bp.armor(x, -2, -2)
        bat = ("<CurrentStoredPower>1</CurrentStoredPower><ProducerEnabled>true</ProducerEnabled><MaxStoredPower>1</MaxStoredPower>"
               "<SemiautoEnabled>false</SemiautoEnabled><OnlyDischargeEnabled>false</OnlyDischargeEnabled><ChargeMode>0</ChargeMode>")
        bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockBatteryBlock", (-1, -4, -1), "Forward", "Up", f"{tag} Battery", extra=bat, kind="bat", mass=1040)
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
        bp.add("MyObjectBuilder_RadioAntenna", "LargeBlockRadioAntenna", (0, 0, 0), "Forward", "Up", f"{tag} Antenna", extra=ant_extra(10000), kind="ant", mass=3400)
    else:
        bp.add("MyObjectBuilder_RadioAntenna", "SmallBlockRadioAntenna", (0, 0, 0), "Up", "Backward", f"{tag} Antenna", extra=ant_extra(5000), kind="ant", mass=72)
    cam(bp, (0, -1, 1), "Backward", "Up", f"{tag} Camera Rear")
    cam(bp, (0, -3, -3), "Down", "Forward", f"{tag} Camera Down")
    cam(bp, (0, 2, -1), "Up", "Backward", f"{tag} Camera Up")
    cam(bp, (-2, -1, 0), "Left", "Up", f"{tag} Camera Left")
    cam(bp, (2, -1, 0), "Right", "Up", f"{tag} Camera Right")
    bp.add("MyObjectBuilder_SensorBlock", sub("SmallBlockSensor", "LargeBlockSensor"), (-1, -3, -3), "Down", "Forward", f"{tag} Sensor", kind="sensor")
    bp.add("MyObjectBuilder_OffensiveCombatBlock", sub("SmallOffensiveCombat", "LargeOffensiveCombat"), (1, -3, -3), name=f"{tag} AI Offensive [AI]", kind="ai")
    bp.add("MyObjectBuilder_FlightMovementBlock", sub("SmallFlightMovement", "LargeFlightMovement"), (1, -3, -1) if L else (1, -3, -2), name=f"{tag} AI Flight [AI]", kind="ai")
    return bp
