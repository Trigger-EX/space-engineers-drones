# Pathfinder: stand-alone hive weakpoint finder (and its printer, the Pathfinder Nest)

Status: **implemented** 2026-10-16, sim-smoke and 20-seed A/B run (section 6: a net loss of power kills in the one matchup tested) (script `scripts/PathfinderAI.cs` v1.0, model `Pathfinder` in `tools/designs/peeper.py`, printer
`Pathfinder Nest` in `tools/designs/printers.py`). Compile-checked; sim results in section 6; nothing has run in the game.

Owner request: break the Ballista's small probe (the Peeper) off into its own model, give it a printer, connect it to the hive so it waits
until there is a target within 5 km and then flies to it, and let it run the Longbow L Seeker / Basilisk Seeker experiment: a turret on the
drone draws a line to the target's power systems and the location of that block, relative to the target ship, goes to every friendly combatant.

## 1. What it is

| piece | what |
|---|---|
| hull | the Peeper's: 3.5 x 4.5 x 5.5 m small grid, 60 blocks, 4.6 t, 12 ion thrusters (6.3 m/s2 on every axis), 4 batteries, gyro, 3 cameras, 5 km antenna, one `SmallGatlingTurret [Seeker]` with **no ammunition** (a target designator). Green paint. |
| latch | none in the blueprint. The Peeper's merge block sits on a stalk under the tail, which the outline rule forbids for new work, so the Pathfinder's printer sets a **flush** merge block into the hull (`printers.DRONES` latch=True -> `printer_lib.with_flush_latch`, as the Tubes and Longbow S). |
| PBs | `Pathfinder PB` (PathfinderAI.cs, ~52 k characters raw: one PB is plenty) and the `[Thrust]` PB every flight script gets (`C.add_support_pbs`). |
| printer | `Pathfinder Nest`: 2 bays, Max Drones 2, 2 ion thrusters per direction, HiveMotherAI as every other printer (tag `PATHFINDER`, `_SWARM` heartbeat counts the live ones, reprints a lost one). |
| Ballista | unchanged: it keeps its Probe Foundry and the Peeper/Spotter pair. |

## 2. Behaviour (PathfinderAI v1.0)

States: `IDLE` (on the printer: merge locked, or a `[Bay` / Hive Mother PB block on its grid: everything off) -> shared **LAUNCH** block
(take-off burn along the printer's `Away` vector, switches welding-left-off blocks on) -> `WAIT` -> `SCOUT` -> `HOME` -> `WAIT`.
A pasted (never docked, never armed) Pathfinder goes to `WAIT` by itself after 3 s.

* **WAIT**: dampeners hold it where it stopped after take-off. It listens to the fleet: `<ch>_TGT` (FLEET LINK block, plus `[Fleet] Listen Tags`).
  The first fresh contact (< 5 s old) with radius >= `Min Target Radius` (10 m) **within `Engage Range` (5,000 m) of the drone** starts SCOUT.
  Several: the biggest, ties the nearest. A radius -1 "finished" message drops a contact for 120 s.
* **SCOUT**: straight in, then circles the target **on the wing's side** (`Wing Side=true`): a circle of half-angle `Sweep Angle` (45 deg)
  around the axis from the target to the home point (where the wing comes from), at target radius + `Orbit Margin` (60 m, at least `Orbit Min`
  100 m) from the centre, nose (cameras) on the target. Why: the turret picks the power block nearest to it, so a full orbit finds blocks on the
  far side of the ship, which the snipers would have to drill through the whole hull to reach (sim, section 6). Opposite points of the circle
  are 90 deg apart, enough spread to cross the lines. `Wing Side=false` = the Peeper's full sphere orbit. Orbit speed `Orbit Speed` (25 m/s)
  capped so the centripetal pull needs at most half the weakest axis' thrust. The turret's own lock on the target refreshes the track (and is shared on `<ch>_TGT` twice a
  second, `Share Targets`), so it keeps working when the wing loses its lock. Target older than `Park After` (20 s) or beyond `Leash Range`
  (7.5 km): the next contact in range, else HOME.
* **HOME**: back to the fleet's `<ch>_HOME` (a carrier) or `<Tag>_HOME`, else its take-off point; within `Home Stop` (30 m) and slow -> WAIT.
* Commands (PB argument or FLEET `Allowed Commands`): `wait|hunt|guard`, `hold|idle|stop`, `rtb`, `home`, `reload`, `armed|launch|disarm`.

## 3. Finding the power block

Every 0.4 s while the turret (targeting group `Seeker Group`, default `PowerSystems`) `HasTarget`, is on our target and `IsAimed`:

1. **Aim line.** The block position `GetTargetedEntity().HitPosition` when the game gives one (flag HITPOS), else the turret's aim line
   (Azimuth/Elevation): `q` = the point of that line nearest the target centre, minus the turret's lead (`Lead Speed` 800 m/s, the gatling's).
