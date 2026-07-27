using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Folding
{
    /// <summary>
    /// Authored definition of a foldable section of the page. Immutable — the runtime "which side is
    /// showing" state lives in <see cref="FoldTopology"/> so a board can be cloned or rewound cheaply.
    /// </summary>
    public sealed class FoldRegion
    {
        public int Id { get; }
        public string Name { get; }
        public GridRect Area { get; }

        /// <summary>
        /// Axis of the line the paper rotates about.
        /// <see cref="Axis.Vertical"/> = the section swings left/right like a book page.
        /// <see cref="Axis.Horizontal"/> = it flips up/down like a calendar.
        /// </summary>
        public Axis FlipAxis { get; }

        /// <summary>Which way tiles fall while the front side is showing.</summary>
        public Direction FrontGravity { get; }

        /// <summary>Which way tiles fall while the back side is showing. This is where fold levels get their teeth.</summary>
        public Direction BackGravity { get; }

        public SurfaceSide InitialSide { get; }

        /// <summary>
        /// Non-zero means the region starts sealed: every <see cref="ObstacleType.FoldLock"/> carrying
        /// this group id must be cleared before the section will fold.
        /// </summary>
        public int LockGroup { get; }

        /// <summary>Moves consumed by folding. 0 in tutorial levels, 1 from the mid-chapter on.</summary>
        public int MoveCost { get; }

        /// <summary>Fold meter charge consumed. Levels that hand out free folds set this to 0.</summary>
        public int MeterCost { get; }

        /// <summary>0 = unlimited. Boss levels cap this to make folding a resource.</summary>
        public int MaxFolds { get; }

        public FoldRegion(
            int id,
            GridRect area,
            Axis flipAxis = Axis.Vertical,
            Direction frontGravity = Direction.Down,
            Direction backGravity = Direction.Down,
            SurfaceSide initialSide = SurfaceSide.Front,
            int lockGroup = 0,
            int moveCost = 0,
            int meterCost = 100,
            int maxFolds = 0,
            string name = null)
        {
            Id = id;
            Area = area;
            FlipAxis = flipAxis;
            FrontGravity = frontGravity;
            BackGravity = backGravity;
            InitialSide = initialSide;
            LockGroup = lockGroup;
            MoveCost = moveCost;
            MeterCost = meterCost;
            MaxFolds = maxFolds;
            Name = string.IsNullOrEmpty(name) ? $"Fold {id}" : name;
        }

        public Direction GravityFor(SurfaceSide side) => side == SurfaceSide.Front ? FrontGravity : BackGravity;

        /// <summary>Does the flip change how tiles fall? Used by the tutorial to flag "interesting" folds.</summary>
        public bool ChangesGravity => FrontGravity != BackGravity;

        public override string ToString() => $"{Name} {Area} flip={FlipAxis}";
    }
}
