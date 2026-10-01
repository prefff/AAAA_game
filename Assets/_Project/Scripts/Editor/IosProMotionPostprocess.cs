#if UNITY_EDITOR && UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Game.EditorTools
{
    /// <summary>
    /// Разрешает 120 Гц на iPhone с ProMotion: без ключа CADisableMinimumFrameDurationOnPhone iOS ограничивает
    /// приложение 60 Гц, даже если Application.targetFrameRate = 120. Ключ ставится идемпотентно.
    /// </summary>
    public static class IosProMotionPostprocess
    {
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("CADisableMinimumFrameDurationOnPhone", true);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