2. **Hull entry.** A camera ray through `q` (+`Ray Past` 30 m) gives the hull hit `s` and the ray direction `d`, and the target's `Position` and
   `Orientation` at that moment.
3. **Target frame.** The turret line (origin and direction), `s` and `d` are turned into the target's own frame (`(x - Position)` rotated by
   `Transpose(Orientation)`), the convention of `<ch>_WP` and the BREACH block, so they stay true while the ship moves and turns.
4. **Crossing the lines.** The lines are grouped into block hypotheses: a line joins the hypothesis whose least-squares point (or, with one
   line, whose line) it passes within `Line Tolerance` (3 m + 1.5 % of the range: 1 deg of aim error) of; else it starts a new one (8 at most,
   12 lines each, lines older than 60 s dropped). The point `V` nearest to all lines of a hypothesis (`sum(I - u u') V = sum(I - u u') p`, 3x3)
   is **the block**, once >= 3 lines spread >= 6 deg cross within the tolerance, inside the target's radius, not in front of the hull.
   Orbiting gives the spread for free (25 m/s at 100-160 m: 5-9 deg between samples). When the turret switches to another power block
   (the old one died, or a nearer one came round) the new lines start a new hypothesis; the reported one is the confirmed one with the most
   lines of the last 20 s, with a small hysteresis.
