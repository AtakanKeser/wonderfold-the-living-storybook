using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Wonderfold.Core.Level;
using Wonderfold.Game.Presentation;
using Wonderfold.Game.Services;

namespace Wonderfold.Game.UI
{
    /// <summary>
    /// The playable chapter map. It intentionally uses the same runtime-only construction as the rest
    /// of the slice: a fresh checkout can show progression without a scene, prefabs, or inspector links.
    /// </summary>
    public sealed class StoryMapView : MonoBehaviour
    {
        private const float ReferenceWidth = 1080f;
        private CanvasScaler _canvasScaler;
        private Text _title;
        private Text _chapter;
        private Text _livesLabel;
        private Text _progressLabel;
        private RectTransform _levelsRoot;
        private readonly List<Button> _buttons = new List<Button>();
        private LevelCatalog _catalog;
        private PlayerProfile _profile;
        private float _nextClockUpdate;
        private GameObject _setupPanel;
        private Text _setupTitle;
        private Text _setupRocketLabel;
        private int _selectedLevelId;
        private bool _packStartingRocket;
        private static Font _font;
        private int _layoutScreenWidth = -1;
        private int _layoutScreenHeight = -1;

        public event Action<int, bool> LevelRequested;

        public static StoryMapView Create(Transform parent)
        {
            var root = new GameObject("Story Map", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(parent, false);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var view = root.AddComponent<StoryMapView>();
            view._canvasScaler = scaler;
            view.Build();
            root.SetActive(false);
            return view;
        }

        public void Open(LevelCatalog catalog, PlayerProfile profile)
        {
            _catalog = catalog;
            _profile = profile;
            profile.RefreshLives(DateTime.UtcNow.Ticks);
            gameObject.SetActive(true);
            ApplyResponsiveLayout(true);
            Rebuild();
        }

        public void Close() => gameObject.SetActive(false);

        private void Update()
        {
            ApplyResponsiveLayout(false);
            if (!gameObject.activeInHierarchy || _profile == null || Time.unscaledTime < _nextClockUpdate) return;
            _nextClockUpdate = Time.unscaledTime + 1f;
            _profile.RefreshLives(DateTime.UtcNow.Ticks);
            RefreshHeader();
            bool canPlay = _profile.Lives > 0;
            for (int i = 0; i < _buttons.Count; i++)
            {
                int id = i + 1;
                _buttons[i].interactable = canPlay && _profile.IsUnlocked(id);
            }
        }

        private void Build()
        {
            Panel(transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.045f, 0.035f, 0.10f, 0.985f));

            _title = Label(transform, "THE WONDERFOLD ARCHIVE", 52, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.97f), Vector2.zero, Vector2.zero);
            _title.color = new Color(1f, 0.84f, 0.38f);

            _chapter = Label(transform, "BOOK I  ·  THE MIDNIGHT CARNIVAL", 30, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.87f), Vector2.zero, Vector2.zero);
            _chapter.color = new Color(0.77f, 0.82f, 0.98f);

