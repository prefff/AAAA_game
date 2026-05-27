using Game.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Game.Combat
{
    /// <summary>
    /// Здоровье сущности. Слушает входящий урон через метод TakeDamage().
    /// Поднимает события через EventBus + локальный UnityEvent для UI/анимаций.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private float _current;

        public float Max => _maxHealth;
        public float Current => _current;
        public float Normalized => _maxHealth <= 0f ? 0f : Mathf.Clamp01(_current / _maxHealth);
        public bool IsAlive => _current > 0f;

        public UnityEvent<float> OnHealthChanged;   // (normalized 0..1)
        public UnityEvent OnDied;

        private void Awake()
        {
            _current = _maxHealth;
        }

        public void TakeDamage(float amount, GameObject attacker, Vector3 hitPoint)
        {
            if (!IsAlive || amount <= 0f) return;

            _current = Mathf.Max(0f, _current - amount);
            OnHealthChanged?.Invoke(Normalized);

            EventBus.Raise(new DamageDealtEvent(attacker, gameObject, amount, hitPoint));

            if (_current <= 0f)
            {
                OnDied?.Invoke();
                EventBus.Raise(new DeathEvent(gameObject));
            }
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            _current = Mathf.Min(_maxHealth, _current + amount);
            OnHealthChanged?.Invoke(Normalized);
        }

        public void ResetHealth()
        {
            _current = _maxHealth;
            OnHealthChanged?.Invoke(Normalized);
        }
    }
}