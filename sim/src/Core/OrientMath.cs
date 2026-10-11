// SLOT A. Block orientation maths (blueprint BlockOrientation Forward/Up -> local axes). Implemented by the architect.
// Grid frame: +X Right, +Y Up, +Z Backward (Forward = -Z), cell (x,y,z) has its CENTER at (x,y,z)*cellSize.
// A block's rotation matrix R (row-vector, VRageMath) has rows Right, Up, Backward of the block expressed in grid axes:
//   R.Forward = dir(BlockOrientation.Forward), R.Up = dir(BlockOrientation.Up), R.Right = Cross(R.Forward, R.Up).
// A block-local vector v maps to grid space as Vector3D.TransformNormal(v, R).
// Definition size (size_cells x,y,z) is along block-local X (right), Y (up), Z (backward); grid extent = |TransformNormal(size, R)|.
// Blueprint <Min> is the minimum cell; Max = Min + extent - 1; block center (grid-local metres) = (Min + Max) / 2 * cellSize.
using System;
using VRageMath;

namespace SeSim.Core
{
    public static class OrientMath
    {
        public static Base6Directions.Direction ParseDir(string s)
        {
            switch (s)
            {
                case "Forward": return Base6Directions.Direction.Forward;
                case "Backward": return Base6Directions.Direction.Backward;
                case "Left": return Base6Directions.Direction.Left;
                case "Right": return Base6Directions.Direction.Right;
                case "Up": return Base6Directions.Direction.Up;
                case "Down": return Base6Directions.Direction.Down;
            }
            throw new FormatException("bad Base6 direction: " + s);
        }

        /// <summary>Rotation part of the block's local matrix (no translation).</summary>
        public static MatrixD Rotation(MyBlockOrientation o)
        {
            Vector3D f = Base6Directions.GetVector(o.Forward), u = Base6Directions.GetVector(o.Up);
            Vector3D r = Vector3D.Cross(f, u);
            MatrixD m = MatrixD.Identity;
            m.Right = r; m.Up = u; m.Forward = f;
            return m;
        }

        public static Vector3I RotatedSize(Vector3I size, MyBlockOrientation o)
        {
            Vector3D v = Vector3D.TransformNormal(new Vector3D(size.X, size.Y, size.Z), Rotation(o));
            return new Vector3I((int)Math.Round(Math.Abs(v.X)), (int)Math.Round(Math.Abs(v.Y)), (int)Math.Round(Math.Abs(v.Z)));
        }

        public static Vector3I MaxCell(Vector3I min, Vector3I size, MyBlockOrientation o)
        {
            Vector3I e = RotatedSize(size, o);
            return new Vector3I(min.X + e.X - 1, min.Y + e.Y - 1, min.Z + e.Z - 1);
        }

        public static Vector3D CenterLocal(Vector3I min, Vector3I max, double cell)
        {
            return new Vector3D((min.X + max.X) * 0.5 * cell, (min.Y + max.Y) * 0.5 * cell, (min.Z + max.Z) * 0.5 * cell);
        }

        /// <summary>Block local matrix in grid space: rotation + translation to the block center.</summary>
        public static MatrixD LocalMatrix(MyBlockOrientation o, Vector3I min, Vector3I max, double cell)
        {
            MatrixD m = Rotation(o);
            m.Translation = CenterLocal(min, max, cell);
            return m;
        }
    }
}
