using Game.Characters;
using Game.Combat;
using Game.Simulation;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Собирает арену и бойцов из данных симуляции:
    ///   - MatchRunner (симуляция боя) с параметрами бойцов;
    ///   - пол, стены по краям и препятствия — по ArenaSpec (геометрия в мире совпадает с коллизиями симуляции);
    ///   - виды двух бойцов (из префабов или процедурно: капсула + диск хитбокса), снарядов/областей и прицела скилла;
    ///   - эффекты боя (<see cref="CombatFx"/>), камеру MobaCamera на своём бойце и свет.
    /// Материалы арены и эффектов — из <see cref="ArtLibrary"/> (нет ассета — серые примитивы). Освещение мобильное:
    /// один направленный свет без realtime-теней (под бойцами — тень-блоб из префаба), окружение — три цвета.
    /// Физики Unity здесь нет: коллайдеры примитивов удаляются, всё движение и столкновения — в симуляции.
    /// Уже существующие объекты (свет, пол, камера) переиспользуются, поэтому повторный вызов безопасен.
    /// </summary>
    public static class ArenaBuilder
    {
        public static readonly Color PlayerColor = new(0.2f, 0.6f, 1f, 1f);
        public static readonly Color OpponentColor = new(0.9f, 0.4f, 0.2f, 1f);
        private static readonly Color FloorColor = new(0.25f, 0.27f, 0.30f, 1f);
        private static readonly Color WallColor = new(0.18f, 0.19f, 0.22f, 1f);
        private static readonly Color ObstacleColor = new(0.42f, 0.45f, 0.52f, 1f);
        private static readonly Color HitboxColor = new(1f, 0.2f, 0.1f, 1f);

        private const float WallHeight = 1.2f;
        private const float WallThickness = 0.5f;
        private const float ObstacleHeight = 1.6f;
        private const string ArenaRootName = "Arena";

        public sealed class Result
        {
            public MatchRunner Runner;
            public readonly FighterView[] Views = new FighterView[GameState.FighterCount];
            public MobaCamera Camera;
            public SkillObjectsView SkillObjects;
            public SkillAimIndicator AimIndicator;
            public CombatFx Fx;

            public FighterView Local => Views[Runner.LocalPlayer];
            public FighterView Opponent => Views[Runner.Opponent];
        }

        /// <summary> Вид бойца — из его <see cref="FighterDefinition.ViewPrefab"/> (пусто — капсула). </summary>
        public static Result Build(FighterDefinition player = null, FighterDefinition opponent = null, int localPlayer = 0,
                                   BotMode botMode = BotMode.Idle, bool training = false)
        {
            var result = new Result();
            EnsureLight();

            result.Runner = Object.FindAnyObjectByType<MatchRunner>();
            if (result.Runner == null) result.Runner = CreateRunner(player, opponent, localPlayer, botMode, training);
            var runner = result.Runner;

            var arena = runner.Sim.Setup.Arena;
            EnsureFloor(arena);
            BuildArenaGeometry(arena);

            for (int i = 0; i < GameState.FighterCount; i++)
            {
                bool local = i == runner.LocalPlayer;
                var prefab = ViewPrefab(runner.Definition(i));
                result.Views[i] = CreateView(runner, i, prefab, local ? PlayerColor : OpponentColor, local ? "Player" : "Opponent");
            }

            var colors = new Color[GameState.FighterCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = i == runner.LocalPlayer ? PlayerColor : OpponentColor;
            result.SkillObjects = new GameObject("_SkillObjects").AddComponent<SkillObjectsView>();
            result.SkillObjects.Bind(runner, colors);
            result.AimIndicator = new GameObject("_SkillAim").AddComponent<SkillAimIndicator>();
            result.AimIndicator.Bind(runner);
            result.Fx = new GameObject("_CombatFx").AddComponent<CombatFx>();
            result.Fx.Bind(runner, colors);

            result.Camera = EnsureMainCamera(result.Local.transform, runner.LocalPlayer);
            return result;
        }

        public static MatchRunner CreateRunner(FighterDefinition player, FighterDefinition opponent, int localPlayer, BotMode botMode, bool training)
        {
            var go = new GameObject("_Match");
            go.SetActive(false); // Configure до Awake
            var runner = go.AddComponent<MatchRunner>();
            runner.Configure(player, opponent, localPlayer, botMode, training);
            go.SetActive(true);
            return runner;
        }

        // ---------- Бойцы ----------

        private static FighterView ViewPrefab(FighterDefinition definition)
        {
            if (definition == null || definition.ViewPrefab == null) return null;
            var view = definition.ViewPrefab.GetComponent<FighterView>();
            if (view == null) Debug.LogWarning($"{definition.name}: у ViewPrefab нет FighterView — будет капсула", definition);
            return view;
        }

        public static FighterView CreateView(MatchRunner runner, int index, FighterView prefab, Color color, string name)
        {
            FighterView view;
            if (prefab != null)
            {
                view = Object.Instantiate(prefab);
            }
            else
            {
                var root = new GameObject();
                root.SetActive(false); // Awake вида должен увидеть уже покрашенный меш
                var mesh = CreatePrimitive(PrimitiveType.Capsule, "Mesh", root.transform, color);
                mesh.transform.localPosition = new Vector3(0f, 1f, 0f);
                var marker = CreatePrimitive(PrimitiveType.Cylinder, "HitboxMarker", root.transform, HitboxColor);
                marker.SetActive(false);
                view = root.AddComponent<FighterView>();
                view.Setup(mesh.GetComponent<Renderer>(), marker.transform);
                root.SetActive(true);
            }
            view.name = name;
            view.SetTeam(color);
            view.Bind(runner, index);
            return view;
        }

        // ---------- Арена ----------

        private static void EnsureLight()
        {
            var light = Object.FindFirstObjectByType<Light>();
            if (light == null || light.type != LightType.Directional)
            {
                var go = new GameObject("Directional Light");
                go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1f, 0.96f, 0.9f);
                light.intensity = 1.1f;
                light.shadows = LightShadows.Soft;
            }
            if (ArtLibrary.Instance == null) return;

            // Тёплый «закатный» ключевой свет и холодное окружение: бойцы читаются на тёплом полу.
            // Realtime-тени выключены — на мобилках это самый дорогой проход; под бойцом тень-блоб.
            light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            light.color = new Color(1f, 0.93f, 0.82f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.None;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.48f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.34f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.18f, 0.17f);
            RenderSettings.fog = false;
        }

        private static void EnsureFloor(ArenaSpec arena)
        {
            var lib = ArtLibrary.Instance;
            var size = ToWorld(arena.Max) - ToWorld(arena.Min);
            var center = (ToWorld(arena.Max) + ToWorld(arena.Min)) * 0.5f;
            var floor = GameObject.Find("Floor");
            if (floor == null)
            {
                floor = CreatePrimitive(PrimitiveType.Plane, "Floor", null, FloorColor, lib != null ? lib.Floor : null);
                // Plane — 10×10 м; с запасом за стенами, чтобы край не был виден камерой.
                floor.transform.localScale = new Vector3(size.x / 10f + 1f, 1f, size.z / 10f + 1f);
            }
            // Земля симуляции — y = 0: на ней стоят бойцы, стены и тени (пол из сцены стоял на −0,5).
            floor.transform.position = center;
            var rend = floor.GetComponent<Renderer>();
            if (lib != null && lib.Floor != null && rend != null)
            {
                // Пол из сцены тоже получает материал арены. Разметка ложится ровно в её прямоугольник.
                rend.sharedMaterial = lib.Floor;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                var block = new MaterialPropertyBlock();
                rend.GetPropertyBlock(block);
                var min = ToWorld(arena.Min);
                var max = ToWorld(arena.Max);
                block.SetVector("_ArenaRect", new Vector4(min.x, min.z, max.x, max.z));
                rend.SetPropertyBlock(block);
            }
        }

        /// <summary> Стены по краям и препятствия — один в один с коллизиями симуляции. </summary>
        public static GameObject BuildArenaGeometry(ArenaSpec arena)
        {
            var existing = GameObject.Find(ArenaRootName);
            if (existing != null) return existing;

            var root = new GameObject(ArenaRootName);
            var min = ToWorld(arena.Min);
            var max = ToWorld(arena.Max);
            var size = max - min;
            var center = (min + max) * 0.5f;
            float t = WallThickness;
            var lib = ArtLibrary.Instance;
            var wallMat = lib != null ? lib.Wall : null;
            var obstacleMat = lib != null ? lib.Obstacle : null;

            CreateBlock(root.transform, "Wall_N", new Vector3(center.x, 0f, max.z + t * 0.5f), new Vector3(size.x + 2f * t, WallHeight, t), WallColor, wallMat);
            CreateBlock(root.transform, "Wall_S", new Vector3(center.x, 0f, min.z - t * 0.5f), new Vector3(size.x + 2f * t, WallHeight, t), WallColor, wallMat);
            CreateBlock(root.transform, "Wall_E", new Vector3(max.x + t * 0.5f, 0f, center.z), new Vector3(t, WallHeight, size.z), WallColor, wallMat);
            CreateBlock(root.transform, "Wall_W", new Vector3(min.x - t * 0.5f, 0f, center.z), new Vector3(t, WallHeight, size.z), WallColor, wallMat);

            for (int i = 0; i < arena.Obstacles.Length; i++)
            {
                var o = arena.Obstacles[i];
                var pos = ToWorld(o.Center);
                if (o.Shape == ObstacleShape.Circle)
                {
                    float d = o.Radius.ToFloat() * 2f;
                    var pillar = CreatePrimitive(PrimitiveType.Cylinder, $"Pillar_{i}", root.transform, ObstacleColor, obstacleMat);
                    pillar.transform.position = pos + Vector3.up * (ObstacleHeight * 0.5f);
                    pillar.transform.localScale = new Vector3(d, ObstacleHeight * 0.5f, d); // цилиндр — 2 м в высоту
                }
                else
                {
                    var half = ToWorld(o.HalfExtents);
                    CreateBlock(root.transform, $"Block_{i}", pos, new Vector3(half.x * 2f, ObstacleHeight, half.z * 2f), ObstacleColor, obstacleMat);
                }
            }
            return root;
        }

        private static void CreateBlock(Transform parent, string name, Vector3 groundCenter, Vector3 size, Color color, Material material = null)
        {
            var go = CreatePrimitive(PrimitiveType.Cube, name, parent, color, material);
            go.transform.position = groundCenter + Vector3.up * (size.y * 0.5f);
            go.transform.localScale = size;
        }

        // ---------- Камера ----------

        private static MobaCamera EnsureMainCamera(Transform target, int localPlayer)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
                cam = go.GetComponent<Camera>();
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ArtLibrary.Instance != null ? new Color(0.06f, 0.065f, 0.08f, 1f) : new Color(0.1f, 0.12f, 0.16f, 1f);
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            if (!cam.TryGetComponent<MobaCamera>(out var moba)) moba = cam.gameObject.AddComponent<MobaCamera>();
            moba.SetSide(localPlayer);
            moba.SetTarget(target);
            return moba;
        }

        // ---------- Helpers ----------

        public static Vector3 ToWorld(FixVec2 v) => new(v.X.ToFloat(), 0f, v.Y.ToFloat());

        /// <summary> Примитив без коллайдера: с материалом из библиотеки или стандартный, покрашенный в color. </summary>
        private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Color color, Material material = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            // Коллайдеры PhysX в геймплее не нужны: столкновения считает симуляция.
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
            var rend = go.GetComponent<Renderer>();
            if (material != null)
            {
                rend.sharedMaterial = material;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }
            else Tint(rend, color);
            return go;
        }

        /// <summary> Красим через PropertyBlock, чтобы не плодить инстансы материала. </summary>
        public static void Tint(Renderer rend, Color color)
        {
            if (rend == null || rend.sharedMaterial == null) return;
            var block = new MaterialPropertyBlock();
            rend.GetPropertyBlock(block);
            if (rend.sharedMaterial.HasProperty("_BaseColor")) block.SetColor("_BaseColor", color);
            else if (rend.sharedMaterial.HasProperty("_Color")) block.SetColor("_Color", color);
            rend.SetPropertyBlock(block);
        }
    }
}
