using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Level
{
    /// <summary>
    /// Turns authored level text into a live <see cref="BoardModel"/>.
    ///
    /// <para>Layers are read top row first so a level file reads the way the board looks, and flipped
    /// into the board's bottom-up coordinates here — the one place that conversion is allowed to happen.
    /// </para>
    /// </summary>
    public static class LevelBuilder
    {
        public static BoardModel Build(LevelDefinition definition, int seed)
        {
            var regions = new List<FoldRegion>();
            for (int i = 0; i < definition.FoldRegions.Count; i++) regions.Add(definition.FoldRegions[i].Create());

            var folds = new FoldTopology(definition.Width, definition.Height, regions);

            var tables = new List<SpawnTable>();
            for (int i = 0; i < definition.SpawnTables.Count; i++) tables.Add(definition.SpawnTables[i].Create());

            var board = new BoardModel(definition.Width, definition.Height, folds,
                new DeterministicRandom(seed), definition.DefaultGravity, tables);

            ApplySurface(board, definition, SurfaceSide.Front, definition.Front);
            ApplySurface(board, definition, SurfaceSide.Back, definition.Back);
            ApplyPortals(board, definition);

            return board;
        }

        private static void ApplySurface(BoardModel board, LevelDefinition definition, SurfaceSide side,
            SurfaceSpec spec)
        {
            bool anyExplicitSpawner = false;

            for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++)
            {
                var coord = new GridCoord(x, y);
                var cell = board.CellAt(coord, side);
                int row = board.Height - 1 - y;

                char tileGlyph = Glyph(spec.Tiles, row, x, LevelGlyphs.Empty);
                if (tileGlyph == LevelGlyphs.Void || tileGlyph == ' ')
                {
                    cell.Role = CellRole.Void;
                    continue;
                }

                if (tileGlyph == LevelGlyphs.Spawner)
                {
                    cell.Role = CellRole.Spawner;
                    anyExplicitSpawner = true;
                }
                else
                {
                    cell.Role = CellRole.Playable;
                }

                if (LevelGlyphs.TryColor(tileGlyph, out var color))
                {
                    cell.Tile = board.CreateTile(color);
                }
                else if (tileGlyph == LevelGlyphs.BlankTile)
                {
                    cell.Tile = board.CreateTile(TileColor.None);
                    cell.Tile.IsBlank = true;
                }
                else if (LevelGlyphs.TryBooster(tileGlyph, out var booster, out var orientation))
                {
                    cell.Tile = board.CreateBooster(booster, TileColor.None, orientation);
                }

                char obstacleGlyph = Glyph(spec.Obstacles, row, x, LevelGlyphs.Empty);
                if (LevelGlyphs.TryObstacle(obstacleGlyph, out var obstacleType))
                {
                    int group = Digit(Glyph(spec.Groups, row, x, '0'));
                    cell.Obstacle = new Obstacle(obstacleType, 0, group);
                    // A blocking obstacle fills the cell; nothing can also be sitting there.
                    if (cell.Obstacle.IsBlocking) cell.Tile = null;
                }

                if (Glyph(spec.Illustration, row, x, LevelGlyphs.Empty) == '*') cell.IsIllustration = true;

                cell.SpawnGroup = Digit(Glyph(spec.SpawnGroups, row, x, '0'));

                int link = Digit(Glyph(spec.StoryLinks, row, x, '0'));
                if (link > 0 && cell.Tile != null) cell.Tile.StoryLink = link;
            }

            if (!anyExplicitSpawner) AutoMarkSpawners(board, definition, side);
        }

        /// <summary>
        /// When a level does not place spawners by hand, any cell that nothing could ever flow into
        /// becomes one. Designers only write 'S' for the unusual cases.
        ///
        /// <para>"Nothing could flow in" covers three situations: the upstream neighbour is off the page,
        /// it is a hole, or it falls in a different direction. That last one is what keeps a fold region
        /// alive after it turns over and starts pulling sideways — its upstream edge sits in the middle
        /// of the board, where without this rule it would quietly drain and never refill.</para>
        /// </summary>
        private static void AutoMarkSpawners(BoardModel board, LevelDefinition definition, SurfaceSide side)
        {
            for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++)
            {
                var coord = new GridCoord(x, y);
                var cell = board.CellAt(coord, side);
                if (cell.IsVoid) continue;

                var gravity = board.GravityAt(coord, side);
                var upstream = coord.Step(gravity.Opposite());

                bool nothingCanFlowIn =
                    !board.InBounds(upstream) ||
                    board.CellAt(upstream, side).IsVoid ||
                    board.GravityAt(upstream, side) != gravity;

                if (nothingCanFlowIn) cell.Role = CellRole.Spawner;
            }
        }

        private static void ApplyPortals(BoardModel board, LevelDefinition definition)
        {
            for (int i = 0; i < definition.Portals.Count; i++)
            {
                var spec = definition.Portals[i];
                var from = board.CellAt(new GridCoord(spec.FromX, spec.FromY), spec.FromSide);
                if (from == null) continue;
                from.SetPortal(new CellRef(new GridCoord(spec.ToX, spec.ToY), spec.ToSide));
            }
        }

        private static char Glyph(List<string> layer, int row, int column, char fallback)
        {
            if (layer == null || row < 0 || row >= layer.Count) return fallback;
            var line = layer[row];
            if (line == null || column < 0 || column >= line.Length) return fallback;
            return line[column];
        }

        private static int Digit(char c) => c >= '0' && c <= '9' ? c - '0' : 0;
    }
}
