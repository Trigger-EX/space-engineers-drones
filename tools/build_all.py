#!/usr/bin/env python3
"""Build, re-validate and package every drone / printer blueprint.

    python3 -I tools/build_all.py [--jobs N] [--only "Name" ...] [--out DIR] [--no-zip]

0. skips models that are already up to date: out/.build_cache.json keeps, per model, a fingerprint of the build inputs (tools/*.py, tools/designs/*.py,
   shapes.json, defs*.json and the scripts/*.cs the model embeds) and the sha256 of the files written to out/Blueprints/<Name>/.  A model is skipped when the
   fingerprint is unchanged and the files on disk still match their checksums (its validation row is reused for MODELS.md and the zips).  --force rebuilds all.
1. imports every design module (tools/designs/*.py; absent modules are reported and skipped),
2. builds every model in its Full variant ("<Name>") into  out/Blueprints/<Name>/{bp.sbc,thumb.png}.  The Lite variants ("<Name> Lite") are
   built by tools/build_lite.py (same code, lite=True), which is rarely needed,
3. re-validates each one: sebp validate (mount points, one connected piece, no overlaps, DLC), an independent DLC check against
   defs_stats.json, the written XML (well formed, no <DLC>, no <IsStatic>, block count, unique block ids and names, Program present for
   scripted ships) and a reload of the written file through sebp.load_sbc (every CubeGrid, projected ones included, must validate),
   plus cross-checks (unique folder names and blueprint ids),
4. writes out/dist/MODELS.md and the zips  Drones_v<VERSION>_Full / Warships_v<VERSION>_Full / Printers_v<VERSION>_Full /
   SpaceEngineers_Drones_v<VERSION>_All (Full only)  (Blueprints/, Scripts/, README.md); build_lite.py writes the *_Lite zips and MODELS_Lite.md.  README text comes from
   packages/*.md and *.tmpl.  Model kinds: drone, ship (the warships and the Cradle Pod: designs.SHIP_MODULES), printer, test (never packaged).

Exit code 1 if any model fails or any check fails.  The printers dominate the run time (bay solver), so models are built in parallel
(fork pool; --jobs, default = CPU count).
"""
import sys, os, re, time, zipfile, argparse, traceback, hashlib, json, glob
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..'))
sys.path.insert(0, HERE)
import designs                                   # noqa: E402  (puts tools/designs on sys.path)
import sebp                                      # noqa: E402
import _common as C                              # noqa: E402
from minify_cs import minify                     # noqa: E402  (PB limit: 100,000 characters)

VERSION = '2.7'
PKG_DIR = os.path.join(ROOT, 'packages')
SCRIPTS_DIR = os.path.join(ROOT, 'scripts')
AXES = ('Forward', 'Backward', 'Up', 'Down', 'Left', 'Right')
AX_SHORT = {'Forward': 'F', 'Backward': 'B', 'Up': 'U', 'Down': 'D', 'Left': 'L', 'Right': 'R'}
EXTRA_SCRIPTS = ('GnatMothership.cs', 'HornetMothership.cs', 'LongbowMothership.cs')   # optional carrier relays, shipped with drones
MIDGE_NOTES = 'MidgeNotes.md'

