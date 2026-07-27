using System.Collections.Generic;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Live
{
    /// <summary>Which shape of crease a woven page is built around.</summary>
    public enum FoldArchetype
    {
        /// <summary>No fold at all — used for the gentlest pages, where the puzzle is the whole lesson.</summary>
        Flat = 0,
        /// <summary>A two-column strip down the middle of the page. The signature Wonderfold shape.</summary>
        VerticalStrip = 1,
        /// <summary>A two-row band across the page. Turns into a conveyor when its back gravity differs.</summary>
        HorizontalBand = 2,
        /// <summary>A square block in one corner, so only part of each row is ever folded.</summary>
        Corner = 3,
        /// <summary>Two strips that can be folded independently, producing two live seams.</summary>
        TwinStrips = 4
    }

    /// <summary>Everything the weaver needs to produce one page. Deterministic in every field.</summary>
    public sealed class WeaveRequest
    {
        /// <summary>The only source of randomness. Same seed in, byte-identical page out, forever.</summary>
        public int Seed;

        /// <summary>Stable identity used by replay codes and saves, e.g. <c>daily-207</c>.</summary>
        public string Key = "woven";

        /// <summary>Id given to the produced definition. Kept out of the authored 1..N range by callers.</summary>
        public int Id = 9000;

        public string Name;

        /// <summary>0 = first-week gentle, 1 = chapter finale. Drives every other choice below.</summary>
        public float Difficulty = 0.5f;

        /// <summary>Win rate the audition will tune the move budget towards.</summary>
        public float TargetWinRate = 0.55f;

        public bool AllowFold = true;

        public string Book = "woven-archive";
    }

    /// <summary>
    /// Writes a brand new page from a seed.
    ///
    /// <para>This exists because of an arithmetic problem the genre cannot escape: the market leaders ship
    /// thousands of hand-authored levels and add dozens more every week, and a player who catches up with
    /// the content churns. Wonderfold cannot out-author them. It can, however, do something they cannot:
    /// its gameplay core is pure, deterministic C# with a bot simulator attached, so a page can be
    /// invented <i>and measured</i> on the player's device before it is ever handed over. The weaver is
    /// the invention half; <see cref="PageAudition"/> is the measuring half, and nothing reaches a player
    /// without passing through both.</para>
    ///
    /// <para>The weaver deliberately produces the same kind of level a designer would: empty tile layers
    /// that the engine settles at start, a crease with a real reason to be turned, and obstacles that are
    /// never parked on the cells tiles enter through. It writes authored-format data, so a woven page can
    /// be serialised, opened in the Level Laboratory and hand-edited like any other.</para>
    /// </summary>
    public static class PageWeaver
    {
        /// <summary>
        /// The move budget a woven page may be tuned to. The authored chapter spans 16–46 moves on 8×8
        /// boards, and the bounds are kept close to that on purpose: a page tuned to eight moves or to
        /// seventy is technically inside its win-rate band and is still not a level anyone wants to play.
        /// </summary>
        public const int MinMoves = 12;
        public const int MaxMoves = 48;

        private static readonly TileColor[] Palette =
        {
            TileColor.Crimson, TileColor.Azure, TileColor.Meadow,
            TileColor.Amber, TileColor.Violet, TileColor.Blush
        };

        private static readonly string[] Adjectives =
        {
            "Whispering", "Lantern", "Folded", "Midnight", "Gilded", "Paper", "Restless", "Hushed",
            "Crooked", "Velvet", "Wandering", "Tallow", "Starlit", "Inkwell", "Moth-Eaten", "Brass"
        };

        private static readonly string[] Nouns =
        {
            "Marquee", "Crease", "Carousel", "Gallery", "Stairwell", "Balcony", "Aviary", "Bandstand",
            "Hall of Mirrors", "Ticket Line", "Rooftop", "Colonnade", "Lantern Row", "Menagerie",
            "Fountain", "Puppet Stage"
        };

        public static LevelDefinition Weave(WeaveRequest request)
        {
            if (request == null) request = new WeaveRequest();
            var rng = new DeterministicRandom(request.Seed);
            float difficulty = Clamp01(request.Difficulty);

            var level = new LevelDefinition
            {
                Id = request.Id,
                Book = request.Book,
                ChapterIndex = 0,
                DefaultGravity = Direction.Down,
                DesignWinRate = request.TargetWinRate,
                DesignWinRateTolerance = 0.12f
            };

            int size = difficulty < 0.32f ? 7 : difficulty < 0.68f ? 8 : rng.Chance(0.55) ? 9 : 8;
            level.Width = size;
            level.Height = size;

            int colours = difficulty < 0.30f ? 4 : difficulty < 0.72f ? 5 : 6;
            var table = new SpawnTableSpec { Group = 0 };
            for (int i = 0; i < colours; i++) table.Colors.Add(Palette[i]);
            level.SpawnTables.Add(table);

            var archetype = ChooseArchetype(rng, difficulty, request.AllowFold);
            BuildFolds(level, rng, difficulty, archetype);

            // Spawners are decided by gravity, not by hand, so ask a scratch board where they landed
            // rather than trying to predict it. Blocking obstacles must never sit on one — a wall on the
            // cell tiles enter through is the single most common way a level measures as unwinnable.
            var forbidden = CollectSpawnerCells(level);

            var plan = PlaceObstacles(level, rng, difficulty, forbidden);
            BuildGoals(level, rng, difficulty, plan, archetype);
            BuildModifiers(level, difficulty, rng);

            level.Moves = EstimateMoves(level, difficulty, plan, archetype);
            level.Name = request.Name ?? ComposeName(rng);
            level.DioramaPieceId = null;
            level.TutorialKey = null;

            return level;
        }

        // ------------------------------------------------------------------ folds

        private static FoldArchetype ChooseArchetype(DeterministicRandom rng, float difficulty, bool allowFold)
        {
            if (!allowFold) return FoldArchetype.Flat;

            // A page with no crease is a fine breather, but it is not what this game is for; keep it rare
            // and confined to the easiest end of the curve.
            if (difficulty < 0.22f && rng.Chance(0.5)) return FoldArchetype.Flat;

            if (difficulty >= 0.78f && rng.Chance(0.35)) return FoldArchetype.TwinStrips;

            int roll = rng.NextInt(3);
            return roll == 0 ? FoldArchetype.VerticalStrip
                : roll == 1 ? FoldArchetype.HorizontalBand
                : FoldArchetype.Corner;
        }

        private static void BuildFolds(LevelDefinition level, DeterministicRandom rng, float difficulty,
            FoldArchetype archetype)
        {
            int meterCost = 100 - (int)(difficulty * 25f);   // harder pages charge the meter a touch cheaper
            meterCost = meterCost < 40 ? 40 : meterCost > 100 ? 100 : meterCost;

            switch (archetype)
            {
                case FoldArchetype.Flat:
                    return;

                case FoldArchetype.VerticalStrip:
                {
                    int x = rng.Range(2, level.Width - 3);
                    level.FoldRegions.Add(Strip(1, "Mirror Strip", x, 0, 2, level.Height, Axis.Vertical,
                        meterCost, SidewaysGravity(rng, difficulty, Axis.Vertical)));
                    return;
                }

                case FoldArchetype.HorizontalBand:
                {
                    int y = rng.Range(2, level.Height - 3);
                    level.FoldRegions.Add(Strip(1, "Turning Band", 0, y, level.Width, 2, Axis.Horizontal,
                        meterCost, SidewaysGravity(rng, difficulty, Axis.Horizontal)));
                    return;
                }

                case FoldArchetype.Corner:
                {
                    int span = level.Width >= 9 ? 4 : 3;
                    int x = rng.Chance(0.5) ? 0 : level.Width - span;
                    int y = rng.Chance(0.5) ? 0 : level.Height - span;
                    level.FoldRegions.Add(Strip(1, "Dog-Eared Corner", x, y, span, span,
                        rng.Chance(0.5) ? Axis.Vertical : Axis.Horizontal, meterCost, Direction.Down));
                    return;
                }

                case FoldArchetype.TwinStrips:
                {
                    // Two creases the player can turn independently: the interesting decision is which
                    // one to spend the meter on, and whether to leave a booster on either crease.
                    int left = 1;
                    int right = level.Width - 3;
                    level.FoldRegions.Add(Strip(1, "First Crease", left, 0, 2, level.Height, Axis.Vertical,
                        meterCost, Direction.Down));
                    level.FoldRegions.Add(Strip(2, "Second Crease", right, 0, 2, level.Height, Axis.Vertical,
                        meterCost, SidewaysGravity(rng, difficulty, Axis.Vertical)));
                    return;
                }
            }
        }

        /// <summary>
        /// A crease whose reverse falls sideways is the mechanic at its best — the strip becomes a
        /// conveyor the moment it turns. It is also the sharpest difficulty spike the generator has, so
        /// it is gated behind difficulty and kept to shapes whose upstream edge is a full board edge.
        /// </summary>
        private static Direction SidewaysGravity(DeterministicRandom rng, float difficulty, Axis flipAxis)
        {
            if (difficulty < 0.45f || !rng.Chance(0.30 + difficulty * 0.25)) return Direction.Down;
            return flipAxis == Axis.Vertical
                ? (rng.Chance(0.5) ? Direction.Right : Direction.Left)
                : (rng.Chance(0.5) ? Direction.Right : Direction.Left);
        }

        private static FoldRegionSpec Strip(int id, string name, int x, int y, int width, int height,
            Axis axis, int meterCost, Direction backGravity) => new FoldRegionSpec
        {
            Id = id,
            Name = name,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            FlipAxis = axis,
            FrontGravity = Direction.Down,
            BackGravity = backGravity,
            InitialSide = SurfaceSide.Front,
            MeterCost = meterCost,
            MoveCost = 0
        };

        // ------------------------------------------------------------------ obstacles

        /// <summary>What the obstacle pass actually put on the page. Read back by the goal pass.</summary>
        private sealed class ObstaclePlan
        {
            public int TornPages;
            public int WaxSeals;
            public int Knots;
            public int ChainLinks;
            public int TotalBlocking;
        }

        private static ObstaclePlan PlaceObstacles(LevelDefinition level, DeterministicRandom rng,
            float difficulty, HashSet<CellRef> forbidden)
        {
            var plan = new ObstaclePlan();
            var front = NewLayer(level, '.');
            var back = NewLayer(level, '.');
            var frontGroups = NewLayer(level, '0');
            var backGroups = NewLayer(level, '0');

            int cells = level.Width * level.Height;
            int blockingBudget = (int)(cells * (0.05f + difficulty * 0.10f));

            int tornPages = 2 + (int)(difficulty * 6f);
            if (tornPages > blockingBudget) tornPages = blockingBudget;
            plan.TornPages = Scatter(level, rng, forbidden, front, back, 'T', tornPages, true);

            if (difficulty >= 0.34f)
            {
                int seals = 1 + (int)(difficulty * 4f);
                int room = blockingBudget - plan.TornPages;
                if (seals > room) seals = room < 0 ? 0 : room;
                // Wax seals only yield to a blast. They belong on the hidden face, where finding them is
                // the reason to fold, but never in numbers that outrun the boosters a page can produce.
                plan.WaxSeals = Scatter(level, rng, forbidden, front, back, 'W', seals, true);
            }

            plan.TotalBlocking = plan.TornPages + plan.WaxSeals;

            if (difficulty >= 0.50f && level.FoldRegions.Count > 0)
            {
                // A knot is one obstacle threaded through both faces of a coordinate; cutting either side
                // releases both. Placing the pair is what makes it read as a single knot.
                int knots = 1 + (int)(difficulty * 3f);
                for (int i = 0; i < knots; i++)
                {
                    // Both halves of a knot have to be reachable, so it only makes sense inside a crease.
                    if (!TryFindFreeCoord(level, rng, front, back, true, out int row, out int column)) break;
                    front[row][column] = 'K';
                    back[row][column] = 'K';
                    plan.Knots++;
                }
            }

            if (difficulty >= 0.62f)
            {
                // Chains are only interesting in groups: every link has to go before any of them does.
                int links = 2 + rng.NextInt(3);
                bool onBack = level.FoldRegions.Count > 0 && rng.Chance(0.5);
                var layer = onBack ? back : front;
                var groups = onBack ? backGroups : frontGroups;
                for (int i = 0; i < links; i++)
                {
                    if (!TryFindFreeCoord(level, rng, front, back, onBack, out int row, out int column)) break;
                    layer[row][column] = 'C';
                    groups[row][column] = '1';
                    plan.ChainLinks++;
                }

                if (plan.ChainLinks == 1)
                {
                    // A one-link chain is just an overlay wearing a costume, and the validator says so.
                    for (int r = 0; r < level.Height; r++)
                    for (int c = 0; c < level.Width; c++)
                    {
                        if (layer[r][c] != 'C') continue;
                        layer[r][c] = '.';
                        groups[r][c] = '0';
                    }

                    plan.ChainLinks = 0;
                }
            }

            level.Front.Tiles = BlankTiles(level);
            level.Back.Tiles = BlankTiles(level);
            level.Front.Obstacles = ToRows(front);
            level.Back.Obstacles = ToRows(back);
            if (plan.ChainLinks > 0)
            {
                level.Front.Groups = ToRows(frontGroups);
                level.Back.Groups = ToRows(backGroups);
            }

            return plan;
        }

        /// <summary>
        /// Places <paramref name="count"/> copies of a glyph, biased towards the hidden face so that
        /// turning the page is where the level's problems actually live.
        /// </summary>
        private static int Scatter(LevelDefinition level, DeterministicRandom rng, HashSet<CellRef> forbidden,
            char[][] front, char[][] back, char glyph, int count, bool blocking)
        {
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                bool onBack = level.FoldRegions.Count > 0 && rng.Chance(0.62);
                var layer = onBack ? back : front;
                var side = onBack ? SurfaceSide.Back : SurfaceSide.Front;

                for (int attempt = 0; attempt < 24; attempt++)
                {
                    int row = rng.NextInt(level.Height);
                    int column = rng.NextInt(level.Width);
                    if (layer[row][column] != '.') continue;
                    if (front[row][column] != '.' || back[row][column] != '.') continue;

                    var coord = new GridCoord(column, level.Height - 1 - row);

                    // The back of a cell that belongs to no fold region can never be turned face up, so
                    // anything put there is invisible — and an "clear every torn page" goal that counts
                    // one of them is an unwinnable level. Measured at 8–42% win before this check existed.
                    if (onBack && !IsInsideAFoldRegion(level, coord)) continue;

                    if (blocking && forbidden.Contains(new CellRef(coord, side))) continue;

                    layer[row][column] = glyph;
                    placed++;
                    break;
                }
            }

            return placed;
        }

        private static bool IsInsideAFoldRegion(LevelDefinition level, GridCoord coord)
        {
            for (int i = 0; i < level.FoldRegions.Count; i++)
            {
                var region = level.FoldRegions[i];
                if (coord.X >= region.X && coord.X < region.X + region.Width &&
                    coord.Y >= region.Y && coord.Y < region.Y + region.Height)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The audition's second dial. When the move budget has run out of travel — a page that is still
        /// too easy at fourteen moves, or still too hard at forty-eight — the objectives themselves are
        /// what has to move. Only counted goals can be scaled; "clear every wax seal" has no dial.
        /// </summary>
        public static bool TryScaleGoals(LevelDefinition level, float factor)
        {
            if (level == null || factor <= 0f) return false;

            bool changed = false;
            for (int i = 0; i < level.Goals.Count; i++)
            {
                var goal = level.Goals[i];
                switch (goal.Type)
                {
                    case "collect":
                    {
                        // Rounding to a readable number must never swallow the whole adjustment: a 17%
                        // increase on a target of 20 rounds straight back to 20, and the tuner reads that
                        // as "the dial is stuck" and throws away a perfectly good page.
                        int next = RoundToNearest((int)(goal.Count * factor), 5);
                        if (next == goal.Count) next = goal.Count + (factor > 1f ? 5 : -5);
                        if (next < 10) next = 10;
                        if (next > level.Width * level.Height * 2) next = level.Width * level.Height * 2;
                        if (next == goal.Count) continue;
                        goal.Count = next;
                        changed = true;
                        break;
                    }

                    case "seam":
                    {
                        int next = (int)System.Math.Round(goal.Count * factor);
                        if (next == goal.Count) next = goal.Count + (factor > 1f ? 1 : -1);
                        if (next < 1) next = 1;
                        if (next > 20) next = 20;
                        if (next == goal.Count) continue;
                        goal.Count = next;
                        changed = true;
                        break;
                    }
                }
            }

            return changed;
        }

        private static bool TryFindFreeCoord(LevelDefinition level, DeterministicRandom rng, char[][] front,
            char[][] back, bool mustBeFoldable, out int row, out int column)
        {
            for (int attempt = 0; attempt < 32; attempt++)
            {
                row = rng.NextInt(level.Height);
                column = rng.NextInt(level.Width);
                if (front[row][column] != '.' || back[row][column] != '.') continue;
                if (mustBeFoldable &&
                    !IsInsideAFoldRegion(level, new GridCoord(column, level.Height - 1 - row))) continue;
                return true;
            }

            row = -1;
            column = -1;
            return false;
        }

        // ------------------------------------------------------------------ goals and pacing

        private static void BuildGoals(LevelDefinition level, DeterministicRandom rng, float difficulty,
            ObstaclePlan plan, FoldArchetype archetype)
        {
            // Calibrated against the shipped chapter: 8×8 pages ask for 23–40 tiles of a colour.
            int area = level.Width * level.Height;
            int collect = (int)(area * (0.40f + difficulty * 0.30f));
            collect = RoundTo(collect, 5);
            if (collect < 20) collect = 20;

            var colour = Palette[rng.NextInt(level.SpawnTables[0].Colors.Count)];
            level.Goals.Add(new GoalSpec { Type = "collect", Color = colour, Count = collect });

            // A second colour is the cheapest way to make a page feel authored rather than assembled: it
            // splits the player's attention without adding a rule to learn.
            if (difficulty >= 0.45f && rng.Chance(0.45))
            {
                var second = Palette[rng.NextInt(level.SpawnTables[0].Colors.Count)];
                if (second != colour)
                {
                    level.Goals.Add(new GoalSpec
                    {
                        Type = "collect",
                        Color = second,
                        Count = RoundTo((int)(collect * 0.7f), 5)
                    });
                }
            }

            if (plan.TornPages > 0)
                level.Goals.Add(new GoalSpec { Type = "obstacle", Obstacle = ObstacleType.TornPage });

            if (plan.WaxSeals > 0)
                level.Goals.Add(new GoalSpec { Type = "obstacle", Obstacle = ObstacleType.WaxSeal });

            if (plan.ChainLinks > 0)
                level.Goals.Add(new GoalSpec { Type = "obstacle", Obstacle = ObstacleType.PaperChain });

            if (archetype == FoldArchetype.Flat) return;

            if (difficulty >= 0.34f)
            {
                // The seam goal is what stops a woven page from being "a match-3 level that happens to
                // have a crease". It forces at least one match to be planned across the fold.
                int seams = 2 + (int)(difficulty * 5f);
                level.Goals.Add(new GoalSpec { Type = "seam", Count = seams });
            }
            else
            {
                // Below that, the page at least insists the crease gets turned once. A woven page that
                // never asks the player to fold is not a Wonderfold page.
                level.Goals.Add(new GoalSpec { Type = "fold", Count = 1 });
            }
        }

        private static void BuildModifiers(LevelDefinition level, float difficulty, DeterministicRandom rng)
        {
            if (difficulty < 0.70f || !rng.Chance(0.35)) return;

            // Vanishing Ink is the one modifier that raises pressure without changing the goal set, so it
            // is the safe way to make a late page feel like it is fighting back.
            level.Modifiers.Add(new ModifierSpec
            {
                Type = "vanishing-ink",
                Period = 4,
                Count = 1,
                StartTurn = 4
            });
        }

        /// <summary>
        /// A first guess only. The audition measures the page and moves this number until the win rate
        /// lands where the request asked for, so the estimate merely has to be in the right postcode.
        /// </summary>
        private static int EstimateMoves(LevelDefinition level, float difficulty, ObstaclePlan plan,
            FoldArchetype archetype)
        {
            int moves = 12;
            for (int i = 0; i < level.Goals.Count; i++)
            {
                var goal = level.Goals[i];
                switch (goal.Type)
                {
                    case "collect": moves += goal.Count / 3; break;
                    case "seam": moves += goal.Count * 2; break;
                    case "fold": moves += 3; break;
                    default: moves += 4; break;
                }
            }

            moves += plan.TotalBlocking / 2;
            if (archetype == FoldArchetype.TwinStrips) moves += 3;
            moves -= (int)(difficulty * 10f);
            return moves < MinMoves ? MinMoves : moves > MaxMoves ? MaxMoves : moves;
        }

        // ------------------------------------------------------------------ helpers

        private static HashSet<CellRef> CollectSpawnerCells(LevelDefinition level)
        {
            var probe = new LevelDefinition
            {
                Id = level.Id,
                Width = level.Width,
                Height = level.Height,
                Moves = 1,
                DefaultGravity = level.DefaultGravity
            };
            probe.SpawnTables.AddRange(level.SpawnTables);
            probe.FoldRegions.AddRange(level.FoldRegions);
            probe.Goals.Add(new GoalSpec { Type = "collect", Color = level.SpawnTables[0].Colors[0], Count = 1 });

            var board = LevelBuilder.Build(probe, 1);
            var spawners = new HashSet<CellRef>();
            foreach (var coord in board.AllCoords())
            {
                for (int side = 0; side < 2; side++)
                {
                    if (board.CellAt(coord, (SurfaceSide)side).IsSpawner)
                        spawners.Add(new CellRef(coord, (SurfaceSide)side));
                }
            }

            return spawners;
        }

        private static char[][] NewLayer(LevelDefinition level, char fill)
        {
            var rows = new char[level.Height][];
            for (int r = 0; r < level.Height; r++)
            {
                rows[r] = new char[level.Width];
                for (int c = 0; c < level.Width; c++) rows[r][c] = fill;
            }

            return rows;
        }

        private static List<string> ToRows(char[][] layer)
        {
            var rows = new List<string>(layer.Length);
            for (int r = 0; r < layer.Length; r++) rows.Add(new string(layer[r]));
            return rows;
        }

        private static List<string> BlankTiles(LevelDefinition level)
        {
            var rows = new List<string>(level.Height);
            var line = new string(LevelGlyphs.Empty, level.Width);
            for (int r = 0; r < level.Height; r++) rows.Add(line);
            return rows;
        }

        private static string ComposeName(DeterministicRandom rng) =>
            $"The {Adjectives[rng.NextInt(Adjectives.Length)]} {Nouns[rng.NextInt(Nouns.Length)]}";

        private static int RoundTo(int value, int step) => value / step * step;

        private static int RoundToNearest(int value, int step) => (value + step / 2) / step * step;

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
