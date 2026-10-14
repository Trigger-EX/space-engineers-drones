#!/usr/bin/env python3
"""Writes pc/plans/charge_standoff.plan: every main drone design vs a Tech Hunter charging straight at it at 100 m/s, one plan line per
(design, standoff) from 200 m up to the longest range the design's weapon is usable, plus one line with the script's own default.

  python3 -I tools/gen_charge_plan.py [--seeds 20] [--start 3000] [--cap 120] [--speed 100]

Run the plan on the PC:  pc/run_plan.sh plans/charge_standoff.plan   (Windows: pc\\run.bat -Plan ... see pc/README_PC.md; add --only to run a few lines)
Then:  python3 -I tools/charge_standoff_report.py pc/results      and paste the table back.
Setting changed per design (everything else is the shipped default; the Tech Hunter flies the sim Pirate brain in rush mode):
"""
import argparse, os

HELM = ("[Link]\\nBlock Prefix=[SH]\\n\\n[Shrike]\\nIGC Tag=BALLISTA\\nAI Block Search Assist=true\\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=HUNT\\nStandoff Range={v}\\n"
        "Min Range=900\\nEvasive Jinking=true\\nOrbit Speed=30\\nCombat Speed=100\\n\\n[Battery]\\nPrimary (AUTO/B1..B4)=B1\\nStandoff By Weapons=false\\nSalvo (AUTO/ON/OFF)=ON\\n")

# design -> (fleet/model name, how to set the value, lowest, highest usable, step, why the top is what it is)
DESIGNS = [
    ('Hornet',    'cd:Hornet:Standoff Range vs Huge Grids',   200, 700, 100, 'gatling, script Max Fire Range 700 (the Tech Hunter is a huge grid: "Standoff Range vs Huge Grids", default 700 since 2026-10-14)'),
    ('Harrier',   'cd:Harrier:Standoff Range vs Huge Grids',  200, 700, 100, 'gatling, Max Fire Range 700 (huge-grid key, default 700)'),
    ('Lancer',    'cd:Lancer:Standoff Range vs Huge Grids',   200, 750, 100, 'autocannon, Max Fire Range 750 (huge-grid key, default 700)'),
    ('Wasp',      'cd:Wasp:Standoff Range (unlimited rockets)', 150, 650, 50, 'rockets; the sim gives no rocket inventory, so the script runs its unlimited-rockets doctrine (Max Fire Range 650, standoff default 350); "Standoff Range" is only read with a known stock'),
    ('Aegis',     'cd:Aegis:Standoff Range vs Huge Grids',    200, 780, 100, 'gatling, Max Fire Range vs Huge Grids 780 (Tech Hunter is a huge grid)'),
    ('Basilisk',  'cd:Basilisk:Standoff Range vs Huge Grids', 200, 800, 100, 'rockets, Max Fire Range vs Huge Grids 800'),
    ('Longbow S', 'cd:Longbow:Forced Range (0: auto)',        400, 1400, 100, 'small railgun 1400 m'),
    ('Longbow L', 'cd:Longbow:Forced Range (0: auto)',        400, 2000, 100, 'large railgun 2000 m'),
    ('Mantis',    'cd:Mantis:Forced Range (0: auto)',         300, 1400, 100, 'small railgun 1400 m (Forced Range beats the automatic range; since 2026-10-14 the automatic range vs huge grids is Engage Cap vs Huge Grids 1350)'),
    ('Ballista',  'helm',                                     800, 1700, 100, '27 large railguns, Helm weapon range 0.85 x 2000 m'),
]

def values(lo, hi, step):
    v = list(range(lo, hi, step)) + [hi]
    return v

def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--seeds', type=int, default=20); ap.add_argument('--start', type=int, default=3000)
    ap.add_argument('--cap', type=int, default=120); ap.add_argument('--speed', type=int, default=100)
    ap.add_argument('--out', default=os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'pc', 'plans', 'charge_standoff.plan'))
    a = ap.parse_args()
    common = (f'--enemies enemies --pirate-rush true --pirate-cruise-speed {a.speed} --start-distance {a.start}:{a.start} '
              f'--cap-seconds {a.cap} --seeds {a.seeds} --resume')
    L = [f'# Charge test: each main drone design alone vs the Tech Hunter flying straight at it at {a.speed} m/s from {a.start} m, {a.seeds} seeds per line, fight cap {a.cap} s.',
         '# One line per (design, standoff). name = charge-<design>-<standoff>; "default" = the shipped script/blueprint setting. Needs pc/enemies/Tech Hunter and the blueprints in out/Blueprints.',
         '# Report: python3 -I tools/charge_standoff_report.py pc/results   (regenerate this file: python3 -I tools/gen_charge_plan.py --seeds N)',
         '# Run a few lines only: run_plan.sh plans/charge_standoff.plan --only charge-Hornet-300,charge-Hornet-default']
    for model, how, lo, hi, step, why in DESIGNS:
        L.append(f'# --- {model}: {why}')
        slug = model.replace(' ', '')
        for v in [None] + values(lo, hi, step):
            tag = 'default' if v is None else str(v)
            if v is None: ov = ''
            elif how == 'helm': ov = f' --pb-custom-data "Helm={HELM.format(v=v)}"'
            else:
                _, sec, key = how.split(':', 2)
                ov = f' --custom-data "[{sec}]\\n{key}={v}"'
            if v is None and how == 'helm': ov = f' --pb-custom-data "Helm={HELM.format(v=1600)}"'   # the shipped Ballista setting
            L.append(f'charge-{slug}-{tag} | run --kind enemy --filter "^enemy/Tech Hunter-vs-{model}$" --enemy-fleets "{model}" {common}{ov} --out {{OUT}}')
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    open(a.out, 'w', newline='\n').write('\n'.join(L) + '\n')
    print(f'wrote {a.out}: {sum(1 for x in L if not x.startswith("#"))} lines x {a.seeds} seeds')

if __name__ == '__main__':
    main()
