using Game.Simulation;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Frame data атаки. Тайминги — в кадрах 60 Гц (1 кадр = 1/60 с) независимо от частоты тика симуляции:
    /// это даёт «файтинговую» предсказуемость. В бою используется <see cref="ToSpec"/> — данные в единицах симуляции.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Combat/Attack Data", fileName = "Attack_New")]
    public class AttackData : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Light Attack";

        [Header("Frame Data (at 60 FPS)")]
        [Tooltip("Кадры до начала активной фазы (windup). Это же окно распознавания жеста: удар отменяется бесплатно.")]
        [Min(0)] public int StartupFrames = 6;
        [Tooltip("Кадры, в которые хитбокс активен и наносит урон.")]
        [Min(1)] public int ActiveFrames = 3;
        [Tooltip("Кадры восстановления (отменяются в парирование/уклонение, после попадания — в удар).")]
        [Min(0)] public int RecoveryFrames = 12;

        [Header("Damage & Knockback")]
        public float Damage = 10f;
        [Tooltip("Скорость отбрасывания цели при попадании, м/с (по направлению атакующий → цель).")]
        public float Knockback = 3f;
        [Tooltip("Кадров hitstun у цели после попадания.")]
        [Min(0)] public int HitstunFrames = 12;
        [Tooltip("Кадров блок-стана у цели, если она в блоке.")]
        [Min(0)] public int BlockstunFrames = 8;
        [Tooltip("Стоп-кадр обоим бойцам при контакте, кадров.")]
        [Min(0)] public int HitstopFrames = 3;

        [Header("Hitbox")]
        [Tooltip("Центр круга-хитбокса перед бойцом, м.")]
        [Min(0f)] public float HitboxOffset = 1f;
        [Tooltip("Радиус хитбокса, м.")]
        [Min(0.05f)] public float HitboxRadius = 0.6f;

        [Header("Cost")]
        [Tooltip("Стамина за удар; списывается при выходе хитбокса, поэтому отмена в startup бесплатна.")]
        [Min(0f)] public float StaminaCost = 0f;

        [Header("Cancel rules")]
        [Tooltip("После попадания или блока active/recovery отменяются следующим ударом (комбо).")]
        public bool CanCancelOnHit = true;

        public int TotalFrames => StartupFrames + ActiveFrames + RecoveryFrames;

        // Секунды — только для старого прототипа на Rigidbody (Characters/States.cs), он ждёт удаления.
        public float StartupSeconds => StartupFrames / 60f;
        public float ActiveSeconds => ActiveFrames / 60f;
        public float RecoverySeconds => RecoveryFrames / 60f;

        public AttackSpec ToSpec(AttackKind kind) => new()
        {
            Kind = kind,
            StartupTicks = SimTime.Frames(StartupFrames),
            ActiveTicks = SimTime.Frames(ActiveFrames),
            RecoveryTicks = SimTime.Frames(RecoveryFrames),
            Damage = Fix.FromFloat(Damage),
            KnockbackSpeed = SimTime.PerSecond(Knockback),
            HitstunTicks = SimTime.Frames(HitstunFrames),
            BlockstunTicks = SimTime.Frames(BlockstunFrames),
            HitstopTicks = SimTime.Frames(HitstopFrames),
            StaminaCost = Fix.FromFloat(StaminaCost),
            CancelOnHit = CanCancelOnHit,
            HitOffset = Fix.FromFloat(HitboxOffset),
            HitRadius = Fix.FromFloat(HitboxRadius),
        };
    }
}
