using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Level
{
    /// <summary>
    /// Vanishing Ink creeps. Every few turns it copies itself onto a neighbouring tile, so a stain you
    /// ignore becomes a stain you cannot afford to ignore.
    /// </summary>
    public sealed class VanishingInkModifier : LevelModifier
    {
        private readonly int _period;
        private readonly int _count;
        private readonly int _startTurn;

        public VanishingInkModifier(int period, int count, int startTurn = 1) : base("vanishing-ink")
        {
            _period = period < 1 ? 1 : period;
            _count = count < 1 ? 1 : count;
            _startTurn = startTurn;
        }

        public override void OnTurnEnded(LevelSession session)
        {
            if (session.TurnNumber < _startTurn) return;
            if ((session.TurnNumber - _startTurn) % _period != 0) return;

            var board = session.Board;
            var candidates = new List<GridCoord>();

            foreach (var coord in board.AllCoords())
            {
                var cell = board.ActiveCell(coord);
                if (cell?.Obstacle == null || cell.Obstacle.Type != ObstacleType.VanishingInk) continue;

                for (int d = 0; d < 4; d++)
                {
                    var n = coord.Step((Direction)d);
                    if (!board.InBounds(n)) continue;
                    var neighbour = board.ActiveCell(n);
                    if (neighbour?.Tile == null || neighbour.HasObstacle) continue;
                    if (!candidates.Contains(n)) candidates.Add(n);
                }
            }

            if (candidates.Count == 0) return;
            session.Rng.Shuffle(candidates);

            var spread = new List<CellRef>();
            for (int i = 0; i < _count && i < candidates.Count; i++)
            {
                var cell = board.ActiveCell(candidates[i]);
                cell.Obstacle = new Obstacle(ObstacleType.VanishingInk);
                spread.Add(cell.Ref);
            }

            if (spread.Count > 0) session.Events.Publish(new BlankSpreadEvent(spread));
        }
    }

    /// <summary>
    /// The Blank itself. Each cycle it drains the colour out of a few cells, leaving stains that have to
    /// be scrubbed off. This is what turns an "Escape the Blank" level into a race.
    /// </summary>
    public sealed class BlankTideModifier : LevelModifier
    {
        private readonly int _period;
        private readonly int _count;
        private readonly int _startTurn;

        public BlankTideModifier(int period, int count, int startTurn = 1) : base("blank-tide")
        {
            _period = period < 1 ? 1 : period;
            _count = count < 1 ? 1 : count;
            _startTurn = startTurn;
        }

        public override void OnTurnEnded(LevelSession session)
        {
            if (session.TurnNumber < _startTurn) return;
            if ((session.TurnNumber - _startTurn) % _period != 0) return;

            var board = session.Board;
            var candidates = new List<GridCoord>();
            foreach (var coord in board.AllCoords())
            {
                var cell = board.ActiveCell(coord);
                if (cell?.Tile == null || cell.HasObstacle || cell.Tile.IsBooster) continue;
                candidates.Add(coord);
            }

            if (candidates.Count == 0) return;
            session.Rng.Shuffle(candidates);

            var stained = new List<CellRef>();
            for (int i = 0; i < _count && i < candidates.Count; i++)
            {
                var cell = board.ActiveCell(candidates[i]);
                cell.Obstacle = new Obstacle(ObstacleType.BlankStain);
                stained.Add(cell.Ref);
            }

            if (stained.Count > 0) session.Events.Publish(new BlankSpreadEvent(stained));
        }
    }

    /// <summary>
    /// Moves characters along whatever route the player has opened. Path-finding is a breadth-first
    /// search over cells that are clear of obstacles <i>and</i> currently showing the face the character
    /// is printed on, which is what makes folding matter to a walking puzzle.
    /// </summary>
    public sealed class WalkerModifier : LevelModifier
    {
        private readonly List<WalkerSpec> _specs;

        public WalkerModifier(List<WalkerSpec> specs) : base("walker")
        {
            _specs = specs ?? new List<WalkerSpec>();
        }

        public override void Initialise(LevelSession session)
        {
            for (int i = 0; i < _specs.Count; i++)
            {
                var spec = _specs[i];
                session.Board.AddWalker(new Walker(
                    spec.Id,
                    new GridCoord(spec.X, spec.Y),
                    new GridCoord(spec.TargetX, spec.TargetY),
                    spec.Side,
                    spec.StepsPerTurn,
                    spec.CharacterId));
            }
        }

        public override void OnTurnEnded(LevelSession session)
        {
            var board = session.Board;
            for (int i = 0; i < board.Walkers.Count; i++)
            {
                var walker = board.Walkers[i];
                if (walker.HasArrived) continue;

                var path = FindPath(board, walker);
                if (path == null || path.Count == 0) continue;

                int steps = path.Count < walker.StepsPerTurn ? path.Count : walker.StepsPerTurn;
                var taken = new List<GridCoord>(steps);
                for (int s = 0; s < steps; s++) taken.Add(path[s]);

                walker.Position = taken[taken.Count - 1];
                if (walker.Position == walker.Target) walker.HasArrived = true;

                session.Events.Publish(new WalkerMovedEvent(walker.Id, taken, walker.HasArrived));
            }
        }

        /// <summary>Shortest walkable route, excluding the starting cell. Null when there is no route yet.</summary>
        private static List<GridCoord> FindPath(BoardModel board, Walker walker)
        {
            if (!board.IsWalkable(walker.Position, walker.Side) && walker.Position != walker.Target) return null;

            var cameFrom = new Dictionary<GridCoord, GridCoord>();
            var visited = new HashSet<GridCoord> { walker.Position };
            var queue = new Queue<GridCoord>();
            queue.Enqueue(walker.Position);

            bool found = false;
            while (queue.Count > 0 && !found)
            {
                var current = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    var next = current.Step((Direction)d);
                    if (visited.Contains(next)) continue;
                    if (!board.IsWalkable(next, walker.Side)) continue;

                    visited.Add(next);
                    cameFrom[next] = current;
                    if (next == walker.Target)
                    {
                        found = true;
                        break;
                    }

                    queue.Enqueue(next);
                }
            }

            if (!found) return null;

            var path = new List<GridCoord>();
            var cursor = walker.Target;
            while (cursor != walker.Position)
            {
                path.Add(cursor);
                cursor = cameFrom[cursor];
            }

            path.Reverse();
            return path;
        }
    }

    /// <summary>
    /// The pop-up boss. The board <i>is</i> the creature: armour plates are cells, and stripping a whole
    /// layer of them makes the paper dragon rear up and print a fresh one in a new shape.
    ///
    /// <para>Phases are authored as character grids, so a designer draws the dragon's armour rather than
    /// describing it.</para>
    /// </summary>
    public sealed class PopUpBossModifier : LevelModifier
    {
        private readonly List<List<string>> _phases;
        private readonly int _retaliationPeriod;
        private readonly int _retaliationCount;
        private int _phaseIndex;

        public int PhaseCount => _phases.Count;
        public int CurrentPhase => _phaseIndex;
        public bool IsDefeated => _phaseIndex >= _phases.Count;

        public PopUpBossModifier(List<List<string>> phases, int retaliationPeriod = 4, int retaliationCount = 2)
            : base("boss")
        {
            _phases = phases ?? new List<List<string>>();
            _retaliationPeriod = retaliationPeriod < 1 ? 1 : retaliationPeriod;
            _retaliationCount = retaliationCount;
        }

        public override void Initialise(LevelSession session)
        {
            if (_phases.Count > 0) ApplyPhase(session, 0);
        }

        public override void OnTurnEnded(LevelSession session)
        {
            if (IsDefeated) return;

            if (CountArmour(session.Board) == 0)
            {
                _phaseIndex++;
                if (!IsDefeated) ApplyPhase(session, _phaseIndex);
                return;
            }

            // Between phases the dragon hits back by tearing a page it has already lost.
            if (_retaliationCount <= 0 || session.TurnNumber % _retaliationPeriod != 0) return;

            var board = session.Board;
            var candidates = new List<GridCoord>();
            foreach (var coord in board.AllCoords())
            {
                var cell = board.ActiveCell(coord);
                if (cell?.Tile != null && !cell.HasObstacle && !cell.Tile.IsBooster) candidates.Add(coord);
            }

            if (candidates.Count == 0) return;
            session.Rng.Shuffle(candidates);

            var torn = new List<CellRef>();
            for (int i = 0; i < _retaliationCount && i < candidates.Count; i++)
            {
                var cell = board.ActiveCell(candidates[i]);
                cell.Tile = null;
                cell.Obstacle = new Obstacle(ObstacleType.TornPage);
                torn.Add(cell.Ref);
            }

            if (torn.Count > 0) session.Events.Publish(new BlankSpreadEvent(torn));
        }

        private static int CountArmour(BoardModel board)
        {
            int count = 0;
            foreach (var cell in board.AllCells())
            {
                if (cell.Obstacle != null && cell.Obstacle.Type == ObstacleType.ArmourPlate) count++;
            }

            return count;
        }

        private void ApplyPhase(LevelSession session, int phase)
        {
            var rows = _phases[phase];
            var board = session.Board;
            var placed = new List<CellRef>();

            for (int r = 0; r < rows.Count; r++)
            {
                int y = board.Height - 1 - r;
                if (y < 0) break;
                var row = rows[r];
                for (int x = 0; x < row.Length && x < board.Width; x++)
                {
                    if (row[x] != 'A') continue;
                    var cell = board.ActiveCell(new GridCoord(x, y));
                    if (cell == null || cell.IsVoid || cell.HasObstacle) continue;
                    cell.Tile = null;
                    cell.Obstacle = new Obstacle(ObstacleType.ArmourPlate);
                    placed.Add(cell.Ref);
                }
            }

            if (placed.Count > 0) session.Events.Publish(new BlankSpreadEvent(placed));
        }
    }

    /// <summary>Completes when every armour phase of the pop-up boss has been stripped away.</summary>
    public sealed class BossPhaseGoal : Goals.LevelGoal
    {
        private readonly PopUpBossModifier _boss;

        public BossPhaseGoal(PopUpBossModifier boss, string id = null)
            : base(id ?? "boss.phases", boss?.PhaseCount ?? 1)
        {
            _boss = boss;
        }

        public override string Description => $"Unmake the paper dragon ({Target} layers)";

        public override void OnTurnEnded(BoardModel board)
        {
            if (_boss != null) SetProgress(_boss.CurrentPhase);
        }
    }
}
