using Game.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Input
{
    /// <summary>
    /// Простой виртуальный джойстик для левой половины экрана.
    /// Вешается на UI Image (фон джойстика); ссылка _handle — на дочерний Image (рукоятка).
    /// Публикует MoveInputEvent через EventBus.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform _background;
        [SerializeField] private RectTransform _handle;
        [Tooltip("Радиус, на который может смещаться рукоятка от центра, в пикселях канваса.")]
        [SerializeField] private float _radius = 100f;
        [Tooltip("Мёртвая зона (0..1) — ниже этого значения движение не публикуется.")]
        [Range(0f, 1f)][SerializeField] private float _deadZone = 0.15f;

        private Vector2 _input;

        public Vector2 Direction => _input.magnitude < _deadZone ? Vector2.zero : _input;

        private void Reset()
        {
            _background = GetComponent<RectTransform>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_background == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _background, eventData.position, eventData.pressEventCamera, out var local);

            var clamped = Vector2.ClampMagnitude(local, _radius);
            if (_handle != null) _handle.anchoredPosition = clamped;

            _input = clamped / _radius;
            EventBus.Raise(new MoveInputEvent(Direction));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _input = Vector2.zero;
            if (_handle != null) _handle.anchoredPosition = Vector2.zero;
            EventBus.Raise(new MoveInputEvent(Vector2.zero));
        }
    }
}