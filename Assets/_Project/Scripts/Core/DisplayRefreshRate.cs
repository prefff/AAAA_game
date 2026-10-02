using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Реальная частота экрана на Android и запрос самого быстрого режима (120/144 Гц).
    ///
    /// Зачем: Unity на Android по <see cref="Screen.currentResolution"/> отдаёт не тот режим (на OnePlus показывает
    /// 60 Гц при реальных 120), а сама частоту экрана у системы не просит — экран остаётся на том, что решит система.
    /// Здесь окно игры просит режим с наибольшей частотой при текущем разрешении (WindowManager.LayoutParams
    /// .preferredDisplayModeId), а текущая частота читается прямо из Display.getRefreshRate().
    /// Окончательно решает система: потолок частоты в настройках экрана, энергосбережение, перегрев (ColorOS/MIUI
    /// дополнительно ограничивают частоту по приложениям).
    /// На других платформах — значения Unity.
    /// </summary>
    public static class DisplayRefreshRate
    {
        private const float CacheSeconds = 0.5f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _display;
        private static float _cached;
        private static float _cachedAt = -1f;
        private static float _max;
        private static int _maxModeId = -1;
#endif

        /// <summary> Частота экрана сейчас, Гц (на Android — у дисплея, кэш 0,5 с). </summary>
        public static float Current
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_cachedAt >= 0f && Time.unscaledTime - _cachedAt < CacheSeconds) return _cached;
                try
                {
                    var display = Display();
                    _cached = display != null ? display.Call<float>("getRefreshRate") : 0f;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[DisplayRefreshRate] getRefreshRate: {e.Message}");
                    _cached = 0f;
                }
                _cachedAt = Time.unscaledTime;
                if (_cached > 0f) return _cached;
#endif
                return (float)Screen.currentResolution.refreshRateRatio.value;
            }
        }

        /// <summary> Наибольшая частота среди режимов экрана с текущим разрешением, Гц. </summary>
        public static float Max
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_maxModeId < 0) FindMaxMode();
                if (_max > 0f) return _max;
#endif
                double best = Screen.currentResolution.refreshRateRatio.value;
                foreach (var r in Screen.resolutions)
                    if (r.refreshRateRatio.value > best) best = r.refreshRateRatio.value;
                return (float)best;
            }
        }

        /// <summary>
        /// Просит у системы режим экрана с наибольшей частотой (повторный вызов безопасен — например, после
        /// возврата в приложение). Возвращает запрошенную частоту, 0 — не удалось или не Android.
        /// </summary>
        public static float RequestMax()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                FindMaxMode();
                if (_maxModeId < 0) return 0f;
                int modeId = _maxModeId;
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                // Атрибуты окна меняются только в UI-потоке Android.
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using var window = activity.Call<AndroidJavaObject>("getWindow");
                        using var attrs = window.Call<AndroidJavaObject>("getAttributes");
                        if (attrs.Get<int>("preferredDisplayModeId") == modeId) return;
                        attrs.Set("preferredDisplayModeId", modeId);
                        window.Call("setAttributes", attrs);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[DisplayRefreshRate] setAttributes: {e.Message}");
                    }
                }));
                _cachedAt = -1f;
                Debug.Log($"[DisplayRefreshRate] запрошен режим {modeId}: {_max:F0} Гц");
                return _max;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DisplayRefreshRate] {e.Message}");
            }
#endif
            return 0f;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject Display()
        {
            if (_display != null) return _display;
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var wm = activity.Call<AndroidJavaObject>("getWindowManager");
            _display = wm.Call<AndroidJavaObject>("getDefaultDisplay");
            return _display;
        }

        private static void FindMaxMode()
        {
            var display = Display();
            if (display == null) return;
            using var current = display.Call<AndroidJavaObject>("getMode");
            int w = current.Call<int>("getPhysicalWidth"), h = current.Call<int>("getPhysicalHeight");
            var modes = display.Call<AndroidJavaObject[]>("getSupportedModes");
            float best = 0f;
            int bestId = -1;
            foreach (var m in modes)
            {
                // Только режимы с тем же разрешением: смена разрешения экрана — пересоздание поверхности и мигание.
                if (m.Call<int>("getPhysicalWidth") == w && m.Call<int>("getPhysicalHeight") == h)
                {
                    float rate = m.Call<float>("getRefreshRate");
                    if (rate > best)
                    {
                        best = rate;
                        bestId = m.Call<int>("getModeId");
                    }
                }
                m.Dispose();
            }
            _max = best;
            _maxModeId = bestId;
        }
#endif
    }
}
