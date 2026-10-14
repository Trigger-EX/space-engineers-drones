// >>> FRIENDLY BEACON v1.0 (shared block, synced by tools/blocks_sync.py, see docs/friendly-avoidance.md) >>>
// The sending half of FRIENDLY AVOIDANCE for the scripts that have no room for the whole block (Mantis, Wasp: 100,000-character limit) and
// for the Shrike Helm (80,900 budget): broadcasts this grid's bounding sphere (id, centre, velocity, radius) on the IGC channel DRONE_SPACE,
// so drones that do avoid friendly grids keep clear of this one. Hook: FaBeacon() in BroadcastStatus() (10 Hz). Uses _rc.
void FaBeacon()
{
    var v = Me.CubeGrid.WorldVolume;
    IGC.SendBroadcastMessage("DRONE_SPACE", new MyTuple<long, Vector3D, Vector3D, float>(Me.CubeGrid.EntityId, v.Center, _rc.GetShipVelocities().LinearVelocity, (float)v.Radius));
}
// <<< FRIENDLY BEACON v1.0 <<<
