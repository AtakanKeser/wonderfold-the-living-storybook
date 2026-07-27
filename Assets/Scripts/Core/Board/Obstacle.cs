using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>Runtime instance of an obstacle occupying (or covering) a cell.</summary>
    public sealed class Obstacle
    {
        public ObstacleType Type { get; }
        public int Health { get; internal set; }
        public int MaxHealth { get; }

        /// <summary>
        /// Ties instances together: Paper Chain links, Fold Lock keys per fold region,
        /// Story Knot pairs. Zero means "standalone".
        /// </summary>
        public int GroupId { get; }

        public Obstacle(ObstacleType type, int health = 0, int groupId = 0)
        {
            Type = type;
            MaxHealth = health > 0 ? health : ObstacleCatalog.DefaultHealth(type);
            Health = MaxHealth;
            GroupId = groupId;
        }

        public ObstacleLayer Layer => ObstacleCatalog.LayerOf(Type);
        public bool IsBlocking => Layer == ObstacleLayer.Blocking;
        public bool IsOverlay => Layer == ObstacleLayer.Overlay;
        public bool IsDead => Health <= 0;

        public Obstacle Clone() => new Obstacle(Type, MaxHealth, GroupId) { Health = Health };

        public override string ToString() => $"{Type}({Health}/{MaxHealth})" + (GroupId != 0 ? $"g{GroupId}" : "");
    }

    /// <summary>
    /// Single source of truth for obstacle behaviour. Keeping this as a lookup table rather than a
    /// class hierarchy means the simulator, the level validator and the editor all agree on the
    /// rules without instantiating anything.
    /// </summary>
    public static class ObstacleCatalog
    {
        public static ObstacleLayer LayerOf(ObstacleType type)
        {
            switch (type)
            {
                case ObstacleType.TornPage:
                case ObstacleType.WaxSeal:
                case ObstacleType.FoldLock:
                case ObstacleType.ArmourPlate:
                    return ObstacleLayer.Blocking;
                default:
                    return ObstacleLayer.Overlay;
            }
        }

        public static int DefaultHealth(ObstacleType type)
        {
            switch (type)
            {
                case ObstacleType.TornPage: return 2;
                // One blast, one seal. Wax seals are already immune to ordinary matching; giving them
                // health on top of that made them a wall rather than a puzzle — measured at 0% clear
                // across every level that used them.
                case ObstacleType.WaxSeal: return 1;
                case ObstacleType.ArmourPlate: return 2;
                case ObstacleType.PaperChain: return 1;
                case ObstacleType.StoryKnot: return 1;
                case ObstacleType.VanishingInk: return 1;
                case ObstacleType.BlankStain: return 1;
                case ObstacleType.FoldLock: return 1;
                default: return 1;
            }
        }

        /// <summary>Does a match happening in a neighbouring cell chip this obstacle?</summary>
        public static bool TakesAdjacentMatchDamage(ObstacleType type)
        {
            switch (type)
            {
                case ObstacleType.TornPage:
                case ObstacleType.ArmourPlate:
                    return true;
                // Wax seals and fold locks are deliberately immune: they exist to force booster usage.
                default:
                    return false;
            }
        }

        /// <summary>Does destroying the tile underneath chip this overlay?</summary>
        public static bool TakesUnderlyingClearDamage(ObstacleType type)
        {
            switch (type)
            {
                case ObstacleType.VanishingInk:
                case ObstacleType.PaperChain:
                case ObstacleType.StoryKnot:
                case ObstacleType.BlankStain:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Does a booster blast passing over this cell chip it?</summary>
        public static bool TakesBlastDamage(ObstacleType type) => type != ObstacleType.None;

        /// <summary>Blocking obstacles stop tiles falling through their cell.</summary>
        public static bool BlocksFall(ObstacleType type) => LayerOf(type) == ObstacleLayer.Blocking;

        /// <summary>Can the player drag the tile in this cell?</summary>
        public static bool BlocksSwap(ObstacleType type)
        {
            if (LayerOf(type) == ObstacleLayer.Blocking) return true;
            switch (type)
            {
                case ObstacleType.StoryKnot:
                case ObstacleType.PaperChain:
                case ObstacleType.VanishingInk:
                    return true;
                case ObstacleType.BlankStain:
                    return false; // stains are ugly, not sticky
                default:
                    return false;
            }
        }

        /// <summary>
        /// Chained obstacles only die when every member of their group has been reduced to zero
        /// health in the same resolution pass.
        /// </summary>
        public static bool IsGroupLinked(ObstacleType type) => type == ObstacleType.PaperChain;

        public static string DisplayName(ObstacleType type)
        {
            switch (type)
            {
                case ObstacleType.TornPage: return "Torn Page";
                case ObstacleType.WaxSeal: return "Wax Seal";
                case ObstacleType.VanishingInk: return "Vanishing Ink";
                case ObstacleType.StoryKnot: return "Story Knot";
                case ObstacleType.PaperChain: return "Paper Chain";
                case ObstacleType.FoldLock: return "Fold Lock";
                case ObstacleType.ArmourPlate: return "Armour Plate";
                case ObstacleType.BlankStain: return "Blank Stain";
                default: return type.ToString();
            }
        }
    }
}
