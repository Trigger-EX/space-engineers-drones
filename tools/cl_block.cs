// >>> CONFIG LINK v1.0 (SKELETON, flight_split P0: header contract only; P1-F writes the code. tools/cl_block.cs, synced by tools/blocks_sync.py) >>>
// Flight / Helm side of the config move (docs/design/flight_split.md sections 3, 4.1, 4.3, 6.1). Final hosts: the 9 drone Flights and ShrikeAI.
// The PB never writes its own Custom Data any more: the Staff's CONFIG SERVER writes every default there and sends the parsed values on .CFG.
// Channel (TransmissionDistance.CurrentConstruct; drone key K = "WSPL"+<Flight PB EntityId>, Helm key "<Ship Tag>."):
//   .CFG  Staff -> Flight/Helm   string: "CF1\t<schema>\t<status>\n" + one line per section (M model, K kamikaze, A AI gate, S survival), values
//         tab-separated in table order. Bools are 1/0, doubles ToString("R"), strings raw. schema = key counts per section + version, generated on
//         both sides from tools/cfg_tables.json. A mismatch is ignored and the screen says "! config schema: update Flight and Staff together".
//         Sent in the tick the Staff sees F2S with bit 16, at most once per 30 ticks per key. Helm sections (4.3): [Shrike] minus Tag/Start Mode,
//         the Helm-side [Manual] keys, [Battery], [Kamikaze Defence], 4 [AI Gate] values.
//   WANT: drone F2S flags bit 16 CFG WANT (SPLIT LINK v1.2: an F2S goes out within 12 ticks while set); Helm H2S flags bit 9 CFG WANT (HELMLINK 1.3).
//   AI ASSIST (runtime AiAssist) rides beside it: F2S bit 17 / H2S bit 10; the Aux (SDK bit 9) or StaffAI does the AI-block setup.
// Contract (names fixed; P1-S, P1-H and P1-T rely on them):
//   listener on CfKey() + ".CFG"; value readers N() (double), B() (bool), T() (string) over the line being applied;
//   CfApply(string): parse, schema check, apply atomically: host hook CfgIn() for the model line (Helm: its section lines), then the section
//     appliers KzSet (K line -> KAMIKAZE DEFENCE fields KzOn..KzShareOn), AgSet4 (A line -> AI GATE core AiGateOn, AiWin, ScriptWin, AiHoldMax),
//     SvSet (S line -> the SURVIVAL LINK fields that SvLoad filled: SvLJ, SvPG, SvJa, SvEv, SvJm, SvJM, SvJr, SvPh); clears WANT; CfSave() keeps the
//     string in Storage; CfLoad() applies the Storage cache at start; bool CfWant is true from start / "reload" until a matching .CFG is applied.
//   The order of values in every line is the order of tools/cfg_tables.json (the K / A / S tables are "shared"; A carries only the core four).
// Host hooks: string CfKey() (drones: _lkK; Helm: "<Ship Tag>."); void CfgIn() (per-model assignments, generated from cfg_tables.json, including the
//   post-ops recorded there: (int) casts, Math.Max clamps, .Trim().ToUpperInvariant()).
// Sync: start and "reload" set WANT; until a matching .CFG arrives the PB runs on its Storage cache, else on its field defaults
//   (screen "cfg: cached" / "cfg: defaults (no Staff)"; after an apply "cfg: Staff"). Tag and Start Mode stay local (Get-only: the IGC channels are
//   registered in the constructor); drone Flights keep the SPLIT LINK key, the Helm keeps [Link]. Runtime words (aim, breach, wp, range, split/focus,
//   ai on|off) last until the next start or "reload"; the Staff never pushes config unasked. Flight rebuilt: new key + WANT. Staff rebuilt: answers the
//   next WANT. Run gap: nothing (the config is in memory).
// <<< CONFIG LINK v1.0 <<<
