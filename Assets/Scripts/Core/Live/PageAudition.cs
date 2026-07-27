using System;
using System.Collections.Generic;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;
using Wonderfold.Core.Simulation;

namespace Wonderfold.Core.Live
{
    /// <summary>How hard to interrogate a candidate page before it is allowed in front of a player.</summary>
    public sealed class AuditionSettings
    {
        /// <summary>Bot playthroughs per measurement. 24 is enough to place a page inside a ±10% band.</summary>
        public int Runs = 24;

        /// <summary>Distinct pages to invent before settling for the closest one measured.</summary>
        public int MaxCandidates = 4;

        /// <summary>Move-budget adjustments allowed per candidate before it is abandoned.</summary>
        public int MaxRetunes = 5;

        /// <summary>How far from the requested win rate is still acceptable.</summary>
        public float Tolerance = 0.10f;

        /// <summary>Which bot measures. "heuristic" is the reference player for every balance number.</summary>
        public string Agent = "heuristic";

        /// <summary>
        /// The settings every <i>shared</i> page is auditioned with.
        ///
        /// <para>These numbers are part of the page's identity, not a performance knob. The audition tunes
        /// the move budget from what it measures, so a client that ran 200 playthroughs instead of 24
        /// would tune to a slightly different number and serve a subtly different page — which would
        /// quietly destroy the one property the Daily Fold and the Endless Archive exist for. Anything a
        /// player's score is compared against must use exactly this.</para>
        /// </summary>
        public static AuditionSettings Canonical => new AuditionSettings();

        /// <summary>
        /// Deeper, slower settings for exploring the generator offline. Never use these to serve a page
        /// a score will be compared against — see <see cref="Canonical"/>.
        /// </summary>
        public static AuditionSettings Thorough => new AuditionSettings
        {
            Runs = 120,
            MaxCandidates = 8,
            MaxRetunes = 6,
            Tolerance = 0.07f
        };

        public AuditionSettings Clone() => (AuditionSettings)MemberwiseClone();
    }

    /// <summary>The page that passed, and the measurement that let it pass.</summary>
    public sealed class AuditionResult
    {
        public LevelDefinition Level;
        public SimulationReport Report;

        /// <summary>How many distinct pages had to be invented before this one.</summary>
        public int Candidates;

        /// <summary>Total bot playthroughs spent reaching this answer.</summary>
        public int Playthroughs;

        /// <summary>True when the measured win rate landed inside the requested band.</summary>
        public bool InBand;

        public float WinRate => Report == null ? 0f : Report.WinRate;
        public float DeadBoardRate => Report == null ? 0f : Report.DeadBoardRate;
    }

    /// <summary>
    /// The gate every generated page goes through: invent it, validate it, then <i>play it</i> — with the
    /// same bots that balanced the authored chapter — and only serve it if the measured win rate is where
    /// the request asked for.
    ///
    /// <para>This is the part that makes infinite content safe. Procedural match-3 levels are easy to
    /// generate and notoriously easy to generate <i>badly</i>: a fold whose reverse side starves, a wax
    /// seal no booster can reach, a goal the move budget cannot pay for. The authored chapter found four
    /// bugs of exactly that shape during production, and it found them because the simulator played the
    /// levels, not because anyone read them. Running that same simulator at the moment the page is woven
    /// means an unfair page is discarded before the player ever sees it.</para>
    ///
    /// <para>The loop is: measure → adjust the move budget → measure again. Moves are the right dial
    /// because they move win rate smoothly and change nothing about how the board reads. When the
    /// adjustments cannot bring a page into band, the page itself is the problem, so it is thrown away
    /// and a new one is woven from the next seed.</para>
    /// </summary>
    public static class PageAudition
    {
        /// <summary>
        /// Runs the whole audition and blocks until it has an answer. Fine for tools and tests; the game
        /// uses <see cref="PageAuditionRunner"/> so the work can be spread over frames.
        /// </summary>
        public static AuditionResult Hold(WeaveRequest request, AuditionSettings settings = null,
            GameRules rules = null)
        {
            var runner = new PageAuditionRunner(request, settings, rules);
            while (runner.Advance()) { }
            return runner.Result;
        }
    }

