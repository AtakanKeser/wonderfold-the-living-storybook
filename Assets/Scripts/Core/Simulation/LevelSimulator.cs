using System;
using System.Collections.Generic;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;

namespace Wonderfold.Core.Simulation
{
    /// <summary>
    /// Plays a level thousands of times with bots and reports what happened.
    ///
    /// <para>This is the part of the project that makes level design a measurement rather than a guess.
    /// Every run is seeded, so a suspicious result can be replayed exactly; the report is the same object
    /// whether it was produced by the command line tool or by the Level Laboratory window inside Unity.
    /// </para>
    /// </summary>
    public static class LevelSimulator
    {
        /// <summary>One seeded playthrough.</summary>
        public static LevelRunStats Run(LevelDefinition definition, int seed, IAgent agent, GameRules rules = null,
            int maxTurns = 400)
        {
            var session = new LevelSession(definition, seed, rules);
            session.Events.Recording = false; // bots do not read the event stream
            session.Start();

            int guard = 0;
            while (!session.IsOver && guard++ < maxTurns)
            {
                var move = agent.ChooseMove(session);
                if (move == null) break;

                if (!session.TryExecute(move.Value, out _))
                {
                    // The agent proposed something illegal; drop it and try once more before giving up.
                    var fallback = agent.ChooseMove(session);
                    if (fallback == null || !session.TryExecute(fallback.Value, out _)) break;
                }
            }

            var stats = session.Stats;
            if (stats.Outcome == LevelOutcome.InProgress)
            {
                stats.Outcome = session.AllGoalsComplete() ? LevelOutcome.Won : LevelOutcome.LostBoardCollapsed;
                stats.MovesRemaining = session.MovesRemaining;
                stats.MovesUsed = definition.Moves - session.MovesRemaining;
            }

            stats.GoalCompletion = new List<GoalSnapshot>();
            for (int i = 0; i < session.Goals.Count; i++)
            {
                var goal = session.Goals[i];
                stats.GoalCompletion.Add(new GoalSnapshot(goal.Id, goal.Current, goal.Target));
            }

            return stats;
        }

        /// <summary>Aggregates many runs into the numbers a designer actually needs.</summary>
        public static SimulationReport Sweep(LevelDefinition definition, int runs, IAgentFactory agentFactory,
            GameRules rules = null, int baseSeed = 1)
        {
            var report = new SimulationReport(definition.Id, definition.Name, agentFactory.Name, runs);
            var movesRemaining = new List<int>();

            for (int i = 0; i < runs; i++)
            {
                int seed = baseSeed + i * 7919;
                var stats = Run(definition, seed, agentFactory.Create(seed), rules);
                report.Accumulate(stats);
                if (stats.Outcome == LevelOutcome.Won) movesRemaining.Add(stats.MovesRemaining);
            }

            report.Finish(movesRemaining);
            return report;
        }

        public static SimulationReport Sweep(LevelDefinition definition, int runs, string agentName,
            GameRules rules = null, int baseSeed = 1) =>
            Sweep(definition, runs, new NamedAgentFactory(agentName), rules, baseSeed);
    }

    public interface IAgentFactory
    {
        string Name { get; }
        IAgent Create(int seed);
    }

    public sealed class NamedAgentFactory : IAgentFactory
    {
        public NamedAgentFactory(string name)
        {
            Name = name ?? "heuristic";
        }

        public string Name { get; }
        public IAgent Create(int seed) => AgentFactory.Create(Name, seed);
    }

    public readonly struct GoalSnapshot
    {
        public readonly string Id;
        public readonly int Current;
        public readonly int Target;

        public GoalSnapshot(string id, int current, int target)
        {
            Id = id;
            Current = current;
            Target = target;
        }

        public bool IsComplete => Current >= Target;
        public float Completion => Target <= 0 ? 1f : (float)Current / Target;
    }

