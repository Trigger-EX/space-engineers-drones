// >>> CONFIG SERVER v1.0 (SKELETON, flight_split P0: header contract only; P1-S writes the code. tools/cs_block.cs, synced by tools/blocks_sync.py) >>>
// Staff side of the config move (docs/design/flight_split.md sections 3, 4.1, 6.1). Final hosts: the 9 drone Staffs and StaffAI (Helm ships).
// The only writer of the target PB's Custom Data (the Flight / Helm PB): a generic table-driven MyIni default writer and reader. It writes every
// missing default, parses the values, builds the .CFG string and sends it.
// Tables: the KZ, AI Gate and Survival tables are in this block (shared, from tools/cfg_tables.json "shared"); the model table comes from the host.
// Channel (TransmissionDistance.CurrentConstruct):
//   .CFG  Staff -> Flight/Helm   string: "CF1\t<schema>\t<status>\n" + one line per section (M model, K kamikaze, A AI gate, S survival), values
//         tab-separated in table order. Bools are 1/0, doubles ToString("R"), strings raw. schema = key counts per section + version, generated on
//         both sides from tools/cfg_tables.json. Sent in the tick the Staff sees F2S with bit 16 CFG WANT (Helm: H2S bit 9), at most once per 30 ticks
//         per key. The Staff never pushes config unasked.
//   The four AI GATE SETUP values (Switch On AI Behavior, Use Typed AI Block API, AI Flight Speed Limit, AI Search Interval) do not go on .CFG: drone
//   Staffs pass them to the Aux in the SDK cfg ("Tag\nSeekerGroup\nflChan\nwpPri\nwpExp\nagSwitch|agTyped|agSpeed|agEvery", AUX LINK v1.1);
//   StaffAI (and GnatStaffAI under G2) use them with their own AI GATE SETUP block.
// Host hooks: IMyProgrammableBlock CfTarget() (the Flight / Helm PB whose Custom Data is served); string[] CfModel() (per-model table, flat
//   key/default/type triples in cfg_tables.json order; type d double, i int, b bool, s string); string CfKey() (the target's link key + ".CFG" prefix:
//   drones "WSPL"+<Flight PB EntityId>, Helm "<Ship Tag>."); CfExtra(...) (an override after the parse: DetectWeapon for Longbow and Mantis).
// Sync: answers every WANT (Staff rebuilt: nothing to do); re-keys on the existing 1 Hz rescan while the Flight is down (Flight rebuilt).
// <<< CONFIG SERVER v1.0 <<<
