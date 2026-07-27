using System.Collections.Generic;
using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Obstacles;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;

namespace Wonderfold.Core.Boosters
{
    /// <summary>
    /// Fires boosters and their combinations.
    ///
    /// <para>Detonations are processed through an explicit queue rather than recursion. A rocket that
    /// sets off a bloom that sets off three more rockets is exactly the moment a match-3 is supposed to
    /// feel great, and it is also exactly the moment a naive implementation blows the stack — the queue
    /// plus <see cref="GameRules.MaxBoosterChainDepth"/> makes the fireworks unbounded in fun and
    /// bounded in cost.</para>
    ///
    /// <para>Two behaviours are unique to Wonderfold. A Ribbon Rocket that meets a Story Seam curls
    /// around the fold and keeps burning on the face you cannot see. A Golden Stitch — born only from a
    /// match that spans a seam — sews straight through the paper, hitting both faces at every cell.</para>
    /// </summary>
    public sealed class BoosterEngine
    {
        private readonly GameRules _rules;
        private readonly ObstacleService _obstacles;
        private readonly Queue<Blast> _queue = new Queue<Blast>();
        private readonly List<CellRef> _affected = new List<CellRef>();
        private readonly List<CellRef> _wrapped = new List<CellRef>();

        public IBoosterTargetOracle Oracle { get; set; } = DefaultBoosterTargetOracle.Instance;

        public BoosterEngine(GameRules rules, ObstacleService obstacles)
        {
            _rules = rules;
            _obstacles = obstacles;
        }

        private readonly struct Blast
        {
            public readonly GridCoord At;
            public readonly BoosterType Type;
            public readonly BoosterOrientation Orientation;
            public readonly TileColor Color;
            /// <summary>Radius / line width / target count multiplier applied by combos.</summary>
            public readonly int Power;
            /// <summary>Effect delivered at each target by carrier boosters (Bird, Golden Stitch, Prism).</summary>
            public readonly BoosterType Payload;

            public Blast(GridCoord at, BoosterType type, BoosterOrientation orientation, TileColor color,
                int power = 1, BoosterType payload = BoosterType.None)
            {
                At = at; Type = type; Orientation = orientation; Color = color; Power = power; Payload = payload;
            }
        }

        // ------------------------------------------------------------------ public entry points

        /// <summary>Sets off a booster sitting on the board (a tap, or a chain reaction).</summary>
        public bool Detonate(BoardModel board, GridCoord at, IBoardEventSink sink, IResolutionListener listener)
        {
            if (!TryLift(board, at, sink, listener, out var blast)) return false;
            _queue.Enqueue(blast);
            Run(board, sink, listener);
            return true;
        }

        /// <summary>Fires the combination produced by dragging two boosters together.</summary>
        public bool DetonateCombo(BoardModel board, GridCoord a, GridCoord b, IBoardEventSink sink,
            IResolutionListener listener)
        {
            var cellA = board.ActiveCell(a);
            var cellB = board.ActiveCell(b);
            if (cellA?.Tile == null || cellB?.Tile == null) return false;
            if (!cellA.Tile.IsBooster || !cellB.Tile.IsBooster) return false;

            var first = cellA.Tile;
            var second = cellB.Tile;

            // Normalise so the table only needs one ordering.
            if ((int)second.Booster < (int)first.Booster)
            {
                (first, second) = (second, first);
                (a, b) = (b, a);
                (cellA, cellB) = (cellB, cellA);
            }

            cellA.Tile = null;
            cellB.Tile = null;
            listener.OnTileCleared(cellA.Ref, first, ClearCause.Booster);
            listener.OnTileCleared(cellB.Ref, second, ClearCause.Booster);

            var color = first.Color != TileColor.None ? first.Color : second.Color;
            sink.Publish(new BoosterComboEvent(cellB.Ref, first.Booster, second.Booster,
                ComboName(first.Booster, second.Booster)));

            EnqueueCombo(board, b, first.Booster, second.Booster, first.Orientation, second.Orientation, color);
            Run(board, sink, listener);
            return true;
        }

