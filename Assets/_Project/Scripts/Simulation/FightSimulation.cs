using System;
using System.Collections.Generic;

namespace Game.Simulation
{
    /// <summary>
    /// Детерминированная симуляция боя 1 на 1: Step(состояние, вводы) → следующее состояние.
    /// Никакого времени кадра, камеры, Random и порядка Update: только GameState, TickInput и неизменяемый SimSetup.
    ///
    /// Порядок тика (для каждого бойца, сначала 0, потом 1):
    ///   ресурсы → стоп-кадр (если есть — только буферизуем ввод) → таймеры состояния (конец recovery/оглушения)
    ///   → буферная команда → новые команды → бег/стойка → движение.
    /// Затем тела расталкиваются и выталкиваются из стен, хитбоксы проверяются для обоих одновременно (размен
    /// ударами возможен), и обновляется фаза матча.
    ///
    /// Команда, пришедшая в тик, действует в этом же тике: удар входит в startup, уклонение уже сдвигает бойца.
    /// </summary>
    public sealed class FightSimulation
    {
        public readonly SimSetup Setup;
        /// <summary> События последнего Step (очищаются в начале каждого). </summary>
        public readonly List<SimEvent> Events = new();

        private MatchRules Rules => Setup.Rules;

        public FightSimulation(SimSetup setup)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            if (setup.Fighters == null || setup.Fighters.Length != GameState.FighterCount)
                throw new ArgumentException("Нужны параметры ровно двух бойцов", nameof(setup));
        }

        public GameState CreateInitialState()
        {
            var s = new GameState();
            StartMatch(s);
            Events.Clear();
            return s;
        }

        public void Step(GameState s, in TickInput input0, in TickInput input1)
        {
            Events.Clear();
            s.Tick++;

            if (input0.Contains(CommandKind.Restart) || input1.Contains(CommandKind.Restart))
            {
                StartMatch(s);
                return;
            }

            bool canAct = s.Phase == MatchPhase.Fight;
            StepFighter(s, 0, input0, canAct);
            StepFighter(s, 1, input1, canAct);
            ResolveBodies(s);
            if (s.Phase == MatchPhase.Fight) ResolveHits(s);
            AdvanceMatch(s);
        }

        // ---------- Запросы для отображения ----------

        public AttackSpec CurrentAttack(GameState s, int fighter)
        {
            ref readonly var f = ref s.Fighters[fighter];
            return f.State == ActionState.Attack ? Setup.Fighters[fighter].Attack(f.Attack) : null;
        }

        public AttackPhase AttackPhaseOf(GameState s, int fighter)
        {
            var atk = CurrentAttack(s, fighter);
            return atk == null ? AttackPhase.None : atk.PhaseAt(s.Fighters[fighter].StateTicks);
        }

        public bool HasIFrames(GameState s, int fighter)
        {
            ref readonly var f = ref s.Fighters[fighter];
            return f.State == ActionState.Dodge && f.StateTicks < Setup.Fighters[fighter].DodgeIFrameTicks;
        }

        public static bool IsBufferable(CommandKind kind) =>
            kind == CommandKind.LightAttack || kind == CommandKind.HeavyAttack || kind == CommandKind.Dodge ||
            kind == CommandKind.Parry || kind == CommandKind.BlockStart;

        // ---------- Боец ----------

        private void StepFighter(GameState s, int i, in TickInput input, bool canAct)
        {
            ref var f = ref s.Fighters[i];
            var spec = Setup.Fighters[i];
            Regenerate(ref f, spec);

            if (f.HitstopTicks > 0)
            {
                // Стоп-кадр: таймеры и движение стоят, ввод копится в буфере (отмены в комбо вводят именно сейчас).
                f.HitstopTicks--;
                f.MoveInput = canAct ? input.Move.ClampMagnitude(Fix.One) : FixVec2.Zero;
                for (int k = 0; k < input.Count; k++) HandleNew(s, i, ref f, spec, input[k], canAct, frozen: true);
                return;
            }

            Advance(ref f, spec, canAct);
            f.MoveInput = canAct ? input.Move.ClampMagnitude(Fix.One) : FixVec2.Zero;

            if (canAct) TryBuffered(s, i, ref f, spec);
            for (int k = 0; k < input.Count; k++) HandleNew(s, i, ref f, spec, input[k], canAct, frozen: false);

            UpdateLocomotion(ref f);
            Integrate(ref f, spec);
        }

