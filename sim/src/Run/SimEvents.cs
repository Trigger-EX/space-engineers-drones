// Harness events: a scenario command whose text starts with '!' is not a PB argument but something done TO the ship at that time.
//   !kill-thrust <F|B|L|R|U|D> [N]   remove N (default all) thrusters that push the ship along that controller direction (Forward, Backward, Left, Right, Up, Down)
// Used by `run/one --events "T:!kill-thrust F;T2:!kill-thrust R 1"` and `follow-test --events ...` to test the THRUST VECTORING block (docs/thrust-vectoring.md).
using System;
using System.Collections.Generic;
using System.Globalization;
using SeSim.Core;
using SeSim.Api;
using VRageMath;

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
}
