using System;
using System.Collections.Generic;

namespace Wonderfold.Core.Primitives
{
    /// <summary>Axis-aligned integer rectangle. Min-inclusive, max-exclusive.</summary>
    public readonly struct GridRect : IEquatable<GridRect>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public GridRect(int x, int y, int width, int height)
        {
            if (width < 0 || height < 0)
                throw new ArgumentException("GridRect size cannot be negative.");
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int MinX => X;
        public int MinY => Y;
        public int MaxX => X + Width;   // exclusive
        public int MaxY => Y + Height;  // exclusive
        public int Area => Width * Height;
        public bool IsEmpty => Width == 0 || Height == 0;

        public bool Contains(GridCoord c) => c.X >= X && c.X < MaxX && c.Y >= Y && c.Y < MaxY;

        public bool Overlaps(GridRect other) =>
            X < other.MaxX && other.X < MaxX && Y < other.MaxY && other.Y < MaxY;

        public IEnumerable<GridCoord> Coords()
        {
            for (int y = Y; y < MaxY; y++)
            for (int x = X; x < MaxX; x++)
                yield return new GridCoord(x, y);
        }

        public bool Equals(GridRect other) =>
            X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

        public override bool Equals(object obj) => obj is GridRect other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = X;
                h = (h * 397) ^ Y;
                h = (h * 397) ^ Width;
                h = (h * 397) ^ Height;
                return h;
            }
        }

        public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
    }
}
