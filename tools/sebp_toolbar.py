"""Toolbars (Timer, Button Panel, Event Controller, cockpits, ...) and block groups for sebp.

Shapes are the ones the game writes in the owner's blueprints (tools/tests/test_sebp_ext.py checks each against ref/user-samples):

  <Toolbar><ToolbarType>Character</ToolbarType><SelectedSlot xsi:nil="true" />
    <Slots>
      <Slot><Index>0</Index><Item />
        <Data xsi:type="MyObjectBuilder_ToolbarItemTerminalBlock"><Action>OnOff_On</Action>[<Parameters>..</Parameters>][<CustomIconTitle>..</CustomIconTitle>]<BlockEntityId>123</BlockEntityId></Data></Slot>
      <Slot><Index>1</Index><Item />
        <Data xsi:type="MyObjectBuilder_ToolbarItemTerminalGroup"><Action>OnOff</Action>[..]<GridEntityId>0</GridEntityId><BlockEntityId>(the block that owns the toolbar)</BlockEntityId><GroupName>Thrusters</GroupName></Data></Slot>
      <Slot><Index>2</Index><Item />
        <Data xsi:type="MyObjectBuilder_ToolbarItemWeapon"><DefinitionId Type="MyObjectBuilder_SmallGatlingGun" Subtype="SmallBlockAutocannon" /></Data></Slot>
    </Slots><SlotsGamepad /></Toolbar>

Facts read from the samples (and different from what one would guess):
  * a group item stores the EntityId of the block that OWNS the toolbar in <BlockEntityId> (155 of 155 group items), not a member;
  * <BlockGroups> of a CubeGrid lists <Vector3I> cells, not EntityIds.  The cell is the block's definition "Center" cell
    (e.g. the 3x3x3 hydrogen thruster (1,1,2), a hangar door (0,2,0)); the per-definition centres are derived from the samples
    (shapes.json "_centers"); where no sample shows one, the block-local cell (0,0,0) is written (any cell of the block resolves);
  * parameters: <Parameters><MyObjectBuilder_ToolbarItemActionParameter><TypeCode>Single</TypeCode><Value>1</Value>...  (seen: Single, Int32, Int64).

NOT seen in any game file: a Programmable Block 'Run' slot (with or without an argument).  Bare `run(pb)` uses the action id "Run"
(taken from the game source, shape identical to the seen terminal-block items); `run(pb, argument=..., experimental=True)` adds a
String parameter by analogy with the seen Single/Int32/Int64 parameters.  UNVERIFIED, it is listed as such in sebp.confidence_table().
"""
import xml.etree.ElementTree as ET
from sebp_xml import E, XSI, _txt, EMPTY_TOOLBAR, frag

# every Action id that occurs in the owner's samples (211 terminal-block + 147 group slots)
SEEN_ACTIONS = frozenset('''AI ActivateBehavior ActivateBehavior_Off Autolock Control DecreaseJumpDistance DecreaseRadius DecreaseStrength
DecreaseY DecreaseZ Depressurize Extend FollowMe IncreaseJumpDistance IncreaseRadius IncreaseStrength IncreaseY IncreaseZ Jump Lock OnOff
OnOff_Off OnOff_On Open Open_Off Open_On PlaySound Recharge Recharge_Off Recharge_On Retract Reverse RotateToAngle SetColor SetOverride
SetVelocity Shoot Start Stockpile Stockpile_Off Stockpile_On SwitchLock TerrainClearingMode TriggerNow Unlock View AI_Off AI_On'''.split())
RUN_ACTION = 'Run'          # MyProgrammableBlock terminal action id (game source; not present in any sample)

# SelectedEvent ids of the Event Controller that occur in the samples, with what they were used for (the NAMES are inferred
# from the selected block types and thresholds, not read from the game)
EVENTS_SEEN = {
    1: 'cargo container fill level (selected CargoContainer, Threshold 0.5) [inferred]',
    2: 'block integrity (selected guns/cameras/hinges, Threshold 0.99, ConditionInvert) [inferred]',
    6: 'seen once with a selected block that is not in the file (Pon AI Off) [unknown]',
    7: 'connector state (selected ShipConnector, Threshold 0.8, ConditionInvert) [inferred]',
    14: 'welder (selected ShipWelder, actions OnOff_On/Off) [inferred]',
}
EVENT_ALIASES = {'cargo_fill': 1, 'integrity': 2, 'connector': 7, 'welder': 14}

class ToolbarError(ValueError):
    pass