        private static void Regenerate(ref FighterSim f, FighterSpec spec)
        {
            if (!f.IsAlive) return;
            if (f.StaminaRegenDelay > 0) f.StaminaRegenDelay--;
            else if (f.Stamina < spec.MaxStamina) f.Stamina = Fix.Min(spec.MaxStamina, f.Stamina + spec.StaminaRegen);
            if (f.Mana < spec.MaxMana) f.Mana = Fix.Min(spec.MaxMana, f.Mana + spec.ManaRegen);
        }

        /// <summary> Таймеры текущего состояния: выход из удара, оглушения, уклонения, парирования. </summary>
        private static void Advance(ref FighterSim f, FighterSpec spec, bool canAct)
        {
            if (f.StateTicks < int.MaxValue) f.StateTicks++;

            switch (f.State)
            {
                case ActionState.Attack:
                {
                    var atk = spec.Attack(f.Attack);
                    if (f.StateTicks == atk.StartupTicks && !TryPayForAttack(ref f, spec, atk))
                    {
                        Enter(ref f, ActionState.Idle);
                        break;
                    }
                    if (f.StateTicks >= atk.TotalTicks) Enter(ref f, ActionState.Idle);
                    break;
                }
                case ActionState.Block:
                    if (f.StunTicks > 0) f.StunTicks--;
                    if (f.StunTicks == 0 && !f.BlockHeld) Enter(ref f, ActionState.Idle);
                    break;
                case ActionState.Parry:
                    if (f.StateTicks >= spec.ParryWindowTicks) Enter(ref f, ActionState.ParryRecovery);
                    break;
                case ActionState.ParryRecovery:
                    if (f.StateTicks >= spec.ParryWhiffRecoveryTicks) Enter(ref f, ActionState.Idle);
                    break;
                case ActionState.Dodge:
                    if (f.StateTicks >= spec.DodgeTicks) Enter(ref f, ActionState.Idle);
                    break;
                case ActionState.Hitstun:
                case ActionState.ParryStunned:
                    if (--f.StunTicks <= 0) Enter(ref f, ActionState.Idle);
                    break;
            }

            // Палец всё ещё держит блок (блок не успел начаться или его прервало оглушение) — возвращаемся в блок.
            if (canAct && f.State == ActionState.Idle && f.BlockHeld) EnterBlock(ref f);
        }

        private void TryBuffered(GameState s, int i, ref FighterSim f, FighterSpec spec)
        {
            if (f.BufferTicks <= 0) return;
            var cmd = f.Buffered;
            if (TryCommand(s, i, ref f, spec, cmd))
            {
                f.BufferTicks = 0;
                CommandEvent(s, SimEventType.CommandAccepted, i, cmd);
                return;
            }
            if (--f.BufferTicks == 0) CommandEvent(s, SimEventType.CommandDropped, i, cmd);
        }

        private void HandleNew(GameState s, int i, ref FighterSim f, FighterSpec spec, SimCommand cmd, bool canAct, bool frozen)
        {
            switch (cmd.Kind)
            {
                case CommandKind.None:
                case CommandKind.Restart:
                    return;
                case CommandKind.BlockEnd:
                    HandleBlockEnd(s, i, ref f, cmd, canAct && !frozen);
                    return;
                case CommandKind.BlockStart:
                    f.BlockHeld = true;
                    break;
            }

            if (!canAct)
            {
                CommandEvent(s, SimEventType.CommandDropped, i, cmd);
                return;
            }

            if (!frozen && TryCommand(s, i, ref f, spec, cmd))
            {
                if (f.BufferTicks > 0) CommandEvent(s, SimEventType.CommandDropped, i, f.Buffered);
                f.BufferTicks = 0;
                CommandEvent(s, SimEventType.CommandAccepted, i, cmd);
                return;
            }

            // Сейчас нельзя (recovery, оглушение, стоп-кадр) — запоминаем; новая команда вытесняет старую.
            if (IsBufferable(cmd.Kind) && spec.InputBufferTicks > 0)
            {
                if (f.BufferTicks > 0) CommandEvent(s, SimEventType.CommandDropped, i, f.Buffered);
                f.Buffered = cmd;
                f.BufferTicks = spec.InputBufferTicks;
                return;
            }
            CommandEvent(s, SimEventType.CommandDropped, i, cmd);
        }

