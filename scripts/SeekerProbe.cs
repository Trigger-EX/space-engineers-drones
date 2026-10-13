// SeekerProbe v1.0 - in-game test: can a script learn WHERE an enemy's power blocks are through a turret?
// Put it in any Programmable Block on a ship that has at least one turret (Gatling, Missile, Interior, Assault Cannon, Artillery)
// with [Seeker] in its name (no [Seeker] turret: every turret on this grid is used). Run it once; it runs itself every second.
//   argument "group PowerSystems" (or Weapons / Propulsion / Default) sets the targeting group of the probe turrets
//   argument "clear" empties the log in Custom Data
// What it shows (PB screen and its own display), per turret:
//   group     the targeting group the turret reports (GetTargetingGroup); "set ok" if SetTargetingGroup worked
//   target    HasTarget / IsAimed / the targeted entity's name and distance
//   hitpos    whether GetTargetedEntity() carries a HitPosition, and how far it is from the target's centre
//             -> "hitpos 12.3 m from centre" = the game hands us the subsystem block's position directly (best case)
//   aim ray   where the turret's barrel points (Azimuth/Elevation): a camera with [Seeker] in its name (else any camera)
//             raycasts that line and reports how far from the target's centre the hull hit is, and whether the aim line
//             passes near the centre (turret on the centre) or off it (turret on a subsystem)
// Every new reading is also appended to Custom Data (last 60 lines) so you can copy it out after the fight.
// Decoys on the target pull the turret onto themselves first: note whether the target ship has decoys.
string Group = "PowerSystems";
List<IMyLargeTurretBase> _t = new List<IMyLargeTurretBase>();
List<IMyCameraBlock> _c = new List<IMyCameraBlock>();
List<string> _log = new List<string>();
IMyTextSurface _lcd;
int _n;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
    Scan();
}

void Scan()
{
    _t.Clear(); _c.Clear();
    GridTerminalSystem.GetBlocksOfType(_t, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("[Seeker]"));
    if (_t.Count == 0) GridTerminalSystem.GetBlocksOfType(_t, b => b.CubeGrid == Me.CubeGrid);
    GridTerminalSystem.GetBlocksOfType(_c, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("[Seeker]"));
    if (_c.Count == 0) GridTerminalSystem.GetBlocksOfType(_c, b => b.CubeGrid == Me.CubeGrid);
    foreach (var c in _c) c.EnableRaycast = true;
    _lcd = Me.GetSurface(0);
    _lcd.ContentType = ContentType.TEXT_AND_IMAGE;
}

public void Main(string arg, UpdateType u)
{
    var w = arg.Trim().Split(' ');
    if (w[0] == "group" && w.Length > 1) { Group = w[1]; Log("group -> " + Group); }
    if (w[0] == "clear") { _log.Clear(); Me.CustomData = ""; }
    if (++_n % 10 == 1) Scan();
    var s = new StringBuilder("SeekerProbe v1.0  turrets " + _t.Count + "  cameras " + _c.Count + "\n");
    foreach (var t in _t)
    {
        string set = "";
        try { if (Group.Length > 0 && t.GetTargetingGroup() != Group) { t.SetTargetingGroup(Group); set = " (set)"; } }
        catch (Exception x) { set = " SET FAILED: " + x.Message; }
        string g;
        try { g = t.GetTargetingGroup(); } catch (Exception x) { g = "? " + x.Message; }
        s.Append(t.CustomName + "\n  group " + g + set + "\n");
        if (!t.HasTarget) { s.Append("  no target\n"); continue; }
        var e = t.GetTargetedEntity();
        double dist = (e.Position - t.GetPosition()).Length();
        s.Append("  target " + e.Name + " " + e.Type + " " + dist.ToString("0") + " m, aimed " + t.IsAimed + "\n");
        string hp = e.HitPosition.HasValue ? "hitpos " + (e.HitPosition.Value - e.Position).Length().ToString("0.0") + " m from centre" : "no hitpos";
        s.Append("  " + hp + "\n");
        Vector3D d; Vector3D.CreateFromAzimuthAndElevation(t.Azimuth, t.Elevation, out d);
        d = Vector3D.TransformNormal(d, t.WorldMatrix);
        Vector3D o = t.GetPosition();
        double along = Vector3D.Dot(e.Position - o, d);
        double off = (e.Position - (o + d * along)).Length();      // how far the aim line passes from the target's centre
        string ray = "aim line passes " + off.ToString("0.0") + " m from centre";
        foreach (var c in _c)
        {
            Vector3D q = o + d * (along + 30);
            if (!c.CanScan(q)) continue;
            var h = c.Raycast(q);
            if (h.IsEmpty()) { ray += ", ray: nothing"; break; }
            if (h.EntityId != e.EntityId) { ray += ", ray hit " + h.Name; break; }
            if (h.HitPosition.HasValue) ray += ", hull hit " + (h.HitPosition.Value - e.Position).Length().ToString("0.0") + " m from centre";
            break;
        }
        s.Append("  " + ray + "\n");
        Log(t.CustomName + ": " + g + " | " + hp + " | " + ray);
    }
    string txt = s.ToString();
    Echo(txt);
    if (_lcd != null) _lcd.WriteText(txt);
}

void Log(string line)
{
    _log.Add(DateTime.Now.ToString("HH:mm:ss") + " " + line);
    while (_log.Count > 60) _log.RemoveAt(0);
    Me.CustomData = string.Join("\n", _log);
}
