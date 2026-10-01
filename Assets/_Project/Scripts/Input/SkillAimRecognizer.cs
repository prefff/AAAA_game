using System;
using UnityEngine;

namespace Game.Input
{
    /// <summary>
    /// Раскладка кнопок скиллов на экране. Позиции заданы в пикселях канваса шириной <see cref="ReferenceWidth"/> от
    /// правого нижнего угла — так же масштабирует HUD (CanvasScaler, Match Width), поэтому касания и рисунок совпадают.
    /// </summary>
    public static class SkillButtonLayout
    {
        public const float ReferenceWidth = 1920f;
        public const int SlotCount = 3;
        /// <summary> Маска слотов, у которых есть скилл (бит slot). Пустой слот кнопкой не считается: касание уходит в удары. </summary>
        public const int AllSlots = (1 << SlotCount) - 1;

        public static bool HasSlot(int slotMask, int slot) => slot >= 0 && slot < SlotCount && (slotMask & (1 << slot)) != 0;

        public static float Scale(float screenWidth) => Mathf.Max(1f, screenWidth) / ReferenceWidth;

        public static Vector2 ToScreen(Vector2 offsetFromBottomRight, float screenWidth) =>
            new(screenWidth - offsetFromBottomRight.x * Scale(screenWidth), offsetFromBottomRight.y * Scale(screenWidth));

        public static Vector2 ButtonCenter(GestureSettings s, int slot, float screenWidth) =>
            ToScreen(s.SkillButtonOffsets[slot], screenWidth);

        public static float ButtonRadius(GestureSettings s, float screenWidth) => s.SkillButtonRadius * Scale(screenWidth);

        /// <summary> Кнопка скилла под точкой экрана; −1 — мимо (или слот пуст по slotMask). </summary>
        public static int HitButton(GestureSettings s, Vector2 screenPos, float screenWidth, int slotMask = AllSlots)
        {
            if (s.SkillButtonOffsets == null) return -1;
            float r = ButtonRadius(s, screenWidth);
            int count = Mathf.Min(SlotCount, s.SkillButtonOffsets.Length);
            for (int slot = 0; slot < count; slot++)
                if (HasSlot(slotMask, slot) && (screenPos - ButtonCenter(s, slot, screenWidth)).sqrMagnitude <= r * r) return slot;
            return -1;
        }

        public static bool InCancelZone(GestureSettings s, Vector2 screenPos, float screenWidth)
        {
            float r = s.SkillCancelRadius * Scale(screenWidth);
            return (screenPos - ToScreen(s.SkillCancelOffset, screenWidth)).sqrMagnitude <= r * r;
        }
    }

    /// <summary>
    /// Прицел скилла с кнопки (как в MLBB):
    ///   касание кнопки → прицел (каста ещё нет: боец бегает и дерётся, сколько угодно долго);
    ///   сдвиг пальца от центра кнопки → прицел (направление в мире × доля дальности), непрерывно, как джойстик;
    ///   сдвиг меньше мёртвой зоны → автоприцел (быстрый каст тапом);
    ///   отпускание → команда скилла по последнему прицелу;
    ///   отпускание в зоне отмены → ничего (мана и перезарядка не тратятся).
    /// Чистый C#: позиции, время и перевод в мир — снаружи, тестируется без сцены.
    /// </summary>
    public sealed class SkillAimRecognizer
    {
        private readonly GestureSettings _settings;
        private readonly Action<InputCommand> _output;
        private readonly Action<SkillAimInputEvent> _aimOutput;

        private int _slot = -1;
        private Vector2 _center;
        private Vector2 _aim;
        private bool _inCancel;

        /// <summary> Пикселей экрана в миллиметре (Screen.dpi / 25.4). </summary>
        public float PixelsPerMm { get; set; } = 160f / 25.4f;
        /// <summary> Ширина экрана в пикселях — для зоны отмены (раскладка масштабируется по ширине). </summary>
        public float ScreenWidth { get; set; } = SkillButtonLayout.ReferenceWidth;
        /// <summary> Экранное направление → мировое (XZ). По умолчанию «вверх по экрану» = +Z. </summary>
        public Func<Vector2, Vector2> ScreenToWorld { get; set; } = d => d;

        public bool IsAiming => _slot >= 0;
        public int Slot => _slot;
        public Vector2 Aim => _aim;
        public bool InCancelZone => _inCancel;

        public SkillAimRecognizer(GestureSettings settings, Action<InputCommand> output, Action<SkillAimInputEvent> aimOutput)
        {
            _settings = settings != null ? settings : throw new ArgumentNullException(nameof(settings));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _aimOutput = aimOutput ?? throw new ArgumentNullException(nameof(aimOutput));
        }

        public static CommandType CommandFor(int slot) => slot switch
        {
            0 => CommandType.Ability1,
            1 => CommandType.Ability2,
            _ => CommandType.Ultimate,
        };

        /// <summary> Палец коснулся кнопки slot с центром buttonCenter (пиксели экрана). </summary>
        public void Begin(int slot, Vector2 buttonCenter, double time)
        {
            if (IsAiming) Cancel(time);
            _slot = slot;
            _center = buttonCenter;
            _aim = Vector2.zero;
            _inCancel = false;
            _aimOutput(new SkillAimInputEvent(slot, SkillAimPhase.Held, _aim, false));
        }

        public void Move(Vector2 screenPos, double time)
        {
            if (!IsAiming) return;
            Track(screenPos);
            _aimOutput(new SkillAimInputEvent(_slot, SkillAimPhase.Held, _aim, _inCancel));
        }

        public void End(Vector2 screenPos, double time)
        {
            if (!IsAiming) return;
            Track(screenPos);
            int slot = _slot;
            _slot = -1;
            if (_inCancel)
            {
                _aimOutput(new SkillAimInputEvent(slot, SkillAimPhase.Canceled, _aim, true));
                return;
            }
            // Сначала прицел, потом команда: симуляция получит их в одном тике, скилл уйдёт по этому прицелу.
            _aimOutput(new SkillAimInputEvent(slot, SkillAimPhase.Released, _aim, false));
            _output(new InputCommand(CommandFor(slot), Vector2.zero, (float)time, time, time));
        }

        /// <summary> Касание прервано системой — прицел снимается без каста (непреднамеренный скилл хуже потерянного). </summary>
        public void Cancel(double time)
        {
            if (!IsAiming) return;
            int slot = _slot;
            _slot = -1;
            _aimOutput(new SkillAimInputEvent(slot, SkillAimPhase.Canceled, _aim, true));
        }

        private void Track(Vector2 screenPos)
        {
            _inCancel = SkillButtonLayout.InCancelZone(_settings, screenPos, ScreenWidth);
            var offset = screenPos - _center;
            float mm = offset.magnitude / Mathf.Max(0.01f, PixelsPerMm);
            if (mm < _settings.SkillAimDeadZoneMm)
            {
                _aim = Vector2.zero;
                return;
            }
            var world = ScreenToWorld(offset / offset.magnitude);
            if (world.sqrMagnitude < 1e-6f)
            {
                _aim = Vector2.zero;
                return;
            }
            _aim = world.normalized * Mathf.Min(1f, mm / Mathf.Max(0.01f, _settings.SkillAimRadiusMm));
        }
    }
}
