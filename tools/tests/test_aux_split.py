#!/usr/bin/env python3
"""Staff split (docs/design/staff_split.md): REPAIR CLIENT and SEEKER moved from the 7 split Staff PBs into a fourth PB, [Aux] (scripts/AuxAI.cs),
linked by AUX LINK (tools/al_block.cs).   python3 -I tools/tests/test_aux_split.py
(a) the .SDK / .DKS / .SKW tuple types agree between sender and receiver, (b) what moved where and the Aux PB writes no override,
(c) sizes, (d) designs: one [Aux] PB inside the outline next to its own [Flight] PB (skipped until tools/designs has SUPPORT_AUX)."""
import os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.dirname(TOOLS)
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path: sys.path.insert(0, p)
from minify_cs import minify                                         # noqa: E402

SEVEN = ['HornetStaffAI', 'HarrierStaffAI', 'LancerStaffAI', 'AegisStaffAI', 'WaspStaffAI', 'LongbowStaffAI', 'MantisStaffAI']
LOCAL = ['GnatStaffAI', 'BasiliskStaffAI']

def raw(path):
    with open(os.path.join(ROOT, path), encoding='utf-8') as f: return f.read()
def code(path):   # comments out: the block headers name the tuples and hooks too
    return re.sub(r'//[^\n]*', '', raw(path))
def script(n): return 'scripts/' + n + '.cs'

def sends(t, tag):
    """the MyTuple<...> type sent on _fk + tag"""
    return re.findall(r'SendBroadcastMessage\(_fk \+ "' + re.escape(tag) + r'", new (MyTuple<[^(]*>)\(', t)
def takes(t):
    """every MyTuple<...> type a receiver tests with 'is'"""
    return set(re.findall(r'm\.Data is (MyTuple<[^)]*>)\)', t))

