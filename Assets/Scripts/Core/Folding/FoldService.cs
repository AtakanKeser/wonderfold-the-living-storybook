using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Boosters;
using Wonderfold.Core.Events;
using Wonderfold.Core.Obstacles;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Folding
{
    public readonly struct FoldValidation
    {
        public readonly bool IsValid;
        public readonly string Reason;

        private FoldValidation(bool isValid, string reason)
        {
            IsValid = isValid;
            Reason = reason;
        }

        public static readonly FoldValidation Valid = new FoldValidation(true, null);
        public static FoldValidation Invalid(string reason) => new FoldValidation(false, reason);
    }

    /// <summary>
    /// Performs the fold.
    ///
    /// <para>The flip itself is one line — <see cref="FoldTopology.Flip"/>. Everything else here is the
    /// stuff around it that makes folding a decision rather than a button: the lock, the meter, the move
    /// cost, and the Chain Fold that rewards leaving a booster sitting on the crease.</para>
    ///
    /// <para>Order matters and is deliberate. Boosters are lifted off the crease <i>before</i> the flip,
    /// because after it they would be on the wrong face; they are put back <i>after</i> it, upgraded, on
    /// the face that just came up.</para>
    /// </summary>
    public sealed class FoldService
    {
        public FoldValidation Validate(BoardModel board, int regionIndex, FoldMeter meter, int movesRemaining)
        {
            if (regionIndex < 0 || regionIndex >= board.Folds.RegionCount)
                return FoldValidation.Invalid("No such fold.");

            var region = board.Folds.Regions[regionIndex];

            if (region.MaxFolds > 0 && board.Folds.FoldCountOfRegion(regionIndex) >= region.MaxFolds)
                return FoldValidation.Invalid("This page will not take another crease.");

            if (region.LockGroup != 0 && CountLocks(board, region.LockGroup) > 0)
                return FoldValidation.Invalid("Fold Lock still holds the page shut.");

            if (meter != null && region.MeterCost > 0 && !meter.CanPay(region.MeterCost))
                return FoldValidation.Invalid("Fold meter is not charged.");

            if (region.MoveCost > movesRemaining)
                return FoldValidation.Invalid("Not enough moves left to fold.");

            return FoldValidation.Valid;
        }

        private static int CountLocks(BoardModel board, int lockGroup)
        {
            int count = 0;
            foreach (var cell in board.AllCells())
            {
                var o = cell.Obstacle;
                if (o != null && o.Type == ObstacleType.FoldLock && o.GroupId == lockGroup) count++;
            }

            return count;
        }

        /// <summary>
        /// Turns the page over. Assumes <see cref="Validate"/> already passed; the caller owns paying the
        /// meter and the move because it owns those resources.
        /// </summary>
        public void Fold(BoardModel board, int regionIndex, ResolutionEngine engine, IBoardEventSink sink,
            IResolutionListener listener)
        {
            var region = board.Folds.Regions[regionIndex];

            var carried = LiftBoostersOffCrease(board, regionIndex);

            var newSide = board.Folds.Flip(regionIndex);
            sink.Publish(new BoardFoldedEvent(region.Id, region.Area, region.FlipAxis, newSide,
                region.GravityFor(newSide)));

            PlaceCarriedBoosters(board, carried, sink, listener);

            // The face that just came up has been frozen since the player last saw it: it may have holes
            // from an earlier clear and stale matches sitting in it. Settling resolves both.
            engine.ResolveUntilStable(board, sink, listener);
        }

        private readonly struct CarriedBooster
        {
            public readonly GridCoord Coord;
            public readonly CellRef From;
            public readonly BoosterType Type;
            public readonly BoosterOrientation Orientation;
            public readonly TileColor Color;

            public CarriedBooster(GridCoord coord, CellRef from, BoosterType type, BoosterOrientation orientation,
                TileColor color)
            {
                Coord = coord; From = from; Type = type; Orientation = orientation; Color = color;
            }
        }

        private static List<CarriedBooster> LiftBoostersOffCrease(BoardModel board, int regionIndex)
        {
            var result = new List<CarriedBooster>();
            var creaseCells = board.Folds.CollectFoldLineCells(regionIndex);

            for (int i = 0; i < creaseCells.Count; i++)
            {
                var coord = creaseCells[i];
                var cell = board.ActiveCell(coord);
                var tile = cell?.Tile;
                if (tile == null || !tile.IsBooster) continue;

                cell.Tile = null;
                result.Add(new CarriedBooster(coord, cell.Ref, tile.Booster, tile.Orientation, tile.Color));
            }

            return result;
        }

        private static void PlaceCarriedBoosters(BoardModel board, List<CarriedBooster> carried,
            IBoardEventSink sink, IResolutionListener listener)
        {
            for (int i = 0; i < carried.Count; i++)
            {
                var entry = carried[i];
                var destination = board.ActiveCell(entry.Coord);
                if (destination == null || !destination.CanHoldTile)
                {
                    // Nowhere to land (the far face has a wax seal here). The booster is spent, which is
                    // a real cost of folding at the wrong moment rather than a silent no-op.
                    continue;
                }

                var upgraded = BoosterFactory.Upgrade(entry.Type);
                var orientation = entry.Orientation;
                if (upgraded == BoosterType.InkBloom || upgraded == BoosterType.OrigamiBird ||
                    upgraded == BoosterType.PrismBookmark)
                {
                    orientation = BoosterOrientation.None;
                }
                else if (orientation == BoosterOrientation.None)
                {
                    orientation = BoosterOrientation.Horizontal;
                }

                destination.Tile = board.CreateBooster(upgraded, entry.Color, orientation);
                sink.Publish(new ChainFoldEvent(entry.From, destination.Ref, entry.Type, upgraded));
                sink.Publish(new BoosterCreatedEvent(destination.Ref, upgraded, orientation, entry.Color, true));
                listener.OnBoosterCreated(destination.Ref, upgraded);
            }
        }
    }
}
