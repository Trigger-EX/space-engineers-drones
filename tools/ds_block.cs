// >>> DAMAGE SENSE v1.0 (damage sensing moved Flight -> Staff, docs/design/flight_split.md sections 2, 3, 4.1, 6.2; tools/ds_block.cs, synced by tools/blocks_sync.py) >>>
// Final hosts: the 8 drone Staffs. GnatStaffAI does not carry it: its own GNAT HIT BEARING section (2-tick checks plus the hit-bearing estimator, flight_split P2-G2)
// sends the same .DMG tuple.
// Content: the Flight's ScanSlims / StepSlimScan / CheckDamage verbatim (a pass over the grid's cells, 200 cells per tick while the instruction count is
// below 60 %; CheckDamage every 20 ticks), plus the .DMG sender. The Staff's own survival logic keeps reading damage from .SVF (SURVIVAL LINK in the Flight,
// fed by DAMAGE LINK).
// Channel (TransmissionDistance.CurrentConstruct, key K = "WSPL"+<Flight PB EntityId> = _fk):
//   .DMG  Staff -> Flight   MyTuple<int hits, double integ, Vector3D hitDir, double hitDirAge, int blocksHit, double maxDrop>; hits is a cumulative counter
//         (+1 per CheckDamage whose integrity sum fell by more than 0.5), integ the integrity sum (-1 while a pass has not been compared yet), hitDir the
//         smoothed hit direction (world, from the grid centre), hitDirAge the seconds since it was set (99 = never), blocksHit / maxDrop the blocks that
//         lost integrity and the largest drop since the previous .DMG (the Flight's _svNb / _svMax). After each CheckDamage with a drop (<= 3 Hz),
//         else 1 Hz.
// Receiver: DAMAGE LINK (tools/dl_block.cs) in the Flight.
// Hooks: ScanSlims() in GetBlocks; DsStep() once a tick (before the no-controller return). Uses: _fk, _now, _tick, Me, IGC.
List<IMySlimBlock> _slims = new List<IMySlimBlock>();
List<double> _slimHp = new List<double>(); Vector3D _hitDir; double _hitDirT = -99, _integ = -1, _svMax, _dsT = -99; int _hits, _svNb;
List<IMySlimBlock> _slimsNew = new List<IMySlimBlock>(); HashSet<Vector3I> _slimSeen = new HashSet<Vector3I>();
IMyCubeGrid _slimsGrid, _scanGrid; bool _slimBusy = false; int _sx, _sy, _sz, _mnx, _mny, _mnz, _mxx, _mxy, _mxz;
const int SlimCellsPerTick = 200, SlimMaxCells = 30000;
void ScanSlims()
{
    var g = Me.CubeGrid;
    if (_slimsGrid != g) { _slims.Clear(); _integ = -1; }
    _slimBusy = false;
    Vector3I mn = g.Min, mx = g.Max;
    if ((long)(mx.X - mn.X + 1) * (mx.Y - mn.Y + 1) * (mx.Z - mn.Z + 1) > SlimMaxCells) return;   // docked to something big: skip
    _scanGrid = g; _slimsNew.Clear(); _slimSeen.Clear();
    _mnx = mn.X; _mny = mn.Y; _mnz = mn.Z; _mxx = mx.X; _mxy = mx.Y; _mxz = mx.Z;
    _sx = _mnx; _sy = _mny; _sz = _mnz; _slimBusy = true;
}
void StepSlimScan()
{
    if (!_slimBusy) return;
    var g = _scanGrid;
    if (g != Me.CubeGrid) { _slimBusy = false; return; }
    for (int n = 0; n < SlimCellsPerTick; n++)
    {
        var p = new Vector3I(_sx, _sy, _sz);
        if (g.CubeExists(p)) { var s = g.GetCubeBlock(p); if (s != null && _slimSeen.Add(s.Position)) _slimsNew.Add(s); }
        if (++_sz > _mxz) { _sz = _mnz; if (++_sy > _mxy) { _sy = _mny; if (++_sx > _mxx) { _slimBusy = false; break; } } }
    }
    if (_slimBusy) return;
    _slims.Clear(); _slims.AddRange(_slimsNew); _slimsGrid = g; _integ = -1;   // swap in the finished pass
}
void CheckDamage()
{
    double sum = 0; var g = Me.CubeGrid;
    // which side was hit: blocks that lost integrity since the last check, as directions from the grid centre (the shooter is out that way)
    bool cmp = _integ >= 0 && _slimHp.Count == _slims.Count;
    if (!cmp) { _slimHp.Clear(); for (int i = 0; i < _slims.Count; i++) _slimHp.Add(0); }
    Vector3D hd = Vector3D.Zero, gc = g.WorldVolume.Center;
    for (int i = 0; i < _slims.Count; i++)
    {
        var s = _slims[i];
        double v = g.CubeExists(s.Position) ? s.BuildIntegrity - s.CurrentDamage : 0;
        sum += v;
        if (cmp && _slimHp[i] - v > 0.5) { _svNb++; _svMax = Math.Max(_svMax, _slimHp[i] - v); Vector3D d = g.GridIntegerToWorld(s.Position) - gc; double l = d.Length(); if (l > 0.3) hd += d / l; }
        _slimHp[i] = v;
    }
    if (hd.LengthSquared() > 0.01)
    {
        hd.Normalize();
        Vector3D m = _now - _hitDirT < 3 ? _hitDir * 0.6 + hd * 0.4 : hd;
        if (m.LengthSquared() > 0.01) { _hitDir = Vector3D.Normalize(m); _hitDirT = _now; }
    }
    bool hit = _integ >= 0 && sum < _integ - 0.5;
    if (hit) _hits++;
    _integ = sum;
    if (_fk == "" || (!hit && _svNb == 0 && _now - _dsT < 1)) return;
    IGC.SendBroadcastMessage(_fk + ".DMG", new MyTuple<int, double, Vector3D, double, int, double>(_hits, _integ, _hitDir, Math.Min(99, _now - _hitDirT), _svNb, _svMax),
        TransmissionDistance.CurrentConstruct);
    _dsT = _now; _svNb = 0; _svMax = 0;
}
void DsStep()
{
    if (Runtime.CurrentInstructionCount < Runtime.MaxInstructionCount * 0.6) StepSlimScan();
    if (_tick % 20 == 7) CheckDamage();
}
// <<< DAMAGE SENSE v1.0 <<<
