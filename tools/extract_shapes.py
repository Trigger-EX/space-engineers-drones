"""Extract game-written block shapes from a real blueprint into tools/shapes.json.

  python3 -I tools/extract_shapes.py ref/Starburst/bp.sbc tools/shapes.json

For every block type we use that the game itself serialized in the reference ship, keep what the game writes
AFTER the instance fields (everything instance specific is removed): the ComponentContainer, DeformationRatio,
and the derived-class fields after <Enabled>.  Entity ids, toolbars, waypoints, inventories and ammo are emptied so the
result is a clean "freshly placed block" template.  Keys are "Type/Subtype" (subtype of the smallest exemplar)
plus "Type/*" (smallest exemplar of the type).  Exemplars from nested <ProjectedGrids> are ignored.
"""
import sys, os, json, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xml.etree.ElementTree as ET
import sebp
from sebp import ser, frag, E, XSI

WANT = ['Thrust', 'CameraBlock', 'BatteryBlock', 'RadioAntenna', 'RemoteControl', 'TurretControlBlock', 'SmallMissileLauncherReload',
        'SmallMissileLauncher', 'SmallGatlingGun', 'Gyro', 'ShipConnector', 'MyProgrammableBlock', 'EventControllerBlock',
        'Projector', 'ShipWelder']
BASE = {'SubtypeName', 'EntityId', 'Min', 'IntegrityPercent', 'BuildPercent', 'BlockOrientation', 'ColorMaskHSV', 'SkinSubtypeId',
        'ConstructionInventory', 'ConstructionStockpile', 'Owner', 'BuiltBy', 'ShareMode', 'DeformationRatio', 'ComponentContainer',
        'CustomName', 'ShowOnHUD', 'ShowInTerminal', 'ShowInToolbarConfig', 'ShowInInventory', 'NumberInGrid', 'Enabled'}
TOOLBARS = {'Toolbar', 'BuildToolbar', 'OnLockedToolbar'}
ZERO = {'AzimuthId', 'ElevationId', 'CameraId', 'DirectionBlockId', 'BindedCamera', 'AttachedPB'}

def clean_container(cc):
    comps = cc.find('Components')
    for cd in list(comps):
        tid = cd.findtext('TypeId')
        if tid == 'MyModStorageComponentBase': comps.remove(cd); continue      # holds script/CustomData; sebp adds it on demand
        c = cd.find('Component')
        if tid == 'MyInventoryBase':
            c.find('Items').clear(); c.find('nextItemId').text = '0'
        elif tid == 'MyTimerComponent':
            c.find('FramesFromLastTrigger').text = '0'
    return cc if len(comps) else None

def clean_tail(e):
    t = e.tag
    if t in TOOLBARS:
        n = frag(sebp.EMPTY_TOOLBAR)[0]; n.tag = t; return n
    if t in ZERO: n = E(t, 0); return n
    if t == 'ToolIds' or t == 'SelectedBlocks': return E(t)
    if t == 'ConnectedEntityId': return None
    if t in ('Program', 'Storage'): return None
    if t == 'ProjectedGrids': return E(t)
    if t in ('ProjectionOffset', 'ProjectionRotation'):
        n = ET.Element(t)
        for a in 'XYZ': n.append(E(a, 0))
        return n
    if t in ('ProjectionsRemaining',): return E(t, 0)
    if t == 'GunBase':
        g = ET.Element('GunBase')
        g.append(E('CurrentAmmoMagazineName', e.findtext('CurrentAmmoMagazineName') or None))
        g.append(E('RemainingAmmosList')); g.append(E('LastShootTime', 0)); return g
    if t == 'Target' or t == 'PreviousControlledEntityId': return e
    return e

def exemplar(b):
    spec = {'level': 'func' if b.find('Enabled') is not None else 'term',
            'owner': b.find('ShareMode') is not None}
    if b.find('DeformationRatio') is not None: spec['deform'] = float(b.findtext('DeformationRatio'))
    if b.find('Enabled') is not None and b.findtext('Enabled') == 'false': spec['enabled'] = False
    cc = b.find('ComponentContainer')
    if cc is not None:
        cc = clean_container(cc)
        if cc is not None: spec['container'] = ser(cc).strip()
    tail = []
    for c in b:
        if c.tag in BASE: continue
        n = clean_tail(c)
        if n is not None: tail.append(n)
    spec['tail'] = ''.join(ser(t, 0) for t in tail)
    return spec

def main(src, dst):
    root = ET.parse(src).getroot()
    best = {}
    for g in root.iter('CubeGrid'):
        for b in g.find('CubeBlocks'):
            t = b.get(XSI + 'type').replace('MyObjectBuilder_', '')
            if t not in WANT: continue
            sub = b.findtext('SubtypeName') or ''
            sp = exemplar(b); size = len(json.dumps(sp))
            for k in (f'{t}/{sub}', f'{t}/*'):
                if k not in best or size < best[k][0]: best[k] = (size, sp)
    out = {k: v[1] for k, v in sorted(best.items())}
    json.dump(out, open(dst, 'w'), indent=1, sort_keys=True)
    print(f'{len(out)} shapes -> {dst}')
    for k in sorted(out):
        if k.endswith('/*'): print('  ', k)

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