# ---------------------------------------------------------------------------------------------
# descriptions (the printers bring their own: printers.DOCS)
# ---------------------------------------------------------------------------------------------
DRONE_DOCS = {
    'Gnat': 'Compact swarm drone: one gatling, two axial ion thrusters (it steers by turning the whole ship), sensor, AI Offensive + AI Flight blocks on a 2-cell tail (Lite: none), belly merge latch. Flies yo-yo attack runs inside a leaderless IGC swarm.',
    'Hornet': 'The standard fighter: two gatlings, 12 ion thrusters (2 per axis), 7 cameras, 4 batteries, rear merge latch. Holds a standoff, orbits and jinks; guard / hunt / assist.',
    'Longbow S': 'Small-grid railgun sniper (1400 m). Computes the longest range at which a dodging target is still hit, takes one settled shot, jinks while reloading. Eight small reactors (4 MW, uranium loaded) carry the railgun recharge (3.6 MW) and, with the 1 MWh battery, keep it going through a full diagonal manoeuvre: 10.5 t, 5.5 m/s2 on every axis, 24 ion thrusters, two gyros.',
    'Longbow L': 'Large-grid railgun sniper (2000 m). Same logic as Longbow S; not printable (no merge block). Five small reactors (75 MW, uranium loaded) carry the railgun recharge (38 MW) and, with the 4 batteries, keep it going through a full diagonal manoeuvre: 272 t, 8.9 m/s2 (7.6 up), 41 ion thrusters, two gyros.',
    'Wasp': 'Hornet frame with small rocket launchers (8 rockets). Integrates the rocket speed curve to lead targets, fires short salvos, refuses shots at unpredictable targets or near hive mates.',
    'Lancer': 'Hornet-class frame with 1x1x5 autocannons (800 m/s shells). Burst fire only when the solution is tight; standoff 420 m.',
    'Watcher': 'Unarmed scout / spotter: 11 cameras, 5000 m antenna. Keeps 900 m standoff and shares targets with the hive (runs the Hornet script in scout mode).',
    'Midge': 'EXPERIMENTAL scriptless drone: gatling, AI Offensive + AI Flight + AI Defensive blocks, no Programmable Block. The AI blocks must be set up by hand (Scripts/MidgeNotes.md). Midge Lite is only a renamed Gnat Lite running GnatSwarmAI.cs.',
    'Harrier': 'Fast interceptor: Hornet frame with four main engines (12 m/s2 forward). Hunts small fast grids, drones and missiles first; shares threat cues on the DRONE_CUE channel.',
    'Aegis': 'Escort / point defence: four gatlings, two decoys (Lite: armour plates). Parks on a station around the carrier and screens it by standing on the carrier-to-threat line.',
    'Mantis': 'Railgun + gatling hybrid on a Longbow S spine: alpha strike at range, gatling up close. Engage cap 650 m. Eight small reactors (4 MW, uranium loaded) and the 1 MWh battery keep the railgun recharging through a full diagonal manoeuvre: 10.9 t, 9.3 m/s2 forward, 5.3 on the other axes, 27 ion thrusters, two gyros.',
    'Basilisk': 'LARGE-grid armoured rocket gunship: four large missile launchers, six heavy armour blocks, 12 thrusters; Full adds a large gatling turret as point defence. Not printable.',
    'Longbow L Seeker': 'TEST SHIP for subsystem seeking: Longbow L with an artillery turret (2 km, the railgun\'s reach) named [Seeker] on its back. The script sets the turret to a targeting group (Custom Data: Seeker Group, default PowerSystems), reads where it points and aims its railgun slugs down that line. The status screen shows a "Seek:" line with what the turret reports. Same reactors and thrusters as the Longbow L (286 t, 8.5 m/s2, 7.2 up).',
    'Basilisk Seeker': 'TEST SHIP for subsystem seeking: the Full Basilisk with its roof turret named [Seeker]. The script sets that turret to a targeting group (Custom Data: Seeker Group, default PowerSystems), reads where it points, and drills its rockets into the hull over that block. The status screen shows a "Seek:" line with what the turret reports.',
}
# extra per-model notes shown in the README
MODEL_NOTES = {
    'Gnat': 'Thrust only along the nose axis (forward / back), by design.',
    'Midge': 'Experimental; no script. Configure the three AI blocks by hand (Scripts/MidgeNotes.md). Gatling ammo is not preloaded.',
    'Basilisk': 'Large grid: paste it as a free ship; it has no merge block and cannot be put in a printer bay.',
    'Longbow L Seeker': 'Large grid, paste only. Experimental (see Basilisk Seeker). Load artillery shells into the turret: a turret without ammunition may not pick a target at all, and then it cannot guide the railgun.',
    'Basilisk Seeker': 'Large grid, paste only. Experimental: tells you whether the game lets a script find an enemy\'s power blocks through a turret. Decoys pull the turret first.',
    'Longbow L': 'Large grid, no merge block: paste only.',
    'Longbow S': 'No docking face in the base model; printers add a docking stalk to it.',
    'Wasp': 'Needs rockets loaded by hand in survival; relies on the terminal action ShootOnce (unverified).',
    'Aegis': 'For a carrier link copy HornetMothership.cs and change Tag to AEGIS, or use the default extra channel HORNET_HOME.',
    'Watcher': 'Configured only through the PB Custom Data ([Hornet] section, IGC Tag HORNET, Start Mode HUNT). `range` is clamped to Max Fire Range; use Standoff in Custom Data for larger values.',
}
COMMAND_DOC = {
    'guard': 'hold the home point, engage anything inside Guard Radius', 'hunt': 'chase and engage any target, actively scan',
    'sentry': 'hold this spot, still, until a target comes within Sentry Range (2.5 km, the AI Offensive search range); then hunt it down with no leash and come back',
    'assist': 'you fly, the script aims and fires', 'rtb': 'fly home, then idle', 'idle': 'release all control (`stop` is an alias)',
    'lock': 'raycast-lock whatever the nose camera points at', 'unlock': 'drop the current target', 'next': 'ignore the current target for 60 s',
    'split': 'split targets across the hive', 'focus': 'focus fire', 'ai': '`ai on|off` AI-block search assist', 'aim': '`aim <mode>` aim point (AUTO / BREACH / ENGINES / FACING / SPREAD / CENTER)', 'breach': '`breach on|off` AUTO drills one hole into big ships (v2.6)', 'seeker': '`seeker on|off` let a [Seeker] turret guide the aim (v2.6, experimental)',
    'aialert': 'treat the AI-block alert as raised', 'home': 'set the home point to the current position', 'range': '`range <m>` set the standoff',
    'reload': 're-read Custom Data and re-scan blocks', 'start': 'start printing',
    'max': '`max <n>` set Max Drones', 'release': 'printers: `release [bay]` release finished drones; warship Staff / bay PBs: `release <n|a-b|all> [delay_s] [force]` let go of clamped drones (waits for a calm ship unless `force`)', 'hold': '`hold <bay>` keep a bay paused',
    'unhold': '`unhold <bay>` resume a bay', 'relay': '`relay on|off` forward carrier turret targets', 'launch': 'Stinger rack: leave the dock and hunt; warship Staff: `launch [n|a-b|all] [force]` park the Helm, release the bays, hand the Helm its mode back',
    'manual': 'warship Helm: the ship is hand-flown (nothing of the script touches thrusters or gyros)', 'auto': 'warship Helm: the script flies again even with a pilot aboard', 'pilot': 'warship Helm: toggle manual / auto',
    'wing': 'warship Staff: `wing hunt|guard|rtb|follow|satellite|lock|roll` order the drones', 'page': 'warship Staff: `page [1|2]` status / wing roll call on the PB screen', 'fleet': 'warship Staff: `fleet <command>` on the fleet channel',
    'recall': 'warship Staff / bays: wing rtb, then satellite, bays cancel and re-open', 'doors': 'warship Staff: `doors open|close` the hangar doors', 'status': 'refresh the screen',
    'abort': 'abort and release', 'disarm': 'disarm the warheads',
}
HIVE_DOC = {'stop': 'stop printing, all welders off'}
PAL_ALL = dict(C.PAL, welder=(70, 190, 90), proj=(150, 90, 40), cargo=(120, 100, 70))
CPU_HEAVY_FIRST = {'printer': 0, 'ship': 0, 'drone': 1, 'test': 1}

