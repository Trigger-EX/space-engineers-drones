# Ballista vs a Tech Hunter rushing at 100 m/s: sweep Standoff Range. Usage (repo root, out/sim built, Ballista blueprint in out/Blueprints):
#   python3 -I tools/ballista_charge_sweep.py 1200,1400,1600 8 3000 150 false   (standoffs, seeds, start distance m, cap s, Standoff By Weapons)
# Result and caveats: docs/design/ships/Ballista.md 3c.
import subprocess, json, sys, itertools
from concurrent.futures import ThreadPoolExecutor
import os; REPO=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HELM="[Link]\nBlock Prefix=[SH]\n\n[Shrike]\nIGC Tag=BALLISTA\nAI Block Search Assist=true\nStart Mode (GUARD/HUNT/ASSIST/IDLE)=HUNT\nStandoff Range={so}\nMin Range=900\nEvasive Jinking=true\nOrbit Speed=30\nCombat Speed=100\n\n[Battery]\nPrimary (AUTO/B1..B4)=B1\nStandoff By Weapons={sbw}\nSalvo (AUTO/ON/OFF)=ON\n"
def run(a):
    so,sbw,seed,dist,cap=a
    cmd=['mono','out/sim/SimHost.exe','one','--scenario','ship/Ballista-vs-Tech Hunter','--enemies','pc/enemies','--pirate-rush','true','--pirate-cruise-speed','100',
         '--start-distance',f'{dist}:{dist}','--seed-index',str(seed),'--cap-seconds',str(cap),'--pb-custom-data','Helm='+HELM.format(so=so,sbw=sbw).replace('\n','\\n')]
    out=subprocess.run(cmd,cwd=REPO,capture_output=True,text=True).stdout
    j=json.loads(out.splitlines()[0]); b,t=j['ships']
    return dict(so=so,sbw=sbw,seed=seed,dist=dist,mind=b['min_enemy_dist_m'],dmg=b['damage_taken'],lost=b['blocks_lost'],alive=b['alive'],thshots=t['shots'],thdealt=t['damage_dealt'],outcome=j['outcome'],dur=j['duration_s'],bhit=b['hits'],bdealt=b['damage_dealt'])
if __name__=='__main__':
    sos=[int(x) for x in sys.argv[1].split(',')]; seeds=range(int(sys.argv[2])); dist=int(sys.argv[3]); cap=int(sys.argv[4]); sbw=sys.argv[5] if len(sys.argv)>5 else 'false'
    jobs=[(so,sbw,s,dist,cap) for so in sos for s in seeds]
    with ThreadPoolExecutor(4) as ex: res=list(ex.map(run,jobs))
    json.dump(res,open(sys.argv[6] if len(sys.argv)>6 else '/dev/stdout','w'),indent=0) if False else None
    for so in sos:
        r=[x for x in res if x['so']==so]
        n=len(r); print(f"so={so} sbw={sbw} n={n} minDist avg {sum(x['mind'] for x in r)/n:.0f} min {min(x['mind'] for x in r):.0f} | dmg avg {sum(x['dmg'] for x in r)/n:.0f} hit-runs {sum(1 for x in r if x['dmg']>0)}/{n} dead {sum(1 for x in r if not x['alive'])} | TH blocks lost avg {sum(x['bdealt'] for x in r)/n:.0f}")
