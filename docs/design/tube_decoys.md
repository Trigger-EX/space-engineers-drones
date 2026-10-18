# Tube decoys: decoy blocks and flare pods on the Tube 4x4 (sim experiment, 2026-10-17)

Owner request (verbatim): *"test using decoys with the tube drones, lets test on the 4x4 tube for now. think up and experiment with all
different ways of using decoys, for example, detaching them as a form of chaff/countermeasure flare"*. Nothing here has run in the game.

## 1. What a decoy does in the sim (and what it does not)

`sim/src/Combat/Decoys.cs` (sim-audit-1 7.4): a working `SmallDecoy` (alive, functional, switched on) makes
* **target choice**: a turret (`TurretAi`) or an AI block stand-in (`VanillaAi`, which also stands in for the AI Offensive blocks that
  scripted ships such as the Basilisk and Ballista use to pick targets) prefers a body with a working decoy over any body without one,
  and drops its current target for a decoy body that comes into range (`DecoyPreferTarget`, default true);
* **aim point**: on that body it aims at the nearest working decoy, not the centre (`DecoyAimProbability`, default 1, an assumption [I]).

Scripted guns that aim from their own camera rays are not affected. Sim rockets are unguided. A decoy is 64 kg and 276 HP (functional
down to 145 HP): one burst kills an exposed one.

A part that splits off a grid (merge block switched off) becomes its own body with the same owner and velocity, so turrets can pick it.
That is what makes flares possible.

## 2. The ideas tried

| Label | What | Blueprint |
|---|---|---|
| base | plain Tube 4x4 | `Tube 4x4` |
| D-nose | **armoured nose decoys**: 4 decoys in the 2nd nose layer under the front heavy armour, so the aim point is the nose, not the systems at the rear | `Tube 4x4 D` (experiment build) |
| DR-rear | control: 4 decoys right in front of the rear systems (aim pulled onto them) | `Tube 4x4 DR` (experiment build) |
| F-none | flare blueprint, pods never dropped, decoys off: the **mass control** (the flare bay costs 3.7 t) | `Tube 4x4 F`, `Flare Triggers=NONE` |
| F-fire | **flares on fire**: drop one pod each time the hull loses integrity (at most one per 1.5 s) | `Tube 4x4 F` (default) |
| F-range900 / 2000 | drop one pod every 1.5 s once within 900 / 2,000 m of the target | `RANGE` |
| F-all900 | **screen**: all four pods at once at 900 m; they coast on ahead of / beside the tube toward the target | `ALL` |
| F-term | drop in the terminal run (last 2.5 s / 300 m) | `TERM` |
| F-extend | drop while extending after a ram, when the tube is slow and turning | `EXTEND` |
| F-fire+ext | FIRE and EXTEND | |
| F-fire-dodge | FIRE, then side-step away from the pod's line at 20 m/s for 2 s | `Flare Dodge (m/s)=20` |
| lure-1D+3, lure-1F+3 | **lure salvo**: one decoy tube (D) or one flare tube (F) with three plain tubes, so the decoy draws the fire off the others | mixed side |
| sensitivity | base vs F-fire-dodge with `--decoy-aim-probability 0.5`, `--decoy-prefer-target false`, `--decoys-functional false` | |

### 2.1 The flare pod (`Tube 4x4 F`, `tools/designs/tube.py` `_flare_pods`)
A 4-layer flare bay behind the side thrusters (z 6..9), inside the 2 x 2 m section (outline rule). Each **corner** holds a pod along the
tube axis: heavy armour plate (front), decoy (switched OFF), small battery, merge block; the tube's latch merge block sits behind it (z 10).
The bay's edge cells stay **empty** and the cell in front of each pod is a side thruster's nozzle end (no mount there), so a pod touches
the tube only through its merge pair; `flare_pod_parts()` asserts that (the mount graph without the merge-pair edges must fall into the
tube plus four 4-block pods). A 2 x 2 heavy-armour spine carries the hull through the bay (a light-armour spine snapped on the first ram
and cut the PB off). The 3x2x3 battery moved off the corners. The merges are kind `flare` (not docking latches). Cost: 182 blocks, 17.8 t
instead of 14.0 t, 13.0 m/s2 forward instead of 16.4, 8 m long instead of 6.

