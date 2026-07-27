using System.Collections;
using UnityEngine;
using Wonderfold.Core.Board;
using Wonderfold.Core.Level;
using Wonderfold.Core.Live;
using Wonderfold.Core.Primitives;
using Wonderfold.Game.Meta;
using Wonderfold.Game.Presentation;
using Wonderfold.Game.Services;
using Wonderfold.Game.UI;

namespace Wonderfold.Game.Bootstrap
{
    /// <summary>
    /// Runs one level: owns the <see cref="LevelSession"/>, feeds it the player's intent, and hands the
    /// resulting event stream to the view.
    ///
    /// <para>Input is closed while a batch animates. That is not a nicety — the core resolves a whole
    /// turn synchronously, so a second move arriving mid-animation would be applied to a board the
    /// player cannot see yet.</para>
    /// </summary>
    public sealed class LevelRunner : MonoBehaviour
    {
        private LevelSession _session;
        private LevelDefinition _definition;
        private BoardView _boardView;
        private BoardInput _input;
        private GameHud _hud;
        private Camera _camera;
        private PlayerProfile _profile;
        private SaveSystem _saves;
        private DioramaView _diorama;
        private LiveOpsService _liveOps;
        private int _seed;
        private string _pageKey;
        private bool _isAuthored = true;
        private bool _pausedByMenu;
        private int _layoutScreenWidth;
        private int _layoutScreenHeight;

        public LevelSession Session => _session;

        /// <summary>Fires for every finished page. <c>outcome</c> is null for generated pages.</summary>
        public event System.Action<LevelDefinition, LevelOutcome, LiveRunOutcome> LevelFinished;

        public string PageKey => _pageKey;
        public bool IsAuthoredPage => _isAuthored;

        public void Initialise(Camera camera, BoardView boardView, BoardInput input, GameHud hud,
            DioramaView diorama, PlayerProfile profile, SaveSystem saves, LiveOpsService liveOps)
        {
            _camera = camera;
            _boardView = boardView;
            _input = input;
            _hud = hud;
            _diorama = diorama;
            _profile = profile;
            _saves = saves;
            _liveOps = liveOps;

            _input.MoveRequested += OnMoveRequested;
            _hud.FoldRequested += OnFoldRequested;
            _hud.ToolSelected += OnToolSelected;
            _boardView.BatchCompleted += OnBatchCompleted;
        }

        private void OnDestroy()
        {
            if (_input != null) _input.MoveRequested -= OnMoveRequested;
            if (_hud != null) _hud.FoldRequested -= OnFoldRequested;
            if (_hud != null) _hud.ToolSelected -= OnToolSelected;
            if (_boardView != null) _boardView.BatchCompleted -= OnBatchCompleted;
        }

        public void LoadLevel(LevelDefinition definition, int seed, bool packStartingRocket = false)
        {
            LoadPage(definition, seed, ReplayCode.AuthoredKey(definition.Id), packStartingRocket);
        }

        /// <summary>
        /// Runs any page, authored or woven. The key is what tells the runner which rules of progression
        /// apply on the way out: an authored page unlocks the next chapter entry and mends a piece of the
        /// diorama, a woven one reports to the live archive instead.
        /// </summary>
        public void LoadPage(LevelDefinition definition, int seed, string pageKey, bool packStartingRocket = false)
        {
            _pageKey = pageKey;
            _isAuthored = ReplayCode.TryParseAuthored(pageKey, out _);
            LoadCore(definition, seed, packStartingRocket, null);
        }

        public void ResumeLevel(LevelDefinition definition, int seed, System.Collections.Generic.List<PlayerMove> moves)
        {
            _pageKey = ReplayCode.AuthoredKey(definition.Id);
            _isAuthored = true;
            LoadCore(definition, seed, false, moves);
        }

