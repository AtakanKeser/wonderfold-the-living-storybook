using UnityEngine;

namespace Wonderfold.Game.Audio
{
    /// <summary>
    /// A small stereo score for the Midnight Carnival chapter. It is composed from several restrained
    /// instrument layers rather than a single tone so the prototype has a real musical arc while
    /// staying self-contained and safe to ship without a licensed music dependency.
    /// </summary>
    internal static class MidnightCarnivalComposer
    {
        private const int SampleRate = 32000;
        private const float LoopSeconds = 30f;
        private const float BarLength = 2.5f; // 96 BPM, twelve bars.

        public static AudioClip Compose()
        {
            int frames = Mathf.CeilToInt(SampleRate * LoopSeconds);
            var data = new float[frames * 2];

            // Roots, thirds and fifths. The A section is inviting; the bridge adds an A-major pull
            // before resolving home, so the loop feels like a little story rather than a menu drone.
            int[][] chords =
            {
                new[] { 50, 53, 57, 62 }, new[] { 46, 50, 53, 58 }, new[] { 53, 57, 60, 65 },
                new[] { 48, 52, 55, 60 }, new[] { 50, 53, 57, 62 }, new[] { 43, 46, 50, 55 },
                new[] { 46, 50, 53, 58 }, new[] { 45, 49, 52, 57 }, new[] { 46, 50, 53, 58 },
                new[] { 53, 57, 60, 65 }, new[] { 48, 52, 55, 60 }, new[] { 50, 53, 57, 62 }
            };

            int[][] melody =
            {
                new[] { 74, 77, 81, 77, 74, 72, 69, 74 }, new[] { 70, 74, 77, 74, 70, 65, 67, 70 },
                new[] { 72, 77, 81, 84, 81, 77, 72, 69 }, new[] { 67, 72, 76, 79, 76, 72, 67, 64 },
                new[] { 74, 77, 81, 77, 74, 72, 69, 74 }, new[] { 70, 74, 79, 77, 74, 70, 67, 70 },
                new[] { 70, 74, 77, 82, 77, 74, 70, 65 }, new[] { 69, 73, 76, 81, 76, 73, 69, 64 },
                new[] { 70, 74, 77, 82, 86, 82, 77, 74 }, new[] { 72, 77, 81, 84, 81, 77, 72, 69 },
                new[] { 67, 72, 76, 79, 84, 79, 76, 72 }, new[] { 69, 74, 77, 81, 77, 74, 72, 69 }
            };

            for (int barIndex = 0; barIndex < chords.Length; barIndex++)
            {
                float barStart = barIndex * BarLength;
                int[] chord = chords[barIndex];
                for (int voice = 0; voice < chord.Length; voice++)
                {
                    float pan = voice == 0 ? -0.28f : voice == chord.Length - 1 ? 0.28f : (voice % 2 == 0 ? -0.10f : 0.10f);
                    MixVelvetPad(data, barStart, BarLength + 0.12f, MidiToHz(chord[voice]), 0.027f, pan);
                }

                for (int beat = 0; beat < 4; beat++)
                {
                    float beatStart = barStart + beat * (BarLength * 0.25f);
                    MixBass(data, beatStart, 0.56f, MidiToHz(chord[0] - 12), 0.068f, -0.04f);
                    MixBrush(data, beatStart, beat == 0 ? 0.17f : 0.11f, beat == 0 ? 0.020f : 0.012f,
                        beat % 2 == 0 ? -0.20f : 0.20f);
                    if (beat == 1 || beat == 3)
                        MixPaperTick(data, beatStart + 0.055f, 0.10f, 0.014f, 0.24f);
                }

                float step = BarLength / 8f;
                for (int note = 0; note < melody[barIndex].Length; note++)
                {
                    float start = barStart + note * step;
                    float pan = note % 2 == 0 ? -0.16f : 0.16f;
                    MixCelesta(data, start, step * 1.85f, MidiToHz(melody[barIndex][note]), 0.067f, pan);
                    if ((note == 2 || note == 6) && barIndex >= 4)
                        MixHarp(data, start + 0.035f, step * 1.25f, MidiToHz(melody[barIndex][note] - 12), 0.022f, -pan);
                }

                if (barIndex == 3 || barIndex == 7 || barIndex == 11)
                {
                    MixCelesta(data, barStart + BarLength * 0.56f, 1.10f, MidiToHz(chord[3] + 5), 0.040f, 0.33f);
                    MixCelesta(data, barStart + BarLength * 0.64f, 0.92f, MidiToHz(chord[3] + 9), 0.032f, -0.33f);
                }
            }

            SoftLimit(data);
            var clip = AudioClip.Create("Wonderfold Midnight Carnival Score", frames, 2, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void MixVelvetPad(float[] data, float start, float duration, float frequency, float gain, float pan)
        {
            Mix(data, start, duration, gain, pan, (time, progress) =>
            {
                float envelope = Mathf.Clamp01(Mathf.Min(progress * 8f, (1f - progress) * 5f));
                float drift = Mathf.Sin(time * 0.78f) * 0.0025f;
                float fundamental = Mathf.Sin((frequency * (1f + drift)) * time * Mathf.PI * 2f);
                float octave = Mathf.Sin(frequency * 0.5f * time * Mathf.PI * 2f) * 0.20f;
                float warmth = Mathf.Sin(frequency * 2f * time * Mathf.PI * 2f) * 0.08f;
                return (fundamental + octave + warmth) * envelope * (0.90f + Mathf.Sin(time * 1.5f) * 0.10f);
            });
        }

        private static void MixBass(float[] data, float start, float duration, float frequency, float gain, float pan)
        {
            Mix(data, start, duration, gain, pan, (time, progress) =>
            {
                float envelope = Mathf.Pow(1f - progress, 1.9f);
                float body = Mathf.Sin(time * frequency * Mathf.PI * 2f);
                float sub = Mathf.Sin(time * frequency * 0.5f * Mathf.PI * 2f) * 0.36f;
                return (body + sub) * envelope;
            });
        }

        private static void MixCelesta(float[] data, float start, float duration, float frequency, float gain, float pan)
        {
            Mix(data, start, duration, gain, pan, (time, progress) =>
            {
                float envelope = Mathf.Sin(Mathf.Clamp01(progress * 18f) * Mathf.PI * 0.5f)
                    * Mathf.Pow(1f - progress, 2.05f);
                float carrier = Mathf.Sin(time * frequency * Mathf.PI * 2f);
                float bell = Mathf.Sin(time * frequency * 2.01f * Mathf.PI * 2f) * 0.34f;
                float glass = Mathf.Sin(time * frequency * 3.96f * Mathf.PI * 2f) * 0.12f;
                return (carrier + bell + glass) * envelope;
            });
        }

        private static void MixHarp(float[] data, float start, float duration, float frequency, float gain, float pan)
        {
            Mix(data, start, duration, gain, pan, (time, progress) =>
            {
                float envelope = Mathf.Pow(1f - progress, 2.8f);
                float body = Mathf.Sin(time * frequency * Mathf.PI * 2f)
                    + Mathf.Sin(time * frequency * 2f * Mathf.PI * 2f) * 0.17f;
                return body * envelope;
            });
        }

        private static void MixBrush(float[] data, float start, float duration, float gain, float pan)
        {
            Mix(data, start, duration, gain, pan, (time, progress) =>
            {
                float envelope = Mathf.Pow(1f - progress, 3.8f);
                return MusicalNoise(time * 7853.2f) * envelope;
            });
        }

        private static void MixPaperTick(float[] data, float start, float duration, float gain, float pan)
        {
            Mix(data, start, duration, gain, pan, (time, progress) =>
            {
                float envelope = Mathf.Pow(1f - progress, 5.5f);
                return (MusicalNoise(time * 16421.7f) + Mathf.Sin(time * 2100f * Mathf.PI * 2f) * 0.25f) * envelope;
            });
        }

        private static void Mix(float[] data, float start, float duration, float gain, float pan,
            System.Func<float, float, float> sample)
        {
            int frames = data.Length / 2;
            int begin = Mathf.Max(0, Mathf.FloorToInt(start * SampleRate));
            int end = Mathf.Min(frames, Mathf.CeilToInt((start + duration) * SampleRate));
            if (end <= begin) return;

            float leftGain = Mathf.Sqrt((1f - pan) * 0.5f) * gain;
            float rightGain = Mathf.Sqrt((1f + pan) * 0.5f) * gain;
            for (int frame = begin; frame < end; frame++)
            {
                float time = (frame - begin) / (float)SampleRate;
                float progress = (frame - begin) / (float)(end - begin);
                float value = sample(time, progress);
                int sampleIndex = frame * 2;
                data[sampleIndex] += value * leftGain;
                data[sampleIndex + 1] += value * rightGain;
            }
        }

        private static float MusicalNoise(float value)
        {
            return Mathf.Sin(value * 12.9898f) * 0.55f + Mathf.Sin(value * 78.233f) * 0.30f
                + Mathf.Sin(value * 37.719f) * 0.15f;
        }

        private static void SoftLimit(float[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                float value = data[i];
                data[i] = value / (1f + Mathf.Abs(value) * 0.82f);
            }
        }

        private static float MidiToHz(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);
    }
}