The pod battery is there because it is not certain the game lets a decoy work without power; drop it if an in-game test shows it does.

### 2.2 Script (`TubeRamAI.cs` v1.4, FLARES)
* Pods: `<Tag> Flare Latch n` (tube side), `<Tag> Flare Merge n`, `<Tag> Flare Decoy n`. Flare merges never count as docked.
* Dropping a pod: its decoy on, then its latch off, in the same tick (the grid splits at the end of it), so the tube itself never carries
  a working decoy. `flare` (command) drops one by hand.
* Hit sensing for FIRE: the integrity of every slim block of the grid, summed every 10 ticks (re-read after each drop).
* Released pods' grid ids go to an ignore list: never a target, a friendly-guard abort or a friendly-avoidance obstacle.
* `[Tube]` keys (only on tubes with pods): `Flare Triggers=FIRE` (any of FIRE, RANGE, TERM, EXTEND, ALL, NONE), `Flare Range (m)=900`,
  `Flare Interval (s)=1.5`, `Flare Dodge (m/s)=0`, `Flare Dodge Time (s)=2`. Status line `Flares: n left, m out`.
* 37,528 characters minified, one PB.

Sim support added: `--pb-custom-data "<Model>=<ini>"` (a key equal to a model name sets every PB of that model; the ram scenarios do not
load blueprint Custom Data).

## 3. Results

Ad-hoc `one --scenario "ram/<Target>-vs-<N>x<model>"` runs. Targets: **Pirate** (code-built, turrets), **Ballista** (warship, PD
turrets + scripted main guns that kill a tube from 1.5-1.9 km), **Basilisk** (scripted fixed guns, AI Offensive target pick, opens fire
at about 600 m). Round 1: 12 configurations x 6 scenarios x 20 seeds. Round 2: the leading flare modes on the 4-tube scenarios, 50 seeds;
sensitivity and lure salvos 20 seeds. Each run takes 0.4-3 s, so these counts were cheap; 50 seeds is where the wins column means something.

Columns: target damage (all sources, kHP), contacts on the target, enemy shots fired, tube life (s, until disabled), wins of 50.

**4 tubes, 50 seeds**

| Config | Pirate dmg | Pirate wins | Ballista dmg | Ballista shots | Ballista life | Basilisk dmg | Basilisk wins | Basilisk life |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| base | 85.0 | 1 | 47.0 | 1,922 | 70 | 9.6 | 0 | 78 |
| F-none (mass control) | 87.4 | 2 | 59.5 | 2,360 | 88 | 11.4 | 3 | 77 |
| **F-fire** | 89.5 | **6** | 68.8 | 3,905 | **141** | **32.2** | **8** | **119** |
| F-fire+ext | 95.7 | 6 | 61.6 | 3,421 | 124 | 31.7 | 5 | 120 |
| F-fire-dodge | 93.5 | 6 | **70.7** | **4,083** | 142 | 33.0 | 3 | 114 |
| F-fire+ext-dodge | 92.6 | 4 | 66.7 | 3,656 | 131 | 29.3 | 2 | 117 |

**1 tube, 20 seeds** (round 1): flares keep a lone tube alive far longer: vs Ballista 17 s (base) -> 75 s (F-fire), 112 s (F-range900),
enemy shots 162 -> 1,300-1,500; vs Basilisk 33 s -> 58-67 s and the tube reaches the hull (contacts 0.15 -> 1.0-1.3); vs Pirate no change
in damage, except F-range2000 (22 -> 32 kHP, life 33 -> 79 s). A lone tube still never wins.

