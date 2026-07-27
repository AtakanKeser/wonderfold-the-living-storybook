using System.Collections.Generic;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// Finds every match on the surface currently in play.
    ///
    /// <para>Runs of three or more are collected per axis, then merged with a union-find so an L or a T
    /// is reported as a single group rather than two overlapping lines. Matching never looks at the
    /// hidden surface: a fold is what brings the other face into scoring range, and that is the whole
    /// tension of the mechanic.</para>
    ///
    /// <para>Instances are reusable and reuse their scratch buffers, so the simulator can run this
    /// millions of times without pressuring the GC.</para>
    /// </summary>
    public sealed class MatchFinder
    {
        public const int MinMatchLength = 3;

        private int[] _parent = System.Array.Empty<int>();
        private int[] _runH = System.Array.Empty<int>();
        private int[] _runV = System.Array.Empty<int>();
        private bool[] _inMatch = System.Array.Empty<bool>();
        private int _width;
        private int _height;

        private void EnsureCapacity(BoardModel board)
        {
            int n = board.Width * board.Height;
            if (_parent.Length == n && _width == board.Width) return;
            _parent = new int[n];
            _runH = new int[n];
            _runV = new int[n];
            _inMatch = new bool[n];
            _width = board.Width;
            _height = board.Height;
        }

        private int Index(GridCoord c) => c.Y * _width + c.X;

        private int Find(int i)
        {
            while (_parent[i] != i)
            {
                _parent[i] = _parent[_parent[i]];
                i = _parent[i];
            }

            return i;
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _parent[rb] = ra;
        }

        /// <summary>All match groups on the active surface, in deterministic order.</summary>
        public List<MatchGroup> FindAll(BoardModel board)
        {
            EnsureCapacity(board);
            int n = board.Width * board.Height;
            for (int i = 0; i < n; i++)
            {
                _parent[i] = i;
                _runH[i] = 0;
                _runV[i] = 0;
                _inMatch[i] = false;
            }

            // ---- horizontal runs
            for (int y = 0; y < board.Height; y++)
            {
                int runStart = 0;
                TileColor runColor = TileColor.None;
                for (int x = 0; x <= board.Width; x++)
                {
                    TileColor color = x < board.Width ? board.ActiveCell(x, y).MatchColor : TileColor.None;
                    if (color != runColor || color == TileColor.None)
                    {
                        int length = x - runStart;
                        if (runColor != TileColor.None && length >= MinMatchLength)
                            CommitRun(runStart, y, length, true);
                        runStart = x;
                        runColor = color;
                    }
                }
            }

            // ---- vertical runs
            for (int x = 0; x < board.Width; x++)
            {
                int runStart = 0;
                TileColor runColor = TileColor.None;
                for (int y = 0; y <= board.Height; y++)
                {
                    TileColor color = y < board.Height ? board.ActiveCell(x, y).MatchColor : TileColor.None;
                    if (color != runColor || color == TileColor.None)
                    {
                        int length = y - runStart;
                        if (runColor != TileColor.None && length >= MinMatchLength)
                            CommitRun(x, runStart, length, false);
                        runStart = y;
                        runColor = color;
                    }
                }
            }

            // ---- collect groups
            var byRoot = new Dictionary<int, MatchGroup>();
            var ordered = new List<MatchGroup>();
            for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++)
            {
                var coord = new GridCoord(x, y);
                int i = Index(coord);
                if (!_inMatch[i]) continue;

                int root = Find(i);
                if (!byRoot.TryGetValue(root, out var group))
                {
                    group = new MatchGroup { Color = board.ActiveCell(coord).MatchColor };
                    byRoot[root] = group;
                    ordered.Add(group);
                }

                group.Cells.Add(coord);
                if (_runH[i] > group.LongestHorizontal) group.LongestHorizontal = _runH[i];
                if (_runV[i] > group.LongestVertical) group.LongestVertical = _runV[i];
            }

            for (int g = 0; g < ordered.Count; g++)
            {
                var group = ordered[g];
                group.CrossesSeam = ComputeCrossesSeam(board, group);
                group.Pivot = ComputeDefaultPivot(group);
            }

            return ordered;
        }

        private void CommitRun(int startX, int startY, int length, bool horizontal)
        {
            int firstIndex = -1;
            for (int k = 0; k < length; k++)
            {
                int x = horizontal ? startX + k : startX;
                int y = horizontal ? startY : startY + k;
                int i = y * _width + x;
                _inMatch[i] = true;
                if (horizontal)
                {
                    if (length > _runH[i]) _runH[i] = length;
                }
                else
                {
                    if (length > _runV[i]) _runV[i] = length;
                }

                if (firstIndex < 0) firstIndex = i;
                else Union(firstIndex, i);
            }
        }

        private static bool ComputeCrossesSeam(BoardModel board, MatchGroup group)
        {
            for (int i = 0; i < group.Cells.Count; i++)
            {
                var a = group.Cells[i];
                var sideA = board.ActiveSide(a);
                for (int j = i + 1; j < group.Cells.Count; j++)
                {
                    var b = group.Cells[j];
                    if (!a.IsOrthogonalNeighbourOf(b)) continue;
                    if (board.ActiveSide(b) != sideA) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Where the booster lands when the player did not directly cause the group (cascades).
        /// Prefers the crossing point of the two axes, otherwise the middle of the line.
        /// </summary>
        private static GridCoord ComputeDefaultPivot(MatchGroup group)
        {
            if (group.LongestHorizontal >= 3 && group.LongestVertical >= 3)
            {
                // The intersection cell is the one with a same-group neighbour on both axes.
                for (int i = 0; i < group.Cells.Count; i++)
                {
                    var c = group.Cells[i];
                    bool h = group.Contains(new GridCoord(c.X - 1, c.Y)) || group.Contains(new GridCoord(c.X + 1, c.Y));
                    bool v = group.Contains(new GridCoord(c.X, c.Y - 1)) || group.Contains(new GridCoord(c.X, c.Y + 1));
                    if (h && v) return c;
                }
            }

            return group.Cells[group.Cells.Count / 2];
        }

        /// <summary>Fast "is the board still alive" probe — stops at the first group found.</summary>
        public bool HasAnyMatch(BoardModel board)
        {
            for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++)
            {
                var color = board.ActiveCell(x, y).MatchColor;
                if (color == TileColor.None) continue;

                if (x + 2 < board.Width &&
                    board.ActiveCell(x + 1, y).MatchColor == color &&
                    board.ActiveCell(x + 2, y).MatchColor == color) return true;

                if (y + 2 < board.Height &&
                    board.ActiveCell(x, y + 1).MatchColor == color &&
                    board.ActiveCell(x, y + 2).MatchColor == color) return true;
            }

            return false;
        }

        /// <summary>
        /// Does swapping these two coordinates produce at least one match? Performs the swap on the live
        /// board and undoes it, which is safe because nothing else observes the board mid-check.
        /// </summary>
        public bool WouldMatch(BoardModel board, GridCoord a, GridCoord b)
        {
            var ca = board.ActiveCell(a);
            var cb = board.ActiveCell(b);
            if (ca == null || cb == null) return false;

            (ca.Tile, cb.Tile) = (cb.Tile, ca.Tile);
            bool result = HasMatchAt(board, a) || HasMatchAt(board, b);
            (ca.Tile, cb.Tile) = (cb.Tile, ca.Tile);
            return result;
        }

        /// <summary>Is this specific coordinate part of a run of 3+ right now?</summary>
        public bool HasMatchAt(BoardModel board, GridCoord c)
        {
            var color = board.ActiveCell(c).MatchColor;
            if (color == TileColor.None) return false;

            int horizontal = 1;
            for (int x = c.X - 1; x >= 0 && board.ActiveCell(x, c.Y).MatchColor == color; x--) horizontal++;
            for (int x = c.X + 1; x < board.Width && board.ActiveCell(x, c.Y).MatchColor == color; x++) horizontal++;
            if (horizontal >= MinMatchLength) return true;

            int vertical = 1;
            for (int y = c.Y - 1; y >= 0 && board.ActiveCell(c.X, y).MatchColor == color; y--) vertical++;
            for (int y = c.Y + 1; y < board.Height && board.ActiveCell(c.X, y).MatchColor == color; y++) vertical++;
            return vertical >= MinMatchLength;
        }
    }
}
