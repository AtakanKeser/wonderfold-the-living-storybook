using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wonderfold.Core.Level;
using Wonderfold.Game.Presentation;
using Wonderfold.Game.Services;

namespace Wonderfold.Game.Meta
{
    /// <summary>
    /// The pop-up carnival the board is cut from.
    ///
    /// <para>This is the piece that makes Wonderfold's metagame different from a decoration screen. The
    /// puzzle is not a separate activity that pays for furniture: it is a panel of the same diorama, and
    /// when a level is won the camera pulls back far enough to show you that. The restored piece then
    /// stays standing behind every level you play afterwards.</para>
    ///
    /// <para>Pieces are placeholder paper slabs here — the shape of the sequence is real, the art is
    /// not. Replacing <see cref="SpawnPiece"/> with authored prefabs is the whole art integration.</para>
    /// </summary>
    public sealed class DioramaView : MonoBehaviour
    {
        [SerializeField] private float _pullBackDuration = 1.1f;
        [SerializeField] private float _pullBackZoom = 1.9f;

        private readonly Dictionary<string, Transform> _pieces = new Dictionary<string, Transform>();
        private readonly List<Transform> _characters = new List<Transform>();
        private Camera _camera;
        private Transform _stage;
        private float _baseOrthographicSize;

        public static DioramaView Create(Transform parent, Camera camera)
        {
            var go = new GameObject("Diorama");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<DioramaView>();
            view._camera = camera;
            view._baseOrthographicSize = camera.orthographicSize;

            view._stage = new GameObject("Stage").transform;
            view._stage.SetParent(go.transform, false);
            view._stage.localPosition = new Vector3(0f, 0f, 4f);
            
            var bgSprite = ProceduralArt.LoadAuthoredSprite("midnight_carnival_bg");
            if (bgSprite != null)
            {
                var bg = new GameObject("Background", typeof(SpriteRenderer));
                bg.transform.SetParent(view._stage, false);
                bg.transform.localPosition = new Vector3(0f, 2f, 10f); // pushed back
                bg.transform.localScale = new Vector3(8f, 8f, 1f); // Adjust as necessary
                var renderer = bg.GetComponent<SpriteRenderer>();
                renderer.sortingOrder = -50;
                renderer.sprite = bgSprite;
            }
            
            view.SpawnCharacter("Mira", new Vector3(-6.6f, -3.4f, 0f), new Color(0.96f, 0.43f, 0.56f));
            view.SpawnCharacter("Quill", new Vector3(-5.7f, -3.5f, 0f), new Color(0.94f, 0.56f, 0.25f));
            return view;
        }

        /// <summary>Rebuilds the backdrop for a level, showing every piece the player has already mended.</summary>
        public void ShowLevel(LevelDefinition level, PlayerProfile profile)
        {
            foreach (var piece in profile.RestoredPieces) EnsurePiece(piece, true, profile);
            if (!string.IsNullOrEmpty(level.DioramaPieceId)) EnsurePiece(level.DioramaPieceId, false, profile);
        }

        private void EnsurePiece(string id, bool restored, PlayerProfile profile)
        {
            if (!_pieces.TryGetValue(id, out var piece))
            {
                piece = SpawnPiece(id);
                _pieces[id] = piece;
            }

            var renderers = piece.GetComponentsInChildren<SpriteRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].color = restored
                    ? PieceColour(id, profile)
                    : new Color(0.55f, 0.55f, 0.60f, 0.35f);
            }

