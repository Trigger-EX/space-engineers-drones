// M0 (docs/design/control_arch.md s6 S5): a scenario-driven pilot. The sim cannot fly a cockpit by hand, so a timeline plays the
// part of the human: it sits in the ship's cockpit (IsUnderControl), pushes MoveIndicator / RotationIndicator / RollIndicator, toggles
// the dampeners, fires, presses Button Panel buttons and runs PB commands the way a cockpit toolbar would. It runs as a brain (phase 3
// of World.Step, before the physics), so the indicators it writes are what the thrusters and gyros see the same tick.
//
// Script syntax (ShipSpec.Pilot, CLI --pilot): "<seconds>:<verb> [args];..." e.g.  "0:sit;5:move 0 0 -1;8:rot 0 1;20:leave"
//   sit | leave                      take / leave the cockpit (leave also clears all input)
//   seat NAME                        the seat for the following steps is the cockpit whose name contains NAME (M6: 'seat gunnery'; no NAME = the first cockpit)
//   move X Y Z                       MoveIndicator in cockpit axes (x right+, y up+, z backward+; W = 0 0 -1), clamped to +-1 by Physics
//   rot PITCH YAW | roll R | stop    RotationIndicator (pitch+ = nose down, yaw+ = turn right), RollIndicator; stop clears move/rot/roll/modes
//   damp on|off                      DampenersOverride (the V key)
//   fly-to X Y Z [SPEED]             closed loop: P controller on the move indicators toward a WORLD point (default speed 30 m/s)
//   hold                             closed loop: cancel the velocity with the move indicators (dampeners untouched)
//   face-target | face-target off    closed loop: rotation indicators turn the cockpit's forward toward the nearest enemy ship
//   fire on|off                      hand-fire every fixed gun (toolbar weapon slot)
//   fire-aligned DEG                 closed loop: guns fire while the target is inside DEG degrees of the cockpit's forward, else cease fire
//   press SLOT                       run slot SLOT (0-based) of the cockpit's own toolbar
//   button NAME SLOT                 press button SLOT of the Button Panel whose name starts with NAME
//   run TAG ARG...                   PB Run with an argument on the PBs tagged [TAG] (what a toolbar Run slot with a parameter would do)
//   killpb TAG                       the PBs tagged [TAG] stop running for good (their overrides stay in the blocks): the dead-man scenario
//   disablepb TAG | enablepb TAG     switch the PB blocks off / on
// Verbs that need a cockpit are skipped (and counted in Skipped) on a ship without one. Statistics for the tests: OverrideFightTicks (pilot
// input non-zero while any gyro/thruster override is on and the pilot is seated, M0 acceptance "no override fight"), SeatedTicks.
using System;
using System.Collections.Generic;
using System.Globalization;
using VRageMath;
using SeSim.Api;
using SeSim.Core;

namespace SeSim.Run
{
    public sealed class ScriptedPilot : IShipBrain
    {
        struct Step { public long Tick; public string Verb; public string[] Args; }
        enum Mode { None, FlyTo, Hold, Face }

        readonly ShipInstance _ship; readonly ScriptHost _host;
        readonly List<Step> _steps = new List<Step>();
        int _next;
        string _seatName = "";           // 'seat NAME': the cockpit whose name contains NAME (default: the first cockpit)
        SimCockpit _cock;                 // the seat (first cockpit of the ship)
        Mode _mode; Vector3D _target; double _speed = 30;
        bool _fireAligned; double _fireDeg;
        bool _firing;
        public int Skipped, SeatedTicks, OverrideFightTicks, Steps;
        public long FirstSeatedTick = -1;
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public ScriptedPilot(ShipInstance ship, ScriptHost host, string script)
        {
            _ship = ship; _host = host;
            Parse(script);
        }

        public SimCockpit Seat { get { return _cock; } }