        /// <summary>
        /// Detonates a booster the player swapped into an ordinary tile — used by the celebration at the
        /// end of a level and by in-game boosters that spawn a special directly.
        /// </summary>
        public void DetonateAll(BoardModel board, IEnumerable<GridCoord> coords, IBoardEventSink sink,
            IResolutionListener listener)
        {
            foreach (var c in coords)
            {
                if (TryLift(board, c, sink, listener, out var blast)) _queue.Enqueue(blast);
            }

            Run(board, sink, listener);
        }

        // ------------------------------------------------------------------ combo table

        private void EnqueueCombo(BoardModel board, GridCoord at, BoosterType first, BoosterType second,
            BoosterOrientation orientA, BoosterOrientation orientB, TileColor color)
        {
            // Golden Stitch takes precedence: it carries whatever it is paired with along the fold.
            if (first == BoosterType.GoldenStitch || second == BoosterType.GoldenStitch)
            {
                var other = first == BoosterType.GoldenStitch ? second : first;
                var orientation = first == BoosterType.GoldenStitch ? orientA : orientB;
                if (orientation == BoosterOrientation.None) orientation = BoosterOrientation.Horizontal;
                _queue.Enqueue(new Blast(at, BoosterType.GoldenStitch, orientation, color, 2,
                    other == BoosterType.GoldenStitch ? BoosterType.None : other));
                return;
            }

            switch (first)
            {
                case BoosterType.RibbonRocket when second == BoosterType.RibbonRocket:
                    _queue.Enqueue(new Blast(at, BoosterType.RibbonRocket, BoosterOrientation.Horizontal, color));
                    _queue.Enqueue(new Blast(at, BoosterType.RibbonRocket, BoosterOrientation.Vertical, color));
                    return;

                case BoosterType.RibbonRocket when second == BoosterType.InkBloom:
                    _queue.Enqueue(new Blast(at, BoosterType.RibbonRocket, BoosterOrientation.Horizontal, color, 2));
                    _queue.Enqueue(new Blast(at, BoosterType.RibbonRocket, BoosterOrientation.Vertical, color, 2));
                    return;

                case BoosterType.RibbonRocket when second == BoosterType.OrigamiBird:
                    _queue.Enqueue(new Blast(at, BoosterType.OrigamiBird, BoosterOrientation.None, color,
                        _rules.BirdComboTargetCount, BoosterType.RibbonRocket));
                    return;

                case BoosterType.RibbonRocket when second == BoosterType.PrismBookmark:
                    _queue.Enqueue(new Blast(at, BoosterType.PrismBookmark, BoosterOrientation.None, color, 1,
                        BoosterType.RibbonRocket));
                    return;

                case BoosterType.InkBloom when second == BoosterType.InkBloom:
                    _queue.Enqueue(new Blast(at, BoosterType.InkBloom, BoosterOrientation.None, color,
                        _rules.InkBloomComboRadius));
                    return;

                case BoosterType.InkBloom when second == BoosterType.OrigamiBird:
                    _queue.Enqueue(new Blast(at, BoosterType.OrigamiBird, BoosterOrientation.None, color,
                        _rules.BirdComboTargetCount, BoosterType.InkBloom));
                    return;

                case BoosterType.InkBloom when second == BoosterType.PrismBookmark:
                    _queue.Enqueue(new Blast(at, BoosterType.PrismBookmark, BoosterOrientation.None, color, 1,
                        BoosterType.InkBloom));
                    return;

                case BoosterType.OrigamiBird when second == BoosterType.OrigamiBird:
                    _queue.Enqueue(new Blast(at, BoosterType.OrigamiBird, BoosterOrientation.None, color,
                        _rules.BirdComboTargetCount + 2, BoosterType.InkBloom));
                    return;

                case BoosterType.OrigamiBird when second == BoosterType.PrismBookmark:
                    _queue.Enqueue(new Blast(at, BoosterType.PrismBookmark, BoosterOrientation.None, color, 1,
                        BoosterType.OrigamiBird));
                    return;

                case BoosterType.PrismBookmark when second == BoosterType.PrismBookmark:
                    // Wipe the page clean. The most expensive and most satisfying thing in the game.
                    _queue.Enqueue(new Blast(at, BoosterType.PrismBookmark, BoosterOrientation.None,
                        TileColor.None, 99));
                    return;
            }

            // Anything unmapped still does something reasonable rather than nothing.
            _queue.Enqueue(new Blast(at, first, orientA, color));
            _queue.Enqueue(new Blast(at, second, orientB, color));
        }