            piece.localScale = restored ? Vector3.one : new Vector3(1f, 0.15f, 1f);
        }

        /// <summary>
        /// Placeholder geometry: a slab whose position is derived from the piece id, so the layout is
        /// stable across runs without an authored scene.
        /// </summary>
        private Transform SpawnPiece(string id)
        {
            var go = new GameObject($"Piece {id}");
            go.transform.SetParent(_stage, false);

            int hash = 0;
            for (int i = 0; i < id.Length; i++) hash = hash * 31 + id[i];
            var random = new System.Random(hash);

            float x = (float)(random.NextDouble() * 14.0 - 7.0);
            float y = (float)(random.NextDouble() * 3.0 + 2.6);
            float width = (float)(random.NextDouble() * 1.8 + 1.0);
            float height = (float)(random.NextDouble() * 2.4 + 1.2);

            go.transform.localPosition = new Vector3(x, y, 0f);

            string authoredId = id.ToLowerInvariant().Contains("ferris") ? "ferris_wheel_piece" 
                              : id.ToLowerInvariant().Contains("carousel") ? "carousel_piece" 
                              : null;
            
            Sprite authoredSprite = null;
            if (authoredId != null) authoredSprite = ProceduralArt.LoadAuthoredSprite(authoredId);
            
            if (authoredSprite != null)
            {
                var body = AddPaperPart(go.transform, "Authored Piece", Vector3.zero, new Vector3(3f, 3f, 1f), -20);
                body.sprite = authoredSprite;
                return go.transform;
            }

            var procBody = AddPaperPart(go.transform, "Body", Vector3.zero, new Vector3(width, height, 1f), -20);
            procBody.sprite = ProceduralArt.PaperTile();

            // Each restored slab gains a silhouette and a paper flap. That keeps the deterministic
            // authoring-free layout while giving the camera pull-back a recognisable 2.5D storybook read.
            var crest = AddPaperPart(go.transform, "Pop-up Crest", new Vector3(0f, height * 0.48f, -0.02f),
                new Vector3(width * 0.60f, height * 0.55f, 1f), -19);
            crest.sprite = id.Contains("ferris") || id.Contains("carousel") ? ProceduralArt.Ring(0.16f) : ProceduralArt.Disc();
            crest.color = new Color(1f, 1f, 1f, 0.75f);

            var flap = AddPaperPart(go.transform, "Folded Flap", new Vector3(width * 0.30f, -height * 0.30f, -0.03f),
                new Vector3(width * 0.45f, height * 0.45f, 1f), -18);
            flap.sprite = ProceduralArt.FoldedCorner();
            flap.color = new Color(0.16f, 0.09f, 0.20f, 0.36f);
            return go.transform;
        }

        private static Color PieceColour(string id, PlayerProfile profile)
        {
            if (id != null && id.Contains("ferris") && profile != null &&
                profile.StoryChoices.TryGetValue("carnival-ferris-theme", out var choice))
            {
                return choice == "firefly-garden"
                    ? new Color(0.37f, 0.80f, 0.49f)
                    : new Color(0.43f, 0.62f, 0.98f);
            }
            int hash = 0;
            for (int i = 0; i < id.Length; i++) hash = hash * 17 + id[i];
            float hue = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(hue, 0.45f, 0.92f);
        }

        private static SpriteRenderer AddPaperPart(Transform parent, string name, Vector3 position, Vector3 scale,
            int order)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            return renderer;
        }

        private void SpawnCharacter(string name, Vector3 position, Color colour)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_stage, false);
            root.localPosition = position;
            
            var authored = ProceduralArt.LoadAuthoredSprite(name.ToLower() + "_character");
            if (authored != null)
            {
                var body = AddPaperPart(root, "Authored Figure", Vector3.zero, new Vector3(3f, 3f, 1f), -12);
                body.sprite = authored;
                _characters.Add(root);
                return;
            }
            
            var procBody = AddPaperPart(root, "Paper Figure", Vector3.zero, new Vector3(0.65f, 0.95f, 1f), -12);
            procBody.sprite = ProceduralArt.PaperTile();
            procBody.color = colour;
            var head = AddPaperPart(root, "Head", new Vector3(0f, 0.55f, -0.01f), new Vector3(0.52f, 0.52f, 1f), -11);
            head.sprite = ProceduralArt.Disc();
            head.color = new Color(1f, 0.86f, 0.72f);
            _characters.Add(root);
        }

        private void Update()
        {
            for (int i = 0; i < _characters.Count; i++)
            {
                var character = _characters[i];
                if (character == null) continue;
                character.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 1.8f + i) * 3f);
                character.localPosition = new Vector3(character.localPosition.x,
                    -3.45f + i * -0.08f + Mathf.Sin(Time.time * 2.3f + i) * 0.05f, character.localPosition.z);
            }
        }

        /// <summary>
        /// The signature beat. The camera pulls away from the board, the restored piece unfolds into the
        /// scene, and the player sees that the puzzle they were solving was a panel of it all along.
        /// </summary>
        public IEnumerator PlayRestoration(LevelDefinition level, BoardView boardView)
        {
            if (string.IsNullOrEmpty(level.DioramaPieceId)) yield break;

            if (!_pieces.TryGetValue(level.DioramaPieceId, out var piece))
            {
                piece = SpawnPiece(level.DioramaPieceId);
                _pieces[level.DioramaPieceId] = piece;
            }

            var startScale = piece.localScale;
            var endScale = Vector3.one;
            var colour = PieceColour(level.DioramaPieceId, null);
            var renderers = piece.GetComponentsInChildren<SpriteRenderer>();

            float elapsed = 0f;
            while (elapsed < _pullBackDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _pullBackDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);

                _camera.orthographicSize = Mathf.Lerp(_baseOrthographicSize,
                    _baseOrthographicSize * _pullBackZoom, eased);

                // The paper unfolds upward — squash and stretch rather than a plain scale.
                piece.localScale = new Vector3(
                    Mathf.Lerp(startScale.x, endScale.x, eased),
                    Mathf.Lerp(startScale.y, endScale.y, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.4f))),
                    1f);

                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].color = Color.Lerp(new Color(0.55f, 0.55f, 0.60f, 0.35f), colour, eased);
                }

                yield return null;
            }

            yield return new WaitForSeconds(0.45f);

            elapsed = 0f;
            while (elapsed < 0.5f)
            {
                elapsed += Time.deltaTime;
                _camera.orthographicSize = Mathf.Lerp(_baseOrthographicSize * _pullBackZoom,
                    _baseOrthographicSize, Mathf.SmoothStep(0f, 1f, elapsed / 0.5f));
                yield return null;
            }

            _camera.orthographicSize = _baseOrthographicSize;
        }
    }
}
