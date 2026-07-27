using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Simulation
{
    /// <summary>
    /// A resolution listener that only counts. Bots use it to ask "what would this move actually
    /// achieve?" against a cloned board without any of it reaching the real game.
    /// </summary>
    public sealed class CountingListener : IResolutionListener
    {
        public readonly Dictionary<TileColor, int> TilesByColor = new Dictionary<TileColor, int>();
        public readonly Dictionary<ObstacleType, int> ObstaclesByType = new Dictionary<ObstacleType, int>();

        public int TilesCleared;
        public int ObstaclesCleared;
        public int ObstacleHits;
        public int Matches;
        public int SeamMatches;
        public int BoostersCreated;
        public int GoldenStitches;
        public int BoosterDetonations;
        public int IllustrationRestored;
        public int StoryLinkedCleared;

        public void Reset()
        {
            TilesByColor.Clear();
            ObstaclesByType.Clear();
            TilesCleared = 0;
            ObstaclesCleared = 0;
            ObstacleHits = 0;
            Matches = 0;
            SeamMatches = 0;
            BoostersCreated = 0;
            GoldenStitches = 0;
            BoosterDetonations = 0;
            IllustrationRestored = 0;
            StoryLinkedCleared = 0;
        }

        public int TilesOf(TileColor color) => TilesByColor.TryGetValue(color, out int n) ? n : 0;
        public int ObstaclesOf(ObstacleType type) => ObstaclesByType.TryGetValue(type, out int n) ? n : 0;

        public void OnTileCleared(CellRef at, TilePiece tile, ClearCause cause)
        {
            TilesCleared++;
            if (tile == null) return;
            if (tile.StoryLink != 0) StoryLinkedCleared++;
            if (tile.IsBooster || tile.Color == TileColor.None) return;

            TilesByColor.TryGetValue(tile.Color, out int count);
            TilesByColor[tile.Color] = count + 1;
        }

        public void OnObstacleDamaged(CellRef at, Obstacle obstacle, ClearCause cause) => ObstacleHits++;

        public void OnObstacleCleared(CellRef at, Obstacle obstacle)
        {
            ObstaclesCleared++;
            ObstaclesByType.TryGetValue(obstacle.Type, out int count);
            ObstaclesByType[obstacle.Type] = count + 1;
        }

        public void OnMatchResolved(MatchGroup group)
        {
            Matches++;
            if (group.CrossesSeam) SeamMatches++;
        }

        public void OnBoosterCreated(CellRef at, BoosterType type)
        {
            BoostersCreated++;
            if (type == BoosterType.GoldenStitch) GoldenStitches++;
        }

        public void OnBoosterActivated(CellRef at, BoosterType type) => BoosterDetonations++;
    }
}