**Round 1, other ideas (20 seeds, 4 tubes unless noted):**
* **Fixed decoys do nothing useful.** D-nose ~ base everywhere (Pirate 95.6 vs 91.0 kHP, Ballista 53.2 vs 51.9, Basilisk 6.8 vs 11.6).
  Head-on, the shots enter through the nose whatever the aim point is, and a decoy on the tube only makes the tube the turrets' first
  choice. DR-rear (aim on the systems) was no worse either: the aim point hardly matters for a tube flying straight at the gun.
* **TERM drops are bad** (Ballista 4 tubes: 24.7 kHP vs 51.9 base): pods released in the last 300 m tumble beside the tube and it rams
  them; the run is spoilt. Never drop late.
* **ALL at 900 m (screen)** and **RANGE** are close to FIRE but not better; RANGE 2000 wastes the pods before the Pirate and Basilisk open
  fire (it is the best against the Ballista's long-range guns for a lone tube, though).
* **EXTEND** alone is no better than F-none; adding it to FIRE adds nothing.
* **Dodge** (step off the pod's line) adds nothing consistent: the side thrusters give 1.6 m/s2, and HUNT turns the nose to burn sideways.
* **Lure salvos**: one decoy or flare tube among three plain ones does not protect the others (Ballista 49.4 / 43.0 kHP vs 51.9 base,
  Basilisk 13.6 / 13.2 vs 11.6). Flares protect the tube that carries them.

**Sensitivity (F-fire-dodge, 4 tubes, 20 seeds; base is unchanged by these settings since it carries no decoy):**

| Setting | Pirate dmg | Ballista dmg / life | Basilisk dmg / wins |
|---|--:|--:|--:|
| default (aim 1, prefer on) | 97.9 | 68.1 / 139 s | 39.1 / 2 |
| aim probability 0.5 | 94.3 | 55.8 / 146 s | 30.5 / 5 |
| prefer target off | 92.5 | 64.2 / 82 s | 15.6 / 0 |
| decoys inert | 98.0 | 60.6 / 95 s | 16.1 / 0 |
| (base) | 91.0 | 51.9 / 71 s | 11.6 / 0 |

So the gain rests on **target choice** (a decoy grid is picked before the tube), not on the aim point: with `DecoyPreferTarget` off it
mostly goes (the Basilisk gain drops from 3x to 1.3x). Some of it remains with decoys inert: loose pods soak up shots and the heavier tube
rams harder. How strongly real turrets and AI blocks prefer a separate decoy grid is the open question that decides this.

## 4. Conclusions and defaults
* Default `Flare Triggers=FIRE`, `Flare Interval (s)=1.5`, `Flare Dodge (m/s)=0`.
* Flares are the decoy use that works: against turret- or AI-picked targets a Tube 4x4 F lives about twice as long and draws 2x as many
  enemy shots; 4 of them do 3.3x the base damage to a Basilisk (8 wins of 50 vs 0) and 1.5x to a Ballista; against the Pirate +5 % damage,
  6 wins of 50 vs 1.
* Fixed decoys on a ram drone: not worth it (they only make it the first target). Lure tubes: no help to the salvo.
* Cost: 3.7 t and 2 m of length for four pods (acceleration 16.4 -> 13.0 m/s2); F-none shows the mass alone is not the gain.

## 5. Not known / in-game test (Q)
* Whether a SmallDecoy needs power (the pods carry a battery to be safe).
* How real turrets weigh a decoy grid against the tube (`PriorityModifier 10` is per block; the sim makes any decoy grid win).
* Whether the scripted ships' AI Offensive blocks switch to a decoy grid in the game as the sim's stand-in does.
* Whether two merge blocks of one grid split it cleanly when one is switched off inside a recess (the printers' merge release is owner-confirmed;
  this layout is not), and whether a released pod scrapes the tube on its way out.
* Test: paste a Tube 4x4 F next to a pirate base or a ship with turrets, `flare`, watch the turrets switch; then a full run.

## 6. Next steps (not done)
* The same flare bay on the Tube 3x3 / 2x4 / W variants (needs a recess per section; the 2x2 has no room inside its outline).
* A pod without battery if the game does not need it (lighter, more pods).
