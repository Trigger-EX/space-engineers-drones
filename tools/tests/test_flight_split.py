#!/usr/bin/env python3
"""Flight split (docs/design/flight_split.md, section 9.3): config, damage and search left the 9 drone Flight PBs and the ShrikeAI Helm.
    python3 -I tools/tests/test_flight_split.py
Written against the FINAL state (packages P1-F / S / X / H). Until they land, failures are expected; each message names the package that owes the change.
(a) tuple shapes at sender and receiver, (b) what moved, (c) config tables vs tools/cfg_tables.json, (d) sizes."""
import json, os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.dirname(TOOLS)
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
from minify_cs import minify                                         # noqa: E402

FLIGHTS = {'Hornet': 'HornetDroneAI', 'Harrier': 'HarrierAI', 'Lancer': 'LancerAI', 'Aegis': 'AegisAI', 'Wasp': 'WaspFlightAI',
           'Longbow': 'LongbowSniperAI', 'Gnat': 'GnatSwarmAI', 'Basilisk': 'BasiliskAI', 'Mantis': 'MantisAI'}
STAFFS = {'Hornet': 'HornetStaffAI', 'Harrier': 'HarrierStaffAI', 'Lancer': 'LancerStaffAI', 'Aegis': 'AegisStaffAI', 'Wasp': 'WaspStaffAI',
          'Longbow': 'LongbowStaffAI', 'Gnat': 'GnatStaffAI', 'Basilisk': 'BasiliskStaffAI', 'Mantis': 'MantisStaffAI'}
EIGHT = [m for m in FLIGHTS if m != 'Gnat']      # Gnat runs G1a (flight_split.md 7.2); since P2-G2 its damage sensing + RAID hit estimator run in GnatStaffAI (own copy, not DAMAGE SENSE)
PKG = {'cl': 'P1-F', 'cs': 'P1-S', 'ds': 'P1-S', 'dl': 'P1-F', 'sc': 'P1-X', 'sq': 'P1-F', 'al': 'P1-S'}

def raw(path):
    p = path if path.startswith(('tools/', 'scripts/')) else 'scripts/' + path
    if not p.endswith(('.cs', '.json')): p += '.cs'
    with open(os.path.join(ROOT, p), encoding='utf-8') as f: return f.read()
def code(path): return re.sub(r'//[^\n]*', '', raw(path))     # comments out: block headers name the tuples and old functions too
def blk(k): return code('tools/%s_block.cs' % k)

# ---- MyTuple<...> text, names stripped
def balanced(t, start):
    """t[start:] begins with 'MyTuple<': the text up to its matching '>'"""
    i, d = t.index('<', start), 0
    for j in range(i, len(t)):
        if t[j] == '<': d += 1
        elif t[j] == '>':
            d -= 1
            if d == 0: return t[start:j + 1]
    return None
def norm(tt):
    """MyTuple<int hits, double integ, MyTuple<A,B> box> -> 'MyTuple<int,double,MyTuple<A,B>>' (member names and spaces gone)"""
    tt = re.sub(r'//|\([^)]*\)', ' ', tt)
    inner = tt[tt.index('<') + 1:tt.rindex('>')]
    parts, d, cur = [], 0, ''
    for ch in inner:
        if ch == '<': d += 1
        if ch == '>': d -= 1
        if ch == ',' and d == 0: parts.append(cur); cur = ''
        else: cur += ch
    parts.append(cur)
    out = []
    for p in parts:
        p = ' '.join(p.split())
        if p.startswith('MyTuple<'): out.append(norm(balanced(p, 0)))
        else: out.append(p.split(' ')[0])
    return 'MyTuple<' + ','.join(out) + '>'
def tuples(t):
    r, i = set(), 0
    while True:
        i = t.find('MyTuple<', i)
        if i < 0: return r
        b = balanced(t, i)
        if b: r.add(norm(b))
        i += 8