        public static string ComboName(BoosterType a, BoosterType b)
        {
            if ((int)b < (int)a) (a, b) = (b, a);
            if (a == BoosterType.GoldenStitch || b == BoosterType.GoldenStitch) return "Bound in Gold";
            if (a == BoosterType.RibbonRocket && b == BoosterType.RibbonRocket) return "Ribbon Cross";
            if (a == BoosterType.RibbonRocket && b == BoosterType.InkBloom) return "Ink Comet";
            if (a == BoosterType.InkBloom && b == BoosterType.InkBloom) return "Ink Flood";
            if (a == BoosterType.PrismBookmark && b == BoosterType.PrismBookmark) return "Blank Page";
            if (a == BoosterType.OrigamiBird && b == BoosterType.OrigamiBird) return "Paper Flock";
            if (b == BoosterType.PrismBookmark) return "Prism Cascade";
            if (b == BoosterType.OrigamiBird) return "Escort Flight";
            return "Combination";
        }

        // ------------------------------------------------------------------ queue processing

        private bool TryLift(BoardModel board, GridCoord at, IBoardEventSink sink, IResolutionListener listener,
            out Blast blast)
        {
            blast = default;
            var cell = board.ActiveCell(at);
            var tile = cell?.Tile;
            if (tile == null || !tile.IsBooster) return false;

            cell.Tile = null;
            listener.OnTileCleared(cell.Ref, tile, ClearCause.Booster);
            blast = new Blast(at, tile.Booster, tile.Orientation, tile.Color);
            return true;
        }

        private void Run(BoardModel board, IBoardEventSink sink, IResolutionListener listener)
        {
            int processed = 0;
            while (_queue.Count > 0)
            {
                if (++processed > _rules.MaxBoosterChainDepth * 8)
                {
                    _queue.Clear();
                    break;
                }

                var blast = _queue.Dequeue();
                listener.OnBoosterActivated(new CellRef(blast.At, board.ActiveSide(blast.At)), blast.Type);

                _affected.Clear();
                _wrapped.Clear();

                switch (blast.Type)
                {
                    case BoosterType.RibbonRocket: CollectRocket(board, blast); break;
                    case BoosterType.InkBloom: CollectBloom(board, blast); break;
                    case BoosterType.OrigamiBird: CollectBird(board, blast, sink, listener); break;
                    case BoosterType.PrismBookmark: CollectPrism(board, blast, sink, listener); break;
                    case BoosterType.GoldenStitch: CollectStitch(board, blast, sink, listener); break;
                }

                sink.Publish(new BoosterActivatedEvent(new CellRef(blast.At, board.ActiveSide(blast.At)),
                    blast.Type, blast.Orientation, _affected.ToArray(), _wrapped.ToArray()));

                ApplyHits(board, sink, listener);
            }
        }

        private void ApplyHits(BoardModel board, IBoardEventSink sink, IResolutionListener listener)
        {
            // Copy because chained boosters push more work while we iterate.
            var active = _affected.ToArray();
            var hidden = _wrapped.ToArray();

            var cleared = new List<CellRef>();
            for (int i = 0; i < active.Length; i++) HitCell(board, active[i], true, sink, listener, cleared);
            for (int i = 0; i < hidden.Length; i++) HitCell(board, hidden[i], false, sink, listener, cleared);

            if (cleared.Count > 0) sink.Publish(new TilesClearedEvent(cleared, ClearCause.Booster));
        }

        /// <summary>
        /// Applies one blast hit. On the hidden face boosters are left intact rather than chained —
        /// a special you uncover later by folding is a gift, not a wasted explosion.
        /// </summary>
        private void HitCell(BoardModel board, CellRef reference, bool allowChaining, IBoardEventSink sink,
            IResolutionListener listener, List<CellRef> cleared)
        {
            var cell = board.CellAt(reference);
            if (cell == null || cell.IsVoid) return;

            if (cell.HasObstacle)
            {
                _obstacles.Damage(board, reference, DamageSource.Blast, sink, listener);
                if (cell.HasBlockingObstacle) return; // the plate soaked the hit; nothing behind it
            }

            var tile = cell.Tile;
            if (tile == null) return;

            if (tile.IsBooster)
            {
                if (!allowChaining) return;
                cell.Tile = null;
                listener.OnTileCleared(reference, tile, ClearCause.Booster);
                _queue.Enqueue(new Blast(reference.Coord, tile.Booster, tile.Orientation, tile.Color));
                return;
            }

            cell.Tile = null;
            listener.OnTileCleared(reference, tile, ClearCause.Booster);
            cleared.Add(reference);
        }

