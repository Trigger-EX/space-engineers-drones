// >>> SEARCH LINK v1.0 (SKELETON, flight_split P0: header contract only; P1-F writes the code. tools/sq_block.cs, synced by tools/blocks_sync.py) >>>
// Flight side of the search move (docs/design/flight_split.md sections 3, 4.1, 4.2, 6.3, 6.6). Final hosts: the 8 drone Flights (not the Gnat under G2).
// Content: the .SCS sender, the .KZR sender (the KzRayStep() proxy the KAMIKAZE DEFENCE v1.1 core calls; replaces KZ RAYS), the .SCN receiver
// (rebuilds MyDetectedEntityInfo, Valid(), AddContact(e, src)), lock results and messages, the Aux heartbeat.
// Channels (TransmissionDistance.CurrentConstruct, key K = "WSPL"+<Flight PB EntityId>):
//   .SCS  Flight -> Aux   MyTuple<int flags, long tgtId, Vector3D lostPos, Vector3D lostVel, Vector3D hitDir, MyTuple<Vector3D,Vector3D,Vector3D,Vector3D> p>
//         Every 6 ticks + at once on a flag change. Stale at the Aux after 30 ticks: the Aux stops all scanning.
//         flags: bit0 AUTO (no target, AI off, AutoScan on, mode GUARD/HUNT/FOLLOW), bit1 OMNI (mode not IDLE and CloseScan on), bit2 HARD (UnderFire or
//         Alerted), bit3 has target, bit4 AI on, bit5 SENSORS, bit6 HITLOOK (off for the Gnat variant), bit7 KZRAYS, bits 8-15 lock request counter,
//         bit17 lock camera uses Forwardish (0.9) instead of 0.99.
//         p: a = (auto-scan range, scan cone in degrees, spiral N); b = (close-scan range, 900, lock range); c = (lost age, hit-dir age, lost radius);
//         d = (KzPack, KzReserve, 0).
//   .KZR  Flight -> Aux   MyTuple<long id, Vector3D pos, Vector3D vel, double radius, double age, int flags>: bit0 is the Flight's target, bits 8-11 index,
//         12-15 count, 16-31 pass number. From the KzRayStep() proxy, <= 4 per KZ pass (6 Hz), only while threats exist.
//   .SCN  Aux -> Flight   MyTuple<long id, int f, Vector3D hit, Vector3D vel, MyTuple<Vector3D,Vector3D> box, MyTuple<Vector3D,Vector3D> ori(fwd, up)>:
//         f bits 0-7 entity type, 8-15 relationship, 16 has hit, 20-22 source (1 SCAN, 2 SENSOR, 3 KZ, 4 LOCK), 24-27 lock result (0 ok, 1 no forward
//         camera, 2 nothing hit with the charge in hit.X). id 0 = heartbeat. About <= 6 per tick. Heartbeat every 30 ticks.
// Rebuild (4.2): new MyDetectedEntityInfo(id, "", type, hit?, MatrixD.CreateWorld(c, fwd, up), vel, rel, box, 0), then Valid(e) (FaSee, SvSee), then
//   AddContact(e, src). Fallback if the game rejects the constructor: AddContactRaw(...), about +0.3k.
// Sync: Flight run gap: send .SCS at once and drop queued .SCN. Aux silent (no heartbeat): screen "! Aux PB silent: camera search off".
// <<< SEARCH LINK v1.0 <<<
