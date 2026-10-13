"""Inventory pre-loading and stored-state fields for sebp (ammunition, uranium, hydrogen, charge).

XML shapes are the ones the game writes in the owner's blueprints (verified in tools/tests/test_sebp_ext.py against
ref/user-samples):

  <ComponentContainer><Components><ComponentData><TypeId>MyInventoryBase</TypeId>
    <Component xsi:type="MyObjectBuilder_Inventory"><CanPutItems>true</CanPutItems>
      <Items><MyObjectBuilder_InventoryItem><Amount>24</Amount>
          <PhysicalContent xsi:type="MyObjectBuilder_AmmoMagazine"><SubtypeName>NATO_25x184mm</SubtypeName><ProjectilesCount>0</ProjectilesCount></PhysicalContent>
          <ItemId>0</ItemId></MyObjectBuilder_InventoryItem></Items>
      <nextItemId>1</nextItemId><Volume>0.384</Volume><Mass>9223372036854.775807</Mass><MaxItemCount>2147483647</MaxItemCount>
      <Size xsi:nil="true" /><InventoryFlags>CanReceive</InventoryFlags><RemoveEntityOnEmpty>false</RemoveEntityOnEmpty>
  ...
  railgun capacitor:  <ComponentData><TypeId>MyEntityCapacitorComponent</TypeId><Component xsi:type="MyObjectBuilder_EntityCapacitorComponent"><StoredPower>0.016</StoredPower>
  reactor:            tail <Capacity>104.682739</Capacity> (= uranium kg in the inventory)
  hydrogen engine:    tail <Capacity>5000.56543</Capacity> (litres of hydrogen, no inventory)
  hydrogen tank:      tail <FilledRatio>1</FilledRatio>
  battery:            tail <CurrentStoredPower>3</CurrentStoredPower> ... <MaxStoredPower>3</MaxStoredPower>

Item volumes and masses: magazines and components from defs_stats.json (litres, kg); ingots and ore from the table below
(recollection of the game's PhysicalItems, [rec], checked against the owner's files: no game-written inventory in the samples exceeds
its own <Volume> with these numbers and the ore/scrap-filled ones come out at the brim; see the tests).
"""
import json, os, math
import xml.etree.ElementTree as ET
from sebp_xml import E, ser, frag, XSI, _txt

HERE = os.path.dirname(os.path.abspath(__file__))
UNLIMITED_MASS = 9223372036854.775807            # the game's "no mass limit" marker (long.MaxValue / 1e6)
UNLIMITED_COUNT = 2147483647

# litres per kg of ingot / ore [rec]
INGOT_L_PER_KG = {'Iron': 0.127, 'Nickel': 0.112, 'Cobalt': 0.112, 'Magnesium': 0.575, 'Silicon': 0.429, 'Silver': 0.095,
                  'Gold': 0.052, 'Platinum': 0.047, 'Uranium': 0.052, 'Stone': 0.37, 'Scrap': 0.254}
ORE_L_PER_KG = {'Scrap': 0.254}      # every other ore 0.37 L/kg; Scrap = 0.254 is fitted: Venture's Connector (8000 L) and Starburst's cargo (15625 L) fill to exactly the brim with it
ORE_DEFAULT_L_PER_KG = 0.37
KINDS = ('AmmoMagazine', 'Ingot', 'Component', 'Ore')
INTEGER_KINDS = ('AmmoMagazine', 'Component')

class InventoryError(ValueError):
    """a pre-load that the game would not accept (or that does not fit)"""

_STATS = None
def stats():
    global _STATS
    if _STATS is None:
        with open(os.path.join(HERE, '..', 'defs_stats.json')) as fh: _STATS = json.load(fh)
    return _STATS