            _livesLabel = Label(transform, "", 32, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.73f), new Vector2(0.95f, 0.80f), Vector2.zero, Vector2.zero);
            _progressLabel = Label(transform, "", 26, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.74f), Vector2.zero, Vector2.zero);
            _progressLabel.color = new Color(0.78f, 0.75f, 0.88f);

            BuildSetupPanel();

            var root = new GameObject("Level Pages", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            _levelsRoot = root.GetComponent<RectTransform>();
            _levelsRoot.anchorMin = new Vector2(0.04f, 0.06f);
            _levelsRoot.anchorMax = new Vector2(0.96f, 0.67f);
            _levelsRoot.offsetMin = Vector2.zero;
            _levelsRoot.offsetMax = Vector2.zero;
            ApplyResponsiveLayout(true);
        }

        private void ApplyResponsiveLayout(bool force)
        {
            if (_levelsRoot == null || (!force && Screen.width == _layoutScreenWidth && Screen.height == _layoutScreenHeight))
                return;

            _layoutScreenWidth = Screen.width;
            _layoutScreenHeight = Screen.height;
            bool landscape = Screen.width > Screen.height * 1.15f;
            if (_canvasScaler != null)
            {
                _canvasScaler.referenceResolution = landscape
                    ? new Vector2(1920f, 1080f)
                    : new Vector2(ReferenceWidth, 1920f);
                _canvasScaler.matchWidthOrHeight = 0.5f;
            }

            if (!landscape)
            {
                SetRect(_title.rectTransform, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.97f));
                SetRect(_chapter.rectTransform, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.87f));
                SetRect(_livesLabel.rectTransform, new Vector2(0.05f, 0.73f), new Vector2(0.95f, 0.80f));
                SetRect(_progressLabel.rectTransform, new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.74f));
                SetRect(_levelsRoot, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.67f));
                _title.fontSize = 52;
                _chapter.fontSize = 30;
                return;
            }

            // Landscape has room for a broad book-selection spread rather than a tall phone list.
            SetRect(_title.rectTransform, new Vector2(0.08f, 0.86f), new Vector2(0.92f, 0.97f));
            SetRect(_chapter.rectTransform, new Vector2(0.08f, 0.79f), new Vector2(0.92f, 0.86f));
            SetRect(_livesLabel.rectTransform, new Vector2(0.08f, 0.72f), new Vector2(0.48f, 0.79f));
            SetRect(_progressLabel.rectTransform, new Vector2(0.50f, 0.72f), new Vector2(0.92f, 0.79f));
            SetRect(_levelsRoot, new Vector2(0.08f, 0.09f), new Vector2(0.92f, 0.69f));
            _title.fontSize = 48;
            _chapter.fontSize = 26;
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void Rebuild()
        {
            for (int i = _levelsRoot.childCount - 1; i >= 0; i--) Destroy(_levelsRoot.GetChild(i).gameObject);
            _buttons.Clear();

            int count = _catalog == null ? 0 : _catalog.Count;
            for (int i = 0; i < count; i++)
            {
                var level = _catalog.ByIndex(i);
                if (level == null) continue;

                int row = i / 4;
                int column = i % 4;
                var page = new GameObject($"Level {level.Id}", typeof(Image), typeof(Button));
                page.transform.SetParent(_levelsRoot, false);

                var image = page.GetComponent<Image>();
                image.sprite = ProceduralArt.RoundedSquare(0.16f, 0.02f);
                bool unlocked = _profile.IsUnlocked(level.Id);
                bool completed = _profile.BestMovesRemaining.ContainsKey(level.Id);
                image.color = !unlocked ? new Color(0.20f, 0.19f, 0.29f, 0.8f)
                    : completed ? new Color(0.28f, 0.60f, 0.52f, 1f)
                    : new Color(0.95f, 0.78f, 0.38f, 1f);

                var rect = image.rectTransform;
                rect.anchorMin = new Vector2(column * 0.25f + 0.012f, 1f - (row + 1) * 0.2f + 0.015f);
                rect.anchorMax = new Vector2((column + 1) * 0.25f - 0.012f, 1f - row * 0.2f - 0.015f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var label = Label(image.transform, unlocked ? $"{level.Id}\n{ShortName(level.Name)}" : "✦\nLOCKED",
                    22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Vector2(8f, 4f), new Vector2(-8f, -4f));
                label.color = unlocked ? new Color(0.10f, 0.08f, 0.13f) : new Color(0.75f, 0.72f, 0.84f);

                int id = level.Id;
                var button = page.GetComponent<Button>();
                button.interactable = unlocked && _profile.Lives > 0;
                button.onClick.AddListener(() => ShowSetup(id));
                _buttons.Add(button);
            }

            // --- LIVE OPS: WEEKLY LOST PAGE ---
            int weeklyLevelId = (DateTime.UtcNow.DayOfYear / 7) % count + 1;
            var liveOpsBtn = AddButton(_levelsRoot.transform, "LiveOps Button", new Vector2(0.1f, -0.15f), new Vector2(0.9f, -0.02f),
                new Color(0.38f, 0.20f, 0.60f, 0.95f), () => ShowSetup(weeklyLevelId));
            var liveOpsLabel = Label(liveOpsBtn.transform, "✦ WEEKLY LOST PAGE ✦\nPlay this week's restored memory", 
                24, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            liveOpsLabel.color = new Color(1f, 0.85f, 0.40f);
            
            RefreshHeader();
        }

        private void BuildSetupPanel()
        {
            _setupPanel = Panel(transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.03f, 0.02f, 0.08f, 0.88f)).gameObject;
            var card = Panel(_setupPanel.transform, new Vector2(0.10f, 0.29f), new Vector2(0.90f, 0.71f),
                Vector2.zero, Vector2.zero, new Color(0.16f, 0.13f, 0.26f, 1f));
            _setupTitle = Label(card.transform, "", 40, TextAnchor.MiddleCenter,
                new Vector2(0.06f, 0.67f), new Vector2(0.94f, 0.91f), Vector2.zero, Vector2.zero);
            _setupTitle.color = new Color(1f, 0.85f, 0.40f);
            var prompt = Label(card.transform, "Pack one starter booster? It is placed at the heart of the page.", 26,
                TextAnchor.MiddleCenter, new Vector2(0.08f, 0.51f), new Vector2(0.92f, 0.67f), Vector2.zero, Vector2.zero);
            prompt.color = new Color(0.84f, 0.82f, 0.94f);

            var rocket = AddButton(card.transform, "Starter Rocket", new Vector2(0.17f, 0.28f), new Vector2(0.83f, 0.48f),
                new Color(1f, 0.72f, 0.36f), ToggleStartingRocket);
            _setupRocketLabel = Label(rocket.transform, "", 27, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _setupRocketLabel.color = new Color(0.10f, 0.08f, 0.14f);

            AddButton(card.transform, "Enter Page", new Vector2(0.17f, 0.07f), new Vector2(0.83f, 0.22f),
                new Color(0.49f, 0.77f, 0.56f), StartSelectedLevel);
            _setupPanel.SetActive(false);
        }

        private void ShowSetup(int levelId)
        {
            _selectedLevelId = levelId;
            _packStartingRocket = false;
            var level = _catalog.ById(levelId);
            _setupTitle.text = level == null ? "OPEN THIS PAGE" : $"OPEN PAGE {level.Id}: {level.Name.ToUpperInvariant()}";
            RefreshSetup();
            _setupPanel.transform.SetAsLastSibling();
            _setupPanel.SetActive(true);
        }

        private void ToggleStartingRocket()
        {
            if (_profile == null || _profile.RibbonRockets <= 0) return;
            _packStartingRocket = !_packStartingRocket;
            RefreshSetup();
        }

        private void RefreshSetup()
        {
            if (_setupRocketLabel == null || _profile == null) return;
            _setupRocketLabel.text = _packStartingRocket
                ? $"➜ STARTER ROCKET PACKED  ×{_profile.RibbonRockets}"
                : $"➜ PACK RIBBON ROCKET  ×{_profile.RibbonRockets}";
        }

        private void StartSelectedLevel()
        {
            if (_selectedLevelId <= 0) return;
            _setupPanel.SetActive(false);
            LevelRequested?.Invoke(_selectedLevelId, _packStartingRocket);
        }

        private void RefreshHeader()
        {
            if (_profile == null) return;
            int restored = _profile.RestoredPieces.Count;
            _progressLabel.text = $"{restored}/20 paper scenes restored  ·  tools:  ⚒ {_profile.Hammers}   ➜ {_profile.RibbonRockets}";
            if (_profile.Lives >= PlayerProfile.MaxLives)
            {
                _livesLabel.text = $"♥  {_profile.Lives}/{PlayerProfile.MaxLives}  —  every Storykeeper is ready";
                return;
            }

            var span = TimeSpan.FromTicks(_profile.TicksToNextLife(DateTime.UtcNow.Ticks));
            _livesLabel.text = _profile.Lives > 0
                ? $"♥  {_profile.Lives}/{PlayerProfile.MaxLives}  ·  next heart in {span.Minutes:00}:{span.Seconds:00}"
                : $"♥  0/{PlayerProfile.MaxLives}  ·  next heart in {span.Minutes:00}:{span.Seconds:00}";
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "UNTITLED";
            return name.Length <= 17 ? name.ToUpperInvariant() : name.Substring(0, 16).ToUpperInvariant() + "…";
        }

        private static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        private static Image Panel(Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin,
            Vector2 offsetMax, Color colour)
        {
            var go = new GameObject("Panel", typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = ProceduralArt.Solid();
            image.color = colour;
            var rect = image.rectTransform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return image;
        }

        private static Button AddButton(Transform parent, string name, Vector2 min, Vector2 max, Color colour,
            UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = ProceduralArt.RoundedSquare(0.18f, 0.02f);
            image.color = colour;
            var rect = image.rectTransform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(action);
            return button;
        }

        private static Text Label(Transform parent, string text, int size, TextAnchor alignment,
            Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject("Label", typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = new Color(0.96f, 0.94f, 0.90f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            var rect = label.rectTransform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return label;
        }
    }
}
