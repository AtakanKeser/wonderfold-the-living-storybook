using Wonderfold.Core.Board;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Boosters
{
    /// <summary>
    /// Lets the level steer "smart" boosters without the board knowing what a goal is.
    ///
    /// <para>The Origami Bird has to pick something to fly at, and a Prism Bookmark fired on its own has
    /// to pick a colour. Both questions only have good answers if you know the objective, so the level
    /// supplies this and the board just asks.</para>
    /// </summary>
    public interface IBoosterTargetOracle
    {
        /// <summary>Higher is more desirable. Return <see cref="int.MinValue"/> to exclude a cell.</summary>
        int ScoreTarget(BoardModel board, GridCoord coord);

        /// <summary>Colour a colour-clearing booster should choose when nothing else decided for it.</summary>
        TileColor PreferredColor(BoardModel board);
    }

    /// <summary>
    /// Goal-agnostic fallback: go for the toughest obstacle, then anything covering the hidden
    /// illustration, then the busiest colour. Good enough that a level with no oracle still feels smart.
    /// </summary>
    public sealed class DefaultBoosterTargetOracle : IBoosterTargetOracle
    {
        public static readonly DefaultBoosterTargetOracle Instance = new DefaultBoosterTargetOracle();

        public int ScoreTarget(BoardModel board, GridCoord coord)
        {
            var cell = board.ActiveCell(coord);
            if (cell == null || cell.IsVoid) return int.MinValue;

            int score = 0;
            if (cell.HasObstacle)
            {
                score += 100 + cell.Obstacle.Health * 10;
                if (cell.Obstacle.Type == ObstacleType.WaxSeal || cell.Obstacle.Type == ObstacleType.FoldLock)
                    score += 60; // these cannot be chipped any other way
            }

            if (cell.IsIllustration && cell.HasObstacle) score += 40;

            var hidden = board.HiddenCell(coord);
            if (hidden != null && hidden.HasObstacle) score += 15;

            if (cell.Tile != null && cell.Tile.IsBooster) score -= 50; // let the player use it themselves
            return score;
        }

        public TileColor PreferredColor(BoardModel board)
        {
            var counts = new int[16];
            foreach (var coord in board.AllCoords())
            {
                var color = board.ActiveCell(coord).MatchColor;
                if (color != TileColor.None) counts[(int)color]++;
            }

            int best = 0;
            var bestColor = TileColor.None;
            for (int i = 1; i < counts.Length; i++)
            {
                if (counts[i] > best)
                {
                    best = counts[i];
                    bestColor = (TileColor)i;
                }
            }

            return bestColor;
        }
    }
}
