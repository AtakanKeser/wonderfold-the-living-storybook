using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Wonderfold.Core.Serialization;

namespace Wonderfold.Game.Services
{
    /// <summary>What the player has done so far. Everything the meta layer needs and nothing else.</summary>
    public sealed class PlayerProfile
    {
        public const int MaxLives = 5;
        public const long LifeIntervalTicks = System.TimeSpan.TicksPerMinute * 30;

        public int HighestLevelUnlocked = 1;
        public int Coins = 100;
        public int Lives = MaxLives;
        public long LivesRefilledAtTicks;
        public int Hammers = 3;
        public int RibbonRockets = 2;

        /// <summary>Diorama pieces already restored, by id. Drives what the pop-up scene shows.</summary>
        public readonly HashSet<string> RestoredPieces = new HashSet<string>();

        /// <summary>Best moves-remaining per level, used for the three-star grade.</summary>
        public readonly Dictionary<int, int> BestMovesRemaining = new Dictionary<int, int>();

        /// <summary>Choices the player made at branch points, e.g. observatory vs firefly garden.</summary>
        public readonly Dictionary<string, string> StoryChoices = new Dictionary<string, string>();

        public bool IsUnlocked(int levelId) => levelId <= HighestLevelUnlocked;

        /// <summary>Applies elapsed real time in whole 30-minute life intervals.</summary>
        public bool RefreshLives(long utcNowTicks)
        {
            if (Lives >= MaxLives)
            {
                Lives = MaxLives;
                LivesRefilledAtTicks = 0;
                return false;
            }

            if (LivesRefilledAtTicks <= 0)
            {
                LivesRefilledAtTicks = utcNowTicks;
                return false;
            }

            long elapsed = utcNowTicks - LivesRefilledAtTicks;
            if (elapsed < LifeIntervalTicks) return false;

            int gained = (int)(elapsed / LifeIntervalTicks);
            Lives = System.Math.Min(MaxLives, Lives + gained);
            LivesRefilledAtTicks += gained * LifeIntervalTicks;
            if (Lives == MaxLives) LivesRefilledAtTicks = 0;
            return true;
        }

        public bool TryConsumeLife(long utcNowTicks)
        {
            RefreshLives(utcNowTicks);
            if (Lives <= 0) return false;
            if (Lives == MaxLives) LivesRefilledAtTicks = utcNowTicks;
            Lives--;
            return true;
        }

        public bool TrySpendTool(Wonderfold.Core.Board.PageTool tool)
        {
            switch (tool)
            {
                case Wonderfold.Core.Board.PageTool.Hammer:
                    if (Hammers <= 0) return false;
                    Hammers--;
                    return true;
                case Wonderfold.Core.Board.PageTool.RibbonRocket:
                    if (RibbonRockets <= 0) return false;
                    RibbonRockets--;
                    return true;
                default:
                    return false;
            }
        }

        public void RefundTool(Wonderfold.Core.Board.PageTool tool)
        {
            if (tool == Wonderfold.Core.Board.PageTool.Hammer) Hammers++;
            else if (tool == Wonderfold.Core.Board.PageTool.RibbonRocket) RibbonRockets++;
        }

        public long TicksToNextLife(long utcNowTicks)
        {
            if (Lives >= MaxLives) return 0;
            if (LivesRefilledAtTicks <= 0) return LifeIntervalTicks;
            return System.Math.Max(0, LifeIntervalTicks - (utcNowTicks - LivesRefilledAtTicks));
        }

        public void RecordWin(int levelId, int movesRemaining, string dioramaPieceId)
        {
            if (levelId >= HighestLevelUnlocked) HighestLevelUnlocked = levelId + 1;

            BestMovesRemaining.TryGetValue(levelId, out int best);
            if (movesRemaining > best) BestMovesRemaining[levelId] = movesRemaining;

            if (!string.IsNullOrEmpty(dioramaPieceId)) RestoredPieces.Add(dioramaPieceId);
        }
    }

    /// <summary>
    /// Reads and writes the profile as JSON in <see cref="Application.persistentDataPath"/>.
    ///
    /// <para>Saves are written to a temporary file and then moved into place, so a crash mid-write leaves
    /// the previous save intact rather than a half-written one. A corrupt save is reported and replaced
    /// with a fresh profile instead of taking the app down on launch.</para>
    /// </summary>
    public sealed class SaveSystem
    {
        private const string FileName = "wonderfold-profile.json";
        private const int Version = 2;

        private string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public PlayerProfile Load()
        {
            try
            {
                if (!File.Exists(Path)) return new PlayerProfile();
                return Deserialise(File.ReadAllText(Path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Wonderfold] Could not read the save ({e.Message}); starting a new profile.");
                return new PlayerProfile();
            }
        }

        public void Save(PlayerProfile profile)
        {
            try
            {
                var temporary = Path + ".tmp";
                File.WriteAllText(temporary, Serialise(profile));
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(temporary, Path);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Wonderfold] Could not write the save: {e.Message}");
            }
        }

        public void Delete()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }

        private static string Serialise(PlayerProfile profile)
        {
            var root = JsonValue.NewObject();
            root.Set("version", Version);
            root.Set("highestLevel", profile.HighestLevelUnlocked);
            root.Set("coins", profile.Coins);
            root.Set("lives", profile.Lives);
            root.Set("livesRefilledAt", profile.LivesRefilledAtTicks);
            root.Set("hammers", profile.Hammers);
            root.Set("ribbonRockets", profile.RibbonRockets);

            var pieces = JsonValue.NewArray();
            foreach (var piece in profile.RestoredPieces) pieces.Add(JsonValue.Create(piece));
            root.Set("restoredPieces", pieces);

            var best = JsonValue.NewObject();
            foreach (var pair in profile.BestMovesRemaining) best.Set(pair.Key.ToString(), pair.Value);
            root.Set("bestMoves", best);

            var choices = JsonValue.NewObject();
            foreach (var pair in profile.StoryChoices) choices.Set(pair.Key, pair.Value);
            root.Set("storyChoices", choices);

            return root.ToJson();
        }

        private static PlayerProfile Deserialise(string json)
        {
            var root = JsonValue.Parse(json);
            var profile = new PlayerProfile
            {
                HighestLevelUnlocked = root["highestLevel"].AsInt(1),
                Coins = root["coins"].AsInt(100),
                Lives = Mathf.Clamp(root["lives"].AsInt(PlayerProfile.MaxLives), 0, PlayerProfile.MaxLives),
                LivesRefilledAtTicks = (long)root["livesRefilledAt"].AsDouble(),
                Hammers = Mathf.Max(0, root["hammers"].AsInt(3)),
                RibbonRockets = Mathf.Max(0, root["ribbonRockets"].AsInt(2))
            };

            var pieces = root["restoredPieces"];
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i].AsString();
                if (piece != null) profile.RestoredPieces.Add(piece);
            }

            var best = root["bestMoves"];
            for (int i = 0; i < best.Keys.Count; i++)
            {
                var key = best.Keys[i];
                if (int.TryParse(key, out int levelId)) profile.BestMovesRemaining[levelId] = best[key].AsInt();
            }

            var choices = root["storyChoices"];
            for (int i = 0; i < choices.Keys.Count; i++)
            {
                var key = choices.Keys[i];
                profile.StoryChoices[key] = choices[key].AsString(string.Empty);
            }

            return profile;
        }
    }
}