    /// <summary>
    /// The audition, one bot playthrough at a time.
    ///
    /// <para>Auditioning a page costs a few hundred simulated games. That is nothing on a desktop and
    /// several seconds on a phone, so the work is exposed as steps rather than as a blocking call: the
    /// game pumps <see cref="Advance"/> inside a frame budget while the book animates itself writing the
    /// page, and the player sees a paced beat instead of a stalled app. Stepping changes nothing about
    /// the answer — the sequence of seeds is identical to running it straight through, which is what
    /// keeps a page the same page on every device.</para>
    /// </summary>
    public sealed class PageAuditionRunner
    {
        private enum Phase
        {
            NeedCandidate,
            Measuring,
            Finished
        }

        private readonly WeaveRequest _request;
        private readonly AuditionSettings _settings;
        private readonly GameRules _rules;
        private readonly AuditionResult _result = new AuditionResult();
        private readonly List<int> _movesRemainingOnWin = new List<int>();

        private Phase _phase = Phase.NeedCandidate;
        private LevelDefinition _candidate;
        private int _candidateIndex = -1;
        private int _candidateSeed;
        private int _retune;
        private int _run;
        private SimulationReport _report;
        private float _best = float.MaxValue;
        private bool _fallbackUsed;

        public PageAuditionRunner(WeaveRequest request, AuditionSettings settings = null, GameRules rules = null)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _settings = (settings ?? AuditionSettings.Canonical).Clone();
            if (_settings.Runs < 1) _settings.Runs = 1;
            if (_settings.MaxCandidates < 1) _settings.MaxCandidates = 1;
            _rules = rules;
        }

        public AuditionResult Result => _result;
        public bool IsDone => _phase == Phase.Finished;

        /// <summary>Rough 0..1 for a progress bar. Pages that pass early simply finish sooner.</summary>
        public float Progress
        {
            get
            {
                if (IsDone) return 1f;
                int budget = _settings.MaxCandidates * (_settings.MaxRetunes + 1) * _settings.Runs;
                if (budget <= 0) return 0f;
                float done = (float)_result.Playthroughs / budget;
                return done > 0.99f ? 0.99f : done;
            }
        }

        /// <summary>
        /// Does one unit of work — weaving and validating a candidate, or playing one bot game. Returns
        /// false once the audition has an answer.
        /// </summary>
        public bool Advance()
        {
            switch (_phase)
            {
                case Phase.Finished:
                    return false;

                case Phase.NeedCandidate:
                    StartNextCandidate();
                    return _phase != Phase.Finished;

                default:
                    MeasureOneRun();
                    return _phase != Phase.Finished;
            }
        }

        /// <summary>Convenience for callers that just want to burn a chunk of the budget this frame.</summary>
        public bool Advance(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                if (!Advance()) return false;
            }

