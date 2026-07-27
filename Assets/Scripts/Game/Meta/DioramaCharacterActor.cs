using System.Collections.Generic;
using UnityEngine;
using Wonderfold.Game.Presentation;

namespace Wonderfold.Game.Meta
{
    /// <summary>
    /// Gives a single authored character cutout a role-specific performance without coupling the
    /// production art to an Animator Controller or a prefab. The art can be replaced at any time;
    /// timing, reaction and idle language stay consistent across the cast.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class DioramaCharacterActor : MonoBehaviour
    {
        public enum Role
        {
            Mira,
            Quill,
            Folio,
            Luna,
            Blank
        }

        private readonly List<SpriteRenderer> _sparkles = new List<SpriteRenderer>();
        private Transform _visual;
        private SpriteRenderer _body;
        private SpriteRenderer _aura;
        private SpriteRenderer _contactShadow;
        private AudioSource _voice;
        private Vector3 _basePosition;
        private float _baseScale;
        private float _phase;
        private float _reactionStartedAt = float.NegativeInfinity;
        private float _reactionEndsAt = float.NegativeInfinity;
        private float _reactionIntensity;
        private Vector3 _reactionFocus;
        private int _restingSortingOrder;
        private bool _bringForwardOnReaction;
        private Role _role;
        private bool _initialised;

        public static DioramaCharacterActor Create(Transform parent, string name, Role role, Vector3 position,
            Sprite authoredSprite, Color fallbackColour, int sortingOrder, float scale)
        {
            var root = new GameObject(name, typeof(AudioSource)).transform;
            root.SetParent(parent, false);
            root.localPosition = position;

            var actor = root.gameObject.AddComponent<DioramaCharacterActor>();
            actor.Initialise(name, role, authoredSprite, fallbackColour, sortingOrder, scale);
            return actor;
        }

        private void Awake()
        {
            _voice = GetComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.spatialBlend = 0f;
            _voice.volume = 0.20f;
            _voice.priority = 96;
        }

        public void ReactToRestoration()
        {
            _reactionStartedAt = Time.time;
            _reactionEndsAt = _reactionStartedAt + (_role == Role.Blank ? 0.72f : 1.08f);
            _reactionIntensity = 1f;
            _reactionFocus = _basePosition + Vector3.up * 0.8f;
        }

        /// <summary>
        /// A gameplay reaction is deliberately independent of an Animator Controller. Quill can dart
        /// toward the match, Mira leans in, and the supporting cast celebrate without the game logic
        /// needing to know anything about animation states.
        /// </summary>
        public void ReactToGameplay(Vector3 worldFocus, float intensity)
        {
            _reactionStartedAt = Time.time;
            _reactionEndsAt = _reactionStartedAt + Mathf.Lerp(0.42f, 0.94f, Mathf.Clamp01(intensity));
            _reactionIntensity = Mathf.Clamp01(intensity);
            _reactionFocus = transform.parent != null ? transform.parent.InverseTransformPoint(worldFocus) : worldFocus;
            _reactionFocus.z = _basePosition.z;
        }

        /// <summary>Lets the player acknowledge a character without turning a touch into a board move.</summary>
        public bool TryReactToTouch(Vector3 worldPoint)
        {
            if (!isActiveAndEnabled || _body == null) return false;
            // The diorama deliberately sits on a deeper Z layer than the puzzle. ScreenToWorldPoint
            // resolves touches on the board plane, so a full 3D Bounds.Contains check would reject
            // every otherwise valid character tap. Character art is a 2D hit target.
            var bounds = _body.bounds;
            bool inside = worldPoint.x >= bounds.min.x && worldPoint.x <= bounds.max.x
                && worldPoint.y >= bounds.min.y && worldPoint.y <= bounds.max.y;
            if (!inside) return false;

            ReactToGameplay(worldPoint, 0.90f);
            PlayTouchVoice();
            Handheld.Vibrate();
            return true;
        }

        /// <summary>Moves the actor without breaking its procedural idle offset.</summary>
        public void SetStagePosition(Vector3 position)
        {
            _basePosition = position;
            transform.localPosition = position;
        }

        /// <summary>Updates a role's scale and render layer when the board changes shape.</summary>
        public void SetStageAppearance(float scale, int sortingOrder, bool bringForwardOnReaction = false)
        {
            _baseScale = scale;
            _restingSortingOrder = sortingOrder;
            _bringForwardOnReaction = bringForwardOnReaction;
            ApplySorting(sortingOrder);
            UpdateContactShadow(0f);
        }

        public void SetStageVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        private void Initialise(string name, Role role, Sprite authoredSprite, Color fallbackColour,
            int sortingOrder, float scale)
        {
            _role = role;
            _basePosition = transform.localPosition;
            _baseScale = scale;
            _restingSortingOrder = sortingOrder;

            int hash = 17;
            for (int i = 0; i < name.Length; i++) hash = hash * 31 + name[i];
            _phase = Mathf.Abs(hash % 1000) * 0.017f;

            var auraObject = new GameObject("Story Glow", typeof(SpriteRenderer));
            auraObject.transform.SetParent(transform, false);
            auraObject.transform.localScale = Vector3.one * 1.65f;
            _aura = auraObject.GetComponent<SpriteRenderer>();
            _aura.sprite = ProceduralArt.Disc();
            _aura.sortingOrder = sortingOrder - 1;

            var shadowObject = new GameObject("Page Contact Shadow", typeof(SpriteRenderer));
            shadowObject.transform.SetParent(transform, false);
            _contactShadow = shadowObject.GetComponent<SpriteRenderer>();
            _contactShadow.sprite = ProceduralArt.Disc();
            _contactShadow.color = new Color(0.02f, 0.01f, 0.07f, 0.26f);
            _contactShadow.sortingOrder = sortingOrder - 2;

            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);
            _visual.localScale = Vector3.one * scale;
            _body = AddSprite(_visual, "Art", authoredSprite ?? ProceduralArt.PaperTile(), sortingOrder);
            _body.color = authoredSprite != null ? Color.white : fallbackColour;

            int sparkleCount = role == Role.Blank ? 4 : 3;
            for (int i = 0; i < sparkleCount; i++)
            {
                var sparkle = AddSprite(transform, $"Ink Spark {i}", ProceduralArt.Disc(), sortingOrder + 1);
                sparkle.transform.localScale = Vector3.one * (0.055f + i * 0.014f);
                _sparkles.Add(sparkle);
            }

            _initialised = true;
            UpdateContactShadow(0f);
        }

