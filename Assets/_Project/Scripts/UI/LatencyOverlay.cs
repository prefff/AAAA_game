using System.Collections;
using System.Text;
using Game.Characters;
using Game.Core;
using Game.Simulation;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Оверлей разработчика: задержка последних действий (мс и кадры), распознанный жест, состояние бойца по тикам,
    /// преимущество по кадрам после последнего контакта, FPS — и панель тренировки (режим бота, матч/тренировка,
    /// запись и повтор действий, рестарт).
    ///
    /// Задержка считается от события касания (метка Input System) до конца рендера кадра, в котором результат
    /// команды уже есть (WaitForEndOfFrame). Время вывода на дисплей (композитор ОС, буферы свопчейна, развёртка
    /// панели) сюда не входит — его можно измерить только внешней камерой; поэтому цифра — нижняя граница.
    ///   Всего  = касание → отрисованный кадр;
    ///   Жест   = касание → событие, на котором жест распознан (отпускание пальца, порог удержания);
    ///   Игра   = событие → отрисованный кадр (то, что добавляет игра; критерий этапа 1 — не больше 2 кадров).
    /// </summary>
    public class LatencyOverlay : MonoBehaviour
    {
        [SerializeField] private MatchRunner _runner;
        [SerializeField] private int _visibleRows = 8;
        [Tooltip("Дублировать каждое измерение в консоль (на Android — в logcat), чтобы собрать базовые цифры.")]
        [SerializeField] private bool _logToConsole = true;
        [SerializeField] private bool _expanded = true;

        private static readonly string[] Commands =
            { "LightAttack", "HeavyAttack", "BlockStart", "BlockEnd", "Parry", "Dodge", "Ability1", "Ability2", "Ultimate" };
        private const float ReferenceHeight = 720f;

        private readonly StringBuilder _sb = new(1024);
        private string _text = string.Empty;
        private float _smoothedDt = 1f / 60f;
        private float _nextRebuild;
        private long _loggedCount;
        private GUIStyle _style;
        private Font _font;

        public void Bind(MatchRunner runner) => _runner = runner;

        private void OnDestroy()
        {
            if (_font != null) Destroy(_font);
        }

        private void OnEnable()
        {
            Latency.Log.Clear();
            _loggedCount = 0;
            StartCoroutine(MarkFramesRendered());
        }

        private IEnumerator MarkFramesRendered()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                Latency.Log.MarkRendered(Time.realtimeSinceStartupAsDouble, Time.frameCount);
                if (_logToConsole) LogNewSamples();
            }
        }

        private void Update()
        {
            _smoothedDt = Mathf.Lerp(_smoothedDt, Time.unscaledDeltaTime, 0.1f);
            if (_expanded && Time.unscaledTime >= _nextRebuild)
            {
                _nextRebuild = Time.unscaledTime + 0.2f;
                _text = BuildText();
            }
        }

        private float FrameMs => _smoothedDt * 1000f;

        private string BuildText()
        {
            var log = Latency.Log;
            _sb.Clear();
            double refresh = Screen.currentResolution.refreshRateRatio.value;
            _sb.Append($"FPS {1f / Mathf.Max(0.0001f, _smoothedDt):F0} ({FrameMs:F1} мс)  экран {refresh:F0} Гц  " +
                       $"target {Application.targetFrameRate}  vSync {QualitySettings.vSyncCount}\n");

            string gesture = log.Count > 0 ? log.Last.Label : "—";
            _sb.Append($"Состояние: {DescribeState()}   Жест: {gesture}\n");
            if (_runner != null)
            {
                _sb.Append($"Тик {_runner.State.Tick} ({SimTime.TickRate} Гц)  тиков в кадре {_runner.TicksLastFrame}  " +
                           $"досрочных {_runner.EarlyTicks}\n");
                _sb.Append($"Контакт: {DescribeContact()}\n");
            }

            _sb.Append("Команда         Всего   Жест    Игра (кадры)  Итог\n");
            int from = Mathf.Max(0, log.Count - _visibleRows);
            for (int i = log.Count - 1; i >= from; i--)
            {
                var s = log.Get(i);
                if (!s.IsRendered) continue;
                _sb.Append($"{s.Label,-14} {s.TotalMs,6:F0} {s.GestureMs,6:F0} {s.EngineMs,6:F0} ({s.EngineMs / FrameMs:F1})  " +
                           $"{(s.Accepted ? s.ResultState : "отброшена")}\n");
            }

            _sb.Append("Медиана (всего / игра), мс:\n");
            foreach (var c in Commands)
                if (log.TryGetMedian(c, out double total, out double engine, out int n))
                    _sb.Append($"  {c,-14} {total,5:F0} / {engine,4:F0}  n={n}\n");
            return _sb.ToString();
        }

        private string DescribeState()
        {
            if (_runner == null) return "—";
            int i = _runner.LocalPlayer;
            ref readonly var f = ref _runner.State.Fighters[i];
            var sk = _runner.Sim.CurrentSkill(_runner.State, i);
            if (sk != null)
            {
                string castPhase = f.SkillFired ? "Recovery" : "Startup";
                return $"Cast {sk.Name} {castPhase} [{f.StateTicks + 1}/{sk.TotalTicks}]";
            }
            var atk = _runner.Sim.CurrentAttack(_runner.State, i);
            if (atk == null) return $"{f.State} [{f.StateTicks}]";
            var phase = atk.PhaseAt(f.StateTicks);
            return $"{f.State} {atk.Kind} {phase} [{f.StateTicks + 1}/{atk.TotalTicks}]";
        }

        /// <summary> Преимущество по кадрам после последнего контакта — с точки зрения локального игрока. </summary>
        private string DescribeContact()
        {
            if (!_runner.HasContact) return "—";
            var e = _runner.LastContact;
            // FrameAdvantage — преимущество Actor (атакующего; для парирования — защитника).
            int adv = e.Actor == _runner.LocalPlayer ? e.FrameAdvantage : -e.FrameAdvantage;
            string who = e.Actor == _runner.LocalPlayer ? "вы" : "соперник";
            string what = e.Type switch
            {
                SimEventType.Hit => "попадание",
                SimEventType.Blocked => "блок",
                SimEventType.Parried => "парирование",
                SimEventType.GuardBreak => "пробитие блока",
                _ => e.Type.ToString(),
            };
            return $"{what} ({who}), у вас {(adv > 0 ? "+" : "")}{adv} тиков";
        }

        private static string BotName(BotMode mode) => mode switch
        {
            BotMode.Idle => "Манекен",
            BotMode.Block => "Блок",
            BotMode.Attack => "Атака",
            BotMode.Aggressive => "Агрессия",
            BotMode.Zoner => "Маг",
            _ => mode.ToString(),
        };

        /// <summary> Панель тренировки — в верхнем левом углу (зона джойстика: касания там не становятся жестами). </summary>
        private void DrawTrainingPanel(float left, float top)
        {
            if (_runner == null) return;
            const float h = 26f;
            float x = left;

            string bot = _runner.IsReplaying ? "Повтор" : BotName(_runner.BotMode);
            if (GUI.Button(new Rect(x, top, 130f, h), $"Бот: {bot}"))
                _runner.BotMode = (BotMode)(((int)_runner.BotMode + 1) % TrainingBot.ModeCount);
            x += 134f;

            if (GUI.Button(new Rect(x, top, 110f, h), _runner.Training ? "Тренировка" : "Матч"))
                _runner.SetTraining(!_runner.Training);
            x += 114f;

            if (GUI.Button(new Rect(x, top, 90f, h), _runner.IsRecording ? $"■ {_runner.RecordedTicks}" : "● Запись"))
                _runner.ToggleRecording();
            x += 94f;

            GUI.enabled = _runner.RecordedTicks > 0;
            if (GUI.Button(new Rect(x, top, 90f, h), _runner.IsReplaying ? "■ Повтор" : "▶ Повтор"))
                _runner.ToggleReplay();
            GUI.enabled = true;
            x += 94f;

            if (GUI.Button(new Rect(x, top, 90f, h), "Рестарт"))
                _runner.RequestRestart();
        }

        private void LogNewSamples()
        {
            var log = Latency.Log;
            // Журнал кольцевой: считаем по общему числу записей; после очистки начинаем заново.
            if (_loggedCount > log.TotalRecorded) _loggedCount = 0;
            long fresh = log.TotalRecorded - _loggedCount;
            for (int i = log.Count - (int)System.Math.Min(fresh, log.Count); i < log.Count; i++)
            {
                var s = log.Get(i);
                Debug.Log($"[Latency] {s.Label}: всего {s.TotalMs:F1} мс, жест {s.GestureMs:F1} мс, " +
                          $"игра {s.EngineMs:F1} мс ({s.EngineMs / FrameMs:F2} кадра при {FrameMs:F1} мс/кадр), " +
                          $"кадров от команды до рендера: {s.RenderedFrame - s.CommandFrame}, " +
                          $"итог: {(s.Accepted ? s.ResultState : "отброшена")}");
            }
            _loggedCount = log.TotalRecorded;
        }

        private void OnGUI()
        {
            float scale = Screen.height / ReferenceHeight;
            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            // Левый верхний угол под полосками HP: это зона джойстика, касания здесь не становятся жестами,
            // и панель не закрывает арену.
            const float left = 16f, top = 86f; // под полосками HP/стамины/маны и надписью раунда

            if (GUI.Button(new Rect(left, top, 80f, 26f), _expanded ? "LAT ▲" : "LAT ▼"))
            {
                _expanded = !_expanded;
                _nextRebuild = 0f;
            }
            DrawTrainingPanel(left + 84f, top);

            if (_expanded)
            {
                if (_style == null)
                {
                    // Моноширинный шрифт ОС — чтобы колонки таблицы ровнялись.
                    _font = MonospaceOSFont(13);
                    _style = new GUIStyle(GUI.skin.box)
                    {
                        alignment = TextAnchor.UpperLeft,
                        fontSize = 13,
                        richText = false,
                    };
                    if (_font != null) _style.font = _font;
                }
                var content = new GUIContent(_text);
                var size = _style.CalcSize(content);
                GUI.Box(new Rect(left, top + 30f, size.x, size.y), content, _style);
            }

            GUI.matrix = prev;
        }

        /// <summary>
        /// Первый установленный в ОС моноширинный шрифт; null — нет ни одного (тогда шрифт скина). Только из установленных:
        /// шрифт по имени, которого нет (Consolas на Android), не рисует текст и каждый кадр пишет в лог предупреждение со стеком.
        /// </summary>
        private static Font MonospaceOSFont(int size)
        {
            var installed = new System.Collections.Generic.HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var name in new[] { "Consolas", "Roboto Mono", "Droid Sans Mono", "DroidSansMono", "Cutive Mono", "Menlo", "Courier New" })
                if (installed.Contains(name)) return Font.CreateDynamicFontFromOSFont(name, size);
            return null;
        }
    }
}