            return true;
        }

        private void StartNextCandidate()
        {
            while (true)
            {
                _candidateIndex++;
                if (_candidateIndex >= _settings.MaxCandidates)
                {
                    FinishWithFallbackIfNeeded();
                    return;
                }

                _candidateSeed = Mix(_request.Seed, _candidateIndex);
                _candidate = PageWeaver.Weave(Derive(_candidateSeed, _request.Difficulty, _request.AllowFold));

                // A page that cannot even build is not worth measuring, and the check is far cheaper than
                // the sweep it saves.
                var issues = LevelValidator.Validate(_candidate);
                if (LevelValidator.HasErrors(issues)) continue;

                _result.Candidates = _candidateIndex + 1;
                _retune = 0;
                BeginMeasurement();
                return;
            }
        }

        private void BeginMeasurement()
        {
            // The same seed set across every retune of a candidate: only the move budget changes between
            // measurements, so the tuner reads a real difference rather than bot luck.
            _report = new SimulationReport(_candidate.Id, _candidate.Name, _settings.Agent, _settings.Runs);
            _movesRemainingOnWin.Clear();
            _run = 0;
            _phase = Phase.Measuring;
        }

        private void MeasureOneRun()
        {
            int seed = _candidateSeed + _run * 7919;
            var stats = LevelSimulator.Run(_candidate, seed, AgentFactory.Create(_settings.Agent, seed), _rules);
            _report.Accumulate(stats);
            if (stats.Outcome == LevelOutcome.Won) _movesRemainingOnWin.Add(stats.MovesRemaining);
            _result.Playthroughs++;
            _run++;

            if (_run < _settings.Runs) return;

            _report.Finish(_movesRemainingOnWin);
            EvaluateMeasurement();
        }

        private void EvaluateMeasurement()
        {
            float distance = Math.Abs(_report.WinRate - _request.TargetWinRate);
            if (distance < _best)
            {
                _best = distance;
                _result.Level = _candidate.Clone();
                _result.Report = _report;
                _result.InBand = distance <= _settings.Tolerance;
            }

            if (distance <= _settings.Tolerance)
            {
                _phase = Phase.Finished;
                return;
            }

            // A board that collapses on its own is not a difficulty problem, and no number of extra moves
            // will fix it. Abandon the page rather than tuning around it.
            bool exhausted = _report.DeadBoardRate > 0.15f
                             || _retune >= _settings.MaxRetunes
                             || !TryAdjustDifficulty(_candidate, _report.WinRate, _request.TargetWinRate);

            if (exhausted)
            {
                _phase = Phase.NeedCandidate;
                return;
            }

            _retune++;
            BeginMeasurement();
        }

        /// <summary>
        /// Nothing landed in band. Rather than serve an unmeasured page, fall back to a deliberately
        /// gentler request — a flat, simpler page — and measure that instead.
        /// </summary>
        private void FinishWithFallbackIfNeeded()
        {
            if (_result.Level != null || _fallbackUsed)
            {
                _phase = Phase.Finished;
                return;
            }

            _fallbackUsed = true;
            _candidateSeed = Mix(_request.Seed, 9973);
            _candidate = PageWeaver.Weave(Derive(_candidateSeed, _request.Difficulty * 0.6f, false));
            _result.Candidates++;
            _retune = _settings.MaxRetunes; // measure once, then take it
            BeginMeasurement();
        }

        private WeaveRequest Derive(int seed, float difficulty, bool allowFold) => new WeaveRequest
        {
            Seed = seed,
            Key = _request.Key,
            Id = _request.Id,
            Name = _request.Name,
            Difficulty = difficulty,
            TargetWinRate = _request.TargetWinRate,
            AllowFold = allowFold,
            Book = _request.Book
        };

        /// <summary>
        /// Two dials, tried in order. Moves come first because changing the budget does not change how a
        /// board reads. When the budget saturates — still a walkover at fourteen moves, still brutal at
        /// forty-eight — the objectives are what is wrong, so the goal counts move instead. Returns false
        /// only when neither dial has any travel left, which is the signal to throw the page away.
        /// </summary>
        private static bool TryAdjustDifficulty(LevelDefinition level, float measured, float target)
        {
            if (TryAdjustMoves(level, measured, target)) return true;

            float error = measured - target;
            float factor = error > 0f
                ? 1f + Math.Min(0.45f, error * 0.9f)   // too easy — ask for more
                : 1f - Math.Min(0.35f, -error * 0.9f); // too hard — ask for less

            return PageWeaver.TryScaleGoals(level, factor);
        }

        /// <summary>
        /// Nudges the move budget towards the target win rate. Returns false when the dial has run out of
        /// travel.
        /// </summary>
        private static bool TryAdjustMoves(LevelDefinition level, float measured, float target)
        {
            float error = target - measured;
            int delta = (int)Math.Round(error * level.Moves * 1.2f);
            if (delta == 0) delta = error > 0f ? 1 : -1;

            int next = level.Moves + delta;
            if (next < PageWeaver.MinMoves) next = PageWeaver.MinMoves;
            if (next > PageWeaver.MaxMoves) next = PageWeaver.MaxMoves;
            if (next == level.Moves) return false;

            level.Moves = next;
            return true;
        }

        private static int Mix(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u;
                h ^= (uint)(salt + 1) * 2246822519u;
                h ^= h >> 15;
                h *= 2654435761u;
                h ^= h >> 13;
                return (int)h;
            }
        }
    }
}
