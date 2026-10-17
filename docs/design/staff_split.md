# Staff split: a fourth PB, `[Aux]`, for the repair client and the seeker

`docs/design/staff_split.md`. Planned 2026-10-17 on the uncommitted survivability working tree. **Implemented 2026-10-17** (blocks, scripts, designs, tests, sim harness; release zips not rebuilt). Implementation notes:
- Measured sizes: the 7 Staff PBs 72.64-72.71k, Gnat 73.8k, Basilisk 73.5k, AuxAI 19.4k (section 8 estimated 72.0k / 17k).
- SURVIVAL STAFF `DkRel()` also guards on `_dkOn` (set by `DkGo`): a release with nothing taken writes nothing (no stray `DampenersOverride` and no 2-tick FOL bit 2 on an abort from state 1, "bay did not answer"). AUX LINK's run-gap path clears `_dkOn` without a write.
- REPAIR CLIENT v1.1 calls `DkRel()` directly instead of keeping an `RpRel()` wrapper.
- AuxAI ignores SDK bit 0 for the 2 s it holds DKS bit 3 (REPAIRED), so a DAMAGED bit in flight cannot restart a request; the seeker runs only while the link is up.
- Placement: Mantis's Aux PB lands at (-2,-3,-2) (the dry run's (0,-2,1) lies behind the spine, which the Foundry welders now need: `extra.mantis` passes `avoid=core.spine_weld_cells(bp)`); Longbow S is pinned at `core.LONGBOW_S_AUX_CELL` = (1,-5,-1) (`add_support_pbs(..., aux_cell=)`): the free choice (-1,-3,-2) took the self-projector's cell, the survival kit moved and the Longbow Foundry lost reach to the gyro and the up camera; (1,-5,-1) leaves the kit unchanged and the Foundry passes. Welder reach checked (reach.verify_printer): Hornet Forge, Wasp / Lancer / Longbow Foundry, Harrier Forge, Aegis Shipyard, Watcher Pad, Fleet Forge, Escort Carrier PASS; Mantis Foundry fails with the same three blocks as before the Aux.
- `dock-launch --return-at S` ends at CONTACT: the sim has no merge re-lock in flight, so "docked" cannot be seen there; `--events` takes `!disable-pb` / `!enable-pb` only.

## 0. Decision in one paragraph

Seven Staff PBs (Hornet/Watcher, Harrier, Lancer, Aegis, Wasp, Longbow, Mantis) are at 82.9-83.0k minified. Each of those seven drones gets a fourth PB, **`[Aux] <Model> PB`**, which runs **one shared, model-independent script, `scripts/AuxAI.cs`**. Two blocks leave their Staff PBs and go to it:
- **REPAIR CLIENT**: the whole block.
- **The seeker turret reader**: the SEEKER part of STAFF SEARCH.

The Staff keeps a thin **AUX LINK** block in their place. The Aux PB **writes no thruster or gyro override at all**. During a dock approach it only computes the setpoints (velocity, nose, roof) and sends them to the Staff every tick. The Staff flies them with the `SvSteer` it already has for limp-home. So the override-writer set stays exactly what it is today: Flight, or Staff under FOL bit 2.

Result: the seven Staff PBs come down to about 72.0k. The Gnat (73.5k) and the Basilisk (73.2k) are already under 75k and keep both blocks in their Staff PB. The Gnat could not take a fourth PB anyway: there is no free cell inside its 2 x 2 section. Flight scripts: no change.

## 1. Measured sizes (minified, `tools/minify_cs.py`)

| Item | chars |
|---|---|
| Hornet / Harrier / Lancer / Aegis / Wasp Staff | 82,935-82,956 |
| Longbow / Mantis Staff | 83,001 / 82,993 |
| Gnat / Basilisk Staff | 73,476 / 73,219 |
| REPAIR CLIENT `tools/rc_block.cs` | 10,899 |
| STAFF SEARCH `tools/ss_block.cs` | 8,597, of which the seeker is 2,081 (`SsSeek`) + 505 (turret/camera setup in `SsBlocks`) + 134 (fields) + about 60 (status, call) ≈ 2.8k |
| SURVIVAL STAFF `tools/svs_block.cs` | 25,038: threat memory 2.1k, missiles 1.9k, via/standoff/gather/blind side 4.7k, config 3.2k, `SvSteer` 1.4k, `SvWatch` 1.1k, `SvStatus` 1.1k |
| Staff `Display()` | 1.65k |
| Draft AUX LINK (Staff side, section 4) | 1,872 |
| Draft `DkGo/DkSteer/DkRel/SvRepaired` (into SVS) | 503 |
| Draft `.SKW` reader + seeker stubs | about 350 |

## 2. Candidates

