"""Medic: small-grid tender drone that welds a damaged friendly drone back up in the field (docs/design/survivability.md item 18, package E1).

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Script scripts/MedicAI.cs is read at call time.
`python3 -I tools/designs/medic.py` builds + validates the model and prints one line.  Unarmed: not in the sim scenario lists.

Owner (2026-10-18): "medic should be able to carry a lot".  The Medic is built round ONE SmallBlockLargeContainer (5x5x5, 15,625 L, no DLC) that
carries the parts of TWO full Hornet rebuilds (about 6,100 L / 11.7 t, every component of the Hornet blueprint, the bulky power cells, radio parts and
metal grids included).  The medium container (3,375 L) would hold barely one Hornet (3,058 L), so the large one it is.

Frame (cells of 0.5 m; x Right, y Up, z Backward, nose = -z).  Outline: the 5 x 5 section of the container (x, y -2..2) from the welder tip to the
main thrusters; the flank thrusters (2 cells), cameras and antenna stand on it as on the Hornet.  No merge block (it does not dock).
  * one SmallShipWelder (3x3x6, x -1..1, y -1..1, z -9..-4) on the nose axis, tip forward.  Its conveyor port is the centre of its back face (as the
    printers assume, printer_lib.PIPE_BACK, UNVERIFIED); a small conveyor junction sits in that cell (0,0,-3) and touches the centre of the container's
    front face (z -2): welder <- junction <- [Stock] container, the ConveyorLines are written (_common.link_conveyors) and test_medic.py checks the chain;
  * front slab z -3 round the junction: Remote Control, MedicAI PB, the self-projector [Self] (item 15, Enabled=false), a decoy (item 11), 4 small
    batteries; around it, in the ring x or y = +-2 (z -4..-3), 12 small ion BRAKE thrusters whose flames run forward beside the welder;
  * container (x, y -2..2, z -2..2), with 10 small ions on each flank (x +-3..4), on its top and on its bottom (y +-3..4): lateral and vertical thrust;
  * rear core z 3..5: 3 large batteries (12 MW), 4 gyros, armour; 2 large ion MAIN thrusters behind it (x -1..1, y 1..2 and -2..-1, z 6..9);
  * antenna on the rear deck, cameras forward (corner of the brake ring), down, left, right, up, rear (between the mains).
  Single welder: a second 3x3 welder does not fit beside the first inside the 5x5 section, and its work sphere (r 1.3 m) would sit 1.5 m off the work
  point, so it could not reach the block the first one welds (reach.py); the time goes into the hold, not the weld rate.

Flame clearance in the work pose (WORK: the welder tip about 0.1 m off the patient's surface, nose on the work point's normal): the brake thrusters are
  small ions (flame damage about 0.8 m at full thrust, estimated from defs_stats FlameDamageLengthScale; not measured in the game); their nozzles are
  5 cells (2.5 m) behind the tip face, so the flame stops about 1.7 m short of the patient's surface.  The flank and vertical flames run sideways from
  z -2..2 (3.5 m behind the tip face) and the large mains fire backwards: none of them points at the patient.
"""
import sys, os, copy
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C

NAME = "Medic"
CUSTOM = ("[Medic]\nMedic Channel=MEDIC\nWork Hold (s)=20\nApproach Speed=5\nWork Speed=0.5\nTip Offset (m)=0.6\nCruise Speed=40\n"
          "Max Job Range (m)=3000\n")
WELDER_TIP = (0, 0, -9)            # tip-centre cell of the welder (works along Forward)
PORT = (0, 0, -3)                  # junction behind the centre of the welder's back face
CARGO_SUB = "SmallBlockLargeContainer"
CARGO_MIN = (-2, -2, -2)           # 5x5x5: x, y, z -2..2; the centre of its front face (0, 0, -2) touches the junction
CARGO_L = 15625.0                  # SmallBlockLargeContainer inventory, litres (2.5 m cube; the game's value at inventory multiplier 1)
KITS = 2                           # full Hornet rebuilds pre-loaded
STOCK_MIN_L = 300                  # MedicAI StockMinL: below this many litres of parts the medic refuses jobs (STOCK LOW)
FLANK_Z = (0, 2)                   # z columns of the flank (lateral) and top / bottom (vertical) small ions, 5 per column (y or x -2..2): centred on the
                                   # loaded centre of mass (z about +0.7 m, pulled back by the rear core), so lateral thrust gives little yaw / pitch

