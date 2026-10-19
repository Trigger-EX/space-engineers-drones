// >>> SEARCH LINK v1.0 (Flight side of the search move, docs/design/flight_split.md 3, 4.1, 4.2, 6.3, 6.6; tools/sq_block.cs, synced by tools/blocks_sync.py) >>>
// Final hosts: the 8 drone Flights (not the Gnat under G2). The Aux PB's SEARCH block (tools/sc_block.cs) casts the auto-scan, the all-round close scan,
// the hit-look, the manual lock ray, the sensor poll and the KZ rays; this block tells it what to do and rebuilds the contacts it finds. The tracked
// target (MaintainLock, designators, Track, Lead) stays in the Flight.
// Channels (TransmissionDistance.CurrentConstruct, key _lkK = "WSPL"+<Flight PB EntityId>):
//   .SCS  Flight -> Aux   MyTuple<int, long, Vector3D, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>>
//         = (flags, tgtId, lostPos, lostVel, hitDir, p). Every 6 ticks + at once on a flag change (and after a run gap). Stale at the Aux after 30 ticks.
//         flags: bit0 AUTO (the host's old auto-scan condition, e.g. no target, AI off, AutoScan on, mode GUARD/HUNT/FOLLOW), bit1 OMNI (mode not IDLE
//         and CloseScan on), bit2 HARD (UnderFire or Alerted), bit3 has target, bit4 AI on, bit5 SENSORS, bit6 HITLOOK, bit7 KZRAYS (KzRays), bits 8-15
//         lock request counter (+1 per "lock" word), bit17 lock camera uses Forwardish (0.9) instead of 0.99. Bits 0, 5, 6 and 17 come from the host's
//         int SqF() (the 8: AUTO | 96; Gnat: AUTO (its own condition) | bit17, no sensor, no hit-look).
//         lostPos / lostVel: the dropped target's last state (NoteLost; the Aux extrapolates lostPos + lostVel * min(lost age, 10)).
//         p: a = (auto-scan range, scan cone in degrees, spiral N) from the host's SqA(); b = (close-scan range, 900 = hard-scan range, lock range);
//         c = (lost age, hit-dir age, lost radius) in s, s, m; d = (KzPack, KzReserve, 0).
//   .KZR  Flight -> Aux   MyTuple<long, Vector3D, Vector3D, double, double, int> = (id, pos, vel, radius, age, flags): one per threat of _kz (most urgent
//         first), <= 4 per KZ pass (6 Hz), only while threats exist (KzAssess calls KzRayStep() then). pos / vel are the track extrapolated to the send
//         time (Track.P / V), age = now - last sighting. flags: bit0 the Flight's target, bits 8-11 index, 12-15 count, 16-31 pass number (low 16 bits).
//   .SCN  Aux -> Flight   MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>> = (id, f, hit, vel, box(min, max),
//         ori(fwd, up)). f bits 0-7 entity type, 8-15 relationship, 16 has hit, 20-22 source (1 SCAN, 2 SENSOR, 3 KZ, 4 LOCK), 24-27 lock result (0 ok,
//         1 no forward camera, 2 nothing hit with the charge in hit.X). id 0 = heartbeat (every 30 ticks); any message counts as a heartbeat.
// Rebuild (4.2): new MyDetectedEntityInfo(id, "", type, hit?, MatrixD.CreateWorld(box centre, fwd, up), vel, rel, box, 1) (TimeStamp 1: 0 would read
//   IsEmpty()), then Valid(e) (FaSee, SvSee) and AddContact(e, "SCAN" / "SENSOR" / "KZ"). A LOCK result is not Valid()-filtered (as ManualLock was):
//   friendly -> "Lock: target is friendly", else it becomes _tgt. Fallback if the game rejects the constructor: AddContactRaw(...), about +0.3k.
// Sync: Flight run gap: SqGap() in the run-gap line sends .SCS at once and drops queued .SCN. Aux silent for 2 s: SqStatus() = "! Aux PB silent: camera
//   search off". Hooks: SqInit() after LkInit(); SqRead() after LkRead(); SqSend() after LkSend(); "lock" -> _sqLk++; SqStatus() on the screen; host
//   Vector3D SqA(), int SqF(). Uses: _now, _tick, _lkK, _tgt, _aiOn, _mode (IDLE), CloseScan, CloseScanRange, LockRange, UnderFire, Alerted, KzRays, KzPack,
//   KzReserve, _kz, _kzRay, _lostPos, _lostVel, _lostT, _lostRad, _hitDir, _hitDirT, _err, Valid, Friendly, AddContact.
IMyBroadcastListener _sqL; double _sqT = -99; int _sqF = -1, _sqLk;
void SqInit() { _sqL = IGC.RegisterBroadcastListener(_lkK + ".SCN"); }
void SqGap() { _sqF = -1; while (_sqL.HasPendingMessage) _sqL.AcceptMessage(); }
string SqStatus() { return _now - _sqT > 2 ? "\n! Aux PB silent: camera search off" : ""; }
void SqSend()
{
    int f = SqF() | (_mode != Mode.IDLE && CloseScan ? 2 : 0) | (UnderFire || Alerted ? 4 : 0) | (_tgt != null ? 8 : 0) | (_aiOn ? 16 : 0) | (KzRays ? 128 : 0) | (_sqLk & 255) << 8;
    if (f == _sqF && _tick % 6 != 1) return;
    _sqF = f;
    IGC.SendBroadcastMessage(_lkK + ".SCS", new MyTuple<int, long, Vector3D, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>>(f, _tgt != null ? _tgt.Id : 0,
        _lostPos, _lostVel, _hitDir, new MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>(SqA(), new Vector3D(CloseScanRange, 900, LockRange),
        new Vector3D(_now - _lostT, _now - _hitDirT, _lostRad), new Vector3D(KzPack, KzReserve, 0))), TransmissionDistance.CurrentConstruct);
}
void SqRead()
{
    while (_sqL.HasPendingMessage)
    {
        var m = _sqL.AcceptMessage();
        if (!(m.Data is MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>>)) continue;
        var d = (MyTuple<long, int, Vector3D, Vector3D, MyTuple<Vector3D, Vector3D>, MyTuple<Vector3D, Vector3D>>)m.Data;
        _sqT = _now;
        if (d.Item1 == 0) continue;
        int f = d.Item2, s = f >> 20 & 7, r = f >> 24 & 15;
        if (s == 4 && r > 0) { _err = r == 1 ? "No forward camera for lock" : "Lock: nothing hit (range " + d.Item3.X.ToString("0") + " m)"; continue; }
        var b = new BoundingBoxD(d.Item5.Item1, d.Item5.Item2);
        var e = new MyDetectedEntityInfo(d.Item1, "", (MyDetectedEntityType)(f & 255), (f & 65536) != 0 ? d.Item3 : (Vector3D?)null,
            MatrixD.CreateWorld(b.Center, d.Item6.Item1, d.Item6.Item2), (Vector3)d.Item4, (MyRelationsBetweenPlayerAndBlock)(f >> 8 & 255), b, 1);
        if (s == 4) { if (Friendly(e)) _err = "Lock: target is friendly"; else { _err = ""; _tgt = AddContact(e, "LOCK"); } }
        else if (Valid(e)) AddContact(e, s == 2 ? "SENSOR" : s == 3 ? "KZ" : "SCAN");
    }
}
// KAMIKAZE DEFENCE v1.1 hook (replaces KZ RAYS): the Aux casts the refresh and pack rays for these threats and returns the hits as source KZ
void KzRayStep()
{
    int n = Math.Min(4, _kz.Count); _kzRay++;
    for (int i = 0; i < n; i++)
    {
        var t = _kz[i].T;
        IGC.SendBroadcastMessage(_lkK + ".KZR", new MyTuple<long, Vector3D, Vector3D, double, double, int>(t.Id, t.P(_now), t.V(_now), t.Radius, _now - t.Seen,
            (t == _tgt ? 1 : 0) | i << 8 | n << 12 | (_kzRay & 65535) << 16), TransmissionDistance.CurrentConstruct);
    }
}
// <<< SEARCH LINK v1.0 <<<
