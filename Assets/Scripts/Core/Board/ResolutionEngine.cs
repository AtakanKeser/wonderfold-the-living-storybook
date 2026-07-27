using System.Collections.Generic;
using Wonderfold.Core.Boosters;
using Wonderfold.Core.Events;
using Wonderfold.Core.Obstacles;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// Drives one turn from beginning to end: perform the move, resolve matches, mint boosters, chip
    /// obstacles, settle gravity, then do it all again for as long as the cascade keeps producing.
    ///
    /// <para>This is the only class allowed to run that loop. Everything it calls is a small service with
    /// one job, and everything it does is reported as events, so a turn is fully reproducible from a
    /// seed and fully replayable in the view without the view ever reading board state.</para>
    /// </summary>
    public sealed class ResolutionEngine
    {
        private readonly GameRules _rules;
        private readonly MatchFinder _matchFinder;
        private readonly MoveValidator _validator;
        private readonly GravityService _gravity;
        private readonly BoosterEngine _boosters;
        private readonly ObstacleService _obstacles;
        private readonly ShuffleService _shuffle;

        public MatchFinder MatchFinder => _matchFinder;
        public MoveValidator Validator => _validator;
        public BoosterEngine Boosters => _boosters;
        public ObstacleService Obstacles => _obstacles;
        public ShuffleService Shuffle => _shuffle;
        public GravityService Gravity => _gravity;

        public ResolutionEngine(GameRules rules)
        {
            _rules = rules;
            _matchFinder = new MatchFinder();
            _validator = new MoveValidator(_matchFinder);
            _gravity = new GravityService(rules, _matchFinder);
            _obstacles = new ObstacleService();
            _boosters = new BoosterEngine(rules, _obstacles);
            _shuffle = new ShuffleService(rules, _matchFinder, _validator);
        }

        // ------------------------------------------------------------------ turns

        /// <summary>
        /// Applies a swap. Returns false without touching the board when the swap is illegal, so callers
        /// can safely try speculative moves.
        /// </summary>
        public bool TrySwap(BoardModel board, GridCoord a, GridCoord b, IBoardEventSink sink,
            IResolutionListener listener, out string reason)
        {
            var validation = _validator.ValidateSwap(board, a, b);
            if (!validation.IsValid)
            {
                reason = validation.Reason;
                sink.Publish(new SwapRejectedEvent(
                    new CellRef(a, board.ActiveSide(a)), new CellRef(b, board.ActiveSide(b)), reason));
                return false;
            }

            reason = null;
            var cellA = board.ActiveCell(a);
            var cellB = board.ActiveCell(b);
            bool boosterA = cellA.Tile.IsBooster;
            bool boosterB = cellB.Tile.IsBooster;

            board.SwapTiles(a, b);
            sink.Publish(new SwapPerformedEvent(cellA.Ref, cellB.Ref));

            if (boosterA && boosterB)
            {
                // The tiles have already traded places; the combo detonates where they ended up.
                _boosters.DetonateCombo(board, a, b, sink, listener);
                ResolveUntilStable(board, sink, listener);
                return true;
            }

            if (boosterA || boosterB)
            {
                // A booster dragged onto an ordinary tile goes off at its destination — this is how the
                // player aims one.
                var destination = boosterA ? b : a;
                _boosters.Detonate(board, destination, sink, listener);
                ResolveUntilStable(board, sink, listener);
                return true;
            }

            ResolveUntilStable(board, sink, listener, a, b);
            return true;
        }

        public bool TryActivateBooster(BoardModel board, GridCoord at, IBoardEventSink sink,
            IResolutionListener listener, out string reason)
        {
            var validation = _validator.ValidateActivate(board, at);
            if (!validation.IsValid)
            {
                reason = validation.Reason;
                return false;
            }

            reason = null;
            _boosters.Detonate(board, at, sink, listener);
            ResolveUntilStable(board, sink, listener);
            return true;
        }

        // ------------------------------------------------------------------ cascade

        /// <summary>
        /// Settle, resolve, repeat. <paramref name="pivotA"/>/<paramref name="pivotB"/> are the cells the
        /// player just touched: when a match contains one of them the booster is born under the finger,
        /// which is what makes deliberate four-matches feel deliberate.
        /// </summary>
        public void ResolveUntilStable(BoardModel board, IBoardEventSink sink, IResolutionListener listener,
            GridCoord? pivotA = null, GridCoord? pivotB = null)
        {
            for (int depth = 0; depth < _rules.MaxCascadeDepth; depth++)
            {
                _gravity.Settle(board, sink);

                var groups = _matchFinder.FindAll(board);
                if (groups.Count == 0) break;

                sink.Publish(new CascadeStepEvent(depth));
                ResolveGroups(board, groups, sink, listener, depth == 0 ? pivotA : null, depth == 0 ? pivotB : null);
            }

            _gravity.Settle(board, sink);
        }

        private void ResolveGroups(BoardModel board, List<MatchGroup> groups, IBoardEventSink sink,
            IResolutionListener listener, GridCoord? pivotA, GridCoord? pivotB)
        {
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];

                var pivot = group.Pivot;
                if (pivotA.HasValue && group.Contains(pivotA.Value)) pivot = pivotA.Value;
                else if (pivotB.HasValue && group.Contains(pivotB.Value)) pivot = pivotB.Value;
                group.Pivot = pivot;

                sink.Publish(new MatchFoundEvent(ToRefs(board, group.Cells), group.Color, group.Shape, group.CrossesSeam));

                bool makesBooster = BoosterFactory.TryCreate(_rules, group, out var boosterType, out var orientation);

                var cleared = new List<CellRef>();
                for (int i = 0; i < group.Cells.Count; i++)
                {
                    var coord = group.Cells[i];
                    if (makesBooster && coord == pivot) continue;
                    ClearTile(board, coord, ClearCause.Match, sink, listener, cleared);
                }

                if (cleared.Count > 0) sink.Publish(new TilesClearedEvent(cleared, ClearCause.Match));

                _obstacles.DamageAround(board, group.Cells, sink, listener);
                InkNearbyBlanks(board, group, sink);

                if (makesBooster)
                {
                    var cell = board.ActiveCell(pivot);
                    // The pivot may have been swallowed by a chained effect; only place if it survived.
                    if (cell != null && cell.CanHoldTile)
                    {
                        cell.Tile = board.CreateBooster(boosterType, group.Color, orientation);
                        sink.Publish(new BoosterCreatedEvent(cell.Ref, boosterType, orientation, group.Color));
                        listener.OnBoosterCreated(cell.Ref, boosterType);
                    }
                }

                listener.OnMatchResolved(group);
            }
        }

        /// <summary>Removes one tile and chips whatever overlay was sitting on it.</summary>
        public void ClearTile(BoardModel board, GridCoord coord, ClearCause cause, IBoardEventSink sink,
            IResolutionListener listener, List<CellRef> cleared = null)
        {
            var cell = board.ActiveCell(coord);
            if (cell?.Tile == null) return;

            var tile = cell.Tile;
            cell.Tile = null;
            listener.OnTileCleared(cell.Ref, tile, cause);
            cleared?.Add(cell.Ref);

            if (cell.HasOverlayObstacle)
                _obstacles.Damage(board, cell.Ref, DamageSource.UnderlyingClear, sink, listener);
        }

        /// <summary>
        /// Blank tiles have no colour of their own; they drink the colour of the nearest match. This runs
        /// after the group has been cleared so a blank never joins the match that inks it.
        /// </summary>
        private static void InkNearbyBlanks(BoardModel board, MatchGroup group, IBoardEventSink sink)
        {
            if (group.Color == TileColor.None) return;

            for (int i = 0; i < group.Cells.Count; i++)
            {
                for (int d = 0; d < 4; d++)
                {
                    var n = group.Cells[i].Step((Direction)d);
                    if (!board.InBounds(n)) continue;

                    var cell = board.ActiveCell(n);
                    var tile = cell?.Tile;
                    if (tile == null || !tile.IsBlank || tile.Color != TileColor.None) continue;

                    tile.Color = group.Color;
                    sink.Publish(new BlankTileInkedEvent(cell.Ref, group.Color));
                }
            }
        }

        private static CellRef[] ToRefs(BoardModel board, List<GridCoord> coords)
        {
            var result = new CellRef[coords.Count];
            for (int i = 0; i < coords.Count; i++) result[i] = new CellRef(coords[i], board.ActiveSide(coords[i]));
            return result;
        }

        // ------------------------------------------------------------------ board maintenance

        /// <summary>
        /// Prepares a freshly built level: fills every cell and quietly removes accidental matches
        /// without awarding anything.
        ///
        /// <para>Crucially this also prepares the faces the player cannot see yet. Gravity only ever runs
        /// on the surface in play, so without this pass a fold would reveal a blank page that fills in
        /// front of you — and planning a fold, which is the entire mechanic, would be impossible. Each
        /// region is briefly turned over, settled, and turned back.</para>
        /// </summary>
        public void SettleSilently(BoardModel board)
        {
            SettleActiveSurface(board);

            for (int i = 0; i < board.Folds.RegionCount; i++)
            {
                var original = board.Folds.SideOfRegion(i);
                var other = original == SurfaceSide.Front ? SurfaceSide.Back : SurfaceSide.Front;

                board.Folds.ForceSide(i, other);
                SettleActiveSurface(board);
                board.Folds.ForceSide(i, original);
            }

            SettleActiveSurface(board);
        }

        private void SettleActiveSurface(BoardModel board)
        {
            var sink = NullEventSink.Instance;

            for (int depth = 0; depth < _rules.MaxCascadeDepth; depth++)
            {
                _gravity.Settle(board, sink);
                var groups = _matchFinder.FindAll(board);
                if (groups.Count == 0) break;

                // Recolour instead of clearing so the authored layout survives intact.
                for (int g = 0; g < groups.Count; g++)
                {
                    var group = groups[g];
                    for (int i = 0; i < group.Cells.Count; i++)
                    {
                        var cell = board.ActiveCell(group.Cells[i]);
                        if (cell?.Tile == null || cell.Tile.IsBooster) continue;
                        var table = board.SpawnTableFor(cell);
                        cell.Tile.Color = table.RollExcluding(board.Rng, cell.Tile.Color);
                    }
                }
            }

            _gravity.Settle(board, sink);
        }

        /// <summary>
        /// Shuffles when there is nothing left to do. Returns false when even a shuffle cannot save the
        /// board, which the level turns into a loss rather than a soft lock.
        /// </summary>
        public bool EnsurePlayable(BoardModel board, IBoardEventSink sink, IResolutionListener listener)
        {
            if (!_shuffle.IsBoardStuck(board)) return true;
            if (!_shuffle.Shuffle(board, sink)) return false;

            ResolveUntilStable(board, sink, listener);
            return !_shuffle.IsBoardStuck(board) || _shuffle.Shuffle(board, sink);
        }
    }
}
