// LEAD BENCH (docs/lead-v1.md). Offline test of the drone scripts' lead solvers: one shooter (Basilisk / Hornet / Longbow-like
// movement) fires at one box-shaped target (or a twin-hull "cat", or a hollow "tube") that manoeuvres: strafe (flip every 2-5 s,
// like the vanilla AI's evasive option), strafe3d, strafeturn, random, orbit, cruise, burn. Sightings every 0.2 s give exact
// position / velocity / orientation and one hull point (a ray from the shooter), as a camera lock does. Each solver variant (Cfg)
// keeps its own Track and fires at the same moments; rockets fly the 60 Hz model chosen by truth=0 (no inheritance), 1 (ship velocity
// + launch speed, speed capped: curving) or 2 (full inheritance). Output: hit % per variant.
//   build:  mcs -langversion:7.2 -optimize+ -r:out/sim/SeApi.dll -out:bench.exe tools/lead_bench/Bench.cs   (sim/build.sh makes SeApi.dll)
//   run:    mono bench.exe weapon=rocket scen=strafe size=huge truth=1 seeds=12
//           mono bench.exe weapon=gatling scen=random size=small amax=15 flipmin=0.5 flipmax=2 standoff=350 sacc=20 svmax=100 orbit=25 jink=10 shotdt=0.1
// "F ..." rows are the shipped LEAD v1.0 settings (AccMode 2 fresh acceleration, AccPred 2 hold with HoldLong 2, lateral aim offset,
// spin for <= 1 s, hull margin); the others are the alternatives that lost (docs/lead-v1.md).
// Offline rocket-lead bench: a Basilisk-like shooter fires rockets at a huge evasive box target.
// Compares the v2.3 lead solver with candidate improvements. Sightings every 0.2 s (exact pos/vel/orientation, one hull point).
using System;
using System.Collections.Generic;
using VRageMath;

class Cfg
{
    public string Name;
    public int RktModel = 0;       // solver's rocket model: 0 none, 1 capped (inherit + speed cap), 2 full inherit
    public int AccMode = 0;        // 0 old (0.8/0.2 per sighting + steadiness gate), 1 segment mean
    public bool Singer = false;    // exponentially decaying acceleration over the flight
    public bool RotAim = false;    // rotate the hull aim offset with the target's measured spin
    public bool Robust = false;    // keep the aim point inside the hull by the predicted miss
    public string Aim = "AUTO";
    public double SigmaK = 1.0;
    public double ManT0 = 2.0;
    public int AccPred = 1;   // with Singer: 1 tau=ManT, 2 hold for the expected remaining time, 3 tau=max(ManT, segment age)
    public bool Central = false;
    public int SegWin = 8;
    public double HoldLong = 0;
    public bool Lateral; public double RotCap = 4;   // aim offset: drop the part along the line of sight
    public double MarginK = -1;   // >=0: only hull points with this many sigmas of known hull around them (across the line of fire)
    public int WideAim = 0;   // when Wide: 0 bounding-box centre, 1 lateral part of the most central hull point
    public double SigSwitch = -1;   // >0: aim at the centre when the predicted miss exceeds this fraction of the radius
    public bool ForceCenter;
}

