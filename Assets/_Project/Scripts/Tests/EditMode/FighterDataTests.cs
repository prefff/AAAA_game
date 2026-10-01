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
