// >>> STAFF SEARCH v1.0 (patrol, sensor setup, target feeds, seeker moved from the Flight PB, docs/design/survivability.md) >>>
// The four search modules that left the Flight PB (off the hot path, docs/design/survivability.md s1.1). Keys keep their names in the Flight PB's [<FS>] section;
// the Staff reads them there and writes missing defaults (SvDef, after the first F2S). The Flight's Target Small/Large Grids, Target Neutral/Unowned and Target
// Characters keys are read for the sensors.
//  * SEARCH PATROL (SsPatrol): HUNT with no target for "Search Patrol Starts After (s)" (after a fight: "... After A Fight (s)"): chords of a square of
//    "Search Patrol Radius (m)" round home, with "Listening Stop (s)" halts for the AI blocks; a moving AI (it has found something) holds the stop up to 90 s.
//    .PAT (Staff -> Flight) MyTuple<Vector3D point, double speed, int flags>: bit0 leg active (fly to point), bit1 listening stop (hold point); every 30 ticks
//    and on a change (the Flight drops it after 1.5 s). Mates fan out: start corner by rank among the hive mates, direction and radius from the grid id.
//  * SENSOR SETUP: every sensor of the construct is set up in GetBlocks (50 m all round, enemies, neutral / small / large by the Flight's keys, owner on foot
//    when SsFoot (FOLLOW), players when SsFoot or Target Characters).
//  * TARGET FEEDS (SsFeed): other fleets' "<TAG>_TGT" ("Listen To IGC Tags (target feeds)", own Tag skipped) join the coalesced .TGT relay to the Flight.
//  * SEEKER: a turret named [Seeker] on this grid is set to "Seeker Group" (PowerSystems / Weapons / Propulsion). Twice a second, when it holds the Flight's
//    target and is aimed, the point it shows (the game's block position, else its line with the lead taken off) is raycast by a camera holding twice the ray
//    length (the camera charge is shared with the Flight PB). The hull point hit (target frame) becomes an own-seeker entry of the WEAKPOINT RELAY: ranked
//    first, sent to the Flight on .WPT with flag 4096, shared with the wing on <ch>_WP. "seeker on" / "seeker off" (Staff word) switch it; Seek: line on screen.
// Per-script: readonly string FS; readonly bool SsPatrol, SsFeed, SsFoot; bool SsHunt() (the mode is HUNT).
// Hooks: SsBlocks() in GetBlocks after LoadFlightCfg(); SsCfg(ini) after SvCfg(ini) in LoadFlightCfg; SsFeeds() first in Relay(); SsStep() (called by SvWatch).
// Uses: _rc, _fk, _now, _tick, _mode, _fl, _tgtId, _sensors, _mates, _rel, _flChan, Tag, Me, FleetOk, SvT/SvBn/SvDn (key reader), SvHomeKnown(), _svHomeP, WpSeek().
bool PatrolOn = true, PatrolRandom = true, SeekerOn = true, _ssTS = true, _ssTL = true, _ssTN, _ssTC;
double PatrolRadius = 1200, PatrolAfter = 15, PatrolAfterFight = 90, PatrolListen = 6, PatrolMaxLeg = 40, PatrolSpeed = 60, PatrolHold = 90;
bool _patrolLeg, _patrolFrame, _patrolStopSet; double _patrolPhaseT = -99, _noTgtSince, _listenT = -1, _aiMoveT = -99, _ssLastT = -1, _patrolRadF = 1;
int _patrolK, _patrolDir = 1, _ssMd = -1, _ssPatF = -1, _ssMiss; long _ssPatTk = -99;
Vector3D _listenV, _patrolWp, _patrolC, _patrolE1, _patrolE2, _patrolStop, _ssPatSent;
string ListenTags = "HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER", _ssFeedKey = "", SeekerGroup = "PowerSystems", _skLog = "";
List<IMyBroadcastListener> _ssFeed = new List<IMyBroadcastListener>();
List<IMyLargeTurretBase> _skT = new List<IMyLargeTurretBase>(); List<IMyCameraBlock> _ssCams = new List<IMyCameraBlock>();
double _skT0 = -99;

