"""XML helpers shared by sebp.py and its extension modules (moved out of sebp.py unchanged).

ElementTree based, serialized the way the game's XmlSerializer lays things out: two-space indent, `<a />` for empty
elements, `xsi:` prefix for the schema-instance attributes.
"""
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

XSI_NS = 'http://www.w3.org/2001/XMLSchema-instance'
XSI = '{%s}' % XSI_NS

def _qn(k):
    return 'xsi:' + k[len(XSI):] if k.startswith(XSI) else k

def ser(e, ind=0):
    pad = '  ' * ind
    attrs = ''.join(f' {_qn(k)}="{escape(v, {chr(34): "&quot;"})}"' for k, v in e.attrib.items())
    kids = list(e)
    if not kids:
        if not e.text: return f'{pad}<{e.tag}{attrs} />\n'
        return f'{pad}<{e.tag}{attrs}>{escape(e.text)}</{e.tag}>\n'
    return f'{pad}<{e.tag}{attrs}>\n' + ''.join(ser(k, ind + 1) for k in kids) + f'{pad}</{e.tag}>\n'

def frag(xml):
    """parse an XML fragment (zero or more sibling elements) -> list of Elements"""
    root = ET.fromstring(f'<r xmlns:xsi="{XSI_NS}">{xml}</r>')
    for e in root.iter():
        if len(e) and (e.text or '').strip() == '': e.text = None
        if e.tail is not None: e.tail = None
    return list(root)

def E(tag, text=None, **attrs):
    e = ET.Element(tag, {k: str(v) for k, v in attrs.items()})
    if text is not None: e.text = _txt(text)
    return e

def _txt(v):
    if isinstance(v, bool): return 'true' if v else 'false'
    if isinstance(v, float):
        t = ('%.6f' % v).rstrip('0').rstrip('.')
        return '0' if t in ('', '-0') else t
    return str(v)

EMPTY_TOOLBAR = '<Toolbar><ToolbarType>Character</ToolbarType><SelectedSlot xsi:nil="true" /><Slots /><SlotsGamepad /></Toolbar>'
