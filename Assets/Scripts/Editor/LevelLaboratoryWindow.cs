using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Wonderfold.Core.Board;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Simulation;

namespace Wonderfold.EditorTools
{
    /// <summary>
    /// The Level Laboratory: author a level, and measure it, without leaving the editor.
    ///
    /// <para>The reason this exists is that level balance is not a matter of opinion. A designer paints
    /// the board on the left, presses Simulate, and gets a win rate, a dead-board rate, how many folds a
    /// typical run uses and — the number that actually changes designs — which goal was the one that beat
    /// the player. Because the gameplay core has no Unity dependency, those thousands of playthroughs run
    /// in the editor in a second or two.</para>
    ///
    /// <para>Paint on either face, drop fold regions, watch the validator complain in real time, save
    /// back to the same JSON the shipped game loads. No intermediate format, no export step.</para>
    /// </summary>
    public sealed class LevelLaboratoryWindow : EditorWindow
    {
        private enum Brush
        {
            Empty,
            Void,
            Spawner,
            TornPage,
            WaxSeal,
            FoldLock,
            ArmourPlate,
            StoryKnot,
            PaperChain,
            VanishingInk,
            BlankStain,
            Illustration,
            Erase
        }

        private const string LevelFolder = "Assets/Resources/Levels";

        private LevelDefinition _level;
        private string _path;
        private SurfaceSide _face = SurfaceSide.Front;
        private Brush _brush = Brush.TornPage;
        private int _brushGroup = 1;

        private Vector2 _scroll;
        private List<ValidationIssue> _issues = new List<ValidationIssue>();

        private int _runs = 500;
        private int _agentIndex = 1;
        private SimulationReport _report;
        private double _lastSimulationSeconds;

        private BoardModel _preview;
        private int _previewSeed = 1;

        [MenuItem("Wonderfold/Level Laboratory %#l")]
        public static void Open()
        {
            var window = GetWindow<LevelLaboratoryWindow>("Level Laboratory");
            window.minSize = new Vector2(880f, 640f);
        }

        private void OnEnable()
        {
            if (_level == null) NewLevel();
        }

        private void OnGUI()
        {
            DrawToolbar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawGridColumn();
                DrawInspectorColumn();
            }
        }