class Track
{
    public Cfg C;
    public Vector3D Pos, Vel, Acc; public double AccMag, Seen, Radius, RawMax;
    public MatrixD O = MatrixD.Identity; public bool HasOri; public Vector3D OF, OU, OR;
    public List<Vector3D> Hull = new List<Vector3D>(); public int HullNext;
    public Vector3D Thrust, AimLocal; public double AimT = -99; public bool WideSel;
    public Vector3D W;                         // angular velocity (world, rad/s)
    public Vector3D AccF;
    public Vector3D SegAcc; public int SegN; public double SegT0 = -99, ManT = 2.0;
    public Track(Cfg c) { C = c; ManT = c.ManT0; }
    public void Ori(MatrixD o, double dt)
    {
        if (HasOri && dt > 0.04 && dt < 2)
        {
            Vector3D phi = 0.5 * (Vector3D.Cross(OF, o.Forward) + Vector3D.Cross(OU, o.Up) + Vector3D.Cross(OR, o.Right));
            W = W * 0.5 + phi / dt * 0.5;
        }
        OF = o.Forward; OU = o.Up; OR = o.Right; HasOri = true; O = o;
    }
    public void Set(Vector3D p, Vector3D v, double r, double now)
    {
        double dt = now - Seen;
        if (Seen > 0 && dt > 0.04 && dt < 2)
        {
            Vector3D a = (v - Vel) / dt;
            if (a.LengthSquared() > 10000) a = Vector3D.Normalize(a) * 100;
            Acc = Acc * 0.8 + a * 0.2;
            AccF = AccF * 0.2 + (a.LengthSquared() > 900 ? Vector3D.Normalize(a) * 30 : a) * 0.8;
            AccMag = AccMag * 0.8 + a.Length() * 0.2;
            if (HasOri && a.LengthSquared() > 1) Thrust = Thrust * 0.9 + Vector3D.TransformNormal(Vector3D.Normalize(a), MatrixD.Transpose(O)) * 0.1;
            // manoeuvre segments: a new one starts when the acceleration turns well away from the current segment's mean
            double am = a.Length(), sm = SegAcc.Length(), mx = Math.Max(am, sm);
            if (SegN == 0 || (mx > 0.5 && (a - SegAcc).Length() > 0.6 * mx && now - SegT0 > 0.3))
            {
                if (SegN > 0) ManT = MathHelper.Clamp(ManT * 0.7 + (now - SegT0) * 0.3, 0.4, 8);
                SegAcc = a; SegN = 1; SegT0 = now;
            }
            else { SegN++; SegAcc += (a - SegAcc) / Math.Min(SegN, C.SegWin); }
            if (now - SegT0 > ManT) ManT = Math.Min(8, ManT + 0.3 * dt);   // a long steady burn: trust it longer
        }
        else if (dt >= 2) { Acc = Vector3D.Zero; AccMag = 0; SegN = 0; }
        Pos = p; Vel = v; Radius = Math.Max(r, 1.5); RawMax = Math.Max(RawMax, r); Seen = now;
    }
    public Vector3D EA()
    {
        if (C.AccMode == 1) return SegN >= 2 ? SegAcc : Vector3D.Zero;
        if (C.AccMode == 2) return AccF.LengthSquared() < 0.25 ? Vector3D.Zero : AccF * 0.9;
        double m = Acc.Length();
        if (m < 0.5 || AccMag < 1e-3) return Vector3D.Zero;
        double steady = MathHelper.Clamp((m / AccMag - 0.6) / 0.3, 0, 1);
        return Acc * steady;
    }
    const double AccHold = 1.0;
    public Vector3D P(double now) { double t = now - Seen, ta = Math.Min(t, AccHold); return Pos + Vel * t + EA() * (ta * t - 0.5 * ta * ta); }
    public Vector3D V(double now) { return Vel + EA() * Math.Min(now - Seen, AccHold); }
}

class Solver
{
    public Cfg C; public Track T; public double Now; public long Slot = 4242;
    public double RktV0 = 100, RktAcc = 600, RktVmax = 200;
    public double LeadT; public bool LeadOk; public double Sigma;
    public Solver(Cfg c) { C = c; T = new Track(c); }

    double RktDist(double t)
    {
        double ta = Math.Max(0, (RktVmax - RktV0) / Math.Max(RktAcc, 1));
        if (t <= ta) return RktV0 * t + 0.5 * RktAcc * t * t;
        return RktV0 * ta + 0.5 * RktAcc * ta * ta + RktVmax * (t - ta);
    }
    double RktTime(double d)
    {
        double ta = Math.Max(0, (RktVmax - RktV0) / Math.Max(RktAcc, 1));
        double da = RktV0 * ta + 0.5 * RktAcc * ta * ta;
        if (d <= da) return (-RktV0 + Math.Sqrt(RktV0 * RktV0 + 2 * RktAcc * d)) / Math.Max(RktAcc, 1e-6);
        return ta + (d - da) / Math.Max(RktVmax, 1);
    }
    // capped model: launch velocity = ship velocity + V0 along the barrel; accelerates along the barrel; speed capped at Vmax
    // (the cap rescales the velocity, so a sideways start bends round towards the barrel axis). ua/us = ship velocity along / across
    // the barrel. Returns distance along the barrel s, sideways drift q (in the direction of us) and the speed along the barrel sv.
    public void RktPath(double T, double ua, double us, out double s, out double q, out double sv)
    {
        double V = RktVmax, A = Math.Max(RktAcc, 1e-3), va = ua + RktV0, t1, th;
        if (va * va + us * us >= V * V) { t1 = 0; th = Math.Atan2(us, va); }
        else { t1 = (Math.Sqrt(V * V - us * us) - va) / A; th = Math.Asin(MathHelper.Clamp(us / V, -1, 1)); }
        if (T <= t1) { s = va * T + 0.5 * A * T * T; q = us * T; sv = va + A * T; return; }
        s = va * t1 + 0.5 * A * t1 * t1; q = us * t1;
        double k = A / V, h = T - t1, c = Math.Tan(0.5 * th), x = c * Math.Exp(-k * h);
        q += V / k * 2 * (Math.Atan(c) - Math.Atan(x));
        s += V * (h - Math.Log((1 + c * c) / (1 + x * x)) / k);
        sv = V * (1 - x * x) / (1 + x * x);
    }
    // displacement factor of the target's acceleration over t seconds of flight (Singer: acceleration decays with the manoeuvre time)
    double Tau() { double age = Now - T.SegT0; if (C.AccPred == 3) return Math.Max(T.ManT, age); if (C.AccPred == 2) return Math.Max(Math.Max(T.ManT - age, 0.3 * T.ManT), C.HoldLong * (age - T.ManT)); return T.ManT; }
    double AccK(double t)
    {
        if (!C.Singer) return 0.5 * t * t; double tau = Tau();
        if (C.AccPred == 2) return t <= tau ? 0.5 * t * t : tau * (t - 0.5 * tau);
        return tau * tau * (t / tau - 1 + Math.Exp(-t / tau));
    }
    double AccKd(double t) { if (!C.Singer) return t; double tau = Tau(); if (C.AccPred == 2) return Math.Min(t, tau); return tau * (1 - Math.Exp(-t / tau)); }

