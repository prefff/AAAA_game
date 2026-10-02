using Game.Simulation;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Процедурная анимация бойца. Поза — чистая функция состояния симуляции (состояние, StateTicks, frame data
    /// удара/скилла) этого кадра: никакого Animator, клипов и смешивания во времени. Отсюда три свойства, ради
    /// которых она так устроена:
    ///   - отклик: поза меняется в том же кадре, что и состояние; startup удара виден с первого тика (замах сразу
    ///     заметен), active — резкий «щелчок» в выпад;
    ///   - честность: анимация растягивается ровно под кадры удара/скилла (скорость атаки, баланс правятся числами);
    ///     стоп-кадр симуляции сам замораживает позу;
    ///   - цена: ~20 поворотов костей в LateUpdate, без компонента Animator.
    /// Время кадра влияет только на «косметику», которая не несёт игровой информации: дыхание в стойке, шаг ног
    /// (фаза от пройденного пути — ноги не скользят), отставание шарфа и пояса.
    ///
    /// Ноги — аналитический IK на две кости: стопы ставятся в точки (стойка или цикл бега по направлению движения),
    /// поэтому стрейф в любую сторону и бег спиной вперёд выглядят правильно при любом повороте корпуса — удар и
    /// каст не разворачивают бойца, а ноги продолжают бежать.
    ///
    /// Повороты задаются в осях модели (x — вправо, y — вверх, z — вперёд) поверх позы привязки, иерархически:
    /// поворот предплечья — относительно плеча. Кости ищутся по имени (см. Tools/Blender/build_hero.py).
    /// </summary>
    public sealed class FighterRig : MonoBehaviour
    {
        private enum B
        {
            Hips, Spine, Chest, Neck, Head,
            UpperArmL, ForearmL, HandL, UpperArmR, ForearmR, HandR,
            ThighL, ShinL, FootL, ThighR, ShinR, FootR,
            Scarf, Sash,
            Count,
        }

        private static readonly string[] BoneNames =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "UpperArm.L", "Forearm.L", "Hand.L", "UpperArm.R", "Forearm.R", "Hand.R",
            "Thigh.L", "Shin.L", "Foot.L", "Thigh.R", "Shin.R", "Foot.R",
            "Scarf", "Sash",
        };

        private const int N = (int)B.Count;

        [Tooltip("Корень модели (экземпляр FBX). Его поворачивают перекат и падение.")]
        [SerializeField] private Transform _model;
        [Tooltip("Длина полного цикла шагов бега, м (две ступни).")]
        [SerializeField] private float _strideLength = 1.4f;
        [Tooltip("Скорость, при которой ноги полностью в цикле бега, м/с.")]
        [SerializeField] private float _runSpeed = 4.5f;
        [Tooltip("Время жизни следа кулака, с.")]
        [SerializeField] private float _trailTime = 0.11f;

        // Привязка.
        private Transform[] _bones;
        private Quaternion[] _bindLocal;
        private Quaternion[] _parentBindModel;
        private Vector3[] _bindModelPos;
        private Vector3 _hipsBindLocalPos;
        private Quaternion _hipsParentBindModel;
        private Vector3 _modelBindPos;
        private Quaternion _modelBindRot;
        private float _thighLength, _shinLength;

        // Поза кадра.
        private readonly Quaternion[] _d = new Quaternion[N];
        private Vector3 _hipsOffset;
        private Quaternion _bodyRot;
        private Vector3 _bodyPivot;
        private Vector3 _bodyOffset;
        private Vector3 _footTargetL, _footTargetR;
        private Quaternion _footYawL, _footYawR;

        // Движение (только визуально).
        private Vector3 _lastWorldPos;
        private int _lastTick = int.MinValue;
        private Vector3 _velocity;      // м/с, мир, по тикам симуляции
        private float _runWeight;
        private float _phase;
        private Vector3 _moveLocal = Vector3.forward;
        private Vector2 _scarf, _scarfVel, _sash, _sashVel;

        private TrailRenderer _trailL, _trailR;

        /// <summary> Насколько усилить свечение (телеграф тяжёлого удара и каста); 0 — обычное. </summary>
        public float GlowBoost { get; private set; }
        public bool IsValid => _bones != null;
        public Transform Model => _model;
        public Transform RightHand => IsValid ? _bones[(int)B.HandR] : null;

        public void Setup(Transform model) => _model = model;

        private void Awake()
        {
            if (_model == null && transform.childCount > 0) _model = transform.GetChild(0);
            if (_model == null) return;

            var bones = new Transform[N];
            for (int i = 0; i < N; i++)
            {
                bones[i] = FindDeep(_model, BoneNames[i]);
                if (bones[i] == null)
                {
                    Debug.LogWarning($"{name}: нет кости {BoneNames[i]} — процедурная анимация выключена", this);
                    return;
                }
            }

            _bones = bones;
            _bindLocal = new Quaternion[N];
            _parentBindModel = new Quaternion[N];
            _bindModelPos = new Vector3[N];
            var inv = Quaternion.Inverse(_model.rotation);
            for (int i = 0; i < N; i++)
            {
                _bindLocal[i] = bones[i].localRotation;
                _parentBindModel[i] = inv * bones[i].parent.rotation;
                _bindModelPos[i] = _model.InverseTransformPoint(bones[i].position);
            }
            _hipsBindLocalPos = bones[(int)B.Hips].localPosition;
            _hipsParentBindModel = _parentBindModel[(int)B.Hips];
            _modelBindPos = _model.localPosition;
            _modelBindRot = _model.localRotation;
            _thighLength = Vector3.Distance(_bindModelPos[(int)B.ThighR], _bindModelPos[(int)B.ShinR]);
            _shinLength = Vector3.Distance(_bindModelPos[(int)B.ShinR], _bindModelPos[(int)B.FootR]);

            _trailL = CreateTrail(bones[(int)B.HandL]);
            _trailR = CreateTrail(bones[(int)B.HandR]);
        }

        private TrailRenderer CreateTrail(Transform hand)
        {
            var lib = ArtLibrary.Instance;
            if (lib == null || lib.Glow == null) return null;
            var go = new GameObject("FistTrail");
            go.transform.SetParent(hand, false);
            // центр кулака — на 6 см дальше запястья вниз по руке в позе привязки
            go.transform.position = _model.TransformPoint(_bindModelPos[System.Array.IndexOf(_bones, hand)] + Vector3.down * 0.06f);
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = lib.Glow;
            trail.time = _trailTime;
            trail.minVertexDistance = 0.04f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.24f), new Keyframe(1f, 0f));
            trail.numCapVertices = 0;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            return trail;
        }

        /// <summary> Цвет следов кулаков (цвет команды). </summary>
        public void SetTrailColor(Color color)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.Lerp(color, Color.white, 0.5f), 0f), new GradientColorKey(color, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            if (_trailL != null) _trailL.colorGradient = g;
            if (_trailR != null) _trailR.colorGradient = g;
        }

        // ================= Поза =================

        /// <summary> Вызывается видом бойца после того, как он поставил позицию и поворот корня. </summary>
        public void Pose(MatchRunner runner, int index, float dt)
        {
            if (!IsValid || runner == null || runner.State == null) return;
            var state = runner.State;
            var sim = runner.Sim;
            ref readonly var f = ref state.Fighters[index];
            ref readonly var opp = ref state.Fighters[1 - index];

            ResetPose();
            Measure(state.Tick, dt);
            Locomotion();
            GlowBoost = 0f;
            bool trailL = false, trailR = false;
            bool lookAtOpponent = true;

            switch (f.State)
            {
                case ActionState.Idle:
                case ActionState.Move:
                    Guard(1f);
                    break;
                case ActionState.Attack:
                    lookAtOpponent = false;
                    Attack(f, sim.CurrentAttack(state, index), ref trailL, ref trailR);
                    break;
                case ActionState.Block:
                    lookAtOpponent = false;
                    BlockPose(0f);
                    break;
                case ActionState.Parry:
                    lookAtOpponent = false;
                    BlockPose(1f - Mathf.Clamp01(f.StateTicks / 4f)); // «толчок» в первые кадры окна
                    GlowBoost = 1.5f * (1f - Mathf.Clamp01(f.StateTicks / (float)Mathf.Max(1, sim.Setup.Fighters[index].ParryWindowTicks)));
                    break;
                case ActionState.ParryRecovery:
                    ArmsBoth(ArmVulnerable);
                    Crouch(0.03f);
                    break;
                case ActionState.Dodge:
                    lookAtOpponent = false;
                    Roll(f, sim.Setup.Fighters[index]);
                    break;
                case ActionState.Hitstun:
                    lookAtOpponent = false;
                    Recoil(f, opp, Mathf.Exp(-f.StateTicks / 9f), 1f);
                    break;
                case ActionState.ParryStunned:
                    lookAtOpponent = false;
                    Stagger(f, opp, f.StateTicks, false);
                    break;
                case ActionState.GuardBroken:
                    lookAtOpponent = false;
                    Stagger(f, opp, f.StateTicks, true);
                    break;
                case ActionState.Cast:
                    lookAtOpponent = false;
                    Cast(state, sim, index, f, ref trailL, ref trailR);
                    break;
                case ActionState.Dead:
                    lookAtOpponent = false;
                    Dead(f.StateTicks);
                    break;
            }

            if (f.HitstopTicks > 0 && IsReceiving(f.State))
            {
                // дрожь в стоп-кадре у получившего удар — классика файтингов, читается как «попало»
                _bodyOffset += new Vector3(Random.Range(-0.03f, 0.03f), 0f, Random.Range(-0.03f, 0.03f));
            }
            if (lookAtOpponent && opp.IsAlive) LookAt(f, opp);
            Breathe(f.State);
            Secondary(dt);
            SolveLegs();
            Apply();

            if (_trailL != null && _trailL.emitting != trailL) _trailL.emitting = trailL;
            if (_trailR != null && _trailR.emitting != trailR) _trailR.emitting = trailR;
        }

        private static bool IsReceiving(ActionState s) =>
            s == ActionState.Hitstun || s == ActionState.Block || s == ActionState.GuardBroken || s == ActionState.ParryStunned || s == ActionState.Dead;

        private void ResetPose()
        {
            for (int i = 0; i < N; i++) _d[i] = Quaternion.identity;
            _hipsOffset = Vector3.zero;
            _bodyRot = Quaternion.identity;
            _bodyPivot = new Vector3(0f, 0.55f, 0f);
            _bodyOffset = Vector3.zero;
            _footYawL = _footYawR = Quaternion.identity;
        }

        // ---------- движение ----------

        private void Measure(int tick, float dt)
        {
            var pos = transform.position;
            if (_lastTick == int.MinValue || tick < _lastTick || tick - _lastTick > 30)
            {
                _velocity = Vector3.zero;
            }
            else if (tick != _lastTick)
            {
                var v = (pos - _lastWorldPos) / ((tick - _lastTick) / (float)SimTime.TickRate);
                v.y = 0f;
                _velocity = v;
                // фаза шага — от пройденного пути, поэтому стопы не скользят
                _phase += (pos - _lastWorldPos).magnitude * (Mathf.PI * 2f) / _strideLength;
                if (_phase > Mathf.PI * 200f) _phase -= Mathf.PI * 200f;
            }
            if (tick != _lastTick)
            {
                _lastTick = tick;
                _lastWorldPos = pos;
            }

            float speed = _velocity.magnitude;
            float target = Mathf.Clamp01(speed / _runSpeed);
            // Короткое сглаживание только веса цикла ног (не позиции!): микро-стрейфы не дёргают ноги.
            _runWeight = Mathf.MoveTowards(_runWeight, target, dt * 12f);
            if (speed > 0.2f)
            {
                var local = transform.InverseTransformDirection(_velocity / speed);
                local.y = 0f;
                if (local.sqrMagnitude > 1e-4f) _moveLocal = local.normalized;
            }
        }

        private void Locomotion()
        {
            float w = _runWeight;
            // Стойка: левая нога впереди, колени согнуты.
            var stanceR = new Vector3(0.15f, 0f, -0.10f);
            var stanceL = new Vector3(-0.13f, 0f, 0.13f);
            float a = _strideLength * 0.25f;
            var side = new Vector3(0.12f, 0f, 0f);
            float pr = _phase, pl = _phase + Mathf.PI;
            var runR = side + _moveLocal * (-a * Mathf.Cos(pr)) + Vector3.up * (0.2f * Mathf.Max(0f, Mathf.Sin(pr)));
            var runL = -side + _moveLocal * (-a * Mathf.Cos(pl)) + Vector3.up * (0.2f * Mathf.Max(0f, Mathf.Sin(pl)));
            _footTargetR = Vector3.Lerp(stanceR, runR, w);
            _footTargetL = Vector3.Lerp(stanceL, runL, w);

            _hipsOffset.y = Mathf.Lerp(-0.06f, -0.11f + 0.05f * Mathf.Abs(Mathf.Sin(pr)), w);
            var leanAxis = Vector3.Cross(Vector3.up, _moveLocal);
            _d[(int)B.Spine] = Quaternion.AngleAxis(13f * w, leanAxis);
            _d[(int)B.Hips] = Quaternion.Euler(0f, 9f * w * Mathf.Sin(pr), 0f);
            _d[(int)B.Chest] = Quaternion.Euler(0f, -9f * w * Mathf.Sin(pr), 0f);
            // стопы смотрят вперёд корпуса, при беге боком — чуть по ходу
            float yaw = Mathf.Clamp(Mathf.Atan2(_moveLocal.x, Mathf.Abs(_moveLocal.z) + 0.3f) * Mathf.Rad2Deg, -40f, 40f) * w;
            _footYawR = Quaternion.Euler(0f, yaw + 8f, 0f);
            _footYawL = Quaternion.Euler(0f, yaw - 14f, 0f);
        }

        private void Crouch(float depth) => _hipsOffset.y -= depth;

        // ---------- руки ----------

        private struct Arm
        {
            public Quaternion U, F, H;
            public Arm(float ux, float uy, float uz, float fx, float hx = 0f)
            {
                U = Quaternion.Euler(ux, uy, uz);
                F = Quaternion.Euler(fx, 0f, 0f);
                H = Quaternion.Euler(hx, 0f, 0f);
            }
            public static Arm Lerp(in Arm a, in Arm b, float t) => new()
            {
                U = Quaternion.SlerpUnclamped(a.U, b.U, t),
                F = Quaternion.SlerpUnclamped(a.F, b.F, t),
                H = Quaternion.SlerpUnclamped(a.H, b.H, t),
            };
        }

        // Позы правой руки в осях модели; левая — зеркально. Рука в привязке висит вдоль тела:
        // x < 0 — вперёд, z > 0 — в сторону, y < 0 — к центру; предплечье x < 0 — сгиб локтя вперёд.
        private static readonly Arm ArmGuard = new(-40f, -15f, 18f, -110f);
        private static readonly Arm ArmJabWind = new(-22f, 0f, 26f, -128f, 10f);
        private static readonly Arm ArmJabHit = new(-88f, -7f, 2f, -4f);
        private static readonly Arm ArmHeavyWind = new(-12f, 50f, 80f, -80f);
        private static readonly Arm ArmHeavyHit = new(-84f, 12f, 30f, -10f);
        private static readonly Arm ArmBlock = new(-72f, -38f, 8f, -100f);
        private static readonly Arm ArmVulnerable = new(-18f, 0f, 26f, -55f);
        private static readonly Arm ArmFlung = new(-135f, 0f, 55f, -30f);
        private static readonly Arm ArmThrowWind = new(28f, 12f, 22f, -95f, -10f);
        private static readonly Arm ArmThrowHit = new(-92f, -4f, 0f, 0f, -40f);
        private static readonly Arm ArmPoint = new(-82f, -12f, 4f, -12f);
        private static readonly Arm ArmRaise = new(-168f, 0f, 16f, -25f);
        private static readonly Arm ArmSlam = new(-68f, -14f, 6f, -6f, -40f);
        private static readonly Arm ArmTuck = new(-60f, -25f, 10f, -125f);
        private static readonly Arm ArmSplayed = new(-12f, 0f, 78f, -12f);
        private static readonly Arm ArmLow = new(8f, 0f, 32f, -40f);

        private void ArmR(in Arm a)
        {
            _d[(int)B.UpperArmR] = a.U;
            _d[(int)B.ForearmR] = a.F;
            _d[(int)B.HandR] = a.H;
        }

        private void ArmL(in Arm a)
        {
            _d[(int)B.UpperArmL] = Mirror(a.U);
            _d[(int)B.ForearmL] = Mirror(a.F);
            _d[(int)B.HandL] = Mirror(a.H);
        }

        private void ArmsBoth(in Arm a)
        {
            ArmR(a);
            ArmL(a);
        }

        private void ArmSide(bool right, in Arm a)
        {
            if (right) ArmR(a);
            else ArmL(a);
        }

        /// <summary> Отражение поворота относительно плоскости YZ (правая сторона → левая). </summary>
        private static Quaternion Mirror(Quaternion q) => new(q.x, -q.y, -q.z, q.w);

        private void Guard(float weight)
        {
            float swing = 22f * _runWeight * Mathf.Sin(_phase);
            ArmR(Arm.Lerp(ArmLow, ArmGuard, weight));
            ArmL(Arm.Lerp(ArmLow, ArmGuard, weight));
            // при беге руки работают в такт ногам
            _d[(int)B.UpperArmR] = Quaternion.Euler(swing, 0f, 0f) * _d[(int)B.UpperArmR];
            _d[(int)B.UpperArmL] = Quaternion.Euler(-swing, 0f, 0f) * _d[(int)B.UpperArmL];
        }

        // ---------- удары ----------

        private void Attack(in FighterSim f, AttackSpec atk, ref bool trailL, ref bool trailR)
        {
            if (atk == null)
            {
                Guard(1f);
                return;
            }
            int t = f.StateTicks;
            int s = Mathf.Max(1, atk.StartupTicks), a = Mathf.Max(1, atk.ActiveTicks), r = Mathf.Max(1, atk.RecoveryTicks);
            bool heavy = atk.Kind == AttackKind.Heavy;

            if (!heavy)
            {
                // Серия лёгких чередует руки: первый — левой (передней), второй — правой и т. д.
                bool right = f.LightChain % 2 == 0;
                var other = ArmGuard;
                Arm arm;
                float twist; // поворот корпуса за ударом
                float sign = right ? 1f : -1f;
                if (t < s)
                {
                    float u = EaseOut((t + 1f) / s); // уже в первом тике замах заметен
                    arm = Arm.Lerp(ArmGuard, ArmJabWind, u);
                    twist = 14f * u;
                }
                else if (t < s + a)
                {
                    float u = (t - s + 1f) / a;
                    arm = Arm.Lerp(ArmJabWind, ArmJabHit, 1.08f - 0.08f * u); // щелчок с перелётом
                    twist = -24f;
                    _hipsOffset.z += 0.06f;
                    if (right) trailR = true; else trailL = true;
                }
                else
                {
                    float u = EaseInOut((t - s - a + 1f) / r);
                    arm = Arm.Lerp(ArmJabHit, ArmGuard, u);
                    twist = Mathf.Lerp(-24f, 0f, u);
                    _hipsOffset.z += 0.06f * (1f - u);
                    if (u < 0.35f) { if (right) trailR = true; else trailL = true; }
                }
                ArmSide(right, arm);
                ArmSide(!right, other);
                _d[(int)B.Chest] = Quaternion.Euler(4f, twist * sign, 0f) * _d[(int)B.Chest];
                _d[(int)B.Spine] = Quaternion.Euler(0f, twist * 0.35f * sign, 0f) * _d[(int)B.Spine];
                return;
            }

            // Тяжёлый: широкий хук правой с разворотом корпуса и выпадом. Startup длинный и хорошо виден
            // (телеграф для парирования): глубокий замах + нарастающее свечение перчаток.
            Arm right2;
            float yaw, lean, lunge, crouch;
            if (t < s)
            {
                float u = EaseOut((t + 1f) / s);
                right2 = Arm.Lerp(ArmGuard, ArmHeavyWind, u);
                yaw = 48f * u; lean = 6f * u; lunge = -0.05f * u; crouch = 0.07f * u;
                GlowBoost = 2.5f * ((t + 1f) / s);
            }
            else if (t < s + a)
            {
                float u = (t - s + 1f) / a;
                right2 = Arm.Lerp(ArmHeavyWind, ArmHeavyHit, 1f);
                yaw = Mathf.Lerp(30f, -58f, EaseOut(u));
                lean = 16f; lunge = 0.16f; crouch = 0.09f;
                GlowBoost = 2.5f;
                trailR = true;
            }
            else
            {
                float u = EaseInOut((t - s - a + 1f) / r);
                right2 = Arm.Lerp(ArmHeavyHit, ArmGuard, u);
                yaw = Mathf.Lerp(-58f, 0f, u); lean = Mathf.Lerp(16f, 0f, u);
                lunge = Mathf.Lerp(0.16f, 0f, u); crouch = Mathf.Lerp(0.09f, 0f, u);
                GlowBoost = 2.5f * (1f - u);
                if (u < 0.3f) trailR = true;
            }
            ArmR(right2);
            ArmL(ArmGuard);
            _d[(int)B.Chest] = Quaternion.Euler(0f, -yaw * 0.6f, 0f) * _d[(int)B.Chest];
            _d[(int)B.Spine] = Quaternion.Euler(lean, -yaw * 0.4f, 0f) * _d[(int)B.Spine];
            _hipsOffset.z += lunge;
            Crouch(crouch);
        }

        // ---------- защита ----------

        private void BlockPose(float push)
        {
            var arm = Arm.Lerp(ArmBlock, ArmJabHit, push * 0.25f);
            ArmsBoth(arm);
            _d[(int)B.Spine] = Quaternion.Euler(10f, 0f, 0f) * _d[(int)B.Spine];
            _d[(int)B.Head] = Quaternion.Euler(12f, 0f, 0f);
            Crouch(0.06f);
        }

        private void Roll(in FighterSim f, FighterSpec spec)
        {
            float u = Mathf.Clamp01((f.StateTicks + 1f) / Mathf.Max(1, spec.DodgeTicks));
            var dir = new Vector3(f.DodgeDirection.X.ToFloat(), 0f, f.DodgeDirection.Y.ToFloat());
            var local = dir.sqrMagnitude > 1e-4f ? transform.InverseTransformDirection(dir) : Vector3.forward;
            local.y = 0f;
            local = local.sqrMagnitude > 1e-4f ? local.normalized : Vector3.forward;

            float tuck = Mathf.Sin(u * Mathf.PI); // сгруппировался в середине переката
            float angle = 360f * EaseInOut(u);
            _bodyRot = Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, local));
            _bodyPivot = new Vector3(0f, 0.5f, 0f);
            ArmsBoth(Arm.Lerp(ArmGuard, ArmTuck, Mathf.Clamp01(tuck * 1.6f)));
            _d[(int)B.Spine] = Quaternion.Euler(30f * tuck, 0f, 0f);
            _d[(int)B.Head] = Quaternion.Euler(25f * tuck, 0f, 0f);
            _hipsOffset.y = Mathf.Lerp(_hipsOffset.y, -0.32f, tuck);
            _footTargetR = Vector3.Lerp(_footTargetR, new Vector3(0.12f, 0.42f, 0.12f), tuck);
            _footTargetL = Vector3.Lerp(_footTargetL, new Vector3(-0.12f, 0.46f, 0.16f), tuck);
        }

        private void Recoil(in FighterSim f, in FighterSim opp, float k, float strength)
        {
            var away = AwayFrom(f, opp);
            var axis = Vector3.Cross(Vector3.up, away);
            _d[(int)B.Spine] = Quaternion.AngleAxis(22f * k * strength, axis);
            _d[(int)B.Head] = Quaternion.AngleAxis(18f * k * strength, axis);
            ArmsBoth(Arm.Lerp(ArmGuard, ArmVulnerable, k));
            _hipsOffset += away * (0.06f * k);
            Crouch(0.05f * k);
        }

        private void Stagger(in FighterSim f, in FighterSim opp, int ticks, bool broken)
        {
            // спарирован / блок пробит: руки разбросаны, корпус откинут, «плывёт»
            float k = Mathf.Clamp01(1f - ticks / (broken ? 45f : 30f));
            float wobble = Mathf.Sin(Time.time * 9f) * (broken ? 9f : 5f);
            var away = AwayFrom(f, opp);
            var axis = Vector3.Cross(Vector3.up, away);
            var arm = Arm.Lerp(ArmVulnerable, ArmFlung, Mathf.Clamp01(k * 1.5f));
            ArmsBoth(arm);
            _d[(int)B.Spine] = Quaternion.AngleAxis(20f * k + 4f, axis) * Quaternion.Euler(0f, 0f, wobble);
            _d[(int)B.Head] = Quaternion.Euler(-16f * k, wobble * 1.5f, -wobble);
            _hipsOffset += away * (0.05f * k);
        }

        private void Dead(int ticks)
        {
            float u = EaseOut(Mathf.Clamp01(ticks / 24f));
            _bodyRot = Quaternion.AngleAxis(-84f * u, Vector3.right);
            _bodyPivot = Vector3.zero;
            _bodyOffset = new Vector3(0f, 0.12f * u, 0f);
            ArmsBoth(Arm.Lerp(ArmVulnerable, ArmSplayed, u));
            _d[(int)B.Head] = Quaternion.Euler(-20f * u, 25f * u, 0f);
            _footTargetR = Vector3.Lerp(_footTargetR, new Vector3(0.2f, 0f, 0.05f), u);
            _footTargetL = Vector3.Lerp(_footTargetL, new Vector3(-0.16f, 0.05f, -0.05f), u);
            _hipsOffset.y = Mathf.Lerp(_hipsOffset.y, 0f, u);
        }

        // ---------- скиллы ----------

        private void Cast(GameState state, FightSimulation sim, int index, in FighterSim f, ref bool trailL, ref bool trailR)
        {
            var sk = sim.CurrentSkill(state, index);
            if (sk == null)
            {
                Guard(1f);
                return;
            }
            int t = f.StateTicks;
            int s = Mathf.Max(1, sk.StartupTicks), r = Mathf.Max(1, sk.RecoveryTicks);
            bool fired = f.SkillFired;
            float up = fired ? 0f : EaseOut((t + 1f) / s);              // подготовка
            float rec = fired ? EaseInOut(Mathf.Clamp01((t - s + 1f) / r)) : 0f; // возврат
            float snap = fired ? 1f - rec : 0f;                          // сам выпуск

            // Корпус — к цели (каст не разворачивает бойца, ноги бегут дальше).
            if (!fired || sk.Kind != SkillKind.Blink)
            {
                var target = sim.PreviewAim(state, index, (int)f.CastSlot, f.SkillAim, out _);
                var dir = new Vector3((target.X - f.Position.X).ToFloat(), 0f, (target.Y - f.Position.Y).ToFloat());
                if (dir.sqrMagnitude > 0.01f)
                {
                    var local = transform.InverseTransformDirection(dir);
                    float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -80f, 80f);
                    _d[(int)B.Spine] = Quaternion.Euler(0f, yaw * 0.4f, 0f) * _d[(int)B.Spine];
                    _d[(int)B.Chest] = Quaternion.Euler(0f, yaw * 0.6f, 0f) * _d[(int)B.Chest];
                }
            }

            switch (sk.Kind)
            {
                case SkillKind.Projectile:
                    // Искра: правая рука заряжается у бедра, левая указывает цель; выпуск — толчок ладонью.
                    if (!fired)
                    {
                        ArmR(Arm.Lerp(ArmGuard, ArmThrowWind, up));
                        ArmL(Arm.Lerp(ArmGuard, ArmPoint, up));
                        _d[(int)B.Chest] = Quaternion.Euler(0f, 22f * up, 0f) * _d[(int)B.Chest];
                        GlowBoost = 2f * up;
                    }
                    else
                    {
                        ArmR(Arm.Lerp(ArmThrowHit, ArmGuard, rec));
                        ArmL(Arm.Lerp(ArmVulnerable, ArmGuard, rec));
                        _d[(int)B.Chest] = Quaternion.Euler(0f, -20f * snap, 0f) * _d[(int)B.Chest];
                        _hipsOffset.z += 0.07f * snap;
                        GlowBoost = 1.5f * snap;
                        trailR = rec < 0.3f;
                    }
                    break;

                case SkillKind.Blink:
                    // Скачок: резко присел — исчез — вырос в точке, руки ещё разведены.
                    if (!fired)
                    {
                        ArmsBoth(Arm.Lerp(ArmGuard, ArmLow, up));
                        Crouch(0.22f * up);
                        _d[(int)B.Spine] = Quaternion.Euler(22f * up, 0f, 0f) * _d[(int)B.Spine];
                        GlowBoost = 2.5f * up;
                    }
                    else
                    {
                        ArmsBoth(Arm.Lerp(ArmSplayed, ArmGuard, rec));
                        Crouch(0.12f * snap);
                        GlowBoost = 2f * snap;
                    }
                    break;

                default:
                    // Сверхновая (и прочие области): руки вверх, ядро разгорается — удар обеими руками вниз к цели.
                    if (!fired)
                    {
                        ArmsBoth(Arm.Lerp(ArmGuard, ArmRaise, up));
                        _d[(int)B.Spine] = Quaternion.Euler(-12f * up, 0f, 0f) * _d[(int)B.Spine];
                        _d[(int)B.Head] = Quaternion.Euler(-15f * up, 0f, 0f);
                        GlowBoost = 3.5f * up;
                    }
                    else
                    {
                        ArmsBoth(Arm.Lerp(ArmSlam, ArmGuard, rec));
                        _d[(int)B.Spine] = Quaternion.Euler(24f * snap, 0f, 0f) * _d[(int)B.Spine];
                        Crouch(0.12f * snap);
                        GlowBoost = 3f * snap;
                        trailL = trailR = rec < 0.3f;
                    }
                    break;
            }
        }

        // ---------- косметика ----------

        private void LookAt(in FighterSim f, in FighterSim opp)
        {
            var dir = new Vector3((opp.Position.X - f.Position.X).ToFloat(), 0f, (opp.Position.Y - f.Position.Y).ToFloat());
            if (dir.sqrMagnitude < 0.01f) return;
            var local = transform.InverseTransformDirection(dir);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -65f, 65f);
            _d[(int)B.Neck] = Quaternion.Euler(0f, yaw * 0.4f, 0f) * _d[(int)B.Neck];
            _d[(int)B.Head] = Quaternion.Euler(0f, yaw * 0.6f, 0f) * _d[(int)B.Head];
        }

        private void Breathe(ActionState s)
        {
            if (s != ActionState.Idle && s != ActionState.Move && s != ActionState.Block) return;
            float b = Mathf.Sin(Time.time * 2.4f) * (1f - _runWeight);
            _hipsOffset.y += 0.008f * b;
            _d[(int)B.Chest] = Quaternion.Euler(2f * b, 0f, 0f) * _d[(int)B.Chest];
        }

        private void Secondary(float dt)
        {
            // Шарф и полы пояса отстают от движения (пружина, только картинка).
            var vLocal = transform.InverseTransformDirection(_velocity);
            if (_bodyRot != Quaternion.identity) vLocal *= 0.3f;
            var scarfTarget = new Vector2(Mathf.Clamp(8f + vLocal.z * 9f, -10f, 65f), Mathf.Clamp(-vLocal.x * 9f, -40f, 40f));
            var sashTarget = new Vector2(Mathf.Clamp(4f + vLocal.z * 7f, -20f, 50f), Mathf.Clamp(-vLocal.x * 6f, -30f, 30f));
            Spring(ref _scarf, ref _scarfVel, scarfTarget, dt, 90f, 11f);
            Spring(ref _sash, ref _sashVel, sashTarget, dt, 110f, 12f);
            _d[(int)B.Scarf] = Quaternion.Euler(_scarf.x, 0f, _scarf.y);
            _d[(int)B.Sash] = Quaternion.Euler(_sash.x, 0f, _sash.y);
        }

        private static void Spring(ref Vector2 x, ref Vector2 v, Vector2 target, float dt, float k, float c)
        {
            dt = Mathf.Min(dt, 0.05f);
            if (dt <= 0f) return;
            v += ((target - x) * k - v * c) * dt;
            x += v * dt;
        }

        private Vector3 AwayFrom(in FighterSim f, in FighterSim opp)
        {
            var dir = new Vector3((f.Position.X - opp.Position.X).ToFloat(), 0f, (f.Position.Y - opp.Position.Y).ToFloat());
            if (dir.sqrMagnitude < 1e-4f) return Vector3.back;
            var local = transform.InverseTransformDirection(dir.normalized);
            local.y = 0f;
            return local.sqrMagnitude > 1e-4f ? local.normalized : Vector3.back;
        }

        // ---------- IK ног и применение ----------

        private void SolveLegs()
        {
            var hipsPos = _bindModelPos[(int)B.Hips] + _hipsOffset;
            var hipsRot = _d[(int)B.Hips];
            SolveLeg(B.ThighR, B.ShinR, B.FootR, hipsPos, hipsRot, _footTargetR, _footYawR);
            SolveLeg(B.ThighL, B.ShinL, B.FootL, hipsPos, hipsRot, _footTargetL, _footYawL);
        }

        private void SolveLeg(B thigh, B shin, B foot, Vector3 hipsPos, Quaternion hipsRot, Vector3 footOffset, Quaternion footYaw)
        {
            var hip = hipsPos + hipsRot * (_bindModelPos[(int)thigh] - _bindModelPos[(int)B.Hips]);
            // цель — лодыжка: точка стопы на высоте лодыжки привязки
            var target = new Vector3(footOffset.x, _bindModelPos[(int)foot].y + footOffset.y, footOffset.z);
            var toTarget = target - hip;
            float l1 = _thighLength, l2 = _shinLength;
            float d = Mathf.Clamp(toTarget.magnitude, 0.05f, l1 + l2 - 0.001f);
            var dir = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.down;
            // колено сгибается вперёд (и чуть наружу)
            var pole = new Vector3(Mathf.Sign(_bindModelPos[(int)thigh].x) * 0.15f, 0f, 1f);
            var perp = pole - dir * Vector3.Dot(pole, dir);
            perp = perp.sqrMagnitude > 1e-6f ? perp.normalized : Vector3.forward;
            float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            var knee = hip + dir * (l1 * cosA) + perp * (l1 * sinA);
            var ankle = hip + dir * d;

            var thighAbs = Quaternion.FromToRotation(Vector3.down, (knee - hip).normalized);
            var shinAbs = Quaternion.FromToRotation(Vector3.down, (ankle - knee).normalized);
            _d[(int)thigh] = Quaternion.Inverse(hipsRot) * thighAbs;
            _d[(int)shin] = Quaternion.Inverse(thighAbs) * shinAbs;
            _d[(int)foot] = Quaternion.Inverse(shinAbs) * footYaw;
        }

        private void Apply()
        {
            for (int i = 0; i < N; i++)
            {
                var p = _parentBindModel[i];
                _bones[i].localRotation = Quaternion.Inverse(p) * _d[i] * p * _bindLocal[i];
            }
            _bones[(int)B.Hips].localPosition = _hipsBindLocalPos + Quaternion.Inverse(_hipsParentBindModel) * _hipsOffset;
            _model.localRotation = _bodyRot * _modelBindRot;
            _model.localPosition = _modelBindPos + _bodyPivot - _bodyRot * _bodyPivot + _bodyOffset;
        }

        // ---------- утилиты ----------

        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            float k = 1f - t;
            return 1f - k * k * k;
        }

        private static float EaseInOut(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
