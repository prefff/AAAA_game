using System;
using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.Input;
using Game.Simulation;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Ведёт детерминированную симуляцию боя с фиксированным тиком (<see cref="SimTime.TickRate"/>) и связывает её
    /// с Unity: собирает ввод локального игрока (EventBus), даёт ввод второму бойцу (тренировочный бот или
    /// повтор записи), хранит снимки для отката и пишет замер задержки.
    ///
    /// Отзывчивость: если в кадре есть новая команда, а тик по времени ещё не настал, тик выполняется сразу
    /// (симуляция уходит вперёд не больше чем на <see cref="_earlyTickWindow"/> тика, дальше время её догоняет).
    /// Поэтому команда влияет на картинку в кадре касания даже при 120 Гц экрана и 60 Гц симуляции.
    ///
    /// Порядок в кадре: Input System и распознаватель (−2000), джойстик (−1000) → этот компонент (−500) →
    /// виды бойцов и камера (LateUpdate).
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class MatchRunner : MonoBehaviour
    {
        [Header("Бойцы")]
        [Tooltip("Пусто — Resources/Fighters/Fighter_Default.")]
        [SerializeField] private FighterDefinition _player;
        [SerializeField] private FighterDefinition _opponent;
        [Tooltip("Каким бойцом управляет этот телефон (0 — синяя сторона, 1 — красная).")]
        [SerializeField, Range(0, 1)] private int _localPlayer;

        [Header("Тренировка")]
        [SerializeField] private BotMode _botMode = BotMode.Idle;
        [Tooltip("Тренировка: без таймера и счёта, после KO раунд перезапускается.")]
        [SerializeField] private bool _training;
        [Tooltip("Повтор записи: бот проигрывает записанные действия игрока, отражая их (стороны зеркальны).")]
        [SerializeField] private bool _mirrorReplay = true;

        [Header("Тик")]
        [Tooltip("На сколько тиков симуляция может обогнать время, чтобы новая команда сработала в этом же кадре. 0 — ждать тика.")]
        [SerializeField, Range(0f, 1f)] private float _earlyTickWindow = 1f;
        [Tooltip("Сколько тиков хранить снимков (откат, отладка).")]
        [SerializeField] private int _historyTicks = 120;

        /// <summary> Длинный кадр (загрузка, пауза) не должен прокручивать секунды боя. </summary>
        private const double MaxFrameSeconds = 0.25;

        private FightSimulation _sim;
        private GameState _state;
        private StateHistory _history;
        private double _accumulator;
        private Vector2 _moveScreen;
        private ushort _nextCommandId = 1;
        // Прицел скилла (мировой, направление × доля дальности): держится, пока палец на кнопке; отпускание
        // уходит в ближайший тик, даже если палец уже снова на экране.
        private Vector2 _aim;
        private bool _aimHeld;
        private bool _aimReleasePending;

        private readonly List<PendingCommand> _pending = new();
        private readonly Dictionary<ushort, PendingCommand> _inFlight = new();

        // Запись и повтор действий игрока для бота.
        private readonly List<TickInput> _recorded = new();
        private bool _recording;
        private bool _replaying;
        private int _replayIndex;

        public FightSimulation Sim => _sim;
        /// <summary> Данные бойцов (имена, подписи скиллов) — для интерфейса; в бою используется Sim.Setup. </summary>
        public FighterDefinition LocalDefinition { get; private set; }
        public FighterDefinition OpponentDefinition { get; private set; }
        /// <summary> Данные бойца по индексу в симуляции. </summary>
        public FighterDefinition Definition(int index) => index == _localPlayer ? LocalDefinition : OpponentDefinition;
        public GameState State => _state;
        public StateHistory History => _history;
        public int LocalPlayer => _localPlayer;
        public int Opponent => 1 - _localPlayer;
        public int TicksLastFrame { get; private set; }
        public int EarlyTicks { get; private set; }
        /// <summary> Доля тика, накопленная с последнего шага (0..1): для сглаживания вида чужого бойца при желании. </summary>
        public float TickProgress => (float)Math.Max(0.0, Math.Min(1.0, _accumulator / SimTime.TickSeconds));

        public BotMode BotMode { get => _botMode; set { _botMode = value; _replaying = false; } }
        /// <summary> Прицел локального игрока, пока палец держит кнопку скилла (для индикатора). </summary>
        public bool IsAiming => _aimHeld;
        public Vector2 AimInput => _aim;
        public int AimSlot { get; private set; } = -1;
        public bool AimInCancelZone { get; private set; }
        public bool IsRecording => _recording;
        public bool IsReplaying => _replaying;
        public int RecordedTicks => _recorded.Count;
        public bool Training => _sim.Setup.Rules.Training;

        /// <summary> Каждое событие тика (попадание, блок, KO, принятая команда) — для видов, звука и отладки. </summary>
        public event Action<SimEvent> SimEventRaised;

        /// <summary> Последний контакт ударов: преимущество по кадрам для оверлея. </summary>
        public SimEvent LastContact { get; private set; }
        public bool HasContact { get; private set; }

        private struct PendingCommand
        {
            public SimCommand Command;
            public string Label;
            public double InputTime;
            public double RecognizedTime;
        }

        /// <summary> Настроить до первого кадра (процедурная сборка арены). Вызывать на выключенном объекте. </summary>
        public void Configure(FighterDefinition player, FighterDefinition opponent, int localPlayer, BotMode botMode, bool training)
        {
            _player = player;
            _opponent = opponent;
            _localPlayer = Mathf.Clamp(localPlayer, 0, 1);
            _botMode = botMode;
            _training = training;
        }

        private void Awake()
        {
            var p = _player != null ? _player : FighterDefinition.LoadOrDefault();
            var o = _opponent != null ? _opponent : p;
            LocalDefinition = p;
            OpponentDefinition = o;
            var setup = new SimSetup();
            setup.Fighters[_localPlayer] = p.ToSpec();
            setup.Fighters[1 - _localPlayer] = o.ToSpec();
            setup.Rules.Training = _training;
            _sim = new FightSimulation(setup);
            _state = _sim.CreateInitialState();
            _history = new StateHistory(Mathf.Max(8, _historyTicks));
            _history.Save(_state);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<CommandInputEvent>(this, OnCommand);
            EventBus.Subscribe<MoveInputEvent>(this, OnMove);
            EventBus.Subscribe<SkillAimInputEvent>(this, OnSkillAim);
            _accumulator = 0.0;
        }

        private void OnDisable()
        {
            EventBus.UnsubscribeAll(this);
            _pending.Clear();
            _moveScreen = Vector2.zero;
            _aimHeld = _aimReleasePending = false;
        }

        // ---------- Управление тренировкой ----------

        public void RequestRestart() => Enqueue(new SimCommand(CommandKind.Restart), null, 0.0, 0.0);

        public void SetTraining(bool training)
        {
            if (_sim.Setup.Rules.Training == training) return;
            _sim.Setup.Rules.Training = training;
            RequestRestart();
        }

        /// <summary> Начать/закончить запись действий игрока для повтора ботом. </summary>
        public void ToggleRecording()
        {
            _recording = !_recording;
            if (_recording)
            {
                _recorded.Clear();
                _replaying = false;
            }
        }

        public void ToggleReplay()
        {
            _replaying = !_replaying && _recorded.Count > 0;
            _recording = false;
            _replayIndex = 0;
        }

        // ---------- Ввод ----------

        private void OnMove(MoveInputEvent e) => _moveScreen = e.Direction;

        private void OnSkillAim(SkillAimInputEvent e)
        {
            _aim = e.Aim;
            AimSlot = e.Slot;
            AimInCancelZone = e.InCancelZone;
            bool held = e.Phase == SkillAimPhase.Held;
            if (_aimHeld && !held) _aimReleasePending = true;
            _aimHeld = held;
        }

        private void OnCommand(CommandInputEvent e)
        {
            var c = e.Command;
            var kind = ToKind(c.Type);
            if (kind == CommandKind.None) return;
            // Кнопка скилла: прицел пришёл непрерывным вводом (TickInput.Aim, отпускание — в этом же тике).
            var cmd = FightSimulation.IsSkill(kind)
                ? SimCommand.SkillWithInputAim(kind)
                : SimCommand.Of(kind, c.Direction.x, c.Direction.y);
            Enqueue(cmd, c.Type.ToString(), c.InputTime, c.RecognizedTime);
        }

        private void Enqueue(SimCommand cmd, string label, double inputTime, double recognizedTime)
        {
            ushort id = _nextCommandId++;
            if (_nextCommandId == 0) _nextCommandId = 1;
            _pending.Add(new PendingCommand
            {
                Command = new SimCommand(cmd.Kind, cmd.DirX, cmd.DirY, id),
                Label = label,
                InputTime = inputTime,
                RecognizedTime = recognizedTime,
            });
        }

        private static CommandKind ToKind(CommandType type) => type switch
        {
            CommandType.LightAttack => CommandKind.LightAttack,
            CommandType.HeavyAttack => CommandKind.HeavyAttack,
            CommandType.BlockStart => CommandKind.BlockStart,
            CommandType.BlockEnd => CommandKind.BlockEnd,
            CommandType.Parry => CommandKind.Parry,
            CommandType.Dodge => CommandKind.Dodge,
            CommandType.Ability1 => CommandKind.Skill1,
            CommandType.Ability2 => CommandKind.Skill2,
            CommandType.Ultimate => CommandKind.Ultimate,
            _ => CommandKind.None,
        };

        // ---------- Тик ----------

        private void Update()
        {
            _accumulator += Math.Min(Time.deltaTime, MaxFrameSeconds);
            int ran = 0;
            while (_accumulator >= SimTime.TickSeconds)
            {
                StepOnce();
                _accumulator -= SimTime.TickSeconds;
                ran++;
            }

            // Новая команда, а тик ещё не настал: выполняем его сейчас, а не в следующем кадре.
            if (ran == 0 && _pending.Count > 0 && _accumulator >= SimTime.TickSeconds * (1.0 - _earlyTickWindow))
            {
                StepOnce();
                _accumulator -= SimTime.TickSeconds;
                ran++;
                EarlyTicks++;
            }
            TicksLastFrame = ran;
        }

        /// <summary> Один тик симуляции. Публичный — для тестов и пошаговой отладки. </summary>
        public void StepOnce()
        {
            var local = BuildLocalInput();
            var other = BuildOpponentInput();
            if (_recording) _recorded.Add(local);

            if (_localPlayer == 0) _sim.Step(_state, local, other);
            else _sim.Step(_state, other, local);
            _history.Save(_state);

            var events = _sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Type == SimEventType.Hit || e.Type == SimEventType.Blocked || e.Type == SimEventType.Parried ||
                    e.Type == SimEventType.GuardBreak)
                {
                    LastContact = e;
                    HasContact = true;
                }
                RecordLatency(e);
                SimEventRaised?.Invoke(e);
            }
        }

        private TickInput BuildLocalInput()
        {
            var input = new TickInput();
            var move = _moveScreen.sqrMagnitude > 1e-6f ? InputSpace.ScreenToWorld(_moveScreen) : Vector2.zero; // длина (аналог) сохраняется
            input.SetMove(move.x, move.y);
            if (_aimHeld) input.SetAim(AimState.Held, _aim.x, _aim.y);
            else if (_aimReleasePending) input.SetAim(AimState.Released, _aim.x, _aim.y);
            _aimReleasePending = false;

            // Команда, пропавшая без события (буфер сброшен KO или новым раундом), не должна копиться вечно.
            if (_inFlight.Count > 64) _inFlight.Clear();
            int taken = 0;
            while (taken < _pending.Count && input.Add(_pending[taken].Command))
            {
                var p = _pending[taken];
                if (p.Label != null && p.InputTime > 0.0) _inFlight[p.Command.Id] = p;
                taken++;
            }
            _pending.RemoveRange(0, taken); // не влезло в тик — уйдёт в следующий
            return input;
        }

        private TickInput BuildOpponentInput()
        {
            if (_replaying && _recorded.Count > 0)
            {
                var rec = _recorded[_replayIndex];
                _replayIndex = (_replayIndex + 1) % _recorded.Count;
                return _mirrorReplay ? Mirror(rec) : rec;
            }
            return TrainingBot.Think(_state, _sim.Setup, Opponent, _botMode);
        }

        /// <summary> Стороны арены зеркальны (точечная симметрия): поворачиваем направления на 180°. </summary>
        private static TickInput Mirror(TickInput src)
        {
            var dst = new TickInput
            {
                MoveX = (sbyte)-src.MoveX, MoveY = (sbyte)-src.MoveY,
                AimX = (sbyte)-src.AimX, AimY = (sbyte)-src.AimY, Aim = src.Aim,
            };
            for (int i = 0; i < src.Count; i++)
            {
                var c = src[i];
                if (c.Kind == CommandKind.Restart) continue;
                dst.Add(c.UsesInputAim ? c : new SimCommand(c.Kind, (sbyte)-c.DirX, (sbyte)-c.DirY));
            }
            return dst;
        }

        /// <summary> Замер задержки: время отрисовки проставит оверлей в конце кадра. </summary>
        private void RecordLatency(in SimEvent e)
        {
            if (e.Type != SimEventType.CommandAccepted && e.Type != SimEventType.CommandDropped) return;
            if (e.Actor != _localPlayer || !_inFlight.TryGetValue(e.CommandId, out var p)) return;
            _inFlight.Remove(e.CommandId);
            string state = e.Type == SimEventType.CommandAccepted ? _state.Fighters[_localPlayer].State.ToString() : null;
            Latency.Log.Record(p.Label, state, p.InputTime, p.RecognizedTime, Time.frameCount);
        }
    }
}
