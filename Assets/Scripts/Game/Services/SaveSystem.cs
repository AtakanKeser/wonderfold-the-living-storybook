using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Wonderfold.Core.Live;
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

        public int ActiveLevelId;
        public int ActiveSeed;
        public readonly List<Wonderfold.Core.Board.PlayerMove> ActiveMoves = new List<Wonderfold.Core.Board.PlayerMove>();
        public bool HasActiveSession => ActiveLevelId > 0;

        // ---- the live archive -------------------------------------------------------------------
        // None of this belongs in the core: the core knows how to weave and measure a page, and the
        // profile is what remembers which ones this player has met.

        /// <summary>Consecutive days played, and the one mend that can bridge a missed day.</summary>
        public readonly StreakLedger Streak = new StreakLedger();

        /// <summary>Best Story Ink per Daily Fold, keyed by day index. Drives "beat your own page".</summary>
        public readonly Dictionary<int, int> DailyBestInk = new Dictionary<int, int>();

        /// <summary>The winning thread for each daily, so the player can re-share or re-watch it.</summary>
        public readonly Dictionary<int, string> DailyThreads = new Dictionary<int, string>();

        /// <summary>Next page of the Endless Archive to attempt, and the deepest ever reached.</summary>
        public int EndlessDepth = 1;
        public int EndlessBestDepth;

        public readonly QuestTracker Quests = new QuestTracker();

        /// <summary>Which day's and week's errand lists the tracker currently holds.</summary>
        public int QuestDayIndex = int.MinValue;
        public int QuestWeekIndex = int.MinValue;

        /// <summary>Refilling hearts is the second thing coins are for, after mending a streak.</summary>
        public const int LifeRefillCost = 150;
        public const int HammerCost = 120;
        public const int RocketCost = 160;

        public bool IsUnlocked(int levelId) => levelId <= HighestLevelUnlocked;

        public bool TrySpendCoins(int amount)
        {
            if (amount <= 0 || Coins < amount) return false;
            Coins -= amount;
            return true;
        }

        public void AddCoins(int amount)
        {
            if (amount <= 0) return;
            Coins += amount;
        }

        public bool TryBuyLives()
        {
            if (Lives >= MaxLives || !TrySpendCoins(LifeRefillCost)) return false;
            Lives = MaxLives;
            LivesRefilledAtTicks = 0;
            return true;
        }

        public bool TryBuyTool(Wonderfold.Core.Board.PageTool tool)
        {
            int cost = tool == Wonderfold.Core.Board.PageTool.Hammer ? HammerCost : RocketCost;
            if (!TrySpendCoins(cost)) return false;
            if (tool == Wonderfold.Core.Board.PageTool.Hammer) Hammers++;
            else RibbonRockets++;
            return true;
        }

        public int DailyBest(int dayIndex)
        {
            DailyBestInk.TryGetValue(dayIndex, out int ink);
            return ink;
        }

        /// <summary>Keeps only the better run of a day, and the thread that produced it.</summary>
        public bool RecordDailyResult(int dayIndex, int storyInk, string threadCode)
        {
            int best = DailyBest(dayIndex);
            if (storyInk <= best) return false;

            DailyBestInk[dayIndex] = storyInk;
            if (!string.IsNullOrEmpty(threadCode)) DailyThreads[dayIndex] = threadCode;
            return true;
        }

        public void RecordEndlessWin(int depth)
        {
            if (depth > EndlessBestDepth) EndlessBestDepth = depth;
            if (depth >= EndlessDepth) EndlessDepth = depth + 1;
        }

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
        private const int Version = 3;

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

            root.Set("activeLevel", profile.ActiveLevelId);
            root.Set("activeSeed", profile.ActiveSeed);
            var activeMoves = JsonValue.NewArray();
            for (int i = 0; i < profile.ActiveMoves.Count; i++)
            {
                var move = profile.ActiveMoves[i];
                var m = JsonValue.NewObject();
                m.Set("k", (int)move.Kind);
                m.Set("ax", move.A.X); m.Set("ay", move.A.Y);
                m.Set("bx", move.B.X); m.Set("by", move.B.Y);
                m.Set("r", move.RegionId);
                m.Set("t", (int)move.Tool);
                activeMoves.Add(m);
            }
            root.Set("activeMoves", activeMoves);

            var live = JsonValue.NewObject();
            live.Set("streak", profile.Streak.Current);
            live.Set("bestStreak", profile.Streak.Best);
            live.Set("streakDay", profile.Streak.LastDayIndex);
            live.Set("mendsUsed", profile.Streak.MendsUsed);
            live.Set("endlessDepth", profile.EndlessDepth);
            live.Set("endlessBest", profile.EndlessBestDepth);
            live.Set("questDay", profile.QuestDayIndex);
            live.Set("questWeek", profile.QuestWeekIndex);

            var dailyInk = JsonValue.NewObject();
            foreach (var pair in profile.DailyBestInk) dailyInk.Set(pair.Key.ToString(), pair.Value);
            live.Set("dailyInk", dailyInk);

            var threads = JsonValue.NewObject();
            foreach (var pair in profile.DailyThreads) threads.Set(pair.Key.ToString(), pair.Value);
            live.Set("dailyThreads", threads);

            var questProgress = JsonValue.NewObject();
            foreach (var pair in profile.Quests.Progress) questProgress.Set(pair.Key, pair.Value);
            live.Set("questProgress", questProgress);

            var claimed = JsonValue.NewArray();
            foreach (var id in profile.Quests.Claimed) claimed.Add(JsonValue.Create(id));
            live.Set("questClaimed", claimed);

            root.Set("live", live);

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

            profile.ActiveLevelId = root["activeLevel"].AsInt(0);
            profile.ActiveSeed = root["activeSeed"].AsInt(0);
            var activeMoves = root["activeMoves"];
            if (activeMoves != null)
            {
                for (int i = 0; i < activeMoves.Count; i++)
                {
                    var m = activeMoves[i];
                    var kind = (Wonderfold.Core.Board.MoveKind)m["k"].AsInt(0);
                    var a = new Wonderfold.Core.Primitives.GridCoord(m["ax"].AsInt(0), m["ay"].AsInt(0));
                    var b = new Wonderfold.Core.Primitives.GridCoord(m["bx"].AsInt(0), m["by"].AsInt(0));
                    int r = m["r"].AsInt(0);
                    var t = (Wonderfold.Core.Board.PageTool)m["t"].AsInt(0);

                    if (kind == Wonderfold.Core.Board.MoveKind.Swap) profile.ActiveMoves.Add(Wonderfold.Core.Board.PlayerMove.Swap(a, b));
                    else if (kind == Wonderfold.Core.Board.MoveKind.ActivateBooster) profile.ActiveMoves.Add(Wonderfold.Core.Board.PlayerMove.ActivateBooster(a));
                    else if (kind == Wonderfold.Core.Board.MoveKind.Fold) profile.ActiveMoves.Add(Wonderfold.Core.Board.PlayerMove.Fold(r));
                    else if (kind == Wonderfold.Core.Board.MoveKind.UseTool) profile.ActiveMoves.Add(Wonderfold.Core.Board.PlayerMove.UseTool(t, a));
                }
            }

            // A version 2 save has no "live" block; every field below simply keeps its default, so an
            // existing player picks up the archive with a clean slate rather than a wiped profile.
            var live = root["live"];
            if (live != null && !live.IsNull)
            {
                profile.Streak.Current = Mathf.Max(0, live["streak"].AsInt(0));
                profile.Streak.Best = Mathf.Max(0, live["bestStreak"].AsInt(0));
                profile.Streak.LastDayIndex = live["streakDay"].AsInt(StreakLedger.NeverPlayed);
                profile.Streak.MendsUsed = Mathf.Max(0, live["mendsUsed"].AsInt(0));
                profile.EndlessDepth = Mathf.Max(1, live["endlessDepth"].AsInt(1));
                profile.EndlessBestDepth = Mathf.Max(0, live["endlessBest"].AsInt(0));
                profile.QuestDayIndex = live["questDay"].AsInt(int.MinValue);
                profile.QuestWeekIndex = live["questWeek"].AsInt(int.MinValue);

                var dailyInk = live["dailyInk"];
                for (int i = 0; i < dailyInk.Keys.Count; i++)
                {
                    var key = dailyInk.Keys[i];
                    if (int.TryParse(key, out int day)) profile.DailyBestInk[day] = dailyInk[key].AsInt();
                }

                var threads = live["dailyThreads"];
                for (int i = 0; i < threads.Keys.Count; i++)
                {
                    var key = threads.Keys[i];
                    if (int.TryParse(key, out int day)) profile.DailyThreads[day] = threads[key].AsString(string.Empty);
                }

                var questProgress = live["questProgress"];
                for (int i = 0; i < questProgress.Keys.Count; i++)
                {
                    var key = questProgress.Keys[i];
                    profile.Quests.Progress[key] = questProgress[key].AsInt();
                }

                var claimed = live["questClaimed"];
                for (int i = 0; i < claimed.Count; i++)
                {
                    var id = claimed[i].AsString();
                    if (!string.IsNullOrEmpty(id)) profile.Quests.Claimed.Add(id);
                }
            }

            return profile;
        }
    }
}
