using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Wonderfold.Core.Board;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;
using Wonderfold.Game.Presentation;
using Wonderfold.Game.Services;

namespace Wonderfold.Game.UI
{
    /// <summary>
    /// The level HUD, built in code.
    ///
    /// <para>Built rather than authored as a prefab for the same reason the art is procedural: the
    /// project has to run the moment it is opened, with no missing references. The layout uses anchors
    /// and a <see cref="CanvasScaler"/> in match-height mode, so it survives every phone aspect ratio and
    /// an orientation change without re-authoring.</para>
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        private CanvasScaler _canvasScaler;
        private RectTransform _topBar;
        private Text _levelLabel;
        private Text _movesLabel;
        private Text _livesLabel;
        private Image _foldFill;
        private Text _foldLabel;
        private RectTransform _meterBack;
        private RectTransform _goalRow;
        private RectTransform _foldButtonRow;
        private RectTransform _toolButtonRow;
        private HorizontalLayoutGroup _goalHorizontalLayout;
        private HorizontalLayoutGroup _foldHorizontalLayout;
        private HorizontalLayoutGroup _toolHorizontalLayout;
        private int _layoutScreenWidth = -1;
        private int _layoutScreenHeight = -1;
        private GameObject _endPanel;
        private Text _endTitle;
        private Text _endSubtitle;
        private Button _endPrimary;
        private Text _endPrimaryLabel;

        private readonly List<GoalChip> _goalChips = new List<GoalChip>();
        private readonly List<Button> _foldButtons = new List<Button>();
        private readonly Dictionary<PageTool, Button> _toolButtons = new Dictionary<PageTool, Button>();
        private static Font _font;
        private PlayerProfile _profile;
        private PageTool? _selectedTool;

        private GameObject _dialoguePanel;
        private CanvasGroup _dialogueCanvasGroup;
        private Image _dialoguePortrait;
        private Text _dialogueSpeaker;
        private Text _dialogueLine;
        private Vector2 _dialoguePortraitBasePosition;
        private bool _dialogueAdvance;

        private GameObject _choicePanel;
        private string _choiceValue;

        public event System.Action<int> FoldRequested;
        public event System.Action PrimaryEndActionRequested;
        public event Action<PageTool?> ToolSelected;

        private sealed class GoalChip
        {
            public GameObject Root;
            public Image Swatch;
            public Text Label;
        }

