# Hive / fleet protocol (IGC)

All drone scripts talk over IGC broadcasts. Every drone type keeps its **own per-type tags** (unchanged) and, since FLEET LINK v1.0, **also**
listens to (and, optionally, sends on) one **common fleet channel**. This document is the reference for tags, tuples, commands, the
configuration keys and the security model. The shared code lives in `tools/fleet_block.cs`; `tools/fleet_sync.py` keeps the copy inside every
script identical.

## 1. Per-type channels (unchanged)

`<TAG>` is the script's `IGC Tag` (Custom Data). Defaults: HORNET (Hornet and Watcher), GNAT, LONGBOW (S and L), WASP, LANCER, HARRIER,
AEGIS, MANTIS, BASILISK, STINGER (command channel only).

| tag | payload | direction |
|---|---|---|
| `<TAG>_TGT` | `MyTuple<long id, Vector3D pos, Vector3D vel, double radius, long sender>`; `radius < 0` = "target finished, everybody drop it for 120 s" | drones and carrier relays -> same-type drones (plus the legacy feed lists below) |
| `<TAG>_CMD` | `string` | carrier relay / player -> drones |
| `<TAG>_HOME` | `Vector3D` (moving rally point) | carrier relay -> drones |
| `<TAG>_HOMER` | `double` (carrier radius) | carrier relay (all three since relay v1.1/v1.2) -> Gnats, FOLLOW drones |
| `<TAG>_SWARM` | `MyTuple<long gridId, Vector3D pos, Vector3D vel, long targetId, int flags>`; bit0 under fire, bit1 alarm, bit2 lock stale | wingmates of the same type |
| `<TAG>_JOIN` | (empty trigger) | printer -> freshly released drones |
| `<TAG>_HIT` | `MyTuple<Vector3D, Vector3D>` (Gnat only) | Gnat -> Gnats |
| `DRONE_CUE` | same tuple as `_TGT`, small/fast threats | Aegis / Harrier -> Aegis / Harrier |

Legacy cross-type feeds that already existed before FLEET: Gnat, Wasp, Lancer, Harrier listen to `HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,
MANTIS,BASILISK,WATCHER` (+ `_TGT`, key `Listen To IGC Tags (target feeds)`), Stinger additionally to `GNAT` and `DRONE_CUE` (key
`Listen To IGC Tags`). Hornet, Longbow, Mantis, Basilisk (and Aegis, except for `DRONE_CUE`) listened to nobody but their own tag, and
nobody listened to `WATCHER_TGT` because the Watcher shares on `HORNET_TGT`.

## 2. The FLEET channel

Name: `<ch>` = `FLEET` (key `Channel`), or `FLEET_<secret>` when `Secret` is set. Four tags:

| tag | payload | meaning |
|---|---|---|
| `<ch>_TGT` | `MyTuple<long, Vector3D, Vector3D, double, long>` identical to `<TAG>_TGT` | a contact: id, position, velocity, radius, sender PB. `radius < 0` = finished, drop it. |
| `<ch>_HOME` | `Vector3D` | rally / home point (sent by the carrier relays, every 1/6 s) |
| `<ch>_CMD` | `string` (one command, optional arguments) | fleet-wide command |
| `<ch>_HOMER` | `double` | radius of the ship sending `<ch>_HOME` (carrier relays since v1.1/v1.2). Read by the FOLLOW block and the Gnat, not by the FLEET block. |

Rules every receiver follows (inside the shared block, `FleetOk` / `FleetPump`):

* A target message is processed by the script's **own existing target handler** (same de-duplication as `<TAG>_TGT`: "own sighting younger than
  0.3-0.5 s wins", `Ignored()`, wingmate and own-grid filter, radius < 0 handling). The block only rejects NaN / absurd values (|coordinate|
  >= 1e9, radius >= 1e5) and the PB's own grid.
* A contact that arrived over IGC (`Src == "IGC"`) is **never re-shared** (this is pre-existing logic in every script), so the channel cannot
  echo or amplify.
* A command is accepted only if the first word is in `Allowed Commands` (default `hunt,attack,guard,follow,escort,form,assist,rtb,idle,stop,
  launch,abort,split,focus,next,unlock,aialert`; `follow,escort` since v2.2). Not allowed over FLEET by default: `lock` (manual raycast), `range`, `ai`, `aim`, `home`, `reload`,
  `disarm`. Each script applies the word to its own `Command()` (unknown words are ignored there), so `fleet rtb` makes all types return,
  `fleet launch` releases the Stingers, `fleet hunt` starts everything.
