using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Goals;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;

namespace Wonderfold.Core.Level
{
    /// <summary>
    /// Everything an authored level is, as plain data.
    ///
    /// <para>Levels are text: each surface is a stack of character layers — what is in the cell, what is
    /// covering it, which part of the hidden picture it belongs to. A designer can read a level in a diff,
    /// hand-edit one in a text editor, and the Level Laboratory round-trips the same file.</para>
    /// </summary>
    public sealed class LevelDefinition
    {
        public int Id;
        public string Name = "Untitled";

        /// <summary>Which storybook this page belongs to, e.g. "midnight-carnival".</summary>
        public string Book = "midnight-carnival";
        public int ChapterIndex;

        public int Width = 8;
        public int Height = 8;
        public int Moves = 25;
        public int FoldMeterMax;
        public Direction DefaultGravity = Direction.Down;

        /// <summary>
        /// The win rate this level is meant to have, as measured by the reference bot. A tutorial and a
        /// chapter finale cannot share one target band, so the intent is authored per level and the
        /// Level Laboratory flags drift against it rather than against a global number.
        /// Zero means "use the tool's default band".
        /// </summary>
        public float DesignWinRate;
        public float DesignWinRateTolerance = 0.12f;

        /// <summary>Optional per-level rule overrides. Null means the global defaults.</summary>
        public GameRules Rules;

        public readonly List<SpawnTableSpec> SpawnTables = new List<SpawnTableSpec>();
        public readonly List<FoldRegionSpec> FoldRegions = new List<FoldRegionSpec>();
        public readonly List<GoalSpec> Goals = new List<GoalSpec>();
        public readonly List<ModifierSpec> Modifiers = new List<ModifierSpec>();
        public readonly List<PortalSpec> Portals = new List<PortalSpec>();

        public SurfaceSpec Front = new SurfaceSpec();
        public SurfaceSpec Back = new SurfaceSpec();

        // ---- presentation / meta hooks (ignored by the core, consumed by Unity)
        /// <summary>Which piece of the diorama this level restores when it is beaten.</summary>
        public string DioramaPieceId;
        /// <summary>Tutorial script key shown the first time this level is opened.</summary>
        public string TutorialKey;
        public readonly List<string> IntroDialogue = new List<string>();
        public readonly List<string> OutroDialogue = new List<string>();

        public SurfaceSpec SurfaceFor(SurfaceSide side) => side == SurfaceSide.Front ? Front : Back;

        public List<ILevelGoal> CreateGoals()
        {
            var result = new List<ILevelGoal>();
            for (int i = 0; i < Goals.Count; i++)
            {
                var goal = Goals[i].Create();
                if (goal != null) result.Add(goal);
            }

            return result;
        }

        public List<ILevelModifier> CreateModifiers()
        {
            var result = new List<ILevelModifier>();
            for (int i = 0; i < Modifiers.Count; i++)
            {
                var modifier = Modifiers[i].Create();
                if (modifier != null) result.Add(modifier);
            }

            return result;
        }

        public LevelDefinition Clone() => LevelSerializer.FromJson(LevelSerializer.ToJson(this));
    }

    /// <summary>
    /// One face of the page, as character layers. Rows are authored top-first so the text in the file
    /// looks like the board on screen; the builder flips them into bottom-up board coordinates.
    /// </summary>
    public sealed class SurfaceSpec
    {
        /// <summary>Cell role plus any pre-placed tile. See <see cref="LevelGlyphs"/>.</summary>
        public List<string> Tiles = new List<string>();
        /// <summary>Obstacles covering or filling the cell. Optional.</summary>
        public List<string> Obstacles = new List<string>();
        /// <summary>Group ids for chains, locks and knots, as single digits. Optional.</summary>
        public List<string> Groups = new List<string>();
        /// <summary>'*' marks a cell belonging to the hidden illustration. Optional.</summary>
        public List<string> Illustration = new List<string>();
        /// <summary>Digit selecting which spawn table refills the cell. Optional, defaults to 0.</summary>
        public List<string> SpawnGroups = new List<string>();
        /// <summary>Digit pairing tiles across the page for Reconnect-the-Story. Optional.</summary>
        public List<string> StoryLinks = new List<string>();

        public bool IsEmpty => Tiles == null || Tiles.Count == 0;
    }

    public sealed class SpawnTableSpec
    {
        public int Group;
        public List<TileColor> Colors = new List<TileColor>();
        public List<int> Weights = new List<int>();

        public SpawnTable Create() => new SpawnTable(Group, Colors, Weights.Count > 0 ? Weights : null);
    }

    public sealed class FoldRegionSpec
    {
        public int Id;
        public string Name;
        public int X, Y, Width, Height;
        public Axis FlipAxis = Axis.Vertical;
        public Direction FrontGravity = Direction.Down;
        public Direction BackGravity = Direction.Down;
        public SurfaceSide InitialSide = SurfaceSide.Front;
        public int LockGroup;
        public int MoveCost;
        public int MeterCost = 100;
        public int MaxFolds;

        public Folding.FoldRegion Create() => new Folding.FoldRegion(
            Id, new GridRect(X, Y, Width, Height), FlipAxis, FrontGravity, BackGravity, InitialSide,
            LockGroup, MoveCost, MeterCost, MaxFolds, Name);
    }

    public sealed class PortalSpec
    {
        public int FromX, FromY;
        public SurfaceSide FromSide = SurfaceSide.Front;
        public int ToX, ToY;
        public SurfaceSide ToSide = SurfaceSide.Back;
    }

