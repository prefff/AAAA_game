namespace Game.Simulation
{
    public enum BotMode : byte
    {
        /// <summary> Манекен: стоит и получает удары. </summary>
        Idle,
        /// <summary> Всё время держит блок — отработка давления и тяжёлого удара. </summary>
        Block,
        /// <summary> Подходит и бьёт по таймеру — отработка блока, парирования и уклонения. </summary>
        Attack,
        /// <summary> Подходит и смешивает удары, блок, уклонение и парирование. </summary>
        Aggressive,
    }

    /// <summary>
    /// Тренировочный бот. Решение — чистая функция состояния и номера тика (без своей памяти и Random),
    /// поэтому бот детерминирован, переживает откат и одинаково играет в записи.
    /// Выдаёт обычный TickInput — для симуляции он ничем не отличается от игрока.
    /// </summary>
    public static class TrainingBot
    {
        public const int DefaultAttackPeriodTicks = SimTime.TickRate; // раз в секунду

        public static TickInput Think(GameState s, SimSetup setup, int self, BotMode mode, int attackPeriodTicks = DefaultAttackPeriodTicks)
        {
            var input = new TickInput();
            ref readonly var me = ref s.Fighters[self];
            ref readonly var foe = ref s.Fighters[1 - self];
            if (s.Phase != MatchPhase.Fight || !me.IsAlive) return input;

            switch (mode)
            {
                case BotMode.Block:
                    if (!me.BlockHeld) input.Add(new SimCommand(CommandKind.BlockStart));
                    break;

                case BotMode.Attack:
                    if (me.BlockHeld) input.Add(new SimCommand(CommandKind.BlockEnd));
                    if (!Approach(ref input, me, foe, setup.Fighters[self]) && attackPeriodTicks > 0 && s.Tick % attackPeriodTicks == 0)
                    {
                        // Каждый третий — тяжёлый: разные тайминги для парирования.
                        bool heavy = (s.Tick / attackPeriodTicks) % 3 == 2;
                        input.Add(new SimCommand(heavy ? CommandKind.HeavyAttack : CommandKind.LightAttack));
                    }
                    break;

                case BotMode.Aggressive:
                    Aggressive(ref input, s, setup, self, me, foe);
                    break;
            }
            return input;
        }

        /// <summary> Идти к противнику, пока он дальше дистанции удара. true — ещё идём. </summary>
        private static bool Approach(ref TickInput input, in FighterSim me, in FighterSim foe, FighterSpec spec)
        {
            var reach = spec.Light.HitOffset + spec.Light.HitRadius;
            var toFoe = foe.Position - me.Position;
            if (toFoe.SqrMagnitude <= reach * reach) return false;
            var dir = toFoe.Normalized;
            input.MoveX = ToAxis(dir.X);
            input.MoveY = ToAxis(dir.Y);
            return true;
        }

        private static void Aggressive(ref TickInput input, GameState s, SimSetup setup, int self, in FighterSim me, in FighterSim foe)
        {
            // Решение меняется раз в 12 тиков (5 раз в секунду): иначе бот дёргается каждый кадр.
            const int decisionTicks = 12;
            int bucket = s.Tick / decisionTicks;
            uint roll = Mix((uint)bucket * 2654435761u ^ (uint)(self + 1) * 40503u) % 100;
            bool decisionTick = s.Tick % decisionTicks == 0;

            bool wantsBlock = roll >= 70 && roll < 85;
            if (me.BlockHeld && !wantsBlock) input.Add(new SimCommand(CommandKind.BlockEnd));

            if (Approach(ref input, me, foe, setup.Fighters[self]) || !decisionTick) return;

            if (roll < 45) input.Add(new SimCommand(CommandKind.LightAttack));
            else if (roll < 60) input.Add(new SimCommand(CommandKind.HeavyAttack));
            else if (roll < 70)
            {
                // Уклонение назад от противника.
                var away = (me.Position - foe.Position).Normalized;
                input.Add(new SimCommand(CommandKind.Dodge, ToAxis(away.X), ToAxis(away.Y)));
            }
            else if (wantsBlock) { if (!me.BlockHeld) input.Add(new SimCommand(CommandKind.BlockStart)); }
            else if (roll < 95) input.Add(new SimCommand(CommandKind.Parry));
            // остальное — пауза
        }

        private static sbyte ToAxis(Fix v)
        {
            long q = v.Raw * 127 / Fix.OneRaw;
            return (sbyte)(q > 127 ? 127 : (q < -127 ? -127 : q));
        }

        private static uint Mix(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }
    }
}
