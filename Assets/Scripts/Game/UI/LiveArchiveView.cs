using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Wonderfold.Core.Live;
using Wonderfold.Game.Presentation;
using Wonderfold.Game.Services;

namespace Wonderfold.Game.UI
{
    /// <summary>
    /// The Living Archive: today's shared page, the endless climb, the errands, the streak, and the
    /// Story Thread box.
    ///
    /// <para>Built at runtime like the rest of the interface, for the same reason: a fresh checkout has to
    /// be able to show all of this without a scene file or a prefab reference that can come unset.</para>
    /// </summary>
    public sealed class LiveArchiveView : MonoBehaviour
    {
        private const int MaxQuestRows = 6;

        private sealed class QuestRow
        {
            public GameObject Root;
            public Text Title;
            public Image BarFill;
            public Button Claim;
            public Text ClaimLabel;
            public Quest Quest;
        }

        private CanvasScaler _scaler;
        private LiveOpsService _live;
        private PlayerProfile _profile;

        private Text _title;
        private Text _status;
        private Text _streakLine;
        private Button _mendButton;
        private Text _mendLabel;

        private Image _dailyCard;
        private Text _dailyTitle;
        private Text _dailyDetail;
        private Button _dailyPlay;
        private Text _dailyPlayLabel;
        private Button _dailyShare;
        private Text _dailyShareLabel;
        private Image _dailyProgressFill;

        private Image _endlessCard;
        private Text _endlessTitle;
        private Text _endlessDetail;
        private Button _endlessPlay;
        private Text _endlessPlayLabel;
        private Image _endlessProgressFill;

        private Text _errandsHeader;
        private readonly List<QuestRow> _rows = new List<QuestRow>();

        private InputField _threadField;
        private Button _threadButton;
        private Text _threadResult;
        private Button _closeButton;

        private int _layoutWidth = -1;
        private int _layoutHeight = -1;
        private float _nextClock;
        private static Font _font;

        /// <summary>Raised with a page key — <c>daily-207</c> or <c>endless-42</c> — the player wants to play.</summary>
        public event Action<string> PageRequested;
        public event Action Closed;

