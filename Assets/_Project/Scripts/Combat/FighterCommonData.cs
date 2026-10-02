using Game.Simulation;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Механики, одинаковые для всех персонажей: парирование, уклонение, блок, буфер ввода, мана за урон. Персонажи
    /// различаются ударами, скиллами и характеристиками (<see cref="FighterDefinition"/>), а окно парирования или i-кадры
    /// уклонения — это правила игры: игрок переносит навык между персонажами, а баланс меняется в одном месте.
    /// Ассет: Resources/Combat/FighterCommon. Длительности — в кадрах 60 Гц.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Combat/Fighter Common Rules", fileName = "FighterCommon")]
    public class FighterCommonData : ScriptableObject
    {
        public const string ResourcePath = "Combat/FighterCommon";

        [Header("Stamina & Mana")]
        [Tooltip("Задержка перед регенерацией стамины после траты, сек.")]
        [Min(0f)] public float StaminaRegenDelay = 0.6f;
        [Tooltip("Мана в начале раунда.")]
        [Min(0f)] public float StartMana = 50f;
        [Tooltip("Мана за единицу нанесённого урона (и урона в блок).")]
        [Min(0f)] public float ManaPerDamageDealt = 0.5f;
        [Tooltip("Мана за единицу полученного урона.")]
        [Min(0f)] public float ManaPerDamageTaken = 0.35f;

        [Header("Parry (блок вовремя)")]
        [Tooltip("Первые кадры блока — парирование.")]
        [Min(1)] public int ParryWindowFrames = 11;
        [Tooltip("Парирование мимо, а блок уже отпущен: столько кадров боец уязвим.")]
        [Min(0)] public int ParryWhiffRecoveryFrames = 8;
        [Tooltip("Новое парирование — не раньше, чем через столько кадров после начала прошлого (удачное — сразу). " +
                 "Пока не перезарядилось, нажатие блока даёт обычный блок: дёрганье блока не спамит парирование.")]
        [Min(0)] public int ParryRearmFrames = 30;

        [Header("Dodge")]
        [Min(0f)] public float DodgeSpeed = 8f;
        [Min(1)] public int DodgeFrames = 15;
        [Min(0)] public int DodgeIFrames = 8;
        [Min(0f)] public float DodgeStaminaCost = 20f;

        [Header("Block")]
        [Range(0f, 1f)] public float BlockDamageMultiplier = 0.3f;
        [Range(0f, 1f)] public float BlockKnockbackMultiplier = 0.3f;

        [Header("Input")]
        [Tooltip("Буфер ввода, кадров: команда, пришедшая в recovery/оглушение, выполнится в первый разрешённый тик.")]
        [Min(0)] public int InputBufferFrames = 7;

        /// <summary> Записать общие механики в спеку бойца. </summary>
        public void ApplyTo(FighterSpec spec)
        {
            spec.StaminaRegenDelayTicks = SimTime.Seconds(StaminaRegenDelay);
            spec.StartMana = Fix.FromFloat(StartMana);
            spec.ManaPerDamageDealt = Fix.FromFloat(ManaPerDamageDealt);
            spec.ManaPerDamageTaken = Fix.FromFloat(ManaPerDamageTaken);
            spec.ParryWindowTicks = SimTime.Frames(ParryWindowFrames);
            spec.ParryWhiffRecoveryTicks = SimTime.Frames(ParryWhiffRecoveryFrames);
            spec.ParryRearmTicks = SimTime.Frames(ParryRearmFrames);
            spec.DodgeSpeed = SimTime.PerSecond(DodgeSpeed);
            spec.DodgeTicks = SimTime.Frames(DodgeFrames);
            spec.DodgeIFrameTicks = SimTime.Frames(DodgeIFrames);
            spec.DodgeStaminaCost = Fix.FromFloat(DodgeStaminaCost);
            spec.BlockDamageMultiplier = Fix.FromFloat(BlockDamageMultiplier);
            spec.BlockKnockbackMultiplier = Fix.FromFloat(BlockKnockbackMultiplier);
            spec.InputBufferTicks = SimTime.Frames(InputBufferFrames);
        }

        /// <summary> Ассет из Resources или, если его нет, значения по умолчанию. </summary>
        public static FighterCommonData LoadOrDefault()
        {
            var asset = Resources.Load<FighterCommonData>(ResourcePath);
            return asset != null ? asset : CreateInstance<FighterCommonData>();
        }
    }
}