* Home: last writer wins (also versus `<TAG>_HOME`). Use one carrier per fleet, or disable `Accept Home` on drones that have another home.
* Sending: every script that shares a target on `<TAG>_TGT` (and the radius -1 "finished" message) sends the same tuple to every tag in
  `Extra Broadcast Tags` (default `FLEET` = `<ch>_TGT`). Scripts with `Share Targets Over IGC=false` send nothing on either channel.

## 3. Configuration (PB Custom Data, own `[Fleet]` section)

Defaults are written on the first run (and after `reload`). Case of tags is irrelevant, they are upper-cased.

```
[Fleet]
Channel=FLEET                      ; "" switches the whole fleet channel off (listen, send, commands, home)
Secret=                            ; any word without spaces; part of the tag: FLEET_<secret>_TGT / _CMD / _HOME
Listen Tags=                       ; extra target feeds: "SCOUT" -> SCOUT_TGT, a name with "_" is used as is, "FLEET" = the fleet channel
Extra Broadcast Tags=FLEET         ; where this PB copies its target shares (same tag rules). Watcher: FLEET. "" = per-type tag only
Accept Commands=true
Allowed Commands=hunt,attack,guard,form,assist,rtb,idle,stop,launch,abort,split,focus,next,unlock,aialert
Accept Home=true
```

The script's own section (`[Hornet]`, `[Gnat]`, ...) is untouched; the existing `IGC Tag` and `Listen To IGC Tags` keys keep working. The
block re-registers listeners only when the resolved tags change, so `reload` does not duplicate them.

Carrier relays (`HornetMothership`, `GnatMothership`, `LongbowMothership`) use the same block send-only: they also broadcast their home point and
every enemy their turrets track on `<ch>`, and the argument `fleet <cmd>` (for example `fleet rtb`) sends `<cmd>` on `<ch>_CMD` to every type
(a plain argument still goes to the per-type `<TAG>_CMD` only).

## 4. Who sends what

| script | sends on FLEET | listens on FLEET |
|---|---|---|
| Watcher (HornetDroneAI) | `_TGT` (its contacts) | `_TGT`, `_CMD`, `_HOME` |
| Hornet, Gnat, Longbow S/L, Wasp, Lancer, Harrier, Aegis, Mantis, Basilisk | `_TGT` of the locked target and of "finished" | `_TGT`, `_CMD`, `_HOME` |
| Stinger | nothing | `_TGT`, `_CMD` (`launch`, `abort`), `_HOME` ignored |
| Carrier relays | `_TGT`, `_HOME`, `_HOMER`, `_CMD` (on `fleet ...`) | nothing |
| HiveMotherAI (printer) | nothing (its `_JOIN` and rack `launch` stay on the per-type tags) | nothing |

## 4b. Weakpoints (`<ch>_WP`, WEAKPOINT RELAY v1.0)

One more fleet tag, `<ch>_WP`, carries the shared breach point (design and reasons: `docs/design/hive_weakpoint.md`). Payload
`MyTuple<long target, Vector3D s, Vector3D d, Vector3D info, long senderGrid, int flags>`: `s` hull entry and `d` drill direction in the
TARGET frame, `info` = (hole depth m, vital depth hint m or -1, age s). Flags: bits 0-2 kind (0 camera breach point, 1 PowerSystems,
2 Weapons, 3 Propulsion, 4 other), 8 seeker found it, 16 block position from the game, 32 DONE, 64 STALL, 128 ADOPTED.
A drone sends only its own confirmed findings (a seeker sample, or its hole after its own probe ray hit it) and never forwards a received
one; holes within 4 m are one hole and the best wins (seeker kind by `Weakpoint Priority`, supporters, depth, lowest grid id). The Staff PB
does all of this (shared block in the nine `*StaffAI.cs`) and hands the Flight PB the best hole on `.WPT`. Keys: `[Fleet] Weakpoint Priority=1,3,2`,
`Weakpoint Expire (s)=20`; Flight script section `Use Fleet Weakpoints`, `Share Weakpoints`, `Weakpoint Cone (deg)`; command `wp on|off`.
Not in `Allowed Commands`. A different `Secret` keeps the weakpoints of two fleets apart.

## 5. Security (read before playing multiplayer)

