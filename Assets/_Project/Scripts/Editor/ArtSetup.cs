using System.IO;
using Game.Characters;
using Game.Combat;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Собирает арт из исходников одной командой (повторный запуск безопасен — ассеты обновляются на месте):
    ///   - настройки импорта Hero.fbx (из Tools/Blender/build_hero.py) и текстур (из Tools/Textures/gen_textures.py);
    ///   - материалы персонажа, арены и эффектов, библиотеку <see cref="ArtLibrary"/> в Resources;
    ///   - префаб бойца Prefabs/Fighter_Hero (модель + <see cref="FighterView"/> + <see cref="FighterRig"/> + тень-блоб +
    ///     диск хитбокса) и назначает его всем персонажам в Resources/Fighters.
    /// Меню: Game/Art/Rebuild Art Assets. CLI: unity command eval --code 'Game.EditorTools.ArtSetup.Rebuild(); return "ok";'
    /// </summary>
    public static class ArtSetup
    {
        private const string Art = "Assets/_Project/Art";
        private const string HeroFbx = Art + "/Characters/Hero/Hero.fbx";
        private const string Materials = Art + "/Materials";
        private const string EnvTex = Art + "/Textures/Environment";
        private const string FxTex = Art + "/Textures/Fx";
        private const string PrefabPath = "Assets/_Project/Prefabs/Fighter_Hero.prefab";
        private const string LibraryPath = "Assets/Resources/" + ArtLibrary.ResourcePath + ".asset";

        [MenuItem("Game/Art/Rebuild Art Assets")]
        public static void Rebuild()
        {
            AssetDatabase.Refresh();
            ConfigureTextures();
            ConfigureModel();
            AssetDatabase.Refresh();

            EnsureFolder(Materials);
            var character = Mat("M_Character", "Game/Character", m =>
            {
                m.SetColor("_ShadowTint", new Color(0.5f, 0.5f, 0.66f));
                m.SetFloat("_BaseRim", 0.22f);
            });
            var floor = Mat("M_Env_Floor", "Game/Environment", m =>
            {
                m.SetTexture("_MainTex", Tex(EnvTex + "/T_Floor_Stone.png"));
                m.SetFloat("_TexWorldSize", 4f);
                m.SetTexture("_Markings", Tex(EnvTex + "/T_Arena_Markings.png"));
                m.SetFloat("_UseMarkings", 1f);
                m.EnableKeyword("_MARKINGS");
                m.SetColor("_MarkingsColor", new Color(0.95f, 0.88f, 0.7f, 0.65f));
                m.SetFloat("_OutsideDark", 0.6f);
            });
            var wall = Mat("M_Env_Wall", "Game/Environment", m =>
            {
                m.SetTexture("_MainTex", Tex(EnvTex + "/T_Wall_Blocks.png"));
                m.SetFloat("_TexWorldSize", 2f);
                m.SetColor("_Color", new Color(0.8f, 0.82f, 0.9f));
                m.SetColor("_TopTint", new Color(1.25f, 1.2f, 1.1f));
                m.SetFloat("_AoStrength", 0.5f);
            });
            var obstacle = Mat("M_Env_Obstacle", "Game/Environment", m =>
            {
                m.SetTexture("_MainTex", Tex(EnvTex + "/T_Wall_Blocks.png"));
                m.SetFloat("_TexWorldSize", 1.6f);
                m.SetColor("_Color", new Color(1.1f, 1.02f, 0.92f));
                m.SetColor("_TopTint", new Color(1.3f, 1.25f, 1.15f));
                m.SetFloat("_AoStrength", 0.55f);
                m.SetFloat("_AoHeight", 0.8f);
            });
            var glow = Mat("M_Fx_Glow", "Game/FxAdditive", m => { m.SetTexture("_MainTex", Tex(FxTex + "/T_Fx_Glow.png")); m.SetFloat("_Intensity", 1.6f); });
            var ring = Mat("M_Fx_Ring", "Game/FxAdditive", m => { m.SetTexture("_MainTex", Tex(FxTex + "/T_Fx_Ring.png")); m.SetFloat("_Intensity", 1.8f); });
            var star = Mat("M_Fx_Star", "Game/FxAdditive", m => { m.SetTexture("_MainTex", Tex(FxTex + "/T_Fx_Star.png")); m.SetFloat("_Intensity", 2f); });
            var puff = Mat("M_Fx_Puff", "Game/FxAlpha", m => m.SetTexture("_MainTex", Tex(FxTex + "/T_Fx_Puff.png")));
            var shadow = Mat("M_Fx_Shadow", "Game/FxAlpha", m =>
            {
                m.SetTexture("_MainTex", Tex(FxTex + "/T_Fx_Shadow.png"));
                m.SetColor("_Color", new Color(0f, 0f, 0f, 0.55f));
            });
            var hitbox = Mat("M_Fx_Hitbox", "Game/FxAdditive", m =>
            {
                m.SetTexture("_MainTex", Tex(FxTex + "/T_Fx_Ring.png"));
                m.SetColor("_Color", new Color(1f, 0.3f, 0.15f, 0.7f));
                m.SetFloat("_Intensity", 1.4f);
            });

            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(LibraryPath);
            if (lib == null)
            {
                EnsureFolder(Path.GetDirectoryName(LibraryPath).Replace('\\', '/'));
                lib = ScriptableObject.CreateInstance<ArtLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            lib.Floor = floor;
            lib.Wall = wall;
            lib.Obstacle = obstacle;
            lib.Glow = glow;
            lib.Ring = ring;
            lib.Star = star;
            lib.Puff = puff;
            lib.Shadow = shadow;
            EditorUtility.SetDirty(lib);

            var prefab = BuildPrefab(character, shadow, hitbox);
            AssignToFighters(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ArtSetup] Готово: {PrefabPath}, {LibraryPath}");
        }

        // ---------- импорт ----------

        private static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { EnvTex, FxTex }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                bool fx = path.StartsWith(FxTex);
                bool markings = path.EndsWith("T_Arena_Markings.png");
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = true;
                ti.wrapMode = fx || markings ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.filterMode = FilterMode.Bilinear;
                ti.anisoLevel = fx ? 0 : 2;
                ti.alphaSource = fx || markings ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = fx || markings;
                ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
        }

        private static void ConfigureModel()
        {
            if (AssetImporter.GetAtPath(HeroFbx) is not ModelImporter mi)
            {
                Debug.LogError($"[ArtSetup] Нет {HeroFbx} — запусти Tools/Blender/build_hero.py");
                return;
            }
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            mi.optimizeGameObjects = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.skinWeights = ModelImporterSkinWeights.Custom;
            mi.maxBonesPerVertex = 1; // жёсткая привязка: одна кость на вершину — самый дешёвый скиннинг
            mi.SaveAndReimport();
        }

        // ---------- материалы ----------

        private static Material Mat(string name, string shaderName, System.Action<Material> setup)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[ArtSetup] Нет шейдера {shaderName}");
                return null;
            }
            string path = $"{Materials}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            setup(mat);
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D Tex(string path)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) Debug.LogError($"[ArtSetup] Нет текстуры {path} — запусти Tools/Textures/gen_textures.py");
            return t;
        }

        // ---------- префаб ----------

        private static GameObject BuildPrefab(Material character, Material shadow, Material hitbox)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(HeroFbx);
            if (model == null) return null;

            var root = new GameObject("Fighter_Hero");
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                instance.name = "Model";
                var smr = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                smr.sharedMaterial = character;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.skinnedMotionVectors = false;
                smr.updateWhenOffscreen = false;
                smr.quality = SkinQuality.Bone1;
                smr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                smr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                // Границы с запасом на перекат и удары — пересчитывать их каждый кадр не нужно.
                smr.localBounds = new Bounds(smr.localBounds.center, smr.localBounds.size * 1.6f);

                // Тень-блоб: квад на полу вместо realtime-теней.
                var shadowQuad = Quad("Shadow", root.transform, shadow);
                shadowQuad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                shadowQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                shadowQuad.transform.localScale = new Vector3(1.3f, 1.3f, 1f);

                // Диск хитбокса в active-кадрах: масштаб корня маркера 1 = диаметр 1 м (FighterView).
                var marker = new GameObject("HitboxMarker");
                marker.transform.SetParent(root.transform, false);
                var ringQuad = Quad("Ring", marker.transform, hitbox);
                ringQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                marker.SetActive(false);

                var rig = root.AddComponent<FighterRig>();
                rig.Setup(instance.transform);
                var view = root.AddComponent<FighterView>();
                view.Setup(smr, marker.transform);
                SetField(view, "_rig", rig);

                EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject Quad(string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        private static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignToFighters(GameObject prefab)
        {
            if (prefab == null) return;
            foreach (var guid in AssetDatabase.FindAssets("t:FighterDefinition", new[] { "Assets/Resources/Fighters" }))
            {
                var def = AssetDatabase.LoadAssetAtPath<FighterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.ViewPrefab == prefab) continue;
                def.ViewPrefab = prefab;
                EditorUtility.SetDirty(def);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
