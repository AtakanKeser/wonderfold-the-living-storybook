using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    public enum MoveKind
    {
        /// <summary>Drag one tile onto an orthogonal neighbour.</summary>
        Swap = 0,
        /// <summary>Tap a booster already on the board to set it off.</summary>
        ActivateBooster = 1,
        /// <summary>Turn a fold region over.</summary>
        Fold = 2,
        /// <summary>Spend a limited page tool on a coordinate. Tools never cost a board move.</summary>
        UseTool = 3
    }

    /// <summary>
    /// Consumables supplied by the meta layer. They deliberately live in the core vocabulary so a
    /// replay can describe every board mutation, but the core does not own their inventory or pricing.
    /// </summary>
    public enum PageTool
    {
        Hammer = 0,
        RibbonRocket = 1
    }

    /// <summary>
    /// A single player intent. Everything the player can do is one of these, which is what makes
    /// replays, bot agents and the "rerun this level from a seed" tooling possible.
    /// </summary>
    public readonly struct PlayerMove
    {
        public readonly MoveKind Kind;
        public readonly GridCoord A;
        public readonly GridCoord B;
        public readonly int RegionId;
        public readonly PageTool Tool;

        private PlayerMove(MoveKind kind, GridCoord a, GridCoord b, int regionId, PageTool tool = PageTool.Hammer)
        {
            Kind = kind;
            A = a;
            B = b;
            RegionId = regionId;
            Tool = tool;
        }

        public static PlayerMove Swap(GridCoord a, GridCoord b) =>
            new PlayerMove(MoveKind.Swap, a, b, 0);

        public static PlayerMove Swap(int ax, int ay, int bx, int by) =>
            Swap(new GridCoord(ax, ay), new GridCoord(bx, by));

        public static PlayerMove ActivateBooster(GridCoord at) =>
            new PlayerMove(MoveKind.ActivateBooster, at, GridCoord.Invalid, 0);

        public static PlayerMove Fold(int regionId) =>
            new PlayerMove(MoveKind.Fold, GridCoord.Invalid, GridCoord.Invalid, regionId);

        public static PlayerMove UseTool(PageTool tool, GridCoord at) =>
            new PlayerMove(MoveKind.UseTool, at, GridCoord.Invalid, 0, tool);

        public override string ToString()
        {
            switch (Kind)
            {
                case MoveKind.Swap: return $"Swap {A}<->{B}";
                case MoveKind.ActivateBooster: return $"Activate {A}";
                case MoveKind.Fold: return $"Fold region {RegionId}";
                case MoveKind.UseTool: return $"Use {Tool} at {A}";
                default: return Kind.ToString();
            }
        }
    }

    public readonly struct MoveValidation
    {
        public readonly bool IsValid;
        public readonly string Reason;

        private MoveValidation(bool isValid, string reason)
        {
            IsValid = isValid;
            Reason = reason;
        }

        public static readonly MoveValidation Valid = new MoveValidation(true, null);
        public static MoveValidation Invalid(string reason) => new MoveValidation(false, reason);
    }
}
