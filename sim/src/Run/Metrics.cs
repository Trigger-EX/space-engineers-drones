// SLOT D. Event sink + per-run results + termination (docs/sim-design.md section 9).
// Events (SimEvent.ShipA/ShipB are ShipInstance indices) are aggregated per ship. Every SampleTicks (10) the metrics module
// samples the world: combat capability per ship, time on target, distances, speed, stuck/circling. Termination is a small pure
// state machine (Termination) so it can be unit-tested without a World.
using System;
using System.Collections.Generic;
using SeSim.Core;
using SeSim.Combat;
using VRageMath;

namespace SeSim.Run
{
    public sealed class ShipStats
    {
        public int Ship; public string Model; public int Team;
        public long HitEvents;            // raw Hit events on enemies (a piercing slug / splash counts per tick and body); Hits = unique projectiles when SimSettings.HitCountUnique
        public double CollisionDamageTaken;
        public long Shots, Hits, HitsTaken, FriendlyHitsDealt, BlocksLost, BlocksKilled, Collisions, ScriptExceptions, Detonations, Splits;
        public double DamageDealt, DamageTaken, FriendlyDamageDealt, TimeOnTargetS, MinDistanceToEnemy = double.MaxValue;
        public double MassLostFraction, TimeAliveS, DisabledAtS = -1, DestroyedAtS = -1, AvgSpeed, MaxSpeed;
        public double ScriptCpuMs, ScriptMaxTickMs; public long ScriptRuns;
        /// <summary>Seconds the PB status screen showed a script target ("Target : ..." not "none"), first such time (-1 never),
        /// and seconds the ship's AI Offensive block was enabled (v1.8 hybrid search: the vanilla AI flies the ship).</summary>
        public double ScriptTargetS, FirstScriptTargetS = -1, AiOnS;
        public bool StuckOrCircling;      // speed < 1 m/s for 30 s with an enemy alive, or no engagement within 1.5x weapon range for 60 s
        public string StuckKind = "";     // "slow" | "noengage" | "" (first one that fired)
        public string FirstException;
        // end-of-run state
        public bool Alive, Capable, EverCapable;
        public int BlocksStart, BlocksEnd;
        public string Script = "";
        public double BlockMassLostKg, InitialMassKg;
        public double WeaponRange;
    }

    public sealed class RunResult
    {
        public string Scenario, Kind; public int SeedIndex; public ulong Seed;
        public int WinnerTeam = -1;       // -1 draw / timeout
        public string Outcome;            // "win", "draw", "timeout", "mutual-kill", "no-contest", "error"
        public double DurationS, WallMs, CpuMs;
        public double TimeToKillS = -1;   // when the losing side(s) were last beaten (win), else -1
        public string SettingsHash = "";
        public readonly List<ShipStats> Ships = new List<ShipStats>();
        public string Error;              // engine exception (run counted as "error", never silently dropped)
    }

    /// <summary>Pure termination rule: a side is beaten while none of its ships is combat capable. A run ends when at most one
    /// side is still capable and every beaten side has been beaten for HoldSeconds without interruption.</summary>
    public sealed class Termination
    {
        readonly int _teams; readonly double _hold;
        readonly double[] _beatenSince; readonly bool[] _ever;
        public int Winner = -1; public string Outcome; public double DecisiveAt = -1;
        public Termination(int teams, double holdSeconds)
        {
            _teams = teams; _hold = holdSeconds;
            _beatenSince = new double[teams]; _ever = new bool[teams];
            for (int i = 0; i < teams; i++) _beatenSince[i] = -1;
        }

        /// <summary>Feeds one sample. Returns true when the run is decided (Outcome, Winner, DecisiveAt are then valid).</summary>
        public bool Update(bool[] teamCapable, double now)
        {
            int alive = 0, last = -1;
            for (int t = 0; t < _teams; t++)
            {
                if (teamCapable[t]) { alive++; last = t; _ever[t] = true; _beatenSince[t] = -1; }
                else if (_beatenSince[t] < 0) _beatenSince[t] = now;
            }
            if (alive > 1) return false;
            double latest = -1;
            for (int t = 0; t < _teams; t++)
            {
                if (teamCapable[t]) continue;
                if (now - _beatenSince[t] + 1e-9 < _hold) return false;
                if (_ever[t] && _beatenSince[t] > latest) latest = _beatenSince[t];   // a side that never could fight (unarmed Watcher) does not define the kill time
            }
            DecisiveAt = latest;
            if (alive == 1) { Winner = last; Outcome = "win"; return true; }
            bool anyEver = false;
            for (int t = 0; t < _teams; t++) if (_ever[t]) anyEver = true;
            Winner = -1; Outcome = anyEver ? "mutual-kill" : "no-contest";
            return true;
        }
    }

