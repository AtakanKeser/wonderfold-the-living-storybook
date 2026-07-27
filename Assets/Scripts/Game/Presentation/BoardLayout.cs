using UnityEngine;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Game.Presentation
{
    /// <summary>
    /// The single place board coordinates become world positions.
    ///
    /// <para>Note that render position is simply logical position: the fold is animated as a rotation of
    /// the page, never as a rearrangement of the grid. See <c>SurfaceMapper</c> in the core for why
    /// mirroring the content would break seam matches, which are the point of the mechanic.</para>
    /// </summary>
    public sealed class BoardLayout
    {
        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 Origin { get; }

        public BoardLayout(int width, int height, float cellSize, Vector3 centre)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            Origin = centre - new Vector3((width - 1) * 0.5f * cellSize, (height - 1) * 0.5f * cellSize, 0f);
        }

        public Vector3 WorldOf(GridCoord coord) =>
            Origin + new Vector3(coord.X * CellSize, coord.Y * CellSize, 0f);

        public Vector3 WorldOf(int x, int y) => WorldOf(new GridCoord(x, y));

        /// <summary>Nearest cell to a world point. Returns an out-of-range coord when the point misses.</summary>
        public GridCoord CoordOf(Vector3 world)
        {
            var local = world - Origin;
            int x = Mathf.RoundToInt(local.x / CellSize);
            int y = Mathf.RoundToInt(local.y / CellSize);
            return new GridCoord(x, y);
        }

        public bool Contains(GridCoord coord) =>
            coord.X >= 0 && coord.Y >= 0 && coord.X < Width && coord.Y < Height;

        public Bounds WorldBounds =>
            new Bounds(Origin + new Vector3((Width - 1) * 0.5f * CellSize, (Height - 1) * 0.5f * CellSize, 0f),
                new Vector3(Width * CellSize, Height * CellSize, 0.1f));

        /// <summary>
        /// Cell size that makes a board of this shape fill the given screen fraction. Called on every
        /// orientation change so the layout survives rotation and odd aspect ratios.
        /// </summary>
        public static float FitCellSize(Camera camera, int width, int height, float horizontalFill = 0.92f,
            float verticalFill = 0.62f)
        {
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;

            float byWidth = halfWidth * 2f * horizontalFill / width;
            float byHeight = halfHeight * 2f * verticalFill / height;
            return Mathf.Min(byWidth, byHeight);
        }
    }
}
