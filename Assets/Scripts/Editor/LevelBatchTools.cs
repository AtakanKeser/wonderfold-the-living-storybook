using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Wonderfold.Core.Level;
using Wonderfold.Core.Simulation;

namespace Wonderfold.EditorTools
{
    /// <summary>
    /// Chapter-wide tooling: validate everything, sweep everything, and fail a build when a level is
    /// broken. The same checks the command line runs, available without leaving the editor.
    /// </summary>
    public static class LevelBatchTools
    {
        private const string LevelFolder = "Levels";

        [MenuItem("Wonderfold/Validate All Levels")]
        public static void ValidateAll()
        {
            var levels = LoadAll();
            int errors = 0;
            int warnings = 0;

            for (int i = 0; i < levels.Count; i++)
            {
                var issues = LevelValidator.Validate(levels[i]);
                for (int j = 0; j < issues.Count; j++)
                {
                    string message = $"[Wonderfold] level {levels[i].Id} '{levels[i].Name}': {issues[j].Message}";
                    if (issues[j].Severity == ValidationSeverity.Error)
                    {
                        errors++;
                        Debug.LogError(message);
                    }
                    else if (issues[j].Severity == ValidationSeverity.Warning)
                    {
                        warnings++;
                        Debug.LogWarning(message);
                    }
                }
            }

            Debug.Log($"[Wonderfold] {levels.Count} levels validated — {errors} errors, {warnings} warnings.");
        }

        [MenuItem("Wonderfold/Simulate Chapter (500 runs each)")]
        public static void SimulateChapter()
        {
            var levels = LoadAll();
            var report = new StringBuilder();
            report.AppendLine("id   name                         win%   dead%  moves  folds  seam  stitch  blocked by");

            try
            {
                for (int i = 0; i < levels.Count; i++)
                {
                    var level = levels[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Level Laboratory",
                            $"Simulating {level.Id} — {level.Name}", (float)i / levels.Count))
                    {
                        break;
                    }

                    var sweep = LevelSimulator.Sweep(level, 500, "heuristic");
                    bool offTarget = level.DesignWinRate > 0f &&
                                     Mathf.Abs(sweep.WinRate - level.DesignWinRate) > level.DesignWinRateTolerance;

                    report.AppendLine(
                        $"{level.Id,-4} {Truncate(level.Name, 27),-27} {sweep.WinRate,6:P0} {sweep.DeadBoardRate,6:P0} " +
                        $"{sweep.AverageMovesUsed,6:F1} {sweep.AverageFolds,6:F2} {sweep.AverageSeamMatches,5:F1} " +
                        $"{sweep.AverageGoldenStitches,7:F2}  {sweep.MostCommonFailureGoal() ?? "-"}" +
                        (offTarget ? $"   OFF TARGET (want {level.DesignWinRate:P0})" : string.Empty));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[Wonderfold] Chapter balance\n{report}");
        }

        private static string Truncate(string value, int length) =>
            string.IsNullOrEmpty(value) || value.Length <= length ? value ?? string.Empty : value.Substring(0, length);

        private static List<LevelDefinition> LoadAll()
        {
            var assets = Resources.LoadAll<TextAsset>(LevelFolder);
            var texts = new List<string>(assets.Length);
            for (int i = 0; i < assets.Length; i++) texts.Add(assets[i].text);

            var errors = new List<string>();
            var library = LevelLibrary.FromJsonTexts(texts, errors);
            for (int i = 0; i < errors.Count; i++) Debug.LogError($"[Wonderfold] {errors[i]}");

            return new List<LevelDefinition>(library.Levels);
        }
    }
}
