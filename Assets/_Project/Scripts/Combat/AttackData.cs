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
        [Tooltip("Сколько стамины удар выбивает из блока; стамина кончилась — блок пробит.")]
        [Min(0f)] public float GuardDamage = 6f;

        [Header("Movement")]
        [Tooltip("Доля скорости бега во время удара: 1 — удар не тормозит, 0 — боец встаёт на месте.")]
        [Range(0f, 1f)] public float MoveSpeedFactor = 1f;

        [Header("Cancel rules")]
        [Tooltip("После попадания или блока active/recovery отменяются следующим ударом (комбо).")]
        public bool CanCancelOnHit = true;

        public int TotalFrames => StartupFrames + ActiveFrames + RecoveryFrames;

        // Секунды — только для старого прототипа на Rigidbody (Characters/States.cs), он ждёт удаления.
        public float StartupSeconds => StartupFrames / 60f;
        public float ActiveSeconds => ActiveFrames / 60f;
        public float RecoverySeconds => RecoveryFrames / 60f;

        /// <summary>
        /// Спека удара. attackSpeed — скорость атаки персонажа: весь цикл удара (startup, active, recovery) делится на
        /// неё, а hitstun и блок-стан цели — нет. Поэтому скорость атаки двигает преимущество по кадрам: медленный
        /// боец после удара освобождается позже.
        /// </summary>
        public AttackSpec ToSpec(AttackKind kind, float attackSpeed = 1f) => new()
        {
            Kind = kind,
            StartupTicks = ScaledTicks(StartupFrames, attackSpeed, 0),
            ActiveTicks = ScaledTicks(ActiveFrames, attackSpeed, 1),
            RecoveryTicks = ScaledTicks(RecoveryFrames, attackSpeed, 0),
            Damage = Fix.FromFloat(Damage),
            KnockbackSpeed = SimTime.PerSecond(Knockback),
            HitstunTicks = SimTime.Frames(HitstunFrames),
            BlockstunTicks = SimTime.Frames(BlockstunFrames),
            HitstopTicks = SimTime.Frames(HitstopFrames),
            StaminaCost = Fix.FromFloat(StaminaCost),
            GuardDamage = Fix.FromFloat(GuardDamage),
            CancelOnHit = CanCancelOnHit,
            HitOffset = Fix.FromFloat(HitboxOffset),
            HitRadius = Fix.FromFloat(HitboxRadius),
            MoveSpeedFactor = Fix.FromFloat(MoveSpeedFactor),
        };

        private static int ScaledTicks(int frames, float attackSpeed, int min)
        {
            if (attackSpeed <= 0f) attackSpeed = 1f;
            int scaled = (int)System.Math.Round(frames / (double)attackSpeed, System.MidpointRounding.AwayFromZero);
            return SimTime.Frames(System.Math.Max(min, scaled));
        }
    }
}
