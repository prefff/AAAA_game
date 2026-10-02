using System;
using System.Collections.Generic;

namespace Game.Simulation
{
    /// <summary>
    /// Детерминированная симуляция боя 1 на 1: Step(состояние, вводы) → следующее состояние.
    /// Никакого времени кадра, камеры, Random и порядка Update: только GameState, TickInput и неизменяемый SimSetup.
    ///
    /// Порядок тика (для каждого бойца, сначала 0, потом 1):
    ///   ресурсы и перезарядки → прицел скилла и отмена каста → стоп-кадр (если есть — только буферизуем ввод)
    ///   → таймеры состояния (конец recovery/оглушения, выход скилла) → буферная команда → новые команды
    ///   → бег/стойка → движение.
    /// Затем тела расталкиваются и выталкиваются из стен, хитбоксы проверяются для обоих одновременно (размен
    /// ударами возможен), летят снаряды и взрываются области, обновляется фаза матча.
    ///
    /// Команда, пришедшая в тик, действует в этом же тике: удар входит в startup, уклонение уже сдвигает бойца,
    /// каст скилла начинается.
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
            if (s.Phase == MatchPhase.Fight)
            {
                ResolveHits(s);
                StepObjects(s);
            }
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

        public SkillSpec CurrentSkill(GameState s, int fighter)
        {
            ref readonly var f = ref s.Fighters[fighter];
            return f.State == ActionState.Cast ? Setup.Fighters[fighter].Skill((int)f.CastSlot) : null;
        }

        public bool HasIFrames(GameState s, int fighter) => IsInvulnerable(s.Fighters[fighter], Setup.Fighters[fighter]);

        /// <summary> Скилл готов: есть в слоте, перезарядка прошла, маны хватает. </summary>
        public bool CanCast(GameState s, int fighter, int slot)
        {
            var sk = Setup.Fighters[fighter].Skill(slot);
            ref readonly var f = ref s.Fighters[fighter];
            return sk != null && f.Cooldown(slot) <= 0 && f.Mana >= sk.ManaCost;
        }

        /// <summary>
        /// Куда уйдёт скилл с прицелом aim (направление × доля дальности; ноль — автоприцел) — тот же расчёт, что при
        /// выходе скилла. Для индикатора прицела: игрок видит ровно то, что сделает симуляция.
        /// </summary>
        public FixVec2 PreviewAim(GameState s, int fighter, int slot, FixVec2 aim, out Fix distance)
        {
            var sk = Setup.Fighters[fighter].Skill(slot);
            if (sk == null)
            {
                distance = Fix.Zero;
                return FixVec2.Zero;
            }
            return ResolveAim(s, fighter, sk, aim.ClampMagnitude(Fix.One), out distance);
        }

        public static bool IsBufferable(CommandKind kind) =>
            kind == CommandKind.LightAttack || kind == CommandKind.HeavyAttack || kind == CommandKind.Dodge ||
            kind == CommandKind.Parry || kind == CommandKind.BlockStart || IsSkill(kind);

        public static bool IsSkill(CommandKind kind) =>
            kind == CommandKind.Skill1 || kind == CommandKind.Skill2 || kind == CommandKind.Ultimate;

        public static int SlotOf(CommandKind kind) => kind switch
        {
            CommandKind.Skill1 => 0,
            CommandKind.Skill2 => 1,
            CommandKind.Ultimate => 2,
            _ => -1,
        };

        public static CommandKind SkillCommand(int slot) => slot switch
        {
            0 => CommandKind.Skill1,
            1 => CommandKind.Skill2,
            2 => CommandKind.Ultimate,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Слотов скиллов " + FighterSpec.SkillSlots),
        };

        // ---------- Боец ----------

        private void StepFighter(GameState s, int i, in TickInput input, bool canAct)
        {
            ref var f = ref s.Fighters[i];
            var spec = Setup.Fighters[i];
            Regenerate(ref f, spec);

            // Прицел — непрерывный ввод, как джойстик: пока палец держит кнопку, боец свободен (бегает, бьёт), прицел
            // только запоминается. Обновляется до команд, чтобы скилл, пришедший с отпусканием пальца, ушёл по нему.
            if (canAct && input.Aim != AimState.None) f.SkillAim = input.AimVector.ClampMagnitude(Fix.One);
            for (int k = 0; k < input.Count; k++)
                if (input[k].Kind == CommandKind.SkillCancel) CancelCast(s, i, ref f, input[k], canAct);

            if (f.HitstopTicks > 0)
            {
                // Стоп-кадр: таймеры и движение стоят, ввод копится в буфере (отмены в комбо вводят именно сейчас).
                f.HitstopTicks--;
                f.MoveInput = canAct ? input.Move.ClampMagnitude(Fix.One) : FixVec2.Zero;
                for (int k = 0; k < input.Count; k++) HandleNew(s, i, ref f, spec, input[k], canAct, frozen: true);
                return;
            }

            Advance(s, i, ref f, spec, canAct);
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
            if (f.Cooldown0 > 0) f.Cooldown0--;
            if (f.Cooldown1 > 0) f.Cooldown1--;
            if (f.Cooldown2 > 0) f.Cooldown2--;
            if (f.ParryCooldown > 0) f.ParryCooldown--;
            if (f.StunImmunityTicks > 0) f.StunImmunityTicks--;
        }

