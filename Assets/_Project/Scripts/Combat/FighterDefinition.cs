using Game.Simulation;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Параметры персонажа для тюнинга в инспекторе. В бою используется <see cref="ToSpec"/> — fixed-point данные
    /// в тиках симуляции. Длительности — в кадрах 60 Гц, как и frame data ударов.
    /// Ассет по умолчанию: Resources/Fighters/Fighter_Default.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Combat/Fighter Definition", fileName = "Fighter_New")]
    public class FighterDefinition : ScriptableObject
    {
        public const string DefaultResourcePath = "Fighters/Fighter_Default";

        [Header("Resources")]
        [Min(1f)] public float MaxHealth = 100f;
        [Min(0f)] public float MaxStamina = 100f;
        [Min(0f)] public float StaminaRegenPerSecond = 25f;
        [Tooltip("Задержка перед регенерацией стамины после траты, сек.")]
        [Min(0f)] public float StaminaRegenDelay = 0.6f;
        [Tooltip("Мана — для скиллов (этап 6).")]
        [Min(0f)] public float MaxMana = 100f;
        [Min(0f)] public float ManaRegenPerSecond = 4f;

        [Header("Body")]
        [Min(0f)] public float MoveSpeed = 5f;
        [Tooltip("Радиус тела: стены и другой боец не дают пройти.")]
        [Min(0.05f)] public float BodyRadius = 0.45f;
        [Tooltip("Радиус уязвимой зоны для хитбоксов.")]
        [Min(0.05f)] public float HurtRadius = 0.5f;

        [Header("Parry")]
        [Min(1)] public int ParryWindowFrames = 11;
        [Tooltip("Парирование мимо: столько кадров боец уязвим.")]
        [Min(0)] public int ParryWhiffRecoveryFrames = 8;

        [Header("Dodge")]
        [Min(0f)] public float DodgeSpeed = 8f;
        [Min(1)] public int DodgeFrames = 15;
        [Min(0)] public int DodgeIFrames = 8;
        [Min(0f)] public float DodgeStaminaCost = 20f;
        [Tooltip("Столько кадров от начала уклонения flick-парирование ещё отменяет его (с возвратом стамины).")]
        [Min(0)] public int DodgeToParryCancelFrames = 9;

        [Header("Block")]
        [Range(0f, 1f)] public float BlockDamageMultiplier = 0.3f;
        [Range(0f, 1f)] public float BlockKnockbackMultiplier = 0.3f;

        [Header("Input")]
        [Tooltip("Буфер ввода, кадров: команда, пришедшая в recovery/оглушение, выполнится в первый разрешённый тик.")]
        [Min(0)] public int InputBufferFrames = 7;

        [Header("Attacks")]
        public AttackData LightAttack;
        public AttackData HeavyAttack;

        public FighterSpec ToSpec()
        {
            var spec = new FighterSpec
            {
                MaxHealth = Fix.FromFloat(MaxHealth),
                MaxStamina = Fix.FromFloat(MaxStamina),
                StaminaRegen = SimTime.PerSecond(StaminaRegenPerSecond),
                StaminaRegenDelayTicks = SimTime.Seconds(StaminaRegenDelay),
                MaxMana = Fix.FromFloat(MaxMana),
                ManaRegen = SimTime.PerSecond(ManaRegenPerSecond),
                MoveSpeed = SimTime.PerSecond(MoveSpeed),
                BodyRadius = Fix.FromFloat(BodyRadius),
                HurtRadius = Fix.FromFloat(HurtRadius),
                ParryWindowTicks = SimTime.Frames(ParryWindowFrames),
                ParryWhiffRecoveryTicks = SimTime.Frames(ParryWhiffRecoveryFrames),
                DodgeSpeed = SimTime.PerSecond(DodgeSpeed),
                DodgeTicks = SimTime.Frames(DodgeFrames),
                DodgeIFrameTicks = SimTime.Frames(DodgeIFrames),
                DodgeStaminaCost = Fix.FromFloat(DodgeStaminaCost),
                DodgeToParryCancelTicks = SimTime.Frames(DodgeToParryCancelFrames),
                BlockDamageMultiplier = Fix.FromFloat(BlockDamageMultiplier),
                BlockKnockbackMultiplier = Fix.FromFloat(BlockKnockbackMultiplier),
                InputBufferTicks = SimTime.Frames(InputBufferFrames),
            };
            if (LightAttack != null) spec.Light = LightAttack.ToSpec(AttackKind.Light);
            if (HeavyAttack != null) spec.Heavy = HeavyAttack.ToSpec(AttackKind.Heavy);
            return spec;
        }

        /// <summary> Ассет по умолчанию или, если его нет, значения по умолчанию с ударами из Resources/Attacks. </summary>
        public static FighterDefinition LoadOrDefault()
        {
            var asset = Resources.Load<FighterDefinition>(DefaultResourcePath);
            if (asset != null) return asset;
            var def = CreateInstance<FighterDefinition>();
            def.LightAttack = Resources.Load<AttackData>("Attacks/Attack_Light");
            def.HeavyAttack = Resources.Load<AttackData>("Attacks/Attack_Heavy");
            return def;
        }
    }
}
