using Wonderfold.Core.Board;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Goals
{
    /// <summary>
    /// One objective on the level banner.
    ///
    /// <para>Goals observe the board, they never drive it. Every hook is a notification of something that
    /// already happened, which means adding a new objective type is a new file and nothing else — the
    /// resolution pipeline never learns what the level is asking for.</para>
    /// </summary>
    public interface ILevelGoal
    {
        string Id { get; }
        string Description { get; }
        int Current { get; }
        int Target { get; }
        bool IsComplete { get; }

        /// <summary>Called once the board is built. Goals with an implicit target ("clear them all") size themselves here.</summary>
        void Initialise(BoardModel board);

        void OnTileCleared(BoardModel board, CellRef at, TilePiece tile, ClearCause cause);
        void OnObstacleCleared(BoardModel board, CellRef at, Obstacle obstacle);
        void OnMatchResolved(BoardModel board, MatchGroup group);
        void OnFolded(BoardModel board, int regionId);

        /// <summary>End of a full turn, after every cascade has settled.</summary>
        void OnTurnEnded(BoardModel board);
    }

    public abstract class LevelGoal : ILevelGoal
    {
        protected LevelGoal(string id, int target)
        {
            Id = id;
            Target = target;
        }

        public string Id { get; }
        public int Target { get; protected set; }
        public int Current { get; protected set; }
        public bool IsComplete => Current >= Target;
        public abstract string Description { get; }

        /// <summary>Progress is monotonic by construction — a goal can never run backwards mid-level.</summary>
        protected bool Advance(int amount = 1)
        {
            if (amount <= 0 || IsComplete) return false;
            Current += amount;
            if (Current > Target) Current = Target;
            return true;
        }

        protected void SetProgress(int value)
        {
            if (value <= Current) return;
            Current = value > Target ? Target : value;
        }

        public virtual void Initialise(BoardModel board) { }
        public virtual void OnTileCleared(BoardModel board, CellRef at, TilePiece tile, ClearCause cause) { }
        public virtual void OnObstacleCleared(BoardModel board, CellRef at, Obstacle obstacle) { }
        public virtual void OnMatchResolved(BoardModel board, MatchGroup group) { }
        public virtual void OnFolded(BoardModel board, int regionId) { }
        public virtual void OnTurnEnded(BoardModel board) { }

        public override string ToString() => $"{Description} {Current}/{Target}";
    }
}