        private void LoadCore(LevelDefinition definition, int seed, bool packStartingRocket, System.Collections.Generic.List<PlayerMove> replayMoves)
        {
            StopAllCoroutines();
            _pausedByMenu = false;
            _definition = definition;
            _seed = seed;

            _session = new LevelSession(definition, seed);
            _session.Start();
            if (packStartingRocket) _session.TryPlaceStartingRocket();

            if (replayMoves != null)
            {
                for (int i = 0; i < replayMoves.Count; i++) _session.TryExecute(replayMoves[i], out _);
                _session.Events.Drain();
            }

            // A page is resumable as soon as it opens. This lets a player use Chapter Map immediately,
            // even before making a first move, without silently losing the page they just selected.
            if (_isAuthored)
            {
                _profile.ActiveLevelId = _definition.Id;
                _profile.ActiveSeed = _seed;
                _profile.ActiveMoves.Clear();
                _profile.ActiveMoves.AddRange(_session.MoveHistory);
                _saves.Save(_profile);
            }

            ApplyResponsiveBoardLayout(true);
            _input.SelectTool(null);
            _input.Enabled = false;

            _hud.BindProfile(_profile);
            _hud.BindLevel(definition, _session);
            _hud.Refresh(_session);

            _diorama?.ShowLevel(definition, _profile);

            // Drain whatever Start() produced without animating it; the board opens settled.
            _session.Events.Drain();
            StartCoroutine(BeginLevel());
        }

        private void Update()
        {
            if (_session == null || _boardView == null || _boardView.IsAnimating) return;
            if (Screen.width == _layoutScreenWidth && Screen.height == _layoutScreenHeight) return;
            ApplyResponsiveBoardLayout(false);
        }

        private void ApplyResponsiveBoardLayout(bool initial)
        {
            if (_session == null || _definition == null || _camera == null) return;

            var layout = BoardLayout.CreateResponsive(_camera, _definition.Width, _definition.Height);
            if (initial) _boardView.Bind(_session, layout);
            else _boardView.Relayout(layout);

            _input.Bind(_camera, layout, _session.Board);
            _diorama?.ApplyBoardLayout(layout, _session.Board);
            _layoutScreenWidth = Screen.width;
            _layoutScreenHeight = Screen.height;
        }

        /// <summary>
        /// Replays the current page. An authored page gets a fresh seed so a retry is a new board; a page
        /// from the archive keeps its seed, because a shared page that reshuffled on retry would stop
        /// being the same page for everyone.
        /// </summary>
        public void Restart() => LoadPage(_definition, _isAuthored ? _seed + 1 : _seed, _pageKey);

        /// <summary>Closes player input while the chapter map or page menu is in front of the board.</summary>
        public void SetPausedByMenu(bool paused)
        {
            _pausedByMenu = paused;
            if (paused)
            {
                _input.Enabled = false;
                _input.SelectTool(null);
                _hud.ClearSelectedTool();
                return;
            }

            if (_session != null && !_session.IsOver && !_boardView.IsAnimating)
                _input.Enabled = true;
        }

        private void OnMoveRequested(PlayerMove move) => Execute(move);

        private void OnFoldRequested(int regionId) => Execute(PlayerMove.Fold(regionId));

        private void OnToolSelected(PageTool? tool)
        {
            if (_pausedByMenu || _session == null || _session.IsOver || _boardView.IsAnimating) return;
            _input.SelectTool(tool);
        }

        private IEnumerator BeginLevel()
        {
            if (_definition.IntroDialogue.Count > 0)
                yield return _hud.PlayDialogue(_definition.IntroDialogue);
            if (_session != null && !_session.IsOver && !_pausedByMenu) _input.Enabled = true;
        }

