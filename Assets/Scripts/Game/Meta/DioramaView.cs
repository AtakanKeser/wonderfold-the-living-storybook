using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;
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
    /// <para>Authored production cutouts are loaded through Resources, then given a lightweight idle and
    /// reaction pass. This keeps the scene immediately playable while leaving a clean upgrade path to
    /// prefab or skeletal animation later.</para>
    /// </summary>
    public sealed class DioramaView : MonoBehaviour
    {
        [SerializeField] private float _pullBackDuration = 1.1f;
        [SerializeField] private float _pullBackZoom = 1.9f;

        private readonly Dictionary<string, Transform> _pieces = new Dictionary<string, Transform>();
        private readonly List<DioramaCharacterActor> _characters = new List<DioramaCharacterActor>();
        private Camera _camera;
        private Transform _stage;
        private SpriteRenderer _background;
        private SpriteRenderer _quillStoryWindow;
        private SpriteRenderer _miraPageTab;
        private DioramaCharacterActor _mira;
        private DioramaCharacterActor _quill;
        private DioramaCharacterActor _folio;
        private DioramaCharacterActor _luna;
        private DioramaCharacterActor _blank;
        private float _baseOrthographicSize;
        private int _backdropScreenWidth = -1;
        private int _backdropScreenHeight = -1;

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
                bg.transform.localPosition = new Vector3(0f, 0f, 10f); // pushed back
                var renderer = bg.GetComponent<SpriteRenderer>();
                renderer.sortingOrder = -50;
                renderer.sprite = bgSprite;
                renderer.color = new Color(1f, 1f, 1f, 0.62f);
                view._background = renderer;
            }

            view._quillStoryWindow = CreateStageAnchor(view._stage, "Quill Story Window", 5,
                new Color(0.06f, 0.04f, 0.14f, 0.34f));
            view._miraPageTab = CreateStageAnchor(view._stage, "Mira Page Tab", 9,
                new Color(0.97f, 0.83f, 0.49f, 0.88f));
            
            view._mira = view.SpawnCharacter("Mira", DioramaCharacterActor.Role.Mira, new Vector3(-6.6f, -3.4f, 0f),
                new Color(0.96f, 0.43f, 0.56f), 24, 4.5f);
            view._quill = view.SpawnCharacter("Quill", DioramaCharacterActor.Role.Quill, new Vector3(-5.7f, -3.5f, 0f),
                new Color(0.94f, 0.56f, 0.25f), 26, 4.2f);
            view._folio = view.SpawnCharacter("Professor Folio", DioramaCharacterActor.Role.Folio, new Vector3(6.55f, -3.5f, 0f),
                new Color(0.74f, 0.56f, 0.34f), -12, 2.75f);
            view._luna = view.SpawnCharacter("Luna", DioramaCharacterActor.Role.Luna, new Vector3(5.25f, -3.45f, 0f),
                new Color(0.61f, 0.67f, 1f), -11, 2.75f);
            view._blank = view.SpawnCharacter("The Blank", DioramaCharacterActor.Role.Blank,
                new Vector3(6.0f, 2.7f, 0f), new Color(0.78f, 0.78f, 0.85f), -28, 2.65f);
            view._blank.gameObject.SetActive(false);
            view.FitBackdrop();
            return view;
        }

        /// <summary>Aligns scene dressing with the board's reserved play area after a display change.</summary>
        public void ApplyBoardLayout(BoardLayout layout, BoardModel board = null)
        {
            if (layout == null) return;
            bool landscape = _camera != null && _camera.aspect >= 1.15f;
            var bounds = layout.WorldBounds;
            float cell = layout.CellSize;

            // Mira is attached to a physical page tab rather than hovering at the bottom of the frame.
            // Her silhouette can overlap the board edge a little, but never obscures a broad block of tiles.
            Vector3 miraPosition = new Vector3(bounds.min.x - cell * 0.95f, bounds.min.y + cell * 1.18f, 0f);
            SetAnchor(_miraPageTab, new Vector3(bounds.min.x - cell * 0.74f, bounds.min.y + cell * 0.18f, 0f),
                new Vector3(cell * 1.55f, cell * 0.28f, 1f), true);
            _mira?.SetStageVisible(true);
            _mira?.SetStageAppearance(landscape ? 3.65f : 3.35f, 24);
            _mira?.SetStagePosition(miraPosition);

            // A deliberate void in the page becomes Quill's story window. He rests behind the board's
            // paper rim, then rises above it only while travelling to a match or explosion.
            bool hasStoryWindow = TryFindVoidPocket(board, layout, out Vector3 quillPosition, out Vector2 pocketSize);
            if (!hasStoryWindow)
            {
                quillPosition = new Vector3(bounds.min.x + cell * 0.82f, bounds.max.y + cell * 0.30f, 0f);
                pocketSize = Vector2.one;
            }
            SetAnchor(_quillStoryWindow, quillPosition, new Vector3(
                Mathf.Max(cell * 1.10f, pocketSize.x * cell * 0.90f),
                Mathf.Max(cell * 0.80f, pocketSize.y * cell * 0.72f), 1f), hasStoryWindow);
            _quill?.SetStageVisible(true);
            _quill?.SetStageAppearance(landscape ? 2.95f : 2.70f, hasStoryWindow ? 8 : 24, hasStoryWindow);
            _quill?.SetStagePosition(quillPosition + new Vector3(0f, hasStoryWindow ? -cell * 0.08f : 0f, 0f));

            // Folio and Luna remain part of the restored diorama and dialogue, but keeping them off
            // the moment-to-moment board prevents the gameplay frame from becoming visual clutter.
            _folio?.SetStageVisible(false);
            _luna?.SetStageVisible(false);

            _blank?.SetStageAppearance(landscape ? 3.65f : 3.30f, 18);
            _blank?.SetStagePosition(new Vector3(bounds.max.x - cell * 0.40f, bounds.max.y - cell * 0.72f, 0f));
        }

        private static SpriteRenderer CreateStageAnchor(Transform parent, string name, int sortingOrder, Color colour)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = ProceduralArt.RoundedSquare(0.22f, 0.02f);
            renderer.color = colour;
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = false;
            return renderer;
        }

        private static void SetAnchor(SpriteRenderer anchor, Vector3 position, Vector3 scale, bool visible)
        {
            if (anchor == null) return;
            anchor.transform.localPosition = position;
            anchor.transform.localScale = scale;
            anchor.enabled = visible;
        }

        private static bool TryFindVoidPocket(BoardModel board, BoardLayout layout, out Vector3 centre, out Vector2 size)
        {
            centre = layout.WorldBounds.center;
            size = Vector2.one;
            if (board == null) return false;

            int count = 0;
            int minX = layout.Width;
            int maxX = -1;
            int minY = layout.Height;
            int maxY = -1;
            foreach (var coord in board.AllCoords())
            {
                var cell = board.ActiveCell(coord);
                if (cell == null || !cell.IsVoid) continue;
                count++;
                minX = Mathf.Min(minX, coord.X);
                maxX = Mathf.Max(maxX, coord.X);
                minY = Mathf.Min(minY, coord.Y);
                maxY = Mathf.Max(maxY, coord.Y);
            }

            if (count < 2) return false;
            centre = layout.Origin + new Vector3((minX + maxX) * 0.5f * layout.CellSize,
                (minY + maxY) * 0.5f * layout.CellSize, 0f);
            size = new Vector2(maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        /// <summary>Converts descriptive board events into character performances at the affected cell.</summary>
        public void ReactToBoardEvent(BoardEvent boardEvent, BoardLayout layout)
        {
            if (boardEvent == null || layout == null) return;

            Vector3 focus = layout.WorldBounds.center;
            float intensity;
            switch (boardEvent)
            {
                case TilesClearedEvent cleared:
                    focus = AverageCellPosition(cleared.Cells, layout);
                    intensity = Mathf.Clamp01(0.30f + cleared.Cells.Count * 0.06f +
                        (cleared.Cause == ClearCause.Booster ? 0.22f : 0f));
                    break;
                case BoosterCreatedEvent created:
                    focus = layout.WorldOf(created.At.Coord);
                    intensity = 0.62f;
                    break;
                case BoosterActivatedEvent activated:
                    focus = layout.WorldOf(activated.At.Coord);
                    intensity = 0.90f;
                    break;
                case BoosterComboEvent combo:
                    focus = layout.WorldOf(combo.At.Coord);
                    intensity = 1f;
                    break;
                case BoardFoldedEvent folded:
                    focus = layout.WorldBounds.center;
                    intensity = 0.74f;
                    break;
                default:
                    return;
            }

            _quill?.ReactToGameplay(focus, intensity);
            _mira?.ReactToGameplay(focus, intensity * 0.82f);
            if (_blank != null && _blank.gameObject.activeInHierarchy)
                _blank.ReactToGameplay(focus, intensity * 0.55f);
        }

        private static Vector3 AverageCellPosition(System.Collections.Generic.IReadOnlyList<CellRef> cells,
            BoardLayout layout)
        {
            if (cells == null || cells.Count == 0) return layout.WorldBounds.center;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < cells.Count; i++) sum += layout.WorldOf(cells[i].Coord);
            return sum / cells.Count;
        }

        private void Update()
        {
            if (Screen.width == _backdropScreenWidth && Screen.height == _backdropScreenHeight) return;
            FitBackdrop();
        }

        private void FitBackdrop()
        {
            _backdropScreenWidth = Screen.width;
            _backdropScreenHeight = Screen.height;
            if (_background == null || _background.sprite == null || _camera == null) return;

            var size = _background.sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f) return;
            float cameraHeight = _camera.orthographicSize * 2f;
            float cameraWidth = cameraHeight * _camera.aspect;
            float scale = Mathf.Max(cameraWidth / size.x, cameraHeight / size.y) * 1.05f;
            _background.transform.localScale = Vector3.one * scale;
        }

        /// <summary>Rebuilds the backdrop for a level, showing every piece the player has already mended.</summary>
        public void ShowLevel(LevelDefinition level, PlayerProfile profile)
        {
            foreach (var piece in profile.RestoredPieces) EnsurePiece(piece, true, profile);
            if (!string.IsNullOrEmpty(level.DioramaPieceId)) EnsurePiece(level.DioramaPieceId, false, profile);
            if (_blank != null) _blank.SetStageVisible(level.Id >= 18);
        }

        private void EnsurePiece(string id, bool restored, PlayerProfile profile)
        {
            if (!_pieces.TryGetValue(id, out var piece))
            {
                piece = SpawnPiece(id);
                _pieces[id] = piece;
            }

            piece.GetComponent<DioramaPieceMotion>()?.SetRestored(restored);
            var renderers = piece.GetComponentsInChildren<SpriteRenderer>();
            Color tint = restored
                ? RestoredTint(id, profile)
                : new Color(0.55f, 0.55f, 0.60f, 0.35f);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].color = tint;
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
            var motion = go.AddComponent<DioramaPieceMotion>();
            motion.Initialise(id);

            string authoredId = id.ToLowerInvariant().Contains("ferris") ? "ferris_wheel_piece" 
                              : id.ToLowerInvariant().Contains("carousel") ? "carousel_piece" 
                              : null;
            
            Sprite authoredSprite = null;
            if (authoredId != null)
            {
                // The alpha cutouts layer naturally into the 2.5D stage; the older JPG art remains
                // available during an incremental asset migration.
                authoredSprite = ProceduralArt.LoadAuthoredSprite(authoredId + "_cutout")
                    ?? ProceduralArt.LoadAuthoredSprite(authoredId);
            }
            
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

        /// <summary>Keeps painted art rich while still carrying the chapter's palette into the scene.</summary>
        private static Color RestoredTint(string id, PlayerProfile profile) =>
            Color.Lerp(Color.white, PieceColour(id, profile), 0.18f);

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

        private DioramaCharacterActor SpawnCharacter(string name, DioramaCharacterActor.Role role, Vector3 position,
            Color colour, int sortingOrder, float scale)
        {
            // Prefer the alpha cutout for the stage. The older JPG portraits remain a deliberate
            // fallback so a missing optional art import can never make the runtime scene blank.
            string characterId = CharacterResourceId(name);
            var authored = ProceduralArt.LoadAuthoredSprite(characterId + "_cutout")
                ?? ProceduralArt.LoadAuthoredSprite(characterId);
            var actor = DioramaCharacterActor.Create(_stage, name, role, position, authored, colour, sortingOrder, scale);
            _characters.Add(actor);
            return actor;
        }

        private static string CharacterResourceId(string name)
        {
            string id = name.ToLowerInvariant().Replace("professor ", string.Empty).Replace(' ', '_');
            return id + "_character";
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
            var colour = RestoredTint(level.DioramaPieceId, null);
            var renderers = piece.GetComponentsInChildren<SpriteRenderer>();
            piece.GetComponent<DioramaPieceMotion>()?.SetRestored(true);
            piece.GetComponent<DioramaPieceMotion>()?.ReactToRestoration();
            for (int i = 0; i < _characters.Count; i++)
            {
                if (_characters[i] != null && _characters[i].gameObject.activeInHierarchy)
                    _characters[i].ReactToRestoration();
            }

            float elapsed = 0f;
            while (elapsed < _pullBackDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _pullBackDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);

                _camera.orthographicSize = Mathf.Lerp(_baseOrthographicSize,
                    _baseOrthographicSize * _pullBackZoom, eased);
                FitBackdrop();

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
                FitBackdrop();
                yield return null;
            }

            _camera.orthographicSize = _baseOrthographicSize;
        }
    }
}