        // ------------------------------------------------------------------ effect shapes

        private void AddActive(BoardModel board, GridCoord c)
        {
            if (!board.InBounds(c)) return;
            var reference = new CellRef(c, board.ActiveSide(c));
            if (!_affected.Contains(reference)) _affected.Add(reference);
        }

        private void AddHidden(BoardModel board, GridCoord c)
        {
            if (!board.InBounds(c)) return;
            var hidden = board.HiddenCell(c);
            if (hidden == null) return;
            if (!_wrapped.Contains(hidden.Ref)) _wrapped.Add(hidden.Ref);
        }

        private void CollectRocket(BoardModel board, Blast blast)
        {
            var orientation = blast.Orientation == BoosterOrientation.None
                ? BoosterOrientation.Horizontal
                : blast.Orientation;

            int band = blast.Power >= 2 ? _rules.RocketComboBandHalfWidth : 0;
            var forward = orientation == BoosterOrientation.Horizontal ? Direction.Right : Direction.Up;
            var backward = forward.Opposite();

            for (int offset = -band; offset <= band; offset++)
            {
                var origin = orientation == BoosterOrientation.Horizontal
                    ? new GridCoord(blast.At.X, blast.At.Y + offset)
                    : new GridCoord(blast.At.X + offset, blast.At.Y);
                if (!board.InBounds(origin)) continue;

                AddActive(board, origin);
                TraceRocketArm(board, origin, forward);
                TraceRocketArm(board, origin, backward);
            }
        }

        /// <summary>
        /// Walks one arm of a rocket. The moment the arm crosses a Story Seam it also starts burning
        /// through to the face behind the fold for a few cells — the "curl around the page" beat.
        /// </summary>
        private void TraceRocketArm(BoardModel board, GridCoord origin, Direction direction)
        {
            var side = board.ActiveSide(origin);
            int wrapRemaining = 0;
            var cursor = origin;

            while (true)
            {
                cursor = cursor.Step(direction);
                if (!board.InBounds(cursor)) break;

                var cursorSide = board.ActiveSide(cursor);
                if (cursorSide != side)
                {
                    wrapRemaining = _rules.SeamWrapDistance;
                    side = cursorSide;
                }

                AddActive(board, cursor);
                if (wrapRemaining > 0)
                {
                    AddHidden(board, cursor);
                    wrapRemaining--;
                }
            }
        }

        private void CollectBloom(BoardModel board, Blast blast)
        {
            int radius = blast.Power > 1 ? blast.Power : _rules.InkBloomRadius;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                AddActive(board, new GridCoord(blast.At.X + dx, blast.At.Y + dy));
        }

        private void CollectBird(BoardModel board, Blast blast, IBoardEventSink sink, IResolutionListener listener)
        {
            int count = blast.Power > 1 ? blast.Power : _rules.BirdTargetCount;
            var targets = PickTargets(board, count);

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (blast.Payload != BoosterType.None)
                {
                    var orientation = blast.Payload == BoosterType.RibbonRocket
                        ? (i % 2 == 0 ? BoosterOrientation.Horizontal : BoosterOrientation.Vertical)
                        : BoosterOrientation.None;
                    _queue.Enqueue(new Blast(target, blast.Payload, orientation, blast.Color));
                }
                else
                {
                    AddActive(board, target);
                    for (int d = 0; d < 4; d++) AddActive(board, target.Step((Direction)d));
                }
            }
        }

