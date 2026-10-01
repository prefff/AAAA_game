using System;

namespace Game.Simulation
{
    /// <summary>
    /// Frame data удара в тиках симуляции. Неизменяемые данные (не часть состояния) — строятся из AttackData.
    /// Хитбокс — круг на расстоянии HitOffset перед бойцом.
    /// </summary>
    public sealed class AttackSpec
    {
        public AttackKind Kind = AttackKind.Light;
        public int StartupTicks = SimTime.Frames(6);
        public int ActiveTicks = SimTime.Frames(3);
        public int RecoveryTicks = SimTime.Frames(12);
        public Fix Damage = Fix.FromInt(8);
        /// <summary> Скорость отбрасывания цели, м/тик. </summary>
        public Fix KnockbackSpeed = SimTime.PerSecond(3f);
        public int HitstunTicks = SimTime.Frames(18);
        public int BlockstunTicks = SimTime.Frames(10);
        /// <summary> Стоп-кадр обоим бойцам при контакте (ощущение удара). </summary>
        public int HitstopTicks = SimTime.Frames(3);
        /// <summary> Списывается при выходе хитбокса: отмена в startup бесплатна. </summary>
        public Fix StaminaCost = Fix.Zero;
        /// <summary> Сколько стамины удар выбивает из блока; стамина кончилась — блок пробит. </summary>
        public Fix GuardDamage = Fix.FromInt(6);
        /// <summary> После попадания (или блока) active/recovery отменяются следующим ударом — комбо. </summary>
        public bool CancelOnHit = true;
        public Fix HitOffset = Fix.One;
        public Fix HitRadius = Fix.FromFloat(0.6f);
        /// <summary> Доля скорости бега во время удара: 1 — удар не тормозит, 0 — боец встаёт на месте. </summary>
        public Fix MoveSpeedFactor = Fix.One;

        public int TotalTicks => StartupTicks + ActiveTicks + RecoveryTicks;

        /// <summary> Фаза по числу тиков в состоянии атаки (0 — тик входа). </summary>
        public AttackPhase PhaseAt(int ticks)
        {
            if (ticks < StartupTicks) return AttackPhase.Startup;
            if (ticks < StartupTicks + ActiveTicks) return AttackPhase.Active;
            return AttackPhase.Recovery;
        }

        public HitData Hit => new(Damage, KnockbackSpeed, HitstunTicks, BlockstunTicks, HitstopTicks, GuardDamage);

        public static AttackSpec DefaultLight() => new();

        public static AttackSpec DefaultHeavy() => new()
        {
            Kind = AttackKind.Heavy,
            StartupTicks = SimTime.Frames(14),
            ActiveTicks = SimTime.Frames(4),
            RecoveryTicks = SimTime.Frames(28),
            Damage = Fix.FromInt(20),
            KnockbackSpeed = SimTime.PerSecond(7f),
            HitstunTicks = SimTime.Frames(34),
            BlockstunTicks = SimTime.Frames(18),
            HitstopTicks = SimTime.Frames(6),
            StaminaCost = Fix.FromInt(25),
            GuardDamage = Fix.FromInt(28),
            CancelOnHit = false,
            HitOffset = Fix.FromFloat(1.1f),
            HitRadius = Fix.FromFloat(0.7f),
        };
    }

    /// <summary> Что делает попадание — общее для ударов и скиллов. </summary>
    public readonly struct HitData
    {
        public readonly Fix Damage;
        /// <summary> Скорость отбрасывания, м/тик. </summary>
        public readonly Fix KnockbackSpeed;
        public readonly int HitstunTicks;
        public readonly int BlockstunTicks;
        public readonly int HitstopTicks;
        public readonly Fix GuardDamage;

        public HitData(Fix damage, Fix knockbackSpeed, int hitstunTicks, int blockstunTicks, int hitstopTicks, Fix guardDamage)
        {
            Damage = damage;
            KnockbackSpeed = knockbackSpeed;
            HitstunTicks = hitstunTicks;
            BlockstunTicks = blockstunTicks;
            HitstopTicks = hitstopTicks;
            GuardDamage = guardDamage;
        }
    }