def _cargo_shape():
    """the CargoContainer shape printer_lib registers says Volume 0.125 (a small container): give the large container its own 15.625 m3"""
    import sebp, printer_lib as PL
    PL.register_shapes()
    if f"CargoContainer/{CARGO_SUB}" not in sebp.SHAPES:
        sp = dict(PL._CARGO_SHAPE)
        sp['container'] = sp['container'].replace('<Volume>0.125</Volume>', f'<Volume>{CARGO_L / 1000.0}</Volume>')
        assert f'<Volume>{CARGO_L / 1000.0}</Volume>' in sp['container']
        sebp.SHAPES[f"CargoContainer/{CARGO_SUB}"] = sp

def kit_for(model_bp, litres, copies=1):
    """{component: count}: the components of every block of model_bp (defs_stats.json) times `copies`, scaled down by one factor until the volume
    fits `litres` (at least one of each component that still fits)."""
    from sebp_inv import Item
    tot = {}
    for b in model_bp.blocks:
        for c in C._stat(b).get('components', []): tot[c['subtype']] = tot.get(c['subtype'], 0) + c['count']
    unit = {k: Item.component(k, 1).litres() for k in tot}
    def scaled(f):
        kit = {k: max(1, int(n * f + 1e-9)) for k, n in tot.items()}
        while sum(unit[k] * n for k, n in kit.items()) > litres:          # the minimum of one each may overflow: drop the bulkiest singles
            k = max((k for k in kit if kit[k] == 1), key=lambda k: unit[k], default=None)
            if k is None: return None
            del kit[k]
        return kit
    best = {}
    for i in range(100 * copies, 0, -1):                                  # the largest factor (1 % steps) whose kit fits
        kit = scaled(i / 100.0)
        if kit is not None and sum(kit.values()) > sum(best.values()): best = kit
        if kit is not None and len(kit) == len(tot): break
    return best

def hornet_bp():
    """the Hornet as build_all builds it (support PBs and survival kit included); falls back to the bare frame if a script is mid-edit"""
    import core
    try:
        return core.MODELS["Hornet"](False)
    except Exception:
        return core.hornet_frame("Hornet", "", "MyObjectBuilder_SmallGatlingGun", None, ai=True, sensor=True)

def hornet_kit():
    return kit_for(hornet_bp(), CARGO_L * 0.95, copies=KITS)