        public static LiveArchiveView Create(Transform parent)
        {
            var root = new GameObject("Living Archive", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(parent, false);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 260;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var view = root.AddComponent<LiveArchiveView>();
            view._scaler = scaler;
            view.Build();
            root.SetActive(false);
            return view;
        }

        public void Open(LiveOpsService live, PlayerProfile profile)
        {
            _live = live;
            _profile = profile;
            _live.RollOver(DateTime.UtcNow);
            gameObject.SetActive(true);
            ApplyResponsiveLayout(true);
            Refresh();
            WarmToday();
        }

        /// <summary>Leaves the archive by the front door — the caller is expected to show the map again.</summary>
        public void Close()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>Steps aside without announcing it, for when the archive is handing over to a page.</summary>
        public void Hide() => gameObject.SetActive(false);

        private void Update()
        {
            ApplyResponsiveLayout(false);
            if (_live == null || Time.unscaledTime < _nextClock) return;
            _nextClock = Time.unscaledTime + 1f;
            RefreshStatus();
        }

        // ------------------------------------------------------------------ behaviour

        /// <summary>
        /// Starts weaving today's page the moment the hub opens, so the audition is usually finished
        /// before the player decides to press Play.
        /// </summary>
        private void WarmToday()
        {
            int day = _live.Today;
            if (!_live.IsReady(DailyFold.Key(day)))
            {
                _live.RequestDaily(day, _ => RefreshDaily(), p => SetProgress(_dailyProgressFill, p));
            }

            int depth = _profile.EndlessDepth;
            if (!_live.IsReady(EndlessArchive.Key(depth)))
            {
                _live.RequestEndless(depth, _ => RefreshEndless(), p => SetProgress(_endlessProgressFill, p));
            }
        }

        private void PlayDaily()
        {
            int day = _live.Today;
            if (!_live.IsReady(DailyFold.Key(day)))
            {
                _dailyPlayLabel.text = "WEAVING…";
                _live.RequestDaily(day, _ => PageRequested?.Invoke(DailyFold.Key(day)),
                    p => SetProgress(_dailyProgressFill, p));
                return;
            }

            PageRequested?.Invoke(DailyFold.Key(day));
        }

        private void PlayEndless()
        {
            int depth = _profile.EndlessDepth;
            if (!_live.IsReady(EndlessArchive.Key(depth)))
            {
                _endlessPlayLabel.text = "WEAVING…";
                _live.RequestEndless(depth, _ => PageRequested?.Invoke(EndlessArchive.Key(depth)),
                    p => SetProgress(_endlessProgressFill, p));
                return;
            }

            PageRequested?.Invoke(EndlessArchive.Key(depth));
        }

        /// <summary>Copies the brag and the thread to the clipboard — the whole share flow, no network.</summary>
        private void ShareDaily()
        {
            int day = _live.Today;
            if (!_profile.DailyThreads.TryGetValue(day, out string thread) || string.IsNullOrEmpty(thread))
            {
                _dailyShareLabel.text = "FINISH IT FIRST";
                return;
            }

            var cached = _live.Cached(DailyFold.Key(day));
            string name = cached?.Level?.Name ?? "The Lost Page";
            GUIUtility.systemCopyBuffer =
                $"Wonderfold — {name} ({LiveClock.DayLabel(day)})\n" +
                $"{_profile.DailyBest(day):N0} story ink\nThread: {thread}";
            _dailyShareLabel.text = "COPIED";
        }

        /// <summary>
        /// Verifies a pasted thread by replaying it. Nothing is taken on trust: the page is rebuilt from
        /// the code's own key and the score is recomputed from the moves.
        /// </summary>
        private void ReadThread()
        {
            string code = _threadField != null ? _threadField.text : null;
            if (!ReplayCode.TryDecode(code, out var thread, out string error))
            {
                _threadResult.text = error;
                _threadResult.color = new Color(0.95f, 0.55f, 0.5f);
                return;
            }

            var page = _live.ResolveThreadPage(thread);
            if (page == null)
            {
                // The page has never been woven on this device. Weave it, then read the thread again.
                _threadResult.color = new Color(0.85f, 0.83f, 0.95f);
                _threadResult.text = "Weaving that page so the thread can be checked…";

                if (DailyFold.TryParseKey(thread.PageKey, out int day))
                    _live.RequestDaily(day, _ => ReadThread());
                else if (EndlessArchive.TryParseKey(thread.PageKey, out int depth))
                    _live.RequestEndless(depth, _ => ReadThread());
                else
                    _threadResult.text = "That thread names a page this archive does not hold.";

                return;
            }

            if (!ReplayVerifier.TryReplay(page.Level, thread, out var stats, out error))
            {
                _threadResult.text = error;
                _threadResult.color = new Color(0.95f, 0.55f, 0.5f);
                return;
            }

            int ink = RunScore.StoryInk(stats);
            int stars = RunScore.Stars(page.Level, stats);
            int mine = DailyFold.TryParseKey(thread.PageKey, out int myDay) ? _profile.DailyBest(myDay) : 0;

            _threadResult.color = new Color(0.80f, 0.92f, 0.85f);
            _threadResult.text = $"{page.Level.Name}: {ShareCard.Stars(stars)} {ink:N0} ink in " +
                                 $"{thread.Moves.Count} moves" +
                                 (mine > 0 ? (ink > mine ? "  —  they are ahead of you." : "  —  you are ahead.") : string.Empty);
        }

        private void MendStreak()
        {
            if (!_live.TryMendStreak())
            {
                _mendLabel.text = "NOT ENOUGH COINS";
                return;
            }

            Refresh();
        }

        // ------------------------------------------------------------------ refresh

        public void Refresh()
        {
            if (_live == null) return;
            RefreshStatus();
            RefreshDaily();
            RefreshEndless();
            RefreshQuests();
        }

        private void RefreshStatus()
        {
            if (_profile == null) return;

            var untilTomorrow = _live.UntilTomorrow;
            _status.text = $"◈ {_profile.Coins:N0} coins     ♥ {_profile.Lives}/{PlayerProfile.MaxLives}" +
                           $"     new page in {(int)untilTomorrow.TotalHours:00}:{untilTomorrow.Minutes:00}:{untilTomorrow.Seconds:00}";

            var streak = _profile.Streak;
            _streakLine.text = streak.Current > 0
                ? $"✦ {streak.Current}-day streak   ·   best {streak.Best}   ·   tomorrow pays " +
                  $"{StreakLedger.RewardFor(streak.Current + 1).Coins} coins"
                : "✦ Play any page today to begin a streak.";

            bool canMend = _live.CanMendStreak;
            _mendButton.gameObject.SetActive(canMend);
            if (canMend) _mendLabel.text = $"MEND THE STREAK  ·  {_live.MendCost} coins";
        }

        private void RefreshDaily()
        {
            int day = _live.Today;
            var cached = _live.Cached(DailyFold.Key(day));
            int best = _profile.DailyBest(day);

            _dailyTitle.text = $"TODAY'S LOST PAGE  ·  {LiveClock.DayLabel(day)}";

            string name = cached?.Level == null ? "being written…" : cached.Level.Name;
            string shape = cached?.Level == null
                ? string.Empty
                : $"{cached.Level.Width}×{cached.Level.Height}  ·  {cached.Level.Moves} moves  ·  ";

            _dailyDetail.text =
                $"{name}\n{shape}{DifficultyWord(DailyFold.DifficultyFor(day))}\n" +
                (best > 0
                    ? $"Your best today: {best:N0} story ink"
                    : "Everyone in the world plays this exact page today.");

            _dailyPlayLabel.text = best > 0 ? "PLAY AGAIN" : "PLAY TODAY'S PAGE";
            _dailyShareLabel.text = best > 0 ? "COPY SHARE CARD" : "FINISH IT TO SHARE";
            SetProgress(_dailyProgressFill, cached != null ? 1f : 0f);
        }

        private void RefreshEndless()
        {
            int depth = _profile.EndlessDepth;
            var cached = _live.Cached(EndlessArchive.Key(depth));

            _endlessTitle.text = $"THE ENDLESS ARCHIVE  ·  PAGE {depth}";
            string name = cached?.Level == null ? "being written…" : cached.Level.Name;
            string measured = cached?.Report == null
                ? string.Empty
                : $"\nauditioned at {cached.WinRate:P0} — every page is bot-tested before it is served";

            _endlessDetail.text =
                $"{name}\ndeepest page reached: {Mathf.Max(_profile.EndlessBestDepth, 0)}" +
                (EndlessArchive.IsRewardDepth(depth) ? $"   ·   bound page: +{EndlessArchive.RewardCoins(depth)} coins" : string.Empty) +
                measured;

            _endlessPlayLabel.text = "CLIMB";
            SetProgress(_endlessProgressFill, cached != null ? 1f : 0f);
        }

        private void RefreshQuests()
        {
            var quests = new List<Quest>();
            quests.AddRange(_live.DailyQuests);
            quests.AddRange(_live.WeeklyQuests);

            var untilWeek = LiveClock.UntilNextWeek(DateTime.UtcNow);
            _errandsHeader.text = $"THE STORYKEEPER'S ERRANDS   ·   weekly list resets in {untilWeek.Days}d {untilWeek.Hours}h";

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (i >= quests.Count)
                {
                    row.Root.SetActive(false);
                    continue;
                }

                var quest = quests[i];
                row.Quest = quest;
                row.Root.SetActive(true);

                int progress = _profile.Quests.ProgressOf(quest);
                bool complete = _profile.Quests.IsComplete(quest);
                bool claimed = _profile.Quests.IsClaimed(quest);
                bool weekly = i >= _live.DailyQuests.Count;

                row.Title.text = $"{(weekly ? "week" : "today")}  ·  {quest.Title}   {progress}/{quest.Target}";
                row.BarFill.rectTransform.anchorMax = new Vector2(
                    quest.Target <= 0 ? 1f : Mathf.Clamp01((float)progress / quest.Target), 1f);
                row.BarFill.color = complete
                    ? new Color(0.36f, 0.78f, 0.45f)
                    : weekly ? new Color(0.63f, 0.52f, 0.92f) : new Color(0.95f, 0.78f, 0.38f);

                row.Claim.interactable = complete && !claimed;
                row.ClaimLabel.text = claimed ? "TAKEN" : $"+{quest.RewardCoins}";
            }
        }

