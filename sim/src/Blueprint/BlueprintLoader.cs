// SLOT A. Parses out/Blueprints/<Name>/bp.sbc (System.Xml; System.Xml.Linq is NOT installed; load time only) into plain data.
// Cache: BlueprintLoader.Get(name) keeps parsed blueprints per process (thread-safe, immutable after load).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using VRageMath;
using SeSim.Core;

// M0 (control_arch S7): <Toolbar> of a block (Timer, Event Controller, Button Panel, cockpit; ToolbarItemTerminalBlock / TerminalGroup /
// Weapon slots), <SelectedBlocks> of an Event Controller and the grid's <BlockGroups> (Vector3I cells) are parsed into BlockData.Toolbar /
// Selected and GridData.Groups. They are only EXECUTED when SimSettings.BlockAutomation is on (SimToolbar, SimEventController).
namespace SeSim.Blueprint
{
    public sealed class BlueprintData
    {
        public string Name;                 // DisplayName / folder name
        public string Path;
        public readonly List<GridData> Grids = new List<GridData>();
    }

    public sealed class GridData
    {
        public long EntityId;
        public string DisplayName;
        public bool Large;                  // GridSizeEnum
        public bool IsStatic;
        public Vector3D Position, Forward = Vector3D.Forward, Up = Vector3D.Up;   // PositionAndOrientation (informational)
        public bool DampenersEnabled = true;
        public readonly List<BlockData> Blocks = new List<BlockData>();
        /// <summary>&lt;BlockGroups&gt;: group name -> member cells (the game stores Vector3I cells, not ids). Empty for most blueprints.</summary>
        public readonly List<GroupData> Groups = new List<GroupData>();
    }

    public sealed class GroupData
    {
        public string Name;
        public int Construct;                // index of the blueprint grid the group came from (fused grids keep their own groups)
        public readonly List<Vector3I> Cells = new List<Vector3I>();
    }

    /// <summary>One slot of a block's toolbar. Kind: "Block" (ToolbarItemTerminalBlock: Action on BlockId), "Group" (ToolbarItemTerminalGroup:
    /// Action on the block group GroupName of the owner's grid), "Weapon" (ToolbarItemWeapon: DefinitionType/Subtype).</summary>
    public sealed class ToolbarItem
    {
        public int Slot;
        public string Kind, Action, GroupName, WeaponType, WeaponSubtype;
        public long BlockId;                 // blueprint entity id (remapped at spawn: BlockInst.Toolbar)
        public string Argument;              // first parameter value (String/Single/Int32...), null when none
    }

    public sealed class BlockData
    {
        public string XsiType;              // "MyObjectBuilder_Thrust"
        public string TypeId;               // "Thrust" (XsiType without "MyObjectBuilder_")
        public string Subtype;              // "" allowed (gatling, rocket launcher)
        public long EntityId;               // 0 for armor
        public Vector3I Min;
        public MyBlockOrientation Orientation = MyBlockOrientation.Identity;   // default Forward/Up when the element is absent
        public int Origin = -1;             // >= 0: index of the blueprint grid this block came from (set by GridFuse; -1 = the grid it is stored in)
        public string CustomName, CustomData, Program, Storage;
        public bool Enabled = true;
        public long Owner;                  // <Owner> if present (ignored: the scenario assigns the team identity)
        /// <summary>Simple child elements by name (text), e.g. "BroadcastRadius"="5000", "CurrentStoredPower"="1",
        /// "ChargeMode"="0", "IsArmed", "CountdownMs"; attribute vectors flattened as "FieldMin.x". Component data that matters
        /// is flattened too: "StoredPower" (railgun capacitor).</summary>
        public readonly Dictionary<string, string> Props = new Dictionary<string, string>();
        public readonly List<GridData> ProjectedGrids = new List<GridData>();   // projectors' pre-loaded drones
        public readonly List<ToolbarItem> Toolbar = new List<ToolbarItem>();   // slots of <Toolbar> (empty = none)
        public readonly List<long> Selected = new List<long>();                // Event Controller <SelectedBlocks>
        public BlockData Clone()
        {
            var c = (BlockData)MemberwiseClone();
            return c;
        }
        public string Key { get { return TypeId + "/" + Subtype; } }      // defs_stats.json key

