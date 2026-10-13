#!/usr/bin/env python3
"""Build defs_stats.json: per-block / weapon / ammo / component stats straight from the game data in ref/.

Usage:  python3 -I tools/index_stats.py [repo_root]

Reads (read-only):
  ref/CubeBlocks/*.sbc                 every block definition (<CubeBlocks><Definition>)
  ref/Data/Components.sbc              component mass / volume / MaxIntegrity
  ref/Data/Blueprints.sbc              component ingot cost + assembler time
  ref/Data/Ammos.sbc                   ammo definitions (speed, range, damage, explosion)
  ref/Data/AmmoMagazines.sbc           magazine -> ammo, capacity
  ref/Data/Weapons.sbc                 WeaponDefinition (rate of fire, spread, burst, reload) used by weapon blocks
  ref/Data/Environment.sbc             max speeds
  ref/Data/Configuration.sbc           grid cell sizes
  ref/Data/ResourceDistributionGroups.sbc   power priority groups
Writes defs_stats.json.

Conventions (see _meta.notes in the output): power MW, energy MWh, force N, mass kg, length m.
Every number is copied from a file; fields named derived_* are arithmetic on file values only.
A value that is not in the files is simply absent (never guessed).
"""
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

XSI = '{http://www.w3.org/2001/XMLSchema-instance}type'
ROOT = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REF = os.path.join(ROOT, 'ref')

# tags that are presentation-only (models, sounds, particles, icons, ...) and are dropped from "raw"
NOISE_TAG = re.compile(
    r'(Sound|Effect|Model|Icon|Texture|Particle|Flare|Overlay|Emissive|Description|ScreenAreas|TieredUpdateTimes|'
    r'MountPoints|BuildProgressModels|Components|CriticalComponent|Mirroring|Center|ModelOffset|TargetingGroups|'
    r'DestroyEffect|DamageEffect|Material|Color|ToolbarTooltip|Tooltip|BlockVariants|Skeleton|PrefabName|VoxelPlacement|'
    r'Decal|Thumbnail|AnimationLoop|SubpartMovement|ExcludedBlocks|Flame(Idle|Full)Color|Mesh|IconSymbols|Bone|Pattern)', re.I)


def num(s):
    if s is None:
        return None
    t = s.strip()
    if t == '':
        return None
    if t.lower() in ('true', 'false'):
        return t.lower() == 'true'
    try:
        f = float(t)
        return int(f) if f.is_integer() and 'e' not in t.lower() and '.' not in t else f
    except ValueError:
        return t


def flatten(el, prefix='', out=None):
    """Flatten an element into {dotted.path: scalar}. Presentation tags are skipped."""
    if out is None:
        out = {}
    counts = {}
    for c in el:
        if not isinstance(c.tag, str) or NOISE_TAG.search(c.tag):
            continue
        counts[c.tag] = counts.get(c.tag, 0) + 1
        key = prefix + c.tag
        if counts[c.tag] > 1 or sum(1 for x in el if x.tag == c.tag) > 1:
            key += '[%d]' % (counts[c.tag] - 1)
        for an, av in c.attrib.items():
            if NOISE_TAG.search(an) or an.endswith('}type'):
                continue
            out[key + '@' + an] = num(av)
        if len(c):
            flatten(c, key + '.', out)
        else:
            v = num(c.text)
            if v is not None:
                out[key] = v
    return out


def ident(d):
    i = d.find('Id')
    if i is None:
        return None, None
    t = (i.findtext('TypeId') or i.get('Type') or '').strip()
    s = (i.findtext('SubtypeId') or i.get('Subtype') or '').strip()
    return t, s


def load(path):
    return ET.parse(path).getroot()


# ----------------------------------------------------------------------------- constants
def constants():
    c = {}
    env = load(os.path.join(REF, 'Data', 'Environment.sbc')).find('Definition')
    for k in ('SmallShipMaxSpeed', 'LargeShipMaxSpeed', 'SmallShipMaxAngularSpeed', 'LargeShipMaxAngularSpeed',
              'FloatingObjectLinearDampening'):
        v = env.findtext(k)
        if v is not None:
            c[k] = num(v)
    c['_source_Environment'] = 'Data/Environment.sbc'
    cs = load(os.path.join(REF, 'Data', 'Configuration.sbc')).find('Configuration/CubeSizes')
    c['CubeSizeMeters'] = {'Large': num(cs.get('Large')), 'Small': num(cs.get('Small'))}
    c['_source_CubeSizes'] = 'Data/Configuration.sbc'
    groups = {}
    for g in load(os.path.join(REF, 'Data', 'ResourceDistributionGroups.sbc')).iter('DistributionGroup'):
        groups[g.findtext('Id/SubtypeId')] = dict(is_source=num(g.findtext('IsSource')),
                                                  priority=num(g.findtext('Priority')),
                                                  adaptible=num(g.findtext('IsAdaptible')))
    c['PowerDistributionGroups'] = groups
    c['_source_PowerDistributionGroups'] = 'Data/ResourceDistributionGroups.sbc'
    return c


