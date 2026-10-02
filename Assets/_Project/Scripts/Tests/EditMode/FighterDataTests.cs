using System.Collections.Generic;
using System.Linq;
using Game.Combat;
using Game.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// Данные настоящих персонажей: ростер полон и без повторов, ассеты без ошибок, каждый скилл каждого персонажа
    /// реально применяется, а бот играет персонажем без исключений. Новому персонажу отдельные тесты не нужны —
    /// достаточно добавить его в ростер.
    /// </summary>
    public class FighterDataTests
    {
        private static FighterRoster Roster()
        {
            var roster = FighterRoster.Load();
            Assert.IsNotNull(roster, $"Нет ростера Resources/{FighterRoster.ResourcePath}");
            return roster;
        }

        private static IEnumerable<FighterDefinition> Fighters() => Roster().Fighters.Where(f => f != null);

        [Test]
        public void AttackSpeed_ScalesTheWholeSwing_ButNotTheStun()
        {
            var data = ScriptableObject.CreateInstance<AttackData>();
            var def = ScriptableObject.CreateInstance<FighterDefinition>();
            try
            {
                data.StartupFrames = 10;
                data.ActiveFrames = 3;
                data.RecoveryFrames = 20;
                data.HitstunFrames = 15;
                var normal = data.ToSpec(AttackKind.Light);
                var slow = data.ToSpec(AttackKind.Light, 0.5f);
                var fast = data.ToSpec(AttackKind.Light, 2f);

                Assert.AreEqual(SimTime.Frames(20), slow.StartupTicks);
                Assert.AreEqual(SimTime.Frames(6), slow.ActiveTicks);
                Assert.AreEqual(SimTime.Frames(40), slow.RecoveryTicks);
                Assert.AreEqual(SimTime.Frames(5), fast.StartupTicks);
                Assert.AreEqual(SimTime.Frames(2), fast.ActiveTicks, "1.5 кадра округляются вверх");
                Assert.AreEqual(SimTime.Frames(10), fast.RecoveryTicks);
                Assert.AreEqual(normal.HitstunTicks, slow.HitstunTicks, "Оглушение цели от скорости атаки не зависит");
                Assert.AreEqual(normal.HitstunTicks, fast.HitstunTicks);

                def.LightAttack = data;
                def.HeavyAttack = data;
                def.AttackSpeed = 0.5f;
                var spec = def.ToSpec();
                Assert.AreEqual(slow.TotalTicks, spec.Light.TotalTicks);
                Assert.AreEqual(slow.TotalTicks, spec.Heavy.TotalTicks);
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void Roster_IsValid_AndContainsEveryFighterAsset()
        {
            var roster = Roster();
            CollectionAssert.IsEmpty(roster.Validate());
            foreach (var f in Resources.LoadAll<FighterDefinition>("Fighters"))
                CollectionAssert.Contains(roster.Fighters, f, $"«{f.name}» не добавлен в ростер");
            Assert.IsNotNull(roster.Find("tester"));
        }

        [Test]
        public void EveryFighter_HasNoDataProblems()
        {
            foreach (var f in Fighters())
                CollectionAssert.IsEmpty(f.Validate(), f.name);
        }

        [Test]
        public void CommonRulesAsset_Exists()
        {
            Assert.IsNotNull(Resources.Load<FighterCommonData>(FighterCommonData.ResourcePath));
        }

        [Test]
        public void EveryFighter_EverySkill_Fires()
        {
            foreach (var def in Fighters())
            {
                for (int slot = 0; slot < FighterSpec.SkillSlots; slot++)
                {
                    if (def.Skill(slot) == null) continue;
                    var h = DuelOf(def);
                    h.Tick(SimHarness.Cmd(FightSimulation.SkillCommand(slot)));
                    Assert.AreEqual(ActionState.Cast, h.P0.State, $"{def.name}, слот {slot}: каст не начался");
                    h.TickUntil(() => h.Has(SimEventType.SkillFired), 600);
                }
            }
        }

        [Test]
        public void EveryFighter_PlayableByBots()
        {
            foreach (var def in Fighters())
            {
                foreach (var mode in new[] { BotMode.Aggressive, BotMode.Zoner })
                {
                    var h = DuelOf(def);
                    for (int t = 0; t < SimTime.Seconds(20f); t++)
                        h.Tick(TrainingBot.Think(h.S, h.Sim.Setup, 0, mode), TrainingBot.Think(h.S, h.Sim.Setup, 1, mode));
                }
            }
        }

        /// <summary> Персонаж против себя, полная мана, все скиллы готовы. </summary>
        private static SimHarness DuelOf(FighterDefinition def)
        {
            var setup = SimHarness.Duel(3f);
            for (int i = 0; i < GameState.FighterCount; i++)
            {
                setup.Fighters[i] = def.ToSpec();
                setup.Fighters[i].StartMana = setup.Fighters[i].MaxMana;
            }
            var h = new SimHarness(setup);
            for (int i = 0; i < GameState.FighterCount; i++)
                for (int slot = 0; slot < FighterSpec.SkillSlots; slot++) h.S.Fighters[i].SetCooldown(slot, 0);
            return h;
        }
    }
}