void SsCfg(MyIni i)
{
    _ssTS = i.Get(FS, "Target Small Grids").ToBoolean(true); _ssTL = i.Get(FS, "Target Large Grids").ToBoolean(true);
    _ssTN = i.Get(FS, "Target Neutral/Unowned").ToBoolean(false); _ssTC = i.Get(FS, "Target Characters").ToBoolean(false);
    if (SsPatrol)
    {
        PatrolOn = SvBn(i, FS, "Search Patrol When Nothing Found (HUNT)", true);
        PatrolRandom = SvBn(i, FS, "Search Patrol Own Start Corner And Direction", true);
        PatrolRadius = SvDn(i, FS, "Search Patrol Radius (m)", 1200);
        PatrolAfter = SvDn(i, FS, "Search Patrol Starts After (s)", 15);
        PatrolAfterFight = SvDn(i, FS, "Search Patrol Starts After A Fight (s)", 90);
        PatrolListen = SvDn(i, FS, "Search Patrol Listening Stop (s)", 6);
        PatrolSpeed = SvDn(i, FS, "Search Patrol Speed", 60);
    }
    if (SsFeed) ListenTags = SvT(i, FS, "Listen To IGC Tags (target feeds)", "HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER");
    SeekerGroup = SvT(i, FS, "Seeker Group (PowerSystems/Weapons/Propulsion)", "PowerSystems").Trim();
    SeekerOn = SvBn(i, FS, "Seeker Turret Guides Aim", true);
}

void SsBlocks()
{
    _ssMiss = 0;
    foreach (var s in _sensors)
    {
        s.Enabled = true;
        SsF(s, "LeftExtend", "Left"); SsF(s, "RightExtend", "Right"); SsF(s, "TopExtend", "Top"); SsF(s, "BottomExtend", "Bottom");
        SsF(s, "FrontExtend", "Front"); SsF(s, "BackExtend", "Back");
        SsB(s, true, "Detect Enemy", "DetectEnemy"); SsB(s, _ssTN, "Detect Neutral", "DetectNeutral");
        SsB(s, SsFoot, "Detect Owner", "DetectOwner"); SsB(s, false, "Detect Friendly", "DetectFriendly");
        SsB(s, _ssTS, "Detect Small Ships", "DetectSmallShips"); SsB(s, _ssTL, "Detect Large Ships", "DetectLargeShips");
        SsB(s, _ssTL, "Detect Stations", "DetectStations"); SsB(s, SsFoot || _ssTC, "Detect Players", "DetectPlayers");
        SsB(s, false, "Detect Asteroids", "DetectAsteroids"); SsB(s, false, "Detect Floating Objects", "DetectFloatingObjects");
        SsB(s, false, "Detect Subgrids", "DetectSubgrids");
    }
    var g = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType(_ssCams, b => b.CubeGrid == g);
    foreach (var c in _ssCams) c.EnableRaycast = true;
    _skT.Clear();
    if (SeekerOn) GridTerminalSystem.GetBlocksOfType(_skT, b => b.CubeGrid == g && b.CustomName.Contains("[Seeker]"));
    if (_skT.Count == 0) _skLog = ""; else if (_skLog == "") _skLog = "ready, group " + SeekerGroup;   // (re-scans keep the last report)
    foreach (var tu in _skT)
    {
        try { if (SeekerGroup.Length > 0 && tu.GetTargetingGroup() != SeekerGroup) tu.SetTargetingGroup(SeekerGroup); }
        catch (Exception x) { _skLog = "group not set: " + x.Message; }
    }
    string k = SsFeed ? ListenTags + "|" + Tag : "";
    if (k == _ssFeedKey) return;
    _ssFeedKey = k;
    foreach (var l in _ssFeed) IGC.DisableBroadcastListener(l);
    _ssFeed.Clear();
    if (k != "") foreach (var t in ListenTags.Split(','))
        { string tt = t.Trim().ToUpperInvariant(); if (tt != "" && tt != Tag.ToUpperInvariant()) _ssFeed.Add(IGC.RegisterBroadcastListener(tt + "_TGT")); }
}
void SsF(IMyTerminalBlock b, string n1, string n2)
{
    if (b.GetProperty(n1) != null) b.SetValueFloat(n1, 50f); else if (b.GetProperty(n2) != null) b.SetValueFloat(n2, 50f); else _ssMiss++;
}
void SsB(IMyTerminalBlock b, bool v, string n1, string n2)
{
    if (b.GetProperty(n1) != null) b.SetValueBool(n1, v); else if (b.GetProperty(n2) != null) b.SetValueBool(n2, v); else _ssMiss++;
}

