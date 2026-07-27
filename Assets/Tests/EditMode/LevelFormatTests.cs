using System.Collections.Generic;
using NUnit.Framework;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Serialization;

namespace Wonderfold.Tests
{
    [TestFixture]
    public class JsonTests
    {
        [Test]
        public void RoundTripsScalarsArraysAndObjects()
        {
            const string source = "{\"a\":1,\"b\":[1,2,3],\"c\":{\"d\":\"x\"},\"e\":true,\"f\":null,\"g\":-2.5}";

            var value = JsonValue.Parse(source);

            Assert.That(value["a"].AsInt(), Is.EqualTo(1));
            Assert.That(value["b"].Count, Is.EqualTo(3));
            Assert.That(value["b"][2].AsInt(), Is.EqualTo(3));
            Assert.That(value["c"]["d"].AsString(), Is.EqualTo("x"));
            Assert.That(value["e"].AsBool(), Is.True);
            Assert.That(value["f"].IsNull, Is.True);
            Assert.That(value["g"].AsDouble(), Is.EqualTo(-2.5).Within(1e-9));
        }

        [Test]
        public void ReparsingWhatItWroteGivesTheSameDocument()
        {
            var original = JsonValue.Parse("{\"x\":[1,2],\"y\":{\"z\":\"hi\\n\\\"there\\\"\"}}");

            var reparsed = JsonValue.Parse(original.ToJson());

            Assert.That(reparsed.ToJson(false), Is.EqualTo(original.ToJson(false)));
        }

        [Test]
        public void MissingKeysReturnTheFallbackRatherThanThrowing()
        {
            var value = JsonValue.Parse("{}");

            Assert.That(value["nope"].AsInt(42), Is.EqualTo(42));
            Assert.That(value["nope"]["deeper"].AsString("fallback"), Is.EqualTo("fallback"));
            Assert.That(value["nope"][3].IsNull, Is.True);
        }