    /// <summary>
    /// Скилл в единицах симуляции. Строится из SkillData. Цикл: startup (прицел уточняется, удержание пальца
    /// ставит каст на паузу в конце startup) → выход скилла (мана и перезарядка списываются только здесь, поэтому
    /// отмена в startup бесплатна) → recovery.
    /// </summary>
    public sealed class SkillSpec
    {
        public string Name = "Skill";
        public SkillKind Kind = SkillKind.Projectile;

        public int StartupTicks = SimTime.Frames(12);
        public int RecoveryTicks = SimTime.Frames(16);
        /// <summary> Неуязвимость от начала каста, тиков. </summary>
        public int InvulnerableTicks;
        /// <summary> Доля скорости бега во время каста: 1 — каст не тормозит, 0 — боец встаёт на месте. </summary>
        public Fix MoveSpeedFactor = Fix.One;

        public Fix ManaCost = Fix.FromInt(20);
        public int CooldownTicks = SimTime.Seconds(3f);
        /// <summary> Перезарядка в начале раунда (ультимейт открывается не сразу). </summary>
        public int InitialCooldownTicks;

        /// <summary> Дальность: полёт снаряда, дистанция телепорта, вынос области. </summary>
        public Fix Range = Fix.FromInt(9);
        /// <summary> Быстрый каст (без прицела) наводится на противника в пределах дальности. Иначе — вперёд / по джойстику. </summary>
        public bool AutoAim = true;

        public Fix ProjectileSpeed = SimTime.PerSecond(16f);
        public Fix ProjectileRadius = Fix.FromFloat(0.35f);
        /// <summary> Парирование отражает снаряд обратно (он становится снарядом защитника). </summary>
        public bool Reflectable = true;

        public Fix ZoneRadius = Fix.FromFloat(2.5f);
        /// <summary> Тиков от появления области до взрыва — время увидеть и уйти. </summary>
        public int ZoneDelayTicks = SimTime.Frames(30);

        public Fix Damage = Fix.FromInt(12);
        public Fix KnockbackSpeed = SimTime.PerSecond(4f);
        public int HitstunTicks = SimTime.Frames(20);
        public int BlockstunTicks = SimTime.Frames(12);
        public int HitstopTicks = SimTime.Frames(4);
        public Fix GuardDamage = Fix.FromInt(10);

        public int TotalTicks => StartupTicks + RecoveryTicks;
        /// <summary> Время жизни снаряда: долетает ровно до дальности. </summary>
        public int ProjectileLifetimeTicks => ProjectileSpeed.Raw <= 0 ? 1 : (int)((Range.Raw + ProjectileSpeed.Raw - 1) / ProjectileSpeed.Raw);

        public HitData Hit => new(Damage, KnockbackSpeed, HitstunTicks, BlockstunTicks, HitstopTicks, GuardDamage);
    }

    /// <summary> Параметры бойца в единицах симуляции (тики, м/тик). Строятся из FighterDefinition. </summary>
    public sealed class FighterSpec
    {
        public Fix MaxHealth = Fix.FromInt(100);
        public Fix MaxStamina = Fix.FromInt(100);
        public Fix StaminaRegen = SimTime.PerSecond(25f);
        public int StaminaRegenDelayTicks = SimTime.Seconds(0.6f);
        public Fix MaxMana = Fix.FromInt(100);
        /// <summary> Мана в начале раунда: полный запас сразу превращал бы первые секунды в обмен скиллами. </summary>
        public Fix StartMana = Fix.FromInt(50);
        public Fix ManaRegen = SimTime.PerSecond(5f);
        /// <summary> Мана за единицу нанесённого урона (и в блок): агрессия заряжает скиллы. </summary>
        public Fix ManaPerDamageDealt = Fix.Half;
        /// <summary> Мана за единицу полученного урона: проигрывающий быстрее получает шанс отыграться. </summary>
        public Fix ManaPerDamageTaken = Fix.FromFloat(0.35f);