    public bool Wide; public double SigPre; public Vector3D CurLos;
    public bool AimActive() { return C.Aim != "CENTER" && T.Hull.Count >= 3 && (!Wide || C.WideAim == 1); }
    Vector3D CentralLocal(Vector3D los)
    {
        var t = T; int n = t.Hull.Count;
        Vector3D l = Vector3D.TransformNormal(los, MatrixD.Transpose(t.O));
        Vector3D cen = Vector3D.Zero; foreach (var h in t.Hull) cen += h; cen /= n;
        cen -= Vector3D.Dot(cen, l) * l;
        int best = 0; double bestD = 1e9;
        for (int i = 0; i < n; i++) { Vector3D d = t.Hull[i] - Vector3D.Dot(t.Hull[i], l) * l - cen; double dl = d.LengthSquared(); if (dl < bestD) { bestD = dl; best = i; } }
        Vector3D h0 = t.Hull[best]; return h0 - Vector3D.Dot(h0, l) * l;
    }
    public string AimLabel() { if (!AimActive()) return "CENTER"; return C.Aim == "AUTO" ? (T.Thrust.LengthSquared() > 0.09 ? "ENGINES" : "SPREAD") : C.Aim; }
    public Vector3D AimLocal(Vector3D shooter, Vector3D los)
    {
        var t = T;
        if (Wide && C.WideAim == 1) { if (Now - t.AimT > 0.5 || !t.WideSel) { t.AimLocal = CentralLocal(los); t.AimT = Now; t.WideSel = true; } return t.AimLocal; }
        if (t.WideSel) { t.WideSel = false; t.AimT = -99; }
        if (Now - t.AimT > 0.5)
        {
            string mode = AimLabel();
            int n = t.Hull.Count, slot = (int)Math.Abs(Slot % 9973);
            int step = slot + (int)(Now / 4);
            Vector3D axis = mode == "ENGINES" ? -t.Thrust : Vector3D.TransformNormal(shooter - t.P(Now), MatrixD.Transpose(t.O));
            if (axis.LengthSquared() < 1e-6) axis = Vector3D.Forward;
            axis.Normalize();
            var idx = new List<int>(); for (int i = 0; i < n; i++) idx.Add(i);
            if (C.Central)
            {
                Vector3D l = Vector3D.TransformNormal(los, MatrixD.Transpose(t.O));
                Vector3D cen = Vector3D.Zero; foreach (var h in t.Hull) cen += h; cen /= n;
                int best = 0; double bestD = 1e9;
                for (int i = 0; i < n; i++) { Vector3D d = t.Hull[i] - cen; d -= Vector3D.Dot(d, l) * l; double dl = d.LengthSquared(); if (dl < bestD) { bestD = dl; best = i; } }
                t.AimLocal = t.Hull[best]; t.AimT = Now; return t.AimLocal;
            }
            if (C.MarginK >= 0)
            {
                Vector3D l = Vector3D.TransformNormal(los, MatrixD.Transpose(t.O));
                Vector3D u = Vector3D.Cross(l, Math.Abs(l.Y) < 0.9 ? Vector3D.Up : Vector3D.Right); u.Normalize(); Vector3D v = Vector3D.Cross(l, u);
                Vector3D[] ax = { u, v, (u + v) * 0.7071, (u - v) * 0.7071 };
                double[] mn = new double[4], mxv = new double[4]; for (int a = 0; a < 4; a++) { mn[a] = 1e9; mxv[a] = -1e9; }
                foreach (var h in t.Hull) for (int a = 0; a < 4; a++) { double x = Vector3D.Dot(h, ax[a]); mn[a] = Math.Min(mn[a], x); mxv[a] = Math.Max(mxv[a], x); }
                var keep = new List<int>(); int best = 0; double bestM = -1e9;
                for (int i = 0; i < n; i++)
                {
                    double m = 1e9; for (int a = 0; a < 4; a++) { double x = Vector3D.Dot(t.Hull[i], ax[a]); m = Math.Min(m, Math.Min(x - mn[a], mxv[a] - x)); }
                    if (m >= C.MarginK * SigPre) keep.Add(i);
                    if (m > bestM) { bestM = m; best = i; }
                }
                if (keep.Count == 0) keep.Add(best);
                idx = keep; n = keep.Count;
            }
            if (C.Robust)
            {
                // keep only hull points that leave Sigma of known hull around them across the line of fire
                Vector3D l = Vector3D.TransformNormal(los, MatrixD.Transpose(t.O));
                Vector3D cen = Vector3D.Zero; foreach (var h in t.Hull) cen += h; cen /= n;
                double rmax = 0;
                foreach (var h in t.Hull) { Vector3D d = h - cen; d -= Vector3D.Dot(d, l) * l; rmax = Math.Max(rmax, d.Length()); }
                double allow = rmax - Sigma;
                var keep = new List<int>(); int best = 0; double bestD = 1e9;
                for (int i = 0; i < n; i++) { Vector3D d = t.Hull[i] - cen; d -= Vector3D.Dot(d, l) * l; double dl = d.Length(); if (dl <= allow) keep.Add(i); if (dl < bestD) { bestD = dl; best = i; } }
                if (keep.Count == 0) keep.Add(best);
                idx = keep; n = keep.Count;
            }
            if (mode == "SPREAD") t.AimLocal = t.Hull[idx[step % n]];
            else
            {
                idx.Sort((a, b) => Vector3D.Dot(t.Hull[b], axis).CompareTo(Vector3D.Dot(t.Hull[a], axis)));
                t.AimLocal = t.Hull[idx[step % Math.Max(1, n / 4)]];
            }
            t.AimT = Now;
        }
        return t.AimLocal;
    }
    Vector3D Rot(Vector3D v, Vector3D w, double h)
    {
        double wl = w.Length(); if (wl < 1e-4 || h <= 0) return v;
        Vector3D k = w / wl; double a = wl * h, c = Math.Cos(a), s = Math.Sin(a);
        return v * c + Vector3D.Cross(k, v) * s + k * (Vector3D.Dot(k, v) * (1 - c));
    }
    // aim offset (world) at flight time tt
    Vector3D Off(Vector3D local, double tt)
    {
        if (!AimActive()) return Vector3D.Zero;
        Vector3D o = Vector3D.TransformNormal(local, T.O);
        o = C.RotAim ? Rot(o, T.W, Math.Min(Now - T.Seen + tt, C.RotCap)) : o;
        if (C.Lateral) o -= Vector3D.Dot(o, CurLos) * CurLos;
        return o;
    }