        private void ClaimQuest(QuestRow row)
        {
            if (row?.Quest == null) return;
            if (_live.ClaimQuest(row.Quest) <= 0) return;
            Refresh();
        }

        private static void SetProgress(Image fill, float value)
        {
            if (fill == null) return;
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            fill.enabled = value > 0.001f && value < 0.999f;
        }

        private static string DifficultyWord(float difficulty)
        {
            if (difficulty < 0.36f) return "a gentle page";
            if (difficulty < 0.50f) return "a fair page";
            if (difficulty < 0.62f) return "a firm page";
            if (difficulty < 0.72f) return "a hard page";
            return "the week's spike";
        }

        // ------------------------------------------------------------------ construction

        private void Build()
        {
            Panel(transform, Vector2.zero, Vector2.one, new Color(0.045f, 0.035f, 0.10f, 0.99f));

            _title = Label(transform, "THE LIVING ARCHIVE", 48, TextAnchor.MiddleCenter);
            _title.color = new Color(1f, 0.84f, 0.38f);

            _status = Label(transform, "", 26, TextAnchor.MiddleCenter);
            _status.color = new Color(0.80f, 0.84f, 0.98f);

            _streakLine = Label(transform, "", 26, TextAnchor.MiddleCenter);
            _streakLine.color = new Color(0.96f, 0.78f, 0.55f);

            _mendButton = Button(transform, "Mend Streak", new Color(0.62f, 0.36f, 0.72f), () => MendStreak());
            _mendLabel = Label(_mendButton.transform, "", 24, TextAnchor.MiddleCenter, true);
            _mendButton.gameObject.SetActive(false);

            BuildDailyCard();
            BuildEndlessCard();

            _errandsHeader = Label(transform, "THE STORYKEEPER'S ERRANDS", 26, TextAnchor.MiddleLeft);
            _errandsHeader.color = new Color(0.78f, 0.75f, 0.90f);
            for (int i = 0; i < MaxQuestRows; i++) _rows.Add(BuildQuestRow());

            BuildThreadBox();

            _closeButton = Button(transform, "Close", new Color(0.30f, 0.29f, 0.42f), Close);
            var closeLabel = Label(_closeButton.transform, "BACK TO THE CHAPTER MAP", 26, TextAnchor.MiddleCenter, true);
            closeLabel.color = new Color(0.94f, 0.93f, 0.98f);
        }