IGC broadcasts reach **every programmable block within antenna range, whatever its faction**, and the game gives a receiving script no
trustworthy sender identity (`MyIGCMessage.Source` is only the sender's PB address, which anyone can read). So:

* Anyone who knows the tag can read your contacts and can **inject targets** (fake positions, "finished" messages) and **send commands**
  (`rtb`, `idle`, `launch` -> your Stingers detonate away from the enemy). The default `FLEET` tag is public knowledge.
* **Set `Secret=` to the same unguessable word on every PB of your fleet** (Custom Data is only visible to people with terminal access to
  the block). Without the secret an enemy script cannot guess your tags. It is not encryption: anyone who can read your
  Custom Data (or a captured drone's PB) learns it, so change it after a loss.
* Defence in depth: `Accept Commands=false` on drones that should only ever be steered locally (the per-type `<TAG>_CMD` still works), and
  trim `Allowed Commands` to what you need (for example `rtb,hunt`). Plausibility filters in the block (finite values, size limits) stop
  crashes, not lies.
* Separate fleets on one server: give each its own `Secret`.
* The simulator carries IGC only between non-enemy teams, so the sim cannot show spoofing; the secret test below shows the isolation instead.

## 6. Verification (private sim build, 30 seeds each, blueprints rebuilt with `build_all --only`)

Before = blueprints built from the scripts at the start of this task (no FLEET code); after = with FLEET LINK. Harness: a small program in the
scratchpad using `SeSim.Run.ScenarioGen/ScenarioRunner` (the shipped sim has no Watcher+Hornet or Watcher+Stinger scenario). "Blind" = the
receivers get `Auto Camera Scan=false`, `All-around Close Scan=false`, `AI Block Search Assist=false`, so they can only get a target over
IGC; ships start facing the enemy 500-900 m away; the Watcher does `lock` at 0.5 s, then `hunt`. "Win" = the Watcher's team won (the Watcher
cannot fire, so the receivers must kill).

Watcher on its default tag (HORNET), receivers blind:

| team (Watcher +) vs enemy | A wins before | after | median kill s before -> after |
|---|---:|---:|---|
| 3x Gnat vs Hornet | 30 | 30 | 14.8 -> 14.7 (already fed through the legacy `HORNET` feed) |
| 2x Hornet vs Aegis | 29 | 29 | 8.8 -> 8.8 (same tag) |
| Longbow S vs Aegis | 0 | 18 | - -> 5.5 |
| Mantis vs Aegis | 0 | 22 | - -> 8.3 |
| Basilisk vs Hornet | 21 | 25 | 16.0 -> 17.7 |
| Aegis vs Hornet | 0 | 30 | - -> 6.5 |
| Wasp, Lancer, Harrier (legacy feeds) | 28 / 30 / 29 | 29 / 30 / 29 | unchanged |
| 2x Stinger vs Hornet / Aegis (no gun; detonations per run) | 1.73 / 1.67 | 1.73 / 1.67 | unchanged (legacy feed) |

Watcher moved to a tag nobody listens to (`IGC Tag=SCOUTX`, so only FLEET can carry it), receivers blind:

| team (Watcher +) vs enemy | A wins before | after | Stinger detonations / run before -> after |
|---|---:|---:|---|
| 3x Gnat vs Hornet | 1 | 30 | |
| 2x Hornet vs Aegis | 0 | 29 | |
| Longbow S vs Aegis | 0 | 18 | |
| Mantis vs Aegis | 0 | 22 | |
| Wasp vs Lancer | 0 | 29 | |
| Aegis vs Hornet | 0 | 30 | |
| 2x Stinger vs Hornet | 0 | 0 (no gun) | 0.00 -> 1.73 |

Isolation checks, after-build only, same SCOUTX Watcher: different `Secret` on Watcher and receivers: the team behaves as before (Gnat 1 win,
Hornet 0, Longbow 0, Mantis 0, Stinger 0.00 detonations). Same `Secret=ABC` on both: identical to the plain FLEET numbers above (30 / 29 / 18 /
22, Stinger 1.73). `Channel=` empty on the Watcher: nobody receives anything.

Regression of the default (every armed drone also copies its shares to FLEET), same seeds before -> after, A wins / B wins / timeouts:
1v1 Basilisk-vs-Harrier 10/10/10 -> 10/10/10; 1v1 Lancer-vs-Wasp 21/1/8 -> same; 2v3 2xHornet-vs-3xWasp 15/4/11 -> same; swarm 6xGnat-vs-Wasp
0/24/6 -> same; 1v3 Aegis-vs-3xStinger 14/0/16 -> same; mixed Hornet+Longbow S vs Lancer+Mantis (100 seeds) 28/25/46 -> 27/22/51; mixed Hornet+Longbow
S vs Wasp+2xGnat (100 seeds) 66/0/34 -> 62/1/37; Wasp+2xGnat vs Lancer+Mantis (30 seeds) 0/21/9 -> 1/23/6; Watcher+3xGnat vs Lancer+Mantis (30 seeds) 0/21/9 -> 0/23/7. No script
exceptions in any run. The 100-seed mixed differences (2-4 wins) are inside the run-to-run noise (about +/-5 at n=100).

Caveat: the unarmed Watcher finds nothing by itself in the sim (no AI-block model for it, raycast search only, see `docs/sim-audit-1.md`
section 2), so the free-search `Watcher+3xGnat` scenarios do not change (vs Hornet 1/20/9 and vs Aegis 1/17/12 wins/losses/timeouts, 30 seeds,
identical in both builds). The numbers above therefore use a pilot `lock` to give the Watcher a target.

## 7. Adding the block to a script (checklist)

1. `tools/fleet_sync.py --apply` inserts `tools/fleet_block.cs` before `public Program()` (and refreshes it when the block changes).
2. After `LoadConfig();` in the constructor and in the `reload` command: `FleetLoad(true);` (carrier relays: `FleetLoad(false);`).
3. In `ReadIGC()`: drain every listener of `FleetTgtL` with the same handler as `_lTgt` (skip the message when `!FleetOk(d)`), then
   `FleetPump(Command, p => { _dynHome = p; _dynHomeT = _now; });` (Stinger: `FleetPump(Command, p => { });`).
4. Next to each `IGC.SendBroadcastMessage(Tag + "_TGT", tup)` (share, and the radius -1 finished message): `FleetSend(tup);`.
5. `python3 -I tools/fleet_sync.py --check`, `bash tools/compile_check.sh`, `build_all.py --only "<Model>"`.

No other behaviour of the scripts was changed. Printers (`HiveMotherAI`) are not on the fleet channel yet.

## 8. Open points

* Real game: IGC range is the antenna broadcast range of each ship (small drone antennas are 500-5,000 m); the sim models the same
  4 kW/km rule but enemy teams cannot hear each other there. In the real game they can, hence the secret.
* A command sent on FLEET reaches every type; a type that does not know a word ignores it. `lock` is deliberately not allowed.
* Message load: each sharing drone now sends its two target messages per second on two tags instead of one; listeners hold at most the game's queue
  length and drop the oldest first.

## 9. Launch protocol (HiveMotherAI v2.1 -> freshly printed drone)

Take-off must not depend on one message. The printer uses three independent paths; any one of them is enough.

1. **Arming (printer grid, before separation).** In the `ARM` state the printer still shares a grid with the drone, so it can see the drone PB
   through `GridTerminalSystem`. It merges this into the drone PB's Custom Data (other sections untouched) and then calls `DPb.TryRun("armed")`
   (retried each tick for up to `Arm Max Seconds`, 3 s, because the PB may still be recompiling):

   ```
   [Launch]
   Auto=true              ; false in Rack Mode: the drone waits for the "launch" command instead
   Burst Seconds=4        ; how long to burn away from the printer (bay key "Launch Burst Seconds")
   Away X=...             ; world vector away from the printer, written at release (zero = unknown)
   Away Y=...
   Away Z=...
   Exit Direction=Left    ; printer axis the drone leaves along (bay key "Exit Direction"), blank = straight away
   Armed=true             ; set by the printer; the drone clears it when it takes off
   ```
   The drone script reads `[Launch]` on the argument `armed`, and also in its constructor (after a recompile). Armed + Auto means: as soon as
   the grid is no longer the printer's (merge off), burn along Away for Burst Seconds, then start the normal AI.
2. **`<TAG>_JOIN`** (string `"join"`) on the per-type tag **and** `<ch>_CMD` string `"join"` on the FLEET channel (`Fleet Channel`, `Fleet Secret`
   in the printer's `[Hive Mother]` section). Sent every 2 s for `Join Seconds` (default 40, minimum 30) after the separation is confirmed, and
   again (one more send, window extended to now+6 s) whenever a `_SWARM` message from a not yet known drone id arrives on that tag.
   Drones must accept `join` over FLEET (add it to `Allowed Commands`, the default list does not contain it).
3. Rack mode: the `<Launch Tag>_CMD` string `"launch"` is sent 3 times (every 2 s) after the separation instead of once at release.

Also done by the printer at separation: drone batteries are set to `Auto` charge mode, the drone PB is `Enabled`.
