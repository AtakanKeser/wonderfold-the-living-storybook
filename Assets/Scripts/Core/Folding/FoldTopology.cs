using System;
using System.Collections.Generic;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Folding
{
    /// <summary>
    /// Maps every board coordinate to the fold region that owns it, and tracks which side of the
    /// paper each region is currently showing.
    ///
    /// <para><b>The central invariant of the whole game:</b> folding never moves a tile between
    /// coordinates. A logical coordinate always addresses the same square of the puzzle; what a fold
    /// changes is which of that square's two faces is in play. Matching, gravity and adjacency
    /// therefore stay ordinary match-3 rules, and the "two boards in one" feeling comes entirely from
    /// <see cref="SideAt"/> switching under them.</para>
    /// </summary>
    public sealed class FoldTopology
    {
        private readonly FoldRegion[] _regions;
        private readonly int[] _regionIndexByCell; // width*height, -1 when the cell folds with nothing
        private readonly SurfaceSide[] _currentSides;
        private readonly int[] _foldCounts;

        public int Width { get; }
        public int Height { get; }

        public IReadOnlyList<FoldRegion> Regions => _regions;
        public int RegionCount => _regions.Length;

        public FoldTopology(int width, int height, IEnumerable<FoldRegion> regions)
        {
            Width = width;
            Height = height;
            _regions = regions != null ? new List<FoldRegion>(regions).ToArray() : Array.Empty<FoldRegion>();
            _regionIndexByCell = new int[width * height];
            for (int i = 0; i < _regionIndexByCell.Length; i++) _regionIndexByCell[i] = -1;

            _currentSides = new SurfaceSide[_regions.Length];
            _foldCounts = new int[_regions.Length];

            for (int i = 0; i < _regions.Length; i++)
            {
                _currentSides[i] = _regions[i].InitialSide;
                foreach (var c in _regions[i].Area.Coords())
                {
                    if (!InBounds(c)) continue;
                    int idx = Index(c);
                    if (_regionIndexByCell[idx] != -1)
                    {
                        throw new InvalidOperationException(
                            $"Fold regions overlap at {c}: '{_regions[_regionIndexByCell[idx]].Name}' and '{_regions[i].Name}'.");
                    }

                    _regionIndexByCell[idx] = i;
                }
            }
        }

        private FoldTopology(FoldTopology source)
        {
            Width = source.Width;
            Height = source.Height;
            _regions = source._regions;                                    // definitions are immutable, share them
            _regionIndexByCell = source._regionIndexByCell;                // derived from definitions, also shareable
            _currentSides = (SurfaceSide[])source._currentSides.Clone();
            _foldCounts = (int[])source._foldCounts.Clone();
        }

        public FoldTopology Clone() => new FoldTopology(this);

        public bool InBounds(GridCoord c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

        private int Index(GridCoord c) => c.Y * Width + c.X;

        /// <summary>Index into <see cref="Regions"/>, or -1 when this coordinate is not foldable.</summary>
        public int RegionIndexAt(GridCoord c) => InBounds(c) ? _regionIndexByCell[Index(c)] : -1;

        public FoldRegion RegionAt(GridCoord c)
        {
            int i = RegionIndexAt(c);
            return i >= 0 ? _regions[i] : null;
        }

        public int IndexOfRegionId(int regionId)
        {
            for (int i = 0; i < _regions.Length; i++)
            {
                if (_regions[i].Id == regionId) return i;
            }

            return -1;
        }

        public FoldRegion RegionById(int regionId)
        {
            int i = IndexOfRegionId(regionId);
            return i >= 0 ? _regions[i] : null;
        }

        /// <summary>Which face of the paper is currently in play at this coordinate.</summary>
        public SurfaceSide SideAt(GridCoord c)
        {
            int i = RegionIndexAt(c);
            return i >= 0 ? _currentSides[i] : SurfaceSide.Front;
        }

        public SurfaceSide SideOfRegion(int regionIndex) => _currentSides[regionIndex];

        public int FoldCountOfRegion(int regionIndex) => _foldCounts[regionIndex];

        public bool IsFolded(int regionIndex) => _currentSides[regionIndex] != _regions[regionIndex].InitialSide;

        /// <summary>Flips a region and returns the side now showing. Callers must validate first.</summary>
        public SurfaceSide Flip(int regionIndex)
        {
            _currentSides[regionIndex] = _currentSides[regionIndex] == SurfaceSide.Front
                ? SurfaceSide.Back
                : SurfaceSide.Front;
            _foldCounts[regionIndex]++;
            return _currentSides[regionIndex];
        }

        /// <summary>Used by save/load and by the editor preview; does not count as a fold.</summary>
        public void ForceSide(int regionIndex, SurfaceSide side, int foldCount = -1)
        {
            _currentSides[regionIndex] = side;
            if (foldCount >= 0) _foldCounts[regionIndex] = foldCount;
        }

        public Direction GravityAt(GridCoord c, Direction defaultGravity)
        {
            int i = RegionIndexAt(c);
            return i >= 0 ? _regions[i].GravityFor(_currentSides[i]) : defaultGravity;
        }

        /// <summary>
        /// A Story Seam runs between two adjacent cells whose faces disagree. Matches spanning a seam
        /// mint the game's signature booster, so this predicate is hot — keep it allocation free.
        /// </summary>
        public bool IsSeamEdge(GridCoord a, GridCoord b) => SideAt(a) != SideAt(b);

        /// <summary>True when this coordinate touches at least one seam edge.</summary>
        public bool IsSeamCell(GridCoord c)
        {
            var side = SideAt(c);
            for (int d = 0; d < 4; d++)
            {
                var n = c.Step((Direction)d);
                if (InBounds(n) && SideAt(n) != side) return true;
            }

            return false;
        }

        /// <summary>
        /// Cells lying against the crease of their own region — the physical fold line, which exists
        /// whether or not the region is currently turned over. This is what a Chain Fold grabs: a
        /// booster resting on the crease gets dragged through the paper when the page turns.
        /// </summary>
        public bool IsFoldLineCell(GridCoord c)
        {
            int region = RegionIndexAt(c);
            if (region < 0) return false;

            for (int d = 0; d < 4; d++)
            {
                var n = c.Step((Direction)d);
                if (!InBounds(n)) continue;
                if (RegionIndexAt(n) != region) return true;
            }

            return false;
        }

        /// <summary>Fold-line cells of one region, in stable order.</summary>
        public List<GridCoord> CollectFoldLineCells(int regionIndex)
        {
            var result = new List<GridCoord>();
            if (regionIndex < 0 || regionIndex >= _regions.Length) return result;

            foreach (var c in _regions[regionIndex].Area.Coords())
            {
                if (InBounds(c) && IsFoldLineCell(c)) result.Add(c);
            }

            return result;
        }

        /// <summary>Every coordinate that currently borders a seam, ordered for stable iteration.</summary>
        public List<GridCoord> CollectSeamCells()
        {
            var result = new List<GridCoord>();
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var c = new GridCoord(x, y);
                if (IsSeamCell(c)) result.Add(c);
            }

            return result;
        }
    }
}
