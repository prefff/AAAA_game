using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

namespace Game.Input
{
    /// <summary>
    /// Мост между Unity Input System (EnhancedTouch) и <see cref="GestureRecognizer"/>.
    /// На Android/iOS — реальные касания; в редакторе — эмуляция мышью (ЛКМ — основной палец, ПКМ — второй палец).
    /// Один компонент на сцену; распознанные команды публикуются в EventBus как <see cref="CommandInputEvent"/>.
    ///
    /// Касания приходят колбэками EnhancedTouch во время обновления Input System — в начале кадра, до Update.
    /// Эмуляция мышью и таймер удержания идут в Update, поэтому порядок выполнения — самый ранний среди игровых скриптов.
    /// В распознаватель передаётся время самого события (Touch.startTime / Touch.time), а не время кадра.
    /// </summary>
    [DefaultExecutionOrder(-2000)]
    public class TouchInputProvider : MonoBehaviour
    {
        [Tooltip("Пороги жестов. Пусто — Resources/Input/GestureSettings или значения по умолчанию.")]
        [SerializeField] private GestureSettings _settings;

        /// <summary> DPI, если устройство его не сообщает (Screen.dpi = 0). </summary>
        private const float FallbackDpi = 160f;

        private GestureRecognizer _recognizer;
        // Палец, которым сейчас рисуется жест. Остальные пальцы в правой зоне — «второй палец», на джойстике — игнор.
        private ETouch.Finger _gestureFinger;

        public GestureRecognizer Recognizer => _recognizer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ConfigureInputUpdate()
        {
            // События ввода обрабатываются один раз в кадр, до Update (а не в FixedUpdate, где они ждали бы шага физики).
            // Это и так значение по умолчанию; фиксируем явно, чтобы случайная смена настройки не добавила задержку.
            var settings = InputSystem.settings;
            if (settings != null && settings.updateMode != InputSettings.UpdateMode.ProcessEventsInDynamicUpdate)
                settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        }

        private void Awake()
        {
            if (_settings == null) _settings = GestureSettings.LoadOrDefault();
            _recognizer = new GestureRecognizer(_settings, cmd => EventBus.Raise(new CommandInputEvent(cmd)))
            {
                ScreenToWorld = InputSpace.ScreenToWorld,
            };
        }

        private void OnEnable()
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : FallbackDpi;
            _recognizer.PixelsPerMm = dpi / 25.4f;

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

            if (_recognizer.IsTracking) _recognizer.TouchCanceled(Time.realtimeSinceStartupAsDouble);
            _gestureFinger = null;
        }

        private void Update()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            // Если EnhancedTouch видит реальные касания, не дублируем их мышью.
            if (ETouch.Touch.activeTouches.Count == 0) EmulateWithMouse();
#endif
            // Удержание неподвижного пальца событий не присылает — проверяем порог каждый кадр, до Update бойцов.
            if (_recognizer.IsTracking) _recognizer.Update(Time.realtimeSinceStartupAsDouble);
        }

        private void EmulateWithMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            var pos = mouse.position.ReadValue();
            // Время последнего события мыши — та же шкала, что и у касаний (точнее времени кадра).
            double time = mouse.lastUpdateTime;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (IsRightZone(pos)) _recognizer.TouchBegan(pos, time);
            }
            else if (mouse.leftButton.isPressed)
                _recognizer.TouchMoved(pos, time);
            else if (mouse.leftButton.wasReleasedThisFrame)
                _recognizer.TouchEnded(pos, time);

            if (mouse.rightButton.wasPressedThisFrame && IsRightZone(pos))
                _recognizer.SecondaryTouchBegan(time);
        }

        private bool IsRightZone(Vector2 screenPos) =>
            screenPos.x / Mathf.Max(1f, Screen.width) >= _settings.GestureZoneMinX;

        private void OnFingerDown(ETouch.Finger finger)
        {
            var pos = finger.screenPosition;
            if (!IsRightZone(pos)) return;
            var touch = finger.lastTouch;

            if (_gestureFinger == null)
            {
                _gestureFinger = finger;
                _recognizer.TouchBegan(pos, touch.startTime);
            }
            else
            {
                _recognizer.SecondaryTouchBegan(touch.startTime);
            }
        }

        private void OnFingerMove(ETouch.Finger finger)
        {
            if (finger != _gestureFinger) return;
            _recognizer.TouchMoved(finger.screenPosition, finger.lastTouch.time);
        }

        private void OnFingerUp(ETouch.Finger finger)
        {
            if (finger != _gestureFinger) return;
            _gestureFinger = null;
            _recognizer.TouchEnded(finger.screenPosition, finger.lastTouch.time);
        }
    }
}
