// >>> PRINTER GUARD v1.0 (a drone welded by a printer flies nothing until the merge pair lets go; tools/blocks_sync.py) >>>
// Until its merge block lets go, a drone welded from a projection is part of the PRINTER's grid (docs/audit/printers.md printers-01): every
// thruster, gyro and controller this PB finds on "its" grid or construct may be the printer's (owner, 2026-10-14: a Watcher flew the Watcher Pad).
// PgHold() is true while Me.CubeGrid holds a "[Bay" block or the Hive Mother PB (checked once a second): the caller releases its overrides
// once and returns. The tick the drone has split off, the block lists are read again so the printer's thrusters and gyros drop out.
bool _pgFused, _pgWas; double _pgT = -99;
bool PgHold()
{
    if (_now - _pgT >= 1)
    {
        _pgT = _now;
        var l = new List<IMyTerminalBlock>(); var g = Me.CubeGrid;
        GridTerminalSystem.GetBlocksOfType(l, b => b.CubeGrid == g && b != Me && (b.CustomName.Contains("[Bay") || (b is IMyProgrammableBlock && b.CustomData.Contains("[Hive Mother]"))));
        _pgFused = l.Count > 0;
    }
    if (_pgFused) { if (!_pgWas) Release(); _pgWas = true; Echo("Held: still welded to a printer"); return true; }
    if (_pgWas) { _pgWas = false; GetBlocks(); }
    return false;
}
// <<< PRINTER GUARD v1.0 <<<