class Item:
    """one stack: kind in KINDS, subtype, amount (magazines/components are counts, ingots/ore are kg)"""
    __slots__ = ('kind', 'sub', 'amount', 'raw')
    def __init__(self, kind, sub, amount, raw=None):
        kind = kind.replace('MyObjectBuilder_', '')
        self.raw = raw         # PhysicalContent element of an item of a kind we cannot size (only set when READING a file)
        if kind not in KINDS and raw is None: raise InventoryError(f'unsupported item kind {kind!r}; supported: {KINDS}')
        if not (amount > 0) or math.isnan(amount) or math.isinf(amount): raise InventoryError(f'{kind}/{sub}: amount must be > 0, got {amount}')
        if kind in INTEGER_KINDS and raw is None and abs(amount - round(amount)) > 1e-9:
            raise InventoryError(f'{kind}/{sub}: amount must be a whole number, got {amount}')
        self.kind, self.sub, self.amount = kind, sub, (int(round(amount)) if kind in INTEGER_KINDS and raw is None else float(amount))
    def __repr__(self): return f'Item({self.kind}, {self.sub}, {self.amount})'
    def __eq__(self, o): return isinstance(o, Item) and (self.kind, self.sub, self.amount) == (o.kind, o.sub, o.amount)
    def __hash__(self): return hash((self.kind, self.sub, self.amount))

    # constructors
    @classmethod
    def ammo(cls, sub, n): return cls('AmmoMagazine', sub, n)
    @classmethod
    def ingot(cls, sub, kg): return cls('Ingot', sub, kg)
    @classmethod
    def component(cls, sub, n): return cls('Component', sub, n)
    @classmethod
    def ore(cls, sub, kg): return cls('Ore', sub, kg)

    # physical data per unit
    def unit_litres(self):
        if self.raw is not None: raise InventoryError(f'{self.kind}/{self.sub}: size unknown (item kind not supported for pre-loading)')
        if self.kind == 'AmmoMagazine':
            m = stats()['magazines'].get(self.sub)
            if not m: raise InventoryError(f'unknown magazine {self.sub!r} (defs_stats.json magazines)')
            return m['volume']
        if self.kind == 'Component':
            c = stats()['components'].get(self.sub)
            if not c: raise InventoryError(f'unknown component {self.sub!r} (defs_stats.json components)')
            return c['volume']
        if self.kind == 'Ingot':
            if self.sub not in INGOT_L_PER_KG: raise InventoryError(f'unknown ingot {self.sub!r}')
            return INGOT_L_PER_KG[self.sub]
        return ORE_L_PER_KG.get(self.sub, ORE_DEFAULT_L_PER_KG)
    def unit_kg(self):
        if self.raw is not None: raise InventoryError(f'{self.kind}/{self.sub}: mass unknown (item kind not supported for pre-loading)')
        if self.kind == 'AmmoMagazine': return stats()['magazines'][self.sub]['mass']
        if self.kind == 'Component': return stats()['components'][self.sub]['mass']
        return 1.0
    def litres(self): return self.amount * self.unit_litres()
    def kg(self): return self.amount * self.unit_kg()

    # XML
    def element(self, item_id):
        it = ET.Element('MyObjectBuilder_InventoryItem')
        it.append(E('Amount', self.amount))
        if self.raw is not None: it.append(self.raw)
        else:
            pc = ET.SubElement(it, 'PhysicalContent', {XSI + 'type': 'MyObjectBuilder_' + self.kind})
            pc.append(E('SubtypeName', self.sub))
            if self.kind == 'AmmoMagazine': pc.append(E('ProjectilesCount', 0))
        it.append(E('ItemId', item_id))
        return it
    @classmethod
    def from_element(cls, el):
        pc = el.find('PhysicalContent')
        kind = pc.get(XSI + 'type').replace('MyObjectBuilder_', '')
        return cls(kind, pc.findtext('SubtypeName'), float(el.findtext('Amount')), raw=None if kind in KINDS else pc)

def _guess_kind(sub, amount):
    if sub in stats()['magazines']: return 'AmmoMagazine'
    if sub in stats()['components']: return 'Component'
    if sub in INGOT_L_PER_KG:
        raise InventoryError(f'{sub!r} is an ingot or an ore: give the kind, e.g. ("Ingot", {sub!r}, {amount})')
    raise InventoryError(f'cannot tell what {sub!r} is: give the kind, e.g. ("Component", {sub!r}, {amount})')

def as_items(spec):
    """normalise `items=` of Blueprint.add: Item | (kind, sub, amount) | (sub, amount) | {'kind','sub','amount'} | list of those.
    Stacks of the same kind/subtype are merged (the game keeps one stack per type)."""
    if spec is None: return []
    if isinstance(spec, Item): spec = [spec]
    out = []
    for s in spec:
        if isinstance(s, Item): it = s
        elif isinstance(s, dict): it = Item(s['kind'], s['sub'], s['amount'])
        elif len(s) == 3: it = Item(*s)
        elif len(s) == 2: it = Item(_guess_kind(s[0], s[1]), s[0], s[1])
        else: raise InventoryError(f'cannot read item spec {s!r}')
        out.append(it)
    merged = {}
    for it in out:
        k = (it.kind, it.sub)
        merged[k] = Item(it.kind, it.sub, merged[k].amount + it.amount) if k in merged else it
    return list(merged.values())

# ---------------------------------------------------------------------------------------------------------------
# container access
# ---------------------------------------------------------------------------------------------------------------
def _components(cc):
    return cc.find('Components') if cc is not None else None

