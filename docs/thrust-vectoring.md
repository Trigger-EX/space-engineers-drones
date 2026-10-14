# Thrust vectoring: fly on with a lost thrust direction

A drone that loses a whole thrust direction (every thruster that pushed it forward is shot off, ...) keeps going where it wants to go: it turns the ship so a
SURVIVING thruster group points along the force it needs, as the Gnat does on purpose, and keeps the guns as near the target as that allows.
Intact ships fly exactly as before.

## Parts

| Part | File | Where |
|---|---|---|
| Thrust Vector PB | `scripts/ThrustVectorAI.cs` (about 7k minified) | one small `[Thrust] <Model> PB` on every drone / ship whose flight script has the link |
| THRUST VECTOR LINK block | `tools/tv_link_block.cs`, synced by `tools/thrust_sync.py` | in the flight script (about 1k minified): `TvAim(...)` first in `Aim()`, `_tvN = forceWorld;` in `ApplyThrust()`, `_tvD = distance to target;` in the aiming section |
| SPLIT LINK block + Staff PB | `tools/lk_block.cs` (`tools/split_sync.py`), `scripts/<Drone>StaffAI.cs` (made by `tools/make_staff.py`) | Aegis, Mantis, Longbow S/L and Wasp: the flight script was at the 100,000 character cap, so FLEET LINK, FOLLOW, LAUNCH and the status screen live in a `[Staff] ...` PB (the Wasp Split design, `docs/design/wasp_split.md`), the flight PB is `[Flight] ...` |
| Sim | `!kill-thrust <F\|B\|L\|R\|U\|D> [N]` | `--events "T:!kill-thrust F;..."` on `run` / `one` (team `--event-team`, default 0) and on `follow-test`; `follow-test --flight-custom-data "..."` |

`tools/designs/_common.py: add_support_pbs()` (called by `tools/build_all.py` for every model, Lite too) finds a free cell on the hull, inside it where it can, for the extra PBs.

## How it works

1. The flight script sends the Thrust PB `TVEC.I` (IGC, `CurrentConstruct`, no antenna): the force it asks of the thrusters, the attitude it would fly (forward, up),
   its aim block's axes, the thrust power scale and the distance to its target. 1 Hz while all is well, 20 Hz while a group is lost.
2. The Thrust PB groups the thrusters on the controller's six axes (as the flight scripts do) and keeps the best working thrust it ever saw per group on THIS grid
   (thrusters of a printer or carrier never count). A group is LOST when it works at less than `Lost Fraction` (0.5) of that. Shot-off thrusters and thrusters on a
   broken-off piece (`Closed`, not the same construct) make it rescan (1 Hz check).
3. With a group lost and a force over `Min Force` (0.3 m/s2) asked for, it scores "plain attitude" against "line group k up with the force" for each surviving k:
   thrust shortfall (against what the strongest group alone could give) + `Aim Weight` x aim error (full weight inside `Fight Range` = 1200 m of a target, 0.2 outside,
   0 when falling in gravity) + a small turn cost. Group k's push direction makes a fixed angle with the gun forward, so the new forward is the point of that cone round
   the force nearest to the wanted forward; the roll follows from the same two directions. The choice is held `Hold Time` (1 s) with `Hysteresis` (0.1).
4. The answer (`TVEC.O<flight PB address>`: forward, up, lost mask, chosen group) is flown by `Aim()` for 0.5 s at a time; feed-forward is zeroed. No answer (intact
   ship, Thrust PB gone, link down) means the script flies its own attitude.

Status: the Thrust PB screen (`THRUST LOST F  vectoring R  short 17%`). Custom Data `[Thrust Vectoring]`: `Enabled`, `Lost Fraction`, `Aim Weight`, `Hold Time (s)`,
`Hysteresis`, `Min Force (m/s2)`, `Fight Range (m)`; `reload` re-reads, `reset` forgets the rated thrust.

## Notes

* One writer per override: only the flight PB writes thrusters and gyros; the Thrust PB only advises. A PB-to-PB hop is 2 ticks each way, which is fine for an
  attitude choice held for a second; the hot path (tracking, lead, aim, control, firing) stays in the flight PB.
* Extra PBs cost mass (about 140 kg each on a small grid). Sim 1v1 Aegis-vs-Hornet, 300 seeds: 191 wins before, 160 with the Staff and Thrust PBs added
  (the fight is chaotic; the weight and a 3 % acceleration loss are the only measured causes: an empty Staff PB gives the same numbers as the real one).
* Not run in the game. Release zips are not rebuilt.
