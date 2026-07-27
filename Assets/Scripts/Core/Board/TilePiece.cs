using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// A movable piece. Identity (<see cref="Id"/>) is stable for the lifetime of the piece so the
    /// view can follow the same sprite across falls, folds and swaps instead of re-binding by index.
    /// </summary>
    public sealed class TilePiece
    {
        public int Id { get; internal set; }

        /// <summary>Match colour. <see cref="TileColor.None"/> means "matches nothing".</summary>
        public TileColor Color { get; set; }

        public BoosterType Booster { get; set; }

        /// <summary>Line direction for <see cref="BoosterType.RibbonRocket"/> and <see cref="BoosterType.GoldenStitch"/>.</summary>
        public BoosterOrientation Orientation { get; set; }

        /// <summary>
        /// Blank tiles start colourless and copy the colour of the most recent match resolved
        /// next to them. See <see cref="ObstacleType.BlankStain"/> for the board-level cousin.
        /// </summary>
        public bool IsBlank { get; set; }

        /// <summary>
        /// Non-zero pairs this tile with the same id on the opposite surface. Used by
        /// Reconnect-the-Story goals and by Prism Bookmark's cross-surface reach.
        /// </summary>
        public int StoryLink { get; set; }

        public bool IsBooster => Booster != BoosterType.None;

        /// <summary>Plain coloured tiles are the only pieces that participate in colour matching.</summary>
        public bool IsMatchable => Color != TileColor.None && Booster == BoosterType.None;

        public TilePiece(int id, TileColor color)
        {
            Id = id;
            Color = color;
        }

        public TilePiece CloneWithId(int id) => new TilePiece(id, Color)
        {
            Booster = Booster,
            Orientation = Orientation,
            IsBlank = IsBlank,
            StoryLink = StoryLink
        };

        public override string ToString() =>
            Booster == BoosterType.None ? $"{Color}#{Id}" : $"{Booster}({Color})#{Id}";
    }
}
