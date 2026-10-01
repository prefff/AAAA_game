using Game.Combat;
using Game.Core;
using Game.Input;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Корневой компонент бойца. Связывает Input → StateMachine → Combat.
    /// Содержит ссылки на ключевые компоненты (Health, Stamina, Hurtbox, Hitbox, Rigidbody)
    /// и набор AttackData (light / heavy).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Fighter : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Health _health;
        [SerializeField] private Stamina _stamina;
        [SerializeField] private Hurtbox _hurtbox;
        [SerializeField] private Hitbox _hitbox;
        [SerializeField] private Rigidbody _rigidbody;

        [Header("Attacks")]
        [SerializeField] private AttackData _lightAttack;
        [SerializeField] private AttackData _heavyAttack;

        [Header("Movement")]
        public float MoveSpeed = 5f;
        public float RotationSpeed = 720f;

        [Header("Defense tuning")]
        [Tooltip("Длина окна парирования, сек.")]
        public float ParryWindow = 0.18f;
        [Tooltip("Кадры неуязвимости при уклонении (60 FPS).")]
        public int DodgeIFrames = 8;
        [Tooltip("Скорость уклонения, м/с.")]
        public float DodgeSpeed = 8f;
        [Tooltip("Длительность уклонения, сек.")]
        public float DodgeDuration = 0.25f;
        [Tooltip("Стамина за уклонение.")]
        public float DodgeStaminaCost = 20f;
        [Tooltip("Сколько секунд от начала уклонения flick-парирование ещё отменяет его (с возвратом стамины).")]
        public float DodgeToParryCancelWindow = 0.15f;

        [Header("Input")]
        [Tooltip("Буфер ввода, сек: команда, пришедшая, когда действие запрещено (recovery, hitstun), " +
                 "выполнится в первый разрешённый кадр, если с её прихода прошло не больше этого времени.")]
        public float InputBufferSeconds = 0.12f;

        public Health Health => _health;
        public Stamina Stamina => _stamina;
        public Hurtbox Hurtbox => _hurtbox;
        public Hitbox Hitbox => _hitbox;
        public Rigidbody Body => _rigidbody;
        public AttackData LightAttack => _lightAttack;
        public AttackData HeavyAttack => _heavyAttack;
        public float DodgeIFrameSeconds => DodgeIFrames / 60f;

        /// <summary> Текущее направление движения от джойстика (в плоскости XZ камеры). </summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary> Является ли этим бойцом локальный игрок (true) или AI/удалённый игрок (false). </summary>
        public bool IsLocalPlayer = true;

        private StateMachine _fsm;
        public StateMachine FSM => _fsm;

        private readonly InputBuffer _inputBuffer = new(0.0);
        public InputBuffer InputBuffer => _inputBuffer;

        /// <summary>
        /// Программная настройка ссылок (процедурная сборка бойца без reflection).
        /// Вызывать на неактивном объекте, до первого Awake.
        /// </summary>
        public void Setup(Health health, Stamina stamina, Hurtbox hurtbox, Hitbox hitbox, Rigidbody body,
                          AttackData lightAttack, AttackData heavyAttack)
        {
            _health = health;
            _stamina = stamina;
            _hurtbox = hurtbox;
            _hitbox = hitbox;
            _rigidbody = body;
            _lightAttack = lightAttack;
            _heavyAttack = heavyAttack;
        }

        private void Reset()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _health = GetComponentInChildren<Health>();
            _stamina = GetComponentInChildren<Stamina>();
            _hurtbox = GetComponentInChildren<Hurtbox>();
            _hitbox = GetComponentInChildren<Hitbox>();
        }

        private void Awake()
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
            ConfigureBody(_rigidbody);

            // Автоподхват ссылок, если забыли назначить в инспекторе.
            if (_health == null)  _health  = GetComponentInChildren<Health>();
            if (_stamina == null) _stamina = GetComponentInChildren<Stamina>();
            if (_hurtbox == null) _hurtbox = GetComponentInChildren<Hurtbox>();
            if (_hitbox == null)  _hitbox  = GetComponentInChildren<Hitbox>();

            if (_hurtbox == null) Debug.LogError($"[Fighter] {name}: Hurtbox не назначен и не найден в детях.", this);
            if (_hitbox  == null) Debug.LogError($"[Fighter] {name}: Hitbox не назначен и не найден в детях.", this);
            if (_health  == null) Debug.LogError($"[Fighter] {name}: Health не назначен и не найден в детях.", this);
            if (_stamina == null) Debug.LogError($"[Fighter] {name}: Stamina не назначен и не найден в детях.", this);

            _fsm = new StateMachine(this);
            _fsm.Register(new IdleState());
            _fsm.Register(new MoveState());
            _fsm.Register(new AttackState());
            _fsm.Register(new BlockState());
            _fsm.Register(new ParryState());
            _fsm.Register(new DodgeState());
            _fsm.Register(new HitstunState());

            // Цвет по состоянию — пока нет анимаций, это единственный видимый отклик на команду в кадре касания.
            if (!TryGetComponent<FighterStateView>(out _)) gameObject.AddComponent<FighterStateView>();
        }

        /// <summary>
        /// Физика бойца: не опрокидывается, не проваливается при рывках.
        /// Без интерполяции: она рисует тело на шаг физики позже, а физика и так шагает каждый кадр (<see cref="PhysicsStepper"/>).
        /// </summary>
        public static void ConfigureBody(Rigidbody rb)
        {
            rb.constraints |= RigidbodyConstraints.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.useGravity = true;
            rb.isKinematic = false;
        }

        private void OnEnable()
        {
            if (IsLocalPlayer)
            {
                EventBus.Subscribe<MoveInputEvent>(this, OnMoveInput);
                EventBus.Subscribe<CommandInputEvent>(this, OnCommandInput);
            }
            EventBus.Subscribe<HitstunRequestedEvent>(this, OnHitstunRequested);

            _fsm.Change(_fsm.Get<IdleState>());
        }

        private void OnDisable()
        {
            // OnDisable вызывается и перед уничтожением, поэтому отдельный OnDestroy не нужен.
            EventBus.UnsubscribeAll(this);
            MoveInput = Vector2.zero;
            _inputBuffer.Clear();
            _fsm.Stop();
        }

        public void EnterHitstun(float seconds)
        {
            var state = _fsm.Get<HitstunState>();
            state.SetDuration(seconds);
            _fsm.Change(state); // если уже в hitstun — Change ничего не делает, а SetDuration продлевает оглушение
        }

        /// <summary> Перевести джойстик (экранные XY) в направление в мире относительно камеры. Свайпы переводит слой ввода. </summary>
        public Vector3 ToWorldDirection(Vector2 input)
        {
            var cam = Camera.main;
            Vector3 fwd, right;
            if (cam != null)
            {
                fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
                right = cam.transform.right; right.y = 0f; right.Normalize();
            }
            else
            {
                fwd = Vector3.forward;
                right = Vector3.right;
            }
            return right * input.x + fwd * input.y;
        }

        /// <summary> Обнулить горизонтальную скорость (вертикальную — гравитацию — сохраняем). </summary>
        public void StopHorizontal()
        {
            var v = _rigidbody.linearVelocity;
            v.x = 0f; v.z = 0f;
            _rigidbody.linearVelocity = v;
        }

        private void OnMoveInput(MoveInputEvent e) => MoveInput = e.Direction;

        private static double Now => Time.realtimeSinceStartupAsDouble;

        private void OnCommandInput(CommandInputEvent e)
        {
            var cmd = e.Command;
            if (_fsm.HandleCommand(cmd))
            {
                _inputBuffer.Clear();
                RecordLatency(cmd, accepted: true);
                return;
            }

            // Сейчас нельзя (recovery, hitstun, активная фаза) — запоминаем и выполним в первый разрешённый кадр.
            if (InputBuffer.IsBufferable(cmd.Type))
            {
                _inputBuffer.WindowSeconds = InputBufferSeconds;
                if (_inputBuffer.Push(cmd, Now, out var replaced)) RecordLatency(replaced, accepted: false);
                if (!_inputBuffer.HasCommand) RecordLatency(cmd, accepted: false); // буфер выключен (0 с)
                return;
            }

            // Отпустили блок раньше, чем он успел начаться, — буферный блок больше не нужен.
            if (cmd.Type == CommandType.BlockEnd && _inputBuffer.HasCommand && _inputBuffer.Command.Type == CommandType.BlockStart)
            {
                RecordLatency(_inputBuffer.Command, accepted: false);
                _inputBuffer.Clear();
            }
            RecordLatency(cmd, accepted: false);
        }

        /// <summary> Выполнить команду из буфера, если состояние уже позволяет; просроченную — выбросить. </summary>
        private void TryBufferedCommand()
        {
            if (!_inputBuffer.HasCommand) return;
            if (_inputBuffer.Expire(Now, out var expired))
            {
                RecordLatency(expired, accepted: false);
                return;
            }

            var cmd = _inputBuffer.Command;
            if (!_fsm.HandleCommand(cmd)) return;
            _inputBuffer.Clear();
            RecordLatency(cmd, accepted: true);
        }

        /// <summary> Замер задержки: время отрисовки проставит оверлей в конце кадра. </summary>
        private void RecordLatency(InputCommand cmd, bool accepted)
        {
            if (cmd.InputTime <= 0.0) return; // команда не от касания (тесты, ИИ)
            string state = accepted && _fsm.Current != null ? _fsm.Current.GetType().Name : null;
            Latency.Log.Record(cmd.Type.ToString(), state, cmd.InputTime, cmd.RecognizedTime, Time.frameCount);
        }

        private void OnHitstunRequested(HitstunRequestedEvent e)
        {
            if (e.Entity != gameObject || e.Seconds <= 0f) return;

            if (e.IsBlockstun && _fsm.Current is BlockState block)
                block.ApplyBlockstun(e.Seconds);
            else
                EnterHitstun(e.Seconds);
        }

        // Ввод уже обработан (Input System и распознаватель работают раньше), физика шагнёт после всех Update
        // (PhysicsStepper) — поэтому и логика, и «физическая» часть состояния считаются в том же кадре, что и ввод.
        private void Update()
        {
            float dt = Time.deltaTime;
            _fsm.Tick(dt);
            // После Tick: если состояние только что закончилось (recovery, hitstun), буферная команда стартует в этом же кадре.
            TryBufferedCommand();
            _fsm.FixedTick(Mathf.Min(dt, PhysicsStepper.MaxStep));
        }
    }
}
