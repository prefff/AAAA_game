#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Скрипт сборки Android APK.
    /// Вызывается двумя способами:
    ///   1) Через меню: Tools → Build → Android APK
    ///   2) Через CLI: -executeMethod Game.EditorTools.BuildScript.BuildAndroid
    ///
    /// Перед сборкой автоматически выставляет необходимые Player Settings:
    ///   - landscape ориентация
    ///   - IL2CPP + ARM64
    ///   - package name
    ///   - корректное Active Input Handling
    ///
    /// Варианты для замера задержки (этап 1 плана) — сравнить на одном устройстве оверлеем LAT:
    ///   - Optimized Frame Pacing (Swappy) вкл/выкл;
    ///   - число буферов свопчейна Vulkan: 2 (на кадр меньше задержки) или 3 (ровнее при просадках).
    /// CLI: -framePacing on|off  -swapchainBuffers 2|3  -development
    /// Имя APK содержит вариант, например AAAA_game_fp-off_sc2.apk.
    /// </summary>
    public static class BuildScript
    {
        private const string PackageName = "com.aaaa.game";
        private const string ProductName = "AAAA Game";
        private const string CompanyName = "AAAA";
        private const string OutputDir = "Builds/Android";
        private const string ApkBaseName = "AAAA_game";
        private const string ScenePath = "Assets/_Project/Scenes/Arena.unity";

        /// <summary> Настройки сборки, влияющие на задержку вывода кадра. </summary>
        private struct LatencyVariant
        {
            public bool FramePacing;
            public int SwapchainBuffers;
            public bool Development;

            public string Suffix => $"_fp-{(FramePacing ? "on" : "off")}_sc{SwapchainBuffers}{(Development ? "_dev" : "")}";
        }

        private static readonly LatencyVariant DefaultVariant = new() { FramePacing = true, SwapchainBuffers = 3 };

        [MenuItem("Tools/Build/Android APK")]
        public static void BuildAndroidMenu() => BuildAndroid(DefaultVariant);

        [MenuItem("Tools/Build/Android APK (замер задержки: без Frame Pacing, 2 буфера)")]
        public static void BuildAndroidLowLatencyMenu() =>
            BuildAndroid(new LatencyVariant { FramePacing = false, SwapchainBuffers = 2 });

        /// <summary> Точка входа CLI: -executeMethod Game.EditorTools.BuildScript.BuildAndroid [-framePacing off] [-swapchainBuffers 2] [-development] </summary>
        public static void BuildAndroid() => BuildAndroid(ParseVariant(Environment.GetCommandLineArgs()));

        private static LatencyVariant ParseVariant(string[] args)
        {
            var v = DefaultVariant;
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-framePacing":
                        v.FramePacing = next != "off" && next != "false" && next != "0";
                        break;
                    case "-swapchainBuffers":
                        if (int.TryParse(next, out int n) && (n == 2 || n == 3)) v.SwapchainBuffers = n;
                        else Debug.LogWarning($"[BuildScript] -swapchainBuffers: ожидается 2 или 3, получено '{next}'");
                        break;
                    case "-development":
                        v.Development = true;
                        break;
                }
            }
            return v;
        }

        private static void BuildAndroid(LatencyVariant variant)
        {
            ConfigurePlayerSettings();
            PlayerSettings.Android.optimizedFramePacing = variant.FramePacing;
            PlayerSettings.vulkanNumSwapchainBuffers = (uint)variant.SwapchainBuffers;
            Debug.Log($"[BuildScript] Вариант задержки: Frame Pacing {(variant.FramePacing ? "вкл" : "выкл")}, " +
                      $"буферов свопчейна {variant.SwapchainBuffers}, development {variant.Development}");

            var outDir = Path.Combine(Directory.GetCurrentDirectory(), OutputDir);
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, ApkBaseName + variant.Suffix + ".apk");

            // Добавляем сцену, если её нет в Build Settings
            var scenes = EditorBuildSettings.scenes;
            bool sceneAlreadyAdded = false;
            foreach (var s in scenes)
            {
                if (s.path == ScenePath) { sceneAlreadyAdded = true; break; }
            }
            if (!sceneAlreadyAdded)
            {
                var newScenes = new EditorBuildSettingsScene[scenes.Length + 1];
                Array.Copy(scenes, newScenes, scenes.Length);
                newScenes[scenes.Length] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = newScenes;
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = variant.Development ? BuildOptions.Development : BuildOptions.None
            };

            Debug.Log($"[BuildScript] Начинаю сборку APK → {outPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] ✓ Сборка успешна: {outPath} ({summary.totalSize / (1024 * 1024)} МБ, длительность {summary.totalTime})");
            }
            else
            {
                Debug.LogError($"[BuildScript] ✗ Сборка не удалась: {summary.result}, ошибок: {summary.totalErrors}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static void ConfigurePlayerSettings()
        {
            // Identity
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            // Orientation: landscape only
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.useAnimatedAutorotation = false;

            // Android-specific
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24; // Android 7.0
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);

            // Render outside safe area (для устройств с выемкой/dynamic island)
            PlayerSettings.Android.renderOutsideSafeArea = true;

            // Color space (URP лучше работает в Linear)
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Active Input Handling — у нас используется новый Input System
            // (поле задаётся через serializedObject; в новых Unity ставится автоматически по пакету)

            AssetDatabase.SaveAssets();
            Debug.Log("[BuildScript] Player Settings сконфигурированы для Android.");
        }
    }
}
#endif