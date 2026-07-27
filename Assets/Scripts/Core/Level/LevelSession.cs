using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Boosters;
using Wonderfold.Core.Events;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Goals;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;

namespace Wonderfold.Core.Level
{
    /// <summary>
    /// One playthrough of one level: the board, the objectives, the moves, the fold meter and the rules
    /// for how a turn begins and ends.
    ///
    /// <para>This is the entire public surface of the gameplay core. The Unity view, the bot agents and
    /// the editor preview all drive a session through <see cref="TryExecute"/> and read back
    /// <see cref="Events"/>. Nothing above this class is allowed to reach into the board directly.</para>
    /// </summary>
    public sealed class LevelSession : IResolutionListener, IBoosterTargetOracle
    {
        private readonly List<ILevelGoal> _goals = new List<ILevelGoal>();
        private readonly List<ILevelModifier> _modifiers = new List<ILevelModifier>();
        private readonly FoldService _foldService = new FoldService();
        private bool _meterDirty;
        private bool _meterJustFilled;
        private bool _started;

        public LevelDefinition Definition { get; }
        public GameRules Rules { get; }
        public BoardModel Board { get; }
        public ResolutionEngine Engine { get; }
        public BoardEventLog Events { get; }
        public FoldMeter FoldMeter { get; }
        
        public int InitialSeed { get; }
        public List<PlayerMove> MoveHistory { get; } = new List<PlayerMove>();

        public int MovesRemaining { get; private set; }
        public int TurnNumber { get; private set; }
        public LevelOutcome Outcome { get; private set; } = LevelOutcome.InProgress;

        public IReadOnlyList<ILevelGoal> Goals => _goals;
        public IReadOnlyList<ILevelModifier> Modifiers => _modifiers;
        public DeterministicRandom Rng => Board.Rng;
        public bool IsOver => Outcome != LevelOutcome.InProgress;

        /// <summary>Statistics the Level Laboratory reads back after a simulated run.</summary>
        public LevelRunStats Stats { get; } = new LevelRunStats();

        public LevelSession(LevelDefinition definition, int seed, GameRules rules = null)
        {
            Definition = definition;
            InitialSeed = seed;
            Rules = rules ?? definition.Rules ?? GameRules.Default;
            Events = new BoardEventLog();
            Board = LevelBuilder.Build(definition, seed);
            Engine = new ResolutionEngine(Rules);
            Engine.Boosters.Oracle = this;
            FoldMeter = new FoldMeter(Rules, definition.FoldMeterMax);
            MovesRemaining = definition.Moves;

            _goals.AddRange(definition.CreateGoals());
            _modifiers.AddRange(definition.CreateModifiers());
            WireBossGoal(definition);
        }

        /// <summary>
        /// The boss objective is the one goal that has to be handed its modifier: "beat the dragon" is
        /// literally "the dragon ran out of armour phases", and only the modifier knows that.
        /// </summary>
        private void WireBossGoal(LevelDefinition definition)
        {
            PopUpBossModifier boss = null;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (_modifiers[i] is PopUpBossModifier found) { boss = found; break; }
            }

            if (boss == null) return;

            for (int i = 0; i < definition.Goals.Count; i++)
            {
                var type = definition.Goals[i].Type;
                if (type != null && type.ToLowerInvariant() == "boss") _goals.Add(new BossPhaseGoal(boss));
            }
        }

        /// <summary>Fills the board, removes accidental matches and lets goals size themselves.</summary>
        public void Start()
        {
            if (_started) return;
            _started = true;

            Engine.SettleSilently(Board);

            for (int i = 0; i < _goals.Count; i++) _goals[i].Initialise(Board);
            for (int i = 0; i < _modifiers.Count; i++) _modifiers[i].Initialise(this);

            Engine.EnsurePlayable(Board, Events, this);
            PublishGoalProgress();
            Events.Publish(new MovesChangedEvent(MovesRemaining));
            Events.Publish(new FoldMeterChangedEvent(FoldMeter.Value, FoldMeter.Max, false));
            EvaluateOutcome();
        }