def medic(lite=False):
    import printer_lib as PL
    from sebp_inv import Item
    _cargo_shape()
    name = NAME + (" Lite" if lite else "")
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    HULL = (0.58, -0.10, 0.20); ACC = (0.0, 0.0, 0.45)            # white hull, red-cross accents
    # ---- welder on the nose ----
    mn, fwd, up = PL.welder_block_args(WELDER_TIP, DIR["Forward"])
    welder = bp.add("MyObjectBuilder_ShipWelder", "SmallShipWelder", mn, fwd, up, f"{name} Welder", PL.WELD_COLOR, kind="welder")
    # ---- front slab z -3 (behind the welder) ----
    bp.add("MyObjectBuilder_Conveyor", "SmallBlockConveyor", PORT, "Forward", "Up", f"{name} Conveyor", kind="conv")
    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (0, 1, -3), name=f"{name} Remote Control", kind="ctrl")
    bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (0, -1, -3), "Backward", "Up", f"{name} PB",   # mounts only on its front: the container
           extra=pb_extra(script_text("MedicAI.cs")), kind="ctrl", custom_data=CUSTOM)
    bp.add("MyObjectBuilder_Projector", "SmallProjector", (-1, 0, -3), "Forward", "Up", f"{name} Projector [Self]", kind="proj", enabled=False)
    bp.add("MyObjectBuilder_Decoy", "SmallDecoy", (1, 0, -3), "Forward", "Up", f"{name} Decoy", kind="decoy")
    for i, (x, y) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
        bat(bp, (x, y, -3), f"{name} Battery S{i + 1}")
    # brake ring: small ions in x or y = +-2 (corners left out), z -4..-3, flames forward beside the welder
    k = 0
    for x in range(-2, 3):
        for y in range(-2, 3):
            if max(abs(x), abs(y)) != 2 or (abs(x) == 2 and abs(y) == 2): continue
            k += 1; thr(bp, (x, y, -4), "Forward", "Up", f"{name} Ion Brake {k}")
    cam(bp, (-2, 2, -3), "Forward", "Up", f"{name} Camera Forward", 3)
    for c in ((2, 2, -3), (-2, -2, -3), (2, -2, -3)): bp.armor(*c, ACC)
    # ---- the [Stock] container ----
    stock = bp.add("MyObjectBuilder_CargoContainer", CARGO_SUB, CARGO_MIN, "Forward", "Up", f"{name} Component Stock [Stock]", kind="cargo")
    # flanks: lateral and vertical small ions on the container
    for j, z in enumerate(FLANK_Z):
        for v in range(-2, 3):
            tag = f"{v + 3}{'ab'[j]}"
            thr(bp, (3, v, z), "Right", "Up", f"{name} Ion Strafe L {tag}"); thr(bp, (-4, v, z), "Left", "Up", f"{name} Ion Strafe R {tag}")
            thr(bp, (v, -4, z), "Down", "Forward", f"{name} Ion Lift {tag}"); thr(bp, (v, 3, z), "Up", "Backward", f"{name} Ion Dive {tag}")
    # ---- rear core z 3..5 ----
    bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockBatteryBlock", (-2, 1, 3), "Forward", "Up", f"{name} Battery 1", extra=bat_extra(1), kind="bat")
    bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockBatteryBlock", (-2, -2, 3), "Forward", "Up", f"{name} Battery 2", extra=bat_extra(1), kind="bat")
    bp.add("MyObjectBuilder_BatteryBlock", "SmallBlockBatteryBlock", (1, -2, 3), "Forward", "Right", f"{name} Battery 3", extra=bat_extra(1), kind="bat")
    for i, c in enumerate(((1, 1, 3), (2, 1, 3), (1, 1, 4), (2, 1, 4))):   # gyros mount only on their bottom: they stand on Battery 3
        bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", c, "Forward", "Up", f"{name} Gyroscope {i + 1}", kind="gyro")
    occ = bp.occupancy()
    for z in range(3, 6):
        for y in range(-2, 3):
            for x in range(-2, 3):
                if (x, y, z) not in occ: bp.armor(x, y, z, ACC if z == 5 else HULL)
    # ---- mains: 2 large ions behind the rear core ----
    for y, tag in ((1, "U"), (-2, "D")):
        b = bp.add("MyObjectBuilder_Thrust", "SmallBlockLargeThrust", (-1, y, 6), "Backward", "Up", f"{name} Ion Main {tag}", DARK, kind="thr")
        bp.keep_clear(b, 6, "exhaust", face=True)
    # ---- antenna and cameras ----
    ant(bp, (0, 3, 4), "Up", "Backward", f"{name} Antenna", 5000)
    cam(bp, (0, -3, 4), "Down", "Forward", f"{name} Camera Down")
    cam(bp, (-3, 0, 4), "Left", "Up", f"{name} Camera Left")
    cam(bp, (3, 0, 4), "Right", "Up", f"{name} Camera Right")
    cam(bp, (-1, 3, 4), "Up", "Backward", f"{name} Camera Up")
    cam(bp, (0, 0, 6), "Backward", "Up", f"{name} Camera Rear")
    # ---- conveyor lines: welder port <- junction <- stock container ----
    C.link_conveyors(bp, terminals=[(PORT, welder['name']), (PORT, stock['name'])])
    # ---- component kit: KITS full Hornet rebuilds ----
    bp.preload(stock, items=[Item.component(s, c) for s, c in sorted(hornet_kit().items())])
    # ---- self-projector (item 15): a ghost of this very drone (no cargo), first block on the real first block, no recursion ----
    proj = [b for b in bp.blocks if b['kind'] == 'proj'][0]
    ghost = copy.deepcopy(bp)
    for b in ghost.blocks:
        if b['kind'] == 'proj': b['settings'] = None
        b['items'] = []
    sett, _off = PL.projector_settings(ghost, (0, 0, 0), tuple(proj['min']), seed_text='self')
    proj['settings'] = sett
    finalize(bp)
    bp.validate()
    return bp

def build(lite=False):
    return C.add_support_pbs(medic(lite))          # MedicAI is one PB (no SPLIT LINK, no THRUST VECTOR LINK): nothing is added, kept for the pattern

MODELS = {NAME: build}
NO_LITE = (NAME,)
DOCS = {NAME: 'Unarmed tender drone built round a large cargo container (15,625 L) pre-loaded with the parts of two full Hornet rebuilds, conveyor-fed '
              'to one ship welder on the nose; 2 large and 52 small ion thrusters, 4 gyros, 3 large + 4 small batteries, antenna, decoy and self-projector. '
              'Answers repair calls of damaged drones on the MEDIC channel, flies to the patient (which holds still), points the welder at each missing or '
              'damaged block in turn, welds, and returns home.'}
MODEL_NOTES = {NAME: 'Patients are drones whose Staff PB carries the REPAIR CLIENT (medic calls when no repair bay is free). Refill the [Stock] container by hand; '
                     f'a medic with less than {STOCK_MIN_L} L of parts refuses jobs (STOCK LOW). Run "home" on its PB where it should park.'}
SCRIPTS = ['MedicAI.cs']

if __name__ == "__main__":
    sys.exit(C.run_module(MODELS))
