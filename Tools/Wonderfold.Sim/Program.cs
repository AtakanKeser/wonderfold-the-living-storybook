using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Wonderfold.Core.Board;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Simulation;

namespace Wonderfold.Sim
{
    /// <summary>
    /// Headless Level Laboratory.
    ///
    /// <para>The same simulation the Unity editor window runs, driven from a terminal so it can be run in
    /// CI, diffed between branches, and used to balance a chapter without opening the engine.</para>
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = CommandLineOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            string levelsPath = options.LevelsPath ?? FindLevelsDirectory();
            if (levelsPath == null || !Directory.Exists(levelsPath))
            {
                Console.Error.WriteLine($"Could not find a levels directory (looked for '{levelsPath}').");
                return 2;
            }

            var errors = new List<string>();
            var library = LoadLibrary(levelsPath, errors);
            for (int i = 0; i < errors.Count; i++) Console.Error.WriteLine($"level parse error: {errors[i]}");

            if (library.Count == 0)
            {
                Console.Error.WriteLine($"No levels found in {levelsPath}.");
                return 2;
            }

            switch (options.Command)
            {
                case "simulate": return RunSimulate(library, options);
                case "validate": return RunValidate(library, options);
                case "render": return RunRender(library, options);
                case "play": return RunPlay(library, options);
                default:
                    Console.Error.WriteLine($"Unknown command '{options.Command}'.");
                    PrintHelp();
                    return 2;
            }
        }

        // ------------------------------------------------------------------ commands

        private static int RunSimulate(LevelLibrary library, CommandLineOptions options)
        {
            var targets = SelectLevels(library, options);
            if (targets.Count == 0)
            {
                Console.Error.WriteLine("No levels matched.");
                return 2;
            }

            Console.WriteLine($"Wonderfold — Level Laboratory   agent={options.Agent}  runs={options.Runs}");
            Console.WriteLine(new string('─', 108));
            Console.WriteLine(
                $"{"id",-4}{"name",-26}{"win%",7}{"dead%",7}{"moves",8}{"left",7}{"p10",6}{"folds",7}{"seam",7}{"stitch",8}{"boost",7}  blocked by");
            Console.WriteLine(new string('─', 108));

            int flagged = 0;
            foreach (var level in targets)
            {
                var report = LevelSimulator.Sweep(level, options.Runs, options.Agent, null, options.Seed);

                // A level that declares its intent is judged against that; everything else against the
                // tool's default band.
                bool outOfBand = level.DesignWinRate > 0f
                    ? Math.Abs(report.WinRate - level.DesignWinRate) > level.DesignWinRateTolerance
                    : report.WinRate < options.MinWinRate || report.WinRate > options.MaxWinRate;
                if (outOfBand) flagged++;

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-4}{1,-26}{2,6:P0} {3,6:P0} {4,7:F1} {5,6:F1} {6,5} {7,6:F2} {8,6:F1} {9,7:F2} {10,6:F1}  {11}{12}",
                    level.Id,
                    Truncate(level.Name, 25),
                    report.WinRate,
                    report.DeadBoardRate,
                    report.AverageMovesUsed,
                    (float)report.MedianMovesRemainingOnWin,
                    report.P10MovesRemainingOnWin,
                    report.AverageFolds,
                    report.AverageSeamMatches,
                    report.AverageGoldenStitches,
                    report.AverageBoosters,
                    report.MostCommonFailureGoal() ?? "-",
                    outOfBand
                        ? (level.DesignWinRate > 0f
                            ? $"  ⚠ target {level.DesignWinRate:P0}"
                            : "  ⚠")
                        : string.Empty));
            }

            Console.WriteLine(new string('─', 108));
            Console.WriteLine($"{targets.Count} levels simulated, {flagged} off target " +
                              $"(default band {options.MinWinRate:P0}–{options.MaxWinRate:P0}).");
            return 0;
        }

        private static int RunValidate(LevelLibrary library, CommandLineOptions options)
        {
            var targets = SelectLevels(library, options);
            int errors = 0;
            int warnings = 0;

            foreach (var level in targets)
            {
                var issues = LevelValidator.Validate(level);
                if (issues.Count == 0) continue;

                Console.WriteLine($"── {level.Id} {level.Name}");
                for (int i = 0; i < issues.Count; i++)
                {
                    Console.WriteLine($"   {issues[i]}");
                    if (issues[i].Severity == ValidationSeverity.Error) errors++;
                    else if (issues[i].Severity == ValidationSeverity.Warning) warnings++;
                }
            }

            Console.WriteLine($"{targets.Count} levels checked — {errors} errors, {warnings} warnings.");
            return errors > 0 ? 1 : 0;
        }

        private static int RunRender(LevelLibrary library, CommandLineOptions options)
        {
            var level = library.ById(options.LevelId) ?? library.ByIndex(0);
            var session = new LevelSession(level, options.Seed);
            session.Start();

            Console.WriteLine($"{level.Id} — {level.Name}   ({level.Width}x{level.Height}, {level.Moves} moves)");
            PrintSession(session);
            return 0;
        }

        /// <summary>
        /// Plays a level in the terminal. Not a toy: being able to drive the real core by hand, with no
        /// renderer in the way, is the fastest way to feel whether a rule change is right.
        /// </summary>
        private static int RunPlay(LevelLibrary library, CommandLineOptions options)
        {
            var level = library.ById(options.LevelId) ?? library.ByIndex(0);
            var session = new LevelSession(level, options.Seed);
            session.Start();

            Console.WriteLine($"{level.Id} — {level.Name}");
            Console.WriteLine("commands:  x1 y1 x2 y2   swap        |  t x y  tap booster");
            Console.WriteLine("           f <regionId>  fold        |  a      let the bot move");
            Console.WriteLine("           q             quit\n");

            var bot = AgentFactory.Create(options.Agent, options.Seed);

            while (!session.IsOver)
            {
                PrintSession(session);
                Console.Write("> ");
                var line = Console.ReadLine();
                if (line == null || line.Trim() == "q") break;

                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                PlayerMove move;
                if (parts[0] == "a")
                {
                    var chosen = bot.ChooseMove(session);
                    if (chosen == null)
                    {
                        Console.WriteLine("the bot has nothing to play.");
                        continue;
                    }

                    move = chosen.Value;
                    Console.WriteLine($"bot plays {move}");
                }
                else if (parts[0] == "f" && parts.Length >= 2 && int.TryParse(parts[1], out int regionId))
                {
                    move = PlayerMove.Fold(regionId);
                }
                else if (parts[0] == "t" && parts.Length >= 3 &&
                         int.TryParse(parts[1], out int tx) && int.TryParse(parts[2], out int ty))
                {
                    move = PlayerMove.ActivateBooster(new GridCoord(tx, ty));
                }
                else if (parts.Length >= 4 &&
                         int.TryParse(parts[0], out int ax) && int.TryParse(parts[1], out int ay) &&
                         int.TryParse(parts[2], out int bx) && int.TryParse(parts[3], out int by))
                {
                    move = PlayerMove.Swap(new GridCoord(ax, ay), new GridCoord(bx, by));
                }
                else
                {
                    Console.WriteLine("did not understand that.");
                    continue;
                }

                if (!session.TryExecute(move, out var reason)) Console.WriteLine($"rejected: {reason}");
            }

            PrintSession(session);
            Console.WriteLine($"\noutcome: {session.Outcome}");
            return 0;
        }

        // ------------------------------------------------------------------ output helpers

        private static void PrintSession(LevelSession session)
        {
            var board = session.Board;
            Console.WriteLine();
            Console.WriteLine("     " + ColumnRuler(board.Width) + "        hidden face");

            for (int y = board.Height - 1; y >= 0; y--)
            {
                var visible = new System.Text.StringBuilder();
                var hidden = new System.Text.StringBuilder();
                for (int x = 0; x < board.Width; x++)
                {
                    var coord = new GridCoord(x, y);
                    visible.Append(Glyph(board.ActiveCell(coord))).Append(' ');
                    hidden.Append(Glyph(board.HiddenCell(coord))).Append(' ');
                }

                Console.WriteLine($"  {y,2} {visible}      {hidden}");
            }

            Console.WriteLine("     " + ColumnRuler(board.Width));

            Console.Write($"\n  moves {session.MovesRemaining}   fold meter {session.FoldMeter.Value}/{session.FoldMeter.Max}");
            var folds = session.AvailableFoldRegionIds();
            Console.WriteLine(folds.Count > 0 ? $"   foldable: {string.Join(",", folds)}" : "   foldable: —");

            for (int i = 0; i < session.Goals.Count; i++)
            {
                var goal = session.Goals[i];
                Console.WriteLine($"  {(goal.IsComplete ? "✔" : "·")} {goal.Description}: {goal.Current}/{goal.Target}");
            }

            for (int i = 0; i < board.Walkers.Count; i++) Console.WriteLine($"  ⤷ {board.Walkers[i]}");
            Console.WriteLine();
        }

        private static string ColumnRuler(int width)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = 0; x < width; x++) sb.Append((char)('0' + x % 10)).Append(' ');
            return sb.ToString();
        }

        private static char Glyph(Cell cell)
        {
            if (cell == null || cell.IsVoid) return ' ';
            if (cell.HasBlockingObstacle)
            {
                switch (cell.Obstacle.Type)
                {
                    case ObstacleType.TornPage: return cell.Obstacle.Health > 1 ? 'T' : 't';
                    case ObstacleType.WaxSeal: return cell.Obstacle.Health > 1 ? 'W' : 'w';
                    case ObstacleType.FoldLock: return 'L';
                    case ObstacleType.ArmourPlate: return (char)('0' + Math.Min(9, cell.Obstacle.Health));
                    default: return '?';
                }
            }

            if (cell.Tile == null) return '_';

            if (cell.Tile.IsBooster)
            {
                switch (cell.Tile.Booster)
                {
                    case BoosterType.RibbonRocket:
                        return cell.Tile.Orientation == BoosterOrientation.Horizontal ? '-' : '|';
                    case BoosterType.InkBloom: return '*';
                    case BoosterType.OrigamiBird: return '^';
                    case BoosterType.PrismBookmark: return '@';
                    case BoosterType.GoldenStitch: return '=';
                }
            }

            char baseGlyph = LevelGlyphs.ToChar(cell.Tile.Color);
            if (cell.Tile.Color == TileColor.None) baseGlyph = '?';
            if (cell.HasOverlayObstacle) return char.ToUpperInvariant(baseGlyph);
            return baseGlyph;
        }

        private static string Truncate(string value, int length) =>
            string.IsNullOrEmpty(value) || value.Length <= length ? value ?? string.Empty : value.Substring(0, length);

        // ------------------------------------------------------------------ plumbing

        private static List<LevelDefinition> SelectLevels(LevelLibrary library, CommandLineOptions options)
        {
            var result = new List<LevelDefinition>();
            if (options.All || options.LevelId <= 0)
            {
                result.AddRange(library.Levels);
                return result;
            }

            var level = library.ById(options.LevelId);
            if (level != null) result.Add(level);
            return result;
        }

        private static LevelLibrary LoadLibrary(string path, List<string> errors)
        {
            var texts = new List<string>();
            foreach (var file in Directory.GetFiles(path, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    texts.Add(File.ReadAllText(file));
                }
                catch (Exception e)
                {
                    errors.Add($"{Path.GetFileName(file)}: {e.Message}");
                }
            }

            return LevelLibrary.FromJsonTexts(texts, errors);
        }

        private static string FindLevelsDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && directory != null; i++)
            {
                var candidate = Path.Combine(directory.FullName, "Assets", "Resources", "Levels");
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            return null;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"wonderfold-sim — headless Level Laboratory

  simulate   run seeded bot playthroughs and report balance
  validate   check levels for authoring errors
  render     print a level's opening board
  play       play a level in the terminal

options
  --level <id>        target one level (default: all)
  --all               target every level
  --runs <n>          playthroughs per level          (default 500)
  --agent <name>      random | heuristic | greedy     (default heuristic)
  --seed <n>          base seed                       (default 1)
  --levels <path>     levels directory                (default <repo>/Assets/Resources/Levels)
  --min-win <0..1>    lower edge of the target band   (default 0.35)
  --max-win <0..1>    upper edge of the target band   (default 0.85)

examples
  wonderfold-sim simulate --all --runs 2000
  wonderfold-sim simulate --level 12 --agent greedy --runs 300
  wonderfold-sim play --level 5 --seed 42");
        }
    }

    internal sealed class CommandLineOptions
    {
        public string Command = "simulate";
        public int LevelId;
        public bool All;
        public int Runs = 500;
        public string Agent = "heuristic";
        public int Seed = 1;
        public string LevelsPath;
        public float MinWinRate = 0.35f;
        public float MaxWinRate = 0.85f;
        public bool ShowHelp;

        public static CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();
            if (args.Length == 0)
            {
                options.ShowHelp = true;
                return options;
            }

            int start = 0;
            if (!args[0].StartsWith("-"))
            {
                options.Command = args[0];
                start = 1;
            }

            for (int i = start; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        break;
                    case "--all":
                        options.All = true;
                        break;
                    case "--level":
                        options.LevelId = NextInt(args, ref i, options.LevelId);
                        break;
                    case "--runs":
                        options.Runs = NextInt(args, ref i, options.Runs);
                        break;
                    case "--seed":
                        options.Seed = NextInt(args, ref i, options.Seed);
                        break;
                    case "--agent":
                        options.Agent = NextString(args, ref i, options.Agent);
                        break;
                    case "--levels":
                        options.LevelsPath = NextString(args, ref i, options.LevelsPath);
                        break;
                    case "--min-win":
                        options.MinWinRate = NextFloat(args, ref i, options.MinWinRate);
                        break;
                    case "--max-win":
                        options.MaxWinRate = NextFloat(args, ref i, options.MaxWinRate);
                        break;
                }
            }

            return options;
        }

        private static int NextInt(string[] args, ref int i, int fallback) =>
            i + 1 < args.Length && int.TryParse(args[i + 1], out int value) ? Consume(ref i, value) : fallback;

        private static float NextFloat(string[] args, ref int i, float fallback) =>
            i + 1 < args.Length &&
            float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? Consume(ref i, value)
                : fallback;

        private static string NextString(string[] args, ref int i, string fallback) =>
            i + 1 < args.Length ? Consume(ref i, args[i + 1]) : fallback;

        private static T Consume<T>(ref int i, T value)
        {
            i++;
            return value;
        }
    }
}