        private void HandleBlockEnd(GameState s, int i, ref FighterSim f, SimCommand cmd, bool canRelease)
        {
            bool wasHeld = f.BlockHeld;
            f.BlockHeld = false;
            // Отпустили раньше, чем блок успел начаться, — буферный блок больше не нужен.
            if (f.BufferTicks > 0 && f.Buffered.Kind == CommandKind.BlockStart)
            {
                CommandEvent(s, SimEventType.CommandDropped, i, f.Buffered);
                f.BufferTicks = 0;
            }

            if (canRelease && f.State == ActionState.Block && f.StunTicks == 0)
            {
                Enter(ref f, ActionState.Idle);
                CommandEvent(s, SimEventType.CommandAccepted, i, cmd);
            }
            else
            {
                // Блок-стан или стоп-кадр: блок снимется, когда закончится (см. Advance).
                CommandEvent(s, wasHeld ? SimEventType.CommandAccepted : SimEventType.CommandDropped, i, cmd);
            }
        }

        /// <summary> Попробовать выполнить команду в текущем состоянии. Правила отмен — здесь. </summary>
        private bool TryCommand(GameState s, int i, ref FighterSim f, FighterSpec spec, SimCommand cmd)
        {
            switch (f.State)
            {
                case ActionState.Idle:
                case ActionState.Move:
                    return Route(s, i, ref f, spec, cmd);

                case ActionState.Attack:
                {
                    var atk = spec.Attack(f.Attack);
                    var phase = atk.PhaseAt(f.StateTicks);
                    bool isAttack = cmd.Kind == CommandKind.LightAttack || cmd.Kind == CommandKind.HeavyAttack;

                    // Комбо: попавший удар отменяется в следующий.
                    if (isAttack && phase != AttackPhase.Startup && f.AttackConnected && atk.CancelOnHit)
                        return TryAttack(s, i, ref f, spec, KindOf(cmd.Kind));

                    switch (phase)
                    {
                        // Startup — окно распознавания жеста: касание уже запустило удар, продолжение жеста его уточняет.
                        case AttackPhase.Startup:
                            switch (cmd.Kind)
                            {
                                case CommandKind.Dodge: return TryDodge(ref f, spec, cmd);
                                case CommandKind.Parry: EnterParry(ref f); return true;
                                case CommandKind.BlockStart: EnterBlock(ref f); return true;
                                case CommandKind.HeavyAttack:
                                    return f.Attack != AttackKind.Heavy && TryAttack(s, i, ref f, spec, AttackKind.Heavy);
                            }
                            return false;
                        // Recovery отменяется в оборону.
                        case AttackPhase.Recovery:
                            if (cmd.Kind == CommandKind.Parry) { EnterParry(ref f); return true; }
                            if (cmd.Kind == CommandKind.Dodge) return TryDodge(ref f, spec, cmd);
                            return false;
                    }
                    return false;
                }

                case ActionState.Block:
                    if (f.StunTicks > 0) return false;
                    switch (cmd.Kind)
                    {
                        case CommandKind.BlockStart: return true; // уже в блоке
                        case CommandKind.Parry: EnterParry(ref f); return true;
                        case CommandKind.Dodge: return TryDodge(ref f, spec, cmd);
                    }
                    return false;

                case ActionState.Dodge:
                    // Flick распознаётся при отпускании, а сдвиг пальца уже запустил уклонение: это этап распознавания,
                    // а не намерение игрока — отменяем в парирование и возвращаем стамину.
                    if (cmd.Kind == CommandKind.Parry && f.StateTicks <= spec.DodgeToParryCancelTicks)
                    {
                        if (f.DodgePaid) f.Stamina = Fix.Min(spec.MaxStamina, f.Stamina + spec.DodgeStaminaCost);
                        EnterParry(ref f);
                        return true;
                    }
                    return false;
            }
            return false;
        }