        public string Prop(string name, string dflt) { string v; return Props.TryGetValue(name, out v) ? v : dflt; }
        public double PropNum(string name, double dflt)
        {
            string v; double d;
            if (Props.TryGetValue(name, out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return dflt;
        }
        public bool PropBool(string name, bool dflt)
        {
            string v; if (!Props.TryGetValue(name, out v)) return dflt;
            return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ? true : (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) ? false : dflt);
        }
    }

    public static class BlueprintLoader
    {
        const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";
        static readonly object CacheLock = new object();
        static readonly Dictionary<string, BlueprintData> Cache = new Dictionary<string, BlueprintData>();

        public static BlueprintData Load(string bpSbcPath)
        {
            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;     // Program / Storage text must be exact
            using (var r = XmlReader.Create(bpSbcPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc.Load(r);
            var bp = new BlueprintData { Path = bpSbcPath };
            XmlElement shipBp = FindFirst(doc.DocumentElement, "ShipBlueprint");
            if (shipBp == null) throw new InvalidDataException(bpSbcPath + ": no ShipBlueprint element");
            bp.Name = ChildText(shipBp, "DisplayName", null);
            if (bp.Name == null) bp.Name = new DirectoryInfo(System.IO.Path.GetDirectoryName(bpSbcPath)).Name;
            XmlElement grids = Child(shipBp, "CubeGrids");
            if (grids == null) throw new InvalidDataException(bpSbcPath + ": no CubeGrids");
            foreach (XmlNode n in grids.ChildNodes)
            {
                var ge = n as XmlElement;
                if (ge != null)
                {
                    var pg = ParseGrid(ge, bp.Name);
                    foreach (var gd in pg.Groups) gd.Construct = bp.Grids.Count;
                    bp.Grids.Add(pg);
                }
            }
            if (bp.Grids.Count == 0) throw new InvalidDataException(bpSbcPath + ": no grids");
            return bp;
        }

        /// <summary>Loads blueprintsDir/name/bp.sbc once per process.</summary>
        public static BlueprintData Get(string blueprintsDir, string name)
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.Combine(blueprintsDir, name), "bp.sbc"));
            lock (CacheLock)
            {
                BlueprintData bp;
                if (Cache.TryGetValue(path, out bp)) return bp;
                if (!File.Exists(path)) throw new FileNotFoundException("blueprint not found (run python3 -I tools/build_all.py): " + path);
                bp = Load(path);
                Cache[path] = bp;
                return bp;
            }
        }

        // ------------------------------------------------------------------------------------------------------------

        static XmlElement FindFirst(XmlElement root, string name)
        {
            if (root.Name == name) return root;
            foreach (XmlNode n in root.ChildNodes) { var e = n as XmlElement; if (e != null) { var r = FindFirst(e, name); if (r != null) return r; } }
            return null;
        }
        static XmlElement Child(XmlElement p, string name)
        {
            foreach (XmlNode n in p.ChildNodes) { var e = n as XmlElement; if (e != null && e.Name == name) return e; }
            return null;
        }
        static string ChildText(XmlElement p, string name, string dflt) { var c = Child(p, name); return c == null ? dflt : c.InnerText; }
        static bool HasElementChildren(XmlElement e) { foreach (XmlNode n in e.ChildNodes) if (n is XmlElement) return true; return false; }
        static double Dbl(string s) { return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }
        static double Attr(XmlElement e, string a) { string s = e.GetAttribute(a); return s.Length == 0 ? 0 : Dbl(s); }
        static long Long(string s) { return long.Parse(s.Trim(), CultureInfo.InvariantCulture); }
        static bool Bool(string s, bool d)
        {
            if (s == null) return d; s = s.Trim();
            return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ? true : (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) ? false : d);
        }

