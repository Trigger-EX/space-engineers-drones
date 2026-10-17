// >>> STAFF SEARCH v1.1 (patrol, sensor setup, target feeds, seeker keys; moved from the Flight PB, docs/design/survivability.md) >>>
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
//  * SEEKER KEYS: "Seeker Group" and "Seeker Turret Guides Aim" (SeekerGroup, SeekerOn; "seeker on" / "seeker off" Staff word). v1.1: the turret reader is the
//    SEEKER block (tools/sk_block.cs: local in Gnat / Basilisk, in the [Aux] PB for the other 7, reached through AUX LINK's SkBlocks/SkStep/SkStatus proxies).
// Per-script: readonly string FS; readonly bool SsPatrol, SsFeed, SsFoot; bool SsHunt() (the mode is HUNT).
// Hooks: SsBlocks() in GetBlocks after LoadFlightCfg(); SsCfg(ini) after SvCfg(ini) in LoadFlightCfg; SsFeeds() first in Relay(); SsStep() (called by SvWatch).
// Uses: _rc, _fk, _now, _tick, _mode, _fl, _tgtId, _sensors, _mates, _rel, _flChan, Tag, Me, FleetOk, SvT/SvBn/SvDn (key reader), SvHomeKnown(), _svHomeP,
//   SEEKER or AUX LINK (SkBlocks, SkStep, SkStatus).
bool PatrolOn = true, PatrolRandom = true, SeekerOn = true, _ssTS = true, _ssTL = true, _ssTN, _ssTC;
double PatrolRadius = 1200, PatrolAfter = 15, PatrolAfterFight = 90, PatrolListen = 6, PatrolMaxLeg = 40, PatrolSpeed = 60, PatrolHold = 90;
bool _patrolLeg, _patrolFrame, _patrolStopSet; double _patrolPhaseT = -99, _noTgtSince, _listenT = -1, _aiMoveT = -99, _ssLastT = -1, _patrolRadF = 1;
int _patrolK, _patrolDir = 1, _ssMd = -1, _ssPatF = -1, _ssMiss; long _ssPatTk = -99;
Vector3D _listenV, _patrolWp, _patrolC, _patrolE1, _patrolE2, _patrolStop, _ssPatSent;
string ListenTags = "HORNET,LONGBOW,WASP,AEGIS,HARRIER,LANCER,MANTIS,BASILISK,WATCHER", _ssFeedKey = "", SeekerGroup = "PowerSystems";
List<IMyBroadcastListener> _ssFeed = new List<IMyBroadcastListener>();

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
    SkBlocks();
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
    SkStep();
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

string SsStatus()
{
    string s = SkStatus();
    if (_patrolLeg) s += "Patrol : leg " + _patrolK + ", " + Vector3D.Distance(_rc.GetPosition(), _patrolWp).ToString("0") + " m to go\n";
    else if (_patrolStopSet && SsHunt()) s += "Patrol : listening stop\n";
    if (_ssMiss > 0) s += "! sensor setup: " + _ssMiss + " properties not found\n";
    return s;
}
// <<< STAFF SEARCH v1.1 <<<
