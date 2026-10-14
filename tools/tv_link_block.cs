// >>> THRUST VECTOR LINK v1.0 (shared block: identical in every thrust-driving drone script, synced by tools/thrust_sync.py, see docs/thrust-vectoring.md) >>>
// The flight side of THRUST VECTORING. The work (loss detection, choosing the thruster group to line up) is done by the small [Thrust] PB (scripts/ThrustVectorAI.cs);
// this block only tells it what the ship wants and takes its answer: while a thrust group is lost the PB sends a replacement attitude and Aim flies it for 0.5 s at a time.
// Intact ships (or no [Thrust] PB) never get an answer, so nothing changes.
// Script hooks: TvAim(ref desiredFwd, ref desiredUp, ref ffWorld); as the first line of Aim(); _tvN = forceWorld; in ApplyThrust(); _tvD = distance to the target (m); in the aiming section
// (target held). Uses _now, _aimRef, _powerScale (Peeper: add "double _powerScale = 1;"), IGC.
IMyBroadcastListener _tvL; Vector3D _tvN, _tvF, _tvU; double _tvS = -9, _tvT = -9, _tvD = -1; int _tvX;
void TvAim(ref Vector3D f, ref Vector3D u, ref Vector3D ff)
{
    if (_tvL == null) _tvL = IGC.RegisterBroadcastListener("TVEC.O" + IGC.Me);
    while (_tvL.HasPendingMessage) { var m = _tvL.AcceptMessage(); if (m.Data is MyTuple<Vector3D, Vector3D, int>) { var d = (MyTuple<Vector3D, Vector3D, int>)m.Data; _tvF = d.Item1; _tvU = d.Item2; _tvX = d.Item3; _tvT = _now; } }
    if (_now - _tvS >= ((_tvX & 63) != 0 ? 0.05 : 1))
    {
        _tvS = _now; var r = _aimRef.WorldMatrix;
        IGC.SendBroadcastMessage("TVEC.I", new MyTuple<Vector3D, Vector3D, Vector3D, Vector3D, Vector3D, int>(_tvN, f, u, r.Forward, r.Up, (int)Math.Min(100, _powerScale * 100) | (int)Math.Min(4194303, _tvD < 0 ? 4194303 : _tvD / 10) << 7), TransmissionDistance.CurrentConstruct);
    }
    _tvD = -1;
    if (_now - _tvT < 0.5 && _tvF.LengthSquared() > 0.5) { f = _tvF; u = _tvU; ff = Vector3D.Zero; }
}
// <<< THRUST VECTOR LINK <<<
