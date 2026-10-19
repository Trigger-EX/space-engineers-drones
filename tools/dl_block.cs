// >>> DAMAGE LINK v1.0 (Flight side of the damage-sensing move, docs/design/flight_split.md 3, 4.1, 6.2; tools/dl_block.cs, synced by tools/blocks_sync.py) >>>
// Final hosts: the 9 drone Flights (GnatSwarmAI since flight_split P2-G2: its .DMG comes from GnatStaffAI's GNAT HIT BEARING, after each 2-tick check with a
// drop, <= 30 Hz). The Staff's DAMAGE SENSE (tools/ds_block.cs) runs the
// slim scan and CheckDamage and sends the result; Evade and the jinks stay in the Flight and read _hits / _hitDir as before.
// Channel (TransmissionDistance.CurrentConstruct, key _lkK = "WSPL"+<Flight PB EntityId>):
//   .DMG  Staff -> Flight   MyTuple<int, double, Vector3D, double, int, double> = (hits, integ, hitDir, hitDirAge, blocksHit, maxDrop); hits is a
//         cumulative counter (CheckDamage's _hits), integ the summed integrity (-1 unknown), hitDirAge the age of hitDir at send time (s),
//         blocksHit / maxDrop of the checks since the previous message (capped age 99 s for hitDirAge). After each CheckDamage with a drop (<= 3 Hz),
//         else 1 Hz.
// Receiver: _integ = integ, _hitDir / _hitDirT = hitDir / now - hitDirAge; when hits rose while the link was alive: _hitAt = _now, _hits += the rise,
//   _svNb += blocksHit, _svMax = max(_svMax, maxDrop) (SURVIVAL LINK's per-check stats); then SvDamage() (SURVIVAL LINK) on every message.
// Sync: first message, or a counter that went down (a new Staff): copy the counter, no hit. .DMG silent for 3 s after it was heard: _hitAt = _now
//   once and _integ = -1 (the helm_split rule); the next message counts as a first one.
// Hooks: DlInit() after LkInit(); DlRead() after LkRead(). Uses: _now, _lkK, _hits, _hitAt, _hitDir, _hitDirT, _integ, _svNb, _svMax, SvDamage().
IMyBroadcastListener _dlL; double _dlT = -99; int _dlH = -1;
void DlInit() { _dlL = IGC.RegisterBroadcastListener(_lkK + ".DMG"); }
void DlRead()
{
    while (_dlL.HasPendingMessage)
    {
        var m = _dlL.AcceptMessage();
        if (!(m.Data is MyTuple<int, double, Vector3D, double, int, double>)) continue;
        var d = (MyTuple<int, double, Vector3D, double, int, double>)m.Data;
        if (_dlH >= 0 && d.Item1 > _dlH) { _hitAt = _now; _hits += d.Item1 - _dlH; _svNb += d.Item5; _svMax = Math.Max(_svMax, d.Item6); }
        _dlH = d.Item1; _dlT = _now; _integ = d.Item2;
        if (d.Item4 < 1e3) { _hitDir = d.Item3; _hitDirT = _now - d.Item4; }
        SvDamage();
    }
    if (_dlH >= 0 && _now - _dlT > 3) { _hitAt = _now; _integ = -1; _dlH = -1; }
}
// <<< DAMAGE LINK v1.0 <<<