        private bool Route(GameState s, int i, ref FighterSim f, FighterSpec spec, SimCommand cmd)
        {
            switch (cmd.Kind)
            {
                case CommandKind.LightAttack:
                case CommandKind.HeavyAttack:
                    return TryAttack(s, i, ref f, spec, KindOf(cmd.Kind));
                case CommandKind.BlockStart:
                    EnterBlock(ref f);
                    return true;
                case CommandKind.Parry:
                    EnterParry(ref f);
                    return true;
                case CommandKind.Dodge:
                    return TryDodge(ref f, spec, cmd);
            }
            return false;
        }

        private static AttackKind KindOf(CommandKind kind) =>
            kind == CommandKind.HeavyAttack ? AttackKind.Heavy : AttackKind.Light;

        /// <summary> Удар, если он настроен и хватает стамины. Поворачивает к противнику в радиусе захвата. </summary>
        private bool TryAttack(GameState s, int i, ref FighterSim f, FighterSpec spec, AttackKind kind)
        {
            var atk = spec.Attack(kind);
            if (atk == null || f.Stamina < atk.StaminaCost) return false;

            Enter(ref f, ActionState.Attack);
            f.Attack = atk.Kind;
            f.BlockHeld = false;

            ref readonly var target = ref s.Fighters[1 - i];
            if (target.IsAlive)
            {
                var toTarget = target.Position - f.Position;
                var r = Rules.LockOnRadius;
                if (!toTarget.IsZero && toTarget.SqrMagnitude <= r * r) f.Facing = toTarget.Normalized;
            }

            if (atk.StartupTicks == 0 && !TryPayForAttack(ref f, spec, atk))
            {
                Enter(ref f, ActionState.Idle);
                return false;
            }
            return true;
        }

        /// <summary> Стамина за удар списывается при выходе хитбокса: отмена в startup бесплатна. </summary>
        private static bool TryPayForAttack(ref FighterSim f, FighterSpec spec, AttackSpec atk)
        {
            if (atk.StaminaCost.Raw <= 0) return true;
            if (f.Stamina < atk.StaminaCost) return false;
            Spend(ref f, spec, atk.StaminaCost);
            return true;
        }

        private static bool TryDodge(ref FighterSim f, FighterSpec spec, SimCommand cmd)
        {
            if (f.Stamina < spec.DodgeStaminaCost) return false;
            var dir = cmd.Direction.Normalized;
            if (dir.IsZero) dir = -f.Facing; // без направления — назад
            Enter(ref f, ActionState.Dodge);
            f.DodgeDirection = dir;
            f.BlockHeld = false;
            if (spec.DodgeStaminaCost.Raw > 0)
            {
                Spend(ref f, spec, spec.DodgeStaminaCost);
                f.DodgePaid = true;
            }
            return true;
        }

        private static void Spend(ref FighterSim f, FighterSpec spec, Fix cost)
        {
            f.Stamina -= cost;
            f.StaminaRegenDelay = spec.StaminaRegenDelayTicks;
        }

        private static void EnterParry(ref FighterSim f)
        {
            Enter(ref f, ActionState.Parry);
            f.BlockHeld = false;
        }

        private static void EnterBlock(ref FighterSim f)
        {
            Enter(ref f, ActionState.Block);
            f.StunTicks = 0;
        }

        private static void Enter(ref FighterSim f, ActionState state)
        {
            f.State = state;
            f.StateTicks = 0;
            f.Attack = AttackKind.None;
            f.AttackResolved = false;
            f.AttackConnected = false;
            f.DodgePaid = false;
            if (!KeepsKnockback(state)) f.Velocity = FixVec2.Zero;
        }

        /// <summary> В этих состояниях отбрасывание доигрывает; в остальных боец стоит или движется сам. </summary>
        private static bool KeepsKnockback(ActionState state) =>
            state == ActionState.Hitstun || state == ActionState.Block ||
            state == ActionState.ParryStunned || state == ActionState.Dead;

