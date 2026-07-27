using System.Collections.Generic;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Events
{
    /// <summary>
    /// Everything the simulation does is reported as an ordered stream of these. The view never reads
    /// board state directly — it consumes this stream and turns it into animation. That is what lets
    /// the same core run headlessly at ten thousand levels a second inside the simulator.
    /// </summary>
    public interface IBoardEvent
    {
        int SequenceIndex { get; }
    }

    public abstract class BoardEvent : IBoardEvent
    {
        public int SequenceIndex { get; internal set; }
        public override string ToString() => $"#{SequenceIndex} {GetType().Name}";
    }

    /// <summary>Where events go. <see cref="BoardEventLog"/> is the standard implementation.</summary>
    public interface IBoardEventSink
    {
        void Publish(BoardEvent boardEvent);
    }

    public sealed class BoardEventLog : IBoardEventSink
    {
        private readonly List<BoardEvent> _events = new List<BoardEvent>();
        private int _next;

        public IReadOnlyList<BoardEvent> Events => _events;
        public int Count => _events.Count;

        /// <summary>Set false by the simulator: bots do not need the event history, only the outcome.</summary>
        public bool Recording { get; set; } = true;

        public void Publish(BoardEvent boardEvent)
        {
            boardEvent.SequenceIndex = _next++;
            if (Recording) _events.Add(boardEvent);
        }

        public void Clear()
        {
            _events.Clear();
        }

        /// <summary>Drains the buffered events, keeping the sequence counter running.</summary>
        public List<BoardEvent> Drain()
        {
            var copy = new List<BoardEvent>(_events);
            _events.Clear();
            return copy;
        }
    }

    /// <summary>Sink that throws everything away. Used by bot look-ahead on cloned boards.</summary>
    public sealed class NullEventSink : IBoardEventSink
    {
        public static readonly NullEventSink Instance = new NullEventSink();
        private int _next;
        public void Publish(BoardEvent boardEvent) => boardEvent.SequenceIndex = _next++;
    }

    // ---------------------------------------------------------------- payload structs

    public readonly struct TileMove
    {
        public readonly int TileId;
        public readonly CellRef From;
        public readonly CellRef To;

        public TileMove(int tileId, CellRef from, CellRef to)
        {
            TileId = tileId;
            From = from;
            To = to;
        }
    }

    public readonly struct SpawnedTile
    {
        public readonly int TileId;
        public readonly CellRef At;
        public readonly TileColor Color;
        /// <summary>Cell the piece should visually drop in from — one step upstream of gravity.</summary>
        public readonly CellRef EntryFrom;

        public SpawnedTile(int tileId, CellRef at, TileColor color, CellRef entryFrom)
        {
            TileId = tileId;
            At = at;
            Color = color;
            EntryFrom = entryFrom;
        }
    }

    public enum MatchShape
    {
        Line3 = 0,
        Line4 = 1,
        Line5 = 2,
        /// <summary>Five cells across both axes — an L or a T.</summary>
        Corner = 3,
        /// <summary>Six or more cells in one connected group.</summary>
        Large = 4
    }

    // ---------------------------------------------------------------- events

    public sealed class SwapPerformedEvent : BoardEvent
    {
        public CellRef A { get; }
        public CellRef B { get; }
        public SwapPerformedEvent(CellRef a, CellRef b) { A = a; B = b; }
    }

    public sealed class SwapRejectedEvent : BoardEvent
    {
        public CellRef A { get; }
        public CellRef B { get; }
        public string Reason { get; }
        public SwapRejectedEvent(CellRef a, CellRef b, string reason) { A = a; B = b; Reason = reason; }
    }

    public sealed class MatchFoundEvent : BoardEvent
    {
        public IReadOnlyList<CellRef> Cells { get; }
        public TileColor Color { get; }
        public MatchShape Shape { get; }
        /// <summary>True when the group spans a Story Seam — this is what mints a Golden Stitch.</summary>
        public bool CrossesSeam { get; }

        public MatchFoundEvent(IReadOnlyList<CellRef> cells, TileColor color, MatchShape shape, bool crossesSeam)
        {
            Cells = cells; Color = color; Shape = shape; CrossesSeam = crossesSeam;
        }
    }

    public sealed class TilesClearedEvent : BoardEvent
    {
        public IReadOnlyList<CellRef> Cells { get; }
        public ClearCause Cause { get; }
        public TilesClearedEvent(IReadOnlyList<CellRef> cells, ClearCause cause) { Cells = cells; Cause = cause; }
    }

    public sealed class BoosterCreatedEvent : BoardEvent
    {
        public CellRef At { get; }
        public BoosterType Type { get; }
        public BoosterOrientation Orientation { get; }
        public TileColor Color { get; }
        /// <summary>True when the booster was born on the far side of a fold (Chain Fold upgrade).</summary>
        public bool FromChainFold { get; }

        public BoosterCreatedEvent(CellRef at, BoosterType type, BoosterOrientation orientation, TileColor color, bool fromChainFold = false)
        {
            At = at; Type = type; Orientation = orientation; Color = color; FromChainFold = fromChainFold;
        }
    }

    public sealed class BoosterActivatedEvent : BoardEvent
    {
        public CellRef At { get; }
        public BoosterType Type { get; }
        public BoosterOrientation Orientation { get; }
        public IReadOnlyList<CellRef> Affected { get; }
        /// <summary>Cells the effect reached on the hidden surface by curling around a seam.</summary>
        public IReadOnlyList<CellRef> WrappedThroughSeam { get; }

        public BoosterActivatedEvent(CellRef at, BoosterType type, BoosterOrientation orientation,
            IReadOnlyList<CellRef> affected, IReadOnlyList<CellRef> wrappedThroughSeam)
        {
            At = at; Type = type; Orientation = orientation; Affected = affected; WrappedThroughSeam = wrappedThroughSeam;
        }
    }

    public sealed class BoosterComboEvent : BoardEvent
    {
        public CellRef At { get; }
        public BoosterType First { get; }
        public BoosterType Second { get; }
        public string ComboName { get; }
        public BoosterComboEvent(CellRef at, BoosterType first, BoosterType second, string comboName)
        {
            At = at; First = first; Second = second; ComboName = comboName;
        }
    }

    public sealed class ObstacleDamagedEvent : BoardEvent
    {
        public CellRef At { get; }
        public ObstacleType Type { get; }
        public int Remaining { get; }
        public int Max { get; }
        public ObstacleDamagedEvent(CellRef at, ObstacleType type, int remaining, int max)
        {
            At = at; Type = type; Remaining = remaining; Max = max;
        }
    }

    public sealed class ObstacleClearedEvent : BoardEvent
    {
        public CellRef At { get; }
        public ObstacleType Type { get; }
        public int GroupId { get; }
        public ObstacleClearedEvent(CellRef at, ObstacleType type, int groupId) { At = at; Type = type; GroupId = groupId; }
    }

    /// <summary>One gravity pass. Every move inside it is a single-cell step, so the view can play them together.</summary>
    public sealed class TilesMovedEvent : BoardEvent
    {
        public IReadOnlyList<TileMove> Moves { get; }
        public TilesMovedEvent(IReadOnlyList<TileMove> moves) { Moves = moves; }
    }

    public sealed class TilesSpawnedEvent : BoardEvent
    {
        public IReadOnlyList<SpawnedTile> Spawns { get; }
        public TilesSpawnedEvent(IReadOnlyList<SpawnedTile> spawns) { Spawns = spawns; }
    }

    public sealed class TilePortalledEvent : BoardEvent
    {
        public int TileId { get; }
        public CellRef From { get; }
        public CellRef To { get; }
        public TilePortalledEvent(int tileId, CellRef from, CellRef to) { TileId = tileId; From = from; To = to; }
    }

    public sealed class BoardFoldedEvent : BoardEvent
    {
        public int RegionId { get; }
        public GridRect Area { get; }
        public Axis FlipAxis { get; }
        public SurfaceSide NewSide { get; }
        public Direction NewGravity { get; }
        public BoardFoldedEvent(int regionId, GridRect area, Axis flipAxis, SurfaceSide newSide, Direction newGravity)
        {
            RegionId = regionId; Area = area; FlipAxis = flipAxis; NewSide = newSide; NewGravity = newGravity;
        }
    }

    /// <summary>
    /// Chain Fold: a booster sitting on the seam was dragged through the paper by the fold and came out
    /// the other side as something stronger.
    /// </summary>
    public sealed class ChainFoldEvent : BoardEvent
    {
        public CellRef From { get; }
        public CellRef To { get; }
        public BoosterType Before { get; }
        public BoosterType After { get; }
        public ChainFoldEvent(CellRef from, CellRef to, BoosterType before, BoosterType after)
        {
            From = from; To = to; Before = before; After = after;
        }
    }

    /// <summary>A Blank tile drank the colour of a match resolved next to it.</summary>
    public sealed class BlankTileInkedEvent : BoardEvent
    {
        public CellRef At { get; }
        public TileColor Color { get; }
        public BlankTileInkedEvent(CellRef at, TileColor color) { At = at; Color = color; }
    }

    public sealed class FoldRejectedEvent : BoardEvent
    {
        public int RegionId { get; }
        public string Reason { get; }
        public FoldRejectedEvent(int regionId, string reason) { RegionId = regionId; Reason = reason; }
    }

    public sealed class FoldMeterChangedEvent : BoardEvent
    {
        public int Value { get; }
        public int Max { get; }
        public bool JustFilled { get; }
        public FoldMeterChangedEvent(int value, int max, bool justFilled) { Value = value; Max = max; JustFilled = justFilled; }
    }

    public sealed class MovesChangedEvent : BoardEvent
    {
        public int Remaining { get; }
        public MovesChangedEvent(int remaining) { Remaining = remaining; }
    }

    public sealed class GoalProgressEvent : BoardEvent
    {
        public string GoalId { get; }
        public int Current { get; }
        public int Target { get; }
        public bool IsComplete { get; }
        public GoalProgressEvent(string goalId, int current, int target, bool isComplete)
        {
            GoalId = goalId; Current = current; Target = target; IsComplete = isComplete;
        }
    }

    public sealed class WalkerMovedEvent : BoardEvent
    {
        public int WalkerId { get; }
        public IReadOnlyList<GridCoord> Path { get; }
        public bool ReachedGoal { get; }
        public WalkerMovedEvent(int walkerId, IReadOnlyList<GridCoord> path, bool reachedGoal)
        {
            WalkerId = walkerId; Path = path; ReachedGoal = reachedGoal;
        }
    }

    public sealed class BlankSpreadEvent : BoardEvent
    {
        public IReadOnlyList<CellRef> Cells { get; }
        public BlankSpreadEvent(IReadOnlyList<CellRef> cells) { Cells = cells; }
    }

    public sealed class BoardShuffledEvent : BoardEvent
    {
        public int Attempts { get; }
        public bool Succeeded { get; }
        public BoardShuffledEvent(int attempts, bool succeeded) { Attempts = attempts; Succeeded = succeeded; }
    }

    /// <summary>Marks the boundary between cascade generations so the view can pace combo callouts.</summary>
    public sealed class CascadeStepEvent : BoardEvent
    {
        public int Depth { get; }
        public CascadeStepEvent(int depth) { Depth = depth; }
    }

    public sealed class LevelFinishedEvent : BoardEvent
    {
        public LevelOutcome Outcome { get; }
        public int MovesRemaining { get; }
        public LevelFinishedEvent(LevelOutcome outcome, int movesRemaining) { Outcome = outcome; MovesRemaining = movesRemaining; }
    }
}