def find_component(cc, type_id):
    comps = _components(cc)
    if comps is None: return None
    for cd in comps:
        if cd.findtext('TypeId') == type_id: return cd.find('Component')
    return None

def inventory_limits(cc):
    """(volume m3, mass kg or None for unlimited, max stacks or None) of the MyInventoryBase component of a container, or None"""
    inv = find_component(cc, 'MyInventoryBase')
    if inv is None: return None
    vol = float(inv.findtext('Volume'))
    mass = float(inv.findtext('Mass') or UNLIMITED_MASS)
    cnt = int(inv.findtext('MaxItemCount') or UNLIMITED_COUNT)
    return vol, (None if mass >= UNLIMITED_MASS else mass), (None if cnt >= UNLIMITED_COUNT else cnt)

def read_items(cc):
    """the Items of the MyInventoryBase component as [Item] ([] when there is no inventory)"""
    inv = find_component(cc, 'MyInventoryBase')
    if inv is None or inv.find('Items') is None: return []
    return [Item.from_element(e) for e in inv.find('Items')]

def allowed_items(key):
    """(kind, set of subtypes) an inventory of this definition accepts, or None for 'anything'.
    Weapons take only the magazines of their weapon definition; reactors only their fuel ingot."""
    d = stats()['blocks'].get(key)
    if not d: return None
    w = d.get('stats', {}).get('weapon')
    if w: return ('AmmoMagazine', set(w['magazines']))
    f = d['raw'].get('FuelInfos.FuelInfo.Id.SubtypeId')
    if f and d['raw'].get('FuelInfos.FuelInfo.Id.TypeId') == 'Ingot': return ('Ingot', {f})
    return None

def check_fit(label, limits, items, key=None):
    """raise InventoryError unless `items` fit into limits=(m3, kg|None, stacks|None) and are accepted by the block"""
    if limits is None:
        raise InventoryError(f'{label}: this block has no inventory in its shape (see sebp.confidence_table(); unseen subtypes carry no container)')
    vol_m3, mass_kg, stacks = limits
    ok = allowed_items(key) if key else None
    items = [i for i in items if i.raw is None]          # items of kinds we cannot size (read from a file) are kept as they are
    if ok:
        for it in items:
            if it.kind != ok[0] or it.sub not in ok[1]:
                raise InventoryError(f'{label}: the game only accepts {ok[0]} {sorted(ok[1])} here, not {it.kind}/{it.sub}')
    litres = sum(i.litres() for i in items)
    if litres > vol_m3 * 1000 * (1 + 1e-6) + 1e-9:       # the game's own full inventories exceed their Volume by up to 2e-7 (float rounding)
        raise InventoryError(f'{label}: {litres:.4g} L of items do not fit into {vol_m3 * 1000:.4g} L '
                             f'({", ".join(f"{i.amount:g} x {i.sub}" for i in items)})')
    if mass_kg is not None and sum(i.kg() for i in items) > mass_kg:
        raise InventoryError(f'{label}: {sum(i.kg() for i in items):.4g} kg exceed the {mass_kg:.4g} kg limit')
    if stacks is not None and len(items) > stacks:
        raise InventoryError(f'{label}: {len(items)} stacks exceed the limit of {stacks}')

def capacity_of(limits, item_kind, item_sub, used=()):
    """how many units of an item fit into the free space (whole units for magazines/components)"""
    free = limits[0] * 1000 - sum(i.litres() for i in used)
    n = free / Item(item_kind, item_sub, 1).unit_litres()
    return int(math.floor(n + 1e-9)) if item_kind in INTEGER_KINDS else n

def write_items(cc, items):
    """replace the stacks of the MyInventoryBase component in place (ItemId = position, nextItemId = count)"""
    inv = find_component(cc, 'MyInventoryBase')
    if inv is None: raise InventoryError('no MyInventoryBase component to fill')
    its = inv.find('Items')
    for c in list(its): its.remove(c)
    for i, it in enumerate(items): its.append(it.element(i))
    its.text = None
    inv.find('nextItemId').text = str(len(items))

# ---------------------------------------------------------------------------------------------------------------
# scalar state: capacitor charge, reactor / engine fuel, tank fill, battery charge
# ---------------------------------------------------------------------------------------------------------------
STATE_KEYS = ('capacitor', 'fuel', 'filled', 'stored_power')

