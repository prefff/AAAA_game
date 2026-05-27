using UnityEngine;
using UnityEngine.Events;

namespace Game.Combat
{
    /// <summary>
    /// Стамина — тратится на уклонения, тяжёлые атаки, удержание блока.
    /// Регенерируется автоматически с задержкой после последней траты.
    /// </summary>
    public class Stamina : MonoBehaviour
    {
        [SerializeField] private float _max = 100f;
        [SerializeField] private float _current;
        [SerializeField] private float _regenPerSecond = 25f;
        [Tooltip("Задержка перед началом регенерации после траты, сек.")]
        [SerializeField] private float _regenDelay = 0.6f;

        private float _regenCooldown;

        public float Max => _max;
        public float Current => _current;
        public float Normalized => _max <= 0f ? 0f : Mathf.Clamp01(_current / _max);
        public bool HasEnough(float cost) => _current >= cost;

        public UnityEvent<float> OnStaminaChanged; // normalized

        private void Awake()
        {
            // При AddComponent в рантайме UnityEvent остаётся null — подстраховываемся.
            if (OnStaminaChanged == null) OnStaminaChanged = new UnityEvent<float>();
            _current = _max;
        }

        private void Update()
        {
            if (_regenCooldown > 0f)
            {
                _regenCooldown -= Time.deltaTime;
                return;
            }
            if (_current < _max)
            {
                _current = Mathf.Min(_max, _current + _regenPerSecond * Time.deltaTime);
                OnStaminaChanged?.Invoke(Normalized);
            }
        }

        /// <summary> Попытка потратить стамину. Возвращает true, если хватило. </summary>
        public bool TrySpend(float cost)
        {
            if (cost <= 0f) return true;
            if (_current < cost) return false;
            _current -= cost;
            _regenCooldown = _regenDelay;
            OnStaminaChanged?.Invoke(Normalized);
            return true;
        }
    }
}