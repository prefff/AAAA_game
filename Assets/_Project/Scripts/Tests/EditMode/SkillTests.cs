using System.Collections.Generic;
using System.Linq;
using Game.Input;
using Game.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// Скиллы для тестов механик — по одному каждого типа. Нарочно не из ассетов: тюнинг персонажей не должен ломать
    /// тесты механик. Данные настоящих персонажей проверяет <see cref="FighterDataTests"/>.
    /// </summary>
    internal static class TestSkills
    {
        /// <summary> Снаряд средней дальности. </summary>
        public static SkillSpec Bolt() => new() { Name = "Bolt" };

        /// <summary> Телепорт: короткий startup с неуязвимостью. </summary>
        public static SkillSpec Blink() => new()
        {
            Name = "Blink",
            Kind = SkillKind.Blink,
            StartupTicks = SimTime.Frames(4),
            RecoveryTicks = SimTime.Frames(10),
            InvulnerableTicks = SimTime.Frames(8),
            ManaCost = Fix.FromInt(25),
            CooldownTicks = SimTime.Seconds(6f),
            Range = Fix.FromFloat(4.5f),
            AutoAim = false,
            Damage = Fix.Zero,
        };

        /// <summary> Ультимейт: область в точке, взрыв через полсекунды. </summary>
        public static SkillSpec Nova() => new()
        {
            Name = "Nova",
            Kind = SkillKind.Zone,
            StartupTicks = SimTime.Frames(18),
            RecoveryTicks = SimTime.Frames(24),
            ManaCost = Fix.FromInt(50),
            CooldownTicks = SimTime.Seconds(20f),
            InitialCooldownTicks = SimTime.Seconds(12f),
            Range = Fix.FromInt(7),
            ZoneRadius = Fix.FromFloat(2.5f),
            ZoneDelayTicks = SimTime.Frames(30),
            Damage = Fix.FromInt(28),
            KnockbackSpeed = SimTime.PerSecond(9f),
            HitstunTicks = SimTime.Frames(45),
            BlockstunTicks = SimTime.Frames(20),
            HitstopTicks = SimTime.Frames(8),
            GuardDamage = Fix.FromInt(40),
        };

        /// <summary> Слоты: снаряд, телепорт, ультимейт-область. </summary>
        public static SkillSpec[] Kit() => new[] { Bolt(), Blink(), Nova() };
    }

    internal static class SkillSetup
    {
        /// <summary> Дуэль, где у обоих <see cref="TestSkills.Kit"/>, полная мана и все скиллы готовы. </summary>
        public static SimHarness Duel(float distance = 5f, SimSetup setup = null)
        {
            setup ??= SimHarness.Duel(distance);
            foreach (var f in setup.Fighters)
            {
                f.Skills = TestSkills.Kit();
                f.StartMana = f.MaxMana;
            }
            var h = new SimHarness(setup);
            for (int i = 0; i < GameState.FighterCount; i++)
                for (int slot = 0; slot < FighterSpec.SkillSlots; slot++) h.S.Fighters[i].SetCooldown(slot, 0);
            return h;
        }

        public static TickInput Cast(CommandKind kind, float aimX = 0f, float aimY = 0f) => SimHarness.Cmd(kind, aimX, aimY);

        /// <summary> Нажатие кнопки пальцем: прицел придёт непрерывным вводом. </summary>
        public static TickInput Press(CommandKind kind, AimState aim, float aimX = 0f, float aimY = 0f)
        {
            var t = new TickInput();
            t.Add(SimCommand.SkillWithInputAim(kind));
            t.SetAim(aim, aimX, aimY);
            return t;
        }

        public static TickInput Aim(AimState aim, float x = 0f, float y = 0f)
        {
            var t = new TickInput();
            t.SetAim(aim, x, y);
            return t;
        }

        public static SkillObject? FirstObject(GameState s, SkillObjectKind kind)
        {
            foreach (var o in s.Objects)
                if (o.Kind == kind) return o;
            return null;
        }
    }

    public class SkillCastTests
    {
        [Test]
        public void Skill_StartsInCommandTick_FiresAfterStartup_PaysOnlyOnFire()
        {
            var h = SkillSetup.Duel();
            var bolt = h.Spec0.Skill(0);
            var mana = h.P0.Mana;

            h.Tick(SkillSetup.Cast(CommandKind.Skill1));
            Assert.AreEqual(ActionState.Cast, h.P0.State, "Каст должен начаться в тике команды");
            Assert.AreEqual(mana, h.P0.Mana, "Мана списывается при выходе скилла, а не при нажатии");
            Assert.AreEqual(0, h.P0.Cooldown0);

            h.Ticks(bolt.StartupTicks - 1);
            Assert.IsFalse(h.P0.SkillFired);
            h.Tick();
            Assert.IsTrue(h.P0.SkillFired);
            Assert.IsTrue(h.Has(SimEventType.SkillFired));
            Assert.Less(h.P0.Mana.Raw, mana.Raw);
            Assert.AreEqual(bolt.CooldownTicks, h.P0.Cooldown0);
            Assert.IsNotNull(SkillSetup.FirstObject(h.S, SkillObjectKind.Projectile));

            h.TickUntil(() => h.P0.State != ActionState.Cast, 40);
            Assert.AreEqual(ActionState.Idle, h.P0.State);
        }

        [Test]
        public void CastStartup_CancelsIntoDodge_ForFree()
        {
            var h = SkillSetup.Duel();
            var mana = h.P0.Mana;
            h.Tick(SkillSetup.Cast(CommandKind.Skill1));
            h.Tick(SimHarness.Cmd(CommandKind.Dodge, 1f, 0f));
            Assert.AreEqual(ActionState.Dodge, h.P0.State);
            h.Ticks(30);
            Assert.AreEqual(0, h.P0.Cooldown0, "Отменённый скилл не уходит на перезарядку");
            Assert.GreaterOrEqual(h.P0.Mana.Raw, mana.Raw);
            Assert.IsNull(SkillSetup.FirstObject(h.S, SkillObjectKind.Projectile));
        }

        [Test]
        public void SkillCancel_StopsCastBeforeItFires()
        {
            var h = SkillSetup.Duel();
            h.Tick(SkillSetup.Cast(CommandKind.Ultimate));
            h.Ticks(3);
            h.Tick(SimHarness.Cmd(CommandKind.SkillCancel));
            Assert.AreNotEqual(ActionState.Cast, h.P0.State);
            Assert.AreEqual(0, h.P0.Cooldown2);
            Assert.IsNull(SkillSetup.FirstObject(h.S, SkillObjectKind.Zone));
        }

        [Test]
        public void HeldAim_DoesNotCast_FighterMovesFreely_ForAsLongAsItIsHeld()
        {
            var h = SkillSetup.Duel();
            var mana = h.P0.Mana;
            var start = h.P0.Position;
            for (int i = 0; i < SimTime.Seconds(5f); i++)
            {
                var t = SkillSetup.Aim(AimState.Held, 0f, -1f);
                t.SetMove(0f, 1f);
                h.Tick(t);
                Assert.AreNotEqual(ActionState.Cast, h.P0.State, "Прицел — не каст");
                Assert.IsFalse(h.Has(SimEventType.SkillFired), "Скилл не выходит сам, сколько ни держи");
            }
            Assert.Greater((h.P0.Position - start).Y.Raw, 0, "Пока палец держит прицел, боец бегает");
            Assert.AreEqual(mana, h.P0.Mana);
            Assert.AreEqual(0, h.P0.Cooldown0);
            Assert.AreEqual(-1f, h.P0.SkillAim.Y.ToFloat(), 1e-2f, "Прицел запоминается");
        }

        [Test]
        public void Release_CastsInThatTick_WithFinalAim()
        {
            var h = SkillSetup.Duel();
            var bolt = h.Spec0.Skill(0);
            for (int i = 0; i < 20; i++) h.Tick(SkillSetup.Aim(AimState.Held, 0f, -1f));
            h.Tick(SkillSetup.Press(CommandKind.Skill1, AimState.Released, 1f, 0f));
            Assert.AreEqual(ActionState.Cast, h.P0.State, "Каст начинается в тике отпускания");
            Assert.AreEqual(1f, h.P0.Facing.X.ToFloat(), 1e-2f, "Боец разворачивается по последнему прицелу");

            h.Ticks(bolt.StartupTicks);
            Assert.IsTrue(h.P0.SkillFired);
            var p = SkillSetup.FirstObject(h.S, SkillObjectKind.Projectile).Value;
            Assert.Greater(p.Velocity.X.Raw, 0, "Снаряд летит по последнему прицелу");
        }

        [Test]
        public void Cast_DoesNotTakeAwayMovement()
        {
            var h = SkillSetup.Duel(10f);
            var bolt = h.Spec0.Skill(0);
            var t = SkillSetup.Cast(CommandKind.Skill1, 0f, 1f);
            t.SetMove(1f, 0f);
            h.Tick(t);
            var before = h.P0.Position;
            int ticks = 0;
            while (h.P0.State == ActionState.Cast && ticks < 100)
            {
                h.Tick(SimHarness.Move(1f, 0f));
                ticks++;
                if (h.P0.State == ActionState.Cast)
                    Assert.AreEqual(1f, h.P0.Facing.Y.ToFloat(), 1e-2f, "В касте боец смотрит по прицелу, а не по бегу");
            }
            Assert.AreEqual(bolt.TotalTicks, ticks);
            Assert.AreEqual(h.Spec0.MoveSpeed.ToFloat() * ticks, (h.P0.Position - before).X.ToFloat(), 1e-2f,
                "Каст не тормозит бег ни на тик");
        }

        [Test]
        public void PlayerPress_UsesInputAim_BotCommand_UsesItsOwnAim()
        {
            var h = SkillSetup.Duel();
            // Палец уже утащил прицел вправо; команда нажатия прицела не несёт.
            h.Tick(SkillSetup.Press(CommandKind.Skill1, AimState.Released, 1f, 0f));
            h.TickUntil(() => h.P0.SkillFired, 30);
            Assert.Greater(SkillSetup.FirstObject(h.S, SkillObjectKind.Projectile).Value.Velocity.X.Raw, 0);

            var b = SkillSetup.Duel();
            b.Tick(SkillSetup.Cast(CommandKind.Skill1, -1f, 0f));
            b.TickUntil(() => b.P0.SkillFired, 30);
            Assert.Less(SkillSetup.FirstObject(b.S, SkillObjectKind.Projectile).Value.Velocity.X.Raw, 0);
        }

        [Test]
        public void UltimateLocked_AtRoundStart_AndWithoutMana()
        {
            var setup = SimHarness.Duel(5f);
            foreach (var f in setup.Fighters) f.Skills = TestSkills.Kit();
            var h = new SimHarness(setup);
            Assert.AreEqual(h.Spec0.Skill(2).InitialCooldownTicks, h.P0.Cooldown2, "Ультимейт открывается не сразу");
            h.Tick(SkillSetup.Cast(CommandKind.Ultimate));
            Assert.AreNotEqual(ActionState.Cast, h.P0.State);

            var poor = SkillSetup.Duel();
            poor.P0.Mana = Fix.FromInt(5);
            poor.Tick(SkillSetup.Cast(CommandKind.Skill1));
            Assert.AreNotEqual(ActionState.Cast, poor.P0.State, "Без маны каст не начинается");
            Assert.IsFalse(poor.Sim.CanCast(poor.S, 0, 0));
        }

        [Test]
        public void UnimplementedSkillKind_Throws_InsteadOfBecomingAnotherSkill()
        {
            var h = SkillSetup.Duel();
            h.Spec0.Skills[0].Kind = (SkillKind)250;
            h.Spec0.Skills[0].StartupTicks = 0;
            Assert.Throws<System.InvalidOperationException>(() => h.Tick(SkillSetup.Cast(CommandKind.Skill1)));
        }

        [Test]
        public void LightHit_CancelsIntoSkill()
        {
            var h = SkillSetup.Duel(1.2f);
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.Has(SimEventType.Hit), 20);
            h.Tick(SkillSetup.Cast(CommandKind.Skill1)); // стоп-кадр → буфер → отмена после него
            h.TickUntil(() => h.P0.State == ActionState.Cast, 10);
            Assert.AreEqual(ActionState.Hitstun, h.P1.State, "Отмена в скилл — пока цель в hitstun");
        }
    }

    public class ProjectileTests
    {
        [Test]
        public void Bolt_HitsAtRange_WithItsHitData()
        {
            var h = SkillSetup.Duel(6f);
            var bolt = h.Spec0.Skill(0);
            h.Tick(SkillSetup.Cast(CommandKind.Skill1)); // автоприцел: противник в пределах дальности
            h.TickUntil(() => h.Has(SimEventType.Hit), 80);
            var hit = h.Events.First(e => e.Type == SimEventType.Hit);
            Assert.AreEqual(0, hit.Slot);
            Assert.AreEqual(h.Spec1.MaxHealth - bolt.Damage, h.P1.Health);
            Assert.AreEqual(ActionState.Hitstun, h.P1.State);
            Assert.IsNull(SkillSetup.FirstObject(h.S, SkillObjectKind.Projectile), "Попавший снаряд исчезает");
        }

        [Test]
        public void Bolt_StopsAtObstacle()
        {
            var setup = SimHarness.Duel(6f);
            setup.Arena.Obstacles = new[] { Obstacle.Box(FixVec2.FromFloat(0f, 3f), FixVec2.FromFloat(2f, 0.3f)) };
            var h = SkillSetup.Duel(setup: setup);
            h.Tick(SkillSetup.Cast(CommandKind.Skill1, 0f, 1f));
            h.TickUntil(() => h.Has(SimEventType.ProjectileExpired), 80);
            h.Ticks(30);
            Assert.AreEqual(h.Spec1.MaxHealth, h.P1.Health);
            Assert.IsFalse(h.Has(SimEventType.Hit));
        }

        [Test]
        public void Bolt_FliesOnlyItsRange()
        {
            var h = SkillSetup.Duel(30f);
            var bolt = h.Spec0.Skill(0);
            h.Tick(SkillSetup.Cast(CommandKind.Skill1, 0f, 1f));
            h.TickUntil(() => h.Has(SimEventType.ProjectileExpired), 120);
            var e = h.Events.First(x => x.Type == SimEventType.ProjectileExpired);
            float flown = e.Position.Y.ToFloat();
            Assert.AreEqual(bolt.Range.ToFloat(), flown, 0.6f);
        }

        [Test]
        public void Parry_ReflectsBolt_BackIntoTheCaster()
        {
            var h = SkillSetup.Duel(6f);
            h.Tick(SkillSetup.Cast(CommandKind.Skill1));
            // Парируем, когда снаряд подлетел почти вплотную.
            h.TickUntil(() =>
            {
                var p = SkillSetup.FirstObject(h.S, SkillObjectKind.Projectile);
                return p.HasValue && (h.P1.Position - p.Value.Position).Magnitude < Fix.FromFloat(1.6f);
            }, 80);
            h.Tick(default, SimHarness.Cmd(CommandKind.Parry));
            h.TickUntil(() => h.Has(SimEventType.ProjectileReflected), 15);
            Assert.AreEqual(h.Spec1.MaxHealth, h.P1.Health);

            h.TickUntil(() => h.Has(SimEventType.Hit), 80);
            Assert.Less(h.P0.Health.Raw, h.Spec0.MaxHealth.Raw, "Отражённый снаряд бьёт того, кто его бросил");
            Assert.AreEqual(1, h.Events.First(e => e.Type == SimEventType.Hit).Actor);
        }

        [Test]
        public void ReflectedBolt_KeepsCastersData_WhenReflectorHasAnotherSkillInThatSlot()
        {
            var h = SkillSetup.Duel(6f);
            // У отражающего в слоте 1 — телепорт: отражённый снаряд всё равно должен остаться Искрой бросившего.
            h.Spec1.Skills = new[] { TestSkills.Blink(), TestSkills.Bolt(), TestSkills.Nova() };
            h.Tick(SkillSetup.Cast(CommandKind.Skill1));
            h.TickUntil(() =>
            {
                var p = SkillSetup.FirstObject(h.S, SkillObjectKind.Projectile);
                return p.HasValue && (h.P1.Position - p.Value.Position).Magnitude < Fix.FromFloat(1.6f);
            }, 80);
            h.Tick(default, SimHarness.Cmd(CommandKind.Parry));
            h.TickUntil(() => h.Has(SimEventType.ProjectileReflected), 15);
            var reflected = h.Events.First(e => e.Type == SimEventType.ProjectileReflected);
            Assert.AreEqual(1, reflected.Actor);
            Assert.AreEqual(0, reflected.Caster, "Событие отражения называет, чей это скилл");

            h.TickUntil(() => h.Has(SimEventType.Hit), 80);
            Assert.AreEqual(h.Spec0.MaxHealth - h.Spec0.Skill(0).Damage, h.P0.Health, "Урон — Искры бросившего, а не скилла слота отражающего");
        }

        [Test]
        public void Dodge_PassesThroughBolt()
        {
            var h = SkillSetup.Duel(6f);
            h.Tick(SkillSetup.Cast(CommandKind.Skill1));
            h.TickUntil(() =>
            {
                var p = SkillSetup.FirstObject(h.S, SkillObjectKind.Projectile);
                return p.HasValue && (h.P1.Position - p.Value.Position).Magnitude < Fix.FromFloat(1.4f);
            }, 80);
            h.Tick(default, SimHarness.Cmd(CommandKind.Dodge, 0f, 1f)); // назад, по линии полёта
            h.Ticks(60);
            Assert.IsTrue(h.Has(SimEventType.Evaded));
            Assert.AreEqual(h.Spec1.MaxHealth, h.P1.Health);
        }

        [Test]
        public void BlockedBolt_ChipsAndDrainsStamina()
        {
            var h = SkillSetup.Duel(6f);
            h.Tick(SkillSetup.Cast(CommandKind.Skill1), SimHarness.Cmd(CommandKind.BlockStart));
            h.TickUntil(() => h.Has(SimEventType.Blocked), 80);
            var bolt = h.Spec0.Skill(0);
            Assert.AreEqual(h.Spec1.MaxHealth - bolt.Damage * h.Spec1.BlockDamageMultiplier, h.P1.Health);
            Assert.AreEqual(h.Spec1.MaxStamina - bolt.GuardDamage, h.P1.Stamina);
            Assert.AreEqual(ActionState.Block, h.P1.State);
        }
    }

    public class BlinkAndZoneTests
    {
        [Test]
        public void Blink_TeleportsByAim_ThroughObstacles()
        {
            var setup = SimHarness.Duel(10f);
            setup.Arena.Obstacles = new[] { Obstacle.Box(FixVec2.FromFloat(2f, 0f), FixVec2.FromFloat(0.3f, 3f)) };
            var h = SkillSetup.Duel(setup: setup);
            var blink = h.Spec0.Skill(1);
            h.Tick(SkillSetup.Cast(CommandKind.Skill2, 1f, 0f)); // полная дальность вправо, сквозь стенку
            h.TickUntil(() => h.P0.SkillFired, 20);
            Assert.AreEqual(blink.Range.ToFloat(), h.P0.Position.X.ToFloat(), 1e-2f);

            var half = SkillSetup.Duel(10f);
            half.Tick(SkillSetup.Cast(CommandKind.Skill2, -0.5f, 0f));
            half.TickUntil(() => half.P0.SkillFired, 20);
            Assert.AreEqual(-blink.Range.ToFloat() * 0.5f, half.P0.Position.X.ToFloat(), 0.05f, "Прицел — доля дальности");
        }

        [Test]
        public void Blink_QuickCast_GoesByJoystick()
        {
            var h = SkillSetup.Duel(10f);
            var t = SkillSetup.Cast(CommandKind.Skill2);
            t.SetMove(-1f, 0f);
            h.Tick(t);
            h.TickUntil(() => h.P0.SkillFired, 20);
            Assert.Less(h.P0.Position.X.ToFloat(), -3f);
        }

        [Test]
        public void Blink_StartupIsInvulnerable()
        {
            var h = SkillSetup.Duel(1.2f);
            h.Tick(default, SimHarness.Cmd(CommandKind.LightAttack));
            h.Ticks(h.Spec1.Light.StartupTicks - 2);
            h.Tick(SkillSetup.Cast(CommandKind.Skill2, 1f, 0f));
            h.Ticks(3);
            Assert.AreEqual(h.Spec0.MaxHealth, h.P0.Health);
            Assert.IsTrue(h.Has(SimEventType.Evaded) || !h.Has(SimEventType.Hit));
        }

        [Test]
        public void Nova_TelegraphsThenExplodes_OnTheOpponent()
        {
            var h = SkillSetup.Duel(5f);
            var nova = h.Spec0.Skill(2);
            h.Tick(SkillSetup.Cast(CommandKind.Ultimate)); // автоприцел — на противника
            h.TickUntil(() => h.P0.SkillFired, 30);
            var zone = SkillSetup.FirstObject(h.S, SkillObjectKind.Zone);
            Assert.IsTrue(zone.HasValue);
            Assert.AreEqual(5f, zone.Value.Position.Y.ToFloat(), 0.05f);
            Assert.AreEqual(h.Spec1.MaxHealth, h.P1.Health, "До взрыва урона нет");

            h.TickUntil(() => h.Has(SimEventType.ZoneDetonated), nova.ZoneDelayTicks + 2);
            Assert.AreEqual(h.Spec1.MaxHealth - nova.Damage, h.P1.Health);
            Assert.IsNull(SkillSetup.FirstObject(h.S, SkillObjectKind.Zone));
        }

        [Test]
        public void Nova_CanBeWalkedOutOf_WhenReactingToTheCast()
        {
            // Область поставлена в точку, где стоит противник; он уходит, увидев начало каста (startup + задержка взрыва).
            var h = SkillSetup.Duel(5f);
            var nova = h.Spec0.Skill(2);
            h.Tick(SkillSetup.Cast(CommandKind.Ultimate, 0f, 5f / nova.Range.ToFloat()));
            for (int i = 0; i < 90 && !h.Has(SimEventType.ZoneDetonated); i++) h.Tick(default, SimHarness.Move(1f, 0f));
            Assert.IsTrue(h.Has(SimEventType.ZoneDetonated));
            Assert.AreEqual(h.Spec1.MaxHealth, h.P1.Health, "Каст и предупреждение области дают время уйти");
        }

        [Test]
        public void Nova_CanBeDodgedOnDetonation()
        {
            var h = SkillSetup.Duel(5f);
            h.Tick(SkillSetup.Cast(CommandKind.Ultimate));
            h.TickUntil(() => SkillSetup.FirstObject(h.S, SkillObjectKind.Zone)?.TicksLeft == 3, 60);
            h.Tick(default, SimHarness.Cmd(CommandKind.Dodge, 1f, 0f));
            h.TickUntil(() => h.Has(SimEventType.ZoneDetonated), 10);
            Assert.IsTrue(h.Has(SimEventType.Evaded));
            Assert.AreEqual(h.Spec1.MaxHealth, h.P1.Health);
        }

        [Test]
        public void PreviewAim_MatchesWhereTheSkillLands()
        {
            var h = SkillSetup.Duel(10f);
            var aim = TickInput.Dequantize(TickInput.Quantize(0.6f), TickInput.Quantize(0.3f));
            var dir = h.Sim.PreviewAim(h.S, 0, 1, aim, out var dist);
            var expected = h.P0.Position + dir * dist;
            var t = SkillSetup.Cast(CommandKind.Skill2, 0.6f, 0.3f);
            h.Tick(t);
            h.TickUntil(() => h.P0.SkillFired, 20);
            Assert.AreEqual(expected.X.ToFloat(), h.P0.Position.X.ToFloat(), 1e-3f);
            Assert.AreEqual(expected.Y.ToFloat(), h.P0.Position.Y.ToFloat(), 1e-3f);
        }
    }

    public class BalanceTests
    {
        [Test]
        public void HeavyPressure_BreaksHeldBlock_ThenGuardRecovers()
        {
            var h = new SimHarness(SimHarness.Duel());
            h.Tick(default, SimHarness.Cmd(CommandKind.BlockStart));
            int guard = 0;
            for (int i = 0; i < 1200 && !h.Has(SimEventType.GuardBreak); i++)
            {
                var input = h.P0.State == ActionState.Idle ? SimHarness.Cmd(CommandKind.HeavyAttack) : default;
                h.Tick(input);
                guard = i;
            }
            Assert.IsTrue(h.Has(SimEventType.GuardBreak), "Тяжёлые удары должны пробивать удерживаемый блок");
            Assert.AreEqual(ActionState.GuardBroken, h.P1.State);
            Assert.Less(guard, 600, "Пробитие — за разумное время, а не за минуту давления");

            h.TickUntil(() => h.P1.State != ActionState.GuardBroken, 120);
            Assert.AreEqual(ActionState.Block, h.P1.State, "Палец держит блок — боец снова в блоке");
            Assert.GreaterOrEqual(h.P1.Stamina.Raw, (h.Spec1.MaxStamina / 2).Raw, "После пробития блок восстанавливается наполовину");
        }

        [Test]
        public void GuardBreak_GivesAGuaranteedPunish()
        {
            var h = new SimHarness(SimHarness.Duel());
            h.P1.Stamina = Fix.FromInt(3);
            h.P1.StaminaRegenDelay = 600; // без регенерации до удара
            h.HoldBlock(1);
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.Has(SimEventType.GuardBreak), 20);
            int adv = h.Events.First(e => e.Type == SimEventType.GuardBreak).FrameAdvantage;
            Assert.Greater(adv, h.Spec0.Heavy.StartupTicks, "Пробитие должно гарантировать даже тяжёлый удар");
        }

        [Test]
        public void LightChain_AgainstTheWall_IsNotInfinite()
        {
            // Цель у стены (отбрасывание гаснет), держит блок с первого удара; атакующий жмёт удар каждый тик.
            var setup = SimHarness.Duel(1.2f);
            setup.Arena = new ArenaSpec { Min = FixVec2.FromFloat(-5f, -5f), Max = FixVec2.FromFloat(5f, 1.7f) };
            var h = new SimHarness(setup);
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.Has(SimEventType.Hit), 20);
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack), SimHarness.Cmd(CommandKind.BlockStart));
            for (int i = 0; i < 400 && !h.Has(SimEventType.Blocked); i++) h.Tick(SimHarness.Cmd(CommandKind.LightAttack));

            int hits = h.Events.Count(e => e.Type == SimEventType.Hit);
            Assert.IsTrue(h.Has(SimEventType.Blocked), $"Серия у стены не прервалась за {hits} попаданий");
            Assert.LessOrEqual(hits, 6);
        }

        [Test]
        public void ComboDamage_ScalesAfterTwoHits()
        {
            var setup = SimHarness.Duel(1.2f);
            setup.Arena = new ArenaSpec { Min = FixVec2.FromFloat(-5f, -5f), Max = FixVec2.FromFloat(5f, 1.7f) };
            var h = new SimHarness(setup);
            for (int i = 0; i < 200 && h.Events.Count(e => e.Type == SimEventType.Hit) < 3; i++)
                h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            var hits = h.Events.Where(e => e.Type == SimEventType.Hit).ToArray();
            Assert.GreaterOrEqual(hits.Length, 3);
            var dmg = h.Spec0.Light.Damage;
            Assert.AreEqual(dmg, hits[0].Amount);
            Assert.AreEqual(dmg, hits[1].Amount);
            Assert.AreEqual(3, hits[2].Combo);
            Assert.Less(hits[2].Amount.Raw, dmg.Raw, "Третье попадание серии — со сниженным уроном");
        }

        [Test]
        public void DealingAndTakingDamage_GivesMana()
        {
            var h = new SimHarness(SimHarness.Duel());
            h.P0.Mana = h.P1.Mana = Fix.Zero;
            h.Tick(SimHarness.Cmd(CommandKind.LightAttack));
            h.TickUntil(() => h.Has(SimEventType.Hit), 20);
            var regen = h.Spec0.ManaRegen * h.S.Tick; // грубо: регенерация за прошедшие тики
            Assert.Greater(h.P0.Mana.Raw, (h.Spec0.Light.Damage * h.Spec0.ManaPerDamageDealt).Raw - 1);
            Assert.Greater(h.P1.Mana.Raw, regen.Raw, "Получивший урон тоже копит ману");
        }
    }

    public class SkillDeterminismTests
    {
        private static readonly CommandKind[] Kinds =
        {
            CommandKind.LightAttack, CommandKind.HeavyAttack, CommandKind.BlockStart, CommandKind.BlockEnd,
            CommandKind.Parry, CommandKind.Dodge, CommandKind.Skill1, CommandKind.Skill2, CommandKind.Ultimate,
            CommandKind.SkillCancel,
        };

        private static SimSetup Setup()
        {
            var setup = new SimSetup();
            setup.Rules.RoundTicks = 900;
            setup.Rules.CountdownTicks = 10;
            setup.Rules.RoundOverTicks = 10;
            foreach (var f in setup.Fighters) f.Skills = TestSkills.Kit();
            return setup;
        }

        private static List<TickInput> RandomInputs(int seed, int count)
        {
            var rnd = new System.Random(seed);
            var list = new List<TickInput>(count);
            for (int i = 0; i < count; i++)
            {
                var t = new TickInput();
                if (rnd.NextDouble() < 0.7) t.SetMove((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1));
                if (rnd.NextDouble() < 0.3)
                    t.SetAim((AimState)rnd.Next(3), (float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1));
                if (rnd.NextDouble() < 0.15)
                {
                    var kind = Kinds[rnd.Next(Kinds.Length)];
                    t.Add(FightSimulation.IsSkill(kind) && rnd.NextDouble() < 0.5
                        ? SimCommand.SkillWithInputAim(kind)
                        : SimCommand.Of(kind, (float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1)));
                }
                list.Add(t);
            }
            return list;
        }

        [Test]
        public void SyncTest_WithSkills_TwoCopiesMatchEveryTick()
        {
            const int ticks = 6000;
            var p0 = RandomInputs(11, ticks);
            var p1 = RandomInputs(12, ticks);
            var a = new FightSimulation(Setup());
            var b = new FightSimulation(Setup());
            var sa = a.CreateInitialState();
            var sb = b.CreateInitialState();

            int skills = 0, skillHits = 0;
            for (int i = 0; i < ticks; i++)
            {
                // Вторая половина — маг против агрессора: снаряды, области, телепорты.
                var in0 = i < ticks / 2 ? p0[i] : TrainingBot.Think(sa, a.Setup, 0, BotMode.Zoner);
                var in1 = i < ticks / 2 ? p1[i] : TrainingBot.Think(sa, a.Setup, 1, BotMode.Aggressive);
                if (sa.Phase == MatchPhase.MatchOver) in0.Add(new SimCommand(CommandKind.Restart));
                a.Step(sa, in0, in1);
                b.Step(sb, in0, in1);
                skills += a.Events.Count(e => e.Type == SimEventType.SkillFired);
                skillHits += a.Events.Count(e => (e.Type == SimEventType.Hit || e.Type == SimEventType.Blocked) && e.Slot >= 0);
                Assert.AreEqual(sa.ComputeHash(), sb.ComputeHash(), $"Рассинхрон на тике {sa.Tick}");
            }
            Assert.Greater(skills, 20, "Тест должен включать скиллы");
            Assert.Greater(skillHits, 3, "Тест должен включать попадания скиллами");
        }

        [Test]
        public void Rollback_WithSkillObjects_GivesTheSameResult()
        {
            const int ticks = 900, rollback = 10;
            var p0 = RandomInputs(13, ticks);
            var p1 = RandomInputs(14, ticks);
            var sim = new FightSimulation(Setup());
            var s = sim.CreateInitialState();
            var history = new StateHistory(32);
            var hashes = new Dictionary<int, ulong>();
            history.Save(s);
            for (int i = 0; i < ticks; i++)
            {
                var in0 = i % 2 == 0 ? p0[i] : TrainingBot.Think(s, sim.Setup, 0, BotMode.Zoner);
                p0[i] = in0;
                sim.Step(s, in0, p1[i]);
                history.Save(s);
                hashes[s.Tick] = s.ComputeHash();
                if (i < rollback || i % 23 != 0) continue;
                var replay = new GameState();
                Assert.IsTrue(history.TryLoad(s.Tick - rollback, replay));
                for (int k = i - rollback + 1; k <= i; k++)
                {
                    sim.Step(replay, p0[k], p1[k]);
                    Assert.AreEqual(hashes[replay.Tick], replay.ComputeHash(), $"Пересчёт разошёлся на тике {replay.Tick}");
                }
            }
        }

        [Test]
        public void SkillObjects_AreInTheSnapshot()
        {
            var h = SkillSetup.Duel(8f);
            h.Tick(SkillSetup.Cast(CommandKind.Skill1));
            h.TickUntil(() => h.P0.SkillFired, 20);
            var snap = h.S.Clone();
            h.Ticks(5);
            Assert.AreNotEqual(snap.Objects[0].Position, h.S.Objects[0].Position);
            var a = snap.ComputeHash();
            h.S.CopyFrom(snap);
            Assert.AreEqual(a, h.S.ComputeHash());
        }

        [Test]
        public void ZonerBot_KeepsDistance_AndHitsWithSkills()
        {
            var setup = SimHarness.Duel(3f);
            foreach (var f in setup.Fighters) f.Skills = TestSkills.Kit();
            var h = new SimHarness(setup);
            for (int i = 0; i < 900 && h.P0.Health == h.Spec0.MaxHealth; i++)
                h.Tick(default, TrainingBot.Think(h.S, h.Sim.Setup, 1, BotMode.Zoner));
            Assert.Less(h.P0.Health.Raw, h.Spec0.MaxHealth.Raw, "Маг должен попадать скиллами");
            Assert.IsTrue(h.Events.Any(e => e.Type == SimEventType.Hit && e.Slot >= 0));
        }
    }

    public class SkillAimRecognizerTests
    {
        private const float PxPerMm = 10f; // полная дальность 12 мм = 120 px, мёртвая зона 2.5 мм = 25 px
        private GestureSettings _settings;
        private SkillAimRecognizer _r;
        private readonly List<InputCommand> _cmds = new();
        private readonly List<SkillAimInputEvent> _aims = new();
        private Vector2 _center;

        [SetUp]
        public void SetUp()
        {
            _cmds.Clear();
            _aims.Clear();
            _settings = ScriptableObject.CreateInstance<GestureSettings>();
            _r = new SkillAimRecognizer(_settings, _cmds.Add, _aims.Add) { PixelsPerMm = PxPerMm, ScreenWidth = 1920f };
            _center = SkillButtonLayout.ButtonCenter(_settings, 0, 1920f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        [Test]
        public void Press_OnlyAims_DoesNotCast()
        {
            _r.Begin(0, _center, 5.0);
            Assert.IsEmpty(_cmds, "Касание кнопки — прицел, каст будет при отпускании");
            Assert.IsTrue(_r.IsAiming);
            Assert.AreEqual(SkillAimPhase.Held, _aims.Last().Phase);
            Assert.AreEqual(Vector2.zero, _aims.Last().Aim, "Без сдвига — автоприцел");
        }

        [Test]
        public void Drag_SetsAimDirectionAndRangeFraction()
        {
            _r.Begin(0, _center, 5.0);
            _r.Move(_center + new Vector2(60f, 0f), 5.05);
            Assert.AreEqual(0.5f, _aims.Last().Aim.magnitude, 1e-3f);
            Assert.Greater(_aims.Last().Aim.x, 0.49f);

            _r.Move(_center + new Vector2(0f, 500f), 5.1);
            Assert.AreEqual(1f, _aims.Last().Aim.magnitude, 1e-3f, "Дальше радиуса — полная дальность");

            _r.Move(_center + new Vector2(10f, 10f), 5.15);
            Assert.AreEqual(Vector2.zero, _aims.Last().Aim, "В мёртвой зоне — автоприцел");
        }

        [Test]
        public void Release_CastsWithLastAim()
        {
            _r.Begin(0, _center, 5.0);
            _r.Move(_center + new Vector2(-120f, 0f), 5.05);
            _r.End(_center + new Vector2(-120f, 0f), 5.1);
            Assert.AreEqual(CommandType.Ability1, _cmds.Single().Type);
            Assert.AreEqual(5.1, _cmds[0].InputTime, "Время команды — момент отпускания");
            Assert.AreEqual(SkillAimPhase.Released, _aims.Last().Phase);
            Assert.Less(_aims.Last().Aim.x, -0.99f);
            Assert.IsFalse(_r.IsAiming);
        }

        [Test]
        public void ReleaseInCancelZone_DoesNotCast()
        {
            _r.Begin(2, SkillButtonLayout.ButtonCenter(_settings, 2, 1920f), 5.0);
            var cancel = SkillButtonLayout.ToScreen(_settings.SkillCancelOffset, 1920f);
            _r.Move(cancel, 5.1);
            Assert.IsTrue(_aims.Last().InCancelZone);
            _r.End(cancel, 5.2);
            Assert.IsEmpty(_cmds);
            Assert.AreEqual(SkillAimPhase.Canceled, _aims.Last().Phase);
        }

        [Test]
        public void SystemCancel_DoesNotCast()
        {
            _r.Begin(1, _center, 5.0);
            _r.Cancel(5.1);
            Assert.IsEmpty(_cmds);
            Assert.AreEqual(SkillAimPhase.Canceled, _aims.Last().Phase);
            Assert.IsFalse(_r.IsAiming);
        }

        [Test]
        public void ButtonLayout_LeavesRoomToAimTowardsTheEdges()
        {
            // Телефон пользователя (2800 px по ширине, ~450 dpi): полный сдвиг прицела от любой кнопки вправо и вниз
            // остаётся на экране, а полный сдвиг в любую сторону не заезжает в зону отмены.
            const float width = 2800f, pxPerMm = 450f / 25.4f;
            float aim = _settings.SkillAimRadiusMm * pxPerMm;
            float cancelR = _settings.SkillCancelRadius * SkillButtonLayout.Scale(width);
            var cancel = SkillButtonLayout.ToScreen(_settings.SkillCancelOffset, width);
            for (int slot = 0; slot < SkillButtonLayout.SlotCount; slot++)
            {
                var c = SkillButtonLayout.ButtonCenter(_settings, slot, width);
                Assert.Less(c.x + aim, width, $"Слот {slot}: вправо не прицелиться");
                Assert.Greater(c.y - aim, 0f, $"Слот {slot}: вниз не прицелиться");
                Assert.Greater(Vector2.Distance(c, cancel), aim + cancelR, $"Слот {slot}: полный прицел задевает отмену");
            }
        }

        [Test]
        public void ButtonLayout_HitsButtonsOnly()
        {
            for (int slot = 0; slot < SkillButtonLayout.SlotCount; slot++)
                Assert.AreEqual(slot, SkillButtonLayout.HitButton(_settings, SkillButtonLayout.ButtonCenter(_settings, slot, 1920f), 1920f));
            Assert.AreEqual(-1, SkillButtonLayout.HitButton(_settings, new Vector2(1300f, 600f), 1920f), "Середина правой зоны — удары");
            // Масштаб по ширине: на экране 2400 px кнопка там же относительно угла.
            var big = SkillButtonLayout.ButtonCenter(_settings, 1, 2400f);
            Assert.AreEqual(1, SkillButtonLayout.HitButton(_settings, big, 2400f));
        }

        [Test]
        public void ButtonLayout_EmptySlot_IsNotAButton()
        {
            int onlyUlt = 1 << (int)SkillSlot.Ultimate;
            var skill1 = SkillButtonLayout.ButtonCenter(_settings, 0, 1920f);
            Assert.AreEqual(-1, SkillButtonLayout.HitButton(_settings, skill1, 1920f, onlyUlt), "Пустой слот — касание уходит в удары");
            var ult = SkillButtonLayout.ButtonCenter(_settings, 2, 1920f);
            Assert.AreEqual(2, SkillButtonLayout.HitButton(_settings, ult, 1920f, onlyUlt));
            Assert.AreEqual(-1, SkillButtonLayout.HitButton(_settings, ult, 1920f, 0), "Персонаж без скиллов — кнопок нет");
        }
    }
}
