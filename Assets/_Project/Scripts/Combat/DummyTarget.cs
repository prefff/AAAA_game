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

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Health _health;
        private float _lastNormalized = 1f;
        private Color _originalColor;
        private int _colorId = -1;
        private MaterialPropertyBlock _block;
        private Coroutine _flashRoutine;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _health.OnDied.AddListener(OnDied);
            _health.OnHealthChanged.AddListener(OnHealthChanged);

            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
            var mat = _renderer != null ? _renderer.sharedMaterial : null;
            if (mat != null)
            {
                // Цвет меняем через PropertyBlock: не создаём инстанс материала и не конфликтуем
                // с покраской из ArenaBuilder (она тоже через PropertyBlock).
                if (mat.HasProperty(BaseColorId)) _colorId = BaseColorId;
                else if (mat.HasProperty(ColorId)) _colorId = ColorId;

                _block = new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(_block);
                if (_colorId != -1)
                    _originalColor = _block.HasColor(_colorId) ? _block.GetColor(_colorId) : mat.GetColor(_colorId);
            }
        }

        private void OnHealthChanged(float normalized)
        {
            // Вспыхиваем только от урона, не от лечения/респауна.
            if (normalized < _lastNormalized) Flash();
            _lastNormalized = normalized;
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
            if (_colorId == -1) return;
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
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(_colorId, c);
            _renderer.SetPropertyBlock(_block);
        }
    }
}