"""M6 assembly: what the four ships carry (PB names, Custom Data, block names, tags) against what the scripts look up.

    python3 -I tools/tests/test_m6.py          (or through tools/tests/run_all.py)

Pure python (builds Shrike, Corsair, Warden and, for one test, the Leviathan; about 40 s).  The mismatches this pins were real: the designs wrote `[Battery n]` and
`[Cockpit]` Custom Data sections and a `Timer D` that no script looked for.  What is checked:
  * every `[Section] Key` of every PB's Custom Data appears as a string literal in the script that runs in that PB (ShrikeAI, StaffAI, CradleBayAI);
  * the ship tag agrees between the Helm ([Link] Ship Tag / [Ship] Tag), the Staff ([Staff] Ship Tag / [Ship] Tag) and the pod PBs ([Pod] Tag);
  * [Link] Block Prefix == [Staff] Block Prefix and every thruster, gyro, gun, turret, battery, seat, camera and the dead-man timer of a prefixed ship carries it;
  * the Helm finds a dead-man timer ("deadman" in its name) and a seat; the Staff's [Hangar n] Pod= names a PB, its Group= a block group; the bay's Merge= / Timer= names exist;
  * the script sizes: ShrikeAI <= 80,600 minified characters, every other <= 100,000;
  * the packager files the warships as kind 'ship', the test ships as 'test', the drones as 'drone'.
"""
import os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.normpath(os.path.join(TOOLS, '..'))
for p in (os.path.join(TOOLS, 'designs'), TOOLS):
    if p not in sys.path: sys.path.insert(0, p)
import designs                                 # noqa: E402
import _common as C                            # noqa: E402
import shrike, corsair, warden, leviathan      # noqa: E402
from minify_cs import minify                   # noqa: E402

_CACHE = {}
def ship(name):
    if name not in _CACHE:
        _CACHE[name] = {'Shrike': lambda: shrike.shrike(False), 'Corsair': lambda: corsair.corsair(False), 'Corsair Wing': lambda: corsair.corsair_wing(False),
                        'Warden': lambda: warden.warden(False), 'Leviathan': lambda: leviathan.leviathan(False)}[name]()
    return _CACHE[name]

def src(script):
    return open(os.path.join(ROOT, 'scripts', script), encoding='utf-8').read()

def literals(script):
    return set(re.findall(r'"((?:[^"\\\n]|\\.)*)"', src(script)))

def ini(text):
    out, sec = {}, None
    for line in (text or '').splitlines():
        line = line.strip()
        if not line or line.startswith(';'): continue
        m = re.match(r'^\[(.+)\]$', line)
        if m: sec = m.group(1); out[sec] = {}; continue
        if '=' in line and sec is not None:
            k, v = line.split('=', 1); out[sec][k.strip()] = v.strip()
    return out

def pbs(bp):
    """[(name, script file, parsed Custom Data)] of every PB of the ship and its sub-grids"""
    res = []
    for g, _ in bp.grids():
        for b in g.blocks:
            if b['xtype'].endswith('MyProgrammableBlock'):
                prog = b.get('extra') or ''
                scr = [f for f in ('ShrikeAI.cs', 'StaffAI.cs', 'CradleBayAI.cs', 'HornetDroneAI.cs', 'TurretAI.cs') if C.script_text(f)[:200] in prog]
                res.append((b['name'], scr[0] if scr else None, ini(b.get('custom_data')), g))
    return res

def all_blocks(bp):
    return [b for g, _ in bp.grids() for b in g.blocks]

# section names built by concatenation in the scripts ("Hangar " + n, "Bay " + n)
SECTION_PREFIX = ('Hangar ', 'Bay ', 'Mount T')

