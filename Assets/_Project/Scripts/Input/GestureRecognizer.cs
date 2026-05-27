using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Input
{
    /// <summary>
    /// Распознаёт жесты в правой половине экрана:
    ///   - Тап (короткое касание, малое смещение)          ? LightAttack
    ///   - Долгий тап / удержание                          ? BlockStart / BlockEnd
    ///   - Свайп (быстрое движение в одну сторону)         ? Dodge в направлении свайпа
    ///   - Резкий свайп навстречу (короткий и быстрый)     ? Parry (если зажат BlockStart — пока упрощённо)
    ///   - Двойной тап                                     ? HeavyAttack
    ///   - Зигзаг / круг (через GestureLibrary)            ? способности (TODO, hook готов)
    ///
    /// На вход даём ScreenSpace позиции касания (через InputProvider / Input System).
    /// На выход — публикуем CommandInputEvent через EventBus.
    /// </summary>
    public class GestureRecognizer : MonoBehaviour
    {
        [Header("Зона жестов (правая половина экрана по умолчанию)")]
        [Tooltip("Доля экрана по X, левее которой касания игнорируются (0..1). 0.5 = правая половина.")]
        [Range(0f, 1f)] public float GestureZoneMinX = 0.5f;

        [Header("Tap")]
        [Tooltip("Максимальное смещение пальца, чтобы считаться тапом (в пикселях).")]
        public float TapMaxMove = 30f;
        [Tooltip("Максимальная длительность тапа, сек.")]
        public float TapMaxDuration = 0.18f;
        [Tooltip("Окно для двойного тапа, сек.")]
        public float DoubleTapWindow = 0.25f;

        [Header("Hold (block)")]
        [Tooltip("Длительность удержания, после которой считается, что начат блок, сек.")]
        public float HoldThreshold = 0.18f;

        [Header("Swipe")]
        [Tooltip("Минимальное расстояние свайпа в пикселях.")]
        public float SwipeMinDistance = 80f;
        [Tooltip("Максимальная длительность свайпа, сек.")]
        public float SwipeMaxDuration = 0.30f;
        [Tooltip("Очень быстрый и короткий 'flick' = парирование. Время в сек.")]
        public float ParryFlickMaxDuration = 0.12f;
        [Tooltip("Минимальная скорость flick для парирования (пикс/сек).")]
        public float ParryFlickMinSpeed = 1500f;

        // --- Состояние одного активного касания (упрощённо: один палец на правой зоне) ---
        private bool _tracking;
        private Vector2 _startPos;
        private Vector2 _lastPos;
        private float _startTime;
        private bool _holdFired;
        private float _lastTapTime = -10f;

        /// <summary> Вызывается из InputProvider: палец впервые коснулся экрана. </summary>
        public void OnTouchBegan(Vector2 screenPos)
        {
            if (!IsInGestureZone(screenPos)) return;
            _tracking = true;
            _startPos = screenPos;
            _lastPos = screenPos;
            _startTime = Time.unscaledTime;
            _holdFired = false;
        }

        /// <summary> Палец двигается. </summary>
        public void OnTouchMoved(Vector2 screenPos)
        {
            if (!_tracking) return;
            _lastPos = screenPos;

            // Hold-блок: палец почти не сдвинулся и держится дольше порога
            if (!_holdFired &&
                (screenPos - _startPos).sqrMagnitude < TapMaxMove * TapMaxMove &&
                Time.unscaledTime - _startTime >= HoldThreshold)
            {
                _holdFired = true;
                Raise(CommandType.BlockStart, Vector2.zero);
            }
        }

        /// <summary> Палец оторван от экрана. </summary>
        public void OnTouchEnded(Vector2 screenPos)
        {
            if (!_tracking) return;
            _tracking = false;

            var delta = screenPos - _startPos;
            float distance = delta.magnitude;
            float duration = Time.unscaledTime - _startTime;

            // Если уже шёл hold/block ? завершаем блок
            if (_holdFired)
            {
                Raise(CommandType.BlockEnd, Vector2.zero);
                return;
            }

            // --- Tap / Double Tap ---
            if (distance < TapMaxMove && duration < TapMaxDuration)
            {
                if (Time.unscaledTime - _lastTapTime < DoubleTapWindow)
                {
                    Raise(CommandType.HeavyAttack, Vector2.zero);
                    _lastTapTime = -10f; // сбрасываем, чтобы тройной тап не считался
                }
                else
                {
                    Raise(CommandType.LightAttack, Vector2.zero);
                    _lastTapTime = Time.unscaledTime;
                }
                return;
            }

            // --- Swipe / Flick ---
            if (distance >= SwipeMinDistance && duration <= SwipeMaxDuration)
            {
                var dir = delta.normalized;
                float speed = distance / Mathf.Max(0.001f, duration);

                // Очень быстрый короткий flick ? парирование
                if (duration <= ParryFlickMaxDuration && speed >= ParryFlickMinSpeed)
                {
                    Raise(CommandType.Parry, dir);
                    return;
                }

                // Обычный свайп ? уклонение в направлении свайпа
                Raise(CommandType.Dodge, dir);
                return;
            }

            // Иначе — игнорируем (слишком долго, слишком мало).
        }

        public void OnTouchCanceled()
        {
            if (_holdFired) Raise(CommandType.BlockEnd, Vector2.zero);
            _tracking = false;
            _holdFired = false;
        }

        private bool IsInGestureZone(Vector2 screenPos)
        {
            float normX = screenPos.x / Mathf.Max(1f, Screen.width);
            return normX >= GestureZoneMinX;
        }

        private void Raise(CommandType type, Vector2 dir)
        {
            var cmd = new InputCommand(type, dir, Time.unscaledTime);
            EventBus.Raise(new CommandInputEvent(cmd));
        }
    }
}