#!/usr/bin/env python3
"""Run the sebp tests and the existing sebp checks.

    python3 -I tools/tests/run_all.py [-v] [--fast] [--update-golden]

1. unit tests: every tools/tests/test_*.py (unittest); tests that need the owner's samples (ref/user-samples, pc/enemies/Tech Hunter) are
   skipped, and counted as skipped, when those files cannot be found (set SEBP_SAMPLES to the samples folder);
2. the existing checks (skipped with --fast):
     build_all.py --only Hornet Gnat --no-zip      builds, re-validates and reloads the shipped drones (must PASS)
     probe_blueprints.py                           the risky-block probe set must still be writable
     audit.py --shape <Hornet> <Starburst sample>  generated XML vs a game-written ship: tag presence / order
     audit.py --types <sample>                     the mount-point validator on a game-written multi-grid ship (100 % attached)
--update-golden rewrites tools/tests/golden_hashes.json (sha256 of the bp.sbc of the shipped drones) after a deliberate design change.
Exit status 0 only if everything passed.
"""
import os, sys, subprocess, tempfile, unittest, time
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (HERE, TOOLS):
    if p not in sys.path: sys.path.insert(0, p)

def run(cmd, label, ok_text=None):
    t0 = time.time()
    r = subprocess.run([sys.executable, '-I'] + cmd, capture_output=True, text=True, cwd=os.path.dirname(TOOLS))
    out = (r.stdout or '') + (r.stderr or '')
    good = r.returncode == 0 and (ok_text is None or ok_text in out)
    print(f'  {"PASS" if good else "FAIL"}  {label}  ({time.time() - t0:.1f}s)')
    if not good: print('\n'.join('      ' + l for l in out.strip().splitlines()[-15:]))
    return good

def existing_checks():
    import ground
    ok = True
    with tempfile.TemporaryDirectory() as d:
        ok &= run([os.path.join(TOOLS, 'build_all.py'), '--only', 'Hornet', 'Gnat', '--no-zip', '--out', os.path.join(d, 'out')],
                  'build_all.py --only Hornet Gnat --no-zip', ' 0 FAIL')
        ok &= run([os.path.join(TOOLS, 'probe_blueprints.py'), os.path.join(d, 'probes')], 'probe_blueprints.py')
        hornet = os.path.join(d, 'out', 'Blueprints', 'Hornet', 'bp.sbc')
        if ground.have_samples():
            star = [f for f in ground.files() if ground.name_of(f) == 'Starburst']
            if star and os.path.exists(hornet):
                ok &= run([os.path.join(TOOLS, 'audit.py'), '--shape', hornet, star[0]], 'audit.py --shape Hornet vs Starburst')
            pond = [f for f in ground.files() if ground.name_of(f) == 'Pond Hopper Mk2']
            if pond:
                ok &= run([os.path.join(TOOLS, 'audit.py'), '--types', pond[0]], 'audit.py --types Pond Hopper Mk2', '(100.00%)')
        else:
            print('  SKIP  audit.py checks (no samples)')
    return ok

def main(argv):
    if '--update-golden' in argv:
        import test_sebp_ext
        test_sebp_ext.update_golden(); return 0
    verbosity = 2 if '-v' in argv else 1
    print('== unit tests')
    suite = unittest.defaultTestLoader.discover(HERE, pattern='test_*.py')
    res = unittest.TextTestRunner(verbosity=verbosity, stream=sys.stdout).run(suite)
    ok = res.wasSuccessful()
    print(f'   {res.testsRun} tests, {len(res.failures)} failures, {len(res.errors)} errors, {len(res.skipped)} skipped')
    if '--fast' not in argv:
        print('== existing sebp checks')
        ok &= bool(existing_checks())
    print('ALL PASSED' if ok else 'FAILED')
    return 0 if ok else 1

if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
