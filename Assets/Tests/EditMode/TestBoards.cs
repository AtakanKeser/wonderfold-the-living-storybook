using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;

namespace Wonderfold.Tests
{
    /// <summary>
    /// Builds boards from ASCII so a test reads like the thing it is testing.
    ///
    /// <code>
    /// var board = TestBoards.Build(
    ///     "rrb",
    ///     "gyg",
    ///     "bbr");
    /// </code>
    ///
    /// Rows are written top-first, matching how levels are authored.
    /// </summary>
    public static class TestBoards
    {
        public static BoardModel Build(params string[] rows) => Build(null, 1, rows);

        public static BoardModel Build(IEnumerable<FoldRegion> regions, int seed, params string[] rows)
        {
            int height = rows.Length;
            int width = rows[0].Length;
            var folds = new FoldTopology(width, height, regions);
            var board = new BoardModel(width, height, folds, new DeterministicRandom(seed));

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var cell = board.CellAt(new GridCoord(x, y), SurfaceSide.Front);
                Apply(board, cell, rows[height - 1 - y][x]);
            }

            // Give the back a plain filler layout so folded tests have something to land on.
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var cell = board.CellAt(new GridCoord(x, y), SurfaceSide.Back);
                cell.Role = y == height - 1 ? CellRole.Spawner : CellRole.Playable;
                cell.Tile = board.CreateTile((x + y) % 2 == 0 ? TileColor.Violet : TileColor.Blush);
            }

            return board;
        }

        /// <summary>Overwrites the back surface from ASCII, for tests about folding.</summary>
        public static void PaintBack(BoardModel board, params string[] rows)
        {
            for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++)
            {
                var cell = board.CellAt(new GridCoord(x, y), SurfaceSide.Back);
                cell.Tile = null;
                cell.Obstacle = null;
                Apply(board, cell, rows[board.Height - 1 - y][x]);
            }
        }

        private static void Apply(BoardModel board, Cell cell, char glyph)
        {
            switch (glyph)
            {
                case '#':
                case ' ':
                    cell.Role = CellRole.Void;
                    return;
                case 'S':
                    cell.Role = CellRole.Spawner;
                    return;
                case '_':
                    cell.Role = CellRole.Playable;
                    return;
                case 'T':
                    cell.Role = CellRole.Playable;
                    cell.Obstacle = new Obstacle(ObstacleType.TornPage);
                    return;
                case 'W':
                    cell.Role = CellRole.Playable;
                    cell.Obstacle = new Obstacle(ObstacleType.WaxSeal);
                    return;
                case 'K':
                    cell.Role = CellRole.Playable;
                    cell.Obstacle = new Obstacle(ObstacleType.StoryKnot);
                    cell.Tile = board.CreateTile(TileColor.Meadow);
                    return;
                case '-':
                    cell.Role = CellRole.Playable;
                    cell.Tile = board.CreateBooster(BoosterType.RibbonRocket, TileColor.Crimson,
                        BoosterOrientation.Horizontal);
                    return;
                case '|':
                    cell.Role = CellRole.Playable;
                    cell.Tile = board.CreateBooster(BoosterType.RibbonRocket, TileColor.Crimson,
                        BoosterOrientation.Vertical);
                    return;
                case '*':
                    cell.Role = CellRole.Playable;
                    cell.Tile = board.CreateBooster(BoosterType.InkBloom, TileColor.Crimson, BoosterOrientation.None);
                    return;
            }

            cell.Role = CellRole.Playable;
            switch (glyph)
            {
                case 'r': cell.Tile = board.CreateTile(TileColor.Crimson); break;
                case 'b': cell.Tile = board.CreateTile(TileColor.Azure); break;
                case 'g': cell.Tile = board.CreateTile(TileColor.Meadow); break;
                case 'y': cell.Tile = board.CreateTile(TileColor.Amber); break;
                case 'p': cell.Tile = board.CreateTile(TileColor.Violet); break;
                case 'k': cell.Tile = board.CreateTile(TileColor.Blush); break;
            }
        }

        public static GameRules Rules() => new GameRules { AvoidInstantMatchSpawns = false };

        /// <summary>Total tiles on both faces — the invariant most property tests hang off.</summary>
        public static int CountTiles(BoardModel board)
        {
            int count = 0;
            foreach (var cell in board.AllCells())
            {
                if (cell.Tile != null) count++;
            }

            return count;
        }

        public static int CountTilesOnActiveSurface(BoardModel board)
        {
            int count = 0;
            foreach (var coord in board.AllCoords())
            {
                if (board.ActiveCell(coord)?.Tile != null) count++;
            }

            return count;
        }
    }
}
