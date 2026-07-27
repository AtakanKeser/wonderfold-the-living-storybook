using System.Collections.Generic;
using UnityEngine;
using Wonderfold.Core.Level;

namespace Wonderfold.Game.Services
{
    /// <summary>
    /// Loads authored levels from <c>Resources/Levels</c>.
    ///
    /// <para>The core takes JSON strings and knows nothing about Unity's asset pipeline, so swapping this
    /// for Addressables later is a change to this one file. Parse failures are reported rather than
    /// swallowed — a level that will not load is a build problem, not a runtime surprise.</para>
    /// </summary>
    public sealed class LevelCatalog
    {
        public LevelLibrary Library { get; private set; } = new LevelLibrary();
        public IReadOnlyList<string> LoadErrors => _errors;

        private readonly List<string> _errors = new List<string>();

        public const string ResourceFolder = "Levels";

        public void Load()
        {
            _errors.Clear();
            var assets = Resources.LoadAll<TextAsset>(ResourceFolder);
            var texts = new List<string>(assets.Length);
            for (int i = 0; i < assets.Length; i++) texts.Add(assets[i].text);

            Library = LevelLibrary.FromJsonTexts(texts, _errors);

            for (int i = 0; i < _errors.Count; i++) Debug.LogError($"[Wonderfold] level load: {_errors[i]}");
            if (Library.Count == 0)
                Debug.LogError($"[Wonderfold] No levels found in Resources/{ResourceFolder}.");
        }

        public LevelDefinition ById(int id) => Library.ById(id);
        public LevelDefinition ByIndex(int index) => Library.ByIndex(index);
        public int Count => Library.Count;
    }
}
