using System.Collections.Generic;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Serialization;

namespace Wonderfold.Core.Level
{
    /// <summary>
    /// Round-trips a <see cref="LevelDefinition"/> through JSON.
    ///
    /// <para>Optional keys are omitted when they hold their default, so a simple level is a short file and
    /// a diff shows only what the designer actually changed. Loading tolerates missing keys entirely,
    /// which means adding a new field never invalidates the levels already authored.</para>
    /// </summary>
    public static class LevelSerializer
    {
        public const int FormatVersion = 1;

        // ------------------------------------------------------------------ write

        public static string ToJson(LevelDefinition level, bool pretty = true) =>
            ToJsonValue(level).ToJson(pretty);

        public static JsonValue ToJsonValue(LevelDefinition level)
        {
            var root = JsonValue.NewObject();
            root.Set("format", FormatVersion);
            root.Set("id", level.Id);
            root.Set("name", level.Name);
            root.Set("book", level.Book);
            root.SetIf("chapter", level.ChapterIndex);
            root.Set("width", level.Width);
            root.Set("height", level.Height);
            root.Set("moves", level.Moves);
            root.SetIf("foldMeterMax", level.FoldMeterMax);
            if (level.DesignWinRate > 0f) root.Set("designWinRate", level.DesignWinRate);
            if (System.Math.Abs(level.DesignWinRateTolerance - 0.12f) > 0.0001f)
                root.Set("designWinRateTolerance", level.DesignWinRateTolerance);
            if (level.DefaultGravity != Direction.Down) root.Set("gravity", level.DefaultGravity.ToString());
            root.SetIf("dioramaPiece", level.DioramaPieceId);
            root.SetIf("tutorial", level.TutorialKey);

            if (level.IntroDialogue.Count > 0) root.Set("intro", JsonValue.FromStrings(level.IntroDialogue));
            if (level.OutroDialogue.Count > 0) root.Set("outro", JsonValue.FromStrings(level.OutroDialogue));

            if (level.SpawnTables.Count > 0)
            {
                var array = JsonValue.NewArray();
                for (int i = 0; i < level.SpawnTables.Count; i++)
                {
                    var spec = level.SpawnTables[i];
                    var node = JsonValue.NewObject();
                    node.SetIf("group", spec.Group);
                    var colors = JsonValue.NewArray();
                    for (int c = 0; c < spec.Colors.Count; c++) colors.Add(JsonValue.Create(spec.Colors[c].ToString()));
                    node.Set("colors", colors);
                    if (spec.Weights.Count > 0) node.Set("weights", JsonValue.FromInts(spec.Weights));
                    array.Add(node);
                }

                root.Set("spawnTables", array);
            }

            if (level.FoldRegions.Count > 0)
            {
                var array = JsonValue.NewArray();
                for (int i = 0; i < level.FoldRegions.Count; i++)
                {
                    var spec = level.FoldRegions[i];
                    var node = JsonValue.NewObject();
                    node.Set("id", spec.Id);
                    node.SetIf("name", spec.Name);
                    node.Set("rect", JsonValue.FromInts(new[] { spec.X, spec.Y, spec.Width, spec.Height }));
                    if (spec.FlipAxis != Axis.Vertical) node.Set("flipAxis", spec.FlipAxis.ToString());
                    if (spec.FrontGravity != Direction.Down) node.Set("frontGravity", spec.FrontGravity.ToString());
                    if (spec.BackGravity != Direction.Down) node.Set("backGravity", spec.BackGravity.ToString());
                    if (spec.InitialSide != SurfaceSide.Front) node.Set("initialSide", spec.InitialSide.ToString());
                    node.SetIf("lockGroup", spec.LockGroup);
                    node.SetIf("moveCost", spec.MoveCost);
                    if (spec.MeterCost != 100) node.Set("meterCost", spec.MeterCost);
                    node.SetIf("maxFolds", spec.MaxFolds);
                    array.Add(node);
                }

                root.Set("folds", array);
            }

            root.Set("front", SurfaceToJson(level.Front));
            if (!level.Back.IsEmpty) root.Set("back", SurfaceToJson(level.Back));

            if (level.Portals.Count > 0)
            {
                var array = JsonValue.NewArray();
                for (int i = 0; i < level.Portals.Count; i++)
                {
                    var spec = level.Portals[i];
                    var node = JsonValue.NewObject();
                    node.Set("from", JsonValue.FromInts(new[] { spec.FromX, spec.FromY }));
                    node.Set("fromSide", spec.FromSide.ToString());
                    node.Set("to", JsonValue.FromInts(new[] { spec.ToX, spec.ToY }));
                    node.Set("toSide", spec.ToSide.ToString());
                    array.Add(node);
                }

                root.Set("portals", array);
            }

            var goals = JsonValue.NewArray();
            for (int i = 0; i < level.Goals.Count; i++)
            {
                var spec = level.Goals[i];
                var node = JsonValue.NewObject();
                node.Set("type", spec.Type);
                if (spec.Type == "collect") node.Set("color", spec.Color.ToString());
                if (spec.Type == "obstacle") node.Set("obstacle", spec.Obstacle.ToString());
                node.SetIf("count", spec.Count);
                if (spec.RegionId >= 0) node.Set("region", spec.RegionId);
                node.SetIf("walker", spec.WalkerId);
                goals.Add(node);
            }

            root.Set("goals", goals);

            if (level.Modifiers.Count > 0)
            {
                var array = JsonValue.NewArray();
                for (int i = 0; i < level.Modifiers.Count; i++)
                {
                    var spec = level.Modifiers[i];
                    var node = JsonValue.NewObject();
                    node.Set("type", spec.Type);
                    node.SetIf("period", spec.Period);
                    node.SetIf("count", spec.Count);
                    node.SetIf("startTurn", spec.StartTurn);
                    node.SetIf("amount", spec.Amount);

                    if (spec.Walkers.Count > 0)
                    {
                        var walkers = JsonValue.NewArray();
                        for (int w = 0; w < spec.Walkers.Count; w++)
                        {
                            var walker = spec.Walkers[w];
                            var wnode = JsonValue.NewObject();
                            wnode.Set("id", walker.Id);
                            wnode.Set("at", JsonValue.FromInts(new[] { walker.X, walker.Y }));
                            wnode.Set("target", JsonValue.FromInts(new[] { walker.TargetX, walker.TargetY }));
                            if (walker.Side != SurfaceSide.Front) wnode.Set("side", walker.Side.ToString());
                            if (walker.StepsPerTurn != 2) wnode.Set("steps", walker.StepsPerTurn);
                            wnode.SetIf("character", walker.CharacterId);
                            walkers.Add(wnode);
                        }

                        node.Set("walkers", walkers);
                    }

                    if (spec.Phases.Count > 0)
                    {
                        var phases = JsonValue.NewArray();
                        for (int p = 0; p < spec.Phases.Count; p++) phases.Add(JsonValue.FromStrings(spec.Phases[p]));
                        node.Set("phases", phases);
                    }

                    array.Add(node);
                }

                root.Set("modifiers", array);
            }

            return root;
        }