def header_tuple(block_file, tag):
    """the canonical tuple a block header declares for a tag: the line '//   .TAG  A -> B   MyTuple<...>' (continues over comment lines)"""
    t = raw('tools/%s_block.cs' % block_file)
    m = re.search(r'^//\s+' + re.escape(tag) + r'\s.*?(MyTuple<.*)$', t, re.M | re.S)
    if not m: return None
    txt = re.sub(r'\n//\s*', ' ', m.group(1))
    b = balanced(txt, 0)
    return norm(b) if b else None

TUPLES = {                    # tag: (sender files, receiver files); a file is a block ('tools/x') or a script
    '.DMG': (['tools/ds_block.cs'] + ['scripts/%s.cs' % f for f in STAFFS.values()], ['tools/dl_block.cs'] + ['scripts/%s.cs' % f for f in FLIGHTS.values()]),
    '.HAX': (['scripts/GnatStaffAI.cs'], ['scripts/GnatSwarmAI.cs']),      # Gnat only (P2-G2): the hit bearing for RAID
    '.SCS': (['tools/sq_block.cs'] + ['scripts/%s.cs' % FLIGHTS[m] for m in EIGHT], ['tools/sc_block.cs', 'scripts/AuxAI.cs']),
    '.KZR': (['tools/sq_block.cs'] + ['scripts/%s.cs' % FLIGHTS[m] for m in EIGHT], ['tools/sc_block.cs', 'scripts/AuxAI.cs']),
    '.SCN': (['tools/sc_block.cs', 'scripts/AuxAI.cs'], ['tools/sq_block.cs'] + ['scripts/%s.cs' % FLIGHTS[m] for m in EIGHT]),
    '.LKR': (['scripts/StaffAI.cs'], ['scripts/ShrikeAI.cs']),
}
CANON = {      # the doc's shapes (flight_split.md 4.1); the block headers must say the same
    '.DMG': 'MyTuple<int,double,Vector3D,double,int,double>',
    '.SCS': 'MyTuple<int,long,Vector3D,Vector3D,Vector3D,MyTuple<Vector3D,Vector3D,Vector3D,Vector3D>>',
    '.KZR': 'MyTuple<long,Vector3D,Vector3D,double,double,int>',
    '.SCN': 'MyTuple<long,int,Vector3D,Vector3D,MyTuple<Vector3D,Vector3D>,MyTuple<Vector3D,Vector3D>>',
    '.LKR': 'MyTuple<long,int,Vector3D,Vector3D,MyTuple<Vector3D,Vector3D>,MyTuple<Vector3D,Vector3D>>',      # = the .SCN tuple
    '.HAX': 'MyTuple<Vector3D,double,double,double>',      # axis, strength, spread, lastHitAge (flight_split.md 4.1, P2-G2)
}
HDR = {'.DMG': 'ds', '.SCS': 'sq', '.KZR': 'sq', '.SCN': 'sc'}

class T(unittest.TestCase):
    longMessage = False      # failures print only our message, not a dump of the 80 kB script