        public Fix MoveSpeed = SimTime.PerSecond(5f);
        /// <summary> Тело: стены и другой боец не дают пройти. </summary>
        public Fix BodyRadius = Fix.FromFloat(0.45f);
        /// <summary> Уязвимая зона для хитбоксов. </summary>
        public Fix HurtRadius = Fix.Half;

        public int ParryWindowTicks = SimTime.Frames(11);
        /// <summary> Парирование мимо: столько тиков боец уязвим. Без этого парирование бесплатно спамится. </summary>
        public int ParryWhiffRecoveryTicks = SimTime.Frames(8);

        public Fix DodgeSpeed = SimTime.PerSecond(8f);
        public int DodgeTicks = SimTime.Frames(15);
        public int DodgeIFrameTicks = SimTime.Frames(8);
        public Fix DodgeStaminaCost = Fix.FromInt(20);
        /// <summary> Flick распознаётся при отпускании: уклонение, начатое сдвигом пальца, столько тиков ещё отменяется в парирование с возвратом стамины. </summary>
        public int DodgeToParryCancelTicks = SimTime.Frames(9);

        public Fix BlockDamageMultiplier = Fix.FromFloat(0.3f);
        public Fix BlockKnockbackMultiplier = Fix.FromFloat(0.3f);

        /// <summary> Команда, которую нельзя выполнить сейчас (recovery, hitstun), ждёт столько тиков. </summary>
        public int InputBufferTicks = SimTime.Frames(7);

        public AttackSpec Light = AttackSpec.DefaultLight();
        public AttackSpec Heavy = AttackSpec.DefaultHeavy();

        /// <summary> Скиллы по слотам (Skill1, Skill2, Ultimate); null или пустой слот — скилла нет. </summary>
        public SkillSpec[] Skills = new SkillSpec[SkillSlots];

        public const int SkillSlots = 3;

        public AttackSpec Attack(AttackKind kind) => kind switch
        {
            AttackKind.Light => Light,
            AttackKind.Heavy => Heavy ?? Light,
            _ => null,
        };

        public SkillSpec Skill(int slot) => Skills != null && slot >= 0 && slot < Skills.Length ? Skills[slot] : null;
    }

    /// <summary> Правила матча. По умолчанию — до 2 побед, раунд 60 с (решение по вопросу 5). </summary>
    public sealed class MatchRules
    {
        public int RoundsToWin = 2;
        /// <summary> Предел раундов при ничьих: дальше матч заканчивается по счёту. </summary>
        public int MaxRounds = 5;
        public int RoundTicks = SimTime.Seconds(60f);
        public int CountdownTicks = SimTime.Seconds(1.5f);
        public int RoundOverTicks = SimTime.Seconds(2f);

        /// <summary> Тренировка: нет таймера и счёта, после KO раунд просто перезапускается. </summary>
        public bool Training;

        /// <summary> Удар поворачивает к противнику, если тот ближе этого радиуса (как базовая атака MLBB). </summary>
        public Fix LockOnRadius = Fix.FromInt(4);

        public int ParryStunTicks = SimTime.Frames(30);
        public int ParryHitstopTicks = SimTime.Frames(8);
        /// <summary> Отражение снаряда: стоп-кадр парирующему (атакующий далеко, его не останавливаем). </summary>
        public int ReflectHitstopTicks = SimTime.Frames(5);

        /// <summary> Блок пробит: столько тиков защитник оглушён (дольше награды за парирование — это наказание за пассивность). </summary>
        public int GuardBreakStunTicks = SimTime.Frames(45);
        public int GuardBreakHitstopTicks = SimTime.Frames(8);