# ---------------------------------------------------------------------------------------------
# up-to-date check (skip models whose inputs and output files are unchanged)
# ---------------------------------------------------------------------------------------------
CACHE_NAME = '.build_cache.json'
OUT_FILES = ('bp.sbc', 'thumb.png')

def sha_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''): h.update(chunk)
    return h.hexdigest()

def code_fingerprint():
    """one hash over everything except the scripts that a design module reads to build a blueprint"""
    files = sorted(glob.glob(os.path.join(HERE, '*.py')) + glob.glob(os.path.join(HERE, 'designs', '*.py')) + [os.path.join(HERE, 'shapes.json')]
                   + glob.glob(os.path.join(ROOT, 'defs*.json')))
    h = hashlib.sha256()
    for p in files:
        h.update(os.path.relpath(p, ROOT).encode() + b'\0' + (sha_file(p).encode() if os.path.exists(p) else b'-') + b'\0')
    return h.hexdigest()

def model_key(code_fp, scripts):
    h = hashlib.sha256(code_fp.encode())
    for s in sorted(scripts):
        p = os.path.join(SCRIPTS_DIR, s)
        h.update(s.encode() + b'\0' + (sha_file(p).encode() if os.path.exists(p) else b'-') + b'\0')
    return h.hexdigest()

def load_cache(path):
    try:
        with open(path, encoding='utf-8') as f: return json.load(f)
    except (OSError, ValueError): return {}

def save_cache(path, cache):
    tmp = path + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f: json.dump(cache, f, indent=0, default=str)
    os.replace(tmp, path)

def cached_row(cache, task, code_fp):
    """the stored row of `task` if its inputs are unchanged and its output files still match their checksums, else None"""
    kind, modname, model, lite, outroot = task
    label = model + (' Lite' if lite else '')
    e = cache.get(label)
    if not e or e.get('key') != model_key(code_fp, e['row'].get('scripts', [])): return None
    folder = e['row'].get('folder')
    if not folder or os.path.abspath(folder) != os.path.abspath(os.path.join(outroot, 'Blueprints', e['row'].get('name', label))): return None
    if os.path.exists(os.path.join(folder, 'bp.sbcB5')): return None
    for f in OUT_FILES:
        p = os.path.join(folder, f)
        if not os.path.exists(p) or sha_file(p) != e['files'].get(f): return None
    return e['row']

def cache_entry(row, code_fp):
    return dict(key=model_key(code_fp, row['scripts']), files={f: sha_file(os.path.join(row['folder'], f)) for f in OUT_FILES}, row=row)

# ---------------------------------------------------------------------------------------------
# build + validate one model (runs in a pool worker)
# ---------------------------------------------------------------------------------------------
_MODS = {}
_SCRIPT_ESC = None

def script_table():
    global _SCRIPT_ESC
    if _SCRIPT_ESC is None:
        _SCRIPT_ESC = {}
        for f in sorted(os.listdir(SCRIPTS_DIR)):
            if f.endswith('.cs'):
                t = minify(open(os.path.join(SCRIPTS_DIR, f), encoding='utf-8').read())   # blueprints carry the minified text
                _SCRIPT_ESC[f] = (escape(t), escape(escape(t)))
    return _SCRIPT_ESC

def scripts_in(xml):
    return sorted(f for f, (a, b) in script_table().items() if a in xml or b in xml)

