using System.Linq;
using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
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
                var levels = so.FindProperty("levels");
                levels.arraySize = limits.maxLevel;
                for (var i = 0; i < levels.arraySize; i++)
                {
                    var level = levels.GetArrayElementAtIndex(i);
                    level.FindPropertyRelative("maxHealth").intValue = i == 5 ? 1 : 100 + i * 10;
                    level.FindPropertyRelative("damage").intValue = 5 + i;
                    level.FindPropertyRelative("xpRequired").intValue = 100 + i * 50;
                }
            });

            var issues = new ProgressionRule().Run(Context(progressions: new[] { progression })).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Message.Contains("PV baissent")));
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
