using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// How the board tells the level about things that happened to it.
    ///
    /// <para>Goals, the fold meter and the boss health bar all live above the board and subscribe
    /// through this one interface. The board itself has no idea what a level objective is, which is why
    /// a new goal type never requires touching the resolution code.</para>
    /// </summary>
    public interface IResolutionListener
    {
        void OnTileCleared(CellRef at, TilePiece tile, ClearCause cause);
        void OnObstacleDamaged(CellRef at, Obstacle obstacle, ClearCause cause);
        void OnObstacleCleared(CellRef at, Obstacle obstacle);
        void OnMatchResolved(MatchGroup group);
        void OnBoosterCreated(CellRef at, BoosterType type);
        void OnBoosterActivated(CellRef at, BoosterType type);
    }

    public sealed class NullResolutionListener : IResolutionListener
    {
        public static readonly NullResolutionListener Instance = new NullResolutionListener();
        public void OnTileCleared(CellRef at, TilePiece tile, ClearCause cause) { }
        public void OnObstacleDamaged(CellRef at, Obstacle obstacle, ClearCause cause) { }
        public void OnObstacleCleared(CellRef at, Obstacle obstacle) { }
        public void OnMatchResolved(MatchGroup group) { }
        public void OnBoosterCreated(CellRef at, BoosterType type) { }
        public void OnBoosterActivated(CellRef at, BoosterType type) { }
    }
}
