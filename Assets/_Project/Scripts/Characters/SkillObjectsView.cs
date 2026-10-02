using System.Collections.Generic;
using Game.Simulation;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Вид снарядов и областей скиллов: только читает GameState.Objects. Слот пула = индекс объекта в состоянии,
    /// поэтому вид не может разойтись с симуляцией (и переживает откат). Область — кольцо радиуса взрыва и внутреннее
    /// кольцо-таймер, которое растёт до взрыва. Вспышки взрыва и гашения снаряда — по событиям, чисто визуальные.
    /// С библиотекой арта (<see cref="ArtLibrary"/>): снаряд — светящееся ядро-билборд со следом, область — ещё и
    /// заливка, которая густеет к взрыву. Без неё — сферы и линии, как раньше.
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
        private Renderer[] _projectileCores;
        private TrailRenderer[] _projectileTrails;
        private Renderer[] _zoneFills;
        private bool _fancy;
        private Camera _camera;

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
            _projectileCores = new Renderer[n];
            _projectileTrails = new TrailRenderer[n];
            _zoneFills = new Renderer[n];
            var lib = ArtLibrary.Instance;
            _fancy = lib != null && lib.Glow != null && lib.Star != null && lib.Shadow != null;
            for (int k = 0; k < n; k++)
            {
                if (_fancy) CreateFancyProjectile(k, lib);
                else
                {
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphere.name = $"Projectile_{k}";
                    sphere.transform.SetParent(transform, false);
                    Destroy(sphere.GetComponent<Collider>()); // физики в геймплее нет
                    sphere.SetActive(false);
                    _projectiles[k] = sphere.transform;
                    _projectileRenderers[k] = sphere.GetComponent<Renderer>();
                }
                _zoneOuter[k] = WorldLines.Create($"Zone_{k}", transform, 0.1f, loop: true);
                _zoneTimer[k] = WorldLines.Create($"ZoneTimer_{k}", transform, 0.06f, loop: true);
                if (_fancy)
                {
                    var fill = Quad($"ZoneFill_{k}", transform, lib.Shadow);
                    fill.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    fill.gameObject.SetActive(false);
                    _zoneFills[k] = fill;
                }
            }
        }

        private void CreateFancyProjectile(int k, ArtLibrary lib)
        {
            var root = new GameObject($"Projectile_{k}").transform;
            root.SetParent(transform, false);
            var glow = Quad("Glow", root, lib.Glow);
            glow.transform.localScale = Vector3.one * 2.6f;
            var core = Quad("Core", root, lib.Star);
            core.transform.localScale = Vector3.one * 1.5f;
            core.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            var trail = root.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = lib.Glow;
            trail.time = 0.18f;
            trail.minVertexDistance = 0.08f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.numCapVertices = 0;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            root.gameObject.SetActive(false);
            _projectiles[k] = root;
            _projectileRenderers[k] = glow;
            _projectileCores[k] = core;
            _projectileTrails[k] = trail;
        }

        private static Renderer Quad(string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
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
                bool appeared = projectile && !_projectiles[k].gameObject.activeSelf;
                if (projectile)
                {
                    projectiles++;
                    float d = sk.ProjectileRadius.ToFloat() * 2f;
                    _projectiles[k].position = pos + Vector3.up * ProjectileHeight;
                    _projectiles[k].localScale = new Vector3(d, d, d);
                    Tint(_projectileRenderers[k], Color.Lerp(color, Color.white, 0.4f));
                    if (_fancy)
                    {
                        if (_camera == null) _camera = Camera.main;
                        if (_camera != null) _projectiles[k].rotation = _camera.transform.rotation;
                        _projectileCores[k].transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 720f);
                        Tint(_projectileCores[k], Color.Lerp(color, Color.white, 0.75f));
                        _projectileTrails[k].widthMultiplier = d * 0.9f;
                        _projectileTrails[k].startColor = Color.Lerp(color, Color.white, 0.3f);
                        _projectileTrails[k].endColor = new Color(color.r, color.g, color.b, 0f);
                    }
                }
                SetActive(_projectiles[k].gameObject, projectile);
                if (appeared && _fancy) _projectileTrails[k].Clear(); // след из слота пула не тянется с прошлого снаряда

                if (sk != null && o.Kind == SkillObjectKind.Zone)
                {
                    zones++;
                    float r = sk.ZoneRadius.ToFloat();
                    float progress = 1f - o.TicksLeft / (float)Mathf.Max(1, sk.ZoneDelayTicks);
                    WorldLines.Ring(_zoneOuter[k], pos, r, color);
                    WorldLines.Ring(_zoneTimer[k], pos, Mathf.Max(0.05f, r * progress), new Color(color.r, color.g, color.b, 0.6f));
                    if (_fancy)
                    {
                        var fill = _zoneFills[k];
                        fill.transform.position = pos + Vector3.up * 0.025f;
                        fill.transform.localScale = new Vector3(r * 2.3f, r * 2.3f, 1f);
                        Tint(fill, new Color(color.r, color.g, color.b, 0.12f + 0.4f * progress * progress));
                        SetActive(fill.gameObject, true);
                    }
                }
                else
                {
                    WorldLines.Hide(_zoneOuter[k]);
                    WorldLines.Hide(_zoneTimer[k]);
                    if (_fancy) SetActive(_zoneFills[k].gameObject, false);
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