// other fleets' target feeds -> the .TGT relay (coalesced per target id there)
void SsFeeds()
{
    foreach (var l in _ssFeed)
        while (l.HasPendingMessage)
        {
            var m = l.AcceptMessage();
            if (!(m.Data is MyTuple<long, Vector3D, Vector3D, double, long>)) continue;
            var d = (MyTuple<long, Vector3D, Vector3D, double, long>)m.Data;
            if (FleetOk(d)) _rel[d.Item1] = d;
        }
}

void SsStep()
{
    if (_tick % 5 == 2) SsPat();
    SsSeek();
}

// SEARCH PATROL (was the Flight's PatrolStep; the Flight's AI-move detection is done here from the RC's motion while the AI blocks are on)
void SsPat()
{
    if ((int)_mode != _ssMd) { _ssMd = (int)_mode; _noTgtSince = _now; _patrolLeg = false; _patrolStopSet = false; }
    if (_tgtId != 0) { _ssLastT = _now; _noTgtSince = _now; _patrolStopSet = false; if (_patrolLeg) { _patrolLeg = false; _patrolPhaseT = _now; } }
    if ((_fl & 64) != 0 && PatrolOn)
    {
        var vs = _rc.GetShipVelocities();
        Vector3D refV = _patrolK == 0 ? Vector3D.Zero : _listenV;   // before the first leg the reference is rest
        if ((vs.LinearVelocity - refV).LengthSquared() > 9 || vs.AngularVelocity.LengthSquared() > 0.0144) _aiMoveT = _now;
    }
    if (!SsPatrol || !PatrolOn || !SsHunt() || (_fl & 16) != 0 || _now - _noTgtSince < (_ssLastT < 0 ? PatrolAfter : PatrolAfterFight) || _rc.GetNaturalGravity().LengthSquared() > 0.25)
        _patrolLeg = false;
    else if (!_patrolLeg)
    {
        if (_listenT < 0 || _listenT < _patrolPhaseT) { _listenT = _now; _listenV = _rc.GetShipVelocities().LinearVelocity; }   // a listening stop begins
        if (_now - _patrolPhaseT >= PatrolListen && !(_now - _listenT < PatrolHold && _now - _aiMoveT < 4))
        {
            if (!_patrolFrame)
            {
                _patrolC = SvHomeKnown() ? _svHomeP : _rc.GetPosition(); _patrolE1 = _rc.WorldMatrix.Forward; _patrolE2 = _rc.WorldMatrix.Right; _patrolFrame = true;
                int rank = 0; foreach (var m in _mates.Values) if (m.Id < Me.CubeGrid.EntityId) rank++;
                double a0 = 6.2831853 * rank / (_mates.Count + 1) + (Math.Abs(Me.CubeGrid.EntityId) % 628) * 0.001;
                Vector3D e1 = _patrolE1 * Math.Cos(a0) + _patrolE2 * Math.Sin(a0);
                _patrolE2 = _patrolE2 * Math.Cos(a0) - _patrolE1 * Math.Sin(a0); _patrolE1 = e1;
                int ph = PatrolRandom ? (int)(Math.Abs(Me.CubeGrid.EntityId) % 997) : 0;
                _patrolDir = ((ph / 8) & 1) == 0 ? 1 : -1; _patrolRadF = PatrolRandom ? 0.8 + 0.1 * ((ph / 16) % 5) : 1;
            }
            double th = _patrolK++ * 1.5707963 * _patrolDir;
            _patrolWp = _patrolC + (_patrolE1 * Math.Cos(th) + _patrolE2 * Math.Sin(th)) * (PatrolRadius * _patrolRadF);
            _patrolLeg = true; _patrolPhaseT = _now; _listenT = -1;
        }
    }
    else if (_now - _patrolPhaseT > PatrolMaxLeg || Vector3D.Distance(_rc.GetPosition(), _patrolWp) < 150)
    { _patrolLeg = false; _patrolPhaseT = _now; _patrolStop = _rc.GetPosition(); _patrolStopSet = true; }
    if (_fk == "") return;
    int f = _patrolLeg ? 1 : _patrolStopSet ? 2 : 0;
    Vector3D p = _patrolLeg ? _patrolWp : _patrolStop;
    if (f == _ssPatF && (f == 0 || (p - _ssPatSent).LengthSquared() < 1) && _tick - _ssPatTk < 30) return;
    _ssPatF = f; _ssPatSent = p; _ssPatTk = _tick;
    IGC.SendBroadcastMessage(_fk + ".PAT", new MyTuple<Vector3D, double, int>(p, PatrolSpeed, f), TransmissionDistance.CurrentConstruct);
}

