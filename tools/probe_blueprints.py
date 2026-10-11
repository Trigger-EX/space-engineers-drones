"""Generate tiny single-block "paste probe" blueprints so a paste failure can be pinned on ONE block type.

  python3 -I tools/probe_blueprints.py out/probes

Each probe is two armour cubes plus one block of the type under test, placed (by the validator) on a face where it
really attaches.  Paste them with F10 in order (docs/xml-notes.md, section "In-game test plan").  P00 is the
control (armour only), the last one has every type together.  Folder names sort in risk order.
"""
import sys, os, itertools, zlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sebp

PROBES = [   # (folder, xtype, subtype, settings/custom_data kwargs)
    ('P01_Thrust_Gyro_Camera', None, None, {}),
    ('P02_Sensor', 'SensorBlock', 'SmallBlockSensor', {}),
    ('P03_TurretController', 'TurretControlBlock', 'SmallTurretControlBlock', {}),
    ('P04_Timer', 'TimerBlock', 'TimerBlockSmall', {}),
    ('P05_AI_Offensive', 'OffensiveCombatBlock', 'SmallOffensiveCombat', {}),
    ('P06_AI_Flight', 'FlightMovementBlock', 'SmallFlightMovement', {}),
    ('P07_AI_Defensive', 'DefensiveCombatBlock', 'SmallDefensiveCombat', {}),
    ('P08_AI_BasicMission', 'BasicMissionBlock', 'SmallBasicMission', {}),
    ('P09_EventController', 'EventControllerBlock', 'EventControllerSmall', {}),
    ('P10_Merge', 'MergeBlock', 'SmallShipSmallMergeBlock', {}),
    ('P11_Warhead', 'Warhead', 'SmallWarhead', {}),
    ('P12_Decoy', 'Decoy', 'SmallDecoy', {}),
    ('P13_Projector', 'Projector', 'SmallProjector', {}),
    ('P14_Welder', 'ShipWelder', 'SmallShipWelder', {}),
    ('P15_Gatling', 'SmallGatlingGun', '', {}),
    ('P16_RocketLauncher', 'SmallMissileLauncher', '', {}),
    ('P17_PB_CustomData', 'MyProgrammableBlock', 'SmallProgrammableBlock',
     dict(custom_data='PROBE-CUSTOMDATA-OK\nline2', extra='<Program>// probe: if you can read this the program text survived\n</Program><Storage />')),
]
ORIENT = [(f, u) for f in sebp.DIR for u in sebp.DIR if sum(a*b for a, b in zip(sebp.DIR[f], sebp.DIR[u])) == 0]

def place(bp, xtype, sub, name, **kw):
    """first (orientation, position) around the armour pair at the origin where the block validates as attached"""
    for fwd, up in ORIENT:
        for mn in itertools.product(range(-6, 4), repeat=3):
            n = len(bp.blocks)
            try:
                bp.add('MyObjectBuilder_' + xtype, sub, mn, fwd, up, name, kind='ai', **kw)
                bp.validate()
                return bp.blocks[-1]
            except AssertionError:
                del bp.blocks[n:]
    raise RuntimeError(f'could not attach {xtype}/{sub}')

def build(name, items):
    bp = sebp.Blueprint(name, 'Small', seed=zlib.crc32(name.encode()) & 0xffff)
    bp.armor(0, 0, 0); bp.armor(1, 0, 0)
    for xtype, sub, kw in items:
        place(bp, xtype, sub, f'{name} {xtype}', **kw)
    bp.validate()
    return bp

def main(outdir):
    palette = {'armor': (92, 100, 112), 'ai': (60, 150, 170)}
    allitems = []
    bps = [build('P00_Control_ArmourOnly', [])]
    for folder, xtype, sub, kw in PROBES:
        items = [('Thrust', 'SmallBlockSmallThrust', {}), ('Gyro', 'SmallBlockGyro', {}), ('CameraBlock', 'SmallCameraBlock', {})] if xtype is None \
            else [(xtype, sub, kw)]
        if xtype: allitems += items
        bps.append(build(folder, items))
    bps.append(build('P99_Everything', allitems))
    for bp in bps:
        d = os.path.join(outdir, bp.name)
        bp.write(d, version='probe')
        bp.thumb(os.path.join(d, 'thumb.png'), bp.name, 'paste probe', palette)
        print(f'{bp.name}: {len(bp.blocks)} blocks -> {d}')

if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'out/probes')
