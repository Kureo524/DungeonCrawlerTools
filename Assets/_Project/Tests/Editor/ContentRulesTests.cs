using System.Linq;
using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Progression;
using TheDungeon.Editor.Validation;
using UnityEngine;

namespace TheDungeon.Editor.Tests
{
    public class ContentRulesTests
    {
        ForgeDesignLimits limits;

        [SetUp]
        public void SetUp() => limits = ScriptableObject.CreateInstance<ForgeDesignLimits>();

        [TearDown]
        public void TearDown() => TestData.Destroy(limits);

        ValidationContext Context(ItemData[] items = null, LootTable[] tables = null, PlayerProgression[] progressions = null) =>
            new(items ?? new ItemData[0], new EnemyData[0], tables ?? new LootTable[0], new BiomeData[0], new RoomData[0], new FloorData[0],
                progressions ?? new PlayerProgression[0], limits);

        [Test]
        public void LootTable_WithMissingItem_IsAnError()
        {
            var table = TestData.Create<LootTable>("LT_Test", so =>
            {
                var entries = so.FindProperty("entries");
                entries.arraySize = 1;
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("weight").intValue = 10;
            });

            var issues = new LootTableRule().Run(Context(tables: new[] { table })).ToList();

            Assert.IsTrue(issues.Any(i => i.Severity == Severity.Error && i.Message.Contains("aucun objet")));
            TestData.Destroy(table);
        }

        [Test]
        public void Item_WithStatAboveRarityMax_IsAnError()
        {
            var item = TestData.Create<ItemData>("ITM_Test", so =>
            {
                so.FindProperty("displayName").stringValue = "Test";
                so.FindProperty("price").intValue = 10;
                var stats = so.FindProperty("stats");
                stats.arraySize = 1;
                stats.GetArrayElementAtIndex(0).FindPropertyRelative("value").intValue = 99;
            });

            var issues = new ItemRule().Run(Context(items: new[] { item })).ToList();

            Assert.IsTrue(issues.Any(i => i.Severity == Severity.Error && i.Message.Contains("trop élevé")));
            TestData.Destroy(item);
        }