        private static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, int sortingOrder)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private void ApplySorting(int bodyOrder)
        {
            if (_body != null) _body.sortingOrder = bodyOrder;
            if (_aura != null) _aura.sortingOrder = bodyOrder - 1;
            if (_contactShadow != null) _contactShadow.sortingOrder = bodyOrder - 2;
            for (int i = 0; i < _sparkles.Count; i++)
                if (_sparkles[i] != null) _sparkles[i].sortingOrder = bodyOrder + 1;
        }

        /// <summary>A soft oval at the character's feet makes a cutout read as standing on the paper.</summary>
        private void UpdateContactShadow(float reaction)
        {
            if (_contactShadow == null) return;
            float width = _baseScale * (_role == Role.Quill ? 0.36f : 0.43f);
            _contactShadow.transform.localPosition = new Vector3(0f, -_baseScale * 0.36f, 0.01f);
            _contactShadow.transform.localScale = new Vector3(width, width * 0.20f * (1f - reaction * 0.18f), 1f);
            _contactShadow.color = new Color(0.02f, 0.01f, 0.07f, 0.22f + reaction * 0.08f);
        }

        private void PlayTouchVoice()
        {
            if (_voice == null) return;

            const int sampleRate = 22050;
            const float duration = 0.34f;
            int count = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[count];
            float start = _role == Role.Quill ? 620f : 390f;
            float end = _role == Role.Quill ? 1050f : 640f;
            float phase = 0f;

            for (int i = 0; i < count; i++)
            {
                float progress = i / (float)count;
                float frequency = Mathf.Lerp(start, end, Mathf.SmoothStep(0f, 1f, progress));
                phase += frequency / sampleRate;
                float envelope = Mathf.Sin(Mathf.Clamp01(progress * 7f) * Mathf.PI * 0.5f)
                    * Mathf.Pow(1f - progress, 1.45f);
                float tone = Mathf.Sin(phase * Mathf.PI * 2f)
                    + Mathf.Sin(phase * Mathf.PI * 4.04f) * 0.22f
                    + Mathf.Sin(phase * Mathf.PI * 6.08f) * 0.08f;
                data[i] = tone * envelope * 0.52f;
            }

            var clip = AudioClip.Create($"{_role} touch", count, 1, sampleRate, false);
            clip.SetData(data, 0);
            _voice.PlayOneShot(clip, _role == Role.Quill ? 0.32f : 0.26f);
            Destroy(clip, duration + 0.1f);
        }

        private void Update()
        {
            if (!_initialised) return;

            float time = Time.time + _phase;
            IdleProfile(time, out float bob, out float drift, out float tilt, out float breathe, out float aura);

            float reactionProgress = ReactionProgress();
            float reaction = ReactionStrength() * _reactionIntensity;
            float reactionWave = reaction > 0f
                ? Mathf.Sin((Time.time - _reactionStartedAt) * (_role == Role.Quill ? 19f : 13f)) * reaction
                : 0f;

            if (_role == Role.Blank)
            {
                drift += reaction * 0.18f;
                tilt += reactionWave * 8f;
                breathe -= reaction * 0.10f;
                _body.color = new Color(1f, 1f, 1f, 0.80f + (1f - reaction) * 0.20f);
            }

            Vector3 response = GameplayOffset(reactionProgress, reaction);
            transform.localPosition = _basePosition + new Vector3(drift, bob, 0f) + response;
            transform.localRotation = Quaternion.Euler(0f, 0f, tilt + reactionWave * 2.5f);
            _visual.localScale = Vector3.one * _baseScale * (1f + breathe + reactionWave * 0.06f + reaction * 0.055f);
            ApplySorting(_bringForwardOnReaction && reaction > 0.01f
                ? _restingSortingOrder + 20
                : _restingSortingOrder);
            UpdateContactShadow(reaction);

            Color glow = GlowColour();
            _aura.color = new Color(glow.r, glow.g, glow.b, aura + reaction * 0.22f);
            _aura.transform.localScale = Vector3.one * (1.48f + breathe * 5f + reaction * 0.36f);

            for (int i = 0; i < _sparkles.Count; i++)
            {
                float orbit = time * (0.78f + i * 0.13f) + i * 2.1f;
                float radius = 0.42f + i * 0.11f;
                var sparkle = _sparkles[i];
                sparkle.transform.localPosition = new Vector3(Mathf.Cos(orbit) * radius,
                    0.45f + Mathf.Sin(orbit * 1.37f) * radius, -0.02f);
                float alpha = 0.26f + Mathf.Sin(orbit * 2.1f) * 0.18f + reaction * 0.38f;
                sparkle.color = new Color(glow.r, glow.g, glow.b, Mathf.Clamp01(alpha));
            }
        }

        private void IdleProfile(float time, out float bob, out float drift, out float tilt, out float breathe,
            out float aura)
        {
            bob = 0f;
            drift = 0f;
            tilt = 0f;
            breathe = 0f;
            aura = 0.11f;

            switch (_role)
            {
                case Role.Mira:
                    bob = Mathf.Sin(time * 2.0f) * 0.045f;
                    tilt = Mathf.Sin(time * 1.5f) * 1.4f;
                    breathe = Mathf.Sin(time * 2.0f) * 0.012f;
                    aura = 0.14f;
                    break;
                case Role.Quill:
                    bob = Mathf.Abs(Mathf.Sin(time * 3.2f)) * 0.105f;
                    drift = Mathf.Sin(time * 1.6f) * 0.045f;
                    tilt = Mathf.Sin(time * 3.2f) * 4.2f;
                    breathe = Mathf.Sin(time * 3.2f) * 0.040f;
                    aura = 0.18f;
                    break;
                case Role.Folio:
                    bob = Mathf.Sin(time * 1.35f) * 0.030f;
                    tilt = Mathf.Sin(time * 0.95f) * 0.8f;
                    breathe = Mathf.Sin(time * 1.35f) * 0.008f;
                    aura = 0.10f;
                    break;
                case Role.Luna:
                    bob = Mathf.Sin(time * 1.75f) * 0.055f;
                    drift = Mathf.Sin(time * 0.82f) * 0.025f;
                    tilt = Mathf.Sin(time * 1.3f) * 1.8f;
                    breathe = Mathf.Sin(time * 1.75f) * 0.015f;
                    aura = 0.16f;
                    break;
                case Role.Blank:
                    bob = Mathf.Sin(time * 1.10f) * 0.115f;
                    drift = Mathf.Sin(time * 0.63f) * 0.065f;
                    tilt = Mathf.Sin(time * 0.82f) * 2.5f;
                    breathe = Mathf.Sin(time * 1.1f) * 0.025f;
                    aura = 0.07f + Mathf.Sin(time * 2.5f) * 0.025f;
                    break;
            }
        }

        private float ReactionStrength()
        {
            if (Time.time >= _reactionEndsAt) return 0f;
            return Mathf.Sin(ReactionProgress() * Mathf.PI);
        }

        private float ReactionProgress()
        {
            if (Time.time >= _reactionEndsAt) return 1f;
            float duration = Mathf.Max(0.01f, _reactionEndsAt - _reactionStartedAt);
            return Mathf.Clamp01((Time.time - _reactionStartedAt) / duration);
        }

        private Vector3 GameplayOffset(float progress, float reaction)
        {
            if (reaction <= 0f) return Vector3.zero;

            Vector3 toFocus = _reactionFocus - _basePosition;
            toFocus.z = 0f;
            float arc = Mathf.Sin(progress * Mathf.PI);

            switch (_role)
            {
                case Role.Quill:
                    // Quill is the living cursor of Wonderfold: a successful match makes him briefly
                    // fly into the page before returning to his perch.
                    return Vector3.ClampMagnitude(toFocus, 4.5f) * (arc * reaction * 0.56f)
                        + Vector3.up * (Mathf.Sin(progress * Mathf.PI) * reaction * 0.45f);
                case Role.Mira:
                    return new Vector3(Mathf.Sign(toFocus.x) * arc * reaction * 0.24f,
                        Mathf.Sin(progress * Mathf.PI) * reaction * 0.28f, 0f);
                case Role.Luna:
                    return Vector3.up * (arc * reaction * 0.34f);
                case Role.Folio:
                    return new Vector3(Mathf.Sign(toFocus.x) * arc * reaction * 0.10f,
                        arc * reaction * 0.12f, 0f);
                case Role.Blank:
                    return -Vector3.up * (arc * reaction * 0.30f);
                default:
                    return Vector3.zero;
            }
        }

        private Color GlowColour()
        {
            switch (_role)
            {
                case Role.Quill: return new Color(1f, 0.66f, 0.23f);
                case Role.Folio: return new Color(1f, 0.76f, 0.34f);
                case Role.Luna: return new Color(0.63f, 0.58f, 1f);
                case Role.Blank: return new Color(0.70f, 0.80f, 1f);
                default: return new Color(0.38f, 0.78f, 1f);
            }
        }
    }
}