| Candidate | Frees (net) | Coupling | Hot path? | Verdict |
|---|---|---|---|---|
| **REPAIR CLIENT** (bay dock controller, medic patient side, self-projector) | 10.9 − 1.9 − 0.5 = **≈ 8.5k** | Inside the Staff: about 10 SVS fields and functions. Outside: antenna-range channels any PB can use. Overrides: under FOL bit 2. | No. It only runs when damaged, at home, or on the words `repair`/`dock`. | **Move** |
| **Seeker** (part of STAFF SEARCH) | 2.8 − 0.35 = **≈ 2.45k** | One call into WEAKPOINT RELAY, `WpSeek(tuple)`. Needs `_tgtId`, `SeekerOn`, `SeekerGroup`. | No. It samples at 2 Hz and its hole stays valid for 3 s. | **Move (for margin)** |
| Patrol (STAFF SEARCH) | ≈ 2.9k | Mates rank (SWARM), home (SVF), mode, AI-moving bit, and `.PAT` to the Flight | Mostly not (HUNT with no target) | Keep: needs more mirrored state than the seeker |
| Sensors / feeds | 2.0 / 0.25k | Sensors also feed FOLLOW (`PollSensors`). Feeds go into the Staff's `.TGT` relay. | No | Keep: a second `.TGT` sender, not worth 2k |
| Display (`Display` + `SvStatus` + other statuses) | ≈ 3k | Needs nearly the whole Staff state mirrored | No | Reject |
| Threat memory | ≈ 2k | Read by standoff, via, blind side, SVS class, last gasp | Feeds SVS at 5 Hz | Reject |

REPAIR CLIENT alone gives about **74.4k**: ≤ 75k, but only 0.6k of margin. Adding the seeker gives about **72.0k** (3k under 75k, 8k under the 80k budget). Both go to the same PB with the same link, sync and re-bind machinery, so the second module adds almost no risk. The PB is named `[Aux]` rather than `[Dock]` because it holds more than docking. It is also the documented home for the next off-hot-path Staff module.

**Why both are off the hot path.**
- The dock controller is not combat. It runs only with the damage latch set or on a `repair`/`dock` word, in RTB or IDLE, never under fire. The control loop itself (`SvSteer`: fresh attitude and velocity feedback every tick) stays in one PB, the Staff. Only the setpoint is one tick old. That is 5 mm at the 0.3 m/s insert speed, and the lateral PD gain is 1/s.
- The medic side is 1 Hz.
- The seeker samples at 2 Hz and its hole is kept 3 s.

## 3. What moves, what stays

### 3.1 New and changed shared blocks (`tools/blocks.json`)

| Block | File | Scripts | Change |
|---|---|---|---|
| REPAIR CLIENT v1.1 | `tools/rc_block.cs` | **GnatStaffAI, BasiliskStaffAI, AuxAI** (removed from the other 7 Staff scripts) | The actuator writes become host hooks (section 3.2). Otherwise unchanged. |
| SEEKER v1.0 (new, cut out of STAFF SEARCH) | `tools/sk_block.cs` | **GnatStaffAI, BasiliskStaffAI, AuxAI** | Takes the `SsSeek` body (renamed `SkStep`), the seeker part of `SsBlocks` (renamed `SkBlocks`: cameras `EnableRaycast`, the `[Seeker]` turret list, `SetTargetingGroup`), the fields `_skT`, `_skCams` (was `_ssCams`), `_skT0`, `_skLog`, and `SkStatus()` (the `Seek :` line). Uses: `_tgtId, _rc, _now, Me, SeekerOn, SeekerGroup, WpSeek()`. |
| STAFF SEARCH v1.1 | `tools/ss_block.cs` | the 9 Staff scripts | The seeker code is removed. It keeps the `Seeker Group` / `Seeker Turret Guides Aim` keys (`SeekerOn`, `SeekerGroup`, so the Staff still writes their defaults), and calls `SkBlocks()` at the end of `SsBlocks`, `SkStep()` in `SsStep`, and `SkStatus()` at the start of `SsStatus`. |
| SURVIVAL STAFF v1.1 | `tools/svs_block.cs` | the 9 Staff scripts | Adds the dock actuator (section 3.2) and the header "Uses" list. `SvWatch`, `SvSteer`, `SvSend` and every other function are unchanged. |
| AUX LINK v1.0 (new) | `tools/al_block.cs` | the 7 split Staff scripts (Hornet, Harrier, Lancer, Aegis, Wasp, Longbow, Mantis) | Implements the API that SVS and STAFF SEARCH call on REPAIR CLIENT and SEEKER (`RpCfg RpListen RpBlocks RpRead RpStep RpStatus RpStop RpWant SvFly RpMedCh SkBlocks SkStep SkStatus`) as IGC proxies (section 4). |

So `svs_block` and `ss_block` stay identical in all 9 Staff scripts. Each of the two host types gets the same function names: the real blocks in Gnat/Basilisk, the AUX LINK proxies in the other 7. AuxAI gets REPAIR CLIENT + SEEKER plus host code that mirrors the Staff state they read.

Steps:
- Delete the old REPAIR CLIENT and seeker text from the 7 Staff scripts by hand: `blocks_sync` inserts and replaces blocks but does not delete them.
- Then run `blocks_sync.py --apply`, then `--check`. `--check` reports UNLISTED if a stale marker is left.

### 3.2 Host hooks (the one-writer seam)

