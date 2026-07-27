using UnityEngine;
using Wonderfold.Core.Board;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Game.Presentation
{
    /// <summary>
    /// Turns touches and mouse drags into <see cref="PlayerMove"/>s.
    ///
    /// <para>Two gestures, both of which the audience already knows: drag a tile onto a neighbour, or tap
    /// a booster to set it off. A drag is committed as soon as it crosses half a cell so the board feels
    /// responsive rather than waiting for the finger to lift.</para>
    /// </summary>
    public sealed class BoardInput : MonoBehaviour
    {
        [SerializeField] private float _dragThreshold = 0.45f;

        private Camera _camera;
        private BoardLayout _layout;
        private BoardModel _board;

        private bool _dragging;
        private GridCoord _origin;
        private Vector3 _originWorld;
        private PageTool? _selectedTool;

        public bool Enabled { get; set; } = true;

        /// <summary>Raised with a move the player intends. Validation stays in the core.</summary>
        public event System.Action<PlayerMove> MoveRequested;

        public void Bind(Camera camera, BoardLayout layout, BoardModel board)
        {
            _camera = camera;
            _layout = layout;
            _board = board;
        }

        /// <summary>A selected pouch tool turns the next board tap into a replayable core move.</summary>
        public void SelectTool(PageTool? tool)
        {
            _selectedTool = tool;
            _dragging = false;
        }

        private void Update()
        {
            if (!Enabled || _camera == null || _layout == null) return;

            if (Input.GetMouseButtonDown(0)) BeginDrag(PointerWorld());
            else if (Input.GetMouseButton(0) && _dragging) ContinueDrag(PointerWorld());
            else if (Input.GetMouseButtonUp(0)) EndDrag(PointerWorld());
        }

        private Vector3 PointerWorld()
        {
            var screen = Input.mousePosition;
            screen.z = Mathf.Abs(_camera.transform.position.z);
            return _camera.ScreenToWorldPoint(screen);
        }

        private void BeginDrag(Vector3 world)
        {
            var coord = _layout.CoordOf(world);
            if (!_layout.Contains(coord)) return;

            if (_selectedTool.HasValue)
            {
                var tool = _selectedTool.Value;
                _selectedTool = null;
                MoveRequested?.Invoke(PlayerMove.UseTool(tool, coord));
                return;
            }

            var cell = _board.ActiveCell(coord);
            if (cell == null || cell.Tile == null) return;

            _dragging = true;
            _origin = coord;
            _originWorld = world;
        }

        private void ContinueDrag(Vector3 world)
        {
            var delta = world - _originWorld;
            if (delta.magnitude < _layout.CellSize * _dragThreshold) return;

            // Snap to whichever axis the finger committed to; diagonals are never legal moves.
            var direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                ? (delta.x > 0 ? Direction.Right : Direction.Left)
                : (delta.y > 0 ? Direction.Up : Direction.Down);

            var target = _origin.Step(direction);
            _dragging = false;

            if (!_layout.Contains(target)) return;
            MoveRequested?.Invoke(PlayerMove.Swap(_origin, target));
        }

        private void EndDrag(Vector3 world)
        {
            if (!_dragging) return;
            _dragging = false;

            var coord = _layout.CoordOf(world);
            if (coord != _origin || !_layout.Contains(coord)) return;

            var cell = _board.ActiveCell(coord);
            if (cell?.Tile == null || !cell.Tile.IsBooster) return;

            MoveRequested?.Invoke(PlayerMove.ActivateBooster(coord));
        }
    }
}
