"""Medic: small-grid tender drone that welds a damaged friendly drone back up in the field (docs/design/survivability.md item 18, package E1).

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Script scripts/MedicAI.cs is read at call time.
`python3 -I tools/designs/medic.py` builds + validates the model and prints one line.  Unarmed: not in the sim scenario lists.

Frame (cells of 0.5 m; x Right, y Up, z Backward, nose = -z):
  * one SmallShipWelder (3x3x6, x -1..1, y -1..1, z -9..-4) on the nose axis, tip forward.  Its conveyor port is the centre of its back face (as the
    printers assume, printer_lib.PIPE_BACK, UNVERIFIED); a small conveyor junction sits in that cell (0,0,-3);
  * 2 SmallBlockSmallContainer [Stock] above and below the junction (0,+-1,-3): welder <- junction <- cargo, the ConveyorLines are written
    (_common.link_conveyors, the printers' routing helper) and test_medic.py checks the port chain;
  * core 3x3x4 behind the welder (z -3..0): Remote Control, MedicAI PB, 2 gyros, 4 small batteries on the welder's back ring, the self-projector
    [Self] (item 15, built like the printers' projections, Enabled=false), one decoy (item 11), armour;
  * 12 ion thrusters, 2 per axis (the Hornet pattern), antenna on the rear deck, cameras forward (over the welder), down, left, right, up, rear.
  Outline: the 3x3 welder section and the 3x3 core; thrusters, cameras and antenna stand on it as on the Hornet.  No merge block (it does not dock).

Components: a full Hornet rebuild is about 2,400 L / 4.7 t of components (defs_stats.json), far more than the two 125 L containers of the design hold.
  The containers are pre-loaded with HORNET_KIT: the Hornet's component mix scaled down to what fits (about a tenth: enough for the damaged blocks of a
  typical hit, not for a full rebuild).  kit_for() computes it from defs_stats.json at build time.
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
STOCK_CELLS = ((0, 1, -3), (0, -1, -3))
CARGO_L = 125.0                    # SmallBlockSmallContainer inventory, litres (the CargoContainer shape printer_lib registers)
KIT_FILL = 0.95                    # of the stock volume

def kit_for(model_bp, litres):
    """{component: count}: the components of every block of model_bp (defs_stats.json), scaled down by one factor until the volume fits `litres`
    (at least one of each component that still fits)."""
    from sebp_inv import Item
    tot = {}
    for b in model_bp.blocks:
        for c in C._stat(b).get('components', []): tot[c['subtype']] = tot.get(c['subtype'], 0) + c['count']
    unit = {k: Item.component(k, 1).litres() for k in tot}
    def scaled(f):
        kit = {k: max(1, int(n * f)) for k, n in tot.items()}
        while sum(unit[k] * n for k, n in kit.items()) > litres:          # the minimum of one each may overflow: drop the bulkiest singles
            k = max((k for k in kit if kit[k] == 1), key=lambda k: unit[k], default=None)
            if k is None: return None
            del kit[k]
        return kit
    best = {}
    for i in range(100, 0, -1):                                           # the largest factor (1 % steps) whose kit fits
        kit = scaled(i / 100.0)
        if kit is not None and sum(kit.values()) > sum(best.values()): best = kit
        if kit is not None and len(kit) == len(tot): break
    return best

def _split(kit, n):
    """deal the kit into n containers of CARGO_L by volume (largest items first)"""
    from sebp_inv import Item
    unit = {k: Item.component(k, 1).litres() for k in kit}
    bins = [dict() for _ in range(n)]; vol = [0.0] * n
    for k in sorted(kit, key=lambda k: -unit[k] * kit[k]):
        left = kit[k]
        while left:
            i = min(range(n), key=lambda i: vol[i])
            fit = int((CARGO_L - vol[i]) // unit[k]) if unit[k] else left
            take = min(left, max(fit, 0))
            if take == 0:
                i = max(range(n), key=lambda i: CARGO_L - vol[i]); take = min(left, int((CARGO_L - vol[i]) // unit[k]))
                if take == 0: break
            bins[i][k] = bins[i].get(k, 0) + take; vol[i] += take * unit[k]; left -= take
    return bins

def hornet_kit():
    import core
    return kit_for(core.hornet_frame("Hornet", "", "MyObjectBuilder_SmallGatlingGun", None, ai=True, sensor=True), CARGO_L * len(STOCK_CELLS) * KIT_FILL)

def medic(lite=False):
    import printer_lib as PL
    from sebp_inv import Item
    PL.register_shapes()                       # the CargoContainer shape (inventory) seen in Starburst
    name = NAME + (" Lite" if lite else "")
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    HULL = (0.58, -0.10, 0.20); ACC = (0.0, 0.0, 0.45)            # white hull, red-cross accents
    # ---- welder on the nose ----
    mn, fwd, up = PL.welder_block_args(WELDER_TIP, DIR["Forward"])
    welder = bp.add("MyObjectBuilder_ShipWelder", "SmallShipWelder", mn, fwd, up, f"{name} Welder", PL.WELD_COLOR, kind="welder")
    # ---- core (x -1..1, y -1..1, z -3..0) ----
    roles = {
        (-1, -1, -3): "bat", (1, -1, -3): "bat", (-1, 1, -3): "bat", (1, 1, -3): "bat",
        STOCK_CELLS[0]: "cargo", STOCK_CELLS[1]: "cargo", PORT: "conv",
        (-1, 0, -2): "gyro", (1, 0, -2): "gyro", (0, 0, -2): "rc", (0, -1, -2): "pb", (-1, 1, -2): "proj", (1, 1, -2): "decoy",
    }
    n = {"bat": 0, "cargo": 0, "gyro": 0}
    for z in range(-3, 1):
        for y in (-1, 0, 1):
            for x in (-1, 0, 1):
                c = (x, y, z); r = roles.get(c)
                if r is None: bp.armor(x, y, z, ACC if (z == -3 or (x == 0 and y == 1)) else HULL)
                elif r == "bat":
                    n["bat"] += 1; bat(bp, c, f"{name} Battery {n['bat']}")
                elif r == "cargo":
                    n["cargo"] += 1
                    bp.add("MyObjectBuilder_CargoContainer", "SmallBlockSmallContainer", c, "Forward", "Up", f"{name} Component Stock {n['cargo']} [Stock]", kind="cargo")
                elif r == "conv":
                    bp.add("MyObjectBuilder_Conveyor", "SmallBlockConveyor", c, "Forward", "Up", f"{name} Conveyor", kind="conv")
                elif r == "gyro":       # mounts only on its bottom: stands on the armour below
                    n["gyro"] += 1; bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", c, "Forward", "Up", f"{name} Gyroscope {n['gyro']}", kind="gyro")
                elif r == "rc":
                    bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", c, name=f"{name} Remote Control", kind="ctrl")
                elif r == "pb":         # mounts only on its front: faces the lower stock container
                    bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", c, "Forward", "Up", f"{name} PB",
                           extra=pb_extra(script_text("MedicAI.cs")), kind="ctrl", custom_data=CUSTOM)
                elif r == "decoy":
                    bp.add("MyObjectBuilder_Decoy", "SmallDecoy", c, "Forward", "Up", f"{name} Decoy", kind="decoy")
                elif r == "proj":
                    bp.add("MyObjectBuilder_Projector", "SmallProjector", c, "Forward", "Up", f"{name} Projector [Self]", kind="proj", enabled=False)
    # ---- thrusters: 2 per axis (Hornet pattern) ----
    thr(bp, (-1, 0, 1), "Backward", "Up", f"{name} Ion Main L"); thr(bp, (1, 0, 1), "Backward", "Up", f"{name} Ion Main R")
    thr(bp, (-2, 1, -2), "Forward", "Up", f"{name} Ion Brake L"); thr(bp, (2, 1, -2), "Forward", "Up", f"{name} Ion Brake R")
    thr(bp, (-3, 0, 0), "Left", "Up", f"{name} Ion Strafe R1"); thr(bp, (-3, -1, 0), "Left", "Up", f"{name} Ion Strafe R2")
    thr(bp, (2, 0, 0), "Right", "Up", f"{name} Ion Strafe L1"); thr(bp, (2, -1, 0), "Right", "Up", f"{name} Ion Strafe L2")
    thr(bp, (-1, -3, -1), "Down", "Forward", f"{name} Ion Lift L"); thr(bp, (1, -3, -1), "Down", "Forward", f"{name} Ion Lift R")
    thr(bp, (-1, 2, -1), "Up", "Backward", f"{name} Ion Dive L"); thr(bp, (1, 2, -1), "Up", "Backward", f"{name} Ion Dive R")
    # ---- antenna and cameras ----
    ant(bp, (0, 2, 0), "Up", "Backward", f"{name} Antenna", 5000)
    bp.armor(0, 2, -2, ACC)                                                       # seat of the forward camera (looks over the welder)
    cam(bp, (0, 2, -3), "Forward", "Up", f"{name} Camera Forward", 3)
    cam(bp, (0, -2, -3), "Down", "Forward", f"{name} Camera Down")
    cam(bp, (-2, -1, -2), "Left", "Up", f"{name} Camera Left")
    cam(bp, (2, -1, -2), "Right", "Up", f"{name} Camera Right")
    cam(bp, (-1, 2, 0), "Up", "Backward", f"{name} Camera Up")
    cam(bp, (0, 0, 1), "Backward", "Up", f"{name} Camera Rear")
    # ---- conveyor lines: welder port <- junction <- both stock containers ----
    stock = [b for b in bp.blocks if b['kind'] == 'cargo']
    C.link_conveyors(bp, terminals=[(PORT, welder['name'])] + [(PORT, k['name']) for k in stock])
    # ---- component kit ----
    for k, items in zip(stock, _split(hornet_kit(), len(stock))):
        bp.preload(k, items=[Item.component(s, c) for s, c in sorted(items.items())])
    # ---- self-projector (item 15): a ghost of this very drone, first block on the real first block, no recursion ----
    proj = [b for b in bp.blocks if b['kind'] == 'proj'][0]
    ghost = copy.deepcopy(bp)
    for b in ghost.blocks:
        if b['kind'] == 'proj': b['settings'] = None
    sett, _off = PL.projector_settings(ghost, (0, 0, 0), tuple(proj['min']), seed_text='self')
    proj['settings'] = sett
    finalize(bp)
    bp.validate()
    return bp

def build(lite=False):
    return C.add_support_pbs(medic(lite))          # MedicAI is one PB (no SPLIT LINK, no THRUST VECTOR LINK): nothing is added, kept for the pattern

MODELS = {NAME: build}
NO_LITE = (NAME,)
DOCS = {NAME: 'Unarmed tender drone: one ship welder on the nose fed by conveyor from two component containers (pre-loaded with about a tenth of a Hornet '
              'in parts), 12 ion thrusters, 2 gyros, 4 batteries, antenna, decoy and self-projector. Answers repair calls of damaged drones on the MEDIC channel, '
              'flies to the patient (which holds still), points the welder at each missing or damaged block in turn, welds, and returns home.'}
MODEL_NOTES = {NAME: 'Patients are drones whose Staff PB carries the REPAIR CLIENT (medic calls when no repair bay is free). Refill the [Stock] containers by hand; '
                     'a medic below 10 % stock refuses jobs (STOCK LOW). Run "home" on its PB where it should park.'}
SCRIPTS = ['MedicAI.cs']

if __name__ == "__main__":
    sys.exit(C.run_module(MODELS))
