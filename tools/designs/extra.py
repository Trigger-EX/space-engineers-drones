"""Extra drone models: Harrier, Aegis, Mantis (small grid) and Basilisk (LARGE grid).

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Scripts are read from scripts/ at call time.
`python3 -I tools/designs/extra.py` builds + validates every model and variant and prints one line each.

ROLE GAPS (what the sim should test with them; see docs in each docstring):
  Harrier   fast gatling interceptor.  Hornet frame, FOUR main engines (forward accel ~2x Hornet), brakes, no spare weight.
            Closes to 250 m, prefers small / fast contacts (drones, rockets, kamikazes), big turn rate.  Counter to Gnat swarms,
            Stingers, Midges; loses to a Longbow that sits at 1 km and to anything with a big armour pool.
  Aegis     escort / point defence.  FOUR gatlings (2 stacked pairs, ~2x Hornet DPS) + two Decoy blocks (draws turret / missile
            targeting) + heavy armour plates.  Sits on the carrier -> threat line, shoots incoming small objects first (Stingers,
            rockets, drones), shares cues on the shared "DRONE_CUE" channel.  Slower than a Hornet (more mass).
  Mantis    hybrid mid-range.  ONE small railgun + ONE gatling on the same spine; the engage range is capped near 650 m so the
            gatling is always in play, railgun alpha-strikes (1000 m/s, 8000 damage budget) every ~5 s while the gatling
            sprays between shots.  Between Hornet (all DPS, short) and Longbow S (all alpha, long).  8 small reactors + the battery keep the
            railgun recharging on a full diagonal manoeuvre (27 ion thrusters, 2 gyros; docs/design/railgun_power.md).
  Basilisk  LARGE-grid armoured gunship (115 t).  Four LargeMissileLauncher (120 rpm each, 500 dmg, 4 m radius) behind a wall of six
            LargeHeavyBlockArmorBlock, 12 large ion thrusters (2 per axis, ~6 m/s^2), 3 large batteries.  Full variant adds a
            LargeGatlingTurret (autonomous point defence, also a designator) + Sensor + AI blocks.  Tests: a heavy brawler against
            swarms (rockets have blast radius, the turret covers the roof) and against Longbow L (it must close to ~600 m to hurt it).

LITE variants contain only block types whose XML shape was seen in Starburst plus the small merge block where the model has one:
  no Sensor, no AI Offensive / AI Flight, no Decoy (Aegis Lite uses heavy armour plates instead), no LargeGatlingTurret.
All models: antenna ON, batteries pre-charged, unique "<Model> ..." names, PB with the script embedded and the ini pre-set.

Frame notes (small drones) follow core.py: x = Right, y = Up, z = Backward (nose at -z); cells are 0.5 m; thrusters are 1x1x2 with
Forward = exhaust direction (the ship is pushed the opposite way).  The merge block clamps along its local Up (see _common.py).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403  (helpers + constants)
import _common as C
import spares

HULL = (0.58, -0.78, -0.25); ACC = (0.07, 0.55, 0.10)

def _custom(section, tag, mode="IDLE", extra=""):
    return f"[{section}]\nIGC Tag={tag}\nStart Mode (GUARD/HUNT/ASSIST/IDLE)={mode}\n{extra}"

def _pb(bp, mn, name, script, custom, fwd="Forward", up="Up", large=False):
    return bp.add("MyObjectBuilder_MyProgrammableBlock", "LargeProgrammableBlock" if large else "SmallProgrammableBlock", mn, fwd, up,
                  name, extra=pb_extra(script) if script else '', kind="ctrl", custom_data=custom)

def _core(bp, name, script, custom):
    """The Hornet core: x -1..1, y 0..1, z -3..0.  Batteries, RC, PB, gyro, nose cameras, armour."""
    for x in (-1, 0, 1):
        for y in (0, 1): bp.armor(x, y, -3, ACC)
    for i, (x, y) in enumerate(((-1, 0), (1, 0), (-1, 1), (1, 1))):
        bat(bp, (x, y, -2), f"{name} Battery {i+1}")
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (0, 0, -2), name=f"{name} Remote Control", kind="ctrl")
    _pb(bp, (0, 1, -2), f"{name} PB", script, custom)
    for x in (-1, 0, 1): bp.armor(x, 0, -1)
    bp.armor(-1, 1, -1); bp.armor(1, 1, -1)
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (0, 1, -1), "Forward", "Up", f"{name} Gyroscope", kind="gyro")
    for x in (-1, 0, 1):
        for y in (0, 1): bp.armor(x, y, 0)
    cam(bp, (0, 0, -4), "Forward", "Up", f"{name} Camera Lower", 3)
    cam(bp, (0, 1, -4), "Forward", "Up", f"{name} Camera Upper", 3)

def _all_around(bp, name):
    """antenna on the rear deck + up/down/rear/left/right cameras (as Hornet)"""
    ant(bp, (0, 2, 0), "Up", "Backward", f"{name} Antenna", 5000)
    cam(bp, (0, 2, -3), "Up", "Backward", f"{name} Camera Up")
    cam(bp, (0, -1, -3), "Down", "Forward", f"{name} Camera Down")
    cam(bp, (0, 1, 1), "Backward", "Up", f"{name} Camera Rear")
    bp.armor(-1, -1, -2, HULL); bp.armor(1, -1, -2, HULL)
    cam(bp, (-2, -1, -2), "Left", "Up", f"{name} Camera Left")
    cam(bp, (2, -1, -2), "Right", "Up", f"{name} Camera Right")

def _gun(bp, name, mn, label):
    g = bp.add("MyObjectBuilder_SmallGatlingGun", "", mn, "Forward", "Up", f"{name} Gatling {label}", GUN, kind="gun")
    bp.keep_clear(g, 6, "muzzle")
    return g

def _merge_stalk(bp, name):
    bp.armor(0, 0, 1, RED); bp.armor(0, 0, 2, RED)
    merge_small(bp, (0, 0, 3), "Backward", f"{name} Merge")

def _ai_sensor(bp, name, ai, sensor, ai_flight_at=(0, -1, 0), ai_off_at=(0, -1, -1), sensor_at=(0, -1, -2)):
    if sensor:
        bp.add("MyObjectBuilder_SensorBlock", "SmallBlockSensor", sensor_at, "Down", "Forward", f"{name} Sensor", kind="sensor")
    if ai:
        bp.add("MyObjectBuilder_OffensiveCombatBlock", "SmallOffensiveCombat", ai_off_at, name=f"{name} AI Offensive [AI]", kind="ai")
        bp.add("MyObjectBuilder_FlightMovementBlock", "SmallFlightMovement", ai_flight_at, name=f"{name} AI Flight [AI]", kind="ai")

# ---------------------------------------------------------------------------------------------
def harrier(lite=False):
    """HARRIER: Hornet frame, 4 mains (2x2 behind the core), 2 brakes, ONE strafe thruster per side, lift/dive pairs, gyro, 2 gatlings.
    The two mains on the upper row sit at y=1 beside the rear camera (z=1..2); the merge stalk stays on the centre line y=0."""
    name = "Harrier Lite" if lite else "Harrier"
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    _core(bp, name, script_text("HarrierAI.cs"), _custom("Harrier", "HARRIER", extra="Target Priority Small Bonus=300\n"))
    for x, s in ((-2, "L"), (2, "R")): _gun(bp, name, (x, 0, -4), s)
    for x, s in ((-1, "L"), (1, "R")):
        thr(bp, (x, 0, 1), "Backward", "Up", f"{name} Ion Main {s}1")
        thr(bp, (x, 1, 1), "Backward", "Up", f"{name} Ion Main {s}2")
    thr(bp, (-2, 1, -2), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (2, 1, -2), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, 0, 0), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, 1, 0), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, 0, 0), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, 1, 0), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-1, -2, -1), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (1, -2, -1), "Down", "Forward", f"{name} Ion Lift R")
    thr(bp, (-1, 2, -1), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (1, 2, -1), "Up", "Backward", f"{name} Ion Dive R")
    _all_around(bp, name)
    _merge_stalk(bp, name)
    _ai_sensor(bp, name, not lite, not lite)
    import core
    return core.hornet_survival(finalize(bp), name)      # Gyroscope 2, [Flight] + [Staff] (+ [Thrust]) here too (a Harrier printed by a Forge carries its Staff PB), survival kit

# ---------------------------------------------------------------------------------------------
def aegis(lite=False):
    """AEGIS: Hornet frame with FOUR gatlings (stacked pairs on each flank) and two Decoy blocks on the roof.
    The brake thrusters move to the nose (x=+-1, y=0, z=-5..-4) because the upper gatlings take the Hornet brake slots.
    Lite: no Decoy / Sensor / AI; the roof gets two heavy armour plates instead."""
    name = "Aegis Lite" if lite else "Aegis"
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    _core(bp, name, script_text("AegisAI.cs"), _custom("Aegis", "AEGIS",
          extra="Extra Home Channel=HORNET_HOME\nCue Channel=DRONE_CUE\n"))
    for x, s in ((-2, "L"), (2, "R")):
        _gun(bp, name, (x, 0, -4), s + "1")
        _gun(bp, name, (x, 1, -4), s + "2")
    thr(bp, (-1, 0, 1), "Backward", "Up", f"{name} Ion Main L"); thr(bp, (1, 0, 1), "Backward", "Up", f"{name} Ion Main R")
    thr(bp, (-1, 0, -5), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (1, 0, -5), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, 0, 0), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, 1, 0), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, 0, 0), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, 1, 0), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-1, -2, -1), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (1, -2, -1), "Down", "Forward", f"{name} Ion Lift R")
    thr(bp, (-1, 2, -1), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (1, 2, -1), "Up", "Backward", f"{name} Ion Dive R")
    _all_around(bp, name)
    for x, s in ((-1, "L"), (1, "R")):
        if lite:
            bp.add("MyObjectBuilder_CubeBlock", "SmallHeavyBlockArmorBlock", (x, 2, -3), color=HULL, mass=112)
        else:
            bp.add("MyObjectBuilder_Decoy", "SmallDecoy", (x, 2, -3), "Forward", "Up", f"{name} Decoy {s}", HULL, kind="decoy")
    _merge_stalk(bp, name)
    _ai_sensor(bp, name, not lite, not lite)
    import core
    return core.hornet_survival(finalize(bp), name, decoys=0)      # Gyroscope 2, support PBs (an Aegis printed by a Shipyard carries them), survival kit; it keeps its two roof decoys

# ---------------------------------------------------------------------------------------------
def mantis(lite=False):
    """MANTIS: Longbow S spine (railgun on the centre line, hull below it) + ONE gatling riding on top of the railgun's rear
    mounts, 4 mains instead of 2, 3 nose cameras instead of 6.  The gatling bottom (rear cell) seats on the railgun Top mount.
    The up camera moves to the antenna top because the gatling takes its cell."""
    name = "Mantis Lite" if lite else "Mantis"
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    rail = bp.add("MyObjectBuilder_SmallMissileLauncherReload", "SmallRailgun", (0, 0, -8), "Forward", "Up", f"{name} Railgun", DARK, kind="rail")
    bp.keep_clear(rail, 8, "muzzle")
    gat = bp.add("MyObjectBuilder_SmallGatlingGun", "", (0, 2, -4), "Forward", "Up", f"{name} Gatling", GUN, kind="gun")
    bp.keep_clear(gat, 6, "muzzle")
    for x in (-1, 0, 1): cam(bp, (x, -1, -4), "Forward", "Up", f"{name} Camera Nose {x+2}", 3)
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (0, -1, -3), name=f"{name} Remote Control", kind="ctrl")
    for x in (-1, 1): bp.armor(x, -1, -3, ACC); bp.armor(x, -2, -3, ACC)
    bp.armor(0, -2, -3, ACC)
    _pb(bp, (0, -2, -2), f"{name} PB", script_text("MantisAI.cs"),
        _custom("Mantis", "MANTIS", extra="Engage Cap=650\nMin Engage Range=250\n"))
    bp.armor(0, -1, -2)
    bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (0, -1, -1), "Forward", "Up", f"{name} Gyroscope", kind="gyro")
    for x in (-1, 1): bp.armor(x, -1, -2); bp.armor(x, -2, -2)
    bat(bp, (-1, -4, -1), f"{name} Battery", small=False)
    for z in (-1, 0):
        for x in (-1, 0, 1):
            for y in (-1, -2):
                if (x, y, z) != (0, -1, -1): bp.armor(x, y, z)
    for x, s in ((-1, "L"), (1, "R")):
        thr(bp, (x, -1, 1), "Backward", "Up", f"{name} Ion Main {s}1")
        thr(bp, (x, -2, 1), "Backward", "Up", f"{name} Ion Main {s}2")
    thr(bp, (-2, -1, -3), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (2, -1, -3), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, -2, -1), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, -2, 0), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, -2, -1), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, -2, 0), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-2, -5, 0), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (2, -5, 0), "Down", "Forward", f"{name} Ion Lift R")
    thr(bp, (-2, 0, -2), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (2, 0, -2), "Up", "Backward", f"{name} Ion Dive R")
    ant(bp, (0, 0, 0), "Up", "Backward", f"{name} Antenna", 5000)
    cam(bp, (0, 3, -1), "Up", "Backward", f"{name} Camera Up")
    cam(bp, (0, -1, 1), "Backward", "Up", f"{name} Camera Rear")
    cam(bp, (0, -3, -3), "Down", "Forward", f"{name} Camera Down")
    cam(bp, (-2, -1, 0), "Left", "Up", f"{name} Camera Left")
    cam(bp, (2, -1, 0), "Right", "Up", f"{name} Camera Right")
    if not lite:
        bp.add("MyObjectBuilder_SensorBlock", "SmallBlockSensor", (-1, -3, -3), "Down", "Forward", f"{name} Sensor", kind="sensor")
        bp.add("MyObjectBuilder_OffensiveCombatBlock", "SmallOffensiveCombat", (1, -3, -3), name=f"{name} AI Offensive [AI]", kind="ai")
        bp.add("MyObjectBuilder_FlightMovementBlock", "SmallFlightMovement", (1, -3, -2), name=f"{name} AI Flight [AI]", kind="ai")
    # power for the railgun recharge (docs/design/railgun_power.md): the Longbow S pack + three more mains on the battery's back face (7 in all keep 8.7 m/s2 forward)
    import core
    core.small_rail_power(bp, name, mains=(((-1, -3, 2), "L3"), ((1, -3, 2), "R3"), ((0, -3, 2), "C")))
    C.add_support_pbs(finalize(bp), avoid=core.spine_weld_cells(bp))      # [Flight] + [Staff] (+ [Thrust] + [Aux]) here too, so a printed Mantis carries them; the Aux PB clear of the Foundry's welders
    # no bury_swap (as the Longbow S): the reactors keep the cells the Foundry's welders were solved for
    return finalize(C.add_survival_kit(bp, model=name, avoid=core.spine_weld_cells(bp), panels=False))      # survival kit (docs/design/survivability.md items 11-15), clear of the Foundry's welders

# ---------------------------------------------------------------------------------------------
def basilisk(lite=False, seeker=False, staff=True):
    """BASILISK: LARGE-grid armoured gunship (2.5 m cells).  x = Right, y = Up, z = Backward; nose at -z.
      front wall  z=-1   6x LargeHeavyBlockArmorBlock (3.3 t each)       nose cameras at (0,0..1,-2) between the launchers
      launchers   z=-3..-2  4x LargeMissileLauncher at (+-1, 0..1)        keep 2 cells (5 m) clear in front
      core        z=0..2    x -1..1, y 0..1: 3 large batteries, RC, PB, armour, gyro
      engines     2 mains (z=3), 2 brakes on the flanks, 2+2 strafe, 2 dive (flanks), 2 lift (belly): 2 per axis
      top deck    LargeGatlingTurret (Full only) centred on the roof, compact antenna + up camera behind it
    Everything is 3 cells wide so every block has a face on the hull; the grid is one connected piece (validated)."""
    name = ("Basilisk Seeker" if seeker else "Basilisk") + (" Lite" if lite else "")
    bp = Blueprint(name, "Large", seed=seed_for(name), legacy_shapes=True)
    HEAVY = "LargeHeavyBlockArmorBlock"
    for x in (-1, 0, 1):
        for y in (0, 1): bp.armor(x, y, -1, ACC, sub=HEAVY)
    for x in (-1, 1):
        for y in (0, 1):
            g = bp.add("MyObjectBuilder_SmallMissileLauncher", "LargeMissileLauncher", (x, y, -3), "Forward", "Up",
                       f"{name} Launcher {'L' if x < 0 else 'R'}{y+1}", GUN, kind="gun")
            bp.keep_clear(g, 2, "muzzle")
    cam(bp, (0, 0, -2), "Forward", "Up", f"{name} Camera Lower", 3)
    cam(bp, (0, 1, -2), "Forward", "Up", f"{name} Camera Upper", 3)
    # core z=0
    bp.add("MyObjectBuilder_RemoteControl", "LargeBlockRemoteControl", (0, 0, 0), name=f"{name} Remote Control", kind="ctrl")
    bat(bp, (-1, 0, 0), f"{name} Battery 1"); bat(bp, (1, 0, 0), f"{name} Battery 2"); bat(bp, (1, 1, 0), f"{name} Battery 3")
    _pb(bp, (-1, 1, 0), f"[Flight] {name} PB" if staff else f"{name} PB", script_text("BasiliskAI.cs"),
        _custom("Basilisk", "BASILISK", extra="Standoff Range=600\n"), large=True)
    bp.armor(0, 1, 0)
    # z=1, z=2
    for z in (1, 2):
        for x in (-1, 0, 1):
            for y in (0, 1):
                if (x, y, z) != (0, 1, 1): bp.armor(x, y, z)
    bp.add("MyObjectBuilder_Gyro", "LargeBlockGyro", (0, 1, 1), "Forward", "Up", f"{name} Gyroscope", kind="gyro")
    if not lite:    # survivability item 14: Gyroscope 2 in place of the core armour at (0, 0, 2), on the AI Flight block (buried on every face)
        C.add_gyro(bp, (0, 0, 2), f"{name} Gyroscope 2")
    # engines
    thr(bp, (-1, 0, 3), "Backward", "Up", f"{name} Ion Main L"); thr(bp, (1, 0, 3), "Backward", "Up", f"{name} Ion Main R")
    thr(bp, (-2, 0, -2), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (2, 0, -2), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, 0, 0), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, 1, 0), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, 0, 0), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, 1, 0), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-2, 0, 2), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (2, 0, 2), "Up", "Backward", f"{name} Ion Dive R")
    thr(bp, (-1, -2, 1), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (1, -2, 1), "Down", "Forward", f"{name} Ion Lift R")
    # roof: compact antenna (1x3x1, bottom mount) + up camera behind the turret footprint (x -1..1, z -1..1)
    bp.add("MyObjectBuilder_RadioAntenna", "LargeBlockCompactRadioAntenna", (0, 2, 2), "Forward", "Up", f"{name} Antenna",
           extra=ant_extra(10000), kind="ant")
    cam(bp, (1, 2, 2), "Up", "Backward", f"{name} Camera Up")
    cam(bp, (0, 1, 3), "Backward", "Up", f"{name} Camera Rear")
    cam(bp, (0, -1, -1), "Down", "Forward", f"{name} Camera Down")
    cam(bp, (-2, 1, 1), "Left", "Up", f"{name} Camera Left")
    cam(bp, (2, 1, 1), "Right", "Up", f"{name} Camera Right")
    if not lite or seeker:      # the Seeker Lite keeps the turret (it is the point of the ship)
        t = bp.add("MyObjectBuilder_LargeGatlingTurret", "", (-1, 2, -1), "Forward", "Up", f"{name} Gatling Turret" + (" [Seeker]" if seeker else ""), GUN, kind="turret")
    if not lite:
        bp.add("MyObjectBuilder_SensorBlock", "LargeBlockSensor", (0, -1, 0), "Down", "Forward", f"{name} Sensor", kind="sensor")
        bp.add("MyObjectBuilder_OffensiveCombatBlock", "LargeOffensiveCombat", (0, -1, 1), name=f"{name} AI Offensive [AI]", kind="ai")
        bp.add("MyObjectBuilder_FlightMovementBlock", "LargeFlightMovement", (0, -1, 2), name=f"{name} AI Flight [AI]", kind="ai")
    # the Staff half (FLEET LINK + FOLLOW + the status screen leave BasiliskAI, 95 k minified; docs/design/basilisk_split.md) sits in the cell the spare PB found (most enclosed free cell);
    # staff=False is the Shrike Basilisk Test (ShrikeAI in the main PB, no Staff next to it): today's ship with its blank spare.  [Thrust] is added by build_all.
    if staff: spares.add_spare_pbs(bp, total=spares.pb_count(bp) + 1, name='[Staff] {model} PB', model=name, reserved_ok=True, extra=pb_extra(script_text("BasiliskStaffAI.cs")))
    else: spares.add_spare_pbs(bp, total=spares.pb_count(bp) + 1, model=name)
    if staff:       # [Thrust] here (not only in build_all) so the self-projection holds it; then the survival kit (docs/design/survivability.md items 11-15)
        C.add_support_pbs(finalize(bp))
        C.bury_swap(bp)
        C.add_survival_kit(bp, model=name)
    return finalize(bp)

def basilisk_seeker(lite=False):
    """Option-4 test ship: the Full Basilisk whose roof turret is named [Seeker]. The script sets it to the targeting group in
    Custom Data (Seeker Group, default PowerSystems) and drills where it points. Lite keeps the turret but drops sensor + AI blocks."""
    return basilisk(lite=lite, seeker=True)

MODELS = {"Harrier": harrier, "Aegis": aegis, "Mantis": mantis, "Basilisk": basilisk, "Basilisk Seeker": basilisk_seeker}

if __name__ == "__main__":
    sys.exit(C.run_module(MODELS))
