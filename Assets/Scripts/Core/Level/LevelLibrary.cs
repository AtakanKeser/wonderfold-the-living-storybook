using System.Collections.Generic;

namespace Wonderfold.Core.Level
{
    /// <summary>
    /// An ordered set of levels. Deliberately fed raw JSON strings rather than file paths, so the same
    /// class serves the command line tool (reads from disk), Unity (reads TextAssets or Addressables)
    /// and the tests (string literals).
    /// </summary>
    public sealed class LevelLibrary
    {
        private readonly List<LevelDefinition> _levels = new List<LevelDefinition>();
        private readonly Dictionary<int, LevelDefinition> _byId = new Dictionary<int, LevelDefinition>();

        public IReadOnlyList<LevelDefinition> Levels => _levels;
        public int Count => _levels.Count;

        public static LevelLibrary FromJsonTexts(IEnumerable<string> jsonTexts, List<string> errors = null)
        {
            var library = new LevelLibrary();
            foreach (var text in jsonTexts)
            {
                if (string.IsNullOrEmpty(text)) continue;
                if (LevelSerializer.TryFromJson(text, out var level, out var error)) library.Add(level);
                else errors?.Add(error);
            }

            library.Sort();
            return library;
        }

        public void Add(LevelDefinition level)
        {
            if (level == null) return;
            _levels.Add(level);
            _byId[level.Id] = level;
        }

        public void Sort() => _levels.Sort((a, b) => a.Id.CompareTo(b.Id));

        public LevelDefinition ById(int id) => _byId.TryGetValue(id, out var level) ? level : null;

        public LevelDefinition ByIndex(int index) =>
            index >= 0 && index < _levels.Count ? _levels[index] : null;

        /// <summary>Levels belonging to one storybook, in play order.</summary>
        public List<LevelDefinition> Book(string book)
        {
            var result = new List<LevelDefinition>();
            for (int i = 0; i < _levels.Count; i++)
            {
                if (_levels[i].Book == book) result.Add(_levels[i]);
            }

            return result;
        }
    }
}
