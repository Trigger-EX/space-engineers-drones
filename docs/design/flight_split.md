# Flight split: off-hot-path modules leave the 9 drone Flight PBs and the ShrikeAI Helm

`docs/design/flight_split.md`. Planned 2026-10-18 at 9027e80, read-only. Pattern: `docs/design/wasp_split.md`, `staff_split.md`, `helm_split.md`. Rules: CLAUDE.md "Script size". Owner, 2026-10-18: "always split PBs if they outgrow their space".

**Status 2026-10-18: phase 2 (integration) done, see "Phase 2 results" at the end of section 9.** Gnat runs G1a (lead decision, owner's 'always split' rule; G2 did not fit: Staff 80,385 / Flight 80,634 minified). P0 done (see "P0 handover"); phase 1 under way; P2-G (Gnat G1a) done, section 7.2. P2-G2 done: the Gnat's damage sensing and RAID hit estimator moved to GnatStaffAI (Flight 74,409 / Staff 70,671 minified; sections 4.1, 7.2).

## P0 handover (read first in every phase-1 package)

P0 split KAMIKAZE DEFENCE and AI GATE into core + sub-blocks, behaviour-preserving: every old carrier carries all of its sub-blocks, the sorted IL of the 12 scripts is identical to before (only member order moved), minified sizes went down by 3-5 characters (merged field declarations). Every script compiles; `blocks_sync.py --check` passes.

**How the host lists work during phase 1** (`tools/blocks.json`, `tools/blocks_sync.py`):
- `scripts` is already the **final** host list of every block (section 3).
- `leaving`: scripts that still carry the block until their phase-1 package deletes it **by hand**. They are never UNLISTED; `--apply` never writes them; a stale copy is only a `note`.
- `arriving`: final hosts that do not carry the block yet. Absent is `pending`, not a problem. `--apply` inserts it only when the script is named with `--in`.
- **Every phase-1 apply names its own scripts:** `python3 -I tools/blocks_sync.py --apply --only "<BLOCK>" --in <Script>[,<Script>]`. Without `--in`, an apply also writes the final hosts other packages own (example: `--only "WEAKPOINT RELAY"` would rewrite GnatStaffAI, which is P1-S's).
- `--final` ignores both lists: it shows the target state (phase 2 must pass it).
- Phase 2 deletes every `leaving` / `arriving` list and the `_transition` note; `--check` is then strict.
- `blocks.json` legacy list now also runs `split_sync.py` (SPLIT LINK) and `thrust_sync.py` (THRUST VECTOR LINK), so one `--check` covers every shared block. `ai_gate_sync.py` matches `// >>> AI GATE v` only, so the sub-blocks are not UNLISTED there.

**Block states after P0**

| Block | File | Final hosts (`scripts`) | Carried now | Content |
|---|---|---|---|---|
| KAMIKAZE DEFENCE v1.1 core (legacy `kz_sync.py`) | `kz_block.cs` | the 10 + WaspAI | same | final. Needs exactly one `KzRayStep()` in the host. |
| KZ CONFIG v1.0 | `kzc_block.cs` | WaspAI | + leaving: 9 Flights, ShrikeAI | final (`KzLoad/KzD/KzB/KzStatus`) |
| KZ RAYS v1.0 | `kzr_block.cs` | WaspAI, ShrikeAI, GnatSwarmAI | + leaving: the 8 Flights (not Gnat) | final (`KzRayStep`) |
| AI GATE v1.3 core (legacy `ai_gate_sync.py`) | `ai_gate_block.cs` | the 10 + WaspAI + TubeRamAI | same | final (`AiGateOn/AiWin/ScriptWin/AiHoldMax`, runtime fields, `AiGate`) |
| AI GATE SETUP v1.0 | `ags_block.cs` | AuxAI, StaffAI, GnatStaffAI, WaspAI, TubeRamAI | WaspAI, TubeRamAI + leaving: 9 Flights, ShrikeAI; arriving: AuxAI, StaffAI, GnatStaffAI | final (`AiBehaviorSwitch/AiTyped/AiSpeed/AiSearchEvery/_agFound/_agProps`, `AiGateFind`, `AiGateStatus`) |
| AI GATE CONFIG v1.0 | `agc_block.cs` | WaspAI, TubeRamAI | + leaving: 9 Flights, ShrikeAI | final (`AiGateLoad`) |
| CONFIG LINK v1.0 | `cl_block.cs` | 9 Flights, ShrikeAI | none (all arriving) | skeleton: header contract only. P1-F writes it. |
| CONFIG SERVER v1.0 | `cs_block.cs` | 9 drone Staffs, StaffAI | none (all arriving) | skeleton. P1-S writes it. |
| DAMAGE SENSE v1.0 | `ds_block.cs` | the 8 Staffs (not Gnat) | none (all arriving) | skeleton. P1-S. |
| DAMAGE LINK v1.0 | `dl_block.cs` | the 8 Flights (not Gnat) | none (all arriving) | skeleton. P1-F. |
| SEARCH v1.0 | `sc_block.cs` | AuxAI | none (arriving) | skeleton. P1-X. |
| SEARCH LINK v1.0 | `sq_block.cs` | the 8 Flights (not Gnat) | none (all arriving) | skeleton. P1-F. |
| WEAKPOINT RELAY (v1.1 now) | `wp_block.cs` | AuxAI, GnatStaffAI | GnatStaffAI + leaving: the 8 Staffs; arriving: AuxAI | P1-X makes it v1.2 |
| AUX LINK (v1.0 now) | `al_block.cs` | 7 split Staffs + BasiliskStaffAI | the 7; arriving: BasiliskStaffAI | P1-S makes it v1.1 |
| REPAIR CLIENT, SEEKER | `rc_block.cs`, `sk_block.cs` | GnatStaffAI, AuxAI | + leaving: BasiliskStaffAI | unchanged |
| SPLIT LINK, SURVIVAL LINK, HELMLINK, MANUAL, BATTERY | unchanged lists | | | each owned by one package (P1-F / P1-H) |

**Two decisions that differ from the section 3 text:**
- `AgSet(string)` is **not** in AI GATE SETUP. It is host code in `AuxAI.cs` (P1-X), the only PB that parses the SDK `agSwitch|agTyped|agSpeed|agEvery` line. Why: WaspAI (99,770 minified) carries the block and has no room for dead code, and nobody owns `ags_block.cs` in phase 1.
- AI GATE SETUP reads `_agLive` (the "AI ok" suffix of `AiGateStatus`). The AI GATE core declares it. **A host without the core (AuxAI, StaffAI, GnatStaffAI) declares `bool _agLive;` itself.** Checked: those three compile with the block inserted plus that one line.

**`tools/cfg_tables.json`** (generator `tools/gen_cfg_tables.py`, `--check` regenerates and diffs, `--src DIR` reads other script copies). It was extracted from the pre-split readers; re-running it on the split scripts gives the same file.
- Per model and section: `[key, default, type, field, post]` in reader order, which is the `.CFG` value order. Extras: `local` (Tag, Start Mode), `block` (`core`/`setup` on the [AI Gate] keys: `.CFG` carries only the 4 core values; the 4 setup values go in SDK), `then` (Helm locals such as `_asLvl`, `_batFix`, `_pd`), `src`.
- Counts: Hornet 65, Harrier 72, Lancer 69, Aegis 76, Wasp 91, Longbow 79, Gnat 61 (section 1.2 said 59), Basilisk 94, Mantis 86. Shrike 17, Manual 15, Battery 10. Shared: Kamikaze Defence 20, AI Gate 8, Survival 8.
- The self-check passed: every key read in `LoadConfig/KzLoad/AiGateLoad/SvLoad/ManCfg/BatCfg` is in the table, there is no `Cfg(...)` outside those readers (except `HlkCfg`, deliberately left out), no constructor presets a table field, and every default was cross-checked against the field initializer (732 entries).

**What each phase-1 package flips or removes**
- **P1-F (9 Flights; `cl`, `dl`, `sq`, `lk`, `sv`).**
  - In all 9, remove by hand the KZ CONFIG, AI GATE SETUP and AI GATE CONFIG blocks. In the 8 (not Gnat), remove KZ RAYS too. Gnat keeps KZ RAYS (G2).
  - Remove the call sites: `KzLoad();` and `AiGateLoad();` (constructor and `reload`), `if (AiAssist) AiGateFind(_ai);` in `FindAi`, `AiGateStatus()` in the AI status line (every Flight except Basilisk), `_sb.Append(KzStatus())` (Gnat).
  - Write `cl_block.cs`, `dl_block.cs` and `sq_block.cs` within their header contracts, then apply:
    - `--apply --only "CONFIG LINK" --in HornetDroneAI,HarrierAI,LancerAI,AegisAI,WaspFlightAI,LongbowSniperAI,GnatSwarmAI,BasiliskAI,MantisAI` (not ShrikeAI);
    - DAMAGE LINK and SEARCH LINK `--in` the same list minus GnatSwarmAI.
  - SEARCH LINK provides `KzRayStep()` for the 8 Flights.
  - SPLIT LINK v1.2: `split_sync.py --apply`. SURVIVAL LINK v1.1: `--only "SURVIVAL LINK"`. P1-F owns every host of both.
  - Publish `cl_block.cs` early: P1-H needs it.
- **P1-S (9 `*StaffAI.cs`; `cs`, `ds`, `al`).**
  - Apply CONFIG SERVER `--in` the 9 Staffs (not StaffAI), DAMAGE SENSE `--in` the 8 (not GnatStaffAI), and AUX LINK `--in` the 7 + BasiliskStaffAI.
  - Remove by hand: WEAKPOINT RELAY from the 8 Staffs; REPAIR CLIENT and SEEKER from BasiliskStaffAI.
  - GnatStaffAI (G2): `--apply --only "AI GATE SETUP" --in GnatStaffAI`, then declare `bool _agLive;`. Once P1-X publishes `wp_block` v1.2: `--apply --only "WEAKPOINT RELAY" --in GnatStaffAI`.
  - Build the model tables from `cfg_tables.json`.
- **P1-X (`AuxAI.cs`; `sc`, `wp`).**
  - `--apply --only "SEARCH" --in AuxAI`, `--only "AI GATE SETUP" --in AuxAI`, `--only "WEAKPOINT RELAY" --in AuxAI`.
  - Declare `bool _agLive;` and write `AgSet(string)` in AuxAI.
  - `wp_block` → v1.2. The leaving Staff copies are never rewritten.
- **P1-H (`ShrikeAI.cs`, `StaffAI.cs`, Helm blocks).**
  - ShrikeAI: remove KZ CONFIG, AI GATE SETUP and AI GATE CONFIG (keep KZ RAYS) and their call sites (`KzLoad`, `AiGateLoad`, `AiGateFind`, `AiGateStatus`, `KzStatus`).
  - StaffAI: `--apply --only "AI GATE SETUP" --in StaffAI`, plus `bool _agLive;`.
  - Then `--only "CONFIG SERVER" --in StaffAI` once P1-S publishes `cs`, and `--only "CONFIG LINK" --in ShrikeAI` once P1-F publishes `cl`.
- **P1-D:** nothing from P0.
- **P1-T:**
  - `blocks_sync.py --check --final` lists what phase 1 still owes.
  - Use `cfg_tables.json` for test 9.3c.
  - `test_gnat_split.test_sizes` and `test_basilisk_split.test_sizes` already failed before P0 (stale 80k / 85k limits, section 9.4).
  - Golden hashes change with any script text change, P0 included.
- **Phase 2:** delete the transition lists, then `blocks_sync.py --check` must pass. Keep `cfg_tables.json` (tests); `gen_cfg_tables.py` is one-off: do not regenerate it from the split scripts, whose readers are gone.

## 0. Decision in one paragraph

The same set of modules leaves every drone Flight PB. Each one is model-independent, or is passed over IGC as parameters:

1. **All Flight config** goes to the drone's **Staff**: the `[<Model>]` section, `[Kamikaze Defence]`, `[AI Gate]` and the Flight's `[Survival]` keys. A new CONFIG SERVER block in the Staff writes every default and parses the values; the Flight's CONFIG LINK receives them as one `.CFG` string and caches it in `Storage`. The Flight PB's Custom Data then has one writer, the Staff.
2. **Damage sensing** goes to the **Staff**, sent to the Flight on `.DMG`. The Staff is the survival brain that already uses it.
3. **Camera and sensor search** goes to the **Aux**: auto-scan, all-round close scan, hit-look, manual lock, sensor poll and the kamikaze pack/refresh rays. Contacts come back to the Flight on `.SCN` and are rebuilt there.
4. **AI-block setup** (`AiGateFind`) goes to the **Aux**.
5. **Weapon auto-detect** (`DetectWeapon`, Longbow and Mantis) goes into the Staff's config server.
6. Dead status helpers are deleted (they are not referenced anywhere).

To make room in the Staffs, **WEAKPOINT RELAY moves Staff → Aux**, where the seeker already runs. **Basilisk gets the [Aux] PB.** Its Staff switches to AUX LINK, so REPAIR CLIENT and SEEKER leave it as they left the other 7. **ShrikeAI** follows helm_split.md: its config server, AI GATE SETUP, Cut 2 (cockpit HUD page and Run menu), part of the status text and the manual lock go to `StaffAI.cs`.

The hot path stays whole in the Flight / Helm: tracking (MaintainLock, designators, Track, target selection), lead, aim, control, firing, KZ classification and steering, FA, AI time-share. **No override writer changes.**

## 1. Measured sizes (minified, `tools/minify_cs.py`, 9027e80)

| Script | Minified | Needed to reach 76k |
|---|---|---|
| HornetDroneAI | 80,303 | 4.3k |
| HarrierAI | 81,361 | 5.4k |
| LancerAI | 81,743 | 5.7k |
| AegisAI | 84,304 | 8.3k |
| WaspFlightAI | 86,245 | 10.2k |
| LongbowSniperAI | 86,286 | 10.3k |
| GnatSwarmAI | 86,391 | 10.4k |
| BasiliskAI | 87,489 | 11.5k |
| MantisAI | 89,304 | 13.3k |
| ShrikeAI | 84,678 | 8.7k |

Receiving PBs:
- Hornet / Harrier / Lancer / Aegis / Wasp / Longbow / Mantis Staff: 72,667 / 72,675 / 72,667 / 72,659 / 72,654 / 72,720 / 72,712.
- Gnat Staff 73,820. Basilisk Staff 73,550.
- AuxAI 19,449. StaffAI 39,526. ThrustVectorAI 7,314.
- `scripts/WaspAI.cs` (99,761) is legacy: it is not embedded in any design (`wasp.py` uses WaspFlightAI).

### 1.1 Shared blocks, identical in all 10 scripts

| Block | Size |
|---|---|
| KAMIKAZE DEFENCE | 7,355 |
| AI GATE | 3,489 |
| LEAD | 2,663 |
| FRIENDLY AVOIDANCE | 2,377 |

In the 9 drone Flights only: SURVIVAL LINK 5,906, BREACH+SEEKER 4,829, SPLIT LINK 2,851.

### 1.2 Candidate modules per Flight

Sizes are gross / net of the Flight-side receiver.

| Module | Hor | Har | Lan | Aeg | Wsp | Lbw | Gnt | Bas | Man |
|---|---|---|---|---|---|---|---|---|---|
| M1 `[Model]` config (`LoadConfig` + `Cfg`); net after `CfgIn` (about 15 chars per key) and the link (0.55k) | 4183/2657 | 4604/2962 | 4403/2804 | 4807/3095 | 5668/3744 | 4910/3179 | 3755/2344 | 5923/3947 | 5255/3427 |
| keys in `[Model]` | 65 | 72 | 69 | 76 | 91 | 79 | 59 | 94 | 86 |
| M2 KZ config (`KzLoad/KzD/KzB` + `KzStatus`, the latter dead in split Flights) | 1731/1431 | all 9 | | | | | | | |
| M3 AI GATE setup (`AiGateLoad`, `AiGateFind`, `AiGateStatus`, fields) | 2563/2503 | all 9 | | | | | | | |
| M4 `[Survival]` read (`SvLoad/SvK`) | 437/317 | all 9 | | | | | | | |
| M5 damage sensing (`ScanSlims`, `StepSlimScan`, `CheckDamage`, fields) | 1886/1536 | | | | | | 1928/1578 | 1899/1549 | |
| M6 dead code (`HiveStatus`, `AiStatus`, `_sb`, `_sweepYaw`, ...) | 446 | 446 | 446 | 446 | 65 | 381 | 680 (unused model consts) | 103 | 381 |
| M7 search (`AutoScanStep`, `OmniScan`, `HitPoint`, `ManualLock`, `PollSensors`, fields); net of the `.SCS/.SCN` link (0.85k) | 3123/2273 | | | | | 3141/2291 | 2569/1719 | | 3141/2291 |
| M8 `DetectWeapon` | – | – | – | – | – | 573 | – | – | 578 |
| M9 `KzRayStep` (net of a 0.25k proxy) | 929/680, all 9 | | | | | | | | |

### 1.3 Rejected candidates

| Candidate | Size (Mantis) | Why it stays |
|---|---|---|
| KZ classification (`KzAssess`, `KzWant`, `KzScore`) | about 4k | It steers. It reads the whole contact table at 6 Hz and is life-critical. |
| Fleet weakpoint adoption (`WpAdopt`, `WpTick` in BREACH) | 2.0k | Aim-point selection on the Flight's own hull samples and breach state. Only the relay side moves (Staff → Aux). |
| AI time-share (`AiGate`, `AiStep`) | 1.9k | Decides when the AI blocks fly: override arbitration. |
| Hive (`ReadSwarm`, `BroadcastStatus`), friendly beacon | 1.0k | Mates feed separation and fire safety. If the beacon or `_SWARM` came from a softer PB, mates would lose this drone. |
| Command parsing | 1.4k | The Flight must take words while its Staff is dead (`Tag_CMD`, PB argument). |
| `PowerAndHealth` | 1.0k | `_powerScale` throttles thrust for the railgun recharge: that is control. |
| `PollDesignators` | 0.3k | A tracking source for the current target. |
| `SweepDir` | 0.8k | An attitude setpoint every tick. |
| Flight `Display` and first-contact line | 0.5k | Small. Errors must show while the Staff is dead. Kept as reserve R1. |
| `[Thrust]` PB as a receiver | – | It is on the thrust allocation path. Gets nothing. |

## 2. Why each move is off the hot path

- **Config.** It is read at start and on `reload` only. Values persist in `Storage`.
- **Damage sensing.** A pass already runs every 20 ticks over 4-80 ticks; the move adds 1 tick. Evade (`Evade`, jinks) stays in the Flight and reads `_hits/_hitDir` as before.
- **Search.** It finds *new* contacts; it does not track. A first sighting gets 1-2 ticks older. The tracked target (MaintainLock, designators, Track, Lead) stays in the Flight. Side benefit: the raycasts and the 200-cells-per-tick slim scan leave the Flight's instruction budget.
- **KZ rays.** They refresh *other* incoming tracks and search for the rest of a pack, once per 6 Hz pass. Classification and `KzWant` steering stay.
- **AiGateFind.** One-time terminal property writes. The values persist in the world.
- **DetectWeapon.** Setup.
- **WEAKPOINT RELAY.** 1 Hz wing data, holes valid for seconds.
- **Helm HUD and menu.** Pilot display. helm_split's Cut 2 was deferred; the owner's directive now settles it. Risk "HUD pip lag" goes on the in-game list.

## 3. Blocks (`tools/blocks.json`; legacy `kz_sync.py` / `ai_gate_sync.py` keep the core markers)

| Block | File | Hosts | Content |
|---|---|---|---|
| KAMIKAZE DEFENCE v1.1 (core) | `tools/kz_block.cs` | the 10 + WaspAI | v1.0 minus `KzLoad/KzD/KzB/KzStatus/KzRayStep`. Hooks unchanged: `KzAssess` still calls `KzRayStep()`. |
| KZ CONFIG v1.0 | `tools/kzc_block.cs` (new) | WaspAI (standalone) | `KzLoad`, `KzD`, `KzB`, `KzStatus` |
| KZ RAYS v1.0 | `tools/kzr_block.cs` (new) | WaspAI, ShrikeAI, GnatSwarmAI (until Q1) | `KzRayStep` (local) |
| AI GATE v1.3 (core) | `tools/ai_gate_block.cs` | the 10 + WaspAI + TubeRamAI | runtime fields, `AiGateOn/AiWin/ScriptWin/AiHoldMax`, `AiGate()` |
| AI GATE SETUP v1.0 | `tools/ags_block.cs` (new) | AuxAI, StaffAI, GnatStaffAI (until Q1), WaspAI, TubeRamAI | `AiBehaviorSwitch/AiTyped/AiSpeed/AiSearchEvery/_agProps/_agFound`, `AiGateFind`, `AiGateStatus`. `AgSet(string)` is AuxAI host code; a host without the core declares `bool _agLive;` (P0 handover) |
| AI GATE CONFIG v1.0 | `tools/agc_block.cs` (new) | WaspAI, TubeRamAI | `AiGateLoad` (writes and parses `[AI Gate]` into core + setup fields) |
| CONFIG LINK v1.0 | `tools/cl_block.cs` (new) | 9 drone Flights, ShrikeAI | `.CFG` listener on `CfKey()+".CFG"`, `N()/B()/T()` value readers, `CfApply`, schema check, `Storage` cache (`CfSave/CfLoad`), `CfWant`. Section appliers `KzSet`, `AgSet4`, `SvSet`. Host hook `CfgIn()` holds the per-model assignments. |
| CONFIG SERVER v1.0 | `tools/cs_block.cs` (new) | 9 drone Staffs, StaffAI | Generic table-driven MyIni default writer and reader for the target PB's Custom Data; KZ, AI Gate and Survival tables; builds and sends `.CFG`. Host hooks: `CfTarget()`, `CfModel()` (per-model `string[]` key/default/type table), `CfKey()`, `CfExtra(...)` (DetectWeapon override). |
| DAMAGE SENSE v1.0 | `tools/ds_block.cs` (new) | 8 drone Staffs (not GnatStaffAI: it runs its own GNAT HIT BEARING copy, P2-G2, 7.2) | The Flight's `ScanSlims/StepSlimScan/CheckDamage` verbatim, plus the `.DMG` sender |
| DAMAGE LINK v1.0 | `tools/dl_block.cs` (new) | 9 drone Flights (GnatSwarmAI since P2-G2) | `.DMG` receiver: fills `_hits/_hitAt/_hitDir/_hitDirT/_integ/_svNb/_svMax`, then calls `SvDamage()` |
| SEARCH v1.0 | `tools/sc_block.cs` (new) | AuxAI | Auto-scan, omni-scan, hit-look, lock ray, sensor poll, KZ rays from the `.KZR` table, own `CamFor`/`Forwardish`; sends `.SCN` |
| SEARCH LINK v1.0 | `tools/sq_block.cs` (new) | 8 drone Flights | `.SCS` sender, `.KZR` sender (the `KzRayStep()` proxy), `.SCN` receiver (rebuilds `MyDetectedEntityInfo`, `Valid()`, `AddContact(e, src)`, lock results and messages), Aux heartbeat |
| WEAKPOINT RELAY v1.2 | `tools/wp_block.cs` | AuxAI, GnatStaffAI (until Q1) | `WpCfg()` becomes a host hook. In the Aux, values come from SDK and `WpSeek` is local. |
| AUX LINK v1.1 | `tools/al_block.cs` | 7 split Staffs + Basilisk Staff | SDK cfg gets more lines (fleet channel, Weakpoint Priority/Expire, AI Gate setup values); flags bit 9 AI ASSIST; `.SKW` reader removed; `WpStatus` comes in the DKS status |
| SPLIT LINK v1.2 | `tools/lk_block.cs` | 9 drone Flights | F2S flags bit 16 CFG WANT, bit 17 AI ASSIST |
| SURVIVAL LINK v1.1 | `tools/sv_block.cs` | 9 drone Flights | `SvLoad` gone (values come through `SvSet`) |
| HELMLINK 1.3 / MANUAL 1.2 / BATTERY | Helm block files | ShrikeAI | Section 4.3 |

`blocks_sync` does not delete blocks. The stale sub-block text is removed from the 10 scripts by hand.

## 4. IGC (all `TransmissionDistance.CurrentConstruct`)

Drone key: `K = "WSPL"+<Flight PB EntityId>`. Helm key: `<Ship Tag>.` (HELMLINK).

### 4.1 Drone channels

| Tag | From → to | Tuple | Rate |
|---|---|---|---|
| F2S (existing) | Flight → Staff | new flag bits: 16 CFG WANT, 17 AI ASSIST (runtime `AiAssist`) | unchanged (5 Hz + change); WANT set means an F2S goes out within 12 ticks |
| `.CFG` | Staff → Flight | `string`: `"CF1\t<schema>\t<status>\n" + one line per section (M model, K kamikaze, A AI gate, S survival), values tab-separated in table order`. Bools are `1/0`, doubles `ToString("R")`, strings raw. `schema` = key counts per section + version, generated on both sides from `tools/cfg_tables.json`. A mismatch is ignored and the screen says `! config schema: update Flight and Staff together`. | In the tick the Staff sees F2S with bit 16, at most once per 30 ticks per key |
| `.DMG` | Staff → Flight | `MyTuple<int hits, double integ, Vector3D hitDir, double hitDirAge, int blocksHit, double maxDrop>`; `hits` is a cumulative counter | After each `CheckDamage` with a drop (≤ 3 Hz; Gnat Staff ≤ 30 Hz, it checks every 2nd tick), else 1 Hz |
| `.HAX` (Gnat only, P2-G2) | GnatStaffAI → GnatSwarmAI | `MyTuple<Vector3D axis, double strength, double spread, double lastHitAge>`: the hit-bearing estimator's `HitAxis()` at send time (strength = decayed hits x peak sharpness, spread = half-angle in deg holding 80 % of the likelihood, lastHitAge = s since the last own vote or mate share) | At once after a vote or a mate's `<Tag>_HIT`, every 6 ticks (10 Hz) while strength > 0, else 1 Hz |
| `.SCS` | Flight → Aux | `MyTuple<int flags, long tgtId, Vector3D lostPos, Vector3D lostVel, Vector3D hitDir, MyTuple<Vector3D,Vector3D,Vector3D,Vector3D> p>` (see below) | Every 6 ticks + at once on a flag change. Stale at the Aux after 30 ticks: the Aux stops all scanning. |
| `.KZR` | Flight → Aux | `MyTuple<long id, Vector3D pos, Vector3D vel, double radius, double age, int flags>`: bit0 is the Flight's target, bits 8-11 index, 12-15 count, 16-31 pass number | From the `KzRayStep()` proxy, ≤ 4 per KZ pass (6 Hz), only while threats exist |
| `.SCN` | Aux → Flight | `MyTuple<long id, int f, Vector3D hit, Vector3D vel, MyTuple<Vector3D,Vector3D> box, MyTuple<Vector3D,Vector3D> ori(fwd, up)>`: f bits 0-7 entity type, 8-15 relationship, 16 has hit, 20-22 source (1 SCAN, 2 SENSOR, 3 KZ, 4 LOCK), 24-27 lock result (0 ok, 1 no forward camera, 2 nothing hit with the charge in `hit.X`). `id 0` = heartbeat. | Per non-empty hit, except own grid and planets; friendlies and asteroids are sent because FaSee uses them. About ≤ 6 per tick. Heartbeat every 30 ticks. |
| `.SDK` (existing) | Staff → Aux | Same tuple type. cfg = `Tag\nSeekerGroup\nflChan\nwpPri\nwpExp\nagSwitch\|agTyped\|agSpeed\|agEvery`; new flags bit 9 AI ASSIST | unchanged |
| `.DKS` (existing) | Aux → Staff | status string adds `WpStatus()` and a search / AI-setup line | unchanged |
| `.WPF` (existing) | Flight → **Aux** | unchanged tuple; the Staff no longer listens | unchanged |
| `.WPT` (existing) | **Aux** → Flight | unchanged tuple; exactly one sender, guaranteed by the host lists | unchanged |
| `.SKW` | removed | | |
| `<ch>_WP` | Aux, through the antenna | unchanged | unchanged |

`.SCS` details:
- **flags.** bit0 AUTO (the Flight's existing auto-scan condition: no target, AI off, AutoScan on, mode GUARD/HUNT/FOLLOW). bit1 OMNI (mode not IDLE and CloseScan on). bit2 HARD (UnderFire or Alerted). bit3 has target. bit4 AI on. bit5 SENSORS. bit6 HITLOOK (off for the Gnat variant). bit7 KZRAYS. bits 8-15 lock request counter. bit17 lock camera uses Forwardish (0.9) instead of 0.99.
- **p.** a = (auto-scan range, scan cone in degrees, spiral N). b = (close-scan range, 900, lock range). c = (lost age, hit-dir age, lost radius). d = (KzPack, KzReserve, 0).
- The model differences found by diffing the variants (ScanRange vs WeaponRange×1.1, N 120/150, Gnat without HitPoint, lock rule and range) are carried in p and the flags. AuxAI stays model-independent.

### 4.2 The Flight rebuilds contacts

`new MyDetectedEntityInfo(id, "", type, hit?, MatrixD.CreateWorld(c, fwd, up), vel, rel, box, 0)`, then `Valid(e)` (FaSee, SvSee), then `AddContact(e, src)`.

The constructor exists in the sim (`sim/src/Api/Detection.cs`). In the game it is [game?]. Fallback if the game rejects it: refactor to `AddContactRaw(...)`, about +0.3k.

On a run gap the Flight drops any queued `.SCN`.

### 4.3 Helm channels (HELMLINK 1.3)

- H2S flags: bit 9 CFG WANT, bit 10 AI ASSIST (first free bits; verify in `helmlink_block.cs`).
- `.CFG` Staff → Helm: same format as the drones. Helm sections: `[Shrike]` minus Tag/Start Mode, the Helm-side `[Manual]` keys, `[Battery]`, `[Kamikaze Defence]`, 4 `[AI Gate]` values.
- `.HUD` and `.MNU`: exactly as specified in helm_split.md section 2 (Cut 2). `.HUD` also gains the KZ count / next impact and the AI-search bits, for the lines that leave the Helm screen.
- `.LCK` Helm → Staff: `int` lock counter. The Staff casts the ray from its message callback and replies `.LKR` with the `.SCN` tuple.

## 5. Override writers (survivability.md 1.3 is unchanged for overrides)

| Output | Writer after this split |
|---|---|
| Gyro / thruster overrides, `DampenersOverride` | **Unchanged.** Flight. Staff under FOL bit 2 (take-off burn, dock via `.DKS`). Staff limp-home when the Flight is down. Aux, `[Thrust]` and StaffAI never write (StaffAI only `ReleaseAll` while the Helm is silent). |
| Guns, railgun | unchanged (Flight / Helm) |
| AI blocks `Enabled` | unchanged (Flight; Staff off-once when the Flight is down) |
| AI blocks "AI Behavior" / `SpeedLimit` / `UpdateTargetInterval` | **Aux** (drones), **StaffAI** (Helm), was Flight. GnatStaffAI until Q1. |
| Flight / Helm PB Custom Data | **Staff only.** The Flight and Helm never write it any more (test). |
| Camera `EnableRaycast` (always true) | Flight GetBlocks + Aux (SEEKER, SEARCH). Same value every time, so harmless. |
| Sensor properties | Staff (unchanged) |
| Projector / merge / seeker turret group | unchanged (staff_split) |
| Cockpit HUD surface (Helm ships) | StaffAI (Cut 2) |

## 6. Sync

### 6.1 Config
- **Start-up.**
  - The Flight sets CFG WANT on every start and on `reload`.
  - Until a matching `.CFG` arrives, it runs on its `Storage` cache, or on its field defaults if the cache is empty. Screen: `cfg: cached` or `cfg: defaults (no Staff)`.
  - The Staff answers within one F2S period (≤ 12 ticks). The Flight applies atomically, clears WANT and saves the string in `Storage`.
- **Values that stay local.** Tag and Start Mode are read locally, Get-only, because the IGC channels are registered in the constructor. Drone Flights also keep the SPLIT LINK key; the Helm keeps `[Link]`.
- **Runtime words.** `aim`, `breach`, `wp`, `range`, `split/focus`, `ai on|off` survive until the next start or `reload`, as today. The Staff never pushes config unasked.
- **Re-bind.**
  - Flight rebuilt: a new key, WANT, and the Staff re-keys (its existing 1 Hz rescan while down).
  - Staff rebuilt: nothing to do. It answers the next WANT.
- **Run gaps.** Nothing happens; the config is in memory.

### 6.2 Damage
- First message, or a counter that went down (a new Staff): the Flight copies the value.
- Counter up while the link is alive: `_hitAt = _now; _hits++`.
- `.DMG` silent for 3 s after it was heard: `_hitAt = _now` once, `_integ = -1` (the helm_split rule).

### 6.3 Search
- The Aux scans only while `.SCS` is ≤ 30 ticks old.
- Aux rebuilt: re-binds to the nearest `[Flight]` (existing rule); scanning resumes on the next `.SCS`, within 6 ticks.
- Flight rebuilt: new key; the Aux re-scans at 1 Hz while stale.
- Aux run gap: drops `.KZR` entries older than 0.5 s.
- Flight run gap: sends `.SCS` at once and drops queued `.SCN`.

### 6.4 AI setup
The Aux runs `AiGateFind` when:
- SDK bit 9 rises;
- the AI Gate values in the cfg change;
- it rescans blocks (every 600 ticks) while bit 9 is set.

### 6.5 WEAKPOINT RELAY in the Aux
- The holes are soft state (20 s expiry). Aux rebuilt: the map refills from the wing.
- The Staff writes the two `[Fleet]` weakpoint defaults and passes them in SDK.

### 6.6 Failure behaviour (add to survivability.md 1.4)
- **Aux dead.** Lost (besides repair, medic, seeker): camera auto-scan, close scan, hit-look, sensor poll, manual lock, KZ pack rays, wing weakpoint sharing, AI-behaviour setup. Setup already made persists.
- What still finds and tracks targets: the AI Offensive block (hybrid search), turret designators, hive and fleet shares, MaintainLock on the current target.
- The Flight screen says `! Aux PB silent: camera search off`.

## 7. Per-model decisions, placement, sizes after (estimates ±0.5k; measured in phase 2, `tools/minify_cs.py`, 2026-10-18)

| PB | Before | Estimate | **Measured (phase 2)** | Moves |
|---|---|---|---|---|
| Hornet / Watcher Flight | 80,303 | ~68.5k | **70,053** | M1-M9 |
| Harrier | 81,361 | ~69.2k | **70,806** | same |
| Lancer | 81,743 | ~69.8k | **71,338** | same |
| Aegis | 84,304 | ~72.0k | **73,661** | same |
| WaspFlight | 86,245 | ~73.7k | **75,177** | same |
| Longbow (S/L/L Seeker) | 86,286 | ~73.4k | **74,926** | same + M8 |
| Basilisk (+ Seeker) | 87,489 | ~74.7k | **76,439** | same; **gets an Aux** |
| Mantis | 89,304 | ~76.2k | **77,663** (exception ≤ 78,000) | same + M8 (tightest) |
| Gnat | 86,391 | ~79.3k (G1a) | **74,409** (G1a + P2-G2) | config, AI setup, search, KZ rays out (G1a); damage sensing + RAID hit estimator out to the Staff (P2-G2) |
| ShrikeAI | 84,678 | ~76.4k | **77,994** (exception ≤ 78,000) | H1-H6 below |
| Hornet / Harrier / Lancer / Aegis Staff | 72.7k | ~73.5 / 73.8 / 73.6 / 73.9k | **75,810 / 76,090 / 75,923 / 76,183** | − WP RELAY 5.5k, + CONFIG SERVER 2.0k + model table 2.1-2.5k + DAMAGE SENSE 2.2k |
| Wasp / Longbow / Mantis Staff | 72.7k | ~74.4 / 74.7 / 74.8k | **76,721 / 77,540 / 77,788** | same (Longbow and Mantis + DetectWeapon 0.6k) |
| Basilisk Staff | 73,550 | ~64.9k | **67,181** | − RC 10.5k, − SEEKER 2.8k, + AUX LINK 2.65k, then as above |
| Gnat Staff | 73,820 | ~63.7k (G1a) | **70,671** (G1a + P2-G2) | + CONFIG SERVER + AUX LINK + GNAT HIT BEARING (6.9k); − WP RELAY, REPAIR CLIENT, SEEKER, AI GATE SETUP |
| AuxAI | 19,449 | ~32.5k | **35,132** | + WP RELAY 5.5k, SEARCH ~5.9k, AI GATE SETUP 1.15k, glue 0.5k |
| StaffAI | 39,526 | ~47k | **53,359** | config server and Helm table 3.35k, AGS 1.15k, HUD and menu 1.9k, status 0.4k, lock 0.6k |
| ThrustVectorAI | 7,314 | unchanged | 7,314 | |

### 7.1 ShrikeAI moves (helm_split pattern, all into StaffAI)

| Move | Net |
|---|---|
| H1 config: `LoadConfig/Cfg/ManCfg/BatCfg/HlkCfg`, KZ CONFIG, `AiGateLoad` → CONFIG SERVER. The Helm keeps Get-only Tag, Start Mode and `[Link]`. | −4.9k |
| H2 `AiGateFind` and `AiGateStatus` | −1.06k |
| H3 Cut 2: `ManHud`, `ManShow`, menu, HUD surface → `.HUD/.MNU` | −1.5k |
| H4 `AiStatus` and the KZ line leave the Helm screen | −0.23k |
| H5 dead `AimInfo` and `_avgMs` | −0.15k |
| H6 manual lock through `.LCK/.LKR` | −0.35k |
| H7 camera auto-scan: the Staff casts the rays while H2S bit 11 SCAN is set (done in P1-H; was a section 10 candidate) | −0.8k |

- **Ships without a Staff.** They run the Helm on the `Storage` cache or on defaults. All five shipped blueprints (Ballista, Shrike, Corsair, Warden, Leviathan) carry a Staff. This reverses helm_split's "config stays", per the owner's directive; the cache keeps the Helm self-sufficient after its first link.
- **KZ RAYS** stay local in the Helm (StaffAI is Update10).

### 7.2 Placement (CLAUDE.md outline rule)
- **Basilisk / Basilisk Seeker:**
  - Change: add `'BASILISK'` to `SUPPORT_AUX` in `_common.py`.
  - In-memory dry run: `[Aux] Basilisk PB` at **(2,0,1), Forward/Right**, inside `kit_outline`, Chebyshev distance 3 from the Flight PB at (-1,1,0). `validate()` OK, kit skipped `[]`.
  - Note: the dry run added the Aux after the kit. Re-check with the real order (add_support_pbs → bury_swap → add_survival_kit).
  - No printer prints the Basilisk, so no welder reach check is needed.
  - +100 PCU, +138 kg. Check that min accel stays ≥ 1.0 m/s².
- **The other 7:** already in place (staff_split).
- **Gnat — G1a (lead decision 2026-10-18, owner's "always split" rule).** G2 did not fit: Gnat Staff 80,385 / Gnat Flight 80,634 minified, both over the 80k budget. Q1 is closed: the Gnat traded its 50 m sensor for an `[Aux] Gnat PB`.
  - Dry runs (kept for the record): armour cell (1,1,-1) cuts the two ion thrusters off ("separate pieces"); decoy cell (1,1,2) cuts the sensor off; nose cell (1,1,-3): nothing mounts; sensor moved to (1,1,2) with the PB at (1,1,3): the PB does not mount. **The only cell that works is the sensor's own cell (1,1,3), PB facing Forward/Up.**
  - Built (P2-G): `core.gnat()` builds the frame without a sensor; `gnat_survival` puts Decoy 1 at (1,1,2) and pins `[Aux] Gnat PB` at `GNAT_AUX_CELL` (1,1,3) Forward/Up (like `LONGBOW_S_AUX_CELL`), inside the 2 x 2 section. `'GNAT'` is in `SUPPORT_AUX`; `add_support_pbs` finds the pinned Aux and adds nothing. The test ships `Gnat Side*` (side thruster flame through (1,1,3)) search the section for a free PB cell and record `no cell for the [Aux] PB inside the 2 x 2 section` in `bp.kit['skipped']` when there is none (Gnat Side); `add_support_pbs` then adds no Aux outside the outline.
  - Mass 2,481 → 2,485 kg (sensor out, PB in), accel F/B 5.8 m/s² unchanged (≥ 1.0). 20 blocks (ctrl 4, no sensor). `validate()` OK; kit unchanged (Decoy 1, the same four skipped items).
  - Printers that print Gnats (Hive Mother, Twin Hive, Quad Hive, Fleet Forge, Mobile Hive, Static Nest): `reach.verify_printer` PASS, every Gnat bay 7 welders / 20 drone blocks / min 1 welder per block; the bay projector's ghost holds `[Aux] Gnat PB` and no sensor.
  - Scripts: GnatStaffAI carries AUX LINK v1.1 (RpDockOk false; SeekerOn / SeekerGroup from its STAFF SEARCH keys as before, now passed in SDK) and no longer carries WEAKPOINT RELAY, REPAIR CLIENT, SEEKER, AI GATE SETUP or the host `_agLive` / `AgStep` / `WpCfg` code; those run in the Aux. 80,385 → 63,724 minified. The Gnat Flight takes SEARCH LINK (KZ RAYS and AutoScanStep out) and sends `.SCS` without the SENSORS bit. (Under G1a the lead first kept damage sensing in the Flight; P2-G2 below moved it.)
  - **P2-G2 (damage sensing + hit estimator → Gnat Staff).** Gnat Flight 79.3k → 74,409, Gnat Staff 63.7k → 70,671 minified.
    - What moved: `ScanSlims / StepSlimScan / CheckDamage`, the hit-bearing estimator (`HitVotes`, `CountFaces`, `Decay`, `AddHit`, `HitAxis`, 64-direction `_lp`, 12 s decay) and the `<Tag>_HIT` share and listen. RAID (`RaidOn`, `RaidDir`, `RaidScan`, the RAID screen line) stays in the Flight: it steers.
    - Design: **a Gnat-local scanner in the Staff** (host section GNAT HIT BEARING in `GnatStaffAI.cs`, the Flight's v1.9 code moved unchanged), **not DAMAGE SENSE with a hook**. Why: the Gnat checks every 2nd tick (DAMAGE SENSE every 20), takes `_hitDir` from the estimator axis (DAMAGE SENSE: centroid of the hurt blocks), keeps per-block cell bounds and a hurt list; hosting that in `ds_block.cs` needs a cadence switch plus three hooks in all 8 Staffs. The copy costs ~1.4k of duplicated slim-scan text in one Staff with room to spare. `ds_block.cs` / `dl_block.cs` changed only in their header comments.
    - Link: the Staff sends the standard `.DMG` tuple (so GnatSwarmAI carries **DAMAGE LINK** unchanged: `_hits / _hitAt / _integ / _hitDir / _svNb / _svMax`, then `SvDamage()`) and the Gnat-only `.HAX` (4.1). The Flight's `HitAxis(out st)` returns the cached axis with `st = strength x e^(-age/12 - max(0, age - 3))`, age = s since the last `.HAX`: the estimator's own 12 s decay between messages, and after 3 s of silence (Staff gone) it falls to 0 within a few seconds. `_hitLastT = now - lastHitAge`, `_axisSpread = spread` on every message; RaidOn's RaidLife / RaidMinStart / RaidMinHold tests read them as before. Latency: one tick more than before (vote in the Staff, `.HAX` sent the same tick).
    - Sync: Staff rebuilt → `_hits` restarts at 0, DAMAGE LINK copies the lower counter (6.2), the estimator starts empty (RAID needs fresh hits, as after a Flight restart before). `.DMG` silent 3 s → DAMAGE LINK rule. Flight rebuilt → the Staff re-keys `_fk` (existing rescan); `.HAX` / `.DMG` go to the new key.
    - Sim smoke (out/sim_g2, blueprints out/g2bp): 1v1/Gnat-vs-Hornet 3 seeds default (UNDER FIRE counter from `.DMG`), plus 3 seeds with `AI Block Search Assist = false` (RAID needs the AI off): RAID started in 1 of 3 (spread narrowing 85 → 55 deg), the other two Gnats died after 5-7 hits, below Raid Start Confidence. Writer audit 0, no exceptions.
  - Survivability audit: score 31 → 28. The audit counts the Aux PB as a "flight PB" (so flight PB leaves the SPOF list but shows as CLUSTERED), and open critical faces go 44 % → 48 % (the Aux at the tail end is open on four faces).

## 8. Work packages

Every package owns its files exclusively. Phase-1 packages never edit `blocks.json`; they apply with `blocks_sync.py --apply --only <their blocks>`. The full `--check` runs only in phase 2.

### P0 (sequential, first)
1. Split the KZ and AI GATE blocks into core plus sub-blocks, behaviour-preserving: every current carrier gets all of its sub-blocks, so all 12 scripts compile unchanged.
2. Write `tools/gen_cfg_tables.py` (one-off) and `tools/cfg_tables.json`: per model / section, the ordered list of [key, default, type, field, post-op], extracted from today's `LoadConfig/KzLoad/AiGateLoad/SvLoad/ManCfg/BatCfg` **before** anyone edits them.
3. Set the final host lists in `blocks.json` and create the new block files (skeletons with header contracts).
4. Save this doc.
5. Checks: `compile_check`, kz / ai_gate sync, `blocks_sync --check`.

Files owned: `kz_block`, `kzc_block`, `kzr_block`, `ai_gate_block`, `ags_block`, `agc_block`, `blocks.json`, `cfg_tables.json`, `gen_cfg_tables.py`, this doc. Re-sync: TubeRamAI and WaspAI.

### Phase 1 (parallel)

| Package | Files owned | Work |
|---|---|---|
| **P1-F, drone Flights** | the 9 Flight scripts, `cl_block.cs`, `dl_block.cs`, `sq_block.cs`, `lk_block.cs`, `sv_block.cs` | Writes CONFIG LINK (publishes it for P1-H); generates `CfgIn` from `cfg_tables.json`; removes M1-M9; Gnat per G2. |
| **P1-S, drone Staffs** | the 9 `*StaffAI.cs` (not StaffAI.cs), `cs_block.cs`, `ds_block.cs`, `al_block.cs` | Writes CONFIG SERVER (publishes it for P1-H) and the model tables from `cfg_tables.json`; DAMAGE SENSE; DetectWeapon hook; removes WP RELAY from 8 Staffs; Basilisk Staff → AUX LINK; Gnat Staff per G2 (CONFIG SERVER + AGS); re-syncs `wp_block` v1.2 into Gnat after P1-X. |
| **P1-X, Aux** | `AuxAI.cs`, `sc_block.cs`, `wp_block.cs` | SEARCH, WP RELAY v1.2 hosting, AGS hosting, SDK cfg parsing, DKS status lines |
| **P1-H, Helm** | `ShrikeAI.cs`, `StaffAI.cs`, `helmlink_block.cs`, `manual_block.cs`, `battery_block.cs`, `sim/src/Run/SelfTestHelmSplit.cs` | H3-H6 first; H1/H2 once `cl`/`cs` are published |
| **P1-D, designs** | `tools/designs/_common.py`, `core.py` (only G1a after Q1) | `SUPPORT_AUX += BASILISK`; `PB_BUDGET = 80000` as the default limit in `script_text()`; ShrikeAI entry → 80000 |
| **P1-T, tests** | every `tools/tests/*.py` edit, any `sim/src` IGC check | Section 9; confirms the sim's IGC accepts nested `MyTuple` |

### Phase 2 (one agent)
1. `blocks_sync --apply`, then `--check`.
2. `compile_check.sh`, `minify_check.sh`; measure every size and fill in section 7.
3. `run_all.py --update-golden` (every drone and Helm ship hash changes).
4. `build_all.py --no-zip --only` Hornet Harrier Lancer Aegis Wasp Watcher "Longbow S" "Longbow L" "Longbow L Seeker" Mantis Gnat Basilisk "Basilisk Seeker" Ballista Shrike Corsair Warden Leviathan "Shrike Staff Test".
5. Sim runs (section 9).
6. Docs: survivability.md 1.1-1.4, 4.1, 5; staff_split / helm_split status lines; `retrofit.md` Role-specific row; one line in CLAUDE.md "Script size" (budget enforced by `test_pb_budget.py` and `script_text()`; owner 2026-10-18).
7. Commit to main. Say the zips are not rebuilt.

### Phase 3
P2-G, Gnat G1a — done 2026-10-18 (lead decision; section 7.2).

## 9. Tests

### 9.1 Static
1. `tools/compile_check.sh` (all scripts) and `tools/minify_check.sh`.
2. `python3 -I tools/blocks_sync.py --check` (includes the legacy kz / ai_gate / split syncs): no UNLISTED.

### 9.2 New `tools/tests/test_pb_budget.py` (the durable rule)
- Every `scripts/*.cs` minified ≤ 80,000. The only exception is `LEGACY = {'WaspAI.cs'}`, which the test also asserts is never passed to `script_text(` in `tools/designs`.
- Failure message: "over the 80,000 PB budget: move an off-hot-path module into another PB (CLAUDE.md 'Script size', docs/design/flight_split.md section 10); do not trim features or raise the budget".
- Prints a "plan the next split" note above 76,000.
- `_common.script_text()` fails builds above `PB_BUDGET` too.

### 9.3 New `tools/tests/test_flight_split.py`
- **(a) Tuple shapes.** `.DMG .SCS .KZR .SCN .LKR` `MyTuple<...>` text is identical at the sender and the receiver. The `.CFG` header and schema constants match on both sides.
- **(b) What moved.**
  - The 9 Flights + ShrikeAI have no `Me.CustomData =`, no `void KzLoad(`, no `void AiGateLoad(`, no `void AiGateFind(`.
  - The 8 drone Flights (and the Gnat after G1a) have no `void ScanSlims(` and no `void AutoScanStep(`.
  - The 8 Staffs have no `void WpStep(`. AuxAI has `WEAKPOINT RELAY v1.2` and `SEARCH v1.0`.
  - AuxAI has no `ThrustOverride`, `GyroOverride` or `DampenersOverride`.
- **(c) Config tables.** For each model: the Staff table and the Flight `CfgIn` match `cfg_tables.json` in count and order, and each default equals the Flight field initializer.
- **(d) Sizes.**
  - the 10 scripts ≤ 76,500 (Gnat ≤ 80,000 while Q1 is open);
  - the 9 Staffs ≤ 78,000 (Gnat ≤ 80,000 under G2);
  - AuxAI ≤ 40,000; StaffAI ≤ 78,000.

### 9.4 Updates to existing tests
- `test_gnat_split.test_sizes` (stale 80k / 40k) and `test_basilisk_split.test_sizes` (85k / 40k): replace with pointers to (d).
- `test_aux_split`:
  - (a): drop `.SKW`;
  - (b): WP RELAY moved;
  - (c): Staff ≤ 78k, AuxAI ≤ 40k;
  - (d): Basilisk has 4 PBs (Aux ≤ 4 cells from its Flight PB, kit complete), Gnat per Q1.
- `test_m6`, `test_ballista`, `test_helm_split`, `test_harasser`: limits now come from `C.PB_BUDGET`; ShrikeAI ≤ 80,000.
- `test_pb_count`.
- `golden_hashes.json`.

### 9.5 Sim runs (seed counts: "runs, no exception, writer audit 0", not strength)
1. `SimHost.exe selftest m2 m4 m5 m6 hsplit`; hsplit gains the CFG handshake, HUD/menu (helm_split step 5d) and lock checks.
2. `one --scenario "1v1/Hornet-vs-Hornet" --seed-index 0 --echo-every 5 --pb-custom-data "<Flight>=[Hornet]\nCombat Speed=55"`: the Flight shows `cfg: Staff` within 1 s and its trace speed cap is 55.
3. `--events "0:!disable-pb Staff;6:!enable-pb Staff"`: `cfg: defaults (no Staff)`, then applied within 1 s of the Staff's return. 1 seed, Hornet and Basilisk.
4. `--events "20:!disable-pb Aux;35:!enable-pb Aux"`: SCAN contacts stop and resume. 3 seeds each on Hornet, Basilisk and Mantis.
5. `--events "30:!disable-pb Staff;45:!enable-pb Staff"`: one hit on DMG loss, no false hit burst on return. 3 seeds.
6. `--events "30:!disable-pb Flight;45:!enable-pb Flight"`: limp-home as before. 3 seeds.
7. Writer audit gyro 0 / thrust 0 in every run.
8. `run --filter "kamikaze/" --seeds 3`, baseline vs after (KZ-sourced contacts present, outcomes in line).
9. `run --filter "1v1/(Hornet|Wasp|Mantis|Longbow S|Basilisk|Gnat)-vs-Hornet" --seeds 5`, before vs after.
10. `follow-test --model Hornet --count 3`; `dock-launch` for Hornet, Wasp and Basilisk (new Aux).
11. Helm: `run --filter "^enemy/Tech Hunter-vs-Ballista$" --seeds 5`; Shrike vs Hornet 5 seeds; Corsair / Warden / Leviathan vs Pirate 3 seeds each.

Note: the sim cannot rebuild a PB mid-run, so the re-bind after a rebuild is only exercised by the t=0 start-up handshake.

### 9.6 In-game list (owner)
- `.CFG` reaches the Flight and the Helm.
- `MyDetectedEntityInfo` construction compiles in game.
- Aux camera search finds a target.
- Shrike HUD pip lag (Cut 2).
- Gnat without its sensor (G1a): close contacts still picked up by the Aux close scan.
- A Helm without a Staff runs on its cache / defaults (no HUD page, menu, manual lock or auto-scan).
- These are `packages/tests.md` items 59-64.

### 9.7 Phase 2 results (2026-10-18)

**Static.** `blocks_sync.py --check` and `--check --final` pass (every `leaving` / `arriving` list and the `_transition` note deleted from `tools/blocks.json`). `compile_check.sh`: every script compiles; `minify_check.sh`: same IL everywhere. Sizes: section 7 ("Measured").

**Fixed in phase 2.**
- **BasiliskAI never flew (regression from P1-F).** The new readers were inserted in the tick line in front of an existing trailing comment, which then swallowed `SvRead(); ReadSwarm();`. `SvYield` stayed true from the first tick, so neither the script nor the AI blocks flew (sim: speed 0 and STUCK in every Basilisk 1v1, 1 win of 25 against 15 before). Fixed (76,439 → 76,460); new test `test_flight_split.WhatMoved.test_flight_tick_calls_every_reader`.
- Tests: `test_flight_split` (CfgIn parser read the body's opening brace; 5-element Helm entries; shared Helm sections; documented exceptions MantisAI and ShrikeAI ≤ 78,000), `test_m6` / `test_ballista` (Helm keys are literals in StaffAI now; the shipped Shrike carries the Staff; a Helm PB without a Staff PB fails), `test_basilisk_split` (the Basilisk's `[Aux]` PB; the Staff sits one cell deeper, (-1, 0, 1), because bury_swap runs after the Aux; Shrike Basilisk Test now has a StaffAI Staff), `test_battery_solvers` (compare without the minifier's line breaks; the Hornet's write-only `_leadT` was deleted as dead code, the Helm's two writes are its own). Golden hashes updated.
- Test ships: Shrike Test Rocket, Shrike Multi Test and Shrike Basilisk Test carry a `[Staff]` PB running StaffAI, so their Custom Data reaches the Helm (selftest m3). Shrike Test stays the single-PB ship (selftest m2 "the link is optional"). SelfTestM1: the Run-menu check reads the Staff's menu on Shrike Staff Test (Cut 2); the assist and lock checks run on Shrike Staff Test (HUD page and lock ray are the Staff's).

**Unit tests** (`run_all.py --fast`, 417 tests): only the sample-based `test_sebp_ext` tests fail (they need the owner's files). `test_battery_solvers` passes now.

**Sim** (baseline = a HEAD worktree with its own blueprints and sim build; seed counts as in 9.5, enough to see "runs, no exception, writer audit 0" and a gross regression, not strength):

| Run | Seeds | Before (HEAD) | After |
|---|---|---|---|
| 1v1 among Hornet, Wasp, Mantis, Longbow S, Basilisk, Gnat (all 21 pairings, the 6 with Hornet included) | 5 | 60 decisive / 43 timeouts / 2 draws | 68 / 36 / 1; Basilisk-vs-Hornet 4-1 → 5-0, Hornet-vs-Hornet 2-1 → 2-2, Gnat-vs-Hornet 0-3 → 0-4; 0 exceptions, writer audit 0 |
| `kamikaze/` | 3 | 93 runs end at 5.2 s | the same: **the Stinger in the Stinger Rack projection has no PB** (pre-existing at HEAD, `printers._stinger_fallback` drops the PB with the blocks that hung on the gatling), so the swarm never moves; only "scripts start, no exception" is shown |
| `ram/<M>-vs-3xTube 2x2 W` (supplement for KZ: real kamikazes) | 3 each | Hornet 1-2, Mantis 1 win + 2 mutual, Basilisk 3-0 (976 / 201 / 7,257 damage taken) | Hornet 1-2, Mantis 1 + 2 mutual, Basilisk 3-0 (990 / 7,740 / 1,980); 0 exceptions, writer audit 0 |
| `.CFG` handshake: Hornet, `--pb-custom-data "Flight=[Hornet] Combat Speed=55"` | 1 | | `cfg: Staff` at 1 s; the Flight's `MaxSpeed` reads 55 (temporary debug line) |
| Staff off 0-6 s (Hornet, Basilisk) | 1 each | | `cfg: defaults (no Staff)` for 6 s, `cfg: Staff` within 1 s of the Staff's return |
| Staff off 30-45 s (Hornet, DAMAGE LINK) | 3 | | `_integ = -1` at 33 s (3 s of silence), no hit burst when the Staff returns (the counter is adopted); later hits counted normally |
| Aux off 20-35 s (Hornet, Basilisk, Mantis) | 3 each | | `! Aux PB silent: camera search off` from 22.5 s to 35 s, gone after; SCAN-sourced contacts are 0 in these fights in both trees (targets come from the AI block and the turrets), so stop / resume of SCAN contacts could not be shown; 0 exceptions, writer audit 0 |
| Flight off 30-45 s (Hornet) | 3 | win / loss / win | identical outcomes |
| follow-test Hornet `--count 3` | 1 | FOLLOW OK | FOLLOW OK (identical) |
| dock-launch Hornet, Wasp | 1 | LAUNCH OK, writer audit 1/1 (take-off hand-over) | identical. The Basilisk has no merge block: dock-launch does not apply (the 1v1 and ram runs cover its new Aux) |
| Helm: Shrike vs Hornet / Ballista vs Hornet / Corsair, Warden, Leviathan vs Pirate | 5 / 5 / 3 each | 2-2-1 / 4-0-1 / 2+1 to, 3-0, 3-0 | 1-3-1 / 5-0 / 2+1 to, 3-0, 3-0; 0 exceptions, writer audit 0 |
| selftest m2 / m3 / m4 / m5 / m6 / hsplit | as coded | 1 / 2 / 2 / 4 / 0 / 1 failures | 0 / 3 / 2 / 4 / 0 / 1 |
| selftest m1 | as coded | 5 failures | 5 failures: the same checks except that the Run menu now passes and BATTERY-lite fails 0/3 (was 1/3) |

Notes on the selftests:
- m3: section 1 failed in one seed (no rocket fired in seed 1; 2/3), section 2 improved (2/3 from 1/3); the standoff median 851 m (833 before), threshold 900. These are 3-seed fire-control checks; no script error.
- m4: the same two checks fail as at HEAD (wing satellite ring 0/3; wing hunt / wing lock 4/5 vs 2/3). m5: the same 4 failures as at HEAD. hsplit: the same "Staff killed at 15 s" check (1/3) fails as at HEAD; the new CFG / HUD / menu / lock / AI checks pass.
- m1 assist hud: at 11 s the Staff-drawn HUD page on Shrike Staff Test shows `MODE AUTO` in 4 of 5 seeds (the old Helm-drawn page on Shrike Test: 4 of 5 passed). Worth a look in the next session; the in-game HUD check is `packages/tests.md` item 62.

## 10. Next cuts (when a PB passes 76k again; test_pb_budget points here)

- **R1:** Flight `Display` lines → Staff screen (0.4k).
- **R2:** `PollDesignators` → Aux (0.3k; needs a tracking-latency A/B first).
- ~~H7~~ done in phase 1 (P1-H, section 7.1).
- **H8 (next for ShrikeAI, 77,994; from P1-H's report): a ship `[Aux]` PB.** KZ RAYS (`KzRayStep`, as the drones' SEARCH does it) plus the ammo / power readout move to an AuxAI-style PB on Update1 on the Helm ships, about −1.2k on the Helm. StaffAI is Update10, so it cannot take KZ RAYS; the ship Aux can. Needs a placement on each Helm ship (Ballista, Shrike, Corsair, Warden, Leviathan) and a `.KZR/.SCN`-style link on the HELMLINK key.
- **Mantis (77,663, the tightest drone Flight):** R1 + R2 first (about 0.7k); anything beyond that needs a new plan (CLAUDE.md 'Script size': an Opus subagent plans the next split).
- **Staff side:** SURVIVAL STAFF missiles / threat memory → Aux.
- **KZ classification:** rejected unless the owner asks and a sim A/B supports it.
- **Gnat:** see Q1.

## 11. Risks
- Real-game IGC latency and loss (as in staff_split).
- The `MyDetectedEntityInfo` constructor in game (fallback in 4.2).
- The Aux now matters to search, which is a combat degradation if it dies (6.6).
- Config dependence on the Staff (the `Storage` cache mitigates it).
- Drift between the Staff tables and the Flight defaults (test 9.3c).
- Gnat sits at the budget edge until Q1 is answered.

### Critical files for implementation
- /home/user/space-engineers-drones/tools/kz_block.cs and /home/user/space-engineers-drones/tools/ai_gate_block.cs (core and sub-block split; new kzc/kzr/ags/agc block files)
- /home/user/space-engineers-drones/tools/blocks.json (final host lists; new cl/cs/ds/dl/sc/sq blocks)
- /home/user/space-engineers-drones/scripts/MantisAI.cs and /home/user/space-engineers-drones/scripts/MantisStaffAI.cs (the tightest pair; the pattern for all 9 drones)
- /home/user/space-engineers-drones/scripts/AuxAI.cs and /home/user/space-engineers-drones/tools/wp_block.cs (SEARCH, WEAKPOINT RELAY v1.2, AI GATE SETUP)
- /home/user/space-engineers-drones/scripts/ShrikeAI.cs and /home/user/space-engineers-drones/scripts/StaffAI.cs (H1-H6; tools/helmlink_block.cs, tools/manual_block.cs)
- /home/user/space-engineers-drones/tools/designs/_common.py (SUPPORT_AUX += BASILISK, PB_BUDGET in script_text) and /home/user/space-engineers-drones/tools/designs/core.py (Gnat G1a only after Q1)
