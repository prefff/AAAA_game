using Game.Simulation;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Вид бойца: только читает состояние симуляции и рисует его. Своего игрового состояния нет.
    /// Позиция — последнее состояние симуляции без интерполяции с отставанием (отставание ощущается как задержка).
    ///
    /// С моделью (<see cref="FighterRig"/> + шейдер Game/Character): позу считает риг из frame data, а состояние,
    /// которое позой не передать, показывает шейдер — ободок (окно парирования — белый, неуязвимость переката —
    /// бирюзовый, оглушение — красный, пробитый блок — оранжевый, иммунитет к оглушению — стальной), вспышка кадров
    /// попадания и свечение перчаток (телеграф тяжёлого удара и каста). Цвет команды — капюшон, шарф, свечение.
    ///
    /// Без модели (капсула) цвет показывает состояние и фазы удара (startup — жёлтый, active — красный, recovery —
    /// бурый, блок — синий, уклонение — зелёный, парирование (начало блока) — белый, оглушение — фиолетовый, каст
    /// скилла — голубой, пробитый блок — оранжевый, только что вышел из оглушения и лёгкие его не оглушают — стальной),
    /// а во время active
    /// под бойцом виден круг хитбокса — так frame data читается глазами.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FighterView : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [Tooltip("Процедурная анимация модели. Пусто — капсула, состояние показывается цветом.")]
        [SerializeField] private FighterRig _rig;
        [Tooltip("Базовая яркость свечения перчаток (Game/Character).")]
        [SerializeField] private float _glowIntensity = 1.6f;
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
        private static readonly int TeamColorId = Shader.PropertyToID("_TeamColor");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        private const float FlashSeconds = 0.12f;

        private MatchRunner _runner;
        private int _index;
        private MaterialPropertyBlock _props;
        private int _colorId = -1;
        private Color _original;
        private Color _shown;

        // Режим модели: свойства шейдера Game/Character.
        private bool _shaded;
        private Color _flash;
        private float _flashStart = -1f;
        private Color _rimShown = Color.clear;
        private Color _flashShown = Color.clear;
        private float _glowShown = -1f;

        public int Index => _index;
        public MatchRunner Runner => _runner;
        public Renderer Body => _renderer;
        public FighterRig Rig => _rig != null && _rig.IsValid ? _rig : null;

        public void Setup(Renderer body, Transform hitboxMarker)
        {
            _renderer = body;
            _hitboxMarker = hitboxMarker;
        }

        public void Bind(MatchRunner runner, int index)
        {
            if (_runner != null) _runner.SimEventRaised -= OnSimEvent;
            _runner = runner;
            _index = index;
            if (_runner != null && isActiveAndEnabled) _runner.SimEventRaised += OnSimEvent;
            Sync();
        }

        private void OnEnable()
        {
            if (_runner != null) _runner.SimEventRaised += OnSimEvent;
        }

        private void OnDisable()
        {
            if (_runner != null) _runner.SimEventRaised -= OnSimEvent;
        }

        /// <summary> Цвет команды: капюшон, шарф, свечение перчаток и следы ударов (только режим модели). </summary>
        public void SetTeam(Color team)
        {
            if (Rig != null) Rig.SetTrailColor(team);
            if (!_shaded) return;
            _renderer.GetPropertyBlock(_props);
            _props.SetColor(TeamColorId, team);
            _props.SetColor(GlowColorId, Color.Lerp(team, Color.white, 0.35f));
            _renderer.SetPropertyBlock(_props);
        }

        private void Awake()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
            if (_rig == null) _rig = GetComponent<FighterRig>();
            var mat = _renderer != null ? _renderer.sharedMaterial : null;
            if (mat == null) return;
            if (mat.HasProperty(RimColorId) && mat.HasProperty(FlashColorId))
            {
                _shaded = true;
                _props = new MaterialPropertyBlock();
                return;
            }
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
            var rig = Rig;
            if (facing.sqrMagnitude > 1e-6f)
            {
                var rot = Quaternion.LookRotation(facing, Vector3.up);
                // KO — капсула лежит (модель падает сама, анимацией).
                transform.rotation = f.State == ActionState.Dead && rig == null ? rot * Quaternion.Euler(-90f, 0f, 0f) : rot;
            }

            var phase = _runner.Sim.AttackPhaseOf(_runner.State, _index);
            UpdateHitboxMarker(phase);
            if (rig != null) rig.Pose(_runner, _index, Time.deltaTime);
            if (_shaded) UpdateShading(f, rig != null ? rig.GlowBoost : 0f);
            else SetColor(ColorFor(f, phase));
        }

        // ---------- режим модели ----------

        private void OnSimEvent(SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.Hit when e.Target == _index:
                    Flash(new Color(1f, 1f, 1f, 0.85f));
                    break;
                case SimEventType.Blocked when e.Target == _index:
                    Flash(new Color(_block.r, _block.g, _block.b, 0.35f));
                    break;
                case SimEventType.GuardBreak when e.Target == _index:
                    Flash(new Color(_guardBroken.r, _guardBroken.g, _guardBroken.b, 0.7f));
                    break;
                case SimEventType.Parried when e.Actor == _index:
                    Flash(new Color(1f, 1f, 1f, 0.6f));
                    break;
            }
        }

        private void Flash(Color c)
        {
            _flash = c;
            _flashStart = Time.time;
        }

        private void UpdateShading(in FighterSim f, float glowBoost)
        {
            var rim = RimFor(f);
            var flash = Color.clear;
            if (_flashStart >= 0f)
            {
                float t = (Time.time - _flashStart) / FlashSeconds;
                if (t >= 1f) _flashStart = -1f;
                else flash = new Color(_flash.r, _flash.g, _flash.b, _flash.a * (1f - t * t));
            }
            float glow = _glowIntensity * (1f + glowBoost) * (f.State == ActionState.Dead ? 0.25f : 1f);
            if (rim == _rimShown && flash == _flashShown && Mathf.Approximately(glow, _glowShown)) return;
            _rimShown = rim;
            _flashShown = flash;
            _glowShown = glow;
            _renderer.GetPropertyBlock(_props);
            _props.SetColor(RimColorId, rim);
            _props.SetColor(FlashColorId, flash);
            _props.SetFloat(GlowIntensityId, glow);
            _renderer.SetPropertyBlock(_props);
        }

        /// <summary> Ободок: то, что важно для решения противника и чего не видно по позе. a — сила. </summary>
        private Color RimFor(in FighterSim f)
        {
            switch (f.State)
            {
                case ActionState.Parry:
                {
                    int window = Mathf.Max(1, _runner.Sim.Setup.Fighters[_index].ParryWindowTicks);
                    return new Color(1f, 1f, 1f, 1f - 0.6f * f.StateTicks / window);
                }
                case ActionState.Block: return new Color(_block.r, _block.g, _block.b, 0.35f);
                case ActionState.ParryRecovery: return new Color(_vulnerable.r, _vulnerable.g, _vulnerable.b, 0.5f);
                case ActionState.Dodge:
                case ActionState.Cast:
                    // неуязвимость переката и телепорта
                    return _runner.Sim.HasIFrames(_runner.State, _index) ? new Color(_dodge.r, _dodge.g, _dodge.b, 0.9f) : Color.clear;
                case ActionState.Hitstun:
                case ActionState.ParryStunned:
                    return new Color(1f, 0.2f, 0.15f, 0.55f);
                case ActionState.GuardBroken:
                    return new Color(_guardBroken.r, _guardBroken.g, _guardBroken.b, 0.6f + 0.4f * Mathf.Sin(Time.time * 18f));
                case ActionState.Dead: return Color.clear;
                default: return f.StunImmunityTicks > 0 ? new Color(_stunImmune.r, _stunImmune.g, _stunImmune.b, 0.45f) : Color.clear;
            }
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