        [Test]
        public void MalformedDocumentsFailLoudlyButSafely()
        {
            Assert.That(JsonValue.TryParse("{\"a\":}", out _, out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void LineCommentsAreToleratedSoLevelsCanBeAnnotated()
        {
            var value = JsonValue.Parse("{\n // the fold meter is deliberately cheap here\n \"a\": 3\n}");

            Assert.That(value["a"].AsInt(), Is.EqualTo(3));
        }
    }

    [TestFixture]
    public class LevelSerializerTests
    {
        private static LevelDefinition Sample()
        {
            var level = new LevelDefinition
            {
                Id = 42,
                Name = "Sample",
                Book = "midnight-carnival",
                Width = 4,
                Height = 3,
                Moves = 17,
                DesignWinRate = 0.55f,
                DioramaPieceId = "test-piece",
                DefaultGravity = Direction.Down
            };

            level.SpawnTables.Add(new SpawnTableSpec
            {
                Colors = { TileColor.Crimson, TileColor.Azure },
                Weights = { 3, 1 }
            });

            level.FoldRegions.Add(new FoldRegionSpec
            {
                Id = 1, Name = "Leaf", X = 2, Y = 0, Width = 2, Height = 3,
                BackGravity = Direction.Right, MeterCost = 40, MoveCost = 1
            });

            level.Front.Tiles = new List<string> { "....", "....", "...." };
            level.Front.Obstacles = new List<string> { "....", ".T..", "...." };
            level.Back.Tiles = new List<string> { "....", "....", "...." };
            level.Goals.Add(new GoalSpec { Type = "collect", Color = TileColor.Azure, Count = 20 });
            level.Modifiers.Add(new ModifierSpec { Type = "blank-tide", Period = 2, Count = 3 });
            level.IntroDialogue.Add("MIRA: Hello.");
            return level;
        }

        [Test]
        public void RoundTripPreservesEverythingThatMatters()
        {
            var original = Sample();

            var restored = LevelSerializer.FromJson(LevelSerializer.ToJson(original));

            Assert.That(restored.Id, Is.EqualTo(original.Id));
            Assert.That(restored.Name, Is.EqualTo(original.Name));
            Assert.That(restored.Moves, Is.EqualTo(original.Moves));
            Assert.That(restored.DesignWinRate, Is.EqualTo(original.DesignWinRate).Within(1e-4));
            Assert.That(restored.DioramaPieceId, Is.EqualTo(original.DioramaPieceId));
            Assert.That(restored.FoldRegions[0].BackGravity, Is.EqualTo(Direction.Right));
            Assert.That(restored.FoldRegions[0].MeterCost, Is.EqualTo(40));
            Assert.That(restored.SpawnTables[0].Weights, Is.EqualTo(original.SpawnTables[0].Weights));
            Assert.That(restored.Front.Obstacles, Is.EqualTo(original.Front.Obstacles));
            Assert.That(restored.Goals[0].Count, Is.EqualTo(20));
            Assert.That(restored.Modifiers[0].Count, Is.EqualTo(3));
            Assert.That(restored.IntroDialogue[0], Is.EqualTo("MIRA: Hello."));
        }

        [Test]
        public void SerialisingTwiceIsStable()
        {
            var once = LevelSerializer.ToJson(Sample());
            var twice = LevelSerializer.ToJson(LevelSerializer.FromJson(once));

            Assert.That(twice, Is.EqualTo(once), "an editor round trip must not churn the file");
        }

        [Test]
        public void RowsAreAuthoredTopFirstAndBuildBottomUp()
        {
            var level = Sample();
            level.Front.Tiles = new List<string> { "rrrr", "bbbb", "gggg" };

            var board = LevelBuilder.Build(level, 1);

            Assert.That(board.CellAt(new GridCoord(0, 2), SurfaceSide.Front).Tile.Color,
                Is.EqualTo(TileColor.Crimson), "the first row written is the top row of the board");
            Assert.That(board.CellAt(new GridCoord(0, 0), SurfaceSide.Front).Tile.Color,
                Is.EqualTo(TileColor.Meadow));
        }
    }

    [TestFixture]
    public class LevelValidatorTests
    {
        private static LevelDefinition Minimal()
        {
            var level = new LevelDefinition { Id = 1, Width = 4, Height = 4, Moves = 10 };
            level.SpawnTables.Add(new SpawnTableSpec { Colors = { TileColor.Crimson, TileColor.Azure } });
            level.Front.Tiles = new List<string> { "....", "....", "....", "...." };
            level.Goals.Add(new GoalSpec { Type = "collect", Color = TileColor.Crimson, Count = 10 });
            return level;
        }

        [Test]
        public void AWellFormedLevelPasses()
        {
            Assert.That(LevelValidator.Validate(Minimal()), Is.Empty);
        }

        [Test]
        public void CatchesAGoalAskingForAColourTheLevelNeverSpawns()
        {
            var level = Minimal();
            level.Goals[0].Color = TileColor.Blush;

            var issues = LevelValidator.Validate(level);

            Assert.That(LevelValidator.HasErrors(issues), Is.True);
        }

        [Test]
        public void CatchesAFoldRegionHangingOffThePage()
        {
            var level = Minimal();
            level.FoldRegions.Add(new FoldRegionSpec { Id = 1, X = 3, Y = 0, Width = 3, Height = 4 });

            Assert.That(LevelValidator.HasErrors(LevelValidator.Validate(level)), Is.True);
        }

        [Test]
        public void CatchesAFoldLockedToAGroupNothingUses()
        {
            var level = Minimal();
            level.FoldRegions.Add(new FoldRegionSpec { Id = 1, X = 2, Y = 0, Width = 2, Height = 4, LockGroup = 4 });

            Assert.That(LevelValidator.HasErrors(LevelValidator.Validate(level)), Is.True);
        }

        [Test]
        public void CatchesGravityPointingBackAtItself()
        {
            var level = Minimal();
            level.DefaultGravity = Direction.Down;
            level.FoldRegions.Add(new FoldRegionSpec
            {
                Id = 1, X = 0, Y = 0, Width = 4, Height = 2,
                FrontGravity = Direction.Up, BackGravity = Direction.Up
            });

            Assert.That(LevelValidator.HasErrors(LevelValidator.Validate(level)), Is.True);
        }

        [Test]
        public void CatchesAWalkerWalledOffFromItsTarget()
        {
            var level = Minimal();
            level.Front.Tiles = new List<string> { "..#.", "..#.", "..#.", "..#." };
            level.Modifiers.Add(new ModifierSpec
            {
                Type = "walker",
                Walkers = { new WalkerSpec { Id = 1, X = 0, Y = 0, TargetX = 3, TargetY = 3 } }
            });
            level.Goals.Add(new GoalSpec { Type = "walker", WalkerId = 1 });

            Assert.That(LevelValidator.HasErrors(LevelValidator.Validate(level)), Is.True);
        }

        [Test]
        public void WarnsAboutAWallSittingOnTheCellTilesEnterThrough()
        {
            var level = Minimal();
            level.Front.Obstacles = new List<string> { "WWWW", "....", "....", "...." };

            var issues = LevelValidator.Validate(level);

            Assert.That(LevelValidator.HasErrors(issues), Is.True,
                "walling off every spawner makes the board unplayable, not merely awkward");
        }
    }
}