# ---------------------------------------------------------------------------------------------------------------
# parameters
# ---------------------------------------------------------------------------------------------------------------
class Param:
    __slots__ = ('code', 'value')
    def __init__(self, code, value): self.code, self.value = code, value
    def element(self):
        p = ET.Element('MyObjectBuilder_ToolbarItemActionParameter')
        p.append(E('TypeCode', self.code)); p.append(E('Value', self.value))
        return p
def single(v): return Param('Single', float(v))
def int32(v): return Param('Int32', int(v))
def int64(v): return Param('Int64', int(v))
def string(v): return Param('String', str(v))

# ---------------------------------------------------------------------------------------------------------------
# slot items (resolved to XML when the blueprint is written, because they refer to EntityIds)
# ---------------------------------------------------------------------------------------------------------------
class SlotItem:
    kind = None
    experimental = False
    def element(self, owner_id, resolve):         # -> <Data> element
        raise NotImplementedError

def _params(action, params):
    ps = [p if isinstance(p, Param) else _auto_param(p) for p in (params or [])]
    return ps

def _auto_param(v):
    if isinstance(v, bool): raise ToolbarError('bool parameters have not been seen in any sample')
    if isinstance(v, int): return int32(v)
    if isinstance(v, float): return single(v)
    return string(v)

class BlockItem(SlotItem):
    kind = 'ToolbarItemTerminalBlock'
    def __init__(self, target, action, params=None, title=None, experimental=False):
        self.target, self.action, self.params, self.title, self.experimental = target, action, _params(action, params), title, experimental
    def element(self, owner_id, resolve):
        d = ET.Element('Data', {XSI + 'type': 'MyObjectBuilder_' + self.kind})
        d.append(E('Action', self.action))
        if self.params:
            ps = ET.SubElement(d, 'Parameters')
            for p in self.params: ps.append(p.element())
        if self.title: d.append(E('CustomIconTitle', self.title))
        d.append(E('BlockEntityId', resolve(self.target)))
        return d

class GroupItem(SlotItem):
    kind = 'ToolbarItemTerminalGroup'
    def __init__(self, group, action, params=None, title=None, experimental=False):
        self.group, self.action, self.params, self.title, self.experimental = group, action, _params(action, params), title, experimental
    def element(self, owner_id, resolve):
        d = ET.Element('Data', {XSI + 'type': 'MyObjectBuilder_' + self.kind})
        d.append(E('Action', self.action))
        if self.params:
            ps = ET.SubElement(d, 'Parameters')
            for p in self.params: ps.append(p.element())
        if self.title: d.append(E('CustomIconTitle', self.title))
        d.append(E('GridEntityId', 0)); d.append(E('BlockEntityId', owner_id)); d.append(E('GroupName', self.group))
        return d

class WeaponItem(SlotItem):
    kind = 'ToolbarItemWeapon'
    def __init__(self, xtype, sub):
        self.xtype = xtype if xtype.startswith('MyObjectBuilder_') else 'MyObjectBuilder_' + xtype
        self.sub = sub
    def element(self, owner_id, resolve):
        d = ET.Element('Data', {XSI + 'type': 'MyObjectBuilder_' + self.kind})
        d.append(E('DefinitionId', Type=self.xtype, Subtype=self.sub))
        return d

def block(target, action, params=None, title=None):
    """terminal-block slot: `action` on one block (a Blueprint block dict, or an EntityId int)"""
    return BlockItem(target, action, params, title)
def group(name, action, params=None, title=None):
    """terminal-group slot: `action` on the named block group (must exist: Blueprint.group(name, blocks))"""
    return GroupItem(name, action, params, title)
def weapon(xtype, sub=''):
    """weapon slot (cockpit / remote control): the block type and subtype of the gun, e.g. weapon('SmallGatlingGun', 'SmallBlockAutocannon')"""
    return WeaponItem(xtype, sub)
def run(pb, argument=None, experimental=False):
    """Programmable Block 'Run' slot.  With an argument the shape is a HYPOTHESIS (never seen in a game file): needs experimental=True"""
    if argument is None: return BlockItem(pb, RUN_ACTION)
    if not experimental:
        raise ToolbarError('a PB Run slot with an argument has never been seen in a game file; pass experimental=True to write the hypothesised shape '
                           '(<Action>Run</Action> + one String parameter)')
    return BlockItem(pb, RUN_ACTION, [string(argument)], experimental=True)

# ---------------------------------------------------------------------------------------------------------------
# toolbar element
# ---------------------------------------------------------------------------------------------------------------
TOOLBAR_TAGS = ('Toolbar', 'BuildToolbar', 'OnLockedToolbar')