# ----------------------------------------------------------------------------- components
def components():
    comps = {}
    for d in load(os.path.join(REF, 'Data', 'Components.sbc')).iter('Component'):
        t, s = ident(d)
        if not s:
            continue
        comps[s] = dict(mass=num(d.findtext('Mass')), volume=num(d.findtext('Volume')),
                        max_integrity=num(d.findtext('MaxIntegrity')), item_health=num(d.findtext('Health')),
                        drop_probability=num(d.findtext('DropProbability')))
    # blueprints: component recipes (assembler)
    done = set()
    for b in load(os.path.join(REF, 'Data', 'Blueprints.sbc')).iter('Blueprint'):
        res = b.find('Result')
        if res is None or res.get('TypeId') != 'Component' or res.get('SubtypeId') not in comps:
            continue
        cs = res.get('SubtypeId')
        primary = num(b.findtext('IsPrimary')) is True
        if cs in done and not primary:
            continue
        ing = {}
        for it in b.findall('Prerequisites/Item'):
            ing[it.get('SubtypeId')] = ing.get(it.get('SubtypeId'), 0) + num(it.get('Amount'))
        comps[cs].update(ingots=ing, assembler_seconds=num(b.findtext('BaseProductionTimeInSeconds')),
                         amount_per_craft=num(res.get('Amount')), blueprint=b.findtext('Id/SubtypeId'))
        done.add(cs)
    return comps


# ----------------------------------------------------------------------------- ammo / magazines / weapons
def ammo():
    out = {}
    for a in load(os.path.join(REF, 'Data', 'Ammos.sbc')).iter('Ammo'):
        t, s = ident(a)
        if not s:
            continue
        e = dict(kind=(a.get(XSI) or '').replace('MyObjectBuilder_', '').replace('AmmoDefinition', ''))
        e.update(flatten(a))
        e.pop('Id.TypeId', None)
        e.pop('Id.SubtypeId', None)
        # tidy common names
        out[s] = e
    return out


def magazines():
    out = {}
    for m in load(os.path.join(REF, 'Data', 'AmmoMagazines.sbc')).iter('AmmoMagazine'):
        t, s = ident(m)
        if not s:
            continue
        ad = m.find('AmmoDefinitionId')
        out[s] = dict(capacity=num(m.findtext('Capacity')), mass=num(m.findtext('Mass')),
                      volume=num(m.findtext('Volume')), ammo=ad.get('Subtype') if ad is not None else None)
    return out


def weapons(ammos, mags):
    out = {}
    for w in load(os.path.join(REF, 'Data', 'Weapons.sbc')).iter('Weapon'):
        t, s = ident(w)
        if not s:
            continue
        e = {}
        for tag in ('ProjectileAmmoData', 'MissileAmmoData'):
            n = w.find(tag)
            if n is not None:
                e['ammo_data_kind'] = tag
                e['rate_of_fire_rpm'] = num(n.get('RateOfFire'))
                e['shots_in_burst'] = num(n.get('ShotsInBurst'))
        for k, tag in (('deviate_shot_angle_deg', 'DeviateShotAngle'), ('deviate_shot_angle_aiming_deg', 'DeviateShotAngleAiming'),
                       ('shot_delay_ms', 'ShotDelay'), ('reload_time_ms', 'ReloadTime'),
                       ('release_time_after_fire_ms', 'ReleaseTimeAfterFire'), ('damage_multiplier', 'DamageMultiplier'),
                       ('range_multiplier', 'RangeMultiplier'), ('support_reloading', 'SupportReloading')):
            v = w.findtext(tag)
            if v is not None:
                e[k] = num(v)
        ms = [m.get('Subtype') for m in w.findall('AmmoMagazines/AmmoMagazine')]
        e['magazines'] = ms
        if 'rate_of_fire_rpm' in e and e['rate_of_fire_rpm']:
            e['derived_shots_per_second'] = round(e['rate_of_fire_rpm'] / 60.0, 4)
        # ammo behaviour for each magazine
        per = {}
        for m in ms:
            mg = mags.get(m)
            if not mg:
                continue
            am = ammos.get(mg['ammo'], {})
            d = dict(magazine_capacity=mg['capacity'], magazine_mass=mg['mass'], magazine_volume=mg['volume'],
                     ammo=mg['ammo'], speed=am.get('BasicProperties.DesiredSpeed'),
                     max_range=am.get('BasicProperties.MaxTrajectory'),
                     explosive_damage_multiplier=am.get('BasicProperties.ExplosiveDamageMultiplier'))
            if am.get('kind') == 'Projectile':
                d.update(damage_blocks=am.get('ProjectileProperties.ProjectileMassDamage'),
                         damage_characters=am.get('ProjectileProperties.ProjectileHealthDamage'))
                if e.get('derived_shots_per_second') and d['damage_blocks'] is not None:
                    d['derived_block_dps'] = round(d['damage_blocks'] * e['derived_shots_per_second'] *
                                                   e.get('damage_multiplier', 1), 2)
            elif am.get('kind') == 'Missile':
                for k, src in (('explosion_radius', 'MissileProperties.MissileExplosionRadius'),
                               ('explosion_damage', 'MissileProperties.MissileExplosionDamage'),
                               ('missile_mass', 'MissileProperties.MissileMass'),
                               ('initial_speed', 'MissileProperties.MissileInitialSpeed'),
                               ('acceleration', 'MissileProperties.MissileAcceleration'),
                               ('health_pool', 'MissileProperties.MissileHealthPool'),
                               ('ricochet_damage', 'MissileProperties.MissileRicochetDamage'),
                               ('gravity_enabled', 'MissileProperties.MissileGravityEnabled')):
                    if src in am:
                        d[k] = am[src]
            per[m] = d
        e['ammo_detail'] = per
        out[s] = e
    return out