    public double BulletSpeed = 0;
    public bool LeadBullet(Vector3D shooter, Vector3D shooterVel, out Vector3D point, out Vector3D dir)
    {
        var t = T;
        Vector3D c0 = t.P(Now), tv = t.V(Now), ta = t.EA();
        Vector3D rp0 = c0 - shooter, rv = tv - shooterVel; double sp = BulletSpeed;
        Vector3D los = Vector3D.Normalize(rp0); CurLos = los;
        double tt = rp0.Length() / sp;
        { double ju0 = Math.Max(0, t.AccMag - t.EA().Length()); double sg = 0.5 * Math.Max(ju0, 0.5 * t.AccMag) * Math.Min(tt, 2 * t.ManT) * tt; Wide = C.SigSwitch > 0 && sg > C.SigSwitch * t.Radius; SigPre = 0.5 * t.AccMag * tt * tt; }
        Vector3D local = AimActive() ? AimLocal(shooter, los) : Vector3D.Zero;
        Vector3D rp = rp0 + Off(local, tt);
        double a = rv.LengthSquared() - sp * sp, b = 2 * Vector3D.Dot(rp, rv), c = rp.LengthSquared();
        double disc = b * b - 4 * a * c;
        if (Math.Abs(a) > 1e-6 && disc >= 0) { double sq = Math.Sqrt(disc), t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a); tt = (t1 > 0 && (t1 < t2 || t2 <= 0)) ? t1 : t2; }
        if (tt <= 0 || double.IsNaN(tt)) tt = rp.Length() / sp;
        Vector3D p = rp;
        for (int i = 0; i < 6; i++) { p = rp0 + Off(local, tt) + rv * tt + ta * AccK(tt); tt = p.Length() / sp; }
        LeadT = tt; LeadOk = true; point = shooter + p; dir = Vector3D.Normalize(p);
        return true;
    }
    public bool Lead(Vector3D shooter, Vector3D shooterVel, out Vector3D point, out Vector3D dir)
    {
        var t = T;
        Vector3D c0 = t.P(Now), tv = t.V(Now), ta = t.EA();
        Vector3D rp0 = c0 - shooter;
        Vector3D los = Vector3D.Normalize(rp0);
        // predicted miss (1 sigma): what the target can do that the model does not predict, over the flight
        double tt = RktTime(rp0.Length());
        { double ju0 = Math.Max(0, t.AccMag - t.EA().Length()); double sg = 0.5 * Math.Max(ju0, 0.5 * t.AccMag) * Math.Min(tt, 2 * t.ManT) * tt; Wide = C.SigSwitch > 0 && sg > C.SigSwitch * t.Radius; SigPre = 0.5 * t.AccMag * tt * tt; }
        Sigma = 0;
        Vector3D local = AimActive() ? AimLocal(shooter, los) : Vector3D.Zero;
        if (C.RktModel == 0 || C.RktModel == 2)
        {
            Vector3D rv = C.RktModel == 2 ? tv - shooterVel : tv;
            double resid = 0; Vector3D p = rp0;
            for (int i = 0; i < 10; i++)
            {
                p = rp0 + Off(local, tt) + rv * tt + ta * AccK(tt);
                double nt = RktTime(p.Length());
                resid = Math.Abs(nt - tt);
                tt = MathHelper.Clamp(0.5 * (tt + nt), 0, 12);
            }
            LeadOk = resid < 0.05; LeadT = tt;
            point = shooter + p; dir = Vector3D.Normalize(p);
        }
        else
        {
            // capped rocket: R(T) = s d + q e, e = sideways part of our velocity across the barrel d. Newton on the flight time.
            Vector3D d = los; Vector3D p = rp0; double f = 0;
            for (int i = 0; i < 12; i++)
            {
                Vector3D D = rp0 + Off(local, tt) + tv * tt + ta * AccK(tt);
                double ua = Vector3D.Dot(shooterVel, d); Vector3D up = shooterVel - ua * d; double us = up.Length();
                Vector3D e = us > 1e-3 ? up / us : Vector3D.Zero;
                double s, q, sv; RktPath(tt, ua, us, out s, out q, out sv);
                Vector3D Wv = D - q * e; double wl = Wv.Length();
                d = Wv / wl; p = D;
                f = s - wl;
                double closing = Vector3D.Dot(d, tv + ta * AccKd(tt));
                tt = MathHelper.Clamp(tt - f / Math.Max(sv - closing, 20), 0.02, 12);
            }
            LeadOk = Math.Abs(f) < 2; LeadT = tt;
            point = shooter + p; dir = d;
        }
        double ju = Math.Max(0, t.AccMag - t.EA().Length());
        Sigma = C.SigmaK * 0.5 * Math.Max(ju, 0.5 * t.AccMag) * Math.Min(LeadT, 2 * t.ManT) * LeadT;
        return LeadOk;
    }
}