class CustomDataTests(unittest.TestCase):
    def check_keys(self, model, bp):
        for name, script, data, _g in pbs(bp):
            if script in (None, 'HornetDroneAI.cs'): continue
            lit = literals(script)
            for sec, kv in data.items():
                base = next((p for p in SECTION_PREFIX if sec.startswith(p)), None)
                self.assertTrue(sec in lit or (base and base in lit), f'{model} {name}: section [{sec}] is not read by {script}')
                for k in kv:
                    self.assertTrue(k in lit, f'{model} {name}: [{sec}] {k} is not read by {script}')

    def test_shrike(self): self.check_keys('Shrike', ship('Shrike'))
    def test_corsair(self): self.check_keys('Corsair', ship('Corsair'))
    def test_warden(self): self.check_keys('Warden', ship('Warden'))
    def test_leviathan(self): self.check_keys('Leviathan', ship('Leviathan'))

    def test_every_pb_has_its_script(self):
        for model in ('Shrike', 'Corsair', 'Warden', 'Leviathan'):
            for name, script, data, _g in pbs(ship(model)):
                self.assertIsNotNone(script, f'{model} {name} carries no known script')
                if name.startswith('[Helm]'): self.assertEqual(script, 'ShrikeAI.cs', name)
                if name.startswith('[Staff]'): self.assertEqual(script, 'StaffAI.cs', name)
                if name.startswith(('[Pod]', '[Bay]')): self.assertEqual(script, 'CradleBayAI.cs', name)
                if name.startswith('[TU]'): self.assertEqual(script, 'TurretAI.cs', name)

class WiringTests(unittest.TestCase):
    def ship_tag(self, data, sec, key):
        return (data.get(sec, {}).get(key) or data.get('Ship', {}).get('Tag') or 'SHIP').upper()

    def test_ship_tag_agrees_between_helm_staff_and_pods(self):
        for model in ('Corsair', 'Warden', 'Leviathan'):
            tags = {}
            for name, script, data, _g in pbs(ship(model)):
                if script == 'ShrikeAI.cs': tags['helm'] = self.ship_tag(data, 'Link', 'Ship Tag')
                elif script == 'StaffAI.cs': tags['staff'] = self.ship_tag(data, 'Staff', 'Ship Tag')
                elif script == 'CradleBayAI.cs': tags.setdefault('pods', set()).add((data.get('Pod', {}).get('Tag') or data.get('Ship', {}).get('Tag') or 'SHIP').upper())
            self.assertEqual(tags['helm'], tags['staff'], model)
            self.assertEqual(tags['pods'], {tags['helm']}, model)

    def test_block_prefix_agrees_and_the_ship_wears_it(self):
        for model in ('Corsair', 'Warden', 'Leviathan'):
            bp = ship(model)
            helm = [d for n, s, d, g in pbs(bp) if s == 'ShrikeAI.cs'][0]; staff = [d for n, s, d, g in pbs(bp) if s == 'StaffAI.cs'][0]
            pre = helm['Link']['Block Prefix']
            self.assertEqual(pre, staff['Staff']['Block Prefix'], model)
            host = bp.grids()[0][0]
            kinds = ('Thrust', 'Gyro', 'Gun', 'Turret', 'Battery', 'Cockpit', 'Camera', 'Timer', 'Reactor')
            for b in host.blocks:
                t = b['xtype'].replace('MyObjectBuilder_', '')
                if any(k in t for k in kinds) or 'Missile' in t:
                    self.assertTrue((b['name'] or '').startswith(pre), f'{model}: {t} {b["name"]!r} does not start with {pre}')

    def test_the_helm_finds_a_deadman_timer_and_a_seat(self):
        for model in ('Shrike', 'Corsair', 'Warden', 'Leviathan'):
            host = ship(model).grids()[0][0]
            timers = [b['name'] for b in host.blocks if b['xtype'].endswith('TimerBlock') and 'deadman' in (b['name'] or '').lower()]
            self.assertEqual(len(timers), 1, f'{model}: {timers}')
            seats = [b['name'] for b in host.blocks if b['xtype'].endswith('Cockpit') and 'gunnery' not in (b['name'] or '').lower()]
            self.assertEqual(len(seats), 1, f'{model}: the Helm needs exactly one seat that is not a gunnery seat: {seats}')

    def test_batteries_are_tagged_for_the_fixed_guns_only(self):
        for model in ('Shrike', 'Corsair', 'Warden', 'Leviathan'):
            for b in ship(model).grids()[0][0].blocks:
                if '[B' in (b['name'] or '') and re.search(r'\[B[1-4]\]', b['name']):
                    self.assertFalse('Turret' in b['xtype'], f'{model}: turret {b["name"]} tagged as a battery')

    def test_staff_hangars_and_bays_name_real_blocks(self):
        for model in ('Corsair', 'Warden', 'Leviathan'):
            bp = ship(model)
            names = {b['name'] for b in all_blocks(bp) if b['name']}
            groups = set(bp.groups)
            for g, _ in bp.grids(): groups |= set(g.groups)
            for name, script, data, _g in pbs(bp):
                if script == 'StaffAI.cs':
                    for sec, kv in data.items():
                        if sec.startswith('Hangar '):
                            self.assertIn(kv['Group'], groups, f'{model}: {sec} group')
                            self.assertIn(kv['Pod'], names, f'{model}: {sec} pod PB')
                if script == 'CradleBayAI.cs':
                    for sec, kv in data.items():
                        if sec.startswith('Bay '):
                            self.assertIn(kv['Merge'], names, f'{model}: {name} {sec} merge block')
                            if 'Timer' in kv: self.assertIn(kv['Timer'], names, f'{model}: {name} {sec} timer')

    def test_turret_target_names_hit_turrets_and_spare_the_point_defence(self):
        for model in ('Warden', 'Leviathan'):
            bp = ship(model)
            staff = [d for n, s, d, g in pbs(bp) if s == 'StaffAI.cs'][0]['Staff']
            names = [w.strip().lower() for w in staff['Turret Target Names'].split(',')]
            hit = [b['name'] for b in bp.grids()[0][0].blocks if 'Turret' in b['xtype'] and any(w in b['name'].lower() for w in names)]
            self.assertGreaterEqual(len(hit), 6, model)
            self.assertFalse([n for n in hit if '[PD]' in n], model)

