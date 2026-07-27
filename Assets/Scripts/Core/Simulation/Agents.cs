using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Goals;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Simulation
{
    /// <summary>Plays whatever is legal. The floor of the difficulty band — nobody plays worse than this.</summary>
    public sealed class RandomAgent : IAgent
    {
        private readonly DeterministicRandom _rng;

        public RandomAgent(int seed)
        {
            _rng = new DeterministicRandom(seed ^ 0x5EED);
        }

        public string Name => "random";

        public PlayerMove? ChooseMove(LevelSession session)
        {
            var moves = session.EnumerateLegalMoves();
            if (moves.Count == 0) return null;
            return moves[_rng.NextInt(moves.Count)];
        }
    }

    /// <summary>
    /// Reads the board but does not look ahead: prefers big matches, matches that touch obstacles or the
    /// current objective, and folds when the meter is full and there is something worth uncovering.
    /// Fast enough to run tens of thousands of times, which makes it the workhorse of the sweep.
    /// </summary>
    public sealed class HeuristicAgent : IAgent
    {
        private readonly DeterministicRandom _rng;
        private readonly MatchFinder _matchFinder = new MatchFinder();

        public HeuristicAgent(int seed)
        {
            _rng = new DeterministicRandom(seed ^ 0x7EED);
        }

        public string Name => "heuristic";

        public PlayerMove? ChooseMove(LevelSession session)
        {
            var moves = session.EnumerateLegalMoves();
            if (moves.Count == 0) return null;

            int bestScore = int.MinValue;
            var best = new List<PlayerMove>();

            for (int i = 0; i < moves.Count; i++)
            {
                int score = Score(session, moves[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best.Clear();
                    best.Add(moves[i]);
                }
                else if (score == bestScore)
                {
                    best.Add(moves[i]);
                }
            }

            return best[_rng.NextInt(best.Count)];
        }

        private int Score(LevelSession session, PlayerMove move)
        {
            var board = session.Board;
            switch (move.Kind)
            {
                case MoveKind.Fold:
                    return ScoreFold(session, move.RegionId);
                case MoveKind.ActivateBooster:
                    return ScoreActivation(session, move.A);
                default:
                    return ScoreSwap(session, move);
            }
        }

        /// <summary>
        /// Tapping a booster is the only way to aim one, and some obstacles — wax seals, fold locks —
        /// cannot be touched any other way. So the score is mostly "what does this blast actually hit",
        /// estimated from the booster's footprint rather than by simulating it.
        /// </summary>
        private static int ScoreActivation(LevelSession session, GridCoord at)
        {
            var board = session.Board;
            var tile = board.ActiveCell(at)?.Tile;
            if (tile == null || !tile.IsBooster) return 0;

            int score = 20 + BoosterWeight(tile.Booster);

            switch (tile.Booster)
            {
                case BoosterType.RibbonRocket:
                    score += ScoreLine(session, at, tile.Orientation != BoosterOrientation.Vertical);
                    break;
                case BoosterType.InkBloom:
                    score += ScoreBox(session, at, 1);
                    break;
                case BoosterType.GoldenStitch:
                    score += ScoreLine(session, at, tile.Orientation != BoosterOrientation.Vertical) * 2;
                    break;
                case BoosterType.OrigamiBird:
                case BoosterType.PrismBookmark:
                    // These aim themselves through the level's oracle, so credit the whole board.
                    score += CountGoalObstacles(session) * 25;
                    break;
            }

            return score;
        }

        /// <summary>Value of setting <paramref name="booster"/> off at <paramref name="destination"/>.</summary>
        private static int ScoreAimedBooster(LevelSession session, TilePiece booster, GridCoord destination)
        {
            int score = 15 + BoosterWeight(booster.Booster);
            switch (booster.Booster)
            {
                case BoosterType.RibbonRocket:
                    score += ScoreLine(session, destination, booster.Orientation != BoosterOrientation.Vertical);
                    break;
                case BoosterType.InkBloom:
                    score += ScoreBox(session, destination, 1);
                    break;
                case BoosterType.GoldenStitch:
                    score += ScoreLine(session, destination, booster.Orientation != BoosterOrientation.Vertical) * 2;
                    break;
                default:
                    score += CountGoalObstacles(session) * 25;
                    break;
            }

            return score;
        }

        private static int ScoreLine(LevelSession session, GridCoord at, bool horizontal)
        {
            var board = session.Board;
            int score = 0;
            int length = horizontal ? board.Width : board.Height;
            for (int i = 0; i < length; i++)
            {
                var coord = horizontal ? new GridCoord(i, at.Y) : new GridCoord(at.X, i);
                score += ScoreCellForBlast(session, coord);
            }

            return score;
        }

        private static int ScoreBox(LevelSession session, GridCoord at, int radius)
        {
            int score = 0;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                score += ScoreCellForBlast(session, new GridCoord(at.X + dx, at.Y + dy));
            return score;
        }

        private static int ScoreCellForBlast(LevelSession session, GridCoord coord)
        {
            var board = session.Board;
            if (!board.InBounds(coord)) return 0;

            var cell = board.ActiveCell(coord);
            if (cell?.Obstacle == null) return cell?.Tile != null ? 1 : 0;

            int score = 10;
            // Obstacles a match can never touch are precisely what a blast is for.
            if (!ObstacleCatalog.TakesAdjacentMatchDamage(cell.Obstacle.Type)) score += 45;

            for (int i = 0; i < session.Goals.Count; i++)
            {
                if (session.Goals[i].IsComplete) continue;
                if (session.Goals[i] is ClearObstacleGoal goal && goal.ObstacleType == cell.Obstacle.Type)
                    score += 60;
                if (session.Goals[i] is RestoreIllustrationGoal && cell.IsIllustration) score += 50;
            }

            return score;
        }

        private static int CountGoalObstacles(LevelSession session)
        {
            int count = 0;
            for (int i = 0; i < session.Goals.Count; i++)
            {
                if (session.Goals[i].IsComplete) continue;
                if (!(session.Goals[i] is ClearObstacleGoal goal)) continue;
                count += goal.Target - goal.Current;
            }

            return count;
        }

        private int ScoreSwap(LevelSession session, PlayerMove move)
        {
            var board = session.Board;
            var cellA = board.ActiveCell(move.A);
            var cellB = board.ActiveCell(move.B);

            if (cellA?.Tile == null || cellB?.Tile == null) return 0;

            if (cellA.Tile.IsBooster && cellB.Tile.IsBooster)
                return 250 + BoosterWeight(cellA.Tile.Booster) + BoosterWeight(cellB.Tile.Booster);

            // Dragging a booster somewhere useful. Score it where it would land, not where it sits.
            if (cellA.Tile.IsBooster) return ScoreAimedBooster(session, cellA.Tile, move.B);
            if (cellB.Tile.IsBooster) return ScoreAimedBooster(session, cellB.Tile, move.A);

            // Peek at the group the swap would create without disturbing anything permanent.
            (cellA.Tile, cellB.Tile) = (cellB.Tile, cellA.Tile);
            var groups = _matchFinder.FindAll(board);
            int score = 0;

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (!group.Contains(move.A) && !group.Contains(move.B)) continue;

                score += group.Count * 4;
                if (group.CrossesSeam) score += 60;
                if (group.Count >= 5) score += 45;
                else if (group.Count == 4) score += 25;

                score += GoalWeight(session, group);
                score += AdjacentObstacleWeight(board, group);
            }

            (cellA.Tile, cellB.Tile) = (cellB.Tile, cellA.Tile);
            return score;
        }

        private static int GoalWeight(LevelSession session, MatchGroup group)
        {
            int score = 0;
            for (int i = 0; i < session.Goals.Count; i++)
            {
                var goal = session.Goals[i];
                if (goal.IsComplete) continue;
                if (goal is CollectColorGoal colorGoal && colorGoal.Color == group.Color) score += group.Count * 6;
                if (goal is SeamMatchGoal && group.CrossesSeam) score += 80;
                if (goal is ReconnectStoryGoal) score += CountStoryLinked(session.Board, group) * 120;
            }

            return score;
        }

        /// <summary>Story-linked halves are specific tiles, so a match that consumes one is worth chasing.</summary>
        private static int CountStoryLinked(BoardModel board, MatchGroup group)
        {
            int count = 0;
            for (int i = 0; i < group.Cells.Count; i++)
            {
                var tile = board.ActiveCell(group.Cells[i])?.Tile;
                if (tile != null && tile.StoryLink != 0) count++;
            }

            return count;
        }

        private static int AdjacentObstacleWeight(BoardModel board, MatchGroup group)
        {
            int score = 0;
            for (int i = 0; i < group.Cells.Count; i++)
            {
                for (int d = 0; d < 4; d++)
                {
                    var n = group.Cells[i].Step((Direction)d);
                    if (!board.InBounds(n)) continue;
                    var cell = board.ActiveCell(n);
                    if (cell?.Obstacle == null) continue;
                    score += ObstacleCatalog.TakesAdjacentMatchDamage(cell.Obstacle.Type) ? 30 : 4;
                }
            }

            return score;
        }

        private static int ScoreFold(LevelSession session, int regionId)
        {
            var board = session.Board;
            var region = board.Folds.RegionById(regionId);
            if (region == null) return 0;

            int score = 30;
            for (int i = 0; i < session.Goals.Count; i++)
            {
                var goal = session.Goals[i];
                if (goal.IsComplete) continue;
                if (goal is FoldCountGoal) score += 400;
                if (goal is SeamMatchGoal) score += 120;
            }

            // Folding is only worth a move if the far face has something on it worth having.
            foreach (var coord in region.Area.Coords())
            {
                if (!board.InBounds(coord)) continue;
                var hidden = board.HiddenCell(coord);
                if (hidden == null) continue;
                if (hidden.HasObstacle) score += 12;
                if (hidden.Tile != null && hidden.Tile.IsBooster) score += 25;
                if (hidden.IsIllustration && hidden.HasObstacle) score += 20;

                var visible = board.ActiveCell(coord);
                if (visible?.Tile != null && visible.Tile.IsBooster && board.Folds.IsFoldLineCell(coord))
                    score += 60; // Chain Fold upgrade waiting to happen
            }

            return score;
        }

        private static int BoosterWeight(BoosterType type)
        {
            switch (type)
            {
                case BoosterType.GoldenStitch: return 80;
                case BoosterType.PrismBookmark: return 60;
                case BoosterType.OrigamiBird: return 45;
                case BoosterType.InkBloom: return 35;
                case BoosterType.RibbonRocket: return 25;
                default: return 0;
            }
        }
    }

    /// <summary>
    /// One-ply look-ahead: clones the board, actually plays each candidate on the copy and keeps the one
    /// that did the most for the objective. Slower, and the realistic ceiling of an attentive player —
    /// a level that this bot cannot beat is not hard, it is broken.
    /// </summary>
    public sealed class GreedyAgent : IAgent
    {
        private readonly DeterministicRandom _rng;
        private readonly CountingListener _counter = new CountingListener();
        private readonly FoldService _foldService = new FoldService();

        public GreedyAgent(int seed)
        {
            _rng = new DeterministicRandom(seed ^ 0x6EED);
        }

        public string Name => "greedy";

        public PlayerMove? ChooseMove(LevelSession session)
        {
            var moves = session.EnumerateLegalMoves();
            if (moves.Count == 0) return null;

            int bestScore = int.MinValue;
            var best = new List<PlayerMove>();

            for (int i = 0; i < moves.Count; i++)
            {
                int score = Evaluate(session, moves[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best.Clear();
                    best.Add(moves[i]);
                }
                else if (score == bestScore)
                {
                    best.Add(moves[i]);
                }
            }

            return best[_rng.NextInt(best.Count)];
        }

        private int Evaluate(LevelSession session, PlayerMove move)
        {
            var board = session.Board.Clone();
            _counter.Reset();
            var sink = NullEventSink.Instance;
            var engine = session.Engine;

            switch (move.Kind)
            {
                case MoveKind.Swap:
                    if (!engine.TrySwap(board, move.A, move.B, sink, _counter, out _)) return int.MinValue;
                    break;
                case MoveKind.ActivateBooster:
                    if (!engine.TryActivateBooster(board, move.A, sink, _counter, out _)) return int.MinValue;
                    break;
                case MoveKind.Fold:
                    int index = board.Folds.IndexOfRegionId(move.RegionId);
                    if (index < 0) return int.MinValue;
                    _foldService.Fold(board, index, engine, sink, _counter);
                    break;
            }

            return ScoreOutcome(session, move, _counter);
        }

        private static int ScoreOutcome(LevelSession session, PlayerMove move, CountingListener counter)
        {
            int score = counter.TilesCleared + counter.BoostersCreated * 15 + counter.GoldenStitches * 45;

            for (int i = 0; i < session.Goals.Count; i++)
            {
                var goal = session.Goals[i];
                if (goal.IsComplete) continue;

                switch (goal)
                {
                    case CollectColorGoal collect:
                        score += counter.TilesOf(collect.Color) * 12;
                        break;
                    case ClearObstacleGoal obstacles:
                        score += counter.ObstaclesOf(obstacles.ObstacleType) * 140;
                        break;
                    case RestoreIllustrationGoal _:
                        score += counter.ObstaclesCleared * 90;
                        break;
                    case SeamMatchGoal _:
                        score += counter.SeamMatches * 160;
                        break;
                    case ReconnectStoryGoal _:
                        score += counter.StoryLinkedCleared * 160;
                        break;
                    case PurgeBlankGoal _:
                        score += counter.ObstaclesOf(ObstacleType.BlankStain) * 140;
                        break;
                    case FoldCountGoal _:
                        if (move.Kind == MoveKind.Fold) score += 500;
                        break;
                    case GuideWalkerGoal _:
                        score += counter.ObstaclesCleared * 70;
                        break;
                }
            }

            // A fold costs a move and a full meter; make the bot pay for it so it does not fold idly.
            if (move.Kind == MoveKind.Fold) score -= 25;
            return score;
        }
    }

    public static class AgentFactory
    {
        public static readonly string[] Names = { "random", "heuristic", "greedy" };

        public static IAgent Create(string name, int seed)
        {
            switch ((name ?? "heuristic").ToLowerInvariant())
            {
                case "random": return new RandomAgent(seed);
                case "greedy": return new GreedyAgent(seed);
                default: return new HeuristicAgent(seed);
            }
        }
    }
}
