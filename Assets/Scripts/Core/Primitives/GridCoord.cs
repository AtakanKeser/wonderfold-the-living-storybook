using System;

namespace Wonderfold.Core.Primitives
{
    /// <summary>
    /// Integer grid coordinate. Deliberately Unity-free so the whole gameplay core can be
    /// compiled and unit-tested outside the editor.
    /// Origin is bottom-left: +X is right, +Y is up.
    /// </summary>
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public readonly int X;
        public readonly int Y;

        public GridCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static readonly GridCoord Invalid = new GridCoord(int.MinValue, int.MinValue);

        public bool IsValid => X != int.MinValue;

        public GridCoord Step(Direction direction, int distance = 1)
        {
            var delta = direction.ToDelta();
            return new GridCoord(X + delta.X * distance, Y + delta.Y * distance);
        }

        public static GridCoord operator +(GridCoord a, GridCoord b) => new GridCoord(a.X + b.X, a.Y + b.Y);
        public static GridCoord operator -(GridCoord a, GridCoord b) => new GridCoord(a.X - b.X, a.Y - b.Y);

        public int ManhattanTo(GridCoord other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public bool IsOrthogonalNeighbourOf(GridCoord other) => ManhattanTo(other) == 1;

        public bool Equals(GridCoord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
        public static bool operator ==(GridCoord a, GridCoord b) => a.Equals(b);
        public static bool operator !=(GridCoord a, GridCoord b) => !a.Equals(b);
        public override string ToString() => $"({X},{Y})";
    }
}
