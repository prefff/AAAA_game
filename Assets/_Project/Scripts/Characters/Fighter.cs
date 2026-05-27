using Game.Combat;
using Game.Core;
using Game.Input;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Корневой компонент бойца. Связывает Input ? StateMachine ? Combat.
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
        [Tooltip("Скорость уклонения (импульс), м/с.")]
        public float DodgeSpeed = 8f;
        [Tooltip("Длительность уклонения, сек.")]
        public float DodgeDuration = 0.25f;
        [Tooltip("Стамина за уклонение.")]
        public float DodgeStaminaCost = 20f;

        public Health Health => _health;
        public Stamina Stamina => _stamina;
        public Hurtbox Hurtbox => _hurtbox;
        public Hitbox Hitbox => _hitbox;
        public Rigidbody Body => _rigidbody;
        public AttackData LightAttack => _lightAttack;
        public AttackData HeavyAttack => _heavyAttack;

        /// <summary> Текущее направление движения от джойстика (в плоскости XZ камеры). </summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary> Является ли этим бойцом локальный игрок (true) или AI/удалённый игрок (false). </summary>
        public bool IsLocalPlayer = true;

        private StateMachine _fsm;
        public StateMachine FSM => _fsm;

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
            _rigidbody.freezeRotation = true;

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
            _fsm.Change(_fsm.Get<IdleState>());
        }

        private void OnEnable()
        {
            if (IsLocalPlayer)
            {
                EventBus.Subscribe<MoveInputEvent>(OnMoveInput);
                EventBus.Subscribe<CommandInputEvent>(OnCommandInput);
            }
        }

        private void OnDisable()
        {
            if (IsLocalPlayer)
            {
                EventBus.Unsubscribe<MoveInputEvent>(OnMoveInput);
                EventBus.Unsubscribe<CommandInputEvent>(OnCommandInput);
            }
        }

        private void OnMoveInput(MoveInputEvent e) => MoveInput = e.Direction;

        private void OnCommandInput(CommandInputEvent e) => _fsm.HandleCommand(e.Command);

        private void Update() => _fsm.Tick(Time.deltaTime);
        private void FixedUpdate() => _fsm.FixedTick(Time.fixedDeltaTime);
    }
}