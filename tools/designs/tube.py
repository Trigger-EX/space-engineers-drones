"""Tube drones: kinetic ram drones (Tube 2x2, Tube 2x4, Tube 3x3, Tube 4x4), small grid.  Design record: docs/design/tube_drones.md.

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Script scripts/TubeRamAI.cs is read at call time.
`python3 -I tools/designs/tube.py` builds + validates every model, one line each.

A tube is a closed box of armour: a solid heavy-armour nose cap (with the nose camera set flush in the front layer), the systems packed behind
it, a service layer (side cameras, antenna, docking latch) and the WHOLE rear face one bank of ion thrusters, exhaust aft.  No warhead: the kill
is kinetic.  Frame (core.py convention): x = Right (0..W-1), y = Up (0..H-1), z = Backward, nose front layer z = 0, cells are 0.5 m.
Thrust is forward only (it brakes by flipping the nose round).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C

HEAVY = "SmallHeavyBlockArmorBlock"
CAP_COL = (0.07, 0.55, 0.10)        # nose cap accent
HULL_COL = (0.58, -0.78, -0.25)

def tube_frame(name, W, H, cap, body, spec):
    """W x H section, `cap` heavy nose layers, `body` layers before the thruster bank (z 0..body-1).  spec = dict with keys
    rc, pb, gyros, ai (AI Offensive cell, AI Flight cell), battery ('s' = one small battery per entry in bats, or 'L' = one 3x2x3 battery at bat_min), nose (cells), left, right, ant (cell, fwd), latch x."""
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    script = script_text("TubeRamAI.cs")
    custom = f"[Tube]\nIGC Tag=TUBE\nBlock Tag={name}\n"
    # nose cap: heavy armour, cameras flush in the front layer
    nose = set(spec["nose"])
    for z in range(cap):
        for y in range(H):
            for x in range(W):
                if z == 0 and (x, y) in nose: continue
                bp.armor(x, y, z, CAP_COL if z == 0 else HULL_COL, sub=HEAVY)
    for i, (x, y) in enumerate(spec["nose"]):
        C.cam(bp, (x, y, 0), "Forward", "Up", f"{name} Camera Nose" + ("" if len(spec["nose"]) == 1 else f" {i+1}"), 3)
    # systems
    x, y, z = spec["rc"];  bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (x, y, z), name=f"{name} Remote Control", kind="ctrl")
    x, y, z = spec["pb"];  bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (x, y, z), "Forward", "Up", f"{name} PB",
                                  extra=pb_extra(script), kind="ctrl", custom_data=custom)
    for i, (x, y, z) in enumerate(spec["gyros"]):
        bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (x, y, z), "Forward", "Up", f"{name} Gyroscope" + ("" if len(spec["gyros"]) == 1 else f" {i+1}"), kind="gyro")
    if spec.get("bat_min"):
        C.bat(bp, spec["bat_min"], f"{name} Battery", small=False)
    for i, c in enumerate(spec.get("bats", ())):
        C.bat(bp, c, f"{name} Battery {i+1}")
    # AI blocks (the script hands the ship to them while it has no target and confirms what they find with camera rays)
    for kind_, sub_, xt_, label_, cell_ in (("AI Offensive", "SmallOffensiveCombat", "MyObjectBuilder_OffensiveCombatBlock", "AI Offensive [AI]", spec["ai"][0]),
                                            ("AI Flight", "SmallFlightMovement", "MyObjectBuilder_FlightMovementBlock", "AI Flight [AI]", spec["ai"][1])):
        bp.add(xt_, sub_, cell_, name=f"{name} {label_}", kind="ai")
    # service layer: flush side cameras, antenna
    for (x, y, z), d, label in spec["cams"]:
        C.cam(bp, (x, y, z), d, "Up", f"{name} Camera {label}")
    (ax, ay, az), afwd, aup = spec["ant"]
    C.ant(bp, (ax, ay, az), afwd, aup, f"{name} Antenna", 2500)
    # fill every empty body cell with light armour (closed box, nothing floats)
    occ = bp.occupancy()
    for z in range(body):
        for y in range(H):
            for x in range(W):
                if (x, y, z) not in occ: bp.armor(x, y, z, HULL_COL)
    # docking latch below the tail on the centre line (the merge block is the lowest block)
    lx = spec["latch"]; lz = body - 1
    bp.armor(lx, -1, lz, RED)
    C.merge_small(bp, (lx, -2, lz), "Down", f"{name} Merge")
    # thruster bank: one ion thruster per section cell, exhaust aft
    n = 0
    for y in range(H):
        for x in range(W):
            n += 1
            C.thr(bp, (x, y, body), "Backward", "Up", f"{name} Ion Main {n}")
    return finalize(bp)

def _variant(name):
    if name == "Tube 2x2":
        return dict(W=2, H=2, cap=2, body=6, spec=dict(
            nose=[(0, 1)], rc=(0, 0, 2), pb=(1, 0, 2), gyros=[(0, 1, 2)],
            bats=[(1, 1, 2), (0, 0, 3), (1, 0, 3), (0, 1, 3), (1, 1, 3)],
            cams=[((0, 0, 4), "Left", "Left")], ant=((1, 1, 4), "Backward", "Up"), latch=0, ai=[(1, 0, 5), (0, 1, 5)]))
    if name == "Tube 2x4":      # 4 wide x 2 tall
        return dict(W=4, H=2, cap=3, body=7, spec=dict(
            nose=[(1, 1)], rc=(3, 0, 3), pb=(3, 0, 4), gyros=[(3, 1, 3)], bat_min=(0, 0, 3),
            cams=[((0, 0, 6), "Left", "Left"), ((3, 0, 6), "Right", "Right")], ant=((1, 1, 6), "Right", "Up"), latch=1, ai=[(3, 0, 5), (3, 1, 5)]))
    if name == "Tube 3x3":
        return dict(W=3, H=3, cap=3, body=7, spec=dict(
            nose=[(1, 1)], rc=(0, 2, 3), pb=(0, 2, 4), gyros=[(1, 2, 3)], bat_min=(0, 0, 3),
            cams=[((0, 1, 6), "Left", "Left"), ((2, 1, 6), "Right", "Right")], ant=((1, 2, 6), "Right", "Up"), latch=1, ai=[(1, 2, 4), (2, 2, 4)]))
    if name == "Tube 4x4":
        return dict(W=4, H=4, cap=4, body=8, spec=dict(
            nose=[(1, 1), (2, 2)], rc=(3, 0, 4), pb=(1, 2, 4), gyros=[(3, 1, 4), (0, 2, 4)], bat_min=(0, 0, 4),
            cams=[((0, 1, 7), "Left", "Left"), ((3, 1, 7), "Right", "Right")], ant=((1, 3, 7), "Right", "Up"), latch=1, ai=[(2, 2, 5), (3, 2, 5)]))
    raise KeyError(name)

def build(name):
    v = _variant(name)
    return tube_frame(name, v["W"], v["H"], v["cap"], v["body"], v["spec"])

MODELS = {n: (lambda lite=False, n=n: build(n)) for n in ("Tube 2x2", "Tube 2x4", "Tube 3x3", "Tube 4x4")}
NO_LITE = set(MODELS)
REQUIRED_AXES = {m: ("Forward",) for m in MODELS}
DOCS = {
    'Tube 2x2': 'Kinetic ram drone, 1 x 1 m section, 4 m long, about 3.4 t, about 17 m/s2: heavy-armour nose, one ion thruster per cell of the rear face, merge latch. AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams while it can still fly.',
    'Tube 2x4': 'Kinetic ram drone, 2 m wide x 1 m tall section, 4.5 m long, about 6.1 t, about 19 m/s2: heavy-armour nose, 4 MW battery, 8 ion thrusters. AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams.',
    'Tube 3x3': 'Kinetic ram drone, 1.5 x 1.5 m section, 4.5 m long, about 6.6 t, about 19.5 m/s2: heavy-armour nose, 4 MW battery, 9 ion thrusters. AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams.',
    'Tube 4x4': 'Kinetic ram drone, 2 x 2 m section, 5 m long, about 12.7 t, about 18 m/s2: four-layer heavy-armour nose, 4 MW battery, 16 ion thrusters, two gyros. AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams.',
}
_NOTE = ('Thrust only forward: it brakes by flipping. Finds targets with its AI Offensive / AI Flight blocks (AI Behavior must be on; the in-game behaviour of the AI blocks is unverified) or takes them from the fleet / hive / carrier channels or a player\'s `lock`. Ram damage is not verified in the game.')
MODEL_NOTES = {m: _NOTE for m in MODELS}
SCRIPTS = ['TubeRamAI.cs']

if __name__ == "__main__":
    bad = 0
    for n, fn in MODELS.items():
        try:
            bp = fn(False)
        except AssertionError as e:
            print(f'FAIL {n}: {e}'); bad += 1; continue
        ok, t = C.report_line(n, bp, 1.0, ("Forward",))
        print(t); bad += (not ok)
    sys.exit(1 if bad else 0)
