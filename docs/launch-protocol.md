# Launch protocol: automatic take-off of printed drones

Owner: `tools/launch_block.cs` (the shared LAUNCH block), `tools/launch_sync.py`, this document. Printer side: `scripts/HiveMotherAI.cs` and
`docs/hive-protocol.md` section 9. Test: `sim/dock_launch_test.sh` (`SimHost.exe dock-launch`, section 7).

## 1. The bug (reproduced in the simulator)

Before this change a printed drone left only if it received ONE message: `ReadJoin()` handled `<Tag>_JOIN` and started GUARD, and only if the
drone was IDLE and already undocked at that moment. The blueprint state of the script is IDLE ("safe default"), the drone never thrusts by
itself, and a missed `_JOIN` (PB still compiling or never compiled, batteries empty, message sent 3 times in the first seconds, drone
on the wrong channel) left it hanging next to the printer for ever.

`sim/dock_launch_test.sh` baseline (the old scripts, the dummy printer's merge block off at 3 s, no `_JOIN`, 30 s):

```
dock-launch Hornet:  RESULT split 3.02 s, first movement never, max dist 2.3 m, max speed 0.0 m/s, final mode IDLE (since 0.5 s)   LAUNCH FAILED
dock-launch Harrier: RESULT split 3.02 s, first movement never, max dist 2.3 m, max speed 0.0 m/s, final mode IDLE (since 0.5 s)   LAUNCH FAILED
```

## 2. What the drone does now (LAUNCH block v1.1)

State machine, evaluated from the top of `Control()` (`LaunchStep`), at most 6 checks per second, none of them heavy:

| state | meaning | leaves when |
|---|---|---|
| 0 free | not docked, nothing armed | a trigger below fires; or a merge pair locks (-> 1) |
| 1 docked | a merge block on this PB's grid is connected (`IsConnected`); `Armed=true` is written to Custom Data | no connected merge block (two checks in a row) or the PB's grid changed (-> 2) |
| 2 burn | the take-off | `Burst Seconds` elapsed, or `Clear Distance` covered after 0.5 s (-> 3) |
| 3 launched | done for this life of the script | `armed` re-arms it (docked again) |

Triggers that start the burn, any ONE is enough (the first is the primary, the others cover failures):

1. **The PB sees the separation itself** (state 1 -> no locked merge block, or `Me.CubeGrid` changed). No message needed. This is what makes the
   drone leave even if the printer's arming, `_JOIN`, FLEET `join` and recompile all failed.
2. **`[Launch] Armed=true` while not docked** (the printer wrote it before the release, then the PB was recompiled: the new program has no memory
   of the docking, but Custom Data survives).
3. **Argument `launch`** (also allowed over FLEET by default): starts the burn of a free, never launched drone (state 0/1). Ignored once launched.
4. **`<Tag>_JOIN`** (any payload; HiveMotherAI sends the string `"join"`) or **`<ch>_CMD` string `"join"`** on the FLEET channel (`_flChan`, i.e.
   `[Fleet] Channel`/`Secret`). The block registers its own listeners, so `join` does NOT have to be in the FLEET `Allowed Commands`. A join only
   counts for a drone that is IDLE, undocked, and complete (every terminal block on its grid is functional, it has a thruster and a controller), so a half-built
   drone on another bay of the same printer cannot take off on a neighbour's join.
5. **Argument `armed`** sets `Armed` (printer: `DPb.TryRun("armed")` while the PB is still on the printer grid); `disarm` cancels (Custom Data `Armed=false`
   and no auto-docking arm until the next `armed`).

`Auto=false` (rack mode): the separation does not launch; only the argument `launch` does.

Take-off (`LnBegin` / `LnBurn`):

* Pre-flight, after the grid has split (the block waits up to ~1.7 s while a second merge block is still on the grid, so it never drives the printer's
  thrusters; it only touches blocks with `CubeGrid == Me.CubeGrid`): thrusters, gyros, batteries (and charge mode `Auto`), antennas (and
  `EnableBroadcasting`), guns and cameras are switched ON; every merge block is switched OFF (never re-latches).
* Direction `Away`, first that applies: (a) Custom Data `Away X/Y/Z` (world vector, written by the printer; ignored when it points INTO the
  printer, i.e. against the direction learned while docked), (b) the direction learned while docked (from the printer-side merge block to the drone-side
  one, stored in the PB's own axes), (c) from the drone's own merge block to its centre of mass, (d) away from the last `_HOME` point, (e) the controller's Backward.
* Thrusters get an override proportional to the cosine between their push direction and `Away` (largest = 100 %), gyros are overridden at ZERO rate
  (holds the attitude, no gyro sign convention involved), dampeners off. Thrust is cut while the speed along `Away` is above `Max Speed`.
* End: overrides released, dampeners on, `Armed=false` written, then the script is told `home` and the mode: the script's `Start Mode`
  if it is not IDLE, else `[Launch] Mode` (default GUARD). Mode changes are only made if the script is still IDLE.
* Batteries: the block cannot wait for a charge once the drone is on its own (it has no power then). The printer decides when to release
  (HiveMotherAI `DroneCharged`, drone batteries are set to `Auto` there too); the block only guarantees that its batteries are enabled and in `Auto`.

## 3. Custom Data (`[Launch]`, defaults written on the first run, `reload` re-reads)

| key | default | meaning |
|---|---|---|
| `Auto` | true | launch on the separation by itself (false: only the argument `launch`) |
| `Burst Seconds` | 4 | maximum length of the burn (HiveMotherAI writes its own `Launch Burst Seconds`) |
| `Away X`, `Away Y`, `Away Z` | 0 | world vector away from the printer; 0,0,0 = unknown |
| `Armed` | false | set by the printer (or the drone itself when it docks); cleared at take-off |
| `Clear Distance` | 8 | the burn ends early once the drone moved this far (m) along `Away` |
| `Max Speed` | 25 | thrust is cut above this speed along `Away` (m/s) |
| `Mode` | GUARD | mode after take-off when the script's own `Start Mode` is IDLE (HUNT, GUARD, ASSIST, IDLE = stay idle) |

`Exit Direction` (written by the printer) is ignored by the drone; the printer already folded it into `Away X/Y/Z`.

## 4. Arguments and messages

| what | where | effect |
|---|---|---|
| `armed` | PB argument | arm now (printer, before the release) |
| `launch` | PB argument, FLEET `launch` | start the burn of a free drone |
| `disarm` | PB argument | cancel the auto-launch |
| `join` | `<Tag>_JOIN` any payload, `<ch>_CMD` string | see trigger 4 |

## 5. Integrating another drone script (about 8 lines)

1. `python3 -I tools/launch_sync.py --apply --add <ScriptName>` inserts the block before `public Program()` (`--check` keeps all copies identical, a
   script without the block is only reported as `absent`). Do not edit inside the block.
2. Constructor and `reload`: `LaunchLoad();` after `LoadConfig(); FleetLoad(true);`. Delete the old `_JOIN` listener/`ReadJoin`/`Docked` if the script has them.
3. `Command()`: `case "armed": case "launch": case "disarm": LaunchCmd(w[0]); break;`
4. First line of `Control(dt)` (or of the tick, before anything can thrust):
   `if (LaunchStep(dt, _mode == Mode.IDLE, StartMode, _dynHomeT > 0, _dynHome, Command)) return;`
   Arguments: seconds since the last call, "the script is idle" (JOIN only starts idle drones), the script's start mode text, whether a rally point
   is known and where it is, and the `Action<string>` the block calls with `"home"` then the mode word (`guard`/`hunt`/`assist`; a script with other
   words, e.g. Gnat `attack`, passes a small wrapper delegate).
5. Display: `_sb.Append(LaunchStatus());`
6. The block needs the script's `string Tag` and the FLEET LINK block's `_flChan`. It uses `Me`, `IGC`, `GridTerminalSystem`, `MyIni` and nothing else.

HornetDroneAI and HarrierAI are integrated this way (v2.2).

## 6. What the printer must still do (HiveMotherAI side, see hive-protocol.md section 9)

* Before the release, while the drone PB is visible: `Enabled=true`, `ApplyAction("Recompile")` when it is not running (`IsRunning` is true only while
  Main runs, so the printer cannot tell a never-compiled PB from an idle one; recompiling a PB that has not been run yet is harmless), write `[Launch]` and
  run `armed`. All of these are best effort; the drone's own detection does not depend on them.
* Switch the printer-side merge block off (and the drone-side one, optional). The drone-side block is also switched off by the drone.
* Keep sending `_JOIN` / `join` (cheap insurance) and release only when the drone is complete and charged: the drone cannot wait for a charge.

v1.1 (audit printers-03): `LnReady()` refuses to start the burn while the PB's grid also holds a `[Bay` block or a Programmable Block whose Custom Data has a `[Hive Mother]` section (a drone still fused to its printer would otherwise burn the printer's thrusters and gyros). The PB screen says "Launch: held, this grid holds a printer".

## 7. Simulator test

`SIM_OUT=<dir> bash sim/build.sh`, rebuild the model blueprint (`python3 -I tools/build_all.py --only "Hornet"`: the PB runs the script text stored in
`out/Blueprints`), then `SIM_OUT=<dir> bash sim/dock_launch_test.sh [Model ...]` (default Hornet and Harrier). One run:
`mono SimHost.exe dock-launch --model Hornet --seconds 30 --release-at 3 [--both-merges] [--arm-at T] [--join-at T] [--recompile-at T] [--custom-data "INI"] [--echo] [--trace FILE]`.

The test builds a dummy printer in code (the blueprint's own merge block gets its partner on the face side, a stalk and a 9x9x9 armor box, heavier than
the drone), spawns the real blueprint merged to it, runs the real script in its default blueprint state (IDLE, default Custom Data) and switches the
printer's merge block off at 3 s. PASS means: the drone is more than 12 m from the printer's merge block and not IDLE at the end. Ten cases per model:
default, both merge blocks off, printer arms at 1 s (also with an `Away` that points INTO the printer, which must be ignored), PB recompiled while docked,
PB recompiled during the burn, PB first running after the release with `Armed` in Custom Data, PB first running after the release with only a `_JOIN`, and two negative
controls (`Auto=false`, and a never-docked free drone with no trigger: both must stay).

Result with the new scripts (Hornet accel 6.5 m/s2, Harrier 12.3 m/s2 forward):

```
Hornet  default: split 3.02 s, first movement 3.18 s, >12 m away 4.77 s, max dist 18.1 m, max speed 10.1 m/s, final mode GUARD (since 5.0 s)
Harrier default: split 3.02 s, first movement 3.12 s, >12 m away 4.33 s, max dist 25.3 m, max speed 13.6 m/s, final mode GUARD (since 4.5 s)
DOCK-LAUNCH ALL OK   (20 cases)
```

Combat unchanged: `run --filter '^1v1/.*(Hornet|Harrier)' --seeds 10` (250 runs) with the old and the new Hornet/Harrier blueprints: outcome, winner and duration identical in
all 250 runs, 0 script exceptions (the block is inert for a ship that never docks). Score (win 1, draw/timeout 0.5) over those runs: Hornet 63.6 %,
Harrier 58.6 % both before and after (docs/sim-r2-base-1v1.md, 50 seeds against all 13 models: Hornet 53.8 %, Harrier 63.6 %; the 10-seed subset has a
different opponent mix).

## 8. Not verified in the real game

* That `IMyShipMergeBlock.IsConnected` turns false and `Me.CubeGrid` changes in the same tick or within the ~1.7 s the block waits for the split.
* That a PB welded from a projection runs its constructor by itself (the printer's `Recompile` is the insurance; the drone cannot compensate for a PB that does not run at all).
* The merge block face convention is not needed (the direction comes from the two merge block positions), but a printer whose bay has other drone merge blocks within a
  few metres of the PB can confuse the learned direction; the printer's `Away X/Y/Z` has priority and is the reliable source.
* Burn length with real thrust/mass: the drone leaves after `Clear Distance` (8 m) at the earliest 0.5 s, at most `Burst Seconds`.

## 9. Review notes (independent launch review)

* **Scripts with the block**: Hornet, Harrier, Gnat, Longbow (S and L, same script), Wasp, Lancer, Aegis, Mantis (`launch_sync.py --check` ok; Basilisk is on the EXCLUDE list: it has no merge block and the block cost ~9k chars of its 100k budget; Longbow keeps it because Longbow S has a merge block and shares the script with L).
  Hooks verified by grep in each: `LaunchLoad` (constructor and `reload`), `armed/launch/disarm` in `Command`, `LaunchStep` first on the control path (Gnat: before `AiStep`/`Control`), `LaunchStatus` in the display.
* **Not on the block (honest status)**:
  * **Stinger** (`StingerAI.cs`) is a rack missile. It has no LAUNCH block on purpose: it waits docked, and `Docked()` plus `Auto Launch When Released` start the hunt only when a target is in range
    (or on the `launch` command over `<LaunchTag>_CMD`). It does not fly off by itself without a target. The Stinger Rack printer sends `launch` three times after separation (rack mode).
  * **Midge** has no script. With no Programmable Block it cannot be told to take off: its AI Flight/Offensive blocks must be switched on by hand (or by a printer-side timer) once and the
    defaults of those blocks decide what it does. Midge Lite is a Gnat Lite and does launch (Gnat block).
  * **Longbow L** has no merge block, so the block is inert there.
* **Away sign**: `HiveMotherAI` keeps `b.Away` as the direction the PRINTER pushes itself (drone to printer). The `[Launch] Away X/Y/Z` written to the drone is the negation (away from the printer).
  The drone additionally ignores a stored vector that points into the printer when it learned the merge-pair direction while docked.
* **Thrust-axis fallback (new)**: when no functional thruster pushes within 0.3 (cosine) of Away, and none points into the printer (cosine above -0.2), the burn uses the thruster axis that is
  closest to Away (usually perpendicular), so ships with only fore/aft thrusters (Gnat) leave instead of staying put. The text `thrust axis` appears in the take-off message.
  If every thruster points into the printer the burn does not thrust at all.
* **Safety**: the burn can only start when the merge pair is open (`LnDocked()` false, two consecutive checks) or on `launch`/`Armed` while undocked; it waits up to ~1.7 s for the grid split.
  Gyros are held at zero rate; no gyro sign convention is used.
* **Test**: `sim/dock_launch_test.sh Hornet Harrier Gnat Wasp Lancer Aegis` all 10/10 ('DOCK-LAUNCH ALL OK' each). Mantis and Longbow S need the printer latch (`with_latch`) in the test blueprint; the
  stock blueprints have none, so they are not covered by the shipped script (see the lead's scratch run in section 7).
