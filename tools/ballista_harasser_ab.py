# Ballista vs a held Tech Hunter plus a small harasser (a Hornet): harasser defence on (default) vs off (Spotter Custom Data "Enabled=false").
# Usage (repo root, out/sim and out/Blueprints built):  python3 -I tools/ballista_harasser_ab.py [seeds=6] [cap_s=90] [harasser=Hornet] [start_m=1600]
# Result and caveats: docs/design/ballista_harasser_defence.md "Results".  The stand-in Hornet leads with acceleration (LEAD v1.0), the vanilla turret model is first-order, the rotor turrets are inert in the sim.
import subprocess, json, sys, os
from concurrent.futures import ThreadPoolExecutor
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ARMS = {'on': None, 'off': 'Spotter=[Harasser Defence]\\nEnabled=false', 'kite': 'Spotter=[Harasser Defence]\\nJink Speed (m/s)=0', 'jink': 'Spotter=[Harasser Defence]\\nAway Speed (m/s)=0'}   # on = corner jink + kite (the default); kite = only open the range; jink = only the corner jink; off = module disabled (the old Helm: the harasser becomes its target)
def run(a):
    arm, seed, cap, harasser, dist = a
    cmd = ['mono', 'out/sim/SimHost.exe', 'one', '--scenario', f'ship/Ballista-vs-Tech Hunter+{harasser}', '--enemies', 'pc/enemies', '--enemy-brain', 'hold',
           '--start-distance', f'{dist}:{dist}', '--cap-seconds', str(cap), '--seed-index', str(seed)]
    if ARMS[arm]: cmd += ['--pb-custom-data', ARMS[arm]]
    out = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True).stdout
    j = json.loads([l for l in out.splitlines() if l.startswith('{')][0])
    b = j['ships'][0]; th = j['ships'][1]; h = j['ships'][2]
    return dict(arm=arm, seed=seed, taken=b['damage_taken'], lost=b['blocks_lost'], alive=b['alive'], hshots=h['shots'], hhits=h['hits'], halive=h['alive'], hdead=h.get('t_dead_s'),
                thtaken=th['damage_taken'], mind=h.get('min_enemy_dist_m'), havg=h.get('avg_speed'), outcome=j['outcome'], dur=j['duration_s'])
if __name__ == '__main__':
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 6; cap = int(sys.argv[2]) if len(sys.argv) > 2 else 90
    harasser = sys.argv[3] if len(sys.argv) > 3 else 'Hornet'; dist = int(sys.argv[4]) if len(sys.argv) > 4 else 1600
    jobs = [(arm, s, cap, harasser, dist) for arm in ARMS for s in range(seeds)]
    with ThreadPoolExecutor(4) as ex: res = list(ex.map(run, jobs))
    for arm in ARMS:
        r = [x for x in res if x['arm'] == arm]; n = len(r)
        hs = sum(x['hshots'] for x in r); hh = sum(x['hhits'] for x in r)
        print(f"{arm:3} n={n} Ballista damage taken avg {sum(x['taken'] for x in r)/n:,.0f} blocks lost avg {sum(x['lost'] for x in r)/n:.1f} dead {sum(1 for x in r if not x['alive'])} | "
              f"harasser shots {hs} hits {hh} ({100*hh/max(hs,1):.0f} %) dead {sum(1 for x in r if not x['halive'])}/{n} | Hornet closest avg {sum(x['mind'] or 0 for x in r)/n:.0f} m | TH damage taken avg {sum(x['thtaken'] for x in r)/n:,.0f}")
