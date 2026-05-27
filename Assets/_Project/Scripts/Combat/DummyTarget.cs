using System.Collections;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Тренировочный манекен. Требует на объекте/детях:
    ///   - Health
    ///   - Hurtbox с триггер-коллайдером
    /// При смерти просто перезагружает HP через RespawnDelay (никаких сцен не трогает).
    /// При попадании коротко подсвечивает Renderer (если задан) — визуальный отклик.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class DummyTarget : MonoBehaviour
    {
        [SerializeField] private float _respawnDelay = 2f;
        [SerializeField] private Renderer _renderer;
        [SerializeField] private Color _hitColor = Color.red;
        [SerializeField] private float _hitFlashDuration = 0.08f;

        private Health _health;
        private Color _originalColor;
        private Material _materialInstance;
        private Coroutine _flashRoutine;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _health.OnDied.AddListener(OnDied);
            _health.OnHealthChanged.AddListener(_ => Flash());

            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
            if (_renderer != null)
            {
                _materialInstance = _renderer.material; // создаёт instance, чтобы не трогать общий материал
                if (_materialInstance.HasProperty("_BaseColor"))
                    _originalColor = _materialInstance.GetColor("_BaseColor");
                else if (_materialInstance.HasProperty("_Color"))
                    _originalColor = _materialInstance.GetColor("_Color");
            }
        }

        private void OnDied()
        {
            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            // Простейшая визуальная индикация смерти — спрятать на время
            if (_renderer != null) _renderer.enabled = false;
            yield return new WaitForSeconds(_respawnDelay);
            _health.ResetHealth();
            if (_renderer != null) _renderer.enabled = true;
        }

        private void Flash()
        {
            if (_renderer == null || _materialInstance == null) return;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            SetColor(_hitColor);
            yield return new WaitForSeconds(_hitFlashDuration);
            SetColor(_originalColor);
        }

        private void SetColor(Color c)
        {
            if (_materialInstance.HasProperty("_BaseColor"))
                _materialInstance.SetColor("_BaseColor", c);
            else if (_materialInstance.HasProperty("_Color"))
                _materialInstance.SetColor("_Color", c);
        }
    }
}