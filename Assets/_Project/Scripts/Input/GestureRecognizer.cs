using System;
using UnityEngine;

namespace Game.Input
{
    /// <summary>
    /// Распознаватель жестов правой зоны по модели «сначала действие, потом уточнение»:
    /// ложное срабатывание лучше задержки, поэтому команда уходит в первый же момент, когда она возможна,
    /// а продолжение жеста отменяет её в другую.
    ///
    ///   касание                          → LightAttack сразу (startup удара — окно распознавания);
    ///   сдвиг дальше порога              → Dodge сразу, в сторону сдвига (отменяет удар в startup), с любой скоростью;
    ///   удержание без сдвига             → BlockStart по порогу (отменяет удар в startup), отпускание → BlockEnd;
    ///                                      первые кадры блока — парирование («блок вовремя», см. FightSimulation);
    ///   удержание, потом сдвиг           → Dodge из блока;
    ///   второй палец, пока первый на экране → HeavyAttack (отменяет лёгкий удар в startup), первый палец
    ///                                      после этого жестов не даёт.
    ///
    /// Чистый C#: время, позиции и перевод направления в мир передаются снаружи, поэтому тестируется
    /// на записанных последовательностях касаний без сцены. Время — шкала Time.realtimeSinceStartupAsDouble
    /// (на ней же метки событий Input System). Направление в командах — мировое (x = X, y = Z).
    /// </summary>
    public sealed class GestureRecognizer
    {
        private enum Phase { None, Pending, Dodging, Holding, Consumed }

        private readonly GestureSettings _settings;
        private readonly Action<InputCommand> _output;

        private Phase _phase;
        private Vector2 _startPos;
        private double _startTime;
        private bool _blockActive;

        /// <summary> Пикселей экрана в миллиметре (Screen.dpi / 25.4). </summary>
        public float PixelsPerMm { get; set; } = 160f / 25.4f;

        /// <summary>
        /// Экранное направление (x = вправо, y = вверх) → мировое в плоскости XZ. Камера не вращается, поэтому
        /// это фиксированное преобразование; по умолчанию — «вверх по экрану» = +Z.
        /// </summary>
        public Func<Vector2, Vector2> ScreenToWorld { get; set; } = d => d;

        public GestureSettings Settings => _settings;
        public bool IsTracking => _phase != Phase.None;

        public GestureRecognizer(GestureSettings settings, Action<InputCommand> output)
        {
            _settings = settings != null ? settings : throw new ArgumentNullException(nameof(settings));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary> Основной палец коснулся правой зоны (проверку зоны делает провайдер). </summary>
        public void TouchBegan(Vector2 screenPos, double time)
        {
            if (_phase != Phase.None) TouchCanceled(time); // новый жест без отпускания старого — закрываем старый
            _phase = Phase.Pending;
            _startPos = screenPos;
            _startTime = time;
            _blockActive = false;
            Emit(CommandType.LightAttack, Vector2.zero, time, time);
        }

        public void TouchMoved(Vector2 screenPos, double time)
        {
            if (_phase == Phase.None || _phase == Phase.Consumed) return;

            if ((_phase == Phase.Pending || _phase == Phase.Holding) && MovedBeyondThreshold(screenPos))
            {
                _phase = Phase.Dodging;
                Emit(CommandType.Dodge, WorldDirection(screenPos), _startTime, time);
                return;
            }
            Update(time);
        }

        /// <summary> Каждый кадр, пока палец на экране: неподвижный палец событий не присылает, а удержание идёт по таймеру. </summary>
        public void Update(double now)
        {
            if (_phase != Phase.Pending || now - _startTime < _settings.HoldThreshold) return;
            _phase = Phase.Holding;
            _blockActive = true;
            // Распознать удержание можно было ровно в момент порога; ожидание кадра — задержка игры, а не жеста.
            Emit(CommandType.BlockStart, Vector2.zero, _startTime, _startTime + _settings.HoldThreshold);
        }

        public void TouchEnded(Vector2 screenPos, double time)
        {
            if (_phase == Phase.None) return;
            if (_phase == Phase.Pending && !MovedBeyondThreshold(screenPos)) Update(time);

            var phase = _phase;
            _phase = Phase.None;

            if (_blockActive)
            {
                _blockActive = false;
                Emit(CommandType.BlockEnd, Vector2.zero, time, time);
                if (phase == Phase.Holding) return;
            }

            // Резкий бросок пальца между событиями: сдвиг виден только при отпускании — это тоже уклонение.
            // Тап — удар уже идёт; сдвиг с уклонением — оно уже начато.
            if (phase == Phase.Pending && MovedBeyondThreshold(screenPos))
                Emit(CommandType.Dodge, WorldDirection(screenPos), _startTime, time);
        }

        /// <summary> Касание прервано системой (уведомление, сворачивание): отпустить блок, если держали. </summary>
        public void TouchCanceled(double time)
        {
            if (_blockActive) Emit(CommandType.BlockEnd, Vector2.zero, time, time);
            _blockActive = false;
            _phase = Phase.None;
        }

        /// <summary> Второй палец коснулся правой зоны → тяжёлая атака. Основной палец больше жестов не даёт. </summary>
        public void SecondaryTouchBegan(double time)
        {
            if (_phase == Phase.Pending) _phase = Phase.Consumed;
            Emit(CommandType.HeavyAttack, Vector2.zero, time, time);
        }

        private bool MovedBeyondThreshold(Vector2 screenPos)
        {
            float threshold = _settings.DodgeThresholdMm * PixelsPerMm;
            return (screenPos - _startPos).sqrMagnitude >= threshold * threshold;
        }

        private Vector2 WorldDirection(Vector2 screenPos)
        {
            var world = ScreenToWorld((screenPos - _startPos).normalized);
            return world.sqrMagnitude > 1e-6f ? world.normalized : Vector2.zero;
        }

        private void Emit(CommandType type, Vector2 worldDir, double inputTime, double recognizedTime)
        {
            _output(new InputCommand(type, worldDir, (float)recognizedTime, inputTime, recognizedTime));
        }
    }
}
