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
    ///   1) Через меню: Tools ? Build ? Android APK
    ///   2) Через CLI: -executeMethod Game.EditorTools.BuildScript.BuildAndroid
    ///
    /// Перед сборкой автоматически выставляет необходимые Player Settings:
    ///   - landscape ориентация
    ///   - IL2CPP + ARM64
    ///   - package name
    ///   - корректное Active Input Handling
    /// </summary>
    public static class BuildScript
    {
        private const string PackageName = "com.aaaa.game";
        private const string ProductName = "AAAA Game";
        private const string CompanyName = "AAAA";
        private const string OutputDir = "Builds/Android";
        private const string ApkName = "AAAA_game.apk";
        private const string ScenePath = "Assets/_Project/Scenes/Arena.unity";

        [MenuItem("Tools/Build/Android APK")]
        public static void BuildAndroidMenu() => BuildAndroid();

        public static void BuildAndroid()
        {
            ConfigurePlayerSettings();

            var outDir = Path.Combine(Directory.GetCurrentDirectory(), OutputDir);
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, ApkName);

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
                options = BuildOptions.None
            };

            Debug.Log($"[BuildScript] Начинаю сборку APK ? {outPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] ? Сборка успешна: {outPath} ({summary.totalSize / (1024 * 1024)} МБ, длительность {summary.totalTime})");
            }
            else
            {
                Debug.LogError($"[BuildScript] ? Сборка не удалась: {summary.result}, ошибок: {summary.totalErrors}");
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