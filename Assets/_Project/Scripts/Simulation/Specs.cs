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
        /// <summary> После попадания (или блока) active/recovery отменяются следующим ударом — комбо. </summary>
        public bool CancelOnHit = true;
        public Fix HitOffset = Fix.One;
        public Fix HitRadius = Fix.FromFloat(0.6f);

        public int TotalTicks => StartupTicks + ActiveTicks + RecoveryTicks;

        /// <summary> Фаза по числу тиков в состоянии атаки (0 — тик входа). </summary>
        public AttackPhase PhaseAt(int ticks)
        {
            if (ticks < StartupTicks) return AttackPhase.Startup;
            if (ticks < StartupTicks + ActiveTicks) return AttackPhase.Active;
            return AttackPhase.Recovery;
        }

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
            CancelOnHit = false,
            HitOffset = Fix.FromFloat(1.1f),
            HitRadius = Fix.FromFloat(0.7f),
        };
    }

    /// <summary> Параметры бойца в единицах симуляции (тики, м/тик). Строятся из FighterDefinition. </summary>
    public sealed class FighterSpec
    {
        public Fix MaxHealth = Fix.FromInt(100);
        public Fix MaxStamina = Fix.FromInt(100);
        public Fix StaminaRegen = SimTime.PerSecond(25f);
        public int StaminaRegenDelayTicks = SimTime.Seconds(0.6f);
        public Fix MaxMana = Fix.FromInt(100);
        public Fix ManaRegen = SimTime.PerSecond(4f);

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

        public AttackSpec Attack(AttackKind kind) => kind switch
        {
            AttackKind.Light => Light,
            AttackKind.Heavy => Heavy ?? Light,
            _ => null,
        };
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