        // ------------------------------------------------------------------ turn loop

        public bool TryExecute(PlayerMove move, out string reason)
        {
            reason = null;
            if (IsOver)
            {
                reason = "The level is already over.";
                return false;
            }

            if (!_started) Start();

            for (int i = 0; i < _modifiers.Count; i++) _modifiers[i].OnTurnStarted(this);

            bool executed;
            switch (move.Kind)
            {
                case MoveKind.Swap:
                    executed = ExecuteSwap(move, out reason);
                    break;
                case MoveKind.ActivateBooster:
                    executed = ExecuteActivate(move, out reason);
                    break;
                case MoveKind.Fold:
                    executed = ExecuteFold(move, out reason);
                    break;
                case MoveKind.UseTool:
                    executed = ExecuteTool(move, out reason);
                    break;
                default:
                    reason = "Unknown move.";
                    return false;
            }

            if (!executed) return false;
            
            MoveHistory.Add(move);

            TurnNumber++;
            Stats.Turns = TurnNumber;

            for (int i = 0; i < _modifiers.Count; i++) _modifiers[i].OnTurnEnded(this);
            for (int i = 0; i < _goals.Count; i++) _goals[i].OnTurnEnded(Board);

            // A modifier may have dropped fresh obstacles or tiles onto the board.
            Engine.ResolveUntilStable(Board, Events, this);

            FlushMeter();
            PublishGoalProgress();
            Events.Publish(new MovesChangedEvent(MovesRemaining));

            if (!Engine.EnsurePlayable(Board, Events, this) && !AllGoalsComplete())
            {
                Finish(LevelOutcome.LostBoardCollapsed);
                return true;
            }

            EvaluateOutcome();
            return true;
        }

        public bool TryExecute(PlayerMove move) => TryExecute(move, out _);

        /// <summary>
        /// Converts one settled starting tile into a rocket before the player receives control. This is
        /// the core half of a pre-level booster: the meta layer chooses and pays for it, while the level
        /// stays deterministic and the board never has to know about currencies.
        /// </summary>
        public bool TryPlaceStartingRocket()
        {
            if (!_started || IsOver) return false;

            GridCoord best = GridCoord.Invalid;
            float bestDistance = float.MaxValue;
            float centreX = (Board.Width - 1) * 0.5f;
            float centreY = (Board.Height - 1) * 0.5f;
            foreach (var coord in Board.AllCoords())
            {
                var cell = Board.ActiveCell(coord);
                if (cell?.Tile == null || cell.Tile.IsBooster || !cell.CanHoldTile) continue;
                float dx = coord.X - centreX;
                float dy = coord.Y - centreY;
                float distance = dx * dx + dy * dy;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = coord;
            }

            if (!best.IsValid) return false;
            var target = Board.ActiveCell(best);
            target.Tile.Booster = BoosterType.RibbonRocket;
            target.Tile.Orientation = BoosterOrientation.Horizontal;
            Events.Publish(new BoosterCreatedEvent(target.Ref, BoosterType.RibbonRocket,
                BoosterOrientation.Horizontal, target.Tile.Color));
            Stats.BoostersCreated++;
            return true;
        }

        private bool ExecuteSwap(PlayerMove move, out string reason)
        {
            if (!Engine.TrySwap(Board, move.A, move.B, Events, this, out reason)) return false;
            SpendMoves(1);
            Stats.Swaps++;
            return true;
        }

        private bool ExecuteActivate(PlayerMove move, out string reason)
        {
            if (!Engine.TryActivateBooster(Board, move.A, Events, this, out reason)) return false;
            SpendMoves(1);
            Stats.BoosterActivations++;
            return true;
        }

