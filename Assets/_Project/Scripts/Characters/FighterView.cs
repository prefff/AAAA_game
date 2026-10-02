using Game.Simulation;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Вид бойца: только читает состояние симуляции и рисует его. Своего игрового состояния нет.
    /// Позиция — последнее состояние симуляции без интерполяции с отставанием (отставание ощущается как задержка).
    /// Пока нет анимаций, цвет показывает состояние и фазы удара (startup — жёлтый, active — красный, recovery —
    /// бурый, блок — синий, уклонение — зелёный, парирование (начало блока) — белый, оглушение — фиолетовый, каст
    /// скилла — голубой, пробитый блок — оранжевый, только что вышел из оглушения и лёгкие его не оглушают — стальной),
    /// а во время active
    /// под бойцом виден круг хитбокса — так frame data читается глазами.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FighterView : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [Tooltip("Плоский диск-индикатор хитбокса (масштаб 1 = диаметр 1 м). Необязателен.")]
        [SerializeField] private Transform _hitboxMarker;
        [SerializeField] private Color _startup = new(1f, 0.85f, 0.2f);
        [SerializeField] private Color _active = new(1f, 0.25f, 0.15f);
        [SerializeField] private Color _recovery = new(0.55f, 0.4f, 0.3f);
        [SerializeField] private Color _block = new(0.3f, 0.45f, 1f);
        [SerializeField] private Color _parry = new(1f, 1f, 1f);
        [SerializeField] private Color _vulnerable = new(0.45f, 0.45f, 0.45f);
        [SerializeField] private Color _dodge = new(0.3f, 1f, 0.6f);
        [SerializeField] private Color _hitstun = new(0.85f, 0.2f, 0.85f);
        [SerializeField] private Color _dead = new(0.15f, 0.15f, 0.15f);
        [SerializeField] private Color _castStartup = new(0.3f, 0.85f, 1f);
        [SerializeField] private Color _castRecovery = new(0.25f, 0.45f, 0.55f);
        [SerializeField] private Color _guardBroken = new(1f, 0.55f, 0.1f);
        [Tooltip("Стоит/бежит, а лёгкие удары его пока не оглушают (иммунитет после выхода из hitstun).")]
        [SerializeField] private Color _stunImmune = new(0.7f, 0.8f, 0.85f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MatchRunner _runner;
        private int _index;
        private MaterialPropertyBlock _props;
        private int _colorId = -1;
        private Color _original;
        private Color _shown;

        public int Index => _index;
        public MatchRunner Runner => _runner;
        public Renderer Body => _renderer;

        public void Setup(Renderer body, Transform hitboxMarker)
        {
            _renderer = body;
            _hitboxMarker = hitboxMarker;
        }

        public void Bind(MatchRunner runner, int index)
        {
            _runner = runner;
            _index = index;
            Sync();
        }

        private void Awake()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
            var mat = _renderer != null ? _renderer.sharedMaterial : null;
            if (mat == null) return;
            if (mat.HasProperty(BaseColorId)) _colorId = BaseColorId;
            else if (mat.HasProperty(ColorId)) _colorId = ColorId;
            if (_colorId == -1) return;

            // Исходный цвет — из PropertyBlock (сборщик арены красит через него) или из материала.
            _props = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_props);
            _original = _props.HasColor(_colorId) ? _props.GetColor(_colorId) : mat.GetColor(_colorId);
            _shown = _original;
        }

        // LateUpdate: тик этого кадра уже выполнен (MatchRunner в Update).
        private void LateUpdate() => Sync();

        private void Sync()
        {
            if (_runner == null || _runner.State == null) return;
            ref readonly var f = ref _runner.State.Fighters[_index];

            transform.position = new Vector3(f.Position.X.ToFloat(), 0f, f.Position.Y.ToFloat());
            var facing = new Vector3(f.Facing.X.ToFloat(), 0f, f.Facing.Y.ToFloat());
            if (facing.sqrMagnitude > 1e-6f)
            {
                var rot = Quaternion.LookRotation(facing, Vector3.up);
                // KO — боец лежит.
                transform.rotation = f.State == ActionState.Dead ? rot * Quaternion.Euler(-90f, 0f, 0f) : rot;
            }

            var phase = _runner.Sim.AttackPhaseOf(_runner.State, _index);
            UpdateHitboxMarker(phase);
            SetColor(ColorFor(f, phase));
        }

        private void UpdateHitboxMarker(AttackPhase phase)
        {
            if (_hitboxMarker == null) return;
            bool show = phase == AttackPhase.Active;
            if (_hitboxMarker.gameObject.activeSelf != show) _hitboxMarker.gameObject.SetActive(show);
            if (!show) return;
            var atk = _runner.Sim.CurrentAttack(_runner.State, _index);
            float d = atk.HitRadius.ToFloat() * 2f;
            _hitboxMarker.localPosition = new Vector3(0f, 0.02f, atk.HitOffset.ToFloat());
            _hitboxMarker.localScale = new Vector3(d, 0.01f, d);
        }

        private Color ColorFor(in FighterSim f, AttackPhase phase)
        {
            switch (f.State)
            {
                case ActionState.Attack:
                    return phase switch
                    {
                        AttackPhase.Startup => _startup,
                        AttackPhase.Active => _active,
                        _ => _recovery,
                    };
                case ActionState.Block: return _block;
                case ActionState.Parry: return _parry;
                case ActionState.ParryRecovery: return _vulnerable;
                case ActionState.Dodge: return _dodge;
                case ActionState.Hitstun:
                case ActionState.ParryStunned: return _hitstun;
                case ActionState.Dead: return _dead;
                case ActionState.Cast:
                    return f.SkillFired ? _castRecovery : _castStartup;
                case ActionState.GuardBroken: return _guardBroken;
                default: return f.StunImmunityTicks > 0 ? _stunImmune : _original;
            }
        }

        private void SetColor(Color color)
        {
            if (_colorId == -1 || color == _shown) return;
            _shown = color;
            _renderer.GetPropertyBlock(_props);
            _props.SetColor(_colorId, color);
            _renderer.SetPropertyBlock(_props);
        }
    }
}
