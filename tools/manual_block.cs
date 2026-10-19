// >>> MANUAL v1.2 (shared block of the hand-flyable warships: seat detection, MANUAL mode, hand-over, assist levels, dead-man heartbeat; docs/design/control_arch.md s2, tools/blocks_sync.py) >>>
// MANUAL is a mode of the Helm script, not another script. While somebody sits in a flight seat (an IMyShipController that CanControlShip and is not the Remote Control)
// the script gives the ship to him: every gyro and thruster override is released the tick the seat is taken, the dampener switch goes back to his state, the AI blocks
// go off, target tracking, sensors and turrets keep running, and ManFly() replaces Control(). The seat must stay taken for "Seat Debounce" before MANUAL is committed
// (until then the ship is only held free), and stay empty for "Release Delay" before the script flies again from its saved auto mode (_mode, in Storage).
// Verbs: manual | auto | pilot (toggle) | assist hud|fbw|off | fire auto|pilot. "manual" / "auto" override the seat until the seat changes.
// v1.2 (docs/design/flight_split.md H3, helm_split.md Cut 2): the cockpit HUD page and the Run menu live in the Staff PB (StaffAI.cs). The Helm sends the HUD data
// on "<Ship Tag>.HUD" and a Run without argument as ".MNU" (HELMLINK 1.3); the Staff writes the page, steps "Run Menu" and sends the chosen verb back on .S2H.
// Without a live Staff a Run without argument toggles "pilot". [Manual] Run Menu / Run Menu Wait / HUD Surface stay in this PB's Custom Data; the Staff reads them.
// Assist: HUD = lead pip and fire window text on the cockpit surface (written by the Staff); FBW = fly-by-wire: the pilot's stick rate (Rotation Input Scale, Pilot Rate Gain, Pilot Rot Sign
// are ASSUMED until the in-game tests K1/K2) plus the PD rate to the lead point, only while a lock exists, faded out by the stick (Pilot Priority Rate).
// Dead-man: a Timer named like "Deadman" is re-armed (Stop + Start) once a second while the script flies; in MANUAL it is stopped.
// Script hooks: ManIn() for the [Manual] line of CONFIG LINK (CfgIn; the Staff's CONFIG SERVER writes and reads the section, H1); ManBlocks() at the end of GetBlocks; ManSeat() early in Tick, then
// "if (Man) ManFly(dt); else if (!AiStep()) Control(dt);"; ManTick() each Update1 tick; "if (ManCmd(w)) return;" at the top of Command; ManMode() in Display; ManSave() / ManLoad(..)
// for Storage; ManCap() as the turn-rate cap of Aim. _pipR / _pipU / _aimErrDeg / _fireWinDeg feed the HUD (HELMLINK HlkHud).
// Uses from the script: _rc, _gyros, _thr, _tgt, _aimRef, _now, _tick, _mode, _ai, _aiOn, _aiKick, _havePrev, _prevErr, _firing, Lead(), AimW(), GyroRate(), SetFire(), Release*(), Command(), PrimGuns().
List<IMyShipController> _seats = new List<IMyShipController>(); IMyShipController _seat;
List<IMyTimerBlock> _dm = new List<IMyTimerBlock>();
bool _man, _hold, _occ, _rawOcc, _asOv, _userDamp = true, _dampInit, _asKeep; int _force, _asLvl = 1; // _force: 0 follow the seat, 1 manual, 2 auto
double _rawT, _rampT = -99, _w0, _asG = 1, _pipR, _pipU;
double ManDebounce = 0.4, ManRelease = 1.5, ManAssistMax = 1.5, ManGain = 1.5, ManSign = -1, ManScale = 1, ManPrio = 0.3;
bool ManAutoFire, ManScan; string ManDmName = "Deadman", ManSeatEx = "gunnery";
bool Man { get { return _man || _hold; } }

void ManIn()
{   // [Manual] values in cfg_tables.json order; Run Menu, Run Menu Wait and HUD Surface are the Staff's (HUD page and menu). The assist level saved in Storage wins until "reload".
    ManDebounce = N(); ManRelease = N(); string a = T().Trim().ToUpperInvariant();
    if (!_asKeep) _asLvl = a == "FBW" ? 2 : a == "OFF" ? 0 : 1;
    ManAssistMax = N(); ManGain = N(); ManSign = N(); ManScale = Math.Max(1e-3, N()); ManPrio = Math.Max(1e-3, N()); ManAutoFire = B(); ManScan = B(); T(); N(); ManDmName = T(); ManSeatEx = T().ToLowerInvariant();
}