    /// <summary>Aggregate of one sweep. Everything here maps to a decision a designer has to make.</summary>
    public sealed class SimulationReport
    {
        public int LevelId { get; }
        public string LevelName { get; }
        public string AgentName { get; }
        public int Runs { get; }

        public int Wins;
        public int LostOutOfMoves;
        public int LostBoardCollapsed;

        public long TotalMovesUsed;
        public long TotalFolds;
        public long TotalBoostersCreated;
        public long TotalGoldenStitches;
        public long TotalSeamMatches;
        public long TotalBoosterActivations;
        public long TotalTilesCleared;

        public int MedianMovesRemainingOnWin;
        public int P10MovesRemainingOnWin;

        /// <summary>How often each goal was the one still unfinished when the level was lost.</summary>
        public readonly Dictionary<string, int> BlockingGoal = new Dictionary<string, int>();

        public SimulationReport(int levelId, string levelName, string agentName, int runs)
        {
            LevelId = levelId;
            LevelName = levelName;
            AgentName = agentName;
            Runs = runs;
        }

        public float WinRate => Runs <= 0 ? 0f : (float)Wins / Runs;
        public float AverageMovesUsed => Runs <= 0 ? 0f : (float)TotalMovesUsed / Runs;
        public float AverageFolds => Runs <= 0 ? 0f : (float)TotalFolds / Runs;
        public float AverageBoosters => Runs <= 0 ? 0f : (float)TotalBoostersCreated / Runs;
        public float AverageGoldenStitches => Runs <= 0 ? 0f : (float)TotalGoldenStitches / Runs;
        public float AverageSeamMatches => Runs <= 0 ? 0f : (float)TotalSeamMatches / Runs;
        public float DeadBoardRate => Runs <= 0 ? 0f : (float)LostBoardCollapsed / Runs;

        public void Accumulate(LevelRunStats stats)
        {
            switch (stats.Outcome)
            {
                case LevelOutcome.Won: Wins++; break;
                case LevelOutcome.LostOutOfMoves: LostOutOfMoves++; break;
                default: LostBoardCollapsed++; break;
            }

            TotalMovesUsed += stats.MovesUsed;
            TotalFolds += stats.Folds;
            TotalBoostersCreated += stats.BoostersCreated;
            TotalGoldenStitches += stats.GoldenStitches;
            TotalSeamMatches += stats.SeamMatches;
            TotalBoosterActivations += stats.BoosterActivations;
            TotalTilesCleared += stats.TilesCleared;

            if (stats.Outcome == LevelOutcome.Won || stats.GoalCompletion == null) return;

            // Credit the goal that was furthest from done — that is what actually beat the player.
            string worst = null;
            float worstCompletion = 2f;
            for (int i = 0; i < stats.GoalCompletion.Count; i++)
            {
                var snapshot = stats.GoalCompletion[i];
                if (snapshot.IsComplete) continue;
                if (snapshot.Completion >= worstCompletion) continue;
                worstCompletion = snapshot.Completion;
                worst = snapshot.Id;
            }

            if (worst == null) return;
            BlockingGoal.TryGetValue(worst, out int count);
            BlockingGoal[worst] = count + 1;
        }

        public void Finish(List<int> movesRemainingOnWin)
        {
            if (movesRemainingOnWin == null || movesRemainingOnWin.Count == 0) return;
            movesRemainingOnWin.Sort();
            MedianMovesRemainingOnWin = movesRemainingOnWin[movesRemainingOnWin.Count / 2];
            P10MovesRemainingOnWin = movesRemainingOnWin[Math.Max(0, movesRemainingOnWin.Count / 10)];
        }

        /// <summary>The goal that most often stopped the player, or null when the level is comfortable.</summary>
        public string MostCommonFailureGoal()
        {
            string worst = null;
            int best = 0;
            foreach (var pair in BlockingGoal)
            {
                if (pair.Value <= best) continue;
                best = pair.Value;
                worst = pair.Key;
            }

            return worst;
        }
    }
}
