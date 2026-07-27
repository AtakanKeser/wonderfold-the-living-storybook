using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Goals
{
    /// <summary>"Collect 40 moonlight crystals." The bread-and-butter objective.</summary>
    public sealed class CollectColorGoal : LevelGoal
    {
        public TileColor Color { get; }

        public CollectColorGoal(TileColor color, int target, string id = null)
            : base(id ?? $"collect.{color}", target)
        {
            Color = color;
        }

        public override string Description => $"Collect {Target} {Color}";

        public override void OnTileCleared(BoardModel board, CellRef at, TilePiece tile, ClearCause cause)
        {
            if (tile != null && tile.Color == Color && !tile.IsBooster) Advance();
        }
    }

    /// <summary>
    /// "Tear away every torn page." Leaving <paramref name="target"/> at zero means "however many are on
    /// the board", which is how nearly every authored level uses it.
    /// </summary>
    public sealed class ClearObstacleGoal : LevelGoal
    {
        public ObstacleType ObstacleType { get; }
        private readonly bool _autoSize;

        public ClearObstacleGoal(ObstacleType type, int target = 0, string id = null)
            : base(id ?? $"clear.{type}", target)
        {
            ObstacleType = type;
            _autoSize = target <= 0;
        }

        public override string Description => $"Clear {Target} {ObstacleCatalog.DisplayName(ObstacleType)}";

        public override void Initialise(BoardModel board)
        {
            if (!_autoSize) return;

            int count = 0;
            foreach (var cell in board.AllCells())
            {
                if (cell.Obstacle != null && cell.Obstacle.Type == ObstacleType) count++;
            }

            Target = count;
        }

        public override void OnObstacleCleared(BoardModel board, CellRef at, Obstacle obstacle)
        {
            if (obstacle.Type == ObstacleType) Advance();
        }
    }

    /// <summary>
    /// "Restore the Illustration": the picture hiding under the board is revealed as the cells marked as
    /// illustration are freed of whatever was covering them.
    /// </summary>
    public sealed class RestoreIllustrationGoal : LevelGoal
    {
        private readonly HashSet<CellRef> _restored = new HashSet<CellRef>();

        public RestoreIllustrationGoal(int target = 0, string id = null)
            : base(id ?? "restore.illustration", target) { }

        public override string Description => $"Reveal the illustration ({Target} pieces)";

        public override void Initialise(BoardModel board)
        {
            int total = 0;
            foreach (var cell in board.AllCells())
            {
                if (!cell.IsIllustration) continue;
                total++;
                if (!cell.HasObstacle) _restored.Add(cell.Ref);
            }

            if (Target <= 0) Target = total;
            SetProgress(_restored.Count);
        }

        public override void OnObstacleCleared(BoardModel board, CellRef at, Obstacle obstacle)
        {
            var cell = board.CellAt(at);
            if (cell == null || !cell.IsIllustration || cell.HasObstacle) return;
            if (_restored.Add(at)) SetProgress(_restored.Count);
        }
    }

    /// <summary>
    /// "Sew three seams." Teaches the signature mechanic by asking for the thing that mints a Golden
    /// Stitch, which means the tutorial reward and the tutorial goal are the same action.
    /// </summary>
    public sealed class SeamMatchGoal : LevelGoal
    {
        public SeamMatchGoal(int target, string id = null) : base(id ?? "seam.matches", target) { }

        public override string Description => $"Match across the fold {Target} times";

        public override void OnMatchResolved(BoardModel board, MatchGroup group)
        {
            if (group.CrossesSeam) Advance();
        }
    }

    /// <summary>"Turn the page twice." Used only in the fold tutorial levels.</summary>
    public sealed class FoldCountGoal : LevelGoal
    {
        private readonly int _regionId;

        public FoldCountGoal(int target, int regionId = -1, string id = null)
            : base(id ?? "fold.count", target)
        {
            _regionId = regionId;
        }

        public override string Description => $"Fold the page {Target} times";

        public override void OnFolded(BoardModel board, int regionId)
        {
            if (_regionId < 0 || regionId == _regionId) Advance();
        }
    }

    /// <summary>
    /// "Reconnect the Story": paired symbols are printed on opposite faces of the same page. Clearing one
    /// half only counts once its partner behind the paper has gone too — which cannot be done without
    /// folding.
    /// </summary>
    public sealed class ReconnectStoryGoal : LevelGoal
    {
        private readonly Dictionary<int, int> _clearedPerLink = new Dictionary<int, int>();

        public ReconnectStoryGoal(int target = 0, string id = null) : base(id ?? "reconnect.story", target) { }

        public override string Description => $"Reunite {Target} story pairs";

        public override void Initialise(BoardModel board)
        {
            if (Target > 0) return;

            var links = new HashSet<int>();
            foreach (var cell in board.AllCells())
            {
                if (cell.Tile != null && cell.Tile.StoryLink != 0) links.Add(cell.Tile.StoryLink);
            }

            Target = links.Count;
        }

        public override void OnTileCleared(BoardModel board, CellRef at, TilePiece tile, ClearCause cause)
        {
            if (tile == null || tile.StoryLink == 0) return;

            _clearedPerLink.TryGetValue(tile.StoryLink, out int count);
            _clearedPerLink[tile.StoryLink] = count + 1;

            if (count + 1 != 2) return; // both halves, front and back
            int complete = 0;
            foreach (var pair in _clearedPerLink)
            {
                if (pair.Value >= 2) complete++;
            }

            SetProgress(complete);
        }
    }

    /// <summary>
    /// "Escape the Blank": survive while The Blank drains the page, and scrub off the stains it leaves.
    /// The stains themselves are spawned by <c>BlankTideModifier</c>.
    /// </summary>
    public sealed class PurgeBlankGoal : LevelGoal
    {
        public PurgeBlankGoal(int target, string id = null) : base(id ?? "purge.blank", target) { }

        public override string Description => $"Scrub {Target} blank stains";

        public override void OnObstacleCleared(BoardModel board, CellRef at, Obstacle obstacle)
        {
            if (obstacle.Type == ObstacleType.BlankStain) Advance();
        }
    }
}