        static GridData ParseGrid(XmlElement ge, string fallbackName)
        {
            var g = new GridData();
            g.EntityId = Long(ChildText(ge, "EntityId", "0"));
            g.DisplayName = ChildText(ge, "DisplayName", fallbackName);
            g.Large = ChildText(ge, "GridSizeEnum", "Small").Trim() == "Large";
            g.IsStatic = Bool(ChildText(ge, "IsStatic", null), false);
            g.DampenersEnabled = Bool(ChildText(ge, "DampenersEnabled", null), true);
            var po = Child(ge, "PositionAndOrientation");
            if (po != null)
            {
                var p = Child(po, "Position"); var f = Child(po, "Forward"); var u = Child(po, "Up");
                if (p != null) g.Position = new Vector3D(Attr(p, "x"), Attr(p, "y"), Attr(p, "z"));
                if (f != null) g.Forward = new Vector3D(Attr(f, "x"), Attr(f, "y"), Attr(f, "z"));
                if (u != null) g.Up = new Vector3D(Attr(u, "x"), Attr(u, "y"), Attr(u, "z"));
            }
            var bg = Child(ge, "BlockGroups");
            if (bg != null)
                foreach (XmlNode n in bg.ChildNodes)
                {
                    var gx = n as XmlElement;
                    if (gx == null) continue;
                    var gd = new GroupData { Name = ChildText(gx, "Name", "") };
                    var gb = Child(gx, "Blocks");
                    if (gb != null)
                        foreach (XmlNode vn in gb.ChildNodes)
                        {
                            var v = vn as XmlElement;
                            if (v == null) continue;
                            gd.Cells.Add(new Vector3I((int)Dbl(ChildText(v, "X", "0")), (int)Dbl(ChildText(v, "Y", "0")), (int)Dbl(ChildText(v, "Z", "0"))));
                        }
                    g.Groups.Add(gd);
                }
            var cb = Child(ge, "CubeBlocks");
            if (cb != null)
                foreach (XmlNode n in cb.ChildNodes)
                {
                    var be = n as XmlElement;
                    if (be != null && be.Name == "MyObjectBuilder_CubeBlock") g.Blocks.Add(ParseBlock(be));
                }
            return g;
        }

