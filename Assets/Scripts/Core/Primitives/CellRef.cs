using System;

namespace Wonderfold.Core.Primitives
{
    /// <summary>
    /// Addresses one physical cell: a coordinate on a specific side of the page.
    /// Board events always carry a <see cref="CellRef"/> so the view knows which surface to animate.
    /// </summary>
    public readonly struct CellRef : IEquatable<CellRef>
    {
        public readonly GridCoord Coord;
        public readonly SurfaceSide Side;

        public CellRef(GridCoord coord, SurfaceSide side)
        {
            Coord = coord;
            Side = side;
        }

        public CellRef(int x, int y, SurfaceSide side) : this(new GridCoord(x, y), side) { }

        public int X => Coord.X;
        public int Y => Coord.Y;

        /// <summary>The same coordinate on the opposite side of the paper.</summary>
        public CellRef Twin => new CellRef(Coord, Side == SurfaceSide.Front ? SurfaceSide.Back : SurfaceSide.Front);

        public bool Equals(CellRef other) => Coord.Equals(other.Coord) && Side == other.Side;
        public override bool Equals(object obj) => obj is CellRef other && Equals(other);
        public override int GetHashCode() => unchecked((Coord.GetHashCode() * 397) ^ (int)Side);
        public static bool operator ==(CellRef a, CellRef b) => a.Equals(b);
        public static bool operator !=(CellRef a, CellRef b) => !a.Equals(b);
        public override string ToString() => $"{Coord}{(Side == SurfaceSide.Front ? "F" : "B")}";
    }
}
