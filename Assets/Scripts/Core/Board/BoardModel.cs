using System;
using System.Collections.Generic;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// The entire puzzle state: two full grids of cells (front and back of the page), the fold state
    /// that decides which grid is in play per coordinate, and the RNG.
    ///
    /// <para>This class is a state container and a query surface — it holds no resolution logic. Match
    /// finding, gravity, boosters and obstacles are separate services that read and mutate it. That is
    /// what keeps the file reviewable and each rule independently testable.</para>
    ///
    /// <para>Deliberately free of any Unity type so the simulator can run millions of these.</para>
    /// </summary>
    public sealed class BoardModel
    {
        public int Width { get; }
        public int Height { get; }
        public Direction DefaultGravity { get; }
        public FoldTopology Folds { get; }
        public DeterministicRandom Rng { get; }

        private readonly Cell[] _front;
        private readonly Cell[] _back;
        private readonly Dictionary<int, SpawnTable> _spawnTables;
        private readonly List<Walker> _walkers = new List<Walker>();
        private int _nextTileId;

        public BoardModel(int width, int height, FoldTopology folds, DeterministicRandom rng,
            Direction defaultGravity = Direction.Down, IEnumerable<SpawnTable> spawnTables = null)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Board must have positive dimensions.");

            Width = width;
            Height = height;
            DefaultGravity = defaultGravity;
            Folds = folds ?? new FoldTopology(width, height, null);
            Rng = rng ?? new DeterministicRandom(0);

            _front = new Cell[width * height];
            _back = new Cell[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var c = new GridCoord(x, y);
                _front[Index(c)] = new Cell(c, SurfaceSide.Front);
                _back[Index(c)] = new Cell(c, SurfaceSide.Back);
            }

            _spawnTables = new Dictionary<int, SpawnTable>();
            if (spawnTables != null)
            {
                foreach (var t in spawnTables) _spawnTables[t.Group] = t;
            }

            if (!_spawnTables.ContainsKey(0))
            {
                _spawnTables[0] = SpawnTable.Uniform(0, TileColor.Crimson, TileColor.Azure, TileColor.Meadow,
                    TileColor.Amber, TileColor.Violet);
            }
        }

        private int Index(GridCoord c) => c.Y * Width + c.X;

        // ------------------------------------------------------------ queries

        public bool InBounds(GridCoord c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public Cell CellAt(GridCoord c, SurfaceSide side) =>
            InBounds(c) ? (side == SurfaceSide.Front ? _front[Index(c)] : _back[Index(c)]) : null;

        public Cell CellAt(CellRef reference) => CellAt(reference.Coord, reference.Side);

        /// <summary>Which face of the paper is in play at this coordinate right now.</summary>
        public SurfaceSide ActiveSide(GridCoord c) => Folds.SideAt(c);

        /// <summary>The cell the player is currently looking at and interacting with.</summary>
        public Cell ActiveCell(GridCoord c) => CellAt(c, ActiveSide(c));

        public Cell ActiveCell(int x, int y) => ActiveCell(new GridCoord(x, y));

        /// <summary>The face currently hidden behind the page at this coordinate.</summary>
        public Cell HiddenCell(GridCoord c)
        {
            var side = ActiveSide(c);
            return CellAt(c, side == SurfaceSide.Front ? SurfaceSide.Back : SurfaceSide.Front);
        }

        public bool IsActive(CellRef reference) => ActiveSide(reference.Coord) == reference.Side;

        public Direction GravityAt(GridCoord c) => Folds.GravityAt(c, DefaultGravity);

        /// <summary>
        /// Gravity this coordinate would have if the given face were the one in play. Needed when
        /// preparing a surface that is not currently turned up.
        /// </summary>
        public Direction GravityAt(GridCoord c, SurfaceSide side)
        {
            var region = Folds.RegionAt(c);
            return region != null ? region.GravityFor(side) : DefaultGravity;
        }

        /// <summary>Enumerates every coordinate of the board in a stable, deterministic order.</summary>
        public IEnumerable<GridCoord> AllCoords()
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return new GridCoord(x, y);
        }

        /// <summary>Every cell on both surfaces — used by validators and goal counters.</summary>
        public IEnumerable<Cell> AllCells()
        {
            for (int i = 0; i < _front.Length; i++) yield return _front[i];
            for (int i = 0; i < _back.Length; i++) yield return _back[i];
        }

        public IEnumerable<Cell> ActiveCells()
        {
            foreach (var c in AllCoords()) yield return ActiveCell(c);
        }

        // ------------------------------------------------------------ tile factory

        public int PeekNextTileId => _nextTileId;

        public TilePiece CreateTile(TileColor color) => new TilePiece(_nextTileId++, color);

        public TilePiece CreateBooster(BoosterType type, TileColor color, BoosterOrientation orientation)
        {
            var tile = new TilePiece(_nextTileId++, color)
            {
                Booster = type,
                Orientation = orientation
            };
            return tile;
        }

        internal void RestoreNextTileId(int value) => _nextTileId = Math.Max(_nextTileId, value);

        public SpawnTable SpawnTableFor(Cell cell)
        {
            if (cell != null && _spawnTables.TryGetValue(cell.SpawnGroup, out var table)) return table;
            return _spawnTables[0];
        }

        public IEnumerable<SpawnTable> SpawnTables => _spawnTables.Values;

        /// <summary>All colours that can appear on this board — the palette the UI and bots reason about.</summary>
        public List<TileColor> Palette()
        {
            var seen = new List<TileColor>();
            foreach (var table in _spawnTables.Values)
            {
                foreach (var c in table.Colors)
                {
                    if (!seen.Contains(c)) seen.Add(c);
                }
            }

            seen.Sort();
            return seen;
        }

        // ------------------------------------------------------------ walkers

        public IReadOnlyList<Walker> Walkers => _walkers;

        public void AddWalker(Walker walker)
        {
            if (walker != null) _walkers.Add(walker);
        }

        public Walker WalkerById(int id)
        {
            for (int i = 0; i < _walkers.Count; i++)
            {
                if (_walkers[i].Id == id) return _walkers[i];
            }

            return null;
        }

        /// <summary>Can a character stand here? Obstacles of any kind bar the way — that is the puzzle.</summary>
        public bool IsWalkable(GridCoord c, SurfaceSide side)
        {
            if (!InBounds(c)) return false;
            if (ActiveSide(c) != side) return false; // this square is currently showing its other face
            var cell = CellAt(c, side);
            return cell != null && cell.IsPlayable && !cell.HasObstacle;
        }

        // ------------------------------------------------------------ mutation helpers

        /// <summary>
        /// Swaps the pieces living in two cells. Assumes the caller already validated the move —
        /// see <see cref="MoveValidator"/>.
        /// </summary>
        public void SwapTiles(GridCoord a, GridCoord b)
        {
            var ca = ActiveCell(a);
            var cb = ActiveCell(b);
            (ca.Tile, cb.Tile) = (cb.Tile, ca.Tile);
        }

        // ------------------------------------------------------------ cloning

        /// <summary>
        /// Deep copy, used by look-ahead bots and by the editor's "what if" preview. The RNG stream is
        /// copied too, so a cloned board replays identically to the original.
        /// </summary>
        public BoardModel Clone()
        {
            var clone = new BoardModel(Width, Height, Folds.Clone(), Rng.Clone(), DefaultGravity, _spawnTables.Values);
            clone._nextTileId = _nextTileId;
            for (int i = 0; i < _walkers.Count; i++) clone._walkers.Add(_walkers[i].Clone());

            for (int i = 0; i < _front.Length; i++)
            {
                CopyCell(_front[i], clone._front[i]);
                CopyCell(_back[i], clone._back[i]);
            }

            return clone;
        }

        private static void CopyCell(Cell source, Cell target)
        {
            target.Role = source.Role;
            target.IsIllustration = source.IsIllustration;
            target.SpawnGroup = source.SpawnGroup;
            target.Tile = source.Tile?.CloneWithId(source.Tile.Id);
            target.Obstacle = source.Obstacle?.Clone();
            if (source.HasPortal) target.SetPortal(source.PortalTarget);
            else target.ClearPortal();
        }

        // ------------------------------------------------------------ debugging

        /// <summary>Compact ASCII dump of the surface currently in play. Invaluable in test failures.</summary>
        public string ToAscii(bool showHidden = false)
        {
            var sb = new System.Text.StringBuilder();
            for (int y = Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < Width; x++)
                {
                    var coord = new GridCoord(x, y);
                    var cell = showHidden ? HiddenCell(coord) : ActiveCell(coord);
                    sb.Append(AsciiFor(cell));
                }

                if (!showHidden)
                {
                    sb.Append("  ");
                    for (int x = 0; x < Width; x++)
                    {
                        sb.Append(ActiveSide(new GridCoord(x, y)) == SurfaceSide.Front ? '.' : '#');
                    }
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }

        private static char AsciiFor(Cell cell)
        {
            if (cell == null || cell.IsVoid) return ' ';
            if (cell.HasBlockingObstacle)
            {
                switch (cell.Obstacle.Type)
                {
                    case ObstacleType.TornPage: return cell.Obstacle.Health > 1 ? 'T' : 't';
                    case ObstacleType.WaxSeal: return cell.Obstacle.Health > 1 ? 'W' : 'w';
                    case ObstacleType.FoldLock: return 'L';
                    case ObstacleType.ArmourPlate: return (char)('0' + Math.Min(9, cell.Obstacle.Health));
                    default: return '?';
                }
            }

            if (cell.Tile == null) return '_';
            if (cell.Tile.IsBooster)
            {
                switch (cell.Tile.Booster)
                {
                    case BoosterType.RibbonRocket:
                        return cell.Tile.Orientation == BoosterOrientation.Horizontal ? '-' : '|';
                    case BoosterType.InkBloom: return '*';
                    case BoosterType.OrigamiBird: return '^';
                    case BoosterType.PrismBookmark: return '@';
                    case BoosterType.GoldenStitch: return '=';
                }
            }

            if (cell.Tile.IsBlank && cell.Tile.Color == TileColor.None) return '?';

            switch (cell.Tile.Color)
            {
                case TileColor.Crimson: return 'r';
                case TileColor.Azure: return 'b';
                case TileColor.Meadow: return 'g';
                case TileColor.Amber: return 'y';
                case TileColor.Violet: return 'p';
                case TileColor.Blush: return 'k';
                default: return '.';
            }
        }
    }
}
