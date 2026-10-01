using System.Collections.Generic;
using Game.Simulation;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Персонаж: то, чем он отличается от других, — характеристики, удары, скиллы, вид. Общие механики (парирование,
    /// уклонение, блок, буфер ввода) — в <see cref="FighterCommonData"/>. В бою используется <see cref="ToSpec"/> —
    /// fixed-point данные в тиках симуляции. Новый персонаж — ассет здесь + ассеты скиллов + строка в <see cref="FighterRoster"/>.
    /// Ассет по умолчанию: Resources/Fighters/Fighter_Default.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Combat/Fighter Definition", fileName = "Fighter_New")]
    public class FighterDefinition : ScriptableObject
    {
        public const string DefaultResourcePath = "Fighters/Fighter_Default";

        [Header("Identity")]
        [Tooltip("Постоянный идентификатор (латиница, без пробелов): по нему персонаж выбирается в сети и сохранениях. После релиза не менять.")]
        public string Id = "";
        [Tooltip("Имя персонажа в интерфейсе.")]
        public string DisplayName = "Боец";
        [Tooltip("Префаб вида с компонентом FighterView. Пусто — капсула.")]
        public GameObject ViewPrefab;

        [Header("Resources")]
        [Min(1f)] public float MaxHealth = 100f;
        [Min(0f)] public float MaxStamina = 100f;
        [Min(0f)] public float StaminaRegenPerSecond = 25f;
        [Tooltip("Мана — только для скиллов.")]
        [Min(0f)] public float MaxMana = 100f;
        [Min(0f)] public float ManaRegenPerSecond = 5f;

        [Header("Body")]
        [Min(0f)] public float MoveSpeed = 5f;
        [Tooltip("Радиус тела: стены и другой боец не дают пройти.")]
        [Min(0.05f)] public float BodyRadius = 0.45f;
        [Tooltip("Радиус уязвимой зоны для хитбоксов.")]
        [Min(0.05f)] public float HurtRadius = 0.5f;

        [Header("Attacks")]
        public AttackData LightAttack;
        public AttackData HeavyAttack;

        [Header("Skills")]
        public SkillData Skill1;
        public SkillData Skill2;
        public SkillData Ultimate;

        public SkillData Skill(int slot) => slot switch { 0 => Skill1, 1 => Skill2, 2 => Ultimate, _ => null };

        /// <summary> Спека для симуляции; общие механики — из common (пусто — Resources/Combat/FighterCommon). </summary>
        public FighterSpec ToSpec(FighterCommonData common = null)
        {
            var spec = new FighterSpec
            {
                MaxHealth = Fix.FromFloat(MaxHealth),
                MaxStamina = Fix.FromFloat(MaxStamina),
                StaminaRegen = SimTime.PerSecond(StaminaRegenPerSecond),
                MaxMana = Fix.FromFloat(MaxMana),
                ManaRegen = SimTime.PerSecond(ManaRegenPerSecond),
                MoveSpeed = SimTime.PerSecond(MoveSpeed),
                BodyRadius = Fix.FromFloat(BodyRadius),
                HurtRadius = Fix.FromFloat(HurtRadius),
            };
            (common != null ? common : FighterCommonData.LoadOrDefault()).ApplyTo(spec);
            if (LightAttack != null) spec.Light = LightAttack.ToSpec(AttackKind.Light);
            if (HeavyAttack != null) spec.Heavy = HeavyAttack.ToSpec(AttackKind.Heavy);
            for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
                spec.Skills[slot] = Skill(slot) != null ? Skill(slot).ToSpec() : null;
            return spec;
        }

        /// <summary> Ошибки данных персонажа (пусто — всё в порядке). Показываются в инспекторе и проверяются тестом. </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(Id)) problems.Add("Не задан Id.");
            else if (!IsValidId(Id)) problems.Add($"Id «{Id}»: только латиница, цифры, '_' и '-'.");
            if (LightAttack == null) problems.Add("Нет лёгкого удара.");
            if (HeavyAttack == null) problems.Add("Нет тяжёлого удара.");
            if (ViewPrefab != null && ViewPrefab.GetComponent("FighterView") == null)
                problems.Add("У ViewPrefab нет компонента FighterView.");
            for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
            {
                var sk = Skill(slot);
                if (sk == null) continue;
                string label = slot == (int)SkillSlot.Ultimate ? "Ultimate" : $"Skill{slot + 1}";
                if (sk.ManaCost > MaxMana) problems.Add($"{label} «{sk.DisplayName}»: стоит {sk.ManaCost} маны при запасе {MaxMana} — не применить никогда.");
                foreach (var p in sk.Validate()) problems.Add($"{label} «{sk.DisplayName}»: {p}");
                for (int other = 0; other < slot; other++)
                    if (Skill(other) == sk) problems.Add($"{label}: тот же скилл, что и в слоте {other + 1}.");
            }
            return problems;
        }

        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                    return false;
            return true;
        }

        /// <summary> Ассет по умолчанию или, если его нет, значения по умолчанию с ударами из Resources/Attacks. </summary>
        public static FighterDefinition LoadOrDefault()
        {
            var asset = Resources.Load<FighterDefinition>(DefaultResourcePath);
            if (asset != null) return asset;
            var def = CreateInstance<FighterDefinition>();
            def.Id = "default";
            def.LightAttack = Resources.Load<AttackData>("Attacks/Attack_Light");
            def.HeavyAttack = Resources.Load<AttackData>("Attacks/Attack_Heavy");
            return def;
        }
    }
}
