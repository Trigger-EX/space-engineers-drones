// >>> MANUAL v1.0 (shared block of the hand-flyable warships: seat detection, MANUAL mode, hand-over, assist levels, dead-man heartbeat, Run menu; docs/design/control_arch.md s2, tools/blocks_sync.py) >>>
// MANUAL is a mode of the Helm script, not another script. While somebody sits in a flight seat (an IMyShipController that CanControlShip and is not the Remote Control)
// the script gives the ship to him: every gyro and thruster override is released the tick the seat is taken, the dampener switch goes back to his state, the AI blocks
// go off, target tracking, sensors and turrets keep running, and ManFly() replaces Control(). The seat must stay taken for "Seat Debounce" before MANUAL is committed
// (until then the ship is only held free), and stay empty for "Release Delay" before the script flies again from its saved auto mode (_mode, in Storage).
// Verbs: manual | auto | pilot (toggle) | assist hud|fbw|off | fire auto|pilot. "manual" / "auto" override the seat until the seat changes. Run without an argument
// (cockpit toolbar fallback when Run-with-argument does not save): each press steps through "Run Menu", the highlighted item runs 2 s after the last press.
// Assist: HUD = lead pip and fire window text on the cockpit surface; FBW = fly-by-wire: the pilot's stick rate (Rotation Input Scale, Pilot Rate Gain, Pilot Rot Sign
// are ASSUMED until the in-game tests K1/K2) plus the PD rate to the lead point, only while a lock exists, faded out by the stick (Pilot Priority Rate).
// Dead-man: a Timer named like "Deadman" is re-armed (Stop + Start) once a second while the script flies; in MANUAL it is stopped.
// Script hooks: ManCfg() at the end of LoadConfig; ManBlocks() at the end of GetBlocks; ManSeat() early in Tick, then "if (Man) ManFly(dt); else if (!AiStep()) Control(dt);";
// ManTick() each Update1 tick and ManMenu() for a Run without argument; "if (ManCmd(w)) return;" at the top of Command; ManMode()/ManHud() in Display; ManSave() / ManLoad(..)
// for Storage; ManCap() as the turn-rate cap of Aim.
// Uses from the script: _rc, _gyros, _thr, _tgt, _aimRef, _now, _tick, _mode, _sb, _ai, _aiOn, _aiKick, _patrolLeg, _havePrev, _prevErr, _firing, Lead(), AimW(), GyroRate(), SetFire(), Release*(), Command(), PrimGuns().
List<IMyShipController> _seats = new List<IMyShipController>(); IMyShipController _seat;
List<IMyTimerBlock> _dm = new List<IMyTimerBlock>(); IMyTextSurface _hud;
bool _man, _hold, _occ, _rawOcc, _asOv, _userDamp = true, _dampInit, _menuOn; int _force, _asLvl = 1, _menuI; // _force: 0 follow the seat, 1 manual, 2 auto
double _rawT, _rampT = -99, _w0, _asG = 1, _menuT, _pipR, _pipU;
double ManDebounce = 0.4, ManRelease = 1.5, ManAssistMax = 1.5, ManGain = 1.5, ManSign = -1, ManScale = 1, ManPrio = 0.3, ManMenuWait = 2;
bool ManAutoFire, ManScan; string ManMenuItems = "manual,auto,assist hud,assist fbw,assist off,lock,next,unlock,hunt,guard,rtb,idle", ManDmName = "Deadman"; int ManHudSurf = 0;
bool Man { get { return _man || _hold; } }

void ManCfg()
{
    const string M = "Manual";
    ManDebounce = Cfg(M, "Seat Debounce", ManDebounce); ManRelease = Cfg(M, "Release Delay", ManRelease);
    string a = Cfg(M, "Assist (OFF/HUD/FBW)", "HUD").Trim().ToUpperInvariant();
    _asLvl = a == "FBW" ? 2 : a == "OFF" ? 0 : 1;
    ManAssistMax = Cfg(M, "Assist Max Rate", ManAssistMax); ManGain = Cfg(M, "Pilot Rate Gain", ManGain); ManSign = Cfg(M, "Pilot Rot Sign", ManSign);
    ManScale = Math.Max(1e-3, Cfg(M, "Rotation Input Scale", ManScale)); ManPrio = Math.Max(1e-3, Cfg(M, "Pilot Priority Rate", ManPrio));
    ManAutoFire = Cfg(M, "Auto Fire In Manual", ManAutoFire); ManScan = Cfg(M, "Scan In Manual", ManScan);
    ManMenuItems = Cfg(M, "Run Menu", ManMenuItems); ManMenuWait = Cfg(M, "Run Menu Wait", ManMenuWait);
    ManDmName = Cfg(M, "Deadman Timer Name", ManDmName); ManHudSurf = (int)Cfg(M, "HUD Surface", (double)ManHudSurf);
}

