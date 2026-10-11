// SLOT D. Teams. Every team is one faction with one identity (all its ships share the owner => Relation "Owner" among them).
// Allies (FactionShare) via AlliedWith; everyone else Enemies. The pirate team is Enemies to all.
using System;
using System.Collections.Generic;

namespace SeSim.Run
{
    public sealed class TeamSpec
    {
        public int Id; public string Name;
        public readonly List<int> AlliedWith = new List<int>();
        public TeamSpec(int id, string name) { Id = id; Name = name; }
    }
}
