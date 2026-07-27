using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Folding
{
    /// <summary>Everything the view needs to animate one fold, computed without touching Unity.</summary>
    public readonly struct FoldAnimationPlan
    {
        public readonly int RegionId;
        public readonly GridRect Area;
        public readonly Axis FlipAxis;
        public readonly SurfaceSide NewSide;

        /// <summary>
        /// Position of the crease in board space. For a vertical flip axis this is an X coordinate; for a
        /// horizontal one, a Y. Fractional because the crease runs between cells, not through them.
        /// </summary>
        public readonly float HingePosition;

        /// <summary>Seconds of delay per cell of distance from the crease, for the ripple.</summary>
        public readonly float PerCellDelay;

        public FoldAnimationPlan(int regionId, GridRect area, Axis flipAxis, SurfaceSide newSide,
            float hingePosition, float perCellDelay)
        {
            RegionId = regionId;
            Area = area;
            FlipAxis = flipAxis;
            NewSide = newSide;
            HingePosition = hingePosition;
            PerCellDelay = perCellDelay;
        }

        /// <summary>How long into the fold this cell should start turning.</summary>
        public float DelayFor(GridCoord coord)
        {
            float distance = FlipAxis == Axis.Vertical
                ? System.Math.Abs(coord.X + 0.5f - HingePosition)
                : System.Math.Abs(coord.Y + 0.5f - HingePosition);
            return distance * PerCellDelay;
        }
    }

    /// <summary>
    /// Bridges logical board space and the space the view draws in.
    ///
    /// <para><b>Render position equals logical position — always.</b> That is a design decision, not an
    /// oversight. A real page turn mirrors its content, and mirroring here would tear the board's
    /// legibility apart at exactly the wrong moment: two tiles that are logical neighbours across a
    /// Story Seam would land on opposite sides of the screen, and seam matches are the whole point of the
    /// mechanic. So the back of every page is printed to be read from the back, the flip is a pure
    /// animation, and the puzzle underneath stays an ordinary, readable grid.</para>
    ///
    /// <para>What the view actually needs from folding is timing and geometry, and that is what this
    /// class provides.</para>
    /// </summary>
    public static class SurfaceMapper
    {
        /// <summary>Identity by design. Kept as a call site so the intent is documented where it is used.</summary>
        public static GridCoord RenderCoord(GridCoord logical) => logical;

        public static FoldAnimationPlan PlanFor(FoldRegion region, SurfaceSide newSide, float perCellDelay = 0.035f)
        {
            float hinge = region.FlipAxis == Axis.Vertical
                ? region.Area.X + region.Area.Width * 0.5f
                : region.Area.Y + region.Area.Height * 0.5f;

            return new FoldAnimationPlan(region.Id, region.Area, region.FlipAxis, newSide, hinge, perCellDelay);
        }

        /// <summary>
        /// Total duration of the flip, so the command queue knows how long to hold before the next
        /// board event is allowed to play.
        /// </summary>
        public static float DurationOf(in FoldAnimationPlan plan, float baseDuration = 0.45f)
        {
            int span = plan.FlipAxis == Axis.Vertical ? plan.Area.Width : plan.Area.Height;
            return baseDuration + span * 0.5f * plan.PerCellDelay;
        }
    }
}