        private static JsonValue SurfaceToJson(SurfaceSpec surface)
        {
            var node = JsonValue.NewObject();
            node.Set("tiles", JsonValue.FromStrings(surface.Tiles));
            if (HasContent(surface.Obstacles)) node.Set("obstacles", JsonValue.FromStrings(surface.Obstacles));
            if (HasContent(surface.Groups)) node.Set("groups", JsonValue.FromStrings(surface.Groups));
            if (HasContent(surface.Illustration)) node.Set("illustration", JsonValue.FromStrings(surface.Illustration));
            if (HasContent(surface.SpawnGroups)) node.Set("spawnGroups", JsonValue.FromStrings(surface.SpawnGroups));
            if (HasContent(surface.StoryLinks)) node.Set("storyLinks", JsonValue.FromStrings(surface.StoryLinks));
            return node;
        }

        private static bool HasContent(List<string> rows)
        {
            if (rows == null) return false;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (string.IsNullOrEmpty(row)) continue;
                for (int c = 0; c < row.Length; c++)
                {
                    if (row[c] != '.' && row[c] != ' ' && row[c] != '0') return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ read

        public static LevelDefinition FromJson(string json) => FromJsonValue(JsonValue.Parse(json));

        public static bool TryFromJson(string json, out LevelDefinition level, out string error)
        {
            level = null;
            if (!JsonValue.TryParse(json, out var root, out error)) return false;

            try
            {
                level = FromJsonValue(root);
                return true;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public static LevelDefinition FromJsonValue(JsonValue root)
        {
            var level = new LevelDefinition
            {
                Id = root["id"].AsInt(),
                Name = root["name"].AsString("Untitled"),
                Book = root["book"].AsString("midnight-carnival"),
                ChapterIndex = root["chapter"].AsInt(),
                Width = root["width"].AsInt(8),
                Height = root["height"].AsInt(8),
                Moves = root["moves"].AsInt(25),
                FoldMeterMax = root["foldMeterMax"].AsInt(),
                DesignWinRate = root["designWinRate"].AsFloat(),
                DesignWinRateTolerance = root["designWinRateTolerance"].AsFloat(0.12f),
                DefaultGravity = root["gravity"].AsEnum(Direction.Down),
                DioramaPieceId = root["dioramaPiece"].AsString(),
                TutorialKey = root["tutorial"].AsString()
            };

            level.IntroDialogue.AddRange(root["intro"].AsStringList());
            level.OutroDialogue.AddRange(root["outro"].AsStringList());

            var tables = root["spawnTables"];
            for (int i = 0; i < tables.Count; i++)
            {
                var node = tables[i];
                var spec = new SpawnTableSpec { Group = node["group"].AsInt() };
                var colors = node["colors"];
                for (int c = 0; c < colors.Count; c++) spec.Colors.Add(colors[c].AsEnum(TileColor.None));
                spec.Weights.AddRange(node["weights"].AsIntList());
                level.SpawnTables.Add(spec);
            }

            var folds = root["folds"];
            for (int i = 0; i < folds.Count; i++)
            {
                var node = folds[i];
                var rect = node["rect"].AsIntList();
                var spec = new FoldRegionSpec
                {
                    Id = node["id"].AsInt(),
                    Name = node["name"].AsString(),
                    X = rect.Count > 0 ? rect[0] : 0,
                    Y = rect.Count > 1 ? rect[1] : 0,
                    Width = rect.Count > 2 ? rect[2] : 1,
                    Height = rect.Count > 3 ? rect[3] : 1,
                    FlipAxis = node["flipAxis"].AsEnum(Axis.Vertical),
                    FrontGravity = node["frontGravity"].AsEnum(Direction.Down),
                    BackGravity = node["backGravity"].AsEnum(Direction.Down),
                    InitialSide = node["initialSide"].AsEnum(SurfaceSide.Front),
                    LockGroup = node["lockGroup"].AsInt(),
                    MoveCost = node["moveCost"].AsInt(),
                    MeterCost = node["meterCost"].AsInt(100),
                    MaxFolds = node["maxFolds"].AsInt()
                };

                level.FoldRegions.Add(spec);
            }

            level.Front = SurfaceFromJson(root["front"]);
            level.Back = SurfaceFromJson(root["back"]);

            var portals = root["portals"];
            for (int i = 0; i < portals.Count; i++)
            {
                var node = portals[i];
                var from = node["from"].AsIntList();
                var to = node["to"].AsIntList();
                level.Portals.Add(new PortalSpec
                {
                    FromX = from.Count > 0 ? from[0] : 0,
                    FromY = from.Count > 1 ? from[1] : 0,
                    FromSide = node["fromSide"].AsEnum(SurfaceSide.Front),
                    ToX = to.Count > 0 ? to[0] : 0,
                    ToY = to.Count > 1 ? to[1] : 0,
                    ToSide = node["toSide"].AsEnum(SurfaceSide.Back)
                });
            }

            var goals = root["goals"];
            for (int i = 0; i < goals.Count; i++)
            {
                var node = goals[i];
                level.Goals.Add(new GoalSpec
                {
                    Type = node["type"].AsString("collect"),
                    Color = node["color"].AsEnum(TileColor.Crimson),
                    Obstacle = node["obstacle"].AsEnum(ObstacleType.TornPage),
                    Count = node["count"].AsInt(),
                    RegionId = node.Has("region") ? node["region"].AsInt() : -1,
                    WalkerId = node["walker"].AsInt()
                });
            }

            var modifiers = root["modifiers"];
            for (int i = 0; i < modifiers.Count; i++)
            {
                var node = modifiers[i];
                var spec = new ModifierSpec
                {
                    Type = node["type"].AsString("vanishing-ink"),
                    Period = node["period"].AsInt(3),
                    Count = node["count"].AsInt(1),
                    StartTurn = node["startTurn"].AsInt(1),
                    Amount = node["amount"].AsInt()
                };

                var walkers = node["walkers"];
                for (int w = 0; w < walkers.Count; w++)
                {
                    var wnode = walkers[w];
                    var at = wnode["at"].AsIntList();
                    var target = wnode["target"].AsIntList();
                    spec.Walkers.Add(new WalkerSpec
                    {
                        Id = wnode["id"].AsInt(),
                        X = at.Count > 0 ? at[0] : 0,
                        Y = at.Count > 1 ? at[1] : 0,
                        TargetX = target.Count > 0 ? target[0] : 0,
                        TargetY = target.Count > 1 ? target[1] : 0,
                        Side = wnode["side"].AsEnum(SurfaceSide.Front),
                        StepsPerTurn = wnode["steps"].AsInt(2),
                        CharacterId = wnode["character"].AsString("quill")
                    });
                }

                var phases = node["phases"];
                for (int p = 0; p < phases.Count; p++) spec.Phases.Add(phases[p].AsStringList());

                level.Modifiers.Add(spec);
            }

            return level;
        }

        private static SurfaceSpec SurfaceFromJson(JsonValue node)
        {
            var surface = new SurfaceSpec();
            if (node == null || node.IsNull) return surface;

            surface.Tiles = node["tiles"].AsStringList();
            surface.Obstacles = node["obstacles"].AsStringList();
            surface.Groups = node["groups"].AsStringList();
            surface.Illustration = node["illustration"].AsStringList();
            surface.SpawnGroups = node["spawnGroups"].AsStringList();
            surface.StoryLinks = node["storyLinks"].AsStringList();
            return surface;
        }
    }
}
