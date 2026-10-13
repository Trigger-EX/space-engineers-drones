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
        static List<string> _enemies;

        /// <summary>Blueprint folders (containing bp.sbc) in EnemyDir, sorted; empty when EnemyDir is unset.</summary>
        public static List<string> Enemies()
        {
            if (_enemies != null) return _enemies;
            var l = new List<string>();
            if (!string.IsNullOrEmpty(EnemyDir))
            {
                if (!System.IO.Directory.Exists(EnemyDir)) throw new System.IO.DirectoryNotFoundException("--enemies folder not found: " + EnemyDir);
                foreach (string d in System.IO.Directory.GetDirectories(EnemyDir))
                    if (System.IO.File.Exists(System.IO.Path.Combine(d, "bp.sbc"))) l.Add(System.IO.Path.GetFileName(d));
                l.Sort(StringComparer.Ordinal);
                if (l.Count == 0) throw new System.IO.FileNotFoundException("--enemies " + EnemyDir + ": no <Name>/bp.sbc blueprint folders inside");
            }
            _enemies = l;
            return l;
        }

        public static bool IsEnemy(string model) { return EnemyDir != null && Enemies().Contains(model); }

        static readonly object EnemyLock = new object();
        static readonly Dictionary<string, SeSim.Blueprint.BlueprintData> EnemyBps = new Dictionary<string, SeSim.Blueprint.BlueprintData>();

        /// <summary>The enemy blueprint reduced to its largest grid (cached, immutable).</summary>
        public static SeSim.Blueprint.BlueprintData EnemyBlueprint(string name)
        {
            lock (EnemyLock)
            {
                SeSim.Blueprint.BlueprintData bp;
                if (EnemyBps.TryGetValue(name, out bp)) return bp;
                var full = SeSim.Blueprint.BlueprintLoader.Get(EnemyDir, name);
                var main = full.Grids[0];
                for (int i = 1; i < full.Grids.Count; i++) if (full.Grids[i].Blocks.Count > main.Blocks.Count) main = full.Grids[i];
                bp = new SeSim.Blueprint.BlueprintData { Name = name, Path = full.Path };
                bp.Grids.Add(main);
                EnemyBps[name] = bp;
                return bp;
            }
        }

        public static List<Scenario> EnemyScenarios()
        {
            var l = new List<Scenario>();
            foreach (string e in Enemies())
            {
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
                    if (IsEnemy(s.Model)) break;     // enemy blueprint: brain + turrets, no scripts started
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

        public static List<Scenario> All()
        {
            var l = Matrix1v1();
            l.AddRange(Unfair());
            l.AddRange(Mixed());
            l.AddRange(Swarm());
            l.AddRange(Pirate());
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