    public sealed class Metrics : IEventSink
    {
        public const int SampleTicks = 10;
        public readonly RunResult Result = new RunResult();
        public readonly List<ShipInstance> Ships;
        public bool Finished;
        public World W;
        public Scenario Sc;
        public ScriptHost Host;
        public Termination Term;
        ShipStats[] _st;
        readonly HashSet<long> _hitShots = new HashSet<long>();    // projectile ids already counted as a hit (membership only, never iterated)
        int _n, _teams;
        bool[] _teamCap, _teamEverArmed, _termFlags;
        // friendly-fire dedupe: tick of the last Hit event per ordered pair
        long[] _lastHit;
        readonly List<SimEvent> _ffPending = new List<SimEvent>();
        // per-ship sampling state
        double[] _slowT, _lastEngage, _engageRefD, _speedSum; long[] _speedN; BlockInst[][] _aiBlocks;
        readonly List<Body>[] _bodies;
        readonly List<Body> _scratch = new List<Body>();

        public Metrics(List<ShipInstance> ships)
        {
            Ships = ships; _n = ships.Count;
            _st = new ShipStats[_n];
            _lastHit = new long[_n * _n];
            _slowT = new double[_n]; _lastEngage = new double[_n]; _engageRefD = new double[_n]; _aiBlocks = new BlockInst[_n][]; _speedSum = new double[_n]; _speedN = new long[_n];
            _bodies = new List<Body>[_n];
            for (int i = 0; i < _n; i++)
            {
                _lastHit[i] = -1;
                var s = new ShipStats { Ship = i, Model = ships[i].Spec.Model, Team = ships[i].Team };
                _st[i] = s; Result.Ships.Add(s);
                _bodies[i] = new List<Body>(4);
            }
            for (int i = 0; i < _lastHit.Length; i++) _lastHit[i] = -1;
        }

        /// <summary>Called by the runner after the ships are spawned and their PBs attached.</summary>
        public void Setup(World w, Scenario sc, ScriptHost host)
        {
            W = w; Sc = sc; Host = host;
            _teams = Math.Max(2, sc.Teams.Count);
            _teamCap = new bool[_teams]; _teamEverArmed = new bool[_teams]; _termFlags = new bool[_teams];
            Term = new Termination(_teams, sc.DecisiveHoldSeconds);
            for (int i = 0; i < _n; i++)
            {
                var sh = Ships[i]; var st = _st[i];
                st.BlocksStart = sh.InitialBlocks; st.InitialMassKg = sh.InitialMass; st.WeaponRange = sh.WeaponRange;
                var sb = new System.Text.StringBuilder();
                var seen = new List<string>();
                for (int k = 0; k < sh.Pbs.Count; k++) { string nm = sh.Pbs[k].ScriptName ?? "?"; if (!seen.Contains(nm)) seen.Add(nm); }
                for (int k = 0; k < seen.Count; k++) { if (k > 0) sb.Append('+'); sb.Append(seen[k]); }
                st.Script = sb.Length > 0 ? sb.ToString() : (sh.HasBrain ? "(brain)" : "(none)");
                _lastEngage[i] = 0; _engageRefD[i] = double.MaxValue;
                var ai = new List<BlockInst>();
                for (int k = 0; k < sh.Bodies.Count; k++)
                    foreach (var b in sh.Bodies[k].Blocks)
                    {
                        var g = b.Api as SeSim.Api.SimGenericFunctional;
                        if (g != null && g.AiKind == "OffensiveCombat") ai.Add(b);
                    }
                _aiBlocks[i] = ai.ToArray();
            }
            // initial capability so the first sample's EverCapable is right even if the run is only a few ticks long
            Sample(w);
        }

