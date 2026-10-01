using Game.Characters;
using Game.Core;
using Game.Input;
using Game.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Кнопки скиллов у правого нижнего угла и зона отмены — только рисунок. Касания ловит
    /// <see cref="TouchInputProvider"/> по той же раскладке (<see cref="SkillButtonLayout"/>), поэтому Raycast у картинок
    /// выключен: кнопка не должна съедать касание у распознавателя. Состояние (перезарядка, мана) читается из симуляции.
    /// </summary>
    public class SkillBar : MonoBehaviour
    {
        private static readonly Color Ready = new(0.25f, 0.55f, 0.95f, 0.85f);
        private static readonly Color UltReady = new(0.95f, 0.6f, 0.15f, 0.9f);
        private static readonly Color NoMana = new(0.3f, 0.3f, 0.38f, 0.75f);
        private static readonly Color Pressed = new(1f, 1f, 1f, 0.95f);

        private MatchRunner _runner;
        private GestureSettings _settings;
        private readonly Image[] _bg = new Image[SkillButtonLayout.SlotCount];
        private readonly Image[] _cooldown = new Image[SkillButtonLayout.SlotCount];
        private readonly Text[] _label = new Text[SkillButtonLayout.SlotCount];
        private readonly Text[] _timer = new Text[SkillButtonLayout.SlotCount];
        private Image _cancel;
        private int _pressedSlot = -1;

        public bool CancelVisible => _cancel != null && _cancel.gameObject.activeSelf;
        public float CooldownFill(int slot) => _cooldown[slot] != null ? _cooldown[slot].fillAmount : 0f;
        public bool IsDimmed(int slot) => _bg[slot] != null && _bg[slot].color == NoMana;

        public void Bind(MatchRunner runner, GestureSettings settings)
        {
            _runner = runner;
            _settings = settings;
        }

        private void Start()
        {
            var spec = _runner.Sim.Setup.Fighters[_runner.LocalPlayer];
            var definition = _runner.LocalDefinition;
            float size = _settings.SkillButtonRadius * 2f;

            for (int slot = 0; slot < SkillButtonLayout.SlotCount; slot++)
            {
                var sk = spec.Skill(slot);
                if (sk == null || slot >= _settings.SkillButtonOffsets.Length) continue;
                var offset = _settings.SkillButtonOffsets[slot];
                var rt = UiFactory.Rect($"Skill{slot + 1}", transform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f),
                    new Vector2(-offset.x, offset.y), new Vector2(size, size));
                _bg[slot] = rt.gameObject.AddComponent<Image>();
                _bg[slot].sprite = UiFactory.CircleSprite();
                _bg[slot].raycastTarget = false;

                var cdRt = UiFactory.Rect("Cooldown", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
                _cooldown[slot] = cdRt.gameObject.AddComponent<Image>();
                _cooldown[slot].sprite = UiFactory.CircleSprite();
                _cooldown[slot].color = new Color(0f, 0f, 0f, 0.6f);
                _cooldown[slot].type = Image.Type.Filled;
                _cooldown[slot].fillMethod = Image.FillMethod.Radial360;
                _cooldown[slot].fillOrigin = (int)Image.Origin360.Top;
                _cooldown[slot].fillClockwise = false;
                _cooldown[slot].raycastTarget = false;

                var data = definition != null ? definition.Skill(slot) : null;
                if (data != null && data.Icon != null)
                {
                    float iconSize = size * 0.7f;
                    var iconRt = UiFactory.Rect("Icon", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));
                    var icon = iconRt.gameObject.AddComponent<Image>();
                    icon.sprite = data.Icon;
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    iconRt.SetSiblingIndex(0); // под затемнением перезарядки
                }
                else
                {
                    _label[slot] = UiFactory.Label("Name", rt, new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(size, 40f), 26, TextAnchor.MiddleCenter);
                    _label[slot].text = data != null ? data.ShortName : sk.Name;
                }
                _timer[slot] = UiFactory.Label("Timer", rt, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(size, 40f), 30, TextAnchor.MiddleCenter);
            }

            var c = _settings.SkillCancelOffset;
            float cs = _settings.SkillCancelRadius * 2f;
            var cancelRt = UiFactory.Rect("SkillCancel", transform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-c.x, c.y), new Vector2(cs, cs));
            _cancel = cancelRt.gameObject.AddComponent<Image>();
            _cancel.sprite = UiFactory.CircleSprite();
            _cancel.raycastTarget = false;
            var x = UiFactory.Label("X", cancelRt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cs, cs), 34, TextAnchor.MiddleCenter);
            x.text = "Отмена";
            cancelRt.gameObject.SetActive(false);
        }

        private void OnEnable() => EventBus.Subscribe<SkillAimInputEvent>(this, OnAim);
        private void OnDisable() => EventBus.UnsubscribeAll(this);

        private void OnAim(SkillAimInputEvent e)
        {
            _pressedSlot = e.Phase == SkillAimPhase.Held ? e.Slot : -1;
            if (_cancel == null) return;
            if (_cancel.gameObject.activeSelf != (_pressedSlot >= 0)) _cancel.gameObject.SetActive(_pressedSlot >= 0);
            _cancel.color = e.InCancelZone ? new Color(1f, 0.25f, 0.2f, 0.85f) : new Color(0.6f, 0.15f, 0.15f, 0.45f);
        }

        private void LateUpdate()
        {
            if (_runner == null || _runner.State == null) return;
            int me = _runner.LocalPlayer;
            var spec = _runner.Sim.Setup.Fighters[me];
            ref readonly var f = ref _runner.State.Fighters[me];

            for (int slot = 0; slot < SkillButtonLayout.SlotCount; slot++)
            {
                var sk = spec.Skill(slot);
                if (sk == null || _bg[slot] == null) continue;
                int cd = f.Cooldown(slot);
                int full = Mathf.Max(1, cd > sk.CooldownTicks ? sk.InitialCooldownTicks : sk.CooldownTicks);
                float fill = cd > 0 ? Mathf.Clamp01(cd / (float)full) : 0f;
                if (!Mathf.Approximately(_cooldown[slot].fillAmount, fill)) _cooldown[slot].fillAmount = fill;

                string timer = cd > 0 ? Mathf.CeilToInt(cd / (float)SimTime.TickRate).ToString() : string.Empty;
                if (_timer[slot].text != timer) _timer[slot].text = timer;

                bool mana = f.Mana >= sk.ManaCost;
                var color = slot == _pressedSlot ? Pressed : (!mana ? NoMana : (slot == (int)SkillSlot.Ultimate ? UltReady : Ready));
                if (_bg[slot].color != color) _bg[slot].color = color;
            }
        }
    }
}