        private bool ExecuteFold(PlayerMove move, out string reason)
        {
            int index = Board.Folds.IndexOfRegionId(move.RegionId);
            var validation = _foldService.Validate(Board, index, FoldMeter, MovesRemaining);
            if (!validation.IsValid)
            {
                reason = validation.Reason;
                Events.Publish(new FoldRejectedEvent(move.RegionId, reason));
                return false;
            }

            reason = null;
            var region = Board.Folds.Regions[index];
            FoldMeter.Pay(region.MeterCost);
            _meterDirty = true;
            SpendMoves(region.MoveCost);

            _foldService.Fold(Board, index, Engine, Events, this);
            Stats.Folds++;

            for (int i = 0; i < _goals.Count; i++) _goals[i].OnFolded(Board, region.Id);
            return true;
        }

        /// <summary>
        /// Resolves an inventory-backed tool. The Unity/meta layer is responsible for reserving and
        /// refunding inventory around this method; keeping that concern out of the core preserves the
        /// simulator and makes tools replayable without introducing an economy into level data.
        /// </summary>
        private bool ExecuteTool(PlayerMove move, out string reason)
        {
            if (!Board.InBounds(move.A))
            {
                reason = "Choose a cell on the page.";
                return false;
            }

            var cell = Board.ActiveCell(move.A);
            if (cell == null || cell.IsVoid)
            {
                reason = "That part of the page is missing.";
                return false;
            }

            switch (move.Tool)
            {
                case PageTool.Hammer:
                {
                    bool changed = false;
                    var cleared = new List<CellRef>();
                    if (cell.Tile != null)
                    {
                        Engine.ClearTile(Board, move.A, ClearCause.Booster, Events, this, cleared);
                        changed = true;
                    }

                    // A hammer is a precise booster hit: it can open even blast-only blockers.
                    if (cell.Obstacle != null && Engine.Obstacles.Damage(
                        Board, cell.Ref, Wonderfold.Core.Obstacles.DamageSource.Blast, Events, this)) changed = true;

                    if (cleared.Count > 0) Events.Publish(new TilesClearedEvent(cleared, ClearCause.Booster));
                    if (!changed)
                    {
                        reason = "There is nothing for the hammer to mend here.";
                        return false;
                    }

                    Engine.ResolveUntilStable(Board, Events, this);
                    reason = null;
                    return true;
                }

                case PageTool.RibbonRocket:
                {
                    if (cell.Tile == null || !cell.CanHoldTile)
                    {
                        reason = "Place the rocket on a visible tile.";
                        return false;
                    }

                    var old = cell.Tile;
                    cell.Tile = Board.CreateBooster(BoosterType.RibbonRocket, old.Color,
                        BoosterOrientation.Horizontal);
                    Events.Publish(new BoosterCreatedEvent(cell.Ref, BoosterType.RibbonRocket,
                        BoosterOrientation.Horizontal, old.Color));
                    if (!Engine.TryActivateBooster(Board, move.A, Events, this, out reason)) return false;
                    Stats.BoosterActivations++;
                    return true;
                }

                default:
                    reason = "Unknown page tool.";
                    return false;
            }
        }

        private void SpendMoves(int count)
        {
            if (count <= 0) return;
            MovesRemaining -= count;
            if (MovesRemaining < 0) MovesRemaining = 0;
        }

        // ------------------------------------------------------------------ outcome

        public bool AllGoalsComplete()
        {
            for (int i = 0; i < _goals.Count; i++)
            {
                if (!_goals[i].IsComplete) return false;
            }

            return true;
        }

        private void EvaluateOutcome()
        {
            if (IsOver) return;

            if (AllGoalsComplete())
            {
                PlayWinCelebration();
                Finish(LevelOutcome.Won);
                return;
            }

            if (MovesRemaining <= 0) Finish(LevelOutcome.LostOutOfMoves);
        }

