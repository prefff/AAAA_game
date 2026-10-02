using Game.Simulation;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Эффекты боя по событиям симуляции: искры и вспышка попадания, волна блока, звезда парирования, осколки
    /// пробитого блока, взрыв KO, вспышки скиллов, пыль переката и телепорта.
    ///
    /// Дёшево: на всю арену пять систем частиц (искры, вспышки, кольца у лица, кольца на полу, пыль), частицы
    /// выпускаются вручную через Emit — ни объектов на эффект, ни Instantiate в бою. Только картинка: на симуляцию
    /// не влияет и ничего не задерживает. Эффекты стартуют в кадре события (тик идёт в Update, вид — в LateUpdate).
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class CombatFx : MonoBehaviour
    {
        private const float ContactHeight = 1.15f;

        private MatchRunner _runner;
        private Color[] _colors;
        private ParticleSystem _sparks, _flashes, _rings, _groundRings, _dust;
        private readonly Vector3[] _lastPos = new Vector3[GameState.FighterCount];
        private readonly ActionState[] _lastState = new ActionState[GameState.FighterCount];
        private bool _hasLast;

        public int EmittedTotal { get; private set; }

        public void Bind(MatchRunner runner, Color[] colors)
        {
            Unsubscribe();
            _runner = runner;
            _colors = colors;
            _hasLast = false;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake()
        {
            var lib = ArtLibrary.Instance;
            if (lib == null) return;
            _sparks = CreateSystem("Sparks", lib.Glow, ParticleSystemRenderMode.Stretch, 160, gravity: 1.5f, sizeCurve: Shrink());
            _flashes = CreateSystem("Flashes", lib.Star, ParticleSystemRenderMode.Billboard, 24, sizeCurve: Grow(0.5f, 1f));
            _rings = CreateSystem("Rings", lib.Ring, ParticleSystemRenderMode.Billboard, 24, sizeCurve: Grow(0.2f, 1f));
            _groundRings = CreateSystem("GroundRings", lib.Ring, ParticleSystemRenderMode.HorizontalBillboard, 24, sizeCurve: Grow(0.15f, 1f));
            _dust = CreateSystem("Dust", lib.Puff, ParticleSystemRenderMode.Billboard, 64, gravity: -0.15f, sizeCurve: Grow(0.5f, 1f), drag: 4f);
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_runner != null) _runner.SimEventRaised += OnSimEvent;
        }

        private void Unsubscribe()
        {
            if (_runner != null) _runner.SimEventRaised -= OnSimEvent;
        }

        // ---------- события ----------

        private void OnSimEvent(SimEvent e)
        {
            if (_sparks == null || _runner == null) return;
            var state = _runner.State;
            switch (e.Type)
            {
                case SimEventType.Hit:
                {
                    if (!Valid(e.Actor) || !Valid(e.Target)) break;
                    bool heavy = e.Slot < 0 && state.Fighters[e.Actor].Attack == AttackKind.Heavy;
                    var (at, dir) = Contact(e.Target, e.Actor);
                    var c = Color.Lerp(ColorOf(e.Actor), Color.white, 0.55f);
                    Flash(at, heavy ? 2.5f : 1.7f, c, heavy ? 0.16f : 0.12f);
                    Sparks(at, -dir, heavy ? 20 : 12, c, heavy ? 9f : 6.5f, 0.9f);
                    if (heavy) Ring(at, 1.8f, c, 0.18f);
                    break;
                }
                case SimEventType.Blocked:
                {
                    if (!Valid(e.Actor) || !Valid(e.Target)) break;
                    var (at, dir) = Contact(e.Target, e.Actor);
                    var c = new Color(0.55f, 0.75f, 1f);
                    Ring(at, 1.3f, c, 0.16f);
                    Sparks(at, dir, 6, c, 4f, 0.6f);
                    break;
                }
                case SimEventType.Parried:
                {
                    // Actor — защитник, Target — атакующий.
                    if (!Valid(e.Actor) || !Valid(e.Target)) break;
                    var (at, _) = Contact(e.Actor, e.Target);
                    var c = new Color(1f, 0.95f, 0.75f);
                    Flash(at, 2.8f, c, 0.2f);
                    Ring(at, 2.4f, Color.white, 0.22f);
                    GroundRing(Pos(e.Actor), 3.2f, c, 0.3f);
                    Sparks(at, Vector3.up, 22, c, 7f, 1f);
                    break;
                }
                case SimEventType.Evaded:
                {
                    if (!Valid(e.Target)) break;
                    var p = Pos(e.Target) + Vector3.up * 0.9f;
                    Ring(p, 1.4f, new Color(0.4f, 1f, 0.85f), 0.15f);
                    break;
                }
                case SimEventType.GuardBreak:
                {
                    if (!Valid(e.Target)) break;
                    var p = Pos(e.Target) + Vector3.up * ContactHeight;
                    var c = new Color(1f, 0.55f, 0.15f);
                    Flash(p, 2.3f, c, 0.2f);
                    Sparks(p, Vector3.up, 26, c, 7f, 1f);
                    GroundRing(Pos(e.Target), 2.5f, c, 0.3f);
                    break;
                }
                case SimEventType.KO:
                {
                    int who = Valid(e.Target) ? e.Target : e.Actor;
                    if (!Valid(who)) break;
                    var p = Pos(who) + Vector3.up;
                    Flash(p, 3.5f, Color.white, 0.25f);
                    GroundRing(Pos(who), 4.5f, Color.white, 0.45f);
                    Sparks(p, Vector3.up, 34, Color.Lerp(ColorOf(who), Color.white, 0.5f), 8f, 1f);
                    Dust(Pos(who), 8, 1.2f);
                    break;
                }
                case SimEventType.SkillFired:
                    OnSkillFired(e);
                    break;
                case SimEventType.ProjectileExpired:
                {
                    var p = ToWorld(e.Position) + Vector3.up;
                    var c = Color.Lerp(ColorOf(e.Actor), Color.white, 0.4f);
                    Flash(p, 1.1f, c, 0.12f);
                    Sparks(p, Vector3.up, 7, c, 4f, 1f);
                    break;
                }
                case SimEventType.ProjectileReflected:
                {
                    var p = ToWorld(e.Position) + Vector3.up;
                    Flash(p, 1.8f, Color.white, 0.15f);
                    Ring(p, 1.6f, Color.Lerp(ColorOf(e.Actor), Color.white, 0.5f), 0.18f);
                    break;
                }
                case SimEventType.ZoneDetonated:
                {
                    if (!Valid(e.Caster)) break;
                    var sk = _runner.Sim.Setup.Fighters[e.Caster].Skill(e.Slot);
                    float r = sk != null ? sk.ZoneRadius.ToFloat() : 2f;
                    var p = ToWorld(e.Position);
                    var c = Color.Lerp(ColorOf(e.Actor), Color.white, 0.35f);
                    GroundRing(p, r * 2.4f, c, 0.35f);
                    GroundRing(p, r * 1.4f, Color.white, 0.2f);
                    Flash(p + Vector3.up * 0.6f, r * 1.4f, c, 0.2f);
                    SparksDisc(p, r, 30, c);
                    Dust(p, 10, r * 0.7f);
                    break;
                }
            }
        }

        private void OnSkillFired(SimEvent e)
        {
            if (!Valid(e.Actor)) return;
            var sk = _runner.Sim.Setup.Fighters[e.Actor].Skill(e.Slot);
            if (sk == null) return;
            var c = Color.Lerp(ColorOf(e.Actor), Color.white, 0.4f);
            switch (sk.Kind)
            {
                case SkillKind.Projectile:
                {
                    var p = ToWorld(e.Position) + Vector3.up;
                    Flash(p, 1.2f, c, 0.12f);
                    break;
                }
                case SkillKind.Blink:
                {
                    // Откуда — позиция прошлого кадра (тик уже перенёс бойца), куда — текущая.
                    var from = _hasLast ? _lastPos[e.Actor] : Pos(e.Actor);
                    var to = Pos(e.Actor);
                    GroundRing(from, 1.8f, c, 0.25f);
                    GroundRing(to, 2.2f, c, 0.25f);
                    Flash(to + Vector3.up, 1.6f, c, 0.14f);
                    Dust(from, 5, 0.5f);
                    Streak(from + Vector3.up, to + Vector3.up, c);
                    break;
                }
                default:
                {
                    var p = Pos(e.Actor) + Vector3.up * 1.4f;
                    Flash(p, 1.8f, c, 0.16f);
                    break;
                }
            }
        }

        // Перекат — по смене состояния (отдельного события у него нет).
        private void LateUpdate()
        {
            if (_runner == null || _runner.State == null) return;
            var fighters = _runner.State.Fighters;
            for (int i = 0; i < fighters.Length; i++)
            {
                var pos = Pos(i);
                var s = fighters[i].State;
                if (_hasLast && _dust != null && s == ActionState.Dodge && _lastState[i] != ActionState.Dodge) Dust(_lastPos[i], 4, 0.45f);
                _lastPos[i] = pos;
                _lastState[i] = s;
            }
            _hasLast = true;
        }

        // ---------- примитивы эффектов ----------

        private void Flash(Vector3 at, float size, Color c, float life)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = at,
                startSize = size,
                startLifetime = life,
                startColor = c,
                rotation = Random.Range(0f, 90f),
                velocity = Vector3.zero,
                applyShapeToPosition = false,
            };
            _flashes.Emit(p, 1);
            EmittedTotal++;
        }

        private void Ring(Vector3 at, float size, Color c, float life)
        {
            _rings.Emit(new ParticleSystem.EmitParams { position = at, startSize = size, startLifetime = life, startColor = c, velocity = Vector3.zero }, 1);
            EmittedTotal++;
        }

        private void GroundRing(Vector3 at, float size, Color c, float life)
        {
            at.y = 0.05f;
            _groundRings.Emit(new ParticleSystem.EmitParams { position = at, startSize = size, startLifetime = life, startColor = c, velocity = Vector3.zero }, 1);
            EmittedTotal++;
        }

        private void Sparks(Vector3 at, Vector3 dir, int count, Color c, float speed, float spread)
        {
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up;
            for (int k = 0; k < count; k++)
            {
                var v = (dir + Random.insideUnitSphere * spread + Vector3.up * 0.25f).normalized * (speed * Random.Range(0.5f, 1.2f));
                _sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = v,
                    startSize = Random.Range(0.09f, 0.16f),
                    startLifetime = Random.Range(0.14f, 0.3f),
                    startColor = Color.Lerp(c, Color.white, Random.value * 0.5f),
                }, 1);
            }
            EmittedTotal += count;
        }

        private void SparksDisc(Vector3 center, float radius, int count, Color c)
        {
            for (int k = 0; k < count; k++)
            {
                var r = Random.insideUnitCircle * radius;
                _sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = center + new Vector3(r.x, 0.1f, r.y),
                    velocity = new Vector3(r.x * 1.5f, Random.Range(4f, 9f), r.y * 1.5f),
                    startSize = Random.Range(0.08f, 0.16f),
                    startLifetime = Random.Range(0.2f, 0.4f),
                    startColor = Color.Lerp(c, Color.white, Random.value * 0.4f),
                }, 1);
            }
            EmittedTotal += count;
        }

        private void Streak(Vector3 from, Vector3 to, Color c)
        {
            const int n = 12;
            for (int k = 0; k <= n; k++)
            {
                _sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = Vector3.Lerp(from, to, k / (float)n) + Random.insideUnitSphere * 0.08f,
                    velocity = Random.insideUnitSphere * 0.4f,
                    startSize = 0.22f,
                    startLifetime = 0.12f + 0.12f * k / n,
                    startColor = c,
                }, 1);
            }
            EmittedTotal += n + 1;
        }

        private void Dust(Vector3 at, int count, float radius)
        {
            for (int k = 0; k < count; k++)
            {
                var r = Random.insideUnitCircle * radius;
                _dust.Emit(new ParticleSystem.EmitParams
                {
                    position = at + new Vector3(r.x, 0.15f, r.y),
                    velocity = new Vector3(r.x, 0.3f, r.y) * 2.5f,
                    startSize = Random.Range(0.5f, 0.9f),
                    startLifetime = Random.Range(0.35f, 0.55f),
                    startColor = new Color(0.62f, 0.58f, 0.52f, 0.55f),
                    rotation = Random.Range(0f, 360f),
                }, 1);
            }
            EmittedTotal += count;
        }

        // ---------- утилиты ----------

        /// <summary> Точка контакта — край тела цели в сторону атакующего. </summary>
        private (Vector3 at, Vector3 dirToAttacker) Contact(int target, int attacker)
        {
            var t = Pos(target);
            var dir = Pos(attacker) - t;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            float r = _runner.Sim.Setup.Fighters[target].HurtRadius.ToFloat();
            return (t + dir * r + Vector3.up * ContactHeight, dir);
        }

        private bool Valid(int i) => i >= 0 && i < GameState.FighterCount;

        private Vector3 Pos(int i) => ToWorld(_runner.State.Fighters[i].Position);

        private static Vector3 ToWorld(FixVec2 v) => new(v.X.ToFloat(), 0f, v.Y.ToFloat());

        private Color ColorOf(int i) => _colors != null && i >= 0 && i < _colors.Length ? _colors[i] : Color.white;

        private static AnimationCurve Grow(float from, float to) => new(new Keyframe(0f, from, 0f, 3f), new Keyframe(1f, to, 0f, 0f));
        private static AnimationCurve Shrink() => new(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

        private ParticleSystem CreateSystem(string name, Material material, ParticleSystemRenderMode mode, int max,
                                            float gravity = 0f, AnimationCurve sizeCurve = null, float drag = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            if (sizeCurve != null)
            {
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
            }
            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.drag = drag;
            }

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = material;
            rend.renderMode = mode;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sortMode = ParticleSystemSortMode.None;
            if (mode == ParticleSystemRenderMode.Stretch)
            {
                rend.velocityScale = 0.035f;
                rend.lengthScale = 1.5f;
            }
            ps.Play();
            return ps;
        }
    }
}