        private static void UpdateLocomotion(ref FighterSim f)
        {
            if (f.State == ActionState.Idle && !f.MoveInput.IsZero) Enter(ref f, ActionState.Move);
            else if (f.State == ActionState.Move && f.MoveInput.IsZero) Enter(ref f, ActionState.Idle);
        }

        private void Integrate(ref FighterSim f, FighterSpec spec)
        {
            switch (f.State)
            {
                case ActionState.Move:
                    f.Position += f.MoveInput * spec.MoveSpeed;
                    var dir = f.MoveInput.Normalized;
                    if (!dir.IsZero) f.Facing = dir; // поворот мгновенный: отзывчивость важнее плавности
                    break;
                case ActionState.Dodge:
                    f.Position += f.DodgeDirection * spec.DodgeSpeed;
                    break;
                default:
                    if (KeepsKnockback(f.State) && !f.Velocity.IsZero)
                    {
                        f.Position += f.Velocity;
                        f.Velocity = Decelerate(f.Velocity, Rules.KnockbackDeceleration);
                    }
                    break;
            }
        }

        private static FixVec2 Decelerate(FixVec2 v, Fix decel)
        {
            var speed = v.Magnitude;
            if (speed <= decel) return FixVec2.Zero;
            return v * ((speed - decel) / speed);
        }

        // ---------- Тела и удары ----------

        private void ResolveBodies(GameState s)
        {
            ref var a = ref s.Fighters[0];
            ref var b = ref s.Fighters[1];
            var ra = Setup.Fighters[0].BodyRadius;
            var rb = Setup.Fighters[1].BodyRadius;
            if (a.IsAlive && b.IsAlive) Collision.SeparateCircles(ref a.Position, ra, ref b.Position, rb);
            a.Position = Collision.ResolveArena(a.Position, ra, Setup.Arena);
            b.Position = Collision.ResolveArena(b.Position, rb, Setup.Arena);
        }

        private enum HitResult : byte { None, Hit, Blocked, Parried, Evaded }

        private readonly struct Contact
        {
            public readonly HitResult Result;
            public readonly AttackSpec Attack;
            public readonly int AttackerTicks;

            public Contact(HitResult result, AttackSpec attack, int attackerTicks)
            {
                Result = result;
                Attack = attack;
                AttackerTicks = attackerTicks;
            }
        }

        private void ResolveHits(GameState s)
        {
            // Сначала оба исхода по состоянию до ударов, потом применяем: одновременные удары разменываются.
            var c0 = CheckContact(s, 0);
            var c1 = CheckContact(s, 1);
            ApplyContact(s, 0, c0);
            ApplyContact(s, 1, c1);
        }

        private Contact CheckContact(GameState s, int a)
        {
            ref readonly var f = ref s.Fighters[a];
            ref readonly var d = ref s.Fighters[1 - a];
            if (f.State != ActionState.Attack || f.AttackResolved || f.HitstopTicks > 0 || !d.IsAlive) return default;

            var atk = Setup.Fighters[a].Attack(f.Attack);
            if (atk.PhaseAt(f.StateTicks) != AttackPhase.Active) return default;

            var center = f.Position + f.Facing * atk.HitOffset;
            var dspec = Setup.Fighters[1 - a];
            if (!Collision.CirclesOverlap(center, atk.HitRadius, d.Position, dspec.HurtRadius)) return default;

            HitResult result;
            if (d.State == ActionState.Parry) result = HitResult.Parried;                       // высший приоритет
            else if (d.State == ActionState.Dodge && d.StateTicks < dspec.DodgeIFrameTicks) result = HitResult.Evaded;
            else if (d.State == ActionState.Block) result = HitResult.Blocked;
            else result = HitResult.Hit;
            return new Contact(result, atk, f.StateTicks);
        }

