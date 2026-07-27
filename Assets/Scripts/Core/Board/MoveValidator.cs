using System.Collections.Generic;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// Decides whether a swap is legal <i>before</i> anything is mutated.
    ///
    /// <para>The rules follow familiar match-three conventions, so players need no re-learning:</para>
    /// <list type="bullet">
    /// <item>An ordinary swap must create a match.</item>
    /// <item>Dragging a booster onto any neighbour is always legal and sets the booster off where it
    /// lands. Without this a booster can only ever fire from the square it happened to be born on, and
    /// obstacles that <i>only</i> blast damage can touch — wax seals, fold locks — become unclearable.</item>
    /// <item>Dragging two boosters together fires their combination.</item>
    /// </list>
    /// </summary>
    public sealed class MoveValidator
    {
        private readonly MatchFinder _matchFinder;

        public MoveValidator(MatchFinder matchFinder)
        {
            _matchFinder = matchFinder;
        }

        public MoveValidation ValidateSwap(BoardModel board, GridCoord a, GridCoord b)
        {
            if (!board.InBounds(a) || !board.InBounds(b))
                return MoveValidation.Invalid("Off board.");

            if (!a.IsOrthogonalNeighbourOf(b))
                return MoveValidation.Invalid("Tiles must be orthogonal neighbours.");

            var ca = board.ActiveCell(a);
            var cb = board.ActiveCell(b);

            if (!ca.IsSwappable) return MoveValidation.Invalid(DescribeBlock(ca));
            if (!cb.IsSwappable) return MoveValidation.Invalid(DescribeBlock(cb));

            if (ca.Tile.IsBooster || cb.Tile.IsBooster) return MoveValidation.Valid;

            return _matchFinder.WouldMatch(board, a, b)
                ? MoveValidation.Valid
                : MoveValidation.Invalid("Swap creates no match.");
        }

        public MoveValidation ValidateActivate(BoardModel board, GridCoord at)
        {
            if (!board.InBounds(at)) return MoveValidation.Invalid("Off board.");
            var cell = board.ActiveCell(at);
            if (cell.Tile == null) return MoveValidation.Invalid("Empty cell.");
            if (!cell.Tile.IsBooster) return MoveValidation.Invalid("Not a booster.");
            if (cell.HasObstacle && ObstacleCatalog.BlocksSwap(cell.Obstacle.Type))
                return MoveValidation.Invalid($"{ObstacleCatalog.DisplayName(cell.Obstacle.Type)} holds it down.");
            return MoveValidation.Valid;
        }

        private static string DescribeBlock(Cell cell)
        {
            if (cell.IsVoid) return "Not part of the page.";
            if (cell.HasBlockingObstacle) return $"{ObstacleCatalog.DisplayName(cell.Obstacle.Type)} blocks the cell.";
            if (cell.Tile == null) return "Empty cell.";
            if (cell.HasOverlayObstacle) return $"{ObstacleCatalog.DisplayName(cell.Obstacle.Type)} holds the tile.";
            return "Tile cannot move.";
        }

        /// <summary>
        /// Every legal swap on the board right now. Feeds the dead-board detector, the hint system and
        /// the bot agents — three consumers, one definition of "a move exists".
        /// </summary>
        public List<PlayerMove> EnumerateSwaps(BoardModel board)
        {
            var result = new List<PlayerMove>();
            foreach (var a in board.AllCoords())
            {
                // Only look right and up so each unordered pair is produced exactly once.
                var right = new GridCoord(a.X + 1, a.Y);
                var up = new GridCoord(a.X, a.Y + 1);
                if (board.InBounds(right) && ValidateSwap(board, a, right).IsValid) result.Add(PlayerMove.Swap(a, right));
                if (board.InBounds(up) && ValidateSwap(board, a, up).IsValid) result.Add(PlayerMove.Swap(a, up));
            }

            return result;
        }

        /// <summary>Boosters sitting on the board that the player could tap right now.</summary>
        public List<PlayerMove> EnumerateActivations(BoardModel board)
        {
            var result = new List<PlayerMove>();
            foreach (var coord in board.AllCoords())
            {
                if (ValidateActivate(board, coord).IsValid) result.Add(PlayerMove.ActivateBooster(coord));
            }

            return result;
        }

        public bool HasAnyLegalSwap(BoardModel board)
        {
            foreach (var a in board.AllCoords())
            {
                var right = new GridCoord(a.X + 1, a.Y);
                if (board.InBounds(right) && ValidateSwap(board, a, right).IsValid) return true;
                var up = new GridCoord(a.X, a.Y + 1);
                if (board.InBounds(up) && ValidateSwap(board, a, up).IsValid) return true;
            }

            return false;
        }
    }
}