        private void BuildDailyCard()
        {
            _dailyCard = Panel(transform, Vector2.zero, Vector2.one, new Color(0.17f, 0.13f, 0.30f, 1f));
            _dailyTitle = Label(_dailyCard.transform, "", 30, TextAnchor.UpperLeft);
            _dailyTitle.color = new Color(1f, 0.85f, 0.42f);
            _dailyDetail = Label(_dailyCard.transform, "", 24, TextAnchor.UpperLeft);
            _dailyDetail.color = new Color(0.86f, 0.85f, 0.95f);

            _dailyPlay = Button(_dailyCard.transform, "Play Daily", new Color(0.95f, 0.78f, 0.38f), PlayDaily);
            _dailyPlayLabel = Label(_dailyPlay.transform, "PLAY TODAY'S PAGE", 26, TextAnchor.MiddleCenter, true);
            _dailyPlayLabel.color = new Color(0.10f, 0.08f, 0.14f);

            _dailyShare = Button(_dailyCard.transform, "Share Daily", new Color(0.36f, 0.55f, 0.78f), ShareDaily);
            _dailyShareLabel = Label(_dailyShare.transform, "COPY SHARE CARD", 24, TextAnchor.MiddleCenter, true);

            _dailyProgressFill = ProgressBar(_dailyCard.transform, new Color(1f, 0.84f, 0.38f, 0.85f));
        }