def check_written(bp, folder, label, scripted_expected):
    """checks on the file that was just written; returns (list of problems, xml text)"""
    probs = []
    path = os.path.join(folder, 'bp.sbc')
    xml = open(path, encoding='utf-8').read()
    try:
        root = ET.fromstring(xml)
    except ET.ParseError as e:
        return [f'bp.sbc is not well formed: {e}'], xml, None
    if root.findall('.//DLC'): probs.append('written XML contains <DLC>')
    if root.findall('.//IsStatic'): probs.append('written XML contains <IsStatic> (printers and drones must be dynamic)')
    if os.path.exists(os.path.join(folder, 'bp.sbcB5')): probs.append('stale bp.sbcB5 present')
    grids = root.findall('.//CubeGrid')
    main = grids[0]
    cbs = main.find('CubeBlocks')
    if len(cbs) != len(bp.blocks): probs.append(f'XML has {len(cbs)} blocks, model has {len(bp.blocks)}')
    ids = [e.findtext('EntityId') for e in cbs if e.findtext('EntityId')]   # plain armour carries none
    if len(set(ids)) != len(ids): probs.append('duplicate block EntityId')
    names = [e.findtext('CustomName') for e in cbs if e.findtext('CustomName')]
    dup = sorted({n for n in names if names.count(n) > 1})
    if dup: probs.append(f'duplicate block names: {dup[:3]}')
    if main.findtext('GridSizeEnum') != bp.grid: probs.append('GridSizeEnum mismatch')
    progs = [e for e in cbs.iter('Program') if (e.text or '').strip()]
    if scripted_expected and not progs: probs.append('script expected but no <Program> text in the XML')
    for e in progs:
        if len(e.text) > 100000: probs.append(f'<Program> has {len(e.text)} characters; the Programmable Block takes at most 100,000')
    # reload the written file: every CubeGrid (main + projected) must validate and the main one must keep its block count
    try:
        loaded = sebp.load_sbc(path, allow_dlc=False)
        if len(loaded[0].blocks) != len(bp.blocks) or loaded[0].skipped:
            probs.append(f'reload: main grid {len(loaded[0].blocks)} blocks, skipped {loaded[0].skipped[:2]}')
        for i, g in enumerate(loaded):
            if g.skipped and i: probs.append(f'reload: projected grid {i} has skipped blocks {g.skipped[:2]}')
            try: g.validate()
            except AssertionError as e: probs.append(f'reload: grid {i} ({g.name}): {str(e)[:160]}')
    except Exception as e:                                           # noqa: BLE001
        probs.append(f'reload failed: {type(e).__name__}: {e}')
    m = re.search(r'<Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="([^"]*)"', xml)
    return probs, xml, (m.group(1) if m else None)

def build_one(task):
    kind, modname, model, lite, outroot = task
    label = model + (' Lite' if lite else '')
    t0 = time.time()
    row = dict(kind=kind, module=modname, model=model, lite=lite, label=label, ok=False, problems=[])
    try:
        bp = _MODS[modname].MODELS[model](lite)
        C.add_support_pbs(bp)                                          # [Thrust] PB for THRUST VECTORING, [Staff] PB for the split drones
        C.finalize(bp)
        row['name'] = bp.name
        if bp.name != label: row['problems'].append(f'blueprint name {bp.name!r} != expected {label!r}')
        bp.validate()                                                   # mounts, one piece, no overlaps, reserve zones, DLC
        dlc = sorted({d for b in bp.blocks for d in (C._stat(b).get('dlc') or [])})
        if dlc or bp.dlc_used(): row['problems'].append(f'DLC blocks: {dlc or bp.dlc_used()}')
        acc, mass = C.twr(bp)
        st = bp.stats()
        cs = C.cost(bp)
        scripted = any('<Program>' in (b.get('extra') or '') for b in bp.blocks)
        folder = os.path.join(outroot, 'Blueprints', bp.name)
        bp.write(folder, bp.name)
        bays = getattr(bp, 'bays', None)
        sub = (f'{len(bays)} bays, {sum(len(b["welders"]) for b in bays)} welders' if bays else f'{len(bp.blocks)} blocks, {mass / 1000:.1f} t')
        bp.thumb(os.path.join(folder, 'thumb.png'), bp.name, sub, PAL_ALL)
        probs, xml, bid = check_written(bp, folder, label, scripted)
        row['problems'] += probs
        row.update(
            folder=folder, bid=bid, grid=bp.grid, blocks=len(bp.blocks), mass=mass, dims=st['dims'], accel={d: acc[d] for d in AXES},
            pcu=sum(C._stat(b).get('pcu') or 0 for b in bp.blocks), cost=cs, counts=C.block_counts(bp), scripted=scripted,
            scripts=scripts_in(xml), merges=sum(1 for b in bp.blocks if b['kind'] == 'merge'), size_kb=len(xml) // 1024,
            bays=len(bays) if bays else 0, welders=sum(len(b['welders']) for b in bays) if bays else 0,
            has_thumb=os.path.exists(os.path.join(folder, 'thumb.png')))
        if kind in ('drone', 'ship') and row['merges']:
            try:
                for b in bp.blocks:
                    if b['kind'] == 'merge': row['dock'] = C.check_docking(bp, b)
            except AssertionError as e: row['problems'].append(f'docking: {e}')
        row['ok'] = not row['problems']
    except AssertionError as e:
        row['problems'].append(f'build/validate: {e}')
    except Exception as e:                                                # noqa: BLE001
        row['problems'].append(f'{type(e).__name__}: {e}\n{traceback.format_exc(limit=4)}')
    row['secs'] = round(time.time() - t0, 1)
    return row

# ---------------------------------------------------------------------------------------------
# formatting
# ---------------------------------------------------------------------------------------------
def fmt_acc(r):
    a = r['accel']
    return ' '.join(f'{AX_SHORT[d]}{a[d]:.1f}' for d in AXES)

def acc_range(r):
    v = [r['accel'][d] for d in AXES]
    return f'{min(v):.1f}' if min(v) == max(v) else f'{min(v):.1f}-{max(v):.1f}'

