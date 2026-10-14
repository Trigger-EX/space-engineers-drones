#!/usr/bin/env python3
"""Power for the railgun recharge on the battery-only railgun drones: Longbow S, Longbow L (+ Seeker), Mantis (docs/design/railgun_power.md).
python3 -I tools/tests/test_rail_power.py   (unittest; no third-party packages)

Owner's brief: the railguns always have sufficient power, and thrust is added as required to keep the same manoeuvrability.  Checked per model, on the
blueprint the build writes (support PBs added):
  * peak: reactors + batteries >= 1.05 x (every railgun recharging + on each axis the larger of its two opposite thruster groups at full + the small loads);
  * sustained: the reactors alone >= the railgun recharge draw, so a flat battery never stops the rails; every reactor carries uranium;
  * acceleration with the pre-loaded items aboard, on all six axes, not below the figure before the change;
  * gyro turn rate (pitch / yaw / roll) not below the figure before the change."""
import os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
for p in (TOOLS, os.path.join(TOOLS, 'designs')):
    if p not in sys.path:
        sys.path.insert(0, p)
import _common as C                                                # noqa: E402
import core                                                        # noqa: E402
import extra                                                       # noqa: E402
import pods                                                        # noqa: E402

# before the change (2026-10-14, battery-only): acceleration m/s2 (no items aboard) and gyro turn rate rad/s2 (pitch, yaw, roll), from C.thrust_by_dir / C.turn_rates
BEFORE = {
    'Longbow S':        (dict(Forward=4.5567, Backward=4.5567, Up=4.5567, Down=4.5567, Left=4.5567, Right=4.5567), (38.9, 47.46, 71.48)),
    'Longbow L':        (dict(Forward=8.6709, Backward=8.6709, Up=6.5032, Down=8.6709, Left=8.6709, Right=8.6709), (6.71, 5.97, 7.25)),
    'Longbow L Seeker': (dict(Forward=7.9607, Backward=7.9607, Up=5.9705, Down=7.9607, Left=7.9607, Right=7.9607), (4.86, 5.63, 5.32)),
    'Mantis':           (dict(Forward=8.7421, Backward=4.3711, Up=4.3711, Down=4.3711, Left=4.3711, Right=4.3711), (35.49, 44.59, 65.45)),
}
BUILD = {'Longbow S': core.longbow_s, 'Longbow L': core.longbow_l, 'Longbow L Seeker': core.longbow_l_seeker, 'Mantis': extra.mantis}
_CACHE = {}


def model(name):
    if name not in _CACHE:
        bp = BUILD[name](False)
        C.add_support_pbs(bp)                     # as build_all does
        C.finalize(bp)
        _CACHE[name] = bp
    return _CACHE[name]


class RailPower(unittest.TestCase):
    def test_peak_supply_covers_rails_and_every_thruster(self):
        for name in BEFORE:
            pw = C.power_budget(model(name))
            self.assertGreater(pw['rail_MW'], 0, name)
            self.assertGreaterEqual(pw['peak_supply_MW'], C.POWER_MARGIN * pw['peak_need_MW'], f'{name}: {pw}')

    def test_reactors_alone_carry_the_recharge(self):
        for name in BEFORE:
            bp = model(name)
            pw = C.power_budget(bp)
            self.assertGreaterEqual(pw['reactor_MW'], pw['rail_MW'], f'{name}: {pw}')
            gens = [b for b in bp.blocks if b['xtype'] == 'MyObjectBuilder_Reactor']
            for b in gens:
                self.assertTrue(any(it.sub == 'Uranium' and it.amount > 0 for it in bp.items_of(b)), b['name'])
                self.assertTrue(b['name'].startswith(f'{name} Reactor '), b['name'])

    def test_acceleration_on_every_axis_not_below_before(self):
        for name, (acc0, _) in BEFORE.items():
            acc = C.loaded_accel(model(name))
            for d, a0 in acc0.items():
                self.assertGreaterEqual(acc[d], a0, f'{name} {d}: {acc[d]:.3f} < {a0}')

    def test_turn_rates_kept(self):
        for name, (_, turn0) in BEFORE.items():
            tr = C.turn_rates(model(name))['pitch_yaw_roll_rad_s2']
            for axis, t, t0 in zip(('pitch', 'yaw', 'roll'), tr, turn0):
                self.assertGreaterEqual(t, t0, f'{name} {axis}: {t} < {t0}')

    def test_one_piece_no_dlc_lanes_clear(self):
        for name in BEFORE:
            bp = model(name)
            bp.validate()                         # mounts, one piece, no block in an exhaust / muzzle / camera lane
            pods.assert_clean(bp)
            names = [b['name'] for b in bp.blocks if b['name']]
            self.assertEqual(len(names), len(set(names)), name)

    def test_the_old_blocks_keep_their_names(self):
        for name, keep in (('Longbow S', ('Railgun', 'Battery', 'Gyroscope', 'Ion Main L', 'Ion Lift R', 'Remote Control')),
                           ('Longbow L', ('Railgun', 'Battery 4', 'Gyroscope', 'Ion Main R2', 'Ion Lift M', 'Antenna')),
                           ('Mantis', ('Railgun', 'Gatling', 'Battery', 'Gyroscope', 'Ion Main L2', 'Camera Up'))):
            names = {b['name'] for b in model(name).blocks}
            for k in keep:
                self.assertIn(f'{name} {k}', names)


if __name__ == '__main__':
    unittest.main()