# ----------------------------------------------------------------------------- blocks
# promoted, type-specific fields: (output key, raw key)
PROMOTE = {
    'Thrust': [('force_n', 'ForceMagnitude'), ('max_power_mw', 'MaxPowerConsumption'), ('min_power_mw', 'MinPowerConsumption'),
               ('thruster_type', 'ThrusterType'), ('flame_length_scale', 'FlameLengthScale'),
               ('flame_damage_length_scale', 'FlameDamageLengthScale'), ('slowdown_factor', 'SlowdownFactor'),
               ('min_planetary_influence', 'MinPlanetaryInfluence'), ('max_planetary_influence', 'MaxPlanetaryInfluence'),
               ('effectiveness_at_min_influence', 'EffectivenessAtMinInfluence'),
               ('effectiveness_at_max_influence', 'EffectivenessAtMaxInfluence'),
               ('needs_atmosphere', 'NeedsAtmosphereForInfluence'), ('flame_damage', 'FlameDamage'),
               ('fuel', 'FuelConverter.FuelId.SubtypeId')],
    'Gyro': [('force_n', 'ForceMagnitude'), ('power_mw', 'RequiredPowerInput')],
    'BatteryBlock': [('max_stored_mwh', 'MaxStoredPower'), ('max_output_mw', 'MaxPowerOutput'),
                     ('max_input_mw', 'RequiredPowerInput'), ('initial_charge_ratio', 'InitialStoredPowerRatio'),
                     ('recharge_multiplier', 'RechargeMultiplier'), ('adaptible_input', 'AdaptibleInput')],
    'CameraBlock': [('raycast_cone_limit_deg', 'RaycastConeLimit'), ('raycast_distance_limit', 'RaycastDistanceLimit'),
                    ('raycast_time_multiplier', 'RaycastTimeMultiplier'), ('min_fov', 'MinFov'), ('max_fov', 'MaxFov'),
                    ('power_mw', 'RequiredPowerInput'), ('charging_power_mw', 'RequiredChargingInput')],
    'SensorBlock': [('max_range_m', 'MaxRange'), ('power_mw', 'RequiredPowerInput')],
    'RadioAntenna': [('max_range_m', 'MaxBroadcastRadius'), ('power_mw', 'RequiredPowerInput')],
    'LaserAntenna': [('max_range_m', 'MaxRange'), ('power_lasing_mw', 'PowerInputLasing'), ('power_turning_mw', 'PowerInputTurning'),
                     ('power_idle_mw', 'PowerInputIdle'), ('rotation_rate', 'RotationRate')],
    'MergeBlock': [('strength', 'Strength'), ('deformation_ratio', 'DeformationRatio')],
    'ShipWelder': [('radius_m', 'SensorRadius'), ('sensor_offset', 'SensorOffset')],
    'ShipGrinder': [('radius_m', 'SensorRadius'), ('sensor_offset', 'SensorOffset')],
    'Projector': [('power_mw', 'RequiredPowerInput')],
    'TimerBlock': [],
    'EventController': [('power_mw', 'RequiredPowerInput')],
    'OffensiveCombat': [('power_mw', 'RequiredPowerInput')],
    'DefensiveCombat': [('power_mw', 'RequiredPowerInput')],
    'FlightMovement': [('power_mw', 'RequiredPowerInput')],
    'BasicMission': [('power_mw', 'RequiredPowerInput')],
    'ProgrammableBlock': [('power_mw', 'RequiredPowerInput')],
    'RemoteControl': [('power_mw', 'RequiredPowerInput')],
    'Warhead': [('explosion_radius_m', 'ExplosionRadius'), ('explosion_damage', 'WarheadExplosionDamage')],
    'Reactor': [('max_output_mw', 'MaxPowerOutput')],
    'SolarPanel': [('max_output_mw', 'MaxPowerOutput')],
    'Decoy': [('priority_modifier', 'PriorityModifier')],
    'TurretControl': [('max_range_m', 'MaxRangeMeters'), ('power_idle_mw', 'PowerInputIdle')],
    'Cockpit': [('power_mw', 'RequiredPowerInput')],
    'GasTank': [('capacity', 'Capacity')],
    'JumpDrive': [('power_mw', 'RequiredPowerInput'), ('max_jump_distance_m', 'MaxJumpDistance')],
}


def kind_of(xsi):
    k = (xsi or 'CubeBlock').replace('MyObjectBuilder_', '').replace('Definition', '')
    return k.replace('BlockDefinition', '')


def kind_key(xsi):
    """Map xsi type to a PROMOTE key."""
    k = (xsi or '').replace('MyObjectBuilder_', '').replace('Definition', '')
    for cand in ('CombatBlock',):
        k = k.replace(cand, 'Combat')
    k = {'BatteryBlock': 'BatteryBlock', 'SensorBlock': 'SensorBlock', 'CameraBlock': 'CameraBlock',
         'EventControllerBlock': 'EventController', 'TimerBlock': 'TimerBlock', 'OffensiveCombatBlock': 'OffensiveCombat',
         'DefensiveCombatBlock': 'DefensiveCombat', 'FlightMovementBlock': 'FlightMovement',
         'BasicMissionBlock': 'BasicMission', 'TurretControlBlock': 'TurretControl', 'WeaponBlock': 'WeaponBlock'}.get(k, k)
    return k