def cost_str(r, keys=('Iron', 'Nickel', 'Cobalt', 'Silicon', 'Gold', 'Platinum')):
    cs = r['cost']
    s = ' '.join(f'{k} {cs[k]:.0f}' for k in keys if cs.get(k))
    other = {k: v for k, v in cs.items() if k not in keys and v}
    return s + (' ' + ' '.join(f'{k} {v:.0f}' for k, v in sorted(other.items())) if other else '')

def models_md(rows, failed):
    out = [f'# Models v{VERSION}: master table', '',
           'Generated by `tools/build_all.py`. Mass, thrust and cost come from `defs_stats.json` (mass = sum of component masses, thrust = '
           '`force_n` of ion thrusters per direction, no gravity or planet effects). Accel = thrust / mass in m/s2 along F(orward) B(ack) '
           'U(p) D(own) L(eft) R(ight) of the ship; Gnat-class ships thrust on F/B, Tube drones on F only, by design, a Static Nest has no thrusters. '
           'PCU = sum of block PCU. Cost = ingots needed (Iron, Nickel, Cobalt, Silicon, Gold, Platinum). No ship uses DLC blocks (asserted).', '']
    for kind, title in (('drone', 'Drones'), ('ship', 'Warships and pods'), ('printer', 'Printers')):
        rs = [r for r in rows if r['kind'] == kind and r['ok']]
        if not rs: continue
        out += [f'## {title}', '']
        extra = ' Bays | Welders |' if kind == 'printer' else ''
        out += ['| Model | Variant | Grid | Blocks | Mass kg | Size m | Accel m/s2 (F B U D L R) | PCU | Merge | Script |' + extra + ' Cost |',
                '|---|---|---|--:|--:|---|---|--:|--:|---|' + ('--:|--:|' if kind == 'printer' else '') + '---|']
        for r in rs:
            sc = ', '.join(s[:-3] for s in r['scripts']) or ('none' if not r['scripted'] else '?')
            ex = f' {r["bays"]} | {r["welders"]} |' if kind == 'printer' else ''
            out.append(f'| {r["model"]} | {"Lite" if r["lite"] else "Full"} | {r["grid"]} | {r["blocks"]} | {r["mass"]:.0f} | '
                       f'{"x".join(str(d) for d in r["dims"])} | {fmt_acc(r)} | {r["pcu"]} | {r["merges"]} | {sc} |{ex} {cost_str(r)} |')
        out.append('')
    if failed:
        out += ['## FAILED (not packaged)', ''] + [f'* {r["label"]}: ' + '; '.join(p.splitlines()[0] for p in r['problems']) for r in failed] + ['']
    return '\n'.join(out)

# ---------------------------------------------------------------------------------------------
# script reference (derived from the scripts themselves)
# ---------------------------------------------------------------------------------------------
def script_reference(script):
    t = open(os.path.join(SCRIPTS_DIR, script), encoding='utf-8').read()
    sec = re.search(r'const string S = "([^"]*)"', t)
    sec = sec.group(1) if sec else None
    cmds = []
    for c in re.findall(r'case "([a-z][a-z ]*)"', t):
        if c not in cmds: cmds.append(c)
    if script == 'StingerAI.cs': cmds = ['launch', 'abort', 'disarm', 'reload']
    groups = {}
    for first, key in re.findall(r'\bC(?:fg)?\(\s*(S|"[^"]*"|\w+)\s*,\s*"([^"]+)"', t):
        s = sec if first == 'S' else ('Bay N' if first == 'sec' else first.strip('"'))
        groups.setdefault(s, [])
        if key not in groups[s]: groups[s].append(key)
    ver = re.search(r'(?:VER|Version)\s*=\s*"([^"]*)"', t)
    return dict(script=script, section=sec, commands=cmds, keys=groups, ver=ver.group(1) if ver else '')

def script_reference_md(scripts, level='###'):
    out = []
    for s in scripts:
        if s.endswith('Mothership.cs'):
            out += [f'{level} {s} (optional carrier link)', '',
                    'Put it on a carrier ship\'s Programmable Block (needs an antenna). It broadcasts the carrier position as the drones\' moving home point, '
                    'forwards what its turrets track to the drones and relays commands: run it with an argument such as `guard`, `hunt`, `rtb`, `idle`, `unlock` '
                    '(Gnat link: `form`, `attack`, `rtb`, `idle`, `unlock`). Its `Tag` constant must equal the drones\' `IGC Tag`.', '']
            continue
        if not s.endswith('.cs') or not os.path.exists(os.path.join(SCRIPTS_DIR, s)): continue
        r = script_reference(s)
        out += [f'{level} {s}' + (f' (v{r["ver"]})' if r['ver'] else ''), '']
        if r['commands']:
            out.append('Commands (PB argument, toolbar, or IGC `<IGC Tag>_CMD`):')
            out.append('')
            for c in r['commands']:
                if c == 'stop' and s != 'HiveMotherAI.cs': continue        # alias of idle, noted there
                out.append(f'* `{c}`' + (f': {(HIVE_DOC if s == "HiveMotherAI.cs" else {}).get(c) or COMMAND_DOC[c]}' if (c in COMMAND_DOC or (s == 'HiveMotherAI.cs' and c in HIVE_DOC)) else ''))
            out.append('')
        for s2, keys in r['keys'].items():
            out.append(f'Custom Data `[{s2}]` keys: ' + ', '.join(f'`{k}`' for k in keys) + '.')
            out.append('')
        out.append('')
    return '\n'.join(out)