        private List<GridCoord> PickTargets(BoardModel board, int count)
        {
            var scored = new List<KeyValuePair<int, GridCoord>>();
            foreach (var coord in board.AllCoords())
            {
                int score = Oracle.ScoreTarget(board, coord);
                if (score == int.MinValue) continue;
                scored.Add(new KeyValuePair<int, GridCoord>(score, coord));
            }

            // Stable sort on (score desc, y, x) keeps bot replays byte-identical across runs.
            scored.Sort((l, r) =>
            {
                int byScore = r.Key.CompareTo(l.Key);
                if (byScore != 0) return byScore;
                int byY = l.Value.Y.CompareTo(r.Value.Y);
                return byY != 0 ? byY : l.Value.X.CompareTo(r.Value.X);
            });

            var result = new List<GridCoord>();
            for (int i = 0; i < scored.Count && result.Count < count; i++) result.Add(scored[i].Value);
            return result;
        }

        private void CollectPrism(BoardModel board, Blast blast, IBoardEventSink sink, IResolutionListener listener)
        {
            // Prism + Prism: erase the page entirely.
            if (blast.Power >= 99)
            {
                foreach (var coord in board.AllCoords()) AddActive(board, coord);
                return;
            }

            var color = blast.Color != TileColor.None ? blast.Color : Oracle.PreferredColor(board);
            if (color == TileColor.None) return;

            var converted = new List<GridCoord>();
            foreach (var coord in board.AllCoords())
            {
                var cell = board.ActiveCell(coord);
                if (cell?.Tile == null || cell.Tile.Color != color || cell.Tile.IsBooster) continue;

                if (blast.Payload != BoosterType.None) converted.Add(coord);
                else AddActive(board, coord);

                // Story-linked twins on the hidden face burn with their partner. This is the reason
                // levels bother marking tiles as linked at all.
                var hidden = board.HiddenCell(coord);
                if (hidden?.Tile != null && hidden.Tile.Color == color && hidden.Tile.StoryLink != 0)
                    AddHidden(board, coord);
            }

            for (int i = 0; i < converted.Count; i++)
            {
                var coord = converted[i];
                var cell = board.ActiveCell(coord);
                if (cell?.Tile == null) continue;

                var orientation = blast.Payload == BoosterType.RibbonRocket
                    ? (i % 2 == 0 ? BoosterOrientation.Horizontal : BoosterOrientation.Vertical)
                    : BoosterOrientation.None;

                cell.Tile.Booster = blast.Payload;
                cell.Tile.Orientation = orientation;
                sink.Publish(new BoosterCreatedEvent(cell.Ref, blast.Payload, orientation, color));
                listener.OnBoosterCreated(cell.Ref, blast.Payload);

                if (TryLift(board, coord, sink, listener, out var chained)) _queue.Enqueue(chained);
            }
        }

        /// <summary>
        /// The Golden Stitch runs a line straight through the paper: every cell it passes is hit on
        /// both faces at once. Combined with another booster it drops that booster along the fold.
        /// </summary>
        private void CollectStitch(BoardModel board, Blast blast, IBoardEventSink sink, IResolutionListener listener)
        {
            var orientation = blast.Orientation == BoosterOrientation.None
                ? BoosterOrientation.Horizontal
                : blast.Orientation;

            var line = new List<GridCoord>();
            if (orientation == BoosterOrientation.Horizontal)
            {
                for (int x = 0; x < board.Width; x++) line.Add(new GridCoord(x, blast.At.Y));
            }
            else
            {
                for (int y = 0; y < board.Height; y++) line.Add(new GridCoord(blast.At.X, y));
            }

            for (int i = 0; i < line.Count; i++)
            {
                AddActive(board, line[i]);
                AddHidden(board, line[i]);
            }

            if (blast.Payload == BoosterType.None) return;

            // Drop the partnered booster at every seam cell the stitch touches.
            int dropped = 0;
            for (int i = 0; i < line.Count && dropped < 4; i++)
            {
                if (!board.Folds.IsSeamCell(line[i])) continue;
                var orientationForPayload = blast.Payload == BoosterType.RibbonRocket
                    ? (dropped % 2 == 0 ? BoosterOrientation.Vertical : BoosterOrientation.Horizontal)
                    : BoosterOrientation.None;
                _queue.Enqueue(new Blast(line[i], blast.Payload, orientationForPayload, blast.Color));
                dropped++;
            }
        }
    }
}