def blocks(comps, wdefs, consts):
    out = {}
    cube = consts['CubeSizeMeters']
    for f in sorted(glob.glob(os.path.join(REF, 'CubeBlocks', '*.sbc'))):
        try:
            root = load(f)
        except ET.ParseError as e:
            print('skip', f, e, file=sys.stderr)
            continue
        cb = root.find('CubeBlocks')
        if cb is None:
            continue
        for d in cb.findall('Definition'):
            t, s = ident(d)
            if not t:
                continue
            xsi = d.get(XSI)
            grid = d.findtext('CubeSize')
            sz = d.find('Size')
            def _dim(a):
                v = sz.get(a)
                if v is None:
                    v = sz.get(a.upper())
                if v is None:
                    v = sz.findtext(a) or sz.findtext(a.upper())
                return int(float(v)) if v is not None else 1
            size = [_dim(a) for a in 'xyz'] if sz is not None else [1, 1, 1]
            cs = cube.get(grid, 0)
            e = dict(type=t, subtype=s, xsi=xsi, kind=kind_of(xsi), grid=grid, size_cells=size,
                     size_m=[round(x * cs, 3) for x in size], display=d.findtext('DisplayName'),
                     dlc=[x.text for x in d.findall('DLC')], public=num(d.findtext('Public')),
                     gui_visible=num(d.findtext('GuiVisible')), file=os.path.basename(f))
            e['pcu'] = num(d.findtext('PCU'))
            e['build_time_s'] = num(d.findtext('BuildTimeSeconds'))
            # components -> mass, integrity, critical
            cl, mass, integ = [], 0.0, 0.0
            ccn = d.find('CriticalComponent')
            cidx = num(ccn.get('Index')) if ccn is not None else None
            csub = ccn.get('Subtype') if ccn is not None else None
            seen_crit, crit_integ_before, crit_found, crit_integ_through = 0, None, False, None
            missing = []
            for c in d.findall('Components/Component'):
                sub, cnt = c.get('Subtype'), num(c.get('Count'))
                cd = comps.get(sub)
                if cd is None:
                    missing.append(sub)
                    continue
                if (not crit_found) and csub == sub:
                    if seen_crit == cidx:
                        crit_integ_before = integ
                        crit_found = True
                        crit_integ_through = integ + cnt * cd['max_integrity']   # game: criticalIntegrity = integrity after adding it, minus 1
                    seen_crit += 1
                cl.append(dict(subtype=sub, count=cnt, unit_mass=cd['mass'], unit_integrity=cd['max_integrity']))
                mass += cnt * cd['mass']
                integ += cnt * cd['max_integrity']
            e['components'] = cl
            if missing:
                e['components_missing_from_Components_sbc'] = missing
            e['mass_kg'] = round(mass, 4)
            ing = {}
            asm = 0.0
            for c in cl:
                cd = comps[c['subtype']]
                for k, v in (cd.get('ingots') or {}).items():
                    ing[k] = round(ing.get(k, 0) + v * c['count'] / (cd.get('amount_per_craft') or 1), 4)
                asm += (cd.get('assembler_seconds') or 0) * c['count'] / (cd.get('amount_per_craft') or 1)
            e['ingot_cost'] = ing
            e['derived_assembler_seconds_at_1x'] = round(asm, 3)
            explicit_mi = num(d.findtext('MaxIntegrity'))
            e['integrity'] = dict(
                sum_of_components=round(integ, 3),
                explicit_MaxIntegrity_override=explicit_mi,
                critical_component=csub, critical_index=cidx,
                integrity_before_critical_component=crit_integ_before,
                critical_integrity=(round(crit_integ_through - 1, 3) if crit_integ_through is not None else None),
                critical_ratio_approx=(round(crit_integ_before / integ, 4) if crit_integ_before is not None and integ else None),
                deformation_ratio=num(d.findtext('DeformationRatio')),
                general_damage_multiplier=num(d.findtext('GeneralDamageMultiplier')),
                damage_multiplier_to_block=num(d.findtext('DamageMultiplier')))
            raw = flatten(d)
            for k in ('Id.TypeId', 'Id.SubtypeId', 'CubeSize', 'DisplayName', 'DLC', 'PCU', 'BuildTimeSeconds', 'Public',
                      'GuiVisible', 'Size@x', 'Size@y', 'Size@z'):
                raw.pop(k, None)
            e['raw'] = raw
            # promoted stats
            st = {}
            for kk in (kind_key(xsi),) + (('Thrust',) if 'Thrust' in (xsi or '') else ()):
                for okey, rkey in PROMOTE.get(kk, []):
                    if rkey in raw:
                        st[okey] = raw[rkey]
            # sink groups / power
            for rk, ok in (('ResourceSinkGroup', 'power_sink_group'), ('ResourceSourceGroup', 'power_source_group'),
                           ('RequiredPowerInput', 'required_power_mw'), ('MaxPowerOutput', 'max_power_output_mw')):
                if rk in raw:
                    st[ok] = raw[rk]
            # thrust derived
            if 'force_n' in st and e['mass_kg'] and kind_key(xsi) == 'Thrust':
                st['derived_force_per_kg_block'] = round(st['force_n'] / e['mass_kg'], 2)
            # weapons: join Weapons.sbc
            wid = raw.get('WeaponDefinitionId@Subtype')
            if wid:
                st['weapon_definition'] = wid
                wd = wdefs.get(wid)
                if wd:
                    st['weapon'] = wd
                st['inventory_max_volume'] = raw.get('InventoryMaxVolume')
                st['inventory_fill_factor_min'] = raw.get('InventoryFillFactorMin')
            e['stats'] = st
            key = '%s/%s' % (t, s)
            if key in out:
                key += '#' + os.path.basename(f)
            out[key] = e
    return out