        /// <summary>
        /// Leftover moves become rockets and go off. Purely for the feeling of a level ending well —
        /// nothing about the outcome depends on it.
        /// </summary>
        private void PlayWinCelebration()
        {
            if (!Rules.ConvertLeftoverMovesToBoosters || MovesRemaining <= 0) return;

            int budget = MovesRemaining < Rules.MaxLeftoverMoveBoosters
                ? MovesRemaining
                : Rules.MaxLeftoverMoveBoosters;

            var candidates = new List<GridCoord>();
            foreach (var coord in Board.AllCoords())
            {
                var cell = Board.ActiveCell(coord);
                if (cell?.Tile != null && !cell.Tile.IsBooster && cell.CanHoldTile) candidates.Add(coord);
            }

            if (candidates.Count == 0) return;
            Rng.Shuffle(candidates);

            var placed = new List<GridCoord>();
            for (int i = 0; i < budget && i < candidates.Count; i++)
            {
                var cell = Board.ActiveCell(candidates[i]);
                cell.Tile.Booster = BoosterType.RibbonRocket;
                cell.Tile.Orientation = i % 2 == 0 ? BoosterOrientation.Horizontal : BoosterOrientation.Vertical;
                Events.Publish(new BoosterCreatedEvent(cell.Ref, BoosterType.RibbonRocket,
                    cell.Tile.Orientation, cell.Tile.Color));
                placed.Add(candidates[i]);
            }

            Engine.Boosters.DetonateAll(Board, placed, Events, this);
            Engine.ResolveUntilStable(Board, Events, this);
        }

        private void Finish(LevelOutcome outcome)
        {
            Outcome = outcome;
            Stats.Outcome = outcome;
            Stats.MovesRemaining = MovesRemaining;
            Stats.MovesUsed = Definition.Moves - MovesRemaining;
            Events.Publish(new LevelFinishedEvent(outcome, MovesRemaining));
        }

        private void PublishGoalProgress()
        {
            for (int i = 0; i < _goals.Count; i++)
            {
                var goal = _goals[i];
                Events.Publish(new GoalProgressEvent(goal.Id, goal.Current, goal.Target, goal.IsComplete));
            }
        }

        private void FlushMeter()
        {
            if (!_meterDirty) return;
            _meterDirty = false;
            Events.Publish(new FoldMeterChangedEvent(FoldMeter.Value, FoldMeter.Max, _meterJustFilled));
            _meterJustFilled = false;
        }

        // ------------------------------------------------------------------ fold helpers for the UI

        public FoldValidation CanFold(int regionId)
        {
            int index = Board.Folds.IndexOfRegionId(regionId);
            return _foldService.Validate(Board, index, FoldMeter, MovesRemaining);
        }

        public List<int> AvailableFoldRegionIds()
        {
            var result = new List<int>();
            for (int i = 0; i < Board.Folds.RegionCount; i++)
            {
                var region = Board.Folds.Regions[i];
                if (_foldService.Validate(Board, i, FoldMeter, MovesRemaining).IsValid) result.Add(region.Id);
            }

            return result;
        }

        /// <summary>Every move the player could legally make right now. Drives hints and the bots.</summary>
        public List<PlayerMove> EnumerateLegalMoves(bool includeFolds = true, bool includeActivations = true)
        {
            var moves = Engine.Validator.EnumerateSwaps(Board);
            if (includeActivations) moves.AddRange(Engine.Validator.EnumerateActivations(Board));
            if (!includeFolds) return moves;

            var folds = AvailableFoldRegionIds();
            for (int i = 0; i < folds.Count; i++) moves.Add(PlayerMove.Fold(folds[i]));
            return moves;
        }

        // ------------------------------------------------------------------ IResolutionListener

