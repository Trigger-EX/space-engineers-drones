// SLOT D. Scenario generation: the 1v1 matrix (all unordered pairs incl. mirrors) and the unfair-fight generator.
// Models (Full variants): Aegis, Basilisk, Gnat, Harrier, Hornet, Lancer, Longbow L, Longbow S, Mantis, Midge, Stinger, Wasp, Watcher.
// Unfair: for each ordered pair (A,B) with A != B: 1v2, 1v3, 2v3 (A side smaller); mixed: Watcher+3 Gnats, Hornet+Longbow S,
// Wasp+2 Gnats... vs each single model; pirate: each model x1, x2, x3 vs the Pirate ship. --filter limits by regex on Name.
//
// Kinds and counts (UnfairMode "curated", the default; "full" = all ordered pairs for 1v3 and 2v3 as well):
//   1v1    91 = 13*14/2 (every unordered pair including mirrors)
//   1v2   156 = 13*12   (A alone vs 2xB, every ordered pair of distinct models)
//   1v3, 2v3: curated = A in {Aegis, Basilisk, Harrier, Hornet, Lancer, Longbow L, Longbow S, Mantis, Wasp} vs
//             B in {Gnat, Hornet, Wasp, Midge, Stinger} (A != B); full = all 156 ordered pairs
//   mixed  4 compositions (Watcher+3xGnat, Hornet+Longbow S, Wasp+2xGnat, Lancer+Mantis) vs each single model (13) and vs each
//          other composition (unordered, incl. mirrors: 10)
//   swarm  6, 9 and 12 Gnats (a hive swarm) vs each single model (13) and vs the Pirate
//   pirate each armed model x1, x2, x3 vs the Pirate ship (code-built, large grid with turrets)
//   enemy  only with --enemies DIR: every blueprint folder in DIR (e.g. a user's warship) alone vs N of each armed model
//          (N from --enemy-counts, default 1,2,3,4,6,8,12) and vs each mixed composition ("1vX"). Only the blueprint's largest grid
//          is spawned (rotor/hinge/piston subgrids are dropped). It is flown by the pirate brain (--enemy-brain pirate, default)
//          or holds position with dampeners on (--enemy-brain hold); either way its turrets fight on their own.
//   kamikaze  incoming swarms of small warhead drones (docs/kamikaze-defence.md): each scripted combat model x1 vs 4 and 8 Stingers,
//          x2 vs 8 Stingers, plus 3 Gnats vs 8. The Stinger team gets a radar cue every 0.5 s (Scenario.SwarmCueSeconds), so every run is
//          a real attack (on their own the Stingers only see 50 m by sensor and never launch); 10 * 3 + 1 = 31 scenarios
// Team 0 is the first-named side, team 1 the second. Seeds decide positions/orientations, so the side order has no bias.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SeSim.Run
{
    public static class ScenarioGen
    {
        public static readonly string[] Models = { "Aegis", "Basilisk", "Gnat", "Harrier", "Hornet", "Lancer", "Longbow L", "Longbow S", "Mantis", "Midge", "Stinger", "Wasp", "Watcher" };
        /// <summary>Armed models (Watcher is an unarmed spotter: it can never be combat capable).</summary>
        public static readonly string[] ArmedModels = { "Aegis", "Basilisk", "Gnat", "Harrier", "Hornet", "Lancer", "Longbow L", "Longbow S", "Mantis", "Midge", "Stinger", "Wasp" };
        static readonly string[] CuratedBig = { "Aegis", "Basilisk", "Harrier", "Hornet", "Lancer", "Longbow L", "Longbow S", "Mantis", "Wasp" };
        static readonly string[] CuratedSmall = { "Gnat", "Hornet", "Wasp", "Midge", "Stinger" };
        static readonly string[][] MixedTeams =
        {
            new[] { "Watcher", "Gnat", "Gnat", "Gnat" },
            new[] { "Hornet", "Longbow S" },
            new[] { "Wasp", "Gnat", "Gnat" },
            new[] { "Lancer", "Mantis" },
        };
        public static string UnfairMode = "curated";       // "curated" | "full"

        // ---- enemy blueprints (--enemies DIR) ----
        public static string EnemyDir;                      // null = no enemy scenarios
        public static int[] EnemyCounts = { 1, 2, 3, 4, 6, 8, 12 };
        public static string EnemyBrain = "pirate";         // "pirate" | "hold"
        /// <summary>--enemy-scripted (M0): the enemy blueprints from --enemies run their OWN Programmable Block scripts (start commands "Helm:hunt" and
        /// "Staff:wing satellite"), keep every grid that fuses with the main one (merge pairs), and use no Pirate / hold brain.</summary>
        public static bool EnemyScripted;
        static List<string> _enemies; static string _enemiesDir;

        /// <summary>Blueprint folders (containing bp.sbc) in EnemyDir, sorted; empty when EnemyDir is unset.</summary>
        public static List<string> Enemies()
        {
            if (_enemies != null && _enemiesDir == EnemyDir) return _enemies;
            var l = new List<string>();
            if (!string.IsNullOrEmpty(EnemyDir))
            {
                if (!System.IO.Directory.Exists(EnemyDir)) throw new System.IO.DirectoryNotFoundException("--enemies folder not found: " + EnemyDir);
                foreach (string d in System.IO.Directory.GetDirectories(EnemyDir))
                    if (System.IO.File.Exists(System.IO.Path.Combine(d, "bp.sbc"))) l.Add(System.IO.Path.GetFileName(d));
                l.Sort(StringComparer.Ordinal);
                if (l.Count == 0) throw new System.IO.FileNotFoundException("--enemies " + EnemyDir + ": no <Name>/bp.sbc blueprint folders inside");
            }
            _enemies = l; _enemiesDir = EnemyDir;
            return l;
        }

        public static bool IsEnemy(string model) { return EnemyDir != null && Enemies().Contains(model); }

        static readonly object EnemyLock = new object();
        static readonly Dictionary<string, SeSim.Blueprint.BlueprintData> EnemyBps = new Dictionary<string, SeSim.Blueprint.BlueprintData>();

        /// <summary>The enemy blueprint reduced to its largest grid (cached, immutable).</summary>
        public static SeSim.Blueprint.BlueprintData EnemyBlueprint(string name) { return EnemyBlueprint(name, null, false); }

        /// <summary>scripted: the grids are merge-fused first (GridFuse) and the largest fused grid is kept, so a warship whose bays hold merged drones keeps them.</summary>
        public static SeSim.Blueprint.BlueprintData EnemyBlueprint(string name, SeSim.Blueprint.DefDb defs, bool scripted)
        {
            lock (EnemyLock)
            {
                SeSim.Blueprint.BlueprintData bp;
                string key = scripted ? name + "\n#scripted" : name;
                if (EnemyBps.TryGetValue(key, out bp)) return bp;
                var full = SeSim.Blueprint.BlueprintLoader.Get(EnemyDir, name);
                System.Collections.Generic.IList<SeSim.Blueprint.GridData> gl = full.Grids;
                if (scripted && defs != null) gl = SeSim.Core.GridFuse.Fuse(defs, full.Grids);
                var main = gl[0];
                for (int i = 1; i < gl.Count; i++) if (gl[i].Blocks.Count > main.Blocks.Count) main = gl[i];
                bp = new SeSim.Blueprint.BlueprintData { Name = name, Path = full.Path };
                bp.Grids.Add(main);
                EnemyBps[key] = bp;
                return bp;
            }
        }

        /// <summary>--enemy-fleets: explicit drone fleets ("3xHornet+2xGnat+Longbow L") fought against each enemy instead of the
        /// counts x models grid and the mixed teams. Null = the grid.</summary>
        public static List<string[]> EnemyFleets;
        /// <summary>Blueprints usable in --enemy-fleets but not part of the standard matrices.</summary>
        public static readonly string[] FleetOnlyModels = { "Basilisk Seeker", "Longbow L Seeker" };

        /// <summary>Parses "3xHornet+2xGnat+Longbow L" into a model list (count prefix "Nx" optional); models must be known.</summary>
        public static string[] ParseFleet(string spec)
        {
            var l = new List<string>();
            foreach (string part in spec.Split('+'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int n = 1;
                int x = p.IndexOf('x');
                int parsed;
                if (x > 0 && int.TryParse(p.Substring(0, x), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsed)) { n = parsed; p = p.Substring(x + 1).Trim(); }
                if (Array.IndexOf(Models, p) < 0 && Array.IndexOf(FleetOnlyModels, p) < 0) throw new ArgumentException("unknown model '" + p + "' in fleet '" + spec + "'");
                if (n < 1 || n > 64) throw new ArgumentException("bad count in fleet '" + spec + "'");
                for (int i = 0; i < n; i++) l.Add(p);
            }
            if (l.Count == 0) throw new ArgumentException("empty fleet '" + spec + "'");
            return l.ToArray();
        }

        /// <summary>M0: an ad-hoc scenario for `one --scenario "kind/A-vs-B"` where A and B are '+'-separated blueprint folder names of out/Blueprints
        /// (optional "Nx" count prefix), e.g. "pilot/Hornet Pilot Test-vs-Hornet", "1v1/Twin PB Test-vs-2xHornet". Null when a name is not a blueprint folder.</summary>
        public static Scenario Custom(string name, string bpDir)
        {
            int slash = name.IndexOf('/');
            int vs = name.IndexOf("-vs-", StringComparison.Ordinal);
            if (slash <= 0 || vs < slash) return null;
            string kind = name.Substring(0, slash);
            var a = ParseSide(name.Substring(slash + 1, vs - slash - 1), bpDir);
            var b = ParseSide(name.Substring(vs + 4), bpDir);
            if (a == null || b == null) return null;
            var sc = Build(kind, a, b);
            if (sc.Name != name) sc.Name = name;
            sc.BlueprintCustomData = sc.RigidSubgrids = kind == "ship";      // M6: warship scenarios start with the Custom Data the blueprint carries
            return sc;
        }

        static string[] ParseSide(string side, string bpDir)
        {
            var l = new List<string>();
            foreach (string part in side.Split('+'))
            {
                string p = part.Trim(); int n = 1;
                int x = p.IndexOf('x'); int parsed;
                if (x > 0 && int.TryParse(p.Substring(0, x), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsed) && parsed >= 1 && parsed <= 64) { n = parsed; p = p.Substring(x + 1).Trim(); }
                if (p != "Pirate" && !IsEnemy(p) && !System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(bpDir, p), "bp.sbc"))) return null;
                for (int i = 0; i < n; i++) l.Add(p);
            }
            return l.Count == 0 ? null : l.ToArray();
        }

        public static List<Scenario> EnemyScenarios()
        {
            var l = new List<Scenario>();
            foreach (string e in Enemies())
            {
                if (EnemyFleets != null)
                {
                    for (int i = 0; i < EnemyFleets.Count; i++) l.Add(Build("enemy", new[] { e }, EnemyFleets[i]));
                    continue;
                }
                for (int j = 0; j < ArmedModels.Length; j++)
                    for (int c = 0; c < EnemyCounts.Length; c++)
                        l.Add(Build("enemy", new[] { e }, Rep(ArmedModels[j], EnemyCounts[c])));
                for (int i = 0; i < MixedTeams.Length; i++) l.Add(Build("enemy", new[] { e }, MixedTeams[i]));
            }
            return l;
        }

        // ------------------------------------------------------------------------------------------------------
        static string SideName(string[] side)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < side.Length)
            {
                int j = i; while (j < side.Length && side[j] == side[i]) j++;
                if (sb.Length > 0) sb.Append('+');
                if (j - i > 1) sb.Append(j - i).Append('x');
                sb.Append(side[i]);
                i = j;
            }
            return sb.ToString();
        }

        static string[] Rep(string model, int n)
        {
            var a = new string[n];
            for (int i = 0; i < n; i++) a[i] = model;
            return a;
        }

        public static Scenario Build(string kind, string[] a, string[] b)
        {
            var sc = new Scenario { Kind = kind, Name = kind + "/" + SideName(a) + "-vs-" + SideName(b) };
            sc.Teams.Add(new TeamSpec(0, SideName(a)));
            sc.Teams.Add(new TeamSpec(1, SideName(b)));
            for (int i = 0; i < a.Length; i++) Add(sc, a[i], 0, "A" + (i + 1));
            for (int i = 0; i < b.Length; i++) Add(sc, b[i], 1, "B" + (i + 1));
            return sc;
        }

        static void Add(Scenario sc, string model, int team, string role)
        {
            var s = new ShipSpec { Model = model, Team = team, Role = role };
            DefaultCommands(s);
            sc.Ships.Add(s);
        }

        /// <summary>Default start commands per model (e.g. "hunt" for script drones; Stinger "launch").</summary>
        public static void DefaultCommands(ShipSpec s)
        {
            s.Commands.Clear();
            switch (s.Model)
            {
                case "Midge":      // scriptless: vanilla AI brain
                case "Pirate":     // brain
                    break;
                case "Stinger":
                    s.Commands.Add(new KeyValuePair<double, string>(0.5, "launch"));
                    break;
                default:
                    if (IsEnemy(s.Model))
                    {
                        // --enemy-scripted: the blueprint's own PBs fly it: Helm hunts, Staff sends the wing to its satellite ring
                        if (EnemyScripted)
                        {
                            s.Commands.Add(new KeyValuePair<double, string>(0.5, "Helm:hunt"));
                            s.Commands.Add(new KeyValuePair<double, string>(1.0, "Staff:wing satellite"));
                        }
                        break;               // otherwise: brain + turrets, no scripts started
                    }
                    // script drones: HUNT (Gnat: ATTACK), Watcher runs HornetDroneAI in hunt/scout mode
                    s.Commands.Add(new KeyValuePair<double, string>(0.5, "hunt"));
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------------------
        public static List<Scenario> Matrix1v1()
        {
            var l = new List<Scenario>();
            for (int i = 0; i < Models.Length; i++)
                for (int j = i; j < Models.Length; j++)
                    l.Add(Build("1v1", new[] { Models[i] }, new[] { Models[j] }));
            return l;
        }

        public static List<Scenario> Unfair()
        {
            var l = new List<Scenario>();
            bool full = UnfairMode == "full";
            for (int i = 0; i < Models.Length; i++)
                for (int j = 0; j < Models.Length; j++)
                    if (i != j) l.Add(Build("1v2", new[] { Models[i] }, Rep(Models[j], 2)));
            if (full)
            {
                for (int i = 0; i < Models.Length; i++)
                    for (int j = 0; j < Models.Length; j++)
                        if (i != j)
                        {
                            l.Add(Build("1v3", new[] { Models[i] }, Rep(Models[j], 3)));
                            l.Add(Build("2v3", Rep(Models[i], 2), Rep(Models[j], 3)));
                        }
            }
            else
            {
                for (int i = 0; i < CuratedBig.Length; i++)
                    for (int j = 0; j < CuratedSmall.Length; j++)
                        if (CuratedBig[i] != CuratedSmall[j])
                        {
                            l.Add(Build("1v3", new[] { CuratedBig[i] }, Rep(CuratedSmall[j], 3)));
                            l.Add(Build("2v3", Rep(CuratedBig[i], 2), Rep(CuratedSmall[j], 3)));
                        }
            }
            return l;
        }

        public static List<Scenario> Mixed()
        {
            var l = new List<Scenario>();
            for (int i = 0; i < MixedTeams.Length; i++)
                for (int j = 0; j < Models.Length; j++)
                    l.Add(Build("mixed", MixedTeams[i], new[] { Models[j] }));
            for (int i = 0; i < MixedTeams.Length; i++)
                for (int j = i; j < MixedTeams.Length; j++)
                    l.Add(Build("mixed", MixedTeams[i], MixedTeams[j]));
            return l;
        }

        /// <summary>Hive swarms: 6, 9 and 12 Gnats vs each single model and vs the Pirate.</summary>
        public static List<Scenario> Swarm()
        {
            var l = new List<Scenario>();
            int[] sizes = { 6, 9, 12 };
            for (int s = 0; s < sizes.Length; s++)
            {
                for (int j = 0; j < Models.Length; j++) l.Add(Build("swarm", Rep("Gnat", sizes[s]), new[] { Models[j] }));
                l.Add(Build("swarm", Rep("Gnat", sizes[s]), new[] { "Pirate" }));
            }
            return l;
        }

        public static List<Scenario> Pirate()
        {
            var l = new List<Scenario>();
            for (int i = 0; i < ArmedModels.Length; i++)
                for (int n = 1; n <= 3; n++)
                    l.Add(Build("pirate", Rep(ArmedModels[i], n), new[] { "Pirate" }));
            return l;
        }

        static readonly string[] KamikazeTargets = { "Aegis", "Basilisk", "Gnat", "Harrier", "Hornet", "Lancer", "Longbow L", "Longbow S", "Mantis", "Wasp" };

        /// <summary>Incoming kamikaze swarms: cued Stingers (4 / 8) against each scripted combat model (x1 / x2) and against 3 Gnats.</summary>
        public static List<Scenario> Kamikaze()
        {
            var l = new List<Scenario>();
            foreach (var m in KamikazeTargets)
            {
                l.Add(Build("kamikaze", new[] { m }, Rep("Stinger", 4)));
                l.Add(Build("kamikaze", new[] { m }, Rep("Stinger", 8)));
                l.Add(Build("kamikaze", Rep(m, 2), Rep("Stinger", 8)));
            }
            l.Add(Build("kamikaze", Rep("Gnat", 3), Rep("Stinger", 8)));
            foreach (var sc in l) sc.SwarmCueSeconds = 0.5;
            return l;
        }

        public static List<Scenario> All()
        {
            var l = Matrix1v1();
            l.AddRange(Unfair());
            l.AddRange(Mixed());
            l.AddRange(Swarm());
            l.AddRange(Pirate());
            l.AddRange(Kamikaze());
            l.AddRange(EnemyScenarios());
            return l;
        }

        /// <summary>Scenarios whose Name matches the regex (null/empty = all), optionally restricted to one Kind.</summary>
        public static List<Scenario> Select(string filterRegex, string kind)
        {
            var all = All();
            Regex rx = string.IsNullOrEmpty(filterRegex) ? null : new Regex(filterRegex, RegexOptions.CultureInvariant);
            var l = new List<Scenario>();
            for (int i = 0; i < all.Count; i++)
            {
                if (rx != null && !rx.IsMatch(all[i].Name)) continue;
                if (!string.IsNullOrEmpty(kind) && all[i].Kind != kind) continue;
                l.Add(all[i]);
            }
            return l;
        }
    }
}
