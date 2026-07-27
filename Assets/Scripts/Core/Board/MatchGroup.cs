using System.Collections.Generic;
using Wonderfold.Core.Events;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>One connected set of same-coloured tiles that qualifies as a match.</summary>
    public sealed class MatchGroup
    {
        public readonly List<GridCoord> Cells = new List<GridCoord>();
        public TileColor Color;

        /// <summary>Longest horizontal run inside the group (0 when there is none of length 3+).</summary>
        public int LongestHorizontal;

        /// <summary>Longest vertical run inside the group.</summary>
        public int LongestVertical;

        /// <summary>True when the group spans two cells showing different faces of the page.</summary>
        public bool CrossesSeam;

        /// <summary>Where a booster born from this group should appear.</summary>
        public GridCoord Pivot;

        public int Count => Cells.Count;

        public MatchShape Shape
        {
            get
            {
                bool hasH = LongestHorizontal >= 3;
                bool hasV = LongestVertical >= 3;
                if (hasH && hasV) return Count >= 6 ? MatchShape.Large : MatchShape.Corner;

                int len = LongestHorizontal > LongestVertical ? LongestHorizontal : LongestVertical;
                if (len >= 6) return MatchShape.Large;
                if (len == 5) return MatchShape.Line5;
                if (len == 4) return MatchShape.Line4;
                return MatchShape.Line3;
            }
        }

        /// <summary>Axis a rocket born from this group should fire along.</summary>
        public BoosterOrientation LineOrientation =>
            LongestHorizontal >= LongestVertical ? BoosterOrientation.Horizontal : BoosterOrientation.Vertical;

        public bool Contains(GridCoord c)
        {
            for (int i = 0; i < Cells.Count; i++) if (Cells[i] == c) return true;
            return false;
        }

        public override string ToString() =>
            $"{Shape} {Color} x{Count}{(CrossesSeam ? " SEAM" : "")} @{Pivot}";
    }
}
