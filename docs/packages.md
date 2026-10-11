# Packaging (tools/build_all.py)

`out/` is gitignored and rebuilt by one command:

    python3 -I tools/build_all.py [--jobs N] [--only "Name" ...] [--out DIR] [--no-zip]

## What it does

1. `tools/designs/__init__.py` (`designs.load()`) imports the design modules `core, wasp, stinger, support, extra` (drones) and `printers,
   printers_timer` (printers). A module whose file does not exist is reported and skipped (`stinger` and `printers_timer` did not exist
   when this was written). Each module exposes `MODELS = {"<Name>": callable(lite) -> sebp.Blueprint}`.
2. Every model is built in both variants, `<Name>` and `<Name> Lite`, in a fork pool (the printer bay solver is slow), and written to
   `out/Blueprints/<blueprint name>/{bp.sbc,thumb.png}`. Folders of models that no longer exist are removed.
3. Each blueprint is re-validated:
   * `Blueprint.validate()`: every block attached through real mount points, one connected piece, no overlaps, clear zones free, no DLC;
   * independent DLC check against `defs_stats.json` (`dlc` field of every block);
   * the written `bp.sbc`: well formed, no `<DLC>`, no `<IsStatic>` (printers and drones must be dynamic), no stale `bp.sbcB5`, block count
     equal to the model, `GridSizeEnum` right, unique block `EntityId`s and unique `CustomName`s, a `<Program>` in every scripted ship;
   * a reload of the written file with `sebp.load_sbc(allow_dlc=False)`: the main grid and every embedded projected grid (the printers'
     pre-loaded drones) must validate again and lose no blocks;
   * the docking face check (`_common.check_docking`) for drones with a merge block;
   * across all blueprints: unique folder names and unique blueprint ids.
4. `out/dist/MODELS.md` (master table: blocks, mass, size, thrust-to-weight along the six axes, PCU, merge blocks, scripts, cost) and
   `out/dist/readmes/*_README.md`.
5. Zips in `out/dist/` (fixed timestamps, so the zip is reproducible byte for byte for equal inputs):

| Zip | Blueprints | Scripts |
|---|---|---|
| `Drones_v2.0_Full.zip` | all drones, Full | the drone scripts they embed, the three optional carrier-link scripts, `MidgeNotes.md` |
| `Drones_v2.0_Lite.zip` | all drones, Lite | same |
| `Printers_v2.0_Full.zip` | all printers, Full | `HiveMotherAI.cs` and every drone script embedded in a projector |
| `Printers_v2.0_Lite.zip` | all printers, Lite | same |
| `SpaceEngineers_Drones_v2.0_All.zip` | everything | union, plus `MODELS.md` |

Each zip holds `Blueprints/<Name>/{bp.sbc,thumb.png}`, `Scripts/*`, `README.md`. A script counts as "used" when its text is found in the written
XML (the PB `<Program>` of a ship, or of a drone inside a projector), so the list cannot drift from the blueprints.

Exit code 1 if any model fails a check; failed models are left out of the zips and listed at the end of the run and in `MODELS.md`.

## README templates (`packages/`)

| File | Purpose |
|---|---|
| `README_Drones.md.tmpl`, `README_Printers.md.tmpl`, `README_All.md.tmpl` | page skeletons with `{{PLACEHOLDERS}}` |
| `install.md` | install steps (delete old blueprint folders first, F10 paste), Full vs Lite |
| `unknowns.md` | known unknowns; every line is tagged `[drone]`, `[printer]` or `[both]` and filtered per package |
| `tests.md` | the ordered in-game tests (research.md section 10 and xml-notes.md section 5, merged by risk); same tags, renumbered per package |
| `changelog.md` | v2.0 entry plus HANDOFF section 12 (v1.0 to v1.8) |

Generated automatically from the data: the model table and per-model sections (blocks, mass, size, thrust-to-weight, PCU, script, cost, the
printers' `DOCS` line), and the **command and Custom Data reference of every script in the package**, extracted from the scripts' own
`case "<command>"` labels and `Cfg(S, "<key>")` / `C(S, "<key>")` calls. Edit the descriptions of drones in `DRONE_DOCS` / `MODEL_NOTES` in `tools/build_all.py`.

## Adding a model

Add a module to `tools/designs/` with `MODELS`, list its name in `designs.DRONE_MODULES` or `PRINTER_MODULES` if it is new, add a one-line
description to `DRONE_DOCS` (drones) or `DOCS` (printers), and run `build_all.py`. Model names must be unique across modules and the
blueprint's `name` must be `<Name>` / `<Name> Lite`.
