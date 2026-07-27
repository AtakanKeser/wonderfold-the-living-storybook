using System.Collections;
using UnityEngine;
using Wonderfold.Core.Board;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Game.Presentation
{
    /// <summary>
    /// One tile on screen. Owns nothing about the rules — it is told what to look like and where to be.
    ///
    /// <para>Views are keyed by the model's tile id, which is stable for the life of a piece. That is why
    /// a tile keeps its identity through a fall, a fold and a swap instead of popping as the grid
    /// re-binds underneath it.</para>
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class TileView : MonoBehaviour
    {
        public int TileId { get; private set; }

        private SpriteRenderer _body;
        private SpriteRenderer _badge;
        private SpriteRenderer _corner;
        private Coroutine _motion;

        private void Awake()
        {
            _body = GetComponent<SpriteRenderer>();
            
            var authored = ProceduralArt.LoadAuthoredSprite("base_gem_tile");
            _body.sprite = authored != null ? authored : ProceduralArt.PaperTile();
            _body.sortingOrder = 10;

            var cornerObject = new GameObject("Folded Corner");
            cornerObject.transform.SetParent(transform, false);
            cornerObject.transform.localPosition = new Vector3(0f, 0f, -0.015f);
            _corner = cornerObject.AddComponent<SpriteRenderer>();
            _corner.sprite = ProceduralArt.FoldedCorner();
            _corner.color = new Color(0.15f, 0.10f, 0.18f, 0.32f);
            _corner.sortingOrder = 11;
            
            if (authored != null)
            {
                // Disable corner if we're using a polished gem tile
                _corner.gameObject.SetActive(false);
            }

            var badgeObject = new GameObject("Badge");
            badgeObject.transform.SetParent(transform, false);
            badgeObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            _badge = badgeObject.AddComponent<SpriteRenderer>();
            _badge.sortingOrder = 12;
            _badge.enabled = false;
        }

        public void Bind(TilePiece tile, float cellSize)
        {
            TileId = tile.Id;
            name = $"Tile {tile.Id}";
            transform.localScale = Vector3.one * cellSize;
            Refresh(tile);
        }

        public void Refresh(TilePiece tile)
        {
            if (tile == null) return;

            _body.color = tile.Color == TileColor.None && tile.IsBlank
                ? new Color(0.92f, 0.92f, 0.94f)
                : ProceduralArt.ColorOf(tile.Color);
            _corner.enabled = !tile.IsBlank;

            if (!tile.IsBooster)
            {
                _badge.enabled = false;
                return;
            }

            _badge.enabled = true;
            _badge.color = BadgeColor(tile.Booster);
            _badge.transform.localRotation = Quaternion.identity;
            _badge.transform.localScale = Vector3.one * 0.62f;

            switch (tile.Booster)
            {
                case BoosterType.RibbonRocket:
                    _badge.sprite = ProceduralArt.Chevron();
                    _badge.transform.localRotation = tile.Orientation == BoosterOrientation.Vertical
                        ? Quaternion.Euler(0f, 0f, 90f)
                        : Quaternion.identity;
                    break;
                case BoosterType.GoldenStitch:
                    _badge.sprite = ProceduralArt.Ring(0.16f);
                    _badge.transform.localScale = Vector3.one * 0.9f;
                    break;
                case BoosterType.PrismBookmark:
                    _badge.sprite = ProceduralArt.Ring(0.22f);
                    break;
                default:
                    _badge.sprite = ProceduralArt.Disc();
                    break;
            }
        }

        private static Color BadgeColor(BoosterType type)
        {
            switch (type)
            {
                case BoosterType.GoldenStitch: return new Color(1f, 0.86f, 0.35f);
                case BoosterType.PrismBookmark: return new Color(1f, 1f, 1f, 0.95f);
                case BoosterType.OrigamiBird: return new Color(1f, 0.97f, 0.92f);
                case BoosterType.InkBloom: return new Color(0.12f, 0.11f, 0.18f);
                default: return new Color(1f, 1f, 1f, 0.92f);
            }
        }

        public void SnapTo(Vector3 position)
        {
            StopMotion();
            transform.position = position;
        }

        public Coroutine MoveTo(Vector3 target, float duration)
        {
            StopMotion();
            _motion = StartCoroutine(MoveRoutine(target, duration));
            return _motion;
        }

        private IEnumerator MoveRoutine(Vector3 target, float duration)
        {
            var start = transform.position;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Slight overshoot on landing reads as weight without needing a physics pass.
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                transform.position = Vector3.LerpUnclamped(start, target, eased);
                yield return null;
            }

            transform.position = target;
            _motion = null;
        }

        public IEnumerator PlayPop(float duration = 0.16f)
        {
            var start = transform.localScale;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.localScale = start * (1f + 0.35f * Mathf.Sin(t * Mathf.PI));
                _body.color = new Color(_body.color.r, _body.color.g, _body.color.b, 1f - t);
                yield return null;
            }
        }

        public void SetSelected(bool selected)
        {
            transform.localRotation = selected ? Quaternion.Euler(0f, 0f, 4f) : Quaternion.identity;
        }

        private void StopMotion()
        {
            if (_motion == null) return;
            StopCoroutine(_motion);
            _motion = null;
        }
    }
}
