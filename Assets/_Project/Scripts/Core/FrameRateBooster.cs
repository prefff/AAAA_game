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
        public static void Apply(int targetFps = 0, bool disableVSync = false)
        {
            // VSync отключаем только если явно попросили. На мобилках и в редакторе
            // лучше оставить системный VSync, а targetFrameRate использовать как "потолок".
            if (disableVSync)
            {
                QualitySettings.vSyncCount = 0;
            }

            int fps;
            if (targetFps > 0)
            {
                fps = targetFps;
            }
            else
            {
                fps = MaxRefreshRate();
                // Подстраховка: если устройство сообщает 0 (бывает в эмуляторе), ставим 60.
                if (fps <= 0) fps = 60;
            }

            Application.targetFrameRate = fps;
        }

        /// <summary>
        /// Максимальная частота экрана. Текущий режим может быть ниже максимума: Android держит 60 Гц, пока
        /// приложение не попросит больше, — а просим мы через targetFrameRate, поэтому берём лучший из режимов.
        /// </summary>
        public static int MaxRefreshRate()
        {
            double best = Screen.currentResolution.refreshRateRatio.value;
            foreach (var r in Screen.resolutions)
                if (r.refreshRateRatio.value > best) best = r.refreshRateRatio.value;
            return (int)System.Math.Round(best);
        }
    }
}