// SEEKER: the turret's view of the Flight's target -> a hull point in the target frame -> the WEAKPOINT RELAY (own-seeker entry)
void SsSeek()
{
    if (!SeekerOn || _skT.Count == 0 || _tgtId == 0 || _now - _skT0 < 0.5) return;
    _skT0 = _now;
    foreach (var tu in _skT)
    {
        if (!tu.IsFunctional) { _skLog = "turret damaged"; continue; }
        if (!tu.HasTarget) { _skLog = "turret has no target (group " + tu.GetTargetingGroup() + ")"; continue; }
        var te = tu.GetTargetedEntity();
        if (te.EntityId != _tgtId) { _skLog = "turret on another target"; continue; }
        if (!tu.IsAimed) { _skLog = "turret turning"; continue; }
        Vector3D o = tu.GetPosition(), q;
        bool hp = te.HitPosition.HasValue && (te.HitPosition.Value - te.Position).LengthSquared() > 1;
        if (hp) q = te.HitPosition.Value;                  // the game told us the block itself
        else
        {
            Vector3D d; Vector3D.CreateFromAzimuthAndElevation(tu.Azimuth, tu.Elevation, out d);
            d = Vector3D.TransformNormal(d, tu.WorldMatrix);
            double D = Vector3D.Dot(te.Position - o, d);
            if (D < 5) { _skLog = "turret points away"; continue; }
            // turrets lead their target; take the lead off again (bullets inherit our velocity)
            Vector3D rv = new Vector3D(te.Velocity) - _rc.GetShipVelocities().LinearVelocity;
            string st = tu.BlockDefinition.SubtypeName;
            q = o + d * D - rv * (D / (st.Contains("Calibre") ? 500 : tu is IMyLargeMissileTurret ? 200 : 800));
        }
        IMyCameraBlock cam = null; double best = 0;
        foreach (var c in _ssCams)
        {
            if (!c.IsWorking) continue;
            Vector3D cd0 = q - c.GetPosition(); double l0 = cd0.Length();
            if (l0 < 1 || Vector3D.Dot(cd0 / l0, c.WorldMatrix.Forward) < 0.72 || c.AvailableScanRange < 2 * (l0 + 30)) continue;   // the Flight PB shares the charge: twice the ray
            if (c.AvailableScanRange > best) { best = c.AvailableScanRange; cam = c; }
        }
        if (cam == null) { _skLog = "no camera charge/angle"; continue; }
        Vector3D cd = q - cam.GetPosition(); double cl = cd.Length(); cd /= cl;
        var e = cam.Raycast(q + cd * 30);
        if (e.IsEmpty() || e.EntityId != _tgtId || !e.HitPosition.HasValue) { _skLog = (hp ? "block pos given, " : "aim ray, ") + "ray missed hull"; continue; }
        MatrixD inv = MatrixD.Transpose(e.Orientation);
        double hint = hp ? (q - e.HitPosition.Value).Length() : -1;
        int k = SeekerGroup == "PowerSystems" ? 1 : SeekerGroup == "Weapons" ? 2 : SeekerGroup == "Propulsion" ? 3 : 4;
        WpSeek(new MyTuple<long, Vector3D, Vector3D, Vector3D, long, int>(_tgtId, Vector3D.TransformNormal(e.HitPosition.Value - e.Position, inv),
            Vector3D.TransformNormal(cd, inv), new Vector3D(0, hint, 0), Me.CubeGrid.EntityId, k | 8 | (hp ? 16 : 0)));
        _skLog = (hp ? "block pos given" : "aim ray") + ", " + (q - e.HitPosition.Value).Length().ToString("0") + " m under hull";
        return;
    }
}

string SsStatus()
{
    string s = _skT.Count > 0 ? "Seek   : " + _skLog + "\n" : "";
    if (_patrolLeg) s += "Patrol : leg " + _patrolK + ", " + Vector3D.Distance(_rc.GetPosition(), _patrolWp).ToString("0") + " m to go\n";
    else if (_patrolStopSet && SsHunt()) s += "Patrol : listening stop\n";
    if (_ssMiss > 0) s += "! sensor setup: " + _ssMiss + " properties not found\n";
    return s;
}
// <<< STAFF SEARCH v1.0 <<<