static class Bench
{
    // oriented box: half extents in local (right, up, backward) coordinates; target world matrix rows = basis
    static bool SegBox(Vector3D a, Vector3D b, Vector3D half, double grow)
    {
        Vector3D d = b - a; double t0 = 0, t1 = 1;
        double[] A = { a.X, a.Y, a.Z }, D = { d.X, d.Y, d.Z }, H = { half.X + grow, half.Y + grow, half.Z + grow };
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(D[i]) < 1e-12) { if (A[i] < -H[i] || A[i] > H[i]) return false; continue; }
            double u0 = (-H[i] - A[i]) / D[i], u1 = (H[i] - A[i]) / D[i];
            if (u0 > u1) { var x = u0; u0 = u1; u1 = x; }
            t0 = Math.Max(t0, u0); t1 = Math.Min(t1, u1); if (t0 > t1) return false;
        }
        return true;
    }
    static double BoxDist(Vector3D p, Vector3D half)
    {
        double dx = Math.Max(Math.Abs(p.X) - half.X, 0), dy = Math.Max(Math.Abs(p.Y) - half.Y, 0), dz = Math.Max(Math.Abs(p.Z) - half.Z, 0);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
    static bool RayBox(Vector3D a, Vector3D d, Vector3D half, out double tHit)
    {
        tHit = 0; double t0 = 0, t1 = 1e9;
        double[] A = { a.X, a.Y, a.Z }, D = { d.X, d.Y, d.Z }, H = { half.X, half.Y, half.Z };
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(D[i]) < 1e-12) { if (A[i] < -H[i] || A[i] > H[i]) return false; continue; }
            double u0 = (-H[i] - A[i]) / D[i], u1 = (H[i] - A[i]) / D[i];
            if (u0 > u1) { var x = u0; u0 = u1; u1 = x; }
            t0 = Math.Max(t0, u0); t1 = Math.Min(t1, u1); if (t0 > t1) return false;
        }
        tHit = t0; return true;
    }

    static List<Vector3D> BC = new List<Vector3D>(), BH = new List<Vector3D>();
    static bool SegAny(Vector3D a, Vector3D b) { for (int i = 0; i < BC.Count; i++) if (SegBox(a - BC[i], b - BC[i], BH[i], 0)) return true; return false; }
    static double DistAny(Vector3D p) { double m = 1e9; for (int i = 0; i < BC.Count; i++) m = Math.Min(m, BoxDist(p - BC[i], BH[i])); return m; }
    static bool RayAny(Vector3D a, Vector3D d, out double th) { th = 1e9; bool hit = false; for (int i = 0; i < BC.Count; i++) { double t; if (RayBox(a - BC[i], d, BH[i], out t) && t < th) { th = t; hit = true; } } return hit; }
    class Rocket { public Vector3D P, V, D, U; public double Own = 100; public double Trav; public int Who; public bool Done; public double MinD = 1e9; }

    static bool DBG = Environment.GetEnvironmentVariable("DBG") == "1";
    static Dictionary<string, string> A = new Dictionary<string, string>();
    static double D(string k, double d) { string v; return A.TryGetValue(k, out v) ? double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : d; }
    static string S(string k, string d) { string v; return A.TryGetValue(k, out v) ? v : d; }

    public static void Main(string[] args)
    {
        foreach (var x in args) { int i = x.IndexOf('='); if (i > 0) A[x.Substring(0, i)] = x.Substring(i + 1); }
        string scen = S("scen", "strafe"), size = S("size", "huge"), weapon = S("weapon", "rocket"), aim = S("aim", "AUTO"), set = S("set", "b");
        int seeds = (int)D("seeds", 12), truth = (int)D("truth", 1);
        double amax = D("amax", 5), flipMin = D("flipmin", 2), flipMax = D("flipmax", 5), strafeV = D("strafev", 30), standoff = D("standoff", 750);
        double sightDt = D("sight", 0.2), shotDt = D("shotdt", 0.6), sAcc = D("sacc", 6), sVmax = D("svmax", 60), orbitV = D("orbit", 10), jinkV = D("jink", 5);
        double bulletV = weapon == "gatling" ? 800 : weapon == "rail" ? 1000 : 0, range = weapon == "gatling" ? 800 : weapon == "rail" ? 1400 : 800;
        double spread = weapon == "gatling" ? 0.15 : weapon == "rail" ? 0 : 0.1;
        var cfgs = new List<Cfg>();
        if (weapon == "rocket")
        {
            cfgs.Add(new Cfg { Name = "v2.3 (as shipped)", Aim = aim });
            cfgs.Add(new Cfg { Name = "SHIPPED v2.4 (LEAD v1.0)", RktModel = 1, AccMode = 2, Singer = true, AccPred = 2, HoldLong = 2, MarginK = 1, RotAim = true, RotCap = 1, Lateral = true, Aim = aim });
            foreach (double hl in new[] { 0.0, 0.5, 1.0, 2.0 })
                cfgs.Add(new Cfg { Name = "F k0.5 rot1 hl" + hl, RktModel = 1, AccMode = 2, Singer = true, AccPred = 2, HoldLong = hl, MarginK = 0.5, RotAim = true, RotCap = 1, Lateral = true, Aim = aim });
            cfgs.Add(new Cfg { Name = "F hl1 quadratic", RktModel = 1, AccMode = 2, MarginK = 0.5, RotAim = true, RotCap = 1, Lateral = true, Aim = aim });
            cfgs.Add(new Cfg { Name = "F hl1, truth-none solver", RktModel = 0, AccMode = 2, Singer = true, AccPred = 2, HoldLong = 1, MarginK = 0.5, RotAim = true, RotCap = 1, Lateral = true, Aim = aim });
            cfgs.Add(new Cfg { Name = "fast+hold, BB centre switch", RktModel = 1, AccMode = 2, Singer = true, AccPred = 2, SigSwitch = 0.1, RotAim = true, Aim = aim });
        }
        else
        {
            cfgs.Add(new Cfg { Name = "old smoothed+gate (Hornet)", Aim = aim });
            cfgs.Add(new Cfg { Name = "old fast acc (Longbow)", AccMode = 2, Aim = aim });
            cfgs.Add(new Cfg { Name = "fast acc+hold+sw", AccMode = 2, Singer = true, AccPred = 2, SigSwitch = 0.1, RotAim = true, Aim = aim });
            cfgs.Add(new Cfg { Name = "SHIPPED v2.4 (LEAD v1.0)", AccMode = 2, Singer = true, AccPred = 2, HoldLong = 2, MarginK = 1, RotAim = true, RotCap = 1, Lateral = true, Aim = aim });
            foreach (double hl in new[] { 0.0, 1.0 }) foreach (double k in new[] { 1.0, 2.0 })
                cfgs.Add(new Cfg { Name = "F k" + k + " rot1 hl" + hl, AccMode = 2, Singer = true, AccPred = 2, HoldLong = hl, MarginK = k, RotAim = true, RotCap = 1, Lateral = true, Aim = aim });
            cfgs.Add(new Cfg { Name = "old smoothed, CENTER", Aim = "CENTER" });
            cfgs.Add(new Cfg { Name = "fast acc (Longbow), CENTER", AccMode = 2, Aim = "CENTER" });
        }
        int nc = cfgs.Count;
        int[] shots = new int[nc], hits = new int[nc];
        double[] missSum = new double[nc];
        Vector3D half = size == "huge" || size == "cat" ? new Vector3D(15, 12, 60) : size == "large" ? new Vector3D(9, 5, 15) : size == "small" || size == "tube" ? new Vector3D(1.5, 1.25, 3) : new Vector3D(30, 20, 100);
        if (size == "cat") { BC.Add(new Vector3D(-11, 0, 0)); BH.Add(new Vector3D(4, 8, 60)); BC.Add(new Vector3D(11, 0, 0)); BH.Add(new Vector3D(4, 8, 60)); BC.Add(new Vector3D(0, 4, -50)); BH.Add(new Vector3D(15, 4, 8)); }
        else if (size == "tube") { BC.Add(new Vector3D(-1.25, 0, 0)); BH.Add(new Vector3D(0.25, 1.25, 3)); BC.Add(new Vector3D(1.25, 0, 0)); BH.Add(new Vector3D(0.25, 1.25, 3)); BC.Add(new Vector3D(0, 1, 0)); BH.Add(new Vector3D(1.5, 0.25, 3)); BC.Add(new Vector3D(0, -1, 0)); BH.Add(new Vector3D(1.5, 0.25, 3)); }
        else { BC.Add(Vector3D.Zero); BH.Add(half); }
        double dt = 1.0 / 60;
        for (int seed = 0; seed < seeds; seed++)
        {
            var rng = new Random(1000 + seed);
            var sol = new List<Solver>(); foreach (var c in cfgs) { var so = new Solver(c); so.BulletSpeed = bulletV; sol.Add(so); }
            Vector3D tp = Vector3D.Zero, tv = new Vector3D(rng.NextDouble() * 20 - 10, 0, rng.NextDouble() * 20 - 10);
            MatrixD to = MatrixD.CreateWorld(Vector3D.Zero, Vector3D.Normalize(new Vector3D(rng.NextDouble() - 0.5, 0.1, rng.NextDouble() - 0.5)), Vector3D.Up);
            to.Translation = Vector3D.Zero;
            Vector3D tw = Vector3D.Zero;
            double ang = rng.NextDouble() * 6.283;
            Vector3D sp = new Vector3D(Math.Cos(ang) * standoff, (rng.NextDouble() - 0.5) * standoff * 0.25, Math.Sin(ang) * standoff), sv = tv;
            double flipAt = 0; int strafe = 1; Vector3D side = Vector3D.Zero, vdes = tv, cruise = tv; Vector3D wdes = Vector3D.Zero;
            Vector3D jink = Vector3D.Zero; double jinkAt = 0; double orbitSign = 1;
            var rockets = new List<Rocket>();
            double nextSight = 0, nextShot = 1.0, lastSight = -1;
            for (int tick = 0; tick < 60 * 90; tick++)
            {
                double now = tick * dt;
                Vector3D los = tp - sp; double dist = los.Length(); Vector3D lu = los / dist;
                if (scen == "strafe" || scen == "strafe3d" || scen == "strafeturn")
                {
                    if (now >= flipAt)
                    {
                        strafe = -strafe; flipAt = now + flipMin + (flipMax - flipMin) * rng.NextDouble();
                        if (scen == "strafe3d" || side.LengthSquared() < 0.5) { side = Vector3D.Cross(lu, new Vector3D(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5)); side.Normalize(); }
                        if (scen == "strafeturn") wdes = new Vector3D(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 0.5;
                    }
                    Vector3D s2 = side - Vector3D.Dot(side, lu) * lu; if (s2.LengthSquared() > 1e-6) side = Vector3D.Normalize(s2);
                    vdes = cruise + side * (strafeV * strafe);
                }
                else if (scen == "random")
                {
                    if (now >= flipAt) { flipAt = now + flipMin * 0.5 + (flipMax - flipMin) * 0.75 * rng.NextDouble(); vdes = new Vector3D(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * strafeV * 2.6; wdes = new Vector3D(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 0.4; }
                }
                else if (scen == "orbit")
                {
                    Vector3D tan = Vector3D.Normalize(Vector3D.Cross(lu, Vector3D.Up));
                    vdes = sv + tan * strafeV; wdes = Vector3D.Up * 0.05;
                }
                else if (scen == "burn")   // long steady burns in random directions (8-15 s)
                {
                    if (now >= flipAt) { flipAt = now + 8 + 7 * rng.NextDouble(); vdes = Vector3D.Normalize(new Vector3D(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5)) * 100; }
                }
                Vector3D ta = vdes - tv; if (ta.Length() > amax) ta = Vector3D.Normalize(ta) * amax;
                if (ta.Length() < 0.05) ta = Vector3D.Zero;
                tv += ta * dt; if (tv.Length() > 100) tv = Vector3D.Normalize(tv) * 100; tp += tv * dt;
                Vector3D dw = wdes - tw; if (dw.Length() > 0.1 * dt) dw = Vector3D.Normalize(dw) * 0.1 * dt; tw += dw;
                if (tw.Length() > 1e-6) { var R = MatrixD.CreateFromAxisAngle(Vector3D.Normalize(tw), tw.Length() * dt); to.Translation = Vector3D.Zero; to = to * R; }
                MatrixD tInv = MatrixD.Transpose(to);
                double e2 = dist - standoff; double vr = Math.Sign(e2) * Math.Min(Math.Min(Math.Abs(e2) * 0.6, sVmax), Math.Sqrt(2 * sAcc * 0.55 * Math.Abs(e2)));
                if (now > jinkAt) { jinkAt = now + 3 + 3 * rng.NextDouble(); if (rng.NextDouble() < 0.25) orbitSign = -orbitSign; Vector3D r = new Vector3D(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5); r -= Vector3D.Dot(r, lu) * lu; jink = Vector3D.Normalize(r) * jinkV; }
                Vector3D tanS = Vector3D.Normalize(Vector3D.Cross(lu, Vector3D.Up));
                Vector3D want = tv + lu * vr + tanS * orbitV * orbitSign + jink;
                if (want.Length() > sVmax) want = Vector3D.Normalize(want) * sVmax;
                Vector3D sa = (want - sv) * 2; if (sa.Length() > sAcc) sa = Vector3D.Normalize(sa) * sAcc;
                sv += sa * dt; sp += sv * dt;
                if (now >= nextSight)
                {
                    nextSight = now + sightDt;
                    Vector3D aimp = tp + new Vector3D(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * (2 * half.Length() * 0.8);
                    Vector3D rd = Vector3D.Normalize(aimp - sp);
                    Vector3D la = Vector3D.TransformNormal(sp - tp, tInv), ld = Vector3D.TransformNormal(rd, tInv);
                    double th; bool hitR = RayAny(la, ld, out th);
                    Vector3D hullL = la + ld * th;
                    double r = half.Length();
                    foreach (var s in sol)
                    {
                        s.Now = now;
                        double dts = lastSight < 0 ? 0 : now - lastSight;
                        s.T.Ori(to, dts); s.T.Set(tp, tv, r, now);
                        if (hitR)
                        {
                            bool dup = false; foreach (var h in s.T.Hull) if ((h - hullL).LengthSquared() < Math.Min(4, half.LengthSquared() * 0.02)) { dup = true; break; }
                            if (!dup) { if (s.T.Hull.Count < 48) s.T.Hull.Add(hullL); else s.T.Hull[s.T.HullNext++ % 48] = hullL; }
                        }
                    }
                    lastSight = now;
                }
                if (now >= nextShot && now > 3)
                {
                    nextShot = now + shotDt;
                    for (int k = 0; k < nc; k++)
                    {
                        var s = sol[k]; s.Now = now;
                        Vector3D pt, d;
                        if (weapon == "rocket")
                        {
                            if (!s.Lead(sp, sv, out pt, out d)) continue;
                            if (s.LeadT > 4.3 || Vector3D.Distance(pt, sp) > range) continue;
                        }
                        else
                        {
                            s.LeadBullet(sp, sv, out pt, out d);
                            if (Vector3D.Distance(pt, sp) > range * 0.95) continue;
                        }
                        Vector3D pr = new Vector3D(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5); pr -= Vector3D.Dot(pr, d) * d;
                        d = Vector3D.Normalize(d + pr * (spread * 0.0349));
                        var rk = new Rocket { P = sp, D = d, Who = k, U = sv };
                        if (weapon == "rocket") rk.V = truth == 0 ? d * 100 : sv + d * 100; else rk.V = sv + d * bulletV;
                        rockets.Add(rk); shots[k]++;
                        if (DBG && seed == 0 && (k == 0 || k == 1) && shots[k] % 5 == 0) Console.WriteLine(string.Format("t{0:0.0} k{1} dist {2:0} tv {3:0} sv {4:0} ta ({5:0.0},{6:0.0},{7:0.0}) EA ({8:0.0},{9:0.0},{10:0.0}) ManT {11:0.0} segAge {12:0.0} T {13:0.00} aimOff {14:0.0} wide {15}",
                            now, k, dist, tv.Length(), sv.Length(), ta.X, ta.Y, ta.Z, s.T.EA().X, s.T.EA().Y, s.T.EA().Z, s.T.ManT, now - s.T.SegT0, s.LeadT, (pt - (tp + tv * 0)).Length(), s.Wide));
                    }
                }
                foreach (var rk in rockets)
                {
                    if (rk.Done) continue;
                    Vector3D p0 = rk.P;
                    if (weapon == "rocket")
                    {
                        double spd = rk.V.Length();
                        if (truth == 1 || truth == 0)
                        {
                            if (spd < 200) { rk.V += rk.D * 600 * dt; if (rk.V.Length() > 200) rk.V = Vector3D.Normalize(rk.V) * 200; }
                            else rk.V = Vector3D.Normalize(rk.V + rk.D * 600 * dt) * 200;
                        }
                        else { rk.Own = Math.Min(rk.Own + 600 * dt, 200); rk.V = rk.U + rk.D * rk.Own; }
                    }
                    Vector3D p1 = p0 + rk.V * dt;
                    rk.Trav += (p1 - p0).Length(); rk.P = p1;
                    Vector3D l0 = Vector3D.TransformNormal(p0 - tp, tInv), l1 = Vector3D.TransformNormal(p1 - tp, tInv);
                    rk.MinD = Math.Min(rk.MinD, DistAny(l1));
                    if (SegAny(l0, l1)) { rk.Done = true; hits[rk.Who]++; continue; }
                    if (rk.Trav >= range) { rk.Done = true; missSum[rk.Who] += rk.MinD; }
                }
                rockets.RemoveAll(x => x.Done);
            }
        }
        Console.WriteLine(weapon + " " + scen + " size " + size + " amax " + amax + " flips " + flipMin + "-" + flipMax + " standoff " + standoff + (weapon == "rocket" ? " truth " + truth : "") + " seeds " + seeds);
        for (int k = 0; k < nc; k++)
            Console.WriteLine(string.Format("  {0,-28} shots {1,6}  hit {2,5:0.0}%  mean miss {3,5:0.0} m", cfgs[k].Name, shots[k], 100.0 * hits[k] / Math.Max(1, shots[k]), missSum[k] / Math.Max(1, shots[k] - hits[k])));
    }
}