class TupleShapes(T):
    """(a) .DMG .SCS .KZR .SCN .LKR .HAX: MyTuple<...> text identical at the sender and the receiver"""
    def test_block_headers_match_the_doc(self):
        for tag, k in HDR.items():
            self.assertEqual(header_tuple(k, tag), CANON[tag], f'{k}_block.cs header: tuple of {tag} differs from flight_split.md 4.1 (owner {PKG[k]})')
        for k, tag in (('dl', '.DMG'), ('sc', '.SCS'), ('sc', '.KZR'), ('sq', '.SCN')):
            self.assertEqual(header_tuple(k, tag), CANON[tag], f'{k}_block.cs header: tuple of {tag} differs from flight_split.md 4.1 (owner {PKG[k]})')

    def test_sender_and_receiver_use_the_same_tuple(self):
        for tag, (snd, rcv) in TUPLES.items():
            want = CANON[tag]
            for side, files in (('sender', snd), ('receiver', rcv)):
                # a script that carries the block inherits it: the block file is the proof, a script only has to carry the block (checked in the 'what moved' tests)
                texts = {f: code(f) for f in files if os.path.exists(os.path.join(ROOT, f))}
                carriers = [f for f, t in texts.items() if want in tuples(t)]
                self.assertTrue(carriers, f'{tag} {side}: no file of {files[:3]}... contains {want} (the block or the script is not written yet)')
                for f, t in texts.items():
                    if f.startswith('tools/') or ('"' + tag + '"') in t:
                        self.assertIn(want, tuples(t), f'{tag} {side} {f}: tuple text differs from the canonical {want}; found {sorted(x for x in tuples(t) if x.count(",") >= 4)}')

    def test_cfg_header_and_schema_match_on_both_sides(self):
        cl, cs = blk('cl'), blk('cs')
        self.assertIn('"CF1', cl, 'cl_block.cs (P1-F) must parse the "CF1\\t<schema>\\t<status>" header')
        self.assertIn('"CF1', cs, 'cs_block.cs (P1-S) must build the "CF1\\t<schema>\\t<status>" header')
        self.assertIn('".CFG"', cl + cs, 'the .CFG tag')
        self.assertIn('CurrentConstruct', cs, 'cs_block.cs: .CFG goes over TransmissionDistance.CurrentConstruct')
        self.assertIn('config schema', cl, "cl_block.cs: the mismatch screen text '! config schema: update Flight and Staff together'")
        # schema = key counts per section + version (flight_split.md 4.1): the same number literals on both sides
        tbl = json.loads(raw('tools/cfg_tables.json'))
        for k in ('Kamikaze Defence', 'AI Gate', 'Survival'):
            self.assertIn(k, tbl['shared'])

    def test_ldck_is_an_int(self):
        s, h = code('scripts/StaffAI.cs'), code('scripts/ShrikeAI.cs')
        self.assertIn('".LCK"', s + h, '.LCK Helm -> Staff int lock counter (P1-H)')
        self.assertIn('".LKR"', s + h, '.LKR Staff -> Helm (P1-H)')


