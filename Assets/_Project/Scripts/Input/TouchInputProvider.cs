using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

namespace Game.Input
{
    /// <summary>
    /// Мост между Unity Input System (EnhancedTouch) и GestureRecognizer.
    /// На Android/iOS использует реальные касания; в редакторе — эмуляция мышью.
    /// Один компонент на сцену. Ссылку на GestureRecognizer выставить в инспекторе.
    /// </summary>
    public class TouchInputProvider : MonoBehaviour
    {
        [SerializeField] private GestureRecognizer _gestureRecognizer;

        [Tooltip("Левая половина (где джойстик) обрабатывается отдельно через UI EventSystem, не через этот провайдер.")]
        [Range(0f, 1f)] public float IgnoreLeftBelowX = 0.5f;

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            ETouch.Touch.onFingerDown += OnFingerDown;
            ETouch.Touch.onFingerMove += OnFingerMove;
            ETouch.Touch.onFingerUp += OnFingerUp;
        }

        private void OnDisable()
        {
            ETouch.Touch.onFingerDown -= OnFingerDown;
            ETouch.Touch.onFingerMove -= OnFingerMove;
            ETouch.Touch.onFingerUp -= OnFingerUp;
            EnhancedTouchSupport.Disable();
        }

        private void Update()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            // Эмуляция мышью в редакторе: ЛКМ работает как один палец.
            if (_gestureRecognizer == null || Mouse.current == null) return;
            var pos = Mouse.current.position.ReadValue();
            if (Mouse.current.leftButton.wasPressedThisFrame)
                _gestureRecognizer.OnTouchBegan(pos);
            else if (Mouse.current.leftButton.isPressed)
                _gestureRecognizer.OnTouchMoved(pos);
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
                _gestureRecognizer.OnTouchEnded(pos);
#endif
        }

        private bool IsRightZone(Vector2 screenPos) =>
            screenPos.x / Mathf.Max(1f, Screen.width) >= IgnoreLeftBelowX;

        private void OnFingerDown(ETouch.Finger finger)
        {
            if (_gestureRecognizer == null) return;
            var pos = finger.screenPosition;
            if (!IsRightZone(pos)) return;
            _gestureRecognizer.OnTouchBegan(pos);
        }

        private void OnFingerMove(ETouch.Finger finger)
        {
            if (_gestureRecognizer == null) return;
            _gestureRecognizer.OnTouchMoved(finger.screenPosition);
        }

        private void OnFingerUp(ETouch.Finger finger)
        {
            if (_gestureRecognizer == null) return;
            _gestureRecognizer.OnTouchEnded(finger.screenPosition);
        }
    }
}