class SizeTests(unittest.TestCase):
    def test_script_sizes(self):
        for f in ('ShrikeAI.cs', 'StaffAI.cs', 'CradleBayAI.cs', 'TurretAI.cs'):
            n = len(minify(src(f)))
            self.assertLessEqual(n, C.SCRIPT_LIMITS.get(f, 100000), f)

class PackagerTests(unittest.TestCase):
    def test_kinds(self):
        mods, _ = designs.load()
        kind = {m: k for k, mod, m, fn in designs.iter_models(mods)}
        for m in ('Shrike', 'Corsair', 'Corsair Wing', 'Warden', 'Warden Wing', 'Leviathan', 'Leviathan Wing', 'Cradle Pod'): self.assertEqual(kind[m], 'ship', m)
        for m in ('Hornet', 'Basilisk', 'Gnat'): self.assertEqual(kind[m], 'drone', m)
        for m in ('Shrike Test', 'Shrike Staff Test', 'Cradle Test', 'Cradle Pod Test'): self.assertEqual(kind[m], 'test', m)

    def test_warships_zips_are_registered(self):
        import build_all
        names = [p[0] for p in build_all.PACKAGES]
        self.assertIn('Warships_v%s_Full' % build_all.VERSION, names)
        self.assertIn('Warships_v%s_Lite' % build_all.VERSION, names)
        allz = [p for p in build_all.PACKAGES if p[0].endswith('_All')][0]
        self.assertIn('ship', allz[1])
        self.assertTrue(os.path.exists(os.path.join(ROOT, 'packages', 'README_Warships.md.tmpl')))

if __name__ == '__main__':
    unittest.main()