        private void ApplyContact(GameState s, int a, Contact c)
        {
            if (c.Result == HitResult.None) return;
            int di = 1 - a;
            ref var f = ref s.Fighters[a];
            ref var d = ref s.Fighters[di];
            var dspec = Setup.Fighters[di];
            var atk = c.Attack;
            // Сколько тиков атакующему до свободы — для подсчёта преимущества по кадрам.
            int attackerBusy = atk.TotalTicks - c.AttackerTicks;

            switch (c.Result)
            {
                case HitResult.Parried:
                    // Награда за парирование: атакующий оглушён, защитник сразу свободен — окно контратаки.
                    Enter(ref f, ActionState.ParryStunned);
                    f.StunTicks = Rules.ParryStunTicks;
                    Enter(ref d, ActionState.Idle);
                    f.HitstopTicks = d.HitstopTicks = Rules.ParryHitstopTicks;
                    Emit(s, SimEventType.Parried, di, a, Fix.Zero, Rules.ParryStunTicks);
                    return;

                case HitResult.Evaded:
                    f.AttackResolved = true;
                    Emit(s, SimEventType.Evaded, a, di, Fix.Zero, 0);
                    return;

                case HitResult.Blocked:
                {
                    f.AttackResolved = f.AttackConnected = true;
                    var damage = atk.Damage * dspec.BlockDamageMultiplier;
                    TakeDamage(ref d, damage);
                    if (d.Health.Raw <= 0) // урон через блок тоже может добить
                    {
                        Kill(s, a, ref d, KnockDirection(f, d) * atk.KnockbackSpeed);
                        return;
                    }
                    d.StunTicks = Math.Max(d.StunTicks, atk.BlockstunTicks);
                    d.Velocity = KnockDirection(f, d) * (atk.KnockbackSpeed * dspec.BlockKnockbackMultiplier);
                    f.HitstopTicks = d.HitstopTicks = atk.HitstopTicks;
                    Emit(s, SimEventType.Blocked, a, di, damage, atk.BlockstunTicks - attackerBusy);
                    return;
                }

                case HitResult.Hit:
                {
                    f.AttackResolved = f.AttackConnected = true;
                    var knock = KnockDirection(f, d) * atk.KnockbackSpeed;
                    TakeDamage(ref d, atk.Damage);
                    f.HitstopTicks = atk.HitstopTicks;
                    if (d.Health.Raw <= 0)
                    {
                        Emit(s, SimEventType.Hit, a, di, atk.Damage, 0);
                        Kill(s, a, ref d, knock);
                        return;
                    }
                    Enter(ref d, ActionState.Hitstun);
                    d.StunTicks = atk.HitstunTicks;
                    d.Velocity = knock;
                    d.HitstopTicks = atk.HitstopTicks;
                    Emit(s, SimEventType.Hit, a, di, atk.Damage, atk.HitstunTicks - attackerBusy);
                    return;
                }
            }
        }

        private static FixVec2 KnockDirection(in FighterSim attacker, in FighterSim defender)
        {
            var dir = (defender.Position - attacker.Position).Normalized;
            return dir.IsZero ? attacker.Facing : dir;
        }

        private static void TakeDamage(ref FighterSim d, Fix amount)
        {
            d.Health -= amount;
            if (d.Health.Raw < 0) d.Health = Fix.Zero;
        }

        private void Kill(GameState s, int killer, ref FighterSim d, FixVec2 knock)
        {
            d.Health = Fix.Zero;
            Enter(ref d, ActionState.Dead);
            d.Velocity = knock;
            d.BlockHeld = false;
            d.BufferTicks = 0;
            Emit(s, SimEventType.KO, killer, 1 - killer, Fix.Zero, 0);
        }

        // ---------- Матч ----------

        private void StartMatch(GameState s)
        {
            s.Wins0 = s.Wins1 = 0;
            s.MatchWinner = -1;
            StartRound(s, 1);
        }

        private void StartRound(GameState s, int round)
        {
            s.Round = round;
            s.RoundTicksLeft = Rules.RoundTicks;
            s.LastRoundWinner = -1;
            for (int i = 0; i < GameState.FighterCount; i++) ResetFighter(ref s.Fighters[i], i);

            if (Rules.CountdownTicks > 0)
            {
                s.Phase = MatchPhase.Countdown;
                s.PhaseTicks = Rules.CountdownTicks;
            }
            else
            {
                s.Phase = MatchPhase.Fight;
                s.PhaseTicks = 0;
                Emit(s, SimEventType.RoundStarted, -1, -1, Fix.Zero, 0);
            }
        }

