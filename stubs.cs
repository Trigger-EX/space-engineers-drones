// Stand-in for the SE in-game API (a SUBSET of Sandbox.ModAPI.Ingame / VRage / VRageMath).
// Two jobs:
//   1. compile check of scripts/*.cs (tools/compile_check.sh), and
//   2. the API assembly of the simulator (sim/build.sh builds it as out/sim/SeApi.dll; the engine implements the
//      interfaces, the scripts reference it). VRageMath, MyIni, MyTuple, MyGridProgram and the extension classes therefore
//      carry REAL behaviour here, written to match Keen's VRageMath/Sandbox.Common semantics (row-vector matrices,
//      Forward = -Z, Normalize of a zero vector gives NaN like the game).
// Rule: only members the real API has. Namespaces and member signatures follow the real API (MDK-SE api wiki,
// malforge pbapi, Keen SpaceEngineers source). See docs/script-audit.md and docs/sim-design.md section 4 for what was verified.
// Owner: SLOT B from now on (docs/sim-design.md section 4.1): additions only, each a member the real API has, verified.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace VRageMath {
  public struct Vector2 {
    public float X, Y;
    public static Vector2 Zero = new Vector2(0, 0), One = new Vector2(1, 1);
    public Vector2(float x, float y) { X = x; Y = y; }
    public Vector2(float v) { X = v; Y = v; }
    public float Length() { return (float)Math.Sqrt(X * X + Y * Y); }
    public float LengthSquared() { return X * X + Y * Y; }
    public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.X + b.X, a.Y + b.Y); }
    public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.X - b.X, a.Y - b.Y); }
    public static Vector2 operator -(Vector2 a) { return new Vector2(-a.X, -a.Y); }
    public static Vector2 operator *(Vector2 a, float b) { return new Vector2(a.X * b, a.Y * b); }
    public static Vector2 operator *(float b, Vector2 a) { return new Vector2(a.X * b, a.Y * b); }
    public static bool operator ==(Vector2 a, Vector2 b) { return a.X == b.X && a.Y == b.Y; }
    public static bool operator !=(Vector2 a, Vector2 b) { return !(a == b); }
    public override bool Equals(object o) { return o is Vector2 && this == (Vector2)o; }
    public override int GetHashCode() { return X.GetHashCode() ^ (Y.GetHashCode() * 397); }
    public override string ToString() { return string.Format(CultureInfo.CurrentCulture, "X:{0} Y:{1}", X, Y); }
  }

  public struct Vector3 {
    public float X, Y, Z;
    public static Vector3 Zero = new Vector3(0, 0, 0), One = new Vector3(1, 1, 1);
    public static Vector3 Forward = new Vector3(0, 0, -1), Backward = new Vector3(0, 0, 1), Up = new Vector3(0, 1, 0),
                          Down = new Vector3(0, -1, 0), Left = new Vector3(-1, 0, 0), Right = new Vector3(1, 0, 0);
    public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
    public Vector3(double x, double y, double z) { X = (float)x; Y = (float)y; Z = (float)z; }
    public float Length() { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); }
    public float LengthSquared() { return X * X + Y * Y + Z * Z; }
    public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
    public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
    public static Vector3 operator -(Vector3 a) { return new Vector3(-a.X, -a.Y, -a.Z); }
    public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.X * b, a.Y * b, a.Z * b); }
    public static Vector3 operator *(float b, Vector3 a) { return new Vector3(a.X * b, a.Y * b, a.Z * b); }
    public static Vector3 operator /(Vector3 a, float b) { float i = 1f / b; return new Vector3(a.X * i, a.Y * i, a.Z * i); }
    public static bool operator ==(Vector3 a, Vector3 b) { return a.X == b.X && a.Y == b.Y && a.Z == b.Z; }
    public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
    public static explicit operator Vector3(Vector3D v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }
    public override bool Equals(object o) { return o is Vector3 && this == (Vector3)o; }
    public override int GetHashCode() { return X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 1013); }
    public override string ToString() { return string.Format(CultureInfo.CurrentCulture, "X:{0} Y:{1} Z:{2}", X, Y, Z); }
  }

  public struct Vector3I {
    public int X, Y, Z;
    public static readonly Vector3I Zero = new Vector3I(0, 0, 0), One = new Vector3I(1, 1, 1);
    public static readonly Vector3I Forward = new Vector3I(0, 0, -1), Backward = new Vector3I(0, 0, 1), Up = new Vector3I(0, 1, 0),
                                    Down = new Vector3I(0, -1, 0), Left = new Vector3I(-1, 0, 0), Right = new Vector3I(1, 0, 0);
    public Vector3I(int x, int y, int z) { X = x; Y = y; Z = z; }
    public static Vector3I operator +(Vector3I a, Vector3I b) { return new Vector3I(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
    public static Vector3I operator -(Vector3I a, Vector3I b) { return new Vector3I(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
    public static Vector3I operator -(Vector3I a) { return new Vector3I(-a.X, -a.Y, -a.Z); }
    public static Vector3I operator *(Vector3I a, int b) { return new Vector3I(a.X * b, a.Y * b, a.Z * b); }
    public static bool operator ==(Vector3I a, Vector3I b) { return a.X == b.X && a.Y == b.Y && a.Z == b.Z; }
    public static bool operator !=(Vector3I a, Vector3I b) { return !(a == b); }
    public static implicit operator Vector3D(Vector3I v) { return new Vector3D(v.X, v.Y, v.Z); }
    public static Vector3I Min(Vector3I a, Vector3I b) { return new Vector3I(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)); }
    public static Vector3I Max(Vector3I a, Vector3I b) { return new Vector3I(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)); }
    public override bool Equals(object o) { return o is Vector3I && this == (Vector3I)o; }
    public override int GetHashCode() { return (X * 397 ^ Y) * 397 ^ Z; }
    public override string ToString() { return "[X:" + X + ", Y:" + Y + ", Z:" + Z + "]"; }
  }

  public struct Vector3D {
    public double X, Y, Z;
    public static Vector3D Zero = new Vector3D(0, 0, 0), One = new Vector3D(1, 1, 1);
    public static Vector3D Forward = new Vector3D(0, 0, -1), Backward = new Vector3D(0, 0, 1), Up = new Vector3D(0, 1, 0),
                           Down = new Vector3D(0, -1, 0), Left = new Vector3D(-1, 0, 0), Right = new Vector3D(1, 0, 0);
    public static Vector3D UnitX = new Vector3D(1, 0, 0), UnitY = new Vector3D(0, 1, 0), UnitZ = new Vector3D(0, 0, 1);
    public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    public Vector3D(Vector3 v) { X = v.X; Y = v.Y; Z = v.Z; }
    public double Length() { return Math.Sqrt(X * X + Y * Y + Z * Z); }
    public double LengthSquared() { return X * X + Y * Y + Z * Z; }
    // Like VRageMath: returns the old length; a zero vector becomes NaN.
    public double Normalize() { double l = Math.Sqrt(X * X + Y * Y + Z * Z); double i = 1.0 / l; X *= i; Y *= i; Z *= i; return l; }
    /// <summary>VRageMath: Transform(Forward, CreateFromYawPitchRoll(azimuth, elevation, 0)) = (-sin a cos e, sin e, -cos a cos e).</summary>
    public static void CreateFromAzimuthAndElevation(double azimuth, double elevation, out Vector3D direction) { double ce = Math.Cos(elevation); direction = new Vector3D(-Math.Sin(azimuth) * ce, Math.Sin(elevation), -Math.Cos(azimuth) * ce); }
    public static Vector3D Normalize(Vector3D v) { double i = 1.0 / Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z); return new Vector3D(v.X * i, v.Y * i, v.Z * i); }
    public static double Dot(Vector3D a, Vector3D b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
    public double Dot(Vector3D b) { return X * b.X + Y * b.Y + Z * b.Z; }
    public static Vector3D Cross(Vector3D a, Vector3D b) { return new Vector3D(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
    public Vector3D Cross(Vector3D b) { return Cross(this, b); }
    public static double Distance(Vector3D a, Vector3D b) { double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return Math.Sqrt(x * x + y * y + z * z); }
    public static double DistanceSquared(Vector3D a, Vector3D b) { double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return x * x + y * y + z * z; }
    public static Vector3D TransformNormal(Vector3D v, MatrixD m) {
      return new Vector3D(v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31, v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32, v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33);
    }
    public static Vector3D Transform(Vector3D v, MatrixD m) {
      double x = v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31 + m.M41, y = v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32 + m.M42,
             z = v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33 + m.M43, w = v.X * m.M14 + v.Y * m.M24 + v.Z * m.M34 + m.M44;
      double iw = 1.0 / w; return new Vector3D(x * iw, y * iw, z * iw);
    }
    public static Vector3D Transform(Vector3D v, QuaternionD q) {
      double x2 = q.X + q.X, y2 = q.Y + q.Y, z2 = q.Z + q.Z, wx = q.W * x2, wy = q.W * y2, wz = q.W * z2, xx = q.X * x2, xy = q.X * y2,
             xz = q.X * z2, yy = q.Y * y2, yz = q.Y * z2, zz = q.Z * z2;
      return new Vector3D(v.X * (1 - yy - zz) + v.Y * (xy - wz) + v.Z * (xz + wy), v.X * (xy + wz) + v.Y * (1 - xx - zz) + v.Z * (yz - wx),
                          v.X * (xz - wy) + v.Y * (yz + wx) + v.Z * (1 - xx - yy));
    }
    public static Vector3D Reject(Vector3D v, Vector3D dir) { return v - Dot(v, dir) / dir.LengthSquared() * dir; }
    public static Vector3D ProjectOnVector(ref Vector3D v, ref Vector3D dir) { return Dot(v, dir) / dir.LengthSquared() * dir; }
    public static Vector3D Min(Vector3D a, Vector3D b) { return new Vector3D(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)); }
    public static Vector3D Max(Vector3D a, Vector3D b) { return new Vector3D(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)); }
    public static Vector3D Abs(Vector3D a) { return new Vector3D(Math.Abs(a.X), Math.Abs(a.Y), Math.Abs(a.Z)); }
    public static Vector3D Lerp(Vector3D a, Vector3D b, double t) { return new Vector3D(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t); }
    public static bool IsZero(Vector3D v) { return IsZero(v, 0.0001); }
    public static bool IsZero(Vector3D v, double eps) { return Math.Abs(v.X) < eps && Math.Abs(v.Y) < eps && Math.Abs(v.Z) < eps; }
    public bool IsValid() { return !(double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(Z) || double.IsInfinity(X) || double.IsInfinity(Y) || double.IsInfinity(Z)); }
    public static Vector3D operator +(Vector3D a, Vector3D b) { return new Vector3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
    public static Vector3D operator -(Vector3D a, Vector3D b) { return new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
    public static Vector3D operator -(Vector3D a) { return new Vector3D(-a.X, -a.Y, -a.Z); }
    public static Vector3D operator *(Vector3D a, double b) { return new Vector3D(a.X * b, a.Y * b, a.Z * b); }
    public static Vector3D operator *(double b, Vector3D a) { return new Vector3D(a.X * b, a.Y * b, a.Z * b); }
    public static Vector3D operator *(Vector3D a, Vector3D b) { return new Vector3D(a.X * b.X, a.Y * b.Y, a.Z * b.Z); }
    public static Vector3D operator /(Vector3D a, double b) { double i = 1.0 / b; return new Vector3D(a.X * i, a.Y * i, a.Z * i); }
    public static Vector3D operator /(Vector3D a, Vector3D b) { return new Vector3D(a.X / b.X, a.Y / b.Y, a.Z / b.Z); }
    public static bool operator ==(Vector3D a, Vector3D b) { return a.X == b.X && a.Y == b.Y && a.Z == b.Z; }
    public static bool operator !=(Vector3D a, Vector3D b) { return !(a == b); }
    public static implicit operator Vector3D(Vector3 v) { return new Vector3D(v.X, v.Y, v.Z); }
    public override bool Equals(object o) { return o is Vector3D && this == (Vector3D)o; }
    public override int GetHashCode() { return X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 1013); }
    public override string ToString() { return string.Format(CultureInfo.CurrentCulture, "X:{0} Y:{1} Z:{2}", X, Y, Z); }
  }

  // Row-vector convention like VRageMath: rows are Right (M11..M13), Up (M21..), Backward (M31..), Translation (M41..).
  public struct MatrixD {
    public double M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34, M41, M42, M43, M44;
    public static MatrixD Identity = new MatrixD(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    public MatrixD(double m11, double m12, double m13, double m14, double m21, double m22, double m23, double m24,
                   double m31, double m32, double m33, double m34, double m41, double m42, double m43, double m44) {
      M11 = m11; M12 = m12; M13 = m13; M14 = m14; M21 = m21; M22 = m22; M23 = m23; M24 = m24;
      M31 = m31; M32 = m32; M33 = m33; M34 = m34; M41 = m41; M42 = m42; M43 = m43; M44 = m44;
    }
    public Vector3D Right { get { return new Vector3D(M11, M12, M13); } set { M11 = value.X; M12 = value.Y; M13 = value.Z; } }
    public Vector3D Left { get { return new Vector3D(-M11, -M12, -M13); } set { M11 = -value.X; M12 = -value.Y; M13 = -value.Z; } }
    public Vector3D Up { get { return new Vector3D(M21, M22, M23); } set { M21 = value.X; M22 = value.Y; M23 = value.Z; } }
    public Vector3D Down { get { return new Vector3D(-M21, -M22, -M23); } set { M21 = -value.X; M22 = -value.Y; M23 = -value.Z; } }
    public Vector3D Backward { get { return new Vector3D(M31, M32, M33); } set { M31 = value.X; M32 = value.Y; M33 = value.Z; } }
    public Vector3D Forward { get { return new Vector3D(-M31, -M32, -M33); } set { M31 = -value.X; M32 = -value.Y; M33 = -value.Z; } }
    public Vector3D Translation { get { return new Vector3D(M41, M42, M43); } set { M41 = value.X; M42 = value.Y; M43 = value.Z; } }
    public MatrixD GetOrientation() { MatrixD m = this; m.M41 = m.M42 = m.M43 = 0; m.M14 = m.M24 = m.M34 = 0; m.M44 = 1; return m; }
    public static MatrixD Transpose(MatrixD m) {
      return new MatrixD(m.M11, m.M21, m.M31, m.M41, m.M12, m.M22, m.M32, m.M42, m.M13, m.M23, m.M33, m.M43, m.M14, m.M24, m.M34, m.M44);
    }
    public static MatrixD Multiply(MatrixD a, MatrixD b) {
      MatrixD r;
      r.M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41; r.M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42;
      r.M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43; r.M14 = a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44;
      r.M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41; r.M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42;
      r.M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43; r.M24 = a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44;
      r.M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41; r.M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42;
      r.M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43; r.M34 = a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44;
      r.M41 = a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41; r.M42 = a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42;
      r.M43 = a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43; r.M44 = a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44;
      return r;
    }
    public static MatrixD operator *(MatrixD a, MatrixD b) { return Multiply(a, b); }
    public static MatrixD CreateTranslation(Vector3D p) { MatrixD m = Identity; m.M41 = p.X; m.M42 = p.Y; m.M43 = p.Z; return m; }
    // XNA/VRageMath formula: rotation about 'axis' by 'angle' (right-handed, axis must be unit length).
    public static MatrixD CreateFromAxisAngle(Vector3D axis, double angle) {
      double x = axis.X, y = axis.Y, z = axis.Z, s = Math.Sin(angle), c = Math.Cos(angle), xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z;
      return new MatrixD(xx + c * (1 - xx), xy - c * xy + s * z, xz - c * xz - s * y, 0,
                         xy - c * xy - s * z, yy + c * (1 - yy), yz - c * yz + s * x, 0,
                         xz - c * xz + s * y, yz - c * yz - s * x, zz + c * (1 - zz), 0, 0, 0, 0, 1);
    }
    public static MatrixD CreateWorld(Vector3D position, Vector3D forward, Vector3D up) {
      Vector3D b = Vector3D.Normalize(-forward), r = Vector3D.Normalize(Vector3D.Cross(up, b)), u = Vector3D.Cross(b, r);
      MatrixD m = Identity; m.Right = r; m.Up = u; m.Backward = b; m.Translation = position; return m;
    }
    public static MatrixD CreateFromQuaternion(QuaternionD q) {
      double xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z, xy = q.X * q.Y, zw = q.Z * q.W, zx = q.Z * q.X, yw = q.Y * q.W, yz = q.Y * q.Z, xw = q.X * q.W;
      return new MatrixD(1 - 2 * (yy + zz), 2 * (xy + zw), 2 * (zx - yw), 0, 2 * (xy - zw), 1 - 2 * (zz + xx), 2 * (yz + xw), 0,
                         2 * (zx + yw), 2 * (yz - xw), 1 - 2 * (yy + xx), 0, 0, 0, 0, 1);
    }
    public static MatrixD Invert(MatrixD m) {
      // General 4x4 inverse (cofactors). The engine uses Transpose for pure rotations.
      double a = m.M11, b = m.M12, c = m.M13, d = m.M14, e = m.M21, f = m.M22, g = m.M23, h = m.M24, i = m.M31, j = m.M32, k = m.M33, l = m.M34,
             mm = m.M41, n = m.M42, o = m.M43, p = m.M44;
      double kp = k * p - l * o, jp = j * p - l * n, jo = j * o - k * n, ip = i * p - l * mm, io = i * o - k * mm, iN = i * n - j * mm;
      double a11 = f * kp - g * jp + h * jo, a12 = -(e * kp - g * ip + h * io), a13 = e * jp - f * ip + h * iN, a14 = -(e * jo - f * io + g * iN);
      double det = a * a11 + b * a12 + c * a13 + d * a14, inv = 1.0 / det;
      double gp = g * p - h * o, fp = f * p - h * n, fo = f * o - g * n, ep = e * p - h * mm, eo = e * o - g * mm, en = e * n - f * mm;
      double gl = g * l - h * k, fl = f * l - h * j, fk = f * k - g * j, el = e * l - h * i, ek = e * k - g * i, ej = e * j - f * i;
      return new MatrixD(a11 * inv, -(b * kp - c * jp + d * jo) * inv, (b * gp - c * fp + d * fo) * inv, -(b * gl - c * fl + d * fk) * inv,
                         a12 * inv, (a * kp - c * ip + d * io) * inv, -(a * gp - c * ep + d * eo) * inv, (a * gl - c * el + d * ek) * inv,
                         a13 * inv, -(a * jp - b * ip + d * iN) * inv, (a * fp - b * ep + d * en) * inv, -(a * fl - b * el + d * ej) * inv,
                         a14 * inv, (a * jo - b * io + c * iN) * inv, -(a * fo - b * eo + c * en) * inv, (a * fk - b * ek + c * ej) * inv);
    }
  }

  public struct QuaternionD {
    public double X, Y, Z, W;
    public static QuaternionD Identity = new QuaternionD(0, 0, 0, 1);
    public QuaternionD(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
    public static QuaternionD CreateFromAxisAngle(Vector3D axis, double angle) {
      double s = Math.Sin(angle * 0.5), c = Math.Cos(angle * 0.5); return new QuaternionD(axis.X * s, axis.Y * s, axis.Z * s, c);
    }
    public static QuaternionD CreateFromRotationMatrix(MatrixD m) {
      double tr = m.M11 + m.M22 + m.M33; QuaternionD q;
      if (tr > 0) { double s = Math.Sqrt(tr + 1); q.W = s * 0.5; s = 0.5 / s; q.X = (m.M23 - m.M32) * s; q.Y = (m.M31 - m.M13) * s; q.Z = (m.M12 - m.M21) * s; }
      else if (m.M11 >= m.M22 && m.M11 >= m.M33) { double s = Math.Sqrt(1 + m.M11 - m.M22 - m.M33), h = 0.5 / s; q.X = 0.5 * s; q.Y = (m.M12 + m.M21) * h; q.Z = (m.M13 + m.M31) * h; q.W = (m.M23 - m.M32) * h; }
      else if (m.M22 > m.M33) { double s = Math.Sqrt(1 + m.M22 - m.M11 - m.M33), h = 0.5 / s; q.X = (m.M21 + m.M12) * h; q.Y = 0.5 * s; q.Z = (m.M32 + m.M23) * h; q.W = (m.M31 - m.M13) * h; }
      else { double s = Math.Sqrt(1 + m.M33 - m.M11 - m.M22), h = 0.5 / s; q.X = (m.M31 + m.M13) * h; q.Y = (m.M32 + m.M23) * h; q.Z = 0.5 * s; q.W = (m.M12 - m.M21) * h; }
      return q;
    }
    public void Normalize() { double i = 1.0 / Math.Sqrt(X * X + Y * Y + Z * Z + W * W); X *= i; Y *= i; Z *= i; W *= i; }
    public static QuaternionD Normalize(QuaternionD q) { q.Normalize(); return q; }
    // XNA/VRageMath Quaternion.Multiply formula.
    public static QuaternionD operator *(QuaternionD a, QuaternionD b) {
      return new QuaternionD(a.X * b.W + b.X * a.W + (a.Y * b.Z - a.Z * b.Y), a.Y * b.W + b.Y * a.W + (a.Z * b.X - a.X * b.Z),
                             a.Z * b.W + b.Z * a.W + (a.X * b.Y - a.Y * b.X), a.W * b.W - (a.X * b.X + a.Y * b.Y + a.Z * b.Z));
    }
  }

  public struct BoundingBoxD {
    public Vector3D Min, Max;
    public BoundingBoxD(Vector3D min, Vector3D max) { Min = min; Max = max; }
    public Vector3D Center { get { return (Min + Max) * 0.5; } }
    public Vector3D Size { get { return Max - Min; } }
    public Vector3D HalfExtents { get { return (Max - Min) * 0.5; } }
    public Vector3D Extents { get { return Max - Min; } }
    public bool Intersects(BoundingBoxD b) { return Max.X >= b.Min.X && Min.X <= b.Max.X && Max.Y >= b.Min.Y && Min.Y <= b.Max.Y && Max.Z >= b.Min.Z && Min.Z <= b.Max.Z; }
    public ContainmentType Contains(Vector3D p) {
      return (p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z) ? ContainmentType.Contains : ContainmentType.Disjoint;
    }
  }
  public enum ContainmentType { Disjoint, Contains, Intersects }
  public struct BoundingSphereD {
    public Vector3D Center; public double Radius;
    public BoundingSphereD(Vector3D center, double radius) { Center = center; Radius = radius; }
  }

  public static class MathHelper {
    public const float Pi = 3.141593f, TwoPi = 6.283185f, PiOver2 = 1.570796f, PiOver4 = 0.7853982f;
    public const float RPMToRadiansPerSecond = 0.1047198f, RadiansPerSecondToRPM = 9.549296f;
    public static double Clamp(double v, double a, double b) { v = v > b ? b : v; v = v < a ? a : v; return v; }
    public static float Clamp(float v, float a, float b) { v = v > b ? b : v; v = v < a ? a : v; return v; }
    public static int Clamp(int v, int a, int b) { v = v > b ? b : v; v = v < a ? a : v; return v; }
    public static float ToRadians(float deg) { return deg / 360f * TwoPi; }
    public static double ToRadians(double deg) { return deg / 360.0 * (Math.PI * 2); }
    public static float ToDegrees(float rad) { return rad * 57.29578f; }
    public static double ToDegrees(double rad) { return rad * (180.0 / Math.PI); }
    public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
    public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
  }

  public static class Base6Directions {
    public enum Direction : byte { Forward, Backward, Left, Right, Up, Down }
    public static readonly Direction[] EnumDirections = { Direction.Forward, Direction.Backward, Direction.Left, Direction.Right, Direction.Up, Direction.Down };
    static readonly Vector3I[] IntVectors = { new Vector3I(0, 0, -1), new Vector3I(0, 0, 1), new Vector3I(-1, 0, 0), new Vector3I(1, 0, 0), new Vector3I(0, 1, 0), new Vector3I(0, -1, 0) };
    public static Vector3I GetIntVector(Direction d) { return IntVectors[(int)d]; }
    public static Vector3D GetVector(Direction d) { Vector3I v = IntVectors[(int)d]; return new Vector3D(v.X, v.Y, v.Z); }
    public static Direction GetFlippedDirection(Direction d) { return (Direction)((int)d ^ 1); }
    public static Direction GetDirection(Vector3I v) {
      for (int i = 0; i < 6; i++) if (IntVectors[i] == v) return (Direction)i;
      throw new ArgumentException("not a base direction: " + v);
    }
    // Left = Cross(Up, Forward) in a right-handed system with Forward=-Z, Up=+Y, Left=-X.
    public static Direction GetLeft(Direction up, Direction forward) {
      Vector3I u = IntVectors[(int)up], f = IntVectors[(int)forward];
      return GetDirection(new Vector3I(u.Y * f.Z - u.Z * f.Y, u.Z * f.X - u.X * f.Z, u.X * f.Y - u.Y * f.X));
    }
  }

  public struct MyBlockOrientation {
    public Base6Directions.Direction Forward, Up;
    public static readonly MyBlockOrientation Identity = new MyBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
    public MyBlockOrientation(Base6Directions.Direction forward, Base6Directions.Direction up) { Forward = forward; Up = up; }
    public Base6Directions.Direction Left { get { return Base6Directions.GetLeft(Up, Forward); } }
    // Where the block-local direction d points in grid space.
    public Base6Directions.Direction TransformDirection(Base6Directions.Direction d) {
      switch (d) {
        case Base6Directions.Direction.Forward: return Forward;
        case Base6Directions.Direction.Backward: return Base6Directions.GetFlippedDirection(Forward);
        case Base6Directions.Direction.Up: return Up;
        case Base6Directions.Direction.Down: return Base6Directions.GetFlippedDirection(Up);
        case Base6Directions.Direction.Left: return Left;
        default: return Base6Directions.GetFlippedDirection(Left);
      }
    }
    public override string ToString() { return "[" + Forward + ", " + Up + "]"; }
  }

  public struct Color {
    public byte R, G, B, A;
    public Color(int r, int g, int b) { R = (byte)r; G = (byte)g; B = (byte)b; A = 255; }
    public Color(int r, int g, int b, int a) { R = (byte)r; G = (byte)g; B = (byte)b; A = (byte)a; }
    public static Color White { get { return new Color(255, 255, 255); } }
    public static Color Black { get { return new Color(0, 0, 0); } }
  }
}

namespace VRage {
  // Real MyFixedPoint: fixed point with 6 decimals stored in RawValue.
  public struct MyFixedPoint {
    public long RawValue;
    public static explicit operator MyFixedPoint(double d) { MyFixedPoint r; r.RawValue = (long)(d * 1000000.0); return r; }
    public static explicit operator MyFixedPoint(float d) { MyFixedPoint r; r.RawValue = (long)(d * 1000000.0); return r; }
    public static implicit operator MyFixedPoint(int i) { MyFixedPoint r; r.RawValue = i * 1000000L; return r; }
    public static explicit operator float(MyFixedPoint v) { return v.RawValue / 1000000f; }
    public static explicit operator double(MyFixedPoint v) { return v.RawValue / 1000000.0; }
    public int ToIntSafe() { return (int)(RawValue / 1000000L); }
    public override string ToString() { return (RawValue / 1000000.0).ToString(CultureInfo.InvariantCulture); }
  }
  public struct MyTuple<T1> { public T1 Item1; public MyTuple(T1 a) { Item1 = a; } }
  public struct MyTuple<T1, T2> { public T1 Item1; public T2 Item2; public MyTuple(T1 a, T2 b) { Item1 = a; Item2 = b; } }
  public struct MyTuple<T1, T2, T3> { public T1 Item1; public T2 Item2; public T3 Item3; public MyTuple(T1 a, T2 b, T3 c) { Item1 = a; Item2 = b; Item3 = c; } }
  public struct MyTuple<T1, T2, T3, T4> { public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public MyTuple(T1 a, T2 b, T3 c, T4 d) { Item1 = a; Item2 = b; Item3 = c; Item4 = d; } }
  public struct MyTuple<T1, T2, T3, T4, T5> { public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5; public MyTuple(T1 a, T2 b, T3 c, T4 d, T5 e) { Item1 = a; Item2 = b; Item3 = c; Item4 = d; Item5 = e; } }
  public struct MyTuple<T1, T2, T3, T4, T5, T6> { public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5; public T6 Item6; public MyTuple(T1 a, T2 b, T3 c, T4 d, T5 e, T6 f) { Item1 = a; Item2 = b; Item3 = c; Item4 = d; Item5 = e; Item6 = f; } }
}
namespace VRage.ObjectBuilders {
  // ToString gives "MyObjectBuilder_<Type>/<Subtype>" like the game (scripts test it with Contains()).
  public struct SerializableDefinitionId {
    public string TypeIdString; public string SubtypeName;
    public SerializableDefinitionId(string typeIdString, string subtypeName) { TypeIdString = typeIdString; SubtypeName = subtypeName; }
    public string SubtypeId { get { return SubtypeName ?? ""; } }
    public override string ToString() { return (TypeIdString ?? "") + "/" + (SubtypeName ?? ""); }
  }
}
namespace VRage.Game.Components { public class MyEntityComponentBase {} public class MyEntityComponentContainer { public bool TryGet<T>(out T c) where T: MyEntityComponentBase { c = null; return false; } } }
namespace Sandbox.Game.EntityComponents { internal static class NsAnchor {} }
namespace VRage.Collections { internal static class NsAnchor {} }
namespace VRage.Game.ObjectBuilders.Definitions { internal static class NsAnchor {} }
namespace SpaceEngineers.Game.ModAPI.Ingame {
  using Sandbox.ModAPI.Ingame; using VRage.Game.ModAPI.Ingame;
  public interface IMyShipMergeBlock : IMyFunctionalBlock { bool IsConnected {get;} }
  public interface IMyTurretControlBlock : IMyFunctionalBlock { bool HasTarget {get;} MyDetectedEntityInfo GetTargetedEntity(); bool IsAimed {get;} float Range {get;set;} bool AIEnabled {get;} }
  public interface IMySmallMissileLauncherReload : IMySmallMissileLauncher {}
  // IMyTimerBlock: real name is TriggerDelay (float SECONDS); no Delay member (audit api_fidelity A-3)
  public interface IMyTimerBlock : IMyFunctionalBlock { bool IsCountingDown {get;} bool Silent {get;set;} float TriggerDelay {get;set;} void StartCountdown(); void StopCountdown(); void Trigger(); }
  public interface IMySmallGatlingGun : IMyUserControllableGun {}
  public interface IMyLargeGatlingTurret : IMyLargeConveyorTurretBase {}
  public interface IMyLargeMissileTurret : IMyLargeConveyorTurretBase {}
}
namespace VRage.Game {
  public enum MyCubeSize : byte { Large, Small }
  public struct MyDefinitionId { public static MyDefinitionId Parse(string s){return new MyDefinitionId();} }
  public enum MyRelationsBetweenPlayerAndBlock { NoOwnership, Owner, FactionShare, Neutral, Enemies, Friends }
}
namespace VRage.Game.GUI.TextPanel {
  using VRageMath;
  public enum ContentType { NONE, TEXT_AND_IMAGE, SCRIPT }
  public enum TextAlignment { LEFT, RIGHT, CENTER }
  public enum SpriteType { TEXTURE, TEXT, CLIP_RECT }
  // Real MySprite: Type, Data (texture / text), Position, Size, Color, FontId, Alignment, RotationOrScale (docs: control_arch s2.8).
  public struct MySprite {
    public SpriteType Type; public string Data; public Vector2? Position; public Vector2? Size; public Color? Color; public string FontId; public TextAlignment Alignment; public float RotationOrScale;
    public MySprite(SpriteType type = SpriteType.TEXTURE, string data = null, Vector2? position = null, Vector2? size = null, Color? color = null, string fontId = null, TextAlignment alignment = TextAlignment.CENTER, float rotation = 0f) {
      Type = type; Data = data; Position = position; Size = size; Color = color; FontId = fontId; Alignment = alignment; RotationOrScale = rotation; }
    public static MySprite CreateText(string text, string fontId, Color color, float scale = 1f, TextAlignment alignment = TextAlignment.CENTER) { return new MySprite(SpriteType.TEXT, text, null, null, color, fontId, alignment, scale); }
  }
  public struct MySpriteDrawFrame : IDisposable { public int Count; public void Add(MySprite sprite) { Count++; } public void Dispose() { } }
}
namespace VRage.Game.ModAPI.Ingame {
  using VRageMath; using VRage;
  public interface IMyInventory { MyFixedPoint CurrentVolume {get;} MyFixedPoint MaxVolume {get;} MyFixedPoint CurrentMass {get;} int ItemCount {get;} }
  public interface IMyEntity {
    VRage.Game.Components.MyEntityComponentContainer Components {get;} MatrixD WorldMatrix {get;} long EntityId {get;} Vector3D GetPosition();
    IMyInventory GetInventory(); IMyInventory GetInventory(int i); int InventoryCount {get;} bool HasInventory {get;}
    BoundingSphereD WorldVolume {get;} BoundingBoxD WorldAABB {get;} string DisplayName {get;} string Name {get;} bool Closed {get;}
  }
  public interface IMySlimBlock {
    float BuildIntegrity {get;} float CurrentDamage {get;} float MaxIntegrity {get;} float AccumulatedDamage {get;} float DamageRatio {get;}
    bool IsDestroyed {get;} bool IsFullIntegrity {get;} float Mass {get;} Vector3I Position {get;} IMyCubeBlock FatBlock {get;} IMyCubeGrid CubeGrid {get;}
    VRage.ObjectBuilders.SerializableDefinitionId BlockDefinition {get;}
  }
  public interface IMyCubeGrid : IMyEntity {
    VRage.Game.MyCubeSize GridSizeEnum {get;} float GridSize {get;} bool IsStatic {get;} string CustomName {get;set;}
    Vector3I Min {get;} Vector3I Max {get;} bool CubeExists(Vector3I p); IMySlimBlock GetCubeBlock(Vector3I p);
    Vector3D GridIntegerToWorld(Vector3I gridCoords); Vector3I WorldToGridInteger(Vector3D coords); bool IsSameConstructAs(IMyCubeGrid other);
  }
  public interface IMyCubeBlock : IMyEntity {
    VRage.ObjectBuilders.SerializableDefinitionId BlockDefinition {get;} IMyCubeGrid CubeGrid {get;} bool IsFunctional {get;} bool IsWorking {get;} long OwnerId {get;}
    Vector3I Position {get;} Vector3I Min {get;} Vector3I Max {get;} MyBlockOrientation Orientation {get;} float Mass {get;} int NumberInGrid {get;}
    string DisplayNameText {get;} string DefinitionDisplayNameText {get;} string GetOwnerFactionTag();
  }
  public enum MyDetectedEntityType { None, Unknown, SmallGrid, LargeGrid, CharacterHuman, CharacterOther, FloatingObject, Asteroid, Planet, Meteor, Missile }
  public struct MyDetectedEntityInfo {
    public readonly long EntityId; public readonly string Name; public readonly MyDetectedEntityType Type; public readonly Vector3D? HitPosition;
    public readonly MatrixD Orientation; public readonly Vector3 Velocity; public readonly VRage.Game.MyRelationsBetweenPlayerAndBlock Relationship;
    public readonly BoundingBoxD BoundingBox; public readonly long TimeStamp;
    public MyDetectedEntityInfo(long entityId, string name, MyDetectedEntityType type, Vector3D? hitPosition, MatrixD orientation, Vector3 velocity,
                                VRage.Game.MyRelationsBetweenPlayerAndBlock relationship, BoundingBoxD boundingBox, long timeStamp) {
      EntityId = entityId; Name = name; Type = type; HitPosition = hitPosition; Orientation = orientation; Velocity = velocity;
      Relationship = relationship; BoundingBox = boundingBox; TimeStamp = timeStamp;
    }
    public Vector3D Position { get { return BoundingBox.Center; } }
    public bool IsEmpty() { return TimeStamp == 0; }
  }
}
namespace VRage.Game.ModAPI.Ingame.Utilities {
  // Functional MyIni: sections "[name]", "key=value" lines, ';' comments, "---" ends the INI part. Keys keep insertion order.
  public struct MyIniValue {
    readonly string _value; readonly bool _has;
    public MyIniValue(string value) { _value = value; _has = value != null; }
    public bool IsEmpty { get { return !_has; } }
    public string ToString(string d) { return _has ? _value : d; }
    public override string ToString() { return _has ? _value : ""; }
    public double ToDouble(double d) { double r; return _has && double.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : d; }
    public float ToSingle(float d) { float r; return _has && float.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : d; }
    public int ToInt32(int d) { int r; return _has && int.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : d; }
    public long ToInt64(long d) { long r; return _has && long.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : d; }
    public bool ToBoolean(bool d) {
      if (!_has) return d; string v = _value.Trim().ToLowerInvariant();
      if (v == "true" || v == "yes" || v == "on" || v == "1") return true;
      if (v == "false" || v == "no" || v == "off" || v == "0") return false;
      return d;
    }
  }
  public struct MyIniParseResult {
    public int LineNo; public bool Success; public Exception Error;
    public bool IsDefined { get { return LineNo != 0 || Success; } }
  }
  public class MyIni {
    readonly List<string> _sections = new List<string>();
    readonly Dictionary<string, List<KeyValuePair<string, string>>> _data = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);
    string _end;
    public bool TryParse(string s, out MyIniParseResult r) {
      Clear(); r = new MyIniParseResult(); r.Success = true;
      if (string.IsNullOrEmpty(s)) return true;
      string[] lines = s.Replace("\r\n", "\n").Split('\n'); string cur = null;
      for (int i = 0; i < lines.Length; i++) {
        string t = lines[i].Trim();
        if (t == "---") { _end = string.Join("\n", lines, i + 1, lines.Length - i - 1); break; }
        if (t.Length == 0 || t[0] == ';') continue;
        if (t[0] == '[') {
          int e = t.IndexOf(']'); if (e < 0) { r.LineNo = i + 1; r.Success = false; return false; }
          cur = t.Substring(1, e - 1).Trim(); AddSection(cur); continue;
        }
        int eq = t.IndexOf('='); if (eq <= 0 || cur == null) { r.LineNo = i + 1; r.Success = false; Clear(); return false; }
        Set(cur, t.Substring(0, eq).Trim(), t.Substring(eq + 1).Trim());
      }
      return true;
    }
    public void Clear() { _sections.Clear(); _data.Clear(); _end = null; }
    public string EndContent { get { return _end; } set { _end = value; } }
    void AddSection(string sec) { if (!_data.ContainsKey(sec)) { _sections.Add(sec); _data[sec] = new List<KeyValuePair<string, string>>(); } }
    public bool ContainsSection(string sec) { return _data.ContainsKey(sec); }
    public bool ContainsKey(string sec, string key) { List<KeyValuePair<string, string>> l; if (!_data.TryGetValue(sec, out l)) return false; for (int i = 0; i < l.Count; i++) if (string.Equals(l[i].Key, key, StringComparison.OrdinalIgnoreCase)) return true; return false; }
    public MyIniValue Get(string sec, string key) { List<KeyValuePair<string, string>> l; if (_data.TryGetValue(sec, out l)) for (int i = 0; i < l.Count; i++) if (string.Equals(l[i].Key, key, StringComparison.OrdinalIgnoreCase)) return new MyIniValue(l[i].Value); return new MyIniValue(null); }
    public void Set(string sec, string key, string v) {
      AddSection(sec); var l = _data[sec];
      for (int i = 0; i < l.Count; i++) if (string.Equals(l[i].Key, key, StringComparison.OrdinalIgnoreCase)) { l[i] = new KeyValuePair<string, string>(l[i].Key, v); return; }
      l.Add(new KeyValuePair<string, string>(key, v));
    }
    public void Set(string sec, string key, double v) { Set(sec, key, v.ToString("R", CultureInfo.InvariantCulture)); }
    public void Set(string sec, string key, float v) { Set(sec, key, v.ToString("R", CultureInfo.InvariantCulture)); }
    public void Set(string sec, string key, int v) { Set(sec, key, v.ToString(CultureInfo.InvariantCulture)); }
    public void Set(string sec, string key, long v) { Set(sec, key, v.ToString(CultureInfo.InvariantCulture)); }
    public void Set(string sec, string key, bool v) { Set(sec, key, v ? "true" : "false"); }
    public void Delete(string sec, string key) { List<KeyValuePair<string, string>> l; if (_data.TryGetValue(sec, out l)) l.RemoveAll(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)); }
    public void GetSections(List<string> names) { names.Clear(); names.AddRange(_sections); }
    public override string ToString() {
      var sb = new StringBuilder();
      for (int s = 0; s < _sections.Count; s++) {
        if (s > 0) sb.Append('\n');
        sb.Append('[').Append(_sections[s]).Append("]\n");
        foreach (var kv in _data[_sections[s]]) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
      }
      if (_end != null) sb.Append("---\n").Append(_end);
      return sb.ToString();
    }
  }
}
namespace Sandbox.ModAPI.Interfaces {
  using Sandbox.ModAPI.Ingame; using VRage.Game.ModAPI.Ingame;
  public interface ITerminalProperty { string Id {get;} string TypeName {get;} }
  public interface ITerminalProperty<TValue> : ITerminalProperty {
    TValue GetValue(IMyCubeBlock block); void SetValue(IMyCubeBlock block, TValue value);
    TValue GetDefaultValue(IMyCubeBlock block); TValue GetMinimum(IMyCubeBlock block); TValue GetMaximum(IMyCubeBlock block);
  }
  public interface ITerminalAction { string Id {get;} StringBuilder Name {get;} void Apply(IMyCubeBlock block); bool IsEnabled(IMyCubeBlock block); }
  public static class TerminalPropertyExtensions {
    public static ITerminalProperty<TValue> As<TValue>(this ITerminalProperty property) { return property as ITerminalProperty<TValue>; }
    public static ITerminalProperty<TValue> Cast<TValue>(this ITerminalProperty property) { return (ITerminalProperty<TValue>)property; }
    public static ITerminalProperty<float> AsFloat(this ITerminalProperty property) { return property as ITerminalProperty<float>; }
    public static ITerminalProperty<bool> AsBool(this ITerminalProperty property) { return property as ITerminalProperty<bool>; }
    public static bool Is<TValue>(this ITerminalProperty property) { return property is ITerminalProperty<TValue>; }
    public static T GetValue<T>(this IMyTerminalBlock block, string propertyId) { return block.GetProperty(propertyId).Cast<T>().GetValue(block); }
    public static void SetValue<T>(this IMyTerminalBlock block, string propertyId, T value) { block.GetProperty(propertyId).Cast<T>().SetValue(block, value); }
    public static float GetValueFloat(this IMyTerminalBlock block, string propertyId) { return block.GetValue<float>(propertyId); }
    public static bool GetValueBool(this IMyTerminalBlock block, string propertyId) { return block.GetValue<bool>(propertyId); }
    public static void SetValueFloat(this IMyTerminalBlock block, string propertyId, float value) { block.SetValue<float>(propertyId, value); }
    public static void SetValueBool(this IMyTerminalBlock block, string propertyId, bool value) { block.SetValue<bool>(propertyId, value); }
  }
}
namespace Sandbox.ModAPI.Ingame {
  using VRageMath; using VRage; using VRage.Game.ModAPI.Ingame; using VRage.Game.GUI.TextPanel;
  using Sandbox.ModAPI.Interfaces;
  public interface IMyTerminalBlock : IMyCubeBlock {
    ITerminalProperty GetProperty(string id); ITerminalAction GetActionWithName(string name);
    void GetProperties(List<ITerminalProperty> resultList, Func<ITerminalProperty, bool> collect = null);
    void GetActions(List<ITerminalAction> resultList, Func<ITerminalAction, bool> collect = null);
    string CustomName {get;set;} string CustomData {get;set;} string DetailedInfo {get;} string CustomInfo {get;}
    bool ShowOnHUD {get;set;} bool ShowInTerminal {get;set;} bool IsSameConstructAs(IMyTerminalBlock o);
  }
  public interface IMyFunctionalBlock : IMyTerminalBlock { bool Enabled {get;set;} }
  public interface IMyTextSurface {
    ContentType ContentType {get;set;} float FontSize {get;set;} string Font {get;set;}
    bool WriteText(string s, bool append=false); bool WriteText(StringBuilder s, bool append=false); string GetText();
    MySpriteDrawFrame DrawFrame(); Vector2 SurfaceSize {get;} Vector2 TextureSize {get;}
    Color ScriptBackgroundColor {get;set;} Color ScriptForegroundColor {get;set;} Color FontColor {get;set;} Color BackgroundColor {get;set;} TextAlignment Alignment {get;set;}
  }
  public interface IMyTextSurfaceProvider { IMyTextSurface GetSurface(int index); int SurfaceCount {get;} }
  public interface IMyProgrammableBlock : IMyFunctionalBlock, IMyTextSurfaceProvider { bool IsRunning {get;} bool TryRun(string argument); }
  public struct MyShipVelocities { public Vector3D LinearVelocity; public Vector3D AngularVelocity; public MyShipVelocities(Vector3D linear, Vector3D angular) { LinearVelocity = linear; AngularVelocity = angular; } }
  public struct MyShipMass { public float BaseMass, TotalMass, PhysicalMass; public MyShipMass(float baseMass, float totalMass, float physicalMass) { BaseMass = baseMass; TotalMass = totalMass; PhysicalMass = physicalMass; } }
  public interface IMyShipController : IMyTerminalBlock {
    bool CanControlShip {get;} bool IsUnderControl {get;} bool DampenersOverride {get;set;} bool IsMainCockpit {get;set;} bool ControlThrusters {get;set;}
    Vector3D CenterOfMass {get;} MyShipVelocities GetShipVelocities(); double GetShipSpeed(); Vector3D GetNaturalGravity(); Vector3D GetArtificialGravity();
    Vector3D GetTotalGravity(); MyShipMass CalculateShipMass();
    // Pilot input (docs/design/control_arch.md s2.1): Move x right+, y up+, z backward+ (W = -1); Rotation x = pitch (mouse Y), y = yaw (mouse X); values can be large (clamp).
    Vector3 MoveIndicator {get;} Vector2 RotationIndicator {get;} float RollIndicator {get;} bool HandBrake {get;set;}
  }
  public interface IMyRemoteControl : IMyShipController {}
  public interface IMyCockpit : IMyShipController, IMyTextSurfaceProvider {}
  public enum DoorStatus { Open, Closed, Opening, Closing }
  public interface IMyDoor : IMyFunctionalBlock { DoorStatus Status {get;} void OpenDoor(); void CloseDoor(); void ToggleDoor(); }
  public enum MyShipConnectorStatus { Unconnected, Connectable, Connected }
  public interface IMyShipConnector : IMyFunctionalBlock { bool ThrowOut {get;set;} bool CollectAll {get;set;} MyShipConnectorStatus Status {get;} void Connect(); void Disconnect(); void ToggleConnect(); }
  public interface IMyButtonPanel : IMyFunctionalBlock { bool AnyoneCanUse {get;set;} }
  public interface IMyEventControllerBlock : IMyFunctionalBlock { bool IsAndModeEnabled {get;set;} bool IsLowerOrEqualCondition {get;set;} float Threshold {get;set;} }
  public interface IMyTextPanel : IMyFunctionalBlock, IMyTextSurface {}
  public interface IMyAttachableTopBlock : IMyCubeBlock { bool IsAttached {get;} }
  public interface IMyMechanicalConnectionBlock : IMyFunctionalBlock { IMyAttachableTopBlock Top {get;} IMyCubeGrid TopGrid {get;} bool IsAttached {get;} void Attach(); void Detach(); }
  public interface IMyMotorBase : IMyMechanicalConnectionBlock { bool RotorLock {get;set;} float Torque {get;set;} float BrakingTorque {get;set;} float TargetVelocityRad {get;set;} float TargetVelocityRPM {get;set;} float Displacement {get;set;} }
  public interface IMyMotorStator : IMyMotorBase { float Angle {get;} float LowerLimitRad {get;set;} float UpperLimitRad {get;set;} float LowerLimitDeg {get;set;} float UpperLimitDeg {get;set;} }
  public interface IMyMotorAdvancedStator : IMyMotorStator {}
  public interface IMyGyro : IMyFunctionalBlock { bool GyroOverride {get;set;} float GyroPower {get;set;} float Pitch {get;set;} float Yaw {get;set;} float Roll {get;set;} }
  public interface IMyThrust : IMyFunctionalBlock {
    float ThrustOverride {get;set;} float ThrustOverridePercentage {get;set;} float MaxThrust {get;} float MaxEffectiveThrust {get;}
    float CurrentThrust {get;} float CurrentThrustPercentage {get;} Vector3I GridThrustDirection {get;}
  }
  public interface IMyWarhead : IMyTerminalBlock { bool IsCountingDown {get;} float DetonationTime {get;set;} bool IsArmed {get;set;} bool StartCountdown(); bool StopCountdown(); void Detonate(); }
  public interface IMyProjector : IMyFunctionalBlock { bool IsProjecting {get;} int RemainingBlocks {get;} int TotalBlocks {get;} int BuildableBlocksCount {get;} }
  public interface IMyShipToolBase : IMyFunctionalBlock {}
  public interface IMyShipWelder : IMyShipToolBase {}
  public interface IMyUserControllableGun : IMyFunctionalBlock { bool IsShooting {get;} bool Shoot {get;set;} void ShootOnce(); }
  public interface IMySmallMissileLauncher : IMyUserControllableGun {}
  public interface IMyLargeTurretBase : IMyUserControllableGun { bool HasTarget {get;} MyDetectedEntityInfo GetTargetedEntity(); bool IsAimed {get;} float Range {get;set;} bool AIEnabled {get;}
    float Azimuth {get;} float Elevation {get;} string GetTargetingGroup(); void SetTargetingGroup(string groupSubtypeId); }
  public interface IMyLargeConveyorTurretBase : IMyLargeTurretBase {}
  // AI blocks (audit api_fidelity A-4/A-5). IMyOffensiveCombatStayAtRange is NOT a block: it is the attack-pattern component
  // returned by IMyOffensiveCombatBlock.TryGetSelectedAttackPattern. IsAutoPilotEnabled is get-only. SpeedLimit unit unconfirmed (m/s assumed).
  public enum FlightMode { Patrol, Circle, OneWay }
  public enum OffensiveCombatTargetPriority { Closest, Largest }
  public interface IMyAttackPatternComponent {}
  public interface IMyOffensiveCombatStayAtRange : IMyAttackPatternComponent { float MinimalDistance {get;set;} float MaximalDistance {get;set;} bool EvasiveManeuvers {get;set;} }
  public interface IMyFlightMovementBlock : IMyFunctionalBlock {
    float SpeedLimit {get;set;} FlightMode FlightMode {get;set;} bool PrecisionMode {get;set;} bool CollisionAvoidance {get;set;}
    bool IsAutoPilotEnabled {get;} Vector3D? LookAtPosition {get;} }
  public interface IMyOffensiveCombatBlock : IMyFunctionalBlock {
    long SelectedAttackPattern {get;set;} OffensiveCombatTargetPriority TargetPriority {get;set;} int UpdateTargetInterval {get;set;}
    bool TryGetSelectedAttackPattern(out IMyAttackPatternComponent pattern); }
  public interface IMyCameraBlock : IMyFunctionalBlock {
    bool EnableRaycast {get;set;} double AvailableScanRange {get;} bool IsActive {get;} float RaycastConeLimit {get;} double RaycastDistanceLimit {get;} float RaycastTimeMultiplier {get;}
    bool CanScan(double distance); bool CanScan(double distance, Vector3D direction); bool CanScan(Vector3D target); int TimeUntilScan(double distance);
    MyDetectedEntityInfo Raycast(double d, float pitch=0, float yaw=0); MyDetectedEntityInfo Raycast(Vector3D target); MyDetectedEntityInfo Raycast(double distance, Vector3D direction);
  }
  public interface IMySensorBlock : IMyFunctionalBlock {
    void DetectedEntities(List<MyDetectedEntityInfo> l); MyDetectedEntityInfo LastDetectedEntity {get;} bool IsActive {get;} float MaxRange {get;}
    float LeftExtend {get;set;} float RightExtend {get;set;} float TopExtend {get;set;} float BottomExtend {get;set;} float FrontExtend {get;set;} float BackExtend {get;set;}
  }
  public enum ChargeMode { Auto, Recharge, Discharge }
  public interface IMyPowerProducer : IMyFunctionalBlock { float CurrentOutput {get;} float MaxOutput {get;} }
  public interface IMyBatteryBlock : IMyPowerProducer {
    float CurrentStoredPower {get;} float MaxStoredPower {get;} float CurrentInput {get;} float MaxInput {get;} bool IsCharging {get;} bool HasCapacityRemaining {get;}
    ChargeMode ChargeMode {get;set;}
  }
  public interface IMyRadioAntenna : IMyFunctionalBlock { float Radius {get;set;} bool EnableBroadcasting {get;set;} string HudText {get;set;} bool IsBroadcasting {get;} }
  public interface IMyBlockGroup { string Name {get;} void GetBlocks(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null); }
  public interface IMyGridTerminalSystem {
    void GetBlocks(List<IMyTerminalBlock> blocks);
    void GetBlocksOfType<T>(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null) where T: class;
    void GetBlocksOfType<T>(List<T> l, Func<T,bool> f=null) where T: class;
    void SearchBlocksOfName(string name, List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null);
    IMyTerminalBlock GetBlockWithName(string name); IMyTerminalBlock GetBlockWithId(long id);
    void GetBlockGroups(List<IMyBlockGroup> blockGroups, Func<IMyBlockGroup, bool> collect = null); IMyBlockGroup GetBlockGroupWithName(string name);
  }
  public static class TerminalBlockExtentions {
    public static void ApplyAction(this IMyTerminalBlock b, string a) { ITerminalAction act = b.GetActionWithName(a); if (act != null) act.Apply(b); }
    public static bool HasAction(this IMyTerminalBlock b, string a) { return b.GetActionWithName(a) != null; }
    public static long GetId(this IMyTerminalBlock b) { return b.EntityId; }
  }
  [Flags] public enum UpdateType { None=0, Terminal=1, Trigger=2, Mod=4, Script=8, Update1=16, Update10=32, Update100=64, Once=128, IGC=256 }
  [Flags] public enum UpdateFrequency { None=0, Update1=1, Update10=2, Update100=4, Once=8 }
  public interface IMyGridProgramRuntimeInfo { TimeSpan TimeSinceLastRun {get;} double LastRunTimeMs {get;} UpdateFrequency UpdateFrequency {get;set;}
     int CurrentInstructionCount {get;} int MaxInstructionCount {get;} int CurrentCallChainDepth {get;} int MaxCallChainDepth {get;} }
  public struct MyIGCMessage {
    public readonly object Data; public readonly string Tag; public readonly long Source;
    public MyIGCMessage(object data, string tag, long source) { Data = data; Tag = tag; Source = source; }
    public TData As<TData>() { return (TData)Data; }
  }
  public interface IMyMessageProvider { bool HasPendingMessage {get;} int MaxWaitingMessages {get;} MyIGCMessage AcceptMessage(); void SetMessageCallback(string argument = ""); void DisableMessageCallback(); }
  public interface IMyBroadcastListener : IMyMessageProvider { bool IsActive {get;} string Tag {get;} }
  public interface IMyUnicastListener : IMyMessageProvider {}
  public enum TransmissionDistance { CurrentConstruct, ConnectedConstructs, AntennaRelay }
  public interface IMyIntergridCommunicationSystem {
    long Me {get;} IMyUnicastListener UnicastListener {get;}
    IMyBroadcastListener RegisterBroadcastListener(string t); void DisableBroadcastListener(IMyBroadcastListener l);
    void GetBroadcastListeners(List<IMyBroadcastListener> listeners, Func<IMyBroadcastListener, bool> collect = null);
    bool IsEndpointReachable(long address, TransmissionDistance d = TransmissionDistance.AntennaRelay);
    void SendBroadcastMessage<T>(string tag, T data, TransmissionDistance d = TransmissionDistance.AntennaRelay);
    bool SendUnicastMessage<T>(long addressee, string tag, T data);
  }
  // Real shape: properties with protected setters; the host fills them through Sandbox.ModAPI.IMyGridProgram on an
  // uninitialized instance BEFORE the script constructor runs (FormatterServices.GetUninitializedObject + ctor.Invoke), like the game.
  public abstract class MyGridProgram : Sandbox.ModAPI.IMyGridProgram {
    readonly Action<string, UpdateType> _main2; readonly Action<string> _main1; readonly Action _save;
    Func<IMyIntergridCommunicationSystem> _igc;
    protected MyGridProgram() {
      Type t = GetType(); const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
      MethodInfo m = t.GetMethod("Main", F, null, new[] { typeof(string), typeof(UpdateType) }, null);
      if (m != null) _main2 = (Action<string, UpdateType>)Delegate.CreateDelegate(typeof(Action<string, UpdateType>), this, m);
      else { m = t.GetMethod("Main", F, null, new[] { typeof(string) }, null); if (m != null) _main1 = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), this, m); }
      MethodInfo s = t.GetMethod("Save", F, null, Type.EmptyTypes, null);
      if (s != null) _save = (Action)Delegate.CreateDelegate(typeof(Action), this, s);
    }
    public virtual IMyGridTerminalSystem GridTerminalSystem { get; protected set; }
    public virtual IMyProgrammableBlock Me { get; protected set; }
    public virtual IMyGridProgramRuntimeInfo Runtime { get; protected set; }
    public virtual string Storage { get; protected set; }
    public Action<string> Echo { get; protected set; }
    public IMyIntergridCommunicationSystem IGC { get { return _igc != null ? _igc() : null; } }
    IMyGridTerminalSystem Sandbox.ModAPI.IMyGridProgram.GridTerminalSystem { get { return GridTerminalSystem; } set { GridTerminalSystem = value; } }
    IMyProgrammableBlock Sandbox.ModAPI.IMyGridProgram.Me { get { return Me; } set { Me = value; } }
    IMyGridProgramRuntimeInfo Sandbox.ModAPI.IMyGridProgram.Runtime { get { return Runtime; } set { Runtime = value; } }
    string Sandbox.ModAPI.IMyGridProgram.Storage { get { return Storage; } set { Storage = value; } }
    Action<string> Sandbox.ModAPI.IMyGridProgram.Echo { get { return Echo; } set { Echo = value; } }
    Func<IMyIntergridCommunicationSystem> Sandbox.ModAPI.IMyGridProgram.IGC_ContextGetter { set { _igc = value; } }
    bool Sandbox.ModAPI.IMyGridProgram.HasMainMethod { get { return _main2 != null || _main1 != null; } }
    bool Sandbox.ModAPI.IMyGridProgram.HasSaveMethod { get { return _save != null; } }
    void Sandbox.ModAPI.IMyGridProgram.Main(string argument, UpdateType updateSource) {
      if (_main2 != null) _main2(argument, updateSource); else if (_main1 != null) _main1(argument);
    }
    void Sandbox.ModAPI.IMyGridProgram.Save() { if (_save != null) _save(); }
  }
}
namespace Sandbox.ModAPI {
  using Sandbox.ModAPI.Ingame;
  public interface IMyGridProgram {
    Func<IMyIntergridCommunicationSystem> IGC_ContextGetter { set; }
    IMyGridTerminalSystem GridTerminalSystem {get;set;} IMyProgrammableBlock Me {get;set;} IMyGridProgramRuntimeInfo Runtime {get;set;}
    string Storage {get;set;} Action<string> Echo {get;set;} bool HasMainMethod {get;} bool HasSaveMethod {get;}
    void Main(string argument, UpdateType updateSource); void Save();
  }
}
