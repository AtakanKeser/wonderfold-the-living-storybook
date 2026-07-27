using Wonderfold.Core.Board;

namespace Wonderfold.Core.Goals
{
    /// <summary>
    /// "Guide Quill": clear a route across the page and the fox follows it. Folding is part of the
    /// puzzle rather than a shortcut around it — a folded region shows its other face, and Quill cannot
    /// walk on a face that is not turned up.
    /// </summary>
    public sealed class GuideWalkerGoal : LevelGoal
    {
        public int WalkerId { get; }

        public GuideWalkerGoal(int walkerId, string id = null)
            : base(id ?? $"walker.{walkerId}", 1)
        {
            WalkerId = walkerId;
        }

        public override string Description => "Guide Quill home";

        public override void OnTurnEnded(BoardModel board)
        {
            var walker = board.WalkerById(WalkerId);
            if (walker != null && walker.HasArrived) SetProgress(1);
        }
    }
}
