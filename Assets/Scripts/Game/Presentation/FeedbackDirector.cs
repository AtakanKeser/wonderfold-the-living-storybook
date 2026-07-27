using System.Collections;
using UnityEngine;
using Wonderfold.Core.Events;
using Wonderfold.Core.Primitives;
using Wonderfold.Game.Meta;

namespace Wonderfold.Game.Presentation
{
    /// <summary>
    /// A compact runtime feedback pass: ink motes, paper sparks, procedural tones and mobile haptics.
    /// It subscribes to the same event stream as the board view, keeping all effect timing descriptive
    /// instead of trying to infer gameplay from rendered tiles.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class FeedbackDirector : MonoBehaviour
    {
        private AudioSource _audio;
        private BoardView _board;
        private DioramaView _diorama;
        private float _lastHaptic;

        public static FeedbackDirector Create(Transform parent, BoardView board, DioramaView diorama = null)
        {
            var go = new GameObject("Feedback Director", typeof(AudioSource));
            go.transform.SetParent(parent, false);
            var director = go.AddComponent<FeedbackDirector>();
            director.Initialise(board, diorama);
            return director;
        }

        private void Awake()
        {
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.volume = 0.22f;
        }

        private void Initialise(BoardView board, DioramaView diorama)
        {
            _board = board;
            _diorama = diorama;
            _board.EventPlayed += OnEventPlayed;
        }

        private void OnDestroy()
        {
            if (_board != null) _board.EventPlayed -= OnEventPlayed;
        }

        private void OnEventPlayed(BoardEvent boardEvent)
        {
            if (_board == null || _board.Layout == null) return;
            _diorama?.ReactToBoardEvent(boardEvent, _board.Layout);
            switch (boardEvent)
            {
                case TilesClearedEvent cleared:
                    for (int i = 0; i < cleared.Cells.Count; i++)
                        Burst(_board.Layout.WorldOf(cleared.Cells[i].Coord),
                            cleared.Cause == ClearCause.Match ? new Color(1f, 0.88f, 0.55f) : new Color(0.82f, 0.55f, 1f), 4);
                    Tone(cleared.Cause == ClearCause.Match ? 560f : 420f, 0.075f, 0.085f,
                        cleared.Cause == ClearCause.Match ? 0.14f : 0.42f);
                    break;
                case BoosterCreatedEvent created:
                    Burst(_board.Layout.WorldOf(created.At.Coord), new Color(1f, 0.92f, 0.40f), 8);
                    Tone(760f, 0.13f, 0.13f, 0.18f);
                    break;
                case BoosterActivatedEvent activated:
                    Burst(_board.Layout.WorldOf(activated.At.Coord), new Color(1f, 0.56f, 0.30f), 14);
                    Tone(240f, 0.22f, 0.24f, 0.78f);
                    Haptic();
                    break;
                case BoosterComboEvent combo:
                    Burst(_board.Layout.WorldOf(combo.At.Coord), new Color(1f, 0.78f, 0.24f), 24);
                    Tone(330f, 0.31f, 0.30f, 1f);
                    Haptic();
                    break;
                case BoardFoldedEvent folded:
                    var centre = new Vector3(folded.Area.X + folded.Area.Width * 0.5f - _board.Layout.Width * 0.5f,
                        folded.Area.Y + folded.Area.Height * 0.5f - _board.Layout.Height * 0.5f, 0f) * _board.Layout.CellSize + _board.Layout.Origin;
                    Burst(centre, new Color(0.65f, 0.78f, 1f), 16);
                    Tone(180f, 0.20f, 0.18f, 0.55f);
                    Haptic();
                    break;
            }
        }

        private void Burst(Vector3 origin, Color colour, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Ink Mote", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                go.transform.position = origin + new Vector3(0f, 0f, -0.5f);
                go.transform.localScale = Vector3.one * Random.Range(0.035f, 0.085f);
                var renderer = go.GetComponent<SpriteRenderer>();
                renderer.sprite = ProceduralArt.Disc();
                renderer.color = colour;
                renderer.sortingOrder = 80;
                StartCoroutine(Mote(go.transform, renderer, Random.insideUnitCircle.normalized * Random.Range(0.35f, 1.15f)));
            }
        }

        private static IEnumerator Mote(Transform mote, SpriteRenderer renderer, Vector2 velocity)
        {
            var start = renderer.color;
            float duration = Random.Range(0.28f, 0.52f);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                mote.position += (Vector3)(velocity * Time.deltaTime);
                mote.localScale *= 0.985f;
                renderer.color = new Color(start.r, start.g, start.b, 1f - t);
                yield return null;
            }
            Destroy(mote.gameObject);
        }

        private void Tone(float frequency, float duration, float volume, float impact)
        {
            const int rate = 22050;
            int samples = Mathf.Max(1, Mathf.CeilToInt(rate * duration));
            var clip = AudioClip.Create("Wonderfold tone", samples, 1, rate, false);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float envelope = 1f - i / (float)samples;
                float clean = Mathf.Sin(t * frequency * Mathf.PI * 2f);
                float overtone = Mathf.Sin(t * frequency * 2.03f * Mathf.PI * 2f) * 0.24f;
                // Deterministic pseudo-noise turns a pure beep into paper/ink impact without an asset.
                float noise = Mathf.Sin((i + 1) * 12.9898f) * 0.5f + Mathf.Sin((i + 1) * 78.233f) * 0.5f;
                data[i] = (clean + overtone + noise * impact * 0.32f) * envelope * envelope;
            }
            clip.SetData(data, 0);
            _audio.PlayOneShot(clip, volume);
            Destroy(clip, duration + 0.1f);
        }

        private void Haptic()
        {
            if (Time.unscaledTime - _lastHaptic < 0.08f) return;
            _lastHaptic = Time.unscaledTime;
            Handheld.Vibrate();
        }
    }
}