void ManBlocks()
{
    _seats.Clear(); _dm.Clear();
    GridTerminalSystem.GetBlocksOfType(_seats, b => Own(b) && b.CanControlShip && !(b is IMyRemoteControl) && (ManSeatEx == "" || !b.CustomName.ToLowerInvariant().Contains(ManSeatEx)));   // (a gunnery seat does not take the ship from the Helm)
    string n = ManDmName.ToLowerInvariant();
    if (n != "") GridTerminalSystem.GetBlocksOfType(_dm, b => Own(b) && b.CustomName.ToLowerInvariant().Contains(n));
    if (!_dampInit && _rc != null) { _userDamp = _rc.DampenersOverride; _dampInit = true; }   // the pilot's own dampener switch, until MANUAL tracks it
}

bool Seated() { _seat = null; foreach (var c in _seats) if (c.IsFunctional && c.IsUnderControl) { _seat = c; break; } return _seat != null; }

// seat -> mode machine; runs first in Tick so the overrides are gone in the same tick the seat is taken
void ManSeat()
{
    bool raw = Seated();
    if (raw != _rawOcc) { _rawOcc = raw; _rawT = _now; }
    if (raw != _occ && _now - _rawT >= (raw ? ManDebounce : ManRelease)) { _occ = raw; _force = 0; }
    bool want = _force == 1 || (_force == 0 && _occ), hold = raw && !_occ && _force == 0 && !want;
    if ((want && !_man && !_hold) || (hold && !_hold)) { ManFree(); ManDm(false); }
    else if (want && _hold) ManDamp(_userDamp);   // the seat is committed: an AI block that was switched off may have touched the dampeners since
    if (_man && !want) ManExit();
    bool ch = _man != want || _hold != hold;
    _man = want; _hold = hold;
    if (ch) Display();   // the mode line changes at once
}

// hand the ship to the pilot: nothing of mine stays switched on. Guns the pilot set are left alone (only my own Shoot_On is cancelled).
void ManFree()
{
    ReleaseGyros();
    foreach (var t in _thr) t.ThrustOverridePercentage = 0;
    _prevErr = Vector3D.Zero; _havePrev = false; _asOv = false; _asG = 1;
    BatFire(false, Vector3D.Zero);   // a continuous second battery stops too
    if (_aiOn) { _aiOn = false; _aiKick = false; foreach (var b in _ai) b.Enabled = false; }
    ManDamp(_userDamp);   // after the AI blocks: switching those off may touch the dampeners
    if (_firing) { _firing = false; foreach (var g in _guns) if (g.IsFunctional) g.ApplyAction("Shoot_Off"); }
}

// back to the saved auto mode: the nose is not snapped, the turn-rate cap ramps from the present rate to MaxTurn over 2 s
void ManExit()
{
    ReleaseGyros(); _asOv = false; _firing = true;
    _w0 = _rc != null ? _rc.GetShipVelocities().AngularVelocity.Length() : 0; _rampT = _now; ManDm(true);
}
double ManCap() { double k = (_now - _rampT) / 2; return k >= 1 ? MaxTurn : Math.Min(MaxTurn, _w0 + (MaxTurn - _w0) * Math.Max(0, k)); }

void ManDamp(bool v) { _rc.DampenersOverride = v; foreach (var s in _seats) if (s.IsUnderControl) s.DampenersOverride = v; }
void ManDm(bool arm) { foreach (var t in _dm) { try { t.StopCountdown(); if (arm) t.StartCountdown(); } catch (Exception) { } } }

void ManTick()
{
    if (_tick % 60 == 11) ManDm(!Man);   // dead-man heartbeat: re-armed once a second while the script flies, stopped while a pilot does
}

bool ManCmd(string[] w)
{
    switch (w[0])
    {
        case "manual": _force = 1; return true;
        case "auto": _force = Seated() ? 2 : 0; return true;
        case "pilot": _force = Man ? (Seated() ? 2 : 0) : 1; return true;
        case "assist": if (w.Length < 2) return false; _asLvl = w[1] == "fbw" ? 2 : w[1] == "hud" ? 1 : 0; return true;
    }
    return false;
}

string ManSave() { return _asLvl + ";" + (_userDamp ? "1" : "0"); }
void ManLoad(string asl, string dmp) { int a; if (int.TryParse(asl, out a)) { _asLvl = Math.Max(0, Math.Min(2, a)); _asKeep = true; } if (dmp == "0" || dmp == "1") { _dampInit = true; _userDamp = dmp == "1"; } }

