using System.Collections.Generic;
using Game.Simulation;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Вид снарядов и областей скиллов: только читает GameState.Objects. Слот пула = индекс объекта в состоянии,
    /// поэтому вид не может разойтись с симуляцией (и переживает откат). Область — кольцо радиуса взрыва и внутреннее
    /// кольцо-таймер, которое растёт до взрыва. Вспышки взрыва и гашения снаряда — по событиям, чисто визуальные.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class SkillObjectsView : MonoBehaviour
    {
        private const float ProjectileHeight = 1f;
        private const float FlashSeconds = 0.25f;

        private MatchRunner _runner;
        private Color[] _colors;
        private Transform[] _projectiles;
        private Renderer[] _projectileRenderers;
        private LineRenderer[] _zoneOuter;
        private LineRenderer[] _zoneTimer;

        private struct Flash
        {
            public LineRenderer Line;
            public Vector3 Center;
            public float Radius;
            public float Start;
            public Color Color;
        }

        private readonly List<Flash> _flashes = new();
        private readonly Stack<LineRenderer> _freeFlashLines = new();

        public int VisibleProjectiles { get; private set; }
        public int VisibleZones { get; private set; }

        /// <summary> colors — по индексу бойца (владельца снаряда/области). </summary>
        public void Bind(MatchRunner runner, Color[] colors)
        {
            Unsubscribe();
            _runner = runner;
            _colors = colors;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake()
        {
            int n = GameState.MaxSkillObjects;
            _projectiles = new Transform[n];
            _projectileRenderers = new Renderer[n];
            _zoneOuter = new LineRenderer[n];
            _zoneTimer = new LineRenderer[n];
            for (int k = 0; k < n; k++)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Projectile_{k}";
                sphere.transform.SetParent(transform, false);
                Destroy(sphere.GetComponent<Collider>()); // физики в геймплее нет
                sphere.SetActive(false);
                _projectiles[k] = sphere.transform;
                _projectileRenderers[k] = sphere.GetComponent<Renderer>();
                _zoneOuter[k] = WorldLines.Create($"Zone_{k}", transform, 0.1f, loop: true);
                _zoneTimer[k] = WorldLines.Create($"ZoneTimer_{k}", transform, 0.06f, loop: true);
            }
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

        private void OnSimEvent(SimEvent e)
        {
            if (e.Actor < 0 || e.Slot < 0 || e.Caster < 0) return;
            var spec = _runner.Sim.Setup.Fighters[e.Caster].Skill(e.Slot);
            if (spec == null) return;
            switch (e.Type)
            {
                case SimEventType.ZoneDetonated:
                    AddFlash(e.Position, spec.ZoneRadius.ToFloat(), ColorOf(e.Actor));
                    break;
                case SimEventType.ProjectileExpired:
                case SimEventType.ProjectileReflected:
                    AddFlash(e.Position, spec.ProjectileRadius.ToFloat() * 2.5f, ColorOf(e.Actor));
                    break;
            }
        }

        private void LateUpdate()
        {
            if (_runner == null || _runner.State == null) return;
            var objects = _runner.State.Objects;
            var fighters = _runner.Sim.Setup.Fighters;
            int projectiles = 0, zones = 0;

            for (int k = 0; k < objects.Length; k++)
            {
                var o = objects[k];
                var sk = o.IsActive ? fighters[o.Caster].Skill((int)o.Slot) : null;
                var pos = new Vector3(o.Position.X.ToFloat(), 0f, o.Position.Y.ToFloat());
                var color = sk != null ? ColorOf(o.Owner) : Color.white;

                bool projectile = sk != null && o.Kind == SkillObjectKind.Projectile;
                SetActive(_projectiles[k].gameObject, projectile);
                if (projectile)
                {
                    projectiles++;
                    float d = sk.ProjectileRadius.ToFloat() * 2f;
                    _projectiles[k].position = pos + Vector3.up * ProjectileHeight;
                    _projectiles[k].localScale = new Vector3(d, d, d);
                    Tint(_projectileRenderers[k], Color.Lerp(color, Color.white, 0.4f));
                }

                if (sk != null && o.Kind == SkillObjectKind.Zone)
                {
                    zones++;
                    float r = sk.ZoneRadius.ToFloat();
                    float progress = 1f - o.TicksLeft / (float)Mathf.Max(1, sk.ZoneDelayTicks);
                    WorldLines.Ring(_zoneOuter[k], pos, r, color);
                    WorldLines.Ring(_zoneTimer[k], pos, Mathf.Max(0.05f, r * progress), new Color(color.r, color.g, color.b, 0.6f));
                }
                else
                {
                    WorldLines.Hide(_zoneOuter[k]);
                    WorldLines.Hide(_zoneTimer[k]);
                }
            }
            VisibleProjectiles = projectiles;
            VisibleZones = zones;
            UpdateFlashes();
        }

        private void AddFlash(FixVec2 at, float radius, Color color)
        {
            var line = _freeFlashLines.Count > 0 ? _freeFlashLines.Pop() : WorldLines.Create("Flash", transform, 0.15f, loop: true);
            _flashes.Add(new Flash
            {
                Line = line,
                Center = new Vector3(at.X.ToFloat(), 0f, at.Y.ToFloat()),
                Radius = radius,
                Start = Time.time,
                Color = color,
            });
        }

        private void UpdateFlashes()
        {
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var f = _flashes[i];
                float t = (Time.time - f.Start) / FlashSeconds;
                if (t >= 1f)
                {
                    WorldLines.Hide(f.Line);
                    _freeFlashLines.Push(f.Line);
                    _flashes.RemoveAt(i);
                    continue;
                }
                var c = Color.Lerp(Color.white, f.Color, t);
                c.a = 1f - t;
                WorldLines.Ring(f.Line, f.Center, f.Radius * (0.6f + 0.6f * t), c);
            }
        }

        private Color ColorOf(int owner) => _colors != null && owner >= 0 && owner < _colors.Length ? _colors[owner] : Color.white;

        private static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active) go.SetActive(active);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _props;

        private void Tint(Renderer rend, Color color)
        {
            var mat = rend.sharedMaterial;
            if (mat == null) return;
            _props ??= new MaterialPropertyBlock();
            rend.GetPropertyBlock(_props);
            if (mat.HasProperty(BaseColorId)) _props.SetColor(BaseColorId, color);
            else if (mat.HasProperty(ColorId)) _props.SetColor(ColorId, color);
            rend.SetPropertyBlock(_props);
        }
    }
}