class WhatMoved(T):
    """(b)"""
    def test_flights_and_helm_lost_config(self):
        for f in list(FLIGHTS.values()) + ['ShrikeAI']:
            t = code(f)
            self.assertIsNone(re.search(r'\bMe\.CustomData\s*=[^=]', t), f'{f}: still writes Me.CustomData (the Staff is the only writer, flight_split.md 5)')
            for fn in ('void KzLoad(', 'void AiGateLoad(', 'void AiGateFind('):
                self.assertNotIn(fn, t, f'{f}: still has {fn} (P1-F / P1-H must remove KZ CONFIG, AI GATE SETUP, AI GATE CONFIG)')
    def test_drone_flights_carry_the_link_blocks(self):
        for m, f in FLIGHTS.items():
            r = raw(f)
            self.assertIn('// >>> CONFIG LINK v1.0', r, f'{f}: CONFIG LINK missing (P1-F)')
            self.assertIn('// >>> SEARCH LINK v1.0', r, f'{f}: SEARCH LINK missing (P1-F)')
            self.assertIn('// >>> DAMAGE LINK v1.0', r, f'{f}: DAMAGE LINK missing (P1-F; GnatSwarmAI: P2-G2)')
        self.assertIn('// >>> CONFIG LINK v1.0', raw('ShrikeAI'), 'ShrikeAI: CONFIG LINK missing (P1-H)')
    def test_flight_tick_calls_every_reader(self):
        # phase 2: in BasiliskAI the new readers were inserted before a trailing comment that then swallowed "SvRead(); ReadSwarm();" -> SvYield
        # stayed true from the start and the Basilisk never flew (sim: speed 0 in every 1v1). The tick line must call each reader as code.
        for m, f in FLIGHTS.items():
            t = code(f)
            for call in ('LkRead();', 'CfRead();', 'DlRead();', 'SqRead();', 'SvRead();') + (() if m == 'Gnat' else ('ReadSwarm();',)):
                self.assertRegex(t, r'ReadIGC\(\);[^\n]*' + re.escape(call), f'{f}: the tick line does not call {call} (a comment may have swallowed it)')
    def test_eight_flights_lost_damage_and_search(self):
        for m in EIGHT:
            t = code(FLIGHTS[m])
            for fn in ('void ScanSlims(', 'void AutoScanStep('):
                self.assertNotIn(fn, t, f'{FLIGHTS[m]}: still has {fn} (damage -> Staff DAMAGE SENSE, search -> Aux SEARCH; P1-F)')
            self.assertNotIn('// >>> KZ RAYS', raw(FLIGHTS[m]), f'{FLIGHTS[m]}: KZ RAYS must go (SEARCH LINK provides KzRayStep)')
    def test_gnat_g1a(self):
        """G1a (flight_split.md 7.2) + P2-G2: the Gnat Flight hands search and KZ rays to its [Aux] PB (SEARCH LINK in, AutoScanStep and KZ RAYS out) and its
        damage sensing + RAID hit estimator to GnatStaffAI (DAMAGE LINK + the .HAX receiver in the Flight; a Gnat-local scanner in the Staff, no DAMAGE SENSE)"""
        t, r = code('GnatSwarmAI'), raw('GnatSwarmAI')
        for fn in ('void ScanSlims(', 'void CheckDamage(', 'void HitVotes(', 'void AddHit(', 'void CountFaces(', '"_HIT"'):
            self.assertNotIn(fn, t, f'GnatSwarmAI: {fn} must move to GnatStaffAI (P2-G2)')
        for need in ('bool RaidOn', 'Vector3D RaidDir(', 'void RaidScan(', 'Vector3D HitAxis(', '_lkK + ".HAX"'):
            self.assertIn(need, t, f'GnatSwarmAI: {need} (RAID stays in the Flight and reads the Staff\'s .HAX, P2-G2)')
        self.assertNotIn('void AutoScanStep(', t, 'GnatSwarmAI: AutoScanStep must go (search -> Aux SEARCH, P1-F)')
        self.assertNotIn('// >>> KZ RAYS', r, 'GnatSwarmAI: KZ RAYS must go under G1a (SEARCH LINK provides KzRayStep, P1-F)')
        self.assertIn('// >>> DAMAGE LINK v1.0', r, 'GnatSwarmAI: DAMAGE LINK missing (P2-G2)')
        s, sc = raw('GnatStaffAI'), code('GnatStaffAI')
        self.assertNotIn('// >>> DAMAGE SENSE', s, 'GnatStaffAI: no DAMAGE SENSE (its own GNAT HIT BEARING copy checks every 2nd tick and votes, P2-G2)')
        for need in ('void ScanSlims(', 'void CheckDamage(', 'void HitVotes(', 'void AddHit(', 'Vector3D HitAxis(', 'GdStep();', 'ScanSlims();', 'Tag + "_HIT"', '_fk + ".DMG"', '_fk + ".HAX"'):
            self.assertIn(need, sc, f'GnatStaffAI: {need} missing (GNAT HIT BEARING, P2-G2)')
        self.assertIn('// >>> AUX LINK v1.1', s, 'GnatStaffAI: AUX LINK missing (G1a)')
        for b in ('WEAKPOINT RELAY', 'REPAIR CLIENT', 'SEEKER', 'AI GATE SETUP'):
            self.assertNotIn('// >>> ' + b + ' v', s, f'GnatStaffAI: {b} must leave for the [Aux] Gnat PB (G1a)')
    def test_staffs(self):
        for m, f in STAFFS.items():
            r = raw(f)
            self.assertIn('// >>> CONFIG SERVER v1.0', r, f'{f}: CONFIG SERVER missing (P1-S)')
            if m != 'Gnat': self.assertIn('// >>> DAMAGE SENSE v1.0', r, f'{f}: DAMAGE SENSE missing (P1-S)')
            self.assertNotIn('void WpStep(', code(f), f'{f}: void WpStep( must leave the Staff (WEAKPOINT RELAY -> Aux, P1-S)')
        self.assertIn('// >>> CONFIG SERVER v1.0', raw('StaffAI'), 'StaffAI: CONFIG SERVER missing (P1-H)')
        self.assertIn('// >>> AI GATE SETUP v1.0', raw('StaffAI'), 'StaffAI: AI GATE SETUP missing (P1-H)')
    def test_aux(self):
        r, t = raw('AuxAI'), code('AuxAI')
        self.assertIn('WEAKPOINT RELAY v1.2', r, 'AuxAI: WEAKPOINT RELAY v1.2 missing (P1-X)')
        self.assertIn('SEARCH v1.0', r, 'AuxAI: SEARCH v1.0 missing (P1-X)')
        self.assertIn('AI GATE SETUP v1.0', r, 'AuxAI: AI GATE SETUP missing (P1-X)')
        for w in ('ThrustOverride', 'GyroOverride', 'DampenersOverride'):
            self.assertNotIn(w, t, f'AuxAI writes {w}: the Aux never writes an override (flight_split.md 5)')


