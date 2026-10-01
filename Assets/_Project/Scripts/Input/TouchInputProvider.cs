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
    ///
    /// Касание кнопки скилла (правый нижний угол, <see cref="SkillButtonLayout"/>) уходит в <see cref="SkillAimRecognizer"/>
    /// и ударом не становится. В редакторе кнопки нажимаются ЛКМ, быстрый каст — клавиши 1/2/3 (или Q/E/R).
    /// </summary>
    [DefaultExecutionOrder(-2000)]
    public class TouchInputProvider : MonoBehaviour
    {
        [Tooltip("Пороги жестов. Пусто — Resources/Input/GestureSettings или значения по умолчанию.")]
        [SerializeField] private GestureSettings _settings;

        /// <summary> DPI, если устройство его не сообщает (Screen.dpi = 0). </summary>
        private const float FallbackDpi = 160f;

        private GestureRecognizer _recognizer;
        private SkillAimRecognizer _skills;
        // Палец, которым сейчас рисуется жест. Остальные пальцы в правой зоне — «второй палец», на джойстике — игнор.
        private ETouch.Finger _gestureFinger;
        // Палец на кнопке скилла (один каст за раз; второй палец на кнопках игнорируется).
        private ETouch.Finger _skillFinger;
        private bool _mouseOnSkill;

        public GestureRecognizer Recognizer => _recognizer;
        public SkillAimRecognizer Skills => _skills;
        public GestureSettings Settings => _settings;

        /// <summary>
        /// Слоты, где у своего бойца есть скилл (<see cref="SkillButtonLayout.HasSlot"/>). Задаёт сборка арены по персонажу:
        /// на месте пустого слота кнопки нет, и касание там — обычный жест удара.
        /// </summary>
        public int SkillSlotMask { get; set; } = SkillButtonLayout.AllSlots;

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
            _skills = new SkillAimRecognizer(_settings, cmd => EventBus.Raise(new CommandInputEvent(cmd)), e => EventBus.Raise(e))
            {
                ScreenToWorld = InputSpace.ScreenToWorld,
            };
        }

        private void OnEnable()
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : FallbackDpi;
            _recognizer.PixelsPerMm = dpi / 25.4f;
            _skills.PixelsPerMm = dpi / 25.4f;
            _skills.ScreenWidth = Screen.width;

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
            if (_skills.IsAiming) _skills.Cancel(Time.realtimeSinceStartupAsDouble);
            _gestureFinger = null;
            _skillFinger = null;
            _mouseOnSkill = false;
        }

        private void Update()
        {
            _skills.ScreenWidth = Screen.width; // поворот экрана / смена разрешения в редакторе
#if UNITY_EDITOR || UNITY_STANDALONE
            // Если EnhancedTouch видит реальные касания, не дублируем их мышью.
            if (ETouch.Touch.activeTouches.Count == 0) EmulateWithMouse();
            QuickCastFromKeyboard();
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
                if (TryBeginSkill(pos, time)) _mouseOnSkill = true;
                else if (IsRightZone(pos)) _recognizer.TouchBegan(pos, time);
            }
            else if (mouse.leftButton.isPressed)
            {
                if (_mouseOnSkill) _skills.Move(pos, time);
                else _recognizer.TouchMoved(pos, time);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (_mouseOnSkill)
                {
                    _mouseOnSkill = false;
                    _skills.End(pos, time);
                }
                else _recognizer.TouchEnded(pos, time);
            }

            if (mouse.rightButton.wasPressedThisFrame && IsRightZone(pos))
                _recognizer.SecondaryTouchBegan(time);
        }

        /// <summary> Быстрый каст с клавиатуры в редакторе: нажатие и отпускание кнопки без прицела (автоприцел). </summary>
        private void QuickCastFromKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null || _skills.IsAiming) return;
            int slot = -1;
            if (kb.digit1Key.wasPressedThisFrame || kb.qKey.wasPressedThisFrame) slot = 0;
            else if (kb.digit2Key.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) slot = 1;
            else if (kb.digit3Key.wasPressedThisFrame || kb.rKey.wasPressedThisFrame) slot = 2;
            if (!SkillButtonLayout.HasSlot(SkillSlotMask, slot)) return;
            double time = kb.lastUpdateTime;
            var center = SkillButtonLayout.ButtonCenter(_settings, slot, Screen.width);
            _skills.Begin(slot, center, time);
            _skills.End(center, time);
        }

        private bool TryBeginSkill(Vector2 screenPos, double time)
        {
            int slot = SkillButtonLayout.HitButton(_settings, screenPos, Screen.width, SkillSlotMask);
            if (slot < 0) return false;
            if (!_skills.IsAiming) _skills.Begin(slot, SkillButtonLayout.ButtonCenter(_settings, slot, Screen.width), time);
            return true; // кнопка занята другим пальцем — касание всё равно не удар
        }

        private bool IsRightZone(Vector2 screenPos) =>
            screenPos.x / Mathf.Max(1f, Screen.width) >= _settings.GestureZoneMinX;

        private void OnFingerDown(ETouch.Finger finger)
        {
            var pos = finger.screenPosition;
            var touch = finger.lastTouch;
            if (SkillButtonLayout.HitButton(_settings, pos, Screen.width, SkillSlotMask) >= 0)
            {
                bool free = _skillFinger == null;
                if (TryBeginSkill(pos, touch.startTime) && free) _skillFinger = finger;
                return;
            }
            if (!IsRightZone(pos)) return;

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
            if (finger == _skillFinger)
            {
                _skills.Move(finger.screenPosition, finger.lastTouch.time);
                return;
            }
            if (finger != _gestureFinger) return;
            _recognizer.TouchMoved(finger.screenPosition, finger.lastTouch.time);
        }

        private void OnFingerUp(ETouch.Finger finger)
        {
            if (finger == _skillFinger)
            {
                _skillFinger = null;
                _skills.End(finger.screenPosition, finger.lastTouch.time);
                return;
            }
            if (finger != _gestureFinger) return;
            _gestureFinger = null;
            _recognizer.TouchEnded(finger.screenPosition, finger.lastTouch.time);
        }
    }
}
