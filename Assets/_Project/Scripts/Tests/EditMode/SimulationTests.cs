using System.Collections.Generic;
using System.Linq;
using Game.Simulation;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary> Обвязка для тестов симуляции: шаг за шагом, с накоплением событий. </summary>
    internal sealed class SimHarness
    {
        public readonly FightSimulation Sim;
        public readonly GameState S;
        public readonly List<SimEvent> Events = new();

        public SimHarness(SimSetup setup)
        {
            Sim = new FightSimulation(setup);
            S = Sim.CreateInitialState();
        }

        public ref FighterSim P0 => ref S.Fighters[0];
        public ref FighterSim P1 => ref S.Fighters[1];
        public FighterSpec Spec0 => Sim.Setup.Fighters[0];
        public FighterSpec Spec1 => Sim.Setup.Fighters[1];
        public MatchRules Rules => Sim.Setup.Rules;

        public void Tick(TickInput p0 = default, TickInput p1 = default)
        {
            Sim.Step(S, p0, p1);
            Events.AddRange(Sim.Events);
        }

        public void Ticks(int n)
        {
            for (int i = 0; i < n; i++) Tick();
        }

        /// <summary> Тикать без ввода, пока не выполнится условие; возвращает число тиков. </summary>
        public int TickUntil(System.Func<bool> condition, int limit = 300)
        {
            for (int i = 1; i <= limit; i++)
            {
                Tick();
                if (condition()) return i;
            }
            Assert.Fail($"Условие не выполнилось за {limit} тиков");
            return -1;
        }

        public bool Has(SimEventType type) => Events.Any(e => e.Type == type);

        public static TickInput Cmd(CommandKind kind, float x = 0f, float y = 0f, ushort id = 0)
        {
            var t = new TickInput();
            t.Add(SimCommand.Of(kind, x, y, id));
            return t;
        }

        public static TickInput Cmds(params CommandKind[] kinds)
        {
            var t = new TickInput();
            foreach (var k in kinds) t.Add(new SimCommand(k));
            return t;
        }

        public static TickInput Move(float x, float y)
        {
            var t = new TickInput();
            t.SetMove(x, y);
            return t;
        }

        /// <summary> Дуэль на открытой арене без отсчёта: P0 в (0,0), P1 в (0,distance), смотрят друг на друга. </summary>
        public static SimSetup Duel(float distance = 1.2f)
        {
            var setup = new SimSetup { Arena = ArenaSpec.Open(20f, 20f) };
            setup.Rules.CountdownTicks = 0;
            setup.Rules.Spawns = new[] { FixVec2.FromFloat(0f, 0f), FixVec2.FromFloat(0f, distance) };
            return setup;
        }
    }

    public class FixMathTests
    {
        [Test]
        public void Arithmetic_IsExactForBinaryFractions()
        {
            Assert.AreEqual(Fix.FromFloat(3f), Fix.FromFloat(1.5f) * Fix.FromInt(2));
            Assert.AreEqual(Fix.FromFloat(0.75f), Fix.FromInt(3) / Fix.FromInt(4));
            Assert.AreEqual(21845, (Fix.One / Fix.FromInt(3)).Raw);
            Assert.AreEqual(Fix.FromFloat(-2.5f), Fix.FromFloat(-5f) / 2);
        }

        [Test]
        public void Sqrt_And_Normalize()
        {
            Assert.AreEqual(Fix.FromInt(2), Fix.Sqrt(Fix.FromInt(4)));
            Assert.AreEqual(1.41421f, Fix.Sqrt(Fix.FromInt(2)).ToFloat(), 2e-5f);
            Assert.AreEqual(Fix.Zero, Fix.Sqrt(Fix.FromInt(-1)));

            var n = FixVec2.FromFloat(3f, 4f).Normalized;
            Assert.AreEqual(0.6f, n.X.ToFloat(), 1e-4f);
            Assert.AreEqual(0.8f, n.Y.ToFloat(), 1e-4f);
            Assert.IsTrue(FixVec2.Zero.Normalized.IsZero);
        }

        [Test]
        public void InputQuantization_RoundTripsUnitAxes()
        {
            Assert.AreEqual(127, TickInput.Quantize(1f));
            Assert.AreEqual(-127, TickInput.Quantize(-3f));
            Assert.AreEqual(Fix.One, TickInput.Dequantize(127, 0).X);
            Assert.AreEqual(-Fix.One, TickInput.Dequantize(0, -127).Y);
        }

        [Test]
        public void TickInput_HoldsUpToFourCommandsInOrder()
        {
            var t = new TickInput();
            Assert.IsTrue(t.Add(new SimCommand(CommandKind.LightAttack)));
            Assert.IsTrue(t.Add(new SimCommand(CommandKind.Dodge)));
            Assert.IsTrue(t.Add(new SimCommand(CommandKind.Parry)));
            Assert.IsTrue(t.Add(new SimCommand(CommandKind.BlockEnd)));
            Assert.IsFalse(t.Add(new SimCommand(CommandKind.HeavyAttack)));
            Assert.AreEqual(CommandKind.Dodge, t[1].Kind);
            Assert.IsTrue(t.Contains(CommandKind.BlockEnd));
        }
    }

    public class CollisionTests
    {
        [Test]
        public void Walls_And_Obstacles_StopMovement()
        {
            var setup = SimHarness.Duel(10f);
            setup.Arena = new ArenaSpec
            {
                Min = FixVec2.FromFloat(-4f, -4f),
                Max = FixVec2.FromFloat(4f, 20f),
                Obstacles = new[] { Obstacle.Box(FixVec2.FromFloat(3f, 0f), FixVec2.FromFloat(0.5f, 1f)) },
            };
            var h = new SimHarness(setup);
            float r = h.Spec0.BodyRadius.ToFloat();

            for (int i = 0; i < 120; i++) h.Tick(SimHarness.Move(1f, 0f));
            Assert.AreEqual(2.5f - r, h.P0.Position.X.ToFloat(), 1e-3f, "Прямоугольник не остановил бойца");

            for (int i = 0; i < 240; i++) h.Tick(SimHarness.Move(0f, -1f));
            Assert.AreEqual(-4f + r, h.P0.Position.Y.ToFloat(), 1e-3f, "Стена арены не остановила бойца");
        }

        [Test]
        public void CirclePillar_PushesOut()
        {
            var pos = Collision.PushOutOfCircle(FixVec2.FromFloat(0.5f, 0f), Fix.Half, FixVec2.Zero, Fix.One);
            Assert.AreEqual(1.5f, pos.X.ToFloat(), 1e-4f);
            Assert.AreEqual(0f, pos.Y.ToFloat(), 1e-4f);
        }

        [Test]
        public void BoxPushOut_AtTheFace_DoesNotDivideByZero()
        {
            // Точка в 1/65536 м от грани: длина вектора до грани округляется в 0.
            var face = FixVec2.FromFloat(1f, 0f);
            var pos = new FixVec2(face.X + Fix.FromRaw(1), face.Y);
            var pushed = Collision.PushOutOfBox(pos, Fix.Half, FixVec2.Zero, FixVec2.FromFloat(1f, 1f));
            Assert.AreEqual(1.5f, pushed.X.ToFloat(), 1e-3f);
            Assert.AreEqual(0f, pushed.Y.ToFloat(), 1e-3f);
        }

        [Test]
        public void Fighters_DoNotWalkThroughEachOther()
        {
            var h = new SimHarness(SimHarness.Duel(2f));
            var minDist = (h.Spec0.BodyRadius + h.Spec1.BodyRadius).ToFloat();
            for (int i = 0; i < 90; i++)
            {
                h.Tick(SimHarness.Move(0f, 1f));
                Assert.GreaterOrEqual((h.P1.Position - h.P0.Position).Magnitude.ToFloat(), minDist - 1e-3f);
            }
        }

        [Test]
        public void SegmentBlocked_ByProjectileBlockingObstacles()
        {
            var arena = ArenaSpec.Default();
            Assert.IsTrue(Collision.SegmentBlocked(FixVec2.FromFloat(0f, 0f), FixVec2.FromFloat(0f, 6f), arena), "Стенка в (0, 4.5)");
            Assert.IsTrue(Collision.SegmentBlocked(FixVec2.FromFloat(-9f, 0f), FixVec2.FromFloat(-4f, 0f), arena), "Колонна в (-6.5, 0)");
            Assert.IsFalse(Collision.SegmentBlocked(FixVec2.FromFloat(-3f, 0f), FixVec2.FromFloat(3f, 0f), arena));
        }
    }

    public class SimulationCombatTests
    {
        [Test]
        public void LightAttack_StartsInTheCommandTick_HitsAfterStartup_HitstunAndKnockback()
        {
            var h = new SimHarness(SimHarness.Duel());
            var light = h.Spec0.Light;
            var hp = h.P1.Health;

            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            Assert.AreEqual(ActionState.Attack, h.P0.State);
            Assert.AreEqual(AttackPhase.Startup, h.Sim.AttackPhaseOf(h.S, 0), "Удар должен начаться в тике команды");

            h.Ticks(light.StartupTicks - 1);
            Assert.AreEqual(hp, h.P1.Health, "Хитбокс вышел раньше startup");

            h.Tick();
            Assert.AreEqual(hp - light.Damage, h.P1.Health);
            Assert.AreEqual(ActionState.Hitstun, h.P1.State);
            Assert.IsTrue(h.Has(SimEventType.Hit));

            var z = h.P1.Position.Y;
            h.Ticks(10);
            Assert.Greater(h.P1.Position.Y.Raw, z.Raw, "Knockback не отбросил цель");
        }

        [Test]
        public void BlockedHit_KeepsBlock_ReducesDamage_ReleaseAfterBlockstun()
        {
            var h = new SimHarness(SimHarness.Duel());
            var light = h.Spec0.Light;
            h.Tick(default, SimHarness.Cmd(CommandKind.BlockStart));
            Assert.AreEqual(ActionState.Block, h.P1.State);

            var hp = h.P1.Health;
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.P1.Health != hp);

            Assert.AreEqual(ActionState.Block, h.P1.State, "Удар в блок не должен сбивать блок");
            Assert.AreEqual(hp - light.Damage * h.Spec1.BlockDamageMultiplier, h.P1.Health);
            Assert.Greater(h.P1.StunTicks, 0);

            // Отпускание во время блок-стана снимает блок, только когда стан закончится.
            h.Tick(default, SimHarness.Cmd(CommandKind.BlockEnd));
            Assert.AreEqual(ActionState.Block, h.P1.State);
            h.TickUntil(() => h.P1.State != ActionState.Block, 60);
            Assert.AreEqual(0, h.P1.StunTicks);
        }

        [Test]
        public void HeldBlock_ReturnsAfterHitstun()
        {
            var h = new SimHarness(SimHarness.Duel());
            // Блок зажат, но удар пришёл раньше: после оглушения боец сам встаёт в блок.
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.P1.State == ActionState.Hitstun);
            h.Tick(default, SimHarness.Cmd(CommandKind.BlockStart));
            Assert.AreEqual(ActionState.Hitstun, h.P1.State);
            h.TickUntil(() => h.P1.State != ActionState.Hitstun, 60);
            Assert.AreEqual(ActionState.Block, h.P1.State);
        }

        [Test]
        public void HeavyAttack_WithoutStamina_IsDroppedAfterBuffer()
        {
            var h = new SimHarness(SimHarness.Duel());
            h.P0.Stamina = Fix.One;
            h.Tick(SimHarness.Cmd(CommandKind.HeavyAttack));
            Assert.AreEqual(ActionState.Idle, h.P0.State);
            h.Ticks(h.Spec0.InputBufferTicks + 1);
            Assert.AreEqual(ActionState.Idle, h.P0.State);
            Assert.IsTrue(h.Events.Any(e => e.Type == SimEventType.CommandDropped && e.Command == CommandKind.HeavyAttack));
        }

        [Test]
        public void LightStartup_CancelsIntoDodge_MovingInTheSameTick()
        {
            var h = new SimHarness(SimHarness.Duel(5f));
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            var x = h.P0.Position.X;
            h.Tick(SimHarness.Cmd(CommandKind.Dodge, 1f, 0f));
            Assert.AreEqual(ActionState.Dodge, h.P0.State);
            Assert.Greater(h.P0.Position.X.Raw, x.Raw, "Уклонение должно сдвинуть бойца в тике команды, в сторону свайпа");
        }

        [Test]
        public void TapAndSwipeInOneTick_EndInDodge()
        {
            var h = new SimHarness(SimHarness.Duel(5f));
            var t = new TickInput();
            t.Add(new SimCommand(CommandKind.LightAttack));
            t.Add(SimCommand.Of(CommandKind.Dodge, -1f, 0f));
            h.Tick(t);
            Assert.AreEqual(ActionState.Dodge, h.P0.State);
        }

        [Test]
        public void LightStartup_CancelsIntoBlock()
        {
            var h = new SimHarness(SimHarness.Duel(5f));
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.Tick(SimHarness.Cmd(CommandKind.BlockStart));
            Assert.AreEqual(ActionState.Block, h.P0.State);
        }

        [Test]
        public void SecondFinger_TurnsLightIntoHeavy_PaysOnlyWhenHitboxComesOut()
        {
            var h = new SimHarness(SimHarness.Duel(5f));
            var max = h.P0.Stamina;
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.Tick(SimHarness.Cmd(CommandKind.HeavyAttack));
            Assert.AreEqual(AttackKind.Heavy, h.P0.Attack);
            Assert.AreEqual(max, h.P0.Stamina, "В startup стамина ещё не списана");

            h.TickUntil(() => h.Sim.AttackPhaseOf(h.S, 0) == AttackPhase.Active);
            Assert.AreEqual(max - h.Spec0.Heavy.StaminaCost, h.P0.Stamina);
        }

        [Test]
        public void HeavyCancelledInStartup_CostsOnlyTheDodge()
        {
            var h = new SimHarness(SimHarness.Duel(5f));
            var max = h.P0.Stamina;
            h.Tick(SimHarness.Cmd(CommandKind.HeavyAttack));
            h.Tick(SimHarness.Cmd(CommandKind.Dodge, -1f, 0f));
            Assert.AreEqual(ActionState.Dodge, h.P0.State);
            Assert.AreEqual(max - h.Spec0.DodgeStaminaCost, h.P0.Stamina);
        }

        [Test]
        public void FlickAfterDodgeStart_CancelsIntoParry_AndRefundsDodge()
        {
            var h = new SimHarness(SimHarness.Duel(5f));
            var max = h.P0.Stamina;
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.Tick(SimHarness.Cmd(CommandKind.Dodge, 1f, 0f));
            h.Ticks(3);
            h.Tick(SimHarness.Cmd(CommandKind.Parry));
            Assert.AreEqual(ActionState.Parry, h.P0.State);
            Assert.AreEqual(max, h.P0.Stamina);
        }

        [Test]
        public void BufferedAttack_StartsOnTheTickRecoveryEnds()
        {
            var h = new SimHarness(SimHarness.Duel(5f)); // вне досягаемости: без попадания и комбо-отмены
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.Sim.AttackPhaseOf(h.S, 0) == AttackPhase.Recovery);
            h.Ticks(h.Spec0.Light.RecoveryTicks - h.Spec0.InputBufferTicks); // осталось меньше окна буфера

            h.Tick(SimHarness.Cmd(CommandKind.LightAttack, id: 7));
            Assert.Greater(h.P0.BufferTicks, 0, "В recovery удар запрещён — должен уйти в буфер");

            bool sawIdle = false;
            h.TickUntil(() =>
            {
                sawIdle |= h.P0.State == ActionState.Idle;
                return h.P0.State == ActionState.Attack && h.P0.StateTicks == 0;
            }, 30);
            Assert.IsFalse(sawIdle, "Между ударами был тик Idle — буфер опоздал");
            Assert.IsTrue(h.Events.Any(e => e.Type == SimEventType.CommandAccepted && e.CommandId == 7));
        }

        [Test]
        public void ExpiredBufferedCommand_IsDropped()
        {
            var h = new SimHarness(SimHarness.Duel(5f));
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.Sim.AttackPhaseOf(h.S, 0) == AttackPhase.Active);
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack, id: 9)); // recovery дольше окна буфера
            h.TickUntil(() => h.P0.State == ActionState.Idle, 40);
            h.Tick();
            Assert.AreEqual(ActionState.Idle, h.P0.State, "Просроченная команда не должна выполниться");
            Assert.IsTrue(h.Events.Any(e => e.Type == SimEventType.CommandDropped && e.CommandId == 9));
        }

        [Test]
        public void Parry_StunsAttacker_DefenderGetsGuaranteedCounter()
        {
            var h = new SimHarness(SimHarness.Duel());
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack), SimHarness.Cmd(CommandKind.Parry));
            h.TickUntil(() => h.Has(SimEventType.Parried), 20);

            var parried = h.Events.First(e => e.Type == SimEventType.Parried);
            Assert.AreEqual(1, parried.Actor);
            Assert.AreEqual(h.Rules.ParryStunTicks, parried.FrameAdvantage);
            Assert.AreEqual(ActionState.ParryStunned, h.P0.State);
            Assert.AreEqual(ActionState.Idle, h.P1.State);
            Assert.AreEqual(h.Rules.ParryHitstopTicks, h.P0.HitstopTicks);
            Assert.AreEqual(h.P0.Health, h.Spec0.MaxHealth);

            // Контратака вводится во время стоп-кадра (уходит в буфер) и успевает раньше, чем атакующий очнётся.
            h.Tick(default, SimHarness.Cmd(CommandKind.LightAttack));
            bool attackerActed = false;
            h.TickUntil(() =>
            {
                attackerActed |= h.P0.State == ActionState.Idle;
                return h.P0.Health < h.Spec0.MaxHealth;
            }, 60);
            Assert.IsFalse(attackerActed, "Оглушённый после парирования успел действовать до контратаки");
        }

        [Test]
        public void ParryWhiff_LeavesShortRecovery()
        {
            var h = new SimHarness(SimHarness.Duel(5f));
            h.Tick(SimHarness.Cmd(CommandKind.Parry));
            h.Ticks(h.Spec0.ParryWindowTicks);
            Assert.AreEqual(ActionState.ParryRecovery, h.P0.State);
            h.Ticks(h.Spec0.ParryWhiffRecoveryTicks);
            Assert.AreEqual(ActionState.Idle, h.P0.State);
        }

        [Test]
        public void DodgeIFrames_AttackPassesThrough()
        {
            var h = new SimHarness(SimHarness.Duel());
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.Ticks(h.Spec0.Light.StartupTicks - 2);
            h.Tick(default, SimHarness.Cmd(CommandKind.Dodge, 1f, 0f));
            h.Ticks(h.Spec0.Light.ActiveTicks + 1);
            Assert.IsTrue(h.Has(SimEventType.Evaded));
            Assert.AreEqual(h.Spec1.MaxHealth, h.P1.Health);
        }

        [Test]
        public void LightHit_CancelsIntoNextLight_TrueCombo()
        {
            var h = new SimHarness(SimHarness.Duel());
            var dmg = h.Spec0.Light.Damage;
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.Has(SimEventType.Hit), 20);
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack)); // в стоп-кадре → буфер → отмена после него

            bool escaped = false;
            h.TickUntil(() =>
            {
                escaped |= h.P1.State != ActionState.Hitstun;
                return h.Events.Count(e => e.Type == SimEventType.Hit) == 2;
            }, 40);
            Assert.AreEqual(h.Spec1.MaxHealth - dmg - dmg, h.P1.Health);
            Assert.IsFalse(escaped, "Второй удар должен попасть, пока цель ещё в hitstun");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FrameAdvantage_MatchesWhoActsFirst(bool blocked)
        {
            var h = new SimHarness(SimHarness.Duel());
            if (blocked) h.Tick(default, SimHarness.Cmd(CommandKind.BlockStart));
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            var type = blocked ? SimEventType.Blocked : SimEventType.Hit;
            h.TickUntil(() => h.Has(type), 20);
            int advantage = h.Events.First(e => e.Type == type).FrameAdvantage;

            int t = 0, attackerFree = -1, defenderFree = -1;
            while ((attackerFree < 0 || defenderFree < 0) && t < 100)
            {
                h.Tick();
                t++;
                if (attackerFree < 0 && h.P0.State == ActionState.Idle) attackerFree = t;
                bool defenderOk = blocked ? h.P1.StunTicks == 0 : h.P1.State == ActionState.Idle;
                if (defenderFree < 0 && defenderOk) defenderFree = t;
            }
            Assert.AreEqual(advantage, defenderFree - attackerFree);
        }

        [Test]
        public void Attack_LocksOntoOpponentInRadius_OtherwiseForward()
        {
            var near = new SimHarness(SimHarness.Duel(2f));
            near.P0.Facing = FixVec2.Right;
            near.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            Assert.AreEqual(FixVec2.Forward, near.P0.Facing);

            var far = new SimHarness(SimHarness.Duel(6f));
            far.P0.Facing = FixVec2.Right;
            far.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            Assert.AreEqual(FixVec2.Right, far.P0.Facing);
        }

        [Test]
        public void AcceptedCommand_ReportsItsId()
        {
            var h = new SimHarness(SimHarness.Duel());
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack, id: 42));
            Assert.IsTrue(h.Events.Any(e => e.Type == SimEventType.CommandAccepted && e.CommandId == 42 && e.Actor == 0));
        }
    }

    public class MatchFlowTests
    {
        private static SimHarness OneHitKo(int roundOverTicks = 5)
        {
            var setup = SimHarness.Duel();
            setup.Fighters[1].MaxHealth = Fix.FromInt(5);
            setup.Rules.RoundOverTicks = roundOverTicks;
            return new SimHarness(setup);
        }

        [Test]
        public void TwoKOs_WinTheMatch_RestartResets()
        {
            var h = OneHitKo();
            for (int round = 1; round <= 2; round++)
            {
                Assert.AreEqual(round, h.S.Round);
                Assert.AreEqual(MatchPhase.Fight, h.S.Phase);
                h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
                h.TickUntil(() => h.S.Phase != MatchPhase.Fight, 20);
                Assert.AreEqual(ActionState.Dead, h.P1.State);
                Assert.AreEqual(round, h.S.Wins0);
                h.Ticks(h.Rules.RoundOverTicks);
            }

            Assert.AreEqual(MatchPhase.MatchOver, h.S.Phase);
            Assert.AreEqual(0, h.S.MatchWinner);
            Assert.IsTrue(h.Has(SimEventType.MatchEnded));

            h.Tick(SimHarness.Cmd(CommandKind.Restart));
            Assert.AreEqual(1, h.S.Round);
            Assert.AreEqual(0, h.S.Wins0);
            Assert.AreEqual(MatchPhase.Fight, h.S.Phase);
            Assert.AreEqual(h.Spec1.MaxHealth, h.P1.Health);
        }

        [Test]
        public void NewRound_ResetsFightersToSpawns()
        {
            var h = OneHitKo();
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.S.Phase == MatchPhase.RoundOver, 20);
            h.Ticks(h.Rules.RoundOverTicks);
            Assert.AreEqual(2, h.S.Round);
            Assert.AreEqual(h.Rules.Spawns[1], h.P1.Position);
            Assert.AreEqual(ActionState.Idle, h.P1.State);
            Assert.AreEqual(h.Spec1.MaxHealth, h.P1.Health);
        }

        [TestCase(true, 0)]
        [TestCase(false, -1)]
        public void Timeout_HigherHealthPercentWins_EqualIsDraw(bool hit, int expectedWinner)
        {
            var setup = SimHarness.Duel();
            setup.Rules.RoundTicks = 60;
            var h = new SimHarness(setup);
            if (hit) h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.S.Phase == MatchPhase.RoundOver, 70);
            Assert.AreEqual(expectedWinner, h.S.LastRoundWinner);
            Assert.AreEqual(hit ? 1 : 0, h.S.Wins0);
            Assert.AreEqual(0, h.S.Wins1);
        }

        [Test]
        public void Draws_EndMatchAtMaxRounds()
        {
            var setup = SimHarness.Duel();
            setup.Rules.RoundTicks = 2;
            setup.Rules.RoundOverTicks = 1;
            setup.Rules.MaxRounds = 3;
            var h = new SimHarness(setup);
            h.TickUntil(() => h.S.Phase == MatchPhase.MatchOver, 50);
            Assert.AreEqual(3, h.S.Round);
            Assert.AreEqual(-1, h.S.MatchWinner);
        }

        [Test]
        public void Training_KO_RestartsRoundWithoutScore()
        {
            var h = OneHitKo();
            h.Rules.Training = true;
            for (int i = 0; i < 3; i++)
            {
                h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
                h.TickUntil(() => h.S.Phase == MatchPhase.RoundOver, 20);
                h.Ticks(h.Rules.RoundOverTicks);
            }
            Assert.AreEqual(MatchPhase.Fight, h.S.Phase);
            Assert.AreEqual(0, h.S.Wins0);
        }

        [Test]
        public void Countdown_IgnoresCommands_ThenStartsTheRound()
        {
            var setup = SimHarness.Duel();
            setup.Rules.CountdownTicks = 5;
            var h = new SimHarness(setup);
            Assert.AreEqual(MatchPhase.Countdown, h.S.Phase);

            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            Assert.AreEqual(ActionState.Idle, h.P0.State);
            Assert.IsTrue(h.Has(SimEventType.CommandDropped));

            h.Ticks(4);
            Assert.AreEqual(MatchPhase.Fight, h.S.Phase);
            Assert.IsTrue(h.Has(SimEventType.RoundStarted));
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            Assert.AreEqual(ActionState.Attack, h.P0.State);
        }
    }

    public class DeterminismTests
    {
        private static readonly CommandKind[] Kinds =
        {
            CommandKind.LightAttack, CommandKind.HeavyAttack, CommandKind.BlockStart, CommandKind.BlockEnd,
            CommandKind.Parry, CommandKind.Dodge,
        };

        /// <summary> Псевдослучайный, но воспроизводимый ввод (System.Random с сидом — только в тесте). </summary>
        private static List<TickInput> RandomInputs(int seed, int count)
        {
            var rnd = new System.Random(seed);
            var list = new List<TickInput>(count);
            for (int i = 0; i < count; i++)
            {
                var t = new TickInput();
                if (rnd.NextDouble() < 0.7) t.SetMove((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1));
                if (rnd.NextDouble() < 0.15)
                    t.Add(SimCommand.Of(Kinds[rnd.Next(Kinds.Length)], (float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1)));
                list.Add(t);
            }
            return list;
        }

        private static SimSetup ShortRounds()
        {
            var setup = new SimSetup();
            setup.Rules.RoundTicks = 900;
            setup.Rules.CountdownTicks = 10;
            setup.Rules.RoundOverTicks = 10;
            return setup;
        }

        [Test]
        public void SyncTest_TwoCopiesWithSameInputs_MatchEveryTick()
        {
            const int ticks = 5000;
            var p0 = RandomInputs(1, ticks);
            var p1 = RandomInputs(2, ticks);
            var a = new FightSimulation(ShortRounds());
            var b = new FightSimulation(ShortRounds());
            var sa = a.CreateInitialState();
            var sb = b.CreateInitialState();

            int hits = 0;
            for (int i = 0; i < ticks; i++)
            {
                // Вторая половина — боты с обеих сторон: их решения тоже должны совпадать.
                var in0 = i < ticks / 2 ? p0[i] : TrainingBot.Think(sa, a.Setup, 0, BotMode.Aggressive);
                var in1 = i < ticks / 2 ? p1[i] : TrainingBot.Think(sa, a.Setup, 1, BotMode.Aggressive);
                if (sa.Phase == MatchPhase.MatchOver) in0.Add(new SimCommand(CommandKind.Restart));
                a.Step(sa, in0, in1);
                b.Step(sb, in0, in1);
                hits += a.Events.Count(e => e.Type == SimEventType.Hit || e.Type == SimEventType.Blocked);
                Assert.AreEqual(sa.ComputeHash(), sb.ComputeHash(), $"Рассинхрон на тике {sa.Tick}");
            }
            Assert.Greater(hits, 10, "Тест должен включать настоящий бой, а не только ходьбу");
        }

        [Test]
        public void DifferentInput_ChangesHash()
        {
            var sim = new FightSimulation(SimHarness.Duel());
            var a = sim.CreateInitialState();
            var b = a.Clone();
            sim.Step(a, SimHarness.Move(1f, 0f), default);
            sim.Step(b, SimHarness.Move(0f, 1f), default);
            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void Rollback_RestoreAndResimulate_GivesTheSameResult()
        {
            const int ticks = 600, rollback = 12;
            var p0 = RandomInputs(3, ticks);
            var p1 = RandomInputs(4, ticks);
            var sim = new FightSimulation(ShortRounds());
            var s = sim.CreateInitialState();
            var history = new StateHistory(32);
            var hashes = new Dictionary<int, ulong>();
            int startTick = s.Tick;
            history.Save(s);

            for (int i = 0; i < ticks; i++)
            {
                sim.Step(s, p0[i], p1[i]);
                history.Save(s);
                hashes[s.Tick] = s.ComputeHash();

                if (i < rollback || i % 37 != 0) continue;
                // Откат на N тиков назад и пересчёт с теми же вводами должен прийти к тому же состоянию.
                var replay = new GameState();
                Assert.IsTrue(history.TryLoad(s.Tick - rollback, replay));
                for (int k = i - rollback + 1; k <= i; k++)
                {
                    sim.Step(replay, p0[k], p1[k]);
                    Assert.AreEqual(hashes[replay.Tick], replay.ComputeHash(), $"Пересчёт разошёлся на тике {replay.Tick}");
                }
            }
            Assert.AreEqual(startTick + ticks, s.Tick);
        }

        [Test]
        public void Recording_ReplaysToTheSameState()
        {
            const int ticks = 1500;
            var p0 = RandomInputs(5, ticks);
            var p1 = RandomInputs(6, ticks);
            var sim = new FightSimulation(ShortRounds());
            var s = sim.CreateInitialState();
            var rec = new InputRecording(s);
            for (int i = 0; i < ticks; i++)
            {
                rec.Add(p0[i], p1[i]);
                sim.Step(s, p0[i], p1[i]);
            }
            var replayed = rec.Play(new FightSimulation(ShortRounds()));
            Assert.AreEqual(s.ComputeHash(), replayed.ComputeHash());
        }

        [Test]
        public void Snapshot_IsIndependentCopy()
        {
            var sim = new FightSimulation(SimHarness.Duel());
            var s = sim.CreateInitialState();
            var snap = s.Clone();
            sim.Step(s, SimHarness.Cmd(CommandKind.LightAttack), default);
            Assert.AreEqual(ActionState.Idle, snap.Fighters[0].State, "Снимок не должен меняться вместе с состоянием");
            Assert.AreNotEqual(snap.ComputeHash(), s.ComputeHash());
        }
    }

    public class TrainingBotTests
    {
        [Test]
        public void BlockBot_HoldsBlock()
        {
            var h = new SimHarness(SimHarness.Duel());
            h.Tick(default, TrainingBot.Think(h.S, h.Sim.Setup, 1, BotMode.Block));
            Assert.AreEqual(ActionState.Block, h.P1.State);
            h.Tick(default, TrainingBot.Think(h.S, h.Sim.Setup, 1, BotMode.Block));
            Assert.AreEqual(ActionState.Block, h.P1.State);
        }

        [Test]
        public void AttackBot_ApproachesAndHits()
        {
            var h = new SimHarness(SimHarness.Duel(5f));
            for (int i = 0; i < 300 && h.P0.Health == h.Spec0.MaxHealth; i++)
                h.Tick(default, TrainingBot.Think(h.S, h.Sim.Setup, 1, BotMode.Attack));
            Assert.Less(h.P0.Health.Raw, h.Spec0.MaxHealth.Raw, "Бот должен подойти и ударить");
        }

        [Test]
        public void IdleBot_DoesNothing()
        {
            var h = new SimHarness(SimHarness.Duel());
            var input = TrainingBot.Think(h.S, h.Sim.Setup, 1, BotMode.Idle);
            Assert.AreEqual(0, input.Count);
            Assert.AreEqual(0, input.MoveX);
        }
    }
}
