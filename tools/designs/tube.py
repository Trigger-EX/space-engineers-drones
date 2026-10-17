"""Tube drones: kinetic ram drones (Tube 2x2, Tube 2x4, Tube 3x3, Tube 4x4), small grid.  Design record: docs/design/tube_drones.md.

Module contract: MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.  Script scripts/TubeRamAI.cs is read at call time.
`python3 -I tools/designs/tube.py` builds + validates every model, one line each.

A tube is a closed box of armour: a solid heavy-armour nose cap (with the nose camera set flush in the front layer; on W tubes the second
layer is warheads), right behind it two layers of recessed side thrusters (nozzles flush with the hull: push up, down, left, right; never
reverse) so it can correct its path without turning its flank to the target, light armour filler, then the critical systems (RC, PB, gyros,
batteries, AI blocks, antenna, side cameras) in the layers just in front of the rear bank, as far from the impact and the blast as the
tube allows, and the WHOLE rear face one bank of ion thrusters, exhaust aft.  Nothing stands outside the W x H section (no merge latch:
the section is a hard requirement).  Frame (core.py convention): x = Right (0..W-1), y = Up (0..H-1), z = Backward, nose front layer z = 0,
cells are 0.5 m.  No reverse thrust (it brakes by flipping the nose round).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import *            # noqa: F401,F403
import _common as C

HEAVY = "SmallHeavyBlockArmorBlock"
CAP_COL = (0.07, 0.55, 0.10)        # nose cap accent
HULL_COL = (0.58, -0.78, -0.25)

def tube_frame(name, W, H, cap, body, spec, warheads=False):
    """W x H section, `cap` heavy nose layers, `body` layers before the thruster bank (z 0..body-1).  spec = dict (final cells) with keys
    ins (first of the two side-thruster layers), rc, pb, gyros, ai (AI Offensive cell, AI Flight cell), bats (one small battery each) and/or
    bat_min (one 3x2x3 battery), nose (cells), cams (side cameras), up/down/left/right (side thrusters), ant (cell, fwd, up).
    Layout front to back: nose cap (warheads in z = 1 on W), the two side-thruster layers, light armour filler, the systems (RC, PB, gyros,
    batteries, AI blocks, antenna, side cameras) in the layers just in front of the rear bank, so a nose hit or the tube's own blast has to
    get through the whole tube before it reaches them (owner, 2026-10-14)."""
    bp = Blueprint(name, "Small", seed=seed_for(name), legacy_shapes=True)
    script = script_text("TubeRamAI.cs")
    custom = f"[Tube]\nIGC Tag=TUBE\nBlock Tag={name}\n"
    # the AI Offensive block is the detector (as on the Ballista): it gets 20 s turns, the script's own spin 5 s (the defaults 6 / 8 s are
    # shorter than one AI search)
    custom += "Start Mode (WAIT/HUNT)=HUNT\n\n[AI Gate]\nAI Window (s)=20\nScript Search Window (s)=5\n"
    BF = _orient("Down", "Forward", along=("Up", "Down"))          # gyro: it mounts on its Bottom face only, set it facing forward
    ins = spec["ins"]
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
                if (x, y, z) in spec.get("decoys", ()): continue
                if warheads and z == 1:
                    nw += 1
                    bp.add("MyObjectBuilder_Warhead", "SmallWarhead", (x, y, z), "Forward", "Up", f"{name} Warhead {nw}", (0.98, 0.65, 0.05), kind="warhead")
                    continue
                bp.armor(x, y, z, CAP_COL if z == 0 else HULL_COL, sub=HEAVY)
    for i, (x, y) in enumerate(spec["nose"]):
        C.cam(bp, (x, y, 0), "Forward", "Up", f"{name} Camera Nose" + ("" if len(spec["nose"]) == 1 else f" {i+1}"), 3)
    # recessed side thrusters right behind the nose: each is a 1 x 1 x 2 block lying in the hull with its nozzle flush with the outer face
    # (exhaust Up/Down in layer ins, Left/Right in layer ins+1); the push is the opposite way.  A column or row holds at most one.
    zA, zB = ins, ins + 1
    for i, x in enumerate(spec["up"]):    C.thr(bp, (x, H - 2, zA), "Up", "Forward", f"{name} Ion Side Up {i+1}")
    for i, x in enumerate(spec["down"]):  C.thr(bp, (x, 0, zA), "Down", "Forward", f"{name} Ion Side Down {i+1}")
    for i, y in enumerate(spec["left"]):  C.thr(bp, (0, y, zB), "Left", "Up", f"{name} Ion Side Left {i+1}")
    for i, y in enumerate(spec["right"]): C.thr(bp, (W - 2, y, zB), "Right", "Up", f"{name} Ion Side Right {i+1}")
    # systems, at the rear (PB front face and gyro bottom face on the block ahead: their only mount faces)
    x, y, z = spec["rc"];  bp.add("MyObjectBuilder_RemoteControl", "SmallBlockRemoteControl", (x, y, z), name=f"{name} Remote Control", kind="ctrl")
    x, y, z = spec["pb"];  bp.add("MyObjectBuilder_MyProgrammableBlock", "SmallProgrammableBlock", (x, y, z), "Forward", "Up", f"{name} PB",
                                  extra=pb_extra(script), kind="ctrl", custom_data=custom)
    for i, (x, y, z) in enumerate(spec["gyros"]):
        bp.add("MyObjectBuilder_Gyro", "SmallBlockGyro", (x, y, z), BF[0], BF[1], f"{name} Gyroscope" + ("" if len(spec["gyros"]) == 1 else f" {i+1}"), kind="gyro")
    if spec.get("bat_min"):
        C.bat(bp, spec["bat_min"], f"{name} Battery", small=False)
    for i, c in enumerate(spec.get("bats", ())):
        C.bat(bp, c, f"{name} Battery {i+1}")
    # AI blocks (the script hands the ship to them while it has no target and confirms what they find with camera rays)
    for kind_, sub_, xt_, label_, cell_ in (("AI Offensive", "SmallOffensiveCombat", "MyObjectBuilder_OffensiveCombatBlock", "AI Offensive [AI]", spec["ai"][0]),
                                            ("AI Flight", "SmallFlightMovement", "MyObjectBuilder_FlightMovementBlock", "AI Flight [AI]", spec["ai"][1])):
        bp.add(xt_, sub_, cell_, name=f"{name} {label_}", kind="ai")
    # flush side cameras (they see the target round the tube when the nose camera is gone), antenna
    for (x, y, z), d, label in spec["cams"]:
        C.cam(bp, (x, y, z), d, "Up", f"{name} Camera {label}")
    (ax, ay, az), afwd, aup = spec["ant"]
    C.ant(bp, (ax, ay, az), afwd, aup, f"{name} Antenna", 2500)
    # decoys fixed in the hull (docs/design/tube_decoys.md): enemy turrets and AI blocks aim at the nearest working decoy, not the centre
    for i, c in enumerate(spec.get("decoys", ())):
        bp.add("MyObjectBuilder_Decoy", "SmallDecoy", c, "Forward", "Up", f"{name} Decoy {i+1}", DECOY_COL, kind="decoy")
    gaps = _flare_pods(bp, name, spec["pods"]) if spec.get("pods") else set()
    # fill every empty body cell with light armour (closed box, nothing floats; the flare bay's gaps stay open)
    occ = bp.occupancy()
    for z in range(body):
        for y in range(H):
            for x in range(W):
                if (x, y, z) not in occ and (x, y, z) not in gaps: bp.armor(x, y, z, HULL_COL)
    # thruster bank: one ion thruster per section cell, exhaust aft
    n = 0
    for y in range(H):
        for x in range(W):
            n += 1
            C.thr(bp, (x, y, body), "Backward", "Up", f"{name} Ion Main {n}")
    return finalize(bp)

DECOY_COL = (0.0, 0.9, 0.55)

def _flare_pods(bp, name, pods):
    """Flare bay (docs/design/tube_decoys.md): pods = dict(z0, corners, spine).  Each corner (x, y) holds a pod along z, front to back
    heavy armour (z0), decoy (z0+1, switched OFF until it is released), small battery (z0+2), pod merge block (z0+3, clamp Backward), and
    the tube's latch merge block behind it (z0+4, clamp Forward).  The bay's edge cells stay EMPTY, so a pod touches the tube only
    through the merge pair (in front of it is the nozzle end of a side thruster, which has no mount there): switching the latch off
    splits it off as its own grid.  The spine cells carry the hull through the bay.  Returns the set of cells that must stay empty."""
    z0, corners = pods["z0"], pods["corners"]
    for i, (x, y) in enumerate(corners):
        n = i + 1
        bp.armor(x, y, z0, CAP_COL, sub=HEAVY)
        bp.add("MyObjectBuilder_Decoy", "SmallDecoy", (x, y, z0 + 1), "Forward", "Up", f"{name} Flare Decoy {n}", DECOY_COL, kind="decoy", enabled=False)
        C.bat(bp, (x, y, z0 + 2), f"{name} Flare Battery {n}")
        # kind "flare": an internal merge pair, not a docking latch (check_docking only looks at kind "merge")
        C.merge_small(bp, (x, y, z0 + 3), "Backward", f"{name} Flare Merge {n}", color=DECOY_COL)['kind'] = "flare"
        C.merge_small(bp, (x, y, z0 + 4), "Forward", f"{name} Flare Latch {n}", color=DECOY_COL)['kind'] = "flare"
    for z in range(z0, z0 + 4):
        for (x, y) in pods["spine"]: bp.armor(x, y, z, HULL_COL, sub=HEAVY)   # heavy: the spine alone carries the hull through the bay
    W, H = pods["W"], pods["H"]
    keep = set(corners) | set(pods["spine"])
    return {(x, y, z) for z in range(z0, z0 + 4) for y in range(H) for x in range(W) if (x, y) not in keep}

def flare_pod_parts(bp):
    """Checks that every flare pod is held ONLY by its merge pair: the mount graph without the latch-merge edges must fall apart into the
    tube and one 4-block piece per pod.  Returns the number of pods."""
    adj = bp.attachment()
    byid = {id(b): b for b in bp.blocks}
    def cut(a, b):
        na, nb = byid[a]['name'] or '', byid[b]['name'] or ''
        return ('Flare Merge' in na and 'Flare Latch' in nb) or ('Flare Latch' in na and 'Flare Merge' in nb)
    adj2 = {k: {v for v in vs if not cut(k, v)} for k, vs in adj.items()}
    comps = bp.components(adj2)
    pods = [c for c in comps if any('Flare Decoy' in (b['name'] or '') for b in c)]
    main = [c for c in comps if c not in pods]
    assert len(main) == 1, f'{bp.name}: the tube falls apart without its flare pods ({len(main)} pieces)'
    for c in pods: assert len(c) == 4, f'{bp.name}: a flare pod holds {len(c)} blocks, expected 4 (it touches the tube outside its merge pair)'
    return len(pods)

# cells are final: nose z 0..cap-1, side thrusters z ins..ins+1 (ins = cap), filler, systems in the last layers before the bank (z = body)
def _variant(name):
    if name == "Tube 2x2":
        return dict(W=2, H=2, cap=2, body=8, spec=dict(
            nose=[(0, 1)], ins=2, up=[0], down=[1], left=[1], right=[0],
            bats=[(0, 0, 4), (1, 0, 4), (0, 1, 4), (1, 1, 4), (1, 0, 5), (0, 1, 5), (1, 1, 5)], cams=[((0, 0, 5), "Left", "Left")],
            ai=[(0, 0, 6), (1, 0, 6)], ant=((0, 1, 6), "Right", "Up"),
            gyros=[(0, 0, 7)], pb=(1, 0, 7), rc=(0, 1, 7)))
    if name == "Tube 2x4":      # 4 wide x 2 tall
        return dict(W=4, H=2, cap=3, body=9, spec=dict(
            nose=[(1, 1)], ins=3, up=[0, 3], down=[1, 2], left=[0, 1], right=[0, 1],
            bat_min=(0, 0, 5), ant=((3, 1, 6), "Backward", "Up"),
            pb=(0, 0, 8), rc=(1, 0, 8), gyros=[(2, 0, 8)], ai=[(1, 1, 8), (2, 1, 8)],
            cams=[((0, 1, 8), "Left", "Left"), ((3, 1, 8), "Right", "Right")]))
    if name == "Tube 3x3":
        return dict(W=3, H=3, cap=3, body=9, spec=dict(
            nose=[(1, 1)], ins=3, up=[0], down=[2], left=[0], right=[2],
            bat_min=(0, 0, 5), ai=[(0, 2, 7), (1, 2, 7)],
            pb=(0, 0, 8), rc=(1, 0, 8), gyros=[(2, 0, 8)], ant=((1, 2, 8), "Right", "Up"),
            cams=[((0, 1, 8), "Left", "Left"), ((2, 1, 8), "Right", "Right")]))
    if name == "Tube 4x4":
        return dict(W=4, H=4, cap=4, body=10, spec=dict(
            nose=[(1, 1), (2, 2)], ins=4, up=[0, 3], down=[0, 3], left=[0, 3], right=[0, 3],
            bat_min=(0, 0, 6), bats=[(1, 1, 9)],
            pb=(0, 0, 9), rc=(1, 0, 9), gyros=[(2, 0, 9), (0, 2, 9)], ai=[(2, 1, 9), (1, 2, 9)], ant=((1, 3, 9), "Right", "Up"),
            cams=[((0, 1, 9), "Left", "Left"), ((3, 1, 9), "Right", "Right")]))
    # Tube 4x4 D: four decoys in the second nose layer, behind the front heavy armour (aim point pulled onto the nose)
    if name == "Tube 4x4 D":
        v = _variant("Tube 4x4"); v["spec"] = dict(v["spec"], decoys=[(1, 1, 1), (2, 1, 1), (1, 2, 1), (2, 2, 1)]); return v
    # Tube 4x4 DR (experiment control): four decoys at the rear, in the filler in front of the systems (aim point pulled onto the systems)
    if name == "Tube 4x4 DR":
        v = _variant("Tube 4x4"); v["spec"] = dict(v["spec"], decoys=[(3, 2, 8), (3, 3, 8), (0, 3, 8), (1, 3, 8)]); return v
    # Tube 4x4 F: the 4x4 with a flare bay (4 corner pods, z 6..9) behind the side thrusters; latches, battery and filler in z 10..12,
    # systems in z 13, bank z 14..15.  The 3x2x3 battery moves off the corners (x 1..3, y 1..2) so the latch corners are free.
    if name == "Tube 4x4 F":
        return dict(W=4, H=4, cap=4, body=14, spec=dict(
            nose=[(1, 1), (2, 2)], ins=4, up=[0, 3], down=[0, 3], left=[0, 3], right=[0, 3],
            pods=dict(z0=6, W=4, H=4, corners=[(0, 0), (3, 0), (0, 3), (3, 3)], spine=[(1, 1), (2, 1), (1, 2), (2, 2)]),
            bat_min=(1, 1, 10), bats=[(1, 1, 13)],
            pb=(0, 0, 13), rc=(1, 0, 13), gyros=[(2, 0, 13), (0, 2, 13)], ai=[(2, 1, 13), (1, 2, 13)], ant=((1, 3, 13), "Right", "Up"),
            cams=[((0, 1, 13), "Left", "Left"), ((3, 1, 13), "Right", "Right")]))
    raise KeyError(name)

# ---------------------------------------------------------------------------------------------
# LARGE-grid tubes: Tube L 1x1 (a long one-block column) and Tube L 2x2.  Same idea, large blocks (2.5 m cells): heavy-armour nose
# (second layer = large warheads in the W variants), side thrusters right behind it (push up, down, left, right; no reverse), filler,
# RC / PB / batteries / gyro / AI blocks / compact antenna just in front of the rear bank, and heavy armour panels bolted round the body (gunfire killed the small tubes'
# systems from the side before they reached a Basilisk in the sim).  Large-grid quirks: the large ion thruster is 1 x 1 x 2, so the 1x1
# column cannot recess its side thrusters (each sits half in the column, its nozzle flush with the panels) and gets a cruciform rear bank of
# five (one thruster would give 3 m/s2); PB, gyro and compact antenna mount on their Bottom face only, so they face it forward onto the
# block in front; the 1x1's nose cameras sit above and below the nose, one block back, so the contact fuse adds that setback.
# No merge latch: the racks and printers are small grid.

LHEAVY, LLIGHT, LPANEL = "LargeHeavyBlockArmorBlock", "LargeBlockArmorBlock", "LargeArmorPanelHeavy"

def _orient(face, gdir, along=None):
    """(fwd, up) for a block whose local `face` points along grid direction `gdir`; with `along`, prefer an orientation whose local Forward
    lies on that grid axis (so panels in a row meet edge to edge)"""
    cands = []
    for f in DIR:
        for u in DIR:
            if sum(a * b for a, b in zip(DIR[f], DIR[u])): continue
            r = sebp.cross(DIR[f], DIR[u])
            loc = {"Forward": DIR[f], "Backward": tuple(-v for v in DIR[f]), "Up": DIR[u], "Down": tuple(-v for v in DIR[u]),
                   "Right": r, "Left": tuple(-v for v in r)}
            if loc[face] == DIR[gdir]: cands.append((f, u))
    if along: cands.sort(key=lambda fu: 0 if fu[0] in along else 1)
    return cands[0]

def tube_large(name):
    w = name.endswith(" W"); base = name[:-2] if w else name
    bp = Blueprint(name, "Large", seed=seed_for(name), legacy_shapes=True)
    script = script_text("TubeRamAI.cs")
    one = base == "Tube L 1x1"
    custom = (f"[Tube]\nIGC Tag=TUBE\nBlock Tag={name}\n" + "Start Mode (WAIT/HUNT)=HUNT\n"
              + ("Nose Camera Setback (m)=2.5\n" if one else "") + "\n[AI Gate]\nAI Window (s)=20\nScript Search Window (s)=5\n")
    BF = _orient("Down", "Forward", along=("Up", "Down"))          # Bottom face forward (PB, gyro, compact antenna)
    def heavy(c, col=HULL_COL): bp.armor(*c, color=col, sub=LHEAVY)
    def light(c): bp.armor(*c, color=HULL_COL, sub=LLIGHT)
    def warhead(c, i): bp.add("MyObjectBuilder_Warhead", "LargeWarhead", c, "Forward", "Up", f"{name} Warhead {i}", (0.98, 0.65, 0.05), kind="warhead")
    def rc(c): bp.add("MyObjectBuilder_RemoteControl", "LargeBlockRemoteControl", c, "Forward", "Up", f"{name} Remote Control", kind="ctrl")
    def pb(c): bp.add("MyObjectBuilder_MyProgrammableBlock", "LargeProgrammableBlock", c, BF[0], BF[1], f"{name} PB", extra=pb_extra(script), kind="ctrl", custom_data=custom)
    def gyro(c, n=""): bp.add("MyObjectBuilder_Gyro", "LargeBlockGyro", c, BF[0], BF[1], f"{name} Gyroscope{n}", kind="gyro")
    def ai(c, off): bp.add("MyObjectBuilder_OffensiveCombatBlock" if off else "MyObjectBuilder_FlightMovementBlock", "LargeOffensiveCombat" if off else "LargeFlightMovement",
                           c, "Forward", "Up", f"{name} AI {'Offensive' if off else 'Flight'} [AI]", kind="ai")
    def antenna(c): bp.add("MyObjectBuilder_RadioAntenna", "LargeBlockCompactRadioAntenna", c, BF[0], BF[1], f"{name} Antenna", extra=C.ant_extra(2500), kind="ant")
    nbat = [0]
    def battery(c): nbat[0] += 1; C.bat(bp, c, f"{name} Battery {nbat[0]}")
    def side(c, d, label): C.thr(bp, c, d, "Forward" if d in ("Up", "Down") else "Up", f"{name} Ion Side {label}")
    nmain = [0]
    def main(c): nmain[0] += 1; C.thr(bp, c, "Backward", "Up", f"{name} Ion Main {nmain[0]}")
    PANEL_TO = {(-1, 0): "Right", (1, 0): "Left", (0, -1): "Up", (0, 1): "Down"}     # ring offset -> grid direction of the hull it is bolted to
    def panels(cells, skip):
        occ = bp.occupancy()
        for c in cells:
            if c in occ or c in skip: continue
            x, y, z = c
            off = (-1 if x < 0 else 1 if x >= W else 0, -1 if y < 0 else 1 if y >= H else 0)
            f, u = _orient("Right", PANEL_TO[off], along=("Forward", "Backward"))
            bp.add("MyObjectBuilder_CubeBlock", LPANEL, c, f, u, color=HULL_COL)
    if one:
        W = H = 1
        heavy((0, 0, 0), CAP_COL)
        if w: warhead((0, 0, 1), 1)
        else: heavy((0, 0, 1))
        C.cam(bp, (0, 1, 1), "Forward", "Up", f"{name} Camera Nose 1", 3); heavy((0, 1, 2))
        C.cam(bp, (0, -1, 1), "Forward", "Up", f"{name} Camera Nose 2", 3); heavy((0, -1, 2))
        heavy((0, 0, 2))
        # side thrusters right behind the nose (the nose cameras' backing blocks take z 2 above and below the column)
        side((-1, 0, 3), "Left", "Left"); side((0, 0, 4), "Right", "Right")
        side((0, 0, 5), "Up", "Up"); side((0, -1, 6), "Down", "Down")
        light((0, 0, 7)); light((0, 0, 8))
        # systems just in front of the rear bank; PB / gyro / antenna face their only mount face (Bottom) forward onto the block ahead
        antenna((0, 0, 9))                                    # z 9..11
        ai((0, 0, 12), True); ai((0, 0, 13), False)
        battery((0, 0, 14)); gyro((0, 0, 15)); battery((0, 0, 16)); pb((0, 0, 17)); battery((0, 0, 18))
        rc((0, 0, 19))                                        # its back face carries the bulkhead
        light((0, 0, 20))
        for c in ((-1, 0, 20), (1, 0, 20), (0, -1, 20), (0, 1, 20)): light(c)          # bulkhead the outer rear thrusters sit on
        for c in ((0, 0, 21), (-1, 0, 21), (1, 0, 21), (0, -1, 21), (0, 1, 21)): main(c)
        ring = [(x, y, z) for z in range(2, 20) for (x, y) in ((-1, 0), (1, 0), (0, -1), (0, 1))]
        panels(ring, skip=set(bp.reserve))
    else:
        W = H = 2
        for x in (0, 1):
            for y in (0, 1):
                if (x, y) != (0, 1): heavy((x, y, 0), CAP_COL)
        C.cam(bp, (0, 1, 0), "Forward", "Up", f"{name} Camera Nose", 3)
        i = 0
        for x in (0, 1):
            for y in (0, 1):
                if w: i += 1; warhead((x, y, 1), i)
                else: heavy((x, y, 1))
        # side thrusters right behind the nose, filler, then the systems just in front of the rear bank
        side((0, 0, 2), "Up", "Up"); side((1, 0, 2), "Down", "Down")          # columns: nozzles flush with the top / bottom face
        side((0, 1, 3), "Left", "Left"); side((0, 0, 3), "Right", "Right")    # rows: nozzles flush with the left / right face
        for c in ((0, 0, 4), (1, 0, 4), (0, 1, 4), (1, 1, 4), (0, 0, 5), (1, 0, 5), (0, 1, 5), (1, 1, 5)): light(c)
        battery((0, 0, 6)); battery((0, 1, 6)); antenna((1, 0, 6)); light((1, 1, 6))          # antenna z 6..8
        pb((0, 0, 7)); gyro((0, 1, 7)); ai((1, 1, 7), True)                                    # PB and gyro: Bottom face on a battery
        rc((0, 0, 8)); ai((0, 1, 8), False); light((1, 1, 8))
        for x in (0, 1):
            for y in (0, 1): main((x, y, 9))
        ring = [(x, y, z) for z in range(2, 9) for (x, y) in ((-1, 0), (-1, 1), (2, 0), (2, 1), (0, -1), (1, -1), (0, 2), (1, 2))]
        panels(ring, skip=set(bp.reserve))
    return finalize(bp)

def build(name):
    if name.startswith("Tube L "): return tube_large(name)
    w = name.endswith(" W")                         # "Tube 3x3 W": the warhead variant of "Tube 3x3"
    v = _variant(name[:-2] if w else name)
    bp = tube_frame(name, v["W"], v["H"], v["cap"], v["body"], v["spec"], warheads=w)
    if v["spec"].get("pods"): flare_pod_parts(bp)
    return bp

BASE = ("Tube 2x2", "Tube 2x4", "Tube 3x3", "Tube 4x4", "Tube L 1x1", "Tube L 2x2")
DECOY = ("Tube 4x4 D", "Tube 4x4 DR", "Tube 4x4 F")      # decoy variants (docs/design/tube_decoys.md)
MODELS = {n: (lambda lite=False, n=n: build(n)) for n in BASE + tuple(b + " W" for b in BASE) + DECOY}
NO_LITE = set(MODELS)
REQUIRED_AXES = {m: ("Forward", "Up", "Down", "Left", "Right") for m in MODELS}   # no reverse thrust: it brakes by flipping
DOCS = {
    'Tube 2x2': 'Kinetic ram drone, 1 x 1 m section, 5 m long, about 4.2 t, about 14 m/s2 forward and 3.4 m/s2 sideways: heavy-armour nose, 4 main ion thrusters on the rear face, 4 recessed side thrusters (up, down, left, right; no reverse), 7 small batteries. AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams while it can still fly.',
    'Tube 2x4': 'Kinetic ram drone, 2 m wide x 1 m tall section, 5.5 m long, about 7.1 t, about 16 m/s2 forward and 4 m/s2 sideways: heavy-armour nose, 4 MW battery, 8 main ion thrusters, 8 recessed side thrusters (2 per side, no reverse). AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams.',
    'Tube 3x3': 'Kinetic ram drone, 1.5 x 1.5 m section, 5.5 m long, about 7.3 t, about 18 m/s2 forward and 2 m/s2 sideways: heavy-armour nose, 4 MW battery, 9 main ion thrusters, 4 recessed side thrusters (no reverse). AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams.',
    'Tube 4x4': 'Kinetic ram drone, 2 x 2 m section, 6 m long, about 14.1 t, about 16 m/s2 forward and 2 m/s2 sideways: four-layer heavy-armour nose, 4 MW battery + 1 small, 16 main ion thrusters, 8 recessed side thrusters (2 per side, no reverse), two gyros. AI Offensive + AI Flight blocks (it can search alone). No weapon: it flies into the target at full speed and re-rams.',
}
DOCS['Tube L 1x1'] = ('LARGE-grid ram drone: a one-block (2.5 m) column 52 m long plus a cruciform tail, about 110 t, about 16 m/s2 forward and 3 m/s2 sideways: '
                      'two-block heavy-armour nose, heavy armour panels round the body, 3 large batteries, one large gyro, 4 side thrusters half sunk in the column (no reverse), '
                      '5 rear thrusters, AI Offensive + AI Flight, compact antenna, nose cameras above and below the nose. Re-rams while it can fly. No merge latch.')
DOCS['Tube L 2x2'] = ('LARGE-grid ram drone: 5 x 5 m section, 27.5 m long, about 105 t, about 13 m/s2 forward and 3.3 m/s2 sideways: two-layer heavy-armour nose with a flush '
                      'camera, heavy armour panels round the body, 2 large batteries, one large gyro, 4 recessed side thrusters (no reverse), 4 rear thrusters, AI Offensive + AI Flight, '
                      'compact antenna. Re-rams while it can fly. No merge latch.')
DOCS['Tube L 1x1 W'] = 'Tube L 1x1 with its second nose block a large warhead (22 m blast): fired on impact or by the contact fuse, armed only at the last moment; if the tube survives its blast it comes back as a kinetic ram.'
DOCS['Tube L 2x2 W'] = 'Tube L 2x2 with its second nose layer 4 large warheads (22 m blast): fired on impact or by the contact fuse, armed only at the last moment; if the tube survives its blast it comes back as a kinetic ram.'
DOCS['Tube 4x4 D'] = 'Tube 4x4 with four decoys in its second nose layer, under the front heavy armour: enemy turrets and AI blocks aim at the nose (decoy-armoured nose), not at the systems at the rear. Experimental (docs/design/tube_decoys.md).'
DOCS['Tube 4x4 DR'] = 'Experiment control: Tube 4x4 with four decoys in the filler right in front of its rear systems (turret aim pulled onto the systems). Not for use (docs/design/tube_decoys.md).'
DOCS['Tube 4x4 F'] = ('Tube 4x4 with a flare bay: four corner pods (heavy plate, decoy, small battery, merge block) held only by merge pairs inside the 2 x 2 m section. '
                      'In the fight it drops them one by one like flares: each pod flies on as its own grid with its decoy switched on, and enemy turrets and AI blocks switch to it. '
                      '8 m long, about 16.5 t. Experimental (docs/design/tube_decoys.md).')
_WN = {'Tube 2x2 W': 4, 'Tube 2x4 W': 8, 'Tube 3x3 W': 9, 'Tube 4x4 W': 16}
for _m, _n in _WN.items():
    DOCS[_m] = (f'{_m[:-2]} with its second nose layer replaced by {_n} small warheads, right under the front heavy armour (same size and mass): it fires them on its first impact '
                '(velocity jump or the nose camera\'s contact fuse), armed only at the last moment (20 m from the hull or 0.3 s out, because gunfire sets armed warheads off) and never near its launch point; '
                'if it survives its own blast (systems at the rear) it comes round for a second, kinetic ram.')
_NOTE = ('Main thrust forward, recessed side thrusters up/down/left/right, no reverse: it brakes by flipping. Hunts by itself once off its rack (Start Mode HUNT). Finds targets with its AI Offensive block, rescanning every 5 s, confirmed by nose-camera rays (AI Behavior must be on; the in-game behaviour of an unarmed grid\'s AI blocks is unverified) or takes them from the fleet / hive / carrier channels or a player\'s `lock`. Ram damage is not verified in the game.')
MODEL_NOTES = {m: _NOTE for m in MODELS}
MODEL_NOTES['Tube 4x4 F'] = _NOTE + ' Flare pods: `[Tube] Flare Triggers` in Custom Data says when they go (FIRE = when hit, RANGE, TERM, EXTEND, ALL); `flare` drops one by hand. Unverified in the game: that a decoy needs no more than its pod battery, and how strongly real turrets prefer a decoy grid.'
MODEL_NOTES['Tube 4x4 D'] = _NOTE + ' Decoys in the nose: unverified in the game how far real turrets follow a decoy inside armour.' 
for _m in list(_WN) + ['Tube L 1x1 W', 'Tube L 2x2 W']: MODEL_NOTES[_m] = _NOTE + ' The warheads go off on the first ram; a tube that survives its blast re-rams as a kinetic ram. The warheads are armed only by the script; keep the tube off friendly ships while it is in its final run.'
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