        private void BuildEndlessCard()
        {
            _endlessCard = Panel(transform, Vector2.zero, Vector2.one, new Color(0.12f, 0.19f, 0.27f, 1f));
            _endlessTitle = Label(_endlessCard.transform, "", 30, TextAnchor.UpperLeft);
            _endlessTitle.color = new Color(0.62f, 0.90f, 0.86f);
            _endlessDetail = Label(_endlessCard.transform, "", 23, TextAnchor.UpperLeft);
            _endlessDetail.color = new Color(0.84f, 0.90f, 0.94f);

            _endlessPlay = Button(_endlessCard.transform, "Climb", new Color(0.42f, 0.78f, 0.70f), PlayEndless);
            _endlessPlayLabel = Label(_endlessPlay.transform, "CLIMB", 26, TextAnchor.MiddleCenter, true);
            _endlessPlayLabel.color = new Color(0.06f, 0.12f, 0.13f);

            _endlessProgressFill = ProgressBar(_endlessCard.transform, new Color(0.42f, 0.85f, 0.78f, 0.85f));
        }

        private QuestRow BuildQuestRow()
        {
            var row = new QuestRow();
            var panel = Panel(transform, Vector2.zero, Vector2.one, new Color(0.13f, 0.12f, 0.20f, 1f));
            row.Root = panel.gameObject;

            row.Title = Label(panel.transform, "", 22, TextAnchor.UpperLeft);
            row.Title.color = new Color(0.92f, 0.91f, 0.97f);

            var track = Panel(panel.transform, new Vector2(0.02f, 0.12f), new Vector2(0.74f, 0.40f),
                new Color(0.22f, 0.21f, 0.32f, 1f));
            row.BarFill = Panel(track.transform, Vector2.zero, new Vector2(0f, 1f), new Color(0.95f, 0.78f, 0.38f));

            row.Claim = Button(panel.transform, "Claim", new Color(0.36f, 0.70f, 0.50f), null);
            SetRect(row.Claim.GetComponent<RectTransform>(), new Vector2(0.78f, 0.14f), new Vector2(0.98f, 0.86f));
            row.ClaimLabel = Label(row.Claim.transform, "", 22, TextAnchor.MiddleCenter, true);
            row.ClaimLabel.color = new Color(0.06f, 0.10f, 0.08f);
            row.Claim.onClick.AddListener(() => ClaimQuest(row));

            row.Root.SetActive(false);
            return row;
        }

        private void BuildThreadBox()
        {
            var box = Panel(transform, Vector2.zero, Vector2.one, new Color(0.10f, 0.10f, 0.17f, 1f));
            box.name = "Thread Box";

            var caption = Label(box.transform, "A FRIEND'S STORY THREAD", 22, TextAnchor.UpperLeft);
            SetRect(caption.rectTransform, new Vector2(0.02f, 0.66f), new Vector2(0.98f, 0.98f));
            caption.color = new Color(0.75f, 0.73f, 0.88f);

            var fieldPanel = Panel(box.transform, new Vector2(0.02f, 0.34f), new Vector2(0.72f, 0.66f),
                new Color(0.19f, 0.18f, 0.28f, 1f));
            var fieldText = Label(fieldPanel.transform, "", 22, TextAnchor.MiddleLeft);
            SetRect(fieldText.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));
            fieldText.supportRichText = false;

