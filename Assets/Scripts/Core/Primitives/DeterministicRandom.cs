using System;
using System.Collections.Generic;

namespace Wonderfold.Core.Primitives
{
    /// <summary>
    /// xorshift64* PRNG.
    /// <para>
    /// We do not use <see cref="System.Random"/> because its sequence is not contractually stable
    /// across runtimes — and the whole point of the simulation harness, replays and save/restore is
    /// that the same seed plus the same inputs must always produce the same board.
    /// </para>
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(int seed) : this(Scramble((ulong)(uint)seed)) { }

        public DeterministicRandom(ulong state)
        {
            _state = state == 0 ? 0x9E3779B97F4A7C15UL : state;
        }

        private static ulong Scramble(ulong seed)
        {
            // splitmix64 finaliser — spreads small seeds like 0/1/2 across the whole 64-bit space.
            ulong z = seed + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Full internal state — persisted in saves so a resumed level continues identically.</summary>
        public ulong State
        {
            get => _state;
            set => _state = value == 0 ? 0x9E3779B97F4A7C15UL : value;
        }

        public DeterministicRandom Clone() => new DeterministicRandom(_state);

        public ulong NextUInt64()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        public uint NextUInt32() => (uint)(NextUInt64() >> 32);

        /// <summary>Uniform in [0, maxExclusive). Rejection sampled, so no modulo bias.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            uint bound = (uint)maxExclusive;
            uint threshold = (uint)((0x100000000UL - bound) % bound);
            while (true)
            {
                uint r = NextUInt32();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        /// <summary>Uniform in [0,1).</summary>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        public bool Chance(double probability) => NextDouble() < probability;

        /// <summary>Picks an index proportional to the supplied weights. Returns -1 if all weights are zero.</summary>
        public int WeightedIndex(IReadOnlyList<int> weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] > 0) total += weights[i];
            }

            if (total <= 0) return -1;

            int roll = NextInt(total);
            for (int i = 0; i < weights.Count; i++)
            {
                int w = weights[i];
                if (w <= 0) continue;
                if (roll < w) return i;
                roll -= w;
            }

            return weights.Count - 1;
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
