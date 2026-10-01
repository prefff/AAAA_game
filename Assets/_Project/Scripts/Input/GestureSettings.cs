using UnityEngine;

namespace Game.Input
{
    /// <summary>
    /// Пороги распознавания жестов — тюнинг без перекомпиляции (позже — в настройках игрока).
    /// Расстояния в миллиметрах экрана: пиксели на разных телефонах слишком разные по физическому размеру.
    /// Ассет по умолчанию: Resources/Input/GestureSettings.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Input/Gesture Settings", fileName = "GestureSettings")]
    public class GestureSettings : ScriptableObject
    {
        public const string ResourcePath = "Input/GestureSettings";

        [Header("Зона жестов")]
        [Tooltip("Доля ширины экрана, левее которой касания не считаются жестами (там джойстик).")]
        [Range(0f, 1f)] public float GestureZoneMinX = 0.5f;

        [Header("Уклонение (свайп)")]
        [Tooltip("Сдвиг пальца, мм, после которого начатый удар сразу отменяется в уклонение в сторону сдвига.")]
        [Min(0.5f)] public float DodgeThresholdMm = 6f;

        [Header("Парирование (flick)")]
        [Tooltip("Максимальная длительность flick от касания до отпускания, сек. Короче — парирование, длиннее — уклонение.")]
        [Min(0.01f)] public float ParryFlickMaxDuration = 0.12f;
        [Tooltip("Минимальная средняя скорость flick, мм/с.")]
        [Min(0f)] public float ParryFlickMinSpeedMmPerSec = 100f;

        [Header("Блок (удержание)")]
        [Tooltip("Удержание без сдвига дольше порога, сек → блок. Меньше startup лёгкого удара (6 кадров = 0,1 с), " +
                 "чтобы блок отменял удар до активной фазы. Больше — меньше ложных блоков на медленных тапах.")]
        [Min(0.02f)] public float HoldThreshold = 0.09f;

        /// <summary> Загрузить ассет из Resources или, если его нет, взять значения по умолчанию. </summary>
        public static GestureSettings LoadOrDefault()
        {
            var asset = Resources.Load<GestureSettings>(ResourcePath);
            return asset != null ? asset : CreateInstance<GestureSettings>();
        }
    }
}
