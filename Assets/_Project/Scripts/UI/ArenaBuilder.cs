using Game.Characters;
using Game.Combat;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// ѕроцедурно строит арену при старте сцены:
    ///   - Floor (плоскость)
    ///   - Player (Capsule + Fighter + Health + Stamina + Hurtbox + Hitbox + Rigidbody)
    ///   - Dummy (Capsule + Health + Hurtbox + DummyTarget)
    ///   - MainCamera (с SimpleFollowCamera)
    ///   - Directional Light
    ///
    /// јтаки (AttackData) загружаютс€ из Resources/Attacks/Attack_Light и Attack_Heavy.
    /// </summary>
    public static class ArenaBuilder
    {
        // --- ѕараметры (можно вытащить в SerializeField bootstrap'a при необходимости) ---
        private const float FloorSize = 30f;
        private static readonly Vector3 PlayerStart = new Vector3(-3f, 1f, 0f);
        private static readonly Vector3 DummyStart  = new Vector3( 3f, 1f, 0f);

        /// <summary>
        /// ≈сли в сцене уже есть Player/Dummy/Floor Ч пропускаем их создание.
        /// </summary>
        public static void Build(bool addOnlyMissing = true)
        {
            EnsureLight();
            EnsureFloor(addOnlyMissing);

            var player = EnsurePlayer(addOnlyMissing);
            EnsureDummy(addOnlyMissing);

            EnsureMainCamera(player);
        }

        // ---------- Light ----------

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

        // ---------- Floor ----------

        private static void EnsureFloor(bool skipIfExists)
        {
            if (skipIfExists && GameObject.Find("Floor") != null) return;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(FloorSize / 10f, 1f, FloorSize / 10f); // Plane = 10x10 m по умолчанию

            // ѕроста€ раскраска через PropertyBlock, чтобы не плодить материалы
            var rend = floor.GetComponent<Renderer>();
            if (rend != null)
            {
                var block = new MaterialPropertyBlock();
                rend.GetPropertyBlock(block);
                if (rend.sharedMaterial != null)
                {
                    if (rend.sharedMaterial.HasProperty("_BaseColor"))
                        block.SetColor("_BaseColor", new Color(0.25f, 0.27f, 0.30f, 1f));
                    else if (rend.sharedMaterial.HasProperty("_Color"))
                        block.SetColor("_Color", new Color(0.25f, 0.27f, 0.30f, 1f));
                }
                rend.SetPropertyBlock(block);
            }
        }

        // ---------- Player ----------

        private static Fighter EnsurePlayer(bool skipIfExists)
        {
            // ≈сли в сцене уже есть локальный игрок Ч используем его
            if (skipIfExists)
            {
                foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
                    if (f.IsLocalPlayer) return f;
            }

            // Root
            var root = new GameObject("Player");
            root.transform.position = PlayerStart;

            // Mesh (Capsule visual)
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            mesh.name = "Mesh";
            mesh.transform.SetParent(root.transform, false);
            // ”дал€ем коллайдер у визуала Ч мы поставим свой
            var meshCol = mesh.GetComponent<Collider>();
            if (meshCol != null) Object.Destroy(meshCol);
            TintRenderer(mesh, new Color(0.2f, 0.6f, 1f, 1f));

            // Physics
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 70f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // Body collider (дл€ столкновений со стенами / землЄй)
            var bodyCol = root.AddComponent<CapsuleCollider>();
            bodyCol.height = 2f;
            bodyCol.radius = 0.45f;
            bodyCol.center = new Vector3(0f, 0f, 0f);

            // Health & Stamina
            var health = root.AddComponent<Health>();
            var stamina = root.AddComponent<Stamina>();

            // Hurtbox (триггер)
            var hurtGo = new GameObject("Hurtbox");
            hurtGo.transform.SetParent(root.transform, false);
            var hurtCol = hurtGo.AddComponent<CapsuleCollider>();
            hurtCol.isTrigger = true;
            hurtCol.height = 2f;
            hurtCol.radius = 0.5f;
            var hurtbox = hurtGo.AddComponent<Hurtbox>();

            // Hitbox (триггер впереди, выключенный)
            var hitGo = new GameObject("Hitbox");
            hitGo.transform.SetParent(root.transform, false);
            hitGo.transform.localPosition = new Vector3(0f, 0f, 1.0f); // впереди
            var hitCol = hitGo.AddComponent<BoxCollider>();
            hitCol.isTrigger = true;
            hitCol.size = new Vector3(1.2f, 1.6f, 1.2f);
            hitCol.enabled = false; // выключаем Ч Hitbox.Activate() включит
            var hitbox = hitGo.AddComponent<Hitbox>();

            // Fighter Ч настраиваем через SerializedField через reflection
            var fighter = root.AddComponent<Fighter>();
            SetPrivateField(fighter, "_health", health);
            SetPrivateField(fighter, "_stamina", stamina);
            SetPrivateField(fighter, "_hurtbox", hurtbox);
            SetPrivateField(fighter, "_hitbox", hitbox);
            SetPrivateField(fighter, "_rigidbody", rb);

            // јтаки из Resources
            var lightAttack = Resources.Load<AttackData>("Attacks/Attack_Light");
            var heavyAttack = Resources.Load<AttackData>("Attacks/Attack_Heavy");
            if (lightAttack != null) SetPrivateField(fighter, "_lightAttack", lightAttack);
            if (heavyAttack != null) SetPrivateField(fighter, "_heavyAttack", heavyAttack);

            // Owner дл€ hitbox/hurtbox
            SetPrivateField(hitbox, "_owner", root);
            SetPrivateField(hurtbox, "_owner", root);
            SetPrivateField(hurtbox, "_health", health);

            fighter.IsLocalPlayer = true;

            return fighter;
        }

        // ---------- Dummy ----------

        private static void EnsureDummy(bool skipIfExists)
        {
            if (skipIfExists && GameObject.Find("Dummy") != null) return;

            var root = new GameObject("Dummy");
            root.transform.position = DummyStart;

            var mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            mesh.name = "Mesh";
            mesh.transform.SetParent(root.transform, false);
            var meshCol = mesh.GetComponent<Collider>();
            if (meshCol != null) Object.Destroy(meshCol);
            TintRenderer(mesh, new Color(0.8f, 0.4f, 0.2f, 1f));

            // Body collider (не trigger Ч чтобы об него можно было уперетьс€)
            var bodyCol = root.AddComponent<CapsuleCollider>();
            bodyCol.height = 2f;
            bodyCol.radius = 0.45f;

            // Health + DummyTarget
            var health = root.AddComponent<Health>();
            var dt = root.AddComponent<DummyTarget>();
            // _renderer в DummyTarget Ч найдЄтс€ через GetComponentInChildren в Awake().

            // Hurtbox (триггер)
            var hurtGo = new GameObject("Hurtbox");
            hurtGo.transform.SetParent(root.transform, false);
            var hurtCol = hurtGo.AddComponent<CapsuleCollider>();
            hurtCol.isTrigger = true;
            hurtCol.height = 2f;
            hurtCol.radius = 0.5f;
            var hurtbox = hurtGo.AddComponent<Hurtbox>();
            SetPrivateField(hurtbox, "_owner", root);
            SetPrivateField(hurtbox, "_health", health);
        }

        // ---------- Camera ----------

        private static void EnsureMainCamera(Fighter player)
        {
            var cam = Camera.main;
            GameObject camGo;
            if (cam == null)
            {
                camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camGo.tag = "MainCamera";
                cam = camGo.GetComponent<Camera>();
            }
            else
            {
                camGo = cam.gameObject;
            }

            // Ѕазовые настройки камеры
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.12f, 0.16f, 1f);
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            // Follow-компонент
            var follow = camGo.GetComponent<SimpleFollowCamera>();
            if (follow == null) follow = camGo.AddComponent<SimpleFollowCamera>();
            if (player != null) follow.SetTarget(player.transform);
        }

        // ---------- Helpers ----------

        private static void TintRenderer(GameObject go, Color color)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;
            var block = new MaterialPropertyBlock();
            rend.GetPropertyBlock(block);
            if (rend.sharedMaterial != null)
            {
                if (rend.sharedMaterial.HasProperty("_BaseColor"))
                    block.SetColor("_BaseColor", color);
                else if (rend.sharedMaterial.HasProperty("_Color"))
                    block.SetColor("_Color", color);
            }
            rend.SetPropertyBlock(block);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            if (target == null) return;
            var t = target.GetType();
            while (t != null)
            {
                var f = t.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) { f.SetValue(target, value); return; }
                t = t.BaseType;
            }
        }
    }
}