def apply_state(label, xtype, key, state, items, cc, capacitor_full=None):
    """-> tail override elements for `state` (and patches the capacitor component of cc in place).
      capacitor     railgun: MWh stored (<= the full charge of the shape) or 'full'
      fuel          HydrogenEngine: litres or 'full' (definition FuelCapacity); Reactor: kg of uranium, default = the uranium in `items`
      filled        OxygenTank: ratio 0..1 or 'full'
      stored_power  BatteryBlock: MWh or 'full' (definition MaxStoredPower)"""
    state = dict(state or {})
    t = xtype.replace('MyObjectBuilder_', '')
    raw = (stats()['blocks'].get(key) or {}).get('raw', {})
    out = []
    bad = [k for k in state if k not in STATE_KEYS]
    if bad: raise InventoryError(f'{label}: unknown state key(s) {bad}; known: {STATE_KEYS}')
    def need(k, types):
        if k in state and t not in types: raise InventoryError(f'{label}: state[{k!r}] applies to {types}, not {t}')
    need('capacitor', ('SmallMissileLauncherReload',)); need('fuel', ('HydrogenEngine', 'Reactor'))
    need('filled', ('OxygenTank',)); need('stored_power', ('BatteryBlock',))
    if 'capacitor' in state:
        comp = find_component(cc, 'MyEntityCapacitorComponent')
        if comp is None: raise InventoryError(f'{label}: no capacitor component in this block shape (only railguns have one)')
        full = capacitor_full if capacitor_full is not None else float(comp.findtext('StoredPower') or 0)
        if not full: raise InventoryError(f'{label}: the full capacitor charge of this railgun is not known from any sample')
        v = full if state['capacitor'] == 'full' else float(state['capacitor'])
        if v < 0 or v > full * (1 + 1e-9): raise InventoryError(f'{label}: capacitor {v} MWh outside 0..{full} (full charge seen in the samples)')
        if v > 0:
            sp = comp.find('StoredPower')
            if sp is None: sp = ET.SubElement(comp, 'StoredPower')
            sp.text = _txt(v)
    if t == 'Reactor':
        ur = sum(i.amount for i in items if i.kind == 'Ingot' and i.sub == 'Uranium')
        v = ur if 'fuel' not in state else (ur if state['fuel'] == 'full' else float(state['fuel']))
        out.append(E('Capacity', v))                 # the game writes the uranium kg here (Red Ship: 104.68 kg in the inventory, Capacity 104.68)
    elif t == 'HydrogenEngine':
        cap = raw.get('FuelCapacity')
        if 'fuel' in state:
            v = cap if state['fuel'] == 'full' else float(state['fuel'])
            if v is None: raise InventoryError(f'{label}: definition has no FuelCapacity')
            if v < 0 or (cap is not None and v > cap * 1.01):          # the game's own engines hold up to 1.0026 x FuelCapacity
                raise InventoryError(f'{label}: fuel {v} L outside 0..{cap}')
            out.append(E('Capacity', v))
    if 'filled' in state:
        v = 1.0 if state['filled'] == 'full' else float(state['filled'])
        if not 0 <= v <= 1: raise InventoryError(f'{label}: filled ratio {v} outside 0..1')
        out.append(E('FilledRatio', v))
    if 'stored_power' in state:
        mx = raw.get('MaxStoredPower')
        v = mx if state['stored_power'] == 'full' else float(state['stored_power'])
        if v is None or mx is None: raise InventoryError(f'{label}: definition has no MaxStoredPower')
        if v < 0 or v > mx * (1 + 1e-9): raise InventoryError(f'{label}: stored power {v} MWh outside 0..{mx}')
        out.append(E('CurrentStoredPower', v)); out.append(E('MaxStoredPower', mx))
    return out

def read_state(el):
    """state fields of a block element (any of the known ones that are present) as a dict"""
    out = {}
    cc = el.find('ComponentContainer')
    comp = find_component(cc, 'MyEntityCapacitorComponent')
    if comp is not None: out['capacitor'] = float(comp.findtext('StoredPower') or 0)
    t = el.get(XSI + 'type', '').replace('MyObjectBuilder_', '')
    if t in ('Reactor', 'HydrogenEngine') and el.find('Capacity') is not None: out['fuel'] = float(el.findtext('Capacity'))
    if t == 'OxygenTank' and el.find('FilledRatio') is not None: out['filled'] = float(el.findtext('FilledRatio'))
    if t == 'BatteryBlock' and el.find('CurrentStoredPower') is not None: out['stored_power'] = float(el.findtext('CurrentStoredPower'))
    return out

def default_ammo(key):
    """the magazine a weapon definition lists first (what the game loads by default), or None"""
    ok = allowed_items(key)
    if ok and ok[0] == 'AmmoMagazine':
        d = stats()['blocks'][key]['stats']['weapon']
        return d['magazines'][0]
    return None
