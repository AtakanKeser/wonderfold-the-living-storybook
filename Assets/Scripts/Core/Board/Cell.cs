using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// One cell on one side of the page. Every logical coordinate owns exactly two of these —
    /// a Front cell and a Back cell — and folding decides which of the pair is currently in play.
    /// </summary>
    public sealed class Cell
    {
        public GridCoord Coord { get; }
        public SurfaceSide Side { get; }
        public CellRef Ref => new CellRef(Coord, Side);

        public CellRole Role { get; set; }

        public TilePiece Tile { get; set; }
        public Obstacle Obstacle { get; set; }

        /// <summary>
        /// Part of the hidden picture revealed by "Restore the Illustration" goals.
        /// Counts as restored once the cell holds no obstacle.
        /// </summary>
        public bool IsIllustration { get; set; }

        /// <summary>
        /// Which spawn table this cell draws from when it refills. Levels use this to bias
        /// regions towards specific colours (e.g. the moonlight machinery on the back surface).
        /// </summary>
        public int SpawnGroup { get; set; }

        /// <summary>Portal destination. Invalid when the cell is not a Misprinted Portal mouth.</summary>
        public CellRef PortalTarget { get; set; }
        public bool HasPortal { get; private set; }

        public Cell(GridCoord coord, SurfaceSide side, CellRole role = CellRole.Playable)
        {
            Coord = coord;
            Side = side;
            Role = role;
            PortalTarget = new CellRef(GridCoord.Invalid, side);
        }

        public void SetPortal(CellRef target)
        {
            PortalTarget = target;
            HasPortal = target.Coord.IsValid;
        }

        public void ClearPortal()
        {
            HasPortal = false;
            PortalTarget = new CellRef(GridCoord.Invalid, Side);
        }

        public bool IsVoid => Role == CellRole.Void;
        public bool IsPlayable => Role != CellRole.Void;
        public bool IsSpawner => Role == CellRole.Spawner;

        public bool HasObstacle => Obstacle != null;
        public bool HasBlockingObstacle => Obstacle != null && Obstacle.IsBlocking;
        public bool HasOverlayObstacle => Obstacle != null && Obstacle.IsOverlay;

        /// <summary>True when a tile could physically rest here right now.</summary>
        public bool CanHoldTile => IsPlayable && !HasBlockingObstacle;

        /// <summary>True when a falling tile may pass through / land in this cell.</summary>
        public bool IsFallThrough => CanHoldTile && Tile == null;

        /// <summary>True when the player is allowed to drag the tile living here.</summary>
        public bool IsSwappable =>
            Tile != null && IsPlayable && (Obstacle == null || !ObstacleCatalog.BlocksSwap(Obstacle.Type));

        /// <summary>The colour the match finder should see. None = never matches.</summary>
        public TileColor MatchColor => Tile != null && Tile.IsMatchable ? Tile.Color : TileColor.None;

        public void Clear()
        {
            Tile = null;
        }

        public override string ToString() =>
            $"{Ref} {Role} tile={(Tile?.ToString() ?? "-")} obs={(Obstacle?.ToString() ?? "-")}";
    }
}
