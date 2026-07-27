namespace Wonderfold.Core.Primitives
{
    /// <summary>
    /// Which physical side of the paper a cell belongs to. Every logical coordinate owns one
    /// Front cell and one Back cell; folding swaps which one is currently in play.
    /// </summary>
    public enum SurfaceSide
    {
        Front = 0,
        Back = 1
    }

    /// <summary>
    /// Colour identity of a movable tile. <see cref="None"/> is used by colourless pieces
    /// (fresh Blank tiles, boosters that carry no colour, walkers).
    /// </summary>
    public enum TileColor
    {
        None = 0,
        Crimson = 1,   // red   — "ink"
        Azure = 2,     // blue  — "moonlight"
        Meadow = 3,    // green — "vine"
        Amber = 4,     // yellow— "lantern"
        Violet = 5,    // purple— "dream"
        Blush = 6      // pink  — reserved for late-game books
    }

    public enum BoosterType
    {
        None = 0,
        /// <summary>Clears a row or column; curls onto the far surface when it meets a seam.</summary>
        RibbonRocket = 1,
        /// <summary>Ink explosion clearing a square neighbourhood; hurts sealed obstacles.</summary>
        InkBloom = 2,
        /// <summary>Homes onto the most valuable obstacle/goal tile on the board.</summary>
        OrigamiBird = 3,
        /// <summary>Colour clear. Also reaches story-linked tiles on the hidden surface.</summary>
        PrismBookmark = 4,
        /// <summary>Signature booster. Born from a match that crosses a Story Seam.</summary>
        GoldenStitch = 5
    }

    /// <summary>Orientation carried by directional boosters (rockets, stitches).</summary>
    public enum BoosterOrientation
    {
        None = 0,
        Horizontal = 1,
        Vertical = 2
    }

    public enum ObstacleType
    {
        None = 0,
        /// <summary>Two-stage blocking paper. Damaged by adjacent matches.</summary>
        TornPage = 1,
        /// <summary>Blocking. Immune to adjacent matches — only booster blasts break it.</summary>
        WaxSeal = 2,
        /// <summary>Overlay on a tile. Must be cleared by matching underneath; spreads over time.</summary>
        VanishingInk = 3,
        /// <summary>Ties the same coordinate on both surfaces together. Locks the tile until the twin clears.</summary>
        StoryKnot = 4,
        /// <summary>Multi-cell chain; every link must be cleared before any of them counts.</summary>
        PaperChain = 5,
        /// <summary>Blocking. Prevents its fold region from being folded until the matching key is collected.</summary>
        FoldLock = 6,
        /// <summary>Blocking armour plate used by pop-up boss levels.</summary>
        ArmourPlate = 7,
        /// <summary>Overlay marking a cell as drained by The Blank. Must be re-inked.</summary>
        BlankStain = 8
    }

    /// <summary>How an obstacle occupies its cell.</summary>
    public enum ObstacleLayer
    {
        /// <summary>Sits in the cell instead of a tile. Nothing can move through it.</summary>
        Blocking = 0,
        /// <summary>Sits on top of a tile. The tile can still be matched but cannot be swapped.</summary>
        Overlay = 1
    }

    /// <summary>How a cell participates in the puzzle.</summary>
    public enum CellRole
    {
        /// <summary>Not part of the board (hole in the page).</summary>
        Void = 0,
        Playable = 1,
        /// <summary>Playable and refills from off-board when its gravity upstream is empty.</summary>
        Spawner = 2
    }

    /// <summary>Reason a cascade step removed a tile — drives VFX selection and goal crediting.</summary>
    public enum ClearCause
    {
        Match = 0,
        Booster = 1,
        SeamStitch = 2,
        Shuffle = 3,
        LevelEnd = 4
    }

    public enum LevelOutcome
    {
        InProgress = 0,
        Won = 1,
        LostOutOfMoves = 2,
        LostBoardCollapsed = 3
    }
}
