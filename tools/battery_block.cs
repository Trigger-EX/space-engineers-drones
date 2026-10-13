// >>> BATTERY v0.1-lite (two weapon batteries with a class table, for the Shrike fighter; the full N-battery manager of docs/design/control_arch.md s3 replaces it; tools/blocks_sync.py) >>>
// Fixed guns form two batteries by the tag in the block name: [B1] and [B2]; an untagged gun joins B1 when it is ballistic (gatling, autocannon), else B2. Each battery's
// class (speed, range, shot interval) comes from its first gun's definition id (BatSpec). The PRIMARY battery is B1 (B2 when B1 is empty): it is the one Control()/Aim() orient the
// ship by, with the script's own lead solver, ProjSpeed and MaxFireRange (taken from the class table when the primary is not ballistic). The other battery fires on its
// own lead solution (BatFire) whenever ITS boresight is inside its silhouette window, in its own range band and the line of fire is clear: continuous fire (Shoot_On/Off) for
// ballistic guns, ShootOnce every "interval" seconds for rocket, railgun and shell classes. Turrets never belong to a battery (they are designators and fire themselves).
// Hooks: BatSort() at the end of GetBlocks (after _guns; sets _aimRef); PrimGuns() instead of _guns for the gun centre in Control() and for SetFire(); BatFire(haveAim, myVel)
// after the primary's fire decision in Control() (BatFire(false, ..) in IDLE) and in ManFly().
class Bat { public string Cls = "-"; public double Speed = 800, Range = 800, MinRange = 0, Cool = 0, Next; public bool On; public List<IMyUserControllableGun> G = new List<IMyUserControllableGun>(); }
Bat[] _bat = { new Bat(), new Bat() }; int _prim;
List<IMyUserControllableGun> PrimGuns() { return _bat[_prim].G; }

void BatSpec(Bat b)
{
    b.Cls = "-"; b.Speed = 800; b.Range = 800; b.MinRange = 0; b.Cool = 0;
    if (b.G.Count == 0) return;
    string s = b.G[0].BlockDefinition.ToString();   // "MyObjectBuilder_SmallGatlingGun/" (a gatling has no subtype)
    if (s.Contains("Gatling") || s.Contains("Autocannon") || s.Contains("Interior")) b.Cls = "BALL";
    else if (s.Contains("Railgun")) { b.Cls = "RAIL"; bool big = s.Contains("Large"); b.Speed = big ? 2000 : 1000; b.Range = big ? 2000 : 1400; b.Cool = big ? 2.1 : 0.6; b.MinRange = 150; }
    else if (s.Contains("Missile") || s.Contains("Rocket")) { b.Cls = "ROCK"; b.Speed = 200; b.Range = 800; b.Cool = 0.35; b.MinRange = 100; }
    else { b.Cls = "SHEL"; b.Speed = 500; b.Range = s.Contains("Large") ? 2000 : 1400; b.Cool = 1.0; b.MinRange = 150; }
}

void BatSort()
{
    foreach (var b in _bat) b.G.Clear();
    foreach (var g in _guns)
    {
        string n = g.CustomName, s = g.BlockDefinition.ToString();
        _bat[n.Contains("[B2]") ? 1 : n.Contains("[B1]") ? 0 : (s.Contains("Gatling") || s.Contains("Autocannon") ? 0 : 1)].G.Add(g);
    }
    foreach (var b in _bat) BatSpec(b);
    _prim = _bat[0].G.Count > 0 ? 0 : 1;
    if (_bat[_prim].G.Count > 0 && _bat[_prim].Cls != "BALL") { ProjSpeed = _bat[_prim].Speed; MaxFireRange = _bat[_prim].Range * 0.85; }
    _aimRef = _bat[_prim].G.Count > 0 ? (IMyTerminalBlock)_bat[_prim].G[0] : _rc;
}

void BatFire(bool allow, Vector3D myVel)
{
    var b = _bat[1 - _prim];
    if (b.G.Count == 0) return;
    bool shoot = false;
    if (allow && _tgt != null)
    {
        Vector3D gp = Vector3D.Zero, pt, dir; int n = 0; IMyUserControllableGun f = null;
        foreach (var g in b.G) if (g.IsFunctional) { gp += g.GetPosition(); n++; if (f == null) f = g; }
        if (n > 0)
        {
            gp /= n;
            double sp = ProjSpeed, sl = _leadT; ProjSpeed = b.Speed;
            bool ok = Lead(_tgt, gp, myVel, out pt, out dir);
            ProjSpeed = sp; _leadT = sl;
            if (ok)
            {
                Vector3D fw = f.WorldMatrix.Forward;
                double dist = Vector3D.Distance(pt, gp), aimR = AimActive(_tgt) ? Math.Min(_tgt.Radius * 0.6, 3) : _tgt.Radius * 0.6;
                double win = Math.Max(0.3, Math.Atan(aimR / Math.Max(dist, 1)) * 57.2958) * FireTol, err = Math.Acos(MathHelper.Clamp(Vector3D.Dot(fw, dir), -1, 1)) * 57.2958;
                shoot = dist >= b.MinRange && dist < b.Range * 0.85 && err < win && _now > _holdFireUntil && _now - _tgt.Seen < FireStaleMax;
                if (shoot && FriendCheck) { MatesInLine(gp, fw, dist); if (_now < _holdFireUntil) shoot = false; }
            }
        }
    }
    if (b.Cool > 0) { if (shoot && _now >= b.Next) { b.Next = _now + b.Cool; foreach (var g in b.G) if (g.IsFunctional) g.ShootOnce(); } }
    else if (shoot != b.On) { b.On = shoot; foreach (var g in b.G) if (g.IsFunctional) g.ApplyAction(shoot ? "Shoot_On" : "Shoot_Off"); }
}

string BatStatus() { return "Batt " + _bat[0].Cls + "x" + _bat[0].G.Count + " " + _bat[1].Cls + "x" + _bat[1].G.Count + " prim B" + (_prim + 1) + "\n"; }
// <<< BATTERY v0.1 <<<
