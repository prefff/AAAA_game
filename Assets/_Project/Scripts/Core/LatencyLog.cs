using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary> Одно измеренное действие: от касания до кадра, в котором его результат отрисован. </summary>
    public struct LatencySample
    {
        /// <summary> Что за команда (тип жеста). </summary>
        public string Label;
        /// <summary> Состояние бойца после команды; null — команда отброшена (не изменила состояние). </summary>
        public string ResultState;
        /// <summary> Начало действия игрока: касание (для отпускания блока — момент отпускания). Время Input System, сек. </summary>
        public double InputTime;
        /// <summary> Событие, на котором распознаватель выдал команду (отпускание, порог удержания). </summary>
        public double RecognizedTime;
        /// <summary> Конец рендера кадра, в котором результат команды уже виден. 0 — кадр ещё не отрисован. </summary>
        public double RenderedTime;
        public int CommandFrame;
        public int RenderedFrame;

        public bool Accepted => ResultState != null;
        public bool IsRendered => RenderedTime > 0.0;

        /// <summary> Полная задержка, мс: касание → отрисованный кадр. </summary>
        public double TotalMs => (RenderedTime - InputTime) * 1000.0;
        /// <summary> Сколько ждал распознаватель жеста, мс (отпускание пальца, порог удержания). </summary>
        public double GestureMs => (RecognizedTime - InputTime) * 1000.0;
        /// <summary> Задержка, добавленная игрой после распознавания, мс: событие ввода → отрисованный кадр. </summary>
        public double EngineMs => (RenderedTime - RecognizedTime) * 1000.0;
    }

    /// <summary>
    /// Журнал задержки действий (кольцевой буфер). Чистый C#, время передаётся явно — тестируется без сцены.
    /// Порядок: <see cref="Record"/> в момент обработки команды бойцом → <see cref="MarkRendered"/> в конце кадра.
    /// Все времена — на шкале Time.realtimeSinceStartupAsDouble (на ней же метки событий Input System).
    /// </summary>
    public sealed class LatencyLog
    {
        private readonly LatencySample[] _samples;
        private int _next;
        private int _count;
        private int _firstUnrendered = -1; // индекс самой старой неотрисованной записи в порядке добавления

        public LatencyLog(int capacity = 64)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _samples = new LatencySample[capacity];
        }

        public int Count => _count;
        public int Capacity => _samples.Length;
        /// <summary> Сколько записей добавлено с последней очистки (включая вытесненные из буфера). </summary>
        public long TotalRecorded { get; private set; }

        /// <summary> Последний добавленный образец; Count должен быть > 0. </summary>
        public LatencySample Last => Get(_count - 1);

        public void Record(string label, string resultState, double inputTime, double recognizedTime, int frame)
        {
            _samples[_next] = new LatencySample
            {
                Label = label,
                ResultState = resultState,
                InputTime = inputTime,
                RecognizedTime = recognizedTime,
                CommandFrame = frame,
            };
            if (_firstUnrendered < 0) _firstUnrendered = _count;
            TotalRecorded++;
            _next = (_next + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
            else if (_firstUnrendered > 0) _firstUnrendered--; // самый старый образец вытеснен
        }

        /// <summary> Проставить время отрисовки всем ещё не отрисованным записям. Вызывать в конце кадра. </summary>
        public void MarkRendered(double time, int frame)
        {
            if (_firstUnrendered < 0) return;
            for (int i = _firstUnrendered; i < _count; i++)
            {
                int idx = Index(i);
                _samples[idx].RenderedTime = time;
                _samples[idx].RenderedFrame = frame;
            }
            _firstUnrendered = -1;
        }

        /// <summary> i-й образец в порядке добавления (0 — самый старый из хранимых). </summary>
        public LatencySample Get(int i)
        {
            if (i < 0 || i >= _count) throw new ArgumentOutOfRangeException(nameof(i));
            return _samples[Index(i)];
        }

        /// <summary> Медиана полной и «игровой» задержки по принятым и отрисованным командам с данной меткой. </summary>
        public bool TryGetMedian(string label, out double totalMs, out double engineMs, out int samples)
        {
            var totals = new List<double>();
            var engines = new List<double>();
            for (int i = 0; i < _count; i++)
            {
                var s = _samples[Index(i)];
                if (!s.Accepted || !s.IsRendered || s.Label != label) continue;
                totals.Add(s.TotalMs);
                engines.Add(s.EngineMs);
            }
            samples = totals.Count;
            totalMs = Median(totals);
            engineMs = Median(engines);
            return samples > 0;
        }

        public void Clear()
        {
            _next = 0;
            _count = 0;
            _firstUnrendered = -1;
            TotalRecorded = 0;
        }

        private int Index(int i) => (_next - _count + i + _samples.Length) % _samples.Length;

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0.0;
            values.Sort();
            int mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) * 0.5;
        }
    }

    /// <summary> Общий журнал задержки игры (его пишет локальный боец, читает оверлей разработчика). </summary>
    public static class Latency
    {
        public static readonly LatencyLog Log = new(64);
    }
}