def normalise(spec):
    """`toolbar=` argument -> {slot index: SlotItem}.  A list is positional (None skips a slot); a dict maps index -> item."""
    if spec is None: return {}
    if isinstance(spec, dict): items = dict(spec)
    else: items = {i: s for i, s in enumerate(spec) if s is not None}
    for i, s in items.items():
        if not isinstance(i, int) or i < 0: raise ToolbarError(f'bad slot index {i!r}')
        if not isinstance(s, SlotItem): raise ToolbarError(f'slot {i}: expected a sebp_toolbar item (block/group/weapon/run), got {s!r}')
    return items

def toolbar_element(tag, slots, owner_id, resolve, group_names=None, selected=None):
    """<Toolbar> (or BuildToolbar/OnLockedToolbar) with the given {index: SlotItem}"""
    tb = ET.Element(tag)
    tb.append(E('ToolbarType', 'Character'))
    sel = ET.SubElement(tb, 'SelectedSlot')
    if selected is None: sel.set(XSI + 'nil', 'true')
    else: sel.text = str(selected)
    sl = ET.SubElement(tb, 'Slots')
    for i in sorted(slots):
        item = slots[i]
        if isinstance(item, GroupItem) and group_names is not None and item.group not in group_names:
            raise ToolbarError(f'slot {i}: block group {item.group!r} is not defined on this grid (Blueprint.group)')
        s = ET.SubElement(sl, 'Slot')
        s.append(E('Index', i)); s.append(ET.Element('Item')); s.append(item.element(owner_id, resolve))
    ET.SubElement(tb, 'SlotsGamepad')
    return tb

def empty_toolbar(tag='Toolbar'):
    n = frag(EMPTY_TOOLBAR)[0]; n.tag = tag; return n

# ---------------------------------------------------------------------------------------------------------------
# block groups
# ---------------------------------------------------------------------------------------------------------------
def block_groups_element(groups, cell_of):
    """<BlockGroups> for {name: [block, ...]}; `cell_of(block)` gives the grid cell the game writes for that block"""
    bg = ET.Element('BlockGroups')
    for name, blocks in groups.items():
        g = ET.SubElement(bg, 'MyObjectBuilder_BlockGroup')
        g.append(E('Name', name))
        bl = ET.SubElement(g, 'Blocks')
        for b in blocks:
            x, y, z = cell_of(b)
            v = ET.SubElement(bl, 'Vector3I')
            v.append(E('X', x)); v.append(E('Y', y)); v.append(E('Z', z))
    return bg

# ---------------------------------------------------------------------------------------------------------------
# event controller helper
# ---------------------------------------------------------------------------------------------------------------
def event_settings(event, threshold=None, invert=False, and_gate=False):
    """`settings=` dict for an EventControllerBlock: event is a SelectedEvent id seen in the samples (EVENTS_SEEN) or an alias
    (EVENT_ALIASES).  Select the watched blocks with links={'selected': [blocks]}; the actions go into toolbar=[...]."""
    ev = EVENT_ALIASES.get(event, event)
    if ev not in EVENTS_SEEN: raise ToolbarError(f'event {event!r} was not seen in any sample; seen ids: {sorted(EVENTS_SEEN)} (aliases {sorted(EVENT_ALIASES)})')
    s = {'SelectedEvent': ev, 'ConditionInvert': bool(invert), 'ANDGate': bool(and_gate)}
    if threshold is not None: s['Threshold'] = float(threshold)
    return s

# ---------------------------------------------------------------------------------------------------------------
# reading
# ---------------------------------------------------------------------------------------------------------------
def _read_param(p):
    code, val = p.findtext('TypeCode'), p.findtext('Value')
    if code == 'Single': return Param(code, float(val))
    if code in ('Int32', 'Int64'): return Param(code, int(val))
    return Param(code, val or '')

def read_toolbar(tb):
    """{slot index: SlotItem} of a <Toolbar>-like element (slots of unknown item types are skipped)"""
    out = {}
    sl = tb.find('Slots')
    if sl is None: return out
    for slot in sl:
        d = slot.find('Data')
        if d is None: continue
        ty = (d.get(XSI + 'type') or '').replace('MyObjectBuilder_', '')
        idx = int(slot.findtext('Index'))
        params = [_read_param(p) for p in (d.find('Parameters') if d.find('Parameters') is not None else [])]
        title = d.findtext('CustomIconTitle')
        if ty == 'ToolbarItemTerminalBlock':
            out[idx] = BlockItem(int(d.findtext('BlockEntityId')), d.findtext('Action'), params, title)
        elif ty == 'ToolbarItemTerminalGroup':
            out[idx] = GroupItem(d.findtext('GroupName'), d.findtext('Action'), params, title)
        elif ty == 'ToolbarItemWeapon':
            di = d.find('DefinitionId')
            out[idx] = WeaponItem(di.get('Type'), di.get('Subtype') or '')
    return out
