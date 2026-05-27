using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Снимает ограничение FPS на мобилках и пытается использовать максимальную частоту экрана.
    /// Unity по умолчанию ставит targetFrameRate = 30 на iOS/Android (для экономии батареи),
    /// поэтому на устройствах с 90/120 Гц картинка выглядит "залипающей".
    ///
    /// Установка: один объект на сцену (или вызов из любого bootstrap-компонента).
    /// </summary>
    public static class FrameRateBooster
    {
        /// <summary>
        /// Применяет настройки максимального FPS.
        /// Если передан 0 / отрицательное — пытаемся использовать частоту экрана устройства.
        /// </summary>
        public static void Apply(int targetFps = 0)
        {
            // Отключаем VSync — иначе targetFrameRate игнорируется и FPS привязан к VSync.
            QualitySettings.vSyncCount = 0;

            int fps;
            if (targetFps > 0)
            {
                fps = targetFps;
            }
            else
            {
                // Берём refresh rate экрана; на 120-герцовых телефонах будет 120.
                // Screen.currentResolution.refreshRateRatio — современный API (Unity 2022.2+).
#if UNITY_2022_2_OR_NEWER
                var rate = Screen.currentResolution.refreshRateRatio;
                fps = (int)System.Math.Round(rate.value);
#else
                fps = Screen.currentResolution.refreshRate;
#endif
                // Подстраховка: если устройство сообщает 0 (бывает в эмуляторе), ставим 60.
                if (fps <= 0) fps = 60;
            }

            Application.targetFrameRate = fps;
        }
    }
}