**REPAIR CLIENT v1.1 edits** (all inside `rc_block.cs`):
- ACK branch of `RpRead`: replace `_rpThr.Clear(); _rpThr.AddRange(_thr); _rpGy.Clear(); _rpGy.AddRange(_gyros);` and `_rpFlyTk = _tick` with `DkGo();`.
- `RpRel()`: its body becomes `DkRel();`.
- Delete `if (_tick - _rpFlyTk < 3) return;` (the gate moves into `DkSteer`).
- `SvSteer(...)`: the three calls become `DkSteer(...)`.
- `_svRtb = false;`: the two places (bay release, medic done) become `SvRepaired();`.
- Fields `_rpThr`, `_rpGy`, `_rpFlyTk` are removed.
- `bool SvFly { get { return _rpFly || DkHold; } }`.

**SURVIVAL STAFF v1.1 adds the Staff-side actuator** (used by local REPAIR CLIENT in Gnat/Basilisk and by AUX LINK in the 7):

```
List<IMyThrust> _dkThr = ...; List<IMyGyro> _dkGy = ...; long _dkTk, _dkRelTk = -9;
bool DkHold { get { return _tick - _dkRelTk < 2; } }    // FOL bit 2 stays 2 ticks after the release write
void DkGo() { snapshot _thr/_gyros (own blocks; once merged the lists hold the host's too); _dkTk = _tick; }
void DkSteer(Vector3D v, Vector3D f, Vector3D u) { if (_tick - _dkTk >= 3) SvSteer(v, f, u); }   // FOL bit 2 reaches the Flight first
void DkRel() { _dkRelTk = _tick; if (_down) return;    // Flight down: SvWatch already released / limp-home owns the actuators
               zero _dkGy/_dkThr overrides, _rc.DampenersOverride = true; }
void SvRepaired() { _svRtb = false; }
```

**AuxAI host defines the same names as proxies** and never touches an override, `DampenersOverride` included:
- `DkGo()`: nothing.
- `DkSteer(v,f,u)`: stores the setpoint.
- `DkRel()`: marks a DKS resend.
- `SvRepaired()`: clears the mirror and holds DKS bit 3 for 2 s.
- `Fwd(c)`: sends `K.S2F` straight to the Flight. That is the same semantics as the Staff's `Fwd` (Flight only, no Staff `Local`). It is used only for "resume the mode from before the damage" after a medic repair.
- `WpSeek(d)`: sends `.SKW` to the Staff.
- `SvB/SvD/SvT`: read-only getters on the Flight PB's `[Survival]` section, like the Flight's `SvLoad`.
- `FleetV`: copied from FLEET LINK.

### 3.3 `scripts/AuxAI.cs` (one shared script, about 17k)

**There are no per-model constants.** Everything that differs between models reaches the Aux from its Staff in `.SDK`:
- Tag, MaxSpeed and SeekerGroup.
- Mode, as two bits (IDLE, RTB). REPAIR CLIENT names only `Mode.IDLE` and `Mode.RTB`. The Aux declares `enum Mode { IDLE, GUARD, RTB }` and maps "neither" to GUARD. This works for any model, Gnat's enum included.
- `RpDockOk` and `SeekerOn`, as bits.

The RC keys live in the model-independent `[Survival]` section of the Flight PB, which the Aux reads with `Get` only. One file therefore serves all seven models, and blocks_sync keeps it identical.

**Layout.**
- Header (link, keys, failure behaviour).
- Mirror fields: `Tag, SeekerGroup, _svPrevVerb, SeekerOn, RpDockOk, _svRtb, _svDock, _down, _fl, _svAmin, _svCap, MaxSpeed, _tgtId, _mode`.
- The hooks above.
- `// >>> REPAIR CLIENT v1.1` and `// >>> SEEKER v1.0` blocks.
- `Program()`: `GetBlocks()`, `Update1`.
- `GetBlocks()`:
  - RC: the same rule as the Staff.
  - `_fpb` = the nearest `[Flight]` PB on its own grid, so `_fk = "WSPL"+id`.
  - `.SDK` listener.
  - `RpBlocks()`, `SkBlocks()`.
  - `RpCfg` from `_fpb.CustomData`.
- `Main`: try/catch with a screen message, like the Staff.
- `Tick`:
  1. A PB argument goes to the Staff on `.D2S`.
  2. `dt`, `_now`, `_tick`.
  3. Re-scan every 600 ticks, on a grid change, and every 60 ticks while the link is down.
  4. Every 60 ticks, re-parse `[Survival]` when the Flight's Custom Data text changed (covers `reload` and edits).
  5. Read `.SDK`:
     - apply the mirror;
     - a new Tag calls `RpListen()`;
     - a new seeker configuration calls `SkBlocks()`;
     - run the ops (section 4.2).
  6. `_down = bit2 || SDK older than 30 ticks || no SDK yet`.
  7. `RpRead()`.
  8. If the RC works: `RpStep(); SkStep();`.
  9. `SendDks()`.
  10. Every 30 ticks: the screen (`AUX PB v1.0 <Tag>`, Flight and Staff link lines, `RpStatus()`, `SkStatus()`).

The Aux PB needs no Custom Data.

### 3.4 What stays in the Staff

