using System.Collections.Generic;
using Game.Simulation;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Скилл персонажа для тюнинга в инспекторе. Тайминги — в кадрах 60 Гц, как frame data ударов; в бою используется
    /// <see cref="ToSpec"/>. Цикл каста: startup (боец бежит, отмена уклонением бесплатна) → выход скилла (мана и
    /// перезарядка списываются здесь) → recovery.
    /// Поля снаряда, области и попадания нужны не всем типам: какие читает симуляция — <see cref="UsesField"/>
    /// (инспектор показывает только их).
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Combat/Skill Data", fileName = "Skill_New")]
    public class SkillData : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Skill";
        [Tooltip("Короткая подпись на кнопке.")]
        public string ShortName = "S";
        [Tooltip("Иконка на кнопке. Пусто — на кнопке ShortName.")]
        public Sprite Icon;
        public SkillKind Kind = SkillKind.Projectile;

        [Header("Frame Data (at 60 FPS)")]
        [Tooltip("Кадры от отпускания кнопки до выхода скилла: противник видит каст и успевает ответить.")]
        [Min(0)] public int StartupFrames = 12;
        [Min(0)] public int RecoveryFrames = 16;
        [Tooltip("Неуязвимость от начала каста, кадров (телепорт).")]
        [Min(0)] public int InvulnerableFrames;
        [Tooltip("Доля скорости бега во время каста: 1 — каст не тормозит, 0 — боец встаёт на месте.")]
        [Range(0f, 1f)] public float MoveSpeedFactor = 1f;

        [Header("Cost")]
        [Min(0f)] public float ManaCost = 20f;
        [Min(0f)] public float CooldownSeconds = 3f;
        [Tooltip("Перезарядка в начале раунда, сек (ультимейт открывается не сразу).")]
        [Min(0f)] public float InitialCooldownSeconds;

        [Header("Targeting")]
        [Tooltip("Дальность, м: полёт снаряда, дистанция телепорта, вынос области.")]
        [Min(0.1f)] public float Range = 9f;
        [Tooltip("Быстрый каст (тап без прицела) наводится на противника в пределах дальности.")]
        public bool AutoAim = true;

        [Header("Projectile")]
        [Min(0.1f)] public float ProjectileSpeed = 16f;
        [Min(0.05f)] public float ProjectileRadius = 0.35f;
        [Tooltip("Парирование отражает снаряд обратно.")]
        public bool Reflectable = true;

        [Header("Zone")]
        [Min(0.1f)] public float ZoneRadius = 2.5f;
        [Tooltip("Кадров от появления области до взрыва — время увидеть и уйти.")]
        [Min(1)] public int ZoneDelayFrames = 30;

        [Header("Hit")]
        [Min(0f)] public float Damage = 12f;
        [Tooltip("Скорость отбрасывания, м/с.")]
        [Min(0f)] public float Knockback = 4f;
        [Min(0)] public int HitstunFrames = 20;
        [Min(0)] public int BlockstunFrames = 12;
        [Min(0)] public int HitstopFrames = 4;
        [Tooltip("Сколько стамины выбивает из блока.")]
        [Min(0f)] public float GuardDamage = 10f;

        /// <summary>
        /// Читает ли симуляция поле для этого типа скилла. Новый SkillKind — добавить сюда его поля (и ветку в
        /// FightSimulation.FireSkill), тогда инспектор покажет их, а лишние спрячет.
        /// </summary>
        public static bool UsesField(SkillKind kind, string field) => field switch
        {
            nameof(ProjectileSpeed) or nameof(ProjectileRadius) or nameof(Reflectable) => kind == SkillKind.Projectile,
            nameof(ZoneRadius) or nameof(ZoneDelayFrames) => kind == SkillKind.Zone,
            nameof(Damage) or nameof(Knockback) or nameof(HitstunFrames) or nameof(BlockstunFrames) or nameof(HitstopFrames)
                or nameof(GuardDamage) => kind != SkillKind.Blink,
            _ => true,
        };

        /// <summary> Ошибки данных скилла (пусто — всё в порядке). </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (!System.Enum.IsDefined(typeof(SkillKind), Kind)) problems.Add($"Неизвестный тип {(int)Kind}.");
            if (string.IsNullOrWhiteSpace(ShortName) && Icon == null) problems.Add("Нет ни иконки, ни ShortName — кнопка пустая.");
            if (Kind != SkillKind.Blink && Damage <= 0f) problems.Add("Урон 0: атакующий скилл ничего не делает.");
            return problems;
        }

        public SkillSpec ToSpec() => new()
        {
            Name = DisplayName,
            Kind = Kind,
            StartupTicks = SimTime.Frames(StartupFrames),
            RecoveryTicks = SimTime.Frames(RecoveryFrames),
            InvulnerableTicks = SimTime.Frames(InvulnerableFrames),
            MoveSpeedFactor = Fix.FromFloat(MoveSpeedFactor),
            ManaCost = Fix.FromFloat(ManaCost),
            CooldownTicks = SimTime.Seconds(CooldownSeconds),
            InitialCooldownTicks = SimTime.Seconds(InitialCooldownSeconds),
            Range = Fix.FromFloat(Range),
            AutoAim = AutoAim,
            ProjectileSpeed = SimTime.PerSecond(ProjectileSpeed),
            ProjectileRadius = Fix.FromFloat(ProjectileRadius),
            Reflectable = Reflectable,
            ZoneRadius = Fix.FromFloat(ZoneRadius),
            ZoneDelayTicks = SimTime.Frames(ZoneDelayFrames),
            Damage = Fix.FromFloat(Damage),
            KnockbackSpeed = SimTime.PerSecond(Knockback),
            HitstunTicks = SimTime.Frames(HitstunFrames),
            BlockstunTicks = SimTime.Frames(BlockstunFrames),
            HitstopTicks = SimTime.Frames(HitstopFrames),
            GuardDamage = Fix.FromFloat(GuardDamage),
        };
    }
}