        void Parse(string script)
        {
            var idx = new List<KeyValuePair<int, Step>>();
            int n = 0;
            foreach (string raw in (script ?? "").Split(';'))
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                int c = s.IndexOf(':');
                double t;
                if (c <= 0 || !double.TryParse(s.Substring(0, c), NumberStyles.Float, Inv, out t)) throw new ArgumentException("pilot step '" + s + "': expected <seconds>:<verb> [args]");
                string[] w = s.Substring(c + 1).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) throw new ArgumentException("pilot step '" + s + "' has no verb");
                var args = new string[w.Length - 1]; Array.Copy(w, 1, args, 0, args.Length);
                idx.Add(new KeyValuePair<int, Step>(n++, new Step { Tick = (long)Math.Round(t * SimSettings.TicksPerSecond), Verb = w[0].ToLowerInvariant(), Args = args }));
            }
            idx.Sort((a, b) => a.Value.Tick != b.Value.Tick ? a.Value.Tick.CompareTo(b.Value.Tick) : a.Key.CompareTo(b.Key));   // stable: equal ticks keep script order
            foreach (var kv in idx) _steps.Add(kv.Value);
        }

        SimCockpit FindSeat()
        {
            foreach (var b in _ship.Bodies)
                for (int i = 0; i < b.Blocks.Count; i++)
                {
                    var c = b.Blocks[i].Api as SimCockpit;
                    if (c != null && !b.Blocks[i].Destroyed && (_seatName == "" || (c.CustomName ?? "").ToLowerInvariant().Contains(_seatName))) return c;
                }
            return null;
        }

        static double D(string[] a, int i, double dflt) { double v; return i < a.Length && double.TryParse(a[i], NumberStyles.Float, Inv, out v) ? v : dflt; }

        public void Tick(World w)
        {
            if (_cock == null || _cock.Inst.Destroyed) { _cock = FindSeat(); }
            while (_next < _steps.Count && _steps[_next].Tick <= w.Tick) Do(w, _steps[_next++]);
            if (_cock == null) return;
            var inp = _cock.Input;
            if (_cock.IsUnderControl)
            {
                SeatedTicks++;
                if (FirstSeatedTick < 0) FirstSeatedTick = w.Tick;
                ClosedLoop(w);
                if (inp.AnyMove || inp.AnyRot) CountFight();
            }
            _cock.AttachToBody();
        }

        void CountFight()
        {
            foreach (var b in _ship.Bodies)
            {
                if (!b.Alive) continue;
                for (int i = 0; i < b.Gyros.Count; i++) if (b.Gyros[i].Override && b.Gyros[i].Working) { OverrideFightTicks++; return; }
                for (int i = 0; i < b.Thrusters.Count; i++) if (b.Thrusters[i].OverrideN > 0 && b.Thrusters[i].Working) { OverrideFightTicks++; return; }
            }
        }

        Body Main { get { return _cock != null ? _cock.Inst.Body : _ship.Main; } }

        Body NearestEnemy(World w)
        {
            Body me = Main, best = null; double bd = double.MaxValue;
            if (me == null) return null;
            for (int i = 0; i < w.Bodies.Count; i++)
            {
                var o = w.Bodies[i];
                if (!o.Alive || o.Team == me.Team || o.ShipIndex < 0 || o.Blocks.Count < 3) continue;
                double d = (o.WorldBoundCenter - me.Position).LengthSquared();
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        void ClosedLoop(World w)
        {
            var inp = _cock.Input;
            var body = _cock.Inst.Body;
            MatrixD toLocal = MatrixD.Transpose(body.RotMatrix);
            // controller-local = grid-local rotated back by the block rotation; world -> grid-local -> controller-local
            Func<Vector3D, Vector3D> worldToCtl = v => Vector3D.TransformNormal(Vector3D.TransformNormal(v, toLocal), MatrixD.Transpose(inp.CtlRot));
            if (_mode == Mode.FlyTo || _mode == Mode.Hold)
            {
                Vector3D want = default(Vector3D);
                if (_mode == Mode.FlyTo)
                {
                    Vector3D to = _target - body.Position; double d = to.Length();
                    if (d > 0.5) want = to * (Math.Min(_speed, 0.5 * d) / d);
                }
                Vector3D dv = want - body.LinearVelocity;
                Vector3D m = worldToCtl(dv) * 0.5;                       // 2 s time constant
                inp.Move = new Vector3D(Cl(m.X), Cl(m.Y), Cl(m.Z));
            }
            if (_mode == Mode.Face || _fireAligned)
            {
                Body e = NearestEnemy(w);
                if (e != null)
                {
                    Vector3D fwd = body.DirToWorld(Vector3D.TransformNormal(Vector3D.Forward, inp.CtlRot));
                    Vector3D to = e.WorldBoundCenter - body.Position; double len = to.Length();
                    if (len > 1e-6)
                    {
                        to /= len;
                        if (_mode == Mode.Face)
                        {
                            Vector3D err = worldToCtl(Vector3D.Cross(fwd, to));       // right-handed rotation vector (rad) in controller axes
                            double kp = 2.0;                                           // rate command = kp * error
                            double g = w.Settings.PilotRateGain * w.Settings.PilotRotSign;
                            inp.Pitch = Cl(kp * err.X / g); inp.Yaw = Cl(kp * err.Y / g); inp.Roll = 0;
                        }
                        if (_fireAligned)
                        {
                            double ang = Math.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(fwd, to)))) * 180 / Math.PI;
                            SetFire(ang <= _fireDeg);
                        }
                    }
                }
                else if (_fireAligned) SetFire(false);
            }
        }

        static double Cl(double v) { return double.IsNaN(v) ? 0 : (v < -1 ? -1 : (v > 1 ? 1 : v)); }

        void SetFire(bool on)
        {
            if (on == _firing) return;
            _firing = on;
            foreach (var b in _ship.Bodies) if (b.Alive) SimToolbar.SetWeapons(b, null, null, on);
        }

        void Do(World w, Step st)
        {
            Steps++;
            string[] a = st.Args;
            switch (st.Verb)
            {
                case "seat": if (_cock != null && _cock.IsUnderControl) { _cock.Input.Clear(); _cock.SetUnderControl(false); } _seatName = string.Join(" ", a).ToLowerInvariant(); _cock = FindSeat(); if (_cock == null) Skipped++; break;
                case "sit": if (_cock == null) { Skipped++; break; } _cock.SetUnderControl(true); break;
                case "leave": if (_cock == null) { Skipped++; break; } _mode = Mode.None; _fireAligned = false; SetFire(false); _cock.SetUnderControl(false); break;
                case "move": if (_cock == null) { Skipped++; break; } _mode = Mode.None; _cock.Input.Move = new Vector3D(D(a, 0, 0), D(a, 1, 0), D(a, 2, 0)); break;
                case "rot": if (_cock == null) { Skipped++; break; } _cock.Input.Pitch = D(a, 0, 0); _cock.Input.Yaw = D(a, 1, 0); break;
                case "roll": if (_cock == null) { Skipped++; break; } _cock.Input.Roll = D(a, 0, 0); break;
                case "stop": if (_cock == null) { Skipped++; break; } _mode = Mode.None; _fireAligned = false; _cock.Input.Clear(); break;
                case "damp": if (_cock == null) { Skipped++; break; } _cock.DampenersOverride = a.Length == 0 || a[0] != "off"; break;
                case "fly-to": if (_cock == null) { Skipped++; break; } _target = new Vector3D(D(a, 0, 0), D(a, 1, 0), D(a, 2, 0)); _speed = D(a, 3, 30); _mode = Mode.FlyTo; break;
                case "hold": if (_cock == null) { Skipped++; break; } _mode = Mode.Hold; break;
                case "face-target":
                    if (_cock == null) { Skipped++; break; }
                    if (a.Length > 0 && a[0] == "off") { _mode = Mode.None; _cock.Input.Pitch = _cock.Input.Yaw = 0; } else _mode = Mode.Face;
                    break;
                case "fire": if (_cock == null) { Skipped++; break; } _fireAligned = false; SetFire(a.Length == 0 || a[0] != "off"); break;
                case "fire-aligned": if (_cock == null) { Skipped++; break; } _fireAligned = true; _fireDeg = D(a, 0, 3); break;
                case "press":
                    if (_cock == null) { Skipped++; break; }
                    if (!SimToolbar.RunSlot(_cock, (int)D(a, 0, 0))) Skipped++;
                    break;
                case "button":
                {
                    SimButtonPanel bp = FindBlock<SimButtonPanel>(a.Length > 0 ? a[0] : "");
                    if (bp == null || !bp.Press((int)D(a, 1, 0))) Skipped++;
                    break;
                }
                case "run":
                {
                    string tag = a.Length > 0 ? a[0] : "";
                    string arg = a.Length > 1 ? string.Join(" ", a, 1, a.Length - 1) : "";
                    var pbs = _ship.PbsByTag(tag);
                    if (pbs.Count == 0) Skipped++;
                    foreach (var r in pbs) _host.RunArgument(r.Pb, arg, Sandbox.ModAPI.Ingame.UpdateType.Terminal);
                    break;
                }
                case "killpb":
                {
                    var pbs = _ship.PbsByTag(a.Length > 0 ? a[0] : "");
                    if (pbs.Count == 0) Skipped++;
                    foreach (var r in pbs) _host.KillPb(r.Pb, "killed by scenario at t=" + (w.Tick / (double)SimSettings.TicksPerSecond).ToString("0.0", Inv));
                    break;
                }
                case "disablepb":
                case "enablepb":
                {
                    var pbs = _ship.PbsByTag(a.Length > 0 ? a[0] : "");
                    if (pbs.Count == 0) Skipped++;
                    foreach (var r in pbs) r.Pb.Enabled = st.Verb == "enablepb";
                    break;
                }
                default: throw new ArgumentException("unknown pilot verb '" + st.Verb + "'");
            }
        }

        T FindBlock<T>(string namePrefix) where T : SimTerminalBlock
        {
            foreach (var b in _ship.Bodies)
                for (int i = 0; i < b.Blocks.Count; i++)
                {
                    var t = b.Blocks[i].Api as T;
                    if (t != null && !b.Blocks[i].Destroyed && t.CustomNameValue.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)) return t;
                }
            return null;
        }
    }
}