        bool Valid(int i) { return (uint)i < (uint)_n; }

        bool Friendly(int a, int b)
        {
            if (a == b) return true;
            int ta = Ships[a].Team, tb = Ships[b].Team;
            if (ta == tb) return true;
            var rel = W != null && ta < W.Teams.Count && tb < W.Teams.Count ? W.Teams.Rel[ta, tb] : VRage.Game.MyRelationsBetweenPlayerAndBlock.Enemies;
            return rel == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare || rel == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner
                || rel == VRage.Game.MyRelationsBetweenPlayerAndBlock.Friends;
        }

        public void On(ref SimEvent e)
        {
            int a = e.ShipA, b = e.ShipB;
            switch (e.Kind)
            {
                case EventKind.Shot:
                    if (Valid(a)) _st[a].Shots++;
                    break;
                case EventKind.Hit:
                    if (Valid(b))
                    {
                        _st[b].DamageTaken += e.Value; _st[b].HitsTaken++;
                        if (Valid(a))
                        {
                            _lastHit[a * _n + b] = e.Tick;
                            if (Friendly(a, b)) { _st[a].FriendlyHitsDealt++; _st[a].FriendlyDamageDealt += e.Value; }
                            else
                            {
                                _st[a].HitEvents++; _st[a].DamageDealt += e.Value;
                                if (e.Id == 0 || W == null || !W.Settings.HitCountUnique || _hitShots.Add(e.Id)) _st[a].Hits++;
                            }
                        }
                    }
                    else if (Valid(a)) { /* hit on debris / unowned: not counted */ }
                    break;
                case EventKind.FriendlyFire:
                    _ffPending.Add(e);
                    break;
                case EventKind.BlockDestroyed:
                    if (Valid(b)) { _st[b].BlocksLost++; _st[b].BlockMassLostKg += e.Value; }
                    if (Valid(a) && Valid(b) && !Friendly(a, b)) _st[a].BlocksKilled++;
                    break;
                case EventKind.Collision:
                    if (Valid(a)) _st[a].Collisions++;
                    if (Valid(b) && b != a) _st[b].Collisions++;
                    break;
                case EventKind.CollisionDamage:
                    if (Valid(b)) { _st[b].DamageTaken += e.Value; _st[b].CollisionDamageTaken += e.Value; }
                    break;
                case EventKind.Detonation:
                    if (Valid(a)) _st[a].Detonations++;
                    break;
                case EventKind.GridSplit:
                    if (Valid(a)) _st[a].Splits++;
                    break;
                case EventKind.ScriptException:
                    if (Valid(a)) { _st[a].ScriptExceptions++; if (_st[a].FirstException == null) _st[a].FirstException = e.Text; }
                    break;
            }
        }

        public void FlushFriendlyFire() { ResolveFriendlyFire(); }

        void ResolveFriendlyFire()
        {
            for (int i = 0; i < _ffPending.Count; i++)
            {
                var e = _ffPending[i];
                int a = e.ShipA, b = e.ShipB;
                if (!Valid(a)) continue;
                bool alreadyHit = Valid(b) && _lastHit[a * _n + b] == e.Tick;
                if (alreadyHit) continue;       // the Hit event of the same damage was counted (as friendly) already
                if (a == b) { _st[b].DamageTaken += e.Value; continue; }   // own blast (warhead on its carrier): damage taken, not friendly fire
                _st[a].FriendlyHitsDealt++; _st[a].FriendlyDamageDealt += e.Value;
                if (Valid(b)) { _st[b].DamageTaken += e.Value; _st[b].HitsTaken++; }
            }
            _ffPending.Clear();
        }

        /// <summary>Every tick: cheap. Every 10 ticks: CombatCapable per ship, time on target (gun axis within 2 deg of an enemy
        /// body's bounding sphere), distances. Decisive = all ships of all but one team not CombatCapable for
        /// DecisiveHoldSeconds -> Finished.</summary>
        public void EndTick(World w)
        {
            if (Finished) return;
            if (_ffPending.Count > 0) ResolveFriendlyFire();
            if (w.Tick % SampleTicks != 0) return;
            Sample(w);
            if (Term.Update(TerminationFlags(), w.Time))
            {
                Finished = true;
                Result.Outcome = Term.Outcome; Result.WinnerTeam = Term.Winner;
                Result.TimeToKillS = Term.Outcome == "win" ? Term.DecisiveAt : -1;
            }
        }

