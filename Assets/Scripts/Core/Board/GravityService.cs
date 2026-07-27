using System.Collections.Generic;
using Wonderfold.Core.Events;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// Settles the board after tiles are removed.
    ///
    /// <para>Gravity is a <i>local</i> rule: every cell has its own direction (fold regions can flip it
    /// when they turn over), and a tile at S moves into C when C is the cell directly downstream of S.
    /// Solving it locally rather than column-by-column is what lets a folded strip pull tiles sideways
    /// through the middle of an otherwise ordinary falling board.</para>
    ///
    /// <para>Each pass moves every tile at most one cell and is published as a single
    /// <see cref="TilesMovedEvent"/>, which gives the view a natural stepped fall to animate.</para>
    ///
    /// <para>Only the surface in play is settled. A hidden face is closed paper: its tiles stay exactly
    /// where the player left them, which is what makes planning a fold meaningful.</para>
    /// </summary>
    public sealed class GravityService
    {
        private readonly GameRules _rules;
        private readonly MatchFinder _matchFinder;
        private readonly HashSet<int> _movedThisPass = new HashSet<int>();

        public GravityService(GameRules rules, MatchFinder matchFinder)
        {
            _rules = rules;
            _matchFinder = matchFinder;
        }

        /// <summary>Runs passes until nothing moves. Returns true if anything changed at all.</summary>
        public bool Settle(BoardModel board, IBoardEventSink sink)
        {
            bool changedAny = false;
            for (int pass = 0; pass < _rules.MaxGravityPasses; pass++)
            {
                if (!Step(board, sink)) break;
                changedAny = true;
            }

            return changedAny;
        }

        /// <summary>One gravity pass. Returns false once the board is settled.</summary>
        public bool Step(BoardModel board, IBoardEventSink sink)
        {
            _movedThisPass.Clear();
            List<TileMove> moves = null;
            List<SpawnedTile> spawns = null;
            List<TilePortalledEvent> portals = null;

            foreach (var target in IterationOrder(board))
            {
                var cell = board.ActiveCell(target);
                if (cell == null || !cell.IsFallThrough) continue;

                var gravity = board.GravityAt(target);
                var upstream = target.Step(gravity.Opposite());

                if (TryPull(board, target, cell, upstream, gravity, ref moves)) continue;
                if (TryDiagonalPull(board, target, cell, upstream, gravity, ref moves)) continue;
                TrySpawn(board, target, cell, upstream, gravity, ref spawns);
            }

            // Resolve portals after the movement pass so a tile never teleports twice in one step.
            ResolvePortals(board, ref portals);

            bool changed = false;
            if (moves != null && moves.Count > 0)
            {
                sink.Publish(new TilesMovedEvent(moves));
                changed = true;
            }

            if (spawns != null && spawns.Count > 0)
            {
                sink.Publish(new TilesSpawnedEvent(spawns));
                changed = true;
            }

            if (portals != null)
            {
                for (int i = 0; i < portals.Count; i++) sink.Publish(portals[i]);
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Downstream-first ordering so a whole lane shifts in a single pass instead of one cell per
        /// pass. Derived from the board's default gravity; regions that disagree simply take an extra
        /// pass or two, which is invisible at animation speed.
        /// </summary>
        private static IEnumerable<GridCoord> IterationOrder(BoardModel board)
        {
            switch (board.DefaultGravity)
            {
                case Direction.Up:
                    for (int y = board.Height - 1; y >= 0; y--)
                    for (int x = 0; x < board.Width; x++)
                        yield return new GridCoord(x, y);
                    break;
                case Direction.Left:
                    for (int x = 0; x < board.Width; x++)
                    for (int y = 0; y < board.Height; y++)
                        yield return new GridCoord(x, y);
                    break;
                case Direction.Right:
                    for (int x = board.Width - 1; x >= 0; x--)
                    for (int y = 0; y < board.Height; y++)
                        yield return new GridCoord(x, y);
                    break;
                default: // Down
                    for (int y = 0; y < board.Height; y++)
                    for (int x = 0; x < board.Width; x++)
                        yield return new GridCoord(x, y);
                    break;
            }
        }

        private bool TryPull(BoardModel board, GridCoord target, Cell targetCell, GridCoord upstream,
            Direction gravity, ref List<TileMove> moves)
        {
            if (!board.InBounds(upstream)) return false;

            var source = board.ActiveCell(upstream);
            if (source?.Tile == null) return false;
            if (source.HasObstacle && ObstacleCatalog.BlocksSwap(source.Obstacle.Type)) return false;
            if (_movedThisPass.Contains(source.Tile.Id)) return false;

            // The source must actually be falling *towards* this cell under its own gravity.
            if (board.GravityAt(upstream) != gravity) return false;

            MoveTile(source, targetCell, ref moves);
            return true;
        }

        /// <summary>
        /// Classic diagonal slide: when the cell straight upstream is solid, tiles resting on its
        /// shoulders topple into the gap. Without this, any blocking obstacle would carve a permanent
        /// dead column through the board.
        /// </summary>
        private bool TryDiagonalPull(BoardModel board, GridCoord target, Cell targetCell, GridCoord upstream,
            Direction gravity, ref List<TileMove> moves)
        {
            bool upstreamIsSolid = !board.InBounds(upstream);
            if (!upstreamIsSolid)
            {
                var straight = board.ActiveCell(upstream);
                upstreamIsSolid = straight.IsVoid || straight.HasBlockingObstacle;
            }

            if (!upstreamIsSolid) return false;

            gravity.Perpendiculars(out var left, out var right);
            // Deterministic tie-break: always try the same side first for a given gravity.
            if (TryDiagonalFrom(board, upstream.Step(left), targetCell, gravity, ref moves)) return true;
            return TryDiagonalFrom(board, upstream.Step(right), targetCell, gravity, ref moves);
        }

        private bool TryDiagonalFrom(BoardModel board, GridCoord source, Cell targetCell, Direction gravity,
            ref List<TileMove> moves)
        {
            if (!board.InBounds(source)) return false;

            var cell = board.ActiveCell(source);
            if (cell?.Tile == null) return false;
            if (cell.HasObstacle && ObstacleCatalog.BlocksSwap(cell.Obstacle.Type)) return false;
            if (_movedThisPass.Contains(cell.Tile.Id)) return false;
            if (board.GravityAt(source) != gravity) return false;

            // Only topple when the tile has nowhere to go straight ahead.
            var straightAhead = source.Step(gravity);
            if (board.InBounds(straightAhead))
            {
                var ahead = board.ActiveCell(straightAhead);
                if (ahead.IsFallThrough) return false;
            }

            MoveTile(cell, targetCell, ref moves);
            return true;
        }

        private void MoveTile(Cell source, Cell target, ref List<TileMove> moves)
        {
            var tile = source.Tile;
            source.Tile = null;
            target.Tile = tile;
            _movedThisPass.Add(tile.Id);
            (moves ??= new List<TileMove>()).Add(new TileMove(tile.Id, source.Ref, target.Ref));
        }

        private void TrySpawn(BoardModel board, GridCoord target, Cell targetCell, GridCoord upstream,
            Direction gravity, ref List<SpawnedTile> spawns)
        {
            if (!targetCell.IsSpawner) return;

            // A spawner only creates tiles when nothing could flow into it from the board. That test has
            // to mirror TryPull exactly, including the gravity-direction check: at the upstream edge of a
            // folded region the neighbour is an ordinary playable cell, but it falls a different way, so
            // it will never hand a tile over. Without the direction check that lane deadlocks — it
            // refuses to pull and refuses to spawn, and the whole face stays blank.
            if (board.InBounds(upstream))
            {
                var up = board.ActiveCell(upstream);
                bool canFlowIn = !up.IsVoid && !up.HasBlockingObstacle && board.GravityAt(upstream) == gravity;
                if (canFlowIn) return;
            }

            var table = board.SpawnTableFor(targetCell);
            var color = table.Roll(board.Rng);

            if (_rules.AvoidInstantMatchSpawns)
            {
                targetCell.Tile = board.CreateTile(color);
                bool instant = _matchFinder.HasMatchAt(board, target);
                targetCell.Tile = null;
                if (instant) color = table.RollExcluding(board.Rng, color);
            }

            var tile = board.CreateTile(color);
            targetCell.Tile = tile;
            (spawns ??= new List<SpawnedTile>()).Add(
                new SpawnedTile(tile.Id, targetCell.Ref, color, new CellRef(upstream, targetCell.Side)));
        }

        /// <summary>
        /// Misprinted Portals swallow whatever lands on them and spit it out at their partner cell —
        /// which is usually on the face you cannot currently see.
        /// </summary>
        private static void ResolvePortals(BoardModel board, ref List<TilePortalledEvent> portals)
        {
            foreach (var coord in board.AllCoords())
            {
                var cell = board.ActiveCell(coord);
                if (cell?.Tile == null || !cell.HasPortal) continue;

                var destination = board.CellAt(cell.PortalTarget);
                if (destination == null || destination.Tile != null || !destination.CanHoldTile) continue;

                var tile = cell.Tile;
                cell.Tile = null;
                destination.Tile = tile;
                (portals ??= new List<TilePortalledEvent>()).Add(
                    new TilePortalledEvent(tile.Id, cell.Ref, destination.Ref));
            }
        }
    }
}