        public static GameHud Create(Transform parent)
        {
            var canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var hud = canvasObject.AddComponent<GameHud>();
            hud._canvasScaler = scaler;
            hud.Build();
            return hud;
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

        private void Build()
        {
            var top = Panel(transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -180f), new Vector2(0f, 0f), new Color(0.09f, 0.08f, 0.16f, 0.85f));
            _topBar = top.rectTransform;

            _levelLabel = Label(top, "Wonderfold", 42, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(0.50f, 1f), new Vector2(40f, 0f), new Vector2(0f, 0f));

            _livesLabel = Label(top, "♥ 5", 30, TextAnchor.MiddleCenter,
                new Vector2(0.48f, 0f), new Vector2(0.72f, 1f), Vector2.zero, Vector2.zero);

            _movesLabel = Label(top, "0", 62, TextAnchor.MiddleRight,
                new Vector2(0.72f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-40f, 0f));

            _goalRow = Row(transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(20f, -320f), new Vector2(-20f, -190f), out _goalHorizontalLayout);

            var meterBack = Panel(transform, new Vector2(0.08f, 0f), new Vector2(0.92f, 0f),
                new Vector2(0f, 150f), new Vector2(0f, 200f), new Color(1f, 1f, 1f, 0.16f));
            _meterBack = meterBack.rectTransform;

            var fillObject = new GameObject("Fill", typeof(Image));
            fillObject.transform.SetParent(meterBack.transform, false);
            _foldFill = fillObject.GetComponent<Image>();
            _foldFill.color = new Color(1f, 0.83f, 0.35f, 0.95f);
            _foldFill.sprite = ProceduralArt.Solid();
            var fillRect = _foldFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            _foldLabel = Label(meterBack, "Fold Meter", 30, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            _foldButtonRow = Row(transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(20f, 40f), new Vector2(-20f, 140f), out _foldHorizontalLayout);

            _toolButtonRow = Row(transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(20f, 145f), new Vector2(-20f, 245f), out _toolHorizontalLayout);

            BuildEndPanel();
            BuildDialoguePanel();
            BuildChoicePanel();
            ApplyResponsiveLayout(true);
        }

        private void Update()
        {
            if (Screen.width == _layoutScreenWidth && Screen.height == _layoutScreenHeight) return;
            ApplyResponsiveLayout(false);
        }

        /// <summary>
        /// Portrait keeps the familiar top / board / bottom-mobile rhythm. Landscape turns the spare
        /// width into a right-hand information rail, so buttons never float over the board.
        /// </summary>
        private void ApplyResponsiveLayout(bool force)
        {
            if (_topBar == null || (!force && Screen.width == _layoutScreenWidth && Screen.height == _layoutScreenHeight))
                return;

            _layoutScreenWidth = Screen.width;
            _layoutScreenHeight = Screen.height;
            bool landscape = Screen.width > Screen.height * 1.15f;

            if (_canvasScaler != null)
            {
                _canvasScaler.referenceResolution = landscape
                    ? new Vector2(1920f, 1080f)
                    : new Vector2(1080f, 1920f);
                _canvasScaler.matchWidthOrHeight = 0.5f;
            }

            if (landscape) ApplyLandscapeLayout();
            else ApplyPortraitLayout();
            ApplyFlowLayout(landscape);
        }

        private void ApplyPortraitLayout()
        {
            SetRect(_topBar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -180f), Vector2.zero);
            SetTextRect(_levelLabel, new Vector2(0f, 0f), new Vector2(0.50f, 1f), new Vector2(40f, 0f),
                Vector2.zero, 42, TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow);
            SetTextRect(_livesLabel, new Vector2(0.48f, 0f), new Vector2(0.72f, 1f), Vector2.zero,
                Vector2.zero, 30, TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow);
            SetTextRect(_movesLabel, new Vector2(0.72f, 0f), new Vector2(1f, 1f), Vector2.zero,
                new Vector2(-40f, 0f), 62, TextAnchor.MiddleRight, HorizontalWrapMode.Overflow);

            SetRect(_goalRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -320f),
                new Vector2(-20f, -190f));
            SetRect(_meterBack, new Vector2(0.08f, 0f), new Vector2(0.92f, 0f), new Vector2(0f, 150f),
                new Vector2(0f, 200f));
            SetRect(_foldButtonRow, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 40f),
                new Vector2(-20f, 140f));
            SetRect(_toolButtonRow, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 145f),
                new Vector2(-20f, 245f));
            _foldLabel.fontSize = 30;
        }

        private void ApplyLandscapeLayout()
        {
            // The board owns the left two thirds of a landscape display. Everything actionable is
            // intentionally contained in this right rail.
            SetRect(_topBar, new Vector2(0.67f, 0.79f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero);
            SetTextRect(_levelLabel, new Vector2(0.06f, 0.53f), new Vector2(0.94f, 0.97f), Vector2.zero,
                Vector2.zero, 26, TextAnchor.MiddleCenter, HorizontalWrapMode.Wrap);
            SetTextRect(_livesLabel, new Vector2(0.06f, 0.12f), new Vector2(0.53f, 0.52f), Vector2.zero,
                Vector2.zero, 23, TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow);
            SetTextRect(_movesLabel, new Vector2(0.54f, 0.08f), new Vector2(0.94f, 0.54f), Vector2.zero,
                Vector2.zero, 46, TextAnchor.MiddleRight, HorizontalWrapMode.Overflow);

            SetRect(_goalRow, new Vector2(0.67f, 0.55f), new Vector2(0.98f, 0.77f), Vector2.zero, Vector2.zero);
            SetRect(_meterBack, new Vector2(0.67f, 0.50f), new Vector2(0.98f, 0.535f), Vector2.zero, Vector2.zero);
            SetRect(_foldButtonRow, new Vector2(0.67f, 0.32f), new Vector2(0.98f, 0.48f), Vector2.zero, Vector2.zero);
            SetRect(_toolButtonRow, new Vector2(0.67f, 0.13f), new Vector2(0.98f, 0.29f), Vector2.zero, Vector2.zero);
            _foldLabel.fontSize = 21;
        }

        private void ApplyFlowLayout(bool landscape)
        {
            _goalHorizontalLayout.enabled = true;
            _foldHorizontalLayout.enabled = true;
            _toolHorizontalLayout.enabled = true;
            _goalHorizontalLayout.spacing = landscape ? 10f : 18f;
            _foldHorizontalLayout.spacing = landscape ? 10f : 18f;
            _toolHorizontalLayout.spacing = landscape ? 10f : 18f;

            float goalWidth = landscape ? CompactRowWidth(_goalChips.Count, 170f) : 120f;
            float foldWidth = landscape ? CompactRowWidth(_foldButtons.Count, 260f) : 260f;
            float toolWidth = landscape ? CompactRowWidth(_toolButtons.Count, 255f) : 255f;
            float goalHeight = landscape ? 76f : 120f;
            float foldHeight = landscape ? 66f : 96f;
            float toolHeight = landscape ? 68f : 92f;

            for (int i = 0; i < _goalChips.Count; i++)
                SetLayoutSize(_goalChips[i].Root, goalWidth, goalHeight);
            for (int i = 0; i < _foldButtons.Count; i++)
                SetLayoutSize(_foldButtons[i].gameObject, foldWidth, foldHeight);
            foreach (var pair in _toolButtons)
                SetLayoutSize(pair.Value.gameObject, toolWidth, toolHeight);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_goalRow);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_foldButtonRow);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_toolButtonRow);
        }

        private static float CompactRowWidth(int count, float maximum)
        {
            if (count <= 0) return maximum;
            return Mathf.Min(maximum, Mathf.Max(88f, 560f / count - 8f));
        }

        private static void SetLayoutSize(GameObject go, float width, float height)
        {
            var layout = go.GetComponent<LayoutElement>();
            if (layout == null) return;
            layout.preferredWidth = width;
            layout.preferredHeight = height;
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SetTextRect(Text text, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax,
            int size, TextAnchor alignment, HorizontalWrapMode wrap)
        {
            SetRect(text.rectTransform, min, max, offsetMin, offsetMax);
            text.fontSize = size;
            text.alignment = alignment;
            text.horizontalOverflow = wrap;
        }

        private void BuildEndPanel()
        {
            _endPanel = Panel(transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.04f, 0.10f, 0.88f)).gameObject;

            var card = Panel(_endPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-380f, -260f), new Vector2(380f, 260f), new Color(0.14f, 0.13f, 0.22f, 0.98f));

            _endTitle = Label(card, "", 64, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.6f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            _endSubtitle = Label(card, "", 34, TextAnchor.UpperCenter,
                new Vector2(0f, 0.28f), new Vector2(1f, 0.62f), new Vector2(30f, 0f), new Vector2(-30f, 0f));

            var buttonObject = new GameObject("Primary", typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(card.transform, false);
            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.sprite = ProceduralArt.Solid();
            buttonImage.color = new Color(1f, 0.83f, 0.35f, 1f);

            var buttonRect = buttonImage.rectTransform;
            buttonRect.anchorMin = new Vector2(0.2f, 0.08f);
            buttonRect.anchorMax = new Vector2(0.8f, 0.24f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            _endPrimary = buttonObject.GetComponent<Button>();
            _endPrimary.onClick.AddListener(() => PrimaryEndActionRequested?.Invoke());

            _endPrimaryLabel = Label(buttonImage, "Continue", 38, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _endPrimaryLabel.color = new Color(0.1f, 0.08f, 0.05f);

            _endPanel.SetActive(false);
        }

        // ------------------------------------------------------------------ updates

        public void BindLevel(LevelDefinition level, LevelSession session)
        {
            _levelLabel.text = $"{level.Id}  {level.Name}";
            RebuildGoalChips(session);
            RebuildFoldButtons(session);
            RebuildToolButtons();
            ApplyResponsiveLayout(true);
            _endPanel.SetActive(false);
        }

        /// <summary>The HUD only displays the profile; inventory authority remains with LevelRunner.</summary>
        public void BindProfile(PlayerProfile profile)
        {
            _profile = profile;
            RefreshProfile();
            RebuildToolButtons();
            ApplyResponsiveLayout(true);
        }

        public void Refresh(LevelSession session)
        {
            _movesLabel.text = session.MovesRemaining.ToString();

            float normalised = session.FoldMeter.Normalised;
            _foldFill.rectTransform.anchorMax = new Vector2(normalised, 1f);
            _foldLabel.text = session.FoldMeter.IsFull ? "FOLD READY" : "Fold Meter";

            for (int i = 0; i < _goalChips.Count && i < session.Goals.Count; i++)
            {
                var goal = session.Goals[i];
                _goalChips[i].Label.text = goal.IsComplete
                    ? "✔"
                    : $"{Mathf.Max(0, goal.Target - goal.Current)}";
                _goalChips[i].Swatch.color = goal.IsComplete
                    ? new Color(0.36f, 0.78f, 0.45f)
                    : GoalColour(goal);
            }

            var available = session.AvailableFoldRegionIds();
            for (int i = 0; i < _foldButtons.Count; i++)
            {
                int regionId = session.Board.Folds.Regions[i].Id;
                _foldButtons[i].interactable = available.Contains(regionId);
            }

            RefreshProfile();
        }

        public void RefreshProfile()
        {
            if (_profile == null || _livesLabel == null) return;
            _profile.RefreshLives(DateTime.UtcNow.Ticks);
            _livesLabel.text = $"♥ {_profile.Lives}/{PlayerProfile.MaxLives}";
            RefreshToolButtons();
        }

        private static Color GoalColour(Wonderfold.Core.Goals.ILevelGoal goal)
        {
            if (goal is Wonderfold.Core.Goals.CollectColorGoal collect)
                return ProceduralArt.ColorOf(collect.Color);
            if (goal is Wonderfold.Core.Goals.ClearObstacleGoal obstacle)
                return ProceduralArt.ColorOf(obstacle.ObstacleType);
            return new Color(0.85f, 0.80f, 0.65f);
        }

        private void RebuildGoalChips(LevelSession session)
        {
            for (int i = 0; i < _goalChips.Count; i++) Destroy(_goalChips[i].Root);
            _goalChips.Clear();

            for (int i = 0; i < session.Goals.Count; i++)
            {
                var chipObject = new GameObject($"Goal {i}", typeof(Image));
                chipObject.transform.SetParent(_goalRow, false);

                var swatch = chipObject.GetComponent<Image>();
                swatch.sprite = ProceduralArt.RoundedSquare();
                swatch.color = GoalColour(session.Goals[i]);

                var label = Label(swatch, "", 34, TextAnchor.MiddleCenter,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                label.color = new Color(0.08f, 0.07f, 0.12f);

                var layout = chipObject.AddComponent<LayoutElement>();
                layout.preferredWidth = 120f;
                layout.preferredHeight = 120f;

                _goalChips.Add(new GoalChip { Root = chipObject, Swatch = swatch, Label = label });
            }
        }

        private void RebuildFoldButtons(LevelSession session)
        {
            for (int i = 0; i < _foldButtons.Count; i++) Destroy(_foldButtons[i].gameObject);
            _foldButtons.Clear();

            var regions = session.Board.Folds.Regions;
            for (int i = 0; i < regions.Count; i++)
            {
                int regionId = regions[i].Id;

                var buttonObject = new GameObject($"Fold {regionId}", typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(_foldButtonRow, false);

                var image = buttonObject.GetComponent<Image>();
                image.sprite = ProceduralArt.RoundedSquare(0.2f, 0.02f);
                image.color = new Color(0.98f, 0.96f, 0.90f, 0.92f);

                var label = Label(image, $"Fold\n{regions[i].Name}", 26, TextAnchor.MiddleCenter,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                label.color = new Color(0.10f, 0.09f, 0.16f);

                var layout = buttonObject.AddComponent<LayoutElement>();
                layout.preferredWidth = 260f;
                layout.preferredHeight = 96f;

                var button = buttonObject.GetComponent<Button>();
                button.onClick.AddListener(() => FoldRequested?.Invoke(regionId));
                _foldButtons.Add(button);
            }
        }

        private void RebuildToolButtons()
        {
            foreach (var pair in _toolButtons) Destroy(pair.Value.gameObject);
            _toolButtons.Clear();
            if (_toolButtonRow == null) return;

            AddToolButton(PageTool.Hammer, "HAMMER", "⚒");
            AddToolButton(PageTool.RibbonRocket, "ROCKET", "➜");
            RefreshToolButtons();
        }

        private void AddToolButton(PageTool tool, string name, string icon)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(_toolButtonRow, false);
            var image = go.GetComponent<Image>();
            image.sprite = ProceduralArt.RoundedSquare(0.2f, 0.02f);
            image.color = tool == PageTool.Hammer
                ? new Color(0.70f, 0.77f, 0.94f, 0.95f)
                : new Color(1f, 0.72f, 0.38f, 0.95f);
            var label = Label(image, $"{icon} {name}", 23, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            label.color = new Color(0.10f, 0.08f, 0.15f);
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = 255f;
            layout.preferredHeight = 92f;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() => SelectTool(tool));
            _toolButtons.Add(tool, button);
        }

        private void RefreshToolButtons()
        {
            foreach (var pair in _toolButtons)
            {
                var label = pair.Value.GetComponentInChildren<Text>();
                int count = pair.Key == PageTool.Hammer ? _profile?.Hammers ?? 0 : _profile?.RibbonRockets ?? 0;
                string icon = pair.Key == PageTool.Hammer ? "⚒" : "➜";
                string name = pair.Key == PageTool.Hammer ? "HAMMER" : "ROCKET";
                if (label != null) label.text = $"{icon} {name}  ×{count}";
                pair.Value.interactable = count > 0;
                var image = pair.Value.GetComponent<Image>();
                if (image != null) image.color = _selectedTool == pair.Key
                    ? new Color(1f, 0.94f, 0.52f, 1f)
                    : pair.Key == PageTool.Hammer
                        ? new Color(0.70f, 0.77f, 0.94f, 0.95f)
                        : new Color(1f, 0.72f, 0.38f, 0.95f);
            }
        }

        private void SelectTool(PageTool tool)
        {
            _selectedTool = _selectedTool == tool ? (PageTool?)null : tool;
            RefreshToolButtons();
            ToolSelected?.Invoke(_selectedTool);
        }

        public void ClearSelectedTool()
        {
            _selectedTool = null;
            RefreshToolButtons();
        }

        public void ShowLevelEnd(LevelOutcome outcome, string subtitle, string buttonLabel)
        {
            _endPanel.SetActive(true);
            _endTitle.text = outcome == LevelOutcome.Won ? "The page mends" : "The Blank wins this page";
            _endTitle.color = outcome == LevelOutcome.Won
                ? new Color(1f, 0.86f, 0.4f)
                : new Color(0.85f, 0.85f, 0.9f);
            _endSubtitle.text = subtitle;
            _endPrimaryLabel.text = buttonLabel;
        }

        public void HideLevelEnd() => _endPanel.SetActive(false);

        // ------------------------------------------------------------------ character dialogue and small story choices

        private void BuildDialoguePanel()
        {
            _dialoguePanel = Panel(transform, new Vector2(0.04f, 0f), new Vector2(0.96f, 0f),
                new Vector2(0f, 270f), new Vector2(0f, 590f), new Color(0.09f, 0.07f, 0.17f, 0.97f)).gameObject;
            _dialogueCanvasGroup = _dialoguePanel.AddComponent<CanvasGroup>();

            var portrait = new GameObject("Portrait", typeof(Image));
            portrait.transform.SetParent(_dialoguePanel.transform, false);
            _dialoguePortrait = portrait.GetComponent<Image>();
            _dialoguePortrait.sprite = ProceduralArt.Disc();
            var portraitRect = _dialoguePortrait.rectTransform;
            portraitRect.anchorMin = new Vector2(0.04f, 0.17f);
            portraitRect.anchorMax = new Vector2(0.23f, 0.83f);
            portraitRect.offsetMin = Vector2.zero;
            portraitRect.offsetMax = Vector2.zero;
            _dialoguePortraitBasePosition = portraitRect.anchoredPosition;

            _dialogueSpeaker = Label(_dialoguePanel.transform, "MIRA", 30, TextAnchor.MiddleLeft,
                new Vector2(0.27f, 0.62f), new Vector2(0.94f, 0.88f), Vector2.zero, Vector2.zero);
            _dialogueSpeaker.color = new Color(1f, 0.84f, 0.38f);
            _dialogueLine = Label(_dialoguePanel.transform, "", 32, TextAnchor.UpperLeft,
                new Vector2(0.27f, 0.20f), new Vector2(0.94f, 0.65f), Vector2.zero, Vector2.zero);
            _dialogueLine.horizontalOverflow = HorizontalWrapMode.Wrap;

            var buttonObject = new GameObject("Continue", typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(_dialoguePanel.transform, false);
            var image = buttonObject.GetComponent<Image>();
            image.sprite = ProceduralArt.RoundedSquare(0.2f, 0.02f);
            image.color = new Color(0.95f, 0.78f, 0.35f, 1f);
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.70f, 0.03f);
            rect.anchorMax = new Vector2(0.94f, 0.17f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var label = Label(image, "CONTINUE", 21, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            label.color = new Color(0.12f, 0.09f, 0.13f);
            buttonObject.GetComponent<Button>().onClick.AddListener(() => _dialogueAdvance = true);
            _dialoguePanel.SetActive(false);
        }

        public IEnumerator PlayDialogue(IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0) yield break;
            _dialoguePanel.SetActive(true);
            _dialogueCanvasGroup.alpha = 0f;
            yield return FadeDialogueIn();
            for (int i = 0; i < lines.Count; i++)
            {
                SplitDialogue(lines[i], out var speaker, out var line);
                _dialogueSpeaker.text = speaker.ToUpperInvariant();
                _dialogueLine.text = line;
                
                // Names in dialogue are human-readable ("Professor Folio", "The Blank"); resource
                // ids are stable snake case. Alpha cutouts take precedence in the UI as well as on the
                // diorama, with the original portrait retained as a robust fallback.
                string charId = speaker.ToLowerInvariant().Replace("professor ", "").Replace(' ', '_') + "_character";
                var authoredSprite = ProceduralArt.LoadAuthoredSprite(charId + "_cutout")
                    ?? ProceduralArt.LoadAuthoredSprite(charId);
                
                if (authoredSprite != null)
                {
                    _dialoguePortrait.sprite = authoredSprite;
                    _dialoguePortrait.color = Color.white;
                }
                else
                {
                    _dialoguePortrait.sprite = ProceduralArt.Disc();
                    _dialoguePortrait.color = CharacterColour(speaker);
                }
                
                yield return PlayPortraitEntrance(speaker);
                _dialogueAdvance = false;
                while (!_dialogueAdvance)
                {
                    AnimateDialoguePortrait(speaker);
                    yield return null;
                }
            }

            _dialoguePanel.SetActive(false);
        }

        private IEnumerator FadeDialogueIn()
        {
            float elapsed = 0f;
            while (elapsed < 0.16f)
            {
                elapsed += Time.unscaledDeltaTime;
                _dialogueCanvasGroup.alpha = Mathf.SmoothStep(0f, 1f, elapsed / 0.16f);
                yield return null;
            }
            _dialogueCanvasGroup.alpha = 1f;
        }

        private IEnumerator PlayPortraitEntrance(string speaker)
        {
            var portrait = _dialoguePortrait.rectTransform;
            portrait.anchoredPosition = _dialoguePortraitBasePosition;
            float elapsed = 0f;
            while (elapsed < 0.18f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / 0.18f);
                portrait.localScale = Vector3.one * Mathf.Lerp(0.78f, 1.08f, t);
                portrait.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(IdleTilt(speaker) * -4f, 0f, t));
                yield return null;
            }
            AnimateDialoguePortrait(speaker);
        }

        private void AnimateDialoguePortrait(string speaker)
        {
            var portrait = _dialoguePortrait.rectTransform;
            float time = Time.unscaledTime;
            float bob = 0f;
            float drift = 0f;
            float scale = 1.05f;
            float tilt = IdleTilt(speaker);

            switch ((speaker ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "QUILL":
                    bob = Mathf.Abs(Mathf.Sin(time * 4.3f)) * 7f;
                    drift = Mathf.Sin(time * 2.1f) * 3f;
                    scale += Mathf.Sin(time * 4.3f) * 0.045f;
                    break;
                case "PROFESSOR FOLIO":
                    bob = Mathf.Sin(time * 1.35f) * 2f;
                    scale += Mathf.Sin(time * 1.35f) * 0.012f;
                    break;
                case "LUNA":
                    bob = Mathf.Sin(time * 1.8f) * 4f;
                    drift = Mathf.Sin(time * 0.9f) * 2f;
                    scale += Mathf.Sin(time * 1.8f) * 0.020f;
                    break;
                case "THE BLANK":
                    bob = Mathf.Sin(time * 1.05f) * 7f;
                    drift = Mathf.Sin(time * 0.62f) * 4f;
                    scale += Mathf.Sin(time * 1.05f) * 0.030f;
                    _dialoguePortrait.color = new Color(1f, 1f, 1f, 0.82f + Mathf.Sin(time * 2.5f) * 0.11f);
                    break;
                default:
                    bob = Mathf.Sin(time * 2.0f) * 3f;
                    scale += Mathf.Sin(time * 2.0f) * 0.018f;
                    break;
            }

            portrait.anchoredPosition = _dialoguePortraitBasePosition + new Vector2(drift, bob);
            portrait.localScale = Vector3.one * scale;
            portrait.localRotation = Quaternion.Euler(0f, 0f, tilt + Mathf.Sin(time * 1.4f) * 0.55f);
        }

        private static float IdleTilt(string speaker)
        {
            switch ((speaker ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "QUILL": return -3.0f;
                case "PROFESSOR FOLIO": return 0.7f;
                case "LUNA": return 1.3f;
                case "THE BLANK": return -1.8f;
                default: return -0.9f;
            }
        }

        private void BuildChoicePanel()
        {
            _choicePanel = Panel(transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.04f, 0.03f, 0.09f, 0.94f)).gameObject;
            var card = Panel(_choicePanel.transform, new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.72f),
                Vector2.zero, Vector2.zero, new Color(0.16f, 0.13f, 0.25f, 1f));
            var title = Label(card, "CHOOSE HOW THE CARNIVAL GLOWS", 38, TextAnchor.MiddleCenter,
                new Vector2(0.06f, 0.68f), new Vector2(0.94f, 0.92f), Vector2.zero, Vector2.zero);
            title.color = new Color(1f, 0.84f, 0.36f);
            AddChoiceButton(card.transform, "MOONLIGHT OBSERVATORY", "observatory", new Vector2(0.08f, 0.16f), new Vector2(0.46f, 0.56f), new Color(0.43f, 0.59f, 0.96f));
            AddChoiceButton(card.transform, "FIREFLY GARDEN", "firefly-garden", new Vector2(0.54f, 0.16f), new Vector2(0.92f, 0.56f), new Color(0.40f, 0.78f, 0.48f));
            _choicePanel.SetActive(false);
        }

        private void AddChoiceButton(Transform parent, string labelText, string value, Vector2 min, Vector2 max, Color colour)
        {
            var go = new GameObject(value, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = ProceduralArt.RoundedSquare(0.18f, 0.02f);
            image.color = colour;
            var rect = image.rectTransform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var label = Label(image, labelText, 27, TextAnchor.MiddleCenter,
                new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.88f), Vector2.zero, Vector2.zero);
            label.color = new Color(0.08f, 0.07f, 0.13f);
            go.GetComponent<Button>().onClick.AddListener(() => _choiceValue = value);
        }

        public IEnumerator ChooseCarnivalStyle(Action<string> chosen)
        {
            _choiceValue = null;
            _choicePanel.SetActive(true);
            while (string.IsNullOrEmpty(_choiceValue)) yield return null;
            _choicePanel.SetActive(false);
            chosen?.Invoke(_choiceValue);
        }

        private static void SplitDialogue(string source, out string speaker, out string line)
        {
            int colon = source == null ? -1 : source.IndexOf(':');
            speaker = colon > 0 ? source.Substring(0, colon).Trim() : "Mira";
            line = colon > 0 ? source.Substring(colon + 1).Trim() : source ?? string.Empty;
        }

        private static Color CharacterColour(string speaker)
        {
            switch ((speaker ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "QUILL": return new Color(0.93f, 0.58f, 0.27f);
                case "PROFESSOR FOLIO": return new Color(0.62f, 0.48f, 0.30f);
                case "LUNA": return new Color(0.61f, 0.67f, 1f);
                case "THE BLANK": return new Color(0.78f, 0.78f, 0.85f);
                default: return new Color(0.96f, 0.46f, 0.58f);
            }
        }

        // ------------------------------------------------------------------ tiny uGUI helpers

        private static Image Panel(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, Color colour)
        {
            var go = new GameObject("Panel", typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = ProceduralArt.Solid();
            image.color = colour;

            var rect = image.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return image;
        }

        private static RectTransform Row(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, out HorizontalLayoutGroup horizontal)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            horizontal = go.GetComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 18f;
            horizontal.childAlignment = TextAnchor.MiddleCenter;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;
            return rect;
        }

        private static Text Label(Component parent, string text, int size, TextAnchor alignment,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject("Label", typeof(Text));
            go.transform.SetParent(parent.transform, false);

            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Font;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = new Color(0.98f, 0.96f, 0.92f);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            var rect = label.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return label;
        }
    }
}
