using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Wonderfold.Game.UI
{
    /// <summary>
    /// Gives runtime-built uGUI buttons a consistent, tactile response. The views build their controls
    /// in code, so this keeps press, hover, disabled and focus feedback consistent across every screen.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class ButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        private Button _button;
        private RectTransform _rect;
        private Vector3 _restScale;
        private bool _pressed;
        private bool _hovered;

        public static void Apply(Button button)
        {
            if (button == null) return;
            if (button.GetComponent<ButtonFeedback>() == null) button.gameObject.AddComponent<ButtonFeedback>();
        }

        private void Awake()
        {
            _button = GetComponent<Button>();
            _rect = transform as RectTransform;
            _restScale = transform.localScale;

            // Preserve each button's authored colour while Selectable supplies a visible state change.
            _button.transition = Selectable.Transition.ColorTint;
            var colors = _button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.82f, 1f);
            colors.selectedColor = new Color(1.05f, 1.05f, 1.05f, 1f);
            colors.disabledColor = new Color(0.42f, 0.42f, 0.46f, 0.68f);
            colors.fadeDuration = 0.07f;
            _button.colors = colors;

            if (GetComponent<Shadow>() == null)
            {
                var shadow = gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0.02f, 0.01f, 0.07f, 0.42f);
                shadow.effectDistance = new Vector2(0f, -5f);
            }
        }

        private void OnEnable()
        {
            _pressed = false;
            _hovered = false;
            ApplyScale();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_button == null || !_button.interactable) return;
            _pressed = true;
            ApplyScale();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            ApplyScale();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_button == null || !_button.interactable) return;
            _hovered = true;
            ApplyScale();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pressed = false;
            _hovered = false;
            ApplyScale();
        }

        private void ApplyScale()
        {
            if (_rect == null) return;
            float scale = _pressed ? 0.94f : _hovered ? 1.025f : 1f;
            _rect.localScale = _restScale * scale;
        }
    }
}
