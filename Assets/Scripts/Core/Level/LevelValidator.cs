using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Level
{
    public enum ValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public readonly struct ValidationIssue
    {
        public readonly ValidationSeverity Severity;
        public readonly string Message;

        public ValidationIssue(ValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public override string ToString() => $"[{Severity}] {Message}";
    }

    /// <summary>
    /// Catches the authoring mistakes that are cheap to make and expensive to find by playing:
    /// a goal asking for a colour the level never spawns, a fold region hanging off the edge of the page,
    /// two gravity zones pointing into each other, a character with nowhere to walk.
    ///
    /// <para>The Level Laboratory runs this on every keystroke and the build runs it over every level, so
    /// a broken level cannot ship quietly.</para>
    /// </summary>
    public static class LevelValidator
    {
        public static List<ValidationIssue> Validate(LevelDefinition level)
        {
            var issues = new List<ValidationIssue>();
            if (level == null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "Level is null."));
                return issues;
            }

            ValidateShape(level, issues);
            ValidateFolds(level, issues);
            ValidateGoals(level, issues);

            // Everything past this point needs a real board; bail out if we could not build one.
            BoardModel board;
            try
            {
                board = LevelBuilder.Build(level, 1);
            }
            catch (System.Exception e)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Level fails to build: {e.Message}"));
                return issues;
            }

            ValidateSpawners(board, issues);
            ValidateGravity(board, issues);
            ValidatePortals(level, board, issues);
            ValidateWalkers(level, board, issues);
            ValidateObstacleGroups(board, issues);

            return issues;
        }

        public static bool HasErrors(List<ValidationIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == ValidationSeverity.Error) return true;
            }

            return false;
        }

        private static void ValidateShape(LevelDefinition level, List<ValidationIssue> issues)
        {
            if (level.Width < 3 || level.Height < 3)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "Board must be at least 3x3."));

            if (level.Moves <= 0)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "Level has no moves."));

            if (level.Front.IsEmpty)
                issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                    "Front surface has no authored layout; a default full board will be generated."));

            CheckLayerSize(level.Front, level, "front", issues);
            CheckLayerSize(level.Back, level, "back", issues);
        }

        private static void CheckLayerSize(SurfaceSpec surface, LevelDefinition level, string label,
            List<ValidationIssue> issues)
        {
            if (surface.Tiles == null || surface.Tiles.Count == 0) return;

            if (surface.Tiles.Count != level.Height)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{label}.tiles has {surface.Tiles.Count} rows but the board is {level.Height} tall."));
            }

            for (int i = 0; i < surface.Tiles.Count; i++)
            {
                if (surface.Tiles[i] != null && surface.Tiles[i].Length != level.Width)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"{label}.tiles row {i} is {surface.Tiles[i].Length} wide but the board is {level.Width}."));
                }
            }
        }

        private static void ValidateFolds(LevelDefinition level, List<ValidationIssue> issues)
        {
            var seenIds = new HashSet<int>();
            for (int i = 0; i < level.FoldRegions.Count; i++)
            {
                var region = level.FoldRegions[i];

                if (!seenIds.Add(region.Id))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Duplicate fold region id {region.Id}."));

                if (region.Width <= 0 || region.Height <= 0)
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Fold region {region.Id} is empty."));

                if (region.X < 0 || region.Y < 0 ||
                    region.X + region.Width > level.Width || region.Y + region.Height > level.Height)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"Fold region {region.Id} extends past the edge of the page."));
                }

                if (region.LockGroup != 0 && !HasFoldLock(level, region.LockGroup))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"Fold region {region.Id} is locked to group {region.LockGroup} but no Fold Lock uses that group — it can never be folded."));
                }

                if (region.MeterCost > (level.FoldMeterMax > 0 ? level.FoldMeterMax : 100))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"Fold region {region.Id} costs more meter than the level's meter can ever hold."));
                }
            }
        }

        private static bool HasFoldLock(LevelDefinition level, int group)
        {
            return SurfaceHasLock(level.Front, group) || SurfaceHasLock(level.Back, group);
        }

        private static bool SurfaceHasLock(SurfaceSpec surface, int group)
        {
            if (surface.Obstacles == null) return false;
            for (int r = 0; r < surface.Obstacles.Count; r++)
            {
                var row = surface.Obstacles[r];
                if (row == null) continue;
                for (int c = 0; c < row.Length; c++)
                {
                    if (row[c] != 'L') continue;
                    int cellGroup = 0;
                    if (surface.Groups != null && r < surface.Groups.Count)
                    {
                        var groupRow = surface.Groups[r];
                        if (groupRow != null && c < groupRow.Length && groupRow[c] >= '0' && groupRow[c] <= '9')
                            cellGroup = groupRow[c] - '0';
                    }

                    if (cellGroup == group) return true;
                }
            }

            return false;
        }

        private static void ValidateGoals(LevelDefinition level, List<ValidationIssue> issues)
        {
            if (level.Goals.Count == 0)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "Level has no goals."));
                return;
            }

            var palette = new HashSet<TileColor>();
            for (int i = 0; i < level.SpawnTables.Count; i++)
            {
                for (int c = 0; c < level.SpawnTables[i].Colors.Count; c++) palette.Add(level.SpawnTables[i].Colors[c]);
            }

            bool hasFoldableRegion = level.FoldRegions.Count > 0;

            for (int i = 0; i < level.Goals.Count; i++)
            {
                var goal = level.Goals[i];
                switch (goal.Type)
                {
                    case "collect":
                        if (palette.Count > 0 && !palette.Contains(goal.Color))
                        {
                            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                                $"Goal asks for {goal.Color} but no spawn table produces it."));
                        }

                        if (goal.Count <= 0)
                            issues.Add(new ValidationIssue(ValidationSeverity.Error, "Collect goal has no target count."));
                        break;

                    case "seam":
                    case "fold":
                    case "reconnect":
                        if (!hasFoldableRegion)
                        {
                            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                                $"Goal '{goal.Type}' needs at least one fold region."));
                        }

                        break;

                    case "walker":
                        if (!HasWalker(level, goal.WalkerId))
                        {
                            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                                $"Goal targets walker {goal.WalkerId}, which no modifier creates."));
                        }

                        break;
                }
            }
        }

        private static bool HasWalker(LevelDefinition level, int id)
        {
            for (int i = 0; i < level.Modifiers.Count; i++)
            {
                var modifier = level.Modifiers[i];
                for (int w = 0; w < modifier.Walkers.Count; w++)
                {
                    if (modifier.Walkers[w].Id == id) return true;
                }
            }

            return false;
        }

        private static void ValidateSpawners(BoardModel board, List<ValidationIssue> issues)
        {
            for (int side = 0; side < 2; side++)
            {
                int playable = 0;
                int spawners = 0;
                foreach (var coord in board.AllCoords())
                {
                    var cell = board.CellAt(coord, (SurfaceSide)side);
                    if (cell.IsVoid) continue;
                    playable++;
                    if (cell.IsSpawner) spawners++;
                }

                if (playable > 0 && spawners == 0)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"The {(SurfaceSide)side} surface has playable cells but no spawner — it can never refill."));
                }
            }

            ValidateBlockedSpawners(board, issues);
        }

        /// <summary>
        /// A wall parked on the cell tiles enter through starves everything downstream of it. Tiles can
        /// still trickle in diagonally, so this is a warning rather than an error — but it is the single
        /// most common reason an authored level measures as unwinnable, so it is worth shouting about.
        /// </summary>
        private static void ValidateBlockedSpawners(BoardModel board, List<ValidationIssue> issues)
        {
            for (int side = 0; side < 2; side++)
            {
                int blocked = 0;
                int total = 0;
                foreach (var coord in board.AllCoords())
                {
                    var cell = board.CellAt(coord, (SurfaceSide)side);
                    if (!cell.IsSpawner) continue;
                    total++;
                    if (!cell.HasBlockingObstacle) continue;

                    blocked++;
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                        $"{ObstacleCatalog.DisplayName(cell.Obstacle.Type)} sits on the {(SurfaceSide)side} spawner at {coord}; that lane can only refill diagonally."));
                }

                if (total > 0 && blocked >= total)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"Every spawner on the {(SurfaceSide)side} surface is walled off — the board can never refill."));
                }
            }
        }

        /// <summary>
        /// Two adjacent cells whose gravity points at each other would swap a tile back and forth for
        /// ever. The gravity pass is capped so it cannot hang, but the level would still be nonsense.
        /// </summary>
        private static void ValidateGravity(BoardModel board, List<ValidationIssue> issues)
        {
            for (int regionIndex = -1; regionIndex < board.Folds.RegionCount; regionIndex++)
            {
                // Check both fold states, since a fold can introduce a cycle that was not there before.
                for (int flip = 0; flip < 2; flip++)
                {
                    if (regionIndex >= 0) board.Folds.ForceSide(regionIndex, (SurfaceSide)flip);

                    foreach (var coord in board.AllCoords())
                    {
                        var gravity = board.GravityAt(coord);
                        var next = coord.Step(gravity);
                        if (!board.InBounds(next)) continue;
                        if (board.GravityAt(next) != gravity.Opposite()) continue;

                        issues.Add(new ValidationIssue(ValidationSeverity.Error,
                            $"Gravity loops between {coord} and {next}."));
                        return;
                    }

                    if (regionIndex < 0) break;
                }

                if (regionIndex >= 0)
                    board.Folds.ForceSide(regionIndex, board.Folds.Regions[regionIndex].InitialSide);
            }
        }

        private static void ValidatePortals(LevelDefinition level, BoardModel board, List<ValidationIssue> issues)
        {
            for (int i = 0; i < level.Portals.Count; i++)
            {
                var portal = level.Portals[i];
                var from = new GridCoord(portal.FromX, portal.FromY);
                var to = new GridCoord(portal.ToX, portal.ToY);

                if (!board.InBounds(from) || !board.InBounds(to))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Portal {i} points off the page."));
                    continue;
                }

                var destination = board.CellAt(to, portal.ToSide);
                if (destination.IsVoid || destination.HasBlockingObstacle)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"Portal {i} empties into a cell nothing can occupy."));
                }

                if (from == to && portal.FromSide == portal.ToSide)
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Portal {i} points at itself."));
            }
        }

        private static void ValidateWalkers(LevelDefinition level, BoardModel board, List<ValidationIssue> issues)
        {
            for (int m = 0; m < level.Modifiers.Count; m++)
            {
                var modifier = level.Modifiers[m];
                for (int w = 0; w < modifier.Walkers.Count; w++)
                {
                    var spec = modifier.Walkers[w];
                    var start = new GridCoord(spec.X, spec.Y);
                    var target = new GridCoord(spec.TargetX, spec.TargetY);

                    if (!board.InBounds(start) || !board.InBounds(target))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error,
                            $"Walker {spec.Id} starts or ends off the page."));
                        continue;
                    }

                    if (board.CellAt(start, spec.Side).IsVoid || board.CellAt(target, spec.Side).IsVoid)
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error,
                            $"Walker {spec.Id} starts or ends on a hole in the page."));
                    }

                    if (!IsReachableIgnoringObstacles(board, start, target, spec.Side))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error,
                            $"Walker {spec.Id} can never reach its target — the route is walled off by voids."));
                    }
                }
            }
        }

        /// <summary>
        /// Route check that ignores obstacles: obstacles are meant to be cleared, holes in the page are
        /// not. This is the difference between a hard level and an impossible one.
        /// </summary>
        private static bool IsReachableIgnoringObstacles(BoardModel board, GridCoord start, GridCoord target,
            SurfaceSide side)
        {
            var visited = new HashSet<GridCoord> { start };
            var queue = new Queue<GridCoord>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == target) return true;

                for (int d = 0; d < 4; d++)
                {
                    var next = current.Step((Direction)d);
                    if (!board.InBounds(next) || visited.Contains(next)) continue;
                    if (board.CellAt(next, side).IsVoid) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            return false;
        }

        private static void ValidateObstacleGroups(BoardModel board, List<ValidationIssue> issues)
        {
            var chainCounts = new Dictionary<int, int>();
            foreach (var cell in board.AllCells())
            {
                var obstacle = cell.Obstacle;
                if (obstacle == null || obstacle.Type != ObstacleType.PaperChain) continue;
                chainCounts.TryGetValue(obstacle.GroupId, out int count);
                chainCounts[obstacle.GroupId] = count + 1;
            }

            foreach (var pair in chainCounts)
            {
                if (pair.Value >= 2) continue;
                issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                    $"Paper Chain group {pair.Key} has a single link, so it behaves like an ordinary overlay."));
            }
        }
    }
}
