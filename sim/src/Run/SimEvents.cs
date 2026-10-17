// Harness events: a scenario command whose text starts with '!' is not a PB argument but something done TO the ship at that time.
//   !kill-thrust <F|B|L|R|U|D> [N]   remove N (default all) thrusters that push the ship along that controller direction (Forward, Backward, Left, Right, Up, Down)
//   !kill-block <name part> [N]      destroy N (default all) blocks of the ship whose custom name or SubtypeId contains the text (case-insensitive)
//   !damage <fraction>               remove that fraction (0..1) of the ship's total integrity from random blocks (deterministic per seed and ship)
//   !disable-pb <tag>                switch off every PB of the ship whose name contains the text (Enabled=false; its overrides stay in the thrusters and gyros)
//   !enable-pb <tag>                 switch them on again
//   !msl <distance> [Tag]            spawn a rocket <distance> m from the ship (from the direction of the nearest enemy, else random), aimed at the ship
//                                    with lead, and broadcast "<Tag>_MSL" tracks (MyTuple<long id, Vector3D pos, Vector3D vel, long sender 0>, 5 Hz while
//                                    it flies) to the PBs of the ship's team. Tag defaults to the model's first word upper-case ("Hornet" -> HORNET).
//                                    The rocket is owned by the nearest enemy ship (damage counts as that ship's), SmallMissileLauncher ammo.
// Used by `run/one --events "T:!kill-thrust F;T2:!kill-thrust R 1"` and `follow-test --events ...` to test the THRUST VECTORING block (docs/thrust-vectoring.md).
using System;
using System.Collections.Generic;
using System.Globalization;
using SeSim.Core;
using SeSim.Api;
using System.Runtime.CompilerServices;
using VRageMath;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace SeSim.Run
{
    public static class SimEvents
    {
        /// <summary>Parses "T:cmd;T2:cmd2" into (time, command) pairs.</summary>
        public static List<KeyValuePair<double, string>> Parse(string s)
        {
            var l = new List<KeyValuePair<double, string>>();
            if (string.IsNullOrEmpty(s)) return l;
            foreach (string c0 in s.Split(';'))
            {
                string c = c0.Trim(); if (c.Length == 0) continue;
                int colon = c.IndexOf(':'); double t;
                if (colon <= 0 || !double.TryParse(c.Substring(0, colon), NumberStyles.Float, CultureInfo.InvariantCulture, out t)) throw new ArgumentException("event needs 'seconds:!command': " + c);
                l.Add(new KeyValuePair<double, string>(t, c.Substring(colon + 1).Trim()));
            }
            return l;
        }

        /// <summary>Runs a '!' command on a ship of a running world (all events; needs the world for blocks, PBs, rockets and IGC).</summary>
        public static string Run(World w, ShipInstance ship, string cmd)
        {
            string[] p = cmd.TrimStart('!').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) throw new ArgumentException("empty event");
            var inv = CultureInfo.InvariantCulture;
            switch (p[0])
            {
                case "kill-thrust": if (ship.Main == null) return "kill-thrust: no ship"; return Run(ship.Main, cmd);
                case "kill-block":
                    if (p.Length < 2) throw new ArgumentException("!kill-block <name part> [N]");
                    return "kill-block " + p[1] + ": " + KillBlock(w, ship, p[1], p.Length > 2 ? int.Parse(p[2], inv) : int.MaxValue) + " destroyed";
                case "damage":
                    if (p.Length < 2) throw new ArgumentException("!damage <fraction>");
                    return "damage: " + Damage(w, ship, double.Parse(p[1], inv)).ToString("F0", inv) + " integrity removed";
                case "disable-pb": case "enable-pb":
                    if (p.Length < 2) throw new ArgumentException("!" + p[0] + " <tag>");
                    return p[0] + " " + p[1] + ": " + SetPb(ship, p[1], p[0] == "enable-pb") + " PBs";
                case "msl":
                    if (p.Length < 2) throw new ArgumentException("!msl <distance> [Tag]");
                    return "msl: " + SpawnMissile(w, ship, double.Parse(p[1], inv), p.Length > 2 ? p[2] : null);
            }
            throw new ArgumentException("unknown event: " + cmd);
        }

        static List<BlockInst> ShipBlocks(World w, ShipInstance ship)
        {
            var l = new List<BlockInst>();
            for (int i = 0; i < w.Bodies.Count; i++) { var b = w.Bodies[i]; if (b.Alive && b.ShipIndex == ship.Index) l.AddRange(b.Blocks); }
            return l;
        }

        public static int KillBlock(World w, ShipInstance ship, string part, int n)
        {
            var hit = new List<BlockInst>();
            foreach (var b in ShipBlocks(w, ship))
            {
                if (hit.Count >= n) break;
                string nm = b.Api != null ? b.Api.CustomNameValue : "";
                string st = b.Def != null ? (string.IsNullOrEmpty(b.Def.Subtype) ? b.Def.Key : b.Def.Subtype) : "";
                if ((nm != null && nm.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) || (st != null && st.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)) hit.Add(b);
            }
            foreach (var b in hit) b.Body.RemoveBlock(b, -1);
            return hit.Count;
        }

        public static double Damage(World w, ShipInstance ship, double frac)
        {
            var blocks = ShipBlocks(w, ship);
            double total = 0; foreach (var b in blocks) total += b.MaxHp;
            double want = total * Math.Max(0, Math.Min(1, frac)), done = 0;
            var rng = new DetRng(DetRng.Mix(w.Seed, 0xDA3A6E00UL + (ulong)(ship.Index + 1)));
            int guard = 0;
            while (done < want - 0.5 && blocks.Count > 0 && guard++ < 100000)
            {
                int k = (int)(rng.NextDouble() * blocks.Count); if (k >= blocks.Count) k = blocks.Count - 1;
                var b = blocks[k];
                if (b.Destroyed || b.Hp <= 0f) { blocks.RemoveAt(k); continue; }
                float amt = (float)Math.Min(want - done, b.Hp);
                done += Combat.Damage.ApplyCore(w, b, amt, -1, 0, Combat.DamageKind.Explosion, b.WorldCenter);
                if (b.Destroyed || b.Hp <= 0f) blocks.RemoveAt(k);
            }
            return done;
        }

        public static int SetPb(ShipInstance ship, string tag, bool on)
        {
            int n = 0;
            foreach (var r in ship.Pbs)
                if (r.Pb.CustomNameValue.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0) { r.Pb.Enabled = on; n++; }
            return n;
        }

        // ---- !msl -------------------------------------------------------------------------------------------------------
        sealed class MslTrack { public long ShotId; public string Tag; public int Team; public long Next; }
        sealed class State { public readonly List<MslTrack> Tracks = new List<MslTrack>(); }
        static readonly ConditionalWeakTable<World, State> _state = new ConditionalWeakTable<World, State>();

        public static string SpawnMissile(World w, ShipInstance ship, double dist, string tag)
        {
            Body me = ship.Main; if (me == null || w.Projectiles == null) return "no ship or projectile system";
            var ammo = w.Defs.Weapons["SmallMissileLauncher"].Ammo;
            Vector3D c = me.WorldBoundCenter;
            // shooter = nearest enemy ship (attribution and ownership), direction of approach = from it
            Body en = null; double best = double.MaxValue;
            for (int i = 0; i < w.Bodies.Count; i++)
            {
                var b = w.Bodies[i];
                if (!b.Alive || b.ShipIndex < 0 || b.ShipIndex == ship.Index || b.Team == ship.Team || b.Blocks.Count == 0) continue;
                double d2 = (b.WorldBoundCenter - c).LengthSquared(); if (d2 < best) { best = d2; en = b; }
            }
            Vector3D dirOut;
            if (en != null && best > 1) dirOut = Vector3D.Normalize(en.WorldBoundCenter - c);
            else
            {
                var rng = new DetRng(DetRng.Mix(w.Seed, 0x35110000UL + (ulong)w.Tick));
                double z = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(1 - z * z);
                dirOut = new Vector3D(r * Math.Cos(a), r * Math.Sin(a), z);
            }
            double speed = ammo.Speed > 0 ? ammo.Speed : 200;
            Vector3D aim = c;
            for (int it = 0; it < 3; it++) { double t = (c + dirOut * dist - aim).Length() / speed; aim = c + me.LinearVelocity * t; }
            Vector3D start = c + dirOut * dist;
            Vector3D head = Vector3D.Normalize(aim - start);
            double v0 = ammo.MissileInitialSpeed > 0 ? ammo.MissileInitialSpeed : speed;
            w.Projectiles.Spawn(start, head * v0, ammo, en, en != null ? en.ShipIndex : -1, head, default(Vector3D), null, dist * 1.5 + 200);
            long id = w.Projectiles.ShotId[w.Projectiles.Count - 1];
            string tg = tag;
            if (string.IsNullOrEmpty(tg)) { string m = ship.Spec.Model ?? "X"; int sp = m.IndexOf(' '); tg = (sp > 0 ? m.Substring(0, sp) : m).ToUpperInvariant(); }
            _state.GetOrCreateValue(w).Tracks.Add(new MslTrack { ShotId = id, Tag = tg + "_MSL", Team = ship.Team, Next = w.Tick });
            return "rocket " + id + " from " + dist.ToString("F0", CultureInfo.InvariantCulture) + " m, track " + tg + "_MSL";
        }

        /// <summary>Called once per tick (Metrics.EndTick): sends the _MSL tracks of live harness rockets, 5 Hz each.</summary>
        public static void TickHook(World w)
        {
            State st; if (!_state.TryGetValue(w, out st) || st.Tracks.Count == 0 || w.Igc == null) return;
            var pr = w.Projectiles;
            for (int k = st.Tracks.Count - 1; k >= 0; k--)
            {
                var t = st.Tracks[k];
                int pi = -1; for (int i = 0; i < pr.Count; i++) if (pr.ShotId[i] == t.ShotId) { pi = i; break; }
                if (pi < 0) { st.Tracks.RemoveAt(k); continue; }
                if (w.Tick < t.Next) continue;
                t.Next = w.Tick + 12;
                var tup = new VRage.MyTuple<long, Vector3D, Vector3D, long>(t.ShotId, pr.Pos[pi], pr.Vel[pi], 0);
                for (int e = 0; e < w.Igc.Endpoints.Count; e++)
                {
                    var ep = w.Igc.Endpoints[e];
                    if (ep.Pb.Inst.Destroyed || ep.Pb.Inst.Body == null || ep.Pb.Inst.Body.Team != t.Team) continue;
                    var l = ep.ListenerFor(t.Tag);
                    if (l != null) { l.Push(new MyIGCMessage(tup, t.Tag, 0)); w.Igc.Delivered++; }
                }
            }
        }

        /// <summary>Runs a '!' command on the ship whose main body is `main`. Returns a one-line description (thrusters removed).</summary>
        public static string Run(Body main, string cmd)
        {
            string[] p = cmd.TrimStart('!').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 2 && p[0] == "kill-thrust")
            {
                int n = p.Length > 2 ? int.Parse(p[2], CultureInfo.InvariantCulture) : int.MaxValue;
                return "kill-thrust " + p[1] + ": " + KillThrust(main, p[1][0], n) + " removed";
            }
            throw new ArgumentException("unknown event: " + cmd);
        }

        public static int KillThrust(Body main, char group, int n)
        {
            MatrixD cm = MatrixD.Identity;
            foreach (var b in main.Blocks) if (b.Api is SimShipController) { cm = b.LocalMatrix; break; }
            Vector3D dir;
            switch (char.ToUpperInvariant(group))
            {
                case 'F': dir = cm.Forward; break;
                case 'B': dir = cm.Backward; break;
                case 'L': dir = cm.Left; break;
                case 'R': dir = cm.Right; break;
                case 'U': dir = cm.Up; break;
                case 'D': dir = cm.Down; break;
                default: throw new ArgumentException("thrust group must be F, B, L, R, U or D");
            }
            var hit = new List<BlockInst>();
            foreach (var t in main.Thrusters) if (Vector3D.Dot(t.ForceDirLocal, dir) > 0.9 && hit.Count < n) hit.Add(t.Block);
            foreach (var b in hit) main.RemoveBlock(b, -1);
            return hit.Count;
        }
    }

    /// <summary>Writer audit (docs/design/survivability.md 1.3): counts ticks in which two DIFFERENT PBs set gyro (kind 0) or thruster (kind 1) overrides on the
    /// same grid in the same tick or in consecutive ticks. SimGyro / SimThrust setters call Note; the PB that is inside Main is the writer (writes outside
    /// any PB are ignored). Reporting only, changes nothing in the fight.</summary>
    public static class WriterAudit
    {
        public sealed class Rec { public PbRunner[] Pb = new PbRunner[2]; public long[] Tick = { -10, -10 }, Counted = { -10, -10 }; }
        public sealed class Data { public long Gyro, Thrust; public readonly Dictionary<Body, Rec> Recs = new Dictionary<Body, Rec>(); }
        static readonly ConditionalWeakTable<World, Data> _d = new ConditionalWeakTable<World, Data>();
        [ThreadStatic] static World _cw; [ThreadStatic] static Data _cd;
        [ThreadStatic] static PbRunner _last;
        [ThreadStatic] static Body _lb; [ThreadStatic] static Rec _lr;

        public static Data Of(World w)
        {
            if (!ReferenceEquals(_cw, w)) { _cd = _d.GetOrCreateValue(w); _cw = w; _lb = null; _lr = null; _last = null; }
            return _cd;
        }

        public static void Note(World w, Body body, int kind)
        {
            var host = w.Scripts as ScriptHost; if (host == null || body == null) return;
            PbRunner pb = _last;
            if (pb == null || !pb.Pb.InMain || !ReferenceEquals(_cw, w))
            {
                pb = null;
                var rs = host.Runners;
                for (int i = 0; i < rs.Count; i++) if (rs[i].Pb.InMain) { pb = rs[i]; break; }
                if (pb == null) return;
            }
            var d = Of(w); _last = pb;
            Rec r;
            if (ReferenceEquals(_lb, body)) r = _lr;
            else { if (!d.Recs.TryGetValue(body, out r)) { r = new Rec(); d.Recs[body] = r; } _lb = body; _lr = r; }
            long t = w.Tick;
            if (r.Pb[kind] != null && !ReferenceEquals(r.Pb[kind], pb) && t - r.Tick[kind] <= 1 && r.Counted[kind] != t)
            {
                r.Counted[kind] = t;
                if (kind == 0) d.Gyro++; else d.Thrust++;
            }
            r.Pb[kind] = pb; r.Tick[kind] = t;
        }
    }
}