        /// <summary> Таймеры текущего состояния: выход из удара, оглушения, уклонения, парирования; выход скилла. </summary>
        private void Advance(GameState s, int i, ref FighterSim f, FighterSpec spec, bool canAct)
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
                case ActionState.Cast:
                    AdvanceCast(s, i, ref f, spec);
                    break;
                case ActionState.Block:
                    if (f.StunTicks > 0) f.StunTicks--;
                    if (f.StunTicks == 0 && !f.BlockHeld) Enter(ref f, ActionState.Idle);
                    break;
                case ActionState.Parry:
                    // Окно прошло без удара: палец держит блок — просто блок, отпустили — короткая уязвимость.
                    if (f.StateTicks >= spec.ParryWindowTicks)
                    {
                        if (f.BlockHeld && canAct) EnterBlock(ref f);
                        else Enter(ref f, ActionState.ParryRecovery);
                    }
                    break;
                case ActionState.ParryRecovery:
                    if (f.StateTicks >= spec.ParryWhiffRecoveryTicks) Enter(ref f, ActionState.Idle);
                    break;
                case ActionState.Dodge:
                    if (f.StateTicks >= spec.DodgeTicks) Enter(ref f, ActionState.Idle);
                    break;
                case ActionState.Hitstun:
                    if (--f.StunTicks <= 0)
                    {
                        Enter(ref f, ActionState.Idle);
                        f.StunImmunityTicks = Rules.LightStunImmunityTicks;
                    }
                    break;
                case ActionState.ParryStunned:
                    if (--f.StunTicks <= 0) Enter(ref f, ActionState.Idle);
                    break;
                case ActionState.GuardBroken:
                    if (--f.StunTicks <= 0)
                    {
                        // Блок восстанавливается наполовину: иначе удерживающий блок ломался бы снова первым же ударом.
                        f.Stamina = Fix.Max(f.Stamina, spec.MaxStamina / 2);
                        Enter(ref f, ActionState.Idle);
                    }
                    break;
            }

            // Палец всё ещё держит блок (блок не успел начаться или его прервало оглушение) — возвращаемся в блок.
            if (canAct && f.State == ActionState.Idle && f.BlockHeld) EnterBlock(ref f);
        }

        /// <summary>
        /// Каст: startup (боец поворачивается по прицелу) → выход скилла → recovery. Прицеливание идёт до каста
        /// (палец на кнопке, боец свободен), поэтому каст не ждёт и сам по таймеру не выходит. Бежать можно всё время
        /// каста (см. Integrate).
        /// </summary>
        private void AdvanceCast(GameState s, int i, ref FighterSim f, FighterSpec spec)
        {
            var sk = spec.Skill((int)f.CastSlot);
            if (sk == null)
            {
                Enter(ref f, ActionState.Idle);
                return;
            }

            if (!f.SkillFired)
            {
                if (f.StateTicks < sk.StartupTicks)
                {
                    var dir = ResolveAim(s, i, sk, f.SkillAim, out _);
                    if (!dir.IsZero) f.Facing = dir;
                    return;
                }
                FireSkill(s, i, ref f, spec, sk);
                if (f.State != ActionState.Cast) return;
            }
            if (f.StateTicks >= sk.TotalTicks) Enter(ref f, ActionState.Idle);
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
                case CommandKind.SkillCancel: // обработана до таймеров
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

            // Сейчас нельзя (recovery, оглушение, стоп-кадр, перезарядка вот-вот кончится) — запоминаем; новая команда вытесняет старую.
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

        /// <summary> Отмена каста до выхода скилла (и скилла, ждущего в буфере): мана и перезарядка не тратятся. </summary>
        private void CancelCast(GameState s, int i, ref FighterSim f, SimCommand cmd, bool canAct)
        {
            bool cancelled = false;
            if (canAct && f.State == ActionState.Cast && !f.SkillFired)
            {
                Enter(ref f, ActionState.Idle);
                cancelled = true;
            }
            if (f.BufferTicks > 0 && IsSkill(f.Buffered.Kind))
            {
                CommandEvent(s, SimEventType.CommandDropped, i, f.Buffered);
                f.BufferTicks = 0;
                cancelled = true;
            }
            CommandEvent(s, cancelled ? SimEventType.CommandAccepted : SimEventType.CommandDropped, i, cmd);
        }

        /// <summary> Попробовать выполнить команду в текущем состоянии. Правила отмен — здесь. </summary>
        private bool TryCommand(GameState s, int i, ref FighterSim f, FighterSpec spec, SimCommand cmd)
        {
            bool isSkill = IsSkill(cmd.Kind);
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

                    // Комбо: попавший (или заблокированный) удар отменяется в следующий удар или в скилл.
                    if (phase != AttackPhase.Startup && f.AttackConnected)
                    {
                        if (isAttack && atk.CancelOnHit && CanChain(f, KindOf(cmd.Kind)))
                            return TryAttack(s, i, ref f, spec, KindOf(cmd.Kind), chained: true);
                        if (isSkill) return TrySkill(s, i, ref f, spec, cmd);
                    }

                    switch (phase)
                    {
                        // Startup — окно распознавания жеста: касание уже запустило удар, продолжение жеста его уточняет.
                        case AttackPhase.Startup:
                            if (isSkill) return TrySkill(s, i, ref f, spec, cmd);
                            switch (cmd.Kind)
                            {
                                case CommandKind.Dodge: return TryDodge(ref f, spec, cmd);
                                case CommandKind.Parry: return TryParry(ref f, spec, held: false);
                                case CommandKind.BlockStart: StartGuard(ref f, spec); return true;
                                case CommandKind.HeavyAttack:
                                    return f.Attack != AttackKind.Heavy && TryAttack(s, i, ref f, spec, AttackKind.Heavy);
                            }
                            return false;
                        // Recovery отменяется в оборону: уклонение или парирование (обычный блок — только после recovery).
                        case AttackPhase.Recovery:
                            if (cmd.Kind == CommandKind.Parry) return TryParry(ref f, spec, held: false);
                            if (cmd.Kind == CommandKind.BlockStart) return TryParry(ref f, spec, held: true);
                            if (cmd.Kind == CommandKind.Dodge) return TryDodge(ref f, spec, cmd);
                            return false;
                    }
                    return false;
                }

                case ActionState.Cast:
                    // До выхода скилла каст бесплатно отменяется в оборону (как startup удара); после — только recovery-отмены.
                    switch (cmd.Kind)
                    {
                        case CommandKind.Dodge: return TryDodge(ref f, spec, cmd);
                        case CommandKind.Parry: return TryParry(ref f, spec, held: false);
                        case CommandKind.BlockStart:
                            if (f.SkillFired) return TryParry(ref f, spec, held: true);
                            StartGuard(ref f, spec);
                            return true;
                    }
                    return false;

                case ActionState.Block:
                    if (f.StunTicks > 0) return false;
                    if (isSkill) return TrySkill(s, i, ref f, spec, cmd);
                    switch (cmd.Kind)
                    {
                        case CommandKind.BlockStart: return true; // уже в блоке
                        case CommandKind.Parry: return TryParry(ref f, spec, held: false);
                        case CommandKind.Dodge: return TryDodge(ref f, spec, cmd);
                    }
                    return false;

                case ActionState.Parry:
                    // Удержание, потом свайп — уклонение из блока, даже если окно парирования ещё идёт.
                    if (cmd.Kind == CommandKind.Dodge) return TryDodge(ref f, spec, cmd);
                    return cmd.Kind == CommandKind.BlockStart; // уже в обороне: окно доиграет и перейдёт в блок
            }
            return false;
        }

        /// <summary>
        /// Можно ли отменить попавший удар в удар kind. Тяжёлый — после любого контакта. Лёгкий в лёгкий — только если
        /// цель оглушена и серия не длиннее LightChainMax: цепочка лёгких короткая, продолжение — тяжёлым или скиллом.
        /// </summary>
        private bool CanChain(in FighterSim f, AttackKind kind)
        {
            if (kind != AttackKind.Light) return true;
            return f.AttackStunned && f.LightChain < Rules.LightChainMax;
        }

        private bool Route(GameState s, int i, ref FighterSim f, FighterSpec spec, SimCommand cmd)
        {
            switch (cmd.Kind)
            {
                case CommandKind.LightAttack:
                case CommandKind.HeavyAttack:
                    return TryAttack(s, i, ref f, spec, KindOf(cmd.Kind));
                case CommandKind.BlockStart:
                    StartGuard(ref f, spec);
                    return true;
                case CommandKind.Parry:
                    return TryParry(ref f, spec, held: false);
                case CommandKind.Dodge:
                    return TryDodge(ref f, spec, cmd);
                case CommandKind.Skill1:
                case CommandKind.Skill2:
                case CommandKind.Ultimate:
                    return TrySkill(s, i, ref f, spec, cmd);
            }
            return false;
        }

        private static AttackKind KindOf(CommandKind kind) =>
            kind == CommandKind.HeavyAttack ? AttackKind.Heavy : AttackKind.Light;

        /// <summary>
        /// Удар, если он настроен и хватает стамины. Поворачивает к противнику в радиусе захвата. chained — отмена
        /// попавшего удара: лёгкий продолжает серию лёгких, иначе начинает новую.
        /// </summary>
        private bool TryAttack(GameState s, int i, ref FighterSim f, FighterSpec spec, AttackKind kind, bool chained = false)
        {
            var atk = spec.Attack(kind);
            if (atk == null || f.Stamina < atk.StaminaCost) return false;

            int chain = atk.Kind != AttackKind.Light ? 0 : (chained ? f.LightChain + 1 : 1);
            Enter(ref f, ActionState.Attack);
            f.Attack = atk.Kind;
            f.LightChain = chain;
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

        /// <summary> Начать каст: скилл есть, перезарядка прошла, маны хватает. Мана и перезарядка — при выходе скилла. </summary>
        private bool TrySkill(GameState s, int i, ref FighterSim f, FighterSpec spec, SimCommand cmd)
        {
            int slot = SlotOf(cmd.Kind);
            var sk = spec.Skill(slot);
            if (sk == null || f.Cooldown(slot) > 0 || f.Mana < sk.ManaCost) return false;

            Enter(ref f, ActionState.Cast);
            f.CastSlot = (SkillSlot)slot;
            f.BlockHeld = false;
            // С кнопки: прицел пришёл непрерывным вводом (в тике отпускания пальца). Бот — прицел в команде.
            if (!cmd.UsesInputAim) f.SkillAim = cmd.Direction.ClampMagnitude(Fix.One);

            var dir = ResolveAim(s, i, sk, f.SkillAim, out _);
            if (!dir.IsZero) f.Facing = dir;
            if (sk.StartupTicks == 0) FireSkill(s, i, ref f, spec, sk);
            return true;
        }

        /// <summary>
        /// Направление и дальность скилла. Прицел — направление × доля дальности. Без прицела: на противника в пределах
        /// дальности (если у скилла автоприцел), иначе по джойстику, иначе вперёд — на полную дальность.
        /// </summary>
        private FixVec2 ResolveAim(GameState s, int i, SkillSpec sk, FixVec2 aim, out Fix distance)
        {
            ref readonly var f = ref s.Fighters[i];
            if (!aim.IsZero)
            {
                var m = aim.Magnitude;
                distance = sk.Range * Fix.Min(m, Fix.One);
                return aim / m;
            }

            if (sk.AutoAim)
            {
                ref readonly var foe = ref s.Fighters[1 - i];
                var toFoe = foe.Position - f.Position;
                if (foe.IsAlive && !toFoe.IsZero && toFoe.SqrMagnitude <= sk.Range * sk.Range)
                {
                    distance = toFoe.Magnitude;
                    return toFoe / distance;
                }
            }

            distance = sk.Range;
            var move = f.MoveInput.Normalized;
            return move.IsZero ? f.Facing : move;
        }

        /// <summary> Выход скилла: списать ману и запустить перезарядку, затем снаряд / телепорт / область. </summary>
        private void FireSkill(GameState s, int i, ref FighterSim f, FighterSpec spec, SkillSpec sk)
        {
            int slot = (int)f.CastSlot;
            if (f.Mana < sk.ManaCost)
            {
                Enter(ref f, ActionState.Idle); // не бывает при штатном ходе, но мана не может уйти в минус
                return;
            }
            f.Mana -= sk.ManaCost;
            f.SetCooldown(slot, sk.CooldownTicks);
            f.SkillFired = true;

            var dir = ResolveAim(s, i, sk, f.SkillAim, out var distance);
            if (dir.IsZero) dir = f.Facing;
            f.Facing = dir;
            var arena = Setup.Arena;
            FixVec2 at;

            switch (sk.Kind)
            {
                case SkillKind.Projectile:
                    at = f.Position + dir * spec.BodyRadius;
                    Spawn(s, new SkillObject
                    {
                        Kind = SkillObjectKind.Projectile,
                        Owner = i,
                        Caster = i,
                        Slot = f.CastSlot,
                        Position = at,
                        Velocity = dir * sk.ProjectileSpeed,
                        TicksLeft = sk.ProjectileLifetimeTicks,
                    });
                    break;

                case SkillKind.Blink:
                    // Телепорт сквозь препятствия: в стену не встанешь — выталкивает к ближайшему краю.
                    at = Collision.ResolveArena(f.Position + dir * distance, spec.BodyRadius, arena);
                    f.Position = at;
                    break;

                case SkillKind.Zone:
                    at = Collision.ClampToArena(f.Position + dir * distance, arena);
                    Spawn(s, new SkillObject
                    {
                        Kind = SkillObjectKind.Zone,
                        Owner = i,
                        Caster = i,
                        Slot = f.CastSlot,
                        Position = at,
                        TicksLeft = Math.Max(1, sk.ZoneDelayTicks),
                    });
                    break;

                default:
                    // Новый SkillKind без ветки здесь — ошибка кода, а не данных: молча делать из него другой скилл нельзя.
                    throw new InvalidOperationException($"SkillKind {sk.Kind} ({sk.Name}) не реализован в FireSkill");
            }
            Emit(s, SimEventType.SkillFired, i, -1, Fix.Zero, 0, slot: slot, position: at);
        }

        private static void Spawn(GameState s, in SkillObject obj)
        {
            var objects = s.Objects;
            for (int k = 0; k < objects.Length; k++)
            {
                if (objects[k].IsActive) continue;
                objects[k] = obj;
                return;
            }
            // Пул полон (12 объектов на арене не бывает при штатных перезарядках) — скилл тратится впустую.
        }

        private static bool TryDodge(ref FighterSim f, FighterSpec spec, SimCommand cmd)
        {
            if (f.Stamina < spec.DodgeStaminaCost) return false;
            var dir = cmd.Direction.Normalized;
            if (dir.IsZero) dir = -f.Facing; // без направления — назад
            Enter(ref f, ActionState.Dodge);
            f.DodgeDirection = dir;
            f.BlockHeld = false;
            if (spec.DodgeStaminaCost.Raw > 0) Spend(ref f, spec, spec.DodgeStaminaCost);
            return true;
        }

        private static void Spend(ref FighterSim f, FighterSpec spec, Fix cost)
        {
            f.Stamina -= cost;
            f.StaminaRegenDelay = spec.StaminaRegenDelayTicks;
        }

        /// <summary>
        /// Нажатие блока: если парирование перезарядилось — блок начинается с окна парирования («блок вовремя»),
        /// иначе — обычный блок.
        /// </summary>
        private static void StartGuard(ref FighterSim f, FighterSpec spec)
        {
            if (!TryParry(ref f, spec, held: true)) EnterBlock(ref f);
        }

        /// <summary>
        /// Окно парирования, если оно перезарядилось. held — палец держит блок: после окна боец останется в блоке;
        /// иначе (команда Parry — нажатие и отпускание) после окна — короткая уязвимость.
        /// </summary>
        private static bool TryParry(ref FighterSim f, FighterSpec spec, bool held)
        {
            if (f.ParryCooldown > 0) return false;
            Enter(ref f, ActionState.Parry);
            f.BlockHeld = held;
            f.ParryCooldown = spec.ParryRearmTicks;
            return true;
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
            f.AttackStunned = false;
            f.SkillFired = false;
            if (!KeepsKnockback(state)) f.Velocity = FixVec2.Zero;
        }

        /// <summary> В этих состояниях отбрасывание доигрывает; в остальных боец стоит или движется сам. </summary>
        private static bool KeepsKnockback(ActionState state) =>
            state == ActionState.Hitstun || state == ActionState.Block || state == ActionState.GuardBroken ||
            state == ActionState.ParryStunned || state == ActionState.Dead;

        /// <summary> Неуязвимость: начало уклонения или скилла с неуязвимостью (телепорт). </summary>
        private static bool IsInvulnerable(in FighterSim f, FighterSpec spec)
        {
            switch (f.State)
            {
                case ActionState.Dodge:
                    return f.StateTicks < spec.DodgeIFrameTicks;
                case ActionState.Cast:
                    var sk = spec.Skill((int)f.CastSlot);
                    return sk != null && f.StateTicks < sk.InvulnerableTicks;
            }
            return false;
        }

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
                case ActionState.Attack:
                    // Удар не останавливает бойца (касание правой зоны — всегда удар, даже если жест станет уклонением):
                    // бег продолжается, но разворот — нет, хитбокс бьёт туда, куда смотрел боец в начале удара.
                    f.Position += f.MoveInput * (spec.MoveSpeed * spec.Attack(f.Attack).MoveSpeedFactor);
                    break;
                case ActionState.Cast:
                    // Каст тоже не отнимает управление: бег продолжается, смотрит боец по прицелу (см. AdvanceCast).
                    var sk = spec.Skill((int)f.CastSlot);
                    if (sk != null) f.Position += f.MoveInput * (spec.MoveSpeed * sk.MoveSpeedFactor);
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
            else if (IsInvulnerable(d, dspec)) result = HitResult.Evaded;
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
                    d.ParryCooldown = 0; // удачное парирование перезаряжается сразу: следующий удар серии тоже можно поймать
                    f.HitstopTicks = d.HitstopTicks = Rules.ParryHitstopTicks;
                    Emit(s, SimEventType.Parried, di, a, Fix.Zero, Rules.ParryStunTicks, position: d.Position);
                    return;

                case HitResult.Evaded:
                    f.AttackResolved = true;
                    Emit(s, SimEventType.Evaded, a, di, Fix.Zero, 0, position: d.Position);
                    return;

                case HitResult.Blocked:
                    f.AttackResolved = f.AttackConnected = true;
                    LandBlock(s, a, atk.Hit, KnockDirection(f, d), attackerBusy, melee: true, slot: -1);
                    return;

                case HitResult.Hit:
                    f.AttackResolved = f.AttackConnected = true;
                    if (atk.Kind == AttackKind.Light && d.StunImmunityTicks > 0)
                    {
                        LandUnstunned(s, a, atk.Hit, attackerBusy);
                        return;
                    }
                    f.AttackStunned = true;
                    LandHit(s, a, atk.Hit, KnockDirection(f, d), attackerBusy, melee: true, slot: -1);
                    return;
            }
        }

        /// <summary>
        /// Лёгкий удар по цели, которая только что вышла из оглушения: урон проходит, но цель не оглушена и продолжает
        /// своё действие. Так серия лёгких не перезапускается сама собой — после неё у противника есть ход.
        /// </summary>
        private void LandUnstunned(GameState s, int a, in HitData hit, int attackerBusy)
        {
            int di = 1 - a;
            ref var f = ref s.Fighters[a];
            ref var d = ref s.Fighters[di];

            DealDamage(s, a, hit.Damage);
            f.HitstopTicks = hit.HitstopTicks;
            if (d.Health.Raw <= 0)
            {
                Emit(s, SimEventType.Hit, a, di, hit.Damage, 0, combo: 1, slot: -1, position: d.Position);
                Kill(s, a, ref d, FixVec2.Zero);
                return;
            }
            d.HitstopTicks = hit.HitstopTicks;
            Emit(s, SimEventType.Hit, a, di, hit.Damage, -attackerBusy, combo: 1, slot: -1, position: d.Position);
        }

        /// <summary>
        /// Попадание по цели противника a. Серия: каждое следующее попадание, пока цель в hitstun, даёт меньше hitstun
        /// (цель рано или поздно вырывается — даже у стены) и, начиная с ComboFullDamageHits, меньше урона.
        /// melee — атакующий рядом и тоже получает стоп-кадр; снаряд и область его не останавливают.
        /// </summary>
        private void LandHit(GameState s, int a, in HitData hit, FixVec2 knockDir, int attackerBusy, bool melee, int slot, int caster = -1)
        {
            int di = 1 - a;
            ref var f = ref s.Fighters[a];
            ref var d = ref s.Fighters[di];

            int n = d.State == ActionState.Hitstun ? d.ComboHits : 0;
            var damage = hit.Damage * ComboDamageScale(n);
            int hitstun = n == 0
                ? hit.HitstunTicks
                : Math.Min(hit.HitstunTicks, Math.Max(Rules.MinHitstunTicks, hit.HitstunTicks - Rules.HitstunDecayPerHit * n));
            var knock = knockDir * hit.KnockbackSpeed;

            DealDamage(s, a, damage);
            if (melee) f.HitstopTicks = hit.HitstopTicks;
            if (d.Health.Raw <= 0)
            {
                Emit(s, SimEventType.Hit, a, di, damage, 0, combo: n + 1, slot: slot, caster: caster, position: d.Position);
                Kill(s, a, ref d, knock);
                return;
            }
            Enter(ref d, ActionState.Hitstun);
            d.StunTicks = hitstun;
            d.StunImmunityTicks = 0; // оглушил тяжёлый или скилл — серия продолжается; иммунитет — после выхода из неё
            d.Velocity = knock;
            d.HitstopTicks = hit.HitstopTicks;
            d.ComboHits = n + 1;
            Emit(s, SimEventType.Hit, a, di, damage, hitstun - attackerBusy, combo: n + 1, slot: slot, caster: caster, position: d.Position);
        }

        /// <summary>
        /// Удар в блок: часть урона проходит, а стамина защитника тратится. Кончилась — блок пробит: долгое оглушение.
        /// Так бесконечный блок перестаёт быть ответом на всё, а тяжёлый удар получает роль «ломать оборону».
        /// </summary>
        private void LandBlock(GameState s, int a, in HitData hit, FixVec2 knockDir, int attackerBusy, bool melee, int slot, int caster = -1)
        {
            int di = 1 - a;
            ref var f = ref s.Fighters[a];
            ref var d = ref s.Fighters[di];
            var dspec = Setup.Fighters[di];

            var damage = hit.Damage * dspec.BlockDamageMultiplier;
            DealDamage(s, a, damage);
            if (d.Health.Raw <= 0) // урон через блок тоже может добить
            {
                Kill(s, a, ref d, knockDir * hit.KnockbackSpeed);
                return;
            }

            if (hit.GuardDamage.Raw > 0)
            {
                d.Stamina -= hit.GuardDamage;
                d.StaminaRegenDelay = dspec.StaminaRegenDelayTicks;
            }
            if (d.Stamina.Raw <= 0)
            {
                d.Stamina = Fix.Zero;
                Enter(ref d, ActionState.GuardBroken);
                d.StunTicks = Rules.GuardBreakStunTicks;
                d.Velocity = knockDir * hit.KnockbackSpeed;
                d.HitstopTicks = Rules.GuardBreakHitstopTicks;
                if (melee) f.HitstopTicks = Rules.GuardBreakHitstopTicks;
                Emit(s, SimEventType.GuardBreak, a, di, damage, Rules.GuardBreakStunTicks - attackerBusy, slot: slot, caster: caster, position: d.Position);
                return;
            }

            d.StunTicks = Math.Max(d.StunTicks, hit.BlockstunTicks);
            d.Velocity = knockDir * (hit.KnockbackSpeed * dspec.BlockKnockbackMultiplier);
            d.HitstopTicks = hit.HitstopTicks;
            if (melee) f.HitstopTicks = hit.HitstopTicks;
            Emit(s, SimEventType.Blocked, a, di, damage, hit.BlockstunTicks - attackerBusy, slot: slot, caster: caster, position: d.Position);
        }

        private Fix ComboDamageScale(int hitIndex)
        {
            if (hitIndex < Rules.ComboFullDamageHits) return Fix.One;
            var scale = Fix.One - Rules.ComboDamageScalePerHit * (hitIndex - Rules.ComboFullDamageHits + 1);
            return Fix.Max(Rules.ComboMinDamageScale, scale);
        }

        /// <summary> Урон цели противника a и мана обоим: нанёсшему и получившему. </summary>
        private void DealDamage(GameState s, int a, Fix amount)
        {
            ref var f = ref s.Fighters[a];
            ref var d = ref s.Fighters[1 - a];
            var aspec = Setup.Fighters[a];
            var dspec = Setup.Fighters[1 - a];
            TakeDamage(ref d, amount);
            f.Mana = Fix.Min(aspec.MaxMana, f.Mana + amount * aspec.ManaPerDamageDealt);
            if (d.IsAlive && d.Health.Raw > 0) d.Mana = Fix.Min(dspec.MaxMana, d.Mana + amount * dspec.ManaPerDamageTaken);
        }

        /// <summary> Сколько тиков бойцу до свободы (для подсчёта преимущества по кадрам). </summary>
        private int BusyTicks(GameState s, int i)
        {
            ref readonly var f = ref s.Fighters[i];
            var spec = Setup.Fighters[i];
            switch (f.State)
            {
                case ActionState.Attack:
                    return Math.Max(0, spec.Attack(f.Attack).TotalTicks - f.StateTicks);
                case ActionState.Cast:
                    var sk = spec.Skill((int)f.CastSlot);
                    return sk == null ? 0 : Math.Max(0, sk.TotalTicks - f.StateTicks);
            }
            return 0;
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
            d.ComboHits = 0;
            Emit(s, SimEventType.KO, killer, 1 - killer, Fix.Zero, 0, position: d.Position);
        }

        // ---------- Снаряды и области ----------

        private void StepObjects(GameState s)
        {
            var objects = s.Objects;
            for (int k = 0; k < objects.Length; k++)
            {
                switch (objects[k].Kind)
                {
                    case SkillObjectKind.Projectile:
                        StepProjectile(s, ref objects[k]);
                        break;
                    case SkillObjectKind.Zone:
                        if (--objects[k].TicksLeft <= 0) Detonate(s, ref objects[k]);
                        break;
                }
            }
        }

        /// <summary>
        /// Снаряд за тик проходит отрезок: задел цель — попадание / блок / отражение парированием / пролёт сквозь
        /// неуязвимость; задел препятствие или край арены — гаснет. Цель проверяется раньше препятствия.
        /// </summary>
        private void StepProjectile(GameState s, ref SkillObject o)
        {
            var sk = Setup.Fighters[o.Caster].Skill((int)o.Slot);
            if (sk == null)
            {
                o = default;
                return;
            }
            int caster = o.Caster;

            var from = o.Position;
            var to = from + o.Velocity;
            int ti = 1 - o.Owner;
            ref var d = ref s.Fighters[ti];
            var dspec = Setup.Fighters[ti];

            if (d.IsAlive && !o.Evaded && Collision.SegmentHitsCircle(from, to, d.Position, sk.ProjectileRadius + dspec.HurtRadius))
            {
                var knockDir = o.Velocity.Normalized;
                if (d.State == ActionState.Parry && sk.Reflectable)
                {
                    // Отражение: снаряд разворачивается и становится снарядом парирующего, тот сразу свободен.
                    int attacker = o.Owner;
                    o.Owner = ti;
                    o.Velocity = -o.Velocity;
                    o.TicksLeft = sk.ProjectileLifetimeTicks;
                    o.Evaded = false;
                    Enter(ref d, ActionState.Idle);
                    d.ParryCooldown = 0;
                    d.HitstopTicks = Rules.ReflectHitstopTicks;
                    Emit(s, SimEventType.ProjectileReflected, ti, attacker, Fix.Zero, 0, slot: (int)o.Slot, caster: caster, position: from);
                    return;
                }
                if (IsInvulnerable(d, dspec))
                {
                    o.Evaded = true;
                    Emit(s, SimEventType.Evaded, o.Owner, ti, Fix.Zero, 0, slot: (int)o.Slot, caster: caster, position: d.Position);
                }
                else
                {
                    int owner = o.Owner;
                    int slot = (int)o.Slot;
                    o = default;
                    if (d.State == ActionState.Block || d.State == ActionState.Parry)
                        LandBlock(s, owner, sk.Hit, knockDir, BusyTicks(s, owner), melee: false, slot: slot, caster: caster);
                    else
                        LandHit(s, owner, sk.Hit, knockDir, BusyTicks(s, owner), melee: false, slot: slot, caster: caster);
                    return;
                }
            }

            if (!Collision.InsideArena(to, Setup.Arena) || Collision.SegmentBlocked(from, to, Setup.Arena) || --o.TicksLeft <= 0)
            {
                Emit(s, SimEventType.ProjectileExpired, o.Owner, -1, Fix.Zero, 0, slot: (int)o.Slot, caster: caster, position: to);
                o = default;
                return;
            }
            o.Position = to;
        }

        /// <summary> Взрыв области: цель в радиусе получает удар; блок и парирование только блокируют, неуязвимость спасает. </summary>
        private void Detonate(GameState s, ref SkillObject o)
        {
            int owner = o.Owner;
            int slot = (int)o.Slot;
            var center = o.Position;
            var sk = Setup.Fighters[o.Caster].Skill(slot);
            o = default;
            Emit(s, SimEventType.ZoneDetonated, owner, -1, Fix.Zero, 0, slot: slot, position: center);
            if (sk == null) return;

            int ti = 1 - owner;
            ref var d = ref s.Fighters[ti];
            var dspec = Setup.Fighters[ti];
            if (!d.IsAlive || !Collision.CirclesOverlap(center, sk.ZoneRadius, d.Position, dspec.HurtRadius)) return;

            var knockDir = (d.Position - center).Normalized;
            if (knockDir.IsZero) knockDir = (d.Position - s.Fighters[owner].Position).Normalized;
            if (knockDir.IsZero) knockDir = s.Fighters[owner].Facing;

            if (IsInvulnerable(d, dspec))
                Emit(s, SimEventType.Evaded, owner, ti, Fix.Zero, 0, slot: slot, position: d.Position);
            else if (d.State == ActionState.Block || d.State == ActionState.Parry)
                LandBlock(s, owner, sk.Hit, knockDir, BusyTicks(s, owner), melee: false, slot: slot);
            else
                LandHit(s, owner, sk.Hit, knockDir, BusyTicks(s, owner), melee: false, slot: slot);
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
            Array.Clear(s.Objects, 0, s.Objects.Length);

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
                Mana = Fix.Min(spec.MaxMana, spec.StartMana),
            };
            for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
                f.SetCooldown(slot, spec.Skill(slot)?.InitialCooldownTicks ?? 0);
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

        private void Emit(GameState s, SimEventType type, int actor, int target, Fix amount, int advantage, int winner = -1,
                          int combo = 0, int slot = -1, int caster = -1, FixVec2 position = default)
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
                Combo = combo,
                Slot = slot,
                Caster = slot >= 0 && caster < 0 ? actor : caster,
                Position = position,
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
                Slot = SlotOf(cmd.Kind),
                Caster = SlotOf(cmd.Kind) >= 0 ? fighter : -1,
            });
        }
    }
}