# ----------------------------------------------------------------------------- docs/stats.md
def _f(v):
    if v is None:
        return '-'
    if isinstance(v, float):
        return ('%.6g' % v)
    return str(v)


def write_md(res, path):
    b, wd, am, mg, K = res['blocks'], res['weapons'], res['ammo'], res['magazines'], res['constants']
    L = []
    w = L.append
    w('# Block and weapon stats (generated)')
    w('')
    w('Generated by `tools/index_stats.py` from the game data in `ref/` (do not edit by hand; rerun the script).')
    w('Full data: `defs_stats.json`. Every number below is copied from the named file or is arithmetic on file values')
    w('(columns/rows named "derived"). "-" means the value is not present in the files. DLC blocks are excluded here.')
    w('Units: power MW, energy MWh, force N, mass kg, length m.')
    w('')
    w('## Sim constants')
    w('')
    w('| Constant | Value | File |')
    w('|---|---|---|')
    w('| SmallShipMaxSpeed | %s m/s | Data/Environment.sbc |' % K['SmallShipMaxSpeed'])
    w('| LargeShipMaxSpeed | %s m/s | Data/Environment.sbc |' % K['LargeShipMaxSpeed'])
    w('| SmallShipMaxAngularSpeed | %s (unit not stated in file; engine uses deg/s) | Data/Environment.sbc |' % K['SmallShipMaxAngularSpeed'])
    w('| LargeShipMaxAngularSpeed | %s | Data/Environment.sbc |' % K['LargeShipMaxAngularSpeed'])
    w('| Cube size small / large | %s m / %s m | Data/Configuration.sbc |' % (K['CubeSizeMeters']['Small'], K['CubeSizeMeters']['Large']))
    w('| Power priority (lower = served first) | ' + ', '.join('%s=%s' % (k, v['priority']) for k, v in sorted(
        K['PowerDistributionGroups'].items(), key=lambda kv: kv[1]['priority'])) + ' | Data/ResourceDistributionGroups.sbc |')
    w('| Damage multipliers (BlockDamageModifier, WeaponsEnabled, ...) | not in the data files (world/session settings) | - |')
    w('| Gravity, planet effects, atmosphere drag | not in the files read here | - |')
    w('')

    def row(k, extra=''):
        e = b[k]
        i = e['integrity']
        crit = i['integrity_before_critical_component']
        return '| `%s` | %s | %s | %s | %s | %s | %s%s | %s |' % (
            k, 'x'.join(str(x) for x in e['size_cells']), _f(e['mass_kg']), _f(e['pcu']), _f(e['build_time_s']),
            _f(i['sum_of_components']), _f(i['explicit_MaxIntegrity_override']) if i['explicit_MaxIntegrity_override'] else '',
            '' if not i['explicit_MaxIntegrity_override'] else ' (override)', extra)

    hdr = '| Block (Type/Subtype) | Cells | Mass | PCU | Build s | Integrity | Notes |'
    sep = '|---|---|---|---|---|---|---|'

    def table(title, keys, notes, src):
        keys = [k for k in keys if k in b and not b[k]['dlc']]
        if not keys:
            return
        w('## ' + title)
        w('')
        w('Source: ' + src)
        w('')
        w(hdr)
        w(sep)
        for k in keys:
            e = b[k]
            i = e['integrity']
            w('| `%s` | %s | %s | %s | %s | %s | %s |' % (
                k, 'x'.join(str(x) for x in e['size_cells']), _f(e['mass_kg']), _f(e['pcu']), _f(e['build_time_s']),
                _f(i['explicit_MaxIntegrity_override'] if i['explicit_MaxIntegrity_override'] else i['sum_of_components']),
                notes(e)))
        w('')

    def small(kind, only_visible=False):
        return [k for k, e in b.items() if e['grid'] == 'Small' and e['kind'] == kind and not e['dlc'] and
                (not only_visible or e['gui_visible'] is not False)]

    def stat_str(e, keys):
        st = e['stats']
        return '; '.join('%s=%s' % (k, _f(st[k])) for k in keys if k in st) or '-'

    table('Structure (small grid, non-DLC)',
          ['CubeBlock/SmallBlockArmorBlock', 'CubeBlock/SmallHeavyBlockArmorBlock', 'CubeBlock/SmallBlockArmorSlope',
           'CubeBlock/SmallHeavyBlockArmorSlope', 'CubeBlock/SmallBlockArmorCorner', 'CubeBlock/SmallHeavyBlockArmorCorner'],
          lambda e: 'deformation_ratio=%s' % _f(e['integrity']['deformation_ratio']),
          'ref/CubeBlocks/CubeBlocks_Armor.sbc + ref/Data/Components.sbc (mass/integrity = sum of components)')

    table('Thrusters (small grid)', small('Thrust'),
          lambda e: stat_str(e, ['thruster_type', 'force_n', 'max_power_mw', 'min_power_mw', 'flame_length_scale',
                                 'flame_damage_length_scale', 'flame_damage', 'derived_force_per_kg_block']),
          'ref/CubeBlocks/CubeBlocks_Thrusters.sbc (ForceMagnitude N, MaxPowerConsumption MW, Flame*Scale)')
    table('Gyros', small('Gyro'), lambda e: stat_str(e, ['force_n', 'power_mw']),
          'ref/CubeBlocks/CubeBlocks_Control.sbc (ForceMagnitude N, RequiredPowerInput MW)')
    table('Power', small('BatteryBlock') + small('Reactor') + small('SolarPanel'),
          lambda e: stat_str(e, ['max_stored_mwh', 'max_output_mw', 'max_input_mw', 'initial_charge_ratio', 'recharge_multiplier']),
          'ref/CubeBlocks/CubeBlocks_Energy.sbc')
    table('Sensing, control, comms', small('CameraBlock') + small('SensorBlock') + small('RadioAntenna') +
          small('RemoteControl') + small('ProgrammableBlock') + small('TurretControlBlock') + small('Projector') +
          small('TimerBlock') + small('EventControllerBlock') + small('OffensiveCombatBlock') +
          small('DefensiveCombatBlock') + small('FlightMovementBlock') + small('BasicMissionBlock') + small('Decoy'),
          lambda e: stat_str(e, ['power_mw', 'power_idle_mw', 'max_range_m', 'raycast_cone_limit_deg', 'raycast_distance_limit',
                                 'raycast_time_multiplier', 'min_fov', 'max_fov', 'power_sink_group', 'priority_modifier']),
          'ref/CubeBlocks/CubeBlocks_Automation.sbc, _Control.sbc, _Communications.sbc, _Utility.sbc, _Warfare/_Weapons as listed in the `file` field of defs_stats.json')
    table('Tools, merge, warhead', small('MergeBlock') + small('ShipWelder') + small('ShipGrinder') + small('Warhead') +
          small('LandingGear'),
          lambda e: stat_str(e, ['strength', 'deformation_ratio', 'radius_m', 'explosion_radius_m', 'explosion_damage']),
          'ref/CubeBlocks/CubeBlocks_Utility.sbc, _Tools.sbc, _Warfare1.sbc (merge Strength, SensorRadius, Warhead*)')

    # weapons
    wk = [k for k, e in b.items() if e['grid'] == 'Small' and not e['dlc'] and 'weapon' in e['stats']]
    wk += [k for k, e in b.items() if e['grid'] == 'Large' and not e['dlc'] and e['stats'].get('weapon_definition') == 'LargeRailgun']
    table('Weapon blocks (non-DLC; large railgun shown for comparison)', wk,
          lambda e: 'weapon=%s; sink=%s' % (e['stats'].get('weapon_definition'), e['stats'].get('power_sink_group')),
          'ref/CubeBlocks/CubeBlocks_Weapons.sbc')
    w('## Weapon fire data (Data/Weapons.sbc + Ammos.sbc + AmmoMagazines.sbc)')
    w('')
    w('| Weapon def | RoF rpm | shots/s (derived) | burst | spread deg | shot delay ms | reload ms | magazine (cap) | speed m/s | range m | dmg/hit blocks | block DPS (derived) | explosion r / dmg | health pool |')
    w('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    used = sorted({e['stats']['weapon_definition'] for k, e in b.items() if not e['dlc'] and 'weapon' in e['stats'] and
                   (e['grid'] == 'Small' or e['stats']['weapon_definition'] == 'LargeRailgun') and 'Flare' not in e['stats']['weapon_definition']})
    for name in used:
        x = wd[name]
        for m, d in x['ammo_detail'].items():
            w('| %s | %s | %s | %s | %s | %s | %s | %s (%s) | %s | %s | %s | %s | %s / %s | %s |' % (
                name, _f(x.get('rate_of_fire_rpm')), _f(x.get('derived_shots_per_second')), _f(x.get('shots_in_burst')),
                _f(x.get('deviate_shot_angle_deg')), _f(x.get('shot_delay_ms')), _f(x.get('reload_time_ms')), m,
                _f(d.get('magazine_capacity')), _f(d.get('speed')), _f(d.get('max_range')), _f(d.get('damage_blocks')),
                _f(d.get('derived_block_dps')), _f(d.get('explosion_radius')), _f(d.get('explosion_damage')),
                _f(d.get('health_pool'))))
    w('')
    w('## Components (Data/Components.sbc, Data/Blueprints.sbc)')
    w('')
    w('| Component | Mass | Integrity | Ingots per item | Assembler s |')
    w('|---|---|---|---|---|')
    for k, c in sorted(res['components'].items()):
        w('| %s | %s | %s | %s | %s |' % (k, _f(c['mass']), _f(c['max_integrity']),
                                           ', '.join('%s %s' % (i, _f(v)) for i, v in (c.get('ingots') or {}).items()) or '-',
                                           _f(c.get('assembler_seconds'))))
    w('')
    w('## Not in the files (do not assume)')
    w('')
    w('- Small radio antenna range/power: the SmallBlockRadioAntenna definition has no MaxBroadcastRadius or power field.')
    w('- Small camera RaycastConeLimit / RaycastDistanceLimit / RaycastTimeMultiplier: absent for the vanilla small camera '
      '(present only on DLC/other cameras).')
    w('- Weapon power draw (gatling/autocannon/rocket/railgun): no field in block defs, only the sink group name.')
    w('- Programmable block power draw (no power field in its definition); timer power draw (none in the small timer definition).')
    w('- How the engine converts MissileHealthPool (railgun) into block damage, and rocket explosion falloff.')
    w('- World settings (damage modifiers, inventory multipliers, welding/grinding speed) and gravity.')
    w('')
    w('## Discoveries')
    w('')

    def ad(wn, mag):
        return wd[wn]['ammo_detail'][mag]
    g, ac, ro, rg = ad('GatlingGun', 'NATO_25x184mm'), ad('Autocannon', 'AutocannonClip'), ad('SmallMissileLauncher', 'Missile200mm'), ad('SmallRailgun', 'SmallRailgunAmmo')
    lt = b['CubeBlock/SmallBlockArmorBlock']['integrity']['sum_of_components']
    hv = b['CubeBlock/SmallHeavyBlockArmorBlock']['integrity']['sum_of_components']
    D = []
    D.append('- Gatling (small): %s rpm = %s shots/s, %s damage per hit to blocks (ammo LargeCaliber; %s m/s, %s m range), spread %s deg. '
             'Derived block DPS %s; a light armor block (%s integrity) dies in %d hits, heavy (%s) in %d.'
             % (wd['GatlingGun']['rate_of_fire_rpm'], wd['GatlingGun']['derived_shots_per_second'], g['damage_blocks'], g['speed'],
                g['max_range'], wd['GatlingGun']['deviate_shot_angle_deg'], g['derived_block_dps'], lt, -(-lt // g['damage_blocks']),
                hv, -(-hv // g['damage_blocks'])))
    D.append('- Autocannon (small): %s rpm = %s shots/s, %s damage per hit (AutocannonShell; %s m/s, %s m), mag %s rounds, derived DPS %s. '
             'On paper it out-damages the gatling per second; one shell exceeds a light armor block integrity several times over; 16-round mags of %s kg.'
             % (wd['Autocannon']['rate_of_fire_rpm'], wd['Autocannon']['derived_shots_per_second'], ac['damage_blocks'], ac['speed'],
                ac['max_range'], ac['magazine_capacity'], ac['derived_block_dps'], ac['magazine_mass']))
    D.append('- Gatling magazine NATO_25x184mm holds %s rounds (%s kg, %s L); at %s shots/s that is %.1f s of fire. Autocannon clip: %s rounds = %.1f s.'
             % (g['magazine_capacity'], g['magazine_mass'], g['magazine_volume'], wd['GatlingGun']['derived_shots_per_second'],
                g['magazine_capacity'] / wd['GatlingGun']['derived_shots_per_second'], ac['magazine_capacity'],
                ac['magazine_capacity'] / wd['Autocannon']['derived_shots_per_second']))
    D.append('- Rocket (small launcher): %s/s, explosion damage %s, radius %s m, initial speed %s, acceleration %s, DesiredSpeed %s, range %s m; spread %s deg.'
             % (wd['SmallMissileLauncher']['derived_shots_per_second'], ro['explosion_damage'], ro['explosion_radius'], ro['initial_speed'],
                ro['acceleration'], ro['speed'], ro['max_range'], wd['SmallMissileLauncher']['deviate_shot_angle_deg']))
    D.append('- Small railgun: slug %s m/s, range %s m, NO explosion (radius %s, damage %s), MissileHealthPool %s (damage budget), shot delay %s ms, reload %s ms, '
             'spread %s deg. Large railgun: %s m/s, %s m, pool %s. The block is %s kg and %s PCU %s.'
             % (rg['speed'], rg['max_range'], rg['explosion_radius'], rg['explosion_damage'], rg['health_pool'],
                wd['SmallRailgun']['shot_delay_ms'], wd['SmallRailgun']['reload_time_ms'], wd['SmallRailgun']['deviate_shot_angle_deg'],
                ad('LargeRailgun', 'LargeRailgunAmmo')['speed'], ad('LargeRailgun', 'LargeRailgunAmmo')['max_range'],
                ad('LargeRailgun', 'LargeRailgunAmmo')['health_pool'], b['SmallMissileLauncherReload/SmallRailgun']['mass_kg'],
                b['SmallMissileLauncherReload/SmallRailgun']['pcu'], '(sink group DefenseAdative, power draw not in files)'))
    wh = b['Warhead/SmallWarhead']['stats']
    D.append('- Small warhead: %s damage, radius %s m. Light armor block integrity %s, heavy %s.' % (wh['explosion_damage'], wh['explosion_radius_m'], lt, hv))
    D.append('- Ranges are capped by ammo MaxTrajectory (gatling/autocannon/rocket 800 m, small railgun 1400 m); the small ship speed cap is %s m/s.'
             % K['SmallShipMaxSpeed'])
    sm, mi = b['MergeBlock/SmallShipMergeBlock']['stats'], b['MergeBlock/SmallShipSmallMergeBlock']['stats']
    D.append('- Merge strength: SmallShipMergeBlock %s, SmallShipSmallMergeBlock %s (Strength field, unit not stated in the file). Welder/grinder radius %s m.'
             % (sm['strength'], mi['strength'], b['ShipWelder/SmallShipWelder']['stats']['radius_m']))
    th, gy, bt = b['Thrust/SmallBlockSmallThrust'], b['Gyro/SmallBlockGyro'], b['BatteryBlock/SmallBlockSmallBatteryBlock']
    D.append('- Small ion thruster %s N for %s MW, block mass %s kg; small gyro %s N*m torque (ForceMagnitude) at %s MW; small battery %s MWh, %s MW out, %s kg.'
             % (th['stats']['force_n'], th['stats']['max_power_mw'], th['mass_kg'], gy['stats']['force_n'], gy['stats']['power_mw'],
                bt['stats']['max_stored_mwh'], bt['stats']['max_output_mw'], bt['mass_kg']))
    D.append('- Block mass here is component mass only; magazines/cargo carried in inventories add mass in game (magazine masses are listed above).')
    D.append('- The engine\'s own integrity/mass formulas are VERIFIED from public Keen source (see `_meta.notes` in defs_stats.json); '
             'RateOfFire = rounds per minute and DeviateShotAngle = degrees are VERIFIED too. The damage-per-hit-to-block interpretation of '
             'ProjectileMassDamage and the railgun HealthPool semantics are INFERRED.')
    s_ = '\n'.join(D)
    w(s_)
    with open(path, 'w') as fh:
        fh.write('\n'.join(L) + '\n')


def main():
    consts = constants()
    comps = components()
    am = ammo()
    mg = magazines()
    wd = weapons(am, mg)
    bl = blocks(comps, wd, consts)
    meta = dict(
        generator='tools/index_stats.py',
        sources=['ref/CubeBlocks/*.sbc', 'ref/Data/Components.sbc', 'ref/Data/Blueprints.sbc', 'ref/Data/Ammos.sbc',
                 'ref/Data/AmmoMagazines.sbc', 'ref/Data/Weapons.sbc', 'ref/Data/Environment.sbc',
                 'ref/Data/Configuration.sbc', 'ref/Data/ResourceDistributionGroups.sbc'],
        counts=dict(blocks=len(bl), blocks_nonDLC=sum(1 for b in bl.values() if not b['dlc']),
                    blocks_DLC=sum(1 for b in bl.values() if b['dlc']), components=len(comps), ammo=len(am),
                    magazines=len(mg), weapon_definitions=len(wd)),
        notes=[
            'Units as stored in the files: power MW, energy MWh, thruster/gyro force N, mass kg, lengths m.',
            'mass_kg = sum(count*component Mass). VERIFIED against public KeenSoftwareHouse MyCubeBlockDefinition.Init '
            '(mass += Count*Definition.Mass). Block inventories and attached cargo are not included.',
            'integrity.sum_of_components = sum(count*component MaxIntegrity). VERIFIED (integrity += Count*MaxIntegrity; '
            'MaxIntegrity = integrity). An explicit <MaxIntegrity> in a block def overrides it (explicit_MaxIntegrity_override).',
            'critical integrity: engine uses the cumulative integrity at the critical component (CriticalIntegrityRatio = '
            'criticalIntegrity / MaxIntegrity). integrity_before_critical_component is the sum of the components listed '
            'BEFORE the critical one (kept for reference); critical_integrity = cumulative integrity through the critical component entry minus 1 '
            '(game: criticalIntegrity = integrity - 1 after adding the critical component), the value the sim uses; a block is functional above it.',
            'Weapons.sbc RateOfFire is rounds per minute and DeviateShotAngle is degrees converted to radians on load. VERIFIED '
            'against public MyWeaponDefinition.cs. ReloadTime/ShotDelay are milliseconds (INFERRED; the engine does not convert them).',
            'derived_* fields are arithmetic on file values only. derived_block_dps = ProjectileMassDamage * shots/s * weapon '
            'DamageMultiplier (INFERRED that ProjectileMassDamage is the per-hit damage dealt to blocks).',
            'Missile/railgun ammo: MissileExplosionDamage/Radius are explosion values; railgun slugs have 0 explosion damage '
            'and carry MissileHealthPool (the slug damage budget). How the engine converts HealthPool to block damage is NOT in the files.',
            'World/session multipliers (BlockDamageModifier, WeaponsEnabled, ...) are session settings, not game data files: absent here.',
            'Key order inside blocks[]: type/subtype matches tools/index_defs.py keys (defs.json); duplicates get #file suffix.',
        ])
    res = dict(_meta=meta, constants=consts, components=comps, ammo=am, magazines=mg, weapons=wd, blocks=bl)
    outp = os.path.join(ROOT, 'defs_stats.json')
    with open(outp, 'w') as fh:
        json.dump(res, fh, indent=1, sort_keys=False)
    os.makedirs(os.path.join(ROOT, 'docs'), exist_ok=True)
    write_md(res, os.path.join(ROOT, 'docs', 'stats.md'))
    print('wrote', outp, os.path.getsize(outp), 'bytes')
    print(json.dumps(meta['counts']))


if __name__ == '__main__':
    main()