        /// <summary>Per-team "still in the fight" flags fed to Termination. A team with an armed, working ship is in the fight. A team that
        /// has NEVER had an armed working ship (unarmed Watcher spotter) is no loser merely for being unarmed: while another team has
        /// been armed, it counts as in the fight until none of its ships can fly/control any more (power + thruster + PB + controller)
        /// or all are destroyed. Without this an armed side "won" 5 s after the start against a lone Watcher. When no team was ever
        /// armed the flags stay as they are (Termination then reports no-contest).</summary>
        bool[] TerminationFlags()
        {
            for (int t = 0; t < _teams; t++) { _teamEverArmed[t] = _teamEverArmed[t] || _teamCap[t]; _termFlags[t] = _teamCap[t]; }
            bool anyArmed = false;
            for (int t = 0; t < _teams; t++) if (_teamEverArmed[t]) anyArmed = true;
            if (!anyArmed) return _termFlags;
            for (int i = 0; i < _n; i++)
            {
                int t = Ships[i].Team;
                if (_termFlags[t] || _teamEverArmed[t]) continue;
                var sh = Ships[i]; var list = _bodies[i];
                for (int k = 0; k < list.Count; k++)
                    if (BodyCapable(W, list[k], !sh.HasBrain, sh, false)) { _termFlags[t] = true; break; }
            }
            return _termFlags;
        }

        // -------------------------------------------------------------------------------------------------
        void CollectBodies(World w)
        {
            for (int i = 0; i < _n; i++) _bodies[i].Clear();
            var bs = w.Bodies;
            for (int k = 0; k < bs.Count; k++)
            {
                var b = bs[k];
                int si = b.ShipIndex;
                if (b.Alive && b.Blocks.Count > 0 && (uint)si < (uint)_n) _bodies[si].Add(b);
            }
        }

        bool BodyCapable(World w, Body b, bool needControl, ShipInstance sh, bool needWeapon = true)
        {
            // stored power
            bool power = false;
            var bat = b.Batteries;
            for (int i = 0; i < bat.Count; i++) if (bat[i].CanDischarge) { power = true; break; }
            if (!power) return false;
            bool thr = false;
            var th = b.Thrusters;
            for (int i = 0; i < th.Count; i++) if (th[i].Working) { thr = true; break; }
            if (!thr) return false;
            if (needControl)
            {
                bool pbOk = false;
                for (int i = 0; i < sh.Pbs.Count; i++)
                {
                    var r = sh.Pbs[i];
                    if (!r.Dead && r.Pb.Inst.Body == b && r.Pb.Inst.IsFunctional && !r.Pb.Inst.Destroyed) { pbOk = true; break; }
                }
                if (!pbOk) return false;
                bool rc = false;
                for (int i = 0; i < sh.Controllers.Count; i++)
                {
                    var c = sh.Controllers[i];
                    if (!c.Destroyed && c.Body == b && c.IsFunctional) { rc = true; break; }
                }
                if (!rc) return false;
            }
            if (!needWeapon) return true;     // flight/control-capable only (used for never-armed target ships)
            // weapon or warhead
            var wp = b.Weapons;
            bool inf = w.Settings.InfiniteAmmo;
            for (int i = 0; i < wp.Count; i++)
            {
                var u = wp[i];
                if (u.Block.Body == b && u.Block.IsFunctional && (inf || u.RoundsLeftInMag > 0 || u.MagazinesLeft > 0)) return true;
            }
            if (w.Combat != null)
            {
                var tu = w.Combat.Turrets;
                for (int i = 0; i < tu.Count; i++)
                {
                    var t = tu[i];
                    if (!t.IsController && t.Block.Body == b && t.Block.IsFunctional) return true;
                }
                var wh = w.Combat.Warheads;
                for (int i = 0; i < wh.Count; i++)
                {
                    var u = wh[i];
                    if (!u.Exploded && u.Block.Body == b && u.Block.IsFunctional && !u.Block.Destroyed) return true;
                }
            }
            return false;
        }

