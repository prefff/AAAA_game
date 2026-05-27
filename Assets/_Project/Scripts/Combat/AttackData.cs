using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Frame data атаки. Описывается в инспекторе как ScriptableObject и переиспользуется анимациями/состояниями.
    /// Тайминги — в кадрах при 60 FPS (1 кадр = 1/60 сек). Это даёт ту самую "файтинговую" предсказуемость.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Combat/Attack Data", fileName = "Attack_New")]
    public class AttackData : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Light Attack";

        [Header("Frame Data (at 60 FPS)")]
        [Tooltip("Кадры до начала активной фазы (windup).")]
        [Min(0)] public int StartupFrames = 6;
        [Tooltip("Кадры, в которые хитбокс активен и наносит урон.")]
        [Min(1)] public int ActiveFrames = 3;
        [Tooltip("Кадры восстановления (можно прервать только специальным input cancel).")]
        [Min(0)] public int RecoveryFrames = 12;

        [Header("Damage & Knockback")]
        public float Damage = 10f;
        [Tooltip("Сила импульса при попадании (по направлению атакующий ? цель).")]
        public float Knockback = 3f;
        [Tooltip("Кадров hitstun у цели после получения урона.")]
        [Min(0)] public int HitstunFrames = 12;
        [Tooltip("Кадров блок-стана у цели, если она в блоке.")]
        [Min(0)] public int BlockstunFrames = 8;

        [Header("Cost")]
        [Tooltip("Сколько стамины тратится на эту атаку.")]
        [Min(0f)] public float StaminaCost = 0f;

        [Header("Cancel rules")]
        [Tooltip("Можно ли отменить recovery следующей атакой (для комбо).")]
        public bool CanCancelOnHit = true;

        // --- Утилиты ---
        public float StartupSeconds => StartupFrames / 60f;
        public float ActiveSeconds => ActiveFrames / 60f;
        public float RecoverySeconds => RecoveryFrames / 60f;
        public float TotalSeconds => (StartupFrames + ActiveFrames + RecoveryFrames) / 60f;
    }
}