using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Материалы арены и эффектов боя (Resources/Art/ArtLibrary). Виды и сборщик арены берут их отсюда, а не через
    /// Shader.Find: так шейдеры гарантированно попадают в сборку. Ассет собирает меню «Game/Art/Rebuild Art Assets».
    /// Нет ассета — всё работает на примитивах, как раньше.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Art Library", fileName = "ArtLibrary")]
    public sealed class ArtLibrary : ScriptableObject
    {
        public const string ResourcePath = "Art/ArtLibrary";

        [Header("Арена")]
        public Material Floor;
        public Material Wall;
        public Material Obstacle;

        [Header("Эффекты (Game/FxAdditive, Game/FxAlpha)")]
        [Tooltip("Мягкое свечение: искры, снаряды, следы ударов.")]
        public Material Glow;
        [Tooltip("Кольцо: ударные волны, блок, области.")]
        public Material Ring;
        [Tooltip("Звезда-вспышка: попадание, парирование.")]
        public Material Star;
        [Tooltip("Клуб пыли (прозрачный, не аддитивный).")]
        public Material Puff;
        [Tooltip("Мягкая тень под бойцом и заливка областей (прозрачный).")]
        public Material Shadow;

        private static ArtLibrary _cached;
        private static bool _loaded;

        /// <summary> Ассет из Resources или null. </summary>
        public static ArtLibrary Instance
        {
            get
            {
                if (_loaded) return _cached;
                _loaded = true;
                _cached = Resources.Load<ArtLibrary>(ResourcePath);
                return _cached;
            }
        }
    }
}