        void Sample(World w)
        {
            double now = w.Time;
            CollectBodies(w);
            for (int t = 0; t < _teams; t++) _teamCap[t] = false;
            // 1. per ship state
            for (int i = 0; i < _n; i++)
            {
                var sh = Ships[i]; var st = _st[i]; var list = _bodies[i];
                double mass = 0; int blocks = 0; Body main = null;
                for (int k = 0; k < list.Count; k++)
                {
                    var b = list[k];
                    mass += b.Mass; blocks += b.Blocks.Count;
                    if (main == null || b.Mass > main.Mass) main = b;
                }
                sh.Main = main;
                sh.Bodies.Clear(); sh.Bodies.AddRange(list);
                bool cap = false;
                for (int k = 0; k < list.Count && !cap; k++) cap = BodyCapable(w, list[k], !sh.HasBrain, sh);
                sh.CombatCapable = cap;
                st.Capable = cap; st.BlocksEnd = blocks; st.Alive = blocks > 0;
                if (cap) { st.EverCapable = true; _teamCap[sh.Team] = true; }
                else if (st.EverCapable && !sh.Disabled) { sh.Disabled = true; sh.DisabledAt = now; st.DisabledAtS = now; }
                if (!sh.Destroyed && (blocks == 0 || (sh.InitialMass > 0 && mass < 0.1 * sh.InitialMass))) { sh.Destroyed = true; sh.DestroyedAt = now; st.DestroyedAtS = now; }
                st.MassLostFraction = sh.InitialMass > 0 ? Math.Max(0, 1.0 - mass / sh.InitialMass) : 0;
                st.TimeAliveS = sh.Destroyed ? sh.DestroyedAt : now;
                if (main != null)
                {
                    double sp = main.LinearVelocity.Length();
                    _speedSum[i] += sp; _speedN[i]++;
                    if (sp > st.MaxSpeed) st.MaxSpeed = sp;
                    st.AvgSpeed = _speedSum[i] / _speedN[i];
                }
            }
            // 2. relative stuff (needs every ship's state)
            const double dtS = SampleTicks * SimSettings.Dt;
            for (int i = 0; i < _n; i++)
            {
                var sh = Ships[i]; var st = _st[i];
                if (sh.Main == null) continue;
                Vector3D p = sh.Main.Position;
                double minD = double.MaxValue; bool enemyCapable = false;
                for (int j = 0; j < _n; j++)
                {
                    if (j == i) continue;
                    var oj = Ships[j];
                    if (oj.Main == null || Friendly(i, j)) continue;
                    double d = (oj.Main.Position - p).Length();
                    if (d < minD) minD = d;
                    if (oj.CombatCapable) enemyCapable = true;
                }
                if (minD < st.MinDistanceToEnemy) st.MinDistanceToEnemy = minD;
                // time on target: any functional fixed weapon whose axis is within 2 deg (of the target sphere) of an enemy body
                if (sh.CombatCapable && W.Settings != null) st.TimeOnTargetS += OnTarget(sh, i) ? dtS : 0;
                if (sh.CombatCapable)
                {
                    if (ScriptHasTarget(sh)) { st.ScriptTargetS += dtS; if (st.FirstScriptTargetS < 0) st.FirstScriptTargetS = now; }
                    var ab = _aiBlocks[i];
                    if (ab != null)
                        for (int k = 0; k < ab.Length; k++)
                        {
                            var f = ab[k].Api as SeSim.Api.SimFunctionalBlock;
                            if (!ab[k].Destroyed && f != null && f.EnabledValue) { st.AiOnS += dtS; break; }
                        }
                }
                // stuck / circling
                if (sh.CombatCapable && enemyCapable)
                {
                    double sp = sh.Main.LinearVelocity.Length();
                    if (sp < 1.0) _slowT[i] += dtS; else _slowT[i] = 0;
                    double reach = 1.5 * Math.Max(sh.WeaponRange, 100);
                    // engaging = inside 1.5x weapon range, or still closing in (50 m nearer than at the last reset)
                    if (minD <= reach || minD < _engageRefD[i] - 50) { _lastEngage[i] = now; _engageRefD[i] = minD; }
                    if (!st.StuckOrCircling)
                    {
                        if (_slowT[i] >= 30) { st.StuckOrCircling = true; st.StuckKind = "slow"; }
                        else if (now - _lastEngage[i] >= 60) { st.StuckOrCircling = true; st.StuckKind = "noengage"; }
                    }
                }
                else { _slowT[i] = 0; _lastEngage[i] = now; _engageRefD[i] = double.MaxValue; }
            }
        }

