using UnityEngine;
using Wonderfold.Core.Board;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Game.Presentation
{
    /// <summary>
    /// The paper under one coordinate: the cell slot, whatever obstacle is covering it, and the tint
    /// that tells the player which face of the page they are looking at.
    /// </summary>
    public sealed class CellDecorView : MonoBehaviour
    {
        private SpriteRenderer _slot;
        private SpriteRenderer _obstacle;
        private SpriteRenderer _damage;
        private SpriteRenderer _illustration;

        public GridCoord Coord { get; private set; }

        private static readonly Color FrontPaper = new Color(0.98f, 0.96f, 0.90f, 0.45f);
        private static readonly Color BackPaper = new Color(0.72f, 0.78f, 0.92f, 0.55f);

        public static CellDecorView Create(Transform parent, GridCoord coord, Vector3 position, float cellSize)
        {
            var root = new GameObject($"Cell {coord}");
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var view = root.AddComponent<CellDecorView>();
            view.Coord = coord;
            view.Build(cellSize);
            return view;
        }

        private void Build(float cellSize)
        {
            _slot = AddRenderer("Slot", 0, cellSize * 0.94f);
            _slot.sprite = ProceduralArt.PaperTile();

            _illustration = AddRenderer("Illustration", 1, cellSize * 0.80f);
            _illustration.sprite = ProceduralArt.Disc();
            _illustration.enabled = false;

            _obstacle = AddRenderer("Obstacle", 20, cellSize * 0.98f);
            _obstacle.sprite = ProceduralArt.RoundedSquare(0.18f, 0.02f);
            _obstacle.enabled = false;

            _damage = AddRenderer("Damage", 21, cellSize * 0.55f);
            _damage.sprite = ProceduralArt.Ring(0.24f);
            _damage.enabled = false;
        }

        private SpriteRenderer AddRenderer(string label, int order, float scale)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            child.transform.localScale = Vector3.one * scale;
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            return renderer;
        }

        /// <summary>Pulls everything about this coordinate straight out of the model.</summary>
        public void Sync(BoardModel board)
        {
            var side = board.ActiveSide(Coord);
            var cell = board.ActiveCell(Coord);

            if (cell == null || cell.IsVoid)
            {
                _slot.enabled = false;
                _obstacle.enabled = false;
                _damage.enabled = false;
                _illustration.enabled = false;
                return;
            }

            _slot.enabled = true;
            _slot.color = side == SurfaceSide.Front ? FrontPaper : BackPaper;

            _illustration.enabled = cell.IsIllustration;
            if (cell.IsIllustration)
            {
                // The picture brightens as the thing covering it comes away.
                _illustration.color = cell.HasObstacle
                    ? new Color(1f, 0.85f, 0.55f, 0.12f)
                    : new Color(1f, 0.85f, 0.55f, 0.65f);
            }

            if (cell.Obstacle == null)
            {
                _obstacle.enabled = false;
                _damage.enabled = false;
                return;
            }

            _obstacle.enabled = true;
            var colour = ProceduralArt.ColorOf(cell.Obstacle.Type);

            if (cell.Obstacle.IsOverlay)
            {
                // Overlays sit on top of the tile, so they must not hide it completely.
                _obstacle.color = new Color(colour.r, colour.g, colour.b, 0.55f);
                _obstacle.sortingOrder = 20;
            }
            else
            {
                _obstacle.color = colour;
                _obstacle.sortingOrder = 20;
            }

            bool chipped = cell.Obstacle.Health < cell.Obstacle.MaxHealth;
            _damage.enabled = chipped;
            if (chipped) _damage.color = new Color(0f, 0f, 0f, 0.35f);
        }
    }
}
