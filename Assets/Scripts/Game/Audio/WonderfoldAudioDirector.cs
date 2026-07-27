using UnityEngine;

namespace Wonderfold.Game.Audio
{
    /// <summary>
    /// Owns the chapter's music bed. It prefers the authored, loopable chapter score in Resources and
    /// retains a self-contained procedural score only as a safe fallback for incomplete builds.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class WonderfoldAudioDirector : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const float LoopSeconds = 20f;

        private AudioSource _music;

        public static WonderfoldAudioDirector Create(Transform parent)
        {
            var go = new GameObject("Midnight Carnival Music", typeof(AudioSource));
            go.transform.SetParent(parent, false);
            var director = go.AddComponent<WonderfoldAudioDirector>();
            director.Initialise();
            return director;
        }

        private void Awake()
        {
            _music = GetComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.spatialBlend = 0f;
            _music.volume = 0.26f;
            _music.priority = 64;
        }

        private void Initialise()
        {
            if (_music == null) Awake();
            var authoredScore = Resources.Load<AudioClip>("Audio/midnight_carnival_theme");
            _music.clip = authoredScore != null ? authoredScore : MidnightCarnivalComposer.Compose();
            _music.Play();
        }

        private void OnDestroy()
        {
            // Resource-owned audio clips are unloaded by Unity. Only destroy the runtime fallback.
            if (_music != null && _music.clip != null && _music.clip.name == "Wonderfold Midnight Carnival Score")
                Destroy(_music.clip);
        }

        private static AudioClip ComposeMidnightCarnivalLoop()
        {
            int length = Mathf.CeilToInt(SampleRate * LoopSeconds);
            var data = new float[length];
            const float bar = 2.5f; // 96 bpm, four beats per bar.

            // D minor gives the carnival its midnight colour. The final tonic makes the loop resolve
            // cleanly back to the first bar instead of sounding like a generic menu drone.
            int[][] chords =
            {
                new[] { 50, 53, 57 }, // Dm
                new[] { 46, 50, 53 }, // Bb
                new[] { 53, 57, 60 }, // F
                new[] { 48, 52, 55 }, // C
                new[] { 50, 53, 57 },
                new[] { 46, 50, 53 },
                new[] { 45, 49, 52 }, // A major: a little storybook tension
                new[] { 50, 53, 57 }
            };

            for (int barIndex = 0; barIndex < chords.Length; barIndex++)
            {
                float start = barIndex * bar;
                int[] chord = chords[barIndex];
                for (int note = 0; note < chord.Length; note++)
                    MixPad(data, start, bar + 0.20f, MidiToHz(chord[note]), 0.022f);

                // A gentle calliope bass gives the ambience forward movement without fighting matches.
                for (int beat = 0; beat < 4; beat++)
                {
                    float beatStart = start + beat * (bar * 0.25f);
                    MixPluck(data, beatStart, 0.42f, MidiToHz(chord[0] - 12), 0.055f);
                    if (beat == 1 || beat == 3)
                        MixPluck(data, beatStart + 0.05f, 0.25f, MidiToHz(chord[1] + 12), 0.026f);
                }
            }

            // Eight short celesta phrases; the rhythm leaves air for UI and match feedback.
            int[][] melody =
            {
                new[] { 69, 72, 74, 77, 74, 72, 69, 65 },
                new[] { 70, 74, 77, 74, 70, 65, 67, 70 },
                new[] { 69, 72, 77, 81, 77, 72, 69, 65 },
                new[] { 67, 72, 76, 79, 76, 72, 67, 64 },
                new[] { 69, 72, 74, 77, 81, 77, 74, 72 },
                new[] { 70, 74, 77, 82, 77, 74, 70, 65 },
                new[] { 69, 73, 76, 81, 76, 73, 69, 64 },
                new[] { 69, 72, 77, 81, 77, 74, 72, 69 }
            };
            const float step = bar / 8f;
            for (int phrase = 0; phrase < melody.Length; phrase++)
            {
                for (int note = 0; note < melody[phrase].Length; note++)
                {
                    float offset = phrase * bar + note * step;
                    MixBell(data, offset, step * 1.55f, MidiToHz(melody[phrase][note]), 0.070f);
                    if ((note == 0 || note == 4) && phrase % 2 == 0)
                        MixBell(data, offset + 0.035f, step, MidiToHz(melody[phrase][note] + 12), 0.020f);
                }
            }

            // A small headroom clamp protects mobile speakers when the music and a booster combine.
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -0.78f, 0.78f);

            var clip = AudioClip.Create("Wonderfold Midnight Carnival Loop", length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void MixPad(float[] data, float start, float duration, float frequency, float gain)
        {
            Mix(data, start, duration, (time, progress) =>
            {
                float attackRelease = Mathf.Clamp01(Mathf.Min(progress * 9f, (1f - progress) * 6f));
                float wobble = 0.90f + Mathf.Sin(time * 1.4f) * 0.10f;
                return (Mathf.Sin(time * frequency * Mathf.PI * 2f)
                    + Mathf.Sin(time * frequency * 0.5f * Mathf.PI * 2f) * 0.18f) * attackRelease * wobble * gain;
            });
        }

        private static void MixPluck(float[] data, float start, float duration, float frequency, float gain)
        {
            Mix(data, start, duration, (time, progress) =>
            {
                float envelope = Mathf.Pow(1f - progress, 2.8f);
                float body = Mathf.Sin(time * frequency * Mathf.PI * 2f)
                    + Mathf.Sin(time * frequency * 2f * Mathf.PI * 2f) * 0.22f;
                return body * envelope * gain;
            });
        }

        private static void MixBell(float[] data, float start, float duration, float frequency, float gain)
        {
            Mix(data, start, duration, (time, progress) =>
            {
                float envelope = Mathf.Pow(1f - progress, 2.2f);
                float shimmer = Mathf.Sin(time * frequency * 2.01f * Mathf.PI * 2f) * 0.34f
                    + Mathf.Sin(time * frequency * 3.99f * Mathf.PI * 2f) * 0.14f;
                return (Mathf.Sin(time * frequency * Mathf.PI * 2f) + shimmer) * envelope * gain;
            });
        }

        private static void Mix(float[] data, float start, float duration, System.Func<float, float, float> sample)
        {
            int begin = Mathf.Max(0, Mathf.FloorToInt(start * SampleRate));
            int end = Mathf.Min(data.Length, Mathf.CeilToInt((start + duration) * SampleRate));
            if (end <= begin) return;

            for (int index = begin; index < end; index++)
            {
                float time = (index - begin) / (float)SampleRate;
                float progress = (index - begin) / (float)(end - begin);
                data[index] += sample(time, progress);
            }
        }

        private static float MidiToHz(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);
    }
}
