using Wonderfold.Core.Level;

namespace Wonderfold.Core.Live
{
    /// <summary>
    /// Turns a finished run into one comparable number: <b>Story Ink</b>.
    ///
    /// <para>A win/lose result is not enough for any of the competitive live modes. Two players who both
    /// beat today's page need to be ranked, and a player racing their own ghost needs to see whether the
    /// new attempt was actually better. The formula is deliberately dominated by <i>moves spared</i> —
    /// that is the thing a better player reliably produces — and then rewards the mechanics this game
    /// wants people to learn: seam matches and the Golden Stitches they mint.</para>
    ///
    /// <para>It lives in the core, next to the stats it reads, so the score a shared replay claims can be
    /// recomputed from the replay itself rather than trusted.</para>
    /// </summary>
    public static class RunScore
    {
        public const int PerMoveSpared = 120;
        public const int PerGoldenStitch = 250;
        public const int PerSeamMatch = 40;
        public const int PerObstacleCleared = 15;
        public const int PerTileCleared = 2;
        public const int WinBonus = 500;

        /// <summary>Zero for a loss: an unfinished page has no score to compare.</summary>
        public static int StoryInk(LevelRunStats stats)
        {
            if (stats == null || stats.Outcome != Primitives.LevelOutcome.Won) return 0;

            return WinBonus
                   + stats.MovesRemaining * PerMoveSpared
                   + stats.GoldenStitches * PerGoldenStitch
                   + stats.SeamMatches * PerSeamMatch
                   + stats.ObstaclesCleared * PerObstacleCleared
                   + stats.TilesCleared * PerTileCleared;
        }

        /// <summary>
        /// The classic three-star grade, expressed against the moves the level actually granted so a
        /// generated page grades on the same curve as an authored one.
        /// </summary>
        public static int Stars(LevelDefinition definition, LevelRunStats stats)
        {
            if (definition == null || stats == null || stats.Outcome != Primitives.LevelOutcome.Won) return 0;
            if (definition.Moves <= 0) return 1;

            float spare = (float)stats.MovesRemaining / definition.Moves;
            if (spare >= 0.30f) return 3;
            if (spare >= 0.12f) return 2;
            return 1;
        }
    }
}