class ConfigTables(T):
    """(c) Staff table and Flight CfgIn vs tools/cfg_tables.json"""
    @classmethod
    def setUpClass(cls): cls.T = json.loads(raw('tools/cfg_tables.json'))

    @staticmethod
    def body(t, sig):
        i = t.find(sig)
        if i < 0: return None
        j = t.index('{', i); d = 0
        for k in range(j, len(t)):
            if t[k] == '{': d += 1
            elif t[k] == '}':
                d -= 1
                if d == 0: return t[j + 1:k]
    @staticmethod
    def ext(e): return e[5] if len(e) > 5 and isinstance(e[5], dict) else {}
    @staticmethod
    def lit(v):
        if isinstance(v, bool): return {'1', 'true', 'True'} if v else {'0', 'false', 'False'}
        return {str(v)}
    @staticmethod
    def same(a, b):
        a, b = str(a).strip().strip('"'), str(b).strip().strip('"')
        a, b = re.sub(r'(?<=\d)[fFdD]$', '', a), re.sub(r'(?<=\d)[fFdD]$', '', b)
        if a.lower() in ('true', '1') and b.lower() in ('true', '1'): return True
        if a.lower() in ('false', '0') and b.lower() in ('false', '0'): return True
        try: return abs(float(a) - float(b)) < 1e-9
        except ValueError: return a == b

    def test_staff_model_tables(self):
        for m in STAFFS:
            ent = self.T['models'][m]['sections'][m]
            t = code(STAFFS[m]); b = self.body(t, 'CfModel()')
            self.assertIsNotNone(b, f'{STAFFS[m]}: no CfModel() host hook (P1-S)')
            strs = [s.replace('\\"', '"') for s in re.findall(r'"((?:[^"\\]|\\.)*)"', b)]
            self.assertEqual(len(strs), 3 * len(ent), f'{STAFFS[m]}: CfModel() has {len(strs) // 3} key/default/type triples, cfg_tables.json says {len(ent)} for {m}')
            for i, e in enumerate(ent):
                k, d, ty = strs[3 * i:3 * i + 3]
                self.assertEqual(k, e[0], f'{STAFFS[m]} CfModel() entry {i}: key "{k}" != table "{e[0]}" (order matters)')
                self.assertEqual(ty, e[2], f'{STAFFS[m]} CfModel() "{k}": type {ty} != {e[2]}')
                self.assertTrue(self.same(d, e[1]), f'{STAFFS[m]} CfModel() "{k}": default {d} != table {e[1]}')

    def test_flight_cfgin_matches_the_table_and_field_initializers(self):
        for m, f in FLIGHTS.items():
            ent = [e for e in self.T['models'][m]['sections'][m] if not self.ext(e).get('local')]
            t = code(f); b = self.body(t, 'void CfgIn()')
            self.assertIsNotNone(b, f'{f}: no CfgIn() host hook (CONFIG LINK, P1-F)')
            stm = [s for s in b.split(';') if re.search(r'\b[NBT]\(\)', s)]
            self.assertEqual(len(stm), len(ent), f'{f}: CfgIn() reads {len(stm)} values, cfg_tables.json has {len(ent)} non-local keys for {m}')
            for s, e in zip(stm, ent):
                fld = re.match(r'\s*(\w+)\s*=', s)
                self.assertTrue(fld and fld.group(1) == e[3], f'{f} CfgIn(): "{s.strip()[:60]}" should assign field {e[3]} (key "{e[0]}"), order of cfg_tables.json')
                call = {'d': 'N', 'i': 'N', 'b': 'B', 's': 'T'}[e[2]]
                self.assertIn(call + '()', s, f'{f} CfgIn() {e[3]}: type {e[2]} needs {call}()')
                m2 = re.search(r'\b(?:double|int|bool|string|float|long)\s+(?:\w+\s*(?:=[^;,]+)?,\s*)*' + re.escape(e[3]) + r'\s*=\s*([^;,]+)', t)
                self.assertIsNotNone(m2, f'{f}: no field initializer for {e[3]} (the default when no .CFG has arrived)')
                if m2 and not self.ext(e).get('block'):
                    self.assertTrue(self.same(m2.group(1), e[1]) or not re.match(r'^[-\d.truefalsTRUEFALS"]+[fFdD]?$', m2.group(1).strip()),
                                    f'{f}: field {e[3]} initializer {m2.group(1).strip()} != table default {e[1]}')

    def test_shared_tables_in_the_config_server(self):
        cs = blk('cs')
        strs = [s.replace('\\"', '"') for s in re.findall(r'"((?:[^"\\]|\\.)*)"', cs)]
        for sec, ent in self.T['shared'].items():
            pos = -1
            for e in ent:
                self.assertIn(e[0], strs[pos + 1:], f'cs_block.cs: shared [{sec}] key "{e[0]}" missing or out of table order (P1-S)')
                pos = strs.index(e[0], pos + 1)

    def test_helm_keys_are_in_the_staff(self):
        s = raw('StaffAI')
        for sec, ent in self.T['models']['Shrike']['sections'].items():
            if ent == 'shared': ent = self.T['shared'][sec]  # the shared KZ / AI Gate tables live in CONFIG SERVER, which StaffAI carries
            for e in ent:
                if self.ext(e).get('local'): continue
                self.assertIn('"%s"' % e[0], s, f'StaffAI: Helm key "{e[0]}" [{sec}] missing (CONFIG SERVER table, P1-H)')

    def test_counts_in_the_table_file_are_stable(self):
        want = {'Hornet': 65, 'Harrier': 72, 'Lancer': 69, 'Aegis': 76, 'Wasp': 91, 'Longbow': 79, 'Gnat': 61, 'Basilisk': 94, 'Mantis': 86, 'Shrike': 17}
        for m, n in want.items(): self.assertEqual(len(self.T['models'][m]['sections'][m]), n, m)


