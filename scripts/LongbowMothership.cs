// =====================================================================
//  LONGBOW MOTHERSHIP LINK v1.0  (optional - put on your carrier's PB)
//  * Broadcasts this ship's position as the drones' moving home point.
//  * Forwards every enemy your turrets / turret controllers are tracking
//    to all drones, so they attack what the carrier sees.
//  * Relays commands:  run with argument  guard | hunt | rtb | idle | unlock
//  Needs an antenna on this ship. Tag must match the drones' "IGC Tag".
// =====================================================================
const string Tag = "LONGBOW";
List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
List<IMyTurretControlBlock> _ctcs = new List<IMyTurretControlBlock>();
int _tick = 0;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    Refresh();
}

void Refresh()
{
    GridTerminalSystem.GetBlocksOfType(_turrets, b => b.IsSameConstructAs(Me));
    GridTerminalSystem.GetBlocksOfType(_ctcs, b => b.IsSameConstructAs(Me));
}

public void Main(string arg, UpdateType src)
{
    if (!string.IsNullOrWhiteSpace(arg))
    {
        IGC.SendBroadcastMessage(Tag + "_CMD", arg.Trim());
        Echo("Sent: " + arg);
    }
    if ((src & UpdateType.Update10) == 0) return;
    if (++_tick % 60 == 0) Refresh();

    IGC.SendBroadcastMessage(Tag + "_HOME", Me.CubeGrid.WorldVolume.Center);

    int sent = 0;
    foreach (var t in _turrets)
        if (t.IsFunctional && t.HasTarget) { Send(t.GetTargetedEntity()); sent++; }
    foreach (var c in _ctcs)
        if (c.IsFunctional && c.HasTarget) { Send(c.GetTargetedEntity()); sent++; }
    Echo("Longbow link: " + sent + " target(s) shared\nDesignators: " + (_turrets.Count + _ctcs.Count));
}

void Send(MyDetectedEntityInfo e)
{
    if (e.IsEmpty() || e.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) return;
    IGC.SendBroadcastMessage(Tag + "_TGT", new MyTuple<long, Vector3D, Vector3D, double, long>(
        e.EntityId, e.Position, new Vector3D(e.Velocity), e.BoundingBox.Size.Length() * 0.5, Me.EntityId));
}