class AuxSplit(unittest.TestCase):
    def setUp(self):
        self.al, self.aux = code('tools/al_block.cs'), code('scripts/AuxAI.cs')

    # ---- (a) tuple shapes
    def test_sdk_tuple(self):
        s = sends(self.al, '.SDK'); self.assertEqual(len(s), 1, s)
        self.assertEqual(s[0], 'MyTuple<int, long, Vector3D, string, string, string>')
        self.assertIn(s[0], takes(self.aux))
        self.assertIn('RegisterBroadcastListener(_fk + ".SDK")', self.aux)

    def test_dks_tuple(self):
        s = sends(self.aux, '.DKS'); self.assertEqual(len(s), 1, s)
        self.assertEqual(s[0], 'MyTuple<int, Vector3D, Vector3D, Vector3D, string>')
        self.assertIn(s[0], takes(self.al))
        self.assertIn('RegisterBroadcastListener(_fk + ".DKS")', self.al)

    def test_skw_and_d2s(self):
        wp = re.search(r'void WpSeek\((MyTuple<[^)]*>) d\)', code('tools/wp_block.cs')).group(1)
        self.assertIn('void WpSeek(' + wp + ' d)', self.aux)
        self.assertIn('IGC.SendBroadcastMessage(_fk + ".SKW", d, TransmissionDistance.CurrentConstruct)', self.aux)
        self.assertIn(wp, takes(self.al)); self.assertIn('RegisterBroadcastListener(_fk + ".SKW")', self.al)
        self.assertIn('IGC.SendBroadcastMessage(_fk + ".D2S", arg, TransmissionDistance.CurrentConstruct)', self.aux)
        self.assertIn('RegisterBroadcastListener(_fk + ".D2S")', self.al)
        self.assertIn('Command((string)m.Data)', self.al)

    def test_link_is_in_construct(self):
        for name, t in (('al_block', self.al), ('AuxAI', self.aux)):
            for m in re.finditer(r'SendBroadcastMessage\(_fk \+ "\.(\w+)"[^;]*;', t):
                self.assertIn('TransmissionDistance.CurrentConstruct', m.group(0), name + ' .' + m.group(1))

    # ---- (b) what moved
    def test_seven_staffs_carry_aux_link_not_the_modules(self):
        for n in SEVEN:
            t, r = code(script(n)), raw(script(n))
            self.assertIn('// >>> AUX LINK v1.0', r, n)
            self.assertNotIn('// >>> REPAIR CLIENT v', r, n); self.assertNotIn('// >>> SEEKER v', r, n)
            for gone in ('void RpScan(', '_rpBays', 'void RpMedicStep(', 'GetTargetedEntity', 'Raycast(', '_skT', 'SetTargetingGroup'):
                self.assertNotIn(gone, t, n + ': ' + gone)
            self.assertIn('void SkStep() { }', t, n)
            self.assertIn('scripts/AuxAI.cs', r.split('// =====')[1], n + ': the header names AuxAI.cs')

    def test_local_hosts_keep_both_blocks(self):
        for n in LOCAL:
            r = raw(script(n))
            self.assertIn('// >>> REPAIR CLIENT v1.1', r, n); self.assertIn('// >>> SEEKER v1.0', r, n); self.assertNotIn('// >>> AUX LINK', r, n)

    def test_aux_has_the_modules_and_writes_no_override(self):
        r = raw('scripts/AuxAI.cs')
        self.assertIn('// >>> REPAIR CLIENT v1.1', r); self.assertIn('// >>> SEEKER v1.0', r)
        for w in ('ThrustOverride', 'GyroOverride', 'DampenersOverride', '.Pitch =', '.Yaw =', '.Roll =', 'SvSteer(', 'ApplyAction('):
            self.assertNotIn(w, self.aux, 'AuxAI writes ' + w)
        for hook in ('void DkGo()', 'void DkSteer(Vector3D v, Vector3D f, Vector3D u)', 'void DkRel()', 'bool DkHold', 'void SvRepaired()',
                     'double SvCapV()', 'void Fwd(string c)', 'bool FleetV(Vector3D v)'):
            self.assertIn(hook, self.aux, hook)
        self.assertIn('enum Mode { IDLE, GUARD, RTB }', self.aux)

    def test_blocks_call_hooks_not_actuators(self):
        rc, sk = code('tools/rc_block.cs'), code('tools/sk_block.cs')
        for w in ('ThrustOverride', 'GyroOverride', 'DampenersOverride', 'SvSteer(', '_thr', '_gyros', '_svRtb = false'):
            self.assertNotIn(w, rc, 'REPAIR CLIENT v1.1: ' + w)
        for need in ('DkGo();', 'DkSteer(', 'DkRel();', 'SvRepaired();', 'bool SvFly { get { return _rpFly || DkHold; } }'): self.assertIn(need, rc, need)
        self.assertNotIn('Override', sk)
        svs = code('tools/svs_block.cs')
        for need in ('void DkGo()', 'void DkSteer(Vector3D v, Vector3D f, Vector3D u)', 'void DkRel()', 'bool DkHold', 'void SvRepaired()'): self.assertIn(need, svs, need)
        ss = code('tools/ss_block.cs')
        for need in ('SkBlocks();', 'SkStep();', 'SkStatus();'): self.assertIn(need, ss, need)
        self.assertNotIn('Raycast(', ss); self.assertNotIn('_skT', ss)

    def test_aux_link_proxies_cover_the_api(self):
        """AUX LINK implements every REPAIR CLIENT / SEEKER name that SURVIVAL STAFF and STAFF SEARCH call"""
        for sig in ('void RpCfg(MyIni i)', 'void RpListen()', 'void RpBlocks()', 'void RpRead()', 'void RpStep()', 'string RpStatus()', 'void RpStop(string why)',
                    'void RpWant(int k)', 'bool SvFly', 'string RpMedCh', 'void SkBlocks()', 'void SkStep()', 'string SkStatus()'):
            self.assertIn(sig, self.al, sig)
            self.assertIn(sig, code('tools/rc_block.cs') + code('tools/sk_block.cs'), sig)

    def test_one_writer_rules(self):
        al = self.al
        self.assertIn('_tick - _alDkTk <= 15 && !_down', al)           # setpoint hold 15 ticks, never while the Flight is down
        self.assertIn('if (f) DkGo(); else DkRel();', al)
        self.assertIn('if (_dkFly) { _dkOn = false; _dkRelTk = _tick; }', al)   # run gap: no write
        svs = code('tools/svs_block.cs')
        self.assertIn('if (_dkOn && _tick - _dkTk >= 3) SvSteer(v, f, u);', svs)
        self.assertIn('return _tick - _dkRelTk < 2;', svs)

    # ---- (c) sizes
    def test_sizes(self):
        for n in SEVEN:
            self.assertLessEqual(len(minify(raw(script(n)))), 75000, n)
        for n in LOCAL:
            self.assertLessEqual(len(minify(raw(script(n)))), 75000, n)
        self.assertLessEqual(len(minify(raw('scripts/AuxAI.cs'))), 30000)

    # ---- (d) designs
    def test_designs(self):
        """the 7 models (+ Watcher, Longbow L / L Seeker) carry one [Aux] PB inside kit_outline, nearest to their own [Flight] PB (<= 4 cells), the kit complete;
        Gnat keeps [Flight] + [Staff] (no cell in its 2 x 2 section), Basilisk its 3 PBs (under budget)"""
        import designs, _common as C
        allm = {}
        for m in designs.load()[0].values():
            for n, f in getattr(m, 'MODELS', {}).items(): allm.setdefault(n, f)
        for name in ('Hornet', 'Watcher', 'Harrier', 'Lancer', 'Aegis', 'Wasp', 'Longbow S', 'Longbow L', 'Longbow L Seeker', 'Mantis'):
            bp = allm[name](False); C.add_support_pbs(bp); bp.validate()
            ps = [b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]
            aux = [b for b in ps if (b['name'] or '').startswith('[Aux]')]
            self.assertEqual(len(aux), 1, name)
            self.assertTrue('void SendDks()' in aux[0]['extra'], name + ': the Aux PB carries AuxAI.cs')
            self.assertTrue(C.in_outline(aux[0]['min'], C.kit_outline(bp)), name)
            dist = lambda b: max(abs(a - c) for a, c in zip(b['min'], aux[0]['min']))
            fl = min((b for b in ps if (b['name'] or '').startswith('[Flight]')), key=dist)
            self.assertLessEqual(dist(fl), 4, name + ': the Aux PB is too far from its Flight PB')
            self.assertEqual(getattr(bp, 'kit', {}).get('skipped', []), [], name)
        bp = allm['Gnat'](False); C.add_support_pbs(bp)
        self.assertEqual(sorted(b['name'] for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')), ['[Flight] Gnat PB', '[Staff] Gnat PB'])
        bp = allm['Basilisk'](False); C.add_support_pbs(bp)
        self.assertEqual(len([b for b in bp.blocks if b['xtype'].endswith('MyProgrammableBlock')]), 3)

if __name__ == '__main__':
    unittest.main(verbosity=2)
