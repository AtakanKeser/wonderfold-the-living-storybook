using System;
using System.Collections.Generic;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// Weighted colour distribution used when a spawner refills. Levels can define several tables and
    /// assign them per cell, which is how the Midnight Carnival back surface leans blue (moonlight)
    /// while the front leans amber (lantern light).
    /// </summary>
    public sealed class SpawnTable
    {
        public int Group { get; }
        private readonly TileColor[] _colors;
        private readonly int[] _weights;

        public IReadOnlyList<TileColor> Colors => _colors;
        public IReadOnlyList<int> Weights => _weights;

        public SpawnTable(int group, IReadOnlyList<TileColor> colors, IReadOnlyList<int> weights = null)
        {
            if (colors == null || colors.Count == 0)
                throw new ArgumentException("A spawn table needs at least one colour.", nameof(colors));

            Group = group;
            _colors = new TileColor[colors.Count];
            for (int i = 0; i < colors.Count; i++) _colors[i] = colors[i];

            _weights = new int[colors.Count];
            for (int i = 0; i < colors.Count; i++)
            {
                _weights[i] = weights != null && i < weights.Count ? Math.Max(0, weights[i]) : 1;
            }

            bool any = false;
            for (int i = 0; i < _weights.Length; i++) if (_weights[i] > 0) any = true;
            if (!any) _weights[0] = 1;
        }

        public static SpawnTable Uniform(int group, params TileColor[] colors) => new SpawnTable(group, colors);

        public TileColor Roll(DeterministicRandom rng)
        {
            int i = rng.WeightedIndex(_weights);
            return i >= 0 ? _colors[i] : _colors[0];
        }

        /// <summary>Roll while avoiding a colour — used by the anti-frustration spawn filter.</summary>
        public TileColor RollExcluding(DeterministicRandom rng, TileColor excluded)
        {
            if (_colors.Length <= 1) return _colors[0];

            int total = 0;
            for (int i = 0; i < _colors.Length; i++) if (_colors[i] != excluded) total += _weights[i];
            if (total <= 0) return Roll(rng);

            int roll = rng.NextInt(total);
            for (int i = 0; i < _colors.Length; i++)
            {
                if (_colors[i] == excluded) continue;
                if (roll < _weights[i]) return _colors[i];
                roll -= _weights[i];
            }

            return _colors[0];
        }

        public bool Contains(TileColor color)
        {
            for (int i = 0; i < _colors.Length; i++) if (_colors[i] == color) return true;
            return false;
        }
    }
}
