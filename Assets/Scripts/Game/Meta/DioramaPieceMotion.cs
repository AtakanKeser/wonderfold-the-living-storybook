using UnityEngine;

namespace Wonderfold.Game.Meta
{
    /// <summary>
    /// A small, prefab-free motion pass for restored pop-up pieces. It makes the authored art feel
    /// like part of a living paper scene without trying to fake a full skeletal animation from one image.
    /// </summary>
    public sealed class DioramaPieceMotion : MonoBehaviour
    {
        private Vector3 _basePosition;
        private float _phase;
        private bool _restored;
        private float _reactionStartedAt = float.NegativeInfinity;
        private float _reactionEndsAt = float.NegativeInfinity;
        private bool _isCarousel;
        private bool _isFerris;
        private bool _initialised;

        public void Initialise(string pieceId)
        {
            _basePosition = transform.localPosition;
            _isCarousel = pieceId != null && pieceId.ToLowerInvariant().Contains("carousel");
            _isFerris = pieceId != null && pieceId.ToLowerInvariant().Contains("ferris");

            int hash = 23;
            if (pieceId != null)
                for (int i = 0; i < pieceId.Length; i++) hash = hash * 37 + pieceId[i];
            _phase = Mathf.Abs(hash % 1000) * 0.013f;
            _initialised = true;
        }

        public void SetRestored(bool restored) => _restored = restored;

        public void ReactToRestoration()
        {
            _reactionStartedAt = Time.time;
            _reactionEndsAt = _reactionStartedAt + 1.15f;
        }

        private void Update()
        {
            if (!_initialised || !_restored) return;

            float time = Time.time + _phase;
            float reaction = ReactionStrength();
            float bob = Mathf.Sin(time * 1.25f) * 0.022f + reaction * 0.08f;
            float idleTilt = _isCarousel
                ? Mathf.Sin(time * 1.9f) * 1.05f
                : _isFerris ? Mathf.Sin(time * 0.72f) * 0.42f : Mathf.Sin(time) * 0.65f;
            float celebration = reaction > 0f ? Mathf.Sin((Time.time - _reactionStartedAt) * 15f) * reaction * 2.2f : 0f;

            transform.localPosition = _basePosition + new Vector3(0f, bob, 0f);
            transform.localRotation = Quaternion.Euler(0f, 0f, idleTilt + celebration);
        }

        private float ReactionStrength()
        {
            if (Time.time >= _reactionEndsAt) return 0f;
            float duration = Mathf.Max(0.01f, _reactionEndsAt - _reactionStartedAt);
            return Mathf.Sin(Mathf.Clamp01((Time.time - _reactionStartedAt) / duration) * Mathf.PI);
        }
    }
}
