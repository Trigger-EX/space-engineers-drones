// >>> SEARCH v1.0 (SKELETON, flight_split P0: header contract only; P1-X writes the code. tools/sc_block.cs, synced by tools/blocks_sync.py) >>>
// Camera and sensor search moves Flight -> Aux (docs/design/flight_split.md sections 2, 3, 4.1, 6.3, 6.6). Final host: AuxAI (model-independent).
// Content: auto-scan, omni-scan (all-round close scan), hit-look, the manual lock ray, sensor poll, KZ rays from the .KZR table, own CamFor / Forwardish.
// Finds NEW contacts only; the tracked target (MaintainLock, designators, Track, Lead) stays in the Flight. Never writes an override.
// Channels (TransmissionDistance.CurrentConstruct, key K = "WSPL"+<Flight PB EntityId>):
//   .SCS  Flight -> Aux   MyTuple<int flags, long tgtId, Vector3D lostPos, Vector3D lostVel, Vector3D hitDir, MyTuple<Vector3D,Vector3D,Vector3D,Vector3D> p>
//         Every 6 ticks + at once on a flag change. Stale at the Aux after 30 ticks: the Aux stops all scanning.
//         flags: bit0 AUTO (no target, AI off, AutoScan on, mode GUARD/HUNT/FOLLOW), bit1 OMNI (mode not IDLE and CloseScan on), bit2 HARD (UnderFire or
//         Alerted), bit3 has target, bit4 AI on, bit5 SENSORS, bit6 HITLOOK (off for the Gnat variant), bit7 KZRAYS, bits 8-15 lock request counter,
//         bit17 lock camera uses Forwardish (0.9) instead of 0.99.
//         p: a = (auto-scan range, scan cone in degrees, spiral N); b = (close-scan range, 900, lock range); c = (lost age, hit-dir age, lost radius);
//         d = (KzPack, KzReserve, 0). The model differences (ScanRange vs WeaponRange x 1.1, N 120/150, Gnat without HitPoint, lock rule and range)
//         are carried in p and the flags.
//   .KZR  Flight -> Aux   MyTuple<long id, Vector3D pos, Vector3D vel, double radius, double age, int flags>: bit0 is the Flight's target, bits 8-11 index,
//         12-15 count, 16-31 pass number. From the KzRayStep() proxy, <= 4 per KZ pass (6 Hz), only while threats exist.
//   .SCN  Aux -> Flight   MyTuple<long id, int f, Vector3D hit, Vector3D vel, MyTuple<Vector3D,Vector3D> box, MyTuple<Vector3D,Vector3D> ori(fwd, up)>:
//         f bits 0-7 entity type, 8-15 relationship, 16 has hit, 20-22 source (1 SCAN, 2 SENSOR, 3 KZ, 4 LOCK), 24-27 lock result (0 ok, 1 no forward
//         camera, 2 nothing hit with the charge in hit.X). id 0 = heartbeat. Per non-empty hit, except own grid and planets; friendlies and asteroids
//         are sent because FaSee uses them. About <= 6 per tick. Heartbeat every 30 ticks.
// Sync: scans only while .SCS is <= 30 ticks old. Aux rebuilt: re-binds to the nearest [Flight] (existing rule), resumes on the next .SCS (<= 6 ticks).
//   Flight rebuilt: new key; the Aux re-scans at 1 Hz while stale. Aux run gap: drops .KZR entries older than 0.5 s.
// <<< SEARCH v1.0 <<<