        private void Execute(PlayerMove move)
        {
            if (_pausedByMenu || _session == null || _session.IsOver || _boardView.IsAnimating) return;

            bool spentTool = false;
            if (move.Kind == MoveKind.UseTool)
            {
                if (!_profile.TrySpendTool(move.Tool))
                {
                    Debug.Log("[Wonderfold] That page tool is not in the pouch.");
                    _input.SelectTool(null);
                    _hud.ClearSelectedTool();
                    return;
                }
                spentTool = true;
            }

            if (!_session.TryExecute(move, out var reason))
            {
                if (spentTool) _profile.RefundTool(move.Tool);
                // Rejections still produce events (the shake, the "locked" callout).
                var rejected = _session.Events.Drain();
                if (rejected.Count > 0) _boardView.Play(rejected);
                if (!string.IsNullOrEmpty(reason)) Debug.Log($"[Wonderfold] {reason}");
                _input.SelectTool(null);
                _hud.ClearSelectedTool();
                _hud.RefreshProfile();
                return;
            }

            if (spentTool)
            {
                _input.SelectTool(null);
                _hud.ClearSelectedTool();
                _hud.RefreshProfile();
            }

            // Only authored pages are resumable from the chapter map. A woven page is reproducible from
            // its key rather than from the catalogue, so parking its id in the profile would send the next
            // launch looking for a level that was never on disk.
            if (_isAuthored)
            {
                _profile.ActiveLevelId = _definition.Id;
                _profile.ActiveSeed = _seed;
                _profile.ActiveMoves.Clear();
                _profile.ActiveMoves.AddRange(_session.MoveHistory);
                _saves.Save(_profile);
            }

            _input.Enabled = false;
            _boardView.Play(_session.Events.Drain());
        }

        private void OnBatchCompleted()
        {
            _hud.Refresh(_session);

            if (!_session.IsOver)
            {
                if (!_pausedByMenu) _input.Enabled = true;
                return;
            }

            StartCoroutine(FinishLevel());
        }

        private IEnumerator FinishLevel()
        {
            _profile.ActiveLevelId = 0;
            _profile.ActiveMoves.Clear();

            _input.Enabled = false;
            yield return new WaitForSeconds(0.35f);

            bool won = _session.Outcome == LevelOutcome.Won;
            LiveRunOutcome live = null;

            if (won && _isAuthored)
            {
                _profile.RecordWin(_definition.Id, _session.MovesRemaining, _definition.DioramaPieceId);
                _liveOps?.RecordAuthoredRun(_definition, _session);
                _saves.Save(_profile);
                yield return _diorama != null
                    ? _diorama.PlayRestoration(_definition, _boardView)
                    : null;
            }
            else
            {
                if (!won) _profile.TryConsumeLife(System.DateTime.UtcNow.Ticks);
                if (!_isAuthored && _liveOps != null) live = _liveOps.RecordRun(_pageKey, _definition, _session);
                _saves.Save(_profile);
            }

            _hud.RefreshProfile();

            if (_definition.OutroDialogue.Count > 0)
                yield return _hud.PlayDialogue(_definition.OutroDialogue);

            if (won && _definition.DioramaPieceId == "ferris-wheel-cabins" &&
                !_profile.StoryChoices.ContainsKey("carnival-ferris-theme"))
            {
                yield return _hud.ChooseCarnivalStyle(choice =>
                {
                    _profile.StoryChoices["carnival-ferris-theme"] = choice;
                    _saves.Save(_profile);
                    _diorama?.ShowLevel(_definition, _profile);
                });
            }

            _hud.ShowLevelEnd(_session.Outcome, Summary(won, live),
                _isAuthored ? (won ? "Continue" : "Try Again") : "Back to the Archive");
            LevelFinished?.Invoke(_definition, _session.Outcome, live);
        }

        private string Summary(bool won, LiveRunOutcome live)
        {
            if (!won)
            {
                for (int i = 0; i < _session.Goals.Count; i++)
                {
                    var goal = _session.Goals[i];
                    if (!goal.IsComplete)
                        return $"{goal.Description}\nstill {goal.Target - goal.Current} to go.";
                }

                return "So close.";
            }

            var stats = _session.Stats;
            string summary = $"{stats.MovesRemaining} moves spare  ·  {stats.Folds} folds  ·  " +
                             $"{stats.GoldenStitches} golden stitches";

            if (live == null) return summary;

            // A shared page is judged on score rather than on "you passed", so the number leads.
            summary = $"{ShareCard.Stars(live.Stars)}   {live.StoryInk:N0} story ink\n{summary}";
            if (live.IsPersonalBest) summary += "\nA new personal best for this page.";
            if (live.CoinsAwarded > 0) summary += $"\n+{live.CoinsAwarded} coins";
            if (live.QuestsCompleted.Count > 0) summary += $"\n{live.QuestsCompleted.Count} errand(s) finished";
            return summary;
        }
    }
}