    public sealed class GoalSpec
    {
        /// <summary>collect | obstacle | illustration | seam | fold | reconnect | blank | walker</summary>
        public string Type = "collect";
        public TileColor Color = TileColor.Crimson;
        public ObstacleType Obstacle = ObstacleType.TornPage;
        public int Count;
        public int RegionId = -1;
        public int WalkerId;

        public ILevelGoal Create()
        {
            switch ((Type ?? string.Empty).ToLowerInvariant())
            {
                case "collect": return new CollectColorGoal(Color, Count);
                case "obstacle": return new ClearObstacleGoal(Obstacle, Count);
                case "illustration": return new RestoreIllustrationGoal(Count);
                case "seam": return new SeamMatchGoal(Count <= 0 ? 1 : Count);
                case "fold": return new FoldCountGoal(Count <= 0 ? 1 : Count, RegionId);
                case "reconnect": return new ReconnectStoryGoal(Count);
                case "blank": return new PurgeBlankGoal(Count <= 0 ? 1 : Count);
                case "walker": return new GuideWalkerGoal(WalkerId);
                default: return null;
            }
        }
    }

    public sealed class WalkerSpec
    {
        public int Id;
        public int X, Y;
        public int TargetX, TargetY;
        public SurfaceSide Side = SurfaceSide.Front;
        public int StepsPerTurn = 2;
        public string CharacterId = "quill";
    }

    public sealed class ModifierSpec
    {
        /// <summary>vanishing-ink | blank-tide | walker | boss</summary>
        public string Type = "vanishing-ink";
        public int Period = 3;
        public int Count = 1;
        public int StartTurn = 1;
        public int Amount;
        public readonly List<WalkerSpec> Walkers = new List<WalkerSpec>();
        /// <summary>Boss armour patterns, one list of rows per phase.</summary>
        public readonly List<List<string>> Phases = new List<List<string>>();

        public ILevelModifier Create()
        {
            switch ((Type ?? string.Empty).ToLowerInvariant())
            {
                case "vanishing-ink": return new VanishingInkModifier(Period, Count, StartTurn);
                case "blank-tide": return new BlankTideModifier(Period, Count, StartTurn);
                case "walker": return new WalkerModifier(Walkers);
                case "boss": return new PopUpBossModifier(Phases, Period, Count);
                default: return null;
            }
        }
    }

    /// <summary>
    /// The character alphabet used by authored level files. Kept here so the builder, the serialiser and
    /// the Level Laboratory palette can never drift apart.
    /// </summary>
    public static class LevelGlyphs
    {
        public const char Void = '#';
        public const char Empty = '.';
        public const char Spawner = 'S';
        public const char BlankTile = '?';

        public static bool TryColor(char c, out TileColor color)
        {
            switch (c)
            {
                case 'r': color = TileColor.Crimson; return true;
                case 'b': color = TileColor.Azure; return true;
                case 'g': color = TileColor.Meadow; return true;
                case 'y': color = TileColor.Amber; return true;
                case 'p': color = TileColor.Violet; return true;
                case 'k': color = TileColor.Blush; return true;
                default: color = TileColor.None; return false;
            }
        }

        public static char ToChar(TileColor color)
        {
            switch (color)
            {
                case TileColor.Crimson: return 'r';
                case TileColor.Azure: return 'b';
                case TileColor.Meadow: return 'g';
                case TileColor.Amber: return 'y';
                case TileColor.Violet: return 'p';
                case TileColor.Blush: return 'k';
                default: return Empty;
            }
        }

        public static bool TryBooster(char c, out BoosterType type, out BoosterOrientation orientation)
        {
            orientation = BoosterOrientation.None;
            switch (c)
            {
                case '-': type = BoosterType.RibbonRocket; orientation = BoosterOrientation.Horizontal; return true;
                case '|': type = BoosterType.RibbonRocket; orientation = BoosterOrientation.Vertical; return true;
                case '*': type = BoosterType.InkBloom; return true;
                case '^': type = BoosterType.OrigamiBird; return true;
                case '@': type = BoosterType.PrismBookmark; return true;
                case '=': type = BoosterType.GoldenStitch; orientation = BoosterOrientation.Horizontal; return true;
                default: type = BoosterType.None; return false;
            }
        }

        public static bool TryObstacle(char c, out ObstacleType type)
        {
            switch (c)
            {
                case 'T': type = ObstacleType.TornPage; return true;
                case 'W': type = ObstacleType.WaxSeal; return true;
                case 'V': type = ObstacleType.VanishingInk; return true;
                case 'K': type = ObstacleType.StoryKnot; return true;
                case 'C': type = ObstacleType.PaperChain; return true;
                case 'L': type = ObstacleType.FoldLock; return true;
                case 'A': type = ObstacleType.ArmourPlate; return true;
                case 'X': type = ObstacleType.BlankStain; return true;
                default: type = ObstacleType.None; return false;
            }
        }

        public static char ToChar(ObstacleType type)
        {
            switch (type)
            {
                case ObstacleType.TornPage: return 'T';
                case ObstacleType.WaxSeal: return 'W';
                case ObstacleType.VanishingInk: return 'V';
                case ObstacleType.StoryKnot: return 'K';
                case ObstacleType.PaperChain: return 'C';
                case ObstacleType.FoldLock: return 'L';
                case ObstacleType.ArmourPlate: return 'A';
                case ObstacleType.BlankStain: return 'X';
                default: return Empty;
            }
        }
    }
}