5. **Relay** (`Share Weakpoints`), **only a confirmed block** (crossed lines or the game's block position; `Send Unconfirmed=false`), on `<ch>_WP` after each sample of the reported hypothesis, else every 2 s while it has a line younger than
   20 s: `(target, s, d, (0, hint, age), grid id, kind | 8 [| 16])`, kind 1 PowerSystems (2 Weapons, 3 Propulsion, 4 other). With a confirmed
   block, `d = normalize(V - s)` and `hint = |V - s|`, so every receiver gets `V = s + d * hint` exactly. (`Send Unconfirmed=true` also sends
   single camera lines with hint -1, as the Basilisk Seeker does; off by default because a receiver drops its own deepening hole for any
   seeker point.)
   Optional `Host Tag` (empty by default; `BALLISTA` feeds a Ballista's Spotter): the same sample as `<Host>_CRIT` in the Peeper's format.

The Pathfinder never forwards a received `<ch>_WP` (it listens to none) and sends only its own samples: the no-echo rule holds.

## 4. Who uses it (no change in any receiver)

Every armed split drone's Staff PB (WEAKPOINT RELAY v1.0: Hornet, Gnat, Longbow S/L, Wasp, Lancer, Harrier, Aegis, Mantis, Basilisk) already
clusters `<ch>_WP`, ranks seeker kinds above camera breach points (`Weakpoint Priority=1,3,2`: power first) and hands the best hole to its
Flight PB. A Flight PB on the same side (40 deg cone) drills the hole; on another side it **converges**: it aims at `V` through its own nearest
hull point (`WpAdopt`, needs the depth hint, which the Pathfinder now gives without the game's HitPosition). Drilled to `hint + 3 m`: DONE, the
wing moves on. Helm ships (ShrikeAI: Ballista, Warden, ...) do not read `<ch>_WP` yet (retrofit Pending row of the weakpoint relay); the
Ballista can take the Pathfinder's samples through `Host Tag=BALLISTA`.

A different `[Fleet] Secret` keeps two fleets' weakpoints apart, as for every fleet tag.

## 5. Custom Data (`[Pathfinder]`, defaults written on the first run)

`IGC Tag=PATHFINDER`, `Host Tag=`, `Seeker Group=PowerSystems`, `Start Mode=WAIT`, `Engage Range=5000`, `Leash Range=7500`,
`Min Target Radius=10`, `Orbit Margin=60`, `Orbit Min=100`, `Orbit Speed=25`, `Max Speed=90`, `Turn Rate=3`, `Settle Seconds=1.5`,
`Lead Speed=800`, `Ray Past=30`, `Park After=20`, `Line Tolerance=3`, `Home Stop=30`, `Share Targets=true`, `Share Weakpoints=true`,
`Wing Side=true`, `Sweep Angle=45`, `Send Unconfirmed=false`.
Plus `[Fleet]` (FLEET LINK) and `[Launch]` (LAUNCH; the printer writes it).

## 6. Sim

Enemy `Tech Hunter 90k` (`pc/enemies_th90k`), `--turret-ai-block-extends-range false`, 300 s cap, 20 seeds per line (a rough A/B
direction, CLAUDE.md). The sim gives the turret ammunition, so it also shoots (12,600 damage in one run); every run is a timeout.

| fleet | TH power blocks killed / run | runs with a power kill | Pathfinder disabled at |
|---|---:|---:|---|
| 2x Longbow L (no finder) | 1.55 | 15 / 20 | - |
| Pathfinder + 2x Longbow L, v1.0 first cut (full sphere orbit, unconfirmed lines sent) | 0.75 | 9 / 20 | 23-50 s |
| Pathfinder + 2x Longbow L, as committed (wing side, confirmed blocks only) | 1.05 | 11 / 20 | 20-51 s |

What works (one traced run, seed 0): take-off, WAIT, a contact 2.2 km away -> SCOUT, first aim lines from 455 m, 12 lines crossed within
0.1 m at a power block 6 m under the hull by t = 32 s; the Longbow's Staff shows `Weakpt : power hole`, its Flight `hole 0 m (fleet 1)`.
0 script exceptions in all 63 runs.

What does not (yet): **in this matchup the finder costs power kills.** The Tech Hunter kills the Pathfinder 20-50 s in, during the approach
or within seconds of arriving (a wider orbit, 250 m margin, changed nothing in 3 seeds: it dies on the way in). The Longbows fire about once
a minute; a receiver drops its own deepening camera hole for any seeker hole at once (aim block `WpAdopt`, "seeker beats kind 0") and the
hole expires 20 s after the finder dies unless the receiver's own probe ray confirmed it. So the wing loses its progress for a hole it
rarely gets to drill. The wing-side circle and confirmed-only sends halved that loss. The remaining levers are on the receiving side,
**not changed here** (the shared aim block in nine scripts, owner's call): keep a held hole that made progress in the last ~10 s instead of
switching to a fresh seeker hole, or keep relayed holes past the finder's death. A faster-firing wing, or a finder that lives longer, also helps.

## 7. Risks and in-game tests

Same unknowns as the Peeper and the Seeker variants (`docs/design/hive_weakpoint.md` 10, `docs/design/ships/Ballista.md` 7):
1. A turret **without ammunition** may not acquire or aim at all. If so, load a few gatling magazines (`Lead Speed` already assumes gatling rounds;
   it will then also shoot).
2. The targeting groups may not pick subsystem blocks of an enemy grid from outside: then every line goes to the target centre, the lines
   cross at the centre, and the wing drills for the middle of the ship.
3. Printed drones may not separate from the printer (`docs/audit/printers.md` printers-01), as for every printer.
4. The orbit is close (100-160 m) and slow enough to be hit: the drone is expendable and the nest reprints it. Raise `Orbit Margin` if it
   dies before 3 lines (the small turret reaches 800 m).
5. Flying 5 km straight in: no obstacle or friendly avoidance on the way.

In-game tests: (1) paste the nest, it prints two, both take off and sit by the nest (`WAIT`); (2) bring a large target within 5 km with a Longbow
wing on the same FLEET channel: both go, the PB screen shows `aim line ... line(s)`, then `Block: crossed, N lines`; (3) the Longbow Staff screen
shows `Weakpt : power hole ..., k drone(s)`, the Flight status `hole N m (fleet k)`; the railgun holes meet at one power block; (4) target gone:
both fly back and wait.
