"""BATTERY v1.0 (control_arch M3, acceptance (a)): the lead solvers of the Helm are the solvers of the class' own script.
  BALL / RAIL / SHEL  ShrikeAI Lead() == HornetDroneAI Lead() (only the one-line "rocket class" dispatch is added); the speed is the battery's (ProjSpeed swap), no gravity drop
                      (space; Longbow's GravityDrop is a planet feature that the Hornet solver does not have either)
  ROCK                BatRkt = the Basilisk's Lead (RktTime, RktPath, Newton on the flight time) with the Basilisk's rocket constants (V0 100, A 600, Vmax 200) and the
                      capped model; the numbers of RkP are checked against a 60 Hz flight in SimHost selftest m3
Run: python3 -I tools/tests/run_all.py  (no sample files needed)"""
import os, re, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(ROOT, 'tools'))
from minify_cs import minify

def src(name):
    with open(os.path.join(ROOT, 'scripts', name), encoding='utf-8') as f: return f.read()

def func(text, header):
    """text of the top-level member that starts with `header` (brace matching on the minified source)"""
    m = minify(text)
    i = m.index(header)
    j = m.index('{', i); depth = 0; k = j
    while True:
        c = m[k]
        if c == '"':
            k += 1
            while m[k] != '"':
                if m[k] == '\\': k += 1
                k += 1
        elif c == '{': depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0: return m[i:k + 1].replace('\n', '')   # the minifier breaks lines wherever the length falls: compare without its line breaks
        k += 1

LEAD = 'bool Lead(Track t'

class TestSolvers(unittest.TestCase):
    def test_ballistic_solver_is_the_hornets(self):
        shrike = func(src('ShrikeAI.cs'), LEAD)
        hornet = func(src('HornetDroneAI.cs'), LEAD)
        dispatch = 'if(_rkt)return BatRkt(t,shooter,shooterVel,out point,out dir);'
        self.assertIn(dispatch, shrike)
        # flight_split M6 (2026-10-18): the Hornet's _leadT was write-only and was deleted as dead code; the Helm still reads it (HUD pip time), so its two
        # writes are the Helm's own additions, like the dispatch line
        for w in ('_leadT=0;', '_leadT=tt;'): self.assertIn(w, shrike); shrike = shrike.replace(w, '')
        self.assertEqual(shrike.replace(dispatch, ''), hornet)

    def test_rocket_solver_is_the_basilisks(self):
        bat = func(src('ShrikeAI.cs'), 'bool BatRkt(Track t')
        bas = func(src('BasiliskAI.cs'), LEAD)
        # the iteration lines of the two solvers, character for character
        for line in ('p=rp+LeadOffset(t,off,los,tt)+(tv*tt+ta*LeadK(tt,h))*LeadScale;',
                     'tt=MathHelper.Clamp(tt-f/Math.Max(sv-Vector3D.Dot(d,tv+ta*LeadKd(tt,h)),20),0.02,12);',
                     '_aimLos=los;_aimNeed=LeadMargin*LeadMiss(t,tt);',
                     'Vector3D off=AimOffset(t);',
                     'if(us>1e-3)w-=up*(q/us);'):
            self.assertIn(line, bat, line)
        for line in ('p=rp+LeadOffset(t,off,los,tt)+(tv*tt+ta*LeadK(tt,h))*LeadScale;',
                     'Vector3D off=AimOffset(t);',
                     '_aimLos=los;_aimNeed=LeadMargin*LeadMiss(t,tt);',
                     'if(us>1e-3)w-=up*(q/us);'):
            self.assertIn(line, bas, line)
        self.assertIn('for(int i=0;i<12;i++)', bat); self.assertIn('for(int i=0;i<12;i++)', bas)

    def test_rocket_path_is_the_basilisks(self):
        # RkP and RktPath: same formulas, the Basilisk's constants inlined (RktVmax 200, RktAcc 600, RktV0 100)
        rkp = func(src('ShrikeAI.cs'), 'void RkP(')
        rkt = func(src('BasiliskAI.cs'), 'void RktPath(')
        norm = lambda s: re.sub(r'\s+', '', s)
        for frag in ('s=va*T+0.5*A*T*T;q=us*T;sv=va+A*T;', 'q+=V/k*2*(Math.Atan(c)-Math.Atan(x));', 's+=V*(h-Math.Log((1+c*c)/(1+x*x))/k);', 'sv=V*(1-x*x)/(1+x*x);',
                     'double k=A/V,h=T-t1,c=Math.Tan(0.5*th),x=c*Math.Exp(-k*h);'):
            self.assertIn(norm(frag), norm(rkp)); self.assertIn(norm(frag), norm(rkt))
        for const in ('RktVmax=200', 'RktAcc=600', 'RktV0=100'):
            self.assertIn(const, norm(src('BasiliskAI.cs')))

if __name__ == '__main__':
    unittest.main()