        [Test]
        public void Progression_WithDecreasingHealth_IsAnError()
        {
            var progression = TestData.Create<PlayerProgression>("PRG_Player", so =>
            {
                ProgressionEditing.Reset(so, limits);
                ProgressionEditing.SetValues(so, 0, Enumerable.Range(0, limits.maxLevel).Select(i => i == 5 ? 100f : 100f + i * 10).ToList());
            });

            var issues = new ProgressionRule().Run(Context(progressions: new[] { progression })).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Message.Contains("baisse au niveau 6")));
            TestData.Destroy(progression);
        }

        [Test]
        public void Progression_WithoutXpStat_IsAnError()
        {
            var progression = TestData.Create<PlayerProgression>("PRG_Player", so =>
            {
                ProgressionEditing.Reset(so, limits);
                ProgressionEditing.RemoveStat(so, 3);
            });

            var issues = new ProgressionRule().Run(Context(progressions: new[] { progression })).ToList();

            Assert.IsTrue(issues.Any(i => i.Severity == Severity.Error && i.Message.Contains("XP pour le niveau suivant")));
            TestData.Destroy(progression);
        }

        [Test]
        public void Progression_SkillUpgradedBeforeUnlock_IsAnError()
        {
            var progression = TestData.Create<PlayerProgression>("PRG_Player", so =>
            {
                ProgressionEditing.Reset(so, limits);
                var skill = ProgressionEditing.Skill("Ruée", SkillEffect.DashCooldown, Color.white, 10f, 20f);
                ProgressionEditing.AddSkill(so, skill);
                ProgressionEditing.AddReward(so, new LevelReward { level = 4, type = RewardType.UpgradeSkill, skillId = skill.id });
                ProgressionEditing.AddReward(so, new LevelReward { level = 6, type = RewardType.UnlockSkill, skillId = skill.id });
            });

            var issues = new ProgressionRule().Run(Context(progressions: new[] { progression })).ToList();

            Assert.IsTrue(issues.Any(i => i.Severity == Severity.Error && i.Message.Contains("améliorée avant d'être débloquée")));
            TestData.Destroy(progression);
        }

        [Test]
        public void Progression_MechanicsOnSameKeyOrCostingANonGauge_AreErrors()
        {
            var progression = TestData.Create<PlayerProgression>("PRG_Player", so =>
            {
                ProgressionEditing.Reset(so, limits);
                var attack = so.FindProperty("stats").GetArrayElementAtIndex(1).FindPropertyRelative("id").stringValue;
                ProgressionEditing.AddMechanic(so, ProgressionEditing.Mechanic("Frappe", MechanicAction.Projectile, MechanicInput.Primary, Color.white, attack, 5f));
            });

            var issues = new ProgressionRule().Run(Context(progressions: new[] { progression })).ToList();

            Assert.IsTrue(issues.Any(i => i.Severity == Severity.Error && i.Message.Contains("Plusieurs mécaniques utilisent la touche")));
            Assert.IsTrue(issues.Any(i => i.Severity == Severity.Error && i.Message.Contains("n'est pas une jauge")));
            TestData.Destroy(progression);
        }

        [Test]
        public void Progression_LevelCount_CanChangeWithinTheCap()
        {
            var longer = TestData.Create<PlayerProgression>("PRG_Player", so =>
            {
                ProgressionEditing.Reset(so, limits);
                ProgressionEditing.SetLevelCount(so, 40);
            });
            var single = TestData.Create<PlayerProgression>("PRG_Player", so =>
            {
                ProgressionEditing.Reset(so, limits);
                ProgressionEditing.SetLevelCount(so, 1);
            });

            var rule = new ProgressionRule();
            Assert.IsFalse(rule.Run(Context(progressions: new[] { longer })).Any(i => i.Message.Contains("niveaux :")));
            Assert.IsTrue(rule.Run(Context(progressions: new[] { single })).Any(i => i.Severity == Severity.Error && i.Message.Contains("il en faut entre 2")));
            TestData.Destroy(longer);
            TestData.Destroy(single);
        }

        [Test]
        public void Progression_PointsWithoutAttributes_IsAnError()


        {
            var progression = TestData.Create<PlayerProgression>("PRG_Player", so =>
            {
                ProgressionEditing.Reset(so, limits);
                so.FindProperty("attributes").arraySize = 0;
                so.ApplyModifiedProperties();
            });

            var issues = new ProgressionRule().Run(Context(progressions: new[] { progression })).ToList();

            Assert.IsTrue(issues.Any(i => i.Severity == Severity.Error && i.Message.Contains("aucun attribut")));
            TestData.Destroy(progression);
        }

        [Test]
        public void DefaultProgression_IsValid()

        {
            var progression = TestData.Create<PlayerProgression>("PRG_Player", so => ProgressionEditing.Reset(so, limits));

            var issues = new ProgressionRule().Run(Context(progressions: new[] { progression })).ToList();

            CollectionAssert.IsEmpty(issues.Select(i => i.Message));
            TestData.Destroy(progression);
        }

        [Test]
        public void LootTable_Probabilities_FollowWeights()
        {
            var table = TestData.Create<LootTable>("LT_Test", so =>
            {
                so.FindProperty("dropChance").floatValue = 1f;
                var entries = so.FindProperty("entries");
                entries.arraySize = 2;
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("weight").intValue = 3;
                entries.GetArrayElementAtIndex(1).FindPropertyRelative("weight").intValue = 1;
            });

            Assert.AreEqual(0.75f, table.GetProbability(table.Entries[0]), 0.001f);
            Assert.AreEqual(0.25f, table.GetProbability(table.Entries[1]), 0.001f);
            TestData.Destroy(table);
        }
    }
}