        static BlockData ParseBlock(XmlElement be)
        {
            var b = new BlockData();
            b.XsiType = be.GetAttribute("type", Xsi);
            if (string.IsNullOrEmpty(b.XsiType)) b.XsiType = "MyObjectBuilder_CubeBlock";
            b.TypeId = b.XsiType.StartsWith("MyObjectBuilder_", StringComparison.Ordinal) ? b.XsiType.Substring(16) : b.XsiType;
            b.Subtype = "";
            foreach (XmlNode n in be.ChildNodes)
            {
                var c = n as XmlElement;
                if (c == null) continue;
                switch (c.Name)
                {
                    case "SubtypeName": b.Subtype = c.InnerText.Trim(); break;
                    case "EntityId": b.EntityId = Long(c.InnerText); break;
                    case "Min": b.Min = new Vector3I((int)Attr(c, "x"), (int)Attr(c, "y"), (int)Attr(c, "z")); break;
                    case "BlockOrientation":
                        b.Orientation = new MyBlockOrientation(
                            c.HasAttribute("Forward") ? OrientMath.ParseDir(c.GetAttribute("Forward")) : Base6Directions.Direction.Forward,
                            c.HasAttribute("Up") ? OrientMath.ParseDir(c.GetAttribute("Up")) : Base6Directions.Direction.Up);
                        break;
                    case "CustomName": b.CustomName = c.InnerText; break;
                    case "CustomData": b.CustomData = c.InnerText; break;
                    case "Program": b.Program = c.InnerText; break;
                    case "Storage": b.Storage = c.InnerText; break;
                    case "Enabled": b.Enabled = Bool(c.InnerText, true); break;
                    case "Owner": b.Owner = Long(c.InnerText); break;
                    case "ProjectedGrids":
                        foreach (XmlNode gn in c.ChildNodes) { var ge = gn as XmlElement; if (ge != null) b.ProjectedGrids.Add(ParseGrid(ge, b.CustomName)); }
                        break;
                    case "ComponentContainer":
                        ScanForStoredPower(c, b);
                        { int ts = CountElements(c, "MySerializedTextPanelData"); if (ts > 0) b.Props["TextSurfaces"] = ts.ToString(CultureInfo.InvariantCulture); }
                        break;
                    case "Toolbar":
                        ParseToolbar(c, b);
                        break;
                    case "SelectedBlocks":
                        foreach (XmlNode sn in c.ChildNodes) { var se = sn as XmlElement; if (se != null && se.Name == "long") { long id; if (long.TryParse(se.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) b.Selected.Add(id); } }
                        break;
                    default:
                        if (!HasElementChildren(c))
                        {
                            if (c.Attributes.Count > 0 && c.InnerText.Length == 0)
                            {
                                foreach (XmlAttribute a in c.Attributes)
                                    if (a.LocalName != "nil" && a.NamespaceURI != Xsi) b.Props[c.Name + "." + a.LocalName] = a.Value;
                            }
                            else b.Props[c.Name] = c.InnerText;
                        }
                        break;
                }
            }
            return b;
        }

        static int CountElements(XmlElement e, string name)
        {
            int n = 0;
            foreach (XmlNode c in e.ChildNodes)
            {
                var ce = c as XmlElement;
                if (ce == null) continue;
                if (ce.Name == name) n++;
                else n += CountElements(ce, name);
            }
            return n;
        }

        static void ParseToolbar(XmlElement tb, BlockData b)
        {
            var slots = Child(tb, "Slots");
            if (slots == null) return;
            foreach (XmlNode sn in slots.ChildNodes)
            {
                var s = sn as XmlElement;
                if (s == null || s.Name != "Slot") continue;
                var d = Child(s, "Data");
                if (d == null) continue;
                var it = new ToolbarItem();
                int idx; it.Slot = int.TryParse(ChildText(s, "Index", "0").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) ? idx : 0;
                string ty = d.GetAttribute("type", Xsi);
                if (ty.StartsWith("MyObjectBuilder_", StringComparison.Ordinal)) ty = ty.Substring(16);
                if (ty == "ToolbarItemTerminalBlock" || ty == "ToolbarItemTerminalGroup")
                {
                    it.Kind = ty == "ToolbarItemTerminalBlock" ? "Block" : "Group";
                    it.Action = ChildText(d, "Action", "");
                    it.GroupName = ChildText(d, "GroupName", null);
                    long id; if (long.TryParse(ChildText(d, "BlockEntityId", "0").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) it.BlockId = id;
                    var ps = Child(d, "Parameters");
                    if (ps != null)
                        foreach (XmlNode pn in ps.ChildNodes)
                        {
                            var pe = pn as XmlElement;
                            if (pe != null) { it.Argument = ChildText(pe, "Value", null); break; }
                        }
                }
                else if (ty == "ToolbarItemWeapon")
                {
                    it.Kind = "Weapon";
                    var di = Child(d, "DefinitionId");
                    if (di != null)
                    {
                        string wt = di.GetAttribute("Type");
                        it.WeaponType = wt.StartsWith("MyObjectBuilder_", StringComparison.Ordinal) ? wt.Substring(16) : wt;
                        it.WeaponSubtype = di.GetAttribute("Subtype");
                    }
                }
                else continue;
                b.Toolbar.Add(it);
            }
        }

        // railgun capacitor: <Component xsi:type="MyObjectBuilder_EntityCapacitorComponent"><StoredPower>..</StoredPower>
        static void ScanForStoredPower(XmlElement e, BlockData b)
        {
            foreach (XmlNode n in e.ChildNodes)
            {
                var c = n as XmlElement;
                if (c == null) continue;
                if (c.Name == "StoredPower" && !HasElementChildren(c)) { b.Props["StoredPower"] = c.InnerText; continue; }
                if (HasElementChildren(c)) ScanForStoredPower(c, b);
            }
        }
    }
}
