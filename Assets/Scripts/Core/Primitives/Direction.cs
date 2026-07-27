using System;

namespace Wonderfold.Core.Primitives
{
    /// <summary>Four-way orthogonal direction. Also used as a gravity direction.</summary>
    public enum Direction
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3
    }

    public enum Axis
    {
        Horizontal = 0,
        Vertical = 1
    }

    public static class DirectionExtensions
    {
        private static readonly GridCoord[] Deltas =
        {
            new GridCoord(0, 1),   // Up
            new GridCoord(1, 0),   // Right
            new GridCoord(0, -1),  // Down
            new GridCoord(-1, 0)   // Left
        };

        public static GridCoord ToDelta(this Direction direction) => Deltas[(int)direction];

        public static Direction Opposite(this Direction direction) => (Direction)(((int)direction + 2) & 3);

        public static Direction RotateCw(this Direction direction) => (Direction)(((int)direction + 1) & 3);

        public static Direction RotateCcw(this Direction direction) => (Direction)(((int)direction + 3) & 3);

        public static Axis ToAxis(this Direction direction) =>
            direction == Direction.Up || direction == Direction.Down ? Axis.Vertical : Axis.Horizontal;

        /// <summary>The two directions perpendicular to <paramref name="direction"/>.</summary>
        public static void Perpendiculars(this Direction direction, out Direction a, out Direction b)
        {
            a = direction.RotateCw();
            b = direction.RotateCcw();
        }

        public static Axis Other(this Axis axis) => axis == Axis.Horizontal ? Axis.Vertical : Axis.Horizontal;

        public static Direction ToDirection(this Axis axis) =>
            axis == Axis.Horizontal ? Direction.Right : Direction.Up;

        public static Direction FromDelta(int dx, int dy)
        {
            if (dx == 0 && dy == 1) return Direction.Up;
            if (dx == 1 && dy == 0) return Direction.Right;
            if (dx == 0 && dy == -1) return Direction.Down;
            if (dx == -1 && dy == 0) return Direction.Left;
            throw new ArgumentException($"Delta ({dx},{dy}) is not an orthogonal unit step.");
        }
    }
}
