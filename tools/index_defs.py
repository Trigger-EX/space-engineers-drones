"""Index vanilla CubeBlocks definitions -> JSON (id, size, grid, mount points, DLC, display name)."""
import sys, os, json, glob
import xml.etree.ElementTree as ET
src, out = sys.argv[1], sys.argv[2]
XSI = '{http://www.w3.org/2001/XMLSchema-instance}type'
defs = {}
for f in sorted(glob.glob(os.path.join(src, '**', '*.sbc'), recursive=True)):
    try:
        root = ET.parse(f).getroot()
    except ET.ParseError as e:
        print('skip', f, e); continue
    for d in root.iter('Definition'):
        idn = d.find('Id')
        if idn is None: continue
        t = (idn.findtext('TypeId') or idn.get('Type') or '').strip()
        st = (idn.findtext('SubtypeId') or idn.get('Subtype') or '').strip()
        if not t: continue
        sz = d.find('Size')
        def g(n): 
            v = sz.get(n) if sz.get(n) is not None else sz.get(n.upper())
            if v is None: v = sz.findtext(n) or sz.findtext(n.upper())
            return int(float(v)) if v is not None else 1
        size = [g('x'), g('y'), g('z')] if sz is not None else [1,1,1]
        mps = []
        mpn = d.find('MountPoints')
        if mpn is not None:
            for mp in mpn.findall('MountPoint'):
                if mp.get('Enabled', 'true').lower() == 'false': continue
                mps.append(dict(side=mp.get('Side'), sx=float(mp.get('StartX', 0)), sy=float(mp.get('StartY', 0)),
                                ex=float(mp.get('EndX', 0)), ey=float(mp.get('EndY', 0)),
                                excl=mp.get('ExclusionMask'), prop=mp.get('PropertiesMask'), default=mp.get('Default')))
        key = f'{t}/{st}'
        defs[key] = dict(type=t, subtype=st, xsi=d.get(XSI), grid=d.findtext('CubeSize'), size=size,
                         mounts=mps if mpn is not None else None, display=d.findtext('DisplayName'),
                         public=d.findtext('Public'), dlc=[x.text for x in d.findall('DLC')], pair=d.findtext('BlockPairName'),
                         file=os.path.basename(f))
json.dump(defs, open(out, 'w'), indent=0)
print(len(defs), 'definitions indexed')
