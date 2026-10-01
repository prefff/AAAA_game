using System.Linq;
using Game.Simulation;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Бот со скиллами: применяет каждый, продолжает попавший удар скиллом, бьёт с упреждением, уходит из областей и
    /// отвечает на снаряды. Скиллы — <see cref="TestSkills.Kit"/> (по одному каждого типа): бот выбирает их по типу.
    /// </summary>
    public class BotSkillTests
    {
        private static TickInput Bot(SimHarness h, int self, BotMode mode) => TrainingBot.Think(h.S, h.Sim.Setup, self, mode);

        [Test]
        public void Bots_UseEverySkill_InALongDuel()
        {
            // Арена боя, большой запас здоровья и тренировка (без таймера): видно, что делают боты за полторы минуты.
            var setup = new SimSetup();
            setup.Rules.Training = true;
            setup.Rules.CountdownTicks = 0;
            foreach (var f in setup.Fighters)
            {
                f.Skills = TestSkills.Kit();
                f.MaxHealth = Fix.FromInt(2000);
            }
            var h = new SimHarness(setup);
            for (int t = 0; t < SimTime.Seconds(90f); t++) h.Tick(Bot(h, 0, BotMode.Aggressive), Bot(h, 1, BotMode.Zoner));

            for (int self = 0; self < GameState.FighterCount; self++)
                for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
                    Assert.IsTrue(h.Events.Any(e => e.Type == SimEventType.SkillFired && e.Actor == self && e.Slot == slot),
                        $"Бот {self} ни разу не применил скилл слота {slot}");
            Assert.IsTrue(h.Events.Any(e => e.Type == SimEventType.Hit && e.Slot >= 0), "Скиллы должны попадать");
        }

        [Test]
        public void Aggressive_CancelsLandedHit_IntoSkill_ThatConnects()
        {
            var h = SkillSetup.Duel(1.5f);
            bool cancelled = false;
            for (int t = 0; t < 300 && !(cancelled && SkillHit(h)); t++)
            {
                var input = Bot(h, 1, BotMode.Aggressive);
                bool skill = false;
                for (int k = 0; k < input.Count; k++) skill |= FightSimulation.IsSkill(input[k].Kind);
                if (skill && h.P1.State == ActionState.Attack && h.P1.AttackConnected && h.P0.State == ActionState.Hitstun) cancelled = true;
                h.Tick(default, input);
            }
            Assert.IsTrue(cancelled, "Попавший удар должен отменяться в скилл, пока цель в hitstun");
            Assert.IsTrue(SkillHit(h), "Скилл из серии должен попасть");
        }

        private static bool SkillHit(SimHarness h) => h.Events.Any(e => e.Type == SimEventType.Hit && e.Actor == 1 && e.Slot >= 0);

        [Test]
        public void Zoner_LeadsARunningTarget()
        {
            // Цель бежит поперёк линии броска: снаряд в её текущую точку пролетел бы в паре метров позади.
            var h = SkillSetup.Duel(7f);
            for (int t = 0; t < 180 && !ProjectileHit(h); t++) h.Tick(SimHarness.Move(1f, 0f), Bot(h, 1, BotMode.Zoner));
            Assert.IsTrue(ProjectileHit(h), "Маг должен попадать нюком по бегущему с упреждением");
        }

        private static bool ProjectileHit(SimHarness h) =>
            h.Events.Any(e => e.Type == SimEventType.Hit && e.Actor == 1 && e.Slot >= 0 &&
                              h.Spec1.Skill(e.Slot).Kind == SkillKind.Projectile);

        [Test]
        public void Bot_GetsOutOfMostZones()
        {
            const int trials = 8;
            int hits = 0;
            for (int k = 0; k < trials; k++)
            {
                // Противник далеко, бот идёт к нему; область — у бота под ногами, чуть впереди: просто дойти не выйдет.
                var h = SkillSetup.Duel(12f);
                h.P0.Position = FixVec2.FromFloat(k - 3.5f, 0f);
                var toFoe = (h.P0.Position - h.P1.Position).Normalized;
                h.S.Objects[0] = new SkillObject
                {
                    Kind = SkillObjectKind.Zone,
                    Owner = 0,
                    Caster = 0,
                    Slot = SkillSlot.Ultimate,
                    Position = h.P1.Position + toFoe * Fix.Half,
                    TicksLeft = h.Spec0.Skill((int)SkillSlot.Ultimate).ZoneDelayTicks,
                };
                for (int t = 0; t < 60 && !h.Has(SimEventType.ZoneDetonated); t++) h.Tick(default, Bot(h, 1, BotMode.Aggressive));
                Assert.IsTrue(h.Has(SimEventType.ZoneDetonated));
                if (h.Events.Any(e => e.Type == SimEventType.Hit && e.Target == 1)) hits++;
            }
            Assert.LessOrEqual(hits, trials / 4, "Бот должен уходить из большинства областей (уклонением на взрыв)");
        }

        [Test]
        public void Bot_AnswersProjectiles_AndEveryAnswerSavesIt()
        {
            // Бот отвечает не на каждый снаряд (иначе нюк против него бесполезен), но ответ — вовремя.
            const int trials = 8;
            int answered = 0;
            for (int k = 0; k < trials; k++)
            {
                var h = SkillSetup.Duel(8.5f);
                h.P0.Position = FixVec2.FromFloat(k * 0.5f - 2f, 0f);
                h.Tick(SkillSetup.Cast(CommandKind.Skill1), Bot(h, 1, BotMode.Aggressive));
                bool Saved() => h.Events.Any(e => (e.Type == SimEventType.ProjectileReflected && e.Actor == 1) ||
                                                  (e.Type == SimEventType.Evaded && e.Target == 1));
                bool Hit() => h.Events.Any(e => e.Type == SimEventType.Hit && e.Target == 1 && e.Actor == 0);
                for (int t = 0; t < 90 && !Saved() && !Hit(); t++) h.Tick(default, Bot(h, 1, BotMode.Aggressive));
                bool saved = Saved();
                bool hit = Hit();
                Assert.IsFalse(saved && hit, $"Попытка {k}: ответ на снаряд не спас");
                if (saved) answered++;
            }
            Assert.GreaterOrEqual(answered, 2, "Бот должен отражать или пропускать сквозь уклонение часть снарядов");
            Assert.LessOrEqual(answered, trials - 1, "…но не все");
        }
    }
}
