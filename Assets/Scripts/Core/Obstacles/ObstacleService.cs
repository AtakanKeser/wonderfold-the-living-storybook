using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Obstacles
{
    /// <summary>Why an obstacle is being hit. Different obstacles ignore different sources.</summary>
    public enum DamageSource
    {
        /// <summary>A match resolved in a neighbouring cell.</summary>
        AdjacentMatch = 0,
        /// <summary>The tile this overlay was sitting on got cleared.</summary>
        UnderlyingClear = 1,
        /// <summary>A booster blast swept over the cell. Nothing is immune to this.</summary>
        Blast = 2
    }

    /// <summary>
    /// All obstacle damage funnels through here so the immunity rules exist in exactly one place.
    ///
    /// <para>Two obstacles need more than a health counter and get explicit handling: a Paper Chain
    /// only falls once every link in its group is cut, and a Story Knot pierces the page — cutting it
    /// on either face releases both.</para>
    /// </summary>
    public sealed class ObstacleService
    {
        private readonly List<CellRef> _scratch = new List<CellRef>();

        public bool Damage(BoardModel board, CellRef at, DamageSource source, IBoardEventSink sink,
            IResolutionListener listener, int amount = 1)
        {
            var cell = board.CellAt(at);
            if (cell?.Obstacle == null) return false;

            var obstacle = cell.Obstacle;
            if (!Accepts(obstacle.Type, source)) return false;
            if (obstacle.Health <= 0) return false;

            obstacle.Health -= amount;
            if (obstacle.Health < 0) obstacle.Health = 0;

            var cause = source == DamageSource.Blast ? ClearCause.Booster : ClearCause.Match;
            sink.Publish(new ObstacleDamagedEvent(at, obstacle.Type, obstacle.Health, obstacle.MaxHealth));
            listener.OnObstacleDamaged(at, obstacle, cause);

            if (obstacle.Health > 0) return true;

            if (ObstacleCatalog.IsGroupLinked(obstacle.Type))
            {
                // A chain is only released when every link has been cut.
                if (!IsGroupFullyCut(board, obstacle.Type, obstacle.GroupId)) return true;
                ReleaseGroup(board, obstacle.Type, obstacle.GroupId, sink, listener);
                return true;
            }

            RemoveObstacle(board, at, sink, listener);

            // A Story Knot is one knot threaded through the paper: cut it anywhere and both faces free.
            if (obstacle.Type == ObstacleType.StoryKnot)
            {
                var twin = at.Twin;
                var twinCell = board.CellAt(twin);
                if (twinCell?.Obstacle != null && twinCell.Obstacle.Type == ObstacleType.StoryKnot)
                {
                    twinCell.Obstacle.Health = 0;
                    RemoveObstacle(board, twin, sink, listener);
                }
            }

            return true;
        }

        private static bool Accepts(ObstacleType type, DamageSource source)
        {
            switch (source)
            {
                case DamageSource.AdjacentMatch: return ObstacleCatalog.TakesAdjacentMatchDamage(type);
                case DamageSource.UnderlyingClear: return ObstacleCatalog.TakesUnderlyingClearDamage(type);
                case DamageSource.Blast: return ObstacleCatalog.TakesBlastDamage(type);
                default: return false;
            }
        }

        private static bool IsGroupFullyCut(BoardModel board, ObstacleType type, int groupId)
        {
            foreach (var cell in board.AllCells())
            {
                var o = cell.Obstacle;
                if (o != null && o.Type == type && o.GroupId == groupId && o.Health > 0) return false;
            }

            return true;
        }

        private void ReleaseGroup(BoardModel board, ObstacleType type, int groupId, IBoardEventSink sink,
            IResolutionListener listener)
        {
            _scratch.Clear();
            foreach (var cell in board.AllCells())
            {
                var o = cell.Obstacle;
                if (o != null && o.Type == type && o.GroupId == groupId) _scratch.Add(cell.Ref);
            }

            for (int i = 0; i < _scratch.Count; i++) RemoveObstacle(board, _scratch[i], sink, listener);
        }

        private static void RemoveObstacle(BoardModel board, CellRef at, IBoardEventSink sink, IResolutionListener listener)
        {
            var cell = board.CellAt(at);
            var obstacle = cell?.Obstacle;
            if (obstacle == null) return;

            cell.Obstacle = null;
            sink.Publish(new ObstacleClearedEvent(at, obstacle.Type, obstacle.GroupId));
            listener.OnObstacleCleared(at, obstacle);
        }

        /// <summary>
        /// Chips every obstacle bordering a resolved match. Cells inside the match are excluded — those
        /// take <see cref="DamageSource.UnderlyingClear"/> instead when their tile is removed.
        /// </summary>
        public void DamageAround(BoardModel board, IReadOnlyList<GridCoord> matchedCells, IBoardEventSink sink,
            IResolutionListener listener)
        {
            _scratch.Clear();
            for (int i = 0; i < matchedCells.Count; i++)
            {
                var origin = matchedCells[i];
                for (int d = 0; d < 4; d++)
                {
                    var n = origin.Step((Direction)d);
                    if (!board.InBounds(n)) continue;
                    if (ContainsCoord(matchedCells, n)) continue;

                    var neighbour = board.ActiveCell(n);
                    if (neighbour?.Obstacle == null) continue;

                    var reference = neighbour.Ref;
                    if (_scratch.Contains(reference)) continue;
                    _scratch.Add(reference);
                }
            }

            // Copy out first: Damage can mutate the board (chains release in bulk).
            var targets = _scratch.ToArray();
            for (int i = 0; i < targets.Length; i++)
            {
                Damage(board, targets[i], DamageSource.AdjacentMatch, sink, listener);
            }
        }

        private static bool ContainsCoord(IReadOnlyList<GridCoord> list, GridCoord c)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == c) return true;
            return false;
        }

        /// <summary>How many obstacles of a type (optionally in one group) are still standing.</summary>
        public static int CountRemaining(BoardModel board, ObstacleType type, int groupId = 0, bool anyGroup = true)
        {
            int count = 0;
            foreach (var cell in board.AllCells())
            {
                var o = cell.Obstacle;
                if (o == null || o.Type != type) continue;
                if (!anyGroup && o.GroupId != groupId) continue;
                count++;
            }

            return count;
        }
    }
}
