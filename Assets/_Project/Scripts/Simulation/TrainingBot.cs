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
        /// <summary>
        /// Ближник: смешивает удары, блок, уклонение и парирование; продолжает попавший удар скиллом, телепортом
        /// сокращает дистанцию, ультимейт бросает по открытому противнику.
        /// </summary>
        Aggressive,
        /// <summary>
        /// Маг: кружит на дистанции, бросает нюк с упреждением, копит ману на ультимейт, телепортом уходит из ближнего
        /// боя в открытую сторону.
        /// </summary>
        Zoner,
    }

    /// <summary>
    /// Тренировочный бот. Решение — чистая функция состояния и номера тика (без своей памяти и Random),
    /// поэтому бот детерминирован, переживает откат и одинаково играет в записи.
    /// Выдаёт обычный TickInput — для симуляции он ничем не отличается от игрока: прицел скилла тоже уточняет
    /// непрерывным вводом, пока скилл не вышел, как палец на кнопке.
    ///
    /// Скиллы выбираются по типу и цифрам (дальность, startup, задержка области), а не по слоту и имени, поэтому
    /// бот играет любым персонажем ростера. «Честность»: бот не видит удар или снаряд раньше <see cref="ReactionTicks"/>
    /// и отвечает на угрозу не всегда — шанс задан характером режима (<see cref="Style"/>).
    /// </summary>
    public static class TrainingBot
    {
        public const int DefaultAttackPeriodTicks = SimTime.TickRate; // раз в секунду
        public const int ModeCount = 5;

        /// <summary> Реакция, тиков (~170 мс — быстрый человек): лёгкий удар (startup 7) бот не видит, тяжёлый (14) — успевает. </summary>
        public const int ReactionTicks = 10;

        /// <summary> Ультимейт откатится за столько тиков — остальные скиллы не тратят его ману. </summary>
        private static readonly int SaveForUltimateTicks = SimTime.Seconds(4f);
        /// <summary> Маг держит ману на телепорт, если тот откатится за столько тиков. </summary>
        private static readonly int SaveForBlinkTicks = SimTime.Seconds(2f);

        private static readonly Fix InvSqrt2 = Fix.FromRaw(46341); // 1/√2: поворот на 45° без тригонометрии
        private static readonly Fix ThreatMargin = Fix.FromRaw(Fix.OneRaw / 4);
        /// <summary> Отступ точек обхода от препятствия сверх радиуса тела: тело не цепляет угол. </summary>
        private static readonly Fix DetourClearance = Fix.FromFloat(0.3f);
        private static readonly Fix PathSlack = Fix.FromFloat(0.05f);
        /// <summary> Точка обхода ближе 10 см считается достигнутой. </summary>
        private static readonly Fix ReachedSq = Fix.FromFloat(0.01f);

        /// <summary> Характер режима: шансы из 100. </summary>
        private readonly struct Style
        {
            /// <summary> Ответить на летящий снаряд (отразить парированием или уклониться). </summary>
            public readonly int Projectile;
            /// <summary> Уйти из области до взрыва. </summary>
            public readonly int Zone;
            /// <summary> Увидеть startup тяжёлого удара и спарировать. </summary>
            public readonly int ReadHeavy;
            /// <summary> Держать ману на телепорт-побег. </summary>
            public readonly bool KeepBlink;

            public Style(int projectile, int zone, int readHeavy, bool keepBlink)
            {
                Projectile = projectile;
                Zone = zone;
                ReadHeavy = readHeavy;
                KeepBlink = keepBlink;
            }
        }

        private static readonly Style BrawlerStyle = new(55, 80, 40, keepBlink: false);
        private static readonly Style ZonerStyle = new(70, 85, 30, keepBlink: true);

        /// <summary> Всё, что бот знает о моменте: оба бойца, их параметры и взаимное положение. </summary>
        private readonly struct View
        {
            public readonly GameState S;
            public readonly SimSetup Setup;
            public readonly int Self;
            public readonly FighterSim Me;
            public readonly FighterSim Foe;
            public readonly FighterSpec MySpec;
            public readonly FighterSpec FoeSpec;
            public readonly Fix Dist;
            /// <summary> Единичный вектор к противнику (совпали — взгляд бота). </summary>
            public readonly FixVec2 Dir;
            /// <summary> Держать ману на телепорт-побег (характер режима). </summary>
            public readonly bool KeepBlink;

            public View(GameState s, SimSetup setup, int self, in Style style)
            {
                KeepBlink = style.KeepBlink;
                S = s;
                Setup = setup;
                Self = self;
                Me = s.Fighters[self];
                Foe = s.Fighters[1 - self];
                MySpec = setup.Fighters[self];
                FoeSpec = setup.Fighters[1 - self];
                var toFoe = Foe.Position - Me.Position;
                Dist = toFoe.Magnitude;
                Dir = Dist.Raw == 0 ? Me.Facing : toFoe / Dist;
            }

            public ArenaSpec Arena => Setup.Arena;
        }

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
                    if (!Approach(ref input, me, foe, setup.Fighters[self], setup.Arena) && attackPeriodTicks > 0 && s.Tick % attackPeriodTicks == 0)
                    {
                        // Каждый третий — тяжёлый: разные тайминги для парирования.
                        bool heavy = (s.Tick / attackPeriodTicks) % 3 == 2;
                        input.Add(new SimCommand(heavy ? CommandKind.HeavyAttack : CommandKind.LightAttack));
                    }
                    break;

                case BotMode.Aggressive:
                    if (foe.IsAlive) Brawler(ref input, new View(s, setup, self, BrawlerStyle));
                    break;

                case BotMode.Zoner:
                    if (foe.IsAlive) Zoner(ref input, new View(s, setup, self, ZonerStyle));
                    break;
            }
            return input;
        }

        /// <summary> Идти к противнику (в обход препятствий), пока он дальше дистанции удара. true — ещё идём. </summary>
        private static bool Approach(ref TickInput input, in FighterSim me, in FighterSim foe, FighterSpec spec, ArenaSpec arena)
        {
            var reach = spec.Light.HitOffset + spec.Light.HitRadius;
            var toFoe = foe.Position - me.Position;
            if (toFoe.SqrMagnitude <= reach * reach) return false;
            SetMove(ref input, PathTo(me.Position, foe.Position, spec.BodyRadius, arena));
            return true;
        }

        /// <summary>
        /// Направление к цели в обход препятствий. Прямой путь свободен — прямо; иначе — к точке обхода ближайшего
        /// перекрывающего препятствия (угол стенки, бок колонны). Из точек обхода лучше видимая, из видимых — та, от
        /// которой цель уже видна, дальше — с самым коротким путём. Пересчёт каждый тик: бот срезает угол, как только
        /// цель становится видна, и не упирается в стенку, за которой стоит противник.
        /// </summary>
        private static FixVec2 PathTo(FixVec2 from, FixVec2 to, Fix radius, ArenaSpec arena)
        {
            var direct = (to - from).Normalized;
            // Путь проверяем по чуть уменьшенному телу: прижатый к стенке бот сам её не «пересекает».
            var probe = radius - PathSlack;
            var obstacles = arena.Obstacles;
            int blocking = -1;
            var nearest = Fix.Zero;
            for (int k = 0; k < obstacles.Length; k++)
            {
                if (!Collision.SegmentHitsObstacle(from, to, obstacles[k], probe)) continue;
                var d = (obstacles[k].Center - from).SqrMagnitude;
                if (blocking < 0 || d < nearest)
                {
                    blocking = k;
                    nearest = d;
                }
            }
            if (blocking < 0) return direct;

            var o = obstacles[blocking];
            var pad = radius + DetourClearance;
            int corners = o.Shape == ObstacleShape.Box ? 4 : 2;
            var best = FixVec2.Zero;
            int bestRank = -1;
            var bestCost = Fix.Zero;
            for (int c = 0; c < corners; c++)
            {
                FixVec2 p;
                if (o.Shape == ObstacleShape.Box)
                {
                    var hx = o.HalfExtents.X + pad;
                    var hy = o.HalfExtents.Y + pad;
                    p = o.Center + new FixVec2((c & 1) == 0 ? hx : -hx, (c & 2) == 0 ? hy : -hy);
                }
                else
                {
                    var side = Perp(direct) * (o.Radius + pad);
                    p = o.Center + (c == 0 ? side : -side);
                }
                p = Collision.ResolveArena(p, radius, arena);
                var leg = p - from;
                if (leg.SqrMagnitude < ReachedSq) continue; // уже здесь — нужна следующая точка

                int rank = (PathBlocked(from, p, probe, arena) ? 0 : 2) + (PathBlocked(p, to, probe, arena) ? 0 : 1);
                var cost = leg.Magnitude + (to - p).Magnitude;
                if (rank > bestRank || (rank == bestRank && cost < bestCost))
                {
                    best = p;
                    bestRank = rank;
                    bestCost = cost;
                }
            }
            if (bestRank < 0) return direct;
            var dir = (best - from).Normalized;
            return dir.IsZero ? direct : dir;
        }

        private static bool PathBlocked(FixVec2 a, FixVec2 b, Fix probe, ArenaSpec arena)
        {
            var obstacles = arena.Obstacles;
            for (int k = 0; k < obstacles.Length; k++)
                if (Collision.SegmentHitsObstacle(a, b, obstacles[k], probe)) return true;
            return false;
        }

        // ---------- Ближник ----------

        private static void Brawler(ref TickInput input, in View v)
        {
            if (Defend(ref input, v, BrawlerStyle)) return;
            TrackAim(ref input, v);

            // Решение меняется раз в 12 тиков (5 раз в секунду): иначе бот дёргается каждый кадр.
            const int decisionTicks = 12;
            var s = v.S;
            uint roll = Roll((uint)(s.Tick / decisionTicks) * 2654435761u ^ (uint)(v.Self + 1) * 40503u);
            bool decisionTick = s.Tick % decisionTicks == 0;

            bool wantsBlock = roll >= 70 && roll < 85;
            if (v.Me.BlockHeld && !wantsBlock) input.Add(new SimCommand(CommandKind.BlockEnd));

            // Попавший удар продолжается каждый тик, а не по таймеру решений: окно отмены короткое.
            if (FollowUp(ref input, v, roll)) return;
            if (CanAct(v.Me) && BrawlerSkills(ref input, v, decisionTick, roll)) return;

            if (Approach(ref input, v.Me, v.Foe, v.MySpec, v.Arena) || !decisionTick || !CanAct(v.Me)) return;

            if (roll < 45) input.Add(new SimCommand(CommandKind.LightAttack));
            else if (roll < 60) input.Add(new SimCommand(CommandKind.HeavyAttack));
            else if (roll < 70) Dodge(ref input, -v.Dir); // уклонение назад от противника
            else if (wantsBlock) { if (!v.Me.BlockHeld) input.Add(new SimCommand(CommandKind.BlockStart)); }
            else if (roll < 95) input.Add(new SimCommand(CommandKind.Parry));
            // остальное — пауза
        }

        /// <summary>
        /// Лёгкий удар попал: отменить его в то, что наверняка достанет цель в hitstun, — ультимейт, нюк, тяжёлый, —
        /// иначе продолжить серию лёгким, пока она не упёрлась в LightChainMax. hitstun затухает с каждым попаданием,
        /// поэтому тяжёлый (startup 14) — только после первого. true — команда отдана.
        /// </summary>
        private static bool FollowUp(ref TickInput input, in View v, uint roll)
        {
            ref readonly var me = ref v.Me;
            if (me.State != ActionState.Attack || !me.AttackConnected || me.HitstopTicks > 0 || me.BufferTicks > 0) return false;
            var atk = v.MySpec.Attack(me.Attack);
            if (atk == null || !atk.CancelOnHit || atk.PhaseAt(me.StateTicks) == AttackPhase.Startup) return false;
            if (v.Foe.State != ActionState.Hitstun) return false;

            int hits = v.Foe.ComboHits;
            int ult = (int)SkillSlot.Ultimate;
            if (IsKind(v, ult, SkillKind.Zone) && Ready(v, ult, ignoreReserve: true) && Guaranteed(v, v.MySpec.Skill(ult)) &&
                CastAt(ref input, v, ult))
                return true;
            // Нюк — не с первого удара (иначе лёгкие серии не видно), а после второго или по настроению.
            if (hits >= 2 || roll % 2 == 0)
            {
                for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
                    if (IsKind(v, slot, SkillKind.Projectile) && Ready(v, slot, ignoreReserve: false) &&
                        Guaranteed(v, v.MySpec.Skill(slot)) && CastAt(ref input, v, slot))
                        return true;
            }
            if (HeavyConnects(v) && roll < 50)
            {
                input.Add(new SimCommand(CommandKind.HeavyAttack));
                return true;
            }
            if (me.Attack == AttackKind.Light && me.AttackStunned && me.LightChain < v.Setup.Rules.LightChainMax)
            {
                input.Add(new SimCommand(CommandKind.LightAttack));
                return true;
            }
            return false; // серия затухла — в нейтраль
        }

        private static bool BrawlerSkills(ref TickInput input, in View v, bool decisionTick, uint roll)
        {
            // Противник открыт (пробит блок, спарирован): вблизи — тяжёлый удар, издалека — то, что наверняка попадёт.
            if (HeavyConnects(v))
            {
                input.Add(new SimCommand(CommandKind.HeavyAttack));
                return true;
            }
            if (CastGuaranteed(ref input, v)) return true;
            if (!decisionTick) return false;

            int ult = (int)SkillSlot.Ultimate;
            // Ультимейт: противник занят (бьёт, кастует) или прячется в блок — чаще; иначе изредка, чтобы не копить вечно.
            if (Ready(v, ult, ignoreReserve: true) && roll < (Committed(v) || v.Foe.State == ActionState.Block ? 60 : 8) &&
                CastAt(ref input, v, ult))
                return true;

            var reach = v.MySpec.Light.HitOffset + v.MySpec.Light.HitRadius;
            for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
            {
                var sk = v.MySpec.Skill(slot);
                if (sk == null || slot == ult || !Ready(v, slot, ignoreReserve: false)) continue;
                switch (sk.Kind)
                {
                    case SkillKind.Blink:
                        // Отход: мало здоровья, противник вплотную.
                        if (v.Me.Health * 3 < v.MySpec.MaxHealth && v.Dist <= reach + Fix.One && roll < 40)
                            return Cast(ref input, slot, EscapeAim(v, sk));
                        // Сближение: далеко для шагов, но в дальности телепорта; чаще — пока противник занят.
                        if (v.Dist > reach + Fix.FromInt(2) && v.Dist <= sk.Range + reach && roll < (Committed(v) ? 80 : 45))
                            return Cast(ref input, slot, EngageAim(v, sk, reach));
                        break;
                    case SkillKind.Projectile:
                        // Издалека — только если останется мана на нюк в продолжение попавшего удара: он ценнее тычка.
                        if (v.Dist > Fix.FromInt(3) && roll < 55 && v.Me.Mana >= sk.ManaCost * 2 && CastAt(ref input, v, slot)) return true;
                        break;
                }
            }
            return false;
        }

        // ---------- Маг ----------

        /// <summary> Кружить на 4.5–7.5 м, бросать скиллы с упреждением; противник вплотную — телепорт в открытую сторону. </summary>
        private static void Zoner(ref TickInput input, in View v)
        {
            if (Defend(ref input, v, ZonerStyle)) return;
            TrackAim(ref input, v);

            var s = v.S;
            ZonerMove(ref input, v);
            if (!CanAct(v.Me)) return;
            if (CastGuaranteed(ref input, v)) return;
            if (s.Tick % 6 != 0) return;

            uint roll = Roll((uint)(s.Tick / 6) * 2246822519u ^ (uint)(v.Self + 1) * 3266489917u);
            int ult = (int)SkillSlot.Ultimate;
            var reach = v.FoeSpec.Light.HitOffset + v.FoeSpec.Light.HitRadius + v.MySpec.HurtRadius;

            if (v.Dist < Fix.Ratio(5, 2))
            {
                for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
                    if (IsKind(v, slot, SkillKind.Blink) && Ready(v, slot, ignoreReserve: true))
                    {
                        Cast(ref input, slot, EscapeAim(v, v.MySpec.Skill(slot)));
                        return;
                    }
                // Телепорта нет — отбиваться: тычок или уклонение в открытую сторону.
                if (v.Dist <= reach)
                {
                    if (roll < 35) input.Add(new SimCommand(CommandKind.LightAttack));
                    else if (roll < 60) Dodge(ref input, EscapeDir(v, v.MySpec.DodgeSpeed * v.MySpec.DodgeTicks));
                    return;
                }
            }

            if (Ready(v, ult, ignoreReserve: true) && roll < (Committed(v) || v.Foe.State == ActionState.Block ? 70 : 6) &&
                CastAt(ref input, v, ult))
                return;
            for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
                if (slot != ult && !IsKind(v, slot, SkillKind.Blink) && Ready(v, slot, ignoreReserve: false) && CastAt(ref input, v, slot))
                    return;
        }

        private static void ZonerMove(ref TickInput input, in View v)
        {
            var radial = FixVec2.Zero;
            if (v.Dist > Fix.Ratio(15, 2)) radial = v.Dir;
            else if (v.Dist < Fix.Ratio(9, 2)) radial = -v.Dir;
            // Кружит, меняя сторону раз в 1.5 с: стоящий маг — бесплатная мишень, и игроку полезно бить с упреждением.
            int sign = ((v.S.Tick / 90) + v.Self) % 2 == 0 ? 1 : -1;
            var move = radial * Fix.FromInt(2) + Perp(v.Dir) * Fix.FromInt(sign) + AwayFromWalls(v.Me.Position, v.Arena) * Fix.FromInt(3);
            SetMove(ref input, move.Normalized);
        }

        // ---------- Оборона ----------

        /// <summary>
        /// Ответ на угрозу: область под ногами — уйти (не успеваем — уклонение или телепорт в момент взрыва), снаряд —
        /// отразить парированием или пропустить сквозь уклонение, тяжёлый удар — спарировать. true — бот занят обороной.
        /// </summary>
        private static bool Defend(ref TickInput input, in View v, in Style style)
        {
            ref readonly var me = ref v.Me;
            bool busy = me.State == ActionState.Parry || me.State == ActionState.Dodge;

            if (ZoneThreat(v, style.Zone, out var center, out var radius, out int ticksLeft))
            {
                var away = me.Position - center;
                var dir = away.IsZero ? Perp(v.Dir) : away.Normalized;
                if (me.BlockHeld) input.Add(new SimCommand(CommandKind.BlockEnd));
                SetMove(ref input, dir);
                if (busy || !CanDefend(v)) return true;
                // Пешком не успеть — уклонение с неуязвимостью на взрыв, без стамины — телепорт.
                int walk = TicksToCover(radius + v.MySpec.HurtRadius - away.Magnitude, v.MySpec.MoveSpeed);
                if (walk < ticksLeft) return true;
                if (ticksLeft <= v.MySpec.DodgeIFrameTicks && me.Stamina >= v.MySpec.DodgeStaminaCost) Dodge(ref input, dir);
                else
                {
                    for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
                        if (IsKind(v, slot, SkillKind.Blink) && Ready(v, slot, ignoreReserve: true) &&
                            ticksLeft > v.MySpec.Skill(slot).StartupTicks)
                        {
                            Cast(ref input, slot, dir);
                            break;
                        }
                }
                return true;
            }

            int t = IncomingProjectile(v, style.Projectile, out var velocity, out bool reflectable, out uint choice);
            if (t >= 0 && t <= v.MySpec.ParryWindowTicks)
            {
                if (busy) return true;
                if (!CanDefend(v)) return false;
                // Отражение — лучший ответ, но не всегда: иначе нюк против бота бесполезен.
                if (reflectable && me.ParryCooldown == 0 && t >= 1 && t <= v.MySpec.ParryWindowTicks - 2 && choice % 3 != 0)
                {
                    input.Add(new SimCommand(CommandKind.Parry));
                    return true;
                }
                if (t < v.MySpec.DodgeIFrameTicks && me.Stamina >= v.MySpec.DodgeStaminaCost)
                {
                    var side = Perp(velocity.Normalized);
                    if (FixVec2.Dot(side, Center(v.Arena) - me.Position).Raw < 0) side = -side;
                    Dodge(ref input, side);
                    return true;
                }
            }

            if (ReadsHeavy(v, style.ReadHeavy))
            {
                if (busy) return true;
                if (CanDefend(v) && me.ParryCooldown == 0)
                {
                    input.Add(new SimCommand(CommandKind.Parry));
                    return true;
                }
            }
            return false;
        }

        /// <summary> Область противника накрывает бота (и бот её «заметил»): центр, радиус, тиков до взрыва. </summary>
        private static bool ZoneThreat(in View v, int chance, out FixVec2 center, out Fix radius, out int ticksLeft)
        {
            center = FixVec2.Zero;
            radius = Fix.Zero;
            ticksLeft = int.MaxValue;
            bool found = false;
            foreach (var o in v.S.Objects)
            {
                if (o.Kind != SkillObjectKind.Zone || o.Owner == v.Self) continue;
                var sk = v.Setup.Fighters[o.Caster].Skill((int)o.Slot);
                if (sk == null || sk.ZoneDelayTicks - o.TicksLeft < ReactionTicks || o.TicksLeft >= ticksLeft) continue;
                var r = sk.ZoneRadius + v.MySpec.HurtRadius + ThreatMargin;
                if ((v.Me.Position - o.Position).SqrMagnitude >= r * r) continue;
                // Шанс — свой у каждой области (тик появления и место), а не новый каждый тик: иначе бот реагировал бы всегда.
                int born = v.S.Tick - (sk.ZoneDelayTicks - o.TicksLeft);
                if (Roll(Mix((uint)o.Position.X.Raw ^ (uint)born * 2654435761u) ^ (uint)o.Position.Y.Raw * 19349663u ^ (uint)v.Self) >= chance) continue;
                center = o.Position;
                radius = sk.ZoneRadius;
                ticksLeft = o.TicksLeft;
                found = true;
            }
            return found;
        }

        /// <summary> Ближайший снаряд противника, который заденет бота: через сколько тиков (−1 — нет). </summary>
        private static int IncomingProjectile(in View v, int chance, out FixVec2 velocity, out bool reflectable, out uint choice)
        {
            velocity = FixVec2.Zero;
            reflectable = false;
            choice = 0;
            int best = -1;
            foreach (var o in v.S.Objects)
            {
                if (o.Kind != SkillObjectKind.Projectile || o.Owner == v.Self) continue;
                var sk = v.Setup.Fighters[o.Caster].Skill((int)o.Slot);
                if (sk == null || sk.ProjectileLifetimeTicks - o.TicksLeft < ReactionTicks) continue;

                var rel = v.Me.Position - o.Position;
                var speedSq = o.Velocity.SqrMagnitude;
                var along = FixVec2.Dot(rel, o.Velocity);
                if (speedSq.Raw == 0 || along.Raw <= 0) continue; // летит мимо, от бота
                var t = along / speedSq;
                var miss = rel - o.Velocity * t;
                var r = sk.ProjectileRadius + v.MySpec.HurtRadius + ThreatMargin;
                int ticks = ToTicks(t);
                if (miss.SqrMagnitude > r * r || ticks > o.TicksLeft || (best >= 0 && ticks >= best)) continue;
                if (Collision.SegmentBlocked(o.Position, v.Me.Position, v.Arena)) continue;

                // Шанс — свой у каждого снаряда (тик вылета и направление); отражённый — новый снаряд.
                int born = v.S.Tick - (sk.ProjectileLifetimeTicks - o.TicksLeft);
                uint seed = Mix(Mix((uint)o.Velocity.X.Raw ^ (uint)born * 2654435761u) ^ (uint)o.Velocity.Y.Raw * 83492791u ^ (uint)v.Self);
                if (seed % 100 >= chance) continue;
                best = ticks;
                velocity = o.Velocity;
                reflectable = sk.Reflectable;
                choice = seed / 100;
            }
            return best;
        }

        /// <summary> Противник в startup тяжёлого удара, бот это увидел и стоит в зоне хитбокса — пора парировать. </summary>
        private static bool ReadsHeavy(in View v, int chance)
        {
            ref readonly var foe = ref v.Foe;
            if (foe.State != ActionState.Attack || foe.Attack != AttackKind.Heavy || foe.StateTicks < ReactionTicks) return false;
            var atk = v.FoeSpec.Attack(foe.Attack);
            int left = atk.StartupTicks - foe.StateTicks;
            if (left <= 0 || left >= v.MySpec.ParryWindowTicks) return false;
            var r = atk.HitRadius + v.MySpec.HurtRadius + ThreatMargin;
            if ((foe.Position + foe.Facing * atk.HitOffset - v.Me.Position).SqrMagnitude >= r * r) return false;
            return Roll((uint)(v.S.Tick - foe.StateTicks) * 2654435761u ^ (uint)(v.Self + 7) * 40503u) < chance;
        }

        // ---------- Скиллы ----------

        /// <summary> Скилл, который наверняка попадёт по запертому противнику (ультимейт первым): без резерва маны. </summary>
        private static bool CastGuaranteed(ref TickInput input, in View v)
        {
            if (LockTicks(v) == 0) return false;
            for (int k = FighterSpec.SkillSlots - 1; k >= 0; k--) // ультимейт — последний слот, проверяем первым
            {
                var sk = v.MySpec.Skill(k);
                if (sk != null && Ready(v, k, ignoreReserve: k == (int)SkillSlot.Ultimate) && Guaranteed(v, sk) && CastAt(ref input, v, k))
                    return true;
            }
            return false;
        }

        /// <summary> Противник не успеет ни уйти, ни уклониться, пока скилл до него долетит / область взорвётся. </summary>
        private static bool Guaranteed(in View v, SkillSpec sk)
        {
            int locked = LockTicks(v);
            if (locked == 0) return false;
            switch (sk.Kind)
            {
                case SkillKind.Projectile:
                    if (!ProjectileAim(v, sk, sk.StartupTicks, out _, out var flight)) return false;
                    return locked > sk.StartupTicks + flight;
                case SkillKind.Zone:
                    if (!ZoneAim(v, sk, sk.StartupTicks, out _)) return false;
                    // Цель в центре области: выйти из неё пешком — столько тиков после конца оглушения.
                    int escape = TicksToCover(sk.ZoneRadius + v.FoeSpec.HurtRadius, v.FoeSpec.MoveSpeed);
                    return locked + escape > sk.StartupTicks + sk.ZoneDelayTicks + 2;
            }
            return false;
        }

        /// <summary> Скилл с прицелом в противника (с упреждением); false — сейчас не достать. </summary>
        private static bool CastAt(ref TickInput input, in View v, int slot)
        {
            var sk = v.MySpec.Skill(slot);
            if (sk == null) return false;
            FixVec2 aim;
            switch (sk.Kind)
            {
                case SkillKind.Projectile:
                    if (!ProjectileAim(v, sk, sk.StartupTicks, out aim, out _)) return false;
                    // В парирование не бросаем: отразится в лицо.
                    if (v.Foe.State == ActionState.Parry && sk.Reflectable) return false;
                    break;
                case SkillKind.Zone:
                    if (!ZoneAim(v, sk, sk.StartupTicks, out aim)) return false;
                    break;
                default:
                    return false; // телепорт — не «в противника»: EngageAim / EscapeAim
            }
            return Cast(ref input, slot, aim);
        }

        /// <summary> Пока скилл не вышел, прицел уточняется непрерывным вводом — как палец, ведущий цель. </summary>
        private static void TrackAim(ref TickInput input, in View v)
        {
            ref readonly var me = ref v.Me;
            if (me.State != ActionState.Cast || me.SkillFired) return;
            var sk = v.MySpec.Skill((int)me.CastSlot);
            if (sk == null) return;
            int delay = System.Math.Max(0, sk.StartupTicks - me.StateTicks);
            FixVec2 aim;
            switch (sk.Kind)
            {
                case SkillKind.Projectile:
                    if (!ProjectileAim(v, sk, delay, out aim, out _)) return;
                    break;
                case SkillKind.Zone:
                    if (!ZoneAim(v, sk, delay, out aim)) return;
                    break;
                default:
                    return; // телепорт: направление — намерение (сблизиться/уйти), его не ведём
            }
            input.Aim = AimState.Released;
            input.AimX = ToAxis(aim.X);
            input.AimY = ToAxis(aim.Y);
        }

        /// <summary> Направление снаряда в точку встречи: противник бежит, пока идёт startup и летит снаряд. </summary>
        private static bool ProjectileAim(in View v, SkillSpec sk, int delay, out FixVec2 aim, out int flight)
        {
            aim = FixVec2.Zero;
            flight = 0;
            if (sk.ProjectileSpeed.Raw <= 0) return false;
            var from = v.Me.Position;
            var target = v.Foe.Position;
            // Снаряд рождается перед телом и попадает при касании радиусов — лететь ему меньше, чем между центрами.
            var head = v.MySpec.BodyRadius + sk.ProjectileRadius + v.FoeSpec.HurtRadius;
            for (int k = 0; k < 2; k++)
            {
                flight = TicksToCover((target - from).Magnitude - head, sk.ProjectileSpeed);
                target = PredictFoe(v, delay + flight);
            }
            var to = target - from;
            var reach = sk.Range + v.FoeSpec.HurtRadius;
            if (to.IsZero || to.SqrMagnitude > reach * reach) return false;
            if (Collision.SegmentBlocked(from, target, v.Arena)) return false;
            aim = to.Normalized;
            return true;
        }

        /// <summary>
        /// Центр области: запертый противник — туда, куда его отнесёт; бегущий — на полпути упреждения (увидев область,
        /// человек свернёт, а точно в текущую точку — уйдёт, просто продолжая бег).
        /// </summary>
        private static bool ZoneAim(in View v, SkillSpec sk, int delay, out FixVec2 aim)
        {
            aim = FixVec2.Zero;
            int hitTime = delay + sk.ZoneDelayTicks;
            var target = PredictFoe(v, LockTicks(v) > 0 ? hitTime : hitTime / 2);
            var to = target - v.Me.Position;
            if (to.SqrMagnitude > (sk.Range + sk.ZoneRadius / 2) * (sk.Range + sk.ZoneRadius / 2)) return false;
            aim = (to / sk.Range).ClampMagnitude(Fix.One); // ноль — быстрый каст: автоприцел в противника
            return true;
        }

        /// <summary> Телепорт к противнику: встать на дистанции удара перед ним. </summary>
        private static FixVec2 EngageAim(in View v, SkillSpec sk, Fix reach)
        {
            var target = PredictFoe(v, sk.StartupTicks) - v.Dir * (reach * 3 / 4);
            var to = target - v.Me.Position;
            if (to.IsZero) return v.Dir;
            return (to / sk.Range).ClampMagnitude(Fix.One);
        }

        /// <summary> Телепорт прочь на полную дальность — в ту сторону, где после него до противника дальше всего (не в угол). </summary>
        private static FixVec2 EscapeAim(in View v, SkillSpec sk) => EscapeDir(v, sk.Range);

        private static FixVec2 EscapeDir(in View v, Fix distance)
        {
            var away = -v.Dir;
            var best = away;
            var bestScore = Fix.FromInt(-1);
            for (int k = 0; k < 5; k++)
            {
                // Прочь, ±45°, ±90°.
                var dir = k switch
                {
                    0 => away,
                    1 => Rotate45(away, 1),
                    2 => Rotate45(away, -1),
                    3 => Perp(away),
                    _ => -Perp(away),
                };
                var land = Collision.ResolveArena(v.Me.Position + dir * distance, v.MySpec.BodyRadius, v.Arena);
                var score = (land - v.Foe.Position).SqrMagnitude;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = dir;
                }
            }
            return best;
        }

        /// <summary> Где будет противник через ticks: бег, уклонение или отбрасывание с торможением. </summary>
        private static FixVec2 PredictFoe(in View v, int ticks)
        {
            ref readonly var foe = ref v.Foe;
            var spec = v.FoeSpec;
            var pos = foe.Position;
            if (ticks <= 0) return pos;
            switch (foe.State)
            {
                case ActionState.Move:
                    pos += foe.MoveInput * (spec.MoveSpeed * ticks);
                    break;
                case ActionState.Dodge:
                    pos += foe.DodgeDirection * (spec.DodgeSpeed * System.Math.Max(0, System.Math.Min(ticks, spec.DodgeTicks - foe.StateTicks)));
                    break;
                default:
                    // Равнозамедленно: путь до остановки — v·t/2. Почти нулевая скорость (корень округлился в 0) — стоит.
                    var decel = v.Setup.Rules.KnockbackDeceleration;
                    var speed = foe.Velocity.Magnitude;
                    if (speed.Raw == 0) break;
                    int stop = decel.Raw <= 0 ? ticks : ToTicks(speed / decel);
                    int tt = System.Math.Min(ticks, stop);
                    var travelled = stop <= 0 ? Fix.Zero : speed * tt - (decel * tt * tt) / 2;
                    pos += foe.Velocity / speed * Fix.Max(Fix.Zero, travelled);
                    break;
            }
            return Collision.ResolveArena(pos, spec.BodyRadius, v.Arena);
        }

        /// <summary> Сколько тиков противник не может ни двинуться, ни отменить действие в оборону. </summary>
        private static int LockTicks(in View v)
        {
            ref readonly var f = ref v.Foe;
            int stop = f.HitstopTicks;
            switch (f.State)
            {
                case ActionState.Hitstun:
                case ActionState.ParryStunned:
                case ActionState.GuardBroken:
                    return stop + f.StunTicks;
                case ActionState.Block:
                    return f.StunTicks > 0 ? stop + f.StunTicks : 0; // блок-стан
                case ActionState.ParryRecovery:
                    return stop + System.Math.Max(0, v.FoeSpec.ParryWhiffRecoveryTicks - f.StateTicks);
                case ActionState.Attack:
                {
                    // Active не отменяется ничем; startup и recovery — в уклонение/парирование.
                    var atk = v.FoeSpec.Attack(f.Attack);
                    if (atk == null || atk.PhaseAt(f.StateTicks) != AttackPhase.Active) return 0;
                    return stop + atk.StartupTicks + atk.ActiveTicks - f.StateTicks;
                }
            }
            return 0;
        }

        /// <summary> Тяжёлый удар наверняка попадёт: противник заперт дольше его startup и не улетит из хитбокса (отбрасывание). </summary>
        private static bool HeavyConnects(in View v)
        {
            var heavy = v.MySpec.Heavy;
            if (heavy == null || v.Me.Stamina < heavy.StaminaCost || LockTicks(v) <= heavy.StartupTicks) return false;
            var reach = heavy.HitOffset + heavy.HitRadius + v.FoeSpec.HurtRadius;
            return (PredictFoe(v, heavy.StartupTicks) - v.Me.Position).SqrMagnitude <= reach * reach;
        }

        /// <summary> Противник занят: бьёт или кастует (отменить можно, но человек обычно не успевает). </summary>
        private static bool Committed(in View v) =>
            LockTicks(v) > 0 || v.Foe.State == ActionState.Attack || v.Foe.State == ActionState.Cast;

        /// <summary>
        /// Скилл готов: перезарядка прошла, маны хватает. Без ignoreReserve мана ультимейта, который вот-вот откатится
        /// (и у мага — телепорта-побега), не тратится: иначе мелкие скиллы съедают её и ультимейт не выходит никогда.
        /// </summary>
        private static bool Ready(in View v, int slot, bool ignoreReserve)
        {
            var sk = v.MySpec.Skill(slot);
            if (sk == null || v.Me.Cooldown(slot) > 0) return false;
            var need = sk.ManaCost;
            if (!ignoreReserve && slot != (int)SkillSlot.Ultimate)
            {
                bool keepBlink = v.KeepBlink && (v.Me.Health < v.MySpec.MaxHealth || v.Dist < Fix.FromInt(6));
                for (int other = 0; other < FighterSpec.SkillSlots; other++)
                {
                    var o = v.MySpec.Skill(other);
                    if (o == null || other == slot) continue;
                    if (other == (int)SkillSlot.Ultimate && v.Me.Cooldown(other) <= SaveForUltimateTicks) need += o.ManaCost;
                    else if (o.Kind == SkillKind.Blink && keepBlink && v.Me.Cooldown(other) <= SaveForBlinkTicks) need += o.ManaCost;
                }
            }
            return v.Me.Mana >= need;
        }

        private static bool IsKind(in View v, int slot, SkillKind kind) => v.MySpec.Skill(slot)?.Kind == kind;

        private static bool Cast(ref TickInput input, int slot, FixVec2 aim)
        {
            input.Add(new SimCommand(FightSimulation.SkillCommand(slot), ToAxis(aim.X), ToAxis(aim.Y)));
            return true;
        }

        // ---------- Состояние бота ----------

        /// <summary> Может начать новое действие прямо сейчас. </summary>
        private static bool CanAct(in FighterSim f)
        {
            if (f.HitstopTicks > 0) return false;
            return f.State == ActionState.Idle || f.State == ActionState.Move || (f.State == ActionState.Block && f.StunTicks == 0);
        }

        /// <summary> Может отменить текущее действие в уклонение или парирование (иначе команда ляжет в буфер и сработает поздно). </summary>
        private static bool CanDefend(in View v)
        {
            ref readonly var f = ref v.Me;
            if (f.HitstopTicks > 0) return false;
            switch (f.State)
            {
                case ActionState.Idle:
                case ActionState.Move:
                case ActionState.Cast:
                    return true;
                case ActionState.Block:
                    return f.StunTicks == 0;
                case ActionState.Attack:
                    var atk = v.MySpec.Attack(f.Attack);
                    return atk != null && atk.PhaseAt(f.StateTicks) != AttackPhase.Active;
            }
            return false;
        }

        // ---------- Геометрия и ввод ----------

        private static void Dodge(ref TickInput input, FixVec2 dir) =>
            input.Add(new SimCommand(CommandKind.Dodge, ToAxis(dir.X), ToAxis(dir.Y)));

        private static void SetMove(ref TickInput input, FixVec2 dir)
        {
            input.MoveX = ToAxis(dir.X);
            input.MoveY = ToAxis(dir.Y);
        }

        private static FixVec2 Perp(FixVec2 v) => new(-v.Y, v.X);

        private static FixVec2 Rotate45(FixVec2 v, int sign) => sign > 0
            ? new FixVec2((v.X - v.Y) * InvSqrt2, (v.X + v.Y) * InvSqrt2)
            : new FixVec2((v.X + v.Y) * InvSqrt2, (v.Y - v.X) * InvSqrt2);

        private static FixVec2 Center(ArenaSpec arena) => (arena.Min + arena.Max) / Fix.FromInt(2);

        /// <summary> Отталкивание от краёв арены ближе 2.5 м: сила 0..1 по каждой оси. </summary>
        private static FixVec2 AwayFromWalls(FixVec2 p, ArenaSpec arena)
        {
            var margin = Fix.Ratio(5, 2);
            return new FixVec2(Push(p.X - arena.Min.X, margin) - Push(arena.Max.X - p.X, margin),
                               Push(p.Y - arena.Min.Y, margin) - Push(arena.Max.Y - p.Y, margin));
        }

        private static Fix Push(Fix gap, Fix margin) => gap >= margin ? Fix.Zero : (margin - Fix.Max(Fix.Zero, gap)) / margin;

        private static int ToTicks(Fix t) => t.Raw <= 0 ? 0 : (int)(t.Raw >> Fix.FractionalBits);

        /// <summary> За сколько тиков пройти distance со скоростью speed (м/тик); стоящему — никогда. </summary>
        private static int TicksToCover(Fix distance, Fix speed)
        {
            if (distance.Raw <= 0) return 0;
            return speed.Raw <= 0 ? int.MaxValue / 2 : ToTicks(distance / speed);
        }

        private static sbyte ToAxis(Fix v)
        {
            long q = v.Raw * 127 / Fix.OneRaw;
            return (sbyte)(q > 127 ? 127 : (q < -127 ? -127 : q));
        }

        private static uint Roll(uint seed) => Mix(seed) % 100;

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
