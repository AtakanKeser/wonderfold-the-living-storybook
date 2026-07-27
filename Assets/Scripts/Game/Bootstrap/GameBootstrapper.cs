using UnityEngine;
using UnityEngine.EventSystems;
using Wonderfold.Core.Board;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;
using Wonderfold.Game.Meta;
using Wonderfold.Game.Presentation;
using Wonderfold.Game.Services;
using Wonderfold.Game.UI;

namespace Wonderfold.Game.Bootstrap
{
    /// <summary>
    /// The single entry point. Drop this on one object in an otherwise empty scene and press Play.
    ///
    /// <para>Everything else — camera, board, HUD, diorama — is constructed here rather than authored,
    /// so the project has no scene file that can rot and no prefab reference that can come unset. When
    /// the art pass lands, this is the class that starts loading prefabs instead of building them.</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrapper : MonoBehaviour
    {
        [Header("Start")]
        [Tooltip("0 = continue from the save. Otherwise force a level id, which is what the demo capture uses.")]
        [SerializeField] private int _forceLevelId;
        [SerializeField] private int _seed = 20260727;
        [SerializeField] private bool _clearSaveOnStart;

        [Header("Presentation")]
        [SerializeField] private float _orthographicSize = 10f;
        [SerializeField] private Color _background = new Color(0.06f, 0.05f, 0.12f);

        private LevelCatalog _catalog;
        private SaveSystem _saves;
        private PlayerProfile _profile;
        private LevelRunner _runner;
        private GameHud _hud;
        private StoryMapView _map;
        private LevelDefinition _current;
        private LevelOutcome _lastOutcome = LevelOutcome.InProgress;

        /// <summary>Creates the whole game in an empty scene, so there is nothing to set up by hand.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (FindAnyObjectByType<GameBootstrapper>() != null) return;
            new GameObject("Wonderfold").AddComponent<GameBootstrapper>();
        }

        private void Start()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            _saves = new SaveSystem();
            if (_clearSaveOnStart) _saves.Delete();
            _profile = _saves.Load();
            if (_profile.RefreshLives(System.DateTime.UtcNow.Ticks)) _saves.Save(_profile);

            _catalog = new LevelCatalog();
            _catalog.Load();
            if (_catalog.Count == 0) return;

            var camera = BuildCamera();
            var diorama = DioramaView.Create(transform, camera);
            EnsureEventSystem();

            var boardObject = new GameObject("Board");
            boardObject.transform.SetParent(transform, false);
            var boardView = boardObject.AddComponent<BoardView>();
            var input = boardObject.AddComponent<BoardInput>();

            _hud = GameHud.Create(transform);
            _hud.PrimaryEndActionRequested += OnPrimaryEndAction;

            _runner = gameObject.AddComponent<LevelRunner>();
            _runner.Initialise(camera, boardView, input, _hud, diorama, _profile, _saves);
            _runner.LevelFinished += OnLevelFinished;
            FeedbackDirector.Create(transform, boardView);

            _map = StoryMapView.Create(transform);
            _map.LevelRequested += OnLevelRequested;

            if (_forceLevelId > 0) LoadStartingLevel();
            else if (_profile.HasActiveSession) ResumeActiveSession();
            else _map.Open(_catalog, _profile);
        }

        private void ResumeActiveSession()
        {
            var level = _catalog.ById(_profile.ActiveLevelId);
            if (level == null) 
            {
                _profile.ActiveLevelId = 0;
                _saves.Save(_profile);
                _map.Open(_catalog, _profile);
                return;
            }
            _current = level;
            _lastOutcome = LevelOutcome.InProgress;
            _map.Close();
            _runner.ResumeLevel(level, _profile.ActiveSeed, _profile.ActiveMoves);
        }

        private Camera BuildCamera()
        {
            var existing = Camera.main;
            if (existing != null)
            {
                Configure(existing);
                return existing;
            }

            var go = new GameObject("Main Camera", typeof(Camera));
            go.tag = "MainCamera";
            go.transform.SetParent(transform, false);
            var camera = go.GetComponent<Camera>();
            Configure(camera);
            return camera;
        }

        private void Configure(Camera camera)
        {
            camera.orthographic = true;
            camera.orthographicSize = _orthographicSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = _background;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
        }

        private void LoadStartingLevel()
        {
            int levelId = _forceLevelId > 0 ? _forceLevelId : _profile.HighestLevelUnlocked;
            _current = _catalog.ById(levelId) ?? _catalog.ByIndex(0);
            _runner.LoadLevel(_current, _seed + _current.Id);
        }

        private void OnLevelRequested(int levelId, bool packStartingRocket)
        {
            _profile.RefreshLives(System.DateTime.UtcNow.Ticks);
            if (_profile.Lives <= 0)
            {
                _map.Open(_catalog, _profile);
                return;
            }

            var level = _catalog.ById(levelId);
            if (level == null || !_profile.IsUnlocked(level.Id)) return;
            if (packStartingRocket)
            {
                if (!_profile.TrySpendTool(PageTool.RibbonRocket)) return;
                _saves.Save(_profile);
            }
            _current = level;
            _lastOutcome = LevelOutcome.InProgress;
            _map.Close();
            _runner.LoadLevel(level, _seed + level.Id, packStartingRocket);
        }

        private void OnLevelFinished(LevelDefinition level, LevelOutcome outcome)
        {
            _current = level;
            _lastOutcome = outcome;
        }

        private void OnPrimaryEndAction()
        {
            _hud.HideLevelEnd();

            if (_forceLevelId > 0)
            {
                _runner.Restart();
                return;
            }

            if (_lastOutcome != LevelOutcome.Won)
            {
                _profile.RefreshLives(System.DateTime.UtcNow.Ticks);
                if (_profile.Lives <= 0)
                {
                    _map.Open(_catalog, _profile);
                    return;
                }
                _runner.Restart();
                return;
            }

            var next = _catalog.ById(_current.Id + 1);
            if (next != null && _profile.IsUnlocked(next.Id))
            {
                _current = next;
                _runner.LoadLevel(next, _seed + next.Id);
                return;
            }

            _runner.Restart();
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }
}