        void IResolutionListener.OnTileCleared(CellRef at, TilePiece tile, ClearCause cause)
        {
            Stats.TilesCleared++;
            if (FoldMeter.ChargeForTile()) _meterJustFilled = true;
            _meterDirty = true;

            for (int i = 0; i < _goals.Count; i++) _goals[i].OnTileCleared(Board, at, tile, cause);
        }

        void IResolutionListener.OnObstacleDamaged(CellRef at, Obstacle obstacle, ClearCause cause)
        {
            if (FoldMeter.ChargeForObstacleHit()) _meterJustFilled = true;
            _meterDirty = true;
        }

        void IResolutionListener.OnObstacleCleared(CellRef at, Obstacle obstacle)
        {
            Stats.ObstaclesCleared++;
            for (int i = 0; i < _goals.Count; i++) _goals[i].OnObstacleCleared(Board, at, obstacle);
        }

        void IResolutionListener.OnMatchResolved(MatchGroup group)
        {
            Stats.Matches++;
            if (group.CrossesSeam)
            {
                Stats.SeamMatches++;
                if (FoldMeter.ChargeForSeamMatch()) _meterJustFilled = true;
                _meterDirty = true;
            }

            for (int i = 0; i < _goals.Count; i++) _goals[i].OnMatchResolved(Board, group);
        }

        void IResolutionListener.OnBoosterCreated(CellRef at, BoosterType type)
        {
            Stats.BoostersCreated++;
            if (type == BoosterType.GoldenStitch) Stats.GoldenStitches++;
        }

        void IResolutionListener.OnBoosterActivated(CellRef at, BoosterType type)
        {
            Stats.BoosterDetonations++;
        }

        // ------------------------------------------------------------------ IBoosterTargetOracle

        /// <summary>
        /// Smart boosters aim at whatever the level is actually asking for. Without this the Origami Bird
        /// picks the toughest obstacle on the board even when the objective is a colour, which reads as
        /// the game not paying attention.
        /// </summary>
        int IBoosterTargetOracle.ScoreTarget(BoardModel board, GridCoord coord)
        {
            int score = DefaultBoosterTargetOracle.Instance.ScoreTarget(board, coord);
            if (score == int.MinValue) return score;

            var cell = board.ActiveCell(coord);
            for (int i = 0; i < _goals.Count; i++)
            {
                var goal = _goals[i];
                if (goal.IsComplete) continue;

                if (goal is ClearObstacleGoal obstacleGoal && cell.HasObstacle &&
                    cell.Obstacle.Type == obstacleGoal.ObstacleType)
                {
                    score += 200;
                }
                else if (goal is CollectColorGoal colorGoal && cell.Tile != null &&
                         cell.Tile.Color == colorGoal.Color)
                {
                    score += 35;
                }
                else if (goal is RestoreIllustrationGoal && cell.IsIllustration && cell.HasObstacle)
                {
                    score += 180;
                }
            }

            return score;
        }

        TileColor IBoosterTargetOracle.PreferredColor(BoardModel board)
        {
            for (int i = 0; i < _goals.Count; i++)
            {
                if (_goals[i] is CollectColorGoal colorGoal && !colorGoal.IsComplete) return colorGoal.Color;
            }

            return DefaultBoosterTargetOracle.Instance.PreferredColor(board);
        }
    }

    /// <summary>Per-run counters. Aggregated across thousands of runs by the Level Laboratory.</summary>
    public sealed class LevelRunStats
    {
        public LevelOutcome Outcome;
        public int Turns;
        public int Swaps;
        public int Folds;
        public int BoosterActivations;
        public int MovesUsed;
        public int MovesRemaining;
        public int TilesCleared;
        public int ObstaclesCleared;
        public int Matches;
        public int SeamMatches;
        public int BoostersCreated;
        public int BoosterDetonations;
        public int GoldenStitches;
        public int Shuffles;

        /// <summary>Filled in by the simulator when a run ends, so a report can say which goal blocked it.</summary>
        public List<Simulation.GoalSnapshot> GoalCompletion;
    }
}