            // The field is wired up while its object is inactive: an InputField that wakes without a text
            // component logs on the first frame.
            fieldPanel.gameObject.SetActive(false);
            _threadField = fieldPanel.gameObject.AddComponent<InputField>();
            _threadField.textComponent = fieldText;
            _threadField.targetGraphic = fieldPanel;
            _threadField.characterLimit = 512;
            fieldPanel.gameObject.SetActive(true);

            _threadButton = Button(box.transform, "Read Thread", new Color(0.55f, 0.45f, 0.85f), ReadThread);
            SetRect(_threadButton.GetComponent<RectTransform>(), new Vector2(0.74f, 0.34f), new Vector2(0.98f, 0.66f));
            var readLabel = Label(_threadButton.transform, "WATCH IT", 24, TextAnchor.MiddleCenter, true);
            readLabel.color = new Color(0.97f, 0.96f, 1f);

            _threadResult = Label(box.transform, "Paste a thread to replay it move for move and see the score it truly earns.",
                21, TextAnchor.UpperLeft);
            SetRect(_threadResult.rectTransform, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.32f));
            _threadResult.color = new Color(0.72f, 0.70f, 0.84f);
        }

        // ------------------------------------------------------------------ layout

        private void ApplyResponsiveLayout(bool force)
        {
            if (!force && Screen.width == _layoutWidth && Screen.height == _layoutHeight) return;
            _layoutWidth = Screen.width;
            _layoutHeight = Screen.height;

            bool landscape = Screen.width > Screen.height * 1.15f;
            _scaler.referenceResolution = landscape ? new Vector2(1920f, 1080f) : new Vector2(1080f, 1920f);

            if (landscape) LayoutLandscape();
            else LayoutPortrait();
        }

        private void LayoutPortrait()
        {
            SetRect(_title.rectTransform, new Vector2(0.04f, 0.945f), new Vector2(0.96f, 0.995f));
            SetRect(_status.rectTransform, new Vector2(0.04f, 0.910f), new Vector2(0.96f, 0.945f));
            SetRect(_streakLine.rectTransform, new Vector2(0.04f, 0.878f), new Vector2(0.96f, 0.910f));
            SetRect(Rect(_mendButton), new Vector2(0.24f, 0.836f), new Vector2(0.76f, 0.876f));

            SetRect(_dailyCard.rectTransform, new Vector2(0.04f, 0.640f), new Vector2(0.96f, 0.828f));
            LayoutDailyCard();

            SetRect(_endlessCard.rectTransform, new Vector2(0.04f, 0.488f), new Vector2(0.96f, 0.630f));
            LayoutEndlessCard();

            SetRect(_errandsHeader.rectTransform, new Vector2(0.05f, 0.452f), new Vector2(0.96f, 0.484f));
            LayoutQuestRows(0.448f, 0.0386f, 0.04f, 0.96f);

            SetRect(RectOf("Thread Box"), new Vector2(0.04f, 0.070f), new Vector2(0.96f, 0.206f));
            SetRect(Rect(_closeButton), new Vector2(0.28f, 0.014f), new Vector2(0.72f, 0.060f));
        }

        private void LayoutLandscape()
        {
            SetRect(_title.rectTransform, new Vector2(0.03f, 0.930f), new Vector2(0.97f, 0.995f));
            SetRect(_status.rectTransform, new Vector2(0.03f, 0.880f), new Vector2(0.97f, 0.930f));
            SetRect(_streakLine.rectTransform, new Vector2(0.03f, 0.835f), new Vector2(0.66f, 0.880f));
            SetRect(Rect(_mendButton), new Vector2(0.68f, 0.838f), new Vector2(0.97f, 0.882f));

            // Two columns: the pages on the left, the errands and the thread box on the right.
            SetRect(_dailyCard.rectTransform, new Vector2(0.03f, 0.520f), new Vector2(0.49f, 0.820f));
            LayoutDailyCard();

            SetRect(_endlessCard.rectTransform, new Vector2(0.03f, 0.250f), new Vector2(0.49f, 0.505f));
            LayoutEndlessCard();

            SetRect(RectOf("Thread Box"), new Vector2(0.03f, 0.075f), new Vector2(0.49f, 0.235f));
            SetRect(Rect(_closeButton), new Vector2(0.03f, 0.015f), new Vector2(0.49f, 0.065f));

            SetRect(_errandsHeader.rectTransform, new Vector2(0.52f, 0.775f), new Vector2(0.97f, 0.820f));
            LayoutQuestRows(0.762f, 0.118f, 0.52f, 0.97f);
        }

        private void LayoutDailyCard()
        {
            SetRect(_dailyTitle.rectTransform, new Vector2(0.03f, 0.76f), new Vector2(0.97f, 0.97f));
            SetRect(_dailyDetail.rectTransform, new Vector2(0.03f, 0.36f), new Vector2(0.97f, 0.76f));
            SetRect(Rect(_dailyPlay), new Vector2(0.03f, 0.10f), new Vector2(0.58f, 0.33f));
            SetRect(Rect(_dailyShare), new Vector2(0.61f, 0.10f), new Vector2(0.97f, 0.33f));
            SetRect(_dailyProgressFill.rectTransform.parent as RectTransform,
                new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.07f));
        }

        private void LayoutEndlessCard()
        {
            SetRect(_endlessTitle.rectTransform, new Vector2(0.03f, 0.70f), new Vector2(0.97f, 0.96f));
            SetRect(_endlessDetail.rectTransform, new Vector2(0.03f, 0.28f), new Vector2(0.97f, 0.70f));
            SetRect(Rect(_endlessPlay), new Vector2(0.03f, 0.08f), new Vector2(0.44f, 0.26f));
            SetRect(_endlessProgressFill.rectTransform.parent as RectTransform,
                new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.06f));
        }

        private void LayoutQuestRows(float top, float height, float left, float right)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                float rowTop = top - i * height;
                SetRect(_rows[i].Root.GetComponent<RectTransform>(),
                    new Vector2(left, rowTop - height + 0.008f), new Vector2(right, rowTop));
            }
        }

        // ------------------------------------------------------------------ tiny uGUI helpers

        private RectTransform RectOf(string childName)
        {
            var child = transform.Find(childName);
            return child == null ? null : child as RectTransform;
        }

        private static RectTransform Rect(Component component) =>
            component == null ? null : component.GetComponent<RectTransform>();

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max) =>
            SetRect(rect, min, max, Vector2.zero, Vector2.zero);

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin,
            Vector2 offsetMax)
        {
            if (rect == null) return;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static Image Panel(Transform parent, Vector2 min, Vector2 max, Color colour)
        {
            var go = new GameObject("Panel", typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = ProceduralArt.RoundedSquare(0.10f, 0.02f);
            image.color = colour;
            SetRect(image.rectTransform, min, max);
            return image;
        }

        private static Image ProgressBar(Transform parent, Color colour)
        {
            var track = Panel(parent, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.07f),
                new Color(0.20f, 0.19f, 0.28f, 0.9f));
            track.name = "Progress";
            var fill = Panel(track.transform, Vector2.zero, new Vector2(0f, 1f), colour);
            fill.enabled = false;
            return fill;
        }

        private Button Button(Transform parent, string name, Color colour, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = ProceduralArt.RoundedSquare(0.18f, 0.02f);
            image.color = colour;
            SetRect(image.rectTransform, Vector2.zero, Vector2.one);
            var button = go.GetComponent<Button>();
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        private static Text Label(Transform parent, string text, int size, TextAnchor alignment,
            bool fill = false)
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
            SetRect(label.rectTransform, Vector2.zero, Vector2.one,
                fill ? Vector2.zero : new Vector2(6f, 2f), fill ? Vector2.zero : new Vector2(-6f, -2f));
            return label;
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
    }
}
