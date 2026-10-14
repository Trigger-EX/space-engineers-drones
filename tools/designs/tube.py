"""Tube drones: kinetic ram drones (Tube 2x2, Tube 2x4, Tube 3x3, Tube 4x4), small grid.  Design record: docs/design/tube_drones.md.

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Script scripts/TubeRamAI.cs is read at call time.
`python3 -I tools/designs/tube.py` builds + validates every model, one line each.

A tube is a closed box of armour: a solid heavy-armour nose cap (with the nose camera set flush in the front layer), the systems packed behind
it, two layers of recessed side thrusters (nozzles flush with the hull: push up, down, left, right; never reverse) so it can correct its
path without turning its flank to the target, a service layer (side cameras, antenna) and the WHOLE rear face one bank of
ion thrusters, exhaust aft.  Nothing stands outside the W x H section (no merge latch: the section is a hard requirement).  No warhead: the kill
is kinetic.  Frame (core.py convention): x = Right (0..W-1), y = Up (0..H-1), z = Backward, nose front layer z = 0, cells are 0.5 m.
No reverse thrust (it brakes by flipping the nose round).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C

HEAVY = "SmallHeavyBlockArmorBlock"
CAP_COL = (0.07, 0.55, 0.10)        # nose cap accent
HULL_COL = (0.58, -0.78, -0.25)

def tube_frame(name, W, H, cap, body, spec, warheads=False):
    """W x H section, `cap` heavy nose layers, `body` layers before the thruster bank (z 0..body-1).  spec = dict with keys
    rc, pb, gyros, ai (AI Offensive cell, AI Flight cell), battery ('s' = one small battery per entry in bats, or 'L' = one 3x2x3 battery at bat_min), nose (cells), left, right, ant (cell, fwd)."""
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    script = script_text("TubeRamAI.cs")
    custom = f"[Tube]\nIGC Tag=TUBE\nBlock Tag={name}\n" + ("Max Rams=1\n" if warheads else "")
    # maneuver block: two extra layers (z = ins, ins+1) of recessed side thrusters (up, down, left, right; never reverse) go in front of
    # the service layer, so the cells the spec lists at z >= ins shift back by 2 and the body is 2 layers longer
    ins = spec["ins"]; body += 2
    sh = lambda c: (c[0], c[1], c[2] + 2 if c[2] >= ins else c[2])
    spec = dict(spec)
    for k in ("rc", "pb", "bat_min"):
        if spec.get(k): spec[k] = sh(spec[k])
    for k in ("gyros", "bats", "ai"):
        spec[k] = [sh(c) for c in spec.get(k, ())]
    spec["cams"] = [(sh(c), d, l) for c, d, l in spec["cams"]]
    a_, af, au = spec["ant"]; spec["ant"] = (sh(a_), af, au)
    # nose cap: heavy armour, cameras flush in the front layer
    nose = set(spec["nose"])
    # W variants: the second nose layer (z = 1) is a full layer of small warheads right under the front heavy-armour layer (same mass, same
    # length); the heavy layers behind it keep the ram mass. Sim A/B, 10 seeds, 4 tubes vs a Basilisk: this layer vs the last cap layer gave
    # no clear difference (3x3 W 17.2 vs 21.5 kHP, 4x4 W 19.6 vs 15.4 kHP); kept here, closest to the target's hull
    nw = 0
    for z in range(cap):
        for y in range(H):
            for x in range(W):
                if z == 0 and (x, y) in nose: continue
                if warheads and z == 1:
                    nw += 1
                    bp.add("MyObjectBuilder_Warhead", "SmallWarhead", (x, y, z), "Forward", "Up", f"{name} Warhead {nw}", (0.98, 0.65, 0.05), kind="warhead")
                    continue
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
    # recessed side thrusters: each is a 1 x 1 x 2 block lying in the hull with its nozzle flush with the outer face (exhaust Up/Down in layer ins,
    # Left/Right in layer ins+1); the push is the opposite way.  A column or row holds at most one, so the lists name distinct ones.
    zA, zB = ins, ins + 1
    for i, x in enumerate(spec["up"]):    C.thr(bp, (x, H - 2, zA), "Up", "Forward", f"{name} Ion Side Up {i+1}")
    for i, x in enumerate(spec["down"]):  C.thr(bp, (x, 0, zA), "Down", "Forward", f"{name} Ion Side Down {i+1}")
    for i, y in enumerate(spec["left"]):  C.thr(bp, (0, y, zB), "Left", "Up", f"{name} Ion Side Left {i+1}")
    for i, y in enumerate(spec["right"]): C.thr(bp, (W - 2, y, zB), "Right", "Up", f"{name} Ion Side Right {i+1}")
    # fill every empty body cell with light armour (closed box, nothing floats)
    occ = bp.occupancy()
    for z in range(body):
        for y in range(H):
            for x in range(W):
                if (x, y, z) not in occ: bp.armor(x, y, z, HULL_COL)
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
            bats=[(1, 1, 2), (0, 0, 3), (1, 0, 3), (0, 1, 3), (1, 1, 3), (1, 0, 4), (0, 1, 4)],
            cams=[((0, 0, 4), "Left", "Left")], ant=((1, 1, 4), "Backward", "Up"),  ai=[(1, 0, 5), (0, 1, 5)],
            ins=4, up=[0], down=[1], left=[1], right=[0]))
    if name == "Tube 2x4":      # 4 wide x 2 tall
        return dict(W=4, H=2, cap=3, body=7, spec=dict(
            nose=[(1, 1)], rc=(3, 0, 3), pb=(3, 0, 4), gyros=[(3, 1, 3)], bat_min=(0, 0, 3),
            cams=[((0, 0, 6), "Left", "Left"), ((3, 0, 6), "Right", "Right")], ant=((1, 1, 6), "Right", "Up"),  ai=[(3, 0, 5), (3, 1, 5)],
            ins=6, up=[0, 3], down=[1, 2], left=[0, 1], right=[0, 1]))
    if name == "Tube 3x3":
        return dict(W=3, H=3, cap=3, body=7, spec=dict(
            nose=[(1, 1)], rc=(0, 2, 3), pb=(0, 2, 4), gyros=[(1, 2, 3)], bat_min=(0, 0, 3),
            cams=[((0, 1, 6), "Left", "Left"), ((2, 1, 6), "Right", "Right")], ant=((1, 2, 6), "Right", "Up"),  ai=[(1, 2, 4), (2, 2, 4)],
            ins=6, up=[0], down=[2], left=[0], right=[2]))
    if name == "Tube 4x4":
        return dict(W=4, H=4, cap=4, body=8, spec=dict(
            nose=[(1, 1), (2, 2)], rc=(3, 0, 4), pb=(1, 2, 4), gyros=[(3, 1, 4), (0, 2, 4)], bat_min=(0, 0, 4), bats=[(2, 2, 4)],
            cams=[((0, 1, 7), "Left", "Left"), ((3, 1, 7), "Right", "Right")], ant=((1, 3, 7), "Right", "Up"),  ai=[(2, 2, 5), (3, 2, 5)],
            ins=7, up=[0, 3], down=[0, 3], left=[0, 3], right=[0, 3]))
    raise KeyError(name)

def build(name):
    w = name.endswith(" W")                         # "Tube 3x3 W": the warhead variant of "Tube 3x3"
    v = _variant(name[:-2] if w else name)
    return tube_frame(name, v["W"], v["H"], v["cap"], v["body"], v["spec"], warheads=w)

BASE = ("Tube 2x2", "Tube 2x4", "Tube 3x3", "Tube 4x4")
MODELS = {n: (lambda lite=False, n=n: build(n)) for n in BASE + tuple(b + " W" for b in BASE)}
NO_LITE = set(MODELS)
REQUIRED_AXES = {m: ("Forward", "Up", "Down", "Left", "Right") for m in MODELS}   # no reverse thrust: it brakes by flipping
DOCS = {
    'Tube 2x2': 'Kinetic ram drone, 1 x 1 m section, 5 m long, about 4.2 t, about 14 m/s2 forward and 3.4 m/s2 sideways: heavy-armour nose, 4 main ion thrusters on the rear face, 4 recessed side thrusters (up, down, left, right; no reverse), 7 small batteries. AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams while it can still fly.',
    'Tube 2x4': 'Kinetic ram drone, 2 m wide x 1 m tall section, 5.5 m long, about 7.1 t, about 16 m/s2 forward and 4 m/s2 sideways: heavy-armour nose, 4 MW battery, 8 main ion thrusters, 8 recessed side thrusters (2 per side, no reverse). AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams.',
    'Tube 3x3': 'Kinetic ram drone, 1.5 x 1.5 m section, 5.5 m long, about 7.3 t, about 18 m/s2 forward and 2 m/s2 sideways: heavy-armour nose, 4 MW battery, 9 main ion thrusters, 4 recessed side thrusters (no reverse). AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams.',
    'Tube 4x4': 'Kinetic ram drone, 2 x 2 m section, 6 m long, about 14.1 t, about 16 m/s2 forward and 2 m/s2 sideways: four-layer heavy-armour nose, 4 MW battery + 1 small, 16 main ion thrusters, 8 recessed side thrusters (2 per side, no reverse), two gyros. AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams.',
}
_WN = {'Tube 2x2 W': 4, 'Tube 2x4 W': 8, 'Tube 3x3 W': 9, 'Tube 4x4 W': 16}
for _m, _n in _WN.items():
    DOCS[_m] = (f'{_m[:-2]} with its second nose layer replaced by {_n} small warheads, right under the front heavy armour (same size and mass): it rams once and fires them on impact '
                '(velocity jump or the nose camera\'s contact fuse), armed only at the last moment (20 m from the hull or 0.3 s out, because gunfire sets armed warheads off) and never near its launch point.')
_NOTE = ('Main thrust forward, recessed side thrusters up/down/left/right, no reverse: it brakes by flipping. Finds targets with its AI Offensive / AI Flight blocks (AI Behavior must be on; the in-game behaviour of the AI blocks is unverified) or takes them from the fleet / hive / carrier channels or a player\'s `lock`. Ram damage is not verified in the game.')
MODEL_NOTES = {m: _NOTE for m in MODELS}
for _m in _WN: MODEL_NOTES[_m] = _NOTE + ' One-shot (Max Rams=1). The warheads are armed only by the script; keep the tube off friendly ships while it is in its final run.'
SCRIPTS = ['TubeRamAI.cs']

if __name__ == "__main__":
    bad = 0
    for n, fn in MODELS.items():
        try:
            bp = fn(False)
        except AssertionError as e:
            print(f'FAIL {n}: {e}'); bad += 1; continue
        ok, t = C.report_line(n, bp, 1.0, REQUIRED_AXES[n])
        print(t); bad += (not ok)
    sys.exit(1 if bad else 0)
