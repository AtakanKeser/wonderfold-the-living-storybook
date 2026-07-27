using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Game.Presentation
{
    /// <summary>
    /// Turns the core's event stream into something you can watch.
    ///
    /// <para>The view never reads the board to decide what happened — it reads it only to decide what
    /// things should currently look like. What happened arrives as <see cref="IBoardEvent"/>s, in order,
    /// and is played back through a queue so a seven-step cascade animates as seven beats instead of
    /// snapping to the final state.</para>
    ///
    /// <para>Every batch ends with a full reconciliation against the model. Animation is allowed to be
    /// approximate; the board the player sees at rest is not.</para>
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        [Header("Timing")]
        [SerializeField] private float _fallStepDuration = 0.075f;
        [SerializeField] private float _swapDuration = 0.14f;
        [SerializeField] private float _clearDuration = 0.16f;
        [SerializeField] private float _cascadeBeat = 0.06f;
        [SerializeField] private float _foldDuration = 0.5f;

        private readonly Dictionary<int, TileView> _tiles = new Dictionary<int, TileView>();
        private readonly List<CellDecorView> _decor = new List<CellDecorView>();
        private readonly List<TileView> _recycle = new List<TileView>();

        private LevelSession _session;
        private Transform _tileRoot;
        private Transform _decorRoot;
        private FoldAnimator _foldAnimator;

        public BoardLayout Layout { get; private set; }
        public bool IsAnimating { get; private set; }

        /// <summary>Raised when a batch finishes playing, so input and the HUD can unblock together.</summary>
        public event System.Action BatchCompleted;
        /// <summary>Raised as each descriptive core event reaches the presentation queue.</summary>
        public event System.Action<BoardEvent> EventPlayed;

        public void Bind(LevelSession session, BoardLayout layout)
        {
            _session = session;
            Layout = layout;

            if (_tileRoot == null)
            {
                _tileRoot = new GameObject("Tiles").transform;
                _tileRoot.SetParent(transform, false);
                _decorRoot = new GameObject("Decor").transform;
                _decorRoot.SetParent(transform, false);
                _foldAnimator = gameObject.AddComponent<FoldAnimator>();
            }

            BuildDecor();
            SyncFromModel();
        }

        private void BuildDecor()
        {
            for (int i = 0; i < _decor.Count; i++)
            {
                if (_decor[i] != null) Destroy(_decor[i].gameObject);
            }

            _decor.Clear();

            foreach (var coord in _session.Board.AllCoords())
            {
                _decor.Add(CellDecorView.Create(_decorRoot, coord, Layout.WorldOf(coord), Layout.CellSize));
            }
        }

        // ------------------------------------------------------------------ reconciliation

        /// <summary>
        /// Makes the scene match the model exactly. Cheap enough to run after every batch, and it means
        /// a missed animation is a cosmetic hiccup rather than a desynchronised board.
        /// </summary>
        public void SyncFromModel()
        {
            var board = _session.Board;
            var alive = new HashSet<int>();

            foreach (var coord in board.AllCoords())
            {
                var cell = board.ActiveCell(coord);
                if (cell?.Tile == null) continue;

                alive.Add(cell.Tile.Id);
                var view = Acquire(cell.Tile);
                view.Refresh(cell.Tile);
                view.SnapTo(Layout.WorldOf(coord));
            }

            var stale = new List<int>();
            foreach (var pair in _tiles)
            {
                if (!alive.Contains(pair.Key)) stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++) Release(stale[i]);
            for (int i = 0; i < _decor.Count; i++) _decor[i].Sync(board);
        }

        private TileView Acquire(TilePiece tile)
        {
            if (_tiles.TryGetValue(tile.Id, out var existing)) return existing;

            TileView view;
            if (_recycle.Count > 0)
            {
                view = _recycle[_recycle.Count - 1];
                _recycle.RemoveAt(_recycle.Count - 1);
                view.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("Tile");
                go.transform.SetParent(_tileRoot, false);
                view = go.AddComponent<TileView>();
            }

            view.Bind(tile, Layout.CellSize);
            _tiles[tile.Id] = view;
            return view;
        }

        private void Release(int tileId)
        {
            if (!_tiles.TryGetValue(tileId, out var view)) return;
            _tiles.Remove(tileId);
            if (view == null) return;

            view.gameObject.SetActive(false);
            _recycle.Add(view);
        }

        // ------------------------------------------------------------------ playback

        public Coroutine Play(List<BoardEvent> events) => StartCoroutine(PlayRoutine(events));

        private IEnumerator PlayRoutine(List<BoardEvent> events)
        {
            IsAnimating = true;

            for (int i = 0; i < events.Count; i++)
            {
                EventPlayed?.Invoke(events[i]);
                switch (events[i])
                {
                    case SwapPerformedEvent swap:
                        yield return PlaySwap(swap);
                        break;
                    case TilesMovedEvent moved:
                        yield return PlayMoves(moved);
                        break;
                    case TilesSpawnedEvent spawned:
                        yield return PlaySpawns(spawned);
                        break;
                    case TilesClearedEvent cleared:
                        yield return PlayClears(cleared);
                        break;
                    case BoosterCreatedEvent created:
                        PlayBoosterCreated(created);
                        break;
                    case ObstacleDamagedEvent damaged:
                        Decor(damaged.At.Coord)?.Sync(_session.Board);
                        break;
                    case ObstacleClearedEvent obstacleCleared:
                        Decor(obstacleCleared.At.Coord)?.Sync(_session.Board);
                        break;
                    case BoardFoldedEvent folded:
                        yield return PlayFold(folded);
                        break;
                    case CascadeStepEvent _:
                        yield return new WaitForSeconds(_cascadeBeat);
                        break;
                    case BoardShuffledEvent _:
                        SyncFromModel();
                        yield return new WaitForSeconds(0.2f);
                        break;
                    case TilePortalledEvent portalled:
                        PlayPortal(portalled);
                        break;
                }
            }

            SyncFromModel();
            IsAnimating = false;
            BatchCompleted?.Invoke();
        }

        private IEnumerator PlaySwap(SwapPerformedEvent swap)
        {
            var board = _session.Board;
            var a = board.CellAt(swap.A);
            var b = board.CellAt(swap.B);

            // The model has already swapped, so each cell now holds the other's piece.
            if (a?.Tile != null && _tiles.TryGetValue(a.Tile.Id, out var viewA))
                viewA.MoveTo(Layout.WorldOf(swap.A.Coord), _swapDuration);
            if (b?.Tile != null && _tiles.TryGetValue(b.Tile.Id, out var viewB))
                viewB.MoveTo(Layout.WorldOf(swap.B.Coord), _swapDuration);

            yield return new WaitForSeconds(_swapDuration);
        }

        private IEnumerator PlayMoves(TilesMovedEvent moved)
        {
            for (int i = 0; i < moved.Moves.Count; i++)
            {
                var move = moved.Moves[i];
                if (_tiles.TryGetValue(move.TileId, out var view))
                    view.MoveTo(Layout.WorldOf(move.To.Coord), _fallStepDuration);
            }

            yield return new WaitForSeconds(_fallStepDuration);
        }

        private IEnumerator PlaySpawns(TilesSpawnedEvent spawned)
        {
            for (int i = 0; i < spawned.Spawns.Count; i++)
            {
                var spawn = spawned.Spawns[i];
                var cell = _session.Board.CellAt(spawn.At);
                if (cell?.Tile == null) continue;

                var view = Acquire(cell.Tile);
                view.Refresh(cell.Tile);
                view.SnapTo(Layout.WorldOf(spawn.EntryFrom.Coord));
                view.MoveTo(Layout.WorldOf(spawn.At.Coord), _fallStepDuration);
            }

            yield return new WaitForSeconds(_fallStepDuration);
        }

        private IEnumerator PlayClears(TilesClearedEvent cleared)
        {
            var popped = new List<TileView>();
            for (int i = 0; i < cleared.Cells.Count; i++)
            {
                var reference = cleared.Cells[i];
                Decor(reference.Coord)?.Sync(_session.Board);

                var view = FindViewAt(reference);
                if (view == null) continue;
                popped.Add(view);
                view.StartCoroutine(view.PlayPop(_clearDuration));
            }

            yield return new WaitForSeconds(_clearDuration);

            for (int i = 0; i < popped.Count; i++) Release(popped[i].TileId);
        }

        /// <summary>
        /// Cleared tiles are already gone from the model by the time we animate them, so the view has to
        /// be located by position rather than by looking the cell up.
        /// </summary>
        private TileView FindViewAt(CellRef reference)
        {
            var target = Layout.WorldOf(reference.Coord);
            float best = Layout.CellSize * 0.4f;
            TileView found = null;

            foreach (var pair in _tiles)
            {
                var view = pair.Value;
                if (view == null) continue;
                float distance = Vector3.Distance(view.transform.position, target);
                if (distance >= best) continue;
                best = distance;
                found = view;
            }

            return found;
        }

        private void PlayBoosterCreated(BoosterCreatedEvent created)
        {
            var cell = _session.Board.CellAt(created.At);
            if (cell?.Tile == null) return;

            var view = Acquire(cell.Tile);
            view.Refresh(cell.Tile);
            view.SnapTo(Layout.WorldOf(created.At.Coord));
        }

        private void PlayPortal(TilePortalledEvent portalled)
        {
            if (!_tiles.TryGetValue(portalled.TileId, out var view) || view == null) return;

            // The destination is usually on the face you cannot see, so the piece exits rather than travels.
            Release(portalled.TileId);
        }

        private IEnumerator PlayFold(BoardFoldedEvent folded)
        {
            var region = _session.Board.Folds.RegionById(folded.RegionId);
            if (region == null) yield break;

            var plan = SurfaceMapper.PlanFor(region, folded.NewSide);
            var affected = new List<Transform>();

            foreach (var coord in region.Area.Coords())
            {
                if (!Layout.Contains(coord)) continue;
                var decor = Decor(coord);
                if (decor != null) affected.Add(decor.transform);

                var cell = _session.Board.CellAt(coord, folded.NewSide == SurfaceSide.Front
                    ? SurfaceSide.Back
                    : SurfaceSide.Front);
                if (cell?.Tile != null && _tiles.TryGetValue(cell.Tile.Id, out var view) && view != null)
                    affected.Add(view.transform);
            }

            yield return _foldAnimator.Play(plan, affected, Layout, _foldDuration, SyncFromModel);
        }

        private CellDecorView Decor(GridCoord coord)
        {
            for (int i = 0; i < _decor.Count; i++)
            {
                if (_decor[i].Coord == coord) return _decor[i];
            }

            return null;
        }
    }
}