# ---------------------------------------------------------------------------------------------
# README assembly
# ---------------------------------------------------------------------------------------------
def read_pkg(name):
    return open(os.path.join(PKG_DIR, name), encoding='utf-8').read()

def scoped(name, kinds, number=False):
    """lines of packages/<name> whose [scope] tag fits `kinds` (set of 'drone'/'ship'/'printer'); optionally renumbered"""
    out, n = [], 0
    for line in read_pkg(name).splitlines():
        m = re.match(r'^(?:\d+\.|-)\s+\[(drone|printer|ship|both)\]\s+(.*)$', line)
        if not m: continue
        scope, text = m.groups()
        if scope != 'both' and scope not in kinds: continue
        n += 1
        out.append(f'{n}. {text}' if number else f'- {text}')
    return '\n'.join(out)

def model_note(model):
    """README note of a model: this file's MODEL_NOTES, else the MODEL_NOTES dict of a design module (warship modules bring their own)"""
    if model in MODEL_NOTES: return MODEL_NOTES[model]
    for m in designs.DRONE_MODULES:
        d = getattr(_MODS.get(m), 'MODEL_NOTES', {}) if _MODS.get(m) else {}
        if model in d: return d[model]
    return ''

def model_docs(kind, model):
    if kind == 'printer':
        for m in ('printers',):
            d = getattr(_MODS.get(m), 'DOCS', {}) if _MODS.get(m) else {}
            if model in d: return d[model]
        return ''
    for m in designs.DRONE_MODULES + designs.TEST_MODULES:            # warship and test modules bring their own DOCS
        d = getattr(_MODS.get(m), 'DOCS', {}) if _MODS.get(m) else {}
        if model in d: return d[model]
    return DRONE_DOCS.get(model, '')

def model_sections(rows):
    out, seen = [], []
    for r in rows:
        if r['model'] not in seen: seen.append(r['model'])
    for m in seen:
        rs = [r for r in rows if r['model'] == m]
        title = m + (' (experimental)' if m == 'Midge' else '')
        out += [f'### {title}', '', model_docs(rs[0]['kind'], m), '']
        for r in sorted(rs, key=lambda r: r['lite']):
            sc = ', '.join(r['scripts']) or 'none'
            extra = f', {r["bays"]} bays / {r["welders"]} welders' if r['bays'] else ''
            out.append(f'* **{r["label"]}**: {r["blocks"]} blocks, {r["mass"]:.0f} kg, {"x".join(str(d) for d in r["dims"])} m, '
                       f'accel {fmt_acc(r)} m/s2, PCU {r["pcu"]}{extra}; script: {sc}; cost {cost_str(r)}')
        if model_note(m): out += ['', f'Note: {model_note(m)}']
        out.append('')
    return '\n'.join(out)

def model_table(rows):
    out = ['| Model | Variant | Blocks | Mass kg | Accel m/s2 | PCU | Script |', '|---|---|--:|--:|--:|--:|---|']
    for r in rows:
        out.append(f'| {r["model"]} | {"Lite" if r["lite"] else "Full"} | {r["blocks"]} | {r["mass"]:.0f} | {acc_range(r)} | {r["pcu"]} | '
                   f'{", ".join(s[:-3] for s in r["scripts"]) or "none"} |')
    return '\n'.join(out)

PACKAGES = [   # (zip base name, kinds, variants, template, intro)
    ('Drones_v%s_Full' % VERSION, ('drone',), (False,), 'README_Drones.md.tmpl',
     'All drone models in their **Full** design (Sensor, AI blocks, Decoy, turret where the model has them). Try `Drones_v%s_Lite` if a Full ship does not paste.' % VERSION),
    ('Drones_v%s_Lite' % VERSION, ('drone',), (True,), 'README_Drones.md.tmpl',
     'All drone models in their **Lite** design: only block types whose XML was copied from a game-written ship (plus the small merge block). Lite ships still fly and fight.'),
    ('Printers_v%s_Full' % VERSION, ('printer',), (False,), 'README_Printers.md.tmpl',
     'Drone printers in their **Full** design. Each projector holds the **Full** drone; Full printers also carry two component-stock cargo containers.'),
    ('Printers_v%s_Lite' % VERSION, ('printer',), (True,), 'README_Printers.md.tmpl',
     'Drone printers in their **Lite** design. Each projector holds the **Lite** drone; no cargo containers.'),
    ('Warships_v%s_Full' % VERSION, ('ship',), (False,), 'README_Warships.md.tmpl',
     'The warships Shrike, Corsair, Warden and Leviathan in their **Full** design, with the Cradle Pod. Paste-ready scripts for the Helm, Staff and bay Programmable Blocks are in Scripts/.'),
    ('Warships_v%s_Lite' % VERSION, ('ship',), (True,), 'README_Warships.md.tmpl',
     'The warships Shrike, Corsair, Warden and Leviathan in their **Lite** design (only block shapes seen in a game-written blueprint: no turrets, sensors or decoys), with the Cradle Pod.'),
    ('SpaceEngineers_Drones_v%s_All' % VERSION, ('drone', 'ship', 'printer'), (False, True), 'README_All.md.tmpl',
     'Every drone, every warship and every printer, Full design, in one zip.'),
]