        static bool ScriptHasTarget(ShipInstance sh)
        {
            for (int k = 0; k < sh.Pbs.Count; k++)
            {
                var pr = sh.Pbs[k];
                if (pr.Dead) continue;
                string e = pr.EchoText;
                if (e == null) continue;
                int ti = e.IndexOf("Target :", StringComparison.Ordinal);
                if (ti < 0) ti = e.IndexOf("Tgt  :", StringComparison.Ordinal);      // Gnat status screen
                if (ti < 0) continue;
                int end = e.IndexOf('\n', ti); if (end < 0) end = e.Length;
                if (e.IndexOf("none", ti, end - ti, StringComparison.Ordinal) < 0) return true;
            }
            return false;
        }

        const double OnTargetDeg = 2.0;

        bool OnTarget(ShipInstance sh, int idx)
        {
            double cosTol = Math.Cos(OnTargetDeg * Math.PI / 180.0);
            var list = sh.Bodies;
            for (int k = 0; k < list.Count; k++)
            {
                var b = list[k];
                var wp = b.Weapons;
                for (int wi = 0; wi < wp.Count; wi++)
                {
                    var u = wp[wi];
                    if (u.Block.Body != b || !u.Block.IsFunctional) continue;
                    Vector3D o = b.LocalToWorld(u.MuzzleLocal);
                    Vector3D dir = b.DirToWorld(u.DirLocal);
                    double wrange = u.Ammo != null && u.Ammo.MaxTrajectory > 0 ? u.Ammo.MaxTrajectory : double.MaxValue;   // out of range is not on target
                    for (int j = 0; j < _n; j++)
                    {
                        if (j == idx || Friendly(idx, j)) continue;
                        var ob = Ships[j].Bodies;
                        for (int q = 0; q < ob.Count; q++)
                        {
                            var tb = ob[q];
                            Vector3D to = tb.WorldBoundCenter - o;
                            double d = to.Length();
                            if (d < 1e-6) return true;
                            if (d - tb.BoundRadius > wrange) continue;
                            double r = Math.Max(tb.BoundRadius, 0.5);
                            double angR = r >= d ? Math.PI : Math.Asin(r / d);
                            double ang = Math.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(dir, to) / d)));
                            if (ang - angR <= OnTargetDeg * Math.PI / 180.0) return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>Completes the result at the end of the run. timedOut: the cap was reached without a decision.</summary>
        public void Finish(World w, bool timedOut, string error)
        {
            if (_ffPending.Count > 0) ResolveFriendlyFire();
            double now = w.Time;
            try { Sample(w); } catch (Exception) { }
            Result.DurationS = now;
            if (error != null) { Result.Outcome = "error"; Result.Error = error; }
            else if (!Finished)
            {
                Result.Outcome = "timeout"; Result.WinnerTeam = -1;
            }
            // script stats
            if (Host != null)
            {
                for (int i = 0; i < Host.Runners.Count; i++)
                {
                    var r = Host.Runners[i];
                    int si = r.ShipIndex;
                    if (!Valid(si)) continue;
                    var st = _st[si];
                    st.ScriptRuns += r.Runs; st.ScriptCpuMs += r.CpuMs;
                    if (r.MaxCpuMs > st.ScriptMaxTickMs) st.ScriptMaxTickMs = r.MaxCpuMs;
                    if (r.Dead && r.DeathReason != null && st.FirstException == null && r.DeathReason != "detached" && r.DeathReason != "block destroyed")
                        st.FirstException = r.DeathReason;
                }
            }
            for (int i = 0; i < _n; i++)
                if (_st[i].TimeAliveS <= 0 || !Ships[i].Destroyed) _st[i].TimeAliveS = now;
            Finished = true;
        }
    }
}
