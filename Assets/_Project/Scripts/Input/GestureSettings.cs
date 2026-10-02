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

        [Header("Блок и парирование (удержание)")]
        [Tooltip("Удержание без сдвига дольше порога, сек → блок; первые кадры блока — парирование. Меньше startup " +
                 "лёгкого удара (7 кадров ≈ 0,12 с), чтобы блок отменял удар до активной фазы. Больше — меньше ложных " +
                 "блоков на медленных тапах, но позже парирование.")]
        [Min(0.02f)] public float HoldThreshold = 0.09f;

        [Header("Скиллы (кнопки у правого нижнего угла)")]
        [Tooltip("Центры кнопок Skill1, Skill2, Ultimate: отступ от правого нижнего угла в пикселях канваса 1920 по ширине. " +
                 "От краёв экрана — не меньше полного сдвига прицела (SkillAimRadiusMm), иначе к краю не прицелиться.")]
        public Vector2[] SkillButtonOffsets = { new(430f, 190f), new(320f, 330f), new(210f, 480f) };
        [Tooltip("Радиус кнопки, пикселей канваса 1920. Касание кнопки — прицел скилла, а не удар.")]
        [Min(10f)] public float SkillButtonRadius = 78f;
        [Tooltip("Зона отмены: отпустить палец здесь — скилла не будет. Отступ от правого нижнего угла.")]
        public Vector2 SkillCancelOffset = new(150f, 715f);
        [Min(10f)] public float SkillCancelRadius = 85f;
        [Tooltip("Сдвиг пальца от центра кнопки, мм, дающий полную дальность скилла.")]
        [Min(1f)] public float SkillAimRadiusMm = 12f;
        [Tooltip("Сдвиг меньше этого, мм — быстрый каст с автоприцелом.")]
        [Min(0f)] public float SkillAimDeadZoneMm = 2.5f;

        /// <summary> Загрузить ассет из Resources или, если его нет, взять значения по умолчанию. </summary>
        public static GestureSettings LoadOrDefault()
        {
            var asset = Resources.Load<GestureSettings>(ResourcePath);
            return asset != null ? asset : CreateInstance<GestureSettings>();
        }
    }
}