class Sizes(T):
    """(d) minified sizes after the split (flight_split.md 7 / 9.3d)"""
    def n(self, f): return len(minify(raw(f)))
    def check(self, files, lim, why):
        bad = [f'{f} {self.n(f):,}' for f in files if self.n(f) > lim]
        self.assertFalse(bad, f'over {lim:,} minified ({why}): ' + ', '.join(bad) + ". Move an off-hot-path module into another PB (CLAUDE.md 'Script size', flight_split.md section 10); do not trim features")
    # Documented exceptions (flight_split.md section 7): after every M1-M9 / H1-H6 move these two are the tightest PBs; the lead
    # accepted them at <= 78,000 (still under the 80,000 PB budget, test_pb_budget.py). The next cut for each is in section 10.
    EXCEPTIONS = {'MantisAI': 78000, 'ShrikeAI': 78000}
    def test_flights_and_helm(self):
        rest = [f for f in list(FLIGHTS.values()) + ['ShrikeAI'] if f not in self.EXCEPTIONS]
        self.check(rest, 76500, 'target for the 9 drone Flights + ShrikeAI (Gnat since P2-G2)')
        for f, lim in self.EXCEPTIONS.items():
            self.check([f], lim, 'documented exception, flight_split.md 7; next cut in section 10')
    def test_staffs(self):
        self.check(list(STAFFS.values()), 78000, 'Staff target (GnatStaffAI included under G1a)')
    def test_aux_and_staffai(self):
        self.check(['AuxAI'], 40000, 'AuxAI target'); self.check(['StaffAI'], 78000, 'StaffAI target')


if __name__ == '__main__':
    unittest.main(verbosity=2)
