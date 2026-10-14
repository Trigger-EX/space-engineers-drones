# Wasp Split: the Wasp over two Programmable Blocks

WaspAI.cs is 98,971 of the 100,000 minified characters a PB takes. Wasp Split is the same drone as two scripts on one small grid:

| PB | script | minified | holds |
|---|---|---|---|
| `[Flight] Wasp Split PB` | `scripts/WaspFlightAI.cs` | ~76.0k | settings, AI GATE, LEAD, KZ, tracking, aim, control, rockets, firing, damage sensing, BREACH+SEEKER, AI hybrid, printer join, hive (`_SWARM`, `_TGT`, `_CMD`) |
| `[Staff] Wasp Split PB` | `scripts/WaspStaffAI.cs` | ~32.6k | FLEET LINK, FOLLOW / SATELLITE, LAUNCH, the full status screen, Flight watchdog |

`scripts/WaspAI.cs` and `tools/designs/wasp.py` are untouched (Wasp golden hash unchanged). The blueprint is `tools/designs/wasp_split.py`, kind `test` (never packaged):
the Wasp frame, PB renamed `[Flight] ...`, second PB `[Staff] ...` at (-1,1,1). Build: `python3 -I tools/build_all.py --no-zip --only "Wasp Split"`.

## Why this cut
`docs/audit/multi_pb_feasibility.md`: IGC on one grid costs 2 ticks, timers have a 1 s minimum, `TryRun` is unverified. So the hot path (track, lead, aim,
gyros, thrusters, rockets) stays whole in Flight, and only slow data crosses: owner geometry, take-off, fleet channel, display.

## IGC link (CurrentConstruct, key `WSPL<Flight PB EntityId>`, both PBs Update1)
| tag | from -> to | payload | rate |
|---|---|---|---|
| `.F2S` | Flight -> Staff | `MyTuple<int flags, long tgtId, Vector3D tgtPos, Vector3D aux(aimErr, fireWin, aBrake), float batt, string gateWhy>` | 5 Hz + mode change (heartbeat) |
| `.FOL` | Staff -> Flight | `MyTuple<Vector3D want, ownerP, ownerV, dynHome, radii(engage, leash, 0), int flags>` | 2 ticks in FOLLOW / burn, else 30 |
| `.S2F` | Staff -> Flight | `string` verb into `Command()`; a mode verb equal to the current mode is ignored | on event |
| `.CMD` | Flight -> Staff | `string` PB argument given to Flight, for the Staff's words | on event |
| `.TGT` | Staff -> Flight | fleet target relay, the `<Tag>_TGT` tuple, coalesced per id, <= 10 / tick | on arrival |
| `.FLT` | Flight -> Staff | the same tuple: target shares and "finished" for the fleet channel | on event |

F2S flags: bits 0-2 mode, 3 firing, 4 under fire, 5 lock stale, 6 AI on, 8-15 rockets (255 unknown). FOL flags: 0 owner known, 1 on foot, 2 take-off burn, 3 dynHome fresh.
The Staff finds the nearest `[Flight]` PB of its construct (every 600 ticks and on a grid change); merged printed copies share a construct, hence the EntityId in the key.

## Failure behaviour
* Staff silent for 1 s (killed, disabled, none on the grid): Flight clears the take-off flag, Home() is `_home` (or the dynHome while < 5 s old), engage radius 800, FOLLOW is flown as GUARD round the last owner point,
  the fleet relay stops. Hive and `Tag_CMD` keep working. The drone fights fully.
* Flight silent for 90 ticks after it had spoken, or `!IsWorking`: Staff releases gyro / thruster overrides, dampeners on, switches the `[AI]` blocks and guns off (once), repeats the release each second
  (never during its own take-off burn). Screen: `FLIGHT PB DOWN`.

## Config
Flight Custom Data: every `[Wasp]` key of WaspAI (same names; existing configs paste in), `[Kamikaze Defence]`, `[AI Gate]`. Staff: `[Launch]`, `[Follow]`, `[Fleet]` (written on first run). The Staff reads IGC Tag, Start Mode,
Combat Speed and Wingmate Separation Range from the Flight PB's Custom Data. Both PBs work with empty Custom Data. A PB argument works on either PB: Staff words are handled by the Staff, every word reaches Flight.

## Sim results (seeds stated; the Wasp is weak against the Hornet in this sim, so these show "no cut bug", not strength)
* `1v1/Wasp Split-vs-Hornet`, 10 seeds: ship 0 0 wins / 8 losses / 2 timeouts; baseline `1v1/Wasp-vs-Hornet` 1 / 7 / 2. Shots per fight 0-17 in both.
* `1v1/Wasp Split-vs-Wasp` vs `1v1/Wasp-vs-Wasp`, 10 seeds each: 3 / 6 / 1 (timeouts) vs 2 / 3 / 4 timeouts + 1 mutual kill.
* Staff killed at 5 s (3 seeds) and at 45 s: Flight keeps fighting. Flight killed at 45 s: overrides released 1.5 s later (speed falls, gyro rate 0); `disablepb Flight`: released at once, resumes on `enablepb`.
* `follow-test --count 3`: cruise, weave, lost, foot, fight, park + satellite all OK, numbers within 1 m of the single-PB Wasp.
* `dock-launch`: left in 4.8 s, GUARD at 5.0 s, same as the Wasp (also with `--arm-at`, `--join-at`).

Real-game IGC latency is not measured; a follow lag of 2-4 ticks is tolerable. Release zips are not rebuilt.