SHIP_PB = {'ShrikeAI.cs': 'Helm (every ship)', 'TurretAI.cs': 'Turret PB (Shrike, Corsair, Warden, Leviathan, Ballista)', 'StaffAI.cs': 'Staff (Corsair, Warden, Leviathan)', 'CradleBayAI.cs': 'Bay / Pod PB (Corsair, each pod)',
           'HornetDroneAI.cs': 'the Hornets of the Wing ships'}

def script_sizes_md(scripts):
    """table of the minified script sizes against the Programmable Block limit (the ship Helm has its own 80,000 budget, C.SCRIPT_LIMITS)"""
    out = ['| script | runs in | minified characters | limit |', '|---|---|--:|--:|']
    for s_ in scripts:
        if s_ not in SHIP_PB or not os.path.exists(os.path.join(SCRIPTS_DIR, s_)): continue
        n = len(minify(open(os.path.join(SCRIPTS_DIR, s_), encoding='utf-8').read()))
        out.append(f'| `{s_}` | {SHIP_PB[s_]} | {n:,} | {C.SCRIPT_LIMITS.get(s_, 100000):,} |')
    return '\n'.join(out)

def render_readme(tmpl, intro, variant, rows, kinds):
    kindset = set(kinds)
    scripts = sorted({s for r in rows for s in r['scripts']})
    if 'drone' in kindset: scripts += [s for s in EXTRA_SCRIPTS if s not in scripts]
    counts = ', '.join(f'{sum(1 for r in rows if r["kind"] == k)} {k} blueprints' for k in kinds)
    txt = read_pkg(tmpl)
    rep = {'VERSION': VERSION, 'VARIANT': variant, 'INTRO': intro, 'COUNTS': counts, 'INSTALL': read_pkg('install.md').strip(),
           'MODEL_TABLE': model_table(rows), 'MODEL_SECTIONS': model_sections(rows),
           'SCRIPT_REFERENCE': script_reference_md(scripts) if '{{SCRIPT_REFERENCE}}' in txt else '', 'SCRIPT_SIZES': script_sizes_md(scripts), 'UNKNOWNS': scoped('unknowns.md', kindset),
           'TESTS': scoped('tests.md', kindset, number=True), 'CHANGELOG': read_pkg('changelog.md').strip()}
    for k, v in rep.items(): txt = txt.replace('{{%s}}' % k, v)
    assert '{{' not in txt, 'unreplaced placeholder in ' + tmpl
    return txt

def make_zip(path, rows, readme, extra_files):
    scripts = sorted({s for r in rows for s in r['scripts']})
    if any(r['kind'] == 'drone' for r in rows): scripts += [s for s in EXTRA_SCRIPTS if s not in scripts]
    files = {}                                                    # arcname -> bytes
    for r in rows:
        for f in ('bp.sbc', 'thumb.png'):
            files[f'Blueprints/{r["name"]}/{f}'] = open(os.path.join(r['folder'], f), 'rb').read()
    for s in scripts:
        # Scripts/: paste-ready (minified, under the PB's 100,000-character limit); Scripts/Source/: the commented originals
        src = open(os.path.join(SCRIPTS_DIR, s), encoding='utf-8').read()
        files[f'Scripts/{s}'] = minify(src).encode('utf-8')
        files[f'Scripts/Source/{s}'] = src.encode('utf-8')
    if any(r['model'] == 'Midge' for r in rows):
        files['Scripts/' + MIDGE_NOTES] = open(os.path.join(SCRIPTS_DIR, MIDGE_NOTES), 'rb').read()
    files['README.md'] = readme.encode('utf-8')
    for n, b in extra_files.items(): files[n] = b
    if os.path.exists(path): os.remove(path)
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for n in sorted(files):
            zi = zipfile.ZipInfo(n, (2026, 1, 1, 0, 0, 0)); zi.compress_type = zipfile.ZIP_DEFLATED; zi.external_attr = 0o644 << 16
            z.writestr(zi, files[n])
    return len(files), scripts