        // ------------------------------------------------------------------ toolbar

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(52f))) NewLevel();
                if (GUILayout.Button("Open", EditorStyles.toolbarButton, GUILayout.Width(56f))) OpenLevel();
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(56f))) SaveLevel();

                GUILayout.Space(12f);

                var faces = new[] { "Front face", "Back face" };
                int selected = GUILayout.Toolbar((int)_face, faces, EditorStyles.toolbarButton,
                    GUILayout.Width(200f));
                _face = (SurfaceSide)selected;

                GUILayout.Space(12f);
                if (GUILayout.Button("Reroll preview", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                {
                    _previewSeed++;
                    RebuildPreview();
                }

                GUILayout.FlexibleSpace();

                if (_report != null)
                {
                    GUILayout.Label($"win {_report.WinRate:P0}   dead {_report.DeadBoardRate:P0}   " +
                                    $"{_lastSimulationSeconds:F2}s", EditorStyles.toolbarButton);
                }
            }
        }

        // ------------------------------------------------------------------ grid

        private void DrawGridColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(470f)))
            {
                EditorGUILayout.LabelField($"{_face} — click to paint, drag to sweep", EditorStyles.boldLabel);

                var surface = _level.SurfaceFor(_face);
                EnsureLayerSize(surface);

                float cell = Mathf.Min(430f / _level.Width, 430f / _level.Height);
                var rect = GUILayoutUtility.GetRect(_level.Width * cell, _level.Height * cell,
                    GUILayout.ExpandWidth(false));

                HandlePainting(rect, cell, surface);
                DrawGrid(rect, cell, surface);

                GUILayout.Space(8f);
                DrawBrushPalette();
                GUILayout.Space(8f);
                DrawLegend();
            }
        }

        private void DrawGrid(Rect rect, float cell, SurfaceSpec surface)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.11f, 0.16f));

            for (int row = 0; row < _level.Height; row++)
            for (int column = 0; column < _level.Width; column++)
            {
                var cellRect = new Rect(rect.x + column * cell, rect.y + row * cell, cell - 1f, cell - 1f);
                int y = _level.Height - 1 - row;

                char tile = Glyph(surface.Tiles, row, column, '.');
                char obstacle = Glyph(surface.Obstacles, row, column, '.');
                bool illustration = Glyph(surface.Illustration, row, column, '.') == '*';

                Color background = tile == '#' ? new Color(0.06f, 0.06f, 0.08f)
                    : tile == 'S' ? new Color(0.24f, 0.30f, 0.24f)
                    : new Color(0.20f, 0.20f, 0.26f);

                EditorGUI.DrawRect(cellRect, background);

                if (illustration)
                {
                    EditorGUI.DrawRect(new Rect(cellRect.x + 2f, cellRect.y + 2f, cellRect.width - 4f, 4f),
                        new Color(1f, 0.85f, 0.4f, 0.8f));
                }

                if (obstacle != '.' && obstacle != ' ')
                {
                    EditorGUI.DrawRect(new Rect(cellRect.x + 4f, cellRect.y + 4f,
                        cellRect.width - 8f, cellRect.height - 8f), ObstacleColour(obstacle));

                    var style = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Color.black }
                    };
                    GUI.Label(cellRect, obstacle.ToString(), style);
                }

                // Fold regions are drawn as an outline so the crease is visible while painting.
                var region = FindRegion(column, y);
                if (region != null)
                {
                    var tint = RegionColour(region.Id);
                    DrawOutline(cellRect, tint,
                        FindRegion(column - 1, y) != region, FindRegion(column + 1, y) != region,
                        FindRegion(column, y + 1) != region, FindRegion(column, y - 1) != region);
                }
            }
        }

        private static void DrawOutline(Rect rect, Color colour, bool left, bool right, bool top, bool bottom)
        {
            const float thickness = 2f;
            if (left) EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), colour);
            if (right) EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), colour);
            if (top) EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), colour);
            if (bottom) EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), colour);
        }

        private void HandlePainting(Rect rect, float cell, SurfaceSpec surface)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;
            if (!rect.Contains(e.mousePosition)) return;

            int column = Mathf.FloorToInt((e.mousePosition.x - rect.x) / cell);
            int row = Mathf.FloorToInt((e.mousePosition.y - rect.y) / cell);
            if (column < 0 || row < 0 || column >= _level.Width || row >= _level.Height) return;

            Paint(surface, row, column);
            e.Use();
            Revalidate();
            Repaint();
        }

        private void Paint(SurfaceSpec surface, int row, int column)
        {
            switch (_brush)
            {
                case Brush.Empty:
                    SetGlyph(surface.Tiles, row, column, '.');
                    SetGlyph(surface.Obstacles, row, column, '.');
                    break;
                case Brush.Void:
                    SetGlyph(surface.Tiles, row, column, '#');
                    SetGlyph(surface.Obstacles, row, column, '.');
                    break;
                case Brush.Spawner:
                    SetGlyph(surface.Tiles, row, column, 'S');
                    break;
                case Brush.Illustration:
                    char current = Glyph(surface.Illustration, row, column, '.');
                    SetGlyph(surface.Illustration, row, column, current == '*' ? '.' : '*');
                    break;
                case Brush.Erase:
                    SetGlyph(surface.Obstacles, row, column, '.');
                    SetGlyph(surface.Illustration, row, column, '.');
                    SetGlyph(surface.Groups, row, column, '0');
                    break;
                default:
                    SetGlyph(surface.Obstacles, row, column, ObstacleGlyph(_brush));
                    SetGlyph(surface.Groups, row, column, (char)('0' + Mathf.Clamp(_brushGroup, 0, 9)));
                    if (Glyph(surface.Tiles, row, column, '.') == '#')
                        SetGlyph(surface.Tiles, row, column, '.');
                    break;
            }
        }

        private void DrawBrushPalette()
        {
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            var names = Enum.GetNames(typeof(Brush));
            _brush = (Brush)GUILayout.SelectionGrid((int)_brush, names, 4);
            _brushGroup = EditorGUILayout.IntSlider("Group id", _brushGroup, 0, 9);
            EditorGUILayout.HelpBox(
                "Group id ties Paper Chain links together and matches a Fold Lock to the region it seals.",
                MessageType.None);
        }

        private void DrawLegend()
        {
            EditorGUILayout.LabelField("Legend", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("T torn  W wax  L lock  A armour  K knot  C chain  V ink  X stain",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("# hole   S spawner   gold bar = part of the hidden illustration",
                EditorStyles.miniLabel);
        }

        // ------------------------------------------------------------------ inspector

        private void DrawInspectorColumn()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;

                DrawLevelSettings();
                GUILayout.Space(10f);
                DrawFoldRegions();
                GUILayout.Space(10f);
                DrawGoals();
                GUILayout.Space(10f);
                DrawValidation();
                GUILayout.Space(10f);
                DrawSimulation();
                GUILayout.Space(10f);
                DrawPreview();
            }
        }

        private void DrawLevelSettings()
        {
            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _level.Id = EditorGUILayout.IntField("Id", _level.Id);
                _level.Name = EditorGUILayout.TextField("Name", _level.Name);
                _level.Book = EditorGUILayout.TextField("Book", _level.Book);
                _level.Moves = EditorGUILayout.IntSlider("Moves", _level.Moves, 5, 60);
                _level.Width = EditorGUILayout.IntSlider("Width", _level.Width, 4, 12);
                _level.Height = EditorGUILayout.IntSlider("Height", _level.Height, 4, 12);
                _level.DesignWinRate = EditorGUILayout.Slider("Target win rate", _level.DesignWinRate, 0f, 1f);
                _level.DioramaPieceId = EditorGUILayout.TextField("Diorama piece", _level.DioramaPieceId);

                if (check.changed)
                {
                    EnsureLayerSize(_level.Front);
                    EnsureLayerSize(_level.Back);
                    Revalidate();
                    RebuildPreview();
                }
            }
        }

        private void DrawFoldRegions()
        {
            EditorGUILayout.LabelField("Fold regions", EditorStyles.boldLabel);

            for (int i = 0; i < _level.FoldRegions.Count; i++)
            {
                var region = _level.FoldRegions[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"#{region.Id}", GUILayout.Width(30f));
                        region.Name = EditorGUILayout.TextField(region.Name);
                        if (GUILayout.Button("×", GUILayout.Width(24f)))
                        {
                            _level.FoldRegions.RemoveAt(i);
                            Revalidate();
                            return;
                        }
                    }

                    region.X = EditorGUILayout.IntSlider("X", region.X, 0, _level.Width - 1);
                    region.Y = EditorGUILayout.IntSlider("Y", region.Y, 0, _level.Height - 1);
                    region.Width = EditorGUILayout.IntSlider("Width", region.Width, 1, _level.Width - region.X);
                    region.Height = EditorGUILayout.IntSlider("Height", region.Height, 1, _level.Height - region.Y);
                    region.FlipAxis = (Axis)EditorGUILayout.EnumPopup("Flip axis", region.FlipAxis);
                    region.BackGravity = (Direction)EditorGUILayout.EnumPopup("Back gravity", region.BackGravity);
                    region.MeterCost = EditorGUILayout.IntSlider("Meter cost", region.MeterCost, 0, 100);
                    region.MoveCost = EditorGUILayout.IntSlider("Move cost", region.MoveCost, 0, 3);
                    region.LockGroup = EditorGUILayout.IntSlider("Lock group", region.LockGroup, 0, 9);
                }
            }

            if (GUILayout.Button("Add fold region"))
            {
                int id = _level.FoldRegions.Count + 1;
                _level.FoldRegions.Add(new FoldRegionSpec
                {
                    Id = id, Name = $"Leaf {id}",
                    X = 0, Y = 0, Width = Mathf.Min(3, _level.Width), Height = _level.Height,
                    MeterCost = 60
                });
                Revalidate();
            }
        }

        private void DrawGoals()
        {
            EditorGUILayout.LabelField("Goals", EditorStyles.boldLabel);
            var types = new[] { "collect", "obstacle", "illustration", "seam", "fold", "reconnect", "blank", "walker", "boss" };

            for (int i = 0; i < _level.Goals.Count; i++)
            {
                var goal = _level.Goals[i];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    int index = Mathf.Max(0, Array.IndexOf(types, goal.Type));
                    goal.Type = types[EditorGUILayout.Popup(index, types, GUILayout.Width(110f))];

                    if (goal.Type == "collect")
                        goal.Color = (TileColor)EditorGUILayout.EnumPopup(goal.Color, GUILayout.Width(90f));
                    if (goal.Type == "obstacle")
                        goal.Obstacle = (ObstacleType)EditorGUILayout.EnumPopup(goal.Obstacle, GUILayout.Width(110f));

                    goal.Count = EditorGUILayout.IntField(goal.Count, GUILayout.Width(50f));

                    if (GUILayout.Button("×", GUILayout.Width(24f)))
                    {
                        _level.Goals.RemoveAt(i);
                        Revalidate();
                        return;
                    }
                }
            }

            if (GUILayout.Button("Add goal"))
            {
                _level.Goals.Add(new GoalSpec { Type = "collect", Color = TileColor.Crimson, Count = 25 });
                Revalidate();
            }

            EditorGUILayout.HelpBox("An obstacle goal with count 0 sizes itself to whatever is on the board.",
                MessageType.None);
        }

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No problems found.", MessageType.Info);
                return;
            }

            for (int i = 0; i < _issues.Count; i++)
            {
                var issue = _issues[i];
                EditorGUILayout.HelpBox(issue.Message,
                    issue.Severity == ValidationSeverity.Error ? MessageType.Error :
                    issue.Severity == ValidationSeverity.Warning ? MessageType.Warning : MessageType.Info);
            }
        }

        private void DrawSimulation()
        {
            EditorGUILayout.LabelField("Simulation", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _runs = EditorGUILayout.IntPopup("Runs", _runs,
                    new[] { "100", "500", "2000", "10000" }, new[] { 100, 500, 2000, 10000 });
                _agentIndex = EditorGUILayout.Popup(_agentIndex, AgentFactory.Names, GUILayout.Width(90f));
            }

            using (new EditorGUI.DisabledScope(LevelValidator.HasErrors(_issues)))
            {
                if (GUILayout.Button("Simulate", GUILayout.Height(28f))) RunSimulation();
            }

            if (LevelValidator.HasErrors(_issues))
                EditorGUILayout.HelpBox("Fix the errors above before simulating.", MessageType.Warning);

            if (_report == null) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Stat("Win rate", $"{_report.WinRate:P1}",
                    _level.DesignWinRate > 0f ? $"target {_level.DesignWinRate:P0}" : null);
                Stat("Dead boards", $"{_report.DeadBoardRate:P1}");
                Stat("Moves used", $"{_report.AverageMovesUsed:F1} of {_level.Moves}");
                Stat("Moves left on a win", $"median {_report.MedianMovesRemainingOnWin}, p10 {_report.P10MovesRemainingOnWin}");
                Stat("Folds per run", $"{_report.AverageFolds:F2}");
                Stat("Seam matches", $"{_report.AverageSeamMatches:F1}");
                Stat("Golden stitches", $"{_report.AverageGoldenStitches:F2}");
                Stat("Boosters made", $"{_report.AverageBoosters:F1}");
                Stat("Usually blocked by", _report.MostCommonFailureGoal() ?? "nothing");
            }

            if (_level.DesignWinRate > 0f)
            {
                float drift = _report.WinRate - _level.DesignWinRate;
                if (Mathf.Abs(drift) > _level.DesignWinRateTolerance)
                {
                    EditorGUILayout.HelpBox(
                        drift > 0f
                            ? $"Easier than intended by {drift:P0}. Trim moves, or raise the goal."
                            : $"Harder than intended by {-drift:P0}. Add moves, or thin out the obstacles.",
                        MessageType.Warning);
                }
            }
        }

        private static void Stat(string label, string value, string note = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(170f));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel, GUILayout.Width(160f));
                if (!string.IsNullOrEmpty(note)) EditorGUILayout.LabelField(note, EditorStyles.miniLabel);
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField($"Opening board (seed {_previewSeed})", EditorStyles.boldLabel);
            if (_preview == null) RebuildPreview();
            if (_preview == null) return;

            EditorGUILayout.TextArea(_preview.ToAscii(), EditorStyles.textArea, GUILayout.Height(150f));
        }

        // ------------------------------------------------------------------ actions

        private void RunSimulation()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _report = LevelSimulator.Sweep(_level, _runs, AgentFactory.Names[_agentIndex]);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Level Laboratory] Simulation failed: {e}");
                _report = null;
            }

            stopwatch.Stop();
            _lastSimulationSeconds = stopwatch.Elapsed.TotalSeconds;
        }

        private void RebuildPreview()
        {
            try
            {
                _preview = LevelBuilder.Build(_level, _previewSeed);
                new ResolutionEngine(Wonderfold.Core.Rules.GameRules.Default).SettleSilently(_preview);
            }
            catch
            {
                _preview = null;
            }
        }

        private void Revalidate()
        {
            _issues = LevelValidator.Validate(_level);
            RebuildPreview();
        }

        private void NewLevel()
        {
            _level = new LevelDefinition { Id = 1, Name = "New Page", Moves = 25, DesignWinRate = 0.6f };
            _level.SpawnTables.Add(new SpawnTableSpec
            {
                Colors = { TileColor.Crimson, TileColor.Azure, TileColor.Meadow, TileColor.Amber, TileColor.Violet }
            });
            _level.Goals.Add(new GoalSpec { Type = "collect", Color = TileColor.Amber, Count = 30 });
            EnsureLayerSize(_level.Front);
            EnsureLayerSize(_level.Back);
            _path = null;
            _report = null;
            Revalidate();
        }

        private void OpenLevel()
        {
            var path = EditorUtility.OpenFilePanel("Open level", LevelFolder, "json");
            if (string.IsNullOrEmpty(path)) return;

            if (!LevelSerializer.TryFromJson(File.ReadAllText(path), out var level, out var error))
            {
                EditorUtility.DisplayDialog("Level Laboratory", $"That file did not parse:\n{error}", "OK");
                return;
            }

            _level = level;
            _path = path;
            _report = null;
            EnsureLayerSize(_level.Front);
            EnsureLayerSize(_level.Back);
            Revalidate();
        }

        private void SaveLevel()
        {
            var path = _path;
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel("Save level", LevelFolder,
                    $"level_{_level.Id:00}.json", "json");
                if (string.IsNullOrEmpty(path)) return;
            }

            File.WriteAllText(path, LevelSerializer.ToJson(_level));
            _path = path;
            AssetDatabase.Refresh();
            Debug.Log($"[Level Laboratory] Saved {path}");
        }

        // ------------------------------------------------------------------ layer helpers

        private void EnsureLayerSize(SurfaceSpec surface)
        {
            Resize(ref surface.Tiles, '.');
            Resize(ref surface.Obstacles, '.');
            Resize(ref surface.Groups, '0');
            Resize(ref surface.Illustration, '.');
            Resize(ref surface.SpawnGroups, '0');
            Resize(ref surface.StoryLinks, '0');
        }

        private void Resize(ref List<string> layer, char fill)
        {
            layer ??= new List<string>();
            while (layer.Count < _level.Height) layer.Add(new string(fill, _level.Width));
            while (layer.Count > _level.Height) layer.RemoveAt(layer.Count - 1);

            for (int i = 0; i < layer.Count; i++)
            {
                var row = layer[i] ?? string.Empty;
                if (row.Length < _level.Width) row = row.PadRight(_level.Width, fill);
                else if (row.Length > _level.Width) row = row.Substring(0, _level.Width);
                layer[i] = row;
            }
        }

        private static char Glyph(List<string> layer, int row, int column, char fallback)
        {
            if (layer == null || row < 0 || row >= layer.Count) return fallback;
            var line = layer[row];
            return line != null && column >= 0 && column < line.Length ? line[column] : fallback;
        }

        private static void SetGlyph(List<string> layer, int row, int column, char glyph)
        {
            if (layer == null || row < 0 || row >= layer.Count) return;
            var line = layer[row];
            if (line == null || column < 0 || column >= line.Length) return;
            var chars = line.ToCharArray();
            chars[column] = glyph;
            layer[row] = new string(chars);
        }

        private FoldRegionSpec FindRegion(int x, int y)
        {
            for (int i = 0; i < _level.FoldRegions.Count; i++)
            {
                var region = _level.FoldRegions[i];
                if (x >= region.X && x < region.X + region.Width &&
                    y >= region.Y && y < region.Y + region.Height)
                {
                    return region;
                }
            }

            return null;
        }

        private static Color RegionColour(int id)
        {
            float hue = (id * 0.27f) % 1f;
            return Color.HSVToRGB(hue, 0.75f, 1f);
        }

        private static char ObstacleGlyph(Brush brush)
        {
            switch (brush)
            {
                case Brush.TornPage: return 'T';
                case Brush.WaxSeal: return 'W';
                case Brush.FoldLock: return 'L';
                case Brush.ArmourPlate: return 'A';
                case Brush.StoryKnot: return 'K';
                case Brush.PaperChain: return 'C';
                case Brush.VanishingInk: return 'V';
                case Brush.BlankStain: return 'X';
                default: return '.';
            }
        }

        private static Color ObstacleColour(char glyph)
        {
            switch (glyph)
            {
                case 'T': return new Color(0.86f, 0.82f, 0.72f);
                case 'W': return new Color(0.72f, 0.23f, 0.29f);
                case 'L': return new Color(0.85f, 0.72f, 0.35f);
                case 'A': return new Color(0.62f, 0.65f, 0.72f);
                case 'K': return new Color(0.85f, 0.70f, 0.35f);
                case 'C': return new Color(0.70f, 0.72f, 0.80f);
                case 'V': return new Color(0.40f, 0.40f, 0.48f);
                case 'X': return new Color(0.93f, 0.93f, 0.95f);
                default: return Color.gray;
            }
        }
    }
}
