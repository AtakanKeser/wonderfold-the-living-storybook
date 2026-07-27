using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;

namespace Wonderfold.Core.Boosters
{
    /// <summary>
    /// Turns the shape of a match into the booster it earns.
    ///
    /// <para>The ladder follows familiar match-3 language — four in a line is a rocket, an L or T is a
    /// bomb, five in a line clears a colour — so nobody has to relearn the game.
    /// The one addition is the Golden Stitch, which only exists here: a long enough match that spans a
    /// Story Seam is the single most valuable thing you can set up, and it is the reason to fold at all.
    /// </para>
    /// </summary>
    public static class BoosterFactory
    {
        public static bool TryCreate(GameRules rules, MatchGroup group, out BoosterType type,
            out BoosterOrientation orientation)
        {
            type = BoosterType.None;
            orientation = BoosterOrientation.None;

            if (group.CrossesSeam && group.Count >= rules.SeamStitchMinLength)
            {
                type = BoosterType.GoldenStitch;
                orientation = group.LineOrientation;
                return true;
            }

            switch (group.Shape)
            {
                case MatchShape.Large:
                    type = BoosterType.OrigamiBird;
                    return true;
                case MatchShape.Line5:
                    type = BoosterType.PrismBookmark;
                    return true;
                case MatchShape.Corner:
                    type = BoosterType.InkBloom;
                    return true;
                case MatchShape.Line4:
                    type = BoosterType.RibbonRocket;
                    orientation = group.LineOrientation;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Chain Fold upgrade path. A booster caught on the seam when the page turns is pulled through
        /// and comes out as the next thing up the ladder.
        /// </summary>
        public static BoosterType Upgrade(BoosterType type)
        {
            switch (type)
            {
                case BoosterType.RibbonRocket: return BoosterType.InkBloom;
                case BoosterType.InkBloom: return BoosterType.OrigamiBird;
                case BoosterType.OrigamiBird: return BoosterType.PrismBookmark;
                case BoosterType.PrismBookmark: return BoosterType.GoldenStitch;
                case BoosterType.GoldenStitch: return BoosterType.GoldenStitch; // already the top
                default: return type;
            }
        }

        public static string DisplayName(BoosterType type)
        {
            switch (type)
            {
                case BoosterType.RibbonRocket: return "Ribbon Rocket";
                case BoosterType.InkBloom: return "Ink Bloom";
                case BoosterType.OrigamiBird: return "Origami Bird";
                case BoosterType.PrismBookmark: return "Prism Bookmark";
                case BoosterType.GoldenStitch: return "Golden Stitch";
                default: return "None";
            }
        }
    }
}