void ManBlocks()
{
    _seats.Clear(); _dm.Clear(); _hud = null;
    GridTerminalSystem.GetBlocksOfType(_seats, b => b.IsSameConstructAs(Me) && b.CanControlShip && !(b is IMyRemoteControl));
    string n = ManDmName.ToLowerInvariant();
    if (n != "") GridTerminalSystem.GetBlocksOfType(_dm, b => b.IsSameConstructAs(Me) && b.CustomName.ToLowerInvariant().Contains(n));
    try
    {
        var sp = _seats.Count > 0 ? _seats[0] as IMyTextSurfaceProvider : null;
        if (sp != null && ManHudSurf >= 0 && ManHudSurf < sp.SurfaceCount) { _hud = sp.GetSurface(ManHudSurf); _hud.ContentType = ContentType.TEXT_AND_IMAGE; _hud.FontSize = 0.8f; _hud.Font = "Monospace"; }
    }
    catch (Exception) { _hud = null; }
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
    _patrolLeg = false;
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
    if (_menuOn && _now - _menuT > ManMenuWait) { _menuOn = false; var it = ManMenuItems.Split(','); if (it.Length > 0) Command(it[_menuI % it.Length]); }
    if (_tick % 60 == 11) ManDm(!Man);   // dead-man heartbeat: re-armed once a second while the script flies, stopped while a pilot does
}
void ManMenu() { _menuI = _menuOn ? _menuI + 1 : 0; _menuOn = true; _menuT = _now; }

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
void ManLoad(string asl, string dmp) { int a; if (int.TryParse(asl, out a)) _asLvl = Math.Max(0, Math.Min(2, a)); if (dmp == "0" || dmp == "1") { _dampInit = true; _userDamp = dmp == "1"; } }

// MANUAL flight step (replaces Control): lead data for the HUD, optional auto fire, optional fly-by-wire
void ManFly(double dt)
{
    if (_rc == null) return;
    if (_hold) return;
    _userDamp = _rc.DampenersOverride;   // the pilot flips it with the V key: remembered for the next hand-over
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
        double aimR = AimActive(_tgt) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6;
        _fireWinDeg = Math.Max(0.3, Math.Atan(aimR / Math.Max(dist, 1)) * 57.2958) * FireTol;
        shoot = dist < MaxFireRange && _aimErrDeg < _fireWinDeg && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax;
        if (shoot && FriendCheck) { MatesInLine(gp, rm.Forward, dist); if (_tick % 20 == 0) CheckLineOfFire(gp, rm.Forward, dist); if (_now < _holdFireUntil) shoot = false; }
    }
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

string ManMode() { return Man ? "MANUAL" + (_asLvl > 0 ? " ASSIST-" + (_asLvl == 2 ? "FBW" : "HUD") : "") : (_occ ? "AUTO (pilot aboard) " : "AUTO ") + _mode; }

// compact cockpit page (<= 8 lines)
string ManHud()
{
    var b = new StringBuilder();
    b.Append("MODE ").Append(ManMode()).Append(_hold ? " (seat taken)" : "").Append('\n');
    if (_tgt != null)
    {
        b.Append("TGT  ").Append(Vector3D.Distance(_tgt.P(_now), _rc.GetPosition()).ToString("0")).Append(" m  v ").Append(_tgt.V(_now).Length().ToString("0")).Append("  seen ").Append((_now - _tgt.Seen).ToString("0.0")).Append("s  T ").Append(_leadT.ToString("0.00")).Append('\n');
        b.Append("PIP  R").Append(_pipR.ToString("+0.0;-0.0")).Append(" U").Append(_pipU.ToString("+0.0;-0.0")).Append("  err ").Append(_aimErrDeg.ToString("0.0")).Append(" win ").Append(_fireWinDeg.ToString("0.0")).Append(_fireWinDeg > 0 && _aimErrDeg < _fireWinDeg ? " FIRE" : "").Append('\n');
    }
    else b.Append("TGT  none (").Append(_contacts.Count).Append(")\n");
    b.Append("PWR ").Append((_batFrac * 100).ToString("0")).Append("% AMMO ").Append((_ammoFrac * 100).ToString("0")).Append("% THR ").Append((_powerScale * 100).ToString("0")).Append("% DAMP ").Append(_rc.DampenersOverride ? "on" : "off").Append("  DEADMAN ").Append(_dm.Count == 0 ? "-" : Man ? "off" : "armed").Append('\n');
    if (_menuOn) { var it = ManMenuItems.Split(','); b.Append("MENU > ").Append(it[_menuI % it.Length].Trim()).Append(' ').Append(_menuI % it.Length + 1).Append('/').Append(it.Length).Append('\n'); }
    if (_err != "") b.Append("! ").Append(_err).Append('\n');
    return b.ToString();
}
void ManShow()
{
    if (_hud == null) return;
    try { _hud.WriteText(ManHud()); } catch (Exception) { _hud = null; }
}
// <<< MANUAL v1.0 <<<
