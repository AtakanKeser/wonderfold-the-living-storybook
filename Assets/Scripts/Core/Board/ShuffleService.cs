using System.Collections.Generic;
using Wonderfold.Core.Events;
using Wonderfold.Core.Rules;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// Rescues a board with no legal move left.
    ///
    /// <para>Only free, plain tiles are reshuffled: boosters stay exactly where the player earned them,
    /// and anything pinned by an overlay stays pinned. An arrangement is accepted only if it contains no
    /// pre-made match and at least one legal swap — otherwise we roll again.</para>
    ///
    /// <para>If no arrangement works at all (a board choked with obstacles), the level is genuinely over
    /// and the caller ends it rather than pretending otherwise.</para>
    /// </summary>
    public sealed class ShuffleService
    {
        private readonly GameRules _rules;
        private readonly MatchFinder _matchFinder;
        private readonly MoveValidator _validator;

        public ShuffleService(GameRules rules, MatchFinder matchFinder, MoveValidator validator)
        {
            _rules = rules;
            _matchFinder = matchFinder;
            _validator = validator;
        }

        public bool IsBoardStuck(BoardModel board) =>
            !_validator.HasAnyLegalSwap(board) && !HasActivatableBooster(board);

        private static bool HasActivatableBooster(BoardModel board)
        {
            foreach (var coord in board.AllCoords())
            {
                var cell = board.ActiveCell(coord);
                if (cell?.Tile == null || !cell.Tile.IsBooster) continue;
                if (cell.HasObstacle && ObstacleCatalog.BlocksSwap(cell.Obstacle.Type)) continue;
                return true;
            }

            return false;
        }

        /// <summary>Returns false when the board cannot be made playable at all.</summary>
        public bool Shuffle(BoardModel board, IBoardEventSink sink)
        {
            var cells = new List<Cell>();
            var tiles = new List<TilePiece>();

            foreach (var coord in board.AllCoords())
            {
                var cell = board.ActiveCell(coord);
                if (cell?.Tile == null) continue;
                if (cell.Tile.IsBooster) continue;
                if (cell.HasObstacle && ObstacleCatalog.BlocksSwap(cell.Obstacle.Type)) continue;
                cells.Add(cell);
                tiles.Add(cell.Tile);
            }

            if (cells.Count < 3)
            {
                sink.Publish(new BoardShuffledEvent(0, false));
                return false;
            }

            for (int attempt = 1; attempt <= _rules.MaxShuffleAttempts; attempt++)
            {
                board.Rng.Shuffle(tiles);
                for (int i = 0; i < cells.Count; i++) cells[i].Tile = tiles[i];

                if (_matchFinder.HasAnyMatch(board)) continue;
                if (!_validator.HasAnyLegalSwap(board)) continue;

                sink.Publish(new BoardShuffledEvent(attempt, true));
                return true;
            }

            sink.Publish(new BoardShuffledEvent(_rules.MaxShuffleAttempts, false));
            return false;
        }
    }
}
