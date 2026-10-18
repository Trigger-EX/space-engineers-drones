// >>> PATROL LINK v1.0 (search patrol leg/waypoint from the Staff PB (.PAT), docs/design/survivability.md) >>>
// HUNT's search patrol (nothing found for a while: fly chords of a square round home with listening stops for the AI blocks) is planned by the Staff PB's
// STAFF SEARCH block; this is only the receiver. .PAT (Staff -> Flight, key _lkK, CurrentConstruct): MyTuple<Vector3D point, double speed, int flags>,
// bit0 leg active (fly to the point at that speed, AI blocks off), bit1 listening stop (hold the point, AI blocks may search). Every 30 ticks and on a change;
// 1.5 s stale = no patrol. A target, damage, IDLE or RTB end it here at once, whatever the Staff last said.
// Hooks: PlInit() after LkInit(); "if (PatrolStep()) want = false;" in AiStep (it is the old PatrolStep); _patrolLeg / _patrolWp / PatrolSpeed in Control's
// no-target branch; "_patrolStopSet ? _patrolWp" in Home(). Uses: _now, _tgt, _mode (IDLE/RTB only), UnderFire, _lkK, IGC.
IMyBroadcastListener _plL; Vector3D _patrolWp; double _plT = -99, PatrolSpeed = 60; int _plF; bool _patrolLeg, _patrolStopSet;
void PlInit() { _plL = IGC.RegisterBroadcastListener(_lkK + ".PAT"); }
bool PatrolStep()
{
    while (_plL.HasPendingMessage)
    {
        var m = _plL.AcceptMessage();
        if (!(m.Data is MyTuple<Vector3D, double, int>)) continue;
        var d = (MyTuple<Vector3D, double, int>)m.Data;
        _patrolWp = d.Item1; PatrolSpeed = Math.Max(5, d.Item2); _plF = d.Item3; _plT = _now;
    }
    bool ok = _now - _plT < 1.5 && _tgt == null && !UnderFire && _mode != Mode.IDLE && _mode != Mode.RTB;
    _patrolLeg = ok && (_plF & 1) != 0; _patrolStopSet = ok && (_plF & 2) != 0;
    return _patrolLeg;
}
// <<< PATROL LINK v1.0 <<<
