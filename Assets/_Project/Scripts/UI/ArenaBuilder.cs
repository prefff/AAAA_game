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
    ///   - виды двух бойцов (из префабов или процедурно: капсула + диск хитбокса);
    ///   - камеру MobaCamera на своём бойце и свет.
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

            public FighterView Local => Views[Runner.LocalPlayer];
            public FighterView Opponent => Views[Runner.Opponent];
        }

        public static Result Build(FighterDefinition player = null, FighterDefinition opponent = null, int localPlayer = 0,
                                   BotMode botMode = BotMode.Idle, bool training = false,
                                   FighterView playerViewPrefab = null, FighterView opponentViewPrefab = null)
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
                var prefab = local ? playerViewPrefab : opponentViewPrefab;
                result.Views[i] = CreateView(runner, i, prefab, local ? PlayerColor : OpponentColor, local ? "Player" : "Opponent");
            }

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
            view.Bind(runner, index);
            return view;
        }

        // ---------- Арена ----------

        private static void EnsureLight()
        {
            var existing = Object.FindFirstObjectByType<Light>();
            if (existing != null && existing.type == LightType.Directional) return;

            var go = new GameObject("Directional Light");
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
        }

        private static void EnsureFloor(ArenaSpec arena)
        {
            if (GameObject.Find("Floor") != null) return;
            var size = ToWorld(arena.Max) - ToWorld(arena.Min);
            var center = (ToWorld(arena.Max) + ToWorld(arena.Min)) * 0.5f;
            var floor = CreatePrimitive(PrimitiveType.Plane, "Floor", null, FloorColor);
            floor.transform.position = center;
            // Plane — 10×10 м; с запасом за стенами, чтобы край не был виден камерой.
            floor.transform.localScale = new Vector3(size.x / 10f + 1f, 1f, size.z / 10f + 1f);
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

            CreateBlock(root.transform, "Wall_N", new Vector3(center.x, 0f, max.z + t * 0.5f), new Vector3(size.x + 2f * t, WallHeight, t), WallColor);
            CreateBlock(root.transform, "Wall_S", new Vector3(center.x, 0f, min.z - t * 0.5f), new Vector3(size.x + 2f * t, WallHeight, t), WallColor);
            CreateBlock(root.transform, "Wall_E", new Vector3(max.x + t * 0.5f, 0f, center.z), new Vector3(t, WallHeight, size.z), WallColor);
            CreateBlock(root.transform, "Wall_W", new Vector3(min.x - t * 0.5f, 0f, center.z), new Vector3(t, WallHeight, size.z), WallColor);

            for (int i = 0; i < arena.Obstacles.Length; i++)
            {
                var o = arena.Obstacles[i];
                var pos = ToWorld(o.Center);
                if (o.Shape == ObstacleShape.Circle)
                {
                    float d = o.Radius.ToFloat() * 2f;
                    var pillar = CreatePrimitive(PrimitiveType.Cylinder, $"Pillar_{i}", root.transform, ObstacleColor);
                    pillar.transform.position = pos + Vector3.up * (ObstacleHeight * 0.5f);
                    pillar.transform.localScale = new Vector3(d, ObstacleHeight * 0.5f, d); // цилиндр — 2 м в высоту
                }
                else
                {
                    var half = ToWorld(o.HalfExtents);
                    CreateBlock(root.transform, $"Block_{i}", pos, new Vector3(half.x * 2f, ObstacleHeight, half.z * 2f), ObstacleColor);
                }
            }
            return root;
        }

        private static void CreateBlock(Transform parent, string name, Vector3 groundCenter, Vector3 size, Color color)
        {
            var go = CreatePrimitive(PrimitiveType.Cube, name, parent, color);
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
            cam.backgroundColor = new Color(0.1f, 0.12f, 0.16f, 1f);
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

        private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Color color)
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
            Tint(go.GetComponent<Renderer>(), color);
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