        private void ResetFighter(ref FighterSim f, int i)
        {
            var spec = Setup.Fighters[i];
            var spawn = Rules.Spawns[i];
            var facing = (Rules.Spawns[1 - i] - spawn).Normalized;
            f = new FighterSim
            {
                Position = spawn,
                Facing = facing.IsZero ? FixVec2.Forward : facing,
                State = ActionState.Idle,
                Health = spec.MaxHealth,
                Stamina = spec.MaxStamina,
                Mana = spec.MaxMana,
            };
        }

        private void AdvanceMatch(GameState s)
        {
            switch (s.Phase)
            {
                case MatchPhase.Countdown:
                    if (--s.PhaseTicks <= 0)
                    {
                        s.Phase = MatchPhase.Fight;
                        Emit(s, SimEventType.RoundStarted, -1, -1, Fix.Zero, 0);
                    }
                    break;

                case MatchPhase.Fight:
                {
                    bool dead0 = !s.Fighters[0].IsAlive, dead1 = !s.Fighters[1].IsAlive;
                    if (dead0 || dead1)
                    {
                        EndRound(s, dead0 && dead1 ? -1 : (dead0 ? 1 : 0));
                        break;
                    }
                    if (!Rules.Training && --s.RoundTicksLeft <= 0) EndRound(s, WinnerByHealth(s));
                    break;
                }

                case MatchPhase.RoundOver:
                    if (--s.PhaseTicks <= 0) NextRound(s);
                    break;
            }
        }

        /// <summary> По таймеру побеждает больший процент HP; равенство — ничья. </summary>
        private int WinnerByHealth(GameState s)
        {
            // h0 / max0 против h1 / max1 без деления.
            var left = s.Fighters[0].Health * Setup.Fighters[1].MaxHealth;
            var right = s.Fighters[1].Health * Setup.Fighters[0].MaxHealth;
            return left > right ? 0 : (right > left ? 1 : -1);
        }

        private void EndRound(GameState s, int winner)
        {
            s.Phase = MatchPhase.RoundOver;
            s.PhaseTicks = Math.Max(1, Rules.RoundOverTicks);
            s.LastRoundWinner = winner;
            if (!Rules.Training)
            {
                if (winner == 0) s.Wins0++;
                else if (winner == 1) s.Wins1++;
            }
            Emit(s, SimEventType.RoundEnded, -1, -1, Fix.Zero, 0, winner);
        }

        private void NextRound(GameState s)
        {
            bool decided = s.Wins0 >= Rules.RoundsToWin || s.Wins1 >= Rules.RoundsToWin || s.Round >= Rules.MaxRounds;
            if (!Rules.Training && decided)
            {
                s.Phase = MatchPhase.MatchOver;
                s.PhaseTicks = 0;
                s.MatchWinner = s.Wins0 > s.Wins1 ? 0 : (s.Wins1 > s.Wins0 ? 1 : -1);
                Emit(s, SimEventType.MatchEnded, -1, -1, Fix.Zero, 0, s.MatchWinner);
                return;
            }
            StartRound(s, s.Round + 1);
        }

        // ---------- События ----------

        private void Emit(GameState s, SimEventType type, int actor, int target, Fix amount, int advantage, int winner = -1)
        {
            Events.Add(new SimEvent
            {
                Type = type,
                Tick = s.Tick,
                Actor = actor,
                Target = target,
                Amount = amount,
                FrameAdvantage = advantage,
                Winner = winner,
            });
        }

        private void CommandEvent(GameState s, SimEventType type, int fighter, SimCommand cmd)
        {
            Events.Add(new SimEvent
            {
                Type = type,
                Tick = s.Tick,
                Actor = fighter,
                Target = -1,
                Command = cmd.Kind,
                CommandId = cmd.Id,
                Winner = -1,
            });
        }
    }
}