Everything else. Damage state and latch, limp caps, failover limp-home (`SvWatch`, `SvSteer`), threat memory, missiles, flares, gather, blind side, hand-off, patrol, sensors, feeds, the seeker keys, WEAKPOINT RELAY, FOLLOW, LAUNCH, FLEET LINK, the display.

The Staff still writes every `[Survival]` default. AUX LINK's `RpCfg` registers the five RC keys through `SvB/SvD/SvT`, and STAFF SEARCH registers the seeker keys. So the Flight PB's Custom Data still has one writer.

## 4. IGC (all `TransmissionDistance.CurrentConstruct`, key `K = "WSPL" + <Flight PB EntityId>`)

**Existing channels are unchanged.** The Flight sees nothing new: the same FOL bit 2 and the same `.S2F` words.

### 4.1 New channels

| Tag | From → to | Tuple | Rate |
|---|---|---|---|
| `.SDK` | Staff → Aux | `MyTuple<int flags, long tgtId, Vector3D aux, string cfg, string prevVerb, string ops>`. flags: bit0 DAMAGED latch (`_svRtb`), bit1 docked/welded (`_svDock`), bit2 FLIGHT DOWN (`_down`), bit3 Staff is steering a dock (`_dkFly`, screen only), bit4 under fire (`_fl & 16`), bit5 mode IDLE, bit6 mode RTB, bit7 SeekerOn, bit8 RpDockOk. aux = (`_svAmin`, `SvCapV()`, `MaxSpeed`). cfg = `Tag + "\n" + SeekerGroup`. prevVerb = `_svPrevVerb`. ops = queued repair words, one per line (`w1`/`w2` = `RpWant(k)`, `s<why>` = `RpStop(why)`), empty when none. | Sent from AUX LINK's `RpRead`, so every tick evaluated, with or without an RC. Every 10 ticks, and at once on a change of flags, `tgtId`, cfg or prevVerb, or when ops is not empty. Full resend: on a new DKS sender (new Aux PB), on a new key (`RpListen`), after a Staff run gap, on `SkBlocks()` (the `seeker on/off` word). Stale at the Aux after 30 ticks. |
| `.DKS` | Aux → Staff | `MyTuple<int flags, Vector3D vel, Vector3D fwd, Vector3D up, string status>`. flags: bit0 FLY (the Staff steers `SvSteer(vel, fwd, up)`, i.e. world velocity, nose, roof; zero = free), bit1 repair session active, bit3 REPAIRED (held 2 s after a repair completes; the Staff calls `SvRepaired()` while it is set, idempotent). status = `RpStatus() + SkStatus()` for the Staff screen. | Every tick while REPAIR CLIENT's `_rpFly` is set. Otherwise every 30 ticks and at once on a change of flags or status. Full at once after an Aux run gap and on the first SDK after the link was down. |
| `.SKW` | Aux → Staff | `MyTuple<long tgt, Vector3D s, Vector3D d, Vector3D info, long senderGrid, int flags>`: exactly the `WpSeek` argument (target-frame hull point, ray, hint; kind \| 8 \| 16). The Staff calls `WpSeek(d)`. | ≤ 2 Hz, while the seeker has a fresh finding |
| `.D2S` | Aux → Staff | `string`: a PB argument given to the Aux PB. The Staff runs `Command(arg)` (its `Local` + `Fwd` to the Flight). | on event |
| `.S2F` (existing tag, new sender) | Aux → Flight | `string`: only `Fwd(_svPrevVerb)` when a medic repair completes (today's REPAIR CLIENT call, same semantics) | on event |

Antenna-range repair channels (`<Tag>_RBAY/_RREQ/_RACK`, `<M>_AD/_REQ/_ACK/_WORK`, survivability.md 4.3) are unchanged. They are now sent and received by the Aux PB, through the drone's antenna as before, and no longer by the 7 Staff PBs.

### 4.2 AUX LINK (Staff side, about 1.9k + 0.35k)

- **`RpCfg(i)`:** registers the 5 key defaults and sets `RpMedCh`.
- **`RpListen()`** (called by `SvListen` on a key change): registers `K.DKS`, `K.D2S`, `K.SKW` and forces a full SDK.
- **`RpBlocks()`:** empty.
- **`RpRead()`:**
  - reads DKS (on bit 3: `SvRepaired()`; on a new `m.Source`: full SDK), `.D2S` (→ `Command`) and `.SKW` (→ `WpSeek`);
  - computes `f = FLY && last DKS ≤ 15 ticks old && !_down`. `f` rising calls `DkGo()`, `f` falling calls `DkRel()`, and `_dkFly = f`;
  - on a run gap (`Runtime.TimeSinceLastRun > 0.5 s`): `_dkFly = false` with no zeroing (the Flight may already fly), `_dkRelTk = _tick`, full SDK;
  - then the SDK send.
- **`RpStep()`:** `if (_dkFly) DkSteer(last vel, fwd, up);`.
- **`RpWant`/`RpStop`:** append to ops.
- **`RpStatus()`:** the DKS status while it is < 3 s old; otherwise `! Aux PB silent` once one has spoken; `! no [Aux] PB (repair, medic, seeker off)` when none has spoken after 5 s.
- **`SvFly`:** `_dkFly || DkHold`.
- **`SkBlocks()`:** full SDK. **`SkStep()`:** empty. **`SkStatus()`:** `""` (the seek line comes inside the DKS status).

The hooks are already in place (`RpRead` in `SvsRead`, `RpStep` in `SvWatch`, `SendFol(burn || SvFly)` in Tick). The Staff Tick code does not change.

### 4.3 Word paths

- A Staff word (PB argument, `Tag_CMD`, fleet command, Flight `.CMD`): `Local` → `SvWord` → `RpStop`/`RpWant` (proxy) → the ops line goes out in the same tick's SDK. The Aux applies the mirror first, then the ops (one message, atomic), so a stale DAMAGED bit can never restart a request after `fight`.
- An Aux argument: `.D2S` → Staff `Command` → the same path, plus `Fwd` to the Flight.

## 5. Override writers (update `docs/design/survivability.md` 1.3)

| Output | Normal | Take-off burn | Dock approach (item 17) | Flight PB down (item 5) | Docked or welded |
|---|---|---|---|---|---|
| Gyro and thruster overrides, `DampenersOverride` | Flight | Staff (FOL bit 2, `LkBurn`) | **Staff** (FOL bit 2; `DkSteer` flies the setpoints from the Aux PB's `.DKS`; Gnat/Basilisk: local REPAIR CLIENT, same `DkSteer`). **The Aux PB never writes.** | Staff limp-home, then nobody | nobody |
| Own projector `Enabled` | Aux PB (REPAIR CLIENT); Gnat/Basilisk Staff | same | same | same | bay script in REPAIR, else Aux / Staff |
| Own merge `Enabled` | LAUNCH (Staff) switches it off after a take-off | LAUNCH | Aux PB switches it on at ALIGN → INSERT | n/a | n/a |
| `[Seeker]` turret targeting group, seeker cameras `EnableRaycast` (always true) | Aux PB (SEEKER); Gnat/Basilisk Staff | same | same | same | same |
| Flare launchers, sensors, guns, AI blocks, railgun | unchanged | | | | |

**Hand-over timelines.** One-tick IGC hops are measured in the sim (`docs/audit/multi_pb_feasibility.md`). The sim writer audit counts different PBs writing in the same or consecutive ticks.

- **Acquire.**
  1. Tick t: the Aux gets RACK ok, sets `_rpFly`, computes the first setpoint, sends DKS FLY.
  2. Tick t+1: the Staff's `RpRead` sees FLY → `DkGo` (snapshot, `_dkTk`). `SendFol` sends bit 2 at once (a change).
  3. Tick t+2: the Flight reads FOL → `LkBurn` → no `Control`, guarded `Release`.
  4. Tick t+4: the Staff's first write. The Flight wrote last at t+1 at the latest, so 3 empty ticks.
- **Release** (CONTACT, abort, `fight`, mode word, timeout).
  1. Tick u: the Aux clears FLY and sends at once.
  2. Tick u+1: the Staff calls `DkRel` (zero, dampeners on, `_dkRelTk`). `SvFly` stays true through u+2 (`DkHold`).
  3. Tick u+3: FOL bit 2 cleared.
  4. Tick u+4: the Flight writes. At CONTACT, PRINTER GUARD holds the Flight instead.
- **Aux PB dies mid-approach.** The Staff keeps flying the last setpoint for ≤ 15 ticks, then `DkRel`, FOL clear 2 ticks later, the Flight resumes.
- **Staff dies mid-approach.** Nobody writes. The overrides stay as left until the Flight's `LkAlive` lapses (1 s), then the Flight flies. The Aux sees SDK stale after 30 ticks and aborts the session (`staff silent`). It writes nothing.
- **Flight dies mid-approach.** `SvWatch` sets `_down` after its 3-tick wait (dock flight is not `busy`): `SvRel`, AI off, guns off, maybe limp-home. The next tick `DkRel` skips the zeroing (`_down`) and `_dkFly` drops. SDK bit 2 makes the Aux abort. Every write comes from the Staff.
- **Flight returns during a dock.** It yields 6 ticks, then sees `LkBurn` (bit 2 set while the Staff steers) and stays off.
- **Staff run gap.** `_dkFly` drops without a write. If the Aux still sends FLY, `DkGo` re-runs with the 3-tick gate.

## 6. Sync

- **Start-up.**
  - The Aux binds `K` like the Staff (nearest `[Flight]` PB on its own grid). It sends DKS every 30 ticks from its first tick, so the Staff learns it exists and answers with a full SDK.
  - Until its first SDK the Aux is link-down: no requests, no seeker. That is at most one SDK period after the Staff runs.
  - The Staff behaves as without an Aux until DKS arrives. It shows `! no [Aux] PB` after 5 s.
- **Re-bind.**
  - Flight rebuilt (new `K`): the Staff re-keys (existing `GetBlocks`, 1 Hz while down) and AUX LINK re-registers in `RpListen`. The Aux sees SDK stale, re-scans at 1 Hz, finds the new Flight PB and registers `K'.SDK`. Gap about 1.5 s.
  - Staff rebuilt (same `K`): the Aux is link-down while SDK is stale (sessions in states 1-4 abort, no new requests). It resumes on the first SDK and resends a full DKS. The new Staff starts with its latch cleared.
  - Aux rebuilt: the Staff sees a new `m.Source` and sends a full SDK. An active dock flight is released by the 15-tick rule. A bay reservation lapses at the bay after 90 s; a medic gives up as today.
- **Run gaps.** Aux: full DKS next tick, the state machine carries on (as today). Staff: AUX LINK drops `_dkFly` without zeroing and sends a full SDK. The existing SVS and failover rules are unchanged.

## 7. Where the PB sits (outline rule)

### 7.1 `tools/designs/_common.py`

- `SUPPORT_AUX = {'HORNET','HARRIER','LANCER','AEGIS','WASP','LONGBOW','MANTIS'}`, keyed by the Flight script's default `Tag` like `SUPPORT_STAFF`, so the Watcher (HornetDroneAI) is included. `AUX_SCRIPT = 'AuxAI.cs'`.
- `free_pb_spot(bp, sub, near, name=..., inside=None, avoid=())`.
  - New `inside` argument: the candidates are limited to the strict bounding box (`kit_outline(bp)` taken before the PB is added). That keeps the PB inside the set outline; today's search may pick one cell outside the box.
  - New `avoid` argument, and `_dock_ok(bp, cell)` (keep merge faces clear).
  - The defaults keep today's behaviour, so the Staff and Thrust placements of every other ship do not move.
- `add_support_pbs(bp, avoid=())`: **after** the `[Thrust]` step (so existing PB cells do not move), if the Flight's Tag is in `SUPPORT_AUX` and no `[Aux]` exists:
  - place `[Aux] <base> PB` with `free_pb_spot(bp, sub, flight['min'], inside=kit_outline(bp), avoid=avoid)`;
  - assert a cell was found (`'<model>: no free cell inside the outline for the Aux PB'`).
  - Idempotent like the rest. `build_all`'s top-level call also covers the Wasp Split test ship.
- `core.longbow_s`: `add_support_pbs(..., avoid=LONGBOW_S_WELD_CELLS)`, because the two cells emptied for the Foundry welders must stay empty.

The order stays add_support_pbs → `bury_swap` → `add_survival_kit`. So the self-projection ghost holds the Aux PB. `bury_swap` may trade the Aux into a buried armour cell; the occupied cells do not change, so welder reach is unaffected.

### 7.2 Dry run (in memory, this rule, before `bury_swap`)

| Model | Aux cell (fwd, up) | Flight PB | Distance to its Flight PB (cells) | Kit skipped |
|---|---|---|---|---|
| Hornet | (-1,0,-4) Backward/Up | (-1,0,-1) | 3 | none |
| Harrier | (-1,0,-4) | (-1,0,-1) | 3 | none |
| Lancer | (-1,0,-4) | (-1,0,-1) | 3 | none |
| Wasp | (-1,0,-3) | (-1,0,-1) | 2 | none |
| Watcher | (-2,0,-1) | (0,1,-2) | 4 | none |
| Aegis | (1,1,-4) | (0,1,-2) | 3 | none |
| Longbow S | (-1,-3,-2) (weld cells avoided) | (0,-2,-2) | 2 | none |
| Longbow L / L Seeker (large) | (-1,0,-2) Forward/Up | (0,-2,-2) | 3 | none |
| Mantis | (0,-2,1) Down/Forward | (0,-2,-2) | 3 | none |
| **Gnat** | **no free cell**: the 2 x 2 section has one free cell and a PB does not mount there (`gnat_survival` already skips decoy 2, the flare, the projector and gyro 2 for lack of cells) | | | |
| Basilisk / Seeker | not needed (73.2k). Free validated cells exist if it ever grows, e.g. (-3,-1,1), (-2,-1,0), (-2,1,-1). | | | |

- Every kit placement still succeeded (`kit['skipped']` empty).
- **Hornet Forge welder reach passed** with the Aux at (-1,0,-4) (`reach.verify_printer`, 193 s).
- The other printers (Wasp/Lancer/Longbow/Mantis Foundry, Harrier Forge, Aegis Shipyard, Watcher Pad, Fleet Forge, Escort Carrier) must pass `python3 -I tools/designs/printers.py` and `tools/designs/reach.py`. If one fails, pin that model's Aux cell by hand, as `longbow_s` does for its Thrust PB.
- The Aux PB sits 2-4 cells from its own Flight PB, so the nearest-Flight binding is right even with drones side by side at a printer.

### 7.3 PCU, mass, cost

| Model | PCU now → after | Mass kg | Min accel m/s² now → after |
|---|---|---|---|
| Hornet | 1469 → 1569 | 5704 → 5842 | 5.05 → 4.93 |
| Harrier | 1493 → 1593 | 5946 → 6084 | 4.84 → 4.73 |
| Lancer | 1469 → 1569 | 5768 → 5906 | 4.99 → 4.87 |
| Aegis | 1629 → 1729 | 6000 → 6138 | 4.80 → 4.69 |
| Wasp | 2159 → 2259 | 5860 → 5998 | 4.92 → 4.81 |
| Watcher | 1410 → 1510 | 5590 → 5728 | 5.15 → 5.03 |
| Longbow S | 1688 → 1788 | 11012 → 11150 | 5.23 → 5.17 |
| Mantis | 1727 → 1827 | 11362 → 11500 | 5.07 → 5.01 |
| Longbow L | 1853 → 1953 | +534 kg on 275 t | 7.54 (no change) |

- Each Aux PB is 100 PCU and 138.4 kg small / 534.4 kg large; power is within the small-load allowance.
- Every model stays far above the 1.0 m/s² design floor.
- Blueprint cost goes up by one PB; the self-projection ghost carries the extra program (about 17k chars).

## 8. Sizes after

| Script | After (estimate) |
|---|---|
| Hornet, Harrier, Lancer, Aegis, Wasp, Longbow, Mantis Staff | 82.95 − 10.90 (RC) + 1.87 (AUX LINK) + 0.50 (SVS actuator) − 2.80 (seeker) + 0.35 (SKW reader, stubs) ≈ **72.0k** |
| Gnat / Basilisk Staff | ≈ 73.6k / 73.4k (+ about 0.15k for the hook rework; still ≤ 75k, ~6.5k under the 80k budget) |
| AuxAI | ≈ 17k (RC 10.6k + SEEKER 2.8k + host 3.5k) |
| Flight scripts, ThrustVectorAI | unchanged |

If a later change needs room in the Gnat Staff: it cannot take another PB without leaving its section, so that needs an owner decision (CLAUDE.md outline rule). Basilisk can take the Aux PB any time: switch its blocks to AUX LINK and add BASILISK to `SUPPORT_AUX`.

## 9. Failure behaviour (add to survivability.md 1.4)

- **Aux PB missing or dead.** Lost: bay docking, medic calls, self-projector control and the seeker. The `Seeker Group` / `Seeker Turret Guides Aim` keys still exist but nothing reads the turret. Everything else works. The Staff screen says `! Aux PB silent` or `! no [Aux] PB (repair, medic, seeker off)`. An in-game drone whose Staff script is updated without adding the PB loses exactly these, so the Staff header and README must say to paste `AuxAI.cs` into a PB named `[Aux] ...`.
- **Staff dead.** The Aux shows `STAFF PB SILENT`, sends no requests and aborts any session in states 1-4. A medic session in progress keeps broadcasting WORK, as today. Its seeker output has no consumer.
- **Flight dead.** Unchanged (item 5): the Aux aborts flight through SDK bit 2.

## 10. Tests

1. `tools/compile_check.sh`: all scripts, AuxAI included.
2. `tools/minify_check.sh`: same IL; AuxAI under the 100k default.
3. `python3 -I tools/blocks_sync.py --check`: REPAIR CLIENT and SEEKER in Gnat, Basilisk and AuxAI; AUX LINK in the 7; STAFF SEARCH and SURVIVAL STAFF in the 9; no UNLISTED.
4. **New `tools/tests/test_aux_split.py`** (the sync checks, Wasp Split style):
   - **(a) Tuple shapes.** The `MyTuple<...>` type text of `.SDK`, `.DKS`, `.SKW` must match exactly between `al_block.cs` and `AuxAI.cs` (regex on send and receive sites).
   - **(b) What moved.**
     - The 7 Staff scripts: no `void RpScan(`, `void SkStep()` body or `_rpBays`; they do have `AUX LINK`.
     - AuxAI: has `REPAIR CLIENT v1.1` and `SEEKER v1.0`, has no `ThrustOverride` or `GyroOverride =` outside the hook names, and has no `DampenersOverride` write.
   - **(c) Sizes.** Each of the 7 Staffs ≤ 75,000 (target 73,000); Gnat and Basilisk Staff ≤ 75,000; AuxAI ≤ 30,000.
   - **(d) Designs.**
     - The 7 models plus Watcher, Longbow L and L Seeker carry exactly one `[Aux]` PB, inside `kit_outline`, nearest to their own `[Flight]` PB (≤ 4 cells), with `kit['skipped']` unchanged.
     - Gnat has exactly `[Flight]` + `[Staff]`; Basilisk has 3 PBs.
5. Update the existing tests:
   - `tools/tests/test_pb_count.py`: printed Wasp, Aegis, Mantis and Longbow S carry `[Aux]`.
   - `test_survival_kit.py`: the ghost cells still match.
   - `test_survivability_audit.py`.
   - `golden_hashes.json`: the 7 models change.
   - `run_all.py`.
6. Printers: `python3 -I tools/designs/printers.py`, `tools/designs/reach.py`, `test_printer_lanes.py`, `test_printer_conveyors.py`.
7. Build for the sim: `python3 -I tools/build_all.py --no-zip --only Hornet Harrier Lancer Aegis Wasp Watcher "Longbow S" "Longbow L Seeker" Mantis Gnat Basilisk "Basilisk Seeker"`. Then `sim/build.sh` (compiles `scripts/*.cs`, AuxAI included) and `SimHost.exe selftest`.
8. Sim harness touch points (small; check with the F-package owner):
   - `DockLaunch.PbRank`: `[Aux]` ranks 0, like `[Thrust]`.
   - `SelfTestM4` flight pick: exclude `[Aux]`.
   - **New `dock-launch --return-at S`:** at S the harness plays the bay. It broadcasts `<Tag>_RBAY` for the dummy printer's merge (`clampPos`, `axis` = merge Up, `dockFwd`/`dockUp` from the printed pose) every 2 s, answers `_RREQ` with `_RACK ok`, and sends the `dock` word. PASS: the merge locks again, then `docked`, writer audit gyro 0 / thrust 0.
9. Sim runs (seed counts chosen: the goal is "it runs, no exception, writer audit 0", not strength):
   - `follow-test --model Hornet --count 3` (paths cruise, fight): numbers within 1 m of before.
   - `dock-launch --model Hornet`, `--model Wasp`, `--model "Longbow S"`: left in about 4.8 s, GUARD at 5 s, as before.
   - `dock-launch --model Hornet --return-at 20` plus three variants:
     - `--events "26:!disable-pb Aux;32:!enable-pb Aux"`: the Staff releases within 15 ticks and the Flight resumes; the dock restarts after the Aux returns.
     - `"26:!disable-pb Staff;30:!enable-pb Staff"`: the Aux aborts and the Flight takes over after 1 s.
     - `"26:!disable-pb Flight;32:!enable-pb Flight"`: dock aborted, limp-home, then the item 5 return.
     - Each one 1 seed, writer audit 0 in all.
   - `one --scenario "pirate/Hornet-vs-Pirate" --seed-index 0..2`, 3 seeds each:
     - `--events "20:!disable-pb Aux;35:!enable-pb Aux"` and `"30:!disable-pb Flight;45:!enable-pb Flight"`: no script error, writer audit 0;
     - `--echo-every 10`: the Staff screen shows the Aux status lines.
   - Seeker: `one` with Longbow L Seeker (and Basilisk Seeker for the local path), 2 seeds, `--echo-every 5`:
     - the Staff screen shows `Seek :` relayed from the Aux;
     - the Flight trace shows `.WPT` flag 4096 holes.
   - Regression: `run --filter "1v1/(Hornet|Wasp|Mantis|Longbow S)-vs-Hornet" --seeds 5`: no exceptions, outcomes in line with the last run.

## 11. Work order

1. **Blocks.**
   - Cut SEEKER out of `ss_block.cs` into `sk_block.cs` (v1.0); STAFF SEARCH becomes v1.1.
   - `rc_block.cs` v1.1 (hooks); `svs_block.cs` v1.1 (actuator).
   - New `al_block.cs`.
   - `blocks.json` entries.
2. **Scripts.**
   - Remove REPAIR CLIENT and the seeker from the 7 Staff scripts by hand.
   - Add the AUX LINK anchor; `blocks_sync --apply`.
   - Update the Staff headers ("REPAIR CLIENT and seeker run in the `[Aux]` PB, `scripts/AuxAI.cs`").
   - Write `scripts/AuxAI.cs`.
3. **Designs.** `_common.py` (`SUPPORT_AUX`, `free_pb_spot inside/avoid`, `add_support_pbs avoid`), `core.longbow_s`.
4. **Tests and harness** (section 10).
5. **Docs.**
   - This file.
   - survivability.md: 1.1 (what runs where), 1.2 (blocks), 1.3 (writer table above), 1.4, 4.1 (pointer to section 4 here), 5.2 (real sizes).
   - `docs/retrofit.md`: a **Role-specific** row ("[Aux] PB for the 7 split Staffs: PB budget, not a feature; Basilisk may join when it grows; Gnat has no cell").
   - The README/MODELS script table gets `AuxAI.cs` the next time zips are built.
6. Commit to `main`. Say the zips are not rebuilt.

## 12. Risks

- **The real-game IGC latency and drop rate are unmeasured.** The 15-tick setpoint hold absorbs short stalls. A longer Aux stall gives a release and re-acquire (3-tick gate), never two writers.
- **The nearest-`[Flight]` binding** at printers with merged drones: same rule and same risk as the Staff today. The Aux PB is placed 2-4 cells from its own Flight PB.
- **The dock approach is not verified in the game** (survivability.md item 17). The split adds a 1-tick setpoint delay, negligible at dock speeds.
- **Two hosts for REPAIR CLIENT and SEEKER** (Gnat/Basilisk Staff and AuxAI): kept honest by blocks_sync and the hook-name contract in each block header.

### Critical files for implementation
- /home/user/space-engineers-drones/tools/rc_block.cs
- /home/user/space-engineers-drones/tools/svs_block.cs
- /home/user/space-engineers-drones/tools/ss_block.cs (SEEKER cut out into new tools/sk_block.cs; new tools/al_block.cs, new scripts/AuxAI.cs)
- /home/user/space-engineers-drones/tools/designs/_common.py (add_support_pbs, free_pb_spot, SUPPORT_AUX) and tools/designs/core.py (longbow_s avoid)
- /home/user/space-engineers-drones/tools/blocks.json and /home/user/space-engineers-drones/sim/src/Run/DockLaunch.cs (PbRank, `--return-at`)
