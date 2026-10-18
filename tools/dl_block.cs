// >>> DAMAGE LINK v1.0 (SKELETON, flight_split P0: header contract only; P1-F writes the code. tools/dl_block.cs, synced by tools/blocks_sync.py) >>>
// Flight side of the damage-sensing move (docs/design/flight_split.md sections 3, 4.1, 6.2). Final hosts: the 8 drone Flights (the Gnat keeps its local
// ScanSlims / CheckDamage until Q1).
// Channel (TransmissionDistance.CurrentConstruct, key K = "WSPL"+<Flight PB EntityId>):
//   .DMG  Staff -> Flight   MyTuple<int hits, double integ, Vector3D hitDir, double hitDirAge, int blocksHit, double maxDrop>; hits is a cumulative counter.
//         After each CheckDamage with a drop (<= 3 Hz), else 1 Hz.
// Receiver: fills _hits / _hitAt / _hitDir / _hitDirT / _integ / _svNb / _svMax, then calls SvDamage() (SURVIVAL LINK). Evade and the jinks stay in the
// Flight and read _hits / _hitDir as before.
// Sync: first message, or a counter that went down (a new Staff): copy the value. Counter up while the link is alive: _hitAt = _now; _hits++.
//   .DMG silent for 3 s after it was heard: _hitAt = _now once, _integ = -1 (the helm_split rule).
// <<< DAMAGE LINK v1.0 <<<
