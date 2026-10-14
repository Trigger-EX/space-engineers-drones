"""Spare Programmable Blocks: bring every blueprint to at least three PBs (headroom for modules that move off a full PB over IGC; CLAUDE.md "Script size").

    import spares
    spares.add_spare_pbs(bp, total=3, name='[Spare] {model} PB {i}')        # last thing in a builder, before C.finalize(bp)
    python3 -I tools/designs/spares.py            PB count of every shipped blueprint (both variants), one line each; exit 1 if any has fewer than 3

A spare is a Programmable Block with NO program and empty Custom Data (or `custom_data`), enabled, that
  * sits inside the blueprint's existing cell box (the dimensions never grow: printer bays, pod cradles and docks are sized from them),
  * is seated by a mount rectangle on a neighbour (pods.orient_attached: the same rule as the game) and never in a keep-clear (exhaust / muzzle / camera) cell,
  * goes to the most enclosed free cell (most occupied faces = most protected), nearest the existing PB / controller among equals; when no free cell fits it
    takes the place of a plain 1x1x1 armour block (`swap_armor`), most enclosed first, and only if the grid stays one connected piece,
  * is named so that no script mistakes it for something else (see NAMES below).

NAMES.  Scripts find their PBs by name, so a spare's name must (1) not START with a role tag a script looks up: [Helm] [Staff] [Flight] [Spotter] [Bay [Pod] [TU] [Thrust]
and (2) not contain `Hive Mother`.  Small drones use `[Spare] <Model> PB <i>`: CradleBayAI.Mine() skips every name that starts with `[`, and HiveMotherAI.FindDrone() only
takes PBs whose name starts with the bay Prefix (the drone's model name), so a spare is never taken for the drone's own PB.  Ships use their block prefix after the tag
(`[Spare][SH] <Ship> PB <i>`) so the Helm's `[Link] Block Prefix` still sees it as the ship's block.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sebp
from sebp import DIR
import pods

PB_X = 'MyObjectBuilder_MyProgrammableBlock'
RESERVED_START = ('[helm]', '[staff]', '[flight]', '[spotter]', '[bay', '[pod]', '[tu]', '[thrust]')
FACES = list(DIR.values())

def pb_count(bp):
    return sum(1 for b in bp.blocks if b['xtype'] == PB_X)

def _neighbours(occ, c):
    return sum(1 for d in FACES if (c[0] + d[0], c[1] + d[1], c[2] + d[2]) in occ)

def _anchor(bp):
    for key in ('pb', 'rc', 'ctrl'):
        for b in bp.blocks:
            if (key == 'pb' and b['xtype'] == PB_X) or (key == 'rc' and b['xtype'].endswith('RemoteControl')) or (key == 'ctrl' and b.get('kind') == 'ctrl'):
                return bp.cells(b)[0]
    return (0, 0, 0)

def _dist2(a, b):
    return sum((a[i] - b[i]) ** 2 for i in range(3))

def check_name(name):
    n = name.lower()
    assert not n.startswith(RESERVED_START), f'spare PB name {name!r} starts with a tag a script looks up'
    assert 'hive mother' not in n, f'spare PB name {name!r} contains "Hive Mother"'

def _try(bp, occ, cell, sub, kw):
    """add a PB at `cell` in any attached orientation; returns the block or None (bp.blocks and occ are left unchanged on failure)"""
    try:
        return pods.orient_attached(bp, PB_X, sub, cell, occ=occ, **kw)
    except AssertionError:
        return None

def add_spare_pbs(bp, total=3, name='[Spare] {model} PB {i}', model=None, swap_armor=True, custom_data=None, min_enclosed=1, anchor=None, max_tries=300, color=None, extra=None, reserved_ok=False):
    """add PBs until `bp` has `total` (default 3); returns the new blocks.  `name` takes {model} (default bp.name) and {i} (the PB's number: have+1 ...)."""
    have = pb_count(bp)
    need = total - have
    if need <= 0: return []
    sub = 'LargeProgrammableBlock' if bp.grid == 'Large' else 'SmallProgrammableBlock'
    model = model or bp.name
    anchor = anchor or _anchor(bp)
    dims0 = bp.stats()['dims']
    first = next((b for b in bp.blocks if b['xtype'] == PB_X), None)
    color = color or (first['color'] if first else (0, -0.8, -0.3))
    out = []
    for k in range(need):
        i = have + k + 1
        nm = name.format(model=model, i=i)
        if not reserved_ok: check_name(nm)      # (reserved_ok: the PB is meant to carry a script that looks up the name, e.g. the Staff the Helm links to)
        kw = dict(name=nm, color=color, kind='ctrl', custom_data=custom_data)
        if extra: kw['extra'] = extra
        occ = bp.occupancy()
        xs = [c[0] for c in occ]; ys = [c[1] for c in occ]; zs = [c[2] for c in occ]
        lo = (min(xs), min(ys), min(zs)); hi = (max(xs), max(ys), max(zs))
        free = [(x, y, z) for x in range(lo[0], hi[0] + 1) for y in range(lo[1], hi[1] + 1) for z in range(lo[2], hi[2] + 1)
                if (x, y, z) not in occ and (x, y, z) not in bp.reserve]
        free = [c for c in free if _neighbours(occ, c) >= min_enclosed]
        free.sort(key=lambda c: (-_neighbours(occ, c), _dist2(c, anchor), c))
        placed = None
        for c in free[:max_tries]:
            placed = _try(bp, occ, c, sub, kw)
            if placed:
                try: bp.validate()
                except AssertionError:
                    bp.blocks.remove(placed); placed = None; continue
                break
        if placed is None and swap_armor:
            plain = [b for b in bp.blocks if b.get('kind') == 'armor' and b['size'] == (1, 1, 1) and b['sub'] in ('SmallBlockArmorBlock', 'LargeBlockArmorBlock')]
            plain.sort(key=lambda b: (-_neighbours(occ, tuple(b['min'])), _dist2(tuple(b['min']), anchor), tuple(b['min'])))
            for old in plain[:max_tries]:
                idx = bp.blocks.index(old)
                bp.blocks.pop(idx)
                occ2 = bp.occupancy()
                placed = _try(bp, occ2, tuple(old['min']), sub, kw)
                if placed:
                    try: bp.validate()
                    except AssertionError:
                        bp.blocks.remove(placed); placed = None
                if placed: break
                bp.blocks.insert(idx, old)
        assert placed is not None, f'{bp.name}: no cell for spare PB {i} (free cells tried: {min(len(free), max_tries)}, swap_armor={swap_armor})'
        assert bp.stats()['dims'] == dims0, f'{bp.name}: the spare PB grew the blueprint {dims0} -> {bp.stats()["dims"]}'
        out.append(placed)
    return out

def report():
    import designs
    mods, _ = designs.load()
    bad = 0
    for kind, mod, model, fn in designs.iter_models(mods):
        if kind == 'test': continue
        for lite in (False, True):
            key = model + (' Lite' if lite else '')
            try:
                bp = fn(lite)
            except Exception as e:
                print(f'ERR  {key}: {str(e)[:120]}'); bad += 1; continue
            n = pb_count(bp)
            print(f'{"ok " if n >= 3 else "LOW"}  {key:26} {kind:8} {n} PB  ' + ', '.join(b['name'] for b in bp.blocks if b['xtype'] == PB_X))
            bad += n < 3
    return 1 if bad else 0

if __name__ == '__main__':
    sys.exit(report())