// MANUAL flight step (replaces Control): lead data for the HUD, optional auto fire, optional fly-by-wire
void ManFly(double dt)
{
    if (_rc == null) return;
    if (_hold) return;
    _userDamp = (_seat != null ? (IMyShipController)_seat : _rc).DampenersOverride;   // the pilot flips it (on his own seat: per-controller state is unverified, K7) with the V key: remembered for the next hand-over
    bool have = false; Vector3D pt = Vector3D.Zero, dir = Vector3D.Zero, gp = Vector3D.Zero;
    var gl = PrimGuns(); int n = 0;
    if (_tgt != null && _aimRef != null)
    {
        foreach (var g in gl) if (g.IsFunctional) { gp += g.GetPosition(); n++; }
        gp = n > 0 ? gp / n : _rc.GetPosition();
        Vector3D mv = _rc.GetShipVelocities().LinearVelocity;
        have = Lead(_tgt, gp, mv, out pt, out dir);
    }
    bool shoot = false; _aimErrDeg = 0; _fireWinDeg = 0;
    if (have)
    {
        MatrixD rm = _aimRef.WorldMatrix;
        Vector3D d = Vector3D.TransformNormal(dir, MatrixD.Transpose(rm));
        _pipR = Math.Atan2(d.X, -d.Z) * 57.2958; _pipU = Math.Atan2(d.Y, -d.Z) * 57.2958;
        double dist = Vector3D.Distance(pt, gp);
        _aimErrDeg = Math.Acos(MathHelper.Clamp(Vector3D.Dot(rm.Forward, dir), -1, 1)) * 57.2958;
        _fireWinDeg = BatWin(dist);
        shoot = dist < MaxFireRange && _aimErrDeg < _fireWinDeg && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax;
        if (shoot && FriendCheck) { MatesInLineP(gp, rm.Forward, dist); if (_tick % 20 == 0) CheckLineOfFire(gp, rm.Forward, dist); if (_now < _holdFireUntil) shoot = false; }
    }
    if (have && !ManAutoFire && _tick % 10 == 3) BatPick();   // (the pip follows the primary battery even when the script does not fire)
    if (ManAutoFire) { SetFire(shoot); BatFire(have, _rc.GetShipVelocities().LinearVelocity); }
    // fly-by-wire: stick rate + the PD rate to the lead point, the stick fades the assist; no lock: full release (native feel)
    bool ov = false;
    if (_asLvl == 2 && have && _seat != null)
    {
        var ri = _seat.RotationIndicator;
        Vector3D l = new Vector3D(MathHelper.Clamp(ri.X / ManScale, -1, 1), MathHelper.Clamp(ri.Y / ManScale, -1, 1), MathHelper.Clamp(_seat.RollIndicator / ManScale, -1, 1)) * (ManGain * ManSign);
        Vector3D wp = Vector3D.TransformNormal(l, _seat.WorldMatrix);
        double tg = MathHelper.Clamp(1 - wp.Length() / ManPrio, 0, 1);
        _asG = tg < _asG ? Math.Max(tg, _asG - dt / 0.15) : Math.Min(tg, _asG + dt / 0.5);
        Vector3D los = Vector3D.Zero;
        if (_havePrev) los = Vector3D.Cross(_prevAim, dir) / dt;
        if (los.LengthSquared() > 4) los = Vector3D.Normalize(los) * 2;
        _prevAim = dir; _havePrev = true;
        Vector3D wa = AimW(dir, Vector3D.Zero, los, dt);
        if (wa.LengthSquared() > ManAssistMax * ManAssistMax) wa = Vector3D.Normalize(wa) * ManAssistMax;
        Vector3D w = wp + wa * _asG;
        if (w.LengthSquared() > MaxTurn * MaxTurn) w = Vector3D.Normalize(w) * MaxTurn;
        GyroRate(w); ov = true;
    }
    else { _asG = 1; _havePrev = false; }
    if (!ov && _asOv) ReleaseGyros();
    _asOv = ov;
}

string ManMode() { return Man ? "MANUAL" + (_asLvl > 0 ? " ASSIST-" + (_asLvl == 2 ? "FBW" : "HUD") : "") : (_occ ? "AUTO (pilot aboard) " : "AUTO ") + (_sentry && _mode == Mode.GUARD ? "SENTRY" : _mode.ToString()); }

// <<< MANUAL v1.2 <<<
