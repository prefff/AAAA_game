using Game.Characters;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Полоски HP / стамины / маны одного бойца. Значения читаются из состояния симуляции каждый кадр:
    /// своего состояния у HUD нет, поэтому он не может разойтись с боем.
    /// </summary>
    public class FighterHUD : MonoBehaviour
    {
        [SerializeField] private Image _healthFill;
        [SerializeField] private Image _staminaFill;
        [SerializeField] private Image _manaFill;

        private MatchRunner _runner;
        private int _index;

        public void Bind(MatchRunner runner, int index, Image healthFill, Image staminaFill, Image manaFill)
        {
            _runner = runner;
            _index = index;
            _healthFill = healthFill;
            _staminaFill = staminaFill;
            _manaFill = manaFill;
        }

        public float Health { get; private set; }

        private void LateUpdate()
        {
            if (_runner == null || _runner.State == null) return;
            ref readonly var f = ref _runner.State.Fighters[_index];
            var spec = _runner.Sim.Setup.Fighters[_index];
            Health = Ratio(f.Health.ToFloat(), spec.MaxHealth.ToFloat());
            Set(_healthFill, Health);
            Set(_staminaFill, Ratio(f.Stamina.ToFloat(), spec.MaxStamina.ToFloat()));
            Set(_manaFill, Ratio(f.Mana.ToFloat(), spec.MaxMana.ToFloat()));
        }

        private static float Ratio(float value, float max) => max <= 0f ? 0f : Mathf.Clamp01(value / max);

        private static void Set(Image fill, float value)
        {
            if (fill != null && !Mathf.Approximately(fill.fillAmount, value)) fill.fillAmount = value;
        }
    }
}
