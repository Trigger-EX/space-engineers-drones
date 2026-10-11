// SLOT B. IMySensorBlock. Field = box in the sensor's local frame: Left/Right along -X/+X, Bottom/Top -Y/+Y,
// Front/Back -Z/+Z (front = sensor Forward), each extend 0.1..MaxRange (50 m). Initial values from blueprint FieldMin/FieldMax
// (absolute values: FieldMin = (-left,-bottom,-front), FieldMax = (right, top, back); the blueprint default is +-5).
// Every SensorUpdateTicks (10) ticks: grids whose bounding sphere and world AABB intersect the field box (box vs the 8 AABB corners
// expressed in sensor frame, a conservative test) and pass the Detect* filters (size: Small/Large ships/stations; relation flags
// Owner/Friendly/Neutral/Enemy) are stored; DetectedEntities copies that list (HitPosition null). Own body excluded.
using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimSensor : SimFunctionalBlock, IMySensorBlock
    {
        public const float MinExtend = 0.1f;
        public float L = 5, R = 5, T = 5, B = 5, F = 5, Bk = 5;    // extends (m)
        public bool DetectSmallShips, DetectLargeShips, DetectStations, DetectPlayers, DetectAsteroids, DetectFloatingObjects, DetectSubgrids;
        public bool DetectOwner, DetectFriendly, DetectNeutral, DetectEnemy;
        public readonly List<MyDetectedEntityInfo> Detected = new List<MyDetectedEntityInfo>();
        long _lastPoll = long.MinValue / 2;

        public SimSensor(BlockInst inst, World world) : base(inst, world)
        {
            var p = inst.Src != null ? inst.Src.Props : null;
            if (p != null)
            {
                L = Ext(p, "FieldMin.x", L); B = Ext(p, "FieldMin.y", B); F = Ext(p, "FieldMin.z", F);
                R = Ext(p, "FieldMax.x", R); T = Ext(p, "FieldMax.y", T); Bk = Ext(p, "FieldMax.z", Bk);
                DetectPlayers = Flag(p, "DetectPlayers", false); DetectFloatingObjects = Flag(p, "DetectFloatingObjects", false);
                DetectSmallShips = Flag(p, "DetectSmallShips", false); DetectLargeShips = Flag(p, "DetectLargeShips", false);
                DetectStations = Flag(p, "DetectStations", false); DetectSubgrids = Flag(p, "DetectSubgrids", false);
                DetectAsteroids = Flag(p, "DetectAsteroids", false); DetectOwner = Flag(p, "DetectOwner", true);
                DetectFriendly = Flag(p, "DetectFriendly", true); DetectNeutral = Flag(p, "DetectNeutral", true);
                DetectEnemy = Flag(p, "DetectEnemy", true);
            }
            else { DetectOwner = DetectFriendly = DetectNeutral = DetectEnemy = true; }
            L = Clamp(L); R = Clamp(R); T = Clamp(T); B = Clamp(B); F = Clamp(F); Bk = Clamp(Bk);
        }

        static float Ext(Dictionary<string, string> p, string key, float dflt)
        {
            string s; float v;
            if (p.TryGetValue(key, out s) && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return Math.Abs(v);
            return dflt;
        }
        static bool Flag(Dictionary<string, string> p, string key, bool dflt)
        {
            string s;
            if (!p.TryGetValue(key, out s)) return dflt;
            return string.Equals(s.Trim(), "true", StringComparison.OrdinalIgnoreCase) || s.Trim() == "1";
        }
        float Clamp(float v)
        {
            if (float.IsNaN(v)) return MinExtend;
            float max = MaxRange;
            return v < MinExtend ? MinExtend : (v > max ? max : v);
        }

        public void DetectedEntities(List<MyDetectedEntityInfo> l) { l.Clear(); l.AddRange(Detected); }
        public MyDetectedEntityInfo LastDetectedEntity
        {
            get { return Detected.Count > 0 ? Detected[Detected.Count - 1] : default(MyDetectedEntityInfo); }
        }
        public bool IsActive { get { return Detected.Count > 0; } }
        public float MaxRange { get { float m = Inst.Def.SensorMaxRange; return m > 0 ? m : 50f; } }
        public float LeftExtend { get { return L; } set { L = Clamp(value); } }
        public float RightExtend { get { return R; } set { R = Clamp(value); } }
        public float TopExtend { get { return T; } set { T = Clamp(value); } }
        public float BottomExtend { get { return B; } set { B = Clamp(value); } }
        public float FrontExtend { get { return F; } set { F = Clamp(value); } }
        public float BackExtend { get { return Bk; } set { Bk = Clamp(value); } }

        bool Passes(MyRelationsBetweenPlayerAndBlock rel)
        {
            switch (rel)
            {
                case MyRelationsBetweenPlayerAndBlock.Owner: return DetectOwner;
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                case MyRelationsBetweenPlayerAndBlock.Friends: return DetectFriendly;
                case MyRelationsBetweenPlayerAndBlock.Enemies: return DetectEnemy;
                default: return DetectNeutral;   // NoOwnership, Neutral
            }
        }

        /// <summary>World phase 9, every SensorUpdateTicks.</summary>
        public void Poll()
        {
            if (Inst.Destroyed) return;
            long tick = World.Tick;
            if (tick - _lastPoll < World.Settings.SensorUpdateTicks) return;
            _lastPoll = tick;
            Detected.Clear();
            if (!IsWorking) return;

            Body own = Inst.Body;
            MatrixD m = Inst.WorldMatrix;                                  // sensor world matrix (rows Right, Up, Backward)
            Vector3D c = m.Translation, rt = m.Right, up = m.Up, bk = m.Backward;
            double lo0 = -L, hi0 = R, lo1 = -B, hi1 = T, lo2 = -F, hi2 = Bk;   // sensor-local box (x right, y up, z back)
            // bounding sphere of the box for a cheap first reject
            double cx = (hi0 + lo0) * 0.5, cy = (hi1 + lo1) * 0.5, cz = (hi2 + lo2) * 0.5;
            double hx = (hi0 - lo0) * 0.5, hy = (hi1 - lo1) * 0.5, hz = (hi2 - lo2) * 0.5;
            double boxRad = Math.Sqrt(hx * hx + hy * hy + hz * hz);
            Vector3D boxC = c + rt * cx + up * cy + bk * cz;

            var bodies = World.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                Body b = bodies[i];
                if (b == own || !b.Alive) continue;
                bool station = b.IsStatic;
                if (station) { if (!DetectStations) continue; }
                else if (b.GridSize == MyCubeSize.Small) { if (!DetectSmallShips) continue; }
                else if (!DetectLargeShips) continue;
                Vector3D bc = b.LocalToWorld(b.BoundCenterLocal);
                double dx = bc.X - boxC.X, dy = bc.Y - boxC.Y, dz = bc.Z - boxC.Z;
                double reach = boxRad + b.BoundRadius;
                if (dx * dx + dy * dy + dz * dz > reach * reach) continue;
                if (!AabbHitsBox(b.WorldAabb(), c, rt, up, bk, lo0, hi0, lo1, hi1, lo2, hi2)) continue;
                long owner = Detection.EffectiveOwner(b);
                if (!Passes(World.Teams.Relation(Inst.OwnerId, owner))) continue;
                Detected.Add(Detection.FromBody(World, b, Inst.OwnerId, null));
            }
        }

        /// <summary>Box (in sensor frame) vs world AABB: the AABB corners are expressed in sensor axes and their bounds are
        /// tested against the box (conservative: may report a grid whose AABB corner region misses the box by a hair).</summary>
        static bool AabbHitsBox(BoundingBoxD a, Vector3D c, Vector3D rt, Vector3D up, Vector3D bk,
                                double lo0, double hi0, double lo1, double hi1, double lo2, double hi2)
        {
            double mn0 = double.MaxValue, mx0 = double.MinValue, mn1 = double.MaxValue, mx1 = double.MinValue, mn2 = double.MaxValue, mx2 = double.MinValue;
            for (int k = 0; k < 8; k++)
            {
                double px = ((k & 1) != 0 ? a.Max.X : a.Min.X) - c.X, py = ((k & 2) != 0 ? a.Max.Y : a.Min.Y) - c.Y, pz = ((k & 4) != 0 ? a.Max.Z : a.Min.Z) - c.Z;
                double s0 = px * rt.X + py * rt.Y + pz * rt.Z, s1 = px * up.X + py * up.Y + pz * up.Z, s2 = px * bk.X + py * bk.Y + pz * bk.Z;
                if (s0 < mn0) mn0 = s0; if (s0 > mx0) mx0 = s0;
                if (s1 < mn1) mn1 = s1; if (s1 > mx1) mx1 = s1;
                if (s2 < mn2) mn2 = s2; if (s2 > mx2) mx2 = s2;
            }
            return mx0 >= lo0 && mn0 <= hi0 && mx1 >= lo1 && mn1 <= hi1 && mx2 >= lo2 && mn2 <= hi2;
        }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            const float dflt = 5f;
            AddFloat(r, "Left", b => ((SimSensor)b).L, (b, v) => ((SimSensor)b).LeftExtend = v, dflt, MinExtend, 50f);
            AddFloat(r, "Right", b => ((SimSensor)b).R, (b, v) => ((SimSensor)b).RightExtend = v, dflt, MinExtend, 50f);
            AddFloat(r, "Bottom", b => ((SimSensor)b).B, (b, v) => ((SimSensor)b).BottomExtend = v, dflt, MinExtend, 50f);
            AddFloat(r, "Top", b => ((SimSensor)b).T, (b, v) => ((SimSensor)b).TopExtend = v, dflt, MinExtend, 50f);
            AddFloat(r, "Back", b => ((SimSensor)b).Bk, (b, v) => ((SimSensor)b).BackExtend = v, dflt, MinExtend, 50f);
            AddFloat(r, "Front", b => ((SimSensor)b).F, (b, v) => ((SimSensor)b).FrontExtend = v, dflt, MinExtend, 50f);
            AddBool(r, "Detect Players", b => ((SimSensor)b).DetectPlayers, (b, v) => ((SimSensor)b).DetectPlayers = v, true);
            AddBool(r, "Detect Floating Objects", b => ((SimSensor)b).DetectFloatingObjects, (b, v) => ((SimSensor)b).DetectFloatingObjects = v, true);
            AddBool(r, "Detect Small Ships", b => ((SimSensor)b).DetectSmallShips, (b, v) => ((SimSensor)b).DetectSmallShips = v, true);
            AddBool(r, "Detect Large Ships", b => ((SimSensor)b).DetectLargeShips, (b, v) => ((SimSensor)b).DetectLargeShips = v, true);
            AddBool(r, "Detect Stations", b => ((SimSensor)b).DetectStations, (b, v) => ((SimSensor)b).DetectStations = v, true);
            AddBool(r, "Detect Subgrids", b => ((SimSensor)b).DetectSubgrids, (b, v) => ((SimSensor)b).DetectSubgrids = v, true);
            AddBool(r, "Detect Asteroids", b => ((SimSensor)b).DetectAsteroids, (b, v) => ((SimSensor)b).DetectAsteroids = v, true);
            AddBool(r, "Detect Owner", b => ((SimSensor)b).DetectOwner, (b, v) => ((SimSensor)b).DetectOwner = v, true);
            AddBool(r, "Detect Friendly", b => ((SimSensor)b).DetectFriendly, (b, v) => ((SimSensor)b).DetectFriendly = v, true);
            AddBool(r, "Detect Neutral", b => ((SimSensor)b).DetectNeutral, (b, v) => ((SimSensor)b).DetectNeutral = v, true);
            AddBool(r, "Detect Enemy", b => ((SimSensor)b).DetectEnemy, (b, v) => ((SimSensor)b).DetectEnemy = v, true);
        }
    }
}