# ---------------------------------------------------------------------------------------------
def main(argv, lites=(False,)):
    """lites: which variants this run builds: (False,) = Full (build_all.py), (True,) = Lite (build_lite.py)"""
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--jobs', type=int, default=os.cpu_count() or 1)
    ap.add_argument('--only', nargs='*', help='build only these model names (base names, e.g. "Hornet" "Hive Mother"); zips are then skipped unless --zip (written to <out>/dist_partial)')
    ap.add_argument('--out', default=os.path.join(ROOT, 'out'))
    ap.add_argument('--no-zip', action='store_true')
    ap.add_argument('--force', action='store_true', help='rebuild every model even when its inputs and output files are unchanged')
    ap.add_argument('--zip', action='store_true', help='with --only: also write the zips and READMEs of the packages that have models among the built ones, into <out>/dist_partial (tests the packaging path without the full build)')
    a = ap.parse_args(argv)
    t0 = time.time()
    mods, missing = designs.load()
    _MODS.update(mods)
    print(f'design modules loaded: {", ".join(mods)}' + (f' | not present (skipped): {", ".join(missing)}' if missing else ''))
    tasks = []
    for kind, modname, model, _fn in designs.iter_models(mods):
        if a.only and model not in a.only: continue
        for lite in lites:
            if lite and model in getattr(mods[modname], 'NO_LITE', ()): continue      # test ships have one variant
            tasks.append((kind, modname, model, lite, a.out))
    names = [(t[2], t[3]) for t in tasks]
    assert len(set(names)) == len(names), 'duplicate model name across design modules'
    bp_root = os.path.join(a.out, 'Blueprints')
    os.makedirs(bp_root, exist_ok=True)
    tasks.sort(key=lambda t: CPU_HEAVY_FIRST[t[0]])
    rows = []
    cache_path = os.path.join(a.out, CACHE_NAME)
    cache = {} if a.force else load_cache(cache_path)
    code_fp = code_fingerprint()
    todo = []
    for t in tasks:
        r = None if a.force else cached_row(cache, t, code_fp)
        if r is None: todo.append(t)
        else:
            r = dict(r, secs=0.0, cached=True); rows.append(r); report(r)
    if len(todo) < len(tasks): print(f'{len(tasks) - len(todo)} of {len(tasks)} models up to date (skipped), {len(todo)} to build')
    def done(r):
        rows.append(r); report(r)
        label = r['label']
        if r['ok']: cache[label] = cache_entry(r, code_fp)
        else: cache.pop(label, None)
        save_cache(cache_path, cache)
    if a.jobs > 1 and len(todo) > 1:
        import multiprocessing as mp
        with mp.get_context('fork').Pool(min(a.jobs, len(todo))) as pool:
            for r in pool.imap_unordered(build_one, todo, 1): done(r)
    else:
        for t in todo: done(build_one(t))
    order = {(t[2], t[3]): i for i, t in enumerate(sorted(tasks, key=lambda t: (list(designs.ALL_MODULES).index(t[1]), t[2], t[3])))}
    rows.sort(key=lambda r: order[(r['model'], r['lite'])])
    ok_rows = [r for r in rows if r['ok']]
    failed = [r for r in rows if not r['ok']]
    # cross checks
    cross = []
    for key in ('name', 'bid'):
        vals = [r.get(key) for r in ok_rows]
        d = sorted({v for v in vals if vals.count(v) > 1})
        if d: cross.append(f'duplicate {key}: {d}')
    if any(r.get('bid') is None for r in ok_rows): cross.append('a blueprint has no Id Subtype')
    # stale folders in out/Blueprints that no model writes any more
    keep = {r['name'] for r in ok_rows}
    if not a.only:
        import shutil
        for d in sorted(os.listdir(bp_root)):
            if d.endswith(' Lite') != (lites == (True,)): continue                 # the other variant's folders belong to the other script
            if d not in keep and os.path.isdir(os.path.join(bp_root, d)) and not failed:
                shutil.rmtree(os.path.join(bp_root, d)); print(f'removed stale out/Blueprints/{d}')
    for c in cross: print('CROSS-CHECK FAIL:', c)
    dist = os.path.join(a.out, 'dist_partial' if a.only else 'dist')
    if (not a.only and not a.no_zip) or (a.only and a.zip and not failed):
        os.makedirs(dist, exist_ok=True)
        md = models_md(rows, failed)
        open(os.path.join(dist, 'MODELS_Lite.md' if lites == (True,) else 'MODELS.md'), 'w', encoding='utf-8').write(md + '\n')
        os.makedirs(os.path.join(dist, 'readmes'), exist_ok=True)
        for base, kinds, variants, tmpl, intro in PACKAGES:
            prow = [r for r in ok_rows if r['kind'] in kinds and r['lite'] in variants]
            variants = tuple(v for v in variants if v in lites)              # build_all.py: Full only, build_lite.py: Lite only
            if not variants: continue
            prow = [r for r in prow if r['lite'] in variants]
            if a.only and not prow: continue                                 # a partial build: only the packages that have a model
            variant = 'Lite' if variants[0] else 'Full'
            readme = render_readme(tmpl, intro, variant, prow, kinds)
            open(os.path.join(dist, 'readmes', base + '_README.md'), 'w', encoding='utf-8').write(readme)
            extra = {'MODELS_Lite.md' if lites == (True,) else 'MODELS.md': (md + '\n').encode()} if base.endswith('_All') else {}
            n, sc = make_zip(os.path.join(dist, base + '.zip'), prow, readme, extra)
            print(f'zip {base}.zip: {len(prow)} blueprints, {n} files, scripts {", ".join(sc) or "-"}, {os.path.getsize(os.path.join(dist, base + ".zip")) // 1024} KB')
    print(f'\n{len(ok_rows)} PASS, {len(failed)} FAIL, {len(cross)} cross-check failures, {time.time() - t0:.0f}s total')
    for r in failed:
        print(f'FAIL {r["label"]}:')
        for p in r['problems']: print('   ', p.replace('\n', '\n    '))
    return 1 if (failed or cross or missing == list(designs.ALL_MODULES)) else 0

def report(r):
    if r['ok']:
        took = 'up to date' if r.get('cached') else f'{r["secs"]:.0f}s'
        print(f'PASS {r["label"]:<24} {r["kind"]:<7} {r["blocks"]:>3} blocks {r["mass"]:>8.0f} kg  accel {fmt_acc(r)}  PCU {r["pcu"]:>5}  '
              f'scripts {",".join(s[:-3] for s in r["scripts"]) or "-"}  [{took}]', flush=True)
    else:
        print(f'FAIL {r["label"]}: ' + ' | '.join(p.splitlines()[0] for p in r['problems']), flush=True)

if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
