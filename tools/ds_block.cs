// >>> DAMAGE SENSE v1.0 (SKELETON, flight_split P0: header contract only; P1-S writes the code. tools/ds_block.cs, synced by tools/blocks_sync.py) >>>
// Damage sensing moves Flight -> Staff (docs/design/flight_split.md sections 2, 3, 4.1, 6.2). Final hosts: the 8 drone Staffs (GnatStaffAI only after
// Q1 = yes; under G2 the Gnat Flight keeps its local sensing).
// Content: the Flight's ScanSlims / StepSlimScan / CheckDamage verbatim (pass every 20 ticks, 200 cells per tick), plus the .DMG sender.
// Channel (TransmissionDistance.CurrentConstruct, key K = "WSPL"+<Flight PB EntityId>):
//   .DMG  Staff -> Flight   MyTuple<int hits, double integ, Vector3D hitDir, double hitDirAge, int blocksHit, double maxDrop>; hits is a cumulative counter.
//         After each CheckDamage with a drop (<= 3 Hz), else 1 Hz.
// Receiver: DAMAGE LINK (tools/dl_block.cs) in the Flight. The Staff's own survival logic keeps using the values directly.
// <<< DAMAGE SENSE v1.0 <<<