        // Комбо. Без затухания отмена лёгкого удара в лёгкий у стены бесконечна: стена гасит отбрасывание,
        // и цель не выходит из досягаемости. Каждое следующее попадание серии даёт меньше hitstun и урона.
        /// <summary> На столько тиков короче hitstun каждого следующего попадания серии. </summary>
        public int HitstunDecayPerHit = SimTime.Frames(3);
        public int MinHitstunTicks = SimTime.Frames(6);
        /// <summary> Столько первых попаданий серии — в полный урон. </summary>
        public int ComboFullDamageHits = 2;
        public Fix ComboDamageScalePerHit = Fix.FromFloat(0.15f);
        public Fix ComboMinDamageScale = Fix.FromFloat(0.4f);

        /// <summary> Торможение отбрасывания, (м/тик)/тик. </summary>
        public Fix KnockbackDeceleration = SimTime.PerSecondSquared(10f);

        public FixVec2[] Spawns = { FixVec2.FromFloat(-3f, 0f), FixVec2.FromFloat(3f, 0f) };
    }

    public enum ObstacleShape : byte { Circle, Box }

    /// <summary> Статичное препятствие арены. Box — прямоугольник по осям (Center ± HalfExtents). </summary>
    public readonly struct Obstacle
    {
        public readonly ObstacleShape Shape;
        public readonly FixVec2 Center;
        public readonly FixVec2 HalfExtents;
        public readonly Fix Radius;
        /// <summary> Перекрывает снаряды и линию видимости (скиллы этапа 6). </summary>
        public readonly bool BlocksProjectiles;

        private Obstacle(ObstacleShape shape, FixVec2 center, FixVec2 halfExtents, Fix radius, bool blocksProjectiles)
        {
            Shape = shape;
            Center = center;
            HalfExtents = halfExtents;
            Radius = radius;
            BlocksProjectiles = blocksProjectiles;
        }

        public static Obstacle Circle(FixVec2 center, Fix radius, bool blocksProjectiles = true) =>
            new(ObstacleShape.Circle, center, FixVec2.Zero, radius, blocksProjectiles);

        public static Obstacle Box(FixVec2 center, FixVec2 halfExtents, bool blocksProjectiles = true) =>
            new(ObstacleShape.Box, center, halfExtents, Fix.Zero, blocksProjectiles);
    }

    /// <summary> Плоская арена: прямоугольник Min..Max со стенами по краям и препятствия внутри. </summary>
    public sealed class ArenaSpec
    {
        public FixVec2 Min;
        public FixVec2 Max;
        public Obstacle[] Obstacles = Array.Empty<Obstacle>();

        /// <summary>
        /// Арена по умолчанию: открытый центр для дуэли, по бокам колонны и две стенки — укрытия, чтобы
        /// разорвать дистанцию или зажать соперника.
        /// </summary>
        public static ArenaSpec Default() => new()
        {
            Min = FixVec2.FromFloat(-11f, -7f),
            Max = FixVec2.FromFloat(11f, 7f),
            Obstacles = new[]
            {
                Obstacle.Circle(FixVec2.FromFloat(-6.5f, 0f), Fix.FromFloat(0.9f)),
                Obstacle.Circle(FixVec2.FromFloat(6.5f, 0f), Fix.FromFloat(0.9f)),
                Obstacle.Box(FixVec2.FromFloat(0f, 4.5f), FixVec2.FromFloat(2.5f, 0.4f)),
                Obstacle.Box(FixVec2.FromFloat(0f, -4.5f), FixVec2.FromFloat(2.5f, 0.4f)),
            },
        };

        public static ArenaSpec Open(float halfWidth, float halfDepth) => new()
        {
            Min = FixVec2.FromFloat(-halfWidth, -halfDepth),
            Max = FixVec2.FromFloat(halfWidth, halfDepth),
        };
    }

    /// <summary> Всё неизменяемое, что нужно симуляции: правила, арена, параметры двух бойцов. </summary>
    public sealed class SimSetup
    {
        public MatchRules Rules = new();
        public ArenaSpec Arena = ArenaSpec.Default();
        public FighterSpec[] Fighters = { new(), new() };
    }
}
